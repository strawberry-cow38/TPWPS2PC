using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using TPW.PS2.Data;

/// <summary>Meaningful research-section bytes only. These are explicit core fixtures, NOT proof
/// of player reachability, outer save alignment, world/park selection, or a Save/Research UI.
/// Expected bytes use the literal retail catalogue below and raw disc DBA words, never
/// Level/Percent/Group/Work or a persistence traversal as an oracle. Reflection is read-only:
/// public queries would themselves promote group-zero records. The only direct setup writes
/// are the existing internals-visible ResearchProject properties, identified at the write sites.</summary>
static class ResearchPersistenceChecks
{
    static readonly int[] Order = { 3, 7, 6, 1, 2, 4, 5, 8 };
    static readonly int[] Lengths = { 98, 100, 100, 102, 94, 96, 94, 94 };

    // Independent catalogue fixture, columns in NATIVE order, not numeric kind order.
    // findings/research.md §1.1: all eight ordinary parks; no test/award park catalogue.
    static readonly uint[][][] Keys =
    {
        new uint[][] {
            new uint[] {221,222,223,224,226,228,230,234}, new uint[] {}, new uint[] {220}, new uint[] {225},
            new uint[] {179,182,184,186,188,190,192,193,194,197,198,200,202,204,205,207,208},
            new uint[] {239,240,241,242,243,244,245,246}, new uint[] {247,249,251,253}, new uint[] {237,236}},
        new uint[][] {
            new uint[] {216,215,227,231,233,219,229}, new uint[] {232}, new uint[] {235}, new uint[] {217,218},
            new uint[] {178,180,183,185,187,189,191,193,195,196,198,199,201,203,204,206,207,208},
            new uint[] {239,240,241,242,243,244,245,246}, new uint[] {252,250,248,606}, new uint[] {238}},
        new uint[][] {
            new uint[] {136,139,140,130,144,128,168}, new uint[] {145}, new uint[] {146}, new uint[] {132,169},
            new uint[] {100,102,103,105,106,108,109,110,111,113,114,115,116,117,120,121,122,462},
            new uint[] {147,148,149,150,151,152,153,154}, new uint[] {155,160,158,156}, new uint[] {167}},
        new uint[][] {
            new uint[] {131,134,137,141,142,143,129}, new uint[] {}, new uint[] {138}, new uint[] {133,135,170},
            new uint[] {101,104,105,107,110,112,113,115,118,119,120,121,458,459,460,461,463,464},
            new uint[] {147,148,149,150,151,152,153,154}, new uint[] {159,157,161,607}, new uint[] {165,166}},
        new uint[][] {
            new uint[] {46,48,55,57,58,60,83}, new uint[] {}, new uint[] {62}, new uint[] {47,52},
            new uint[] {26,27,29,31,32,34,36,37,40,465,467,469,470,85,471,472,475,477},
            new uint[] {64,65,66,67,68,69,70,71}, new uint[] {74,77,73}, new uint[] {}},
        new uint[][] {
            new uint[] {49,63,50,53,54,56}, new uint[] {61}, new uint[] {59}, new uint[] {51},
            new uint[] {26,28,30,84,33,35,39,465,466,468,469,470,473,474,475,476,478},
            new uint[] {64,65,66,67,68,69,70,71}, new uint[] {76,72,75,605}, new uint[] {81,82}},
        new uint[][] {
            new uint[] {365,366,372,374,379,380,403,383}, new uint[] {}, new uint[] {381}, new uint[] {371},
            new uint[] {338,340,342,344,346,347,348,350,353,355,356,357,358,479,481,483,484},
            new uint[] {385,386,387,388,389,390,391,392}, new uint[] {395,397,396}, new uint[] {402}},
        new uint[][] {
            new uint[] {364,368,369,373,377,378,405,382}, new uint[] {375}, new uint[] {367}, new uint[] {370,376},
            new uint[] {339,341,343,345,346,347,352,354,356,357,358,480,482,483,485},
            new uint[] {385,386,387,388,389,390,391,392}, new uint[] {393,394,398,608}, new uint[] {}}
    };

    readonly record struct Cell(int Cat, int Item, uint Key, int Percent);
    readonly record struct Raw(int Cat, int Item, int Percent, int Level);

    static IEnumerable<Cell> Cells(int park)
    {
        int pct = 0;
        for (int column = 0; column < 8; column++)
            for (int item = 0; item < Keys[park][column].Length; item++)
                yield return new Cell(Order[column], item, Keys[park][column][item], ++pct);
    }

    static int Word(AssetResourceDatabase dba, uint key, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(dba.Find(key).Payload.Span.Slice(offset, 4));
    static bool SimpleZero(AssetResourceDatabase dba, Cell c)
        => c.Cat is 2 or 4 or 5 or 8 && Word(dba, c.Key, 0x28) == 0;

    static ResearchManager Manager(AssetResourceDatabase dba, int park = 0)
        => new() { Database = new ResearchDatabase(park / 2, park % 2, dba) };

    static Raw[] Records(ResearchDatabase db)
    {
        var list = (IEnumerable)typeof(ResearchDatabase).GetField("_records", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(db);
        var result = new List<Raw>();
        foreach (object record in list)
        {
            object Field(string name) => record.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public).GetValue(record);
            result.Add(new Raw((int)Field("Cat"), (int)Field("Item"), Convert.ToInt32(Field("Percent")), Convert.ToInt32(Field("Level"))));
        }
        return result.ToArray();
    }
    static Raw Record(ResearchDatabase db, int cat, int item) => Records(db).Single(r => r.Cat == cat && r.Item == item);
    static string Slot(ResearchProject s)
        => $"{s.Slot}/{s.Weight}/{s.Required}/{s.Progress}/{s.Active}/{s.Complete}/{s.Category}/{s.Item}";
    static string Census(ResearchManager m)
        => $"{m.Database.World}/{m.Database.Park}/{m.Database.AllResearched};"
         + string.Join(";", Records(m.Database)) + $";{m.Budget}/{m.CompletedFlag}/{m.Quanta}/{m.Completions};"
         + string.Join(",", m.Thresholds) + ";" + string.Join(";", m.Slots.Select(Slot));

    sealed class Callbacks
    {
        public int Advisor, Researched, All, Left, Level, Mechanics, Built;
        public void Attach(ResearchManager m)
        {
            m.Advisor = _ => Advisor++;
            m.Researched = _ => Researched++;
            m.AllResearched = () => All++;
            m.AnythingLeftToResearch = () => { Left++; return false; };
            m.ItemLevel = (_, _) => { Level++; return 0; };
            m.MechanicCount = () => { Mechanics++; return 0; };
            m.BuiltCount = (_, _) => { Built++; return 0; };
        }
        public bool Quiet => Advisor + Researched + All + Left + Level + Mechanics + Built == 0;
    }

    public static void Run(Disc disc, Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "research persistence: explicit core fixture: " + label);
        var files = disc.Files();
        var data = files.Single(f => f.Path.Equals("/DATA/DATA.WAD", StringComparison.OrdinalIgnoreCase));
        var wad = new WadArchive(disc.Read(data.Extent, data.Size));
        var dba = new AssetResourceDatabase(wad.Read(wad.Find("/arsdb.dba")));
        var exe = files.Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        Executable(disc.Read(exe.Extent, exe.Size), Check);
        CataloguePayloads(dba, Check);
        Narrowing(dba, Check);
        Context(dba, Check);
        Projects(dba, Check);
        ExistingState(dba, Check);
        Malformed(dba, Check);
        TestPark(dba, Check);
    }

    static byte[] Oracle(AssetResourceDatabase dba, int park, int pass, byte budget)
    {
        var bytes = new List<byte>();
        foreach (var c in Cells(park))
        {
            bool zero = SimpleZero(dba, c);
            bytes.Add(zero ? (byte)100 : (byte)c.Percent);
            bytes.Add(zero ? (byte)(pass + 2) : (byte)1);
        }
        bytes.Add(budget);
        // Literal ctor triples: inactive category/item -1 narrowed to FF.
        for (int slot = 0; slot < 5; slot++) bytes.AddRange(new byte[] { 0, 255, 255 });
        return bytes.ToArray();
    }

    static void CataloguePayloads(AssetResourceDatabase dba, Action<bool, string> Check)
    {
        Check(ResearchPersistence.ManagerBytes == 16, "manager is exactly budget + five triples (16 bytes)");
        for (int park = 0; park < 8; park++)
        {
            var m = Manager(dba, park); var db = m.Database;
            var cb = new Callbacks(); cb.Attach(m);
            var cells = Cells(park).ToArray();
            Check(cells.All(c => dba.Find(c.Key) is { } e && (int)e.Kind == c.Cat), $"park {park}: literal keys resolve in real disc DBA");
            Check(Order.Select((cat, col) => db.Count(cat) == Keys[park][col].Length
                  && db.Keys(cat).SequenceEqual(Keys[park][col])).All(x => x), $"park {park}: catalogue ordinal identity agrees with independent literal fixture");
            Check(cells.Where(c => c.Cat is 1 or 3 or 6 or 7).All(c => Word(dba, c.Key, 0x7c) != 0),
                  $"park {park}: raw ride tier-1 groups nonzero, so seeded level1 must not promote");
            foreach (var c in cells) db.File(c.Cat, c.Item, 1, c.Percent);
            m.SetBudget(0x123);
            db.AllResearched = true; // omitted debug flag must not alter the raw persistence queries
            string seeded = Census(m);
            Check(ResearchPersistence.Length(db) == Lengths[park] && Census(m) == seeded,
                  $"park {park}: literal payload length {Lengths[park]}, Length has no query side effects");
            var first = ResearchPersistence.Save(m);
            var expected = Oracle(dba, park, 1, 0x23);
            Check(first.SequenceEqual(expected), $"park {park}: exact P-first/L-second bytes, cat3,7,6,1,2,4,5,8; no outer padding/context");
            Check(cells.All(c => Record(db, c.Cat, c.Item) == new Raw(c.Cat, c.Item,
                  SimpleZero(dba, c) ? (byte)0 : (byte)c.Percent, SimpleZero(dba, c) ? (byte)4 : (byte)1)),
                  $"park {park}: ONE Save captured L3/P100 but left each raw group0 simple at L4/P0");
            var loaded = Manager(dba, park); var loadCb = new Callbacks(); loadCb.Attach(loaded);
            ResearchPersistence.Load(loaded, expected);
            Check(cells.All(c => Record(loaded.Database, c.Cat, c.Item) == new Raw(c.Cat, c.Item,
                  SimpleZero(dba, c) ? (byte)0 : (byte)c.Percent, SimpleZero(dba, c) ? (byte)4 : (byte)1))
                  && loaded.Budget == 0x23 && loaded.ActiveCount == 0 && loaded.CompletedFlag && !loaded.Database.AllResearched,
                  $"park {park}: literal payload load files completion pairs, unsigned budget, ignores inactive FF keys and omitted debug flag");
            Check(loaded.Slots.All(s => s.Category == -1 && s.Item == -1 && !s.Complete && s.Required == 0 && s.Progress == 0),
                  $"park {park}: inactive triples do not manufacture stopped metadata");
            string beforeOwnership = Census(m);
            first[0] ^= 255;
            Check(Census(m) == beforeOwnership, $"park {park}: returned bytes owned, mutation cannot change live records");
            var second = ResearchPersistence.Save(m);
            Check(second.SequenceEqual(Oracle(dba, park, 2, 0x23)) && !ReferenceEquals(first, second),
                  $"park {park}: second invocation captures L4/P100, not the first pass's bytes");
            Check(cells.All(c => Record(db, c.Cat, c.Item).Level == (SimpleZero(dba, c) ? 5 : 1)),
                  $"park {park}: simulated native sizing+writing = TWO saves; raw group0 simple now L5");
            Check(cb.Quiet && loadCb.Quiet && m.Completions == 0 && loaded.Completions == 0,
                  $"park {park}: save/load have zero callbacks and no spurious completion");
        }
    }

    static void Narrowing(AssetResourceDatabase dba, Action<bool, string> Check)
    {
        var db = Manager(dba).Database;
        db.File(3, 0, 255, 99);
        Check(Record(db, 3, 0) == new Raw(3, 0, 99, 255), "File accepts unsigned-byte level255 before completion");
        db.File(3, 0, 255, 100);
        Check(Record(db, 3, 0) == new Raw(3, 0, 0, 0), "File increments/comparisons BEFORE byte stores: 255+1 wraps0");
        db.File(3, 0, 256, -1);
        Check(Record(db, 3, 0) == new Raw(3, 0, 255, 0), "File narrows level256 and signed percent-1 only at stores");
        db.File(3, 0, 1, 17);
        db.File(3, 0, -1, 42);
        Check(Record(db, 3, 0) == new Raw(3, 0, 17, 1), "signed level-1 refused before narrowing (not unsigned255)");
        db.File(3, 0, 0, 100);
        Check(Record(db, 3, 0) == new Raw(3, 0, 0, 1), "completion increment precedes comparison: level0/P100 matches existing level1");
        var literal = new byte[98]; literal[0] = 100; literal[1] = 255; literal[82] = 255;
        for (int s = 0; s < 5; s++) { literal[84 + 3 * s] = 255; literal[85 + 3 * s] = 255; }
        var m = Manager(dba); ResearchPersistence.Load(m, literal);
        Check(Record(m.Database, 3, 0) == new Raw(3, 0, 0, 0) && m.Budget == 255 && m.ActiveCount == 0,
              "literal load P100/L255 wraps0, budget FF is unsigned, inactive FF/FF never validated/restarted");
        m.SetBudget(-1);
        Check(ResearchPersistence.Save(m)[82] == 255, "Save budget-1 narrows to FF without clamping");

        var wrap = Manager(dba);
        foreach (var c in Cells(0)) wrap.Database.File(c.Cat, c.Item, 1, c.Percent);
        wrap.Database.File(2, 7, 255, 17); // key193, the group-zero security camera
        Check(Word(dba, 193, 0x28) == 0, "wrap fixture is a raw DBA group-zero simple item");
        var wrappedBytes = Oracle(dba, 0, 1, 80);
        wrappedBytes[34] = 100; wrappedBytes[35] = 255; // literal ordinal: 10 preceding rides, feature index7
        Check(ResearchPersistence.Save(wrap).SequenceEqual(wrappedBytes)
              && Record(wrap.Database, 2, 7) == new Raw(2, 7, 0, 0),
              "Save captures L255/P100 before group-zero Percent wraps LIVE level to0");
        var wrappedLoad = Manager(dba); ResearchPersistence.Load(wrappedLoad, wrappedBytes);
        Check(Record(wrappedLoad.Database, 2, 7) == new Raw(2, 7, 0, 0),
              "literal L255/P100 load also wraps0 without post-load querying/promotion");
        Check(ResearchPersistence.Save(wrap).SequenceEqual(Oracle(dba, 0, 2, 80).Select((b, i) => i == 35 ? (byte)3 : b)),
              "next invocation after byte wrap promotes camera anew to capturedL3/liveL4, while other simple zeros captureL4");
    }

    static void Context(AssetResourceDatabase dba, Action<bool, string> Check)
    {
        // Jungle1 and Hallow0 both have 100 meaningful bytes. No embedded context can detect
        // this mismatch: choosing the correct park is explicitly the OUTER coordinator's job.
        var bytes = Oracle(dba, 1, 1, 173);
        var target = Manager(dba, 2);
        ResearchPersistence.Load(target, bytes);
        var cells = Cells(2).ToArray();
        Check(target.Database.World == 1 && target.Database.Park == 0 && target.Budget == 173
              && cells.Select((c, i) => Record(target.Database, c.Cat, c.Item) == new Raw(c.Cat, c.Item,
                  bytes[2 * i] >= 100 ? (byte)0 : bytes[2 * i],
                  bytes[2 * i] >= 100 ? (byte)(bytes[2 * i + 1] + 1) : bytes[2 * i + 1])).All(x => x),
              "equal-length wrong-context literal payload files by target catalogue ordinal; world/park are not in this section");
    }

    // Literal all-five setup: four base projects and an upgrade (not a player interaction).
    static readonly (int Cat, int Item, uint Key, int Tier, int Pct)[] Jobs =
    {
        (3, 0, 221, 0, 13), (4, 3, 242, 0, 27), (5, 2, 251, 0, 39),
        (2, 0, 179, 0, 51), (3, 4, 226, 1, 67)
    };
    static int JobWork(AssetResourceDatabase dba, int slot)
    {
        var j = Jobs[slot];
        return Word(dba, j.Key, j.Cat is 1 or 3 or 6 or 7 ? 0x4c + 0x34 * j.Tier : 0x24);
    }
    static byte[] ProjectOracle(AssetResourceDatabase dba)
    {
        var bytes = Oracle(dba, 0, 1, 255);
        int at = 0;
        foreach (var c in Cells(0))
        {
            foreach (var j in Jobs)
                if (c.Cat == j.Cat && c.Item == j.Item) { bytes[at] = (byte)j.Pct; bytes[at + 1] = (byte)j.Tier; }
            at += 2;
        }
        for (int s = 0; s < 5; s++)
        {
            bytes[83 + 3 * s] = 1; bytes[84 + 3 * s] = (byte)Jobs[s].Cat; bytes[85 + 3 * s] = (byte)Jobs[s].Item;
        }
        return bytes;
    }
    static ResearchManager ProjectSource(AssetResourceDatabase dba)
    {
        var m = Manager(dba);
        foreach (var c in Cells(0))
        {
            var jobs = Jobs.Where(j => j.Cat == c.Cat && j.Item == c.Item).ToArray();
            m.Database.File(c.Cat, c.Item, jobs.Length == 0 ? 1 : jobs[0].Tier,
                            jobs.Length == 0 ? c.Percent : jobs[0].Pct);
        }
        m.SetBudget(255);
        for (int s = 0; s < 5; s++)
        {
            var j = Jobs[s]; m.Start(s, j.Cat, j.Item, JobWork(dba, s), j.Pct);
            // EXISTING internals-visible slot fields: an unsaved progress unit (fractional except
            // the native zero-work feature), dead weight,
            // and contradictory completion flag isolate what is/isn't in the persistence payload.
            m.Slots[s].Progress += 1;
            m.Slots[s].Weight = 90 + s;
            m.Slots[s].Complete = true;
        }
        return m;
    }

    static void Projects(AssetResourceDatabase dba, Action<bool, string> Check)
    {
        var source = ProjectSource(dba); var sourceCb = new Callbacks(); sourceCb.Attach(source);
        var bytes = ResearchPersistence.Save(source); var expected = ProjectOracle(dba);
        Check(bytes.SequenceEqual(expected), "five active triples in slot order, filed whole percents only, no progress/weight/complete fields");
        var target = Manager(dba); target.Database.AllResearched = false;
        var cb = new Callbacks(); cb.Attach(target);
        // Native loader uses any nonzero active byte, not just 1.
        var nonzero = (byte[])expected.Clone();
        for (int s = 0; s < 5; s++) nonzero[83 + 3 * s] = new byte[] { 2, 128, 255, 17, 1 }[s];
        ResearchPersistence.Load(target, nonzero);
        Check(target.ActiveCount == 5 && target.Budget == 255 && !target.Database.AllResearched
              && target.Thresholds.Take(4).SequenceEqual(new[] { 8, 8, 1, 8 }) && !target.CompletedFlag,
              "all DB pairs filed BEFORE restarts/Refresh: literal thresholds8/8/1/8, not fresh1/1/1/1 (one of two group1 sideshows still locked)");
        for (int s = 0; s < 5; s++)
        {
            var j = Jobs[s]; var t = target.Slots[s]; uint required = unchecked((uint)JobWork(dba, s) << 12);
            uint progress = unchecked((uint)j.Pct * required) / 100u;
            Check((s == 3 ? required == 0 : required > 100) && t.Active && t.Category == j.Cat && t.Item == j.Item && t.Required == required
                  && t.Progress == progress && source.Slots[s].Progress == progress + 1
                  && t.Weight == 0 && !t.Complete,
                  $"slot{s}: StartResearch rebuilds required from raw disc DBA, resumes filed percent (native zero-work feature included), drops unsaved progress/weight/complete");
            Check(Record(target.Database, j.Cat, j.Item) == new Raw(j.Cat, j.Item, (byte)j.Pct, (byte)j.Tier),
                  $"slot{s}: restart consumes filed percent at the loaded tier without completing it");
        }
        Check(sourceCb.Quiet && cb.Quiet && target.Quanta == 0 && target.Completions == 0,
              "active restart is not a quantum/completion: zero callbacks (including mechanic/advisor/all-researched)");

        source.Stop(1); source.Stop(3);
        var stoppedBytes = ResearchPersistence.Save(source);
        Check(stoppedBytes[86] == 0 && stoppedBytes[87] == 4 && stoppedBytes[88] == 3
              && stoppedBytes[92] == 0 && stoppedBytes[93] == 2 && stoppedBytes[94] == 0,
              "stopped slots serialize inactive plus their retained category/item");
        var stopped = Manager(dba); var stopCb = new Callbacks(); stopCb.Attach(stopped);
        ResearchPersistence.Load(stopped, stoppedBytes);
        Check(stopped.ActiveCount == 3 && new[] { 1, 3 }.All(s => stopped.Slots[s].Category == -1 && stopped.Slots[s].Item == -1
              && stopped.Slots[s].Progress == 0 && stopped.Slots[s].Required == 0),
              "fresh load ignores stopped metadata, does not reconstruct inactive projects");
        Check(Record(stopped.Database, 4, 3).Percent == 27 && Record(stopped.Database, 2, 0).Percent == 51 && stopCb.Quiet,
              "stopped projects keep DB percent; no callback or automatic restart");
    }

    static void ExistingState(AssetResourceDatabase dba, Action<bool, string> Check)
    {
        var inactive = Oracle(dba, 0, 1, 201);
        var m = Manager(dba); var cb = new Callbacks(); cb.Attach(m);
        m.Database.File(3, 7, 20, 9); // higher level must not be wiped or lowered
        m.Start(0, 3, 1, 123, 7);
        m.Start(1, 5, 0, 321, 19); m.Stop(1);
        // EXISTING internals-visible slot setup: inactive completion metadata is a sentinel.
        m.Slots[1].Complete = true; m.Slots[1].Weight = 77;
        string[] before = m.Slots.Select(Slot).ToArray();
        ResearchPersistence.Load(m, inactive);
        Check(m.Slots.Select(Slot).SequenceEqual(before) && m.Budget == 201 && m.ActiveCount == 1,
              "load is not reset/wipe: ALL inactive triples leave existing active and stopped slot metadata untouched");
        Check(Record(m.Database, 3, 7) == new Raw(3, 7, 9, 20), "File refuses to lower pre-existing higher DB level on load");
        var active = ProjectOracle(dba); active[83] = 255;
        ResearchPersistence.Load(m, active);
        Check(Slot(m.Slots[0]) == before[0], "busy active slot refuses restart; loader ignores result, preserves its project");
        Check(m.Slots[1].Active && m.Slots[1].Category == 4 && m.Slots[1].Item == 3 && !m.Slots[1].Complete,
              "inactive target slot can restart from active triple despite stale complete/category/item");
        Check(cb.Quiet && m.Completions == 0, "native-style busy refusal and stale-slot reuse produce no completion callbacks");

        var refused = new byte[98]; refused[82] = 211;
        refused[83] = 128; refused[84] = 3; refused[85] = 7; // Jungle ride234, tier0 group above the fresh threshold1
        var r = Manager(dba); var rcb = new Callbacks(); rcb.Attach(r);
        Check(Word(dba, 234, 0x48) > 1, "refusal fixture independently has raw tier0 group above threshold1");
        ResearchPersistence.Load(r, refused);
        Check(r.Budget == 211 && r.Thresholds[0] == 1 && r.ActiveCount == 0 && r.Slots[0].Item == -1
              && Record(r.Database, 3, 7) == new Raw(3, 7, 0, 0) && rcb.Quiet,
              "valid active key above threshold: DB/budget restored, StartResearch refusal ignored, not a malformed error");
    }

    static void Reject(ResearchManager m, byte[] bytes, string message, Action<bool, string> Check, bool testPark = false)
    {
        string before = Census(m); string error = null;
        try { ResearchPersistence.Load(m, bytes, testPark); }
        catch (InvalidDataException ex) { error = ex.Message; }
        Check(error == message, $"strict host guard: {message}");
        Check(Census(m) == before, "malformed rejection BEFORE any DB, budget, slot, threshold, flag or instrumentation writes (read-only census)");
    }
    static void Malformed(AssetResourceDatabase dba, Action<bool, string> Check)
    {
        var m = ProjectSource(dba); m.Database.AllResearched = true;
        var cb = new Callbacks(); cb.Attach(m);
        foreach (int length in new[] { 0, 1, 97, 99, 100, 101 })
            Reject(m, new byte[length], $"research section length {length}, expected 98", Check);
        // First triple valid and first pair would unlock; bad final triple proves whole-section
        // preflight, not an incremental guard after writing earlier records/budget/slots.
        foreach (var key in new[] { (Cat: 0, Item: 0, Signed: 0), (Cat: 9, Item: 0, Signed: 0),
                                   (Cat: 255, Item: 0, Signed: 0), (Cat: 3, Item: 255, Signed: -1),
                                   (Cat: 3, Item: 128, Signed: -128), (Cat: 3, Item: 127, Signed: 127),
                                   (Cat: 3, Item: 8, Signed: 8), (Cat: 7, Item: 0, Signed: 0) })
        {
            var bytes = ProjectOracle(dba); bytes[0] = 100; bytes[82] = 42;
            bytes[95] = 255; bytes[96] = (byte)key.Cat; bytes[97] = (byte)key.Item;
            Reject(m, bytes, $"research active slot 4 has invalid catalogue key {key.Cat}/{key.Signed}", Check);
        }
        Check(cb.Quiet, "defensive malformed-input deviation from unsafe native loader is atomic and callback-free");
    }

    static void TestPark(AssetResourceDatabase dba, Action<bool, string> Check)
    {
        var m = ProjectSource(dba); var cb = new Callbacks(); cb.Attach(m);
        string before = Census(m);
        Check(ResearchPersistence.Length(m.Database, true) == 0 && ResearchPersistence.Save(m, true).Length == 0
              && Census(m) == before, "explicit testPark skips BOTH payloads and ALL mutating queries");
        ResearchPersistence.Load(m, ReadOnlySpan<byte>.Empty, true);
        Check(Census(m) == before && cb.Quiet, "testPark empty load does not file, set budget, reset, restart or callback");
        Reject(m, new byte[98], "research section length 98, expected 0", Check, true);
        var actual = new ResearchManager { Database = new ResearchDatabase(0, 2, dba) };
        string fresh = Census(actual);
        Check(ResearchPersistence.Length(actual.Database, true) == 0 && ResearchPersistence.Save(actual, true).Length == 0
              && Census(actual) == fresh, "real Jungle test-park context + explicit skip flag: no lazy record allocations");
    }

    static void Executable(byte[] elf, Action<bool, string> Check)
    {
        uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(at, 4));
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(at, 2));
        uint WordAt(uint address)
        {
            int ph = checked((int)U32(28));
            for (int i = 0; i < U16(44); i++)
            {
                int p = ph + i * U16(42);
                if (U32(p) != 1) continue;
                uint va = U32(p + 8), size = U32(p + 16);
                if (address >= va && (ulong)address + 4 <= (ulong)va + size)
                    return U32(checked((int)(U32(p + 4) + address - va)));
            }
            throw new InvalidDataException($"research persistence ELF word missing at 0x{address:X}");
        }
        Check(WordAt(0x1b6764) == 0x0c06da20,
              "disc ELF 0x1B6764 = jal0x1B6880: native loader restarts through StartResearch, not raw slot copying");
        Check(WordAt(0x12bb3c) == 0x2a220064,
              "disc ELF 0x12BB3C = slti v0,s1,100: File's completion comparison is signed, before byte storage");
        // Register allocation is immaterial here; literal opcode/record-offset teeth at the two
        // final stores prove these are byte fields, not widened host state. The pair is unordered.
        uint a = WordAt(0x12bb78) & 0xfc00ffff, b = WordAt(0x12bb7c) & 0xfc00ffff;
        Check((a == 0xa0000002 && b == 0xa0000003) || (a == 0xa0000003 && b == 0xa0000002),
              "disc ELF 0x12BB78/7C are sb to record+2/+3: percent/level narrow at final stores");
    }
}
