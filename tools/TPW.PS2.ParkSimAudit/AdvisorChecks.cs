using System.Buffers.Binary;
using System.Text;
using TPW.PS2.Data;

/// <summary>⭐⭐ THE ADVISOR, STEP A (findings/advisor-rules.md, findings/advisor-messages.md;
/// ghidra_tpw/notes/SPEC-advisor-A-core.md): the scheduler's warm-up, cursor, ring gate and state-1-only rule;
/// next/lastFail/v78; the REAL disc rules firing from the port's produced variables; submit routing; the
/// 20-slot ring; the six states' timings; modal skipping; the message stack; the v52/v53 quirk; the counters'
/// flag gate; the park-start reset and the resume order; the direct emitters -- through
/// <see cref="ParkAdvisor"/>, <see cref="AdvisorScheduler"/>, <see cref="AdvisorProducers"/> and
/// <see cref="AdvisorMessageStack"/> as the viewer drives them.
///
/// Every native constant is re-read from the EXECUTABLE. tools/advisor_teeth.py mutates each rule and
/// requires this family to go red.</summary>
static class AdvisorChecks
{
    /// <summary>A producer set the checks control: every external variable from <see cref="Values"/>.</summary>
    sealed class SetProducers : IAdvisorProducers
    {
        public readonly short[] Values = new short[AdvisorScheduler.VariableCount];
        public readonly List<int> Produced = new();
        public int Months { get; set; }
        public int UpgradeResearchPercent { get; set; }
        public short Produce(int index) { Produced.Add(index); return Values[index]; }
    }

    sealed record Asset(string Stem, AssetResourceDatabase.Entry Record, byte[] Script, byte[] Aps,
                        Func<string, byte[]> Sibling, Func<RideDefinition> Definition);

    sealed class Park
    {
        public ParkPaths Paths; public ParkSim Sim; public GuestWalk Walk; public ParkVisitors Visitors;
        public ParkStaff Staff; public ParkClock Clock; public ParkManagement Mgmt; public ParkCell B;
        public int NextId = 300;
        public readonly List<StaffFeature> Extra = new();
        public ParkCell At(int dx, int dz) => B.Offset(dx, dz);
        public void Days(int n) { for (int i = 0; i < n; i++) Clock.Advance(ParkClock.UnitsPerDay, out _, out _, out _); }
    }

    /// <summary>What an advisor submitted and presented, with the tick of each.</summary>
    sealed class Log
    {
        public readonly List<(long Tick, ushort Id, bool Immediate, object Obj)> Submitted = new();
        public readonly List<(long Tick, AdvisorPlayback P)> Played = new();
        public int GameOvers, Skips;
        public void Hook(ParkAdvisor a)
        {
            a.Submitted = (r, imm) => Submitted.Add((a.Ticks, r.Id, imm, r.Object));
            a.Played = p => Played.Add((a.Ticks, p));
            a.GameOver = () => GameOvers++;
            a.UiSound = s => { if (s == ParkAdvisor.SoundSkip) Skips++; };
        }
    }

    public static void Run(Disc disc, Model terrain, WadArchive data, WadArchive world, string worldName,
                           Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "advisor: " + label);
        var exe = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        byte[] elf = disc.Read(exe.Extent, exe.Size);
        var cat = new AdvisorCatalogue(elf);
        byte[] Find(string suffix) => data.Read(data.Entries.First(x => x.Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)));
        var rules = new AdvisorRules(Find("/Generic/Advisor/headers.ass"), Find("/Generic/Advisor/opcodes.ass"));
        Check(rules.Rules.Count == 106 && cat.Messages.Count == 275, $"the disc's rule VM: {rules.Rules.Count} rules, {cat.Messages.Count} messages");

        Constants(elf, Check);
        Routing(cat, rules, Check);
        Ring(cat, rules, Check);
        Timings(cat, rules, Check);
        Modal(cat, rules, Check);
        Stack(cat, rules, Check);
        Scheduler(cat, rules, Check);
        Elapsed(rules, Check);
        Quirk5253(rules, Check);
        Counters(cat, rules, Check);
        Reset(cat, rules, Check);
        Producers(Check);
        Emitters(cat, rules, Check);

        // ---- a real park for the producers and the disc rules ------------------------------------------
        var compiled = new CompiledAssets(new AssetResourceDatabase(data.Read(data.Find("/arsdb.dba"))), TextDatabase.Load(data, "eur"));
        IEnumerable<Asset> Assets(AssetResourceDatabase.AssetKind kind)
        {
            foreach (var e in world.Entries.Where(e => e.Path.EndsWith(".sam", StringComparison.OrdinalIgnoreCase))
                                           .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
            {
                var rec = compiled.For(worldName, e.Path);
                if (rec?.Kind != kind) continue;
                string stem = e.Path[..^4], folder = stem[..(stem.LastIndexOf('/') + 1)];
                var rse = world.Find(stem + ".rse");
                if (rse == null) continue;
                var aps = world.Find(stem + ".aps");
                string sam = Encoding.ASCII.GetString(world.Read(e));
                RideDefinition Definition()
                {
                    var def = RideDefinition.Parse(sam, "/DATA/" + worldName + ".WAD" + e.Path);
                    compiled.Attach(new[] { def }, out _);
                    return def;
                }
                yield return new Asset(stem, rec, world.Read(rse), aps == null ? null : world.Read(aps),
                                       n => world.Find(folder + n) is { } s ? world.Read(s) : null, Definition);
            }
        }
        var paths = new ParkPaths(terrain);
        paths.SetEntrance(ParkEntrance.ReadExecutable(elf));
        int pathMaterial = Enumerable.Range(1, paths.Materials.Count - 1).First(i => ParkPaths.Classify(paths.Materials[i]) == ParkPathKind.Path);
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
        Check(origin != null, $"{worldName}: a clear 30x14 block for the advisor fixture ({origin})");
        if (origin == null) return;
        var B = origin.Value.Offset(1, 1);
        for (int x = 0; x < 28; x++) paths.Lay(B.Offset(x, 2), pathMaterial);

        Park NewPark()
        {
            var sim = new ParkSim(paths);
            var walk = new GuestWalk(paths);
            var rng = new Random(7);
            var p = new Park { Paths = paths, Sim = sim, Walk = walk, B = B, Clock = new ParkClock() };
            p.Visitors = new ParkVisitors(sim, walk, () => 0) { Needs = new VisitorNeeds(seed: 11) };
            p.Staff = new ParkStaff(p.Visitors, p.Clock, new NativeActivationSequence(3000, "advisor checks"), n => n <= 0 ? 0 : rng.Next(n));
            p.Visitors.Staff = p.Staff;
            var baseFeatures = p.Staff.Features;
            p.Staff.Features = () => baseFeatures().Concat(p.Extra);
            p.Mgmt = new ParkManagement(p.Clock, new ParkAwards()) { Finances = sim.Finances, Staff = p.Staff };
            return p;
        }
        ParkRide PlaceFirst(Park p, AssetResourceDatabase.AssetKind kind, ParkCell at)
        {
            foreach (var asset in Assets(kind))
            {
                var a = asset.Record.ConnectionA;
                ParkCell? stub = a.IsPresent ? at.Offset(a.X, a.Z - 1) : null;
                var ride = p.Sim.Add(p.NextId++, asset.Stem, at, asset.Record.Width, asset.Record.Depth, asset.Script,
                                     asset.Aps == null ? null : new Animation(asset.Aps), 1, stub, stub, out _,
                                     sibling: asset.Sibling, definition: asset.Definition(), placementTurns: 0);
                if (ride != null) return ride;
            }
            return null;
        }
        StaffMember HireAt(Park p, StaffKind kind, ParkCell cell)
        {
            var m = p.Staff.Hire(kind, p.Staff.Candidates.Available(kind).First().Slot);
            if (!p.Staff.Drop(m, cell)) throw new InvalidOperationException($"drop refused at {cell}");
            return m;
        }
        DiscRules(cat, rules, NewPark, PlaceFirst, HireAt, Check);
        ParkProducers(NewPark, PlaceFirst, Check);
    }

    static ParkAdvisor Make(AdvisorCatalogue cat, AdvisorRules rules, IAdvisorProducers producers = null, ParkClock clock = null,
                            bool testPark = false, Log log = null)
    {
        var a = new ParkAdvisor(cat, rules, clock ?? new ParkClock(), producers ?? new SetProducers(), testPark, tutorial: false, world: 1,
                                random: _ => 0);
        log?.Hook(a);
        return a;
    }
    static void Tick(ParkAdvisor a, int n = 1) { for (int i = 0; i < n; i++) a.Update(); }
    static bool RunUntil(ParkAdvisor a, Func<bool> done, int max)
    {
        for (int i = 0; i < max; i++) { a.Update(); if (done()) return true; }
        return false;
    }
    /// <summary>A message with no text and no voice in any variant, below the immediate block.</summary>
    static IEnumerable<ushort> SilentIds(AdvisorCatalogue cat) =>
        cat.Messages.Where(m => m.Id < 0xD0 && m.Id != ParkAdvisor.Bankrupted && !m.HasText && m.Voices.All(v => v.SoundId == 0)).Select(m => (ushort)m.Id);
    static ushort SilentId(AdvisorCatalogue cat) => SilentIds(cat).First();
    /// <summary>A modal-block message (208..274) whose variant 0 speaks.</summary>
    static ushort VoicedModalId(AdvisorCatalogue cat) =>
        (ushort)cat.Messages.First(m => m.Id >= 0xD0 && m.Id < 0x113 && m.VariantCount <= 1 && m.Voices[0].SoundId != 0).Id;

    // =============================================================================================
    // The numbers, read back out of the executable.

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
        int Imm(uint va) => (short)U16(At(va));
        Check(Imm(0x106440) == ParkAdvisor.StartDelayTicks && Imm(0x106C6C) == ParkAdvisor.CooldownTicks,
              $"code: the start delay {Imm(0x106440)} (ctor 0x106440) and the cooldown {Imm(0x106C6C)} (state 4, 0x106C6C)");
        Check(Imm(0x107784) == ParkAdvisor.RingSlots && Imm(0x10780C) == ParkAdvisor.RingSlots && Imm(0x10783C) == ParkAdvisor.RingSlots,
              $"code: QueueMessage wraps the ring at {Imm(0x107784)} (0x107784, 0x10780C, 0x10783C)");
        Check(Imm(0x10760C) == -ParkAdvisor.ImmediateFirst && Imm(0x107610) == ParkAdvisor.ImmediateCount,
              $"code: 0x1075F0 routes (u16)(id + {Imm(0x10760C)}) < {Imm(0x107610)} immediately (0x10760C, 0x107610: sltiu)");
        Check(Imm(0x106C60) == (ParkAdvisor.FlagText | ParkAdvisor.FlagQueue),
              $"code: a modal message's exit sets flags | 0x{Imm(0x106C60):x} (0x106C60)");
        Check(Imm(0x1515A0) == ParkAdvisor.ParkStartFlags && Imm(0x151638) == (ParkAdvisor.ParkStartFlags | ParkAdvisor.FlagTutorial)
              && Imm(0x15164C) == ParkAdvisor.FlagRules,
              $"code: park start writes 0x{Imm(0x1515A0):x}, 0x{Imm(0x151638):x} with the tutorial, | {Imm(0x15164C)} off the test park (0x1515A0, 0x151638, 0x15164C)");
        Check(Imm(0x106820) == ParkAdvisor.GreetingAchieved && Imm(0x106840) == ParkAdvisor.GreetingFirst && Imm(0x106850) == ParkAdvisor.GreetingOther,
              $"code: the greetings 0x{Imm(0x106820):x} / 0x{Imm(0x106840):x} / 0x{Imm(0x106850):x} (0x106820, 0x106840, 0x106850)");
        Check(Imm(0x10DCB4) == AdvisorScheduler.WarmUpRefreshes && Imm(0x10DC84) == AdvisorScheduler.VariableCount
              && Imm(0x10DE10) == AdvisorScheduler.CounterLimit && Imm(0x10DDD8) == AdvisorScheduler.CounterCount,
              $"code: the scheduler's warm-up sltiu {Imm(0x10DCB4)}, the refresh % {Imm(0x10DC84)}, the counters' slti {Imm(0x10DDD8)} and clamp {Imm(0x10DE10)}");
        var table = Enumerable.Range(0, 79).Select(i => U32(At(0x359860 + (uint)(i * 4)))).ToArray();
        bool counterPath = table[52] == 0x10E484 && Enumerable.Range(56, 22).All(i => table[i] == 0x10E484);
        Check(counterPath && table[53] == 0x10E40C && table[78] == 0x10E498 && table.Take(52).All(t => t != 0x10E484),
              "code: the producer table 0x359860 sends 52 and 56..77 to the counter path 0x10E484, 53 to 0x10E40C (which falls into it), 78 to the return 0x10E498");
    }

    // =============================================================================================
    // Submitting (0x1075F0, 0x107640).

    static void Routing(AdvisorCatalogue cat, AdvisorRules rules, Action<bool, string> Check)
    {
        var a = Make(cat, rules, testPark: true);
        a.Submit(0x05); a.Submit(0xCF); a.Submit(0x113);
        bool queued = a.RingIds.SequenceEqual(new ushort[] { 0x05, 0xCF, 0x113 }) && !a.PendingSet;
        a.Submit(0xD0);
        bool first = a.PendingSet && a.PendingId == 0xD0;
        a.Submit(0x112);
        Check(queued && first && a.PendingId == 0x112 && a.RingCount == 3,
              "routing: with flag 0x20 ids 5, 207, 275 queue; 208 and 274 are immediate, the second overwriting the first pending");
        var b = Make(cat, rules, testPark: true);
        b.EnterRideAlong();                                                // flags 6: no queue bit
        b.Submit(0x05);
        Check(b.Flags == ParkAdvisor.RideAlongFlags && b.PendingSet && b.PendingId == 0x05 && b.RingCount == 0,
              "routing: with flag 0x20 clear (ride-along's 6) an ordinary id is immediate");
        // An immediate in state 3 stops the speech and the message is LOST; in state 2 the ring's message stays.
        var log = new Log();
        var c = Make(cat, rules, testPark: true, log: log);
        c.SpeechLength = (_, _, _) => 3000;
        Tick(c, 50);
        c.Submit(0x00);                                                    // OPEN_PARK: voiced
        Tick(c, 2);                                                        // take, then play
        bool speaking = c.State == AdvisorState.Speaking && c.Speaking;
        c.Submit(0xD0);
        bool cut = c.State == AdvisorState.Exiting && !c.Speaking && c.RingCount == 0;
        RunUntil(c, () => log.Played.Count >= 2, 400);
        Check(speaking && cut && log.Played.Select(p => p.P.Id).SequenceEqual(new ushort[] { 0x00, 0xD0 }),
              $"routing: an immediate in state 3 cuts the speech (state 4) and the message is not played again ({string.Join(",", log.Played.Select(p => $"0x{p.P.Id:x}"))})");
        var log2 = new Log();
        var d = Make(cat, rules, testPark: true, log: log2);
        d.Head = new AdvisorTimedHead();
        d.SpeechLength = (_, _, _) => 1000;
        Tick(d, 50);
        d.Submit(0x00);
        Tick(d, 3);                                                        // taken, entering (the enter animation runs)
        bool entering = d.State == AdvisorState.Entering && d.RingCount == 1;
        d.Submit(0xD0);
        bool abandoned = d.State == AdvisorState.Exiting && d.RingIds.SequenceEqual(new ushort[] { 0x00 });
        RunUntil(d, () => log2.Played.Count >= 2, 1000);
        Check(entering && abandoned && log2.Played.Select(p => p.P.Id).SequenceEqual(new ushort[] { 0xD0, 0x00 }),
              $"routing: an immediate in state 2 abandons the entering message, which stays in the ring and plays after it ({string.Join(",", log2.Played.Select(p => $"0x{p.P.Id:x}"))})");
    }

    // =============================================================================================
    // The ring (0x107760).

    static void Ring(AdvisorCatalogue cat, AdvisorRules rules, Action<bool, string> Check)
    {
        var a = Make(cat, rules, testPark: true);
        a.Submit(0x05); a.Submit(0x06); a.Submit(0x05);
        Check(a.RingIds.SequenceEqual(new ushort[] { 0x05, 0x06 }), "ring: a second 5 while one is still queued is dropped (dedup against tail..head)");
        var b = Make(cat, rules, testPark: true);
        for (ushort id = 1; id <= 20; id++) b.Submit(id);
        Check(b.RingCount == 19 && b.RingIds[0] == 2 && b.RingIds[^1] == 20 && b.Overflows == 1,
              $"ring: 20 distinct ids hold {b.RingCount} (empty is head == tail): the 20th drops the OLDEST (now {b.RingIds[0]}..{b.RingIds[^1]})");
        var log = new Log();
        var c = Make(cat, rules, testPark: true, log: log);
        Tick(c, 50);
        c.Submit(0x05);
        RunUntil(c, () => log.Played.Count >= 1, 10);
        c.Submit(0x05);
        Check(c.RingCount == 1, "ring: dedup is only against what is still QUEUED -- the message being presented does not stop a second copy");
    }

    // =============================================================================================
    // The six states (0x1066B0).

    static void Timings(AdvisorCatalogue cat, AdvisorRules rules, Action<bool, string> Check)
    {
        var log = new Log();
        var a = Make(cat, rules, testPark: true, log: log);
        Tick(a, 49);
        bool held = a.State == AdvisorState.StartDelay;
        Tick(a, 1);
        Check(held && a.State == AdvisorState.Idle, $"states: the start delay is exactly 50 ticks (state {a.State} after 50, start delay after 49)");
        Check(log.Submitted.Count == 0, "states: the test park posts no greeting");
        // Greetings (not the test park).
        ushort Greeting(Func<bool> achieved, Func<int> earned)
        {
            var l = new Log();
            var g = Make(cat, rules, log: l);
            g.GoalsAlreadyAchieved = achieved; g.GoldTicketsEarned = earned;
            Tick(g, 50);
            return l.Submitted.Count == 1 && l.Submitted[0].Tick == 50 ? l.Submitted[0].Id : (ushort)0xFFFF;
        }
        ushort g171 = Greeting(null, null), g188 = Greeting(null, () => 1), g202 = Greeting(() => true, () => 1);
        Check(g171 == 0xAB && g188 == 0xBC && g202 == 0xCA,
              $"states: at the end of the delay the greeting: 171 with no gold ticket ever earned, 188 after one, 202 with the goals achieved (0x{g171:x}, 0x{g188:x}, 0x{g202:x})");
        // A silent message costs the cycle: take, play, end-and-exit, 100 of cooldown.
        ushort silent = SilentId(cat);
        a.Submit(silent); a.Submit(SilentIds(cat).Skip(1).First());
        var states = new List<AdvisorState>();
        RunUntil(a, () => { states.Add(a.State); return log.Played.Count >= 2; }, 400);
        long gap = log.Played.Count >= 2 ? log.Played[1].Tick - log.Played[0].Tick : -1;
        int cooling = states.Count(s => s == AdvisorState.Cooldown);
        Check(gap == 1 + 1 + 1 + ParkAdvisor.CooldownTicks && cooling == ParkAdvisor.CooldownTicks && log.Played[0].P.SoundId == 0 && !log.Played[0].P.TextAdded,
              $"states: a SILENT message (0x{silent:x}) still costs the cycle -- the next is played {gap} ticks later, {cooling} of them cooling down");
        // A voiced one with the stand-in head: 30 frames up, the speech, 20 frames down, 100 ticks.
        var log2 = new Log();
        var v = Make(cat, rules, testPark: true, log: log2);
        v.Head = new AdvisorTimedHead();
        v.SpeechLength = (_, _, _) => 2000;
        Tick(v, 50);
        v.Submit(0x00);
        var seen = new List<AdvisorState>();
        RunUntil(v, () => { seen.Add(v.State); return seen.Count > 5 && v.State == AdvisorState.Idle; }, 1000);
        int up = seen.Count(s => s == AdvisorState.Entering), talk = seen.Count(s => s == AdvisorState.Speaking),
            down = seen.Count(s => s == AdvisorState.Exiting), cool = seen.Count(s => s == AdvisorState.Cooldown);
        Check(up == 25 && talk == 50 && down == 17 && cool == 100 && log2.Played[0].P.TalkRecord == 1,
              $"states: voiced OPEN_PARK at 40 ms a tick: {up} ticks entering (30 frames), {talk} speaking (2000 ms, talk record {log2.Played[0].P.TalkRecord}), {down} exiting (20 frames), {cool} cooling");
        // flag 0x10 clear, or an immediate waiting, ends the cooldown at once.
        var i = Make(cat, rules, testPark: true);
        Tick(i, 50);
        i.Submit(silent);
        RunUntil(i, () => i.State == AdvisorState.Cooldown, 20);
        Tick(i, 3);
        i.Submit(0xD0);
        Tick(i, 1);
        Check(i.State == AdvisorState.Idle, "states: an immediate waiting cuts the 100-tick cooldown (flag 0x10) on the next tick");
        Check(ParkAdvisor.TalkRecord(1665) == 0 && ParkAdvisor.TalkRecord(1666) == 1 && ParkAdvisor.TalkRecord(9999) == 4
              && ParkAdvisor.TalkRecord(10999) == 6 && ParkAdvisor.TalkRecord(11000) == 3,
              "states: the talk record by speech length (0x107A9C): <0x682 → 0, 9999 → 4, 10999 → 6, 11000 → 3 (the MIPS's 0x2AF7)");
    }

    // =============================================================================================
    // Modal messages and the skip (0x1074C0, 0x107530).

    static void Modal(AdvisorCatalogue cat, AdvisorRules rules, Action<bool, string> Check)
    {
        var log = new Log();
        var a = Make(cat, rules, testPark: true, log: log);
        Tick(a, 50);
        a.Submit(ParkAdvisor.Bankrupted);
        Tick(a, 1);
        bool locked = a.Modal && a.PadLocked && a.CancelLabelShown;
        RunUntil(a, () => a.State == AdvisorState.Idle, 200);
        Check(locked && log.GameOvers == 1 && !a.PadLocked && !a.Modal,
              $"modal: 123 BANKRUPTED locks the pad and shows Cancel; its end calls game over (0x13BDD0) once ({log.GameOvers}); the cooldown unlocks");
        // A voiced modal message skipped: the latch is never cleared, so an immediate that arrives during the exit is cancelled.
        ushort voiced = VoicedModalId(cat);
        var log2 = new Log();
        var b = Make(cat, rules, testPark: true, log: log2);
        b.Head = new AdvisorTimedHead();
        b.SpeechLength = (_, _, _) => 5000;
        Tick(b, 50);
        b.Submit(voiced);
        RunUntil(b, () => b.State == AdvisorState.Speaking, 100);
        b.SkipHeld = true; Tick(b, 1);
        bool latched = b.SkipLatched && log2.Skips == 1 && b.State == AdvisorState.Speaking;
        b.SkipHeld = false; Tick(b, 1);
        bool skipped = b.State == AdvisorState.Exiting && !b.Speaking;
        b.Submit(0xD1);
        bool pending = b.PendingSet;
        Tick(b, 1);
        Check(latched && skipped && pending && !b.PendingSet,
              $"modal: Triangle down then up in state 3 of 0x{voiced:x} takes the head off (state {b.State}); the latch stays set, so an immediate arriving in the exit is CANCELLED");
        var c = Make(cat, rules, testPark: true);
        c.EnterRideAlong();
        Tick(c, 50);
        c.Submit(voiced);
        RunUntil(c, () => c.State == AdvisorState.Idle && c.Ticks > 55, 400);
        Check(c.Flags == (ParkAdvisor.RideAlongFlags | 0x21), $"modal: the end of a modal message sets flags | 0x21 -- from ride-along's 6, text and queue come back: 0x{c.Flags:x2}");
    }

    // =============================================================================================
    // The message stack (0x3928C8).

    static void Stack(AdvisorCatalogue cat, AdvisorRules rules, Action<bool, string> Check)
    {
        var s = new AdvisorMessageStack();
        int sounds = 0; s.UiSound = n => { if (n == AdvisorMessageStack.SoundAdded) sounds++; };
        for (short r = 1; r <= 33; r++) s.Add(AdvisorRecordType.Plain, r);
        Check(s.Count == 32 && s.Records[0].Row == 2 && s.Records[31].Row == 33 && sounds == 33,
              $"stack: 32 records; the 33rd removes the OLDEST (now rows {s.Records[0].Row}..{s.Records[31].Row}); every add plays 0x1E");
        var q = new AdvisorMessageStack();
        foreach (short r in new short[] { 10, 11, 12, 13 }) q.Add(AdvisorRecordType.Plain, r);
        q.MarkForRemoval(0);
        q.RemoveByRow(12);
        Check(q.Records.Select(r => (int)r.Row).SequenceEqual(new[] { 11, 12 }),
              $"stack: 0x108808 takes the row's index BEFORE flushing an older removal, so it removes the record after it ({string.Join(",", q.Records.Select(r => r.Row))})");
        // The advisor: a text message adds a record when PLAYED; silent (row 310) never; opcode 8 retracts.
        var log = new Log();
        var a = Make(cat, rules, testPark: true, log: log);
        Tick(a, 50);
        short open = (short)cat.Messages[0].TextRow;
        a.Submit(0x00); RunUntil(a, () => log.Played.Count == 1, 20);
        a.Submit(SilentId(cat)); RunUntil(a, () => log.Played.Count == 2, 300);
        bool one = a.Stack.Count == 1 && a.Stack.Records[0].Row == open && a.Stack.Records[0].Type == AdvisorRecordType.Plain;
        int before = a.Stack.Count;
        a.TextUi(SilentId(cat));
        bool silentNoop = a.Stack.Count == before;
        a.TextUi(0x00);
        Check(one && silentNoop && a.Stack.Count == 0,
              $"stack: OPEN_PARK adds row {open} when played, a silent message adds nothing, opcode 8 of a row-310 id does nothing and of OPEN_PARK retracts it");
        // Real rule 0 firing twice keeps ONE copy: TEXT_UI 0 retracts the old before MESSAGE 0 adds the new.
        var prod = new SetProducers();
        prod.Values[4] = 4; prod.Values[0] = 0; prod.Values[14] = 1;
        var clock = new ParkClock();
        var log2 = new Log();
        var r0 = Make(cat, rules, prod, clock, log: log2);
        for (int d = 0; d < 5; d++) clock.Advance(ParkClock.UnitsPerDay, out _, out _, out _);
        bool firstShown = RunUntil(r0, () => log2.Played.Any(p => p.P.Id == 0), 3000);
        for (int d = 0; d < 70; d++) clock.Advance(ParkClock.UnitsPerDay, out _, out _, out _);
        bool secondShown = RunUntil(r0, () => log2.Played.Count(p => p.P.Id == 0) == 2, 3000);
        Check(firstShown && secondShown && r0.Stack.Records.Count(r => r.Row == open) == 1,
              $"stack: rule 0 fired twice (60-day delay) leaves ONE OPEN_PARK record -- opcode 8's retract (held {r0.Stack.Records.Count(r => r.Row == open)})");
        // Text off (flags 6): nothing is added.
        var off = Make(cat, rules, testPark: true);
        off.EnterRideAlong(); Tick(off, 50); off.Submit(0x00); Tick(off, 3);
        Check(off.Stack.Count == 0, "stack: with flags bit 0 clear (ride-along) a text message adds no record");
        // Open, the cursor, delete, the object removal quirk.
        var o = new AdvisorMessageStack();
        var ride = new object();
        o.Add(AdvisorRecordType.Plain, 20); o.Add(AdvisorRecordType.Object, 21, ride); o.Add(AdvisorRecordType.Plain, 22);
        bool opened = o.Open() && o.IsOpen && o.Cursor == 0;
        o.Update();
        o.Press(AdvisorStackButtons.Next); o.Update(); o.Update();
        bool onRide = o.Cursor == 1 && o.Selected?.Row == 21 && o.CrossLabel == AdvisorMessageStack.LabelSelect;
        o.Press(AdvisorStackButtons.Delete); o.Update();
        bool sliding = o.Deleting && o.Records[1].State == 1;
        for (int t = 0; t < 20 && o.Count == 3; t++) o.Update();
        Check(opened && onRide && sliding && o.Count == 2 && !o.Deleting && o.Records.Select(r => (int)r.Row).SequenceEqual(new[] { 20, 22 }),
              "stack: L2 opens at the OLDEST; Next moves the cursor; Circle slides the selected record out, then removes it");
        var x = new AdvisorMessageStack();
        x.Add(AdvisorRecordType.Object, 30, ride); x.Add(AdvisorRecordType.Plain, 31); x.Add(AdvisorRecordType.Object, 32, new object());
        x.ObjectRemoved(); x.FlushRemoval();
        Check(x.Records.Select(r => (int)r.Row).SequenceEqual(new[] { 30, 31 }),
              "stack: ANY object removed (0x13D8C0) marks the LAST type-2 record, whatever its object");
        // Save and load (0x1C2D50 / 0x1608E8).
        var sv = new AdvisorMessageStack();
        sv.Add(AdvisorRecordType.Plain, 40); sv.Add(AdvisorRecordType.Object, 41, ride); sv.AddGoalNotice("goal"); sv.Add(AdvisorRecordType.Tutorial, 42);
        var state = sv.CaptureState(obj => ReferenceEquals(obj, ride) ? ((byte)3, (byte)7) : null);
        var back = new AdvisorMessageStack();
        back.RestoreState(state, (k, i) => k == 3 && i == 7 ? ride : null);
        Check(state.Records.Count == 3 && state.Records[1].ObjectKind == 3 && state.Records[1].ObjectIndex == 7
              && back.Records.Select(r => (r.Row, r.Type)).SequenceEqual(new[] { ((short)40, AdvisorRecordType.Plain), ((short)41, AdvisorRecordType.Object), ((short)42, AdvisorRecordType.Tutorial) })
              && ReferenceEquals(back.Records[1].Object, ride),
              "stack: the save keeps every record but the type-4 goal notice, a type-2 record's object as (kind, index); the load re-adds them");
    }

    // =============================================================================================
    // The scheduler (0x10DC50).

    static void Scheduler(AdvisorCatalogue cat, AdvisorRules rules, Action<bool, string> Check)
    {
        var prod = new SetProducers();
        uint day = 1;
        var s = new AdvisorScheduler(rules, prod, () => day);
        var refreshed = new List<int>(); var considered = new List<int>();
        for (int c = 0; c < 17; c++) { s.Step(() => true, null, null); refreshed.Add(s.Last.Refreshed); considered.Add(s.Last.ConsideredRule); }
        Check(refreshed.Take(15).All(n => n == 5) && refreshed[15] == 4 && refreshed[16] == 1
              && considered.Take(15).All(r => r == -1) && considered[15] == 0 && considered[16] == 1,
              $"scheduler: the warm-up refreshes 5 a call for 15 calls, the 16th refreshes the last 4 AND considers rule 0, then 1 a call ({string.Join(",", refreshed)})");
        for (int c = 0; c < 105; c++) { s.Step(() => true, null, null); considered.Add(s.Last.ConsideredRule); }
        var rr = considered.Skip(15).ToList();
        Check(rr.Count == 107 && rr.Take(106).SequenceEqual(Enumerable.Range(0, 106)) && rr[106] == 0 && s.Calls == 122,
              "scheduler: one rule a call, round robin 0..105 and back to 0");
        int cursor = s.RuleCursor, vcur = s.VariableCursor;
        s.Step(() => false, null, null);
        Check(s.Last.Gated && s.Last.Refreshed == 1 && s.RuleCursor == cursor && s.VariableCursor == (vcur + 1) % 79,
              "scheduler: with the advisor's ring not empty it refreshes one variable and does NOT advance the rule cursor");
        // State 1 only, flag 8 only: the advisor calls it only idle.
        var a = Make(cat, rules);
        Tick(a, 50);
        long atDelay = a.Scheduler.Calls;
        Tick(a, 1);                                                        // the greeting is queued: this tick takes it
        long taking = a.Scheduler.Calls;
        var inCycle = new List<long>();
        RunUntil(a, () => { if (a.State != AdvisorState.Idle) inCycle.Add(a.Scheduler.Calls); return a.State == AdvisorState.Idle; }, 400);
        Check(atDelay == 0 && taking == 1 && inCycle.All(n => n == 1),
              $"scheduler: never called in the start delay ({atDelay}), once on the idle tick that takes the greeting, never while it is presented");
    }

    // =============================================================================================
    // next / lastFail / v78.

    static void Elapsed(AdvisorRules rules, Action<bool, string> Check)
    {
        var prod = new SetProducers();
        uint day = 10;
        var s = new AdvisorScheduler(rules, prod, () => day);
        void To(int rule) { int n = 0; do { s.Step(() => true, null, null); } while ((s.Last.ConsideredRule != rule) && ++n < 400); }
        To(5);                                                             // rule 5: always := 0, delay 180
        bool completed = s.Last.Result == AdvisorRules.Result.Completed && s.Next(5) == 10 + rules.Rules[5].DelayDays && s.LastFail(5) == 10;
        day = 190; To(5);
        bool equalBlocks = !s.Last.Due && s.Next(5) == 190;
        day = 191; To(5);
        Check(completed && equalBlocks && s.Last.Due && s.Next(5) == 191 + rules.Rules[5].DelayDays,
              $"next: END sets next = day + delay ({rules.Rules[5].DelayDays}); at day == next the rule is NOT due (unsigned, strict), at next + 1 it is");
        prod.Values[4] = 0; s.Produce(4);
        day = 200; To(0);
        bool failed = s.Last.Result == AdvisorRules.Result.ConditionFailed && s.LastFail(0) == 200 && s.Next(0) == 0;
        // rule 2: v4 < 24, v14 < 4, v12 > 0, v0 != 0, then held > 120 -- held for 0 days is result 2.
        prod.Values[12] = 1; prod.Values[0] = 1; prod.Values[14] = 0;
        foreach (int v in new[] { 0, 4, 12, 14 }) s.Produce(v);          // refreshed now, not when the cursor comes round
        s.SetTimes(2, 0, 250);
        day = 250; To(2);
        Check(failed && s.Last.Result == AdvisorRules.Result.ElapsedBlocked && s.Next(2) == 0 && s.LastFail(2) == 250,
              "next: a failed comparison sets lastFail = day and leaves next; a 'held' block (result 2) changes neither");
        s.SetTimes(2, 0, 0);
        day = 40000; To(2);
        short wrapped = s.Variables[78];
        var blocked = s.Last.Result;
        s.SetTimes(2, 0, 70000);
        day = 70010; To(2);
        Check(wrapped == unchecked((short)40000) && blocked == AdvisorRules.Result.ElapsedBlocked && s.Variables[78] == 10,
              $"v78: (s16)(day − (u16)lastFail): 40000 days reads {wrapped}, so 'held > 120' BLOCKS; lastFail 70000 (past 65535, written as a u32) at day 70010 reads {s.Variables[78]}");
        var o = new AdvisorScheduler(rules, prod, () => 100);
        Check(Enumerable.Range(0, o.RuleCount).All(r => o.Next(r) == 0 && o.LastFail(r) == 100),
              "next: the loader (0x10DA78) sets every rule's next 0 and lastFail to the day it is built on");
    }

    // =============================================================================================
    // v52 / v53 (0x10E40C falls into 0x10E484).

    static void Quirk5253(AdvisorRules rules, Action<bool, string> Check)
    {
        var prod = new SetProducers();
        var s = new AdvisorScheduler(rules, prod, () => 1);
        s.AddCounter(19, 7); s.AddCounter(20, 3);
        s.Produce(75); s.Produce(76); s.Produce(52); s.Produce(53);
        bool copies = s.Variables[52] == 7 && s.Variables[53] == 3 && s.Variables[75] == 7 && s.Variables[76] == 3;
        s.AddCounter(19, 5);
        s.Produce(52);
        bool variable = s.Variables[52] == 7;
        prod.UpgradeResearchPercent = 40; prod.Months = 9;
        s.Produce(53);
        Check(copies && variable && s.UpgradeLatched && s.UpgradeMonthLatch == 9 && s.Variables[53] == 3 && !prod.Produced.Contains(52) && !prod.Produced.Contains(53),
              "v52/v53: v52 copies VARIABLE 75 (the ticket counter's last snapshot, not the counter) and v53 variable 76 (the stink bomb's); 53's latch still sets and is overwritten");
    }

    // =============================================================================================
    // The counters (0x1073C0 → 0x10DDD8).

    static void Counters(AdvisorCatalogue cat, AdvisorRules rules, Action<bool, string> Check)
    {
        var a = Make(cat, rules);
        a.CountEvent(20, 1);
        a.EnterRideAlong();
        a.CountEvent(20, 1);
        bool dropped = a.Scheduler.Counters[20] == 1;
        a.ExitRideAlong();
        a.CountEvent(20, 1);
        Check(dropped && a.Scheduler.Counters[20] == 2 && a.Flags == 0x3F && a.SavedFlags == 0xFF,
              "counters: events count with flags bit 3; ride-along (flags 6) DROPS them; its end restores the flags (0x1072B0)");
        var t = Make(cat, rules, testPark: true);
        t.CountEvent(0, 1);
        Check(t.Scheduler == null && t.Flags == 0x37, "counters: the test park has no rule object (flags 0x37) and counts nothing");
        var s = new AdvisorScheduler(rules, new SetProducers(), () => 1);
        s.AddCounter(0, 29999); s.AddCounter(0, 2);
        s.AddCounter(1, -29999); s.AddCounter(1, -2);
        s.AddCounter(22, 1); s.AddCounter(-1, 1);
        Check(s.Counters[0] == 30000 && s.Counters[1] == -30000 && s.Counters.Skip(2).All(c => c == 0),
              "counters: += d as s16 then clamped to ±30000; indices outside 0..21 are ignored");
    }

    // =============================================================================================
    // Park start and resume (0x151688 builds, 0x15172C restores the calendar after).

    static void Reset(AdvisorCatalogue cat, AdvisorRules rules, Action<bool, string> Check)
    {
        var clock = new ParkClock();
        for (int d = 0; d < 77; d++) clock.Advance(ParkClock.UnitsPerDay, out _, out _, out _);
        var a = Make(cat, rules, clock: clock);
        var s = a.Scheduler;
        Check(Enumerable.Range(0, s.RuleCount).All(r => s.Next(r) == 0 && s.LastFail(r) == 77) && s.Variables.All(v => v == 0)
              && s.Counters.All(c => c == 0) && s.RuleCursor == 0 && s.VariableCursor == 0 && !s.Warm
              && a.State == AdvisorState.StartDelay && a.Countdown == 50 && a.RingCount == 0 && !a.PendingSet && a.Stack.Count == 0 && a.Flags == 0x3F,
              "reset: a park start builds everything fresh -- next 0, lastFail = today (77), variables/counters/cursors 0, state 0 with 50, empty ring and stack, flags 0x3F");
        // Resumed: built at day 0, the calendar restored AFTERWARDS -> every rule due with v78 = D.
        var c2 = new ParkClock();
        var prod = new SetProducers();
        var r = Make(cat, rules, prod, c2);
        for (int d = 0; d < 300; d++) c2.Advance(ParkClock.UnitsPerDay, out _, out _, out _);
        bool evaluated = RunUntil(r, () => r.Scheduler.Last.ConsideredRule == 0, 2000);
        Check(evaluated && r.Scheduler.Last.Due && r.Scheduler.Variables[78] == 300,
              $"reset: a resumed park (built at day 0, calendar restored to 300 after) finds rule 0 due with v78 = {r.Scheduler.Variables[78]}");
    }

    // =============================================================================================
    // Producers the port computes without a park.

    static void Producers(Action<bool, string> Check)
    {
        VisitorWants W(byte hunger = 0, byte thirst = 0, byte toilet = 0, byte sick = 0, byte happy = 50, byte bored = 0)
            => new() { Hunger = hunger, Thirst = thirst, Toilet = toilet, Sick = sick, Happiness = happy, Boredom = bored };
        int[] got =
        {
            AdvisorProducers.NeedClass(W(hunger: 81, thirst: 81)), AdvisorProducers.NeedClass(W(hunger: 81, thirst: 80)),
            AdvisorProducers.NeedClass(W(hunger: 80, thirst: 76)), AdvisorProducers.NeedClass(W(toilet: 76, thirst: 75)),
            AdvisorProducers.NeedClass(W(sick: 76)), AdvisorProducers.NeedClass(W(happy: 76)),
            AdvisorProducers.NeedClass(W(bored: 76)), AdvisorProducers.NeedClass(W(happy: 24)), AdvisorProducers.NeedClass(W(happy: 25)),
        };
        Check(got.SequenceEqual(new[] { 7, 2, 1, 3, 4, 5, 6, 8, 0 }),
              $"producers: the need classifier 0x211C80 (7 both, 2 hunger ≥ 81, 1 thirst ≥ 76, 3 toilet, 4 sick, 5 happy, 6 bored, 8 unhappy &lt; 25): {string.Join(",", got)}");
        var clock = new ParkClock();
        var census = new List<AdvisorPlacement>();
        var p = new AdvisorProducers(clock) { Placements = () => census };
        census.AddRange(new[]
        {
            new AdvisorPlacement(AssetResourceDatabase.AssetKind.Ride, 1, 0, null),
            new AdvisorPlacement(AssetResourceDatabase.AssetKind.Coaster, 0, 0, null),
            new AdvisorPlacement(AssetResourceDatabase.AssetKind.Shop, 1, 0, null),
            new AdvisorPlacement(AssetResourceDatabase.AssetKind.Sideshow, 1, 0, null),
            new AdvisorPlacement(AssetResourceDatabase.AssetKind.Feature, 1, 1, null),     // toilet
            new AdvisorPlacement(AssetResourceDatabase.AssetKind.Feature, 0, 8, null),     // camera, status 0
            new AdvisorPlacement(AssetResourceDatabase.AssetKind.Feature, 1, 0, null),     // other
        });
        Check(p.Produce(14) == 2 && p.Produce(15) == 1 && p.Produce(16) == 1 && p.Produce(17) == 2 && p.Produce(50) == 6 * 2 + 4 + 4 + 5 * 3,
              $"producers: rides {p.Produce(14)} (any status), shops {p.Produce(15)}, sideshows {p.Produce(16)}, standing features {p.Produce(17)}, park size {p.Produce(50)} = 6·2 + 4 + 4 + 5·3 (every feature)");
        Check(p.Produce(0) == 1 && p.Produce(21) == AdvisorProducers.QuietVariety && p.Produce(22) == 100 && p.Produce(23) == 100 && p.Produce(24) == 100,
              "producers: the hooks' defaults keep their rules quiet: park open 1, variety 50, research 100, upgrades in use 100, upgrade research 100");
        for (int d = 0; d < 400; d++) clock.Advance(ParkClock.UnitsPerDay, out _, out _, out _);
        Check(p.Produce(3) == 400 && p.Produce(4) == clock.Month + 12 * clock.Year && p.Produce(5) == clock.Year && p.Months == p.Produce(4),
              $"producers: v3 days {p.Produce(3)}, v4 months since creation {p.Produce(4)}, v5 years {p.Produce(5)}");
        var sim = new ParkSim(null);
        var money = new AdvisorProducers(clock, sim);
        sim.Finances.Balance = 300_000; int opening = money.Produce(48);
        sim.Finances.Balance = -123_456; int negative = money.Produce(48);
        sim.Finances.Balance = 400_000_000; int capped = money.Produce(48);
        Check(opening == 300 && negative == -123 && capped == 30000, $"producers: v48 = balance/10/100 capped ±30000: $30,000 is {opening}, −$12,345.6 is {negative}, $40M is {capped}");
    }

    // =============================================================================================
    // Direct emitters.

    static void Emitters(AdvisorCatalogue cat, AdvisorRules rules, Action<bool, string> Check)
    {
        var log = new Log();
        var a = Make(cat, rules, testPark: true, log: log);
        a.CoasterFinished(closed: false, valid: true);
        bool open = log.Submitted.Select(s => (int)s.Id).SequenceEqual(new[] { 0xCC, 0xCC }) && a.RingIds.SequenceEqual(new ushort[] { 0xCC });
        log.Submitted.Clear();
        a.CoasterFinished(closed: true, valid: false);
        bool invalid = log.Submitted.Select(s => (int)s.Id).SequenceEqual(new[] { 0xCB });
        log.Submitted.Clear();
        a.CoasterFinished(closed: true, valid: true);
        Check(open && invalid && log.Submitted.Count == 0,
              "emitters: the coaster Triangle: an open ring posts 204 twice (0x11BA00, then 0x11BBD8) and the ring keeps one; invalid 203; closed and valid nothing");
        log.Submitted.Clear();
        a.CoasterPlaced(1); bool none = log.Submitted.Count == 0;
        a.CoasterPlaced(2); a.CoasterPlaced(14, parkIndex: 2);
        Check(none && log.Submitted.Select(s => (int)s.Id).SequenceEqual(new[] { 0xC9, 0xC9 }),
              "emitters: 201 COASTER_STOCK_OUT when (park 2 ? 14 : 2) − coasters in use is 0");
        // The in-the-red chain (0x16B060).
        var clock = new ParkClock();
        var sim = new ParkSim(null);
        var msgs = new List<int>();
        var m = new ParkManagement(clock, new ParkAwards()) { Finances = sim.Finances, Advisor = msgs.Add };
        var perMonth = new List<string>();
        sim.Finances.Balance = -1;
        for (int month = 0; month < 5; month++)
        {
            msgs.Clear();
            int mc = m.MonthChanges; while (m.MonthChanges == mc) m.Advance(ParkClock.UnitsPerDay);
            perMonth.Add(string.Join("+", msgs.Select(x => x.ToString("x"))));
        }
        int red = m.RedMonths;
        sim.Finances.Balance = 1000;
        { int mc = m.MonthChanges; while (m.MonthChanges == mc) m.Advance(ParkClock.UnitsPerDay); }
        Check(string.Join(" ", perMonth) == "ce+79  7a 7b " && red == 5 && m.RedMonths == 0,
              $"emitters: in the red at a month end: 1st 0xCE then 0x79, 2nd nothing, 3rd 0x7A, 4th 0x7B BANKRUPTED, 5th nothing; back above zero resets ({string.Join(" | ", perMonth)})");
        var bank = cat.Messages[ParkManagement.MessageBankrupted];
        var upgrade = cat.Messages[StaffTables.UpgradeNoMechanicsMessage];
        Check(!bank.HasText && bank.Voices.All(v => v.SoundId == 0) && !upgrade.HasText && upgrade.Voices[0].SoundId != 0,
              $"emitters: 123 BANKRUPTED is silent; 207 UPGRADE_NO_MECHANICS has no text but a VOICE (sound {upgrade.Voices[0].SoundId}) -- Mechanic.cs's note corrected");
        var rm = new ResearchManager();
        Check(rm.CompletionMessage(9, 0) == AdvisorRequest.UnsetId && AdvisorRequest.UnsetId == 276,
              "emitters: a research completion of an unhandled category posts the request's unset id 276 (0x107C90), not nothing");
        var l3 = new Log();
        var b = Make(cat, rules, testPark: true, log: l3);
        Tick(b, 50);
        b.Submit(AdvisorRequest.UnsetId);
        RunUntil(b, () => l3.Played.Count == 1, 10);
        Check(l3.Played.Count == 1 && !l3.Played[0].P.TextAdded && l3.Played[0].P.SoundId == 0,
              "emitters: id 276 (past the 275-record table) is presented as a silent message without throwing (⚠ natively it reads past the table)");
    }

    // =============================================================================================
    // The disc's rules from the port's produced variables, and the routing of the port's emitters.

    static void DiscRules(AdvisorCatalogue cat, AdvisorRules rules, Func<Park> newPark,
                          Func<Park, AssetResourceDatabase.AssetKind, ParkCell, ParkRide> place,
                          Func<Park, StaffKind, ParkCell, StaffMember> hireAt, Action<bool, string> Check)
    {
        // Rule 0: 4+ months, the park closed, a ride -> OPEN_PARK, text and voice.
        AdvisorRules.Result? Rule0(bool parkOpen, out bool presented, out ParkRide ride)
        {
            var p = newPark();
            ride = place(p, AssetResourceDatabase.AssetKind.Ride, p.At(4, 4));
            p.Days(130);
            var prod = new AdvisorProducers(p.Clock, p.Sim, p.Staff, p.Visitors) { ParkOpen = () => parkOpen };
            var log = new Log();
            var a = Make(cat, rules, prod, p.Clock, log: log);
            a.Attach(p.Sim, p.Staff, p.Mgmt);
            AdvisorRules.Result? result = null;
            RunUntil(a, () => { if (a.Scheduler.Last.ConsideredRule == 0 && a.Scheduler.Last.Due) result = a.Scheduler.Last.Result; return result != null; }, 3000);
            RunUntil(a, () => log.Played.Any(x => x.P.Id == 0), 600);
            presented = log.Played.Any(x => x.P.Id == 0 && x.P.TextAdded && x.P.TextRow == cat.Messages[0].TextRow)
                        && a.Stack.Records.Any(r => r.Row == cat.Messages[0].TextRow);
            return result;
        }
        var closed = Rule0(false, out bool shown, out var r0ride);
        var open = Rule0(true, out bool shownOpen, out _);
        Check(r0ride != null && closed == AdvisorRules.Result.Completed && shown && open == AdvisorRules.Result.ConditionFailed && !shownOpen,
              $"rules: real rule 0 over the producers -- a placed ride ({r0ride?.Name}: v14), 130 days (v4 > 3), park closed (v0 = 0) posts OPEN_PARK to the stack; open, it fails ({closed}/{open})");
        // Rule 48: a tired staff member and no staff room, held > 50 days -> NEED_STAFF_ROOM.
        (AdvisorRules.Result? Result, uint Next, uint LastFail, bool Submitted) Rule48(bool room, int buildDay)
        {
            var p = newPark();
            p.Days(buildDay);
            var m = hireAt(p, StaffKind.Handyman, p.At(3, 2));
            m.Tiredness = 40;
            if (room) p.Extra.Add(new StaffFeature(p.At(10, 6), p.At(10, 6), 2, 1, null, "room"));
            var prod = new AdvisorProducers(p.Clock, p.Sim, p.Staff, p.Visitors);
            var log = new Log();
            var a = Make(cat, rules, prod, p.Clock, log: log);
            p.Days(120 - buildDay);
            AdvisorRules.Result? result = null;
            RunUntil(a, () => { if (a.Scheduler.Last.ConsideredRule == 48 && a.Scheduler.Last.Due) result = a.Scheduler.Last.Result; return result != null; }, 4000);
            return (result, a.Scheduler.Next(48), a.Scheduler.LastFail(48), log.Submitted.Any(s => s.Id == 0x80));
        }
        var fires = Rule48(false, 0);
        var roomed = Rule48(true, 0);
        var early = Rule48(false, 120);
        Check(fires.Result == AdvisorRules.Result.Completed && fires.Submitted && fires.Next == 120 + rules.Rules[48].DelayDays,
              $"rules: real rule 48 -- a handyman at tiredness 40 (v55 > 15), no staff room (v19 = 0), 120 days since the build (v78 > 50) posts NEED_STAFF_ROOM 0x80");
        Check(roomed.Result == AdvisorRules.Result.ConditionFailed && !roomed.Submitted && roomed.LastFail == 120
              && early.Result == AdvisorRules.Result.ElapsedBlocked && !early.Submitted && early.Next == 0 && early.LastFail == 120,
              $"rules: a staff room fails rule 48 (lastFail = day); built on day 120 it is held-BLOCKED (v78 = 0 ≤ 50) and neither word changes ({roomed.Result}, {early.Result})");
        // The port's emitters, routed: a ride's breakdown with the ride attached, and counter 0.
        var q = newPark();
        var ride = place(q, AssetResourceDatabase.AssetKind.Ride, q.At(4, 4));
        var qlog = new Log();
        var qa = Make(cat, rules, new AdvisorProducers(q.Clock, q.Sim, q.Staff, q.Visitors), q.Clock, log: qlog);
        qa.Attach(q.Sim, q.Staff, q.Mgmt);
        q.Sim.SetRideStatus(ride, 4);
        q.Sim.SetRideStatus(ride, 5);
        var up = q.Staff.RequestUpgrade(ride);
        Check(qlog.Submitted.Any(s => s.Id == ParkSim.AdvisorBreakdownImminent && ReferenceEquals(s.Obj, ride))
              && qlog.Submitted.Any(s => s.Id == ParkSim.AdvisorNoMechanics && ReferenceEquals(s.Obj, ride))
              && qa.Scheduler.Counters[0] == 1 && qa.Scheduler.Counters[1] == 1
              && up == ParkStaff.UpgradeRequest.NoMechanics && qlog.Submitted.Any(s => s.Id == StaffTables.UpgradeNoMechanicsMessage && s.Obj == null),
              "routing: through Attach, a ride entering 4 and 5 submits 0x36 and 0x37 WITH the ride and counts events 0 and 1; the refused upgrade submits 0xCF");
    }

    // =============================================================================================
    // Producers over a real park.

    static void ParkProducers(Func<Park> newPark, Func<Park, AssetResourceDatabase.AssetKind, ParkCell, ParkRide> place, Action<bool, string> Check)
    {
        var p = newPark();
        var prod = new AdvisorProducers(p.Clock, p.Sim, p.Staff, p.Visitors);
        var ids = new List<int>();
        for (int i = 0; i < 4; i++) ids.Add(p.Visitors.Arrive(p.At(2 + i, 2), p.At(20, 2)).Id);
        void Set(int id, byte thirst, byte happy) { var w = p.Visitors.Needs.Of(id); w.Thirst = thirst; w.Hunger = 0; w.Toilet = 0; w.Sick = 0; w.Boredom = 0; w.Happiness = happy; p.Visitors.Needs.Set(id, w); }
        Set(ids[0], 90, 60); Set(ids[1], 10, 60); Set(ids[2], 10, 30); Set(ids[3], 10, 10);
        Check(prod.Produce(12) == 4 && prod.Produce(1) == 25 && prod.Produce(47) == (60 + 60 + 30 + 10) / 4 && prod.Produce(2) == 0,
              $"producers: over four real guests: v12 {prod.Produce(12)}, v1 thirsty {prod.Produce(1)}%, v2 {prod.Produce(2)}%, v47 average happiness {prod.Produce(47)}");
        var shop = place(p, AssetResourceDatabase.AssetKind.Shop, p.At(10, 4));
        int shops = prod.Produce(15);
        int size = prod.Produce(50);
        Check(shop != null && shops == 1 && size == 4, $"producers: a placed shop ({shop?.Name ?? "none"}) counts in v15 ({shops}) and 4 in the park size ({size})");
    }
}
