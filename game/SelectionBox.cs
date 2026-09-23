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
/// ⭐⭐ AND IT BREATHES, and the whole chain is read. Master: "can u find what animates the box".
///
///   0x1B3038  every frame, unless paused:  DAT_002E74A8 += frameTime   (0x1C4920, the SAME getter
///             the camera at 0x14F820 calls five times, so the same 0x1000-a-tick unit)
///   0x1B2EE0  angle = (DAT_002E74A8 * 0x28 >> 12) &amp; 0xFFF          -- a 12-bit turn
///             s     = |(sin12(angle) &lt;&lt; 8) >> 12|                  -- 0..256
///   0x195998  sin12(a) = (int)(sinf(a / 4096 * 6.283) * 4096)        -- note 6.283, not 2pi
///   0x2225A0  DAT_002F0C04 = sinf(s / 256)                           -- 0x28C910 IS sinf
///   0x221E38  o = (DAT_002F0C04 - 0.6) * sx * 0.12
///
/// So the pulse is `sinf(|sin(angle)|)`, sweeping 0 .. sin(1) = 0.8415, and the corners move
/// between 7.2% of the box pulled IN and 2.9% pushed OUT. The angle gains 40 of 4096 a tick: a
/// full turn every 102.4 ticks, and because of the |sin| the visible breath repeats every HALF of
/// that -- about once a second at the console's 50. Master's savestate caught it at 0.6958, which
/// is |sin| = 0.77 on this curve, a sample landing where it should.
///
/// ⭐ 0x1B3038 walks THREE slots at 0x397470, so the console can hold up to three boxes at once.
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

    /// <summary>`DAT_002E74A8`: the box's own clock, in the console's frame-time units (0x1000 a
    /// tick). ⚠ Advanced from real delta at the console's RATE rather than in whole ticks -- the
    /// session's rule, console speed interpolated -- so the breath is smooth and exactly as fast.</summary>
    double _clock;
    Vector3 _min, _size;
    bool _shown;

    /// <summary>The pulse, exactly as 0x1B2EE0 -> 0x2225A0 compute it. ⚠ The integer steps are
    /// kept (s is 0..256), because they are the game's; only the ANGLE is fed continuously.</summary>
    public float Pulse
    {
        get
        {
            double angle12 = (_clock * 0x28 / 4096.0) % 4096.0;
            int s12 = (int)(System.Math.Sin(angle12 / 4096.0 * 6.283) * 4096.0);
            int s = System.Math.Abs((s12 << 8) >> 12);
            return (float)System.Math.Sin(s * 0.00390625);
        }
    }

    /// <summary>Advance the clock and redraw, while a box is up. ⚠ The console skips the add while
    /// paused (0x14DD68); the caller does the same by not stepping.</summary>
    public void Step(double delta)
    {
        if (!_shown) return;
        _clock += delta * GameCamera.TicksPerSecond * GameCamera.FrameTick;
        Build();
    }

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
                // ⭐⭐ YELLOW, AND THE TINT IS IN THE CODE. 0x221E38 sets four consecutive floats
                // immediately before its draw loop, from DAT_002F0C00 (which 0x2225A0 takes from
                // the selected object's own field, and which held 9 in master's savestate):
                //
                //   0x2F02A0 = n * 0.25 * 0.0078125 + 1.6   ->  1.6176
                //   0x2F02A4 = n * 0.21 * 0.0078125 + 1.4   ->  1.4148
                //   0x2F02A8 = 0
                //   0x2F02AC = n * 0.00390625               ->  0.0352
                //
                // Four in a row with the third pinned at ZERO and the first two near each other is
                // an RGBA, and R:G:B of 1.6176 : 1.4148 : 0 is yellow. Master said yellow from
                // having played it; the constants say yellow from the other end. The tile itself
                // is white, so it is tinted, and this is that tint normalised on its own maximum.
                AlbedoColor = new Color(1f, 1.4148f / 1.6176f, 0f),
                // ⚠ Both sides: the strips are a closed-ish shell and the camera goes round it.
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                // ⚠⚠ IT DEPTH-TESTS. I had NoDepthTest on, reasoning that a selection hidden behind
                // what you selected is not a selection -- master: "it seems like all the corners
                // are rendering on top of the ride". They are meant to go behind it; the far
                // corners of a box are behind the thing in the box. No depth WRITE, though, so the
                // brackets do not fight each other where two overlap.
                DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            };
        }
        catch (System.Exception e) { GD.PrintErr($"[select] Selectbox would not build: {e.Message}"); }
        return _mat;
    }

    public void Hide() { _shown = false; Clear(); }
    void Clear() { foreach (var c in Root.GetChildren()) c.QueueFree(); }

    /// <summary>Put the box round a world-space box.</summary>
    public void Show(Vector3 min, Vector3 size)
    {
        _min = min; _size = size; _shown = true;
        Build();
    }

    void Build()
    {
        Clear();
        var min = _min; var size = _size;
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
