// 설치 폴더(<install>)의 두 패키지 index 와 리소스 읽기. UnityEngine 에 의존하지 않는다.
//   AssetPackages    : Unity 에셋 번들(프리팹·머티리얼·텍스처). 번들 열기는 Character.BundleCache 가 맡는다.
//   ResourcesPackage : 모델 정의·본·포즈(SJON), 메시(SMM), 시트·심볼 테이블. 복호화해서 읽는다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

public sealed class InstallFiles
{
    public const string AssetPackagesDir = "AssetPackages";
    public const string ResourcesPackageDir = "ResourcesPackage";
    const string ModelDefSuffix = ".StageObjectModel.sjon.bin";

    public readonly string Root;
    public readonly PackageIndex Assets, Resources;
    public readonly BlobReader Blobs = new BlobReader();
    public readonly long IndexLoadMs;
    readonly Dictionary<string, string> modelDef = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    long resourceBytes;

    public InstallFiles(string root)
    {
        var sw = Stopwatch.StartNew();
        Root = root;
        Assets = PackageIndex.Load(Path.Combine(root, AssetPackagesDir));
        Resources = PackageIndex.Load(Path.Combine(root, ResourcesPackageDir));
        foreach (var p in Resources.PathToLoc.Keys)
        {
            if (!p.EndsWith(ModelDefSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            string file = p.Substring(p.LastIndexOf('/') + 1);
            modelDef[file.Substring(0, file.Length - ModelDefSuffix.Length)] = p;
        }
        IndexLoadMs = sw.ElapsedMilliseconds;
    }

    public long ResourceBytes => resourceBytes;
    public IEnumerable<string> ModelNames => modelDef.Keys;
    public int ModelCount => modelDef.Count;
    public string ModelDefPath(string model) => model != null && modelDef.TryGetValue(model, out var p) ? p : null;
    public bool HasResource(string path) => Resources.PathToLoc.ContainsKey(path);
    public bool HasAsset(string path) => Assets.PathToLoc.ContainsKey(path);

    public byte[] ReadResource(string path)
    {
        if (!Resources.PathToLoc.TryGetValue(path, out uint loc)) throw new FileNotFoundException("리소스가 index 에 없습니다: " + path);
        var raw = Blobs.Read(Resources.BlobPath(loc), PackageIndex.Entry(loc), out long off);
        System.Threading.Interlocked.Add(ref resourceBytes, raw.Length);
        return ResourceCodec.Decode(raw, off);
    }

    public object ReadSjon(string path) => Sjon.Decode(ReadResource(path));

    public string FindResourcePathEndingWith(string suffix)
    {
        foreach (var p in Resources.PathToLoc.Keys)
            if (p.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    public string FindAssetPathEndingWith(string suffix)
    {
        foreach (var p in Assets.PathToLoc.Keys)
            if (p.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }
}
