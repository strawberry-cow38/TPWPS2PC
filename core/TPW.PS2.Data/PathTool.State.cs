using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

public sealed partial class PathTool
{
    public const int StateVersion = 1;
    public const int MaxStateCells = 1_048_576;
    public const int MaxStateLegs = 65536;
    readonly string[] _materialKeys;
    internal Model.HeightField SnapshotField => _field;

    /// <summary>No terrain bytes or assets: the parent saves/restores the shared grid separately.
    /// Material references are ordered slots scoped by TerrainKey, checked against the complete
    /// stable material-name table. Null sprite arrays preserve an unready tool. Callbacks are not saved.
    /// Leg IDs index LegObjects; UndoLegIds is the ordered stack, ActiveLegId is -1 for null.
    /// An active object outside the stack is supported; it is never implicitly the last leg.</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required string TerrainKey { get; init; }
        public required string PathPiecesKey { get; init; }
        public required bool HasPieces { get; init; }
        public required string[] MaterialKeys { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int[] PathSprites { get; init; }
        public required int[] QueueSprites { get; init; }
        public required Kind[] Kinds { get; init; }
        public required int[] Turns { get; init; }
        public required int[] Owners { get; init; }
        public required int[] Runs { get; init; }
        public required int[] Bridge { get; init; }
        public required int[] Walkway { get; init; }
        public required DoorState[] Doors { get; init; }
        public required GroundState[] OriginalTilesBefore { get; init; }
        public required CellState[][] LegObjects { get; init; }
        public required int[] UndoLegIds { get; init; }
        public required int ActiveLegId { get; init; }
        public required int Laid { get; init; }
        public required string Report { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CellState
    {
        public required int At { get; init; }
        public required Kind Kind { get; init; }
        public required int Owner { get; init; }
        public required int Run { get; init; }
        public required byte Tile { get; init; }
        public required int Turns { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record DoorState
    {
        public required int At { get; init; }
        public required int Ride { get; init; }
        public required bool Entrance { get; init; }
        public required int Dx { get; init; }
        public required int Dy { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GroundState
    {
        public required int At { get; init; }
        public required byte Tile { get; init; }
    }

    public State CaptureState(string terrainKey, string pathPiecesKey)
    {
        Require(_field != null, "tool has no grid");
        // Bound before copying the history or invoking asset-dependent validation.
        Require(_field.Width>0 && _field.Height>0 && (long)_field.Width*_field.Height<=MaxStateCells
            && _legs.Count<=MaxStateLegs, "capture grid/history bounds");
        long records=0;
        foreach(var leg in _legs) { Require(leg!=null,"null live leg"); records+=leg.Count; }
        if(_leg!=null && !_legs.Contains(_leg)) records+=_leg.Count;
        Require(records<=MaxStateCells,"capture undo record budget");
        var objects = new List<List<Was>>();
        var objectIds = new Dictionary<List<Was>, int>(ReferenceEqualityComparer.Instance);
        int Id(List<Was> leg)
        {
            if (leg == null) return -1;
            if (objectIds.TryGetValue(leg, out int id)) return id;
            id = objects.Count; objectIds.Add(leg, id); objects.Add(leg); return id;
        }
        var undo = _legs.Select(Id).ToArray();
        int active = Id(_leg);
        var s = new State
        {
            Version = StateVersion, TerrainKey = terrainKey, PathPiecesKey = pathPiecesKey,
            HasPieces = _pieces != null, MaterialKeys = (string[])_materialKeys.Clone(),
            Width = _field.Width, Height = _field.Height,
            PathSprites = _pathSprites?.ToArray(), QueueSprites = _queueSprites?.ToArray(),
            Kinds = _kind.ToArray(), Turns = _turns.ToArray(), Owners = _owner.ToArray(), Runs = _run.ToArray(),
            Bridge = _bridge.ToArray(), Walkway = _walkway.ToArray(),
            Doors = _doors.Select(p => new DoorState { At = p.Key, Ride = p.Value.Ride,
                Entrance = p.Value.Entrance, Dx = p.Value.Dx, Dy = p.Value.Dy }).ToArray(),
            OriginalTilesBefore = _before.Select(p => new GroundState { At = p.Key, Tile = p.Value }).ToArray(),
            LegObjects = objects.Select(l => l.Select(w => new CellState { At = w.At, Kind = w.Kind,
                Owner = w.Owner, Run = w.Run, Tile = w.Tile, Turns = w.Turns }).ToArray()).ToArray(),
            UndoLegIds = undo, ActiveLegId = active, Laid = Laid, Report = Report,
        };
        Validate(s, terrainKey, pathPiecesKey, _materialKeys, _field, _pieces);
        return s;
    }

    /// <summary>Allocation/copy only. Does not construct via the gameplay constructor, repaint,
    /// replay edits, or publish events. The supplied field IS the shared restored field, not a clone.
    /// Resolve keys independently and bound JSON bytes before decoding. Rebind events after staging.</summary>
    public static PathTool FromState(State state, string expectedTerrainKey, string expectedPiecesKey,
        Model terrain, PathPieces pieces, Model.HeightField restoredSharedField)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(terrain);
        Validate(state, expectedTerrainKey, expectedPiecesKey, terrain.Materials.ToArray(), restoredSharedField, pieces);
        Require(terrain.Field != null && terrain.Field.Width == state.Width && terrain.Field.Height == state.Height,
            "terrain grid identity");
        return new PathTool(state, pieces, restoredSharedField);
    }

    private PathTool(State s, PathPieces pieces, Model.HeightField field)
    {
        _field = field; _pieces = pieces; _materialKeys = s.MaterialKeys.ToArray();
        _pathSprites = s.PathSprites?.ToArray(); _queueSprites = s.QueueSprites?.ToArray();
        _kind = s.Kinds.ToArray(); _turns = s.Turns.ToArray(); _owner = s.Owners.ToArray(); _run = s.Runs.ToArray();
        foreach (int at in s.Bridge) _bridge.Add(at);
        foreach (int at in s.Walkway) _walkway.Add(at);
        foreach (var d in s.Doors) _doors.Add(d.At, (d.Ride, d.Entrance, d.Dx, d.Dy));
        foreach (var g in s.OriginalTilesBefore) _before.Add(g.At, g.Tile);
        var objects = s.LegObjects.Select(l => l.Select(w => new Was(w.At, w.Kind, w.Owner, w.Run, w.Tile, w.Turns)).ToList()).ToArray();
        foreach (int id in s.UndoLegIds) _legs.Add(objects[id]);
        _leg = s.ActiveLegId == -1 ? null : objects[s.ActiveLegId];
        Laid = s.Laid; Report = s.Report;
    }

    static void Require(bool ok, string reason)
    { if (!ok) throw new ArgumentException("Invalid PathTool state: " + reason); }
    static void Validate(State s, string terrainKey, string piecesKey, string[] materials,
        Model.HeightField field, PathPieces pieces)
    {
        bool Key(string key, string expected) => !string.IsNullOrWhiteSpace(expected) && expected.Length <= 1024
            && string.Equals(key, expected, StringComparison.Ordinal);
        Require(s.Version == StateVersion, "version");
        Require(Key(s.TerrainKey, terrainKey) && Key(s.PathPiecesKey, piecesKey), "asset keys");
        Require(s.HasPieces == (pieces != null), "pieces binding");
        Require(s.Width is > 0 and <= 4096 && s.Height is > 0 and <= 4096
            && (long)s.Width * s.Height <= MaxStateCells, "grid bounds");
        int n = s.Width * s.Height;
        Require(field != null && field.Width == s.Width && field.Height == s.Height
            && field.Cells != null && field.Cells.Length == n * 2, "shared field shape");
        Require(s.MaterialKeys != null && s.MaterialKeys.Length <= 65536
            && s.MaterialKeys.All(k => k == null || k.Length <= 4096)
            && s.MaterialKeys.SequenceEqual(materials), "material bindings");
        void Sprites(int[] sprites, int count, ParkPathKind kind)
        {
            if (sprites == null) return;
            Require(sprites.Length == count && sprites.Distinct().Count() == count, "sprite count/duplicates");
            foreach (int i in sprites) Require(i > 0 && i < materials.Length && i <= 255
                && ParkPaths.Classify(materials[i] ?? "") == kind, "sprite reference");
            Require(sprites.SequenceEqual(Enumerable.Range(1, materials.Length - 1)
                .Where(i => ParkPaths.Classify(materials[i] ?? "") == kind)), "sprite order");
        }
        Require((s.PathSprites == null) == (s.QueueSprites == null), "partial sprites");
        Sprites(s.PathSprites, 16, ParkPathKind.Path); Sprites(s.QueueSprites, 4, ParkPathKind.Queue);
        bool KindOk(Kind k) => k is Kind.None or Kind.Path or Kind.Queue or Kind.Both;
        void Values(Kind k, int owner, int run, int turns)
            => Require(KindOk(k) && owner >= 0 && run is >= 0 and <= 255 && turns is >= 0 and <= 3, "cell values");
        Require(s.Kinds?.Length == n && s.Turns?.Length == n && s.Owners?.Length == n && s.Runs?.Length == n, "array lengths");
        for (int i = 0; i < n; i++) Values(s.Kinds[i], s.Owners[i], s.Runs[i], s.Turns[i]);
        void Indices(int[] indices)
        {
            Require(indices != null && indices.Length <= n, "cell list length");
            var seen = new HashSet<int>();
            foreach (int at in indices) Require(at >= 0 && at < n && seen.Add(at), "cell bounds/duplicate ID");
        }
        Indices(s.Bridge); Indices(s.Walkway);
        Require(s.Doors != null && s.Doors.Length <= n && s.Doors.All(d => d != null), "doors");
        Indices(s.Doors.Select(d => d.At).ToArray());
        foreach (var d in s.Doors) Require(d.Ride >= 0 && d.Dx is >= -1 and <= 1 && d.Dy is >= -1 and <= 1, "door values");
        Require(s.OriginalTilesBefore != null && s.OriginalTilesBefore.Length <= n
            && s.OriginalTilesBefore.All(g => g != null), "ground records");
        Indices(s.OriginalTilesBefore.Select(g => g.At).ToArray());
        Require(s.LegObjects != null && s.LegObjects.Length <= MaxStateLegs, "leg objects");
        long total = 0;
        foreach (var leg in s.LegObjects)
        {
            Require(leg != null && leg.Length <= n && (total += leg.Length) <= MaxStateCells && leg.All(w => w != null), "leg bounds");
            Indices(leg.Select(w => w.At).ToArray());
            foreach (var w in leg) Values(w.Kind, w.Owner, w.Run, w.Turns);
        }
        Require(s.UndoLegIds != null && s.UndoLegIds.Length <= MaxStateLegs, "undo stack");
        var ids = new HashSet<int>();
        foreach (int id in s.UndoLegIds) Require(id >= 0 && id < s.LegObjects.Length && ids.Add(id), "undo reference/duplicate ID");
        Require(s.ActiveLegId >= -1 && s.ActiveLegId < s.LegObjects.Length, "active leg reference");
        if (s.ActiveLegId >= 0) ids.Add(s.ActiveLegId);
        Require(ids.Count == s.LegObjects.Length, "unreferenced leg object");
        // Laid is an operational counter, not the number of nonempty cells (Both/undo differ).
        Require(s.Report == null || s.Report.Length <= 4096, "report length");
    }
}
