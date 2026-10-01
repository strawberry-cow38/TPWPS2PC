using Godot;
using TPW.PS2.Data;
// ⚠ Same alias the lobby uses: `Animation` is ambiguous against Godot's own once `using Godot` is
// in scope, so the data-side one is named explicitly rather than left to the compiler.
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

/// <summary>⭐⭐⭐ THE TOUR RIDE'S VEHICLE -- the thing that flies. Master: "work on how tour rides
/// work. im assuming the planes follow a set path or something", then "it flies around the park".
///
/// ⭐⭐ THERE IS NO PATH. That is the finding, and it is why nothing could be found: the route is
/// GENERATED, not authored. `FUN_001e9b68` places the vehicle on a CIRCLE around the ride's own
/// position:
///
///     base = (obj[0x14], obj[0x18], obj[0x1c])          the ride's placed position
///     X    = base.x + (sin(angle) * radius) >> 5
///     Y    = base.y + 0x32
///     Z    = base.z + (cos(angle) * radius) >> 5
///
/// ⚠ AND THE TWO LOOKUPS WERE NOT ASSUMED TO BE sin/cos -- they were read:
///     `FUN_00195998(a) = sin(a * 0.00024414063 * 6.283) * 4096`
///     `FUN_00195940(a) = cos(a * 0.00024414063 * 6.283) * 4096`
/// `0.00024414063` is exactly 1/4096, so the angle is a **12-bit turn** -- the same convention the
/// particle system's `AngleUnitsPerTurn` already records. Nothing in this engine uses radians.
///
/// ⭐ Checked in all three places a path COULD have been authored, in all four worlds, and it is in
/// none of them: the script's TOUR opcode is inert in every branch (`0x1c1260`, read directly),
/// the fittings carry seats/camera/walkon nodes and no waypoints, and every tour `.aps` is
/// morph-only -- **0 position keys and 0 skeletal records across all eight files**.
///
/// ⭐ THE VEHICLE NAMES ITSELF, so nothing here is hardcoded per world:
/// `SupplementalMeshes[0].FileName` in the ride's own `.sam` -- `Bird.md2` (Jungle and Fantasy),
/// `Balloon.md2` (Hallow), `craft.md2` (Space). The authored name is the PC `.md2`; the PS2 ships
/// the same stem as `.mps`. Same shape as `Info.CreateParticleEffect`: the object says what it
/// needs and the engine does not keep a table.
///
/// ⚠⚠ THE RADIUS IS SMALLER THAN "AROUND THE PARK" AND I HAVE NOT HIDDEN THAT. `FUN_001e8f28`
/// (the class's init) sets the radius byte to `0xF`, and the arithmetic above turns 15 into
/// `sin * 4096 * 15 >> 5` = 1920 position units = **3 cells** against 640 units per cell. So the
/// decoded default is a 3-cell circle over the ride's own plot, not a lap of the park. Built
/// exactly as read rather than scaled up to match the description: if it looks too tight on
/// screen, then a radius setter elsewhere is missing and that is a finding, not a number to tune.
/// The one other write I found sets it to 1 (`FUN_001e9b68`), which is tighter still.
///
/// ⚠ THE SPEED IS THIS PORT'S. The car update `FUN_001ea980` ramps a speed at `+0x24` by ±1 per
/// tick toward a target at `+0x20` and advances the angle by it -- so the SHAPE is read, but where
/// the target comes from is not, and `TurnPerTick` below is chosen, not decoded. Named here so it
/// reads as an invented number and not a measured one.</summary>
public partial class Viewer
{
    /// <summary>`0x23efe8`-style 12-bit turn, the same one the particle spiral uses.</summary>
    public const int TourAngleUnitsPerTurn = 4096;

    /// <summary>`FUN_001e8f28` init: the radius byte is `0xF`.</summary>
    public const int TourRadiusUnits = 0xF;

    /// <summary>⚠⚠ THE ONE NUMBER THAT IS NOT DECODED: how many position units are in a cell for a
    /// RIDE OBJECT. Master: "its flying on the ground".
    ///
    /// The arithmetic is the disc's and is not in doubt -- `(sin * 4096 * radius) >> 5` out, and
    /// `+ 0x32` up. What I got wrong was the SCALE: 640 units per cell is the PARTICLE system's
    /// constant (`ParticleTemplate.PositionUnitsPerCell`) and I carried it into ride-object space,
    /// which is a different subsystem that never promised the same unit. Guests, for one, use 256.
    ///
    /// ⭐ THE SHAPE IS UNIT-FREE AND IS RIGHT WHATEVER THIS IS. Radius is `128 * r` units and the
    /// lift is 50, so the circle is always **38x wider than it is high** -- a flat lap, never a
    /// steep climb. Only how big it is depends on this number:
    ///
    ///     640 (particles) -> 3.0 cells out, 0.08 up    -- what master saw: on the ground
    ///     256 (guests)    -> 7.5 cells out, 0.20 up
    ///      64             -> 30 cells out,  0.78 up
    ///      16             -> 120 cells out, 3.1 up     -- a lap of the whole park and beyond
    ///
    /// It is ONE named number rather than two tuned ones precisely so the open question stays
    /// visible and a future calibration (a savestate read of a placed ride's `+0x14/+0x18/+0x1c`
    /// against its known cell) settles it in one place.</summary>
    public const float TourPositionUnitsPerCell = 64f;

    /// <summary>`(sin * 4096 * radius) >> 5`, in cells.</summary>
    public const float TourCellsPerRadiusUnit = 4096f / 32f / TourPositionUnitsPerCell;

    /// <summary>`base.y + 0x32`, in cells.</summary>
    public const float TourLiftCells = 0x32 / TourPositionUnitsPerCell;

    /// <summary>⚠ OURS, not the disc's -- see the class note. 12-bit units per console tick.</summary>
    public const int TourTurnPerTick = 12;

    sealed class TourVehicle
    {
        public AnimatedModel Model;
        public Vector3 Centre;     // the ride's own placed centre, in world units (1 = 1 cell)
        public float Angle;        // 12-bit turn units, fractional so a slow turn is smooth
        public float ModelTime;
        public string Name;
    }

    readonly System.Collections.Generic.List<TourVehicle> _tourVehicles = new();

    /// <summary>⭐ Called where a placed ride is registered. A ride with no
    /// `SupplementalMeshes[0]` is not a tour ride and silently gets nothing -- the field is the
    /// test, so no list of ride names is kept anywhere.</summary>
    void SpawnTourVehicle(ParkRide ride, RideDefinition def, Vector3 centre, Node3D station = null)
    {
        if (def == null || ride == null) return;
        if (!def.Fields.TryGetValue("SupplementalMeshes[0].FileName", out var raw)) return;
        string stem = raw.Trim().Trim('"');
        int dot = stem.LastIndexOf('.');
        if (dot > 0) stem = stem[..dot];                  // "Bird.md2" -> "Bird"; the PS2 ships .mps
        if (stem.Length == 0) return;

        var assets = _lib?.Rides.FirstOrDefault(r =>
            string.Equals(Leaf(r.Name), stem + ".mps", System.StringComparison.OrdinalIgnoreCase));
        if (assets == null)
        {
            // ⚠ Named, not swallowed: the .sam asked for a mesh this archive does not have, which
            // is a data question and not something to paper over with a default vehicle.
            GD.PrintErr($"[tour] {ride.Name}: .sam asks for \"{raw.Trim()}\" -> no {stem}.mps in this world");
            return;
        }
        var model = LoadPlaceable(assets, out var anim, out _);
        if (model?.Root == null) { GD.PrintErr($"[tour] {ride.Name}: {stem}.mps would not load"); return; }
        // ⭐ Its own morph animation is the flapping/bobbing -- the part of the ride that IS
        // authored. Section 5 at rate 1.0, the same call the lobby and the laptop preview make.
        var rec = anim?.Records().FirstOrDefault(r => r.Slot == 5 && r.Skeletal)
               ?? anim?.Records().FirstOrDefault(r => r.Slot == 5);
        if (rec != null) { model.UseRecord(rec); model.SetFrame(0); }

        AddChild(model.Root);
        var v = new TourVehicle { Model = model, Centre = centre, Name = $"{ride.Name} {stem}" };
        PlaceTourVehicle(v);
        _tourVehicles.Add(v);
        // ⚠ INSTRUMENT, after the first fix did not make it appear. "It spawned" and "it is on
        // screen" are different claims and the log only supported the first one.
        var (blo, bhi) = Park.DrawnBounds(model.Root, inParent: true);
        GD.Print($"[tour] {ride.Name}: STATION at "
               + $"{(station == null ? "(none)" : station.Position.Snapped(Vector3.One * 0.01f).ToString())}, "
               + $"circle centre {centre.Snapped(Vector3.One * 0.01f)}");
        GD.Print($"[tour] {ride.Name}: {stem} root at {model.Root.Position.Snapped(Vector3.One * 0.01f)} "
               + $"scale {model.Root.Basis.Scale.Snapped(Vector3.One * 0.001f)} visible={model.Root.Visible} "
               + $"parent={model.Root.GetParent()?.Name} frames={model.Frames} "
               + $"drawn x {blo.X:F2}..{bhi.X:F2} y {blo.Y:F2}..{bhi.Y:F2} z {blo.Z:F2}..{bhi.Z:F2}");
        GD.Print($"[tour] {ride.Name}: flying {stem}.mps on a generated circle -- "
               + $"radius {TourRadiusUnits} units = {TourRadiusUnits * TourCellsPerRadiusUnit:F2} cells, "
               + $"lift {TourLiftCells:F3} cells, {TourTurnPerTick} angle units/tick "
               + $"= a lap every {TourAngleUnitsPerTurn / (float)TourTurnPerTick * ParkSim.TickMilliseconds / 1000f:F1}s");
    }

    /// <summary>The console's own placement, in the port's units.</summary>
    void PlaceTourVehicle(TourVehicle v)
    {
        if (v.Model?.Root == null || !IsInstanceValid(v.Model.Root)) return;
        float a = v.Angle / TourAngleUnitsPerTurn * Mathf.Tau;
        float r = TourRadiusUnits * TourCellsPerRadiusUnit;
        v.Model.Root.Position = v.Centre + new Vector3(Mathf.Sin(a) * r, TourLiftCells, Mathf.Cos(a) * r);
        // ⚠ FACING IS NOT READ -- the console builds its basis in `FUN_00115ae8`, which was not
        // decompiled -- so the vehicle is turned along its own travel, which is what anything
        // flying a circle must do. The TANGENT is exact: differentiating the position above gives
        // (cos a, 0, -sin a).
        //
        // ⚠⚠ NEGATED, because the two conventions disagree. Godot's `LookingAt` aims the basis's
        // **-Z** at the direction given, and these models face **+Z** (censused 8 of 8 on the
        // lobby's park nodes). Without the sign the bird flew its circle perfectly and backwards,
        // which is exactly what master saw.
        var travel = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
        v.Model.Root.Basis = Basis.LookingAt(-travel, Vector3.Up);
    }

    /// <summary>One frame. ⚠ On the PARK's clock, not the frame's: the angle is per console tick.</summary>
    void StepTourRides(double delta)
    {
        if (_tourVehicles.Count == 0) return;
        float ticks = (float)delta * 1000f / ParkSim.TickMilliseconds;
        for (int i = _tourVehicles.Count - 1; i >= 0; i--)
        {
            var v = _tourVehicles[i];
            if (v.Model?.Root == null || !IsInstanceValid(v.Model.Root)) { _tourVehicles.RemoveAt(i); continue; }
            v.Angle = Mathf.PosMod(v.Angle + TourTurnPerTick * ticks, TourAngleUnitsPerTurn);
            PlaceTourVehicle(v);
            if (v.Model.Frames > 0)
            {
                v.ModelTime += (float)delta * Aps.Fps;
                v.Model.SetFrame(v.ModelTime % v.Model.Frames);
            }
        }
    }

    /// <summary>⚠ Dropped with the park, like every other placed thing -- a survivor would fly over
    /// the next world's sea.</summary>
    void ClearTourVehicles()
    {
        foreach (var v in _tourVehicles)
            if (v.Model?.Root != null && IsInstanceValid(v.Model.Root)) v.Model.Root.QueueFree();
        _tourVehicles.Clear();
    }
}
