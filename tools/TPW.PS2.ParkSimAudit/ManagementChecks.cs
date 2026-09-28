using System.Buffers.Binary;
using System.Text;
using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

/// <summary>⭐⭐ STAFF STEP 5, MANAGEMENT (findings/staff-management.md; ghidra_tpw/notes/SPEC-staff-management.md):
/// wages (pro rata, striking, firing, WAGES_HIGH), the strike thresholds and ladder, training, research
/// progress and completion, the patrol tool's off-by-one, kick-out, the advisor producers and the
/// Security Award -- over the real disc and a real park, driven through the paths the viewer uses:
/// <see cref="ParkManagement.Advance"/> for the calendar, <see cref="ParkStaff.Update"/> for the staff,
/// <see cref="StaffPatrolTool"/> for the tool.
///
/// Every native number is re-read from the EXECUTABLE at its address. Every check names the rule it
/// guards; tools/management_teeth.py mutates each rule and requires this family to go red.</summary>
static class ManagementChecks
{
    sealed class TestRandom
    {
        readonly Random _rng;
        public Func<int, int?> Override;
        public readonly Dictionary<int, int> Draws = new();
        public TestRandom(int seed) => _rng = new Random(seed);
        public int Next(int n)
        {
            Draws[n] = Draws.GetValueOrDefault(n) + 1;
            if (Override?.Invoke(n) is int forced) return forced;
            return n <= 0 ? 0 : _rng.Next(n);
        }
    }

    sealed record Asset(string Stem, AssetResourceDatabase.Entry Record, byte[] Script, byte[] Aps, Func<string, byte[]> Sibling,
                        Func<RideDefinition> Definition);

    sealed class Park
    {
        public ParkPaths Paths; public ParkSim Sim; public GuestWalk Walk; public ParkVisitors Visitors;
        public ParkStaff Staff; public ParkClock Clock; public TestRandom Rng; public ParkManagement Mgmt; public ParkAwards Awards;
        public ParkCell B; public int NextId = 100;
        public readonly List<int> Messages = new(), Sounds = new();
        public readonly List<StaffFeature> Extra = new();
        public ParkCell At(int dx, int dz) => B.Offset(dx, dz);
        public void Tick(int n = 1) { for (int i = 0; i < n; i++) Staff.Update(); }
        public void Days(int n) { for (int i = 0; i < n; i++) Mgmt.Advance(ParkClock.UnitsPerDay); }
        /// <summary>Advance the calendar to the next month change (inclusive).</summary>
        public void ToNextMonth() { int m = Mgmt.MonthChanges; while (Mgmt.MonthChanges == m) Mgmt.Advance(ParkClock.UnitsPerDay); }
        public int Balance => Sim.Finances.Balance;
    }

    public static void Run(Disc disc, Model terrain, WadArchive data, WadArchive world, string worldName,
                           Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "management: " + label);
        var exe = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        byte[] elf = disc.Read(exe.Extent, exe.Size);

        Tables(elf, Check);
        Messages(elf, data, Check);

        // ---- the park -------------------------------------------------------------------------
        var compiled = new CompiledAssets(new AssetResourceDatabase(data.Read(data.Find("/arsdb.dba"))),
                                          TextDatabase.Load(data, "eur"));
        Asset Find(Func<AssetResourceDatabase.Entry, bool> want, bool scripted, string what)
        {
            foreach (var e in world.Entries.Where(e => e.Path.EndsWith(".sam", StringComparison.OrdinalIgnoreCase))
                                           .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
            {
                var rec = compiled.For(worldName, e.Path);
                if (rec?.Kind != AssetResourceDatabase.AssetKind.Feature || !want(rec)) continue;
                string stem = e.Path[..^4], folder = stem[..(stem.LastIndexOf('/') + 1)];
                var rse = world.Find(stem + ".rse");
                if (scripted && rse == null) continue;
                var aps = world.Find(stem + ".aps");
                string sam = Encoding.ASCII.GetString(world.Read(e));
                RideDefinition Definition()
                {
                    var def = RideDefinition.Parse(sam, "/DATA/" + worldName + ".WAD" + e.Path);
                    compiled.Attach(new[] { def }, out _);
                    return def;
                }
                return new Asset(stem, rec, rse == null ? null : world.Read(rse), aps == null ? null : world.Read(aps),
                                 n => world.Find(folder + n) is { } s ? world.Read(s) : null, Definition);
            }
            throw new InvalidDataException($"{worldName}: no {what} feature");
        }
        var roomAsset = Find(r => (r.RawFeatureFlags & 2) != 0 && r.ConnectionA.IsPresent && r.ConnectionA.Direction == 0
                                  && r.Width <= 2 && r.Depth <= 2, scripted: true, "staff room");
        var cameraAsset = Find(r => (r.RawFeatureFlags & 8) != 0, scripted: false, "camera");
        byte cameraFlags = cameraAsset.Record.RawFeatureFlags.GetValueOrDefault();
        Check(cameraFlags == 8 && (roomAsset.Record.RawFeatureFlags & 2) != 0,
              $"{worldName} fixtures: staff room {roomAsset.Stem} (key {roomAsset.Record.Key}), camera {cameraAsset.Stem} (key {cameraAsset.Record.Key}, DBA +0x2E = 0x{cameraFlags:x2})");

        var paths = new ParkPaths(terrain);
        var entranceTable = ParkEntrance.ReadExecutable(elf);
        paths.SetEntrance(entranceTable);
        int pathMaterial = Enumerable.Range(1, paths.Materials.Count - 1).First(i => ParkPaths.Classify(paths.Materials[i]) == ParkPathKind.Path);
        if (paths.EntranceEntry is { } entry)
            foreach (var (x, z) in entry.StartingPath())
                if (paths.CanLay(new ParkCell(x, z)) && paths.Kind(new ParkCell(x, z)) == ParkPathKind.None) paths.Lay(new ParkCell(x, z), pathMaterial);
        ParkCell? origin = null;
        for (int z = 0; z + 11 <= paths.Field.Height && origin == null; z++)
            for (int x = 0; x + 32 <= paths.Field.Width && origin == null; x++)
            {
                bool clear = true;
                for (int dz = 0; dz < 11 && clear; dz++)
                    for (int dx = 0; dx < 32 && clear; dx++)
                    {
                        var c = new ParkCell(x + dx, z + dz);
                        clear = paths.CanBuild(c) && paths.Kind(c) == ParkPathKind.None && !paths.IsEntrance(c);
                    }
                if (clear) origin = new ParkCell(x, z);
            }
        Check(origin != null, $"{worldName}: a clear 32x11 block for the fixture ({origin})");
        if (origin == null) return;
        var B = origin.Value.Offset(1, 1);
        for (int x = 0; x < 30; x++) paths.Lay(B.Offset(x, 2), pathMaterial);
        paths.Lay(B.Offset(10, 3), pathMaterial);

        Park NewPark(int seed = 1, ParkAwards awards = null)
        {
            var sim = new ParkSim(paths);
            var walk = new GuestWalk(paths);
            var rng = new TestRandom(seed);
            var p = new Park { Paths = paths, Sim = sim, Walk = walk, Rng = rng, B = B, Clock = new ParkClock(), Awards = awards ?? new ParkAwards() };
            p.Visitors = new ParkVisitors(sim, walk, () => 0);
            p.Staff = new ParkStaff(p.Visitors, p.Clock, new NativeActivationSequence(1000, "management checks"), rng.Next);
            var baseFeatures = p.Staff.Features;
            p.Staff.Features = () => baseFeatures().Concat(p.Extra);
            p.Staff.Advisor = p.Messages.Add;
            p.Staff.UiSound = p.Sounds.Add;
            p.Mgmt = new ParkManagement(p.Clock, p.Awards) { Finances = sim.Finances, Staff = p.Staff, Advisor = p.Messages.Add, UiSound = p.Sounds.Add };
            return p;
        }
        ParkRide Place(Park p, Asset asset, ParkCell at)
        {
            var a = asset.Record.ConnectionA;
            ParkCell? stub = a.IsPresent ? at.Offset(a.X, a.Z - 1) : null;
            var ride = p.Sim.Add(p.NextId++, asset.Stem, at, asset.Record.Width, asset.Record.Depth, asset.Script,
                                 asset.Aps == null ? null : new Animation(asset.Aps), 1, stub, stub, out var fault,
                                 sibling: asset.Sibling, definition: asset.Definition(), placementTurns: 0);
            return ride ?? throw new InvalidOperationException($"{asset.Stem} would not start: {fault}");
        }
        StaffMember HireAt(Park p, StaffKind kind, ParkCell cell, int? level = null)
        {
            var slot = p.Staff.Candidates.Available(kind).First().Slot;
            var m = p.Staff.Hire(kind, slot);
            if (level is int l) m.Level = l;
            if (!p.Staff.Drop(m, cell)) throw new InvalidOperationException($"drop refused at {cell}");
            return m;
        }

        Calendar(NewPark, HireAt, Check);
        Wages(NewPark, HireAt, Check);
        Takings(Check);
        Firing(NewPark, HireAt, Check);
        StrikeTest(NewPark, HireAt, Check);
        Ladder(NewPark, HireAt, Check);
        StrikeWalk(NewPark, HireAt, Check);
        Training(NewPark, HireAt, Check);
        Research(NewPark, HireAt, Check);
        Patrol(NewPark, HireAt, Check);
        KickOut(NewPark, Place, roomAsset, HireAt, Check);
        Ambience(NewPark, Place, roomAsset, Check);
        Coverage(NewPark, HireAt, Check);
        Award(NewPark, cameraFlags, Check);
        AdvisorRulesOnDisc(data, NewPark, Place, roomAsset, cameraFlags, HireAt, Check);
    }

    // =============================================================================================
    // The numbers, read back out of the executable.

    static void Tables(byte[] elf, Action<bool, string> Check)
    {
        uint U32(int off) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(off, 4));
        int U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(off, 2));
        int At(uint va)
        {
            int ph = checked((int)U32(28));
            for (int i = 0; i < U16(44); i++)
            {
                int p = ph + i * U16(42);
                if (U32(p) == 1 && va >= U32(p + 8) && va < U32(p + 8) + U32(p + 16)) return checked((int)(U32(p + 4) + va - U32(p + 8)));
            }
            throw new InvalidDataException($"0x{va:x} is not file-backed");
        }
        int[] Words(uint va, int n) => Enumerable.Range(0, n).Select(i => (int)U32(At(va + (uint)(i * 4)))).ToArray();
        int[] Bytes(uint va, int n) => Enumerable.Range(0, n).Select(i => (int)elf[At(va + (uint)i)]).ToArray();
        string S(IEnumerable<int> v) => string.Join(",", v);

        var bases = Bytes(StaffTables.StrikeMessageBaseTable, 5);
        Check(bases.SequenceEqual(StaffTables.StrikeMessageBase),
              $"tables: the strike message base 0x361D98 by type code ({S(bases)}) is the port's");
        var labels = Words(StaffTables.StaffRoomLabelTable, 5);
        Check(labels.SequenceEqual(StaffTables.TypeLabelRows), $"tables: the Staff Room labels 0x369790 ({S(labels)})");
        // The list-box action table 0x360F70: {text, fn} pairs; the kick-outs are 0x360FA0..0x360FC0.
        var kick = Enumerable.Range(0, 5).Select(i => Words(0x360FA0 + (uint)i * 8, 2)).ToArray();
        uint[] kickFns = { 0x124478, 0x1244F8, 0x124578, 0x1245F8, 0x124678 };
        bool kickOk = Enumerable.Range(0, 5).All(i => kick[i][0] == StaffTables.KickOutTextRow(StaffTables.KickOutOrder[i]) && (uint)kick[i][1] == kickFns[i]);
        Check(kickOk, $"tables: the Kick Out entries 0x360FA0.. ({S(kick.Select(k => k[0]))}) run Mechanics, Researchers, Cleaners, Entertainers, Guards");
        var opts = Enumerable.Range(0, 12).Select(i => Words(0x360F70 + (uint)i * 8, 1)[0]).ToArray();
        Check(opts[2] == StaffTables.SetPatrolAreaTextRow && opts[4] == StaffTables.FireTextRow && opts[5] == StaffTables.ZoomToTextRow
              && opts[11] == StaffTables.TrainingTextRow && opts[3] == 971,
              $"tables: the list-box actions 0x360F70 carry Set Patrol Area 79, Grab 971, Fire 906, Zoom To 391, Training 930 ({S(opts)})");
        var levelRows = Words(0x36BA88, 6);
        Check(levelRows.Take(5).SequenceEqual(StaffTables.TrainingLevelTextRows) && levelRows[5] == 0,
              $"tables: the Training screen's level rows 0x36BA88 ({S(levelRows)})");
        var research = Words(StaffTables.ResearcherWorkTable, 5);
        var jump = Words(0x3661F0, 8).Select(w => (uint)w).ToArray();
        // cat-1 → handler: 0x1B7538 ride/addon test, 0x1B75BC 0x4F, 0x1B7594 0x4D, 0x1B75A8 0x4E, 0x1B75D0 0x4C
        uint[] handler = { 0x1B7538, 0x1B75BC, 0x1B7538, 0x1B7594, 0x1B75A8, 0x1B7538, 0x1B7538, 0x1B75D0 };
        Check(research.SequenceEqual(StaffTables.ResearcherWork) && jump.SequenceEqual(handler),
              $"tables: researcher work 0x366150 ({S(research)}) and the completion jump table 0x3661F0 (by category 1..8)");
        // Code constants, read at their instructions.
        int Imm(uint va) => (short)U16(At(va));
        Check(Imm(0x1B60B8) == StaffTables.ResearchChanceOutOf && Imm(0x1B60EC) == StaffTables.ResearchChanceBelow,
              $"code: the researcher draws rand({Imm(0x1B60B8)}) and researches below {Imm(0x1B60EC)} (0x1B60B8, 0x1B60EC)");
        Check(Imm(0x1B6224) == -0x50 && Imm(0x1B6218) == 3 && Imm(0x1B6240) == 0x65,
              "code: the quantum's tiredness is (budget - 0x50) divu 3, capped below 0x65 (0x1B6224, 0x1B6218, 0x1B6240)");
        Check(Imm(0x142CF0) == 0x1C && Imm(0x142D3C) == 0x1C && Imm(0x142D7C) == 10 && Imm(0x142D98) == 0x15D
              && StaffTables.TimeEmployed(28) == (1, 0, 10) && StaffTables.TimeEmployed(42) == (1, 5, 349) && StaffTables.TimeEmployed(3) == (0, 1, 349),
              "code: Time Employed divides by 0x1C and picks row 10 at exactly 28 days, else 0x15D (0x142CE0)");
    }

    /// <summary>The ladder's messages in the ELF's own catalogue: only UNHAPPY and HAPPIER carry text and a
    /// voice, for all five types; VERY_UNHAPPY, STRIKING, STRIKE_END_BAD are row 310 with no voice.</summary>
    static void Messages(byte[] elf, WadArchive data, Action<bool, string> Check)
    {
        var cat = new AdvisorCatalogue(elf);
        int loud = 0, silent = 0;
        foreach (StaffKind k in Enum.GetValues<StaffKind>())
        {
            foreach (int off in new[] { StaffTables.MessageUnhappy, StaffTables.MessageHappier })
            {
                var m = cat.Messages[StaffTables.StrikeMessage(k, off)];
                if (m.HasText && m.Voices.Any(v => v.SoundId != 0)) loud++;
            }
            foreach (int off in new[] { StaffTables.MessageVeryUnhappy, StaffTables.MessageStriking, StaffTables.MessageStrikeEndBad })
            {
                var m = cat.Messages[StaffTables.StrikeMessage(k, off)];
                if (!m.HasText && m.Voices.All(v => v.SoundId == 0)) silent++;
            }
        }
        Check(loud == 10 && silent == 15, $"messages: UNHAPPY/HAPPIER have text and voice ({loud} of 10), VERY_UNHAPPY/STRIKING/STRIKE_END_BAD are silent ({silent} of 15)");
        var keys = new[] { 0x16, 0x17, 0x18, 0x19, 0x1A }.Select(i => cat.Messages[i].SymbolicKey).ToArray();
        Check(keys[StaffTables.StrikeMessage(StaffKind.Entertainer, 0x16) - 0x16].EndsWith("ENTERTAINERS")
              && keys[StaffTables.StrikeMessage(StaffKind.Handyman, 0x16) - 0x16].EndsWith("HANDYMEN")
              && keys[StaffTables.StrikeMessage(StaffKind.Researcher, 0x16) - 0x16].EndsWith("RESEARCHERS"),
              $"messages: the base table names the right type ({string.Join(" ", keys)})");
        var award = cat.Messages[StaffTables.SecurityAwardMessage];
        var wages = cat.Messages[StaffTables.WagesHighMessage];
        Check(award.SymbolicKey == "STR_ADVMES_GOLD_TICKET_BROTHER" && wages.SymbolicKey == "STR_ADVMES_ADD_WAGES_HIGH"
              && cat.Messages[0x4F].SymbolicKey.Contains("FEATURE_RESEARCHED") && cat.Messages[0x7E].SymbolicKey.Contains("NEW_UPGRADE_HIRE_MECHANIC"),
              "messages: 0xA0 is the Security Award, 0x52 WAGES_HIGH, 0x4F feature researched, 0x7E new upgrade hire a mechanic");
    }

    // =============================================================================================
    // The calendar (0x16B060).

    static void Calendar(Func<int, ParkAwards, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        var p = newPark(1, null);
        var days = new List<int>();
        int weekly = 0;
        for (int d = 0; d < 365; d++)
        {
            int before = p.Mgmt.WeeklyPasses;
            p.Mgmt.Advance(ParkClock.UnitsPerDay);
            if (p.Mgmt.WeeklyPasses != before) { weekly++; if (days.Count < 8) days.Add(p.Clock.DayOfMonth); }
        }
        Check(weekly == 59 && days.Take(6).SequenceEqual(new[] { 8, 15, 22, 29, 1, 8 }) && p.Mgmt.MonthChanges == 12
              && p.Sim.Finances.PeriodCount == 12,
              $"calendar: a year runs 12 month ends (period {p.Sim.Finances.PeriodCount}) and {weekly} weekly passes on days {string.Join(",", days)}... (1, 8, 15, 22, 29: 11x5 + February's 4 = 59)");
        // A frame that is not a whole day advances nothing and runs nothing.
        var q = newPark(1, null);
        q.Mgmt.Advance(ParkClock.UnitsPerDay - 1);
        Check(q.Clock.TotalDays == 0 && q.Mgmt.WeeklyPasses == 0 && q.Mgmt.MonthChanges == 0,
              "calendar: 0xEFFFF units is not a day (the accumulator must pass 0xEFFFF)");
    }

    // =============================================================================================
    // Wages (§6).

    static void Wages(Func<int, ParkAwards, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        var p = newPark(2, null);
        var early = hireAt(p, StaffKind.Guard, p.At(2, 2), 1);           // Jan 1: day 0
        p.Days(15);                                                       // Jan 16
        var h = hireAt(p, StaffKind.Handyman, p.At(5, 2), 0);
        p.Days(9);                                                        // Jan 25
        var m = hireAt(p, StaffKind.Mechanic, p.At(8, 2), 4);
        int b0 = p.Balance;
        p.ToNextMonth();                                                  // Feb 1
        int handy = 50 * (16 * 100 / 31) / 100, mech = 300 * (7 * 100 / 31) / 100, guard = 110;
        int expect = (handy + mech + guard) * 10;
        Check(p.Clock.Month == 1 && p.Clock.Day == 0 && b0 - p.Balance == expect && p.Mgmt.LastWages == expect
              && p.Sim.Finances.WagesInPeriod(1) == 0 && p.Sim.Finances.PeriodCount == 1,
              $"wages: the first month is pro rata over January's 31 days -- handyman 16 days ({handy}), mechanic L4 7 days ({mech}), guard L1 full ({guard}): {Money.Format(b0 - p.Balance)} debited, {Money.Format(expect)} expected; ring read 0 (0x100F68: k=1 is not < 1 completed month)");
        int b1 = p.Balance;
        p.ToNextMonth();                                                  // Mar 1: the previous month is February (28)
        int full = (50 + 300 + 110) * 10;
        Check(b1 - p.Balance == full && p.Sim.Finances.WagesInPeriod(1) == full && p.Sim.Finances.WagesInPeriod(2) == 0,
              $"wages: the second month is whole for all three ({Money.Format(b1 - p.Balance)}); the ring reads it back ({p.Sim.Finances.WagesInPeriod(1)}) but never the FIRST month (slot 0: {p.Sim.Finances.WagesInPeriod(2)})");
        // Unlimited off and too little money: the debit refuses, but the month is still filed.
        p.Sim.Finances.Unlimited = false; p.Sim.Finances.Balance = 100;
        p.ToNextMonth();
        Check(p.Balance == 100 && p.Sim.Finances.WagesInPeriod(1) == full,
              $"wages: a park that may not overspend and cannot pay keeps its balance (0x100698 refuses) but files the wages ({p.Sim.Finances.WagesInPeriod(1)})");
        var fb = newPark(3, null);
        fb.Sim.Finances.FreeBuild = true;
        hireAt(fb, StaffKind.Guard, fb.At(3, 2), 0);
        int fb0 = fb.Balance;
        fb.ToNextMonth();
        Check(fb.Balance == fb0 && fb.Mgmt.LastWages == 1000 && fb.Sim.Finances.WageAccumulator == 1000,
              $"wages: a free-build park (DAT_002A60B8 / park index 2) computes the bill ({Money.Format(fb.Mgmt.LastWages)}) but 0x100698 returns before taking it");
        // WAGES_HIGH: wages above ALL income in each of the last two completed months.
        var w = newPark(3, null);
        hireAt(w, StaffKind.Mechanic, w.At(3, 2), 0);
        w.ToNextMonth(); w.ToNextMonth(); w.ToNextMonth();
        bool high = w.Sim.Finances.WagesHigh;
        w.Sim.Finances.Credit(4500);                                      // this month: income 450.0 = 3 x 150.0 wages
        w.ToNextMonth();
        bool afterIncome = w.Sim.Finances.WagesHigh;
        w.ToNextMonth(); w.ToNextMonth();
        bool again = w.Sim.Finances.WagesHigh;
        Check(high && !afterIncome && again,
              $"wages high: two months of wages over zero income sets it ({high}); one month with income >= wages clears it ({afterIncome}); two more months set it again ({again})");
        var e = newPark(3, null);
        hireAt(e, StaffKind.Handyman, e.At(3, 2), 0);
        var seen = new List<bool>();
        for (int i = 0; i < 3; i++) { e.ToNextMonth(); seen.Add(e.Sim.Finances.WagesHigh); }
        Check(seen.SequenceEqual(new[] { false, false, true }) && e.Sim.Finances.IncomeInPeriod(5) == 0 && e.Sim.Finances.WagesInPeriod(1) == 500,
              $"wages high: with no income at all it cannot fire before the THIRD month end ({string.Join(",", seen)}) -- 0x100F68 reads k only while k < completed months, so the first month is never read");
    }

    /// <summary>The takings rings the finance graphs read: gate `+0x53C` (`0x100D28`), shop `+0x77C` and
    /// sideshow `+0x9BC` (`0x1007D8` by kind), their totals, and the month end clearing the next slot.</summary>
    static void Takings(Action<bool, string> Check)
    {
        var f = new ParkFinances();
        f.MonthEnd(0);                                                   // close month 0: slot 0 is never read
        f.CreditAdmission(300);
        f.CreditByKind((int)AssetResourceDatabase.AssetKind.Shop, 70);
        f.CreditByKind((int)AssetResourceDatabase.AssetKind.Sideshow, 40);
        f.CreditByKind((int)AssetResourceDatabase.AssetKind.Ride, 999);  // files nothing (jump table: 12 kinds fall through)
        f.MonthEnd(0);
        Check(f.GateInPeriod(1) == 300 && f.ShopInPeriod(1) == 70 && f.SideshowInPeriod(1) == 40
              && f.IncomeInPeriod(1) == 300 + 70 + 40 + 999,
              $"takings: an admission files the gate ring ({f.GateInPeriod(1)}), kind 4 the shop ring ({f.ShopInPeriod(1)}), kind 5 the sideshow ring ({f.SideshowInPeriod(1)}), a ride nothing; all four still credit income ({f.IncomeInPeriod(1)})");
        Check(f.GateTotal == 300 && f.ShopTotal == 70 && f.SideshowTotal == 40,
              $"takings: the running totals +0x12C8/+0x12CC/+0x12C4 read {f.GateTotal}/{f.ShopTotal}/{f.SideshowTotal}");
        f.MonthEnd(0);                                                   // an empty month
        bool before = f.GateInPeriod(1) == 0 && f.GateInPeriod(2) == 300;
        // The clear is only visible on a REUSED slot: month 1's takings sit in slot 1, and slot 1 comes round
        // again as month 145. A fresh slot is 0 whether or not the month end clears it.
        while (f.PeriodCount < 146) f.MonthEnd(0);
        Check(before && f.GateInPeriod(1) == 0 && f.ShopInPeriod(1) == 0 && f.SideshowInPeriod(1) == 0,
              $"takings: the month end clears the next slot of each ring -- month 145 reuses month 1's slot and reads {f.GateInPeriod(1)}/{f.ShopInPeriod(1)}/{f.SideshowInPeriod(1)} (month 1 took 300/70/40)");
    }

    static void Firing(Func<int, ParkAwards, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        var p = newPark(4, null);
        var old = hireAt(p, StaffKind.Researcher, p.At(2, 2), 2);
        var fresh = hireAt(p, StaffKind.Handyman, p.At(4, 2), 0);
        p.ToNextMonth();                                                  // Feb 1
        var fresh2 = hireAt(p, StaffKind.Entertainer, p.At(6, 2), 0);
        int acc = p.Sim.Finances.WageAccumulator, ring = p.Sim.Finances.WagesInPeriod(1), b = p.Balance;
        p.Staff.Fire(old);                                                // day 1 of February: a whole month again
        int paidOld = b - p.Balance;
        b = p.Balance;
        p.Staff.Fire(fresh2);                                             // hired today: 0 days
        int paidFresh = b - p.Balance;
        Check(paidOld == 195 * 10 && paidFresh == 0 && p.Sim.Finances.WageAccumulator == acc + 1950 && p.Sim.Finances.WagesInPeriod(1) == ring,
              $"firing: 0x100C78 pays 0x1DC338 again -- a researcher L2 fired on the 1st costs a FULL month ({Money.Format(paidOld)}), one hired today {Money.Format(paidFresh)}; the accumulator +0x12D0 takes it, the month's ring does not");
        p.Days(14);                                                       // Feb 15: 14 of January's 31
        b = p.Balance;
        p.Staff.Fire(fresh);
        Check(b - p.Balance == 500,
              $"firing: a handyman employed 45 days fired mid-February is paid in full against January's 31 days ({Money.Format(b - p.Balance)})");
        var q = newPark(4, null);
        q.Days(10);
        var mid = hireAt(q, StaffKind.Guard, q.At(2, 2), 0);
        q.Days(5);
        int bq = q.Balance;
        q.Staff.Fire(mid);
        Check(bq - q.Balance == 100 * (5 * 100 / 31) / 100 * 10,
              $"firing: 5 days in January against December's 31 (the previous month of month 0 wraps to 11): {Money.Format(bq - q.Balance)}");
    }

    // =============================================================================================
    // Strikes (§9).

    static void StrikeTest(Func<int, ParkAwards, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        var p = newPark(5, null);
        var a = hireAt(p, StaffKind.Handyman, p.At(2, 2), 0);
        a.Tiredness = 100; a.Morale = 50;
        bool single = p.Staff.Unhappy(StaffKind.Handyman);
        var b = hireAt(p, StaffKind.Handyman, p.At(4, 2), 0);
        bool Case(int t1, int t2, int m1, int m2)
        { a.Tiredness = (sbyte)t1; b.Tiredness = (sbyte)t2; a.Morale = (sbyte)m1; b.Morale = (sbyte)m2; return p.Staff.Unhappy(StaffKind.Handyman); }
        bool e15 = Case(85, 85, 50, 50), e14 = Case(86, 85, 50, 50);
        bool m15 = Case(0, 0, 15, 15), m14 = Case(0, 0, 15, 14), m0 = Case(0, 0, 0, 29);
        Check(!single && !e15 && e14 && !m15 && m14 && m0,
              $"strike test: one employee never ({single}); energy avg 15 no ({e15}), (14+15)/2 = 14 yes ({e14}); morale avg 15 no ({m15}), 29/2 = 14 yes ({m14}, {m0})");
        p.Staff.ParkRunning = false;
        Case(100, 100, 0, 0);
        p.ToNextMonth();
        bool gated = p.Messages.Count == 0 && p.Staff.StrikeStage(StaffKind.Handyman) == 0;
        p.Staff.ParkRunning = true;
        p.ToNextMonth();
        Check(gated && p.Messages.SequenceEqual(new[] { 0x17 }) && p.Staff.StrikeStage(StaffKind.Handyman) == 1,
              $"strike test: gated by 0x151258 (no check while the park is not running); running, an unhappy pair posts UNHAPPY 0x17 ({string.Join(",", p.Messages.Select(x => $"0x{x:x}"))})");
    }

    static void Ladder(Func<int, ParkAwards, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        var p = newPark(6, null);
        var crew = Enumerable.Range(0, 2).Select(i => hireAt(p, StaffKind.Handyman, p.At(2 + 2 * i, 2), 0)).ToList();
        foreach (var m in crew) { m.Tiredness = 100; m.Morale = 50; }
        var seq = new List<string>();
        for (int month = 1; month <= 11; month++)
        {
            p.Messages.Clear();
            p.ToNextMonth();
            seq.Add($"{string.Join("+", p.Messages.Select(x => $"{x:x}"))}/{p.Staff.StrikeStage(StaffKind.Handyman)}{(p.Staff.IsStriking(StaffKind.Handyman) ? "S" : "")}");
        }
        string want = "17/1 1c/2S 2b/2 21/3S 2b/3 21/4S 2b/4 21/5S 2b/5 2b/0 17/1";
        Check(string.Join(" ", seq) == want,
              $"ladder: a persistent grievance, month by month (message/stage, S = striking): {string.Join(" ", seq)} -- the findings' M1..M11");
        // Recovery at stage 1 posts HAPPIER; at stage 2+ it is silent.
        var q = newPark(7, null);
        var qc = Enumerable.Range(0, 2).Select(i => hireAt(q, StaffKind.Guard, q.At(2 + 2 * i, 2), 0)).ToList();
        foreach (var m in qc) m.Tiredness = 100;
        q.ToNextMonth();
        foreach (var m in qc) m.Tiredness = 0;
        q.Messages.Clear();
        q.ToNextMonth();
        bool happier = q.Messages.SequenceEqual(new[] { 0x27 }) && q.Staff.StrikeStage(StaffKind.Guard) == 0;
        foreach (var m in qc) m.Tiredness = 100;
        q.ToNextMonth(); q.ToNextMonth(); q.ToNextMonth();                // 1, 2 (strike), clear
        foreach (var m in qc) m.Tiredness = 0;
        q.Messages.Clear();
        q.ToNextMonth();
        Check(happier && q.Messages.Count == 0 && q.Staff.StrikeStage(StaffKind.Guard) == 0 && !q.Staff.IsStriking(StaffKind.Guard),
              $"ladder: recovering at stage 1 posts HAPPIER 0x27 (guards); recovering from stage 2 is silent ({q.Messages.Count} messages)");
        // Every type, one month: the check's order ent, mech, guard, res, handy and the per-type base.
        var r = newPark(8, null);
        foreach (StaffKind k in Enum.GetValues<StaffKind>())
            for (int i = 0; i < 2; i++) hireAt(r, k, r.At(2 + 3 * (int)k + i, 2), 0).Tiredness = 100;
        r.ToNextMonth();
        Check(r.Messages.SequenceEqual(new[] { 0x19, 0x16, 0x18, 0x1A, 0x17 }),
              $"ladder: all five types unhappy post UNHAPPY in 0x16C120's order ent, mech, guard, res, handy ({string.Join(",", r.Messages.Select(x => $"0x{x:x}"))})");
    }

    /// <summary>The strike through the staff update and the wages in the same month changes: strikes
    /// first, so the month a strike starts is paid in full, and the month it covered is not.</summary>
    static void StrikeWalk(Func<int, ParkAwards, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        var p = newPark(9, null);
        if (p.Paths.EntranceEntry is not { } e) { Check(false, "strike walk: no entrance entry"); return; }
        var drop = new ParkCell(e.XCol + 1, e.ZEnd + e.PathRows);
        var crew = Enumerable.Range(0, 2).Select(_ => hireAt(p, StaffKind.Handyman, drop, 0)).ToList();
        foreach (var m in crew) { m.Tiredness = 100; m.Morale = 50; }
        p.ToNextMonth();                                                  // Feb 1: UNHAPPY
        int b = p.Balance;
        p.ToNextMonth();                                                  // Mar 1: strike ON, then wages
        int paidAtStart = b - p.Balance;
        bool striking = p.Staff.IsStriking(StaffKind.Handyman);
        int t = 0;
        for (; t < 8000 && crew.Any(m => m.State != StaffMember.StateStriking); t++) p.Tick();
        bool standing = crew.All(m => m.State == StaffMember.StateStriking);
        b = p.Balance;
        p.ToNextMonth();                                                  // Apr 1: strike cleared, then wages
        int paidStriking = b - p.Balance;
        Check(striking && paidAtStart == 1000 && standing && paidStriking == 0 && !p.Staff.IsStriking(StaffKind.Handyman),
              $"strike walk: the strike starts at a month change and that month is paid in full ({Money.Format(paidAtStart)}); both walk out and stand in 0xF ({t} updates); at the next change the flag clears FIRST and the wages find them still in 0xF: {Money.Format(paidStriking)}");
        p.Tick();
        Check(crew.All(m => m.State != StaffMember.StateStriking),
              "strike walk: with the flag clear they leave 0xF at their next update (0x1DB900)");
    }

    // =============================================================================================
    // Training (§7).

    static void Training(Func<int, ParkAwards, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        var p = newPark(10, null);
        var focused = new List<StaffMember>();
        p.Staff.Focus = focused.Add;
        int right = 0, cases = 0;
        var log = new List<string>();
        foreach (StaffKind k in Enum.GetValues<StaffKind>())
        {
            var m = hireAt(p, k, p.At(2 + 3 * (int)k, 2), 0);
            for (int l = 0; l < 4; l++)
            {
                cases++;
                m.Tiredness = 60; m.Morale = 20;
                int b = p.Balance; p.Sounds.Clear(); focused.Clear();
                bool offered = p.Staff.SingleStaffOptions(m).Contains(StaffTables.TrainingTextRow);
                var result = p.Staff.Train(m);
                int cost = new[] { 250, 275, 325, 400, 500 }[l] * new[] { 3, 1, 1, 2, 6 }[(int)k];
                bool ok = offered && result == TrainingResult.Trained && b - p.Balance == cost * 10 && m.Level == l + 1
                          && m.Tiredness == 0 && m.Morale == 100 && focused.SequenceEqual(new[] { m }) && p.Sounds.SequenceEqual(new[] { 0x12F });
                if (ok) right++; else log.Add($"{k} L{l}: {result} paid {b - p.Balance} level {m.Level}");
            }
            int bb = p.Balance;
            bool capped = p.Staff.Train(m) == TrainingResult.NotOffered && p.Balance == bb && m.Level == 4
                          && !p.Staff.SingleStaffOptions(m).Contains(StaffTables.TrainingTextRow);
            if (capped) right++; else log.Add($"{k} L4 not capped");
            cases++;
        }
        Check(right == cases, $"training: T[L] x N[kind] x 10 debited, level +1, tiredness 0, morale 100, the member focused, sound 0x12F -- and nothing at level 4 ({right} of {cases}) {string.Join("; ", log)}");
        // The screen's affordability test reads the raw balance (0x100688) even when the park may overspend.
        var q = newPark(11, null);
        var g = hireAt(q, StaffKind.Guard, q.At(3, 2), 1);
        q.Sim.Finances.Balance = 550 * 10 - 1;
        g.Tiredness = 40; g.Morale = 40;
        var refused = q.Staff.Train(g);
        bool untouched = g.Level == 1 && g.Tiredness == 40 && q.Balance == 5499 && q.Sounds.SequenceEqual(new[] { 0xAF });
        q.Sim.Finances.Balance = 5500;
        var bought = q.Staff.Train(g);
        Check(q.Sim.Finances.Unlimited && refused == TrainingResult.CannotAfford && untouched && bought == TrainingResult.Trained && q.Balance == 0 && g.Level == 2,
              $"training: balance 549.9 refuses a 550.0 course with 0xAF even though the park may overspend; exactly 550.0 buys it ({bought})");
        var f = newPark(12, null);
        f.Sim.Finances.FreeBuild = true;
        var r = hireAt(f, StaffKind.Researcher, f.At(3, 2), 0);
        int fb = f.Balance;
        Check(f.Staff.Train(r) == TrainingResult.Trained && f.Balance == fb && r.Level == 1 && r.TrainingCost(true) == 0,
              "training: free-build costs 0 (0x1DC2A8) and debits nothing (0x100698)");
        var opts = f.Staff.SingleStaffOptions(r);
        Check(opts.SequenceEqual(new[] { 79, 906, 930, 391 }) && r.WageAfterTraining == 195 && r.TrainingBar == 50 && r.TrainingLevelTextRow == 112,
              $"training: Single Staff offers 79, 906, 930, 391 in order (no Grab); the Training screen shows the next wage {r.WageAfterTraining}, bar {r.TrainingBar}, row {r.TrainingLevelTextRow}");
    }

    // =============================================================================================
    // Research (§10).

    static void Research(Func<int, ParkAwards, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        var p = newPark(13, null);
        p.Rng.Override = n => n == 10 ? 0 : null;                          // always research
        var r = (Researcher)hireAt(p, StaffKind.Researcher, p.At(5, 2), 0);
        var mgr = p.Staff.Research;
        Check(mgr.Budget == 80 && mgr.ActiveCount == 0, $"research: the manager starts at budget {mgr.Budget}, no project");
        mgr.Start(0, 2, 7, 100000);
        var deltas = new List<uint>();
        int right = 0;
        for (int l = 0; l <= 4; l++)
        {
            r.Level = l; r.Tiredness = 0;
            uint before = mgr.Slots[0].Progress;
            p.Tick(2);                                                    // find work → 0x1F, then the quantum
            uint d = mgr.Slots[0].Progress - before;
            deltas.Add(d);
            if (d == (uint)(StaffTables.ResearcherWork[l] * 80) * 4096 / 100 && r.Tiredness == 0) right++;
        }
        Check(right == 5, $"research: one quantum adds R[L] x 80 x 4096 / 100 at every level ({string.Join(",", deltas)}), tiredness +0 at budget 80");
        mgr.Start(1, 4, 3, 100000);
        r.Level = 3;
        uint a0 = mgr.Slots[0].Progress, a1 = mgr.Slots[1].Progress;
        p.Tick(2);
        uint half = (uint)(40 * 80) * 4096 / 200;
        Check(mgr.Slots[0].Progress - a0 == half && mgr.Slots[1].Progress - a1 == half,
              $"research: two active projects share it -- each gets R x budget x 4096 / (2 x 100) = {half}");
        mgr.OpenResearchScreen();
        r.Tiredness = 10;
        a0 = mgr.Slots[0].Progress;
        p.Tick(2);
        Check(mgr.Budget == 100 && mgr.Slots[0].Progress - a0 == (uint)(40 * 100) * 4096 / 200 && r.Tiredness == 16,
              $"research: opening the Research screen forces budget 100 (0x1B54CC): the share grows and each quantum costs 6 tiredness ({r.Tiredness - 10})");
        // Completion.
        var q = newPark(14, null);
        q.Rng.Override = n => n == 10 ? 0 : null;
        var qr = (Researcher)hireAt(q, StaffKind.Researcher, q.At(5, 2), 0);
        var qm = q.Staff.Research;
        var done = new List<ResearchProject>();
        qm.Researched = done.Add;
        qm.Start(2, 2, 9, 16);                                            // 16 << 12 = 65536 = exactly one L0 quantum at 80
        q.Tick(2);
        var s = qm.Slots[2];
        Check(done.Count == 1 && done[0] == s && s.Complete && !s.Active && s.Item == -1 && s.Percent >= 100 && qm.CompletedFlag
              && q.Messages.SequenceEqual(new[] { 0x4F }),
              $"research: progress == required completes (!(progress < required), 0x1B74A0): inactive, item -1, the unlock hook once, FEATURE_RESEARCHED 0x4F posted ({string.Join(",", q.Messages.Select(x => $"0x{x:x}"))})");
        bool restarted = qm.Start(2, 4, 1, 1000, 50);
        Check(restarted && qm.Slots[2].Active && !qm.Slots[2].Complete && qm.Slots[2].Progress == (1000u << 12) / 2 && qm.Slots[2].Percent == 50
              && !qm.Start(2, 4, 1, 1000),
              $"research: a project starts from the percent the database holds (0x1B7650: {qm.Slots[2].Progress} of {qm.Slots[2].Required}), and an active slot refuses a second start");
        qm.ItemLevel = (c, i) => i;                                       // ⚠ the research DB answer, supplied
        int NoMech() => qm.CompletionMessage(1, 5) ?? -1;
        int m1 = qm.CompletionMessage(1, 1) ?? -1, m2 = NoMech();
        hireAt(q, StaffKind.Mechanic, q.At(9, 2), 0);
        int m3 = qm.CompletionMessage(6, 5) ?? -1;
        int[] others = { qm.CompletionMessage(4, 0) ?? -1, qm.CompletionMessage(5, 0) ?? -1, qm.CompletionMessage(8, 0) ?? -1, qm.CompletionMessage(9, 0) ?? -1 };
        Check(m1 == 0x4B && m2 == 0x7E && m3 == 0x4C && others.SequenceEqual(new[] { 0x4D, 0x4E, 0x4C, (int)AdvisorRequest.UnsetId }),
              $"research: category 1/3/6/7 posts RIDE 0x4B below level 2, else ADDON 0x4C -- or 0x7E with no mechanic; 4 shop 0x4D, 5 sideshow 0x4E, 8 addon 0x4C, others the unset id 0x114 ({m1:x},{m2:x},{m3:x},{string.Join(",", others.Select(o => o.ToString("x")))})");
        // The rand(10) is drawn before the tired check.
        var t = newPark(15, null);
        var tr = hireAt(t, StaffKind.Researcher, t.At(5, 2), 0);
        tr.Tiredness = 90;
        int draws = t.Rng.Draws.GetValueOrDefault(10);
        t.Tick();
        Check(t.Rng.Draws.GetValueOrDefault(10) == draws + 1 && tr.State == StaffMember.StateGoRest,
              $"research: a tired researcher still draws rand(10) before going to rest ({t.Rng.Draws.GetValueOrDefault(10) - draws} draw, state 0x{tr.State:x2})");
        var u = newPark(16, null);
        u.Rng.Override = n => n == 10 ? 3 : null;
        var ur = hireAt(u, StaffKind.Researcher, u.At(5, 2), 0);
        ur.Tiredness = 0;
        u.Tick();
        Check(ur.State != Researcher.StateResearching && u.Staff.Research.Quanta == 0,
              "research: rand(10) = 3 is not < 3 -- he patrols (sound 0xA8) instead");
    }

    // =============================================================================================
    // The patrol-area tool (§4).

    static void Patrol(Func<int, ParkAwards, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        var p = newPark(17, null);
        var g = hireAt(p, StaffKind.Guard, p.At(10, 2), 0);
        (int, int, int, int) Walked(StaffMember m) => (m.PatrolX0, m.PatrolX1 - 1, m.PatrolZ0, m.PatrolZ1 - 1);
        (bool Held, (int, int, int, int) Light, (int, int, int, int) Walk) Draw(ParkCell c, ParkCell u)
        {
            var tool = new StaffPatrolTool(p.Staff, g);
            tool.MoveCursor(c);
            bool held = g.Held;
            tool.Press();
            tool.MoveCursor(u);
            var (hx, hz, w, d) = tool.Highlight;
            bool set = tool.Press();
            return (held && set && !g.Held && !tool.Open, (hx, hx + w - 1, hz, hz + d - 1), Walked(g));
        }
        var large = Draw(p.At(12, 6), p.At(4, 1));
        var small = Draw(p.At(4, 1), p.At(12, 6));
        Check(large.Held && large.Light == large.Walk,
              $"patrol tool: first corner the LARGER -- highlighted x/z {large.Light} and walked {large.Walk} agree");
        Check(small.Held && small.Walk == (p.At(5, 0).X, p.At(11, 0).X, p.At(0, 2).Z, p.At(0, 5).Z) && small.Light == large.Light,
              $"patrol tool: first corner the SMALLER -- highlighted {small.Light}, walked {small.Walk}: BOTH edges lost (the corner's +1)");
        var adj = Draw(p.At(6, 2), p.At(7, 3));
        g.Tiredness = 0;
        int draws = p.Rng.Draws.Values.Sum();
        byte before = g.State;
        Check(adj.Held && g.HasPatrolArea && g.PatrolX0 == g.PatrolX1 && g.PatrolZ0 == g.PatrolZ1 && p.Staff.NoPatrolAreaPercent(4) == 0,
              $"patrol tool: cursor == corner + 1 stores an EMPTY range ({g.PatrolX0}..{g.PatrolX1}) that still counts as set (no-area % {p.Staff.NoPatrolAreaPercent(4)})");
        // The tool's sounds and its Triangle.
        p.Sounds.Clear();
        var t = new StaffPatrolTool(p.Staff, g);
        t.MoveCursor(p.At(3, 2)); t.Press(); t.MoveCursor(p.At(5, 2));
        bool stays = !t.Cancel() && t.Open && !t.CornerPlaced && !g.Held;
        t.MoveCursor(p.At(8, 2));
        bool reheld = g.Held && t.Corner == p.At(8, 2);
        bool leaves = t.Cancel() && !t.Open && !g.Held;
        var t2 = new StaffPatrolTool(p.Staff, g);
        t2.MoveCursor(p.At(3, 2)); t2.Press(); t2.MoveCursor(p.At(9, 4)); t2.Press();
        Check(stays && reheld && leaves && p.Sounds.SequenceEqual(new[] { 0xDB, 0xDB, 0x1F, 0xDB }),
              $"patrol tool: Triangle with a corner placed forgets it and stays, else leaves; the cursor holds the member (0x40) and a press unholds; sounds {string.Join(",", p.Sounds.Select(x => $"0x{x:x}"))} (0xDB each press, 0x1F on the set)");
    }

    // =============================================================================================
    // Kick-out (§8.3).

    static void KickOut(Func<int, ParkAwards, Park> newPark, Func<Park, Asset, ParkCell, ParkRide> place, Asset roomAsset,
                        Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        var p = newPark(18, null);
        var room = place(p, roomAsset, p.At(2, 3));
        var crew = new[] { hireAt(p, StaffKind.Handyman, p.At(20, 2), 0), hireAt(p, StaffKind.Handyman, p.At(24, 2), 0) };
        foreach (var m in crew) { m.Tiredness = 100; m.Morale = 50; }
        int t = 0;
        for (; t < 4000 && crew.Any(m => m.State != StaffMember.StateResting); t++) p.Tick();
        bool resting = crew.All(m => m.State == StaffMember.StateResting && ReferenceEquals(m.Target, room) && !m.Shown);
        var counts = p.Staff.StaffRoomCounts(room);
        var options = p.Staff.KickOutOptions(room);
        Check(resting && counts.SequenceEqual(new[] { 0, 0, 0, 0, 2 }) && options.SequenceEqual(new[] { StaffKind.Handyman })
              && p.Staff.StaffRoomCounts(new object()).Sum() == 0,
              $"kick-out: two handymen resting in the room ({t} updates): the Staff Room counts {string.Join(",", counts)} (ent, mech, guard, res, CLEANERS) and offers Kick Out Cleaners only");
        foreach (var m in crew) m.Tiredness = 95;
        int n = p.Staff.KickOut(room, StaffKind.Guard);
        int k = p.Staff.KickOut(room, StaffKind.Handyman);
        bool out1 = n == 0 && k == 2 && crew.All(m => m.State == StaffMember.StatePatrol && m.Shown && m.Target == null && m.Tiredness == 95);
        Check(out1 && p.Staff.KickOutOptions(room).Count == 0,
              "kick-out: Kick Out Cleaners ends both rests (0x1DBFD0: shown, target 0, state 0xD) and leaves tiredness 95 untouched");
        bool back = false;
        for (int i = 0; i < 3000 && !back; i++)
        {
            p.Tick();
            back = crew.All(m => m.State is StaffMember.StateGoRest or StaffMember.StateWaitForRoute or StaffMember.StateWalk or StaffMember.StateSegmentEnd or StaffMember.StateResting
                                 && ReferenceEquals(m.Target, room));
        }
        Check(back, "kick-out: still >= 81, they head straight back to the room at their next find work (INFERRED consequence, reproduced)");
    }

    static void Ambience(Func<int, ParkAwards, Park> newPark, Func<Park, Asset, ParkCell, ParkRide> place, Asset roomAsset, Action<bool, string> Check)
    {
        var p = newPark(19, null);
        var events = new List<(object, bool)>();
        p.Staff.StaffRoomAmbience = (f, on) => events.Add((f.Key, on));
        var room = place(p, roomAsset, p.At(2, 3));
        p.Tick(3);
        bool quietShut = events.Count == 0;
        p.Sim.SetOpen(room.Id, true);
        p.Tick(3);
        bool started = events.SequenceEqual(new[] { ((object)room, true) });
        p.Sim.SetOpen(room.Id, false);
        p.Tick(2);
        bool noStop = events.Count == 1;
        p.Sim.Remove(room.Id);
        p.Tick();
        Check(quietShut && started && noStop && events.Count == 2 && events[1] == (room, false),
              $"ambience: a staff room at status 1 is silent; turning 2 starts 0xBC once; closing does not stop it; removal does ({events.Count} events)");
    }

    // =============================================================================================
    // Coverage, the producers and the Security Award (§11).

    static void Coverage(Func<int, ParkAwards, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        var p = newPark(20, null);
        var g = hireAt(p, StaffKind.Guard, p.At(0, 2), 0);
        int w4 = p.Staff.Tiles.Width >> 2, h4 = p.Staff.Tiles.Height >> 2;
        // An independent count: path blocks sampled from ParkPaths (not the tile view) at the four points.
        bool PathBlock(int c, int r) => new[] { (0, 0), (0, 2), (2, 0), (2, 2) }
            .Any(o => new ParkCell(4 * c + o.Item1, 4 * r + o.Item2) is var cell
                      && p.Paths.Kind(cell) == ParkPathKind.Path && p.Paths.EntranceKind(cell) == null);
        int paths = 0;
        for (int r = 0; r < h4; r++) for (int c = 0; c < w4; c++) if (PathBlock(c, r)) paths++;
        int none = p.Staff.PatrolCoverage(4);
        g.SetPatrolArea(new ParkCell(0, 0), new ParkCell(Math.Min(127, p.Staff.Tiles.Width - 1), Math.Min(127, p.Staff.Tiles.Height - 1)));
        int all = p.Staff.PatrolCoverage(4);
        var a = p.At(0, 0); var b = p.At(13, 4);
        g.SetPatrolArea(a, b);
        int covered = 0;
        for (int r = a.Z >> 2; r <= b.Z >> 2; r++) for (int c = a.X >> 2; c <= b.X >> 2; c++) if (PathBlock(c, r)) covered++;
        int expect = covered == 0 ? 0 : paths <= covered ? 100 : covered * 100 / paths;
        int got = p.Staff.PatrolCoverage(4);
        Check(none == 0 && all == 100 && got == expect && expect > 0 && expect < 100 && p.Staff.PatrolCoverage(1) == 0,
              $"coverage: 0x104FB0 over {paths} path blocks of 4x4 -- no area 0, the whole park 100, a guard over x {a.X}..{b.X} z {a.Z}..{b.Z} covers {covered}: {got}% (independent count {expect}%); no mechanics 0");
        // The producers the rules read.
        var h = hireAt(p, StaffKind.Handyman, p.At(20, 2), 1);
        var h2 = hireAt(p, StaffKind.Handyman, p.At(22, 2), 3);
        h.Tiredness = 37; h2.Tiredness = 12; g.Tiredness = 5;
        var held = p.Staff.Hire(StaffKind.Handyman, p.Staff.Candidates.Available(StaffKind.Handyman).First().Slot);
        var v = new short[79];
        p.Staff.FillAdvisorVariables(v);
        Check(v[10] == 2 && v[8] == 1 && v[11] == 3 && v[33 + 1] == 100 && v[33 + 2] == 0 && v[41 + 1] == (1 + 3 + held.Level) * 100 / 12
              && v[55] == Math.Max(37, (int)held.Tiredness) && v[39] == got,
              $"producers: v10 handymen {v[10]} (3 hired, one still on the hire tool), v11 {v[11]}, no-area handy {v[34]} guard {v[35]}, trained handy {v[42]}%, max tiredness {v[55]}, guard coverage {v[39]}");
        p.Staff.CancelHire(held);
    }

    static void Award(Func<int, ParkAwards, Park> newPark, byte cameraFlags, Action<bool, string> Check)
    {
        var probe = newPark(21, null);
        int bpr = (probe.Staff.Tiles.Width + 127) >> 7, rows = (probe.Staff.Tiles.Height + 15) >> 4, n = bpr * rows;
        int need = 80 * n / 100 + 1;                                      // popcount*100/n > 80
        List<ParkCell> Blocks(int k)
        {
            var list = new List<ParkCell>();
            for (int r = 0; r < rows && list.Count < k; r++)
                for (int c = 0; c < Math.Min(8, (probe.Staff.Tiles.Width + 15) >> 4) && list.Count < k; c++)
                    list.Add(new ParkCell(16 * c + 3, 16 * r + 3));
            return list;
        }
        (int Coverage, int Tickets, List<int> Msgs, int Day) Run(int cameras, byte status, bool twoPerBlock = false, ParkAwards awards = null)
        {
            var p = newPark(22, awards);
            foreach (var c in Blocks(cameras))
            {
                p.Extra.Add(new StaffFeature(c, c, cameraFlags, status, null, c));
                if (twoPerBlock) p.Extra.Add(new StaffFeature(c.Offset(5, 5), c.Offset(5, 5), cameraFlags, status, null, c.Offset(5, 5)));
            }
            int day = -1;
            for (int d = 0; d < 40; d++)
            {
                int t = p.Awards.GoldTickets;
                p.Mgmt.Advance(ParkClock.UnitsPerDay);
                if (p.Awards.GoldTickets != t && day < 0) day = p.Clock.TotalDays;
            }
            return (p.Staff.FeatureCoverage(0x40), p.Awards.GoldTickets, p.Messages.ToList(), day);
        }
        var win = Run(need, 1);
        var lose = Run(need - 1, 1);
        var doubled = Run(need - 1, 1, twoPerBlock: true);
        var closed = Run(need, 0);
        Check(win.Coverage > 80 && win.Tickets == 1 && win.Msgs.SequenceEqual(new[] { 0xA0 }) && win.Day == 7,
              $"award: cameras in {need} distinct 16x16 blocks of {n} bytes make 0x104CE0(0x40) = {win.Coverage} > 80 -- the first weekly pass (day 8) awards ONE gold ticket and posts 0xA0, never again");
        Check(lose.Coverage <= 80 && lose.Tickets == 0 && doubled.Coverage == lose.Coverage && doubled.Tickets == 0 && closed.Tickets == 0,
              $"award: {need - 1} blocks give {lose.Coverage} (not > 80) -- no award; two cameras in a block count once ({doubled.Coverage}); status 0 cameras count for nothing");
        var awards = new ParkAwards();
        var first = Run(need, 1, awards: awards);
        var second = Run(need, 1, awards: awards);
        Check(first.Tickets == 1 && second.Tickets == 1 && awards.HasHiddenAward(0) && awards.Medals[1] && awards.GoldTicketsEarned == 1,
              "award: the hidden-award bit lives in [0x3975E8], not the park -- a second park with the same awards wins nothing");
        var gated = newPark(23, null);
        gated.Mgmt.GoalsRecordPresent = () => false;
        foreach (var c in Blocks(need)) gated.Extra.Add(new StaffFeature(c, c, cameraFlags, 1, null, c));
        gated.Days(10);
        Check(gated.Awards.GoldTickets == 0 && gated.Mgmt.WeeklyPasses == 1, "award: no goals record (0x16C0E8) -- the weekly pass returns at once");
    }

    /// <summary>The producers fed to the REAL rules from the disc (headers.ass/opcodes.ass).</summary>
    static void AdvisorRulesOnDisc(WadArchive data, Func<int, ParkAwards, Park> newPark, Func<Park, Asset, ParkCell, ParkRide> place, Asset roomAsset,
                                   byte cameraFlags, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        byte[] Find(string suffix) => data.Read(data.Entries.First(x => x.Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)));
        var rules = new AdvisorRules(Find("headers.ass"), Find("opcodes.ass"));
        int RuleFor(int message) => Enumerable.Range(0, rules.Rules.Count)
            .First(i => rules.Rules[i].Instructions.Any(ins => ins.Code == AdvisorRules.Op.Message && ins.A == message));
        AdvisorRules.Evaluation Eval(Park p, int rule, int v4 = 30)
        {
            var v = new short[79]; var counters = new short[22];
            p.Staff.FillAdvisorVariables(v);
            v[4] = (short)v4; v[78] = 1000;
            return rules.Evaluate(rule, v, counters);
        }
        bool Posts(AdvisorRules.Evaluation e, int message) => e.Result == AdvisorRules.Result.Completed && e.Effects.Any(x => x.Code == AdvisorRules.Op.Message && x.MessageId == message);
        int wagesRule = RuleFor(0x52), roomRule = RuleFor(0x80), cameraRule = RuleFor(0x0C);
        var p = newPark(24, null);
        hireAt(p, StaffKind.Mechanic, p.At(3, 2), 0).Tiredness = 40;
        bool noWages = !Posts(Eval(p, wagesRule), 0x52);
        p.ToNextMonth(); p.ToNextMonth(); p.ToNextMonth();
        bool wages = Posts(Eval(p, wagesRule), 0x52);
        Check(wagesRule == 103 && noWages && wages, $"rules: rule {wagesRule} posts WAGES_HIGH 0x52 once wages beat income two months running (v49), not before");
        bool needRoom = Posts(Eval(p, roomRule), 0x80);
        place(p, roomAsset, p.At(2, 3));
        bool haveRoom = !Posts(Eval(p, roomRule), 0x80);
        Check(roomRule == 48 && needRoom && haveRoom,
              $"rules: rule {roomRule} posts NEED_STAFF_ROOM 0x80 with a staff member at tiredness 40 (v55 > 15) and no room (v19 = 0); a placed room silences it");
        var q = newPark(25, null);
        hireAt(q, StaffKind.Guard, q.At(3, 2), 0);
        q.Extra.Add(new StaffFeature(q.At(7, 5), q.At(7, 5), cameraFlags, 0, null, "cam0"));   // status 0: not counted
        bool noCamera = Posts(Eval(q, cameraRule), 0x0C);
        q.Extra.Add(new StaffFeature(q.At(5, 5), q.At(5, 5), cameraFlags, 1, null, "cam"));
        bool camera = !Posts(Eval(q, cameraRule), 0x0C);
        Check(cameraRule == 1 && noCamera && camera, $"rules: rule {cameraRule} posts BUILD_CAMERA 0x0C for a guard with no standing camera (v8; v20 skips status 0); a standing camera silences it");
    }
}
