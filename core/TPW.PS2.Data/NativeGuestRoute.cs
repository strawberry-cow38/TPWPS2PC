using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

/// <summary>
/// Managed per-route cursor for the observed guest execution-state timeline in
/// findings/native-guest-motion.md (191E98/191D78/20D628), not a native pathfinder
/// or search-resource model. Actual output slots are owned in a NativeRoutePool.
/// Route queries, readiness policy (including its bypass), cancellation and
/// movement-purpose dispatch remain caller-owned. Supply native delta and the
/// actual signed active speed; no seconds conversion or unused movement carry.
/// </summary>
public sealed class NativeGuestRoute : IDisposable
{
    public enum Outcome { Pending, Completed, Failed, Idle }

    public NativeRoutePool Pool { get; }
    readonly ulong generation;
    public bool Disposed { get; private set; }

    /// <summary>
    /// Copies and slot-codec-quantizes every waypoint. The current position is
    /// retained exactly: guest coordinates, unlike route targets, need not be
    /// quarter-cell aligned. Empty routes still traverse the two no-slot phases.
    /// </summary>
    public NativeGuestRoute(Point start, IReadOnlyList<Point> waypoints)
        : this(start, new NativeRoutePool(), -1)
    {
        if (!Pool.TryBuild(waypoints, out int head))
            throw new InvalidOperationException("Isolated route exceeds native output-slot capacity.");
        SlotIndex = head;
    }

    /// <summary>Adopt an exclusively owned prepared chain. Actual GuestWalk routes all
    /// use its shared pool; this constructor never allocates or duplicates that chain.</summary>
    internal NativeGuestRoute(Point start, NativeRoutePool pool, int head)
    {
        Pool = pool ?? throw new ArgumentNullException(nameof(pool));
        if (head != -1 && !pool.IsAllocated(head))
            throw new InvalidOperationException("Cannot adopt an unallocated route head.");
        generation = pool.ResetGeneration;
        Position = start;
        SlotIndex = head;
    }

    void ValidateEpoch()
    {
        if (SlotIndex >= 0 && generation != Pool.ResetGeneration)
            throw new InvalidOperationException("Managed safety: pool reset while a route still owned slots.");
    }

    public void Dispose()
    {
        if (Disposed) return;
        ValidateEpoch();
        Pool.FreeChain(SlotIndex);
        SlotIndex = -1;
        Disposed = true;
    }

    public Point Position { get; private set; }
    public Point? CurrentTarget
    {
        get { ValidateEpoch(); return SlotIndex < 0 ? null : Pool.Target(SlotIndex); }
    }
    /// <summary>3 walks; 2 releases/dispatches. Bounds failure sets native state 0.</summary>
    public int ExecutionState { get; private set; } = 3;
    /// <summary>Actual shared output-pool handle; -1 means no slot.</summary>
    public int SlotIndex { get; private set; }
    public bool Finished { get; private set; }
    public bool Failed { get; private set; }
    /// <summary>Last selected facing; defaults to 0 until the first ready slot step.</summary>
    public int FacingQuarterTurns { get; private set; }

    /// <summary>
    /// Performs one guest update phase. Arrival keeps the slot until a later call;
    /// release never moves. Terminal no-slot walking precedes completion dispatch.
    /// Completed and Failed are emitted once, followed only by Idle.
    /// </summary>
    public Outcome Step(sbyte speed, int delta, int columns, int rows, bool animationReady)
    {
        if (Disposed || Finished || Failed)
            return Outcome.Idle;
        ValidateEpoch();

        if (ExecutionState == 2)
        {
            if (SlotIndex < 0)
            {
                Finished = true;
                return Outcome.Completed;
            }
            int next = Pool.Next(SlotIndex); // 191D78 saves successor BEFORE free-one
            Pool.FreeOne(SlotIndex);
            SlotIndex = next;
            ExecutionState = 3;
            return Outcome.Pending;
        }

        // Native no-slot handling precedes the animation-readiness gate.
        if (SlotIndex < 0)
        {
            ExecutionState = 2;
            return Outcome.Pending;
        }
        if (!animationReady)
            return Outcome.Pending;

        Point target = Pool.Target(SlotIndex);
        int dx = target.X - Position.X, dz = target.Z - Position.Z;
        FacingQuarterTurns = dx < 0 ? 3 : dx > 0 ? 1 : dz > 0 ? 0 : 2;
        var moved = NativeGuestMotion.AdvanceCoordinates(Position, target, speed, delta, columns, rows);
        if (moved.Outcome == NativeGuestMotion.Outcome.OutsideGrid)
        {
            Pool.FreeChain(SlotIndex); // 192038 invalid-position path disposes the WHOLE remainder
            SlotIndex = -1;
            ExecutionState = 0;
            Failed = true;
            return Outcome.Failed;
        }
        Position = moved.Position;
        if (moved.Outcome == NativeGuestMotion.Outcome.AtWaypoint)
            ExecutionState = 2;
        return Outcome.Pending;
    }
}
