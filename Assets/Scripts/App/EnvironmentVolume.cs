// 배경의 환경 볼륨(EnvironmentVolumeData)을 장면에 적용한다(Background.md 8장).
//   주광: 방향(회전)·색·세기·그림자 세기를 장면 방향광에 넣는다. 배경 원본 셰이더가 이 빛을 쓴다.
//   전역 셰이더 값: 하늘·구름·높이 안개·캐릭터 조명 등을 게임 셰이더의 전역 이름으로 넣는다. 이름은 볼륨 필드 이름과의 대응이다 [추정].
//   안개: FogColor·FogStartDistance·FogEndDistance 를 Unity 선형 안개로 켠다 [추정].
// 캐릭터 빛(앱 셰이더 MobiAtelier/Character 용 _MobiCharLightDir·_MobiCharLightColor):
//   색: 볼륨 CharacterLight.LightColor 의 알파가 0 보다 크면 그 색, 아니면 주광 색 × 세기.
//     원본 얼굴 셰이더(Skin_Face)가 이 규칙으로 움직인다 [확인: 2026-11-12 공간의 틈: 어둠 촬영. 캐릭터 조명·주광 중 하나만 빼면
//     얼굴이 다른 쪽 색으로 물들고, 둘 다 빼야 기본색. 피팅룸 주광 세기 0.4 는 얼굴 밝기에 영향 없음]. 몸·의상도 같은 색을 쓰게 맞춘다.
//   방향: 장면 처음의 방향광 방향을 유지한다. 원본 캐릭터 셰이더가 쓰는 방향은 [미확인]이고, 볼륨 주광은 위에서 거의 수직으로 비춘다.
// 볼륨이 없으면(색 배경·키 없음·읽기 실패) 장면 처음 조명으로 되돌리고 전역 값을 0, 안개를 끈다(이 기능 전과 같은 상태).
// 볼륨의 앰비언트 그라데이션(PostProcessing 섹션)·SSAO·바람·하늘 Unlit 색은 적용하지 않는다(ImplementationGaps.md).
using UnityEngine;

public static class EnvironmentVolume
{
    /// <summary>여러 키를 가진 볼륨 칸을 평가할 시각. 스튜디오 볼륨은 시각과 관계없다 [확인]. 다른 볼륨에서의 의미는 [추정].</summary>
    public const float StudioTime = 0.5f;

    static readonly int CharLightDirId = Shader.PropertyToID("_MobiCharLightDir");
    static readonly int CharLightColorId = Shader.PropertyToID("_MobiCharLightColor");

    /// <summary>볼륨 값 이름 → 게임 셰이더 전역 이름. color 면 색(감마 → 선형 변환), 아니면 첫 성분 실수.</summary>
    static readonly (string field, string global, bool color)[] Globals =
    {
        ("Skybox.SkyColorTop", "_Global_SkyColorTop", true),
        ("Skybox.SkyColorMiddle", "_Global_SkyColorMiddle", true),
        ("Skybox.SkyColorBottom", "_Global_SkyColorBottom", true),
        ("Skybox.SkyPosition1", "_Global_SkyPosition1", false),
        ("Skybox.SkyPosition2", "_Global_SkyPosition2", false),
        ("Skybox.SkyPosition3", "_Global_SkyPosition3", false),
        ("Skybox.SkyPosition4", "_Global_SkyPosition4", false),
        ("Skybox.SunDisk", "_Global_SunDisk", false),
        ("Skybox.SunGlowDisk", "_Global_SunGlowDisk", false),
        ("Skybox.SunGlowColor", "_Global_SunGlowColor", true),
        ("SkyClouds.CloudColor", "_Global_CloudColor", true),
        ("GlobalEnvironment.CloudDensity", "_Global_CloudDensity", false),
        ("GlobalEnvironment.CloudScale", "_Global_CloudScale", false),
        ("GlobalEnvironment.CloudEdgeHardness", "_Global_CloudEdgeHardness", false),
        ("GlobalEnvironment.GILightColor", "_Global_GILightMulti", true),
        ("GlobalEnvironment.Night2Day", "_Global_Night2Day", false),
        ("GlobalEnvironment.Raining", "_Global_Raining", false),
        ("GlobalEnvironment.Snowing", "_Global_Snow", false),
        ("GlobalEnvironment.FogHeightOffset", "_Global_FogHeightOffset", false),
        ("GlobalEnvironment.FogHeightScale", "_Global_FogHeightScale", false),
        ("GlobalEnvironment.FogHeightNoiseValue", "_Global_FogHeightNoiseValue", false),
        ("GlobalEnvironment.FogHeightNoiseSpeed", "_Global_FogHeightNoiseSpeed", false),
        ("GlobalEnvironment.FogHeightNoiseScale", "_Global_FogHeightNoiseScale", false),
        ("GlobalEnvironment.DimFogRange", "_Global_DimFog_Range", false),
        ("GlobalEnvironment.DimFogPower", "_Global_DimFog_Power", false),
        ("CharacterLight.LightColor", "_Global_Character_Light_Color", true),
        ("CharacterLight.GIColor", "_Global_Character_GI_Color", true),
        ("CharacterLight.SilhouetteColor", "_Global_Silhouette_Color", true),
        ("CharacterLight.SilhouetteBand", "_Global_Silhouette_BandWidth", false),
    };

    static bool captured;
    static Quaternion baseRotation;
    static Color baseColor;
    static float baseIntensity, baseShadow;

    /// <summary>시험용: 적용하지 않을 볼륨 값 이름("섹션.필드"). 스모크 테스트 -maVolumeSkip 이 채운다.</summary>
    public static readonly System.Collections.Generic.HashSet<string> Skip = new System.Collections.Generic.HashSet<string>();

    /// <summary>지금 적용된 볼륨 키(없으면 null). 로그·시험용.</summary>
    public static string CurrentKey { get; private set; }

    /// <summary>볼륨을 적용한다. volume 이 null 이면 장면 처음 상태로 되돌린다.</summary>
    public static void Apply(EnvironmentVolumeData volume, Light light)
    {
        Capture(light);
        CurrentKey = volume?.Key;

        foreach (var (field, global, color) in Globals)
        {
            if (Has(volume, field, StudioTime, out var v))
            {
                if (color) Shader.SetGlobalColor(global, ToColor(v));
                else Shader.SetGlobalFloat(global, v.Length > 0 ? v[0] : 0f);
            }
            else if (color) Shader.SetGlobalColor(global, Color.clear);
            else Shader.SetGlobalFloat(global, 0f);
        }

        if (light)
        {
            light.transform.rotation = Has(volume, "MainLight.Rotation", StudioTime, out var q) && q.Length >= 4
                ? Normalize(new Quaternion(q[0], q[1], q[2], q[3])) : baseRotation;
            light.color = Has(volume, "MainLight.Color", StudioTime, out var c) ? ToColor(c) : baseColor;
            light.intensity = Has(volume, "MainLight.Intensity", StudioTime, out var i) && i.Length > 0 ? i[0] : baseIntensity;
            light.shadowStrength = Has(volume, "MainLight.ShadowIntensity", StudioTime, out var s) && s.Length > 0 ? Mathf.Clamp01(s[0]) : baseShadow;
            SetCharacterLight(volume, light);
        }

        float[] fc = null, fs = null, fe = null;
        bool fog = Has(volume, "GlobalEnvironment.FogColor", StudioTime, out fc)
                   && Has(volume, "GlobalEnvironment.FogStartDistance", StudioTime, out fs) && fs.Length > 0
                   && Has(volume, "GlobalEnvironment.FogEndDistance", StudioTime, out fe) && fe.Length > 0;
        RenderSettings.fog = fog;
        if (fog)
        {
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = ToColor(fc);
            RenderSettings.fogStartDistance = fs[0];
            RenderSettings.fogEndDistance = Mathf.Max(fe[0], fs[0] + 0.01f);
        }
    }

    static bool Has(EnvironmentVolumeData volume, string field, float time, out float[] v)
    {
        v = null;
        return volume != null && !Skip.Contains(field) && volume.TryGet(field, time, out v);
    }

    /// <summary>장면 처음의 방향광 상태를 한 번 기록한다. 캐릭터 조명과 볼륨 해제 시 복원값이 된다.</summary>
    static void Capture(Light light)
    {
        if (captured || !light) return;
        captured = true;
        baseRotation = light.transform.rotation;
        baseColor = light.color;
        baseIntensity = light.intensity;
        baseShadow = light.shadowStrength;
    }

    /// <summary>앱 캐릭터 셰이더가 쓸 빛. URP 주광과 같은 형식(빛 쪽 방향, 선형 색)이다. 규칙은 파일 머리 주석.</summary>
    static void SetCharacterLight(EnvironmentVolumeData volume, Light light)
    {
        var toLight = baseRotation * Vector3.back;
        Shader.SetGlobalVector(CharLightDirId, new Vector4(toLight.x, toLight.y, toLight.z, 1f));
        var c = Has(volume, "CharacterLight.LightColor", StudioTime, out var cl) && cl.Length >= 4 && cl[3] > 0f
            ? ToColor(cl).linear
            : light.color.linear * light.intensity;
        Shader.SetGlobalVector(CharLightColorId, new Vector4(c.r, c.g, c.b, 1f));
    }

    static Color ToColor(float[] v) =>
        new Color(v.Length > 0 ? v[0] : 0f, v.Length > 1 ? v[1] : 0f, v.Length > 2 ? v[2] : 0f, v.Length > 3 ? v[3] : 1f);

    static Quaternion Normalize(Quaternion q)
    {
        float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        return m > 1e-6f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
    }
}
