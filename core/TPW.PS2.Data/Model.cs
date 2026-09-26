using System.Numerics;

namespace TPW.PS2.Data;

/// <summary>M3D2: PS2 `.mps` and the four legacy `.MD2` files on this disc.
/// The layouts differ. Model.Md2.cs handles legacy indexed faces; the batch APIs below are MPS only.</summary>
public sealed partial class Model
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
        /// <summary>⭐⭐ THE SECOND RUN LIST, at mesh +0x9c, and it is NOT the one at +0x98.
        /// The UV-animation channel (APS track flag 0x10000) walks this one: for `cn_stall` it
        /// resolves to 49 runs over the mesh's 68 vertices, matching that track's 49 entries
        /// exactly, while +0x98 gives 30 runs and belongs to morph/skin. It is the only mesh in
        /// the Coconut carrying a +0x9c list, and the only one with a 0x10000 track.</summary>
        public uint UvAnimList;
        public Vector3 BoundsMin, BoundsMax;
    }

    public readonly byte[] D;
    public List<Mesh> Meshes { get; } = new();
    public List<string> Materials { get; } = new();
    /// <summary>Ordered texture choices per material. MPS +0x40, 16-byte descriptors:
    /// +0x0a counts 20-byte names at +0x0c (loader 0x227610–0x227664).
    /// These are explicit lists, not numbered-filename conventions.</summary>
    public List<string[]> MaterialTextures { get; } = new();
    public int MeshTable { get; private set; }
    /// <summary>The park's terrain grid, authored in the terrain file.
    ///
    /// ⭐⭐ THE PER-CELL HEIGHTS ARE ON THE DISC. The runtime field is a verbatim memcpy of this
    /// block -- tinyclaw read the filler at 0x1f3418 and it computes nothing: it takes model+0x44,
    /// allocates NX*NZ*2+0x30, copies the 0x30 header, then memcpys NX*NZ*2 cells. Nothing is
    /// rasterised from the mesh. Present in all 8 terrain files, and every grid matches the size
    /// independently predicted from the `heightfield` marker's AABB -- two unrelated routes, an
    /// AABB on a zero-geometry mesh and a u32 pair nothing else references, agreeing 8 of 8.
    ///
    /// ⚠⚠ THE CELL IS NOT A HEIGHT. It is THREE TABLE INDICES. The builder at 0x222230 reads the
    /// cell as a u16 and splits it three ways, each scaled by 4 and used as an offset into a
    /// different float array (`lwc1`): bits 0-3, bits 4-7, and bits 8-15. The shape logic at
    /// 0x2233b0 skips a cell when `andi 0x3c` is zero, and `andi 0x20` SWAPS TWO BYTES on the
    /// stack -- it is selecting and ROTATING a tile shape. A cell names a tile whose CORNER
    /// heights are looked up, so the ground is sloped and stepped tiles, never a cuboid per cell.
    ///
    /// ⚠ The three float arrays are STACK buffers built earlier in the same function (0x221e3c,
    /// sp+0x60 / sp+0x30 / sp+0x10), not static tables -- assembled at draw time, so they cannot
    /// be dumped off the disc.
    ///
    /// ⚠ `0x40` is NOT a flag. It is bit 6: the bits4-7 nibble holding the value 4. I reported it
    /// as an unexplained flag for hours because I was masking a field that does not exist.
    ///
    /// ⚠ I previously documented `byte0 &amp; 0x03` here as "the height, proven by the engine". It
    /// was neither. `andi 0xc3` in the accessor proves only which bits THAT function preserves,
    /// and I read a meaning into it; the histograms I used as support were me inferring semantics
    /// from distributions. Master called the rendered result wrong on sight and was right.
    ///
    /// ⚠ And jungle cannot referee any of this: bits 4-7 are {0,4} there against fantasy's
    /// {0,2,3,4,6}, and its 0x3C is zero throughout. That is the FOURTH field where the world we
    /// test on is the one that cannot tell a wrong answer from a right one.
    /// (Decoding by tinyclaw, from the code.)</summary>
    public sealed class HeightField
    {
        public int Width, Height;

        /// <summary>The header float at struct +0x18, 2.0 in all eight terrain files.
        ///
        /// ⭐ Almost certainly the step height. tinyclaw dismissed it this morning BECAUSE it is
        /// constant -- "identical everywhere, so it states nothing per park" -- and that was the
        /// wrong test: a step height SHOULD be the same in every park, so being constant is what it
        /// ought to look like. Master then said 1 unit renders too short.
        ///
        /// ⚠ NOT PROVEN. No code has been found reading it as a height, and "right sort of number
        /// in the right sort of place" is the reasoning behind several of today's retractions. It
        /// is read from the file rather than hardcoded so that if it is wrong, it is wrong in a way
        /// the data can correct.</summary>
        public float Step;
        /// <summary>NX*NZ pairs, row-major: [0] is the height-and-flags byte, [1] is unidentified.</summary>
        public byte[] Cells;
        public int Count => Width * Height;
        /// <summary>A cell's first byte, raw. ⚠ NOT a height, and NOT decoded.
        ///
        /// What is established: bit 0 is the skip flag (see <see cref="Drawn"/>); bits 2-5
        /// (`0x3C`) are wiped and repainted by the accessor at 0x166100, so that function owns
        /// them; bit 7 (`0x80`) is never set in any of the eight parks on disc.
        ///
        /// ⚠ Two readings have been proposed and BOTH withdrawn. "`&amp; 0x03` is the height, proven
        /// by `andi 0xc3`" was mine -- that instruction only shows which bits one function
        /// preserves, never what they mean. "Three index fields at bits 0-3 / 4-7 / 8-15" came
        /// from 0x221e3c, which was then shown never to touch this structure at all. Nothing in
        /// any function confirmed to reach the cells turns part of this byte into a Y. Leave it
        /// undecoded rather than name it.</summary>
        public byte Raw0(int x, int y) => Cells[(y * Width + x) * 2];

        /// <summary>The material index for a cell: `byte1`, into the terrain model's own
        /// <see cref="Model.Materials"/>. Confirmed two ways -- a 6-of-7 hit on each world's own
        /// ground-tile set at per-world indices, and the lookup in 0x222fe8, which does
        /// `lbu byte1` then `sll 3` into an 8-byte-record table at `[$s3+0x5c]`.
        /// ⚠ Index 0 is a SENTINEL, not a material.</summary>
        public int Material(int x, int y) => Cells[(y * Width + x) * 2 + 1];

        /// <summary>⭐ Does the engine draw this cell? `byte0` bit 0 SET means SKIP. Tested
        /// identically at both cell sites in 0x222fe8 as `(byte0 ^ 1) &amp; 1`, and consistent with
        /// the `andi 0xfe` / `ori 0x81` in the other routine.
        ///
        /// This is the authored footprint, and it replaces a mask I derived from mesh coverage --
        /// which dropped about 290 cells the game does draw. It is spatially coherent (solid
        /// regions, with the volcano and the plot boundary punched out) and lands at 68-82% drawn
        /// across all eight parks.</summary>
        public bool Drawn(int x, int y) => (Cells[(y * Width + x) * 2] & 1) == 0;

        /// <summary>⭐⭐ Can anything be built here? IT IS THE SAME BIT AS <see cref="Drawn"/>,
        /// and this is read out of the loader rather than guessed.
        ///
        /// The PS2 builds its 8-byte runtime tile map from THIS grid at park load — there is no
        /// map resource anywhere on the disc; I checked every non-asset file in both the world
        /// archive and DATA.WAD. The fill loop is at `0x14E700`: it walks the authored cells two
        /// bytes at a time and writes each tile, and the two branches that matter are
        ///
        ///     andi $v1, $t0, 0x40   → if set, tile[+1] (HEIGHT) = $s1
        ///     andi $v0, $t0, 0x01   → if set, tile[+0] = $t6 and tile[+7] (FLAGS) = $t5
        ///
        /// with the constants loaded just above the loop: `$s1 = 2`, `$t6 = 1`, and
        /// **`$t5 = 35 = 0x23`** — bits 0, 1 and 5. Bit 1 of that flags byte is the one
        /// `0x18E278` tests to refuse a placement, and the PSX port documents independently as
        /// "nothing may be built here".
        ///
        /// Every other cell gets flags 0. So a cell is unbuildable **exactly when** its authored
        /// `byte0` bit 0 is set, which is the same bit that says the terrain draws no ground there.
        /// One bit does both jobs, which is why looking for a separate no-build bit found nothing:
        /// bit 1 of the AUTHORED byte was never it — bit 1 of the RUNTIME flags is, and bit 0 of
        /// the authored byte is what puts it there.
        ///
        /// ⭐ A free check fell out of the same loop: `byte0 & 0x40` sets the tile's height byte to
        /// 2, and the grid header's own step float at `+0x18` is 2.0. The raised-tile reading and
        /// the step height reach the same number by different routes.</summary>
        public bool Buildable(int x, int y) => Drawn(x, y);
        public byte Raw(int x, int y) => Cells[(y * Width + x) * 2];
        public byte Second(int x, int y) => Cells[(y * Width + x) * 2 + 1];
    }

    /// <summary>The terrain grid, or null for a model that carries none (only the 8 terrain files
    /// and LOBBY's base.mps do).</summary>
    public HeightField Field { get; }

    public int HelperTable { get; private set; }

    uint U32(int o) => BitConverter.ToUInt32(D, o);
    ushort U16(int o) => BitConverter.ToUInt16(D, o);
    float F32(int o) => BitConverter.ToSingle(D, o);

    string NameAt(int o)
    {
        if (o <= 0 || o >= D.Length) return null;
        int e = o; while (e < D.Length && D[e] != 0) e++;
        return System.Text.Encoding.Latin1.GetString(D, o, e - o);
    }

    public Model(byte[] data, Mtr companion = null)
    {
        D = data ?? throw new ArgumentNullException(nameof(data));
        if (D.Length < 4) throw new InvalidDataException("truncated model");
        if (U32(0) == LegacyMd2Magic)
        {
            IsLegacyMd2 = true;
            ReadMd2();
            companion?.ValidateAgainst(this);
            Companion = companion;
            return;
        }
        if (companion != null) throw new InvalidDataException("MTR cannot be attached to an MPS model");
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
                float step = F32(fp + 0x18);
                Field = new HeightField
                {
                    Width = nx, Height = nz, Cells = cells,
                    Step = step > 0f && step < 64f ? step : 1f,
                };
            }
        }
        int nmat = U16(0x22), matTable = (int)U32(0x40);
        for (int i = 0; i < nmat; i++)
        {
            int descriptor = checked(matTable + i * 16);
            int count = U16(descriptor + 10), names = checked((int)U32(descriptor + 12));
            if (count == 0 || names <= 0 || (long)names + count * 20L > D.Length)
                throw new InvalidDataException($"MPS material {i}: invalid texture name table");
            var textures = new string[count];
            for (int j = 0; j < count; j++)
            {
                int start = names + j * 20;
                int end = Array.IndexOf(D, (byte)0, start, 20);
                if (end <= start) throw new InvalidDataException($"MPS material {i}, texture {j}: invalid name");
                textures[j] = System.Text.Encoding.Latin1.GetString(D, start, end - start);
            }
            MaterialTextures.Add(textures);
            Materials.Add(textures[0]);
        }

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
                UvAnimList = U32(o + 0x9c),
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
        if (IsLegacyMd2) throw new InvalidOperationException("legacy MD2 has indexed faces, not MPS batches");
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
        if (IsLegacyMd2) throw new InvalidOperationException("legacy MD2 has face groups, not MPS batch groups");
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

    /// <summary>⭐ The mesh's real triangles, honouring the TWO flags the VU1 microcode reads off
    /// every vertex.
    ///
    /// A batch is a triangle strip. Vertex k carries, in the low bit of two of its float words:
    /// <list type="bullet">
    /// <item>X bit 0 -- ADC: the triangle (k-2, k-1, k) is not drawn. That restarts a strip inside
    /// a batch and kills a fan's degenerate triangles.</item>
    /// <item>Y bit 0 -- FACING: 1 means the triangle (k-2, k-1, k) faces its own right-hand normal,
    /// so that order is the outward (counter-clockwise-front) order; 0 means it faces the other
    /// way, so the outward order is (k-2, k, k-1).</item>
    /// </list>
    ///
    /// ⚠⚠ THERE IS NO STRIP PARITY. This used to swap every odd triangle (<c>if (k &amp; 1)</c>),
    /// which is what a strip means on hardware with a winding convention. The GS has none, and the
    /// game culls in its VU1 microprogram instead (<c>.vutext</c>, the strip loops at L00ab-L00c4
    /// and L00e8-L0106): it takes the screen-space orientation of (k-2, k-1, k) from
    /// OPMULA/OPMSUB, reads the sign flag of its Z, and forces ADC on when that sign differs from
    /// Y bit 0. The flag is per-triangle and authoritative; the parity guess agreed with it only by
    /// chance, and pointed half of jungle's ground DOWN. See findings/formats.md.
    ///
    /// Validated on the data: with this rule the right-hand normal of every triangle of every
    /// ground mesh in jungle's terrain_1 points up (2,618 of 2,619), and it agrees with the stored
    /// vertex normals on 60,084 of 60,537 triangles across all 112 jungle models (99.25%). The
    /// disagreements are where smoothed vertex normals are unreliable; the flag is what the game
    /// culls with, not the normal.
    ///
    /// Each flag costs one ulp on a float, which is why <c>FUN_001a6d68</c> writes X AND Y as
    /// <c>value &amp; 0xfffffffe | old &amp; 1</c>.
    ///
    /// ⭐ Validated against <c>mesh+0x62</c>, the format's own face count: reading each batch as one
    /// plain strip matches for 8.1% of meshes; honouring ADC matches for **935 / 935 = 100.00%**.</summary>
    public List<Triangle> Triangles(Mesh m)
    {
        if (IsLegacyMd2) return _md2Triangles[m.Index];
        var batchMat = new Dictionary<int, int>();
        foreach (var (mat, first, n) in Groups(m))
            for (int j = first; j < first + n; j++) batchMat[j] = mat;

        var tris = new List<Triangle>();
        int baseV = 0, bi = 0;
        foreach (var b in Batches(m))
        {
            var adc = new bool[b.Count];
            var faces = new bool[b.Count];
            for (int k = 0; k < b.Count; k++)
            {
                adc[k] = (U32(b.PosOffset + k * 12) & 1) != 0;          // X bit 0
                faces[k] = (U32(b.PosOffset + k * 12 + 4) & 1) != 0;    // Y bit 0
            }
            for (int k = 2; k < b.Count; k++)
            {
                if (adc[k]) continue;
                int i0 = k - 2, i1 = k - 1, i2 = k;
                if (!faces[k]) (i1, i2) = (i2, i1);   // faces away from its right-hand normal
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
        if (IsLegacyMd2) return _md2Vertices[m.Index];
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
    public int[] AnimVertexMap(Mesh m) => RunMap(m.AnimVertexList, m.VertexCount);

    /// <summary>The same run decoding against the UV channel's list at mesh +0x9c. ⭐ One vertex
    /// maps to one ENTRY of the 0x10000 track; a run covers every vertex sharing a UV value, which
    /// is why a 68-vertex mesh needs only 49 entries.</summary>
    public int[] UvVertexMap(Mesh m) => RunMap(m.UvAnimList, m.VertexCount);

    /// <summary>Decode a run list: per vertex, which GROUP it belongs to. A slot's bit 1 means
    /// "the next slot continues this group", exactly as the consumer at 0x1ad378 walks it.</summary>
    int[] RunMap(uint list, int nv)
    {
        if (list == 0) return null;
        var ent = new ushort[nv];
        for (int k = 0; k < nv; k++) ent[k] = U16((int)list + k * 2);
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
        foreach (int o in HelperOffsets())
        {
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
        foreach (int o in HelperOffsets())
        {
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
        int stride = IsLegacyMd2 ? 0x58 : 0x60;
        if (offset >= HelperTable && (offset - HelperTable) % stride == 0 &&
            (!IsLegacyMd2 || (offset - HelperTable) / stride < _md2NodeCount - Meshes.Count))
            return Meshes.Count + (offset - HelperTable) / stride;
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

    /// <summary>One of the model's named fittings: a place a script can point at.</summary>
    /// <param name="Flags">Which KINDS this fitting answers to. A script's node reference carries
    /// a "space" (`0x80`, `0x100`, `0x200`, `0x800`) and only a fitting sharing a bit with it is
    /// a match.</param>
    /// <param name="Id">The number the script uses.</param>
    /// <param name="Node">The model node it IS -- the nth fitting is the nth helper node.</param>
    public readonly record struct Fitting(uint Flags, int Id, int Node, float X, float Y, float Z)
    {
        /// <summary>⭐⭐ THE SURFACE RECORD, present exactly when <see cref="Flags"/> has `0x40`.
        /// A fitting with one is not at a point in space at all -- it is `(u, v, h)` on ONE
        /// TRIANGLE of its PARENT mesh, so that it can ride the face as the mesh morphs:
        ///
        /// <code>P = (tri[b0]*(1-u) + tri[b1]*u)*(1-v) + tri[b2]*v + h*N</code>
        ///
        /// with the three vertices taken through the PARENT's world matrix first, and `N` the
        /// triangle normal whose winding is the M3D2 FACING bit (the LSB of vertex 2's y). Read
        /// from `FUN_001f1248` at `0x1f138c..0x1f16e0`; see `findings/particles.md` and the
        /// memory note `reference_tpw_ps2_fitting_surface_uvh`.
        ///
        /// ⚠ `X`/`Y`/`Z` above are NOT this. They are the older reading of the same pointer and
        /// are only meaningful through <see cref="FittingLocal"/>, which cannot fire.</summary>
        public Surface? OnSurface { get; init; }
    }

    /// <summary>Where a `0x40` fitting sits on its parent's skin. `Corner0/1/2` select which of
    /// the three loaded vertices each term uses -- they are NOT always 0,1,2.</summary>
    public readonly record struct Surface(int Batch, int FirstVertex, float U, float V, float H,
                                          int Corner0, int Corner1, int Corner2);

    List<Fitting> _fittings;

    /// <summary>⭐⭐ WHAT A SCRIPT MEANS BY A "NODE". `EVENT`, `ADDOBJ`, `WALKON`, `ADDHEAD` and
    /// `SPARK` all name one, and none of them means an index into the node table. `0x1b9388` hands
    /// the number AND the space to `0x1f1f78`, which SEARCHES this table -- at `+0x74`, `u16`
    /// count at `+0x36`, twenty bytes an entry -- for the first entry whose id matches and whose
    /// flags share a bit with the space, and returns THAT ENTRY'S INDEX. No match and the
    /// instruction does nothing at all.
    ///
    /// ⭐⭐ AND THE INDEX IS THE HELPER NODE. `monkey.mps` has 24 fittings and exactly 24 helper
    /// nodes before its last dummy -- `Head1`..`Head17`, `nose1`, `nose03`..`nose07`, `destroy` --
    /// and they line up one for one in order. Every one of the sixteen `0x80` fittings lands on a
    /// `Head` node, and EVERY `Head` node is parented to `m_arm` or `m_arm1`: the arms holding the
    /// bananas. Which is where this ride's riders sit.
    ///
    /// ⚠⚠ I FIRST READ THE `u16` AT THE RECORD'S FIRST POINTER `+2` AS THE NODE, AND IT IS NOT.
    /// It gives `m_body`, `m_crate` and `m_arm` -- plausible parts of the right ride, and twelve
    /// of sixteen riders end up on the ape's belly and on a crate that its own visibility
    /// timeline has already hidden. The correction came from master, who has played the game:
    /// "they should all be on the bananas". The hierarchy then says so too. A reading can satisfy
    /// four self-consistency checks and still be the wrong sixteen points; what it could not
    /// survive was somebody who knew what the ride looks like.
    ///
    /// ⚠ The three floats are a position normalised in the node's own bounds -- see
    /// <see cref="FittingLocal"/>. What builds the runtime array `0x1f2978` indexes is still
    /// unread; the ordering above is a correspondence that is exact and matches the game, not a
    /// constructor that was walked.</summary>
    public IReadOnlyList<Fitting> Fittings
    {
        get
        {
            if (_fittings != null) return _fittings;
            _fittings = new List<Fitting>();
            int count = U16(0x36), table = (int)U32(0x74);
            if (count <= 0 || table <= 0 || table + count * 20 > D.Length) return _fittings;
            for (int i = 0; i < count; i++)
            {
                int o = table + i * 20;
                uint flags = U32(o);
                int id = (int)U32(o + 4), p = (int)U32(o + 12);
                float x = 0, y = 0, z = 0;
                if (p > 0 && p + 16 <= D.Length) { x = F32(p + 4); y = F32(p + 8); z = F32(p + 12); }
                // ⭐ The same pointer, read as what it actually is. Layout from the consumer:
                // u16 batch, u16 firstVertex, f32 u, f32 v, f32 h, f32 (unread), u8 b0 b1 b2.
                Surface? surf = null;
                if ((flags & 0x40) != 0 && p > 0 && p + 0x13 <= D.Length)
                    surf = new Surface(U16(p), U16(p + 2), F32(p + 4), F32(p + 8), F32(p + 0xc),
                                       D[p + 0x10], D[p + 0x11], D[p + 0x12]);
                // ⚠ The nth fitting is the nth HELPER, not whatever the record's own u16 says.
                _fittings.Add(new Fitting(flags, id, Meshes.Count + i, x, y, z) { OnSurface = surf });
            }
            return _fittings;
        }
    }

    /// <summary>Where a fitting sits in its node's own space.
    ///
    /// ⭐ The three floats are a position NORMALISED inside the node's bounds (`+0x70` min,
    /// `+0x80` max). Used raw they are a hundredth of a unit apart and every rider on a part
    /// piles onto one spot -- which is how the frame was found, from a renderer reporting three
    /// kids on one point.
    ///
    /// ⚠ A HELPER HAS NO BOUNDS, so a fitting on one is at its node's origin and the floats are
    /// carried unused. That covers every `Head` and `nose` on `monkey.mps`, which is all of them:
    /// the helper IS the seat, and it is already in the right place.</summary>
    public System.Numerics.Vector3 FittingLocal(Fitting f)
    {
        if (f.Node < 0 || f.Node >= Meshes.Count) return System.Numerics.Vector3.Zero;
        var m = Meshes[f.Node];
        var lo = m.BoundsMin; var hi = m.BoundsMax;
        return new System.Numerics.Vector3(lo.X + f.X * (hi.X - lo.X),
                                           lo.Y + f.Y * (hi.Y - lo.Y),
                                           lo.Z + f.Z * (hi.Z - lo.Z));
    }

    /// <summary>How many head slots a ride has, counted the way the loader counts them.
    ///
    /// ⚠⚠ NOT "how many `0x80` fittings there are". `0x1bfdf8` walks UP from id 1 and stops at
    /// the first id with no `0x80` fitting, so the answer is the longest run 1, 2, 3, ... that
    /// the model actually has -- a gap truncates it. The two agree on every jungle ride, because
    /// their seat ids happen to run 1..N with no hole, which is exactly the situation in which a
    /// simpler rule looks right and is not.</summary>
    public int HeadSlotCount
    {
        get
        {
            int n = 0;
            while (FindFitting(n + 1, 0x80) != null) n++;
            return n;
        }
    }

    /// <summary>The fitting a script means, or null. ⚠ The mask falls back exactly as `0x1f1f78`
    /// does: a space sharing no bit with `0x3da1f83` is replaced by `0x3da1f82`.</summary>
    public Fitting? FindFitting(int id, uint space)
    {
        uint mask = (space & 0x3da1f83) != 0 ? space : 0x3da1f82;
        foreach (var f in Fittings) if (f.Id == id && (f.Flags & mask) != 0) return f;
        return null;
    }

    /// <summary>A node's name, mesh or helper. ⭐ A HELPER CARRIES ITS NAME POINTER AT +0x54 LIKE A
    /// MESH DOES -- checked on `monkey.mps`, whose 25 helper records read `Head1, Head02, Head06,
    /// Head03, Head07, Head04, Head08, Head05, Head17, nose05, Head09, Head10, Head14, Head15,
    /// Head12, Head16, Head13, Head11, nose06, nose1, nose07, nose03, nose04, destroy, Dummy01`
    /// through that pointer, in table order. ⚠ Checked on that one PS2 model; the legacy .MD2
    /// helper record is 0x58 bytes and unchecked, so it answers "" there rather than a guess.</summary>
    public string NodeName(int node)
    {
        if (node < 0) return "";
        if (node < Meshes.Count) return Meshes[node].Name ?? "";
        if (IsLegacyMd2) return "";
        int o = NodeOffset(node);
        if (o + 0x58 > D.Length) return "";
        int p = (int)U32(o + 0x54);
        return p > 0 && p < D.Length ? NameAt(p) : "";
    }

    /// <summary>A node's parent node, or -1 at a root. The parent is the record's `+4`, the
    /// offset <see cref="WorldTransforms"/> already walks for every mesh and helper.</summary>
    public int NodeParent(int node)
    {
        if (node < 0) return -1;
        int o = NodeOffset(node);
        if (o + 8 > D.Length) return -1;
        int parent = (int)U32(o + 4);
        return parent <= 0 ? -1 : NodeIndex(parent);
    }

    public int NodeOffset(int node) =>
        node < Meshes.Count ? MeshTable + node * 160 : HelperTable + (node - Meshes.Count) * (IsLegacyMd2 ? 0x58 : 0x60);

    IEnumerable<int> HelperOffsets()
    {
        if (IsLegacyMd2)
        {
            for (int i = 0; i < _md2NodeCount - Meshes.Count; i++) yield return HelperTable + i * 0x58;
            yield break;
        }
        for (int o = HelperTable; o + 0x60 <= D.Length && (U32(o) & 0x80000000) != 0; o += 0x60)
            yield return o;
    }
}
