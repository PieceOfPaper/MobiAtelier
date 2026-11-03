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
        return !b.DevOnly || Disclosure.AllowRestricted;
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

    /// <summary>직전 Apply 가 새 배경을 준비하지 못해 이전 배경을 그대로 두었는지(keepOnFailure 일 때만).</summary>
    public bool LastFailed { get; private set; }

    /// <summary>
    /// 배경을 바꾼다. 새 배경을 먼저 준비한 뒤 성공하면 이전 배경과 교체한다(Background.md 7장 1).
    /// 3D 프리팹 중 하나라도 열지 못하면: keepOnFailure 면 이전 배경·CurrentId 를 그대로 두고 LastFailed 를 켠다(편집 중 새 선택),
    /// 아니면 색 배경으로 대체해 그린다(시작 복원·가져오기의 대체 표시). 어느 쪽이든 사유를 돌려준다.
    /// </summary>
    public string Apply(StudioBackgroundData b, bool keepOnFailure = false)
    {
        using (var prepared = Prepare(b, keepOnFailure))
        {
            LastFailed = prepared.Failed;
            if (!prepared.Failed) Commit(prepared);
            return prepared.Error;
        }
    }

    /// <summary>승인 전에는 렌더링·카메라·현재 배경을 바꾸지 않는 준비본.</summary>
    public sealed class Prepared : IDisposable
    {
        internal readonly List<GameObject> Objects = new List<GameObject>();
        internal StudioBackgroundData Data;
        internal bool Is3D;
        public string Error;
        public bool Failed;
        public void Dispose()
        {
            foreach (var go in Objects) if (go)
            {
                go.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(go);
                else UnityEngine.Object.DestroyImmediate(go);
            }
            Objects.Clear();
        }
    }

    public Prepared Prepare(StudioBackgroundData b, bool keepOnFailure = false)
    {
        var result = new Prepared { Data = b };
        try
        {
            string error = null;
            if (b != null && b.Is3D)
            {
                foreach (var rel in b.Prefabs)
                {
                    var go = Spawn(rel);
                    if (go) { go.SetActive(false); result.Objects.Add(go); }
                    else error = "배경 리소스를 열지 못했습니다: " + rel;
                }
                if (result.Objects.Count > 0 && error == null)
                {
                    result.Is3D = true;
                    return result;
                }
                result.Dispose();
                result.Error = error ?? "배경 리소스가 없습니다.";
                if (keepOnFailure)
                {
                    result.Failed = true;
                    return result;
                }
            }
            var floor = Spawn(ShadowReceiverPath);
            if (floor)
            {
                floor.transform.position = Vector3.zero;       // 원본 프리팹은 y = -100 에 숨겨 두고 게임이 옮긴다.
                floor.SetActive(false);
                result.Objects.Add(floor);
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    public void Commit(Prepared prepared)
    {
        if (prepared == null || prepared.Failed) throw new InvalidOperationException("배경 준비가 완료되지 않았습니다.");
        Clear();
        spawned.AddRange(prepared.Objects);
        prepared.Objects.Clear();
        foreach (var go in spawned)
        {
            go.SetActive(true);
            if (prepared.Is3D) occluders.AddRange(go.GetComponentsInChildren<Renderer>(true));
        }
        CurrentId = prepared.Data?.Id;
        SetGradient(prepared.Data);
        cam.farClipPlane = prepared.Is3D ? FarFar : NearFar;
    }

    /// <summary>
    /// 저장된 배경 ID 를 그대로 표시할 수 없는 사유. 표시할 수 있거나 비어 있으면(구버전 파일의 기본값) null.
    /// 저장 ID 는 지우지 않고 기본 배경으로 대체 표시한다(Background.md 7장 2).
    /// </summary>
    public static string UnavailableReason(GameData data, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var b = data.Background(id);
        if (b == null) return "데이터에 없는 배경입니다";
        if (b.UseWebCam || !string.IsNullOrEmpty(b.Url)) return "이 앱에서 지원하지 않는 배경입니다";
        if (!Selectable(b)) return "에디터 전용 배경이라 공개 빌드에서 표시하지 않습니다";
        return null;
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
                go.SetActive(false);
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
