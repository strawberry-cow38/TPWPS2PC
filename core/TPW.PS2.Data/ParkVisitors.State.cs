using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

public sealed partial class ParkVisitors
{
    public const int StateVersion = 1, StateGuestLimit = 100_000;
    readonly SnapshotRandom _ownedRandom;
    // The latest body of each visitor survives handing it to a ride. A readmission may
    // replace that body, exactly as GuestWalk.Readmit does; snapshot IDs never use Guest.Id.
    readonly SnapshotIntMap<Guest> _guestObjects = new();
    BindingState _pendingBindings;

    /// <summary>Trusted identity registry, not gameplay callbacks. IDs must be one-to-one
    /// by reference (including retired rides with reused display IDs). Resolve against fresh
    /// staged owners. GuestGraph must include ReferencedGuests as extraInactiveGuests.
    /// Terminal bindings retain their trusted CanEnter delegate; it is never serialized.
    /// External RNG providers require a key and independently restored shared provider state.</summary>
    public sealed class StateBindings
    {
        public required GuestWalk.GuestGraph GuestGraph { get; init; }
        public Func<object, string> IdentifyReference { get; init; }
        public Func<string, object> ResolveReference { get; init; }
    }

    public IReadOnlyList<Guest> ReferencedGuests => _guestObjects.Values.Concat(Walk.Guests)
        .Distinct<Guest>(ReferenceEqualityComparer.Instance).ToArray();
    public IReadOnlyList<ParkRide> ReferencedRides => _owners.Values.Concat(_returning.Values.Select(r => r.Ride))
        .Concat(_reliefVisits.Values.Select(r => r.Owner)).Concat(_destinationHistory.Values.SelectMany(h => h))
        .Where(r => r != null).Distinct<ParkRide>(ReferenceEqualityComparer.Instance).ToArray();
    public IReadOnlyList<GuestTerminal> ReferencedTerminals => _serviceTerminals.Values.Where(t => t != null)
        .Distinct<GuestTerminal>(ReferenceEqualityComparer.Instance).ToArray();

    /// <summary>Explicit callback targets for restoring VisitorNeeds and ParkSim bindings.
    /// HydrateStateBindings does not subscribe or use Needs/Staff setters: the owning
    /// snapshots restore the exact subscriptions/overrides, once all cycles are validated.</summary>
    public Action<int, int> StateForwardSound => ForwardSound;
    public Func<int, (int Plain, int Vomit)> StateNearbyLitter => NearbyLitter;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GuestObjectState
    {
        public required int Guest { get; init; }
        public required int GraphId { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PlanState
    {
        public required int Guest { get; init; }
        public required VisitorIntent Intent { get; init; }
        public required int RideId { get; init; }
        public required GuestWalk.CellState At { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReferenceState
    {
        public required int Guest { get; init; }
        public required string ReferenceId { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReturnState
    {
        public required int Guest { get; init; }
        public required GuestWalk.CellState[] Preferred { get; init; }
        public required bool CompletedRide { get; init; }
        public required string RideReferenceId { get; init; }
        public required uint? CompletedAt { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record HistoryState
    {
        public required int Guest { get; init; }
        public required string[] RideReferenceIds { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReliefState
    {
        public required int Guest { get; init; }
        public required string RideReferenceId { get; init; }
        public required ReliefServiceClock.State Clock { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record VomitState
    {
        public required int Guest { get; init; }
        public required uint Deadline { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record BindingState
    {
        public required string NeedsId { get; init; }
        public required string StaffId { get; init; }
        public required string NativeDepartureId { get; init; }
        public required string NativeQueueMouthId { get; init; }
        public required string NativeQueueArrivalId { get; init; }
        public required string GuestSoundId { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required GuestObjectState[] GuestObjects { get; init; }
        public required IntMapLayout GuestObjectsLayout { get; init; }
        public required PlanState[] Plans { get; init; }
        public required IntMapLayout PlansLayout { get; init; }
        public required ReferenceState[] Owners { get; init; }
        public required IntMapLayout OwnersLayout { get; init; }
        public required ReturnState[] Returning { get; init; }
        public required IntMapLayout ReturningLayout { get; init; }
        public required ReferenceState[] ServiceTerminals { get; init; }
        public required IntMapLayout ServiceTerminalsLayout { get; init; }
        public required ReliefState[] ReliefVisits { get; init; }
        public required IntMapLayout ReliefVisitsLayout { get; init; }
        public required HistoryState[] DestinationHistory { get; init; }
        public required IntMapLayout DestinationHistoryLayout { get; init; }
        public required VomitState[] Vomiting { get; init; }
        public required IntMapLayout VomitingLayout { get; init; }
        public required GuestDecisionSchedule.State Decisions { get; init; }
        public required SnapshotRandom.State OwnedRandom { get; init; }
        public required string SharedRandomId { get; init; }
        public required BindingState Bindings { get; init; }
        // DecisionTick is derived from Sim.Time; recorded to reject a mismatched sim.
        public required long SimTime { get; init; }
        public required int Rides { get; init; }
        public required int Boardings { get; init; }
        public required int WentHome { get; init; }
        public required int DiscardedEntranceGuests { get; init; }
        public required int Relieved { get; init; }
        public required int Purchases { get; init; }
        public required int Serviced { get; init; }
        public required int Ejected { get; init; }
        public required int LitterDropped { get; init; }
        public required int LitterBinned { get; init; }
        public required int Vomited { get; init; }
        public required int RideIntensity { get; init; }
        public required int RideHappiness { get; init; }
        public required float RideSickScale { get; init; }
        public required float RideBoredomScale { get; init; }
        public required double SecondsPerService { get; init; }
        public required double SinceService { get; init; }
        public required bool AutoService { get; init; }
    }

    sealed class ReferenceMap
    {
        readonly StateBindings bindings;
        readonly Dictionary<object, string> ids = new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<string, object> objects = new(StringComparer.Ordinal);
        public ReferenceMap(StateBindings bindings) => this.bindings = bindings;
        public string Id(object value)
        {
            if (value == null) return null;
            if (ids.TryGetValue(value, out var id)) return id;
            id = bindings.IdentifyReference?.Invoke(value);
            Require(ValidId(id) && !objects.ContainsKey(id), "missing/aliased external identity");
            ids.Add(value, id); objects.Add(id, value); return id;
        }
        public T Get<T>(string id, bool required = false) where T : class
        {
            if (id == null) { Require(!required, "required reference"); return null; }
            Require(ValidId(id), "reference key");
            if (!objects.TryGetValue(id, out var value))
            {
                value = bindings.ResolveReference?.Invoke(id);
                Require(value != null && !ids.ContainsKey(value), "unresolved/aliased reference " + id);
                ids.Add(value, id); objects.Add(id, value);
            }
            Require(value is T, "wrong reference type " + id); return (T)value;
        }
    }
    static bool ValidId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 1024;
    static void Require(bool condition, string reason)
    { if (!condition) throw new ArgumentException("Invalid ParkVisitors state: " + reason); }

    public State CaptureState(StateBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        Require(_pendingBindings == null && bindings.GuestGraph != null
            && ReferenceEquals(bindings.GuestGraph.Walk, Walk), "unhydrated/wrong walk graph");
        var refs = new ReferenceMap(bindings);
        var s = new State
        {
            Version = StateVersion,
            GuestObjectsLayout = _guestObjects.CaptureLayout(),
            GuestObjects = _guestObjects.Select(p => new GuestObjectState { Guest = p.Key,
                GraphId = bindings.GuestGraph.GuestGraphId(p.Value) }).ToArray(),
            PlansLayout = _plans.CaptureLayout(),
            Plans = _plans.Values.Select(p => new PlanState { Guest = p.Guest, Intent = p.Intent,
                RideId = p.RideId, At = GuestWalk.CellState.Of(p.At) }).ToArray(),
            OwnersLayout = _owners.CaptureLayout(),
            Owners = _owners.Select(p => new ReferenceState { Guest = p.Key, ReferenceId = refs.Id(p.Value) }).ToArray(),
            ReturningLayout = _returning.CaptureLayout(),
            Returning = _returning.Select(p => new ReturnState { Guest = p.Key, Preferred = p.Value.Preferred.Select(GuestWalk.CellState.Of).ToArray(),
                CompletedRide = p.Value.CompletedRide, RideReferenceId = refs.Id(p.Value.Ride), CompletedAt = p.Value.CompletedAt }).ToArray(),
            ServiceTerminalsLayout = _serviceTerminals.CaptureLayout(),
            ServiceTerminals = _serviceTerminals.Select(p => new ReferenceState { Guest = p.Key, ReferenceId = refs.Id(p.Value) }).ToArray(),
            ReliefVisitsLayout = _reliefVisits.CaptureLayout(),
            ReliefVisits = _reliefVisits.Select(p => new ReliefState { Guest = p.Key, RideReferenceId = refs.Id(p.Value.Owner), Clock = p.Value.Clock.CaptureState() }).ToArray(),
            DestinationHistoryLayout = _destinationHistory.CaptureLayout(),
            DestinationHistory = _destinationHistory.Select(p => new HistoryState { Guest = p.Key, RideReferenceIds = p.Value.Select(refs.Id).ToArray() }).ToArray(),
            VomitingLayout = _vomiting.CaptureLayout(),
            Vomiting = _vomiting.Select(p => new VomitState { Guest = p.Key, Deadline = p.Value }).ToArray(),
            Decisions = _decisions.CaptureState(), OwnedRandom = _ownedRandom?.CaptureState(),
            SharedRandomId = _ownedRandom == null ? refs.Id(_random) : null,
            Bindings = new BindingState { NeedsId = refs.Id(_needs), StaffId = refs.Id(_staff),
                NativeDepartureId = refs.Id(NativeDeparture), NativeQueueMouthId = refs.Id(NativeQueueMouth),
                NativeQueueArrivalId = refs.Id(NativeQueueArrival), GuestSoundId = refs.Id(GuestSound) },
            SimTime = Sim.Time,
            Rides = Rides,
            Boardings = Boardings,
            WentHome = WentHome,
            DiscardedEntranceGuests = DiscardedEntranceGuests,
            Relieved = Relieved,
            Purchases = Purchases,
            Serviced = Serviced,
            Ejected = Ejected,
            LitterDropped = LitterDropped,
            LitterBinned = LitterBinned,
            Vomited = Vomited,
            RideIntensity = RideIntensity,
            RideHappiness = RideHappiness,
            RideSickScale = RideSickScale,
            RideBoredomScale = RideBoredomScale,
            SecondsPerService = SecondsPerService,
            SinceService = _sinceService,
            AutoService = AutoService,
        };
        ValidateShape(s);
        return s;
    }

    static void ValidateShape(State s)
    {
        ArgumentNullException.ThrowIfNull(s);
        Require(s.Version == StateVersion && s.Bindings != null && s.Decisions != null, "schema");
        Require((s.OwnedRandom != null) != (s.SharedRandomId != null), "exactly one RNG provider");
        Require(s.SimTime >= 0 && s.SimTime % ParkSim.TickMilliseconds == 0, "sim clock");
        Require(double.IsFinite(s.SecondsPerService) && double.IsFinite(s.SinceService)
            && float.IsFinite(s.RideSickScale) && float.IsFinite(s.RideBoredomScale), "non-finite setting/clock");
        void Key(string id) => Require(id == null || ValidId(id), "external key");
        Key(s.SharedRandomId); Key(s.Bindings.NeedsId); Key(s.Bindings.StaffId);
        Key(s.Bindings.NativeDepartureId); Key(s.Bindings.NativeQueueMouthId);
        Key(s.Bindings.NativeQueueArrivalId); Key(s.Bindings.GuestSoundId);
        HashSet<int> Rows<T>(T[] rows, Func<T, int> key) where T : class
        {
            Require(rows != null && rows.Length <= StateGuestLimit, "table bound");
            var seen = new HashSet<int>();
            foreach (var r in rows) Require(r != null && seen.Add(key(r)), "null/duplicate row");
            return seen;
        }
        var plans = Rows(s.Plans, p => p.Guest);
        var bodies = Rows(s.GuestObjects, p => p.Guest);
        Require(plans.SetEquals(bodies), "every visitor must retain a body identity");
        var graphIds = new HashSet<int>();
        foreach (var g in s.GuestObjects) Require(g.GraphId > 0 && graphIds.Add(g.GraphId), "guest graph identity");
        foreach (var p in s.Plans) Require(Enum.IsDefined(p.Intent) && p.At != null, "plan fields");
        void Subset(HashSet<int> keys) => Require(keys.IsSubsetOf(plans), "orphan visitor state");
        Subset(Rows(s.Owners, p => p.Guest));
        Subset(Rows(s.Returning, p => p.Guest));
        Subset(Rows(s.ServiceTerminals, p => p.Guest));
        Subset(Rows(s.ReliefVisits, p => p.Guest));
        Subset(Rows(s.DestinationHistory, p => p.Guest));
        Subset(Rows(s.Vomiting, p => p.Guest));
        Subset(Rows(s.Decisions.Gates, p => p.Guest));
        foreach (var o in s.Owners) Require(ValidId(o.ReferenceId), "owner key");
        foreach (var t in s.ServiceTerminals) Key(t.ReferenceId); // null terminal is an explicit stored value
        foreach (var r in s.Returning)
            Require(ValidId(r.RideReferenceId) && r.Preferred != null && r.Preferred.Length is > 0 and <= 3
                && r.Preferred.All(c => c != null), "return fields");
        foreach (var h in s.DestinationHistory)
        {
            Require(h.RideReferenceIds != null && h.RideReferenceIds.Length == 4, "history length");
            foreach (var id in h.RideReferenceIds) Key(id);
        }
        foreach (var r in s.ReliefVisits) Require(ValidId(r.RideReferenceId) && r.Clock != null && !r.Clock.Completed, "relief fields");
        var owners = s.Owners.ToDictionary(x => x.Guest);
        var returning = s.Returning.Select(x => x.Guest).ToHashSet();
        var relief = s.ReliefVisits.ToDictionary(x => x.Guest);
        foreach (var p in s.Plans)
        {
            Require(returning.Contains(p.Guest) == (p.Intent == VisitorIntent.Recovering), "recovery intent");
            Require(relief.ContainsKey(p.Guest) == (p.Intent == VisitorIntent.Servicing), "relief intent");
            if (p.Intent is VisitorIntent.Heading or VisitorIntent.Queued or VisitorIntent.Queueing or VisitorIntent.Servicing)
                Require(owners.ContainsKey(p.Guest), "intent needs ride owner");
            if (p.Intent == VisitorIntent.Recovering) Require(!owners.ContainsKey(p.Guest), "return still owned");
            if (relief.TryGetValue(p.Guest, out var r)) Require(owners[p.Guest].ReferenceId == r.RideReferenceId, "relief owner mismatch");
        }
    }

    ParkVisitors(ParkSim sim, GuestWalk walk, SnapshotRandom ownedRandom, Func<int> random, GuestDecisionSchedule decisions)
    { Sim = sim; Walk = walk; _ownedRandom = ownedRandom; _random = random; _decisions = decisions; }

    /// <summary>Stage one. Validate and resolve all logical state before constructing the
    /// coordinator. No Spawn/Register/activation or gameplay calls, no RNG draw, no external
    /// writes. Rides/terminals must already have staged identities; walk may be unhydrated.
    /// Needs/staff/callback cycles are resolved in HydrateStateBindings. Do not publish or Step
    /// this shell until EVERY graph owner and callback binding has validated/hydrated.</summary>
    public static ParkVisitors AllocateState(State state, ParkSim sim, StateBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(sim); ArgumentNullException.ThrowIfNull(bindings);
        ValidateShape(state);
        Require(bindings.GuestGraph != null && state.SimTime == sim.Time, "walk graph/sim time");
        var graph = bindings.GuestGraph;
        var refs = new ReferenceMap(bindings);
        var rng = state.OwnedRandom == null ? null : SnapshotRandom.FromState(state.OwnedRandom);
        Func<int> random = rng == null ? refs.Get<Func<int>>(state.SharedRandomId, true) : rng.Next;
        var decisions = GuestDecisionSchedule.FromState(state.Decisions, random);
        var bodies = state.GuestObjects.Select(g => (g.Guest, Body: graph.GuestByGraphId(g.GraphId))).ToArray();
        foreach (var g in bodies) Require(g.Guest == g.Body.Id, "guest numeric/graph ID mismatch");
        var plans = state.Plans.Select(p => new Plan(p.Guest, p.Intent, p.RideId, p.At.Cell)).ToArray();
        var owners = state.Owners.Select(p => (p.Guest, Ride: refs.Get<ParkRide>(p.ReferenceId, true))).ToArray();
        var returning = state.Returning.Select(p => (p.Guest, Return: new ReturnToPark(p.Preferred.Select(c => c.Cell).ToArray(),
            p.CompletedRide, refs.Get<ParkRide>(p.RideReferenceId, true), p.CompletedAt))).ToArray();
        var terminals = state.ServiceTerminals.Select(p => (p.Guest, Terminal: refs.Get<GuestTerminal>(p.ReferenceId))).ToArray();
        var relief = state.ReliefVisits.Select(p => (p.Guest, Visit: new ReliefVisit(refs.Get<ParkRide>(p.RideReferenceId, true),
            ReliefServiceClock.FromState(p.Clock)))).ToArray();
        var history = state.DestinationHistory.Select(p => (p.Guest, Rides: p.RideReferenceIds.Select(id => refs.Get<ParkRide>(id)).ToArray())).ToArray();
        var vomit = state.Vomiting.ToArray();
        foreach (var o in owners)
            Require(plans.Single(p => p.Guest == o.Guest).RideId == o.Ride.Id, "owner/display ride mismatch");
        // Distinct guest objects with reused numeric IDs elsewhere in the graph remain distinct.
        // Only the exact visitor body is classified here; staff/other graph guests aren't ours.
        foreach (var g in bodies)
        {
            var intent = plans.Single(p => p.Guest == g.Guest).Intent;
            bool active = graph.Walk.Guests.Contains(g.Body);
            Require(active == (intent is not (VisitorIntent.Queued or VisitorIntent.Servicing or VisitorIntent.Recovering)), "active/inactive visitor ownership");
        }
        var result = new ParkVisitors(sim, graph.Walk, rng, random, decisions)
        {
            _pendingBindings = state.Bindings with { },
            Rides = state.Rides,
            Boardings = state.Boardings,
            WentHome = state.WentHome,
            DiscardedEntranceGuests = state.DiscardedEntranceGuests,
            Relieved = state.Relieved,
            Purchases = state.Purchases,
            Serviced = state.Serviced,
            Ejected = state.Ejected,
            LitterDropped = state.LitterDropped,
            LitterBinned = state.LitterBinned,
            Vomited = state.Vomited,
            RideIntensity = state.RideIntensity,
            RideHappiness = state.RideHappiness,
            RideSickScale = state.RideSickScale,
            RideBoredomScale = state.RideBoredomScale,
            SecondsPerService = state.SecondsPerService,
            _sinceService = state.SinceService,
            AutoService = state.AutoService,
        };
        foreach (var g in bodies) result._guestObjects.Add(g.Guest, g.Body);
        foreach (var p in plans) result._plans.Add(p.Guest, p);
        foreach (var p in owners) result._owners.Add(p.Guest, p.Ride);
        foreach (var p in returning) result._returning.Add(p.Guest, p.Return);
        foreach (var p in terminals) result._serviceTerminals.Add(p.Guest, p.Terminal);
        foreach (var p in relief) result._reliefVisits.Add(p.Guest, p.Visit);
        foreach (var p in history) result._destinationHistory.Add(p.Guest, p.Rides);
        foreach (var p in vomit) result._vomiting.Add(p.Guest, p.Deadline);
        result._guestObjects.RestoreLayout(state.GuestObjectsLayout);
        result._plans.RestoreLayout(state.PlansLayout);
        result._owners.RestoreLayout(state.OwnersLayout);
        result._returning.RestoreLayout(state.ReturningLayout);
        result._serviceTerminals.RestoreLayout(state.ServiceTerminalsLayout);
        result._reliefVisits.RestoreLayout(state.ReliefVisitsLayout);
        result._destinationHistory.RestoreLayout(state.DestinationHistoryLayout);
        result._vomiting.RestoreLayout(state.VomitingLayout);

        return result;
    }

    public bool StateBindingsHydrated => _pendingBindings == null;

    /// <summary>Stage two, after cyclic staff/needs/controller shells exist. Lookups only;
    /// callback invocation and setters that wire/replay events are deliberately forbidden.
    /// Failure leaves this shell unchanged. Needs.Sounded/NearbyLitter and Sim.BreakdownMessage
    /// belong to their own owner snapshots; bind StateForwardSound/StateNearbyLitter there.</summary>
    public void HydrateStateBindings(StateBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (_pendingBindings is not { } b) throw new InvalidOperationException("Visitor bindings already hydrated.");
        Require(ReferenceEquals(bindings.GuestGraph?.Walk, Walk), "binding graph mismatch");
        var refs = new ReferenceMap(bindings);
        var needs = refs.Get<VisitorNeeds>(b.NeedsId);
        var staff = refs.Get<ParkStaff>(b.StaffId);
        Require(staff == null || ReferenceEquals(staff.Visitors, this), "staff belongs to another visitor owner");
        var departure = refs.Get<Func<Guest, bool>>(b.NativeDepartureId);
        var mouth = refs.Get<Func<ParkRide, ParkCell?>>(b.NativeQueueMouthId);
        var arrival = refs.Get<Func<Guest, ParkRide, bool>>(b.NativeQueueArrivalId);
        var sound = refs.Get<Action<int, int, ParkCell>>(b.GuestSoundId);
        _needs = needs; _staff = staff;
        NativeDeparture = departure; NativeQueueMouth = mouth; NativeQueueArrival = arrival; GuestSound = sound;
        _pendingBindings = null;
    }

    public static ParkVisitors FromState(State state, ParkSim sim, StateBindings bindings)
    {
        var result = AllocateState(state, sim, bindings);
        result.HydrateStateBindings(bindings);
        return result;
    }
}
