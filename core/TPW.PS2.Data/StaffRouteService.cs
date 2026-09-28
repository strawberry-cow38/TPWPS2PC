using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

/// <summary>⭐⭐ THE STAFF'S ROUTE REQUESTS, IN THE NATIVE SHAPE: `0x18DA78` admits, `0x18D7F8`
/// pumps, the planner notifies event 1 (built) or 2 (failed) to the owner's `vt+0x16C`
/// (findings/staff-person.md §4.3-§4.5, native-route-planner.md).
///
/// The shape, READ:
/// - `0x18DA78(C, x, z, tx, tz, flags, 0)` → `0x18C698` takes a request RECORD (10 of them), keeps
///   the start cell `x&gt;&gt;8, z&gt;&gt;8` and goal cell `tx&gt;&gt;8, tz&gt;&gt;8` (clamped to the map), the EXACT
///   target at `+0x1C/+0x20`, flags at `+0x2C`, priority `param_7` at `+0x34`. No free record →
///   returns 0, and the caller decides what that means (the patrol falls to the wander, the rest
///   and the strike walk retry next tick).
/// - Admission pushes at the HEAD of the active list; the pump, called in `0x151800` BEFORE the
///   object loop `0x14BE60`, services the HEAD -- the NEWEST request -- and continues only through
///   priority-1 records. Every staff request passes priority 0, so **one request a tick, newest
///   first**.
/// - A request made during tick N's object loop is serviced in tick N+1's pump, whose notify sets
///   state 3, and the first step happens later in that same tick: the 0xB wait is one tick at best.
/// - Success: the builder `0x18D358` FREES THE OWNER'S OLD ROUTE FIRST, emits one output slot per
///   direction change into the SHARED pool (`0x3AE1B8`, <see cref="GuestWalk.NativeRoutes"/>) with
///   the exact endpoint first, writes the head to `C+0x28`, then notifies 1. A builder allocation
///   failure frees its partial list and notifies 2. An unreachable goal notifies 2.
///
/// ⚠ ADAPTERS, each standing in for something the port's tiles or search cannot express:
/// - ⚠ THE SEARCH is <see cref="ParkPaths.RouteOver"/>, the port's BFS, over the passable set
///   <see cref="NativeTileView.Admits"/> derives from the request flags. Natively it is A* with
///   costs (open ground 2), link-byte direction checks and a random per-expansion direction order
///   (native-route-planner.md). Route LENGTH on uniform costs agrees; which of several equal
///   routes is taken does not, and flags 0x03/0x23 routes over grass may be shorter here than the
///   cost-2 native ones.
/// - ⚠ THE START CELL is always expanded, whatever its kind (the native start node is expanded
///   before any passability test); the GOAL is admitted by the per-kind rule with `target = true`.
/// - ⚠ ONE CALL FINISHES A SEARCH. The native A* runs about 99 node expansions a call and can span
///   frames; a BFS finishes at once, so a serviced request always answers in the same pump.
/// - ⚠ THE 10 RECORDS ARE THE STAFF'S ALONE. Natively they are shared with every guest request and
///   the pump can be busy with a guest's; the port's guests route through their own services, so
///   staff here see all ten and are never starved by guests.
/// - ⚠ A FIRED OWNER'S REQUEST IS CANCELLED (<see cref="Cancel"/>). Natively nothing cancels it
///   (`0x18DB70` has no caller), the late result writes a route head into the freed slot and leaks
///   those output slots until the next route reset (findings/staff-person.md §2.6). A deliberate
///   difference: the pool here is shared with the guests, and a leak would starve them.</summary>
public sealed partial class StaffRouteService
{
    /// <summary>`0x18C698`'s request-record pool size (shared with guests natively).</summary>
    public const int Records = 10;

    sealed record Request(StaffMember Owner, ParkCell From, ParkCell Goal, Point Target, int Flags);

    readonly LinkedList<Request> _active = new();
    readonly ParkPaths _paths;
    readonly NativeTileView _tiles;
    readonly NativeRoutePool _pool;

    public StaffRouteService(ParkPaths paths, NativeTileView tiles, NativeRoutePool pool)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    }

    /// <summary>Records currently admitted and not yet serviced.</summary>
    public int Pending => _active.Count;
    /// <summary>Instrumentation: requests admitted, refused for want of a record, and answered.</summary>
    public int Admitted { get; private set; }
    public int Refused { get; private set; }
    public int Built { get; private set; }
    public int Unreachable { get; private set; }
    public int OutputExhausted { get; private set; }

    /// <summary>The owner whose request the next <see cref="Pump"/> will service, or null.</summary>
    public StaffMember Next => _active.First?.Value.Owner;

    /// <summary>`0x18DA78(owner, from, target, flags, 0)`. False = no free record (native 0).</summary>
    public bool Submit(StaffMember owner, Point from, Point target, int flags)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_active.Count >= Records) { Refused++; return false; }
        // 0x18C698: cells from the 1/256 coordinates, clamped to the map (with debug prints natively).
        var start = Clamp(new ParkCell(from.X >> 8, from.Z >> 8));
        var goal = Clamp(new ParkCell(target.X >> 8, target.Z >> 8));
        _active.AddFirst(new Request(owner, start, goal, target, flags));   // newest at the head
        Admitted++;
        return true;
    }

    ParkCell Clamp(ParkCell c) => new(Math.Clamp(c.X, 0, _tiles.Width - 1), Math.Clamp(c.Z, 0, _tiles.Height - 1));

    /// <summary>`0x18D7F8`: service the newest request, build its output, notify its owner.</summary>
    public void Pump()
    {
        if (_active.First is not { } node) return;
        var request = node.Value;
        _active.RemoveFirst();
        var cells = _paths.RouteOver(request.From, request.Goal,
            c => c == request.From || _tiles.Admits(c, request.Flags, c == request.Goal));
        if (cells == null)
        {
            Unreachable++;
            request.Owner.RouteEvent(2);                                   // 18D160: open list empty
            return;
        }
        // 18D358: the old route goes first, then the output list is built; a failure frees the
        // partial list (18D488 -> 18D574 -> 192840) and notifies 2.
        request.Owner.FreeRoute();
        var waypoints = NativeRouteOutput.FromCells(cells, request.Target);
        if (!_pool.TryBuild(waypoints, out int head))
        {
            OutputExhausted++;
            request.Owner.RouteEvent(2);
            return;
        }
        request.Owner.AdoptRoute(head);
        Built++;
        request.Owner.RouteEvent(1);
    }

    /// <summary>⚠ Port-only: drop an owner's pending requests (see the class note on firing).</summary>
    internal void Cancel(StaffMember owner)
    {
        for (var n = _active.First; n != null;)
        {
            var next = n.Next;
            if (ReferenceEquals(n.Value.Owner, owner)) _active.Remove(n);
            n = next;
        }
    }

    /// <summary>⚠ Port-only: forget every pending request (a route-system reset).</summary>
    internal void Clear() => _active.Clear();
}
