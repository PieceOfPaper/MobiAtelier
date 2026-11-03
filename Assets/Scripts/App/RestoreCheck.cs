// 업데이트·제한 항목의 복원 검사 (SaveLoad.md "업데이트·제한 항목의 복원", Startup.md, Background.md 7장 2)
//   형식이 정상인 작업 파일에서 지금 설치본으로 그대로 복원할 수 없는 항목을 찾는다.
//   찾은 항목은 지우지 않는다. 부분 복원을 승인하면 선택 ID·색은 그대로 보존한 채 표시만 하지 않는다(또는 기본값으로 대체 표시).
//   염색 파트 변경(사라진 슬롯)은 모델을 조립해야 알 수 있어 이 검사에 넣지 않는다.
using System.Collections.Generic;

public static class RestoreCheck
{
    /// <summary>복원할 수 없는 항목 설명 목록. 비어 있으면 모두 복원할 수 있다.</summary>
    public static List<string> Inspect(EditState s, GameData data)
    {
        var issues = new List<string>();
        if (s == null || data == null) return issues;
        var a = s.appearance ?? new AppearanceState();

        void Need<T>(Dictionary<string, T> map, string id, string label)
        {
            if (!string.IsNullOrEmpty(id) && !map.ContainsKey(id)) issues.Add($"{label} {id}: 데이터에 없는 ID입니다");
        }
        if (!string.IsNullOrEmpty(a.bodyType) && !data.BodyTypes.ContainsKey(a.bodyType)) issues.Add($"체형 {a.bodyType}: 데이터에 없는 ID입니다");
        Need(data.Heads, a.head, "얼굴형");
        Need(data.Hairs, a.hair, "헤어스타일");
        Need(data.Eyes, a.eyes, "눈 모양");
        Need(data.Mouths, a.mouth, "입 모양");
        Need(data.SkinColors, a.skinColor, "피부색");
        Need(data.HairColors, a.hairColor, "헤어 색상");
        Need(data.EyeColors, a.eyeColor, "눈 색상");

        if (s.costumes != null)
            foreach (var c in s.costumes)
            {
                if (c == null) continue;
                if (c.kind == CostumeSlotState.KindResource)
                {
#if !UNITY_EDITOR
                    issues.Add($"{PartLabel(c.part)} {c.resource}: 시트 없는 직접 리소스는 에디터 전용이라 공개 빌드에서 표시하지 않습니다");
#endif
                    continue;
                }
                if (!string.IsNullOrEmpty(c.costumeId) && !data.Costumes.ContainsKey(c.costumeId))
                    issues.Add($"{PartLabel(c.part)} {c.costumeId}: 패션장비 데이터에 없는 ID입니다");
            }

        // 무기 자세(WeaponPose.md): 없는 직업 ID 는 공통 자세로, 모르는 자세 값은 넣은 자세로 대신 표시한다. 값은 지우지 않는다.
        var wp = s.weaponPose;
        if (wp != null && data.CombatJobs.Count > 0)
        {
            if (!string.IsNullOrEmpty(wp.job) && data.CombatJob(wp.job) == null)
                issues.Add($"자세 직업 {wp.job}: 데이터에 없는 ID입니다. 공통 자세로 대신 표시합니다");
            if (!string.IsNullOrEmpty(wp.stance) && wp.stance != WeaponPoseState.Undrawn && !WeaponPoseState.IsDrawn(wp.stance))
                issues.Add($"무기 자세 {wp.stance}: 알 수 없는 값입니다. 넣은 자세로 대신 표시합니다");
        }

        string bg = StudioBackground.UnavailableReason(data, s.background);
        if (bg != null) issues.Add($"배경 {s.background}: {bg}. 기본 배경으로 대신 표시합니다");
        return issues;
    }

    static readonly Dictionary<string, string> PartLabels = new Dictionary<string, string>
    {
        { "Hat", "모자" }, { "Top", "상의" }, { "Bottom", "하의" }, { "Robe", "로브" }, { "Gloves", "장갑" },
        { "Shoes", "신발" }, { "EarDeco", "귀장식" }, { "EyeDeco", "눈장식" }, { "FaceDeco", "얼굴장식" }, { "Weapon", "무기" },
    };

    static string PartLabel(string part) => part != null && PartLabels.TryGetValue(part, out var l) ? l : part;

    /// <summary>안내 문구용. 너무 길면 앞부분만 보이고 나머지 개수를 붙인다.</summary>
    public static string Describe(List<string> issues, int max = 8)
    {
        var lines = new List<string>();
        for (int i = 0; i < issues.Count && i < max; i++) lines.Add("· " + issues[i]);
        if (issues.Count > max) lines.Add($"· 외 {issues.Count - max}개");
        return string.Join("\n", lines);
    }
}
