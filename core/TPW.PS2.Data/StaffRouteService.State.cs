#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

public sealed partial class StaffRouteService
{
    public const int SnapshotVersion = 1;

    /// <summary>Caller-assigned object identity keys, not Kind/PoolSlot/Serial IDs. Staff
    /// proves the members' resource ownership without invoking any owner/provider code.
    /// Its Paths, Tiles and Routes must be the SAME staged instances used by this service.
    /// Keys must be independently resolved, not copied from an untrusted snapshot.</summary>
    public sealed record SnapshotBindings
    {
        public required string PathsKey { get; init; }
        public required string TilesKey { get; init; }
        public required string PoolKey { get; init; }
        public required ParkStaff Staff { get; init; }
        public required IReadOnlyDictionary<string, StaffMember> Owners { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RequestSnapshot
    {
        public required string OwnerKey { get; init; }
        public required int FromX { get; init; }
        public required int FromZ { get; init; }
        public required int GoalX { get; init; }
        public required int GoalZ { get; init; }
        public required short TargetX { get; init; }
        public required short TargetZ { get; init; }
        public required int Flags { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot
    {
        public required int Version { get; init; }
        public required string PathsKey { get; init; }
        public required string TilesKey { get; init; }
        public required string PoolKey { get; init; }
        public required int Admitted { get; init; }
        public required int Refused { get; init; }
        public required int Built { get; init; }
        public required int Unreachable { get; init; }
        public required int OutputExhausted { get; init; }
        /// <summary>Head first (next Pump first), including repeated owners.</summary>
        public required RequestSnapshot[] Pending { get; init; }
    }

    /// <summary>Detached distinct object inventory in first-pending-reference order.
    /// Submit permits multiple records for one owner: capture/restore must NOT deduplicate them.</summary>
    public IReadOnlyList<StaffMember> ReferencedOwners => _active.Select(r => r.Owner)
        .Distinct<StaffMember>(ReferenceEqualityComparer.Instance).ToArray();

    static bool StateKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 1024;
    static void StateRequire([DoesNotReturnIf(false)] bool ok, string reason)
    { if (!ok) throw new ArgumentException("Invalid StaffRouteService snapshot: " + reason); }

    void ValidateBindings(SnapshotBindings b)
    {
        ArgumentNullException.ThrowIfNull(b);
        StateRequire(StateKey(b.PathsKey) && StateKey(b.TilesKey) && StateKey(b.PoolKey)
            && b.Staff != null && b.Owners != null, "resource keys/bindings");
        StateRequire(ReferenceEquals(_paths, b.Staff.Paths) && ReferenceEquals(_tiles, b.Staff.Tiles)
            && ReferenceEquals(_pool, b.Staff.Routes) && ReferenceEquals(_tiles.Paths, _paths)
            && _tiles.Width > 0 && _tiles.Height > 0, "shared resource identity");
    }

    /// <summary>Quiescent single-threaded boundary only. No Submit/Pump/Refresh or owner
    /// events. StaffMember state (including route cursor), ParkPaths, NativeTileView's cached
    /// buildings/provider and NativeRoutePool values are EXTERNAL: save/restore them separately
    /// at this same boundary under the explicit binding keys. StaffMember persistence is not
    /// implemented here. Bound JSON bytes before deserialization. This is not a whole-park save.</summary>
    public Snapshot CaptureState(SnapshotBindings bindings)
    {
        ValidateBindings(bindings);
        var ids = new Dictionary<StaffMember, string>(ReferenceEqualityComparer.Instance);
        foreach (var pair in bindings.Owners)
        {
            StateRequire(StateKey(pair.Key) && pair.Value != null && ids.TryAdd(pair.Value, pair.Key),
                "invalid or aliased owner identity");
        }
        var rows = _active.Select(r =>
        {
            StateRequire(ids.TryGetValue(r.Owner, out var key), "missing owner identity");
            return new RequestSnapshot { OwnerKey = key!, FromX = r.From.X, FromZ = r.From.Z,
                GoalX = r.Goal.X, GoalZ = r.Goal.Z, TargetX = r.Target.X, TargetZ = r.Target.Z, Flags = r.Flags };
        }).ToArray();
        var s = new Snapshot { Version = SnapshotVersion, PathsKey = bindings.PathsKey,
            TilesKey = bindings.TilesKey, PoolKey = bindings.PoolKey, Admitted = Admitted,
            Refused = Refused, Built = Built, Unreachable = Unreachable,
            OutputExhausted = OutputExhausted, Pending = rows };
        ValidateSnapshot(s, bindings);
        return s;
    }

    /// <summary>Read-only preflight; invalid data never mutates the service, owners or resources.
    /// Repeated OwnerKey rows are legal; distinct keys aliasing one object are not. Any int Flags
    /// and short exact target are legal (Submit does not mask flags or clamp the exact endpoint).
    /// From is the stored clamped cell, NOT the discarded fine-grained Submit start point.</summary>
    public void ValidateSnapshot(Snapshot s, SnapshotBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(s);
        ValidateBindings(bindings);
        StateRequire(s.Version == SnapshotVersion && s.PathsKey == bindings.PathsKey
            && s.TilesKey == bindings.TilesKey && s.PoolKey == bindings.PoolKey, "schema/resource keys");
        StateRequire(s.Pending != null && s.Pending.Length <= Records, "pending bound");
        StateRequire(s.Admitted >= 0 && s.Refused >= 0 && s.Built >= 0 && s.Unreachable >= 0
            && s.OutputExhausted >= 0
            && (long)s.Built + s.Unreachable + s.OutputExhausted + s.Pending.Length <= s.Admitted,
            "counters (cancel/clear may leave an admission surplus)");
        var owners = new HashSet<StaffMember>(ReferenceEqualityComparer.Instance);
        foreach (var pair in bindings.Owners)
            StateRequire(StateKey(pair.Key) && pair.Value != null && owners.Add(pair.Value), "owner identity aliases");
        foreach (var r in s.Pending)
        {
            StateRequire(r != null && StateKey(r.OwnerKey), "request/owner key");
            // Use ordinal identity even if caller's dictionary uses a different comparer.
            var pair = bindings.Owners.FirstOrDefault(p => string.Equals(p.Key, r.OwnerKey, StringComparison.Ordinal));
            var owner = pair.Value;
            StateRequire(owner != null && Enum.IsDefined(owner.Kind)
                && (bindings.Staff.Active(owner.Kind).Any(o => ReferenceEquals(o, owner))
                    || bindings.Staff.Free(owner.Kind).Any(o => ReferenceEquals(o, owner))), "unresolved/foreign owner");
            StateRequire(_tiles.InBounds(r.FromX, r.FromZ) && _tiles.InBounds(r.GoalX, r.GoalZ), "cell bounds");
            StateRequire(new ParkCell(r.GoalX, r.GoalZ) == Clamp(new ParkCell(r.TargetX >> 8, r.TargetZ >> 8)),
                "goal/exact endpoint mismatch");
        }
    }

    /// <summary>Replace only this service's counters/list, after complete preflight. Intended
    /// for an unpublished staged ParkStaff.RouteRequests. Reuses, never clones, external owners
    /// and resources. No gameplay calls, route allocations/frees or owner event replay.</summary>
    public void RestoreState(Snapshot s, SnapshotBindings bindings)
    {
        ValidateSnapshot(s, bindings);
        var requests = s.Pending.Select(r => new Request(
            bindings.Owners.First(p => string.Equals(p.Key, r.OwnerKey, StringComparison.Ordinal)).Value,
            new(r.FromX, r.FromZ), new(r.GoalX, r.GoalZ), new Point(r.TargetX, r.TargetZ), r.Flags)).ToArray();
        _active.Clear();
        foreach (var request in requests) _active.AddLast(request);
        Admitted = s.Admitted; Refused = s.Refused; Built = s.Built;
        Unreachable = s.Unreachable; OutputExhausted = s.OutputExhausted;
    }
}
