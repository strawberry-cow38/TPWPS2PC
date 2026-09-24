using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>The eight flags on the poles beside the park's bus stop.
///
/// ⭐⭐ THEY ARE NOT A MESH ON THE DISC, WHICH IS WHY LOOKING FOR ONE FOUND NOTHING. I searched
/// every model on the disc for a mesh named like a flag, found only the go-karts' `Newflag` and the
/// race sideshow's `startflag`, and reported "there is nothing on the disc to do this". Master:
/// *"there are flags on the poles. use a sine. check data again."* They were right; the search had
/// no control and a miss is not an answer. The flags are built by the ENGINE.
///
/// ⭐ WHERE THEY COME FROM, read out of SLES_500.32:
///
/// * `0x220FA0(obj, float x, float y, float z)` allocates **528 bytes = 16 + 8 x 64** and writes a
///   count of **8**, then constructs eight objects of 64 bytes (`0x22EDB0`, stride 64).
/// * Each is handed to `0x22EEB8(obj, verts, verts2, texture)` at a position offset from (x,y,z):
///   x + **0, 4, 10, 14** crossed with z + **0, 1.2** -- four columns, two rows, eight flags.
/// * The texture is loaded by name at `0x221108` into `DAT_002F07CC`, from
///   `data/generic/weather/` -- **`logo.ssh`**, or `logoam.ssh` when a region flag is set
///   (`logojp.ssh` ships too). The same call loads `raindrop.ssh`, `snowflake.ssh` and
///   `justwater.ssh`, so a flag is an ENGINE EFFECT in the same bucket as the rain, not scenery.
/// * The base (x,y,z) is hard-coded per park at `0x149A70..0x149BC0`:
///   x is one of **23.125, 29.125, 31.125, 33.125, 37.125, 41.125**, y is **2.2**, z is **5.875**.
///
/// ⭐⭐ AND THE MESH CONFIRMS EVERY NUMBER. Clustering the tall vertices of `A_POLES &amp; BOLLARDS`
/// gives **eight** poles, not four:
///
///   x 23.135 / 27.135 / 33.135 / 37.135   (offsets +0, +4.00, +10.00, +14.00)
///   z  5.872 / 7.077                      (the code's z + 0 and z + 1.2 = 5.875 and 7.075)
///   top y 2.37                            (the code anchors at 2.2, just under the finial)
///
/// Eight poles, eight flags, one each. And across all eight parks the measured first-pole x is
/// {23.13, 23.13, 33.13, 31.13, 41.13, 37.13, 41.13, 29.13} -- exactly the six values the
/// executable lists, with nothing left over on either side. A model in a WAD and float immediates
/// in an ELF are two sources that know nothing about each other.
///
/// ⭐ SO THE ANCHORS ARE MEASURED, NOT HARD-CODED. The six magic numbers are real, but the poles
/// are in front of us and say the same thing per park with no table to get out of step. The
/// executable's rule is kept as a CHECK, printed when the two disagree.
///
/// ⚠ WHAT IS NOT READ: which way a flag flies and how far. `0x22EEB8` is handed two corners,
/// (0.05, 0, 0) and (1.08, 0, 0.72), and the setup rotates by -pi/2 (`0x16F2D0`), which reads as a
/// flag about 1.03 long and 0.72 tall standing up -- but the axis that -pi/2 turns about is not
/// established here, so the DIRECTION below is a choice. The sine is master's instruction, not a
/// decode: no `sinf` call reaches these objects on the EE, so the wave is on the VU or in the
/// draw, and neither has been read.</summary>
public sealed class EntranceFlags
{
    public Node3D Root { get; } = new() { Name = "EntranceFlags" };

    /// <summary>The executable's own offsets from the base, `0x2212C0..0x2213F8`.</summary>
    static readonly float[] PoleX = { 0f, 4f, 10f, 14f };
    static readonly float[] PoleZ = { 0f, 1.2f };
    /// <summary>`0x149A70..0x149BC0` passes y = 2.2 and z = 5.875 in every park; only x moves.</summary>
    public const float AnchorY = 2.2f, BaseZ = 5.875f;
    /// <summary>Every x the executable hands to `0x220FA0`, for the check against the poles.</summary>
    static readonly float[] KnownBaseX = { 23.125f, 29.125f, 31.125f, 33.125f, 37.125f, 41.125f };

    /// <summary>From the two corners `0x2211FC..0x2212A0` builds: 1.08 - 0.05 long, 0.72 across.</summary>
    const float Length = 1.03f, Height = 0.72f;
    /// <summary>How many segments the strip is cut into. ⚠ A CHOICE -- the console's vertex count
    /// is not read. Eight is enough for the sine to read as cloth rather than as a hinge.</summary>
    const int Segments = 8;

    readonly List<(Vector3 At, MeshInstance3D Mesh, ArrayMesh Strip)> _flags = new();
    /// <summary>⚠ REUSED, NOT REBUILT. A SurfaceTool commit per flag per frame is eight meshes
    /// and eight tool objects a frame for geometry whose SHAPE never changes -- only the vertex
    /// positions do. The arrays and the ArrayMesh are allocated once and refilled.</summary>
    Vector3[] _pos;
    Vector2[] _uv;
    Vector3[] _norm;
    int[] _index;
    Godot.Collections.Array _arrays;
    ShaderMaterial _material;
    double _clock;

    public string Report { get; private set; } = "not built";

    /// <summary>By-eye offset on every flag along **z**, in units, driven by the `[`/`]` tool --
    /// the same axis and the same gesture the gate's nudge used.
    ///
    /// ⚠⚠ THIS WAS Y FOR ONE COMMIT AND THAT WAS WRONG. I moved the flags up and down because
    /// the pole-height heuristic is the shakiest number in this file; master was calibrating
    /// where the flags SIT along the entrance, which is the same thing the gate tool did. The
    /// shakiest number is not automatically the one being looked at.
    ///
    /// ⚠ An OFFSET, never a replacement: the read anchor still does the work and this rides on
    /// top, so a nudge of 0 is exactly the behaviour before the tool existed.</summary>
    public float NudgeZ { get; set; }

    /// <summary>⭐⭐ ONE WIND FOR THE WHOLE ENTRANCE, in degrees about y, 0 being the +x the strip
    /// is built along. Master: "they should all point in a global wind direction (they all face
    /// the same way)." Applied as a rotation on every flag from a single field, so they cannot
    /// drift apart -- eight flags that each decided their own heading is precisely the thing that
    /// would read as a bug. ⚠ CHOSEN: the console's wind direction is not read.</summary>
    public float WindDegrees { get; set; }

    /// <summary>How hard the flag flaps up and down. ⚠ CHOSEN, like the rest of the motion --
    /// the console's vertex animation is not read, only that the flags move.</summary>
    public float VerticalAmplitude { get; set; } = 0.16f;

    /// <summary>Stand the flags on this park's poles. <paramref name="terrain"/> is the park's own
    /// terrain model; <paramref name="read"/> fetches a shared asset out of DATA.WAD.</summary>
    public void Build(Model terrain, Func<string, byte[]> read)
    {
        foreach (var c in Root.GetChildren()) c.QueueFree();
        _flags.Clear();

        var anchors = PoleAnchors(terrain, out string where);
        if (anchors.Count == 0) { Report = where; return; }

        var texture = LoadLogo(read, out string texReport);
        _material = new ShaderMaterial
        {
            // ⚠ Soft, not cutout: the logo's edge is antialiased into alpha and a 16/255 cut
            // leaves it ragged. cull_disabled because a flag is one sheet seen from both sides.
            Shader = Ps2Materials.Shader(true, "cull_disabled", rawNormals: false,
                                         linearFilter: Ps2Materials.Bilinear),
        };
        _material.SetShaderParameter("albedo_tex", texture);
        _material.SetShaderParameter("has_tex", texture != null);
        Ps2Materials.BindLight(_material);

        foreach (var at in anchors)
        {
            var mi = new MeshInstance3D
            {
                Name = $"flag{_flags.Count}",
                MaterialOverride = _material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            var strip = new ArrayMesh();
            mi.Mesh = strip;
            mi.Rotation = new Vector3(0f, Mathf.DegToRad(WindDegrees), 0f);
            Root.AddChild(mi);
            _flags.Add((at, mi, strip));
        }
        Wave(0d);
        Report = $"{_flags.Count} flags on {where}; {texReport}";
    }

    /// <summary>⭐ THE POLES THEMSELVES ARE THE ANCHOR TABLE. The tall vertices of
    /// `A_POLES &amp; BOLLARDS` cluster into eight columns; their shared top is where a flag hangs
    /// from. ⚠ Falls back to nothing rather than to a guessed position: a flag standing in the
    /// wrong place is worse than no flag, because it reads as a modelling error.</summary>
    static List<Vector3> PoleAnchors(Model terrain, out string report)
    {
        var found = new List<Vector3>();
        report = "no A_POLES & BOLLARDS in this terrain";
        if (terrain == null) return found;
        var mesh = terrain.Meshes.FirstOrDefault(
            m => m.Name.Contains("POLES", StringComparison.OrdinalIgnoreCase));
        if (mesh == null) return found;

        var world = terrain.WorldTransforms();
        var pts = terrain.Vertices(mesh).Pos
            .Select(p => System.Numerics.Vector3.Transform(p, world[mesh.Offset])).ToArray();
        if (pts.Length == 0) return found;
        float top = pts.Max(p => p.Y);
        // ⚠ 0.6 of the top, not a fixed height: it separates the four-metre poles from the
        // bollards at their feet without knowing either number in advance.
        var tall = pts.Where(p => p.Y > top * 0.6f).ToArray();
        var cells = new Dictionary<(int, int), (int N, float X, float Z, float Top)>();
        foreach (var p in tall)
        {
            var key = ((int)MathF.Round(p.X * 2), (int)MathF.Round(p.Z * 2));
            (int N, float X, float Z, float Top) c =
                cells.TryGetValue(key, out var e) ? e : (0, 0f, 0f, float.MinValue);
            cells[key] = (c.N + 1, c.X + p.X, c.Z + p.Z, MathF.Max(c.Top, p.Y));
        }
        foreach (var c in cells.Values.OrderBy(c => c.X / c.N).ThenBy(c => c.Z / c.N))
            found.Add(new Vector3(c.X / c.N, AnchorY, c.Z / c.N));

        // ⭐ THE EXECUTABLE'S RULE, USED AS A CHECK AND NOT AS THE ANSWER. It says eight poles at
        // base + {0,4,10,14} x {0,1.2} with base z 5.875 and an x from a fixed list. If the mesh
        // disagrees, SAY SO -- that is the interesting case, not a reason to override it.
        var faults = new List<string>();
        if (found.Count != PoleX.Length * PoleZ.Length) faults.Add($"{found.Count} poles, not 8");
        if (found.Count > 0)
        {
            float bx = found.Min(p => p.X), bz = found.Min(p => p.Z);
            if (!KnownBaseX.Any(k => MathF.Abs(k - bx) < 0.05f))
                faults.Add($"base x {bx:F3} is not one the executable lists");
            if (MathF.Abs(bz - BaseZ) > 0.05f) faults.Add($"base z {bz:F3}, executable says {BaseZ}");
            foreach (var p in found)
            {
                if (!PoleX.Any(o => MathF.Abs(p.X - bx - o) < 0.05f)) faults.Add($"x {p.X:F3} off the grid");
                if (!PoleZ.Any(o => MathF.Abs(p.Z - bz - o) < 0.05f)) faults.Add($"z {p.Z:F3} off the grid");
            }
        }
        report = faults.Count == 0
            ? $"{found.Count} poles, and every one is where the executable puts it"
            : $"{found.Count} poles, DISAGREEING with the executable: {string.Join("; ", faults.Take(4))}";
        return found;
    }

    /// <summary>`data/generic/weather/logo.ssh`. ⚠ The European build's; `logoam.ssh` is picked
    /// instead when the region flag at `0x2210E8` is set, and `logojp.ssh` also ships.</summary>
    static ImageTexture LoadLogo(Func<string, byte[]> read, out string report)
    {
        report = "no logo.ssh";
        if (read == null) return null;
        try
        {
            var bytes = read("/Generic/extra/logo.ssh");
            if (bytes == null) return null;
            var ssh = new Ssh(bytes);
            var img = Image.CreateFromData(ssh.Width, ssh.Height, false, Image.Format.Rgba8, ssh.Pixels);
            report = $"logo.ssh {ssh.Width}x{ssh.Height}";
            return ImageTexture.CreateFromImage(img);
        }
        catch (Exception e) { report = $"logo.ssh would not decode: {e.Message}"; return null; }
    }

    /// <summary>Advance the wave. ⚠ Rebuilds the strips: eight flags of nine rows is 144 vertices
    /// a frame, which is cheaper than a shader uniform would be to wire and keeps the geometry
    /// where it can be measured.</summary>
    public void Step(double delta)
    {
        if (_flags.Count == 0) return;
        _clock += delta;
        Wave(_clock);
    }

    void Wave(double time)
    {
        EnsureArrays();
        for (int i = 0; i < _flags.Count; i++)
        {
            var (at, mi, strip) = _flags[i];
            // ⚠ A PHASE PER FLAG, or eight flags in a row beat as one sheet and read as a bug.
            Fill((float)time * 2.6f + i * 0.7f);
            strip.ClearSurfaces();
            strip.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, _arrays);
            // ⭐ The by-eye offset rides on the read anchor -- see NudgeZ.
            mi.Position = at + new Vector3(0f, 0f, NudgeZ);
        }
    }

    void EnsureArrays()
    {
        if (_arrays != null) return;
        int rows = Segments + 1, n = rows * 2;
        _pos = new Vector3[n];
        _uv = new Vector2[n];
        _norm = new Vector3[n];
        _index = new int[Segments * 6];
        for (int s = 0; s < rows; s++)
        {
            float u = (float)s / Segments;
            _uv[s * 2] = new Vector2(u, 0f);
            _uv[s * 2 + 1] = new Vector2(u, 1f);
            _norm[s * 2] = _norm[s * 2 + 1] = Vector3.Up;
        }
        for (int s = 0; s < Segments; s++)
        {
            int a = s * 2, b = a + 2, k = s * 6;
            _index[k] = a; _index[k + 1] = b; _index[k + 2] = a + 1;
            _index[k + 3] = b; _index[k + 4] = b + 1; _index[k + 5] = a + 1;
        }
        _arrays = new Godot.Collections.Array();
        _arrays.Resize((int)Mesh.ArrayType.Max);
        _arrays[(int)Mesh.ArrayType.TexUV] = _uv;
        _arrays[(int)Mesh.ArrayType.Normal] = _norm;
        _arrays[(int)Mesh.ArrayType.Index] = _index;
    }

    /// <summary>One flag, hanging from the pole top and flying in +x.
    ///
    /// ⚠ THE DIRECTION IS A CHOICE. See the class note: the corners are read, the axis of the
    /// -pi/2 turn is not. +x runs along the row of poles.
    ///
    /// ⭐ The amplitude grows along the length, because a flag is PINNED at the pole: a uniform
    /// sine swings the fixed edge too and reads as a sheet of tin on a hinge.</summary>
    void Fill(float phase)
    {
        for (int s = 0; s <= Segments; s++)
        {
            float u = (float)s / Segments;
            float x = u * Length;
            float z = Mathf.Sin(phase - u * 6.0f) * 0.22f * u;
            // ⭐⭐ A VERTICAL SINE, not just a lift. Master: "we're missing a vertical sine on
            // em." There was a 0.05 nudge here that raised the whole strip together, which is a
            // flag being carried rather than a flag flapping. This travels along the length like
            // the horizontal one does, and the BOTTOM edge runs a quarter-wave behind the top so
            // the cloth twists instead of staying a rigid ribbon.
            float top = Mathf.Sin(phase * 1.3f - u * 4.5f) * VerticalAmplitude * u;
            float bottom = Mathf.Sin(phase * 1.3f - u * 4.5f - 1.6f) * VerticalAmplitude * u;
            _pos[s * 2] = new Vector3(x, top, z);
            _pos[s * 2 + 1] = new Vector3(x, bottom - Height, z * 0.75f);
        }
        _arrays[(int)Mesh.ArrayType.Vertex] = _pos;
    }
}
