using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>Rendered service capture using normal Viewer initialization and production build/tick
/// callbacks. The driver supplies a deterministic customer and camera; this is not physical-input
/// automation or visual sign-off. Invoke with --mode=park --map=JUNGLE --service-shot=/tmp/name.png.</summary>
public partial class StandingServiceSmoke : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo Member(string name) => typeof(Viewer).GetField(name, Hidden) ?? throw new MissingMemberException("Viewer." + name);
    static T Field<T>(Viewer v, string name) => (T)Member(name).GetValue(v);
    static void Set(Viewer v, string name, object value) => Member(name).SetValue(v, value);
    static object Call(Viewer v, string name, params object[] args)
    {
        var method = typeof(Viewer).GetMethod(name, Hidden) ?? throw new MissingMemberException("Viewer." + name);
        var parameters = method.GetParameters();
        if (args.Length < parameters.Length)
            args = args.Concat(parameters.Skip(args.Length).Select(p => p.HasDefaultValue ? p.DefaultValue
                : throw new ArgumentException("Missing required argument for Viewer." + name))).ToArray();
        return method.Invoke(v, args);
    }
    static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        GD.Print("SERVICE SMOKE ok: " + label);
    }

    public override async void _Ready()
    {
        Viewer viewer = null;
        try
        {
            string shot = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--service-shot="))?["--service-shot=".Length..]
                ?? throw new ArgumentException("--service-shot requires an output filename outside the repository");
            if (!System.IO.Path.IsPathFullyQualified(shot) || !string.Equals(System.IO.Path.GetExtension(shot), ".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("absolute .png capture path required");
            if (!System.IO.Path.GetFullPath(shot).StartsWith("/tmp/", StringComparison.Ordinal))
                throw new ArgumentException("smoke artifacts must be under /tmp");
            string afterShot = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(shot), System.IO.Path.GetFileNameWithoutExtension(shot) + "-after.png");
            string hiddenShot = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(shot), System.IO.Path.GetFileNameWithoutExtension(shot) + "-hidden.png");
            string egressShot = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(shot), System.IO.Path.GetFileNameWithoutExtension(shot) + "-egress.png");
            if (System.IO.File.Exists(shot) || System.IO.File.Exists(afterShot) || System.IO.File.Exists(hiddenShot) || System.IO.File.Exists(egressShot))
                throw new ArgumentException("capture paths must be new");
            var output = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(shot));
            if (!output.Exists) throw new ArgumentException("create the output directory before running the smoke");
            for (var parent = output; parent != null; parent = parent.Parent)
                if (System.IO.File.Exists(System.IO.Path.Combine(parent.FullName, ".git")) || System.IO.Directory.Exists(System.IO.Path.Combine(parent.FullName, ".git")))
                    throw new ArgumentException("capture output must be outside Git ancestry");
            // Unlike the headless audit, this invokes the real Viewer._Ready and keeps its tree.
            viewer = new Viewer { Name = "Viewer" }; AddChild(viewer);
            Set(viewer, "_guestCap", 0); viewer.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var park = Field<Park>(viewer, "_park"); var library = Field<AssetLibrary>(viewer, "_lib");
            var terrain = Field<Model>(viewer, "_terrainModel");
            var entrances = Field<ParkEntrance>(viewer, "_entranceTable");
            Require(park?.Field != null && terrain != null && entrances != null, "normal startup loaded the real park and entrance data");
            var entry = entrances.Fit(terrain.Field, ParkEntrance.WalkwayColumnFromPoles(terrain), out _);
            Require(!entry.Empty, "real entrance fits this park");
            var corridor = new List<(int X, int Y)>();
            for (int z = entry.ZEnd; z < entry.ZEnd + 8; z++) corridor.Add((entry.XCol, z));
            Call(viewer, "LayLeg", corridor, PathTool.Kind.Path, 0); Call(viewer, "RefreshFloor");
            Require((bool)Call(viewer, "OpenGate"), "normal guest layer opened on the authored entrance");
            var grid = Field<GuestWalk>(viewer, "_guests").Paths;
            Call(viewer, "ShowBuildCategory", "Features");
            var rows = Field<List<int>>(viewer, "_buildRows");
            int row = rows.FindIndex(i => ((RideDefinition)Call(viewer, "DefinitionFor", library.Rides[i].Model)) is { ProvidesRelief: true, Shape: not null } d
                                         && Park.Footprint.From(d.Shape).Width == 1 && Park.Footprint.From(d.Shape).Height == 1);
            Require(row >= 0, "Features menu exposes a small authored relief facility");
            var blueprint = Field<Placement>(viewer, "_place");
            bool placed = false;
            for (int turn = 0; turn < 4 && !placed; turn++)
            {
                Call(viewer, "ArmFromList", row); blueprint.Turn(turn);
                for (int z = entry.ZEnd; z < entry.ZEnd + 8 && !placed; z++)
                    for (int x = entry.XCol - 4; x <= entry.XCol + 4 && !placed; x++)
                    {
                        if (!blueprint.Fits(park, x, z)) continue;
                        var stubs = blueprint.Stubs(park, x, z).Where(s => s.Entrance).ToArray();
                        if (stubs.Length != 1 || !ParkPaths.Neighbours(new(stubs[0].X, stubs[0].Y)).Any(grid.Open)) continue;
                        int before = park.Placed.Count; Set(viewer, "_cursorOverride", (x, z)); Call(viewer, "PlaceHeld");
                        Set(viewer, "_cursorOverride", null); placed = park.Placed.Count == before + 1;
                    }
            }
            Require(placed, "actual Features placement callback put the facility beside the corridor");
            Call(viewer, "CloseTool");
            Call(viewer, "TickPark");
            var visitors = Field<ParkVisitors>(viewer, "_visitors");
            var ride = Field<ParkSim>(viewer, "_sim").Rides.Single();
            Require(ride.NativeRelief && ride.ServiceEntry != null, "normal placement enables actual compiled NativeRelief with an inside entry");
            foreach (string key in visitors.Needs.Rates.Keys.ToArray()) visitors.Needs.Rates[key] = new VisitorNeeds.Rate(0,0,false);
            var mouth = Field<List<ParkCell>>(viewer, "_mouth")[0];
            var guest = visitors.Arrive(mouth, mouth);
            var need = visitors.Needs.Of(guest.Id); need.Toilet = 91; need.Cash = 1234; need.Happiness = 50;
            need.Hunger = need.Thirst = need.Sick = need.Boredom = 0; visitors.Needs.Set(guest.Id, need);
            var standing = Field<Dictionary<int,Transform3D>>(viewer, "_standing");
            var actors = Field<Dictionary<int,Node3D>>(viewer, "_actors");
            var thoughts = Field<ThoughtBubbles>(viewer, "_thoughts");
            var sim = Field<ParkSim>(viewer, "_sim");
            var inside = ride.ServiceEntry.Value;
            var stub = ride.Entrance.Value;
            uint Now() => (uint)(sim.Time / ParkSim.TickMilliseconds);
            bool FullBody(Node3D body) => body != null && GodotObject.IsInstanceValid(body)
                && body.IsInsideTree() && !body.IsQueuedForDeletion()
                && new[] { "*body*", "*legs*" }.All(pattern => body.FindChildren(pattern, "MeshInstance3D", true, false)
                    .OfType<MeshInstance3D>().Any(m => m.Mesh != null && m.IsVisibleInTree()));
            bool NoBubble() => !thoughts.Root.GetChildren().OfType<Sprite3D>().Any(b => b.Visible);
            bool NoOtherOwner() => (!ride.Host.Visibility.TryGetValue(guest.Id, out var visibility) || visibility.Visible)
                && !ride.Host.Seats.Values.Contains(guest.Id) && !ride.Host.Walkers.ContainsKey(guest.Id)
                && !ParkSim.Chain(ride.Machine).Any(m => m.GuestIds.Contains(guest.Id));
            ReliefServiceClock Clock()
            {
                var visits = (System.Collections.IDictionary)(typeof(ParkVisitors).GetField("_reliefVisits", Hidden)
                    ?? throw new MissingMemberException("ParkVisitors._reliefVisits")).GetValue(visitors);
                var visit = visits[guest.Id] ?? throw new InvalidOperationException("Missing native visit");
                return (ReliefServiceClock)visit.GetType().GetProperty("Clock").GetValue(visit);
            }
            void Record(string phase)
            {
                var n = visitors.Needs.Of(guest.Id);
                GD.Print($"SERVICE SMOKE {phase} tick={Now()} ms={sim.Time} guest={guest.Id} owner={visitors.QueuedOwner(guest.Id)?.Id.ToString() ?? "none"} "
                    + $"intent={visitors.Plans[guest.Id].Intent} cell={guest.Cell} next={guest.Next} progress={guest.Progress} pos={guest.Position} "
                    + $"hidden={visitors.ServiceHidden(guest.Id)} deadline={visitors.ReliefDeadline(guest.Id)?.ToString() ?? "none"} "
                    + $"toilet={n.Toilet} cash={n.Cash} hunger={n.Hunger} thirst={n.Thirst} sick={n.Sick} happiness={n.Happiness} "
                    + $"boardings={visitors.Boardings} rides={visitors.Rides} relieved={visitors.Relieved} bodies={actors.Count} standing={standing.Count}");
            }
            void Tick()
            {
                Call(viewer, "TickPark"); Call(viewer, "PresentScripted", true, 1f); Call(viewer, "PlaceActors", 1f);
            }
            async System.Threading.Tasks.Task Frames()
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            async System.Threading.Tasks.Task Capture(string path)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                Require(image.SavePng(path) == Error.Ok, "saved rendered capture " + System.IO.Path.GetFileName(path));
            }
            // No synthetic coordinator or SendTo: normal startup, placement, destination choice and ticks.
            for (int tick = 0; tick < 6000 && !(guest.Cell == stub && guest.Next == inside && guest.Progress > 0); tick++)
            {
                Tick();
                if (visitors.ServiceHidden(guest.Id)) break;
                if (tick % 8 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Record("walking-inside");
            Require(guest.Cell == stub && guest.Next == inside && guest.Progress > 0
                && !grid.Open(inside) && !visitors.ServiceHidden(guest.Id) && visitors.Boardings == 0
                && FullBody(actors.GetValueOrDefault(guest.Id)),
                "production destination choice walks a visible body along the blocked terminal leg, not a teleport");
            var original = actors[guest.Id];
            var bubble = thoughts.Root.GetChildren().OfType<Sprite3D>().Single(s => s.Name == $"thought{guest.Id}" && s.Visible);
            var authoredIcon = new Ssh(library.ReadGeneric("/Generic/bubbles/tbtoilet.ssh"));
            using (var boundImage = bubble.Texture.GetImage())
                Require(visitors.Needs.Of(guest.Id).Thought == Thought.Toilet
                        && boundImage.GetData().SequenceEqual(authoredIcon.Pixels),
                        "approach thought texture exactly matches decoded disc toilet artwork");
            var camera = Field<Camera3D>(viewer, "_cam");
            var insideWorld = (Vector3)Call(viewer, "GuestWorld", new Vector3(inside.X+.5f,0,inside.Z+.5f), inside);
            var stubWorld = (Vector3)Call(viewer, "GuestWorld", new Vector3(stub.X+.5f,0,stub.Z+.5f), stub);
            var target = insideWorld + Vector3.Up * .35f;
            var front = stubWorld - insideWorld; front.Y = 0; front = front.Normalized();
            camera.GlobalPosition = target + front * 3.2f + Vector3.Up * 1.6f; camera.LookAt(target);
            Field<Control>(viewer, "_panel").Visible = false;
            Require(!camera.IsPositionBehind(target) && GetViewport().GetVisibleRect().HasPoint(camera.UnprojectPosition(target)),
                "camera projects actual inside entry into the viewport");
            await Capture(shot);
            for (int tick = 0; tick < 100 && !visitors.ServiceHidden(guest.Id); tick++) Tick();
            uint entered = Now();
            var insidePosition = guest.Position;
            Record("entered");
            Require(visitors.ServiceHidden(guest.Id) && visitors.ReliefDeadline(guest.Id) == entered + 522
                && guest.Cell == inside && insidePosition == ParkPaths.Centre(inside)
                && visitors.Plans[guest.Id].At == inside && visitors.Plans[guest.Id].Intent == VisitorIntent.Servicing
                && visitors.QueuedOwner(guest.Id) == ride && !visitors.Walk.Guests.Contains(guest)
                && visitors.Boardings == 1 && NoOtherOwner(), "inside arrival starts native hidden residence under coordinator ownership");
            await Frames();
            var guestRoot = Field<Node3D>(viewer, "_guestRoot");
            Require(!actors.ContainsKey(guest.Id) && !standing.ContainsKey(guest.Id)
                && !GodotObject.IsInstanceValid(original) && guestRoot.GetChildCount() == 0 && NoBubble(),
                "hidden residence removes actual body, standing pose and thought bubble");
            sim.SetOpen(ride.Id, false); // Prevent repeat visit; closure must not shorten residence.
            await Capture(hiddenShot);
            for (int tick = 0; tick < 522; tick++) Tick();
            Record("+522-wait");
            Require(Now() == entered + 522 && visitors.ServiceHidden(guest.Id) && !Clock().Finishing
                && visitors.Relieved == 0 && visitors.Rides == 0 && visitors.Needs.Of(guest.Id).Toilet == 91
                && visitors.Needs.Of(guest.Id).Cash == 1234 && visitors.QueuedOwner(guest.Id) == ride
                && !actors.ContainsKey(guest.Id) && !standing.ContainsKey(guest.Id) && NoBubble() && NoOtherOwner(),
                "exact +522 equality still waits hidden, with unchanged needs and owner");
            Tick(); Record("+523-state22");
            Require(Now() == entered + 523 && Clock().Finishing && visitors.ServiceHidden(guest.Id)
                && visitors.Relieved == 0 && visitors.Rides == 0 && visitors.Needs.Of(guest.Id).Toilet == 91
                && visitors.Needs.Of(guest.Id).Cash == 1234 && visitors.QueuedOwner(guest.Id) == ride
                && !actors.ContainsKey(guest.Id) && NoBubble(), "exact +523 enters state22 only, still hidden and unrelieved");
            Tick(); await Frames();
            // Readmission preserves the native guest ID, not the managed Guest object.
            int servicedId = guest.Id;
            var returned = visitors.Walk.Guests.SingleOrDefault(g => g.Id == servicedId);
            Require(returned != null, "completion readmits the original guest ID");
            guest = returned; Record("+524-completion");
            Require(Now() == entered + 524 && !visitors.ServiceHidden(guest.Id) && visitors.ReliefDeadline(guest.Id) == null
                && visitors.Relieved == 1 && visitors.Rides == 1 && visitors.Boardings == 1
                && visitors.Needs.Of(guest.Id).Toilet == 0 && visitors.Needs.Of(guest.Id).Cash == 1234
                && visitors.QueuedOwner(guest.Id) == null && NoOtherOwner(), "exact +524 completes once, clears toilet not cash, releases owner");
            Require(visitors.Walk.Guests.Single(g => g.Id == guest.Id) == guest
                && guest.Cell == inside && guest.Next == null && guest.Progress == 0 && guest.Position == insidePosition
                && FullBody(actors.GetValueOrDefault(guest.Id)) && actors.Count == 1 && guestRoot.GetChildCount() == 1
                && !standing.ContainsKey(guest.Id) && NoBubble(), "same guest reappears at identical inside position, one full body and no duplicate");
            var expectedWorld = (Vector3)Call(viewer, "GuestWorld",
                new Vector3(insidePosition.X, insidePosition.Y, insidePosition.Z), inside);
            Require(actors[guest.Id].Position.DistanceTo(expectedWorld) < .0001f,
                "reappeared body uses production GuestWorld inside floor, not stub floor");
            await Capture(afterShot);
            for (int tick = 0; tick < 100 && guest.Progress == 0; tick++)
            {
                Tick();
                Require(guest.Cell == inside, "egress begins from retained inside cell, never teleports to stub");
            }
            Record("egress-start");
            Require(guest.Cell == inside && guest.Next == stub && guest.Progress > 0
                && guest.Position != insidePosition && FullBody(actors.GetValueOrDefault(guest.Id))
                && !visitors.ServiceHidden(guest.Id), "production ticks physically interpolate egress from inside to public stub");
            await Capture(egressShot);
            for (int tick = 0; tick < 100 && guest.Cell != stub; tick++) Tick();
            Record("egress-stub");
            Require(guest.Cell == stub && grid.Open(guest.Cell) && FullBody(actors.GetValueOrDefault(guest.Id))
                && visitors.Relieved == 1 && visitors.Rides == 1 && visitors.Boardings == 1,
                "physical egress reaches public path without duplicate completion");
            viewer.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("SERVICE SMOKE PASS (rendered captures require visual review)"); GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PrintErr("SERVICE SMOKE FAIL: " + ex); GetTree().Quit(2); }
    }
}
