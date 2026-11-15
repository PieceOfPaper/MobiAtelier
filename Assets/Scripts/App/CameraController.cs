// 카메라 조작 (Camera.md). 게임 스튜디오 촬영 카메라 값과 식을 따른다(2026-11-15 사용자 결정).
//   좌 또는 우 단독 드래그: 캐릭터 기준 회전(상하각은 게임 범위) / 좌+우 드래그: 위아래 높이만 이동(거리별 범위) / 스크롤: 거리
//   H = 캐릭터 머리 메시 아래 높이 − 몸 최저점, t = clamp01((거리 − 최소 거리) / (최대 거리 − 최소 거리))
//   최대 거리 = clamp(H / 기준 키 × DefaultDistance, MinDistance, DefaultDistance). 기본 시점은 최대 거리.
//   기준점 y = 바닥 + 팬 높이 + (CameraTargetHeight − InitialPanningHeight)·H + 0.25·H·(1 − t)
//   카메라 위치 = 기준점 + 회전·(0, 0, −거리) + 카메라 up·(InitialPanningHeight − CameraTargetHeight)·H
//   렌즈 0.5·1·3: 수직 FOV = 2·atan(tan(Fov/2) / 배율)
//   창(카메라 영역)이 세로로 길면 세로 값(Portrait)을 쓴다.
//   에디터 전용 프리 카메라: 각도·거리·높이 제한을 모두 무시한다. 저장·이력에 남기지 않는다.
//   UI 위에서 시작한 드래그는 끝날 때까지 카메라로 넘기지 않는다. 앱이 포커스를 잃으면 드래그를 해제한다.
using System;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class CameraController : MonoBehaviour
{
    /// <summary>저장 형식. 0(필드 없음)은 이전 자유 팬 카메라라 거리·팬을 기본값으로 바꾼다.</summary>
    public const int RigVersion = 1;
    // 게임 코드 상수 [확인: 디스어셈블]
    const float EyeLiftRatio = 0.25f, EyeLiftTime = 0.2f, FovTime = 0.4f;
    // 거리 추종 시간은 게임 값을 찾지 못해 정했다 [추정].
    const float DistanceTime = 0.12f;

    public Camera Cam;
    /// <summary>현재 회전 기준점(월드). 벽 가림 판정에 쓴다.</summary>
    public Vector3 Pivot { get; private set; } = new Vector3(0, 0.9f, 0);
    /// <summary>화면 좌표가 UI 위인지 판단한다. true 면 그 위치에서 시작한 입력을 카메라가 받지 않는다.</summary>
    public Func<Vector2, bool> IsPointerOverUI;
    /// <summary>조작이 끝나 상태가 확정되었을 때(드래그 종료, 스크롤 정지).</summary>
    public event Action Committed;

    public float RotateSpeed = 0.3f;
    StudioCameraData data = StudioCameraData.Defaults();
    // 캐릭터 기준
    float baseY, bodyH = 1.4f, standardH = 1.685396f;

    float yaw = 180, pitch = 3, distance = 3, panHeight = 0.98f;
    float curDistance = 3, eyeLift, curFov = 50;
    float lensMultiplier = 1f;
    public float LensMultiplier => lensMultiplier;
    bool portrait;
    enum Drag { None, Rotate, Pan, Blocked }
    Drag drag;
    float scrollIdle = -1;

    // 에디터 전용 프리 카메라. 제한된 값(yaw·pitch·distance·panHeight)은 그대로 두고 따로 움직인다.
    bool free;
    Vector3 freePivot;
    float freeYaw, freePitch, freeDistance;
    public bool FreeMode => free;
    /// <summary>프리 카메라가 켜지거나 꺼졌을 때.</summary>
    public event Action<bool> FreeModeChanged;

    StudioCameraParams P => portrait ? data.Portrait : data.Landscape;

    public void SetData(StudioCameraData value) { if (value != null) data = value; }

    /// <summary>캐릭터 크기를 갱신한다. 시점은 유지하고 범위만 다시 맞춘다.</summary>
    public void SetCharacter(float floorY, float bodyHeightWithoutHead, float standardHeight)
    {
        baseY = floorY;
        if (bodyHeightWithoutHead > 0.2f) bodyH = bodyHeightWithoutHead;
        if (standardHeight > 0.2f) standardH = standardHeight;
        distance = Mathf.Clamp(distance, P.MinDistance, MaxDistance);
        curDistance = Mathf.Clamp(curDistance, P.MinDistance, MaxDistance);
        Apply(0);
    }

    float MaxDistance => Mathf.Clamp(bodyH / standardH * P.DefaultDistance, P.MinDistance, P.DefaultDistance);

    float Ratio(float d)
    {
        float span = MaxDistance - P.MinDistance;
        return span <= 1e-4f ? 0f : Mathf.Clamp01((d - P.MinDistance) / span);
    }

    void PanRange(float d, out float lo, out float hi)
    {
        float t = Ratio(d);
        lo = bodyH * Mathf.Lerp(P.MinPanningHeightWhenMinDistance, P.MinPanningHeightWhenMaxDistance, t);
        hi = bodyH * Mathf.Lerp(P.MaxPanningHeightWhenMinDistance, P.MaxPanningHeightWhenMaxDistance, t);
    }

    /// <summary>에디터 전용. 켜면 현재 화면에서 시작해 모든 제한을 무시하고, 끄면 켜기 전 시점으로 돌아간다.</summary>
    public void SetFreeMode(bool on)
    {
#if !UNITY_EDITOR
        on = false;
#endif
        if (free == on) return;
        if (on && Cam)
        {
            var t = Cam.transform;
            freeYaw = t.eulerAngles.y;
            freePitch = Mathf.DeltaAngle(0, t.eulerAngles.x);
            freeDistance = Mathf.Max(curDistance, 0.05f);
            freePivot = t.position + t.forward * freeDistance;
        }
        free = on;
        if (drag != Drag.None) drag = Drag.Blocked;
        scrollIdle = -1;
        FreeModeChanged?.Invoke(free);
    }

    public void ResetView()
    {
        SetFreeMode(false);
        portrait = IsPortraitArea();
        yaw = 180; pitch = P.InitialPitch; distance = MaxDistance; panHeight = P.InitialPanningHeight * bodyH;
        lensMultiplier = 1f;
        Snap();
    }

    /// <summary>거리·팬·각도는 유지하고 초점거리 배율에 대응하는 화각만 바꾼다.</summary>
    public void SetLensMultiplier(float value)
    {
        value = NormalizeLensMultiplier(value);
        if (Mathf.Approximately(lensMultiplier, value)) return;
        lensMultiplier = value;
        Committed?.Invoke();
    }

    /// <summary>가장 가까운 거리로 다가간다(시험 촬영용).</summary>
    public void ZoomToNearest() { distance = P.MinDistance; Committed?.Invoke(); }

    static float NormalizeLensMultiplier(float value)
    {
        if (Mathf.Approximately(value, 0.5f)) return 0.5f;
        if (Mathf.Approximately(value, 3f)) return 3f;
        return 1f;
    }

    public CameraState Save() => new CameraState { valid = true, rig = RigVersion, yaw = yaw, pitch = pitch, distance = distance, pan = new Vector2(0, panHeight), lensMultiplier = lensMultiplier };

    public void Load(CameraState s)
    {
        if (s == null || !s.valid) { ResetView(); return; }
        SetFreeMode(false);
        portrait = IsPortraitArea();
        lensMultiplier = NormalizeLensMultiplier(s.lensMultiplier);
        yaw = s.yaw;
        pitch = Mathf.Clamp(s.pitch, P.MinPitch, P.MaxPitch);
        if (s.rig < RigVersion)
        {
            // 이전 자유 팬 카메라 값은 의미가 달라 거리·높이만 기본으로 돌린다(방향·렌즈는 유지).
            distance = MaxDistance;
            panHeight = P.InitialPanningHeight * bodyH;
        }
        else
        {
            distance = Mathf.Clamp(s.distance, P.MinDistance, MaxDistance);
            panHeight = s.pan.y;
        }
        Snap();
    }

    void Snap()
    {
        curDistance = distance;
        eyeLift = EyeLiftRatio * bodyH * (1f - Ratio(curDistance));
        curFov = TargetFov();
        Apply(0);
    }

    float TargetFov() => 2f * Mathf.Atan(Mathf.Tan(P.Fov * 0.5f * Mathf.Deg2Rad) / lensMultiplier) * Mathf.Rad2Deg;

    bool IsPortraitArea() => Cam && Cam.pixelHeight > Cam.pixelWidth;

    /// <summary>가로↔세로 값이 바뀌면 거리 비율·각도를 새 범위로 옮긴다(사용자 구도를 초기화하지 않는다).</summary>
    void CheckOrientation()
    {
        bool p = IsPortraitArea();
        if (p == portrait) return;
        float t = Ratio(distance);
        PanRange(distance, out float lo, out float hi);
        float pt = hi - lo > 1e-4f ? Mathf.Clamp01((panHeight - lo) / (hi - lo)) : 0.5f;
        portrait = p;
        distance = Mathf.Lerp(P.MinDistance, MaxDistance, t);
        curDistance = distance;
        pitch = Mathf.Clamp(pitch, P.MinPitch, P.MaxPitch);
        PanRange(distance, out lo, out hi);
        panHeight = Mathf.Lerp(lo, hi, pt);
    }

    void OnApplicationFocus(bool focus)
    {
        if (!focus && drag != Drag.None) EndDrag();
    }

    void EndDrag()
    {
        bool changed = !free && (drag == Drag.Rotate || drag == Drag.Pan);
        drag = Drag.None;
        if (changed) Committed?.Invoke();
    }

    void Update()
    {
        if (!Cam) return;
        CheckOrientation();
        var mouse = Mouse.current;
        if (mouse != null) HandleInput(mouse);
        Apply(Time.unscaledDeltaTime);
    }

    void HandleInput(Mouse mouse)
    {
        bool l = mouse.leftButton.isPressed, r = mouse.rightButton.isPressed;
        Vector2 pos = mouse.position.ReadValue();
        Vector2 d = mouse.delta.ReadValue();

        if (!l && !r) { if (drag != Drag.None) EndDrag(); }
        else if (drag == Drag.None)
        {
            // 드래그 시작 위치가 UI 면 끝날 때까지 막는다.
            drag = IsPointerOverUI != null && IsPointerOverUI(pos) ? Drag.Blocked : (l && r ? Drag.Pan : Drag.Rotate);
            d = Vector2.zero;     // 시작 프레임 이동량은 쓰지 않는다
        }
        else if (drag != Drag.Blocked)
        {
            // 단독 ↔ 양 버튼 전환. 전환 프레임의 이동량은 버려 튀지 않게 한다.
            var next = l && r ? Drag.Pan : Drag.Rotate;
            if (next != drag) { drag = next; d = Vector2.zero; }
        }

        if (free) { HandleFreeInput(mouse, pos, d); return; }

        if (drag == Drag.Rotate)
        {
            yaw += d.x * RotateSpeed;
            pitch = Mathf.Clamp(pitch - d.y * RotateSpeed, P.MinPitch, P.MaxPitch);
        }
        else if (drag == Drag.Pan && d.y != 0)
        {
            // 게임: 높이 −= 화면 위쪽 이동량 / 화면 높이. 범위 밖으로 나가는 방향만 막는다.
            float next = panHeight - d.y / Mathf.Max(Cam.pixelHeight, 1);
            PanRange(distance, out float lo, out float hi);
            if (next < panHeight && next < lo) next = Mathf.Min(panHeight, lo);
            if (next > panHeight && next > hi) next = Mathf.Max(panHeight, hi);
            panHeight = next;
        }

        float sc = mouse.scroll.ReadValue().y;
        if (sc != 0 && drag != Drag.Blocked && (IsPointerOverUI == null || !IsPointerOverUI(pos)))
        {
            // 휠 한 칸 = 원값 1. 칸당 120 으로 들어오는 환경도 같은 값이 되게 맞춘다.
            float notches = Mathf.Abs(sc) >= 10f ? sc / 120f : sc;
            distance = Mathf.Clamp(distance - notches * data.WheelToDistance, P.MinDistance, MaxDistance);
            scrollIdle = 0.4f;
        }
        if (scrollIdle > 0)
        {
            scrollIdle -= Time.unscaledDeltaTime;
            if (scrollIdle <= 0) Committed?.Invoke();
        }
    }

    /// <summary>프리 카메라: 회전 각도 무제한, 좌+우 드래그는 화면 평면 이동, 휠은 거리 배율(하한만 둔다).</summary>
    void HandleFreeInput(Mouse mouse, Vector2 pos, Vector2 d)
    {
        if (drag == Drag.Rotate)
        {
            freeYaw += d.x * RotateSpeed;
            freePitch -= d.y * RotateSpeed;
        }
        else if (drag == Drag.Pan && d != Vector2.zero)
        {
            // 화면 높이만큼 끌면 현재 거리에서 보이는 높이만큼 움직인다.
            float viewH = 2f * freeDistance * Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float k = viewH / Mathf.Max(Cam.pixelHeight, 1);
            var rot = Quaternion.Euler(freePitch, freeYaw, 0);
            freePivot -= rot * new Vector3(d.x * k, d.y * k, 0);
        }
        float sc = mouse.scroll.ReadValue().y;
        if (sc != 0 && drag != Drag.Blocked && (IsPointerOverUI == null || !IsPointerOverUI(pos)))
        {
            float notches = Mathf.Abs(sc) >= 10f ? sc / 120f : sc;
            freeDistance = Mathf.Max(0.01f, freeDistance * Mathf.Pow(0.85f, notches));
        }
    }

    static float Follow(float cur, float target, float time, float dt) =>
        dt <= 0 ? target : Mathf.Lerp(cur, target, 1f - Mathf.Exp(-dt / Mathf.Max(time, 1e-3f) * 3f));

    void Apply(float dt)
    {
        if (!Cam) return;
        var p = P;
        curDistance = Follow(curDistance, distance, DistanceTime, dt);
        eyeLift = Follow(eyeLift, EyeLiftRatio * bodyH * (1f - Ratio(curDistance)), EyeLiftTime, dt);
        curFov = Follow(curFov, TargetFov(), FovTime, dt);
        Cam.fieldOfView = curFov;
        if (free)
        {
            var frot = Quaternion.Euler(freePitch, freeYaw, 0);
            Pivot = freePivot;
            Cam.transform.SetPositionAndRotation(freePivot + frot * new Vector3(0, 0, -freeDistance), frot);
            return;
        }
        var rot = Quaternion.Euler(pitch, yaw, 0);
        float upOffset = (p.InitialPanningHeight - p.CameraTargetHeight) * bodyH;
        Pivot = new Vector3(0, baseY + panHeight - upOffset + eyeLift, 0);
        Cam.transform.SetPositionAndRotation(Pivot + rot * new Vector3(0, 0, -curDistance) + rot * Vector3.up * upOffset, rot);
    }
}
