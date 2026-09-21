using System.Numerics;

namespace TPW.PS2.Data;

/// <summary>M3D2 (`.mps`) -- the PS2 mesh format.
///
/// The PC release's `.MD2` is the same family under another extension; whether the details below
/// hold there is unchecked.</summary>
public sealed class Model
{
    public const uint Magic = 0x183076E4;

    public sealed class Mesh
    {
        public int Index;
        public string Name;
        public int VertexCount, FaceCount, BatchCount;
        public Matrix4x4 Local;                 // ⚠ PARENT-RELATIVE
        public uint Parent;                     // ⚠ an ABSOLUTE FILE OFFSET, into either table
        public int Offset;                      // this entry's own file offset
        public uint GroupTable, BatchTable, AnimVertexList;
        public Vector3 BoundsMin, BoundsMax;
    }

    public readonly byte[] D;
    public List<Mesh> Meshes { get; } = new();
    public List<string> Materials { get; } = new();
    public int MeshTable { get; }
    public int HelperTable { get; }

    uint U32(int o) => BitConverter.ToUInt32(D, o);
    ushort U16(int o) => BitConverter.ToUInt16(D, o);
    float F32(int o) => BitConverter.ToSingle(D, o);

    string NameAt(int o)
    {
        if (o <= 0 || o >= D.Length) return null;
        int e = o; while (e < D.Length && D[e] != 0) e++;
        return System.Text.Encoding.Latin1.GetString(D, o, e - o);
    }

    public Model(byte[] data)
    {
        D = data;
        if (U32(0) != Magic) throw new InvalidDataException($"not M3D2: {U32(0):X8}");
        MeshTable = (int)U32(0x48);
        HelperTable = (int)U32(0x4C);
        int nmat = U16(0x22), matTable = (int)U32(0x40);
        for (int i = 0; i < nmat; i++) Materials.Add(NameAt((int)U32(matTable + i * 16 + 12)));

        int nmesh = U16(0x30);
        for (int i = 0; i < nmesh; i++)
        {
            int o = MeshTable + i * 160;
            var m = new Mesh
            {
                Index = i,
                Offset = o,
                Parent = U32(o + 4),
                Name = NameAt((int)U32(o + 0x54)),
                VertexCount = U16(o + 0x60),
                FaceCount = U16(o + 0x62),
                BatchCount = U16(o + 0x66),
                GroupTable = U32(o + 0x68),
                BatchTable = U32(o + 0x6C),
                AnimVertexList = U32(o + 0x98),
                BoundsMin = new Vector3(F32(o + 0x70), F32(o + 0x74), F32(o + 0x78)),
                BoundsMax = new Vector3(F32(o + 0x80), F32(o + 0x84), F32(o + 0x88)),
            };
            var f = new float[16];
            for (int k = 0; k < 16; k++) f[k] = F32(o + 0x10 + k * 4);
            m.Local = new Matrix4x4(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7],
                                    f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]);
            Meshes.Add(m);
        }
    }

    public record Batch(int PosOffset, int UvOffset, int NormalOffset, int Count);

    /// <summary>⚠ The batch COUNT comes from <c>mesh+0x66</c>, never from walking until a record
    /// stops looking valid -- that heuristic under-ran on 13 meshes.</summary>
    public List<Batch> Batches(Mesh m)
    {
        var outList = new List<Batch>();
        for (int j = 0; j < m.BatchCount; j++)
        {
            int r = (int)m.BatchTable + j * 16;
            int a = (int)U32(r), b = (int)U32(r + 4), c = (int)U32(r + 8), n = (int)U32(r + 12);
            if (!(a > 0 && a < b && b < c && c <= D.Length) || n == 0 || n > 4096) break;
            outList.Add(new Batch(a, b, c, n));
        }
        return outList;
    }

    /// <summary>Which material each run of batches uses.
    ///
    /// ⭐ The material is not stored as an index. It is encoded by **where the group's pointer lands**
    /// in an 8-byte-per-material table immediately before the material table:
    /// <c>material = (group[0] - (materialTable - 8*(materialCount+1))) / 8 - 1</c>.
    /// Verified: group batch counts sum to <c>mesh+0x66</c> on 963 meshes, 0 mismatched.</summary>
    public IEnumerable<(int Material, int FirstBatch, int Count)> Groups(Mesh m)
    {
        if (m.GroupTable == 0) yield break;
        int nmat = U16(0x22);
        int baseOff = (int)U32(0x40) - 8 * (nmat + 1);
        int first = 0;
        for (int k = 0; k < 64 && first < m.BatchCount; k++)
        {
            int o = (int)m.GroupTable + k * 32;
            int nb = D[o + 8];                                  // ⚠ a BYTE, not a u16
            if (nb == 0) yield break;
            int idx = ((int)U32(o) - baseOff) / 8 - 1;
            yield return (idx >= 0 && idx < nmat ? idx : -1, first, nb);
            first += nb;
        }
    }

    public record Triangle(int A, int B, int C, int Material);

    /// <summary>⭐ The mesh's real triangles, honouring the **ADC bit**.
    ///
    /// A batch is a triangle strip with the PS2's ADC flag living in **bit 0 of the X position
    /// word**: a triangle spanning vertices k, k+1, k+2 is drawn only when bit 0 of vertex k+2's X
    /// is CLEAR. One rule both restarts a strip inside a batch -- so no triangle bridges unrelated
    /// pieces -- and kills a fan's degenerate triangles. The flag costs one ulp on a float, which is
    /// why <c>FUN_001a6d68</c> writes X as <c>value &amp; 0xfffffffe | old &amp; 1</c>.
    ///
    /// ⭐ Validated against <c>mesh+0x62</c>, the format's own face count: reading each batch as one
    /// plain strip matches for 8.1% of meshes; this matches for **935 / 935 = 100.00%**.</summary>
    public List<Triangle> Triangles(Mesh m)
    {
        var batchMat = new Dictionary<int, int>();
        foreach (var (mat, first, n) in Groups(m))
            for (int j = first; j < first + n; j++) batchMat[j] = mat;

        var tris = new List<Triangle>();
        int baseV = 0, bi = 0;
        foreach (var b in Batches(m))
        {
            var adc = new bool[b.Count];
            for (int k = 0; k < b.Count; k++) adc[k] = (U32(b.PosOffset + k * 12) & 1) != 0;
            for (int k = 0; k < b.Count - 2; k++)
            {
                if (adc[k + 2]) continue;
                int i0 = k, i1 = k + 1, i2 = k + 2;
                if ((k & 1) != 0) (i1, i2) = (i2, i1);
                tris.Add(new Triangle(baseV + i0, baseV + i1, baseV + i2,
                                      batchMat.TryGetValue(bi, out var mm) ? mm : -1));
            }
            baseV += b.Count; bi++;
        }
        return tris;
    }

    /// <summary>Positions, UVs and normals in strip-slot order, concatenated across batches.
    /// Normals are the third per-vertex stream: 3 x int8 over 127.</summary>
    public (List<Vector3> Pos, List<Vector2> Uv, List<Vector3> Normal) Vertices(Mesh m)
    {
        List<Vector3> pos = new(); List<Vector2> uv = new(); List<Vector3> nor = new();
        foreach (var b in Batches(m))
            for (int k = 0; k < b.Count; k++)
            {
                pos.Add(new Vector3(F32(b.PosOffset + k * 12), F32(b.PosOffset + k * 12 + 4),
                                    F32(b.PosOffset + k * 12 + 8)));
                uv.Add(new Vector2(BitConverter.ToInt16(D, b.UvOffset + k * 4) / 4096f,
                                   BitConverter.ToInt16(D, b.UvOffset + k * 4 + 2) / 4096f));
                nor.Add(new Vector3((sbyte)D[b.NormalOffset + k * 3] / 127f,
                                    (sbyte)D[b.NormalOffset + k * 3 + 1] / 127f,
                                    (sbyte)D[b.NormalOffset + k * 3 + 2] / 127f));
            }
        return (pos, uv, nor);
    }

    /// <summary>Strip slot -> animated-vertex index, from the model's own list at <c>mesh+0x98</c>.
    ///
    /// <c>FUN_001a6d68</c> walks it while emitting each animated vertex:
    /// <c>do { a = *p++; write(gsPacket + (a &amp; 0xfffc)); } while (a &amp; 2);</c>
    /// so one RUN of entries is one animated vertex, and each entry's <c>a &amp; 0xfffc</c> is a GS
    /// address. Addresses step 12 bytes -- three words, one vertex -- so an address's RANK among the
    /// sorted unique addresses is its strip-vertex index. Runs matched animated-vertex counts on
    /// 7 of 7 meshes, and this field is 0 for exactly the meshes with no vertex stream.</summary>
    public int[] AnimVertexMap(Mesh m)
    {
        if (m.AnimVertexList == 0) return null;
        int nv = m.VertexCount;
        var ent = new ushort[nv];
        for (int k = 0; k < nv; k++) ent[k] = U16((int)m.AnimVertexList + k * 2);
        var rank = ent.Select(e => e & 0xfffc).Distinct().OrderBy(x => x)
                      .Select((s, i) => (s, i)).ToDictionary(t => t.s, t => t.i);
        if (rank.Count != nv) return null;
        var idx = new int[nv]; Array.Fill(idx, -1);
        int g = 0;
        foreach (var e in ent)
        {
            idx[rank[e & 0xfffc]] = g;
            if ((e & 2) == 0) g++;
        }
        return idx.Any(v => v < 0) ? null : idx;
    }

    /// <summary>World transforms for every mesh AND helper, composing the scene graph.
    /// ⚠ A parent link is an ABSOLUTE FILE OFFSET and may point into either table -- Crazy Ape's
    /// arms hang off `Dummy01`, a HELPER, not a mesh. A helper header carries flag bit 0x80000000
    /// and its entries are 96 bytes against a mesh's 160.</summary>
    public Dictionary<int, Matrix4x4> WorldTransforms()
    {
        var local = new Dictionary<int, Matrix4x4>();
        var parent = new Dictionary<int, uint>();
        void Add(int o)
        {
            var f = new float[16];
            for (int k = 0; k < 16; k++) f[k] = F32(o + 0x10 + k * 4);
            local[o] = new Matrix4x4(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7],
                                     f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]);
            parent[o] = U32(o + 4);
        }
        foreach (var m in Meshes) Add(m.Offset);
        for (int o = HelperTable; o + 0x60 <= D.Length; o += 0x60)
        {
            if ((U32(o) & 0x80000000) == 0) break;
            Add(o);
        }
        var world = new Dictionary<int, Matrix4x4>();
        Matrix4x4 Resolve(int o, int depth)
        {
            if (world.TryGetValue(o, out var w)) return w;
            uint p = parent.GetValueOrDefault(o);
            w = (p == 0 || !local.ContainsKey((int)p) || depth > 32)
                ? local[o]
                : local[o] * Resolve((int)p, depth + 1);      // column-major: parent LAST
            world[o] = w;
            return w;
        }
        foreach (var o in local.Keys.ToList()) Resolve(o, 0);
        return world;
    }

    public int NodeOffset(int node) =>
        node < Meshes.Count ? MeshTable + node * 160 : HelperTable + (node - Meshes.Count) * 0x60;
}
