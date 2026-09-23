using Godot;
using System.Collections.Generic;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>The box drawn round whatever is selected in the park.
///
/// ⭐⭐ READ OUT OF THE GAME, NOT DESIGNED. Master: "its meant to be a cube with those as the
/// corner. can you actually look at the game? you keep guessing." So: `Selectbox.ssh` is loaded by
/// `0x220DC0` into `DAT_002F07D0` and drawn by **`0x221E38`**, which builds FIVE ten-vertex
/// triangle strips -- the top face and the four sides of a BOX -- from a packed index table in the
/// executable at **`0x36E8E0`**, 50 ushorts, one per vertex:
///
///   x index = value >> 8    y index = (value >> 4) &amp; 0xF    z index = value &amp; 0xF
///
/// and three coordinate arrays built from the selection's own bounds:
///
///   X = [minX-o, minX, minX + sx/2, minX + sx, minX + sx + o]
///   Y = [minY,   minY + (sy - p)/2, minY + sy - p]
///   Z = [minZ-o, minZ, minZ + sz/2, minZ + sz, minZ + sz + o]
///
/// ⭐ The four values at x or z index 0 and 4 are only ever used at y index 1 -- mid height -- so
/// the box's four vertical corners are pulled OUT into points. That is the shape; a flat ring of
/// four tiles on the floor, which is what I had, is not.
///
/// ⭐ AND THE UVs PUT THE BRACKET ON THE CORNERS. The ten-entry UV table at `0x36E948` reads
/// (1,0) (0,0) (0,1) (0,0) (1,0) (0,0) (0,1) (0,0) (1,0) (0,0) -- and the vertices carrying (0,0),
/// which is where the bracket lives in the texture, are exactly the box's CORNERS. The mid-edge
/// vertices take (1,0) or (0,1), the empty parts of the tile. So the art is stretched from each
/// corner and fades out along the edges, which is what "a cube with those as the corner" means.
///
/// ⚠ THE OVERHANG PULSES and its driver is NOT read. `o = (DAT_002F0C04 - 0.6) * sx * 0.12`, and
/// DAT_002F0C04 read 0.6958 in master's savestate -- one sample of something that plainly animates
/// (0x2F0C00 beside it held a frame count). That value is used as a constant here rather than an
/// invented oscillation, and it is the only number in this file that is not a reading.
///
/// ⚠ AND THE Y OVERHANG IS COMPUTED FROM sz, NOT sy, in the original: `p = (k - 0.6) * sz * 0.12`.
/// Kept, because reproducing it is the job and the two are equal on the live sample anyway.</summary>
public sealed class SelectionBox
{
    public Node3D Root { get; } = new() { Name = "selection" };

    /// <summary>⚠ A DELEGATE, NOT THE LIBRARY -- this is built before the disc is opened, the same
    /// trap that handed GhostMarkers a null and turned into a hang.</summary>
    readonly System.Func<string, byte[]> _read;
    Material _mat;
    bool _tried;

    public SelectionBox(System.Func<string, byte[]> read) { _read = read; }

    /// <summary>The game's own vertex table at 0x36E8E0: five strips of ten packed indices.</summary>
    static readonly ushort[] Strips =
    {
        0x0122, 0x0123, 0x0223, 0x0323, 0x0322, 0x0321, 0x0221, 0x0121, 0x0122, 0x0222,
        0x0223, 0x0323, 0x0414, 0x0303, 0x0203, 0x0103, 0x0014, 0x0123, 0x0223, 0x0213,
        0x0322, 0x0321, 0x0410, 0x0301, 0x0302, 0x0303, 0x0414, 0x0323, 0x0322, 0x0312,
        0x0221, 0x0321, 0x0410, 0x0301, 0x0201, 0x0101, 0x0010, 0x0121, 0x0221, 0x0211,
        0x0122, 0x0123, 0x0014, 0x0103, 0x0102, 0x0101, 0x0010, 0x0121, 0x0122, 0x0112,
    };

    /// <summary>The game's own UV table at 0x36E948, one pair per vertex of a strip. ⭐ (0,0) is
    /// the bracket; it lands on the box's corners and nowhere else.</summary>
    static readonly Vector2[] Uv =
    {
        new(1, 0), new(0, 0), new(0, 1), new(0, 0), new(1, 0),
        new(0, 0), new(0, 1), new(0, 0), new(1, 0), new(0, 0),
    };

    /// <summary>`DAT_002F0C04` as master's savestate held it. ⚠ The one number here that is a
    /// sample of something animated rather than a rule.</summary>
    public const float Pulse = 0.6958f;

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
                // ⚠ Both sides: the strips are a closed-ish shell and the camera goes round it.
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                NoDepthTest = true,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            };
        }
        catch (System.Exception e) { GD.PrintErr($"[select] Selectbox would not build: {e.Message}"); }
        return _mat;
    }

    public void Hide() { foreach (var c in Root.GetChildren()) c.QueueFree(); }

    /// <summary>Put the box round a world-space box.</summary>
    public void Show(Vector3 min, Vector3 size)
    {
        Hide();
        if (Bracket() is not { } mat || size.X <= 0f || size.Z <= 0f) return;

        float k = Pulse - 0.6f;
        float o = k * size.X * 0.12f;      // ⚠ x AND z, both from sx, as 0x221E38 has it
        float p = k * size.Z * 0.12f;      // ⚠ the Y overhang, from sz

        float[] xs = { min.X - o, min.X, min.X + size.X * 0.5f, min.X + size.X, min.X + size.X + o };
        float[] ys = { min.Y, min.Y + (size.Y - p) * 0.5f, min.Y + size.Y - p };
        float[] zs = { min.Z - o, min.Z, min.Z + size.Z * 0.5f, min.Z + size.Z, min.Z + size.Z + o };

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 At(int i)
        {
            ushort v = Strips[i];
            return new Vector3(xs[v >> 8], ys[(v >> 4) & 0xF], zs[v & 0xF]);
        }
        // ⚠⚠ FOUR TRIANGLES A ROW, STEPPING BY TWO: (0,1,2) (2,3,4) (4,5,6) (6,7,8).
        //
        // 0x221E38 asks its builder for `(ctx, 7, 0x50)` -- 80 bytes, four vertices of twenty --
        // so it looked like a quad, and I read it as one. It is not. The loop emits three vertices
        // and then appends a fourth whose index is computed from the value iVar15 had BEFORE the
        // loop ran: the fourth vertex REPEATS THE FIRST, position and UV alike. A degenerate
        // fourth corner. The primitive is a triangle sent in a four-vertex slot.
        //
        // ⭐ AND THE UVs PROVE IT. Per triangle they come out (1,0) (0,0) (0,1) -- three DIFFERENT
        // corners of the tile, with the bracket at (0,0) on the middle vertex, which the index
        // table puts on a box CORNER every time. Read as a real quad the fourth vertex takes (0,0)
        // as well, two vertices share the bracket texel, and the edge between them samples that one
        // texel along its whole length: the render came back with white lines ruled across the box,
        // which is what sent me back to the loop.
        for (int s = 0; s < 5; s++)
        {
            int b = s * 10;
            for (int i = 0; i + 2 < 10; i += 2)
                for (int j = 0; j < 3; j++)
                {
                    st.SetUV(Uv[i + j]);
                    st.SetNormal(Vector3.Up);
                    st.AddVertex(At(b + i + j));
                }
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
