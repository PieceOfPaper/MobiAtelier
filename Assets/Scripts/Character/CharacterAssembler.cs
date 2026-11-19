// 조립 계획(AssemblyPlan)대로 설치 폴더에서 모델을 읽어 캐릭터를 만든다.
//   리소스(ResourcesPackage): StageObjectModel·BoneTemplate·포즈(SJON), 몸·의상 메시(SMM)
//   번들(AssetPackages)    : 프리팹(머티리얼·텍스처), 머리·헤어·눈 메시(프리팹 또는 FBX 번들의 SkinnedMeshRenderer)
// 규칙
//   본: 파츠 순서(몸 → 머리 → 헤어 → 눈 → 의상)대로 BoneTemplate 을 이름 기준 병합.
//       같은 이름인데 기본 회전이 다른 본(예: 장갑의 Forearm Twist 가 몸과 약 60° 다름)은 그 파츠 전용 본을 따로 두고,
//       포즈는 기존 본의 기본 자세 대비 변화량만 더한다. 하나로 합치면 그 파츠 정점이 비틀려 꺾인다.
//   메시: ① <모델>_<i>.SkinnedModularMesh.bytes ② 프리팹 SMR 의 sharedMesh ③ /<모델>.fbx 번들의 같은 이름 SMR
//   보조 메시(Mesh_BP_, Mesh_SP_, Cage_)는 그리지 않는다. 헤어는 Mesh_Hair_*_H0~H5 중 하나만 표시
//   몸 자르기: 몸과 의상의 .ClothingUnit.bytes(몸 삼각형 부위, 의상 분리면)로 게임과 같은 규칙으로 자른다(BodyCut, ClothingCut.cs).
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
    const string HoodOn = "Mesh_Hood_On", HoodOffPressed = "Mesh_Hood_Off_Pressed", HoodOff = "Mesh_Hood_Off";
    const string HelmetCoverOn = "Mesh_HelmetCover_On", HelmetCoverOff = "Mesh_HelmetCover_Off";
    /// <summary>
    /// 후드를 쓴 헤어 단계(게임 StageObjectCostumeVariantsComponent.hairLevelOnHood). 값은 정적 생성자 안이라 읽지 못했다.
    /// 후드를 쓴 채 구운 NPC 모델이 헤어 H5(H0 과 함께)만 가져 5로 본다 [추정].
    /// </summary>
    const int HoodHairLevel = 5;

    static readonly int SideClipHideId = Shader.PropertyToID("_SideClipHide");

    /// <summary>모델이 장식 슬롯(Accessory0~2)을 점유하면 그 번호, 아니면 null.</summary>
    static int? AccessorySlot(Def d)
    {
        string slot = d.Root.Get("ModelPartInfo").Get("AppearanceSlot").Str();
        return slot == "Accessory0" ? 0 : slot == "Accessory1" ? 1 : slot == "Accessory2" ? 2 : (int?)null;
    }

    /// <summary>
    /// 좌우 눈·눈썹 숨김(게임 EyeHideUtility.HideRendererForEyeHide·SetShaderPropertyForEyeHide).
    ///   눈: Mesh_EyeBase_L/R(흰자·속눈썹, 눈알·하이라이트도 이 영역 안에서만 그려진다)를 끈다. 감은 눈 평면은 한쪽이면 _SideClipHide, 양쪽이면 끈다.
    ///   눈썹: Mesh_EyeBrow 의 _SideClipHide 를 1(왼쪽)·2(오른쪽)로, 양쪽이면 끈다. 3 은 2 와 같아 양쪽 값이 아니다 [확인: 촬영 실험].
    ///   _L·왼쪽은 캐릭터 기준 왼쪽(정면 사진의 오른쪽)이다 [확인].
    /// </summary>
    static void HideEyeSide(SkinnedMeshRenderer smr, string name, bool leftEye, bool rightEye, bool leftBrow, bool rightBrow)
    {
        void Side(bool left, bool right)
        {
            if (left && right) { smr.enabled = false; return; }
            if (!left && !right) return;
            foreach (var m in smr.sharedMaterials) if (m && m.HasProperty(SideClipHideId)) m.SetFloat(SideClipHideId, left ? 1f : 2f);
        }
        if (name == "Mesh_EyeBase_L") { if (leftEye) smr.enabled = false; }
        else if (name == "Mesh_EyeBase_R") { if (rightEye) smr.enabled = false; }
        else if (name == "Mesh_EyeBasePlane") Side(leftEye, rightEye);
        else if (name == "Mesh_EyeBrow") Side(leftBrow, rightBrow);
    }

    static bool IsHoodMesh(string name) => name.StartsWith(HoodOn, StringComparison.Ordinal) || name.StartsWith(HoodOff, StringComparison.Ordinal);

    static bool HasMesh(Def d, Func<string, bool> match)
    {
        foreach (var m in d.Root.Get("SkinnedMeshList").Arr()) if (match(m.Get("MeshName").Str() ?? "")) return true;
        return false;
    }

    /// <summary>
    /// 헤어 모델에서 실제로 보일 단계. 원하는 단계 메시가 없으면 그보다 낮은 가장 가까운 단계, 그것도 없으면 가장 낮은 단계.
    /// 헤어 213개는 H0~H5 를 모두 갖는다 [확인: 설치본 모델 정의]. 일부만 가진 헤어를 위한 대비다.
    /// </summary>
    static int ShownHairLevel(Def hair, int wanted)
    {
        int below = -1, lowest = int.MaxValue;
        foreach (var m in hair.Root.Get("SkinnedMeshList").Arr())
        {
            var lv = HairLevel.Match(m.Get("MeshName").Str() ?? "");
            if (!lv.Success) continue;
            int n = lv.Groups[1].Value[0] - '0';
            if (n == wanted) return n;
            if (n < wanted && n > below) below = n;
            lowest = Math.Min(lowest, n);
        }
        return below >= 0 ? below : lowest == int.MaxValue ? wanted : lowest;
    }

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
        // 후드·모자 덮개(Equipment.md "후드·모자 덮개", 게임 StageObjectCostumeVariantsComponent·CostumeVisibleSetter).
        //   후드: 표시 중인 로브, 없으면 다른 의상 중 Mesh_Hood_On/Off 가 있는 첫 파츠 하나. 쓰면 모자를 숨기고 헤어는 HoodHairLevel [추정].
        //   덮개: 모자의 Mesh_HelmetCover_On/Off. 내리면 Hide*ByHelmetCover 숨김 규칙을 더한다.
        Def hoodDef = null, hatDef = null;
        foreach (var d in defs)
        {
            if (d.Part.Role != PartRole.Costume) continue;
            if (d.Part.Key == AssemblyPlan.CostumeKey("Hat")) hatDef = d;
            if (HasMesh(d, IsHoodMesh) && (hoodDef == null || d.Part.Key == AssemblyPlan.CostumeKey("Robe"))) hoodDef = d;
        }
        plan.HasHood = hoodDef != null;
        plan.HoodOn = plan.HasHood && plan.HoodRequested;
        plan.HatHiddenByHood = plan.HoodOn && hatDef != null;
        plan.HasHelmetCover = hatDef != null && !plan.HatHiddenByHood && HasMesh(hatDef, n => n.StartsWith(HelmetCoverOn, StringComparison.Ordinal));
        plan.HelmetCoverOn = plan.HasHelmetCover && plan.HelmetCoverRequested;
        bool hatShown = hatDef != null && !plan.HatHiddenByHood;

        bool hideHair = false, hideHead = false, hideEye = false, hideBeard = false;
        // 좌우 눈·눈썹(게임 EyeHideUtility): 눈은 Mesh_EyeBase_L/R 렌더러, 한쪽 눈썹은 눈썹 재질 _SideClipHide(1 왼쪽, 2 오른쪽) [확인: 촬영 실험].
        bool hideLeftEye = false, hideRightEye = false, hideLeftBrow = false, hideRightBrow = false;
        // 장식 숨김(HideAccessory0~2): 모델 점유 슬롯 Accessory0 얼굴장식·1 눈장식·2 귀장식 [확인: 설치본 모델 정의]. 숨기는 의상 자신은 숨기지 않는다.
        var accessoryHiders = new List<Def>[3] { new List<Def>(), new List<Def>(), new List<Def>() };
        int hairLevel = 0;
        foreach (var d in defs)
        {
            if (d.Part.Role != PartRole.Costume) continue;
            var info = d.Root.Get("ModelPartInfo");
            // 치마 등 옷 모양 조건(Action·AnimationStateSet 의 Clothing:ShortSkirt 등). 앉기 같은 모션이 옷에 맞는 클립을 고른다.
            string clothing = info.Get("ClothingCondition").Str();
            if (!string.IsNullOrEmpty(clothing) && clothing != "None") plan.Clothing = clothing;
            // 후드로 숨긴 모자는 숨김 규칙·헤어 단계를 쓰지 않는다.
            if (d == hatDef && !hatShown) continue;
            bool cover = d == hatDef && plan.HelmetCoverOn;
            bool Flag(string name) => info.Get(name).B() || (cover && info.Get(name + "ByHelmetCover").B());
            hideHair |= Flag("HideHair");
            hideHead |= Flag("HideHead");
            hideEye |= Flag("HideEye");
            hideBeard |= Flag("HideBeard");
            hideLeftEye |= Flag("HideLeftEye");
            hideRightEye |= Flag("HideRightEye");
            hideLeftBrow |= Flag("HideLeftEyebrow");
            hideRightBrow |= Flag("HideRightEyebrow");
            for (int n = 0; n < 3; n++) if (Flag("HideAccessory" + n)) accessoryHiders[n].Add(d);
            foreach (var m in d.Root.Get("SkinnedMeshList").Arr())
            {
                var match = HelmetLevel.Match(m.Get("MeshName").Str() ?? "");
                if (match.Success) hairLevel = Math.Max(hairLevel, match.Groups[1].Value[0] - '0');
            }
        }
        if (plan.HoodOn) hairLevel = Math.Max(hairLevel, HoodHairLevel);
        plan.HairLevel = hairLevel;

        // 후드·덮개 메시 하나를 켤지. 해당하지 않는 메시는 null(프리팹 값 유지).
        bool? CostumeVariantVisible(Def d, string name)
        {
            if (name.StartsWith(HelmetCoverOn, StringComparison.Ordinal)) return d == hatDef ? plan.HelmetCoverOn : (bool?)null;
            if (name.StartsWith(HelmetCoverOff, StringComparison.Ordinal)) return d == hatDef ? !plan.HelmetCoverOn : (bool?)null;
            if (!IsHoodMesh(name)) return null;
            bool on = d == hoodDef && plan.HoodOn;
            if (name.StartsWith(HoodOn, StringComparison.Ordinal)) return on;
            // 벗은 후드: 모자를 쓰면 눌린 후드(있으면), 아니면 보통 후드.
            bool pressed = hatShown && HasMesh(d, n => n.StartsWith(HoodOffPressed, StringComparison.Ordinal));
            if (name.StartsWith(HoodOffPressed, StringComparison.Ordinal)) return !on && pressed;
            return !on && !pressed;
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
        // 몸 자르기 재료: 몸 삼각형의 파일 순서·부위, 의상별 분리면 계획
        int[][] bodyTriSource = null;
        Dictionary<int, byte> bodySegments = null;
        var cutters = new List<Cutter>();
        // 무기 등 장착점(AttachPointInfo)이 있는 파츠: 프리팹 본을 복제한 루트. 몸 본 계층이 정해진 뒤 장착점 본 아래에 붙인다.
        var tools = new List<(Def d, Transform root)>();
        // 스킨 메시 목록이 빈 장착 파츠(곡괭이 4종 [확인])는 프리팹의 일반 메시(MeshRenderer)를 복제 본 아래에 그린다.
        var staticToolRenderers = new Dictionary<Def, List<GameObject>>();

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
            // 수염은 머리에 붙으므로 머리를 가리는 의상에서도 숨긴다.
            if ((part.Role == PartRole.Hair && hideHair) || (part.Role == PartRole.Head && hideHead) || (part.Role == PartRole.Eyes && hideEye) ||
                (part.Role == PartRole.Beard && (hideBeard || hideHead)) || (d == hatDef && !hatShown))
                partGo.SetActive(false);
            if (part.Role == PartRole.Costume && AccessorySlot(d) is int acc && accessoryHiders[acc].Exists(h => h != d))
            {
                partGo.SetActive(false);
                plan.HiddenAccessories.Add(part.Key);
            }
            int shownHairLevel = part.Role == PartRole.Hair ? ShownHairLevel(d, hairLevel) : -1;
            if (shownHairLevel >= 0 && shownHairLevel != hairLevel) L($"  헤어 단계 H{hairLevel} 메시가 없어 H{shownHairLevel} 표시: {part.Model}");

            string prefabPath = BundleCache.AssetRoot + d.Root.Get("AssetPathToLoad").Str();
            var prefab = bundles.LoadAsset<GameObject>(prefabPath);
            if (!prefab) { L($"  프리팹 없음: {prefabPath}"); Fail(part, "프리팹(재질·텍스처)을 읽지 못했습니다: " + prefabPath); }
            var prefabSmr = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
            if (prefab) foreach (var r in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (!prefabSmr.ContainsKey(r.name)) prefabSmr[r.name] = r;
            // 무기는 BoneTemplate 이 없고 메시 본(_Sword_Root 등)이 프리팹 안에만 있다. 프리팹 본을 복제해 이 파츠 전용 본으로 쓴다.
            bool tool = part.Role == PartRole.Costume && d.Root.Get("ModelPartInfo").Get("AttachPointInfo").Arr().Count > 0;
            Transform toolRootT = null;
            if (tool && prefab)
            {
                var toolRoot = toolRootT = new GameObject("@" + part.Model).transform;
                toolRoot.SetParent(root.transform, false);
                if (!partBones.TryGetValue(d, out var own)) partBones[d] = own = new Dictionary<string, Transform>(StringComparer.Ordinal);
                CloneBones(prefab.transform, toolRoot, own);
                tools.Add((d, toolRoot));
            }
            else if (tool) { L($"  장착 파츠 프리팹 없음, 본을 만들지 못함: {part.Model}"); Fail(part, "장착 파츠 프리팹이 없어 본을 만들지 못했습니다"); }
            Dictionary<string, SkinnedMeshRenderer> fbxSmr = null;
            var morph = part.Role == PartRole.Body ? MorphMaterials(d) : null;
            var unit = part.Role == PartRole.Body || (part.Role == PartRole.Costume && !tool) ? ReadClothingUnit(files, d) : null;
            var cutter = unit != null && part.Role == PartRole.Costume && unit.Separators.Count > 0 ? new Cutter { D = d, Unit = unit } : null;
            if (cutter != null) cutters.Add(cutter);

            var list = d.Root.Get("SkinnedMeshList").Arr();
            if (list.Count == 0 && !(tool && prefab && AddPrefabToolMeshes(d, prefab, toolRootT, partGo)))
                Fail(part, "모델의 메시 목록이 비어 있습니다");
            var byIndex = new Dictionary<int, Material[]>();
            for (int i = 0; i < list.Count; i++)
            {
                string name = list[i].Get("MeshName").Str() ?? "";
                bool helper = false;
                foreach (var h in HelperPrefix) if (name.StartsWith(h, StringComparison.Ordinal)) helper = true;
                string smm = $"{d.Dir}{part.Model}_{i}.SkinnedModularMesh.bytes";
                if (helper)
                {
                    // 분리면 메시는 그리지 않는다. 분리면을 기본 자세로 옮길 주 본만 기록한다.
                    if (cutter != null && name.StartsWith("Mesh_SP_", StringComparison.Ordinal) && files.HasResource(smm))
                    {
                        var sm = SmmReader.Read(files.ReadResource(smm), out var sb);
                        cutter.SeparatorBone[name.Substring("Mesh_SP_".Length)] = DominantBone(sm, sb);
                        UnityEngine.Object.DestroyImmediate(sm);
                    }
                    continue;
                }
                Mesh mesh; string[] boneNames; bool owned = false;
                prefabSmr.TryGetValue(name, out var psmr);
                if (files.HasResource(smm))
                {
                    mesh = SmmReader.Read(files.ReadResource(smm), out boneNames, out var triSource);
                    owned = true;
                    if (part.Role == PartRole.Body && bodySegments == null && unit != null && unit.Segments.TryGetValue(i, out var bseg))
                    {
                        bodyTriSource = triSource;
                        bodySegments = bseg;
                    }
                }
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
                if (cutter != null)
                {
                    var bps = mesh.bindposes;
                    for (int b = 0; b < boneNames.Length && b < bps.Length; b++)
                        if (!cutter.Bindposes.ContainsKey(boneNames[b])) cutter.Bindposes[boneNames[b]] = bps[b];
                }

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
                    // 드러난 피부를 의상 메시 안의 피부 조각으로 그리는 의상이 있다(예: 어깨 트인 상의의 가슴). 몸 메시의 그 자리는 잘린다.
                    if (part.Role == PartRole.Costume && src && src.shader && src.shader.name.EndsWith("/Skin_Body", StringComparison.Ordinal))
                        view.CostumeSkinMaterials.Add(subMats[s]);
                }
                smr.sharedMaterials = subMats;
                if (part.Key == AssemblyPlan.CostumeKey("Weapon") && !plan.WeaponIsTool)
                {
                    var origin = new Material[subMats.Length];
                    for (int s = 0; s < origin.Length; s++) origin[s] = srcMats.Length == 0 ? null : srcMats[Mathf.Min(s, srcMats.Length - 1)];
                    view.WeaponMeshes.Add((smr, origin));
                }

                // 프리팹의 켜짐·꺼짐을 그대로 따른다(예: 눈의 Mesh_EyeBasePlane 은 감은 눈 평면이라 기본으로 꺼져 있다).
                if (psmr) { go2.SetActive(psmr.gameObject.activeSelf); smr.enabled = psmr.enabled; }
                // 헤어 단계: 프리팹은 H0 만 켜 두고 H1~H5 를 꺼 둔다 [확인: 헤어 프리팹]. 고른 단계만 켜고 나머지는 끈다.
                var lv = HairLevel.Match(name);
                if (part.Role == PartRole.Hair && lv.Success) go2.SetActive(lv.Groups[1].Value[0] - '0' == shownHairLevel);
                // 후드·덮개: 프리팹은 쓴·벗은 메시를 모두 켜 둔다 [확인: 로브·원피스·모자 프리팹]. 상태에 맞는 것만 켠다.
                if (part.Role == PartRole.Costume && CostumeVariantVisible(d, name) is bool variant) go2.SetActive(variant);
                // 좌우 눈·눈썹 숨김. 표정 애니메이션이 게임 오브젝트를 켜고 끄므로 렌더러 컴포넌트를 끈다.
                if (part.Role == PartRole.Eyes) HideEyeSide(smr, name, hideLeftEye, hideRightEye, hideLeftBrow, hideRightBrow);
                view.Renderers.Add((part.Key, smr));
                byIndex[i] = subMats;
                if (part.Role == PartRole.Body && !view.Body) { view.Body = smr; view.BodyFullMesh = mesh; }
                if (part.Role == PartRole.Head) view.HeadRenderers.Add(smr);
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
        foreach (var d in defs) if (!IsTexturePart(d.Part.Role))
            try { AddModel(d); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { Fail(d.Part, "모델 조립 실패: " + e.Message); }
        // 모델 정의의 스킨 메시 목록이 빈 장착 파츠(곡괭이 4종 [확인])는 프리팹의 렌더러를 그대로 쓴다.
        //   SkinnedMeshRenderer: 메시가 프리팹 안에 있다. 본은 이름으로 복제 본에 연결하고 파츠 오브젝트 아래에 둔다(일반 경로와 같은 자리).
        //   MeshRenderer: 본 없는 SkinnedMeshRenderer 로 만들어 프리팹에서 부모였던 본(복제본) 아래에 같은 로컬 값으로 둔다.
        bool AddPrefabToolMeshes(Def d, GameObject prefab, Transform toolRoot, GameObject partGo)
        {
            if (!toolRoot || !partBones.TryGetValue(d, out var own)) return false;
            var made = new List<GameObject>();
            Material[] Mats(Material[] src, int count)
            {
                var mats = new Material[Mathf.Max(1, count)];
                for (int s2 = 0; s2 < mats.Length; s2++) mats[s2] = Convert(src.Length == 0 ? null : src[Mathf.Min(s2, src.Length - 1)], false, out _);
                return mats;
            }
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                GameObject go;
                SkinnedMeshRenderer smr;
                if (r is SkinnedMeshRenderer psmr)
                {
                    if (!psmr.sharedMesh) continue;
                    go = new GameObject(psmr.name);
                    go.transform.SetParent(partGo.transform, false);
                    smr = go.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = psmr.sharedMesh;
                    var names = BoneNames(psmr);
                    var bt = new Transform[names.Length];
                    for (int b = 0; b < bt.Length; b++) bt[b] = BoneFor(d, names[b]) ?? toolRoot;
                    smr.bones = bt;
                    smr.rootBone = bt.Length > 0 ? bt[0] : toolRoot;
                    smr.sharedMaterials = Mats(psmr.sharedMaterials, psmr.sharedMesh.subMeshCount);
                }
                else if (r is MeshRenderer mr && mr.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh)
                {
                    var parentT = mr.transform.parent && own.TryGetValue(mr.transform.parent.name, out var pb) ? pb : toolRoot;
                    go = new GameObject(mr.name);
                    go.transform.SetParent(parentT, false);
                    go.transform.localPosition = mr.transform.localPosition;
                    go.transform.localRotation = mr.transform.localRotation;
                    go.transform.localScale = mr.transform.localScale;
                    smr = go.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = mf.sharedMesh;
                    smr.sharedMaterials = Mats(mr.sharedMaterials, mf.sharedMesh.subMeshCount);
                    staticToolRenderers.TryGetValue(d, out var st);
                    if (st == null) staticToolRenderers[d] = st = new List<GameObject>();
                    st.Add(go);
                }
                else continue;
                smr.updateWhenOffscreen = true;
                smr.receiveShadows = false;
                go.SetActive(r.gameObject.activeSelf);
                smr.enabled = r.enabled;
                view.Renderers.Add((d.Part.Key, smr));
                made.Add(go);
            }
            if (made.Count == 0) return false;
            L($"  프리팹 메시 장착 파츠: {d.Part.Model} 메시 {made.Count}개(모델 정의 메시 목록 없음)");
            return true;
        }
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
                // 도구 중 넣은 장착점이 캐릭터 루트인 것(피아노·하프·큰북 등)은 들고 다니지 않으므로 넣은 상태에서 숨긴다(Equipment.md "도구").
                bool rootTool = !drawn && plan.WeaponIsTool && d.Part.Key == AssemblyPlan.CostumeKey("Weapon") && (to == "#MODEL_ROOT" || string.IsNullOrEmpty(to));
                var pl = new ToolMount.Placement
                {
                    Valid = true, Parent = parent,
                    Hidden = ap.Get("ModelPartVisibleState").Str() == "AlwaysInvisible" || rootTool,
                    Position = pick != null ? Vec(pick.Get("Position"), Vector3.zero) : Vector3.zero,
                    Rotation = pick != null ? Quaternion.Euler(Vec(pick.Get("Rotation"), Vector3.zero)) : Quaternion.identity,
                    Scale = pick != null ? Vec(pick.Get("LocalScale"), Vector3.one) : Vector3.one,
                };
                if (drawn) m.Drawn = pl; else m.Undrawn = pl;
            }
            string partName = d.Part.Key + ":" + d.Part.Model;
            foreach (var (_, r) in view.Renderers)
                if (r && r.transform.parent && r.transform.parent.name == partName) m.Renderers.Add((r.gameObject, r.gameObject.activeSelf));
            if (staticToolRenderers.TryGetValue(d, out var statics)) foreach (var go in statics) m.Renderers.Add((go, go.activeSelf));
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
        foreach (var d in defs) if (d.Part.Role == PartRole.FaceDeco || d.Part.Role == PartRole.BodyDeco)
        {
            string host = d.Part.Role == PartRole.FaceDeco ? "head" : "body";
            if (plan.Issues.Exists(i => i.Key == host && i.FromBuild && i.IsFailure)) continue;
            try { if (!ApplyTattoo(bundles, d.Root, view, host, L)) Fail(d.Part, (host == "head" ? "얼굴" : "몸") + " 꾸밈 텍스처를 적용하지 못했습니다"); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { Fail(d.Part, "꾸밈 텍스처 읽기 실패: " + e.Message); }
        }
        // 실패한 임시 조립본으로 몸 절단·애니메이션을 확정하지 않는다. 호출자가 제외·대체 후 다시 조립한다.
        if (plan.Issues.Exists(i => i.FromBuild && i.IsFailure)) return view;
        ApplyFacialAnimation(files, plan, view, L);
        view.ApplyBlendShape(plan.BlendShape);
        view.ApplyPlanColors(plan);
        // 몸 가리기는 기본 자세에서 계산한다.
        view.SetPosed(false);
        if (view.Body && view.BodyFullMesh && cutters.Count > 0)
        {
            if (bodySegments == null) L("몸 자르기: 몸 부위 데이터(.ClothingUnit)가 없어 모든 몸 삼각형을 몸통으로 봅니다");
            var plans = new List<BodyCut.Plan>();
            foreach (var c in cutters)
            {
                var cd = c.D;
                plans.Add(BodyCut.MakePlan(cd.Part.Key, c.Unit, BodyCut.CutCaseForBody(c.Unit.Slot), id => SeparatorOverride(cd, plan.BodyType, id), sep =>
                {
                    // 분리면 메시의 주 본, 없으면 관절 기본 본. 그 본의 (월드 × 바인드포즈)로 의상 바인드 공간 → 기본 자세 월드.
                    if (!c.SeparatorBone.TryGetValue(sep.Id, out var bone)) bone = JunctionBone(sep.Junction);
                    var t = bone != null ? BoneFor(cd, bone) : null;
                    if (t && bone != null && c.Bindposes.TryGetValue(bone, out var bp)) return t.localToWorldMatrix * bp;
                    L($"  분리면 {cd.Part.Model}/{sep.Id}: 본 {bone ?? "-"}의 바인드포즈를 찾지 못해 좌표를 그대로 씁니다");
                    return root.transform.localToWorldMatrix;
                }));
            }
            var bm = new Mesh();
            view.Body.BakeMesh(bm, true);
            var bw = bm.vertices;
            UnityEngine.Object.DestroyImmediate(bm);
            var b2w = view.Body.transform.localToWorldMatrix;
            for (int v = 0; v < bw.Length; v++) bw[v] = b2w.MultiplyPoint3x4(bw[v]);
            view.BodyCutMesh = BodyCut.Cut(view.BodyFullMesh, bw, bodyTriSource, bodySegments, plans, L);
            if (view.BodyCutMesh) view.Owned.Add(view.BodyCutMesh);
        }
        view.SetBodyCut(true);
        view.SetPosed(true);
        AttachIdle(files, bundles, plan, view, bones, map, poseBase, bind, rawComY, mounts, L);
        CheckDyes(plan, view);
        view.CharacterShader = shader;
        if (view.WeaponMeshes.Count > 0) EnsureWeaponTexture(bundles, L);
        view.ApplyWeaponAura(plan.WeaponAura);
        if (plan.WeaponAura != null)
        {
            var shaders = new List<string>();
            foreach (var (_, src) in view.WeaponMeshes)
                foreach (var m in src) shaders.Add(m && m.shader ? $"{m.shader.name}(외곽선 끔 {(m.HasProperty("_OutlineOff") ? m.GetFloat("_OutlineOff") : -1)})" : "-");
            L($"무기 오라: {plan.WeaponAura.Name}({plan.WeaponAura.Id}), 무기 메시 {view.WeaponMeshes.Count}, 외곽선 서브메시 {view.WeaponOutlineCount}, " +
              $"텍스처 {(Shader.GetGlobalTexture(WeaponTextureId) ? Shader.GetGlobalTexture(WeaponTextureId).name : "없음")}, 원본 [{string.Join(", ", shaders)}]");
        }
        AttachSetEffect(files, bundles, plan, view, shader, L);
        AttachLookAt(plan, view, defs.Find(x => x.Part.Role == PartRole.Body), L);
        AttachToolEffects(files, bundles, plan, view, shader, L);
        // 의상 자체 연출: 실제로 보이는 의상 모델만. 후드로 숨긴 모자·다른 의상이 숨긴 장식은 뺀다. 후드 씀 연출은 쓴 후드 의상만.
        var shownCostumes = new List<(Def d, bool hoodOn)>();
        foreach (var d in defs)
        {
            if (d.Part.Role != PartRole.Costume) continue;
            if (d == hatDef && !hatShown) continue;
            if (plan.HiddenAccessories.Contains(d.Part.Key)) continue;
            shownCostumes.Add((d, d == hoodDef && plan.HoodOn));
        }
        AttachCostumeEffects(files, bundles, plan, view, shader, shownCostumes, L);
        L($"조립: 파츠 {defs.Count}, 본 {nb}(포즈 {posed}), 렌더러 {view.Renderers.Count}, 염색 연결 {view.Dyes.Count}, 헤어 단계 H{hairLevel}, 번들 {bundles.Count}, {sw.ElapsedMilliseconds}ms");
        if (plan.HiddenAccessories.Count > 0) L("장식 숨김: " + string.Join(", ", plan.HiddenAccessories));
        if (hideLeftEye || hideRightEye || hideLeftBrow || hideRightBrow)
            L($"눈 숨김: 왼눈 {hideLeftEye}, 오른눈 {hideRightEye}, 왼눈썹 {hideLeftBrow}, 오른눈썹 {hideRightBrow}");
        if (plan.HasHood || hatDef != null)
            L($"후드·덮개: 후드 {(hoodDef != null ? hoodDef.Part.Model + (plan.HoodOn ? " 씀" : " 벗음") : "없음")}, 모자 {(hatDef == null ? "없음" : hatShown ? "표시" : "후드로 숨김")}, 덮개 {(plan.HasHelmetCover ? (plan.HelmetCoverOn ? "내림" : "올림") : "없음")}");
        return view;
    }

    /// <summary>무기 외곽선 노이즈(전역 _Global_Weapon_Texture, EnvironmentConfig 값과 같은 텍스처). 배경과 관계없이 한 번 넣는다.</summary>
    internal const string WeaponTexturePath = "Shaders/Texture/GlobalTexture2.tga";
    static readonly int WeaponTextureId = Shader.PropertyToID("_Global_Weapon_Texture");

    internal static void EnsureWeaponTexture(BundleCache bundles, Action<string> L)
    {
        if (Shader.GetGlobalTexture(WeaponTextureId)) return;
        Texture2D tex = null;
        try { tex = bundles.LoadAsset<Texture2D>(BundleCache.AssetRoot + WeaponTexturePath); }
        catch (Exception e) when (!(e is OutOfMemoryException)) { L?.Invoke("무기 외곽선 텍스처 읽기 실패: " + e.Message); }
        if (tex) Shader.SetGlobalTexture(WeaponTextureId, tex);
        else L?.Invoke("무기 외곽선 텍스처 없음: " + WeaponTexturePath);
    }

    /// <summary>
    /// 카메라 쳐다보기(StudioFeatureReview.md). 몸 모델의 LookAtGroupName(플레이어는 Humanoid)으로 시선 설정을 골라 붙인다.
    /// 켜고 끄기는 조립과 관계없이 view.Plan.LookAtCamera 로 정하므로 쓸 수 있으면 항상 붙인다. 쓸 수 없으면 이유를 계획에 남긴다.
    /// </summary>
    static void AttachLookAt(AssemblyPlan plan, CharacterView view, Def body, Action<string> L)
    {
        var motion = view.GetComponent<CharacterMotion>();
        string group = body?.Root.Get("ActorModelInfo").Get("LookAtGroupName").Str();
        if (!motion) { plan.LookAtUnavailable = "기본 모션이 없어 시선을 보정할 수 없습니다"; L("시선: " + plan.LookAtUnavailable); return; }
        var la = CharacterLookAt.Attach(view, plan.LookAtTable, group, out string why);
        if (la == null) { plan.LookAtUnavailable = why; L("시선: " + why); return; }
        motion.LookAt = la;
        L($"시선: 그룹 {group}, {la.Describe()}, {(plan.LookAtCamera ? "켬" : "끔")}");
    }

    /// <summary>
    /// 의상 자체 연출(Equipment.md "의상 자체 연출"): StageObjectModelTimeline 의 반복 타임라인(후드를 쓴 의상은 후드 씀 타임라인도)을 계속 재생한다.
    /// 설치본 의상 타임라인 45개는 이펙트·덧그림 트랙만 있다 [확인]. 이펙트는 캐릭터 본(_Head 등)에, 덧그림은 의상 메시 이름에 붙는다.
    /// 끝 타임라인(End)은 쓰지 않는다. 스텔라그램 타임라인(껌 의상 3개)은 쓰는 상황을 몰라 쓰지 않는다 [미확인]. 읽지 못하면 연출만 뺀다.
    /// </summary>
    static void AttachCostumeEffects(InstallFiles files, BundleCache bundles, AssemblyPlan plan, CharacterView view, Shader shader,
        List<(Def d, bool hoodOn)> shown, Action<string> L)
    {
        if (plan.ModelTimelines == null || plan.ModelTimelines.Count == 0) return;
        var names = new List<string>();
        foreach (var (d, hoodOn) in shown)
        {
            if (!plan.ModelTimelines.TryGetValue(d.Part.Model, out var row)) continue;
            void Add(string n)
            {
                if (string.IsNullOrEmpty(n)) return;
                plan.CostumeEffects.Add((d.Part.Key, n));
                if (!names.Contains(n)) names.Add(n);
            }
            Add(row.Loop);
            if (hoodOn) Add(row.HoodOn);
        }
        if (names.Count == 0) return;
        try
        {
            var m = SocialMotionLoader.LoadTimelines(files, bundles, view, shader, names, L);
            if (m.Effects.Count + m.Additives.Count == 0) { m.Dispose(); L($"의상 연출: 이펙트 없음 [{string.Join(", ", names)}]"); return; }
            var p = view.gameObject.AddComponent<TimelineEffectPlayer>();
            p.Play(m);
            view.CostumeEffects = p;
            L($"의상 연출: [{string.Join(", ", names)}] 이펙트 {m.Effects.Count}, 덧그림 {m.Additives.Count}");
        }
        catch (Exception e) when (!(e is OutOfMemoryException)) { L("의상 연출 읽기 실패: " + e.Message); }
    }

    /// <summary>
    /// 도구 사용 연출(Equipment.md "도구"): 연주·채집 동작의 스테이지 타임라인 이펙트·덧그림을 모션 시간에 맞춰 재생한다.
    ///   시작 타임라인(악기 음표 FX_Song_Play_Loop 등)은 사용을 시작한 때부터 흐르는 시간, 반복 클립 타임라인은 반복 구간 안 위치.
    ///   소셜 모션 중에는 끈다(행동이 도구를 넣는다). 소리·소품(채집 대상)·손 IK 는 쓰지 않는다. 읽지 못하면 연출만 뺀다.
    /// </summary>
    static void AttachToolEffects(InstallFiles files, BundleCache bundles, AssemblyPlan plan, CharacterView view, Shader shader, Action<string> L)
    {
        if (!plan.WeaponIsTool || !plan.WeaponDrawn) return;
        if (plan.ToolStartTimelines.Count + plan.ToolLoopTimelines.Count == 0) { L("도구 연출: 타임라인 없음"); return; }
        var motion = view.GetComponent<CharacterMotion>();
        if (!motion) return;
        void Add(List<string> names, bool loop)
        {
            if (names.Count == 0) return;
            try
            {
                var m = SocialMotionLoader.LoadTimelines(files, bundles, view, shader, names, L);
                if (m.Effects.Count + m.Additives.Count == 0) { m.Dispose(); L($"도구 연출({(loop ? "반복" : "시작")}): 이펙트 없음 [{string.Join(", ", names)}]"); return; }
                var p = view.gameObject.AddComponent<TimelineEffectPlayer>();
                p.Suspend = () => motion.CurrentAction != null;
                if (loop) p.Clock = () => motion.PlayTime < motion.IntroLength ? -1 : motion.PlayTime - motion.IntroLength;
                p.Play(m);
                view.ToolEffects.Add(p);
                L($"도구 연출({(loop ? "반복" : "시작")}): [{string.Join(", ", names)}] 이펙트 {m.Effects.Count}, 덧그림 {m.Additives.Count}");
            }
            catch (Exception e) when (!(e is OutOfMemoryException)) { L("도구 연출 읽기 실패: " + e.Message); }
        }
        Add(plan.ToolStartTimelines, false);
        Add(plan.ToolLoopTimelines, true);
    }

    /// <summary>
    /// 세트 활성 연출(Equipment.md "세트 효과"): 판정한 세트의 스테이지 타임라인을 캐릭터에 계속 재생한다.
    /// 읽지 못하면 연출만 빼고 조립은 그대로 둔다(로그만 남긴다).
    /// </summary>
    static void AttachSetEffect(InstallFiles files, BundleCache bundles, AssemblyPlan plan, CharacterView view, Shader shader, Action<string> L)
    {
        if (plan.SetEffect == null) return;
        L($"세트 효과: {plan.SetEffect.DisplayName}({plan.SetEffect.Id}), 연출 {plan.SetEffectTimeline ?? "없음"}");
        if (plan.SetEffectTimeline == null) return;
        try
        {
            var m = SocialMotionLoader.LoadTimelines(files, bundles, view, shader, new[] { plan.SetEffectTimeline }, L);
            if (m.Effects.Count + m.Additives.Count == 0) { m.Dispose(); return; }
            view.SetEffect = view.gameObject.AddComponent<TimelineEffectPlayer>();
            view.SetEffect.Play(m);
        }
        catch (Exception e) when (!(e is OutOfMemoryException)) { L("세트 연출 읽기 실패: " + e.Message); }
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
        // 무기 등급 외곽선 패스는 외곽선 전용 재질(CharacterView.ApplyWeaponAura)에서만 그린다.
        c.SetShaderPassEnabled("SRPDefaultUnlit", false);
        c.SetShaderPassEnabled("WeaponGradeOutline", false);
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
            // 후드·덮개 결과도 조립 때 정해진다(색 편집은 다시 조립하지 않고 요청 값도 바꾸지 않는다).
            to.HasHood = from.HasHood; to.HoodOn = from.HoodOn; to.HatHiddenByHood = from.HatHiddenByHood;
            to.HasHelmetCover = from.HasHelmetCover; to.HelmetCoverOn = from.HelmetCoverOn; to.HairLevel = from.HairLevel;
            to.LookAtUnavailable = from.LookAtUnavailable;
            to.HiddenAccessories.Clear(); to.HiddenAccessories.AddRange(from.HiddenAccessories);
            to.CostumeEffects.Clear(); to.CostumeEffects.AddRange(from.CostumeEffects);
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

    static bool IsTexturePart(PartRole role) => role == PartRole.Mouth || role == PartRole.FaceDeco || role == PartRole.BodyDeco;

    /// <summary>
    /// 얼굴꾸밈·몸꾸밈(텍스처 모델)을 얼굴·몸 머티리얼의 문신 칸에 건다. 입과 같은 대응:
    ///   _TattooMap = AssetPathToLoad 텍스처, _TattooMapScalePosition = TextureInfo.BaseTexcoord,
    ///   _TattooMapRotation = TextureInfo.Rotation(도), _TattooPushStrength = TextureInfo.PushStrength(얼굴만)
    ///   _IsScarMode = AppearanceSlot 이 FaceScar·BodyScar(홍조·점·흉터)면 1, FaceTattoo·BodyTattoo 면 0
    /// 얼굴은 게임 원본 셰이더(Skin_Face)가, 몸은 앱 셰이더(MobiAtelier/Character)가 합성한다.
    /// 몸 합성식은 원본 Skin_Body 픽셀 셰이더를 따라 옮겼다(MobiCharacter.shader 주석).
    /// </summary>
    static bool ApplyTattoo(BundleCache bundles, object def, CharacterView view, string host, Action<string> log)
    {
        string path = BundleCache.AssetRoot + def.Get("AssetPathToLoad").Str();
        var tex = bundles.LoadAsset<Texture2D>(path);
        if (!tex) { log?.Invoke("꾸밈 텍스처 없음: " + path); return false; }
        var info = def.Get("TextureInfo");
        var st = info.Get("BaseTexcoord").Arr();
        var scalePos = st.Count >= 4 ? new Vector4(st[0].F(), st[1].F(), st[2].F(), st[3].F()) : new Vector4(1, 1, 0, 0);
        string slot = def.Get("ModelPartInfo").Get("AppearanceSlot").Str();
        bool scar = slot == "FaceScar" || slot == "BodyScar";
        string emPath = info.Get("TattooEmissionTextureAssetPathToLoad").Str();
        var emission = string.IsNullOrEmpty(emPath) ? null : bundles.LoadAsset<Texture2D>(BundleCache.AssetRoot + emPath);
        if (!string.IsNullOrEmpty(emPath) && !emission) log?.Invoke("꾸밈 발광 텍스처 없음: " + emPath);
        int applied = 0, costumeSkin = 0;
        var targets = new List<Material>();
        foreach (var (key, r) in view.Renderers)
            if (key == host && r) targets.AddRange(r.sharedMaterials);
        // 몸꾸밈은 의상 안의 피부 조각(Skin_Body)에도 건다. 몸과 같은 몸 UV 다 [확인: 스트리트 고스 캣 웨어 가슴 촬영].
        if (host == "body")
            foreach (var m in view.CostumeSkinMaterials)
                if (m && !targets.Contains(m)) { targets.Add(m); costumeSkin++; }
        foreach (var m in targets)
        {
            if (!m || !m.HasProperty("_TattooMap")) continue;
            m.SetTexture("_TattooMap", tex);
            m.SetVector("_TattooMapScalePosition", scalePos);
            m.SetFloat("_TattooMapRotation", info.Get("Rotation").F());
            if (m.HasProperty("_TattooPushStrength")) m.SetFloat("_TattooPushStrength", info.Get("PushStrength").F());
            if (m.HasProperty("_IsScarMode")) m.SetFloat("_IsScarMode", scar ? 1 : 0);
            if (m.HasProperty("_TattooOn")) m.SetFloat("_TattooOn", 1);
            // 발광·반사(TextureInfo). 얼굴은 원본 셰이더, 몸은 앱 셰이더(원본 식)가 같은 이름으로 받는다. 발광맵이 없으면 발광 0.
            if (m.HasProperty("_TattooEmissionMap"))
            {
                m.SetTexture("_TattooEmissionMap", emission ? emission : Texture2D.blackTexture);
                m.SetFloat("_TattooEmissionIntensity", emission ? info.Get("TattooEmissionIntensity").F() : 0f);
            }
            if (m.HasProperty("_IsEnableTattooEmissionAtNight")) m.SetFloat("_IsEnableTattooEmissionAtNight", info.Get("IsEnableTattooEmissionAtNight").F());
            if (m.HasProperty("_TattooSpecula")) m.SetFloat("_TattooSpecula", info.Get("TattooSpecula").F());
            if (m.HasProperty("_TattooSpeculaIntensity")) m.SetFloat("_TattooSpeculaIntensity", info.Get("TattooSpeculaIntensity").F());
            applied++;
        }
        log?.Invoke($"꾸밈({host}): {tex.name} {slot}, 크기·위치 {scalePos}, 발광 {(emission ? emission.name + " × " + info.Get("TattooEmissionIntensity").F() : "없음")}, 반사 {info.Get("TattooSpecula").F()}/{info.Get("TattooSpeculaIntensity").F()}, 머티리얼 {applied}개(의상 피부 {costumeSkin}개)" + (applied == 0 ? " (_TattooMap 없음)" : ""));
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

    /// <summary>몸을 자르는 의상: 분리면 데이터, 분리면 Id → 분리면 메시의 주 본, 의상 메시의 본 이름 → 바인드포즈.</summary>
    sealed class Cutter
    {
        public Def D;
        public ClothingUnit Unit;
        public readonly Dictionary<string, string> SeparatorBone = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, Matrix4x4> Bindposes = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
    }

    static ClothingUnit ReadClothingUnit(InstallFiles files, Def d)
    {
        string path = d.Dir + d.Part.Model + ".ClothingUnit.bytes";
        if (!files.HasResource(path)) return null;
        return ClothingUnit.Read(files.ReadResource(path));
    }

    /// <summary>분리면 보정(SeparatorOverrideInfo). 체형별 보정이 먼저, 없으면 모델 공통 보정. 없으면 null.</summary>
    static object SeparatorOverride(Def d, string bodyType, string id)
    {
        var ma = d.Root.Get("MorphableAssets");
        if (bodyType != "Default")
            foreach (var bt in ma.Get("SeparatorOverridesByBodyType").Arr())
            {
                var key = bt.Get("BodyType").Arr();
                if (key.Count >= 2 && key[1].Str() == bodyType && bt.Get("SeparatorOverrides").Get(id) is object o) return o;
            }
        return ma.Get("SeparatorOverrides").Get(id);
    }

    /// <summary>가중치 합이 가장 큰 본 이름.</summary>
    static string DominantBone(Mesh m, string[] names)
    {
        var sum = new float[names.Length];
        foreach (var w in m.boneWeights)
        {
            if (w.boneIndex0 < sum.Length) sum[w.boneIndex0] += w.weight0;
            if (w.boneIndex1 < sum.Length) sum[w.boneIndex1] += w.weight1;
            if (w.boneIndex2 < sum.Length) sum[w.boneIndex2] += w.weight2;
            if (w.boneIndex3 < sum.Length) sum[w.boneIndex3] += w.weight3;
        }
        int best = -1;
        for (int i = 0; i < sum.Length; i++) if (best < 0 || sum[i] > sum[best]) best = i;
        return best >= 0 ? names[best] : null;
    }

    /// <summary>분리면 메시가 없을 때 쓰는 관절(FittingJunctionType)별 본.</summary>
    static string JunctionBone(int junction)
    {
        switch (junction)
        {
            case 1: return "_L Thigh";
            case 2: return "_R Thigh";
            case 3: return "_L Forearm";
            case 4: return "_R Forearm";
            case 8: return "_Neck";
            default: return "_Pelvis";
        }
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
