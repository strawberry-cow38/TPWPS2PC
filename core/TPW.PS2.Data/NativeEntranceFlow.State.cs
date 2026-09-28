#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

public sealed partial class NativeEntranceFlow
{
    public const int SnapshotVersion = 1, SnapshotGuestLimit = 100_000;
    bool _stateHydrated = true;
    GuestWalk.GuestGraph? _stateGraph;

    /// <summary>Explicit externally staged service bundle. IDs identify providers, NOT their
    /// values: RNG, pending requests/results, readiness, fee/acceptance accounting, guard
    /// population and delegate targets must be saved by their own owners. No service is called
    /// by capture/validation/restore. The caller must supply fresh staged providers on load.</summary>
    public sealed class SnapshotBindings
    {
        public required GuestWalk.GuestGraph GuestGraph { get; init; }
        public required string ServicesId { get; init; }
        public required Services Services { get; init; }
        public required string OwnerId { get; init; }
        public string? MemberEvent9Id { get; init; }
        public Action? MemberEvent9 { get; init; }
        /// <summary>Trusted identity lookup only (capture). Includes inputs for vanished guests
        /// which may no longer have a Walk lease. Restore exposes StateInputs instead.</summary>
        public Func<NativeMotionInputs, string>? IdentifyInputs { get; init; }
    }

    /// <summary>Union this with the other owners' inventories before Walk.CaptureGraph.
    /// Includes admitted/inactive/vanished bodies until the next Tick cleans them up.</summary>
    public IReadOnlyList<Guest> ReferencedGuests => _allocated.Select(e => e.Guest)
        .Concat(_admitted.Keys).Distinct<Guest>(ReferenceEqualityComparer.Instance).ToArray();
    public object StateOwner => _owner;
    /// <summary>Exact immutable callback bundle, for identity-checked world capture. Never invokes it.</summary>
    public Services StateServices => _services;
    public NativeMotionInputs StateInputs(Guest guest) => _entries.TryGetValue(guest, out var e)
        ? e.Inputs : throw new ArgumentException("Guest is not an entrance entry.");
    public bool StateBindingsHydrated => _stateHydrated;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record EntrySnapshot
    {
        public required int GuestGraphId { get; init; }
        public required string InputsId { get; init; }
        public required uint? Serial { get; init; }
        public required sbyte Baseline { get; init; }
        public required sbyte Speed { get; init; }
        public required State State { get; init; }
        public required int Mode { get; init; }
        public required int Group { get; init; }
        public required ulong? Token { get; init; }
        public required bool StageCounted { get; init; }
        public required bool AcceptanceAttempted { get; init; }
        public required bool RejectionNotified { get; init; }
        public required bool Stopped { get; init; }
        public required bool OutgoingDirection { get; init; }
        public required bool DeferredSticky { get; init; }
        public required bool AlternateRequestFlag { get; init; }
        public required bool Ordinary { get; init; }
        public required uint? HeldAt { get; init; }
        public required bool? Accepted { get; init; }
        public required string? Failure { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record AdmittedSnapshot
    {
        public required int GuestGraphId { get; init; }
        public required uint Serial { get; init; }
        public required sbyte Baseline { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot
    {
        public required int Version { get; init; }
        public required string ServicesId { get; init; }
        public required string OwnerId { get; init; }
        public required string? MemberEvent9Id { get; init; }
        public required bool HasSlotAdvanced { get; init; }
        public required bool HasBusPoint { get; init; }
        // Entries are in allocation order. Other orders refer to guest GRAPH IDs.
        public required EntrySnapshot[] Entries { get; init; }
        public required int[] Active { get; init; }
        public required int[] Group0 { get; init; }
        public required int[] Group1 { get; init; }
        public required AdmittedSnapshot[] Admitted { get; init; }
        public required ulong NextToken { get; init; }
        public required uint Tick { get; init; }
        public required int StagingPending { get; init; }
        public required int EpisodeProcessed { get; init; }
        public required int DeparturePressure { get; init; }
        public required int HoldsResumed { get; init; }
        public required int DepartureHandbacks { get; init; }
        public required int GroupFlips { get; init; }
    }

    /// <summary>Quiescent, single-threaded boundary only: freeze Walk, visitors, guards and
    /// external services as one transaction. Rejects capture from Tick/Add/Clear callbacks.
    /// StageMember/CrossMember and subscription changes must also be frozen by the caller.
    /// Walk graph must be captured at this SAME boundary (including ReferencedGuests).
    /// Bound the JSON payload before deserializing. Delegates and service provider VALUES
    /// are explicitly outside this bounded owner.</summary>
    public Snapshot CaptureState(SnapshotBindings bindings)
    {
        if (_busy || !_stateHydrated) throw new InvalidOperationException("Entrance capture requires a quiescent hydrated owner.");
        ArgumentNullException.ThrowIfNull(bindings);
        RequireSnapshot((long)_allocated.Count + _admitted.Count <= SnapshotGuestLimit, "capture guest bound");
        RequireSnapshot(_allocated.All(e => e.Failure == null || e.Failure.Length <= 16384), "capture failure bound");
        RequireSnapshot(ReferenceEquals(bindings.Services, _services)
            && ReferenceEquals(bindings.MemberEvent9, MemberEvent9), "capture service identity");
        var graph = bindings.GuestGraph;
        RequireSnapshot(graph != null && ReferenceEquals(graph.Walk, _visitors.Walk), "walk graph");
        int Id(Entry e) => graph!.GuestGraphId(e.Guest);
        var s = new Snapshot
        {
            Version = SnapshotVersion, ServicesId = bindings.ServicesId, OwnerId = bindings.OwnerId,
            MemberEvent9Id = bindings.MemberEvent9Id, HasSlotAdvanced = _services.SlotAdvanced != null,
            HasBusPoint = _services.BusPoint != null,
            Entries = _allocated.Select(e => new EntrySnapshot
            {
                GuestGraphId = Id(e), InputsId = bindings.IdentifyInputs?.Invoke(e.Inputs)!,
                Serial = e.Serial, Baseline = e.Baseline, Speed = e.Speed, State = e.State,
                Mode = e.Mode, Group = e.Group, Token = e.Token, StageCounted = e.StageCounted,
                AcceptanceAttempted = e.AcceptanceAttempted, RejectionNotified = e.RejectionNotified,
                Stopped = e.Stopped, OutgoingDirection = e.OutgoingDirection, DeferredSticky = e.DeferredSticky,
                AlternateRequestFlag = e.AlternateRequestFlag, Ordinary = e.Ordinary, HeldAt = e.HeldAt,
                Accepted = e.Accepted, Failure = e.Failure
            }).ToArray(),
            Active = _active.Select(Id).ToArray(), Group0 = _incoming[0].Select(Id).ToArray(),
            Group1 = _incoming[1].Select(Id).ToArray(),
            // Dictionary iteration has no gameplay consumer; canonical graph order avoids
            // making CLR dictionary free-slot layout part of this lookup-only identity table.
            Admitted = _admitted.Select(p => new AdmittedSnapshot { GuestGraphId = graph!.GuestGraphId(p.Key),
                Serial = p.Value.Serial, Baseline = p.Value.Baseline }).OrderBy(p => p.GuestGraphId).ToArray(),
            NextToken = _nextToken, Tick = _tick, StagingPending = StagingPending,
            EpisodeProcessed = EpisodeProcessed, DeparturePressure = DeparturePressure,
            HoldsResumed = HoldsResumed, DepartureHandbacks = DepartureHandbacks, GroupFlips = GroupFlips
        };
        ValidateSnapshot(s, _visitors, bindings);
        ValidateLeaseObjects(graph!);
        return s;
    }

    static bool SnapshotKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 1024;
    static void RequireSnapshot([DoesNotReturnIf(false)] bool valid, string reason)
    { if (!valid) throw new ArgumentException("Invalid NativeEntranceFlow snapshot: " + reason); }

    /// <summary>Pure preflight. Reads DTO/graph identities only; never binds, mutates a world,
    /// acquires a lease, invokes providers, consumes RNG, submits requests or replays Tick.</summary>
    public static void ValidateSnapshot(Snapshot s, ParkVisitors visitors, SnapshotBindings b)
    {
        ArgumentNullException.ThrowIfNull(s); ArgumentNullException.ThrowIfNull(visitors);
        ArgumentNullException.ThrowIfNull(b);
        RequireSnapshot(s.Version == SnapshotVersion && SnapshotKey(s.ServicesId) && SnapshotKey(s.OwnerId)
            && s.ServicesId == b.ServicesId && s.OwnerId == b.OwnerId && b.Services != null
            && s.MemberEvent9Id == b.MemberEvent9Id
            && (s.MemberEvent9Id == null || SnapshotKey(s.MemberEvent9Id))
            && (s.MemberEvent9Id != null) == (b.MemberEvent9 != null)
            && s.HasBusPoint == (b.Services!.BusPoint != null)
            && s.HasSlotAdvanced == (b.Services!.SlotAdvanced != null), "schema/service binding");
        RequireSnapshot(b.GuestGraph != null && ReferenceEquals(b.GuestGraph.Walk, visitors.Walk), "graph/visitors binding");
        RequireSnapshot(s.Entries != null && s.Entries.Length <= SnapshotGuestLimit
            && s.Admitted != null && s.Admitted.Length <= SnapshotGuestLimit - s.Entries!.Length
            && s.Active != null && s.Active.Length <= s.Entries.Length
            && s.Group0 != null && s.Group0.Length <= s.Entries.Length
            && s.Group1 != null && s.Group1.Length <= s.Entries.Length, "table bounds");
        RequireSnapshot(s.StagingPending >= 0 && s.EpisodeProcessed >= 0 && s.DeparturePressure >= 0
            && s.HoldsResumed >= 0 && s.DepartureHandbacks >= 0 && s.DepartureHandbacks <= s.HoldsResumed
            && s.GroupFlips >= 0, "counters");
        var rows = new Dictionary<int, EntrySnapshot>();
        var inputs = new HashSet<string>(StringComparer.Ordinal);
        var tokens = new HashSet<ulong>();
        var guests = b.GuestGraph!.Snapshot.Guests.ToDictionary(g => g.GraphId);
        var live = b.GuestGraph.Snapshot.OrderedLiveGuests.ToHashSet();
        foreach (var e in s.Entries!)
        {
            RequireSnapshot(e != null && guests.ContainsKey(e.GuestGraphId) && rows.TryAdd(e.GuestGraphId, e), "entry guest identity");
            RequireSnapshot(SnapshotKey(e!.InputsId) && inputs.Add(e.InputsId) && Enum.IsDefined(e.State)
                && e.Mode is 0 or 2 or 9 or 11 or 12 or 13 or 14 or 15 or 16 && e.Group is >= -1 and <= 1
                && (e.Failure == null || e.Failure.Length <= 16384), "entry fields");
            RequireSnapshot(!e.Serial.HasValue || s.HasBusPoint, "serial needs bus service");
            RequireSnapshot(!e.Token.HasValue || (e.Token.Value > 0 && e.Token.Value <= s.NextToken
                && tokens.Add(e.Token.Value) && e.State == State.Pending), "pending token");
            RequireSnapshot(e.State != State.Pending || e.Mode is 9 or 11 or 14 or 15, "pending mode");
            RequireSnapshot(e.State != State.Pending || e.Token.HasValue || e.Stopped
                || (e.Mode == 14 && !e.AlternateRequestFlag), "tokenless pending purpose");
            RequireSnapshot(e.State != State.Moving || e.Mode is 9 or 11 or 12 or 13 or 14 or 15 or 16, "moving mode");
            RequireSnapshot(!e.Accepted.HasValue || e.AcceptanceAttempted, "acceptance latch");
            RequireSnapshot(e.State != State.Accepted || e.AcceptanceAttempted
                && (e.Accepted == true || e.Stopped), "accepted state latch");
            RequireSnapshot(e.State != State.Rejected || e.Ordinary || e.Accepted == false, "rejected state latch");
            RequireSnapshot(!e.StageCounted || e.State is State.Staged or State.CrossStaging
                || e.State == State.Moving && e.Mode == 16, "counted staging state");
            RequireSnapshot(e.State is not (State.Staged or State.CrossStaging) || e.StageCounted, "uncounted staging state");
            RequireSnapshot(!e.RejectionNotified || e.Accepted == false, "rejection latch");
            RequireSnapshot(!e.Ordinary || (e.Serial.HasValue && !e.AcceptanceAttempted), "ordinary identity");
            RequireSnapshot(!e.HeldAt.HasValue || e.State is State.DecisionBoundary or State.RecoveryBoundary, "hold state");
            if (live.Contains(e.GuestGraphId) && !e.Stopped)
                RequireSnapshot(visitors.Plans.TryGetValue(guests[e.GuestGraphId].Id, out var plan)
                    && plan.Intent is VisitorIntent.Entering or VisitorIntent.Leaving, "live visitor membership");
            var lease = guests[e.GuestGraphId].NativeLease;
            if (lease == null)
                RequireSnapshot(!live.Contains(e.GuestGraphId) || e.Stopped, "live entry missing lease");
            else RequireSnapshot(lease.OwnerId == s.OwnerId && lease.InputsId == e.InputsId
                && !lease.AutomaticStep && lease.HasSlotAdvanced == s.HasSlotAdvanced, "entry lease identity/flags");
        }
        var membership = new HashSet<int>();
        void List(int[] ids, int group)
        {
            foreach (int id in ids)
            {
                RequireSnapshot(rows.TryGetValue(id, out var e) && membership.Add(id), "missing/duplicate membership");
                if (group >= 0) RequireSnapshot(e!.Group == group && e.State is State.Pending or State.Moving
                    or State.RequestQueue or State.Reposition or State.Waiting, "queue membership/state");
                else RequireSnapshot(e!.State is not (State.Reposition or State.Waiting), "queue state without membership");
                if (group >= 0)
                    RequireSnapshot(!e!.StageCounted && !e.AcceptanceAttempted && !e.Ordinary
                        && (e.State != State.Pending || e.Mode == 11)
                        && (e.State != State.Moving || e.Mode is 11 or 12), "queue entry purpose");
            }
        }
        List(s.Active!, -1); List(s.Group0!, 0); List(s.Group1!, 1);
        RequireSnapshot(membership.Count == rows.Count, "entry missing membership");
        // P is shared with guards, so it need NOT equal the guest population.
        RequireSnapshot(s.StagingPending >= s.Entries.Count(e => e.StageCounted), "staging population");
        var admitted = new HashSet<int>();
        foreach (var a in s.Admitted!) RequireSnapshot(a != null && guests.ContainsKey(a.GuestGraphId)
            && !rows.ContainsKey(a.GuestGraphId) && admitted.Add(a.GuestGraphId), "admitted identity");
        foreach (var g in guests.Values)
            if (g.NativeLease is { } l)
            {
                if (l.OwnerId == s.OwnerId) RequireSnapshot(rows.ContainsKey(g.GraphId), "orphan owner lease");
                if (inputs.Contains(l.InputsId)) RequireSnapshot(rows.TryGetValue(g.GraphId, out var e)
                    && e.InputsId == l.InputsId && l.OwnerId == s.OwnerId, "aliased input lease");
            }
    }

    /// <summary>Allocate after Walk.AllocateState and ParkVisitors.AllocateState, BEFORE Walk
    /// Hydrate. Exposes StateOwner/StateInputs for Walk's cyclic bindings. Snapshot scalars and
    /// list order are copied, no DTO is retained. Only a fresh unpublished staged graph is valid.
    /// Do not publish until Walk, visitors and every provider has also hydrated successfully.</summary>
    public static NativeEntranceFlow AllocateState(Snapshot s, ParkVisitors visitors, SnapshotBindings b)
    {
        ValidateSnapshot(s, visitors, b);
        RequireSnapshot(!b.GuestGraph.IsHydrated, "restore requires unhydrated staged Walk graph");
        var flow = new NativeEntranceFlow(visitors, b.Services)
        {
            _stateHydrated = false, _stateGraph = b.GuestGraph, MemberEvent9 = b.MemberEvent9,
            _nextToken = s.NextToken, _tick = s.Tick, StagingPending = s.StagingPending,
            EpisodeProcessed = s.EpisodeProcessed, DeparturePressure = s.DeparturePressure,
            HoldsResumed = s.HoldsResumed, DepartureHandbacks = s.DepartureHandbacks, GroupFlips = s.GroupFlips
        };
        foreach (var r in s.Entries)
        {
            var e = new Entry(b.GuestGraph.GuestByGraphId(r.GuestGraphId), r.Baseline, r.Serial, b.Services)
            {
                Speed = r.Speed, State = r.State, Mode = r.Mode, Group = r.Group, Token = r.Token,
                StageCounted = r.StageCounted, AcceptanceAttempted = r.AcceptanceAttempted,
                RejectionNotified = r.RejectionNotified, Stopped = r.Stopped,
                OutgoingDirection = r.OutgoingDirection, DeferredSticky = r.DeferredSticky,
                AlternateRequestFlag = r.AlternateRequestFlag, Ordinary = r.Ordinary,
                HeldAt = r.HeldAt, Accepted = r.Accepted, Failure = r.Failure
            };
            flow._entries.Add(e.Guest, e); e.AllocatedNode = flow._allocated.AddLast(e);
        }
        Entry Find(int id) => flow._entries[b.GuestGraph.GuestByGraphId(id)];
        foreach (int id in s.Active) { var e = Find(id); e.ActiveNode = flow._active.AddLast(e); }
        foreach (int id in s.Group0) { var e = Find(id); e.QueueNode = flow._incoming[0].AddLast(e); }
        foreach (int id in s.Group1) { var e = Find(id); e.QueueNode = flow._incoming[1].AddLast(e); }
        foreach (var a in s.Admitted) flow._admitted.Add(b.GuestGraph.GuestByGraphId(a.GuestGraphId), (a.Serial, a.Baseline));
        return flow;
    }

    void ValidateLeaseObjects(GuestWalk.GuestGraph graph)
    {
        foreach (var e in _allocated)
        {
            var lease = e.Guest.NativeMotion;
            RequireSnapshot(lease == null ? !_visitors.Walk.IsLive(e.Guest) || e.Stopped
                : ReferenceEquals(lease.Owner, _owner) && ReferenceEquals(lease.Inputs, e.Inputs), "hydrated lease object identity");
        }
        foreach (var guest in graph.GuestsByGraphId.Values)
            if (guest.NativeMotion is { } l && ReferenceEquals(l.Owner, _owner))
                RequireSnapshot(_entries.ContainsKey(guest), "hydrated orphan lease");
    }

    /// <summary>Finish after Walk.Hydrate has adopted these exact owner/input objects.
    /// Validation failure leaves the shell unhydrated. No callbacks or external writes.</summary>
    public void HydrateStateBindings()
    {
        if (_stateHydrated || _stateGraph == null) throw new InvalidOperationException("No pending entrance snapshot.");
        RequireSnapshot(_stateGraph.IsHydrated, "Walk must hydrate first");
        ValidateLeaseObjects(_stateGraph);
        _stateGraph = null; _stateHydrated = true;
    }
}
