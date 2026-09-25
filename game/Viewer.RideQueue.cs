using Godot;
using TPW.PS2.Data;
using Queues = TPW.PS2.Data.NativeRideQueues;

namespace TPWPS2Viewer;

/// <summary>
/// OPT-IN research, `--native-ride-queues`: guests stand in a ride's queue the way the console lays
/// them out (findings/native-ride-queue.md). Without it a guest reaching the ride's stub leaves the
/// walk and waits invisibly in an unbounded list; with it the guest walks in along the queue tiles to
/// a quarter-cell spot, waits there on the impatience clock, closes up when the line moves, and boards
/// from the front.
///
/// The queue a ride has is read off the path tool: the compiled entrance connection (+17C) is the
/// head's cell, and the cells run from the ride's stub along the queue links the player drew, to the
/// one cell where the queue meets a path (<see cref="PathTool.Kind.Both"/>), its mouth. A ride whose
/// queue does not reach a path has no native queue and keeps the legacy stub.
///
/// ⚠ PORT ADAPTERS, beyond those NativeRideQueues lists:
/// - the phase (N+14) is the guest id, and the walking speed 15 + (id·7 mod 15) stands in for the
///   activation's 15 + rand(15);
/// - readiness (191E10) gates queue walking only under --native-idle-all, which is what pushes
///   ordinary guests' animation requests. Otherwise the walk is ungated, as ordinary walkers are;
/// - the queue update runs after the entrance flow and the animation push inside the same park tick.
/// </summary>
public partial class Viewer
{
    Queues _rideQueues;
    GuestWalk _rideQueueWalk;
    ParkVisitors _rideQueueVisitors;
    Action<uint> _rideQueuePriorTick, _rideQueueTickHook;
    readonly Dictionary<ParkRide, NativeQueueShape> _queueShapes = new(ReferenceEqualityComparer.Instance);
    int _queueShapesTick = -1;
    bool _nativeRideQueues;
    bool NativeRideQueuesRequested => _nativeRideQueues || ResearchFlags.Value.Contains("--native-ride-queues");

    void EnsureNativeRideQueues()
    {
        if (_rideQueues != null || _visitors == null || _guests == null || _paths == null || !NativeRideQueuesRequested) return;
        _nativeRideQueues = true;
        _rideQueueWalk = _guests;
        _rideQueueVisitors = _visitors;
        _rideQueues = new Queues(_visitors, new Queues.Services
        {
            Shape = RideQueueShape,
            Random = n => _guestRng.Next(n),
            Phase = g => (uint)g.Id,
            Speed = g => checked((sbyte)(15 + g.Id * 7 % 15)),
            AnimationReady = g => !NativeIdleAllActive || NativeEntranceReady(g),
            SlotAdvanced = NativeSlotAdvanced,
        });
        _visitors.NativeQueueMouth = ride => _rideQueues?.Mouth(ride);
        _visitors.NativeQueueArrival = (guest, ride) => _rideQueues != null && _rideQueues.Arrive(guest, ride);
        _rideQueuePriorTick = _guests.BeforeStep;
        _rideQueueTickHook = tick => { _rideQueuePriorTick?.Invoke(tick); _rideQueues?.Tick(tick); };
        _guests.BeforeStep = _rideQueueTickHook;
        GD.Print("[queue.native] OPT-IN native ride queues: head count 7+4*tier (tier 0), quarter-cell spots from the entrance connection, 210428 impatience, staggered close-up, boarding while VAR_ONRIDE < VAR_CAPACITY");
        GD.Print("[queue.native] NON-PARITY ADAPTERS: phase = guest id; speed 15+(id*7%15); queue routes follow the drawn queue cells, not the 0x10/0x11 planner; breakdown empties the queue at once (the ordinary state-4 +94 exception is not modelled); a boarded guest joins the ride's script queue for LETMEON; effect 0x7E and 2E28D0 unported");
    }

    NativeQueueShape RideQueueShape(ParkRide ride)
    {
        if (_queueShapesTick != _parkTicks) { _queueShapes.Clear(); _queueShapesTick = _parkTicks; }
        if (_queueShapes.TryGetValue(ride, out var shape)) return shape;
        return _queueShapes[ride] = BuildQueueShape(ride);
    }

    /// <summary>The queue as the player drew it: the ride's stub, then along its run links, to the cell
    /// where it meets a path. Null when the ride has no compiled entrance connection, or the queue
    /// does not reach a path.</summary>
    NativeQueueShape BuildQueueShape(ParkRide ride)
    {
        if (_paths == null || ride?.Entrance is not { } stub || ride.ServiceEntry != null
            || ride.PlacementTurns is not int turns) return null;
        var door = ShopEntrance.Connection(ride.Definition?.CompiledEntry, ride.Origin, turns, ride.Width, ride.Height, stub);
        if (door == null) return null;
        bool Mine(ParkCell c) => _paths.KindAt(c.X, c.Z) == PathTool.Kind.Queue && _paths.OwnerAt(c.X, c.Z) == ride.Id;
        if (!Mine(stub)) return null;
        var cells = new List<ParkCell> { stub };
        var previous = door.Value;
        var at = stub;
        while (_paths.KindAt(at.X, at.Z) == PathTool.Kind.Queue)
        {
            int links = _paths.LinkBits(at.X, at.Z);
            ParkCell? next = null;
            foreach (var n in ParkPaths.Neighbours(at))
            {
                if (n == previous || (links & PathTool.BitToward(at.X, at.Z, n.X, n.Z)) == 0) continue;
                if (Mine(n) || _paths.KindAt(n.X, n.Z) == PathTool.Kind.Both) { next = n; break; }
            }
            if (next is not { } step || cells.Contains(step) || cells.Count > 512) return null;
            cells.Add(step);
            previous = at;
            at = step;
        }
        // Rotation only matters when the first queue cell is the entrance cell, which a stub outside
        // the door never is, so the port's placement turns are not translated into native numbering.
        return new NativeQueueShape(door.Value, cells, 0);
    }

    void ResetNativeRideQueues()
    {
        if (_rideQueueWalk != null && _rideQueueWalk.BeforeStep == _rideQueueTickHook)
            _rideQueueWalk.BeforeStep = _rideQueuePriorTick;
        if (_rideQueueVisitors != null)
        {
            _rideQueueVisitors.NativeQueueMouth = null;
            _rideQueueVisitors.NativeQueueArrival = null;
        }
        _rideQueues?.Clear(null); // the walk and its visitors are discarded with the park
        _rideQueues = null; _rideQueueWalk = null; _rideQueueVisitors = null;
        _rideQueuePriorTick = _rideQueueTickHook = null;
        _queueShapes.Clear(); _queueShapesTick = -1;
    }
}
