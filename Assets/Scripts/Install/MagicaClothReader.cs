// 의상·헤어 프리팹에 붙은 게임 천 물리(MagicaCloth 1세대 MagicaBoneCloth) 설정을 설치 폴더 번들에서 읽는다(Docs/ClothPhysics.md).
//   Unity 는 게임 스크립트가 없어 이 MonoBehaviour 값을 풀지 못하므로 UnityBundle 로 타입트리를 직접 읽는다. UnityEngine 에 의존하지 않는다.
//   읽는 것: 설정값(clothParams), 입자(본) 목록과 고정·이동·깊이·부모(미리 계산된 clothData), 충돌체(구·캡슐) 목록.
//   Unity 내장 공통 필드 이름은 UnityBundle 이 "#common:오프셋" 으로 둔다. 374 = m_GameObject, 427 = m_Name [확인: 설치본 프리팹].
using System;
using System.Collections.Generic;
using System.Text;

public sealed class MagicaBoneClothInfo
{
    /// <summary>컴포넌트가 붙은 게임 오브젝트 이름(로그용).</summary>
    public string Name;
    public bool Enabled;
    /// <summary>ClothParams.algorithm. 1 이면 회전 복원·각도 제한에 "2" 매개변수(restoreRotation2·clampRotationAngle2 등)를 쓴다 [확인: ClothParams.GetRestoreRotationPower 디스어셈블].</summary>
    public int Algorithm;
    /// <summary>입자마다: 본 이름, 프리팹 안 경로, 고정 여부, 깊이(0~1), 부모 입자 번호(-1 이면 없음).</summary>
    public readonly List<Particle> Particles = new List<Particle>();
    public readonly List<Collider> Colliders = new List<Collider>();
    /// <summary>
    /// 입자 사이 거리 연결(structDistanceDataList, 양방향 중복 제거). 연결 방식이 메시(clothTarget.connection ≠ 0)면 사슬 사이 가로·대각선 연결이 들어 있다.
    /// </summary>
    public readonly List<(int a, int b)> Links = new List<(int, int)>();
    /// <summary>삼각형 굽힘(triangleBendDataList): 공유 모서리 맞은편 두 입자(vindex0·vindex1). 앱은 두 입자의 거리 유지로 근사한다.</summary>
    public readonly List<(int a, int b)> Bends = new List<(int, int)>();
    /// <summary>
    /// 콜라이더 관통 방지(penetrationDataList, 모드 1): 입자, 묶인 충돌체(colliderList 순번), 충돌체 오브젝트 국소 공간의 기준점·방향, 기준 거리.
    /// </summary>
    public readonly List<Penetration> Penetrations = new List<Penetration>();

    public sealed class Penetration
    {
        public int Particle, Collider;
        public float PosX, PosY, PosZ, DirX, DirY, DirZ, Distance;
    }
    /// <summary>깊이에 따라 변하는 값(BezierParam). 이름 → 값.</summary>
    public readonly Dictionary<string, Curve> Curves = new Dictionary<string, Curve>(StringComparer.Ordinal);
    /// <summary>단일 값(실수·정수·참거짓). 이름 → 값.</summary>
    public readonly Dictionary<string, float> Values = new Dictionary<string, float>(StringComparer.Ordinal);
    public float GravityX, GravityY = 1f, GravityZ;

    public sealed class Particle
    {
        public string Bone, Path;
        public bool Fixed;
        public float Depth;
        public int Parent = -1;
    }

    public sealed class Collider
    {
        /// <summary>teamData.colliderList 안 순번(관통 방지 데이터가 이 번호로 가리킨다).</summary>
        public int Index;
        /// <summary>충돌체 게임 오브젝트의 프리팹 안 경로(루트 이름 제외)와 부모 본 이름.</summary>
        public string Path, ParentBone;
        public bool Capsule;
        public float CenterX, CenterY, CenterZ;
        /// <summary>캡슐 축(0 X, 1 Y, 2 Z), 중심에서 끝까지 길이, 양 끝 반지름. 구는 Radius.</summary>
        public int Axis;
        public float Length, StartRadius, EndRadius, Radius;
    }

    /// <summary>
    /// 게임 BezierParam.Evaluate [확인: 디스어셈블]. x 는 0~1 로 자른다.
    ///   끝값을 쓰지 않으면 끝값 = 시작값. 곡선을 쓰지 않으면(또는 곡선값 0) 선형 보간,
    ///   쓰면 제어점 = 끝값 + (시작값 − 끝값) × (0.5 + 0.5 × 곡선값)인 2차 베지어.
    /// </summary>
    public struct Curve
    {
        public float Start, End, Shape;
        public bool UseEnd, UseCurve;

        public float Evaluate(float x)
        {
            x = x < 0f ? 0f : x > 1f ? 1f : x;
            float end = UseEnd ? End : Start;
            float c = UseCurve && UseEnd ? Shape : 0f;
            if (c == 0f) return Start + (end - Start) * x;
            float p1 = end + (Start - end) * (c * 0.5f + 0.5f);
            float u = 1f - x;
            return u * u * Start + 2f * u * x * p1 + x * x * end;
        }
    }

    public Curve Get(string name) => Curves.TryGetValue(name, out var c) ? c : default;
    public float Value(string name, float fallback = 0f) => Values.TryGetValue(name, out var v) ? v : fallback;
    public bool On(string name) => Value(name) != 0f;
}

public static class MagicaClothReader
{
    const string GameObjectField = "#common:374", NameField = "#common:427", EnabledField = "#common:349";

    static readonly string[] CurveNames =
    {
        "radius", "mass", "gravity", "drag", "maxVelocity", "worldMoveInfluence", "worldRotationInfluence", "depthInfluence",
        "clampRotationAngle", "clampRotationAngle2", "restoreRotation", "restoreRotation2", "structDistanceStiffness",
        "clampPositionLength", "triangleBend", "triangleBend2", "penetrationDistance", "penetrationRadius",
    };
    static readonly string[] ValueNames =
    {
        "algorithm", "useGravity", "useDrag", "useMaxVelocity", "maxMoveSpeed", "massInfluence",
        "useClampDistanceRatio", "clampDistanceMinRatio", "clampDistanceMaxRatio", "clampDistanceVelocityInfluence",
        "useClampPositionLength", "useClampRotation", "clampRotationVelocityInfluence", "clampRotationVelocityLimit",
        "useRestoreRotation", "restoreRotationVelocityInfluence", "restoreRotationVelocityInfluence2",
        "useCollision", "friction", "useFixedNonRotation", "useLineAvarageRotation",
        "useTriangleBend", "restoreDistanceVelocityInfluence", "usePenetration", "penetrationMode", "penetrationMaxDepth",
    };

    // 설치 폴더·프리팹 경로별 결과. 같은 의상을 다시 조립할 때 번들을 다시 풀지 않는다.
    static InstallFiles cacheOf;
    static readonly Dictionary<string, List<MagicaBoneClothInfo>> cache = new Dictionary<string, List<MagicaBoneClothInfo>>(StringComparer.Ordinal);

    /// <summary>프리팹(Assets/PatchableAssets/… 전체 경로)의 MagicaBoneCloth 목록. 없으면 빈 목록. 형식이 다르면 예외.</summary>
    public static List<MagicaBoneClothInfo> FromPrefab(InstallFiles files, string prefabAssetPath)
    {
        if (cacheOf != files) { cacheOf = files; cache.Clear(); }
        if (cache.TryGetValue(prefabAssetPath, out var hit)) return hit;
        var result = new List<MagicaBoneClothInfo>();
        cache[prefabAssetPath] = result;
        if (!files.Assets.PathToLoc.TryGetValue(prefabAssetPath, out uint loc)) return result;
        var bytes = files.Blobs.Read(files.Assets.BlobPath(loc), PackageIndex.Entry(loc), out _);
        var bundle = UnityBundle.Read(bytes);
        // 한 번들에 여러 프리팹이 들어 있을 수 있다(예: 헤어 번들에 타이틀 화면 캐릭터). 이 프리팹 루트 아래 것만 쓴다.
        string rootName = prefabAssetPath.Substring(prefabAssetPath.LastIndexOf('/') + 1);
        if (rootName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) rootName = rootName.Substring(0, rootName.Length - 7);
        foreach (var kv in bundle.Files)
        {
            if (kv.Key.EndsWith(".resS", StringComparison.OrdinalIgnoreCase) || kv.Key.EndsWith(".resource", StringComparison.OrdinalIgnoreCase)) continue;
            var f = new UnitySerializedFile(kv.Value);
            foreach (long id in f.ObjectsWithField("clothParams"))
            {
                var c = f.Read(id);
                if (RootName(f, GoTransform(f, Pid(Field(c, GameObjectField)))) != rootName) continue;
                var info = ReadCloth(f, c);
                if (info != null) result.Add(info);
            }
            break;
        }
        return result;
    }

    static MagicaBoneClothInfo ReadCloth(UnitySerializedFile f, Dictionary<string, object> c)
    {
        var target = Field(c, "clothTarget");
        if (target == null) return null;                    // MagicaMeshCloth 등 본 대상이 아닌 것
        var data = f.Read(Pid(Field(c, "clothData")));
        if (data == null) return null;
        var info = new MagicaBoneClothInfo { Name = GoName(f, Pid(Field(c, GameObjectField))), Enabled = true };
        // m_Enabled(#common:349, 1바이트) [확인: 설치본 프리팹]
        if (c.TryGetValue(EnabledField, out var en) && en is byte[] eb && eb.Length == 1) info.Enabled = eb[0] != 0;

        var p = Field(c, "clothParams");
        foreach (var n in CurveNames)
        {
            var b = Field(p, n);
            if (b == null) continue;
            info.Curves[n] = new MagicaBoneClothInfo.Curve
            {
                Start = F(Field(b, "startValue")), End = F(Field(b, "endValue")), Shape = F(Field(b, "curveValue")),
                UseEnd = I(Field(b, "useEndValue")) != 0, UseCurve = I(Field(b, "useCurveValue")) != 0,
            };
        }
        foreach (var n in ValueNames)
        {
            var v = Field(p, n);
            if (!(v is byte[] bytes)) continue;
            info.Values[n] = bytes.Length == 1 ? bytes[0] : n == "algorithm" || n == "penetrationMode" ? BitConverter.ToInt32(bytes, 0) : BitConverter.ToSingle(bytes, 0);
        }
        info.Algorithm = (int)info.Value("algorithm");
        var gd = Field(p, "gravityDirection");
        if (gd != null) { info.GravityX = F(Field(gd, "x")); info.GravityY = F(Field(gd, "y")); info.GravityZ = F(Field(gd, "z")); }

        // 입자 k = 정점 useVertexList[k], 정점 v = 본 useTransformList[v] [추정: 설치본 헤어는 모두 순서가 같다].
        var transforms = Field(c, "useTransformList") as List<object>;
        var useVertex = Field(data, "useVertexList") as List<object>;
        var select = Field(data, "selectionData") as List<object>;
        var depth = Field(data, "vertexDepthList") as List<object>;
        var parent = Field(data, "parentList") as List<object>;
        if (transforms == null || useVertex == null || select == null) return null;
        for (int k = 0; k < useVertex.Count; k++)
        {
            int v = I(useVertex[k]);
            if (v < 0 || v >= transforms.Count) return null;
            long tr = Pid(transforms[v]);
            // 선택 값: 0 무효, 1 이동, 2 고정, 3 확장(고정으로 취급) [추정: MagicaCloth SelectionData 상수]
            int s = k < select.Count ? I(select[k]) : 1;
            info.Particles.Add(new MagicaBoneClothInfo.Particle
            {
                Bone = TrName(f, tr), Path = TrPath(f, tr), Fixed = s != 1,
                Depth = depth != null && k < depth.Count ? F(depth[k]) : 0f,
                Parent = parent != null && k < parent.Count ? I(parent[k]) : -1,
            });
        }

        int pc = info.Particles.Count;
        var seen = new HashSet<long>();
        if (Field(data, "structDistanceDataList") is List<object> links)
            foreach (var l in links)
            {
                int a = U16(Field(l, "vertexIndex")), b = U16(Field(l, "targetVertexIndex"));
                if (a == b || a < 0 || b < 0 || a >= pc || b >= pc) continue;
                if (seen.Add((long)Math.Min(a, b) << 32 | (uint)Math.Max(a, b))) info.Links.Add((a, b));
            }
        if (Field(data, "triangleBendDataList") is List<object> bends)
            foreach (var t in bends)
            {
                int a = I(Field(t, "vindex0")), b = I(Field(t, "vindex1"));
                if (a != b && a >= 0 && b >= 0 && a < pc && b < pc) info.Bends.Add((a, b));
            }

        if (Field(data, "penetrationDataList") is List<object> pens)
            foreach (var e in pens)
            {
                int v = U16(Field(e, "vertexIndex")), ci = U16(Field(e, "colliderIndex"));
                if (v < 0 || v >= pc || ci < 0) continue;
                var lp = Field(e, "localPos"); var ld = Field(e, "localDir");
                info.Penetrations.Add(new MagicaBoneClothInfo.Penetration
                {
                    Particle = v, Collider = ci, Distance = F(Field(e, "distance")),
                    PosX = F(Field(lp, "x")), PosY = F(Field(lp, "y")), PosZ = F(Field(lp, "z")),
                    DirX = F(Field(ld, "x")), DirY = F(Field(ld, "y")), DirZ = F(Field(ld, "z")),
                });
            }

        if (Field(Field(c, "teamData"), "colliderList") is List<object> cols)
            for (int ci = 0; ci < cols.Count; ci++)
            {
                var cp = cols[ci];
                var m = f.Read(Pid(cp));
                if (m == null) continue;
                long go = Pid(Field(m, GameObjectField));
                long tr = GoTransform(f, go);
                var col = new MagicaBoneClothInfo.Collider { Index = ci, Path = TrPath(f, tr), ParentBone = TrName(f, Pid(Field(f.Read(tr), "m_Father"))) };
                var ce = Field(m, "center");
                col.CenterX = F(Field(ce, "x")); col.CenterY = F(Field(ce, "y")); col.CenterZ = F(Field(ce, "z"));
                if (m.ContainsKey("startRadius"))
                {
                    col.Capsule = true;
                    col.Axis = I(Field(m, "axis"));
                    col.Length = F(Field(m, "length"));
                    col.StartRadius = F(Field(m, "startRadius"));
                    col.EndRadius = F(Field(m, "endRadius"));
                }
                else if (m.ContainsKey("radius")) col.Radius = F(Field(m, "radius"));
                else continue;                              // 평면 등은 쓰지 않는다
                info.Colliders.Add(col);
            }
        return info;
    }

    static object Field(object o, string n) => UnitySerializedFile.Field(o, n);
    static float F(object o) => UnitySerializedFile.F(o);
    /// <summary>2바이트(ushort) 값. 4바이트면 int 로 읽는다.</summary>
    static int U16(object o) => o is byte[] b ? (b.Length == 2 ? b[0] | b[1] << 8 : b.Length >= 4 ? BitConverter.ToInt32(b, 0) : b.Length == 1 ? b[0] : -1) : -1;
    static int I(object o) => UnitySerializedFile.I(o);
    static long Pid(object ptr) => UnitySerializedFile.L(Field(ptr, "m_PathID"));

    static string GoName(UnitySerializedFile f, long go)
    {
        var g = f.Read(go);
        return g != null && g.TryGetValue(NameField, out var n) && n is byte[] b ? Encoding.UTF8.GetString(b) : null;
    }

    /// <summary>게임 오브젝트의 Transform(첫 구성 요소).</summary>
    static long GoTransform(UnitySerializedFile f, long go)
    {
        var g = f.Read(go);
        if (g == null) return 0;
        foreach (var kv in g)
            if (kv.Value is List<object> comps && comps.Count > 0)
                return Pid(Field(comps[0], "component"));
        return 0;
    }

    static string TrName(UnitySerializedFile f, long tr)
    {
        var t = f.Read(tr);
        return t == null ? null : GoName(f, Pid(Field(t, GameObjectField)));
    }

    static string RootName(UnitySerializedFile f, long tr)
    {
        string name = null;
        for (int guard = 0; tr != 0 && guard < 256; guard++)
        {
            var t = f.Read(tr);
            if (t == null) break;
            name = GoName(f, Pid(Field(t, GameObjectField)));
            tr = Pid(Field(t, "m_Father"));
        }
        return name;
    }

    /// <summary>프리팹 루트 아래 경로("_COM/_Pelvis/…"). 루트 이름은 넣지 않는다.</summary>
    static string TrPath(UnitySerializedFile f, long tr)
    {
        var names = new List<string>();
        for (int guard = 0; tr != 0 && guard < 256; guard++)
        {
            var t = f.Read(tr);
            if (t == null) break;
            names.Insert(0, GoName(f, Pid(Field(t, GameObjectField))));
            tr = Pid(Field(t, "m_Father"));
        }
        if (names.Count > 0) names.RemoveAt(0);
        return string.Join("/", names);
    }
}
