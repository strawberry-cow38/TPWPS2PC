using System.Text.Json.Serialization;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

public sealed partial class NativeRideQueues
{
    public const int StateVersion = 1, StateMemberLimit = 100_000;
    bool _stateHydrated = true;
    GuestWalk.GuestGraph _stateGraph;

    /// <summary>Trusted reference lookups, not gameplay. ServicesId identifies the complete
    /// explicitly rebound bundle. Its VALUES (including queue shape/rotation/cell order, RNG,
    /// speed, phase, readiness, broken/tier and callback targets) belong to external providers:
    /// freeze and snapshot those providers independently. No service is sampled by this owner.
    /// Ride IDs must be one-to-one by reference, including demolished rides with reused IDs.
    /// IdentifyInputs must agree with GuestWalk's bindings, including vanished members.</summary>
    public sealed class StateBindings
    {
        public required GuestWalk.GuestGraph GuestGraph { get; init; }
        public required string OwnerId { get; init; }
        public required string ServicesId { get; init; }
        public required Services Services { get; init; }
        public Func<ParkRide, string> IdentifyRide { get; init; }
        public Func<string, ParkRide> ResolveRide { get; init; }
        public Func<NativeMotionInputs, string> IdentifyInputs { get; init; }
    }

    public object StateOwner => _owner;
    public bool StateBindingsHydrated => _stateHydrated;
    /// <summary>Union with other inventories before GuestWalk.CaptureGraph. Boarding history
    /// retains inactive guest bodies; numeric Guest.Id is not their identity.</summary>
    public IReadOnlyList<Guest> ReferencedGuests => _members.Select(m => m.Guest)
        .Concat(_boardings.Select(b => b.Guest)).Distinct<Guest>(ReferenceEqualityComparer.Instance).ToArray();
    public IReadOnlyList<ParkRide> ReferencedRides => _queues.Keys.Concat(_members.Select(m => m.Ride))
        .Distinct<ParkRide>(ReferenceEqualityComparer.Instance).ToArray();
    public NativeMotionInputs StateInputs(Guest guest) => _members.FirstOrDefault(m => ReferenceEquals(m.Guest, guest))?.Inputs
        ?? throw new ArgumentException("Guest is not a queue member.");
    readonly Dictionary<string, NativeMotionInputs> _stateInputs = new(StringComparer.Ordinal);
    /// <summary>For GuestGraph.Hydrate after AllocateState; returns the SAME member bundle. Valid only until HydrateStateBindings completes.</summary>
    public NativeMotionInputs StateInputs(string id) => _stateInputs.TryGetValue(id, out var input)
        ? input : throw new ArgumentException("Unknown queue input identity.");

    // Also used by Arrive: identical closures/flags, without route assignment or pool allocation.
    NativeMotionInputs CreateStateInputs(Guest guest) => new(() => _services.Speed(guest), () => 0x4000,
        () => _services.AnimationReady(guest)) { SlotAdvanced = () => _services.SlotAdvanced?.Invoke(guest) };

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record MemberState
    {
        public required int GuestGraphId { get; init; }
        public required string RideId { get; init; }
        public required string InputsId { get; init; }
        public required Step Step { get; init; }
        public required uint Deadline { get; init; }
        public required bool Routed { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record QueueState
    {
        public required string RideId { get; init; }
        public required int[] Members { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PointState
    {
        public required short X { get; init; }
        public required short Z { get; init; }
        internal Point Point => new(X, Z);
        internal static PointState Of(Point p) => new() { X = p.X, Z = p.Z };
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record BoardingState
    {
        public required int GuestGraphId { get; init; }
        public required Step Step { get; init; }
        public required PointState Position { get; init; }
        public required PointState Spot { get; init; }
        public required int OnRide { get; init; }
        public required int Capacity { get; init; }
        public required int LetMeOn { get; init; }
        public required int ScriptQueue { get; init; }
        public required uint Tick { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required string OwnerId { get; init; }
        public required string ServicesId { get; init; }
        public required bool HasSlotAdvanced { get; init; }
        public required MemberState[] Members { get; init; }
        public required QueueState[] Queues { get; init; }
        public required IntMapLayout QueueLayout { get; init; }
        public required BoardingState[] Boardings { get; init; }
        public required uint Now { get; init; }
        public required int Joined { get; init; }
        public required int Boarded { get; init; }
        public required int Quits { get; init; }
        public required int Impatient { get; init; }
        public required int Refused { get; init; }
        public required int Released { get; init; }
    }

    /// <summary>Quiescent boundary only, never inside Tick/Arrive/Clear or a service callback.
    /// Freeze all owners together. Supply a graph captured at that same boundary, including
    /// ReferencedGuests. Caller must bound JSON bytes/depth BEFORE deserializing.</summary>
    public State CaptureState(StateBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (!_stateHydrated) throw new InvalidOperationException("Queue shell is not hydrated.");
        Require(ReferenceEquals(_services, bindings.Services), "service identity");
        var ids = new Dictionary<ParkRide, string>(ReferenceEqualityComparer.Instance);
        var used = new HashSet<string>(StringComparer.Ordinal);
        string Ride(ParkRide r)
        {
            if (ids.TryGetValue(r, out var id)) return id;
            id = bindings.IdentifyRide?.Invoke(r);
            Require(Id(id) && used.Add(id), "missing/aliased ride identity");
            ids.Add(r, id); return id;
        }
        var s = new State { Version = StateVersion, OwnerId = bindings.OwnerId, ServicesId = bindings.ServicesId,
            HasSlotAdvanced = _services.SlotAdvanced != null, Now = _now, QueueLayout = _queues.CaptureLayout(),
            Joined = Joined, Boarded = Boarded, Quits = Quits, Impatient = Impatient, Refused = Refused, Released = Released,
            Members = _members.Select(m => new MemberState { GuestGraphId = bindings.GuestGraph.GuestGraphId(m.Guest),
                RideId = Ride(m.Ride), InputsId = bindings.IdentifyInputs?.Invoke(m.Inputs),
                Step = m.Step, Deadline = m.Deadline, Routed = m.Routed }).ToArray(),
            Queues = _queues.Select(p => new QueueState { RideId = Ride(p.Key),
                Members = p.Value.Select(m => bindings.GuestGraph.GuestGraphId(m.Guest)).ToArray() }).ToArray(),
            Boardings = _boardings.Select(b => new BoardingState { GuestGraphId = bindings.GuestGraph.GuestGraphId(b.Guest),
                Step = b.Step, Position = PointState.Of(b.Position), Spot = b.Spot is { } p ? PointState.Of(p) : null,
                OnRide = b.OnRide, Capacity = b.Capacity, LetMeOn = b.LetMeOn, ScriptQueue = b.ScriptQueue, Tick = b.Tick }).ToArray() };
        ValidateState(s, _visitors, bindings);
        CheckLeases(bindings.GuestGraph);
        return s;
    }

    static bool Id(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 1024;
    static void Require(bool ok, string why)
    { if (!ok) throw new ArgumentException("Invalid NativeRideQueues state: " + why); }

    /// <summary>Pure bounded structural/cross-graph checks, before resolving rides or allocating
    /// controller shells. External identity lookups are never gameplay/service callbacks.</summary>
    public static void ValidateState(State s, ParkVisitors visitors, StateBindings b)
    {
        ArgumentNullException.ThrowIfNull(s); ArgumentNullException.ThrowIfNull(visitors); ArgumentNullException.ThrowIfNull(b);
        Require(b.GuestGraph != null && ReferenceEquals(visitors.Walk, b.GuestGraph.Walk), "walking graph identity");
        Require(s.Version == StateVersion && Id(s.OwnerId) && s.OwnerId == b.OwnerId
            && Id(s.ServicesId) && s.ServicesId == b.ServicesId, "version/binding IDs");
        var v = b.Services;
        Require(v != null && v.Shape != null && v.Random != null && v.Phase != null && v.Speed != null
            && v.AnimationReady != null && v.Broken != null && v.Tier != null
            && (v.SlotAdvanced != null) == s.HasSlotAdvanced, "service bundle");
        Require(s.Members != null && s.Members.Length <= StateMemberLimit && s.Queues != null
            && s.Queues.Length <= StateMemberLimit && s.Boardings != null && s.Boardings.Length <= 64, "bounds");
        Require(s.Joined >= 0 && s.Boarded >= 0 && s.Quits >= 0 && s.Impatient >= 0
            && s.Refused >= 0 && s.Released >= 0, "counters");
        var guests = b.GuestGraph.Snapshot.Guests.ToDictionary(g => g.GraphId);
        var members = new Dictionary<int, MemberState>(); var inputs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in s.Members)
        {
            Require(m != null && guests.ContainsKey(m.GuestGraphId) && members.TryAdd(m.GuestGraphId, m)
                && Id(m.RideId) && Id(m.InputsId) && inputs.Add(m.InputsId) && Enum.IsDefined(m.Step), "member fields/identity");
            Require(m.Step != Step.Waiting || !m.Routed, "waiting route latch");
            // A vanished guest can await Forget with no lease. Never manufacture one.
            if (guests[m.GuestGraphId].NativeLease is { } l)
                Require(l.OwnerId == s.OwnerId && l.InputsId == m.InputsId && l.AutomaticStep && l.HasSlotAdvanced, "member lease binding");
        }
        // Validate allocation history before external ride resolution or shell creation.
        var mapProbe = new SnapshotReferenceMap<ParkRide, int>();
        foreach(var row in s.Queues) mapProbe.Add(new ParkRide(), 0);
        mapProbe.RestoreLayout(s.QueueLayout,()=>new ParkRide());
        var rides = new HashSet<string>(StringComparer.Ordinal); var linked = new HashSet<int>();
        int total = 0;
        foreach (var q in s.Queues)
        {
            Require(q != null && Id(q.RideId) && rides.Add(q.RideId) && q.Members != null
                && q.Members.Length <= s.Members.Length - total, "queue bounds/identity");
            total += q.Members.Length;
            foreach (int id in q.Members)
                Require(members.TryGetValue(id, out var m) && m.RideId == q.RideId
                    && m.Step is Step.WalkIn or Step.Waiting or Step.MoveUp && linked.Add(id), "linked membership");
        }
        foreach (var m in s.Members)
            Require(linked.Contains(m.GuestGraphId) == (m.Step is Step.WalkIn or Step.Waiting or Step.MoveUp), "membership completeness");
        foreach (var g in guests.Values)
            if (g.NativeLease?.OwnerId == s.OwnerId)
                Require(members.ContainsKey(g.GraphId), "orphan owned lease");
        foreach (var h in s.Boardings)
            Require(h != null && guests.ContainsKey(h.GuestGraphId) && h.Step == Step.Waiting
                && h.Position != null && h.ScriptQueue == 0 && h.LetMeOn == 0 && h.OnRide < h.Capacity, "boarding history");
    }

    /// <summary>Allocate after GuestWalk and ParkVisitors shells. Rebuild ONLY membership and
    /// closures; never Join, assign routes, replay gameplay, or create/allocate a native pool.
    /// Then hydrate GuestGraph using StateOwner/StateInputs, hydrate visitors' callback seams,
    /// and finally HydrateStateBindings. Publish only after all staged owners succeed.</summary>
    public static NativeRideQueues AllocateState(State s, ParkVisitors visitors, StateBindings bindings)
    {
        ValidateState(s, visitors, bindings);
        Require(!bindings.GuestGraph.IsHydrated, "restore requires staged unhydrated walking graph");
        var rides = new Dictionary<string, ParkRide>(StringComparer.Ordinal);
        var identities = new HashSet<ParkRide>(ReferenceEqualityComparer.Instance);
        foreach (string id in s.Members.Select(m => m.RideId).Concat(s.Queues.Select(q => q.RideId)).Distinct(StringComparer.Ordinal))
        {
            var ride = bindings.ResolveRide?.Invoke(id);
            Require(ride != null && identities.Add(ride), "unresolved/aliased ride ID");
            rides.Add(id, ride);
        }
        var result = new NativeRideQueues(visitors, bindings.Services) { _stateHydrated = false, _stateGraph = bindings.GuestGraph,
            _now = s.Now, Joined = s.Joined, Boarded = s.Boarded, Quits = s.Quits,
            Impatient = s.Impatient, Refused = s.Refused, Released = s.Released };
        var members = new Dictionary<int, Member>();
        foreach (var row in s.Members)
        {
            var guest = bindings.GuestGraph.GuestByGraphId(row.GuestGraphId);
            var input = result.CreateStateInputs(guest);
            var m = new Member(guest, rides[row.RideId], input) { Step = row.Step, Deadline = row.Deadline, Routed = row.Routed };
            result._members.Add(m); members.Add(row.GuestGraphId, m); result._stateInputs.Add(row.InputsId, input);
        }
        foreach (var row in s.Queues) result._queues.Add(rides[row.RideId], row.Members.Select(id => members[id]).ToList());
        result._queues.RestoreLayout(s.QueueLayout,()=>new ParkRide());
        foreach (var h in s.Boardings) result._boardings.Enqueue(new(bindings.GuestGraph.GuestByGraphId(h.GuestGraphId),
            h.Step, h.Position.Point, h.Spot?.Point, h.OnRide, h.Capacity, h.LetMeOn, h.ScriptQueue, h.Tick));
        return result;
    }

    void CheckLeases(GuestWalk.GuestGraph graph)
    {
        Require(graph.IsHydrated, "walking graph not hydrated");
        foreach (var m in _members)
            if (m.Guest.NativeMotion is { } lease)
                Require(ReferenceEquals(lease.Owner, _owner) && ReferenceEquals(lease.Inputs, m.Inputs)
                    && ReferenceEquals(lease.Route.Pool, graph.Walk.NativeRoutes), "actual lease owner/inputs/shared pool");
    }

    void RequireHydratedState()
    { if (!_stateHydrated) throw new InvalidOperationException("Queue shell is not hydrated."); }

    public void HydrateStateBindings()
    {
        if (_stateHydrated) throw new InvalidOperationException("Queue already hydrated.");
        CheckLeases(_stateGraph);
        _stateGraph = null;
        _stateInputs.Clear(); // binding-only registry must not retain future departed guests.
        _stateHydrated = true;
    }
}
