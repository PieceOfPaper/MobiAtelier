// .SkinnedModularMesh.bytes → UnityEngine.Mesh
//   이름, 플래그, 블렌드셰이프 이름, 정점(위치·노멀·탄젠트·UV0~3(w,x,y,z 순)·본 이름 가중치·색·블렌드), 삼각형(서브메시 번호 포함),
//   바인드포즈(본 이름 + 열 우선 16 float)
//   블렌드셰이프 값은 목표 위치·노멀의 절대값이라 기본값을 빼서 델타로 넣는다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

public static class SmmReader
{
    const int FNormal = 1, FTangent = 2, FUv0 = 4, FUv1 = 8, FUv3 = 32, FColor = 64, FBlend = 128;

    /// <summary>boneNames 는 바인드포즈 순서(= 메시 본 인덱스 순서)다.</summary>
    public static Mesh Read(byte[] data, out string[] boneNames)
    {
        using (var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8))
        {
            string name = r.ReadString();
            int fl = r.ReadInt32();
            int nb = r.ReadInt32();
            var bsNames = new string[nb];
            for (int i = 0; i < nb; i++) bsNames[i] = r.ReadString();
            int nv = r.ReadInt32();
            var pos = new Vector3[nv]; var nrm = new Vector3[nv]; var uv = new Vector2[nv];
            var bw = new List<(string, float)>[nv];
            bool hasBlend = (fl & FBlend) != 0, hasNormal = (fl & FNormal) != 0;
            var bsp = hasBlend ? new Vector3[nb][] : null;
            var bsn = hasBlend && hasNormal ? new Vector3[nb][] : null;
            for (int k = 0; hasBlend && k < nb; k++) { bsp[k] = new Vector3[nv]; if (bsn != null) bsn[k] = new Vector3[nv]; }
            Vector3 V3() => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            for (int i = 0; i < nv; i++)
            {
                pos[i] = V3();
                if (hasNormal) nrm[i] = V3();
                if ((fl & FTangent) != 0) r.BaseStream.Seek(16, SeekOrigin.Current);
                if ((fl & FUv0) != 0) { r.ReadSingle(); float x = r.ReadSingle(), y = r.ReadSingle(); r.ReadSingle(); uv[i] = new Vector2(x, y); }
                for (int bit = FUv1; bit <= FUv3; bit <<= 1) if ((fl & bit) != 0) r.BaseStream.Seek(16, SeekOrigin.Current);
                int n = r.ReadInt32();
                var ws = new List<(string, float)>(n);
                for (int k = 0; k < n; k++) ws.Add((r.ReadString(), r.ReadSingle()));
                bw[i] = ws;
                if ((fl & FColor) != 0) r.BaseStream.Seek(16, SeekOrigin.Current);
                if (hasBlend)
                {
                    for (int k = 0; k < nb; k++) bsp[k][i] = V3() - pos[i];
                    if (hasNormal) for (int k = 0; k < nb; k++) bsn[k][i] = V3() - nrm[i];
                }
            }
            int nt = r.ReadInt32();
            var subs = new SortedDictionary<int, List<int>>();
            for (int i = 0; i < nt; i++)
            {
                int sm = r.ReadInt32();
                if (!subs.TryGetValue(sm, out var l)) subs[sm] = l = new List<int>();
                l.Add(r.ReadInt32()); l.Add(r.ReadInt32()); l.Add(r.ReadInt32());
            }
            int np = r.ReadInt32();
            boneNames = new string[np];
            var bp = new Matrix4x4[np];
            var bi = new Dictionary<string, int>();
            for (int i = 0; i < np; i++)
            {
                boneNames[i] = r.ReadString();
                bi[boneNames[i]] = i;
                var m = new Matrix4x4();
                for (int k = 0; k < 16; k++) m[k] = r.ReadSingle();
                bp[i] = m;
            }

            var mesh = new Mesh { name = name };
            if (nv > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = pos;
            if (hasNormal) mesh.normals = nrm;
            if ((fl & FUv0) != 0) mesh.uv = uv;
            int nsub = 0;
            foreach (var k in subs.Keys) nsub = Math.Max(nsub, k + 1);
            mesh.subMeshCount = nsub;
            foreach (var kv in subs) mesh.SetTriangles(kv.Value, kv.Key, false);
            mesh.bindposes = bp;
            var weights = new BoneWeight[nv];
            for (int i = 0; i < nv; i++)
            {
                var ws = bw[i];
                ws.Sort((a, c) => c.Item2.CompareTo(a.Item2));
                float s = 0; for (int k = 0; k < Math.Min(4, ws.Count); k++) s += ws[k].Item2;
                if (s <= 0) s = 1;
                var w = new BoneWeight();
                int Idx(string b) => bi.TryGetValue(b, out var x) ? x : 0;
                if (ws.Count > 0) { w.boneIndex0 = Idx(ws[0].Item1); w.weight0 = ws[0].Item2 / s; }
                if (ws.Count > 1) { w.boneIndex1 = Idx(ws[1].Item1); w.weight1 = ws[1].Item2 / s; }
                if (ws.Count > 2) { w.boneIndex2 = Idx(ws[2].Item1); w.weight2 = ws[2].Item2 / s; }
                if (ws.Count > 3) { w.boneIndex3 = Idx(ws[3].Item1); w.weight3 = ws[3].Item2 / s; }
                weights[i] = w;
            }
            mesh.boneWeights = weights;
            for (int k = 0; hasBlend && k < nb; k++) mesh.AddBlendShapeFrame(bsNames[k], 100f, bsp[k], bsn?[k], null);
            if (!hasNormal) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
