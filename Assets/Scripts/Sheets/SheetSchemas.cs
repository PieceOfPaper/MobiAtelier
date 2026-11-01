// 앱이 읽는 시트의 필드 순서. 시트 파일에는 스키마가 없으므로 게임의 시트 로더가 BinaryDecoder 를 호출하는 순서를 그대로 적는다.
// 표기: "이름:연산" 을 공백으로 구분한다. 배열은 "이름:[연산]". 읽기만 하고 버릴 필드는 이름을 "_" 로 쓴다.
//   I VariantInt / U VariantUInt(bool 포함) / S 문자열 / E 열거형(문자열) / N VariantNumber / C Color32? / B byte
//   K(표) 심볼 ID(u32). 괄호 안은 SymbolTables/<표>_SymbolTable 의 표 이름
// 게임 업데이트로 순서가 바뀌면 SheetSet.Read 가 행 크기 불일치로 실패한다. 이때 여기를 고친다.
using System;
using System.Collections.Generic;

public readonly struct SheetField
{
    public readonly string Name;
    public readonly char Op;
    public readonly string Symbol;
    public readonly bool IsArray;
    public SheetField(string name, char op, string symbol, bool isArray) { Name = name; Op = op; Symbol = symbol; IsArray = isArray; }
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
            string sym = null;
            int p = op.IndexOf('(');
            if (p >= 0) { sym = op.Substring(p + 1, op.Length - p - 2); op = op.Substring(0, p); }
            if (op.Length != 1) throw new ArgumentException("시트 스키마 표기 오류: " + tok);
            outp.Add(new SheetField(name, op[0], sym, arr));
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
          _:[K(CombatJobSheet)] EquippableGender:E EquippableAgeType:E _:[K(MagicEffectSheet)] _:E
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

    /// <summary>시작할 때 읽어야 하는 시트 전부.</summary>
    public static readonly SheetSchema[] Required =
    {
        Costume, CostumeModelVariant, CharacterPresetAppearance, CharacterHead, CharacterHair, CharacterEyes,
        CharacterMouth, CharacterBodyTypes, CharacterSkinColor, CharacterHairColor, CharacterEyeColor, CharacterMouthColor,
    };
}
