using System.Buffers.Binary;
using System.Text;
using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

/// <summary>⭐⭐ STAFF, STEPS 1 AND 2: the staff person and the handyman, run over the real disc and
/// a real park (this world's terrain, with a path block laid into ground the park leaves clear, and
/// the world's own toilet, staff room and bin placed from their compiled records).
///
/// Every native number is checked against the EXECUTABLE at its address, not against a copy of it;
/// every behaviour is checked through <see cref="ParkStaff.Update"/> or <see cref="ParkVisitors.Step"/>
/// -- the paths the viewer will use -- never by calling a handler directly. Every "all of them"
/// verdict prints its count. The teeth (a mutated rule turning each check red) are exercised by the
/// staff mutation runner described in the commit; each check below names the rule it guards.</summary>
static class StaffChecks
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

    sealed record Asset(string Stem, AssetResourceDatabase.Entry Record, byte[] Script, byte[] Aps, Func<string, byte[]> Sibling,
                        Func<RideDefinition> Definition);

    sealed class Park
    {
        public ParkPaths Paths; public ParkSim Sim; public GuestWalk Walk; public ParkVisitors Visitors;
        public ParkStaff Staff; public ParkClock Clock; public TestRandom Rng; public NativeActivationSequence Serials;
        public ParkCell B;
        public int NextId = 100;
        public ParkCell At(int dx, int dz) => B.Offset(dx, dz);
        public void Tick(int n = 1) { for (int i = 0; i < n; i++) Staff.Update(); }
    }

    static Point Centre(ParkCell c) => new((short)(c.X * 256 + 0x80), (short)(c.Z * 256 + 0x80));

    public static void Run(Disc disc, Model terrain, WadArchive data, WadArchive world, string worldName,
                           Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "staff: " + label);
        var exe = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        byte[] elf = disc.Read(exe.Extent, exe.Size);

        Tables(elf, Check);
        Candidates(Check);

        // ---- the park -------------------------------------------------------------------------
        var compiled = new CompiledAssets(new AssetResourceDatabase(data.Read(data.Find("/arsdb.dba"))),
                                          TextDatabase.Load(data, "eur"));
        Asset Find(Func<AssetResourceDatabase.Entry, bool> want, string what)
        {
            foreach (var e in world.Entries.Where(e => e.Path.EndsWith(".sam", StringComparison.OrdinalIgnoreCase))
                                           .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
            {
                var rec = compiled.For(worldName, e.Path);
                if (rec?.Kind != AssetResourceDatabase.AssetKind.Feature || !want(rec)) continue;
                string stem = e.Path[..^4], folder = stem[..(stem.LastIndexOf('/') + 1)];
                var rse = world.Find(stem + ".rse"); if (rse == null) continue;
                var aps = world.Find(stem + ".aps");
                string sam = Encoding.ASCII.GetString(world.Read(e));
                RideDefinition Definition()
                {
                    var def = RideDefinition.Parse(sam, "/DATA/" + worldName + ".WAD" + e.Path);
                    compiled.Attach(new[] { def }, out _);
                    return def;
                }
                return new Asset(stem, rec, world.Read(rse), aps == null ? null : world.Read(aps),
                                 n => world.Find(folder + n) is { } s ? world.Read(s) : null, Definition);
            }
            throw new InvalidDataException($"{worldName}: no {what} feature with a script");
        }
        var toiletAsset = Find(r => (r.RawFeatureFlags & 1) != 0 && r.Width == 1 && r.Depth == 1 && r.ConnectionA.IsPresent
                                    && r.ConnectionA.Direction == 0, "1x1 toilet");
        var roomAsset = Find(r => (r.RawFeatureFlags & 2) != 0 && r.ConnectionA.IsPresent && r.ConnectionA.Direction == 0
                                  && r.Width <= 2 && r.Depth <= 2, "staff room");
        var binAsset = Find(r => (r.RawFeatureFlags & 4) != 0 && r.Width == 1 && r.Depth == 1, "bin");
        Check(true, $"{worldName} fixtures: toilet {toiletAsset.Stem} (key {toiletAsset.Record.Key}), staff room {roomAsset.Stem} "
                  + $"(key {roomAsset.Record.Key}, {roomAsset.Record.Width}x{roomAsset.Record.Depth}), bin {binAsset.Stem}");

        var paths = new ParkPaths(terrain);
        var entranceTable = ParkEntrance.ReadExecutable(elf);
        paths.SetEntrance(entranceTable);
        int pathMaterial = Enumerable.Range(1, paths.Materials.Count - 1).First(i => ParkPaths.Classify(paths.Materials[i]) == ParkPathKind.Path);
        int queueMaterial = Enumerable.Range(1, paths.Materials.Count - 1).First(i => ParkPaths.Classify(paths.Materials[i]) == ParkPathKind.Queue);
        // The path the park starts with (0x14E5B0 → 0x15F4C0), laid as the viewer lays it: the strike
        // point 0x1497C0 is on its first row.
        int starting = 0;
        if (paths.EntranceEntry is { } entry)
            foreach (var (x, z) in entry.StartingPath())
                if (paths.CanLay(new ParkCell(x, z)) && paths.Kind(new ParkCell(x, z)) == ParkPathKind.None) { paths.Lay(new ParkCell(x, z), pathMaterial); starting++; }
        // A 32x11 block of buildable ground with no path, queue or entrance on it: the park's own, found.
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
        Check(origin != null, $"{worldName}: the park has a clear 32x11 block to build the staff fixture in ({origin}; {starting} starting-path cells laid)");
        if (origin == null) return;
        var B = origin.Value.Offset(1, 1);           // one cell of margin
        // The corridor: row +2, 30 cells. A spur at x+10 one row down. An island two rows below the far end.
        for (int x = 0; x < 30; x++) paths.Lay(B.Offset(x, 2), pathMaterial);
        paths.Lay(B.Offset(10, 3), pathMaterial);
        paths.Lay(B.Offset(28, 6), pathMaterial);
        paths.Lay(B.Offset(1, 6), queueMaterial);

        Park NewPark(int seed = 1)
        {
            var sim = new ParkSim(paths);
            var walk = new GuestWalk(paths);
            var rng = new TestRandom(seed);
            var p = new Park { Paths = paths, Sim = sim, Walk = walk, Rng = rng, B = B, Clock = new ParkClock(),
                               Serials = new NativeActivationSequence(1000, "staff checks fixture") };
            p.Visitors = new ParkVisitors(sim, walk, () => 0);
            p.Staff = new ParkStaff(p.Visitors, p.Clock, p.Serials, rng.Next);
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
        void AdvanceDays(ParkClock clock, int days) { for (int i = 0; i < days; i++) clock.Advance(ParkClock.UnitsPerDay, out _, out _, out _); }
        StaffMember HireAt(Park p, StaffKind kind, ParkCell cell, int? level = null)
        {
            var slot = p.Staff.Candidates.Available(kind).First().Slot;
            var m = p.Staff.Hire(kind, slot);
            if (level is int l) m.Level = l;
            if (!p.Staff.Drop(m, cell)) throw new InvalidOperationException($"drop refused at {cell}");
            return m;
        }

        Pools(NewPark, Check);
        Activation(NewPark, AdvanceDays, Check);
        DropRule(NewPark, Place, roomAsset, Check);
        RouteService(NewPark, HireAt, Check);
        Sweep(NewPark, HireAt, Check);
        Toilet(NewPark, Place, toiletAsset, HireAt, AdvanceDays, Check);
        ToiletScore(NewPark, Place, toiletAsset, HireAt, Check);
        WalkTiredness(NewPark, HireAt, Check);
        Rest(NewPark, Place, roomAsset, HireAt, Check);
        NoRoom(NewPark, Place, roomAsset, HireAt, Check);
        Wander(NewPark, HireAt, Check);
        Patrol(NewPark, HireAt, Check);
        UpdateOrder(NewPark, HireAt, Check);
        Strike(NewPark, HireAt, entranceTable, Check);
        Lifecycle(NewPark, Place, toiletAsset, HireAt, AdvanceDays, Check);
        GuestLitter(NewPark, Place, toiletAsset, binAsset, Check);
        StandIn(NewPark, Place, toiletAsset, Check);
        LaserShow(world, worldName, compiled, Check);
    }

    // =============================================================================================
    // The tables, read back out of the executable at their addresses.

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
        int[] Words(uint va, int n, int stride = 4) => Enumerable.Range(0, n).Select(i => (int)U32(At(va + (uint)(i * stride)))).ToArray();
        int[] Halves(uint va, int n, int stride) => Enumerable.Range(0, n).Select(i => U16(At(va + (uint)(i * stride)))).ToArray();
        string S(IEnumerable<int> v) => string.Join(",", v);
        bool Same(IEnumerable<int> a, IEnumerable<int> b) => a.SequenceEqual(b);

        var sweep = Words(StaffTables.HandymanTable, 5, 12);
        var toilet = Words(StaffTables.HandymanTable + 4, 5, 12);
        var hspeed = Words(StaffTables.HandymanTable + 8, 5, 12);
        Check(Same(sweep, StaffTables.HandymanSweepTicks) && Same(toilet, StaffTables.HandymanToiletTicks) && Same(hspeed, StaffTables.HandymanSpeed),
              $"tables: handyman rows at 0x35FBD8 (sweep {S(sweep)}; toilet {S(toilet)}; speed {S(hspeed)}) are the port's");
        var mtime = Halves(StaffTables.MechanicTable, 5, 4); var mspeed = Halves(StaffTables.MechanicTable + 2, 5, 4);
        Check(Same(mtime, StaffTables.MechanicWorkTicks) && Same(mspeed, StaffTables.MechanicSpeed),
              $"tables: mechanic u16 rows at 0x3627C8 (work {S(mtime)}; speed {S(mspeed)}) are the port's");
        var research = Words(StaffTables.ResearcherWorkTable, 5);
        Check(Same(research, StaffTables.ResearcherWork), $"tables: researcher work at 0x366150 ({S(research)})");
        var wage = Words(StaffTables.WageTable, 6); var wmul = Words(StaffTables.WageMultiplierTable, 5);
        var train = Words(StaffTables.TrainingTable, 6); var tmul = Words(StaffTables.TrainingMultiplierTable, 5);
        Check(Same(wage, StaffTables.WageBase) && Same(wmul, StaffTables.WageMultiplier),
              $"tables: wage 0x35C410 ({S(wage)}) x kind 0x35C428 ({S(wmul)})");
        Check(Same(train, StaffTables.TrainingBase) && Same(tmul, StaffTables.TrainingMultiplier),
              $"tables: training 0x35C440 ({S(train)}) x kind 0x35C458 ({S(tmul)})");
        int namesOk = 0;
        foreach (StaffKind k in Enum.GetValues<StaffKind>())
            if (Same(Words(StaffTables.CandidateNameTable(k), 5), StaffTables.CandidateNameRows(k))) namesOk++;
        Check(namesOk == 5, $"tables: all candidate name-row tables 0x35C220..0x35C280 match ({namesOk} of 5 kinds)");
        var dirs = Enumerable.Range(0, 4).Select(d => ((short)U16(At(StaffTables.DirectionTable + (uint)d * 8)),
                                                       (short)U16(At(StaffTables.DirectionTable + (uint)d * 8 + 4)))).ToArray();
        var bits = Enumerable.Range(0, 4).Select(d => (int)elf[At(StaffTables.DirectionBitsTable + (uint)d)]).ToArray();
        var weights = Words(StaffTables.WanderWeightTable, 16);
        Check(dirs.Select(d => ((int)d.Item1, (int)d.Item2)).SequenceEqual(StaffTables.Directions)
              && Same(bits, StaffTables.DirectionBits.Select(b => (int)b))
              && Same(weights, Enumerable.Range(0, 16).Select(i => StaffTables.WanderWeights[i / 4, i % 4])),
              $"tables: wander directions 0x364A18, link bits 0x364818 ({S(bits)}) and weights 0x364A38 ({S(weights)})");

        // The formulas, against the findings' own worked tables (staff-management.md §6.1, §7.2).
        int[][] wages = { new[] { 150, 165, 195, 240, 300 }, new[] { 50, 55, 65, 80, 100 }, new[] { 50, 55, 65, 80, 100 },
                          new[] { 100, 110, 130, 160, 200 }, new[] { 150, 165, 195, 240, 300 } };
        int[][] training = { new[] { 750, 825, 975, 1200 }, new[] { 250, 275, 325, 400 }, new[] { 250, 275, 325, 400 },
                             new[] { 500, 550, 650, 800 }, new[] { 1500, 1650, 1950, 2400 } };
        int wageCells = 0, trainCells = 0;
        foreach (StaffKind k in Enum.GetValues<StaffKind>())
        {
            for (int l = 0; l <= 4; l++) if (StaffTables.MonthlyWage(k, l) == wages[(int)k][l]) wageCells++;
            for (int l = 0; l < 4; l++) if (StaffTables.TrainingCost(k, l) == training[(int)k][l] && StaffTables.TrainingCost(k, l, true) == 0) trainCells++;
        }
        Check(wageCells == 25, $"wages: W[L] x M[kind] gives the findings' table in every cell ({wageCells} of 25)");
        Check(trainCells == 20, $"training: T[L] x N[kind] gives the findings' table, 0 in free-build ({trainCells} of 20)");
        Check(StaffTables.ProRatedWage(StaffKind.Handyman, 0, false, 15, 30) == 25
              && StaffTables.ProRatedWage(StaffKind.Handyman, 0, false, 30, 30) == 50
              && StaffTables.ProRatedWage(StaffKind.Handyman, 0, false, 40, 28) == 50
              && StaffTables.ProRatedWage(StaffKind.Mechanic, 4, false, 7, 31) == 66
              && StaffTables.ProRatedWage(StaffKind.Mechanic, 4, true, 40, 31) == 0,
              "wages: 0x1DC338 pro-rates by days/previous-month (15/30 -> 25, 7/31 of 300 -> 66), full at >= a month, 0 standing on strike");
        Check(StaffTables.EntertainerCostumeVariant(0) == 1 && StaffTables.EntertainerCostumeVariant(1) == 2
              && StaffTables.EntertainerCostumeVariant(2) == null,
              "costume: 0x17D7E8 matches variant park+1 -- park 0 wears 1, park 1 wears 2, park 2 none");
    }

    static void Candidates(Action<bool, string> Check)
    {
        var rng = new Random(9);
        var db = new StaffCandidateDatabase(n => rng.Next(n));
        int grades0 = db.All.Count(c => c.PayGrade == 0), grades1 = db.All.Count(c => c.PayGrade == 1);
        int minM = db.All.Min(c => c.Motivation), maxM = db.All.Max(c => c.Motivation);
        bool perKind = Enum.GetValues<StaffKind>().All(k => Enumerable.Range(0, 5).All(s =>
            db.For(k, s).Kind == k && db.For(k, s).Slot == s && db.For(k, s).NameRow == StaffTables.CandidateNameRows(k)[s]
            && db.For(k, s).Available));
        Check(db.All.Count == 25 && perKind && grades0 + grades1 == 25 && minM >= 20 && maxM <= 79,
              $"candidates: 25 records, 5 per kind with their name rows, pay grade 0/1 ({grades0}/{grades1}), motivation {minM}..{maxM} within 20..79");
        // Draw order, READ from 0x12B5B8: pay grade FIRST (rand & 1), motivation SECOND (rand % 60 + 20).
        var script = new Queue<int>(new[] { 1, 7, 0, 59 });
        var ordered = new StaffCandidateDatabase(n => script.Count > 0 ? script.Dequeue() % n : 0);
        Check(ordered.For(StaffKind.Mechanic, 0).PayGrade == 1 && ordered.For(StaffKind.Mechanic, 0).Motivation == 27
              && ordered.For(StaffKind.Mechanic, 1).PayGrade == 0 && ordered.For(StaffKind.Mechanic, 1).Motivation == 79,
              "candidates: 0x12B5B8 draws the pay grade before the motivation (scripted draws 1,7,0,59 -> grade1/27, grade0/79)");
    }

    // =============================================================================================
    // Step 1: pools, activation, placement, routes.

    static void Pools(Func<int, Park> newPark, Action<bool, string> Check)
    {
        var p = newPark(3);
        var hired = new List<StaffMember>();
        for (int i = 0; i < 5; i++) hired.Add(p.Staff.Hire(StaffKind.Handyman, i));
        Check(hired.Select(m => m.PoolSlot).SequenceEqual(new[] { 4, 3, 2, 1, 0 }),
              $"pools: slots are pushed on the free list at the head, so hires take slots {string.Join(",", hired.Select(m => m.PoolSlot))} (4 first)");
        Check(p.Staff.Active(StaffKind.Handyman).Select(m => m.PoolSlot).SequenceEqual(new[] { 0, 1, 2, 3, 4 })
              && p.Staff.Members.SequenceEqual(Enumerable.Reverse(hired)),
              $"pools: the active list and the map-object list are newest first ({p.Staff.Count(StaffKind.Handyman)} active)");
        Check(p.Staff.Hire(StaffKind.Handyman, 0) == null && !p.Staff.CanHire(StaffKind.Handyman) && p.Staff.Count(StaffKind.Handyman) == 5,
              "pools: a sixth handyman is refused and the Hire tab predicate (5 - count > 0) goes false");
        var middle = hired.Single(m => m.PoolSlot == 2);
        p.Staff.Fire(middle);
        Check(p.Staff.Free(StaffKind.Handyman).Count == 1 && p.Staff.Free(StaffKind.Handyman)[0] == middle
              && p.Staff.Count(StaffKind.Handyman) == 4 && !p.Staff.Members.Contains(middle) && middle.Candidate.Available,
              "pools: firing returns the slot to the FREE-LIST HEAD, unlinks it and hands the candidate back");
        var second = hired.Single(m => m.PoolSlot == 0);
        p.Staff.Fire(second);
        Check(p.Staff.Free(StaffKind.Handyman).Select(m => m.PoolSlot).SequenceEqual(new[] { 0, 2 }),
              $"pools: a second fire pushes in FRONT of the first (free list {string.Join(",", p.Staff.Free(StaffKind.Handyman).Select(m => m.PoolSlot))}, last freed first)");
        var again = p.Staff.Hire(StaffKind.Handyman, second.CandidateSlot);
        Check(again == second && p.Staff.Active(StaffKind.Handyman)[0] == second && p.Staff.Members[0] == second,
              "pools: the next hire reuses the LAST freed slot and puts it at the head of both lists");
        p.Staff.Hire(StaffKind.Handyman, middle.CandidateSlot);
        var mech = p.Staff.Hire(StaffKind.Mechanic, 0);
        Check(p.Staff.Members[0] == mech && p.Staff.Active(StaffKind.Handyman)[0] == middle && p.Staff.Members[1] == middle,
              "pools: the map-object list spans every type (a later mechanic is updated before the handymen)");
        int total = 0;
        foreach (StaffKind k in Enum.GetValues<StaffKind>())
            for (int s = 0; s < 5; s++) if (p.Staff.Hire(k, s) != null) total++;
        int all = Enum.GetValues<StaffKind>().Sum(k => p.Staff.Count(k));
        Check(all == 25 && p.Staff.Members.Count == 25 && Enum.GetValues<StaffKind>().All(k => p.Staff.Count(k) == 5),
              $"pools: five pools of five -- every type fills to 5 and no further ({all} hired, {p.Staff.Members.Count} map objects)");
    }

    static void Activation(Func<int, Park> newPark, Action<ParkClock, int> advanceDays, Action<bool, string> Check)
    {
        var p = newPark(11);
        advanceDays(p.Clock, 23);
        int minT = 99, maxT = -1, minM = 999, maxM = -1, good = 0, n = 300;
        ulong before = p.Serials.Activations;
        for (int i = 0; i < n; i++)
        {
            var kind = (StaffKind)(i % 5);
            var slot = p.Staff.Candidates.Available(kind).First().Slot;
            var m = p.Staff.Hire(kind, slot);
            minT = Math.Min(minT, m.Tiredness); maxT = Math.Max(maxT, m.Tiredness);
            minM = Math.Min(minM, m.Morale); maxM = Math.Max(maxM, m.Morale);
            if (m.Held && m.Shown && m.LogicalRequest == StaffTables.LogicalCarry && m.SpeedBits == 15
                && m.Level == m.Candidate.PayGrade && m.MotivationCopy == m.Candidate.Motivation && m.HireDay == 23
                && !m.HasPatrolArea && m.Mode == StaffMember.ModeIdle && m.State == 0 && m.GoalDepth == 0 && m.RouteSlot == -1
                && m.CandidateSlot == slot && !m.Candidate.Available)
                good++;
            p.Staff.Fire(m);
        }
        Check(minT == 0 && maxT == 29 && minM == 70 && maxM == 99,
              $"activation: over {n} hires tiredness spans exactly rand(30) = {minT}..{maxT} and morale 70+rand(30) = {minM}..{maxM}");
        Check(good == n, $"activation: every hire is held, shown, logical 18, speed 15, level = pay grade, hire day = today, patrol unset, state 0 ({good} of {n})");
        Check(p.Serials.Activations - before == (ulong)n, $"activation: each hire consumes exactly one shared activation serial ({p.Serials.Activations - before} for {n})");
        // Order: MORALE is drawn first, then tiredness (0x1DB618).
        var q = newPark(12);
        var draws = new Queue<int>(new[] { 5, 17 });
        q.Rng.Override = k => k == 30 && draws.Count > 0 ? draws.Dequeue() : null;
        var first = q.Staff.Hire(StaffKind.Guard, 0);
        Check(first.Morale == 75 && first.Tiredness == 17, $"activation: morale takes the first rand(30) and tiredness the second (5,17 -> morale {first.Morale}, tiredness {first.Tiredness})");
        var h = q.Staff.Hire(StaffKind.Handyman, 0);
        var mc = q.Staff.Hire(StaffKind.Mechanic, 0);
        var speeds = Enumerable.Range(0, 5).Select(l => { h.Level = l; mc.Level = l; return (h.Speed, mc.Speed); }).ToArray();
        Check(speeds.Select(s => s.Item1).SequenceEqual(new[] { 10, 15, 20, 20, 18 }) && speeds.Select(s => s.Item2).SequenceEqual(new[] { 9, 12, 14, 16, 18 })
              && first.Speed == 15,
              $"speed: vt+0x18C is the handyman's table ({string.Join(",", speeds.Select(s => s.Item1))}), the mechanic's ({string.Join(",", speeds.Select(s => s.Item2))}), else the 15 speed bits");
        h.Tiredness = 30; h.Morale = 80; int even = h.DisplayedMotivation;
        h.Tiredness = 31; h.Morale = 80; int odd = h.DisplayedMotivation;
        Check(even == 75 && odd == 74, $"motivation: 0x1DC428 shows ((100-t)+m)/2, truncated: t30/m80 -> {even}, t31/m80 -> {odd}");
        h.Level = 1; h.Tiredness = 50; h.Morale = 10; h.Train(2);
        bool up = h.Level == 2 && h.Tiredness == 0 && h.Morale == 100;
        h.Tiredness = 50; h.Morale = 10; h.Train(1);
        bool down = h.Level == 1 && h.Tiredness == 50 && h.Morale == 10;
        h.Train(1);
        Check(up && down && h.Level == 1 && h.Tiredness == 50 && h.Morale == 10,
              "training: 0x1DC968 resets tiredness 0 / morale 100 only when the level RISES (not down, not the same level)");
    }

    static void DropRule(Func<int, Park> newPark, Func<Park, Asset, ParkCell, ParkRide> place, Asset room, Action<bool, string> Check)
    {
        var p = newPark(4);
        var roomRide = place(p, room, p.At(2, 3));
        var entry = StaffFeature.Of(roomRide).Entry;
        var walkway = p.Paths.EntranceCells.FirstOrDefault(c => p.Paths.EntranceKind(c) == 0x0C);
        var mouth = p.Paths.EntranceCells.FirstOrDefault(c => p.Paths.EntranceKind(c) == 0x0E);
        var noBuild = p.Paths.Cells.FirstOrDefault(c => p.Paths.Kind(c) == ParkPathKind.None && !p.Paths.IsEntrance(c) && !p.Paths.CanBuild(c));
        var cases = new (string Name, ParkCell Cell, bool Expect)[]
        {
            ("path", p.At(5, 2), true), ("plain ground", p.At(5, 5), true), ("queue (kind 4)", p.At(1, 6), false),
            ("staff-room footprint (kind 5)", roomRide.Origin.Offset(roomRide.Width - 1, roomRide.Height - 1) == entry
                 ? roomRide.Origin : roomRide.Origin.Offset(roomRide.Width - 1, roomRide.Height - 1), false),
            ("staff-room entry (kind 7)", entry, false), ("entrance walkway (kind 12)", walkway, false),
            ("entrance mouth (kind 14, +7 unread)", mouth, true), ("no-build ground (+7 bit 2)", noBuild, false),
            ("off the map", new ParkCell(-1, 3), false),
        };
        var wrong = cases.Where(c => p.Staff.CanDrop(c.Cell) != c.Expect).Select(c => c.Name).ToList();
        Check(wrong.Count == 0, $"placement: 0x1E65B8 through the tile view accepts path and ground and refuses queues, buildings, the walkway and no-build ({cases.Length - wrong.Count} of {cases.Length} cells right{(wrong.Count > 0 ? "; wrong: " + string.Join(", ", wrong) : "")})");
        var advisor = new List<int>();
        p.Staff.Advisor = advisor.Add;
        var held = p.Staff.Hire(StaffKind.Handyman, 0);
        p.Staff.Carry(held, new Point((short)(p.At(1, 6).X * 256 + 37), (short)(p.At(1, 6).Z * 256 + 200)));
        bool refused = !p.Staff.Drop(held, held.Position) && held.Held;
        var exact = new Point((short)(p.At(6, 2).X * 256 + 37), (short)(p.At(6, 2).Z * 256 + 200));
        p.Staff.Carry(held, exact);
        bool dropped = p.Staff.Drop(held, exact) && !held.Held && held.State == 0 && held.LogicalRequest == StaffTables.LogicalWalk
                       && held.Position == exact;
        Check(refused && dropped, "placement: a refused drop leaves the member held; an accepted one keeps the UNSNAPPED cursor position, state 0, logical 13");
        for (int s = 1; s < 5; s++) { var m = p.Staff.Hire(StaffKind.Handyman, s); p.Staff.Drop(m, p.At(6 + s, 2)); }
        Check(advisor.SequenceEqual(new[] { 0x58 }), $"placement: the fifth handyman's drop posts ADD_MAX 0x58 once ({string.Join(",", advisor.Select(a => $"0x{a:x}"))})");
    }

    static void RouteService(Func<int, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        var p = newPark(5);
        var a = hireAt(p, StaffKind.Researcher, p.At(3, 2), null);
        var b = hireAt(p, StaffKind.Researcher, p.At(4, 2), null);
        var svc = p.Staff.RouteRequests;
        int admitted = 0;
        for (int i = 0; i < 10; i++) if (svc.Submit(a, a.Position, Centre(p.At(20, 2)), 0x11)) admitted++;
        bool eleventh = svc.Submit(b, b.Position, Centre(p.At(20, 2)), 0x11);
        Check(admitted == 10 && !eleventh && svc.Pending == 10, $"routes: ten request records, the eleventh is refused ({admitted} admitted, eleventh {(eleventh ? "admitted" : "refused")})");

        var q = newPark(5);
        var x = hireAt(q, StaffKind.Researcher, q.At(3, 2), null);
        var y = hireAt(q, StaffKind.Researcher, q.At(4, 2), null);
        q.Staff.RouteRequests.Submit(x, x.Position, Centre(q.At(20, 2)), 0x11);
        q.Staff.RouteRequests.Submit(y, y.Position, Centre(q.At(21, 2)), 0x11);
        bool nextIsNewest = q.Staff.RouteRequests.Next == y;
        q.Staff.RouteRequests.Pump();
        bool yFirst = y.RouteSlot >= 0 && x.RouteSlot == -1 && y.State == StaffMember.StateWalk;
        Check(nextIsNewest && yFirst, "routes: the pump services the NEWEST request first, one per call");
        Check(ReferenceEquals(q.Staff.Routes, q.Walk.NativeRoutes) && q.Walk.NativeRoutes.AllocatedCount > 0,
              $"routes: staff output lands in the guests' own pool (GuestWalk.NativeRoutes, {q.Walk.NativeRoutes.AllocatedCount} slots in use)");
        int built = q.Staff.RouteRequests.Built;
        q.Staff.RouteRequests.Submit(x, x.Position, Centre(q.At(28, 6)), 0x11);   // an island path cell
        q.Staff.RouteRequests.Pump();
        bool unreachable = q.Staff.RouteRequests.Unreachable == 1 && q.Staff.RouteRequests.Built == built;
        q.Staff.RouteRequests.Submit(x, x.Position, Centre(q.At(28, 6)), 0x03);   // 0x03 admits open ground
        q.Staff.RouteRequests.Pump();
        Check(unreachable && q.Staff.RouteRequests.Built == built + 1,
              $"routes: flags 0x11 cannot reach a path island across grass, 0x03 can ({q.Staff.RouteRequests.Unreachable} unreachable, {q.Staff.RouteRequests.Built} built)");
        // The 0xB wait: a request made in a tick is answered in the NEXT tick's pump.
        var r = newPark(6);
        r.Rng.Override = k => k == 2 ? 0 : k == 16 ? 1 : null;
        var h = hireAt(r, StaffKind.Handyman, r.At(3, 2), 0);
        r.Staff.Litter.Drop(Centre(r.At(15, 2)), false);
        r.Tick();
        bool waiting = h.State == StaffMember.StateWaitForRoute && h.RouteSlot == -1 && r.Staff.RouteRequests.Pending == 1;
        r.Tick();
        // Answered in the next tick's pump and walked in that same tick: event 1 set state 3 and the
        // walk step ran. ⭐ Its first slot is his OWN cell's centre (the builder's root carries the
        // synthetic direction 0, and this route leaves eastwards), so that first step arrives without
        // moving: state 2.
        bool answered = h.RouteSlot >= 0 && h.State == StaffMember.StateSegmentEnd && r.Staff.RouteRequests.Pending == 0
                        && r.Staff.Routes.Target(h.RouteSlot) == Centre(r.At(3, 2));
        var start = h.Position;
        r.Tick(2);
        Check(waiting && answered && h.Position != start,
              "routes: the find-work tick leaves him waiting in 0xB; the next tick's pump answers and he walks in that same tick (first slot = his own cell centre)");
    }

    // =============================================================================================
    // Step 2: the handyman.

    static void Sweep(Func<int, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        int right = 0; var log = new List<string>();
        for (int level = 0; level <= 4; level++)
            foreach (bool vomit in new[] { false, true })
            {
                var p = newPark(20 + level);
                p.Rng.Override = k => k == 2 ? 0 : k == 16 ? 1 : null;
                var h = hireAt(p, StaffKind.Handyman, p.At(3, 2), level);
                var item = p.Staff.Litter.Drop(Centre(p.At(15, 2)), vomit);
                uint? arrived = null, gone = null; int tArr = 0, mArr = 0; bool logical16 = true, claimed = false;
                for (int t = 0; t < 3000 && gone == null; t++)
                {
                    uint now = p.Staff.Now; byte was = h.State;
                    p.Tick();
                    if (item.Claimant == h) claimed = true;
                    if (arrived == null && h.State == Handyman.StateSweeping) { arrived = now; tArr = h.Tiredness; mArr = h.Morale; }
                    else if (h.State == Handyman.StateSweeping && h.LogicalRequest != StaffTables.LogicalWork) logical16 = false;
                    if (arrived != null && !item.Active) gone = now;
                }
                int T = StaffTables.HandymanSweepTicks[level];
                bool ok = arrived != null && gone != null && gone - arrived == (uint)T + 1 && claimed && logical16
                          && p.Staff.Litter.Count == 0 && item.Claimant == null && h.State == 0 && h.Target == null
                          && h.Tiredness == Math.Min(100, tArr + 5) && h.Morale == (vomit ? Math.Max(0, mArr - 6) : Math.Min(100, mArr + 1));
                if (ok) right++;
                log.Add($"L{level}{(vomit ? "v" : "")}:{(gone - arrived)?.ToString() ?? "never"}");
            }
        {
            // The first-leg slide (findings/staff-person.md §5.3): nothing re-requests the walk logical
            // when a walk starts, so after the sweep he leaves still requesting 16; the FIRST waypoint
            // retire (0x191D78 with a1 = 0) is what asks for 13.
            var p = newPark(29);
            p.Rng.Override = k => k == 2 ? 0 : k == 16 ? 1 : null;
            var h = hireAt(p, StaffKind.Handyman, p.At(3, 2), 4);
            var item = p.Staff.Litter.Drop(Centre(p.At(6, 2)), false);
            int guard = 0;
            while (item.Active && guard++ < 2000) p.Tick();
            long retires = h.WaypointsRetired; int logicalBefore = -1, ticks = 0;
            while (h.WaypointsRetired == retires && ticks++ < 2000) { logicalBefore = h.LogicalRequest; p.Tick(); }
            Check(!item.Active && logicalBefore == StaffTables.LogicalWork && h.LogicalRequest == StaffTables.LogicalWalk,
                  $"first leg: after the sweep he leaves still requesting logical 16 ({logicalBefore}) until the first waypoint retire asks for 13 ({h.LogicalRequest}, {ticks} updates later)");
        }
        Check(right == 10, $"sweep: a dropped litter item is claimed, swept in sweep[L]+1 updates (strict now > deadline), removed, and pays tiredness +5 and morale +1 (vomit -6) at every level ({right} of 10 cases; {string.Join(" ", log)})");
    }

    static void Toilet(Func<int, Park> newPark, Func<Park, Asset, ParkCell, ParkRide> place, Asset toiletAsset,
                       Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<ParkClock, int> advanceDays,
                       Action<bool, string> Check)
    {
        int right = 0; var log = new List<string>();
        foreach (int start in new[] { 50, 30 })
            for (int level = 0; level <= 4; level += 2)
            {
                var p = newPark(40 + level);
                advanceDays(p.Clock, 37);
                var toilet = place(p, toiletAsset, p.At(14, 3));
                toilet.Wear(100 - start);
                p.Rng.Override = k => k == 2 ? 1 : k == 16 ? 1 : null;
                var h = hireAt(p, StaffKind.Handyman, p.At(3, 2), level);
                var entry = StaffFeature.Of(toilet).Entry;
                uint? arrived = null, done = null; int tArr = 0, mArr = 0; int hiddenTicks = 0, cleaningTicks = 0;
                bool targeted = false; Point arrival = default;
                for (int t = 0; t < 3000 && done == null; t++)
                {
                    uint now = p.Staff.Now;
                    p.Tick();
                    if (ReferenceEquals(h.Target, toilet) && h.Mode == Handyman.ModeToilet) targeted = true;
                    if (arrived == null && h.State == Handyman.StateCleaningToilet) { arrived = now; tArr = h.Tiredness; mArr = h.Morale; arrival = h.Position; }
                    if (h.State == Handyman.StateCleaningToilet) { cleaningTicks++; if (!h.Shown) hiddenTicks++; }
                    if (arrived != null && h.State != Handyman.StateCleaningToilet) done = now;
                }
                int T = StaffTables.HandymanToiletTicks[level];
                bool filthy = start < 40;
                bool ok = targeted && arrived != null && done != null && done - arrived == (uint)T + 1
                          && toilet.Condition == 100 && toilet.LastCleanedDay == 37 && h.Shown && hiddenTicks == cleaningTicks
                          && arrival == Centre(entry)
                          && h.Morale == (filthy ? Math.Max(0, mArr - 10) : Math.Min(100, mArr + 5))
                          && h.Tiredness == Math.Min(100, tArr + (filthy ? 10 : 5));
                if (ok) right++;
                log.Add($"c{start}L{level}:{(done - arrived)?.ToString() ?? "never"}/hidden{hiddenTicks}of{cleaningTicks}");
            }
        Check(right == 6, $"toilet: a toilet at 50 (or 30) is walked to at its entry cell, cleaned hidden for toilet[L]+1 updates, set to 100 with the day stamped (37), morale +5 (or -10 and tiredness +5 more below 40) ({right} of 6 cases; {string.Join(" ", log)})");
    }

    static void ToiletScore(Func<int, Park> newPark, Func<Park, Asset, ParkCell, ParkRide> place, Asset toiletAsset,
                            Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        // The handyman stands on the spur (x+10, row 3). T1: same row, 4 across, condition 59.
        // T2: straight below (its stub IS the spur), one row down, condition 10.
        var p = newPark(50);
        var t1 = place(p, toiletAsset, p.At(14, 3)); t1.Wear(41);
        var t2 = place(p, toiletAsset, p.At(10, 4)); t2.Wear(90);
        p.Rng.Override = k => k == 2 ? 1 : k == 16 ? 1 : null;
        var h = hireAt(p, StaffKind.Handyman, p.At(10, 3), 0);
        int Shipped(ParkRide t) => Math.Abs(t.Origin.X - h.Cell.X) + Math.Abs(t.Origin.Z - h.Cell.Z) * (t.Condition + 1);
        int Sensible(ParkRide t) => (Math.Abs(t.Origin.X - h.Cell.X) + Math.Abs(t.Origin.Z - h.Cell.Z)) * (t.Condition + 1);
        var shippedPick = Shipped(t1) < Shipped(t2) ? t1 : t2;
        var sensiblePick = Sensible(t1) < Sensible(t2) ? t1 : t2;
        p.Tick();
        Check(shippedPick == t1 && sensiblePick == t2 && ReferenceEquals(h.Target, t1) && h.Mode == Handyman.ModeToilet,
              $"toilet score: |dx| + |dz|*(c+1) EXACTLY as shipped picks the same-row c59 toilet (score {Shipped(t1)} vs {Shipped(t2)}), where (|dx|+|dz|)*(c+1) would pick the c10 one ({Sensible(t1)} vs {Sensible(t2)}); chose {(ReferenceEquals(h.Target, t1) ? "c59" : ReferenceEquals(h.Target, t2) ? "c10" : "neither")}");

        // Ties: equal shipped scores, the FIRST in the placed-object order wins; reversing the order flips it.
        foreach (bool westFirst in new[] { true, false })
        {
            var q = newPark(51);
            ParkRide west, east;
            if (westFirst) { west = place(q, toiletAsset, q.At(20, 3)); east = place(q, toiletAsset, q.At(24, 3)); }
            else { east = place(q, toiletAsset, q.At(24, 3)); west = place(q, toiletAsset, q.At(20, 3)); }
            west.Wear(100); east.Wear(100);
            q.Rng.Override = k => k == 2 ? 1 : k == 16 ? 1 : null;
            var g = hireAt(q, StaffKind.Handyman, q.At(22, 2), 0);
            q.Tick();
            var expected = westFirst ? west : east;
            Check(ReferenceEquals(g.Target, expected),
                  $"toilet score: a tie (both score 3) goes to the first toilet in placement order, strict < ({(westFirst ? "west" : "east")} placed first, chose {(ReferenceEquals(g.Target, west) ? "west" : ReferenceEquals(g.Target, east) ? "east" : "neither")})");
        }

        // Candidates are strictly below 60; none qualifies -> the litter search.
        var r = newPark(52);
        var at60 = place(r, toiletAsset, r.At(14, 3)); at60.Wear(40);
        r.Rng.Override = k => k == 2 ? 1 : k == 16 ? 1 : null;
        var item = r.Staff.Litter.Drop(Centre(r.At(20, 2)), false);
        var f = hireAt(r, StaffKind.Handyman, r.At(3, 2), 0);
        r.Tick();
        Check(at60.Condition == 60 && ReferenceEquals(f.Target, item) && item.Claimant == f,
              "toilet score: a toilet AT 60 is no candidate, so the coin's toilet branch falls back to the litter");
        // A chosen toilet whose route is REFUSED does not fall back to litter. Ten records are
        // pending; the pump takes one, a NEWER handyman (updated first) takes it back for the same
        // toilet, and then this one's request finds no free record.
        var s = newPark(53);
        var dirty = place(s, toiletAsset, s.At(14, 3)); dirty.Wear(90);
        s.Rng.Override = k => k == 2 ? 1 : k == 16 ? 1 : null;
        var litter = s.Staff.Litter.Drop(Centre(s.At(20, 2)), false);
        var filler = hireAt(s, StaffKind.Researcher, s.At(4, 2), null);
        var e = hireAt(s, StaffKind.Handyman, s.At(3, 2), 0);
        var newer = hireAt(s, StaffKind.Handyman, s.At(5, 2), 0);
        for (int i = 0; i < 10; i++) s.Staff.RouteRequests.Submit(filler, filler.Position, Centre(s.At(9, 2)), 0x11);
        var sounds = new List<int>(); s.Staff.Sound = (m, bank, id) => { if (m == e && bank == 8) sounds.Add(id); };
        int refusedBefore = s.Staff.RouteRequests.Refused;
        s.Tick();
        Check(ReferenceEquals(newer.Target, dirty) && s.Staff.RouteRequests.Refused == refusedBefore + 1
              && litter.Claimant == null && e.Target == null && e.State == StaffMember.StatePatrol && sounds.Contains(Handyman.SoundNothingToDo),
              $"toilet score: a toilet whose route is refused (all 10 records busy) fails the search WITHOUT trying litter: nothing claimed, sound 0xA0, patrol (state 0x{e.State:x2})");
    }

    static void WalkTiredness(Func<int, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        var p = newPark(60);
        var h = hireAt(p, StaffKind.Handyman, p.At(3, 2), 1);
        h.Tiredness = 0;
        long retires = 0, onPhase = 0, rises = 0, strays = 0;
        for (int t = 0; t < 20000 && h.Tiredness < 75; t++)
        {
            long r0 = h.WaypointsRetired; int t0 = h.Tiredness; bool phase = (p.Staff.Now & 3) == (h.Serial & 3);
            p.Tick();
            long dr = h.WaypointsRetired - r0; int dt = h.Tiredness - t0;
            retires += dr;
            if (dr > 0 && phase) onPhase += dr;
            if (dt == 1) { rises++; if (dr == 0 || !phase) strays++; }
            else if (dt != 0) strays++;
        }
        double ratio = retires == 0 ? 0 : rises / (double)retires;
        Check(retires >= 100 && strays == 0 && rises == onPhase,
              $"walking tiredness: every +1 comes from a waypoint retire on a phase tick and nothing else ({rises} rises = {onPhase} phase retires of {retires}, {strays} strays)");
        Check(ratio > 0.15 && ratio < 0.35, $"walking tiredness: about one waypoint in four costs a point ({rises}/{retires} = {ratio:F3})");
    }

    static void Rest(Func<int, Park> newPark, Func<Park, Asset, ParkCell, ParkRide> place, Asset roomAsset,
                     Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        var p = newPark(70);
        var room = place(p, roomAsset, p.At(2, 3));
        var h = hireAt(p, StaffKind.Handyman, p.At(20, 2), 0);
        h.Tiredness = 81; h.Morale = 50;
        bool sawGoRest = false, headed = false; uint? restFrom = null, restTo = null; int hidden = 0, resting = 0;
        for (int t = 0; t < 3000 && restTo == null; t++)
        {
            uint now = p.Staff.Now;
            p.Tick();
            if (h.State == StaffMember.StateGoRest) sawGoRest = true;
            if (h.Mode == StaffMember.ModeStaffRoom && ReferenceEquals(h.Target, room)) headed = true;
            if (restFrom == null && h.State == StaffMember.StateResting) restFrom = now;
            if (h.State == StaffMember.StateResting) { resting++; if (!h.Shown) hidden++; }
            if (restFrom != null && h.State != StaffMember.StateResting) restTo = now;
        }
        uint length = restTo is uint b && restFrom is uint a ? b - a : 0;
        Check(sawGoRest && headed && restFrom != null && hidden == resting && resting > 0,
              $"rest: tiredness 81 sends him to the staff room (state 0x31, mode 0x11, target the room) and he rests HIDDEN ({hidden} of {resting} resting updates hidden)");
        Check(restTo != null && length >= 161 && length <= 164 && h.Tiredness == 0 && h.Morale == 91 && h.Shown
              && h.State == StaffMember.StatePatrol && h.Target == null,
              $"rest: 41 quanta of -2 bring 81 to 0 in {length} updates (161..164), morale 50 -> {h.Morale}, then shown and back to PATROL 0xD (state 0x{h.State:x2})");
    }

    static void NoRoom(Func<int, Park> newPark, Func<Park, Asset, ParkCell, ParkRide> place, Asset roomAsset,
                       Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        foreach (bool withRoom in new[] { false, true })
        {
            var p = newPark(80);
            if (withRoom) place(p, roomAsset, p.At(2, 3));
            p.Rng.Override = k => k == 2 ? 0 : null;
            var item = p.Staff.Litter.Drop(Centre(p.At(25, 2)), false);
            var h = hireAt(p, StaffKind.Handyman, p.At(20, 2), 0);
            h.Tiredness = 81;
            int goRest = 0, rests = 0, sweeps = 0, claims = 0, minTired = 100;
            long walked0 = h.WaypointsRetired;
            byte last = h.State;
            for (int t = 0; t < 4000; t++)
            {
                p.Tick();
                if (h.State != last)
                {
                    if (h.State == StaffMember.StateGoRest) goRest++;
                    if (h.State == StaffMember.StateResting) rests++;
                    if (h.State == Handyman.StateSweeping) sweeps++;
                    last = h.State;
                }
                if (item.Claimant == h) claims++;
                minTired = Math.Min(minTired, h.Tiredness);
            }
            long walked = h.WaypointsRetired - walked0;
            if (!withRoom)
                Check(goRest >= 3 && rests == 0 && sweeps == 0 && claims == 0 && item.Active && minTired >= 81 && walked >= 20,
                      $"no staff room: a tired handyman never works again, he patrols forever -- 4000 updates, {goRest} trips to 0x31, {walked} waypoints walked, {sweeps} sweeps, {claims} claimed updates, tiredness never below {minTired}");
            else
                Check(rests >= 1 && sweeps >= 1 && !item.Active,
                      $"no staff room (control): the same handyman WITH a room rests ({rests}) and then sweeps the litter ({sweeps})");
        }
    }

    static void Wander(Func<int, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        var p = newPark(90);
        var h = hireAt(p, StaffKind.Handyman, p.At(12, 2), 2);
        int chains = 0, waypoints = 0, offPath = 0, gaps = 0, ticks = 0, offCells = 0;
        var visited = new HashSet<ParkCell>();
        byte last = h.State;
        for (int t = 0; t < 6000; t++, ticks++)
        {
            h.Tiredness = 0;                                    // keep him out of the rest branch
            p.Tick();
            visited.Add(h.Cell);
            if (p.Staff.Tiles.Kind(h.Cell) != NativeTileView.KindPath) offCells++;
            if (last == StaffMember.StateWander && h.State == StaffMember.StateWalk && h.Mode == StaffMember.ModePatrol)
            {
                chains++;
                var prev = h.Cell;
                for (int s = h.RouteSlot; s != -1; s = p.Staff.Routes.Next(s))
                {
                    var target = p.Staff.Routes.Target(s);
                    var c = new ParkCell(target.X >> 8, target.Z >> 8);
                    waypoints++;
                    if (p.Staff.Tiles.Kind(c) != NativeTileView.KindPath) offPath++;
                    if (Math.Abs(c.X - prev.X) + Math.Abs(c.Z - prev.Z) != 1) gaps++;
                    prev = c;
                }
            }
            last = h.State;
        }
        Check(chains >= 20 && waypoints >= 40 && offPath == 0 && gaps == 0 && offCells == 0,
              $"wander: on a path the local wander walks one slot per cell and only onto path ({chains} chains, {waypoints} waypoints, {offPath} off path, {gaps} gaps; {offCells} of {ticks} updates off path over {visited.Count} cells)");
        // Off the path it takes branch B: the ring search finds the path two rows up and makes ONE slot to it.
        var q = newPark(91);
        var g = hireAt(q, StaffKind.Handyman, q.At(12, 4), 2);
        g.Tiredness = 0;
        byte before = 0; int guard = 0;
        while (!(before == StaffMember.StateWander && g.State == StaffMember.StateWalk) && guard++ < 50) { before = g.State; q.Tick(); }
        var head = g.RouteSlot;
        var aim = head >= 0 ? q.Staff.Routes.Target(head) : default;
        Check(head >= 0 && q.Staff.Routes.Next(head) == -1 && aim == Centre(q.At(12, 2)) && q.Staff.Tiles.Kind(q.At(12, 4)) == NativeTileView.KindGround,
              $"wander: dropped on grass the ring search routes ONE slot straight to the nearest path centre ({(head >= 0 ? $"({aim.X >> 8},{aim.Z >> 8})" : "no slot")})");
        // The ring runs r = 0 .. rand(5)+3: with rand(5) = 0 a path FOUR cells away is out of reach.
        var r = newPark(92);
        r.Rng.Override = k => k == 5 ? 0 : k == 2 ? 0 : k == 16 ? 1 : null;
        var far = hireAt(r, StaffKind.Handyman, r.At(12, 6), 2);
        far.Tiredness = 0;
        byte b2 = 0; int guard2 = 0;
        while (!(b2 == StaffMember.StateWander && far.State != StaffMember.StateWander) && guard2++ < 50) { b2 = far.State; r.Tick(); }
        int h2 = far.RouteSlot;
        bool direct = h2 >= 0 && r.Staff.Routes.Next(h2) == -1 && r.Staff.Routes.Target(h2) == Centre(r.At(12, 2));
        Check(!direct && b2 == StaffMember.StateWander,
              $"wander: the ring reaches only r = rand(5)+3 cells -- with rand(5) = 0 the path 4 away is NOT taken (fell back to {(far.State == StaffMember.StateWaitForRoute ? "the map-centre request" : "the crawl")})");
    }

    static void Patrol(Func<int, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        // A rectangle over ten corridor cells, one row deep: x0 = +5, x1 = +15, z0 = z1-1 = corridor row.
        var p = newPark(95);
        var h = hireAt(p, StaffKind.Handyman, p.At(10, 2), 1);
        h.SetPatrolArea(p.At(5, 2), p.At(15, 3));
        int legs = 0, inside = 0, maxX = int.MinValue, minX = int.MaxValue;
        byte last = h.State; bool fromPatrol = false;
        for (int t = 0; t < 8000; t++)
        {
            h.Tiredness = 0;
            p.Tick();
            if (last == StaffMember.StatePatrol && h.State == StaffMember.StateWaitForRoute) fromPatrol = true;
            if (fromPatrol && h.State == StaffMember.StateWalk && h.RouteSlot >= 0)
            {
                int s = h.RouteSlot; while (p.Staff.Routes.Next(s) != -1) s = p.Staff.Routes.Next(s);
                var end = p.Staff.Routes.Target(s); var c = new ParkCell(end.X >> 8, end.Z >> 8);
                legs++; fromPatrol = false;
                int dx = c.X - p.At(0, 0).X;
                if (c.Z == p.At(0, 2).Z && dx >= 5 && dx < 15) inside++;
                maxX = Math.Max(maxX, dx); minX = Math.Min(minX, dx);
            }
            last = h.State;
        }
        Check(legs >= 20 && inside == legs && maxX == 14 && minX == 5,
              $"patrol: every patrol leg targets a path cell in [x0, x1) x [z0, z1) -- the max column never ({inside} of {legs} legs inside, x {minX}..{maxX} of 5..15)");
        var q = newPark(96);
        var g = hireAt(q, StaffKind.Handyman, q.At(10, 2), 1);
        g.SetPatrolArea(q.At(8, 2), q.At(8, 6));                // one column wide: dx = 0
        byte before = g.State; bool straightToWander = false; int guard = 0;
        while (guard++ < 200 && !straightToWander)
        {
            g.Tiredness = 0; before = g.State; q.Tick();
            if (before == StaffMember.StatePatrol) straightToWander = g.State == StaffMember.StateWander;
        }
        Check(straightToWander && g.HasPatrolArea, "patrol: a one-wide rectangle (x0 == x1) is treated as no area -- straight to the local wander, no draws");
    }

    static void UpdateOrder(Func<int, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<bool, string> Check)
    {
        // Two handymen equally far from one litter item: the NEWER is updated first and claims it.
        var p = newPark(97);
        p.Rng.Override = k => k == 2 ? 0 : k == 16 ? 1 : null;
        var older = hireAt(p, StaffKind.Handyman, p.At(8, 2), 0);
        var newer = hireAt(p, StaffKind.Handyman, p.At(16, 2), 0);
        var item = p.Staff.Litter.Drop(Centre(p.At(12, 2)), false);
        p.Tick();
        Check(item.Claimant == newer && ReferenceEquals(newer.Target, item) && older.Target == null,
              "update order: members run newest first (the map-object list), so of two equally near handymen the later hire claims the litter");
        // Of two equally near items the newest wins the search (strict < over a newest-first list).
        var q = newPark(98);
        q.Rng.Override = k => k == 2 ? 0 : k == 16 ? 1 : null;
        var a = q.Staff.Litter.Drop(Centre(q.At(8, 2)), false);
        var b = q.Staff.Litter.Drop(Centre(q.At(16, 2)), false);
        var h = hireAt(q, StaffKind.Handyman, q.At(12, 2), 0);
        q.Tick();
        Check(ReferenceEquals(h.Target, b) && b.Claimant == h && a.Claimant == null,
              "litter search: of two equally near items the NEWEST is taken (active list newest first, strict <)");
    }

    static void Strike(Func<int, Park> newPark, Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, ParkEntrance table, Action<bool, string> Check)
    {
        var p = newPark(100);
        var e = p.Paths.EntranceEntry;
        Check(e != null, $"strike: the park's entrance entry is fitted ({e})");
        if (e == null) return;
        // Dropped on the park's own starting path, which the strike point lies on (natively 0x14E5B0
        // lays it; here the fixture laid it as the viewer does).
        var drop = new ParkCell(e.Value.XCol + 1, e.Value.ZEnd + e.Value.PathRows);
        Check(p.Paths.Kind(drop) == ParkPathKind.Path && p.Paths.Kind(new ParkCell(e.Value.XCol, e.Value.ZEnd)) == ParkPathKind.Path,
              $"strike: the starting path runs from the strike row {e.Value.ZEnd} to {drop}");
        var h = hireAt(p, StaffKind.Handyman, drop, 1);
        h.Tiredness = 0;
        p.Staff.SetStriking(StaffKind.Handyman, true);
        bool walked = false; int t = 0;
        for (; t < 6000 && h.State != StaffMember.StateStriking; t++)
        {
            h.Tiredness = 0;
            p.Tick();
            if (h.State == StaffMember.StateWalk && h.Mode == StaffMember.ModeStrike) walked = true;
        }
        var cell = h.Cell;
        // x = XCol*256 + rand(512), z = ZEnd*256 + 0x80; the endpoint slot rounds x to a quarter cell,
        // ties up, so a draw near the top can land on XCol*256 + 512 -- the next cell's edge (READ
        // arithmetic of 0x1497C0 and 0x192560).
        bool atPoint = h.Position.Z == e.Value.ZEnd * 256 + 0x80
                       && h.Position.X >= e.Value.XCol * 256 && h.Position.X <= e.Value.XCol * 256 + 512;
        Check(walked && h.State == StaffMember.StateStriking && h.LogicalRequest == StaffTables.LogicalStrike && atPoint,
              $"strike: the type's flag sends him (0x1A, flags 0x23, mode 5) to the strike point -- XCol*256 + rand(512) on row ZEnd {e.Value.ZEnd} -- where he stands in 0xF with logical 15 (at {h.Position.X}/256, {h.Position.Z}/256 after {t} updates)");
        p.Tick(5);
        bool stays = h.State == StaffMember.StateStriking;
        p.Staff.SetStriking(StaffKind.Handyman, false);
        p.Tick();
        Check(stays && h.State != StaffMember.StateStriking && h.LogicalRequest == StaffTables.LogicalWalk,
              "strike: he stands while the flag holds and returns to work (state 0, logical 13) when it clears");
        // A worker finishes the current job before joining the strike: the check is only in find work.
        var q = newPark(101);
        q.Rng.Override = k => k == 2 ? 0 : k == 16 ? 1 : null;
        var w = hireAt(q, StaffKind.Handyman, q.At(3, 2), 0);
        var item = q.Staff.Litter.Drop(Centre(q.At(8, 2)), false);
        int guard = 0;
        while (w.State != Handyman.StateSweeping && guard++ < 2000) q.Tick();
        q.Staff.SetStriking(StaffKind.Handyman, true);
        bool finishedFirst = true; guard = 0;
        while (item.Active && guard++ < 500) { q.Tick(); if (w.State == StaffMember.StateWalkToStrike) finishedFirst = false; }
        q.Tick(2);
        Check(finishedFirst && !item.Active && (w.State == StaffMember.StateWalkToStrike || w.State == StaffMember.StateWaitForRoute),
              "strike: a handyman already sweeping finishes the job, THEN joins the strike (the check is only in find work)");
    }

    static void Lifecycle(Func<int, Park> newPark, Func<Park, Asset, ParkCell, ParkRide> place, Asset toiletAsset,
                          Func<Park, StaffKind, ParkCell, int?, StaffMember> hireAt, Action<ParkClock, int> advanceDays,
                          Action<bool, string> Check)
    {
        // Fire mid-walk with a claim: claim released, route and request gone, pro-rata wage debited.
        var p = newPark(110);
        p.Rng.Override = k => k == 2 ? 0 : k == 16 ? 1 : null;
        advanceDays(p.Clock, 28);                                  // hired on day 28 (29 Jan)...
        var h = hireAt(p, StaffKind.Handyman, p.At(3, 2), 1);
        advanceDays(p.Clock, 12);                                  // ...fired on day 40, in FEBRUARY
        var item = p.Staff.Litter.Drop(Centre(p.At(25, 2)), false);
        p.Tick(3);
        int slots = p.Walk.NativeRoutes.AllocatedCount;
        int wage = h.ProRatedWage;
        int spent = p.Sim.Finances.TotalSpending;
        bool walking = h.State == StaffMember.StateWalk && item.Claimant == h && slots > 0;
        p.Staff.Fire(h);
        // 0x1DC338 divides by the PREVIOUS month (0x16D2E8): January's 31, not February's 28 --
        // 12 days of a level-1 cleaner's 55 is 12*100/31 = 38 % = 20 (28 days would give 23).
        Check(walking && item.Claimant == null && p.Walk.NativeRoutes.AllocatedCount == 0 && h.Candidate.Available
              && h.DaysEmployed == 12 && wage == 20 && p.Sim.Finances.TotalSpending - spent == 200,
              $"fire: mid-walk, the litter claim is released, his {slots} route slots are freed, the candidate is back and the pro-rata wage over the PREVIOUS month is debited x10 ({wage} for {h.DaysEmployed} days in month {p.Clock.MonthOfYear})");
        // Route-system reset (GuestWalk.Clear resets the shared pool): walkers are released, sweepers keep their job.
        var q = newPark(111);
        q.Rng.Override = k => k == 2 ? 0 : k == 16 ? 1 : null;
        var walker = hireAt(q, StaffKind.Handyman, q.At(3, 2), 0);
        var far = q.Staff.Litter.Drop(Centre(q.At(25, 2)), false);
        q.Tick(3);
        bool wasWalking = walker.State == StaffMember.StateWalk && far.Claimant == walker;
        q.Walk.Clear();
        q.Tick();
        // Released to state 0 and at once back in find work in the same update: his old chain is gone
        // (the pool holds nothing) and he has asked again from where he stood.
        Check(wasWalking && walker.RouteSlot == -1 && q.Walk.NativeRoutes.AllocatedCount == 0
              && walker.State == StaffMember.StateWaitForRoute && q.Staff.RouteRequests.Pending == 1,
              $"route reset: a walking handyman is released when the shared pool resets -- no stale slots, a fresh request (state 0x{walker.State:x2}, {q.Walk.NativeRoutes.AllocatedCount} slots)");
        var sq = newPark(114);
        sq.Rng.Override = k => k == 2 ? 0 : k == 16 ? 1 : null;
        var sweeper = hireAt(sq, StaffKind.Handyman, sq.At(3, 2), 0);
        var near = sq.Staff.Litter.Drop(Centre(sq.At(6, 2)), false);
        int g0 = 0;
        while (sweeper.State != Handyman.StateSweeping && g0++ < 2000) sq.Tick();
        sq.Walk.Clear();
        sq.Tick();
        Check(sweeper.State == Handyman.StateSweeping && near.Claimant == sweeper,
              "route reset: a handyman already sweeping keeps his job and his claim (0x145AC8 protects 0x1B/0x33)");
        // Removal notice: a toilet demolished mid-clean un-hides him and ends the job.
        var r = newPark(112);
        var toilet = place(r, toiletAsset, r.At(14, 3)); toilet.Wear(60);
        r.Rng.Override = k => k == 2 ? 1 : k == 16 ? 1 : null;
        var c = hireAt(r, StaffKind.Handyman, r.At(3, 2), 0);
        int guard = 0;
        while (c.State != Handyman.StateCleaningToilet && guard++ < 2000) r.Tick();
        bool hidden = c.State == Handyman.StateCleaningToilet && !c.Shown;
        r.Sim.Remove(toilet.Id);
        r.Tick();
        Check(hidden && c.Shown && c.State != Handyman.StateCleaningToilet && !ReferenceEquals(c.Target, toilet),
              "removal: a toilet demolished while he cleans it hidden sends the notice -- target cleared, state 0, SHOWN");
        // Sounds: the 1-in-16 idle (0xA1) and nothing-to-do (0xA0) go out on bank 8.
        var s = newPark(113);
        var heard = new List<int>(); s.Staff.Sound = (m, bank, id) => { if (bank == 8) heard.Add(id); };
        s.Rng.Override = k => k == 16 ? 0 : null;
        hireAt(s, StaffKind.Handyman, s.At(3, 2), 0);
        s.Tick(400);
        Check(heard.Count(i => i == Handyman.SoundIdle) >= 1 && heard.Count(i => i == Handyman.SoundNothingToDo) >= 1,
              $"sounds: find work raises bank 8 0xA1 on its 1-in-16 draw and 0xA0 when there is nothing to do ({heard.Count(i => i == 0xA1)} x 0xA1, {heard.Count(i => i == 0xA0)} x 0xA0)");
    }

    // =============================================================================================
    // Step 2: the guests' side, through the real ParkVisitors.

    static VisitorNeeds FrozenNeeds()
    {
        var n = new VisitorNeeds(871) { SecondsPerRise = 1_000_000 };
        foreach (var key in n.Rates.Keys.ToArray()) n.Rates[key] = new(0, 0, false);
        n.Unknown78Bar = n.SickBar = n.ToiletBar = n.HungerBar = n.ThirstBar = 101;
        return n;
    }

    static void GuestLitter(Func<int, Park> newPark, Func<Park, Asset, ParkCell, ParkRide> place, Asset toiletAsset, Asset binAsset,
                            Action<bool, string> Check)
    {
        // A guest who has USED a facility has a decision gate, so 0x20C930's arms are drawn for him
        // (the port's adapter picks a destination for a guest without one). The arm value is forced
        // through the coordinator's own random: 3 is the litter arm, 4 the vomit arm (and rand(4) == 0).
        (Park P, Guest G, ParkRide Toilet) Visit(int arm, bool attach, ParkCell? bin)
        {
            var p = newPark(120 + arm);
            var sim = p.Sim; var walk = p.Walk;
            var toilet = place(p, toiletAsset, p.At(14, 3));
            sim.SetOpen(toilet.Id, true); toilet.Set("VAR_BROKEN", 0);
            if (bin is { } b) place(p, binAsset, b);
            var v = new ParkVisitors(sim, walk, () => arm) { Needs = FrozenNeeds() };
            var staff = new ParkStaff(v, p.Clock, p.Serials, p.Rng.Next);
            if (attach) v.Staff = staff;
            p.Visitors = v; p.Staff = staff;
            var g = v.Arrive(p.At(12, 2), p.At(12, 2));
            v.Needs.Set(g.Id, new VisitorWants { Cash = 1234, Toilet = 100, Happiness = 60, PreferredIntensity = 90 });
            if (!v.SendTo(g, toilet)) throw new InvalidOperationException("guest cannot reach the toilet");
            for (int t = 0; t < 4000 && v.Relieved == 0; t++) v.Step(.04, null);
            return (p, walk.Guests.First(x => x.Id == g.Id), toilet);
        }
        void Steps(Park p, int n) { for (int i = 0; i < n; i++) p.Visitors.Step(.04, null); }

        // Arm 3: a full rubbish meter with no bin within 5 cells drops litter at his feet.
        {
            var (p, g, _) = Visit(3, true, null);
            var w = p.Visitors.Needs.Of(g.Id); w.Litter = 90; p.Visitors.Needs.Set(g.Id, w);
            Steps(p, 3);
            var item = p.Staff.Litter.Active.FirstOrDefault();
            Check(p.Visitors.Relieved == 1 && item != null && item.Cell == g.Cell && !item.Vomit && item.Shown
                  && p.Visitors.Needs.Of(g.Id).Litter == 0 && p.Visitors.LitterDropped == 1
                  && Math.Abs(item.Position.X - (g.Cell.X * 256 + 128)) <= 100 && Math.Abs(item.Position.Z - (g.Cell.Z * 256 + 128)) <= 100,
                  $"guest litter: rubbish 90 (> 89) and no bin within 5 drops ONE shown litter item within +-100 of his position and empties the meter ({p.Staff.Litter.Count} item(s))");
        }
        // Arm 3 with a bin 4 cells away: no litter, meter emptied (⚠ the walk to the bin is not ported).
        {
            var (p, g, _) = Visit(3, true, null);
            // place a bin 4 cells from where he stands, then fill his meter
            place(p, binAsset, g.Cell.Offset(3, 2));
            var w = p.Visitors.Needs.Of(g.Id); w.Litter = 95; p.Visitors.Needs.Set(g.Id, w);
            Steps(p, 3);
            Check(p.Staff.Litter.Count == 0 && p.Visitors.LitterBinned == 1 && p.Visitors.Needs.Of(g.Id).Litter == 0,
                  "guest litter: a bin exactly 5 cells away (sltiu 6) takes the rubbish -- no litter, meter 0 (the walk to it is not ported)");
            var farBin = Visit(3, true, null);
            place(farBin.P, binAsset, farBin.G.Cell.Offset(4, 2));
            var fw = farBin.P.Visitors.Needs.Of(farBin.G.Id); fw.Litter = 95; farBin.P.Visitors.Needs.Set(farBin.G.Id, fw);
            Steps(farBin.P, 3);
            Check(farBin.P.Staff.Litter.Count == 1 && farBin.P.Visitors.LitterBinned == 0,
                  "guest litter: a bin 6 cells away is too far (dist > 5): the rubbish is dropped");
        }
        // Unattached: the same guest keeps his rubbish and nothing is made (today's viewer).
        {
            var (p, g, _) = Visit(3, false, null);
            var w = p.Visitors.Needs.Of(g.Id); w.Litter = 95; p.Visitors.Needs.Set(g.Id, w);
            Steps(p, 50);
            Check(p.Staff.Litter.Count == 0 && p.Visitors.Needs.Of(g.Id).Litter == 95 && p.Visitors.LitterDropped == 0,
                  "guest litter (control): with no staff system attached the litter arm does nothing, as before");
        }
        // Arm 4: sick > 92 and rand(4) == 0 -> state 0x1D for 15 ticks, then vomit and sickness 0.
        {
            var (p, g, _) = Visit(4, true, null);
            var w = p.Visitors.Needs.Of(g.Id); w.Sick = 95; p.Visitors.Needs.Set(g.Id, w);
            int sickTicks = 0; int guard = 0;
            Steps(p, 1);
            while (p.Visitors.IsVomiting(g.Id) && guard++ < 100) { sickTicks++; Steps(p, 1); }
            var item = p.Staff.Litter.Active.FirstOrDefault();
            Check(item != null && item.Vomit && item.Shown && item.Cell == g.Cell && p.Visitors.Needs.Of(g.Id).Sick == 0
                  && sickTicks >= 15 && sickTicks <= 16 && item.ModelId == 487,
                  $"guest vomit: sickness > 92 with the 1-in-4 arm stands him still for 15 ticks ({sickTicks} steps vomiting), then leaves shown VOMIT (model 487 puke) and zeroes sickness");
        }
        // Every 64 ticks, each litter item within Manhattan < 2 costs happiness 3, vomit +3 sickness.
        foreach (int distance in new[] { 1, 2 })
        {
            var (p, g, _) = Visit(3, true, null);
            p.Staff.Litter.Drop(Centre(g.Cell.Offset(distance == 1 ? 1 : 2, 0)), false);
            p.Staff.Litter.Drop(Centre(g.Cell.Offset(0, distance == 1 ? 0 : 2)), true);
            var w0 = p.Visitors.Needs.Of(g.Id);
            Steps(p, 64);
            var w1 = p.Visitors.Needs.Of(g.Id);
            int dh = w1.Happiness - w0.Happiness, ds = w1.Sick - w0.Sick;
            if (distance == 1)
                Check(dh == -6 && ds == 3, $"litter penalty: two items within one cell (one vomit) cost happiness 6 and add sickness 3 once in 64 ticks (happiness {dh:+0;-0}, sickness {ds:+0;-0})");
            else
                Check(dh == 0 && ds == 0, $"litter penalty (control): the same two items two cells away cost nothing (happiness {dh:+0;-0}, sickness {ds:+0;-0})");
        }
    }

    static void StandIn(Func<int, Park> newPark, Func<Park, Asset, ParkCell, ParkRide> place, Asset toiletAsset, Action<bool, string> Check)
    {
        // Attached AND hired: ParkVisitors.Step drives the staff once per park tick, and the toilet is
        // cleaned by the handyman -- day stamped -- not by the stand-in.
        {
            var p = newPark(131);
            p.Clock.Advance(ParkClock.UnitsPerDay * 3, out _, out _, out _);
            var toilet = place(p, toiletAsset, p.At(14, 3)); toilet.Wear(50);
            var v = new ParkVisitors(p.Sim, p.Walk, () => 0);
            var staff = new ParkStaff(v, p.Clock, p.Serials, p.Rng.Next);
            v.Staff = staff;
            p.Rng.Override = k => k == 2 ? 1 : k == 16 ? 1 : null;
            var h = staff.Hire(StaffKind.Handyman, 0); staff.Drop(h, p.At(3, 2));
            uint before = staff.Now; int steps = 0;
            for (; steps < 1500 && toilet.Condition < 100; steps++) v.Step(.04, null);
            Check(toilet.Condition == 100 && toilet.LastCleanedDay == p.Clock.TotalDays && v.Serviced == 0 && staff.Now - before == (uint)steps,
                  $"attached: ParkVisitors.Step runs the staff once per park tick ({staff.Now - before} updates in {steps} steps) and the hired handyman cleans the toilet, day {toilet.LastCleanedDay} stamped, stand-in idle");
        }
        foreach (bool attach in new[] { true, false })
        {
            var p = newPark(130);
            var toilet = place(p, toiletAsset, p.At(14, 3)); toilet.Wear(50);
            var v = new ParkVisitors(p.Sim, p.Walk, () => 0);
            var staff = new ParkStaff(v, p.Clock, p.Serials, p.Rng.Next);
            if (attach) v.Staff = staff;
            for (int i = 0; i < 1500; i++) v.Step(.04, null);
            if (attach)
                Check(toilet.Condition == 50 && v.Serviced == 0,
                      $"stand-in: with staff attached and no handyman hired the 45 s auto-service is OFF -- the toilet stays at {toilet.Condition} after 60 s");
            else
                Check(toilet.Condition == 100 && v.Serviced >= 1,
                      $"stand-in (control): unattached, today's stand-in still cleans it ({v.Serviced} service(s))");
        }
    }

    static void LaserShow(WadArchive world, string worldName, CompiledAssets compiled, Action<bool, string> Check)
    {
        var laser = world.Entries.FirstOrDefault(e => e.Path.EndsWith("/slaser.sam", StringComparison.OrdinalIgnoreCase));
        if (laser == null) { Check(worldName != "SPACE", $"laser show: {worldName} ships none (only SPACE does)"); return; }
        var rec = compiled.For(worldName, laser.Path);
        var def = RideDefinition.Parse(Encoding.ASCII.GetString(world.Read(laser)), "/DATA/" + worldName + ".WAD" + laser.Path);
        compiled.Attach(new[] { def }, out _);
        var ride = new ParkRide { Definition = def, Origin = new ParkCell(10, 10), Width = 1, Height = 1, PlacementTurns = 1 };
        var f = StaffFeature.Of(ride);
        Check(rec?.Key == 356 && f != null && f.IsStaffRoom && !f.IsToilet && f.Entry == new ParkCell(9, 9),
              $"laser show: SPACE key 356 carries DBA bit 1, so the staff treat it as a staff room; its absent connection takes 0x369A30's (-1,-1) entry UNROTATED even placed turned ({f?.Entry})");
    }
}
