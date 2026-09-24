using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

/// <summary>
/// Managed per-route cursor for the observed guest execution-state timeline in
/// findings/native-guest-motion.md (191E98/191D78/20D628), not a native pathfinder
/// or a model of the native global slot pool, allocation failure or capacity.
/// Route queries, readiness policy (including its bypass), cancellation and
/// movement-purpose dispatch remain caller-owned. Supply native delta and the
/// actual signed active speed; no seconds conversion or unused movement carry.
/// </summary>
public sealed class NativeGuestRoute
{
    public enum Outcome { Pending, Completed, Failed, Idle }

    private readonly Point[] targets;

    /// <summary>
    /// Copies and slot-codec-quantizes every waypoint. The current position is
    /// retained exactly: guest coordinates, unlike route targets, need not be
    /// quarter-cell aligned. Empty routes still traverse the two no-slot phases.
    /// </summary>
    public NativeGuestRoute(Point start, IReadOnlyList<Point> waypoints)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        targets = new Point[waypoints.Count];
        for (int i = 0; i < targets.Length; i++)
            targets[i] = NativeGuestMotion.DecodeTarget(
                NativeGuestMotion.EncodeTarget(0, waypoints[i]));
        Position = start;
        SlotIndex = targets.Length == 0 ? -1 : 0;
    }

    public Point Position { get; private set; }
    public Point? CurrentTarget => SlotIndex < 0 ? null : targets[SlotIndex];
    /// <summary>3 walks; 2 releases/dispatches. Bounds failure sets native state 0.</summary>
    public int ExecutionState { get; private set; } = 3;
    /// <summary>Local copied-waypoint index, NOT a native pool handle; -1 means no slot.</summary>
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
        if (Finished || Failed)
            return Outcome.Idle;

        if (ExecutionState == 2)
        {
            if (SlotIndex < 0)
            {
                Finished = true;
                return Outcome.Completed;
            }
            SlotIndex = SlotIndex + 1 < targets.Length ? SlotIndex + 1 : -1;
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

        Point target = targets[SlotIndex];
        int dx = target.X - Position.X, dz = target.Z - Position.Z;
        FacingQuarterTurns = dx < 0 ? 3 : dx > 0 ? 1 : dz > 0 ? 0 : 2;
        var moved = NativeGuestMotion.AdvanceCoordinates(Position, target, speed, delta, columns, rows);
        if (moved.Outcome == NativeGuestMotion.Outcome.OutsideGrid)
        {
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
