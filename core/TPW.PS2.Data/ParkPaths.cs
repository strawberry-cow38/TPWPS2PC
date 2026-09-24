using System.Numerics;
using System.Text.RegularExpressions;

namespace TPW.PS2.Data;

public readonly record struct ParkCell(int X, int Z)
{
    public ParkCell Offset(int x, int z) => new(X + x, Z + z);
    public override string ToString() => $"({X},{Z})";
}

public enum ParkPathKind { None, Path, Queue }

/// <summary>A mutable park grid copied from the terrain. Rendering and routing consume the same
/// material bytes. Construction uses the engine's byte0 bit-0 rule, plus ride occupancy and
/// conservative fixed-scenery exclusion. Walking additionally requires a laid path material;
/// this is a demo routing policy, not a recovered engine navigation service.</summary>
public sealed class ParkPaths
{
    public Model.HeightField Field { get; }
    public IReadOnlyList<string> Materials { get; }
    /// <summary>The plot's corner in the terrain MODEL's own space. ⚠ Model z, not world Z: the
    /// scene mirrors Z, so a cell's world position is <c>-(Origin.Y + cellZ)</c>.</summary>
    public Vector2 Origin { get; }
    readonly HashSet<ParkCell> _occupied = new();
    readonly HashSet<ParkCell> _scenery = new();
    readonly HashSet<ParkCell> _entrance = new();
    /// <summary>What this park's own flagpoles say its walkway column is, for the one case where
    /// two table entries both fit the grid -- see <see cref="ParkEntrance.WalkwayColumnFromPoles"/>.
    /// Taken in the constructor because that is where the terrain MODEL is; SetEntrance only gets
    /// the grid.</summary>
    readonly int? _walkwayColumn;

    /// <summary>⭐⭐ THE WALKWAY THE PARK COMES WITH -- the way in from the gate -- READ FROM THE
    /// GAME'S OWN TABLE at 0x2B71B0 and painted the way 0x14E5B0 paints it. See
    /// <see cref="ParkEntrance"/>; it is set by <see cref="SetEntrance"/> because the table lives
    /// in the executable and this class is handed a terrain file.
    ///
    /// ⚠⚠ THIS REPLACES A GUESS, AND THE GUESS WAS WRONG. Until it was checked against a live
    /// park, the entrance here was every cell covered by a mesh called `A_ROAD`, `A_BUS STOP` or
    /// `ticket_booths` -- 147 cells at z 33..47 in FANTASY, against the game's 29 at x 39..40,
    /// z 6..18. A two-tile walkway, not a fifteen-wide apron, and somewhere else entirely.</summary>
    public IReadOnlyCollection<ParkCell> EntranceCells => _entrance;

    /// <summary>⭐⭐ THE GATE'S OWN GROUND, master's rule: "give the gate an occupancy over the
    /// tiles it sits on, + 1 on each side. mark a 2x2 of paths (right under the gate) as
    /// un-deleteable."
    ///
    /// ⭐ Anchored on READ data rather than the gate model's position: the entrance table gives
    /// the walkway's column pair (`XCol`, `XCol + 1`) and `ZEnd`, whose mouth is `ZEnd - 1`. The
    /// 2x2 is those two columns across the threshold -- the mouth row and the first row inside
    /// the park -- which is the ground a gate straddles by construction. The occupancy is that
    /// rectangle grown by one on every side.
    ///
    /// ⚠ The occupancy is SEPARATE from the authored no-build zone the `.sam` carries, which is
    /// a different and much larger rectangle that mostly lies outside the plot. Both exist; this
    /// one is about the tiles the gate stands on.
    /// ⚠ `Protected` is a flag with no consumer yet -- master: "we dont have delete, but just
    /// give them that flag" -- so it is deliberately inert rather than wired to nothing.</summary>
    public IReadOnlyCollection<ParkCell> Protected => _protected;
    readonly HashSet<ParkCell> _protected = new();
    public bool IsProtected(ParkCell c) => _protected.Contains(c);

    /// <summary>The gate's own ground: its 2x2 grown by one on each side. ⚠ Blocks BUILDING
    /// only -- <see cref="CanLay"/> must still accept these cells or nobody can walk in.</summary>
    public IReadOnlyCollection<ParkCell> GateHold => _gateHold;
    readonly HashSet<ParkCell> _gateHold = new();
    public bool GateHolds(ParkCell c) => _gateHold.Contains(c);

    /// <summary>Paint the park's own entrance into this grid.</summary>
    public string SetEntrance(ParkEntrance table)
    {
        _entrance.Clear();
        _protected.Clear();
        _gateHold.Clear();
        if (table == null) return "no entrance table";
        var entry = table.Fit(Field, _walkwayColumn, out string report);
        if (!entry.Empty)
            foreach (var (x, z, _) in entry.Cells())
                if (x >= 0 && z >= 0 && x < Field.Width && z < Field.Height) _entrance.Add(new ParkCell(x, z));
        int gate = 0, held = 0;
        if (!entry.Empty)
        {
            // ⚠ The protected pair moves with the gate: "right under the gate", and the gate is
            // now the 8x2 on the park's first two rows, so these are the walkway's own columns
            // on those same rows rather than straddling the threshold.
            for (int x = entry.XCol; x <= entry.XCol + 1; x++)
                for (int z = entry.ZEnd; z <= entry.ZEnd + 1; z++)
                {
                    var c = new ParkCell(x, z);
                    if (!Contains(c)) continue;
                    _protected.Add(c); held++;
                }
            // ⚠ Clipped rather than refused: the gate stands at the plot's edge, so part of its
            // skirt is off the map by construction.
            //
            // ⭐⭐ EIGHT BY TWO, INSIDE THE PARK. Master, third pass and the clearest statement
            // of it: "gate should be 8x2 (inside the park, the first tiles against that middle
            // inset.)" The walkway's two columns ARE that inset, so the block is centred on them
            // -- three tiles left, three right -- and sits on the first two rows the park owns,
            // `ZEnd` and `ZEnd + 1`. ⚠ Not straddling the threshold and not reaching back up the
            // walkway, which is what the previous two versions did.
            for (int x = entry.XCol - 3; x <= entry.XCol + 4; x++)
                for (int z = entry.ZEnd; z <= entry.ZEnd + 1; z++)
                {
                    // ⚠⚠ NOT `_occupied`. The first version put these in it, and `CanLay`
                    // consults `_occupied` -- so the gate's own skirt refused the entrance path
                    // and the departure fixture came back "no initial route". The audit caught
                    // it before it reached anyone. ⭐ What master asked for is that you cannot
                    // BUILD on the gate's ground; walking and path-laying across the threshold
                    // are the entrance's whole purpose.
                    var c = new ParkCell(x, z);
                    // ⭐⭐ THE FOUR PROTECTED PATH TILES ARE EXEMPT. Master, with a picture:
                    // "if i update the pp's it makes a hole in the 8x2. make the 4pp tiles exempt
                    // from the no-build zone." They are the park's own entrance path -- the thing
                    // the gate exists to let guests walk down -- so refusing a build on them and
                    // then having the player legitimately change them is what punched the hole.
                    // ⚠ They keep <see cref="Protected"/>, which is a DIFFERENT flag and the one
                    // they were given for: un-deleteable, not un-buildable.
                    if (_protected.Contains(c)) continue;
                    if (!Contains(c) || !_gateHold.Add(c)) continue;
                    gate++;
                }
        }
        return $"{report}; {_entrance.Count} cells; gate holds {gate} cells and protects {held} paths";
    }

    public IEnumerable<ParkCell> Cells => Enumerable.Range(0, Field.Count)
        .Select(i => new ParkCell(i % Field.Width, i / Field.Width));

    public ParkPaths(Model terrain)
    {
        var f = terrain.Field ?? throw new ArgumentException("Terrain has no authored grid");
        Field = new Model.HeightField { Width = f.Width, Height = f.Height, Cells = (byte[])f.Cells.Clone() };
        Materials = terrain.Materials.AsReadOnly();
        _walkwayColumn = ParkEntrance.WalkwayColumnFromPoles(terrain);
        var marker = terrain.Meshes.Single(m => string.Equals(m.Name, "heightfield", StringComparison.OrdinalIgnoreCase));
        var transforms = terrain.WorldTransforms();
        var a = Vector3.Transform(marker.BoundsMin, transforms[marker.Offset]);
        var b = Vector3.Transform(marker.BoundsMax, transforms[marker.Offset]);
        var lo = Vector3.Min(a, b); var size = (Vector3.Max(a, b) - lo) / 1.004f;
        lo += size * 0.001f;
        if (Math.Abs(size.X - f.Width) > 0.001f || Math.Abs(size.Z - f.Height) > 0.001f)
            throw new InvalidDataException("Terrain marker and cell grid disagree");
        // ⚠⚠ THE ROW ORDER IS NOT REVERSED HERE, AND IT WAS. `-(lo.Z + size.Z)` with a later
        // `-v.Z - Origin.Y` counts rows from the FAR end of the plot, so everything this class
        // rasterised came out at `H - z` -- the opposite end of the park from where it is. The
        // renderer has never done that (Park.Build maps row 15 to world -15.5, its own comment
        // says so and cites the ticket booths at model z 14.9..16.1), so the two have disagreed
        // since this file was written and the entrance hunt walked straight into it: the booths
        // read as cell z=61 of 76 instead of 15, and a rasterised "entrance" duly appeared at
        // z 57..71. The game's own table puts it at z 6..18.
        //
        // ⚠ The count-based check that let this stand was no check at all: our disc field and the
        // field in master's savestate agree on 872 skipped cells of 4800, and a MIRRORED array
        // agrees on that too. Cells are compared by POSITION now (see the booths control in
        // --walk-audit), because a total cannot see a flip.
        //
        // Model z runs 0..H across the plot and cell z is model z, plainly.
        Origin = new Vector2(lo.X, lo.Z);
        // Conservative footprint of fixed scenery, through the real mesh parent chains. Even a
        // canopy or authored road blocks new construction here; no guessed terrain heights or
        // collision flags. Triangle/square SAT includes thin walls missed by centre samples.
        foreach (var mesh in terrain.Meshes)
        {
            var vertices = terrain.Vertices(mesh).Pos.Select(v => Vector3.Transform(v, transforms[mesh.Offset]))
                .Select(v => new Vector2(v.X - Origin.X, v.Z - Origin.Y)).ToArray();
            foreach (var triangle in terrain.Triangles(mesh))
            {
                var p = vertices[triangle.A]; var q = vertices[triangle.B]; var r = vertices[triangle.C];
                int x0 = Math.Max(0, (int)Math.Floor(Math.Min(p.X, Math.Min(q.X, r.X))));
                int x1 = Math.Min(f.Width - 1, (int)Math.Floor(Math.Max(p.X, Math.Max(q.X, r.X))));
                int z0 = Math.Max(0, (int)Math.Floor(Math.Min(p.Y, Math.Min(q.Y, r.Y))));
                int z1 = Math.Min(f.Height - 1, (int)Math.Floor(Math.Max(p.Y, Math.Max(q.Y, r.Y))));
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                {
                    var c = new ParkCell(x, z);
                    bool hit = Intersects(p, q, r, new Vector2(x + 0.5f, z + 0.5f));
                    if (hit && !_scenery.Contains(c)) _scenery.Add(c);
                }
            }
        }
    }
    public bool Contains(ParkCell c) => c.X >= 0 && c.Z >= 0 && c.X < Field.Width && c.Z < Field.Height;

    /// <summary>Is this one of the park's own entrance cells -- bus stop, road, turnstiles?</summary>
    public bool IsEntrance(ParkCell c) => _entrance.Contains(c);

    /// <summary>⭐ PUBLIC GROUND: what a visitor with nowhere particular to be may stand on. Laid
    /// path, or the entrance the park came with. ⚠ NOT a queue -- a queue belongs to its ride, and
    /// letting anyone walk it would make every queue a shortcut.</summary>
    public bool Open(ParkCell c) => Contains(c) && (IsEntrance(c) || Kind(c) == ParkPathKind.Path);
    public bool SceneryBlocks(ParkCell c) => _scenery.Contains(c);
    // ⚠ `!IsBridge` -- master: the bridge "should be unbuildable". Its cells carry byte0 bit 0
    // CLEAR, so the terrain's own no-build bit says they are fair game; the refusal is the
    // bridge's, not the ground's.
    public bool CanBuild(ParkCell c) => Contains(c) && Field.Buildable(c.X, c.Z) && !IsBridge(c)
                                     && !_occupied.Contains(c) && !_scenery.Contains(c) && !_gateHold.Contains(c);
    /// <summary>Does this triangle cover the cell at (x,z)? ⚠ PUBLIC so an audit can build the same
    /// coverage the constructor does instead of a looser one -- a control that rasterises by
    /// BOUNDING BOX and compares itself against an exact set reports disagreements that are its
    /// own, which is how "the embankment is walkable" was read off a map that had simply painted
    /// the bank over the road.</summary>
    public static bool TriangleCoversCell(Vector2 a, Vector2 b, Vector2 c, int x, int z)
        => Intersects(a, b, c, new Vector2(x + 0.5f, z + 0.5f));

    static bool Intersects(Vector2 a, Vector2 b, Vector2 c, Vector2 centre)
    {
        var ab = b - a; var bc = c - b; var ca = a - c;
        foreach (var axis in new[] { Vector2.UnitX, Vector2.UnitY, new Vector2(-ab.Y, ab.X), new Vector2(-bc.Y, bc.X), new Vector2(-ca.Y, ca.X) })
        {
            if (axis == Vector2.Zero) continue;
            float pa = Vector2.Dot(a - centre, axis), pb = Vector2.Dot(b - centre, axis), pc = Vector2.Dot(c - centre, axis);
            float radius = (Math.Abs(axis.X) + Math.Abs(axis.Y)) * 0.5f;
            if (Math.Min(pa, Math.Min(pb, pc)) > radius || Math.Max(pa, Math.Max(pb, pc)) < -radius) return false;
        }
        return true;
    }
    /// <summary>⭐⭐ THE BRIDGE DECK. Master: "theres a bridge on the jungle 1 map. it should
    /// count as path tiles, be unbuildable, and have paths connect to it."
    ///
    /// ⚠⚠ DELIBERATELY NOT PART OF <see cref="Classify"/>. That function feeds the sprite tables,
    /// and `PathTool` refuses to start unless it yields **exactly sixteen path and four queue**
    /// materials -- so classifying the bridge as path would take the count to seventeen and
    /// disable the path tool outright. The bridge is a separate fact about a cell: walkable and
    /// linkable like path, but not a path SPRITE and never laid or drawn as one.
    ///
    /// ⭐ Searched all eight parks: the only bridge on the disc is JUNGLE park 1's, four cells at
    /// x 32..35 z 65, material `jbr_log1` -- a log deck -- beside a terrain mesh named `BRIDGE`.
    /// `jbr_tnk1` and `jbr_rai1` are in that world's material table and painted on no cell; they
    /// are the tank and the RAILING, which is why this matches the deck and not the prefix. A new
    /// world's bridge would need its deck material adding here rather than inheriting a guess.</summary>
    public static bool IsBridgeDeck(string material)
        => Regex.IsMatch(Path.GetFileNameWithoutExtension(material ?? ""), @"^[a-z]br_log\d+$",
                         RegexOptions.IgnoreCase);

    public static ParkPathKind Classify(string material)
    {
        var m = Regex.Match(Path.GetFileNameWithoutExtension(material),
            @"^[a-z]pa_(str|cnr|ctr|edg|end|tju|xrd|que|squ|icn)\d+$", RegexOptions.IgnoreCase);
        return !m.Success ? ParkPathKind.None : m.Groups[1].Value.Equals("que", StringComparison.OrdinalIgnoreCase)
            ? ParkPathKind.Queue : ParkPathKind.Path;
    }
    /// <summary>What is painted on this cell -- and ONLY that.
    ///
    /// ⭐⭐ "WHAT IS HERE" IS NOT "WHAT MAY BE BUILT HERE", and asking one question in place of
    /// the other sealed three parks out of four. This used to begin `if (!CanBuild(c)) return
    /// None`, so a cell the scenery projection had ruled out could not read as path even when the
    /// terrain plainly painted one on it -- and since <see cref="Open"/> is built on this, no
    /// guest could stand there.
    ///
    /// ⚠⚠ DISPROVED BY THE GAME'S OWN SAVESTATE. findings/paths.md records master's live FANTASY
    /// tile map holding kind 13 -- a queue laid onto a path, so a cell a player BUILT ON -- at
    /// (40,24) and (39,25). The projection blocks both (ticket_booths and A_ROAD over one,
    /// A_ROAD and EMBANKMENT over the other) with the game's own no-build bit clear on each. A
    /// rule that forbids what the retail game did is not the retail rule.
    ///
    /// ⚠ NOR IS BUILDABILITY A TEST HERE. Master: the paths outside the gate "are just phantom
    /// paths that the ai uses" -- not buildable, still walked. Gating this on the no-build bit
    /// would take those away too.
    ///
    /// The projection stays where it belongs, in <see cref="CanBuild"/>, which is what decides
    /// whether something NEW may go down.</summary>
    /// <summary>Whether this cell is the park's own bridge -- authored, not built.</summary>
    public bool IsBridge(ParkCell c)
        => Contains(c) && IsBridgeDeck(Materials.Count > Field.Material(c.X, c.Z)
                                      ? Materials[Field.Material(c.X, c.Z)] : null);

    public ParkPathKind Kind(ParkCell c)
    {
        if (!Contains(c)) return ParkPathKind.None;
        int material = Field.Material(c.X, c.Z);
        if (material == 0 || material >= Materials.Count) return ParkPathKind.None;
        // ⭐ The bridge reads as PATH here even though it is not a path sprite: it is ground a
        // guest may stand on and a network a laid path joins. See IsBridgeDeck for why it does
        // not go through Classify.
        if (IsBridgeDeck(Materials[material])) return ParkPathKind.Path;
        return Classify(Materials[material]);
    }
    /// <summary>Anything a visitor can legitimately be standing on: public ground or a queue.</summary>
    public bool Walkable(ParkCell c) => IsEntrance(c) || Kind(c) != ParkPathKind.None;
    public void Occupy(IEnumerable<ParkCell> cells)
    {
        var all = cells.ToArray();
        if (all.Any(c => !CanBuild(c) || Kind(c) != ParkPathKind.None))
            throw new InvalidOperationException("Ride footprint overlaps a path or ineligible terrain");
        _occupied.UnionWith(all);
    }
    /// <summary>Whether a path may go down here. ⭐ THE GAME'S OWN RULE, not the projection:
    /// master's FANTASY savestate has player-built path on two cells the projection blocks, so
    /// the projection cannot be what the game asked before letting them lay it. What is left is
    /// the terrain's no-build bit and whether something already stands there.
    ///
    /// ⚠ <see cref="CanBuild"/> -- still projection-backed -- remains the test for putting a RIDE
    /// down. Nothing has disproved it there, and nothing has confirmed it either.</summary>
    public bool CanLay(ParkCell c) => Contains(c) && Field.Buildable(c.X, c.Z) && !_occupied.Contains(c);

    public void Lay(ParkCell cell, int material)
    {
        if (!CanLay(cell) || material <= 0 || material >= Materials.Count || material > byte.MaxValue
            || Classify(Materials[material]) == ParkPathKind.None)
            throw new InvalidOperationException($"Cannot lay path at {cell} with material {material}");
        Field.Cells[(cell.Z * Field.Width + cell.X) * 2 + 1] = (byte)material;
    }
    public int MaterialIndex(string name)
    {
        int index = Enumerable.Range(0, Materials.Count).FirstOrDefault(i => Materials[i].Equals(name,
            StringComparison.OrdinalIgnoreCase), -1);
        return index > 0 ? index : throw new InvalidDataException($"Missing terrain material {name}");
    }
    public static IEnumerable<ParkCell> Neighbours(ParkCell c)
    {
        yield return c.Offset(0, -1); yield return c.Offset(-1, 0);
        yield return c.Offset(1, 0); yield return c.Offset(0, 1);
    }
    /// <summary>Cardinal BFS, including both endpoints; null means disconnected. Queue restrictions
    /// belong to the ride, so a guest cannot use a different ride's queue as a public shortcut.</summary>
    public IReadOnlyList<ParkCell> Route(ParkCell from, ParkCell to, Func<ParkCell, bool> allowed = null)
    {
        bool Can(ParkCell c) => Walkable(c) && (allowed?.Invoke(c) ?? true);
        if (!Can(from) || !Can(to)) return null;
        var previous = new Dictionary<ParkCell, ParkCell> { [from] = from };
        var pending = new Queue<ParkCell>(); pending.Enqueue(from);
        while (pending.TryDequeue(out var c))
        {
            if (c == to)
            {
                var result = new List<ParkCell> { c };
                while (c != from) { c = previous[c]; result.Add(c); }
                result.Reverse(); return result.AsReadOnly();
            }
            foreach (var next in Neighbours(c))
                if (Can(next) && previous.TryAdd(next, c)) pending.Enqueue(next);
        }
        return null;
    }
    public static Vector3 Centre(ParkCell cell) => new(cell.X + 0.5f, 0, cell.Z + 0.5f);
}
