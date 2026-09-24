using Godot;
using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;
using Flow = TPW.PS2.Data.NativeEntranceFlow;

namespace TPWPS2Viewer;

public partial class Viewer
{
    // OPT-IN RESEARCH ADAPTER. Never silently replace the documented main behavior:
    // the route finder, pool/readiness and departure producer are not fully ported.
    bool _experimentalEntrance;
    Flow _entranceFlow;
    GuestWalk _entranceWalk;
    ParkVisitors _entranceVisitors;
    Action<uint> _entrancePriorTick, _entranceTickHook;
    readonly Queue<Flow.RouteResult> _entranceResults = new();
    int _entranceAccepted, _entranceRejected;
    // Ordinary variants use constructor100470's 150 seed. Saved scenario/UI fee
    // overrides are not joined. A fixture can explicitly replace this fee, never
    // silently infer one from shop price or park balance.
    int _experimentalEntranceFee = 150;

    static Point EntranceCentre(ParkCell c) => new(checked((short)(c.X * 256 + 128)),
        checked((short)(c.Z * 256 + 128)));

    void EnsureExperimentalEntrance()
    {
        _experimentalEntrance |= OS.GetCmdlineUserArgs().Contains("--experimental-native-entrance");
        if (!_experimentalEntrance || _entranceFlow != null || _visitors == null || !EnsureNativeBus()) return;
        _entranceWalk = _guests;
        _entranceVisitors = _visitors;
        Point stage = EntranceCentre(_busCatalogue.StagingPoint);
        Point queue = EntranceCentre(_busCatalogue.IncomingQueuePoint);
        _entranceFlow = new Flow(_visitors, new Flow.Services(
            n => _guestRng.Next(n),
            _ => new Point(unchecked((short)(stage.X + _guestRng.Next(256))), stage.Z),
            queue, RequestEntranceRoute, PumpEntranceRoutes,
            _ => true, // explicit UNBOUNDED direct-slot adapter, not native pool capacity
            _ => true, // explicit model-readiness bypass, not a decoded ready-state join
            () => 0x4000,
            g => NativeEntranceAcceptance.TryCharge(_entranceVisitors.Needs, g.Id, _sim.Finances,
                () => _experimentalEntranceFee, EntranceValueSum, n => _guestRng.Next(n),
                () => _entranceAccepted++),
            ExperimentalEntranceExit,
            g => {
                _entranceRejected++;
                GD.Print($"[entrance.experimental] guest {g.Id} rejected: HELD under owner; native departure serial/producer unported");
            },
            _guests.StepOwnedNative));
        _entrancePriorTick = _guests.BeforeStep;
        _entranceTickHook = tick => {
            _entrancePriorTick?.Invoke(tick);
            _busTraffic = _entranceFlow.Tick(tick, _nativeBus.Controller.State, _busTraffic);
        };
        _guests.BeforeStep = _entranceTickHook;
        GD.Print($"[entrance.experimental] OPT-IN controller: actual bus identities -> two incoming groups -> fee -> normal handoff. point1={_busCatalogue.StagingPoint} point2={_busCatalogue.IncomingQueuePoint}");
        GD.Print("[entrance.experimental] NON-PARITY ADAPTERS: deferred-next-tick public BFS, unlimited route slots, readiness bypass; ordinary constructor fee seed only; second staging class absent; rejects held, departure-pressure still bypassed. Not release-ready.");
    }

    bool RequestEntranceRoute(ulong token, Guest guest, int mode, Point from, Point target)
    {
        var quantized = NativeGuestMotion.DecodeTarget(NativeGuestMotion.EncodeTarget(0, target));
        var sourceCell = new ParkCell(from.X >> 8, from.Z >> 8);
        var targetCell = new ParkCell(quantized.X >> 8, quantized.Z >> 8);
        var cells = _entranceWalk.Route(sourceCell, targetCell);
        // This adapter submits now and delivers no earlier than the NEXT route pump.
        // BFS/resource/time budget is port policy; it is not the native18D7F8 planner.
        var points = cells == null ? null : cells.Skip(1).Select(EntranceCentre).ToList();
        if (points != null)
        {
            if (points.Count == 0) points.Add(quantized);
            else points[^1] = quantized;
        }
        _entranceResults.Enqueue(new(token, guest, points?.ToArray(),
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

    Point? ExperimentalEntranceExit(Guest guest)
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
            if (!_entranceWalk.Paths.Contains(c)) return null; // explicit managed bounds safety
            // PathTool's real ground sprites represent kinds2/13. Exclude the
            // bridge policy and phantom entrance corridor from this goal-kind join.
            if (!_entranceWalk.Paths.IsBridge(c) && _entranceWalk.Paths.Kind(c) == ParkPathKind.Path)
                return EntranceCentre(c);
        }
        return null;
    }

    void ResetExperimentalEntrance()
    {
        if (_entranceWalk != null && _entranceWalk.BeforeStep == _entranceTickHook)
            _entranceWalk.BeforeStep = _entrancePriorTick;
        _entranceFlow?.Clear((_, _) => { }, (g, owner) => _entranceVisitors.DiscardEntranceGuest(g, owner));
        _entranceResults.Clear();
        _entranceFlow = null; _entranceWalk = null; _entranceVisitors = null;
        _entrancePriorTick = _entranceTickHook = null;
        _entranceAccepted = _entranceRejected = 0;
    }
}
