#nullable enable
using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

public partial class StaffMember
{
    public const int SnapshotVersion = 1;

    /// <summary>One pool slot, including never-hired and stale fired state. Object keys belong
    /// to the enclosing registry, not to Serial. Candidate and Target may name objects outside
    /// the currently active world (including deferred world/research registry entries).</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot
    {
        public required int Version { get; init; }
        public required StaffKind Kind { get; init; }
        public required int PoolSlot { get; init; }
        public required bool Active { get; init; }
        public required uint Serial { get; init; }
        public required int GoalDepth { get; init; }
        public required byte[] Goals { get; init; }
        public required short PositionX { get; init; }
        public required short PositionZ { get; init; }
        public required string? TargetKey { get; init; }
        public required uint Stamp { get; init; }
        public required NativeGuestRoute.State? Route { get; init; }
        public required ulong RouteEpoch { get; init; }
        public required ushort Flags { get; init; }
        public required byte Mode { get; init; }
        public required byte State { get; init; }
        public required int LogicalRequest { get; init; }
        public required int FacingQuarterTurns { get; init; }
        public required string? CandidateKey { get; init; }
        public required int HireDay { get; init; }
        public required sbyte PatrolX0 { get; init; }
        public required sbyte PatrolZ0 { get; init; }
        public required sbyte PatrolX1 { get; init; }
        public required sbyte PatrolZ1 { get; init; }
        public required int Level { get; init; }
        public required int SpeedBits { get; init; }
        public required int CandidateSlot { get; init; }
        public required sbyte MotivationCopy { get; init; }
        public required sbyte Tiredness { get; init; }
        public required sbyte Morale { get; init; }
        public required long WaypointsRetired { get; init; }
        public required long BadStates { get; init; }
        public required Handyman.JobSnapshot? Handyman { get; init; }
        public required Mechanic.JobSnapshot? Mechanic { get; init; }
        public required Researcher.JobSnapshot? Researcher { get; init; }
    }

    /// <summary>Detached inventory, including inactive slots' stale targets; no providers run.</summary>
    public IReadOnlyList<object> ReferencedTargets => Target == null ? Array.Empty<object>() : new[] { Target };
    /// <summary>Candidate is also an external identity, even after firing.</summary>
    public IReadOnlyList<object> ReferencedSnapshotObjects => new object?[] { Target, Candidate }
        .Where(o => o != null).Cast<object>().Distinct(ReferenceEqualityComparer.Instance).ToArray();

    static void SnapshotRequire(bool ok, string why)
    { if (!ok) throw new ArgumentException("Invalid StaffMember snapshot: " + why); }
    static bool SnapshotKey(string key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 1024;

    /// <summary>Only the explicit identity callback is invoked. No lazy Candidates access,
    /// random draws, world providers, gameplay callbacks, route changes or activation.</summary>
    public Snapshot CaptureState(Func<object, string> objectId)
    {
        ArgumentNullException.ThrowIfNull(objectId);
        string? Key(object? value)
        {
            if (value == null) return null;
            var key = objectId(value);
            SnapshotRequire(SnapshotKey(key), "object key");
            return key;
        }
        return new Snapshot
        {
            Version = SnapshotVersion, Kind = Kind, PoolSlot = PoolSlot, Active = Active,
            Serial = Serial, GoalDepth = GoalDepth, Goals = (byte[])_goals.Clone(),
            PositionX = Position.X, PositionZ = Position.Z, TargetKey = Key(Target), Stamp = Stamp,
            Route = _route?.CaptureState(), RouteEpoch = _routeEpoch, Flags = Flags, Mode = Mode,
            State = State, LogicalRequest = LogicalRequest, FacingQuarterTurns = FacingQuarterTurns,
            CandidateKey = Key(Candidate), HireDay = HireDay, PatrolX0 = PatrolX0, PatrolZ0 = PatrolZ0,
            PatrolX1 = PatrolX1, PatrolZ1 = PatrolZ1, Level = _level, SpeedBits = _speedBits,
            CandidateSlot = CandidateSlot, MotivationCopy = MotivationCopy, Tiredness = Tiredness,
            Morale = Morale, WaypointsRetired = WaypointsRetired, BadStates = BadStates,
            Handyman = (this as Handyman)?.CaptureJobState(),
            Mechanic = (this as Mechanic)?.CaptureJobState(),
            Researcher = (this as Researcher)?.CaptureJobState()
        };
    }

    /// <summary>Read-only preflight against THIS staged member's identity and shared Park.Routes.
    /// Resolver must return already-staged objects (not construct/activate gameplay objects).
    /// No target type restriction: stale and deferred registry objects are legal. The enclosing
    /// graph validates list membership, candidate availability and disjoint guest/staff chains.</summary>
    public void ValidateSnapshot(Snapshot s, Func<string, object> resolve)
        => PrepareSnapshot(s, resolve);

    (object? Target, StaffCandidate? Candidate) PrepareSnapshot(Snapshot s, Func<string, object> resolve)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(resolve);
        SnapshotRequire(s.Version == SnapshotVersion && s.Kind == Kind && s.PoolSlot == PoolSlot
            && Enum.IsDefined(s.Kind) && (uint)s.PoolSlot < StaffTables.PoolSize, "schema/slot identity");
        SnapshotRequire(s.Goals != null && s.Goals.Length == 4 && (uint)s.GoalDepth <= 4, "goals");
        SnapshotRequire((uint)s.Level <= 7 && (uint)s.SpeedBits <= 31
            && (uint)s.CandidateSlot < StaffTables.PoolSize && (uint)s.FacingQuarterTurns <= 3
            && (uint)s.LogicalRequest <= 31, "packed fields");
        // Research uses signed-byte wrap with no lower tiredness clamp. Do not normalize it.
        SnapshotRequire(s.MotivationCopy is >= 0 and <= 100 && s.Morale is >= 0 and <= 100
            && s.Tiredness <= 100 && s.WaypointsRetired >= 0 && s.BadStates >= 0, "stats/counters");
        SnapshotRequire((s.Handyman != null) == (this is Handyman)
            && (s.Mechanic != null) == (this is Mechanic)
            && (s.Researcher != null) == (this is Researcher), "job payload");
        if (s.Handyman != null) Handyman.ValidateJobSnapshot(s.Handyman);
        if (s.Mechanic != null) Mechanic.ValidateJobSnapshot(s.Mechanic);
        if (s.Researcher != null) Researcher.ValidateJobSnapshot(s.Researcher);
        if (s.Route != null)
        {
            NativeGuestRoute.ValidateState(s.Route, Park.Routes);
            SnapshotRequire(s.RouteEpoch == s.Route.Generation, "cursor epoch");
        }
        object? Resolve(string? key)
        {
            if (key == null) return null;
            SnapshotRequire(SnapshotKey(key), "reference key");
            var value = resolve(key);
            SnapshotRequire(value != null, "unresolved reference: " + key);
            return value;
        }
        var target = Resolve(s.TargetKey);
        var candidate = Resolve(s.CandidateKey);
        SnapshotRequire(candidate == null || candidate is StaffCandidate c
            && c.Kind == Kind && c.Slot == s.CandidateSlot, "candidate identity/type");
        return (target, (StaffCandidate?)candidate);
    }

    /// <summary>Restore into an EXISTING newly staged pool shell. Does NOT manage ParkStaff's
    /// lists or other owners' reverse links. Complete validation/resolution/cursor construction
    /// precedes every write. Never frees the previous cursor; do not use on a published world.
    /// FromState binds the shared pool without allocating or adopting output slots.</summary>
    public void RestoreState(Snapshot s, Func<string, object> resolve)
    {
        var refs = PrepareSnapshot(s, resolve);
        var route = s.Route == null ? null : NativeGuestRoute.FromState(s.Route, Park.Routes);
        var goals = (byte[])s.Goals.Clone();
        Active = s.Active; Serial = s.Serial; GoalDepth = s.GoalDepth; goals.CopyTo(_goals, 0);
        Position = new(s.PositionX, s.PositionZ); Target = refs.Target!; Stamp = s.Stamp;
        _route = route; _routeEpoch = s.RouteEpoch; Flags = s.Flags; Mode = s.Mode; State = s.State;
        LogicalRequest = s.LogicalRequest; FacingQuarterTurns = s.FacingQuarterTurns;
        Candidate = refs.Candidate!; HireDay = s.HireDay;
        PatrolX0 = s.PatrolX0; PatrolZ0 = s.PatrolZ0; PatrolX1 = s.PatrolX1; PatrolZ1 = s.PatrolZ1;
        _level = s.Level; _speedBits = s.SpeedBits; CandidateSlot = s.CandidateSlot;
        MotivationCopy = s.MotivationCopy; Tiredness = s.Tiredness; Morale = s.Morale;
        WaypointsRetired = s.WaypointsRetired; BadStates = s.BadStates;
        if (this is Mechanic mechanic) mechanic.RestoreJobState(s.Mechanic!);
        if (this is Researcher researcher) researcher.RestoreJobState(s.Researcher!);
    }
}
