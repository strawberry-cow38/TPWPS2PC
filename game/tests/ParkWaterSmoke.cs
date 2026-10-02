using System.Reflection;
using Godot;
using TPW.PS2.Data;
using NativeWater = TPW.PS2.Data.ProceduralParkWater;

namespace TPWPS2Viewer.Tests;

/// <summary>Normal shipping Viewer startup, natural clock and actual Pause/filter/laptop
/// input. Optional reset fixture invokes ordinary LoadMap explicitly; no state writes,
/// process disabling, manual simulation ticks or fabricated actors. No emulator parity claim.</summary>
public partial class ParkWaterSmoke : Node
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Read<T>(object owner, string name) => (T)(owner.GetType().GetField(name, Hidden)
        ?? throw new MissingFieldException(name)).GetValue(owner);
    static object Call(object owner, string name, params object[] args) =>
        (owner.GetType().GetMethod(name, Hidden) ?? throw new MissingMethodException(name)).Invoke(owner, args);
    Viewer _viewer;
    int _checks;
    ulong _deadline;
    string _label;

    void Check(bool ok, string why)
    {
        if (!ok) throw new InvalidOperationException(why);
        _checks++;
        GD.Print($"{_label} ok: [{_checks}] {why}");
    }

    async Task Frames(int count = 3)
    {
        for (int i = 0; i < count; i++)
        {
            if (Time.GetTicksMsec() > _deadline) throw new TimeoutException("120s park-water deadline");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    async Task Wait(double seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        await Frames();
    }

    void KeyEvent(Key key, bool pressed)
    {
        using var ev = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed };
        Input.ParseInputEvent(ev); Input.FlushBufferedEvents();
    }

    async Task KeyTap(Key key)
    {
        KeyEvent(key, true); await Frames(); KeyEvent(key, false); await Frames();
    }

    async Task Click(Vector2 at)
    {
        GetViewport().WarpMouse(at);
        using (var motion = new InputEventMouseMotion { Position = at, GlobalPosition = at })
        { Input.ParseInputEvent(motion); Input.FlushBufferedEvents(); }
        await Frames();
        foreach (bool down in new[] { true, false })
        {
            using var ev = new InputEventMouseButton { Position = at, GlobalPosition = at,
                ButtonIndex = MouseButton.Left, Pressed = down,
                ButtonMask = down ? MouseButtonMask.Left : (MouseButtonMask)0 };
            Input.ParseInputEvent(ev); Input.FlushBufferedEvents(); await Frames();
        }
    }

    ProceduralParkWaterView Water => Read<ProceduralParkWaterView>(_viewer, "_nativeParkWater");

    void VerifyLoaded(int expectedMap)
    {
        var maps = Read<List<(string Wad, string Path, string Label)>>(_viewer, "_maps");
        Check(Read<int>(_viewer, "_loadedMap") == expectedMap, "normal loader owns expected map");
        var water = Water;
        Check(water != null && IsInstanceValid(water), "shipping terrain load creates procedural water");
        Check(water.GetParent() == _viewer && water.IsVisibleInTree(), "shipping water attached and visible");
        Check(_viewer.GetChildren().OfType<ProceduralParkWaterView>().Count() == 1, "exactly one live water drawable");
        string world = maps[expectedMap].Wad.Split('/').Last().Replace(".WAD", "").ToUpperInvariant();
        int variant = maps[expectedMap].Path.EndsWith("terrain_2.mps", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        int worldIndex = Array.IndexOf(new[] { "JUNGLE", "HALLOW", "FANTASY", "SPACE" }, world);
        float[] low = { 17, 17, 31, 35, 25, 27, 23, 35 };
        float[] high = { 42, 42, 56, 60, 50, 52, 48, 60 };
        int index = worldIndex * 2 + variant - 1;
        Check(index >= 0 && water.Bounds.MinX == low[index] && water.Bounds.MaxX == high[index]
            && water.Bounds.MinZ == -15 && Math.Abs(water.Bounds.MaxZ - 6.4f) < .00001,
            "literal native placement for selected world and variant");
        Check(water.Dimension >= 4 && water.Dimension <= 16, "shipping projected detail stays within native bounds");
        Check(water.Surface.Mesh.GetSurfaceCount() == 1 && water.Material.GetShaderParameter("has_tex").AsBool(),
            "shipping mesh has bound art");
        Check(water.Material.Shader.Code.Contains("ALPHA = c.a;"), "shipping material retains soft alpha");
        var terrain = Read<AnimatedModel>(_viewer, "_terrain");
        var sea = terrain.Root.GetChildren().OfType<MeshInstance3D>()
            .Where(n => n.Name.ToString().StartsWith("A_SEA", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var seaMesh in sea.Where(m => m.Name.ToString().StartsWith("A_SEA_02", StringComparison.OrdinalIgnoreCase)
            || m.Name.ToString().StartsWith("A_SEA_04", StringComparison.OrdinalIgnoreCase)))
        {
            var localBounds = seaMesh.Mesh.GetAabb();
            var transformedBounds = seaMesh.GlobalTransform * localBounds;
            GD.Print($"[water-footprint] {seaMesh.Name} godot-world={transformedBounds} native-grid={water.Bounds}");
        }
        Check(sea.Length >= 6, "opaque authored sea remains beside procedural layer");
        Check(sea.All(n => n.MaterialOverride is ShaderMaterial m
            && m.GetShaderParameter("uv_scroll").AsVector2() == Vector2.Zero
            && m.GetShaderParameter("water_wave").AsVector3() == Vector3.Zero),
            "base sea has neither guessed scroll nor guessed sine");
    }

    async Task VerifyMotion()
    {
        var water = Water;
        float phase = water.State.Phase;
        int advanced = water.AdvancedMilliseconds;
        int oldN = water.Dimension;
        var before = ((ArrayMesh)water.Surface.Mesh).SurfaceGetArrays(0);
        var oldPositions = before[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var oldUvs = before[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        await Wait(.16);
        Check(water.State.Phase > phase && water.AdvancedMilliseconds > advanced,
              "natural shipping process advances native water clock");
        var mesh = (ArrayMesh)water.Surface.Mesh;
        var arrays = mesh.SurfaceGetArrays(0);
        var positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        // Normalized grid corners correspond even if legitimate camera LOD changes N.
        int n = water.Dimension;
        int[] oldCorners = { 0, oldN - 1, oldN * (oldN - 1), oldN * oldN - 1 };
        int[] newCorners = { 0, n - 1, n * (n - 1), n * n - 1 };
        Check(newCorners.Where((i, k) => Math.Abs(positions[i].Y - oldPositions[oldCorners[k]].Y) > .000001).Any(),
              "natural shipping wait changes actual uploaded heights");
        Check(Math.Abs(uv[0].Y - oldUvs[0].Y) > .000001,
              "natural shipping wait changes actual uploaded signed V");
        Check(positions.Length == NativeWater.VertexCount(water.Dimension) && uv.Length == positions.Length,
              "shipping uploaded arrays match current grid");
        Check(positions.Select((p, i) => Math.Abs(p.X - water.Positions[i].X) < .00001
            && Math.Abs(p.Y - water.Positions[i].Y) < .00001 && Math.Abs(p.Z + water.Positions[i].Z) < .00001).All(v => v),
              "shipping GPU-backed positions agree with native geometry and Z reflection");
        Check(uv.Select((p, i) => Math.Abs(p.X - water.Uvs[i].X) < .00001 && Math.Abs(p.Y - water.Uvs[i].Y) < .00001).All(v => v),
              "shipping GPU-backed UVs agree with signed native narrowing");
        Check(water.Surface.CustomAabb.Size.X > 20 && water.Surface.CustomAabb.Size.Z > 20,
              "dynamic culling bounds include main grid and appended geometry");
    }

    async Task Retire()
    {
        if (_viewer == null || !IsInstanceValid(_viewer)) return;
        Call(_viewer, "ResetNativeBus"); Call(_viewer, "StopMusic");
        Read<RideSounds>(_viewer, "_sounds")?.Clear();
        var ownedWater = Water;
        _viewer.QueueFree(); await Frames(2);
        await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
        Check(!IsInstanceValid(_viewer), "normal Viewer teardown frees water owner");
        Check(ownedWater != null && !IsInstanceValid(ownedWater), "normal Viewer teardown frees actual water instance");
        _viewer = null;
    }

    public override async void _Ready()
    {
        _deadline = Time.GetTicksMsec() + 120000;
        var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
        bool reset = args.Contains("--water-reset-fixture");
        _label = reset ? "PARK WATER RESET SMOKE" : "PARK WATER SMOKE";
        int exit = 2;
        try
        {
            Check(DisplayServer.GetName() != "headless", "rendering display required");
            Check((args.Any(a => a.StartsWith("--disc=")) || !string.IsNullOrWhiteSpace(OS.GetEnvironment("TPW_PS2_DISC")))
                && args.Any(a => a.StartsWith("--map=")) && args.Contains("--mode=park"),
                  "explicit ordinary Viewer startup arguments");
            Check(System.Environment.GetEnvironmentVariable("TPW_NATIVE_PARK_WATER") != "0", "no water omission override");
            _viewer = new Viewer { Name = "ShippingWaterViewer" }; AddChild(_viewer);
            ulong loadedBy = Time.GetTicksMsec() + 30000;
            while ((Read<int>(_viewer, "_loadedMap") < 0 || Read<Model>(_viewer, "_terrainModel") == null)
                && Time.GetTicksMsec() < loadedBy) await Frames();
            await Frames(6);
            int map = Read<int>(_viewer, "_loadedMap");
            Check(map >= 0, "normal Viewer startup completes");
            VerifyLoaded(map); await VerifyMotion();
            var firstWater = Water;
            ulong firstId = firstWater.GetInstanceId();
            await KeyTap(Godot.Key.Pause);
            Check(!Read<bool>(_viewer, "_playing"), "real Pause key pauses shipping process");
            float stoppedPhase = firstWater.State.Phase;
            long stoppedUv = firstWater.State.UvAccumulator;
            await Wait(.14);
            Check(firstWater.State.Phase == stoppedPhase && firstWater.State.UvAccumulator == stoppedUv,
                  "shipping water stays still while paused");
            await KeyTap(Godot.Key.Pause);
            Check(Read<bool>(_viewer, "_playing"), "real Pause key resumes shipping process");
            await Wait(.12);
            Check(firstWater.State.Phase > stoppedPhase, "shipping water resumes after input pause");
            await KeyTap(Godot.Key.L);
            Check(Water.Material.Shader.Code.Contains("filter_nearest_mipmap"), "real filter toggle reaches procedural material");
            await KeyTap(Godot.Key.L);
            Check(Water.Material.Shader.Code.Contains("filter_linear_mipmap"), "real filter toggle restores procedural material");

            if (reset)
            {
                var maps = Read<List<(string Wad, string Path, string Label)>>(_viewer, "_maps");
                int other = maps.FindIndex(m => m.Wad.EndsWith("FANTASY.WAD", StringComparison.OrdinalIgnoreCase)
                    && m.Path.EndsWith("terrain_2.mps", StringComparison.OrdinalIgnoreCase));
                Check(other >= 0 && other != map, "declared reset fixture selects distinct FANTASY/2");
                var previousState = firstWater.State;
                float previousPhase = previousState.Phase;
                uint phaseSeed = Read<uint>(_viewer, "_nativeWaterSeed");
                float expectedPhase = (NativeWater.NextRandom(ref phaseSeed) & 0x3fff) / 100f;
                Call(_viewer, "LoadMap", other); // explicitly declared normal-loader component operation
                Check(Water != null && Water.GetInstanceId() != firstId && Water.State.UvAccumulator == 4096
                    && Water.AdvancedMilliseconds == 0, "map load creates fresh owner and initial UV/clock state");
                Check(!ReferenceEquals(Water.State, previousState) && Water.State.Phase == expectedPhase
                    && previousState.Phase == previousPhase, "map load creates independent state with fresh native initial phase");
                Check(!firstWater.Visible, "old water hidden immediately before queued deletion");
                await Frames(3);
                Check(!IsInstanceValid(firstWater), "old water instance freed on map switch");
                VerifyLoaded(other); await VerifyMotion();
            }
            string shot = System.Environment.GetEnvironmentVariable("TPW_WATER_VIEWER_SHOT");
            if (!string.IsNullOrEmpty(shot))
            {
                await KeyTap(Godot.Key.F3); await Frames(3);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage(); image.SavePng(shot);
            }
            await KeyTap(Godot.Key.Tab); await Wait(.1);
            var panel = Read<LaptopShopScreen>(_viewer, "_shopPanel");
            Check(panel != null && panel.Open, "real Tab opens laptop");
            var rows = Read<List<string>>(panel, "_menu");
            int close = rows.FindIndex(r => r.Contains("Close Park", StringComparison.OrdinalIgnoreCase));
            Check(close >= 0, "real laptop offers Close Park");
            var box = panel.MenuRowScreenBox(close);
            Check(box.Size.X > 0 && box.Size.Y > 0, "Close Park has drawn hitbox");
            var closingWater = Water;
            await Click(box.GetCenter()); await Frames();
            Check(Read<bool>(_viewer, "_lobbyMode"), "real Close Park input opens lobby");
            Check(!closingWater.IsVisibleInTree(), "park water hidden in lobby");
            float hiddenPhase = closingWater.State.Phase;
            await Wait(.12);
            Check(closingWater.State.Phase == hiddenPhase, "hidden park drawable does not run behind lobby");
            exit = 0;
        }
        catch (Exception ex) { GD.PrintErr($"{_label} FAIL checks={_checks}: {ex}"); }
        finally
        {
            try { await Retire(); } catch (Exception ex) { exit = 2; GD.PrintErr($"{_label} FAIL cleanup: {ex}"); }
        }
        if (exit == 0) GD.Print($"{_label} PASS checks={_checks}; shipping Viewer with explicit loader reset only when flagged; not emulator parity");
        GetTree().Quit(exit);
    }
}