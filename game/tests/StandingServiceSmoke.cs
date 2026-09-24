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
            string afterShot = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(shot), System.IO.Path.GetFileNameWithoutExtension(shot) + "-after.png");
            string parkShot = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(shot), System.IO.Path.GetFileNameWithoutExtension(shot) + "-park.png");
            if (System.IO.File.Exists(shot) || System.IO.File.Exists(afterShot) || System.IO.File.Exists(parkShot))
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
            Call(viewer, "ToggleBuildMenu"); Call(viewer, "ShowBuildCategory", "Features");
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
            foreach (string key in visitors.Needs.Rates.Keys.ToArray()) visitors.Needs.Rates[key] = new VisitorNeeds.Rate(0,0,false);
            var mouth = Field<List<ParkCell>>(viewer, "_mouth")[0];
            var guest = visitors.Arrive(mouth, mouth);
            var need = visitors.Needs.Of(guest.Id); need.Toilet = 91; need.Cash = 1234; need.Happiness = 50;
            need.Hunger = need.Thirst = need.Sick = need.Unknown7B = 0; visitors.Needs.Set(guest.Id, need);
            var standing = Field<Dictionary<int,Transform3D>>(viewer, "_standing");
            void Tick()
            {
                Call(viewer, "TickPark"); Call(viewer, "PresentScripted", true, 1f); Call(viewer, "PlaceActors", 1f);
            }
            bool serving = false;
            for (int tick = 0; tick < 6000 && !serving; tick++)
            {
                Tick();
                serving = visitors.QueuedOwner(guest.Id) == ride && standing.ContainsKey(guest.Id)
                          && !ride.Queue.Contains(guest.Id) && ride.Get("VAR_LETMEON") != guest.Id;
                if (tick % 8 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Require(serving && visitors.Relieved == 0, "production ticks route the urgent customer into visible service");
            var thoughts = Field<ThoughtBubbles>(viewer, "_thoughts");
            var bubble = thoughts.Root.GetChildren().OfType<Sprite3D>().Single(s => s.Name == $"thought{guest.Id}" && s.Visible);
            var authoredIcon = new Ssh(library.ReadGeneric("/Generic/bubbles/tbtoilet.ssh"));
            using (var boundImage = bubble.Texture.GetImage())
                Require(visitors.Needs.Of(guest.Id).Thought == Thought.Toilet
                        && boundImage.GetData().SequenceEqual(authoredIcon.Pixels),
                        "displayed thought texture exactly matches the decoded disc toilet artwork");
            var actors = Field<Dictionary<int,Node3D>>(viewer, "_actors"); var actor = actors[guest.Id];
            var camera = Field<Camera3D>(viewer, "_cam");
            var target = actor.GlobalPosition + Vector3.Up * .35f;
            // Aim from the actual stub side; captures a person/building relation, not a far park dot.
            var stub = ride.Entrance.Value;
            var stubWorld = (Vector3)Call(viewer, "GuestWorld", new Vector3(stub.X+.5f,0,stub.Z+.5f), stub);
            var front = stubWorld - actor.GlobalPosition; front.Y = 0; front = front.Normalized();
            camera.GlobalPosition = target + front * 3.2f + Vector3.Up * 1.6f; camera.LookAt(target);
            Field<Control>(viewer, "_panel").Visible = false;
            Require(!camera.IsPositionBehind(target) && GetViewport().GetVisibleRect().HasPoint(camera.UnprojectPosition(target)),
                    "camera projects the actual customer into the viewport");
            async System.Threading.Tasks.Task Capture(string path)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var image = GetViewport().GetTexture().GetImage();
                Require(image.SavePng(path) == Error.Ok, "saved rendered capture " + System.IO.Path.GetFileName(path));
            }
            GD.Print($"SERVICE SMOKE serving guest={guest.Id} actor={actor.GlobalPosition} toilet={visitors.Needs.Of(guest.Id).Toilet} cash={visitors.Needs.Of(guest.Id).Cash}");
            await Capture(shot);
            // Measure, do not invent a scaling rule: the default gameplay camera panned onto
            // this same customer. Raw camera distances are /256 Godot units, not world units.
            var closeUp = camera.Transform;
            var gameCamera = Field<GameCamera>(viewer, "_game");
            gameCamera.PlaceAt(actor.GlobalPosition.X, actor.GlobalPosition.Z); Call(viewer, "StepGameCam", .04);
            Vector3 iconAt = bubble.GlobalPosition;
            Vector3 halfWidth = camera.GlobalBasis.X * (bubble.Texture.GetWidth() * bubble.PixelSize * .5f);
            float projectedWidth = camera.UnprojectPosition(iconAt + halfWidth).DistanceTo(camera.UnprojectPosition(iconAt - halfWidth));
            GD.Print($"SERVICE SMOKE park-camera behindRaw={gameCamera.Behind} aboveRaw={gameCamera.Above} dollyRaw={gameCamera.Dolly} "
                   + $"eyeWorld={camera.GlobalPosition} fov={camera.Fov} fullTextureQuadPixels={projectedWidth:F2}");
            Require(float.IsFinite(projectedWidth) && projectedWidth > 0 && !camera.IsPositionBehind(iconAt),
                    "default park camera measurement uses the actual projected bubble quad");
            await Capture(parkShot); camera.Transform = closeUp;
            for (int tick = 0; tick < 6000 && visitors.Relieved == 0; tick++) { Tick(); if (tick % 8 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            Field<ParkSim>(viewer, "_sim").SetOpen(ride.Id, false); Call(viewer, "PlaceActors", 1f);
            Require(visitors.Relieved == 1 && visitors.Needs.Of(guest.Id).Toilet == 0 && visitors.Needs.Of(guest.Id).Cash == 1234
                    && visitors.Walk.Guests.Count(g => g.Id == guest.Id) == 1 && !standing.ContainsKey(guest.Id),
                    "production handback answers the need and restores one walking customer");
            GD.Print($"SERVICE SMOKE handback guest={guest.Id} toilet={visitors.Needs.Of(guest.Id).Toilet} cash={visitors.Needs.Of(guest.Id).Cash} relieved={visitors.Relieved}");
            await Capture(afterShot);
            viewer.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("SERVICE SMOKE PASS (rendered captures require visual review)"); GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PrintErr("SERVICE SMOKE FAIL: " + ex); GetTree().Quit(2); }
    }
}
