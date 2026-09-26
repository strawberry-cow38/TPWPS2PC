using System.Collections;
using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>The restored seaplane and ferry, on the shipping Viewer: normal startup in park mode,
/// every frame through StepPark(.04). Run with --map=WORLD --mode=park.
///
/// HALLOW, FANTASY and SPACE must load both, draw them, and run each one's own script through a
/// whole visit -- VAR_STATUS 1 (arriving) to 6 (away) and back to 1 -- moving between its stops.
/// JUNGLE must load neither: its seaplane stop is inside the hillside and its ferry has no model.
/// `--no-seaplane-ferry` (the field behind it) must leave none in any world.</summary>
public partial class ParkVehiclesSmoke : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo Member(string name) => typeof(Viewer).GetField(name, Hidden)
        ?? throw new MissingMemberException("Viewer." + name);
    static T Field<T>(Viewer v, string name) => (T)Member(name).GetValue(v);
    static void Set(Viewer v, string name, object value) => Member(name).SetValue(v, value);
    static object Call(Viewer v, string name, params object[] args)
        => (typeof(Viewer).GetMethod(name, Hidden) ?? throw new MissingMemberException("Viewer." + name)).Invoke(v, args);
    static object F(object o, string name) => o.GetType().GetField(name)?.GetValue(o)
        ?? throw new MissingMemberException(o.GetType().Name + "." + name);
    int _checks;
    void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checks++;
        GD.Print("PARK VEHICLES ok: " + label);
    }

    public override async void _Ready()
    {
        string world = "unknown";
        try
        {
            Check(DisplayServer.GetName() != "headless", "rendering display required");
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            string map = args.LastOrDefault(a => a.StartsWith("--map="))?["--map=".Length..]
                ?? throw new ArgumentException("Pass --map=WORLD");
            world = new[] { "JUNGLE", "FANTASY", "HALLOW", "SPACE" }.Single(w =>
                map.Equals(w, StringComparison.OrdinalIgnoreCase) || map.StartsWith(w + " ", StringComparison.OrdinalIgnoreCase));

            var viewer = new Viewer { Name = "Viewer" };
            AddChild(viewer);
            viewer.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(Field<object>(viewer, "_mode").ToString() == "Park", "actual Viewer is in Park mode");
            var vehicles = Field<IList>(viewer, "_vehicles");

            Call(viewer, "StepPark", .04);
            if (world == "JUNGLE")
            {
                Check(vehicles.Count == 0 && Field<string>(viewer, "_vehiclesKey") != null,
                    "JUNGLE considered the park and loaded no seaplane or ferry");
            }
            else
            {
                var stems = vehicles.Cast<object>().Select(v => (string)F(v, "Stem")).OrderBy(s => s).ToArray();
                Check(stems.SequenceEqual(new[] { "ferry", "seaplane" }), $"{world} loads the seaplane and the ferry ({string.Join(",", stems)})");
                var seen = vehicles.Cast<object>().ToDictionary(v => (string)F(v, "Stem"), _ => new List<int>());
                var at = vehicles.Cast<object>().ToDictionary(v => (string)F(v, "Stem"), _ => new Dictionary<int, Vector3>());
                for (int tick = 0; tick < 8000 && seen.Values.Any(s => !Cycled(s)); tick++)
                {
                    Call(viewer, "StepPark", .04);
                    foreach (var v in vehicles.Cast<object>())
                    {
                        var stem = (string)F(v, "Stem");
                        int status = (int)F(v, "LastStatus");
                        var list = seen[stem];
                        if (list.Count == 0 || list[^1] != status)
                        {
                            list.Add(status);
                            if (Where(v) is { } p) at[stem][status] = p;
                        }
                    }
                    if (tick % 50 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                foreach (var v in vehicles.Cast<object>())
                {
                    var stem = (string)F(v, "Stem");
                    var root = (Node3D)F(v, "Root");
                    Check(IsInstanceValid(root) && root.IsInsideTree() && root.Visible
                        && root.FindChildren("*", "MeshInstance3D", true, false).Count > 0,
                        $"{world} {stem} is drawn: a visible root in the tree with meshes");
                    Check(Cycled(seen[stem]), $"{world} {stem}'s own script ran a whole visit: {string.Join(" ", seen[stem])}");
                    Check(at[stem].TryGetValue(2, out var stop1) && at[stem].TryGetValue(4, out var stop2)
                        && stop1.DistanceTo(stop2) > 1f,
                        $"{world} {stem} moved between its stops ({(at[stem].TryGetValue(2, out var a) && at[stem].TryGetValue(4, out var b) ? a.DistanceTo(b).ToString("F1") : "-")} units)");
                    Check((int)F(v, "Triggers") >= 3, $"{world} {stem} was sent on by the port at each of its three waits ({F(v, "Triggers")})");
                }
            }

            // The switch: with it set, a reloaded park has neither.
            Call(viewer, "ResetNativeBus");
            Set(viewer, "_noSeaplaneFerry", true);
            Call(viewer, "StepPark", .04);
            Check(vehicles.Count == 0, "--no-seaplane-ferry leaves no vehicle");

            // Retire the park's voices before closing, as BusViewerSmoke does: scene frames alone can
            // outrun Dummy audio's pending stopped playbacks, and they report as leaked instances.
            Call(viewer, "ResetNativeBus");
            Field<RideSounds>(viewer, "_sounds")?.Clear();
            viewer.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            GD.Print($"PARK VEHICLES SMOKE PASS checks={_checks}; world={world}");
            GetTree().Quit(0);
        }
        catch (Exception e)
        {
            GD.PrintErr($"PARK VEHICLES SMOKE FAIL world={world} checks={_checks}: {e}");
            GetTree().Quit(2);
        }
    }

    /// <summary>1, 2, 3, 4, 5, 6 in order and back to 1: one whole visit.</summary>
    static bool Cycled(List<int> s)
    {
        int i = s.IndexOf(1);
        return i >= 0 && s.Count >= i + 7 && s.Skip(i).Take(7).SequenceEqual(new[] { 1, 2, 3, 4, 5, 6, 1 });
    }

    /// <summary>Where the vehicle is: the mean of its animated node positions in the world.</summary>
    static Vector3? Where(object v)
    {
        var presenter = (RseModelPresenter)F(v, "Presenter");
        var root = (Node3D)F(v, "Root");
        var world = presenter.Drawn?.LastWorld;
        if (world == null || world.Count == 0) return null;
        var sum = Vector3.Zero;
        foreach (var m in world.Values) sum += root.GlobalTransform * new Vector3(m.M41, m.M42, m.M43);
        return sum / world.Count;
    }
}
