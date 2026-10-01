using System.Buffers.Binary;
using System.Numerics;
using TPW.PS2.Data;
using AdvisorAssetKind = TPW.PS2.Data.AssetResourceDatabase.AssetKind;

/// <summary>
/// Advisor research producers: literal oracles from advisor-rules §5 and research §1.5/4.3,
/// native 0x103B20/0x1044B0/0x104A40/0x104BD0 and max-tier helper 0x104830.
/// The DBA is retail; placements and installed tiers below are EXPLICIT CORE INPUTS, not
/// evidence that gameplay built, opened, broke or upgraded these facilities. No scripts run.
/// Expected percentages are literals, never another call to the formula being tested.
/// </summary>
static class AdvisorResearchChecks
{
    public static void Run(AssetResourceDatabase dba, Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "advisor research: " + label);
        Fresh(dba, Check);
        MasksAndVariety(dba, Check);
        Features(dba, Check);
        LiveResearch(dba, Check);
        Upgrades(dba, Check);
        DefaultsAndHooks(dba, Check);
    }

    static AdvisorProducers Producer(ResearchDatabase db, params AdvisorPlacement[] placements)
        => new(new ParkClock()) { Database = db, Placements = () => placements };

    static AdvisorPlacement Placement(int cat, uint key, byte status = 1, int tier = 0, byte flags = 0)
        => new((AdvisorAssetKind)cat, status, flags, cat is 1 or 3 or 6 or 7 ? new ParkRide { CurrentTier = tier } : null)
        { CatalogueKey = key };

    static void Equal(int actual, int expected, string label, Action<bool, string> check)
        => check(actual == expected, $"{label}: expected {expected}, got {actual}");

    static void Vector(AdvisorProducers p, int[] expected, string label, Action<bool, string> check)
    {
        for (int i = 0; i < 10; i++) Equal(p.Produce(21 + i), expected[i], $"{label} v{21 + i}", check);
    }

    static void Fresh(AssetResourceDatabase dba, Action<bool, string> check)
    {
        // Start lists are literal research.md §1.5 DATA/DERIVED, not recomputed from DBA groups.
        // Ride/shop/sideshow/feature denominators respectively:
        // J0 10/8/4/17; J1 11/8/4/18; H0,H1 11/8/4/18;
        // F0 10/8/3/18; F1 9/8/4/17; S0 10/8/3/17; S1 12/8/4/15.
        var parks = new (int World, int Park, string Name, uint[] Start, int[] Values)[]
        {
            (0, 0, "JUNGLE0", new uint[] {222,226,228,225,193,198,204,208,240,241,243,247,249},
                new[] {0,40,100,0,0,37,0,50,0,23}),
            (0, 1, "JUNGLE1", new uint[] {215,227,219,229,183,193,198,204,208,240,241,243,252,250},
                new[] {0,36,100,0,0,37,0,50,0,27}),
            (1, 0, "HALLOW0", new uint[] {139,140,130,100,105,106,113,115,120,462,147,151,152,155,160},
                new[] {0,27,100,0,0,37,0,50,0,38}),
            (1, 1, "HALLOW1", new uint[] {142,143,129,105,112,113,115,120,461,147,151,152,159,161},
                new[] {0,27,100,0,0,37,0,50,0,33}),
            (2, 0, "FANTASY0", new uint[] {46,57,83,26,29,465,470,85,475,65,66,67,74,77},
                new[] {0,30,100,0,0,37,0,66,0,33}),
            (2, 1, "FANTASY1", new uint[] {50,53,26,33,35,465,470,475,478,65,66,67,72},
                new[] {0,22,100,0,0,37,0,25,0,41}),
            (3, 0, "SPACE0", new uint[] {366,403,340,346,350,353,357,358,479,481,483,484,387,391,395},
                new[] {0,20,100,0,0,25,0,33,0,58}),
            (3, 1, "SPACE1", new uint[] {364,373,405,382,339,346,352,354,357,358,480,482,483,485,387,391},
                new[] {0,33,100,0,0,25,100,0,0,66}),
        };
        foreach (var f in parks)
        {
            var db = new ResearchDatabase(f.World, f.Park, dba);
            var available = new List<uint>();
            foreach (int cat in new[] {1,2,3,4,5,6,7,8})
                for (int i = 0; i < db.Count(cat); i++)
                    if (db.Available(cat, i, 0)) available.Add(db.Keys(cat)[i]);
            check(available.OrderBy(k => k).SequenceEqual(f.Start.OrderBy(k => k)),
                  $"{f.Name} exact literal start list ({f.Start.Length} keys)");
            Vector(Producer(db), f.Values, f.Name + " empty explicit census", check);
        }
        var empty = Producer(new ResearchDatabase(1, 2, dba), Placement(3, 222, tier: 2));
        Vector(empty, new[] {100,100,100,100,100,100,100,100,100,100}, "HALLOW2 empty catalogue", check);
        foreach (int mask in new[] {0,1,2,4,8,0x10,0x20,0x40,0x80,0x100,0x200,0x3ff})
        {
            Equal(empty.ResearchedPercent(mask), 100, $"HALLOW2 research mask {mask:X}", check);
            Equal(empty.VarietyPercent(mask), 100, $"HALLOW2 variety mask {mask:X}", check);
        }
        Equal(empty.InstalledUpgradePercent(), 100, "HALLOW2 installed upgrades", check);
    }

    static void MasksAndVariety(AssetResourceDatabase dba, Action<bool, string> check)
    {
        var p = Producer(new ResearchDatabase(0, 0, dba), Placement(3,222), Placement(1,225),
            Placement(4,240), Placement(5,247), Placement(2,193), Placement(2,208));
        Vector(p, new[] {50,40,100,0,33,37,50,50,50,23}, "mixed six explicit types", check);
        Equal(p.ResearchedPercent(1), 37, "ordinary research 3/8", check);
        Equal(p.ResearchedPercent(2), 100, "empty tour research", check);
        Equal(p.ResearchedPercent(4), 50, "bit4 research selects track AND coaster: 1/2", check);
        Equal(p.ResearchedPercent(8), 100, "bit8 ignored by research (NOT coaster-only)", check);
        Equal(p.ResearchedPercent(1 | 8), 37, "bit8 adds no research denominator", check);
        Equal(p.ResearchedPercent(1 | 4), 40, "ordinary+track+coaster research 4/10", check);
        Equal(p.ResearchedPercent(1 | 0x100), 37, "ordinary+shops research 6/16 truncates", check);
        Equal(p.ResearchedPercent(4 | 0x100), 40, "track+coaster+shops research 4/10", check);
        Equal(p.ResearchedPercent(0x3ff), 33, "all base research 13/39, no kind8 add-ons", check);
        Equal(p.ResearchedPercent(0), 100, "zero research mask", check);
        Equal(p.VarietyPercent(0), 100, "zero variety mask", check);
        Equal(p.VarietyPercent(1), 33, "ordinary variety 1/3", check);
        Equal(p.VarietyPercent(4), 100, "track kind skipped (zero available)", check);
        Equal(p.VarietyPercent(8), 100, "bit8 DOES select coaster variety", check);
        Equal(p.VarietyPercent(1 | 0x100), 33, "ordinary+shop variety 2/6", check);
        Equal(p.VarietyPercent(0xf | 0x100), 42, "ride+shop variety 3/7 truncates", check);
        Equal(p.VarietyPercent(0x3ff), 46, "all variety 6/13 (aggregate, not mean)", check);

        var db = new ResearchDatabase(0, 0, dba);
        var census = new List<AdvisorPlacement> { Placement(3,222), Placement(3,222,5),
            Placement(3,226,0), Placement(3,228,5), Placement(3,uint.MaxValue), Placement(3,215),
            Placement(4,222), new((AdvisorAssetKind)3, 1, 0, null), Placement(6,220,5) };
        p = Producer(db); p.Placements = () => census;
        Equal(p.VarietyPercent(0xf), 50, "duplicates once; status0 omitted; broken5 counted; unknown/crosspark/wrongkind/null ignored; locked track kind skipped", check);
        census.Add(Placement(3,221,5)); // locked ordinary type, but ordinary KIND has three available
        Equal(p.VarietyPercent(1), 100, "locked built ordinary type counts individually: 3/3", check);
        census.Clear(); census.Add(Placement(3,221));
        Equal(p.VarietyPercent(1), 33, "locked type alone still contributes 1/3", check);
        census.Add(Placement(6,220));
        Equal(p.VarietyPercent(0xf), 25, "locked track cannot inflate mixed 1/4: skip whole unavailable kind", check);
        census.AddRange(new[] {Placement(3,223), Placement(3,224), Placement(3,230)});
        Equal(p.VarietyPercent(1), 100, "4 built types over 3 available saturates at100", check);
    }

    static void Features(AssetResourceDatabase dba, Action<bool, string> check)
    {
        // Pin the actual retail classification bits without inventing a synthetic DBA.
        check(dba.Find(208)?.RawFeatureFlags is byte t && (t & 1) == 1, "retail208 has toilet bit");
        check(dba.Find(193)?.RawFeatureFlags is byte c && (c & 11) == 8, "retail193 camera, not toilet/staffroom");
        check(dba.Find(204)?.RawFeatureFlags is byte s && (s & 11) == 2, "retail204 staffroom, not toilet/camera");
        check(dba.Find(198)?.RawFeatureFlags is byte b && (b & 11) == 0, "retail198 bin is OTHER for advisor classification");
        Equal(AdvisorPlacement.FeatureClassOf(9), 0x20, "flags9 toilet beats camera", check);
        Equal(AdvisorPlacement.FeatureClassOf(10), 0x40, "flags10 camera beats staffroom", check);
        Equal(AdvisorPlacement.FeatureClassOf(3), 0x20, "flags3 toilet beats staffroom", check);
        Equal(AdvisorPlacement.FeatureClassOf(2), 0x80, "flags2 staffroom", check);
        Equal(AdvisorPlacement.FeatureClassOf(0), 0x10, "flags0 other", check);
        foreach (byte flags in new byte[] {9,10,3})
            Equal(new AdvisorPlacement(AdvisorAssetKind.Feature,1,flags,null).FeatureClass,
                flags == 10 ? 0x40 : 0x20, $"placement classification byte{flags}", check);

        var census = new List<AdvisorPlacement> { Placement(2,208,flags:8), Placement(2,208,5,flags:2),
            Placement(2,193,0,flags:1), Placement(2,204,5), Placement(2,198) };
        var p = Producer(new ResearchDatabase(0,0,dba)); p.Placements = () => census;
        // Deliberately wrong placement flags: variety must use the CATALOGUE's DBA flags.
        Equal(p.VarietyPercent(0x20), 100, "toilet variety208 once, from DBA not placement flags", check);
        Equal(p.VarietyPercent(0x40), 0, "camera status0 excluded", check);
        Equal(p.VarietyPercent(0x80), 100, "broken staffroom counts", check);
        Equal(p.VarietyPercent(0x10), 100, "bin other counts", check);
        Equal(p.VarietyPercent(0xf0), 75, "three of four feature types, duplicate toilet not four", check);
        Equal(p.VarietyPercent(0x60), 50, "toilet+camera variety 1/2", check);
        foreach (int mask in new[] {0x10,0x20,0x40,0x80,0x60,0xf0})
            Equal(p.ResearchedPercent(mask), 23, $"feature research mask{mask:X} ALL features 4/17", check);
        Equal(p.ResearchedPercent(1 | 0x20), 28, "ordinary+ANY feature research 7/25", check);
        Equal(p.VarietyPercent(1 | 0x20), 25, "ordinary+toilet variety 1/4", check);
        census.Clear(); census.Add(Placement(2,208,0));
        Equal(p.VarietyPercent(0x20), 0, "status0 toilet alone is absent", check);
        census.Add(Placement(2,193,5));
        Equal(p.VarietyPercent(0x20), 0, "camera does not leak into toilet subtype", check);
        Equal(p.VarietyPercent(0x40), 100, "nonzero5 camera counts", check);
    }

    static void LiveResearch(AssetResourceDatabase dba, Action<bool, string> check)
    {
        var db = new ResearchDatabase(0,0,dba);
        var p = Producer(db, Placement(3,222));
        Equal(p.Produce(22), 40, "live baseline research", check);
        Equal(p.Produce(21), 25, "live baseline variety 1/4", check);
        db.File(3,0,0,99); // catalogue item0 = locked221; explicit filed core input
        Equal(p.Produce(22), 40, "File99 is NOT unlock", check);
        Equal(p.Produce(21), 25, "File99 leaves denominator unchanged", check);
        Equal(p.Produce(24), 0, "File99 adds no unlocked upgrade type", check);
        db.File(3,0,0,100);
        Equal(p.Produce(22), 50, "File100 updates existing producer research 5/10", check);
        Equal(p.ResearchedPercent(1), 50, "live method ordinary 4/8", check);
        Equal(p.Produce(21), 20, "live variety denominator becomes5", check);
        Equal(p.Produce(24), 0, "base unlock is not upgrade research", check);
        db.File(3,0,1,99);
        Equal(p.Produce(24), 0, "upgrade File99 does not count", check);
        db.File(3,0,1,100);
        Equal(p.Produce(24), 10, "one upgrade / ten possible on five unlocked types", check);
        Equal(p.UpgradeResearchPercent, 10, "live upgrade property literal10", check);
        Equal(p.Produce(23), 100, "unbuilt221 upgrades do not affect built222's zero potential", check);
        p.Placements = () => new[] {Placement(3,221)};
        Equal(p.InstalledUpgradePercent(), 0, "built221 now has one unused researched upgrade", check);
    }

    static void Upgrades(AssetResourceDatabase dba, Action<bool, string> check)
    {
        var db = new ResearchDatabase(0,0,dba);
        db.File(3,4,1,100); //226 L2; other three unlocked rides stay L1
        var p = Producer(db, Placement(3,226));
        Equal(p.Produce(24), 12, "one of eight possible upgrades (all four unlocked types)", check);
        Equal(p.InstalledUpgradePercent(), 0, "L2 built at tier0", check);
        p.Placements = () => new[] {Placement(3,226,0,1)};
        Equal(p.Produce(23), 100, "status0 tier1 uses upgrade", check);
        Equal(p.VarietyPercent(1), 0, "same status0 excluded ONLY from variety", check);
        db.File(3,4,2,100); //226 L3
        Equal(p.Produce(24), 25, "two of eight possible upgrades", check);
        Equal(p.Produce(23), 50, "single L3 type with tier1 ->50", check);
        p.Placements = () => new[] {Placement(3,226,5,0), Placement(3,226,0,1), Placement(3,226,1,1)};
        Equal(p.InstalledUpgradePercent(), 50, "same-key duplicates MAX tier1, not sum2", check);
        p.Placements = () => new[] {Placement(3,226,5,1), Placement(3,226,0,2), Placement(3,226,1,0)};
        Equal(p.InstalledUpgradePercent(), 100, "max tier2 even on status0 duplicate", check);
        db.File(3,1,1,100); //222 L2, unbuilt
        Equal(p.Produce(24), 37, "unbuilt222 upgrade DOES enter upgrade research 3/8", check);
        Equal(p.Produce(23), 100, "unbuilt222 upgrade does NOT enter installed denominator", check);
        p.Placements = () => new[] {Placement(3,226,1,1), Placement(3,222,0,0),
            Placement(3,uint.MaxValue,0,2), Placement(3,215,1,2), Placement(4,226,1,2)};
        Equal(p.InstalledUpgradePercent(), 33, "two built types: tier1 / (2+1), unknown/crosspark/wrongkind ignored", check);
        Equal(p.Produce(23), 33, "v23 same literal mixed built denominator", check);
        p.Placements = () => Array.Empty<AdvisorPlacement>();
        Equal(p.Produce(23), 100, "no built types despite researched upgrades ->100", check);

        // Exercise all four native pools in a park with a tour ride; every filed type is L3.
        db = new ResearchDatabase(0,1,dba);
        db.File(3,1,2,100); db.File(6,0,2,100); db.File(7,0,2,100); db.File(1,0,2,100);
        p = Producer(db, Placement(3,215,0,1), Placement(6,235,5,2), Placement(7,232,0,1), Placement(1,217,5,0));
        Equal(p.InstalledUpgradePercent(), 50, "four pools: installed4 / researched8, all statuses", check);
        Equal(p.Produce(24), 57, "four L3 + three L1 unlocked types: 8/14 upgrades", check);

        // Live-definition key fallback and explicit key override are separate adapter contracts.
        var ride = new ParkRide { CurrentTier = 1, Definition = new RideDefinition { CompiledEntry = dba.Find(226) } };
        var fallback = new AdvisorPlacement(AdvisorAssetKind.Ride,0,0,ride);
        check(fallback.Key == 226u, "key falls back to live Ride.Definition.CompiledEntry.Key");
        db = new ResearchDatabase(0,0,dba); db.File(3,4,2,100);
        p = Producer(db, fallback);
        Equal(p.InstalledUpgradePercent(), 50, "fallback key participates in upgrades", check);
        p.Placements = () => new[] {fallback with { Status = 5 }};
        Equal(p.VarietyPercent(1), 33, "nonzero live-definition fallback counts one ordinary type", check);
        p.Placements = () => new[] {fallback with { CatalogueKey = 222 }};
        check((fallback with { CatalogueKey = 222 }).Key == 222u, "CatalogueKey overrides live key");
        Equal(p.InstalledUpgradePercent(), 100, "override222 has L1 zero potential, not226 L3", check);

        db = new ResearchDatabase(0,0,dba) { AllResearched = true };
        p = Producer(db, Placement(3,226));
        Equal(p.Produce(22), 100, "AllResearched makes all BASE types available", check);
        Equal(p.Produce(26), 100, "AllResearched shops", check);
        Equal(p.Produce(28), 100, "AllResearched sideshows", check);
        Equal(p.Produce(30), 100, "AllResearched features", check);
        Equal(p.Produce(21), 10, "AllResearched variety denominator ten, one built", check);
        Equal(p.Produce(24), 0, "AllResearched availability does NOT fabricate upgrade levels", check);
        Equal(p.Produce(23), 100, "AllResearched L1 built type has zero upgrade potential", check);
        db.File(3,4,2,100);
        Equal(p.Produce(24), 25, "AllResearched still uses actual levels: 2/8, not2/20", check);
        Equal(p.Produce(23), 0, "AllResearched explicit L3 unupgraded built type ->0", check);
        p.Placements = () => new[] {Placement(6,220)};
        Equal(p.VarietyPercent(4), 100, "available track is selected by variety bit4", check);
        Equal(p.VarietyPercent(8), 0, "variety bit8 selects only unbuilt coaster, not built track", check);
        Equal(p.VarietyPercent(0xf), 10, "all-researched built track is one of ten types", check);
    }

    static void DefaultsAndHooks(AssetResourceDatabase dba, Action<bool, string> check)
    {
        var p = Producer(null, Placement(3,226,0,2));
        Vector(p, new[] {50,100,100,100,50,100,50,100,50,100}, "no database quiet defaults", check);
        Equal(p.VarietyPercent(0x3ff), 50, "no-DB direct variety", check);
        Equal(p.ResearchedPercent(0x3ff), 100, "no-DB direct research", check);
        Equal(p.InstalledUpgradePercent(), 100, "no-DB direct installed", check);
        var varietyMasks = new List<int>(); var researchMasks = new List<int>();
        p.Variety = mask => { varietyMasks.Add(mask); return mask switch {0xf => 11,0x100 => 12,0x200 => 13,0xf0 => 14,_ => -1}; };
        p.ResearchPercent = mask => { researchMasks.Add(mask); return mask switch {0xf => 61,0x100 => 62,0x200 => 63,0xf0 => 64,_ => -1}; };
        p.UpgradesInUse = () => 31; p.UpgradeResearchPercentHook = () => 41;
        Vector(p, new[] {11,61,31,41,12,62,13,63,14,64}, "hooks without database", check);
        check(varietyMasks.SequenceEqual(new[] {0xf,0x100,0x200,0xf0}) && researchMasks.SequenceEqual(new[] {0xf,0x100,0x200,0xf0}),
              "producer dispatch passes exact four masks to explicit hooks");
        p.Database = new ResearchDatabase(0,0,dba);
        Vector(p, new[] {11,61,31,41,12,62,13,63,14,64}, "hooks override retail database too", check);
        Equal(p.UpgradeResearchPercent, 41, "upgrade property honors explicit hook", check);
        Equal(p.VarietyPercent(0xf), 0, "direct formula independent of producer hook", check);
        Equal(p.ResearchedPercent(0xf), 40, "direct research independent of producer hook", check);
        Equal(p.InstalledUpgradePercent(), 100, "direct installed independent of producer hook", check);
        p.Variety = null; p.ResearchPercent = null; p.UpgradesInUse = null; p.UpgradeResearchPercentHook = null;
        Vector(p, new[] {0,40,100,0,0,37,0,50,0,23}, "null hooks restore real formulas", check);

        // Minimal synthetic geometry ONLY to construct normal staff/visitor owners. No movement,
        // scripts, hire, or park update: this is a database-ownership test, not gameplay evidence.
        var paths = OwnershipPaths(); var clock = new ParkClock(); var sim = new ParkSim(paths);
        var visitors = new ParkVisitors(sim, new GuestWalk(paths), () => 0);
        var staff = new ParkStaff(visitors, clock, new NativeActivationSequence(0, "research ownership fixture"), _ => 0);
        staff.Research.Database = new ResearchDatabase(0,0,dba);
        p = new AdvisorProducers(clock, sim, staff, visitors) { Placements = () => Array.Empty<AdvisorPlacement>() };
        Vector(p, new[] {0,40,100,0,0,37,0,50,0,23}, "staff research database fallback", check);
        p.Database = new ResearchDatabase(1,2,dba);
        Equal(p.Produce(22), 100, "explicit database wins over staff fallback", check);
        p.Database = null;
        Equal(p.Produce(22), 40, "null explicit database restores staff fallback", check);
        staff.Research.Database = new ResearchDatabase(3,0,dba);
        Equal(p.Produce(22), 20, "existing producer observes replacement staff database live", check);
    }

    static ParkPaths OwnershipPaths()
    {
        var bytes = new byte[0x200];
        void U(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at,4), value);
        U(0, Model.Magic); U(0x44,0x50); U(0x5c,1); U(0x60,1); // one-cell field, no authored geometry
        foreach (int at in new[] {0x110,0x124,0x138,0x14c}) U(at,0x3f800000); // marker identity matrix
        var model = new Model(bytes);
        model.Meshes.Add(new Model.Mesh { Name = "heightfield", Offset = 0x100,
            Local = Matrix4x4.Identity, BoundsMin = Vector3.Zero, BoundsMax = new Vector3(1.004f,0,1.004f) });
        return new ParkPaths(model);
    }
}
