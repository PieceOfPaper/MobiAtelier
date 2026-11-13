// 설치 폴더 에셋 번들(UnityFS)과 그 안의 직렬화 파일을 읽는 최소 리더. UnityEngine 에 의존하지 않는다.
//   용도: Unity 가 스크립트 없이 풀지 못하는 게임 MonoBehaviour(예: Beautify 후처리 프로파일)의 값을 타입트리로 읽는다(Background.md 8장).
//   지원 범위 [확인: 설치 폴더 번들 UnityFS 8, Unity 2021.3, LZ4HC 블록]: 압축 없음·LZ4·LZ4HC 블록, 직렬화 파일 버전 17~22, 타입트리 있는 파일.
//   LZMA 블록과 타입트리 없는 파일은 지원하지 않는다(예외).
//   타입트리의 형 이름과 Unity 내장 공통 필드 이름(m_Name 등)은 읽지 않는다. 잎 값은 바이트 크기로, 배열은 노드 플래그로, 정렬은 메타 플래그(0x4000)로 판단한다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public sealed class UnityBundle
{
    /// <summary>번들 안 파일 이름(CAB-…) → 내용.</summary>
    public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

    public static UnityBundle Read(byte[] data)
    {
        var r = new BigReader(data);
        string sig = r.CString();
        if (sig != "UnityFS") throw new InvalidDataException("UnityFS 번들이 아닙니다: " + sig);
        r.U32(); r.CString(); r.CString();                      // 형식 버전, "5.x.x", 엔진 버전
        r.I64();                                                // 전체 크기
        int infoC = (int)r.U32(), infoU = (int)r.U32();
        uint flags = r.U32();
        if ((flags & 0x200) != 0) r.Align(16);
        byte[] infoRaw;
        if ((flags & 0x80) != 0) infoRaw = Slice(data, data.Length - infoC, infoC);
        else { infoRaw = Slice(data, r.Pos, infoC); r.Pos += infoC; }
        var info = new BigReader(Decompress(infoRaw, infoU, (int)(flags & 0x3F)));
        if ((flags & 0x200) != 0) r.Align(16);

        info.Pos += 16;                                         // 해시
        int blockCount = info.I32();
        var blocks = new (int u, int c, int f)[blockCount];
        long total = 0;
        for (int i = 0; i < blockCount; i++) { blocks[i] = ((int)info.U32(), (int)info.U32(), info.U16()); total += blocks[i].u; }
        var all = new byte[total];
        int dst = 0;
        foreach (var b in blocks)
        {
            var raw = Slice(data, r.Pos, b.c); r.Pos += b.c;
            var dec = Decompress(raw, b.u, b.f & 0x3F);
            Buffer.BlockCopy(dec, 0, all, dst, b.u); dst += b.u;
        }
        var bundle = new UnityBundle();
        int nodes = info.I32();
        for (int i = 0; i < nodes; i++)
        {
            long off = info.I64(), size = info.I64(); info.U32();
            string path = info.CString();
            bundle.Files[path] = Slice(all, (int)off, (int)size);
        }
        return bundle;
    }

    static byte[] Slice(byte[] a, int off, int len)
    {
        if (off < 0 || len < 0 || off + len > a.Length) throw new InvalidDataException("번들 범위가 잘못되었습니다.");
        var r = new byte[len]; Buffer.BlockCopy(a, off, r, 0, len); return r;
    }

    static byte[] Decompress(byte[] src, int size, int kind)
    {
        switch (kind)
        {
            case 0: return src;
            case 2: case 3: return Lz4(src, size);
            default: throw new NotSupportedException("지원하지 않는 번들 압축입니다: " + kind);
        }
    }

    /// <summary>LZ4 블록 해제.</summary>
    static byte[] Lz4(byte[] src, int size)
    {
        var dst = new byte[size];
        int s = 0, d = 0;
        while (s < src.Length)
        {
            int token = src[s++];
            int lit = token >> 4;
            if (lit == 15) { int b; do { b = src[s++]; lit += b; } while (b == 255); }
            Buffer.BlockCopy(src, s, dst, d, lit); s += lit; d += lit;
            if (s >= src.Length) break;
            int offset = src[s] | (src[s + 1] << 8); s += 2;
            int len = token & 15;
            if (len == 15) { int b; do { b = src[s++]; len += b; } while (b == 255); }
            len += 4;
            int from = d - offset;
            if (from < 0) throw new InvalidDataException("LZ4 데이터가 잘못되었습니다.");
            for (int i = 0; i < len; i++) dst[d++] = dst[from + i];
        }
        if (d != size) throw new InvalidDataException("LZ4 해제 크기가 다릅니다.");
        return dst;
    }

    sealed class BigReader
    {
        readonly byte[] a; public int Pos;
        public BigReader(byte[] a) { this.a = a; }
        public string CString() { int e = Array.IndexOf(a, (byte)0, Pos); var s = Encoding.UTF8.GetString(a, Pos, e - Pos); Pos = e + 1; return s; }
        public uint U32() { uint v = (uint)(a[Pos] << 24 | a[Pos + 1] << 16 | a[Pos + 2] << 8 | a[Pos + 3]); Pos += 4; return v; }
        public int I32() => (int)U32();
        public ushort U16() { var v = (ushort)(a[Pos] << 8 | a[Pos + 1]); Pos += 2; return v; }
        public long I64() { long hi = U32(), lo = U32(); return hi << 32 | lo; }
        public void Align(int n) { Pos = (Pos + n - 1) / n * n; }
    }
}

/// <summary>직렬화 파일(번들 안 CAB-…). 객체 표·외부 참조·타입트리를 읽고 객체를 값 트리로 푼다.</summary>
public sealed class UnitySerializedFile
{
    sealed class Node { public int Level, Size, Flags, Meta; public string Name; public List<Node> Children = new List<Node>(); }
    sealed class TypeInfo { public int ClassId; public Node Root; }
    public sealed class ObjectInfo { public long PathId; public long Start; public int Size; public int TypeIndex; public int ClassId; }

    readonly byte[] data;
    readonly bool little;
    readonly long dataOffset;
    readonly List<TypeInfo> types = new List<TypeInfo>();
    public readonly Dictionary<long, ObjectInfo> Objects = new Dictionary<long, ObjectInfo>();
    /// <summary>외부 참조 경로(PPtr 의 FileID - 1 번째). 번들 안 파일이면 "archive:/CAB-…/CAB-…".</summary>
    public readonly List<string> Externals = new List<string>();

    public UnitySerializedFile(byte[] data)
    {
        this.data = data;
        int pos = 0;
        uint U32BE() { uint v = (uint)(data[pos] << 24 | data[pos + 1] << 16 | data[pos + 2] << 8 | data[pos + 3]); pos += 4; return v; }
        U32BE(); U32BE();
        int version = (int)U32BE();
        uint off32 = U32BE();
        if (version < 17 || version > 22) throw new NotSupportedException("지원하지 않는 직렬화 파일 버전입니다: " + version);
        little = data[pos] == 0; pos += 4;
        dataOffset = off32;
        if (version >= 22)
        {
            U32BE();                                            // 메타데이터 크기
            pos += 8;                                           // 파일 크기
            dataOffset = (long)U32BE() << 32 | U32BE();
            pos += 8;
        }
        var r = new Reader(data, pos, little);
        r.CString();                                            // 엔진 버전
        r.I32();                                                // 대상 플랫폼
        bool typeTree = r.U8() != 0;
        if (!typeTree) throw new NotSupportedException("타입트리가 없는 직렬화 파일입니다.");
        int typeCount = r.I32();
        for (int i = 0; i < typeCount; i++) types.Add(ReadType(r, version));
        int objCount = r.I32();
        for (int i = 0; i < objCount; i++)
        {
            r.Align(4);
            var o = new ObjectInfo { PathId = r.I64() };
            o.Start = version >= 22 ? r.I64() : r.U32();
            o.Size = (int)r.U32();
            o.TypeIndex = r.I32();
            o.ClassId = o.TypeIndex >= 0 && o.TypeIndex < types.Count ? types[o.TypeIndex].ClassId : -1;
            Objects[o.PathId] = o;
        }
        int scripts = r.I32();
        for (int i = 0; i < scripts; i++) { r.I32(); r.Align(4); r.I64(); }
        int ext = r.I32();
        for (int i = 0; i < ext; i++) { r.CString(); r.Skip(16); r.I32(); Externals.Add(r.CString()); }
    }

    static TypeInfo ReadType(Reader r, int version)
    {
        var t = new TypeInfo { ClassId = r.I32() };
        r.U8();                                                 // stripped
        short scriptIndex = r.I16();
        if (t.ClassId == 114 || scriptIndex >= 0) r.Skip(16);   // 스크립트 ID
        r.Skip(16);                                             // 타입 해시
        int nodeCount = r.I32(), strSize = r.I32();
        int nodeSize = version >= 19 ? 32 : 24;
        int nodeStart = r.Pos;
        r.Skip(nodeCount * nodeSize);
        int strStart = r.Pos;
        r.Skip(strSize);
        var stack = new List<Node>();
        for (int i = 0; i < nodeCount; i++)
        {
            var n = new Reader(r.Data, nodeStart + i * nodeSize, r.Little);
            n.U16();
            var node = new Node { Level = n.U8(), Flags = n.U8() };
            n.U32();                                            // 형 이름(쓰지 않는다)
            uint nameOff = n.U32();
            node.Size = n.I32(); n.I32(); node.Meta = n.I32();
            // Unity 내장 공통 문자열(m_Name 등)은 표를 두지 않아 이름 대신 오프셋 키로 둔다. 게임 스크립트 필드는 파일 자체 이름이다.
            node.Name = (nameOff & 0x80000000) != 0 ? "#common:" + (nameOff & 0x7FFFFFFF) : ReadCString(r.Data, strStart + (int)nameOff);
            while (stack.Count > node.Level) stack.RemoveAt(stack.Count - 1);
            if (stack.Count > 0) stack[stack.Count - 1].Children.Add(node);
            stack.Add(node);
            if (i == 0) t.Root = node;
        }
        if (version >= 21) { int deps = r.I32(); r.Skip(deps * 4); }
        return t;
    }

    static string ReadCString(byte[] a, int pos)
    {
        int e = Array.IndexOf(a, (byte)0, pos);
        return Encoding.UTF8.GetString(a, pos, e - pos);
    }

    /// <summary>객체를 값 트리로 푼다. 구조체는 Dictionary(필드 이름 → 값), 배열은 List, 잎은 원시 바이트(byte[]).</summary>
    public Dictionary<string, object> Read(long pathId)
    {
        if (!Objects.TryGetValue(pathId, out var o)) return null;
        var root = types[o.TypeIndex].Root;
        var r = new Reader(data, (int)(dataOffset + o.Start), little);
        return ReadValue(r, root) as Dictionary<string, object>;
    }

    /// <summary>타입트리 최상위에 이 필드가 있는 객체를 찾는다.</summary>
    public IEnumerable<long> ObjectsWithField(string field)
    {
        foreach (var o in Objects.Values)
        {
            var root = types[o.TypeIndex].Root;
            foreach (var c in root.Children) if (c.Name == field) { yield return o.PathId; break; }
        }
    }

    object ReadValue(Reader r, Node n)
    {
        object v;
        if (n.Children.Count == 0)
        {
            v = r.Bytes(Math.Max(0, n.Size));
        }
        else if (n.Children.Count == 1 && (n.Children[0].Flags & 1) != 0)
        {
            v = ReadArray(r, n.Children[0]);                    // vector / string: 자식이 배열
        }
        else if ((n.Flags & 1) != 0)
        {
            v = ReadArray(r, n);
        }
        else
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var c in n.Children) d[c.Name] = ReadValue(r, c);
            v = d;
        }
        if ((n.Meta & 0x4000) != 0) r.Align(4);
        return v;
    }

    object ReadArray(Reader r, Node arr)
    {
        int count = r.I32();
        var elem = arr.Children[1];
        if (elem.Children.Count == 0 && elem.Size == 1)
        {
            var bytes = r.Bytes(count);                         // 바이트 배열(문자열 포함)
            if ((arr.Meta & 0x4000) != 0) r.Align(4);
            return bytes;
        }
        var list = new List<object>(count);
        for (int i = 0; i < count; i++) list.Add(ReadValue(r, elem));
        if ((arr.Meta & 0x4000) != 0) r.Align(4);
        return list;
    }

    sealed class Reader
    {
        public readonly byte[] Data; public int Pos; public readonly bool Little;
        public Reader(byte[] d, int p, bool little) { Data = d; Pos = p; Little = little; }
        public byte U8() => Data[Pos++];
        public short I16() { var v = Little ? (short)(Data[Pos] | Data[Pos + 1] << 8) : (short)(Data[Pos] << 8 | Data[Pos + 1]); Pos += 2; return v; }
        public ushort U16() => (ushort)I16();
        public uint U32()
        {
            uint v = Little ? (uint)(Data[Pos] | Data[Pos + 1] << 8 | Data[Pos + 2] << 16 | Data[Pos + 3] << 24)
                            : (uint)(Data[Pos] << 24 | Data[Pos + 1] << 16 | Data[Pos + 2] << 8 | Data[Pos + 3]);
            Pos += 4; return v;
        }
        public int I32() => (int)U32();
        public long I64() { long a = U32(), b = U32(); return Little ? (b << 32 | a) : (a << 32 | b); }
        public string CString() { var s = ReadCString(Data, Pos); Pos += Encoding.UTF8.GetByteCount(s) + 1; return s; }
        public void Skip(int n) => Pos += n;
        public void Align(int n) => Pos = (Pos + n - 1) / n * n;
        public byte[] Bytes(int n) { if (Pos + n > Data.Length) throw new InvalidDataException("직렬화 파일 범위를 넘었습니다."); var b = new byte[n]; Buffer.BlockCopy(Data, Pos, b, 0, n); Pos += n; return b; }
    }

    // 값 트리 도우미(잎은 리틀 엔디언 바이트, 설치 폴더 Windows 번들 [확인]).
    public static float F(object leaf) => leaf is byte[] b && b.Length >= 4 ? BitConverter.ToSingle(b, 0) : 0f;
    public static int I(object leaf) => leaf is byte[] b ? (b.Length >= 4 ? BitConverter.ToInt32(b, 0) : b.Length > 0 ? b[0] : 0) : 0;
    public static long L(object leaf) => leaf is byte[] b && b.Length >= 8 ? BitConverter.ToInt64(b, 0) : I(leaf);
    public static object Field(object o, string name) => o is Dictionary<string, object> d && d.TryGetValue(name, out var v) ? v : null;
}
