// 조립된 캐릭터의 몸·얼굴·무기 모션을 재생한다.
// 구간: 한 번만 재생하는 도입 구간(무기 꺼내기·넣기, 꾸미기 포즈 시작)이 있으면 그것을 먼저, 그다음 반복 구간을 계속 재생한다.
// 몸: Generic AnimationClip. 본 경로(_COM/_Pelvis/...)가 조립 루트 기준 본 계층과 같아 Animator + PlayableGraph 로 바로 바인딩된다.
//     번들에서 읽은 클립은 SampleAnimation 이 동작하지 않으므로 그래프를 수동 평가한다.
//     클립은 본 위치·스케일까지 덮어쓰므로 평가 뒤 회전만 남기고 위치·스케일을 조립 값(나이·키 .bdc 포함)으로 되돌린다.
// 얼굴: <클립>_Facial.fbx 클립(볼·눈알·눈썹·턱·입술 본)을 _Head 아래 본만 덮는 레이어로 섞는다(_COM~_Head 경로 값은 쓰지 않는다).
//       <클립>_Facial.xaf(눈 깜빡임 블렌드셰이프·표시·셰이더 값)는 FacialXaf 로 같은 시간에 평가한다.
//       구간 클립에 얼굴 클립이 없으면(직업 자세 등) 꾸미기 아이들의 얼굴을 자기 길이로 따로 반복한다.
// 무기: 장착 파츠마다 Animator 를 두고 파츠 자체 클립(ModelPartAnimation, 활시위·오브 등)을 같은 시간에 평가한다.
//       도입 구간 중에는 무기 상태(넣음·꺼냄)를 구간 표(ToolState 트랙)대로 바꿔 장착점을 옮긴다.
// 소셜 모션(SocialMotion.md): 기본 모션 위에 행동(시작 → 반복) 층을 페이드로 섞는다. 기본 모션은 아래에서 계속 흐른다.
//       행동 중 무기는 넣은 위치로 옮기고 HideWeapon 행동은 숨긴다. 소품(Prop·PropAnimation)과 이펙트(Effect) 구간을 같은 시간으로 켜고 끈다.
//       손 IK(IKControl)는 본 후처리 뒤에 팔 두 본(위팔·아래팔)을 돌려 손바닥을 목표로 옮긴다. 손 방향(월드)은 클립 값을 유지한다.
//       덧그림(AdditiveRenderer)·셰이더 값(ShaderProperty)·숨김(ObjectVisibility)은 구간 동안 렌더러에 적용하고 끝나면 되돌린다.
//       시작 클립만 있는 행동은 끝나면 페이드 아웃하고 ActionFinished 를 알린다.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>장착점에 붙는 파츠(무기 등) 하나. 넣음·꺼냄 두 상태의 붙일 본과 로컬 값을 가진다.</summary>
public sealed class ToolMount
{
    public struct Placement
    {
        public bool Valid, Hidden;
        public Transform Parent;
        public Vector3 Position, Scale;
        public Quaternion Rotation;
    }

    public string Model, ToolType, AnimationGroup;
    public Transform Root;
    public Placement Undrawn, Drawn;
    /// <summary>파츠 렌더러와 프리팹 기준 켜짐 상태. 숨김 상태가 풀리면 원래 상태로 돌린다.</summary>
    public readonly List<(GameObject go, bool active)> Renderers = new List<(GameObject, bool)>();
    /// <summary>복제한 파츠 본과 기본 로컬 값(파츠 클립이 없는 구간에서 되돌린다).</summary>
    public readonly List<(Transform t, Vector3 p, Quaternion r, Vector3 s)> Bones = new List<(Transform, Vector3, Quaternion, Vector3)>();
    public bool? Current;
    bool forceHidden;

    public void Apply(bool drawn)
    {
        if (Current == drawn) return;
        var p = drawn ? Drawn : Undrawn;
        if (!p.Valid) p = drawn ? Undrawn : Drawn;
        if (!p.Valid || !Root) return;
        Current = drawn;
        Root.SetParent(p.Parent, false);
        Root.localPosition = p.Position;
        Root.localRotation = p.Rotation;
        Root.localScale = p.Scale;
        ApplyVisibility(p);
    }

    /// <summary>장착 상태와 관계없이 숨긴다(소셜 모션의 HideWeapon).</summary>
    public void SetForceHidden(bool hidden)
    {
        if (forceHidden == hidden) return;
        forceHidden = hidden;
        if (Current.HasValue) ApplyVisibility(Current.Value ? (Drawn.Valid ? Drawn : Undrawn) : (Undrawn.Valid ? Undrawn : Drawn));
    }

    void ApplyVisibility(Placement p)
    {
        foreach (var (go, active) in Renderers) if (go) go.SetActive(active && !p.Hidden && !forceHidden);
    }

    public void ResetBones()
    {
        foreach (var (t, p, r, s) in Bones) if (t) { t.localPosition = p; t.localRotation = r; t.localScale = s; }
    }
}

/// <summary>모션 구간 하나(도입 또는 반복).</summary>
public sealed class MotionSegment
{
    public AnimationClip Body;
    /// <summary>이 구간 클립의 얼굴. 없으면 꾸미기 아이들 얼굴을 따로 반복한다.</summary>
    public AnimationClip Face;
    public FacialXaf Xaf;
    /// <summary>장착 파츠별 파츠 클립(ToolMount 순서). 없으면 null.</summary>
    public AnimationClip[] Tools;
    /// <summary>구간 중 무기 상태 기본값(true = 꺼냄).</summary>
    public bool Drawn;
    /// <summary>구간 중 무기 상태 예외 구간(파츠 종류 MainTool/SubTool/SubSubTool, 상태, 시작·끝 초).</summary>
    public readonly List<(string toolType, bool drawn, float begin, float end)> ToolSpans = new List<(string, bool, float, float)>();

    public float Length => Body ? Body.length : 0f;

    public bool DrawnAt(string toolType, float t)
    {
        foreach (var s in ToolSpans) if (s.toolType == toolType && t >= s.begin && t < s.end) return s.drawn;
        return Drawn;
    }
}

public sealed class CharacterMotion : MonoBehaviour
{
    CharacterView view;
    Animator animator;
    PlayableGraph graph;
    AnimationMixerPlayable bodyMix, faceMix;
    readonly List<AnimationClipPlayable> bodyClips = new List<AnimationClipPlayable>();
    readonly List<AnimationClipPlayable> faceClips = new List<AnimationClipPlayable>();
    MotionSegment[] segments = Array.Empty<MotionSegment>();
    ToolMount[] mounts = Array.Empty<ToolMount>();
    (AnimationPlayableOutput output, AnimationMixerPlayable mix, AnimationClipPlayable[] clips)[] toolGraphs;
    bool hasIntro;
    AnimationClip fallbackFace;
    FacialXaf fallbackXaf;
    float fallbackLength;
    AvatarMask faceMask;
    int com = -1;
    float comScale = 1f;
    bool[] faceBone = Array.Empty<bool>();
    (Transform dup, Transform main, Quaternion rel)[] parts = Array.Empty<(Transform, Transform, Quaternion)>();
    double time, faceTime;
    int lastSegment = -1;

    // 소셜 모션 층: 기본(입력 0)과 행동(입력 1)을 섞는다.
    AnimationMixerPlayable topBody, topFace, actBody, actFace;
    readonly AnimationClipPlayable[] actBodyClips = new AnimationClipPlayable[2], actFaceClips = new AnimationClipPlayable[2];
    readonly List<(AnimationPlayableOutput output, AnimationMixerPlayable mix, AnimationClipPlayable[] clips)> propGraphs =
        new List<(AnimationPlayableOutput, AnimationMixerPlayable, AnimationClipPlayable[])>();
    ActionMotion action;
    double actionTime;
    float actionWeight;
    bool actionEnding, actionWasOn;
    /// <summary>Sample(t) 를 시험 촬영에서 직접 부를 때 이펙트를 그 시점까지 시뮬레이션한다(실행 중에는 실제 시간으로 재생).</summary>
    bool simulateEffects;
    MaterialPropertyBlock block;
    readonly HashSet<Renderer> blockedNow = new HashSet<Renderer>(), hiddenNow = new HashSet<Renderer>();
    readonly List<Renderer> scratch = new List<Renderer>();

    /// <summary>시작 클립만 있는 행동이 끝나 기본 모션으로 돌아왔다.</summary>
    public event Action ActionFinished;

    /// <summary>반복 구간의 몸 클립.</summary>
    public AnimationClip Clip => segments.Length > 0 ? segments[segments.Length - 1].Body : null;
    /// <summary>도입 구간 길이(초). 없으면 0.</summary>
    public float IntroLength => hasIntro ? segments[0].Length : 0f;
    /// <summary>재생 중인 소셜 모션(끝나는 페이드 중이면 null).</summary>
    public ActionMotion CurrentAction => action != null && !actionEnding ? action : null;
    /// <summary>소셜 모션 시작 뒤 지난 시간(초).</summary>
    public double ActionTime => actionTime;

    /// <param name="com">view.Bones 에서 최상위 _COM 의 순번(-1 이면 없음).</param>
    /// <param name="comScale">클립의 _COM 위치에 곱할 비율(보정 후 정지 포즈 높이 ÷ 원본 정지 포즈 높이).</param>
    /// <param name="partBones">파츠 전용 본(기본 회전이 다른 같은 이름 본)과 그 원래 본, 회전 차(원래 본 기본 회전⁻¹ × 전용 본 기본 회전).</param>
    /// <param name="intro">한 번 재생할 도입 구간. null 이면 없다.</param>
    /// <param name="loop">반복 구간.</param>
    /// <param name="faceClip">구간에 얼굴이 없을 때 쓰는 꾸미기 아이들 얼굴 본 클립.</param>
    /// <param name="faceTracks">그 얼굴 xaf.</param>
    internal void Init(CharacterView view, int com, float comScale, List<(Transform dup, Transform main, Quaternion rel)> partBones,
        MotionSegment intro, MotionSegment loop, AnimationClip faceClip, FacialXaf faceTracks, List<ToolMount> tools)
    {
        this.view = view; this.com = com; this.comScale = comScale;
        hasIntro = intro != null && intro.Body;
        segments = hasIntro ? new[] { intro, loop } : new[] { loop };
        mounts = tools.ToArray();
        fallbackFace = faceClip; fallbackXaf = faceTracks;
        fallbackLength = Mathf.Max(faceClip ? faceClip.length : 0f, faceTracks != null ? faceTracks.Length : 0f);
        parts = partBones.ToArray();
        // 같은 경로의 본이 둘이면 Animator 가 어느 쪽에 바인딩할지 정해지지 않는다. 전용 본 이름을 바꿔 원래 본에만 바인딩되게 한다.
        // 이름은 조립 중에만 쓰므로(FindBone 은 원래 본을 먼저 찾는다) 조립이 끝난 뒤 바꿔도 된다.
        foreach (var p in parts) p.dup.name += "#part";

        animator = gameObject.AddComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        graph = PlayableGraph.Create(name + " Motion");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var output = AnimationPlayableOutput.Create(graph, "Body", animator);

        bodyMix = AnimationMixerPlayable.Create(graph, segments.Length);
        for (int i = 0; i < segments.Length; i++)
        {
            var c = AnimationClipPlayable.Create(graph, segments[i].Body);
            graph.Connect(c, 0, bodyMix, i);
            bodyClips.Add(c);
        }
        actBody = AnimationMixerPlayable.Create(graph, 2);
        topBody = AnimationMixerPlayable.Create(graph, 2);
        graph.Connect(bodyMix, 0, topBody, 0);
        graph.Connect(actBody, 0, topBody, 1);
        topBody.SetInputWeight(0, 1f);
        topBody.SetInputWeight(1, 0f);

        Transform head = null;
        foreach (var b in view.Bones) if (b && b.name == "_Head") { head = b; break; }
        faceBone = new bool[view.Bones.Length];
        if (head)
        {
            // _Head 아래 본(헤어 등 얼굴 클립에 없는 본 포함)만 얼굴 레이어가 덮는다. 클립에 없는 값은 아래 레이어 값이 그대로 남는다.
            faceMask = new AvatarMask();
            var paths = new List<string>();
            for (int i = 0; i < view.Bones.Length; i++)
            {
                var b = view.Bones[i];
                if (!b || b == head || !b.IsChildOf(head)) continue;
                faceBone[i] = true;
                paths.Add(PathOf(b, transform));
            }
            faceMask.transformCount = paths.Count;
            for (int i = 0; i < paths.Count; i++) { faceMask.SetTransformPath(i, paths[i]); faceMask.SetTransformActive(i, true); }
            // 입력: 구간별 얼굴 + 마지막에 꾸미기 아이들 얼굴(대체).
            faceMix = AnimationMixerPlayable.Create(graph, segments.Length + 1);
            for (int i = 0; i <= segments.Length; i++)
            {
                var fc = i < segments.Length ? segments[i].Face : fallbackFace;
                var p = fc ? AnimationClipPlayable.Create(graph, fc) : default;
                if (fc) graph.Connect(p, 0, faceMix, i);
                faceClips.Add(p);
            }
            actFace = AnimationMixerPlayable.Create(graph, 2);
            topFace = AnimationMixerPlayable.Create(graph, 2);
            graph.Connect(faceMix, 0, topFace, 0);
            graph.Connect(actFace, 0, topFace, 1);
            topFace.SetInputWeight(0, 1f);
            topFace.SetInputWeight(1, 0f);
            var layers = AnimationLayerMixerPlayable.Create(graph, 2);
            graph.Connect(topBody, 0, layers, 0);
            graph.Connect(topFace, 0, layers, 1);
            layers.SetInputWeight(0, 1f);
            layers.SetInputWeight(1, 1f);
            layers.SetLayerMaskFromAvatarMask(1, faceMask);
            output.SetSourcePlayable(layers);
        }
        else output.SetSourcePlayable(topBody);

        // 장착 파츠 클립: 파츠 루트에 Animator 를 두고 같은 그래프의 출력으로 평가한다. 클립 경로는 프리팹 루트 기준이다.
        toolGraphs = new (AnimationPlayableOutput, AnimationMixerPlayable, AnimationClipPlayable[])[mounts.Length];
        for (int m = 0; m < mounts.Length; m++)
        {
            bool any = false;
            foreach (var s in segments) any |= s.Tools != null && m < s.Tools.Length && s.Tools[m];
            if (!any || !mounts[m].Root) continue;
            var ta = mounts[m].Root.gameObject.AddComponent<Animator>();
            ta.applyRootMotion = false;
            ta.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var to = AnimationPlayableOutput.Create(graph, "Tool " + mounts[m].Model, ta);
            var mix = AnimationMixerPlayable.Create(graph, segments.Length);
            var clips = new AnimationClipPlayable[segments.Length];
            for (int i = 0; i < segments.Length; i++)
            {
                var tc = segments[i].Tools != null && m < segments[i].Tools.Length ? segments[i].Tools[m] : null;
                if (!tc) continue;
                clips[i] = AnimationClipPlayable.Create(graph, tc);
                graph.Connect(clips[i], 0, mix, i);
            }
            to.SetSourcePlayable(mix);
            toolGraphs[m] = (to, mix, clips);
        }
    }

    static string PathOf(Transform t, Transform root)
    {
        string p = t.name;
        for (var q = t.parent; q && q != root; q = q.parent) p = q.name + "/" + p;
        return p;
    }

    // ------------------------------------------------------------ 소셜 모션

    /// <summary>
    /// 소셜 모션을 재생한다. 이전 행동은 바로 지운다. <paramref name="startTime"/> 초 지점부터 시작한다(다시 조립한 뒤 이어 재생).
    /// <paramref name="fadeIn"/> 이면 행동의 FadeIn 시간 동안 기본 모션에서 섞어 들어간다.
    /// 행동 소품·이펙트는 이 캐릭터 아래에 붙으며 캐릭터와 함께 없어진다.
    /// </summary>
    public void PlayAction(ActionMotion a, double startTime, bool fadeIn)
    {
        ClearAction(false);
        if (a == null || !graph.IsValid()) return;
        action = a;
        actionTime = Math.Max(0, startTime);
        actionEnding = false;
        actionWeight = fadeIn && a.FadeIn > 0 && startTime <= 0 ? 0f : 1f;
        for (int i = 0; i < 2; i++)
        {
            var seg = a.Segment(i);
            if (seg == null) continue;
            actBodyClips[i] = AnimationClipPlayable.Create(graph, seg.Body);
            graph.Connect(actBodyClips[i], 0, actBody, i);
            if (seg.Face && actFace.IsValid())
            {
                actFaceClips[i] = AnimationClipPlayable.Create(graph, seg.Face);
                graph.Connect(actFaceClips[i], 0, actFace, i);
            }
        }
        foreach (var p in a.Props)
        {
            var mix = default(AnimationMixerPlayable);
            AnimationClipPlayable[] clips = null;
            AnimationPlayableOutput output = default;
            if (p.Clips.Count > 0 && p.Go)
            {
                var pa = p.Go.GetComponent<Animator>();
                if (!pa) pa = p.Go.AddComponent<Animator>();
                pa.applyRootMotion = false;
                pa.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                pa.runtimeAnimatorController = null;
                output = AnimationPlayableOutput.Create(graph, "Prop " + p.Key, pa);
                mix = AnimationMixerPlayable.Create(graph, p.Clips.Count);
                clips = new AnimationClipPlayable[p.Clips.Count];
                for (int i = 0; i < p.Clips.Count; i++)
                {
                    clips[i] = AnimationClipPlayable.Create(graph, p.Clips[i].clip);
                    graph.Connect(clips[i], 0, mix, i);
                }
                output.SetSourcePlayable(mix);
            }
            propGraphs.Add((output, mix, clips));
        }
    }

    /// <summary>소셜 모션을 멈춘다. <paramref name="fade"/> 면 FadeOut 시간 동안 기본 모션으로 섞어 돌아간다.</summary>
    public void StopAction(bool fade)
    {
        if (action == null) return;
        if (fade && action.FadeOut > 0 && actionWeight > 0) actionEnding = true;
        else ClearAction(true);
    }

    void ClearAction(bool notify)
    {
        if (action == null) return;
        for (int i = 0; i < 2; i++)
        {
            if (actBodyClips[i].IsValid()) { graph.Disconnect(actBody, i); actBodyClips[i].Destroy(); }
            if (actFaceClips[i].IsValid()) { graph.Disconnect(actFace, i); actFaceClips[i].Destroy(); }
            actBodyClips[i] = default; actFaceClips[i] = default;
        }
        foreach (var (output, mix, clips) in propGraphs)
        {
            if (clips != null) foreach (var c in clips) if (c.IsValid()) c.Destroy();
            if (mix.IsValid()) mix.Destroy();
            if (output.IsOutputValid()) graph.DestroyOutput(output);
        }
        propGraphs.Clear();
        action.Dispose();
        action = null;
        actionEnding = false;
        actionWeight = 0f;
        if (notify) ActionFinished?.Invoke();
    }

    /// <summary>행동 시간 → (구간 0 시작·1 반복, 구간 안 시간). 시작 클립만 있는 행동이 끝났으면 마지막 프레임에 머문다.</summary>
    (int seg, double local, bool done) ActionPhase(double t)
    {
        var a = action;
        float startLen = a.Start != null ? a.Start.Length : 0f;
        if (a.Start != null && t < startLen) return (0, t, false);
        if (a.Loop != null)
        {
            double lt = t - startLen;
            float len = a.Loop.Length;
            return (1, len > 0 ? lt % len : 0, false);
        }
        return (0, startLen, true);
    }

    // ------------------------------------------------------------ 평가

    /// <summary>모션을 처음부터 <paramref name="t"/>초 지난 시점으로 평가해 본·얼굴·무기에 적용한다.</summary>
    public void Sample(double t) => Sample(t, fallbackLength > 0 ? t % fallbackLength : t);

    /// <summary>시험 촬영용: 기본 모션과 소셜 모션(있으면 섞지 않고 전부)을 <paramref name="t"/>초 시점으로 평가한다. 이펙트도 그 시점까지 시뮬레이션한다.</summary>
    public void SampleAll(double t)
    {
        if (action != null) { actionTime = t; actionWeight = 1f; actionEnding = false; }
        simulateEffects = true;
        Sample(t);
        simulateEffects = false;
    }

    void Sample(double t, double ft)
    {
        if (!graph.IsValid() || view == null || segments.Length == 0) return;
        int si; double lt;
        var loop = segments[segments.Length - 1];
        if (hasIntro && t < segments[0].Length) { si = 0; lt = t; }
        else
        {
            si = segments.Length - 1;
            lt = hasIntro ? t - segments[0].Length : t;
            if (loop.Length > 0) lt %= loop.Length;
        }
        var seg = segments[si];
        for (int i = 0; i < segments.Length; i++) bodyMix.SetInputWeight(i, i == si ? 1f : 0f);
        bodyClips[si].SetTime(lt);

        // 소셜 모션 층
        bool actOn = action != null;
        int asi = 0; double alt = 0; MotionSegment aseg = null;
        float aw = actOn ? Mathf.Clamp01(actionWeight) : 0f;
        if (actOn)
        {
            (asi, alt, _) = ActionPhase(actionTime);
            aseg = action.Segment(asi);
            for (int i = 0; i < 2; i++) actBody.SetInputWeight(i, i == asi ? 1f : 0f);
            if (actBodyClips[asi].IsValid()) actBodyClips[asi].SetTime(alt);
        }
        topBody.SetInputWeight(0, 1f - aw);
        topBody.SetInputWeight(1, aw);

        // 얼굴: 구간 얼굴이 있으면 그것을 같은 시간으로, 없으면 꾸미기 아이들 얼굴을 따로 반복한다.
        bool own = seg.Face;
        if (faceMix.IsValid())
        {
            for (int i = 0; i <= segments.Length; i++) faceMix.SetInputWeight(i, own ? (i == si ? 1f : 0f) : (i == segments.Length ? 1f : 0f));
            var fp = faceClips[own ? si : segments.Length];
            // 얼굴 클립이 몸보다 짧으면(남성 5.67초·몸 5.83초) 루프로 처음 값이 나오지 않게 끝 값에 멈춘다.
            if (fp.IsValid()) fp.SetTime(Math.Min(own ? lt : ft, fp.GetAnimationClip().length));
            bool actFaceOn = actOn && actFaceClips[asi].IsValid();
            if (actFaceOn)
            {
                for (int i = 0; i < 2; i++) actFace.SetInputWeight(i, i == asi ? 1f : 0f);
                actFaceClips[asi].SetTime(Math.Min(alt, actFaceClips[asi].GetAnimationClip().length));
            }
            topFace.SetInputWeight(0, actFaceOn ? 1f - aw : 1f);
            topFace.SetInputWeight(1, actFaceOn ? aw : 0f);
        }

        // 무기: 상태(장착점)와 파츠 클립. 소셜 모션 중에는 넣은 위치로 옮기고 파츠 클립을 쓰지 않는다.
        for (int m = 0; m < mounts.Length; m++)
        {
            mounts[m].Apply(actOn ? false : seg.DrawnAt(mounts[m].ToolType, (float)lt));
            mounts[m].SetForceHidden(actOn && action.HideWeapon);
            var tg = toolGraphs[m];
            if (!tg.mix.IsValid()) continue;
            var c = tg.clips[si];
            bool has = c.IsValid() && !actOn;
            tg.output.SetWeight(has ? 1f : 0f);
            for (int i = 0; i < segments.Length; i++) tg.mix.SetInputWeight(i, i == si && has ? 1f : 0f);
            if (has)
            {
                float len = c.GetAnimationClip().length;
                c.SetTime(c.GetAnimationClip().isLooping && len > 0 ? lt % len : Math.Min(lt, len));
            }
        }

        // 소품: 구간 안에서만 켜고 소품 클립을 구간 시작 기준 시간으로 평가한다.
        if (actOn)
            for (int p = 0; p < action.Props.Count; p++)
            {
                var prop = action.Props[p];
                bool on = prop.Go && prop.Segment == asi && ActionMotion.Inside(alt, prop.Begin, prop.End, prop.Infinite);
                if (prop.Go && prop.Go.activeSelf != on) prop.Go.SetActive(on);
                var (output, mix, clips) = propGraphs[p];
                if (!mix.IsValid()) continue;
                int pick = -1;
                if (on)
                    for (int i = 0; i < prop.Clips.Count; i++)
                    {
                        var pc = prop.Clips[i];
                        if (pc.segment == asi && ActionMotion.Inside(alt, pc.begin, pc.end, pc.infinite)) { pick = i; break; }
                    }
                output.SetWeight(pick >= 0 ? 1f : 0f);
                for (int i = 0; i < prop.Clips.Count; i++) mix.SetInputWeight(i, i == pick ? 1f : 0f);
                if (pick >= 0)
                {
                    var clip = prop.Clips[pick].clip;
                    double ct = alt - prop.Clips[pick].begin;
                    clips[pick].SetTime(clip.isLooping && clip.length > 0 ? ct % clip.length : Math.Min(ct, clip.length));
                }
            }

        graph.Evaluate(0);
        if (si != lastSegment || actOn != actionWasOn)
        {
            // 파츠 클립이 없는 구간으로 바뀌면 파츠 본을 기본 값으로 되돌린다.
            for (int m = 0; m < mounts.Length; m++)
                if (actOn || !toolGraphs[m].mix.IsValid() || !toolGraphs[m].clips[si].IsValid()) mounts[m].ResetBones();
            lastSegment = si;
            actionWasOn = actOn;
        }
        var b = view.Bones;
        for (int i = 0; i < b.Length; i++)
        {
            if (!b[i]) continue;
            bool p = view.HasPose[i];
            b[i].localScale = p ? view.PoseScl[i] : view.BindScl[i];
            if (i == com) b[i].localPosition *= comScale;
            else if (!faceBone[i]) b[i].localPosition = p ? view.PosePos[i] : view.BindPos[i];
            // 얼굴 본은 표정(턱·입술·눈썹 이동)이 위치에 있으므로 클립 위치를 쓴다. 머리 크기는 부모 _Head 스케일이 반영한다.
        }
        foreach (var (dup, main, rel) in parts)
            if (dup && main) dup.localRotation = main.localRotation * rel;
        if (actOn)
        {
            foreach (var prop in action.Props) prop.Place(transform);
            if (ApplyIK(asi, alt, aw))
            {
                foreach (var (dup, main, rel) in parts)
                    if (dup && main) dup.localRotation = main.localRotation * rel;
                foreach (var prop in action.Props) prop.Place(transform);
            }
        }
        // 표정 트랙(눈 깜빡임 등)은 섞을 수 없으므로 더 큰 쪽 하나만 평가한다.
        if (actOn && aw >= 0.5f && aseg?.Xaf != null) aseg.Xaf.Sample((float)alt);
        else if (own) seg.Xaf?.Sample((float)lt);
        else fallbackXaf?.Sample((float)ft);

        if (actOn)
        {
            UpdateRenderers(asi, alt);
            UpdateEffects(asi, alt);
        }
    }

    /// <summary>손 IK 구간을 적용한다. 하나라도 적용했으면 true.</summary>
    bool ApplyIK(int asi, double alt, float aw)
    {
        bool any = false;
        foreach (var ik in action.IKs)
        {
            if (!ik.Active(asi, alt)) continue;
            float w = ik.Weight(alt) * aw;
            if (w <= 0f) continue;
            var palm = view.FindBone(ik.Control);
            Transform hand = palm ? palm.parent : null, fore = hand ? hand.parent : null, upper = fore ? fore.parent : null;
            if (!upper) continue;
            Vector3 goal;
            if (ik.Mode == "DisplacementPosition")
            {
                var from = action.FindTarget(ik.Target, view);
                var to = action.FindTarget(ik.Displacement, view);
                if (!from || !to) continue;
                goal = palm.position + (to.position - from.position);
            }
            else
            {
                var t = action.FindTarget(ik.Target, view);
                if (!t) continue;
                goal = t.position;
            }
            SolveTwoBone(upper, fore, hand, hand.position + (goal - palm.position), w);
            any = true;
        }
        return any;
    }

    /// <summary>위팔·아래팔을 돌려 손목(hand)을 목표 쪽으로 옮긴다. 팔꿈치가 굽는 면은 지금 자세의 면을 유지한다. 손 월드 회전은 그대로 둔다.</summary>
    static void SolveTwoBone(Transform upper, Transform fore, Transform hand, Vector3 target, float weight)
    {
        Vector3 a = upper.position, b = fore.position, c = hand.position;
        Vector3 t = Vector3.Lerp(c, target, weight);
        float lab = (b - a).magnitude, lcb = (c - b).magnitude;
        if (lab < 1e-5f || lcb < 1e-5f) return;
        float lat = Mathf.Clamp((t - a).magnitude, Mathf.Abs(lab - lcb) + 1e-4f, lab + lcb - 1e-4f);
        var handRot = hand.rotation;
        Vector3 axis = Vector3.Cross(c - a, b - a);
        if (axis.sqrMagnitude < 1e-10f) axis = Vector3.Cross(c - a, upper.up);
        if (axis.sqrMagnitude < 1e-10f) return;
        axis.Normalize();
        float ac0 = Vector3.Angle(c - a, b - a), ba0 = Vector3.Angle(a - b, c - b);
        float ac1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f)) * Mathf.Rad2Deg;
        float ba1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f)) * Mathf.Rad2Deg;
        upper.rotation = Quaternion.AngleAxis(ac1 - ac0, axis) * upper.rotation;
        fore.rotation = Quaternion.AngleAxis(ba1 - ba0, axis) * fore.rotation;
        upper.rotation = Quaternion.FromToRotation(hand.position - a, t - a) * upper.rotation;
        hand.rotation = handRot;
    }

    /// <summary>덧그림·셰이더 값·숨김 구간을 렌더러에 적용하고, 끝난 구간은 되돌린다.</summary>
    void UpdateRenderers(int asi, double alt)
    {
        foreach (var ad in action.Additives) ad.Set(ad.Active(asi, alt), alt);

        blockedNow.Clear();
        foreach (var sh in action.Shaders)
        {
            if (!sh.Active(asi, alt)) continue;
            if (block == null) block = new MaterialPropertyBlock();
            float u = sh.Progress(alt);
            foreach (var r in sh.Targets)
            {
                if (!r) continue;
                r.GetPropertyBlock(block);
                foreach (var v in sh.Values) v.Apply(block, u);
                r.SetPropertyBlock(block);
                blockedNow.Add(r);
                action.Blocked.Add(r);
            }
        }
        scratch.Clear();
        foreach (var r in action.Blocked) if (!blockedNow.Contains(r)) scratch.Add(r);
        foreach (var r in scratch) { if (r) r.SetPropertyBlock(null); action.Blocked.Remove(r); }

        hiddenNow.Clear();
        foreach (var vis in action.Visibilities)
            if (!vis.Visible && vis.Active(asi, alt))
                foreach (var r in vis.Targets) if (r) hiddenNow.Add(r);
        foreach (var r in hiddenNow) { r.forceRenderingOff = true; action.Hidden.Add(r); }
        scratch.Clear();
        foreach (var r in action.Hidden) if (!hiddenNow.Contains(r)) scratch.Add(r);
        foreach (var r in scratch) { if (r) r.forceRenderingOff = false; action.Hidden.Remove(r); }
    }

    /// <summary>이펙트를 구간에 맞춰 켜고 끈다. 반복 구간이 처음으로 돌아가면 다시 재생한다.</summary>
    void UpdateEffects(int asi, double alt)
    {
        foreach (var fx in action.Effects)
        {
            if (!fx.Go) continue;
            bool on = fx.Segment == asi && ActionMotion.Inside(alt, fx.Begin, fx.End, fx.Infinite);
            bool restart = on && (!fx.Active || alt < fx.LastLocal);
            fx.LastLocal = alt;
            if (!on)
            {
                if (fx.Active) { fx.Active = false; fx.Go.SetActive(false); }
                continue;
            }
            if (restart)
            {
                fx.Active = true;
                fx.Spawn(transform);
                fx.Go.SetActive(true);
                foreach (var ps in fx.Systems) if (ps) { ps.Clear(true); ps.Play(true); }
            }
            if (simulateEffects)
                foreach (var ps in fx.Systems) if (ps) ps.Simulate((float)(alt - fx.Begin), true, true);
        }
    }

    void LateUpdate()
    {
        if (segments.Length == 0) return;
        float dt = Time.deltaTime;
        float loopLen = segments[segments.Length - 1].Length;
        time += dt;
        double start = IntroLength;
        if (time >= start && loopLen > 0) time = start + (time - start) % loopLen;
        faceTime += dt;
        if (fallbackLength > 0) faceTime %= fallbackLength;
        if (action != null)
        {
            actionTime += dt;
            if (!actionEnding)
            {
                actionWeight = action.FadeIn > 0 ? Mathf.Min(1f, actionWeight + dt / action.FadeIn) : 1f;
                if (ActionPhase(actionTime).done) StopAction(true);
            }
            else actionWeight -= action.FadeOut > 0 ? dt / action.FadeOut : 1f;
        }
        Sample(time, faceTime);
        if (action != null && actionEnding && actionWeight <= 0f)
        {
            ClearAction(true);
            Sample(time, faceTime);
        }
    }

    void OnDestroy()
    {
        action?.Dispose();
        action = null;
        if (graph.IsValid()) graph.Destroy();
        if (faceMask) { if (Application.isPlaying) Destroy(faceMask); else DestroyImmediate(faceMask); }
    }
}
