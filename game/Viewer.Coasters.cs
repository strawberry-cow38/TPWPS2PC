using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

public partial class Viewer
{
    /// <summary>⭐⭐ ROLLER COASTERS: the station the player placed, the ring of pylons drawn from it,
    /// the track swept along the spline and the trains that run it. The rules are in core
    /// (<see cref="CoasterTrack"/>, <see cref="CoasterSim"/>, <see cref="CoasterMesh"/>,
    /// findings/coasters.md); this file draws them and runs the tool.
    ///
    /// ⭐ One frame, the track rides' (<see cref="PlaceFrame"/>): the console's park coordinates in, the
    /// plot out, one unit a cell. The spline is already in cells, so its points go in as they are.</summary>
    sealed class CoasterView
    {
        public int Id;
        public ParkRide Ride;
        public CoasterSim Sim;
        public CoasterTrack Track;
        public string Dir;
        /// <summary>DBA +0xd0, charged ×10 a pylon.</summary>
        public int Price;
        public Node3D Frame;
        public readonly Dictionary<CoasterNode, MeshInstance3D> Segments = new();
        public readonly Dictionary<CoasterNode, Node3D> Pylons = new();
        public readonly Dictionary<CoasterCar, CoasterCarView> Cars = new();
        public readonly HashSet<(int X, int Y)> Cells = new();
        public Material[] Slots, Red;
        public int WinchSeen = -1;
        public long SeenTime = -1;
        /// <summary>Sound voices started for this coaster's trains, so a gone train's are stopped.</summary>
        public readonly HashSet<int> Voices = new();
        /// <summary>The last test lap's record, for the stats screen.</summary>
        public CoasterStats Stats = CoasterStats.None;
    }

    /// <summary>A drawn car: its node, the model its riders sit on, and its last two tick poses.</summary>
    sealed class CoasterCarView
    {
        public Node3D Node;
        public AnimatedModel Model;
        public Model Mesh;
        public Vector3 Was, Now;
        public Basis WasB, NowB;
    }

    enum CoasterMode { Build, Edit }

    readonly Dictionary<int, CoasterView> _coasters = new();
    CoasterView _coasterTool;
    CoasterMode _coasterMode;
    CoasterNode _coasterGhost;
    (int X, int Y) _coasterGhostAt = (int.MinValue, 0);
    bool _coasterGhostOk, _coasterFromStation;
    int _coasterStartHeight, _coasterStartBank, _coasterPick;
    double _coasterHold;
    System.Action _afterCoaster;
    readonly Dictionary<int, float> _placedHeight = new();

    bool IsCoaster(AssetLibrary.RideAssets r) => r != null && BuildKind(r) == AssetResourceDatabase.AssetKind.Coaster;

    /// <summary>The park as the placement rules see it (`0x1216d8`).</summary>
    sealed class CoasterGround : CoasterTrack.IGround
    {
        readonly Viewer _v;
        public CoasterGround(Viewer v) => _v = v;
        public bool InGrid(ParkCell c) => c.X >= 0 && c.Z >= 0 && c.X < _v._park.Width - 1 && c.Z < _v._park.Height - 1;
        public bool EmptyLand(ParkCell c) =>
            _v._park.IsPlayable(c.X, c.Z) && _v._park.Vacant(c.X, c.Z)
            && (_v._paths?.KindAt(c.X, c.Z) ?? PathTool.Kind.None) == PathTool.Kind.None;
        public CoasterNode CoasterNodeAt(ParkCell c) =>
            _v._coasters.Values.Select(v => v.Track.BottomAt(c)).FirstOrDefault(n => n != null);
        /// <summary>A placed thing's drawn height over the cell, in cells. ⚠ The console reads a model
        /// field (`*(model+0xc)+0x64`) whose units were not settled; the drawn top stands in for it.</summary>
        public float Clearance(ParkCell c, CoasterTrack own)
        {
            if (_v._park.PlacedAt(c.X, c.Z) is not { } hit) return 0;
            if (_v._coasters.TryGetValue(hit.Id, out var mine) && mine.Track == own) return 0;
            if (!_v._placedHeight.TryGetValue(hit.Id, out float h))
            {
                var (_, hi) = Park.DrawnBounds(hit.Node);
                h = Math.Max(0, (hi.Y - _v._park.BaseY) / Park.CellSize);
                _v._placedHeight[hit.Id] = h;
            }
            return h;
        }
    }

    CoasterGround _coasterGround;
    CoasterGround Ground => _coasterGround ??= new CoasterGround(this);

    /// <summary>⭐ Where the station's track leaves and returns, in park cells. DBA `+0xbc`/`+0xc0` are
    /// local cells in the console's own frame, and `+0xd3` bits 4–5 / 6–7 the step out (0 = z−1,
    /// 1 = x−1, 2 = z+1, 3 = x+1, coaster-building.md §6.2). The port's <see cref="Placement.Base"/> IS
    /// that frame -- Arm mirrors the `.sam` rows into it -- so the cells go through the port's own
    /// quarter turns exactly as the doors do, and land beside the station model it drew.</summary>
    static (ParkCell Exit, int ExitStep, ParkCell Entry) StationLink(ReadOnlyMemory<byte> payload, int cx, int cy, int turns, int w, int h)
    {
        var p = payload.Span;
        (int, int) Step(int d) => (d & 3) switch { 0 => (0, -1), 1 => (-1, 0), 2 => (0, 1), _ => (1, 0) };
        int ex = BitConverter.ToInt16(p[0xbc..]), ez = BitConverter.ToInt16(p[0xbe..]);
        int nx = BitConverter.ToInt16(p[0xc0..]), nz = BitConverter.ToInt16(p[0xc2..]);
        int dExit = (p[0xd3] >> 4) & 3, dEntry = (p[0xd3] >> 6) & 3;
        var (edx, edz) = Step(dExit); var (ndx, ndz) = Step(dEntry);
        for (int t = 0; t < (turns & 3); t++)
        {
            (ex, ez) = (h - 1 - ez, ex); (nx, nz) = (h - 1 - nz, nx);
            (edx, edz) = (-edz, edx); (ndx, ndz) = (-ndz, ndx);
            (w, h) = (h, w);
        }
        int stepCode = (edx, edz) switch { (0, -1) => 0, (-1, 0) => 1, (0, 1) => 2, _ => 3 };
        return (new ParkCell(cx + ex + edx, cy + ez + edz), stepCode, new ParkCell(cx + nx + ndx, cy + nz + ndz));
    }

    /// <summary>⭐ The station preview's coaster-only part (`0x1e3978`, coaster-building.md §6.3): the
    /// track exit and entry cells must be empty land, drawn as 166 with the exit's chevron pointing
    /// out and the entry's pointing in. ⚠ A cell off the grid is SKIPPED, not refused, as on the
    /// console. Empty for anything that is not a coaster.</summary>
    IEnumerable<(ParkCell Cell, int Turn, bool Ok)> CoasterLinkPreview(int cursorX, int cursorY)
    {
        if (!_place.Active || !IsCoaster(_armedRide)) yield break;
        var payload = DefinitionFor(_armedRide.Model)?.CompiledEntry?.Payload;
        if (payload is not { Length: >= 0xd4 } pl) yield break;
        var (cx, cy) = _place.CornerFor(cursorX, cursorY);
        var (exit, step, entry) = StationLink(pl, cx, cy, _place.Turns, _place.Base.Width, _place.Base.Height);
        var (fx, fz) = (step & 3) switch { 0 => (0, -1), 1 => (-1, 0), 2 => (0, 1), _ => (1, 0) };
        foreach (var (c, dx, dz) in new[] { (exit, fx, fz), (entry, fx, fz) })
        {
            if (!Ground.InGrid(c)) continue;
            yield return (c, GhostMarkers.TurnToward(dx, dz), Ground.EmptyLand(c) && Ground.CoasterNodeAt(c) == null);
        }
    }

    /// <summary>Called by PlaceHeld once a coaster station is down: its two station nodes
    /// (`0x122060`), its trains, and a frame to draw them in.</summary>
    bool BeginCoaster(int id, AssetLibrary.RideAssets station, int cx, int cy, int turns, Park.Footprint baseFp)
    {
        var ride = _sim?.Rides.FirstOrDefault(r => r.Id == id);
        if (ride == null || station?.Model == null) return false;
        string dir = station.Model.Path[..(station.Model.Path.LastIndexOf('/') + 1)];
        string folder = dir.TrimEnd('/'); folder = folder[(folder.LastIndexOf('/') + 1)..];
        var type = CoasterType.ForFolder(folder);
        var payload = ride.Definition?.CompiledEntry?.Payload;
        if (type == null || payload is not { Length: >= 0xd4 } pl)
        {
            GD.PrintErr($"[coaster] {station.Name}: {(type == null ? $"folder {folder} is not one of the 14" : "no compiled record")} -- no track");
            return false;
        }
        var (exit, exitStep, entry) = StationLink(pl, cx, cy, turns, baseFp.Width, baseFp.Height);
        // A ground pylon's base is the floor under its cell. ⚠ The console's is tile byte 1 × 4 units
        // (0x149d90); the port draws its floor from the field's raise bit instead, and a pylon has
        // to stand on the floor that is drawn.
        var track = new CoasterTrack(type, exit, exitStep, entry)
        {
            GroundY = (x, z) => (int)MathF.Round((_park.CellY(x, z) - _park.BaseY) / Park.CellSize * 256f),
        };
        var sim = _sim.AttachCoaster(id, track);
        var frame = new Node3D { Name = $"Coaster_{id}" };
        AddChild(frame);
        var view = new CoasterView
        {
            Id = id, Ride = ride, Sim = sim, Track = track, Dir = dir, Frame = frame,
            Price = BitConverter.ToUInt16(pl.Span[0xd0..]),
        };
        _coasters[id] = view;
        ClaimFloor();
        var o = _park.CellCorner(0, 0);
        var ax = _park.CellCorner(1, 0) - o;
        var az = _park.CellCorner(0, 1) - o;
        frame.Transform = new Transform3D(new Basis(ax, new Vector3(0, ax.Length(), 0), az), new Vector3(o.X, _park.BaseY, o.Z));
        LoadCoasterMaterials(view);
        RebuildCoaster(view);
        sim.Scream += (tr, evt) => CoasterScream(view, tr, evt);
        GD.Print($"[coaster] {type.Name}: station at ({cx},{cy}) turned {turns}, track exit {exit} step {exitStep}, entry {entry}, "
               + $"heights {type.ExitHeight}/{type.EntryHeight}, style {type.Style}, {type.CarsPerTrain} car(s) of {type.Seats}, {view.Price} a pylon");
        return true;
    }

    /// <summary>Track-ride cells and coaster pylon cells both keep their floor and refuse building.</summary>
    void ClaimFloor() =>
        _park.Claimed = (x, y) => _tracks.Values.Any(v => v.Cells.Contains((x, y))) || _coasters.Values.Any(v => v.Cells.Contains((x, y)));

    /// <summary>The style's three texture slots from the coaster's `GTexture/`, and `red.ssh` for an
    /// invalid segment. Every strip is double-sided (fn0 sets `+0xc |= 1` on all of them).</summary>
    void LoadCoasterMaterials(CoasterView v)
    {
        string owner = v.Dir + "GTexture/track.mps";
        Material Make(string name, bool scroll)
        {
            if (name == null) return null;
            var (tex, soft) = TextureNear(owner, name);
            // φ −= 0.08 a coaster update, added to V: forward along the track at 0.08 V an update.
            var m = Ps2Materials.Animated(tex, soft, "cull_disabled", scroll ? new Vector2(0, -0.08f * 25f) : Vector2.Zero, 0f, Vector3.Zero);
            return m;
        }
        var t = v.Track.Type;
        var names = new[] { t.Tex0, t.Tex1, t.Tex2 };
        v.Slots = new Material[6];
        v.Red = new Material[6];
        for (int i = 0; i < 3; i++)
        {
            v.Slots[i] = Make(names[i], false);
            v.Slots[i + 3] = Make(names[i], true);
            v.Red[i] = Make("red", false);
            v.Red[i + 3] = Make("red", true);
        }
    }

    /// <summary>Everything drawn again after the track changed: each segment's mesh, each pylon, and
    /// the cells the pylons claim.</summary>
    void RebuildCoaster(CoasterView v)
    {
        foreach (var m in v.Segments.Values) if (IsInstanceValid(m)) m.QueueFree();
        v.Segments.Clear();
        foreach (var p in v.Pylons.Values) if (IsInstanceValid(p)) p.QueueFree();
        v.Pylons.Clear();
        v.Cells.Clear();
        var t = v.Track;
        foreach (var n in t.Nodes().Concat(v == _coasterTool && _coasterGhost != null ? new[] { _coasterGhost } : Array.Empty<CoasterNode>()))
        {
            bool ghost = n == _coasterGhost;
            if (!n.IsStation && !ghost) v.Cells.Add((n.CellX, n.CellZ));
            // The ghost's own pylon and track are forced invisible (0x19d1e0); the entry's segment
            // only exists once the ring is closed.
            if (ghost || n == t.Entry && !t.Closed) continue;
            if (CoasterMesh.Visible(t, n) && CoasterSegment(v, n) is { } seg) { v.Frame.AddChild(seg); v.Segments[n] = seg; }
            if (!n.IsStation && CoasterPylon(v, n) is { } py) { v.Frame.AddChild(py); v.Pylons[n] = py; }
        }
        v.WinchSeen = v.Sim.WinchVersion;
        RefreshFloor();
    }

    MeshInstance3D CoasterSegment(CoasterView v, CoasterNode n)
    {
        var quads = CoasterMesh.Build(n, v.Track.Type.Style);
        if (quads.Count == 0) return null;
        var mesh = new ArrayMesh();
        foreach (var group in quads.GroupBy(q => q.Slot + (q.Scroll ? 3 : 0)).OrderBy(g => g.Key))
        {
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            foreach (var q in group)
            {
                void V(System.Numerics.Vector3 p, System.Numerics.Vector2 uv)
                {
                    st.SetNormal(Vector3.Up);
                    st.SetUV(new Vector2(uv.X, uv.Y));
                    st.AddVertex(new Vector3(p.X, p.Y, p.Z));
                }
                V(q.C0, q.T0); V(q.C1, q.T1); V(q.C2, q.T2);
                V(q.C1, q.T1); V(q.C2, q.T2); V(q.C3, q.T3);
            }
            st.Commit(mesh);
            var mat = (n.Valid ? v.Slots : v.Red)[group.Key];
            if (mat != null) mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, mat);
        }
        return new MeshInstance3D { Mesh = mesh, Name = $"seg_{n.CellX}_{n.CellZ}" };
    }

    /// <summary>The pylon (`stdpylon.mps`), posed as `0x19cdd0` poses it, on all four of its channels:
    /// loft = `clamp(h/2560, 0, 1)` along `.aps` section 3, yaw = heading + half-turn (section 10),
    /// incline held at 0.5 (section 2) and bank = `(bank + 512) / 1024` (section 9, absent on some).
    /// A channel's frame is value × the record's duration (`0x1ad2d8`; the length is the record's
    /// `+4` as a float, the time clamped 0.0001 under it). Placed at the cell's min corner at its base
    /// height; the mesh is authored centred on the cell. ⚠ The yaw channel turns the whole post, so it
    /// is applied to the holder rather than through a record. ⭐ Incline and bank are NOT idle at
    /// neutral: they morph nothing there but add to the post's UVs, and the bank channel tilts the
    /// post's top when the track banks (<see cref="AnimatedModel.AddLayer"/>).</summary>
    Node3D CoasterPylon(CoasterView v, CoasterNode n)
    {
        string folder = v.Track.Type.PylonFolder;
        var assets = _lib.Rides.FirstOrDefault(r => r.Model != null
            && r.Model.Path.EndsWith($"/{folder}/stdpylon.mps", StringComparison.OrdinalIgnoreCase));
        if (assets?.Model == null) return null;
        try
        {
            var mesh = new Model(_lib.Read(assets.Model));
            Aps anim = assets.Animation != null ? new Aps(_lib.Read(assets.Animation)) : null;
            var rec = anim?.Records().FirstOrDefault(r => r.Slot == 3);
            var drawn = new AnimatedModel(mesh, anim, rec, m => TextureNear(assets.Model.Path, m));
            static float At(Aps.Record r, float value) => Math.Min(value * r.DurationFrames, r.DurationFrames - 0.0001f);
            if (anim?.Records().FirstOrDefault(r => r.Slot == 2) is { } incline) drawn.AddLayer(incline, At(incline, 0.5f));
            if (anim?.Records().FirstOrDefault(r => r.Slot == 9) is { } bank) drawn.AddLayer(bank, At(bank, (n.Bank + 512) / 1024f));
            float loft = Math.Clamp(n.Height / 2560f, 0f, 1f);
            if (rec != null) drawn.SetFrame(At(rec, loft));
            // ⭐ `0x199c90`: the stacker is the node of fitting (0x80000, id 1) by the ENGINE's rule,
            // fitting index + header u16 @0x34 (not the port's meshes + index), and it carries hide
            // flag 0x8000 unless a pylon is stacked on this one. ⚠ The same function hides the
            // record at instance+0xc → +0x70 when this pylon stands on another; that it is the
            // first mesh (the post) is INFERRED.
            var hidden = new HashSet<string>();
            void Hide(string meshName) { hidden.Add(meshName); foreach (var (m, _, node) in drawn.Surfaces()) if (m == meshName) node.Visible = false; }
            if (mesh.FindFitting(1, 0x80000) is { } fit)
            {
                int engineNode = fit.Node - mesh.Meshes.Count + BitConverter.ToUInt16(mesh.D, 0x34);
                if (n.Above == null && engineNode >= 0 && engineNode < mesh.Meshes.Count) Hide(mesh.Meshes[engineNode].Name);
            }
            if (n.Below != null && mesh.Meshes.Count > 0) Hide(mesh.Meshes[0].Name);
            drawn.Root.Scale = Vector3.One;
            var holder = new Node3D { Name = $"pylon_{n.CellX}_{n.CellZ}" };
            // The visible parts' top at REST (bind pose, loft 0), from the .mps bounds: what the loft
            // adds its 9 cells a full loft to. Kept for the checks.
            var bindWorld = mesh.WorldTransforms();
            float restTop = float.NegativeInfinity;
            foreach (var part in mesh.Meshes)
            {
                if (hidden.Contains(part.Name) || !bindWorld.TryGetValue(part.Offset, out var bw)) continue;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new System.Numerics.Vector3((c & 1) != 0 ? part.BoundsMax.X : part.BoundsMin.X,
                                                             (c & 2) != 0 ? part.BoundsMax.Y : part.BoundsMin.Y,
                                                             (c & 4) != 0 ? part.BoundsMax.Z : part.BoundsMin.Z);
                    restTop = Math.Max(restTop, System.Numerics.Vector3.Transform(corner, bw).Y);
                }
            }
            if (!float.IsInfinity(restTop)) holder.SetMeta("rest_top", restTop);
            // The posed track dummy (fitting 0x400000 id 2, else id 1, by the engine's node rule, the
            // one 0x19a420 reads), kept for the checks: the post is authored to meet it.
            if ((mesh.FindFitting(2, 0x400000) ?? mesh.FindFitting(1, 0x400000)) is { } dummyFit && drawn.LastWorld != null)
            {
                int node = dummyFit.Node - mesh.Meshes.Count + BitConverter.ToUInt16(mesh.D, 0x34);
                if (drawn.LastWorld.TryGetValue(mesh.NodeOffset(node), out var w))
                    holder.SetMeta("track_dummy", new Vector3(w.M41, w.M42, w.M43));
            }
            holder.AddChild(drawn.Root);
            float yaw = (n.Heading + n.HalfTurn) * Mathf.Tau / 4096f;
            // Turned about the cell centre, where the post stands.
            var centre = new Vector3(n.X / 256f, n.YBase / 256f, n.Z / 256f);
            // Section 10's key at 25 % is +90° about +Y, taking the model's +Z to +X: heading 0x400.
            var basis = new Basis(Vector3.Up, yaw);
            holder.Transform = new Transform3D(basis, centre - basis * new Vector3(0.5f, 0, 0.5f));
            return holder;
        }
        catch (Exception e) { GD.PrintErr($"[coaster] pylon {folder}: {e.Message}"); return null; }
    }

    /// <summary>Per rendered frame: the cars, between the last two ticks, and the meshes again when
    /// the lift marks changed (a fresh spawn re-marks them).</summary>
    void PresentCoasters(float alpha)
    {
        foreach (var v in _coasters.Values)
        {
            if (v.WinchSeen != v.Sim.WinchVersion) RebuildCoaster(v);
            bool ticked = _sim != null && _sim.Time != v.SeenTime;
            if (ticked) v.SeenTime = _sim.Time;
            var live = v.Sim.Trains.SelectMany(t => t.Cars).ToHashSet();
            foreach (var gone in v.Cars.Keys.Where(c => !live.Contains(c)).ToList())
            {
                if (IsInstanceValid(v.Cars[gone].Node)) v.Cars[gone].Node.QueueFree();
                v.Cars.Remove(gone);
            }
            if (ticked) CoasterRumble(v);
            foreach (var car in live)
            {
                var now = new Vector3(car.Pos.X, car.Pos.Y, car.Pos.Z);
                var b = new Basis(new Vector3(car.Side.X, car.Side.Y, car.Side.Z), new Vector3(car.Up.X, car.Up.Y, car.Up.Z),
                                  new Vector3(car.Fwd.X, car.Fwd.Y, car.Fwd.Z));
                if (!v.Cars.TryGetValue(car, out var st))
                {
                    st = CoasterCarModel(v) ?? new CoasterCarView { Node = new Node3D() };
                    v.Frame.AddChild(st.Node);
                    st.Was = st.Now = now; st.WasB = st.NowB = b;
                    v.Cars[car] = st;
                }
                else if (ticked) { st.Was = st.Now; st.Now = now; st.WasB = st.NowB; st.NowB = b; }
                // Render position is quantised to 1/256 cell by the console (vt+0x74); the port draws
                // between ticks, as it does the track rides' cars.
                var q = st.WasB.GetRotationQuaternion().Slerp(st.NowB.GetRotationQuaternion(), alpha);
                st.Node.Transform = new Transform3D(new Basis(q), st.Was.Lerp(st.Now, alpha));
            }
        }
    }

    CoasterCarView CoasterCarModel(CoasterView v)
    {
        string stem = v.Track.Type.CarModel;
        var assets = _lib.Rides.FirstOrDefault(r => r.Model != null
            && r.Model.Path.Equals(v.Dir + stem + ".mps", StringComparison.OrdinalIgnoreCase));
        var built = LoadPlaceable(assets, out _, out var mesh);
        if (built?.Root == null) return null;
        built.Root.Scale = Vector3.One;
        var holder = new Node3D { Name = stem };
        holder.AddChild(built.Root);
        return new CoasterCarView { Node = holder, Model = built, Mesh = mesh };
    }

    /// <summary>⭐ Riders in the cars (`0x17d3a0`): the rider in seat s sits on the car's fitting of
    /// id s + 1 in space 0x80. A missing id seats nobody, which is Caterpillar's fourth rider (its
    /// seat ids are 1, 3, 3, 2). ⚠ The console detaches the HIGHEST occupied seat as the FIRST rider
    /// leaves (`0x17d428`), so mid-unload a departed rider's head can linger; here each rider sits in
    /// its own current slot.</summary>
    void SeatCoasterRiders()
    {
        foreach (var v in _coasters.Values)
            foreach (var car in v.Sim.Trains.SelectMany(t => t.Cars))
            {
                if (car.Riders.Count == 0 || !v.Cars.TryGetValue(car, out var cv)) continue;
                if (cv.Mesh == null || cv.Model?.Root == null || !IsInstanceValid(cv.Model.Root) || cv.Model.LastWorld == null) continue;
                for (int s = 0; s < car.Riders.Count; s++)
                {
                    if (cv.Mesh.FindFitting(s + 1, 0x80) is not { Node: >= 0 } fit) continue;
                    if (!SeatPose(cv.Mesh, cv.Model, cv.Model.Root.GlobalTransform, fit, out var pose, out var forward, out _)) continue;
                    _seated[car.Riders[s]] = (pose, $"{v.Track.Type.Name} seat {s + 1} on {cv.Mesh.NodeName(fit.Node)}", forward, "car", float.NaN);
                }
            }
    }

    void RemoveCoasterView(int id)
    {
        if (!_coasters.Remove(id, out var v)) return;
        foreach (int voice in v.Voices) { _sounds?.Kill(voice, "coaster", RumbleTag, (long)_busElapsedMs); _sounds?.Follow(voice, RumbleTag, null); }
        if (_coasterTool == v) { _coasterTool = null; _afterCoaster = null; _coasterGhost = null; _ghostView?.Clear(); }
        v.Sim.RemoveTrains();
        if (IsInstanceValid(v.Frame)) v.Frame.QueueFree();
        RefreshFloor();
    }

    void ClearCoasterViews()
    {
        foreach (var id in _coasters.Keys.ToList()) RemoveCoasterView(id);
    }

    // ------------------------------------------------------------------ sounds

    const int RumbleTag = 0x6000;

    /// <summary>A voice id per TRAIN, because the rumble's parameters are the train's own and
    /// <see cref="RideSounds.ParameterValue"/> asks by voice owner. Clear of every ride serial.</summary>
    static int CoasterVoice(CoasterView v, CoasterTrain tr) => 0x40000000 | (v.Id << 4) | (tr.Index & 15);

    RideSounds _coasterChainedOn;

    /// <summary>Parameters 6, 7 and 8 of a train's rumble: splash, clip band, speed.</summary>
    void ChainCoasterParameters()
    {
        if (_sounds == null || ReferenceEquals(_coasterChainedOn, _sounds)) return;
        _coasterChainedOn = _sounds;
        var previous = _sounds.ParameterValue;
        _sounds.ParameterValue = (ride, parameter) =>
        {
            if ((ride & 0x40000000) == 0) return previous?.Invoke(ride, parameter) ?? 0;
            int id = (ride & 0x3fffffff) >> 4, index = ride & 15;
            var tr = _coasters.TryGetValue(id, out var v) ? v.Sim.Trains.FirstOrDefault(t => t.Index == index) : null;
            return tr == null ? 0 : parameter switch { 7 => tr.RumbleCode, 6 => tr.Splash, 8 => tr.SpeedCode, _ => 0 };
        };
    }

    /// <summary>The rumble (`+0x260`): category 4 event 0x11 at each train's car 0, started again
    /// whenever it is not playing (`0x1af858`), its clip band chosen by parameter 7. ⚠ Family 1 (the
    /// three troughs and Dare Devil) asks category 5 for event 0x11, which WTRSFX.MAP does not hold, so
    /// on the console those trains rumble silently; they do here too.</summary>
    void CoasterRumble(CoasterView v)
    {
        _sounds ??= MakeSounds();
        if (_sounds == null) return;
        ChainCoasterParameters();
        var alive = new HashSet<int>();
        foreach (var tr in v.Sim.Trains)
        {
            int voice = CoasterVoice(v, tr);
            alive.Add(voice);
            if (v.Track.Type.SoundFamily == 1) continue;
            if (!v.Cars.TryGetValue(tr.Cars[0], out var cv) || !IsInstanceValid(cv.Node)) continue;
            if (v.Voices.Add(voice))
            {
                var node = cv.Node;
                _sounds.Follow(voice, RumbleTag, () => IsInstanceValid(node) ? node.GlobalPosition : null);
            }
            // ⚠ The handle, not the clip: the rumble is a persistent object (ADDOBJ, like the bus's
            // loop) whose graph walks its 32 sets by parameter 7; cued as a plain EVENT it replayed
            // set 0 (Whir01) forever. It is started again only once its graph has actually stopped.
            if (!_sounds.Running(voice, RumbleTag) && !_sounds.Sounding(voice, RumbleTag))
                _sounds.Cue(voice, "coaster", (long)_busElapsedMs, RseOpcode.ADDOBJ, (int)SoundGroup.NativeRidesGrc,
                            -1, 0x11, RumbleTag, cv.Node.GlobalPosition);
        }
        foreach (int gone in v.Voices.Where(x => !alive.Contains(x)).ToList())
        {
            _sounds.Kill(gone, "coaster", RumbleTag, (long)_busElapsedMs);
            _sounds.Follow(gone, RumbleTag, null);
            v.Voices.Remove(gone);
        }
    }

    /// <summary>A scream from the sim (category 7, the kids map) at the train's car 0.</summary>
    void CoasterScream(CoasterView v, CoasterTrain tr, int evt)
    {
        _sounds ??= MakeSounds();
        if (_sounds == null || !v.Cars.TryGetValue(tr.Cars[0], out var cv) || !IsInstanceValid(cv.Node)) return;
        _sounds.Cue(CoasterVoice(v, tr), "coaster", (long)_busElapsedMs, RseOpcode.EVENT, (int)SoundGroup.GlobalKids,
                    -1, evt, RumbleTag + 0x100 + evt, cv.Node.GlobalPosition);
    }

    // ------------------------------------------------------------------ the tool

    /// <summary>Mode 12 (`0x11ad60`): reopen a closed ring, and start every pylon laid this session
    /// at the start node's height and bank -- on a fresh track, the station exit's table height.</summary>
    void OpenCoasterTool(CoasterView v, System.Action after, bool fromStation)
    {
        _coasterTool = v;
        _afterCoaster = after;
        _coasterFromStation = fromStation;
        _coasterMode = CoasterMode.Build;
        v.Sim.RemoveTrains();
        CoasterNode start;
        if (v.Track.Closed) { v.Track.Reopen(); start = v.Track.Entry; }
        else start = v.Track.Last;
        _coasterStartHeight = start.Height;
        _coasterStartBank = start.Bank;
        _coasterGhost = null;
        _coasterGhostAt = (int.MinValue, 0);
        RebuildCoaster(v);
        LookAtCell(v.Track.Exit.CellX, v.Track.Exit.CellZ, null);
        GD.Print($"[coaster] tool open for ride {v.Id}: start height {_coasterStartHeight} bank {_coasterStartBank}");
    }

    /// <summary>The ride list box's "Edit Track" (`0x1240e8` → mode 12) and "Edit Pylons" (`0x124118`
    /// → mode 13).</summary>
    void EditCoaster(int placed, bool pylons)
    {
        if (placed < 0 || placed >= _park.Placed.Count || !_coasters.TryGetValue(_park.Placed[placed].Id, out var v)) return;
        if (!pylons) { OpenCoasterTool(v, null, false); return; }
        _coasterTool = v; _afterCoaster = null; _coasterFromStation = false;
        v.Sim.RemoveTrains();
        EnterPylonEdit(v);
    }

    void EnterPylonEdit(CoasterView v)
    {
        _coasterMode = CoasterMode.Edit;
        if (_coasterGhost != null) { v.Track.UnlinkGhost(_coasterGhost); _coasterGhost = null; }
        _coasterPick = v.Track.Pylons.ToList().FindIndex(n => n.Kind == CoasterNodeKind.Normal);
        _ghostView?.Clear();
        RebuildCoaster(v);
        ShowPylonPick();
    }

    void FinishCoasterTool()
    {
        var v = _coasterTool;
        if (v == null) return;
        if (_coasterGhost != null) { v.Track.UnlinkGhost(_coasterGhost); _coasterGhost = null; }
        _coasterTool = null;
        _ghostView?.Clear();
        RebuildCoaster(v);
        // Triangle (0x11ba00 / 0x11bbd8): the advisor's voice for an open (204) or invalid (203) ring,
        // then the test lap (0x122d48) and its stats screen (0x11bd28).
        string state = !v.Track.Closed ? "the ring is OPEN (advisor 204) -- no trains"
                     : !v.Track.Valid ? "the ring is INVALID (advisor 203) -- no trains" : "closed and valid";
        var lap = v.Sim.TestLap();
        v.Stats = lap;
        string rating = lap.RatingRow != 0 && _text?.Text("eng", lap.RatingRow) is { Length: > 0 } r ? r : "-";
        string stats = lap == CoasterStats.None ? "" :
            $"   {(int)lap.Duration} secs  {(int)lap.Length} meters  {(int)lap.MaxSpeed} kph  {(int)lap.Drops} drops  "
            + $"{(int)lap.SteepestDrop} deg  {lap.MaxVertPos:F1}/{lap.MaxVertNeg:F1}/{lap.MaxLat:F1} g   Coaster Rating: {rating}";
        Status($"{v.Track.Type.Name}: {state}{stats}");
        GD.Print($"[coaster] tool closed for ride {v.Id}: {v.Track.Pylons.Count} pylons, {state}{stats}");
        var after = _afterCoaster; _afterCoaster = null;
        after?.Invoke();
    }

    /// <summary>Per frame (`0x11aef0`): the ghost follows the cursor and is validated with the full
    /// rules; the cursor tile says so (165 / 175) and the entry cell wears the 166 chevron.</summary>
    void UpdateCoasterGhost(double delta)
    {
        var v = _coasterTool;
        if (v == null) return;
        if (_coasterMode == CoasterMode.Edit) { StepPylonEdit(v, delta); return; }
        if (!CursorCell(out int x, out int y)) return;
        if ((x, y) == _coasterGhostAt) return;
        _coasterGhostAt = (x, y);
        var t = v.Track;
        if (_coasterGhost != null) t.UnlinkGhost(_coasterGhost);
        _coasterGhost = null;
        var marks = new List<(int X, int Y, int Marker, int Turns)>();
        bool ok = false;
        if (t.Pylons.Count < CoasterTrack.MaxPylons)
        {
            _coasterGhost = t.LinkGhost(new ParkCell(x, y), _coasterStartHeight, _coasterStartBank);
            int cost = v.Price * 10;
            bool afford = _sim == null || _sim.Finances.Unlimited || _sim.Finances.Balance >= cost;
            ok = afford && t.IsValid(_coasterGhost, Ground, true);
        }
        _coasterGhostOk = ok;
        marks.Add((x, y, ok ? 165 : 175, 0));
        var e = t.Entry.Cell;
        if ((e.X, e.Z) != (x, y)) marks.Add((e.X, e.Z, 166, CoasterEntryTurn(t)));
        _ghostView?.ShowTurnedCells(marks, _park);
        RebuildCoaster(v);
        bool closing = new ParkCell(x, y) == t.Entry.Cell && t.Pylons.Count > 0;
        Status($"Pylon Stock {CoasterTrack.MaxPylons - t.Pylons.Count}   " + (closing ? "close the ring" : $"Cost: {Money.Format(v.Price * 10)}")
             + (ok ? "" : "   (not here)") + (t.Type.Loops ? "   Space: loop" : ""));
    }

    /// <summary>The 166 chevron on the entry cell points the way the track must come in.</summary>
    static int CoasterEntryTurn(CoasterTrack t)
    {
        int dx = t.Exit.CellX - t.Entry.CellX, dz = t.Exit.CellZ - t.Entry.CellZ;
        return GhostMarkers.TurnToward(Math.Sign(dx), Math.Sign(dz));
    }

    /// <summary>Cross (`0x11b550`): add a pylon at the ghost, or close the ring on the entry cell.</summary>
    void PressCoasterTool()
    {
        var v = _coasterTool;
        if (v == null || _coasterMode != CoasterMode.Build || !CursorCell(out int x, out int y)) return;
        _coasterGhostAt = (int.MinValue, 0);
        UpdateCoasterGhost(0);
        var t = v.Track;
        if (!_coasterGhostOk || _coasterGhost == null) { _toolSfx?.Play(ToolSounds.Cue.Refused); return; }
        t.UnlinkGhost(_coasterGhost); _coasterGhost = null;
        var cell = new ParkCell(x, y);
        if (cell == t.Entry.Cell && t.Pylons.Count > 0)
        {
            t.AddPylon(cell, 0, 0, false, CoasterNodeKind.Normal);    // closes (0x120868)
            _toolSfx?.Play(ToolSounds.Cue.Connect);
            GD.Print($"[coaster] ring closed with {t.Pylons.Count} pylons, valid {t.Valid}");
            // From a station session the tool goes on to the pylons, no charge (0x11b6a4).
            if (_coasterFromStation) { EnterPylonEdit(v); return; }
            _sim?.Finances.Debit(v.Price * 10);                        // the leave-the-tool charge (0x11b740)
            FinishCoasterTool();
            return;
        }
        if (_sim != null && !_sim.Finances.Debit(v.Price * 10)) { _toolSfx?.Play(ToolSounds.Cue.Refused); return; }
        t.AddPylon(cell, _coasterStartHeight, _coasterStartBank, false, CoasterNodeKind.Normal);
        _toolSfx?.Play(ToolSounds.Cue.Lay);
        _coasterGhostAt = (int.MinValue, 0);
        RebuildCoaster(v);
    }

    /// <summary>Circle (`0x11b898`): take the last pylon back, refunded, and a loop's lead-in with its
    /// loop. False when there is nothing to take back.</summary>
    bool UndoCoasterPylon()
    {
        var v = _coasterTool;
        if (v == null || _coasterMode != CoasterMode.Build) return false;
        var t = v.Track;
        if (_coasterGhost != null) { t.UnlinkGhost(_coasterGhost); _coasterGhost = null; }
        if (!t.RemoveLast()) { _coasterGhostAt = (int.MinValue, 0); return false; }
        _sim?.Finances.Credit(v.Price * 10);
        if (t.Last.Kind == CoasterNodeKind.LeadIn && t.RemoveLast()) _sim?.Finances.Credit(v.Price * 10);
        _coasterStartHeight = t.Last.Height;
        _coasterStartBank = t.Last.Bank;
        _toolSfx?.Play(ToolSounds.Cue.Undo);
        _coasterGhostAt = (int.MinValue, 0);
        RebuildCoaster(v);
        return true;
    }

    /// <summary>Square (`0x11c668`): a loop -- a lead-in on the cursor cell and the loop node one cell
    /// to the side, both height 0 and bank 0 -- or, right after a loop, another loop one more cell
    /// over. Only for the nine coasters that can loop.</summary>
    void CoasterLoop()
    {
        var v = _coasterTool;
        if (v == null || _coasterMode != CoasterMode.Build || !v.Track.Type.Loops || !CursorCell(out int x, out int y)) return;
        var t = v.Track;
        var cursor = new ParkCell(x, y);
        if (Ground.CoasterNodeAt(cursor) is { } there && there != t.Last) { _toolSfx?.Play(ToolSounds.Cue.Refused); return; }
        int cost = v.Price * 10;
        if (_coasterGhost != null) { t.UnlinkGhost(_coasterGhost); _coasterGhost = null; }
        if (t.Last.Kind == CoasterNodeKind.Loop)
        {
            var last = t.Last; var before = last.Prev;
            var c = new ParkCell(2 * last.CellX - before.CellX, 2 * last.CellZ - before.CellZ);
            if (!Ground.InGrid(c) || _park.PlacedAt(c.X, c.Z) != null || !Ground.EmptyLand(c)) { _coasterGhostAt = (int.MinValue, 0); return; }
            if (_sim != null && !_sim.Finances.Debit(cost)) return;
            t.AddPylon(c, 0, 0, true, CoasterNodeKind.Loop);
        }
        else
        {
            _coasterGhostAt = (int.MinValue, 0);
            UpdateCoasterGhost(0);
            if (!_coasterGhostOk) { _toolSfx?.Play(ToolSounds.Cue.Refused); return; }
            t.UnlinkGhost(_coasterGhost); _coasterGhost = null;
            var prev = t.Last;
            if (_sim != null && !_sim.Finances.Debit(cost)) return;
            t.AddPylon(cursor, 0, 0, true, CoasterNodeKind.LeadIn);
            int dx = cursor.X - prev.CellX, dz = cursor.Z - prev.CellZ;
            var side = Math.Abs(dx) < Math.Abs(dz) ? cursor.Offset(dz >= 0 ? -1 : 1, 0) : cursor.Offset(0, dx >= 0 ? 1 : -1);
            _sim?.Finances.Debit(cost);   // the second node is not gated by affordability
            t.AddPylon(side, 0, 0, true, CoasterNodeKind.Loop);
        }
        _toolSfx?.Play(ToolSounds.Cue.Lay);
        _coasterGhostAt = (int.MinValue, 0);
        RebuildCoaster(v);
    }

    // ------------------------------------------------------------------ pylon edit (mode 13)

    CoasterNode PickedPylon(CoasterView v) =>
        _coasterPick >= 0 && _coasterPick < v.Track.Pylons.Count ? v.Track.Pylons[_coasterPick] : null;

    /// <summary>Next (`0x11c468`) / Prev (`0x11c540`): only plain pylons can be picked, never a loop
    /// node or a station node, wrapping at the ends.</summary>
    void CoasterPick(int step)
    {
        var v = _coasterTool;
        if (v == null || _coasterMode != CoasterMode.Edit) return;
        var list = v.Track.Pylons;
        if (list.Count == 0) return;
        for (int i = 0; i < list.Count; i++)
        {
            _coasterPick = ((_coasterPick + step) % list.Count + list.Count) % list.Count;
            if (list[_coasterPick].Kind == CoasterNodeKind.Normal) break;
        }
        ShowPylonPick();
    }

    void ShowPylonPick()
    {
        var v = _coasterTool;
        var n = v == null ? null : PickedPylon(v);
        if (n == null) { Status("no pylon to edit -- Enter to finish"); return; }
        LookAtCell(n.CellX, n.CellZ, null);
        _ghostView?.ShowTurnedCells(new[] { (n.CellX, n.CellZ, n.Valid ? 165 : 175, 0) }, _park);
        Status($"pylon {_coasterPick + 1}: height {n.Height} bank {n.Bank}   ↑↓ height  ←→ bank  , . pick  Enter done"
             + (n.Valid ? "" : "   (INVALID -- red)"));
    }

    /// <summary>`0x11cac8`: the held D-pad moves the picked pylon 0x14 a step, height clamped to
    /// [0, 0x500] and bank to ±0x200; the pylon and its next are revalidated live. Stepped at the
    /// park's tick rate rather than the render rate, so holding a key reads as the console does.</summary>
    void StepPylonEdit(CoasterView v, double delta)
    {
        var n = PickedPylon(v);
        if (n == null) return;
        _coasterHold += delta;
        if (_coasterHold < ParkSim.TickMilliseconds / 1000.0) return;
        _coasterHold = 0;
        int dh = (Input.IsKeyPressed(Key.Up) ? 0x14 : 0) - (Input.IsKeyPressed(Key.Down) ? 0x14 : 0);
        int db = (Input.IsKeyPressed(Key.Right) ? 0x14 : 0) - (Input.IsKeyPressed(Key.Left) ? 0x14 : 0);
        if (dh == 0 && db == 0) return;
        n.Height = Math.Clamp(n.Height + dh, 0, 0x500);
        n.Bank = v.Track.Type.IsMoonshot ? 0 : Math.Clamp(n.Bank + db, -0x200, 0x200);
        v.Track.Recompute();
        n.Valid = v.Track.IsValid(n, Ground, true);
        if (n.Next != null && !n.Next.IsStation) n.Next.Valid = v.Track.IsValid(n.Next, Ground, true);
        RebuildCoaster(v);
        ShowPylonPick();
    }

    /// <summary>The coaster tool's keys, before anything else sees them. True when handled.</summary>
    bool CoasterToolKey(Key key)
    {
        if (_coasterTool == null) return false;
        switch (key)
        {
            case Key.Escape: case Key.Enter: case Key.KpEnter:
                FinishCoasterTool(); return true;
            case Key.Space when _coasterMode == CoasterMode.Build:
                CoasterLoop(); return true;
            case Key.Period when _coasterMode == CoasterMode.Edit:
                CoasterPick(1); return true;
            case Key.Comma when _coasterMode == CoasterMode.Edit:
                CoasterPick(-1); return true;
            case Key.Up or Key.Down or Key.Left or Key.Right when _coasterMode == CoasterMode.Edit:
                return true;
        }
        return false;
    }
}
