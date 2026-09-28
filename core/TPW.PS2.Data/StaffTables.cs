namespace TPW.PS2.Data;

/// <summary>The five staff types, in the CANDIDATE-DATABASE order -- the `kind` argument of
/// `0x12AD28(factory, kind, slot)` in each type's activation (`0x1783A8` mechanic, `0x12E0A8`
/// entertainer, `0x144C90` handyman, `0x140600` guard, `0x1B5FC0` researcher) and of the hire tool
/// `0x125460(toolmgr, 1, slot, kind)`. READ (findings/staff.md §1.1).
///
/// ⚠⚠ FOUR NUMBERINGS EXIST AND ALL FOUR ARE USED. This enum is the DB kind. The TYPE CODE that
/// `vt+0xAC` returns (and that the pool free `0x14B608`, the strike calendar and All Staff key on)
/// is a different number -- see <see cref="StaffTables.TypeCode"/>. Never cast one to the other.</summary>
public enum StaffKind : byte
{
    Mechanic = 0,
    Entertainer = 1,
    /// <summary>"Cleaners" in the player's text (`STR_PURCHASE_HANDYMEN`, row 658).</summary>
    Handyman = 2,
    Guard = 3,
    Researcher = 4,
}

/// <summary>⭐⭐ EVERY PER-TYPE AND PER-LEVEL NUMBER THE STAFF CODE READS, with the address it is
/// read from. All of them are READ out of `SLES_500.32` (findings/staff.md §1.1-§1.2,
/// findings/staff-person.md §4.1, findings/staff-handymen-entertainers.md §3.1,
/// findings/staff-management.md §6-§7), and the ParkSimAudit's staff family re-reads each table
/// from the executable at its address and compares it with the arrays below, so a typo here fails
/// by name rather than shipping as a slightly-wrong wage.
///
/// Level `L` is `C+0x48 &amp; 7`, 0..4, shown to the player as "Level 1..5". ⚠ The executable
/// never bounds-checks it: level 5+ would index the next row of whatever table (findings/
/// staff-person.md §4.1). Training stops at 4 (the list box offers it only while `L &lt; 4`,
/// `0x15DA8C`), so a level above 4 is a bug here and the accessors throw rather than read garbage.</summary>
public static class StaffTables
{
    /// <summary>Five slots per type, five candidates per type. `0x147EB0` builds each pool with five
    /// slot constructors and registers it as `0x15FA38(block, name, size, stride, 5)`;
    /// `GetNumberOfStaff(kind)` `0x12B160` jumps through `0x35C300` to `0x12B188`, `return 5`, for
    /// every kind. The advisor's ADD_MAX text says 5 is the maximum.</summary>
    public const int PoolSize = 5;

    /// <summary>Highest trainable level (the list box's `L &lt; 4` test, `0x15DA8C..0x15DAC4`).</summary>
    public const int MaxLevel = 4;

    /// <summary>The order `0x147EB0` builds the pools in: guards (`0x148744`), mechanics
    /// (`0x1487F4`), handymen (`0x1488A4`), researchers (`0x148954`), entertainers (`0x148A04`).
    /// ⚠ Observational: nothing in the port depends on allocation order across pools.</summary>
    public static readonly StaffKind[] PoolBuildOrder =
        { StaffKind.Guard, StaffKind.Mechanic, StaffKind.Handyman, StaffKind.Researcher, StaffKind.Entertainer };

    /// <summary>`vt+0xAC`: mechanic `0x1796C8` = 2, entertainer `0x12E750` = 1, handyman
    /// `0x145DB8` = 5, guard `0x141CD8` = 3, researcher `0x1B65B0` = 4. The pool free `0x14B608`
    /// switches on it, and so do the strike calendar (`cal + 0x304 + (t-1)*8`) and All Staff.</summary>
    public static int TypeCode(StaffKind kind) => kind switch
    {
        StaffKind.Mechanic => 2, StaffKind.Entertainer => 1, StaffKind.Handyman => 5,
        StaffKind.Guard => 3, StaffKind.Researcher => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Pool slot stride per type, from the `0x15FA38` registrations in `0x147EB0`
    /// (block sizes 0x204 / 0x218 / 0x1F0 / 0x22C / 0x1F0 over five slots plus a 0x10 header).
    /// ⚠ Informational: the port's slots are managed objects, not strided memory.</summary>
    public static int SlotStride(StaffKind kind) => kind switch
    {
        StaffKind.Mechanic => 0x64, StaffKind.Entertainer => 0x68, StaffKind.Handyman => 0x60,
        StaffKind.Guard => 0x6C, StaffKind.Researcher => 0x60,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The model id each activation asks the visual for, `(C+8)->vt+0x24->+0xC(visual,
    /// id, 0, -1)` = `0x17BFF8(visual, id, 0, -1)`: 0x1AB FatMechanic, 0x1A8 the entertainer's
    /// costume family, 0x1AA Handyman, 0x1A9 Guard, 0x1A7 Researcher (registry `0x2BF2B8`,
    /// findings/staff-person.md §5.6). For 0x1A8 the variant is <see cref="EntertainerCostumeVariant"/>.</summary>
    public static int ModelId(StaffKind kind) => kind switch
    {
        StaffKind.Mechanic => 0x1AB, StaffKind.Entertainer => 0x1A8, StaffKind.Handyman => 0x1AA,
        StaffKind.Guard => 0x1A9, StaffKind.Researcher => 0x1A7,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>⭐ WHICH COSTUME AN ENTERTAINER WEARS, and it is decided by the PARK, not the
    /// entertainer. `0x17D7E8` returns the first registry entry with `+0xC == id` whose variant
    /// `(+0x10 &amp; ~8)` is 0 or equals `[0x3952E8] + 1` -- the park index plus one. Park 0 wears
    /// variant 1 (JUNGLE dino, HALLOW franky, FANTASY flower, SPACE spaceman), park 1 variant 2
    /// (hunter, vampire, gnome, alien). READ, simulated over every world and park
    /// (findings/staff-person.md §5.6).
    ///
    /// ⚠ Park index 2 matches no staff entry, so the visual gets no model at all and readiness
    /// blocks the first walk forever (INFERRED). The front end only offers parks 0 and 1, so null is
    /// returned for anything else rather than a guessed costume.</summary>
    public static int? EntertainerCostumeVariant(int parkIndex) => parkIndex is 0 or 1 ? parkIndex + 1 : null;

    /// <summary>Candidate name text rows, tables at `0x35C220` (mechanic), `0x35C238`
    /// (entertainer), `0x35C268` (handyman), `0x35C250` (guard), `0x35C280` (researcher), five u32
    /// each, indexed by slot in `0x12A758`. ⚠ The TABLE order in the image is mechanic,
    /// entertainer, guard, handyman, researcher -- not the kind order -- which is why each kind
    /// names its own address.</summary>
    public static IReadOnlyList<int> CandidateNameRows(StaffKind kind) => kind switch
    {
        StaffKind.Mechanic => MechanicNames, StaffKind.Entertainer => EntertainerNames,
        StaffKind.Handyman => HandymanNames, StaffKind.Guard => GuardNames,
        StaffKind.Researcher => ResearcherNames,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
    public static readonly int[] MechanicNames = { 255, 256, 257, 258, 259 };      // 0x35C220
    public static readonly int[] EntertainerNames = { 460, 459, 463, 465, 467 };   // 0x35C238
    public static readonly int[] GuardNames = { 1016, 1017, 1018, 1019, 1020 };    // 0x35C250
    public static readonly int[] HandymanNames = { 778, 779, 782, 783, 784 };      // 0x35C268
    public static readonly int[] ResearcherNames = { 979, 980, 982, 985, 988 };    // 0x35C280
    public static uint CandidateNameTable(StaffKind kind) => kind switch
    {
        StaffKind.Mechanic => 0x35C220, StaffKind.Entertainer => 0x35C238, StaffKind.Handyman => 0x35C268,
        StaffKind.Guard => 0x35C250, StaffKind.Researcher => 0x35C280,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The candidate record's `+0xC` constant, written by `0x12A758`: 9 / 0xD / 0xB /
    /// 0xA / 0xC. ⚠ No staff reader was found (findings/staff-management.md §1.2); kept so a
    /// later reader has the value, not because anything here uses it.</summary>
    public static int CandidateRecordConstant(StaffKind kind) => kind switch
    {
        StaffKind.Mechanic => 9, StaffKind.Entertainer => 0xD, StaffKind.Handyman => 0xB,
        StaffKind.Guard => 0xA, StaffKind.Researcher => 0xC,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>`STR_PURCHASE_*` rows for the Hire tabs (`0x365020`): Mechanics 622, Entertainers
    /// 518, Cleaners 658, Guards 576, Researchers 63.</summary>
    public static int PurchaseTextRow(StaffKind kind) => kind switch
    {
        StaffKind.Mechanic => 622, StaffKind.Entertainer => 518, StaffKind.Handyman => 658,
        StaffKind.Guard => 576, StaffKind.Researcher => 63,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>⭐ The Hire panel's tabs, in the order `0x197C40` adds them (selector s = 0..4): Guards,
    /// Mechanics, Cleaners, Researchers, Entertainers. Their text rows are u16 at `0x365020` (576,
    /// 622, 658, 63, 518 -- <see cref="PurchaseTextRow"/>) and their keys u32 at `0x365050` (type
    /// codes 3, 2, 5, 4, 1), and `0x15D238`'s jump table `0x360C10` turns the selector into the DB kind
    /// (0→3, 1→0, 2→2, 3→4, 4→1). A tab is added only while `0x14CA20(10, key) &gt; 0`, i.e. 5 − the
    /// hired count (findings/staff-management.md §2.1).</summary>
    public const uint HireTabTextTable = 0x365020, HireTabKeyTable = 0x365050;
    public static readonly StaffKind[] HireTabs =
        { StaffKind.Guard, StaffKind.Mechanic, StaffKind.Handyman, StaffKind.Researcher, StaffKind.Entertainer };

    /// <summary>The ADD_MAX advisor message the hire drop `0x128918` posts when a type's raw count
    /// reaches 5: 0x5C mechanic, 0x5A entertainer, 0x58 handyman, 0x5E guard, 0x60 researcher
    /// (text, no voice; findings/staff-management.md §2.2).</summary>
    public static int AddMaxAdvisorMessage(StaffKind kind) => kind switch
    {
        StaffKind.Mechanic => 0x5C, StaffKind.Entertainer => 0x5A, StaffKind.Handyman => 0x58,
        StaffKind.Guard => 0x5E, StaffKind.Researcher => 0x60,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // ------------------------------------------------------------------------------------------
    // Per-level tables.

    /// <summary>Handyman rows, 12 bytes each at `0x35FBD8`: `+0` sweep ticks, `+4` toilet ticks,
    /// `+8` walk speed. Readers: the arrival `0x144EC8` (MIPS `0x144F14..0x144F40` and
    /// `0x144F54..0x144F84`) and `vt+0x18C` = `0x144D38`.</summary>
    public const uint HandymanTable = 0x35FBD8;
    public static readonly int[] HandymanSweepTicks = { 120, 60, 30, 20, 10 };   // 0x35FBD8 + 12L
    public static readonly int[] HandymanToiletTicks = { 180, 120, 60, 30, 15 }; // 0x35FBDC + 12L
    /// <summary>⚠ L4 is 18, SLOWER than L2/L3's 20 -- READ, not a typo here.</summary>
    public static readonly int[] HandymanSpeed = { 10, 15, 20, 20, 18 };         // 0x35FBE0 + 12L

    /// <summary>Mechanic rows, 4 bytes each at `0x3627C8`: u16 repair/install ticks (`0x1785F8`,
    /// `0x178A38`), u16 walk speed (`vt+0x18C` = `0x179308`). Area C uses the times; the speed is
    /// the person's and lives here.</summary>
    public const uint MechanicTable = 0x3627C8;
    public static readonly int[] MechanicWorkTicks = { 240, 180, 120, 60, 60 };  // u16 0x3627C8 + 4L
    public static readonly int[] MechanicSpeed = { 9, 12, 14, 16, 18 };          // u16 0x3627CA + 4L
    /// <summary>`[0x2BED10]` = 63, read only by the mechanic's chatter `0x1781B8` (at `0x1781D4`): he
    /// chatters on every tick whose count is NOT a multiple of it (findings/staff-mechanics-guards.md §3.2).</summary>
    public const uint MechanicChatterPeriodAddress = 0x2BED10;
    public const uint MechanicChatterPeriod = 63;

    /// <summary>Research work points per quantum, `0x366150` (reader `0x1B61C0`). Area D.</summary>
    public const uint ResearcherWorkTable = 0x366150;
    public static readonly int[] ResearcherWork = { 20, 30, 35, 40, 43 };

    /// <summary>How long a guest watches an entertainer of level L: `300 + 60*L` ticks, read by the
    /// guest `0x20FB88` from `P+0x50 &amp; 7` (findings/staff-handymen-entertainers.md §5.2). Area B's
    /// entertainer step consumes it.</summary>
    public static int EntertainerWatchTicks(int level) => 300 + 60 * Level(level);

    /// <summary>`W = 0x35C410` = [50, 55, 65, 80, 100, 0] and `M = 0x35C428` by kind; wage =
    /// `W[L] * M[kind]` display dollars a month (`0x12B630`). Index 5 is the table's terminating 0.</summary>
    public const uint WageTable = 0x35C410, WageMultiplierTable = 0x35C428;
    public static readonly int[] WageBase = { 50, 55, 65, 80, 100, 0 };
    public static readonly int[] WageMultiplier = { 3, 1, 1, 2, 3 };

    /// <summary>`T = 0x35C440` = [250, 275, 325, 400, 500, 0] and `N = 0x35C458` by kind; cost =
    /// `T[L] * N[kind]` for training FROM level L (`0x12B680`, via `0x1DC2A8`).</summary>
    public const uint TrainingTable = 0x35C440, TrainingMultiplierTable = 0x35C458;
    public static readonly int[] TrainingBase = { 250, 275, 325, 400, 500, 0 };
    public static readonly int[] TrainingMultiplier = { 3, 1, 1, 2, 6 };

    static int Level(int level) => (uint)level <= MaxLevel ? level
        : throw new ArgumentOutOfRangeException(nameof(level), level,
            "native tables are not bounds-checked; a level above 4 would read the next row");

    /// <summary>`0x12B630(record, L)`: `W[L] * M[kind]`, display dollars a month. The candidate
    /// form (`L &lt; 0` → the record's pay grade) is the Hire screen's; employees pass `C+0x48 &amp; 7`.
    /// ⚠ Every debit multiplies by 10 into the purse's tenths (findings/staff-management.md).</summary>
    public static int MonthlyWage(StaffKind kind, int level) => WageBase[Level(level)] * WageMultiplier[(int)kind];

    /// <summary>`0x1DC2A8`: `T[L] * N[kind]`, or 0 in the free-build modes (`DAT_002A60B8`, or
    /// `0x154428()` = park index 2). ⚠ The free-build test is the caller's to supply: the port does
    /// not hold those two flags.</summary>
    public static int TrainingCost(StaffKind kind, int level, bool freeBuild = false)
        => freeBuild ? 0 : TrainingBase[Level(level)] * TrainingMultiplier[(int)kind];

    /// <summary>⭐ `0x1DC338`, the wage as it is actually PAID -- at the month change and once more
    /// when fired:
    /// <code>
    ///   if state == 0x0F: 0                       // standing on strike
    ///   pct = days &gt;= len ? 100 : days*100/len   // signed div; len = the PREVIOUS month (0x16D2E8)
    ///   return wage(kind, L) * pct / 100          // truncated twice
    /// </code>
    /// `days` is `0x1DC458` = calendar total days (`0x16B218`) minus the hire day `C+0x40`.
    /// Only state 0x0F counts as striking: a striker still WALKING to the strike point is paid.</summary>
    public static int ProRatedWage(StaffKind kind, int level, bool standingOnStrike, int daysEmployed,
                                   int previousMonthLength)
    {
        if (standingOnStrike) return 0;
        if (previousMonthLength <= 0) throw new ArgumentOutOfRangeException(nameof(previousMonthLength)); // native trap(7)
        int pct = daysEmployed >= previousMonthLength ? 100 : daysEmployed * 100 / previousMonthLength;
        return pct * MonthlyWage(kind, level) / 100;
    }

    // ------------------------------------------------------------------------------------------
    // Thresholds and logicals of the staff base.

    /// <summary>Find work sends to rest at tiredness &gt;= 81: `slti 0x51` on the `lb` in `0x1DBA90`.</summary>
    public const int RestTiredness = 81;
    /// <summary>`0x1DBA90`'s morale drain at tiredness &gt;= 91 -- ⚠ UNREACHABLE (findings/
    /// staff-person.md §3.4): its only callers run in state 0, where &gt;= 81 has already returned.</summary>
    public const int DrainTiredness = 91;
    /// <summary>`0x145250`: a toilet is a candidate while its condition is `&lt; 0x3C`.</summary>
    public const int ToiletCandidateBelow = 60;
    /// <summary>`0x1456D8`: condition `&lt; 0x28` at completion costs morale 10 and tiredness 5 more.</summary>
    public const int ToiletFilthyBelow = 40;
    /// <summary>Activation's speed bits: `C+0x48 = (C+0x48 &amp; ~0xF8) | 0x78` in `0x1DB618`, i.e. 15.</summary>
    public const int ActivationSpeed = 15;

    /// <summary>Logical animation requests (low five bits of `C+0x30`; table `0x2AAD48`).
    /// 18 = activation / hire carry (`0x1DB644`, main s3 loop); 13 = walk (main s0); 16 = work
    /// (first s4, main s5, last s6); 15 = striking (main s7); 11 = the entertainer after a show.</summary>
    public const int LogicalCarry = 18, LogicalWalk = 13, LogicalWork = 16, LogicalStrike = 15, LogicalAfterShow = 11;

    /// <summary>⭐ One frame's walk delta. `D = [0x397640] = min((counter - prev) &lt;&lt; 7, 0x4000)`
    /// and the counter gains 10000 a frame (`0x11E758`), so D is 0x4000 every tick and the step
    /// `max(5, speed) * D &gt;&gt; 14` equals the speed: walk speeds are 1/256 cell per tick
    /// (findings/staff-person.md §4.1, coaster-trains.md §2.2).</summary>
    public const int NativeDelta = 0x4000;

    // ------------------------------------------------------------------------------------------
    // Management (findings/staff-management.md §7-§12).

    /// <summary>The five TYPE CODES in the order the strike check `0x16C120`, the All Staff type list
    /// (`0x10BB68`) and the Staff Room screen (`0x1DCC18`) walk them: entertainer 1, mechanic 2, guard
    /// 3, researcher 4, handyman 5 (lists `0x14D670`, `0x14D650`, `0x14D228`, `0x14D660`, `0x14D640`).</summary>
    public static readonly StaffKind[] TypeCodeOrder =
        { StaffKind.Entertainer, StaffKind.Mechanic, StaffKind.Guard, StaffKind.Researcher, StaffKind.Handyman };

    /// <summary>The kind whose <see cref="TypeCode"/> is <paramref name="code"/> (1..5).</summary>
    public static StaffKind KindOfTypeCode(int code) => (uint)(code - 1) < 5 ? TypeCodeOrder[code - 1]
        : throw new ArgumentOutOfRangeException(nameof(code));

    /// <summary>⭐ `0x361D98` (read as `0x361D97[t]` by the ladder), by type code − 1: the per-type
    /// offset of every staff advisor message -- ent 3, mech 0, guard 2, res 4, handy 1. So UNHAPPY is
    /// 0x16 mech, 0x17 handy, 0x18 guard, 0x19 ent, 0x1A res (findings/staff-management.md §9.2).</summary>
    public const uint StrikeMessageBaseTable = 0x361D98;
    public static readonly int[] StrikeMessageBase = { 3, 0, 2, 4, 1 };
    public static int StrikeMessage(StaffKind kind, int offset) => StrikeMessageBase[TypeCode(kind) - 1] + offset;
    /// <summary>The ladder's message offsets (`0x16C2A0`, `0x16C120`): only UNHAPPY and HAPPIER have
    /// text (and voices); VERY_UNHAPPY, STRIKING and STRIKE_END_BAD are row 310 and silent.</summary>
    public const int MessageUnhappy = 0x16, MessageVeryUnhappy = 0x1B, MessageStriking = 0x20,
                     MessageHappier = 0x25, MessageStrikeEndBad = 0x2A;
    /// <summary>`0x16C500`: unhappy when there are at least 2 of the type and the average energy
    /// `Σ(100 − tiredness)/n` is &lt; 15 (signed `div`), or the average morale `Σmorale/n` is &lt;= 14
    /// (`0xE &lt; (uint)avg` is the happy test).</summary>
    public const int StrikeMinimumStaff = 2, StrikeEnergyBelow = 15, StrikeMoraleAtMost = 14;

    /// <summary>The Staff Room list box's Kick Out entries (`0x15D850`, action table `0x360FA0..0x360FC0`),
    /// in the order it adds them: Mechanics 455 (`0x124478`), Researchers 523 (`0x1244F8`), Cleaners 446
    /// (`0x124578`), Entertainers 520 (`0x1245F8`), Guards 830 (`0x124678`).</summary>
    public static readonly StaffKind[] KickOutOrder =
        { StaffKind.Mechanic, StaffKind.Researcher, StaffKind.Handyman, StaffKind.Entertainer, StaffKind.Guard };
    public static int KickOutTextRow(StaffKind kind) => kind switch
    {
        StaffKind.Mechanic => 455, StaffKind.Researcher => 523, StaffKind.Handyman => 446,
        StaffKind.Entertainer => 520, StaffKind.Guard => 830,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>⭐ The labels the All Staff type list (`0x10BB68`: 0x44, 0x360, 0x1A5, 0x377, 0x45) and the
    /// Staff Room screen (`0x369790`) put against each type, by type code − 1: Entertainers 68, Mechanics
    /// 864, Guards 421, Researchers 887, Cleaners 69.</summary>
    public const uint StaffRoomLabelTable = 0x369790;
    public static readonly int[] TypeLabelRows = { 68, 864, 421, 887, 69 };
    public static int TypeLabelRow(StaffKind kind) => TypeLabelRows[TypeCode(kind) - 1];

    /// <summary>⭐ All Staff's "Time Employed", `0x142CE0(buf, days)`, READ (MIPS `0x142CE0..0x142DC0`):
    /// `days/28` "." `(days%28)*10/28` (signed `div` by 0x1C, 28-DAY months whatever the calendar says),
    /// then text row 10 ("mnth") when days == 28 exactly, else row 0x15D = 349 ("mnths"). No separator.</summary>
    public static (int Months, int Tenths, int UnitTextRow) TimeEmployed(int days)
        => (days / 28, days % 28 * 10 / 28, days == 28 ? 10 : 349);

    /// <summary>All Staff's Skill Level bar value: `L * 25`, from the bar object's `+0x18` = `L * 0x190000`
    /// (16.16, `0x10C138`); the Training screen's bar is `min(L+1, 5) * 25` (`0x1FF830`).</summary>
    public static int SkillBar(int level) => level * 25;

    /// <summary>Training: the target level the screen offers is `0x3988A8[0] = L + 1` (`0x1FF578`), and
    /// the list-box entry "Training" (row 930) is added only while `L &lt; 4` (`0x15DA8C..0x15DAC4`).</summary>
    public const int TrainingTextRow = 930, FireTextRow = 906, SetPatrolAreaTextRow = 79, ZoomToTextRow = 391;
    /// <summary>The Training screen's list entry `0x36BA88[L+1]`: rows 110, 111, 112, 114, 116 = "Level 1..5"
    /// (index 5 = row 0, unreachable).</summary>
    public static readonly int[] TrainingLevelTextRows = { 110, 111, 112, 114, 116 };

    /// <summary>UI sounds (the laptop/tool bank the viewer's build tool cues 0xAF and 0x1F in):
    /// 0xAF refused, 0x12F training bought / hire drop, 0x1F the debit's cash, 0xDB a patrol-tool
    /// press, 0xC5 a gold ticket (`0x1C38C0`), 0xBA "nothing left to research" (`0x151998`).</summary>
    public const int UiSoundRefused = 0xAF, UiSoundTrained = 0x12F, UiSoundCash = 0x1F, UiSoundPatrolPress = 0xDB,
                     UiSoundGoldTicket = 0xC5, UiSoundAllResearched = 0xBA;

    /// <summary>`0x1B6640`: the research budget at init, 80 (`param_1[1] = 0x50`); `0x1B5410` sets it to
    /// 100 whenever the Research screen is constructed (`0x1B6848(mgr, 100)` at `0x1B54CC..0x1B54D4`).</summary>
    public const int ResearchBudgetInitial = 80, ResearchBudgetScreen = 100;
    /// <summary>`0x1B60A8`: a researcher's find-work draws `rand(10)` FIRST and researches on `&lt; 3`.</summary>
    public const int ResearchChanceOutOf = 10, ResearchChanceBelow = 3;
    /// <summary>Bank-8 events of the researcher: 0xA8 on choosing to patrol (handle `P+0x58`), 0x8A per
    /// research quantum (handle `P+0x5C`).</summary>
    public const int SoundResearcherPatrol = 0xA8, SoundResearcherWork = 0x8A;
    public const int HandleResearcherPatrol = 0x58, HandleResearcherWork = 0x5C;

    /// <summary>`0x130510`: a staff room whose status turns 2 starts looping bank 8 event 0xBC at the room
    /// (handle `+0xB8`); `0x130498` (`vt+0x10C`, its removal) stops it.</summary>
    public const int SoundStaffRoomAmbience = 0xBC;

    /// <summary>The Security Award, `0x16BC70`: hidden award bit 0, message 0xA0 GOLD_TICKET_BROTHER, when
    /// `0x104CE0(0x40) &gt; 0x50`.</summary>
    public const int SecurityAwardBit = 0, SecurityAwardMessage = 0xA0, SecurityAwardCoverageAbove = 0x50;
    /// <summary>`STR_ADVMES_ADD_WAGES_HIGH`, rule 103's message.</summary>
    public const int WagesHighMessage = 0x52;

    /// <summary>The type mask the advisor producers take (`0x104FB0`, `0x1053A8`, `0x105538`,
    /// `0x105688` switch on it): 1 mechanic (`0x14D650`), 2 handyman (`0x14D640`), 4 guard
    /// (`0x14D228`), 8 entertainer (`0x14D670`), 0x10 researcher (`0x14D660`; only `0x105538` and
    /// `0x105688` accept it).</summary>
    public static int AdvisorMask(StaffKind kind) => kind switch
    {
        StaffKind.Mechanic => 1, StaffKind.Handyman => 2, StaffKind.Guard => 4, StaffKind.Entertainer => 8,
        StaffKind.Researcher => 0x10,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>`0x364A18`: the four directions as 8-byte `{s16 dx, pad, s16 dz, pad}` entries --
    /// dir0 (+1,0), dir1 (0,+1), dir2 (-1,0), dir3 (0,-1). Shared by the local wander.</summary>
    public const uint DirectionTable = 0x364A18;
    public static readonly (int Dx, int Dz)[] Directions = { (1, 0), (0, 1), (-1, 0), (0, -1) };
    /// <summary>`0x364818`: the link-byte bit for each of those directions: 4, 0x10, 0x40, 1
    /// (east, south, west, north; z grows south, findings/paths.md §3).</summary>
    public const uint DirectionBitsTable = 0x364818;
    public static readonly byte[] DirectionBits = { 0x04, 0x10, 0x40, 0x01 };
    /// <summary>`0x364A38`, u32 `[previous][candidate]`: 100 straight on, 40 a turn, 10 back.
    /// The on-path wander's weighted pick.</summary>
    public const uint WanderWeightTable = 0x364A38;
    public static readonly int[,] WanderWeights =
    {
        { 100, 40, 10, 40 },
        { 40, 100, 40, 10 },
        { 10, 40, 100, 40 },
        { 40, 10, 40, 100 },
    };
}
