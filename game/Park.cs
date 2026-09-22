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

    /// <summary>The enclosed empty region inside a terrain model -- the hole the playable tiles
    /// fill. Returns (origin, size) in cells, or a zero size when there is no enclosed area.
    ///
    /// ⚠ NOT a flood fill. The park has an ENTRANCE, so the hole is connected to the outside and a
    /// fill started in the middle escapes through the gap and returns the whole island surround --
    /// which is what a first attempt did, reporting a corner of the bounding box as the hole.
    /// Scanning instead for empty runs that have terrain on BOTH sides keeps the entrance row as
    /// the only one that leaks, rather than all of them.</summary>
    public static (Vector2 Origin, Vector2 Size, float FloorY) FindHole(Node3D terrain, int res = 160)
    {
        var (lo, hi) = DrawnBounds(terrain);
        float w = hi.X - lo.X, h = hi.Z - lo.Z;
        if (w <= 0 || h <= 0) return (Vector2.Zero, Vector2.Zero, 0f);
        var cov = new bool[res, res];
        // ⭐ The top of whatever covers each cell. The floor height has to come from the ground
        // AROUND the hole; the terrain's global minimum is the sea floor, and a floor laid there
        // is buried under the island and reads as "the grid never got built".
        var topY = new float[res, res];
        for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) topY[z, x] = float.MinValue;
        // ⚠⚠ TRIANGLES, NOT BOUNDING BOXES. Coverage taken from each mesh's AABB reported a hole
        // that was not the hole: one mesh whose box spans the gap marks the whole gap covered,
        // while a thin mesh near the shore leaves a box-shaped gap that is solid ground. The map
        // then looked convincing and put the floor a good 80 units off, over the sea. A terrain's
        // occupancy is where its triangles are.
        void Mark(Node n, Transform3D acc)
        {
            var t = n is Node3D n3 && n != terrain ? acc * n3.Transform : acc;
            if (n is MeshInstance3D mi && mi.Mesh != null && mi.Visible)
            {
                for (int surf = 0; surf < mi.Mesh.GetSurfaceCount(); surf++)
                {
                    var arr = mi.Mesh.SurfaceGetArrays(surf);
                    var verts = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    if (verts.Length == 0) continue;
                    var idx = arr[(int)Mesh.ArrayType.Index].AsInt32Array();
                    int tris = (idx.Length > 0 ? idx.Length : verts.Length) / 3;
                    for (int i = 0; i < tris; i++)
                    {
                        Vector3 p0 = t * verts[idx.Length > 0 ? idx[i * 3] : i * 3];
                        Vector3 p1 = t * verts[idx.Length > 0 ? idx[i * 3 + 1] : i * 3 + 1];
                        Vector3 p2 = t * verts[idx.Length > 0 ? idx[i * 3 + 2] : i * 3 + 2];
                        float cx0 = (p0.X - lo.X) / w * (res - 1), cz0 = (p0.Z - lo.Z) / h * (res - 1);
                        float cx1 = (p1.X - lo.X) / w * (res - 1), cz1 = (p1.Z - lo.Z) / h * (res - 1);
                        float cx2 = (p2.X - lo.X) / w * (res - 1), cz2 = (p2.Z - lo.Z) / h * (res - 1);
                        int x0 = Mathf.Clamp((int)Math.Floor(Math.Min(cx0, Math.Min(cx1, cx2))), 0, res - 1);
                        int x1 = Mathf.Clamp((int)Math.Ceiling(Math.Max(cx0, Math.Max(cx1, cx2))), 0, res - 1);
                        int z0 = Mathf.Clamp((int)Math.Floor(Math.Min(cz0, Math.Min(cz1, cz2))), 0, res - 1);
                        int z1 = Mathf.Clamp((int)Math.Ceiling(Math.Max(cz0, Math.Max(cz1, cz2))), 0, res - 1);
                        float top = Math.Max(p0.Y, Math.Max(p1.Y, p2.Y));
                        // ⚠ A triangle smaller than a cell covers no cell centre. Mark its own
                        // cells too, or dense fine geometry reads as empty ground.
                        void Hit(int cz, int cx)
                        {
                            cov[cz, cx] = true;
                            if (top > topY[cz, cx]) topY[cz, cx] = top;
                        }
                        Hit(Mathf.Clamp((int)cz0, 0, res - 1), Mathf.Clamp((int)cx0, 0, res - 1));
                        Hit(Mathf.Clamp((int)cz1, 0, res - 1), Mathf.Clamp((int)cx1, 0, res - 1));
                        Hit(Mathf.Clamp((int)cz2, 0, res - 1), Mathf.Clamp((int)cx2, 0, res - 1));
                        float d = (cz1 - cz2) * (cx0 - cx2) + (cx2 - cx1) * (cz0 - cz2);
                        if (Math.Abs(d) < 1e-9f) continue;
                        for (int z = z0; z <= z1; z++)
                            for (int x = x0; x <= x1; x++)
                            {
                                float a = ((cz1 - cz2) * (x - cx2) + (cx2 - cx1) * (z - cz2)) / d;
                                float b = ((cz2 - cz0) * (x - cx2) + (cx0 - cx2) * (z - cz2)) / d;
                                if (a >= 0 && b >= 0 && a + b <= 1) Hit(z, x);
                            }
                    }
                }
            }
            foreach (var c in n.GetChildren()) Mark(c, t);
        }
        Mark(terrain, Transform3D.Identity);

        if (System.Environment.GetEnvironmentVariable("TPW_HOLE_DEBUG") == "1")
        {
            int marked = 0;
            for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) if (cov[z, x]) marked++;
            GD.Print($"[cov] bounds {lo.X:F1},{lo.Z:F1} .. {hi.X:F1},{hi.Z:F1}  span {w:F1} x {h:F1}  marked {marked}/{res * res} ({100.0 * marked / (res * res):F1}%)");
            for (int z = 0; z < res; z += Math.Max(1, res / 48))
            {
                var sb = new System.Text.StringBuilder("[cov] ");
                for (int x = 0; x < res; x += Math.Max(1, res / 48)) sb.Append(cov[z, x] ? '#' : '.');
                GD.Print(sb.ToString());
            }
        }

        // ⚠⚠ A BAY IS NOT A COURTYARD. The test used to be "empty, with terrain to the left and
        // right on this row and above and below on this column" -- and a bay bitten into the
        // coastline passes it exactly as the park's courtyard does. It picked the bay, and the
        // floor went down half over the sea. The sea is whatever the map border reaches: flood
        // from the edge through empty cells, and what the flood cannot get to is enclosed.
        var outside = new bool[res, res];
        var flood = new Stack<(int Z, int X)>();
        void Seed(int z, int x)
        {
            if (cov[z, x] || outside[z, x]) return;
            outside[z, x] = true; flood.Push((z, x));
        }
        for (int i = 0; i < res; i++) { Seed(0, i); Seed(res - 1, i); Seed(i, 0); Seed(i, res - 1); }
        while (flood.Count > 0)
        {
            var (z, x) = flood.Pop();
            foreach (var (dz, dx) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nz = z + dz, nx = x + dx;
                if (nz < 0 || nx < 0 || nz >= res || nx >= res) continue;
                Seed(nz, nx);
            }
        }
        var inside = new bool[res, res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++) inside[z, x] = !cov[z, x] && !outside[z, x];

        // ⚠ The LARGEST CONNECTED region of those, not their bounding box. A coastline notch is
        // "internal" on its own row too, and one near the edge dragged the origin to the rim -- the
        // park then rendered as a slab hanging off the island rather than sitting in its hole.
        int minX = res, maxX = -1, minZ = res, maxZ = -1, bestArea = 0, bestId = 0, id = 0;
        var comp = new int[res, res];
        var seen = new bool[res, res];
        var stack = new Stack<(int Z, int X)>();
        for (int z0 = 0; z0 < res; z0++)
            for (int x0 = 0; x0 < res; x0++)
            {
                if (!inside[z0, x0] || seen[z0, x0]) continue;
                int aX = res, bX = -1, aZ = res, bZ = -1, area = 0;
                id++;
                seen[z0, x0] = true; stack.Push((z0, x0));
                while (stack.Count > 0)
                {
                    var (z, x) = stack.Pop();
                    comp[z, x] = id;
                    area++;
                    if (x < aX) aX = x;
                    if (x > bX) bX = x;
                    if (z < aZ) aZ = z;
                    if (z > bZ) bZ = z;
                    foreach (var (dz, dx) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nz = z + dz, nx = x + dx;
                        if (nz < 0 || nx < 0 || nz >= res || nx >= res) continue;
                        if (!inside[nz, nx] || seen[nz, nx]) continue;
                        seen[nz, nx] = true; stack.Push((nz, nx));
                    }
                }
                if (area > bestArea) { bestArea = area; bestId = id; minX = aX; maxX = bX; minZ = aZ; maxZ = bZ; }
            }
        if (maxX < minX) return (Vector2.Zero, Vector2.Zero, 0f);
        // ⚠ The MEDIAN of the rim, not its min or max. The rim runs over a beach on one side and
        // a cliff on another, so an extreme picks a floor that is under the ground at one edge or
        // floating above it at the other; the median sits at the height most of the rim is at.
        var rim = new List<float>();
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                if (!cov[z, x] || topY[z, x] == float.MinValue) continue;
                bool touches = false;
                foreach (var (dz, dx) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nz = z + dz, nx = x + dx;
                    if (nz < 0 || nx < 0 || nz >= res || nx >= res) continue;
                    if (comp[nz, nx] == bestId) { touches = true; break; }
                }
                if (touches) rim.Add(topY[z, x]);
            }
        rim.Sort();
        float floorY = rim.Count > 0 ? rim[rim.Count / 2] : lo.Y;

        float ux = w / (res - 1), uz = h / (res - 1);
        return (new Vector2(lo.X + minX * ux, lo.Z + minZ * uz),
                new Vector2((maxX - minX + 1) * ux, (maxZ - minZ + 1) * uz), floorY);
    }

    /// <summary>The node holding the playable floor tiles, so callers can measure where the
    /// floor actually landed rather than trust the origin they asked for.</summary>
    public Node3D GroundRoot => _ground;

    /// <summary>Where the playable grid sits inside the world, in cells. Set from the terrain's
    /// own geometry before Build.</summary>
    public Vector2 Origin = Vector2.Zero;

    /// <summary>The height the playable floor sits at, from the terrain's own base.</summary>
    public float BaseY;

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
                    Position = new Vector3(Origin.X + (x + 0.5f) * CellSize, BaseY, Origin.Y + (y + 0.5f) * CellSize),
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
                    Position = new Vector3(Origin.X + (x + fx + 0.5f) * CellSize, BaseY + CellSize * 0.02f,
                                           Origin.Y + (y + fy + 0.5f) * CellSize),
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
            Origin.X + (x + fp.Width * 0.5f) * CellSize - centre.X,
            BaseY - min.Y,
            Origin.Y + (y + fp.Height * 0.5f) * CellSize - centre.Z);
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
