// 머리카락·치마 간이 물리(Docs/ClothPhysics.md). 게임의 MagicaCloth 1세대 MagicaBoneCloth 설정을 읽어 본 사슬을 흔든다.
//   원본 계산(Burst 잡)은 옮기지 않았다. 설정값의 뜻(MagicaCloth 문서)에 맞춰 위치 기반 물리(PBD)로 새로 짰다.
//   한 단계: 저항·중력 → 부모 기준 회전 복원 → 각도 제한 → 마디 길이 → 뿌리 거리 제한 → 구·캡슐 충돌.
//   결과는 본 회전으로만 쓴다(각 본이 시뮬레이션한 자식 위치를 향하게, 자식이 여럿이면 평균 방향).
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ClothPhysics
{
    /// <summary>
    /// 기준 계산 간격(초). 저항·회전 복원 값은 이 간격 한 단계당 비율로 본다(MagicaCloth 1세대 기본 갱신 빈도 90 [추정]).
    /// 실제 단계는 프레임 시간을 이 간격 이하로 나눈 길이다. 고정 간격으로 돌리면 화면 갱신이 더 빠를 때 계산 없는 프레임이 생겨
    /// 애니메이션은 움직였는데 물리 결과는 멈춰 있어 본이 떨린다(2026-11-20 사용자 제보 "펄럭임").
    /// </summary>
    const float Step = 1f / 90f;
    const int MaxStepsPerFrame = 4;
    /// <summary>처음·시간 건너뜀 뒤 정착 시간(초). 배치 촬영이 처진 모양을 찍도록 기본 자세로 미리 돌린다.</summary>
    const float SettleSeconds = 0.6f;

    public sealed class Team
    {
        public string Name;
        public Transform[] Bones;
        public bool[] Fixed;
        public int[] Parent, Root, Order;
        public int[][] Children;
        // 깊이별로 미리 계산한 값
        public float[] Radius, Gravity, Drag, MaxVelocity, Restore, ClampAngle, Mass;
        public Vector3 GravityDir = Vector3.up;
        public bool UseGravity, UseDrag, UseMaxVelocity, UseRestore, UseClampRotation, UseClampDistance, UseCollision, FixedNonRotation;
        public float RestoreVelocity, ClampRotationVelocity, ClampDistanceMin, ClampDistanceMax, ClampDistanceVelocity;
        public readonly List<ColliderRef> Colliders = new List<ColliderRef>();
        /// <summary>사슬 사이 거리 연결(부모-자식 제외)과 삼각형 굽힘(맞은편 입자 거리)의 두 입자·강도(1/90초 한 단계당).</summary>
        public int[] LinkA = Array.Empty<int>(), LinkB = Array.Empty<int>(), BendA = Array.Empty<int>(), BendB = Array.Empty<int>();
        public float[] LinkStiff = Array.Empty<float>(), BendStiff = Array.Empty<float>();
        public float DistanceVelocity = 1f;
        /// <summary>콜라이더 관통 방지 항목(입자, 충돌체, 기준점·방향, 허용 깊이, 이동 구 반지름).</summary>
        public readonly List<PenetrationRef> Penetrations = new List<PenetrationRef>();

        // 상태
        internal Vector3[] pos, old, vel, basePos;
        /// <summary>이번 계산 단계의 애니메이션 위치(프레임 사이를 단계 수로 나눠 보간)와 지난 단계의 값.</summary>
        internal Vector3[] stepBase, lastStepBase, frameFrom;
        internal Quaternion[] source, applied;
        internal bool[] hasApplied;
        internal Quaternion[] lineDelta;
        internal float[] len0;
    }

    public sealed class PenetrationRef
    {
        public int Particle;
        public ColliderRef Collider;
        public Vector3 LocalPos, LocalDir;
        public float Depth, Radius;
    }

    public sealed class ColliderRef
    {
        public Transform Anchor;
        /// <summary>부모 본 기준 충돌체 오브젝트의 국소 위치·회전·크기(프리팹 값).</summary>
        public Vector3 LocalPos, LocalScale = Vector3.one;
        public Quaternion LocalRot = Quaternion.identity;
        public Vector3 Center;
        public bool Capsule;
        public int Axis;
        public float Length, StartRadius, EndRadius, Radius;
    }

    public readonly List<Team> Teams = new List<Team>();
    /// <summary>시험용: 끌 단계(1 충돌, 2 회전 복원, 4 각도 제한, 8 사슬 사이 연결·굽힘, 16 중력, 32 뿌리 거리 제한, 64 관통 방지).</summary>
    public static int DebugOff;
    /// <summary>끄면 본을 애니메이션 값으로 둔다(시험 촬영 비교용).</summary>
    public bool Enabled = true;
    bool initialized, wasEnabled = true;

    /// <summary>
    /// 애니메이션을 마친 본에 물리를 건다. dt &lt; 0 이면 처음부터 다시 정착시키고, 0 이면 계산 없이 마지막 결과만 다시 쓴다.
    /// </summary>
    public void Apply(float dt)
    {
        if (Teams.Count == 0) return;
        foreach (var t in Teams) RestoreSource(t);
        if (!Enabled) { wasEnabled = false; return; }
        foreach (var t in Teams) ReadBase(t);
        if (!initialized || dt < 0f || !wasEnabled)
        {
            initialized = true; wasEnabled = true;
            foreach (var t in Teams) Reset(t);
            int n = Mathf.RoundToInt(SettleSeconds / Step);
            for (int i = 0; i < n; i++) foreach (var t in Teams) Simulate(t, Step);
        }
        else if (dt > 0f)
        {
            // 매 프레임 한 단계 이상, 단계 길이 ≤ 기준 간격. 너무 긴 프레임(멈춤 뒤)은 최대 단계 수만큼만 진행한다.
            float span = Mathf.Min(dt, Step * MaxStepsPerFrame);
            int steps = Mathf.Clamp(Mathf.CeilToInt(span / Step - 1e-4f), 1, MaxStepsPerFrame);
            float h = span / steps;
            foreach (var t in Teams) Array.Copy(t.lastStepBase, t.frameFrom, t.frameFrom.Length);
            for (int s = 1; s <= steps; s++)
                foreach (var t in Teams)
                {
                    float u = (float)s / steps;
                    for (int i = 0; i < t.stepBase.Length; i++) t.stepBase[i] = Vector3.LerpUnclamped(t.frameFrom[i], t.basePos[i], u);
                    Simulate(t, h);
                }
        }
        foreach (var t in Teams) Write(t);
        if (dt > 0f) Measure();
    }

    /// <summary>진단: 이동 입자들이 지금까지 움직인 거리 합(물리 결과, 애니메이션 자세). Apply 마다 더한다.</summary>
    public float TravelSim, TravelBase;
    Vector3[][] lastSim, lastBase;

    void Measure()
    {
        if (lastSim == null) { lastSim = new Vector3[Teams.Count][]; lastBase = new Vector3[Teams.Count][]; }
        for (int k = 0; k < Teams.Count; k++)
        {
            var t = Teams[k];
            if (lastSim[k] == null) { lastSim[k] = (Vector3[])t.pos.Clone(); lastBase[k] = (Vector3[])t.basePos.Clone(); continue; }
            for (int i = 0; i < t.pos.Length; i++)
            {
                if (t.Fixed[i]) continue;
                TravelSim += (t.pos[i] - lastSim[k][i]).magnitude;
                TravelBase += (t.basePos[i] - lastBase[k][i]).magnitude;
            }
            Array.Copy(t.pos, lastSim[k], t.pos.Length);
            Array.Copy(t.basePos, lastBase[k], t.basePos.Length);
        }
    }

    /// <summary>진단: 이동 입자가 애니메이션 위치에서 벗어난 최대 거리(m). 값이 숫자가 아니면 NaN.</summary>
    public float MaxOffset()
    {
        float m = 0f;
        foreach (var t in Teams)
            for (int i = 0; i < t.Bones.Length; i++)
            {
                if (t.Fixed[i]) continue;
                float d = (t.pos[i] - t.basePos[i]).magnitude;
                if (float.IsNaN(d)) return float.NaN;
                m = Mathf.Max(m, d);
            }
        return m;
    }

    /// <summary>진단: 충돌체마다 월드 양 끝·반지름과, 애니메이션 자세의 이동 입자가 그 충돌체에 파고든 최대 깊이(m).</summary>
    public string DescribeColliders()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in Teams)
        {
            ReadBase(t);
            foreach (var c in t.Colliders)
            {
                if (!c.Anchor) continue;
                Segment(c, out var a, out var b, out float scale);
                float worst = 0f; int who = -1;
                for (int i = 0; i < t.Bones.Length; i++)
                {
                    if (t.Fixed[i]) continue;
                    float pen = Penetration(c, a, b, scale, t.basePos[i], t.Radius[i]);
                    if (pen > worst) { worst = pen; who = i; }
                }
                sb.Append("\n").Append($"    {t.Name} 충돌체 {c.Anchor.name} {(c.Capsule ? "캡슐" : "구")} 끝 {a:F3}~{b:F3} 반지름 {(c.Capsule ? c.StartRadius : c.Radius) * scale:F3}/{c.EndRadius * scale:F3} 크기 {scale:F3} 파고듦 {worst:F3}{(who >= 0 ? " @" + t.Bones[who].name : "")}");
            }
        }
        return sb.ToString();
    }

    static void Segment(ColliderRef c, out Vector3 a, out Vector3 b, out float scale)
    {
        var m = c.Anchor.localToWorldMatrix * Matrix4x4.TRS(c.LocalPos, c.LocalRot, c.LocalScale);
        var center = m.MultiplyPoint3x4(c.Center);
        scale = (m.MultiplyVector(Vector3.right).magnitude + m.MultiplyVector(Vector3.up).magnitude + m.MultiplyVector(Vector3.forward).magnitude) / 3f;
        a = b = center;
        if (!c.Capsule) return;
        var axis = c.Axis == 0 ? Vector3.right : c.Axis == 1 ? Vector3.up : Vector3.forward;
        var half = m.MultiplyVector(axis).normalized * (c.Length * scale * 0.5f);
        a = center - half; b = center + half;
    }

    static float Penetration(ColliderRef c, Vector3 a, Vector3 b, float scale, Vector3 p, float radius)
    {
        var ab = b - a;
        float ll = ab.sqrMagnitude, u = ll > 1e-12f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ll) : 0f;
        float cr = (c.Capsule ? Mathf.Lerp(c.StartRadius, c.EndRadius, u) : c.Radius) * scale + radius;
        return cr - (p - (a + ab * u)).magnitude;
    }

    // ── 기본 자세 ─────────────────────────────────────────────

    /// <summary>클립이 이 본 회전을 쓰지 않았으면 지난번에 쓴 물리 회전이 남아 있다. 그때는 물리 전 회전으로 되돌린다.</summary>
    static void RestoreSource(Team t)
    {
        foreach (int i in t.Order)
        {
            var b = t.Bones[i];
            if (!b) continue;
            var r = b.localRotation;
            if (t.hasApplied[i] && Quaternion.Dot(t.applied[i], r) > 0.999999f) b.localRotation = t.source[i];
            else t.source[i] = r;
        }
    }

    static void ReadBase(Team t)
    {
        for (int i = 0; i < t.Bones.Length; i++) t.basePos[i] = t.Bones[i] ? t.Bones[i].position : Vector3.zero;
    }

    static void Reset(Team t)
    {
        for (int i = 0; i < t.Bones.Length; i++) { t.pos[i] = t.old[i] = t.stepBase[i] = t.lastStepBase[i] = t.basePos[i]; t.vel[i] = Vector3.zero; }
    }

    // ── 한 단계 ─────────────────────────────────────────────

    static void Simulate(Team t, float h)
    {
        Array.Copy(t.stepBase, t.lastStepBase, t.stepBase.Length);

        // 1) 속도 적분. 고정 입자는 애니메이션 위치로 옮긴다.
        foreach (int i in t.Order)
        {
            if (t.Fixed[i]) { t.old[i] = t.pos[i] = t.stepBase[i]; continue; }
            var v = t.vel[i];
            if (t.UseDrag) v *= Mathf.Pow(Mathf.Clamp01(1f - t.Drag[i]), h / Step);
            if (t.UseGravity && (DebugOff & 16) == 0) v += t.GravityDir * (t.Gravity[i] * h);
            if (t.UseMaxVelocity && v.sqrMagnitude > t.MaxVelocity[i] * t.MaxVelocity[i]) v = v.normalized * t.MaxVelocity[i];
            t.old[i] = t.pos[i];
            t.pos[i] += v * h;
        }

        // 2) 거리 연결(StructDistance: 사슬 위아래와 메시 연결의 가로·대각선). 질량 역수로 나눠 옮긴다.
        if ((DebugOff & 8) == 0)
            for (int k = 0; k < t.LinkA.Length; k++) SolveDistance(t, t.LinkA[k], t.LinkB[k], t.LinkStiff[k], h);

        // 2-1) 회전 제한·복원(알고리즘 2: CompositeRotationConstraint.RotationRootLineJob [확인: 게임 코드]).
        if (t.UseClampRotation || t.UseRestore) Composite(t, h);

        // 2-2) 삼각형 굽힘(공유 모서리 맞은편 두 입자 거리 유지로 근사).
        if ((DebugOff & 8) == 0)
            for (int k = 0; k < t.BendA.Length; k++) SolveDistance(t, t.BendA[k], t.BendB[k], t.BendStiff[k], h);

        // 3) 뿌리 거리 제한(ClampDistance): 기본 자세의 뿌리까지 거리 × [최소, 최대] 비율.
        if (t.UseClampDistance && (DebugOff & 32) == 0)
            foreach (int i in t.Order)
            {
                int r = t.Root[i];
                if (t.Fixed[i] || r < 0 || r == i) continue;
                float l0 = (t.stepBase[i] - t.stepBase[r]).magnitude;
                if (l0 < 1e-6f) continue;
                var d = t.pos[i] - t.pos[r];
                float len = d.magnitude, lo = l0 * t.ClampDistanceMin, hi = l0 * t.ClampDistanceMax;
                if (len > hi) Move(t, i, t.pos[r] + d / len * hi - t.pos[i], t.ClampDistanceVelocity);
                else if (len < lo && len > 1e-6f) Move(t, i, t.pos[r] + d / len * lo - t.pos[i], t.ClampDistanceVelocity);
            }

        // 4) 충돌: 입자 반지름만큼 충돌체 밖으로 민다.
        if (t.UseCollision && t.Colliders.Count > 0 && (DebugOff & 1) == 0)
            foreach (var c in t.Colliders)
            {
                if (!c.Anchor) continue;
                var m = c.Anchor.localToWorldMatrix * Matrix4x4.TRS(c.LocalPos, c.LocalRot, c.LocalScale);
                var center = m.MultiplyPoint3x4(c.Center);
                float scale = (m.MultiplyVector(Vector3.right).magnitude + m.MultiplyVector(Vector3.up).magnitude + m.MultiplyVector(Vector3.forward).magnitude) / 3f;
                Vector3 a = center, b = center;
                if (c.Capsule)
                {
                    var axis = c.Axis == 0 ? Vector3.right : c.Axis == 1 ? Vector3.up : Vector3.forward;
                    // length 는 캡슐 전체 길이로 본다(중심 ± 길이/2) [추정: 쇄골·위팔 충돌체 중심이 길이의 절반 위치에 있다].
                    var half = m.MultiplyVector(axis).normalized * (c.Length * scale * 0.5f);
                    a = center - half; b = center + half;
                }
                foreach (int i in t.Order)
                {
                    if (t.Fixed[i]) continue;
                    float u = 0f;
                    var q = a;
                    if (c.Capsule)
                    {
                        var ab = b - a;
                        float ll = ab.sqrMagnitude;
                        u = ll > 1e-12f ? Mathf.Clamp01(Vector3.Dot(t.pos[i] - a, ab) / ll) : 0f;
                        q = a + ab * u;
                    }
                    float cr = (c.Capsule ? Mathf.Lerp(c.StartRadius, c.EndRadius, u) : c.Radius) * scale + t.Radius[i];
                    var dv = t.pos[i] - q;
                    float dist = dv.magnitude;
                    if (dist >= cr) continue;
                    var n = dist > 1e-6f ? dv / dist : (t.pos[i] - center).sqrMagnitude > 1e-12f ? (t.pos[i] - center).normalized : Vector3.up;
                    Move(t, i, n * (cr - dist), 1f);
                }
            }

        // 4-1) 콜라이더 관통 방지(MagicaCloth "Collider Penetration"): 입자를 묶인 충돌체 뒤로 넘어가지 못하게 한다.
        //   기준점 c·방향 n(충돌체 국소 → 월드), 충돌체 반지름 r. 한계점 L = c + n·(r − 허용 깊이), 입자는 L 에 닿는 반지름 R 의 구(중심 L + n·R) 안에 있어야 한다 [추정: 문서 "Moving Radius" 의 기하].
        if ((DebugOff & 64) == 0)
            foreach (var pe in t.Penetrations)
            {
                var c = pe.Collider;
                if (!c.Anchor || t.Fixed[pe.Particle]) continue;
                var m = c.Anchor.localToWorldMatrix * Matrix4x4.TRS(c.LocalPos, c.LocalRot, c.LocalScale);
                Segment(c, out var a, out var b, out float scale);
                var cp = m.MultiplyPoint3x4(pe.LocalPos);
                var n = m.MultiplyVector(pe.LocalDir);
                if (n.sqrMagnitude < 1e-12f) continue;
                n.Normalize();
                float rc = c.Radius * scale;
                if (c.Capsule)
                {
                    var ab = b - a;
                    float ll = ab.sqrMagnitude, u = ll > 1e-12f ? Mathf.Clamp01(Vector3.Dot(cp - a, ab) / ll) : 0f;
                    rc = Mathf.Lerp(c.StartRadius, c.EndRadius, u) * scale;
                }
                var limit = cp + n * (rc - pe.Depth);
                var center = limit + n * pe.Radius;
                var d = t.pos[pe.Particle] - center;
                float len = d.magnitude;
                if (len > pe.Radius && len > 1e-6f) Move(t, pe.Particle, center + d / len * pe.Radius - t.pos[pe.Particle], 1f);
            }

        // 5) 속도 = 이번 단계 이동 ÷ 단계 길이(속도 영향으로 뺀 보정은 old 에 들어 있다).
        for (int i = 0; i < t.Bones.Length; i++) t.vel[i] = t.Fixed[i] ? Vector3.zero : (t.pos[i] - t.old[i]) / h;
    }

    /// <summary>
    /// 회전 제한·복원(게임 CompositeRotationConstraint.RotationRootLineJob 을 옮김 [확인: 디스어셈블]). 사슬 순서로 2회 반복한다.
    ///   각도 제한: 길이는 잡 시작 길이와 현재 길이의 평균. 부모의 시뮬레이션 회전(기본 → 현재 변화)을 곱한 애니메이션 방향에서 최대각을 넘으면 자른다.
    ///             선분 중점을 기준으로 부모·자식을 반씩 옮기고, 속도에는 √(saturate(최대각/90))·(−0.3) + 0.5 만 남긴다.
    ///   회전 복원: 부모의 애니메이션 방향으로 1 − (1 − 세기)^단계비율 만큼 돌린다. 회전 중심은 선분 위 r 지점이고 부모·자식이 함께 움직인다.
    ///             r = (방향·위쪽 × 0.5 + 0.5)^(4 − 3·clamp01(|중력|/9.8)) × 0.2 + 0.25 (GetRotRatio, 최소 0.25·최대 0.45). 속도에는 복원 속도 영향만 남긴다.
    ///   입자 마찰(충돌 마찰) 비율은 1 로 둔다.
    /// </summary>
    static void Composite(Team t, float h)
    {
        foreach (int i in t.Order)
        {
            int p = t.Parent[i];
            t.len0[i] = p >= 0 ? (t.pos[i] - t.pos[p]).magnitude : 0f;
        }
        float power = h / Step;
        for (int iter = 0; iter < 2; iter++)
            foreach (int i in t.Order)
            {
                int p = t.Parent[i];
                t.lineDelta[i] = Quaternion.identity;
                if (p < 0) continue;
                var baseLine = t.stepBase[i] - t.stepBase[p];
                if (baseLine.sqrMagnitude < 1e-12f) { t.lineDelta[i] = t.lineDelta[p]; continue; }
                var dirBase = baseLine.normalized;
                bool cm = !t.Fixed[i], pm = !t.Fixed[p];
                var pdelta = pm ? t.lineDelta[p] : Quaternion.identity;

                if (t.UseClampRotation && (DebugOff & 4) == 0)
                {
                    var tv = pdelta * dirBase;
                    if (cm)
                    {
                        var v = t.pos[i] - t.pos[p];
                        float cl = v.magnitude;
                        if (cl > 1e-9f)
                        {
                            v = v / cl * ((t.len0[i] - cl) * 0.5f + cl);
                            float maxDeg = t.ClampAngle[i], ang = Vector3.Angle(v, tv);
                            var v2 = ang > maxDeg && ang > 1e-4f ? Vector3.Slerp(tv * v.magnitude, v, maxDeg / ang) : v;
                            var mid = t.pos[p] + v * 0.5f;
                            float w = Mathf.Sqrt(Mathf.Clamp01(maxDeg / 90f)) * -0.3f + 0.5f;
                            var dc = mid + v2 * 0.5f - t.pos[i];
                            var dp = mid - v2 * 0.5f - t.pos[p];
                            Move(t, i, dc, w);
                            if (pm) Move(t, p, dp, w);
                        }
                        var nv = t.pos[i] - t.pos[p];
                        t.lineDelta[i] = nv.sqrMagnitude > 1e-12f ? Quaternion.FromToRotation(tv, nv) * pdelta : pdelta;
                    }
                    else t.lineDelta[i] = pdelta;
                }

                if (t.UseRestore && (cm || pm) && (DebugOff & 2) == 0)
                {
                    var v = t.pos[i] - t.pos[p];
                    if (v.sqrMagnitude < 1e-12f) continue;
                    float tt = 1f - Mathf.Pow(1f - Mathf.Clamp01(t.Restore[i]), power);
                    var v2 = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(v, dirBase), tt) * v;
                    float g = Mathf.Abs(t.Gravity[i]);
                    float d = g > 1e-6f ? Vector3.Dot(dirBase, t.GravityDir) * 0.5f + 0.5f : 0.5f;
                    float r = Mathf.Pow(Mathf.Max(d, 0f), 4f - 3f * Mathf.Clamp01(g / 9.8f)) * (0.45f - 0.25f) + 0.25f;
                    var pivot = t.pos[p] + v * r;
                    var dc = pivot + v2 * (1f - r) - t.pos[i];
                    var dp = pivot - v2 * r - t.pos[p];
                    if (cm) Move(t, i, dc, t.RestoreVelocity);
                    if (pm) Move(t, p, dp, t.RestoreVelocity);
                }
            }
    }

    /// <summary>두 입자 거리를 애니메이션 자세의 거리로 stiffness(1/90초 한 단계당 비율)만큼 되돌린다. 고정 입자는 움직이지 않는다.</summary>
    static void SolveDistance(Team t, int a, int b, float stiffness, float h)
    {
        float wa = t.Fixed[a] ? 0f : 1f / Mathf.Max(t.Mass[a], 1e-3f), wb = t.Fixed[b] ? 0f : 1f / Mathf.Max(t.Mass[b], 1e-3f);
        if (wa + wb <= 0f) return;
        float rest = (t.stepBase[b] - t.stepBase[a]).magnitude;
        var d = t.pos[b] - t.pos[a];
        float len = d.magnitude;
        if (len < 1e-6f) return;
        float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(stiffness), h / Step);
        var corr = d / len * ((len - rest) * k / (wa + wb));
        if (wa > 0f) Move(t, a, corr * wa, t.DistanceVelocity);
        if (wb > 0f) Move(t, b, -corr * wb, t.DistanceVelocity);
    }

    /// <summary>입자를 옮긴다. velocityInfluence 만큼만 속도에 남긴다(MagicaCloth 의 "속도 영향").</summary>
    static void Move(Team t, int i, Vector3 delta, float velocityInfluence)
    {
        t.pos[i] += delta;
        t.old[i] += delta * (1f - Mathf.Clamp01(velocityInfluence));
    }

    // ── 본에 쓰기 ─────────────────────────────────────────────

    static void Write(Team t)
    {
        foreach (int i in t.Order)
        {
            var b = t.Bones[i];
            if (!b) continue;
            var ch = t.Children[i];
            if (ch.Length == 0 || (t.Fixed[i] && t.FixedNonRotation)) { t.hasApplied[i] = false; continue; }
            Vector3 from = Vector3.zero, to = Vector3.zero;
            var origin = b.position;
            foreach (int c in ch)
            {
                if (!t.Bones[c]) continue;
                var f = t.Bones[c].position - origin;
                var g = t.pos[c] - origin;
                if (f.sqrMagnitude < 1e-12f || g.sqrMagnitude < 1e-12f) continue;
                from += f.normalized; to += g.normalized;
            }
            if (from.sqrMagnitude > 1e-12f && to.sqrMagnitude > 1e-12f)
                b.rotation = Quaternion.FromToRotation(from, to) * b.rotation;
            t.applied[i] = b.localRotation;
            t.hasApplied[i] = true;
        }
    }

    // ── 만들기 ─────────────────────────────────────────────

    /// <summary>
    /// 읽은 설정으로 묶음을 만든다. bone(name) 은 캐릭터 본, prefabLocal(path) 은 충돌체 오브젝트의 프리팹 국소 값.
    /// 본을 하나라도 찾지 못하면 null 과 사유.
    /// </summary>
    public static Team Build(MagicaBoneClothInfo info, Func<string, Transform> bone,
        Func<string, (Vector3 pos, Quaternion rot, Vector3 scale)?> prefabLocal, out string error)
    {
        error = null;
        int n = info.Particles.Count;
        var t = new Team
        {
            Name = info.Name, Bones = new Transform[n], Fixed = new bool[n], Parent = new int[n], Root = new int[n], Children = new int[n][],
            Radius = new float[n], Gravity = new float[n], Drag = new float[n], MaxVelocity = new float[n], Restore = new float[n], ClampAngle = new float[n], Mass = new float[n],
            stepBase = new Vector3[n], lastStepBase = new Vector3[n], frameFrom = new Vector3[n],
            pos = new Vector3[n], old = new Vector3[n], vel = new Vector3[n], basePos = new Vector3[n], source = new Quaternion[n], applied = new Quaternion[n], hasApplied = new bool[n], lineDelta = new Quaternion[n], len0 = new float[n],
        };
        bool alg2 = info.Algorithm == 1;
        var restore = info.Get(alg2 ? "restoreRotation2" : "restoreRotation");
        var clamp = info.Get(alg2 ? "clampRotationAngle2" : "clampRotationAngle");
        var kids = new List<int>[n];
        for (int i = 0; i < n; i++)
        {
            var p = info.Particles[i];
            t.Bones[i] = bone(p.Bone);
            if (!t.Bones[i]) { error = "본 없음: " + p.Bone; return null; }
            t.Fixed[i] = p.Fixed;
            t.Parent[i] = p.Parent >= 0 && p.Parent < n && p.Parent != i ? p.Parent : -1;
            float d = p.Depth;
            t.Radius[i] = info.Get("radius").Evaluate(d);
            t.Gravity[i] = info.Get("gravity").Evaluate(d);
            t.Drag[i] = info.Get("drag").Evaluate(d);
            t.MaxVelocity[i] = info.Get("maxVelocity").Evaluate(d);
            t.Mass[i] = info.Get("mass").Evaluate(d);
            t.Restore[i] = restore.Evaluate(d);
            t.ClampAngle[i] = clamp.Evaluate(d);
            kids[i] = new List<int>();
        }
        for (int i = 0; i < n; i++) if (t.Parent[i] >= 0) kids[t.Parent[i]].Add(i);
        for (int i = 0; i < n; i++) t.Children[i] = kids[i].ToArray();
        // 부모가 먼저 오도록 정렬하고, 각 입자의 사슬 뿌리(부모를 거슬러 올라간 끝)를 찾는다. 부모 고리는 끊는다.
        var order = new List<int>();
        var placed = new bool[n];
        void Visit(int i, int depth)
        {
            if (placed[i] || depth > n) return;
            placed[i] = true; order.Add(i);
            foreach (int c in kids[i]) Visit(c, depth + 1);
        }
        for (int i = 0; i < n; i++) if (t.Parent[i] < 0) Visit(i, 0);
        for (int i = 0; i < n; i++) if (!placed[i]) { t.Parent[i] = -1; Visit(i, 0); }
        t.Order = order.ToArray();
        for (int i = 0; i < n; i++)
        {
            int r = i;
            for (int guard = 0; t.Parent[r] >= 0 && guard < n; guard++) r = t.Parent[r];
            t.Root[i] = r;
        }

        t.GravityDir = new Vector3(info.GravityX, info.GravityY, info.GravityZ);
        if (t.GravityDir.sqrMagnitude < 1e-6f) t.GravityDir = Vector3.up;
        t.GravityDir.Normalize();
        t.UseGravity = info.On("useGravity");
        t.UseDrag = info.On("useDrag");
        t.UseMaxVelocity = info.On("useMaxVelocity");
        t.UseRestore = info.On("useRestoreRotation");
        t.UseClampRotation = info.On("useClampRotation");
        t.UseClampDistance = info.On("useClampDistanceRatio");
        t.UseCollision = info.On("useCollision");
        t.FixedNonRotation = info.On("useFixedNonRotation");
        t.RestoreVelocity = info.Value(alg2 ? "restoreRotationVelocityInfluence2" : "restoreRotationVelocityInfluence", 1f);
        t.ClampRotationVelocity = info.Value("clampRotationVelocityInfluence", 1f);
        t.ClampDistanceMin = info.Value("clampDistanceMinRatio", 0f);
        t.ClampDistanceMax = info.Value("clampDistanceMaxRatio", 10f);
        t.ClampDistanceVelocity = info.Value("clampDistanceVelocityInfluence", 1f);

        // 거리 연결(사슬 위아래 + 메시 연결의 가로·대각선). 강도는 두 입자 깊이 평균의 값.
        var structCurve = info.Get("structDistanceStiffness");
        var bendCurve = info.Get(alg2 ? "triangleBend2" : "triangleBend");
        var la = new List<int>(); var lb = new List<int>(); var ls = new List<float>();
        foreach (var (a, b) in info.Links)
        {
            if (t.Fixed[a] && t.Fixed[b]) continue;
            la.Add(a); lb.Add(b); ls.Add(structCurve.Evaluate((info.Particles[a].Depth + info.Particles[b].Depth) * 0.5f));
        }
        t.LinkA = la.ToArray(); t.LinkB = lb.ToArray(); t.LinkStiff = ls.ToArray();
        if (info.On("useTriangleBend"))
        {
            var ba = new List<int>(); var bb = new List<int>(); var bs = new List<float>();
            foreach (var (a, b) in info.Bends)
            {
                if (t.Fixed[a] && t.Fixed[b]) continue;
                ba.Add(a); bb.Add(b); bs.Add(bendCurve.Evaluate((info.Particles[a].Depth + info.Particles[b].Depth) * 0.5f));
            }
            t.BendA = ba.ToArray(); t.BendB = bb.ToArray(); t.BendStiff = bs.ToArray();
        }
        t.DistanceVelocity = info.Value("restoreDistanceVelocityInfluence", 1f);

        var byIndex = new Dictionary<int, ColliderRef>();
        foreach (var c in info.Colliders)
        {
            var anchor = bone(c.ParentBone);
            var local = prefabLocal(c.Path);
            if (!anchor || local == null) continue;
            t.Colliders.Add(byIndex[c.Index] = new ColliderRef
            {
                Anchor = anchor, LocalPos = local.Value.pos, LocalRot = local.Value.rot, LocalScale = local.Value.scale,
                Center = new Vector3(c.CenterX, c.CenterY, c.CenterZ), Capsule = c.Capsule, Axis = c.Axis,
                Length = c.Length, StartRadius = c.StartRadius, EndRadius = c.EndRadius, Radius = c.Radius,
            });
        }
        // 콜라이더 관통 방지(모드 1 = Collider Penetration). 최대 깊이보다 깊은 입자는 뺀다.
        if (info.On("usePenetration") && (int)info.Value("penetrationMode") == 1)
        {
            float maxDepth = info.Value("penetrationMaxDepth", 1f);
            var pd = info.Get("penetrationDistance"); var pr = info.Get("penetrationRadius");
            foreach (var pe in info.Penetrations)
            {
                if (!byIndex.TryGetValue(pe.Collider, out var col) || pe.Particle >= n || t.Fixed[pe.Particle]) continue;
                float d = info.Particles[pe.Particle].Depth;
                if (d > maxDepth + 1e-4f) continue;
                t.Penetrations.Add(new PenetrationRef
                {
                    Particle = pe.Particle, Collider = col,
                    LocalPos = new Vector3(pe.PosX, pe.PosY, pe.PosZ), LocalDir = new Vector3(pe.DirX, pe.DirY, pe.DirZ),
                    Depth = pd.Evaluate(d), Radius = Mathf.Max(pr.Evaluate(d), 1e-3f),
                });
            }
        }
        return t;
    }
}
