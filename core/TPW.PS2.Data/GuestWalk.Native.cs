using System.Numerics;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

/// <summary>Caller-owned native inputs; no guessed speed/delta/readiness defaults.</summary>
public sealed record NativeMotionInputs(Func<sbyte> Speed, Func<int> Delta, Func<bool> AnimationReady)
{
    /// <summary>False for a controller which orders its own group/active passes.
    /// Its owner must call StepOwnedNative once in the appropriate pass.</summary>
    public bool AutomaticStep { get; init; } = true;
}

/// <summary>A read-only observation, not access to the live cursor's Step method.</summary>
public readonly record struct NativeMotionSnapshot(Point Position, int ExecutionState,
    int SlotIndex, bool Finished, bool Failed);

internal sealed record NativeWalkLease(object Owner, NativeGuestRoute Route, NativeMotionInputs Inputs)
{
    internal Vector3 Position => new(Route.Position.X / 256f, 0, Route.Position.Z / 256f);
    internal Vector3 Facing => Route.FacingQuarterTurns switch
    { 1 => Vector3.UnitX, 2 => -Vector3.UnitZ, 3 => -Vector3.UnitX, _ => Vector3.UnitZ };
}

public sealed partial class GuestWalk
{
    /// <summary>Take an existing live identity under an explicit native-route owner.
    /// Waypoints are supplied by the caller's route service, NOT generated here by a
    /// claimed native pathfinder. A different owner or a legacy mid-edge handoff refuses
    /// without changing anything. An existing owner may replace its own route, retaining
    /// the precise current coordinate rather than snapping back to a cell center.</summary>
    public bool BeginNativeRoute(Guest guest, object owner, IReadOnlyList<Point> waypoints,
        NativeMotionInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputs.Speed);
        ArgumentNullException.ThrowIfNull(inputs.Delta);
        ArgumentNullException.ThrowIfNull(inputs.AnimationReady);
        if (guest == null || !_guests.Contains(guest) || _guests.Count(g => g.Id == guest.Id) != 1)
            return false;
        var old = guest.NativeMotion;
        if (old != null ? !ReferenceEquals(old.Owner, owner) : guest.Next != null)
            return false;
        var start = old?.Route.Position ?? new Point(
            checked((short)(guest.Cell.X * 256 + 128)), checked((short)(guest.Cell.Z * 256 + 128)));
        var route = new NativeGuestRoute(start, waypoints); // validate/copy before changing ownership
        guest.NativeMotion = new NativeWalkLease(owner, route, inputs);
        guest.Route = null; guest.RouteIndex = 0; guest.Progress = 0; guest.Reason = null;
        guest.State = GuestState.Walking;
        guest.Next = route.CurrentTarget is { } target ? NativeCell(target) : null;
        guest.Destination = waypoints.Count == 0 ? guest.Cell : NativeCell(
            NativeGuestMotion.DecodeTarget(NativeGuestMotion.EncodeTarget(0, waypoints[^1])));
        return true;
    }

    public NativeMotionSnapshot? NativeRouteState(Guest guest, object owner)
    {
        if (guest == null || !_guests.Contains(guest) || guest.NativeMotion is not { } lease
            || !ReferenceEquals(lease.Owner, owner)) return null;
        var r = lease.Route;
        return new(r.Position, r.ExecutionState, r.SlotIndex, r.Finished, r.Failed);
    }

    /// <summary>Hand back only a completed, exact cell-center pose. The original accepted
    /// entrance exit leg targets a cell center. Other fractional handoffs must retain their
    /// owner or use a separately specified recovery policy, never silently teleport.</summary>
    public bool ReleaseNativeRoute(Guest guest, object owner)
    {
        if (NativeRouteState(guest, owner) is not { Finished: true, Failed: false } state
            || (state.Position.X & 255) != 128 || (state.Position.Z & 255) != 128)
            return false;
        guest.Cell = NativeCell(state.Position);
        guest.NativeMotion = null; guest.Next = null; guest.Progress = 0;
        guest.Route = null; guest.RouteIndex = 0; guest.Destination = guest.Cell;
        guest.State = GuestState.Arrived; guest.Reason = null;
        return true;
    }

    /// <summary>Explicit controller step, never permission for a different owner to drive
    /// the cursor. Ordinary automatic leases cannot also be stepped through this API.</summary>
    public void StepOwnedNative(Guest guest, object owner)
    {
        if (NativeRouteState(guest, owner) == null || guest.NativeMotion.Inputs.AutomaticStep)
            throw new InvalidOperationException("Explicit step requires this owner's manual native lease.");
        StepNative(guest);
    }

    static ParkCell NativeCell(Point p) => new(p.X >> 8, p.Z >> 8);

    void StepNative(Guest guest)
    {
        var lease = guest.NativeMotion;
        var cursor = lease.Route;
        bool ready = false; sbyte speed = 0; int delta = 0;
        // The native no-slot/state2 paths bypass animation readiness and speed/delta reads.
        if (!cursor.Finished && !cursor.Failed && cursor.ExecutionState == 3 && cursor.SlotIndex >= 0)
        {
            ready = lease.Inputs.AnimationReady();
            if (ready) { speed = lease.Inputs.Speed(); delta = lease.Inputs.Delta(); }
        }
        cursor.Step(speed, delta, Paths.Field.Width, Paths.Field.Height, ready);
        var cell = NativeCell(cursor.Position);
        guest.Steps += Math.Abs(cell.X - guest.Cell.X) + Math.Abs(cell.Z - guest.Cell.Z);
        guest.Cell = cell; guest.Progress = 0; guest.RouteIndex = cursor.SlotIndex;
        guest.Next = cursor.CurrentTarget is { } target ? NativeCell(target) : null;
        guest.State = cursor.Failed ? GuestState.NoRoute : cursor.Finished ? GuestState.Arrived : GuestState.Walking;
        guest.Reason = cursor.Failed ? "native route candidate outside grid; owner must resolve" : null;
        // Completion does NOT relinquish the lease. The entrance controller must perform its
        // movement-purpose transition before any ordinary destination picker can take over.
    }
}