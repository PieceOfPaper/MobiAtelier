// 조립 계획(AssemblyPlan)대로 설치 폴더에서 모델을 읽어 캐릭터를 만든다.
//   리소스(ResourcesPackage): StageObjectModel·BoneTemplate·포즈(SJON), 몸·의상 메시(SMM)
//   번들(AssetPackages)    : 프리팹(머티리얼·텍스처), 머리·헤어·눈 메시(프리팹 또는 FBX 번들의 SkinnedMeshRenderer)
// 규칙
//   본: 파츠 순서(몸 → 머리 → 헤어 → 눈 → 의상)대로 BoneTemplate 을 이름 기준 병합
//   메시: ① <모델>_<i>.SkinnedModularMesh.bytes ② 프리팹 SMR 의 sharedMesh ③ /<모델>.fbx 번들의 같은 이름 SMR
//   보조 메시(Mesh_BP_, Mesh_SP_, Cage_)는 만들지 않는다. 헤어는 Mesh_Hair_*_H0~H5 중 하나만 표시
//   머티리얼: 원본 복사 후 셰이더만 교체(_BaseMap, _DyeColor1~3, _IsDyable, _TintColor 유지)
//   염색: DyeInfo.Slots[].Wirings[(MeshIndex, MaterialIndex, ChannelIndex)] → 해당 머티리얼 _DyeColor{Channel+1}
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class CharacterAssembler
{
    static readonly string[] HelperPrefix = { "Mesh_BP_", "Mesh_SP_", "Cage_" };
    static readonly Regex HelmetLevel = new Regex(@"^Mesh_Helmet_\d+_C(\d)$");
    static readonly Regex HairLevel = new Regex(@"^Mesh_Hair_.*_H(\d)$");
    const float BodyCutDistance = 0.08f;

    sealed class Def
    {
        public PartPlan Part;
        public string Path, Dir;
        public object Root;
    }

    public static CharacterView Build(InstallFiles files, BundleCache bundles, AssemblyPlan plan, Shader shader, StringBuilder log = null)
    {
        var sw = Stopwatch.StartNew();
        void L(string s) { log?.AppendLine(s); }

        // 1. 모델 정의 읽기와 숨김 규칙 수집
        var defs = new List<Def>();
        foreach (var p in plan.Parts)
        {
            string path = files.ModelDefPath(p.Model);
            if (path == null) { L($"모델 정의 없음: {p.Model}"); continue; }
            defs.Add(new Def { Part = p, Path = path, Dir = path.Substring(0, path.LastIndexOf('/') + 1), Root = files.ReadSjon(path) });
        }
        bool hideHair = false, hideHead = false, hideEye = false;
        int hairLevel = 0;
        foreach (var d in defs)
        {
            if (d.Part.Role != PartRole.Costume) continue;
            var info = d.Root.Get("ModelPartInfo");
            hideHair |= info.Get("HideHair").B();
            hideHead |= info.Get("HideHead").B();
            hideEye |= info.Get("HideEye").B();
            foreach (var m in d.Root.Get("SkinnedMeshList").Arr())
            {
                var match = HelmetLevel.Match(m.Get("MeshName").Str() ?? "");
                if (match.Success) hairLevel = Math.Max(hairLevel, match.Groups[1].Value[0] - '0');
            }
        }

        var root = new GameObject("Character");
        var view = root.AddComponent<CharacterView>();
        view.Plan = plan;
        var bones = new List<Transform>();
        var bind = new List<(Vector3 p, Quaternion r, Vector3 s)>();
        var map = new Dictionary<string, Transform>(StringComparer.Ordinal);
        var parentName = new Dictionary<Transform, string>();
        var cloth = new List<SkinnedMeshRenderer>();

        void AddBones(Def d)
        {
            string tp = d.Path.Substring(0, d.Path.Length - ".StageObjectModel.sjon.bin".Length) + ".BoneTemplate.sjon.bin";
            if (!files.HasResource(tp)) return;
            var nodes = files.ReadSjon(tp).Get("Nodes").Arr();
            foreach (var n in nodes)
            {
                string name = n.Get("Name").Str();
                if (name == null || map.ContainsKey(name)) continue;
                int pi = n.Get("ParentIndex").I();
                var t = new GameObject(name).transform;
                map[name] = t;
                parentName[t] = pi >= 0 && pi < nodes.Count ? nodes[pi].Get("Name").Str() : null;
                bones.Add(t);
                bind.Add((V3(n.Get("Position")), Q(n.Get("Rotation")), V3(n.Get("Scale"))));
            }
        }

        Material Convert(Material m, bool keepOriginal, out bool shadow)
        {
            shadow = false;
            if (!m) { var e = new Material(shader); view.Owned.Add(e); return e; }
            var c = new Material(m) { name = m.name };
            view.Owned.Add(c);
            if (keepOriginal) return c;
            string orig = m.shader ? m.shader.name : "";
            float cull = m.HasProperty("_CullType") ? m.GetFloat("_CullType") : 2f;
            // 눈 재질(Eye_Base·Eye_Pupil 등)은 큐 2440 대와 _ALPHAPREMULTIPLY_ON 으로 반투명을 표시한다.
            bool transparent = m.renderQueue >= 2400 || m.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") ||
                               orig.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0;
            c.shader = shader;
            c.renderQueue = -1;
            c.SetFloat("_Cull", cull);
            c.SetFloat("_Alpha", 1); c.SetFloat("_UseTexAlpha", 0);
            c.SetFloat("_SrcBlend", 1); c.SetFloat("_DstBlend", 0); c.SetFloat("_ZWrite", 1);
            c.SetFloat("_AlbedoOnly", 0);
            c.SetColor("_ShadeColor", new Color(0.62f, 0.55f, 0.6f, 1));
            if (!m.HasProperty("_IsDyable")) c.SetFloat("_IsDyable", 0);
            if (orig.Contains("Hair_Shadow"))
            {
                shadow = true;
                c.SetTexture("_BaseMap", Texture2D.whiteTexture);
                c.SetFloat("_IsDyable", 0);
                c.SetColor("_TintColor", Color.black);
                c.SetFloat("_Alpha", 0.25f);
                transparent = true;
            }
            else if (transparent) c.SetFloat("_UseTexAlpha", 1);
            if (transparent)
            {
                c.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                c.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                c.SetFloat("_ZWrite", 0);
                c.renderQueue = 3000;
            }
            return c;
        }

        Material[] MorphMaterials(Def d)
        {
            if (plan.BodyType == "Default") return null;
            foreach (var mm in d.Root.Get("MorphableAssets").Get("MorphguideMeshMaterials").Arr())
            {
                var bt = mm.Get("BodyType").Arr();
                if (bt.Count < 2 || bt[1].Str() != plan.BodyType) continue;
                var mat = bundles.LoadAsset<Material>(BundleCache.AssetRoot + mm.Get("AssetPath").Str());
                return mat ? new[] { mat } : null;
            }
            return null;
        }

        void AddModel(Def d)
        {
            var part = d.Part;
            var partGo = new GameObject(part.Key + ":" + part.Model);
            partGo.transform.SetParent(root.transform, false);
            if ((part.Role == PartRole.Hair && hideHair) || (part.Role == PartRole.Head && hideHead) || (part.Role == PartRole.Eyes && hideEye))
                partGo.SetActive(false);

            string prefabPath = BundleCache.AssetRoot + d.Root.Get("AssetPathToLoad").Str();
            var prefab = bundles.LoadAsset<GameObject>(prefabPath);
            if (!prefab) L($"  프리팹 없음: {prefabPath}");
            var prefabSmr = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
            if (prefab) foreach (var r in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (!prefabSmr.ContainsKey(r.name)) prefabSmr[r.name] = r;
            Dictionary<string, SkinnedMeshRenderer> fbxSmr = null;
            var morph = part.Role == PartRole.Body ? MorphMaterials(d) : null;

            var list = d.Root.Get("SkinnedMeshList").Arr();
            var byIndex = new Dictionary<int, Material[]>();
            for (int i = 0; i < list.Count; i++)
            {
                string name = list[i].Get("MeshName").Str() ?? "";
                bool helper = false;
                foreach (var h in HelperPrefix) if (name.StartsWith(h, StringComparison.Ordinal)) helper = true;
                if (helper) continue;
                Mesh mesh; string[] boneNames; bool owned = false;
                string smm = $"{d.Dir}{part.Model}_{i}.SkinnedModularMesh.bytes";
                prefabSmr.TryGetValue(name, out var psmr);
                if (files.HasResource(smm)) { mesh = SmmReader.Read(files.ReadResource(smm), out boneNames); owned = true; }
                else if (psmr && psmr.sharedMesh) { mesh = psmr.sharedMesh; boneNames = BoneNames(psmr); }
                else
                {
                    if (fbxSmr == null)
                    {
                        fbxSmr = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
                        string fbx = files.FindAssetPathEndingWith("/" + part.Model + ".fbx");
                        var fb = fbx != null ? bundles.BundleOf(fbx) : null;
                        if (fb)
                            foreach (var go in fb.LoadAllAssets<GameObject>())
                                foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) fbxSmr[r.name] = r;
                    }
                    if (!fbxSmr.TryGetValue(name, out var fsmr) || !fsmr.sharedMesh) { L($"  메시 없음: {part.Model}/{name}"); continue; }
                    mesh = fsmr.sharedMesh; boneNames = BoneNames(fsmr);
                }
                if (owned) view.Owned.Add(mesh);

                var go2 = new GameObject(name);
                go2.transform.SetParent(partGo.transform, false);
                var smr = go2.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                int missing = 0;
                var bt = new Transform[boneNames.Length];
                for (int b = 0; b < bt.Length; b++)
                    if (!map.TryGetValue(boneNames[b] ?? "", out bt[b])) { bt[b] = root.transform; missing++; }
                smr.bones = bt;
                smr.rootBone = bt.Length > 0 ? bt[0] : root.transform;
                smr.updateWhenOffscreen = true;

                var srcMats = morph ?? (psmr ? psmr.sharedMaterials : Array.Empty<Material>());
                var subMats = new Material[mesh.subMeshCount];
                for (int s = 0; s < subMats.Length; s++)
                {
                    var src = srcMats.Length == 0 ? null : srcMats[Mathf.Min(s, srcMats.Length - 1)];
                    if (DebugMaterials && src) L($"  재질 {part.Model}/{name}[{s}]: {src.name} 셰이더 {(src.shader ? src.shader.name : "-")} 큐 {src.renderQueue} 텍스처 {DescribeTextures(src)}");
                    subMats[s] = Convert(src, part.Role == PartRole.Eyes && KeepOriginalEyeShader, out _);
                }
                smr.sharedMaterials = subMats;

                var lv = HairLevel.Match(name);
                if (part.Role == PartRole.Hair && lv.Success && lv.Groups[1].Value[0] - '0' != hairLevel) go2.SetActive(false);
                view.Renderers.Add((part.Key, smr));
                byIndex[i] = subMats;
                if (part.Role == PartRole.Body) { view.Body = smr; view.BodyFullMesh = mesh; }
                if (part.Role == PartRole.Costume && go2.activeSelf && list[i].Get("ClothingMaterialType").Str() != "Skin") cloth.Add(smr);
                if (missing > 0) L($"  {part.Model}/{name}: 본 {missing}개 없음");
            }

            foreach (var slot in d.Root.Get("DyeInfo").Get("Slots").Arr())
            {
                string sname = slot.Get("Slot").Str();
                foreach (var w in slot.Get("Wirings").Arr())
                {
                    int mi = w.Get("MeshIndex").I(), ma = w.Get("MaterialIndex").I(), ch = w.Get("ChannelIndex").I();
                    if (!byIndex.TryGetValue(mi, out var mats) || ma >= mats.Length || ch < 0 || ch > 2) continue;
                    var m = mats[ma]; string prop = "_DyeColor" + (ch + 1);
                    view.Dyes.Add(new DyeBinding { PartKey = part.Key, Slot = sname, Material = m, Property = prop, Original = m.GetColor(prop) });
                }
            }
        }

        foreach (var d in defs) AddBones(d);
        foreach (var d in defs) AddModel(d);
        foreach (var t in bones)
            t.SetParent(parentName[t] != null && map.TryGetValue(parentName[t], out var pt) ? pt : root.transform, false);

        int nb = bones.Count;
        view.Bones = bones.ToArray();
        view.BindPos = new Vector3[nb]; view.BindRot = new Quaternion[nb]; view.BindScl = new Vector3[nb];
        for (int i = 0; i < nb; i++) { view.BindPos[i] = bind[i].p; view.BindRot[i] = bind[i].r; view.BindScl[i] = bind[i].s; }
        view.PosePos = new Vector3[nb]; view.PoseRot = new Quaternion[nb]; view.PoseScl = new Vector3[nb]; view.HasPose = new bool[nb];
        int posed = 0;
        string posePath = PlanResolver.PosePath(plan.Pose ?? "");
        if (files.HasResource(posePath) && files.ReadSjon(posePath).Get("Transforms") is Dictionary<string, object> tr)
        {
            for (int i = 0; i < nb; i++)
            {
                if (!tr.TryGetValue(bones[i].name, out var t)) continue;
                view.HasPose[i] = true; posed++;
                view.PosePos[i] = V3(t.Get("LocalPosition")); view.PoseRot[i] = Q(t.Get("LocalRotation")); view.PoseScl[i] = V3(t.Get("LocalScale"));
            }
        }
        else L($"포즈 없음: {plan.Pose}");

        view.ApplyBlendShape(plan.BlendShape);
        view.ApplyPlanColors(plan);
        // 몸 가리기는 기본 자세에서 계산한다.
        view.SetPosed(false);
        if (view.Body && cloth.Count > 0)
        {
            view.BodyCutMesh = BodyCut.Compute(view.Body, cloth, BodyCutDistance, L);
            if (view.BodyCutMesh) view.Owned.Add(view.BodyCutMesh);
        }
        view.SetBodyCut(true);
        view.SetPosed(true);
        L($"조립: 파츠 {defs.Count}, 본 {nb}(포즈 {posed}), 렌더러 {view.Renderers.Count}, 염색 연결 {view.Dyes.Count}, 헤어 단계 H{hairLevel}, 번들 {bundles.Count}, {sw.ElapsedMilliseconds}ms");
        return view;
    }

    /// <summary>진단용: 원본 머티리얼 정보를 로그에 남긴다.</summary>
    public static bool DebugMaterials;

    /// <summary>눈 파츠는 게임 원본 셰이더를 그대로 쓴다. 대체 셰이더로는 눈 재질의 염색 맵 해석이 맞지 않는다(흰 사각형).</summary>
    public static bool KeepOriginalEyeShader = true;

    static string DescribeTextures(Material m)
    {
        var sb = new StringBuilder();
        foreach (var n in m.GetTexturePropertyNames())
        {
            var t = m.GetTexture(n);
            if (t) sb.Append($"{n}={t.name}({(t is Texture2D t2 ? t2.format.ToString() : "?")}) ");
        }
        foreach (var p in new[] { "_IsDyable", "_Cutoff", "_AlphaClip", "_Surface", "_CullType", "_Blend", "_SrcBlend", "_DstBlend", "_ZWrite" })
            if (m.HasProperty(p)) sb.Append($"{p}={m.GetFloat(p)} ");
        sb.Append("kw[" + string.Join(",", m.shaderKeywords) + "]");
        return sb.ToString();
    }

    static string[] BoneNames(SkinnedMeshRenderer r)
    {
        var b = r.bones;
        var n = new string[b.Length];
        for (int i = 0; i < b.Length; i++) n[i] = b[i] ? b[i].name : "";
        return n;
    }

    static Vector3 V3(object o) { var l = o.Arr(); return l.Count >= 3 ? new Vector3(l[0].F(), l[1].F(), l[2].F()) : Vector3.one; }
    static Quaternion Q(object o) { var l = o.Arr(); return l.Count >= 4 ? new Quaternion(l[0].F(), l[1].F(), l[2].F(), l[3].F()) : Quaternion.identity; }
}

public static class BodyCut
{
    /// <summary>몸 정점의 노멀 방향 +dist / -3cm 안에 옷 면이 있으면 덮인 것으로 본다. 세 정점이 모두 덮인 삼각형을 뺀 메시를 만든다(근사).</summary>
    public static Mesh Compute(SkinnedMeshRenderer body, List<SkinnedMeshRenderer> cloth, float dist, Action<string> log)
    {
        var bm = new Mesh(); body.BakeMesh(bm, true);
        var bv = bm.vertices; var bn = bm.normals; var b2w = body.transform.localToWorldMatrix;
        UnityEngine.Object.DestroyImmediate(bm);
        var tris = new List<Vector3>();
        foreach (var c in cloth)
        {
            var cm = new Mesh(); c.BakeMesh(cm, true);
            var cv = cm.vertices; var c2w = c.transform.localToWorldMatrix;
            for (int s = 0; s < cm.subMeshCount; s++) foreach (var i in cm.GetTriangles(s)) tris.Add(c2w.MultiplyPoint3x4(cv[i]));
            UnityEngine.Object.DestroyImmediate(cm);
        }
        // 옷 삼각형 바운드로 먼저 걸러 검사량을 줄인다.
        int nt = tris.Count / 3;
        var tmin = new Vector3[nt]; var tmax = new Vector3[nt];
        for (int k = 0; k < nt; k++)
        {
            Vector3 a = tris[3 * k], b = tris[3 * k + 1], c = tris[3 * k + 2];
            tmin[k] = Vector3.Min(a, Vector3.Min(b, c)); tmax[k] = Vector3.Max(a, Vector3.Max(b, c));
        }
        var covered = new bool[bv.Length];
        int nc = 0;
        float reach = dist + 0.01f;
        for (int i = 0; i < bv.Length; i++)
        {
            var p = b2w.MultiplyPoint3x4(bv[i]); var n = b2w.MultiplyVector(bn[i]).normalized;
            for (int k = 0; k < nt; k++)
            {
                if (p.x < tmin[k].x - reach || p.x > tmax[k].x + reach || p.y < tmin[k].y - reach || p.y > tmax[k].y + reach ||
                    p.z < tmin[k].z - reach || p.z > tmax[k].z + reach) continue;
                Vector3 a = tris[3 * k], b = tris[3 * k + 1], c = tris[3 * k + 2];
                if (Ray(p - n * 0.005f, n, a, b, c, dist + 0.005f) || Ray(p + n * 0.005f, -n, a, b, c, 0.03f)) { covered[i] = true; nc++; break; }
            }
        }
        var full = body.sharedMesh;
        var cut = UnityEngine.Object.Instantiate(full);
        cut.name = full.name + "_cut";
        int removed = 0, total = 0;
        for (int s = 0; s < full.subMeshCount; s++)
        {
            var t = full.GetTriangles(s); var keep = new List<int>(t.Length);
            for (int i = 0; i < t.Length; i += 3)
            {
                total++;
                if (covered[t[i]] && covered[t[i + 1]] && covered[t[i + 2]]) { removed++; continue; }
                keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]);
            }
            cut.SetTriangles(keep, s);
        }
        log?.Invoke($"몸 가리기 근사: 옷 삼각형 {nt}, 덮인 몸 정점 {nc}/{bv.Length}, 뺀 삼각형 {removed}/{total}");
        return cut;
    }

    static bool Ray(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, float maxT)
    {
        Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, p);
        if (Mathf.Abs(det) < 1e-10f) return false;
        float inv = 1f / det; Vector3 s = o - a;
        float u = Vector3.Dot(s, p) * inv; if (u < 0 || u > 1) return false;
        Vector3 q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(d, q) * inv; if (v < 0 || u + v > 1) return false;
        float t = Vector3.Dot(e2, q) * inv;
        return t > 0 && t < maxT;
    }
}
