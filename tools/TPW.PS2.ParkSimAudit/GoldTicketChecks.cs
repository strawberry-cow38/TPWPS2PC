using System.Buffers.Binary;
using TPW.PS2.Data;

/// <summary>⭐ GOLD TICKETS (findings/awards.md, "The weekly pass, whole" and "What tickets buy"): the goal records and the
/// world map's links re-read from the ELF against the port's tables, the weekly pass's words, and the pass itself driven
/// through every goal and hidden award at its boundary -- each with the input one step short as the control.</summary>
static class GoldTicketChecks
{
    public static void Run(Disc disc, Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "gold tickets: " + label);
        var exe = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        byte[] elf = disc.Read(exe.Extent, exe.Size);
        Records(elf, Check);
        Links(elf, LobbySlots.ReadExecutable(elf), Check);
        Words(elf, Check);
        Goals(Check);
        HiddenAwards(Check);
        Green(Check);
        Economy(Check);
    }

    static uint U32(byte[] elf, int off) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(off, 4));
    static int Offset(byte[] elf, uint va)
    {
        int ph = (int)U32(elf, 28), n = BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(44, 2)),
            es = BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(42, 2));
        for (int i = 0; i < n; i++)
        {
            int p = ph + i * es;
            if (U32(elf, p) == 1 && va >= U32(elf, p + 8) && va < U32(elf, p + 8) + U32(elf, p + 16))
                return (int)(U32(elf, p + 4) + va - U32(elf, p + 8));
        }
        throw new InvalidDataException($"no ELF address 0x{va:X}");
    }
    static uint Word(byte[] elf, uint va) => U32(elf, Offset(elf, va));

    static void Records(byte[] elf, Action<bool, string> Check)
    {
        int differ = 0, total = 5;
        var rows = new List<string>();
        for (int w = 0; w < 4; w++)
            for (int p = 0; p < 2; p++)
            {
                int o = Offset(elf, ParkGoals.RecordAddresses[w, p]);
                var rec = ParkGoals.For(w, p).Value;
                int I(int at) => (int)U32(elf, o + at);
                bool same = I(0xC) == rec.Visitors && I(0x10) == rec.Profit && I(0x14) == rec.Years
                    && I(0x18) == rec.FeatureCost && I(0x1C) == rec.PathCells
                    && (elf[o + 0x30] != 0) == rec.StarterGoal && elf[o + 0x31] == rec.Tickets;
                if (!same) differ++;
                total += elf[o + 0x31];
                rows.Add($"{w}/{p}:{elf[o + 0x31]}");
            }
        Check(differ == 0, $"all eight goal records' visitors/profit/years/feature-cost/path/starter/tickets match the ELF ({differ} differ)");
        Check(total == 53 && ParkAwards.TicketsInGame == 53,
              $"53 tickets in the game: the 5 hidden awards plus each park's +0x31 ({string.Join(" ", rows)})");
        Check(ParkGoals.For(0, 0)!.Value.StarterGoal && Enumerable.Range(0, 8).Count(i => ParkGoals.For(i / 2, i % 2)!.Value.StarterGoal) == 1,
              "only JUNGLE 1 arms the fourth goal (+0x30)");
    }

    static void Links(byte[] elf, LobbySlots slots, Action<bool, string> Check)
    {
        var want = new[] { (0, 2, 1), (0, 4, 1), (4, 2, 1), (2, 1, 4), (4, 1, 4), (1, 6, 6), (1, 3, 6), (6, 3, 6), (6, 5, 8), (3, 7, 8), (5, 7, 8) };
        Check(slots.Links.Select(l => (l.A, l.B, l.Cost)).SequenceEqual(want),
              $"the eleven world-map links (records 8..18, +0x18..0x1A): {string.Join(" ", slots.Links.Select(l => $"{l.A}-{l.B}:{l.Cost}"))}");
        Check(slots.OpenCost(0, 2) == 1 && slots.OpenCost(0, 4) == 1 && slots.OpenCost(2, 1) == 4 && slots.OpenCost(4, 1) == 4
              && slots.OpenCost(1, 6) == 6 && slots.OpenCost(3, 6) == 6 && slots.OpenCost(5, 7) == 8,
              "an island costs its link's tickets from the island the map stands on, in either direction");
        Check(slots.OpenCost(1, 2) == 1 && slots.OpenCost(1, 4) == 1 && slots.OpenCost(2, 1) == 4,
              "0x2186B0's own case: from JUNGLE 2 back to HALLOW 1 or FANTASY 1 costs 1, though their link costs 4 the other way");
        Check(slots.OpenCost(0, 7) == 100 && slots.OpenCost(0, 1) == 100, "no link at all prices an island at 100 (the search's default)");
        Check(Word(elf, 0x1C4330) == 0x2402FFFF && Word(elf, 0x1C4334) == 0x24030002 && Word(elf, 0x1C4340) == 0xA0830004,
              "0x1C4330 initialises a save slot: no saved park (-1) and state 2, locked (li v1,2; sb v1,4(a0))");
        var awards = new ParkAwards();
        Check(awards.SlotState(0, 0) == ParkAwards.SlotOpen && awards.SlotState(0, 2) == ParkAwards.SlotOpen
              && Enumerable.Range(0, 4).All(w => Enumerable.Range(0, 2).All(p => (w == 0 && p == 0) || awards.SlotState(w, p) == ParkAwards.SlotLocked))
              && awards.OpenParks == 2,
              "a new game: JUNGLE 1 and the test park open (0x1C3528(0,0), (0,2)), the other seven islands locked");
    }

    static void Words(byte[] elf, Action<bool, string> Check)
    {
        (uint At, uint Word)[] messages = { (0x16BCEC, 0x2405009D), (0x16BD38, 0x2405009E), (0x16BDC0, 0x2405009F), (0x16BE4C, 0x240500AA),
            (0x16BE8C, 0x240500A0), (0x16BEDC, 0x240500A1), (0x16BF34, 0x240500A2), (0x16BF74, 0x240500A3), (0x16BFD0, 0x240500A4) };
        Check(messages.All(m => Word(elf, m.At) == m.Word),
              "0x16BC70 pays nine messages in this order: goals 0x9D 0x9E 0x9F 0xAA, awards 0xA0..0xA4 (li a1 at each 0x16BB38 delay slot)");
        Check(Word(elf, 0x16BD10) == 0x2404000A && Word(elf, 0x16BD84) == 0x2403000C,
              "goal 2 multiplies the record's profit by 10; goal 3 divides months by 12");
        Check(Word(elf, 0x16BE3C) == 0x2E100002 && Word(elf, 0x16BE7C) == 0x2C420051 && Word(elf, 0x16BEBC) == 0x2C420008
              && Word(elf, 0x16BF0C) == 0x2C420008 && Word(elf, 0x16BFA4) == 0x2C42000A,
              "the bars: two rides for goal 4, coverage over 0x50, rides over 7 (Upgrade, Aesthetic) and over 9 (Economy)");
        Check(Word(elf, 0x16BF5C) == 0x24040005 && Word(elf, 0x16BF60) == 0x0C054BEC && Word(elf, 0x16BF64) == 0x24050002,
              "Green calls 0x152FB0(5, 2): five shops, a bin within two cells");
    }

    sealed class Fixture
    {
        public readonly ParkClock Clock = new();
        public readonly ParkAwards Awards = new();
        public readonly ParkStatistics Stats = new();
        public readonly ParkFinances Finances = new();
        public readonly List<int> Messages = new();
        public bool Open = true;
        public int OpenedMonth, Rides, Path = 50, FeatureCost;
        public bool Upgraded, GreenAnswer;
        public (int, int, int, int) Starter;
        public readonly ParkManagement M;
        public Fixture(int world, int park)
        {
            M = new ParkManagement(Clock, Awards)
            {
                Stats = Stats, Finances = Finances, Advisor = Messages.Add,
                CurrentPark = () => (world, park), ParkOpen = () => Open, OpenedMonth = () => OpenedMonth,
                AwardInputs = new ParkAwardInputs
                {
                    Rides = () => Rides, AllRidesUpgraded = () => Upgraded, FeatureCost = () => FeatureCost,
                    Green = () => GreenAnswer, PathCells = () => Path, StarterCounts = () => Starter,
                },
            };
        }
        public void Admit(int n) { for (int i = 0; i < n; i++) Stats.Admitted(i, 0); }
    }

    static void Goals(Action<bool, string> Check)
    {
        var f = new Fixture(0, 0);
        f.M.WeeklyPass();
        Check(f.Messages.Count == 0 && f.Awards.GoldTickets == 0, "a fresh JUNGLE 1 wins nothing on its first week");

        f.Admit(99); f.M.WeeklyPass(); bool short1 = f.Messages.Count == 0;
        f.Admit(1); f.M.WeeklyPass();
        Check(short1 && f.Messages.SequenceEqual(new[] { 0x9D }) && f.Awards.GoldTickets == 1 && (f.Awards.ParkGoalBits(0, 0) & 2) != 0,
              "goal 1: 99 guests admitted win nothing, the 100th (record +0xC, <=) wins 0x9D, a ticket and bit 1");
        f.M.WeeklyPass();
        Check(f.Messages.Count == 1 && f.Awards.GoldTickets == 1, "goal 1 is won once: the next week pays nothing");

        f.Finances.Credit(20000); f.M.WeeklyPass(); bool short2 = f.Messages.Count == 1;
        f.Finances.Credit(1); f.M.WeeklyPass();
        Check(short2 && f.Messages.Last() == 0x9E && f.Awards.GoldTickets == 2,
              "goal 2: money goal 20000 tenths (profit 2000 x 10) wins nothing, 20001 wins 0x9E -- strictly above");

        f.Open = false; f.Clock.Set(0, 0, 5); f.M.WeeklyPass(); bool shut = f.Messages.Count == 2;
        f.Open = true; f.OpenedMonth = 0; f.Clock.Set(0, 11, 0); f.M.WeeklyPass(); bool short3 = f.Messages.Count == 2;
        f.Clock.Set(0, 0, 1); f.M.WeeklyPass();
        Check(shut && short3 && f.Messages.Last() == 0x9F && f.Awards.GoldTickets == 3,
              "goal 3: a closed park never counts, eleven months open is 0 years, the twelfth is 1 -- 0x9F");

        f.Starter = (1, 1, 1, 1); f.M.WeeklyPass(); bool short4 = f.Messages.Count == 3;
        f.Starter = (1, 1, 1, 2); f.M.WeeklyPass();
        Check(short4 && f.Messages.Last() == 0xAA && (f.Awards.ParkGoalBits(0, 0) & 16) != 0 && f.Awards.GoldTickets == 4,
              "goal 4 (JUNGLE 1): a sideshow, a shop, a feature and ONE ride win nothing; the second ride wins 0xAA");

        var j2 = new Fixture(0, 1) { Starter = (1, 1, 1, 5) };
        j2.M.WeeklyPass();
        Check(j2.Messages.Count == 0, "the control: JUNGLE 2 has no fourth goal (+0x30 clear), whatever is built");

        var test = new Fixture(0, 2); test.Admit(500); test.M.WeeklyPass();
        Check(test.Messages.Count == 0 && test.Awards.GoldTickets == 0, "the test park (park 2) has no goals record and wins nothing");

        Check(f.Awards.TicketsEarntCount == 4 && f.Awards.ParkTicketsLeft(0, 0) == 3 && f.Awards.FloatingTicketsLeft == 5,
              $"the map's count after four goals: earnt {f.Awards.TicketsEarntCount}, JUNGLE 1 has {f.Awards.ParkTicketsLeft(0, 0)} of 7 left, 5 floating");
    }

    static void HiddenAwards(Action<bool, string> Check)
    {
        var f = new Fixture(1, 0) { Rides = 7, Upgraded = true, FeatureCost = 5000 };
        f.M.WeeklyPass(); bool seven = f.Messages.Count == 0;
        f.Rides = 8; f.M.WeeklyPass();
        Check(seven && f.Messages.SequenceEqual(new[] { 0xA1, 0xA2 }) && f.Awards.HasHiddenAward(1) && f.Awards.HasHiddenAward(2)
              && f.Awards.Medals[0] && f.Awards.Medals[4],
              "Upgrade and Aesthetic need MORE than 7 rides: seven win nothing, the eighth wins 0xA1 then 0xA2 and their medals");

        var g = new Fixture(1, 0) { Rides = 8, Upgraded = false, FeatureCost = 1999 };
        g.M.WeeklyPass(); bool shortBoth = g.Messages.Count == 0;
        g.FeatureCost = 2000; g.M.WeeklyPass();
        Check(shortBoth && g.Messages.SequenceEqual(new[] { 0xA2 }),
              "Aesthetic at 2000 exactly (record +0x18, >=), not 1999; one ride not upgraded keeps Upgrade shut");

        var h = new Fixture(1, 0) { GreenAnswer = true };
        h.M.WeeklyPass(); h.M.WeeklyPass();
        Check(h.Messages.SequenceEqual(new[] { 0xA3 }) && h.Awards.GoldTickets == 1 && h.Awards.FloatingTicketsLeft == 4,
              "Green pays 0xA3 once; a hidden award is won once a GAME (cal+0x28 outlives the park)");
        var again = new ParkManagement(new ParkClock(), h.Awards)
        { CurrentPark = () => (2, 0), Advisor = h.Messages.Add, AwardInputs = new ParkAwardInputs { Green = () => true } };
        again.WeeklyPass();
        Check(h.Messages.Count == 1, "...even from another park: FANTASY 1 cannot win Green again");
    }

    static void Green(Action<bool, string> Check)
    {
        AwardFootprint F(int x, int z, int w = 1, int d = 1) => new(x, z, w, d);
        var shops = new[] { F(10, 10, 2, 2), F(20, 10, 2, 2), F(30, 10, 2, 2), F(40, 10, 2, 2), F(50, 10, 2, 2) };
        var bins = shops.Select(s => F(s.X + 2, s.Z)).ToArray();
        Check(!ParkAwardInputs.ShopsHaveBins(4, shops.Take(4).ToList(), bins),
              "Green: four shops are too few however well binned");
        Check(ParkAwardInputs.ShopsHaveBins(5, shops, bins), "Green: five shops, each with a bin beside it");
        var far = bins.ToArray(); far[2] = F(shops[2].X + shops[2].Width + 3, shops[2].Z);
        var edge = bins.ToArray(); edge[2] = F(shops[2].X + shops[2].Width + 2, shops[2].Z);
        Check(!ParkAwardInputs.ShopsHaveBins(5, shops, far) && ParkAwardInputs.ShopsHaveBins(5, shops, edge),
              "Green's reach: a bin starting 2 cells past a shop's far edge counts, 3 cells does not");
        // ⚠ All four sides, each at its edge and one cell past: the far side alone left a mutation to the near-side
        // test standing (2026-10-02).
        AwardFootprint shop = shops[2];
        bool Reach(AwardFootprint bin) { var b = bins.ToArray(); b[2] = bin; return ParkAwardInputs.ShopsHaveBins(5, shops, b); }
        var sides = new (string Side, AwardFootprint Edge, AwardFootprint Past)[]
        {
            ("left", F(shop.X - 2 - 1, shop.Z), F(shop.X - 3 - 1, shop.Z)),
            ("right", F(shop.X + shop.Width + 2, shop.Z), F(shop.X + shop.Width + 3, shop.Z)),
            ("above", F(shop.X, shop.Z - 2 - 1), F(shop.X, shop.Z - 3 - 1)),
            ("below", F(shop.X, shop.Z + shop.Depth + 2), F(shop.X, shop.Z + shop.Depth + 3)),
        };
        var wrong = sides.Where(t => !Reach(t.Edge) || Reach(t.Past)).Select(t => t.Side).ToList();
        Check(wrong.Count == 0, $"Green's reach on every side: a bin 2 cells off counts, 3 does not ({(wrong.Count == 0 ? "all four" : "wrong: " + string.Join(",", wrong))})");
        Check(ParkAwardInputs.ShopsHaveBins(5, new List<AwardFootprint>(), new List<AwardFootprint>()),
              "Green's quirk: five shops of which none is open pass (nothing to test), as 0x152FB0 does");
    }

    static void Economy(Action<bool, string> Check)
    {
        var f = new Fixture(3, 1) { Rides = 9, Path = 100 };
        f.M.WeeklyPass(); bool nine = f.Messages.Count == 0;
        f.Rides = 10; f.Path = 101; f.M.WeeklyPass(); bool long1 = f.Messages.Count == 0;
        f.Path = 100; f.M.WeeklyPass();
        Check(nine && long1 && f.Messages.SequenceEqual(new[] { 0xA4 }),
              "Path Economy: more than 9 rides on at most 100 path cells (record +0x1C) -- nine rides, or 101 cells, win nothing");
        var census = new List<AwardPlacement>
        {
            new(AssetResourceDatabase.AssetKind.Ride, 1, 0, 1, new(0, 0, 2, 2), 0),
            new(AssetResourceDatabase.AssetKind.TourRide, 1, 0, 1, new(5, 0, 2, 2), 0),
            new(AssetResourceDatabase.AssetKind.Coaster, 1, 0, 0, new(9, 0, 2, 2), 0),
            new(AssetResourceDatabase.AssetKind.Feature, 1, 4, 0, new(3, 0, 1, 1), 40),
            new(AssetResourceDatabase.AssetKind.Feature, 1, 0, 0, new(3, 3, 1, 1), 300),
            new(AssetResourceDatabase.AssetKind.Shop, 1, 0, 0, new(1, 0, 1, 1), 0),
            new(AssetResourceDatabase.AssetKind.Sideshow, 1, 0, 0, new(1, 5, 1, 1), 0),
        };
        var inputs = ParkAwardInputs.FromCensus(() => census, () => 7);
        Check(inputs.Rides() == 4 && !inputs.AllRidesUpgraded() && inputs.FeatureCost() == 340
              && inputs.StarterCounts() == (1, 1, 2, 3) && inputs.PathCells() == 7,
              "the census: 3 rides plus one tour transport = 4, a tier-0 coaster blocks Upgrade, features cost 40 + 300, the starter counts");
    }
}
