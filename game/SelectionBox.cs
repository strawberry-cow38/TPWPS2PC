using Godot;
using System.Collections.Generic;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>The box drawn round whatever is selected in the park.
///
/// ⭐⭐ IT IS THE GAME'S OWN ART, AND IT IS FOUR CORNERS. `/Generic/selection/Selectbox.tga` is a
/// single 64x64 tile that is transparent except for ONE bracket in its top-left -- so a selection
/// is that tile laid on the four corner cells of the thing, each turned so its bracket points out.
/// Master: "look at how the actual game does selection boxes." It does not draw a wireframe cage;
/// the first version here did, and it was an invention.
///
/// ⭐ WHICH TURN GOES WHERE falls out of the quad's own UV order, the same table the ground tiles
/// and the ghost markers use. Texture (0,0) -- where the bracket is -- lands at:
///
///   turn 0 -> grid (low x, high y)      turn 2 -> grid (high x, low y)
///   turn 1 -> grid (low x, low y)       turn 3 -> grid (high x, high y)
///
/// so the corner cell at (x0,y0) takes turn 1, (x1,y0) turn 2, (x1,y1) turn 3 and (x0,y1) turn 0.
///
/// ⚠ ONE CELL EACH. The bracket is a ground tile the size of every other ground tile, so it is not
/// stretched to the shape of what is selected -- a 4x3 ride and a 1x1 stall wear the same corner.
///
/// ⚠ AND IT IS FLAT. The console draws these on the floor with the rest of its markers; a box that
/// rose to the model's height would be a thing this game does not have.</summary>
public sealed class SelectionBox
{
    public Node3D Root { get; } = new() { Name = "selection" };

    /// <summary>⚠ A DELEGATE, NOT THE LIBRARY -- this is built before the disc is opened, the same
    /// trap that handed GhostMarkers a null and turned into a hang.</summary>
    readonly System.Func<string, byte[]> _read;
    Material _mat;
    bool _tried;

    public SelectionBox(System.Func<string, byte[]> read) { _read = read; }

    static readonly Vector2[] Uv = { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };

    Material Bracket()
    {
        if (_tried) return _mat;
        _tried = true;
        byte[] bytes = null;
        try { bytes = _read?.Invoke("/Generic/selection/Selectbox.tga"); }
        catch (System.Exception e) { GD.PrintErr($"[select] Selectbox would not read: {e.Message}"); }
        if (bytes == null) { GD.PrintErr("[select] the disc has no /Generic/selection/Selectbox.tga"); return null; }
        try
        {
            var tga = new Targa(bytes);
            var img = Image.CreateFromData(tga.Width, tga.Height, false, Image.Format.Rgba8, tga.Pixels);
            _mat = new StandardMaterial3D
            {
                AlbedoTexture = ImageTexture.CreateFromImage(img),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            };
        }
        catch (System.Exception e) { GD.PrintErr($"[select] Selectbox would not build: {e.Message}"); }
        return _mat;
    }

    public void Hide() { foreach (var c in Root.GetChildren()) c.QueueFree(); }

    /// <summary>Put the four brackets on a footprint's corner cells.</summary>
    public void Show(Park park, Park.Footprint fp, int cx, int cy)
    {
        Hide();
        if (park == null || fp.Width <= 0 || Bracket() is not { } mat) return;

        int x0 = cx, x1 = cx + fp.Width - 1, y0 = cy, y1 = cy + fp.Height - 1;
        var corners = new (int X, int Y, int Turn)[]
        {
            (x0, y0, 1), (x1, y0, 2), (x1, y1, 3), (x0, y1, 0),
        };

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float half = Park.CellSize * 0.5f;
        foreach (var (x, y, turn) in corners)
        {
            var c = park.CellCentre(x, y);
            // ⚠ Above the ghost markers as well as the floor: a selection and a build ghost can be
            // on the same cell, and the selection is the one you are looking for.
            float h = park.CellY(x, y) + Park.CellSize * 0.06f;
            var a = new Vector3(c.X - half, h, c.Z - half);
            var b = new Vector3(c.X + half, h, c.Z - half);
            var d = new Vector3(c.X + half, h, c.Z + half);
            var e = new Vector3(c.X - half, h, c.Z + half);
            void V(Vector3 v, int corner)
            {
                st.SetUV(Uv[(corner + turn) & 3]);
                st.SetNormal(Vector3.Up);
                st.AddVertex(v);
            }
            V(a, 0); V(b, 1); V(d, 2);
            V(a, 0); V(d, 2); V(e, 3);
        }
        var mesh = st.Commit();
        if (mesh == null || mesh.GetSurfaceCount() == 0) return;
        Root.AddChild(new MeshInstance3D
        {
            Mesh = mesh, MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }
}
