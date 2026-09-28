using System.Numerics;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

/// <summary>⭐⭐ ONE STAFF MEMBER: the person base (`0x1912F8`, shared with guests) plus the staff
/// base (`0x1DB5B8`, vtable `0x3694B0`), READ in findings/staff-person.md. A subclass per type adds
/// the job; this class is also what the types whose job step is not built yet are made of (see
/// <see cref="FindWork"/>).
///
/// ⭐ ONE OBJECT PER POOL SLOT, REUSED. Natively the five slots of each pool are constructed once
/// per park and activated on every hire; firing does not destruct them and their fields stay stale
/// until the next activation (§2.6). So a fired and re-hired member is the SAME object here too --
/// a view keys its model on (member, <see cref="Serial"/>), which changes on every activation.
///
/// The fields are the native ones (`C+` offsets, §1.2); the virtual methods are vtable slots and
/// say which. Where the port supplies a value the native reads from elsewhere, the member says so.
///
/// ⚠ ADAPTERS in this class: the tick counter <see cref="ParkStaff.Now"/> (the port's own, one per
/// staff update, standing in for `[0x397644]`); animation readiness <see cref="ParkStaff.AnimationReady"/>
/// (null = no visual, which the native test permits); the tile bytes (<see cref="NativeTileView"/>);
/// the route search (<see cref="StaffRouteService"/>).</summary>
public partial class StaffMember
{
    /// <summary>`C+0x2C` flag bits (§1.3).</summary>
    public const ushort FlagShown = 0x0001, FlagMapCentre = 0x0020, FlagHeld = 0x0040, FlagCutRecord = 0x0200;

    /// <summary>Execution states of the person and the staff base (§3.1; names from the debug
    /// table `0x10CA98` where it has one).</summary>
    public const byte StateIdle = 0x00, StateSetRandomDestination = 0x01, StateSegmentEnd = 0x02,
                      StateWalk = 0x03, StateWander = 0x05, StateWaitForRoute = 0x0B, StatePatrol = 0x0D,
                      StateStriking = 0x0F, StateWalkToStrike = 0x1A, StateGoRest = 0x31, StateResting = 0x32;

    /// <summary>Modes (`C+0x2E`, what the current walk is for) the base handles (§3.2, §8).</summary>
    public const byte ModePatrol = 0x01, ModeIdle = 0x02, ModeStrike = 0x05, ModeStaffRoom = 0x11;

    protected ParkStaff Park { get; }
    NativeGuestRoute _route;
    readonly byte[] _goals = new byte[4];

    internal StaffMember(ParkStaff park, StaffKind kind, int poolSlot)
    {
        Park = park ?? throw new ArgumentNullException(nameof(park));
        Kind = kind; PoolSlot = poolSlot;
    }

    // --------------------------------------------------------------------------------------------
    // Identity and fields (§1.2).

    public StaffKind Kind { get; }
    /// <summary>Which of its pool's five slots this is (the first hire gets slot 4, §2.1).</summary>
    public int PoolSlot { get; }
    /// <summary>On its pool's active list (hired, not yet fired).</summary>
    public bool Active { get; internal set; }

    /// <summary>`C+0x0C`, the activation serial (`0x1093B0`); its low two bits are the TICK PHASE.</summary>
    public uint Serial { get; private set; }
    /// <summary>`C+0x14`, goal-stack depth. ⚠ Every push is before a 0xB wait and events 1/2 zero
    /// the depth, so nothing ever pops it (INFERRED vestigial, §3.3).</summary>
    public int GoalDepth { get; protected set; }
    /// <summary>`C+0x18..0x1B`, the goal stack; exposed for inspection only.</summary>
    public IReadOnlyList<byte> Goals => _goals;
    /// <summary>`C+0x1C/+0x1E`: x/z in 1/256 cell. ⚠ NOT snapped: the hire carry writes the cursor's
    /// raw world position (§2.4).</summary>
    public Point Position { get; internal set; }
    /// <summary>`C+0x20`: the job target -- a <see cref="LitterItem"/>, or the <see cref="ParkRide"/>
    /// (toilet, staff room) or other <see cref="StaffFeature.Key"/> it is working at or walking to.</summary>
    public object Target { get; protected set; }
    /// <summary>`C+0x24`: a tick stamp or job deadline (staff base writes `now` on entering 0xB).</summary>
    public uint Stamp { get; protected set; }
    /// <summary>`C+0x28`: the head of this member's output-slot chain in the SHARED pool, -1 for none.</summary>
    public int RouteSlot => _route?.SlotIndex ?? -1;
    /// <summary>`C+0x2C`: person flags (<see cref="FlagShown"/>, <see cref="FlagHeld"/>, ...).</summary>
    public ushort Flags { get; protected set; }
    /// <summary>`C+0x2E`: mode, what the current walk is for.</summary>
    public byte Mode { get; protected set; }
    /// <summary>`C+0x2F`: the execution state.</summary>
    public byte State { get; protected set; }
    /// <summary>`C+0x30 &amp; 0x1F`: the requested logical animation, pushed to the model every frame
    /// by the visual sync `0x1921D0` (§5.4) -- including while held.</summary>
    public int LogicalRequest { get; protected set; }
    /// <summary>`C+0x34` as quarter turns: 0 faces +z, 1 +x, 2 -z, 3 -x (radians = quarter turns
    /// times pi/2: 0, pi/2, pi, 3pi/2). The walk step writes it; the visual yaw is `pi - facing`.</summary>
    public int FacingQuarterTurns { get; protected set; }
    /// <summary>`C+0x3C`: the candidate this member is.</summary>
    public StaffCandidate Candidate { get; private set; }
    /// <summary>`C+0x40`: the calendar day count at activation (`0x16B218`).</summary>
    public int HireDay { get; private set; }
    /// <summary>`C+0x44..0x47`: patrol rectangle x0, z0, x1, z1 in cells, s8; unset = (0,0,-1,-1).</summary>
    public sbyte PatrolX0 { get; private set; }
    public sbyte PatrolZ0 { get; private set; }
    public sbyte PatrolX1 { get; private set; } = -1;
    public sbyte PatrolZ1 { get; private set; } = -1;
    int _level;
    /// <summary>`C+0x48 &amp; 7`: skill level 0..4 (shown +1).</summary>
    public int Level { get => _level; internal set => _level = value & 7; }
    int _speedBits;
    /// <summary>`C+0x48 &gt;&gt; 3`: the base walk speed, 15 from activation (`| 0x78`).</summary>
    public int SpeedBits { get => _speedBits; internal set => _speedBits = value & 0x1F; }
    /// <summary>`C+0x49`: candidate slot 0..4 (`vt+0x1C4` = `0x1DC918`).</summary>
    public int CandidateSlot { get; private set; }
    /// <summary>`C+0x4A`: the candidate's motivation, clamped 0..100. ⚠ No reader in the ELF (§11).</summary>
    public sbyte MotivationCopy { get; private set; }
    /// <summary>`C+0x4B`: tiredness 0..100.</summary>
    public sbyte Tiredness { get; internal set; }
    /// <summary>`C+0x4C`: morale 0..100.</summary>
    public sbyte Morale { get; internal set; }

    // --------------------------------------------------------------------------------------------
    // What a view reads.

    public bool Shown => (Flags & FlagShown) != 0;
    public bool Held => (Flags & FlagHeld) != 0;
    /// <summary>`C+0x2C &amp; 0x200`: the next logical push cuts the current record (flags 2 to
    /// `10E910`). Only the guard's carry sets it (area C).</summary>
    public bool CutRecordOnPush => (Flags & FlagCutRecord) != 0;
    /// <summary>`0x1921D0` step 4: the visual sync clears 0x200 once it has pushed with flags 2 -- a
    /// one-shot, not a carry flag (findings/staff-mechanics-guards.md §5.4). A view calls this after
    /// its push; a hidden member is not synced and keeps it.</summary>
    public void CutRecordPushed() => Flags &= unchecked((ushort)~FlagCutRecord);
    public float FacingRadians => FacingQuarterTurns * (MathF.PI / 2f);
    /// <summary>In cell space (x across, z down the grid, y 0), the frame of <see cref="Guest.Position"/>.</summary>
    public Vector3 CellPosition => new(Position.X / 256f, 0, Position.Z / 256f);
    public ParkCell Cell => new(Position.X >> 8, Position.Z >> 8);
    /// <summary>The model to show (<see cref="StaffTables.ModelId"/>; an entertainer's costume
    /// variant is <see cref="StaffTables.EntertainerCostumeVariant"/>).</summary>
    public int ModelId => StaffTables.ModelId(Kind);
    /// <summary>⭐ `0x1DC428`: All Staff's "Motivation", `((100 - tiredness) + morale) / 2`. ⚠ NOT
    /// the candidate's motivation, which only the Hire screen shows.</summary>
    public int DisplayedMotivation => ((100 - Tiredness) + Morale) / 2;
    /// <summary>`0x1DC458`: calendar total days minus the hire day.</summary>
    public int DaysEmployed => Park.Clock.TotalDays - HireDay;
    /// <summary>`0x12B630` at the member's level: the Single Staff screen's Monthly Wage.</summary>
    public int MonthlyWage => StaffTables.MonthlyWage(Kind, Level);
    /// <summary>`0x1DC338`: what is paid at a month change or on firing.</summary>
    public int ProRatedWage => StaffTables.ProRatedWage(Kind, Level, State == StateStriking, DaysEmployed,
                                                        Park.Clock.DaysInPreviousMonth);
    /// <summary>`0x1DC2A8`: the next level's training cost (0 at level 4 is the list box's business).</summary>
    public int TrainingCost(bool freeBuild = false) => Level >= StaffTables.MaxLevel ? 0 : StaffTables.TrainingCost(Kind, Level, freeBuild);
    /// <summary>All Staff's Skill Level bar, `L * 25` (`0x10C138`: bar `+0x18 = L * 0x190000`).</summary>
    public int SkillBar => StaffTables.SkillBar(Level);
    /// <summary>The Training screen's "Monthly Wage": `0x12B630(cand, min(L+1, 5))` -- the wage AFTER
    /// training (`0x1FF830`). ⚠ At L = 4 it would read the table's terminating 0; the screen is never
    /// offered there.</summary>
    public int WageAfterTraining => StaffTables.WageBase[Math.Min(Level + 1, 5)] * StaffTables.WageMultiplier[(int)Kind];
    /// <summary>The Training screen's Skill bar: `min(L+1, 5) * 25` (`0x1FF830`: `uVar9 * 0x190000`).</summary>
    public int TrainingBar => Math.Min(Level + 1, 5) * 25;
    /// <summary>The Training screen's single list entry, "Level L+2": text row `0x36BA88[L+1]`.</summary>
    public int TrainingLevelTextRow => Level + 1 < StaffTables.TrainingLevelTextRows.Length ? StaffTables.TrainingLevelTextRows[Level + 1] : 0;
    /// <summary>⭐ Zoom To (`0x124360`): the camera goes to `vt+0xCC` = `0x192F20`, which is `{C+0x1C, _,
    /// C+0x1E, _}` -- the member's fine position, 1/256 cell. The view moves its camera there.</summary>
    public Point ZoomTarget => Position;
    /// <summary>`vt+0x1E4`: whether the list box offers Fire. Base `0x1DCA60` = 1; the mechanic overrides.</summary>
    public virtual bool CanBeFired => true;
    /// <summary>All Staff's "Time Employed": <see cref="StaffTables.TimeEmployed"/> (`0x142CE0`) of
    /// <see cref="DaysEmployed"/> (`0x1DC458`).</summary>
    public (int Months, int Tenths, int UnitTextRow) TimeEmployed => StaffTables.TimeEmployed(DaysEmployed);
    /// <summary>`(now &amp; 3) == (C+0xC &amp; 3)`: the four-tick quantum every tiredness/morale change uses.</summary>
    public bool OnPhase => (Park.Now & 3) == (Serial & 3);

    /// <summary>Instrumentation, not native fields: waypoint retires taken (each `0x191D78`), and
    /// dispatches that hit `0x1920D0`'s "bad state" print.</summary>
    public long WaypointsRetired { get; private set; }
    public long BadStates { get; private set; }

    // --------------------------------------------------------------------------------------------
    // Lifecycle (§2).

    /// <summary>`vt+0x1C4` then `vt+0x34`: the subclass activation → `0x1DB618` → `0x191360`.</summary>
    internal void Activate(int candidateSlot)
    {
        CandidateSlot = candidateSlot;                                   // vt+0x1C4 = 0x1DC918
        OnActivateJob();                                                 // subclass: zero P+0x58..
        Candidate = Park.Candidates.For(Kind, candidateSlot);           // 0x12AD28(0x12A550(), kind, slot)
        Candidate.Available = false;                                     // cand+0x1C = 0
        // 0x191360: serial, mode 2, slot -1, target 0, state 0, flags 0, shown.
        Serial = Park.Activations.Activate("staff:" + Kind);             // 0x1093B0
        Mode = ModeIdle;
        DropRouteWithoutFreeing();                                       // C+0x28 = -1 (see the method)
        Target = null;
        State = StateIdle;
        Flags = 0;
        SetShown(true);                                                  // vt+0x2C(C, 1)
        LogicalRequest = StaffTables.LogicalCarry;                       // 0x1DB644: 18
        // ⭐ MORALE IS DRAWN FIRST, then tiredness (0x1DB618, READ); the 0..100 clamps never bind.
        Morale = (sbyte)(70 + Park.Random(30));
        Tiredness = (sbyte)Park.Random(30);
        Level = Candidate.PayGrade & 7;
        MotivationCopy = (sbyte)Math.Clamp(Candidate.Motivation, 0, 100);
        // vt+0x54 = 0x1DC910 is empty ("release candidate" is a no-op).
        ResetPatrolArea();                                               // 0x1DC540
        SpeedBits = StaffTables.ActivationSpeed;                         // | 0x78
        HireDay = Park.Clock.TotalDays;                                  // 0x16B218(0x16AE90())
        // Model set 0x17BFF8(visual, ModelId, 0, -1) and scene add 0x17CE10 are the view's.
    }

    /// <summary>The subclass activation's own part (zeroing `P+0x58..`). Base: nothing.</summary>
    protected virtual void OnActivateJob() { }

    /// <summary>`0x1DC780 → 0x1928B0`: target 0, route freed, depth 0, HOLD, state 0. The hire tool's
    /// enter and the route reset use it.</summary>
    internal void Freeze()
    {
        Target = null; FreeRoute(); GoalDepth = 0; Flags |= FlagHeld; State = StateIdle;
    }

    /// <summary>The hire carry `0x128760`: position = the cursor's world position, re-hold.</summary>
    internal void Carry(Point position) { Position = position; Flags |= FlagHeld; }

    /// <summary>The hire drop `0x128888`: clear hold, state 0, depth 0, `vt+0x1D4` = `0x1DB750`
    /// (logical 13).</summary>
    internal void Place(Point position)
    {
        Position = position;
        Flags &= unchecked((ushort)~FlagHeld); State = StateIdle; GoalDepth = 0;
        LogicalRequest = StaffTables.LogicalWalk;
    }

    /// <summary>`vt+0x1F4` = `0x1DC6F0` (fire, the member's part): candidate back, route freed.
    /// The pro-rata wage is <see cref="ParkStaff.Fire"/>'s to debit.</summary>
    internal void Dismiss()
    {
        Candidate.Available = true;
        FreeRoute();
    }

    /// <summary>`vt+0x194` release, called by the pool free `0x14B608`: the subclass drops its job,
    /// then `0x1925A8` removes the visual (the view's) and frees the route again.</summary>
    internal virtual void Release() => FreeRoute();

    /// <summary>`vt+0x19C` = `0x1928F8`, "an object was removed": if it was my target, target 0,
    /// route freed, depth 0, state 0, SHOWN (which is what un-hides a handyman whose toilet was
    /// demolished mid-clean).</summary>
    internal void NoticeRemoved(object removed)
    {
        if (removed == null || !Equals(Target, removed)) return;
        Target = null; FreeRoute(); GoalDepth = 0; State = StateIdle; SetShown(true);
    }

    /// <summary>The per-type release on a route-system reset (`0x18C340` → `vt` release →
    /// `0x18C4B8`): natively each type ends in `0x1DC780` except for the states it protects, and the
    /// hold it sets is cleared again at the reset's end. Base: release everyone. ⚠ Called after the
    /// pool was reset, so the old chain's slots are already gone and are dropped, not freed.
    /// ⚠ A resting member is released WITHOUT being shown and stays invisible until the next show
    /// (§5.7, INFERRED) -- reproduced.</summary>
    internal virtual void OnRouteReset()
    {
        DropRouteWithoutFreeing();
        Target = null; GoalDepth = 0; State = StateIdle;
    }

    // --------------------------------------------------------------------------------------------
    // The tick (§2.5, §3).

    /// <summary>`vt+0x3C`: the per-type update. Held → nothing (the focus half of the freeze is dead,
    /// §6). Then `vt+0x174` think -- empty in the base and every subclass, so not called -- then the
    /// type's own states, then 0 → find work, else the staff base `0x1DC018`.</summary>
    internal void Update()
    {
        if (Held) return;
        if (UpdateJobState()) return;
        if (State == StateIdle) { FindWork(); return; }
        BaseUpdate();
    }

    /// <summary>The states a subclass's update handles itself (handyman 0x1B, 0x33; §3.1 table).
    /// Return true when handled. Base: none.</summary>
    protected virtual bool UpdateJobState() => false;

    /// <summary>`0x1DC018`: (think again, empty) 0 → find work, 0xD → patrol, 0xF → striking,
    /// 0x1A → strike walk, 0x31 → go rest, 0x32 → rest, anything else → the person `0x1920D0`.</summary>
    void BaseUpdate()
    {
        switch (State)
        {
            case StateIdle: FindWork(); break;
            case StatePatrol: Patrol(); break;
            case StateStriking: Striking(); break;
            case StateWalkToStrike: WalkToStrike(); break;
            case StateGoRest: GoRest(); break;
            case StateResting: Rest(); break;
            default: PersonDispatch(); break;
        }
    }

    /// <summary>`0x1920D0`: 1 → `0x1920B8` (depth 0, state 5); 2 → `vt+0x144(C, 0)`; 3 →
    /// `vt+0x14C`; 5 → `vt+0x15C`; 0xB → `vt+0x164` (empty for staff); else "bad state %X"
    /// (`0x12BEB0`) and nothing.</summary>
    void PersonDispatch()
    {
        switch (State)
        {
            case StateSetRandomDestination: GoalDepth = 0; State = StateWander; break;
            case StateSegmentEnd: SegmentEnd(); break;
            case StateWalk: WalkStep(); break;
            case StateWander: Wander(); break;
            case StateWaitForRoute: break;   // 0x1920C8: jr ra (the guest's override is its idle picker)
            default: BadStates++; break;
        }
    }

    /// <summary>⭐ `vt+0x1A4`, state 0: FIND WORK. The base `0x1DB800` is EMPTY and every native
    /// subclass overrides it.
    ///
    /// ⚠⚠ ADAPTER for the types whose job step is not built yet (entertainer, guard, researcher --
    /// areas B, C and D; the handyman and the mechanic have theirs): the common skeleton every native find-work shares --
    /// the tired/strike check `vt+0x1BC`, then the job search, then "nothing found → state 0xD
    /// (patrol)" -- with the job search FINDING NOTHING. So they walk, patrol, tire, rest and strike
    /// exactly as the base does, and never work. ⚠ Their idle-sound draws and any job-search draws
    /// (e.g. the entertainer's `rand(3)`) are not made, so the random stream differs from the
    /// console's for them until their step lands.</summary>
    protected virtual void FindWork()
    {
        if (TiredOrStriking()) return;
        GoalDepth = 0; State = StatePatrol;
    }

    /// <summary>`vt+0x1BC` = `0x1DBA90` (not overridden), READ from the MIPS `0x1DBA90..0x1DBB74`:
    /// <code>
    ///   if strike(my type):              vt+0x1DC (0x1DC9C8: free route, target 0); state 0x1A; return 1
    ///   if tiredness &gt;= 81 and state 0: state 0x31; return 1
    ///   if tiredness &gt;= 91 and phase:   morale -= 1 (min 0)       &lt;- unreachable
    ///   return 0
    /// </code>
    /// ⚠ The third line is kept as written and CANNOT run: all five callers are find-work functions
    /// in state 0, where &gt;= 81 has already returned (§3.4). So a worker finishes its current job
    /// before resting or striking -- this is only asked in state 0.</summary>
    protected bool TiredOrStriking()
    {
        if (Park.IsStriking(Kind))
        {
            FreeRoute(); Target = null;                                  // vt+0x1DC = 0x1DC9C8
            GoalDepth = 0; State = StateWalkToStrike;
            return true;
        }
        if (Tiredness >= StaffTables.RestTiredness && State == StateIdle)
        {
            GoalDepth = 0; State = StateGoRest;
            return true;
        }
        if (Tiredness >= StaffTables.DrainTiredness && OnPhase)
            Morale = (sbyte)Math.Max(0, Morale - 1);
        return false;
    }

    /// <summary>State 0xD, `0x1DC6B0 → 0x1DC558`, READ:
    /// <code>
    ///   dx = x1 - x0; dz = z1 - z0; if dx &lt;= 0 or dz &lt;= 0: state 5 (wander)
    ///   repeat 10: x = x0 + rand(dx); z = z0 + rand(dz)
    ///       if 0x149D20(x,z) and kind 2 and request(cell centre, 0x11): stamp; state 0xB; mode 1; depth 0
    ///   state 5
    /// </code>
    /// ⭐ The max column and row are never chosen; a one-wide area is no area; only the TARGET is
    /// confined -- the route may leave the rectangle, and no job search reads it (§4.7).</summary>
    void Patrol()
    {
        int dx = PatrolX1 - PatrolX0, dz = PatrolZ1 - PatrolZ0;
        if (dx > 0 && dz > 0)
            for (int i = 0; i < 10; i++)
            {
                int x = Park.Random(dx) + PatrolX0;
                int z = Park.Random(dz) + PatrolZ0;
                if (!Park.Tiles.InBoundsInner(x, z) || !Park.Tiles.IsPath(x, z)) continue;
                if (!Request(CellCentre(x, z), 0x11)) continue;
                Stamp = Park.Now; State = StateWaitForRoute; Mode = ModePatrol; GoalDepth = 0;
                return;
            }
        GoalDepth = 0; State = StateWander;
    }

    /// <summary>State 0xF, `vt+0x1AC` = `0x1DB900`: stand (logical 15 was set on arrival) until the
    /// type's strike flag (`0x16C988`) clears, then state 0, depth 0, logical 13.</summary>
    void Striking()
    {
        if (Park.IsStriking(Kind)) return;
        State = StateIdle; GoalDepth = 0; LogicalRequest = StaffTables.LogicalWalk;
    }

    /// <summary>State 0x1A, `vt+0x1B4` = `0x1DB808`: up to 10 tries of a route (flags **0x23**) to
    /// the strike point `0x1497C0`; admitted → stamp, mode 5, push, state 0xB; else stay 0x1A and
    /// retry next tick. The tile lookup `0x14E138` it makes on each point is discarded (READ).
    /// ⚠ With no fitted entrance the port has no strike point and makes no try.</summary>
    void WalkToStrike()
    {
        for (int i = 0; i < 10; i++)
        {
            if (Park.StrikePoint() is not { } point) return;
            if (!Request(point, 0x23)) continue;
            Stamp = Park.Now; Mode = ModeStrike; PushGoal(); State = StateWaitForRoute;
            return;
        }
    }

    /// <summary>⭐ State 0x31, `0x1DBB80`: the nearest usable STAFF ROOM, READ:
    /// <code>
    ///   over placed objects (0x14CD30) with DBA +0x2E bit 1 (0x1308C8) and status +0xA2 != 0:
    ///       d = |dx| + |dy| + |dz| of the vt+0x74 cells; take the first, replace only on strictly less
    ///   none: target 0, state 0xD, depth 0
    ///   else: mode 0x11, target = room; request(entry cell centre, 0x11)
    ///         admitted: stamp, push, state 0xB;   refused: stay 0x31, retry next tick
    /// </code>
    /// ⭐ NO CAPACITY: nothing counts who is already resting. ⭐ WITHOUT A ROOM a tired member never
    /// works again: 0x31 → 0xD → walk (more tiredness) → 0 → &gt;= 81 → 0x31 ... (INFERRED from the
    /// READ loop, findings/staff-management.md §8.2).
    /// ⚠ The distance drops the middle lane: the person's `vt+0x74` never writes it (stack residue,
    /// §4.9), so the port measures `|dx| + |dz|`. ⚠ The list order of `0x14CD30` is not traced; the
    /// port walks <see cref="ParkStaff.Features"/> in its order. ⚠ Status `+0xA2` is the port's
    /// <see cref="ParkRide.DestinationState"/>.</summary>
    void GoRest()
    {
        StaffFeature best = null; int bestDistance = 999999;
        foreach (var room in Park.PlacedFeatures())
        {
            if (!room.IsStaffRoom || room.Status == 0) continue;
            int d = Math.Abs(room.Origin.X - Cell.X) + Math.Abs(room.Origin.Z - Cell.Z);
            if (d < bestDistance || best == null) { best = room; bestDistance = d; }
        }
        if (best == null) { Target = null; State = StatePatrol; GoalDepth = 0; return; }
        Mode = ModeStaffRoom; Target = best.Key;
        if (!Request(CellCentre(best.Entry.X, best.Entry.Z), 0x11)) return;
        Stamp = Park.Now; PushGoal(); State = StateWaitForRoute;
    }

    /// <summary>State 0x32, `0x1DBF50`: hidden; on each phase tick morale +1 (max 100) and tiredness
    /// -2 (min 0); at tiredness &lt;= 0 `0x1DBFD0`: shown, target 0, **state 0xD** (patrol, not 0).
    /// From 81 that is 41 quanta, 161..164 ticks (§3.5).</summary>
    void Rest()
    {
        if (OnPhase)
        {
            Morale = (sbyte)Math.Min(100, Morale + 1);
            Tiredness = (sbyte)Math.Max(0, Tiredness - 2);
        }
        if (Tiredness < 1) EndRest();
    }

    /// <summary>`0x1DBFD0`: the end of a rest, and the Staff Room's "Kick Out" (area D).</summary>
    internal void EndRest()
    {
        SetShown(true); Target = null; State = StatePatrol; GoalDepth = 0;
    }

    // --------------------------------------------------------------------------------------------
    // Walking (§4).

    /// <summary>`vt+0x18C`: the walk speed in 1/256 cell per tick. Base `0x1DC928` = the speed
    /// bits; the handyman (`0x144D38`, <see cref="Handyman.Speed"/>) and the mechanic (`0x179308`,
    /// <see cref="Mechanic.Speed"/>) override it with their level tables.</summary>
    public virtual int Speed => SpeedBits;

    /// <summary>State 3, `vt+0x14C` = `0x191E98` (the guard overrides it, area C): the walk step,
    /// through the SAME cursor the guests walk with (<see cref="NativeGuestRoute"/>) over the SAME
    /// pool (<see cref="GuestWalk.NativeRoutes"/>). No slot → state 2 at once; readiness `0x191E10`
    /// (unless `[0x2E2920]`, 0 in the file) must pass; facing, per-axis clamped step of
    /// `max(5, speed) * 0x4000 &gt;&gt; 14`; off the grid → the whole chain freed, state 0; exact contact →
    /// state 2. Depth is zeroed on reaching state 2 and on the failure (MIPS `0x191E98`).</summary>
    protected virtual void WalkStep()
    {
        if (RouteSlot == -1) { DropFinishedRoute(); GoalDepth = 0; State = StateSegmentEnd; return; }
        bool ready = Park.AnimationReady?.Invoke(this) ?? true;
        _route.Step((sbyte)Speed, StaffTables.NativeDelta, Park.Tiles.Width, Park.Tiles.Height, ready);
        FacingQuarterTurns = _route.FacingQuarterTurns;
        if (_route.Failed) { _route = null; GoalDepth = 0; State = StateIdle; return; }  // chain freed by the cursor
        Position = _route.Position;
        if (_route.ExecutionState == StateSegmentEnd) { GoalDepth = 0; State = StateSegmentEnd; }
    }

    /// <summary>⭐ State 2, `vt+0x144`, base `0x1DB970`: with a slot still present, the WAYPOINT
    /// RETIRE -- <see cref="WaypointCost"/> then `0x191D78` (logical 13 unless
    /// <see cref="ReassertsWalkLogical"/> is false, retire one slot, state 3). With no slot, the
    /// ARRIVAL by mode (<see cref="Arrive"/>). Subclasses override this for their own modes and fall
    /// back to <see cref="BaseSegmentEnd"/>.</summary>
    protected virtual void SegmentEnd() => BaseSegmentEnd();

    protected void BaseSegmentEnd()
    {
        if (RouteSlot == -1) { DropFinishedRoute(); Arrive(); return; }
        WaypointCost();
        AdvanceWaypoint();
    }

    /// <summary>⭐ The retire hook, `0x1DB970`'s slot-present half: tiredness +1 (max 100) on a
    /// phase tick. READ. Because state 2 is entered once per reached waypoint and the phase is one
    /// tick in four, about ONE WAYPOINT IN FOUR costs a point (§0.2, §3.5) -- a planner route has one
    /// waypoint per turn plus its endpoint, the wander one per cell.
    /// ⭐ The seam for area C: the mechanic's `0x178BE8` applies morale -2 and tiredness -2 on every
    /// slot-present pass in mode 6 BEFORE this; it overrides this method and calls base last.</summary>
    protected virtual void WaypointCost()
    {
        if (OnPhase) Tiredness = (sbyte)Math.Min(100, Tiredness + 1);
    }

    /// <summary>`0x191D78`'s a1: 0 re-asserts logical 13 at every retire; the guard passes 1 and
    /// never does (§5.3).</summary>
    protected virtual bool ReassertsWalkLogical => true;

    /// <summary>`0x191D78`: [logical 13], save the successor, free ONE slot, successor → `C+0x28`,
    /// state 3, depth 0 -- exactly the cursor's own state-2 step with a slot present.</summary>
    void AdvanceWaypoint()
    {
        if (ReassertsWalkLogical) LogicalRequest = StaffTables.LogicalWalk;
        if (_route.ExecutionState != StateSegmentEnd)
            throw new InvalidOperationException("staff state 2 with a cursor that is not at a waypoint");
        _route.Step(0, 0, Park.Tiles.Width, Park.Tiles.Height, true);   // ExecutionState 2 + slot = the retire
        WaypointsRetired++;
        State = StateWalk; GoalDepth = 0;
    }

    /// <summary>`0x1DB970`'s no-slot half: depth 0, state 0, then by mode -- 5 (strike walk) →
    /// state 0xF, logical 15; 1 (patrol/wander) → mode 2, stay 0 (find work next tick); 0x11 (staff
    /// room) → state 0x32, hidden; anything else stays 0.</summary>
    protected virtual void Arrive()
    {
        byte mode = Mode;
        GoalDepth = 0; State = StateIdle;
        switch (mode)
        {
            case ModeStrike: State = StateStriking; LogicalRequest = StaffTables.LogicalStrike; break;
            case ModePatrol: Mode = ModeIdle; break;
            case ModeStaffRoom: State = StateResting; SetShown(false); break;
        }
    }

    /// <summary>State 5, `vt+0x15C` = `0x1913B8`: <see cref="NativeLocalWander"/>, then the state
    /// writes its four outcomes make natively.</summary>
    void Wander()
    {
        var result = NativeLocalWander.Run(Park.Tiles, Park.Routes, Position, Park.Random,
            target => Request(target, 0x03), FreeRoute);
        switch (result.Outcome)
        {
            case NativeLocalWander.Outcome.DirectToPath:
                AdoptRoute(result.Head); State = StateWalk; Mode = ModePatrol; GoalDepth = 0; break;
            case NativeLocalWander.Outcome.DirectSlotExhausted:
                State = StateIdle; GoalDepth = 0; break;
            case NativeLocalWander.Outcome.MapCentreRequested:
                // 0x191958..: mode 1, flag 0x20, stamp, push, state 0xB -- and NO depth reset.
                Mode = ModePatrol; Flags |= FlagMapCentre; Stamp = Park.Now; PushGoal(); State = StateWaitForRoute; break;
            default:
                AdoptRoute(result.Head); State = StateWalk; Mode = ModePatrol; GoalDepth = 0; break;
        }
    }

    // --------------------------------------------------------------------------------------------
    // Routes and events.

    /// <summary>`0x18DA78(C, C+0x1C, C+0x1E, tx, tz, flags, 0)`.</summary>
    protected bool Request(Point target, int flags) => Park.RouteRequests.Submit(this, Position, target, flags);

    /// <summary>`vt+0x16C`: a planner event. Base `0x1DB768`: 1 (built) → state 3, depth 0;
    /// 2 (failed) → mode 5: state 0; mode 1: state 5 (wander); else state 0xD; depth 0. Others
    /// ignored. The handyman overrides it.</summary>
    internal virtual void RouteEvent(int code) => BaseRouteEvent(code);

    protected void BaseRouteEvent(int code)
    {
        switch (code)
        {
            case 1: GoalDepth = 0; State = StateWalk; break;
            case 2:
                GoalDepth = 0;
                State = Mode == ModeStrike ? StateIdle : Mode == ModePatrol ? StateWander : StatePatrol;
                break;
        }
    }

    /// <summary>The builder's write of a new head to `C+0x28` (the old chain freed first).</summary>
    internal void AdoptRoute(int head)
    {
        FreeRoute();
        _route = new NativeGuestRoute(Position, Park.Routes, head, FacingQuarterTurns);
        _routeEpoch = Park.Routes.ResetGeneration;
    }
    ulong _routeEpoch;

    /// <summary>`0x192840(C+0x28)`; `C+0x28 = -1`. ⚠ Managed: after a pool reset (a route-system
    /// reset, or <see cref="GuestWalk.Clear"/>) the chain's slots are already gone with the pool's
    /// epoch, and freeing them again would free somebody else's -- so a stale cursor is dropped, not
    /// disposed.</summary>
    internal void FreeRoute()
    {
        if (_route != null && _routeEpoch == Park.Routes.ResetGeneration) _route.Dispose();
        _route = null;
    }

    /// <summary>A cursor whose chain is exhausted holds no slots: drop it.</summary>
    void DropFinishedRoute() => FreeRoute();

    /// <summary>⚠ `C+0x28 = -1` WITHOUT a free: activation (`0x191360`) and the route reset do this.
    /// On a reset the slots are already gone (see <see cref="FreeRoute"/>); at activation after a fire
    /// the fire already freed the route. Any chain still held at activation would be a native LEAK,
    /// so the port frees it when it still can rather than reproduce the leak.</summary>
    void DropRouteWithoutFreeing() => FreeRoute();

    /// <summary>`vt+0x2C` = `0x192C10(C, on)`: bit 0 of `C+0x2C`; the visual's `vt+0xAC` is the view's.</summary>
    protected void SetShown(bool on)
    {
        if (on) Flags |= FlagShown; else Flags &= unchecked((ushort)~FlagShown);
    }

    /// <summary>`C+0x18 + depth = state; depth++`. ⚠ Managed guard: natively a fifth push would
    /// write over `C+0x1C` (x); it cannot happen (events zero the depth), so this throws.</summary>
    protected void PushGoal()
    {
        if (GoalDepth >= _goals.Length) throw new InvalidOperationException("staff goal stack overflow (native would overwrite C+0x1C)");
        _goals[GoalDepth++] = State;
    }

    /// <summary>`0x1DC540`: the patrol rectangle unset, (0, 0, -1, -1).</summary>
    void ResetPatrolArea() { PatrolX0 = 0; PatrolZ0 = 0; PatrolX1 = -1; PatrolZ1 = -1; }

    /// <summary>⭐ `0x1DC490(C, a, b)`: the patrol rectangle as the min/max of two cell corners,
    /// stored as s8. This is the setter exactly; the patrol-area tool (<see cref="StaffPatrolTool"/>)
    /// calls it with its first corner already +1, which is the TOOL's off-by-one (findings/
    /// staff-management.md §4.3) and lives there, not here.</summary>
    public void SetPatrolArea(ParkCell a, ParkCell b)
    {
        PatrolX0 = unchecked((sbyte)Math.Min(a.X, b.X)); PatrolZ0 = unchecked((sbyte)Math.Min(a.Z, b.Z));
        PatrolX1 = unchecked((sbyte)Math.Max(a.X, b.X)); PatrolZ1 = unchecked((sbyte)Math.Max(a.Z, b.Z));
    }
    /// <summary>`0x1DC698`: "set?" = `word(C+0x44) != 0xFFFF0000`.</summary>
    public bool HasPatrolArea => !(PatrolX0 == 0 && PatrolZ0 == 0 && PatrolX1 == -1 && PatrolZ1 == -1);
    /// <summary>"Clear" is not a native action: no list-box entry or tool resets a set area (only
    /// activation's `0x1DC540` does). ⚠ Exposed for a caller that needs the unset state back, labelled.</summary>
    public void ClearPatrolArea() => ResetPatrolArea();

    /// <summary>The patrol-area tool's hold: its cursor `0x128C68` ORs `0x40` into `C+0x2C` every frame and
    /// its press/cancel (`0x128E10`, `0x128E8C`) clear it -- ONLY the flag, so the member keeps his state
    /// and route and resumes where he stood (unlike the hire tool's `0x1DC780` freeze).</summary>
    internal void SetHeld(bool on)
    {
        if (on) Flags |= FlagHeld; else Flags &= unchecked((ushort)~FlagHeld);
    }

    /// <summary>`0x1DC968` (the effect of training `0x1FF610`): a HIGHER level resets morale to 100
    /// and tiredness to 0; then level = `new &amp; 7`. The purchase (cost, affordability, focus, debit,
    /// sound) is <see cref="ParkStaff.Train"/>, which ends in this.</summary>
    public void Train(int level)
    {
        if (Level < level) { Morale = 100; Tiredness = 0; }
        Level = level & 7;
    }

    protected static Point CellCentre(int x, int z) => new(unchecked((short)((x << 8) + 0x80)), unchecked((short)((z << 8) + 0x80)));

    public override string ToString() => $"{Kind}#{PoolSlot} state 0x{State:x2} mode 0x{Mode:x2} at {Cell}";
}
