using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

public partial class StandingServiceAudit : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    int _bad, _checks;
    static FieldInfo Member(string name) => typeof(Viewer).GetField(name, Hidden) ?? throw new MissingMemberException("Viewer." + name);
    static T Field<T>(Viewer viewer, string name) => (T)Member(name).GetValue(viewer);
    static void Set(Viewer viewer, string name, object value) => Member(name).SetValue(viewer, value);
    static object Call(Viewer viewer, string name, params object[] args) =>
        (typeof(Viewer).GetMethod(name, Hidden) ?? throw new MissingMemberException("Viewer." + name)).Invoke(viewer, args);
    void Check(bool ok, string label) { _checks++; GD.Print((ok ? "STANDING SERVICE ok: " : "STANDING SERVICE FAIL: ") + $"[{_checks}] " + label); if (!ok) _bad++; }

    public override async void _Ready()
    {
        Viewer viewer = null; AssetLibrary library = null; Node3D stage = null;
        try
        {
            string disc = OS.GetEnvironment("TPW_PS2_DISC");
            EntryStubFallbackChecks.Run(disc, (ok, label) => Check(ok, "entry-stub fallback: " + label));
            library = new AssetLibrary(disc); library.OpenWad("/DATA/JUNGLE.WAD");
            var wad = library.Wad;
            var terrain = new Model(wad.Read(wad.Find("/terrain/terrain_1.mps")));
            var definition = RideDefinition.Parse(System.Text.Encoding.ASCII.GetString(wad.Read(wad.Find("/Features/Toilet/Toilet.sam"))), "toilet");
            RideDefinition Shop(string path) => RideDefinition.Parse(
                System.Text.Encoding.ASCII.GetString(wad.Read(wad.Find(path))), path);
            var ice = Shop("/Shops/IceCream/IceCream.sam");
            var shopOrigin = new ParkCell(10, 20);
            // Independent literal oracle for the 2x2 **;2* shape, entry .6/.4.
            // The 1x1 cycle cannot prove the dimensions/entry-cell part of this transform.
            var shopPoints = new[] { new Vector2(.6f,.6f), new Vector2(1.4f,.6f), new Vector2(1.4f,1.4f), new Vector2(.6f,1.4f) };
            var shopCells = new[] { new ParkCell(10,20), new ParkCell(11,20), new ParkCell(11,21), new ParkCell(10,21) };
            for (int turn = 0; turn < 4; turn++)
            {
                bool created = StandingServicePose.TryCreate(ice, shopOrigin, turn, out var pose);
                Check(created && pose.CellPoint.DistanceTo(new Vector2(10,20) + shopPoints[turn]) < .0001f,
                      $"external shop quarter turn {turn} uses literal authored 2x2 geometry");
                Check(created && pose.HeightCell == shopCells[turn],
                      $"external shop quarter turn {turn} samples its transformed entry cell height");
                var oriented = new Placement(); oriented.Arm(ice, "Ice Cream", 0, Park.Footprint.From(ice.Shape)); oriented.Turn(turn);
                Check(created && pose.Inward == new Vector2I(-oriented.Turned.EntryDX, -oriented.Turned.EntryDY),
                      $"external shop quarter turn {turn} faces inward from actual placement entry");
            }
            Check(!StandingServicePose.TryCreate(Shop("/Shops/Coconut/Coconut.sam"), shopOrigin, 0, out _),
                  "coordinate-less external shop does not invent authored stand geometry");
            Check(!StandingServicePose.TryCreate(Shop("/Shops/Balloon/Balloon.sam"), shopOrigin, 0, out _),
                  "larger LIMBO shop does not receive the small-shop standing fallback");
            Check(!StandingServicePose.TryCreate(Shop("/Rides/Monkey/Monkey.sam"), shopOrigin, 0, out _),
                  "ordinary ride does not become a standing shop");
            var paths = new ParkPaths(terrain);
            int material = Enumerable.Range(1, paths.Materials.Count - 1).First(i => ParkPaths.Classify(paths.Materials[i]) == ParkPathKind.Path);
            var origin = paths.Cells.First(c => Enumerable.Range(0, 3).All(i => paths.CanLay(c.Offset(i, 0))));
            for (int i = 0; i < 3; i++) paths.Lay(origin.Offset(i, 0), material);
            var entrance = origin.Offset(1, 0); var exit = origin.Offset(2, 0);
            var sim = new ParkSim(paths);
            var script = wad.Read(wad.Find("/Features/Toilet/Toilet.rse"));
            var anim = new TPW.PS2.Data.Animation(wad.Read(wad.Find("/Features/Toilet/toilet.aps")));
            byte[] Sibling(string name) => wad.Find("/Features/Toilet/" + name) is { } entry ? wad.Read(entry) : null;
            var ride = sim.Add(1, "Small Toilet", origin, 1, 1, script, anim, 1, entrance, exit, out var fault,
                               sibling: Sibling, definition: definition) ?? throw new Exception(fault);
            sim.SetOpen(1, true); ride.Set("VAR_BROKEN", 0);
            var visitors = new ParkVisitors(sim, new GuestWalk(paths)) { Needs = new VisitorNeeds(73) };
            foreach (string key in visitors.Needs.Rates.Keys.ToArray()) visitors.Needs.Rates[key] = new VisitorNeeds.Rate(0, 0, false);
            var guest = visitors.Arrive(entrance, entrance);
            var wants = visitors.Needs.Of(guest.Id); wants.Toilet = 91; wants.Hunger = wants.Thirst = wants.Sick = 0;
            wants.Happiness = 50; wants.Cash = 1234; visitors.Needs.Set(guest.Id, wants);

            viewer = new Viewer(); Set(viewer, "_discPath", disc); Set(viewer, "_lib", library);
            Call(viewer, "BuildUi");
            stage = new Node3D(); AddChild(stage);
            // Viewer intentionally never enters the tree in this headless fixture. Give
            // production placement particles the SAME live stage as models/guests instead
            // of allowing StartScript to lazily parent an emitter under a detached Viewer.
            Set(viewer, "_burst", Call(viewer, "MakeParticles"));
            foreach (Node child in viewer.GetChildren()) { viewer.RemoveChild(child); stage.AddChild(child); }
            var park = new Park(); stage.AddChild(park.Root); Set(viewer, "_park", park);
            var guestRoot = new Node3D(); stage.AddChild(guestRoot); Set(viewer, "_guestRoot", guestRoot);
            var thoughts = Field<ThoughtBubbles>(viewer, "_thoughts");
            thoughts.Load(library.ReadGeneric);
            Check(thoughts.Ready, "real disc thought artwork loaded");
            Set(viewer, "_guests", visitors.Walk); Set(viewer, "_visitors", visitors); Set(viewer, "_sim", sim);
            // The real guest consumer must follow the built plot, not raw data/overlay bounds.
            // This rotated, translated, non-unit grid gives literal independent expectations.
            var framedPark = new Park { BaseY = 7 };
            framedPark.PlotSpace = new Park.Plot(new Transform3D(
                new Basis(new Vector3(0,0,-2), Vector3.Up, new Vector3(3,0,0)), new Vector3(20,0,-30)),
                Vector3.Zero, new Vector3(4,0,3));
            framedPark.Build(4,3); Set(viewer, "_park", framedPark);
            var framedPoint = (Vector3)Call(viewer, "GuestWorld", new Vector3(1.25f,.25f,1.5f), new ParkCell(1,1));
            Check(framedPoint.DistanceTo(new Vector3(24.5f,7.25f,-32.5f)) < .0001f,
                  "guest frame uses built plot translation rotation scale and fractional cell position");
            Check(((Vector3)Call(viewer, "GuestHeading", Vector3.Right)).Normalized().DistanceTo(Vector3.Forward) < .0001f,
                  "guest frame maps grid X through the built plot direction");
            Check(((Vector3)Call(viewer, "GuestHeading", Vector3.Back)).Normalized().DistanceTo(Vector3.Right) < .0001f,
                  "guest frame maps grid Z through the built plot direction");
            var shiftedPlot = framedPark.PlotSpace.Value;
            framedPark.PlotSpace = shiftedPlot with { ToWorld = new Transform3D(shiftedPlot.ToWorld.Basis, new Vector3(-12,0,44)) };
            Check(((Vector3)Call(viewer, "GuestWorld", new Vector3(1.25f,.25f,1.5f), new ParkCell(1,1)))
                    .DistanceTo(new Vector3(-7.5f,7.25f,41.5f)) < .0001f,
                  "guest frame follows changed plot instead of retaining an old origin");
            Set(viewer, "_park", park); framedPark.Root.Free();
            var serviceRoot = new Node3D(); stage.AddChild(serviceRoot);
            Call(viewer, "RegisterStandingService", ride, serviceRoot, 0);
            // Actual actor creation/animation consumer, not a counter or placeholder mesh.
            Call(viewer, "PlaceActors", 1f);
            var actors = Field<Dictionary<int, Node3D>>(viewer, "_actors");
            Check(actors.TryGetValue(guest.Id, out var original) && original != null && original.IsInsideTree(), "real guest body exists before service");
            visitors.SendTo(guest, ride); visitors.Step(0, null);
            Check(visitors.QueuedOwner(guest.Id) == ride && !visitors.Walk.Guests.Any(), "actual coordinator transfers ownership without a walking duplicate");
            Check(!ParkSim.Chain(ride.Machine).Any(m => m.GuestIds.Contains(guest.Id)), "small-toilet control is not a HUSH-stack rider");
            Call(viewer, "PlaceActors", 1f);
            Check(actors.TryGetValue(guest.Id, out var serving) && serving != null && !serving.IsQueuedForDeletion(),
                  "standing-service body remains in the actual viewer draw set after handover");
            Check(ReferenceEquals(original, serving), "handover retains the same actual actor");
            var standing = Field<Dictionary<int, Transform3D>>(viewer, "_standing");
            Check(standing.ContainsKey(guest.Id), "authoritative queued owner produces a standing pose");
            // Let the real script consume the mailbox; no fabricated HUSH membership.
            for (int i = 0; i < 500 && (ride.Queue.Contains(guest.Id) || ride.Get("VAR_LETMEON") == guest.Id); i++)
                visitors.Step(0.04, null);
            Check(visitors.QueuedOwner(guest.Id) == ride && !ride.Queue.Contains(guest.Id)
                  && ride.Get("VAR_LETMEON") != guest.Id && visitors.Relieved == 0,
                  "real script accepts customer before satisfaction");
            var offsets = new[] { new Vector2(.5f,.2f), new Vector2(.8f,.5f), new Vector2(.5f,.8f), new Vector2(.2f,.5f) };
            for (int turn = 0; turn < 4; turn++)
            {
                Call(viewer, "RegisterStandingService", ride, serviceRoot, turn);
                Call(viewer, "PlaceActors", 1f);
                var at = actors[guest.Id];
                Vector3 expected = new(paths.Origin.X + origin.X + offsets[turn].X, park.CellY(origin.X, origin.Z),
                                       -(paths.Origin.Y + origin.Z + offsets[turn].Y));
                Check(at.Position.DistanceTo(expected) < 0.0001f, $"quarter turn {turn} uses authored fractional stand point");
                Check(Mathf.Abs(at.Basis.Determinant() - 1) < .0001f && at.Basis.Y.DistanceTo(Vector3.Up) < .0001f,
                      $"quarter turn {turn} keeps an upright full body");
                var orientation = new Placement(); orientation.Arm(definition, "Small Toilet", 1402, Park.Footprint.From(definition.Shape)); orientation.Turn(turn);
                var inward = new Vector3(-orientation.Turned.EntryDX, 0, orientation.Turned.EntryDY);
                Check(at.Basis.Z.DistanceTo(inward) < .0001f, $"quarter turn {turn} faces inward using the placement door convention");
                Check(at.FindChildren("*body*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Any(m => m.IsVisibleInTree())
                      && at.FindChildren("*legs*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Any(m => m.IsVisibleInTree()),
                      $"quarter turn {turn} draws body and legs, not just a seated head");
                var bubble = thoughts.Root.GetChildren().OfType<Sprite3D>().Single();
                Check(bubble.Visible && bubble.GlobalPosition.DistanceTo(at.GlobalPosition + Vector3.Up * thoughts.Height) < .0001f,
                      $"quarter turn {turn} thought follows this frame's actor position");
            }
            Check(visitors.Needs.Of(guest.Id).Toilet == 91 && visitors.Needs.Of(guest.Id).Cash == 1234,
                  "service transit neither satisfies nor reseeds needs");
            ride.Host.GuestVisible(guest.Id, false); Call(viewer, "PlaceActors", 1f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!actors.ContainsKey(guest.Id) && !standing.ContainsKey(guest.Id), "explicit host hiding suppresses standing body");
            Check(!thoughts.Root.GetChildren().OfType<Sprite3D>().Any(b => b.Visible), "hidden customer has no orphan thought bubble");
            ride.Host.GuestVisible(guest.Id, true); Call(viewer, "PlaceActors", 1f);
            Check(actors.ContainsKey(guest.Id) && standing.ContainsKey(guest.Id), "host reveal restores outside-service body");
            ride.Host.HeadAt(0, guest.Id); Call(viewer, "PlaceActors", 1f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!standing.ContainsKey(guest.Id), "even unresolved seat ownership cannot acquire a duplicate standing body");
            ride.Host.HeadAt(0, 0); Call(viewer, "PlaceActors", 1f);
            Check(standing.ContainsKey(guest.Id), "removing seat ownership restores standing fallback");
            // Genuine handback, not a synthetic setter on the needs table.
            for (int i = 0; i < 6000 && visitors.Relieved == 0; i++) visitors.Step(.04, null);
            sim.SetOpen(ride.Id, false); // prevent a legitimate second random visit
            Call(viewer, "PlaceActors", 1f);
            Check(visitors.Relieved == 1 && visitors.Rides == 1, "real script handback dispatches one service");
            Check(visitors.Needs.Of(guest.Id).Toilet == 0 && visitors.Needs.Of(guest.Id).Cash == 1234,
                  "completed service clears toilet without reseeding cash");
            Check(visitors.Soil.TryGetValue(ride.Id, out int soil) && soil == 20, "need 91 yields decoded soil 20");
            Check(visitors.Walk.Guests.Count(g => g.Id == guest.Id) == 1 && !standing.ContainsKey(guest.Id)
                  && actors.ContainsKey(guest.Id), "handback has one walking body and no standing duplicate");
            Check(!thoughts.Root.GetChildren().OfType<Sprite3D>().Any(b => b.Visible), "satisfied guest clears visible toilet thought");
            for (int i = 0; i < 50; i++) visitors.Step(.04, null);
            Check(visitors.Relieved == 1 && visitors.Soil[ride.Id] == 20, "later ticks do not repeat satisfaction or soil");
            // A removed owner cannot transfer its presentation to a replacement with the same ID.
            sim.SetOpen(ride.Id, true);
            var second = visitors.Arrive(entrance, entrance);
            Check(visitors.SendTo(second, ride), "removal control enters a second customer");
            visitors.Step(0, null); Call(viewer, "PlaceActors", 1f);
            Check(standing.ContainsKey(second.Id), "removal begins with a visible owned customer");
            sim.Remove(ride.Id);
            var replacement = sim.Add(ride.Id, "replacement", origin, 1, 1, script, anim, 1, entrance, exit, out fault,
                                      sibling: Sibling, definition: definition) ?? throw new Exception(fault);
            Call(viewer, "RegisterStandingService", replacement, serviceRoot, 0);
            Call(viewer, "PlaceActors", 1f);
            Check(visitors.QueuedOwner(second.Id) == null && !standing.ContainsKey(second.Id),
                  "same numeric ride ID does not inherit removed owner's customer");
            visitors.Step(0, null); Call(viewer, "PlaceActors", 1f);
            Check(visitors.Walk.Guests.Count(g => g.Id == second.Id) == 1 && actors.ContainsKey(second.Id),
                  "aborted removal recovers a single walking body");
            Check(!StandingServicePose.TryCreate(null, origin, 0, out _), "missing definition cannot invent a standing pose");
            // End-of-fixture direct pose consumer check: a live queued customer with WALK ownership is excluded.
            sim.SetOpen(replacement.Id, true);
            var third = visitors.Arrive(entrance, entrance); visitors.SendTo(third, replacement); visitors.Step(0, null);
            replacement.Host.WalkerPose(third.Id, 0, 1, 0, 0, 0); Call(viewer, "PlaceActors", 1f);
            Check(!standing.ContainsKey(third.Id), "unresolved scripted WALK ownership does not gain a standing duplicate");
            // Actual press consumer, real placeable mesh/APS/RSE and authored path stubs.
            // Deliberately separate from the controlled lifecycle fixture above: no claim that
            // Viewer._Ready, physical mouse hit-testing, or visual appearance was exercised.
            var placementPaths = new ParkPaths(terrain);
            placementPaths.Field.Cells = terrain.Field.Cells;
            var tool = new PathTool(terrain, PathPieces.ReadExecutable(library.Executable()));
            park.SetPaths(placementPaths); park.Build(terrain.Field.Width, terrain.Field.Height);
            Set(viewer, "_paths", tool); Set(viewer, "_walkGrid", placementPaths);
            Set(viewer, "_rideSerial", 100);
            var placedSim = new ParkSim(placementPaths); Set(viewer, "_sim", placedSim);
            // The audit's live scene parent replaces only the audio attachment parent;
            // Viewer itself deliberately stays unready. Audio uses the Dummy driver.
            var sounds = new RideSounds(stage, new SoundCatalogue(library.Disc, "JUNGLE", 1), new SoundCatalogue(library.Disc, "JUNGLE", 2));
            Set(viewer, "_sounds", sounds);
            var blueprint = Field<Placement>(viewer, "_place");
            blueprint.GroundAt = tool.KindAt;
            var assets = library.Rides.Single(r => r.Model.Path.Equals("/Features/Toilet/Toilet.mps", StringComparison.OrdinalIgnoreCase));
            Set(viewer, "_armedRide", assets);
            var registered = Field<Dictionary<ParkRide, (Node3D Root, StandingServicePose Pose)>>(viewer, "_standingPlaces");
            for (int turn = 0; turn < 4; turn++)
            {
                blueprint.Arm(definition, "Small Toilet", 1402, Park.Footprint.From(definition.Shape), false);
                blueprint.Turn(turn);
                var cell = placementPaths.Cells.First(c => blueprint.Fits(park, c.X, c.Z));
                Set(viewer, "_cursorOverride", (cell.X, cell.Z));
                Call(viewer, "PlaceHeld");
                var placed = placedSim.Rides.SingleOrDefault(r => r.Id == 101 + turn);
                Check(placed != null && park.Placed.Count == turn + 1 && !blueprint.Active,
                      $"actual placement press quarter turn {turn} places model and starts script");
                Check(placed != null && registered.TryGetValue(placed, out var place)
                      && place.Root.IsInsideTree() && place.Root.GetChildCount() > 0
                      && place.Pose.CellPoint.DistanceTo(new Vector2(cell.X, cell.Z) + offsets[turn]) < .0001f,
                      $"actual placement quarter turn {turn} registers the real model and authored standing geometry");
                Check(placed?.Entrance is { } door && placementPaths.Open(door),
                      $"actual placement quarter turn {turn} lays a walkable service stub");
                if (placed?.Entrance is { } stub && registered.TryGetValue(placed, out var registration))
                {
                    // Independent phase anchor: the actual placement stub, not another stand
                    // transform. Project to horizontal distance so uneven ground cannot decide it.
                    Vector2 centre = new(cell.X + .5f, cell.Z + .5f);
                    Vector2 point = registration.Pose.CellPoint;
                    Vector2 opposite = 2 * centre - point;
                    Vector2 stubPoint = new(stub.X + .5f, stub.Z + .5f);
                    Check(point.DistanceSquaredTo(stubPoint) < opposite.DistanceSquaredTo(stubPoint),
                          $"placed quarter turn {turn} authored stand is nearer its real stub than its mirror");
                }
                if (placed?.Entrance is { } start)
                {
                    var live = new ParkVisitors(placedSim, new GuestWalk(placementPaths)) { Needs = new VisitorNeeds(4242) };
                    foreach (string key in live.Needs.Rates.Keys.ToArray()) live.Needs.Rates[key] = new VisitorNeeds.Rate(0,0,false);
                    var customer = live.Arrive(start, start);
                    var need = live.Needs.Of(customer.Id); need.Toilet = 91; need.Cash = 1234;
                    need.Hunger = need.Thirst = need.Sick = 0; need.Happiness = 50; live.Needs.Set(customer.Id, need);
                    Set(viewer, "_guests", live.Walk); Set(viewer, "_visitors", live);
                    live.SendTo(customer, placed); live.Step(0, null);
                    for (int tick = 0; tick < 500 && (placed.Queue.Contains(customer.Id) || placed.Get("VAR_LETMEON") == customer.Id); tick++)
                        live.Step(.04, null);
                    Call(viewer, "PlaceActors", 1f);
                    Check(live.QueuedOwner(customer.Id) == placed && standing.ContainsKey(customer.Id)
                          && actors.TryGetValue(customer.Id, out var drawn) && drawn.IsInsideTree() && !drawn.IsQueuedForDeletion(),
                          $"placed quarter turn {turn} serves with a real standing guest body");
                    for (int tick = 0; tick < 6000 && live.Relieved == 0; tick++) live.Step(.04, null);
                    placedSim.SetOpen(placed.Id, false); Call(viewer, "PlaceActors", 1f);
                    Check(live.Relieved == 1 && live.Needs.Of(customer.Id).Toilet == 0 && live.Needs.Of(customer.Id).Cash == 1234,
                          $"placed quarter turn {turn} completes real relief without reseeding");
                    Check(live.Walk.Guests.Count(g => g.Id == customer.Id) == 1 && actors.ContainsKey(customer.Id)
                          && !standing.ContainsKey(customer.Id), $"placed quarter turn {turn} hands back exactly one walking body");
                }

            }
            // Default-gate consumer control: metadata checks alone cannot catch forgetting
            // RegisterStandingService's fallback call. Use a real outside-shop script here;
            // needs/purchase arithmetic is covered by the normal-startup six-shop smoke.
            var stubDefinition = Shop("/Shops/Coconut/Coconut.sam");
            var stubAsset = library.Rides.Single(r => r.Model.Path.Equals("/Shops/Coconut/Coconut.mps", StringComparison.OrdinalIgnoreCase));
            var stubSim = new ParkSim(paths);
            byte[] StubSibling(string name) => wad.Find("/Shops/Coconut/" + name) is { } e ? wad.Read(e) : null;
            var stubRide = stubSim.Add(700, "Coconut fallback", origin, 2, 2, library.Read(stubAsset.Script),
                new TPW.PS2.Data.Animation(library.Read(stubAsset.Animation)), 1, entrance, exit, out var stubFault,
                sibling: StubSibling, definition: stubDefinition) ?? throw new Exception(stubFault);
            stubSim.SetOpen(stubRide.Id, true); stubRide.Set("VAR_BROKEN", 0);
            var stubVisitors = new ParkVisitors(stubSim, new GuestWalk(paths));
            var stubGuest = stubVisitors.Arrive(entrance, entrance);
            Set(viewer, "_sim", stubSim); Set(viewer, "_visitors", stubVisitors); Set(viewer, "_guests", stubVisitors.Walk);
            var stubRoot = new Node3D(); stage.AddChild(stubRoot);
            Call(viewer, "RegisterStandingService", stubRide, stubRoot, 0);
            Check(registered.TryGetValue(stubRide, out var stubRegistration) && stubRegistration.Pose.IsEntryStubFallback,
                  "entry-stub consumer: actual viewer registration selects tagged policy");
            Check(stubVisitors.SendTo(stubGuest, stubRide), "entry-stub consumer: real coordinator accepts route");
            stubVisitors.Step(0, null);
            for (int tick = 0; tick < 500 && (stubRide.Queue.Contains(stubGuest.Id) || stubRide.Get("VAR_LETMEON") == stubGuest.Id); tick++)
                stubVisitors.Step(.04, null);
            Check(stubVisitors.QueuedOwner(stubGuest.Id) == stubRide && stubVisitors.Rides == 0
                  && !stubRide.Queue.Contains(stubGuest.Id) && stubRide.Get("VAR_LETMEON") != stubGuest.Id,
                  "entry-stub consumer: real shop accepts before handback");
            Call(viewer, "PlaceActors", 1f);
            var actualArrival = stubGuest.Position;
            var expectedStub = (Vector3)Call(viewer, "GuestWorld", new Vector3(actualArrival.X,actualArrival.Y,actualArrival.Z), stubGuest.Cell);
            Check(standing.ContainsKey(stubGuest.Id) && actors.TryGetValue(stubGuest.Id, out var stubBody)
                  && !stubBody.IsQueuedForDeletion() && stubBody.Position.DistanceTo(expectedStub) < .0001f,
                  "entry-stub consumer: actual body holds retained arrival position");
            stubRide.Host.GuestVisible(stubGuest.Id, false); Call(viewer, "PlaceActors", 1f);
            Check(!standing.ContainsKey(stubGuest.Id) && !actors.ContainsKey(stubGuest.Id),
                  "entry-stub consumer: explicit host hide still wins");
            stubRide.Host.GuestVisible(stubGuest.Id, true); Call(viewer, "PlaceActors", 1f);
            Check(standing.ContainsKey(stubGuest.Id) && actors.ContainsKey(stubGuest.Id),
                  "entry-stub consumer: host reveal restores the one body");
            for (int tick = 0; tick < 6000 && stubVisitors.Rides == 0; tick++) stubVisitors.Step(.04, null);
            stubSim.SetOpen(stubRide.Id, false); Call(viewer, "PlaceActors", 1f);
            Check(stubVisitors.Rides == 1 && stubVisitors.Walk.Guests.Count(g => g.Id == stubGuest.Id) == 1
                  && actors.ContainsKey(stubGuest.Id) && !standing.ContainsKey(stubGuest.Id),
                  "entry-stub consumer: genuine handback restores one walker without a standing duplicate");
            Check(!stubDefinition.Fields.ContainsKey("UsageInfo.EntryCellStandPosX")
                  && !stubDefinition.Fields.ContainsKey("UsageInfo.EntryCellStandPosY"),
                  "entry-stub consumer: entire lifecycle leaves absent authored coordinates absent");
            // Preserve synthetic/uncompiled standing controls; compiled relief has its own lifecycle.
            await NativeReliefPresentationChecks.Run(viewer, stage, library, terrain, disc, Check);
            sounds.Clear();
            stage.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // Clear stops the fixture's voices and the frames above free their nodes, but
            // headless process frames can outrun the Dummy audio thread. Let its pending
            // stop/fade mix retire AudioStreamPlaybackWAV references before engine shutdown.
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            GD.Print(_bad == 0 ?  $"STANDING SERVICE PASS: {_checks} checks, 0 failures" : $"STANDING SERVICE FAIL: {_bad}"); GetTree().Quit(_bad == 0 ? 0 : 2);
        }
        catch (Exception ex) { GD.PrintErr("STANDING SERVICE ERROR: " + ex); GetTree().Quit(2); }
        finally
        {
            if (viewer != null)
            {
                Field<AssetLibrary>(viewer, "_charLib")?.Dispose();
                if (GodotObject.IsInstanceValid(viewer)) viewer.Free();
            }
            if (GodotObject.IsInstanceValid(stage) && !stage.IsQueuedForDeletion()) stage.QueueFree();
            library?.Dispose();
        }
    }
}
