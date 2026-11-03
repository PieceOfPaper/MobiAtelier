// 조립 계획(AssemblyPlan)대로 설치 폴더에서 모델을 읽어 캐릭터를 만든다.
//   리소스(ResourcesPackage): StageObjectModel·BoneTemplate·포즈(SJON), 몸·의상 메시(SMM)
//   번들(AssetPackages)    : 프리팹(머티리얼·텍스처), 머리·헤어·눈 메시(프리팹 또는 FBX 번들의 SkinnedMeshRenderer)
// 규칙
//   본: 파츠 순서(몸 → 머리 → 헤어 → 눈 → 의상)대로 BoneTemplate 을 이름 기준 병합.
//       같은 이름인데 기본 회전이 다른 본(예: 장갑의 Forearm Twist 가 몸과 약 60° 다름)은 그 파츠 전용 본을 따로 두고,
//       포즈는 기존 본의 기본 자세 대비 변화량만 더한다. 하나로 합치면 그 파츠 정점이 비틀려 꺾인다.
//   메시: ① <모델>_<i>.SkinnedModularMesh.bytes ② 프리팹 SMR 의 sharedMesh ③ /<모델>.fbx 번들의 같은 이름 SMR
//   보조 메시(Mesh_BP_, Mesh_SP_, Cage_)는 그리지 않는다. 헤어는 Mesh_Hair_*_H0~H5 중 하나만 표시
//   몸 가리기: 의상의 팔·다리 절단면(Mesh_SP_L/R Hand·Arm·Foot·Leg)으로 그 팔다리의 덮인 쪽 몸을 자르고(BodyCut.LimbCuts),
//             나머지는 옷·의상 피부 조각 면 근처의 몸 삼각형을 빼는 근사로 처리한다.
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

    /// <summary>
    /// 실제 읽기 실패까지 반영한 조립. 실패한 의상은 숨김·절단 효과와 함께 제외하고,
    /// 필수 외형은 실패 모델을 피한 공개 기본값으로 다시 준비한다. 원래 선택은 바꾸지 않는다.
    /// </summary>
    public static CharacterView BuildValidated(InstallFiles files, BundleCache bundles, EditState state, GameData data,
        Shader shader, out AssemblyPlan plan, AssemblyPlan previous = null, StringBuilder log = null)
    {
        var failed = new HashSet<string>(StringComparer.Ordinal);
        var failures = new List<PlanIssue>();
        while (true)
        {
            plan = PlanResolver.Resolve(state, data, files, previous, failed);
            var view = Build(files, bundles, plan, shader, log);
            var errors = plan.Issues.FindAll(i => i.FromBuild && i.IsFailure);
            if (errors.Count == 0)
            {
                foreach (var issue in failures) if (!plan.Issues.Exists(i => i.Same(issue))) plan.Issues.Add(issue);
                return view;
            }
            Release(view);
            bool progress = false;
            foreach (var issue in errors)
            {
                if (issue.Key == "body") throw new PlanException(issue.Describe());
                progress |= failed.Add(issue.Id);
                if (!failures.Exists(i => i.Same(issue))) failures.Add(issue);
            }
            if (!progress) throw new PlanException("필수 외형을 정상적으로 조립하지 못했습니다.");
        }
    }

    public static void Release(CharacterView view)
    {
        if (!view) return;
        view.gameObject.SetActive(false);
        if (Application.isPlaying) UnityEngine.Object.Destroy(view.gameObject);
        else UnityEngine.Object.DestroyImmediate(view.gameObject);
    }

    public static CharacterView Build(InstallFiles files, BundleCache bundles, AssemblyPlan plan, Shader shader, StringBuilder log = null)
    {
        var root = new GameObject("Character");
        var view = root.AddComponent<CharacterView>();
        view.Plan = plan;
        try { return BuildCore(files, bundles, plan, shader, view, log); }
        catch { Release(view); throw; }
    }

    static CharacterView BuildCore(InstallFiles files, BundleCache bundles, AssemblyPlan plan, Shader shader, CharacterView view, StringBuilder log)
    {
        var root = view.gameObject;
        var sw = Stopwatch.StartNew();
        void L(string s) { log?.AppendLine(s); }
        // 리소스 읽기 실패는 로그만 남기지 않고 계획의 문제로 기록한다. 호출자가 복원 안내·편집 거절에 쓴다.
        void Fail(PartPlan p, string message) => plan.AddIssue(p.Key, IssueKind.MissingResource, p.Model, null, message, true);

        // 1. 모델 정의 읽기와 숨김 규칙 수집
        var defs = new List<Def>();
        foreach (var p in plan.Parts)
        {
            string path = files.ModelDefPath(p.Model);
            if (path == null) { L($"모델 정의 없음: {p.Model}"); Fail(p, "모델 정의를 설치 폴더에서 찾지 못했습니다"); continue; }
            try { defs.Add(new Def { Part = p, Path = path, Dir = path.Substring(0, path.LastIndexOf('/') + 1), Root = files.ReadSjon(path) }); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { Fail(p, "모델 정의 읽기 실패: " + e.Message); }
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
            // 치마 등 옷 모양 조건(Action·AnimationStateSet 의 Clothing:ShortSkirt 등). 앉기 같은 모션이 옷에 맞는 클립을 고른다.
            string clothing = info.Get("ClothingCondition").Str();
            if (!string.IsNullOrEmpty(clothing) && clothing != "None") plan.Clothing = clothing;
            foreach (var m in d.Root.Get("SkinnedMeshList").Arr())
            {
                var match = HelmetLevel.Match(m.Get("MeshName").Str() ?? "");
                if (match.Success) hairLevel = Math.Max(hairLevel, match.Groups[1].Value[0] - '0');
            }
        }

        var bones = new List<Transform>();
        var bind = new List<(Vector3 p, Quaternion r, Vector3 s)>();
        var map = new Dictionary<string, Transform>(StringComparer.Ordinal);
        var parentName = new Dictionary<Transform, string>();
        // 파츠 전용 본: 파츠 → (이름 → 본). 전용 본의 포즈는 (기존 본의 기본 자세 → 포즈) 변화량을 파츠 기본 자세에 더한다.
        var partBones = new Dictionary<Def, Dictionary<string, Transform>>();
        var boneOwner = new Dictionary<Transform, Def>();
        var poseBase = new Dictionary<Transform, (Vector3 p, Quaternion r)>();
        int partOwnBones = 0;
        var cloth = new List<SkinnedMeshRenderer>();
        var clothByKey = new List<(string key, SkinnedMeshRenderer smr)>();
        var slicers = new List<BodyCut.SlicerMesh>();
        // 무기 등 장착점(AttachPointInfo)이 있는 파츠: 프리팹 본을 복제한 루트. 몸 본 계층이 정해진 뒤 장착점 본 아래에 붙인다.
        var tools = new List<(Def d, Transform root)>();

        void AddBones(Def d)
        {
            string tp = d.Path.Substring(0, d.Path.Length - ".StageObjectModel.sjon.bin".Length) + ".BoneTemplate.sjon.bin";
            if (!files.HasResource(tp)) return;
            var nodes = files.ReadSjon(tp).Get("Nodes").Arr();
            foreach (var n in nodes)
            {
                string name = n.Get("Name").Str();
                if (name == null) continue;
                var pos = V3(n.Get("Position")); var rot = Q(n.Get("Rotation"));
                if (map.TryGetValue(name, out var existing))
                {
                    var eb = bind[bones.IndexOf(existing)];
                    if (Mathf.Abs(Quaternion.Dot(eb.r, rot)) >= 0.999f) continue;
                    if (!partBones.TryGetValue(d, out var own)) partBones[d] = own = new Dictionary<string, Transform>(StringComparer.Ordinal);
                    var dup = new GameObject(name).transform;
                    dup.SetParent(root.transform, false);
                    own[name] = dup;
                    poseBase[dup] = (eb.p, eb.r);
                    partOwnBones++;
                    boneOwner[dup] = d;
                    int dpi = n.Get("ParentIndex").I();
                    parentName[dup] = dpi >= 0 && dpi < nodes.Count ? nodes[dpi].Get("Name").Str() : null;
                    bones.Add(dup);
                    bind.Add((pos, rot, V3(n.Get("Scale"))));
                    continue;
                }
                int pi = n.Get("ParentIndex").I();
                var t = new GameObject(name).transform;
                t.SetParent(root.transform, false);
                map[name] = t;
                boneOwner[t] = d;
                parentName[t] = pi >= 0 && pi < nodes.Count ? nodes[pi].Get("Name").Str() : null;
                bones.Add(t);
                bind.Add((pos, rot, V3(n.Get("Scale"))));
            }
        }

        Material Convert(Material m, bool keepOriginal, out bool shadow) => ConvertMaterial(m, shader, view.Owned, keepOriginal, out shadow);

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
            if (!prefab) { L($"  프리팹 없음: {prefabPath}"); Fail(part, "프리팹(재질·텍스처)을 읽지 못했습니다: " + prefabPath); }
            var prefabSmr = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
            if (prefab) foreach (var r in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (!prefabSmr.ContainsKey(r.name)) prefabSmr[r.name] = r;
            // 무기는 BoneTemplate 이 없고 메시 본(_Sword_Root 등)이 프리팹 안에만 있다. 프리팹 본을 복제해 이 파츠 전용 본으로 쓴다.
            bool tool = part.Role == PartRole.Costume && d.Root.Get("ModelPartInfo").Get("AttachPointInfo").Arr().Count > 0;
            if (tool && prefab)
            {
                var toolRoot = new GameObject("@" + part.Model).transform;
                toolRoot.SetParent(root.transform, false);
                if (!partBones.TryGetValue(d, out var own)) partBones[d] = own = new Dictionary<string, Transform>(StringComparer.Ordinal);
                CloneBones(prefab.transform, toolRoot, own);
                tools.Add((d, toolRoot));
            }
            else if (tool) { L($"  장착 파츠 프리팹 없음, 본을 만들지 못함: {part.Model}"); Fail(part, "장착 파츠 프리팹이 없어 본을 만들지 못했습니다"); }
            Dictionary<string, SkinnedMeshRenderer> fbxSmr = null;
            var morph = part.Role == PartRole.Body ? MorphMaterials(d) : null;

            var list = d.Root.Get("SkinnedMeshList").Arr();
            if (list.Count == 0) Fail(part, "모델의 메시 목록이 비어 있습니다");
            var byIndex = new Dictionary<int, Material[]>();
            for (int i = 0; i < list.Count; i++)
            {
                string name = list[i].Get("MeshName").Str() ?? "";
                bool helper = false;
                foreach (var h in HelperPrefix) if (name.StartsWith(h, StringComparison.Ordinal)) helper = true;
                string smm = $"{d.Dir}{part.Model}_{i}.SkinnedModularMesh.bytes";
                if (helper)
                {
                    // 절단면 메시는 그리지 않고 몸 자르기에만 쓴다.
                    if (part.Role == PartRole.Costume && BodyCut.LimbOfSlicer(name) != null && files.HasResource(smm))
                    {
                        var sm = SmmReader.Read(files.ReadResource(smm), out var sb);
                        view.Owned.Add(sm);
                        var sbt = new Transform[sb.Length];
                        for (int b = 0; b < sb.Length; b++) sbt[b] = BoneFor(d, sb[b]);
                        slicers.Add(new BodyCut.SlicerMesh { Key = part.Key, Name = name, Mesh = sm, BoneNames = sb, Bones = sbt });
                    }
                    continue;
                }
                Mesh mesh; string[] boneNames; bool owned = false;
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
                    if (!fbxSmr.TryGetValue(name, out var fsmr) || !fsmr.sharedMesh) { L($"  메시 없음: {part.Model}/{name}"); Fail(part, "메시를 읽지 못했습니다: " + name); continue; }
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
                    if ((bt[b] = BoneFor(d, boneNames[b])) == null) { bt[b] = root.transform; missing++; }
                smr.bones = bt;
                smr.rootBone = bt.Length > 0 ? bt[0] : root.transform;
                smr.updateWhenOffscreen = true;
                // 캐릭터 그늘은 셰이더 툰 명암·헤어 그림자 메시가 그린다. Unity 그림자 맵은 받지 않고 바닥에 드리우기만 한다.
                smr.receiveShadows = false;

                var srcMats = morph ?? (psmr ? psmr.sharedMaterials : Array.Empty<Material>());
                var subMats = new Material[mesh.subMeshCount];
                for (int s = 0; s < subMats.Length; s++)
                {
                    var src = srcMats.Length == 0 ? null : srcMats[Mathf.Min(s, srcMats.Length - 1)];
                    if (!src) Fail(part, "메시 재질을 읽지 못했습니다: " + name);
                    if (DebugMaterials && src) L($"  재질 {part.Model}/{name}[{s}]: {src.name} 셰이더 {(src.shader ? src.shader.name : "-")} 큐 {src.renderQueue} 텍스처 {DescribeTextures(src)}");
                    subMats[s] = Convert(src, (part.Role == PartRole.Eyes && KeepOriginalEyeShader) || (part.Role == PartRole.Head && KeepOriginalHeadShader), out _);
                }
                smr.sharedMaterials = subMats;

                // 프리팹의 켜짐·꺼짐을 그대로 따른다(예: 눈의 Mesh_EyeBasePlane 은 감은 눈 평면이라 기본으로 꺼져 있다).
                if (psmr) { go2.SetActive(psmr.gameObject.activeSelf); smr.enabled = psmr.enabled; }
                var lv = HairLevel.Match(name);
                if (part.Role == PartRole.Hair && lv.Success && lv.Groups[1].Value[0] - '0' != hairLevel) go2.SetActive(false);
                view.Renderers.Add((part.Key, smr));
                byIndex[i] = subMats;
                if (part.Role == PartRole.Body) { view.Body = smr; view.BodyFullMesh = mesh; }
                // 의상 피부 조각(ClothingMaterialType=Skin)도 그 자리의 몸을 대신하므로 가리기 판정에 넣는다.
                // 무기처럼 장착점에 붙는 파츠는 몸을 덮는 옷이 아니므로 넣지 않는다.
                if (part.Role == PartRole.Costume && !tool && go2.activeSelf && partGo.activeSelf) { cloth.Add(smr); clothByKey.Add((part.Key, smr)); }
                if (missing > 0) { L($"  {part.Model}/{name}: 본 {missing}개 없음"); Fail(part, $"메시 {name}의 본 {missing}개를 찾지 못했습니다"); }
            }

            foreach (var slot in d.Root.Get("DyeInfo").Get("Slots").Arr())
            {
                string sname = slot.Get("Slot").Str();
                string smat = slot.Get("Material").Str();
                foreach (var w in slot.Get("Wirings").Arr())
                {
                    int mi = w.Get("MeshIndex").I(), ma = w.Get("MaterialIndex").I(), ch = w.Get("ChannelIndex").I();
                    if (!byIndex.TryGetValue(mi, out var mats) || ma < 0 || ma >= mats.Length || ch < 0 || ch > 2) continue;
                    var m = mats[ma]; string prop = "_DyeColor" + (ch + 1);
                    if (!m || !m.HasProperty(prop)) continue;
                    view.Dyes.Add(new DyeBinding { PartKey = part.Key, Slot = sname, Material = m, Property = prop, Original = m.GetColor(prop), DyeMaterial = smat });
                }
            }
        }

        foreach (var d in defs)
            try { AddBones(d); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { Fail(d.Part, "본 읽기 실패: " + e.Message); }
        foreach (var d in defs) if (d.Part.Role != PartRole.Mouth)
            try { AddModel(d); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { Fail(d.Part, "모델 조립 실패: " + e.Message); }
        Transform BoneFor(Def d, string name)
        {
            if (name == null) return null;
            if (d != null && partBones.TryGetValue(d, out var own) && own.TryGetValue(name, out var t)) return t;
            return map.TryGetValue(name, out t) ? t : null;
        }
        foreach (var t in bones)
            t.SetParent(BoneFor(boneOwner.TryGetValue(t, out var od) ? od : null, parentName[t]) ?? root.transform, false);
        if (partOwnBones > 0) L($"파츠 전용 본 {partOwnBones}개(기본 회전이 다른 같은 이름 본)");
        var mounts = new List<ToolMount>();
        foreach (var (d, toolRoot) in tools) mounts.Add(Mount(d, toolRoot));

        // 장착점: 상태(Undrawn·Drawn)별 AttachTo 본 + 성별·어린 외형별 위치·회전(오일러 각, 도)·크기.
        // ModelPartVisibleState 가 AlwaysInvisible 이면 그 상태에서 숨긴다(오브·코일·너클 등은 넣으면 사라진다).
        // 두 상태를 모두 준비해 두고 모션이 도입 구간(꺼내기·넣기) 중에 바꾼다.
        ToolMount Mount(Def d, Transform toolRoot)
        {
            var info = d.Root.Get("ModelPartInfo");
            var m = new ToolMount
            {
                Model = d.Part.Model, Root = toolRoot,
                ToolType = info.Get("AppearanceSlot").Str() ?? "MainTool",
                AnimationGroup = info.Get("ModelPartAnimationGroupName").Str(),
            };
            string gender = plan.Male ? "Male" : "Female";
            foreach (var ap in info.Get("AttachPointInfo").Arr())
            {
                bool drawn = string.Equals(ap.Get("State").Str(), "Drawn", StringComparison.OrdinalIgnoreCase);
                if (drawn ? m.Drawn.Valid : m.Undrawn.Valid) continue;
                string to = ap.Get("AttachTo").Str();
                Transform parent = to == "#MODEL_ROOT" || string.IsNullOrEmpty(to) ? root.transform : BoneFor(null, to);
                if (!parent) { L($"  장착 본 없음: {d.Part.Model} → {to}"); parent = root.transform; }
                object pick = null; int best = -1;
                foreach (var t in ap.Get("Transforms").Arr())
                {
                    bool infant = t.Get("IsInfant") is bool bb ? bb : t.Get("IsInfant").F() != 0;
                    string g = t.Get("Gender").Str();
                    int score = (infant == plan.Infant ? 4 : 0) + (g == gender ? 2 : g == "None" ? 1 : 0);
                    if (score > best) { best = score; pick = t; }
                }
                var pl = new ToolMount.Placement
                {
                    Valid = true, Parent = parent,
                    Hidden = ap.Get("ModelPartVisibleState").Str() == "AlwaysInvisible",
                    Position = pick != null ? Vec(pick.Get("Position"), Vector3.zero) : Vector3.zero,
                    Rotation = pick != null ? Quaternion.Euler(Vec(pick.Get("Rotation"), Vector3.zero)) : Quaternion.identity,
                    Scale = pick != null ? Vec(pick.Get("LocalScale"), Vector3.one) : Vector3.one,
                };
                if (drawn) m.Drawn = pl; else m.Undrawn = pl;
            }
            string partName = d.Part.Key + ":" + d.Part.Model;
            foreach (var (_, r) in view.Renderers)
                if (r && r.transform.parent && r.transform.parent.name == partName) m.Renderers.Add((r.gameObject, r.gameObject.activeSelf));
            foreach (var t in toolRoot.GetComponentsInChildren<Transform>(true))
                if (t != toolRoot) m.Bones.Add((t, t.localPosition, t.localRotation, t.localScale));
            bool start = plan.Intro != null ? plan.IntroDrawn : plan.WeaponDrawn;
            foreach (var sp in plan.IntroToolSpans) if (plan.Intro != null && sp.toolType == m.ToolType && sp.begin <= 0f && sp.end > 0f) start = sp.drawn;
            m.Apply(start);
            var cur = start ? m.Drawn : m.Undrawn;
            L($"  장착: {d.Part.Model} {(start ? "Drawn" : "Undrawn")} → {(cur.Parent == root.transform || !cur.Parent ? "루트" : cur.Parent.name)}{(cur.Hidden ? " (숨김)" : "")}, 종류 {m.ToolType}, 클립 그룹 {m.AnimationGroup}");
            return m;
        }

        int nb = bones.Count;
        view.Bones = bones.ToArray();
        view.BindPos = new Vector3[nb]; view.BindRot = new Quaternion[nb]; view.BindScl = new Vector3[nb];
        for (int i = 0; i < nb; i++) { view.BindPos[i] = bind[i].p; view.BindRot[i] = bind[i].r; view.BindScl[i] = bind[i].s; }
        view.PosePos = new Vector3[nb]; view.PoseRot = new Quaternion[nb]; view.PoseScl = new Vector3[nb]; view.HasPose = new bool[nb];
        int posed = 0;
        float rawComY = 0;
        string posePath = PlanResolver.PosePath(plan.Pose ?? "");
        if (files.HasResource(posePath) && files.ReadSjon(posePath).Get("Transforms") is Dictionary<string, object> tr)
        {
            for (int i = 0; i < nb; i++)
            {
                if (!tr.TryGetValue(bones[i].name, out var t)) continue;
                view.HasPose[i] = true; posed++;
                view.PosePos[i] = V3(t.Get("LocalPosition")); view.PoseRot[i] = Q(t.Get("LocalRotation")); view.PoseScl[i] = V3(t.Get("LocalScale"));
                if (bones[i].name == "_COM" && parentName[bones[i]] == null) rawComY = view.PosePos[i].y;
                if (poseBase.TryGetValue(bones[i], out var pb))
                {
                    view.PoseRot[i] = view.PoseRot[i] * Quaternion.Inverse(pb.r) * bind[i].r;
                    view.PosePos[i] = bind[i].p + (view.PosePos[i] - pb.p);
                }
            }
        }
        else L($"포즈 없음: {plan.Pose}");

        // 나이·키·체형별 본 스케일·위치(몸 .bdc). 같은 이름의 파츠 전용 본에도 적용한다.
        var bodyDef = defs.Find(x => x.Part.Role == PartRole.Body);
        if (bodyDef != null)
        {
            string bdc = bodyDef.Dir + bodyDef.Part.Model + ".bdc.sjon.bin";
            string info = null;
            var states = files.HasResource(bdc) ? BodyDiversity.Resolve(files.ReadSjon(bdc), plan.Age, plan.BodyType, plan.HeightModifier, out info) : null;
            if (states == null) L($"키·나이 데이터 없음: {bdc} (나이 {plan.Age}, 체형 {plan.BodyType})");
            else
            {
                // 발바닥 높이를 맞추려고 적용 전 포즈의 발바닥 높이를 잰다.
                view.SetPosed(true);
                float sole0 = SoleHeight(view);
                // .bdc 스케일은 본마다의 누적 스케일이다(14세: 몸통·팔다리 모두 0.787, 머리 0.97).
                // Unity 로컬 스케일 = 자기 값 ÷ 데이터가 있는 가장 가까운 조상의 값. 위치는 부모 스케일 공간 값이라 그대로 쓴다.
                // _COM 위치는 게임이 따로 정한다(comFollow). 데이터 위치(0,0,0)는 쓰지 않고 아래에서 발바닥 높이로 맞춘다.
                int applied = 0;
                for (int i = 0; i < nb; i++)
                {
                    if (!states.TryGetValue(bones[i].name, out var st)) continue;
                    Vector3 parentScale = Vector3.one;
                    for (var pt = bones[i].parent; pt != null && pt != root.transform; pt = pt.parent)
                        if (states.TryGetValue(pt.name, out var ps)) { parentScale = ps.scale; break; }
                    var local = new Vector3(st.scale.x / Mathf.Max(parentScale.x, 1e-4f), st.scale.y / Mathf.Max(parentScale.y, 1e-4f), st.scale.z / Mathf.Max(parentScale.z, 1e-4f));
                    bool com = bones[i].name == "_COM";
                    view.BindScl[i] = local;
                    if (!com) view.BindPos[i] = st.pos;
                    if (view.HasPose[i]) { view.PoseScl[i] = local; if (!com) view.PosePos[i] = st.pos; }
                    applied++;
                }
                view.SetPosed(true);
                float drop = SoleHeight(view) - sole0;
                int ci = bones.FindIndex(x => x.name == "_COM" && parentName[x] == null);
                if (ci >= 0 && !float.IsNaN(drop))
                {
                    view.BindPos[ci] -= new Vector3(0, drop, 0);
                    if (view.HasPose[ci]) view.PosePos[ci] -= new Vector3(0, drop, 0);
                }
                L($"키·나이: {info}, 본 {applied}개, 발바닥 높이 보정 {-drop:F3}m");
            }
        }

        foreach (var d in defs) if (d.Part.Role == PartRole.Mouth && !plan.Issues.Exists(i => i.Key == "head" && i.FromBuild && i.IsFailure))
            try { if (!ApplyMouth(bundles, d.Root, view, L)) Fail(d.Part, "입 텍스처를 얼굴에 적용하지 못했습니다"); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { Fail(d.Part, "입 텍스처 읽기 실패: " + e.Message); }
        // 실패한 임시 조립본으로 몸 절단·애니메이션을 확정하지 않는다. 호출자가 제외·대체 후 다시 조립한다.
        if (plan.Issues.Exists(i => i.FromBuild && i.IsFailure)) return view;
        ApplyFacialAnimation(files, plan, view, L);
        view.ApplyBlendShape(plan.BlendShape);
        view.ApplyPlanColors(plan);
        // 몸 가리기는 기본 자세에서 계산한다.
        view.SetPosed(false);
        if (view.Body && cloth.Count > 0)
        {
            var limbCuts = BodyCut.LimbCuts(slicers, map, clothByKey, L);
            view.BodyCutMesh = BodyCut.Compute(view.Body, cloth, BodyCutDistance, limbCuts, L);
            if (view.BodyCutMesh) view.Owned.Add(view.BodyCutMesh);
        }
        view.SetBodyCut(true);
        view.SetPosed(true);
        AttachIdle(files, bundles, plan, view, bones, map, poseBase, bind, rawComY, mounts, L);
        CheckDyes(plan, view);
        L($"조립: 파츠 {defs.Count}, 본 {nb}(포즈 {posed}), 렌더러 {view.Renderers.Count}, 염색 연결 {view.Dyes.Count}, 헤어 단계 H{hairLevel}, 번들 {bundles.Count}, {sw.ElapsedMilliseconds}ms");
        return view;
    }

    /// <summary>
    /// 원본 캐릭터 재질을 앱 캐릭터 셰이더 재질로 바꾼다(복사본은 owned 가 소유). keepOriginal 이면 원본 셰이더 복사본을 돌려준다.
    /// 조립기와 소셜 모션 소품(MMN/CH 셰이더)이 같이 쓴다.
    /// </summary>
    internal static Material ConvertMaterial(Material m, Shader shader, List<UnityEngine.Object> owned, bool keepOriginal, out bool shadow)
    {
        shadow = false;
        if (!m) { var e = new Material(shader); owned.Add(e); return e; }
        var c = new Material(m) { name = m.name };
        owned.Add(c);
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

    /// <summary>
    /// 사용자가 지정한 의상 염색 슬롯이 조립한 모델의 DyeInfo 에 있는지 확인한다(SaveLoad.md "사라진 염색 파트").
    /// 없으면 값은 보존하고 다른 파트에 옮기지 않으며 문제로만 남긴다. 색만 바꾼 편집 뒤에도 다시 부른다.
    /// </summary>
    public static void CheckDyes(AssemblyPlan plan, CharacterView view)
    {
        plan.Issues.RemoveAll(i => i.FromBuild && i.Kind == IssueKind.DyeUnresolved);
        var have = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in view.Dyes) have.Add(d.PartKey + "|" + d.Slot);
        foreach (var p in plan.Parts)
        {
            if (p.Role != PartRole.Costume) continue;
            var res = plan.Costumes.Find(c => AssemblyPlan.CostumeKey(c.Part) == p.Key);
            foreach (var slot in p.UserSlots)
                if (!have.Contains(p.Key + "|" + slot))
                    plan.AddIssue(p.Key, IssueKind.DyeUnresolved, res?.CostumeId ?? p.Model, res?.DisplayName,
                        $"염색 파트 {slot}: 이 모델에 해당 파트가 없어 적용하지 않습니다(값 보존)", true);
        }
    }

    /// <summary>색만 바꾼 편집: 새 계획에 이전 조립에서 찾은 문제(리소스 읽기 실패)와 옷 모양 조건을 옮기고 염색 파트를 다시 확인한다.</summary>
    public static void CarryBuildResults(AssemblyPlan from, AssemblyPlan to, CharacterView view)
    {
        if (from != null)
        {
            foreach (var i in from.Issues) if (i.FromBuild && i.Kind != IssueKind.DyeUnresolved && !to.Issues.Exists(x => x.Same(i))) to.Issues.Add(i);
            to.Clothing = from.Clothing;
        }
        CheckDyes(to, view);
    }

    /// <summary>.fbx 에셋(Assets/PatchableAssets/ 경로)의 AnimationClip 서브에셋. 없거나 열리지 않으면 null.</summary>
    internal static AnimationClip LoadAnimationClip(InstallFiles files, BundleCache bundles, string fbx)
    {
        var bundle = fbx != null && files.HasAsset(fbx) ? bundles.BundleOf(fbx) : null;
        if (bundle)
            foreach (var c in bundle.LoadAssetWithSubAssets<AnimationClip>(fbx))
                if (c && !c.legacy) return c;
        return null;
    }

    /// <summary>얼굴: 같은 이름 + _Facial 의 본 클립(.fbx)과 블렌드셰이프·표시·셰이더 트랙(.xaf). 없으면 각각 null.</summary>
    internal static (AnimationClip clip, FacialXaf xaf) LoadFace(InstallFiles files, BundleCache bundles, CharacterView view, string fbx)
    {
        string stem = fbx.Substring(0, fbx.Length - ".fbx".Length);
        var fc = LoadAnimationClip(files, bundles, stem + "_Facial.fbx");
        string n = stem.Substring(fbx.LastIndexOf('/') + 1);
        string xp = files.FindResourcePathEndingWith("/" + n + "_Facial.xaf.sjon.bin");
        return (fc, xp != null ? FacialXaf.Bind(files.ReadSjon(xp), view) : null);
    }

    /// <summary>
    /// 모션 클립(.fbx 의 AnimationClip 서브에셋)을 찾아 <see cref="CharacterMotion"/> 을 붙이고 첫 프레임을 적용한다.
    /// 도입 클립(plan.Intro)이 있으면 한 번 재생한 뒤 반복 클립(plan.Idle)으로 넘어간다. 클립이 없거나 열리지 않으면 정지 포즈를 유지한다.
    /// </summary>
    static void AttachIdle(InstallFiles files, BundleCache bundles, AssemblyPlan plan, CharacterView view, List<Transform> bones, Dictionary<string, Transform> map,
        Dictionary<Transform, (Vector3 p, Quaternion r)> poseBase, List<(Vector3 p, Quaternion r, Vector3 s)> bind, float rawComY, List<ToolMount> mounts, Action<string> L)
    {
        if (plan.Idle == null) return;
        AnimationClip LoadClip(string fbx) => LoadAnimationClip(files, bundles, fbx);
        (AnimationClip clip, FacialXaf xaf) Face(string fbx) => LoadFace(files, bundles, view, fbx);
        MotionSegment Segment(string fbx, bool drawn)
        {
            var body = LoadClip(fbx);
            if (!body) return null;
            var seg = new MotionSegment { Body = body, Drawn = drawn, Tools = new AnimationClip[mounts.Count] };
            (seg.Face, seg.Xaf) = Face(fbx);
            // 무기 자체 클립(ModelPartAnimation): 이 몸 클립 전용 행, 없으면 그룹의 Default 행.
            string rel = fbx.StartsWith(BundleCache.AssetRoot, StringComparison.Ordinal) ? fbx.Substring(BundleCache.AssetRoot.Length) : fbx;
            for (int i = 0; i < mounts.Count; i++)
            {
                string pc = plan.PartClip?.Invoke(mounts[i].AnimationGroup, rel);
                if (pc != null) seg.Tools[i] = LoadClip(BundleCache.AssetRoot + pc);
            }
            return seg;
        }
        var loop = Segment(plan.Idle, plan.WeaponDrawn);
        if (loop == null) { L($"아이들 모션을 열지 못함: {plan.Idle}"); return; }
        MotionSegment intro = null;
        if (plan.Intro != null)
        {
            intro = Segment(plan.Intro, plan.IntroDrawn);
            if (intro == null) L($"도입 모션을 열지 못함: {plan.Intro}");
            else intro.ToolSpans.AddRange(plan.IntroToolSpans);
        }
        // 구간에 얼굴 클립이 없으면(직업 자세 등) 꾸미기 아이들의 얼굴을 따로 반복한다.
        AnimationClip fallbackFace = null; FacialXaf fallbackXaf = null;
        if ((!loop.Face || (intro != null && !intro.Face)) && plan.FaceIdle != null)
            (fallbackFace, fallbackXaf) = Face(plan.FaceIdle);
        string Desc(MotionSegment sg)
        {
            int tc = 0; foreach (var t in sg.Tools) if (t) tc++;
            return $"{sg.Body.name} {sg.Body.length:F2}s, 얼굴 {(sg.Face ? sg.Face.name : "대체")}, xaf {(sg.Xaf != null ? "있음" : "없음")}, 무기 클립 {tc}";
        }
        if (intro != null) L($"도입 모션: {Desc(intro)}, 무기 상태 구간 {intro.ToolSpans.Count}");
        L($"반복 모션: {Desc(loop)}");
        if (fallbackFace || fallbackXaf != null) L($"대체 얼굴: {(fallbackFace ? $"{fallbackFace.name} {fallbackFace.length:F2}s" : "없음")}, xaf {(fallbackXaf != null ? $"트랙 {fallbackXaf.TrackCount} {fallbackXaf.Length:F2}s" : "없음")}");
        int com = bones.FindIndex(x => x.name == "_COM" && x.parent == view.transform);
        // 클립의 _COM 위치는 원본 정지 포즈 높이 기준이다. 나이·키 보정 후 높이와의 비율로 줄인다.
        float comScale = com >= 0 && view.HasPose[com] && rawComY > 1e-3f ? view.PosePos[com].y / rawComY : 1f;
        var parts = new List<(Transform dup, Transform main, Quaternion rel)>();
        for (int i = 0; i < bones.Count; i++)
            if (poseBase.TryGetValue(bones[i], out var pb) && map.TryGetValue(bones[i].name, out var main))
                parts.Add((bones[i], main, Quaternion.Inverse(pb.r) * bind[i].r));
        var motion = view.gameObject.AddComponent<CharacterMotion>();
        motion.Init(view, com, comScale, parts, intro, loop, fallbackFace, fallbackXaf, mounts);
        motion.Sample(0);
        L($"아이들 모션: {loop.Body.name} {loop.Body.length:F2}s, _COM 비율 {comScale:F3}, 파츠 전용 본 {parts.Count}");
    }

    /// <summary>
    /// 포즈의 얼굴 애니메이션(&lt;포즈&gt;_Facial.xaf)에서 첫 프레임의 메시 표시 여부와 셰이더 값을 적용한다.
    /// 게임은 눈·입술 메시의 표시를 이 애니메이션의 Visibility 트랙으로 정한다(EyeVisibleSetter). 트랙이 없는 메시는 프리팹 값을 유지한다.
    /// </summary>
    static void ApplyFacialAnimation(InstallFiles files, AssemblyPlan plan, CharacterView view, Action<string> log)
    {
        if (string.IsNullOrEmpty(plan.Pose)) return;
        string path = files.FindResourcePathEndingWith("/" + plan.Pose + "_Facial.xaf.sjon.bin");
        if (path == null) { log?.Invoke($"얼굴 애니메이션 없음: {plan.Pose}_Facial"); return; }
        var anim = files.ReadSjon(path);
        int start = anim.Get("StartFrame").I();
        var visible = new Dictionary<string, bool>(StringComparer.Ordinal);
        var shader = new List<(string obj, string prop, float value)>();
        foreach (var data in anim.Get("AnimationDatas").Arr())
        {
            string obj = data.Get("AnimationObjectName").Str();
            if (obj == null) continue;
            foreach (var track in data.Get("KeyframeTracks").Arr())
            {
                var keys = track.Get("Keyframes").Arr();
                if (keys.Count == 0) continue;
                // 시작 프레임 이하의 마지막 키 값을 쓴다(첫 프레임 정지 화면).
                object chosen = keys[0];
                foreach (var k in keys) if (k.Get("Time").F() <= start) chosen = k;
                float value = chosen.Get("Value").F();
                string type = track.Get("AnimationType").Str();
                if (type == "Visibility") visible[obj] = value != 0;
                else if (type == "Shader") shader.Add((obj, track.Get("AnimationPropertyName").Str(), value));
            }
        }
        int applied = 0;
        foreach (var (_, r) in view.Renderers)
            if (r && visible.TryGetValue(r.gameObject.name, out bool on)) { r.gameObject.SetActive(on); applied++; }
        // 셰이더 값 트랙(예: Mesh_Skin_Head 의 _MouthShowType, _EmotionMouthMapAtlasSize.z). ".x/.y/.z/.w" 는 벡터 성분이다.
        int props = 0;
        foreach (var (obj, prop, value) in shader)
        {
            if (string.IsNullOrEmpty(prop)) continue;
            int dot = prop.LastIndexOf('.');
            string name = dot > 0 ? prop.Substring(0, dot) : prop;
            int comp = dot > 0 ? "xyzw".IndexOf(prop[dot + 1]) : -1;
            foreach (var (_, r) in view.Renderers)
            {
                if (!r || r.gameObject.name != obj) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (!m || !m.HasProperty(name)) continue;
                    if (comp >= 0) { var v = m.GetVector(name); v[comp] = value; m.SetVector(name, v); }
                    else m.SetFloat(name, value);
                    props++;
                }
            }
        }
        log?.Invoke($"얼굴 애니메이션 적용: {path.Substring(path.LastIndexOf('/') + 1)}, 표시 트랙 {visible.Count}(렌더러 {applied}), 셰이더 값 {props}");
    }

    /// <summary>진단용: 원본 머티리얼 정보를 로그에 남긴다.</summary>
    public static bool DebugMaterials;

    /// <summary>눈 파츠는 게임 원본 셰이더를 그대로 쓴다. 대체 셰이더는 눈 테두리·그늘(Eye_Base, EyeShade 등)이 두껍고 검게 나온다(2026-11-01 촬영 비교).</summary>
    public static bool KeepOriginalEyeShader = true;

    /// <summary>
    /// 머리 파츠는 게임 원본 셰이더(MMN/CH/Skin_Face)를 그대로 쓴다. 입 텍스처(_MouthMap)를 얼굴에 합성하는 계산이 이 셰이더 안에 있다.
    /// false 면 대체 셰이더를 쓰고 입은 보이지 않는다.
    /// </summary>
    public static bool KeepOriginalHeadShader = true;

    /// <summary>
    /// 입 파츠(텍스처 모델)를 얼굴 머티리얼에 건다. 게임의 MaterialPropertySetter.ApplyTextureModelPart 와 같은 대응:
    ///   _MouthMap = AssetPathToLoad 텍스처, _MouthMapScalePosition = TextureInfo.BaseTexcoord,
    ///   _MouthMapRotation = TextureInfo.Rotation, _MouthPushStrength = TextureInfo.PushStrength
    /// </summary>
    static bool ApplyMouth(BundleCache bundles, object def, CharacterView view, Action<string> log)
    {
        string path = BundleCache.AssetRoot + def.Get("AssetPathToLoad").Str();
        var tex = bundles.LoadAsset<Texture2D>(path);
        if (!tex) { log?.Invoke("입 텍스처 없음: " + path); return false; }
        var info = def.Get("TextureInfo");
        var st = info.Get("BaseTexcoord").Arr();
        var scalePos = st.Count >= 4 ? new Vector4(st[0].F(), st[1].F(), st[2].F(), st[3].F()) : new Vector4(1, 1, 0, 0);
        int applied = 0;
        foreach (var (key, r) in view.Renderers)
        {
            if (key != "head" || !r) continue;
            foreach (var m in r.sharedMaterials)
            {
                if (!m || !m.HasProperty("_MouthMap")) continue;
                m.SetTexture("_MouthMap", tex);
                m.SetVector("_MouthMapScalePosition", scalePos);
                m.SetFloat("_MouthMapRotation", info.Get("Rotation").F());
                m.SetFloat("_MouthPushStrength", info.Get("PushStrength").F());
                applied++;
            }
        }
        log?.Invoke($"입: {tex.name}, 크기·위치 {scalePos}, 얼굴 머티리얼 {applied}개" + (applied == 0 ? " (머리 셰이더에 _MouthMap 없음)" : ""));
        return applied > 0;
    }

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

    /// <summary>현재 자세에서 좌우 발바닥 본(_L/_R Sole) 중 낮은 쪽의 높이. 없으면 NaN.</summary>
    static float SoleHeight(CharacterView view)
    {
        float y = float.NaN;
        foreach (var n in new[] { "_L Sole", "_R Sole" })
        {
            var t = view.FindBone(n);
            if (t) y = float.IsNaN(y) ? t.position.y : Mathf.Min(y, t.position.y);
        }
        return y;
    }

    internal static Vector3 Vec(object o, Vector3 fallback) { var l = o.Arr(); return l.Count >= 3 ? new Vector3(l[0].F(), l[1].F(), l[2].F()) : fallback; }

    /// <summary>프리팹의 본(렌더러가 없는 Transform) 계층을 같은 로컬 값으로 복제한다. 이름 → 복제본을 <paramref name="byName"/> 에 넣는다.</summary>
    static void CloneBones(Transform src, Transform dst, Dictionary<string, Transform> byName)
    {
        foreach (Transform c in src)
        {
            if (c.GetComponent<Renderer>()) continue;
            var t = new GameObject(c.name).transform;
            t.SetParent(dst, false);
            t.localPosition = c.localPosition; t.localRotation = c.localRotation; t.localScale = c.localScale;
            if (!byName.ContainsKey(c.name)) byName[c.name] = t;
            CloneBones(c, t, byName);
        }
    }

    static Vector3 V3(object o) { var l = o.Arr(); return l.Count >= 3 ? new Vector3(l[0].F(), l[1].F(), l[2].F()) : Vector3.one; }
    static Quaternion Q(object o) { var l = o.Arr(); return l.Count >= 4 ? new Quaternion(l[0].F(), l[1].F(), l[2].F(), l[3].F()) : Quaternion.identity; }
}

/// <summary>
/// 키·나이 본 데이터(.bdc). 게임의 HeightDiversityContainer · BodyDiversityStateSelector 와 같은 규칙.
///   (나이, 체형) 묶음마다 키 단계 Short·Medium·Tall 의 본 로컬 스케일·위치가 있다.
///   HeightModifier ≤ 0 이면 Short→Medium 을 t = HeightModifier + 1 로, > 0 이면 Medium→Tall 을 t = HeightModifier 로 선형 보간한다
///   (GetHeightStateFromHeightModifier: min -1, mid 0. 보간은 BodyDiversityBonePoseStateLerped 의 a + (b - a)·t).
/// </summary>
public static class BodyDiversity
{
    public static Dictionary<string, (Vector3 pos, Vector3 scale)> Resolve(object root, int age, string bodyType, float heightModifier, out string info)
    {
        info = null;
        var colls = root.Get("HeightDiversityDatapointCollection").Arr();
        if (colls.Count == 0) return null;
        bodyType = string.IsNullOrEmpty(bodyType) ? "Default" : bodyType;
        // 같은 나이·체형 → 같은 나이 Default → 가장 가까운 나이(데이터 범위 밖 나이 보정).
        object coll = null; int best = int.MaxValue;
        foreach (var c in colls)
        {
            int ca = c.Get("Age").I();
            string cb = c.Get("BlendshapeName").Str() ?? "Default";
            int score = Math.Abs(ca - age) * 10 + (cb == bodyType ? 0 : cb == "Default" ? 1 : 5);
            if (score < best) { best = score; coll = c; }
        }
        float hm = Mathf.Clamp(heightModifier, -1f, 1f);
        string e0 = hm <= 0 ? "Short" : "Medium", e1 = hm <= 0 ? "Medium" : "Tall";
        float t = hm <= 0 ? hm + 1f : hm;
        object p0 = null, p1 = null;
        foreach (var p in coll.Get("HeightDiversityDatapoints").Arr())
        {
            string e = p.Get("HeightEnum").Str();
            if (e == e0) p0 = p;
            if (e == e1) p1 = p;
        }
        if (p0 == null || p1 == null) return null;
        var a = Bones(p0); var b = Bones(p1);
        var result = new Dictionary<string, (Vector3, Vector3)>(StringComparer.Ordinal);
        foreach (var kv in a)
        {
            var o = b.TryGetValue(kv.Key, out var v) ? v : kv.Value;
            result[kv.Key] = (Vector3.LerpUnclamped(kv.Value.pos, o.pos, t), Vector3.LerpUnclamped(kv.Value.scale, o.scale, t));
        }
        float h0 = p0.Get("HeightHint").F(), h1 = p1.Get("HeightHint").F();
        info = $"나이 {coll.Get("Age").I()} 체형 {coll.Get("BlendshapeName").Str()} 키 {e0}→{e1} t={t:F2}, 키 참고값 {h0 + (h1 - h0) * t:F1}";
        return result;
    }

    static Dictionary<string, (Vector3 pos, Vector3 scale)> Bones(object point)
    {
        var d = new Dictionary<string, (Vector3, Vector3)>(StringComparer.Ordinal);
        foreach (var s in point.Get("BoneLocalScales").Arr())
        {
            string name = s.Get("BoneName").Str();
            if (name == null) continue;
            d[name] = (new Vector3(s.Get("LocalPositionX").F(), s.Get("LocalPositionY").F(), s.Get("LocalPositionZ").F()),
                       new Vector3(s.Get("LocalScaleX").F(), s.Get("LocalScaleY").F(), s.Get("LocalScaleZ").F()));
        }
        return d;
    }
}

public static class BodyCut
{
    public sealed class SlicerMesh
    {
        public string Key, Name;
        public Mesh Mesh;
        public string[] BoneNames;
        public Transform[] Bones;
    }

    /// <summary>팔다리 절단면. Normal 쪽(의상이 덮는 쪽)에 있는, 같은 팔다리 중 Levels 부위의 몸을 자른다.</summary>
    public struct LimbCut
    {
        public string Limb;
        /// <summary>비트 0 위팔·허벅지, 1 아래팔·종아리, 2 손·발</summary>
        public int Levels;
        public Vector3 Point, Normal;
    }

    static readonly Regex SlicerName = new Regex(@"^Mesh_SP_([LR])(Hand|Arm|Foot|Leg)$");

    /// <summary>Mesh_SP_LHand → "L arm", Mesh_SP_RFoot → "R leg". 팔다리 절단면이 아니면 null.</summary>
    public static string LimbOfSlicer(string meshName)
    {
        var m = SlicerName.Match(meshName ?? "");
        if (!m.Success) return null;
        string kind = m.Groups[2].Value;
        return m.Groups[1].Value + (kind == "Hand" || kind == "Arm" ? " arm" : " leg");
    }

    /// <summary>
    /// 본 이름 → 팔다리("L arm", "R leg")와 부위 단계(0 위팔·허벅지, 1 아래팔·종아리, 2 손·발).
    /// 게임의 PoseSegment(LeftUpperArm, LeftLowerArm, LeftHand …)에 대응한다. 몸통·어깨(쇄골·삼각근)·골반이면 null.
    /// </summary>
    public static string LimbOfBone(string bone, out int level)
    {
        level = -1;
        if (string.IsNullOrEmpty(bone) || bone.Contains("SP Skirt")) return null;
        string side = bone.StartsWith("_L") || bone.EndsWith("toolL") ? "L" : bone.StartsWith("_R") || bone.EndsWith("toolR") ? "R" : null;
        if (side == null) return null;
        if (bone.Contains("UpperArm")) { level = 0; return side + " arm"; }
        if (bone.Contains("Forearm") || bone.Contains("Arm2tool")) { level = 1; return side + " arm"; }
        if (bone.Contains("Hand") || bone.Contains("Finger") || bone.Contains("Palm")) { level = 2; return side + " arm"; }
        if (bone.Contains("Thigh")) { level = 0; return side + " leg"; }
        if (bone.Contains("Calf")) { level = 1; return side + " leg"; }
        if (bone.Contains("Foot") || bone.Contains("Sole") || bone.Contains("Toe")) { level = 2; return side + " leg"; }
        return null;
    }

    static string DominantLimb(BoneWeight w, string[] names, out int level)
    {
        int i = w.boneIndex0; float best = w.weight0;
        if (w.weight1 > best) { i = w.boneIndex1; best = w.weight1; }
        if (w.weight2 > best) { i = w.boneIndex2; best = w.weight2; }
        if (w.weight3 > best) { i = w.boneIndex3; }
        level = -1;
        return i >= 0 && i < names.Length ? LimbOfBone(names[i], out level) : null;
    }

    static string[] Names(SkinnedMeshRenderer r)
    {
        var b = r.bones; var n = new string[b.Length];
        for (int i = 0; i < b.Length; i++) n[i] = b[i] ? b[i].name : null;
        return n;
    }

    /// <summary>
    /// 의상의 팔다리 절단면(Mesh_SP_*)으로 평면을 만든다. 평면은 절단면 정점의 무게중심과 면 노멀 합으로 정하고,
    /// 같은 의상의 그 팔다리 정점이 많은 쪽을 덮는 쪽으로 보고, 의상이 실제로 차지한 부위(위·아래·끝)에만 적용한다.
    /// 예: 하이힐의 발 절단면은 발바닥 아래 수평면이라 발 부위에만 적용해야 다리 전체가 잘리지 않는다. 현재 본 자세(기본 자세)에서 계산한다.
    /// 게임은 몸 삼각형마다 부위(PoseSegment)를 두고 절단면으로 자른다(FittingCutPolicy).
    /// 부위 데이터(.ClothingUnit.bytes)는 해석하지 않았으므로 본 가중치로 대신한다.
    /// </summary>
    public static List<LimbCut> LimbCuts(List<SlicerMesh> slicers, Dictionary<string, Transform> bones,
        List<(string key, SkinnedMeshRenderer smr)> cloth, Action<string> log)
    {
        var cuts = new List<LimbCut>();
        foreach (var s in slicers)
        {
            string limb = LimbOfSlicer(s.Name);
            var verts = s.Mesh.vertices; var bw = s.Mesh.boneWeights; var bp = s.Mesh.bindposes;
            if (verts.Length < 3 || bw.Length != verts.Length) continue;
            var skin = new Matrix4x4[s.BoneNames.Length];
            for (int b = 0; b < skin.Length; b++)
                skin[b] = b < bp.Length && b < s.Bones.Length && s.Bones[b] ? s.Bones[b].localToWorldMatrix * bp[b] : Matrix4x4.identity;
            var w = new Vector3[verts.Length];
            Vector3 center = Vector3.zero;
            for (int i = 0; i < verts.Length; i++)
            {
                var b = bw[i]; var v = verts[i]; Vector3 p = Vector3.zero;
                if (b.weight0 > 0) p += skin[b.boneIndex0].MultiplyPoint3x4(v) * b.weight0;
                if (b.weight1 > 0) p += skin[b.boneIndex1].MultiplyPoint3x4(v) * b.weight1;
                if (b.weight2 > 0) p += skin[b.boneIndex2].MultiplyPoint3x4(v) * b.weight2;
                if (b.weight3 > 0) p += skin[b.boneIndex3].MultiplyPoint3x4(v) * b.weight3;
                w[i] = p; center += p;
            }
            center /= verts.Length;
            if (log != null)
            {
                var bb = new Bounds(w[0], Vector3.zero); foreach (var q in w) bb.Encapsulate(q);
                var miss = new List<string>(); for (int b = 0; b < s.BoneNames.Length; b++) if (b >= s.Bones.Length || !s.Bones[b]) miss.Add(s.BoneNames[b]);
                log($"  절단면 메시 {s.Name}: 정점 {verts.Length}, 범위 {bb.min:F3}~{bb.max:F3}, 본 [{string.Join(",", s.BoneNames)}], 없는 본 [{string.Join(",", miss)}]");
            }
            Vector3 normal = Vector3.zero;
            var tri = s.Mesh.triangles;
            for (int i = 0; i + 2 < tri.Length; i += 3)
            {
                var n = Vector3.Cross(w[tri[i + 1]] - w[tri[i]], w[tri[i + 2]] - w[tri[i]]);
                normal += Vector3.Dot(n, normal) < 0 ? -n : n;
            }
            if (normal.sqrMagnitude < 1e-12f) continue;
            normal.Normalize();

            int pos = 0, neg = 0;
            var perLevel = new int[3];
            foreach (var (key, smr) in cloth)
            {
                if (key != s.Key || !smr.sharedMesh) continue;
                var names = Names(smr);
                var m = new Mesh(); smr.BakeMesh(m, true);
                var cv = m.vertices; var cw = smr.sharedMesh.boneWeights; var l2w = smr.transform.localToWorldMatrix;
                UnityEngine.Object.DestroyImmediate(m);
                for (int i = 0; i < cv.Length && i < cw.Length; i++)
                {
                    if (DominantLimb(cw[i], names, out int lv) != limb) continue;
                    perLevel[lv]++;
                    float d = Vector3.Dot(l2w.MultiplyPoint3x4(cv[i]) - center, normal);
                    if (d > 0.005f) pos++; else if (d < -0.005f) neg++;
                }
            }
            if (pos == 0 && neg == 0) { log?.Invoke($"몸 절단면 {s.Key}/{s.Name}: 같은 팔다리 의상 정점이 없어 건너뜀"); continue; }
            if (neg > pos) normal = -normal;
            int total = perLevel[0] + perLevel[1] + perLevel[2], levels = 0;
            for (int k = 0; k < 3; k++) if (perLevel[k] >= Math.Max(8, total * 0.03f)) levels |= 1 << k;
            cuts.Add(new LimbCut { Limb = limb, Levels = levels, Point = center, Normal = normal });
            log?.Invoke($"몸 절단면 {s.Key}/{s.Name}: {limb}, 부위 정점 {perLevel[0]}/{perLevel[1]}/{perLevel[2]} → 적용 {Convert.ToString(levels, 2)}, 중심 {center:F3}, 방향 {normal:F2}, 덮는 쪽 정점 {Math.Max(pos, neg)}/{pos + neg}");
        }
        return cuts;
    }

    /// <summary>
    /// ① 팔다리 절단면의 덮인 쪽에 있는 같은 팔다리 몸 정점을 자른다.
    /// ② 몸 정점의 노멀 방향 +dist / -3cm 안에 옷 면이 있으면 덮인 것으로 본다.
    /// 세 정점이 모두 ①·② 중 하나에 해당하는 삼각형을 뺀 메시를 만든다(근사).
    /// </summary>
    public static Mesh Compute(SkinnedMeshRenderer body, List<SkinnedMeshRenderer> cloth, float dist, List<LimbCut> limbCuts, Action<string> log)
    {
        var bm = new Mesh(); body.BakeMesh(bm, true);
        var bv = bm.vertices; var bn = bm.normals; var b2w = body.transform.localToWorldMatrix;
        UnityEngine.Object.DestroyImmediate(bm);
        var bodyNames = Names(body);
        var bodyWeights = body.sharedMesh.boneWeights;
        var sliced = new bool[bv.Length];
        int ns = 0;
        if (limbCuts != null && limbCuts.Count > 0 && bodyWeights.Length == bv.Length)
            for (int i = 0; i < bv.Length; i++)
            {
                string limb = DominantLimb(bodyWeights[i], bodyNames, out int lv);
                if (limb == null) continue;
                var p = b2w.MultiplyPoint3x4(bv[i]);
                foreach (var c in limbCuts)
                    if (c.Limb == limb && (c.Levels & (1 << lv)) != 0 && Vector3.Dot(p - c.Point, c.Normal) > 0) { sliced[i] = true; ns++; break; }
            }
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
                if ((covered[t[i]] || sliced[t[i]]) && (covered[t[i + 1]] || sliced[t[i + 1]]) && (covered[t[i + 2]] || sliced[t[i + 2]])) { removed++; continue; }
                keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]);
            }
            cut.SetTriangles(keep, s);
        }
        log?.Invoke($"몸 가리기 근사: 절단면 {limbCuts?.Count ?? 0}개로 자른 몸 정점 {ns}, 옷 삼각형 {nt}, 덮인 몸 정점 {nc}/{bv.Length}, 뺀 삼각형 {removed}/{total}");
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
