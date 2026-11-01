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
}

public sealed class CharacterView : MonoBehaviour
{
    public Transform[] Bones = Array.Empty<Transform>();
    internal Vector3[] BindPos, PosePos;
    internal Quaternion[] BindRot, PoseRot;
    internal Vector3[] BindScl, PoseScl;
    internal bool[] HasPose;
    public readonly List<(string partKey, SkinnedMeshRenderer renderer)> Renderers = new List<(string, SkinnedMeshRenderer)>();
    public readonly List<DyeBinding> Dyes = new List<DyeBinding>();
    internal readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
    internal SkinnedMeshRenderer Body;
    internal Mesh BodyFullMesh, BodyCutMesh;
    public AssemblyPlan Plan;

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
    }

    internal void ApplyBodyMesh()
    {
        if (Body && BodyCutMesh) Body.sharedMesh = bodyCut ? BodyCutMesh : BodyFullMesh;
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

    void OnDestroy()
    {
        foreach (var o in Owned)
        {
            if (!o) continue;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
        Owned.Clear();
    }
}
