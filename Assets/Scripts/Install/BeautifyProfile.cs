// 배경 프리팹에 붙은 게임 후처리(Kronnect Beautify) 프로파일 값을 설치 폴더 번들에서 읽는다(Background.md 8장).
//   Unity 는 게임 스크립트가 없어 Beautify 값을 풀지 못하므로 UnityBundle 로 타입트리를 직접 읽는다. UnityEngine 에 의존하지 않는다.
//   경로: 프리팹 번들의 Volume(sharedProfile) → 프로파일(components) → Beautify 컴포넌트의 덮어쓴 값.
//   번들을 열 때마다 해제하므로 배경을 바꿀 때 한 번만 부른다.
using System;
using System.Collections.Generic;

public sealed class BeautifyProfile
{
    /// <summary>덮어쓴 값(필드 이름 → 첫 성분). 덮어쓰지 않은 필드는 없다.</summary>
    public readonly Dictionary<string, float> Values = new Dictionary<string, float>(StringComparer.Ordinal);

    public bool TryGet(string field, out float v) => Values.TryGetValue(field, out v);

    /// <summary>
    /// 노출 배수(tonemapExposurePre × tonemapBrightnessPost, 덮어쓴 것만). 둘 다 없으면 1.
    /// [확인: 벨벳 의상실 앱 배경색 × 1.8 ≈ 게임, 후처리 옵션 단계와 무관]
    /// </summary>
    public float Exposure
    {
        get
        {
            float e = 1f;
            if (TryGet("tonemapExposurePre", out var a)) e *= a;
            if (TryGet("tonemapBrightnessPost", out var b)) e *= b;
            return e;
        }
    }

    /// <summary>프리팹(Assets/PatchableAssets/… 전체 경로)의 첫 Volume 프로파일. 프리팹에 Volume 이 없으면 null. 형식이 다르면 예외.</summary>
    public static BeautifyProfile FromPrefab(InstallFiles files, string prefabAssetPath)
    {
        if (!files.Assets.PathToLoc.TryGetValue(prefabAssetPath, out uint loc)) return null;
        var prefab = OpenFirst(files, loc);
        if (prefab == null) return null;
        foreach (long id in prefab.ObjectsWithField("sharedProfile"))
        {
            var vol = prefab.Read(id);
            var ptr = UnitySerializedFile.Field(vol, "sharedProfile");
            int fileId = UnitySerializedFile.I(UnitySerializedFile.Field(ptr, "m_FileID"));
            long pathId = UnitySerializedFile.L(UnitySerializedFile.Field(ptr, "m_PathID"));
            if (pathId == 0) continue;
            UnitySerializedFile file = prefab;
            if (fileId > 0)
            {
                string cab = CabName(fileId <= prefab.Externals.Count ? prefab.Externals[fileId - 1] : null);
                file = cab != null ? FindCab(files, loc, cab) : null;
                if (file == null) continue;
            }
            var profile = file.Read(pathId);
            if (profile == null) continue;
            var result = new BeautifyProfile();
            if (UnitySerializedFile.Field(profile, "components") is List<object> comps)
                foreach (var c in comps)
                {
                    if (UnitySerializedFile.I(UnitySerializedFile.Field(c, "m_FileID")) != 0) continue;
                    var comp = file.Read(UnitySerializedFile.L(UnitySerializedFile.Field(c, "m_PathID")));
                    if (comp == null || !comp.ContainsKey("tonemapExposurePre")) continue;
                    foreach (var kv in comp)
                    {
                        if (!(kv.Value is Dictionary<string, object> p) || !p.TryGetValue("m_OverrideState", out var ov) || UnitySerializedFile.I(ov) == 0) continue;
                        if (p.TryGetValue("m_Value", out var val) && val is byte[] bytes && bytes.Length == 4)
                            result.Values[kv.Key] = UnitySerializedFile.F(bytes);
                    }
                }
            return result;
        }
        return null;
    }

    /// <summary>"archive:/CAB-x/CAB-x" → "CAB-x".</summary>
    static string CabName(string external)
    {
        if (string.IsNullOrEmpty(external)) return null;
        int i = external.LastIndexOf('/');
        string name = i >= 0 ? external.Substring(i + 1) : external;
        return name.StartsWith("CAB-", StringComparison.OrdinalIgnoreCase) ? name : null;
    }

    static UnitySerializedFile OpenFirst(InstallFiles files, uint loc)
    {
        var bundle = OpenBundle(files, loc);
        foreach (var kv in bundle.Files)
            if (!kv.Key.EndsWith(".resS", StringComparison.OrdinalIgnoreCase) && !kv.Key.EndsWith(".resource", StringComparison.OrdinalIgnoreCase))
                return new UnitySerializedFile(kv.Value);
        return null;
    }

    /// <summary>의존 번들 중 이 CAB 를 가진 번들의 직렬화 파일.</summary>
    static UnitySerializedFile FindCab(InstallFiles files, uint loc, string cab)
    {
        if (!files.Assets.Deps.TryGetValue(loc, out var deps)) return null;
        foreach (var d in deps)
        {
            var bundle = OpenBundle(files, d);
            if (bundle.Files.TryGetValue(cab, out var bytes)) return new UnitySerializedFile(bytes);
        }
        return null;
    }

    static UnityBundle OpenBundle(InstallFiles files, uint loc)
    {
        var bytes = files.Blobs.Read(files.Assets.BlobPath(loc), PackageIndex.Entry(loc), out _);
        return UnityBundle.Read(bytes);
    }
}
