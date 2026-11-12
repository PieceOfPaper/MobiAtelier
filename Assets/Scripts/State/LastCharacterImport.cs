// 마지막으로 접속한 캐릭터 불러오기(LastCharacterImport.md).
// 게임 사용자 데이터 폴더의 타이틀 화면 외형 캐시(TitleDisplay.bin, SJON) 하나만 읽기 전용으로 읽어 장면 상태로 바꾼다.
// 캐시 구조: { DisplayData: ActorDisplayData }
//   Age·Gender·HeightModifier, SkinColor(RGBA), BodyType·HeadType·HairType([번호, 시트 ID]),
//   BodyParts·Clothes·Accessories: [{ ModelParts: [모델 이름], DyeSlots: { DyeSlot 번호: RGBA } }],
//   Tools: { ToolIndex 번호: 같은 형식 } (-1 무기, 1~8 생활 도구, 9 악기, 10 연금 도구),
//   WeaponAuraGrade, IsRobeWeared·IsHoodOn·IsCapClosed·IsWeaponHidden, CostumeSet([번호, 시트 ID]).
// 시트 ID 가 있으면 그대로 쓰고, 모델 이름·원색만 있으면 시트를 거꾸로 찾는다(AGENTS.md 역방향 조회).
// 찾지 못한 부위는 장착하지 않고 Failed 에, 아틀리에가 아직 지원하지 않는 항목은 Unsupported 에 적는다.
// 캐릭터 ID 등 계정 정보는 읽지도 저장하지도 않는다.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class LastCharacterImport
{
    // DyeSlot 번호(게임 열거형): 0~5 Slot0~5, 6 Skin, 7 Hair, 8 Eye, 9 Eyebrow, 10 Mouth, 13 Beard, 14 FaceTattoo, 15 BodyTattoo, 17 Hair1, 18 HairAccessory
    const string DyeHair = "7", DyeEye = "8", DyeHair1 = "17", DyeHairAccessory = "18";
    const string ToolWeapon = "-1";

    public sealed class Report
    {
        /// <summary>불러오지 못해 장착하지 않은 부위: (부위 이름, 사유)</summary>
        public readonly List<(string part, string reason)> Failed = new List<(string, string)>();
        /// <summary>아틀리에가 아직 지원하지 않아 불러오지 않은 항목(ImplementationGaps.md)</summary>
        public readonly List<string> Unsupported = new List<string>();
        /// <summary>시트에 같은 색이 없어 가장 가까운 색으로 고른 항목</summary>
        public readonly List<string> Approximated = new List<string>();
    }

    /// <summary>게임 사용자 데이터 폴더의 캐시 파일 경로. 시험용으로 -maTitleDisplay &lt;파일&gt; 을 받는다.</summary>
    public static string CachePath()
    {
        string over = CommandLine.Value("-maTitleDisplay");
        if (!string.IsNullOrEmpty(over)) return over;
        // %USERPROFILE%\AppData\LocalLow\devCAT\마비노기 모바일\TitleDisplay.bin
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Path.GetDirectoryName(local.TrimEnd(Path.DirectorySeparatorChar));
        return Path.Combine(appData ?? "", "LocalLow", "devCAT", "마비노기 모바일", "TitleDisplay.bin");
    }

    /// <summary>캐시를 읽어 새 장면 상태를 만든다. 파일이 없거나 형식이 다르면 예외(메시지는 사용자 안내용).</summary>
    public static EditState Load(string path, GameData data, EditState current, out Report report)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("게임 캐시 파일이 없습니다. 게임에서 캐릭터로 한 번 접속한 뒤 다시 시도해 주세요.", path);
        object root;
        try { root = Sjon.Decode(File.ReadAllBytes(path)); }
        catch (Exception e) when (!(e is OutOfMemoryException)) { throw new InvalidDataException("게임 캐시 파일을 해석하지 못했습니다: " + e.Message); }
        var d = root.Get("DisplayData");
        if (!(d is Dictionary<string, object>) || d.Get("Gender").Str() == null)
            throw new InvalidDataException("게임 캐시 파일의 형식이 예상과 다릅니다. 게임 업데이트로 바뀌었을 수 있습니다.");
        return Convert(d, data, current, out report);
    }

    public static EditState Convert(object d, GameData data, EditState current, out Report report)
    {
        report = new Report();
        var r = report;
        var s = new EditState();
        var a = s.appearance;
        a.gender = d.Get("Gender").Str() == "Male" ? "Male" : "Female";
        bool male = a.gender == "Male";
        a.age = d.Get("Age").I();
        bool infant = a.age > 0 && a.age < PlanResolver.AdultMinAge;
        a.heightModifier = d.Get("HeightModifier").F();
        var preset = StateFactory.DefaultPreset(data, male);
        a.preset = null;
        // 입 색은 다루지 않는다(Appearance.md). 기본 프리셋 값을 둔다.
        a.mouthColor = preset?.MouthColor;

        // 시트 ID 가 그대로 있는 항목
        a.bodyType = SheetId(d, "BodyType", id => data.BodyTypes.ContainsKey(id), "체형", r);
        a.head = SheetId(d, "HeadType", id => data.Heads.ContainsKey(id), "얼굴형", r);
        a.hair = SheetId(d, "HairType", id => data.Hairs.ContainsKey(id), "헤어스타일", r);
        if (a.head != null && data.Heads.TryGetValue(a.head, out var headRow) && Hidden(headRow)) { r.Failed.Add(("얼굴형", "숨김 항목")); a.head = null; }
        if (a.hair != null && Hidden(GameData.Resolve(data.Hairs, a.hair, a.bodyType ?? "Default"))) { r.Failed.Add(("헤어스타일", "숨김 항목")); a.hair = null; }

        // 피부색: 원색 → 가장 가까운 시트 색
        if (TryColor(d.Get("SkinColor"), out var skin))
            a.skinColor = Nearest(data.SkinColors, c => Dist(c.Color1, skin), "피부색", r);

        // 외형 파츠(모델 이름) → 시트 거꾸로 찾기
        foreach (var part in d.Get("BodyParts").Arr())
        {
            string model = FirstModel(part);
            if (model == null) continue;
            var dyes = part.Get("DyeSlots");
            if (FindGroup(data.Hairs, model) != null)
            {
                // 헤어 색: Hair(7)·Hair1(17) → 가장 가까운 헤어 색 행. 헤어 장식 색(18)은 다루지 않는다.
                if (TryColor(dyes.Get(DyeHair), out var h1))
                {
                    bool two = TryColor(dyes.Get(DyeHair1), out var h2);
                    if (!two) h2 = h1;
                    a.hairColor = Nearest(data.HairColors, c => Dist(c.Color1, h1) + Dist(c.Type == "TwoColor" ? c.Color2 : c.Color1, h2), "헤어 색상", r);
                }
                if (TryColor(dyes.Get(DyeHairAccessory), out _) && !r.Unsupported.Contains("헤어 장식 색")) r.Unsupported.Add("헤어 장식 색");
                continue;
            }
            // 머리 모델은 HeadType 으로 이미 정해진다.
            if (ModelInHeads(data, model)) continue;
            string g;
            if ((g = FindGroup(data.Eyes, model)) != null)
            {
                a.eyes = Visible(GameData.Resolve(data.Eyes, g, a.head ?? "Head_00", "Head_00"), g, "눈 모양", r);
                if (TryColor(dyes.Get(DyeEye), out var eye)) a.eyeColor = Nearest(data.EyeColors, c => Dist(c.Color1, eye), "눈 색상", r);
                continue;
            }
            if ((g = FindGroup(data.Mouths, model)) != null) { a.mouth = Visible(GameData.Resolve(data.Mouths, g, a.head ?? "Head_00", "Head_00"), g, "입 모양", r); continue; }
            if ((g = FindDeco(data.Beards, model)) != null) { a.beard = Visible(data.Beards[g], g, "수염", r); continue; }
            if ((g = FindDeco(data.FaceDecos, model)) != null) { a.faceDeco = Visible(data.FaceDecos[g], g, "얼굴꾸밈", r); continue; }
            if ((g = FindDeco(data.BodyDecos, model)) != null) { a.bodyDeco = Visible(data.BodyDecos[g], g, "몸꾸밈", r); continue; }
            r.Failed.Add(("외형 파츠", "데이터에서 찾지 못한 모델 " + model));
        }
        if (a.eyes == null && !r.Failed.Exists(f => f.part == "눈 모양")) r.Failed.Add(("눈 모양", "캐시에 없음"));
        if (a.mouth == null && !r.Failed.Exists(f => f.part == "입 모양")) r.Failed.Add(("입 모양", "캐시에 없음"));

        // 의상(모델 이름) → 패션장비 거꾸로 찾기
        var index = CostumeIndex(data);
        void Wear(object part, string label)
        {
            var models = Models(part);
            if (models.Count == 0) return;
            var c = PickCostume(data, index, models, male, infant, part.Get("DyeSlots"), out string why);
            if (c == null) { r.Failed.Add((label, why + " (" + models[0] + ")")); return; }
            if (s.costumes.Exists(x => x.part == c.CostumeType))
            {
                r.Failed.Add((AssemblyPlan.PartLabel(c.CostumeType), "같은 부위 의상이 이미 있어 " + ItemLabel.Of(c.Id, c.DisplayName) + "을(를) 장착하지 않습니다"));
                return;
            }
            var slot = new CostumeSlotState { part = c.CostumeType, kind = CostumeSlotState.KindCostume, costumeId = c.Id };
            var dyes = part.Get("DyeSlots") as Dictionary<string, object>;
            if (dyes != null)
                foreach (var kv in dyes)
                {
                    if (!int.TryParse(kv.Key, out int k) || k < 0 || k > 5 || !TryColor(kv.Value, out var col)) continue;
                    var def = c.DyeColors[k];
                    // 시트 기본색과 같으면 사용자 색으로 남기지 않는다(기본색 유지).
                    if (def.HasValue && def.Value.R == col.r32 && def.Value.G == col.g32 && def.Value.B == col.b32) continue;
                    slot.dyes.Add(new ColorOverride { slot = "Slot" + k, color = ColorOverrides.ToHex(col.color) });
                }
            s.costumes.Add(slot);
        }
        foreach (var part in d.Get("Clothes").Arr()) Wear(part, "의상");
        foreach (var part in d.Get("Accessories").Arr()) Wear(part, "장신구");

        // 무기·도구
        var tools = d.Get("Tools") as Dictionary<string, object>;
        var otherTools = new List<string>();
        if (tools != null)
            foreach (var kv in tools)
            {
                if (Models(kv.Value).Count == 0) continue;
                if (kv.Key == ToolWeapon)
                {
                    if (d.Get("IsWeaponHidden").B()) continue;
                    Wear(kv.Value, "무기");
                }
                else otherTools.Add(kv.Key == "9" ? "악기" : "생활 도구");
            }
        if (otherTools.Contains("생활 도구")) r.Unsupported.Add("생활 도구");
        if (otherTools.Contains("악기")) r.Unsupported.Add("악기");

        // 무기 자세 직업: 캐릭터의 현재 직업(애니메이션 조건). 데이터에 없으면 무기 외형의 원래 직업(자동).
        string job = d.Get("CombatJobAnimationCondition").Str();
        s.weaponPose.job = job != null && data.CombatJob(job) != null ? job : null;
        s.weaponPose.stance = WeaponPoseState.Undrawn;

        string aura = d.Get("WeaponAuraGrade").Str();
        if (!string.IsNullOrEmpty(aura) && aura != "None" && aura != "Common" && s.Costume("Weapon") != null) r.Unsupported.Add("무기 오라");
        var set = d.Get("CostumeSet").Arr();
        if (set.Count >= 2 && set[1].Str() is string setId && setId != "Invalid" && setId != "") r.Unsupported.Add("세트 효과");
        if (d.Get("IsCapClosed").B() && s.Costume("Hat") != null) r.Unsupported.Add("모자 접힘 상태");
        if (d.Get("IsHoodOn").B()) r.Unsupported.Add("후드 씀 상태");

        // 배경은 현재 선택을 유지한다. 카메라는 기본 구도로 맞춘다.
        s.background = current?.background;
        return s;
    }

    // ------------------------------------------------------------ 시트 ID

    static string SheetId(object d, string field, Func<string, bool> exists, string label, Report r)
    {
        var v = d.Get(field);
        string id = v is List<object> l && l.Count >= 2 ? l[1].Str() : v.Str();
        if (string.IsNullOrEmpty(id) || id == "Invalid") { r.Failed.Add((label, "캐시에 없음")); return null; }
        if (!exists(id)) { r.Failed.Add((label, "데이터에 없는 ID " + id)); return null; }
        return id;
    }

    static bool Hidden(AppearancePartData p) => p != null && p.HideOnCustomize && !Disclosure.AllowRestricted;

    static string Visible(AppearancePartData p, string id, string label, Report r)
    {
        if (p == null) { r.Failed.Add((label, "데이터에 없는 ID " + id)); return null; }
        if (Hidden(p)) { r.Failed.Add((label, "숨김 항목 " + ItemLabel.Of(id, p.DisplayName))); return null; }
        return id;
    }

    // ------------------------------------------------------------ 모델 이름 → 시트

    static string FirstModel(object part)
    {
        var m = Models(part);
        return m.Count > 0 ? m[0] : null;
    }

    static List<string> Models(object part)
    {
        var list = new List<string>();
        foreach (var m in part.Get("ModelParts").Arr())
            if (m.Str() is string name && name != "" && name != "Invalid") list.Add(name);
        return list;
    }

    static bool Has(AppearancePartData p, string model) =>
        p.MaleModel == model || p.FemaleModel == model || p.InfantMaleModel == model || p.InfantFemaleModel == model;

    static string FindGroup(Dictionary<string, Dictionary<string, AppearancePartData>> map, string model)
    {
        foreach (var kv in map) foreach (var v in kv.Value.Values) if (Has(v, model)) return kv.Key;
        return null;
    }

    static string FindDeco(Dictionary<string, AppearancePartData> map, string model)
    {
        // 같은 모델을 쓰는 행이 여럿이면(예: 듀얼 크로스 숨김·공개 두 행) 공개 행을 먼저 고른다.
        string hidden = null;
        foreach (var kv in map)
        {
            if (!Has(kv.Value, model)) continue;
            if (!kv.Value.HideOnCustomize) return kv.Key;
            hidden ??= kv.Key;
        }
        return hidden;
    }

    static bool ModelInHeads(GameData data, string model)
    {
        foreach (var h in data.Heads.Values) if (Has(h, model)) return true;
        return false;
    }

    /// <summary>모델 이름 → 그 모델을 쓰는 패션장비 행(성별·나이 열 표시). 같은 모델을 여러 행이 공유할 수 있다.</summary>
    static Dictionary<string, List<(CostumeData row, char col)>> CostumeIndex(GameData data)
    {
        var idx = new Dictionary<string, List<(CostumeData, char)>>(StringComparer.Ordinal);
        void Add(string m, CostumeData c, char col)
        {
            if (string.IsNullOrEmpty(m) || m == "Invalid") return;
            if (!idx.TryGetValue(m, out var l)) idx[m] = l = new List<(CostumeData, char)>();
            l.Add((c, col));
        }
        foreach (var c in data.CostumeList)
        {
            foreach (var m in c.MaleModels) Add(m, c, 'M');
            foreach (var m in c.FemaleModels) Add(m, c, 'F');
            foreach (var m in c.BoyModels) Add(m, c, 'B');
            foreach (var m in c.GirlModels) Add(m, c, 'G');
            foreach (var v in c.ExceptionModels)
                if (v != null && data.ModelVariants.TryGetValue(v, out var row))
                {
                    Add(row.Str("MaleModelName"), c, 'M'); Add(row.Str("FemaleModelName"), c, 'F');
                    Add(row.Str("BoyModelName"), c, 'B'); Add(row.Str("GirlModelName"), c, 'G');
                }
        }
        return idx;
    }

    /// <summary>
    /// 같은 모델을 쓰는 패션장비가 여럿이면 다음 순서로 고른다(LastCharacterImport.md "의상 후보 결정").
    ///   1. 지금 캐릭터의 성별·나이 열에 그 모델이 있는 행  2. 착용 가능 성별이 맞는 행  3. 공개 행(일반 빌드는 공개 행만)
    ///   4. 이름이 있고 '&lt;' 로 시작하지 않는 행(&lt;사용하지 않음&gt; 등)  5. 시트 기본 염색이 캐시 색과 가장 가까운 행  6. 시트 순서
    /// 여러 모델로 된 의상(검+방패 등)은 모든 모델을 가진 행만 후보로 본다.
    /// </summary>
    static CostumeData PickCostume(GameData data, Dictionary<string, List<(CostumeData row, char col)>> index, List<string> models,
        bool male, bool infant, object dyes, out string why)
    {
        why = null;
        if (!index.TryGetValue(models[0], out var cands)) { why = "패션장비 데이터에서 찾지 못한 모델"; return null; }
        char want = infant ? (male ? 'B' : 'G') : (male ? 'M' : 'F');
        CostumeData best = null;
        long bestScore = long.MinValue;
        bool anyHidden = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (row, col) in cands)
        {
            if (!seen.Add(row.Id + col)) continue;
            bool allModels = true;
            for (int i = 1; i < models.Count; i++)
                if (!index.TryGetValue(models[i], out var l2) || !l2.Exists(x => x.row == row)) allModels = false;
            if (!allModels) continue;
            bool hidden = Disclosure.IsHidden(row);
            if (hidden && !Disclosure.AllowRestricted) { anyHidden = true; continue; }
            long score = 0;
            if (col == want) score += 1L << 40;
            bool genderOk = !((row.EquippableGender == "Male" && !male) || (row.EquippableGender == "Female" && male));
            if (genderOk) score += 1L << 39;
            if (!hidden) score += 1L << 38;
            if (!string.IsNullOrWhiteSpace(row.DisplayName) && !row.DisplayName.StartsWith("<", StringComparison.Ordinal)) score += 1L << 37;
            score -= DyeDistance(row, dyes) * 1000L;
            score -= row.Index;
            if (score > bestScore) { bestScore = score; best = row; }
        }
        if (best == null) why = anyHidden ? "숨김 항목이라 불러오지 않음" : "같은 모델 구성의 패션장비를 찾지 못함";
        return best;
    }

    static long DyeDistance(CostumeData row, object dyes)
    {
        long sum = 0;
        if (!(dyes is Dictionary<string, object> map)) return 0;
        foreach (var kv in map)
        {
            if (!int.TryParse(kv.Key, out int k) || k < 0 || k > 5 || !TryColor(kv.Value, out var col)) continue;
            var def = row.DyeColors[k];
            sum += def.HasValue ? (long)Dist(def, col) : 3 * 255 * 255;
        }
        return sum;
    }

    // ------------------------------------------------------------ 색

    struct Rgb
    {
        public byte r32, g32, b32;
        public Color color => new Color32(r32, g32, b32, 255);
    }

    static bool TryColor(object v, out Rgb c)
    {
        c = default;
        var l = v.Arr();
        if (l.Count < 3) return false;
        c = new Rgb { r32 = (byte)Mathf.Clamp(l[0].I(), 0, 255), g32 = (byte)Mathf.Clamp(l[1].I(), 0, 255), b32 = (byte)Mathf.Clamp(l[2].I(), 0, 255) };
        return true;
    }

    static int Dist(Rgba32? a, Rgb b)
    {
        if (!a.HasValue) return 3 * 255 * 255;
        int dr = a.Value.R - b.r32, dg = a.Value.G - b.g32, db = a.Value.B - b.b32;
        return dr * dr + dg * dg + db * db;
    }

    /// <summary>시트 색 중 가장 가까운 공개 색(일반 빌드는 꾸미기 숨김 제외). 정확히 같지 않으면 근사로 기록한다.</summary>
    static string Nearest(Dictionary<string, ColorData> map, Func<ColorData, int> dist, string label, Report r)
    {
        string best = null; int bestD = int.MaxValue;
        foreach (var c in map.Values)
        {
            if (c.HideOnCustomize && !Disclosure.AllowRestricted) continue;
            int dd = dist(c);
            if (dd < bestD) { bestD = dd; best = c.Id; }
        }
        if (best == null) r.Failed.Add((label, "고를 수 있는 색이 없음"));
        else if (bestD > 0) r.Approximated.Add(label + ": " + ItemLabel.Of(best, map[best].DisplayName));
        return best;
    }
}
