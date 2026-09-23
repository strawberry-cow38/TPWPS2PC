using Godot;

namespace TPWPS2Viewer;

/// <summary>The box drawn round whatever is selected in the park.
///
/// ⭐ IT IS THE FOOTPRINT'S BOX, not the model's. A ride's mesh overhangs its cells -- the Belly
/// Bounce's fence and signs hang off one side by 0.40 of a cell -- so a box cut to the drawn
/// bounds would sit a little wider than the ground the ride actually claims, and the two would
/// disagree with the blueprint that put it there. The footprint is what the park owns and what a
/// selection means.
///
/// ⭐ The HEIGHT comes from the model, because that part is not a footprint question: a box one
/// cell tall round a coaster would be a line on the floor.
///
/// ⚠ LINES, not a transparent solid. A solid box over a ride tints it and reads as a graphical
/// fault; edges read as a selection and leave the thing itself visible, which is the point of
/// selecting it.</summary>
public sealed class SelectionBox
{
    public Node3D Root { get; } = new() { Name = "selection" };

    MeshInstance3D _mesh;

    /// <summary>Clear it.</summary>
    public void Hide()
    {
        if (_mesh != null && GodotObject.IsInstanceValid(_mesh)) _mesh.QueueFree();
        _mesh = null;
    }

    /// <summary>Draw a box round a footprint at (cx,cy), rising to <paramref name="top"/>.</summary>
    public void Show(Park park, Park.Footprint fp, int cx, int cy, float top, Color colour)
    {
        Hide();
        if (park == null || fp.Width <= 0) return;

        // ⚠ FROM THE CORNERS, min and max in WORLD terms. The plot's transform mirrors, so walking
        // the footprint's corners in grid order gives a rectangle whose "min" is not the minimum
        // of anything -- the same trap the floor hit, where grid-ordered corners wound the quads
        // the wrong way and the whole park was culled.
        var a = park.CellCorner(cx, cy);
        var b = park.CellCorner(cx + fp.Width, cy + fp.Height);
        float x0 = Mathf.Min(a.X, b.X), x1 = Mathf.Max(a.X, b.X);
        float z0 = Mathf.Min(a.Z, b.Z), z1 = Mathf.Max(a.Z, b.Z);
        float y0 = park.BaseY + 0.02f;
        float y1 = Mathf.Max(top, y0 + 0.5f);

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Lines);
        void Line(Vector3 p, Vector3 q) { st.AddVertex(p); st.AddVertex(q); }
        var lo = new[] { new Vector3(x0, y0, z0), new Vector3(x1, y0, z0),
                         new Vector3(x1, y0, z1), new Vector3(x0, y0, z1) };
        var hi = new[] { new Vector3(x0, y1, z0), new Vector3(x1, y1, z0),
                         new Vector3(x1, y1, z1), new Vector3(x0, y1, z1) };
        for (int i = 0; i < 4; i++)
        {
            Line(lo[i], lo[(i + 1) & 3]);
            Line(hi[i], hi[(i + 1) & 3]);
            Line(lo[i], hi[i]);
        }
        var mesh = st.Commit();
        if (mesh == null || mesh.GetSurfaceCount() == 0) return;

        _mesh = new MeshInstance3D
        {
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = colour,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                // ⚠ Through the ride, deliberately. A selection you cannot see because the thing
                // you selected is in front of it is not a selection.
                NoDepthTest = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                VertexColorUseAsAlbedo = false,
            },
        };
        Root.AddChild(_mesh);
    }
}
