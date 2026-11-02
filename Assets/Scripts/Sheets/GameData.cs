// 꾸미기에 필요한 시트를 읽어 ID 로 조회할 수 있게 정리한다. UnityEngine 에 의존하지 않는다.
using System;
using System.Collections.Generic;
using System.Diagnostics;

public sealed class CostumeData
{
    public string Id, DisplayName, FlavorText, CostumeType, DyePolicy, EquippableGender, EquippableAgeType, GenderChange;
    public string MaleIconPath, FemaleIconPath;
    public List<string> HidingParts, MaleModels, FemaleModels, BoyModels, GirlModels, ExceptionModels;
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
