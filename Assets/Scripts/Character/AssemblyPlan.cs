// 장면 상태(EditState) + 시트(GameData) → 조립할 모델과 색의 목록.
// 선택과 실제 출력을 분리한다(Equipment.md). 입을 수 없는 의상은 선택을 유지한 채 상태만 표시한다.
using System;
using System.Collections.Generic;
using UnityEngine;

public enum PartRole { Body, Head, Hair, Eyes, Mouth, Costume }

public enum CostumeStatus
{
    Applied,
    /// <summary>다른 착용 의상의 HidingParts 로 가려짐</summary>
    HiddenByOther,
    /// <summary>현재 성별·나이에 맞는 모델이 없음</summary>
    NoModelForAppearance,
    /// <summary>착용 가능 성별이 다름</summary>
    GenderMismatch,
    /// <summary>시트에 없는 ID</summary>
    UnknownId,
    /// <summary>모델 정의를 설치 폴더에서 찾지 못함</summary>
    MissingResource,
}

public sealed class PartPlan
{
    /// <summary>body, head, hair, eyes, mouth, costume:&lt;부위&gt;</summary>
    public string Key;
    public PartRole Role;
    public string Model;
    /// <summary>이 파츠만의 슬롯 색(Slot0~5). 시트 기본색 위에 사용자 색을 덮은 결과.</summary>
    public readonly Dictionary<string, Color> SlotColors = new Dictionary<string, Color>(StringComparer.Ordinal);
}

public sealed class CostumeResult
{
    public string Part, CostumeId, DisplayName, Detail;
    public CostumeStatus Status;
    public List<string> Models = new List<string>();
}

public sealed class AssemblyPlan
{
    public readonly List<PartPlan> Parts = new List<PartPlan>();
    /// <summary>외형 색 슬롯(Skin, Hair, Hair1, Eye, Mouth). 모든 파츠의 같은 이름 슬롯에 적용한다.</summary>
    public readonly Dictionary<string, Color> GlobalColors = new Dictionary<string, Color>(StringComparer.Ordinal);
    public readonly List<CostumeResult> Costumes = new List<CostumeResult>();
    public readonly List<string> Notes = new List<string>();
    public bool Male, Infant;
    /// <summary>나이와 키 조절값(-1 작게 ~ 0 보통 ~ 1 크게). 몸 .bdc 의 본 스케일·위치를 고른다.</summary>
    public int Age;
    public float HeightModifier;
    public string BodyType = "Default";
    /// <summary>체형 블렌드셰이프 이름. null 이면 기본 체형.</summary>
    public string BlendShape;
    public string Pose;
    /// <summary>기본 아이들 모션(커스터마이즈 서 있기) 클립이 든 .fbx 에셋 경로. null 이면 정지 포즈만 쓴다.</summary>
    public string Idle;

    public static string CostumeKey(string part) => "costume:" + part;
}

public static class PlanResolver
{
    /// <summary>성인 몸 .bdc 는 14~17세, 어린 몸(Infant*) .bdc 는 10~13세 데이터만 있다[확인]. 14세 미만을 어린 모델로 본다.</summary>
    public const int AdultMinAge = 14;

    public static Color ToColor(Rgba32 c) => new Color32(c.R, c.G, c.B, 255);

    public static AssemblyPlan Resolve(EditState state, GameData data, InstallFiles files)
    {
        var plan = new AssemblyPlan();
        var a = state.appearance ?? new AppearanceState();
        plan.Male = string.Equals(a.gender, "Male", StringComparison.OrdinalIgnoreCase);
        plan.Infant = a.age > 0 && a.age < AdultMinAge;
        plan.Age = a.age;
        plan.HeightModifier = a.heightModifier;
        plan.BodyType = string.IsNullOrEmpty(a.bodyType) ? "Default" : a.bodyType;
        if (data.BodyTypes.TryGetValue(plan.BodyType, out var bt) && bt.UseBlendshape) plan.BlendShape = bt.BlendshapeName;

        string gender = plan.Male ? "Male" : "Female";
        string body = plan.Infant ? $"Infant{gender}_Body_00" : $"{gender}_Body_00";
        Add(plan, files, "body", PartRole.Body, body);

        data.Heads.TryGetValue(a.head ?? "", out var head);
        if (head == null) plan.Notes.Add($"얼굴 ID를 찾을 수 없습니다: {a.head}");
        Add(plan, files, "head", PartRole.Head, head?.Model(plan.Male, plan.Infant));

        var hair = GameData.Resolve(data.Hairs, a.hair, plan.BodyType);
        if (hair == null && !string.IsNullOrEmpty(a.hair)) plan.Notes.Add($"헤어 ID를 찾을 수 없습니다: {a.hair}");
        Add(plan, files, "hair", PartRole.Hair, hair?.Model(plan.Male, plan.Infant));

        var eyes = GameData.Resolve(data.Eyes, a.eyes, a.head, "Head_00");
        if (eyes == null && !string.IsNullOrEmpty(a.eyes)) plan.Notes.Add($"눈 ID를 찾을 수 없습니다: {a.eyes}");
        Add(plan, files, "eyes", PartRole.Eyes, eyes?.Model(plan.Male, plan.Infant));

        // 입은 메시가 아니라 얼굴 머티리얼에 합성하는 텍스처 파츠다(CharacterAssembler.ApplyMouth).
        var mouth = GameData.Resolve(data.Mouths, a.mouth, a.head, "Head_00");
        if (mouth == null && !string.IsNullOrEmpty(a.mouth)) plan.Notes.Add($"입 ID를 찾을 수 없습니다: {a.mouth}");
        Add(plan, files, "mouth", PartRole.Mouth, mouth?.Model(plan.Male, plan.Infant));

        // 외형 색: 시트 값 → 사용자 색
        void Global(string slot, Rgba32? sheet)
        {
            if (ColorOverrides.TryParse(ColorOverrides.Get(a.colors, slot), out var user)) plan.GlobalColors[slot] = user;
            else if (sheet.HasValue) plan.GlobalColors[slot] = ToColor(sheet.Value);
        }
        data.SkinColors.TryGetValue(a.skinColor ?? "", out var skin);
        data.HairColors.TryGetValue(a.hairColor ?? "", out var hairColor);
        data.EyeColors.TryGetValue(a.eyeColor ?? "", out var eyeColor);
        data.MouthColors.TryGetValue(a.mouthColor ?? "", out var mouthColor);
        Global("Skin", skin?.Color1);
        Global("Hair", hairColor?.Color1);
        // 투톤 헤어의 두 번째 색. 한 가지 색(OneColor) 행의 ColorCode2 는 #000000 이므로 첫 색을 같이 쓴다[추정].
        Global("Hair1", hair != null && hair.HairType == "TwoColor"
            ? (hairColor != null && hairColor.Type == "TwoColor" ? hairColor.Color2 : hairColor?.Color1) : null);
        Global("Eye", eyeColor?.Color1);
        Global("Mouth", mouthColor?.Color1);

        ResolveCostumes(plan, state, data, files);

        string prefix = plan.Infant ? "HC" : plan.Male ? "HM" : "HF";
        string variant = plan.BodyType == "Muscular" ? "Muscle" : plan.BodyType;
        plan.Pose = $"{prefix}@Customize_Pose_08_{variant}";
        if (!files.HasResource(PosePath(plan.Pose))) plan.Pose = $"{prefix}@Customize_Pose_08_Default";

        // 아이들 모션: <접두>@Customize_Stand_<체형>. 체형별 클립이 없으면(여성 Thin 등) Default.
        string folder = plan.Infant ? "Infant" : plan.Male ? "Male" : "Female";
        plan.Idle = IdlePath(folder, $"{prefix}@Customize_Stand_{variant}");
        if (!files.HasAsset(plan.Idle)) plan.Idle = IdlePath(folder, $"{prefix}@Customize_Stand_Default");
        if (!files.HasAsset(plan.Idle)) { plan.Notes.Add("아이들 모션 없음: " + plan.Idle); plan.Idle = null; }
        return plan;
    }

    public static string PosePath(string pose) => "Animation/Bones/" + pose + ".sjon.bin";
    static string IdlePath(string folder, string clip) => $"{BundleCache.AssetRoot}Animation/Player/Human/{folder}/Customize/{clip}.fbx";

    static PartPlan Add(AssemblyPlan plan, InstallFiles files, string key, PartRole role, string model)
    {
        if (string.IsNullOrEmpty(model)) return null;
        if (files.ModelDefPath(model) == null) { plan.Notes.Add($"모델 정의를 찾을 수 없습니다: {model}"); return null; }
        var p = new PartPlan { Key = key, Role = role, Model = model };
        plan.Parts.Add(p);
        return p;
    }

    /// <summary>
    /// 패션장비 행이 현재 성별·나이에서 입을 수 있는지(설치 폴더 리소스 존재는 보지 않는다).
    /// Applied 면 models 에 조립할 모델 이름, 아니면 GenderMismatch / NoModelForAppearance 와 안내 문구.
    /// </summary>
    public static CostumeStatus CheckCostume(CostumeData row, bool male, bool infant, out List<string> models, out string detail)
    {
        models = null;
        detail = null;
        if ((row.EquippableGender == "Male" && !male) || (row.EquippableGender == "Female" && male))
        {
            detail = row.EquippableGender == "Male" ? "남성 전용입니다." : "여성 전용입니다.";
            return CostumeStatus.GenderMismatch;
        }
        var list = infant ? (male ? row.BoyModels : row.GirlModels) : (male ? row.MaleModels : row.FemaleModels);
        models = list.FindAll(m => !string.IsNullOrEmpty(m) && m != "Invalid");
        if (models.Count == 0) { detail = "현재 외형에 맞는 모델이 없습니다."; return CostumeStatus.NoModelForAppearance; }
        return CostumeStatus.Applied;
    }

    static void ResolveCostumes(AssemblyPlan plan, EditState state, GameData data, InstallFiles files)
    {
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        var applied = new List<(CostumeSlotState slot, CostumeResult res, CostumeData row)>();

        foreach (var slot in state.costumes)
        {
            if (slot == null || string.IsNullOrEmpty(slot.part)) continue;
            var res = new CostumeResult { Part = slot.part, CostumeId = slot.costumeId };
            plan.Costumes.Add(res);
            if (slot.kind == CostumeSlotState.KindResource)
            {
                res.DisplayName = slot.resource;
                if (string.IsNullOrEmpty(slot.resource) || files.ModelDefPath(slot.resource) == null) { res.Status = CostumeStatus.MissingResource; res.Detail = "리소스를 찾을 수 없습니다."; continue; }
                res.Models.Add(slot.resource);
                applied.Add((slot, res, null));
                continue;
            }
            if (string.IsNullOrEmpty(slot.costumeId)) { plan.Costumes.Remove(res); continue; }
            if (!data.Costumes.TryGetValue(slot.costumeId, out var row)) { res.Status = CostumeStatus.UnknownId; res.Detail = "패션장비 데이터에 없는 ID입니다."; continue; }
            res.DisplayName = row.DisplayName;
            var fit = CheckCostume(row, plan.Male, plan.Infant, out var models, out var detail);
            if (fit != CostumeStatus.Applied) { res.Status = fit; res.Detail = detail; continue; }
            var missing = models.Find(m => files.ModelDefPath(m) == null);
            if (missing != null) { res.Status = CostumeStatus.MissingResource; res.Detail = "모델을 찾을 수 없습니다: " + missing; continue; }
            res.Models.AddRange(models);
            applied.Add((slot, res, row));
        }

        // 실제 적용되는 의상의 HidingParts 만 다른 부위를 가린다. 미적용 의상은 가리지 않는다.
        foreach (var (slot, res, row) in applied)
            if (row != null) foreach (var h in row.HidingParts) if (h != slot.part) hidden.Add(h);

        foreach (var (slot, res, row) in applied)
        {
            if (hidden.Contains(slot.part)) { res.Status = CostumeStatus.HiddenByOther; res.Detail = "다른 의상에 가려졌습니다."; continue; }
            res.Status = CostumeStatus.Applied;
            for (int i = 0; i < res.Models.Count; i++)
            {
                var p = Add(plan, files, AssemblyPlan.CostumeKey(slot.part), PartRole.Costume, res.Models[i]);
                if (p == null) continue;
                if (row != null)
                    for (int k = 0; k < 6; k++)
                        if (row.DyeColors[k].HasValue) p.SlotColors["Slot" + k] = ToColor(row.DyeColors[k].Value);
                foreach (var d in slot.dyes)
                    if (ColorOverrides.TryParse(d.color, out var c)) p.SlotColors[d.slot] = c;
            }
        }
    }
}
