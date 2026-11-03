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
        foreach (var (go, active) in Renderers) if (go) go.SetActive(active && !p.Hidden);
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
    MotionSegment[] segments = System.Array.Empty<MotionSegment>();
    ToolMount[] mounts = System.Array.Empty<ToolMount>();
    (AnimationPlayableOutput output, AnimationMixerPlayable mix, AnimationClipPlayable[] clips)[] toolGraphs;
    bool hasIntro;
    AnimationClip fallbackFace;
    FacialXaf fallbackXaf;
    float fallbackLength;
    AvatarMask faceMask;
    int com = -1;
    float comScale = 1f;
    bool[] faceBone = System.Array.Empty<bool>();
    (Transform dup, Transform main, Quaternion rel)[] parts = System.Array.Empty<(Transform, Transform, Quaternion)>();
    double time, faceTime;
    int lastSegment = -1;

    /// <summary>반복 구간의 몸 클립.</summary>
    public AnimationClip Clip => segments.Length > 0 ? segments[segments.Length - 1].Body : null;
    /// <summary>도입 구간 길이(초). 없으면 0.</summary>
    public float IntroLength => hasIntro ? segments[0].Length : 0f;

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

        Transform head = null;
        foreach (var b in view.Bones) if (b && b.name == "_Head") { head = b; break; }
        faceBone = new bool[view.Bones.Length];
        bool anyFace = fallbackFace;
        foreach (var s in segments) anyFace |= s.Face;
        if (anyFace && head)
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
            var layers = AnimationLayerMixerPlayable.Create(graph, 2);
            graph.Connect(bodyMix, 0, layers, 0);
            graph.Connect(faceMix, 0, layers, 1);
            layers.SetInputWeight(0, 1f);
            layers.SetInputWeight(1, 1f);
            layers.SetLayerMaskFromAvatarMask(1, faceMask);
            output.SetSourcePlayable(layers);
        }
        else output.SetSourcePlayable(bodyMix);

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

    /// <summary>모션을 처음부터 <paramref name="t"/>초 지난 시점으로 평가해 본·얼굴·무기에 적용한다.</summary>
    public void Sample(double t) => Sample(t, fallbackLength > 0 ? t % fallbackLength : t);

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

        // 얼굴: 구간 얼굴이 있으면 그것을 같은 시간으로, 없으면 꾸미기 아이들 얼굴을 따로 반복한다.
        bool own = seg.Face;
        if (faceMix.IsValid())
        {
            for (int i = 0; i <= segments.Length; i++) faceMix.SetInputWeight(i, own ? (i == si ? 1f : 0f) : (i == segments.Length ? 1f : 0f));
            var fp = faceClips[own ? si : segments.Length];
            // 얼굴 클립이 몸보다 짧으면(남성 5.67초·몸 5.83초) 루프로 처음 값이 나오지 않게 끝 값에 멈춘다.
            if (fp.IsValid()) fp.SetTime(System.Math.Min(own ? lt : ft, fp.GetAnimationClip().length));
        }

        // 무기: 상태(장착점)와 파츠 클립.
        for (int m = 0; m < mounts.Length; m++)
        {
            mounts[m].Apply(seg.DrawnAt(mounts[m].ToolType, (float)lt));
            var tg = toolGraphs[m];
            if (!tg.mix.IsValid()) continue;
            var c = tg.clips[si];
            bool has = c.IsValid();
            tg.output.SetWeight(has ? 1f : 0f);
            for (int i = 0; i < segments.Length; i++) tg.mix.SetInputWeight(i, i == si && has ? 1f : 0f);
            if (has)
            {
                float len = c.GetAnimationClip().length;
                c.SetTime(c.GetAnimationClip().isLooping && len > 0 ? lt % len : System.Math.Min(lt, len));
            }
        }

        graph.Evaluate(0);
        if (si != lastSegment)
        {
            // 파츠 클립이 없는 구간으로 바뀌면 파츠 본을 기본 값으로 되돌린다.
            for (int m = 0; m < mounts.Length; m++)
                if (!toolGraphs[m].mix.IsValid() || !toolGraphs[m].clips[si].IsValid()) mounts[m].ResetBones();
            lastSegment = si;
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
        if (own) seg.Xaf?.Sample((float)lt);
        else fallbackXaf?.Sample((float)ft);
    }

    void LateUpdate()
    {
        if (segments.Length == 0) return;
        float loopLen = segments[segments.Length - 1].Length;
        time += Time.deltaTime;
        double start = IntroLength;
        if (time >= start && loopLen > 0) time = start + (time - start) % loopLen;
        faceTime += Time.deltaTime;
        if (fallbackLength > 0) faceTime %= fallbackLength;
        Sample(time, faceTime);
    }

    void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
        if (faceMask) { if (Application.isPlaying) Destroy(faceMask); else DestroyImmediate(faceMask); }
    }
}
