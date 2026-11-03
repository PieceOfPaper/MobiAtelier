// 새 작업 상태 만들기 (Startup.md "최초 실행").
using System;

public static class StateFactory
{
    /// <summary>최초 실행 후보. 두 프리셋을 같은 확률로 고른다(확정).</summary>
    public static readonly string[] DefaultPresets = { "MaleAppearance_Default_01", "FemaleAppearance_Default_01" };

    /// <summary>최초 착용 의상(Startup.md, 2026-11-02 확정). 남녀 같은 논리 ID 이며 성별 모델은 데이터에서 해석한다.</summary>
    public static readonly (string part, string id)[] StarterCostumes =
    {
        ("Top", "Fashion_NewbieSword_Top"),
        ("Bottom", "Fashion_NewbieSword_Bottom"),
        ("Shoes", "Fashion_NewbieSword_Shoes"),
    };

    public static EditState NewRandom(GameData data, Random random)
    {
        string id = DefaultPresets[random.Next(DefaultPresets.Length)];
        return WithStarterCostumes(data, FromPreset(data, id));
    }

    /// <summary>
    /// 코디 초기화 기준(Reset.md): 현재 성별의 기본 외형 + 초보자 의상, 사용자 염색 없음, 기본 카메라.
    /// 배경은 유지한다. 성별을 알 수 없으면 여성 기본 외형을 쓴다.
    /// </summary>
    public static EditState ResetFor(GameData data, EditState current)
    {
        bool male = string.Equals(current?.appearance?.gender, "Male", StringComparison.OrdinalIgnoreCase);
        var s = WithStarterCostumes(data, FromPreset(data, male ? DefaultPresets[0] : DefaultPresets[1]));
        s.background = current?.background;
        return s;
    }

    /// <summary>
    /// 초보자 의상을 입힌다. 시트에 없는 ID 도 그대로 넣는다. 조립 계획이 없는 ID 로 보고하므로
    /// 새 작업·초기화는 빈 의상을 정상 결과로 확정하지 않고 실패를 안내한다(Startup.md "최초 착용 의상").
    /// </summary>
    public static EditState WithStarterCostumes(GameData data, EditState s)
    {
        foreach (var (part, id) in StarterCostumes)
            s.costumes.Add(new CostumeSlotState { part = part, kind = CostumeSlotState.KindCostume, costumeId = id });
        return s;
    }

    /// <summary>현재 성별의 기본 외형 프리셋(필수 외형 대체값의 첫 후보). 없으면 null.</summary>
    public static PresetAppearanceData DefaultPreset(GameData data, bool male) =>
        data.Presets.TryGetValue(male ? DefaultPresets[0] : DefaultPresets[1], out var p) ? p : null;

    /// <summary>CharacterPresetAppearance 행의 외형·색 조합을 그대로 초기 상태로 쓴다. 의상은 비워 둔다(WithStarterCostumes 로 입힌다).</summary>
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
