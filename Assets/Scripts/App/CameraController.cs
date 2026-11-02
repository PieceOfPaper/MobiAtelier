// 카메라 조작 (Camera.md, 확정)
//   좌 또는 우 단독 드래그: 캐릭터 기준점 중심 회전 / 좌+우 드래그: 카메라 right·up 방향 평행 이동 / 스크롤: 확대·축소
//   평행 이동은 화면 오프셋으로 유지하고, 회전 중심은 항상 캐릭터 기준점이다(팬 위치를 회전 중심으로 쓰지 않는다).
//   UI 위에서 시작한 드래그는 끝날 때까지 카메라로 넘기지 않는다. 앱이 포커스를 잃으면 드래그를 해제한다.
using System;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class CameraController : MonoBehaviour
{
    // 기존 앱의 기본 수직 화각이다. 원본 게임의 렌즈별 FOV를 재현한 수치는 아니다.
    public const float DefaultFieldOfView = 30f;
    public Camera Cam;
    /// <summary>회전 기준점(캐릭터 기준 월드 위치)</summary>
    public Vector3 Pivot = new Vector3(0, 0.9f, 0);
    /// <summary>화면 좌표가 UI 위인지 판단한다. true 면 그 위치에서 시작한 입력을 카메라가 받지 않는다.</summary>
    public Func<Vector2, bool> IsPointerOverUI;
    /// <summary>조작이 끝나 상태가 확정되었을 때(드래그 종료, 스크롤 정지).</summary>
    public event Action Committed;

    public float RotateSpeed = 0.3f, PanSpeed = 0.0025f, ZoomStep = 0.12f;
    public float MinPitch = -80, MaxPitch = 80, MinDistance = 0.4f, MaxDistance = 10f;

    /// <summary>저장된 시점이 없을 때의 거리. 캐릭터 크기에 맞춰 FitHeight 로 정한다.</summary>
    public float DefaultDistance = 4.2f;
    float yaw = 180, pitch = 5, distance = 4.2f;
    float lensMultiplier = 1f;
    public float LensMultiplier => lensMultiplier;
    Vector2 pan;
    enum Drag { None, Rotate, Pan, Blocked }
    Drag drag;
    float scrollIdle = -1;

    public void ResetView()
    {
        yaw = 180; pitch = 5; distance = DefaultDistance; pan = Vector2.zero;
        lensMultiplier = 1f;
        Apply();
    }

    /// <summary>거리·팬·각도는 유지하고 초점거리 배율에 대응하는 화각만 바꾼다.</summary>
    public void SetLensMultiplier(float value)
    {
        value = NormalizeLensMultiplier(value);
        if (Mathf.Approximately(lensMultiplier, value)) return;
        lensMultiplier = value;
        Apply();
        Committed?.Invoke();
    }

    static float NormalizeLensMultiplier(float value)
    {
        if (Mathf.Approximately(value, 0.5f)) return 0.5f;
        if (Mathf.Approximately(value, 3f)) return 3f;
        return 1f;
    }

    /// <summary>현재 화면비에서 캐릭터 높이·너비가 여백을 두고 들어오는 기본 거리를 정한다. 저장된 시점에는 쓰지 않는다.</summary>
    public void FitSize(float height, float width)
    {
        if (!Cam || height <= 0.1f) return;
        // 초기화는 1x로 돌아가므로 기본 거리도 항상 1x 화각에서 계산한다.
        float halfV = Mathf.Tan(DefaultFieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfH = halfV * Mathf.Max(Cam.aspect, 0.1f);
        float d = Mathf.Max(height * 0.62f / halfV, width * 0.62f / halfH);
        DefaultDistance = Mathf.Clamp(d, MinDistance, MaxDistance);
    }

    public CameraState Save() => new CameraState { valid = true, yaw = yaw, pitch = pitch, distance = distance, pan = pan, lensMultiplier = lensMultiplier };

    public void Load(CameraState s)
    {
        if (s == null || !s.valid) { ResetView(); return; }
        yaw = s.yaw;
        pitch = Mathf.Clamp(s.pitch, MinPitch, MaxPitch);
        distance = Mathf.Clamp(s.distance, MinDistance, MaxDistance);
        pan = s.pan;
        lensMultiplier = NormalizeLensMultiplier(s.lensMultiplier);
        Apply();
    }

    void OnApplicationFocus(bool focus)
    {
        if (!focus && drag != Drag.None) EndDrag();
    }

    void EndDrag()
    {
        bool changed = drag == Drag.Rotate || drag == Drag.Pan;
        drag = Drag.None;
        if (changed) Committed?.Invoke();
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (!Cam || mouse == null) return;
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

        if (drag == Drag.Rotate)
        {
            yaw += d.x * RotateSpeed;
            pitch = Mathf.Clamp(pitch - d.y * RotateSpeed, MinPitch, MaxPitch);
        }
        else if (drag == Drag.Pan)
        {
            pan -= d * PanSpeed * Mathf.Max(distance, 0.5f) / 4.2f;
        }

        float sc = mouse.scroll.ReadValue().y;
        if (sc != 0 && drag != Drag.Blocked && (IsPointerOverUI == null || !IsPointerOverUI(pos)))
        {
            distance = Mathf.Clamp(distance * (1f - Mathf.Sign(sc) * ZoomStep), MinDistance, MaxDistance);
            scrollIdle = 0.4f;
        }
        if (scrollIdle > 0)
        {
            scrollIdle -= Time.unscaledDeltaTime;
            if (scrollIdle <= 0) Committed?.Invoke();
        }
        Apply();
    }

    void Apply()
    {
        if (!Cam) return;
        Cam.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(DefaultFieldOfView * 0.5f * Mathf.Deg2Rad) / lensMultiplier) * Mathf.Rad2Deg;
        var rot = Quaternion.Euler(pitch, yaw, 0);
        Vector3 right = rot * Vector3.right, up = rot * Vector3.up;
        Cam.transform.SetPositionAndRotation(Pivot + rot * new Vector3(0, 0, -distance) + right * pan.x + up * pan.y, rot);
    }
}
