using System.Numerics;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

/// <summary>Caller-owned native inputs; no guessed speed/delta/readiness defaults.</summary>
public sealed record NativeMotionInputs(Func<sbyte> Speed, Func<int> Delta, Func<bool> AnimationReady)
{
    /// <summary>False for a controller which orders its own group/active passes.
    /// Its owner must call StepOwnedNative once in the appropriate pass.</summary>
    public bool AutomaticStep { get; init; } = true;
    /// <summary>Called when the cursor takes 191D78's advance (a present slot at state 2).
    /// Natively that call also requests logical 13 on the guest (191D78 with a1=0 from 20D628).
    /// Optional and observational: it must not step, assign or release the route.</summary>
    public Action SlotAdvanced { get; init; }
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
    /// <summary>One real output-slot pool shared by this park's native walkers and route service.
    /// Search-node/request resources remain distinct and are not supplied by this property.</summary>
    public NativeRoutePool NativeRoutes { get; } = new();
    public enum NativeAssignment { Assigned, Refused, Exhausted }

    bool MayAssignNative(Guest guest, object owner, NativeMotionInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputs.Speed);
        ArgumentNullException.ThrowIfNull(inputs.Delta);
        ArgumentNullException.ThrowIfNull(inputs.AnimationReady);
        return UniqueLive(guest) && (guest.NativeMotion is { } old
            ? ReferenceEquals(old.Owner, owner) : guest.Next == null);
    }

    Point SourcePosition(Guest guest) => guest.NativeMotion?.Route.Position ?? new Point(
        checked((short)(guest.Cell.X * 256 + 128)), checked((short)(guest.Cell.Z * 256 + 128)));

    void InstallNative(Guest guest, object owner, NativeMotionInputs inputs, Point start, int head, Point? last)
    {
        var route = new NativeGuestRoute(start, NativeRoutes, head,
            guest.NativeMotion?.Route.FacingQuarterTurns ?? 0); // route assignment does not reset native facing

        guest.NativeMotion = new NativeWalkLease(owner, route, inputs);
        guest.Route = null; guest.RouteIndex = head; guest.Progress = 0; guest.Reason = null;
        guest.State = GuestState.Walking;
        guest.Next = route.CurrentTarget is { } target ? NativeCell(target) : null;
        guest.Destination = last is {} destination ? NativeCell(
            NativeGuestMotion.DecodeTarget(NativeGuestMotion.EncodeTarget(0, destination))) : NativeCell(start);
    }

    /// <summary>Compatibility wrapper. Refused ownership leaves everything unchanged.
    /// Exhaustion is different: native output replacement discards the old chain FIRST,
    /// retains an empty owner lease at the same coordinate, and returns false to retry.</summary>
    public bool BeginNativeRoute(Guest guest, object owner, IReadOnlyList<Point> waypoints, NativeMotionInputs inputs)
        => AssignNativeRoute(guest, owner, waypoints, inputs) == NativeAssignment.Assigned;

    public NativeAssignment AssignNativeRoute(Guest guest, object owner,
        IReadOnlyList<Point> waypoints, NativeMotionInputs inputs)
    {
        if (!MayAssignNative(guest, owner, inputs)) return NativeAssignment.Refused;
        ArgumentNullException.ThrowIfNull(waypoints);
        var copy = waypoints.ToArray(); // managed input validation before disposing old resource ownership
        var start = SourcePosition(guest);
        guest.NativeMotion?.Route.Dispose(); // 18D3D8: old route is gone even if replacement allocation fails
        InstallNative(guest, owner, inputs, start, -1, null);
        if (!NativeRoutes.TryBuild(copy, out int head))
        {
            guest.Reason = "native output-slot pool exhausted; owner must retry";
            return NativeAssignment.Exhausted;
        }
        InstallNative(guest, owner, inputs, start, head, copy.Length == 0 ? null : copy[^1]);
        return NativeAssignment.Assigned;
    }

    /// <summary>211160 direct assignment: free old, allocate, THEN evaluate target.
    /// In particular mode16's discarded random-X helper must not run on exhaustion.</summary>
    public NativeAssignment AssignDirectNativeRoute(Guest guest, object owner,
        Func<Point> target, NativeMotionInputs inputs)
    {
        if (!MayAssignNative(guest, owner, inputs)) return NativeAssignment.Refused;
        ArgumentNullException.ThrowIfNull(target);
        var start = SourcePosition(guest);
        guest.NativeMotion?.Route.Dispose();
        InstallNative(guest, owner, inputs, start, -1, null);
        int head = NativeRoutes.Allocate();
        if (head == -1)
        {
            guest.Reason = "native direct-slot allocation exhausted; owner must retry";
            return NativeAssignment.Exhausted;
        }
        try
        {
            Point destination = target();
            NativeRoutes.SetTarget(head, destination);
            InstallNative(guest, owner, inputs, start, head, destination);
            return NativeAssignment.Assigned;
        }
        catch { NativeRoutes.FreeOne(head); throw; } // managed exception safety, not a native callback branch
    }

    /// <summary>Explicit native heading write, e.g. mode14 completion20DC6C sets pi.</summary>
    public void SetNativeFacing(Guest guest, object owner, int quarterTurns)
    {
        if (NativeRouteState(guest, owner) == null)
            throw new InvalidOperationException("Facing write requires the current route owner.");
        guest.NativeMotion.Route.SetFacing(quarterTurns);
    }

    public NativeMotionSnapshot? NativeRouteState(Guest guest, object owner)
    {
        if (!IsLive(guest) || guest.NativeMotion is not { } lease
            || !ReferenceEquals(lease.Owner, owner)) return null;
        var r = lease.Route;
        return new(r.Position, r.ExecutionState, r.SlotIndex, r.Finished, r.Failed);
    }

    /// <summary>Hand back only a completed, exact cell-center pose. The original accepted
    /// entrance exit leg targets a cell center. Other fractional handoffs must retain their
    /// owner or use a separately specified recovery policy, never silently teleport.</summary>
    public bool ReleaseNativeRoute(Guest guest, object owner)
    {
        // A lease holding NO slot is standing still (the cursor only moves toward a slot's target), so
        // an empty lease that was never stepped is as safe to hand back as a finished one. Queue item 8
        // needs that: a departure whose route request failed never assigned a route at all.
        if (NativeRouteState(guest, owner) is not { Failed: false } state
            || !state.Finished && state.SlotIndex >= 0
            || (state.Position.X & 255) != 128 || (state.Position.Z & 255) != 128)
            return false;
        guest.Cell = NativeCell(state.Position);
        guest.NativeMotion.Route.Dispose();
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
        bool advancing = !cursor.Finished && !cursor.Failed && cursor.ExecutionState == 2 && cursor.SlotIndex >= 0;
        cursor.Step(speed, delta, Paths.Field.Width, Paths.Field.Height, ready);
        if (advancing) lease.Inputs.SlotAdvanced?.Invoke();
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