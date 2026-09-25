using System.Reflection;
using Godot;
using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPWPS2Viewer.Tests;

/// <summary>Rendering-display consumer proof using normal --disc=... --map=JUNGLE
/// --mode=park startup. Explicit cap0/fixture guest: NOT full admission, fees or queues.</summary>
public partial class NativeEntranceRouteSmoke : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo Member(string name) => typeof(Viewer).GetField(name, Hidden)
        ?? throw new MissingMemberException("Viewer." + name);
    static T Field<T>(Viewer v, string name) => (T)Member(name).GetValue(v);
    static object Call(Viewer v, string name, params object[] args)
    {
        var method = typeof(Viewer).GetMethod(name, Hidden)
            ?? throw new MissingMemberException("Viewer." + name);
        var parameters = method.GetParameters();
        if (args.Length < parameters.Length)
            args = args.Concat(parameters.Skip(args.Length).Select(p => p.HasDefaultValue ? p.DefaultValue
                : throw new ArgumentException("Missing required argument for Viewer." + name))).ToArray();
        return method.Invoke(v, args);
    }
    int _checks;
    void Check(bool ok, string why)
    {
        if (!ok) throw new InvalidOperationException(why);
        _checks++;
    }
    static bool Live(Node3D node) => node != null && GodotObject.IsInstanceValid(node)
        && node.IsInsideTree() && !node.IsQueuedForDeletion() && node.IsVisibleInTree();

    static bool SameNeeds(VisitorWants actual, VisitorWants expected)
    { actual.Thought = expected.Thought; return actual.Equals(expected); }

    public override async void _Ready()
    {
        Viewer viewer = null;
        int exit = 2;
        try
        {
            Check(DisplayServer.GetName() != "headless", "rendering display required");
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            string map = args.LastOrDefault(a => a.StartsWith("--map="))?[6..];
            string mode = args.LastOrDefault(a => a.StartsWith("--mode="))?[7..];
            Check(string.Equals(map, "JUNGLE", StringComparison.OrdinalIgnoreCase)
                && string.Equals(mode, "park", StringComparison.OrdinalIgnoreCase),
                "normal --map=JUNGLE --mode=park startup required");
            Check(!args.Any(a => a.StartsWith("--shot=") || a.Contains("-film=")
                || a.StartsWith("--sound-census=") || a.EndsWith("-test") || a.EndsWith("-audit")
                || a == "--ghost-press") && string.IsNullOrWhiteSpace(OS.GetEnvironment("TPW_PS2_SHOT")),
                "no capture or unrelated startup fixture switches");
            viewer = new Viewer { Name = "Viewer" };
            AddChild(viewer); // normal Viewer startup; no synthetic terrain/simulation injection
            Member("_guestCap").SetValue(viewer, 0); // explicit fixture: automatic arrivals OFF
            viewer.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var park = Field<Park>(viewer, "_park");
            var terrain = Field<Model>(viewer, "_terrainModel");
            var entrance = Field<ParkEntrance>(viewer, "_entranceTable");
            var library = Field<AssetLibrary>(viewer, "_lib");
            Check(park?.Field != null && terrain?.Field != null && entrance != null && library != null
                && Field<object>(viewer, "_mode").ToString() == "Park"
                && string.Equals(System.IO.Path.GetFileNameWithoutExtension(library.WadName), "JUNGLE", StringComparison.OrdinalIgnoreCase),
                "normal startup loaded actual Jungle park assets");
            Check(Field<int>(viewer, "_guestCap") == 0 && Field<int>(viewer, "_parkTicks") == 0,
                "explicit auto-arrivals-off fixture before any park tick");
            var entry = entrance.Fit(terrain.Field, ParkEntrance.WalkwayColumnFromPoles(terrain), out _);
            Check(!entry.Empty, "authored entrance fits terrain");
            Check((bool)Call(viewer, "OpenGate"), "production OpenGate succeeds");
            Call(viewer, "StepPark", .04);
            var visitors = Field<ParkVisitors>(viewer, "_visitors");
            var walk = Field<GuestWalk>(viewer, "_guests");
            Check(visitors != null && ReferenceEquals(visitors.Walk, walk)
                && ReferenceEquals(visitors.Sim, Field<ParkSim>(viewer, "_sim"))
                && visitors.Needs != null && walk.Guests.Count == 0 && visitors.Plans.Count == 0,
                "StepPark supplies actual shared visitors/walk with no automatic guest");
            // Legal authored corridor, deliberately NOT described as a decoded native queue point.
            var cell = new ParkCell(entry.XCol, entry.ZEnd - 2);
            Check(walk.Paths.Open(cell), "explicit fixture guest starts on legal entrance corridor cell");
            visitors.Needs.SecondsPerRise = 1_000_000;
            foreach (var key in visitors.Needs.Rates.Keys.ToArray()) visitors.Needs.Rates[key] = new(0, 0, false);
            var guest = visitors.Arrive(cell, cell);
            int id = guest.Id;
            var wants = new VisitorWants { Cash = 1234, Happiness = 0, Hunger = 12, Thirst = 13, Toilet = 14 };
            visitors.Needs.Set(id, wants);
            Check(visitors.Needs.WantsToGoHome(id), "Happiness0 supplies an actual departure-pressure control");
            Point centre = new(checked((short)(cell.X * 256 + 128)), checked((short)(cell.Z * 256 + 128)));
            Point quarter = new(centre.X, (short)(centre.Z - 64));
            var owner = new object();
            int speeds = 0, deltas = 0, readies = 0;
            var inputs = new NativeMotionInputs(() => { speeds++; return 15; },
                () => { deltas++; return 0x4000; }, () => { readies++; return true; });
            Check(visitors.BeginEntranceRoute(guest, owner, new[] { quarter }, inputs),
                "actual ParkVisitors.BeginEntranceRoute acquires existing fixture identity");
            var plan = visitors.Plans[id];
            var actors = Field<Dictionary<int, Node3D>>(viewer, "_actors");
            // Independent world-space oracle: use known raw fixture coordinates and cell corners,
            // never Guest.Position, GuestWorld, or an actor transform to derive expected X/Z.
            Vector3 Expected(Point raw)
            {
                var a = park.CellCorner(cell.X, cell.Z);
                var b = park.CellCorner(cell.X + 1, cell.Z);
                var c = park.CellCorner(cell.X, cell.Z + 1);
                return a.Lerp(b, (raw.X - cell.X * 256) / 256f)
                    + (c - a) * ((raw.Z - cell.Z * 256) / 256f);
            }
            Node3D originalActor = null;
            void Pose(Point raw, string label)
            {
                Call(viewer, "PlaceActors", 1f); // current pose, not previous-frame interpolation
                Check(actors.TryGetValue(id, out var actor) && Live(actor), label + ": live visible actor root");
                originalActor ??= actor;
                var expected = Expected(raw);
                Check(ReferenceEquals(originalActor, actor)
                    && Mathf.Abs(actor.Position.X - expected.X) < .001f
                    && Mathf.Abs(actor.Position.Z - expected.Z) < .001f,
                    label + ": same live actor X/Z equals independent CellCorner interpolation");
            }
            void Facing(bool decreasingZ)
            {
                var direction = park.CellCorner(cell.X, cell.Z + 1) - park.CellCorner(cell.X, cell.Z);
                direction.Y = 0;
                direction = direction.Normalized() * (decreasingZ ? -1 : 1);
                Check(actors[id].Basis.Z.Normalized().Dot(direction) > .999f,
                    "live actor native facing follows independent mirrored cell direction");
            }
            void Membership(string label)
            {
                Check(walk.Guests.Count == 1 && ReferenceEquals(walk.Guests.Single(), guest)
                    && guest.Id == id && guest.HasNativeRoute && visitors.Plans.Count == 1
                    && visitors.Plans[id] == plan && plan.Intent == VisitorIntent.Entering
                    && visitors.WentHome == 0 && SameNeeds(visitors.Needs.Of(id), wants)
                    && Field<int>(viewer, "_busAdmitted") == 0,
                    label + ": entering membership/identity/cash/needs stable under departure pressure");
            }
            void Tick()
            {
                int before = Field<int>(viewer, "_parkTicks");
                Call(viewer, "StepPark", .04);
                Check(Field<int>(viewer, "_parkTicks") == before + 1, "StepPark executes exactly one update");
            }
            Pose(centre, "initial centre");
            for (int tick = 1; tick <= 8; tick++)
            {
                Tick();
                var expected = new Point(centre.X, (short)(centre.Z - Math.Min(tick * 15, 64)));
                Check(walk.NativeRouteState(guest, owner) is { } state && state.Position == expected
                    && state.Finished == (tick == 8) && guest.Progress == 0 && guest.Steps == 0,
                    $"tick{tick}: five movement ticks plus three deferred handoff updates, no double step");
                Pose(expected, $"tick{tick}"); Facing(true); Membership($"tick{tick}");
                if (tick == 5) Check(!visitors.ReleaseEntranceRoute(guest, owner), "at waypoint still owned; not completion");
            }
            Check(speeds == 5 && deltas == 5 && readies == 5, "handoff updates bypass all native input callbacks");
            var fractional = guest.Position;
            Check(!visitors.ReleaseEntranceRoute(guest, owner) && guest.Position == fractional,
                "finished fractional release refuses without teleport");
            Tick(); Pose(quarter, "completed but owned"); Membership("completed but owned");
            Check(!walk.Send(guest, cell.Offset(0, -1)) && !visitors.ReleaseEntranceRoute(guest, new object()),
                "public send and wrong-owner release cannot steal completed lease");
            Check(visitors.BeginEntranceRoute(guest, owner, new[] { centre }, inputs) && guest.Position == fractional,
                "same-owner centre replacement preserves exact fractional position");
            for (int tick = 1; tick <= 8; tick++)
            {
                Tick();
                Pose(new Point(centre.X, (short)(quarter.Z + Math.Min(tick * 15, 64))), $"return{tick}");
                Facing(false); Membership($"return{tick}");
            }
            var beforeRelease = guest.Position;
            Check(visitors.ReleaseEntranceRoute(guest, owner) && guest.Position == beforeRelease && !guest.HasNativeRoute
                && guest.Id == id && ReferenceEquals(walk.Guests.Single(), guest)
                && visitors.Plans[id].Intent == VisitorIntent.Wandering && SameNeeds(visitors.Needs.Of(id), wants),
                "finished centre releases same identity, cash and needs without a position change");
            Pose(centre, "released centre");
            exit = 0;
        }
        catch (Exception e)
        {
            GD.PrintErr($"NATIVE ENTRANCE ROUTE SMOKE FAIL checks={_checks}: {e}");
        }
        finally
        {
            try
            {
                if (viewer != null && GodotObject.IsInstanceValid(viewer))
                {
                    try { Call(viewer, "ResetNativeBus"); }
                    finally
                    {
                        try { Field<RideSounds>(viewer, "_sounds")?.Clear(); }
                        finally { viewer.QueueFree(); }
                    }
                }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            }
            catch (Exception e) { exit = 2; GD.PrintErr("NATIVE ENTRANCE ROUTE SMOKE cleanup failed: " + e); }
        }
        if (exit == 0) GD.Print($"NATIVE ENTRANCE ROUTE SMOKE PASS checks={_checks}; explicit cap0 fixture; fractional route ownership/rendered consumer ONLY; NO full fees/queues or entrance admission");
        GetTree().Quit(exit);
    }
}
