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

    /// <summary>The plot's own floor, WITHOUT the terrain model parented beside it. Hiding the
    /// whole Root hides the terrain too, which answers nothing.</summary>
    public Node3D Floor => _ground;

    public ParkPaths Paths { get; private set; }
    public void SetPaths(ParkPaths paths) { Paths = paths; Field = paths.Field; }
    Node3D _ground, _ride;

    /// <summary>A ride's footprint as a grid. `*` is an occupied cell and `2` the entrance; rows
    /// are ragged in the file, so width is the longest row and short rows are padded empty.</summary>
    /// <summary>A thing's footprint, and the two doors in it.
    ///
    /// ⭐⭐ THE SHAPE STRING IS A LEGEND, read off every .sam on the disc: `*` an ordinary cell,
    /// `2` the ENTRANCE (171 of them, one in nearly everything placeable), and `N` `S` `E` `W` the
    /// EXIT -- a cell marked with the way it faces (`b_drip` is `*2N*`, `acorn` has `**S**` at one
    /// end and `**2**` at the other). `<` and `>` come in pairs on track rides and `+` and `.`
    /// appear in a handful; none of those are doors and none are claimed to be.</summary>
    public readonly record struct Footprint(int Width, int Height, bool[,] Cells, int EntryX, int EntryY,
                                            int ExitX = -1, int ExitY = -1)
    {
        public static Footprint From(string[] shape)
        {
            var rows = shape.Where(r => r.Trim().Length > 0).ToArray();
            if (rows.Length == 0) return new Footprint(0, 0, new bool[0, 0], -1, -1);
            int w = rows.Max(r => r.TrimEnd().Length), h = rows.Length;
            var cells = new bool[w, h];
            int ex = -1, ey = -1, xx = -1, xy = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < rows[y].TrimEnd().Length; x++)
                {
                    char c = rows[y][x];
                    if (c is ' ' or '\t') continue;
                    cells[x, y] = true;
                    if (c == '2') { ex = x; ey = y; }
                    else if (c is 'N' or 'S' or 'E' or 'W') { xx = x; xy = y; }
                }
            return new Footprint(w, h, cells, ex, ey, xx, xy);
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

    static ShaderMaterial Flat(Color c) => Ps2Materials.Ground(null, c);

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
    /// <summary>The park plot, as the disc states it.
    ///
    /// ⭐⭐ THE PLOT IS AUTHORED, NOT INFERRED. Every M3D2 mesh record carries its local AABB at
    /// +0x70/+0x80 (verified against vertex-computed bounds for all 144 meshes in jungle's terrain
    /// that have geometry). The node named `heightfield` has NO geometry -- zero verts, zero faces
    /// -- and still carries one, and that AABB is the park:
    ///
    ///   JUNGLE 64 x 76   FANTASY 80 x 60   HALLOW 96 x 52   SPACE 96 x 54   (terrain_1)
    ///
    /// ⚠ Y IS NOT AN ELEVATION ENVELOPE. Its two floats are bit-identical in all eight terrain
    /// files across all four worlds (`bcac6046` / `41a07d03`) -- a hardcoded two-cell ceiling
    /// carrying no per-world information. Nothing about how tall jungle's volcano is survives in
    /// it, and I briefly reported otherwise. (tinyclaw)
    ///
    /// ⚠ terrain_1 and terrain_2 are DIFFERENT PARKS everywhere except JUNGLE: FANTASY 80x60 vs
    /// 76x62, HALLOW 96x52 vs 88x56, SPACE 96x54 vs 72x62. Only jungle's two agree, so "the plot
    /// size" is a property of the terrain file, never of the world.
    ///
    /// Clean integers at one cell per unit, and jungle's matches its hoarding footprint to a
    /// decimal. Its node transform places it too: SPACE's carries a translation of (0,0,-100).
    ///
    /// ⚠ What replaced: a flood-fill over triangle coverage that I invented. It put jungle's plot
    /// at Z 12.7..75.0 where the disc says Z 0..76.23 -- a 12-unit strip missing down one whole
    /// edge, which no render would have shown because the strip is empty either way.</summary>
    /// <summary>The plot's own space and how it reaches the world.
    ///
    /// ⭐⭐ RETURNS A TRANSFORM, NOT A BOX, because a box loses the orientation. Each park's
    /// heightfield node carries its own matrix and they are NOT all the same:
    ///
    ///   JUNGLE t1  X(1,0,0)  Z(0,0,1)   T(0,0,0)
    ///   SPACE  t1  X(1,0,0)  Z(0,0,1)   T(0,0,-100)
    ///   FANTASY t1 X(1,0,0)  Z(0,0,1)   T(0,0,-80)
    ///   HALLOW t1  X(-1,0,0) Z(0,0,-1)  T(0,0,-100)   <- a 180-degree yaw
    ///
    /// Computing a cell's world position by hand -- "origin plus x, and Z counts backwards" --
    /// silently assumes every park is axis-aligned the same way. Two are not, so FANTASY landed 34
    /// units out and HALLOW 156. Master asked whether the terrain was 180 out hours before this
    /// was found; they were right, it is just one park rather than all of them.
    ///
    /// So the cell goes through the node's matrix exactly as a mesh vertex does, and rotation,
    /// translation and mirroring all come out right with no convention left to get backwards.</summary>
    public readonly record struct Plot(Transform3D ToWorld, Vector3 LocalMin, Vector3 LocalSize);

    public static Plot? AuthoredPlot(Model model, Transform3D root)
    {
        var hf = model.Meshes.FirstOrDefault(m =>
            m.Name != null && m.Name.Equals("heightfield", StringComparison.OrdinalIgnoreCase));
        if (hf == null) return null;
        var world = model.WorldTransforms();
        if (!world.TryGetValue(hf.Offset, out var w)) return null;

        // ⭐ Divide the exporter pad out IN LOCAL SPACE, before any transform: min is -0.001x the
        // true extent and max is 1.003x, so (max-min)/1.004 is the real size and lands on an exact
        // integer on every axis of every park. Doing it after a transform that can negate an axis
        // would fold the sign into the pad.
        var bmin = new Vector3(hf.BoundsMin.X, hf.BoundsMin.Y, hf.BoundsMin.Z);
        var bmax = new Vector3(hf.BoundsMax.X, hf.BoundsMax.Y, hf.BoundsMax.Z);
        var size = (bmax - bmin) / 1.004f;
        var min = bmin + size * 0.001f;

        // ⚠⚠ THE NODE'S OWN TRANSFORM IS NOT APPLIED. The grid lives in the MODEL'S BASE
        // coordinates, not in the heightfield node's local space -- proved by cross-correlating
        // each park's skip map against its mesh coverage: with no node transform the best
        // alignment is (0,0) for FANTASY, HALLOW and SPACE and within one cell for JUNGLE.
        //
        // Applying it is what broke three parks out of four, and JUNGLE hid it because its node is
        // the identity: FANTASY carries T(0,0,-80) so its grid landed 8 cells out, SPACE T(0,0,-100)
        // for 10 cells, and HALLOW T(0,0,-100) plus a 180-degree YAW, so it was offset AND rotated.
        // Master described exactly that from the picture -- "all parks are just a wrong offset
        // except jungle, and halloween is offset and 180 rotate" -- before this was measured.
        //
        // What IS needed is the model's uniform bind scale (0.1), which the node's world matrix
        // carries in the length of its axes. Take the scale, drop the rotation and translation.
        float bind = new Vector3(w.M11, w.M12, w.M13).Length();
        if (bind <= 0f) bind = 0.1f;
        var scaled = new Transform3D(new Basis(Vector3.Right * bind, Vector3.Up * bind,
                                               Vector3.Back * bind), Vector3.Zero);
        return new Plot(root * scaled, min, size);
    }

    public static (Vector2 Origin, Vector2 Size, float FloorY, bool[,] Cells) FindHole(
        Node3D terrain, int res = 160, Vector2? plotOrigin = null, Vector2? plotSize = null)
    {
        var (lo, hi) = DrawnBounds(terrain, inParent: true);
        float w = hi.X - lo.X, h = hi.Z - lo.Z;
        if (w <= 0 || h <= 0) return (Vector2.Zero, Vector2.Zero, 0f, null);
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
        var edges = new Dictionary<(long A, long B), (int Count, float Y)>();
        var edgeAt = new Dictionary<(long A, long B), Vector3>();
        static long Key(Vector3 v) =>
            ((long)Mathf.RoundToInt(v.X * 100) * 73856093)
          ^ ((long)Mathf.RoundToInt(v.Y * 100) * 19349663)
          ^ ((long)Mathf.RoundToInt(v.Z * 100) * 83492791);
        void Edge(Vector3 a, Vector3 b)
        {
            long ka = Key(a), kb = Key(b);
            var k = ka <= kb ? (ka, kb) : (kb, ka);
            if (edges.TryGetValue(k, out var e)) edges[k] = (e.Count + 1, e.Y);
            else { edges[k] = (1, (a.Y + b.Y) * 0.5f); edgeAt[k] = (a + b) * 0.5f; }
        }

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
                        // ⭐ Count every edge. An edge used by ONE triangle is an OPEN edge -- the
                        // seam the terrain was authored with, where the engine hangs the park's own
                        // tiles. That seam is where the floor height comes from; see below.
                        Edge(p0, p1); Edge(p1, p2); Edge(p2, p0);

                        // ⚠⚠ ONLY NEAR-HORIZONTAL SURFACES COUNT AS GROUND. Testing "does any
                        // triangle overlap this cell in XZ" threw away the elevated terrain and the
                        // palm trees with one swing: a canopy hanging over the grass answered yes,
                        // so the floor lost a cell under every tree, and EMBANKMENT / VOLCANO /
                        // newcliff12 -- which ARE the elevated terrain, not props -- were binned
                        // the same way. With this filter the contributors are exactly EMBANKMENT,
                        // CLIFFS, newcliff12, A_ROAD and VOLCANO, and the palms drop out.
                        var nrm = (p1 - p0).Cross(p2 - p0);
                        if (nrm.Length() <= 0 || Math.Abs(nrm.Y) / nrm.Length() < 0.7f) continue;
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
        Mark(terrain, terrain.Transform);

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
        if (maxX < minX) return (Vector2.Zero, Vector2.Zero, 0f, null);
        // ⭐⭐ THE FLOOR HEIGHT COMES OFF THE MESH'S OPEN SEAM, NOT OUT OF A STATISTIC.
        // This was the median of the rim cells' TOP Y, which is a heuristic I invented, and it sat
        // the park a full unit high: a rim cell usually holds a bank or a wall going up, so the top
        // of it is not the ground. The terrain is genuinely OPEN around the plot -- 1,562 of
        // jungle's 3,700 unshared edges border it -- because that seam is where the engine hangs
        // the park's own tiles. 1,652 of those 3,124 boundary vertices sit at exactly 0.0, and
        // EMBANKMENT's base is -0.004: the datum is authored, not inferred.
        // ⚠ Only edges ADJACENT TO THE PLOT, and the MODE of them, not the median. Taking the
        // median over the plot's bounding box swept in seam edges from river banks and cliffs that
        // happen to fall inside the box, and gave 0.60 / 0.20 / 0.25 in the other three worlds
        // against jungle's clean 0.00 -- a wrong answer that still looked like a measurement.
        // Dilate the plot by one cell and take the most common height: 0.00 wins in every world,
        // by 82% of 817 edges in JUNGLE, 50% of 814 in FANTASY, 57% of 505 in HALLOW, 49% of 487
        // in SPACE, each with the runner-up far behind. The park datum is y = 0 and it is a
        // constant the artists authored, not a per-world measurement.
        var near = new bool[res, res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                if (comp[z, x] != bestId) continue;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nz = z + dz, nx = x + dx;
                        if (nz >= 0 && nx >= 0 && nz < res && nx < res) near[nz, nx] = true;
                    }
            }
        var buckets = new Dictionary<int, (int Count, float Sum)>();
        int seamCount = 0;
        foreach (var (k, e) in edges)
        {
            if (e.Count != 1 || !edgeAt.TryGetValue(k, out var mid)) continue;
            int gx = Mathf.Clamp((int)((mid.X - lo.X) / w * (res - 1)), 0, res - 1);
            int gz = Mathf.Clamp((int)((mid.Z - lo.Z) / h * (res - 1)), 0, res - 1);
            if (!near[gz, gx]) continue;
            seamCount++;
            int b = Mathf.RoundToInt(e.Y * 10f);
            var cur = buckets.TryGetValue(b, out var v) ? v : (0, 0f);
            buckets[b] = (cur.Item1 + 1, cur.Item2 + e.Y);
        }
        float floorY = lo.Y;
        int bestCount = 0;
        foreach (var (_, v) in buckets)
            if (v.Count > bestCount) { bestCount = v.Count; floorY = v.Sum / v.Count; }
        GD.Print($"[floorY] {seamCount} seam edges border the plot -> y={floorY:F3} "
               + $"({bestCount} of them, {100.0 * bestCount / Math.Max(1, seamCount):F0}%)");

        float ux = w / (res - 1), uz = h / (res - 1);
        // ⭐ The AUTHORED extent wins when the disc states one. The flood-fill bbox below is only
        // a fallback for a terrain with no `heightfield` node -- it put jungle's plot at
        // Z 12.7..75.0 where the disc says 0..76.23.
        var origin = plotOrigin ?? new Vector2(lo.X + minX * ux, lo.Z + minZ * uz);
        var size = plotSize ?? new Vector2((maxX - minX + 1) * ux, (maxZ - minZ + 1) * uz);

        // ⚠⚠ THE PLOT IS NOT A RECTANGLE, so its bounding box is not the plot. The hole has the
        // riverbed running out of one corner, and building a full rectangle over the box put two
        // of its four corners in open sea -- while every printed number still agreed, because a
        // bounding box of an L is a perfectly good bounding box. Hand back the CELLS.
        int cw = Math.Max(1, Mathf.RoundToInt(size.X / CellSize)), ch = Math.Max(1, Mathf.RoundToInt(size.Y / CellSize));
        var cells = new bool[cw, ch];
        for (int cy = 0; cy < ch; cy++)
            for (int cx = 0; cx < cw; cx++)
            {
                float wx = origin.X + (cx + 0.5f) * CellSize, wz = origin.Y + (cy + 0.5f) * CellSize;
                int gx = Mathf.Clamp((int)Math.Round((wx - lo.X) / w * (res - 1)), 0, res - 1);
                int gz = Mathf.Clamp((int)Math.Round((wz - lo.Z) / h * (res - 1)), 0, res - 1);
                // ⚠ With the extent authored, a cell is ground unless a near-horizontal terrain
                // surface already occupies it -- the elevated terrain draws itself and does not
                // need a flat tile laid through it. The old test also demanded the cell belong to
                // the flood-filled component, which is meaningless once the boundary is given.
                cells[cx, cy] = plotOrigin != null ? !cov[gz, gx] : comp[gz, gx] == bestId;
            }
        return (origin, size, floorY, cells);
    }

    /// <summary>The node holding the playable floor tiles, so callers can measure where the
    /// floor actually landed rather than trust the origin they asked for.</summary>
    public Node3D GroundRoot => _ground;

    /// <summary>The plot's own space and how it reaches the world. Set before Build.</summary>
    public Plot? PlotSpace { get; set; }

    /// <summary>A cell's centre in world space. ⭐ Goes through the plot's own matrix, so a park
    /// that is rotated (HALLOW t1 is a 180-degree yaw) or translated (FANTASY -80, SPACE -100)
    /// lands correctly with no per-axis reasoning here to get backwards.</summary>
    public Vector3 CellCentre(int x, int y)
    {
        if (PlotSpace is not { } p || Width <= 0 || Height <= 0)
            return new Vector3(Origin.X + (x + 0.5f) * CellSize, BaseY, Origin.Y + (y + 0.5f) * CellSize);
        var local = p.LocalMin + new Vector3((x + 0.5f) / Width * p.LocalSize.X, 0f,
                                             (y + 0.5f) / Height * p.LocalSize.Z);
        var v = p.ToWorld * local;
        return new Vector3(v.X, BaseY, v.Z);
    }

    /// <summary>The world position of a grid CORNER -- the point where cells (x-1,y-1), (x,y-1),
    /// (x-1,y) and (x,y) meet.
    ///
    /// ⭐⭐ THE FLOOR IS BUILT FROM THESE, NOT FROM A CENTRE PLUS A HALF. A quad whose corners are
    /// `centre(x) + half` and whose neighbour's are `centre(x+1) - half` asks for the same point by
    /// two different sums, and two different sums of floats are not the same float. The miss is
    /// about one part in ten million -- invisible up close, and at a shallow angle far away it is
    /// occasionally wide enough to swallow a pixel centre, so the background shows through the
    /// ground in single bright dots along a cell row. Master saw them as seams to the sky.
    ///
    /// Asking for a CORNER by its own index gives both quads the identical expression and
    /// therefore the identical float, and the crack cannot exist.</summary>
    public Vector3 CellCorner(int x, int y)
    {
        if (PlotSpace is not { } p || Width <= 0 || Height <= 0)
            return new Vector3(Origin.X + x * CellSize, BaseY, Origin.Y + y * CellSize);
        var local = p.LocalMin + new Vector3((float)x / Width * p.LocalSize.X, 0f,
                                             (float)y / Height * p.LocalSize.Z);
        var v = p.ToWorld * local;
        return new Vector3(v.X, BaseY, v.Z);
    }

    /// <summary>Where the playable grid sits inside the world, in cells. Set from the terrain's
    /// own geometry before Build.</summary>
    public Vector2 Origin = Vector2.Zero;

    /// <summary>The height the playable floor sits at, from the terrain's own base.</summary>
    public float BaseY;

    /// <summary>Which cells are actually ground. Null means the whole rectangle, which is what a
    /// park with no terrain under it gets.</summary>
    public bool[,] Playable { get; private set; }

    /// <summary>Is this cell part of the plot at all?
    ///
    /// ⭐ When the terrain file carries a field, the ANSWER IS AUTHORED: `byte0` bit 0 is the
    /// engine's own skip flag. That replaces the mesh-coverage mask, which was me inferring the
    /// footprint from where terrain geometry happened to be and dropping ~290 cells the game
    /// draws.</summary>
    /// <summary>Whether nothing is standing on a cell. ⚠ Bounds-checked here rather than by the
    /// caller: a placement ghost is routinely dragged off the edge of the plot, and that has to
    /// read as "no" instead of throwing.</summary>
    public bool Vacant(int x, int y)
        => x >= 0 && y >= 0 && x < Width && y < Height && _occupied[x, y] == 0;

    public bool IsPlayable(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
        if (Field != null && x < Field.Width && y < Field.Height) return Field.Drawn(x, y);
        return Playable == null || Playable[x, y];
    }

    /// <summary>Lay the park. Empty grass, no ride in it -- rides arrive through TryPlace.</summary>
    public void Build(int width, int height, bool[,] playable = null)
    {
        foreach (var c in _ground.GetChildren()) c.QueueFree();
        foreach (var c in _ride.GetChildren()) c.QueueFree();
        _placed.Clear();
        Width = width; Height = height;
        Playable = playable != null && playable.GetLength(0) == width && playable.GetLength(1) == height
            ? playable : null;
        _occupied = new int[width, height];

        // ⭐ ONE SURFACE PER GROUND MATERIAL. The disc says which ground variant goes on each
        // cell -- byte1 indexes the terrain model's OWN material table -- and laying a single
        // texture over the whole plot throws all of it away. In jungle the values hit
        // `jgr_bas1..6` and in space `sfl_bas1..6`: six of the top seven in each, at DIFFERENT
        // indices per world (24/55-59 against 2/19/43-46), so it is a per-file index and not a
        // fixed enum that could have matched by luck.
        //
        // ⚠ Index 0 is a SENTINEL, not a material. It resolves to `gte_wal1` in jungle and
        // `sgr_tnk2` in space -- a wall and a tank, on about a quarter of all cells. Those get the
        // default ground rather than a wall texture laid across the park.
        var surfaces = new Dictionary<int, SurfaceTool>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (!IsPlayable(x, y)) continue;
                int mat = Field != null && x < Field.Width && y < Field.Height ? Field.Material(x, y) : 0;
                if (!surfaces.TryGetValue(mat, out var st))
                {
                    st = new SurfaceTool();
                    st.Begin(Mesh.PrimitiveType.Triangles);
                    surfaces[mat] = st;
                }
                // ⚠⚠ ROW ORDER IS REVERSED IN Z. The scene root mirrors Z (Scale 1,1,-1), so a
                // model Z of 0 -- the park's FRONT, where the bus stop, road and ticket booths sit
                // -- lands at the HIGH end of the plot's world Z, not the low end. Laying row 0 at
                // Origin.Y put the whole grid back to front, which is why tiles appeared over the
                // river and out past the gates.
                //
                // Landmark check: jungle's ticket booths are at model Z 14.9..16.1, so world
                // Z ~= -15.5, and the plot runs world Z -76.2..0. Row 15 therefore belongs at
                // -15.5. Origin.Y + (y + 0.5) gave -60.7; Origin.Y + (H - y - 0.5) gives -15.5.
                //
                // ⭐ Not a mirror and not a rotation: tinyclaw scored the grid against the water
                // under identity, 180, mirror-X and mirror-Z, and identity won outright (space
                // 100%). The data was never wrong -- only my placement of it.
                // ⭐ From the CORNERS, so a cell and its neighbour agree to the last bit.
                //
                // ⚠⚠ ORDERED BY WORLD POSITION, NOT BY GRID INDEX. The plot's transform MIRRORS,
                // so walking the corners (x,y) (x+1,y) (x+1,y+1) (x,y+1) comes out wound the other
                // way in world space and the whole floor is culled -- I shipped exactly that and
                // the park vanished. Taking the min and the max of the four keeps the winding the
                // quad has always had, and min and max of shared corners are still shared, so the
                // crack the corners were for stays shut.
                float cy = CellY(x, y);
                var c00 = CellCorner(x, y);
                var c10 = CellCorner(x + 1, y);
                var c11 = CellCorner(x + 1, y + 1);
                var c01 = CellCorner(x, y + 1);
                float x0 = Mathf.Min(Mathf.Min(c00.X, c10.X), Mathf.Min(c11.X, c01.X));
                float x1 = Mathf.Max(Mathf.Max(c00.X, c10.X), Mathf.Max(c11.X, c01.X));
                float z0 = Mathf.Min(Mathf.Min(c00.Z, c10.Z), Mathf.Min(c11.Z, c01.Z));
                float z1 = Mathf.Max(Mathf.Max(c00.Z, c10.Z), Mathf.Max(c11.Z, c01.Z));
                var a = new Vector3(x0, cy, z0);
                var b = new Vector3(x1, cy, z0);
                var c = new Vector3(x1, cy, z1);
                var dd = new Vector3(x0, cy, z1);
                // ⭐ A GROUND TILE CAN BE TURNED. The game's path pieces name a tile AND a
                // number of quarter turns -- one corner tile serves all four right angles -- so the
                // turn has to reach the quad. It goes in the UVs rather than in the material,
                // because the plot is grouped by material: a per-cell rotation on the material
                // would mean a surface per cell.
                int turn = (TurnsForCell?.Invoke(x, y) ?? 0) & 3;
                void V(Vector3 v, int corner)
                {
                    st.SetUV(UvCorners[(corner + turn) & 3]);
                    st.SetNormal(Vector3.Up);
                    st.AddVertex(v);
                }
                V(a, 0); V(b, 1); V(c, 2);
                V(a, 0); V(c, 2); V(dd, 3);

                // ⚠⚠ VERTICAL FACES ARE AN INVENTION OF MINE AND ARE OFF BY DEFAULT.
                //
                // This closes the floor's edge against the model where the two are at different
                // heights. Nothing on the disc asks for it -- I added it so a step would not leave
                // an open seam. Master, who has played the game, says terrain walls and slopes do
                // not appear in it anywhere, so it does not run unless asked for.
                //
                // A face that looks plausible is worse than a visible gap: the gap makes someone
                // ask what belongs there, the face quietly answers it wrong. The real transition
                // is presumably an authored TILE SHAPE -- the 0x3C nibble tested in 0x222fe8 --
                // and inventing geometry in its place hides the question.
                //
                // ⚠ The real terrain mesh DOES have genuine vertical geometry of its own
                // (EMBANKMENT spans Y -0.04..18.28, newcliff12 -8.63..19.07, space's CLIFFS
                // similar). Those are authored and are not this. Telling them apart: the real ones
                // are tall and irregular, these are exactly one step high and perfectly square.
                if (!WallsEnabled || TerrainTop == null) continue;
                // ⚠ Z PAIRS ARE SWAPPED relative to the naive reading, because the row order is
                // reversed: cz = Origin.Y + (H - y - 0.5), so INCREASING y DECREASES world Z.
                // Neighbour (x, y+1) is therefore the cz-half edge (a,b), not (c,dd). Getting this
                // backwards put every step's wall on the side that did not need one and left the
                // side that did open -- which is why raised squares rendered as holes.
                foreach (var (dx, dy, e0, e1) in new[] { (0, 1, a, b), (1, 0, b, c), (0, -1, c, dd), (-1, 0, dd, a) })
                {
                    int nx2 = x + dx, ny2 = y + dy;
                    if (nx2 < 0 || ny2 < 0 || nx2 >= width || ny2 >= height) continue;
                    if (IsPlayable(nx2, ny2)) continue;
                    if (nx2 >= TerrainTop.GetLength(0) || ny2 >= TerrainTop.GetLength(1)) continue;
                    var nh = TerrainTop[nx2, ny2];
                    if (nh == null || Math.Abs(nh.Value - cy) <= 0.15f) continue;
                    var q0 = new Vector3(e0.X, nh.Value, e0.Z);
                    var q1 = new Vector3(e1.X, nh.Value, e1.Z);
                    var n = new Vector3(dx, 0, dy);
                    void S(Vector3 v, float u, float w2) { st.SetUV(new Vector2(u, w2)); st.SetNormal(n); st.AddVertex(v); }
                    if (nh.Value > cy) { S(e0, 0, 0); S(q0, 0, 1); S(q1, 1, 1); S(e0, 0, 0); S(q1, 1, 1); S(e1, 1, 0); }
                    else               { S(e0, 0, 0); S(q1, 1, 1); S(q0, 0, 1); S(e0, 0, 0); S(e1, 1, 0); S(q1, 1, 1); }
                }
            }
        var fallback = GroundMaterial ?? Flat(new Color(0.30f, 0.46f, 0.22f));
        MaterialCount = 0;
        foreach (var kv in surfaces)
        {
            var mesh = kv.Value.Commit();
            if (mesh == null || mesh.GetSurfaceCount() == 0) continue;
            MaterialCount++;
            _ground.AddChild(new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = MaterialForCell?.Invoke(kv.Key) ?? fallback,
            });
        }
    }

    /// <summary>The plot's ground material -- the disc's own tile texture when one resolved.</summary>
    public Material GroundMaterial { get; set; }

    /// <summary>The terrain model's own surface height per plot cell, or null where the model has
    /// nothing. ⭐ Used to CLOSE THE SEAM: the plot floor is flat (byte0 is undecoded), the model
    /// around it is not, and 116 of the 305 cells where they meet sit at a different height -- 108
    /// stepping up, 8 dropping. That discontinuity is the gap seen around the volcano and along
    /// the river. The edge is closed against the MODEL'S OWN height, which is read, not invented;
    /// it is not a guess at what the tile heights are.</summary>
    public float?[,] TerrainTop { get; set; }

    /// <summary>Resolves a cell's `byte1` to a material through the terrain model's material
    /// table. Returning null falls back to <see cref="GroundMaterial"/>.</summary>
    public Func<int, Material> MaterialForCell { get; set; }

    /// <summary>Quarter turns for a cell's ground tile, clockwise. Null leaves every tile at the
    /// texture's own orientation, which is what the authored ground wants.</summary>
    public Func<int, int, int> TurnsForCell { get; set; }

    /// <summary>The quad's UV corners, in the order the floor emits its vertices.
    ///
    /// ⚠ INSET BY HALF A TEXEL of a 64x64 tile, not 0 and 1. A sample taken at exactly the edge of
    /// a tile sits on the boundary between its outermost texel and whatever the sampler decides is
    /// next, and a linear filter then mixes the two -- the 1-2 pixel line where two path tiles
    /// meet. Clamping the sampler (see Ps2Materials.Ground) stops it wrapping; this stops it
    /// landing on the boundary at all.</summary>
    const float UvInset = 0.5f / 64f;
    static readonly Vector2[] UvCorners =
    {
        new(UvInset, UvInset), new(1 - UvInset, UvInset),
        new(1 - UvInset, 1 - UvInset), new(UvInset, 1 - UvInset),
    };

    /// <summary>How many distinct ground materials the plot was laid with.</summary>
    public int MaterialCount { get; private set; }

    /// <summary>The authored terrain grid, when the terrain file carries one. ⭐ The park floor is
    /// LOADED from this, not approximated from the mesh -- the runtime field is a verbatim copy of
    /// it, so reading the disc and reading RAM give the same thing.</summary>
    public Model.HeightField Field { get; set; }

    /// <summary>World Y of a cell. ⚠⚠ FLAT, DELIBERATELY.
    ///
    /// This used to extrude a flat-topped box per cell from `byte0 &amp; 0x03`, and that is WRONG BY
    /// CONSTRUCTION -- master said so on sight and the engine agrees. The builder at 0x222230 does
    /// not read a height: it takes the cell as a u16 and splits it THREE ways, each scaled by 4 and
    /// used as an offset into a different float array (`lwc1`) -- bits 0-3, bits 4-7, and bits 8-15
    /// (what I called byte1). The shape logic at 0x2233b0 skips a cell when `andi 0x3c` is zero,
    /// and `andi 0x20` SWAPS TWO BYTES on the stack: it selects and ROTATES a tile shape. So a cell
    /// names a tile whose CORNER heights are looked up, which draws as sloped and stepped tiles. A
    /// cuboid per cell can never be that shape, whatever mask feeds it.
    ///
    /// ⚠ The three float arrays are STACK buffers built earlier in the same function (0x221e3c,
    /// sp+0x60 / sp+0x30 / sp+0x10), not static tables, so the corner values are assembled at draw
    /// time and cannot be dumped off the disc.
    ///
    /// Until those are decoded the plot stays flat. A flat plot is visibly unfinished; fabricated
    /// blocks look finished and are not.</summary>
    /// <summary>Raised squares. ⭐ THE MARKER IS `byte0 & 0x40`, and it was master's memory of
    /// playing the game that found it -- they said the real jungle park has "maybe 16-20 raised
    /// tiles" against the 283 we were drawing, which is the kind of error no amount of internal
    /// consistency was ever going to surface.
    ///
    /// `0x40` gives 20 in JUNGLE t1 and t2, 12 in FANTASY t1, 12 in HALLOW t1, 158 in SPACE t1 --
    /// sane numbers for a theme park, where the field we had been using (`byte0 & 0x03 == 2`)
    /// gives 283 and 1,473.
    ///
    /// ⭐ And the POSITIONS match what master recalled independently, hours earlier: "the volcano
    /// is meant to have a few raised tiles, and the back left edge a few too". The 20 cells are a
    /// patch of 16 at x 8-15, z 74-75 -- back left, since Z=0 is the park's front -- and four
    /// single cells on the volcano. Two places, right counts, from outside our own file-reading.
    ///
    /// ⚠ Drawn cells only: `0x41` and `0x42` carry 0x40 with the skip bit set and are not ground.
    /// ⚠ The step height is still one unit and still a guess; it is not in the files.</summary>
    /// <summary>Vertical faces where the floor meets a different height. ⚠ INVENTED, not read
    /// from the disc, and OFF unless TPW_PARK_WALLS=1 -- see the note in Build.</summary>
    static readonly bool WallsEnabled =
        System.Environment.GetEnvironmentVariable("TPW_PARK_WALLS") == "1";

    static readonly bool RaiseEnabled =
        System.Environment.GetEnvironmentVariable("TPW_PARK_RAISE") != "0";

    public float CellY(int x, int y)
    {
        if (!RaiseEnabled || Field == null || x >= Field.Width || y >= Field.Height) return BaseY;
        byte b = Field.Raw0(x, y);
        // ⭐ The step height comes from the field's own header (+0x18), not from CellSize. Master
        // said one unit rendered too short; the header carries 2.0 in all eight parks, which is
        // what a step height should look like -- the same everywhere.
        return BaseY + ((b & 0x40) != 0 && (b & 1) == 0 ? Field.Step : 0f);
    }

    /// <summary>The terrain model's top surface height for each cell of a plot, sampled with a
    /// real point-in-triangle test at the cell centre. ⚠ Not a bounding-box fill: filling each
    /// triangle's box marks cells the triangle never reaches, and I reported a coverage figure off
    /// exactly that mistake.</summary>
    public static float?[,] SurfaceHeights(Node3D terrain, Vector2 origin, int w, int h, float cell)
    {
        var top = new float?[w, h];
        void Walk(Node n, Transform3D acc)
        {
            var t = n is Node3D n3 && n != terrain ? acc * n3.Transform : acc;
            if (n is MeshInstance3D mi && mi.Mesh != null && mi.Visible)
                for (int surf = 0; surf < mi.Mesh.GetSurfaceCount(); surf++)
                {
                    var arr = mi.Mesh.SurfaceGetArrays(surf);
                    var verts = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    if (verts.Length == 0) continue;
                    var idx = arr[(int)Mesh.ArrayType.Index].AsInt32Array();
                    int tris = (idx.Length > 0 ? idx.Length : verts.Length) / 3;
                    for (int i = 0; i < tris; i++)
                    {
                        var p0 = t * verts[idx.Length > 0 ? idx[i * 3] : i * 3];
                        var p1 = t * verts[idx.Length > 0 ? idx[i * 3 + 1] : i * 3 + 1];
                        var p2 = t * verts[idx.Length > 0 ? idx[i * 3 + 2] : i * 3 + 2];
                        var nrm = (p1 - p0).Cross(p2 - p0);
                        if (nrm.Length() <= 0 || Math.Abs(nrm.Y) / nrm.Length() < 0.7f) continue;
                        float ax = (p0.X - origin.X) / cell, az = (p0.Z - origin.Y) / cell;
                        float bx = (p1.X - origin.X) / cell, bz = (p1.Z - origin.Y) / cell;
                        float cx = (p2.X - origin.X) / cell, cz = (p2.Z - origin.Y) / cell;
                        float det = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
                        if (Math.Abs(det) < 1e-9f) continue;
                        int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
                        int x1 = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
                        int z0 = Math.Max(0, (int)Math.Floor(Math.Min(az, Math.Min(bz, cz))));
                        int z1 = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(az, Math.Max(bz, cz))));
                        for (int gz = z0; gz <= z1; gz++)
                            for (int gx = x0; gx <= x1; gx++)
                            {
                                float px = gx + 0.5f, pz = gz + 0.5f;
                                float l1 = ((bz - cz) * (px - cx) + (cx - bx) * (pz - cz)) / det;
                                float l2 = ((cz - az) * (px - cx) + (ax - cx) * (pz - cz)) / det;
                                if (l1 < -0.02f || l2 < -0.02f || l1 + l2 > 1.02f) continue;
                                float y = l1 * p0.Y + l2 * p1.Y + (1 - l1 - l2) * p2.Y;
                                if (top[gx, gz] == null || y > top[gx, gz].Value) top[gx, gz] = y;
                            }
                    }
                }
            foreach (var c in n.GetChildren()) Walk(c, t);
        }
        Walk(terrain, terrain.Transform);
        return top;
    }

    /// <summary>Cells that are ground -- the plot's real size, as opposed to its bounding box.</summary>
    public int PlayableCells
    {
        get
        {
            int n = 0;
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) if (IsPlayable(x, y)) n++;
            return n;
        }
    }

    /// <summary>Put a ride on the plot, as near its middle as it fits. ⚠ Scanning from a corner
    /// drops rides into the plot's thin tail; the middle of the ground is where a park starts.</summary>
    public bool TryPlaceNear(Node3D model, Footprint fp, int id, string name)
    {
        float cx = 0, cy = 0; int n = 0;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (IsPlayable(x, y)) { cx += x; cy += y; n++; }
        if (n == 0) return false;
        cx /= n; cy /= n;
        var order = new List<(int X, int Y)>();
        for (int y = 0; y + fp.Height <= Height; y++)
            for (int x = 0; x + fp.Width <= Width; x++) order.Add((x, y));
        order.Sort((a, b) =>
        {
            float da = (a.X + fp.Width * 0.5f - cx) * (a.X + fp.Width * 0.5f - cx)
                     + (a.Y + fp.Height * 0.5f - cy) * (a.Y + fp.Height * 0.5f - cy);
            float db = (b.X + fp.Width * 0.5f - cx) * (b.X + fp.Width * 0.5f - cx)
                     + (b.Y + fp.Height * 0.5f - cy) * (b.Y + fp.Height * 0.5f - cy);
            return da.CompareTo(db);
        });
        foreach (var (x, y) in order)
            if (TryPlace(model, fp, id, name, x, y)) { LastX = x; LastY = y; return true; }
        return false;
    }

    /// <summary>Where <see cref="TryPlaceNear"/> put the last ride.</summary>
    public int LastX { get; private set; }
    public int LastY { get; private set; }

    /// <summary>Can this footprint sit at (x, y)? Fails on the park edge and on any cell already
    /// claimed. ⚠ Checked BEFORE anything is written, so a rejected placement leaves no trace --
    /// a half-applied placement would corrupt the occupancy map and show up much later as a ride
    /// that cannot be built somewhere for no visible reason.</summary>
    public bool CanPlace(Footprint fp, int x, int y)
    {
        if (x < 0 || y < 0 || x + fp.Width > Width || y + fp.Height > Height) return false;
        for (int fy = 0; fy < fp.Height; fy++)
            for (int fx = 0; fx < fp.Width; fx++)
            {
                if (!fp.Cells[fx, fy]) continue;
                // ⚠ Off the plot is as much a refusal as on top of another ride. Without this a
                // ride sits on a cell that has no ground under it and hangs over the sea.
                if (!IsPlayable(x + fx, y + fy)) return false;
                if (_occupied[x + fx, y + fy] != 0) return false;
            }
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
        // ⚠⚠ NO BASEPLATE. A slab was drawn under every claimed cell -- a grey box the size of the
        // footprint, which is what master saw under everything they put down. It was a debug
        // readout of "these cells are taken" from before the park had a ghost to say so, and the
        // ghost says it now, before the press, where it is actually useful.

        if (model == null) return true;
        // ⚠ The model is already parented elsewhere -- AddChild on a parented node is an ERROR in
        // Godot, not a move, and leaves the ride where it was.
        model.GetParent()?.RemoveChild(model);
        _ride.AddChild(model);
        var (min, max) = DrawnBounds(model, inParent: true);
        var centre = (min + max) * 0.5f;
        model.Position += new Vector3(
            Origin.X + (x + fp.Width * 0.5f) * CellSize - centre.X,
            BaseY - min.Y,
            // ⚠⚠ MINUS, because `centre` is measured IN THE PARENT'S SPACE. `DrawnBounds(model,
            // inParent: true)` starts its walk from `model.Transform`, so the bounds already include
            // the model's own position AND its Scale(1,1,-1). Adding a delta to `Position` shifts
            // parent-space bounds by exactly that delta whatever the scale, so the delta that lands
            // the drawn centre on the target is `target - centre` on every axis -- mirrored or not.
            //
            // History, because this line has flipped three times: main had `DrawnBounds(model)`
            // (local space, excludes the mirror) with `+ centre.Z`, which was right for that frame.
            // visitor-ai had `inParent: true` with `- centre.Z`, also right for ITS frame. The merge
            // took visitor-ai's frame and main's sign -- two halves that are each correct and wrong
            // together. Measured on SPACE t1 (Orbiter, model centre.Z = 1.5002): with `+`, the drawn
            // centre landed 3.0005 off target -- exactly TWICE the centre, for every ride, in the
            // ordinary viewer too. With `-` it lands on the target exactly.
            Origin.Y + (Height - y - fp.Height * 0.5f) * CellSize - centre.Z);
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
    /// <param name="inParent">Measure in the root's PARENT space, applying the root's own
    /// transform. ⚠⚠ THIS MATTERS: <see cref="AnimatedModel"/>'s root carries Scale (1,1,-1), so
    /// local space is Z-MIRRORED against the world every other node lives in. Measuring the terrain
    /// locally and then placing the park in world coordinates put the plot at +Z where the hole is
    /// at -Z -- and a Z mirror leaves the bounding box, the extents and the X axis all correct, so
    /// every number agreed while the picture did not.</param>
    public static (Vector3 Min, Vector3 Max) DrawnBounds(Node3D root, bool inParent = false)
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
        Walk(root, inParent ? root.Transform : Transform3D.Identity);
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
