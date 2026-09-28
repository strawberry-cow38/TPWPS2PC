using System.Buffers.Binary;
using System.Text;
using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

/// <summary>⭐⭐ STAFF STEP 4, GUARDS AND ENTERTAINERS and the guests' side of both, run over the real disc
/// and this world's real park (its terrain, its fitted entrance and walkway, its own toilet), per
/// ghidra_tpw/notes/SPEC-staff-guards-entertainers.md against findings/staff-mechanics-guards.md §5-§6
/// and staff-handymen-entertainers.md §1.3-§1.5, §4, §5.
///
/// Every native constant the port uses is read back out of the EXECUTABLE -- the instruction word that
/// carries it, or the data word -- and compared by name. Every behaviour runs through
/// <see cref="ParkStaff.Update"/> or <see cref="ParkVisitors.Step"/> (the paths the viewer drives),
/// with <see cref="ParkStaff.Prank"/> -- the native `0x20D010(g, 1)`, the one labelled test hook -- as
/// the way to make a prank where the arm's own roll is not the subject. Each check that measures a rule
/// has a control that the same fixture must fail with the rule's other side (a camera moved one cell, a
/// guard one cell further), and every "all of them" prints its count. The teeth for the rest: the
/// mutation runner named in the commit turns each rule red.</summary>
static class GuardChecks
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

    sealed record Asset(string Stem, AssetResourceDatabase.Entry Record, byte[] Script, byte[] Aps,
                        Func<string, byte[]> Sibling, Func<RideDefinition> Definition);

    sealed class Park
    {
        public ParkPaths Paths; public ParkSim Sim; public GuestWalk Walk; public ParkVisitors Visitors;
        public ParkStaff Staff; public ParkClock Clock; public TestRandom Rng; public ParkCell B;
        public int NextId = 100;
        /// <summary>The coordinator's own random, which draws the idle ARM: 3 is inert with an empty
        /// rubbish meter; 2 heckles and 5 pranks (and their rand(1000) is the same value).</summary>
        public int Arm = 3;
        public NativeEntranceFlow Flow; public int Traffic; public int Bus;
        public readonly List<(StaffMember M, int Event, int Handle)> Sounds = new();
        public ParkCell At(int dx, int dz) => B.Offset(dx, dz);
        /// <summary>Staff only (the guests stand where they are).</summary>
        public void Tick(int n = 1) { for (int i = 0; i < n; i++) Staff.Update(); }
        /// <summary>One whole park tick as the viewer runs one: walk (the gate coordinator first), rides, staff, guests.</summary>
        public void Step(int n = 1) { for (int i = 0; i < n; i++) Visitors.Step(ParkSim.TickMilliseconds / 1000.0, null); }
        public Guest Body(int id) => Walk.Guests.FirstOrDefault(g => g.Id == id);
        public T Hire<T>(StaffKind kind, ParkCell at, int level = 0) where T : StaffMember
        {
            var slot = Staff.Candidates.Available(kind).First().Slot;
            var m = Staff.Hire(kind, slot);
            m.Level = level; m.Tiredness = 0; m.Morale = 50;
            if (!Staff.Drop(m, at)) throw new InvalidOperationException($"drop refused at {at}");
            return (T)m;
        }
        /// <summary>Hired and HELD on the hire tool's cursor over a cell: never updated, so he stands
        /// exactly there -- and, state 0 and not busy, he can be sent (natively too).</summary>
        public T Held<T>(StaffKind kind, ParkCell at) where T : StaffMember
        {
            var slot = Staff.Candidates.Available(kind).First().Slot;
            var m = Staff.Hire(kind, slot);
            m.Tiredness = 0; m.Morale = 50;
            Staff.Carry(m, Centre(at));
            return (T)m;
        }
        public Guest Guest(ParkCell at, int happiness = 60)
        {
            var g = Visitors.Arrive(at, at);
            Visitors.Needs.Set(g.Id, new VisitorWants { Cash = 1234, Happiness = (byte)happiness, PreferredIntensity = 50 });
            return g;
        }
        public int Happiness(int id) => Visitors.Needs.Of(id).Happiness;
    }

    static Point Centre(ParkCell c) => new((short)(c.X * 256 + 0x80), (short)(c.Z * 256 + 0x80));

    static VisitorNeeds FrozenNeeds()
    {
        var n = new VisitorNeeds(871) { SecondsPerRise = 1_000_000 };
        foreach (var key in n.Rates.Keys.ToArray()) n.Rates[key] = new(0, 0, false);
        n.Unknown78Bar = n.SickBar = n.ToiletBar = n.HungerBar = n.ThirstBar = 101;
        return n;
    }

    public static void Run(Disc disc, Model terrain, WadArchive data, WadArchive world, string worldName,
                           Action<bool, string> check)
    {
        void G(bool ok, string label) => check(ok, "guard: " + label);
        void E(bool ok, string label) => check(ok, "entertainer: " + label);
        var exe = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        byte[] elf = disc.Read(exe.Extent, exe.Size);
        Constants(elf, G, E);

        // ---- the park: this world's terrain, its entrance fitted, the starting path laid ---------
        var compiled = new CompiledAssets(new AssetResourceDatabase(data.Read(data.Find("/arsdb.dba"))),
                                          TextDatabase.Load(data, "eur"));
        Asset toiletAsset = null;
        foreach (var e in world.Entries.Where(e => e.Path.EndsWith(".sam", StringComparison.OrdinalIgnoreCase))
                                       .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
        {
            var rec = compiled.For(worldName, e.Path);
            if (rec?.Kind != AssetResourceDatabase.AssetKind.Feature || (rec.RawFeatureFlags & 1) == 0 || rec.Width != 1
                || rec.Depth != 1 || !rec.ConnectionA.IsPresent || rec.ConnectionA.Direction != 0) continue;
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
            toiletAsset = new Asset(stem, rec, world.Read(rse), aps == null ? null : world.Read(aps),
                                    n => world.Find(folder + n) is { } s ? world.Read(s) : null, Definition);
            break;
        }
        G(toiletAsset != null, $"{worldName}: the world ships a 1x1 scripted toilet to give guests a decision gate ({toiletAsset?.Stem})");
        if (toiletAsset == null) return;

        var paths = new ParkPaths(terrain);
        paths.SetEntrance(ParkEntrance.ReadExecutable(elf));
        G(paths.EntranceEntry != null, $"{worldName}: the entrance fits ({paths.EntranceEntry})");
        if (paths.EntranceEntry is not { } entry) return;
        var selection = NativeParkSelection.Ordinary("/DATA/" + worldName + ".WAD", AuditPark.Mps);
        var bus = NativeBusCatalogue.Read(disc, selection);
        G(bus.Point0 == new ParkCell(entry.XStart, entry.ZRow) && paths.EntranceKind(bus.StagingPoint) == NativeTileView.KindWalkway
          && paths.EntranceKind(new ParkCell(entry.XStart, entry.ZRow)) == NativeTileView.KindWalkway,
          $"entrance table entry: exit point +0/+1 {bus.Point0} is the walkway's corridor start, staging +2/+3 {bus.StagingPoint} is on the walkway, mouth +0x10/+0x11 ({entry.XCol},{entry.ZEnd})");
        int pathMaterial = Enumerable.Range(1, paths.Materials.Count - 1).First(i => ParkPaths.Classify(paths.Materials[i]) == ParkPathKind.Path);
        foreach (var (x, z) in entry.StartingPath())
            if (paths.CanLay(new ParkCell(x, z)) && paths.Kind(new ParkCell(x, z)) == ParkPathKind.None) paths.Lay(new ParkCell(x, z), pathMaterial);
        // A clear 32x11 block of the park's own ground for the yard, found (as the staff checks do).
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
        G(origin != null, $"{worldName}: a clear 32x11 block for the yard ({origin})");
        if (origin == null) return;
        var B = origin.Value.Offset(1, 1);
        for (int x = 0; x < 30; x++) paths.Lay(B.Offset(x, 2), pathMaterial);     // the corridor, row +2
        for (int x = 0; x < 30; x++) paths.Lay(B.Offset(x, 5), pathMaterial);     // a second row, +5
        for (int z = 2; z <= 5; z++) paths.Lay(B.Offset(20, z), pathMaterial);    // joined at x+20
        // The starting path runs C+1 rows into the park; carry it on into the park (while it can be laid)
        // so a guard in the park has somewhere to be caught on.
        var inside = new List<ParkCell>();
        for (int z = entry.ZEnd; z < entry.ZEnd + 14; z++)
        {
            var c = new ParkCell(entry.XCol, z);
            if (paths.Kind(c) == ParkPathKind.None && paths.CanLay(c)) paths.Lay(c, pathMaterial);
            if (paths.Kind(c) != ParkPathKind.Path) break;
            inside.Add(c);
        }
        G(inside.Count >= 8, $"{worldName}: {inside.Count} path cells run into the park from the mouth");

        Park NewPark(int seed)
        {
            var sim = new ParkSim(paths);
            var walk = new GuestWalk(paths);
            var rng = new TestRandom(seed);
            var p = new Park { Paths = paths, Sim = sim, Walk = walk, Rng = rng, B = B, Clock = new ParkClock() };
            p.Visitors = new ParkVisitors(sim, walk, () => p.Arm) { Needs = FrozenNeeds() };
            p.Staff = new ParkStaff(p.Visitors, p.Clock, new NativeActivationSequence(1000, "guard checks fixture"), rng.Next);
            p.Visitors.Staff = p.Staff;
            p.Staff.StagingCell = bus.StagingPoint;
            p.Staff.HandleSound = (m, bank, id, handle) => { if (bank == 8) p.Sounds.Add((m, id, handle)); };
            // Deterministic defaults: never a 1-in-16 sound, the watch cooldown's spawn draw 0.
            rng.Override = n => n == 16 ? 1 : n == 300 ? 0 : null;
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
        // A guest who has used a facility has a decision gate, so 0x20C930's arms are drawn for him (the
        // port's adapter picks a destination for a guest without one).
        Guest Visited(Park p, ParkCell toiletAt, ParkCell from)
        {
            var toilet = Place(p, toiletAsset, toiletAt);
            p.Sim.SetOpen(toilet.Id, true); toilet.Set("VAR_BROKEN", 0);
            var g = p.Guest(from);
            var w = p.Visitors.Needs.Of(g.Id); w.Toilet = 100; p.Visitors.Needs.Set(g.Id, w);
            if (!p.Visitors.SendTo(g, toilet)) throw new InvalidOperationException("guest cannot reach the toilet");
            for (int t = 0; t < 4000 && p.Visitors.Relieved == 0; t++) p.Step();
            p.Step(2);
            return p.Body(g.Id) ?? throw new InvalidOperationException("the guest did not come back out of the toilet");
        }

        Shows(NewPark, E);
        Watching(NewPark, E);
        Heckling(NewPark, Visited, E, G);
        Pranks(NewPark, Visited, G);
        CameraRule(NewPark, G);
        Chase(NewPark, Place, toiletAsset, G);
        Ejection(NewPark, entry, bus, inside, G);
        StinkTable(NewPark, G);
    }

    // =============================================================================================
    // The numbers, out of the executable.

    static void Constants(byte[] elf, Action<bool, string> G, Action<bool, string> E)
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
        uint Insn(uint va) => U32(At(va));
        // The immediate of an I-type instruction, and that the opcode is the expected one:
        // 0x09 addiu, 0x0B sltiu, 0x0D ori, 0x0F lui.
        int Imm(uint va, int op) => (int)(Insn(va) >> 26) == op ? (short)(Insn(va) & 0xFFFF) : int.MinValue;
        const int Addiu = 0x09, Sltiu = 0x0B, Ori = 0x0D, Lui = 0x0F;
        int Word(uint va) => unchecked((int)U32(At(va)));

        // Guard.
        G(Imm(0x141808, Addiu) == Guard.ChaseTicks && Imm(0x141830, Ori) == Guard.ChaseSpeed << 3 && Imm(0x1414A4, Ori) == Guard.PatrolSpeed << 3,
          $"constants: 0x1417D0 deadline now + {Imm(0x141808, Addiu)} and speed bits {Imm(0x141830, Ori) >> 3}; find work 0x1413E0 speed {Imm(0x1414A4, Ori) >> 3}");
        G(Imm(0x141064, Addiu) == Guard.CatchMorale && Imm(0x141080, Addiu) == Guard.CatchTiredness
          && Imm(0x140EA4, Addiu) == -Guard.GiveUpMorale && Imm(0x140BC4, Addiu) == -Guard.GiveUpWalkingMorale,
          $"constants: ledger -- catch +{Imm(0x141064, Addiu)} morale +{Imm(0x141080, Addiu)} tiredness (0x141064/0x141080), give up {Imm(0x140EA4, Addiu)} in 0x21, {Imm(0x140BC4, Addiu)} mid-walk");
        G(Imm(0x141478, Addiu) == Guard.SoundIdle && Imm(0x141538, Addiu) == Guard.SoundPatrol && Imm(0x1418AC, Addiu) == Guard.SoundDispatched,
          $"constants: bank-8 events 0x{Imm(0x141478, Addiu):X} idle, 0x{Imm(0x141538, Addiu):X} patrol, 0x{Imm(0x1418AC, Addiu):X} dispatched");
        G(Imm(0x14D580, Sltiu) == 64 && Imm(0x14D5E0, Sltiu) == 25 && Imm(0x14D5D4, Addiu) == 64,
          $"constants: 0x14D3E0 accepts d2 < {Imm(0x14D580, Sltiu)} with a camera within d2 <= {Imm(0x14D5D4, Addiu)} (0x14D238's radius argument), else d2 < {Imm(0x14D5E0, Sltiu)}");
        G(Imm(0x1412BC, Addiu) + 1 == Guard.MouthTries && Imm(0x141354, Addiu) == 0x80 && Imm(0x141230, Addiu) == 0x21 && Imm(0x1410B0, Addiu) == 0x11,
          $"constants: route flags -- chase 0x{Imm(0x1410B0, Addiu):X}, to staging 0x{Imm(0x141230, Addiu):X}, to the mouth 0x{Imm(0x141354, Addiu):X} tried {Imm(0x1412BC, Addiu) + 1} times");
        // Entertainer.
        E(Imm(0x12DCDC, Addiu) == Entertainer.ShowChance && Imm(0x12DD4C, Addiu) == Entertainer.EffectorRadius2
          && Imm(0x12DD54, Addiu) == Entertainer.EffectorFlags && Imm(0x12DFC4, Addiu) == Entertainer.ShowTicks,
          $"constants: 0x12DCC8 rand({Imm(0x12DCDC, Addiu)}), effector radius2 {Imm(0x12DD4C, Addiu)} flags {Imm(0x12DD54, Addiu)}; 0x12DF40 show {Imm(0x12DFC4, Addiu)} ticks");
        E(Imm(0x12DF8C, Addiu) == Entertainer.PerformTiredness && Imm(0x12DFA8, Addiu) == Entertainer.PerformMorale
          && Imm(0x12E060, Addiu) == Entertainer.SoundTada && Imm(0x12E088, Ori) == StaffTables.LogicalAfterShow && Imm(0x12DE0C, Addiu) == Entertainer.SoundNoShow,
          $"constants: performing +{Imm(0x12DF8C, Addiu)} tiredness +{Imm(0x12DFA8, Addiu)} morale; end sound 0x{Imm(0x12E060, Addiu):X} logical {Imm(0x12E088, Ori)}; no-show sound 0x{Imm(0x12DE0C, Addiu):X}");
        E(Imm(0x12E2BC, Addiu) == -Entertainer.HeckleMorale && Imm(0x12E2E4, Addiu) == Entertainer.ShockSteps && Imm(0x12E2E8, Addiu) == Entertainer.ShockStepTicks
          && Imm(0x12E350, Addiu) == -Entertainer.ShockedMorale && Imm(0x12E518, Sltiu) == Entertainer.GuardReach + 1 && Imm(0x12E540, Addiu) == -Entertainer.NoGuardMorale,
          $"constants: 0x12E278 morale {Imm(0x12E2BC, Addiu)}, deadline rand({Imm(0x12E2E4, Addiu)}) x {Imm(0x12E2E8, Addiu)}; 0x12E320 {Imm(0x12E350, Addiu)} per update, guard d < {Imm(0x12E518, Sltiu)}, else {Imm(0x12E540, Addiu)}");
        // The guests' side.
        E(Imm(0x20FE20, Addiu) == 300 && Imm(0x20FE14, Addiu) == 60 && StaffTables.EntertainerWatchTicks(0) == 300 && StaffTables.EntertainerWatchTicks(4) == 540
          && Imm(0x210900, Addiu) == ParkStaff.WatchHappiness && Imm(0x21092C, Addiu) == ParkStaff.WatchCooldownTicks
          && Imm(0x2107D0, Ori) == ParkStaff.WatchLogical && Imm(0x210908, Ori) == ParkStaff.WatchEndLogical,
          $"constants: watch {Imm(0x20FE20, Addiu)} + {Imm(0x20FE14, Addiu)}L (0x20FB88), then +{Imm(0x210900, Addiu)} happiness and a {Imm(0x21092C, Addiu)}-tick cooldown, logical {Imm(0x2107D0, Ori)} then {Imm(0x210908, Ori)} (0x2107A0)");
        G(Word(0x2EEB64) == ParkStaff.HeckleChance && Word(0x2EEB68) == ParkStaff.PrankChance && Word(0x2EEB98) == ParkStaff.PrankHappinessBelow
          && Imm(0x20CDE4, Sltiu) == ParkStaff.HeckleReach && Imm(0x20CE54, Ori) == ParkStaff.HeckleLogical && Imm(0x20CE34, Addiu) == 2,
          $"constants: [0x2EEB64] heckle {Word(0x2EEB64)}/1000, [0x2EEB68] prank {Word(0x2EEB68)}/1000 below happiness [0x2EEB98] {Word(0x2EEB98)}; heckler within {Imm(0x20CDE4, Sltiu)}, logical {Imm(0x20CE54, Ori)}, counter {Imm(0x20CE34, Addiu)}");
        G(Imm(0x1822FC, Sltiu) == PrankStinks.Capacity && Imm(0x182404, Lui) == 0x4620 && Imm(0x182434, Addiu) == PrankStink.ParticleTemplateId
          && Imm(0x20D1F4, Addiu) == 2 && Imm(0x20D208, Addiu) == 0x14,
          $"constants: stink table {Imm(0x1822FC, Sltiu)} entries, spawn scale 0x{Imm(0x182404, Lui):X4}0000 (10240f), template {Imm(0x182434, Addiu)}; the prank's counters {Imm(0x20D1F4, Addiu)} and 0x{Imm(0x20D208, Addiu):X}");
        // The prank's allocation gate: `beqz $a0` over the place AND the stink.
        uint gate = Insn(0x20D148);
        G((gate >> 26) == 0x04 && ((gate >> 21) & 31) == 4 && ((gate >> 16) & 31) == 0 && 0x20D148 + 4 + ((short)(gate & 0xFFFF) << 2) == 0x20D1F0,
          $"constants: 0x20D148 is beqz $a0 -> 0x20D1F0, past 0x15E2B8 and 0x1822D0 -- no litter, no stink");
    }

    // =============================================================================================
    // The show (0x12DE40, 0x12DCC8, 0x12DF40).

    static void Shows(Func<int, Park> newPark, Action<bool, string> E)
    {
        // Trigger: rand(3) == 0 AND a guest within dx2 + dz2 < 2 of him.
        (bool Show, Entertainer Ent, Park P) Try(int rand3, (int dx, int dz)? guestAt)
        {
            var p = newPark(10 + rand3 * 7 + (guestAt?.dx ?? 9) * 3 + (guestAt?.dz ?? 9));
            p.Rng.Override = n => n == 3 ? rand3 : n == 16 ? 1 : n == 300 ? 0 : null;
            var at = p.At(10, 2);
            if (guestAt is { } d) p.Guest(at.Offset(d.dx, d.dz == 0 ? 0 : 3 * d.dz));
            var e = p.Hire<Entertainer>(StaffKind.Entertainer, at);
            p.Tick();
            return (e.State == Entertainer.StatePerforming, e, p);
        }
        // Guests stand on path cells: (1,0) is on the corridor; (0, +1) means the second row three rows down
        // (not adjacent), so the "diagonal" case uses two corridor cells instead.
        var own = Try(0, (0, 0)); var next = Try(0, (1, 0)); var none = Try(0, null); var two = Try(0, (2, 0)); var roll = Try(1, (0, 0));
        E(own.Show && next.Show && !none.Show && !two.Show && !roll.Show,
          $"show trigger: rand(3) == 0 and a guest on his cell or the next starts one (own {own.Show}, next {next.Show}); "
          + $"control: no guest {none.Show}, two cells away {two.Show}, rand(3) = 1 {roll.Show}");
        E(none.Ent.State == StaffMember.StatePatrol && roll.Ent.State == StaffMember.StatePatrol
          && none.P.Sounds.Count(s => s.Event == Entertainer.SoundNoShow && s.Handle == Entertainer.HandleNoShow) == 1,
          "no show: state 0xD (patrol) and bank-8 0xA4 on handle P+0x5C");
        var fx = own.Ent.Effector;
        E(fx != null && fx.Active && fx.X == own.Ent.Cell.X && fx.Z == own.Ent.Cell.Z && fx.Radius2 == 1 && fx.Flags == 2 && own.P.Staff.Effectors.Count == 1
          && none.P.Staff.Effectors.Count == 0,
          $"effector: a show registers ONE effector at his cell ({fx?.X},{fx?.Z}) radius2 {fx?.Radius2} flags {fx?.Flags}; none without a show");
        // 2-D adjacency: a guest one cell diagonally (dx2 + dz2 = 2) is not adjacent.
        {
            var p = newPark(31);
            p.Rng.Override = n => n == 3 ? 0 : n == 16 ? 1 : n == 300 ? 0 : null;
            var e = p.Hire<Entertainer>(StaffKind.Entertainer, p.At(20, 3));   // the joining column
            p.Guest(p.At(21, 2));                                             // diagonal: corridor at x+21
            p.Tick();
            var q = newPark(32);
            q.Rng.Override = p.Rng.Override;
            var e2 = q.Hire<Entertainer>(StaffKind.Entertainer, q.At(20, 3));
            q.Guest(q.At(20, 2));                                             // straight up the column
            q.Tick();
            E(e.State == StaffMember.StatePatrol && e2.State == Entertainer.StatePerforming,
              $"adjacency is 2-D dx2 + dz2 < 2: a diagonal guest does not start a show ({e.State:x2}), one straight above does ({e2.State:x2})");
        }
        // Duration: start + 600 < now ends it; each 4-tick phase +2 tiredness +1 morale; TADA and logical 11.
        {
            var (_, e, p) = Try(0, (0, 0));
            uint start = e.Stamp; int performing = 0; int t0 = e.Tiredness, m0 = e.Morale; int phases = 0;
            bool logical16 = true;
            while (e.State == Entertainer.StatePerforming && performing < 2000)
            {
                if ((p.Staff.Now & 3) == (e.Serial & 3)) phases++;
                p.Tick(); performing++;
                if (e.State == Entertainer.StatePerforming) logical16 &= e.LogicalRequest == StaffTables.LogicalWork;
            }
            int expectT = Math.Min(100, t0 + 2 * phases), expectM = Math.Min(100, m0 + phases);
            E(performing == 601 && logical16,
              $"show length: performing from start {start} until start + 600 < now -- {performing} updates (600 in 0xC plus the ending one), logical 16 throughout");
            E(e.State == StaffMember.StateIdle && e.LogicalRequest == StaffTables.LogicalAfterShow
              && p.Sounds.Count(s => s.Event == Entertainer.SoundTada && s.Handle == Entertainer.HandleTada) == 1,
              $"show end: state 0, logical 11 and bank-8 0x87 TADA on handle P+0x64");
            E(e.Tiredness == expectT && e.Morale == expectM,
              $"performing ledger: tiredness {t0} -> {e.Tiredness} and morale {m0} -> {e.Morale} over {phases} phase ticks (+2 / +1 each, capped 100)");
            E(e.Effector != null && e.Effector.Active,
              "the effector outlives the show's end: nothing frees it at 0x12DF40");
            p.Rng.Override = n => n == 3 ? 1 : n == 16 ? 1 : null;
            p.Tick();
            E(e.Effector == null && p.Staff.Effectors.Count == 0, "and the next find work frees it (0x14D038 in 0x12DE40)");
        }
        // Cut short: the guest leaves his side, the show ends the next update.
        {
            var (_, e, p) = Try(0, (1, 0));
            p.Tick(40);
            var g = p.Walk.Guests.Single();
            p.Walk.Remove(g.Id);
            int more = 0;
            while (e.State == Entertainer.StatePerforming && more < 50) { p.Tick(); more++; }
            E(more == 1 && e.LogicalRequest == StaffTables.LogicalAfterShow,
              $"nobody adjacent ends the show at the next update ({more} update after the guest left, logical 11)");
        }
        // Twenty effectors: the 21st show has none (0x14CFB8 returns 0), and is still a show.
        {
            var p = newPark(40);
            var taken = new List<StaffEffector>();
            var alloc = typeof(ParkEffectors).GetMethod("Allocate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            for (int i = 0; i < ParkEffectors.Capacity; i++) taken.Add((StaffEffector)alloc.Invoke(p.Staff.Effectors, null));
            p.Rng.Override = n => n == 3 ? 0 : n == 16 ? 1 : n == 300 ? 0 : null;
            p.Guest(p.At(10, 2));
            var e = p.Hire<Entertainer>(StaffKind.Entertainer, p.At(10, 2));
            p.Tick();
            E(taken.All(x => x != null) && p.Staff.Effectors.Count == 20 && e.State == Entertainer.StatePerforming && e.Effector == null,
              $"pool: 20 effectors ({taken.Count(x => x != null)} taken by the fixture); the 21st show runs with none");
        }
    }

    // =============================================================================================
    // Guests watching (0x20FB88's effector phase, state 0x1C 0x2107A0).

    static void Watching(Func<int, Park> newPark, Action<bool, string> E)
    {
        (Park P, Entertainer Ent, Guest G) Show(int level, int seed)
        {
            var p = newPark(seed);
            p.Rng.Override = n => n == 3 ? 0 : n == 16 ? 1 : n == 300 ? 0 : null;
            var g = p.Guest(p.At(11, 2));
            var e = p.Hire<Entertainer>(StaffKind.Entertainer, p.At(10, 2), level);
            p.Tick();
            if (e.State != Entertainer.StatePerforming) throw new InvalidOperationException("the show did not start");
            return (p, e, g);
        }
        // Start within one 8-tick phase (the cooldown's spawn draw forced 0), stop where he stands.
        foreach (int level in new[] { 0, 2, 4 })
        {
            var (p, e, g) = Show(level, 50 + level);
            int waited = 0;
            while (!p.Staff.IsWatching(g.Id) && waited < 40) { p.Tick(); waited++; }
            var w = p.Staff.Watching.GetValueOrDefault(g.Id);
            uint began = p.Staff.Now - 1;
            int h0 = p.Happiness(g.Id);
            int watching = 0;
            while (p.Staff.IsWatching(g.Id) && watching < 2000) { p.Tick(); watching++; }
            uint cooldown = p.Staff.WatchCooldown(g.Id) ?? 0;
            int expect = 300 + 60 * level;
            E(w != null && ReferenceEquals(w.Entertainer, e) && w.Until == began + (uint)expect && watching == expect + 1
              && p.Happiness(g.Id) == h0 + 5 && cooldown == p.Staff.Now - 1 + 900,
              $"watch, level {level}: starts within {waited} ticks of the show, lasts until now > start + 300 + 60L = {expect} ({watching} updates), then happiness {h0} -> {p.Happiness(g.Id)} (+5) and the cooldown now + 900");
        }
        // The shows' end cuts every watch short, and the +5 is paid anyway.
        {
            var (p, e, g) = Show(4, 60);
            while (!p.Staff.IsWatching(g.Id)) p.Tick();
            int h0 = p.Happiness(g.Id);
            // The entertainer is heckled: Shocked (0x20), not 0xC -- the watch ends at the next update.
            e.Heckled(new GuestTarget(9999));
            p.Tick();
            E(!p.Staff.IsWatching(g.Id) && p.Happiness(g.Id) == h0 + 5,
              $"a show that stops (here a heckle's 0x20) ends the watch at once and still pays +5 ({h0} -> {p.Happiness(g.Id)})");
        }
        // The cooldown: within 900 ticks of a watch's end nothing restarts it; after, it does.
        {
            var (p, e, g) = Show(0, 61);
            while (!p.Staff.IsWatching(g.Id)) p.Tick();
            while (p.Staff.IsWatching(g.Id)) p.Tick();
            uint ended = p.Staff.Now - 1;
            // A second show from the same entertainer right beside him.
            p.Rng.Override = n => n == 3 ? 0 : n == 16 ? 1 : null;
            int restarted = -1;
            for (int t = 0; t < 1200 && restarted < 0; t++)
            {
                e.Tiredness = 0;                                         // the fixture keeps him rested (no staff room)
                p.Tick();
                if (p.Staff.IsWatching(g.Id)) restarted = (int)(p.Staff.Now - 1 - ended);
            }
            E(restarted > 900 && restarted <= 908 + 602,
              $"cooldown: the guest does not watch again until more than 900 ticks after the last watch ended (again after {restarted})");
        }
        // Stands still: a guest walking past through the zone stops where he is, then walks on.
        {
            var p = newPark(62);
            p.Rng.Override = n => n == 3 ? 0 : n == 16 ? 1 : n == 300 ? 0 : null;
            var g = p.Guest(p.At(4, 2));
            var e = p.Hire<Entertainer>(StaffKind.Entertainer, p.At(10, 2));
            p.Walk.Send(g, p.At(28, 2));
            bool watched = false, stood = true, movedAfter = false; Vector3Like at = default; int watchTicks = 0;
            for (int t = 0; t < 2000 && !movedAfter; t++)
            {
                var before = g.Position;
                p.Step();
                if (p.Staff.IsWatching(g.Id))
                {
                    if (!watched) at = new(g.Position.X, g.Position.Z);
                    watched = true; watchTicks++;
                    stood &= g.Position == before || watchTicks == 1;
                }
                else if (watched && g.Position != before) movedAfter = true;
            }
            E(watched && stood && movedAfter,
              $"a walking guest reaching the zone stops where it is ({at.X:F2},{at.Z:F2}) for {watchTicks} ticks and walks on afterwards (not routed)");
        }
        // Facing: the state turns the guest to the entertainer (dx decides, else dz; on his own cell pi).
        {
            var (p, e, g) = Show(0, 63);
            var own = p.Guest(e.Cell);                                   // a second guest on his very cell
            while (!p.Staff.IsWatching(g.Id) || !p.Staff.IsWatching(own.Id)) p.Tick();
            E(p.Staff.Watching[g.Id].FacingQuarterTurns == 3 && p.Staff.Watching[own.Id].FacingQuarterTurns == 2,
              $"facing: the entertainer one cell to -x -> 3pi/2 (quarter turns {p.Staff.Watching[g.Id].FacingQuarterTurns}); on his own cell "
              + $"dx = dz = 0 -> pi ({p.Staff.Watching[own.Id].FacingQuarterTurns}): dx decides before dz");
        }
        // The spawn value G+0x6C = spawn + rand(300): with no show nothing is drawn (the guests' stream is
        // the entrance's too); the first meeting with a show draws it once, from the FIRST SIGHT, not now.
        {
            var p = newPark(64);
            int draws = 0;
            p.Rng.Override = n => { if (n == 300) { draws++; return 299; } return n == 3 ? 0 : n == 16 ? 1 : null; };
            var g = p.Guest(p.At(11, 2));
            var far = p.Guest(p.At(20, 2));
            for (int t = 0; t < 400; t++) p.Tick();
            int noShow = draws;
            var e = p.Hire<Entertainer>(StaffKind.Entertainer, p.At(10, 2));
            int began = -1, waited = 0;
            while (!p.Staff.IsWatching(g.Id) && waited < 700)
            {
                p.Tick(); waited++;
                if (began < 0 && e.State == Entertainer.StatePerforming) began = waited;
            }
            E(noShow == 0 && draws == 1 && began > 0 && p.Staff.IsWatching(g.Id) && waited - began <= 8 && !p.Staff.IsWatching(far.Id),
              $"spawn cooldown: 400 ticks with no show draw rand(300) {noShow} times; the first show draws it {draws - noShow} time(s) "
              + $"(the guest out of range: none), and first sight + 299 is long past: the guest watches {waited - began} ticks after the show starts");
        }
    }

    readonly record struct Vector3Like(float X, float Z);

    // =============================================================================================
    // Heckling: the guest's arm 2 and the entertainer's Shocked state.

    static void Heckling(Func<int, Park> newPark, Func<Park, ParkCell, ParkCell, Guest> visited,
                         Action<bool, string> E, Action<bool, string> G)
    {
        // Through the coordinator: the arm 2 roll (the coordinator's random is 2: arm 2, rand(1000) = 2 < 10).
        (Park P, Guest G, Entertainer E) Heckle(int armValue, int entertainerDx, int seed)
        {
            var p = newPark(seed);
            var g = visited(p, p.At(14, 3), p.At(12, 2));
            var e = p.Hire<Entertainer>(StaffKind.Entertainer, g.Cell.Offset(entertainerDx, 0));
            p.Rng.Override = n => n == 3 ? 1 : n == 16 ? 1 : n == 5 ? 4 : n == 300 ? 0 : null;
            p.Tick();                                        // he sets off on patrol (state 0xD), unheckled
            p.Arm = armValue;
            p.Step();
            p.Arm = 3;
            return (p, g, e);
        }
        var near = Heckle(2, 4, 70); var far = Heckle(2, 5, 71); var roll = Heckle(14, 4, 72);
        E(near.E.State == Entertainer.StateShocked && near.E.Heckler?.Id == near.G.Id && near.E.Heckles == 1
          && near.P.Staff.AdvisorEvents[2] == 1 && near.P.Staff.Heckles == 1,
          $"heckle: arm 2 with rand(1000) = 2 < 10 and an entertainer 4 cells away Shocks him (state 0x{near.E.State:x2}), remembers the heckler, event counter 2 = {near.P.Staff.AdvisorEvents[2]}");
        E(far.E.State != Entertainer.StateShocked && far.P.Staff.Heckles == 0 && roll.E.State != Entertainer.StateShocked && roll.P.Staff.Heckles == 0,
          $"heckle (control): 5 cells away (sltiu 5) nobody is heckled ({far.P.Staff.Heckles}); rand(1000) = 14 >= 10 nobody either ({roll.P.Staff.Heckles})");
        E(near.E.GoalDepth == 1 && near.E.Goals[0] != Entertainer.StateShocked,
          $"heckle: the interrupted state 0x{near.E.Goals[0]:x2} is PUSHED under 0x20");

        // Shocked: -5 at the heckle, -10 every update, and past rand(5)*60 the nearest free guard within 6.
        (Park P, Entertainer E, Guard Near, Guard Far, int Morale0) Shock(int guardDx, bool busy, int seed)
        {
            var p = newPark(seed);
            var e = p.Hire<Entertainer>(StaffKind.Entertainer, p.At(10, 2));
            p.Rng.Override = n => n == 3 ? 1 : n == 16 ? 1 : n == 5 ? 1 : n == 300 ? 0 : null;
            p.Tick();
            e.Morale = 100;
            var heckler = p.Guest(p.At(12, 2));
            var guard = p.Held<Guard>(StaffKind.Guard, p.At(10 + guardDx, 2));
            Guard far = null;
            if (busy) { far = guard; guard.Chase(new GuestTarget(heckler.Id)); guard = null; }
            int m0 = e.Morale;
            e.Heckled(new GuestTarget(heckler.Id));
            return (p, e, guard, far, m0);
        }
        {
            var (p, e, g, _, m0) = Shock(6, false, 73);
            uint deadline = e.Stamp; int m1 = e.Morale; int updates = 0;
            while (e.State == Entertainer.StateShocked && updates < 400) { p.Tick(); updates++; }
            E(m1 == m0 - 5 && deadline == p.Staff.Now - (uint)updates + 60 && updates == 62 && e.Morale == Math.Max(0, m1 - 10 * updates),
              $"Shocked: morale {m0} -> {m1} at the heckle, then -10 on each of {updates} updates (deadline now + rand(5)=1 x 60, strict) -> {e.Morale}");
            G(g.State == Guard.StateChase && g.Target is GuestTarget t && t.Id == ((GuestTarget)e.Target).Id && g.SpeedBits == Guard.ChaseSpeed
              && p.Sounds.Any(s => ReferenceEquals(s.M, g) && s.Event == Guard.SoundDispatched && s.Handle == Guard.HandleDispatched),
              $"called by a Shocked entertainer: the guard 6 cells away (Manhattan <= 6; held on the cursor, so standing still) chases the heckler -- state 0x21, speed 30, bank-8 0x88 on P+0x68");
            E(e.GoalDepth == 0 && e.State == e.Goals[0] && e.GuardsCalled == 1,
              $"Shocked pops back to 0x{e.State:x2}, the state the heckle interrupted");
        }
        {
            var (p, e, g, _, _) = Shock(7, false, 74);
            int updates = 0;
            while (e.State == Entertainer.StateShocked && updates < 400) { p.Tick(); updates++; }
            E(g.State != Guard.StateChase && e.GuardsCalled == 0 && e.Morale == Math.Max(0, 100 - 5 - 10 * updates - 5),
              $"Shocked (control): the guard 7 cells away is not called and the entertainer loses 5 more ({e.Morale})");
        }
        {
            var (p, e, _, busy, _) = Shock(1, true, 75);
            while (e.State == Entertainer.StateShocked) p.Tick();
            G(busy.Busy && e.GuardsCalled == 0 && busy.Dispatches == 1,
              "Shocked: a BUSY guard (0x1416F0: chasing) is skipped even one cell away");
        }
    }

    // =============================================================================================
    // Pranks: the guest's arm 5 and 0x20D010(g, 1).

    static void Pranks(Func<int, Park> newPark, Func<Park, ParkCell, ParkCell, Guest> visited, Action<bool, string> G)
    {
        (Park P, Guest G, Guard Guard) Arm5(int armValue, int happiness, int seed)
        {
            var p = newPark(seed);
            var g = visited(p, p.At(14, 3), p.At(12, 2));
            var w = p.Visitors.Needs.Of(g.Id); w.Happiness = (byte)happiness; p.Visitors.Needs.Set(g.Id, w);
            var guard = p.Hire<Guard>(StaffKind.Guard, g.Cell.Offset(-3, 0));
            p.Rng.Override = n => n == 16 ? 1 : n == 300 ? 0 : null;
            p.Arm = armValue;
            p.Visitors.Step(ParkSim.TickMilliseconds / 1000.0, null);   // the guard's first update is his find work
            p.Arm = 3;
            return (p, g, guard);
        }
        var yes = Arm5(5, 24, 80); var happy = Arm5(5, 25, 81); var roll = Arm5(11, 10, 82);
        var item = yes.P.Staff.Litter.Active.FirstOrDefault();
        var cell = ParkStaff.GuestCell(yes.G);
        G(yes.P.Staff.Pranks == 1 && item != null && !item.Shown && !item.Vomit && Math.Abs(item.Position.X - ParkStaff.GuestFine(yes.G).X) <= 100
          && yes.P.Staff.Stinks.Count == 1 && yes.P.Staff.Stinks.Entries[0].CellX == cell.X && yes.P.Staff.Stinks.Entries[0].CellZ == cell.Z,
          $"prank: arm 5 with happiness 24 (< 25) and rand(1000) = 5 leaves ONE HIDDEN litter item within +-100 and a YellowStink in the guest's cell {cell}");
        G(yes.P.Staff.AdvisorEvents[2] == 1 && yes.P.Staff.AdvisorEvents[0x14] == 1 && yes.P.Staff.AdvisorEvents[0x15] == 0,
          $"prank: advisor event counters 2 and 0x14 += 1 ({yes.P.Staff.AdvisorEvents[2]}, {yes.P.Staff.AdvisorEvents[0x14]}), not the drop's 0x15");
        G(yes.Guard.State == Guard.StateChase && yes.Guard.Target is GuestTarget t && t.Id == yes.G.Id,
          "prank: 0x14D3E0 sends the free guard 3 cells away (d2 9 < 25) after the prankster");
        G(happy.P.Staff.Pranks == 0 && happy.P.Staff.Litter.Count == 0 && roll.P.Staff.Pranks == 0 && roll.P.Staff.Stinks.Count == 0,
          $"prank (control): happiness 25 is not < 25 ({happy.P.Staff.Pranks} pranks); rand(1000) = 11 >= 10 ({roll.P.Staff.Pranks})");
        // A full litter pool: no litter AND no stink (MIPS 0x20D148), but the counters and the guard still happen.
        {
            var p = newPark(83);
            var g = p.Guest(p.At(12, 2), 10);
            for (int i = 0; i < ParkLitter.Capacity; i++) p.Staff.Litter.Drop(Centre(p.At(1 + i % 25, 5)), false);
            var guard = p.Hire<Guard>(StaffKind.Guard, p.At(10, 2));
            p.Staff.Prank(g);
            G(p.Staff.Litter.Count == ParkLitter.Capacity && p.Staff.Litter.RefusedFull == 1 && p.Staff.Stinks.Count == 0
              && p.Staff.AdvisorEvents[2] == 1 && p.Staff.AdvisorEvents[0x14] == 1 && guard.State == Guard.StateChase,
              $"prank with the litter pool full (40): no litter, NO STINK (beqz at 0x20D148), still counters 2/0x14 and the guard ({p.Staff.Stinks.Count} stinks)");
        }
        // The guest carries on: the prank does not change its state (it stays Arrived and deciding).
        G(yes.P.Visitors.Plans.ContainsKey(yes.G.Id) && yes.G.State == GuestState.Arrived,
          "prank: the prankster's own state is unchanged -- it stands and decides on");
    }

    // =============================================================================================
    // 0x14D3E0's camera rule: 8 cells with a camera within 8 of the prankster, else 5.

    static void CameraRule(Func<int, Park> newPark, Action<bool, string> G)
    {
        bool Sent(int guardDx, int? cameraDx, byte cameraStatus = 1, int seed = 90)
        {
            var p = newPark(seed + guardDx * 11 + (cameraDx ?? 20));
            var at = p.At(12, 2);
            var g = p.Guest(at, 10);
            if (cameraDx is int c)
            {
                var camera = new StaffFeature(at.Offset(c, 0), at.Offset(c, 0), 8, cameraStatus, null, "camera");
                var basis = p.Staff.Features;
                p.Staff.Features = () => basis().Append(camera);
            }
            var guard = p.Hire<Guard>(StaffKind.Guard, at.Offset(guardDx, 0));
            p.Staff.Prank(g);
            return guard.State == Guard.StateChase;
        }
        bool at4 = Sent(4, null), at5 = Sent(5, null), at7cam = Sent(7, 8), at7far = Sent(7, 9), at8cam = Sent(8, 0), at7off = Sent(7, 8, 0);
        G(at4 && !at5, $"camera rule: with no camera a guard 4 cells away (d2 16 < 25) is sent, 5 cells away (d2 25) is not ({at4}/{at5})");
        G(at7cam && !at7far, $"camera rule: a camera 8 cells from the PRANKSTER (d2 64 <= 64) lets a guard 7 away (d2 49 < 64) be sent; a camera 9 away does not ({at7cam}/{at7far})");
        G(!at8cam, $"camera rule: even with a camera on the prankster, a guard 8 away (d2 64) is too far: strict < 64 ({at8cam})");
        G(!at7off, $"camera rule: a camera whose status +0x9A is 0 is not a camera to 0x14D238 ({at7off})");
        // Nearest wins, busy ones skipped, newest first on ties.
        {
            var p = newPark(95);
            var at = p.At(12, 2);
            var g = p.Guest(at, 10);
            var farther = p.Hire<Guard>(StaffKind.Guard, at.Offset(3, 0));
            var nearer = p.Hire<Guard>(StaffKind.Guard, at.Offset(-2, 0));
            var busy = p.Hire<Guard>(StaffKind.Guard, at.Offset(1, 0));
            busy.Chase(new GuestTarget(-1));
            p.Staff.Prank(g);
            G(nearer.State == Guard.StateChase && farther.State != Guard.StateChase && busy.Dispatches == 1 && nearer.Dispatches == 1,
              "dispatch: the NEAREST free guard is sent (2 away over 3 away); a busy one 1 away is skipped");
            var q = newPark(96);
            var gq = q.Guest(at, 10);
            var first = q.Hire<Guard>(StaffKind.Guard, at.Offset(2, 0));
            var second = q.Hire<Guard>(StaffKind.Guard, at.Offset(-2, 0));
            q.Staff.Prank(gq);
            G(second.State == Guard.StateChase && first.State != Guard.StateChase,
              "dispatch: equally near, the NEWEST hire wins (0x14D228 is newest first, strict <)");
        }
    }

    // =============================================================================================
    // The chase (0x1417D0, 0x140E08, 0x140B48) and the catch.

    static void Chase(Func<int, Park> newPark, Func<Park, Asset, ParkCell, ParkRide> place, Asset toiletAsset, Action<bool, string> G)
    {
        // Capture, the usual way: the guest stands; the guard walks and the WALK STEP (0x140B48) catches as
        // soon as he enters its cell -- no bonus.
        {
            var p = newPark(100);
            var g = p.Guest(p.At(12, 2), 10);
            var other = p.Hire<Guard>(StaffKind.Guard, p.At(25, 5));
            var guard = p.Hire<Guard>(StaffKind.Guard, p.At(9, 2));
            other.Chase(new GuestTarget(g.Id));                           // a second guard after the same guest
            int otherMorale = other.Morale;
            p.Staff.Prank(g);
            int m0 = guard.Morale, id = g.Id;
            int walked = 0; bool caughtWalking = false;
            for (int t = 0; t < 400 && guard.State != Guard.StateCarrying; t++)
            {
                byte before = guard.State;
                p.Tick(); walked++;
                if (guard.State == Guard.StateCarrying) caughtWalking = before == StaffMember.StateWalk;
            }
            G(guard.State == Guard.StateCarrying && guard.Carried?.GuestId == id && guard.Catches == 1 && caughtWalking,
              $"catch: the guard walks {walked} ticks and the walk step catches the guest on entering its cell -- state 0x3C, carrying a copy of guest {guard.Carried?.GuestId}");
            G(!p.Visitors.Plans.ContainsKey(id) && p.Body(id) == null && p.Visitors.Ejected == 1 && p.Visitors.WentHome == 0 && p.Staff.Caught == 1,
              $"catch: the guest is REMOVED from the park at once (event 4 → 0x14B368) -- no plan, no body, ejected {p.Visitors.Ejected}, not counted as going home");
            G(guard.LogicalRequest == StaffTables.LogicalWork && guard.CutRecordOnPush && guard.Morale == m0,
              $"catch mid-walk: logical 16 with the one-shot cut flag 0x200, and NO bonus (morale {m0} -> {guard.Morale})");
            G(other.Target == null && other.State != Guard.StateChase && other.Shown && other.Morale == otherMorale && other.GiveUps == 0,
              $"catch: the removal notice 0x14B9D0 resets EVERY guard chasing that guest -- the other one's target cleared, not chasing (0x{other.State:x2}), no give-up");
            G(guard.Carried.Position == guard.Position && guard.Carried.FacingQuarterTurns == guard.FacingQuarterTurns
              && p.Sounds.Count(s => ReferenceEquals(s.M, guard) && s.Event == Guard.SoundCarrying && s.Handle == Guard.HandleCarrying) >= 1,
              "catch: the copy stands at the guard's fine position, turned with him, and 0x1409E8 plays bank-8 0x89 on P+0x5C");
        }
        // Capture in 0x21: dispatched while already in the guest's cell -- +10 morale, +3 tiredness.
        {
            var p = newPark(105);
            var g = p.Guest(p.At(12, 2), 10);
            var guard = p.Held<Guard>(StaffKind.Guard, p.At(12, 2));
            p.Staff.Drop(guard, p.At(12, 2));                             // put down in the guest's own cell
            guard.Morale = 50; guard.Tiredness = 10;
            p.Staff.Prank(g);
            p.Tick();
            G(guard.State == Guard.StateCarrying && guard.Morale == 60 && guard.Tiredness == 13 && !p.Visitors.Plans.ContainsKey(g.Id),
              $"catch in state 0x21 (same cell at the chase tick): morale 50 -> {guard.Morale} (+10), tiredness 10 -> {guard.Tiredness} (+3)");
        }
        // One route leg: the guest walks away after the guard's route is laid; he reaches where it WAS and
        // gives up at the next 0x21 tick, the deadline having been overwritten with the request's tick.
        {
            var p = newPark(101);
            var g = p.Guest(p.At(14, 2), 10);
            var camera = new StaffFeature(p.At(14, 1), p.At(14, 1), 8, 1, null, "camera");   // lets a guard 7 away be sent
            var basis = p.Staff.Features; p.Staff.Features = () => basis().Append(camera);
            var guard = p.Hire<Guard>(StaffKind.Guard, p.At(7, 2));
            p.Staff.Prank(g);
            uint dispatchDeadline = guard.Stamp;
            p.Step();                                                    // 0x21: route requested, stamp := now
            uint stamped = guard.Stamp; int requests = p.Staff.RouteRequests.Admitted;
            p.Walk.Send(g, p.At(28, 2));                                 // it walks off along the corridor
            int m0 = guard.Morale; int t = 0;
            for (; t < 400 && guard.State != StaffMember.StateIdle; t++) p.Step();
            G(dispatchDeadline == stamped + Guard.ChaseTicks && guard.GiveUps == 1 && guard.Catches == 0
              && p.Visitors.Plans.ContainsKey(g.Id) && guard.Morale == Math.Max(0, m0 - 5) && p.Staff.RouteRequests.Admitted == requests,
              $"one leg: dispatched with deadline {dispatchDeadline} (now + 3600), the re-route stored now ({stamped}) over it, so on reaching the guest's OLD cell he gives up "
              + $"(morale {m0} -> {guard.Morale}, {guard.GiveUps} give-up, {guard.Catches} catches) while it walks on -- {p.Staff.RouteRequests.Admitted - requests} further route requests, guest at {g.Cell}");
        }
        // Queue immunity: a guest in a ride's queue (native queue lease) cannot be taken; the chase gives up -5.
        {
            var p = newPark(102);
            var toilet = place(p, toiletAsset, p.At(14, 3));
            p.Sim.SetOpen(toilet.Id, true); toilet.Set("VAR_BROKEN", 0);
            var g = p.Guest(p.At(12, 2), 10);
            var owner = new object();
            bool sent = p.Visitors.SendTo(g, toilet);
            var inputs = new NativeMotionInputs(() => 15, () => 0x4000, () => true);
            var queued = sent ? p.Visitors.AssignQueueRoute(g, toilet, owner, new[] { ParkStaff.GuestFine(g) }, inputs) : GuestWalk.NativeAssignment.Refused;
            var guard = p.Hire<Guard>(StaffKind.Guard, p.At(10, 2));
            int m0 = guard.Morale;
            guard.Chase(new GuestTarget(g.Id));
            p.Tick();
            G(queued == GuestWalk.NativeAssignment.Assigned && p.Visitors.Plans[g.Id].Intent == VisitorIntent.Queueing
              && guard.State == StaffMember.StateIdle && guard.Morale == m0 - 5 && guard.Catches == 0 && p.Visitors.Plans.ContainsKey(g.Id),
              $"queue immunity: a guest in a ride's queue (states 0x12..0x14) makes the chase give up at once, morale {m0} -> {guard.Morale}");
            // Mid-walk: the guest joins the queue while the guard walks; the walk step gives up with -2.
            var q = newPark(103);
            var toilet2 = place(q, toiletAsset, q.At(14, 3));
            q.Sim.SetOpen(toilet2.Id, true); toilet2.Set("VAR_BROKEN", 0);
            var g2 = q.Guest(q.At(12, 2), 10);
            var guard2 = q.Hire<Guard>(StaffKind.Guard, q.At(4, 2));
            guard2.Chase(new GuestTarget(g2.Id));
            for (int t = 0; t < 50 && !(guard2.State == StaffMember.StateWalk && guard2.Mode == Guard.ModeChase); t++) q.Tick();
            int m2 = guard2.Morale;
            bool sent2 = q.Visitors.SendTo(g2, toilet2);
            var q2 = sent2 ? q.Visitors.AssignQueueRoute(g2, toilet2, owner, new[] { ParkStaff.GuestFine(g2) }, inputs) : GuestWalk.NativeAssignment.Refused;
            q.Tick();
            G(q2 == GuestWalk.NativeAssignment.Assigned && guard2.State == StaffMember.StateIdle && guard2.Morale == m2 - 2 && guard2.Target == null,
              $"queue immunity mid-walk (0x140B48): the guard walking in mode 8 gives up with morale -2 ({m2} -> {guard2.Morale})");
        }
        // A guest who leaves by another road: the removal notice resets the guard at the next update.
        {
            var p = newPark(104);
            var g = p.Guest(p.At(20, 5), 10);
            var guard = p.Hire<Guard>(StaffKind.Guard, p.At(10, 2));
            guard.Chase(new GuestTarget(g.Id));
            p.Tick(2);
            p.Walk.Remove(g.Id);
            typeof(ParkVisitors).GetMethod("ShowOut", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(p.Visitors, new object[] { g.Id, true });
            int m0 = guard.Morale;
            p.Tick();
            G(guard.State != Guard.StateChase && guard.Target == null && guard.Morale == m0 && guard.Shown,
              "a chased guest who goes home sends the notice: the guard is reset (target 0, state 0, shown) with no morale loss");
        }
    }

    // =============================================================================================
    // Carrying out through the gate (0x3C → 0x27 → 0x2E → 0x2F → 0x30 → 0x3B → 0x2E → 0x2F → 0x37 → 0).

    static void Ejection(Func<int, Park> newPark, ParkEntranceEntry entry, NativeBusCatalogue bus, List<ParkCell> inside,
                         Action<bool, string> G)
    {
        var p = newPark(110);
        var services = new NativeEntranceFlow.Services(n => 0, _ => default, default, (_, _, _, _, _) => false,
            () => Array.Empty<NativeEntranceFlow.RouteResult>(), _ => false, _ => true, () => 0x4000, _ => false,
            _ => null, _ => { }, (_, _) => { });
        p.Flow = new NativeEntranceFlow(p.Visitors, services);
        p.Staff.Gate = p.Flow;
        p.Walk.BeforeStep = tick => p.Traffic = p.Flow.Tick(tick, p.Bus, p.Traffic);
        var g = p.Guest(inside[4], 10);
        var guard = p.Hire<Guard>(StaffKind.Guard, inside[7]);
        p.Staff.Prank(g);
        var trail = new List<(byte State, byte Mode, ParkCell Cell, int P, int R, bool Carrying)>();
        void Record() { var last = trail.Count == 0 ? default : trail[^1];
            if (trail.Count == 0 || last.State != guard.State || last.Mode != guard.Mode)
                trail.Add((guard.State, guard.Mode, guard.Cell, p.Flow.StagingPending, p.Flow.EpisodeProcessed, guard.Carried != null)); }
        int carryTicks = 0, carrySounds0 = 0; int heldAtGate = 0; bool heldTested = false;
        for (int t = 0; t < 3000; t++)
        {
            // Once he first waits at the gate, the bus holds E = 2 for a while: no event 9 may reach him.
            if (guard.State == Guard.StateAtGate && !heldTested)
            {
                heldTested = true;
                p.Traffic = 2; p.Bus = 2;
                for (int k = 0; k < 60; k++) { p.Step(); p.Traffic = 2; if (guard.State == Guard.StateAtGate) heldAtGate++; }
                p.Traffic = 0; p.Bus = 0;
            }
            p.Step();
            Record();
            if (guard.Carried != null && guard.State is StaffMember.StateWalk) carryTicks++;
            if (guard.State == StaffMember.StateIdle && trail.Any(x => x.State == Guard.StateToMouth)) break;
        }
        string path = string.Join(" ", trail.Select(x => $"{x.State:x2}/{x.Mode:x2}"));
        var states = trail.Select(x => x.State).ToList();
        int Index(byte s, int from = 0) => states.FindIndex(from, x => x == s);
        // 0x2F lasts no tick of its own: event 9 arrives in the coordinator pass and 0x141900 lays the
        // crossing leg (mode 0x10, state 3) in the same tick's staff update -- so the crossing is found by its mode.
        int Crossing(int from) => trail.FindIndex(Math.Max(0, from), x => x.Mode == Guard.ModeCrossing);
        int carrying = Index(Guard.StateCarrying), chuck = Index(Guard.StateChuckingOut), gate1 = Index(Guard.StateAtGate),
            cross1 = gate1 < 0 ? -1 : Crossing(gate1), exit = Index(Guard.StateToExit), back = Index(Guard.StateBackToGate),
            gate2 = gate1 < 0 ? -1 : Index(Guard.StateAtGate, gate1 + 1), cross2 = gate2 < 0 ? -1 : Crossing(gate2), mouth = Index(Guard.StateToMouth);
        bool ordered = carrying >= 0 && carrying < chuck && chuck < gate1 && gate1 < cross1 && cross1 < exit && exit < back && back < gate2
                       && gate2 < cross2 && cross2 < mouth;
        G(ordered, $"ejection: 0x3C → 0x27 → 0x2E → (event 9) 0x2F/mode 0x10 → 0x30 → 0x3B → 0x2E → 0x2F/mode 0x10 → 0x37 → 0, in order ({path})");
        if (!ordered) return;
        var atGate = trail[gate1]; var atExit = trail[back];
        var stagingZ = bus.StagingPoint.Z;
        G(atGate.Cell.Z == stagingZ && atGate.Cell.X >= bus.StagingPoint.X && atGate.Cell.X <= bus.StagingPoint.X + 1 && atGate.P >= 1,
          $"ejection: he waits at the staging point {bus.StagingPoint} (at {atGate.Cell}) counted in the coordinator's P = {atGate.P} (0x153298)");
        G(heldTested && heldAtGate == 60,
          $"ejection: while the bus holds E = 2 the coordinator sends no event 9 -- he waited all {heldAtGate} of 60 held ticks");
        G(atExit.Cell == new ParkCell(entry.XStart, entry.ZRow) && !atExit.Carrying && trail[exit].Carrying,
          $"ejection: he carries the copy out through the turnstile to the exit point {atExit.Cell} (+0/+1, the corridor start) and drops it there (0x140990)");
        G(trail[cross1].R == 0 && trail[exit].R >= 1 && trail[exit].P == trail[gate1].P - 1,
          $"ejection: the crossing leg's arrival is 0x1532B0 -- P {trail[gate1].P} -> {trail[exit].P}, R {trail[cross1].R} -> {trail[exit].R}");
        G(guard.State == StaffMember.StateIdle && guard.Carried == null && guard.Cell == new ParkCell(entry.XCol, entry.ZEnd) && p.Flow.StagingPending == 0,
          $"ejection: back through the same turnstile to the park mouth ({guard.Cell}), state 0, no copy, P back to {p.Flow.StagingPending}");
        int sounds = p.Sounds.Count(s => ReferenceEquals(s.M, guard) && s.Event == Guard.SoundCarrying);
        G(sounds >= carryTicks && carryTicks > 0,
          $"ejection: 0x1409E8 raises 0x89 at every reposition while he carries ({sounds} for {carryTicks} walking ticks)");
        p.Tick();
        G(guard.SpeedBits == Guard.PatrolSpeed && guard.State != Guard.StateChase,
          $"ejection: speed 30 held through the ejection, back to 15 at the next find work ({guard.SpeedBits})");

        // With no coordinator (the legacy walk-in), a staged guard crosses at once.
        var q = newPark(111);
        q.Staff.Gate = null;
        var g2 = q.Guest(inside[4], 10);
        var guard2 = q.Hire<Guard>(StaffKind.Guard, inside[7]);
        q.Staff.Prank(g2);
        int waited = 0; bool staged = false;
        for (int t = 0; t < 3000 && guard2.State != Guard.StateToExit; t++)
        {
            q.Step();
            if (guard2.State == Guard.StateAtGate) { staged = true; waited++; }
        }
        G(staged && waited <= 1 && q.Staff.GateOpened >= 1,
          $"ejection (adapter, no coordinator): the staged guard is let through at the next update ({waited} tick(s) at the gate)");
    }

    // =============================================================================================
    // The stink table (0x1822D0 / 0x1824A8) and the handyman's sweep.

    static void StinkTable(Func<int, Park> newPark, Action<bool, string> G)
    {
        var p = newPark(120);
        var s = p.Staff.Stinks;
        int added = 0;
        for (int i = 0; i < 12; i++) if (s.Add(p.B.X + i, p.B.Z)) added++;
        bool dup = s.Add(p.B.X, p.B.Z);
        G(added == 10 && s.Count == 10 && !dup && s.Refused == 3,
          $"stink table: 10 entries at most, an 11th and 12th refused, the same cell twice refused ({added} added, {s.Refused} refused)");
        G(s.Entries.All(e => e.X >= e.CellX + 0.1f - 1e-4f && e.X <= e.CellX + 0.9f + 1e-4f && e.Z >= e.CellZ + 0.1f - 1e-4f && e.Z <= e.CellZ + 0.9f + 1e-4f),
          $"stink table: each YellowStink stands at cell + 0.1 + (rand % 9)/10 on both axes ({string.Join(" ", s.Entries.Take(3).Select(e => $"({e.X:F2},{e.Z:F2})"))} ...)");
        var third = s.Entries[2]; var last = s.Entries[^1];
        s.Remove(third.CellX, third.CellZ);
        G(s.Count == 9 && ReferenceEquals(s.Entries[2], last),
          "stink table: 0x1824A8 moves the LAST entry into the removed one's place");
        // The sweep stops a stink in the LITTER's cell. A prank made by a guest part-way along an edge (its fine
        // x past +0x9C into the cell) with the litter scattered +99 lands in the NEXT cell: the sweep there
        // misses the stink, which keeps smoking (findings §1.5). The control prank from the cell centre, +0.
        (Park P, LitterItem Item, ParkCell GuestCell) SweptPrank(int seed, int scatter, int walkTicks)
        {
            var q = newPark(seed);
            q.Hire<Handyman>(StaffKind.Handyman, q.At(3, 5));
            var guest = q.Guest(q.At(12, 2), 10);
            if (walkTicks > 0)
            {
                q.Walk.Send(guest, q.At(20, 2));
                for (int i = 0; i < walkTicks; i++) q.Walk.Step();
            }
            q.Rng.Override = n => n == 200 ? scatter : n == 16 ? 1 : n == 2 ? 0 : n == 300 ? 0 : null;
            var cell = ParkStaff.GuestCell(guest);
            q.Staff.Prank(guest);
            var item = q.Staff.Litter.Active.Single();
            q.Walk.Remove(guest.Id);                                     // the prankster is gone; the handyman sweeps
            q.Rng.Override = n => n == 16 ? 1 : n == 2 ? 0 : n == 300 ? 0 : null;
            for (int t = 0; t < 3000 && item.Active; t++) q.Tick();
            return (q, item, cell);
        }
        var centred = SweptPrank(121, 100, 0);
        var edge = SweptPrank(122, 199, 5);
        G(!centred.Item.Active && !edge.Item.Active && centred.P.Staff.Litter.Swept == 1 && edge.P.Staff.Litter.Swept == 1,
          $"sweep: the handyman finds, claims and sweeps the HIDDEN prank litter (both swept)");
        G(centred.P.Staff.Stinks.Count == 0 && centred.Item.Cell == centred.GuestCell
          && edge.Item.Cell != edge.GuestCell && edge.P.Staff.Stinks.Count == 1,
          $"sweep: prank litter in the prank's own cell {centred.GuestCell} stops its stink ({centred.P.Staff.Stinks.Count} left); "
          + $"litter scattered into the next cell ({edge.GuestCell} -> {edge.Item.Cell}) is swept and the stink keeps smoking ({edge.P.Staff.Stinks.Count} left)");
    }
}
