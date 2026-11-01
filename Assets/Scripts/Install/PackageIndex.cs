// 패키지 index 리더. <install>\<패키지>\index 와 <install>\<패키지>\Starter\index 를 읽어
// 경로 → location(blob 번호 | Starter 비트 | 엔트리), 번들 의존성을 만든다.
//   location u32: 0~14비트 blob 번호, 15비트 Starter 여부, 16~31비트 blob 안 엔트리 번호
// UnityEngine 에 의존하지 않는다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public sealed class PackageIndex
{
    static readonly byte[] Magic = { 0xBA, 0xC3, 0xA9, 0xE6 };

    public readonly string Root;
    public readonly Dictionary<string, uint> PathToLoc = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<uint, uint[]> Deps = new Dictionary<uint, uint[]>();

    PackageIndex(string root) { Root = root; }

    public static string IndexPath(string root) => Path.Combine(root, "index");
    public static string StarterIndexPath(string root) => Path.Combine(root, "Starter", "index");

    public string BlobPath(uint loc) =>
        Path.Combine(((loc >> 15) & 1) == 1 ? Path.Combine(Root, "Starter") : Root, (loc & 0x7FFF).ToString("D4") + ".blob");

    public static int Entry(uint loc) => (int)(loc >> 16);

    /// <summary>루트 index 와 Starter\index 를 함께 읽는다. 같은 경로는 루트 쪽을 쓴다.</summary>
    public static PackageIndex Load(string root)
    {
        var ix = new PackageIndex(root);
        var starter = StarterIndexPath(root);
        if (File.Exists(starter)) ix.Read(starter, false);
        ix.Read(IndexPath(root), true);
        return ix;
    }

    void Read(string path, bool overwrite)
    {
        byte[] d;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            d = new byte[fs.Length];
            int got = 0;
            while (got < d.Length) { int r = fs.Read(d, got, d.Length - got); if (r <= 0) break; got += r; }
        }
        if (d.Length < 8 || d[0] != Magic[0] || d[1] != Magic[1] || d[2] != Magic[2] || d[3] != Magic[3])
            throw new InvalidDataException("index 머리 형식이 다릅니다: " + path);
        int p = 4;
        uint U() { uint v = BitConverter.ToUInt32(d, p); p += 4; return v; }
        int n = (int)U();
        var locs = new uint[n];
        for (int i = 0; i < n; i++) locs[i] = U();
        int m = (int)U();
        for (int i = 0; i < m; i++)
        {
            uint loc = U(); int cnt = (int)U();
            var ds = new uint[cnt];
            for (int k = 0; k < cnt; k++) ds[k] = U();
            Deps[loc] = ds;
        }
        if (U() != n) throw new InvalidDataException("index 해시 구역 개수가 맞지 않습니다: " + path);
        p += 4 * (n + 1);
        int recStart = p;
        p += 8 * n;
        int b = (int)U(); p += 4 * b;
        int ld = (int)U(); var dirs = ParseDirTable(d, p, ld); p += ld;
        int ln = (int)U(); var names = LzText.Table(d, p, ln); p += ln;
        int le = (int)U(); var exts = LzText.Table(d, p, le);
        for (int i = 0; i < n; i++)
        {
            uint x = BitConverter.ToUInt32(d, recStart + 8 * i), y = BitConverter.ToUInt32(d, recStart + 8 * i + 4);
            int dirOff = (int)(x & 0xFFFFFF);
            int nameOff = (int)(((y & 0xFFFFF) << 8) | (x >> 24));
            int extOff = (int)(y >> 20);
            string dir = dirOff == 0 ? "" : (dirs.TryGetValue(dirOff, out var dd) ? dd : null);
            if (dir == null || !names.TryGetValue(nameOff, out var name) || !exts.TryGetValue(extOff, out var ext)) continue;
            string full = dir.Length > 0 ? dir + "/" + name + ext : name + ext;
            if (PathToLoc.ContainsKey(full) && !overwrite) continue;
            PathToLoc[full] = locs[i];
        }
    }

    static Dictionary<int, string> ParseDirTable(byte[] d, int start, int len)
    {
        var nodes = new Dictionary<int, (string name, int parent)>();
        int p = 1 + d[start];
        while (p < len)
        {
            int st = p;
            while (p < len && d[start + p] > 3) p++;
            string name = Encoding.UTF8.GetString(d, start + st, p - st);
            int n = p < len ? d[start + p] : 0;
            long dist = 0;
            for (int k = 0; k < n; k++) dist |= (long)d[start + p + 1 + k] << (8 * k);
            nodes[st] = (name, dist != 0 ? st - (int)dist : -1);
            p += 1 + n;
        }
        var cache = new Dictionary<int, string>();
        string Full(int o)
        {
            if (cache.TryGetValue(o, out var r)) return r;
            var (name, parent) = nodes[o];
            r = parent < 0 || !nodes.ContainsKey(parent) ? name : Full(parent) + "/" + name;
            cache[o] = r;
            return r;
        }
        var outp = new Dictionary<int, string>();
        foreach (var o in nodes.Keys) outp[o] = Full(o);
        return outp;
    }
}

/// <summary>index 파일 이름 표·시트 문자열 풀·심볼 문자열에 쓰는 LZ 토큰 스트림.</summary>
public static class LzText
{
    /// <summary>오프셋 i 에서 0 바이트까지의 문자열.</summary>
    public static string At(byte[] st, int i)
    {
        var cur = new List<byte>(64);
        while (true)
        {
            byte c = st[i];
            if (c == 0) return Encoding.UTF8.GetString(cur.ToArray());
            if (c < 0x80) { for (int k = 0; k < c; k++) cur.Add(st[i + 1 + k]); i += 1 + c; }
            else
            {
                int v = ((c & 0x7F) << 8) | st[i + 1];
                int src = i - (v >> 5) - 1, cnt = (v & 31) + 1;
                for (int k = 0; k < cnt; k++) cur.Add(st[src + k]);
                i += 2;
            }
        }
    }

    /// <summary>[start, start+len) 구간의 모든 문자열. 키는 구간 안 시작 오프셋.</summary>
    public static Dictionary<int, string> Table(byte[] d, int start, int len)
    {
        var outp = new Dictionary<int, string>();
        var cur = new List<byte>(64);
        int i = 0, s = 0;
        while (i < len)
        {
            byte c = d[start + i];
            if (c == 0) { outp[s] = Encoding.UTF8.GetString(cur.ToArray()); cur.Clear(); i++; s = i; }
            else if (c < 0x80) { for (int k = 0; k < c; k++) cur.Add(d[start + i + 1 + k]); i += 1 + c; }
            else
            {
                int v = ((c & 0x7F) << 8) | d[start + i + 1];
                int src = i - (v >> 5) - 1, cnt = (v & 31) + 1;
                for (int k = 0; k < cnt; k++) cur.Add(d[start + src + k]);
                i += 2;
            }
        }
        return outp;
    }
}
