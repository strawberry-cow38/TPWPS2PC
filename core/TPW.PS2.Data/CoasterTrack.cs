using System.Numerics;

namespace TPW.PS2.Data;

/// <summary>⭐ ONE ROLLER COASTER'S FIXED NUMBERS. Every per-coaster native table is indexed
/// `[world][park 0..2][ordinal 0..2]`; the fourteen rows here are those tables read at the
/// fourteen home triples (findings/coaster-survey.md §1, coaster-trains.md §1.5,
/// coaster-geometry.md §4.3 and §7). The Test Park remaps onto the same rows (`0x11f930`).
///
/// ⚠ The pylon rows are the only ones not read from the executable: `PylonBindY` and the loft
/// path come from each coaster's `stdpylon.mps`/`.aps` (the `TrackDummyCentre` helper's local y and
/// its section-3 path), and they feed `0x19a420`'s attach height through the ADDITIVE pose
/// (coaster-geometry.md §4.2–4.3). That reading fits 10 of the 14 station models' rail heights; it
/// is the one inference in this table.</summary>
public sealed record CoasterType(
    string Name, string Folder, int World, int Park, int Ordinal, char Style,
    string Tex0, string Tex1, string Tex2,
    int CarsPerTrain, float CarSpacing, int Seats, bool Loops,
    int ExitHeight, int EntryHeight,
    float PylonBindY, float LoftFrom, float LoftTo, int AttachOffset,
    string CarModel, string PylonFolder, int SoundFamily)
{
    /// <summary>World order JUNGLE, HALLOW, FANTASY, SPACE; park is the index (0 = terrain_1).</summary>
    public static readonly CoasterType[] All =
    {
        new("Temple of Gloom",     "MineCart", 0, 0, 0, 'G', "mc_rail1", "chain", "mc_struts",   4, 1.0f,  6, true,   375,  375,  5.0f,    0f,   90f,  0x60, "cart",    "MineCart", 2),
        new("Chak Atak",           "Coaster1", 0, 1, 0, 'B', "water2", "trak_sec2", "trak_sec3", 1, 1.0f,  6, false,  575,  575,  6.0f,    0f,   90f,  0x60, "croccar", "Coaster1", 1),
        new("Gorilla Thrilla",     "Coaster3", 0, 1, 1, 'C', "A_Rope", null, null,               1, 1.0f,  2, true,   795,  795, 15.558f,  0f,   80f,  0,    "ape",     "Coaster3", 2),
        new("Hades",               "c_hade",   1, 0, 0, 'B', "Slime2", "slime_up", "rib_side2",  1, 1.0f,  6, false,  420,  420,  6.0f,    0f,   90f,  0x60, "maggot",  "c_hade",   1),
        new("Dare Devil",          "devil",    1, 0, 1, 'A', "flmtrk", "chain", null,            2, 1.25f, 4, false,  265,  265,  5.060f,  0f,   90f,  0x60, "car",     "devil",    1),
        new("Scatty Batty",        "c_scat",   1, 1, 0, 'D', "hw_pillar3", null, null,           2, 1.0f,  3, true,   175,  175,  5.0f,    0f,   90f,  0x60, "bat",     "c_scat",   0),
        new("Ghosta Coasta",       "coasta",   1, 1, 1, 'A', "gt_rail1", "chain", null,          2, 1.0f,  4, true,   250,  250,  5.0f,    0f,   90f,  0x60, "cart",    "coasta",   2),
        // ⚠ Pylon registry id 435 names `coasta`, so Bone Shaker draws Ghosta Coasta's pylon.
        new("Bone Shaker",         "shake",    1, 1, 2, 'A', "Track01", "Track02", null,         3, 1.0f,  4, true,   355,  355,  5.0f,    0f,   90f,  0x60, "car",     "coasta",   2),
        new("Big Dripper",         "b_drip",   2, 0, 0, 'B', "paint_flow", "ratchet", "gutter",  2, 1.0f,  2, false,  290,  290,  9.179f,  0f,   90f,  0x60, "car",     "b_drip",   1),
        new("Caterpillar Coaster", "cat_co",   2, 0, 1, 'A', "beanpole02", "beanleaf01", null,   3, 1.0f,  4, true,   455,  455,  9.5f,  -4.5f, 85.5f, 0x60, "caterbd", "cat_co",   2),
        new("Candy Coaster",       "candy_c",  2, 1, 0, 'G', "curwir_trk", "chain", "mat_trk",   4, 1.0f,  4, true,   285,  285,  5.0f,    0f,   90f,  0x60, "car",     "candy_c",  0),
        new("Moonshot",            "moonshot", 3, 0, 0, 'E', "rail_tube", null, null,            1, 1.0f,  3, true,  1125,  335, -0.058f,  0f,   90f,  0x100, "car",    "moonshot", 0),
        new("Escape Velocity",     "megacost", 3, 1, 0, 'F', "sc_rail", null, null,             2, 1.0f,  4, true,   325,  325,  5.0f,    0f,   90f,  0x60, "cart",    "megacost", 0),
        new("The Shocker",         "shocker",  3, 1, 1, 'A', "S_RAIL_SPARK_01", "S_RAIL_SPARK_01", null, 1, 1.0f, 6, false, 1470, 1470, 14.0f, 0f, 90f, 0, "car",     "shocker",  0),
    };

    public static CoasterType ForFolder(string folder) =>
        All.FirstOrDefault(c => c.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase));

    /// <summary>Moonshot's bank is forced to 0 (`0x19c818`) and its station segment is hidden
    /// (`0x19d1e0`).</summary>
    public bool IsMoonshot => World == 3 && Park == 0 && Ordinal == 0;

    /// <summary>`0x19a420`: the track height above a node's base for a pylon of height
    /// <paramref name="h"/>, in units. `attachY` is the posed `TrackDummyCentre` local y, loft
    /// channel `clamp(h/2560, 0, 1)` along the pylon's own path, added to the bind pose.</summary>
    public int Attach(int h)
    {
        float l = Math.Clamp(h / 2560f, 0f, 1f);
        float attachY = PylonBindY + LoftFrom + (LoftTo - LoftFrom) * l;
        return (int)(attachY * 256f / 10f + AttachOffset);
    }
}

public enum CoasterNodeKind : byte { Normal = 0, LeadIn = 1, Loop = 2 }

/// <summary>One sample of a segment (node `+0xd8 + i·0x48`): the point, the raw side vector, the
/// unit up, the raw tangent, the chord to the next sample and the two texture coordinates.</summary>
public struct CoasterSample
{
    public Vector3 Pos, Side, Normal, Tangent;
    public float Len, V0, V1, W0, W1;
    public bool Winch;
}

/// <summary>⭐ A COASTER NODE (0x620 bytes, class `0x1997d0`): a pylon, a loop node or one of the
/// two station nodes. Its segment is the curve ARRIVING at it, from its prev.
/// Positions are in units (256 a cell); the spline works in cells.</summary>
public sealed class CoasterNode
{
    public int CellX, CellZ;
    /// <summary>`+0x3c`/`+0x40`: the cell centre.</summary>
    public int X => CellX * 256 + 0x80;
    public int Z => CellZ * 256 + 0x80;
    /// <summary>`+0x3e`: terrain, the top of the pylon below, or −0x100 for a station node.</summary>
    public int YBase;
    /// <summary>`+0x44`, 0..0x500 from the tool, step 0x14.</summary>
    public int Height;
    /// <summary>`+0x4e`, ±0x200.</summary>
    public int Bank;
    /// <summary>`+0x4a`, `+0x4c`, `+0x50`: chord heading (1/4096 turn, 0 = +z, 0x400 = +x), half
    /// the turn to the next, 3D chord length in units.</summary>
    public int Heading, HalfTurn, Chord;
    /// <summary>`+0x52`/`+0x53`.</summary>
    public bool LoopFlag;
    public CoasterNodeKind Kind;
    /// <summary>`+0x54`: 0 draws every texture as `red.ssh` and stops the trains.</summary>
    public bool Valid = true;
    public bool IsStation;
    public CoasterNode Prev, Next, Above, Below;

    /// <summary>The window's control points in cells and their side vectors (`+0x7c`, `+0x5b8`).</summary>
    public readonly Vector3[] P = new Vector3[4];
    public readonly Vector3[] S = new Vector3[4];
    public readonly CoasterSample[] Samples = new CoasterSample[17];
    /// <summary>`+0xd0`: the 17 chords summed (the last one extrapolated).</summary>
    public float Length;
    /// <summary>`+0x68`/`+0x6c`: texture V carried in from the previous segment.</summary>
    public float VIn, WIn;
    /// <summary>The track point: y_base + attach (`0x19a420`), in units.</summary>
    public int TrackY;

    public ParkCell Cell => new(CellX, CellZ);
    public override string ToString() => $"{(IsStation ? "station" : Kind.ToString())}@({CellX},{CellZ}) h{Height}";
}

/// <summary>⭐⭐ A ROLLER COASTER'S TRACK: a ring of up to 32 player-placed nodes between two fixed
/// station nodes, drawn as a uniform Catmull-Rom spline (findings/coaster-geometry.md,
/// coaster-building.md). The ring runs exit → pylon 0 … pylon n−1 → entry → exit, so segment 0 is the
/// station (entry → exit) and a train at position p is on segment ⌊p⌋.
///
/// ⭐ Coordinates are the console's, one frame for the park: x and z in units (256 a cell), cell
/// (x, z) the park grid's (x, y). The viewer draws it in the same frame it draws the track rides in.</summary>
public sealed class CoasterTrack
{
    public const int MaxPylons = 32;
    public CoasterType Type { get; }
    public CoasterNode Exit { get; }
    public CoasterNode Entry { get; }
    readonly List<CoasterNode> _pylons = new();
    public IReadOnlyList<CoasterNode> Pylons => _pylons;
    /// <summary>`+0x148`: the ring is closed (the last pylon links to the entry node).</summary>
    public bool Closed { get; private set; }
    /// <summary>`+0x144`: every pylon and both station nodes valid (`0x1229d0`).</summary>
    public bool Valid => Exit.Valid && Entry.Valid && _pylons.All(n => n.Valid);
    /// <summary>The station's travel direction, entry → exit, 0..3 in the DBA encoding
    /// (0 = z−1, 1 = x−1, 2 = z+1, 3 = x+1).</summary>
    public int ExitDirection { get; }

    /// <summary>Terrain height of a cell's min corner in units (`0x149d90`: tile byte 1 × 4).</summary>
    public Func<int, int, int> GroundY { get; set; } = (_, _) => 0;

    /// <param name="exitCell">The cell one step outside the station's track exit (`0x11fdd0`).</param>
    /// <param name="exitDir">That step's direction, DBA encoding.</param>
    /// <param name="entryCell">The cell one step outside the track entry (`0x120288`).</param>
    public CoasterTrack(CoasterType type, ParkCell exitCell, int exitDir, ParkCell entryCell)
    {
        Type = type;
        ExitDirection = exitDir & 3;
        // 0x122060: both at y_base −0x100 with the table heights; entry.next = exit, exit.prev = entry.
        Exit = new CoasterNode { CellX = exitCell.X, CellZ = exitCell.Z, YBase = -0x100, Height = type.ExitHeight, IsStation = true };
        Entry = new CoasterNode { CellX = entryCell.X, CellZ = entryCell.Z, YBase = -0x100, Height = type.EntryHeight, IsStation = true };
        Entry.Next = Exit; Exit.Prev = Entry;
        Recompute();
    }

    /// <summary>The last node laid: the exit node when there are no pylons (`0x120508`).</summary>
    public CoasterNode Last => _pylons.Count > 0 ? _pylons[^1] : Exit;

    /// <summary>Ring order from the exit, the entry last (only linked in when closed).</summary>
    public IEnumerable<CoasterNode> Nodes()
    {
        yield return Exit;
        foreach (var n in _pylons) yield return n;
        yield return Entry;
    }

    /// <summary>Segments a train runs: n + 2 when closed.</summary>
    public int SegmentCount => _pylons.Count + 2;

    /// <summary>The node whose segment is index <paramref name="k"/>: 0 the exit (station segment),
    /// 1..n the pylons, n+1 the entry.</summary>
    public CoasterNode SegmentNode(int k)
    {
        if (k <= 0) return Exit;
        if (k <= _pylons.Count) return _pylons[k - 1];
        return Entry;
    }

    public CoasterNode NodeAt(ParkCell c) => Nodes().FirstOrDefault(n => n.CellX == c.X && n.CellZ == c.Z);

    /// <summary>⭐ `0x121d68`. Appends after the last node; closes the ring instead when the cell is
    /// the entry cell and a pylon exists. Returns the node, or null when it closed or overflowed.
    /// Validation is the caller's (`Valid`), exactly as on the console.</summary>
    public CoasterNode AddPylon(ParkCell cell, int height, int bank, bool loopFlag, CoasterNodeKind kind)
    {
        if (_pylons.Count >= MaxPylons) return null;
        if (cell == Entry.Cell && _pylons.Count != 0) { Close(); return null; }
        var last = Last;
        var n = new CoasterNode
        {
            CellX = cell.X, CellZ = cell.Z, Height = height, Bank = Type.IsMoonshot ? 0 : bank,
            LoopFlag = loopFlag, Kind = kind, Prev = last,
        };
        last.Next = n;
        var b = BottomAt(cell);
        if (b != null && b != last && Owns(b) && !b.IsStation)
        {
            var top = b; while (top.Above != null) top = top.Above;
            top.Above = n; n.Below = top;
        }
        _pylons.Add(n);
        Closed = false;
        Recompute();
        return n;
    }

    /// <summary>`0x120868`: the last node links to the entry node.</summary>
    public void Close()
    {
        var last = Last;
        Entry.Prev = last; last.Next = Entry;
        Closed = true;
        Recompute();
    }

    /// <summary>⭐ The tool's GHOST (`0x2ac438`): a node at the cursor linked after the last one --
    /// ghost.prev = last, last.next = ghost -- so the last segment and the ghost's own are shaped as
    /// they would be, and validated before anything is committed (`0x11aef0`). It stands on the top
    /// of this coaster's stack at the cursor, if there is one (the stacking preview). Take it out
    /// with <see cref="UnlinkGhost"/>.</summary>
    public CoasterNode LinkGhost(ParkCell cell, int height, int bank, CoasterNodeKind kind = CoasterNodeKind.Normal)
    {
        var last = Last;
        var g = new CoasterNode
        {
            CellX = cell.X, CellZ = cell.Z, Height = height, Bank = Type.IsMoonshot ? 0 : bank,
            LoopFlag = kind != CoasterNodeKind.Normal, Kind = kind, Prev = last,
        };
        var b = BottomAt(cell);
        if (b != null && !b.IsStation && !b.LoopFlag && b != last)
        {
            var top = b; while (top.Above != null) top = top.Above;
            g.Below = top;
        }
        last.Next = g;
        Recompute(g);
        return g;
    }

    public void UnlinkGhost(CoasterNode g)
    {
        if (g?.Prev != null && g.Prev.Next == g) g.Prev.Next = null;
        Recompute();
    }

    /// <summary>`0x1234d8`: entering the build tool on a closed ring reopens it.</summary>
    public void Reopen()
    {
        if (!Closed) return;
        Closed = false;
        Last.Next = null;
        Entry.Prev = null;
        Recompute();
    }

    /// <summary>`0x122460`: only the last node can be removed.</summary>
    public bool RemoveLast()
    {
        if (Closed) Reopen();
        if (_pylons.Count == 0) return false;
        var n = _pylons[^1];
        _pylons.RemoveAt(_pylons.Count - 1);
        if (n.Below != null) n.Below.Above = null;
        n.Below = null;
        Last.Next = null;
        Closed = false;
        Recompute();
        return true;
    }

    public bool Owns(CoasterNode n) => n == Exit || n == Entry || _pylons.Contains(n);

    /// <summary>`0x14c688(c, 9)` restricted to this coaster: the bottom node of the stack on a cell.</summary>
    public CoasterNode BottomAt(ParkCell c)
    {
        var n = Nodes().FirstOrDefault(x => x.CellX == c.X && x.CellZ == c.Z);
        while (n?.Below != null) n = n.Below;
        return n;
    }

    // -------------------------------------------------------------------------------------------
    // Geometry
    // -------------------------------------------------------------------------------------------

    /// <summary>Everything derived: base heights, headings, half-turns, windows, control points,
    /// side vectors, samples, lengths (0x19ad28 → 0x19aa48, 0x19cdd0, 0x19b208), for every node.
    /// The console rebuilds only dirty windows; the result is the same.</summary>
    public void Recompute(CoasterNode ghost = null)
    {
        var ring = Nodes().ToList();
        if (ghost != null) ring.Add(ghost);
        foreach (var n in ring) n.TrackY = BaseY(n) + Type.Attach(n.Height);
        foreach (var n in ring) ChordOf(n);
        foreach (var n in ring)
        {
            n.HalfTurn = 0;
            if (n.Kind == CoasterNodeKind.Normal && n.Next != null)
            {
                int d = n.Next.Heading - n.Heading;
                while (d > 0x800) d -= 0x1000;
                while (d < -0x800) d += 0x1000;
                n.HalfTurn = d >> 1;
            }
        }
        foreach (var n in ring) Window(n);
        // Texture V carries along the ring from the exit (zeroed every update, 0x123698).
        Exit.VIn = 0; Exit.WIn = 0;
        foreach (var n in ring) BuildSamples(n);
    }

    /// <summary>`0x19cae0`: station −0x100; a stacked node the top of the pylon below (⚠ the
    /// stack helper's posed y is not read here: the below node's attach point stands in for it);
    /// otherwise the terrain at the cell's min corner.</summary>
    int BaseY(CoasterNode n)
    {
        if (n.IsStation) return -0x100;
        if (n.Below != null) return n.Below.TrackY - Type.AttachOffset;
        return GroundY(n.CellX, n.CellZ);
    }

    /// <summary>`0x19aa48`: heading and chord of prev → self (next − self when there is no prev).</summary>
    static void ChordOf(CoasterNode n)
    {
        int dx, dy, dz;
        if (n.Prev != null) { dx = n.X - n.Prev.X; dy = n.TrackY - n.Prev.TrackY; dz = n.Z - n.Prev.Z; }
        else if (n.Next != null) { dx = n.Next.X - n.X; dy = n.Next.TrackY - n.TrackY; dz = n.Next.Z - n.Z; }
        else { dx = dy = dz = 0; }
        n.Heading = HeadingOf(dx, dz);
        if (n.Kind == CoasterNodeKind.Loop) n.Heading = (n.Heading + 0x400) & 0xfff;
        n.Chord = (int)Math.Sqrt((double)dx * dx + (double)dy * dy + (double)dz * dz);
    }

    /// <summary>`(dx>0 ? 0x400 : 0xc00) − atan(dz·4096/dx)`, axis cases first; 0 = +z, 0x400 = +x.</summary>
    public static int HeadingOf(int dx, int dz)
    {
        if (dx == 0) return dz < 0 ? 0x800 : 0;
        if (dz == 0) return dx > 0 ? 0x400 : 0xc00;
        int r = (int)((long)dz * 4096 / dx);
        int a = (int)(Math.Atan(r / 4096.0) * 4096.0 / (2 * Math.PI));
        return ((dx > 0 ? 0x400 : 0xc00) - a) & 0xfff;
    }

    /// <summary>`0x19a760` + `0x19b208`: (pp, prev, self, next) with the missing ends doubled, the
    /// control points in cells, and the side vectors from heading + half-turn and bank.</summary>
    void Window(CoasterNode n)
    {
        var next = n.Next ?? n;
        var prev = n.Prev ?? n;
        var pp = prev.Prev ?? prev;
        var w = new[] { pp, prev, n, next };
        float lastTheta = 0;
        for (int k = 0; k < 4; k++)
        {
            n.P[k] = new Vector3(w[k].X / 256f, w[k].TrackY / 256f, w[k].Z / 256f);
            float theta = w[k].Heading + w[k].HalfTurn;
            if (k > 0)
            {
                while (theta > lastTheta + 2048) theta -= 4096;
                while (theta < lastTheta - 2048) theta += 4096;
            }
            lastTheta = theta;
            double th = theta * 2 * Math.PI / 4096, be = -w[k].Bank * 2 * Math.PI / 4096;
            n.S[k] = new Vector3((float)(Math.Cos(th) * Math.Cos(be)), (float)Math.Sin(be),
                                 (float)(-Math.Sin(th) * Math.Cos(be)));
        }
    }

    static (float, float, float, float) Basis(float t) =>
        ((-t * t * t + 2 * t * t - t) / 2, (3 * t * t * t - 5 * t * t + 2) / 2,
         (-3 * t * t * t + 4 * t * t + t) / 2, (t * t * t - t * t) / 2);

    static (float, float, float, float) DBasis(float t) =>
        ((-3 * t * t + 4 * t - 1) / 2, (9 * t * t - 10 * t) / 2, (-9 * t * t + 8 * t + 1) / 2, (3 * t * t - 2 * t) / 2);

    /// <summary>⭐ `0x19bda0`: position (cells), raw side vector and raw tangent of a node's segment at
    /// spline parameter <paramref name="t"/> -- the Catmull-Rom, or the loop circle, or the
    /// lead-in/lead-out blends (coaster-geometry.md §2.1–2.2).</summary>
    public static (Vector3 Pos, Vector3 Side, Vector3 Tangent) Spline(CoasterNode n, float t)
    {
        var P = n.P;
        if (n.Kind == CoasterNodeKind.Loop)
        {
            var D = new Vector3(P[1].X - P[2].X, 0, P[1].Z - P[2].Z);
            var F = new Vector3(-D.Z, 0, D.X);
            float s = MathF.Sin(2 * MathF.PI * t), c = MathF.Cos(2 * MathF.PI * t);
            var pos = (1 - t) * P[1] + t * P[2] + 3 * s * F + new Vector3(0, 3 - 3 * c, 0);
            var vin = new Vector3(s * D.Z, c, -s * D.X);
            var tan = Vector3.Cross(D, vin);
            if (tan.LengthSquared() > 0) tan = Vector3.Normalize(tan);
            return (pos, D, tan);
        }
        var (b0, b1, b2, b3) = Basis(t);
        var (d0, d1, d2, d3) = DBasis(t);
        var C = P[0] * b0 + P[1] * b1 + P[2] * b2 + P[3] * b3;
        var Sd = n.S[0] * b0 + n.S[1] * b1 + n.S[2] * b2 + n.S[3] * b3;
        var T = P[0] * d0 + P[1] * d1 + P[2] * d2 + P[3] * d3;
        float w = (1 - MathF.Cos(MathF.PI * t)) / 2;
        if (n.Kind == CoasterNodeKind.LeadIn)
        {
            var d = new Vector3(P[2].X - P[3].X, 0, P[2].Z - P[3].Z);
            var L = new Vector3(P[2].X + (1 - t) * (P[1].X - P[2].X) * d.Z * d.Z, P[2].Y,
                                P[2].Z + (1 - t) * (P[1].Z - P[2].Z) * d.X * d.X);
            return ((1 - w) * C + w * L, (1 - w) * Sd + w * d, (1 - w) * T + w * new Vector3(-d.Z, 0, d.X));
        }
        if (n.Prev is { Kind: CoasterNodeKind.Loop })
        {
            var e = new Vector3(P[0].X - P[1].X, 0, P[0].Z - P[1].Z);
            var L = new Vector3(P[1].X + t * (P[2].X - P[1].X) * e.Z * e.Z, P[1].Y,
                                P[1].Z + t * (P[2].Z - P[1].Z) * e.X * e.X);
            return (w * C + (1 - w) * L, w * Sd + (1 - w) * e, w * T + (1 - w) * new Vector3(-e.Z, 0, e.X));
        }
        return (C, Sd, T);
    }

    /// <summary>`0x19b208`'s sample loop: 17 records at t = i/16, N = normalize(T × S), the chord to
    /// (i+1)/16 (extrapolated at i = 16), the segment length as their sum, V/W carried on.</summary>
    static void BuildSamples(CoasterNode n)
    {
        float v = n.VIn, w = n.WIn, total = 0;
        for (int i = 0; i <= 16; i++)
        {
            float t = i / 16f;
            var (pos, side, tan) = Spline(n, t);
            var N = Vector3.Cross(tan, side);
            if (N.LengthSquared() == 0) { N = Vector3.UnitY; tan = Vector3.Cross(side, N); }
            else N = Vector3.Normalize(N);
            float len = Vector3.Distance(Spline(n, (i + 1) / 16f).Pos, pos);
            bool winch = n.Samples[i].Winch;
            n.Samples[i] = new CoasterSample
            {
                Pos = pos, Side = side, Normal = N, Tangent = tan, Len = len,
                V0 = v, V1 = v + len, W0 = w, W1 = w + 4 * len, Winch = winch,
            };
            total += len;
            if (i < 16) { v = Frac(v + len); w = Frac(w + 4 * len); }
        }
        n.Length = total;
        // next.+0x68/+0x6c, skipped when next is the exit node (reset to 0 each update anyway).
        if (n.Next is { } nx && nx.Prev == n && !(nx.IsStation && nx.Prev?.IsStation == true))
        {
            var s16 = n.Samples[16];
            nx.VIn = Frac(s16.V0 + s16.Len);
            nx.WIn = Frac(s16.W0 + 4 * s16.Len);
        }
    }

    static float Frac(float x) => x - MathF.Floor(x);

    /// <summary>`0x19b180`: clear the 17 winch flags.</summary>
    public static void ClearWinch(CoasterNode n) { for (int i = 0; i < 17; i++) n.Samples[i].Winch = false; }

    /// <summary>`0x123b28` → `0x19b1a0`: flag samples ⌊16a⌋ .. ⌊16b⌋ of each segment the range [a, b]
    /// crosses, positions in segments from the exit's.</summary>
    public void MarkWinch(float a, float b)
    {
        int seg = 0;
        while (a >= 1 && seg < SegmentCount) { a -= 1; b -= 1; seg++; }
        while (seg < SegmentCount && b >= 0)
        {
            var n = SegmentNode(seg);
            int lo = Math.Max(0, (int)MathF.Floor(a * 16)), hi = Math.Min(16, (int)MathF.Floor(b * 16));
            for (int i = lo; i <= hi; i++) n.Samples[i].Winch = true;
            a = 0; b -= 1; seg++;
        }
    }

    // -------------------------------------------------------------------------------------------
    // Validity (0x1216d8)
    // -------------------------------------------------------------------------------------------

    /// <summary>What the park says about a cell, for <see cref="IsValid"/>.</summary>
    public interface IGround
    {
        bool InGrid(ParkCell c);
        /// <summary>`0x1e64d0`: no path, queue, ride, shop, scenery or anything else on it.</summary>
        bool EmptyLand(ParkCell c);
        /// <summary>The bottom coaster node on a cell, of ANY coaster (`0x14c688(c, 9)`).</summary>
        CoasterNode CoasterNodeAt(ParkCell c);
        /// <summary>Clearance a segment must stay above over this cell, in cells: a placed thing's
        /// model height, 2.0 over a flag-bit-0 tile, else 0 (`0x121000`).</summary>
        float Clearance(ParkCell c, CoasterTrack own);
    }

    /// <summary>`0x1216d8`: the node's placement rules. <paramref name="clearance"/> adds the segment
    /// clearance test (`0x121000`); the valid-cell field scan leaves it out.</summary>
    public bool IsValid(CoasterNode n, IGround g, bool clearance)
    {
        var P = n.Prev ?? n; var X = n.Next ?? n;
        bool ok = true;
        int d1 = HDist(n, n.Prev), d2 = HDist(X, X.Prev);
        if (!n.LoopFlag)
        {
            if (d1 < 0x300 || d1 > 0x800) ok = false;
            if (d2 < 0x300 || d2 > 0x800) ok = false;
            for (var r = Exit; r != null && r != Entry; r = r.Next)
                if (r != n && r.LoopFlag && r.CellX == n.CellX && r.CellZ == n.CellZ) { ok = false; break; }
            int lim = P == Exit ? 0x200 : 0x400;
            int t = Math.Abs((short)(n.Heading - P.Heading));
            if (!(P.LoopFlag && n.Next != null) && lim <= t && t < 0x1000 - lim) ok = false;
            if (!ok) return false;
            if (g.InGrid(n.Cell))
            {
                var b = g.CoasterNodeAt(n.Cell);
                if (b != null && b != n && b != n.Prev)
                {
                    if (b.LoopFlag) ok = false;
                    else if (!Owns(b)) ok = false;
                    else if (b == Exit) ok = false;
                    else
                    {
                        int count = 0;
                        for (var s = b; s != null; s = s.Above) if (s != n) count++;
                        if (count >= 2) ok = false;
                    }
                }
                else if (b == null && !g.EmptyLand(n.Cell)) ok = false;
            }
        }
        if (ok)
            for (int dx = -1; dx <= 1 && ok; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    var c = n.Cell.Offset(dx, dz);
                    if (!g.InGrid(c)) continue;
                    var m = g.CoasterNodeAt(c);
                    if (m != null && m != n && !m.LoopFlag) { ok = false; break; }
                }
        if (!ok) return false;
        if (clearance && !n.LoopFlag && P != n && !SegmentClear(P, n, g)) return false;
        int stack = 0;
        { var top = n; while (top.Above != null) top = top.Above; for (var s = top; s != null; s = s.Below) stack += s.Height; }
        if (stack > 0x600) return false;
        if (n.CellX == Entry.CellX && n.CellZ == Entry.CellZ)
        {
            int t = Math.Abs((short)(Exit.Heading - P.Heading));
            if ((ushort)(t - 0x400) <= 0x7ff) return false;
        }
        return true;
    }

    /// <summary>`0x19adf0`: horizontal distance in units, truncated.</summary>
    static int HDist(CoasterNode a, CoasterNode b)
    {
        if (b == null) return 0;
        double dx = a.X - b.X, dz = a.Z - b.Z;
        return (int)Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>`0x121000`, the part this port reproduces: ten samples of the segment arriving at
    /// <paramref name="b"/>, each above the clearance of the cells passed so far (the value carries
    /// over; a flag-bit-0 tile resets it to 2.0). ⚠ The segment-against-segment test (`0x1209b0`) is
    /// reduced to "no two segments of this coaster share a cell within one cell of height".</summary>
    bool SegmentClear(CoasterNode a, CoasterNode b, IGround g)
    {
        if (a.CellX == b.CellX && a.CellZ == b.CellZ && (a.LoopFlag || b.LoopFlag || a.Above != null || b.Above != null))
            return false;
        if (b == Exit) return true;
        float clear = 0;
        (int, int) last = (-1, -1);
        var mine = new List<Vector3>();
        for (int i = 0; i < 10; i++)
        {
            var p = Spline(b, i * 0.1f).Pos;
            mine.Add(p);
            var c = ((int)MathF.Floor(p.X), (int)MathF.Floor(p.Z));
            if (c != last)
            {
                last = c;
                float cl = g.Clearance(new ParkCell(c.Item1, c.Item2), this);
                if (cl == 2.0f) clear = 2.0f; else clear = Math.Max(clear, cl);
            }
            if (p.Y <= clear) return false;
        }
        foreach (var other in Nodes())
        {
            if (other == b || other == a || other.Prev == null || other == Exit || other == Entry) continue;
            if (other.Prev == a || other.Prev == b || other == a.Prev) continue;
            for (int i = 0; i < 10; i++)
            {
                var q = Spline(other, i * 0.1f).Pos;
                foreach (var p in mine)
                    if ((int)MathF.Floor(p.X) == (int)MathF.Floor(q.X) && (int)MathF.Floor(p.Z) == (int)MathF.Floor(q.Z)
                        && MathF.Abs(p.Y - q.Y) < 1f)
                        return false;
            }
        }
        return true;
    }
}
