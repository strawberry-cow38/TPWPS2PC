using System.Buffers.Binary;
using System.Numerics;

namespace TPW.PS2.Data;

/// <summary>Which of the four fence textures a hoarding wears, by its handle k (`model+0x3c + 8k`,
/// written into the primitive group by `0x1f5638`). The files are DATA.WAD
/// `/Generic/MiscMesh/textures/{Closed,Hoarding,Condemn,Upgrade}.ssh`, the list the static ctor
/// `0x1f5d20` builds under `data\generic\MiscMesh\textures\` (`0x36a918`).
/// ⚠ That handle k is list entry k is INFERRED (findings/ride-hoarding.md §6): the names agree with
/// `0x118568`'s kinds, the loader that fills `model+0x3c` is not traced.</summary>
public enum HoardingTexture : byte { Closed = 0, Hoarding = 1, Condemn = 2, Upgrade = 3 }

/// <summary>⭐ A ride's `Info.Hoarding` block as the console parses it: `0x112fa0` against the
/// 16-character table `0x2ac268`, every character a 4-bit EDGE MASK (0x01 +z, 0x04 +x, 0x10 -z,
/// 0x40 -x). Not a sign and not a picture: the fence outline, drawn in characters.
///
/// The parser, READ (findings/ride-hoarding.md §3): spaces are skipped, anything else not in the
/// table fails the whole map ("Illegal map character"), more than 20 cells in a row fails ("Map too
/// wide"), more than 20 rows fails ("Map too high"); the width is the longest row; and AFTER parsing
/// the rows are REVERSED, so the text's top line is z = h-1. Text column = x.
/// ⚠ A failed parse builds no fence -- the console's `.sam` read fails with it (`0x114048`), which
/// this port does not reproduce for the rest of the definition.</summary>
public sealed class HoardingGrid
{
    /// <summary>`0x112fa0`'s 20x20 cell array (row stride 0xa0, 8 bytes a cell).</summary>
    public const int MaxSide = 20;

    /// <summary>The table `0x2ac268`, `{u32 char, u32 value, u32 mask}`; the value word is 0 for every
    /// entry and only the mask reaches `0x1f3dd0` (cell+4).</summary>
    public static readonly IReadOnlyDictionary<char, byte> Characters = new Dictionary<char, byte>
    {
        ['.'] = 0x00, ['^'] = 0x01, ['_'] = 0x10, ['['] = 0x40, [']'] = 0x04, ['J'] = 0x14,
        ['F'] = 0x41, ['7'] = 0x05, ['L'] = 0x50, ['='] = 0x11, ['H'] = 0x44, ['C'] = 0x51,
        ['U'] = 0x54, ['n'] = 0x45, ['3'] = 0x15, ['O'] = 0x55,
    };

    public const byte PlusZ = 0x01, PlusX = 0x04, MinusZ = 0x10, MinusX = 0x40;

    public int Width { get; }
    public int Height { get; }
    readonly byte[,] _mask;

    HoardingGrid(byte[,] mask, int w, int h) { _mask = mask; Width = w; Height = h; }

    /// <summary>The edge mask of cell (x, z) in the grid frame (+z = up the text). 0 outside a short
    /// row, as the zeroed grid leaves it.</summary>
    public byte Mask(int x, int z) => _mask[x, z];

    /// <summary>Set bits over the grid: one quad each.</summary>
    public int EdgeCount
    {
        get
        {
            int n = 0;
            for (int z = 0; z < Height; z++)
                for (int x = 0; x < Width; x++)
                    n += BitOperations.PopCount((uint)(_mask[x, z] & 0x55));
            return n;
        }
    }

    /// <summary>`0x112fa0` over the block's rows, or null with the console's own message.</summary>
    public static HoardingGrid Parse(IReadOnlyList<string> rows, out string fault)
    {
        fault = null;
        if (rows == null || rows.Count == 0) { fault = "Missing map description"; return null; }
        if (rows.Count > MaxSide) { fault = "Map too high"; return null; }
        var text = new List<List<byte>>();
        int w = 0;
        foreach (var row in rows)
        {
            var cells = new List<byte>();
            foreach (char c in row)
            {
                if (c == ' ') continue;
                if (!Characters.TryGetValue(c, out byte mask)) { fault = $"Illegal map character '{c}'"; return null; }
                if (cells.Count == MaxSide) { fault = "Map too wide"; return null; }
                cells.Add(mask);
            }
            w = Math.Max(w, cells.Count);
            text.Add(cells);
        }
        int h = text.Count;
        var grid = new byte[Math.Max(w, 1), h];
        for (int t = 0; t < h; t++)
            for (int x = 0; x < text[t].Count; x++)
                grid[x, h - 1 - t] = text[t][x];                   // the rows reversed: text top = z h-1
        return new HoardingGrid(grid, w, h);
    }
}

/// <summary>One fence panel: an upright quad, `0x1f3dd0`'s 4-vertex slot <see cref="Slot"/>.
/// <see cref="Top0"/> is v0 (above v2, u = 0) and <see cref="Top1"/> is v1 (above v3, u = 1), as xz in
/// the ride's model space (1 unit = 1 cell, footprint corner at the origin). The normal is written
/// as `s8 = n x 127` (`0x1696a0`).</summary>
public readonly record struct HoardingPanel(int Slot, int CellX, int CellZ, byte Side, Vector2 Top0, Vector2 Top1,
                                            Vector3 Normal, float Key)
{
    public float Length => Vector2.Distance(Top0, Top1);
}

/// <summary>⭐ THE FENCE'S GEOMETRY, `0x1f3dd0`, READ (MIPS and decompile; findings/ride-hoarding.md §4).
/// One quad per set edge bit, visited row-major (z, then x) and bits 1, 4, 0x10, 0x40; each quad's
/// slot is its rank under the angle sort `0x1f3c70`; its ends are the cell's corners pulled by the
/// corner offsets of `0x1f39f8` (or `0x1f39b8` for a 1x1 footprint).
///
/// ⚠ Model-agnostic on purpose: the root's vertices come in as xz (<see cref="RootVertices"/> reads
/// them off a <see cref="Model"/> for a caller that has one).</summary>
public sealed class HoardingGeometry
{
    public HoardingGrid Grid { get; }
    /// <summary>By slot: panel i is the i-th to rise.</summary>
    public IReadOnlyList<HoardingPanel> Panels { get; }
    /// <summary>Corners that found no distinct second vertex and got offset 0 (instrumentation).</summary>
    public int CornersWithoutSecond { get; }

    HoardingGeometry(HoardingGrid grid, IReadOnlyList<HoardingPanel> panels, int noSecond)
    { Grid = grid; Panels = panels; CornersWithoutSecond = noSecond; }

    /// <summary>`0x1f3dd0`'s build. <paramref name="rootXZ"/> is the root node's vertices after its
    /// local matrix, as xz; ignored for a 1x1 footprint, which takes the fixed table.</summary>
    public static HoardingGeometry Build(HoardingGrid grid, IReadOnlyList<Vector2> rootXZ)
    {
        int w = grid.Width, h = grid.Height;
        float halfW = w * 0.5f, halfH = h * 0.5f;
        bool oneByOne = w == 1 && h == 1;                                  // 0x1f3dd0: uStack_148
        // Pass 1, the sort (0x1f3c70 per edge, then the stable merge sort 0x1a6370).
        var order = new List<(float Key, int X, int Z, int Side)>();
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                byte b = grid.Mask(x, z);
                for (int side = 0; side < 4; side++)
                    if ((b & SideBit[side]) != 0) order.Add((SortKey(halfW, halfH, x, z, side), x, z, side));
            }
        var ranked = order.Select((e, i) => (e, i)).OrderBy(t => t.e.Key).ThenBy(t => t.i).ToList();   // stable
        var slot = new Dictionary<(int, int, int), int>();
        for (int r = 0; r < ranked.Count; r++) slot[(ranked[r].e.X, ranked[r].e.Z, ranked[r].e.Side)] = r;

        // Pass 2, the quads (0x1f3dd0's second cell walk).
        var panels = new HoardingPanel[order.Count];
        int noSecond = 0;
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                byte b = grid.Mask(x, z);
                if (b == 0) continue;
                var o = oneByOne ? OneByOneOffsets : CornerOffsets(x, z, rootXZ, ref noSecond);
                float X0 = x, X1 = x + 1f, Z0 = z, Z1 = z + 1f;
                bool px = (b & HoardingGrid.PlusX) != 0, mx = (b & HoardingGrid.MinusX) != 0;
                bool pz = (b & HoardingGrid.PlusZ) != 0, mz = (b & HoardingGrid.MinusZ) != 0;
                // The perpendicular offset always; the along-edge one only where the adjacent edge is
                // set too (a corner). o0..o3 belong to c0 (x,z+1), c1 (x+1,z+1), c2 (x+1,z), c3 (x,z).
                if (pz) Add(0, new(X1 + (px ? o[1].X : 0f), Z1 + o[1].Y), new(X0 + (mx ? o[0].X : 0f), Z1 + o[0].Y), new(0, 0, 1));
                if (px) Add(1, new(X1 + o[2].X, Z0 + (mz ? o[2].Y : 0f)), new(X1 + o[1].X, Z1 + (pz ? o[1].Y : 0f)), new(1, 0, 0));
                if (mz) Add(2, new(X0 + (mx ? o[3].X : 0f), Z0 + o[3].Y), new(X1 + (px ? o[2].X : 0f), Z0 + o[2].Y), new(0, 0, -1));
                if (mx) Add(3, new(X0 + o[0].X, Z1 + (pz ? o[0].Y : 0f)), new(X0 + o[3].X, Z0 + (mz ? o[3].Y : 0f)), new(-1, 0, 0));

                void Add(int side, Vector2 top0, Vector2 top1, Vector3 normal)
                {
                    int s = slot[(x, z, side)];
                    panels[s] = new HoardingPanel(s, x, z, SideBit[side], top0, top1, normal, ranked[s].e.Key);
                }
            }
        return new HoardingGeometry(grid, panels, noSecond);
    }

    static readonly byte[] SideBit = { HoardingGrid.PlusZ, HoardingGrid.PlusX, HoardingGrid.MinusZ, HoardingGrid.MinusX };

    /// <summary>`0x1f39b8`, READ in MIPS (`0x1f39d4..0x1f39f4`): +0.3 into slots 0, 0x10, 0x14, 0x18, 0x1c
    /// and -0.3 into 4, 8, 0xc. ⚠⚠ So o2 is (+0.3, +0.3), NOT the (-0.3, +0.3) the research notes give
    /// (hoarding.md §4, "0.3 inward on both axes"): corner c2 = (x+1, z) is pushed OUT in x, and the
    /// +x panel of a 1x1 runs diagonally from (1.3, 0) to (0.7, 0.7). Kept as the executable has it.
    /// Only the four 1x1 Small Toilets (`n`) use it, and a feature never reaches the raise, so no
    /// console player has ever seen it.</summary>
    public static readonly Vector2[] OneByOneOffsets =
    {
        new(0.3f, -0.3f), new(-0.3f, -0.3f), new(0.3f, 0.3f), new(0.3f, 0.3f),
    };

    /// <summary>`0x1f39f8`, READ (decompile checked against MIPS): for each corner, the vertex whose xz
    /// distance is the SECOND smallest among those more than 0.001 (the doubles at `0x36a940`/`0x36a948`,
    /// both 0.001f widened) from the nearest; `o = 0.5 (v - c)` (`fVar14 = 0.5`), or 0 when there is none.
    /// The running pair starts at FLT_MAX (`0x36a938`). ⚠ The distances are the host's float `sqrt`, not
    /// the EE's; nothing on disc sits at a 0.001 boundary for that to move.</summary>
    public static Vector2[] CornerOffsets(int x, int z, IReadOnlyList<Vector2> rootXZ)
    {
        int ignored = 0;
        return CornerOffsets(x, z, rootXZ, ref ignored);
    }

    static Vector2[] CornerOffsets(int x, int z, IReadOnlyList<Vector2> rootXZ, ref int noSecond)
    {
        var corners = new Vector2[] { new(x, z + 1f), new(x + 1f, z + 1f), new(x + 1f, z), new(x, z) };
        var o = new Vector2[4];
        for (int k = 0; k < 4; k++)
        {
            var c = corners[k];
            float best = float.MaxValue, second = float.MaxValue;
            int bestAt = -1, secondAt = -1;
            for (int j = 0; j < (rootXZ?.Count ?? 0); j++)
            {
                float dx = rootXZ[j].X - c.X, dz = rootXZ[j].Y - c.Y;
                float d = MathF.Sqrt(dx * dx + dz * dz);
                if (d < best)
                {
                    // A new nearest pushes the old one down to second, unless it is within 0.001 of it.
                    if (Math.Abs((double)(d - best)) > Threshold) { second = best; secondAt = bestAt; }
                    best = d; bestAt = j;
                }
                else if (d < second && Math.Abs((double)(d - best)) > Threshold) { second = d; secondAt = j; }
            }
            if (secondAt < 0) { o[k] = Vector2.Zero; noSecond++; }
            else o[k] = new Vector2((rootXZ[secondAt].X - c.X) * 0.5f, (rootXZ[secondAt].Y - c.Y) * 0.5f);
        }
        return o;
    }

    /// <summary>`0x36a940`/`0x36a948`: the double 0.0010000000474974513, i.e. 0.001f widened.</summary>
    const double Threshold = 0.001f;

    // ---------------------------------------------------------------------------------------------
    // The sort key.

    /// <summary>⭐ `0x1f3c70`, READ (MIPS `0x1f3c70..0x1f3dc4`): the edge's midpoint against the block's
    /// centre, `fmod(atan2f(mz - h/2, mx - w/2) + pi + pi/4, 2 pi)`. Key 0 is the direction (-x, +z), so
    /// the panels run from the text's top-left corner down the left side, along the bottom, up the right
    /// and back along the top: counter-clockwise with +z drawn up.
    ///
    /// ⚠⚠ IN THE EE's ARITHMETIC, NOT THE HOST's. Newlib's `atan2f` (`0x28cef8` → `0x28f198`, `atanf`
    /// `0x292968`, identified by its tables at `0x37ce78`) and the two adds after it are float ops on
    /// the EE's FPU, which rounds TOWARD ZERO (⚠ INFERRED: the EE's documented FPU mode, as PS2
    /// emulators run it; not measured on this disc). It decides exactly one thing: a midpoint on the
    /// up-left DIAGONAL (e.g. the top-left panel of every 4x3 block, Big Dripper's among them). There
    /// `atan2f(1.5, -1.5)` = 0x4016CBE4 either way, but `+ pi + pi/4` truncates to 0x40C90FDA, one ulp
    /// under 2 pi, so its key is 6.2831850 and it rises LAST; rounded to nearest it is exactly 2 pi,
    /// key 0, and it would rise FIRST. The `fmod` (`0x28cc90`) and the narrowing (`0x296918`) are soft
    /// float and exact here.</summary>
    public static float SortKey(float halfW, float halfH, int x, int z, int side)
    {
        float mx, mz;
        switch (side)
        {
            case 0: mx = Ee.Add(x, 0.5f); mz = Ee.Add(z, 1f); break;       // +z
            case 1: mx = Ee.Add(x, 1f); mz = Ee.Add(z, 0.5f); break;        // +x
            case 2: mx = Ee.Add(x, 0.5f); mz = z; break;                    // -z
            default: mx = x; mz = Ee.Add(z, 0.5f); break;                   // -x
        }
        float a = Ee.Atan2f(Ee.Sub(mz, halfH), Ee.Sub(mx, halfW));
        a = Ee.Add(a, BitConverter.Int32BitsToSingle(0x40490fdb));        // 3.1415927
        a = Ee.Add(a, BitConverter.Int32BitsToSingle(0x3f490fdb));        // 0.7853982
        return (float)((double)a % 6.2831854820251465);                     // 0x36a950
    }

    /// <summary>The same key in the host's round-to-nearest `MathF`, for the audit's control only.</summary>
    public static float HostSortKey(float halfW, float halfH, int x, int z, int side)
    {
        var (mx, mz) = side switch { 0 => (x + 0.5f, z + 1f), 1 => (x + 1f, z + 0.5f), 2 => (x + 0.5f, (float)z), _ => ((float)x, z + 0.5f) };
        float a = MathF.Atan2(mz - halfH, mx - halfW) + 3.14159274f + 0.785398185f;
        return (float)((double)a % 6.2831854820251465);
    }

    // ---------------------------------------------------------------------------------------------
    // The root's vertices.

    /// <summary>`0x1f3dd0`'s gate and its vertex read, off a model: the root node is header `+0x70`
    /// (a file offset into the mesh table); the build runs only if it exists, its node flags (`+0x00`)
    /// have 0x40 clear and it has at most 512 vertices (`u16 +0x60 &lt;= 0x200`). The vertices are the
    /// first `+0x60` of its concatenated batch positions (`0x1697b0`), through its local matrix at
    /// `+0x10` as row vectors -- `x' = x m11 + y m21 + z m31 + m41` (`0x1f3fd8..`), which is
    /// <see cref="Vector3.Transform(Vector3, Matrix4x4)"/>. Returns false, and no fence, when the gate
    /// fails; ⚠ a legacy MD2 model has no such header and is refused.</summary>
    public static bool RootVertices(Model model, out List<Vector2> xz, out string reason)
    {
        xz = null; reason = null;
        if (model == null) { reason = "no model"; return false; }
        if (model.IsLegacyMd2) { reason = "legacy MD2 model: no M3D2 root at header +0x70"; return false; }
        uint rootAt = BinaryPrimitives.ReadUInt32LittleEndian(model.D.AsSpan(0x70));
        var root = model.Meshes.FirstOrDefault(m => m.Offset == rootAt);
        if (root == null) { reason = $"header +0x70 = 0x{rootAt:X} is not a mesh node"; return false; }
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(model.D.AsSpan(root.Offset));
        if ((flags & 0x40) != 0) { reason = $"root '{root.Name}' has node flag 0x40"; return false; }
        if (root.VertexCount > 0x200) { reason = $"root '{root.Name}' has {root.VertexCount} vertices (> 512)"; return false; }
        var (pos, _, _) = model.Vertices(root);
        xz = new List<Vector2>(root.VertexCount);
        for (int i = 0; i < root.VertexCount && i < pos.Count; i++)
        {
            var v = Vector3.Transform(pos[i], root.Local);
            xz.Add(new Vector2(v.X, v.Z));
        }
        reason = $"root '{root.Name}', {xz.Count} vertices";
        return true;
    }

    /// <summary>⚠ The EE's single-precision FPU as far as the sort key needs it: every add, subtract,
    /// multiply and divide ROUNDED TOWARD ZERO (see <see cref="SortKey"/>), and newlib's `atan2f`/`atanf`
    /// op for op as the executable has them (`0x28f198`, `0x292968`; constants read from `0x37ce78`).
    /// Non-finite inputs never arise (grid coordinates) and fall back to the host.</summary>
    internal static class Ee
    {
        static float Chop(double value, double error)
        {
            // value + error is the exact result, |error| far below value's float ulp.
            float f = (float)value;
            double over = ((double)f - value) - error;                      // f - exact
            if (value > 0 && over > 0) f = MathF.BitDecrement(f);
            else if (value < 0 && over < 0) f = MathF.BitIncrement(f);
            return f;
        }

        public static float Add(float a, float b)
        {
            double s = (double)a + b, bv = s - a;
            return Chop(s, ((double)a - (s - bv)) + ((double)b - bv));    // two-sum: a + b = s + err exactly
        }

        public static float Sub(float a, float b) => Add(a, -b);
        public static float Mul(float a, float b) => Chop((double)a * b, 0);  // 24 x 24 bits: exact in a double

        public static float Div(float a, float b)
        {
            float f = (float)((double)a / b);
            // |f| must not exceed |a / b|: compare f b (exact in a double) with a.
            if (Math.Abs((double)f * b) > Math.Abs((double)a))
                f = f > 0 ? MathF.BitDecrement(f) : MathF.BitIncrement(f);
            return f;
        }

        static float F(int bits) => BitConverter.Int32BitsToSingle(bits);
        internal static readonly float[] AtanHi = { F(0x3eed6338), F(0x3f490fda), F(0x3f7b985e), F(0x3fc90fda) };
        internal static readonly float[] AtanLo = { F(0x31ac3769), F(0x33222168), F(0x33140fb4), F(0x33a22168) };
        internal static readonly float[] AT =
        {
            F(0x3eaaaaab), F(unchecked((int)0xbe4ccccd)), F(0x3e124925), F(unchecked((int)0xbde38e38)),
            F(0x3dba2e6e), F(unchecked((int)0xbd9d8795)), F(0x3d886b35), F(unchecked((int)0xbd6ef16b)),
            F(0x3d4bda59), F(unchecked((int)0xbd15a221)), F(0x3c8569d7),
        };
        static readonly float Pi = F(0x40490fda), PiLo = F(0x34222168);

        /// <summary>`0x28f198` (newlib `__ieee754_atan2f`) for finite arguments.</summary>
        public static float Atan2f(float y, float x)
        {
            if (!float.IsFinite(x) || !float.IsFinite(y)) return MathF.Atan2(y, x);
            int hx = BitConverter.SingleToInt32Bits(x), ix = hx & 0x7fffffff;
            int hy = BitConverter.SingleToInt32Bits(y), iy = hy & 0x7fffffff;
            if (hx == 0x3f800000) return Atanf(y);                                     // x = 1.0
            int m = ((hy >> 31) & 1) | ((hx >> 30) & 2);                                // 2 sign(x) + sign(y)
            if (iy == 0) return m switch { 0 or 1 => y, 2 => Pi, _ => -Pi };             // 0x28f214..
            if (ix == 0) return hy < 0 ? F(unchecked((int)0xbfc90fdb)) : F(0x3fc90fdb);
            int k = (iy - ix) >> 23;
            float z = k > 60 ? F(0x3fc90fdc)                                            // pi/2 + pi_lo/2
                    : hx < 0 && k < -60 ? 0f
                    : Atanf(MathF.Abs(Div(y, x)));
            return m switch
            {
                0 => z,
                1 => -z,
                2 => Sub(Pi, Sub(z, PiLo)),
                _ => Sub(Sub(z, PiLo), Pi),
            };
        }

        /// <summary>`0x292968` (newlib `atanf`) for finite arguments.</summary>
        static float Atanf(float x)
        {
            int hx = BitConverter.SingleToInt32Bits(x), ix = hx & 0x7fffffff;
            if (ix >= 0x50800000) return hx > 0 ? Add(AtanHi[3], AtanLo[3]) : Sub(-AtanHi[3], AtanLo[3]);
            int id;
            if (ix < 0x3ee00000)
            {
                if (ix < 0x31000000 && Add(F(0x7149f2ca), x) > 1f) return x;          // |x| < 2^-29
                id = -1;
            }
            else
            {
                x = MathF.Abs(x);
                if (ix < 0x3f980000)
                {
                    if (ix < 0x3f300000) { id = 0; x = Div(Sub(Add(x, x), 1f), Add(x, 2f)); }
                    else { id = 1; x = Div(Sub(x, 1f), Add(x, 1f)); }
                }
                else if (ix < 0x401c0000) { id = 2; x = Div(Sub(x, 1.5f), Add(Mul(x, 1.5f), 1f)); }
                else { id = 3; x = Div(-1f, x); }
            }
            float zz = Mul(x, x), w = Mul(zz, zz);
            float s1 = Mul(zz, Add(AT[0], Mul(w, Add(AT[2], Mul(w, Add(AT[4], Mul(w, Add(AT[6], Mul(w, Add(AT[8], Mul(w, AT[10]))))))))))),
                  s2 = Mul(w, Add(AT[1], Mul(w, Add(AT[3], Mul(w, Add(AT[5], Mul(w, Add(AT[7], Mul(w, AT[9])))))))));
            if (id < 0) return Sub(x, Mul(x, Add(s1, s2)));
            float r = Sub(AtanHi[id], Sub(Sub(Mul(x, Add(s1, s2)), AtanLo[id]), x));
            return hx < 0 ? -r : r;
        }
    }
}

/// <summary>⭐⭐ A RIDE'S HOARDING -- the construction fence round a broken, condemned or upgrading ride
/// (TPHoarding.cpp, `0x1f3c70..0x1f5f28`; findings/ride-hoarding.md). Natively it lives on the ride's
/// model INSTANCE (`TPInstance` `+0x04` flags, `+0x24` progress, `+0x28` rate), not on the ride
/// object, and nothing of it is saved; this class is that state over one <see cref="HoardingGeometry"/>.
///
/// Driven by the ride service (<see cref="ParkSim.HoardingRaise"/>/<see cref="ParkSim.HoardingLower"/>:
/// `0x118568` → `0x1fa690` → <see cref="Raise"/>, `0x118678` → `0x1fa700` → <see cref="Lower"/>) and
/// ticked with PAUSABLE WALL-CLOCK SECONDS (<see cref="Tick"/>): +0.2 progress a second up (5.0 s),
/// -0.3 down (3.33 s).</summary>
public sealed class RideHoarding
{
    /// <summary>`inst+4`: 2 built, 0x20 visible, 0x40 raising, 0x80 lowering, and the current kind
    /// 0x100 Closed / 0x200 Hoarding / 0x400 Condemn / 0x800 Upgrade.</summary>
    public const int Built = 0x2, Visible = 0x20, Raising = 0x40, Lowering = 0x80, KindMask = 0xf00;
    /// <summary>`0x1f5948`: `rate = 0.2f` (0x3E4CCCCD). `0x1f5ab0`: `rate = -0.3f` (0xBE99999A).</summary>
    public const float RiseRate = 0.2f, DropRate = -0.3f;

    public HoardingGeometry Geometry { get; }
    public int Flags { get; private set; } = Built;
    /// <summary>`inst+0x24`, 0..1. 0 at creation (the template image is zeroed by `0x1f7ed8`); NEVER reset
    /// by a raise, so a fence caught half-way down rises again from where it was.</summary>
    public float Progress { get; private set; }
    /// <summary>`inst+0x28`, the signed rate per second.</summary>
    public float Rate { get; private set; }
    /// <summary>The texture in the primitive group, as `0x1f5638` last wrote it.</summary>
    public HoardingTexture Texture { get; private set; } = HoardingTexture.Closed;

    public bool Shown => (Flags & Visible) != 0;
    public bool Rising => (Flags & Raising) != 0;
    public bool Dropping => (Flags & Lowering) != 0;

    public RideHoarding(HoardingGeometry geometry) => Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));

    /// <summary>`0x1f5948(inst, bits)`, READ (MIPS `0x1f5948..0x1f5aa8`), `bits` exactly one of 1/2/4/8:
    /// <code>
    ///   1 Closed    only if lowering or no kind yet   (no caller passes 1)
    ///   2 Hoarding  only if the kind is neither Hoarding nor Condemn (&amp;0x600 == 0)
    ///   4 Condemn   unless already Condemn   -- so a broken, condemned ride keeps Condemn
    ///   8 Upgrade   unless already Upgrade   -- so Upgrade replaces Condemn
    ///   then rate = +0.2; if not visible, 0x1f56b0(inst, 1) (draw-enabled); flags = flags | 0x60 &amp; ~0x80
    /// </code></summary>
    public void Raise(int bits)
    {
        switch (bits)
        {
            case 1 when (Flags & Lowering) != 0 || (Flags & KindMask) == 0: SetKind(HoardingTexture.Closed); break;
            case 2 when (Flags & 0x600) == 0: SetKind(HoardingTexture.Hoarding); break;
            case 4 when (Flags & 0x400) == 0: SetKind(HoardingTexture.Condemn); break;
            case 8 when (Flags & 0x800) == 0: SetKind(HoardingTexture.Upgrade); break;
        }
        Rate = RiseRate;
        Flags = (Flags | Visible | Raising) & ~Lowering;                 // 0x1f56b0(inst,1) adds no flag of its own
    }

    /// <summary>`0x1f5ab0(inst)`: rate -0.3; only a visible fence starts lowering (`| 0x80 &amp; ~0x40`).</summary>
    public void Lower()
    {
        Rate = DropRate;
        if ((Flags & Visible) != 0) Flags = (Flags | Lowering) & ~Raising;
    }

    /// <summary>`0x1f2e70`'s gate (visible) and `0x1f5c10(dt, inst)`, READ (MIPS `0x1f5c10..0x1f5d18`):
    /// only while raising or lowering, `p = clamp(p + rate dt, 0, 1)`; if p moved, reshape. If it did
    /// NOT move: lowering at 0 clears 0xa0 and hides (`0x1f56b0(inst, 0)`: texture Closed, kind 0x100);
    /// raising at 1 clears 0x40. So the hide and the stop each land one tick after the end is reached.
    /// Returns true when anything drawn changed (the shape, or the fence hidden).
    /// ⚠ `p + rate dt` in the host's rounding; the EE truncates, which can move the end by a frame.</summary>
    public bool Tick(float dtSeconds)
    {
        if ((Flags & Visible) == 0 || (Flags & (Raising | Lowering)) == 0) return false;
        float old = Progress;
        float p = old + Rate * dtSeconds;
        if (p < 0f) p = 0f;
        if (p > 1f) p = 1f;
        Progress = p;
        if (p != old) return true;
        if ((Flags & Lowering) != 0)
        {
            if (p <= 0f)
            {
                Flags &= ~(Visible | Lowering);
                SetKind(HoardingTexture.Closed);                                     // 0x1f56b0(inst, 0)
                return true;
            }
        }
        else if (p >= 1f) Flags &= ~Raising;
        return false;
    }

    void SetKind(HoardingTexture t)
    {
        Texture = t;                                                                 // 0x1f5638
        Flags = (Flags & ~KindMask) | (0x100 << (int)t);
    }

    /// <summary>`0x1f5750`, READ (MIPS `0x1f57b0..0x1f5840`), panel <paramref name="slot"/> of
    /// <paramref name="count"/> at <paramref name="progress"/>:
    /// `f = clamp(1 - (i/N x 0.8 - (p - 0.25)) x 4, 0, 1) x (i odd ? 0.8 : 1.0)` -- i.e.
    /// `4p - 3.2 i/N` -- so panel i starts at p = 0.8 i/N and is up 0.25 later, even slots reach 1.0 cell
    /// and odd ones 0.8. ⚠ For N &gt; 16 the last slot never reaches 1 (18 blocks on disc: 0.978).</summary>
    public static float PanelHeight(int slot, int count, float progress)
    {
        float f = 1f - ((float)slot / count * 0.8f - (progress - 0.25f)) * 4f;
        if (f < 0f) f = 0f;
        if (f > 1f) f = 1f;
        return f * ((slot & 1) != 0 ? 0.8f : 1f);
    }

    /// <summary>Panel <paramref name="slot"/>'s height now. The top vertices sit at this y, the bottom at 0;
    /// the TOP UVs stay at v = 1 and the BOTTOM ones become v = 1 - f, so a panel rises out of the ground
    /// carrying its top edge and the texture is never squashed.</summary>
    public float Height(int slot) => PanelHeight(slot, Geometry.Panels.Count, Progress);
}
