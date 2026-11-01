// 게임 데이터 시트 리더. ResourcesPackage 의 Sheetdata_Client/<세트>.sheet{meta,text,data}.bin 을 읽는다.
//   sheettext.bin : 문자열 풀(LZ 토큰 스트림)
//   sheetmeta.bin : u 시트 수 / 시트마다 u 이름(텍스트 오프셋), u RowIdType, u 행 수, u preload 크기, u 행 블록 크기
//                   / 시트마다 행 크기 u × 행 수 / 시트마다 preload 블록
//   sheetdata.bin : 행 블록을 이어붙인 것
// 파일에 필드 스키마가 없으므로 SheetSchemas 의 정의로 읽고, 모든 행이 기록된 크기와 정확히 맞는지 검사한다.
// 맞지 않으면 SheetFormatException 을 던진다(게임 업데이트로 시트 구조가 바뀐 경우).
// UnityEngine 에 의존하지 않는다.
using System;
using System.Collections.Generic;
using System.IO;

public sealed class SheetFormatException : Exception
{
    public SheetFormatException(string message) : base(message) { }
}

/// <summary>시트 한 행. 값은 string, long, double, Rgba32?, List&lt;object&gt; 중 하나다.</summary>
public sealed class SheetRow
{
    public readonly int Index;
    public readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.Ordinal);
    public SheetRow(int index) { Index = index; }

    public string Str(string f) => Values.TryGetValue(f, out var v) ? v as string : null;
    public long Long(string f) => Values.TryGetValue(f, out var v) && v is long l ? l : 0;
    public bool Bool(string f) => Long(f) != 0;
    public double Num(string f) => Values.TryGetValue(f, out var v) ? (v is double d ? d : v is long l ? l : 0) : 0;
    public Rgba32? Color(string f) => Values.TryGetValue(f, out var v) && v is Rgba32 c ? c : (Rgba32?)null;

    public List<string> Strs(string f)
    {
        var outp = new List<string>();
        if (Values.TryGetValue(f, out var v) && v is List<object> l)
            foreach (var x in l) if (x is string s) outp.Add(s);
        return outp;
    }
}

/// <summary>시트의 Color32. 시트 문자열 표기는 #RRGGBBAA.</summary>
public readonly struct Rgba32
{
    public readonly byte R, G, B, A;
    public Rgba32(byte r, byte g, byte b, byte a) { R = r; G = g; B = b; A = a; }
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}

public sealed class SheetSet
{
    public const string Folder = "Sheetdata_Client";
    public const string DefaultSet = "client";

    sealed class SheetInfo
    {
        public string Name;
        public int Rows, PreloadSize, PreloadOffset;
        public int[] RowOffsets;
        public int End;
    }

    readonly byte[] meta, text, data;
    readonly Dictionary<string, SheetInfo> sheets = new Dictionary<string, SheetInfo>(StringComparer.Ordinal);
    readonly Dictionary<int, string> textCache = new Dictionary<int, string>();
    readonly SymbolTables symbols;

    public static string MetaPath(string set) => $"{Folder}/{set}.sheetmeta.bin";
    public static string TextPath(string set) => $"{Folder}/{set}.sheettext.bin";
    public static string DataPath(string set) => $"{Folder}/{set}.sheetdata.bin";

    public SheetSet(InstallFiles files, SymbolTables symbols, string set = DefaultSet)
    {
        this.symbols = symbols;
        meta = files.ReadResource(MetaPath(set));
        text = files.ReadResource(TextPath(set));
        data = files.ReadResource(DataPath(set));

        var r = new SheetDecoder(meta, 0);
        int count = (int)r.U();
        var list = new List<SheetInfo>(count);
        var rowBlock = new List<int>(count);
        int blk = 0;
        for (int i = 0; i < count; i++)
        {
            var s = new SheetInfo { Name = Text((int)r.U()) };
            r.U();                                   // RowIdType
            s.Rows = (int)r.U();
            s.PreloadSize = (int)r.U();
            rowBlock.Add(blk);
            blk += (int)r.U();
            list.Add(s);
        }
        for (int i = 0; i < count; i++)
        {
            var s = list[i];
            int off = rowBlock[i];
            s.RowOffsets = new int[s.Rows];
            for (int k = 0; k < s.Rows; k++) { s.RowOffsets[k] = off; off += (int)r.U(); }
            s.End = off;
        }
        foreach (var s in list) { s.PreloadOffset = r.Pos; r.Pos += s.PreloadSize; }
        if (r.Pos != meta.Length || blk != data.Length)
            throw new SheetFormatException("sheetmeta 길이가 기록된 오프셋과 맞지 않습니다.");
        foreach (var s in list) sheets[s.Name] = s;
    }

    public int SheetCount => sheets.Count;
    public bool Has(string name) => sheets.ContainsKey(name);

    string Text(int off)
    {
        if (textCache.TryGetValue(off, out var s)) return s;
        return textCache[off] = LzText.At(text, off);
    }

    /// <summary>스키마로 시트 전체 행을 읽는다.</summary>
    public List<SheetRow> Read(SheetSchema schema)
    {
        if (!sheets.TryGetValue(schema.Sheet, out var s)) throw new SheetFormatException($"시트를 찾을 수 없습니다: {schema.Sheet}");
        var rows = new List<SheetRow>(s.Rows);
        var pr = new SheetDecoder(meta, s.PreloadOffset);
        for (int i = 0; i < s.Rows; i++)
        {
            int off = s.RowOffsets[i];
            int end = i + 1 < s.Rows ? s.RowOffsets[i + 1] : s.End;
            var row = new SheetRow(i);
            try
            {
                Run(pr, schema.Preload, row.Values);
                var r = new SheetDecoder(data, off);
                Run(r, schema.Row, row.Values);
                if (r.Pos != end) throw new SheetFormatException($"{schema.Sheet} {i}행 크기가 맞지 않습니다 ({r.Pos - off} / {end - off}).");
            }
            catch (IndexOutOfRangeException) { throw new SheetFormatException($"{schema.Sheet} {i}행을 읽다가 범위를 벗어났습니다."); }
            catch (ArgumentOutOfRangeException) { throw new SheetFormatException($"{schema.Sheet} {i}행을 읽다가 범위를 벗어났습니다."); }
            rows.Add(row);
        }
        if (pr.Pos != s.PreloadOffset + s.PreloadSize) throw new SheetFormatException($"{schema.Sheet} preload 크기가 맞지 않습니다.");
        return rows;
    }

    void Run(SheetDecoder r, SheetField[] fields, Dictionary<string, object> outp)
    {
        foreach (var f in fields)
        {
            if (f.IsArray)
            {
                int n = (int)r.U();
                var l = new List<object>(n);
                for (int k = 0; k < n; k++) l.Add(One(r, f));
                Put(outp, f.Name, l);
            }
            else Put(outp, f.Name, One(r, f));
        }
    }

    static void Put(Dictionary<string, object> outp, string name, object v)
    {
        if (name == "_") return;
        outp[name] = v;
    }

    object One(SheetDecoder r, SheetField f)
    {
        switch (f.Op)
        {
            case 'I': return r.I();
            case 'U': return (long)r.U();
            case 'S': case 'E': return Text((int)r.U());
            case 'K': return symbols.Name(f.Symbol, r.K());
            case 'N': return r.N();
            case 'C': return r.Color();
            case 'B': return (long)r.Byte();
            default: throw new SheetFormatException("알 수 없는 시트 연산: " + f.Op);
        }
    }
}

/// <summary>MM.DataSheet BinaryDecoder 의 가변 길이 인코딩.</summary>
sealed class SheetDecoder
{
    readonly byte[] b;
    public int Pos;
    public SheetDecoder(byte[] buf, int pos) { b = buf; Pos = pos; }

    public ulong U()
    {
        ulong r = 0; int sh = 0;
        while (true)
        {
            byte c = b[Pos++];
            r |= (ulong)(c & 0x7F) << sh;
            sh += 7;
            if (c < 0x80) return r;
        }
    }

    public long I()
    {
        byte c = b[Pos++];
        bool neg = (c & 0x40) != 0;
        long r = c & 0x3F; int sh = 6;
        while ((c & 0x80) != 0)
        {
            c = b[Pos++];
            r |= (long)(c & 0x7F) << sh;
            sh += 7;
        }
        return neg ? ~r : r;
    }

    public double N()
    {
        byte c = b[Pos++];
        if (c == 0) return 0;
        int k = c & 7, e = c >> 3;
        long m = 0;
        for (int i = 0; i < k; i++) m |= (long)b[Pos + i] << (8 * i);
        if (k > 0 && k < 8 && (b[Pos + k - 1] & 0x80) != 0) m -= 1L << (8 * k);   // 부호 확장
        Pos += k;
        return e == 0 ? m : m * Math.Pow(10, -e);
    }

    public uint K() { uint v = BitConverter.ToUInt32(b, Pos); Pos += 4; return v; }
    public byte Byte() => b[Pos++];

    public Rgba32? Color()
    {
        byte f = Byte();
        if ((f & 0x80) == 0) return null;
        byte r, g, bl;
        if ((f & 0x20) == 0)        // 4비트 단축형
        {
            r = (byte)((f & 0xF) * 0x11);
            if ((f & 0x10) != 0) g = bl = r;
            else { byte x = Byte(); g = (byte)((x >> 4) * 0x11); bl = (byte)((x & 0xF) * 0x11); }
        }
        else
        {
            int mode = f & 0x1C;
            if (mode == 0) r = g = bl = Byte();
            else if (mode == 0x10) { r = Byte(); g = Byte(); bl = Byte(); }
            else
            {
                byte p = Byte(), q = Byte();
                r = p;
                if (mode == 4) { g = p; bl = q; }
                else if (mode == 8) { g = q; bl = p; }
                else if (mode == 0xC) { g = q; bl = q; }
                else throw new SheetFormatException("알 수 없는 색 인코딩");
            }
        }
        byte a = (f & 0x40) != 0 ? Byte() : (byte)255;
        return new Rgba32(r, g, bl, a);
    }
}

/// <summary>심볼 ID(이름의 u32 해시) → 이름. 필요한 타입의 표만 읽는다.</summary>
public sealed class SymbolTables
{
    public const string Folder = "SymbolTables";
    public static string StringsPath => Folder + "/Strings.cst";
    public static string TablePath(string stem) => $"{Folder}/{stem}_SymbolTable.sjon.bin";

    readonly InstallFiles files;
    readonly byte[] cst;
    readonly Dictionary<string, Dictionary<uint, string>> tables = new Dictionary<string, Dictionary<uint, string>>(StringComparer.Ordinal);

    public SymbolTables(InstallFiles files)
    {
        this.files = files;
        cst = files.ReadResource(StringsPath);
    }

    Dictionary<uint, string> Table(string stem)
    {
        if (tables.TryGetValue(stem, out var t)) return t;
        t = new Dictionary<uint, string>();
        string path = TablePath(stem);
        if (files.HasResource(path))
        {
            foreach (var row in Sjon.Decode(files.ReadResource(path)).Arr())
            {
                var l = row.Arr();
                if (l.Count < 2 || !(l[0] is double id) || !(l[1] is double off)) continue;
                t[unchecked((uint)(long)id)] = LzText.At(cst, (int)off + 4);
            }
        }
        tables[stem] = t;
        return t;
    }

    /// <summary>0 은 빈 참조(null). 표에 없는 값은 "#해시" 로 돌려준다.</summary>
    public string Name(string stem, uint id)
    {
        if (id == 0) return null;
        if (stem != null && Table(stem).TryGetValue(id, out var s)) return s;
        return "#" + id.ToString("X8");
    }
}
