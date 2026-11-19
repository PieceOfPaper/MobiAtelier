// 카메라 쳐다보기(StudioFeatureReview.md "카메라 쳐다보기").
// 게임의 LookAtProcessComponent·LookAtProcessCalculator 는 선언만 있고 계산 본문을 읽지 못했다. LookAt 시트의 본·축·각도 제한으로 앱이 근사한다.
//   - 모션 평가 직후(CharacterMotion.Sample) 머리 → 눈 순서로 본 회전에 보정 회전을 곱한다. 모션이 매 프레임 회전을 다시 쓰므로 보정이 쌓이지 않는다.
//   - 좌우(yaw)는 머리와 눈이 YawMax 비율로 나눠 맡는다(게임 LookAtYawRatios·LookAtTotalYaw 와 같은 구성) [추정: 비율식].
//   - 상하(pitch)는 머리가 제한 안에서 먼저 맡고, 남은 만큼 눈이 맡는다. 부호는 Unity 회전 규칙(오른쪽 축 기준 +가 아래)이다 [추정].
//   - 대상이 좌우 제한(머리 + 눈 YawMax) 밖이면 쳐다보지 않고 정면으로 돌아온다(게임의 "각도 제한 밖 강제 쳐다보기" 옵션이 따로 있다).
//   - 켜기·끄기·대상 이동·소셜 모션 중 잠금은 EaseSeconds 로 부드럽게 바뀐다.
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class CharacterLookAt : MonoBehaviour
{
    sealed class BoneInfo
    {
        public Transform T;
        public LookAtParam P;
        public Vector3 Forward, Up;
    }

    /// <summary>따라가는 시간 상수(초). 게임 꾸미기 캐릭터의 LookAtEaseDuration 값은 읽지 못해 정했다 [임의].</summary>
    const float EaseSeconds = 0.2f;

    CharacterView view;
    BoneInfo head;
    readonly List<BoneInfo> eyes = new List<BoneInfo>();
    float yawLimit;
    float yaw, pitch, weight;

    /// <summary>시험용: 카메라 대신 쳐다볼 월드 위치. null 이면 Camera.main 위치.</summary>
    public Func<Vector3?> TargetOverride;
    /// <summary>진단용: 마지막 프레임의 머리 좌우·상하 보정(도)과 가중치.</summary>
    public string LastState { get; private set; }

    /// <summary>
    /// 몸 모델의 시선 그룹에서 머리·눈 본을 찾아 붙인다. 쓸 수 없으면 null 과 이유.
    /// 이동 상태는 서 있기(Stand) 행, 없으면 공통(All) 행을 쓴다.
    /// </summary>
    public static CharacterLookAt Attach(CharacterView view, List<LookAtParam> table, string group, out string why)
    {
        why = null;
        if (table == null || table.Count == 0) { why = "시선 데이터(LookAt 시트)를 읽지 못했습니다"; return null; }
        if (string.IsNullOrEmpty(group)) { why = "몸 모델에 시선 그룹이 없습니다"; return null; }
        var rows = new Dictionary<string, LookAtParam>(StringComparer.Ordinal);
        foreach (var p in table)
        {
            if (p.Group != group || p.Bone == null) continue;
            if (p.MovingState == "Stand" || (p.MovingState == "All" && !rows.ContainsKey(p.Bone))) rows[p.Bone] = p;
        }
        LookAtParam headRow = null;
        foreach (var p in rows.Values) if (!p.IsEye) { headRow = p; break; }
        if (headRow == null) { why = $"시선 그룹 {group}에 머리 설정이 없습니다"; return null; }
        var headBone = view.FindBone(headRow.Bone);
        if (!headBone) { why = "머리 본을 찾지 못했습니다: " + headRow.Bone; return null; }

        var la = view.gameObject.AddComponent<CharacterLookAt>();
        la.view = view;
        la.head = Info(headBone, headRow);
        float eyeYaw = 0;
        foreach (var p in rows.Values)
        {
            if (!p.IsEye) continue;
            var t = view.FindBone(p.Bone);
            if (!t) continue;
            la.eyes.Add(Info(t, p));
            eyeYaw = Mathf.Max(eyeYaw, p.YawMax);
        }
        la.yawLimit = headRow.YawMax + eyeYaw;
        if (la.yawLimit <= 0) { Destroy(la); why = $"시선 그룹 {group}의 좌우 제한이 0입니다"; return null; }
        return la;
    }

    static BoneInfo Info(Transform t, LookAtParam p) => new BoneInfo { T = t, P = p, Forward = Axis(p.ForwardAxis), Up = Axis(p.UpAxis) };

    /// <summary>시트 축 이름(PlusX·MinusY 등) → 본 로컬 벡터.</summary>
    static Vector3 Axis(string a)
    {
        switch (a)
        {
            case "PlusX": return Vector3.right;
            case "MinusX": return Vector3.left;
            case "PlusY": return Vector3.up;
            case "MinusY": return Vector3.down;
            case "PlusZ": return Vector3.forward;
            case "MinusZ": return Vector3.back;
            default: return Vector3.forward;
        }
    }

    public string Describe() =>
        $"머리 {head.T.name}(좌우 ±{head.P.YawMax}°, 상하 {head.P.PitchMin}~{head.P.PitchMax}°), 눈 {eyes.Count}개" +
        (eyes.Count > 0 ? $"(좌우 ±{eyes[0].P.YawMax}°, 상하 {eyes[0].P.PitchMin}~{eyes[0].P.PitchMax}°)" : "");

    /// <summary>
    /// 모션이 본 회전을 정한 뒤 부른다. dt &lt; 0 이면 따라가기 없이 바로 목표로 둔다(시험 촬영).
    /// suppress: 소셜 모션 가중치(0~1). 행동 중에는 그만큼 시선을 줄인다(게임 LookAtLockComponent 가 행동 중 시선을 잠근다) [추정].
    /// </summary>
    public void Apply(float dt, float suppress)
    {
        if (!head.T) return;
        bool on = view && view.Plan != null && view.Plan.LookAtCamera;
        Vector3? target = TargetOverride != null ? TargetOverride() : null;
        if (target == null && on)
        {
            var cam = Camera.main;
            if (cam) target = cam.transform.position;
        }

        // 머리 기준 틀(모션이 정한 자세): 앞·위·오른쪽. 오른쪽 = 위 × 앞(시트의 세 축과 같은 관계다).
        var hr = head.T.rotation;
        Vector3 f = hr * head.Forward, u = hr * head.Up;
        float wantYaw = yaw, wantPitch = pitch, wantWeight = 0f;
        if (on && target.HasValue)
        {
            var d = target.Value - head.T.position;
            var flat = Vector3.ProjectOnPlane(d, u);
            if (d.sqrMagnitude > 1e-6f && flat.sqrMagnitude > 1e-8f)
            {
                float y = Vector3.SignedAngle(f, flat, u);
                if (Mathf.Abs(y) <= yawLimit)
                {
                    var yr = Quaternion.AngleAxis(y, u);
                    var f2 = yr * f; var r2 = Vector3.Cross(u, f2);
                    wantYaw = y;
                    wantPitch = Vector3.SignedAngle(f2, d, r2);
                    wantWeight = 1f - Mathf.Clamp01(suppress);
                }
            }
        }
        float k = dt < 0 ? 1f : 1f - Mathf.Exp(-dt / EaseSeconds);
        if (wantWeight > 0f) { yaw += (wantYaw - yaw) * k; pitch += (wantPitch - pitch) * k; }
        weight += (wantWeight - weight) * k;
        if (weight < 1e-4f) { weight = 0f; LastState = "끔"; return; }

        float totalYaw = yaw * weight, totalPitch = pitch * weight;
        float headYaw = totalYaw * head.P.YawMax / yawLimit;
        float headPitch = Mathf.Clamp(totalPitch, head.P.PitchMin, head.P.PitchMax);
        Turn(head, headYaw, headPitch);
        foreach (var e in eyes)
        {
            if (!e.T) continue;
            float ey = Mathf.Clamp(totalYaw - headYaw, -e.P.YawMax, e.P.YawMax);
            float ep = Mathf.Clamp(totalPitch - headPitch, e.P.PitchMin, e.P.PitchMax);
            Turn(e, ey, ep);
        }
        LastState = $"좌우 {totalYaw:F1}°(머리 {headYaw:F1}°), 상하 {totalPitch:F1}°(머리 {headPitch:F1}°), 가중치 {weight:F2}";
    }

    /// <summary>본을 자기 위 축으로 yaw, 돌린 뒤의 오른쪽 축으로 pitch 만큼 돌린다(월드 기준).</summary>
    static void Turn(BoneInfo b, float yawDeg, float pitchDeg)
    {
        var rot = b.T.rotation;
        Vector3 f = rot * b.Forward, u = rot * b.Up;
        var yr = Quaternion.AngleAxis(yawDeg, u);
        var r2 = Vector3.Cross(u, yr * f);
        b.T.rotation = Quaternion.AngleAxis(pitchDeg, r2) * yr * rot;
    }
}
