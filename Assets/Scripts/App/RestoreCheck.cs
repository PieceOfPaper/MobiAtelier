// 업데이트·제한 항목의 복원 검사 (SaveLoad.md "업데이트·제한 항목의 복원", Startup.md, Background.md 7장 2)
//   형식이 정상인 작업 파일을 지금 설치본·실행 환경으로 조립해 본 결과(조립 계획 + 실제 조립)에서 그대로 복원할 수 없는 항목을 찾는다.
//     없는 ID, 공개 빌드의 제한 항목(숨김 데이터·미공개 의상·직접 리소스), 리소스 읽기 실패(모델 정의·프리팹·메시),
//     사라진·해석되지 않는 염색 파트, 필수 외형의 기본값 대체(제한·누락 때문인 경우), 배경, 무기 자세 값.
//   정상적인 착용 불가(성별·나이에 맞지 않음)·다른 의상에 가림은 정상 상태라 여기에 넣지 않는다.
//   찾은 항목은 지우지 않는다. 부분 복원을 승인하면 선택 ID·색은 그대로 보존한 채 표시만 하지 않는다(필수 외형은 공개 기본값으로 대신 표시).
//   호출자는 새 캐릭터를 화면 밖에서 먼저 조립하고 이 검사를 한 뒤, 승인을 받은 경우에만 기존 장면을 교체한다.
using System.Collections.Generic;

public static class RestoreCheck
{
    /// <summary>복원할 수 없는 항목 설명 목록. 비어 있으면 모두 복원할 수 있다. plan 은 조립까지 마친 계획이어야 조립 결과 문제를 포함한다.</summary>
    public static List<string> Inspect(EditState s, GameData data, AssemblyPlan plan)
    {
        var issues = new List<string>();
        if (s == null || data == null) return issues;
        var a = s.appearance ?? new AppearanceState();
        void Add(string text) { if (!issues.Contains(text)) issues.Add(text); }

        if (!string.IsNullOrEmpty(a.bodyType) && !data.BodyTypes.ContainsKey(a.bodyType))
            Add($"체형 {a.bodyType}: 데이터에 없는 ID입니다. 기본 체형으로 표시합니다");

        if (plan != null)
            foreach (var i in plan.Issues)
            {
                if (!i.NeedsConsent) continue;
                var sub = plan.Substitute(i.Key);
                string text = i.Describe();
                if (sub != null) text += $". {ShownLabel(data, sub)}을(를) 대신 표시하고 원래 선택은 보관합니다";
                else if (i.Kind != IssueKind.DyeUnresolved) text += ". 선택은 보존하고 표시하지 않습니다";
                Add(text);
            }

        // 무기 자세(WeaponPose.md): 없는 직업 ID 는 공통 자세로, 모르는 자세 값은 넣은 자세로 대신 표시한다. 값은 지우지 않는다.
        var wp = s.weaponPose;
        if (wp != null && data.CombatJobs.Count > 0)
        {
            if (!string.IsNullOrEmpty(wp.job) && data.CombatJob(wp.job) == null)
                Add($"자세 직업 {wp.job}: 데이터에 없는 ID입니다. 공통 자세로 대신 표시합니다");
            if (!string.IsNullOrEmpty(wp.stance) && wp.stance != WeaponPoseState.Undrawn && !WeaponPoseState.IsDrawn(wp.stance))
                Add($"무기 자세 {wp.stance}: 알 수 없는 값입니다. 넣은 자세로 대신 표시합니다");
        }

        string bg = StudioBackground.UnavailableReason(data, s.background);
        if (bg != null) Add($"배경 {s.background}: {bg}. 기본 배경으로 대신 표시합니다");
        return issues;
    }

    /// <summary>새 작업·초기화의 기본 코디처럼 문제가 없어야 확정하는 상태의 캐릭터 문제(배경 제외).</summary>
    public static List<string> CharacterProblems(AssemblyPlan plan)
    {
        var list = new List<string>();
        if (plan != null) foreach (var i in plan.Issues) if (i.NeedsConsent) list.Add(i.Describe());
        return list;
    }

    /// <summary>대신 표시하는 값의 표기(`ID - 이름`).</summary>
    public static string ShownLabel(GameData data, AppearanceSubstitute sub)
    {
        string name = null;
        switch (sub.part)
        {
            case "head": if (data.Heads.TryGetValue(sub.shown ?? "", out var h)) name = h.DisplayName; break;
            case "hair": name = FirstName(data.Hairs, sub.shown); break;
            case "eyes": name = FirstName(data.Eyes, sub.shown); break;
            case "mouth": name = FirstName(data.Mouths, sub.shown); break;
            case "skinColor": if (data.SkinColors.TryGetValue(sub.shown ?? "", out var c1)) name = c1.DisplayName; break;
            case "hairColor": if (data.HairColors.TryGetValue(sub.shown ?? "", out var c2)) name = c2.DisplayName; break;
            case "eyeColor": if (data.EyeColors.TryGetValue(sub.shown ?? "", out var c3)) name = c3.DisplayName; break;
        }
        return "기본값 " + ItemLabel.Of(sub.shown, name);
    }

    static string FirstName(Dictionary<string, Dictionary<string, AppearancePartData>> map, string id)
    {
        if (id == null || !map.TryGetValue(id, out var d)) return null;
        foreach (var v in d.Values) if (!string.IsNullOrEmpty(v.DisplayName)) return v.DisplayName;
        return null;
    }

    /// <summary>안내 문구용. 너무 길면 앞부분만 보이고 나머지 개수를 붙인다.</summary>
    public static string Describe(List<string> issues, int max = 8)
    {
        var lines = new List<string>();
        for (int i = 0; i < issues.Count && i < max; i++) lines.Add("· " + issues[i]);
        if (issues.Count > max) lines.Add($"· 외 {issues.Count - max}개");
        return string.Join("\n", lines);
    }
}
