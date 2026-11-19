// 옷 맞춤 자르기: .ClothingUnit.bytes(의상 분리면·삼각형 부위)를 읽어 게임과 같은 규칙으로 몸·의상 메시를 자른다(대상·순서는 아래 ClothFit).
//
// .ClothingUnit.bytes (리틀엔디언, 문자열은 7비트 길이 + UTF-8)
//   i32 AppearanceSlot, i32 n, i32[n] 렌더러 번호(<모델>_<번호>.SkinnedModularMesh)
//   i32 분리면 수, 분리면[]:
//     string Id(CenterBottom, LLeg …), i32 FittingJunctionType,
//     원통 좌표 기준: f32x3 길이 축, f32x3 극 축, f32x3 원점
//     자르기 돌출 좌표: f32x3 중심, i32 k, f32x3[k], 평면(f32x3 위치, f32x3 노멀, f32 거리)
//     f32 자르기 경도
//     경계 돌출 좌표(같은 형식), f32 경계 경도
//     i32 경계 본 가중치 수, [i32 경계 돌출 꼭짓점 번호, i32 k, (string 본, f32 가중치)[k]], i32 m, 24바이트[m]
//   렌더러마다(n개): i32, i32 복셀 수, 복셀[]: 28바이트, i32 k, i32[k] 삼각형 번호, i32 부위(PoseSegment)
//                    i32, i32 k, 4바이트[k]
//   그 뒤 바이트는 읽지 않는다.
//
// 자르기(게임 FittingCutPolicy·NativeMeshCuttingJob과 같은 규칙)
//   분리면 평면 = 원점 + 길이 축 × 자르기 경도, 노멀 = 길이 축. 노멀 쪽(양수)이 의상이 덮는 쪽이다.
//   평면은 그 관절의 부위(PoseSegment)에만 적용한다. 의상 종류(FittingCutCase)에 따라 Center 분리면은
//   부위를 더 덮고 "우선(dominant)" 평면이 된다. 의상 하나의 평면들로 몸 삼각형마다:
//     ① 의상의 drop 부위면 뺀다.
//     ② 우선 평면의 양수 쪽에 세 정점이 모두 있으면 뺀다.
//     ③ 관련 평면이 있고, 어느 평면의 음수 쪽에도 정점이 하나도 없으면 뺀다.
//     ④ 걸친 평면이 있으면 그 평면들의 음수 쪽만 남기도록 잘라 새 정점을 만든다.
//   의상마다 차례로 적용한다.
//   의상마다 바인드 자세가 다르다(T자세 장갑, A자세 몸 등). 그래서 분리면은 그 분리면 메시(Mesh_SP_<Id>)의 주 본으로
//   (본 월드 행렬 × 바인드포즈) 변환해 기본 자세의 월드로 옮기고, 몸 정점도 기본 자세에서 스키닝한 월드 위치로 판정한다.
//   새로 생기는 정점은 메시 공간 값(위치·노멀·UV·가중치·블렌드셰이프)을 같은 비율로 보간한다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class ClothingUnit
{
    /// <summary>돌출 좌표: 분리면 둘레의 닫힌 꺾은선(중심과 꼭짓점).</summary>
    public sealed class Extrusion
    {
        public Vector3 Center;
        public Vector3[] Positions = Array.Empty<Vector3>();
    }

    public sealed class Separator
    {
        public string Id;
        public int Junction;
        /// <summary>원통 좌표 기준: 길이 축(Axis)·극 축(Polar)·원점.</summary>
        public Vector3 Axis, Polar, Origin;
        public float SlicerLongitude, BoundaryLongitude;
        public Extrusion SlicerExtrusion, BoundaryExtrusion;
        /// <summary>경계 돌출 좌표 꼭짓점마다의 본 가중치(본 이름, 가중치). 없으면 null.</summary>
        public List<(string bone, float weight)>[] BoundaryWeights;
    }

    public int Slot;
    public int[] RendererIndices = Array.Empty<int>();
    public readonly List<Separator> Separators = new List<Separator>();
    /// <summary>렌더러 번호 → (파일 삼각형 번호 → 부위)</summary>
    public readonly Dictionary<int, Dictionary<int, byte>> Segments = new Dictionary<int, Dictionary<int, byte>>();

    public static ClothingUnit Read(byte[] data)
    {
        using (var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8))
        {
            Vector3 V3() => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            void Skip(long n) => r.BaseStream.Seek(n, SeekOrigin.Current);
            Extrusion ReadExtrusion()
            {
                var e = new Extrusion { Center = V3() };
                int k = r.ReadInt32();
                e.Positions = new Vector3[k];
                for (int i = 0; i < k; i++) e.Positions[i] = V3();
                Skip(28); // 돌출 평면(위치·노멀·거리): 쓰지 않는다
                return e;
            }

            var u = new ClothingUnit { Slot = r.ReadInt32() };
            int n = r.ReadInt32();
            u.RendererIndices = new int[n];
            for (int i = 0; i < n; i++) u.RendererIndices[i] = r.ReadInt32();
            int ns = r.ReadInt32();
            for (int s = 0; s < ns; s++)
            {
                var sep = new Separator { Id = r.ReadString(), Junction = r.ReadInt32() };
                sep.Axis = V3(); sep.Polar = V3(); sep.Origin = V3();
                sep.SlicerExtrusion = ReadExtrusion();
                sep.SlicerLongitude = r.ReadSingle();
                sep.BoundaryExtrusion = ReadExtrusion();
                sep.BoundaryLongitude = r.ReadSingle();
                int nb = r.ReadInt32();
                var weights = new List<(string, float)>[sep.BoundaryExtrusion.Positions.Length];
                for (int b = 0; b < nb; b++)
                {
                    int index = r.ReadInt32();
                    int k = r.ReadInt32();
                    var list = new List<(string, float)>(k);
                    for (int j = 0; j < k; j++) list.Add((r.ReadString(), r.ReadSingle()));
                    if (index >= 0 && index < weights.Length) weights[index] = list;
                }
                if (nb > 0) sep.BoundaryWeights = weights;
                Skip(24L * r.ReadInt32());
                u.Separators.Add(sep);
            }
            for (int i = 0; i < n; i++)
            {
                r.ReadInt32();
                int nv = r.ReadInt32();
                var seg = new Dictionary<int, byte>();
                for (int v = 0; v < nv; v++)
                {
                    Skip(28);
                    int k = r.ReadInt32();
                    var tris = new int[k];
                    for (int j = 0; j < k; j++) tris[j] = r.ReadInt32();
                    byte sg = (byte)r.ReadInt32();
                    foreach (var t in tris) seg[t] = sg;
                }
                r.ReadInt32();
                Skip(4L * r.ReadInt32());
                u.Segments[u.RendererIndices[i]] = seg;
            }
            return u;
        }
    }
}

public static class BodyCut
{
    // AppearanceSlot
    const int SlotBody = 1, SlotTop = 7, SlotBottom = 8, SlotOnePiece = 9, SlotFoot = 11, SlotRobe = 12, SlotOnePieceWithShoes = 13;
    // FittingCutCase
    const int CutBodyWithBottom = 0, CutBodyWithFoot = 1, CutBodyWithOnePiece = 2, CutBodyWithTop = 3, CutTopWithBottom = 4, CutDefault = 5;
    // FittingJunctionType
    const int JCenter = 0, JLeftHipJoint = 6, JRightHipJoint = 7;
    const int SegNeck = 13, SegmentCount = 14;
    const uint LowerLegsAndFeet = 0x1b00; // LeftLowerLeg, LeftFoot, RightLowerLeg, RightFoot
    const int MaxPlanes = 32;

    /// <summary>FittingJunctionType → 관련 부위(PoseSegment) 비트</summary>
    static readonly uint[] JunctionSegments =
    {
        Bits(0, 7, 10),          // Center
        Bits(7, 8, 9),           // LeftLeg
        Bits(10, 11, 12),        // RightLeg
        Bits(1, 2, 3),           // LeftArm
        Bits(4, 5, 6),           // RightArm
        Bits(7, 10, 8, 11, 9, 12), // Skirt
        Bits(0, 7),              // LeftHipJoint
        Bits(0, 10),             // RightHipJoint
        Bits(13, 0),             // Neck
        0,                       // Invalid
    };

    static uint Bits(params int[] s) { uint m = 0; foreach (var x in s) m |= 1u << x; return m; }

    public struct CutPlane
    {
        public Vector3 Point, Normal;
        public uint Segments;
        public bool Dominant;
        public string Id;
    }

    /// <summary>한 의상이 몸에 적용하는 평면들과 통째로 빼는 부위.</summary>
    public sealed class Plan
    {
        public string Key;
        public int CutCase;
        public uint DropSegments;
        public readonly List<CutPlane> Planes = new List<CutPlane>();
        /// <summary>이 조각의 밀기(NativeFittingPlan.Pushers). 자르기 다음에 실행한다.</summary>
        public readonly List<Pusher> Pushers = new List<Pusher>();
    }

    /// <summary>밀기 계획에 필요한 자르는 쪽 모델 정보: MorphableAssets(PushCurves·PushIncludeSkin·ExtrusionRatios), 대상 렌더러가 피부인지, 자르는 쪽 부위.</summary>
    public sealed class PushOptions
    {
        public object MorphableAssets;
        public bool TargetSkin;
        public int FitterSlot;
    }

    /// <summary>
    /// 분리면 하나의 밀기(NativePusher + 곡선 + 돌출 비율). 원통 좌표 기준·돌출 꼭짓점은 기본 자세 월드 좌표다.
    /// 경도(높이)는 길이 축 방향 거리: 자르기 SlicerLong, 경계 BoundaryLong, 밀기 시작 PusherLong = 경계 − 밀기 양.
    /// </summary>
    public sealed class Pusher
    {
        public string Id;
        public int Junction;
        public uint Segments;
        public Vector3 L, P, O;
        public Vector3 SlicerCenter, BoundaryCenter;
        public Vector3[] SlicerPos, BoundaryPos;
        public float SlicerLong, BoundaryLong, PusherLong, SlicerScale = 1, BoundaryScale = 1, Ratio = 1;
        public List<(string bone, float weight)>[] BoundaryWeights;
        /// <summary>곡선 키(시간, 값, 들어오는 기울기, 나가는 기울기, 들어오는 가중치, 나가는 가중치).</summary>
        public float[][] Curve;
    }

    /// <summary>
    /// 밀기 대상 메시의 본: 이름 목록, 본마다 기본 자세의 (본 월드 × 바인드포즈). 경계 본 가중치에 메시에 없는 본이 있으면
    /// NewBone 으로 본을 찾아 덧붙인다(바인드포즈 = 본 월드⁻¹ × 첫 본의 스키닝 행렬). AddedBones·AddedBindposes 는 자른 메시에 붙는다.
    /// </summary>
    public sealed class PushContext
    {
        public readonly List<string> BoneNames = new List<string>();
        public readonly List<Matrix4x4> Skin = new List<Matrix4x4>();
        public Func<string, Transform> NewBone;
        public readonly List<Transform> AddedBones = new List<Transform>();
        public readonly List<Matrix4x4> AddedBindposes = new List<Matrix4x4>();

        public int Index(string name)
        {
            int i = BoneNames.IndexOf(name);
            if (i >= 0) return i;
            var t = NewBone?.Invoke(name);
            if (!t || Skin.Count == 0) return -1;
            var bind = t.worldToLocalMatrix * Skin[0];
            BoneNames.Add(name);
            Skin.Add(t.localToWorldMatrix * bind);
            AddedBones.Add(t);
            AddedBindposes.Add(bind);
            return BoneNames.Count - 1;
        }
    }

    /// <summary>BezierAnimationCurve.InOut: (0,0)→(1,1), 기울기 0, 가중치 0(= smoothstep).</summary>
    static readonly float[][] InOutCurve = { new float[] { 0, 0, 0, 0, 0, 0 }, new float[] { 1, 1, 0, 0, 0, 0 } };

    /// <summary>
    /// 밀어내는 쪽 분리면마다 밀기를 만든다(NativeMorphableAssetsDeserializer.CreatePusher·SpaceSeparatorParameter.GetDefault·BuildPlans).
    /// 기본 밀기 양: Center 분리면 + 상의 0.1, 나머지 0.03. Id 에 "neck" 이 있으면 밀지 않는다.
    /// 피부 렌더러는 Center 가 아니면 PushIncludeSkin 이 true 인 분리면만 민다. 보정이 있으면 PusherEnabled=false 면 빼고,
    /// 자르기 경도 += SlicerOffset, 자르기 돌출 배율 *= SlicerScale, 경계·밀기 경도 += PusherOffset, 경계 돌출 배율 *= PusherScale.
    /// </summary>
    static void AddPushers(Plan plan, ClothingUnit fitter, PushOptions push, Func<string, object> overrideOf, Func<ClothingUnit.Separator, Matrix4x4> toWorld)
    {
        var ma = push.MorphableAssets;
        foreach (var s in fitter.Separators)
        {
            if (s.SlicerExtrusion == null || s.BoundaryExtrusion == null) continue;
            if (s.Id != null && s.Id.ToLowerInvariant().Contains("neck")) continue;
            if (push.TargetSkin && s.Junction != JCenter && !(ma.Get("PushIncludeSkin").Get(s.Id) is bool inc && inc)) continue;
            float amount = s.Junction == JCenter && push.FitterSlot == SlotTop ? 0.1f : 0.03f;
            var p = new Pusher
            {
                Id = s.Id, Junction = s.Junction,
                Segments = s.Junction >= 0 && s.Junction < JunctionSegments.Length ? JunctionSegments[s.Junction] : 0,
                SlicerLong = s.SlicerLongitude, BoundaryLong = s.BoundaryLongitude, PusherLong = s.BoundaryLongitude - amount,
                BoundaryWeights = s.BoundaryWeights,
            };
            var keys = ma.Get("PushCurves").Get(s.Id).Arr();
            if (keys.Count > 0)
            {
                p.Curve = new float[keys.Count][];
                for (int k = 0; k < keys.Count; k++)
                {
                    var kf = keys[k].Arr();
                    p.Curve[k] = new float[6];
                    for (int q = 0; q < 6 && q < kf.Count; q++) p.Curve[k][q] = kf[q].F();
                }
            }
            else p.Curve = InOutCurve;
            if (ma.Get("ExtrusionRatios").Get(s.Id) is object r) p.Ratio = r.F();
            var o = overrideOf?.Invoke(s.Id);
            if (o != null)
            {
                if (!o.Get("PusherEnabled").B()) continue;
                p.SlicerLong += o.Get("SlicerOffset").F();
                p.SlicerScale *= o.Get("SlicerScale").F();
                p.BoundaryLong += o.Get("PusherOffset").F();
                p.PusherLong += o.Get("PusherOffset").F();
                p.BoundaryScale *= o.Get("PusherScale").F();
            }
            var w = toWorld != null ? toWorld(s) : Matrix4x4.identity;
            p.O = w.MultiplyPoint3x4(s.Origin);
            p.L = w.MultiplyVector(s.Axis).normalized;
            p.P = w.MultiplyVector(s.Polar).normalized;
            p.SlicerCenter = w.MultiplyPoint3x4(s.SlicerExtrusion.Center);
            p.BoundaryCenter = w.MultiplyPoint3x4(s.BoundaryExtrusion.Center);
            p.SlicerPos = Array.ConvertAll(s.SlicerExtrusion.Positions, x => w.MultiplyPoint3x4(x));
            p.BoundaryPos = Array.ConvertAll(s.BoundaryExtrusion.Positions, x => w.MultiplyPoint3x4(x));
            plan.Pushers.Add(p);
        }
    }

    /// <summary>FittingPolicy.GetFittingCutCase(대상 = 몸)</summary>
    public static int CutCaseForBody(int fitterSlot)
    {
        switch (fitterSlot)
        {
            case SlotBottom: return CutBodyWithBottom;
            case SlotTop: return CutBodyWithTop;
            case SlotRobe: case SlotOnePiece: case SlotOnePieceWithShoes: return CutBodyWithOnePiece;
            case SlotFoot: return CutBodyWithFoot;
            default: return CutDefault;
        }
    }

    /// <summary>
    /// FittingCutPolicy.Apply. overrides 는 분리면 Id → 보정(SeparatorOverrideInfo, SJON 객체).
    /// 보정이 있으면 SlicerEnabled=false 인 분리면은 쓰지 않고, 노멀을 SlicerRotation(도)만큼 돌린 뒤 SlicerOffset 만큼 노멀 방향으로 민다.
    /// </summary>
    public static Plan MakePlan(string key, ClothingUnit fitter, int cutCase, Func<string, object> overrideOf, Func<ClothingUnit.Separator, Matrix4x4> toWorld, PushOptions push = null)
    {
        var plan = new Plan { Key = key, CutCase = cutCase };
        if (push != null) AddPushers(plan, fitter, push, overrideOf, toWorld);
        bool hasCenterOrHip = false;
        int count = 0;
        foreach (var s in fitter.Separators)
        {
            if (count++ >= MaxPlanes) break;
            int j = s.Junction;
            uint mask = j >= 0 && j < JunctionSegments.Length ? JunctionSegments[j] : 0;
            var normal = s.Axis;
            var point = s.Origin + s.Axis * s.SlicerLongitude;
            var o = overrideOf?.Invoke(s.Id);
            if (o != null)
            {
                if (!o.Get("SlicerEnabled").B()) continue;
                var rot = o.Get("SlicerRotation").Arr();
                if (rot.Count >= 3) normal = (Quaternion.Euler(rot[0].F(), rot[1].F(), rot[2].F()) * normal).normalized;
                point += normal * o.Get("SlicerOffset").F();
            }
            if (toWorld != null)
            {
                var w = toWorld(s);
                point = w.MultiplyPoint3x4(point);
                normal = w.MultiplyVector(normal).normalized;
            }
            bool dominant = false;
            if (j == JLeftHipJoint || j == JRightHipJoint)
            {
                hasCenterOrHip = true;
                if (cutCase == CutBodyWithOnePiece) mask |= 1u << SegNeck;
            }
            else if (j == JCenter)
            {
                hasCenterOrHip = true;
                if (cutCase == CutBodyWithFoot || cutCase == CutTopWithBottom) { mask |= LowerLegsAndFeet; dominant = true; }
                else if (cutCase == CutBodyWithOnePiece || cutCase == CutBodyWithTop) { mask |= 1u << SegNeck; dominant = true; }
            }
            plan.Planes.Add(new CutPlane { Point = point, Normal = normal, Segments = mask, Dominant = dominant, Id = s.Id });
        }
        if (cutCase == CutBodyWithOnePiece && !hasCenterOrHip && plan.Planes.Count > 0) plan.DropSegments = (1u << 0) | (1u << SegNeck);
        return plan;
    }

    /// <summary>
    /// 메시(몸·의상)를 자르는 쪽 계획대로 자른 새 메시. triSource[s][k] 는 서브메시 s 의 k 번째 삼각형의 파일 삼각형 번호,
    /// segments 는 파일 삼각형 번호 → 부위(없으면 몸통). 빠지거나 잘린 삼각형이 없으면 null(원본을 그대로 쓴다).
    /// </summary>
    public static Mesh Cut(string target, Mesh full, Vector3[] world, int[][] triSource, Dictionary<int, byte> segments, List<Plan> plans, Action<string> log, PushContext push = null)
    {
        var m = new Editable(full, world);
        var tris = new List<Tri>();
        for (int s = 0; s < full.subMeshCount; s++)
        {
            var t = full.GetTriangles(s);
            var src = triSource != null && s < triSource.Length ? triSource[s] : null;
            for (int i = 0, k = 0; i + 2 < t.Length; i += 3, k++)
            {
                byte seg = 0;
                if (src != null && k < src.Length && segments != null && segments.TryGetValue(src[k], out var sg) && sg < SegmentCount) seg = sg;
                tris.Add(new Tri { A = t[i], B = t[i + 1], C = t[i + 2], Seg = seg, Sub = s });
            }
        }
        int total = tris.Count;
        bool changed = false;
        foreach (var plan in plans)
        {
            int before = tris.Count, vBefore = m.Pos.Count;
            tris = Apply(m, tris, plan, out int dropped, out int removed, out int clipped);
            if (dropped + removed + clipped > 0) changed = true;
            log?.Invoke($"자르기 {target} ← {plan.Key}: 경우 {plan.CutCase}, 평면 {plan.Planes.Count} [{Describe(plan)}], 부위 통째 {Convert.ToString(plan.DropSegments, 2)} → " +
                        $"삼각형 {before}→{tris.Count} (부위 제외 {dropped}, 덮여 제외 {removed}, 잘림 {clipped}), 새 정점 {m.Pos.Count - vBefore}");
            if (push != null && plan.Pushers.Count > 0)
            {
                int pushed = Push(m, tris, plan, push, out float maxMove);
                if (pushed > 0) changed = true;
                log?.Invoke($"밀기 {target} ← {plan.Key}: 분리면 {plan.Pushers.Count}, 민 정점 {pushed}, 최대 이동 {maxMove * 1000:F1}mm");
            }
        }
        if (!changed) { log?.Invoke($"자르기 {target}: 바뀐 삼각형 없음 ({total})"); return null; }
        var cut = m.Build(full, tris, push);
        log?.Invoke($"자르기 {target}: 삼각형 {total} → {tris.Count}, 정점 {full.vertexCount} → {cut.vertexCount}");
        return cut;
    }

    static string Describe(Plan p)
    {
        var sb = new StringBuilder();
        foreach (var c in p.Planes)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append($"{c.Id}{(c.Dominant ? "*" : "")} ({c.Point.x:F3},{c.Point.y:F3},{c.Point.z:F3}) 노멀 ({c.Normal.x:F2},{c.Normal.y:F2},{c.Normal.z:F2}) 부위 {Convert.ToString(c.Segments, 2)}");
        }
        return sb.ToString();
    }

    struct Tri { public int A, B, C, Sub; public byte Seg; }

    static List<Tri> Apply(Editable m, List<Tri> tris, Plan plan, out int dropped, out int removed, out int clipped)
    {
        dropped = removed = clipped = 0;
        int np = Math.Min(plan.Planes.Count, MaxPlanes);
        if (np == 0 && plan.DropSegments == 0) return tris;
        // 부위 → 관련 평면 비트, 우선 평면 비트
        var all = new uint[SegmentCount]; var dom = new uint[SegmentCount];
        for (int p = 0; p < np; p++)
            for (int s = 0; s < SegmentCount; s++)
                if ((plan.Planes[p].Segments & (1u << s)) != 0) { all[s] |= 1u << p; if (plan.Planes[p].Dominant) dom[s] |= 1u << p; }
        var inside = new List<uint>(m.Pos.Count);
        for (int v = 0; v < m.Pos.Count; v++) inside.Add(Inside(plan, np, m.World[v]));
        var cache = new Dictionary<(int, int, int), int>();
        var res = new List<Tri>(tris.Count);
        foreach (var t in tris)
        {
            if ((plan.DropSegments & (1u << t.Seg)) != 0) { dropped++; continue; }
            uint i0 = inside[t.A], i1 = inside[t.B], i2 = inside[t.C];
            uint any = i0 | i1 | i2, every = i0 & i1 & i2;
            if ((dom[t.Seg] & ~any) != 0 || (all[t.Seg] != 0 && (any & all[t.Seg]) == 0)) { removed++; continue; }
            uint clip = ~every & all[t.Seg] & any;
            if (clip == 0) { res.Add(t); continue; }
            clipped++;
            var poly = new List<int> { t.A, t.B, t.C };
            for (int p = 0; p < np && poly.Count >= 3; p++)
            {
                if ((clip & (1u << p)) == 0) continue;
                var next = new List<int>(poly.Count + 2);
                for (int k = 0; k < poly.Count; k++)
                {
                    int a = poly[k], b = poly[(k + 1) % poly.Count];
                    float da = Dist(plan.Planes[p], m.World[a]), db = Dist(plan.Planes[p], m.World[b]);
                    if (da < 0) next.Add(a);
                    if ((da < 0) != (db < 0))
                    {
                        var key = a < b ? (a, b, p) : (b, a, p);
                        if (!cache.TryGetValue(key, out int nv))
                        {
                            int lo = Math.Min(a, b), hi = Math.Max(a, b);
                            float dl = lo == a ? da : db, dh = lo == a ? db : da;
                            nv = m.Lerp(lo, hi, dl / (dl - dh));
                            inside.Add(Inside(plan, np, m.World[nv]));
                            cache[key] = nv;
                        }
                        next.Add(nv);
                    }
                }
                poly = next;
            }
            for (int k = 1; k + 1 < poly.Count; k++) res.Add(new Tri { A = poly[0], B = poly[k], C = poly[k + 1], Seg = t.Seg, Sub = t.Sub });
        }
        return res;
    }

    static float Dist(in CutPlane p, Vector3 v) => Vector3.Dot(v - p.Point, p.Normal);

    static uint Inside(Plan plan, int np, Vector3 v)
    {
        uint r = 0;
        for (int p = 0; p < np; p++) if (Dist(plan.Planes[p], v) < 0) r |= 1u << p;
        return r;
    }

    // ───────── 밀기(게임 NativeMeshPushingJob) ─────────
    //   조각 하나(계획)의 밀기마다: 부위가 맞고 자르기·밀기 시작 평면에 걸치거나 무게중심 높이가 [밀기 시작, 자르기) 사이인 삼각형의 정점을 모은다.
    //   ① 경계 돌출 좌표로 [밀기 시작, 경계] 구간을 곡선 가중치로 밀고 본 가중치를 경계 본 가중치 쪽으로 옮긴다.
    //   ② 경계와 자르기 경도가 다르면 자르기 돌출 좌표로 [자르기, 경계] 구간을 가중치 1로 민다(본 가중치는 그대로).
    //   정점마다 원통 좌표(반지름 r, 방위각, 높이 h)로 돌출 꼭짓점 둘레의 같은 방위 지점을 찾아, r/r돌출 > 0.95 인 정점을
    //   반지름 tanh(2·r/r돌출)·돌출 비율·배율 위치(높이는 그대로)로 옮긴다. 정점마다 가장 많이 움직인 결과를 쓰고 계획 끝에 한꺼번에 반영한다.
    struct PushResult { public Vector3 Pos, Diff; public List<(int bone, float weight)> Weights; }

    static int Push(Editable m, List<Tri> tris, Plan plan, PushContext ctx, out float maxMove)
    {
        maxMove = 0;
        var results = new Dictionary<int, PushResult>();
        var verts = new HashSet<int>();
        foreach (var p in plan.Pushers)
        {
            verts.Clear();
            foreach (var t in tris)
                if ((p.Segments & (1u << t.Seg)) != 0 && IntersectsPusher(p, m.World[t.A], m.World[t.B], m.World[t.C]))
                { verts.Add(t.A); verts.Add(t.B); verts.Add(t.C); }
            if (verts.Count == 0) continue;
            PushVertices(m, ctx, p, p.BoundaryCenter, p.BoundaryPos, true, p.BoundaryScale, p.PusherLong, p.BoundaryLong, false, results, verts);
            if (Math.Abs(p.BoundaryLong - p.SlicerLong) >= 1e-5f)
                PushVertices(m, ctx, p, p.SlicerCenter, p.SlicerPos, false, p.SlicerScale, p.SlicerLong, p.BoundaryLong, true, results, verts);
        }
        foreach (var kv in results)
        {
            int v = kv.Key; var r = kv.Value;
            maxMove = Mathf.Max(maxMove, r.Diff.magnitude);
            m.World[v] = r.Pos;
            // 새 본 가중치로 기본 자세 월드 → 메시 공간: (Σ w·본 스키닝 행렬)⁻¹
            var skin = new Matrix4x4();
            float sum = 0;
            foreach (var (b, w) in r.Weights)
            {
                if (b < 0 || b >= ctx.Skin.Count) continue;
                var s = ctx.Skin[b];
                for (int e = 0; e < 16; e++) skin[e] += s[e] * w;
                sum += w;
            }
            if (sum > 1e-6f && Mathf.Abs(skin.determinant) > 1e-12f) m.Pos[v] = skin.inverse.MultiplyPoint3x4(r.Pos);
            if (m.hasBw) m.bw[v] = ToBoneWeight(r.Weights, m.bw[v]);
        }
        return results.Count;
    }

    static void PushVertices(Editable m, PushContext ctx, Pusher p, Vector3 center, Vector3[] ext, bool useBoundaryWeights, float scale,
                             float minLong, float maxLong, bool alwaysFull, Dictionary<int, PushResult> results, HashSet<int> verts)
    {
        foreach (int v in verts)
        {
            var pos = m.World[v];
            Cyl(p, pos, out float r, out _, out float h);
            if (!TryGetExtrusion(center, ext, pos, p.L, out int i0, out int i1, out float lt)) continue;
            Cyl(p, ExtPos(center, ext, i0, i1, lt, scale), out float re, out _, out _);
            if (re <= 0) continue;
            float q = r / re;
            if (!(q > 0.95f)) continue;
            if (!(h > minLong - 1e-5f) || !(maxLong + 0.03f > h)) continue;
            float s = (float)Math.Tanh(q + q) * p.Ratio * scale;
            Cyl(p, ExtPos(center, ext, i0, i1, lt, s), out float r2, out float az2, out _);
            var target = ToCartesian(p, r2, az2, h);
            float t = maxLong - minLong != 0 ? Mathf.Clamp01((h - minLong) / (maxLong - minLong)) : 1;
            float w = alwaysFull ? 1 : Mathf.Clamp01(Evaluate(p.Curve, t));
            var np = pos + (target - pos) * w;
            var diff = np - pos;
            if (results.TryGetValue(v, out var old) && old.Diff.sqrMagnitude > diff.sqrMagnitude) continue;
            var weights = Named(m, ctx, v);
            if (useBoundaryWeights && p.BoundaryWeights != null && i0 < p.BoundaryWeights.Length && i1 < p.BoundaryWeights.Length
                && p.BoundaryWeights[i0] != null && p.BoundaryWeights[i1] != null)
            {
                var a = Resolve(ctx, p.BoundaryWeights[i0]);
                var b = Resolve(ctx, p.BoundaryWeights[i1]);
                weights = LerpWeights(weights, LerpWeights(a, b, lt), w);
            }
            results[v] = new PushResult { Pos = np, Diff = diff, Weights = weights };
        }
    }

    /// <summary>원통 좌표(NativeCylindricalCoordinateVector3): 높이 = (p−원점)·길이 축, 반지름 = 수직 거리, 방위각 = atan2((극 축 × n)·길이 축, n·극 축).</summary>
    static void Cyl(Pusher p, Vector3 pos, out float r, out float az, out float h)
    {
        var d = pos - p.O;
        h = Vector3.Dot(d, p.L);
        var perp = d - p.L * h;
        r = perp.magnitude;
        if (r < 1.1920929e-07f) { az = 0; return; }
        var n = perp / r;
        az = Mathf.Atan2(Vector3.Dot(Vector3.Cross(p.P, n), p.L), Vector3.Dot(n, p.P));
    }

    static Vector3 ToCartesian(Pusher p, float r, float az, float h) =>
        p.O + p.L * h + (Quaternion.AngleAxis(az * Mathf.Rad2Deg, p.L) * p.P) * r;

    /// <summary>돌출 꼭짓점 사이 지점: 중심 + (lerp(a, b, t) − 중심) × 배율.</summary>
    static Vector3 ExtPos(Vector3 center, Vector3[] ext, int i0, int i1, float t, float scale) =>
        center + (Vector3.Lerp(ext[i0], ext[i1], t) - center) * scale;

    /// <summary>
    /// NativeExtrusionCoordinate.TryGetExtrusion: 중심과 정점 방향, 길이 축이 이루는 반평면(노멀 = −(길이 축 × 방향))을
    /// 둘레 꺾은선이 양수 쪽에서 음수 쪽으로 지나는 첫 변을 찾는다.
    /// </summary>
    static bool TryGetExtrusion(Vector3 center, Vector3[] ext, Vector3 pos, Vector3 axis, out int i0, out int i1, out float t)
    {
        i0 = i1 = 0; t = 0;
        int k = ext.Length;
        if (k == 0) return false;
        var d = pos - center;
        if (d.sqrMagnitude < 1e-12f) return false;
        var c = Vector3.Cross(axis, d / d.magnitude);
        if (c.sqrMagnitude < 1e-12f) return false;
        var normal = -c;
        for (int i = 0; i < k; i++)
        {
            var a = ext[i]; var dir = ext[(i + 1) % k] - a;
            float len = dir.magnitude;
            if (len < 1e-5f) continue;
            dir /= len;
            float denom = -Vector3.Dot(normal, dir);
            if (denom <= 0) continue;
            float hit = Vector3.Dot(a - center, normal) / denom;
            if (hit <= 0 || !(len + 1e-5f > hit)) continue;
            i0 = i; i1 = (i + 1) % k; t = Mathf.Clamp01(hit / len);
            return true;
        }
        return false;
    }

    /// <summary>삼각형이 자르기·밀기 시작 평면에 걸치거나, 무게중심 높이가 [밀기 시작, 자르기) 안인가.</summary>
    static bool IntersectsPusher(Pusher p, Vector3 a, Vector3 b, Vector3 c)
    {
        bool Straddles(float lon)
        {
            var o = p.O + p.L * lon;
            int n = (Vector3.Dot(a - o, p.L) < 0 ? 1 : 0) + (Vector3.Dot(b - o, p.L) < 0 ? 1 : 0) + (Vector3.Dot(c - o, p.L) < 0 ? 1 : 0);
            return n != 0 && n != 3;
        }
        if (Straddles(p.SlicerLong) || Straddles(p.PusherLong)) return true;
        float h = Vector3.Dot((a + b + c) / 3f - p.O, p.L);
        return h >= p.PusherLong && p.SlicerLong > h;
    }

    /// <summary>BezierCurveSlice.Evaluate: 시간 범위로 자르고 구간 키 둘로 보간(가중치 0 이면 에르미트, 아니면 가중 베지어, 무한 기울기는 계단).</summary>
    static float Evaluate(float[][] keys, float t)
    {
        if (keys == null || keys.Length == 0) return 0;
        if (keys.Length == 1) return keys[0][1];
        t = Mathf.Clamp(t, keys[0][0], keys[keys.Length - 1][0]);
        int hi = 1;
        while (hi < keys.Length - 1 && keys[hi][0] <= t) hi++;
        var k0 = keys[hi - 1]; var k1 = keys[hi];
        if (float.IsPositiveInfinity(k0[3]) || float.IsPositiveInfinity(k1[2])) return k0[1];
        if (float.IsNegativeInfinity(k0[3]) || float.IsNegativeInfinity(k1[2])) return k1[1];
        float dt = k1[0] - k0[0];
        if (dt == 0) return k0[1];
        if (k0[5] == 0 && k1[4] == 0)
        {
            float u = (t - k0[0]) / dt, u2 = u * u, u3 = u2 * u;
            return (2 * u3 - 3 * u2 + 1) * k0[1] + (u3 - 2 * u2 + u) * dt * k0[3] + (u3 - u2) * dt * k1[2] + (-2 * u3 + 3 * u2) * k1[1];
        }
        // 가중 베지어: 제어점 (t0 + w0·dt, v0 + w0·dt·m0), (t1 − w1·dt, v1 − w1·dt·m1). x(u) = t 를 이분법으로 푼다.
        float x0 = k0[0], x1 = k0[0] + k0[5] * dt, x2 = k1[0] - k1[4] * dt, x3 = k1[0];
        float y0 = k0[1], y1 = k0[1] + k0[5] * dt * k0[3], y2 = k1[1] - k1[4] * dt * k1[2], y3 = k1[1];
        float B(float u, float p0, float p1, float p2, float p3) { float iu = 1 - u; return iu * iu * iu * p0 + 3 * iu * iu * u * p1 + 3 * iu * u * u * p2 + u * u * u * p3; }
        float lo = 0, up = 1;
        for (int it = 0; it < 40; it++) { float mid = (lo + up) * 0.5f; if (B(mid, x0, x1, x2, x3) < t) lo = mid; else up = mid; }
        return B((lo + up) * 0.5f, y0, y1, y2, y3);
    }

    static List<(int bone, float weight)> Named(Editable m, PushContext ctx, int v)
    {
        var l = new List<(int, float)>(4);
        if (!m.hasBw) return l;
        var w = m.bw[v];
        if (w.weight0 > 0) l.Add((w.boneIndex0, w.weight0));
        if (w.weight1 > 0) l.Add((w.boneIndex1, w.weight1));
        if (w.weight2 > 0) l.Add((w.boneIndex2, w.weight2));
        if (w.weight3 > 0) l.Add((w.boneIndex3, w.weight3));
        return l;
    }

    static List<(int bone, float weight)> Resolve(PushContext ctx, List<(string bone, float weight)> src)
    {
        var l = new List<(int, float)>(src.Count);
        foreach (var (b, w) in src) { int i = ctx.Index(b); if (i >= 0 && w > 0) l.Add((i, w)); }
        return l;
    }

    /// <summary>NativeBoneWeight.Lerp: a·(1−t) + b·t 를 본별로 더해 큰 순서 4개를 남기고 합 1로 맞춘다.</summary>
    static List<(int bone, float weight)> LerpWeights(List<(int bone, float weight)> a, List<(int bone, float weight)> b, float t)
    {
        var acc = new Dictionary<int, float>();
        foreach (var (i, w) in a) { acc.TryGetValue(i, out var o); acc[i] = o + w * (1 - t); }
        foreach (var (i, w) in b) { acc.TryGetValue(i, out var o); acc[i] = o + w * t; }
        var l = new List<(int bone, float weight)>();
        foreach (var kv in acc) if (kv.Value > 0) l.Add((kv.Key, kv.Value));
        l.Sort((x, y) => y.weight.CompareTo(x.weight));
        if (l.Count > 4) l.RemoveRange(4, l.Count - 4);
        float sum = 0; foreach (var x in l) sum += x.weight;
        if (sum <= 0) return a;
        for (int i = 0; i < l.Count; i++) l[i] = (l[i].bone, l[i].weight / sum);
        return l;
    }

    static BoneWeight ToBoneWeight(List<(int bone, float weight)> l, BoneWeight fallback)
    {
        if (l.Count == 0) return fallback;
        var w = new BoneWeight();
        if (l.Count > 0) { w.boneIndex0 = l[0].bone; w.weight0 = l[0].weight; }
        if (l.Count > 1) { w.boneIndex1 = l[1].bone; w.weight1 = l[1].weight; }
        if (l.Count > 2) { w.boneIndex2 = l[2].bone; w.weight2 = l[2].weight; }
        if (l.Count > 3) { w.boneIndex3 = l[3].bone; w.weight3 = l[3].weight; }
        return w;
    }

    /// <summary>정점을 덧붙일 수 있는 메시 사본(위치·노멀·UV0·본 가중치·블렌드셰이프).</summary>
    sealed class Editable
    {
        public readonly List<Vector3> Pos = new List<Vector3>(), Nrm = new List<Vector3>(), World;
        readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<BoneWeight> bw = new List<BoneWeight>();
        readonly List<(string name, float weight, List<Vector3> dp, List<Vector3> dn)> shapes = new List<(string, float, List<Vector3>, List<Vector3>)>();
        public readonly bool hasNrm, hasUv, hasBw;

        public Editable(Mesh m, Vector3[] world)
        {
            m.GetVertices(Pos); m.GetNormals(Nrm); m.GetUVs(0, uv);
            World = world != null && world.Length == Pos.Count ? new List<Vector3>(world) : new List<Vector3>(Pos);
            bw.AddRange(m.boneWeights);
            hasNrm = Nrm.Count == Pos.Count; hasUv = uv.Count == Pos.Count; hasBw = bw.Count == Pos.Count;
            int n = m.vertexCount;
            for (int k = 0; k < m.blendShapeCount; k++)
                for (int f = 0; f < m.GetBlendShapeFrameCount(k); f++)
                {
                    var dp = new Vector3[n]; var dn = new Vector3[n];
                    m.GetBlendShapeFrameVertices(k, f, dp, dn, null);
                    shapes.Add((m.GetBlendShapeName(k), m.GetBlendShapeFrameWeight(k, f), new List<Vector3>(dp), new List<Vector3>(dn)));
                }
        }

        public int Lerp(int a, int b, float t)
        {
            Pos.Add(Vector3.Lerp(Pos[a], Pos[b], t));
            World.Add(Vector3.Lerp(World[a], World[b], t));
            if (hasNrm) Nrm.Add(Vector3.Lerp(Nrm[a], Nrm[b], t).normalized);
            if (hasUv) uv.Add(Vector2.Lerp(uv[a], uv[b], t));
            if (hasBw) bw.Add(LerpWeight(bw[a], bw[b], t));
            foreach (var s in shapes) { s.dp.Add(Vector3.Lerp(s.dp[a], s.dp[b], t)); s.dn.Add(Vector3.Lerp(s.dn[a], s.dn[b], t)); }
            return Pos.Count - 1;
        }

        static BoneWeight LerpWeight(BoneWeight x, BoneWeight y, float t)
        {
            var acc = new Dictionary<int, float>();
            void Add(int i, float w) { if (w <= 0) return; acc.TryGetValue(i, out var o); acc[i] = o + w; }
            Add(x.boneIndex0, x.weight0 * (1 - t)); Add(x.boneIndex1, x.weight1 * (1 - t)); Add(x.boneIndex2, x.weight2 * (1 - t)); Add(x.boneIndex3, x.weight3 * (1 - t));
            Add(y.boneIndex0, y.weight0 * t); Add(y.boneIndex1, y.weight1 * t); Add(y.boneIndex2, y.weight2 * t); Add(y.boneIndex3, y.weight3 * t);
            var l = new List<KeyValuePair<int, float>>(acc);
            l.Sort((p, q) => q.Value.CompareTo(p.Value));
            float sum = 0; for (int k = 0; k < Math.Min(4, l.Count); k++) sum += l[k].Value;
            if (sum <= 0) return x;
            var w = new BoneWeight();
            if (l.Count > 0) { w.boneIndex0 = l[0].Key; w.weight0 = l[0].Value / sum; }
            if (l.Count > 1) { w.boneIndex1 = l[1].Key; w.weight1 = l[1].Value / sum; }
            if (l.Count > 2) { w.boneIndex2 = l[2].Key; w.weight2 = l[2].Value / sum; }
            if (l.Count > 3) { w.boneIndex3 = l[3].Key; w.weight3 = l[3].Value / sum; }
            return w;
        }

        public Mesh Build(Mesh full, List<Tri> tris, PushContext push)
        {
            var cut = new Mesh { name = full.name + "_cut" };
            if (Pos.Count > 65000) cut.indexFormat = IndexFormat.UInt32;
            cut.SetVertices(Pos);
            if (hasNrm) cut.SetNormals(Nrm);
            if (hasUv) cut.SetUVs(0, uv);
            if (hasBw) cut.boneWeights = bw.ToArray();
            if (push != null && push.AddedBindposes.Count > 0)
            {
                var bp = new List<Matrix4x4>(full.bindposes);
                bp.AddRange(push.AddedBindposes);
                cut.bindposes = bp.ToArray();
            }
            else cut.bindposes = full.bindposes;
            cut.subMeshCount = full.subMeshCount;
            var subs = new List<int>[full.subMeshCount];
            for (int s = 0; s < subs.Length; s++) subs[s] = new List<int>();
            foreach (var t in tris) { var l = subs[t.Sub]; l.Add(t.A); l.Add(t.B); l.Add(t.C); }
            for (int s = 0; s < subs.Length; s++) cut.SetTriangles(subs[s], s, false);
            foreach (var s in shapes) cut.AddBlendShapeFrame(s.name, s.weight, s.dp.ToArray(), hasNrm ? s.dn.ToArray() : null, null);
            if (!hasNrm) cut.RecalculateNormals();
            cut.RecalculateBounds();
            return cut;
        }
    }
}

/// <summary>
/// 옷 맞춤 대상·순서 규칙(게임 NativeMorphableAssetFitter.BuildPlans·FittingPolicy·ClothFitOrderPolicy(V2)·ClothJunctionPointSizeResolver).
/// 몸과 .ClothingUnit 이 있는 파츠가 "조각"이다. 순서대로 정렬한 뒤, 조각의 렌더러마다 재질 종류로 자를 조각을 고른다.
///   Skin(피부 조각) → 다른 모든 조각이 자른다. Cloth → 순서상 뒤의 조각이 자른다.
///   Steel → 뒤의 조각 중 공통 접합부에 VeryHighest 크기가 있는 조각만 자른다.
/// </summary>
public static class ClothFit
{
    public const int SlotBody = 1, SlotTop = 7, SlotBottom = 8, SlotOnePiece = 9, SlotHand = 10, SlotFoot = 11, SlotRobe = 12, SlotOnePieceWithShoes = 13;
    public const int MatCloth = 0, MatSkin = 1, MatSteel = 2;
    public const int SizeVeryLowest = -20, SizeVeryHighest = 20;

    /// <summary>ClothFitOrderHint. 접합부 크기는 허리·손목·발목 순(값이 없으면 null).</summary>
    public sealed class Hint
    {
        public string Name;
        public bool Actor;
        public int Slot;
        public readonly int?[] Junction = new int?[3];
        public bool UseV1;
    }

    /// <summary>JunctionPointSize 이름 → 값. 모르면 null.</summary>
    public static int? JunctionSize(string s)
    {
        switch (s)
        {
            case "Medium": return 0;
            case "Lowest": return -10;
            case "Highest": return 10;
            case "VeryLowest": return SizeVeryLowest;
            case "VeryHighest": return SizeVeryHighest;
            default: return null;
        }
    }

    public static int MaterialType(string s) => s == "Skin" ? MatSkin : s == "Steel" ? MatSteel : MatCloth;

    /// <summary>FittingPolicy.GetFittingCutCase(대상, 자르는 쪽).</summary>
    public static int CutCase(int fitted, int fitter)
    {
        if (fitted == SlotTop) return fitter == SlotBottom ? 4 : 5;
        if (fitted == SlotBody) return BodyCut.CutCaseForBody(fitter);
        return 5;
    }

    /// <summary>FittingPolicy.IsFitIgnoringCase: 상의의 "outer" 메시는 하의·신발이, 하의·원피스·로브의 "skirt" 메시는 신발이 자르지 않는다.</summary>
    public static bool IsFitIgnoringCase(string meshName, int fitted, int fitter)
    {
        if (meshName == null) return false;
        if (fitted == SlotTop && (fitter == SlotBottom || fitter == SlotFoot) && meshName.IndexOf("outer", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        bool skirtOwner = fitted == SlotBottom || fitted == SlotOnePiece || fitted == SlotRobe;
        return skirtOwner && fitter == SlotFoot && meshName.IndexOf("skirt", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>자르는 쪽에 대상과 공통인 접합부가 있고 그 크기가 VeryHighest 인가(쇠 재질 렌더러).</summary>
    public static bool HasVeryHighestOnCommonJunction(Hint fitter, Hint fitted)
    {
        for (int k = 0; k < 3; k++)
            if (fitted.Junction[k].HasValue && fitter.Junction[k] == SizeVeryHighest) return true;
        return false;
    }

    /// <summary>ClothJunctionPointSizeResolver: 목록의 신발을 신으면 목록의 의상 발목 크기를 VeryLowest 로 바꾼다.</summary>
    public static void ResolveJunctionSizes(IList<Hint> hints, ICollection<string> costumeNames, ICollection<string> shoeNames)
    {
        if (costumeNames == null || shoeNames == null) return;
        bool shoe = false;
        foreach (var h in hints) if (shoeNames.Contains(h.Name)) { shoe = true; break; }
        if (!shoe) return;
        foreach (var h in hints) if (costumeNames.Contains(h.Name)) h.Junction[2] = SizeVeryLowest;
    }

    static bool HasAnyCommonJunction(Hint a, Hint b)
    {
        for (int k = 0; k < 3; k++) if (a.Junction[k].HasValue && b.Junction[k].HasValue) return true;
        return false;
    }

    static int Priority1(int slot)
    {
        switch (slot)
        {
            case SlotTop: case SlotFoot: return 8;
            case SlotBottom: case SlotOnePiece: case SlotOnePieceWithShoes: return 7;
            case SlotHand: return 9;
            case SlotRobe: return 10;
            default: return 0;
        }
    }

    static int Priority2(int slot)
    {
        switch (slot)
        {
            case SlotTop: return 8;
            case SlotBottom: case SlotOnePiece: case SlotOnePieceWithShoes: return 6;
            case SlotHand: return 9;
            case SlotFoot: return 7;
            case SlotRobe: return 10;
            default: return 0;
        }
    }

    /// <summary>a 가 b 보다 나중에 맞춰지는가(1단계, V1 과 같다): 로브가 아니면 공통 접합부가 있어야 하고, 접합부 크기가 크면 나중, 같으면 부위 우선순위.</summary>
    static bool Phase1(Hint a, Hint b)
    {
        if (b.Actor) return true;
        if (a.Actor) return false;
        if (a.Slot != SlotRobe && b.Slot != SlotRobe && !HasAnyCommonJunction(a, b)) return false;
        for (int k = 0; k < 3; k++)
            if (a.Junction[k].HasValue && b.Junction[k].HasValue && a.Junction[k].Value != b.Junction[k].Value)
                return a.Junction[k].Value > b.Junction[k].Value;
        return Priority1(a.Slot) > Priority1(b.Slot);
    }

    /// <summary>2단계: 공통 접합부가 없는 상의·신발 사이에서 상의를 나중에.</summary>
    static bool Phase2(Hint a, Hint b)
    {
        if (b.Actor) return true;
        if (a.Actor) return false;
        if (HasAnyCommonJunction(a, b)) return false;
        if (!((a.Slot == SlotTop && b.Slot == SlotFoot) || (a.Slot == SlotFoot && b.Slot == SlotTop))) return false;
        return Priority2(a.Slot) > Priority2(b.Slot);
    }

    /// <summary>
    /// 맞춤 순서로 정렬한다(앞 = 먼저 맞춤 = 안쪽). i 번째 조각보다 "나중"인 j 가 있으면 i 를 그런 마지막 j 자리로 옮기고 다시 본다.
    /// V2 는 같은 조각이 세 번째로 옮겨지려 하면 멈추고 다음 자리로 넘어간다. 이어서 상의·신발이 붙어 있으면 2단계를 한 번 더 돈다.
    /// </summary>
    public static void Sort<T>(List<T> list, Func<T, Hint> hint, bool v1)
    {
        SortWith(list, hint, Phase1, !v1);
        if (v1) return;
        int top = -1, foot = -1;
        for (int i = 0; i < list.Count && (top < 0 || foot < 0); i++)
        {
            int s = hint(list[i]).Slot;
            if (s == SlotTop) top = i;
            if (s == SlotFoot) foot = i;
        }
        if (Math.Abs(top - foot) == 1) SortWith(list, hint, Phase2, true);
    }

    static void SortWith<T>(List<T> list, Func<T, Hint> hint, Func<Hint, Hint, bool> later, bool guard)
    {
        var moved = new Dictionary<string, int>(StringComparer.Ordinal);
        int i = 0, steps = 0, maxSteps = list.Count * list.Count * 4 + 16;
        while (i < list.Count - 1)
        {
            var a = hint(list[i]);
            int best = i;
            for (int j = i + 1; j < list.Count; j++) if (later(a, hint(list[j]))) best = j;
            if (best == i || ++steps > maxSteps) { moved.Clear(); i++; continue; }
            var x = list[i];
            list.RemoveAt(i);
            list.Insert(best, x);
            if (!guard) continue;
            string key = a.Name ?? "";
            moved.TryGetValue(key, out int c);
            if (c > 1) { moved.Clear(); i++; continue; }
            moved[key] = c + 1;
        }
    }
}
