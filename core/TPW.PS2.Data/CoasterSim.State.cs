using System.Text.Json.Serialization;
namespace TPW.PS2.Data;

public sealed partial class CoasterSim
{
    public const int StateVersion = 1;

    /// <summary>Tick-boundary simulation data only. Track identity and callbacks belong to the
    /// parent graph. TestLap statistics are return values, not fields of this owner; the parent
    /// must save them if retained. Silent includes the test/lift sound suppression latch.</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required int Status { get; init; }
        public required int Riders { get; init; }
        public required int WinchVersion { get; init; }
        public required long Now { get; init; }
        public required bool ParkOpen { get; init; }
        public required bool Silent { get; init; }
        public required TrainState[] Trains { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record TrainState
    {
        public required int Index { get; init; }
        public required float Pos { get; init; }
        public required int NodeId { get; init; }
        public required bool Chain { get; init; }
        public required bool Held { get; init; }
        public required float PrevSpeed { get; init; }
        public required CoasterTrainState State { get; init; }
        public required int Timer { get; init; }
        public required int Cursor { get; init; }
        public required int Attempts { get; init; }
        public required int RumbleCode { get; init; }
        public required int Splash { get; init; }
        public required int SpeedCode { get; init; }
        public required int SlewedSpeed { get; init; }
        public required bool FirstClimb { get; init; }
        public required long ChainExitAt { get; init; }
        public required long SplashOffAt { get; init; }
        public required long ClimbAt { get; init; }
        public required long DiveArmed { get; init; }
        public required int SoundNodeId { get; init; }
        public required long[] SpeedAt { get; init; }
        public required CarState[] Cars { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CarState
    {
        public required int NodeId { get; init; }
        public required float T { get; init; }
        public required float Speed { get; init; }
        public required int[] Riders { get; init; }
        public required CoasterVectorState Pos { get; init; }
        public required CoasterVectorState Side { get; init; }
        public required CoasterVectorState Up { get; init; }
        public required CoasterVectorState Fwd { get; init; }
        public required CoasterVectorState Pred { get; init; }
    }

    internal long SnapshotValueCount
    {
        get
        {
            CoasterStateValidation.Require(Trains.Count <= 6, "capture train bound");
            long count=0;
            foreach(var t in Trains)
            {
                CoasterStateValidation.Require(t!=null && t.Cars!=null && t.Cars.Length<=32, "capture car bound");
                count+=32;
                foreach(var c in t.Cars)
                { CoasterStateValidation.Require(c!=null && c.Riders.Count<=1024, "capture rider bound"); count+=32L+c.Riders.Count; }
            }
            return count;
        }
    }

    public State CaptureState()
    {
        _ = SnapshotValueCount;
        var nodes = Track.StateNodesForSim();
        var ids = nodes.Select((node, id) => (node, id)).ToDictionary(x => x.node, x => x.id);
        int Id(CoasterNode node)
        {
            if (node == null) return -1;
            CoasterStateValidation.Require(ids.TryGetValue(node, out int id) && Track.Owns(node), "foreign sim node");
            return id;
        }
        var state = new State
        {
            Version = StateVersion, Status = Status, Riders = Riders, WinchVersion = WinchVersion,
            Now = Now, ParkOpen = ParkOpen, Silent = _silent,
            Trains = Trains.Select(t => new TrainState
            {
                Index = t.Index,
                Pos = t.Pos,
                NodeId = Id(t.Node),
                Chain = t.Chain,
                Held = t.Held,
                PrevSpeed = t.PrevSpeed,
                State = t.State,
                Timer = t.Timer,
                Cursor = t.Cursor,
                Attempts = t.Attempts,
                RumbleCode = t.RumbleCode,
                Splash = t.Splash,
                SpeedCode = t.SpeedCode,
                SlewedSpeed = t.SlewedSpeed,
                FirstClimb = t.FirstClimb,
                ChainExitAt = t.ChainExitAt,
                SplashOffAt = t.SplashOffAt,
                ClimbAt = t.ClimbAt,
                DiveArmed = t.DiveArmed,
                SoundNodeId = Id(t.SoundNode),
                SpeedAt = t.SpeedAt.ToArray(),
                Cars = t.Cars.Select(c => new CarState
                {
                    NodeId = Id(c.Node),
                    T = c.T,
                    Speed = c.Speed,
                    Riders = c.Riders.ToArray(),
                    Pos = CoasterVectorState.Capture(c.Pos),
                    Side = CoasterVectorState.Capture(c.Side),
                    Up = CoasterVectorState.Capture(c.Up),
                    Fwd = CoasterVectorState.Capture(c.Fwd),
                    Pred = CoasterVectorState.Capture(c.Pred),
                }).ToArray(),
            }).ToArray(),
        };
        // Validate without stepping/rebuilding or mutating the source track.
        FromState(state, Track);
        return state;
    }

    /// <summary>Allocation and field copies only; does not spawn, step, test, pose, mark lifts,
    /// call terrain or emit callbacks. Resolves all node IDs on the separately restored track;
    /// trains, cars and sound cursors must reference owned nodes, not build-tool ghosts.
    /// The caller must supply the matching track snapshot, then bind callbacks after staging.</summary>
    public static CoasterSim FromState(State state, CoasterTrack track)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(track);
        CoasterStateValidation.Require(state.Version == StateVersion, "sim version");
        CoasterStateValidation.Require(state.Status is 2 or 3 or 4 or 5 or 6 or 7 or 10, "status");
        CoasterStateValidation.Require(state.Now >= 0 && state.Now <= long.MaxValue - 10000, "clock");
        CoasterStateValidation.Require(state.Trains != null && state.Trains.Length <= 6, "train count");
        CoasterStateValidation.Range(track.Type.CarsPerTrain, 1, 32, "trusted car count");
        CoasterStateValidation.Range(track.Type.Seats, 1, 1024, "trusted seat count");
        var sim = new CoasterSim(track)
        {
            Status = state.Status, Riders = state.Riders, WinchVersion = state.WinchVersion,
            Now = state.Now, ParkOpen = state.ParkOpen, _silent = state.Silent,
        };
        var riders = new HashSet<int>();
        var nodes = track.StateNodesForSim();
        if (track.Closed && track.Valid && state.Trains.Length != 0)
            CoasterStateValidation.Require(track.Nodes().All(n => n.Length > 0), "runnable segment length");
        CoasterNode Ref(int id, bool optional = false)
        {
            CoasterStateValidation.Range(id, optional ? -1 : 0, nodes.Count - 1, "sim node ID");
            var node = id == -1 ? null : nodes[id];
            CoasterStateValidation.Require(node == null || track.Owns(node), "sim node ownership");
            return node;
        }
        void Time(long value, long earliest, long latest) => CoasterStateValidation.Require(value >= earliest && value <= latest, "sound timestamp");
        foreach (var t in state.Trains)
        {
            CoasterStateValidation.Require(t != null && t.Index == sim.Trains.Count, "train index/order");
            CoasterStateValidation.Require(Enum.IsDefined(t.State), "train state");
            CoasterStateValidation.Require(t.Cars != null && t.Cars.Length == track.Type.CarsPerTrain && t.SpeedAt?.Length == 3, "car/cooldown lengths");
            CoasterStateValidation.Number(t.Pos, 0, 4096); CoasterStateValidation.Number(t.PrevSpeed, 0, 10000);
            CoasterStateValidation.Range(t.Timer, -D, TimerStart, "timer");
            CoasterStateValidation.Range(t.Cursor, 0, t.Cars.Length, "car cursor");
            CoasterStateValidation.Range(t.Attempts, -1, 40, "attempts");
            CoasterStateValidation.Require(t.RumbleCode is 0 or 10 or 20 or 30 or 40 or 50 or 60 or 70, "rumble");
            CoasterStateValidation.Require((t.Splash is 0 or 10) && (t.DiveArmed is 0 or 1), "sound latches");
            CoasterStateValidation.Range(t.SpeedCode, 0, 999, "speed code");
            CoasterStateValidation.Range(t.SlewedSpeed, 0, 999, "slewed speed");
            Time(t.ChainExitAt, -1, state.Now); Time(t.SplashOffAt, -1, state.Now + 1900);
            Time(t.ClimbAt, long.MinValue / 2, state.Now);
            foreach (long at in t.SpeedAt) Time(at, long.MinValue / 2, state.Now);
            var tr = new CoasterTrain
            {
                Index = t.Index,
                Pos = t.Pos,
                Node = Ref(t.NodeId),
                Chain = t.Chain,
                Held = t.Held,
                PrevSpeed = t.PrevSpeed,
                State = t.State,
                Timer = t.Timer,
                Cursor = t.Cursor,
                Attempts = t.Attempts,
                RumbleCode = t.RumbleCode,
                Splash = t.Splash,
                SpeedCode = t.SpeedCode,
                SlewedSpeed = t.SlewedSpeed,
                FirstClimb = t.FirstClimb,
                ChainExitAt = t.ChainExitAt,
                SplashOffAt = t.SplashOffAt,
                ClimbAt = t.ClimbAt,
                DiveArmed = t.DiveArmed,
                SoundNode = Ref(t.SoundNodeId, true),
                Cars = new CoasterCar[t.Cars.Length],
            };
            Array.Copy(t.SpeedAt, tr.SpeedAt, 3);
            for (int i = 0; i < t.Cars.Length; i++)
            {
                var c = t.Cars[i];
                CoasterStateValidation.Require(c != null && c.Riders != null && c.Riders.Length <= track.Type.Seats, "car/rider length");
                CoasterStateValidation.Number(c.T, -64, 4096); CoasterStateValidation.Number(c.Speed, 0, 10000);
                foreach (int id in c.Riders) CoasterStateValidation.Require(id >= 0 && riders.Add(id), "duplicate/negative rider ID");
                var car = new CoasterCar
                {
                    Node = Ref(c.NodeId),
                    T = c.T,
                    Speed = c.Speed,
                    Pos = CoasterStateValidation.Vector(c.Pos),
                    Side = CoasterStateValidation.Vector(c.Side),
                    Up = CoasterStateValidation.Vector(c.Up),
                    Fwd = CoasterStateValidation.Vector(c.Fwd),
                    Pred = CoasterStateValidation.Vector(c.Pred),
                };
                car.Riders.AddRange(c.Riders);
                tr.Cars[i] = car;
            }
            sim.Trains.Add(tr);
        }
        CoasterStateValidation.Require(state.Riders == riders.Count, "total riders");
        return sim;
    }
}
