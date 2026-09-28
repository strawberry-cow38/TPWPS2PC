using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPWPS2Viewer;

/// <summary>
/// ⭐⭐ STAFF IN THE PARK: every park the viewer builds gets a <see cref="ParkStaff"/>, its members
/// and its litter are drawn, and the laptop's Hire panel hands a candidate to the hire tool (mode 1),
/// which carries him on the cursor until a left click puts him down. findings/staff.md,
/// staff-person.md §5, staff-handymen-entertainers.md §1, staff-management.md §2.
///
/// What is native here, and where it was read:
/// - the model is the registry's (<see cref="NativeModelRegistry"/>, `0x17D7E8`), so an entertainer
///   wears the park's costume (variant = park index + 1) and litter is `litter1..3`/`puke`;
/// - the pose comes from the member's logical request (`C+0x30`) through the SAME dispatcher the
///   guests use (<see cref="NativeGuestAnimation"/> over <see cref="NativeLogicalAnimationTable"/>),
///   pushed by the visual sync `0x192438 → 0x1921D0` only while shown, with flags 2 when
///   `C+0x2C &amp; 0x200` (<see cref="StaffMember.CutRecordOnPush"/>);
/// - section 0 is drawn AS section 0, the baked-vertex walk (findings/baked-walk.md) -- no section-1
///   stand-in: of the staff only the Guard has one;
/// - the yaw is `π − facing` (`0x1921D0`), hidden when not shown, drawn while held.
///
/// ⚠ ADAPTERS, each said once here and again where it lives:
/// - one model update per staff update, after it (the guests' adapter: the native order of
///   `1ACFC0` against the object update was not found); the push is gated on Shown, the model clock
///   is not;
/// - where a member stands on screen is the guests' presentation (<see cref="GuestWorld"/>: the
///   port's cell floor height, the node's origin at the feet of the walk's first frame);
/// - the placed-object list the staff read (<see cref="StaffPlacedFeatures"/>) is the sim's scripted
///   placements plus the scriptless ones the build tool registered;
/// - the Hire panel's tabs are a laptop menu level and its candidates are paged, because this port
///   splits Build &amp; Hire into two rows and draws no tab strip (see <see cref="ShowHireTabs"/>).
/// </summary>
public partial class Viewer
{
    /// <summary>The park's staff, attached to <see cref="_visitors"/> (null until the visitors exist).</summary>
    ParkStaff _staff;
    /// <summary>The coordinator <see cref="_staff"/> was built for; a different one means a new park.</summary>
    ParkVisitors _staffVisitors;
    NativeModelRegistry _modelRegistry;
    /// <summary>`[0x3952E4]` world and `[0x3952E8]` park index for the registry lookup; -1 when the
    /// park is not one of the eight ordinary ones (then only world-4, variant-0 entries match).</summary>
    int _staffWorld = -1, _staffPark = -1;
    Node3D _staffRoot;
    readonly NewlibRand _staffAnimationRand = new();

    sealed class StaffActor
    {
        public StaffMember Member;
        public uint Serial;
        public Node3D Node;
        public AnimatedModel Drawn;
        public Aps Anim;
        public NativeGuestAnimation Animation;
        public Dictionary<(int Slot, int Variant), Aps.Record> Records;
        public Aps.Record Showing;
        public string ModelPath;
        public Vector3 Prev;
        /// <summary>The native yaw last drawn, `π − facing` wrapped to [0, 2π).</summary>
        public float Yaw;
    }
    readonly SnapshotReferenceMap<StaffMember, StaffActor> _staffActors = new(ReferenceEqualityComparer.Instance);
    readonly SnapshotReferenceMap<LitterItem, (Node3D Node, uint Serial, int ModelId, string Path)> _litterActors = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<int> _staffModelMisses = new();
    // Retain the actual renderer, not just its Node, so saves can copy the last rendered pose
    // without calling SetFrame to manufacture one. Lifetime follows the existing litter owner.
    readonly Dictionary<Node3D,AnimatedModel> _litterDrawn = new(ReferenceEqualityComparer.Instance);

    /// <summary>The member the hire tool (mode 1, object `0x388EF0`) is carrying, or null.</summary>
    StaffMember _hireHeld;
    /// <summary>Whether the carry `0x128760` has put him at the cursor yet: until then his position
    /// is the slot's stale one, so he is not drawn.</summary>
    bool _hireCarried;
    /// <summary>For a capture: the cursor's world point in 1/256 cell, as <see cref="_cursorOverride"/>
    /// is its cell.</summary>
    Point? _cursorPointOverride;

    // ---------------------------------------------------------------------------------------------
    // Attach, per park.

    /// <summary>⭐ Build the park's staff once its visitors exist, and again when they change (a new
    /// park). The random stream is the guests' `1448E0` stream (<see cref="_guestRng"/>), the
    /// activation counter the entrance's, the calendar the viewer's.</summary>
    void EnsureStaff()
    {
        if (_visitors == null) return;
        if (_staff != null && ReferenceEquals(_staffVisitors, _visitors)) return;
        ResetStaff();
        NativeActivationSequence serials;
        try { serials = ActivationSequence(); }
        catch (Exception e)
        {
            serials = new NativeActivationSequence(0, "staff fallback: no ordinary-park activation counter (" + e.Message + ")");
            GD.PrintErr($"[staff] {serials.Origin}");
        }
        _staff = new ParkStaff(_visitors, _calendar, serials, n => _guestRng.Next(n));
        _staffVisitors = _visitors;
        _visitors.Staff = _staff;
        _staff.Features = StaffPlacedFeatures;
        _staff.AnimationReady = StaffAnimationReady;
        _sounds ??= MakeSounds();
        _staff.Sound = PlayStaffSound;
        _staff.Litter.Added = OnLitterAdded;
        _staff.Litter.Removed = OnLitterRemoved;
        // ⭐ Mechanics (findings/staff-mechanics-guards.md): the ride side's sounds, the staff's HANDLE plays
        // (chatter, repair noise) and the mouth a mechanic leaves by. The ride side's advisor messages
        // (0x36/0x37/0x38/0x39/0x87, the ride attached) and the staff's go to the park's advisor (AttachAdvisor).
        _visitors.Sim.RideSound = PlayRideServiceSound;
        _staff.HandleSound = PlayStaffHandleSound;
        _staff.HandlePlaying = StaffHandlePlaying;
        // ⚠ ADAPTER for vt+0xF4 (0x117280, the last cell of the ride's queue list): the drawn queue's
        // mouth when the path tool has one, else the core falls back to the stub (ParkStaff.LeaveCell).
        _staff.QueueMouth = ride => RideQueueShape(ride)?.Mouth;
        try { _modelRegistry ??= NativeModelRegistry.Read(_lib.Disc); }
        catch (Exception e) { GD.PrintErr($"[staff] model registry unreadable: {e.Message} -- staff and litter are not drawn"); }
        try
        {
            var selection = NativeParkSelection.Ordinary(_lib.WadName, _terrainPath);
            _staffWorld = selection.World; _staffPark = selection.Variant;
        }
        catch (Exception) { _staffWorld = _staffPark = -1; }
        if (_guestRoot != null && IsInstanceValid(_guestRoot))
        {
            _staffRoot = new Node3D { Name = "staff" };
            _guestRoot.AddChild(_staffRoot);
        }
        GD.Print($"[staff] attached to this park: world {_staffWorld} park {_staffPark}, "
               + $"{_modelRegistry?.Entries.Count ?? 0} registry entries; the toilet stand-in is off, handymen clean");
        AttachSecurity();                                                // Viewer.Security.cs: guards, entertainers
        AttachManagement();                                              // Viewer.Management.cs: strikes, training, rooms
        AttachAdvisor();                                                 // Viewer.Advisor.cs: every message, the rules
    }

    /// <summary>Drop the old park's staff, their nodes and the hire tool's hold.</summary>
    void ResetStaff()
    {
        ResetAdvisor();                                                  // Viewer.Advisor.cs: the head freed, the voice stopped
        ResetSecurity();
        EndPatrolTool();
        foreach (var a in _staffActors.Values) if (a.Node != null && IsInstanceValid(a.Node)) a.Node.QueueFree();
        foreach (var l in _litterActors.Values) if (l.Node != null && IsInstanceValid(l.Node)) l.Node.QueueFree();
        _staffActors.Clear(); _litterActors.Clear(); _litterDrawn.Clear(); _staffModelMisses.Clear();
        if (_staffRoot != null && IsInstanceValid(_staffRoot)) _staffRoot.QueueFree();
        _staffRoot = null;
        if (_staff != null) { _staff.Litter.Added = null; _staff.Litter.Removed = null; }
        if (_staffVisitors != null && ReferenceEquals(_staffVisitors.Staff, _staff)) _staffVisitors.Staff = null;
        _staff = null; _staffVisitors = null; _hireHeld = null; _hireCarried = false;
    }

    /// <summary>⚠ ADAPTER for the placed-object list `0x14CD30`: every scripted placement the sim
    /// holds (the default, <see cref="StaffFeature.Of"/>), then every scriptless one the build tool
    /// placed whose compiled record is a Feature with DBA `+0x2E` flags (bins, cameras): the sim
    /// only keeps a placement that runs a script. For those the entry cell is the origin and the
    /// status `+0xA2` is taken as 1 (standing): nothing of theirs is read by the ported jobs except
    /// the bin bit and the origin. ⚠ The native list's order is not traced.</summary>
    IEnumerable<StaffFeature> StaffPlacedFeatures()
    {
        var sim = _staffVisitors?.Sim;
        if (sim == null) yield break;
        var scripted = new HashSet<int>();
        foreach (var ride in sim.Rides)
        {
            scripted.Add(ride.Id);
            if (StaffFeature.Of(ride) is { } f) yield return f;
        }
        if (_park == null) yield break;
        foreach (var p in _busPlacements)
        {
            if (scripted.Contains(p.RuntimeId)) continue;
            if (p.Definition?.CompiledEntry is not { Kind: AssetResourceDatabase.AssetKind.Feature } record) continue;
            byte flags = record.RawFeatureFlags.GetValueOrDefault();
            if (flags == 0) continue;
            foreach (var placed in _park.Placed)
                if (placed.Id == p.RuntimeId)
                {
                    var origin = new ParkCell(placed.X, placed.Y);
                    yield return new StaffFeature(origin, origin, flags, 1, null, p.RuntimeId);
                    break;
                }
        }
    }

    /// <summary>`0x191E10` for a staff member: his drawn model's CURRENT logical (never the pending
    /// one) must be 9 or 13 while 9 or 13 is requested. A member with no model is "no visual",
    /// which the native test permits (⚠ natively a costume with no registry entry, park index 2,
    /// has a visual with handle 0 and blocks forever; the port has no such park).</summary>
    bool StaffAnimationReady(StaffMember m)
    {
        if (!_staffActors.TryGetValue(m, out var a) || a.Serial != m.Serial || a.Animation == null) return true;
        return NativeLogicalAnimationControl.MovementPermitted(m.LogicalRequest, a.Drawn != null, a.Animation.Control.Current);
    }

    /// <summary>A staff sound: bank 8 is `AUDIO/GLOBAL/staf`, whose map is `STAFSFX.MAP` --
    /// <see cref="SoundGroup.GlobalStaff"/>'s -- played as a one-shot at the member.
    /// ⚠ The native call keeps a handle per member (`P+0x58`/`+0x5C`); a one-shot has none.</summary>
    void PlayStaffSound(StaffMember m, int bank, int eventId)
    {
        if (bank != 8) { GD.Print($"[staff] {m}: sound bank {bank} event 0x{eventId:X} has no mapping here"); return; }
        _sounds?.Cue(0, $"staff {m.Kind}#{m.PoolSlot}", _parkTicks * ParkSim.TickMilliseconds, RseOpcode.EVENT,
                     (int)SoundGroup.GlobalStaff, -1, eventId, 0, StaffWorld(m.CellPosition));
    }

    /// <summary>A voice key per member for <see cref="RideSounds"/>: its handles (`P+0x58`/`+0x5C`/`+0x60`)
    /// are the tags, so "is that handle still playing" is <see cref="RideSounds.Sounding"/>.</summary>
    static int StaffVoice(StaffMember m) => 0x20000000 | (int)(m.Serial & 0xFFFFFF);

    /// <summary>⭐ A staff HANDLE play (`0x111428` with a handle): bank 8 (`AUDIO/GLOBAL/staf`,
    /// <see cref="SoundGroup.GlobalStaff"/>) for the mechanic's chatter 0xA2/0xA3, bank 2
    /// (`AUDIO/GLOBAL/ride`, <see cref="SoundGroup.GlobalRide"/>) for his repair noise 0x6F.
    /// ⚠ A handle that is still sounding is not started again (INFERRED, findings §3.2): without that
    /// the chatter, raised on 62 of every 63 ticks, would stack a voice per tick.</summary>
    void PlayStaffHandleSound(StaffMember m, int bank, int eventId, int handle)
    {
        int group = bank switch { 8 => (int)SoundGroup.GlobalStaff, 2 => (int)SoundGroup.GlobalRide, _ => -1 };
        if (group < 0) { GD.Print($"[staff] {m}: sound bank {bank} event 0x{eventId:X} has no mapping here"); return; }
        _sounds ??= MakeSounds();
        if (_sounds == null || _sounds.Sounding(StaffVoice(m), handle)) return;
        _sounds.Cue(StaffVoice(m), $"staff {m.Kind}#{m.PoolSlot}", _parkTicks * ParkSim.TickMilliseconds, RseOpcode.EVENT,
                    group, -1, eventId, handle, StaffWorld(m.CellPosition));
    }

    /// <summary>⚠ ADAPTER for `0x111CC8(audio, &amp;handle)`: the view's own voice on that handle.</summary>
    bool StaffHandlePlaying(StaffMember m, int handle) => _sounds != null && _sounds.Sounding(StaffVoice(m), handle);

    /// <summary>⭐ A ride-side sound from <see cref="ParkSim.RideSound"/>: native category 2 is
    /// `AUDIO/GLOBAL/ride` (<see cref="SoundGroup.GlobalRide"/>) -- 0x70 on a breakdown for good, 0x18 on
    /// condemnation, 0xB8/0xE1 on an upgrade -- at the ride's centre. ⚠ The enter-4 call's first
    /// argument (0xC) is not a registry id (0x111150 special-cases it, untraced), so it is logged.
    /// ⚠ Whether the voice is 3D is <see cref="RideSounds"/>' rule for the group, so the native
    /// non-positional 0x18 (`{0,0,0}`, flag 1) is placed at the ride like the others.</summary>
    void PlayRideServiceSound(ParkRide ride, int category, int eventId, bool positional)
    {
        if (category != 2)
        {
            GD.Print($"[ride] {DisplayName(ride)}: native sound ({category}, 0x{eventId:X}) has no mapping here");
            return;
        }
        _sounds ??= MakeSounds();
        if (_sounds == null || _park == null) return;
        var centre = new Vector3(ride.Origin.X + ride.Width / 2f, 0, ride.Origin.Z + ride.Height / 2f);
        _sounds.Cue(ride.Id, DisplayName(ride), _parkTicks * ParkSim.TickMilliseconds, RseOpcode.EVENT,
                    (int)SoundGroup.GlobalRide, -1, eventId, 0x6000 + eventId, GuestWorld(centre, ride.Origin));
    }

    Vector3 StaffWorld(System.Numerics.Vector3 cellPosition)
    {
        var p = Cell(cellPosition);
        return GuestWorld(p, new ParkCell(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z)));
    }

    // ---------------------------------------------------------------------------------------------
    // The tick: before and after the visitors step.

    void SnapshotStaff()
    {
        if (_staff == null) return;
        SyncStaffActors();
        foreach (var a in _staffActors.Values) a.Prev = Cell(a.Member.CellPosition);
    }

    /// <summary>After <paramref name="updates"/> staff updates: the visual sync's push (`0x1921D0`,
    /// only while shown, flags 2 on a cut) and one model update (`1ACFC0`) per update.</summary>
    void TickStaffAnimations(uint updates)
    {
        if (_staff == null || updates == 0) return;
        SyncStaffActors();
        // ⭐ In the MAP-LIST order (`ParkStaff.Members`, newest first: `0x14BE60` walks it), not the actor
        // dictionary's. Every update draws from the shared `_staffAnimationRand`, so the order IS the draw
        // order; a Dictionary's enumeration after removals is a .NET implementation detail (cow tools,
        // 2026-09-28, while astraclaw's save work was preserving dictionary "holes" to keep it stable).
        // ⚠ Members' model updates still run as one pass after the staff updates rather than interleaved
        // inside each member's update; that interleaving is not traced.
        for (uint i = 0; i < updates; i++)
            foreach (var m in _staff.Members)
            {
                if (!_staffActors.TryGetValue(m, out var a) || a.Animation == null) continue;
                if (m.Shown)
                {
                    a.Animation.Requested = (a.Animation.Requested & ~0x1f) | (m.LogicalRequest & 0x1f);
                    a.Animation.Push(m.CutRecordOnPush ? 2 : 0);
                    m.CutRecordPushed();                                 // 0x1921D0 clears 0x200 after the push
                }
                a.Animation.Update(ParkSim.TickMilliseconds, _staffAnimationRand.Next);
            }
        TickSecurity(updates);
    }

    /// <summary>One node per member of <see cref="ParkStaff.Members"/>, rebuilt when the slot is
    /// activated again (a new serial is a new hire in the same pooled object).</summary>
    void SyncStaffActors()
    {
        if (_staff == null) return;
        var live = new HashSet<StaffMember>(_staff.Members, ReferenceEqualityComparer.Instance);
        foreach (var gone in _staffActors.Keys.Where(m => !live.Contains(m) || _staffActors[m].Serial != m.Serial).ToArray())
        {
            if (_staffActors[gone].Node is { } n && IsInstanceValid(n)) n.QueueFree();
            _staffActors.Remove(gone);
        }
        foreach (var m in _staff.Members)
            if (!_staffActors.ContainsKey(m)) _staffActors[m] = MakeStaffActor(m);
    }

    void EnsureCharLib()
    {
        if (_charLib == null) { _charLib = new AssetLibrary(_discPath); _charLib.OpenWad("/DATA/DATA.WAD"); }
    }

    /// <summary>The model a registry id resolves to in this park, as a DATA.WAD path, or null (logged
    /// once per id).</summary>
    string RegistryModelPath(int id)
    {
        if (_modelRegistry == null) return null;
        var entry = _modelRegistry.Find(id, _staffWorld, _staffPark);
        if (entry == null)
        {
            if (_staffModelMisses.Add(id)) GD.PrintErr($"[staff] registry: no model {id} for world {_staffWorld} park {_staffPark} (0x17D7E8 finds none)");
            return null;
        }
        var (archive, path) = _modelRegistry.ModelPath(entry);
        if (archive != "DATA")
        {
            if (_staffModelMisses.Add(id)) GD.PrintErr($"[staff] registry: model {id} {entry.Name} is in {archive}.WAD, which this view does not open");
            return null;
        }
        return path;
    }

    Model CharModel(string path)
    {
        EnsureCharLib();
        if (_charModels.TryGetValue(path, out var model)) return model;
        var entry = _charLib.Wad.Find(path) ?? throw new InvalidDataException($"DATA.WAD has no {path}");
        return _charModels[path] = new Model(_charLib.Read(entry));
    }

    Aps CharAps(string modelPath)
    {
        EnsureCharLib();
        string path = System.IO.Path.ChangeExtension(modelPath, ".aps");
        if (_charAnims.TryGetValue(path, out var aps)) return aps;
        var entry = _charLib.Wad.Find(path);
        aps = entry == null ? null : new Aps(_charLib.Read(entry));
        _charAnims[path] = aps;
        return aps;
    }

    StaffActor MakeStaffActor(StaffMember m)
    {
        var actor = new StaffActor { Member = m, Serial = m.Serial, Prev = Cell(m.CellPosition) };
        actor.Node = new Node3D { Name = $"Staff_{m.Kind}_{m.PoolSlot}" };
        (_staffRoot ?? _guestRoot ?? (Node)this).AddChild(actor.Node);
        _logicalAnimations ??= NativeLogicalAnimationTable.Read(_lib.Disc);
        string path = RegistryModelPath(m.ModelId);
        if (path == null) return actor;
        try
        {
            var model = CharModel(path);
            var aps = CharAps(path) ?? throw new InvalidDataException($"no .aps beside {path}");
            var records = new Dictionary<(int, int), Aps.Record>();
            foreach (var section in aps.Records().GroupBy(r => r.Slot))
            {
                int variant = 0;
                foreach (var record in section) records[(section.Key, variant++)] = record;
            }
            var walk = records.GetValueOrDefault((0, 0));
            // ⚠ skeletalHideLists: a skeletal record's own +0x18 list is applied (INFERRED, see
            // AnimationNodeVisibility.ListedNodes) -- else a sweeping handyman holds his strike placard.
            var drawn = new AnimatedModel(model, aps, walk, mat => CharTexture(path, mat), skeletalHideLists: true);
            drawn.SetFrame(0);
            // ⚠ The guests' presentation: feet on the node's origin, centred on it -- measured on the
            // walk's first frame, because several staff models' authored vertices are another pose
            // in another place (findings/baked-walk.md: Dino's body sits 48,000 units off).
            var (lo, hi) = Park.DrawnBounds(drawn.Root, inParent: true);
            drawn.Root.Position = new Vector3(-(lo.X + hi.X) / 2, -lo.Y, -(lo.Z + hi.Z) / 2);
            actor.Node.AddChild(drawn.Root);
            actor.Drawn = drawn; actor.Anim = aps; actor.Records = records; actor.Showing = walk; actor.ModelPath = path;
            // 1ACFC0 checks the MODEL's own set (17D7C0/17D7C8), so durations are this .aps's own.
            actor.Animation = new NativeGuestAnimation(_logicalAnimations,
                (s, v) => records.TryGetValue((s, v), out var r) ? r.DurationFrames : null)
                { Requested = m.LogicalRequest & 0x1f };
            GD.Print($"[staff] {m}: model {m.ModelId} -> {path} ({records.Count} records, "
                   + $"s0 {(walk == null ? "none" : $"{walk.DurationFrames} frames, {drawn.BakedParts} baked parts")}), "
                   + $"{hi.Y - lo.Y:F2} tall");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[staff] {m}: model {path} would not build: {e.Message}");
            foreach (var c in actor.Node.GetChildren()) c.QueueFree();
            actor.Drawn = null; actor.Animation = null;
        }
        return actor;
    }

    // ---------------------------------------------------------------------------------------------
    // The frame.

    /// <summary>⭐ Draw every member where the sim has him: between the last two ticks (a held member
    /// exactly where the carry put him), turned by `π − facing`, hidden when not shown, posed by the
    /// dispatcher's (section, variant).</summary>
    void PlaceStaff(float alpha)
    {
        if (_staff == null) return;
        SyncStaffActors();
        foreach (var a in _staffActors.Values)
        {
            var m = a.Member;
            bool held = m.Held;
            a.Node.Visible = a.Drawn != null && m.Shown && (!held || !ReferenceEquals(m, _hireHeld) || _hireCarried);
            var now = Cell(m.CellPosition);
            var p = held ? now : a.Prev.Lerp(now, alpha);
            a.Node.Position = GuestWorld(p, new ParkCell(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z)));
            a.Yaw = Mathf.PosMod(Mathf.Pi - m.FacingRadians, Mathf.Tau);
            a.Node.Basis = WalkBasis(GuestHeading(StaffHeading(a.Yaw)));
            if (a.Drawn != null && a.Animation != null) PoseStaff(a, alpha);
        }
        PlaceCarried();
    }

    /// <summary>⚠ The grid direction a native yaw points the model along. `0x1921D0` hands the visual
    /// `π − facing`; the rotation sense and the model's forward are the ones under which that yaw
    /// points along the facing the walk step wrote (`0x191E98`: dz &gt; 0 → 0, dx &gt; 0 → π/2, ...),
    /// i.e. forward = (sin yaw, −cos yaw). ⚠ INFERRED from that table, not from the visual's code;
    /// the smoke checks the drawn forward against the steps he actually took.</summary>
    static Vector3 StaffHeading(float yaw) => new(Mathf.Sin(yaw), 0, -Mathf.Cos(yaw));

    /// <summary>The dispatcher's record on the drawn model: section 0 is the baked walk and is drawn
    /// as itself; a held pose (slot F after an unplayable answer) is the old record at its full
    /// duration, exactly as the guests' <see cref="NativeDrawnRecord"/> draws it.</summary>
    void PoseStaff(StaffActor a, float alpha)
    {
        var anim = a.Animation;
        var (slot, variant, frame, holding) = anim.Held is { } h
            ? (h.Slot, h.Variant, anim.Duration, true)
            : (anim.Slot, anim.Variant, anim.Frame + alpha * ParkSim.TickMilliseconds * NativeGuestAnimation.FramesPerSecond / 1000f, false);
        if (slot == NativeAnimationDescriptor.Inactive) return;             // never posed yet: as built
        if (!a.Records.TryGetValue((slot, variant), out var record) || record.Shared) return;
        if (!ReferenceEquals(a.Showing, record)) { a.Drawn.UseRecord(record); a.Showing = record; }
        float duration = Math.Max(1, record.DurationFrames);
        // ⭐ A baked table holds duration + 1 frames and the held pose IS frame == duration
        // (0x1AD268..0x1AD278); a skeletal record is sampled just inside its end, as the guests' are.
        float f = holding && !record.Skeletal ? duration : Math.Min(frame, duration - 0.0001f);
        a.Drawn.SetFrame(f);
    }

    // ---------------------------------------------------------------------------------------------
    // Litter (findings/staff-handymen-entertainers.md §1).

    void OnLitterAdded(LitterItem item)
    {
        OnLitterRemoved(item);
        var node = new Node3D { Name = $"Litter_{item.PoolSlot}" };
        (_staffRoot ?? _guestRoot ?? (Node)this).AddChild(node);
        string path = RegistryModelPath(item.ModelId);
        if (path != null)
        {
            try
            {
                var drawn = new AnimatedModel(CharModel(path), null, null, mat => CharTexture(path, mat));
                drawn.SetFrame(0);
                node.AddChild(drawn.Root);
                _litterDrawn.Add(node,drawn);
            }
            catch (Exception e) { GD.PrintErr($"[staff] litter {item.ModelId} {path}: {e.Message}"); path = null; }
        }
        // ⚠ `0x15E468` places the model at (x, 0, z) and litter has no visual sync; the port stands it
        // on the cell's floor height like everything else it draws on this grid.
        node.Position = StaffWorld(new System.Numerics.Vector3(item.Position.X / 256f, 0, item.Position.Z / 256f));
        node.Visible = item.Shown && path != null;
        _litterActors[item] = (node, item.Serial, item.ModelId, path);
    }

    void OnLitterRemoved(LitterItem item)
    {
        if (!_litterActors.Remove(item, out var had)) return;
        _litterDrawn.Remove(had.Node);
        if (had.Node != null && IsInstanceValid(had.Node)) had.Node.QueueFree();
    }

    // ---------------------------------------------------------------------------------------------
    // The hire tool, mode 1 (findings/staff-management.md §2.2).

    /// <summary>⭐ Build &amp; Hire's confirm, `0x197F48 → 0x125460(toolmgr, 1, slot, kind)`: the tool's
    /// ENTER `0x128690` allocates and freezes the member (<see cref="ParkStaff.Hire"/>), and the laptop
    /// closes. ⭐ It costs nothing: the tool's cost field is only ever written 0.</summary>
    void BeginHire(StaffKind kind, int slot)
    {
        if (_staff == null) { Status("no park is running -- there is nobody to hire into"); return; }
        if (_hireHeld != null) CancelHireTool();
        if (_place.Active) { _place.Clear(); _ghostView?.Clear(); }
        if (_toolOpen) CloseTool();
        var cand = _staff.Candidates.For(kind, slot);
        var m = _staff.Hire(kind, slot);
        if (m == null) { Status($"{cand.Name(_text) ?? kind.ToString()} cannot be hired"); return; }
        _hireHeld = m; _hireCarried = false;
        _shopPanel?.Hide(); _laptopBack.Clear(); _shopPanel?.ShowBalance(null);
        SyncStaffActors();
        Status($"carrying {cand.Name(_text)} -- left-click path or ground to put them down, right-click to cancel");
        GD.Print($"[staff] hire: {kind} candidate {slot} ({cand.Name(_text)}) -> {m} held, logical {m.LogicalRequest}");
    }

    /// <summary>The carry `0x128760`, every frame: the member stands at the cursor's world position
    /// in 1/256 cell, NOT snapped, and stays held.</summary>
    void UpdateHireCarry()
    {
        if (_hireHeld == null || _staff == null) return;
        if (!CursorPoint(out var p)) return;
        _staff.Carry(_hireHeld, p);
        _hireCarried = true;
        var cell = new ParkCell(p.X >> 8, p.Z >> 8);
        Status($"carrying at {cell} -- " + (_staff.CanDrop(cell) ? "left-click to put down" : "cannot put down here"));
    }

    /// <summary>The tool's CROSS `0x128918`: the cell under the carried member must pass `0x1E65B8`
    /// (<see cref="ParkStaff.CanDrop"/>) -- refused: sound 0xAF, still held -- else `0x128888`
    /// (<see cref="ParkStaff.Drop(StaffMember, Point)"/>): placed, logical 13, sound 0x12F.</summary>
    void PressHireTool()
    {
        if (_hireHeld == null || _staff == null) return;
        if (!CursorPoint(out var p)) { Status("that click was not over the park"); return; }
        _staff.Carry(_hireHeld, p);
        _hireCarried = true;
        var at = StaffWorld(_hireHeld.CellPosition);
        if (!_staff.Drop(_hireHeld, p))
        {
            HireToolSound(0xAF, at);
            Status("staff cannot be put down there");
            GD.Print($"[staff] hire: drop refused at {_hireHeld.Cell} (0x1E65B8)");
            return;
        }
        HireToolSound(0x12F, at);
        GD.Print($"[staff] hire: {_hireHeld} dropped at ({p.X},{p.Z}), logical {_hireHeld.LogicalRequest}");
        Status($"{_hireHeld.Candidate.Name(_text)} is at work");
        _hireHeld = null; _hireCarried = false;
    }

    /// <summary>The tool's TRIANGLE `0x128A90`: free him, the candidate available again, sound 0x130.</summary>
    void CancelHireTool()
    {
        if (_hireHeld == null || _staff == null) return;
        var at = StaffWorld(_hireHeld.CellPosition);
        GD.Print($"[staff] hire: cancelled {_hireHeld}");
        _staff.CancelHire(_hireHeld);
        _hireHeld = null; _hireCarried = false;
        SyncStaffActors();
        HireToolSound(0x130, at);
        Status("hire cancelled");
    }

    /// <summary>⚠ The tool's sounds 0xAF, 0x12F and 0x130 are cued in the UI group, the one the
    /// viewer's build tool already cues 0xAF (175) and 0x1F in; the findings name the numbers, not
    /// their bank.</summary>
    void HireToolSound(int eventId, Vector3 at)
    {
        _sounds ??= MakeSounds();
        _sounds?.Cue(0, "hire", _parkTicks * ParkSim.TickMilliseconds, RseOpcode.EVENT, UiSoundGroup, -1, eventId, 0, at);
    }

    /// <summary>The cursor's world position in 1/256 cell, unsnapped -- the tool manager's `+0x2C`
    /// (`0x3951B0`) that the carry reads.</summary>
    bool CursorPoint(out Point point)
    {
        point = default;
        if (_cursorPointOverride is { } fixedPoint) { point = fixedPoint; return true; }
        if (_cursorOverride is { } c) { point = new Point((short)(c.X * 256 + 0x80), (short)(c.Y * 256 + 0x80)); return true; }
        if (_park?.Field == null || _cam == null) return false;
        var mouse = GetViewport().GetMousePosition();
        if (_panel != null && _panel.Visible && mouse.X < PanelW) return false;
        if (PointerOverCheats(mouse)) return false;
        return PointAtScreen(mouse, out point);
    }

    /// <summary>The fractional cell point under a screen position: the cell <see cref="CellAtScreen"/>
    /// picks, and the ray's floor hit expressed in that cell's own corners -- the inverse of
    /// <see cref="GuestWorld"/>'s mapping, so a member dropped here is drawn where the cursor was.</summary>
    bool PointAtScreen(Vector2 screen, out Point point)
    {
        point = default;
        if (!CellAtScreen(screen, out int bx, out int by)) return false;
        var from = _cam.ProjectRayOrigin(screen);
        var dir = _cam.ProjectRayNormal(screen);
        if (Mathf.Abs(dir.Y) < 1e-5f) return false;
        var hit = from + dir * ((_park.BaseY - from.Y) / dir.Y);
        var c0 = _park.CellCorner(bx, by);
        var ex = _park.CellCorner(bx + 1, by) - c0;
        var ez = _park.CellCorner(bx, by + 1) - c0;
        float det = ex.X * ez.Z - ex.Z * ez.X;
        if (Mathf.Abs(det) < 1e-9f) return false;
        var d = hit - c0;
        float u = (d.X * ez.Z - d.Z * ez.X) / det, v = (ex.X * d.Z - ex.Z * d.X) / det;
        int x = Mathf.FloorToInt((bx + u) * 256), z = Mathf.FloorToInt((by + v) * 256);
        if (x < short.MinValue || x > short.MaxValue || z < short.MinValue || z > short.MaxValue) return false;
        point = new Point((short)x, (short)z);
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // The Hire panel (findings/staff-management.md §2.1, §12.5).

    /// <summary>⭐ The tabs `0x197C40` adds: selector s = 0..4, only while `0x14CA20(10, key[s]) &gt; 0`
    /// = 5 − hired count (<see cref="ParkStaff.CanHire"/>), in <see cref="StaffTables.HireTabs"/>'s
    /// order, labelled by `0x365020`'s rows.
    /// ⚠ The console draws them as a tab strip inside Build &amp; Hire; this port has split Build &amp;
    /// Hire into two laptop rows already, so the tabs are a menu level like Build's categories. How the
    /// strip itself looks is not in the findings.</summary>
    List<StaffKind> HireTabs() =>
        _staff == null ? new List<StaffKind>() : StaffTables.HireTabs.Where(_staff.CanHire).ToList();

    void ShowHireTabs()
    {
        var tabs = HireTabs();
        var names = tabs.Select(k => _text?.Text("eng", StaffTables.PurchaseTextRow(k)) ?? k.ToString()).ToList();
        _shopPanel.ShowMenu(names, 0, LaptopMainMenu.MainScene);
        ClearLaptopModel();
        RefreshLaptopBalance();
        Status(tabs.Count == 0 ? "every staff type is at its maximum of 5" : "hire -- pick a type of staff, or Back");
    }

    /// <summary>⭐ One candidate of a tab, as `0x199008` draws him: the name at StaffSelect, Pay Grade
    /// `+0x14 + 1`, Monthly Wage `W[+0x14] × M[kind]`, the Motivation bar `+0x18`. The tab's list is
    /// `0x15D2A0`: slots 0..4 in order, only the available ones; StaffSelectArrows page it.
    /// ⚠ The scene's Model window is left empty: whether `0x199008` draws a model there is not in the
    /// findings. The Hire row under the list is this port's confirm (the pad's Cross), labelled with
    /// the game's own `STR_GIZMO_CPP_HIRE` (291).</summary>
    void ShowHireCandidate(StaffKind kind, int index)
    {
        var list = _staff?.Candidates.Available(kind).ToList() ?? new List<StaffCandidate>();
        if (list.Count == 0) { _laptopBack.RemoveAt(_laptopBack.Count - 1); ShowLaptopLevel(); return; }
        index = Math.Clamp(index, 0, list.Count - 1);
        var cand = list[index];
        var cells = new List<(string, int)>
        {
            ((cand.PayGrade + 1).ToString(), 0),
            (Money.Format(cand.MonthlyWage * 10), 0),
            (null, Math.Clamp(cand.Motivation, 0, 100)),
        };
        ClearLaptopModel();
        _shopPanel.ShowScreen(LaptopScreen.Hire, cand.Name(_text) ?? $"#{cand.NameRow}", cells, buildRow: true, buildTextId: HireTextId);
        RefreshLaptopBalance();
        Status($"{cand.Name(_text)} -- {index + 1} of {list.Count}; the arrows page, Hire takes them on");
    }

    /// <summary>`STR_GIZMO_CPP_HIRE`, the label the main menu's Hire row already borrows.</summary>
    const int HireTextId = 291;

    static (StaffKind Kind, int Index) HireArg(string arg)
    {
        var bits = (arg ?? "2:0").Split(':');
        int kind = bits.Length > 0 && int.TryParse(bits[0], out var k) ? k : (int)StaffKind.Handyman;
        int index = bits.Length > 1 && int.TryParse(bits[1], out var i) ? i : 0;
        return ((StaffKind)kind, index);
    }

    /// <summary>Page the Hire screen onto the next or previous available candidate.</summary>
    bool PageHire(int by)
    {
        if (_laptopBack.Count == 0 || _laptopBack[^1].Kind != "hire") return false;
        var (kind, index) = HireArg(_laptopBack[^1].Arg);
        int count = _staff?.Candidates.Available(kind).Count() ?? 0;
        int next = Math.Clamp(index + by, 0, Math.Max(0, count - 1));
        if (next == index) return true;
        _laptopBack[^1] = ("hire", $"{(int)kind}:{next}");
        ShowLaptopLevel();
        return true;
    }

    /// <summary>The Hire row: take the candidate on the screen.</summary>
    bool ConfirmHire()
    {
        if (_laptopBack.Count == 0 || _laptopBack[^1].Kind != "hire") return false;
        var (kind, index) = HireArg(_laptopBack[^1].Arg);
        var list = _staff?.Candidates.Available(kind).ToList();
        if (list == null || list.Count == 0) { Status("nobody left to hire"); return true; }
        BeginHire(kind, list[Math.Clamp(index, 0, list.Count - 1)].Slot);
        return true;
    }
}
