using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

public partial class Viewer
{
    /// <summary>⭐⭐ THE SEAPLANE AND THE FERRY, restored. Every world ships both as fixed items beside
    /// the bus and the gates (Info.Id x602 and x604 next to Bus x600 and Gates x601): a model, a
    /// three-variant Main animation, and a script that does the bus's job -- arrive at a first stop,
    /// wait for the game's trigger, move to a second stop, wait, leave, wait off screen, loop. The
    /// ferry's own comment says "Float to first bus stop". JUNGLE's ferry has only a script, no model.
    ///
    /// ⚠⚠ THE PS2 NEVER RUNS THEM. The executable's one fixed-item stem list is Gates, Bus1, Bus2
    /// (then the buildables, into `%s\%s.mps`), and neither name appears anywhere else in it; their
    /// EVT_PLANE / EVT_BOAT cues are in no shipped sound map either. The PC version does show them.
    /// strawberry, 2026-09-25: "i dont see a reason not too ... give it a go!" `--no-seaplane-ferry`
    /// switches them off.
    ///
    /// ⭐ THE SCRIPT DRIVES THEM, not a copy of it: each runs its own .RSE on the port's VM, which
    /// plays the animation variants and reports progress in VAR_STATUS (1..6). What the PS2 cut is the
    /// code that pulls VAR_TRIGGER, so that is the one thing written here -- and the WAITS are the
    /// port's choice, not decoded: nothing on this disc says how long either stays.
    ///
    /// ⚠ They carry no guests. On the PC they may deliver them like the bus; nothing on this disc
    /// says, so they are scenery with a timetable until someone reads the PC.</summary>
    sealed class ParkVehicle
    {
        public string Stem;
        public RseMachine Machine;
        public RsePreviewHost Host;
        public RseModelPresenter Presenter;
        public Node3D Root;
        public long StartMs, HeldSince = -1;
        public int Triggers;
        public int LastStatus = -1;
    }

    readonly List<ParkVehicle> _vehicles = new();
    string _vehiclesKey;
    bool _noSeaplaneFerry;
    bool VehiclesOn => !_noSeaplaneFerry && !ResearchFlags.Value.Contains("--no-seaplane-ferry");

    /// <summary>Port-chosen waits, in milliseconds: at the first stop, at the second, and off screen
    /// before the next visit. ⚠ Not decoded -- see the class note.</summary>
    static (int Stop1, int Stop2, int Away, int Start) VehicleWaits(string stem) => stem switch
    {
        "seaplane" => (20_000, 20_000, 90_000, 0),
        _ => (25_000, 25_000, 120_000, 45_000),
    };

    void EnsureParkVehicles()
    {
        if (!VehiclesOn || _lib == null || _terrainPath == null) return;
        string key = _lib.WadName + ":" + _terrainPath;
        if (_vehiclesKey == key) return;
        ResetParkVehicles();
        _vehiclesKey = key;
        // ⚠⚠ NOT IN JUNGLE. The routes are authored in world space, the same numbers in every
        // archive, and they only fit a park whose bay is where the other three worlds put it. In
        // both JUNGLE terrains the seaplane's first stop is inside the grass bank by the road bend
        // (rendered and looked at, 2026-09-25), and JUNGLE's ferry ships no model at all.
        if (Path.GetFileNameWithoutExtension(_lib.WadName).Equals("JUNGLE", StringComparison.OrdinalIgnoreCase))
        {
            GD.Print($"[vehicle] {key}: none -- the seaplane's authored stop is inside the jungle's hillside and its ferry has no model");
            return;
        }
        foreach (var stem in new[] { "seaplane", "ferry" })
        {
            var assets = _lib.Rides.FirstOrDefault(r => r.Name.Equals($"features/{stem}/{stem}.mps", StringComparison.OrdinalIgnoreCase));
            if (assets?.Model == null || assets.Animation == null || assets.Script == null)
            {
                GD.Print($"[vehicle] {key}: no {stem} (model {assets?.Model != null}, animation {assets?.Animation != null}, script {assets?.Script != null})");
                continue;
            }
            try
            {
                var model = _lib.LoadModel(assets);
                var animation = new Aps(_lib.Read(assets.Animation));
                var host = new RsePreviewHost(animation);
                var root = new Node3D { Name = "Vehicle_" + stem, Visible = _mode == Mode.Park };
                AddChild(root); // authored world coordinates (DontApplyOffset), the same frame as the bus
                var waits = VehicleWaits(stem);
                _vehicles.Add(new ParkVehicle
                {
                    Stem = stem, Host = host, Root = root,
                    Machine = new RseMachine(new RseProgram(_lib.Read(assets.Script)), host),
                    Presenter = new RseModelPresenter(root, model, animation, m => TextureNear(assets.Model.Path, m)),
                    StartMs = (long)_busElapsedMs + waits.Start,
                });
                GD.Print($"[vehicle] {key}: {stem} from {assets.Model.Path}, slots {string.Join(",", host.AvailableSlots.Select(s => $"{s.Slot}x{s.Variants}"))}");
            }
            catch (Exception e) { GD.PrintErr($"[vehicle] {key}: {stem} failed: {e.Message}"); }
        }
    }

    /// <summary>One park tick: run each script to now, and pull its trigger once it has waited out
    /// the current stop (VAR_STATUS 2, 4 or 6).</summary>
    void TickParkVehicles()
    {
        EnsureParkVehicles();
        long now = (long)_busElapsedMs;
        foreach (var v in _vehicles)
        {
            if (now < v.StartMs) continue;
            long t = now - v.StartMs;
            v.Machine.RunSlice(t);
            int status = v.Machine[1];
            if (status != v.LastStatus)
            {
                GD.Print($"[vehicle] {v.Stem} {t}ms status {v.LastStatus} -> {status} anim {v.Host.Current?.Record.Slot}/{v.Host.Current?.Variant}");
                v.LastStatus = status;
            }
            if (status is not (2 or 4 or 6)) { v.HeldSince = -1; continue; }
            if (v.HeldSince < 0) v.HeldSince = t;
            var waits = VehicleWaits(v.Stem);
            int hold = status == 2 ? waits.Stop1 : status == 4 ? waits.Stop2 : waits.Away;
            if (t - v.HeldSince >= hold && v.Machine[0] == 0) { v.Machine[0] = 1; v.Triggers++; v.HeldSince = -1; }
        }
    }

    void PresentParkVehicles()
    {
        foreach (var v in _vehicles)
            if (v.Host.Current != null) v.Presenter.Update(v.Host);
        ShootVehicleAtStop();
    }

    /// <summary>A CONTROL, not a feature: `TPW_VEHICLE=seaplane|ferry` with `TPW_VEHICLE_SHOT=path`
    /// frames that vehicle when it reaches its first stop and saves one picture, so where each
    /// park's authored stop lands -- water or not -- can be looked at rather than assumed.</summary>
    int _vehicleShotIn = -1;
    void ShootVehicleAtStop()
    {
        string want = System.Environment.GetEnvironmentVariable("TPW_VEHICLE"), shot = System.Environment.GetEnvironmentVariable("TPW_VEHICLE_SHOT");
        if (string.IsNullOrEmpty(want) || string.IsNullOrEmpty(shot)) return;
        if (_vehicleShotIn > 0 && --_vehicleShotIn == 0) { SaveShot(shot); GetTree().Quit(); return; }
        if (_vehicleShotIn >= 0) return;
        var v = _vehicles.FirstOrDefault(x => x.Stem == want);
        int stop = int.TryParse(System.Environment.GetEnvironmentVariable("TPW_VEHICLE_STOP"), out var st) ? st : 2;
        if (v == null || v.LastStatus != stop) return;
        // ⚠ Not the meshes' AABB: the model moves by animating its NODES, and a mesh's own bounds
        // stay where it was built. The animated node matrices are where it actually is -- the same
        // route the bus's sound position takes.
        var world = v.Presenter.Drawn?.LastWorld;
        if (world == null || world.Count == 0) { GD.Print($"[vehicle] {want} at stop 1 has no animated nodes"); _vehicleShotIn = 2; return; }
        var sum = Vector3.Zero;
        foreach (var m in world.Values) sum += v.Root.GlobalTransform * new Vector3(m.M41, m.M42, m.M43);
        var centre = sum / world.Count;
        if (_panel != null) _panel.Visible = false;
        _freeCam = true; _focus = centre; _pitch = -0.5f;
        _dist = float.TryParse(System.Environment.GetEnvironmentVariable("TPW_VEHICLE_DIST"), out var dd) ? dd : 45f;
        GD.Print($"[vehicle] {want} at status {stop}: centre ({centre.X:F1}, {centre.Y:F1}, {centre.Z:F1}) over {world.Count} nodes; camera {_dist:F0} out");
        _vehicleShotIn = 3;
    }

    void ResetParkVehicles()
    {
        foreach (var v in _vehicles)
        {
            v.Presenter.Dispose();
            if (IsInstanceValid(v.Root)) v.Root.QueueFree();
        }
        _vehicles.Clear();
        _vehiclesKey = null;
    }
}
