using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

/// <summary>One record of PoolOfEffectors (`0x3952A8`, 20 × 0x1C, pointer range `0x150B10`
/// `+0x10..+0x240`): `+0/+4` links, `+8` x (s16, cells), `+0xA` a junk y, `+0xC` z, `+0x10` radius²
/// (u32), `+0x14` flags, `+0x18` unused. READ, findings/staff-handymen-entertainers.md §4.4.</summary>
public sealed class StaffEffector
{
    internal StaffEffector(int slot) => Slot = slot;
    public int Slot { get; }
    public int X { get; internal set; }
    public int Z { get; internal set; }
    public uint Radius2 { get; internal set; }
    public int Flags { get; internal set; }
    public bool Active { get; internal set; }
}

/// <summary>⭐ THE EFFECTOR POOL, the minimal one the research describes: `0x14CFB8` pops the free head
/// and pushes it at the head of the active list (count+1), `0x14D038` unlinks it and pushes it back on
/// the free head (count-1), and `0x14D0A0(pos)` ORs the flags of every active record with
/// `dx² + dz² &lt;= radius²` over cells. ⭐ CENSUS: `jal 0x14CFB8` occurs ONCE in the ELF (`0x12DD14`,
/// the entertainer), so every effector on PS2 is an entertainer's flags-2, radius²-1 zone and the
/// guest code for flags 1 and 4 never fires (findings §4.4). ⚠ The free list's initial order is not
/// read; the port pushes slot 0 first (so slot 19 is allocated first), as the staff pools do.</summary>
public sealed class ParkEffectors
{
    public const int Capacity = 20;
    readonly StaffEffector[] _slots = new StaffEffector[Capacity];
    readonly List<StaffEffector> _free = new();     // index 0 = head
    readonly List<StaffEffector> _active = new();   // index 0 = head = newest

    public ParkEffectors()
    {
        for (int i = 0; i < Capacity; i++) { _slots[i] = new StaffEffector(i); _free.Insert(0, _slots[i]); }
    }

    public IReadOnlyList<StaffEffector> Active => _active;
    public int Count => _active.Count;

    /// <summary>`0x14CFB8`: null when all 20 are taken.</summary>
    internal StaffEffector Allocate()
    {
        if (_free.Count == 0) return null;
        var e = _free[0];
        _free.RemoveAt(0);
        _active.Insert(0, e);
        e.Active = true;
        return e;
    }

    /// <summary>`0x14D038`.</summary>
    internal void Free(StaffEffector e)
    {
        if (e == null || !e.Active) throw new InvalidOperationException("effector is not on the active list");
        _active.Remove(e);
        e.Active = false;
        _free.Insert(0, e);
    }

    /// <summary>`0x14D0A0`: the OR of the flags of every active record whose centre is within its
    /// radius² of the cell (`radius2 &lt; d` skips, so the edge counts).</summary>
    public int Query(ParkCell cell)
    {
        int flags = 0;
        foreach (var e in _active)
        {
            int dx = unchecked((short)(e.X - cell.X)), dz = unchecked((short)(e.Z - cell.Z));
            if (!(e.Radius2 < (uint)(dx * dx + dz * dz))) flags |= e.Flags;
        }
        return flags;
    }
}

/// <summary>One entry of the prank stink table `0x3AE140` (`{u32 particle handle, s32 cellx, s32
/// cellz}`), with the position `0x1822D0` spawned particle template 50 (`YellowStink`) at.</summary>
public sealed class PrankStink
{
    internal PrankStink(int cellX, int cellZ, int unitsX, int unitsZ)
    { CellX = cellX; CellZ = cellZ; UnitsX = unitsX; UnitsZ = unitsZ; }
    public int CellX { get; }
    public int CellZ { get; }
    /// <summary>The spawn's position arguments, 1/10240 cell (`0x18B5A8` shifts them `>> 4` into its
    /// 640-per-cell units).</summary>
    public int UnitsX { get; }
    public int UnitsZ { get; }
    /// <summary>The position in cells: the cell plus 0.1..0.9 on each axis.</summary>
    public float X => (UnitsX >> 4) / (float)ParticleTemplate.PositionUnitsPerCell;
    public float Z => (UnitsZ >> 4) / (float)ParticleTemplate.PositionUnitsPerCell;
    /// <summary>Particle template 50, `YellowStink` in `Tp2.plb` (record 50's name at `+0x118`).
    /// Immortal: it emits until `0x1824A8` stops it.</summary>
    public const int ParticleTemplateId = 50;
}

/// <summary>⭐ THE PRANK STINK TABLE `0x3AE140`, count `[0x37E114]`, READ (MIPS `0x1822D0..0x1824A4`
/// and `0x1824A8..0x182590`, findings/staff-handymen-entertainers.md §1.5):
/// <code>
///   add 0x1822D0(cellx, cellz):   refuse if count &gt;= 10 (sltiu 0xA) or that cell is already listed;
///       r1 = rand(); x = (int)((cellx + 0.1f + (r1 % 9) / 10f) * 10240)
///       r2 = rand(); z = (int)((cellz + 0.1f + (r2 % 9) / 10f) * 10240)
///       spawn particle 50 at (x, 0, z); append {handle, cellx, cellz}; count++
///   remove 0x1824A8(cellx, cellz): the entry with that cell: stop its emitter (0x18AAE0, unless the
///       handle is -1), move the LAST entry into its place, count--
/// </code>
/// ⭐ Keyed by the GUEST's cell, removed by the swept LITTER's cell (the litter lands up to 100/256 cell
/// away), so a prank near a cell edge leaves a stink that sweeping never stops, and an 11th concurrent
/// prank makes litter with no stink (INFERRED from the READ arithmetic, §1.5). ⚠ `rand` is `0x29CF08`,
/// the C library's (<see cref="NewlibRand"/>): the generator, not the console's position in its
/// stream. The particle itself is the view's (<see cref="Started"/>/<see cref="Stopped"/>).</summary>
public sealed class PrankStinks
{
    public const int Capacity = 10;
    readonly List<PrankStink> _entries = new();
    readonly Func<int> _rand;

    public PrankStinks(Func<int> libcRand) => _rand = libcRand ?? throw new ArgumentNullException(nameof(libcRand));

    public IReadOnlyList<PrankStink> Entries => _entries;
    public int Count => _entries.Count;
    /// <summary>Raised when an entry is added (spawn the emitter) and when one is removed or cleared
    /// (stop it).</summary>
    public Action<PrankStink> Started, Stopped;
    /// <summary>Instrumentation: adds refused for a full table or a listed cell.</summary>
    public int Refused { get; private set; }

    /// <summary>`0x1822D0`. True when a stink was added.</summary>
    public bool Add(int cellX, int cellZ)
    {
        if (_entries.Count >= Capacity || _entries.Any(e => e.CellX == cellX && e.CellZ == cellZ)) { Refused++; return false; }
        int r1 = _rand();
        float fx = (float)cellX + 0.1f + (r1 % 9) / 10f;
        int r2 = _rand();
        float fz = (float)cellZ + 0.1f + (r2 % 9) / 10f;
        var stink = new PrankStink(cellX, cellZ, (int)(fx * 10240f), (int)(fz * 10240f));
        _entries.Add(stink);
        Started?.Invoke(stink);
        return true;
    }

    /// <summary>`0x1824A8`: the swept litter's own cell. True when one was stopped.</summary>
    public bool Remove(int cellX, int cellZ)
    {
        int i = _entries.FindIndex(e => e.CellX == cellX && e.CellZ == cellZ);
        if (i < 0) return false;
        var gone = _entries[i];
        _entries[i] = _entries[^1];
        _entries.RemoveAt(_entries.Count - 1);
        Stopped?.Invoke(gone);
        return true;
    }

    /// <summary>`0x182598` (teardown `0x1F6BD0`).</summary>
    public void Clear()
    {
        foreach (var e in _entries.ToArray()) Stopped?.Invoke(e);
        _entries.Clear();
    }
}

/// <summary>⭐⭐ STAFF STEP 4, THE PARK'S SIDE: guards and entertainers in their pools, the effectors, the
/// stink table, the advisor event counters, the gate the guards eject through, and the GUESTS' side --
/// watching a show, heckling, pranks, being caught -- READ in findings/staff-mechanics-guards.md §5-§6
/// and staff-handymen-entertainers.md §1.3, §1.5, §4, §5.
///
/// ⚠ ADAPTERS, each labelled where it lives: <see cref="Gate"/> (the turnstile coordinator is the
/// viewer's entrance flow; without one a staged guard crosses at the next update);
/// <see cref="StagingCell"/>; <see cref="AnimationState"/>; <see cref="LocateGuest"/> (the port's
/// guests are ids with an optional walking body); the watch cooldown's spawn draw; the guest id as
/// the think phase's serial.</summary>
public sealed partial class ParkStaff
{
    /// <summary>Called by the pool construction: guards and entertainers get their job classes.</summary>
    void AttachSecurityJobs(StaffKind kind, StaffMember[] slots)
    {
        for (int i = 0; i < slots.Length; i++)
            slots[i] = kind switch
            {
                StaffKind.Guard => new Guard(this, i),
                StaffKind.Entertainer => new Entertainer(this, i),
                _ => slots[i],
            };
    }

    // --------------------------------------------------------------------------------------------
    // Pools of their own.

    /// <summary>PoolOfEffectors `0x3952A8`.</summary>
    public ParkEffectors Effectors { get; } = new();
    /// <summary>The prank stink table `0x3AE140`.</summary>
    public PrankStinks Stinks { get; } = new(new NewlibRand().Next);

    /// <summary>⭐ The advisor's EVENT COUNTERS (`adv[0x264] + 0x9E + 2n`, n 0..21), which the rule VM
    /// reads as variables `0x38 + n` (<see cref="AdvisorRules.Evaluate"/>'s eventCounters). `0x1073C0`
    /// → `0x10DDD8`: n &lt; 0x16, += d as s16, clamped to ±30000 (MIPS `0x10DDD8..0x10DE28`). Raised
    /// here: 2 by a heckle and a prank, 0x14 by a prank, 0x15 by a guest's ordinary litter drop.
    /// ⚠ `0x1073C0` counts only while the advisor object's byte `+0` has bit 3 set: this array always
    /// counts (instrumentation); the advisor's own counters get the event through
    /// <see cref="ParkSim.AdvisorEvent"/>, gated there (<see cref="ParkAdvisor.CountEvent"/>).</summary>
    public short[] AdvisorEvents { get; } = new short[22];
    internal void AdvisorEvent(int n, int d)
    {
        if ((uint)n >= 0x16) return;
        int v = unchecked((short)(AdvisorEvents[n] + d));
        AdvisorEvents[n] = (short)Math.Clamp(v, -30000, 30000);
        Sim.RaiseAdvisorEvent(n, d);
    }

    // --------------------------------------------------------------------------------------------
    // Sounds with handles.

    /// <summary>A guard's or an entertainer's sound WITH its handle offset `P+`: natively
    /// `0x111428(audio, 8, event, pos, &amp;handle, 0)`, here the mechanics' <see cref="HandleSound"/>
    /// (reused, bank 8; null falls back to <see cref="Sound"/>). The guard's 0x89 is raised on EVERY walk
    /// tick while he carries, so the view's "handle still sounding" test is what keeps it one voice.</summary>
    internal void RaiseGuardSound(Guard g, int eventId, int handle) => RaiseSound(g, 8, eventId, handle);
    internal void RaiseEntertainerSound(Entertainer e, int eventId, int handle) => RaiseSound(e, 8, eventId, handle);

    /// <summary>⚠ ADAPTER for `0x10EC48` on a member's model: its CURRENT logical and phase (control
    /// block bytes +0 and +2, <see cref="NativeLogicalAnimationControl"/>). The guard's 0x3C waits for
    /// logical 16 at phase 1. Null, or a null answer, means no visual: the guard proceeds at once.</summary>
    public Func<StaffMember, (byte Current, byte Phase)?> AnimationState { get; set; }

    uint _copySerial;
    internal uint NextCopySerial() => ++_copySerial;

    // --------------------------------------------------------------------------------------------
    // The gate: the entrance table's points and the turnstile coordinator.

    NativeEntranceFlow _gate;
    /// <summary>⚠ ADAPTER for the coordinator `0x14BCC0`: the entrance flow the viewer runs, whose P/R
    /// counters and event 9 the guards share (<see cref="NativeEntranceFlow.StageMember"/>). ⚠ Null (the
    /// legacy walk-in, a fixture with no flow): no coordinator holds a staged guard -- every guard in
    /// 0x2E gets event 9 at the start of the next staff update.</summary>
    public NativeEntranceFlow Gate
    {
        get => _gate;
        set
        {
            if (_gate != null && _gate.MemberEvent9 == GateEvent9) _gate.MemberEvent9 = null;
            _gate = value;
            if (_gate != null) _gate.MemberEvent9 = GateEvent9;
        }
    }
    /// <summary>⚠ The park's entrance table entry `+2/+3` (the staging point `0x1532D8` reads): the port's
    /// <see cref="ParkEntranceEntry"/> keeps `+0/+1` and `+0x10/+0x11` only, so whoever knows the entry
    /// supplies it (the viewer from <see cref="NativeBusCatalogue.StagingPoint"/>).</summary>
    public ParkCell? StagingCell { get; set; }
    /// <summary>Instrumentation: guards counted in at the turnstile (`0x153298`) and across (`0x1532B0`),
    /// and event 9s delivered to guards.</summary>
    public int GateStaged { get; private set; }
    public int GateCrossed { get; private set; }
    public int GateOpened { get; private set; }

    /// <summary>`0x14E288` returns 1 on the console; ⚠ here, whether the port knows the gate.</summary>
    internal bool HasGate => StagingCell != null && Paths.EntranceEntry != null;

    /// <summary>`0x1532D8(unused, out)`, READ (MIPS `0x1532D8..0x153378`): entry `+2/+3` cell centre,
    /// then x += rand(256) -- up to 383/256 past the cell's corner, so it can land in the next cell.</summary>
    internal Point? StagingPoint()
    {
        if (StagingCell is not { } s) return null;
        short x = unchecked((short)(s.X * 256 + 0x80)), z = unchecked((short)(s.Z * 256 + 0x80));
        x = unchecked((short)(x + Random(256)));
        return new Point(x, z);
    }

    /// <summary>`0x14E290(i)`: entry `+2i/+2i+1` cell centre; only i = 0 is ever asked (rand(1)), the
    /// exit point `+0/+1` = the corridor start (<see cref="ParkEntranceEntry.XStart"/>/ZRow).</summary>
    internal Point? ExitPoint(int index)
    {
        if (index != 0) throw new ArgumentOutOfRangeException(nameof(index), "rand(1) is always 0");
        return Paths.EntranceEntry is { } e ? CentreOf(e.XStart, e.ZRow) : null;
    }

    /// <summary>`0x153380`: entry `+0x10/+0x11` cell centre, no random term; the guard's 0x37 clamps it
    /// to `[0, W&lt;&lt;8] x [0, H&lt;&lt;8]` (`0x14E0F8`/`0x14E108`).</summary>
    internal Point? MouthPoint()
    {
        if (Paths.EntranceEntry is not { } e) return null;
        int x = e.XCol * 256 + 0x80, z = e.ZEnd * 256 + 0x80;
        x = Math.Min(Math.Max(x, 0), Tiles.Width << 8);
        z = Math.Min(Math.Max(z, 0), Tiles.Height << 8);
        return new Point(unchecked((short)x), unchecked((short)z));
    }

    static Point CentreOf(int x, int z) => new(unchecked((short)(x * 256 + 0x80)), unchecked((short)(z * 256 + 0x80)));

    /// <summary>⚠⚠ THE SOFTLOCK. Master: "guests waiting to cross the road, and the bus waiting for
    /// them to cross, but neither goes."
    ///
    /// `StagingPending` (P) and the bus share one variable. `14BCC0`: `P != 0 && E == 0` claims the
    /// crossing with `E = 1`, and E is cleared only when `P == 0` (or `R >= 11 && S == 2`). The bus's
    /// state-1 guard refuses to advance while `E == 1`, so it can never reach state 2 -- which means
    /// **once P is stuck above zero, both sides wait forever**.
    ///
    /// ⚠⚠ AND P COULD ONLY GO UP. `Guard`'s mode switch calls <see cref="GateStage"/> from TWO modes
    /// -- `ModeToStaging` and `ModeBackToStaging`, the second being a guard returning to the gate --
    /// against ONE <see cref="GateCross"/> on `ModeCrossing`. Every guard that went back to staging
    /// without crossing leaked P by one, permanently, and one leaked unit is enough to hold the
    /// crossing shut for the rest of the game.
    ///
    /// ⭐ The flow's own entries never had this: they carry `e.StageCounted` and `Forget` releases
    /// the count only if it was taken. Guards went straight to the counter with no such flag. This
    /// gives them the same discipline -- a guard is counted at most once, and releases only what it
    /// actually took -- rather than changing the console's coordinator, which is decoded and correct.
    ///
    /// ⚠ STILL OPEN, said rather than quietly patched: a guard REMOVED while staged is not released
    /// here, because the guard-despawn path does not pass through these two calls. That is a second,
    /// rarer leak with the same ending, and it wants the despawn hook rather than a watchdog.</summary>
    readonly HashSet<Guard> _gateStaged = new();

    internal void GateStage(Guard g)
    {
        if (g == null || !_gateStaged.Add(g)) return;   // already counted: a re-stage is not a second guard
        Gate?.StageMember(); GateStaged++;
    }

    internal void GateCross(Guard g)
    {
        if (g == null || !_gateStaged.Remove(g)) return; // never counted: nothing of ours to release
        Gate?.CrossMember(); GateCrossed++;
    }

    /// <summary>The coordinator's event 9 to the guard list (`0x14D228`, newest first): every guard in
    /// state 0x2E goes to 0x2F.</summary>
    public void GateEvent9()
    {
        foreach (var m in Active(StaffKind.Guard))
            if (m is Guard g && g.State == Guard.StateAtGate) { g.RouteEvent(9); GateOpened++; }
    }

    // --------------------------------------------------------------------------------------------
    // Guests, as the guard and the entertainer see them.

    public enum GuestPlace { Gone, Queueing, NoBody, Body }

    /// <summary>⚠ ADAPTER for reading a guest through its map object: where it is and whether a guard
    /// may take it. Gone = no longer in the park; Queueing = the native queue states 0x12/0x13/0x14
    /// (a guest in a ride's native queue, <see cref="VisitorIntent.Queueing"/>, or still waiting in a
    /// scripted ride's queue); NoBody = in the park but off the walk (aboard, inside a facility,
    /// recovering) -- ⚠ natively that guest has a stored position and is NOT immune (findings §9: the
    /// edge was not followed); the port has none to route to, so the guard treats it as a null target.
    /// Body = a walking guest, with its fine position.</summary>
    internal (GuestPlace Place, Guest Body, Point Fine) LocateGuest(GuestTarget target)
    {
        if (target == null || !Visitors.Plans.TryGetValue(target.Id, out var plan)) return (GuestPlace.Gone, null, default);
        if (plan.Intent == VisitorIntent.Queueing
            || (plan.Intent == VisitorIntent.Queued && Visitors.QueuedOwner(target.Id) is { } ride && ride.Queue.Contains(target.Id)))
            return (GuestPlace.Queueing, null, default);
        var body = Body(target.Id);
        return body == null ? (GuestPlace.NoBody, null, default) : (GuestPlace.Body, body, GuestFine(body));
    }

    Guest Body(int id)
    {
        foreach (var g in Visitors.Walk.Guests) if (g.Id == id) return g;
        return null;
    }

    /// <summary>A guest's `N+0x24/0x26` in 1/256 cell: its drawn position (the lease's own point for a
    /// native-route guest, the edge interpolation or the cell centre otherwise).</summary>
    public static Point GuestFine(Guest g)
        => new(unchecked((short)MathF.Floor(g.Position.X * 256f)), unchecked((short)MathF.Floor(g.Position.Z * 256f)));
    /// <summary>`vt+0x74` on a guest: the cell of its fine position.</summary>
    public static ParkCell GuestCell(Guest g) { var p = GuestFine(g); return new ParkCell(p.X >> 8, p.Z >> 8); }

    /// <summary>`0x12DAE0`, READ: any guest on the active person list (`0x14D218`) with
    /// `dx² + dy² + dz² &lt; 2` of the cell. ⚠ The y is an unwritten stack short, identical on both sides
    /// (findings §4.3, INFERRED from the MIPS), so this is `dx² + dz² &lt;= 1`: the cell or its four
    /// neighbours. ⚠ Only guests with a body on the walk are seen.</summary>
    internal bool GuestAdjacent(ParkCell cell)
    {
        foreach (var g in Visitors.Walk.Guests)
        {
            var c = GuestCell(g);
            int dx = c.X - cell.X, dz = c.Z - cell.Z;
            if ((uint)(dx * dx + dz * dz) < 2) return true;
        }
        return false;
    }

    // --------------------------------------------------------------------------------------------
    // Sending a guard: 0x14D3E0 and the camera test 0x14D238.

    /// <summary>`0x14D238(pos, 0x40)`, READ (MIPS `0x14D238..0x14D3DC`): over the placed-object list
    /// `0x14CD30`, an object with status `+0x9A` (<see cref="StaffFeature.Status"/>) nonzero and DBA
    /// `+0x2E` bit 3 (`0x130858`, a camera) whose `vt+0x74` cell is within `dx² + dz² &lt;= 64`. Its only
    /// caller is <see cref="DispatchGuard"/>; the position is the GUEST's.</summary>
    public bool CameraNear(ParkCell cell)
    {
        foreach (var f in PlacedFeatures())
        {
            if (f.Status == 0 || !f.IsCamera) continue;
            int dx = unchecked((short)(cell.X - f.Origin.X)), dz = unchecked((short)(cell.Z - f.Origin.Z));
            if (!(64u < (uint)(dx * dx + dz * dz))) return true;
        }
        return false;
    }

    /// <summary>⭐⭐ `0x14D3E0(guest)`, READ (MIPS `0x14D3E0..0x14D63C`): over the guard list `0x14D228`
    /// (newest first), skipping busy ones (`0x1416F0`),
    /// <code>
    ///   d = dx² + dz² (cells, guard to guest);  best = none, bestD = 0xFFFF
    ///   if d &lt; bestD and d &lt; 64 and (camera within 8 of the GUEST (0x14D238) or d &lt; 25): best = this, bestD = d
    ///   best → 0x1417D0(best, guest); return 1      none → return 0
    /// </code>
    /// ⭐ THE CAMERA RULE: with a camera within 8 cells of the prankster the nearest free guard within
    /// 8 cells (d² &lt; 64) is sent, else only one within 5 (d² &lt; 25). Euclidean, whole cells. The
    /// camera test is made per candidate natively; it depends on the guest alone, so once here.</summary>
    public bool DispatchGuard(int guestId, ParkCell guestCell)
    {
        Guard best = null; uint bestDistance = 0xFFFF;
        bool? camera = null;
        foreach (var m in Active(StaffKind.Guard))
        {
            if (m is not Guard g || g.Busy) continue;
            int dx = unchecked((short)(g.Cell.X - guestCell.X)), dz = unchecked((short)(g.Cell.Z - guestCell.Z));
            uint d = (uint)(dx * dx + dz * dz);
            if (!(d < bestDistance) || !(d < 64)) continue;
            camera ??= CameraNear(guestCell);
            if (camera.Value || d < 25) { best = g; bestDistance = d; }
        }
        if (best == null) { DispatchesRefused++; return false; }
        best.Chase(new GuestTarget(guestId));
        return true;
    }
    public int DispatchesRefused { get; private set; }

    // --------------------------------------------------------------------------------------------
    // The guest's idle arms 2 and 5 (0x20C930), called by ParkVisitors with staff attached.

    /// <summary>`[0x2EEB64]` (heckle, also written by the Debug Menu's apply `0x12C348`),
    /// `[0x2EEB68]` (prank) and `[0x2EEB98]` (prank happiness): the shipped image's 10, 10 and 25.</summary>
    public const int HeckleChance = 10, PrankChance = 10, PrankHappinessBelow = 25;
    /// <summary>Arm 2: the entertainer must be within Manhattan 5 (`sltiu 5` at `0x20CDE4`).</summary>
    public const uint HeckleReach = 5;
    /// <summary>The logical a heckling guest requests (`0x20CEB4`: `& ~0x1F | 4`).</summary>
    public const int HeckleLogical = 4;

    /// <summary>⭐ Arm 2, READ (corpus `FUN_0020c930` case 2): `rand(1000) &lt; [0x2EEB64]` FIRST, then the
    /// nearest entertainer on `0x14D670` by Manhattan cells (any state, strict &lt;, from 0xFFFF);
    /// found and `&lt; 5` → the guest's logical 4, event counter 2 += 1, `0x12E278(ent, guest)`. The guest
    /// stays in state 0 and re-rolls next update.</summary>
    internal void HeckleArm(Guest g, Func<uint, uint> rand)
    {
        if (!(rand(1000) < HeckleChance)) return;
        var cell = GuestCell(g);
        Entertainer best = null; uint bestDistance = 0xFFFF;
        foreach (var m in Active(StaffKind.Entertainer))
        {
            if (m is not Entertainer e) continue;
            uint d = (uint)(Math.Abs(e.Cell.X - cell.X) + Math.Abs(e.Cell.Z - cell.Z));
            if (d < bestDistance) { bestDistance = d; best = e; }
        }
        if (best == null || !(bestDistance < HeckleReach)) return;
        AdvisorEvent(2, 1);
        best.Heckled(new GuestTarget(g.Id));
        Heckles++;
        Heckled?.Invoke(g.Id, best);
    }
    /// <summary>A heckle happened: (guest, entertainer). The view's hook for the guest's logical 4.</summary>
    public Action<int, Entertainer> Heckled { get; set; }
    public int Heckles { get; private set; }

    /// <summary>⭐ Arm 5, READ (corpus case 5): happiness `+0x75 &lt; [0x2EEB98]` FIRST (a signed byte
    /// compare), then `rand(1000) &lt; [0x2EEB68]` → <see cref="Prank"/>. The guest stays in state 0.</summary>
    internal void PrankArm(Guest g, int happiness, Func<uint, uint> rand)
    {
        if (!(happiness < PrankHappinessBelow)) return;
        if (!(rand(1000) < PrankChance)) return;
        Prank(g);
    }

    /// <summary>⭐⭐ `0x20D010(g, 1)`, the PRANK, READ (MIPS `0x20D13C..0x20D21C`):
    /// <code>
    ///   litter = 0x14AD08()                      // 40-pool; null when full (or in test-park mode)
    ///   if litter: place at the guest's fine position ± rand(200)-100 (0x15E2B8), NOT shown;
    ///              0x1822D0(guest cell)          // the YellowStink -- ONLY after a successful allocation
    ///   event counters 2 and 0x14 += 1
    ///   0x14D3E0(g)                              // send a guard; its answer is ignored
    /// </code>
    /// ⭐ `beqz $a0, 0x20D1F0` at `0x20D148` skips both the place and the stink when the pool is full --
    /// ⚠ findings §1.3/§6 list the stink as unconditional. The guest's own state is not changed.
    /// ⚠ Public as the one TEST HOOK the smoke uses to force a prank (natively only arm 5 calls it).</summary>
    public void Prank(Guest g)
    {
        ArgumentNullException.ThrowIfNull(g);
        var cell = GuestCell(g);
        var item = Litter.Drop(GuestFine(g), vomit: false, shown: false);
        if (item != null) Stinks.Add(cell.X, cell.Z);
        AdvisorEvent(2, 1);
        AdvisorEvent(0x14, 1);
        DispatchGuard(g.Id, cell);
        Pranks++;
    }
    public int Pranks { get; private set; }

    // --------------------------------------------------------------------------------------------
    // Caught: event 4 and the removal notice.

    /// <summary>A guard's catch: event `{0x35A548, 4}` to the guest's `vt+0x16C` = `0x20F588` case 4 --
    /// `0x14B368` (released, unregistered, back on the free list: GONE, its cash and stats with it) and
    /// `0x14B9D0`, the removal notice, which for class 0xB calls `vt+0x19C` = `0x1928F8` on EVERY guard
    /// (the catcher too). Entertainers are not told (§9: a heckler's pointer dangles).</summary>
    internal void CatchGuest(Guard catcher, int guestId, Guest body)
    {
        Visitors.Eject(guestId);
        GuestRemoved(guestId);
        Caught++;
        GuestCaught?.Invoke(catcher, guestId);
    }
    /// <summary>Guests caught and removed.</summary>
    public int Caught { get; private set; }
    /// <summary>A catch: (guard, guest id), raised after the guest is gone.</summary>
    public Action<Guard, int> GuestCaught { get; set; }

    void GuestRemoved(int guestId)
    {
        var t = new GuestTarget(guestId);
        foreach (var m in Active(StaffKind.Guard)) m.NoticeRemoved(t);
        _watching.Remove(guestId);
        _watchCooldown.Remove(guestId);
        _firstSeen.Remove(guestId);
        _liveTargets.Remove(guestId);
    }

    readonly HashSet<int> _liveTargets = new();
    /// <summary>⚠ ADAPTER for the removal notice when a guest leaves by any other road (home, a
    /// teardown): the port's visitor code raises no event, so a guest a guard targets that WAS in the
    /// park at the last update and is not now gets the notice here, before the members update (as
    /// <see cref="NoticeRemovals"/> does for rides). A guard handed a guest who had ALREADY gone (an
    /// entertainer's dangling heckler) is not notified -- the notice was sent before the hand-off --
    /// and gives up at its next chase tick.</summary>
    void NoticeGuestDepartures()
    {
        var seen = new HashSet<int>();
        foreach (var m in Active(StaffKind.Guard))
        {
            if (m.Target is not GuestTarget t) continue;
            if (Visitors.Plans.ContainsKey(t.Id)) seen.Add(t.Id);
            else if (_liveTargets.Contains(t.Id)) GuestRemoved(t.Id);
        }
        _liveTargets.Clear();
        _liveTargets.UnionWith(seen);
    }

    // --------------------------------------------------------------------------------------------
    // Watching a show: the guest think 0x20FB88's effector phase and state 0x1C 0x2107A0.

    /// <summary>A guest in state 0x1C: whom (`G+0x68`), until when (`G+0x2C`), and the facing the state
    /// writes every update (quarter turns).</summary>
    public sealed class Watch
    {
        internal Watch(Entertainer e, uint until) { Entertainer = e; Until = until; }
        public Entertainer Entertainer { get; }
        public uint Until { get; }
        public int FacingQuarterTurns { get; internal set; }
    }
    readonly Dictionary<int, Watch> _watching = new();
    readonly Dictionary<int, uint> _watchCooldown = new();
    /// <summary>⚠ The tick the phase first saw each guest: the port's stand-in for its spawn time.</summary>
    readonly Dictionary<int, uint> _firstSeen = new();
    /// <summary>The guests watching a show now, by id.</summary>
    public IReadOnlyDictionary<int, Watch> Watching => _watching;
    public bool IsWatching(int guest) => _watching.ContainsKey(guest);
    /// <summary>`G+0x6C`, the time a guest may next start watching, once known.</summary>
    public uint? WatchCooldown(int guest) => _watchCooldown.TryGetValue(guest, out var t) ? t : null;
    public int WatchesStarted { get; private set; }
    public int WatchesEnded { get; private set; }

    /// <summary>`0x20FB88`'s effector phase and state 0x1C, once per tick for every walking guest, after
    /// the members (⚠ natively guests and staff share one newest-first object list; the port runs the
    /// guests' part after the staff's):
    /// <code>
    ///   every 8 ticks ((now &amp; 7) == (serial &amp; 7)):  f = 0x14D0A0(my cell)
    ///       [f &amp; 1 → happiness +6; f &amp; 4 → happiness/sickness: no producer on PS2, not ported]
    ///       if !(flags &amp; 4) (not inside) and depth &lt; 2 and G+0x6C &lt; now and state != 0x1C and f &amp; 2:
    ///           ent = nearest entertainer (0x14D670, Manhattan, strict &lt;, NO state or range test)
    ///           G+0x68 = ent; push(state); state = 0x1C; G+0x2C = now + 300 + 60·(ent C+0x48 &amp; 7)
    ///   state 0x1C (0x2107A0), every update: logical 2; face ent (dx != 0: dx &lt; 0 → 3π/2, else π/2;
    ///       else dz &gt; 0 → 0, else π); ent.state == 0xC and now &lt;= G+0x2C → keep watching; else
    ///       pop, logical 13, happiness = min(100, h + 5), G+0x68 = 0, G+0x6C = now + 900
    /// </code>
    /// ⭐ The guest is NOT routed: it stops where it is (<see cref="GuestWalk.Paused"/>) and resumes what
    /// it was doing. The +5 is paid whether the show ran its course or was cut short.
    /// ⚠ ADAPTERS: the guest id stands in for its activation serial (as in <see cref="VisitorNeeds"/>);
    /// the goal depth (&lt; 2) is not kept for the port's guests; a guest on a NATIVE lease (the entrance
    /// flow, a native queue or departure) is stepped by its owner and does not watch; `G+0x6C`'s
    /// facility-exit value `now + 60 + rand(60)` (`0x20EE40`) is not written.
    /// ⚠⚠ `G+0x6C`'s SPAWN value `spawn + rand(300)` (`0x20BF10`): the port's guests are not built by
    /// `0x20BF10`, so `spawn` is the first tick this phase saw the guest, and the rand(300) is drawn
    /// LAZILY, the first time the guest stands on a show's effector (the only time the value is read to
    /// any effect). Drawn at every guest's first sight instead, it cost the guests' `1448E0` stream one
    /// draw per guest in every park, show or no show, and moved every later draw on it -- the entrance's
    /// group pick included: the SPACE-1 entrance soak lost its group flip (0 flips, peak group 12) with
    /// no entertainer in the park (tinyclaw 2026-09-27). A park with no show now draws nothing here.</summary>
    void GuestThink()
    {
        uint now = Now;
        var bodies = Visitors.Walk.Guests;
        foreach (var id in _watching.Keys.ToArray())
            if (!bodies.Any(g => g.Id == id)) _watching.Remove(id);   // left the walk by another road
        foreach (var g in bodies.ToArray())
        {
            if (g.HasNativeRoute || !Visitors.Plans.ContainsKey(g.Id)) continue;
            var cell = GuestCell(g);
            if (!_firstSeen.ContainsKey(g.Id)) _firstSeen[g.Id] = now;
            if ((now & 7) == ((uint)g.Id & 7))
            {
                int f = Effectors.Query(cell);
                if (!_watching.ContainsKey(g.Id) && (f & 2) != 0 && SpawnCooldown(g.Id) < now)
                {
                    Entertainer best = null; uint bestDistance = 0xFFFFFFFF;
                    foreach (var m in Active(StaffKind.Entertainer))
                    {
                        if (m is not Entertainer e) continue;
                        uint d = (uint)(Math.Abs(e.Cell.X - cell.X) + Math.Abs(e.Cell.Z - cell.Z));
                        if (d < bestDistance) { bestDistance = d; best = e; }
                    }
                    if (best != null)
                    {
                        _watching[g.Id] = new Watch(best, unchecked(now + (uint)StaffTables.EntertainerWatchTicks(best.Level)));
                        WatchesStarted++;
                    }
                }
            }
            if (!_watching.TryGetValue(g.Id, out var w)) continue;
            var ec = w.Entertainer.Cell;
            int dx = ec.X - cell.X, dz = ec.Z - cell.Z;
            w.FacingQuarterTurns = dx != 0 ? (dx < 0 ? 3 : 1) : (dz > 0 ? 0 : 2);
            if (w.Entertainer.State == Entertainer.StatePerforming && !(w.Until < now)) continue;
            _watching.Remove(g.Id);
            if (Visitors.Needs is { } needs && needs.Has(g.Id))
            {
                var wants = needs.Of(g.Id);
                wants.Happiness = (byte)Math.Min(100, wants.Happiness + WatchHappiness);
                needs.Set(g.Id, wants);
            }
            _watchCooldown[g.Id] = unchecked(now + WatchCooldownTicks);
            WatchesEnded++;
        }
        foreach (var id in _watchCooldown.Keys.ToArray())
            if (!Visitors.Plans.ContainsKey(id)) _watchCooldown.Remove(id);
        foreach (var id in _firstSeen.Keys.ToArray())
            if (!Visitors.Plans.ContainsKey(id)) _firstSeen.Remove(id);
    }

    /// <summary>`G+0x6C`, drawing its spawn value `first sight + rand(300)` the first time it is asked for
    /// (see <see cref="GuestThink"/>'s ⚠⚠).</summary>
    uint SpawnCooldown(int guest)
    {
        if (!_watchCooldown.TryGetValue(guest, out uint cooldown))
            _watchCooldown[guest] = cooldown = unchecked(_firstSeen[guest] + (uint)Random(300));
        return cooldown;
    }

    /// <summary>`0x210900`: happiness +5 at a watch's end; `0x21092C`: `G+0x6C = now + 0x384`.</summary>
    public const int WatchHappiness = 5;
    public const uint WatchCooldownTicks = 0x384;
    /// <summary>The logicals a watcher requests: 2 while watching (`0x2107D0`), 13 at the end (`0x210908`).</summary>
    public const int WatchLogical = 2, WatchEndLogical = 13;

    /// <summary>Per tick, before the members: the guests' removal notices and, with no coordinator,
    /// the gate's stand-in.</summary>
    void SecurityBeforeMembers()
    {
        NoticeGuestDepartures();
        if (Gate == null) GateEvent9();
    }
}
