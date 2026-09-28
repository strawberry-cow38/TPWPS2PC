using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

public sealed partial class GuestWalk
{
    public const int StateVersion = 1;
    public const int StateGuestLimit = 100_000, StateRouteCellLimit = 1_000_000;

    /// <summary>Trusted identity lookups ONLY, never IO, gameplay, Step, pathfinding or callbacks
    /// invoked during binding. Null references have null IDs; non-null IDs must resolve or throw.
    /// Within each category IDs and object identities must be one-to-one. Native inputs bind the
    /// entire delegate bundle (including SlotAdvanced); flags are checked against the saved bundle.
    /// Bind to fresh staged external owners, not the source graph. Closures may capture staged
    /// guest maps/controller shells to resolve cycles. Caller bounds JSON before deserialization,
    /// freezes all owners during capture and publishes only after every owner has hydrated.
    /// BeforeStep/QueueStep/Paused subscriptions are identities too; nothing serializes functions.</summary>
    public sealed class StateBindings
    {
        public Func<GuestTerminal, string> IdentifyTerminal { get; init; }
        public Func<string, GuestTerminal> ResolveTerminal { get; init; }
        public Func<object, string> IdentifyNativeOwner { get; init; }
        public Func<string, object> ResolveNativeOwner { get; init; }
        public Func<NativeMotionInputs, string> IdentifyNativeInputs { get; init; }
        public Func<string, NativeMotionInputs> ResolveNativeInputs { get; init; }
        public Func<Action<uint>, string> IdentifyBeforeStep { get; init; }
        public Func<string, Action<uint>> ResolveBeforeStep { get; init; }
        public Func<Func<ParkCell, ParkCell, bool>, string> IdentifyQueueStep { get; init; }
        public Func<string, Func<ParkCell, ParkCell, bool>> ResolveQueueStep { get; init; }
        public Func<Func<Guest, bool>, string> IdentifyPaused { get; init; }
        public Func<string, Func<Guest, bool>> ResolvePaused { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CellState
    {
        public required int X { get; init; }
        public required int Z { get; init; }
        internal ParkCell Cell => new(X, Z);
        internal static CellState Of(ParkCell c) => new() { X = c.X, Z = c.Z };
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record LeaseState
    {
        public required string OwnerId { get; init; }
        public required string InputsId { get; init; }
        public required bool AutomaticStep { get; init; }
        public required bool HasSlotAdvanced { get; init; }
        public required NativeGuestRoute.State Cursor { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GuestStateData
    {
        public required int GraphId { get; init; }
        public required int Id { get; init; }
        public required CellState Cell { get; init; }
        public required CellState Next { get; init; }
        public required int Progress { get; init; }
        public required CellState Destination { get; init; }
        public required GuestState State { get; init; }
        public required CellState[] Route { get; init; }
        public required int RouteIndex { get; init; }
        public required int Steps { get; init; }
        public required int Reroutes { get; init; }
        public required string Reason { get; init; }
        public required string TargetTerminalId { get; init; }
        public required string OccupiedTerminalId { get; init; }
        public required LeaseState NativeLease { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required string GridKey { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required long Time { get; init; }
        public required long Carry { get; init; }
        public required int LastId { get; init; }
        public required GuestStateData[] Guests { get; init; }
        public required int[] OrderedLiveGuests { get; init; }
        public required NativeRoutePool.State SharedNativeRoutePool { get; init; }
        public required string BeforeStepId { get; init; }
        public required string QueueStepId { get; init; }
        public required string PausedId { get; init; }
    }

    /// <summary>Snapshot-local reference identity, explicitly NOT Guest.Id. ParkVisitors/staff
    /// must save GuestGraphId and resolve GuestByGraphId, including inactive guest objects.
    /// AllocateState exposes shells for external cycles; do not tick/publish Walk until Hydrate
    /// and all other staged owners succeed. Snapshot is detached from owners; Hydrate uses a
    /// private copy, so editing the public DTO cannot change the staged graph.</summary>
    public sealed class GuestGraph
    {
        readonly Dictionary<Guest, int> ids = new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<int, Guest> guests;
        readonly State pending;
        public GuestWalk Walk { get; }
        public State Snapshot { get; }
        public IReadOnlyDictionary<int, Guest> GuestsByGraphId { get; }
        public bool IsHydrated { get; private set; }
        internal GuestGraph(GuestWalk walk, State snapshot, Dictionary<int, Guest> guests, bool hydrated)
        {
            Walk = walk; Snapshot = CopyState(snapshot); pending = CopyState(snapshot);
            this.guests = guests; GuestsByGraphId = new ReadOnlyDictionary<int, Guest>(guests);
            foreach (var p in guests) ids.Add(p.Value, p.Key);
            IsHydrated = hydrated;
        }
        public int GuestGraphId(Guest guest) => guest != null && ids.TryGetValue(guest, out int id)
            ? id : throw new ArgumentException("Guest is not in the captured/staged walking graph.");
        public Guest GuestByGraphId(int id) => guests.TryGetValue(id, out var guest)
            ? guest : throw new ArgumentException("Unknown guest graph ID.");

        /// <summary>Resolve everything before writing any external bindings. On error shells
        /// remain unhydrated; discard them (no source/live owner or pool was changed).</summary>
        public void Hydrate(StateBindings bindings)
        {
            if (IsHydrated) throw new InvalidOperationException("Walking graph already hydrated.");
            bindings ??= new();
            var terminals = new Resolver<GuestTerminal>(bindings.ResolveTerminal);
            var owners = new Resolver<object>(bindings.ResolveNativeOwner);
            var inputs = new Resolver<NativeMotionInputs>(bindings.ResolveNativeInputs);
            var targets = new GuestTerminal[pending.Guests.Length];
            var occupied = new GuestTerminal[pending.Guests.Length];
            var leases = new NativeWalkLease[pending.Guests.Length];
            for (int i = 0; i < pending.Guests.Length; i++)
            {
                var s = pending.Guests[i];
                targets[i] = terminals.Get(s.TargetTerminalId);
                occupied[i] = terminals.Get(s.OccupiedTerminalId);
                if (s.NativeLease is not { } l) continue;
                var owner = owners.Get(l.OwnerId);
                var input = inputs.Get(l.InputsId);
                StateRequire(input.Speed != null && input.Delta != null && input.AnimationReady != null
                    && input.AutomaticStep == l.AutomaticStep && (input.SlotAdvanced != null) == l.HasSlotAdvanced,
                    "native input binding/flags");
                leases[i] = new(owner, NativeGuestRoute.FromState(l.Cursor, Walk.NativeRoutes), input);
            }
            var before = new Resolver<Action<uint>>(bindings.ResolveBeforeStep).Get(pending.BeforeStepId);
            var queue = new Resolver<Func<ParkCell, ParkCell, bool>>(bindings.ResolveQueueStep).Get(pending.QueueStepId);
            var paused = new Resolver<Func<Guest, bool>>(bindings.ResolvePaused).Get(pending.PausedId);
            for (int i = 0; i < pending.Guests.Length; i++)
            {
                var guest = guests[pending.Guests[i].GraphId];
                guest.TargetTerminal = targets[i]; guest.OccupiedTerminal = occupied[i]; guest.NativeMotion = leases[i];
            }
            Walk.BeforeStep = before; Walk.QueueStep = queue; Walk.Paused = paused;
            IsHydrated = true;
        }
    }

    sealed class Identifier<T> where T : class
    {
        readonly Func<T, string> identify;
        readonly Dictionary<T, string> ids = new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<string, T> objects = new(StringComparer.Ordinal);
        public Identifier(Func<T, string> identify) => this.identify = identify;
        public string Get(T value)
        {
            if (value == null) return null;
            if (ids.TryGetValue(value, out var id)) return id;
            id = identify?.Invoke(value);
            StateRequire(ValidId(id), "missing/invalid external identity");
            StateRequire(!objects.ContainsKey(id), "external ID aliases distinct objects");
            ids.Add(value, id); objects.Add(id, value); return id;
        }
    }
    sealed class Resolver<T> where T : class
    {
        readonly Func<string, T> resolve;
        readonly Dictionary<string, T> values = new(StringComparer.Ordinal);
        readonly HashSet<T> identities = new(ReferenceEqualityComparer.Instance);
        public Resolver(Func<string, T> resolve) => this.resolve = resolve;
        public T Get(string id)
        {
            if (id == null) return null;
            if (values.TryGetValue(id, out var value)) return value;
            value = resolve?.Invoke(id);
            StateRequire(value != null && identities.Add(value), "unresolved/aliased external ID: " + id);
            values.Add(id, value); return value;
        }
    }

    public State CaptureState(string gridKey, StateBindings bindings = null, IEnumerable<Guest> extraInactiveGuests = null)
        => CaptureGraph(gridKey, bindings, extraInactiveGuests).Snapshot;

    public GuestGraph CaptureGraph(string gridKey, StateBindings bindings = null, IEnumerable<Guest> extraInactiveGuests = null)
    {
        bindings ??= new();
        var map = new Dictionary<int, Guest>();
        var ids = new Dictionary<Guest, int>(ReferenceEqualityComparer.Instance);
        void Include(Guest guest)
        {
            StateRequire(guest != null, "null extra guest");
            if (ids.ContainsKey(guest)) return;
            StateRequire(map.Count < StateGuestLimit, "guest count");
            int id = map.Count + 1; map.Add(id, guest); ids.Add(guest, id);
        }
        foreach (var g in _guests) Include(g);
        if (extraInactiveGuests != null)
        {
            int count = 0;
            foreach (var g in extraInactiveGuests)
            { StateRequire(++count <= StateGuestLimit, "extra guest enumeration bound"); Include(g); }
        }
        var terminals = new Identifier<GuestTerminal>(bindings.IdentifyTerminal);
        var owners = new Identifier<object>(bindings.IdentifyNativeOwner);
        var inputs = new Identifier<NativeMotionInputs>(bindings.IdentifyNativeInputs);
        int routeCells = 0;
        var data = map.Select(p =>
        {
            var g = p.Value;
            StateRequire(g.Route == null || g.Route.Count <= StateRouteCellLimit - routeCells, "route cells bound");
            routeCells += g.Route?.Count ?? 0;
            LeaseState lease = null;
            if (g.NativeMotion is { } l)
            {
                StateRequire(ReferenceEquals(l.Route.Pool, NativeRoutes), "lease uses another pool");
                lease = new() { OwnerId = owners.Get(l.Owner), InputsId = inputs.Get(l.Inputs),
                    AutomaticStep = l.Inputs.AutomaticStep, HasSlotAdvanced = l.Inputs.SlotAdvanced != null,
                    Cursor = l.Route.CaptureState() };
            }
            return new GuestStateData { GraphId = p.Key, Id = g.Id, Cell = CellState.Of(g.Cell),
                Next = g.Next is { } next ? CellState.Of(next) : null, Progress = g.Progress,
                Destination = CellState.Of(g.Destination), State = g.State,
                Route = g.Route?.Select(CellState.Of).ToArray(), RouteIndex = g.RouteIndex,
                Steps = g.Steps, Reroutes = g.Reroutes, Reason = g.Reason,
                TargetTerminalId = terminals.Get(g.TargetTerminal), OccupiedTerminalId = terminals.Get(g.OccupiedTerminal),
                NativeLease = lease };
        }).ToArray();
        var state = new State { Version = StateVersion, GridKey = gridKey,
            Width = Paths.Field.Width, Height = Paths.Field.Height, Time = Time, Carry = _carry, LastId = _lastId,
            Guests = data, OrderedLiveGuests = _guests.Select(g => ids[g]).ToArray(),
            SharedNativeRoutePool = NativeRoutes.CaptureState(),
            BeforeStepId = new Identifier<Action<uint>>(bindings.IdentifyBeforeStep).Get(BeforeStep),
            QueueStepId = new Identifier<Func<ParkCell, ParkCell, bool>>(bindings.IdentifyQueueStep).Get(QueueStep),
            PausedId = new Identifier<Func<Guest, bool>>(bindings.IdentifyPaused).Get(Paused) };
        ValidateState(state, gridKey, Paths, NativeRoutes);
        return new GuestGraph(this, state, map, true);
    }

    /// <summary>Phase one: validate/copy DTOs, allocate fresh guest identities and a SINGLE pool.
    /// Paths must be the independently resolved fresh staged paths (e.g. ParkGroundSnapshot).
    /// No Spawn, AssignRoute, pool reallocation, route search or Step occurs.</summary>
    public static GuestGraph AllocateState(State state, string expectedGridKey, ParkPaths paths)
    {
        ArgumentNullException.ThrowIfNull(state); ArgumentNullException.ThrowIfNull(paths);
        var pool = NativeRoutePool.FromState(state.SharedNativeRoutePool);
        ValidateState(state, expectedGridKey, paths, pool);
        var copy = CopyState(state);
        var walk = new GuestWalk(paths, pool) { Time = copy.Time, _carry = copy.Carry, _lastId = copy.LastId };
        var map = new Dictionary<int, Guest>();
        foreach (var g in copy.Guests)
            map.Add(g.GraphId, new Guest { Id = g.Id, Cell = g.Cell.Cell, Next = g.Next?.Cell,
                Progress = g.Progress, Destination = g.Destination.Cell, State = g.State,
                Route = g.Route == null ? null : Array.AsReadOnly(g.Route.Select(c => c.Cell).ToArray()),
                RouteIndex = g.RouteIndex, Steps = g.Steps, Reroutes = g.Reroutes, Reason = g.Reason });
        foreach (int id in copy.OrderedLiveGuests) walk.AddLive(map[id]);
        return new GuestGraph(walk, copy, map, false);
    }

    public static GuestGraph FromState(State state, string expectedGridKey, ParkPaths paths, StateBindings bindings = null)
    {
        var graph = AllocateState(state, expectedGridKey, paths);
        graph.Hydrate(bindings); return graph;
    }

    static bool ValidId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 1024;
    static void StateRequire(bool ok, string why)
    { if (!ok) throw new ArgumentException("Invalid GuestWalk state: " + why); }

    static void ValidateState(State s, string expectedKey, ParkPaths paths, NativeRoutePool pool)
    {
        StateRequire(s.Version == StateVersion && ValidId(expectedKey) && s.GridKey == expectedKey, "version/grid key");
        StateRequire(s.Width == paths.Field.Width && s.Height == paths.Field.Height
            && s.Width > 0 && s.Height > 0 && (long)s.Width * s.Height <= ParkPaths.MaxStateCells, "grid dimensions");
        StateRequire(s.Time >= 0 && s.Time % TickMilliseconds == 0, "time");
        // Advance accepts signed deltas: preserve signed carry, rather than silently normalizing it.
        StateRequire(s.Guests != null && s.Guests.Length <= StateGuestLimit && s.OrderedLiveGuests != null
            && s.OrderedLiveGuests.Length <= s.Guests.Length, "graph bounds");
        void Ref(string id) => StateRequire(id == null || ValidId(id), "external ID");
        Ref(s.BeforeStepId); Ref(s.QueueStepId); Ref(s.PausedId);
        void Cell(CellState c, bool required)
        {
            StateRequire(!required || c != null, "required cell");
            // Coarse native targets are signed bytes and may intentionally be outside the grid.
            if (c != null) StateRequire(c.X >= short.MinValue && c.X <= Math.Max(short.MaxValue, s.Width)
                && c.Z >= short.MinValue && c.Z <= Math.Max(short.MaxValue, s.Height), "coordinate bounds");
        }
        var ids = new HashSet<int>(); var slots = new HashSet<int>(); int cells = 0;
        var liveIds = s.OrderedLiveGuests.ToHashSet();
        foreach (var g in s.Guests)
        {
            StateRequire(g != null && g.GraphId > 0 && g.GraphId <= s.Guests.Length && ids.Add(g.GraphId), "guest graph ID");
            Cell(g.Cell, true); Cell(g.Next, false); Cell(g.Destination, true);
            StateRequire(Enum.IsDefined(g.State) && g.Progress >= 0 && g.Progress < UnitsPerCell
                && (g.Next != null || g.Progress == 0) && g.Steps >= 0 && g.Reroutes >= 0
                && (g.Reason == null || g.Reason.Length <= 16384), "guest fields");
            Ref(g.TargetTerminalId); Ref(g.OccupiedTerminalId);
            if (g.Route != null)
            {
                StateRequire(g.Route.Length > 0 && g.Route.Length <= StateRouteCellLimit - cells, "route bound");
                cells += g.Route.Length;
                foreach (var c in g.Route) { Cell(c, true); StateRequire(paths.Contains(c.Cell), "legacy route cell"); }
                StateRequire(g.RouteIndex >= 0 && g.RouteIndex < g.Route.Length, "legacy route index");
            }
            // Remove/Clear dispose a native lease without rewriting the detached body's
            // coarse slot. Queue boarding history retains these exact inactive objects.
            else StateRequire(g.NativeLease != null || g.RouteIndex == 0
                || !liveIds.Contains(g.GraphId) && g.RouteIndex >= -1 && g.RouteIndex < NativeRoutePool.Capacity,
                "absent legacy route index");
            if (g.NativeLease is not { } l) continue;
            StateRequire(ValidId(l.OwnerId) && ValidId(l.InputsId) && g.Route == null && g.Progress == 0, "lease refs/legacy conflict");
            NativeGuestRoute.ValidateState(l.Cursor, pool);
            StateRequire(g.RouteIndex == l.Cursor.SlotIndex, "coarse native slot");
            for (int slot = l.Cursor.SlotIndex; slot != -1; slot = pool.Next(slot))
                StateRequire(slots.Add(slot), "overlapping cursor ownership");
        }
        var live = new HashSet<int>();
        foreach (int id in s.OrderedLiveGuests) StateRequire(ids.Contains(id) && live.Add(id), "live guest ID");
    }

    static State CopyState(State s) => s with
    {
        OrderedLiveGuests = (int[])s.OrderedLiveGuests.Clone(),
        SharedNativeRoutePool = s.SharedNativeRoutePool with { Words = (uint[])s.SharedNativeRoutePool.Words.Clone() },
        // Records below contain immutable scalar/record fields; only arrays need further detachment.
        Guests = s.Guests.Select(g => g with { Route = g.Route == null ? null : (CellState[])g.Route.Clone() }).ToArray()
    };
}
