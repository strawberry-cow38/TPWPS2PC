using Godot;
using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;
using Flow = TPW.PS2.Data.NativeEntranceFlow;

namespace TPWPS2Viewer;

public partial class Viewer
{
    // ⭐ THE DEFAULT SINCE 2026-09-25 (strawberry: make tickets part of normal play). Guests off the
    // bus queue at the booths, pay the entry fee into park income or turn back and ride home.
    // `--legacy-entrance` (or _legacyEntrance) restores the old walk-in from the bus stop.
    // ⚠ Not full parity, and the startup log says which adapters stand in: the route finder is
    // this port's BFS, pool/readiness is the documented bypass unless --native-guest-animation,
    // and the fee is the ordinary constructor seed only.
    bool _legacyEntrance;
    NativeActivationSequence _nativeActivations; // process/viewer lifetime; NOT reset on each park
    readonly HashSet<int> _entranceRequestFlags = new();
    // The command line is read ONCE: these are consulted every park tick and, via Gait, every guest
    // every frame (review 2026-09-25). Same argv union as the main parser (Viewer.cs, `argv`).
    static readonly Lazy<HashSet<string>> ResearchFlags = new(() =>
        OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).Where(a => a.StartsWith("--")).ToHashSet());
    bool NativeEntranceOn => !_legacyEntrance && !ResearchFlags.Value.Contains("--legacy-entrance");

    NativeActivationSequence ActivationSequence()
    {
        if (_nativeActivations != null) return _nativeActivations;
        var table = _busCatalogue ?? NativeBusCatalogue.Read(_lib.Disc,
            NativeParkSelection.Ordinary(_lib.WadName, _terrainPath));
        _nativeActivations = new NativeActivationSequence(table.ImageInitialActivationCounter,
            "owner ELF initial counter plus represented port activations ONLY; native startup/restore history unverified");
        GD.Print($"[entrance.serial] phase adapter: seed={_nativeActivations.NextSerial}; {_nativeActivations.Origin}");
        return _nativeActivations;
    }

    void ActivateNativePlacement(Node3D node, RideDefinition definition)
    {
        if (!NativeEntranceOn) return;
        uint serial = ActivationSequence().Activate($"placed:{definition?.CompiledEntry?.Kind}");
        node.SetMeta("represented_activation_serial", (long)serial);
    }
    Flow _entranceFlow;
    GuestWalk _entranceWalk;
    ParkVisitors _entranceVisitors;
    Action<uint> _entrancePriorTick, _entranceTickHook;
    readonly Queue<Flow.RouteResult> _entranceResults = new();
    // Research instrumentation: the last requests with the flow tick they were made on, so a smoke can
    // check 210D70's phase gate. Bounded; never consulted by the flow itself.
    readonly Queue<(Guest Guest, int Mode, int Flags, uint Tick)> _entranceRequests = new();
    uint _entranceTick;
    int _entranceAccepted, _entranceRejected;
    // Ordinary variants use constructor100470's 150 seed. Saved scenario/UI fee
    // overrides are not joined. A fixture can explicitly replace this fee, never
    // silently infer one from shop price or park balance.
    int _entranceFee = 150;

    static Point EntranceCentre(ParkCell c) => new(checked((short)(c.X * 256 + 128)),
        checked((short)(c.Z * 256 + 128)));

    void EnsureNativeEntrance()
    {
        if (!NativeEntranceOn || _entranceFlow != null || _visitors == null || !EnsureNativeBus()) return;
        _entranceWalk = _guests;
        _entranceVisitors = _visitors;
        Point stage = EntranceCentre(_busCatalogue.StagingPoint);
        Point queue = EntranceCentre(_busCatalogue.IncomingQueuePoint);
        _entranceFlow = new Flow(_visitors, new Flow.Services(
            n => _guestRng.Next(n),
            _ => new Point(unchecked((short)(stage.X + _guestRng.Next(256))), stage.Z),
            queue, RequestEntranceRoute, PumpEntranceRoutes,
            _ => true, // no extra admission gate; actual allocation is GuestWalk.NativeRoutes (1000 shared slots)
            NativeEntranceReady, // 191E10 under --native-guest-animation; otherwise the documented bypass
            () => 0x4000,
            g => NativeEntranceAcceptance.TryCharge(_entranceVisitors.Needs, g.Id, _sim.Finances,
                () => _entranceFee, EntranceValueSum, n => _guestRng.Next(n),
                () => _entranceAccepted++),
            EntranceExit,
            g => {
                _entranceRejected++;
                GD.Print($"[entrance] guest {g.Id} rejected: native outgoing journey under represented-activation phase adapter");
            },
            _guests.StepOwnedNative, exitCandidates: EntranceExitCandidates,
            busPoint: (g, index) => index == 0 ? EntranceCentre(_busCatalogue.Point0)
                : throw new InvalidOperationException("Ordinary departure RNG(1) must select point0."),
            requestDetailed: request => {
                _entranceRequestFlags.Add(request.Flags);
                _entranceRequests.Enqueue((request.Guest, request.Mode, request.Flags, _entranceTick));
                while (_entranceRequests.Count > 4096) _entranceRequests.Dequeue();
                // Flags reach the adapter but native 0x21/0x23 search policy is not yet reproduced by BFS.
                return RequestEntranceRoute(request.Token, request.Guest, request.Mode, request.From, request.Target);
            },
            recovery: (g, mode) => GD.Print($"[entrance] guest {g.Id}: mode{mode} native failure hold; the queue-8 adapter resumes it next update"),
            slotAdvanced: NativeSlotAdvanced));

        _entrancePriorTick = _guests.BeforeStep;
        _entranceTickHook = tick => {
            _entrancePriorTick?.Invoke(tick);
            _entranceTick = tick;
            _busTraffic = _entranceFlow.Tick(tick, _nativeBus.Controller.State, _busTraffic);
            TickNativeAnimations();
        };
        _guests.BeforeStep = _entranceTickHook;
        _visitors.NativeDeparture = g => _entranceFlow != null && _entranceFlow.TryDepart(g);
        GD.Print($"[entrance] controller (default; --legacy-entrance for the old walk-in): actual bus identities -> two incoming groups -> fee -> normal handoff. point1={_busCatalogue.StagingPoint} point2={_busCatalogue.IncomingQueuePoint}");
        GD.Print(NativeAnimationActive
            ? "[entrance] native guest animation: 191E10 readiness over the 2AAD48 dispatcher; adapters: one model update per park tick after steps, every owned guest pushed, NewlibRand stream, section 0 drawn as the section-1 walk"
            : "[entrance] readiness BYPASS (pass --native-guest-animation for the dispatcher join)");
        GD.Print("[entrance] NON-PARITY ADAPTERS: deferred-next-tick public BFS/search resources; ordinary constructor fee seed only; guard staging absent; represented-activation phase only; failed-route holds resumed by an adapter (state0: hand back or retry 26; state5: retry the bus leg); ordinary departure enters state26 at once (20C930's later arms unjoined, 211D48 roll unported); guests not admitted by this flow still leave by the legacy gate. Not release-ready.");
    }

    bool RequestEntranceRoute(ulong token, Guest guest, int mode, Point from, Point target)
    {
        var quantized = NativeGuestMotion.DecodeTarget(NativeGuestMotion.EncodeTarget(0, target));
        var sourceCell = new ParkCell(from.X >> 8, from.Z >> 8);
        var targetCell = new ParkCell(quantized.X >> 8, quantized.Z >> 8);
        var cells = _entranceWalk.Route(sourceCell, targetCell);
        // This adapter submits now and delivers no earlier than the NEXT route pump.
        // BFS/resource/time budget is port policy; it is not the native18D7F8 planner.
        var points = cells == null ? null : NativeRouteOutput.FromCells(cells, quantized).ToArray();
        _entranceResults.Enqueue(new(token, guest, points,
            cells == null ? "experimental public BFS found no path" : null));
        return true;
    }

    IEnumerable<Flow.RouteResult> PumpEntranceRoutes()
    {
        var ready = _entranceResults.ToArray();
        _entranceResults.Clear();
        return ready;
    }

    int EntranceValueSum()
    {
        int sum = 0;
        foreach (int kind in new[] {3,6,1,7,4,5,2})
            foreach (var obj in BusObjects().Where(o => o.Kind == kind))
                sum = unchecked(sum + obj.Value);
        return sum;
    }

    Point? EntranceExit(Guest guest)
    {
        foreach (var point in EntranceExitCandidates(guest)) return point;
        return null;
    }

    IEnumerable<Point> EntranceExitCandidates(Guest guest)
    {
        // Narrow reachable-case join: a released incoming HEAD is exactly point2,
        // whose corridor receives native flag8 in14E958..9BC. Do not equate every
        // IsEntrance cell with that native flag byte or allow arbitrary start poses.
        var basePoint = EntranceCentre(_busCatalogue.IncomingQueuePoint);
        if (_entranceFlow.Position(guest) != basePoint)
            throw new InvalidOperationException("Experimental mode13 scan requires the proven incoming-head point2.");
        int x = basePoint.X >> 8, z = basePoint.Z >> 8;
        for (int i = 0; i < 50; i++)
        {
            var c = new ParkCell(x, z + i);
            if (!_entranceWalk.Paths.Contains(c)) yield break; // explicit managed bounds safety
            // PathTool's real ground sprites represent kinds2/13. Exclude the
            // bridge policy and phantom entrance corridor from this goal-kind join.
            if (!_entranceWalk.Paths.IsBridge(c) && _entranceWalk.Paths.Kind(c) == ParkPathKind.Path)
                yield return EntranceCentre(c); // allocation is attempted between candidates, not after choosing one
        }
    }

    void ResetNativeEntrance()
    {
        if (_entranceWalk != null && _entranceWalk.BeforeStep == _entranceTickHook)
            _entranceWalk.BeforeStep = _entrancePriorTick;
        if (_entranceVisitors != null) _entranceVisitors.NativeDeparture = null;
        _entranceFlow?.Clear((_, _) => { }, (g, owner) => _entranceVisitors.DiscardEntranceGuest(g, owner));
        ResetNativeAnimations();
        _entranceResults.Clear();
        _entranceRequestFlags.Clear();
        _entranceRequests.Clear();
        _entranceFlow = null; _entranceWalk = null; _entranceVisitors = null;
        _entrancePriorTick = _entranceTickHook = null;
        _entranceAccepted = _entranceRejected = 0;
    }
}
