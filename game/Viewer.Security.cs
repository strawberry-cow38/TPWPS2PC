using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

/// <summary>
/// ⭐⭐ GUARDS AND ENTERTAINERS IN THE PARK (staff step 4, findings/staff-mechanics-guards.md §5,
/// staff-handymen-entertainers.md §1.5, §4, §5): what the core's <see cref="Guard"/>,
/// <see cref="Entertainer"/> and the guests' side of <see cref="ParkStaff"/> need from a view, and
/// what they give it to draw.
///
/// - The gate: the guards share the entrance flow's turnstile coordinator (<see cref="ParkStaff.Gate"/>)
///   and read the staging cell `+2/+3` from the same table the flow does (<see cref="NativeBusCatalogue"/>).
/// - The carried guest: `0x140880` builds a COPY of the caught guest's model playing logical 17, drawn
///   at the guard's position and yaw (<see cref="CarriedGuest"/>) -- here the kid body that guest wore
///   (<see cref="GuestModels"/> by id), posed through the same dispatcher the staff use.
/// - The guard's carry pose (16) and the show (16, then 11) are the members' own logicals, drawn by
///   Viewer.Staff.cs; the one-shot cut flag `0x200` is consumed by that push, as `0x1921D0` does.
/// - Watchers: a guest in state 0x1C stands (the walk is paused in core) and FACES the entertainer
///   (`0x2107A0` writes the facing every update); see <see cref="WatchHeading"/>.
/// - The stink: each entry of the prank stink table is a `YellowStink` (template 50) emitter at the
///   position `0x1822D0` computed, stopped when the sweep removes the entry.
///
/// ⚠ ADAPTERS: the guest's logical 4 (heckle) and 2 (watching) are not drawn -- ordinary guests here
/// walk and idle through <see cref="Gait"/>, not through the dispatcher -- a watcher is drawn standing
/// and turned; the copy is drawn from a fired guard's slot too (natively it stays registered), at its
/// last position; the stink's emitter is the port's <see cref="RideParticles"/>, whose motion is the
/// port's (see that class).
/// </summary>
public partial class Viewer
{
    sealed class CopyActor
    {
        public uint Serial;
        public Node3D Node;
        public AnimatedModel Drawn;
        public NativeGuestAnimation Animation;
        public Dictionary<(int Slot, int Variant), Aps.Record> Records;
        public Aps.Record Showing;
        public string Where;
    }
    readonly Dictionary<Guard, CopyActor> _copyActors = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<PrankStink, Node3D> _stinkHolders = new(ReferenceEqualityComparer.Instance);
    ParticleLibrary _stinkLibrary;
    AssetLibrary _stinkWad;
    bool _stinkLibraryFailed;

    /// <summary>⭐ Called once a park's staff exist (<see cref="EnsureStaff"/>): the gate, the staging
    /// cell, the handle-aware sounds, the animation query and the stink's particles.</summary>
    void AttachSecurity()
    {
        if (_staff == null) return;
        _staff.Gate = _entranceFlow;
        try
        {
            var table = _busCatalogue ?? NativeBusCatalogue.Read(_lib.Disc, NativeParkSelection.Ordinary(_lib.WadName, _terrainPath));
            _staff.StagingCell = table.StagingPoint;
        }
        catch (Exception e)
        {
            _staff.StagingCell = null;
            GD.PrintErr($"[staff] no entrance table entry for the guards' staging point ({e.Message}): a guard who catches someone cannot carry him out");
        }
        _staff.SecuritySound = PlaySecuritySound;
        _staff.AnimationState = StaffAnimationState;
        _staff.Stinks.Started = OnStinkStarted;
        _staff.Stinks.Stopped = OnStinkStopped;
        _staff.Heckled = (guest, ent) => GD.Print($"[staff] guest {guest} heckles {ent} (logical 4 on the guest is not drawn here)");
        _staff.GuestCaught = (guard, guest) => GD.Print($"[staff] {guard} caught guest {guest}: removed from the park, carried out as a copy");
        GD.Print($"[staff] guards: turnstile {(_staff.Gate == null ? "NONE (legacy entrance: a staged guard crosses at once)" : "shared with the entrance flow")}, "
               + $"staging {_staff.StagingCell?.ToString() ?? "unknown"}");
    }

    /// <summary>Per park tick, after the staff updates: the gate follows a flow built or dropped later,
    /// and the copies' models take their update (one per staff update, like the members').</summary>
    void TickSecurity(uint updates)
    {
        if (_staff == null) return;
        if (!ReferenceEquals(_staff.Gate, _entranceFlow)) _staff.Gate = _entranceFlow;
        SyncCopyActors();
        for (uint i = 0; i < updates; i++)
            foreach (var c in _copyActors.Values)
                c.Animation?.Update(ParkSim.TickMilliseconds, _staffAnimationRand.Next);
    }

    void ResetSecurity()
    {
        foreach (var c in _copyActors.Values) if (c.Node != null && IsInstanceValid(c.Node)) c.Node.QueueFree();
        _copyActors.Clear();
        foreach (var h in _stinkHolders.Values) if (h != null && IsInstanceValid(h)) h.QueueFree();
        _stinkHolders.Clear();
        if (_staff != null) { _staff.Stinks.Started = null; _staff.Stinks.Stopped = null; _staff.Gate = null; }
    }

    /// <summary>⭐ A guard's or an entertainer's sound WITH its handle (`0x111428` with `&amp;handle`):
    /// bank 8 is `AUDIO/GLOBAL/staf` (<see cref="SoundGroup.GlobalStaff"/>). A handle still sounding is
    /// not started again -- the guard's 0x89 is raised on every walk tick while he carries.</summary>
    void PlaySecuritySound(StaffMember m, int bank, int eventId, int handle)
    {
        if (bank != 8) { GD.Print($"[staff] {m}: sound bank {bank} event 0x{eventId:X} has no mapping here"); return; }
        _sounds ??= MakeSounds();
        int voice = 0x30000000 | (int)(m.Serial & 0xFFFFFF);
        if (_sounds == null || _sounds.Sounding(voice, handle)) return;
        _sounds.Cue(voice, $"staff {m.Kind}#{m.PoolSlot}", _parkTicks * ParkSim.TickMilliseconds, RseOpcode.EVENT,
                    (int)SoundGroup.GlobalStaff, -1, eventId, handle, StaffWorld(m.CellPosition));
    }

    /// <summary>`0x10EC48` on the member's drawn model: its control block's CURRENT logical and phase.
    /// No drawn model → null (the core then takes the carry's first section as played).</summary>
    (byte Current, byte Phase)? StaffAnimationState(StaffMember m)
    {
        if (!_staffActors.TryGetValue(m, out var a) || a.Serial != m.Serial || a.Animation == null) return null;
        return (a.Animation.Control.Current, a.Animation.Control.Phase);
    }

    // ---------------------------------------------------------------------------------------------
    // The carried copy.

    /// <summary>Every guard slot's copy, active or fired: the copy is its own registered model.</summary>
    IEnumerable<Guard> GuardSlots() => _staff == null ? Enumerable.Empty<Guard>()
        : _staff.Active(StaffKind.Guard).Concat(_staff.Free(StaffKind.Guard)).OfType<Guard>();

    void SyncCopyActors()
    {
        var wanted = new HashSet<Guard>(ReferenceEqualityComparer.Instance);
        foreach (var g in GuardSlots()) if (g.Carried != null) wanted.Add(g);
        foreach (var gone in _copyActors.Keys.Where(g => !wanted.Contains(g) || _copyActors[g].Serial != g.Carried.Serial).ToArray())
        {
            if (_copyActors[gone].Node is { } n && IsInstanceValid(n)) n.QueueFree();
            _copyActors.Remove(gone);
        }
        foreach (var g in wanted)
            if (!_copyActors.ContainsKey(g)) _copyActors[g] = MakeCopyActor(g.Carried);
    }

    /// <summary>`0x140880`'s model: the guest's own body (the kid <see cref="GuestModels"/> gives that id),
    /// its logical 17 requested with flags 2 at creation. Records come from the file that holds the
    /// kid's tracks (<see cref="SittingRecord"/>: own, or Boy1a's for the Shared boys).</summary>
    CopyActor MakeCopyActor(CarriedGuest carried)
    {
        var actor = new CopyActor { Serial = carried.Serial };
        actor.Node = new Node3D { Name = $"Carried_{carried.GuestId}" };
        (_staffRoot ?? _guestRoot ?? (Node)this).AddChild(actor.Node);
        string path = GuestModels[(carried.GuestId - 1) % GuestModels.Length];
        try
        {
            EnsureCharLib();
            var model = CharModel(path);
            var (aps, _, where) = SittingRecord(path);
            var records = new Dictionary<(int, int), Aps.Record>();
            if (aps != null)
                foreach (var section in aps.Records().GroupBy(r => r.Slot))
                {
                    int variant = 0;
                    foreach (var record in section) records[(section.Key, variant++)] = record;
                }
            var drawn = new AnimatedModel(model, aps, null, m => CharTexture(path, m));
            drawn.SetFrame(0);
            var (lo, hi) = Park.DrawnBounds(drawn.Root, inParent: true);
            drawn.Root.Position = new Vector3(-(lo.X + hi.X) / 2, -lo.Y, -(lo.Z + hi.Z) / 2);
            actor.Node.AddChild(drawn.Root);
            actor.Drawn = drawn; actor.Records = records; actor.Where = where;
            _logicalAnimations ??= NativeLogicalAnimationTable.Read(_lib.Disc);
            actor.Animation = new NativeGuestAnimation(_logicalAnimations,
                (s, v) => records.TryGetValue((s, v), out var r) && !r.Shared ? r.DurationFrames : null)
                { Requested = CarriedGuest.Logical };
            actor.Animation.Push(2);                                   // vt+0x5C(1.0, copy, 0x11, 0, 0, 2)
            GD.Print($"[staff] carried copy of guest {carried.GuestId}: {path}, logical 17 over {records.Count} records ({where})");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[staff] carried copy {path} would not build: {e.Message}");
            foreach (var c in actor.Node.GetChildren()) c.QueueFree();
            actor.Drawn = null; actor.Animation = null;
        }
        return actor;
    }

    /// <summary>Draw each copy where `0x1409E8` last put it, at the guard's yaw (`π − facing`: the
    /// `0 - fmod(facing + 3π, 2π)` it hands the copy is the same angle), posed by the dispatcher.</summary>
    void PlaceCarried()
    {
        if (_staff == null) return;
        SyncCopyActors();
        foreach (var (guard, c) in _copyActors)
        {
            var carried = guard.Carried;
            if (carried == null || c.Node == null) continue;
            c.Node.Visible = c.Drawn != null;
            var p = new Vector3(carried.Position.X / 256f, 0, carried.Position.Z / 256f);
            c.Node.Position = GuestWorld(p, new ParkCell(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z)));
            float yaw = Mathf.PosMod(Mathf.Pi - carried.FacingQuarterTurns * (Mathf.Pi / 2), Mathf.Tau);
            c.Node.Basis = WalkBasis(GuestHeading(StaffHeading(yaw)));
            if (c.Drawn == null || c.Animation == null) continue;
            var anim = c.Animation;
            var (slot, variant, frame, holding) = anim.Held is { } h
                ? (h.Slot, h.Variant, anim.Duration, true)
                : (anim.Slot, anim.Variant, anim.Frame, false);
            if (slot == NativeAnimationDescriptor.Inactive) continue;
            if (!c.Records.TryGetValue((slot, variant), out var record) || record.Shared) continue;
            if (!ReferenceEquals(c.Showing, record)) { c.Drawn.UseRecord(record); c.Showing = record; }
            float duration = Math.Max(1, record.DurationFrames);
            c.Drawn.SetFrame(holding && !record.Skeletal ? duration : Math.Min(frame, duration - 0.0001f));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Watchers.

    /// <summary>The grid direction a watching guest faces: `0x2107A0`'s facing (quarter turns: 0 +z,
    /// 1 +x, 2 -z, 3 -x), or null for a guest not watching.</summary>
    Vector3? WatchHeading(int guest)
    {
        if (_staff?.Watching.GetValueOrDefault(guest) is not { } w) return null;
        return w.FacingQuarterTurns switch { 1 => new Vector3(1, 0, 0), 2 => new Vector3(0, 0, -1), 3 => new Vector3(-1, 0, 0), _ => new Vector3(0, 0, 1) };
    }
    bool GuestWatching(int guest) => _staff?.IsWatching(guest) == true;

    // ---------------------------------------------------------------------------------------------
    // The stink.

    /// <summary>⭐ `0x1822D0`'s spawn of particle 50, `YellowStink`, at the entry's position: one
    /// emitter per entry, in its own holder, so `0x1824A8` can stop exactly that one.</summary>
    void OnStinkStarted(PrankStink stink)
    {
        if (_stinkLibrary == null && !_stinkLibraryFailed)
        {
            try
            {
                _stinkWad = new AssetLibrary(_discPath);
                _stinkWad.OpenWad("/DATA/PARTICLE.WAD");
                _stinkLibrary = new ParticleLibrary(_stinkWad.Read(_stinkWad.Wad.Find("/Tp2.plb")));
            }
            catch (Exception e) { _stinkLibraryFailed = true; GD.PrintErr($"[staff] no particle library for the stink: {e.Message}"); }
        }
        if (_stinkLibrary == null || _staffRoot == null || !IsInstanceValid(_staffRoot)) return;
        var holder = new Node3D { Name = $"Stink_{stink.CellX}_{stink.CellZ}" };
        _staffRoot.AddChild(holder);
        var at = StaffWorld(new System.Numerics.Vector3(stink.X, 0, stink.Z));
        var fx = new RideParticles(holder, _stinkLibrary, _stinkWad).Emit(PrankStink.ParticleTemplateId, at);
        _stinkHolders[stink] = holder;
        GD.Print($"[staff] prank stink at cell ({stink.CellX},{stink.CellZ}) -> {(fx?.Name ?? "no effect")} at ({stink.X:F2},{stink.Z:F2})");
    }

    /// <summary>`0x1824A8` → `0x18AAE0`: the emitter stops; what is alive finishes its life, then the
    /// holder goes.</summary>
    void OnStinkStopped(PrankStink stink)
    {
        if (!_stinkHolders.Remove(stink, out var holder) || holder == null || !IsInstanceValid(holder)) return;
        float life = 0;
        foreach (var p in holder.FindChildren("*", "", true, false).OfType<CpuParticles3D>())
        {
            p.Emitting = false;
            life = Math.Max(life, (float)p.Lifetime);
        }
        GetTree().CreateTimer(life + 0.5).Timeout += () => { if (IsInstanceValid(holder)) holder.QueueFree(); };
        GD.Print($"[staff] prank stink at cell ({stink.CellX},{stink.CellZ}) stopped by a sweep");
    }

    /// <summary>For a check: the stink's emitters under their holder.</summary>
    IReadOnlyList<CpuParticles3D> StinkEmitters(PrankStink stink)
        => _stinkHolders.TryGetValue(stink, out var h) && IsInstanceValid(h)
            ? h.FindChildren("*", "", true, false).OfType<CpuParticles3D>().ToList()
            : new List<CpuParticles3D>();
}
