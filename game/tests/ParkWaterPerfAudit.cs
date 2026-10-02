using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace TPWPS2Viewer.Tests;

/// <summary>Bounded same-build water A/B with ordinary Viewer startup and natural frames.
/// Only the documented load-time omission switch changes; no field writes/manual ticks.
/// Uses the smokes' normal audio/node retirement, not Benchmark's immediate quit path.</summary>
public partial class ParkWaterPerfAudit : Node
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Read<T>(object owner, string name) => (T)(owner.GetType().GetField(name, Hidden)
        ?? throw new MissingFieldException(name)).GetValue(owner);
    static object Call(object owner, string name, params object[] args) =>
        (owner.GetType().GetMethod(name, Hidden) ?? throw new MissingMethodException(name)).Invoke(owner, args);
    const int Capacity = 65536;
    readonly double[] _frames = new double[Capacity], _draws = new double[Capacity];
    int _samples;
    ulong _lastUsec, _stopUsec, _endUsec;
    bool _overflow, _cameraChanged;
    Camera3D _camera;
    Transform3D _cameraTransform;
    Projection _cameraProjection;
    Viewer _viewer;
    ProceduralParkWaterView _water;
    int _gridMin = int.MaxValue, _gridMax;
    float _clipMin = float.PositiveInfinity, _clipMax = float.NegativeInfinity;
    bool _record;
    int _checks;
    ulong _deadline;

    void Check(bool ok, string receipt)
    {
        if (!ok) throw new InvalidOperationException(receipt);
        _checks++;
        GD.Print($"PARK WATER PERF ok: [{_checks}] {receipt}");
    }

    public override void _Process(double delta)
    {
        if (!_record) return;
        ulong now = Time.GetTicksUsec();
        if (_samples == Capacity) { _overflow = true; _record = false; return; }
        _frames[_samples] = (now - _lastUsec) / 1000.0;
        _draws[_samples++] = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        _lastUsec = now;
        if (_water != null && IsInstanceValid(_water))
        {
            _gridMin = Math.Min(_gridMin, _water.Dimension); _gridMax = Math.Max(_gridMax, _water.Dimension);
            _clipMin = Math.Min(_clipMin, _water.ClosestClipZ); _clipMax = Math.Max(_clipMax, _water.ClosestClipZ);
        }
        _cameraChanged |= _camera.GlobalTransform != _cameraTransform || _camera.GetCameraProjection() != _cameraProjection;
        if (now >= _stopUsec) { _endUsec = now; _record = false; }
    }

    async Task Frames(int count = 3)
    {
        for (int i = 0; i < count; i++)
        {
            if (Time.GetTicksMsec() > _deadline) throw new TimeoutException("90s water performance deadline");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    async Task Wait(double seconds)
    {
        ulong until = Time.GetTicksUsec() + (ulong)(seconds * 1000000);
        while (Time.GetTicksUsec() < until) await Frames(1);
    }

    double Percentile(double[] values, double q)
    {
        var ordered = values.Take(_samples).OrderBy(x => x).ToArray();
        return ordered[Math.Clamp((int)Math.Ceiling(q * ordered.Length) - 1, 0, ordered.Length - 1)];
    }

    static string CameraKey(Camera3D camera)
    {
        var t = camera.GlobalTransform; var p = camera.GetCameraProjection();
        Span<float> values = stackalloc float[] {
            t.Basis.X.X, t.Basis.X.Y, t.Basis.X.Z, t.Basis.Y.X, t.Basis.Y.Y, t.Basis.Y.Z,
            t.Basis.Z.X, t.Basis.Z.Y, t.Basis.Z.Z, t.Origin.X, t.Origin.Y, t.Origin.Z,
            p.X.X, p.X.Y, p.X.Z, p.X.W, p.Y.X, p.Y.Y, p.Y.Z, p.Y.W,
            p.Z.X, p.Z.Y, p.Z.Z, p.Z.W, p.W.X, p.W.Y, p.W.Z, p.W.W };
        return Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(values)));
    }

    public override async void _Ready()
    {
        _deadline = Time.GetTicksMsec() + 90000;
        int exit = 2;
        ProceduralParkWaterView water = null;
        bool enabled = System.Environment.GetEnvironmentVariable("TPW_NATIVE_PARK_WATER") != "0";
        try
        {
            Check(DisplayServer.GetName() != "headless", "rendered performance display");
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            Check(args.Contains("--map=JUNGLE  terrain_1.mps") && args.Contains("--mode=park")
                && !args.Any(a => a.StartsWith("--benchmark=")), "normal explicit startup without immediate-quit benchmark");
            _viewer = new Viewer { Name = "WaterPerformanceViewer" }; AddChild(_viewer);
            while (Read<int>(_viewer, "_loadedMap") < 0 || Read<TPW.PS2.Data.Model>(_viewer, "_terrainModel") == null)
                await Frames();
            await Frames(6);
            Check(Read<int>(_viewer, "_loadedMap") >= 0, "normal Viewer startup completes");
            var maps = Read<List<(string Wad, string Path, string Label)>>(_viewer, "_maps");
            var map = maps[Read<int>(_viewer, "_loadedMap")];
            Check(map.Wad.EndsWith("JUNGLE.WAD", StringComparison.OrdinalIgnoreCase)
                && map.Path.EndsWith("terrain_1.mps", StringComparison.OrdinalIgnoreCase), "actual JUNGLE/1 performance map");
            water = Read<ProceduralParkWaterView>(_viewer, "_nativeParkWater");
            Check(enabled ? water != null && IsInstanceValid(water) : water == null, "load-time water switch matches experiment");
            Check(enabled ? water.IsVisibleInTree() : !_viewer.GetChildren().OfType<ProceduralParkWaterView>().Any(),
                "experiment draws water or omits only its drawable");
            _water = water;
            await Wait(3); // Same warm-up as the existing benchmark; excluded from samples.
            _camera = Read<Camera3D>(_viewer, "_cam");
            Check(_camera != null && _camera.Current, "current performance camera is bound");
            _cameraTransform = _camera.GlobalTransform; _cameraProjection = _camera.GetCameraProjection();
            string cameraKey = CameraKey(_camera);
            int advanced = water?.AdvancedMilliseconds ?? 0;
            long alloc = GC.GetAllocatedBytesForCurrentThread();
            ulong began = Time.GetTicksUsec();
            _lastUsec = began; _stopUsec = began + 9000000;
            _record = true;
            while (_record) await Frames(1);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - alloc;
            double wall = (_endUsec - began) / 1000000.0;
            Check(!_overflow && _samples >= 20 && _frames.Take(_samples).All(x => double.IsFinite(x) && x > 0), "finite natural-frame measurement samples");
            Check(wall >= 8.5 && wall <= 12, "bounded monotonic measurement excludes loading and warm-up");
            Check(!_cameraChanged && CameraKey(_camera) == cameraKey, "camera transform and projection remain fixed throughout measurement");
            Check(enabled ? IsInstanceValid(water) && water.AdvancedMilliseconds > advanced + 1000
                : Read<ProceduralParkWaterView>(_viewer, "_nativeParkWater") == null,
                "water advances normally or remains absent throughout measurement");
            GD.Print(FormattableString.Invariant($"PARK WATER PERF RESULT enabled={(enabled ? 1 : 0)} samples={_samples} median_ms={Percentile(_frames, .5):F4} p95_ms={Percentile(_frames, .95):F4} draw_calls={Percentile(_draws, .5):F1} allocated_bytes_per_frame={(double)allocated / _samples:F1} wall_seconds={wall:F6} camera={cameraKey} grid_min={(enabled ? _gridMin : 0)} grid_max={(enabled ? _gridMax : 0)} clip_min={(enabled ? _clipMin : 0):F6} clip_max={(enabled ? _clipMax : 0):F6}"));
            exit = 0;
        }
        catch (Exception ex) { GD.PrintErr($"PARK WATER PERF FAIL checks={_checks}: {ex}"); }
        finally
        {
            _record = false;
            try
            {
                if (_viewer != null && IsInstanceValid(_viewer))
                {
                    Call(_viewer, "ResetNativeBus"); Call(_viewer, "StopMusic");
                    Read<RideSounds>(_viewer, "_sounds")?.Clear();
                    _viewer.QueueFree(); await Frames(2);
                    await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
                    Check(!IsInstanceValid(_viewer), "normal teardown retires actual Viewer");
                    Check(water == null || !IsInstanceValid(water), "normal teardown retires actual water");
                    Check(!GetChildren().OfType<Viewer>().Any(), "no performance Viewer remains");
                }
            }
            catch (Exception ex) { exit = 2; GD.PrintErr($"PARK WATER PERF FAIL cleanup: {ex}"); }
        }
        if (exit == 0) GD.Print($"PARK WATER PERF PASS checks={_checks}; normal startup and natural frames, bounded same-build A/B");
        GetTree().Quit(exit);
    }
}