using Godot;
using System;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>Rain and snow, from the two sprites the game keeps in DATA.WAD.
///
/// ⭐ `0x149958` loads them per park through
/// `FUN_0023d028(obj, "data/generic/weather/", "raindrop.ssh", "snowflake.ssh")` — so the engine
/// names exactly these two and no others, and both sit in `/Generic/Weather/` with `.tga` twins
/// beside the `.ssh`. The `.tga` is used here because SSH pixels are not decoded yet.
///
/// ⚠ CpuParticles3D, NOT GpuParticles3D. GPU particles do not render in an offline `--shot`
/// capture, so every verification render would come back empty and the fault would look like the
/// particles never emitting.</summary>
public sealed class Weather
{
    public enum Kind { None, Rain, Snow }

    public readonly Node3D Root = new() { Name = "Weather" };
    CpuParticles3D _p;
    Kind _kind = Kind.None;
    public Kind Current => _kind;

    /// <summary>How far across the camera the volume reaches. The park is ~64-96 cells and the
    /// camera sees about 20 of them, so the weather only has to cover what is in frame.</summary>
    const float Span = 40f;
    /// <summary>How high the drops start above the plot.
    ///
    /// ⚠⚠ THIS IS TIED TO THE LIFETIME AND GETTING IT WRONG LOOKS LIKE NOTHING EMITTING. At the
    /// first attempt the rain fell 37 units inside its 1.1 second life from a start of 11, so every
    /// drop was underground within a fifth of a second and only the ones near the horizon -- where
    /// the ground is far away -- ever appeared. The system was working the whole time; cranking the
    /// scale to ten proved that before I started changing anything else.</summary>
    const float Height = 13f;

    public string Set(AssetLibrary lib, Kind kind, Vector3 eye)
    {
        _kind = kind;
        // ⚠⚠ THE VOLUME MUST BE OVER THE CAMERA BEFORE THE PARTICLES EXIST. CpuParticles3D emits
        // in GLOBAL space, so Preprocess fills it at whatever transform the node has when it enters
        // the tree, and moving the parent afterwards leaves that whole batch behind. Building at the
        // origin and then following put a 40-unit volume of rain around (0,0) while the camera sat
        // 30 units away looking the other way -- which reads exactly like nothing emitting, and is
        // why I got two empty renders before checking where the drops actually were.
        Follow(eye);
        if (_p != null) { _p.QueueFree(); _p = null; }
        if (kind == Kind.None) return "off";

        var file = kind == Kind.Rain ? "Weather/Raindrop.tga" : "Weather/Snowflake.TGA";
        var raw = lib.ReadGeneric(file);
        if (raw == null) return $"{file} not in DATA.WAD";

        Targa tga;
        try { tga = new Targa(raw); }
        catch (Exception ex) { return $"{file}: {ex.Message}"; }
        var img = Image.CreateFromData(tga.Width, tga.Height, false, Image.Format.Rgba8, tga.Pixels);
        var tex = ImageTexture.CreateFromImage(img);

        bool rain = kind == Kind.Rain;
        _p = new CpuParticles3D
        {
            Amount = rain ? 2200 : 800,
            // Rain crosses the 13 units from the top of the volume to the ground in about this
            // long at the velocity below, so a drop is visible for all of its life and none of it.
            Lifetime = rain ? 0.95f : 9f,
            Preprocess = rain ? 0.95f : 9f,         // ⚠ or the first frame is empty sky
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(Span * 0.5f, 0.5f, Span * 0.5f),
            Direction = Vector3.Down,
            Spread = rain ? 2f : 18f,
            Gravity = new Vector3(0, rain ? -9f : -1.1f, 0),
            InitialVelocityMin = rain ? 9f : 0.4f,
            InitialVelocityMax = rain ? 11f : 0.9f,
            ScaleAmountMin = rain ? 0.55f : 0.5f,
            ScaleAmountMax = rain ? 0.95f : 0.9f,
            // ⚠ A particle system is culled by ITS OWN box, which Godot cannot infer for CPU
            // particles. Left alone the whole system vanishes the moment its origin leaves the
            // frustum -- which, since it follows the camera, is most of the time.
            VisibilityAabb = new Aabb(new Vector3(-Span, -Height, -Span),
                                      new Vector3(Span * 2, Height * 2, Span * 2)),
        };
        if (!rain)
        {
            // Snow wanders; rain does not.
            _p.DampingMin = 0.4f;
            _p.DampingMax = 0.9f;
            _p.TangentialAccelMin = -1.2f;
            _p.TangentialAccelMax = 1.2f;
        }
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = tex,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            // ⚠⚠ BillboardMode.Particles THROWS AWAY ScaleAmount unless this is set. Without it
            // every drop renders at one unit and the screen fills with paper.
            BillboardKeepScale = true,
            VertexColorUseAsAlbedo = true,
            DisableReceiveShadows = true,
        };
        _p.Mesh = new QuadMesh { Size = rain ? new Vector2(0.05f, 0.55f) : new Vector2(0.22f, 0.22f) };
        _p.MaterialOverride = mat;
        // ⚠ Position BEFORE AddChild: a CpuParticles3D preprocesses at the transform it has when
        // it enters the tree, so adding first and moving after starts the whole volume at origin.
        _p.Position = new Vector3(0, Height, 0);
        Root.AddChild(_p);
        return $"{file} {tga.Width}x{tga.Height}, {_p.Amount} particles";
    }

    /// <summary>Keep the volume over the camera. ⚠ Snapped to whole units so the emitter does not
    /// slide under the particles it already made, which reads as the rain drifting sideways.</summary>
    public void Follow(Vector3 eye)
    {
        Root.Position = new Vector3(Mathf.Round(eye.X), 0f, Mathf.Round(eye.Z));
    }
}
