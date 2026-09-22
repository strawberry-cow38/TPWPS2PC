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
/// material bytes. The narrow flat-ground policy excludes every nonzero flag byte: the skip flag
/// is not a complete walkability test, and slope/corner bits have no established walking consumer.</summary>
public sealed class ParkPaths
{
    public Model.HeightField Field { get; }
    public IReadOnlyList<string> Materials { get; }
    /// <summary>Same Z-mirrored plot coordinates used by the park renderer.</summary>
    public Vector2 Origin { get; }
    readonly HashSet<ParkCell> _occupied = new();
    readonly HashSet<ParkCell> _scenery = new();
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
                    if (!_scenery.Contains(c) && Intersects(p, q, r, new Vector2(x + 0.5f, z + 0.5f))) _scenery.Add(c);
                }
            }
        }
    }
    public bool Contains(ParkCell c) => c.X >= 0 && c.Z >= 0 && c.X < Field.Width && c.Z < Field.Height;
    public bool SceneryBlocks(ParkCell c) => _scenery.Contains(c);
    public bool CanBuild(ParkCell c) => Contains(c) && Field.Buildable(c.X, c.Z) && !_occupied.Contains(c) && !_scenery.Contains(c);
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
    public bool Walkable(ParkCell c) => Kind(c) != ParkPathKind.None;
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
