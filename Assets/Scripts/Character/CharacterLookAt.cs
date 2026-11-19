// 카메라 쳐다보기(StudioFeatureReview.md "카메라 쳐다보기").
// 게임의 LookAtProcessComponent·LookAtProcessCalculator 는 선언만 있고 계산 본문을 읽지 못했다. 데이터의 본·각도 제한으로 앱이 근사한다.
// 본과 각도: 꾸미기 캐릭터 전용 상수(Constants/LookAt/CustomizeActor) [확인: 설치본]
//   _Spine·_Spine1·_Neck 좌우 20°씩, _Head 좌우 40°·상하 −19~28°, 눈 좌우 39°·상하 ±2°. 상수가 없으면 LookAt 시트(머리·눈)만 쓴다.
//   머리·눈의 앞·위 축은 LookAt 시트(Humanoid)의 ForwardAxis·UpAxis 다. 허리·목은 상하가 0 이라 축 없이 캐릭터 위 방향으로만 돈다.
// 계산
//   - 모션 평가 직후(CharacterMotion.Sample) 보정 회전을 곱한다. 모션이 매 프레임 회전을 다시 쓰므로 보정이 쌓이지 않는다.
//   - 좌우: 몸 정면(캐릭터 루트) 기준 대상 방향 각을 허리 → 가슴 → 목 → 머리가 YawMax 비율로 나눠 캐릭터 위 축으로 돈다
//     (게임 LookAtYawRatios·LookAtTotalYaw 와 같은 구성) [추정: 비율식]. 몸 사슬 합(100°)을 넘는 만큼은 눈이 맡는다.
//   - 상하: 머리가 제한 안에서 맡고 남은 만큼 눈이 맡는다. 부호는 Unity 회전 규칙(오른쪽 축 기준 +가 아래)이다 [추정].
//   - 대상이 좌우 제한(몸 사슬 + 눈) 밖이면 쳐다보지 않고 정면으로 돌아온다.
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
        /// <summary>눈: 맡은 시선 각 → 실제 본 회전 비율. 시트(LookAt)의 눈 한계 ÷ 꾸미기 상수의 눈 한계(13 ÷ 39) [추정: 게임 EyeAngleCorrectionRatio].</summary>
        public float YawScale = 1f, PitchScale = 1f;
    }

    /// <summary>따라가는 시간 상수(초). 게임 꾸미기 캐릭터의 LookAtEaseDuration 값은 읽지 못해 정했다 [임의].</summary>
    const float EaseSeconds = 0.2f;

    CharacterView view;
    /// <summary>좌우를 나눠 맡는 몸 사슬(허리 → 머리, 부모가 먼저). 마지막이 머리다.</summary>
    readonly List<BoneInfo> chain = new List<BoneInfo>();
    BoneInfo head;
    readonly List<BoneInfo> eyes = new List<BoneInfo>();
    float chainYaw, eyeYaw;
    float yaw, pitch, weight;

    /// <summary>시험용: 카메라 대신 쳐다볼 월드 위치. null 이면 Camera.main 위치.</summary>
    public Func<Vector3?> TargetOverride;
    /// <summary>진단용: 마지막 프레임의 보정(도)과 가중치.</summary>
    public string LastState { get; private set; }

    /// <summary>
    /// 시선 본을 찾아 붙인다. 쓸 수 없으면 null 과 이유.
    /// customize(꾸미기 전용 상수)가 있으면 그 본·각도를, 없으면 LookAt 시트의 서 있기(Stand, 없으면 All) 행을 쓴다.
    /// </summary>
    public static CharacterLookAt Attach(CharacterView view, List<LookAtParam> table, List<LookAtParam> customize, string group, out string why)
    {
        why = null;
        if (table == null || table.Count == 0) { why = "시선 데이터(LookAt 시트)를 읽지 못했습니다"; return null; }
        if (string.IsNullOrEmpty(group)) { why = "몸 모델에 시선 그룹이 없습니다"; return null; }
        var axes = new Dictionary<string, LookAtParam>(StringComparer.Ordinal);
        foreach (var p in table)
        {
            if (p.Group != group || p.Bone == null) continue;
            if (p.MovingState == "Stand" || (p.MovingState == "All" && !axes.ContainsKey(p.Bone))) axes[p.Bone] = p;
        }
        var rows = customize != null && customize.Count > 0 ? customize : new List<LookAtParam>(axes.Values);

        var la = view.gameObject.AddComponent<CharacterLookAt>();
        la.view = view;
        foreach (var p in rows)
        {
            var t = view.FindBone(p.Bone);
            if (!t) continue;
            axes.TryGetValue(p.Bone, out var ax);
            var info = new BoneInfo { T = t, P = p, Forward = Axis(ax?.ForwardAxis), Up = Axis(ax?.UpAxis ?? "PlusY") };
            // 꾸미기 상수의 눈 한계(39°)는 시선 범위다. 그대로 돌리면 눈동자가 흰자 밖으로 나가 사라진다 [확인: 촬영].
            // 본 회전은 시트의 눈 한계(13°·3°)로 줄인다.
            if (p.IsEye && ax != null && ax != p)
            {
                if (p.YawMax > 0) info.YawScale = Mathf.Min(1f, ax.YawMax / p.YawMax);
                float pm = Mathf.Max(Mathf.Abs(p.PitchMin), Mathf.Abs(p.PitchMax)), am = Mathf.Max(Mathf.Abs(ax.PitchMin), Mathf.Abs(ax.PitchMax));
                if (pm > 0) info.PitchScale = Mathf.Min(1f, am / pm);
            }
            if (p.IsEye) { la.eyes.Add(info); la.eyeYaw = Mathf.Max(la.eyeYaw, p.YawMax); }
            else { la.chain.Add(info); la.chainYaw += p.YawMax; }
        }
        // 머리는 상하·축이 필요하다. 상수에 머리가 있어도 시트에 축이 없으면 쓸 수 없다.
        la.head = la.chain.Count > 0 ? la.chain[la.chain.Count - 1] : null;
        if (la.head == null || !axes.ContainsKey(la.head.P.Bone)) { Destroy(la); why = $"시선 그룹 {group}에 머리 설정이 없습니다"; return null; }
        if (la.chainYaw <= 0) { Destroy(la); why = "시선 좌우 제한이 0입니다"; return null; }
        // 부모가 먼저 돌도록 계층 깊이로 정렬한다(허리 → 가슴 → 목 → 머리).
        la.chain.Sort((a, b) => Depth(a.T).CompareTo(Depth(b.T)));
        la.head = la.chain[la.chain.Count - 1];
        return la;
    }

    static int Depth(Transform t) { int n = 0; for (; t; t = t.parent) n++; return n; }

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

    public string Describe()
    {
        var parts = new List<string>();
        foreach (var b in chain) parts.Add($"{b.T.name} ±{b.P.YawMax}°");
        return $"몸 사슬 [{string.Join(", ", parts)}] 합 ±{chainYaw}°, 머리 상하 {head.P.PitchMin}~{head.P.PitchMax}°, 눈 {eyes.Count}개" +
               (eyes.Count > 0 ? $"(좌우 ±{eyes[0].P.YawMax}°, 상하 {eyes[0].P.PitchMin}~{eyes[0].P.PitchMax}°, 본 회전 ×{eyes[0].YawScale:F2})" : "");
    }

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

        // 좌우는 몸 정면(캐릭터 루트) 기준, 상하는 모션이 정한 머리 자세 기준으로 잰다.
        Vector3 up = view.transform.up, bodyForward = view.transform.forward;
        float wantYaw = yaw, wantPitch = pitch, wantWeight = 0f;
        if (on && target.HasValue)
        {
            var d = target.Value - head.T.position;
            var flat = Vector3.ProjectOnPlane(d, up);
            if (d.sqrMagnitude > 1e-6f && flat.sqrMagnitude > 1e-8f)
            {
                float y = Vector3.SignedAngle(Vector3.ProjectOnPlane(bodyForward, up), flat, up);
                if (Mathf.Abs(y) <= chainYaw + eyeYaw)
                {
                    wantYaw = y;
                    // 머리를 좌우로 돌린 뒤의 앞·오른쪽 축으로 상하 각을 잰다.
                    var hr = head.T.rotation;
                    Vector3 hf = hr * head.Forward, hu = hr * head.Up;
                    var turned = Quaternion.AngleAxis(Mathf.Clamp(y, -chainYaw, chainYaw) - Vector3.SignedAngle(Vector3.ProjectOnPlane(bodyForward, up), Vector3.ProjectOnPlane(hf, up), up), up);
                    var f2 = turned * hf; var r2 = Vector3.Cross(turned * hu, f2);
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
        // 몸 사슬: 몸 정면 기준 각을 YawMax 비율로 나눠 위 축으로 돈다. 사슬 합을 넘는 만큼은 눈 몫이다.
        float bodyYaw = Mathf.Clamp(totalYaw, -chainYaw, chainYaw);
        // 모션이 이미 머리를 돌려 둔 만큼은 빼고 돈다(머리가 대상 쪽을 정확히 향하게).
        float already = Vector3.SignedAngle(Vector3.ProjectOnPlane(bodyForward, up), Vector3.ProjectOnPlane(head.T.rotation * head.Forward, up), up);
        float turn = bodyYaw - already * weight;
        foreach (var b in chain)
        {
            float share = turn * b.P.YawMax / chainYaw;
            b.T.rotation = Quaternion.AngleAxis(share, up) * b.T.rotation;
        }
        // 머리 상하
        float headPitch = Mathf.Clamp(totalPitch, head.P.PitchMin, head.P.PitchMax);
        {
            var rot = head.T.rotation;
            Vector3 f = rot * head.Forward, u = rot * head.Up;
            var r = Vector3.Cross(u, f);
            head.T.rotation = Quaternion.AngleAxis(headPitch, r) * rot;
        }
        // 눈: 남은 좌우·상하
        float eyeYawLeft = totalYaw - bodyYaw;
        foreach (var e in eyes)
        {
            if (!e.T) continue;
            float ey = Mathf.Clamp(eyeYawLeft, -e.P.YawMax, e.P.YawMax);
            float ep = Mathf.Clamp(totalPitch - headPitch, e.P.PitchMin, e.P.PitchMax);
            Turn(e, ey * e.YawScale, ep * e.PitchScale);
        }
        LastState = $"좌우 {totalYaw:F1}°(몸 사슬 {bodyYaw:F1}°, 머리 몫 {bodyYaw * head.P.YawMax / chainYaw:F1}°, 눈 {eyeYawLeft:F1}°), 상하 {totalPitch:F1}°(머리 {headPitch:F1}°), 가중치 {weight:F2}";
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
