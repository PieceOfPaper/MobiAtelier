// 장면 상태(EditState) + 시트(GameData) → 조립할 모델과 색의 목록.
// 선택과 실제 출력을 분리한다(Equipment.md). 입을 수 없는 의상은 선택을 유지한 채 상태만 표시한다.
// 공개 범위(Disclosure)는 여기서 최종으로 강제한다. 목록·가져오기·자동 복원·실행 취소 어느 경로로 온 상태든
// 공개 빌드에서는 숨김 데이터·직접 리소스·미공개 의상을 조립 목록에 넣지 않는다(선택 ID·염색은 상태에 그대로 둔다).
// 필수 외형(머리·헤어·눈·입·피부/헤어/눈 색)은 표시할 수 없으면 공개된 호환 기본값으로 대신 표시하고 원래 선택은 보관한다(Appearance.md).
// 기본값도 준비하지 못하면 PlanException 을 던진다. 호출자는 이전 장면·상태·이력을 유지한다.
using System;
using System.Collections.Generic;
using UnityEngine;

public enum PartRole { Body, Head, Hair, Eyes, Mouth, Costume }

public enum CostumeStatus
{
    Applied,
    /// <summary>다른 착용 의상의 HidingParts 로 가려짐</summary>
    HiddenByOther,
    /// <summary>현재 성별·나이에 맞는 모델이 없음</summary>
    NoModelForAppearance,
    /// <summary>착용 가능 성별이 다름</summary>
    GenderMismatch,
    /// <summary>시트에 없는 ID</summary>
    UnknownId,
    /// <summary>모델 정의를 설치 폴더에서 찾지 못함</summary>
    MissingResource,
    /// <summary>숨김·미공개·직접 리소스라 공개 빌드에서 표시하지 않음(에디터 전용)</summary>
    Restricted,
}

public enum IssueKind
{
    /// <summary>데이터에 없는 ID</summary>
    UnknownId,
    /// <summary>공개 빌드에서 제한한 항목(숨김 데이터·미공개 의상·직접 리소스)</summary>
    Restricted,
    /// <summary>설치 폴더의 리소스를 읽지 못함(모델 정의·프리팹·메시). 정상적인 착용 불가와 다르다.</summary>
    MissingResource,
    /// <summary>현재 성별·나이·체형·얼굴형에 맞지 않음(정상 상태, 복원 안내 대상 아님)</summary>
    Incompatible,
    /// <summary>저장된 염색값을 적용할 파트가 없거나 값을 해석하지 못함(값은 보존)</summary>
    DyeUnresolved,
}

/// <summary>조립 계획·조립 결과에서 찾은 문제 하나. 상태 값은 지우지 않는다.</summary>
public sealed class PlanIssue
{
    /// <summary>파츠 키(head, hair, costume:Top, skinColor ...)</summary>
    public string Key;
    public string Label, Id, Name, Message;
    public IssueKind Kind;
    /// <summary>조립기(CharacterAssembler)가 조립 중에 찾은 문제(메시 누락·염색 파트 없음)</summary>
    public bool FromBuild;

    /// <summary>복원·가져오기에서 부분 복원 승인을 받아야 하는 문제. 정상적인 호환 불가는 제외한다.</summary>
    public bool NeedsConsent => Kind != IssueKind.Incompatible;
    /// <summary>리소스 읽기 실패. 일반 편집이 새로 만들면 편집을 확정하지 않는다.</summary>
    public bool IsFailure => Kind == IssueKind.MissingResource;
    public string Describe() => $"{Label} {ItemLabel.Of(Id, Name)}: {Message}";
    public bool Same(PlanIssue o) => o != null && o.Kind == Kind && o.Key == Key && o.Id == Id && o.Message == Message;
}

/// <summary>필수 외형을 공개 기본값으로도 준비하지 못함. 편집·복원을 확정하지 않는다.</summary>
public sealed class PlanException : Exception
{
    public PlanException(string message) : base(message) { }
}

public sealed class PartPlan
{
    /// <summary>body, head, hair, eyes, mouth, costume:&lt;부위&gt;</summary>
    public string Key;
    public PartRole Role;
    public string Model;
    /// <summary>이 파츠만의 슬롯 색(Slot0~5). 시트 기본색 위에 사용자 색을 덮은 결과.</summary>
    public readonly Dictionary<string, Color> SlotColors = new Dictionary<string, Color>(StringComparer.Ordinal);
    /// <summary>사용자가 지정한 염색 슬롯. 조립 뒤 모델에 그 파트가 있는지 확인한다.</summary>
    public readonly HashSet<string> UserSlots = new HashSet<string>(StringComparer.Ordinal);
}

public sealed class CostumeResult
{
    public string Part, CostumeId, DisplayName, Detail;
    public CostumeStatus Status;
    public List<string> Models = new List<string>();
}

public sealed class AssemblyPlan
{
    public readonly List<PartPlan> Parts = new List<PartPlan>();
    /// <summary>외형 색 슬롯(Skin, Hair, Hair1, Eye, Mouth). 모든 파츠의 같은 이름 슬롯에 적용한다.</summary>
    public readonly Dictionary<string, Color> GlobalColors = new Dictionary<string, Color>(StringComparer.Ordinal);
    public readonly List<CostumeResult> Costumes = new List<CostumeResult>();
    public readonly List<string> Notes = new List<string>();
    /// <summary>복원 안내·편집 거절·패널 표시에 쓰는 문제 목록(계획 + 조립 결과).</summary>
    public readonly List<PlanIssue> Issues = new List<PlanIssue>();
    /// <summary>필수 외형의 대체 표시(원래 선택 → 공개 기본값).</summary>
    public readonly List<AppearanceSubstitute> Substitutes = new List<AppearanceSubstitute>();

    public AppearanceSubstitute Substitute(string key) => Substitutes.Find(x => x.part == key);

    public void AddIssue(string key, IssueKind kind, string id, string name, string message, bool fromBuild = false)
    {
        var i = new PlanIssue { Key = key, Label = PartLabel(key), Kind = kind, Id = id, Name = name, Message = message, FromBuild = fromBuild };
        if (!Issues.Exists(x => x.Same(i))) Issues.Add(i);
    }

    static readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        { "body", "몸" }, { "head", "얼굴형" }, { "hair", "헤어스타일" }, { "eyes", "눈 모양" }, { "mouth", "입 모양" },
        { "skinColor", "피부색" }, { "hairColor", "헤어 색상" }, { "eyeColor", "눈 색상" },
        { "Hat", "모자" }, { "Top", "상의" }, { "Bottom", "하의" }, { "Robe", "로브" }, { "Gloves", "장갑" },
        { "Shoes", "신발" }, { "EarDeco", "귀장식" }, { "EyeDeco", "눈장식" }, { "FaceDeco", "얼굴장식" }, { "Weapon", "무기" },
    };

    /// <summary>파츠 키(costume:Top 포함)·부위 이름 → 화면 이름.</summary>
    public static string PartLabel(string key)
    {
        if (key == null) return "";
        if (key.StartsWith("costume:", StringComparison.Ordinal)) key = key.Substring("costume:".Length);
        return Labels.TryGetValue(key, out var l) ? l : key;
    }
    public bool Male, Infant;
    /// <summary>나이와 키 조절값(-1 작게 ~ 0 보통 ~ 1 크게). 몸 .bdc 의 본 스케일·위치를 고른다.</summary>
    public int Age;
    public float HeightModifier;
    public string BodyType = "Default";
    /// <summary>체형 블렌드셰이프 이름. null 이면 기본 체형.</summary>
    public string BlendShape;
    public string Pose;
    /// <summary>기본 아이들 모션(커스터마이즈 서 있기) 클립이 든 .fbx 에셋 경로. null 이면 정지 포즈만 쓴다.</summary>
    public string Idle;
    /// <summary>얼굴(_Facial) 클립을 가져올 꾸미기 아이들 경로. Idle 이 직업 자세 클립이면 Idle 과 다르다.</summary>
    public string FaceIdle;
    /// <summary>무기를 꺼낸 자세로 조립한다(장착점 Drawn). 무기를 착용하지 않았거나 자세 클립이 없으면 false.</summary>
    public bool WeaponDrawn;
    /// <summary>자세에 쓴 직업(CombatJob.Id). null 이면 공통 자세.</summary>
    public string StanceJob;
    /// <summary>자세 클립을 고른 AnimationStateSet·Action 조건(진단용).</summary>
    public string StanceCondition;
    /// <summary>반복 전에 한 번 재생할 도입 클립(무기 꺼내기·넣기, 꾸미기 포즈 시작). null 이면 없다.</summary>
    public string Intro;
    /// <summary>도입 중 무기 상태 기본값. 꺼내기·포즈는 꺼냄, 넣기는 넣음이다.</summary>
    public bool IntroDrawn;
    /// <summary>도입 중 무기 상태 예외 구간(스테이지 타임라인 ToolState 트랙): 파츠 종류, 꺼냄 여부, 시작·끝 초.</summary>
    public readonly List<(string toolType, bool drawn, float begin, float end)> IntroToolSpans = new List<(string, bool, float, float)>();
    /// <summary>파츠 자체 클립 찾기(파츠 애니메이션 그룹, 몸 클립 상대 경로 → 파츠 클립 상대 경로). null 이면 쓰지 않는다.</summary>
    public Func<string, string, string> PartClip;
    /// <summary>현재 무기 외형 ID(적용된 경우). 전환 판정에 쓴다.</summary>
    public string WeaponId;
    /// <summary>입은 의상의 옷 모양 조건(ModelPartInfo.ClothingCondition: ShortSkirt·LongSkirt 등). 조립할 때 채운다. 없으면 null.</summary>
    public string Clothing;

    public static string CostumeKey(string part) => "costume:" + part;
}

public static class PlanResolver
{
    /// <summary>성인 몸 .bdc 는 14~17세, 어린 몸(Infant*) .bdc 는 10~13세 데이터만 있다[확인]. 14세 미만을 어린 모델로 본다.</summary>
    public const int AdultMinAge = 14;

    public static Color ToColor(Rgba32 c) => new Color32(c.R, c.G, c.B, 255);

    /// <param name="previous">직전 장면의 계획. 주면 같은 무기의 넣음↔꺼냄 변경에 꺼내기·넣기 도입 클립을 붙인다.</param>
    public static AssemblyPlan Resolve(EditState state, GameData data, InstallFiles files, AssemblyPlan previous = null,
        HashSet<string> failedModels = null)
    {
        var plan = new AssemblyPlan();
        var a = state.appearance ?? new AppearanceState();
        plan.Male = string.Equals(a.gender, "Male", StringComparison.OrdinalIgnoreCase);
        plan.Infant = a.age > 0 && a.age < AdultMinAge;
        plan.Age = a.age;
        plan.HeightModifier = a.heightModifier;
        plan.BodyType = string.IsNullOrEmpty(a.bodyType) ? "Default" : a.bodyType;
        if (!data.BodyTypes.ContainsKey(plan.BodyType)) plan.BodyType = "Default";
        if (data.BodyTypes.TryGetValue(plan.BodyType, out var bt) && bt.UseBlendshape) plan.BlendShape = bt.BlendshapeName;

        string gender = plan.Male ? "Male" : "Female";
        string body = plan.Infant ? $"Infant{gender}_Body_00" : $"{gender}_Body_00";
        if (files.ModelDefPath(body) == null) throw new PlanException("몸 모델 정의를 설치 폴더에서 찾지 못했습니다: " + body);
        Add(plan, files, "body", PartRole.Body, body);

        // 필수 외형: 표시할 수 없으면 공개된 호환 기본값으로 대신 표시한다. 상태의 원래 선택은 바꾸지 않는다.
        var preset = StateFactory.DefaultPreset(data, plan.Male);
        var head = Required(plan, files, a.age, "head", a.head, id => data.Heads.TryGetValue(id ?? "", out var h) ? h : null,
            data.Heads.Keys, preset?.HeadType, out string headId, failedModels);
        Add(plan, files, "head", PartRole.Head, head.Model(plan.Male, plan.Infant));

        var hair = Required(plan, files, a.age, "hair", a.hair, id => GameData.Resolve(data.Hairs, id, plan.BodyType),
            data.Hairs.Keys, preset?.Hair, out _, failedModels);
        Add(plan, files, "hair", PartRole.Hair, hair.Model(plan.Male, plan.Infant));

        // 눈·입 행은 머리형별이다. 실제로 표시하는 머리(대체값일 수 있음)로 고른다.
        var eyes = Required(plan, files, a.age, "eyes", a.eyes, id => GameData.Resolve(data.Eyes, id, headId, "Head_00"),
            data.Eyes.Keys, preset?.Eyes, out _, failedModels);
        Add(plan, files, "eyes", PartRole.Eyes, eyes.Model(plan.Male, plan.Infant));

        // 입은 메시가 아니라 얼굴 머티리얼에 합성하는 텍스처 파츠다(CharacterAssembler.ApplyMouth).
        var mouth = Required(plan, files, a.age, "mouth", a.mouth, id => GameData.Resolve(data.Mouths, id, headId, "Head_00"),
            data.Mouths.Keys, preset?.Mouth, out _, failedModels);
        Add(plan, files, "mouth", PartRole.Mouth, mouth.Model(plan.Male, plan.Infant));

        // 외형 색: 시트 값 → 사용자 색
        void Global(string slot, Rgba32? sheet)
        {
            if (ColorOverrides.TryParse(ColorOverrides.Get(a.colors, slot), out var user)) plan.GlobalColors[slot] = user;
            else if (sheet.HasValue) plan.GlobalColors[slot] = ToColor(sheet.Value);
        }
        var skin = SheetColor(plan, "skinColor", data.SkinColors, a.skinColor, preset?.SkinColor);
        var hairColor = SheetColor(plan, "hairColor", data.HairColors, a.hairColor, preset?.HairColor);
        var eyeColor = SheetColor(plan, "eyeColor", data.EyeColors, a.eyeColor, preset?.EyeColor);
        var mouthColor = SheetColor(plan, "mouthColor", data.MouthColors, a.mouthColor, preset?.MouthColor);
        if (a.colors != null) foreach (var c in a.colors)
        {
            if (c == null) continue;
            if ((c.slot != "Skin" && c.slot != "Hair" && c.slot != "Hair1" && c.slot != "Eye" && c.slot != "Mouth") ||
                !ColorOverrides.TryParse(c.color, out _))
                plan.AddIssue("appearanceColors", IssueKind.DyeUnresolved, c.slot, null, "외형 색 값을 해석하지 못해 적용하지 않습니다(값 보존)");
        }
        Global("Skin", skin?.Color1);
        Global("Hair", hairColor?.Color1);
        // 투톤 헤어의 두 번째 색. 한 가지 색(OneColor) 행의 ColorCode2 는 #000000 이므로 첫 색을 같이 쓴다[추정].
        Global("Hair1", hair.HairType == "TwoColor"
            ? (hairColor != null && hairColor.Type == "TwoColor" ? hairColor.Color2 : hairColor?.Color1) : null);
        Global("Eye", eyeColor?.Color1);
        Global("Mouth", mouthColor?.Color1);

        ResolveCostumes(plan, state, data, files, failedModels);

        string prefix = plan.Infant ? "HC" : plan.Male ? "HM" : "HF";
        string variant = plan.BodyType == "Muscular" ? "Muscle" : plan.BodyType;
        plan.Pose = $"{prefix}@Customize_Pose_08_{variant}";
        if (!files.HasResource(PosePath(plan.Pose))) plan.Pose = $"{prefix}@Customize_Pose_08_Default";

        // 아이들 모션: <접두>@Customize_Stand_<체형>. 체형별 클립이 없으면(여성 Thin 등) Default.
        string folder = plan.Infant ? "Infant" : plan.Male ? "Male" : "Female";
        plan.Idle = IdlePath(folder, $"{prefix}@Customize_Stand_{variant}");
        if (!files.HasAsset(plan.Idle)) plan.Idle = IdlePath(folder, $"{prefix}@Customize_Stand_Default");
        if (!files.HasAsset(plan.Idle)) { plan.Notes.Add("아이들 모션 없음: " + plan.Idle); plan.Idle = null; }
        plan.FaceIdle = plan.Idle;
        plan.PartClip = data.PartAnimations.Count > 0 ? data.PartAnimation : (Func<string, string, string>)null;
        ResolveWeaponPose(plan, state, data, files, folder, previous);
        return plan;
    }

    /// <summary>
    /// 자세에 쓸 직업. 사용자가 고른 직업이 있으면 그것, 없으면 착용 무기 외형의 원래 직업(EquippableCombatJobs 첫 값).
    /// 무기 선택은 제한하지 않는다(Equipment.md). 둘 다 없으면 null(공통 자세).
    /// </summary>
    public static string StanceJob(EditState state, GameData data, out bool auto)
    {
        auto = false;
        var job = state.weaponPose?.job;
        if (!string.IsNullOrEmpty(job)) return job;
        auto = true;
        var w = state.Costume("Weapon");
        if (w != null && w.kind == CostumeSlotState.KindCostume && data.Costumes.TryGetValue(w.costumeId ?? "", out var row) && row.EquippableCombatJobs != null)
            foreach (var j in row.EquippableCombatJobs) if (!string.IsNullOrEmpty(j)) return j;
        return null;
    }

    /// <summary>
    /// 무기 자세(WeaponPose.md).
    ///   꺼냄(경계·전투): AnimationStateSet 의 Player&lt;Female|Male|Infant&gt; 그룹에서 AlertStand/CombatStand 클립.
    ///   꾸미기 포즈 1~3: Action 의 Player*_Customize 그룹 Customize_Costume_04~06(직업 계열별) 시작 → 반복 클립.
    ///   직전 계획과 무기가 같고 넣음↔꺼냄만 바뀌면 ToolDraw/ToolUndraw 클립을 도입으로 한 번 재생한다.
    /// 맞는 행이 없거나 클립이 설치 폴더에 없으면 넣은 자세로 둔다.
    /// </summary>
    static void ResolveWeaponPose(AssemblyPlan plan, EditState state, GameData data, InstallFiles files, string folder, AssemblyPlan previous)
    {
        string stance = state.weaponPose?.stance;
        plan.StanceJob = StanceJob(state, data, out _);
        var weapon = plan.Costumes.Find(c => c.Part == "Weapon");
        bool hasWeapon = weapon != null && weapon.Status == CostumeStatus.Applied;
        plan.WeaponId = hasWeapon ? weapon.CostumeId ?? weapon.DisplayName : null;
        if (plan.Idle == null) return;
        if (WeaponPoseState.IsDrawn(stance))
        {
            if (!hasWeapon) plan.Notes.Add("무기가 표시되지 않아 넣은 자세로 표시합니다.");
            else if (WeaponPoseState.IsPose(stance)) ResolvePose(plan, data, files, folder, stance);
            else ResolveStand(plan, data, files, folder, stance);
        }
        if (previous != null && hasWeapon && plan.Intro == null && previous.WeaponId == plan.WeaponId
            && previous.Male == plan.Male && previous.Infant == plan.Infant && previous.WeaponDrawn != plan.WeaponDrawn)
        {
            // 넣기는 직전(꺼낸) 자세의 직업 클립을 쓴다.
            string job = plan.WeaponDrawn ? plan.StanceJob : previous.StanceJob ?? plan.StanceJob;
            var row = data.AnimationState("Player" + folder, plan.WeaponDrawn ? "ToolDraw" : "ToolUndraw", Facts(plan, data, job, false));
            string path = row != null ? BundleCache.AssetRoot + row.Paths[0] : null;
            if (path != null && files.HasAsset(path))
            {
                plan.Intro = path;
                plan.IntroDrawn = plan.WeaponDrawn;
                ReadToolSpans(plan, data, files, path);
            }
        }
    }

    static HashSet<string> Facts(AssemblyPlan plan, GameData data, string job, bool lineAsJob)
    {
        var facts = new HashSet<string>(StringComparer.Ordinal)
        {
            "Gender:" + (plan.Male ? "Male" : "Female"),
            "BodyType:" + plan.BodyType,
        };
        if (job != null)
        {
            facts.Add("CombatJob:" + job);
            var j = data.CombatJob(job);
            if (j?.LineType != null)
            {
                facts.Add("CombatJobLine:" + j.LineType);
                // 꾸미기 포즈(Action)는 계열의 기본 직업 ID(Warrior·Archer 등 = 계열 이름)로 조건을 건다[추정].
                if (lineAsJob) facts.Add("CombatJob:" + j.LineType);
            }
        }
        return facts;
    }

    static void ResolveStand(AssemblyPlan plan, GameData data, InstallFiles files, string folder, string stance)
    {
        string symbol = stance == WeaponPoseState.Combat ? "CombatStand" : "AlertStand";
        var row = data.AnimationState("Player" + folder, symbol, Facts(plan, data, plan.StanceJob, false));
        if (row == null) { plan.Notes.Add($"자세 클립 없음: Player{folder} {symbol} {plan.StanceJob}"); return; }
        string path = BundleCache.AssetRoot + row.Paths[0];
        if (!files.HasAsset(path)) { plan.Notes.Add("자세 클립이 설치 폴더에 없음: " + path); return; }
        plan.Idle = path;
        plan.WeaponDrawn = true;
        plan.StanceCondition = row.Condition;
    }

    static void ResolvePose(AssemblyPlan plan, GameData data, InstallFiles files, string folder, string stance)
    {
        string action = "Customize_Costume_0" + (4 + WeaponPoseState.PoseIndex(stance));
        var row = data.FindAction("Player" + folder + "_Customize", action, Facts(plan, data, plan.StanceJob, true));
        if (row == null || string.IsNullOrEmpty(row.LoopClip)) { plan.Notes.Add($"꾸미기 포즈 없음: {action} {plan.StanceJob}"); return; }
        string loop = BundleCache.AssetRoot + row.LoopClip;
        if (!files.HasAsset(loop)) { plan.Notes.Add("꾸미기 포즈 클립이 설치 폴더에 없음: " + loop); return; }
        plan.Idle = loop;
        plan.WeaponDrawn = true;
        plan.StanceCondition = action + " " + row.Condition;
        // 게임 꾸미기 화면은 포즈 중 무기를 꺼낸 상태로 둔다(Customize_ToolStateTrack: MainTool·SubTool Drawn).
        string start = string.IsNullOrEmpty(row.StartClip) ? null : BundleCache.AssetRoot + row.StartClip;
        if (start != null && files.HasAsset(start)) { plan.Intro = start; plan.IntroDrawn = true; }
    }

    /// <summary>클립의 스테이지 타임라인(StageTimeline 시트 → StageTimelines/&lt;이름&gt;)에서 ToolState 트랙 구간을 읽는다.</summary>
    static void ReadToolSpans(AssemblyPlan plan, GameData data, InstallFiles files, string clipPath)
    {
        string name = clipPath.Substring(clipPath.LastIndexOf('/') + 1);
        if (name.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
        if (!data.ClipTimelines.TryGetValue(name, out var timelines)) return;
        foreach (var tl in timelines)
        {
            string path = files.FindResourcePathEndingWith("/" + tl + ".StageTimeline.sjon.bin");
            if (path == null) continue;
            foreach (var track in files.ReadSjon(path).Get("Tracks").Arr())
            {
                if (track.Get("Type").Str() != "ToolState") continue;
                foreach (var iv in track.Get("Intervals").Arr())
                {
                    var pl = iv.Get("Playable");
                    string type = pl.Get("ToolType").Str(), st = pl.Get("ToolState").Str();
                    if (type == null || st == null) continue;
                    plan.IntroToolSpans.Add((type, string.Equals(st, "Drawn", StringComparison.OrdinalIgnoreCase), iv.Get("BeginTime").F(), iv.Get("EndTime").F()));
                }
            }
        }
    }

    public static string PosePath(string pose) => "Animation/Bones/" + pose + ".sjon.bin";
    static string IdlePath(string folder, string clip) => $"{BundleCache.AssetRoot}Animation/Player/Human/{folder}/Customize/{clip}.fbx";

    static PartPlan Add(AssemblyPlan plan, InstallFiles files, string key, PartRole role, string model)
    {
        if (string.IsNullOrEmpty(model)) return null;
        if (files.ModelDefPath(model) == null)
        {
            plan.AddIssue(key, IssueKind.MissingResource, model, null, "모델 정의를 설치 폴더에서 찾지 못했습니다");
            return null;
        }
        var p = new PartPlan { Key = key, Role = role, Model = model };
        plan.Parts.Add(p);
        return p;
    }

    /// <summary>
    /// 외형 파츠를 지금 외형으로 표시할 수 없는 이유. 표시할 수 있으면 null.
    /// asSelection: 사용자의 선택으로 보는지. 숨김 데이터는 에디터의 선택일 때만 허용하고, 대체값으로는 쓰지 않는다.
    /// files 가 null 이면 설치 폴더 리소스 존재는 보지 않는다(목록 표시용).
    /// </summary>
    public static string PartProblem(AppearancePartData p, string id, bool male, bool infant, int age, InstallFiles files, bool asSelection, out IssueKind kind)
    {
        kind = IssueKind.Incompatible;
        if (string.IsNullOrEmpty(id)) { kind = IssueKind.UnknownId; return "선택이 비어 있습니다"; }
        if (p == null) { kind = IssueKind.UnknownId; return "데이터에 없는 ID입니다"; }
        if (p.HideOnCustomize && !(asSelection && Disclosure.AllowRestricted)) { kind = IssueKind.Restricted; return Disclosure.HiddenDataReason; }
        string model = p.Model(male, infant);
        if (model == null) return "현재 성별·나이에 맞는 모델이 없습니다";
        if (age > 0 && p.MinAge > age) return $"{p.MinAge}세 이상 외형입니다";
        if (files != null && files.ModelDefPath(model) == null) { kind = IssueKind.MissingResource; return "모델 정의를 설치 폴더에서 찾지 못했습니다: " + model; }
        return null;
    }

    /// <summary>
    /// 필수 외형 하나. 선택을 표시할 수 있으면 그대로, 아니면 기본 프리셋 값 → 시트 순서의 첫 공개·호환 후보로 대신 표시하고 기록한다.
    /// 정상적인 호환 불가는 대체 기록만, 없는 ID·제한·리소스 누락은 문제(Issues)로도 남긴다.
    /// </summary>
    static AppearancePartData Required(AssemblyPlan plan, InstallFiles files, int age, string key, string selected,
        Func<string, AppearancePartData> lookup, IEnumerable<string> candidates, string preferred, out string shownId, HashSet<string> failedModels)
    {
        var p = lookup(selected);
        string why = PartProblem(p, selected, plan.Male, plan.Infant, age, files, true, out var kind);
        if (why == null && failedModels != null && failedModels.Contains(p.Model(plan.Male, plan.Infant)))
        {
            kind = IssueKind.MissingResource;
            why = "모델의 프리팹·메시·텍스처를 정상적으로 조립하지 못했습니다";
        }
        if (why == null) { shownId = selected; return p; }
        if (kind != IssueKind.Incompatible) plan.AddIssue(key, kind, selected, p?.DisplayName, why);
        foreach (var id in Prepend(preferred, candidates))
        {
            if (id == selected) continue;
            var c = lookup(id);
            if (PartProblem(c, id, plan.Male, plan.Infant, age, files, false, out _) != null) continue;
            if (failedModels != null && failedModels.Contains(c.Model(plan.Male, plan.Infant))) continue;
            shownId = id;
            plan.Substitutes.Add(new AppearanceSubstitute { part = key, selected = selected, shown = id, reason = why });
            return c;
        }
        throw new PlanException($"{AssemblyPlan.PartLabel(key)}: 표시할 수 있는 공개 기본값이 없습니다({why}).");
    }

    static IEnumerable<string> Prepend(string first, IEnumerable<string> rest)
    {
        if (!string.IsNullOrEmpty(first)) yield return first;
        foreach (var r in rest) if (r != first) yield return r;
    }

    /// <summary>외형 색 시트 행. 비어 있으면 원본 머티리얼 색(null). 없는 ID·제한 색은 기본 프리셋 색(공개)으로 대신 표시한다.</summary>
    static ColorData SheetColor(AssemblyPlan plan, string key, Dictionary<string, ColorData> map, string selected, string preferred)
    {
        if (string.IsNullOrEmpty(selected)) return null;
        map.TryGetValue(selected, out var c);
        string why = null;
        var kind = IssueKind.UnknownId;
        if (c == null) why = "데이터에 없는 색 ID입니다";
        else if (c.HideOnCustomize && !Disclosure.AllowRestricted) { why = Disclosure.HiddenDataReason; kind = IssueKind.Restricted; }
        if (why == null) return c;
        plan.AddIssue(key, kind, selected, c?.DisplayName, why);
        foreach (var id in Prepend(preferred, map.Keys))
        {
            if (id == selected || !map.TryGetValue(id, out var f) || f.HideOnCustomize) continue;
            plan.Substitutes.Add(new AppearanceSubstitute { part = key, selected = selected, shown = id, reason = why });
            return f;
        }
        return null;
    }

    /// <summary>의상 염색 슬롯 이름(Slot0~5)인지. 외형 색 슬롯 이름 등은 의상 파트에 적용하지 않는다.</summary>
    public static bool IsCostumeDyeSlot(string slot) =>
        slot != null && slot.Length == 5 && slot.StartsWith("Slot", StringComparison.Ordinal) && slot[4] >= '0' && slot[4] <= '5';

    /// <summary>
    /// 패션장비 행이 현재 성별·나이에서 입을 수 있는지(설치 폴더 리소스 존재는 보지 않는다).
    /// Applied 면 models 에 조립할 모델 이름, 아니면 GenderMismatch / NoModelForAppearance 와 안내 문구.
    /// </summary>
    public static CostumeStatus CheckCostume(CostumeData row, bool male, bool infant, out List<string> models, out string detail)
    {
        models = null;
        detail = null;
        if ((row.EquippableGender == "Male" && !male) || (row.EquippableGender == "Female" && male))
        {
            detail = row.EquippableGender == "Male" ? "남성 전용입니다." : "여성 전용입니다.";
            return CostumeStatus.GenderMismatch;
        }
        var list = infant ? (male ? row.BoyModels : row.GirlModels) : (male ? row.MaleModels : row.FemaleModels);
        models = list.FindAll(m => !string.IsNullOrEmpty(m) && m != "Invalid");
        if (models.Count == 0) { detail = "현재 외형에 맞는 모델이 없습니다."; return CostumeStatus.NoModelForAppearance; }
        return CostumeStatus.Applied;
    }

    static void ResolveCostumes(AssemblyPlan plan, EditState state, GameData data, InstallFiles files, HashSet<string> failedModels)
    {
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        var applied = new List<(CostumeSlotState slot, CostumeResult res, CostumeData row)>();

        foreach (var slot in state.costumes ?? new List<CostumeSlotState>())
        {
            if (slot == null || string.IsNullOrEmpty(slot.part)) continue;
            var res = new CostumeResult { Part = slot.part, CostumeId = slot.costumeId };
            plan.Costumes.Add(res);
            string key = AssemblyPlan.CostumeKey(slot.part);
            if (slot.kind == CostumeSlotState.KindResource)
            {
                res.DisplayName = slot.resource;
                // 직접 리소스는 에디터 전용이다. 공개 빌드에서는 참조·염색을 보존하고 출력하지 않는다.
                if (!Disclosure.AllowRestricted)
                {
                    res.Status = CostumeStatus.Restricted; res.Detail = Disclosure.ResourceReason + ".";
                    plan.AddIssue(key, IssueKind.Restricted, slot.resource, null, Disclosure.ResourceReason);
                    continue;
                }
#if UNITY_EDITOR
                if (string.IsNullOrEmpty(slot.resource) || files.ModelDefPath(slot.resource) == null || (failedModels?.Contains(slot.resource) ?? false))
                {
                    res.Status = CostumeStatus.MissingResource; res.Detail = "리소스를 찾을 수 없습니다.";
                    plan.AddIssue(key, IssueKind.MissingResource, slot.resource, null, "모델 정의를 설치 폴더에서 찾지 못했습니다");
                    continue;
                }
                res.Models.Add(slot.resource);
                applied.Add((slot, res, null));
#endif
                continue;
            }
            if (string.IsNullOrEmpty(slot.costumeId)) { plan.Costumes.Remove(res); continue; }
            if (!data.Costumes.TryGetValue(slot.costumeId, out var row))
            {
                res.Status = CostumeStatus.UnknownId; res.Detail = "패션장비 데이터에 없는 ID입니다.";
                plan.AddIssue(key, IssueKind.UnknownId, slot.costumeId, null, "패션장비 데이터에 없는 ID입니다");
                continue;
            }
            res.DisplayName = row.DisplayName;
            string hiddenWhy = Disclosure.CostumeReason(row);
            if (hiddenWhy != null && !Disclosure.AllowRestricted)
            {
                res.Status = CostumeStatus.Restricted; res.Detail = hiddenWhy + ". 공개 빌드에서 표시하지 않습니다.";
                plan.AddIssue(key, IssueKind.Restricted, row.Id, row.DisplayName, hiddenWhy);
                continue;
            }
            var fit = CheckCostume(row, plan.Male, plan.Infant, out var models, out var detail);
            if (fit != CostumeStatus.Applied) { res.Status = fit; res.Detail = detail; continue; }
            var missing = models.Find(m => files.ModelDefPath(m) == null || (failedModels?.Contains(m) ?? false));
            if (missing != null)
            {
                res.Status = CostumeStatus.MissingResource; res.Detail = "모델을 찾을 수 없습니다: " + missing;
                plan.AddIssue(key, IssueKind.MissingResource, row.Id, row.DisplayName, "모델 리소스를 정상적으로 준비하지 못했습니다: " + missing);
                continue;
            }
            res.Models.AddRange(models);
            applied.Add((slot, res, row));
        }

        // 실제 적용되는 의상의 HidingParts 만 다른 부위를 가린다. 미적용 의상은 가리지 않는다.
        foreach (var (slot, res, row) in applied)
            if (row != null) foreach (var h in row.HidingParts) if (h != slot.part) hidden.Add(h);

        foreach (var (slot, res, row) in applied)
        {
            if (hidden.Contains(slot.part)) { res.Status = CostumeStatus.HiddenByOther; res.Detail = "다른 의상에 가려졌습니다."; continue; }
            res.Status = CostumeStatus.Applied;
            string key = AssemblyPlan.CostumeKey(slot.part);
            string id = row?.Id ?? slot.resource;
            // 염색: 안정적인 슬롯 이름(Slot0~5)으로만 대응한다. 해석하지 못한 값·모르는 슬롯은 보존하되 어느 파트에도 적용하지 않는다.
            foreach (var d in slot.dyes ?? new List<ColorOverride>())
            {
                if (d == null) continue;
                if (!IsCostumeDyeSlot(d.slot))
                    plan.AddIssue(key, IssueKind.DyeUnresolved, id, res.DisplayName, $"염색 파트 {d.slot}: 알 수 없는 파트 이름이라 적용하지 않습니다(값 보존)");
                else if (!ColorOverrides.TryParse(d.color, out _))
                    plan.AddIssue(key, IssueKind.DyeUnresolved, id, res.DisplayName, $"염색 파트 {d.slot}: 색 값 {d.color}을(를) 해석하지 못해 적용하지 않습니다(값 보존)");
            }
            for (int i = 0; i < res.Models.Count; i++)
            {
                var p = Add(plan, files, key, PartRole.Costume, res.Models[i]);
                if (p == null) continue;
                if (row != null)
                    for (int k = 0; k < 6; k++)
                        if (row.DyeColors[k].HasValue) p.SlotColors["Slot" + k] = ToColor(row.DyeColors[k].Value);
                foreach (var d in slot.dyes ?? new List<ColorOverride>())
                    if (d != null && IsCostumeDyeSlot(d.slot) && ColorOverrides.TryParse(d.color, out var c)) { p.SlotColors[d.slot] = c; p.UserSlots.Add(d.slot); }
            }
        }
    }
}
