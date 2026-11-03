// 조립된 캐릭터에 몸·얼굴 모션을 같은 시간으로 반복 재생한다.
// 몸: Generic AnimationClip. 본 경로(_COM/_Pelvis/...)가 조립 루트 기준 본 계층과 같아 Animator + PlayableGraph 로 바로 바인딩된다.
//     번들에서 읽은 클립은 SampleAnimation 이 동작하지 않으므로 그래프를 수동 평가한다.
//     클립은 본 위치·스케일까지 덮어쓰므로 평가 뒤 회전만 남기고 위치·스케일을 조립 값(나이·키 .bdc 포함)으로 되돌린다.
// 얼굴: <클립>_Facial.fbx 클립(볼·눈알·눈썹·턱·입술 본)을 _Head 아래 본만 덮는 레이어로 섞는다(_COM~_Head 경로 값은 쓰지 않는다).
//       <클립>_Facial.xaf(눈 깜빡임 블렌드셰이프·표시·셰이더 값)는 FacialXaf 로 같은 시간에 평가한다.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public sealed class CharacterMotion : MonoBehaviour
{
    CharacterView view;
    Animator animator;
    PlayableGraph graph;
    AnimationClipPlayable body, face;
    AnimationClip clip;
    FacialXaf xaf;
    AvatarMask faceMask;
    int com = -1;
    float comScale = 1f;
    bool[] faceBone = System.Array.Empty<bool>();
    (Transform dup, Transform main, Quaternion rel)[] parts = System.Array.Empty<(Transform, Transform, Quaternion)>();
    double time;

    public AnimationClip Clip => clip;

    /// <param name="com">view.Bones 에서 최상위 _COM 의 순번(-1 이면 없음).</param>
    /// <param name="comScale">클립의 _COM 위치에 곱할 비율(보정 후 정지 포즈 높이 ÷ 원본 정지 포즈 높이).</param>
    /// <param name="partBones">파츠 전용 본(기본 회전이 다른 같은 이름 본)과 그 원래 본, 회전 차(원래 본 기본 회전⁻¹ × 전용 본 기본 회전).</param>
    /// <param name="faceClip">얼굴 본 클립. null 이면 얼굴 본은 정지 포즈를 유지한다.</param>
    /// <param name="faceTracks">얼굴 xaf. null 이면 조립 때 적용한 첫 프레임 값을 유지한다.</param>
    internal void Init(CharacterView view, AnimationClip clip, int com, float comScale, List<(Transform dup, Transform main, Quaternion rel)> partBones,
        AnimationClip faceClip, FacialXaf faceTracks)
    {
        this.view = view; this.clip = clip; this.com = com; this.comScale = comScale; xaf = faceTracks;
        parts = partBones.ToArray();
        // 같은 경로의 본이 둘이면 Animator 가 어느 쪽에 바인딩할지 정해지지 않는다. 전용 본 이름을 바꿔 원래 본에만 바인딩되게 한다.
        // 이름은 조립 중에만 쓰므로(FindBone 은 원래 본을 먼저 찾는다) 조립이 끝난 뒤 바꿔도 된다.
        foreach (var p in parts) p.dup.name += "#part";

        animator = gameObject.AddComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        graph = PlayableGraph.Create(name + " Motion");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        body = AnimationClipPlayable.Create(graph, clip);
        var output = AnimationPlayableOutput.Create(graph, "Body", animator);

        Transform head = null;
        foreach (var b in view.Bones) if (b && b.name == "_Head") { head = b; break; }
        faceBone = new bool[view.Bones.Length];
        if (faceClip && head)
        {
            // _Head 아래 본(헤어 등 얼굴 클립에 없는 본 포함)만 얼굴 레이어가 덮는다. 클립에 없는 값은 아래 레이어 값이 그대로 남는다.
            faceMask = new AvatarMask();
            var paths = new List<string>();
            for (int i = 0; i < view.Bones.Length; i++)
            {
                var b = view.Bones[i];
                if (!b || b == head || !b.IsChildOf(head)) continue;
                faceBone[i] = true;
                paths.Add(PathOf(b));
            }
            faceMask.transformCount = paths.Count;
            for (int i = 0; i < paths.Count; i++) { faceMask.SetTransformPath(i, paths[i]); faceMask.SetTransformActive(i, true); }
            face = AnimationClipPlayable.Create(graph, faceClip);
            var mixer = AnimationLayerMixerPlayable.Create(graph, 2);
            graph.Connect(body, 0, mixer, 0);
            graph.Connect(face, 0, mixer, 1);
            mixer.SetInputWeight(0, 1f);
            mixer.SetInputWeight(1, 1f);
            mixer.SetLayerMaskFromAvatarMask(1, faceMask);
            output.SetSourcePlayable(mixer);
        }
        else output.SetSourcePlayable(body);
    }

    string PathOf(Transform t)
    {
        string p = t.name;
        for (var q = t.parent; q && q != transform; q = q.parent) p = q.name + "/" + p;
        return p;
    }

    /// <summary>모션을 <paramref name="t"/>초 시점으로 평가해 본·얼굴에 적용한다.</summary>
    public void Sample(double t)
    {
        if (!graph.IsValid() || view == null) return;
        body.SetTime(t);
        // 얼굴 클립이 몸보다 짧으면(남성 5.67초·몸 5.83초) 루프로 처음 값이 나오지 않게 끝 값에 멈춘다.
        if (face.IsValid()) face.SetTime(System.Math.Min(t, face.GetAnimationClip().length));
        graph.Evaluate(0);
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
        xaf?.Sample((float)t);
    }

    void LateUpdate()
    {
        if (!clip) return;
        time += Time.deltaTime;
        if (clip.length > 0) time %= clip.length;
        Sample(time);
    }

    void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
        if (faceMask) { if (Application.isPlaying) Destroy(faceMask); else DestroyImmediate(faceMask); }
    }
}
