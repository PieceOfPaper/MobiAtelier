// 꾸미기 장면의 스튜디오 배경(Background.md).
//   색 배경: 배경 시트의 네 모서리 색을 화면 고정 그라데이션(스카이박스)으로 그리고, 발밑에 그림자만 받는 바닥을 둔다.
//   3D 배경: 시트의 배경 프리팹을 설치 폴더 번들에서 열어 원점에 둔다. 원본 셰이더의 전용 패스는 StudioBackgroundPass 가 그린다.
// 배경 선택은 장면 상태(EditState.background)다. 자동 저장·코디 JSON 에 들어간다(SaveLoad.md 확정 원칙).
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class StudioBackground
{
    public const string DefaultId = "BG_Gradation1";
    const string ShadowReceiverPath = "Environment/StudioPhotoMode/Prefab/StudioPhotoMode_ShadowReceiver.prefab";
    /// <summary>색 배경일 때 카메라 먼 평면. 3D 배경은 하늘 돔(반지름 수백 m)이 잘리지 않게 늘린다.</summary>
    const float NearFar = 100f, FarFar = 3000f;

    readonly Camera cam;
    readonly BundleCache bundles;
    readonly Material gradient;
    readonly List<GameObject> spawned = new List<GameObject>();
    /// <summary>3D 배경의 렌더러. 카메라와 캐릭터 사이를 가리면 숨긴다(UpdateOccluders).</summary>
    readonly List<Renderer> occluders = new List<Renderer>();

    public string CurrentId { get; private set; }

    public StudioBackground(Camera cam, BundleCache bundles, Shader gradientShader)
    {
        this.cam = cam;
        this.bundles = bundles;
        gradient = gradientShader ? new Material(gradientShader) { name = "StudioGradient" } : null;
        StudioBackgroundRendering.Install();
    }

    /// <summary>
    /// 고를 수 있는 배경. 웹캠·웹 페이지 배경은 이 앱에서 쓰지 않는다.
    /// 개발용(DevOnly) 행은 에디터에서만 후보에 넣는다(숨김 데이터 공개 범위, Equipment.md).
    /// </summary>
    public static bool Selectable(StudioBackgroundData b)
    {
        if (b == null || b.UseWebCam || !string.IsNullOrEmpty(b.Url)) return false;
#if UNITY_EDITOR
        return true;
#else
        return !b.DevOnly;
#endif
    }

    /// <summary>저장된 ID 를 실제로 쓸 배경으로 푼다. 없거나 고를 수 없으면 기본 배경, 그것도 없으면 첫 후보.</summary>
    public static StudioBackgroundData Resolve(GameData data, string id)
    {
        var b = data.Background(id);
        if (Selectable(b)) return b;
        b = data.Background(DefaultId);
        if (Selectable(b)) return b;
        foreach (var x in data.Backgrounds) if (Selectable(x)) return x;
        return null;
    }

    /// <summary>배경을 바꾼다. 3D 프리팹을 하나도 열지 못하면 색 배경으로 그리고 사유를 돌려준다.</summary>
    public string Apply(StudioBackgroundData b)
    {
        Clear();
        CurrentId = b?.Id;
        string error = null;
        if (b != null && b.Is3D)
        {
            foreach (var rel in b.Prefabs)
            {
                var go = Spawn(rel);
                if (!go) error = "배경 리소스를 열지 못했습니다: " + rel;
            }
            if (spawned.Count > 0)
            {
                foreach (var go in spawned) occluders.AddRange(go.GetComponentsInChildren<Renderer>(true));
                // 3D 배경 뒤에도 시트의 네 모서리 색을 깐다(3D 행은 모두 흰색).
                // '공간의 틈: 빛'의 구름 하늘(SkyLit)은 아직 그려지지 않아 이 색이 하늘로 보인다 [미확인: 원인].
                SetGradient(b);
                cam.farClipPlane = FarFar;
                return error;
            }
        }
        SetGradient(b);
        var floor = Spawn(ShadowReceiverPath);
        if (floor) floor.transform.position = Vector3.zero;       // 원본 프리팹은 y = -100 에 숨겨 두고 게임이 옮긴다.
        cam.farClipPlane = NearFar;
        return error;
    }

    void SetGradient(StudioBackgroundData b)
    {
        if (!gradient)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = StageBuilder.Background;
            return;
        }
        gradient.SetColor("_LeftBottom", Hex(b?.LeftBottom));
        gradient.SetColor("_RightBottom", Hex(b?.RightBottom));
        gradient.SetColor("_LeftUpper", Hex(b?.LeftUpper));
        gradient.SetColor("_RightUpper", Hex(b?.RightUpper));
        RenderSettings.skybox = gradient;
        cam.clearFlags = CameraClearFlags.Skybox;
    }

    static Color Hex(string hex) =>
        hex != null && ColorUtility.TryParseHtmlString(hex.Trim(), out var c) ? c : StageBuilder.Background;

    GameObject Spawn(string rel)
    {
        GameObject prefab = null;
        try { prefab = bundles.LoadAsset<GameObject>(BundleCache.AssetRoot + rel); }
        catch (Exception e) { Debug.LogWarning($"[MobiAtelier] 배경 프리팹 읽기 실패 {rel}: {e.Message}"); }
        if (!prefab) return null;
        var go = UnityEngine.Object.Instantiate(prefab);
        go.name = "Background:" + prefab.name;
        // 배경의 화면 보정(Volume)은 게임이 VolumeKey 로 따로 관리한다. 이 앱은 아직 적용하지 않으므로 끈다(Background.md 미정).
        foreach (var v in go.GetComponentsInChildren<UnityEngine.Rendering.Volume>(true)) v.enabled = false;
        spawned.Add(go);
        return go;
    }

    /// <summary>
    /// 3D 방 배경에서 카메라와 기준점 사이를 가리는 배경 메시(벽·소품)를 숨긴다. 매 프레임 부른다.
    /// 게임은 배경의 레이캐스트 콜라이더로 같은 일을 하는 것으로 보인다 [추정]. 여기서는 렌더러 범위로 근사한다.
    /// 기준점을 감싸는 메시(하늘·바닥 전체 등)는 숨기지 않는다.
    /// </summary>
    public void UpdateOccluders(Vector3 camPos, Vector3 target)
    {
        if (occluders.Count == 0) return;
        var to = target - camPos;
        float len = to.magnitude;
        if (len < 1e-4f) return;
        var ray = new Ray(camPos, to / len);
        foreach (var r in occluders)
        {
            if (!r) continue;
            var b = r.bounds;
            bool hide = !b.Contains(target) && b.IntersectRay(ray, out float hit) && hit < len;
            if (r.forceRenderingOff != hide) r.forceRenderingOff = hide;
        }
    }

    void Clear(bool immediate = false)
    {
        occluders.Clear();
        foreach (var go in spawned)
            if (go)
            {
                if (Application.isPlaying && !immediate) UnityEngine.Object.Destroy(go);
                else UnityEngine.Object.DestroyImmediate(go);     // 종료·에디터 배치 시험
            }
        spawned.Clear();
    }

    /// <summary>번들을 내리기 전에 부른다. 배경 오브젝트가 번들 에셋을 참조한 채 남지 않게 한다.</summary>
    public void Dispose()
    {
        Clear(true);
        if (RenderSettings.skybox == gradient) RenderSettings.skybox = null;
        if (gradient) UnityEngine.Object.Destroy(gradient);
    }
}
