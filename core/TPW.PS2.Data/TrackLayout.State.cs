using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

public sealed partial class TrackLayout
{
    public const int StateVersion = 1;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CellState
    {
        public required int X { get; init; }
        public required int Z { get; init; }
        internal static CellState Of(ParkCell c) => new() { X = c.X, Z = c.Z };
        internal ParkCell Cell => new(X, Z);
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record SampleState
    {
        public required int P1X { get; init; }
        public required int P1Z { get; init; }
        public required int P2X { get; init; }
        public required int P2Z { get; init; }
        public required int Height { get; init; }
        public required int Yaw { get; init; }
        internal static SampleState Of(TrackSample s) => new()
        { P1X = s.P1X, P1Z = s.P1Z, P2X = s.P2X, P2Z = s.P2Z, Height = s.Height, Yaw = s.Yaw };
        internal TrackSample Sample => new(P1X, P1Z, P2X, P2Z, Height, Yaw);
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PieceState
    {
        public required int Type { get; init; }
        public required CellState Anchor { get; init; }
        public required SampleState[] Samples { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record UpgradeState
    {
        public required int Index { get; init; }
        public required int Kind { get; init; }
    }

    /// <summary>The laid chain is authoritative, not regenerated from waypoints. Ground and its
    /// path callback are external. The parent must resolve GroundKey independently against its
    /// staged ground registry; neither a delegate nor an asset/model graph is saved here.</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required string GroundKey { get; init; }
        public required int GroundWorld { get; init; }
        public required int GroundPark { get; init; }
        public required CellState Station { get; init; }
        public required int Rotation { get; init; }
        public required bool Closed { get; init; }
        public required CellState[] Waypoints { get; init; }
        public required PieceState[] Pieces { get; init; }
        public required UpgradeState[] Upgrades { get; init; }
    }

    public State CaptureState(string groundKey)
    {
        CheckGroundKey(groundKey);
        // Preflight counts before allocating. The supported save envelope is the native tool's.
        if (_waypoints.Count is < 1 or > MaxWaypoints || _pieces.Count > MaxPieces || _upgrades.Count > MaxUpgrades)
            throw new ArgumentException("Track layout exceeds snapshot collection bounds.");
        var state = new State
        {
            Version = StateVersion, GroundKey = groundKey, GroundWorld = Ground.World, GroundPark = Ground.Park,
            Station = CellState.Of(Station), Rotation = Rotation, Closed = Closed,
            Waypoints = _waypoints.Select(CellState.Of).ToArray(),
            Pieces = _pieces.Select(p => new PieceState
            { Type = p.Type, Anchor = CellState.Of(p.Anchor), Samples = p.Samples.Select(SampleState.Of).ToArray() }).ToArray(),
            Upgrades = _upgrades.Select(u => new UpgradeState { Index = u.Index, Kind = u.Kind }).ToArray()
        };
        ValidateState(state, groundKey, Ground);
        return state;
    }

    // Allocation only: do not use the public constructor (which seeds waypoints and rebuilds).
    private TrackLayout(State state, TrackGround ground)
    {
        Station = state.Station.Cell;
        Rotation = state.Rotation;
        Ground = ground;
        Closed = state.Closed;
        _waypoints.AddRange(state.Waypoints.Select(w => w.Cell));
        foreach (var p in state.Pieces)
        {
            var piece = new TrackPiece(p.Type, p.Anchor.Cell);
            for (int i = 0; i < 4; i++) piece.Samples[i] = p.Samples[i].Sample;
            _pieces.Add(piece);
        }
        _upgrades.AddRange(state.Upgrades.Select(u => new TrackUpgrade(u.Index, u.Kind)));
    }

    /// <summary>Fresh ownership, without baking, querying ground, replaying upgrades, or editing
    /// the chain. expectedGroundKey must come from the caller's resolved binding, not the DTO.</summary>
    public static TrackLayout FromState(State state, string expectedGroundKey, TrackGround ground)
    {
        ValidateState(state, expectedGroundKey, ground);
        return new TrackLayout(state, ground);
    }

    static void CheckGroundKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 1024)
            throw new ArgumentException("Missing or oversized track ground key.");
    }

    static void ValidateCell(CellState c)
    {
        // Leave ample room for bake offsets and cell/distance arithmetic, including negative cells.
        if (c == null || c.X is < -32768 or > 32767 || c.Z is < -32768 or > 32767)
            throw new ArgumentException("Invalid track cell.");
    }

    static void ValidateState(State s, string expectedGroundKey, TrackGround ground)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(ground);
        CheckGroundKey(s.GroundKey);
        CheckGroundKey(expectedGroundKey);
        if (s.Version != StateVersion || !string.Equals(s.GroundKey, expectedGroundKey, StringComparison.Ordinal)
            || s.GroundWorld is < 0 or > 3 || s.GroundPark is < 0 or > 1
            || s.GroundWorld != ground.World || s.GroundPark != ground.Park || ground.Bridged == null)
            throw new ArgumentException("Unsupported track state or mismatched ground binding.");
        ValidateCell(s.Station);
        if (s.Rotation is < 0 or > 3 || s.Waypoints == null || s.Waypoints.Length is < 1 or > MaxWaypoints
            || s.Pieces == null || s.Pieces.Length > MaxPieces || s.Upgrades == null || s.Upgrades.Length > MaxUpgrades)
            throw new ArgumentException("Invalid track rotation or collection bounds.");
        foreach (var w in s.Waypoints) ValidateCell(w);
        if (s.Waypoints[0].Cell != TrackPieces.Exit(s.Station.Cell, s.Rotation))
            throw new ArgumentException("Track waypoint zero is not the station exit.");
        foreach (var p in s.Pieces)
        {
            if (p == null || (uint)p.Type >= TrackPieces.Count || p.Samples == null || p.Samples.Length != 4
                || p.Samples.Any(sample => sample == null))
                throw new ArgumentException("Invalid track piece or sample array.");
            ValidateCell(p.Anchor);
        }
        foreach (var u in s.Upgrades)
            // An open purchase on station slot zero can store -1. Do not require a one-to-one
            // correspondence with laid add-ons: pending/open edits can retain unused entries.
            if (u == null || u.Kind is < 0 or > 15 || u.Index < -1 || u.Index >= s.Pieces.Length)
                throw new ArgumentException("Invalid track upgrade reference.");
        // Closed is independent: Add at the waypoint limit can clear it without rebuilding.
        // Samples are raw mutable Int32 data, including not-yet-baked zeros and unwrapped yaw.
    }
}
