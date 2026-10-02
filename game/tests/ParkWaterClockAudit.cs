using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace TPWPS2Viewer.Tests;

/// <summary>Declared engine-clock control: normal Viewer, temporarily zero engine TimeScale,
/// monotonic waits, then real Pause input. Not ordinary player/emulator proof.</summary>
public partial class ParkWaterClockAudit : Node
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Read<T>(object owner, string name) => (T)(owner.GetType().GetField(name, Hidden)
        ?? throw new MissingFieldException(name)).GetValue(owner);
    static object Call(object owner, string name, params object[] args) =>
        (owner.GetType().GetMethod(name, Hidden) ?? throw new MissingMethodException(name)).Invoke(owner, args);
    Viewer _viewer;
    int _checks, _zeroFrames;
    bool _observing;
    double _deltaSum;
    ulong _deadline;

    public override void _Process(double delta)
    {
        if (!_observing) return;
        _deltaSum += delta;
        if (delta == 0) _zeroFrames++;
    }

    void Check(bool ok, string receipt)
    {
        if (!ok) throw new InvalidOperationException(receipt);
        GD.Print($"PARK WATER CLOCK ok: [{++_checks}] {receipt}");
    }

    async Task Frames(int n = 3)
    {
        while (n-- > 0)
        {
            if (Time.GetTicksMsec() > _deadline) throw new TimeoutException("90s water-clock deadline");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    async Task WallWait(int milliseconds)
    {
        ulong until = Time.GetTicksMsec() + (uint)milliseconds;
        while (Time.GetTicksMsec() < until) await Frames(1);
    }

    async Task PauseKey()
    {
        foreach (bool down in new[] { true, false })
        {
            using var ev = new InputEventKey { PhysicalKeycode = Key.Pause, Keycode = Key.Pause, Pressed = down };
            Input.ParseInputEvent(ev); Input.FlushBufferedEvents(); await Frames();
        }
    }

    public override async void _Ready()
    {
        _deadline = Time.GetTicksMsec() + 90000;
        var oldScale = Engine.TimeScale;
        ProceduralParkWaterView water = null;
        int exit = 2;
        try
        {
            Check(DisplayServer.GetName() != "headless", "rendered clock-control display");
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            Check(args.Contains("--map=JUNGLE  terrain_1.mps") && args.Contains("--mode=park")
                && System.Environment.GetEnvironmentVariable("TPW_NATIVE_PARK_WATER") != "0", "explicit normal clock-control startup");
            _viewer = new Viewer { Name = "WaterClockViewer" }; AddChild(_viewer);
            while (Read<int>(_viewer, "_loadedMap") < 0 || Read<TPW.PS2.Data.Model>(_viewer, "_terrainModel") == null)
                await Frames();
            await Frames(6);
            Check(Read<int>(_viewer, "_loadedMap") >= 0, "normal Viewer startup completes");
            water = Read<ProceduralParkWaterView>(_viewer, "_nativeParkWater");
            Check(water != null && IsInstanceValid(water) && water.IsVisibleInTree(), "shipping water exists and is visible");
            Check(Read<bool>(_viewer, "_playing"), "normal water running gate is enabled");
            Check(oldScale == 1, "clock fixture begins with ordinary engine scale");
            Engine.TimeScale = 0; // declared public-engine control, never a Viewer field write
            await Frames(2); // settle the already-computed frame delta before observing
            var oldArrays = ((ArrayMesh)water.Surface.Mesh).SurfaceGetArrays(0);
            float oldY = oldArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array()[0].Y;
            float oldV = oldArrays[(int)Mesh.ArrayType.TexUV].AsVector2Array()[0].Y;
            int before = water.AdvancedMilliseconds;
            float phase = water.State.Phase;
            ulong began = Time.GetTicksMsec();
            _observing = true;
            await WallWait(600);
            _observing = false;
            ulong measured = Time.GetTicksMsec() - began;
            int advanced = water.AdvancedMilliseconds - before;
            Check(_zeroFrames >= 3 && _deltaSum == 0, "engine process delta actually becomes zero");
            Check(measured >= 600 && measured < 5000, "monotonic clock-control window is bounded");
            Check(advanced >= (long)measured - 150 && advanced <= (long)measured + 150,
                  "native wall clock advances despite zero engine delta");
            Check(water.State.Phase > phase, "native phase advances despite zero engine delta");
            var arrays = ((ArrayMesh)water.Surface.Mesh).SurfaceGetArrays(0);
            Check(Math.Abs(arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array()[0].Y - oldY) > .000001,
                  "actual uploaded height moves despite zero engine delta");
            Check(Math.Abs(arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array()[0].Y - oldV) > .000001,
                  "actual uploaded V moves despite zero engine delta");
            GD.Print($"[water-clock] real_ms={measured} native_ms={advanced} engine_delta={_deltaSum} zero_frames={_zeroFrames}");
            Engine.TimeScale = oldScale;
            await PauseKey();
            Check(!Read<bool>(_viewer, "_playing"), "real Pause key still gates native clock");
            before = water.AdvancedMilliseconds; phase = water.State.Phase;
            await WallWait(160);
            Check(water.AdvancedMilliseconds == before && water.State.Phase == phase, "wall time is discarded while input-paused");
            await PauseKey();
            Check(Read<bool>(_viewer, "_playing"), "real Pause key resumes native clock");
            before = water.AdvancedMilliseconds;
            await WallWait(160);
            Check(water.AdvancedMilliseconds > before, "native clock resumes without replaying paused time");
            exit = 0;
        }
        catch (Exception ex) { GD.PrintErr($"PARK WATER CLOCK FAIL checks={_checks}: {ex}"); }
        finally
        {
            _observing = false; Engine.TimeScale = oldScale;
            try
            {
                if (_viewer != null && IsInstanceValid(_viewer))
                {
                    Call(_viewer, "ResetNativeBus"); Call(_viewer, "StopMusic");
                    Read<RideSounds>(_viewer, "_sounds")?.Clear();
                    _viewer.QueueFree(); await Frames(2);
                    await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
                    Check(!IsInstanceValid(_viewer), "clock-control Viewer retired normally");
                    Check(water != null && !IsInstanceValid(water), "clock-control actual water retired normally");
                }
            }
            catch (Exception ex) { exit = 2; GD.PrintErr($"PARK WATER CLOCK FAIL cleanup: {ex}"); }
        }
        if (exit == 0) GD.Print($"PARK WATER CLOCK PASS checks={_checks}; declared engine-clock control, not emulator/player parity");
        GetTree().Quit(exit);
    }
}