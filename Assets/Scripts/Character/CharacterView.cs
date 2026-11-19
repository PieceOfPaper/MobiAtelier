// 조립된 캐릭터. 포즈·체형·몸 가리기·염색 상태를 바꾸고, 만든 메시·머티리얼을 소유해 정리한다.
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class DyeBinding
{
    public string PartKey, Slot, Property;
    public Material Material;
    /// <summary>원본 머티리얼에 저장돼 있던 값. 시트 기본색이 없는 슬롯은 이 값을 유지한다.</summary>
    public Color Original;
    /// <summary>DyeInfo 슬롯의 재질(Cloth, Leather, Metal, Wood, None).</summary>
    public string DyeMaterial;
}

/// <summary>의상 한 벌의 염색 파트(Slot0~5) 하나. 같은 슬롯에 연결된 메시·머티리얼은 함께 바뀐다.</summary>
public sealed class DyePart
{
    public string Slot, DyeMaterial;
    /// <summary>0부터. Slot0 → 0. 화면에는 "파트 {Number + 1}"로 표시한다.</summary>
    public int Number;
    /// <summary>지금 머티리얼에 들어 있는 색(원본·시트 기본색·사용자 색 중 적용된 값).</summary>
    public Color Current;
}

public sealed class CharacterView : MonoBehaviour
{
    public Transform[] Bones = Array.Empty<Transform>();
    internal Vector3[] BindPos, PosePos;
    internal Quaternion[] BindRot, PoseRot;
    internal Vector3[] BindScl, PoseScl;
    internal bool[] HasPose;
    /// <summary>애니메이션 뒤에 거는 본 국소 오프셋(도구 장착 본 변위). 없으면 null.</summary>
    internal Vector3[] OffsetPos;
    internal Quaternion[] OffsetRot;
    Quaternion?[] offsetApplied;
    public readonly List<(string partKey, SkinnedMeshRenderer renderer)> Renderers = new List<(string, SkinnedMeshRenderer)>();
    public readonly List<DyeBinding> Dyes = new List<DyeBinding>();
    internal readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
    internal SkinnedMeshRenderer Body;
    /// <summary>머리(Head 역할) 메시. 카메라 높이 기준(머리 아래 높이)에 쓴다.</summary>
    internal readonly List<SkinnedMeshRenderer> HeadRenderers = new List<SkinnedMeshRenderer>();
    internal Mesh BodyFullMesh, BodyCutMesh;
    /// <summary>옷 맞춤으로 자르고 민 렌더러(몸 포함): 원본·자른 메시와 각각의 본 배열(밀기가 본을 덧붙일 수 있다). SetBodyCut 이 함께 바꾼다.</summary>
    internal sealed class CutMesh
    {
        public SkinnedMeshRenderer Renderer;
        public Mesh Full, Cut;
        public Transform[] FullBones, CutBones;
    }
    internal readonly List<CutMesh> CutMeshes = new List<CutMesh>();
    /// <summary>의상 파츠 안의 피부 조각 재질(원본 셰이더 MMN/CH/Skin_Body). 몸과 같은 몸 UV 를 쓰므로 몸꾸밈을 함께 건다.</summary>
    internal readonly List<Material> CostumeSkinMaterials = new List<Material>();
    public AssemblyPlan Plan;
    /// <summary>무기 파츠 메시와 서브메시별 원본 재질(외곽선을 그릴지 정한다). 조립기가 채운다.</summary>
    internal readonly List<(SkinnedMeshRenderer renderer, Material[] sources)> WeaponMeshes = new List<(SkinnedMeshRenderer, Material[])>();
    /// <summary>외곽선 재질을 만들 앱 캐릭터 셰이더.</summary>
    internal Shader CharacterShader;
    /// <summary>세트 활성 연출 재생기. 없으면 null.</summary>
    internal TimelineEffectPlayer SetEffect;
    /// <summary>도구 사용 연출 재생기(시작·반복). 없으면 비어 있다.</summary>
    public readonly List<TimelineEffectPlayer> ToolEffects = new List<TimelineEffectPlayer>();
    /// <summary>의상 자체 연출 재생기. 없으면 null.</summary>
    public TimelineEffectPlayer CostumeEffects;

    readonly List<GameObject> outlineObjects = new List<GameObject>();
    readonly List<(Material material, bool enabled)> outlineMaterials = new List<(Material, bool)>();
    bool outlinesBuilt;
    /// <summary>외곽선을 그리는 서브메시 수(진단용).</summary>
    internal int WeaponOutlineCount { get { int n = 0; foreach (var (_, on) in outlineMaterials) if (on) n++; return n; } }

    bool posed = true, bodyCut = true;

    public Transform FindBone(string name)
    {
        foreach (var b in Bones) if (b && b.name == name) return b;
        return null;
    }

    public void SetPosed(bool value) { posed = value; ApplyBones(); }
    public void SetBodyCut(bool value) { bodyCut = value; ApplyBodyMesh(); }

    internal void ApplyBones()
    {
        for (int i = 0; i < Bones.Length; i++)
        {
            if (!Bones[i]) continue;
            bool p = posed && HasPose != null && HasPose[i];
            Bones[i].localPosition = p ? PosePos[i] : BindPos[i];
            Bones[i].localRotation = p ? PoseRot[i] : BindRot[i];
            Bones[i].localScale = p ? PoseScl[i] : BindScl[i];
        }
        ApplyLocalOffsets();
    }

    /// <summary>본 국소 오프셋을 현재 국소 값에 더한다(위치 += 오프셋, 회전 = 회전 × 오프셋).</summary>
    internal void ApplyLocalOffsets()
    {
        if (OffsetPos == null) return;
        if (offsetApplied == null || offsetApplied.Length != OffsetPos.Length) offsetApplied = new Quaternion?[OffsetPos.Length];
        for (int i = 0; i < Bones.Length && i < OffsetPos.Length; i++)
        {
            if (!Bones[i] || (OffsetPos[i] == Vector3.zero && OffsetRot[i] == Quaternion.identity)) continue;
            Bones[i].localPosition += OffsetPos[i];
            // 클립이 이 본의 회전을 쓰지 않으면 지난번에 건 회전이 그대로 남아 있다. 그때는 오프셋을 한 번만 건다.
            var r = Bones[i].localRotation;
            if (offsetApplied[i] is Quaternion last && Quaternion.Dot(last, r) > 0.999999f) r = last * Quaternion.Inverse(OffsetRot[i]);
            Bones[i].localRotation = r * OffsetRot[i];
            offsetApplied[i] = Bones[i].localRotation;
        }
    }

    internal void ApplyBodyMesh()
    {
        foreach (var c in CutMeshes)
        {
            if (!c.Renderer || !c.Cut) continue;
            c.Renderer.sharedMesh = bodyCut ? c.Cut : c.Full;
            c.Renderer.bones = bodyCut ? c.CutBones : c.FullBones;
        }
    }

    public void ApplyBlendShape(string blendShape)
    {
        foreach (var (_, r) in Renderers)
        {
            if (!r || !r.sharedMesh) continue;
            var m = r.sharedMesh;
            for (int k = 0; k < m.blendShapeCount; k++)
                r.SetBlendShapeWeight(k, blendShape != null && m.GetBlendShapeName(k) == blendShape ? 100f : 0f);
        }
    }

    /// <summary>계획의 색을 적용한다. 파츠 색 → 외형(전역) 색 → 원본 순으로 정한다.</summary>
    public void ApplyPlanColors(AssemblyPlan plan)
    {
        var parts = new Dictionary<string, PartPlan>(StringComparer.Ordinal);
        foreach (var p in plan.Parts) parts[p.Key] = p;
        foreach (var d in Dyes)
        {
            Color c = d.Original;
            if (parts.TryGetValue(d.PartKey, out var pp) && pp.SlotColors.TryGetValue(d.Slot, out var pc)) c = pc;
            else if (plan.GlobalColors.TryGetValue(d.Slot, out var gc)) c = gc;
            d.Material.SetColor(d.Property, c);
        }
        ApplyWeaponAura(plan.WeaponAura);
    }

    /// <summary>
    /// 무기 오라(Equipment.md "무기 오라"). 무기 메시마다 같은 메시·본을 쓰는 외곽선 렌더러를 자식으로 붙인다(무기 숨김을 따라간다).
    /// 원본 재질이 무기 셰이더(MMN/CH/Weapon)이고 외곽선을 끄지 않은(_OutlineOff = 0) 서브메시만 그린다.
    /// </summary>
    public void ApplyWeaponAura(WeaponAuraGrade grade)
    {
        if (grade == null)
        {
            foreach (var go in outlineObjects) Kill(go);
            outlineObjects.Clear();
            foreach (var (m, _) in outlineMaterials) Kill(m);
            outlineMaterials.Clear();
            outlinesBuilt = false;
            return;
        }
        // 조립 중(셰이더를 넘기기 전)에 불리면 만들지 않고, 조립 끝에서 다시 불릴 때 만든다.
        if (!outlinesBuilt && CharacterShader)
        {
            outlinesBuilt = true;
            foreach (var (src, sources) in WeaponMeshes)
            {
                if (!src || !src.sharedMesh) continue;
                var mats = new Material[src.sharedMesh.subMeshCount];
                bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var o = i < sources.Length ? sources[i] : null;
                    bool on = o && o.shader && o.shader.name.StartsWith("MMN/CH/Weapon", StringComparison.Ordinal)
                              && !(o.HasProperty("_OutlineOff") && o.GetFloat("_OutlineOff") > 0.5f);
                    var m = new Material(CharacterShader) { name = (o ? o.name : "Weapon") + "_GradeOutline", renderQueue = 3000 };
                    foreach (var pass in new[] { "UniversalForward", "ForwardLit", "ShadowCaster", "DepthOnly", "DepthNormals" }) m.SetShaderPassEnabled(pass, false);
                    if (o && o.HasProperty("_OutlineColorBlendMin")) m.SetFloat("_OutlineColorBlendMin", o.GetFloat("_OutlineColorBlendMin"));
                    if (o && o.HasProperty("_OutlineColorBlendMax")) m.SetFloat("_OutlineColorBlendMax", o.GetFloat("_OutlineColorBlendMax"));
                    outlineMaterials.Add((m, on));
                    mats[i] = m;
                    any |= on;
                }
                if (!any) continue;
                var go = new GameObject(src.name + "_GradeOutline");
                go.transform.SetParent(src.transform, false);
                var r = go.AddComponent<SkinnedMeshRenderer>();
                r.sharedMesh = src.sharedMesh;
                r.bones = src.bones;
                r.rootBone = src.rootBone;
                r.localBounds = src.localBounds;
                r.updateWhenOffscreen = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.sharedMaterials = mats;
                go.AddComponent<OutlineFollow>().Source = src;
                outlineObjects.Add(go);
            }
        }
        var main = new Color(grade.Main[0], grade.Main[1], grade.Main[2], grade.Index);
        var sub = new Color(grade.Sub[0], grade.Sub[1], grade.Sub[2], 0);
        foreach (var (m, on) in outlineMaterials)
        {
            // SetColor 는 rgb 만 감마 → 선형으로 바꾸고 알파(등급 번호)는 그대로 둔다(게임도 Color 로 넣는다).
            m.SetColor("_WeaponGradeColor", on ? main : Color.clear);
            m.SetColor("_WeaponGradeColor2", sub);
        }
    }

    static void Kill(UnityEngine.Object o)
    {
        if (!o) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    /// <summary>한 파츠의 슬롯 색만 바꾼다. partKey 가 null 이면 모든 파츠의 같은 슬롯(외형 색)을 바꾼다.</summary>
    public void SetColor(string partKey, string slot, Color color)
    {
        foreach (var d in Dyes)
            if (d.Slot == slot && (partKey == null || d.PartKey == partKey)) d.Material.SetColor(d.Property, color);
    }

    public List<string> Slots(string partKey)
    {
        var s = new List<string>();
        foreach (var d in Dyes) if (d.PartKey == partKey && !s.Contains(d.Slot)) s.Add(d.Slot);
        s.Sort(StringComparer.Ordinal);
        return s;
    }

    /// <summary>
    /// 의상 파츠의 염색 파트. 모델 DyeInfo 에서 연결(Wiring)이 실제로 있는 Slot0~5 만 돌려준다.
    /// 연결이 비어 있는 슬롯(예: 일부 신발의 Slot2)과 피부·헤어 등 외형 색 슬롯은 넣지 않는다.
    /// </summary>
    public List<DyePart> DyeParts(string partKey)
    {
        var list = new List<DyePart>();
        foreach (var d in Dyes)
        {
            if (d.PartKey != partKey || !IsCostumeSlot(d.Slot, out int n)) continue;
            if (list.Exists(x => x.Slot == d.Slot)) continue;
            list.Add(new DyePart { Slot = d.Slot, Number = n, DyeMaterial = d.DyeMaterial, Current = d.Material ? d.Material.GetColor(d.Property) : d.Original });
        }
        list.Sort((a, b) => a.Number.CompareTo(b.Number));
        return list;
    }

    public static bool IsCostumeSlot(string slot, out int number)
    {
        number = -1;
        return slot != null && slot.Length == 5 && slot.StartsWith("Slot", StringComparison.Ordinal) && int.TryParse(slot.Substring(4), out number);
    }

    /// <summary>표시 중인 렌더러 전체를 감싸는 바운드(월드).</summary>
    public Bounds VisibleBounds()
    {
        bool any = false; var b = new Bounds(transform.position, Vector3.zero);
        foreach (var (_, r) in Renderers)
        {
            if (!r || !r.gameObject.activeInHierarchy) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    /// <summary>
    /// 게임 HeightInfo 의 BodyHeightWithoutHead 근사: 머리 메시의 가장 낮은 점 − 몸 메시의 가장 낮은 점(지금 포즈, 월드).
    /// 머리·몸 메시가 없으면 표시 바운드 높이를 쓴다. floorY 는 몸 최저점.
    /// </summary>
    public float MeasureBodyHeightWithoutHead(out float floorY)
    {
        floorY = transform.position.y;
        float bodyLow = LowestY(Body);
        float headLow = float.MaxValue;
        foreach (var r in HeadRenderers)
            if (r && r.gameObject.activeInHierarchy) headLow = Mathf.Min(headLow, LowestY(r));
        if (bodyLow < float.MaxValue) floorY = bodyLow;
        if (headLow < float.MaxValue && bodyLow < float.MaxValue && headLow > bodyLow) return headLow - bodyLow;
        var b = VisibleBounds();
        return b.size.y;
    }

    static float LowestY(SkinnedMeshRenderer r)
    {
        if (!r || !r.sharedMesh) return float.MaxValue;
        var m = new Mesh();
        try
        {
            r.BakeMesh(m, true);
            var tr = r.transform;
            float low = float.MaxValue;
            // useScale=true 로 구운 정점은 크기가 이미 반영돼 있어 위치·회전만 적용한다.
            foreach (var v in m.vertices) low = Mathf.Min(low, (tr.position + tr.rotation * v).y);
            return low;
        }
        finally { if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); }
    }

    void OnDestroy()
    {
        foreach (var (m, _) in outlineMaterials) Kill(m);
        outlineMaterials.Clear();
        foreach (var o in Owned)
        {
            if (!o) continue;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
        Owned.Clear();
    }
}

/// <summary>외곽선 렌더러가 원본 무기 렌더러의 렌더링 끔(소셜 모션 숨김 등)·블렌드셰이프를 따른다.</summary>
public sealed class OutlineFollow : MonoBehaviour
{
    public SkinnedMeshRenderer Source;
    SkinnedMeshRenderer self;

    void LateUpdate()
    {
        if (!self) self = GetComponent<SkinnedMeshRenderer>();
        if (!Source || !self) return;
        if (self.forceRenderingOff != Source.forceRenderingOff) self.forceRenderingOff = Source.forceRenderingOff;
        var m = Source.sharedMesh;
        if (m) for (int i = 0; i < m.blendShapeCount; i++) self.SetBlendShapeWeight(i, Source.GetBlendShapeWeight(i));
    }
}
