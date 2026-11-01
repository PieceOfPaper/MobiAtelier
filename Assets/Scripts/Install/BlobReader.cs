// NNNN.blob 리더와 ResourcesPackage 엔트리 복호화.
//   blob: 매직 BA C3 A9 E5 + u32 머리 크기 + 엔트리 끝 오프셋 표. 엔트리 i = [offs[i], offs[i+1]) (파일 절대 위치)
//   ResourcesPackage 엔트리: RomuTrio 키 스트림 XOR(hint1 = 오프셋, hint2 = 크기) → u32 원본 크기 + raw deflate
// UnityEngine 에 의존하지 않는다. 설치 폴더 파일은 읽기 전용·공유 모드로만 연다.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

public sealed class BlobReader
{
    readonly Dictionary<string, uint[]> tables = new Dictionary<string, uint[]>(StringComparer.OrdinalIgnoreCase);

    static FileStream Open(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    public uint[] Table(string blob)
    {
        lock (tables)
        {
            if (tables.TryGetValue(blob, out var t)) return t;
            using (var fs = Open(blob))
            {
                var head = ReadExactly(fs, 8);
                if (head[0] != 0xBA || head[1] != 0xC3 || head[2] != 0xA9 || head[3] != 0xE5)
                    throw new InvalidDataException("blob 머리 형식이 다릅니다: " + blob);
                uint headerSize = BitConverter.ToUInt32(head, 4);
                if (headerSize < 12 || headerSize > fs.Length) throw new InvalidDataException("blob 머리 크기가 잘못되었습니다: " + blob);
                var rest = ReadExactly(fs, (int)headerSize - 8);
                int count = (int)((headerSize - 4) / 4);
                t = new uint[count];
                t[0] = headerSize;
                for (int i = 1; i < count; i++) t[i] = BitConverter.ToUInt32(rest, (i - 1) * 4);
            }
            tables[blob] = t;
            return t;
        }
    }

    public (long offset, int size) Range(string blob, int entry)
    {
        var t = Table(blob);
        if (entry < 0 || entry + 1 >= t.Length) throw new InvalidDataException($"blob 엔트리 번호가 범위 밖입니다: {blob}#{entry}");
        return (t[entry], (int)(t[entry + 1] - t[entry]));
    }

    public byte[] Read(string blob, int entry, out long offset)
    {
        var (off, size) = Range(blob, entry);
        offset = off;
        using (var fs = Open(blob))
        {
            if (off + size > fs.Length) throw new InvalidDataException($"blob 엔트리가 파일 끝을 넘습니다: {blob}#{entry}");
            fs.Seek(off, SeekOrigin.Begin);
            return ReadExactly(fs, size);
        }
    }

    static byte[] ReadExactly(Stream s, int size)
    {
        var buf = new byte[size];
        int got = 0;
        while (got < size) { int r = s.Read(buf, got, size - got); if (r <= 0) break; got += r; }
        if (got != size) throw new EndOfStreamException("파일이 예상보다 짧습니다.");
        return buf;
    }
}

public static class ResourceCodec
{
    const ulong Mul = 0xD3833E804F4C574B;
    static ulong Rotl(ulong v, int k) => (v << k) | (v >> (64 - k));

    static void Deobfuscate(byte[] buf, ulong h1, ulong h2)
    {
        unchecked
        {
            ulong t = (~h2 ^ h1) + 0x171974E5E5BDDA11;
            ulong x = t * Mul;
            ulong y = Rotl(h2 - h1 + 0x14137AA69C0ECB04, 12);
            ulong z = Rotl(t - h2 + 0x7B6119B368DF44C9, 44);
            for (int i = 0; i < buf.Length; i += 8)
            {
                ulong k = x;
                for (int j = 0; j < 8 && i + j < buf.Length; j++) buf[i + j] ^= (byte)(k >> (8 * j));
                ulong xo = x, yo = y;
                x = z * Mul;
                y = Rotl(y - xo, 12);
                z = Rotl(z - yo, 44);
            }
        }
    }

    /// <summary>raw 를 제자리에서 복호화한 뒤 압축을 푼다.</summary>
    public static byte[] Decode(byte[] raw, long offset)
    {
        if (raw.Length < 4) throw new InvalidDataException("리소스 엔트리가 너무 짧습니다.");
        Deobfuscate(raw, (ulong)offset, (ulong)raw.Length);
        int size = BitConverter.ToInt32(raw, 0);
        if (size == 0) return Array.Empty<byte>();
        if (size < 0) throw new InvalidDataException("리소스 원본 크기가 잘못되었습니다.");
        var outp = new byte[size];
        using (var ms = new MemoryStream(raw, 4, raw.Length - 4))
        using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
        {
            int got = 0;
            while (got < size) { int r = ds.Read(outp, got, size - got); if (r <= 0) break; got += r; }
            if (got != size) throw new InvalidDataException($"리소스 크기가 맞지 않습니다 (머리 {size}, 실제 {got}).");
        }
        return outp;
    }
}
