#nullable enable
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

/// <summary>
/// Research-only incoming/rejected-departure entrance controller (native-entrance-lifecycle.md).
/// Owns existing identities through ParkVisitors, not a pathfinder, admission default,
/// search-node/request pool or ordinary departure decision implementation. Output slots are shared through Walk. Call once per Walk tick, BEFORE the
/// experimental bus consumer. The ordinary Walk loop MUST NOT also step these leases.
/// All calls and services are single-threaded; callbacks must not reenter mutations.
/// </summary>
public sealed class NativeEntranceFlow
{
    public enum State
    {
        DecisionBoundary = 0x00, Moving = 0x03, RecoveryBoundary = 0x05, Pending = 0x0B, RequestStaging = 0x24,
        Evaluate = 0x25, Rejected = 0x26, RequestQueue = 0x2A,
        Reposition = 0x2B, Waiting = 0x2C, Accepted = 0x2D,
        Staged = 0x2E, CrossStaging = 0x2F, RequestBus = 0x30
    }

    /// <summary>Failure is explicit; a successful request requires at least its endpoint slot.
    /// Pump transfers a snapshot, not a completion delegate callable during Request.</summary>
    public sealed record RouteResult(ulong Token, Guest Guest, Point[]? Waypoints, string? Failure)
    {
        public bool Succeeded => Failure == null && Waypoints != null;
    }

    public sealed record RouteRequest(ulong Token, Guest Guest, int Mode, Point From,
        Point Target, int Flags, int Priority);

    /// <summary>Values are copied; Guest identifies the original object, not an ID lookup.
    /// Group is -1 until selected (and after head release/evaluation). Stopped entries
    /// retain their lease/membership and need explicit parent recovery or teardown.</summary>
    public readonly record struct Observation(Guest Guest, State State, int Mode, int Group,
        sbyte Speed, sbyte BaselineSpeed, ulong? PendingToken, bool InQueue,
        bool StageCounted, bool AcceptanceAttempted, bool? Accepted,
        bool Stopped, string? Failure, uint? Serial = null, bool OutgoingDirection = false,
        bool DeferredSticky = false, bool AlternateRequestFlag = false);

    public readonly record struct TraceEvent(string Event, Observation Entry);

    /// <summary>
    /// Incoming behavioral services are required. Random(n) must return [0,n).
    /// StagingTarget implements 1532D8, INCLUDING its random-x consumption even when
    /// only z is used. QueueBase is row+4/+5, in signed 8-fractional-bit coordinates.
    /// Request only submits; accepted work is delivered by a LATER Pump. Pump drains
    /// available results; the controller materializes/copies the whole batch first.
    /// StepOwnedNative must step exactly the named guest under the supplied owner.
    /// Accept owns the fee/value calculation and charge, and is invoked at most once.
    /// ExitCandidates supplies the scan lazily; allocation attempts interleave with
    /// candidates in the SAME update. ExitGoal is the single-candidate compatibility
    /// service when no candidate iterator is supplied. TryDirect is an injected gate,
    /// not a fake capacity counter: actual slots come from Walk.NativeRoutes.
    /// Reject is a one-shot boundary notification, NOT permission to release the lease.
    /// BusPoint is required when Add supplies a serial; its index has ALREADY consumed
    /// Random(1). RequestDetailed supersedes Request and exposes flags/priority; the
    /// legacy callback remains a compatibility fallback, not a flags-aware service.
    /// AfterResult runs in finally after each handler (including stale results), so
    /// Pump must retain request resources until then, across any alternate submission.
    /// Trace is optional, observational, and must not throw.
    /// </summary>
    public sealed class Services
    {
        public Func<int, int> Random { get; }
        public Func<Guest, Point> StagingTarget { get; }
        public Point QueueBase { get; }
        public Func<ulong, Guest, int, Point, Point, bool> Request { get; }
        public Func<IEnumerable<RouteResult>> Pump { get; }
        public Func<Point, bool> TryDirect { get; }
        public Func<Guest, bool> Ready { get; }
        public Func<int> Delta { get; }
        public Func<Guest, bool> Accept { get; }
        public Func<Guest, Point?> ExitGoal { get; }
        public Func<Guest, IEnumerable<Point>> ExitCandidates { get; }
        public Action<Guest> Reject { get; }
        public Action<Guest, object> StepOwnedNative { get; }
        public Action<TraceEvent>? Trace { get; }
        public Func<Guest, int, Point>? BusPoint { get; }
        public Func<RouteRequest, bool>? RequestDetailed { get; }
        public Action<RouteResult>? AfterResult { get; }
        /// <summary>One-shot notification with the failed movement mode (14 or 9).
        /// Ordinary decision/recovery is unported; the owner lease remains held.</summary>
        public Action<Guest, int>? Recovery { get; }

        public Services(Func<int, int> random, Func<Guest, Point> stagingTarget,
            Point queueBase, Func<ulong, Guest, int, Point, Point, bool> request,
            Func<IEnumerable<RouteResult>> pump, Func<Point, bool> tryDirect,
            Func<Guest, bool> ready, Func<int> delta, Func<Guest, bool> accept,
            Func<Guest, Point?> exitGoal, Action<Guest> reject,
            Action<Guest, object> stepOwnedNative, Action<TraceEvent>? trace = null,
            Func<Guest, IEnumerable<Point>>? exitCandidates = null,
            Func<Guest, int, Point>? busPoint = null,
            Func<RouteRequest, bool>? requestDetailed = null,
            Action<RouteResult>? afterResult = null, Action<Guest, int>? recovery = null)
        {
            Random = random ?? throw new ArgumentNullException(nameof(random));
            StagingTarget = stagingTarget ?? throw new ArgumentNullException(nameof(stagingTarget));
            QueueBase = queueBase;
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Pump = pump ?? throw new ArgumentNullException(nameof(pump));
            TryDirect = tryDirect ?? throw new ArgumentNullException(nameof(tryDirect));
            Ready = ready ?? throw new ArgumentNullException(nameof(ready));
            Delta = delta ?? throw new ArgumentNullException(nameof(delta));
            Accept = accept ?? throw new ArgumentNullException(nameof(accept));
            ExitGoal = exitGoal ?? throw new ArgumentNullException(nameof(exitGoal));
            ExitCandidates = exitCandidates ?? (g => ExitGoal(g) is {} p ? new[] { p } : Array.Empty<Point>());
            Reject = reject ?? throw new ArgumentNullException(nameof(reject));
            StepOwnedNative = stepOwnedNative ?? throw new ArgumentNullException(nameof(stepOwnedNative));
            Trace = trace;
            BusPoint = busPoint;
            RequestDetailed = requestDetailed;
            AfterResult = afterResult;
            Recovery = recovery;
        }
    }

    sealed class Entry
    {
        internal readonly Guest Guest;
        internal readonly uint? Serial;
        internal readonly sbyte Baseline;
        internal readonly NativeMotionInputs Inputs;
        internal State State = State.RequestStaging;
        internal int Mode, Group = -1;
        internal sbyte Speed;
        internal ulong? Token;
        internal bool StageCounted, AcceptanceAttempted, RejectionNotified, Stopped;
        internal bool OutgoingDirection, DeferredSticky, AlternateRequestFlag;
        internal bool? Accepted;
        internal string? Failure;
        internal LinkedListNode<Entry>? ActiveNode, QueueNode, AllocatedNode;

        internal Entry(Guest guest, sbyte baseline, uint? serial, Services services)
        {
            Guest = guest;
            Serial = serial;
            Baseline = Speed = baseline;
            Inputs = new NativeMotionInputs(() => Speed, services.Delta,
                () => services.Ready(guest)) { AutomaticStep = false };
        }
    }

    readonly ParkVisitors _visitors;
    readonly Services _services;
    // Never key controller state by the display ID: the visitor/walk adapter itself
    // enforces the ID-owned lease constraint, while callbacks require exact identity.
    readonly Dictionary<Guest, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    readonly LinkedList<Entry> _allocated = new();
    readonly LinkedList<Entry> _active = new();
    readonly LinkedList<Entry>[] _incoming = { new(), new() };
    readonly object _owner = new();
    ulong _nextToken;
    uint _tick;
    bool _busy;

    public NativeEntranceFlow(ParkVisitors visitors, Services services)
    {
        _visitors = visitors ?? throw new ArgumentNullException(nameof(visitors));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public (int Group0, int Group1) Counts => (_incoming[0].Count, _incoming[1].Count);
    public int StagingPending { get; private set; }
    public int EpisodeProcessed { get; private set; }
    /// <summary>Ordered A4 contributions from live active entries BEFORE their update.
    /// Includes guests removed later in this pass; new deferrals count next pass.</summary>
    public int DeparturePressure { get; private set; }

    /// <summary>Reference-identity gate for the parent Walk loop: these guests must
    /// only be stepped through the supplied StepOwnedNative callback.</summary>
    public bool Owns(Guest guest) => guest != null && _entries.ContainsKey(guest);

    /// <summary>Source fixed-point position under the current owner, never a rendering float.</summary>
    public Point? Position(Guest guest) => guest != null && _entries.TryGetValue(guest, out var e) && Live(e)
        ? Motion(e).Position : null;

    public IReadOnlyList<Observation> Observations =>
        Array.AsReadOnly(_allocated.Select(Snapshot).ToArray());

    static Observation Snapshot(Entry e) => new(e.Guest, e.State, e.Mode, e.Group,
        e.Speed, e.Baseline, e.Token, e.QueueNode != null, e.StageCounted,
        e.AcceptanceAttempted, e.Accepted, e.Stopped, e.Failure, e.Serial,
        e.OutgoingDirection, e.DeferredSticky, e.AlternateRequestFlag);

    void Trace(string message, Entry e) => _services.Trace?.Invoke(new(message, Snapshot(e)));

    void Enter()
    {
        if (_busy) throw new InvalidOperationException("NativeEntranceFlow callbacks cannot reenter mutations.");
        _busy = true;
    }

    /// <summary>Prepends both allocation and active lists, taking an EMPTY native
    /// route immediately so neither ordinary walking nor destination selection can
    /// run in the asynchronous request gap. Refusal throws without adding an entry.
    /// activationSerial is supplied by the parent's represented shared activation
    /// sequence, never inferred from Guest.Id or pool order. Null explicitly holds
    /// rejection at the legacy boundary; this does not reconstruct native startup history.</summary>
    public void Add(Guest guest, sbyte baselineSpeed, uint? activationSerial = null)
    {
        ArgumentNullException.ThrowIfNull(guest);
        Enter();
        try
        {
            if (_entries.ContainsKey(guest))
                throw new InvalidOperationException("This guest reference already belongs to the entrance flow.");
            if (activationSerial.HasValue && _services.BusPoint == null)
                throw new InvalidOperationException("Explicit departure serial requires a bus-point supplier.");
            var e = new Entry(guest, baselineSpeed, activationSerial, _services);
            if (!_visitors.BeginEntranceRoute(guest, _owner, Array.Empty<Point>(), e.Inputs))
                throw new InvalidOperationException("Initial entrance lease refused; identity was not added.");
            _entries.Add(guest, e);
            e.AllocatedNode = _allocated.AddFirst(e);
            e.ActiveNode = _active.AddFirst(e);
            Trace("added; empty lease acquired", e);
        }
        finally { _busy = false; }
    }

    /// <summary>Returns shared E; the parent bus applies its own 0/2 writes afterward.
    /// This controller contributes only its own staging population, not the unjoined
    /// second native coordinator class. Neither guest ID nor phase is an activation serial.</summary>
    public int Tick(uint tick, int busState, int traffic)
    {
        Enter();
        try
        {
            _tick = tick;
            // FIRST: snapshot the result batch, including waypoint storage. There is
            // no public completion callback; submissions below cannot complete inline.
            var results = _services.Pump().Select(r => r with
            {
                Waypoints = r.Waypoints == null ? null : (Point[])r.Waypoints.Clone()
            }).ToArray();
            RemoveVanished();
            foreach (var result in results)
            {
                // Native notification may submit an alternate request BEFORE the old
                // request record is recycled. Acknowledge even stale results, last.
                try { ApplyResult(result); }
                finally { _services.AfterResult?.Invoke(result); }
            }

            // One update per membership pass. Saved-next survives registration/unlink;
            // the set also protects against an accidental second step after transfer.
            var updated = new HashSet<Entry>();
            for (int group = 0; group < 2; group++)
            {
                var list = _incoming[group];
                bool released = false;
                if ((tick & 31u) == (uint)group && list.First is { } head
                    && head.Value.State == State.Waiting && !head.Value.Stopped)
                {
                    var e = head.Value;
                    list.Remove(head);
                    e.QueueNode = null;
                    e.ActiveNode = _active.AddFirst(e);
                    e.State = State.Evaluate;
                    released = true;
                    Trace("queue head released", e);
                }
                for (var node = list.First; node != null;)
                {
                    var next = node.Next;
                    var e = node.Value;
                    if (released && e.State == State.Waiting && !e.Stopped)
                    {
                        e.State = State.Reposition; // event6; replacement happens in Update
                        Trace("event6", e);
                    }
                    UpdateOnce(e, updated);
                    node = next;
                }
            }

            DeparturePressure = 0; // group-pass contributions are discarded natively

            // Coordinator is intentionally BETWEEN groups and active updates.
            if (StagingPending != 0 && traffic == 0)
            {
                EpisodeProcessed = 0;
                traffic = 1;
            }
            if (EpisodeProcessed < 11 && StagingPending != 0 && traffic == 1)
            {
                for (var node = _allocated.First; node != null;)
                {
                    var next = node.Next;
                    var e = node.Value;
                    if (e.State == State.Staged && !e.Stopped)
                    {
                        e.State = State.CrossStaging;
                        Trace("event9", e);
                    }
                    node = next;
                }
            }
            if ((StagingPending == 0 || (EpisodeProcessed >= 11 && busState == 2)) && traffic == 1)
                traffic = 0; // never clear E==2

            for (var node = _active.First; node != null;)
            {
                var next = node.Next;
                var e = node.Value;
                // Count before dispatch, duplicate-update/stopped suppression and even
                // mode9 removal. Never infer this tally from the post-update collection.
                if (Live(e) && e.DeferredSticky) DeparturePressure++;
                UpdateOnce(e, updated);
                node = next;
            }
            RemoveVanished();
            return traffic;
        }
        finally { _busy = false; }
    }

    bool Live(Entry e) => _visitors.Walk.IsLive(e.Guest);

    void RemoveVanished()
    {
        for (var node = _allocated.First; node != null;)
        {
            var next = node.Next;
            if (!Live(node.Value))
            {
                Trace("SAFETY: guest vanished; logical cancellation and membership cleanup", node.Value);
                Forget(node.Value);
            }
            node = next;
        }
    }

    void ApplyResult(RouteResult result)
    {
        if (result.Guest == null || !_entries.TryGetValue(result.Guest, out var e)
            || e.Stopped || e.State != State.Pending || e.Token != result.Token) return;
        if (!Live(e)) { Forget(e); return; }
        e.Token = null; // consume exactly once, including failure
        try
        {
            if (!result.Succeeded || result.Waypoints!.Length == 0)
                HandleFailure(e, result.Failure ?? "Route service returned no waypoints.");
            else Assign(e, e.Mode, result.Waypoints!);
        }
        catch (Exception ex) { Stop(e, ex.Message); throw; }
    }

    // Native event2, also used for output-builder exhaustion after search success.
    void HandleFailure(Entry e, string failure)
    {
        e.Failure = failure;
        switch (e.Mode)
        {
            case 15:
                e.State = State.RequestStaging;
                Trace("mode15 event2; retry staging", e);
                break;
            case 11:
                e.State = State.RequestQueue;
                Trace("mode11 event2; retry queue", e);
                break;
            case 14:
                if (e.AlternateRequestFlag)
                {
                    e.State = State.DecisionBoundary;
                    Trace("mode14 alternate failed; ordinary decision boundary, lease retained", e);
                    _services.Recovery?.Invoke(e.Guest, e.Mode);
                }
                else
                {
                    // Token was consumed before entering here. Refusal deliberately
                    // leaves Pending with NO token; there is no automatic resubmit.
                    e.State = State.Pending;
                    Trace("mode14 event2; immediate alternate request", e);
                    if (Request(e, 14, _services.StagingTarget(e.Guest), 0x23))
                    {
                        e.AlternateRequestFlag = true;
                        Trace("mode14 alternate accepted; flag20 set", e);
                    }
                    else Trace("mode14 alternate refused; pending without token retained", e);
                }
                break;
            case 9:
                var needs = _visitors.Needs;
                if (needs == null || !needs.Has(e.Guest.Id))
                    throw new InvalidOperationException("Mode9 recovery requires existing visitor needs; reseeding is not supported.");
                var w = needs.Of(e.Guest.Id);
                // VisitorWants documents Happiness=N+75 and Unknown78=N+78.
                // These event2 byte loads are SIGNED; N+78 has no lower clamp here.
                w.Happiness = (byte)Math.Max(0, unchecked((sbyte)w.Happiness) - _services.Random(15));
                w.Unknown78 = unchecked((byte)Math.Min(100, unchecked((sbyte)w.Unknown78) + _services.Random(2)));
                needs.Set(e.Guest.Id, w);
                e.Group = -1;
                e.State = State.RecoveryBoundary;
                Trace("mode9 event2; recovery boundary, lease retained", e);
                _services.Recovery?.Invoke(e.Guest, e.Mode);
                break;
            default:
                throw new InvalidOperationException("Unimplemented asynchronous entrance failure purpose.");
        }
    }

    void UpdateOnce(Entry e, HashSet<Entry> updated)
    {
        if (!updated.Add(e) || !_entries.ContainsKey(e.Guest)) return;
        if (!Live(e)) { Forget(e); return; }
        if (e.Stopped) return;
        try { Update(e); }
        catch (Exception ex)
        {
            // In particular a fee callback may have charged before throwing. Never
            // retry it, or silently treat an exception/lease refusal as acceptance.
            Stop(e, ex.Message);
            throw;
        }
    }

    void Stop(Entry e, string failure)
    {
        e.Stopped = true;
        e.Failure = failure;
        Trace("STOPPED", e);
    }

    NativeMotionSnapshot Motion(Entry e) => _visitors.Walk.NativeRouteState(e.Guest, _owner)
        ?? throw new InvalidOperationException("Entrance lease unexpectedly missing or replaced.");

    void Assign(Entry e, int mode, IReadOnlyList<Point> waypoints)
    {
        var result = _visitors.AssignEntranceRoute(e.Guest, _owner, waypoints, e.Inputs);
        if (result == GuestWalk.NativeAssignment.Refused)
            throw new InvalidOperationException("Entrance route replacement unexpectedly refused.");
        if (result == GuestWalk.NativeAssignment.Exhausted)
        {
            // 18D488->18D574 rolls back private output, notification2 restores retry.
            // Old route was already disposed; empty owner lease prevents ordinary AI stealing.
            HandleFailure(e, "shared output pool exhausted while building route");
            return;
        }
        e.Mode = mode;
        e.AlternateRequestFlag = false; // event1 only AFTER actual output build success
        e.State = State.Moving;
        e.Failure = null;
        Trace("route assigned", e);
    }

    bool AssignDirect(Entry e, int mode, Point probe, Func<Point> target)
    {
        // Each native direct path disposes its previous chain BEFORE allocation.
        // The injected gate remains useful for controlled failures but is NOT the pool.
        if (!_visitors.BeginEntranceRoute(e.Guest, _owner, Array.Empty<Point>(), e.Inputs))
            throw new InvalidOperationException("Entrance direct replacement could not retain its owner.");
        if (mode != 16) e.Mode = mode; // mode16 writes only AFTER successful allocation
        if (!_services.TryDirect(probe)) { Trace($"mode{mode} allocation refused", e); return false; }
        var result = _visitors.AssignDirectEntranceRoute(e.Guest, _owner,
            () => { e.Mode = mode; return target(); }, e.Inputs);
        if (result == GuestWalk.NativeAssignment.Refused)
            throw new InvalidOperationException("Entrance direct assignment unexpectedly refused.");
        if (result == GuestWalk.NativeAssignment.Exhausted)
        {
            e.Failure = "shared output pool exhausted for direct route";
            Trace($"mode{mode} allocation exhausted", e);
            return false;
        }
        e.State = State.Moving;
        e.Failure = null;
        Trace("route assigned", e);
        return true;
    }

    bool Request(Entry e, int mode, Point target, int flags = 1)
    {
        var from = Motion(e).Position;
        // Checked, never reset by Clear: an old result cannot match a reused identity.
        ulong token = checked(++_nextToken);
        var retryState = e.State;
        e.Mode = mode;
        e.Token = token;
        e.State = State.Pending;
        bool accepted = _services.RequestDetailed != null
            ? _services.RequestDetailed(new(token, e.Guest, mode, from, target, flags, 0))
            : _services.Request(token, e.Guest, mode, from, target);
        if (!accepted)
        {
            e.Token = null;
            e.State = retryState;
            e.Failure = "Route request refused.";
            Trace("request refused; membership retained", e);
            return false;
        }
        if (mode == 11) e.Speed = 15;
        e.Failure = null;
        Trace("request accepted (not route completion)", e);
        return true;
    }

    Point QueuePoint(Entry e)
    {
        if (e.Group is < 0 or > 1 || e.QueueNode == null)
            throw new InvalidOperationException("Queue point requires registered incoming membership.");
        int predecessors = 0;
        for (var n = e.QueueNode.Previous; n != null; n = n.Previous) predecessors++;
        // 152868 overwrites the preliminary group!=0 x+256. BOTH use base.X.
        // Match signed-short position storage; height is deliberately not modeled.
        return new Point(_services.QueueBase.X,
            unchecked((short)(_services.QueueBase.Z - 64 * predecessors)));
    }

    Point Register(Entry e)
    {
        if (e.Group is < 0 or > 1)
            throw new InvalidOperationException("Incoming group was not selected by mode16 completion.");
        if (e.QueueNode == null)
        {
            if (e.ActiveNode != null) _active.Remove(e.ActiveNode);
            e.ActiveNode = null;
            e.QueueNode = _incoming[e.Group].AddLast(e);
            Trace("queue registered", e);
        }
        return QueuePoint(e);
    }

    void Update(Entry e)
    {
        switch (e.State)
        {
            case State.RequestStaging:
                Request(e, 15, _services.StagingTarget(e.Guest));
                break;
            case State.Moving:
                _services.StepOwnedNative(e.Guest, _owner);
                if (!Live(e)) { Forget(e); break; }
                var motion = Motion(e);
                if (motion.Failed)
                {
                    Stop(e, "Native route failed; parent recovery required (lease retained).");
                    break;
                }
                if (motion.Finished) Complete(e, motion.Position);
                break;
            case State.CrossStaging:
                var current = Motion(e).Position;
                AssignDirect(e, 16, current, () =>
                {
                    // Native allocation succeeds BEFORE this helper consumes even its unused random-X.
                    var staging = _services.StagingTarget(e.Guest);
                    return new Point(current.X, staging.Z);
                });
                break;
            case State.RequestQueue:
                Request(e, 11, Register(e)); // membership BEFORE submission, even on refusal
                break;
            case State.Reposition:
                var queue = QueuePoint(e);
                AssignDirect(e, 12, queue, () => queue);
                break;
            case State.Evaluate:
                e.Group = -1;
                e.Speed = e.Baseline;
                if (e.AcceptanceAttempted)
                    throw new InvalidOperationException("Entrance acceptance attempted twice.");
                e.AcceptanceAttempted = true; // latch BEFORE external money side effects
                e.State = State.Accepted;
                e.Accepted = _services.Accept(e.Guest);
                if (e.Accepted == false)
                {
                    e.State = State.Rejected;
                    if (e.Serial.HasValue) _visitors.MarkNativeDeparture(e.Guest, _owner);
                    if (!e.RejectionNotified)
                    {
                        e.RejectionNotified = true;
                        Trace(e.Serial.HasValue ? "rejected; native departure requested, lease retained"
                            : "rejected; missing activation serial boundary, lease retained", e);
                        _services.Reject(e.Guest);
                    }
                }
                else Trace("accepted once; awaiting exit goal", e);
                break;
            case State.Accepted:
                bool found = false;
                // 2110D8 loops to the NEXT candidate on failed allocation in this SAME update.
                foreach (var goal in _services.ExitCandidates(e.Guest))
                {
                    found = true;
                    if (AssignDirect(e, 13, goal, () => goal)) break;
                }
                if (!found) Trace("no exit goal; acceptance not repeated", e);
                break;
            case State.Rejected:
                if (!e.Serial.HasValue) break; // compatibility boundary logged once at rejection
                if ((e.Serial.Value & 63u) != (_tick & 63u))
                {
                    e.DeferredSticky = true;
                    Trace("mode14 phase deferred; A4 sticky", e);
                }
                else if (!Request(e, 14, _services.StagingTarget(e.Guest), 0x21))
                {
                    e.DeferredSticky = true;
                    Trace("mode14 request refused; A4 sticky", e);
                }
                break;
            case State.RequestBus:
                int index = _services.Random(1); // native consumption even with only one point
                if (index != 0)
                    throw new InvalidOperationException("Bounded RNG(1) returned an out-of-range value.");
                var busPoint = _services.BusPoint
                    ?? throw new InvalidOperationException("Departure requires a bus-point supplier.");
                Request(e, 9, busPoint(e.Guest, index), 1);
                break;
            // Pending with no token is legitimate after an alternate refusal.
            // Ordinary decision/failed-recovery are explicit held boundaries.
            case State.Pending:
            case State.Staged:
            case State.Waiting:
            case State.DecisionBoundary:
            case State.RecoveryBoundary:
                break;
            default:
                throw new InvalidOperationException("Unimplemented entrance state.");
        }
    }

    void Complete(Entry e, Point position)
    {
        switch (e.Mode)
        {
            case 14:
                e.OutgoingDirection = true;
                e.State = State.Staged;
                e.StageCounted = true;
                StagingPending++;
                _visitors.Walk.SetNativeFacing(e.Guest, _owner, 2); // 20DC6C writes pi after mode14
                Trace("mode14 staged outgoing; faces pi", e);
                break;
            case 15:
                e.Group = 0;
                e.State = State.Staged;
                e.StageCounted = true;
                StagingPending++;
                break;
            case 16:
                if (!e.StageCounted)
                    throw new InvalidOperationException("Mode16 completed without this flow's staging count.");
                if (e.OutgoingDirection)
                {
                    e.OutgoingDirection = false;
                    e.State = State.RequestBus;
                }
                else
                {
                    int group = _services.Random(2);
                    if (group is < 0 or > 1)
                        throw new InvalidOperationException("Bounded RNG(2) returned an out-of-range value.");
                    if (_incoming[group].Count >= 11) group = 1 - group; // ONE flip, not a capacity veto
                    e.Group = group;
                    e.State = State.RequestQueue;
                }
                e.StageCounted = false;
                StagingPending--;
                EpisodeProcessed++; // not event9, slot allocation, or registration
                break;
            case 11:
                e.State = position == Register(e) ? State.Waiting : State.RequestQueue;
                break;
            case 12:
                e.State = position == QueuePoint(e) ? State.Waiting : State.Reposition;
                break;
            case 9:
                _visitors.CompleteNativeDeparture(e.Guest, _owner);
                Trace("mode9 completed native departure; same identity retired", e);
                Forget(e);
                return;
            case 13:
                if (!_visitors.ReleaseEntranceRoute(e.Guest, _owner))
                    throw new InvalidOperationException("Completed entrance exit lease could not be released.");
                Trace("mode13 released to ordinary visitor", e);
                Forget(e);
                return;
            default:
                throw new InvalidOperationException("Unsupported entrance movement purpose.");
        }
        Trace("route completion dispatched", e); // snapshot retains the completed purpose for diagnostics
        e.Mode = 2; // 20DD20 resets the movement-purpose field after terminal dispatch
    }

    void Forget(Entry e)
    {
        if (e.QueueNode != null) _incoming[e.Group].Remove(e.QueueNode);
        if (e.ActiveNode != null) _active.Remove(e.ActiveNode);
        if (e.AllocatedNode != null) _allocated.Remove(e.AllocatedNode);
        e.QueueNode = e.ActiveNode = e.AllocatedNode = null;
        e.Token = null; // logical cancellation: late results no longer match
        if (e.StageCounted)
        {
            e.StageCounted = false;
            StagingPending--;
        }
        _entries.Remove(e.Guest);
    }

    /// <summary>
    /// Explicit managed teardown policy, NOT traced native cleanup. cancel must
    /// cancel/relinquish external request resources (or explicitly choose to leave
    /// them to Pump's stale-result disposal). resolveLease must release through
    /// ParkVisitors or remove the identity from Walk as part of parent teardown;
    /// fractional/unfinished routes cannot be safely handed to ordinary walking.
    /// A live retained lease is an error, not a silent orphan. On callback failure,
    /// unprocessed entries remain owned and Clear can be retried. No token is reused.
    /// Successful Clear drops all flow references and resets P/R, but shared E belongs
    /// to the caller and must be reset there if appropriate. External vanished-guest
    /// cleanup in Tick is a SAFETY policy: unlink, logical cancellation, own P decrement;
    /// native indirect removal callbacks and physical service cancellation are unknown.
    /// </summary>
    public void Clear(Action<ulong, Guest> cancel, Action<Guest, object> resolveLease)
    {
        ArgumentNullException.ThrowIfNull(cancel);
        ArgumentNullException.ThrowIfNull(resolveLease);
        Enter();
        try
        {
            for (var node = _allocated.First; node != null;)
            {
                var next = node.Next;
                var e = node.Value;
                // Latch before either policy callback; a partial cancellation or
                // lease-resolution failure must not resume normal ticking.
                e.Stopped = true;
                e.Failure = "Clear requires parent cancellation/lease resolution.";
                if (e.Token is { } token)
                {
                    cancel(token, e.Guest);
                    e.Token = null;
                }
                resolveLease(e.Guest, _owner);
                if (_visitors.Walk.NativeRouteState(e.Guest, _owner) != null)
                    throw new InvalidOperationException("Clear policy left a live entrance lease owned.");
                Forget(e);
                node = next;
            }
            StagingPending = 0;
            EpisodeProcessed = 0;
            DeparturePressure = 0;
        }
        finally { _busy = false; }
    }
}
