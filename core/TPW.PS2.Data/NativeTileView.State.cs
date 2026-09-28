#nullable enable
using System.Text.Json.Serialization;
namespace TPW.PS2.Data;
public sealed partial class NativeTileView
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CellState
    {
        public required int X { get; init; }
        public required int Z { get; init; }
        public required int Kind { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required string PathsKey { get; init; }
        public required string PlacedKey { get; init; }
        public required CellState[] Buildings { get; init; }
    }
    /// <summary>The provider binding, not its current results. Capture never invokes it.</summary>
    public Func<IEnumerable<ParkRide>> PlacedProvider => _placed;
    public State CaptureState(string pathsKey, Func<Delegate, string> callbackId)
    {
        ArgumentNullException.ThrowIfNull(callbackId);
        var s = new State { Version = 1, PathsKey = pathsKey, PlacedKey = StaffLeafBindings.Key(_placed, callbackId)!,
            Buildings = _buildings.Select(p => new CellState { X = p.Key.X, Z = p.Key.Z, Kind = p.Value }).ToArray() };
        ValidateState(s); return s;
    }
    public static void ValidateState(State s)
    {
        ArgumentNullException.ThrowIfNull(s);
        StaffLeafBindings.Require(s.Version == 1 && StaffLeafBindings.ValidKey(s.PathsKey)
            && StaffLeafBindings.ValidKey(s.PlacedKey) && s.Buildings != null && s.Buildings.Length<=1_000_000, "tile schema/bindings");
        var seen = new HashSet<ParkCell>();
        foreach (var c in s.Buildings!)
            StaffLeafBindings.Require(c != null && c.Kind is KindBuilding or KindBuildingEntry
                && seen.Add(new(c.X, c.Z)), "tile cached cell");
        // Out-of-bounds footprints can exist in Refresh's cache; preserve rather than clip them.
    }
    /// <summary>Bind explicitly resolved shared paths/provider; retain even a stale cache, never Refresh.</summary>
    public static NativeTileView FromState(State s, string expectedPathsKey, ParkPaths paths,
        Func<string, Delegate> resolveCallback)
    {
        ValidateState(s); ArgumentNullException.ThrowIfNull(paths); ArgumentNullException.ThrowIfNull(resolveCallback);
        StaffLeafBindings.Require(s.PathsKey == expectedPathsKey, "tile paths identity");
        var placed = StaffLeafBindings.Resolve<Func<IEnumerable<ParkRide>>>(s.PlacedKey, resolveCallback)!;
        var result = new NativeTileView(paths, placed);
        foreach (var c in s.Buildings) result._buildings.Add(new(c.X, c.Z), c.Kind);
        return result;
    }
}
