using System.Text.Json.Serialization;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

public sealed partial class Park
{
    public const int StateVersion = 1;
    public const int MaxStateCells = 1_048_576;
    // All arrays are row-major: x + y * Width. Required properties work with default STJ options.
    // Caller must bound JSON bytes before deserialization; decoded grids are bounded below.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int[] Occupancy { get; init; }
        public required bool[] Reservations { get; init; } // null is distinct from allocated/empty
        public required bool[] Playable { get; init; }
        public required Vec2State Origin { get; init; }
        public required float BaseY { get; init; }
        public required PlotState PlotSpace { get; init; }
        public required int LastX { get; init; }
        public required int LastY { get; init; }
        public required bool HasPaths { get; init; }
        public required bool HasField { get; init; }
        public required NodeState Root { get; init; }
        public required NodeState Ground { get; init; }
        public required NodeState Ride { get; init; }
        public required AssetState Terrain { get; init; }
        public required PlacementState[] Placed { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public readonly record struct Vec2State([property: JsonRequired] float X, [property: JsonRequired] float Y);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public readonly record struct Vec3State([property: JsonRequired] float X, [property: JsonRequired] float Y,
        [property: JsonRequired] float Z);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record TransformState([property: JsonRequired] Vec3State X, [property: JsonRequired] Vec3State Y,
        [property: JsonRequired] Vec3State Z, [property: JsonRequired] Vec3State Origin);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record NodeState([property: JsonRequired] TransformState Transform,
        [property: JsonRequired] bool Visible, [property: JsonRequired] bool TopLevel);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record AssetState([property: JsonRequired] string Key, [property: JsonRequired] NodeState Node);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PlotState([property: JsonRequired] TransformState ToWorld,
        [property: JsonRequired] Vec3State LocalMin, [property: JsonRequired] Vec3State LocalSize);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record FootprintState
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required bool[] Cells { get; init; }
        public required int EntryX { get; init; }
        public required int EntryY { get; init; }
        public required int ExitX { get; init; }
        public required int ExitY { get; init; }
        public required int EntryDX { get; init; }
        public required int EntryDY { get; init; }
        public required int ExitDX { get; init; }
        public required int ExitDY { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PlacementState
    {
        public required int Id { get; init; }
        public required string Name { get; init; }
        public required int X { get; init; }
        public required int Y { get; init; }
        public required FootprintState Footprint { get; init; }
        public required AssetState Model { get; init; } // null preserves a logical-only placement
    }
    /// <summary>Not serialized. SharedPaths is the current owner on capture; on restore it must
    /// be the independently staged owner, never the old live one. Field is reused, not copied.
    /// IdentifyNode uses id 0 for terrain.
    /// BuildNode is trusted to return fresh unique unparented nodes, not cached/live instances.
    /// Ownership transfers only after ALL factory outputs pass checks; on factory/check failure
    /// all returned nodes remain untouched and factory/caller-owned (including fresh outputs).
    /// On success the returned Park owns them; it is still detached from the SceneTree.</summary>
    public sealed class StateBindings
    {
        public ParkPaths SharedPaths { get; init; }
        // The Viewer shares CELL BYTES, not necessarily the HeightField wrapper, with its
        // walking grid. A distinct render wrapper can retain its own Step value.
        public Model.HeightField SharedField { get; init; }
        internal Model.HeightField Field => SharedField ?? SharedPaths?.Field;
        public Func<int, Node3D, string> IdentifyNode { get; init; }
        public Func<string, Node3D> BuildNode { get; init; }
    }
    // External bindings, NOT state: TerrainTop (asset-derived height cache), GroundMaterial,
    // MaterialForCell, TurnsForCell, Claimed, CutFloor. Rebind these after staging all owners,
    // then call Rebuild. MaterialCount and generated ground meshes are renderer caches.
    // WallsEnabled/RaiseEnabled are process/environment configuration, not per-Park state. UvCorners/constants
    // are fixed renderer data. Asset subtree animation/material/visibility timelines belong to
    // the caller's model owner: only each owned model ROOT's actual transform, local Visible and
    // TopLevel are saved (not arbitrary Node/process/render flags or descendant animation state).
    // This owner snapshot is NOT a complete user save until Viewer and all other owners join.
    public State CaptureState(StateBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        Require(Width>=0 && Height>=0 && (long)Width*Height<=MaxStateCells
            && _placed.Count<=MaxStateCells,"capture grid/placement bounds");
        long footprintCells=0;
        foreach(var placed in _placed) footprintCells+=(long)placed.Fp.Width*placed.Fp.Height;
        Require(footprintCells>=0 && footprintCells<=MaxStateCells*16L,"capture footprint budget");
        Require(Paths == null || ReferenceEquals(Field, Paths.Field), "Paths/Field identity");
        Require(Field == null || ReferenceEquals(Field, bindings.Field), "shared capture field");
        AssetState Asset(int id, Node3D n) => n == null ? null :
            new(bindings.IdentifyNode?.Invoke(id, n), CaptureNode(n));
        var s = new State
        {
            Version = StateVersion, Width = Width, Height = Height, Occupancy = Flatten(_occupied),
            Reservations = Flatten(_reserved), Playable = Flatten(Playable), Origin = new(Origin.X, Origin.Y),
            BaseY = BaseY, PlotSpace = PlotSpace is { } p ? new(CaptureTransform(p.ToWorld), V(p.LocalMin), V(p.LocalSize)) : null,
            LastX = LastX, LastY = LastY, HasPaths = Paths != null, HasField = Field != null,
            Root = CaptureNode(Root), Ground = CaptureNode(_ground), Ride = CaptureNode(_ride), Terrain = Asset(0, _terrain),
            Placed = _placed.Select(p => new PlacementState
            {
                Id = p.Id, Name = p.Name, X = p.X, Y = p.Y, Model = Asset(p.Id, p.Node),
                Footprint = new FootprintState { Width = p.Fp.Width, Height = p.Fp.Height, Cells = Flatten(p.Fp.Cells),
                    EntryX = p.Fp.EntryX, EntryY = p.Fp.EntryY, ExitX = p.Fp.ExitX, ExitY = p.Fp.ExitY,
                    EntryDX = p.Fp.EntryDX, EntryDY = p.Fp.EntryDY, ExitDX = p.Fp.ExitDX, ExitDY = p.Fp.ExitDY }
            }).ToArray()
        };
        Validate(s, bindings);
        return s;
    }
    /// <summary>Allocation-only restore: validates the entire DTO before any factory calls.
    /// No Build, TryPlace, Remove, path mutation, or active-tree effects. Caller publishes Root
    /// only after cross-owner validation/rebinding; free Root if abandoning a successful stage.</summary>
    public static Park FromState(State s, StateBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        Validate(s, bindings);
        Require(bindings.BuildNode != null || (s.Terrain == null && s.Placed.All(p => p.Model == null)), "node factory");
        var park = new Park();
        try
        {
            park.Width = s.Width; park.Height = s.Height;
            park._occupied = Inflate(s.Occupancy, s.Width, s.Height);
            park._reserved = Inflate(s.Reservations, s.Width, s.Height); park.Playable = Inflate(s.Playable, s.Width, s.Height);
            park.Origin = new(s.Origin.X, s.Origin.Y); park.BaseY = s.BaseY;
            park.PlotSpace = s.PlotSpace is { } plot ? new Plot(T(plot.ToWorld), V(plot.LocalMin), V(plot.LocalSize)) : null;
            park.LastX = s.LastX; park.LastY = s.LastY;
            park.Paths = s.HasPaths ? bindings.SharedPaths : null; park.Field = s.HasField ? bindings.Field : null;
            var footprints = s.Placed.Select(p =>
            {
                var f = p.Footprint;
                return new Footprint(f.Width, f.Height, Inflate(f.Cells, f.Width, f.Height),
                    f.EntryX, f.EntryY, f.ExitX, f.ExitY, f.ExitDX, f.ExitDY, f.EntryDX, f.EntryDY);
            }).ToArray();
            park._placed.EnsureCapacity(s.Placed.Length);
            var assets = s.Placed.Where(p => p.Model != null).Select(p => p.Model).ToList();
            if (s.Terrain != null) assets.Add(s.Terrain);
            var nodes = new List<Node3D>(); var ids = new HashSet<ulong>();
            foreach (var asset in assets)
            {
                var n = bindings.BuildNode(asset.Key);
                Require(GodotObject.IsInstanceValid(n) && !n.IsQueuedForDeletion() && !n.IsInsideTree()
                    && n.GetParent() == null && n.Owner == null && ids.Add(n.GetInstanceId()), "factory node must be fresh/unique/unparented");
                nodes.Add(n);
            }
            // Commit node ownership only now. Any subsequent error frees only our staged tree.
            int at = 0, placement = 0;
            foreach (var p in s.Placed)
            {
                Node3D n = p.Model == null ? null : nodes[at++];
                if (n != null) park._ride.AddChild(n);
                park._placed.Add((p.Id, p.Name, footprints[placement++], p.X, p.Y, n));
            }
            if (s.Terrain != null) { park._terrain = nodes[at]; park.Root.AddChild(park._terrain); }
            for (int i = 0; i < nodes.Count; i++) ApplyNode(nodes[i], assets[i].Node);
            ApplyNode(park.Root, s.Root); ApplyNode(park._ground, s.Ground); ApplyNode(park._ride, s.Ride);
            return park;
        }
        catch { park.Root.Free(); throw; }
    }
    static void Require(bool ok, string reason)
    { if (!ok) throw new ArgumentException("Invalid Park state: " + reason); }
    static void Validate(State s, StateBindings b)
    {
        ArgumentNullException.ThrowIfNull(s);
        Require(s.Version == StateVersion, "version");
        Require(s.Width >= 0 && s.Height >= 0 && s.Width <= 4096 && s.Height <= 4096
            && (long)s.Width * s.Height <= MaxStateCells && ((s.Width == 0) == (s.Height == 0)), "dimensions");
        int count = s.Width * s.Height;
        Require(s.Occupancy != null && s.Occupancy.Length == count, "occupancy length");
        Require((s.Reservations == null || s.Reservations.Length == count) && (s.Playable == null || s.Playable.Length == count), "mask length");
        Require(float.IsFinite(s.Origin.X) && float.IsFinite(s.Origin.Y) && float.IsFinite(s.BaseY), "coordinates");
        Require(s.LastX >= 0 && s.LastY >= 0, "last placement coordinates"); // may be stale after Build
        Require(!s.HasPaths || s.HasField, "paths without field");
        Require(!s.HasField || (b.Field != null && b.Field.Width == s.Width && b.Field.Height == s.Height), "shared field dimensions/binding");
        Require(!s.HasPaths || (b.SharedPaths != null && ReferenceEquals(b.Field,b.SharedPaths.Field)), "Paths/Field binding");
        CheckNode(s.Root); CheckNode(s.Ground); CheckNode(s.Ride); CheckAsset(s.Terrain);
        if (s.PlotSpace is { } plot)
        {
            CheckTransform(plot.ToWorld); CheckVector(plot.LocalMin); CheckVector(plot.LocalSize);
            Require(plot.LocalSize.X > 0 && plot.LocalSize.Z > 0 && plot.LocalSize.Y >= 0, "plot size");
        }
        Require(s.Placed != null && s.Placed.Length <= MaxStateCells, "placed list");
        var ids = new HashSet<int>(); var expected = new int[count]; long total = 0;
        foreach (var p in s.Placed)
        {
            Require(p != null && p.Id != 0 && ids.Add(p.Id), "duplicate/zero placement id");
            Require(p.Name == null || p.Name.Length <= 4096, "name length"); CheckAsset(p.Model);
            var f = p.Footprint;
            Require(f != null && f.Width > 0 && f.Height > 0 && f.Width <= s.Width && f.Height <= s.Height
                && p.X >= 0 && p.Y >= 0 && (long)p.X + f.Width <= s.Width && (long)p.Y + f.Height <= s.Height, "footprint bounds");
            total += (long)f.Width * f.Height;
            Require(total <= MaxStateCells * 16L && f.Cells != null && f.Cells.Length == (long)f.Width * f.Height, "footprint cells/budget");
            void Door(int x, int y, int dx, int dy)
            {
                Require(dx >= -1 && dx <= 1 && dy >= -1 && dy <= 1 && Math.Abs(dx) + Math.Abs(dy) <= 1, "door facing");
                Require((x == -1 && y == -1 && dx == 0 && dy == 0) ||
                    (x >= 0 && y >= 0 && x < f.Width && y < f.Height && f.Cells[x + y * f.Width]), "door cell");
            }
            Door(f.EntryX, f.EntryY, f.EntryDX, f.EntryDY); Door(f.ExitX, f.ExitY, f.ExitDX, f.ExitDY);
            for (int y = 0; y < f.Height; y++) for (int x = 0; x < f.Width; x++)
            {
                if (!f.Cells[x + y * f.Width]) continue;
                int index = p.X + x + (p.Y + y) * s.Width;
                Require(expected[index] == 0, "overlapping footprints"); expected[index] = p.Id;
            }
        }
        Require(expected.SequenceEqual(s.Occupancy), "occupancy/placement mismatch");
    }
    static void CheckAsset(AssetState a)
    { if (a == null) return; Require(!string.IsNullOrWhiteSpace(a.Key) && a.Key.Length <= 1024, "asset key"); CheckNode(a.Node); }
    static void CheckNode(NodeState n) { Require(n != null, "node state"); CheckTransform(n.Transform); }
    static void CheckVector(Vec3State v) => Require(float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z), "nonfinite vector");
    static void CheckTransform(TransformState t)
    {
        Require(t != null, "transform"); CheckVector(t.X); CheckVector(t.Y); CheckVector(t.Z); CheckVector(t.Origin);
        float determinant = T(t).Basis.Determinant();
        Require(float.IsFinite(determinant) && determinant != 0, "singular/overflow transform");
    }
    static Vec3State V(Vector3 v) => new(v.X, v.Y, v.Z);
    static Vector3 V(Vec3State v) => new(v.X, v.Y, v.Z);
    static TransformState CaptureTransform(Transform3D t) => new(V(t.Basis.X), V(t.Basis.Y), V(t.Basis.Z), V(t.Origin));
    static Transform3D T(TransformState t) => new(new Basis(V(t.X), V(t.Y), V(t.Z)), V(t.Origin));
    static NodeState CaptureNode(Node3D n) => new(CaptureTransform(n.Transform), n.Visible, n.TopLevel);
    static void ApplyNode(Node3D n, NodeState s) { n.TopLevel = s.TopLevel; n.Transform = T(s.Transform); n.Visible = s.Visible; }
    static TCell[] Flatten<TCell>(TCell[,] a)
    {
        if (a == null) return null;
        int w = a.GetLength(0), h = a.GetLength(1); var cells = new TCell[checked(w * h)];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) cells[x + y * w] = a[x, y];
        return cells;
    }
    static TCell[,] Inflate<TCell>(TCell[] a, int w, int h)
    {
        if (a == null) return null;
        var cells = new TCell[w, h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) cells[x, y] = a[x + y * w];
        return cells;
    }
}
