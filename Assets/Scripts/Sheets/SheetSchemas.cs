// 앱이 읽는 시트의 필드 순서. 시트 파일에는 스키마가 없으므로 게임의 시트 로더가 BinaryDecoder 를 호출하는 순서를 그대로 적는다.
// 표기: "이름:연산" 을 공백으로 구분한다. 배열은 "이름:[연산]". 읽기만 하고 버릴 필드는 이름을 "_" 로 쓴다.
//   I VariantInt / U VariantUInt(bool 포함) / S 문자열 / E 열거형(문자열) / N VariantNumber / C Color32? / B byte / D double(8바이트, TimeSpan 초 등)
//   K(표) 심볼 ID(u32). 괄호 안은 SymbolTables/<표>_SymbolTable 의 표 이름
// 배열 원소가 여러 값으로 된 구조체(예: AssetPath = 문자열 두 개)면 "[S+S]" 처럼 + 로 잇는다. 값은 첫 연산의 것만 남긴다.
// 게임 업데이트로 순서가 바뀌면 SheetSet.Read 가 행 크기 불일치로 실패한다. 이때 여기를 고친다.
using System;
using System.Collections.Generic;

public readonly struct SheetField
{
    public readonly string Name;
    public readonly char Op;
    public readonly string Symbol;
    public readonly bool IsArray;
    /// <summary>배열 원소가 여러 연산으로 된 경우의 나머지 연산(첫 연산은 Op/Symbol). 값은 버린다.</summary>
    public readonly (char op, string symbol)[] Extra;
    public SheetField(string name, char op, string symbol, bool isArray, (char, string)[] extra = null)
    {
        Name = name; Op = op; Symbol = symbol; IsArray = isArray; Extra = extra ?? Array.Empty<(char, string)>();
    }
}

public sealed class SheetSchema
{
    public readonly string Sheet;
    public readonly SheetField[] Preload, Row;

    public SheetSchema(string sheet, string preload, string row)
    {
        Sheet = sheet;
        Preload = Parse(preload);
        Row = Parse(row);
    }

    static SheetField[] Parse(string spec)
    {
        var outp = new List<SheetField>();
        foreach (var tok in spec.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int c = tok.IndexOf(':');
            if (c <= 0) throw new ArgumentException("시트 스키마 표기 오류: " + tok);
            string name = tok.Substring(0, c), op = tok.Substring(c + 1);
            bool arr = op.StartsWith("[") && op.EndsWith("]");
            if (arr) op = op.Substring(1, op.Length - 2);
            var parts = op.Split('+');
            if (parts.Length > 1 && !arr) throw new ArgumentException("시트 스키마 표기 오류(+ 는 배열 원소에만): " + tok);
            var ops = new (char op, string symbol)[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string o = parts[i], sym = null;
                int p = o.IndexOf('(');
                if (p >= 0) { sym = o.Substring(p + 1, o.Length - p - 2); o = o.Substring(0, p); }
                if (o.Length != 1) throw new ArgumentException("시트 스키마 표기 오류: " + tok);
                ops[i] = (o[0], sym);
            }
            var extra = new (char, string)[ops.Length - 1];
            Array.Copy(ops, 1, extra, 0, extra.Length);
            outp.Add(new SheetField(name, ops[0].op, ops[0].symbol, arr, extra));
        }
        return outp.ToArray();
    }
}

public static class SheetSchemas
{
    public static readonly SheetSchema Costume = new SheetSchema("Costume",
        "Id:K(CostumeSheet)",
        @"DisplayName:S FlavorText:S _:K(CostumeSetInfoSheet) CostumeType:E DyePolicy:E HidingParts:[E]
          _:U _:U _:U _:I _:E _:S _:U _:K(CurrencyInfoSheet) _:I _:E _:S _:E
          EquippableCombatJobs:[K(CombatJobSheet)] EquippableGender:E EquippableAgeType:E _:[K(MagicEffectSheet)] _:E
          GenderChange:K(CostumeSheet) _:S _:K(CostumeSheet)
          DyeColor0:C DyeColor1:C DyeColor2:C DyeColor3:C DyeColor4:C DyeColor5:C
          MaleModelName:[S] FemaleModelName:[S] BoyModelName:[S] GirlModelName:[S]
          MaleIconPath:S FemaleIconPath:S ExceptionModelName:[K(CostumeModelVariantSheet)]");

    public static readonly SheetSchema CostumeModelVariant = new SheetSchema("CostumeModelVariant",
        "Id:K(CostumeModelVariantSheet)",
        "PartDataType:E PartData:S MaleModelName:S FemaleModelName:S BoyModelName:S GirlModelName:S");

    public static readonly SheetSchema CharacterPresetAppearance = new SheetSchema("CharacterPresetAppearance",
        "Id:K(CharacterPresetAppearanceSheet)",
        @"Gender:E Age:I SkinColor:K(CharacterSkinColorSheet) HairColor:K(CharacterHairColorSheet) Hair:K(CharacterHairSheetGroup)
          HeadType:K(CharacterHeadSheet) EyeColor:K(CharacterEyeColorSheet) Eyes:K(CharacterEyesSheetGroup)
          MouthColor:K(CharacterMouthColorSheet) Mouth:K(CharacterMouthSheetGroup) HeightModifier:N BodyType:K(CharacterBodyTypesSheet)
          ActorModelName:S Beard:K(CharacterBeardSheet) FaceTattoo:K(CharacterFaceTattooSheet) BodyTattoo:K(CharacterBodyTattooSheet)");

    public static readonly SheetSchema CharacterHead = new SheetSchema("CharacterHead",
        "Id:K(CharacterHeadSheet) MaleModel:S FemaleModel:S InfantMaleModel:S InfantFemaleModel:S",
        "DisplayName:S MinAge:I _:U _:U HideOnCustomize:U IconPath:S");

    public static readonly SheetSchema CharacterHair = new SheetSchema("CharacterHair",
        "GroupId:K(CharacterHairSheetGroup) BodyType:K(CharacterBodyTypesSheet) MaleModel:S FemaleModel:S InfantMaleModel:S InfantFemaleModel:S",
        @"DisplayName:S MinAge:I _:U _:U HideOnCustomize:U HairType:E CapFrontHairDyeSlot:E MaleIconPath:S FemaleIconPath:S
          CanChangeColor:U _:K(CustomizeRestrictionSheet) CustomizeFixedDyeColor:C");

    public static readonly SheetSchema CharacterEyes = new SheetSchema("CharacterEyes",
        "GroupId:K(CharacterEyesSheetGroup) HeadType:K(CharacterHeadSheet) MaleModel:S FemaleModel:S InfantMaleModel:S InfantFemaleModel:S",
        "DisplayName:S MinAge:I _:U _:U HideOnCustomize:U MaleIconPath:S FemaleIconPath:S CanChangeColor:U _:K(CustomizeRestrictionSheet)");

    public static readonly SheetSchema CharacterMouth = new SheetSchema("CharacterMouth",
        "GroupId:K(CharacterMouthSheetGroup) HeadType:K(CharacterHeadSheet) MaleModel:S FemaleModel:S InfantMaleModel:S InfantFemaleModel:S",
        "DisplayName:S MinAge:I _:U _:U HideOnCustomize:U MaleIconPath:S FemaleIconPath:S");

    public static readonly SheetSchema CharacterBodyTypes = new SheetSchema("CharacterBodyTypes",
        "Id:K(CharacterBodyTypesSheet) BlendshapeName:S",
        "DisplayName:S DescriptionText:S UseBlendshape:U IconPath:S");

    public static readonly SheetSchema CharacterSkinColor = new SheetSchema("CharacterSkinColor",
        "Id:K(CharacterSkinColorSheet)",
        "DisplayName:S _:S _:U _:U HideOnCustomize:U ColorCode:C ColorCode_Palm:C");

    public static readonly SheetSchema CharacterHairColor = new SheetSchema("CharacterHairColor",
        "Id:K(CharacterHairColorSheet)",
        "DisplayName:S _:U _:U HideOnCustomize:U HairColorType:E ColorCode1:C ColorCode2:C");

    public static readonly SheetSchema CharacterEyeColor = new SheetSchema("CharacterEyeColor",
        "Id:K(CharacterEyeColorSheet)",
        "DisplayName:S _:S _:U _:U HideOnCustomize:U ColorCode:C");

    public static readonly SheetSchema CharacterMouthColor = new SheetSchema("CharacterMouthColor",
        "Id:K(CharacterMouthColorSheet)",
        "DisplayName:S _:S ColorCode:C");

    /// <summary>스튜디오 배경(Background.md). 시작에 필요하지 않으므로 Required 에 넣지 않는다. 네 모서리 색은 "#RRGGBB" 문자열이다.</summary>
    public static readonly SheetSchema StudioBackground = new SheetSchema("StudioBackground",
        "Id:K(StudioBackgroundSheet)",
        @"BackgroundName:S BackgroundImagePath:S UnlockType:E _:S _:S
          LeftBottom:S RightBottom:S LeftUpper:S RightUpper:S
          UseWebCamTexture:U DevOnly:U Url:S _:U _:I _:I _:I
          BackgroundPrefabPath:[S] VolumeKey:S VolumeGroupName:S");

    /// <summary>전투 직업(클래스). 무기 자세의 직업 이름·계열에 쓴다(WeaponPose.md). 시작에 필요하지 않다.</summary>
    public static readonly SheetSchema CombatJob = new SheetSchema("CombatJob",
        "Id:K(CombatJobSheet)",
        @"ShowClass:U Order:I DisplayName:S _:S CombatJobType:E CombatJobBeforeAdvancement:K(CombatJobSheet) CombatJobLineType:E
          _:E _:E _:E _:E _:K _:K _:S _:S _:K AvatarAnimationSymbol:E AvatarToolState:E");

    /// <summary>
    /// 애니메이션 상태 세트: (그룹, 조건, 상태 기호) → 클립 경로들. 조건은 "Any" 또는 "CombatJob:SwordMaster, BodyType:Fat" 같은 문자열이다.
    /// AssetPath 는 문자열 두 개(경로, 보조값)로 기록된다. 시작에 필요하지 않다.
    /// </summary>
    public static readonly SheetSchema AnimationStateSet = new SheetSchema("AnimationStateSet",
        "AnimationGroup:S Condition:S Symbol:E",
        "AssetPaths:[S+S]");

    /// <summary>
    /// 행동(Action). 꾸미기 포즈(Player*_Customize 의 Customize_Costume_04~06)에 쓴다. preload 세 번째 문자열은 조건("CombatJob:Warrior, BodyType:Fat" 등)이다.
    /// 클립 경로(AssetPath)는 문자열 두 개, 오프셋·페이드 시간(TimeSpan)은 double 이다. 시작에 필요하지 않다.
    /// </summary>
    public static readonly SheetSchema Action = new SheetSchema("Action",
        "ActionName:S GroupName:S Condition:S",
        @"_:N _:N ToolIndices:S HideWeapon:U _:S _:S _:S StartClipPath:S _:S _:D _:D LoopClipPath:S _:S _:D _:D _:D _:D
          _:U _:U _:E _:E _:U _:U _:U");

    /// <summary>무기 등 파츠 자체 클립: (파츠 애니메이션 그룹, 몸 클립 경로 또는 "Default") → 파츠 클립 경로.</summary>
    public static readonly SheetSchema ModelPartAnimation = new SheetSchema("ModelPartAnimation",
        "ModelPartGroupName:S ActorModelClipPath:S _:S",
        "ModelPartClipPath:S _:S");

    /// <summary>클립 이름 → 스테이지 타임라인 이름(StageTimelines/&lt;이름&gt;.StageTimeline). 꺼내기·넣기 중 무기 상태 구간(ToolState 트랙)을 읽는다.</summary>
    public static readonly SheetSchema StageTimeline = new SheetSchema("StageTimeline",
        "AnimationClipName:S",
        "TimelineOverrideConditionTag:S _:I StageTimelineNames:S");

    /// <summary>시작할 때 읽어야 하는 시트 전부.</summary>
    public static readonly SheetSchema[] Required =
    {
        Costume, CostumeModelVariant, CharacterPresetAppearance, CharacterHead, CharacterHair, CharacterEyes,
        CharacterMouth, CharacterBodyTypes, CharacterSkinColor, CharacterHairColor, CharacterEyeColor, CharacterMouthColor,
    };
}
