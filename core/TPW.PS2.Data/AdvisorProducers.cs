namespace TPW.PS2.Data;

/// <summary>⚠ ADAPTER: one placed object as the advisor's pool producers read it (`0x103970`, `0x103718`,
/// `0x104760`). Natively these are the object pools `0x39528C..0x3952A4` (count at `+0xC`, list at `+8`)
/// and the feature list `0x14CD30`; the port keeps a placement in <see cref="ParkSim.Rides"/> only while it
/// runs a script, so a view with scriptless placements supplies its own census
/// (<see cref="AdvisorProducers.Placements"/>).</summary>
/// <param name="Kind">The compiled record's kind: rides are 3 ordinary, 7 tour, 6 track, 1 coaster
/// (the pools of `0x103970` bits 1, 2, 4, 8); 4 shop; 5 sideshow; 2 feature.</param>
/// <param name="Status">`+0xA2` (<see cref="ParkRide.DestinationState"/>): 0 does not count in `0x103970`.</param>
/// <param name="FeatureFlags">A feature's DBA `+0x2E` (bit 0 toilet, 1 staff room, 3 camera).</param>
/// <param name="Ride">The live facility, when it has one (a toilet's cleanliness lives there).</param>
public readonly record struct AdvisorPlacement(AssetResourceDatabase.AssetKind Kind, byte Status, byte FeatureFlags, ParkRide Ride)
{
    /// <summary>`0x103970`'s feature classing, ONCE per feature in this order: toilet (`vt+0x134`), camera
    /// (`0x130858`), staff room (`0x1308C8`), else "other" (0x10). ⚠ Tested on the DBA bits, as
    /// <see cref="ParkStaff.FeatureCount"/> does (<see cref="StaffFeature"/>).</summary>
    public int FeatureClass => (FeatureFlags & 1) != 0 ? 0x20 : (FeatureFlags & 8) != 0 ? 0x40 : (FeatureFlags & 2) != 0 ? 0x80 : 0x10;
    public bool IsToilet => Kind == AssetResourceDatabase.AssetKind.Feature && (FeatureFlags & 1) != 0;
}

/// <summary>⭐⭐ THE 79 PRODUCERS WIRED TO THE PORT (`0x10DE38`, jump table `0x359860`, READ; the table's 79
/// targets re-read from the ELF for this step), findings/advisor-rules.md §5. What each reads and where
/// the port has it -- REAL = the console's formula over a port quantity; HOOK = ⚠ an adapter whose default
/// keeps the rules that read it quiet; OWN = the rule object's (<see cref="AdvisorScheduler.Produce"/>):
/// <code>
///   v0      HOOK  park open [0x2B72A4]              ParkOpen (default OPEN: the port's parks admit guests)
///   v1 v2   REAL  % thirsty / hungry  (0x211C80 class 1 / 2 over the guests)
///   v3..v5  REAL  days, months since creation, years (ParkClock), each min(x, 30000)
///   v6..v11 REAL  staff counts minus the held one, their sum      ParkStaff.AdvisorCount
///   v12     REAL  guests (0x14D690)                              ParkVisitors.Plans (⚠ the live set)
///   v13     REAL  litter (0x14D208)                               ParkStaff.Litter.Count
///   v14..v17 REAL rides / shops / sideshows (held-adjusted) and standing features (0x103970)   Placements
///   v18..v20 REAL toilets / staff rooms / cameras standing        ParkStaff.FeatureCount
///   v21..v30 HOOK variety and research % (0x103B20, 0x1044B0, 0x104A40, 0x104BD0): the research
///                 DATABASE they read (0x389650, 0x12B6D0 availability, 0x12BA08 level) is not ported
///   v31     REAL  toilets' average dirtiness (0x104760)           ParkRide.Condition (+0xB4)
///   v32     REAL  toilet coverage (0x104CE0(0x20))               ParkStaff.FeatureCoverage
///   v33..v45 REAL no-area %, patrol coverage, training %          ParkStaff
///   v46     REAL  mechanics striking (0x16C988(cal, 2))           ParkStaff.IsStriking
///   v47     REAL  average happiness (0x10E270)                   VisitorNeeds +0x75
///   v48     REAL  balance / 1000, ±30000 (0x10E2D0)               ParkFinances.Balance
///   v49     REAL  wages above income two months (0x10E32C)        ParkFinances.WagesHigh
///   v50     REAL  park size (0x103718(0x3FF))                     Placements
///   v51     REAL  a project running (0x1B69F0(0x1B6798()))        ResearchManager.ActiveCount
///   v52 v53 OWN   copies of v75 / v76 (the shipped quirk)
///   v54     REAL  % needing a toilet (class 3)
///   v55     REAL  highest staff tiredness (0x105798)              ParkStaff.MaxTiredness
///   v56..v77 OWN  the 22 event counters;  v78 OWN the scheduler's elapsed days
/// </code>
/// ⚠ "Held on the cursor": `0x103970` subtracts the object the build tool is carrying (modes 5, 6, 7, 0xB,
/// 0xE, 0x10) and `0x103718` does not. The port's build tool holds a GHOST, not a pool object, so there is
/// nothing to subtract from v14..v16 and nothing extra in v50 -- v50 is one object short while a build tool
/// is out (said, not modelled).</summary>
public sealed class AdvisorProducers : IAdvisorProducers
{
    public AdvisorProducers(ParkClock clock, ParkSim sim = null, ParkStaff staff = null, ParkVisitors visitors = null)
    {
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Sim = sim ?? staff?.Sim ?? visitors?.Sim;
        Staff = staff;
        Visitors = visitors ?? staff?.Visitors;
    }

    public ParkClock Clock { get; }
    public ParkSim Sim { get; }
    public ParkStaff Staff { get; }
    public ParkVisitors Visitors { get; }

    /// <summary>⚠ HOOK for v0, `0x14E538` = `[0x2B72A4]`, the open-park flag (set by Open Park `0x14E4C0`).
    /// The port has no park-level flag. Null answers OPEN: the port admits guests to every park it runs
    /// (the bus adapter's `open` is the same assumption) -- and open keeps rule 0 (OPEN_PARK) quiet.</summary>
    public Func<bool> ParkOpen { get; set; }

    /// <summary>⚠ ADAPTER: the placed-object census for v14..v17, v31, v50 (see <see cref="AdvisorPlacement"/>).
    /// Null = every placement in <see cref="ParkSim.Rides"/> whose compiled record joined (its kind and, for
    /// a feature, flags), status `DestinationState`; ⚠ an unjoined placement counts nowhere except a
    /// lavatory (<see cref="ParkRide.ProvidesRelief"/>, <see cref="StaffFeature.Of"/>'s fallback).</summary>
    public Func<IEnumerable<AdvisorPlacement>> Placements { get; set; }

    /// <summary>⚠ HOOK for v21/v25/v27/v29, `0x103B20(mask)`: variety % of rides (0xF) / shops (0x100) /
    /// sideshows (0x200) / features (0xF0) -- distinct types built over types AVAILABLE (`0x12B6D0`), which is
    /// the unported research database. Null answers the quiet value <see cref="QuietVariety"/>.</summary>
    public Func<int, int> Variety { get; set; }
    /// <summary>⚠ HOOK for v22/v26/v28/v30, `0x1044B0(mask)`: researched % of the same kinds (the research
    /// database). Null answers 100 (research complete), which keeps rules 81, 84, 89, 93, 96 quiet.</summary>
    public Func<int, int> ResearchPercent { get; set; }
    /// <summary>⚠ HOOK for v23, `0x104A40`: upgrades in use % (placed tiers over researched levels,
    /// `0x12BA08`). Null answers 100, which keeps rule 87 (`v23 == 0`) quiet.</summary>
    public Func<int> UpgradesInUse { get; set; }
    /// <summary>⚠ HOOK for v24 and producer 53's latch, `0x104BD0`: upgrade research %. Null answers 100,
    /// which keeps rules 81 (`&lt; 100`) and 86 (`== 0`) quiet (87 also needs v23 == 0).</summary>
    public Func<int> UpgradeResearchPercentHook { get; set; }

    /// <summary>The quiet variety: rule 80/84 need ride variety &gt; 99 / == 100, 88/92 need shop/sideshow
    /// variety &lt; 40, 89/93/96 need == 100 -- any of 40..99 keeps every one quiet; 50 is used.</summary>
    public const int QuietVariety = 50;

    /// <summary>`0x10DFD0`: `min(month + 12·year, 30000)` (unsigned compare).</summary>
    public int Months => Min30000(Clock.Month + 12 * Clock.Year);
    public int UpgradeResearchPercent => UpgradeResearchPercentHook?.Invoke() ?? 100;

    static int Min30000(int v) => (uint)v < 30001u ? v : 30000;
    static short S(int v) => unchecked((short)v);

    /// <summary>⭐ `0x10DE38`'s arms for the indices outside the rule object.</summary>
    public short Produce(int i)
    {
        switch (i)
        {
            case 0: return S(ParkOpen?.Invoke() ?? true ? 1 : 0);
            case 1: return S(ClassPercent(1));
            case 2: return S(ClassPercent(2));
            case 3: return S(Min30000(Clock.TotalDays));
            case 4: return S(Months);
            case 5: return S(Min30000(Clock.Year));
            case 6: return S(StaffCount(StaffKind.Entertainer));          // 0x14D830
            case 7: return S(StaffCount(StaffKind.Mechanic));             // 0x14D7C0
            case 8: return S(StaffCount(StaffKind.Guard));                // 0x14D750
            case 9: return S(StaffCount(StaffKind.Researcher));           // 0x14D7F8
            case 10: return S(StaffCount(StaffKind.Handyman));            // 0x14D788
            case 11:                                                      // 0x10E080: a 16-bit running sum
                return S(StaffCount(StaffKind.Entertainer) + StaffCount(StaffKind.Mechanic) + StaffCount(StaffKind.Guard)
                         + StaffCount(StaffKind.Researcher) + StaffCount(StaffKind.Handyman));
            case 12: return S(GuestCount);                                // 0x14D690
            case 13: return S(Staff?.Litter.Count ?? 0);                  // 0x14D208
            case 14: return S(PoolCount(0xF));
            case 15: return S(PoolCount(0x100));
            case 16: return S(PoolCount(0x200));
            case 17: return S(PoolCount(0xF0));
            case 18: return S(Staff?.FeatureCount(0x20) ?? 0);           // 0x103970(0x20)
            case 19: return S(Staff?.FeatureCount(0x80) ?? 0);           // 0x103970(0x80)
            case 20: return S(Staff?.FeatureCount(0x40) ?? 0);           // 0x103970(0x40)
            case 21: return S(Variety?.Invoke(0xF) ?? QuietVariety);
            case 22: return S(ResearchPercent?.Invoke(0xF) ?? 100);
            case 23: return S(UpgradesInUse?.Invoke() ?? 100);
            case 24: return S(UpgradeResearchPercent);
            case 25: return S(Variety?.Invoke(0x100) ?? QuietVariety);
            case 26: return S(ResearchPercent?.Invoke(0x100) ?? 100);
            case 27: return S(Variety?.Invoke(0x200) ?? QuietVariety);
            case 28: return S(ResearchPercent?.Invoke(0x200) ?? 100);
            case 29: return S(Variety?.Invoke(0xF0) ?? QuietVariety);
            case 30: return S(ResearchPercent?.Invoke(0xF0) ?? 100);
            case 31: return S(ToiletDirtiness());
            case 32: return S(Staff?.FeatureCoverage(0x20) ?? 0);        // 0x104CE0(0x20)
            case 33: return S(Staff?.NoPatrolAreaPercent(1) ?? 0);       // 0x1053A8(1, 2, 4, 8)
            case 34: return S(Staff?.NoPatrolAreaPercent(2) ?? 0);
            case 35: return S(Staff?.NoPatrolAreaPercent(4) ?? 0);
            case 36: return S(Staff?.NoPatrolAreaPercent(8) ?? 0);
            case 37: return S(Staff?.PatrolCoverage(1) ?? 0);            // 0x104FB0(1, 2, 4, 8)
            case 38: return S(Staff?.PatrolCoverage(2) ?? 0);
            case 39: return S(Staff?.PatrolCoverage(4) ?? 0);
            case 40: return S(Staff?.PatrolCoverage(8) ?? 0);
            case 41: return S(Staff?.TrainingPercent(1) ?? 0);           // 0x105538(1, 2, 4, 8, 0x10)
            case 42: return S(Staff?.TrainingPercent(2) ?? 0);
            case 43: return S(Staff?.TrainingPercent(4) ?? 0);
            case 44: return S(Staff?.TrainingPercent(8) ?? 0);
            case 45: return S(Staff?.TrainingPercent(0x10) ?? 0);
            case 46: return S(Staff?.IsStriking(StaffKind.Mechanic) == true ? 1 : 0);
            case 47: return S(AverageHappiness());
            case 48: return S(Balance());
            case 49: return S(Sim?.Finances.WagesHigh == true ? 1 : 0);
            case 50: return S(ParkSize());
            case 51: return S(Staff != null && Staff.Research.ActiveCount > 0 ? 1 : 0);   // 0x1B69F0: any slot +0xC
            case 54: return S(ClassPercent(3));
            case 55: return S(Staff?.MaxTiredness() ?? 0);                // 0x105798
            default: return 0;
        }
    }

    int StaffCount(StaffKind kind) => Staff?.AdvisorCount(kind) ?? 0;

    // ------------------------------------------------------------------------------------------------
    // Guests.

    /// <summary>⚠ `0x14D690`, the guest pool's active count. The port's live guest set is
    /// <see cref="ParkVisitors.Plans"/> (it keeps guests in Recovering and drops the retired).</summary>
    public int GuestCount => Visitors?.Plans.Count ?? 0;

    IEnumerable<VisitorWants> GuestWants()
    {
        var needs = Visitors?.Needs;
        if (needs == null) yield break;
        foreach (int id in Visitors.Plans.Keys)
            if (needs.Has(id)) yield return needs.Of(id);
    }

    /// <summary>⭐ `0x211C80`, the guest's most pressing need (MIPS `0x211C80..0x211D24`, signed bytes):
    /// hunger ≥ 81 and thirst ≥ 81 → 7; hunger ≥ 81 → 2; thirst ≥ 76 → 1; toilet ≥ 76 → 3; sick ≥ 76 → 4;
    /// happiness ≥ 76 → 5; boredom ≥ 76 → 6; happiness &lt; 25 → 8; else 0.</summary>
    public static int NeedClass(VisitorWants w)
    {
        sbyte hunger = unchecked((sbyte)w.Hunger), thirst = unchecked((sbyte)w.Thirst), toilet = unchecked((sbyte)w.Toilet),
              sick = unchecked((sbyte)w.Sick), happy = unchecked((sbyte)w.Happiness), bored = unchecked((sbyte)w.Boredom);
        if (hunger >= 81) return thirst > 80 ? 7 : 2;
        if (thirst > 75) return 1;
        if (toilet >= 76) return 3;
        if (sick > 75) return 4;
        if (happy >= 76) return 5;
        if (bored >= 76) return 6;
        return happy < 25 ? 8 : 0;
    }

    /// <summary>`0x10DE98`/`0x10DEE4`/`0x10DF34`: `n = guests; n == 0 ? 0 : #{class == k}·100 / n` (signed).
    /// ⚠ A guest with no wants row (the port's <see cref="VisitorNeeds"/> absent or not seeded) counts in n
    /// and in no class.</summary>
    public int ClassPercent(int k)
    {
        int n = GuestCount;
        if (n == 0) return 0;
        int c = 0;
        foreach (var w in GuestWants()) if (NeedClass(w) == k) c++;
        return c * 100 / n;
    }

    /// <summary>⭐ `0x10E270`: Σ happiness (`lb +0x75`) over the guests, `divu` by n, capped at 100; 0 with none.</summary>
    public int AverageHappiness()
    {
        int n = GuestCount;
        if (n == 0) return 0;
        int sum = 0;
        foreach (var w in GuestWants()) sum += unchecked((sbyte)w.Happiness);
        uint avg = unchecked((uint)sum) / (uint)n;
        return avg < 101 ? (int)avg : 100;
    }

    // ------------------------------------------------------------------------------------------------
    // Money.

    /// <summary>`0x10E2D0`: `clamp(balance / 10 / 100, −30000, 30000)` (signed divides): hundreds of
    /// currency units, so the $30,000 opening balance is 300.</summary>
    public int Balance()
    {
        int b = (Sim?.Finances.Balance ?? 0) / 10 / 100;
        return b > 30000 ? 30000 : b < -30000 ? -30000 : b;
    }

    // ------------------------------------------------------------------------------------------------
    // Placements.

    IEnumerable<AdvisorPlacement> Census()
    {
        if (Placements != null) return Placements();
        return DefaultPlacements(Sim);
    }

    /// <summary>The default census (see <see cref="Placements"/>).</summary>
    public static IEnumerable<AdvisorPlacement> DefaultPlacements(ParkSim sim)
    {
        if (sim == null) yield break;
        foreach (var r in sim.Rides)
        {
            var e = r.Definition?.CompiledEntry;
            if (e != null)
                yield return new AdvisorPlacement(e.Kind, r.DestinationState,
                    e.Kind == AssetResourceDatabase.AssetKind.Feature ? e.RawFeatureFlags.GetValueOrDefault() : (byte)0, r);
            else if (r.ProvidesRelief)
                yield return new AdvisorPlacement(AssetResourceDatabase.AssetKind.Feature, r.DestinationState, 1, r);
        }
    }

    static int PoolBit(AssetResourceDatabase.AssetKind k) => k switch
    {
        AssetResourceDatabase.AssetKind.Ride => 1,                        // 0x39528C ordinary
        AssetResourceDatabase.AssetKind.TourRide => 2,                    // 0x395290 tour
        AssetResourceDatabase.AssetKind.TrackRide => 4,                   // 0x395294 track
        AssetResourceDatabase.AssetKind.Coaster => 8,                     // 0x395298 coasters
        AssetResourceDatabase.AssetKind.Shop => 0x100,                    // 0x39529C
        AssetResourceDatabase.AssetKind.Sideshow => 0x200,                // 0x3952A4
        _ => 0,
    };

    /// <summary>⭐ `0x103970(mask)` for bits 1/2/4/8 (the four ride pools), 0x100 shops, 0x200 sideshows --
    /// each pool's active count, whatever the status -- plus, for 0xF0, every feature with status `+0xA2` != 0
    /// whose class (<see cref="AdvisorPlacement.FeatureClass"/>) is in the mask. s16 arithmetic.</summary>
    public int PoolCount(int mask)
    {
        short n = 0;
        foreach (var p in Census())
        {
            if (p.Kind == AssetResourceDatabase.AssetKind.Feature)
            {
                if ((mask & 0xF0) == 0 || p.Status == 0) continue;
                if ((mask & p.FeatureClass) != 0) n++;
            }
            else if ((mask & PoolBit(p.Kind)) != 0) n++;
        }
        return n;
    }

    /// <summary>⭐ `0x103718(0x3FF)`, the park size, capped at 30000 (unsigned compare of the s16 result):
    /// `6·(ordinary + tour + track + coaster) + 4·shops + 4·sideshows + 5·every feature` -- the features
    /// with NO status test, the pools' raw counts.</summary>
    public int ParkSize()
    {
        short s = 0;
        foreach (var p in Census())
        {
            switch (PoolBit(p.Kind))
            {
                case 1: case 2: case 4: case 8: s += 6; break;
                case 0x100: case 0x200: s += 4; break;
                default: if (p.Kind == AssetResourceDatabase.AssetKind.Feature) s += 5; break;
            }
        }
        uint u = unchecked((uint)(int)s);
        return u < 30001u ? s : 30000;
    }

    /// <summary>⭐ `0x104760`: over EVERY toilet in the feature list (no status test) n and the s16 sum of
    /// cleanliness (`0x130938` = facility `+0xB4`, <see cref="ParkRide.Condition"/>); `n == 0 ? 0 :
    /// (100 − sum / n) &amp; 0xFFFF` -- the average dirtiness %. ⚠ A toilet the port keeps no facility for
    /// (a scriptless placement) reads as 100, the constructed value (`0x1302D8`).</summary>
    public int ToiletDirtiness()
    {
        short sum = 0; int n = 0;
        foreach (var p in Census())
        {
            if (!p.IsToilet) continue;
            n++;
            sum = unchecked((short)(sum + (p.Ride?.Condition ?? 100)));
        }
        return n == 0 ? 0 : (100 - sum / n) & 0xFFFF;
    }
}
