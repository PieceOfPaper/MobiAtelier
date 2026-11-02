// 꾸미기 장면 상태. 자동 복원 파일과 사용자 JSON 이 같은 구조를 쓴다(SaveLoad.md).
// UI 배치·앱 설정은 여기에 넣지 않는다(AppSettings).
// 표시 상태(미적용·가림)는 저장하지 않고 선택 ID 와 외형에서 다시 계산한다.
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class EditState
{
    public const int CurrentFormatVersion = 1;

    public int formatVersion = CurrentFormatVersion;
    /// <summary>작성한 앱 버전(참고용).</summary>
    public string appVersion;
    public AppearanceState appearance = new AppearanceState();
    public List<CostumeSlotState> costumes = new List<CostumeSlotState>();
    public CameraState camera = new CameraState();
    /// <summary>스튜디오 배경 시트(StudioBackground) Id(Background.md). 비어 있거나 고를 수 없는 ID 면 기본 배경으로 표시하고 값은 그대로 둔다.</summary>
    public string background;

    public CostumeSlotState Costume(string part)
    {
        foreach (var c in costumes) if (c.part == part) return c;
        return null;
    }

    public EditState Clone() => JsonUtility.FromJson<EditState>(JsonUtility.ToJson(this));
}

[Serializable]
public class AppearanceState
{
    /// <summary>시작에 사용한 CharacterPresetAppearance.Id (참고용). 외형 해석은 아래 개별 값으로 한다.</summary>
    public string preset;
    public string gender;
    public int age;
    public string bodyType;
    public float heightModifier;
    /// <summary>CharacterHead.Id</summary>
    public string head;
    /// <summary>CharacterHair.GroupId (체형별 행은 bodyType 으로 해석)</summary>
    public string hair;
    /// <summary>CharacterEyes.GroupId (머리형별 행은 head 로 해석)</summary>
    public string eyes;
    /// <summary>CharacterMouth.GroupId</summary>
    public string mouth;
    public string skinColor, hairColor, eyeColor, mouthColor;
    /// <summary>외형 색 슬롯(Skin, Hair, Hair1, Eye, Mouth)의 사용자 지정 색. 없으면 색상 시트 값을 쓴다.</summary>
    public List<ColorOverride> colors = new List<ColorOverride>();
}

[Serializable]
public class CostumeSlotState
{
    public const string KindCostume = "costume";
    public const string KindResource = "resource";

    /// <summary>착용 부위(Costume.CostumeType: Top, Bottom, Shoes ...)</summary>
    public string part;
    /// <summary>costume = 패션장비 시트 항목, resource = 시트에 없는 모델 리소스 직접 선택</summary>
    public string kind = KindCostume;
    public string costumeId;
    /// <summary>kind = resource 일 때 모델 정의 이름</summary>
    public string resource;
    /// <summary>슬롯(Slot0~5 등)별 사용자 지정 색. 없으면 의상 기본색(원본 머티리얼 + 시트 DyeColor)을 쓴다.</summary>
    public List<ColorOverride> dyes = new List<ColorOverride>();
}

[Serializable]
public class ColorOverride
{
    public string slot;
    /// <summary>#RRGGBB</summary>
    public string color;
}

[Serializable]
public class CameraState
{
    public bool valid;
    public float yaw, pitch, distance;
    public Vector2 pan;
}

public static class ColorOverrides
{
    public static string Get(List<ColorOverride> list, string slot)
    {
        if (list == null) return null;
        foreach (var c in list) if (c.slot == slot) return c.color;
        return null;
    }

    public static void Set(List<ColorOverride> list, string slot, string color)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i].slot == slot)
            {
                if (color == null) list.RemoveAt(i); else list[i].color = color;
                return;
            }
        if (color != null) list.Add(new ColorOverride { slot = slot, color = color });
    }

    /// <summary>#RRGGBB 만 받는다. 실패하면 false.</summary>
    public static bool TryParse(string hex, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return false;
        for (int i = 1; i < 7; i++) if (!Uri.IsHexDigit(hex[i])) return false;
        return ColorUtility.TryParseHtmlString(hex, out color);
    }

    public static string ToHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
}
