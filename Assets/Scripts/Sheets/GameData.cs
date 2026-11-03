// 꾸미기에 필요한 시트를 읽어 ID 로 조회할 수 있게 정리한다. UnityEngine 에 의존하지 않는다.
using System;
using System.Collections.Generic;
using System.Diagnostics;

public sealed class CostumeData
{
    public string Id, DisplayName, FlavorText, CostumeType, DyePolicy, EquippableGender, EquippableAgeType, GenderChange;
    public string MaleIconPath, FemaleIconPath;
    public List<string> HidingParts, MaleModels, FemaleModels, BoyModels, GirlModels, ExceptionModels;
    /// <summary>원본 게임의 착용 직업(무기 외형). 도구에서는 선택을 막지 않고 무기 자세의 직업 추정에만 쓴다(Equipment.md).</summary>
    public List<string> EquippableCombatJobs;
    /// <summary>DyeColor0~5. null 은 시트 기본 염색값 미지정(원본 머티리얼 값을 유지).</summary>
    public Rgba32?[] DyeColors = new Rgba32?[6];
    public int Index;
}

public sealed class PresetAppearanceData
{
    public string Id, Gender, SkinColor, HairColor, Hair, HeadType, EyeColor, Eyes, MouthColor, Mouth, BodyType, ActorModelName;
    public int Age;
    public float HeightModifier;
}

/// <summary>머리·헤어·눈·입 행. 성별·나이별 모델 이름을 가진다.</summary>
public sealed class AppearancePartData
{
    public string Id, Variant, DisplayName, MaleModel, FemaleModel, InfantMaleModel, InfantFemaleModel;
    public int MinAge;
    public bool HideOnCustomize, CanChangeColor;
    public string HairType;
    public Rgba32? FixedColor;

    public string Model(bool male, bool infant)
    {
        string m = infant ? (male ? InfantMaleModel : InfantFemaleModel) : (male ? MaleModel : FemaleModel);
        return string.IsNullOrEmpty(m) || m == "Invalid" ? null : m;
    }
}

public sealed class BodyTypeData
{
    public string Id, DisplayName, BlendshapeName;
    public bool UseBlendshape;
}

public sealed class ColorData
{
    public string Id, DisplayName, Type;
    public Rgba32? Color1, Color2;
    public bool HideOnCustomize;
}

/// <summary>스튜디오 배경 시트 행(Background.md).</summary>
public sealed class StudioBackgroundData
{
    public string Id, DisplayName, IconPath, UnlockType, Url, VolumeKey, VolumeGroupName;
    /// <summary>화면 네 모서리 색 "#RRGGBB"(왼쪽 아래, 오른쪽 아래, 왼쪽 위, 오른쪽 위).</summary>
    public string LeftBottom, RightBottom, LeftUpper, RightUpper;
    public bool DevOnly, UseWebCam;
    /// <summary>3D 배경 프리팹(Assets/PatchableAssets/ 아래 상대 경로). 비어 있으면 색 배경이다.</summary>
    public List<string> Prefabs;
    public int Index;

    public bool Is3D => Prefabs != null && Prefabs.Count > 0;
}

/// <summary>전투 직업(클래스) 행.</summary>
public sealed class CombatJobData
{
    public string Id, DisplayName, CombatJobType, BeforeAdvancement, LineType, AvatarAnimationSymbol, AvatarToolState;
    public bool ShowClass;
    public int Order;
}

/// <summary>애니메이션 상태 세트 행: (그룹, 조건, 상태 기호) → 클립 경로(Assets/PatchableAssets/ 아래 상대 경로).</summary>
public sealed class AnimationStateRow
{
    public string Group, Condition, Symbol;
    /// <summary>조건 항목("CombatJob:Bard", "BodyType:Fat" 등). "Any" 는 빈 배열이다.</summary>
    public string[] Terms;
    public List<string> Paths;
}

/// <summary>꾸미기 화면 행동 행(Action, Player*_Customize 그룹).</summary>
public sealed class CustomizeActionRow
{
    public string Name, Group, Condition, ToolIndices, StartClip, LoopClip;
    public string[] Terms;
    public bool HideWeapon;
}

public sealed class GameData
{
    public readonly Dictionary<string, CostumeData> Costumes = new Dictionary<string, CostumeData>(StringComparer.Ordinal);
    public readonly List<CostumeData> CostumeList = new List<CostumeData>();
    public readonly Dictionary<string, PresetAppearanceData> Presets = new Dictionary<string, PresetAppearanceData>(StringComparer.Ordinal);
    public readonly Dictionary<string, AppearancePartData> Heads = new Dictionary<string, AppearancePartData>(StringComparer.Ordinal);
    /// <summary>키: GroupId → (BodyType 또는 HeadType) → 행</summary>
    public readonly Dictionary<string, Dictionary<string, AppearancePartData>> Hairs = new Dictionary<string, Dictionary<string, AppearancePartData>>(StringComparer.Ordinal);
    public readonly Dictionary<string, Dictionary<string, AppearancePartData>> Eyes = new Dictionary<string, Dictionary<string, AppearancePartData>>(StringComparer.Ordinal);
    public readonly Dictionary<string, Dictionary<string, AppearancePartData>> Mouths = new Dictionary<string, Dictionary<string, AppearancePartData>>(StringComparer.Ordinal);
    public readonly Dictionary<string, BodyTypeData> BodyTypes = new Dictionary<string, BodyTypeData>(StringComparer.Ordinal);
    public readonly Dictionary<string, ColorData> SkinColors = new Dictionary<string, ColorData>(StringComparer.Ordinal);
    public readonly Dictionary<string, ColorData> HairColors = new Dictionary<string, ColorData>(StringComparer.Ordinal);
    public readonly Dictionary<string, ColorData> EyeColors = new Dictionary<string, ColorData>(StringComparer.Ordinal);
    public readonly Dictionary<string, ColorData> MouthColors = new Dictionary<string, ColorData>(StringComparer.Ordinal);
    public readonly Dictionary<string, SheetRow> ModelVariants = new Dictionary<string, SheetRow>(StringComparer.Ordinal);
    /// <summary>스튜디오 배경(시트 순서). 시트를 읽지 못하면 비어 있고 BackgroundError 에 사유가 있다.</summary>
    public readonly List<StudioBackgroundData> Backgrounds = new List<StudioBackgroundData>();
    public string BackgroundError;
    /// <summary>전투 직업(시트 순서). 무기 자세용. 읽지 못하면 비어 있고 MotionError 에 사유가 있다.</summary>
    public readonly List<CombatJobData> CombatJobs = new List<CombatJobData>();
    /// <summary>키: AnimationGroup + "|" + Symbol → 행들(조건별).</summary>
    public readonly Dictionary<string, List<AnimationStateRow>> AnimationStates = new Dictionary<string, List<AnimationStateRow>>(StringComparer.Ordinal);
    /// <summary>꾸미기 화면 행동. 키: GroupName + "|" + ActionName.</summary>
    public readonly Dictionary<string, List<CustomizeActionRow>> CustomizeActions = new Dictionary<string, List<CustomizeActionRow>>(StringComparer.Ordinal);
    /// <summary>파츠 클립. 키: 파츠 애니메이션 그룹 + "|" + 몸 클립 경로(또는 "Default") → 파츠 클립 경로(Assets/PatchableAssets/ 아래 상대).</summary>
    public readonly Dictionary<string, string> PartAnimations = new Dictionary<string, string>(StringComparer.Ordinal);
    /// <summary>클립 이름 → 스테이지 타임라인 이름들(조건 태그 없는 행).</summary>
    public readonly Dictionary<string, string[]> ClipTimelines = new Dictionary<string, string[]>(StringComparer.Ordinal);
    public string MotionError;
    public long LoadMs;

    public static GameData Load(InstallFiles files)
    {
        var sw = Stopwatch.StartNew();
        var symbols = new SymbolTables(files);
        var set = new SheetSet(files, symbols);
        var g = new GameData();

        foreach (var r in set.Read(SheetSchemas.Costume))
        {
            var c = new CostumeData
            {
                Index = r.Index, Id = r.Str("Id"), DisplayName = r.Str("DisplayName"), FlavorText = r.Str("FlavorText"),
                CostumeType = r.Str("CostumeType"), DyePolicy = r.Str("DyePolicy"), EquippableGender = r.Str("EquippableGender"),
                EquippableAgeType = r.Str("EquippableAgeType"), GenderChange = r.Str("GenderChange"),
                MaleIconPath = r.Str("MaleIconPath"), FemaleIconPath = r.Str("FemaleIconPath"),
                HidingParts = r.Strs("HidingParts"), MaleModels = r.Strs("MaleModelName"), FemaleModels = r.Strs("FemaleModelName"),
                BoyModels = r.Strs("BoyModelName"), GirlModels = r.Strs("GirlModelName"), ExceptionModels = r.Strs("ExceptionModelName"),
                EquippableCombatJobs = r.Strs("EquippableCombatJobs"),
            };
            for (int i = 0; i < 6; i++) c.DyeColors[i] = r.Color("DyeColor" + i);
            if (c.Id == null) continue;
            g.Costumes[c.Id] = c;
            g.CostumeList.Add(c);
        }
        foreach (var r in set.Read(SheetSchemas.CostumeModelVariant))
            if (r.Str("Id") != null) g.ModelVariants[r.Str("Id")] = r;

        foreach (var r in set.Read(SheetSchemas.CharacterPresetAppearance))
        {
            var p = new PresetAppearanceData
            {
                Id = r.Str("Id"), Gender = r.Str("Gender"), Age = (int)r.Long("Age"), SkinColor = r.Str("SkinColor"),
                HairColor = r.Str("HairColor"), Hair = r.Str("Hair"), HeadType = r.Str("HeadType"), EyeColor = r.Str("EyeColor"),
                Eyes = r.Str("Eyes"), MouthColor = r.Str("MouthColor"), Mouth = r.Str("Mouth"), BodyType = r.Str("BodyType"),
                HeightModifier = (float)r.Num("HeightModifier"), ActorModelName = r.Str("ActorModelName"),
            };
            if (p.Id != null) g.Presets[p.Id] = p;
        }

        foreach (var r in set.Read(SheetSchemas.CharacterHead))
        {
            var h = Part(r, r.Str("Id"), null);
            if (h.Id != null) g.Heads[h.Id] = h;
        }
        foreach (var r in set.Read(SheetSchemas.CharacterHair))
        {
            var h = Part(r, r.Str("GroupId"), r.Str("BodyType"));
            h.HairType = r.Str("HairType");
            h.CanChangeColor = r.Bool("CanChangeColor");
            h.FixedColor = r.Color("CustomizeFixedDyeColor");
            Group(g.Hairs, h);
        }
        foreach (var r in set.Read(SheetSchemas.CharacterEyes))
        {
            var h = Part(r, r.Str("GroupId"), r.Str("HeadType"));
            h.CanChangeColor = r.Bool("CanChangeColor");
            Group(g.Eyes, h);
        }
        foreach (var r in set.Read(SheetSchemas.CharacterMouth))
            Group(g.Mouths, Part(r, r.Str("GroupId"), r.Str("HeadType")));

        foreach (var r in set.Read(SheetSchemas.CharacterBodyTypes))
        {
            var b = new BodyTypeData { Id = r.Str("Id"), DisplayName = r.Str("DisplayName"), BlendshapeName = r.Str("BlendshapeName"), UseBlendshape = r.Bool("UseBlendshape") };
            if (b.Id != null) g.BodyTypes[b.Id] = b;
        }

        Colors(set.Read(SheetSchemas.CharacterSkinColor), g.SkinColors, "ColorCode", "ColorCode_Palm", null);
        Colors(set.Read(SheetSchemas.CharacterHairColor), g.HairColors, "ColorCode1", "ColorCode2", "HairColorType");
        Colors(set.Read(SheetSchemas.CharacterEyeColor), g.EyeColors, "ColorCode", null, null);
        Colors(set.Read(SheetSchemas.CharacterMouthColor), g.MouthColors, "ColorCode", null, null);
        LoadBackgrounds(set, g);
        LoadMotion(set, g);
        g.LoadMs = sw.ElapsedMilliseconds;
        return g;
    }

    /// <summary>배경은 시작에 꼭 필요하지 않으므로 읽기 실패를 앱 시작 실패로 만들지 않는다.</summary>
    static void LoadBackgrounds(SheetSet set, GameData g)
    {
        try
        {
            foreach (var r in set.Read(SheetSchemas.StudioBackground))
            {
                var b = new StudioBackgroundData
                {
                    Index = r.Index, Id = r.Str("Id"), DisplayName = r.Str("BackgroundName"), IconPath = r.Str("BackgroundImagePath"),
                    UnlockType = r.Str("UnlockType"), Url = r.Str("Url"), VolumeKey = r.Str("VolumeKey"), VolumeGroupName = r.Str("VolumeGroupName"),
                    LeftBottom = r.Str("LeftBottom"), RightBottom = r.Str("RightBottom"), LeftUpper = r.Str("LeftUpper"), RightUpper = r.Str("RightUpper"),
                    DevOnly = r.Bool("DevOnly"), UseWebCam = r.Bool("UseWebCamTexture"), Prefabs = r.Strs("BackgroundPrefabPath"),
                };
                if (b.Id != null) g.Backgrounds.Add(b);
            }
        }
        catch (Exception e)
        {
            g.Backgrounds.Clear();
            g.BackgroundError = e.Message;
        }
    }

    /// <summary>무기 자세 데이터(직업·애니메이션 상태 세트). 없어도 앱은 시작하고 무기는 넣은 자세만 쓴다.</summary>
    static void LoadMotion(SheetSet set, GameData g)
    {
        try
        {
            foreach (var r in set.Read(SheetSchemas.CombatJob))
            {
                var j = new CombatJobData
                {
                    Id = r.Str("Id"), DisplayName = r.Str("DisplayName"), ShowClass = r.Bool("ShowClass"), Order = (int)r.Long("Order"),
                    CombatJobType = r.Str("CombatJobType"), BeforeAdvancement = r.Str("CombatJobBeforeAdvancement"), LineType = r.Str("CombatJobLineType"),
                    AvatarAnimationSymbol = r.Str("AvatarAnimationSymbol"), AvatarToolState = r.Str("AvatarToolState"),
                };
                if (j.Id != null) g.CombatJobs.Add(j);
            }
            foreach (var r in set.Read(SheetSchemas.AnimationStateSet))
            {
                var a = new AnimationStateRow { Group = r.Str("AnimationGroup"), Condition = r.Str("Condition"), Symbol = r.Str("Symbol"), Paths = r.Strs("AssetPaths") };
                if (a.Group == null || a.Symbol == null) continue;
                a.Paths.RemoveAll(string.IsNullOrEmpty);
                a.Terms = ParseCondition(a.Condition);
                string key = a.Group + "|" + a.Symbol;
                if (!g.AnimationStates.TryGetValue(key, out var list)) g.AnimationStates[key] = list = new List<AnimationStateRow>();
                list.Add(a);
            }
        }
        catch (Exception e)
        {
            g.CombatJobs.Clear();
            g.AnimationStates.Clear();
            g.MotionError = e.Message;
            return;
        }
        // 아래는 연출 보조 데이터다. 읽지 못해도 자세 선택은 동작한다(포즈·전환·무기 자체 클립만 빠진다).
        try
        {
            foreach (var r in set.Read(SheetSchemas.Action))
            {
                string group = r.Str("GroupName");
                if (group == null || !group.EndsWith("_Customize", StringComparison.Ordinal)) continue;
                var a = new CustomizeActionRow
                {
                    Name = r.Str("ActionName"), Group = group, Condition = r.Str("Condition"), ToolIndices = r.Str("ToolIndices"),
                    HideWeapon = r.Bool("HideWeapon"), StartClip = r.Str("StartClipPath"), LoopClip = r.Str("LoopClipPath"),
                };
                if (a.Name == null) continue;
                a.Terms = ParseCondition(a.Condition);
                string key = group + "|" + a.Name;
                if (!g.CustomizeActions.TryGetValue(key, out var list)) g.CustomizeActions[key] = list = new List<CustomizeActionRow>();
                list.Add(a);
            }
            foreach (var r in set.Read(SheetSchemas.ModelPartAnimation))
            {
                string grp = r.Str("ModelPartGroupName"), actor = r.Str("ActorModelClipPath"), part = r.Str("ModelPartClipPath");
                if (string.IsNullOrEmpty(grp) || string.IsNullOrEmpty(actor) || string.IsNullOrEmpty(part)) continue;
                g.PartAnimations[grp + "|" + actor] = part;
            }
            foreach (var r in set.Read(SheetSchemas.StageTimeline))
            {
                string clip = r.Str("AnimationClipName"), names = r.Str("StageTimelineNames");
                if (string.IsNullOrEmpty(clip) || string.IsNullOrEmpty(names) || !string.IsNullOrEmpty(r.Str("TimelineOverrideConditionTag"))) continue;
                var list = new List<string>();
                foreach (var n in names.Split(',')) if (n.Trim().Length > 0) list.Add(n.Trim());
                g.ClipTimelines[clip] = list.ToArray();
            }
        }
        catch (Exception e)
        {
            g.CustomizeActions.Clear();
            g.PartAnimations.Clear();
            g.ClipTimelines.Clear();
            g.MotionError = "연출 데이터: " + e.Message;
        }
    }

    /// <summary>꾸미기 행동 중 조건 항목이 모두 facts 에 있고 항목이 가장 많은 행. 없으면 null.</summary>
    public CustomizeActionRow CustomizeAction(string group, string name, ICollection<string> facts)
    {
        if (!CustomizeActions.TryGetValue(group + "|" + name, out var list)) return null;
        CustomizeActionRow best = null;
        foreach (var row in list)
        {
            bool ok = true;
            foreach (var t in row.Terms) if (!facts.Contains(t)) { ok = false; break; }
            if (ok && (best == null || row.Terms.Length > best.Terms.Length)) best = row;
        }
        return best;
    }

    /// <summary>파츠 애니메이션 그룹의 클립: 몸 클립 전용 행, 없으면 Default 행. 몸 클립 경로는 Assets/PatchableAssets/ 아래 상대 경로.</summary>
    public string PartAnimation(string group, string actorClip)
    {
        if (string.IsNullOrEmpty(group)) return null;
        if (actorClip != null && PartAnimations.TryGetValue(group + "|" + actorClip, out var p)) return p;
        return PartAnimations.TryGetValue(group + "|Default", out p) ? p : null;
    }

    static string[] ParseCondition(string c)
    {
        if (string.IsNullOrEmpty(c) || c == "Any") return Array.Empty<string>();
        var parts = c.Split(',');
        for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
        return parts;
    }

    public CombatJobData CombatJob(string id)
    {
        foreach (var j in CombatJobs) if (j.Id == id) return j;
        return null;
    }

    /// <summary>
    /// 그룹·상태 기호에 맞는 행 중 조건 항목이 모두 <paramref name="facts"/> 에 있는 행을 고른다. 항목이 가장 많은(구체적인) 행이 우선이다.
    /// 게임은 조건 결정 트리로 고른다. 여기서는 그 근사다[추정]. 맞는 행이 없으면 null.
    /// </summary>
    public AnimationStateRow AnimationState(string group, string symbol, ICollection<string> facts)
    {
        if (!AnimationStates.TryGetValue(group + "|" + symbol, out var list)) return null;
        AnimationStateRow best = null;
        foreach (var row in list)
        {
            if (row.Paths.Count == 0) continue;
            bool ok = true;
            foreach (var t in row.Terms) if (!facts.Contains(t)) { ok = false; break; }
            if (ok && (best == null || row.Terms.Length > best.Terms.Length)) best = row;
        }
        return best;
    }

    public StudioBackgroundData Background(string id)
    {
        foreach (var b in Backgrounds) if (b.Id == id) return b;
        return null;
    }

    static AppearancePartData Part(SheetRow r, string id, string variant) => new AppearancePartData
    {
        Id = id, Variant = variant, DisplayName = r.Str("DisplayName"), MinAge = (int)r.Long("MinAge"),
        HideOnCustomize = r.Bool("HideOnCustomize"),
        MaleModel = r.Str("MaleModel"), FemaleModel = r.Str("FemaleModel"),
        InfantMaleModel = r.Str("InfantMaleModel"), InfantFemaleModel = r.Str("InfantFemaleModel"),
    };

    static void Group(Dictionary<string, Dictionary<string, AppearancePartData>> map, AppearancePartData p)
    {
        if (p.Id == null) return;
        if (!map.TryGetValue(p.Id, out var d)) map[p.Id] = d = new Dictionary<string, AppearancePartData>(StringComparer.Ordinal);
        d[p.Variant ?? ""] = p;
    }

    static void Colors(List<SheetRow> rows, Dictionary<string, ColorData> map, string c1, string c2, string type)
    {
        foreach (var r in rows)
        {
            var c = new ColorData
            {
                Id = r.Str("Id"), DisplayName = r.Str("DisplayName"), HideOnCustomize = r.Bool("HideOnCustomize"),
                Color1 = r.Color(c1), Color2 = c2 != null ? r.Color(c2) : null, Type = type != null ? r.Str(type) : null,
            };
            if (c.Id != null) map[c.Id] = c;
        }
    }

    /// <summary>그룹 행 중 variant(체형·머리형)에 맞는 행. 없으면 Default, 그것도 없으면 첫 행.</summary>
    public static AppearancePartData Resolve(Dictionary<string, Dictionary<string, AppearancePartData>> map, string group, string variant, string fallbackVariant = "Default")
    {
        if (group == null || !map.TryGetValue(group, out var d) || d.Count == 0) return null;
        if (variant != null && d.TryGetValue(variant, out var p)) return p;
        if (fallbackVariant != null && d.TryGetValue(fallbackVariant, out p)) return p;
        foreach (var v in d.Values) return v;
        return null;
    }
}
