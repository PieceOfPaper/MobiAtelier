// 몸 자르기: .ClothingUnit.bytes(의상 분리면·삼각형 부위)를 읽어 게임과 같은 규칙으로 몸 메시를 자른다.
//
// .ClothingUnit.bytes (리틀엔디언, 문자열은 7비트 길이 + UTF-8)
//   i32 AppearanceSlot, i32 n, i32[n] 렌더러 번호(<모델>_<번호>.SkinnedModularMesh)
//   i32 분리면 수, 분리면[]:
//     string Id(CenterBottom, LLeg …), i32 FittingJunctionType,
//     원통 좌표 기준: f32x3 길이 축, f32x3 극 축, f32x3 원점
//     자르기 돌출 좌표: f32x3 중심, i32 k, f32x3[k], 평면(f32x3 위치, f32x3 노멀, f32 거리)
//     f32 자르기 경도
//     경계 돌출 좌표(같은 형식), f32 경계 경도
//     i32 경계 본 가중치 수, [i32 정점, i32 k, (string 본, f32 가중치)[k]], i32 m, 24바이트[m]
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
    public sealed class Separator
    {
        public string Id;
        public int Junction;
        public Vector3 Axis, Origin;
        public float SlicerLongitude;
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
            void Extrusion() { V3(); int k = r.ReadInt32(); Skip(12L * k); Skip(28); }

            var u = new ClothingUnit { Slot = r.ReadInt32() };
            int n = r.ReadInt32();
            u.RendererIndices = new int[n];
            for (int i = 0; i < n; i++) u.RendererIndices[i] = r.ReadInt32();
            int ns = r.ReadInt32();
            for (int s = 0; s < ns; s++)
            {
                var sep = new Separator { Id = r.ReadString(), Junction = r.ReadInt32() };
                sep.Axis = V3(); V3(); sep.Origin = V3();
                Extrusion();
                sep.SlicerLongitude = r.ReadSingle();
                Extrusion();
                r.ReadSingle();
                int nb = r.ReadInt32();
                for (int b = 0; b < nb; b++)
                {
                    r.ReadInt32();
                    int k = r.ReadInt32();
                    for (int j = 0; j < k; j++) { r.ReadString(); r.ReadSingle(); }
                }
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
    public static Plan MakePlan(string key, ClothingUnit fitter, int cutCase, Func<string, object> overrideOf, Func<ClothingUnit.Separator, Matrix4x4> toWorld)
    {
        var plan = new Plan { Key = key, CutCase = cutCase };
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
    /// 몸 메시를 의상 계획대로 자른 새 메시. triSource[s][k] 는 서브메시 s 의 k 번째 삼각형의 파일 삼각형 번호,
    /// segments 는 파일 삼각형 번호 → 부위(없으면 몸통).
    /// </summary>
    public static Mesh Cut(Mesh full, Vector3[] world, int[][] triSource, Dictionary<int, byte> segments, List<Plan> plans, Action<string> log)
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
        foreach (var plan in plans)
        {
            int before = tris.Count, vBefore = m.Pos.Count;
            tris = Apply(m, tris, plan, out int dropped, out int removed, out int clipped);
            log?.Invoke($"몸 자르기 {plan.Key}: 경우 {plan.CutCase}, 평면 {plan.Planes.Count} [{Describe(plan)}], 부위 통째 {Convert.ToString(plan.DropSegments, 2)} → " +
                        $"삼각형 {before}→{tris.Count} (부위 제외 {dropped}, 덮여 제외 {removed}, 잘림 {clipped}), 새 정점 {m.Pos.Count - vBefore}");
        }
        var cut = m.Build(full, tris);
        log?.Invoke($"몸 자르기: 삼각형 {total} → {tris.Count}, 정점 {full.vertexCount} → {cut.vertexCount}");
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

    /// <summary>정점을 덧붙일 수 있는 메시 사본(위치·노멀·UV0·본 가중치·블렌드셰이프).</summary>
    sealed class Editable
    {
        public readonly List<Vector3> Pos = new List<Vector3>(), Nrm = new List<Vector3>(), World;
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<BoneWeight> bw = new List<BoneWeight>();
        readonly List<(string name, float weight, List<Vector3> dp, List<Vector3> dn)> shapes = new List<(string, float, List<Vector3>, List<Vector3>)>();
        readonly bool hasNrm, hasUv, hasBw;

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

        public Mesh Build(Mesh full, List<Tri> tris)
        {
            var cut = new Mesh { name = full.name + "_cut" };
            if (Pos.Count > 65000) cut.indexFormat = IndexFormat.UInt32;
            cut.SetVertices(Pos);
            if (hasNrm) cut.SetNormals(Nrm);
            if (hasUv) cut.SetUVs(0, uv);
            if (hasBw) cut.boneWeights = bw.ToArray();
            cut.bindposes = full.bindposes;
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
