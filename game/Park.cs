using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>One ride standing on park ground, at its own footprint, with the name a player would
/// actually see. The first thing in this repo that is a game rather than a reader.</summary>
public sealed class Park
{
    /// <summary>A grid cell in model units.
    ///
    /// ⚠⚠ THIS WAS 14.42 AND IT WAS WRONG. That figure came from measuring vertex positions
    /// WITHOUT the per-mesh matrix, which inflates every extent by about a third: `4x4rock` reads
    /// 51.0 raw and **40.2** transformed. tinyclaw measured it independently, got different
    /// extents for the same named models, and named the cause exactly.
    ///
    /// ⭐ Settled by containment rather than by a median, because the ratio's tail is heavy and
    /// one-sided -- coaster models span their whole layout, so a mean-like estimator reads 17.6.
    /// Over all 287 rides with both a footprint and a model, at a cell of **10** the median model
    /// fills **0.991** of its plot; at 9.6 it fills 1.032, i.e. they systematically overflow. Of
    /// the 13 rides named after their own footprint, 22 of 26 extents sit inside a 10-unit cell and
    /// the four that do not overhang by at most 8.7%, which is what a rock does.</summary>
    public const float CellSize = 10.0f;

    public readonly Node3D Root = new() { Name = "Park" };
    Node3D _ground, _ride;

    /// <summary>A ride's footprint as a grid. `*` is an occupied cell and `2` the entrance; rows
    /// are ragged in the file, so width is the longest row and short rows are padded empty.</summary>
    public readonly record struct Footprint(int Width, int Height, bool[,] Cells, int EntryX, int EntryY)
    {
        public static Footprint From(string[] shape)
        {
            var rows = shape.Where(r => r.Trim().Length > 0).ToArray();
            if (rows.Length == 0) return new Footprint(0, 0, new bool[0, 0], -1, -1);
            int w = rows.Max(r => r.TrimEnd().Length), h = rows.Length;
            var cells = new bool[w, h];
            int ex = -1, ey = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < rows[y].TrimEnd().Length; x++)
                {
                    char c = rows[y][x];
                    if (c is ' ' or '\t') continue;
                    cells[x, y] = true;
                    if (c == '2') { ex = x; ey = y; }
                }
            return new Footprint(w, h, cells, ex, ey);
        }

        public int Occupied
        {
            get
            {
                int n = 0;
                for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) if (Cells[x, y]) n++;
                return n;
            }
        }
    }

    /// <summary>Whether the node the ride is parented to is actually drawn.</summary>
    public bool RideVisible => _ride.IsVisibleInTree();

    public Park()
    {
        _ground = new Node3D { Name = "Ground" };
        _ride = new Node3D { Name = "Ride" };
        Root.AddChild(_ground);
        Root.AddChild(_ride);
    }

    static StandardMaterial3D Flat(Color c) =>
        new() { AlbedoColor = c, Roughness = 1.0f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled };

    /// <summary>Lay a grass grid, and the ride's own footprint on top of it so the tiles it claims
    /// are visible against the ones it does not.</summary>
    public void Build(Footprint fp, int pad = 6)
    {
        foreach (var c in _ground.GetChildren()) c.QueueFree();

        int w = Math.Max(fp.Width, 1) + pad * 2, h = Math.Max(fp.Height, 1) + pad * 2;
        var grass = Flat(new Color(0.30f, 0.46f, 0.22f));
        var darker = Flat(new Color(0.26f, 0.41f, 0.19f));
        var claimed = Flat(new Color(0.55f, 0.50f, 0.30f));
        var entry = Flat(new Color(0.85f, 0.65f, 0.20f));

        var tile = new BoxMesh { Size = new Vector3(CellSize, 0.6f, CellSize) };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int fx = x - pad, fy = y - pad;
                bool inFoot = fx >= 0 && fy >= 0 && fx < fp.Width && fy < fp.Height && fp.Cells[fx, fy];
                bool isEntry = inFoot && fx == fp.EntryX && fy == fp.EntryY;
                _ground.AddChild(new MeshInstance3D
                {
                    Mesh = tile,
                    MaterialOverride = isEntry ? entry : inFoot ? claimed : ((x + y) % 2 == 0 ? grass : darker),
                    // The footprint's own tiles sit a hair proud so the edge reads at a glance.
                    Position = new Vector3((fx + 0.5f) * CellSize, inFoot ? 0.15f : 0f, (fy + 0.5f) * CellSize),
                });
            }
    }

    /// <summary>Stand a built model on the footprint. The model is moved so its own XZ centre sits
    /// over the footprint's centre and its base rests on the ground, rather than trusting the file
    /// to be authored about an origin -- measured per model, since they are not.</summary>
    public void Place(Node3D model, Model mesh, Footprint fp)
    {
        foreach (var c in _ride.GetChildren())
            if (c != model) c.QueueFree();
        if (model == null) return;
        // ⚠ The model is already a child of the viewer -- Rebuild adds it. AddChild on a node that
        // has a parent is an ERROR in Godot, not a move: it printed "already has a parent" and left
        // the ride where it was, which renders as the park having no ride in it.
        model.GetParent()?.RemoveChild(model);
        _ride.AddChild(model);

        var (min, max) = DrawnBounds(model);
        var centre = (min + max) * 0.5f;
        var target = new Vector3(fp.Width * CellSize * 0.5f, 0, fp.Height * CellSize * 0.5f);
        model.Position = new Vector3(target.X - centre.X, -min.Y, target.Z - centre.Z);
    }

    /// <summary>The bounds of what is actually ON SCREEN: the union of every built mesh's AABB,
    /// through its own transform inside the model.
    ///
    /// ⭐ This exists because deciding a ride's size from the reader turned out to be a choice
    /// between answers that disagree -- per-mesh matrix alone gives `monkey.mps` 53.6 across, the
    /// parent chain gives 33.4, and the two methods differ per model rather than by a constant. The
    /// geometry the builder produced is not an opinion about which transform is right; it is the
    /// thing the player sees, so it is what a footprint should be measured against.</summary>
    public static (Vector3 Min, Vector3 Max) DrawnBounds(Node3D root)
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        bool any = false;
        void Walk(Node n, Transform3D acc)
        {
            var t = n is Node3D n3 && n != root ? acc * n3.Transform : acc;
            if (n is MeshInstance3D mi && mi.Mesh != null)
            {
                var box = mi.GetAabb();
                for (int i = 0; i < 8; i++)
                {
                    var corner = t * (box.Position + new Vector3(
                        (i & 1) != 0 ? box.Size.X : 0,
                        (i & 2) != 0 ? box.Size.Y : 0,
                        (i & 4) != 0 ? box.Size.Z : 0));
                    any = true;
                    min = new Vector3(Math.Min(min.X, corner.X), Math.Min(min.Y, corner.Y), Math.Min(min.Z, corner.Z));
                    max = new Vector3(Math.Max(max.X, corner.X), Math.Max(max.Y, corner.Y), Math.Max(max.Z, corner.Z));
                }
            }
            foreach (var c in n.GetChildren()) Walk(c, t);
        }
        Walk(root, Transform3D.Identity);
        return any ? (min, max) : (Vector3.Zero, Vector3.Zero);
    }

    /// <summary>A model's bounds, THROUGH ITS PER-MESH MATRIX.
    ///
    /// ⚠⚠ Reading `Vertices(mesh).Pos` straight is wrong and does not look wrong: a mesh sits in
    /// its own space and the matrix places and scales it. Skipping it inflates extents by roughly
    /// a third, which is how CellSize came out as 14.42 instead of 10. `FrameCamera` in the viewer
    /// always did this correctly; this did not, so the camera and the placement disagreed.
    ///
    /// ⚠ Each mesh also DECLARES bounds at +0x70/+0x80, and `Declared` exposes them for comparison.
    /// Measuring is used rather than trusting, because a model standing in the wrong place looks
    /// like the ride was authored that way.</summary>
    public static (Vector3 Min, Vector3 Max) Bounds(Model m)
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        bool any = false;
        var world = m.WorldTransforms();
        foreach (var mesh in m.Meshes)
        {
            if (mesh.BatchCount == 0) continue;
            if (!world.TryGetValue(mesh.Offset, out var w)) continue;
            List<System.Numerics.Vector3> pos;
            try { pos = m.Vertices(mesh).Pos; } catch { continue; }
            foreach (var p in pos)
            {
                var v = System.Numerics.Vector3.Transform(p, w);
                any = true;
                min = new Vector3(Math.Min(min.X, v.X), Math.Min(min.Y, v.Y), Math.Min(min.Z, v.Z));
                max = new Vector3(Math.Max(max.X, v.X), Math.Max(max.Y, v.Y), Math.Max(max.Z, v.Z));
            }
        }
        return any ? (min, max) : (Vector3.Zero, Vector3.Zero);
    }

    /// <summary>The bounds the FILE claims, unioned over its meshes -- for comparison against
    /// <see cref="Bounds"/>. Returns null when no mesh carries any.</summary>
    public static (Vector3 Min, Vector3 Max)? Declared(Model m)
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        bool any = false;
        foreach (var mesh in m.Meshes)
        {
            if (mesh.BatchCount == 0) continue;
            any = true;
            min = new Vector3(Math.Min(min.X, mesh.BoundsMin.X), Math.Min(min.Y, mesh.BoundsMin.Y),
                              Math.Min(min.Z, mesh.BoundsMin.Z));
            max = new Vector3(Math.Max(max.X, mesh.BoundsMax.X), Math.Max(max.Y, mesh.BoundsMax.Y),
                              Math.Max(max.Z, mesh.BoundsMax.Z));
        }
        return any ? (min, max) : null;
    }

    /// <summary>What the panel says about a ride. ⚠ `Display` is the table's name and `Internal`
    /// is `Info.Name`; they differ on 70 of the 273 rides that reach a row, so both are shown and
    /// the table's is the one a player would recognise.</summary>
    public static string Describe(RideDefinition d, string display, Footprint fp)
    {
        var lines = new List<string>
        {
            display ?? d.Name ?? "(unnamed)",
        };
        if (display != null && d.Name != null && display != d.Name)
            lines.Add($"   internally \"{d.Name}\"");
        lines.Add($"id {d.Id?.ToString() ?? "-"}   {fp.Width}x{fp.Height}, {fp.Occupied} cells"
                  + (fp.EntryX >= 0 ? $", entrance at {fp.EntryX},{fp.EntryY}" : ", no entrance"));
        if (d.PricePerUse is int p) lines.Add($"£{p} per use" + (d.CostOfGoods is int g ? $"   goods £{g}" : ""));
        if (d.ExcitementLevel is int e) lines.Add($"excitement {e}");
        var caps = Enumerable.Range(0, 3).Select(t => d.UpgradeCapacity(t)).Where(c => c != null).ToList();
        if (caps.Count > 0) lines.Add("capacity " + string.Join(" / ", caps));
        var costs = Enumerable.Range(0, 3).Select(t => d.UpgradeCost(t)).Where(c => c != null).ToList();
        if (costs.Count > 0) lines.Add("upgrades £" + string.Join(" / £", costs));
        if (d.ResearchGroup is int rg) lines.Add($"research group {rg}");
        return string.Join("\n", lines);
    }
}
