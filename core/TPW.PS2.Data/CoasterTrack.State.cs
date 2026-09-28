using System.Text.Json.Serialization;
using System.Numerics;

namespace TPW.PS2.Data;

/// <summary>Explicit JSON vector: System.Text.Json's default does not serialize Vector3 fields.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CoasterVectorState
{
    public required float X { get; init; }
    public required float Y { get; init; }
    public required float Z { get; init; }
    internal static CoasterVectorState Capture(Vector3 v) => new() { X = v.X, Y = v.Y, Z = v.Z };
    internal Vector3 Restore()
    {
        CoasterStateValidation.Number(X); CoasterStateValidation.Number(Y); CoasterStateValidation.Number(Z);
        return new(X, Y, Z);
    }
}

// Generous operational bounds, not geometry regeneration or a second placement validator.
internal static class CoasterStateValidation
{
    internal static void Require(bool ok, string field)
    {
        if (!ok) throw new ArgumentException("Invalid coaster snapshot: " + field);
    }
    internal static void Number(float value, float min = -1000000, float max = 1000000) =>
        Require(float.IsFinite(value) && value >= min && value <= max, "finite/bounded number");
    internal static void Range(int value, int min, int max, string field) => Require(value >= min && value <= max, field);
    internal static Vector3 Vector(CoasterVectorState value)
    {
        Require(value != null, "vector"); return value.Restore();
    }
}

public sealed partial class CoasterTrack
{
    public const int StateVersion = 1;
    // Includes the 34 owned nodes and linked build-tool ghosts. Cross-track links are not bindings.
    public const int MaxStateNodes = 256;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required string TypeKey { get; init; }
        public required string TypeFolder { get; init; }
        public required int TypeWorld { get; init; }
        public required int TypePark { get; init; }
        public required int TypeOrdinal { get; init; }
        public required int ExitDirection { get; init; }
        public required int ExitId { get; init; }
        public required int EntryId { get; init; }
        public required bool Closed { get; init; }
        public required bool Valid { get; init; }
        public required int[] PylonIds { get; init; }
        public required NodeState[] Nodes { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record NodeState
    {
        public required int CellX { get; init; }
        public required int CellZ { get; init; }
        public required int YBase { get; init; }
        public required int Height { get; init; }
        public required int Bank { get; init; }
        public required int Heading { get; init; }
        public required int HalfTurn { get; init; }
        public required int Chord { get; init; }
        public required int TrackY { get; init; }
        public required bool LoopFlag { get; init; }
        public required bool Valid { get; init; }
        public required bool IsStation { get; init; }
        public required CoasterNodeKind Kind { get; init; }
        public required int PrevId { get; init; }
        public required int NextId { get; init; }
        public required int AboveId { get; init; }
        public required int BelowId { get; init; }
        public required CoasterVectorState[] P { get; init; }
        public required CoasterVectorState[] S { get; init; }
        public required SampleState[] Samples { get; init; }
        public required float Length { get; init; }
        public required float VIn { get; init; }
        public required float WIn { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record SampleState
    {
        public required CoasterVectorState Pos { get; init; }
        public required CoasterVectorState Side { get; init; }
        public required CoasterVectorState Normal { get; init; }
        public required CoasterVectorState Tangent { get; init; }
        public required float Len { get; init; }
        public required float V0 { get; init; }
        public required float V1 { get; init; }
        public required float W0 { get; init; }
        public required float W1 { get; init; }
        public required bool Winch { get; init; }
    }

    /// <summary>Snapshot-local stable IDs: exit 0, entry 1, pylons in list order, then linked
    /// auxiliary nodes in deterministic discovery order. Stable until topology changes; -1 is null.
    /// Use these IDs only with this track snapshot, never as persistent park IDs.</summary>
    public int GetNodeId(CoasterNode node)
    {
        if (node == null) return -1;
        int id = StateNodes().IndexOf(node);
        CoasterStateValidation.Require(id >= 0, "foreign node");
        return id;
    }

    public CoasterNode ResolveNodeId(int id)
    {
        if (id == -1) return null;
        var nodes = StateNodes();
        CoasterStateValidation.Range(id, 0, nodes.Count - 1, "node ID");
        return nodes[id];
    }

    internal IReadOnlyList<CoasterNode> StateNodesForSim() => StateNodes();

    List<CoasterNode> StateNodes()
    {
        var nodes = new List<CoasterNode> { Exit, Entry };
        nodes.AddRange(_pylons);
        CoasterStateValidation.Require(nodes.All(n => n != null) && nodes.Distinct().Count() == nodes.Count, "node ownership");
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            foreach (var link in new[] { n.Prev, n.Next, n.Above, n.Below })
                if (link != null && !nodes.Contains(link))
                {
                    CoasterStateValidation.Require(nodes.Count < MaxStateNodes, "linked node limit");
                    nodes.Add(link);
                }
        }
        return nodes;
    }

    /// <summary>Detached graph and exact caches. Call at a tick boundary. The key is supplied by
    /// the parent's trusted asset registry; neither terrain delegates nor assets enter the DTO.</summary>
    public State CaptureState(string typeKey)
    {
        CoasterStateValidation.Require(!string.IsNullOrWhiteSpace(typeKey) && typeKey.Length <= 1024, "type key");
        var nodes = StateNodes();
        int Id(CoasterNode n) => n == null ? -1 : nodes.IndexOf(n);
        return new State
        {
            Version = StateVersion, TypeKey = typeKey, TypeFolder = Type.Folder,
            TypeWorld = Type.World, TypePark = Type.Park, TypeOrdinal = Type.Ordinal,
            ExitDirection = ExitDirection, ExitId = Id(Exit), EntryId = Id(Entry),
            Closed = Closed, Valid = Valid, PylonIds = _pylons.Select(Id).ToArray(),
            Nodes = nodes.Select(n => new NodeState
            {
                CellX = n.CellX,
                CellZ = n.CellZ,
                YBase = n.YBase,
                Height = n.Height,
                Bank = n.Bank,
                Heading = n.Heading,
                HalfTurn = n.HalfTurn,
                Chord = n.Chord,
                TrackY = n.TrackY,
                LoopFlag = n.LoopFlag,
                Valid = n.Valid,
                IsStation = n.IsStation,
                Kind = n.Kind,
                PrevId = Id(n.Prev),
                NextId = Id(n.Next),
                AboveId = Id(n.Above),
                BelowId = Id(n.Below),
                P = n.P.Select(CoasterVectorState.Capture).ToArray(),
                S = n.S.Select(CoasterVectorState.Capture).ToArray(),
                Samples = n.Samples.Select(CaptureSample).ToArray(),
                Length = n.Length,
                VIn = n.VIn,
                WIn = n.WIn,
            }).ToArray(),
        };
    }

    static SampleState CaptureSample(CoasterSample s) => new()
    {
        Pos = CoasterVectorState.Capture(s.Pos),
        Side = CoasterVectorState.Capture(s.Side),
        Normal = CoasterVectorState.Capture(s.Normal),
        Tangent = CoasterVectorState.Capture(s.Tangent),
        Len = s.Len,
        V0 = s.V0,
        V1 = s.V1,
        W0 = s.W0,
        W1 = s.W1,
        Winch = s.Winch,
    };

    // Allocation only: deliberately bypasses the public geometry-building constructor.
    CoasterTrack(CoasterType type, int exitDirection, CoasterNode exit, CoasterNode entry,
                 bool closed, Func<int, int, int> groundY)
    {
        Type = type; ExitDirection = exitDirection; Exit = exit; Entry = entry;
        Closed = closed; GroundY = groundY;
    }

    /// <summary>Allocates and copies only. GroundY must be a pure staged binding supplied by the
    /// parent; it is stored but NEVER queried here. The parent checks the key against its asset
    /// registry and binds graph identity. Does not rebuild links, geometry, validity or winches.</summary>
    public static CoasterTrack FromState(State state, string expectedTypeKey, CoasterType type,
                                         Func<int, int, int> groundY)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(groundY);
        CoasterStateValidation.Require(state.Version == StateVersion, "track version");
        CoasterStateValidation.Require(!string.IsNullOrWhiteSpace(expectedTypeKey) && expectedTypeKey.Length <= 1024
            && state.TypeKey == expectedTypeKey && state.TypeFolder == type.Folder
            && state.TypeWorld == type.World && state.TypePark == type.Park && state.TypeOrdinal == type.Ordinal, "trusted type identity");
        CoasterStateValidation.Range(state.ExitDirection, 0, 3, "exit direction");
        CoasterStateValidation.Require(state.Nodes != null && state.Nodes.Length >= 2 && state.Nodes.Length <= MaxStateNodes
            && state.PylonIds != null && state.PylonIds.Length <= MaxPylons, "node/pylon lengths");
        CoasterStateValidation.Require(state.ExitId == 0 && state.EntryId == 1
            && state.Nodes.Length >= state.PylonIds.Length + 2, "station IDs");
        for (int i = 0; i < state.PylonIds.Length; i++)
            CoasterStateValidation.Require(state.PylonIds[i] == i + 2, "pylon ID/order");
        var nodes = state.Nodes.Select(RestoreNode).ToArray();
        CoasterNode Ref(int id)
        {
            CoasterStateValidation.Range(id, -1, nodes.Length - 1, "node reference");
            return id == -1 ? null : nodes[id];
        }
        for (int i = 0; i < nodes.Length; i++)
        {
            var n = nodes[i]; var s = state.Nodes[i];
            n.Prev = Ref(s.PrevId); n.Next = Ref(s.NextId); n.Above = Ref(s.AboveId); n.Below = Ref(s.BelowId);
        }
        CoasterStateValidation.Require(nodes[0].IsStation && nodes[1].IsStation
            && state.PylonIds.All(id => !nodes[id].IsStation), "station identity");
        var track = new CoasterTrack(type, state.ExitDirection, nodes[0], nodes[1], state.Closed, groundY);
        track._pylons.AddRange(state.PylonIds.Select(id => nodes[id]));
        CoasterStateValidation.Require(track.Valid == state.Valid, "aggregate validity");
        CoasterStateValidation.Require(track.StateNodes().SequenceEqual(nodes), "unreachable/noncanonical node IDs");
        // Closed rings must visit every owned segment, then the entry, then the exit.
        // Merely stopping cycle detection at Exit accepts Exit.Next == Exit: MarkLift/Why
        // walk until Entry and would hang on that malformed graph.
        if (state.Closed)
        {
            var ring = new[] { track.Exit }.Concat(track._pylons).Append(track.Entry).ToArray();
            if (state.Valid)
                CoasterStateValidation.Require(ring.All(n => n.Length > 0), "runnable segment length");
            for (int i = 0; i < ring.Length; i++)
                CoasterStateValidation.Require(ring[i].Next == ring[(i + 1) % ring.Length]
                    && ring[i].Prev == ring[(i + ring.Length - 1) % ring.Length], "closed ring topology");
        }
        // Stack walks have no runtime guard. Open build graphs can have a one-way ghost
        // stack link, so do not demand reciprocity or rebuild it. Any ring cycle must contain
        // BOTH station sentinels: different consumers stop at different sentinels.
        foreach (var start in nodes)
        {
            foreach (bool above in new[] { false, true })
            {
                var seen = new HashSet<CoasterNode>();
                for (var n = start; n != null; n = above ? n.Above : n.Below)
                    CoasterStateValidation.Require(seen.Add(n), "cyclic stack");
            }
            foreach (bool forward in new[] { false, true })
            {
                var walk = new List<CoasterNode>();
                for (var n = start; n != null; n = forward ? n.Next : n.Prev)
                {
                    int repeated = walk.IndexOf(n);
                    if (repeated >= 0)
                    {
                        var cycle = walk.Skip(repeated);
                        CoasterStateValidation.Require(cycle.Contains(track.Exit) && cycle.Contains(track.Entry), "nonstation ring cycle");
                        break;
                    }
                    walk.Add(n);
                }
            }
        }
        return track;
    }

    static CoasterNode RestoreNode(NodeState s)
    {
        CoasterStateValidation.Require(s != null, "node");
        CoasterStateValidation.Range(s.CellX, -32768, 32767, "cell X");
        CoasterStateValidation.Range(s.CellZ, -32768, 32767, "cell Z");
        CoasterStateValidation.Range(s.YBase, -1000000, 1000000, "base height");
        CoasterStateValidation.Range(s.TrackY, -1000000, 1000000, "track height");
        CoasterStateValidation.Range(s.Height, 0, 65535, "height");
        CoasterStateValidation.Range(s.Bank, -2048, 2048, "bank");
        CoasterStateValidation.Range(s.Heading, 0, 4095, "heading");
        CoasterStateValidation.Range(s.HalfTurn, -2048, 2048, "half turn");
        CoasterStateValidation.Range(s.Chord, 0, 32000000, "chord");
        CoasterStateValidation.Require(Enum.IsDefined(s.Kind), "node kind");
        CoasterStateValidation.Require(s.P?.Length == 4 && s.S?.Length == 4 && s.Samples?.Length == 17, "cache lengths");
        CoasterStateValidation.Number(s.Length, 0); CoasterStateValidation.Number(s.VIn); CoasterStateValidation.Number(s.WIn);
        var n = new CoasterNode
        {
            CellX = s.CellX,
            CellZ = s.CellZ,
            YBase = s.YBase,
            Height = s.Height,
            Bank = s.Bank,
            Heading = s.Heading,
            HalfTurn = s.HalfTurn,
            Chord = s.Chord,
            TrackY = s.TrackY,
            LoopFlag = s.LoopFlag,
            Valid = s.Valid,
            IsStation = s.IsStation,
            Kind = s.Kind,
            Length = s.Length,
            VIn = s.VIn,
            WIn = s.WIn,
        };
        for (int i = 0; i < 4; i++) { n.P[i] = CoasterStateValidation.Vector(s.P[i]); n.S[i] = CoasterStateValidation.Vector(s.S[i]); }
        for (int i = 0; i < 17; i++) n.Samples[i] = RestoreSample(s.Samples[i]);
        return n;
    }

    static CoasterSample RestoreSample(SampleState s)
    {
        CoasterStateValidation.Require(s != null, "sample");
        CoasterStateValidation.Number(s.Len, 0);
        CoasterStateValidation.Number(s.V0); CoasterStateValidation.Number(s.V1);
        CoasterStateValidation.Number(s.W0); CoasterStateValidation.Number(s.W1);
        return new CoasterSample
        {
            Pos = CoasterStateValidation.Vector(s.Pos),
            Side = CoasterStateValidation.Vector(s.Side),
            Normal = CoasterStateValidation.Vector(s.Normal),
            Tangent = CoasterStateValidation.Vector(s.Tangent),
            Len = s.Len,
            V0 = s.V0,
            V1 = s.V1,
            W0 = s.W0,
            W1 = s.W1,
            Winch = s.Winch,
        };
    }
}
