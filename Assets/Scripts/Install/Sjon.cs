// SJON 바이너리 디코더. {schema, data} 중 data 트리를 돌려준다.
//   객체: Dictionary<string, object> (타입 객체는 "$type" 키 포함), 배열: List<object>, 숫자: double, 문자열, bool, null
// UnityEngine 에 의존하지 않는다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class Sjon
{
    public static object Decode(byte[] b)
    {
        if (b.Length < 16 || b[0] != (byte)'S' || b[1] != (byte)'J' || b[2] != (byte)'O' || b[3] != (byte)'N')
            throw new InvalidDataException("SJON 머리 형식이 다릅니다.");
        int nstr = BitConverter.ToInt32(b, 8), nwords = BitConverter.ToInt32(b, 12);
        int baseOff = 16 + 4 * nstr;
        var strs = new string[nstr];
        int prev = 0;
        for (int i = 0; i < nstr; i++)
        {
            int e = BitConverter.ToInt32(b, 16 + 4 * i);
            strs[i] = Encoding.UTF8.GetString(b, baseOff + prev, e - prev);
            prev = e;
        }
        int off = (baseOff + prev + 3) & ~3;
        var w = new uint[nwords];
        for (int i = 0; i < nwords; i++) w[i] = BitConverter.ToUInt32(b, off + 4 * i);
        var dec = new Decoder { S = strs, W = w };
        if ((w[0] & 7) != 5) throw new InvalidDataException("SJON 최상위가 배열이 아닙니다.");
        int top = 1 + (int)(w[0] >> 3);
        dec.ReadSchema(top + 1);
        return dec.Val(top + 2);
    }

    sealed class Decoder
    {
        public string[] S; public uint[] W;
        List<(string name, List<string> fields)> types;

        static double Num(uint v)
        {
            int div10 = (int)((v >> 3) & 7);
            int sv = unchecked((int)v) >> 1;
            long mant = sv >> 5;
            return div10 == 0 ? mant : mant / Math.Pow(10, div10);
        }

        static double Wide(ulong v)
        {
            int div10 = (int)(v & 0x1F);
            long mant = unchecked((long)v) >> 5;
            return div10 == 0 ? mant : mant / Math.Pow(10, div10);
        }

        public void ReadSchema(int pos)
        {
            // 스키마는 맵: 타입 이름 → {필드: "."}. 필드 순서가 타입 객체 값 순서다.
            types = new List<(string, List<string>)>();
            uint v = W[pos];
            int tgt = pos + 1 + (int)(v >> 3);
            int n = (int)W[tgt];
            for (int i = 0; i < n; i++)
            {
                string tname = S[W[tgt + 1 + i]];
                int fpos = tgt + 1 + n + i;
                uint fv = W[fpos];
                int ft = fpos + 1 + (int)(fv >> 3);
                int fn = (int)W[ft];
                var fields = new List<string>(fn);
                for (int k = 0; k < fn; k++) fields.Add(S[W[ft + 1 + k]]);
                types.Add((tname, fields));
            }
        }

        public object Val(int pos)
        {
            uint v = W[pos];
            uint tag = v & 7, arg = v >> 3;
            int tgt = pos + 1 + (int)arg;
            switch (tag)
            {
                case 0: return Num(v);
                case 1: return Wide(W[tgt] | ((ulong)W[tgt + 1] << 32));
                case 2: return null;
                case 3: return arg == 1;
                case 4: return S[arg];
                case 5:
                {
                    int n = (int)W[tgt];
                    var l = new List<object>(n);
                    for (int i = 0; i < n; i++) l.Add(Val(tgt + 1 + i));
                    return l;
                }
                case 6:
                {
                    var (tname, fields) = types[(int)W[tgt]];
                    var o = new Dictionary<string, object> { ["$type"] = tname };
                    for (int i = 0; i < fields.Count; i++) o[fields[i]] = Val(tgt + 1 + i);
                    return o;
                }
                default:
                {
                    int n = (int)W[tgt];
                    var o = new Dictionary<string, object>();
                    for (int i = 0; i < n; i++) o[S[W[tgt + 1 + i]]] = Val(tgt + 1 + n + i);
                    return o;
                }
            }
        }
    }
}

/// <summary>SJON 트리 탐색 도우미. 없는 키·다른 형식은 null·0·빈 목록으로 돌려준다.</summary>
public static class SjonExt
{
    public static object Get(this object o, string key) => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v : null;
    public static List<object> Arr(this object o) => o as List<object> ?? new List<object>();
    public static string Str(this object o) => o as string;
    public static float F(this object o) => o is double d ? (float)d : 0f;
    public static int I(this object o) => o is double d ? (int)d : 0;
    public static bool B(this object o) => o is bool b && b;
}
