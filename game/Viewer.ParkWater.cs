using System;
using Godot;
using TPW.PS2.Data;
using NativeWater = TPW.PS2.Data.ProceduralParkWater;

namespace TPWPS2Viewer;

public partial class Viewer
{
    ProceduralParkWaterView _nativeParkWater;
    NativeWater.Profile _nativeWaterProfile;
    NativeWater.NativeNoise _nativeWaterNoise;
    ulong _nativeWaterLastUsec;
    float _nativeWaterLodDepth;
    NativeWater.SurfaceBounds? _nativeWaterLodBounds;
    // Explicit reproducible port RNG stream. Native RNG arithmetic is used, but the game's
    // realized global seed/other consumers are not restored: no frame-exact phase claim.
    uint _nativeWaterSeed = 1;

    void ClearNativeParkWater()
    {
        if (_nativeParkWater != null && GodotObject.IsInstanceValid(_nativeParkWater))
        {
            _nativeParkWater.Visible = false; // queued free must not draw alongside its replacement
            _nativeParkWater.QueueFree();
        }
        _nativeParkWater = null;
        _nativeWaterLastUsec = 0;
        _nativeWaterLodBounds = null;
        _nativeWaterLodDepth = 0;
    }

    void LoadNativeParkWater(string terrainPath)
    {
        ClearNativeParkWater();
        // Controlled A/B switch, not a replacement shader or guessed motion mode.
        bool omitted = System.Environment.GetEnvironmentVariable("TPW_NATIVE_PARK_WATER") == "0";
        string diagnosticDepth = System.Environment.GetEnvironmentVariable("TPW_WATER_LOD_DEPTH");
        if (omitted && string.IsNullOrEmpty(diagnosticDepth)) return;
        if (!string.IsNullOrEmpty(diagnosticDepth)
            && (!float.TryParse(diagnosticDepth, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _nativeWaterLodDepth)
                || !float.IsFinite(_nativeWaterLodDepth) || _nativeWaterLodDepth <= 0 || _nativeWaterLodDepth > 1000))
            throw new InvalidOperationException("TPW_WATER_LOD_DEPTH must be finite in (0,1000]");
        if (_nativeWaterProfile == null)
        {
            _nativeWaterProfile = NativeWater.Profile.Read(_lib.Disc);
            _nativeWaterNoise = NativeWater.NativeNoise.Generate(ref _nativeWaterSeed);
            // 238cc8 generates a second 128xvec3 table after the one consumed by water.
            // Consume those calls too, without allocating an unused coefficient table.
            for (int i = 0; i < 384; i++) NativeWater.NextRandom(ref _nativeWaterSeed);
        }
        string worldName = Leaf(_lib.WadName).Replace(".WAD", "", StringComparison.OrdinalIgnoreCase);
        NativeWater.World world = worldName.ToUpperInvariant() switch
        {
            "JUNGLE" => NativeWater.World.Jungle, "HALLOW" => NativeWater.World.Hallow,
            "FANTASY" => NativeWater.World.Fantasy, "SPACE" => NativeWater.World.Space,
            _ => throw new InvalidOperationException("Native park water requires a known park world")
        };
        string terrain = Leaf(terrainPath).ToLowerInvariant();
        int variant = terrain == "terrain_1.mps" ? 1 : terrain == "terrain_2.mps" ? 2
            : throw new InvalidOperationException("Native park water requires terrain_1/2.mps");
        if (_nativeWaterLodDepth > 0)
        {
            _nativeWaterLodBounds = _nativeWaterProfile.Bounds(world, variant);
            GD.Print(FormattableString.Invariant($"[water-lod-view] declared diagnostic eye depth={_nativeWaterLodDepth:R}; camera only, not native player view"));
        }
        if (omitted) return;
        var source = ProceduralParkWaterView.ReadTexture(_lib);
        if (!source.SourcePath.Equals("/Generic/extra/justwater.ssh", StringComparison.OrdinalIgnoreCase)
            && !source.SourcePath.Equals("/Generic/extra/justwater.tga", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Native park water resolved an unexpected texture source");
        using var image = Image.CreateFromData(source.Width, source.Height, false, Image.Format.Rgba8, source.Pixels);
        image.GenerateMipmaps();
        var texture = ImageTexture.CreateFromImage(image);
        var state = NativeWater.State.Create(_nativeWaterProfile, ref _nativeWaterSeed);
        var view = new ProceduralParkWaterView();
        AddChild(view);
        try { view.Initialize(_nativeWaterNoise, state, world, variant, texture); }
        catch { view.QueueFree(); throw; }
        _nativeParkWater = view;
        _nativeWaterLastUsec = Time.GetTicksUsec(); // load/initialization time is not replayed
        GD.Print($"[park-water] loaded world={worldName} terrain={variant} texture={source.SourceWad}{source.SourcePath} "
            + $"alpha={source.Translucent} grid={view.Dimension} phase={state.Phase:F4} "
            + $"native-bounds={view.Bounds} seed=explicit-port-stream");
    }

    void StepNativeParkWater(double processDelta)
    {
        // Explicit camera-only diagnostic. Apply after the ordinary camera stage on BOTH A/B
        // sides, including omitted-water runs. No simulation/state/geometry fields are forced.
        if (_nativeWaterLodDepth > 0 && _nativeWaterLodBounds is { } diagnostic && !_lobbyMode && _cam != null)
        {
            var centre = new Vector3((diagnostic.MinX + diagnostic.MaxX) * .5f,
                diagnostic.BaseY, -(diagnostic.MinZ + diagnostic.MaxZ) * .5f);
            _cam.GlobalPosition = GlobalTransform * (centre + Vector3.Up * _nativeWaterLodDepth);
            _cam.LookAt(GlobalTransform * centre, GlobalTransform.Basis * Vector3.Back);
        }
        if (_nativeParkWater == null || !GodotObject.IsInstanceValid(_nativeParkWater)) return;
        // The decoded consumer uses gated elapsed real milliseconds, not Godot's scaled/capped
        // process delta. Sample a monotonic clock even when engine TimeScale is zero.
        ulong now = Time.GetTicksUsec();
        double elapsed = _nativeWaterLastUsec == 0 ? 0 : (now - _nativeWaterLastUsec) / 1000000.0;
        _nativeWaterLastUsec = now;
        bool shown = _mode == Mode.Park && !_lobbyMode && _terrain != null && _terrain.Root.Visible;
        _nativeParkWater.Visible = shown;
        // No time from a lobby/load freeze is replayed into a parked drawable on reentry.
        // Native laptop enter/exit 1c55c8/1c5680 clears/restores the same clock gate.
        // Keep drawing behind its panel but discard its elapsed time; do not catch up on close.
        bool running = _playing && !(_shopPanel?.Open ?? false);
        if (shown) _nativeParkWater.Step(elapsed, running, _cam);
    }
}