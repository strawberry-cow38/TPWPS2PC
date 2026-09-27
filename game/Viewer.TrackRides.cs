using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

public partial class Viewer
{
    /// <summary>⭐⭐ TRACK RIDES: the station the player placed, the loop they draw from it, the pieces
    /// laid along it and the cars that run it. The rules live in core (<see cref="TrackLayout"/>,
    /// <see cref="TrackRideSim"/>, findings/track-rides.md); this file draws them and runs the tool.
    ///
    /// ⭐ One frame for everything: <see cref="Frame"/> maps the console's park units (256 a cell, park
    /// x and z) onto the plot through its own corners, so a piece or a car is placed exactly where the
    /// console's numbers say, on any park, rotated or not. Meshes go in at the console's own model
    /// origin and yaw (0x1FCDB8, 0x1FD0C0) with the loader's Z mirror taken off, because the frame
    /// already carries the plot's.</summary>
    sealed class TrackRideView
    {
        public int Id;
        public ParkRide Ride;
        public TrackRideSim Sim;
        public TrackLayout Layout;
        public string Dir, Prefix;
        public int Price;
        public Node3D Frame;
        public readonly List<Node3D> Pieces = new();
        /// <summary>The laid pieces playing their Main record (the water pieces' flow).</summary>
        public readonly List<AnimatedModel> Flowing = new();
        public int FlowFrame = -1;
        /// <summary>So the open-loop warning is said ONCE, not sixty times a second.</summary>
        public bool WarnedOpenLoop;
        public readonly Dictionary<TrackCar, CarView> Cars = new();
        public int NextTag = CarSoundTagBase;
        public readonly HashSet<(int X, int Y)> Cells = new();
        public long SeenTime = -1;
    }

    /// <summary>A drawn car: its node, the model and mesh (for the seat fitting), its last two poses
    /// for interpolation, and the sound tag its engine voice is registered under.</summary>
    sealed class CarView
    {
        public Node3D Node;
        public AnimatedModel Model;
        public Model Mesh;
        public Vector3 Was, Now;
        public float WasYaw, NowYaw;
        public int Tag;
    }

    /// <summary>Car voices share the ride's sound owner with its script's objects, whose tags are small
    /// script numbers, so the cars' tags start well above them.</summary>
    const int CarSoundTagBase = 0x7000;

    readonly Dictionary<int, TrackRideView> _tracks = new();
    TrackRideView _trackTool;
    System.Action _afterTrack;
    bool _trackLegOk;

    /// <summary>The 0x1FD818 shape → the mesh it draws (findings/track-ride-geometry.md §6). Shapes 15
    /// (the station, drawn by the ride's own model) and 99 (hidden) have none; `q` and `v` are never
    /// selected by any shape.</summary>
    static string TrackMesh(int shape) => shape switch
    {
        0 => "trcks", 1 => "trckb", 2 => "trckb2", 3 => "trckh", 4 => "trckx",
        9 => "trckh_u", 10 => "trckh_a", 11 => "trckh_d", _ => null,
    };

    int TrackWorld => System.IO.Path.GetFileNameWithoutExtension(_lib?.WadName ?? "").ToUpperInvariant() switch
    {
        "JUNGLE" => 0, "HALLOW" => 1, "FANTASY" => 2, "SPACE" => 3, _ => 0,
    };
    int TrackPark => (_terrainPath ?? "").Contains("terrain_2", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    /// <summary>Is this placed thing a track ride? By the compiled record's kind, the same test the
    /// build menu uses.</summary>
    bool IsTrackRide(AssetLibrary.RideAssets r) => r != null && BuildKind(r) == AssetResourceDatabase.AssetKind.TrackRide;

    /// <summary>⭐ Where the console's station tables sit for a ride the PORT placed. The port turns the
    /// footprint about its own centre (Placement.Rotate), a turn sending +x to +z, and the console
    /// numbers its rotations the other way (ShopEntrance's note), so native r = −turns. The console's
    /// station geometry lives in a 4×4 frame, so for a 4×3 station a half or three-quarter turn moves
    /// the frame by a cell. Rather than tabulate that, the r=0 exit block is carried through the port's
    /// own turn and the anchor is solved so the console's exit lands on it. The ENTRY block is carried
    /// the same way as an independent check: if the two disagree, the mapping is wrong, and it says so.</summary>
    static (ParkCell Anchor, int Rot, bool Agrees) StationFrame(ParkCell origin, int turns, int baseW, int baseD)
    {
        int h = baseD, w = baseW;
        (int U, int V) exit = (4, 1), entry = (-2, 1);
        for (int t = 0; t < (turns & 3); t++)
        {
            exit = (h - exit.V - 2, exit.U);
            entry = (h - entry.V - 2, entry.U);
            (w, h) = (h, w);
        }
        int rot = (4 - turns) & 3;
        var e = TrackPieces.Exit(new ParkCell(0, 0), rot);
        var anchor = origin.Offset(exit.U - e.X, exit.V - e.Z);
        return (anchor, rot, TrackPieces.Return(anchor, rot) == origin.Offset(entry.U, entry.V));
    }

    /// <summary>Called by PlaceHeld for a track ride once its script is running. Returns false when
    /// there is nothing to attach to (no sim ride), and the caller carries on as for any ride.</summary>
    bool BeginTrackRide(int id, AssetLibrary.RideAssets station, int cx, int cy, int turns, Park.Footprint baseFp)
    {
        var ride = _sim?.Rides.FirstOrDefault(r => r.Id == id);
        if (ride == null || station?.Model == null) return false;
        string dir = station.Model.Path[..(station.Model.Path.LastIndexOf('/') + 1)];
        var straight = _lib.Rides.FirstOrDefault(r => r.Model != null
            && r.Model.Path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)
            && r.Model.Path.EndsWith("trcks.mps", StringComparison.OrdinalIgnoreCase));
        if (straight == null) { GD.PrintErr($"[track] {station.Name}: no *_trcks.mps beside it"); return false; }
        string leaf = straight.Model.Path[(straight.Model.Path.LastIndexOf('/') + 1)..];
        string prefix = leaf[..^"trcks.mps".Length];
        var (anchor, rot, agrees) = StationFrame(new ParkCell(cx, cy), turns, baseFp.Width, baseFp.Height);
        var ground = new TrackGround
        {
            World = TrackWorld, Park = TrackPark,
            Bridged = c => _paths?.KindAt(c.X, c.Z) is PathTool.Kind.Path or PathTool.Kind.Queue or PathTool.Kind.Both,
        };
        var layout = new TrackLayout(anchor, rot, ground);
        bool karts = prefix.StartsWith("gk", StringComparison.OrdinalIgnoreCase);
        var sim = _sim.AttachTrack(id, layout, seed: id, karts: karts);
        // Record +0xCC, the per-piece price the tool loads at 0x129298 (findings/track-ride-tool.md §8).
        var payload = ride.Definition?.CompiledEntry?.Payload;
        int price = payload is { Length: >= 0xd0 } p ? BitConverter.ToInt32(p.Span.Slice(0xcc, 4)) : 0;
        var frame = new Node3D { Name = $"Track_{id}" };
        AddChild(frame);
        var view = new TrackRideView { Id = id, Ride = ride, Sim = sim, Layout = layout, Dir = dir, Prefix = prefix, Price = price, Frame = frame };
        _tracks[id] = view;
        sim.SoundCue += (car, evt) => TrackCarSound(view, car, evt);
        ClaimFloor();
        PlaceFrame(view);
        RebuildTrackView(view);
        GD.Print($"[track] {station.Name}: station anchor {anchor} rot {rot} (port turns {turns}), exit {layout.ExitCell}, "
               + $"entry {layout.ReturnCell}, {(karts ? "karts" : "boats")}, {prefix}* pieces, {price} a piece"
               + (agrees ? "" : " -- ⚠ ENTRY DISAGREES with the port's turn: the station mapping is wrong"));
        return true;
    }

    /// <summary>The frame: park units in, the plot's world out. Axis x is one cell along grid x, axis z
    /// one cell along grid z (world −Z on an unrotated plot), y one cell's length up. So a child placed
    /// at (px/256, h/256, pz/256) is exactly where the console's numbers put it.</summary>
    void PlaceFrame(TrackRideView v)
    {
        var o = _park.CellCorner(0, 0);
        var ex = _park.CellCorner(1, 0) - o;
        var ez = _park.CellCorner(0, 1) - o;
        var basis = new Basis(ex, new Vector3(0, ex.Length(), 0), ez);
        v.Frame.Transform = new Transform3D(basis, new Vector3(o.X, _park.BaseY, o.Z));
    }

    /// <summary>A mesh from the ride's folder, built the way LoadPlaceable builds any model, with the
    /// loader's Z mirror taken off (the frame carries the plot's own).</summary>
    Node3D TrackModel(TrackRideView v, string stem) => TrackModel(v, stem, out _, out _);

    Node3D TrackModel(TrackRideView v, string stem, out AnimatedModel model, out Model mesh)
        => TrackModel(v, stem, out model, out mesh, out _);

    Node3D TrackModel(TrackRideView v, string stem, out AnimatedModel model, out Model mesh, out Aps animation)
    {
        var assets = _lib.Rides.FirstOrDefault(r => r.Model != null
            && r.Model.Path.Equals(v.Dir + stem + ".mps", StringComparison.OrdinalIgnoreCase));
        model = LoadPlaceable(assets, out animation, out mesh);
        if (model?.Root == null) return null;
        model.Root.Scale = Vector3.One;
        var holder = new Node3D { Name = stem };
        holder.AddChild(model.Root);
        return holder;
    }

    /// <summary>Lay the pieces again after the track changed: meshes at 0x1FCDB8's origin and
    /// 0x1FD0C0's yaw, and the cells they stand on claimed.</summary>
    void RebuildTrackView(TrackRideView v)
    {
        foreach (var n in v.Pieces) if (IsInstanceValid(n)) n.QueueFree();
        v.Pieces.Clear();
        v.Flowing.Clear();
        v.FlowFrame = -1;
        v.Cells.Clear();
        foreach (var p in v.Layout.Pieces)
        {
            var info = p.Info;
            int size = p.Type >= 40 ? 4 : 2;
            if (p.Type >= 4)
                for (int dx = 0; dx < size; dx++)
                    for (int dz = 0; dz < size; dz++) v.Cells.Add((p.Anchor.X + dx, p.Anchor.Z + dz));
            string stem = TrackMesh(info.Shape);
            if (stem == null) continue;
            var node = TrackModel(v, v.Prefix + stem, out var model, out _, out var anim);
            if (node == null) continue;
            // ⭐ `0x1fd818`, on giving a piece its model, starts it on `.aps` section 5 (Main) at speed 1.0
            // with flag 1 (vt +0x5c → 0x17c5d8 → 0x1abc80), and flag bit 0 is the channel's LOOP bit
            // (0x1ab7b4 → +0x14 & 1; the end-of-record handler 0x1ac254 restarts the record while it is
            // set). Every water piece's Main is a 50-frame UV loop on its water mesh, so that is the flow.
            // (The kart pieces take the same call; whatever their Main does, it runs too.)
            if (model != null && anim?.Records().FirstOrDefault(r => r.Slot == 5) is { } main)
            {
                model.UseRecord(main);
                v.Flowing.Add(model);
            }
            int r = info.Rot, w = info.Width << 8, d = info.Depth << 8;
            int ox = p.Anchor.X * 256, oz = p.Anchor.Z * 256;
            switch (r) { case 1: oz += w; break; case 2: ox += w; oz += d; break; case 3: ox += d; break; }
            float angle = r * Mathf.Pi / 2;
            float yaw;
            if (p.Type is >= 12 and <= 19 or >= 28 and <= 31) yaw = 2 * Mathf.Pi - angle;
            else
            {
                yaw = Mathf.Pi - angle;
                (int cx, int cz) = r switch { 0 => (w, d), 1 => (w, -d), 2 => (-w, -d), _ => (-w, d) };
                ox += cx; oz += cz;
            }
            // Console yaw φ maps (x,z) to (x cosφ − z sinφ, x sinφ + z cosφ): Godot's RotY(−φ).
            node.Transform = new Transform3D(new Basis(Vector3.Up, -yaw), new Vector3(ox / 256f, 0, oz / 256f));
            v.Frame.AddChild(node);
            v.Pieces.Add(node);
        }
        RefreshFloor();
    }

    /// <summary>⚠⚠ A TRACK RIDE WITH AN UNCLOSED LOOP IS SILENTLY DEAD, and that silence is the
    /// bug behind master's "guests never ride kart rides?". The status machine starts at `Closed`;
    /// the only state that boards is `Loading`; and `Loading` is reachable ONLY through
    /// `Running -> Unloading`, with `Running` gated on `Track.Closed`. So an unfinished loop means
    /// guests walk over, join the queue, and stand there for ever with nothing said anywhere.
    ///
    /// ⭐ This only REPORTS it. Whether a guest should refuse to queue for an unfinished ride is a
    /// behaviour question -- `ParkVisitors.Takes` exists to stop exactly this kind of stranding --
    /// but what the console does here is not read, and changing who queues where on a guess is how
    /// a park ends up behaving plausibly and wrongly.</summary>
    void WarnUnclosedLoop(TrackRideView v)
    {
        int waiting = v.Ride?.Queue?.Count ?? 0;
        if (v.WarnedOpenLoop || waiting == 0 || v.Layout.Closed) return;
        v.WarnedOpenLoop = true;
        GD.Print($"[track] {v.Ride?.Name ?? v.Id.ToString()}: {waiting} guest"
               + $"{(waiting == 1 ? "" : "s")} queuing and the loop is NOT CLOSED -- status "
               + $"{v.Sim.Status}, {v.Layout.Pieces.Count} pieces laid. Nobody can board until the "
               + "track closes back on the station, and the ride says nothing about it.");
    }

    /// <summary>Per rendered frame: the pieces' looping Main, the cars, between the last two ticks, and
    /// their engine voices.</summary>
    void PresentTracks(float alpha)
    {
        // The pieces' loop on the park's own clock, so a paused park holds still and a wound shot has
        // run, in whole frames at the .aps's 30 a second: every water piece rebuilds its water mesh
        // when the frame moves, so only when it moves. ⚠ One phase for every piece: the console starts
        // each piece's loop when 0x1fd818 gives it its model, so pieces laid at different moments run
        // out of step there. Not reproduced.
        int flow = (int)(((_sim?.Time ?? 0) + alpha * ParkSim.TickMilliseconds) * Aps.Fps / 1000f);
        foreach (var v in _tracks.Values)
        {
            WarnUnclosedLoop(v);
            if (flow != v.FlowFrame)
            {
                v.FlowFrame = flow;
                foreach (var m in v.Flowing)
                    if (m.Root != null && IsInstanceValid(m.Root) && m.Frames > 0) m.SetFrame(flow % m.Frames);
            }
            bool ticked = _sim != null && _sim.Time != v.SeenTime;
            if (ticked) v.SeenTime = _sim.Time;
            foreach (var gone in v.Cars.Keys.Where(c => !v.Sim.Cars.Contains(c)).ToList())
            {
                var cv = v.Cars[gone];
                if (IsInstanceValid(cv.Node)) cv.Node.QueueFree();
                _sounds?.Kill(v.Id, "track", cv.Tag, (long)_busElapsedMs);
                _sounds?.Follow(v.Id, cv.Tag, null);
                v.Cars.Remove(gone);
            }
            if (v.Layout.Length == 0) continue;
            foreach (var car in v.Sim.Cars)
            {
                var (x, y, z) = v.Layout.Position(car.Distance, car.Lateral);
                var now = new Vector3(x / 256f, y / 256f, z / 256f);
                float yawNow = car.Heading * Mathf.Tau / 4096f;
                if (!v.Cars.TryGetValue(car, out var cv))
                {
                    string stem = car.IsKart
                        ? v.Prefix + new[] { "blue", "green", "orange", "purple" }[car.Colour & 3]
                        : v.Prefix + "ring";
                    AnimatedModel model; Model mesh;
                    var node = TrackModel(v, stem, out model, out mesh);
                    if (node == null && car.IsKart) node = TrackModel(v, v.Prefix + "blue", out model, out mesh);
                    node ??= new Node3D();
                    v.Frame.AddChild(node);
                    cv = new CarView { Node = node, Model = model, Mesh = mesh, Was = now, Now = now, WasYaw = yawNow, NowYaw = yawNow, Tag = v.NextTag++ };
                    v.Cars[car] = cv;
                    var carNode = node;
                    _sounds ??= MakeSounds();
                    _sounds?.Follow(v.Id, cv.Tag, () => IsInstanceValid(carNode) ? carNode.GlobalPosition : null);
                }
                else if (ticked) { cv.Was = cv.Now; cv.Now = now; cv.WasYaw = cv.NowYaw; cv.NowYaw = yawNow; }
                var pos = cv.Was.Lerp(cv.Now, alpha);
                float yaw = Mathf.LerpAngle(cv.WasYaw, cv.NowYaw, alpha);
                // Car heading h: the console turns the model by −h·2π/4096 (0x2039C8), i.e. RotY(+h) here.
                cv.Node.Transform = new Transform3D(new Basis(Vector3.Up, yaw), pos);
                // ⭐ The engine note: category 6 event 4, a 226 ms clip. The console does not loop it;
                // every car step asks whether the voice is still alive (0x111CC8) and starts it again
                // if not (vtable +0x34), which is what this does once a tick.
                // ⚠ Parameter 4 is set to speed × 100 / target every step, and what the audio object does
                // with parameter 4 is not read, so no pitch or volume follows speed here.
                if (ticked && _sounds != null && !_sounds.Sounding(v.Id, cv.Tag))
                    _sounds.Cue(v.Id, "track", (long)_busElapsedMs, RseOpcode.EVENT, (int)SoundGroup.NativeRidesTrack,
                                -1, 4, cv.Tag, cv.Node.GlobalPosition);
            }
        }
    }

    /// <summary>A car's one-shot (category 6 event 0xF), from the sim.</summary>
    void TrackCarSound(TrackRideView v, TrackCar car, int evt)
    {
        _sounds ??= MakeSounds();
        if (_sounds == null || !v.Cars.TryGetValue(car, out var cv) || !IsInstanceValid(cv.Node)) return;
        _sounds.Cue(v.Id, "track", (long)_busElapsedMs, RseOpcode.EVENT, (int)SoundGroup.NativeRidesTrack,
                    -1, evt, cv.Tag + 0x800, cv.Node.GlobalPosition);
    }

    /// <summary>⭐ RIDERS IN THE CARS. `0x205568` seats a boarding guest on the car model's seat
    /// `n + 1` (0x80 fittings: `bluehead`, `Head02`...), and with one guest per car that is always
    /// fitting 1. Drawn through the same SeatPose the scripted rides use, so a head in a kart is
    /// placed exactly as a head on Crazy Ape's arm.</summary>
    void SeatTrackRiders()
    {
        foreach (var v in _tracks.Values)
            foreach (var car in v.Sim.Cars)
            {
                if (car.Guest is not int guest || !v.Cars.TryGetValue(car, out var cv)) continue;
                if (cv.Mesh == null || cv.Model?.Root == null || !IsInstanceValid(cv.Model.Root) || cv.Model.LastWorld == null) continue;
                if (cv.Mesh.FindFitting(1, 0x80) is not { Node: >= 0 } fit) continue;
                if (!SeatPose(cv.Mesh, cv.Model, cv.Model.Root.GlobalTransform, fit, out var pose, out var forward, out _)) continue;
                _seated[guest] = (pose, $"{v.Ride.Name} car {car.Index} on {cv.Mesh.NodeName(fit.Node)}", forward, "car", float.NaN);
            }
    }

    void RemoveTrackView(int id)
    {
        if (!_tracks.Remove(id, out var v)) return;
        foreach (var cv in v.Cars.Values) { _sounds?.Kill(id, "track", cv.Tag, (long)_busElapsedMs); _sounds?.Follow(id, cv.Tag, null); }
        if (_trackTool == v) { _trackTool = null; _afterTrack = null; _ghostView?.Clear(); }
        if (IsInstanceValid(v.Frame)) v.Frame.QueueFree();
    }

    void ClearTrackViews()
    {
        foreach (var id in _tracks.Keys.ToList()) RemoveTrackView(id);
    }

    // ------------------------------------------------------------------ the tool

    void OpenTrackTool(TrackRideView v, System.Action after)
    {
        _trackTool = v;
        _afterTrack = after;
        LookAtCell(v.Layout.ExitCell.X, v.Layout.ExitCell.Z, null);
        Status($"draw the track: click to lay a leg, right click to take one back, Esc to finish. Close the loop at {v.Layout.ReturnCell}");
        GD.Print($"[track] tool open for ride {v.Id}");
    }

    /// <summary>"Edit Track" (0x129AA8): entering edit takes the closing leg back, refunded, so the
    /// loop is open to redraw. ⚠ The console also keeps a backup that Triangle restores; here Esc
    /// just leaves the track as it stands.</summary>
    void EditTrack(int placed)
    {
        if (placed < 0 || placed >= _park.Placed.Count || !_tracks.TryGetValue(_park.Placed[placed].Id, out var v)) return;
        if (v.Layout.Closed)
        {
            int k = v.Sim.RemoveLastWaypoint();
            if (k > 0) { _sim?.Finances.Credit(v.Price * 10 * k); UnbillTrack(v.Id, v.Price * 10 * k); }
            RebuildTrackView(v);
        }
        OpenTrackTool(v, null);
    }

    void FinishTrackTool()
    {
        var v = _trackTool;
        if (v == null) return;
        _trackTool = null;
        _ghostView?.Clear();
        // ⚠ Or the cost and stock lines stay on screen after the tool has gone.
        _previewCost = null; _previewStock = null;
        GD.Print($"[track] tool closed for ride {v.Id}: {v.Layout.Waypoints.Count} waypoints, {v.Layout.Pieces.Count} pieces, "
               + (v.Layout.Closed ? "loop closed" : "loop OPEN -- the ride stays closed"));
        // Said on screen, not only in the log: an open loop never runs, and nothing else looks wrong.
        if (!v.Layout.Closed) Status($"the track is OPEN: the ride can't run until the loop closes at {v.Layout.ReturnCell}. Edit Track to finish it.");
        var after = _afterTrack; _afterTrack = null;
        after?.Invoke();
    }

    /// <summary>0x14A248's per-block rule, read against the port's ground. Every cell of the 2×2 block
    /// must be in the park and be empty land, a path or queue (a bridge), or one of this ride's own
    /// plain straights (a crossing). The END block must be empty land.</summary>
    bool TrackBlockOk(TrackRideView v, ParkCell c, bool isEnd)
    {
        if (c == v.Layout.Waypoints[^1] || c == v.Layout.ReturnCell) return true; // 0x129378
        for (int dx = 0; dx < 2; dx++)
            for (int dz = 0; dz < 2; dz++)
            {
                int x = c.X + dx, z = c.Z + dz;
                if (x < 0 || z < 0 || x >= _park.Width - 2 || z >= _park.Height - 2 || !_park.IsPlayable(x, z)) return false;
                var own = v.Layout.PieceAt(new ParkCell(x, z));
                if (own != null)
                {
                    if (isEnd || own.Type is < 4 or > 7) return false;
                    continue;
                }
                var kind = _paths?.KindAt(x, z) ?? PathTool.Kind.None;
                if (kind is PathTool.Kind.Path or PathTool.Kind.Queue or PathTool.Kind.Both)
                {
                    if (isEnd) return false;
                    continue;
                }
                if (!_park.Vacant(x, z)) return false;
            }
        return true;
    }

    /// <summary>⭐ 0x1E81E0's track arm, the path and queue tools' verdict on a cell a track piece covers
    /// (the piece found by 0x14A420). Refused under a piece of shape 15 (station), 1 or 2 (the bends),
    /// 4 (the crossing) or 99 (hidden: the station connector, the crossing's other half), under an
    /// add-on (type 40 and up), and on the 2×2 blocks at the station's exit (0x200078) and entry
    /// (0x2001A8). Allowed, then, under straights, humps and ramps, for path (2), queue (4) and kind 13
    /// alike. False when no track covers the cell: that is not this rule's to allow.</summary>
    bool PathMayGoUnderTrack(int x, int z)
    {
        bool under = false;
        foreach (var v in _tracks.Values)
        {
            var p = v.Layout.PieceAt(new ParkCell(x, z));
            if (p == null) continue;
            under = true;
            if (p.Info.Shape is 15 or 1 or 2 or 4 or 99 || p.Type >= 40) return false;
            if (InBlock(v.Layout.ExitCell) || InBlock(v.Layout.ReturnCell)) return false;
        }
        return under;
        bool InBlock(ParkCell b) => x - b.X is 0 or 1 && z - b.Z is 0 or 1;
    }

    /// <summary>What the path ghost refuses for standing on the cell: anything <see cref="Park.Vacant"/>
    /// says no to, except track a path may go under. A coaster's pylon cell stays refused.</summary>
    bool PathBlocked(int x, int y)
        => !_park.Vacant(x, y)
           && !(_park.Unbuilt(x, y) && !_coasters.Values.Any(c => c.Cells.Contains((x, y))) && PathMayGoUnderTrack(x, y));

    /// <summary>⭐ 0x1E6CF0 (tile event 0x84) and 0x18E4B8: when a cell under a track piece becomes path,
    /// queue or kind 13, the piece is set to type 8 and its ride re-laid (0x2009C0), so the chooser's
    /// bridge rule turns the straight into a hump, or a run of them into ramp-up, raised, ramp-down.
    /// A piece that is already a hump (shape 3) is left alone.
    ///
    /// ⚠ PORT DEPARTURE, the undo half: the console hook fires only when the cell IS path, so taking a
    /// path away leaves its bridge standing until something else re-lays the ride. The port's path
    /// undo re-lays it at once, on the ground that an undo should put back what was there.</summary>
    void TrackOverPathChanged(int x, int z)
    {
        var kind = _paths?.KindAt(x, z) ?? PathTool.Kind.None;
        bool path = kind is PathTool.Kind.Path or PathTool.Kind.Queue or PathTool.Kind.Both;
        foreach (var v in _tracks.Values)
        {
            var p = v.Layout.PieceAt(new ParkCell(x, z));
            if (p == null) continue;
            int shape = p.Info.Shape;
            bool relay = path ? shape != 3 : shape is 3 or 9 or 10 or 11;
            if (!relay) continue;
            v.Sim.Relay();
            RebuildTrackView(v);
            GD.Print($"[track] ride {v.Id}: {(path ? "path laid under" : "path taken from under")} ({x},{z}), "
                   + $"piece was {p} -> now {v.Layout.PieceAt(new ParkCell(x, z))}; re-laid, everyone unloaded");
            break;
        }
    }

    /// <summary>0x1293C8's direction rule: from a plain straight any way; from a piece with one exit
    /// (a crossing, a hump, a ramp) only straight on; from anything else (a bend, the station) nowhere.</summary>
    static bool TrackDirectionOk(TrackPiece p0, int dir)
    {
        if (p0 == null) return true;
        int t = p0.Type;
        if (t is >= 4 and <= 7) return true;
        if (t is >= 20 and <= 27 or >= 36 and <= 51) return (t & 3) == dir;
        return false;
    }

    void UpdateTrackGhost()
    {
        var v = _trackTool;
        if (v == null || !CursorCell(out int x, out int y)) return;
        var (end, n, sx, sz) = v.Layout.Leg(new ParkCell(x, y));
        var prev = v.Layout.Waypoints[^1];
        int dir = sx > 0 ? 2 : sx < 0 ? 3 : sz > 0 ? 0 : 1;
        bool dirOk = n == 0 || TrackDirectionOk(v.Layout.PieceAt(prev), dir);
        int stock = TrackLayout.MaxPieces - v.Layout.Pieces.Count;
        int cost = v.Price * 10 * n;
        bool affordable = _sim == null || _sim.Finances.Unlimited || _sim.Finances.Balance >= cost;
        var marks = new List<(int X, int Y, int Marker, int Turns)>();
        bool ok = true;
        for (int i = 0; i <= n; i++)
        {
            var c = prev.Offset(sx * i, sz * i);
            bool isEnd = i == n;
            bool cellOk = stock - i >= 0 && affordable && dirOk && TrackBlockOk(v, c, isEnd);
            int marker = !cellOk ? 175 : isEnd && (c == v.Layout.ReturnCell || c == prev) ? 173 : 165;
            if (!cellOk) ok = false;
            for (int dx = 0; dx < 2; dx++)
                for (int dz = 0; dz < 2; dz++) marks.Add((c.X + dx, c.Z + dz, marker, 0));
        }
        var ret = v.Layout.ReturnCell;
        // The chevron points the way the track runs into the station: from the return cell towards the
        // exit's side. `(rotation + 2) & 3` got rotations 0 and 2 right and 1 and 3 backwards.
        var exitCell = v.Layout.ExitCell;
        int inTurn = GhostMarkers.ChevronToward(Math.Sign(exitCell.X - ret.X), Math.Sign(exitCell.Z - ret.Z));
        if (end != ret)
            for (int dx = 0; dx < 2; dx++)
                for (int dz = 0; dz < 2; dz++) marks.Add((ret.X + dx, ret.Z + dz, 166, inTurn));
        _trackLegOk = ok;
        _ghostView?.ShowTurnedCells(marks, _park);
        // ⭐ The same two numbers the console puts on the HUD in white, rather than only in the
        // debug status line. See Viewer.CostX for the decode.
        _previewCost = cost; _previewStock = stock; _previewStockTextId = TrackStockTextId;
        Status($"Track Stock {stock}   Cost: {Money.Format(cost)}" + (ok ? "" : "   (blocked)"));
    }

    /// <summary>A leg taken back is refunded IN FULL by the console (0x129A00) -- it is an undo,
    /// not a demolition -- so it must also come off the ride's tab. Leaving it on would pay the
    /// player 50% again for track they had already been given all their money back for.</summary>
    void UnbillTrack(int id, int amount)
    {
        if (!_paidFor.TryGetValue(id, out int had)) return;
        int left = had - amount;
        if (left > 0) _paidFor[id] = left; else _paidFor.Remove(id);
    }

    /// <summary>Cross (0x129840): lay the previewed leg, charge it, and finish when it closes the loop.
    ///
    /// ⚠⚠ A ZERO-LENGTH LEG DOES NOTHING HERE -- A DELIBERATE DEPARTURE. On the console Cross with the
    /// cursor within a cell of the last waypoint (`n = |d| >> 1 = 0`) appends a duplicate waypoint and
    /// FINISHES the tool, loop open (track-ride-tool.md §4, READ): on a d-pad that takes a deliberate
    /// press without moving. With a mouse it is the ring of cells round the last waypoint, a near-miss
    /// or a second click on the same spot, and it left strawberry's loop open while it looked closed
    /// ("loop was closed. maybe it wasnt registering as closed?"). Esc still finishes an open track.</summary>
    void PressTrackTool()
    {
        var v = _trackTool;
        if (v == null || !CursorCell(out int x, out int y)) return;
        UpdateTrackGhost();
        if (!_trackLegOk) { _toolSfx?.Play(ToolSounds.Cue.Refused); return; }
        var (end, n, _, _) = v.Layout.Leg(new ParkCell(x, y));
        if (n == 0)
        {
            _toolSfx?.Play(ToolSounds.Cue.Refused);
            Status("that's on the last waypoint: legs go 2 cells at a time, along one axis. Esc finishes.");
            return;
        }
        int cost = v.Price * 10 * n;
        if (cost > 0 && _sim != null && !_sim.Finances.Debit(cost)) { _toolSfx?.Play(ToolSounds.Cue.Refused); return; }
        // ⭐ Track legs go on the ride's tab, so deleting the ride gives half of them back too.
        // A track ride's price is not one number: it is however many legs the player drew.
        if (cost > 0) _paidFor[v.Id] = _paidFor.GetValueOrDefault(v.Id) + cost;
        v.Sim.AddWaypoint(end);
        RebuildTrackView(v);
        _toolSfx?.Play(ToolSounds.Cue.Lay);
        GD.Print($"[track] leg to {end}: {n} pieces for {Money.Format(cost)}; {v.Layout.Pieces.Count} pieces, closed {v.Layout.Closed}");
        if (v.Layout.Closed) FinishTrackTool();
    }

    /// <summary>Circle (0x129A00): take the last leg back and refund it. False when only the exit is
    /// left, so the caller can treat the press as "finish".</summary>
    bool UndoTrackLeg()
    {
        var v = _trackTool;
        if (v == null || v.Layout.Waypoints.Count < 2) return false;
        int k = v.Sim.RemoveLastWaypoint();
        if (k > 0) { _sim?.Finances.Credit(v.Price * 10 * k); UnbillTrack(v.Id, v.Price * 10 * k); }
        RebuildTrackView(v);
        _toolSfx?.Play(ToolSounds.Cue.Undo);
        return true;
    }
}
