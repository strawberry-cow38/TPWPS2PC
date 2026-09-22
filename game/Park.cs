using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>One ride standing on park ground, at its own footprint, with the name a player would
/// actually see. The first thing in this repo that is a game rather than a reader.</summary>
public sealed class Park
{
    /// <summary>A grid cell in model units. **ONE.**
    ///
    /// ⭐⭐ This was never a measurement to make. Composed through the parent chain, a model named
    /// after its own footprint spans EXACTLY its cell count -- `1x1east` 1.000000, `2x2rck`
    /// 2.000000, `4x4rock` 4.000012, `5x5rck` 5.000009, `bigpalm` 2.000000. Over 562 extents the
    /// median, p25 and p75 are all exactly 1.0000 and 479 are within 1% of it. That is an identity
    /// with float noise on it, not a fit. **One model unit is one cell**, so the cell size is
    /// whatever the park chooses and the only honest choice is 1.
    ///
    /// ⚠ It reached 14.42 and then 10 because both were derived from extents taken through each
    /// mesh's OWN matrix, which runs about ten times the composed value. tinyclaw and I measured
    /// that independently and agreed -- on the same shortcut. Two people agreeing is not
    /// corroboration when they share a method. Found by tinyclaw composing the chain properly.
    ///
    /// The 83 extents that are not ~1.0 (p05 0.73, p95 2.0, max 7.4) are models genuinely
    /// overhanging their plot -- coasters and signage -- which is real rather than noise.</summary>
    public const float CellSize = 1.0f;

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

    Node3D _terrain;

    /// <summary>The park's real ground: `terrain_N.mps` out of the world's own `terrain/` folder.
    ///
    /// ⭐ There is no terrain FORMAT on this disc -- it is a model, which is why an extension census
    /// never found it. Every world WAD carries a `terrain/` directory (JUNGLE 159 files, FANTASY
    /// 117, SPACE 132, HALLOW 83) holding two `.mps`, their `.aps`, and 79 textures of their own.
    /// FANTASY's meshes are named `heightfield`, `gatebase01`, `grass01`..`grass10`; JUNGLE's are
    /// `A_ROAD`, `EMBANKMENT`, `RIVERBED_03`, `ticket_booths`. It is the park.</summary>
    public void SetTerrain(Node3D built)
    {
        if (_terrain != null) { _terrain.QueueFree(); _terrain = null; }
        if (built == null) return;
        built.GetParent()?.RemoveChild(built);
        _terrain = built;
        Root.AddChild(_terrain);
    }

    /// <summary>Hide the synthetic grass once real ground is under the park. Keeping both draws a
    /// checkerboard through the terrain and reads as z-fighting rather than as two floors.</summary>
    public bool ShowGrass { set { if (_ground != null) _ground.Visible = value; } }

    public Park()
    {
        _ground = new Node3D { Name = "Ground" };
        _ride = new Node3D { Name = "Ride" };
        Root.AddChild(_ground);
        Root.AddChild(_ride);
    }

    static StandardMaterial3D Flat(Color c) =>
        new() { AlbedoColor = c, Roughness = 1.0f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled };

    /// <summary>The park's own dimensions in cells. Fixed, because a park is a place rides are
    /// put into -- ground sized to whichever ride was selected is a model viewer with grass.</summary>
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>Which ride id occupies each cell, or 0. This is the park's actual state; the
    /// meshes are just what it looks like.</summary>
    int[,] _occupied = new int[0, 0];

    readonly List<(int Id, string Name, Footprint Fp, int X, int Y)> _placed = new();
    public IReadOnlyList<(int Id, string Name, Footprint Fp, int X, int Y)> Placed => _placed;

    /// <summary>Cells claimed by something. ⭐ The invariant a caller can check: this must equal
    /// the summed `Occupied` of everything in <see cref="Placed"/>. If it does not, a placement
    /// overlapped and was written anyway.</summary>
    public int OccupiedCells
    {
        get
        {
            int n = 0;
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) if (_occupied[x, y] != 0) n++;
            return n;
        }
    }

    /// <summary>Lay the park. Empty grass, no ride in it -- rides arrive through TryPlace.</summary>
    public void Build(int width, int height)
    {
        foreach (var c in _ground.GetChildren()) c.QueueFree();
        foreach (var c in _ride.GetChildren()) c.QueueFree();
        _placed.Clear();
        Width = width; Height = height;
        _occupied = new int[width, height];

        var grass = Flat(new Color(0.30f, 0.46f, 0.22f));
        var darker = Flat(new Color(0.26f, 0.41f, 0.19f));
        var tile = new BoxMesh { Size = new Vector3(CellSize, CellSize * 0.06f, CellSize) };
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                _ground.AddChild(new MeshInstance3D
                {
                    Mesh = tile,
                    MaterialOverride = (x + y) % 2 == 0 ? grass : darker,
                    Position = new Vector3((x + 0.5f) * CellSize, 0f, (y + 0.5f) * CellSize),
                });
    }

    /// <summary>Can this footprint sit at (x, y)? Fails on the park edge and on any cell already
    /// claimed. ⚠ Checked BEFORE anything is written, so a rejected placement leaves no trace --
    /// a half-applied placement would corrupt the occupancy map and show up much later as a ride
    /// that cannot be built somewhere for no visible reason.</summary>
    public bool CanPlace(Footprint fp, int x, int y)
    {
        if (x < 0 || y < 0 || x + fp.Width > Width || y + fp.Height > Height) return false;
        for (int fy = 0; fy < fp.Height; fy++)
            for (int fx = 0; fx < fp.Width; fx++)
                if (fp.Cells[fx, fy] && _occupied[x + fx, y + fy] != 0) return false;
        return true;
    }

    /// <summary>Put a ride in the park at cell (x, y). Returns false and changes nothing if it
    /// does not fit. The model is moved so its own XZ centre sits over the footprint's centre and
    /// its base rests on the ground, measured per model rather than trusting the file to be
    /// authored about an origin.</summary>
    public bool TryPlace(Node3D model, Footprint fp, int id, string name, int x, int y)
    {
        if (!CanPlace(fp, x, y)) return false;

        for (int fy = 0; fy < fp.Height; fy++)
            for (int fx = 0; fx < fp.Width; fx++)
                if (fp.Cells[fx, fy]) _occupied[x + fx, y + fy] = id;
        _placed.Add((id, name, fp, x, y));

        // The claimed tiles, drawn over the grass so the plot reads at a glance.
        var claimed = Flat(new Color(0.55f, 0.50f, 0.30f));
        var entry = Flat(new Color(0.85f, 0.65f, 0.20f));
        var tile = new BoxMesh { Size = new Vector3(CellSize, CellSize * 0.08f, CellSize) };
        for (int fy = 0; fy < fp.Height; fy++)
            for (int fx = 0; fx < fp.Width; fx++)
            {
                if (!fp.Cells[fx, fy]) continue;
                bool isEntry = fx == fp.EntryX && fy == fp.EntryY;
                _ground.AddChild(new MeshInstance3D
                {
                    Mesh = tile,
                    MaterialOverride = isEntry ? entry : claimed,
                    Position = new Vector3((x + fx + 0.5f) * CellSize, CellSize * 0.02f, (y + fy + 0.5f) * CellSize),
                });
            }

        if (model == null) return true;
        // ⚠ The model is already parented elsewhere -- AddChild on a parented node is an ERROR in
        // Godot, not a move, and leaves the ride where it was.
        model.GetParent()?.RemoveChild(model);
        _ride.AddChild(model);
        var (min, max) = DrawnBounds(model);
        var centre = (min + max) * 0.5f;
        model.Position = new Vector3(
            (x + fp.Width * 0.5f) * CellSize - centre.X,
            -min.Y,
            (y + fp.Height * 0.5f) * CellSize - centre.Z);
        return true;
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
            // ⚠⚠ SKIP HIDDEN SURFACES. A ride's parts carry visibility timelines -- Crazy Ape has
            // parts keyed vis[0,2], vis[0,38], vis[0,100] -- and a hidden part still has an AABB and
            // a transform, often parked well away from the model until its moment. Counting those
            // measured the whole animation's envelope rather than the ride, which is why the
            // builder's extent came out 42 x 46 against the reader's 4 x 4: the X and Z ratios were
            // 10.56 and 11.64, and a scale error cannot be non-uniform.
            if (n is MeshInstance3D mi && mi.Mesh != null && mi.Visible)
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
