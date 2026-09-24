using System.Reflection;
using Godot;
using TPW.PS2.Data;
using Matrix = System.Numerics.Matrix4x4;

namespace TPWPS2Viewer.Tests;

/// <summary>Rendering-display regression for presentation through actual Viewer.StepPark.
/// Use normal --disc=... --map=JUNGLE --mode=park startup. No synthetic bus or shop.</summary>
public partial class BusPresentationSmoke : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(Viewer v, string name) => (T)(typeof(Viewer).GetField(name, Hidden)
        ?? throw new MissingMemberException("Viewer." + name)).GetValue(v);
    static object Call(Viewer v, string name, params object[] args)
    {
        var method = typeof(Viewer).GetMethod(name, Hidden)
            ?? throw new MissingMemberException("Viewer." + name);
        var parameters = method.GetParameters();
        if (args.Length < parameters.Length)
            args = args.Concat(parameters.Skip(args.Length).Select(p => p.HasDefaultValue ? p.DefaultValue
                : throw new ArgumentException("Missing argument: " + name))).ToArray();
        return method.Invoke(v, args);
    }
    int _checks;
    void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        _checks++;
    }
    static IEnumerable<MeshInstance3D> Meshes(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is MeshInstance3D mesh) yield return mesh;
            foreach (var nested in Meshes(child)) yield return nested;
        }
    }
    static object Snapshot(Viewer v, NativeBus bus) => (
        Field<int>(v, "_parkTicks"), bus.Controller.State, bus.Controller.AppliedState,
        bus.Controller.Frame, bus.Controller.OuterRemaining, bus.Controller.DwellRemaining,
        bus.Controller.EndHold, bus.Controller.RejectedCommands,
        Field<int>(v, "_busBatches"), Field<int>(v, "_busAdmitted"));
    static Vector3 Position(Matrix m) => new(m.M41, m.M42, m.M43);

    public override async void _Ready()
    {
        Viewer viewer = null;
        string world = "unknown";
        int exit = 2;
        try
        {
            Check(DisplayServer.GetName() != "headless", "rendering display required");
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            string map = args.LastOrDefault(a => a.StartsWith("--map="))?[6..];
            string mode = args.LastOrDefault(a => a.StartsWith("--mode="))?[7..];
            Check(map != null && (map.Equals("JUNGLE", StringComparison.OrdinalIgnoreCase)
                || map.StartsWith("JUNGLE ", StringComparison.OrdinalIgnoreCase)), "pass --map=JUNGLE");
            world = "JUNGLE";
            Check(string.Equals(mode, "park", StringComparison.OrdinalIgnoreCase), "pass --mode=park");
            Check(!args.Any(a => a.StartsWith("--shot=") || a.Contains("-film=")
                || a.StartsWith("--sound-census=") || a.EndsWith("-test")
                || a.EndsWith("-audit") || a == "--ghost-press")
                && string.IsNullOrWhiteSpace(OS.GetEnvironment("TPW_PS2_SHOT")), "normal startup only");
            viewer = new Viewer { Name = "Viewer" };
            AddChild(viewer);
            viewer.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var library = Field<AssetLibrary>(viewer, "_lib");
            Check(Field<Park>(viewer, "_park")?.Field != null && library != null
                && Field<Model>(viewer, "_terrainModel")?.Field != null
                && Field<ParkEntrance>(viewer, "_entranceTable") != null, "normalReady");
            Check(Field<object>(viewer, "_mode").ToString() == "Park"
                && string.Equals(System.IO.Path.GetFileNameWithoutExtension(library.WadName), world,
                    StringComparison.OrdinalIgnoreCase), "normal requested park loaded");
            Check(Field<int>(viewer, "_parkTicks") == 0, "scheduler stopped before first tick");
            NativeBus bus = null;
            for (int i = 0; i < 80; i++)
            {
                int ticks = Field<int>(viewer, "_parkTicks");
                Call(viewer, "StepPark", .04);
                Check(Field<int>(viewer, "_parkTicks") == ticks + 1, "warmup executes one tick");
                bus = Field<NativeBus>(viewer, "_nativeBus");
                if (bus?.Controller.AppliedState == 1 && bus.Controller.Frame >= 30) break;
                if (i % 8 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Check(bus != null && bus.Controller.AppliedState == 1
                && bus.Controller.Frame >= 30 && bus.Controller.Frame < 32
                && Field<int>(viewer, "_parkTicks") <= 80, "approach around frame30 within80 ticks");
            var source = Field<Model>(viewer, "_nativeBusMesh");
            int bodyOffset = source.NodeOffset(0);
            string bodyName = source.Meshes.Single(m => m.Offset == bodyOffset).Name;
            var body = Meshes(bus.Root).First(m => m.Name.ToString().StartsWith(bodyName + "#"));
            Check(body.IsInsideTree() && body.Mesh != null && body.Visible, "actual bodyNode0 mesh in scene");
            var record = bus.Model.Record;
            var controllerRecord = bus.Controller.Record;
            var snapshot = Snapshot(viewer, bus);
            var sounds = Field<RideSounds>(viewer, "_sounds");
            void Stable()
            {
                Check(snapshot.Equals(Snapshot(viewer, bus)), "zero ticks preserve controller/countdowns/admissions");
                Check(ReferenceEquals(record, bus.Model.Record)
                    && ReferenceEquals(controllerRecord, bus.Controller.Record), "binding record unchanged");
                Check(sounds?.ParameterValue?.Invoke(int.MinValue + 97, 20) == 0,
                    "approach sound parameter20 remains zero");
            }
            var beforeWorld = bus.Model.LastWorld[bodyOffset];
            var beforeMesh = body.Transform;
            Stable();
            Call(viewer, "StepPark", .01);
            Stable();
            var midWorld = bus.Model.LastWorld[bodyOffset];
            var midMesh = body.Transform;
            Check(midWorld != beforeWorld && Position(midWorld).DistanceTo(Position(beforeWorld)) > 0.00001f,
                "first zero-tick frame moves LastWorld body");
            Check(midMesh != beforeMesh && midMesh.Origin.DistanceTo(beforeMesh.Origin) > 0.00001f,
                "first zero-tick frame moves actual body mesh");
            Call(viewer, "StepPark", .01);
            Stable();
            var endWorld = bus.Model.LastWorld[bodyOffset];
            var endMesh = body.Transform;
            Check(endWorld != midWorld && Position(endWorld).DistanceTo(Position(midWorld)) > 0.00001f,
                "second zero-tick frame moves LastWorld body");
            Check(endMesh != midMesh && endMesh.Origin.DistanceTo(midMesh.Origin) > 0.00001f,
                "second zero-tick frame moves actual body mesh");
            int boundaryTicks = Field<int>(viewer, "_parkTicks");
            Call(viewer, "StepPark", .02);
            Check(Field<int>(viewer, "_parkTicks") == boundaryTicks + 1, "boundary executes exactly one tick");
            var boundaryWorld = bus.Model.LastWorld[bodyOffset];
            var boundaryMesh = body.Transform;
            Check((Position(boundaryWorld) - Position(endWorld)).Dot(
                (Position(midWorld) - Position(beforeWorld)).Normalized()) >= -0.0001f,
                "LastWorld does not jump backwards at tick boundary");
            Check((boundaryMesh.Origin - endMesh.Origin).Dot(
                (midMesh.Origin - beforeMesh.Origin).Normalized()) >= -0.0001f,
                "scene mesh does not jump backwards at tick boundary");
            snapshot = Snapshot(viewer, bus);
            Call(viewer, "StepPark", 0.0);
            Stable();
            Check(bus.Model.LastWorld[bodyOffset] == boundaryWorld && body.Transform == boundaryMesh,
                "paused StepPark(0) neither moves nor advances");
            exit = 0;
        }
        catch (Exception e)
        {
            GD.PrintErr($"BUS PRESENTATION SMOKE FAIL world={world} checks={_checks}: {e}");
        }
        finally
        {
            try
            {
                if (viewer != null && IsInstanceValid(viewer))
                {
                    Call(viewer, "ResetNativeBus");
                    Field<RideSounds>(viewer, "_sounds")?.Clear();
                    viewer.QueueFree();
                }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            }
            catch (Exception e)
            {
                exit = 2;
                GD.PrintErr($"BUS PRESENTATION SMOKE FAIL cleanup: {e}");
            }
        }
        if (exit == 0) GD.Print($"BUS PRESENTATION SMOKE PASS world={world} checks={_checks}");
        GetTree().Quit(exit);
    }
}
