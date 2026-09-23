using Godot;
using System.Collections.Generic;
using System.Linq;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>The build ghost's markers, drawn on the ground under the run.
///
/// ⭐ The art is the console's own: `data/generic/tiles/165.tga` and friends, a flat teal for
/// "will lay", a flat red for "refused" and two interlocking rings for a join. They are numbered
/// with the PSX build's marker sprite ids and the numbers are what the verdict table stores, so
/// they are asked for by number here too.
///
/// ⚠ The console draws TWO quads per tile and lifts each corner to that corner's OWN tile height,
/// so its markers drape over a slope. The plot's floor here is flat per cell, so one quad per cell
/// at the cell's own height is the matching thing to draw -- a second quad and per-corner heights
/// would be modelling a shape the floor underneath does not have.</summary>
public sealed class GhostMarkers
{
    public Node3D Root { get; } = new() { Name = "ghost" };

    /// <summary>⚠⚠ A DELEGATE, NOT THE LIBRARY. This node is built by `BuildUi`, which runs
    /// BEFORE the disc is opened -- the line that opens it sits higher in the file, which is why
    /// holding the library looked safe and handed every marker a null. Reading through a lambda
    /// takes the field at the moment it is needed instead of at the moment this was made.</summary>
    readonly System.Func<string, byte[]> _read;
    readonly Dictionary<int, Material> _cache = new();

    public GhostMarkers(System.Func<string, byte[]> read) { _read = read; }

    Material MarkerMaterial(int id)
    {
        if (_cache.TryGetValue(id, out var got)) return got;
        Material made = null;
        // ⚠⚠ THIS RUNS EVERY FRAME. A throw here is not a bug that shows up as a bug: it is caught
        // by the engine, logged, and the frame is abandoned -- so the app never reaches the next
        // thing it was going to do and simply sits there looking hung. Whatever goes wrong, it
        // must come out as one line and a marker that does not draw.
        byte[] bytes = null;
        try { bytes = _read?.Invoke($"/Generic/Tiles/{id}.tga"); }
        catch (System.Exception e) { GD.PrintErr($"[ghost] marker {id} would not read: {e.Message}"); }
        if (bytes == null) GD.PrintErr($"[ghost] marker {id}: the disc has no /Generic/Tiles/{id}.tga");
        if (bytes != null)
        {
            var tga = new Targa(bytes);
            var img = Image.CreateFromData(tga.Width, tga.Height, false, Image.Format.Rgba8, tga.Pixels);
            try
            {
            made = new StandardMaterial3D
            {
                AlbedoTexture = ImageTexture.CreateFromImage(img),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                // ⚠ No depth WRITE -- set through the typed property, not through Set(name, ...).
                // A material setting that is spelt wrong takes silently and reads as applied; that
                // is how `depth_draw_opaque` sat in this repo claiming to have fixed something.
                // The markers lie a few millimetres over the floor and must not fight it.
                DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            };
            }
            catch (System.Exception e) { GD.PrintErr($"[ghost] marker {id} would not build: {e.Message}"); }
        }
        // ⚠ Cached even when it failed, so a broken marker is one line rather than one a frame.
        _cache[id] = made;
        return made;
    }

    /// <summary>Redraw the ghost for this run. An empty run clears it.</summary>
    public void Show(PathGhost ghost, Park park)
    {
        if (ghost == null) { Clear(); return; }
        ShowTurnedCells(ghost.Tiles.Select(t => (t.X, t.Y, PathGhost.Marker(t.Verdict), 0)), park);
    }

    /// <summary>Draw any set of marked cells. ⭐ The same drawing for a path run and a thing being
    /// put down: both are "these tiles, wearing these markers", and the console draws them with
    /// the same call too.</summary>
    public void ShowCells(IEnumerable<(int X, int Y, int Marker)> cells, Park park)
        => ShowTurnedCells(cells?.Select(c => (c.X, c.Y, c.Marker, 0)), park);

    /// <summary>The quad's UV corners in the order the vertices come out, matching the plot's own
    /// floor so a marker and a ground tile turn the same way.</summary>
    static readonly Vector2[] Uv = { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };

    /// <summary>⭐⭐ WHICH WAY IS UP IN A TILE. Turn 0 lays the texture's top edge along world -Z,
    /// which is grid +y, and each further turn takes it a quarter round:
    /// 0 -> grid +y, 1 -> grid -x, 2 -> grid -y, 3 -> grid +x. Derived from the vertex order, not
    /// guessed: corner a is world (-X,-Z) and wears UV (0,0), so v grows with world +Z.
    ///
    /// ⚠ The DIRECTIONAL markers need this -- the entrance and exit arrows are pictures of a way
    /// to walk, and a ride turned a quarter with its arrows left alone points its visitors at a
    /// wall. Master: "the arrows for entry/exit arent following the rotation of the ride".</summary>
    public static int TurnToward(int dx, int dy)
        => dy > 0 ? 0 : dx < 0 ? 1 : dy < 0 ? 2 : 3;

    /// <summary>Draw marked cells, each with its own quarter turn.</summary>
    public void ShowTurnedCells(IEnumerable<(int X, int Y, int Marker, int Turns)> cells, Park park)
    {
        foreach (var c in Root.GetChildren()) c.QueueFree();
        if (cells == null || park == null) return;

        // One surface per marker, the way the plot groups its floor: a run is mostly one verdict.
        var byMarker = new Dictionary<int, SurfaceTool>();
        float half = Park.CellSize * 0.5f;
        foreach (var t in cells)
        {
            int id = t.Marker;
            if (!byMarker.TryGetValue(id, out var st))
            {
                st = new SurfaceTool();
                st.Begin(Mesh.PrimitiveType.Triangles);
                byMarker[id] = st;
            }
            var c = park.CellCentre(t.X, t.Y);
            float y = park.CellY(t.X, t.Y) + Park.CellSize * 0.03f;
            var a = new Vector3(c.X - half, y, c.Z - half);
            var b = new Vector3(c.X + half, y, c.Z - half);
            var d = new Vector3(c.X + half, y, c.Z + half);
            var e = new Vector3(c.X - half, y, c.Z + half);
            int turn = t.Turns & 3;
            void V(Vector3 v, int corner)
            {
                st.SetUV(Uv[(corner + turn) & 3]);
                st.SetNormal(Vector3.Up);
                st.AddVertex(v);
            }
            V(a, 0); V(b, 1); V(d, 2);
            V(a, 0); V(d, 2); V(e, 3);
        }
        foreach (var (id, st) in byMarker)
        {
            var mesh = st.Commit();
            if (mesh == null || mesh.GetSurfaceCount() == 0) continue;
            var mat = MarkerMaterial(id);
            if (mat == null) continue;
            Root.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }

    public void Clear() { foreach (var c in Root.GetChildren()) c.QueueFree(); }
}
