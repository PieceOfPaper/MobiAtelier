// 무기 오라: 무기 등급 외곽선 색(Equipment.md "무기 오라"). UnityEngine 에 의존하지 않는다.
//   StageConstantSettings/StageConstants.sjon.bin 의 WeaponGradeOutlineColorSettings(MainColor·SubColor, 키 = Rarity 이름).
//   게임은 무기 재질의 _WeaponGradeColor 에 (MainColor.rgb, 등급 번호), _WeaponGradeColor2 에 SubColor 를 넣는다.
//   외곽선은 등급 번호 4(엘리트) 이상만 그린다(원본 셰이더 디스어셈블, MobiCharacter.shader 머리말).
// 파일이 없거나 형식이 다르면 조사 시점(2026-11-18)의 설치본 값으로 대신한다.
using System;
using System.Collections.Generic;

public sealed class WeaponAuraGrade
{
    /// <summary>Rarity 이름(Elite, Epic ...). 상태 파일에 이 값을 저장한다.</summary>
    public string Id;
    /// <summary>게임 Rarity 문자열 표의 이름(엘리트, 에픽 ...).</summary>
    public string Name;
    /// <summary>Rarity 열거형 번호. 셰이더 _WeaponGradeColor.w 로 넘긴다.</summary>
    public int Index;
    /// <summary>RGBA 0~1(sRGB). 셰이더에는 rgb 만 쓴다.</summary>
    public float[] Main = new float[4], Sub = new float[4];
}

public sealed class WeaponAuraData
{
    public const string ConstantsPath = "StageConstantSettings/StageConstants.sjon.bin";
    /// <summary>외곽선을 그리는 가장 낮은 등급 번호(엘리트).</summary>
    public const int MinVisibleIndex = 4;

    /// <summary>Rarity 열거형 순서(번호 = 배열 위치)와 게임 문자열 표(Rarity) 이름 [확인: 설치본 문자열 표, 2026-11-18].</summary>
    public static readonly (string id, string name)[] Rarities =
    {
        ("Invalid", ""), ("Common", "일반"), ("Uncommon", "고급"), ("Rare", "레어"), ("Elite", "엘리트"), ("Epic", "에픽"),
        ("Legendary", "전설"), ("Unique", "유니크"), ("Legendary2", "전설+"), ("Legendary3", "전설++"), ("Mythic", "신화"),
    };

    /// <summary>외곽선을 그리는 등급(번호 순).</summary>
    public readonly List<WeaponAuraGrade> Grades = new List<WeaponAuraGrade>();
    /// <summary>읽지 못한 항목(없으면 null). 대체값으로 동작한다.</summary>
    public string Error;

    public static int IndexOf(string id)
    {
        for (int i = 0; i < Rarities.Length; i++) if (string.Equals(Rarities[i].id, id, StringComparison.Ordinal)) return i;
        return -1;
    }

    public static string NameOf(string id)
    {
        int i = IndexOf(id);
        return i >= 0 ? Rarities[i].name : id;
    }

    /// <summary>외곽선을 그리는 등급. 없거나 낮은 등급(일반~레어)은 null.</summary>
    public WeaponAuraGrade Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var g in Grades) if (g.Id == id) return g;
        return null;
    }

    public static WeaponAuraData Defaults()
    {
        var d = new WeaponAuraData();
        // 설치본 값 [확인: 2026-11-18]. SubColor 는 전설+·전설++ 만 있다.
        float[][] main =
        {
            new[] { 0.5568628f, 0.345098f, 1f }, new[] { 0.8901961f, 0.1921569f, 0.6352941f }, new[] { 0.8941177f, 0.4117647f, 0.1529412f },
            new[] { 0.9490196f, 0.9176471f, 0.8235294f }, new[] { 0.8941177f, 0.4117647f, 0.1529412f }, new[] { 0.8823529f, 0.7058824f, 0.09803922f },
            new[] { 0.8823529f, 0.7058824f, 0.09803922f },
        };
        for (int i = MinVisibleIndex; i < Rarities.Length; i++)
        {
            var g = new WeaponAuraGrade { Id = Rarities[i].id, Name = Rarities[i].name, Index = i };
            Array.Copy(main[i - MinVisibleIndex], g.Main, 3);
            if (g.Id == "Legendary2" || g.Id == "Legendary3") { g.Sub[0] = 0.1490196f; g.Sub[1] = 0.8901961f; g.Sub[2] = 0.2f; }
            d.Grades.Add(g);
        }
        return d;
    }

    public static WeaponAuraData Load(InstallFiles files)
    {
        var d = Defaults();
        try
        {
            var s = files.ReadSjon(ConstantsPath).Get("WeaponGradeOutlineColorSettings");
            if (s == null) throw new FormatException("WeaponGradeOutlineColorSettings 가 없습니다");
            var main = s.Get("MainColor");
            var sub = s.Get("SubColor");
            if (main == null) throw new FormatException("MainColor 가 없습니다");
            foreach (var g in d.Grades)
            {
                Read(main.Get(g.Id), g.Main);
                Read(sub?.Get(g.Id), g.Sub);
            }
        }
        catch (Exception e) { d.Error = "무기 등급 색: " + e.Message; }
        return d;
    }

    static void Read(object o, float[] into)
    {
        var l = o.Arr();
        if (l.Count < 3) return;
        for (int i = 0; i < 4 && i < l.Count; i++) into[i] = l[i].F();
    }
}
