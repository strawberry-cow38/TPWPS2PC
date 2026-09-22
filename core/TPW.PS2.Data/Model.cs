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
    /// <summary>The park's terrain grid, authored in the terrain file.
    ///
    /// ⭐⭐ THE PER-CELL HEIGHTS ARE ON THE DISC. The runtime field is a verbatim memcpy of this
    /// block -- tinyclaw read the filler at 0x1f3418 and it computes nothing: it takes model+0x44,
    /// allocates NX*NZ*2+0x30, copies the 0x30 header, then memcpys NX*NZ*2 cells. Nothing is
    /// rasterised from the mesh. Present in all 8 terrain files, and every grid matches the size
    /// independently predicted from the `heightfield` marker's AABB -- two unrelated routes, an
    /// AABB on a zero-geometry mesh and a u32 pair nothing else references, agreeing 8 of 8.
    ///
    /// ⭐ THE HEIGHT IS `byte0 &amp; 0x03`, proven by the engine rather than by us: the accessor at
    /// 0x166100 takes this same struct (`lw $t3, 0x44($a0)`) and its read-modify-write does
    /// `andi $v0, $v0, 0xc3` -- 11000011, keeping 0x80/0x40/0x02/0x01 and CLEARING bits 2-5. So
    /// `0x3C` is a field the engine wipes and repaints, which is why `&amp; 0x3F` gave wild per-world
    /// histograms; jungle's 0x3C bits happen to be clear, which is why jungle alone looked tidy.
    /// Indexing is `(z * NX + x) * 2`, row-major, with the bounds checks reading +0x0c and +0x10.
    /// (tinyclaw, from the disassembly.)
    ///
    /// Measured over all eight parks: `byte0 &amp; 0x03` is {0,1,2} in seven and {0,1,2,3} in
    /// FANTASY t1 (a single cell). `0x3C` is 0 across both jungle files and heavily used elsewhere.
    /// `0x40` is set on 32-280 cells per park. ⭐ `0x80` is NEVER set in any park on this disc.
    ///
    /// ⚠ HOW TALL A STEP IS, IS STILL A GUESS. I justified one world unit per step against the
    /// marker AABB's `Y 0..2` -- that reasoning was wrong, because those floats are bit-identical
    /// in all eight files and carry no information, and because heights reach 3. One unit is what
    /// the geometry looks like, not what anything states.
    ///
    /// ⚠ `byte1` is WRITTEN from a table, not read as one: `lw $v1, 0x2c($t3)` indexes an array
    /// with `lbu $v0, 0x28($t3)` bounding the argument. On disc +0x28 is 2 in all eight files and
    /// +0x2c points at a 2-byte array ending exactly at EOF -- a good check that the pointer is
    /// read right, but it does not say what byte1 MEANS. Still unidentified.</summary>
    public sealed class HeightField
    {
        public int Width, Height;
        /// <summary>NX*NZ pairs, row-major: [0] is the height-and-flags byte, [1] is unidentified.</summary>
        public byte[] Cells;
        public int Count => Width * Height;
        /// <summary>A cell's height: `byte0 &amp; 0x03`, the mask the engine itself preserves.</summary>
        public int HeightAt(int x, int y) => Cells[(y * Width + x) * 2] & 0x03;
        public byte Raw(int x, int y) => Cells[(y * Width + x) * 2];
        public byte Second(int x, int y) => Cells[(y * Width + x) * 2 + 1];
    }

    /// <summary>The terrain grid, or null for a model that carries none (only the 8 terrain files
    /// and LOBBY's base.mps do).</summary>
    public HeightField Field { get; }

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

        // ⭐ The terrain grid hangs off header +0x44. Guarded: most models carry none, and the
        // field must be inside the file with a sane grid before it is believed.
        int fp = (int)U32(0x44);
        if (fp > 0 && fp + 0x30 <= D.Length)
        {
            int nx = (int)U32(fp + 0x0c), nz = (int)U32(fp + 0x10);
            if (nx > 0 && nz > 0 && nx <= 512 && nz <= 512 && fp + 0x30 + nx * nz * 2 <= D.Length)
            {
                var cells = new byte[nx * nz * 2];
                Array.Copy(D, fp + 0x30, cells, 0, cells.Length);
                Field = new HeightField { Width = nx, Height = nz, Cells = cells };
            }
        }
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
    /// stops looking valid -- that heuristic under-ran on 13 meshes.
    ///
    /// ⚠⚠ THE FOURTH WORD IS <c>u16 vertexCount, u16 ceil(vertexCount/3)</c>, NOT A PLAIN u32.
    /// 563 batch records on the disc carry a non-zero high half and the ratio holds on every one.
    /// This used to carry an "a &lt; b &lt; c and n &lt;= 4096" sanity filter, which read those as
    /// counts near a million, rejected the FIRST batch and broke out -- so the whole mesh came back
    /// with NO GEOMETRY and read as missing artwork. 198 of 3,934 meshes on the disc were empty
    /// because of it, including the go-karts' TRACK: 692 faces declared, 0 produced.
    ///
    /// With the low half taken as the count, every mesh on the disc now matches the face count the
    /// file itself declares: 3,934 / 3,934 across 496 models, all 16 WADs, all five file versions.
    /// ⚠ 483 further records, all in the older versions 0x13b/0x13c, have a high half that is not
    /// ceil/3 and are unexplained -- reported, not filtered.</summary>
    public List<Batch> Batches(Mesh m)
    {
        var outList = new List<Batch>();
        for (int j = 0; j < m.BatchCount; j++)
        {
            int r = (int)m.BatchTable + j * 16;
            if (r + 16 > D.Length) break;
            int a = (int)U32(r), b = (int)U32(r + 4), c = (int)U32(r + 8);
            int n = (int)(U32(r + 12) & 0xFFFF);
            if (n == 0 || a >= D.Length || b >= D.Length || c >= D.Length) break;
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
    public Dictionary<int, Matrix4x4> WorldTransforms(
        IReadOnlyDictionary<int, Matrix4x4> localOverrides = null)
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
        if (localOverrides != null)
            foreach (var kv in localOverrides) if (local.ContainsKey(kv.Key)) local[kv.Key] = kv.Value;
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

    /// <summary>Every node's own parent-relative matrix, meshes and helpers alike, keyed by file
    /// offset -- what a caller overrides to animate one and have its children follow.</summary>
    public Dictionary<int, Matrix4x4> LocalTransforms()
    {
        var local = new Dictionary<int, Matrix4x4>();
        void Add(int o)
        {
            var f = new float[16];
            for (int k = 0; k < 16; k++) f[k] = F32(o + 0x10 + k * 4);
            local[o] = new Matrix4x4(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7],
                                     f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]);
        }
        foreach (var m in Meshes) Add(m.Offset);
        for (int o = HelperTable; o + 0x60 <= D.Length; o += 0x60)
        {
            if ((U32(o) & 0x80000000) == 0) break;
            Add(o);
        }
        return local;
    }

    /// <summary>A node's index from its file offset -- the inverse of <see cref="NodeOffset"/>.
    /// Returns -1 for an offset that is in neither table.</summary>
    public int NodeIndex(int offset)
    {
        if (offset >= MeshTable && offset < MeshTable + Meshes.Count * 160 &&
            (offset - MeshTable) % 160 == 0)
            return (offset - MeshTable) / 160;
        if (offset >= HelperTable && (offset - HelperTable) % 0x60 == 0)
            return Meshes.Count + (offset - HelperTable) / 0x60;
        return -1;
    }

    /// <summary>A node's chain of indices from itself up to the root.
    ///
    /// ⚠ Needed because VISIBILITY IS PER NODE AND INHERITED. 183 of the disc's 2,033 appear /
    /// disappear entries are keyed on a HELPER, not a mesh -- hiding `Dummy01` is how the game
    /// hides the arms hanging off it. Checking only the mesh's own entry leaves those parts on
    /// screen, which is the owner's "some aren't hiding parts properly".</summary>
    public List<int> Ancestry(int node)
    {
        var chain = new List<int>();
        int o = NodeOffset(node);
        for (int guard = 0; guard < 64 && o > 0 && o + 8 <= D.Length; guard++)
        {
            int idx = NodeIndex(o);
            if (idx < 0) break;
            chain.Add(idx);
            int parent = (int)U32(o + 4);
            if (parent == 0 || parent == o) break;
            o = parent;
        }
        return chain;
    }

    public int NodeOffset(int node) =>
        node < Meshes.Count ? MeshTable + node * 160 : HelperTable + (node - Meshes.Count) * 0x60;
}
