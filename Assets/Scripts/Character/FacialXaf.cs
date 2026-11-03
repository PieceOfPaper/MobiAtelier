// 얼굴 애니메이션(.xaf)을 시간에 따라 평가한다. 트랙 종류:
//   Morpher    : 블렌드셰이프 가중치(0~100). 눈 깜빡임 MT_Blink* 등
//   Visibility : 메시 표시(0/1). 눈 하이라이트 Mesh_EyeHigh 는 깜빡이는 동안 꺼진다
//   Shader     : 머티리얼 값. "이름.x" 는 벡터 성분
// 시간 단위는 프레임(FrameRate, 보통 30)이다. 키 보간: Constant 는 계단, Flat 은 기울기 0 Hermite(부드러운 출입), 그 밖은 선형.
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class FacialXaf
{
    enum Kind { Morpher, Visibility, Shader }

    struct Key { public float Time, Value; public bool Constant, Flat; }

    sealed class Track
    {
        public Kind Kind;
        public Key[] Keys;
        public SkinnedMeshRenderer Renderer;
        public int BlendShape = -1;
        public string Property;
        public int Component = -1;
        public float Last = float.NaN;
    }

    readonly List<Track> tracks = new List<Track>();
    public int StartFrame { get; private set; }
    public int EndFrame { get; private set; }
    public float FrameRate { get; private set; } = 30f;
    public float Length => FrameRate > 0 ? (EndFrame - StartFrame) / FrameRate : 0f;
    public int TrackCount => tracks.Count;

    /// <summary>xaf 트랙을 캐릭터 렌더러(게임 오브젝트 이름 기준)에 연결한다. 대상이 없는 트랙은 버린다.</summary>
    public static FacialXaf Bind(object anim, CharacterView view)
    {
        var x = new FacialXaf { StartFrame = anim.Get("StartFrame").I(), EndFrame = anim.Get("EndFrame").I() };
        float fr = anim.Get("FrameRate").F();
        if (fr > 0) x.FrameRate = fr;
        foreach (var data in anim.Get("AnimationDatas").Arr())
        {
            string obj = data.Get("AnimationObjectName").Str();
            if (obj == null) continue;
            SkinnedMeshRenderer r = null;
            foreach (var (_, rr) in view.Renderers) if (rr && rr.gameObject.name == obj) { r = rr; break; }
            if (!r) continue;
            foreach (var tr in data.Get("KeyframeTracks").Arr())
            {
                var keys = tr.Get("Keyframes").Arr();
                if (keys.Count == 0) continue;
                var t = new Track { Renderer = r, Keys = new Key[keys.Count] };
                for (int i = 0; i < keys.Count; i++)
                {
                    string type = keys[i].Get("OutTangentType").Str();
                    t.Keys[i] = new Key { Time = keys[i].Get("Time").F(), Value = keys[i].Get("Value").F(), Constant = type == "Constant", Flat = type == "Flat" };
                }
                string prop = tr.Get("AnimationPropertyName").Str();
                switch (tr.Get("AnimationType").Str())
                {
                    case "Morpher":
                        t.Kind = Kind.Morpher;
                        t.BlendShape = r.sharedMesh && prop != null ? r.sharedMesh.GetBlendShapeIndex(prop) : -1;
                        if (t.BlendShape < 0) continue;
                        break;
                    case "Visibility":
                        t.Kind = Kind.Visibility;
                        break;
                    case "Shader":
                        if (string.IsNullOrEmpty(prop)) continue;
                        t.Kind = Kind.Shader;
                        int dot = prop.LastIndexOf('.');
                        t.Property = dot > 0 ? prop.Substring(0, dot) : prop;
                        t.Component = dot > 0 ? "xyzw".IndexOf(prop[dot + 1]) : -1;
                        break;
                    default: continue;
                }
                x.tracks.Add(t);
            }
        }
        return x;
    }

    /// <summary><paramref name="seconds"/>초(시작 프레임 기준) 값을 적용한다. 값이 바뀐 트랙만 쓴다.</summary>
    public void Sample(float seconds)
    {
        float f = StartFrame + seconds * FrameRate;
        foreach (var t in tracks)
        {
            if (!t.Renderer) continue;
            float v = Evaluate(t.Keys, f);
            if (v == t.Last) continue;
            t.Last = v;
            switch (t.Kind)
            {
                case Kind.Morpher:
                    t.Renderer.SetBlendShapeWeight(t.BlendShape, v);
                    break;
                case Kind.Visibility:
                    if (t.Renderer.gameObject.activeSelf != (v != 0)) t.Renderer.gameObject.SetActive(v != 0);
                    break;
                case Kind.Shader:
                    foreach (var m in t.Renderer.sharedMaterials)
                    {
                        if (!m || !m.HasProperty(t.Property)) continue;
                        if (t.Component >= 0) { var vec = m.GetVector(t.Property); vec[t.Component] = v; m.SetVector(t.Property, vec); }
                        else m.SetFloat(t.Property, v);
                    }
                    break;
            }
        }
    }

    static float Evaluate(Key[] k, float f)
    {
        if (f <= k[0].Time) return k[0].Value;
        int n = k.Length;
        if (f >= k[n - 1].Time) return k[n - 1].Value;
        int i = 0;
        while (i + 1 < n && k[i + 1].Time <= f) i++;
        var a = k[i]; var b = k[i + 1];
        if (a.Constant) return a.Value;
        float span = b.Time - a.Time;
        float s = span > 0 ? (f - a.Time) / span : 1f;
        if (a.Flat) s = s * s * (3f - 2f * s);
        return Mathf.LerpUnclamped(a.Value, b.Value, s);
    }
}
