// 게임 환경 볼륨(ResourcesPackage `VolumeData/<키>.volume.sjon.bin`)을 읽는다. UnityEngine 에 의존하지 않는다(Background.md 8장).
//   VolumeData = GlobalGroup + CustomGroups(이름별). 그룹의 KeyFrames 는 시각(Time, 하루 0~1)과 섹션 몇 칸만 가진다(칸마다 따로 키).
//   섹션: MainLight·Skybox·SkyUnlit1·SkyUnlit2·SkyClouds·GlobalEnvironment·PostProcessing·CharacterLight·SelectiveSSAO.
//   값 이름은 "섹션.필드"(예: "MainLight.Intensity"), 값은 숫자 배열(실수 1개·색 4개·회전 4개)이다.
// 배경 시트의 VolumeGroupName 이 있으면 그 사용자 그룹이 가진 칸만 GlobalGroup 값을 덮어쓴다 [추정].
using System;
using System.Collections.Generic;

public sealed class EnvironmentVolumeData
{
    public const string Folder = "VolumeData/";

    public string Key, Group, Path;
    /// <summary>값 이름 → 시각순 키 목록.</summary>
    readonly Dictionary<string, List<KeyValuePair<float, float[]>>> tracks =
        new Dictionary<string, List<KeyValuePair<float, float[]>>>(StringComparer.Ordinal);

    public IEnumerable<string> Names => tracks.Keys;
    public int Count => tracks.Count;

    /// <summary>키에 맞는 리소스 경로. 이름 끝에 확장자가 겹친 파일(X.volume.sjon.volume.sjon.bin)도 찾는다. 없으면 null.</summary>
    public static string ResourcePath(InstallFiles files, string key)
    {
        if (files == null || string.IsNullOrEmpty(key)) return null;
        string exact = Folder + key + ".volume.sjon.bin";
        if (files.HasResource(exact)) return exact;
        string prefix = Folder + key + ".volume.sjon.";
        foreach (var p in files.Resources.PathToLoc.Keys)
            if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    /// <summary>볼륨을 읽는다. 키가 비었으면 null. 파일이 없거나 형식이 다르면 예외.</summary>
    public static EnvironmentVolumeData Load(InstallFiles files, string key, string group)
    {
        if (string.IsNullOrEmpty(key)) return null;
        string path = ResourcePath(files, key) ?? throw new System.IO.FileNotFoundException("환경 볼륨이 설치 폴더에 없습니다: " + key);
        return Parse(files.ReadSjon(path), key, group, path);
    }

    public static EnvironmentVolumeData Parse(object root, string key, string group, string path = null)
    {
        if (root.Get("$type").Str() != "VolumeData") throw new FormatException("환경 볼륨 형식이 다릅니다: " + key);
        var v = new EnvironmentVolumeData { Key = key, Group = string.IsNullOrEmpty(group) ? null : group, Path = path };
        v.AddGroup(root.Get("GlobalGroup"), false);
        if (v.Group != null)
            foreach (var g in root.Get("CustomGroups").Arr())
                if (g.Get("Name").Str() == v.Group) { v.AddGroup(g, true); break; }
        return v;
    }

    void AddGroup(object g, bool overrideGlobal)
    {
        var local = new Dictionary<string, List<KeyValuePair<float, float[]>>>(StringComparer.Ordinal);
        foreach (var kf in g.Get("KeyFrames").Arr())
        {
            if (!(kf is Dictionary<string, object> d)) continue;
            float time = kf.Get("Time").F();
            foreach (var sec in d)
            {
                if (!(sec.Value is Dictionary<string, object> fields)) continue;
                foreach (var f in fields)
                {
                    if (f.Key == "$type") continue;
                    var val = ToFloats(f.Value);
                    if (val == null) continue;
                    string name = sec.Key + "." + f.Key;
                    if (!local.TryGetValue(name, out var list)) local[name] = list = new List<KeyValuePair<float, float[]>>();
                    list.Add(new KeyValuePair<float, float[]>(time, val));
                }
            }
        }
        foreach (var t in local)
        {
            if (!overrideGlobal && tracks.ContainsKey(t.Key)) continue;
            t.Value.Sort((a, b) => a.Key.CompareTo(b.Key));
            tracks[t.Key] = t.Value;
        }
    }

    static float[] ToFloats(object o)
    {
        switch (o)
        {
            case double d: return new[] { (float)d };
            case bool b: return new[] { b ? 1f : 0f };
            case List<object> l:
                var r = new float[l.Count];
                for (int i = 0; i < l.Count; i++) { if (!(l[i] is double x)) return null; r[i] = (float)x; }
                return r;
            default: return null;
        }
    }

    /// <summary>
    /// 시각 time(0~1)의 값. 키가 하나면 그 값, 여럿이면 앞뒤 키를 선형 보간한다(하루가 돌아 이어진다고 보고 마지막→처음도 잇는다) [추정].
    /// 스튜디오 배경 볼륨(FashionShopPreviewUI·System_Costume_Studio·NaoZone)은 보간 결과가 시각과 관계없다 [확인: 여러 키 칸은 같은 값].
    /// </summary>
    public bool TryGet(string name, float time, out float[] value)
    {
        value = null;
        if (!tracks.TryGetValue(name, out var keys) || keys.Count == 0) return false;
        if (keys.Count == 1) { value = keys[0].Value; return true; }
        int next = keys.FindIndex(k => k.Key > time);
        int prev = next < 0 ? keys.Count - 1 : next - 1;
        if (next < 0) next = 0;
        if (prev < 0) prev = keys.Count - 1;
        var a = keys[prev]; var b = keys[next];
        float span = b.Key - a.Key; if (span <= 0) span += 1f;
        float pos = time - a.Key; if (pos < 0) pos += 1f;
        float u = span > 1e-6f ? Math.Min(1f, pos / span) : 0f;
        int n = Math.Min(a.Value.Length, b.Value.Length);
        value = new float[n];
        for (int i = 0; i < n; i++) value[i] = a.Value[i] + (b.Value[i] - a.Value[i]) * u;
        return true;
    }
}
