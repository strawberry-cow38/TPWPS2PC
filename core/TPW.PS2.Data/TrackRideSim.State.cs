using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

public sealed partial class TrackRideSim
{
    public const int StateVersion = 1;
    /// <summary>Defensive save allocation bound, not a clamp on the live Capacity setting.</summary>
    public const int SnapshotCarLimit = 1024;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CarState
    {
        public required int? Guest { get; init; }
        public required bool IsKart { get; init; }
        public required int Index { get; init; }
        public required ushort Distance { get; init; }
        public required int Total { get; init; }
        public required sbyte Lap { get; init; }
        public required short Lateral { get; init; }
        public required short Heading { get; init; }
        public required sbyte Speed { get; init; }
        public required byte Target { get; init; }
        public required int Rank { get; init; }
        public required bool Finished { get; init; }
        public required int Colour { get; init; }
        public required byte BaseMax { get; init; }
        public required byte BaseAccel { get; init; }
        public required byte Accel { get; init; }
        public required byte State { get; init; }
        public required byte Timer { get; init; }
        public required bool Aggressive { get; init; }
        public required short PushBack { get; init; }
        public required short Wobble { get; init; }
        public required short WobbleRate { get; init; }
    }

    /// <summary>All owned simulation storage. The parent graph owns the track ID and supplies
    /// that resolved TrackLayout to FromState. Queue, wear, breakdown and event callbacks are
    /// deliberately absent: the parent binds them after the entire graph has been staged.</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required bool Karts { get; init; }
        public required TrackRideStatus Status { get; init; }
        public required int RunTimer { get; init; }
        public required int Speed { get; init; }
        public required int Capacity { get; init; }
        public required int Duration { get; init; }
        public required SnapshotRandom.State Random { get; init; }
        public required CarState[] Cars { get; init; }
    }

    public State CaptureState()
    {
        if (_cars.Count > SnapshotCarLimit)
            throw new ArgumentException("Track ride exceeds snapshot car bound.");
        var state = new State
        {
            Version = StateVersion, Karts = Karts, Status = Status, RunTimer = RunTimer,
            Speed = Speed, Capacity = Capacity, Duration = Duration, Random = _rng.CaptureState(),
            Cars = _cars.Select(c => new CarState
            {
                Guest = c.Guest,
                IsKart = c.IsKart,
                Index = c.Index,
                Distance = c.Distance,
                Total = c.Total,
                Lap = c.Lap,
                Lateral = c.Lateral,
                Heading = c.Heading,
                Speed = c.Speed,
                Target = c.Target,
                Rank = c.Rank,
                Finished = c.Finished,
                Colour = c.Colour,
                BaseMax = c.BaseMax,
                BaseAccel = c.BaseAccel,
                Accel = c.Accel,
                State = c.State,
                Timer = c.Timer,
                Aggressive = c.Aggressive,
                PushBack = c.PushBack,
                Wobble = c.Wobble,
                WobbleRate = c.WobbleRate,
            }).ToArray()
        };
        ValidateState(state, Track);
        return state;
    }

    // Allocation only; the public constructor calls Rebuilt, which is NOT a restore operation.
    private TrackRideSim(State state, TrackLayout track, SnapshotRandom random)
    {
        Track = track;
        Karts = state.Karts;
        Status = state.Status;
        RunTimer = state.RunTimer;
        Speed = state.Speed;
        Capacity = state.Capacity;
        Duration = state.Duration;
        _rng = random;
        foreach (var c in state.Cars)
            _cars.Add(new TrackCar
            {
                Guest = c.Guest,
                IsKart = c.IsKart,
                Index = c.Index,
                Distance = c.Distance,
                Total = c.Total,
                Lap = c.Lap,
                Lateral = c.Lateral,
                Heading = c.Heading,
                Speed = c.Speed,
                Target = c.Target,
                Rank = c.Rank,
                Finished = c.Finished,
                Colour = c.Colour,
                BaseMax = c.BaseMax,
                BaseAccel = c.BaseAccel,
                Accel = c.Accel,
                State = c.State,
                Timer = c.Timer,
                Aggressive = c.Aggressive,
                PushBack = c.PushBack,
                Wobble = c.Wobble,
                WobbleRate = c.WobbleRate,
            });
    }

    /// <summary>Restore into new owners with no gameplay transitions or callbacks. The supplied
    /// track must be the parent's independently resolved/staged layout, never the old live owner.</summary>
    public static TrackRideSim FromState(State state, TrackLayout track)
    {
        ValidateState(state, track);
        var random = SnapshotRandom.FromState(state.Random);
        return new TrackRideSim(state, track, random);
    }

    static void ValidateState(State s, TrackLayout track)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(track);
        if (s.Version != StateVersion || !Enum.IsDefined(s.Status) || s.Random == null
            || s.Cars == null || s.Cars.Length > SnapshotCarLimit)
            throw new ArgumentException("Invalid track ride version, status, RNG or car collection.");
        foreach (var c in s.Cars)
            if (c == null || c.IsKart != s.Karts || c.Index is < 0 or >= SnapshotCarLimit
                || c.Rank is < 0 or > SnapshotCarLimit || c.Colour is < 0 or > 3
                || c.Heading is < 0 or > 4095 || c.State > 6)
                throw new ArgumentException("Invalid track car state.");
        // Preserve native narrow-field wraparound (distance/lap/timer), signed totals, duplicate
        // guest IDs and slot indices after removals, and unclamped public settings verbatim.
        // No status/Closed inference: editing/opening and parent breakdown handling are separate.
    }
}
