// AssetPackages 번들을 blob 안 오프셋에서 직접 연다(AssetBundle.LoadFromFile(blob, 0, offset)).
// 같은 번들을 두 번 열면 Unity 오류가 나므로 location 별로 캐시하고, 의존 번들을 먼저 연다.
// 조립한 메시·머티리얼이 번들 에셋을 참조하므로 Dispose(Unload(false))는 캐릭터를 없앤 뒤에 한다.
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BundleCache : IDisposable
{
    public const string AssetRoot = "Assets/PatchableAssets/";

    readonly InstallFiles files;
    readonly Dictionary<uint, AssetBundle> bundles = new Dictionary<uint, AssetBundle>();
    readonly HashSet<uint> loading = new HashSet<uint>();

    public BundleCache(InstallFiles files) { this.files = files; }

    public int Count => bundles.Count;

    public AssetBundle BundleOf(string assetPath) =>
        assetPath != null && files.Assets.PathToLoc.TryGetValue(assetPath, out var loc) ? Load(loc) : null;

    public T LoadAsset<T>(string assetPath) where T : UnityEngine.Object
    {
        var b = BundleOf(assetPath);
        return b ? b.LoadAsset<T>(assetPath) : null;
    }

    AssetBundle Load(uint loc)
    {
        if (bundles.TryGetValue(loc, out var b)) return b;
        if (!loading.Add(loc)) return null;            // 순환 의존성
        try
        {
            if (files.Assets.Deps.TryGetValue(loc, out var deps))
                foreach (var d in deps) Load(d);
            string blob = files.Assets.BlobPath(loc);
            var (off, _) = files.Blobs.Range(blob, PackageIndex.Entry(loc));
            b = AssetBundle.LoadFromFile(blob, 0, (ulong)off);
            if (!b) Debug.LogWarning($"[MobiAtelier] 번들을 열지 못했습니다: {blob}#{PackageIndex.Entry(loc)}");
            bundles[loc] = b;
            return b;
        }
        finally { loading.Remove(loc); }
    }

    public void Dispose()
    {
        foreach (var b in bundles.Values) if (b) b.Unload(false);
        bundles.Clear();
    }
}
