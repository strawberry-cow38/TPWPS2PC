using System.Reflection;
using Godot;
using TPW.PS2.Data;
using Queues = TPW.PS2.Data.NativeRideQueues;

namespace TPWPS2Viewer.Tests;

/// <summary>`--native-ride-queues` in the real viewer (findings/native-ride-queue.md). A ride goes down
/// through the build menu, its queue is laid from the stub to the path as the queue tool lays runs, and
/// the native bus brings guests. Every tick checks that no queue passes 7, that each waiting guest
/// stands on its 117340 spot and is drawn there, and that nobody builds up in the legacy list. Two
/// fixture inputs:
/// - the ride is held full (VAR_CAPACITY 0) until six guests stand in line, because this ride boards as
///   fast as guests arrive and a standing line otherwise never forms;
/// - a guest in the middle of that line is set to impatience 81. It must leave with thought 9 while the
///   guests behind close up.
/// Capacity is then restored and boarding must resume from the front. TPW_QUEUE_FILM=dir also writes
/// frames of the queue from the moment the line stands.</summary>
public partial class NativeRideQueueSmoke : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo Member(string n) => typeof(Viewer).GetField(n, Hidden) ?? throw new MissingMemberException(n);
    static T Field<T>(Viewer v, string n) => (T)Member(n).GetValue(v);
    static void Set(Viewer v, string n, object o) => Member(n).SetValue(v, o);
    static object Call(Viewer v, string n, params object[] args)
    {
        var m = typeof(Viewer).GetMethod(n, Hidden) ?? throw new MissingMethodException(n);
        args = args.Concat(m.GetParameters().Skip(args.Length).Select(p => p.DefaultValue)).ToArray();
        return m.Invoke(v, args);
    }
    int checks; string detail = "";
    // I walk in, W wait, M move up, Q quit, R release: WalkIn and Waiting share a first letter.
    static char Letter(Queues.Step s) => s switch
    { Queues.Step.WalkIn => 'I', Queues.Step.Waiting => 'W', Queues.Step.MoveUp => 'M', Queues.Step.Quit => 'Q', _ => 'R' };
    void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); checks++; }

    static List<(int X, int Y)> Bfs((int X, int Y) from, Func<(int X, int Y), bool> goal, Func<(int X, int Y), bool> pass)
    {
        var previous = new Dictionary<(int, int), (int, int)> { [from] = from };
        var pending = new Queue<(int X, int Y)>();
        pending.Enqueue(from);
        while (pending.TryDequeue(out var c))
        {
            if (c != from && goal(c))
            {
                var run = new List<(int X, int Y)> { c };
                while (c != from) { c = previous[c]; run.Add(c); }
                run.Reverse();
                return run;
            }
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var n = (c.X + dx, c.Y + dy);
                if (previous.ContainsKey(n) || !goal(n) && !pass(n)) continue;
                previous[n] = c;
                pending.Enqueue(n);
            }
        }
        return null;
    }

    public override async void _Ready()
    {
        Viewer viewer = null; int exit = 2;
        try
        {
            Check(DisplayServer.GetName() != "headless", "rendering display required");
            string film = System.Environment.GetEnvironmentVariable("TPW_QUEUE_FILM");
            if (film != null) System.IO.Directory.CreateDirectory(film);
            viewer = new Viewer { Name = "Viewer" };
            Set(viewer, "_nativeRideQueues", true);
            if (System.Environment.GetEnvironmentVariable("TPW_QUEUE_IDLE") == "1")
            { Set(viewer, "_nativeGuestAnimation", true); Set(viewer, "_nativeIdleAll", true); }
            AddChild(viewer); viewer.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var park = Field<Park>(viewer, "_park");
            var lib = Field<AssetLibrary>(viewer, "_lib");
            var terrain = Field<Model>(viewer, "_terrainModel");
            var tool = Field<PathTool>(viewer, "_paths");
            var entry = Field<ParkEntrance>(viewer, "_entranceTable").Fit(terrain.Field,
                ParkEntrance.WalkwayColumnFromPoles(terrain), out _);
            var path = new List<(int, int)>();
            for (int z = entry.ZEnd; z < entry.ZEnd + 16; z++) path.Add((entry.XCol, z));
            Call(viewer, "LayLeg", path, PathTool.Kind.Path, 0); Call(viewer, "RefreshFloor");
            Check((bool)Call(viewer, "OpenGate"), "ordinary guest layer opens");

            Call(viewer, "ShowBuildCategory", "Rides");
            var rows = Field<List<int>>(viewer, "_buildRows");
            int chosen = Enumerable.Range(0, rows.Count).First(row =>
            {
                var d = (RideDefinition)Call(viewer, "DefinitionFor", lib.Rides[rows[row]].Model);
                var initial = NativeRideValue.CreateDefaultState(d?.CompiledEntry);
                return d?.Shape != null && initial.HasValue && NativeRideValue.Calculate(d.CompiledEntry, initial.Value) > 8;
            });
            Call(viewer, "ArmFromList", chosen);
            var blueprint = Field<Placement>(viewer, "_place"); bool placed = false;
            // Clear of the park gate's walls, so the queue is filmable and not hemmed in by the entrance.
            for (int z = entry.ZEnd + 8; z < park.Field.Height - 10 && !placed; z++)
                for (int x = entry.XCol + 6; x < Math.Min(park.Field.Width - 10, entry.XCol + 16) && !placed; x++)
                    if (blueprint.Fits(park, x, z))
                    {
                        Set(viewer, "_cursorOverride", (x, z));
                        try { Call(viewer, "PlaceHeld"); } finally { Set(viewer, "_cursorOverride", null); }
                        placed = park.Placed.Count == 1;
                    }
            Call(viewer, "CloseTool");
            Check(placed, "the build menu placed a positive-value ride beside the path");
            var sim = Field<ParkSim>(viewer, "_sim");
            var ride = sim.Rides.Single();
            Check(ride.Entrance is not null && ride.PlacementTurns.HasValue && ride.Definition?.CompiledEntry != null,
                $"{ride.Name} has a queue stub, a placement and a compiled record");

            // The queue, as the tool lays a run: from the stub to the park's path, over free ground.
            (int X, int Y) stub = (ride.Entrance.Value.X, ride.Entrance.Value.Z);
            bool MainPath((int X, int Y) c) => tool.KindAt(c.X, c.Y) == PathTool.Kind.Path && tool.OwnerAt(c.X, c.Y) == 0;
            bool Free((int X, int Y) c) => tool.CanLay(c.X, c.Y) && tool.KindAt(c.X, c.Y) == PathTool.Kind.None;
            var queueRun = Bfs(stub, MainPath, Free);
            Check(queueRun is { Count: >= 4 }, $"a queue run of {queueRun?.Count} cells reaches the path from the stub");
            Call(viewer, "LayLeg", queueRun, PathTool.Kind.Queue, ride.Id);
            if (ride.Exit is { } exitStub && Bfs((exitStub.X, exitStub.Z), MainPath, Free) is { } exitRun)
                Call(viewer, "LayLeg", exitRun, PathTool.Kind.Path, 0);
            Call(viewer, "RefreshFloor");
            var mouth = new ParkCell(queueRun[^1].X, queueRun[^1].Y);
            var shape = (NativeQueueShape)Call(viewer, "RideQueueShape", ride);
            Check(shape != null && tool.KindAt(mouth.X, mouth.Z) == PathTool.Kind.Both && shape.Mouth == mouth
                && shape.Cells.Select(c => (c.X, c.Z)).SequenceEqual(queueRun)
                && Math.Abs(shape.Entrance.X - stub.X) + Math.Abs(shape.Entrance.Z - stub.Y) == 1
                && shape.Entrance.X >= ride.Origin.X && shape.Entrance.X < ride.Origin.X + ride.Width
                && shape.Entrance.Z >= ride.Origin.Z && shape.Entrance.Z < ride.Origin.Z + ride.Height,
                $"the queue is read back from the tool: entrance connection {shape?.Entrance} inside the ride, {shape?.Cells.Count} cells, mouth {shape?.Mouth} on the path");

            // ⭐ The viewer's own walker follows the queue AS DRAWN (strawberry, 2026-09-25: guests were
            // "short-cutting from a path tile next to the queue tile of the entrance"): from the park's
            // path, a route to the stub enters at the mouth and covers every queue cell in order.
            var guestWalk = Field<GuestWalk>(viewer, "_guests");
            var approach = ParkPaths.Neighbours(mouth).FirstOrDefault(n => MainPath((n.X, n.Z)));
            var routeIn = guestWalk?.Route(approach, new ParkCell(stub.X, stub.Y));
            Check(guestWalk?.QueueStep != null && MainPath((approach.X, approach.Z)) && routeIn != null
                && routeIn.Skip(1).Select(c => (c.X, c.Z)).SequenceEqual(Enumerable.Reverse(queueRun)),
                $"the viewer's walker enters at the mouth and walks all {queueRun.Count} queue cells to the stub ({routeIn?.Count - 1} steps)");

            // Run the park. Checks every tick; the impatience input once a line has formed.
            var actors = Field<Dictionary<int, Node3D>>(viewer, "_actors");
            var camera = Field<Camera3D>(viewer, "_cam");
            Queues queues = null; ParkVisitors visitors = null; GuestWalk walk = null;
            int peak = 0, legacyPeak = 0, drawnChecks = 0, spotChecks = 0, frames = 0;
            Guest leaver = null; Guest[] behind = null; bool[] behindWaiting = null; int[] staggers = null; long triggerTime = -1;
            ParkCell? leaverOut = null; bool thought9 = false;
            var log = new System.Text.StringBuilder();
            Vector3 aim = default, filmFrom3 = default; float reach = 0;
            if (film != null)
            {
                foreach (var layer in viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = false;
                foreach (var ui in viewer.GetChildren().OfType<Control>()) ui.Visible = false;
                Set(viewer, "_freeCam", false);
                var cells = shape.Cells.Append(shape.Entrance).Select(c => park.CellCentre(c.X, c.Z)).ToList();
                aim = cells.Aggregate(Vector3.Zero, (a, b) => a + b) / cells.Count;
                reach = Math.Clamp(cells.Max(p => new Vector2(p.X - aim.X, p.Z - aim.Z).Length()) * 1.3f, 2f, 6f);
                // From the queue's own side: out from the ride's centre, past the line, looking back at it,
                // so the ride is behind the guests rather than in front of them.
                var rideAt = park.CellCentre(ride.Origin.X + ride.Width / 2, ride.Origin.Z + ride.Height / 2);
                var outward = new Vector3(aim.X - rideAt.X, 0, aim.Z - rideAt.Z);
                filmFrom3 = outward.LengthSquared() > 1e-4f ? outward.Normalized() : new Vector3(0, 0, 1);
            }
            int filmFrom = -1, restoreAt = -1, capacity = ride.Get("VAR_CAPACITY"), boardedBefore = -1;
            for (int t = 0; t < 30000; t++)
            {
                Call(viewer, "StepPark", .04);
                Call(viewer, "PlaceActors", 1f);
                queues ??= Field<Queues>(viewer, "_rideQueues");
                visitors ??= Field<ParkVisitors>(viewer, "_visitors");
                walk ??= Field<GuestWalk>(viewer, "_guests");
                if (queues == null) continue;
                if (filmFrom < 0 && ride.Get("VAR_CAPACITY") != 0) ride.Set("VAR_CAPACITY", 0); // fixture: held full
                var line = queues.Observations.Where(o => o.Index >= 0).OrderBy(o => o.Index).ToArray();
                peak = Math.Max(peak, line.Length);
                legacyPeak = Math.Max(legacyPeak, ride.Queue.Count);
                Check(line.Length <= queues.Capacity(ride), $"tick {t}: {line.Length} in a queue whose head count is {queues.Capacity(ride)}");
                Check(ride.Queue.Count <= 1, $"tick {t}: {ride.Queue.Count} guests in the legacy list");
                foreach (var o in line.Where(o => o.Step == Queues.Step.Waiting))
                {
                    Check(o.Spot is { } spot && o.Position == spot, $"tick {t}: guest {o.Guest.Id} waits at {o.Position}, not its spot {o.Spot}");
                    spotChecks++;
                    Check(actors.TryGetValue(o.Guest.Id, out var actor), $"tick {t}: queued guest {o.Guest.Id} has no body");
                    // The renderer's own mapping of the spot, so this asks whether the body is drawn from the
                    // native position rather than from the stub or a cell centre.
                    var want = (Vector3)Call(viewer, "GuestWorld", new Vector3(o.Position.X / 256f, 0, o.Position.Z / 256f),
                        NativeQueueSpots.CellOf(o.Position));
                    Check(new Vector2(actor.Position.X - want.X, actor.Position.Z - want.Z).Length() < 0.01f,
                        $"tick {t}: guest {o.Guest.Id} drawn at {actor.Position}, its spot maps to {want}");
                    drawnChecks++;
                }
                // The fixture's one input: once five wait in line, the third loses patience.
                if (leaver == null && line.Length >= 6 && line.All(o => o.Step == Queues.Step.Waiting))
                {
                    // (A boarding while held full would record Capacity 0 and fail OnRide < Capacity below.)
                    GD.Print($"[queue.smoke] tick {t}: line {string.Join("", line.Select(o => Letter(o.Step)))}; guest {line[2].Guest.Id} set to impatience 81");
                    leaver = line[2].Guest;
                    behind = line.Skip(3).Select(o => o.Guest).ToArray();
                    behindWaiting = line.Skip(3).Select(o => o.Step == Queues.Step.Waiting).ToArray();
                    var w = visitors.Needs.Of(leaver.Id); w.Unknown78 = 81; visitors.Needs.Set(leaver.Id, w);
                    filmFrom = t; restoreAt = t + 120; triggerTime = walk.Time;
                }
                if (t == restoreAt) { ride.Set("VAR_CAPACITY", capacity); boardedBefore = queues.Boarded; }
                // ⚠ A rendered frame can run NO park tick (ConsoleClock carries a double), so "at once" is
                // the first WALK tick after the input, not the next frame.
                else if (leaver != null && staggers == null && walk.Time > triggerTime)
                {
                    Check(walk.Time - triggerTime == GuestWalk.TickMilliseconds,
                        $"exactly one walk tick separates the input from this look ({(walk.Time - triggerTime) / GuestWalk.TickMilliseconds})");
                    var o = queues.Observations.Single(x => ReferenceEquals(x.Guest, leaver));
                    Check(o.Step == Queues.Step.Quit && o.Index < 0, "the impatient guest leaves the line at once");
                    // ⚠ Thought 9 IS written (the headless walked check asserts it), but the viewer's
                    // PlaceThoughts re-decides every visible guest's thought each frame and Decide has no
                    // BadQueue rule, so by the time a frame returns it is gone. The leave is counted instead.
                    thought9 = queues.Impatient == 1;
                    var moved = behind.Select(g => queues.Observations.Single(x => ReferenceEquals(x.Guest, g))).ToArray();
                    // Only a WAITING guest takes the move-up (20F588 code 6); one still walking in ignores it
                    // and finds its new spot when its own walk completes (case 3).
                    Check(moved.Select((m, i) => behindWaiting[i] ? m.Step == Queues.Step.MoveUp : m.Step != Queues.Step.MoveUp).All(ok => ok),
                        $"exactly the guests that were waiting behind it were told to move up: before {string.Join("", behindWaiting.Select(b => b ? 'W' : '-'))}, after {string.Join("", moved.Select(m => Letter(m.Step)))}");
                    var told = moved.Where(m => m.Step == Queues.Step.MoveUp).ToArray();
                    uint first = told.Min(m => m.Deadline);
                    staggers = told.Select(m => (int)(m.Deadline - first)).ToArray();
                    // The stagger grows by rand(3) per guest behind, waiting or not, so the waiting ones'
                    // deadlines step by multiples of 3; the first guest behind, if waiting, gets none.
                    Check(staggers.Zip(staggers.Skip(1)).All(p => p.Second >= p.First && (p.Second - p.First) % 3 == 0)
                        && (!behindWaiting[0] || moved[0].Deadline == first),
                        $"their move-up deadlines step by 3 × a cumulative rand(3): +{string.Join(",", staggers)}");
                }
                if (leaver != null && leaverOut == null && visitors.Plans.TryGetValue(leaver.Id, out var plan) && plan.Intent != VisitorIntent.Queueing)
                {
                    leaverOut = leaver.Cell;
                    GD.Print($"[queue.smoke] tick {t}: guest {leaver.Id} handed back at {leaverOut} as {plan.Intent}");
                }
                if (film != null && filmFrom >= 0 && t >= filmFrom - 40 && t < filmFrom + 400 && t % 2 == 0)
                {
                    camera.GlobalPosition = aim + filmFrom3 * reach * 0.45f + new Vector3(0, reach * 1.5f, 0); camera.LookAt(aim);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var image = GetViewport().GetTexture().GetImage();
                    if (image.SavePng(System.IO.Path.Combine(film, $"q-{frames:D3}.png")) != Error.Ok) throw new InvalidOperationException("frame save failed");
                    log.AppendLine($"q-{frames:D3} tick={t} line={line.Length} steps={string.Join("", line.Select(o => Letter(o.Step)))} boarded={queues.Boarded} quits={queues.Quits}");
                    frames++;
                }
                if (leaverOut != null && boardedBefore >= 0 && queues.Boarded >= boardedBefore + 3
                    && (film == null || t >= filmFrom + 400)) break;
                if (t % 2000 == 0) GD.Print($"[queue.smoke] tick {t}: line {string.Join("", line.Select(o => Letter(o.Step)))} joined={queues.Joined} refused={queues.Refused} boarded={queues.Boarded} quits={queues.Quits} guests={walk.Guests.Count} onride={ride.Get("VAR_ONRIDE")}/{ride.Get("VAR_CAPACITY")} open={visitors.Takes(ride)}");
                if (t % 32 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Check(queues != null && peak >= 5, $"a line formed (peak {peak}, head count {queues?.Capacity(ride)})");
            var lastSeen = leaver == null ? "none chosen" : queues.Observations.Where(o => ReferenceEquals(o.Guest, leaver))
                .Select(o => $"{o.Step} at {o.Position}").FirstOrDefault() ?? $"not held, plan {visitors.Plans.GetValueOrDefault(leaver.Id)?.Intent}";
            Check(leaver != null && thought9 && leaverOut == mouth,
                $"the impatient guest leaves (counted by the controller as impatient) and is handed back on the mouth (leaver {leaver?.Id}: {lastSeen}; thought9={thought9}; out at {leaverOut}; mouth {mouth}; peak {peak}; boarded {queues.Boarded})");
            var boardings = queues.Boardings.ToArray();
            Check(boardedBefore >= 0 && queues.Boarded >= boardedBefore + 3 && boardings.All(b => b.Step == Queues.Step.Waiting && b.Spot is { } s && b.Position == s
                    && b.OnRide < b.Capacity && b.LetMeOn == 0 && b.ScriptQueue == 0),
                $"{queues.Boarded} boardings, each a waiting head at the front while VAR_ONRIDE < VAR_CAPACITY ({string.Join(",", boardings.Select(b => $"{b.OnRide}<{b.Capacity}"))})");
            Check(legacyPeak <= 1 && visitors.Boardings == queues.Boarded, "every boarding came through the native queue; the legacy list never held more than the one being offered");
            Check(visitors.Plans.Where(p => p.Value.Intent == VisitorIntent.Queueing).All(p => walk.Guests.Any(g => g.Id == p.Key && queues.Owns(g))),
                "every Queueing plan is a guest this controller holds");
            if (film != null) System.IO.File.WriteAllText(System.IO.Path.Combine(film, "q-frames.txt"), log.ToString());
            detail = $"ride={ride.Name} cells={shape.Cells.Count} peak={peak} joined={queues.Joined} refused={queues.Refused} boarded={queues.Boarded} quits={queues.Quits} impatient={queues.Impatient} spotChecks={spotChecks} drawnChecks={drawnChecks} staggers=+{string.Join(",", staggers ?? Array.Empty<int>())} frames={frames}";
            exit = 0;
        }
        catch (Exception e) { GD.PrintErr($"NATIVE RIDE QUEUE SMOKE FAIL checks={checks}: {e}"); }
        finally
        {
            try
            {
                if (viewer != null && IsInstanceValid(viewer))
                { Call(viewer, "ResetNativeBus"); Field<RideSounds>(viewer, "_sounds")?.Clear(); viewer.QueueFree(); }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            }
            catch (Exception e) { exit = 2; GD.PrintErr("NATIVE RIDE QUEUE cleanup: " + e); }
        }
        if (exit == 0) GD.Print($"NATIVE RIDE QUEUE SMOKE PASS checks={checks}; {detail}; two fixture inputs (held full until six wait, then impatience 81)");
        GetTree().Quit(exit);
    }
}
