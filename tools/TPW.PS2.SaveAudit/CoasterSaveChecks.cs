using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;

/// <summary>Disc-free public consumer of coaster snapshots. This does not save a park, bind
/// guests/assets automatically, or exercise the Viewer. No reflection or friend assembly.</summary>
public static class CoasterSaveChecks
{
    const string TypeKey = "fixture/coaster/MineCart/v1";
    static readonly ParkCell ExitCell = new(40, 41), EntryCell = new(35, 41);
    static readonly int[] Hills = { 375, 800, 1200, 1200, 400, 100, 300, 375, 375 };
    static readonly int[] Cliff = { 1280, 0, 0, 0, 0, 0, 0, 375, 375 };
    static string Json<T>(T value) => JsonSerializer.Serialize(value);
    static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(Json(value))!;

    // ParkSimAudit/CoasterChecks.Build's public oval and hill/cliff fixtures. Copy the small
    // builder instead of coupling this consumer to that audit's internal helpers/assembly.
    static CoasterTrack Build(int[] heights, bool close = true)
    {
        var t = new CoasterTrack(CoasterType.All[0], ExitCell, 3, EntryCell);
        (int X, int Z)[] oval = { (46, 41), (51, 45), (51, 51), (46, 55), (40, 55),
            (34, 55), (29, 51), (29, 46), (31, 41) };
        for (int i = 0; i < oval.Length; i++)
            t.AddPylon(new(oval[i].X, oval[i].Z), heights[i], 0, false, CoasterNodeKind.Normal);
        if (close) t.AddPylon(EntryCell, 0, 0, false, CoasterNodeKind.Normal);
        return t;
    }

    public sealed record Bundle(CoasterTrack.State Track, CoasterSim.State Sim, CoasterStats Stats);

    // Record equality covers every scalar/enum/vector property, including future additions;
    // explicitly compare the arrays by contents, not reference. No runtime reflection.
    static bool Same(CoasterTrack.State a, CoasterTrack.State b) =>
        a with { PylonIds = b.PylonIds, Nodes = b.Nodes } == b
        && a.PylonIds.SequenceEqual(b.PylonIds) && a.Nodes.Length == b.Nodes.Length
        && a.Nodes.Zip(b.Nodes).All(p =>
            p.First with { P = p.Second.P, S = p.Second.S, Samples = p.Second.Samples } == p.Second
            && p.First.P.SequenceEqual(p.Second.P) && p.First.S.SequenceEqual(p.Second.S)
            && p.First.Samples.SequenceEqual(p.Second.Samples));
    static bool Same(CoasterSim.State a, CoasterSim.State b) =>
        a with { Trains = b.Trains } == b && a.Trains.Length == b.Trains.Length
        && a.Trains.Zip(b.Trains).All(p =>
            p.First with { Cars = p.Second.Cars, SpeedAt = p.Second.SpeedAt } == p.Second
            && p.First.SpeedAt.SequenceEqual(p.Second.SpeedAt) && p.First.Cars.Length == p.Second.Cars.Length
            && p.First.Cars.Zip(p.Second.Cars).All(c =>
                c.First with { Riders = c.Second.Riders } == c.Second
                && c.First.Riders.SequenceEqual(c.Second.Riders)));

    sealed class Inputs
    {
        public readonly Queue<int> Queue;
        public readonly List<string> Events = new();
        public int Boarded, Released, Screams, Wear, Breakdowns;
        public Inputs(IEnumerable<int> queue) { Queue = new(queue); }
        public void Bind(CoasterSim s)
        {
            s.TakeHead = () =>
            {
                int? id = Queue.Count == 0 ? null : Queue.Dequeue();
                Events.Add($"{s.Now}:take:{id}");
                if (id != null) Boarded++;
                return id;
            };
            s.Released += id => { Released++; Events.Add($"{s.Now}:release:{id}"); };
            s.Scream += (t, code) => { Screams++; Events.Add($"{s.Now}:scream:{t.Index}:{code}"); };
            s.Wear = () => { Wear++; Events.Add($"{s.Now}:wear"); };
            s.BreakdownCheck = () => { Breakdowns++; Events.Add($"{s.Now}:breakdown"); };
        }
    }

    static bool PublicPoses(CoasterSim a, CoasterSim b) =>
        a.Trains.Count == b.Trains.Count && a.Trains.Zip(b.Trains).All(t =>
            t.First.Cars.Length == t.Second.Cars.Length && t.First.Cars.Zip(t.Second.Cars).All(c =>
                c.First.Pos == c.Second.Pos && c.First.Side == c.Second.Side && c.First.Up == c.Second.Up
                && c.First.Fwd == c.Second.Fwd && c.First.Pred == c.Second.Pred
                && c.First.Riders.SequenceEqual(c.Second.Riders)
                && a.Track.GetNodeId(c.First.Node) == b.Track.GetNodeId(c.Second.Node)));

    public static void Run(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        void Check(bool ok, string why) => check(ok, "coaster snapshot: " + why);
        void Reject(Action action, string why)
        {
            bool rejected = false;
            try { action(); }
            catch (ArgumentException) { rejected = true; }
            catch (JsonException) { rejected = true; }
            Check(rejected, "reject " + why);
        }
        CoasterTrack Restore(CoasterTrack.State s) => CoasterTrack.FromState(s, TypeKey, CoasterType.All[0],
            (_, _) => throw new Exception("Restore/Step must not query terrain or rebuild geometry"));
        void Required<T>(T value)
        {
            var obj = JsonNode.Parse(Json(value))!.AsObject();
            foreach (string key in obj.Select(p => p.Key).ToArray())
            {
                var bad = obj.DeepClone().AsObject(); bad.Remove(key);
                Reject(() => JsonSerializer.Deserialize<T>(bad.ToJsonString()), typeof(T).Name + " missing " + key);
            }
        }

        // Asset-family identity is not inferred from the MineCart continuation fixture.
        // All fourteen table types survive a cold/open checkpoint with their own station heights.
        foreach(var type in CoasterType.All)
        {
            string key=$"coaster/{type.World}/{type.Park}/{type.Ordinal}/{type.Folder}";
            var t=new CoasterTrack(type,ExitCell,3,EntryCell);
            var saved=Copy(t.CaptureState(key));
            var copy=CoasterTrack.FromState(saved,key,type,(_,_)=>throw new Exception("unexpected ground query"));
            var sim=new CoasterSim(t);var resumed=CoasterSim.FromState(Copy(sim.CaptureState()),copy);
            Check(ReferenceEquals(copy.Type,type)&&Same(saved,copy.CaptureState(key))&&Same(sim.CaptureState(),resumed.CaptureState()),
                type.Name+": independent type key/station/cold sim preserved");
        }

        // Empty, incomplete, invalid, stack and one-way build-tool ghost graphs are not a
        // closed/running special case. Preserve their exact caches and links, not regenerated ones.
        var cold = new CoasterTrack(CoasterType.All[0], ExitCell, 3, EntryCell);
        var open = Build(Hills, false);
        open.AddPylon(open.Pylons[0].Cell, 375, 100, false, CoasterNodeKind.Normal);
        var ghost = open.LinkGhost(open.Pylons[0].Cell, 400, -100);
        open.Pylons[2].Valid = false;
        // Deliberately stale but finite cache values demonstrate that restore does not Recompute.
        open.Pylons[0].P[0] = new(17, 19, 23);
        open.Pylons[0].S[1] = new(2, 3, 5);
        open.Pylons[0].Samples[2].W1 = 71;
        open.Pylons[0].Samples[2].Winch = true;
        foreach (var track in new[] { cold, open })
        {
            var ts = Copy(track.CaptureState(TypeKey)); var restored = Restore(ts);
            var sim = new CoasterSim(track); sim.SetOpen(true);
            var ss = Copy(sim.CaptureState()); var resumed = CoasterSim.FromState(ss, restored);
            Check(Same(ts, restored.CaptureState(TypeKey)) && Same(ss, resumed.CaptureState()),
                "cold/open graph and sim JSON exactly preserved");
            for (int i = 0; i < 50; i++) { sim.Step(); resumed.Step(); }
            Check(!restored.Closed && resumed.Trains.Count == 0 && Same(sim.CaptureState(), resumed.CaptureState()),
                "open incomplete track stays unspawned under real Step");
            Check(resumed.TestLap() == CoasterStats.None, "open incomplete test lap returns None");
        }
        var openCopy = Restore(Copy(open.CaptureState(TypeKey)));
        int ghostId = open.GetNodeId(ghost);
        Check(!openCopy.Owns(openCopy.ResolveNodeId(ghostId))
            && openCopy.ResolveNodeId(ghostId).Below == openCopy.Pylons[^1]
            && openCopy.Pylons[^1].Above == null
            && openCopy.Pylons[^1].Below == openCopy.Pylons[0]
            && openCopy.Pylons[0].Above == openCopy.Pylons[^1], "stack/ghost topology and ownership preserved");
        Check(openCopy.ResolveNodeId(-1) == null && openCopy.GetNodeId(null!) == -1, "null node ID is -1");
        Reject(() => openCopy.GetNodeId(open.Exit), "node owned by another track");
        Reject(() => openCopy.ResolveNodeId(-2), "negative node ID other than null");

        // Also preserve loop/lead-in discriminators and caches (the main continuation fixture
        // below exercises native gravity, not loop physics).
        var loop = new CoasterTrack(CoasterType.All[0], ExitCell, 3, EntryCell);
        loop.AddPylon(new(46, 41), 375, 0, false, CoasterNodeKind.Normal);
        loop.AddPylon(new(52, 41), 0, 0, true, CoasterNodeKind.LeadIn);
        loop.AddPylon(new(52, 42), 0, 0, true, CoasterNodeKind.Loop);
        loop.AddPylon(new(58, 42), 375, 0, false, CoasterNodeKind.Normal);
        var loopState = Copy(loop.CaptureState(TypeKey));
        Check(Same(loopState, Restore(loopState).CaptureState(TypeKey)), "loop and lead-in caches roundtrip");

        var originalTrack = Build(Cliff);
        var original = new CoasterSim(originalTrack);
        var input = new Inputs(Enumerable.Range(1, 2000)); input.Bind(original);
        original.SetOpen(true);
        int warm = 0;
        while (warm++ < 12000)
        {
            original.Step();
            if (original.Trains.Count >= 2 && original.Trains.All(t => t.Cars.Any(c => c.Riders.Count > 0))
                && original.Trains.Any(t => t.State == CoasterTrainState.Run && t.Pos > 2)) break;
        }
        Check(warm < 12000 && original.Riders > 1 && original.Trains.Count == 2
            && original.Trains.All(t => t.Cars.Length == 4), "native multi-train/four-car occupied checkpoint reached");
        var trackState = Copy(originalTrack.CaptureState(TypeKey));
        var simState = Copy(original.CaptureState());
        Check(simState.Status == 10 && simState.Now > 0 && simState.WinchVersion > 0
            && simState.Trains.Any(t => t.SoundNodeId >= 0 && t.SlewedSpeed > 0)
            && trackState.Nodes.SelectMany(n => n.Samples).Any(s => s.Winch), "nondefault status/clock/sound/winch fixture");
        Required(trackState); Required(trackState.Nodes[0]); Required(trackState.Nodes[0].Samples[0]);
        Required(trackState.Nodes[0].P[0]); Required(simState); Required(simState.Trains[0]); Required(simState.Trains[0].Cars[0]);

        int callsBefore = input.Events.Count;
        var restoredTrack = Restore(trackState);
        var restoredSim = CoasterSim.FromState(simState, restoredTrack);
        Check(input.Events.Count == callsBefore && restoredSim.TakeHead == null && restoredSim.Wear == null
            && restoredSim.BreakdownCheck == null && Same(trackState, restoredTrack.CaptureState(TypeKey))
            && Same(simState, restoredSim.CaptureState()) && PublicPoses(original, restoredSim),
            "restore only allocates/copies: no terrain, callbacks, spawn, pose, winch rebuild or gameplay replay");
        bool ids = true;
        for (int i = 0; i < trackState.Nodes.Length; i++)
            ids &= restoredTrack.GetNodeId(restoredTrack.ResolveNodeId(i)) == i
                && !ReferenceEquals(originalTrack.ResolveNodeId(i), restoredTrack.ResolveNodeId(i));
        ids &= restoredSim.Trains.All(t => ReferenceEquals(t.Node, restoredTrack.ResolveNodeId(simState.Trains[t.Index].NodeId))
            && t.Cars.All(c => restoredTrack.Owns(c.Node)));
        Check(ids, "all IDs stable within snapshot; train/car nodes rebound to fresh graph");
        var restoredInput = new Inputs(input.Queue); restoredInput.Bind(restoredSim);
        input.Events.Clear();
        int boardedBefore = input.Boarded, releasedBefore = input.Released, screamsBefore = input.Screams;
        int wearBefore = input.Wear, breakdownBefore = input.Breakdowns;
        bool statesMatch = true, eventsMatch = true, posesMatch = true, moving = false;
        int firstDivergence = -1, arrivals = 0;
        var trainStates = original.Trains.Select(t => t.State).ToArray();
        Vector3 firstPose = original.Trains[0].Cars[0].Pos;
        for (int tick = 0; tick < 6000; tick++)
        {
            original.Step(); restoredSim.Step();
            bool equal = Same(original.CaptureState(), restoredSim.CaptureState())
                && Same(originalTrack.CaptureState(TypeKey), restoredTrack.CaptureState(TypeKey));
            if (!equal && firstDivergence < 0) firstDivergence = tick;
            statesMatch &= equal;
            eventsMatch &= input.Events.SequenceEqual(restoredInput.Events) && input.Queue.SequenceEqual(restoredInput.Queue);
            input.Events.Clear(); restoredInput.Events.Clear();
            posesMatch &= PublicPoses(original, restoredSim);
            moving |= Vector3.Distance(firstPose, original.Trains[0].Cars[0].Pos) > 5;
            foreach (var tr in original.Trains)
            {
                if (trainStates[tr.Index] == CoasterTrainState.Run && tr.State == CoasterTrainState.Dwell) arrivals++;
                trainStates[tr.Index] = tr.State;
            }
        }
        Check(statesMatch, $"6000 native Steps: every typed track/cache/winch/train/car/cooldown field exact (first divergence {firstDivergence})");
        Check(eventsMatch && posesMatch, "every tick: ordered queue/boarding/release/scream/wear/breakdown events and public rider/car poses exact");
        Check(moving && arrivals > 4 && input.Boarded > boardedBefore && input.Released > releasedBefore
            && input.Screams > screamsBefore && input.Wear > wearBefore && input.Breakdowns - breakdownBefore == 6000,
            $"non-inert continuation: {arrivals} arrivals, {input.Boarded - boardedBefore} board, {input.Released - releasedBefore} release, "
            + $"{input.Screams - screamsBefore} screams, {input.Wear - wearBefore} wear callbacks");

        // Supplement the naturally reached checkpoint with legal nonzero sound deadlines and
        // latches. These fields are private natively, so change the public typed DTO, never reflect.
        var sound = Copy(simState);
        sound.Trains[0] = sound.Trains[0] with { RumbleCode = 40, Splash = 10, SpeedCode = 270,
            SlewedSpeed = 230, FirstClimb = false, ChainExitAt = sound.Now - 200,
            SplashOffAt = sound.Now + 1900, ClimbAt = sound.Now - 1200, DiveArmed = 0,
            SoundNodeId = 2, SpeedAt = new[] { sound.Now - 100, sound.Now - 200, sound.Now - 300 } };
        var soundA = CoasterSim.FromState(sound, Restore(trackState));
        var soundB = CoasterSim.FromState(Copy(soundA.CaptureState()), Restore(trackState));
        var soundInputA = new Inputs(Enumerable.Range(10000, 100)); soundInputA.Bind(soundA);
        var soundInputB = new Inputs(soundInputA.Queue); soundInputB.Bind(soundB);
        bool soundsEqual = Same(sound, soundA.CaptureState());
        for (int i = 0; i < 150; i++)
        {
            soundA.Step(); soundB.Step();
            soundsEqual &= Same(soundA.CaptureState(), soundB.CaptureState())
                && soundInputA.Events.SequenceEqual(soundInputB.Events);
        }
        Check(soundsEqual, "forged legal nonzero private sound cooldowns roundtrip and continue for 150 native ticks");
        foreach (int status in new[] { 2, 3, 4, 5, 6, 7, 10 })
        {
            var s = simState with { Status = status, ParkOpen = false };
            var a = CoasterSim.FromState(s, Restore(trackState));
            var b = CoasterSim.FromState(Copy(a.CaptureState()), Restore(trackState));
            var ai = new Inputs(Array.Empty<int>()); ai.Bind(a);
            var bi = new Inputs(Array.Empty<int>()); bi.Bind(b);
            for (int i = 0; i < 30; i++) { a.Step(); b.Step(); }
            Check(Same(a.CaptureState(), b.CaptureState()) && ai.Events.SequenceEqual(bi.Events)
                && (status != 5 || a.Trains[0].Pos == s.Trains[0].Pos), $"status {status}, park closed preserved and continued");
        }

        // Test-lap statistics are RETURNED, not owned by CoasterSim. A parent keeps the record
        // separately. Prove both the post-test runtime and this public record survive a JSON file.
        var statsTrack = Build(Hills); var statsSim = new CoasterSim(statsTrack);
        var lap = statsSim.TestLap();
        var bundle = new Bundle(statsTrack.CaptureState(TypeKey), statsSim.CaptureState(), lap);
        string file = Path.GetTempFileName();
        Bundle disk;
        try { File.WriteAllText(file, Json(bundle)); disk = JsonSerializer.Deserialize<Bundle>(File.ReadAllText(file))!; }
        finally { File.Delete(file); }
        var lapTrack = Restore(disk.Track); var lapSim = CoasterSim.FromState(disk.Sim, lapTrack);
        Check(lap.Duration > 1 && lap.Length > 0 && lap.MaxSpeed > 20 && disk.Stats == lap
            && Same(bundle.Track, lapTrack.CaptureState(TypeKey)) && Same(bundle.Sim, lapSim.CaptureState()),
            "test-lap stats + post-test trains/winch state JSON file roundtrip, without rerunning lap on restore");
        Check(lapSim.TestLap() == statsSim.TestLap(), "explicit later test lap produces identical stats");

        // All arrays are detached in both directions. Deliberately mutate each mutable layer,
        // including nested arrays, after restoration; immutable records need no deep alias test.
        var ownTrackDto = Copy(trackState); var ownSimDto = Copy(simState);
        var ownTrack = Restore(ownTrackDto); var ownSim = CoasterSim.FromState(ownSimDto, ownTrack);
        void Mutate(CoasterTrack.State t, CoasterSim.State s)
        {
            t.Nodes[0].P[0] = t.Nodes[0].P[0] with { X = -77 };
            t.Nodes[0].S[0] = t.Nodes[0].S[0] with { Y = -88 };
            t.Nodes[0].Samples[0] = t.Nodes[0].Samples[0] with { Len = 999 };
            t.PylonIds[0] = -1; t.Nodes[1] = t.Nodes[0];
            s.Trains[0].SpeedAt[0] = 123;
            var car = s.Trains.SelectMany(tr => tr.Cars).First(c => c.Riders.Length != 0);
            car.Riders[0] = 999999;
            s.Trains[0].Cars[0] = s.Trains[0].Cars[0] with { Speed = 999 };
            s.Trains[1] = s.Trains[0];
        }
        Mutate(ownTrackDto, ownSimDto);
        Check(Same(trackState, ownTrack.CaptureState(TypeKey)) && Same(simState, ownSim.CaptureState()), "restore inputs have no mutable aliases");
        Mutate(ownTrack.CaptureState(TypeKey), ownSim.CaptureState());
        Check(Same(trackState, ownTrack.CaptureState(TypeKey)) && Same(simState, ownSim.CaptureState()), "capture results have no mutable aliases");
        var peerTrack = Restore(trackState); var peerSim = CoasterSim.FromState(simState, peerTrack);
        ownTrack.Exit.P[0] = Vector3.Zero; ownTrack.Exit.S[0] = Vector3.Zero;
        ownTrack.Exit.Samples[0] = default; ownTrack.Exit.Next = null!;
        ownSim.Trains[0].Cars[0].Riders.Add(999998); ownSim.Trains[0].Cars[0].Pos = Vector3.Zero;
        Check(Same(trackState, peerTrack.CaptureState(TypeKey)) && Same(simState, peerSim.CaptureState()), "fresh runtime graphs do not alias each other");

        // Bad candidates are never published. Neither a failed track allocation nor a failed
        // sim allocation may mutate an existing track/sim or run callbacks on it.
        string stableTrack = Json(originalTrack.CaptureState(TypeKey)), stableSim = Json(original.CaptureState());
        int stableEvents = input.Events.Count;
        void BadTrack(Func<CoasterTrack.State, CoasterTrack.State> forge, string why) =>
            Reject(() => Restore(forge(Copy(trackState))), why);
        void BadNode(Func<CoasterTrack.NodeState, CoasterTrack.NodeState> forge, string why) =>
            BadTrack(s => { s.Nodes[2] = forge(s.Nodes[2]); return s; }, why);
        void BadSim(Func<CoasterSim.State, CoasterSim.State> forge, string why) =>
            Reject(() => CoasterSim.FromState(forge(Copy(simState)), restoredTrack), why);
        void BadTrain(Func<CoasterSim.TrainState, CoasterSim.TrainState> forge, string why) =>
            BadSim(s => { s.Trains[0] = forge(s.Trains[0]); return s; }, why);
        void BadCar(Func<CoasterSim.CarState, CoasterSim.CarState> forge, string why) =>
            BadTrain(t => { t.Cars[0] = forge(t.Cars[0]); return t; }, why);
        BadTrack(s => s with { Version = 999 }, "track version");
        BadTrack(s => s with { TypeKey = "wrong" }, "asset registry key mismatch");
        Reject(() => CoasterTrack.FromState(trackState, TypeKey, CoasterType.All[1], (_, _) => 0), "asset type mismatch");
        BadTrack(s => s with { TypeOrdinal = 8 }, "asset ordinal mismatch");
        BadTrack(s => s with { ExitId = 1 }, "duplicate station IDs");
        BadTrack(s => { s.PylonIds[1] = s.PylonIds[0]; return s; }, "duplicate pylon IDs");
        BadTrack(s => s with { PylonIds = new[] { 999 } }, "out-of-range pylon IDs");
        BadTrack(s => s with { Nodes = Array.Empty<CoasterTrack.NodeState>() }, "empty node array");
        BadTrack(s => s with { Nodes = new CoasterTrack.NodeState[CoasterTrack.MaxStateNodes + 1] }, "oversized node array");
        BadTrack(s => { s.Nodes[0] = null!; return s; }, "null node");
        BadTrack(s => s with { Valid = !s.Valid }, "inconsistent aggregate validity");
        BadNode(n => n with { NextId = 999 }, "dangling next ID");
        BadNode(n => n with { PrevId = -2 }, "negative prev ID");
        BadNode(n => n with { AboveId = 999 }, "dangling stack ID");
        BadNode(n => n with { AboveId = 2 }, "cyclic above stack");
        BadNode(n => n with { BelowId = 2 }, "cyclic below stack");
        BadNode(n => n with { NextId = 2 }, "nonstation next cycle");
        BadNode(n => n with { PrevId = 2 }, "nonstation prev cycle");
        BadTrack(s => { s.Nodes[0] = s.Nodes[0] with { NextId = 0 }; return s; }, "exit-only cycle (would hang MarkLift)");
        BadTrack(s => { s.Nodes[1] = s.Nodes[1] with { NextId = 1 }; return s; }, "entry-only cycle");
        var openState = Copy(open.CaptureState(TypeKey));
        openState.Nodes[0] = openState.Nodes[0] with { NextId = 0 };
        Reject(() => Restore(openState), "open exit-only cycle (would hang placement)");
        BadTrack(s => s with { Nodes = s.Nodes.Append(s.Nodes[2]).ToArray() }, "unreachable node IDs");
        BadNode(n => n with { IsStation = true }, "pylon claiming station identity");
        BadNode(n => n with { Kind = (CoasterNodeKind)99 }, "unknown node kind");
        BadNode(n => n with { P = Array.Empty<CoasterVectorState>() }, "control point array length");
        BadNode(n => n with { S = null! }, "null side array");
        BadNode(n => n with { Samples = new CoasterTrack.SampleState[16] }, "sample array length");
        BadNode(n => n with { Length = float.NaN }, "NaN node length");
        BadNode(n => n with { Length = 0 }, "zero-length runnable segment (would divide by zero)");
        BadNode(n => { n.P[0] = n.P[0] with { X = float.PositiveInfinity }; return n; }, "nonfinite vector");
        BadNode(n => { n.Samples[0] = n.Samples[0] with { W1 = float.NaN }; return n; }, "NaN sample cache");
        BadSim(s => s with { Version = 999 }, "sim version");
        BadSim(s => s with { Status = 11 }, "unknown sim status");
        BadSim(s => s with { Now = -1 }, "negative clock");
        BadSim(s => s with { Trains = null! }, "null train array");
        BadSim(s => s with { Trains = new CoasterSim.TrainState[7] }, "train count bound");
        BadSim(s => s with { Riders = s.Riders + 1 }, "rider total mismatch");
        BadSim(s => { s.Trains[1] = s.Trains[1] with { Index = 0 }; return s; }, "duplicate train IDs");
        BadTrain(t => t with { NodeId = 999 }, "train node ID");
        var ghostSim = Copy(simState);
        ghostSim.Trains[0] = ghostSim.Trains[0] with { NodeId = ghostId };
        Reject(() => CoasterSim.FromState(ghostSim, openCopy), "train referring to unowned build ghost");
        BadTrain(t => t with { SoundNodeId = 999 }, "sound node ID");
        BadTrain(t => t with { State = (CoasterTrainState)99 }, "unknown train state");
        BadTrain(t => t with { Pos = float.NaN }, "NaN train position");
        BadTrain(t => t with { Cursor = -1 }, "negative boarding cursor");
        BadTrain(t => t with { Cars = Array.Empty<CoasterSim.CarState>() }, "wrong trusted car count");
        BadTrain(t => t with { SpeedAt = new long[2] }, "cooldown array length");
        BadTrain(t => t with { SplashOffAt = simState.Now + 1901 }, "invalid sound deadline");
        BadTrain(t => t with { SpeedAt = new[] { simState.Now + 1, 0L, 0L } }, "future speed cooldown");
        BadCar(c => c with { NodeId = -1 }, "null car node");
        BadCar(c => c with { Speed = float.NaN }, "NaN car speed");
        BadCar(c => c with { Pred = c.Pred with { Y = float.NaN } }, "NaN prediction pose");
        BadCar(c => c with { Riders = new int[CoasterType.All[0].Seats + 1] }, "seat capacity");
        BadCar(c => c with { Riders = new[] { -1 } }, "negative rider ID");
        BadSim(s =>
        {
            s.Trains[0].Cars[0] = s.Trains[0].Cars[0] with { Riders = new[] { 77 } };
            s.Trains[1].Cars[0] = s.Trains[1].Cars[0] with { Riders = new[] { 77 } };
            return s;
        }, "duplicate rider IDs across trains");
        Check(Json(originalTrack.CaptureState(TypeKey)) == stableTrack && Json(original.CaptureState()) == stableSim
            && input.Events.Count == stableEvents, "all rejected restores leave original graph/runtime/callbacks untouched");
        // BadSim deliberately used an existing track as its target; prove it too was untouched.
        Check(Same(originalTrack.CaptureState(TypeKey), restoredTrack.CaptureState(TypeKey)), "failed sim staging never mutates supplied live track");
    }
}
