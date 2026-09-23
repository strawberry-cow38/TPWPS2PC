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
    /// <summary>Same Z-mirrored plot coordinates used by the park renderer.</summary>
    public Vector2 Origin { get; }
    readonly HashSet<ParkCell> _occupied = new();
    readonly HashSet<ParkCell> _scenery = new();
    readonly HashSet<ParkCell> _entrance = new();

    /// <summary>⭐⭐ THE GROUND THAT IS WALKABLE BEFORE ANYONE BUILDS ANYTHING -- the bus stop, the
    /// road up to it and the turnstiles.
    ///
    /// ⚠⚠ THE DISC SHIPS NO PRE-LAID PATH TILES. Measured over jungle's whole grid with the
    /// classifier's own control beside it (the material table names 16 path and 4 queue tiles, so
    /// the classifier had something to hit): 3387 drawn cells, 7 distinct materials, all of them
    /// `jgr_bas*` grass and one `jbr_log1`. ZERO path, ZERO queue. So "the paths that are there
    /// from the start" are not tiles at all -- they are the entrance PREFAB'S MESH, standing on
    /// cells the authored grid marks as drawing no ground.
    ///
    /// ⚠ NAMED, and the names are anchored: findings/gates.md measured `A_ROAD` and
    /// `ticket_booths` as IDENTICAL in all four parks -- the entrance is one prefab translated in
    /// x. So this is a reading of the prefab, not a region drawn by hand per world.
    ///
    /// ⚠⚠ AND NO SKIP TEST. I first took only the cells the terrain draws NO ground on, reasoning
    /// that the plaza is where the grid steps aside for the prefab. It is not: that rule cut the
    /// corridor in two at z=63 and z=65, where the road runs over ordinary drawn terrain, and a
    /// walkway with a hole in it is not a walkway. Measured both ways over jungle --
    ///
    ///   with the skip test     56 cells, the far end reaches neither the near end nor most of itself
    ///   without it           147 cells, ALL 147 reachable from the far end, near end included
    ///
    /// -- and the shape the second one draws is the thing master described: a three-wide corridor
    /// through the turnstiles opening onto a fifteen-wide apron at the bus stop. The skip test was
    /// my own addition and the connectivity is what threw it out.</summary>
    public static readonly string[] EntranceParts = { "A_ROAD", "A_BUS STOP", "ticket_booths" };
    public IEnumerable<ParkCell> Cells => Enumerable.Range(0, Field.Count)
        .Select(i => new ParkCell(i % Field.Width, i / Field.Width));

    public ParkPaths(Model terrain)
    {
        var f = terrain.Field ?? throw new ArgumentException("Terrain has no authored grid");
        Field = new Model.HeightField { Width = f.Width, Height = f.Height, Cells = (byte[])f.Cells.Clone() };
        Materials = terrain.Materials.AsReadOnly();
        var marker = terrain.Meshes.Single(m => string.Equals(m.Name, "heightfield", StringComparison.OrdinalIgnoreCase));
        var transforms = terrain.WorldTransforms();
        var a = Vector3.Transform(marker.BoundsMin, transforms[marker.Offset]);
        var b = Vector3.Transform(marker.BoundsMax, transforms[marker.Offset]);
        var lo = Vector3.Min(a, b); var size = (Vector3.Max(a, b) - lo) / 1.004f;
        lo += size * 0.001f;
        if (Math.Abs(size.X - f.Width) > 0.001f || Math.Abs(size.Z - f.Height) > 0.001f)
            throw new InvalidDataException("Terrain marker and cell grid disagree");
        Origin = new Vector2(lo.X, -(lo.Z + size.Z));
        // Conservative footprint of fixed scenery, through the real mesh parent chains. Even a
        // canopy or authored road blocks new construction here; no guessed terrain heights or
        // collision flags. Triangle/square SAT includes thin walls missed by centre samples.
        foreach (var mesh in terrain.Meshes)
        {
            bool isEntrance = EntranceParts.Any(n => (mesh.Name ?? "").StartsWith(n, StringComparison.OrdinalIgnoreCase));
            var vertices = terrain.Vertices(mesh).Pos.Select(v => Vector3.Transform(v, transforms[mesh.Offset]))
                .Select(v => new Vector2(v.X - Origin.X, -v.Z - Origin.Y)).ToArray();
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
                    if (hit && isEntrance) _entrance.Add(c);
                }
            }
        }
    }
    public bool Contains(ParkCell c) => c.X >= 0 && c.Z >= 0 && c.X < Field.Width && c.Z < Field.Height;

    /// <summary>Is this one of the park's own entrance cells -- bus stop, road, turnstiles?</summary>
    public bool IsEntrance(ParkCell c) => _entrance.Contains(c);
    public IReadOnlyCollection<ParkCell> EntranceCells => _entrance;

    /// <summary>⭐ PUBLIC GROUND: what a visitor with nowhere particular to be may stand on. Laid
    /// path, or the entrance the park came with. ⚠ NOT a queue -- a queue belongs to its ride, and
    /// letting anyone walk it would make every queue a shortcut.</summary>
    public bool Open(ParkCell c) => Contains(c) && (IsEntrance(c) || Kind(c) == ParkPathKind.Path);
    public bool SceneryBlocks(ParkCell c) => _scenery.Contains(c);
    public bool CanBuild(ParkCell c) => Contains(c) && Field.Buildable(c.X, c.Z) && !_occupied.Contains(c) && !_scenery.Contains(c);
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
    public static ParkPathKind Classify(string material)
    {
        var m = Regex.Match(Path.GetFileNameWithoutExtension(material),
            @"^[a-z]pa_(str|cnr|ctr|edg|end|tju|xrd|que|squ|icn)\d+$", RegexOptions.IgnoreCase);
        return !m.Success ? ParkPathKind.None : m.Groups[1].Value.Equals("que", StringComparison.OrdinalIgnoreCase)
            ? ParkPathKind.Queue : ParkPathKind.Path;
    }
    public ParkPathKind Kind(ParkCell c)
    {
        if (!CanBuild(c)) return ParkPathKind.None;
        int material = Field.Material(c.X, c.Z);
        return material == 0 || material >= Materials.Count ? ParkPathKind.None : Classify(Materials[material]);
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
    public void Lay(ParkCell cell, int material)
    {
        if (!CanBuild(cell) || material <= 0 || material >= Materials.Count || material > byte.MaxValue
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
