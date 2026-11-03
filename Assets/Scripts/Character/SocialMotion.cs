// 소셜 모션(게임 "모션" 메뉴의 행동) 재생 자료 (SocialMotion.md)
//   SocialAction.Actions[] → Action(그룹 PlayerFemale·PlayerMale·PlayerInfant, 조건 BodyType·Clothing) → 시작·반복 클립.
//   소품·이펙트: 클립 이름의 StageTimeline 과 행동별 ActionStageTimeline 을 함께 읽는다(쓰다듬기: 고양이는 클립 쪽, 이펙트는 행동 쪽).
//     두 쪽에 같은 프리팹이 같은 시각에 있으면 하나만 쓴다(수박 [추정]).
//     Prop(소품 프리팹을 본에 붙임) · PropAnimation(소품 자체 클립) · Effect(파티클 프리팹)
//     · IKControl(손바닥을 소품·입 쪽으로) · AdditiveRenderer(얼굴 등에 덧그리는 재질) · ObjectVisibility(렌더러 숨김)
//     · ShaderProperty(렌더러 셰이더 값) 트랙을 쓴다.
//     소리(Sound·Footstep)는 보류다(SocialMotion.md 6장). 무기 넣음(ToolState)은 모든 행동에서 이미 넣은 위치로 옮긴다.
//   저장하지 않는 보기 도구다. 장면 상태(EditState)·이력·자동 저장과 관계없다.
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>소셜 모션 소품 하나(타임라인 Prop 구간).</summary>
public sealed class ActionProp
{
    public int Segment;
    public float Begin, End;
    public bool Infinite;
    public string Key;
    public GameObject Go;
    public Quaternion Rotation;
    public bool FollowRotation, OnGround;
    /// <summary>소품 자체 클립(PropAnimation 구간): 구간 번호, 시작·끝 초, 끝없음, 클립.</summary>
    public readonly List<(int segment, float begin, float end, bool infinite, AnimationClip clip)> Clips =
        new List<(int, float, float, bool, AnimationClip)>();

    /// <summary>붙인 본의 회전을 따르지 않는 소품은 캐릭터 방향으로, 바닥에 놓는 소품은 캐릭터 발 높이로 맞춘다.</summary>
    internal void Place(Transform character)
    {
        if (!Go || !Go.activeSelf) return;
        var t = Go.transform;
        if (!FollowRotation) t.rotation = character.rotation * Rotation;
        if (OnGround) { var p = t.position; p.y = character.position.y; t.position = p; }
    }
}

/// <summary>소셜 모션 이펙트 하나(타임라인 Effect 구간).</summary>
public sealed class ActionEffect
{
    public int Segment;
    public float Begin, End;
    public bool Infinite;
    public GameObject Go;
    public ParticleSystem[] Systems = Array.Empty<ParticleSystem>();
    /// <summary>위치 기준(캐릭터 본 또는 소품 본). 없으면 캐릭터 루트.</summary>
    public Transform Ref;
    public bool Attached, OnGround;
    public Vector3 Offset;
    public Quaternion Rotation;
    internal bool Active;
    internal double LastLocal;

    /// <summary>켜질 때 위치를 정한다. 붙는 이펙트는 기준 본 아래, 아니면 그 순간 위치에 캐릭터 루트 아래로 둔다.</summary>
    internal void Spawn(Transform character)
    {
        var r = Ref ? Ref : character;
        var t = Go.transform;
        if (Attached)
        {
            t.SetParent(r, false);
            t.localPosition = Offset;
            t.localRotation = Rotation;
        }
        else
        {
            t.SetParent(character, true);
            t.position = r.TransformPoint(Offset);
            t.rotation = r.rotation * Rotation;
        }
        if (OnGround) { var p = t.position; p.y = character.position.y; t.position = p; }
    }
}

/// <summary>타임라인 구간 하나의 공통 값.</summary>
public abstract class ActionTrack
{
    public int Segment;
    public float Begin, End;
    public bool Infinite;
    public bool Active(int seg, double t) => Segment == seg && ActionMotion.Inside(t, Begin, End, Infinite);
    /// <summary>구간 안 진행도(0~1). 셰이더 값 곡선의 시간이다.</summary>
    public float Progress(double t) => End > Begin ? Mathf.Clamp01((float)((t - Begin) / (End - Begin))) : 0f;
}

/// <summary>
/// 손 IK(IKControl). Control 손바닥(_L Palm·_R Palm)을 목표로 옮긴다.
///   IKReferenceableEffectBone·TargetBone: 목표 본 위치로. DisplacementPosition: Target 본이 Displacement 본에 닿도록 손바닥을 같은 만큼 옮긴다.
/// </summary>
public sealed class ActionIK : ActionTrack
{
    public string Control, Mode, Target, Displacement;
    public float FadeIn, FadeOut;
    public AnimationCurve FadeInCurve, FadeOutCurve;

    public float Weight(double t)
    {
        float w = 1f;
        float a = (float)(t - Begin);
        if (FadeIn > 0 && a < FadeIn) w = Eval(FadeInCurve, a / FadeIn, true);
        if (!Infinite && FadeOut > 0)
        {
            float b = (float)(End - t);
            if (b < FadeOut) w = Mathf.Min(w, Eval(FadeOutCurve, 1f - b / FadeOut, false));
        }
        return Mathf.Clamp01(w);
    }

    static float Eval(AnimationCurve c, float u, bool rising) => c != null && c.length >= 2 ? c.Evaluate(u) : rising ? u : 1f - u;
}

/// <summary>셰이더 값 하나(ShaderPropertyData). 상수 또는 구간 진행도(0~1)에 대한 곡선이다.</summary>
public sealed class ActionShaderValue
{
    public string Name, Type;
    public float Float;
    public Color Color;
    public Vector4 Vector;
    public AnimationCurve FloatCurve;
    public AnimationCurve[] ColorCurves;

    public void Apply(MaterialPropertyBlock b, float u)
    {
        switch (Type)
        {
            case "Float": b.SetFloat(Name, Float); break;
            case "Float_Curve": b.SetFloat(Name, FloatCurve != null && FloatCurve.length > 0 ? FloatCurve.Evaluate(u) : Float); break;
            case "Color": b.SetColor(Name, Color); break;
            case "Color_Curve": b.SetColor(Name, CurveColor(u)); break;
            case "Vector": b.SetVector(Name, Vector); break;
        }
    }

    public void Apply(Material m, float u)
    {
        switch (Type)
        {
            case "Float": m.SetFloat(Name, Float); break;
            case "Float_Curve": m.SetFloat(Name, FloatCurve != null && FloatCurve.length > 0 ? FloatCurve.Evaluate(u) : Float); break;
            case "Color": m.SetColor(Name, Color); break;
            case "Color_Curve": m.SetColor(Name, CurveColor(u)); break;
            case "Vector": m.SetVector(Name, Vector); break;
        }
    }

    Color CurveColor(float u)
    {
        if (ColorCurves == null) return Color;
        float C(int i) => ColorCurves[i] != null && ColorCurves[i].length > 0 ? ColorCurves[i].Evaluate(u) : Color[i];
        return new Color(C(0), C(1), C(2), C(3));
    }
}

/// <summary>
/// 렌더러에 재질을 하나 더 그린다(AdditiveRenderer, 얼굴 볼 붉어짐 등).
/// 재질을 배열 끝에 덧붙이면 Unity 는 마지막 서브메시에만 그리므로, 같은 메시·본을 쓰는 렌더러를 하나 더 만들어 모든 서브메시에 그린다.
/// 표정 블렌드셰이프는 매 프레임 원본 값을 따라간다.
/// </summary>
public sealed class ActionAdditive : ActionTrack
{
    public Material Material;
    public readonly List<Renderer> Targets = new List<Renderer>();
    public readonly List<ActionShaderValue> Values = new List<ActionShaderValue>();
    internal readonly List<(SkinnedMeshRenderer src, SkinnedMeshRenderer overlay)> Applied = new List<(SkinnedMeshRenderer, SkinnedMeshRenderer)>();

    internal void Set(bool on, double t)
    {
        if (on && Applied.Count == 0 && Material)
            foreach (var r in Targets)
            {
                if (!(r is SkinnedMeshRenderer src) || !src.sharedMesh) continue;
                var go = new GameObject(src.name + "_Additive");
                go.transform.SetParent(src.transform.parent, false);
                go.transform.localPosition = src.transform.localPosition;
                go.transform.localRotation = src.transform.localRotation;
                go.transform.localScale = src.transform.localScale;
                var o = go.AddComponent<SkinnedMeshRenderer>();
                o.sharedMesh = src.sharedMesh;
                o.bones = src.bones;
                o.rootBone = src.rootBone;
                o.localBounds = src.localBounds;
                o.updateWhenOffscreen = true;
                o.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                o.receiveShadows = false;
                var mats = new Material[src.sharedMesh.subMeshCount];
                for (int i = 0; i < mats.Length; i++) mats[i] = Material;
                o.sharedMaterials = mats;
                Applied.Add((src, o));
            }
        else if (!on && Applied.Count > 0) Restore();
        if (!on) return;
        foreach (var (src, o) in Applied)
        {
            if (!src || !o) continue;
            o.forceRenderingOff = src.forceRenderingOff || !src.gameObject.activeInHierarchy;
            for (int i = 0; i < src.sharedMesh.blendShapeCount; i++) o.SetBlendShapeWeight(i, src.GetBlendShapeWeight(i));
        }
        if (Material) foreach (var v in Values) v.Apply(Material, Progress(t));
    }

    internal void Restore()
    {
        foreach (var (_, o) in Applied)
            if (o) { if (Application.isPlaying) UnityEngine.Object.Destroy(o.gameObject); else UnityEngine.Object.DestroyImmediate(o.gameObject); }
        Applied.Clear();
    }
}

/// <summary>렌더러 셰이더 값을 구간 동안 바꾼다(ShaderProperty). MaterialPropertyBlock 으로 덮고 끝나면 지운다.</summary>
public sealed class ActionShader : ActionTrack
{
    public readonly List<Renderer> Targets = new List<Renderer>();
    public readonly List<ActionShaderValue> Values = new List<ActionShaderValue>();
}

/// <summary>렌더러를 구간 동안 숨기거나 보인다(ObjectVisibility).</summary>
public sealed class ActionVisibility : ActionTrack
{
    public readonly List<Renderer> Targets = new List<Renderer>();
    public bool Visible;
}

/// <summary>현재 외형으로 해석·로드한 소셜 모션 하나. CharacterMotion.PlayAction 에 넘긴다.</summary>
public sealed class ActionMotion : IDisposable
{
    public SocialActionData Social;
    /// <summary>실제로 재생하는 행동 이름(Random 은 고른 것).</summary>
    public string ActionName;
    public MotionSegment Start, Loop;
    public float FadeIn, FadeOut;
    public bool HideWeapon;
    public readonly List<ActionProp> Props = new List<ActionProp>();
    public readonly List<ActionEffect> Effects = new List<ActionEffect>();
    public readonly List<ActionIK> IKs = new List<ActionIK>();
    public readonly List<ActionAdditive> Additives = new List<ActionAdditive>();
    public readonly List<ActionShader> Shaders = new List<ActionShader>();
    public readonly List<ActionVisibility> Visibilities = new List<ActionVisibility>();
    /// <summary>셰이더 값·숨김을 바꾼 렌더러. 끝날 때 되돌린다.</summary>
    internal readonly HashSet<Renderer> Blocked = new HashSet<Renderer>(), Hidden = new HashSet<Renderer>();
    internal readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
    internal GameObject Holder;

    /// <summary>시작 클립만 있다(끝나면 기본 모션으로 돌아간다).</summary>
    public bool OneShot => Loop == null;
    public MotionSegment Segment(int i) => i == 0 ? Start : Loop;

    internal static bool Inside(double t, float begin, float end, bool infinite) => t >= begin && (infinite || t < end);

    Dictionary<string, List<Transform>> targets;

    /// <summary>
    /// 손 IK 목표 본: 소품·이펙트 안의 같은 이름 본(켜져 있는 것 우선), 없으면 캐릭터 본.
    /// 캐릭터에 없는 _Lip_Effect 는 입술 가운데(_Lip_Center)로 대신한다 [추정].
    /// </summary>
    internal Transform FindTarget(string name, CharacterView view)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (targets == null)
        {
            targets = new Dictionary<string, List<Transform>>(StringComparer.Ordinal);
            void Scan(GameObject go)
            {
                if (!go) return;
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (!targets.TryGetValue(t.name, out var l)) targets[t.name] = l = new List<Transform>();
                    l.Add(t);
                }
            }
            foreach (var p in Props) Scan(p.Go);
            foreach (var e in Effects) Scan(e.Go);
        }
        if (targets.TryGetValue(name, out var list))
        {
            foreach (var t in list) if (t && t.gameObject.activeInHierarchy) return t;
        }
        var bone = view.FindBone(name);
        if (!bone && name == "_Lip_Effect") bone = view.FindBone("_Lip_Center");
        return bone;
    }

    /// <summary>캐릭터 렌더러에 바꾼 것(덧그린 재질·셰이더 값·숨김)을 되돌린다.</summary>
    internal void RestoreRenderers()
    {
        foreach (var a in Additives) a.Restore();
        foreach (var r in Blocked) if (r) r.SetPropertyBlock(null);
        foreach (var r in Hidden) if (r) r.forceRenderingOff = false;
        Blocked.Clear(); Hidden.Clear();
    }

    public void Dispose()
    {
        RestoreRenderers();
        foreach (var p in Props) Kill(p.Go);
        foreach (var e in Effects) Kill(e.Go);
        Kill(Holder);
        foreach (var o in Owned) Kill(o);
        Props.Clear(); Effects.Clear(); Owned.Clear(); IKs.Clear(); Additives.Clear(); Shaders.Clear(); Visibilities.Clear();
    }

    static void Kill(UnityEngine.Object o)
    {
        if (!o) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
    }
}

public static class SocialMotionLoader
{
    /// <summary>모션 목록에 넣는 행동: 2인 모션(Logic)은 뺀다(사용자 결정 2026-11-03).</summary>
    public static bool Listed(SocialActionData s) => s != null && !s.IsLogic;

    public static string Group(AssemblyPlan plan) => plan.Infant ? "PlayerInfant" : plan.Male ? "PlayerMale" : "PlayerFemale";

    static HashSet<string> Facts(AssemblyPlan plan)
    {
        var f = new HashSet<string>(StringComparer.Ordinal)
        {
            "Gender:" + (plan.Male ? "Male" : "Female"),
            "BodyType:" + plan.BodyType,
        };
        if (!string.IsNullOrEmpty(plan.Clothing)) f.Add("Clothing:" + plan.Clothing);
        return f;
    }

    /// <summary>재생할 행동 이름. Random 은 행동 중 하나를 고른다(가중치가 모두 같다).</summary>
    public static string PickAction(SocialActionData s, System.Random rng)
    {
        if (s.IsRandom && s.Actions.Count > 1) return s.Actions[rng.Next(s.Actions.Count)];
        return !string.IsNullOrEmpty(s.PreviewAction) && s.Actions.Contains(s.PreviewAction) ? s.PreviewAction : s.Actions[0];
    }

    /// <summary>
    /// 현재 캐릭터(view.Plan)에 맞는 행동 클립·소품·이펙트를 읽는다. 실패하면 null 과 사유.
    /// 소품 재질 중 캐릭터 셰이더(MMN/CH)는 앱 캐릭터 셰이더로 바꾸고, 나머지(배경·이펙트 셰이더)는 원본을 쓴다.
    /// </summary>
    public static ActionMotion Load(InstallFiles files, BundleCache bundles, GameData data, CharacterView view, Shader shader,
        SocialActionData social, string actionName, Action<string> log, out string error)
    {
        error = null;
        var plan = view.Plan;
        string group = Group(plan);
        var facts = Facts(plan);
        var row = data.FindAction(group, actionName, facts);
        if (row == null) { error = "현재 외형에 맞는 모션 데이터가 없습니다."; return null; }
        string startPath = Abs(row.StartClip), loopPath = Abs(row.LoopClip);
        var a = new ActionMotion
        {
            Social = social, ActionName = actionName, FadeIn = row.FadeIn, FadeOut = row.FadeOut, HideWeapon = row.HideWeapon,
        };
        a.Start = Segment(files, bundles, view, startPath);
        a.Loop = Segment(files, bundles, view, loopPath);
        if (a.Start == null && a.Loop == null) { error = "모션 클립을 설치 폴더에서 찾지 못했습니다."; return null; }
        log?.Invoke($"소셜 모션 {social.Id}/{actionName} ({group} {row.Condition}): 시작 {Desc(a.Start)}, 반복 {Desc(a.Loop)}, 페이드 {a.FadeIn:F2}/{a.FadeOut:F2}s, 무기 숨김 {a.HideWeapon}");

        a.Holder = new GameObject("SocialMotion");
        a.Holder.transform.SetParent(view.transform, false);
        var tl = data.ActionTimeline(group, actionName, facts);
        var startTl = new List<string>(ClipTimelines(data, startPath));
        var loopTl = new List<string>(ClipTimelines(data, loopPath));
        if (tl != null)
        {
            foreach (var n in tl.Start) if (!startTl.Contains(n)) startTl.Add(n);
            foreach (var n in tl.Loop) if (!loopTl.Contains(n)) loopTl.Add(n);
        }
        var pending = new List<(ActionTrack track, object p)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (a.Start != null) foreach (var n in startTl) ReadTimeline(files, bundles, view, shader, a, 0, n, pending, seen, log);
        if (a.Loop != null) foreach (var n in loopTl) ReadTimeline(files, bundles, view, shader, a, 1, n, pending, seen, log);
        // 렌더러 대상은 소품을 모두 만든 뒤 찾는다(다른 구간의 소품 렌더러를 가리킬 수 있다).
        foreach (var (track, p) in pending) ResolveTargets(view, a, track, p, log);
        foreach (var ad in a.Additives)
        {
            var sh = ad.Material ? ad.Material.shader : null;
            var passes = new List<string>();
            if (sh) for (int i = 0; i < ad.Material.passCount; i++) passes.Add(ad.Material.GetPassName(i) + "/" + ad.Material.GetTag("LightMode", false));
            log?.Invoke($"  덧그림 {ad.Material?.name}: 셰이더 {(sh ? sh.name : "-")} 큐 {ad.Material?.renderQueue} 패스 [{string.Join(",", passes)}] 대상 {string.Join(",", ad.Targets.ConvertAll(r => r ? r.name + "(" + r.sharedMaterials.Length + ")" : "-"))}");
        }
        if (a.Props.Count + a.Effects.Count + a.IKs.Count + a.Additives.Count + a.Shaders.Count + a.Visibilities.Count > 0)
            log?.Invoke($"  소품 {a.Props.Count}, 이펙트 {a.Effects.Count}, 손 IK {a.IKs.Count}, 덧그림 {a.Additives.Count}, 셰이더 값 {a.Shaders.Count}, 숨김 {a.Visibilities.Count}");
        return a;
    }

    static string Abs(string rel) => string.IsNullOrEmpty(rel) ? null : BundleCache.AssetRoot + rel;

    static string Desc(MotionSegment s) =>
        s == null ? "없음" : $"{s.Body.name} {s.Length:F2}s(얼굴 {(s.Face ? "있음" : "없음")}, xaf {(s.Xaf != null ? "있음" : "없음")})";

    static MotionSegment Segment(InstallFiles files, BundleCache bundles, CharacterView view, string fbx)
    {
        if (fbx == null) return null;
        var body = CharacterAssembler.LoadAnimationClip(files, bundles, fbx);
        if (!body) return null;
        var seg = new MotionSegment { Body = body };
        (seg.Face, seg.Xaf) = CharacterAssembler.LoadFace(files, bundles, view, fbx);
        return seg;
    }

    static string[] ClipTimelines(GameData data, string fbx)
    {
        if (fbx == null) return Array.Empty<string>();
        string name = fbx.Substring(fbx.LastIndexOf('/') + 1);
        if (name.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
        return data.ClipTimelines.TryGetValue(name, out var t) ? t : Array.Empty<string>();
    }

    static void ReadTimeline(InstallFiles files, BundleCache bundles, CharacterView view, Shader shader, ActionMotion a, int seg, string name,
        List<(ActionTrack track, object p)> pending, HashSet<string> seen, Action<string> log)
    {
        string path = files.FindResourcePathEndingWith("/" + name + ".StageTimeline.sjon.bin");
        if (path == null) { log?.Invoke($"  타임라인 없음: {name}"); return; }
        log?.Invoke($"  타임라인 [{seg}] {name}");
        var anims = new List<(string key, float begin, float end, bool inf, string clip)>();
        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var fxs = new List<(object p, float begin, float end, bool inf)>();
        foreach (var track in files.ReadSjon(path).Get("Tracks").Arr())
        {
            if (track.Get("IsEnabled") is bool en && !en) continue;
            string type = track.Get("Type").Str();
            foreach (var iv in track.Get("Intervals").Arr())
            {
                var p = iv.Get("Playable");
                float begin = iv.Get("BeginTime").F(), end = iv.Get("EndTime").F();
                bool inf = iv.Get("IsInfinite").B();
                string asset = p.Get("AssetPath").Str() ?? p.Get("PrefabPathGroups").Get("Default").Get("Lod0").Str() ?? p.Get("MaterialPath").Str();
                if (!string.IsNullOrEmpty(asset) && !seen.Add($"{seg}|{type}|{asset}|{begin:F2}")) continue;
                if (type == "Prop") AddProp(bundles, view, shader, a, seg, p, begin, end, inf, log);
                else if (type == "PropAnimation") anims.Add((p.Get("PropKey").Str() ?? "", begin, end, inf, p.Get("AnimationClipPath").Str()));
                else if (type == "Effect") fxs.Add((p, begin, end, inf));
                else if (type == "IKControl")
                {
                    var fade = p.Get("FadeInOutInfo");
                    a.IKs.Add(new ActionIK
                    {
                        Segment = seg, Begin = begin, End = end, Infinite = inf,
                        Control = p.Get("ControlBoneName").Str(), Mode = p.Get("IKControlTarget").Str(),
                        Target = p.Get("TargetBoneName").Str(), Displacement = p.Get("DisplacementTargetBoneName").Str(),
                        FadeIn = fade.Get("FadeInDuration").F(), FadeOut = fade.Get("FadeOutDuration").F(),
                        FadeInCurve = Curve(fade.Get("FadeInCurve").Get("Keyframes")), FadeOutCurve = Curve(fade.Get("FadeOutCurve").Get("Keyframes")),
                    });
                }
                else if (type == "AdditiveRenderer")
                {
                    var mat = bundles.LoadAsset<Material>(BundleCache.AssetRoot + asset);
                    if (!mat) { log?.Invoke($"  덧그림 재질 없음: {p.Get("MaterialPath").Str()}"); continue; }
                    var copy = new Material(mat) { name = mat.name };
                    a.Owned.Add(copy);
                    var t = new ActionAdditive { Segment = seg, Begin = begin, End = end, Infinite = inf, Material = copy };
                    t.Values.AddRange(Values(p));
                    a.Additives.Add(t);
                    pending.Add((t, p));
                }
                else if (type == "ShaderProperty")
                {
                    var t = new ActionShader { Segment = seg, Begin = begin, End = end, Infinite = inf };
                    t.Values.AddRange(Values(p));
                    a.Shaders.Add(t);
                    pending.Add((t, p));
                }
                else if (type == "ObjectVisibility")
                {
                    var t = new ActionVisibility { Segment = seg, Begin = begin, End = end, Infinite = inf, Visible = p.Get("IsVisible").B() };
                    a.Visibilities.Add(t);
                    pending.Add((t, p));
                }
                else
                {
                    // 소리(Sound·Footstep, SocialMotion.md 6장)·무기 넣음(ToolState) 등은 쓰지 않는다. 로그로만 남긴다.
                    skipped.TryGetValue(type ?? "?", out int n);
                    skipped[type ?? "?"] = n + 1;
                }
            }
        }
        if (skipped.Count > 0)
        {
            var parts = new List<string>();
            foreach (var kv in skipped) parts.Add(kv.Key + " " + kv.Value);
            log?.Invoke("    안 쓴 트랙: " + string.Join(", ", parts));
        }
        // 소품 클립은 같은 구간의 같은 키 소품(키가 비면 그 구간 첫 소품)에 붙인다.
        foreach (var (key, begin, end, inf, clipPath) in anims)
        {
            var prop = a.Props.Find(x => x.Segment == seg && x.Key == key) ?? (key.Length == 0 ? a.Props.Find(x => x.Segment == seg) : null);
            var clip = CharacterAssembler.LoadAnimationClip(files, bundles, Abs(clipPath));
            if (prop == null || !clip) { log?.Invoke($"  소품 클립 연결 실패: {key} {clipPath}"); continue; }
            prop.Clips.Add((seg, begin, end, inf, clip));
        }
        foreach (var (p, begin, end, inf) in fxs) AddEffect(bundles, view, a, seg, p, begin, end, inf, log);
    }

    static void AddProp(BundleCache bundles, CharacterView view, Shader shader, ActionMotion a, int seg, object p, float begin, float end, bool inf, Action<string> log)
    {
        string asset = p.Get("AssetPath").Str();
        var prefab = string.IsNullOrEmpty(asset) ? null : bundles.LoadAsset<GameObject>(BundleCache.AssetRoot + asset);
        if (!prefab) { log?.Invoke($"  소품 없음: {asset}"); return; }
        string boneName = p.Get("AttachBoneName").Str();
        Transform bone = string.IsNullOrEmpty(boneName) || boneName == "#MODEL_ROOT" ? view.transform : view.FindBone(boneName);
        if (!bone) { log?.Invoke($"  소품 본 없음: {boneName} → 루트"); bone = view.transform; }
        var go = UnityEngine.Object.Instantiate(prefab, bone, false);
        go.name = prefab.name;
        go.SetActive(false);
        var rot = Quaternion.Euler(CharacterAssembler.Vec(p.Get("DefaultLocalRotation"), Vector3.zero));
        go.transform.localPosition = CharacterAssembler.Vec(p.Get("DefaultLocalOffset"), Vector3.zero);
        go.transform.localRotation = rot;
        ConvertMaterials(go, shader, a.Owned);
        log?.Invoke($"  소품 [{seg}] {begin:F2}~{(inf ? "끝없음" : end.ToString("F2"))}s {asset} → {bone.name}");
        a.Props.Add(new ActionProp
        {
            Segment = seg, Begin = begin, End = end, Infinite = inf, Key = p.Get("PropKey").Str() ?? "", Go = go, Rotation = rot,
            FollowRotation = !(p.Get("FollowAttachBoneRotation") is bool f) || f, OnGround = p.Get("SetOnGround").B(),
        });
    }

    static void AddEffect(BundleCache bundles, CharacterView view, ActionMotion a, int seg, object p, float begin, float end, bool inf, Action<string> log)
    {
        string asset = p.Get("PrefabPathGroups").Get("Default").Get("Lod0").Str();
        var prefab = string.IsNullOrEmpty(asset) ? null : bundles.LoadAsset<GameObject>(BundleCache.AssetRoot + asset);
        if (!prefab) { log?.Invoke($"  이펙트 없음: {asset}"); return; }
        Transform r = null;
        if (p.Get("PosRefActor").Str() == "TimelineProp")
        {
            var info = p.Get("PosRefPropInfo");
            string key = info.Get("PropKey").Str() ?? "", boneName = info.Get("BoneName").Str();
            var prop = a.Props.Find(x => x.Segment == seg && x.Key == key);
            if (prop != null) r = string.IsNullOrEmpty(boneName) ? prop.Go.transform : FindChild(prop.Go.transform, boneName) ?? prop.Go.transform;
        }
        else
        {
            string boneName = p.Get("PosRefBoneName").Str();
            if (!string.IsNullOrEmpty(boneName)) r = view.FindBone(boneName);
        }
        var go = UnityEngine.Object.Instantiate(prefab, a.Holder.transform, false);
        go.name = prefab.name;
        go.SetActive(false);
        log?.Invoke($"  이펙트 [{seg}] {begin:F2}~{(inf ? "끝없음" : end.ToString("F2"))}s {asset} → {(r ? r.name : "루트")}");
        a.Effects.Add(new ActionEffect
        {
            Segment = seg, Begin = begin, End = end, Infinite = inf, Go = go, Ref = r,
            Systems = go.GetComponentsInChildren<ParticleSystem>(true),
            Attached = p.Get("PositionAttachmentInfo").Get("EffectAttachmentType").Str() == "Attached",
            OnGround = p.Get("SetOnGround").B(),
            Offset = CharacterAssembler.Vec(p.Get("LocalOffset"), Vector3.zero),
            Rotation = Quaternion.Euler(CharacterAssembler.Vec(p.Get("LocalRotation"), Vector3.zero)),
        });
    }

    /// <summary>
    /// 렌더러 대상. 슬롯: ActorModel(무기를 뺀 캐릭터 전체), MainTool·SubTool·SubSubTool(무기), Accessory0~2(귀·눈·얼굴 장식 [추정]),
    /// Custom: Head(머리 파츠) 또는 Renderers(이름). 이름이 있으면 캐릭터와 소품 렌더러에서 이름으로 찾는다.
    /// </summary>
    static void ResolveTargets(CharacterView view, ActionMotion a, ActionTrack track, object p, Action<string> log)
    {
        var list = track is ActionAdditive ad ? ad.Targets : track is ActionShader sh ? sh.Targets : ((ActionVisibility)track).Targets;
        var slots = new List<(string slot, string custom, List<string> names)>();
        if (track is ActionVisibility)
            foreach (var t in p.Get("Targets").Arr()) slots.Add((t.Get("Slot").Str(), null, Strs(t.Get("RendererNames"))));
        else slots.Add((p.Get("TargetSlot").Str(), p.Get("CustomTargetSlot").Str(), Strs(p.Get("TargetRendererNames"))));
        var propRenderers = new List<Renderer>();
        foreach (var pr in a.Props) if (pr.Go) propRenderers.AddRange(pr.Go.GetComponentsInChildren<Renderer>(true));
        foreach (var (slot, custom, names) in slots)
        {
            if (names.Count > 0)
            {
                foreach (var (_, r) in view.Renderers) if (r && names.Contains(r.name)) Add(r);
                foreach (var r in propRenderers) if (names.Contains(r.name)) Add(r);
                continue;
            }
            switch (slot)
            {
                case "ActorModel": foreach (var (key, r) in view.Renderers) if (key != AssemblyPlan.CostumeKey("Weapon")) Add(r); break;
                case "MainTool": case "SubTool": case "SubSubTool": Part(AssemblyPlan.CostumeKey("Weapon")); break;
                case "Accessory0": Part(AssemblyPlan.CostumeKey("EarDeco")); break;
                case "Accessory1": Part(AssemblyPlan.CostumeKey("EyeDeco")); break;
                case "Accessory2": Part(AssemblyPlan.CostumeKey("FaceDeco")); break;
                case "Custom": if (custom == "Head") Part("head"); break;
            }
        }
        void Part(string key) { foreach (var (k, r) in view.Renderers) if (k == key) Add(r); }
        void Add(Renderer r) { if (r && !list.Contains(r)) list.Add(r); }
    }

    static List<string> Strs(object o)
    {
        var l = new List<string>();
        foreach (var x in o.Arr()) if (x is string s && s.Length > 0) l.Add(s);
        return l;
    }

    static IEnumerable<ActionShaderValue> Values(object p)
    {
        foreach (var d in p.Get("ShaderProperyDataArr").Arr())
        {
            var c = d.Get("ColorCurve");
            yield return new ActionShaderValue
            {
                Name = d.Get("Name").Str(), Type = d.Get("Type").Str(), Float = d.Get("FloatValue").F(),
                Color = Col(d.Get("ColorValue")), Vector = Vec4(d.Get("VectorValue")),
                FloatCurve = Curve(d.Get("FloatCurve").Get("Keyframes")),
                ColorCurves = new[] { Curve(c.Get("KeyframesR")), Curve(c.Get("KeyframesG")), Curve(c.Get("KeyframesB")), Curve(c.Get("KeyframesA")) },
            };
        }
    }

    static Color Col(object o) { var l = o.Arr(); return l.Count >= 4 ? new Color(l[0].F(), l[1].F(), l[2].F(), l[3].F()) : Color.white; }
    static Vector4 Vec4(object o) { var l = o.Arr(); return l.Count >= 4 ? new Vector4(l[0].F(), l[1].F(), l[2].F(), l[3].F()) : Vector4.zero; }

    /// <summary>키 배열 [시간, 값, 들어오는 기울기, 나가는 기울기, …] 또는 [시간, 값]. 기울기 1e6 이상은 계단이다.</summary>
    static AnimationCurve Curve(object keys)
    {
        var l = keys.Arr();
        if (l.Count == 0) return null;
        var c = new AnimationCurve();
        foreach (var k in l)
        {
            var v = k.Arr();
            if (v.Count < 2) continue;
            float it = v.Count > 2 ? v[2].F() : 0f, ot = v.Count > 3 ? v[3].F() : 0f;
            c.AddKey(new Keyframe(v[0].F(), v[1].F(), Mathf.Abs(it) >= 1e5f ? float.PositiveInfinity : it, Mathf.Abs(ot) >= 1e5f ? float.PositiveInfinity : ot));
        }
        if (l.Count > 0 && l[0].Arr().Count <= 2)
            for (int i = 0; i < c.length; i++)
            {
                // [시간, 값] 쌍은 직선으로 잇는다.
                var k = c[i];
                k.inTangent = i > 0 ? (k.value - c[i - 1].value) / Mathf.Max(1e-5f, k.time - c[i - 1].time) : 0f;
                k.outTangent = i < c.length - 1 ? (c[i + 1].value - k.value) / Mathf.Max(1e-5f, c[i + 1].time - k.time) : 0f;
                c.MoveKey(i, k);
            }
        return c;
    }

    static Transform FindChild(Transform t, string name)
    {
        foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
        return null;
    }

    /// <summary>소품의 캐릭터 셰이더(MMN/CH) 재질을 앱 캐릭터 셰이더로 바꾼다. 원본 그대로는 조명 값이 없어 검게 그려진다.</summary>
    static void ConvertMaterials(GameObject go, Shader shader, List<UnityEngine.Object> owned)
    {
        if (!shader) return;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (!m || !m.shader || !m.shader.name.StartsWith("MMN/CH/", StringComparison.Ordinal)) continue;
                mats[i] = CharacterAssembler.ConvertMaterial(m, shader, owned, false, out _);
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }
}
