using System.Buffers.Binary;
using TPW.PS2.Data;

/// <summary>⭐ RESEARCH (findings/research.md): the per-park catalogue against the disc's DBA, what a fresh park starts with,
/// the rows' thresholds and candidates, a project researched to completion (the unlock, the message, and NO restart), an
/// upgrade on the Upgrades row, and the executable words behind the two claims a reading could most easily get wrong.</summary>
static class ResearchChecks
{
    public static void Run(Disc disc, Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "research: " + label);
        var exe = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        Executable(disc.Read(exe.Extent, exe.Size), Check);
        var dataEntry = disc.Files().Single(f => f.Path.Equals("/DATA/DATA.WAD", StringComparison.OrdinalIgnoreCase));
        var data = new WadArchive(disc.Read(dataEntry.Extent, dataEntry.Size));
        var dba = new AssetResourceDatabase(data.Read(data.Find("/arsdb.dba")));
        Catalogue(dba, Check);
        FreshPark(dba, Check);
        Project(dba, Check);
        Upgrade(dba, Check);
        AdvisorResearchChecks.Run(dba, check);
    }

    static void Executable(byte[] elf, Action<bool, string> Check)
    {
        uint U32(int off) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(off, 4));
        int U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(off, 2));
        var calls = new List<uint>();
        uint at = 0;
        int ph = checked((int)U32(28));
        for (int i = 0; i < U16(44); i++)
        {
            int p = ph + i * U16(42);
            if (U32(p) != 1) continue;
            int off = checked((int)U32(p + 4)); uint va = U32(p + 8); int size = checked((int)U32(p + 16));
            for (int o = 0; o + 4 <= size; o += 4)
            {
                uint w = U32(off + o);
                if (w == 0x0c06da20) calls.Add(va + (uint)o);              // jal 0x1B6880
                if (va + (uint)o == 0x12bb3c) at = w;
            }
        }
        Check(calls.SequenceEqual(new uint[] { 0x1b5644, 0x1b6764 }),
              $"0x1B6880 (start a project) has exactly two callers, the Research screen and the save loader "
              + $"({string.Join(", ", calls.Select(c => $"0x{c:X}"))}): nothing starts research on its own");
        Check(at == 0x2a220064, "0x12BAF8 tests the filed percent against 100 (slti s1, 0x64 at 0x12BB3C): reaching it IS the unlock");
        uint Word(uint address)
        {
            for (int i = 0; i < U16(44); i++)
            {
                int p = ph + i * U16(42);
                if (U32(p) != 1) continue;
                uint va = U32(p + 8), size = U32(p + 16);
                if (address >= va && address + 4 <= va + size)
                    return U32(checked((int)(U32(p + 4) + address - va)));
            }
            throw new InvalidDataException($"no executable word at 0x{address:X}");
        }
        Check(Word(0x104544) == 0x32960004 && Word(0x1045e0) == 0x12c00012
              && Word(0x1045ec) == 0x0c04ac28 && Word(0x104608) == 0x24050001,
              "advisor research mask bug is in the ELF: the same bit4 gates track and coaster (count call0x12B0A0, availability kind1)");
        Check(Word(0x103f58) == 0x30620001 && Word(0x103f70) == 0x30620008 && Word(0x103f84) == 0x30620002,
              "advisor variety feature flags read in order1/8/2: toilet, camera, staff room");
        Check(Word(0x103be8) == 0x9082009a && Word(0x103c28) == 0x2a020032,
              "advisor variety reads nonzero object status and counts a50-entry distinct-type set");
        Check(Word(0x104950) == 0xac710000 && Word(0x10499c) == 0x5640ffe2 && Word(0x1049a0) == 0x92420126,
              "upgrade-use helper stores the largest tier+1 and loops over placements using tier+0x126");
    }

    static void Catalogue(AssetResourceDatabase dba, Action<bool, string> Check)
    {
        int keys = 0, bad = 0;
        for (int w = 0; w < 4; w++)
            for (int p = 0; p < 3; p++)
                for (int k = 1; k <= 8; k++)
                    foreach (uint key in ResearchCatalogue.Keys(w, p, (AssetResourceDatabase.AssetKind)k))
                    {
                        keys++;
                        if (dba.Find(key) is not { } e || (int)e.Kind != k) bad++;
                    }
        Check(keys == 347 && bad == 0, $"all {keys} catalogue keys resolve in arsdb.dba with their list's kind ({bad} do not)");
    }

    static readonly uint[] JungleStart = { 222, 226, 228, 225, 193, 198, 204, 208, 240, 241, 243, 247, 249 };
    static readonly uint[] SpaceTwoStart = { 364, 373, 405, 382, 339, 346, 352, 354, 357, 358, 480, 482, 483, 485, 387, 391 };

    static List<uint> AvailableAtStart(ResearchDatabase db)
    {
        var got = new List<uint>();
        foreach (int k in new[] { 3, 7, 6, 1, 2, 4, 5, 8 })
            for (int i = 0; i < db.Count(k); i++)
                if (db.Available(k, i, 0)) got.Add(db.Keys(k)[i]);
        return got;
    }

    static void FreshPark(AssetResourceDatabase dba, Action<bool, string> Check)
    {
        var jungle = new ResearchDatabase(0, 0, dba);
        var got = AvailableAtStart(jungle);
        Check(got.OrderBy(k => k).SequenceEqual(JungleStart.OrderBy(k => k)),
              $"JUNGLE park 1 starts with exactly the {got.Count} items whose tier-0 group is 0 (the live savestate's 13): "
              + string.Join(" ", got));
        var space = AvailableAtStart(new ResearchDatabase(3, 1, dba));
        Check(space.OrderBy(k => k).SequenceEqual(SpaceTwoStart.OrderBy(k => k)),
              $"SPACE park 2 starts with its {space.Count} -- a different list, so the rule is not one park's constant");
        var all = new ResearchDatabase(0, 0, dba) { AllResearched = true };
        Check(AvailableAtStart(all).Count == 41, "the AllResearched debug key opens all 41 of JUNGLE park 1's items (the control)");

        var mgr = new ResearchManager { Database = new ResearchDatabase(0, 0, dba) };
        mgr.Refresh();
        string Keys(int row) => string.Join(" ", mgr.Candidates(row).Select(c => mgr.Database.Keys(c.Cat)[c.Item]));
        Check(mgr.Thresholds.Take(4).SequenceEqual(new[] { 1, 1, 1, 1 }) && Keys(0) == "221 223 224 230 220"
              && Keys(1) == "242 245 246" && Keys(2) == "251 253" && Keys(4) == "",
              $"a fresh JUNGLE park: thresholds [{string.Join(",", mgr.Thresholds.Take(4))}], Rides offers {Keys(0)}, Shops {Keys(1)}, "
              + $"Sideshows {Keys(2)}, and Upgrades nothing (no ride built)");
    }

    static void Project(AssetResourceDatabase dba, Action<bool, string> Check)
    {
        var messages = new List<int>();
        var db = new ResearchDatabase(0, 0, dba);
        var mgr = new ResearchManager { Database = db, Advisor = messages.Add, MechanicCount = () => 1 };
        mgr.OpenResearchScreen();
        bool started = mgr.StartResearch(0, 3, 0);                      // ride 221, row 0's first candidate
        int quanta = 0;
        while (mgr.Slots[0].Active && quanta++ < 100_000) mgr.Contribute(20);
        var slot = mgr.Slots[0];
        Check(started && db.Available(3, 0, 0) && db.Level(3, 0) == 1 && messages.SequenceEqual(new[] { 0x4B })
              && !slot.Active && slot.Complete && slot.Item == -1,
              $"ride 221 researched by a level-0 researcher in {quanta} quanta: available, level 1, message 0x4B, and the slot idle");
        int before = mgr.ActiveCount;
        mgr.Contribute(20);
        Check(before == 0 && mgr.ActiveCount == 0 && !mgr.Candidates(0).Contains((3, 0)),
              "nothing starts the next item: the row stays idle, and 221 is no longer a candidate");
    }

    static void Upgrade(AssetResourceDatabase dba, Action<bool, string> Check)
    {
        var messages = new List<int>();
        var db = new ResearchDatabase(0, 0, dba);
        int ape = db.Keys(3).ToList().IndexOf(226);
        bool built = false;
        var mgr = new ResearchManager
        {
            Database = db, Advisor = messages.Add, MechanicCount = () => 0,
            BuiltCount = (cat, item) => built && cat == 3 && item == ape ? 1 : 0,
        };
        bool offeredUnbuilt = mgr.Candidates(4).Contains((3, ape));
        built = true;
        bool offered = mgr.Candidates(4).Contains((3, ape));
        mgr.OpenResearchScreen();
        mgr.StartResearch(4, 3, ape);
        int quanta = 0;
        while (mgr.Slots[4].Active && quanta++ < 100_000) mgr.Contribute(20);
        Check(!offeredUnbuilt && offered && db.Level(3, ape) == 2 && messages.SequenceEqual(new[] { 0x7E }),
              $"Crazy Ape's first upgrade: offered on Upgrades only once one is built, researched in {quanta} quanta to level 2, "
              + "and with no mechanic hired the message is 0x7E (hire one) rather than 0x4C");
    }
}
