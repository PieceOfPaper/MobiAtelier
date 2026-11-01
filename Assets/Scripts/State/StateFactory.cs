// 새 작업 상태 만들기 (Startup.md "최초 실행").
using System;

public static class StateFactory
{
    /// <summary>최초 실행 후보. 두 프리셋을 같은 확률로 고른다(확정).</summary>
    public static readonly string[] DefaultPresets = { "MaleAppearance_Default_01", "FemaleAppearance_Default_01" };

    public static EditState NewRandom(GameData data, Random random)
    {
        string id = DefaultPresets[random.Next(DefaultPresets.Length)];
        return FromPreset(data, id);
    }

    /// <summary>CharacterPresetAppearance 행의 외형·색 조합을 그대로 초기 상태로 쓴다. 의상은 비워 둔다(최초 착용 의상은 미정).</summary>
    public static EditState FromPreset(GameData data, string presetId)
    {
        if (!data.Presets.TryGetValue(presetId, out var p)) throw new ArgumentException("외형 프리셋을 찾을 수 없습니다: " + presetId);
        var s = new EditState();
        var a = s.appearance;
        a.preset = p.Id;
        a.gender = p.Gender;
        a.age = p.Age;
        a.bodyType = p.BodyType;
        a.heightModifier = p.HeightModifier;
        a.head = p.HeadType;
        a.hair = p.Hair;
        a.eyes = p.Eyes;
        a.mouth = p.Mouth;
        a.skinColor = p.SkinColor;
        a.hairColor = p.HairColor;
        a.eyeColor = p.EyeColor;
        a.mouthColor = p.MouthColor;
        return s;
    }
}
