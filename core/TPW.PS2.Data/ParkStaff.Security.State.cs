#nullable enable
using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

// Shared pure binding checks. Owner partials hydrate their own fields, with no reflection,
// backing-field strings, gameplay setters, pool allocation or RNG draw.
internal static class SecuritySnapshotAccess
{
    internal static void Require(bool value, string why)
    { if (!value) throw new ArgumentException("Invalid security snapshot: " + why); }
    internal static string? Key(object? value, Func<object, string> id)
    {
        if (value == null) return null;
        var key = id(value); Require(!string.IsNullOrWhiteSpace(key) && key.Length <= 1024, "key"); return key;
    }
    internal static T? Resolve<T>(string? key, Func<string, object> resolve) where T : class
    {
        if (key == null) return null;
        Require(!string.IsNullOrWhiteSpace(key) && key.Length <= 1024, "key");
        var value = resolve(key); Require(value is T, "missing/wrong binding " + key); return (T)value;
    }

}

public sealed partial class ParkEffectors
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record SlotSnapshot
    {
        public required int Slot { get; init; }
        public required int X { get; init; }
        public required int Z { get; init; }
        public required uint Radius2 { get; init; }
        public required int Flags { get; init; }
        public required bool Active { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot
    {
        public required int Version { get; init; }
        public required SlotSnapshot[] Slots { get; init; }
        public required int[] Free { get; init; }
        public required int[] Active { get; init; }
    }
    /// <summary>Stable shells exist at construction; usable for resolving cyclic target links before hydration.</summary>
    public StaffEffector SnapshotSlot(int slot)
    {
        if ((uint)slot >= Capacity) throw new ArgumentOutOfRangeException(nameof(slot));
        return _slots[slot];
    }
    public Snapshot CaptureState() => new()
    {
        Version = 1, Slots = _slots.Select(e => new SlotSnapshot { Slot = e.Slot, X = e.X,
            Z = e.Z, Radius2 = e.Radius2, Flags = e.Flags, Active = e.Active }).ToArray(),
        Free = _free.Select(e => e.Slot).ToArray(), Active = _active.Select(e => e.Slot).ToArray()
    };
    public static void ValidateSnapshot(Snapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        SecuritySnapshotAccess.Require(s.Version == 1 && s.Slots != null && s.Slots.Length == Capacity
            && s.Free != null && s.Active != null, "effector schema");
        var order = s.Free!.Concat(s.Active!).ToArray();
        SecuritySnapshotAccess.Require(order.Length == Capacity && order.Distinct().Count() == Capacity
            && order.All(i => (uint)i < Capacity), "effector partition");
        for (int i = 0; i < Capacity; i++)
            SecuritySnapshotAccess.Require(s.Slots![i] != null && s.Slots[i].Slot == i
                && s.Slots[i].Active == s.Active!.Contains(i), "effector slot/membership");
    }
    public void RestoreState(Snapshot s) { ValidateSnapshot(s); Hydrate(s); }
    internal void Hydrate(Snapshot s)
    {
        for (int i = 0; i < Capacity; i++)
        { var e = _slots[i]; var a = s.Slots[i]; e.X = a.X; e.Z = a.Z; e.Radius2 = a.Radius2; e.Flags = a.Flags; e.Active = a.Active; }
        _free.Clear(); _free.AddRange(s.Free.Select(i => _slots[i]));
        _active.Clear(); _active.AddRange(s.Active.Select(i => _slots[i]));
    }
}

public sealed partial class PrankStinks
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record EntrySnapshot
    {
        public required int CellX { get; init; }
        public required int CellZ { get; init; }
        public required int UnitsX { get; init; }
        public required int UnitsZ { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot
    {
        public required int Version { get; init; }
        public required EntrySnapshot[] Entries { get; init; }
        public required int Refused { get; init; }
        public required uint? OwnedRandState { get; init; }
        public required string? ExternalRandKey { get; init; }
        public required string? StartedKey { get; init; }
        public required string? StoppedKey { get; init; }
    }
    public IReadOnlyList<object> ReferencedSnapshotObjects => new object?[]
        { _ownedRand == null ? _rand : null, Started, Stopped }.Where(x => x != null).Cast<object>().ToArray();
    public Snapshot CaptureState(Func<object, string> id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return new Snapshot { Version = 1, Entries = _entries.Select(e => new EntrySnapshot
            { CellX = e.CellX, CellZ = e.CellZ, UnitsX = e.UnitsX, UnitsZ = e.UnitsZ }).ToArray(), Refused = Refused,
            OwnedRandState = _ownedRand == null ? null : _ownedRand.CaptureState(),
            ExternalRandKey = SecuritySnapshotAccess.Key(_ownedRand == null ? _rand : null, id),
            StartedKey = SecuritySnapshotAccess.Key(Started, id), StoppedKey = SecuritySnapshotAccess.Key(Stopped, id) };
    }
    internal Action PrepareRestore(Snapshot s, Func<string, object> resolve)
    {
        ArgumentNullException.ThrowIfNull(s); ArgumentNullException.ThrowIfNull(resolve);
        SecuritySnapshotAccess.Require(s.Version == 1 && s.Entries != null && s.Entries.Length <= Capacity
            && s.Refused >= 0 && (s.OwnedRandState.HasValue != (s.ExternalRandKey != null)), "stink schema/RNG ownership");
        SecuritySnapshotAccess.Require(s.Entries!.All(e => e != null)
            && s.Entries.Select(e => (e.CellX, e.CellZ)).Distinct().Count() == s.Entries.Length, "stink cells");
        var rand = SecuritySnapshotAccess.Resolve<Func<int>>(s.ExternalRandKey, resolve);
        var started = SecuritySnapshotAccess.Resolve<Action<PrankStink>>(s.StartedKey, resolve);
        var stopped = SecuritySnapshotAccess.Resolve<Action<PrankStink>>(s.StoppedKey, resolve);
        var owned = s.OwnedRandState is uint state ? new NewlibRand(state) : null;
        var entries = s.Entries.Select(e => new PrankStink(e.CellX, e.CellZ, e.UnitsX, e.UnitsZ)).ToArray();
        return () => { _entries.Clear(); _entries.AddRange(entries); Refused = s.Refused;
            _ownedRand = owned!; _rand = owned == null ? rand! : owned.Next; Started = started; Stopped = stopped; };
    }
    public void ValidateSnapshot(Snapshot s, Func<string, object> resolve) => PrepareRestore(s, resolve);
    /// <summary>No emitter events replayed. A view rebuilds particles from Entries explicitly. An external
    /// RNG binding must already be restored by its owner; a seed is never substituted for its state.</summary>
    public void RestoreState(Snapshot s, Func<string, object> resolve) => PrepareRestore(s, resolve)();
}

public sealed partial class ParkStaff
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CarriedGuestSnapshot
    {
        public required int GuestId { get; init; }
        public required uint Serial { get; init; }
        public required short X { get; init; }
        public required short Z { get; init; }
        public required int Facing { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GuardJobSnapshot
    {
        public required int Slot { get; init; }
        public required int Leg { get; init; }
        public required CarriedGuestSnapshot? Carried { get; init; }
        public required int Catches { get; init; }
        public required int GiveUps { get; init; }
        public required int Dispatches { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record EntertainerJobSnapshot
    {
        public required int Slot { get; init; }
        public required int? EffectorSlot { get; init; }
        public required int Shows { get; init; }
        public required int Heckles { get; init; }
        public required int GuardsCalled { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record WatchSnapshot
    {
        public required int GuestId { get; init; }
        public required int EntertainerSlot { get; init; }
        public required uint Until { get; init; }
        public required int Facing { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GuestTimeSnapshot
    {
        public required int GuestId { get; init; }
        public required uint Tick { get; init; }
    }
    /// <summary>Only Security.cs owners and guard/entertainer job fields. StaffMember.Snapshot
    /// owns their base state (including target identities); the enclosing graph owns guest IDs,
    /// visitors, routes, gate state and providers. Guest IDs may legitimately be stale/dangling.</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record SecuritySnapshot
    {
        public required int Version { get; init; }
        public required ParkEffectors.Snapshot Effectors { get; init; }
        public required PrankStinks.Snapshot Stinks { get; init; }
        public required GuardJobSnapshot[] Guards { get; init; }
        public required EntertainerJobSnapshot[] Entertainers { get; init; }
        public required short[] AdvisorEvents { get; init; }
        public required uint CopySerial { get; init; }
        public required string? AnimationStateKey { get; init; }
        public required string? HeckledKey { get; init; }
        public required string? GuestCaughtKey { get; init; }
        public required string? GateKey { get; init; }
        public required bool OwnsGateEvent9 { get; init; }
        public required ParkCell? StagingCell { get; init; }
        public required int GateStaged { get; init; }
        public required int GateCrossed { get; init; }
        public required int GateOpened { get; init; }
        public required int DispatchesRefused { get; init; }
        public required int Heckles { get; init; }
        public required int Pranks { get; init; }
        public required int Caught { get; init; }
        public required int WatchesStarted { get; init; }
        public required int WatchesEnded { get; init; }
        public required int[] LiveTargets { get; init; }
        public required WatchSnapshot[] Watching { get; init; }
        public required IntMapLayout WatchingLayout {get;init;}
        public required IntMapLayout CooldownLayout {get;init;}
        public required IntMapLayout FirstSeenLayout {get;init;}
        public required GuestTimeSnapshot[] WatchCooldown { get; init; }
        public required GuestTimeSnapshot[] FirstSeen { get; init; }
    }
    public IReadOnlyList<object> ReferencedSecurityObjects => new object?[]
        { AnimationState, Heckled, GuestCaught, _gate }.Where(x => x != null).Cast<object>()
        .Concat(Stinks.ReferencedSnapshotObjects).ToArray();

    public SecuritySnapshot CaptureSecurityState(Func<object, string> id)
    {
        ArgumentNullException.ThrowIfNull(id);
        GuestTimeSnapshot[] Times(Dictionary<int, uint> d) => d.Select(p => new GuestTimeSnapshot { GuestId = p.Key, Tick = p.Value }).ToArray();
        return new SecuritySnapshot
        {
            Version = 1, Effectors = Effectors.CaptureState(), Stinks = Stinks.CaptureState(id),
            WatchingLayout=_watching.CaptureLayout(),CooldownLayout=_watchCooldown.CaptureLayout(),FirstSeenLayout=_firstSeen.CaptureLayout(),
            Guards = _slots[StaffKind.Guard].Cast<Guard>().Select(g => new GuardJobSnapshot
            {
                Slot = g.PoolSlot, Leg = g.Leg, Catches = g.Catches, GiveUps = g.GiveUps, Dispatches = g.Dispatches,
                Carried = g.Carried is not { } c ? null : new CarriedGuestSnapshot
                    { GuestId = c.GuestId, Serial = c.Serial, X = c.Position.X, Z = c.Position.Z, Facing = c.FacingQuarterTurns }
            }).ToArray(),
            Entertainers = _slots[StaffKind.Entertainer].Cast<Entertainer>().Select(e => new EntertainerJobSnapshot
                { Slot = e.PoolSlot, EffectorSlot = e.Effector?.Slot, Shows = e.Shows, Heckles = e.Heckles, GuardsCalled = e.GuardsCalled }).ToArray(),
            AdvisorEvents = (short[])AdvisorEvents.Clone(), CopySerial = _copySerial,
            AnimationStateKey = SecuritySnapshotAccess.Key(AnimationState, id), HeckledKey = SecuritySnapshotAccess.Key(Heckled, id),
            GuestCaughtKey = SecuritySnapshotAccess.Key(GuestCaught, id), GateKey = SecuritySnapshotAccess.Key(_gate, id),
            OwnsGateEvent9 = _gate != null && _gate.MemberEvent9 == GateEvent9, StagingCell = StagingCell,
            GateStaged = GateStaged, GateCrossed = GateCrossed, GateOpened = GateOpened, DispatchesRefused = DispatchesRefused,
            Heckles = Heckles, Pranks = Pranks, Caught = Caught, WatchesStarted = WatchesStarted, WatchesEnded = WatchesEnded,
            LiveTargets = _liveTargets.ToArray(), Watching = _watching.Select(p => new WatchSnapshot
                { GuestId = p.Key, EntertainerSlot = p.Value.Entertainer.PoolSlot, Until = p.Value.Until, Facing = p.Value.FacingQuarterTurns }).ToArray(),
            WatchCooldown = Times(_watchCooldown), FirstSeen = Times(_firstSeen)
        };
    }

    /// <summary>Read-only preflight, including every binding. Base member and visitor/gate graph
    /// validation belongs to the enclosing transaction. Does not query external providers.</summary>
    public void ValidateSecuritySnapshot(SecuritySnapshot s, Func<string, object> resolve) => PrepareSecurityRestore(s, resolve);

    Action PrepareSecurityRestore(SecuritySnapshot s, Func<string, object> resolve)
    {
        ArgumentNullException.ThrowIfNull(s); ArgumentNullException.ThrowIfNull(resolve);
        void R(bool ok, string why) => SecuritySnapshotAccess.Require(ok, why);
        R(s.Version == 1 && s.AdvisorEvents != null && s.AdvisorEvents.Length == 22, "schema/advisor array");
        ParkEffectors.ValidateSnapshot(s.Effectors);
        var stink = Stinks.PrepareRestore(s.Stinks, resolve);
        R(s.Guards != null && s.Guards.Length == StaffTables.PoolSize && s.Entertainers != null
            && s.Entertainers.Length == StaffTables.PoolSize, "job slots");
        var copies = new CarriedGuest?[StaffTables.PoolSize];
        var used = new HashSet<int>();
        for (int i = 0; i < StaffTables.PoolSize; i++)
        {
            var g = s.Guards![i]; var e = s.Entertainers![i];
            R(g != null && g.Slot == i && g.Leg is 0 or 1 && g.Catches >= 0 && g.GiveUps >= 0 && g.Dispatches >= 0, "guard job");
            if (g!.Carried is { } c)
            {
                R((uint)c.Facing <= 3, "carried facing");
                copies[i] = new CarriedGuest(c.GuestId, c.Serial) { Position = new(c.X, c.Z), FacingQuarterTurns = c.Facing };
            }
            R(e != null && e.Slot == i && e.Shows >= 0 && e.Heckles >= 0 && e.GuardsCalled >= 0, "entertainer job");
            if (e!.EffectorSlot is int slot)
                R((uint)slot < ParkEffectors.Capacity && s.Effectors.Slots[slot].Active && used.Add(slot), "effector reference/duplicate owner");
        }
        R(s.LiveTargets != null && s.LiveTargets.Length<=100_000 && s.LiveTargets.Distinct().Count() == s.LiveTargets.Length, "live targets");
        R(s.Watching != null && s.Watching.Length<=100_000 && s.Watching.All(w => w != null && (uint)w.EntertainerSlot < StaffTables.PoolSize && (uint)w.Facing <= 3)
            && s.Watching.Select(w => w.GuestId).Distinct().Count() == s.Watching.Length, "watchers");
        void Times(GuestTimeSnapshot[] times)
        { R(times != null && times.Length<=100_000 && times.All(t => t != null) && times.Select(t => t.GuestId).Distinct().Count() == times.Length, "guest timestamps"); }
        Times(s.WatchCooldown); Times(s.FirstSeen);
        void Layout(IEnumerable<int> keys,IntMapLayout l) {var m=new SnapshotIntMap<int>();foreach(var key in keys)m.Add(key,0);m.RestoreLayout(l);}
        Layout(s.Watching.Select(w=>w.GuestId),s.WatchingLayout);Layout(s.WatchCooldown.Select(w=>w.GuestId),s.CooldownLayout);
        Layout(s.FirstSeen.Select(w=>w.GuestId),s.FirstSeenLayout);
        R(new[] { s.GateStaged, s.GateCrossed, s.GateOpened, s.DispatchesRefused, s.Heckles, s.Pranks,
            s.Caught, s.WatchesStarted, s.WatchesEnded }.All(n => n >= 0), "counters");
        var animation = SecuritySnapshotAccess.Resolve<Func<StaffMember, (byte Current, byte Phase)?>>(s.AnimationStateKey, resolve);
        var heckled = SecuritySnapshotAccess.Resolve<Action<int, Entertainer>>(s.HeckledKey, resolve);
        var caught = SecuritySnapshotAccess.Resolve<Action<Guard, int>>(s.GuestCaughtKey, resolve);
        var gate = SecuritySnapshotAccess.Resolve<NativeEntranceFlow>(s.GateKey, resolve);
        R(!s.OwnsGateEvent9 || gate != null, "gate event owner");
        var watches = s.Watching!.Select(w => (w.GuestId, Watch: new Watch((Entertainer)_slots[StaffKind.Entertainer][w.EntertainerSlot], w.Until)
            { FacingQuarterTurns = w.Facing })).ToArray();
        return () =>
        {
            Effectors.Hydrate(s.Effectors); stink();
            for (int i = 0; i < StaffTables.PoolSize; i++)
            {
                var g = (Guard)_slots[StaffKind.Guard][i]; var a = s.Guards![i];
                g.RestoreJobFields(a.Leg, copies[i], a.Catches, a.GiveUps, a.Dispatches);
                var e = (Entertainer)_slots[StaffKind.Entertainer][i]; var b = s.Entertainers![i];
                e.RestoreJobFields(b.EffectorSlot is int slot ? Effectors.SnapshotSlot(slot) : null,
                    b.Shows, b.Heckles, b.GuardsCalled);
            }
            s.AdvisorEvents!.CopyTo(AdvisorEvents, 0); _copySerial = s.CopySerial;
            AnimationState = animation; Heckled = heckled; GuestCaught = caught;
            // Bypass Gate's gameplay setter (which detaches another live world's callback).
            _gate = gate; if (s.OwnsGateEvent9) _gate!.MemberEvent9 = GateEvent9;
            StagingCell = s.StagingCell; GateStaged = s.GateStaged; GateCrossed = s.GateCrossed; GateOpened = s.GateOpened;
            DispatchesRefused = s.DispatchesRefused; Heckles = s.Heckles; Pranks = s.Pranks; Caught = s.Caught;
            WatchesStarted = s.WatchesStarted; WatchesEnded = s.WatchesEnded;
            _liveTargets.Clear(); _liveTargets.UnionWith(s.LiveTargets!);
            _watching.Clear(); foreach (var w in watches) _watching.Add(w.GuestId, w.Watch);
            _watchCooldown.Clear(); foreach (var t in s.WatchCooldown) _watchCooldown.Add(t.GuestId, t.Tick);
            _firstSeen.Clear(); foreach (var t in s.FirstSeen) _firstSeen.Add(t.GuestId, t.Tick);
            _watching.RestoreLayout(s.WatchingLayout);_watchCooldown.RestoreLayout(s.CooldownLayout);_firstSeen.RestoreLayout(s.FirstSeenLayout);
        };
    }
    /// <summary>Hydrates existing UNPUBLISHED ParkStaff slot shells. Entire security preflight and
    /// reference resolution precedes all writes. No events, pool allocation, activation or RNG replay.
    /// Resolve external callbacks/gate to staged owners, never the source world's live objects.</summary>
    public void RestoreSecurityState(SecuritySnapshot snapshot, Func<string, object> resolve)
        => PrepareSecurityRestore(snapshot, resolve)();
}

public sealed partial class Guard
{
    internal void RestoreJobFields(int leg,CarriedGuest? carried,int catches,int giveUps,int dispatches)
    {Leg=leg;Carried=carried;Catches=catches;GiveUps=giveUps;Dispatches=dispatches;}
}
public sealed partial class Entertainer
{
    internal void RestoreJobFields(StaffEffector? effector,int shows,int heckles,int guardsCalled)
    {Effector=effector;Shows=shows;Heckles=heckles;GuardsCalled=guardsCalled;}
}
