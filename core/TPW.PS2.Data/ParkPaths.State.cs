using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

public sealed partial class ParkPaths
{
    public const int StateVersion = 1;
    public const int MaxStateCells = 1_048_576;

    /// <summary>Logical grid only, not terrain/model/material assets. Arrays retain enumeration
    /// order and are detached. Origin, walkway column and material bindings come from the resolved
    /// terrain; material classification caches are derived and deliberately not persisted.</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required string TerrainKey { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int CellStride { get; init; }
        public required float Step { get; init; }
        public required byte[] Cells { get; init; }
        public required ParkCell[] Occupied { get; init; }
        public required ParkCell[] Scenery { get; init; }
        public required ParkCell[] Entrance { get; init; }
        public required ParkCell[] Protected { get; init; }
        public required ParkCell[] GateHold { get; init; }
        public required EntranceKindState[] EntranceKinds { get; init; }
        public required EntryState EntranceEntry { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record EntranceKindState
    {
        public required ParkCell Cell { get; init; }
        public required int Kind { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record EntryState
    {
        public required int XStart { get; init; }
        public required int ZRow { get; init; }
        public required int XCol { get; init; }
        public required int ZEnd { get; init; }
        public required int PathRows { get; init; }
    }

    public State CaptureState(string terrainKey)
    {
        CheckGrid(Field.Width, Field.Height, Field.Cells);
        int max=Field.Width*Field.Height;
        Require(_occupied.Count<=max && _scenery.Count<=max && _entrance.Count<=max
            && _protected.Count<=max && _gateHold.Count<=max && _entranceKind.Count<=max,"capture set bounds");
        var state = new State
        {
            Version = StateVersion, TerrainKey = terrainKey, Width = Field.Width,
            Height = Field.Height, CellStride = 2, Step = Field.Step, Cells = (byte[])Field.Cells.Clone(),
            Occupied = _occupied.ToArray(), Scenery = _scenery.ToArray(), Entrance = _entrance.ToArray(),
            Protected = _protected.ToArray(), GateHold = _gateHold.ToArray(),
            EntranceKinds = _entranceKind.Select(p => new EntranceKindState { Cell = p.Key, Kind = p.Value }).ToArray(),
            EntranceEntry = EntranceEntry is { } e ? new EntryState
                { XStart = e.XStart, ZRow = e.ZRow, XCol = e.XCol, ZEnd = e.ZEnd, PathRows = e.PathRows } : null,
        };
        Validate(state, terrainKey, Field.Width, Field.Height);
        return state;
    }

    /// <summary>Fresh owner, no gameplay replay. expectedTerrainKey must be independently resolved
    /// by the parent, not echoed from untrusted state. Terrain is a read-only asset dependency.
    /// Restore PathTool against this owner's Field (never a second copy of that field).
    /// Parent must bound JSON input before deserializing; these bounds apply to the decoded DTO.</summary>
    public static ParkPaths FromState(State state, string expectedTerrainKey, Model terrain)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(terrain);
        Require(terrain.Field != null, "terrain has no grid");
        var f = terrain.Field;
        CheckGrid(f.Width, f.Height, f.Cells);
        Validate(state, expectedTerrainKey, f.Width, f.Height);
        // Constructor only reads geometry and copies the authored grid; all logical sets below
        // replace its derived defaults. No SetEntrance, Lay, Occupy, or other gameplay calls.
        var paths = new ParkPaths(terrain);
        paths.Field.Cells = (byte[])state.Cells.Clone();
        paths.Field.Step = state.Step;
        void Copy(HashSet<ParkCell> target, ParkCell[] cells) { target.Clear(); foreach (var c in cells) target.Add(c); }
        Copy(paths._occupied, state.Occupied); Copy(paths._scenery, state.Scenery);
        Copy(paths._entrance, state.Entrance); Copy(paths._protected, state.Protected); Copy(paths._gateHold, state.GateHold);
        foreach (var k in state.EntranceKinds) paths._entranceKind.Add(k.Cell, k.Kind);
        if (state.EntranceEntry is { } e)
            paths.EntranceEntry = new ParkEntranceEntry(e.XStart, e.ZRow, e.XCol, e.ZEnd, e.PathRows);
        return paths;
    }

    static void Require(bool ok, string reason)
    { if (!ok) throw new ArgumentException("Invalid ParkPaths state: " + reason); }
    static void CheckGrid(int width, int height, byte[] cells)
    {
        Require(width is > 0 and <= 4096 && height is > 0 and <= 4096
            && (long)width * height <= MaxStateCells, "grid bounds");
        Require(cells != null && cells.Length == (long)width * height * 2, "cell byte length");
    }
    static void Validate(State s, string expectedKey, int width, int height)
    {
        Require(s.Version == StateVersion, "version");
        Require(!string.IsNullOrWhiteSpace(expectedKey) && expectedKey.Length <= 1024
            && string.Equals(s.TerrainKey, expectedKey, StringComparison.Ordinal), "terrain key");
        Require(s.Width == width && s.Height == height && s.CellStride == 2, "grid identity/stride");
        CheckGrid(s.Width, s.Height, s.Cells);
        Require(float.IsFinite(s.Step) && s.Step >= 0 && s.Step <= 65536, "height step");
        bool Inside(ParkCell c) => c.X >= 0 && c.Z >= 0 && c.X < width && c.Z < height;
        HashSet<ParkCell> Set(ParkCell[] cells)
        {
            Require(cells != null && cells.Length <= width * height, "set length");
            var seen = new HashSet<ParkCell>();
            foreach (var c in cells) Require(Inside(c) && seen.Add(c), "out-of-grid/duplicate cell");
            return seen;
        }
        Set(s.Occupied); Set(s.Scenery); var entrance = Set(s.Entrance); Set(s.Protected); Set(s.GateHold);
        Require(s.EntranceKinds != null && s.EntranceKinds.Length == entrance.Count, "entrance kind length");
        var kinds = new HashSet<ParkCell>();
        foreach (var k in s.EntranceKinds)
            Require(k != null && entrance.Contains(k.Cell) && kinds.Add(k.Cell) && k.Kind is 12 or 14, "entrance kind");
        if (s.EntranceEntry is { } e)
            Require(e.XStart is >= 0 and <= 255 && e.XCol is >= 0 and <= 255
                && e.ZRow is >= 0 and <= 255 && e.ZEnd is > 0 and <= 255
                && e.PathRows is >= 0 and <= 65535, "entrance entry");
        else Require(entrance.Count == 0 && s.Protected.Length == 0 && s.GateHold.Length == 0, "entrance without entry");
    }
}
