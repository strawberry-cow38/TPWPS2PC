using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

/// <summary>⭐⭐ STAFF STEP 3, MECHANICS: ride wear, breakdown, repair and upgrades, run over the real
/// disc and this world's own rides (findings/staff-mechanics-guards.md §0-§4, §7-§9;
/// coaster-operation.md §2-§4; track-ride-operation.md §3, §6).
///
/// Every native constant is read back out of the EXECUTABLE (a table at its address, or the
/// instruction word that carries it); every behaviour runs through <see cref="ParkSim.Advance"/>,
/// <see cref="ParkVisitors.Step"/> or <see cref="ParkStaff.Update"/> -- the paths the viewer uses --
/// never by calling a handler directly; every "all of them" prints its count. The wear formula is
/// held to the findings' own worked tables, computed there independently of this port.
/// ⚠ Reflection is used in exactly one place (the Call Mechanic predicate), to put a mechanic into a
/// state the console can reach but a short fixture cannot; it is said where it is done.
/// Teeth: the mutation runner named in the commit turns each rule red.</summary>
static partial class MechanicChecks
{
    sealed class TestRandom
    {
        readonly Random _rng;
        public Func<int, int?> Override;
        public TestRandom(int seed) => _rng = new Random(seed);
        public int Next(int n)
        {
            if (Override?.Invoke(n) is int forced) return forced;
            return n <= 0 ? 0 : _rng.Next(n);
        }
    }

    sealed record Asset(string Path, string Stem, AssetResourceDatabase.Entry Record, byte[] Script, byte[] Aps,
                        Func<string, byte[]> Sibling, Func<RideDefinition> Definition, IReadOnlyList<string> Variables);

    /// <summary>The ordinary-ride fixture: a park factory and a placer for this world's running ride.</summary>
    sealed class Fixture
    {
        public Func<int, Park> NewPark;
        public Func<Park, ParkCell, ParkRide> Place;
        public Asset Ordinary;
        public ParkRide Open(Park p, ParkCell at)
        {
            var r = Place(p, at);
            p.Sim.SetOpen(r.Id, true); r.Set("VAR_BROKEN", 0);
            return r;
        }
    }

    static Mechanic Hire(Park p, int level = 0)
    {
        var slot = p.Staff.Candidates.Available(StaffKind.Mechanic).First().Slot;
        var m = (Mechanic)p.Staff.Hire(StaffKind.Mechanic, slot);
        m.Level = level; m.Tiredness = 0; m.Morale = 50;
        return m;
    }
    static void Drop(Park p, StaffMember m, ParkCell cell)
    {
        if (!p.Staff.Drop(m, cell)) throw new InvalidOperationException($"drop refused at {cell}");
    }
    static Mechanic HireAt(Park p, ParkCell cell, int level = 0) { var m = Hire(p, level); Drop(p, m, cell); return m; }
    static Point Centre(ParkCell c) => new((short)(c.X * 256 + 0x80), (short)(c.Z * 256 + 0x80));

    sealed class Park
    {
        public ParkSim Sim; public ParkVisitors Visitors; public ParkStaff Staff; public ParkClock Clock;
        public TestRandom Rng; public ParkCell B; public int NextId = 100;
        public readonly List<(int Id, ParkRide Ride)> Advisors = new();
        public readonly List<(ParkRide Ride, int Category, int Event, bool Positional)> Sounds = new();
        public readonly List<(StaffMember Member, int Bank, int Event, int Handle)> StaffSounds = new();
        public ParkCell At(int dx, int dz) => B.Offset(dx, dz);
        /// <summary>One park tick the way the viewer runs one: rides, then the attached staff.</summary>
        public void Step(int n = 1) { for (int i = 0; i < n; i++) Visitors.Step(ParkSim.TickMilliseconds / 1000.0, null); }
    }

    public static void Run(Disc disc, Model terrain, WadArchive data, WadArchive world, string worldName,
                           Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "mechanic: " + label);
        var exe = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        byte[] elf = disc.Read(exe.Extent, exe.Size);
        Constants(elf, Check);
        WorkedValues(Check);

        var compiled = new CompiledAssets(new AssetResourceDatabase(data.Read(data.Find("/arsdb.dba"))),
                                          TextDatabase.Load(data, "eur"));
        var rides = Assets(world, worldName, compiled, r => r.HasRideTiers);
        Census(rides, worldName, Check);

        // ---- the park: a corridor, ride slots below it, one isolated slot --------------------------
        var paths = new ParkPaths(terrain);
        paths.SetEntrance(ParkEntrance.ReadExecutable(elf));
        int pathMaterial = Enumerable.Range(1, paths.Materials.Count - 1).First(i => ParkPaths.Classify(paths.Materials[i]) == ParkPathKind.Path);
        int queueMaterial = Enumerable.Range(1, paths.Materials.Count - 1).First(i => ParkPaths.Classify(paths.Materials[i]) == ParkPathKind.Queue);
        ParkCell? origin = null;
        for (int z = 0; z + 14 <= paths.Field.Height && origin == null; z++)
            for (int x = 0; x + 30 <= paths.Field.Width && origin == null; x++)
            {
                bool clear = true;
                for (int dz = 0; dz < 14 && clear; dz++)
                    for (int dx = 0; dx < 30 && clear; dx++)
                    {
                        var c = new ParkCell(x + dx, z + dz);
                        clear = paths.CanBuild(c) && paths.Kind(c) == ParkPathKind.None && !paths.IsEntrance(c);
                    }
                if (clear) origin = new ParkCell(x, z);
            }
        Check(origin != null, $"{worldName}: the park has a clear 30x14 block for the mechanic fixture ({origin})");
        if (origin == null) return;
        var B = origin.Value.Offset(1, 1);
        // The corridor, row +2, 28 cells; a return leg down column 27 to row +8 and back along row +8
        // gives a repair walk with corners (waypoints) to the far slot (18, 9). Ride slots hang under
        // the corridor at x 2, 10 and 18 (origin row 4, stub row 3); the isolated slot (6, 9) has a
        // stub (row 8) that no path reaches.
        for (int x = 0; x < 28; x++) paths.Lay(B.Offset(x, 2), pathMaterial);
        for (int z = 3; z <= 8; z++) paths.Lay(B.Offset(27, z), pathMaterial);
        for (int x = 16; x < 27; x++) paths.Lay(B.Offset(x, 8), pathMaterial);

        // An ordinary ride that runs: one whose script actually reports running (vars 9 and 5) once
        // guests are queued, with connection A on its top row facing -z so its stub is the cell above.
        var ordinaryCandidates = rides.Where(a => a.Record.Kind == AssetResourceDatabase.AssetKind.Ride
            && a.Record.ConnectionA.IsPresent && a.Record.ConnectionA.Direction == 0 && a.Record.ConnectionA.Z == 0
            && a.Record.Width <= 4 && a.Record.Depth <= 4 && a.Record.Tier(0).CapacityParameter > 0
            && a.Record.Tier(0).InitialCondition > 0 && a.Variables.Count > 9 && a.Variables[4] == "VAR_BREAKSTAT").ToList();

        Park NewPark(int seed)
        {
            var sim = new ParkSim(paths);
            var walk = new GuestWalk(paths);
            var rng = new TestRandom(seed);
            var p = new Park { Sim = sim, Rng = rng, B = B, Clock = new ParkClock() };
            p.Visitors = new ParkVisitors(sim, walk, () => 0);
            p.Staff = new ParkStaff(p.Visitors, p.Clock, new NativeActivationSequence(2000, "mechanic checks fixture"), rng.Next);
            p.Visitors.Staff = p.Staff;
            sim.Advisor = (id, r) => p.Advisors.Add((id, r));
            sim.RideSound = (r, c, e, pos) => p.Sounds.Add((r, c, e, pos));
            p.Staff.Advisor = id => p.Advisors.Add((id, null));
            p.Staff.HandleSound = (m, b, e, h) => p.StaffSounds.Add((m, b, e, h));
            return p;
        }
        // Slot k: origin (2 + 6k, 4 + row), stub one cell above connection A, laid as queue.
        ParkRide Place(Park p, Asset asset, ParkCell at, bool layStub = true)
        {
            var a = asset.Record.ConnectionA;
            var stub = at.Offset(a.X, a.Z - 1);
            if (layStub && paths.Kind(stub) == ParkPathKind.None) paths.Lay(stub, queueMaterial);
            var ride = p.Sim.Add(p.NextId++, asset.Stem, at, asset.Record.Width, asset.Record.Depth, asset.Script,
                                 asset.Aps == null ? null : new Animation(asset.Aps), 4, stub, stub, out var fault,
                                 sibling: asset.Sibling, definition: asset.Definition(), placementTurns: 0);
            return ride ?? throw new InvalidOperationException($"{asset.Stem} would not start: {fault}");
        }
        // The first candidate whose script reports running with four queued guests.
        Asset ordinary = null;
        foreach (var candidate in ordinaryCandidates)
        {
            var p = NewPark(1);
            var r = Place(p, candidate, p.At(2, 4));
            p.Sim.SetOpen(r.Id, true); r.Set("VAR_BROKEN", 0);
            for (int g = 1; g <= 4; g++) r.Join(9000 + g);
            bool ran = false;
            for (int t = 0; t < 3000 && !ran; t++) { p.Step(); ran = r.Machine[9] != 0 && r.Machine[5] != 0; }
            if (ran) { ordinary = candidate; break; }
        }
        Check(ordinary != null, $"{worldName}: an ordinary ride whose script reports running (vars 9 and 5) with guests aboard: "
                              + $"{ordinary?.Stem ?? "none"} (of {ordinaryCandidates.Count} candidates with the common set)");
        if (ordinary == null) return;

        var f = new Fixture { NewPark = NewPark, Place = (p, at) => Place(p, ordinary, at, true), Ordinary = ordinary };
        OrdinaryWear(f, Check);
        OrdinaryBreakdown(f, Check);
        Condemned(f, Check);
        Repair(f, Check);
        WalkLedger(f, Check);
        Priority(f, Check);
        CallMechanic(f, Check);
        HandOff(f, Check);
        Upgrades(f, Check);
        HoardingService(f, check);                                      // HoardingChecks.cs: the fence on the same calls
        Ejection(f, Check);
        WorkCells(ordinary, Check);
        TrackClass(paths, world, worldName, compiled, Check);
        CoasterClass(paths, world, worldName, compiled, Check);
        HoardingStations(paths, world, worldName, compiled, check);    // HoardingChecks.cs: the stations' fences
    }

    // =============================================================================================
    // The constants, read back out of the executable.

    static void Constants(byte[] elf, Action<bool, string> Check)
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
        uint Word(uint va) => U32(At(va));
        var times = Enumerable.Range(0, 5).Select(l => U16(At(StaffTables.MechanicTable + (uint)(4 * l)))).ToArray();
        Check(times.SequenceEqual(StaffTables.MechanicWorkTicks),
              $"tables: repair/install ticks u16 0x3627C8 + 4L = {string.Join(",", times)} (the port: {string.Join(",", StaffTables.MechanicWorkTicks)})");
        Check(Word(StaffTables.MechanicChatterPeriodAddress) == StaffTables.MechanicChatterPeriod,
              $"tables: the chatter period [0x2BED10] = {Word(StaffTables.MechanicChatterPeriodAddress)} (the port: {StaffTables.MechanicChatterPeriod})");
        // Instruction words that carry the constants: `ori v0,zero,0x9FFF` (0x116DAC), `lui v0,6` /
        // `ori v0,v0,0x4000` (0x116124/0x11612C), `ori v1,zero,0xF000` and `sra v0,v0,5` (0x117BD0/4),
        // `addiu a2,zero,0xF` (0x153D30), and Call Mechanic's `lbu v1,0x2E(a1)` (0x1241E4) against
        // `addiu a3,zero,0x32` (0x1241B0) -- the MODE byte, not the state.
        uint breakWord = Word(0x116DAC), lui = Word(0x116124), ori = Word(0x11612C), life = Word(0x117BD0),
             shift = Word(0x117BD4), cap = Word(0x153D30), modeLoad = Word(0x1241E4), rest = Word(0x1241B0);
        Check(breakWord == (0x34020000u | (uint)(ParkSim.BreakdownBelow - 1)),
              $"constants: 0x116DAC is `ori v0,zero,0x{breakWord & 0xFFFF:X}` -- break below 0x{ParkSim.BreakdownBelow:X} (10.0)");
        Check(lui == 0x3C020006 && ori == 0x34424000 && ParkSim.FullReliability == (6 << 16 | 0x4000),
              $"constants: 0x116124/0x11612C build 0x{(lui & 0xFFFF) << 16 | (ori & 0xFFFF):X} = 100.0 (the port 0x{ParkSim.FullReliability:X})");
        Check(life == (0x34030000u | (uint)ParkSim.ReliabilityPerLife) && shift == 0x00021143,
              $"constants: 0x117BD0 `ori v1,zero,0x{life & 0xFFFF:X}` (one Life per 15.0) and 0x117BD4 `sra v0,v0,5` (rate >> 5)");
        Check(cap == (0x24060000u | (uint)ParkSim.UpgradeListCapacity),
              $"constants: 0x153D30 `addiu a2,zero,{cap & 0xFFFF}` -- the upgrade list holds {ParkSim.UpgradeListCapacity}");
        Check(modeLoad == 0x90A3002E && rest == 0x24070032,
              "constants: Call Mechanic loads the MODE byte (`lbu v1,0x2E(a1)` at 0x1241E4) for its 0x32 test (0x1241B0) -- the shipped slip");
    }

    /// <summary>The findings' worked tables, computed there from the formula as transcribed (Python),
    /// independent of this port: coaster-operation.md §4.3 (Chak Atak tier 0: msd = mcd = 4, wr 5,
    /// MaxCap 36) and track-ride-operation.md §7.5 (Dino Karts tier 0: cap 8).</summary>
    static void WorkedValues(Action<bool, string> Check)
    {
        var coaster = new (int Riders, int Speed, int Rate, int Loss)[]
            { (1, 50, 5415, 169), (18, 50, 10250, 320), (36, 50, 15725, 491), (36, 100, 20840, 651), (0, 50, 5135, 160) };
        var got = coaster.Select(c => NativeRideReliability.Wear(4, 4, 5, c.Speed, c.Riders, 36)).ToArray();
        Check(got.Zip(coaster).All(z => z.First == z.Second.Rate && z.First >> 5 == z.Second.Loss),
              $"wear rate: the coaster/ordinary formula gives coaster-operation.md §4.3's worked rates {string.Join(",", got)} (loss >> 5: {string.Join(",", got.Select(g => g >> 5))})");
        var track = new (int Pieces, int Speed, int Riders, int Wr, int Rate)[]
            { (20, 50, 4, 5, 11380), (20, 100, 8, 5, 18440), (20, 50, 4, 2, 4552), (12, 50, 4, 5, 9560), (35, 50, 4, 5, 14795) };
        var gotT = track.Select(t => NativeRideReliability.TrackWear(4, 4, t.Wr, t.Speed, t.Riders, 8, t.Pieces)).ToArray();
        Check(gotT.Zip(track).All(z => z.First == z.Second.Rate),
              $"wear rate: the track formula (a length third, / 3) gives track-ride-operation.md §7.5's worked rates {string.Join(",", gotT)}");
    }

    static List<Asset> Assets(WadArchive world, string worldName, CompiledAssets compiled, Func<AssetResourceDatabase.Entry, bool> want)
    {
        var list = new List<Asset>();
        foreach (var e in world.Entries.Where(e => e.Path.EndsWith(".sam", StringComparison.OrdinalIgnoreCase))
                                       .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
        {
            var rec = compiled.For(worldName, e.Path);
            if (rec == null || !want(rec)) continue;
            string stem = e.Path[..^4], folder = stem[..(stem.LastIndexOf('/') + 1)];
            var rse = world.Find(stem + ".rse"); if (rse == null) continue;
            var aps = world.Find(stem + ".aps");
            byte[] script = world.Read(rse);
            IReadOnlyList<string> names;
            try { names = new RseProgram(script).VariableNames; } catch (Exception) { continue; }
            string sam = Encoding.ASCII.GetString(world.Read(e));
            list.Add(new Asset(e.Path, stem, rec, script, aps == null ? null : world.Read(aps),
                n => world.Find(folder + n) is { } s ? world.Read(s) : null,
                () =>
                {
                    var def = RideDefinition.Parse(sam, "/DATA/" + worldName + ".WAD" + e.Path);
                    compiled.Attach(new[] { def }, out _);
                    return def;
                }, names));
        }
        return list;
    }

    /// <summary>`0x1C0E28(inst, 4, v)` writes variable INDEX 4, `0x1FA608` reads 9 and 5: in every ride
    /// script of the four classes those indices must be the common set's names.</summary>
    static void Census(List<Asset> rides, string worldName, Action<bool, string> Check)
    {
        int n = rides.Count;
        var bad = rides.Where(a => !(a.Variables.Count > 9 && a.Variables[4] == "VAR_BREAKSTAT"
                                     && a.Variables[5] == "VAR_ONRIDE" && a.Variables[9] == "VAR_RUNNING")).ToList();
        Check(n > 0 && bad.Count == 0,
              $"census: in all {n} {worldName} ride scripts (classes 1/3/6/7) variable 4 is VAR_BREAKSTAT, 5 VAR_ONRIDE and 9 VAR_RUNNING"
              + (bad.Count > 0 ? $" -- NOT in {string.Join(", ", bad.Select(b => b.Stem))}" : ""));
    }

    // =============================================================================================
    // Ordinary rides: wear cadence and amount, through ParkSim.Advance with the ride's own script.

    static void OrdinaryWear(Fixture f, Action<bool, string> Check)
    {
        var p = f.NewPark(2);
        var r = f.Open(p, p.At(2, 4));
        var t0 = r.Definition.CompiledEntry.Tier(0);
        int life0 = r.Life;
        int wears = 0, wrong = 0, offPhase = 0, idleOnPhase = 0, closedWear = 0;
        long lifeLoss = 0;
        var statuses = new HashSet<int>();
        for (int g = 1; g <= 40; g++) r.Join(9100 + g);
        bool running = false;
        for (int t = 0; t < 3000; t++)
        {
            // The boarding pass runs BEFORE the script's slice, so its gate reads the vars the script
            // left last tick, and the riders it wears with are last tick's plus this tick's offer.
            int relBefore = r.Reliability, queueBefore = r.Queue.Count, ridersBefore = r.NativeRiders;
            bool wasRunning = running;
            p.Step();
            uint tick = p.Sim.Tick;
            running = r.Machine[9] != 0 && r.Machine[5] != 0;
            statuses.Add(r.Status);
            int offered = queueBefore - r.Queue.Count;
            int loss = relBefore - r.Reliability;
            // ⚠ The status the pass ran in is 2/4/10/11 throughout (an open ordinary ride never leaves them here).
            bool gate = (tick & 3) == 0 && wasRunning;
            int expected = gate ? Math.Min(relBefore, NativeRideReliability.Wear(t0.MinSpeedDamage, t0.MinCapacityDamage, t0.WearRate,
                                                                                  r.Speed, ridersBefore + offered, t0.CapacityParameter) >> 5) : 0;
            if (loss != expected) wrong++;
            if (loss > 0) { wears++; lifeLoss += relBefore / ParkSim.ReliabilityPerLife - r.Reliability / ParkSim.ReliabilityPerLife; }
            if (wasRunning && (tick & 3) != 0) offPhase++;
            if (!wasRunning && (tick & 3) == 0) idleOnPhase++;
        }
        Check(wears > 20 && wrong == 0 && offPhase > 0 && idleOnPhase > 0 && statuses.All(s => s is 2 or 4 or 10 or 11),
              $"ordinary wear: {f.Ordinary.Stem} loses rate(0) >> 5 (riders = the handshake's count) exactly on ticks with tick & 3 == 0 "
              + $"while its script reports running (vars 9 and 5, 0x1FA608) -- {wears} wears, {wrong} ticks off the rule, {offPhase} running "
              + $"ticks off phase and {idleOnPhase} on-phase ticks not running, all unworn");
        // A point goes each time the reliability crosses a multiple of 15.0 (signed divides), so from 100.0
        // the total is floor(100.0/15.0) - floor(rel/15.0) whatever the steps were.
        long crossings = ParkSim.FullReliability / ParkSim.ReliabilityPerLife - r.Reliability / ParkSim.ReliabilityPerLife;
        Check(lifeLoss > 0 && r.Life == life0 - lifeLoss && lifeLoss == crossings,
              $"ordinary wear: Life falls one point per 15.0 boundary crossed ({life0} -> {r.Life}; reliability 0x{r.Reliability:X}, {crossings} boundaries below 100.0)");
        Check(statuses.Contains(2) && statuses.Contains(10) && statuses.Contains(11),
              $"ordinary status: the boarding pass writes 10 on an offer, 2 while running and 11 on a leaver (seen {string.Join(",", statuses.OrderBy(s => s))})");
        // Closed (3): the empty tick, no wear even with riders aboard and the script running.
        p.Sim.SetOpen(r.Id, false);
        for (int t = 0; t < 200; t++)
        {
            int before = r.Reliability;
            p.Step();
            if (r.Reliability != before) closedWear++;
        }
        Check(closedWear == 0 && r.Status == 3, $"ordinary wear: a closed ride (3) is not worn ({closedWear} of 200 ticks)");
    }

    // =============================================================================================
    // Breakdown thresholds and the enter handlers, ordinary class.

    static void OrdinaryBreakdown(Fixture f, Action<bool, string> Check)
    {
        ParkRide Fresh(Park p, int status)
        {
            var r = f.Open(p, p.At(2, 4));
            if (status == 3) p.Sim.SetOpen(r.Id, false); else p.Sim.SetRideStatus(r, status);
            return r;
        }
        {   // exactly 10.0 does not break; one unit under does, from status 2
            var p = f.NewPark(3); var r = Fresh(p, 2);
            r.ForceReliabilityForTest(ParkSim.BreakdownBelow); p.Step();
            byte at10 = r.Status;
            r.ForceReliabilityForTest(ParkSim.BreakdownBelow - 1); p.Step();
            bool flagAtBreak = r.ServiceFlag;
            p.Step();
            Check(at10 == 2 && r.Status == 4 && !flagAtBreak && r.ServiceFlag && r.Machine[4] == 1,
                  $"breakdown: an ordinary ride at exactly 10.0 stays 2 ({at10}); at 0x9FFF it breaks to 4 ({r.Status}); the NEXT check, seeing 4, "
                  + $"sets the service flag and its script's VAR_BREAKSTAT (variable 4) = {r.Machine[4]} (0x116D68, 0x118568)");
            var enter4 = p.Advisors.Where(a => a.Id == ParkSim.AdvisorBreakdownImminent).ToList();
            Check(enter4.Count == 1 && ReferenceEquals(enter4[0].Ride, r) && p.Sim.AdvisorCounters[0] == 1
                  && p.Sounds.Count(s => s.Category == 0x0C && s.Event == 0xE2 && !s.Positional) == 1,
                  $"breakdown: entering 4 posts advisor 0x36 with the ride once, counts slot 0 ({p.Sim.AdvisorCounters[0]}) and plays 0xC/0xE2 (0x1164D0)");
        }
        {   // only from 2: in 10, 11 and 3 below 10.0 nothing happens (0x116D68)
            var got = new List<string>();
            foreach (int s in new[] { 10, 11, 3 })
            {
                var p = f.NewPark(4); var r = Fresh(p, s);
                // ⚠ a ride with nobody queued does not run, so the boarding pass writes no status.
                r.ForceReliabilityForTest(0x5000); p.Step();
                got.Add($"{s}->{r.Status}");
            }
            Check(got.SequenceEqual(new[] { "10->10", "11->11", "3->3" }),
                  $"breakdown: an ordinary ride breaks ONLY from 2 -- below 10.0 in 10, 11 and 3 it stays ({string.Join(" ", got)})");
        }
        {   // rel exactly 0 while in 2: the rel == 0 branch only moves 4 -> 5, so it does not break at all
            var p = f.NewPark(5); var r = Fresh(p, 2);
            r.ForceReliabilityForTest(0); p.Step();
            byte zeroIn2 = r.Status;
            r.ForceReliabilityForTest(0x100); p.Step();
            byte then4 = r.Status;
            r.ForceReliabilityForTest(0); p.Step();
            Check(zeroIn2 == 2 && then4 == 4 && r.Status == 5,
                  $"breakdown: reliability 0 in 2 does not break (the rel == 0 branch only takes 4 to 5): 2 stays {zeroIn2}; 0x100 -> {then4}; 0 in 4 -> {r.Status}");
            var enter5 = p.Advisors.Where(a => a.Id is ParkSim.AdvisorNoMechanics or ParkSim.AdvisorBusyMechanics or ParkSim.AdvisorMechanicOnIt).ToList();
            Check(enter5.Count == 1 && enter5[0].Id == ParkSim.AdvisorNoMechanics && p.Sim.AdvisorCounters[1] == 1
                  && p.Sounds.Count(s => s.Category == 2 && s.Event == 0x70 && s.Positional) == 1,
                  $"breakdown: entering 5 with no mechanic posts 0x37 (0x103658), counts slot 1 and plays bank 2 0x70 at the ride (0x116550) [{string.Join(",", enter5.Select(a => $"0x{a.Id:X}"))}]");
        }
        {   // 5 and 6: the empty ticks -- nobody is offered, nobody collected
            var p = f.NewPark(6); var r = Fresh(p, 2);
            for (int g = 1; g <= 3; g++) r.Join(9200 + g);
            p.Sim.SetRideStatus(r, 6);
            int queued = r.Queue.Count, lmo = r.Get("VAR_LETMEON");
            p.Step(50);
            Check(queued == 3 && r.Queue.Count == queued && r.Get("VAR_LETMEON") == lmo && r.Status == 6,
                  $"status 6 (ordinary): ticks 5/6 are empty (0x1E5100/0x1E5108) -- the queue of {queued} is not offered ({r.Queue.Count} left, LETMEON {r.Get("VAR_LETMEON")})");
        }
    }

    // =============================================================================================
    // Life and condemnation.

    static void Condemned(Fixture f, Action<bool, string> Check)
    {
        var p = f.NewPark(7);
        var r = f.Open(p, p.At(2, 4));
        var m = Hire(p);                                // held: counts as a mechanic, is not updated
        Check(p.Staff.RequestUpgrade(r) == ParkStaff.UpgradeRequest.Queued && p.Sim.UpgradeList.Contains(r),
              "condemned: fixture -- the ride is on the upgrade list (requested with a held mechanic)");
        // Life 1, reliability just over a 15.0 boundary: the next wear crosses it through the setter.
        r.Life = 1;
        r.ForceReliabilityForTest(ParkSim.ReliabilityPerLife * 2 + 1);
        for (int g = 1; g <= 20; g++) r.Join(9300 + g);
        int ticks = 0;
        while (r.Life > 0 && ticks < 4000) { p.Step(); ticks++; }
        p.Step(2);
        int condemnAdvisor = p.Advisors.Count(a => a.Id == ParkSim.AdvisorCondemned && ReferenceEquals(a.Ride, r));
        Check(r.Life == 0 && condemnAdvisor == 1 && r.ServiceFlag && r.Status == 4 && !p.Sim.UpgradeList.Contains(r)
              && r.Machine[4] == 1 && p.Sounds.Any(s => ReferenceEquals(s.Ride, r) && s.Category == 2 && s.Event == 0x18 && !s.Positional),
              $"condemned: Life 1 -> 0 posts 0x87 once (0x1E1CF0); the ride is then flagged, VAR_BREAKSTAT 1, off the upgrade list, "
              + $"forced to 4 and plays bank 2 0x18 (0x1169C0) -- status {r.Status}, flag {r.ServiceFlag}, after {ticks} ticks");
        // Never repaired: the dispatch refuses it, a coin of 0 sends him to patrol, Call Mechanic does nothing.
        bool dispatch = m.Dispatch(r, repair: true);
        Drop(p, m, p.At(6, 2));
        bool called = p.Staff.CallMechanic(r);
        p.Rng.Override = n => n == 2 ? 0 : null;
        for (int t = 0; t < 400; t++) p.Step();
        Check(!dispatch && !called && r.AssignedMechanic == null && m.RepairDispatches == 0 && r.Status == 4 && r.ServiceFlag,
              $"condemned: never repaired -- 0x178CF8 refuses Life 0 ({dispatch}), Call Mechanic takes nobody ({called}), and 400 ticks of "
              + $"coin-0 decisions leave it unassigned in 4 ({m.RepairDispatches} repair dispatches)");
    }

    // =============================================================================================
    // The repair, end to end, at every level.

    static void Repair(Fixture f, Action<bool, string> Check)
    {
        var timings = new List<string>();
        bool allTimed = true;
        var a = f.Ordinary.Record.ConnectionA;
        for (int level = 0; level <= StaffTables.MaxLevel; level++)
        {
            var p = f.NewPark(10 + level);
            var r = f.Open(p, p.At(2, 4));
            var mouth = p.At(2 + a.X, 2);                            // the adapter's mouth: the path cell past the stub
            p.Staff.QueueMouth = ride => ReferenceEquals(ride, r) ? mouth : null;
            var m = HireAt(p, p.At(12, 2), level);
            p.Rng.Override = n => n == 2 ? 0 : null;
            // At the top level a voice is simulated on the noise's handle: it sounds for 25 ticks after
            // each raise, so 0x111CC8 says "playing" and the noise must NOT be raised again until it ends.
            int lastNoise = int.MinValue / 2;
            if (level == StaffTables.MaxLevel)
            {
                p.Staff.HandleSound = (mm, b, e, h) =>
                {
                    p.StaffSounds.Add((mm, b, e, h));
                    if (h == Mechanic.HandleRepairNoise) lastNoise = (int)p.Staff.Now;
                };
                p.Staff.HandlePlaying = (mm, h) => h == Mechanic.HandleRepairNoise && (int)p.Staff.Now - lastNoise < 25;
            }
            r.ForceReliabilityForTest(0x5000);
            int arrivedAt = -1, sixAt = -1, sevenAt = -1, elevenAt = -1, leftAt = -1, eTicks = 0, facingOk = 0, moraleBefore = -1;
            ParkCell arrivedCell = default;
            for (int t = 0; t < 4000 && leftAt < 0; t++)
            {
                byte stateBefore = m.State;
                p.Step();
                if (arrivedAt < 0 && m.State == Mechanic.StateClosingRide)
                {
                    arrivedAt = t; arrivedCell = m.Cell;
                    // ⚠ FIXTURE: turn him AWAY from the ride as he arrives. His last step already points
                    // at the door, so without this "faces the ride" would pass on the walk's facing alone;
                    // now only 0x1787D0 in the 0xE ticks can turn him back.
                    typeof(StaffMember).GetProperty("FacingQuarterTurns")!.SetValue(m, (r.NativeRotation + 2) & 3);
                }
                if (sixAt < 0 && r.Status == 6) sixAt = t;
                if (stateBefore == Mechanic.StateRepairing)
                {
                    eTicks++;
                    if (m.FacingQuarterTurns == r.NativeRotation && m.LogicalRequest == StaffTables.LogicalWork) facingOk++;
                }
                if (elevenAt < 0 && m.State == Mechanic.StateOpeningRide) { elevenAt = t; moraleBefore = m.Morale; }
                if (sevenAt < 0 && sixAt >= 0 && r.Status != 6) sevenAt = t;
                if (elevenAt >= 0 && t > elevenAt && m.State == 0 && m.Target == null) leftAt = t;
            }
            int T = StaffTables.MechanicWorkTicks[level];
            bool ok = arrivedAt >= 0 && sixAt == arrivedAt + 1 && eTicks == T + 1 && sevenAt == sixAt + T + 2 && facingOk == T + 1;
            allTimed &= ok;
            timings.Add($"L{level}: arrive {arrivedAt}, 6 +{sixAt - arrivedAt}, 0xE {eTicks} ticks, 7 at +{sevenAt - sixAt}");
            if (level == StaffTables.MaxLevel)
            {
                int raises = p.StaffSounds.Count(s => s.Member == m && s.Bank == 2 && s.Event == Mechanic.SoundRepairNoise);
                int expected = (T + 1 + 24) / 25;                // one raise, then one each time the 25-tick voice ends
                Check(raises == expected,
                      $"repair: the noise is raised again only when 0x111CC8 says its handle is idle -- a 25-tick voice over {T + 1} repairing ticks is raised {raises} times (expected {expected})");
            }
            if (level != 0) continue;
            Check(arrivedAt >= 0 && arrivedCell == ParkSim.WorkCell(r) && ParkSim.WorkCell(r) == r.Entrance,
                  $"repair: he walks to the work cell vt+0x17C = the stub outside the door {ParkSim.WorkCell(r)} (arrived at {arrivedCell}, tick {arrivedAt})");
            Check(r.Status == 10 && r.Reliability == ParkSim.FullReliability && !r.ServiceFlag && r.Machine[4] == 0
                  && r.AssignedMechanic == null && r.Life == r.Definition.CompiledEntry.Tier(0).InitialCondition,
                  $"repair: the ride reopens -- 7, 2, then 10 ({r.Status}), reliability 100.0 (0x{r.Reliability:X}), flag clear, "
                  + $"VAR_BREAKSTAT {r.Machine[4]}, unassigned, and Life NOT restored ({r.Life})");
            Check(leftAt > 0 && m.Cell == mouth && m.Morale == Math.Min(100, moraleBefore + 10) && m.Shown,
                  $"repair: pass B -- shown, morale +10 ({moraleBefore} -> {m.Morale}), then the walk out to the queue mouth vt+0xF4 (at {m.Cell})");
            int noise = p.StaffSounds.Count(s => s.Member == m && s.Bank == 2 && s.Event == Mechanic.SoundRepairNoise && s.Handle == Mechanic.HandleRepairNoise);
            Check(noise == T + 1,
                  $"repair: every repairing tick raises bank 2 0x6F on handle P+0x60 when the handle is idle (no player: {noise} raises for {T + 1} ticks)");
        }
        Check(allTimed, $"repair timing: status 6 the tick after arrival, 0xE for T[L]+1 ticks facing the ride's rotation with logical 16, "
                        + $"7 exactly T[L]+2 after the 6 -- {string.Join("; ", timings)}");
    }

    // =============================================================================================
    // Find work's ledger and the repair walk's waypoint cost.

    static void WalkLedger(Fixture f, Action<bool, string> Check)
    {
        {   // an idle attempt: tiredness +6, morale +1, then patrol
            var p = f.NewPark(20);
            var m = HireAt(p, p.At(6, 2));
            m.Tiredness = 10; m.Morale = 40;
            p.Rng.Override = n => n == 2 ? 0 : null;
            p.Step();
            Check(m.Tiredness == 16 && m.Morale == 41 && m.State == StaffMember.StatePatrol,
                  $"find work: an attempt that finds nothing costs tiredness +6 and morale +1 and goes to patrol (0x178E08..; t {m.Tiredness}, m {m.Morale}, state 0x{m.State:X})");
        }
        {   // a repair walk with corners: each waypoint in mode 6 costs -2/-2 before the phase +1
            var p = f.NewPark(21);
            var r = f.Open(p, p.At(18, 9));                 // the far slot, reached round the return leg
            r.ForceReliabilityForTest(0x5000);
            p.Step();                                        // it breaks with nobody hired
            var m = HireAt(p, p.At(2, 2));
            p.Rng.Override = n => n == 2 ? 0 : null;
            p.Step();                                        // dispatched (0x38)
            p.Step();                                        // route requested (0xB, mode 6)
            m.Tiredness = 60; m.Morale = 60;
            long retires = m.WaypointsRetired; int phase = 0;
            int t0 = m.Tiredness, m0 = m.Morale;
            for (int t = 0; t < 3000 && m.State != Mechanic.StateClosingRide; t++)
            {
                long before = m.WaypointsRetired;
                bool onPhase = m.OnPhase;
                p.Step();
                if (m.WaypointsRetired > before && onPhase) phase++;
            }
            long n = m.WaypointsRetired - retires;
            Check(m.Mode == Mechanic.ModeRepair && n >= 3 && m.Morale == m0 - 2 * n && m.Tiredness == t0 - 2 * n + phase,
                  $"repair walk: each of {n} waypoints in mode 6 costs morale -2 and tiredness -2, and {phase} phase retires add +1 "
                  + $"(0x178BE8 then 0x1DB970): morale {m0} -> {m.Morale}, tiredness {t0} -> {m.Tiredness}");
        }
        {   // the same walk as an INSTALL (mode 0x14): only the base's phase +1
            var p = f.NewPark(22);
            var r = f.Open(p, p.At(18, 9));
            var m = HireAt(p, p.At(2, 2));
            p.Staff.RequestUpgrade(r);
            p.Rng.Override = n => n == 2 ? 1 : null;
            p.Step();                                        // dispatched (0x39)
            p.Step();                                        // route requested (0xB, mode 0x14)
            m.Tiredness = 60; m.Morale = 60;
            long retires = m.WaypointsRetired; int phase = 0;
            int t0 = m.Tiredness, m0 = m.Morale;
            for (int t = 0; t < 3000 && m.State != Mechanic.StateInstallArrival; t++)
            {
                long before = m.WaypointsRetired;
                bool onPhase = m.OnPhase;
                p.Step();
                if (m.WaypointsRetired > before && onPhase) phase++;
            }
            long n = m.WaypointsRetired - retires;
            Check(m.State == Mechanic.StateInstallArrival && n >= 3 && m.Morale == m0 && m.Tiredness == t0 + phase,
                  $"install walk: its {n} waypoints in mode 0x14 cost nothing extra -- only {phase} phase retires add +1 "
                  + $"(0x178BE8 tests mode 6): morale {m0} -> {m.Morale}, tiredness {t0} -> {m.Tiredness}");
        }
    }

    // =============================================================================================
    // Dispatch priority, the dead duplicated branch, ties.

    static void Priority(Fixture f, Action<bool, string> Check)
    {
        // Rides break with the mechanic HELD (not updated); he is put down idle, then the coin decides.
        (Park P, ParkRide Near, ParkRide Far, ParkRide Upgrade, Mechanic M) Setup(int seed, bool request)
        {
            var p = f.NewPark(seed);
            var far = f.Open(p, p.At(18, 4));
            var near = f.Open(p, p.At(2, 4));
            var up = f.Open(p, p.At(10, 4));
            var m = Hire(p);
            if (request) p.Staff.RequestUpgrade(up);
            near.ForceReliabilityForTest(0x5000); far.ForceReliabilityForTest(0x5000);
            p.Step();
            Drop(p, m, p.At(4, 2));
            return (p, near, far, up, m);
        }
        {
            var (p, near, far, up, m) = Setup(30, true);
            p.Rng.Override = n => n == 2 ? 0 : null;
            p.Step();
            Check(near.Broken && far.Broken && ReferenceEquals(m.Job, near) && m.RepairDispatches == 1,
                  $"priority: coin 0 with two broken rides and an upgrade pending repairs the NEAREST broken ride ({m.Job?.Origin}; far {far.Origin})");
        }
        {
            var (p, near, far, up, m) = Setup(31, true);
            p.Rng.Override = n => n == 2 ? 1 : null;
            p.Step();
            Check(ReferenceEquals(m.Job, up) && m.InstallDispatches == 1 && m.RepairDispatches == 0,
                  $"priority: coin 1 installs the upgrade even with two broken rides nearer ({m.Job?.Origin})");
        }
        {
            // ⭐ THE DEAD BRANCH: coin 1, broken rides, NO upgrade entry -- the second 0x153D40 finds nothing,
            // so nobody is repaired however many coin-1 decisions he makes.
            var (p, near, far, up, m) = Setup(32, false);
            p.Rng.Override = n => n == 2 ? 1 : null;
            int decisions = 0;
            for (int t = 0; t < 600; t++) { if (m.State == 0) decisions++; p.Step(); }
            Check(decisions >= 3 && m.RepairDispatches == 0 && near.AssignedMechanic == null && far.AssignedMechanic == null,
                  $"priority: the duplicated 0x153D40 is DEAD -- {decisions} coin-1 decisions with two broken rides and an empty upgrade list repair nothing");
        }
        {
            // Ties: two broken ordinary rides equally far -- the NEWER (placed later) is found first.
            var p = f.NewPark(33);
            var older = f.Open(p, p.At(2, 4));
            var newer = f.Open(p, p.At(10, 4));
            var m = Hire(p);
            older.ForceReliabilityForTest(0x5000); newer.ForceReliabilityForTest(0x5000);
            p.Step();
            int midX = (older.Origin.X + newer.Origin.X) / 2;
            Drop(p, m, new ParkCell(midX, p.At(0, 2).Z));
            int dOld = Math.Abs(midX - older.Origin.X) + Math.Abs(m.Cell.Z - older.Origin.Z);
            int dNew = Math.Abs(midX - newer.Origin.X) + Math.Abs(m.Cell.Z - newer.Origin.Z);
            p.Rng.Override = n => n == 2 ? 0 : null;
            p.Step();
            Check(dOld == dNew && ReferenceEquals(m.Job, newer),
                  $"priority: a tie ({dOld} = {dNew} cells) goes to the first found -- the NEWER ride, the pool's active list being newest first ({m.Job?.Origin})");
        }
        {
            // Assigned to another: excluded from my search, so the FARTHER broken ride is mine. (Were it
            // only refused at dispatch, 0x178CF8 would turn him away and he would patrol instead.)
            var p = f.NewPark(34);
            var r = f.Open(p, p.At(2, 4));
            var far = f.Open(p, p.At(18, 4));
            var a = Hire(p);
            var b = Hire(p);
            r.ForceReliabilityForTest(0x5000); far.ForceReliabilityForTest(0x5000); p.Step();
            Drop(p, a, p.At(20, 2)); Drop(p, b, p.At(4, 2));
            bool took = a.Dispatch(r, repair: true);
            p.Rng.Override = n => n == 2 ? 0 : null;
            p.Step();
            Check(took && ReferenceEquals(r.AssignedMechanic, a) && ReferenceEquals(b.Job, far) && b.RepairDispatches == 1,
                  $"priority: a broken ride assigned to another mechanic is skipped by 0x153B80 -- the nearer one is passed over for the far one ({b.Job?.Origin}; state 0x{b.State:X})");
        }
    }

    // =============================================================================================
    // Call Mechanic and the shipped predicate; the breakdown advisors.

    static void CallMechanic(Fixture f, Action<bool, string> Check)
    {
        (Park P, ParkRide R, Mechanic Older, Mechanic Newer) Setup(int seed, bool breakIt = true)
        {
            var p = f.NewPark(seed);
            var r = f.Open(p, p.At(2, 4));
            var older = Hire(p);
            var newer = Hire(p);
            if (breakIt) { r.ForceReliabilityForTest(0x5000); p.Step(); }
            Drop(p, older, p.At(4, 2)); Drop(p, newer, p.At(24, 2));
            return (p, r, older, newer);
        }
        // ⚠ REFLECTION: the console reaches these (mode, state, target) combinations; a short fixture does not.
        static void Poke(StaffMember m, string property, object value) => m.GetType().GetProperty(property)!.SetValue(m, value);
        {
            var (p, r, older, newer) = Setup(40);
            bool called = p.Staff.CallMechanic(r);
            Check(called && ReferenceEquals(r.AssignedMechanic, newer) && older.Target == null,
                  "call mechanic: the FIRST available in list order (newest first, no distance test) takes it -- the newer hire, though the older is nearer");
            Check(!p.Staff.CallMechanic(r), "call mechanic: a ride that already has a mechanic gets nobody else");
        }
        {
            var (p, r, older, newer) = Setup(41);
            Poke(newer, "Mode", (byte)0x32);
            bool byMode = newer.Available;
            p.Staff.CallMechanic(r);
            bool skipped = ReferenceEquals(r.AssignedMechanic, older);
            var (p2, r2, older2, newer2) = Setup(42);
            Poke(newer2, "State", StaffMember.StateResting);
            bool byState = newer2.Available;
            p2.Staff.CallMechanic(r2);
            Check(!byMode && skipped && byState && ReferenceEquals(r2.AssignedMechanic, newer2),
                  "call mechanic: the predicate tests the MODE byte for 0x32 (refused, the next one called) and not the resting STATE (a state-0x32 "
                  + "member with no target is taken) -- the shipped slip, 0x1241FC");
            var (p3, r3, older3, newer3) = Setup(43);
            Poke(newer3, "State", StaffMember.StateResting);
            Poke(newer3, "Target", new object());                      // 0x1DBB80 targets the staff room
            p3.Staff.CallMechanic(r3);
            Check(ReferenceEquals(r3.AssignedMechanic, older3),
                  "call mechanic: a mechanic resting FOR REAL (target = his staff room, 0x1DBD64) is refused by the target test, so resting mechanics are NOT called out");
        }
        {
            var (p, r, older, newer) = Setup(44, breakIt: false);
            bool called = p.Staff.CallMechanic(r);
            Check(!called && r.AssignedMechanic == null && older.Target == null && newer.Target == null,
                  "call mechanic: a ride that is not broken gets nobody -- the first available's dispatch refuses and the loop ends there");
        }
        // 0x103658 at enter 5: no mechanics 0x37, an available one 0x39, all busy 0x38.
        {
            var got = new List<int>();
            foreach (int kase in new[] { 0, 1, 2 })
            {
                var p = f.NewPark(45 + kase);
                var r = f.Open(p, p.At(2, 4));
                if (kase > 0)
                {
                    var m = HireAt(p, p.At(20, 2));
                    if (kase == 2) Poke(m, "Target", new object());
                }
                p.Advisors.Clear();
                p.Sim.SetRideStatus(r, 4);
                p.Sim.SetRideStatus(r, 5);
                got.Add(p.Advisors.Last().Id);
            }
            Check(got.SequenceEqual(new[] { ParkSim.AdvisorNoMechanics, ParkSim.AdvisorMechanicOnIt, ParkSim.AdvisorBusyMechanics }),
                  $"advisor 0x103658: no mechanics 0x37, an available one 0x39 ('on his way', though nothing is dispatched), all busy 0x38 "
                  + $"[{string.Join(",", got.Select(g => $"0x{g:X}"))}]");
        }
    }

    // =============================================================================================
    // Route failure hands the job only to mechanics AFTER me.

    static void HandOff(Fixture f, Action<bool, string> Check)
    {
        var p = f.NewPark(50);
        var r = f.Open(p, p.At(6, 9));                   // the isolated slot: its stub is a queue cell no path reaches
        var first = Hire(p); var middle = Hire(p); var last = Hire(p);
        r.ForceReliabilityForTest(0x5000); p.Step();
        Drop(p, first, p.At(20, 2)); Drop(p, middle, p.At(22, 2)); Drop(p, last, p.At(24, 2));
        // The list is newest first: last, middle, first. Send MIDDLE; his route fails.
        bool sent = middle.Dispatch(r, repair: true);
        p.Rng.Override = n => n == 2 ? 1 : null;          // the others only look for upgrades (there are none)
        int unreachable = p.Staff.RouteRequests.Unreachable;
        for (int t = 0; t < 30 && ReferenceEquals(r.AssignedMechanic, middle); t++) p.Step();
        // ⚠ The hand-off leaves him in state 0 and, in the same update, his find work sends him to patrol.
        Check(sent && p.Staff.RouteRequests.Unreachable > unreachable && ReferenceEquals(r.AssignedMechanic, first)
              && middle.Target == null && middle.RepairDispatches == 1 && middle.State is 0 or StaffMember.StatePatrol
              && last.RepairDispatches == 0 && first.RepairDispatches == 1,
              "hand-off: a failed route (event 2, mode 6) unassigns and passes the job to the first available AFTER me in list order "
              + $"(0x178458 walks from *P) -- the OLDER hire, not the newer ({(ReferenceEquals(r.AssignedMechanic, first) ? "older" : ReferenceEquals(r.AssignedMechanic, last) ? "newer" : "nobody")} took it)");
    }

    // =============================================================================================
    // Upgrades: the request, the list, the install at completion.

    static void Upgrades(Fixture f, Action<bool, string> Check)
    {
        {
            var p = f.NewPark(60);
            var r = f.Open(p, p.At(2, 4));
            int balance = p.Sim.Finances.Balance;
            var none = p.Staff.RequestUpgrade(r);
            var m = HireAt(p, p.At(20, 2));
            p.Staff.SetStriking(StaffKind.Mechanic, true);
            var strike = p.Staff.RequestUpgrade(r);
            p.Staff.SetStriking(StaffKind.Mechanic, false);
            bool listedEarly = p.Sim.UpgradeList.Contains(r) || r.UpgradePending;
            var ok = p.Staff.RequestUpgrade(r);
            Check(none == ParkStaff.UpgradeRequest.NoMechanics && strike == ParkStaff.UpgradeRequest.MechanicsOnStrike && !listedEarly
                  && ok == ParkStaff.UpgradeRequest.Queued && r.UpgradePending && p.Sim.UpgradeList.SequenceEqual(new[] { r })
                  && p.Sim.Finances.Balance == balance,
                  "upgrade request: refused with no mechanic and during a mechanic strike (nothing listed); otherwise listed with +0x128 = 1 and NO money taken (0x1D5C00)");
            var extra = Enumerable.Range(0, 15).Select(i => new ParkRide { Id = 500 + i, Definition = r.Definition }).ToList();
            var results = extra.Select(x => p.Staff.RequestUpgrade(x)).ToList();
            Check(results.Take(14).All(x => x == ParkStaff.UpgradeRequest.Queued) && results[14] == ParkStaff.UpgradeRequest.ListFull
                  && p.Sim.UpgradeList.Count == 15 && extra[14].UpgradePending && !p.Sim.UpgradeList.Contains(extra[14]),
                  $"upgrade list: 15 slots (0x153950); a 16th request is dropped while +0x128 is still set ({p.Sim.UpgradeList.Count} listed)");
            var second = HireAt(p, p.At(22, 2));
            p.Staff.Fire(second);
            int kept = p.Sim.UpgradeList.Count;
            p.Staff.Fire(m);
            Check(kept == 15 && p.Sim.UpgradeList.Count == 0,
                  $"fire: firing a mechanic while another remains keeps the list ({kept}); firing the LAST clears it (0x124300 -> 0x1542A0)");
        }
        {
            // The install, end to end: coin 1, the flag, 6, T+1 ticks, then tier + 1 and the cost -- at completion.
            var p = f.NewPark(61);
            var r = f.Open(p, p.At(2, 4));
            var e = r.Definition.CompiledEntry;
            r.Speed = e.Tier(0).MaxSpeed; r.Duration = e.Tier(0).MinDuration; r.Capacity = 1;
            r.ForceReliabilityForTest(0x40000);
            var m = Hire(p, 1);
            p.Staff.RequestUpgrade(r);
            Drop(p, m, p.At(10, 2));
            p.Rng.Override = n => n == 2 ? 1 : null;
            int paidAt = -1, installedAt = -1, sixAt = -1, flagAt = -1, balanceBeforeInstall = p.Sim.Finances.Balance;
            int installingTicks = 0, chatters = 0, expectedChatters = 0, silentTicks = 0, pairedOk = 0;
            for (int t = 0; t < 3000 && installedAt < 0; t++)
            {
                int bal = p.Sim.Finances.Balance;
                bool installing = m.State == Mechanic.StateInstalling;
                uint now = p.Staff.Now;                      // the value this update's chatter reads
                int sounds = p.StaffSounds.Count;
                p.Step();
                if (installing)
                {
                    installingTicks++;
                    var raised = p.StaffSounds.Skip(sounds).Where(x => x.Member == m && x.Bank == 8).ToList();
                    chatters += raised.Count;
                    pairedOk += raised.Count(x => x.Event == Mechanic.SoundChatterA ? x.Handle == Mechanic.HandleChatterA
                                                 : x.Event == Mechanic.SoundChatterB && x.Handle == Mechanic.HandleChatterB);
                    if (now % StaffTables.MechanicChatterPeriod != 0) expectedChatters++; else silentTicks++;
                }
                if (flagAt < 0 && r.ServiceFlag) flagAt = t;
                if (sixAt < 0 && r.Status == 6) sixAt = t;
                if (paidAt < 0 && p.Sim.Finances.Balance != bal) { paidAt = t; balanceBeforeInstall = bal; }
                if (r.CurrentTier == 1) installedAt = t;
            }
            var t1 = e.Tier(1);
            int T = StaffTables.MechanicWorkTicks[1];
            Check(installingTicks == T + 1 && silentTicks > 0 && chatters == expectedChatters && pairedOk == chatters,
                  $"chatter (0x1781B8): every 0x36 tick raises bank 8 0xA2 on P+0x58 or 0xA3 on P+0x5C EXCEPT when the tick count is a multiple "
                  + $"of [0x2BED10] = {StaffTables.MechanicChatterPeriod} -- {chatters} raises over {installingTicks} installing ticks, {silentTicks} silent");
            Check(installedAt > 0 && paidAt == installedAt && sixAt == flagAt + 1 && installedAt == sixAt + T + 1
                  && r.Machine[4] == 1 && balanceBeforeInstall - p.Sim.Finances.Balance == t1.PurchaseCost * 10,
                  $"install: the flag on arrival (VAR_BREAKSTAT raised: an upgrading ride smokes like a broken one), 6 the next tick, and after "
                  + $"T[1]+1 = {T + 1} ticks tier 0 -> 1 with the NEW tier's cost x 10 = {t1.PurchaseCost * 10} debited THEN, not at the request "
                  + $"[flag {flagAt}, 6 {sixAt}, installed/paid {installedAt}/{paidAt}]");
            Check(r.Reliability == ParkSim.FullReliability && r.Capacity == Math.Max(1, t1.CapacityParameter >> 1)
                  && r.Speed == t1.MinSpeed + ((t1.MaxSpeed - t1.MinSpeed) >> 1) && r.Duration == Math.Max(1, t1.MaxDuration >> 1)
                  && !r.UpgradePending && !p.Sim.UpgradeList.Contains(r)
                  && p.Sounds.Any(s => ReferenceEquals(s.Ride, r) && s.Category == 2 && s.Event == 0xB8 && s.Positional),
                  $"install (0x116268 -> 0x116120): reliability 100.0, capacity {r.Capacity}, speed {r.Speed}, duration {r.Duration} -- the NEW "
                  + "tier's defaults -- pending cleared, off the list, bank 2 0xB8 at the ride");
            p.Step(3);
            Check(r.Status == 10 && !r.ServiceFlag && r.AssignedMechanic == null && r.Machine[4] == 0
                  && (m.State == Mechanic.StateLeave || m.Mode == Mechanic.ModeLeave),
                  $"install: then 0x11 opens it (7 -> 10, flag and VAR_BREAKSTAT clear, unassigned) and he walks out (status {r.Status}, his state 0x{m.State:X})");
        }
    }

    // =============================================================================================
    // The queue: event 7 sends queued guests back out, with the ordinary Life rule.

    static void Ejection(Fixture f, Action<bool, string> Check)
    {
        var p = f.NewPark(70);
        var r = f.Open(p, p.At(2, 4));
        var guests = new List<Guest>();
        var above = r.Entrance.Value.Offset(0, -1);          // the corridor cell over the stub: all three arrive together
        for (int i = 0; i < 3; i++)
        {
            var g = p.Visitors.Arrive(above, above);
            if (p.Visitors.SendTo(g, r)) guests.Add(g);
        }
        // All three join in one tick; the next offers the first LETMEON and the others stand in the queue.
        for (int t = 0; t < 600 && r.Queue.Count < guests.Count - 1; t++) p.Step();
        var queuedIds = r.Queue.ToList();
        p.Sim.SetRideStatus(r, 4);                           // Life > 0: the ordinary enter 4 keeps them
        int after4 = r.Queue.Count;
        p.Sim.SetRideStatus(r, 5);                           // 0x1B8BF0: emptied
        p.Step();
        int out5 = queuedIds.Count(id => p.Visitors.Plans.TryGetValue(id, out var plan) && plan.Intent != VisitorIntent.Queued);
        Check(queuedIds.Count >= 2 && after4 == queuedIds.Count && r.Queue.Count == 0 && out5 == queuedIds.Count,
              $"queue: an ordinary ride's enter 4 keeps its queue while Life > 0 ({after4} of {queuedIds.Count}); enter 5 empties it with "
              + $"event 7 and those {out5} guests walk back out (0x1B8C28 / 0x1B8BF0)");
        var p2 = f.NewPark(71);
        var r2 = f.Open(p2, p2.At(2, 4));
        for (int g = 1; g <= 3; g++) r2.Join(9400 + g);
        r2.Life = 0;
        p2.Sim.SetRideStatus(r2, 4);
        Check(r2.Queue.Count == 0 && r2.Ejected.Count == 3,
              $"queue: with Life < 1 the ordinary enter 4 empties it too ({r2.Ejected.Count} sent out)");
    }

    // =============================================================================================
    // The work cell, against the port's own placement rule, in all four rotations.

    static void WorkCells(Asset asset, Action<bool, string> Check)
    {
        var e = asset.Record;
        var def = asset.Definition();
        int ok = 0;
        var cells = new List<string>();
        for (int turns = 0; turns < 4; turns++)
        {
            int w = turns % 2 == 0 ? e.Width : e.Depth, h = turns % 2 == 0 ? e.Depth : e.Width;
            var ride = new ParkRide { Id = 1, Origin = new ParkCell(50, 50), Width = w, Height = h, Definition = def, PlacementTurns = turns };
            var cell = ParkSim.WorkCell(ride);
            cells.Add($"t{turns}:{cell}");
            // ShopEntrance.Connection returns the inside cell only when inside + the rotated door direction IS the approach.
            if (ShopEntrance.Connection(e, ride.Origin, turns, w, h, cell) is { } inside && inside == PlacedDestination.Entry(ride)) ok++;
        }
        Check(ok == 4, $"work cell: 0x116EC0's step out of the door (native rotation = (4 - turns) & 3) is the port's own placement stub in all four rotations "
                       + $"({ok} of 4: {string.Join(" ", cells)})");
    }

    // =============================================================================================
    // The track ride class: wear gate and amount, breakdown from any status, cars removed on 6.

    static void TrackClass(ParkPaths paths, WadArchive world, string worldName, CompiledAssets compiled, Action<bool, string> Check)
    {
        static string Dir(string p) => p[..(p.LastIndexOf('/') + 1)];
        var assets = Assets(world, worldName, compiled, r => r.Kind == AssetResourceDatabase.AssetKind.TrackRide);
        var asset = assets.FirstOrDefault(a => world.Entries.Any(m => Dir(m.Path).Equals(Dir(a.Path), StringComparison.OrdinalIgnoreCase)
                                                                   && m.Name.EndsWith("_trcks.mps", StringComparison.OrdinalIgnoreCase)));
        if (asset == null) { Check(false, $"{worldName}: no track ride with a compiled record beside a *_trcks.mps"); return; }
        (ParkSim Sim, ParkRide Ride, List<(int Id, ParkRide R)> Adv) Make(bool karts = true)
        {
            var sim = new ParkSim(paths);
            var adv = new List<(int, ParkRide)>();
            sim.Advisor = (id, r) => adv.Add((id, r));
            var station = TrackRideChecks.Station;
            var ride = sim.Add(1, asset.Stem, station, 4, 3, asset.Script, null, 4, station.Offset(-1, 2), station.Offset(4, 2), out var fault,
                               sibling: asset.Sibling, definition: asset.Definition());
            if (ride == null) throw new InvalidOperationException($"{asset.Stem}: {fault}");
            sim.SetOpen(1, true);
            sim.AttachTrack(1, TrackRideChecks.Loop(0, 1, new TrackGround { World = 0, Park = 0 }), seed: 5, karts: karts);
            return (sim, ride, adv);
        }
        var t0 = asset.Record.Tier(0);
        {
            var (sim, ride, _) = Make();
            for (int g = 1; g <= 40; g++) ride.Join(9500 + g);
            int wears = 0, wrong = 0, loadingTicks = 0, loadingWear = 0;
            for (int t = 0; t < 4000; t++)
            {
                int rel = ride.Reliability, riders = ride.Track.Riders, cars = ride.Track.Cars.Count;
                var status = ride.Track.Status;
                sim.Advance(ParkSim.TickMilliseconds / 1000.0);
                uint tick = sim.Tick;
                int loss = rel - ride.Reliability;
                bool gate = (tick & 3) == 0 && cars > 0 && status != TrackRideStatus.Loading;
                int expected = gate ? Math.Min(rel, NativeRideReliability.TrackWear(t0.MinSpeedDamage, t0.MinCapacityDamage, t0.WearRate,
                                          ride.Track.Speed, riders, t0.CapacityParameter, ride.Track.Track.Pieces.Count) >> 5) : 0;
                if (loss != expected) wrong++;
                if (loss > 0) wears++;
                if (status == TrackRideStatus.Loading) { loadingTicks++; if (loss > 0) loadingWear++; }
                ride.ClearLeft();
            }
            Check(wears > 20 && wrong == 0 && loadingTicks > 0 && loadingWear == 0,
                  $"track wear: {asset.Stem} loses TrackWear(..., {ride.Track.Track.Pieces.Count} pieces) >> 5 on tick & 3 == 0 while cars exist "
                  + $"and it is not loading (0x2023B0) -- {wears} wears, {wrong} off the rule, {loadingWear} of {loadingTicks} loading ticks worn");
        }
        {
            var (sim, ride, adv) = Make();
            sim.SetOpen(1, false);
            ride.ForceReliabilityForTest(0x9000);
            for (int g = 1; g <= 3; g++) ride.Join(9600 + g);
            sim.Advance(ParkSim.TickMilliseconds / 1000.0);
            Check(ride.Status == 4 && ride.Queue.Count == 0 && ride.Ejected.Count == 3 && !adv.Any(a => a.Id == ParkSim.AdvisorBreakdownImminent),
                  $"track breakdown: below 10.0 a CLOSED (3) track ride is forced to 4 (0x200358, any status), its queue emptied and no 0x36 (0x2002D8) -- status {ride.Status}");
            sim.Advance(ParkSim.TickMilliseconds / 1000.0);
            Check(ride.Status == 4 && ride.ServiceFlag, "track breakdown: every update below 10.0 re-enters 4 and keeps the service flag set");
        }
        {
            // Enter 6 removes every car at once, riders put off; a repair of a ride still below 10.0 bounces to 4.
            var (sim, ride, _) = Make();
            for (int g = 1; g <= 4; g++) ride.Join(9700 + g);
            for (int t = 0; t < 2000 && !(ride.Track.Status == TrackRideStatus.Running && ride.Track.Cars.Count == 4); t++)
                sim.Advance(ParkSim.TickMilliseconds / 1000.0);
            int cars = ride.Track.Cars.Count;
            ride.ClearLeft();
            sim.SetRideStatus(ride, 6);
            int off = ride.Left.Count;
            ride.ForceReliabilityForTest(0x9000);
            sim.Advance(ParkSim.TickMilliseconds / 1000.0);
            Check(cars == 4 && off == 4 && ride.Track.Cars.Count == 0 && ride.Status == 4,
                  $"track 6: entering 6 removes all {cars} cars at once and puts {off} riders off (0x200310); below 10.0 the next update forces 4 again (0x200358)");
            sim.SetRideStatus(ride, 7);
            Check(ride.Status == 10 && ride.Reliability == ParkSim.FullReliability,
                  $"track 7: the parent 0x116660 -- 2, 100.0, then 10 (status {ride.Status})");
        }
    }

    // =============================================================================================
    // The coaster class: wear in 10 with riders and every 4 tick, 2/10-only breakdown, frozen in 5.

    static void CoasterClass(ParkPaths paths, WadArchive world, string worldName, CompiledAssets compiled, Action<bool, string> Check)
    {
        static string Dir(string p) => p[..(p.LastIndexOf('/') + 1)];
        var assets = Assets(world, worldName, compiled, r => r.Kind == AssetResourceDatabase.AssetKind.Coaster);
        var asset = assets.FirstOrDefault(a => world.Entries.Any(m => Dir(m.Path).Equals(Dir(a.Path), StringComparison.OrdinalIgnoreCase)
                                                                   && m.Name.Equals("stdpylon.mps", StringComparison.OrdinalIgnoreCase)));
        if (asset == null) { Check(false, $"{worldName}: no coaster with a compiled record beside a stdpylon.mps"); return; }
        string folder = Dir(asset.Path).TrimEnd('/'); folder = folder[(folder.LastIndexOf('/') + 1)..];
        var type = CoasterType.ForFolder(folder);
        if (type == null) { Check(false, $"{worldName}: {folder} is not in the coaster table"); return; }
        (ParkSim Sim, ParkRide Ride, List<(int, ParkRide)> Adv) Make()
        {
            var sim = new ParkSim(paths);
            var adv = new List<(int, ParkRide)>();
            sim.Advisor = (id, r) => adv.Add((id, r));
            var at = new ParkCell(36, 40);
            var ride = sim.Add(1, asset.Stem, at, 4, 3, asset.Script, null, 4, at.Offset(1, -1), at.Offset(2, -1), out var fault,
                               sibling: asset.Sibling, definition: asset.Definition());
            if (ride == null) throw new InvalidOperationException($"{asset.Stem}: {fault}");
            sim.SetOpen(1, true);
            var t = CoasterChecks.Build(type, null, out _);
            sim.AttachCoaster(1, t);
            t.AddPylon(CoasterChecks.EntryCell, 0, 0, false, CoasterNodeKind.Normal);
            sim.Advance(ParkSim.TickMilliseconds / 1000.0);
            return (sim, ride, adv);
        }
        var t0 = asset.Record.Tier(0);
        int maxCap = type.CarsPerTrain * type.Seats * 6;
        {
            var (sim, ride, _) = Make();
            for (int g = 1; g <= 30; g++) ride.Join(9800 + g);
            int wears = 0, wrong = 0, emptyOpen = 0, emptyWear = 0;
            for (int t = 0; t < 3000; t++)
            {
                int rel = ride.Reliability;
                sim.Advance(ParkSim.TickMilliseconds / 1000.0);
                uint tick = sim.Tick;
                int riders = ride.Coaster.Riders, status = ride.Coaster.Status;
                int loss = rel - ride.Reliability;
                // 0x122BB8 wears once in 4 whatever is aboard, then 0x122AF8 once more with riders on a closed ring.
                int once = NativeRideReliability.Wear(t0.MinSpeedDamage, t0.MinCapacityDamage, t0.WearRate, ride.Speed, riders, maxCap) >> 5;
                int calls = (tick & 3) != 0 ? 0 : (status == 4 ? 1 : 0) + (status is 2 or 10 or 4 && ride.Coaster.Track.Closed && riders != 0 ? 1 : 0);
                int expected = Math.Min(rel, once * calls);
                if (loss != expected) wrong++;
                if (loss > 0) wears++;
                if (riders == 0 && status is 2 or 10 && (tick & 3) == 0) { emptyOpen++; if (loss > 0) emptyWear++; }
                ride.ClearLeft();
            }
            Check(wears > 10 && wrong == 0 && emptyOpen > 0 && emptyWear == 0,
                  $"coaster wear: {type.Name} (MaxCap {maxCap} = cars x seats x 6) wears rate(0) >> 5 on status-10 ticks with riders aboard "
                  + $"(0x122AF8) -- {wears} wears, {wrong} off the rule, {emptyWear} of {emptyOpen} empty on-phase ticks worn");
        }
        {
            // Status 4 wears every phase tick even empty, TWICE with riders.
            var (sim, ride, adv) = Make();
            ride.ForceReliabilityForTest(0x9800);
            int[] losses = new int[8];
            for (int t = 0; t < 8; t++)
            {
                int rel = ride.Reliability;
                sim.Advance(ParkSim.TickMilliseconds / 1000.0);
                losses[t] = rel - ride.Reliability;
            }
            int empty = NativeRideReliability.Wear(t0.MinSpeedDamage, t0.MinCapacityDamage, t0.WearRate, ride.Speed, 0, maxCap) >> 5;
            Check(ride.Status == 4 && losses.Count(l => l == empty) == 2 && losses.Count(l => l == 0) == 6
                  && adv.Count(a => a.Item1 == ParkSim.AdvisorBreakdownImminent) == 1 && ride.Queue.Count == 0,
                  $"coaster 4: below 10.0 from 2 it breaks (0x1228D0), posts 0x36 once and empties its queue (0x1229A0); then every phase tick "
                  + $"wears even with nobody aboard ({string.Join(",", losses)}; empty rate >> 5 = {empty}) (0x122BB8)");
        }
        {
            var (sim, ride, _) = Make();
            sim.SetOpen(1, false);
            ride.ForceReliabilityForTest(0x5000);
            sim.Advance(ParkSim.TickMilliseconds / 1000.0);
            byte closed = ride.Status;
            sim.SetOpen(1, true);
            sim.Advance(ParkSim.TickMilliseconds / 1000.0);
            Check(closed == 3 && ride.Status == 4, $"coaster breakdown: only from 2 or 10 -- closed (3) below 10.0 stays {closed}, reopened it breaks ({ride.Status})");
            {
                // ...and from 10 (a train boarding): 0x1228D0 widens the parent's status-2 test to 10.
                var (sim10, ride10, _) = Make();
                for (int g = 1; g <= 6; g++) ride10.Join(9900 + g);
                for (int t = 0; t < 2000 && ride10.Status != 10; t++) sim10.Advance(ParkSim.TickMilliseconds / 1000.0);
                byte loading = ride10.Status;
                ride10.ForceReliabilityForTest(0x5000);
                sim10.Advance(ParkSim.TickMilliseconds / 1000.0);
                Check(loading == 10 && ride10.Status == 4,
                      $"coaster breakdown: a coaster in 10 (boarding) below 10.0 breaks too ({loading} -> {ride10.Status}) -- the ordinary check would not");
            }
            // 5 freezes every train; 6 lets them run.
            sim.SetRideStatus(ride, 5);
            var before = ride.Coaster.Trains.Select(tr => tr.Pos).ToArray();
            for (int t = 0; t < 20; t++) sim.Advance(ParkSim.TickMilliseconds / 1000.0);
            bool frozen = ride.Coaster.Trains.Select(tr => tr.Pos).SequenceEqual(before);
            sim.SetRideStatus(ride, 6);
            for (int t = 0; t < 20; t++) sim.Advance(ParkSim.TickMilliseconds / 1000.0);
            bool moved = !ride.Coaster.Trains.Select(tr => tr.Pos).SequenceEqual(before);
            Check(before.Length > 0 && frozen && moved && ride.Status == 6,
                  $"coaster 5/6: in 5 all {before.Length} trains are frozen in place (0x1238C0); in 6 they run again (enter 6 is flag-only)");
        }
    }
}
