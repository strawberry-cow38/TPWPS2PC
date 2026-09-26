namespace TPW.PS2.Data;

/// <summary>One laid track piece: its type (the index into <see cref="TrackPieces"/>) and the anchor
/// cell of its 2×2 block (min x, min z). Piece +0xEC and +0x84/+0x88 (findings/track-ride-geometry.md §8).</summary>
public sealed class TrackPiece
{
    public int Type { get; internal set; }
    public ParkCell Anchor { get; }
    /// <summary>The 4 baked samples (0x1FDBA8), one every 64 distance units.</summary>
    public TrackSample[] Samples { get; } = new TrackSample[4];
    public TrackPiece(int type, ParkCell anchor) { Type = type; Anchor = anchor; }
    public TrackPieceType Info => TrackPieces.Type(Type);
    public override string ToString() => $"{Type}@{Anchor}";
}

/// <summary>A baked sample (piece +0xB0 + 14i). Two lane end points in park units (256 per cell,
/// park x and z, NOT the viewer's mirrored z), the car height and the track yaw. Yaw is 4096 per
/// turn with 0 heading +z and 1024 heading +x, and is NOT wrapped: bends store up to 7168.
/// The pitch field is left out: only 0x1FE4E8 writes it, and nothing calls that.</summary>
public readonly record struct TrackSample(int P1X, int P1Z, int P2X, int P2Z, int Height, int Yaw);

public readonly record struct TrackPieceType(int Shape, int Width, int Depth, int ExitDir, int Rot);

/// <summary>The executable's track tables (findings/track-ride-geometry.md §2–§7).
/// Directions are 0 = +z, 1 = −z, 2 = +x, 3 = −x (0x200CD0).</summary>
public static class TrackPieces
{
    /// <summary>0x2EE1E0, 8 bytes a type: byte 1 shape, 2 width, 4 depth, 5 exit direction, 6 model
    /// rotation. Types 0–3 the station, 4–7 straights, 8–11 the hidden connector, 12–19 the two bend
    /// hands, 20–23 crossings, 24–39 bridge pieces, 40–51 add-ons (not laid by this port yet).</summary>
    static readonly byte[] Raw =
    {
        15,4,4,0,3, 15,4,4,1,1, 15,4,4,2,0, 15,4,4,3,2,
        0,2,2,0,0, 0,2,2,1,2, 0,2,2,2,1, 0,2,2,3,3,
        99,2,2,0,0, 99,2,2,1,2, 99,2,2,2,1, 99,2,2,3,3,
        1,2,2,2,0, 1,2,2,1,1, 1,2,2,3,2, 1,2,2,0,3,
        2,2,2,0,0, 2,2,2,2,1, 2,2,2,1,2, 2,2,2,3,3,
        4,2,2,0,0, 4,2,2,1,2, 99,2,2,2,1, 99,2,2,3,3,
        3,2,2,0,0, 3,2,2,1,2, 3,2,2,2,1, 3,2,2,3,3,
        9,2,2,0,2, 9,2,2,1,0, 9,2,2,2,3, 9,2,2,3,1,
        10,2,2,0,0, 10,2,2,1,2, 10,2,2,2,1, 10,2,2,3,3,
        11,2,2,0,0, 11,2,2,1,2, 11,2,2,2,1, 11,2,2,3,3,
        12,4,4,0,0, 12,4,4,1,2, 12,4,4,2,1, 12,4,4,3,3,
        13,4,4,0,0, 13,4,4,1,2, 13,4,4,2,1, 13,4,4,3,3,
        14,4,4,0,0, 14,4,4,1,2, 14,4,4,2,1, 14,4,4,3,3,
    };

    public const int Count = 52;

    public static TrackPieceType Type(int t)
    {
        if ((uint)t >= Count) throw new ArgumentOutOfRangeException(nameof(t));
        int o = t * 5;
        return new(Raw[o], Raw[o + 1], Raw[o + 2], Raw[o + 3], Raw[o + 4]);
    }

    /// <summary>0x2EE518, indexed [previous exit direction * 4 + new direction]. A U-turn gives 0,
    /// a station piece: garbage geometry the tool must never ask for.</summary>
    public static readonly byte[] Corner = { 4, 0, 17, 14, 0, 5, 12, 19, 15, 18, 6, 0, 16, 13, 0, 7 };

    /// <summary>0x2EE54A: per station rotation, {dx, dz, type} for the station piece, the connector and
    /// the exit straight (the third is laid only while the track has at most one waypoint).</summary>
    static readonly (int Dx, int Dz, int Type)[,] StationRaw =
    {
        { (0, 0, 2), (2, 0, 10), (4, 1, 6) },
        { (0, 2, 1), (0, 0, 9), (1, -2, 5) },
        { (2, 2, 3), (0, 2, 11), (-2, 1, 7) },
        { (2, 0, 0), (2, 2, 8), (1, 4, 4) },
    };

    public static (int Dx, int Dz, int Type) Station(int rot, int k) => StationRaw[rot & 3, k];

    /// <summary>0x200078: the station's exit cell, which is waypoint 0.</summary>
    public static ParkCell Exit(ParkCell station, int rot) => (rot & 3) switch
    {
        0 => station.Offset(4, 1), 1 => station.Offset(1, -2), 2 => station.Offset(-2, 1), _ => station.Offset(1, 4),
    };

    /// <summary>0x2001A8: the entry cell a closing waypoint must equal. These are the `.sam`
    /// `Bumper.{N,E,S,W}{X,Y}Adjust` pairs, hard-coded as immediates on PS2.</summary>
    public static ParkCell Return(ParkCell station, int rot) => (rot & 3) switch
    {
        0 => station.Offset(-2, 1), 1 => station.Offset(1, 4), 2 => station.Offset(4, 1), _ => station.Offset(1, -2),
    };

    /// <summary>0x200FB8: the direction the last piece is forced to face when it lands on the return
    /// cell, which is into the station.</summary>
    public static int IntoStation(int rot) => (rot & 3) switch { 0 => 2, 1 => 1, 2 => 3, _ => 0 };

    /// <summary>0x200C20: a piece's Excitement weight. Crossings and add-ons 4, humps and ramps 2,
    /// everything else (including the raised middle `h_a`) 1.</summary>
    public static int Weight(int type) => type switch
    {
        >= 20 and <= 23 => 4,
        >= 40 and <= 51 => 4,
        >= 24 and <= 31 => 2,
        >= 36 and <= 39 => 2,
        _ => 1,
    };
}

/// <summary>What the track needs to know about the ground: the per-park sample heights (0x1FDBA8)
/// and whether a block holds a path, queue or connection tile (0x1E6338/0x1E6348), which makes a
/// bridge piece.</summary>
public sealed class TrackGround
{
    /// <summary>0 JUNGLE, 1 HALLOW, 2 FANTASY, 3 SPACE (0x3952E4).</summary>
    public int World { get; init; }
    /// <summary>0 for the world's first park, 1 for its second (0x3952E8).</summary>
    public int Park { get; init; }
    /// <summary>True when the cell is a path, a queue or an attraction's connection A (tile kinds 2, 4, 7).</summary>
    public Func<ParkCell, bool> Bridged { get; init; } = _ => false;
}

/// <summary>⭐ A track ride's track: the player's waypoints, and the pieces 0x2009C0 lays from them.
/// Every edit re-lays the whole track from scratch, exactly as the console does.
///
/// Positions are in park units: 256 per cell, park x and z. Heights are relative to the station's
/// level (station y 0), in the same units. ⚠ Whether the console's heights really share the x/z
/// scale is not settled (findings/track-rides.md, "Still unknown"). This is the only reading in which
/// the cars and the pieces agree.
///
/// Add-ons (types 40–51) are not laid yet. The per-park add-on tables overlap, and which catalogue
/// entry becomes which kind is unresolved.</summary>
public sealed class TrackLayout
{
    /// <summary>0x2016E0: at most 34 waypoints (`sltiu count, 0x22`).</summary>
    public const int MaxWaypoints = 34;
    /// <summary>0x154E40 builds 36 piece slots; the tool refuses a leg past them (0x202F78).</summary>
    public const int MaxPieces = 36;
    /// <summary>Distance along the track per piece (ride +0x2854 = pieces << 8).</summary>
    public const int PieceLength = 256;

    readonly List<ParkCell> _waypoints = new();
    readonly List<TrackPiece> _pieces = new();

    public ParkCell Station { get; }
    public int Rotation { get; }
    public TrackGround Ground { get; }
    public IReadOnlyList<ParkCell> Waypoints => _waypoints;
    public IReadOnlyList<TrackPiece> Pieces => _pieces;
    /// <summary>Ride +0x1C6: the last waypoint is the station's entry. The ONLY validity rule.</summary>
    public bool Closed { get; private set; }
    public int Length => _pieces.Count * PieceLength;
    public ParkCell ExitCell => TrackPieces.Exit(Station, Rotation);
    public ParkCell ReturnCell => TrackPieces.Return(Station, Rotation);
    /// <summary>Ride +0x1D1 (0x200C20): the pieces' summed weight, Excitement's track term.</summary>
    public int Weight => _pieces.Sum(p => TrackPieces.Weight(p.Type));

    public TrackLayout(ParkCell station, int rotation, TrackGround ground)
    {
        Station = station;
        Rotation = rotation & 3;
        Ground = ground ?? throw new ArgumentNullException(nameof(ground));
        // 0x1291D8: entering the tool seeds waypoint 0 with the exit cell.
        _waypoints.Add(ExitCell);
        Rebuild();
    }

    /// <summary>0x2016E0: append a waypoint. Closed becomes true iff it is the return cell. Rebuilds.</summary>
    public bool Add(ParkCell c)
    {
        Closed = false;
        if (_waypoints.Count >= MaxWaypoints) return false;
        _waypoints.Add(c);
        Closed = c == ReturnCell;
        Rebuild();
        return true;
    }

    /// <summary>0x2017D0: drop the last waypoint (never waypoint 0) and rebuild. Returns the removed
    /// leg's piece count, which the tool refunds.</summary>
    public int RemoveLast()
    {
        Closed = false;
        if (_waypoints.Count < 2) return 0;
        var gone = _waypoints[^1];
        _waypoints.RemoveAt(_waypoints.Count - 1);
        var prev = _waypoints[^1];
        Rebuild();
        return Math.Max(Math.Abs(gone.X - prev.X), Math.Abs(gone.Z - prev.Z)) >> 1;
    }

    /// <summary>The tool's leg for a cursor cell (0x1293C8): along the dominant axis (ties to z),
    /// snapped to 2-cell steps. Returns the end cell and the step count.</summary>
    public (ParkCell End, int Steps, int Dx, int Dz) Leg(ParkCell cursor)
    {
        var prev = _waypoints[^1];
        int dx = cursor.X - prev.X, dz = cursor.Z - prev.Z;
        if (Math.Abs(dz) < Math.Abs(dx))
        {
            int n = Math.Abs(dx) >> 1, s = Math.Sign(dx) * 2;
            return (prev.Offset(s * n, 0), n, s, 0);
        }
        else
        {
            int n = Math.Abs(dz) >> 1, s = Math.Sign(dz) * 2;
            return (prev.Offset(0, s * n), n, 0, s);
        }
    }

    /// <summary>The piece whose 2×2 block covers a cell, or null (0x202E00).</summary>
    public TrackPiece PieceAt(ParkCell c)
    {
        foreach (var p in _pieces)
        {
            int w = p.Type >= 40 ? 4 : 2;
            if (c.X >= p.Anchor.X && c.X < p.Anchor.X + w && c.Z >= p.Anchor.Z && c.Z < p.Anchor.Z + w) return p;
        }
        return null;
    }

    /// <summary>0x2009C0: throw the track away and lay it again.</summary>
    public void Rebuild()
    {
        _pieces.Clear();
        int n = _waypoints.Count <= 1 ? 3 : 2;
        for (int k = 0; k < n; k++)
        {
            var (dx, dz, type) = TrackPieces.Station(Rotation, k);
            Place(Station.Offset(dx, dz), type);
        }
        for (int i = 0; i + 1 < _waypoints.Count; i++)
            LayLeg(_waypoints[i], _waypoints[i + 1], i == _waypoints.Count - 2);
        // 0x2027E0 builds the samples lazily, one piece per update, in chain order. The connector
        // reads "the previous non-connector piece" through a global (0x2EE4C0), so building them in
        // order here gives the same answer without the global.
        int previous = -1;
        foreach (var p in _pieces)
        {
            Bake(p, previous);
            if (p.Type is < 8 or > 11) previous = p.Type;
        }
    }

    void Place(ParkCell at, int type) => _pieces.Add(new TrackPiece(type, at));

    /// <summary>0x200CD0. A leg must be axis-aligned; the x test comes first, so a diagonal one is
    /// treated as an x leg. Pieces go every 2 cells starting ON the first waypoint; a non-final leg
    /// stops short of its end, which is the next leg's start.</summary>
    void LayLeg(ParkCell a, ParkCell b, bool last)
    {
        int n, sx = 0, sz = 0, dir;
        if (a == b) { n = 0; dir = _pieces[^1].Info.ExitDir; }
        else if (a.X < b.X) { n = b.X - a.X; sx = 2; dir = 2; }
        else if (b.X < a.X) { n = a.X - b.X; sx = -2; dir = 3; }
        else if (a.Z < b.Z) { n = b.Z - a.Z; sz = 2; dir = 0; }
        else { n = a.Z - b.Z; sz = -2; dir = 1; }
        if (!last) n -= 2;
        var pos = a;
        while (n >= 0)
        {
            Choose(pos, dir, n <= 1 && last);
            n -= 2;
            pos = pos.Offset(sx, sz);
        }
    }

    /// <summary>0x200FB8, without the add-on rule: a crossing, then a bridge, then the corner table.</summary>
    void Choose(ParkCell pos, int dir, bool isLast)
    {
        var last = _pieces[^1];
        // Rule 2: the new piece lands exactly on an existing piece's anchor (any but the last).
        for (int k = 0; k < _pieces.Count - 1; k++)
            if (_pieces[k].Anchor == pos)
            {
                _pieces[k].Type = _pieces[k].Info.ExitDir + 0x14;
                Place(pos, dir + 0x14);
                return;
            }
        int lastDir = last.Info.ExitDir;
        int idx = lastDir << 2 | (isLast && pos == ReturnCell ? TrackPieces.IntoStation(Rotation) : dir);
        // Rule 4: a path, queue or connection tile under the block makes a bridge piece. Every retail
        // track ride has record byte +0xC0 = 1, so spans chain h_u, h_a..., h_d.
        bool bridged = false;
        for (int x = 0; x < 2 && !bridged; x++)
            for (int z = 0; z < 2 && !bridged; z++)
                bridged = Ground.Bridged(pos.Offset(x, z));
        if (bridged)
        {
            if (last.Type is >= 0x18 and <= 0x1b) { last.Type = 0x1c + lastDir; Place(pos, 0x24 + lastDir); }
            else if (last.Type is >= 0x24 and <= 0x27) { last.Type = 0x20 + lastDir; Place(pos, 0x24 + lastDir); }
            else Place(pos, 0x18 + lastDir);
            return;
        }
        Place(pos, TrackPieces.Corner[idx]);
    }

    /// <summary>0x1FDBA8: the piece's 4 samples. Local frame, then R(r) and the model origin (0x1FCDB8).</summary>
    void Bake(TrackPiece p, int previousType)
    {
        var info = p.Info;
        int r = info.Rot, t = p.Type;
        int w = info.Width << 8, d = info.Depth << 8;
        int ox = p.Anchor.X * 256, oz = p.Anchor.Z * 256;
        switch (r)
        {
            case 1: oz += w; break;
            case 2: ox += w; oz += d; break;
            case 3: ox += d; break;
        }
        int world = Ground.World, park = Ground.Park;
        int baseH = 0x50;
        if (world == 1 && park == 1) baseH = 0x90;
        if (world == 2 && park == 1) baseH += 0x40;
        for (int i = 0; i < 4; i++)
        {
            double p1x = 160, p1z = 128 * i, p2x = 352, p2z = 128 * i, y = r;
            int h = baseH;
            switch (t)
            {
                case <= 3:
                    p1x = p2x = 128 * i; p1z = 608; p2z = 416; y = r + 1;
                    if (t == 0) { p1z += 512; p2z += 512; }
                    else if (t == 3) { p1x += 512; p2x += 512; p1z += 512; p2z += 512; }
                    else if (t == 1) { p1x += 512; p2x += 512; }
                    break;
                case >= 8 and <= 11:
                    if (previousType is >= 0 and < 4) { p1x -= 256; p2x -= 256; }
                    break;
                case >= 12 and <= 15:
                {
                    float th = i * MathF.PI / 8;
                    p1x = 512 - Trunc(160f * MathF.Cos(th)); p1z = 512 - Trunc(160f * MathF.Sin(th));
                    p2x = 512 - Trunc(352f * MathF.Cos(th)); p2z = 512 - Trunc(352f * MathF.Sin(th));
                    y = (r + 2) - (i + 1) / 4.0;
                    break;
                }
                case >= 16 and <= 19:
                {
                    float th = (3 - i) * MathF.PI / 8;
                    p1x = 512 - Trunc(352f * MathF.Cos(th)); p1z = 512 - Trunc(352f * MathF.Sin(th));
                    p2x = 512 - Trunc(160f * MathF.Cos(th)); p2z = 512 - Trunc(160f * MathF.Sin(th));
                    y = (r + 3) + (i + 1) / 4.0;
                    break;
                }
                case >= 24 and <= 27:
                    if (i != 0) h = 0x100;
                    break;
                case >= 28 and <= 31:
                    p1x = 352; p1z = (4 - i) * 128; p2x = 160; p2z = (4 - i) * 128; y = r + 2;
                    if (i != 0) h = 0x100;
                    break;
                case >= 32 and <= 39:
                    h = 0x100;
                    break;
            }
            if (world == 3 && park == 1) h += 0x5a;
            var (a1, b1) = Rotate(r, (int)p1x, (int)p1z);
            var (a2, b2) = Rotate(r, (int)p2x, (int)p2z);
            p.Samples[i] = new TrackSample(a1 + ox, b1 + oz, a2 + ox, b2 + oz, h, (int)(y * 1024));
        }
    }

    static int Trunc(float v) => (int)v;

    /// <summary>0x194B90 with θ = −r·π/2: x' = cosθ·x − sinθ·z, z' = sinθ·x + cosθ·z.</summary>
    static (int X, int Z) Rotate(int r, int x, int z) => (r & 3) switch
    {
        0 => (x, z),
        1 => (z, -x),
        2 => (-x, -z),
        _ => (-z, x),
    };

    /// <summary>0x202898: the sample at distance d (piece ((d & 0xFFFF) % length) >> 8, slot (d & 0xFF) >> 6).</summary>
    public TrackSample Sample(int d)
    {
        int len = Length;
        if (len == 0) throw new InvalidOperationException("no track");
        int at = (d & 0xffff) % len;
        return _pieces[at >> 8].Samples[(at & 0xff) >> 6];
    }

    /// <summary>0x202938: the track yaw at distance d.</summary>
    public int Yaw(int d) => Sample(d).Yaw;

    /// <summary>0x204C38/0x2039C8: a car's position from its distance and lateral (0..256, 128 the
    /// centre line, 0 on P1). Two lerps: across the lane, then between the samples 64 units apart.
    /// The console's arithmetic shifts are floors; kept.</summary>
    public (int X, int Y, int Z) Position(int d, int lateral)
    {
        var a = Sample(d);
        var b = Sample(d + 0x40);
        int f = d & 0x3f;
        int ax = a.P1X + ((a.P2X - a.P1X) * lateral >> 8), az = a.P1Z + ((a.P2Z - a.P1Z) * lateral >> 8);
        int bx = b.P1X + ((b.P2X - b.P1X) * lateral >> 8), bz = b.P1Z + ((b.P2Z - b.P1Z) * lateral >> 8);
        return (ax + ((bx - ax) * f >> 6), a.Height + ((b.Height - a.Height) * f >> 6), az + ((bz - az) * f >> 6));
    }
}
