namespace TPW.PS2.Data;

/// <summary>⭐⭐ THE RESEARCH STATE -- the console's 60 records at `0x389650`, `{u8 cat, u8 item, u8 percent, u8 level}`
/// (findings/research.md §1.3, READ against the MIPS of every routine named here). One per park: the console wipes the
/// list to `{FF, FF, -, 0}` whenever the asset-db singleton is recreated (`0x12A550`), which park teardown forces
/// (`0x150E80 -> 0x12A5B8`), and records are made lazily by any query.
///
/// ⭐ NOTHING SEEDS IT. What a park starts with is exactly the items whose tier-0 research GROUP is 0: the first question
/// anything asks about one auto-marks it researched (<see cref="Percent"/>). Everything else is locked until a
/// researcher finishes it.
///
/// ⭐ LEVEL is how many tiers are researched, and tier t is available when `level &gt; t`. A ride kind (1, 3, 6, 7) runs
/// 0 locked -&gt; 1 the ride -&gt; 2 its first upgrade -&gt; 3 its second; a simple kind (2, 4, 5, 8) needs level 1.
/// The unlock is the `level++` inside <see cref="File"/>, when a filed percent reaches 100.</summary>
public sealed class ResearchDatabase
{
    /// <summary>The console's hard cap; retail data needs at most 43 (HALLOW 1). A 61st item would be written through a
    /// null pointer on the console; here it throws.</summary>
    public const int Capacity = 60;

    sealed class Record { public int Cat, Item, Percent, Level; }
    readonly List<Record> _records = new();
    readonly AssetResourceDatabase _dba;

    public int World { get; }
    public int Park { get; }

    /// <summary>`[0x2B3070]`, the debug config key "AllResearched" (`0x12BE30`), and `[0x2B72A8]`, the Rollercoaster Test
    /// Park flag: either makes every item available (`0x12B6D0`). The port exposes the first as `--all-researched`.</summary>
    public bool AllResearched { get; set; }

    public ResearchDatabase(int world, int park, AssetResourceDatabase dba)
    {
        World = world; Park = park; _dba = dba;
    }

    /// <summary>The catalogue list for a kind in this park (<see cref="ResearchCatalogue"/>).</summary>
    public IReadOnlyList<uint> Keys(int cat) => ResearchCatalogue.Keys(World, Park, (AssetResourceDatabase.AssetKind)cat);
    public int Count(int cat) => Keys(cat).Count;

    AssetResourceDatabase.Entry Entry(int cat, int item)
        => item >= 0 && Keys(cat) is var keys && item < keys.Count ? _dba?.Find(keys[item]) : null;

    static bool IsRideKind(int cat) => cat is 1 or 3 or 6 or 7;

    /// <summary>`0x12B758`: the research GROUP of a tier -- `Tier(t)` (`+0x48 + 0x34t`) on a ride kind, the simple
    /// economy's (`+0x28`, tier ignored) otherwise. ⚠ A ride's tier 3 reads past its third tier on the console (+0xEC,
    /// non-zero on every retail ride), so it answers non-zero here. A key with no record answers 0.</summary>
    public int Group(int cat, int item, int tier)
    {
        if (Entry(cat, item) is not { } e) return 0;
        if (IsRideKind(cat) && e.HasRideTiers) return tier >= 3 ? 1 : e.Tier(tier).ResearchGroup;
        return e.SimpleEconomy?.ResearchGroup ?? 0;
    }

    /// <summary>`0x12B840`: the WORK a tier takes, 0 when it is already available.</summary>
    public int Work(int cat, int item, int tier)
    {
        if (Available(cat, item, tier) || Entry(cat, item) is not { } e) return 0;
        if (IsRideKind(cat) && e.HasRideTiers) return tier >= 3 ? 0 : e.Tier(tier).ResearchWork;
        return e.SimpleEconomy?.ResearchWork ?? 0;
    }

    /// <summary>`0x12BEE8` find, else `0x12BF38` alloc: a new record is `{cat, item, 0, 0}` (the wipe left level 0).</summary>
    Record Get(int cat, int item)
    {
        foreach (var r in _records) if (r.Cat == cat && r.Item == item) return r;
        if (_records.Count >= Capacity) throw new InvalidOperationException($"research database full ({Capacity}) at {cat}/{item}");
        var made = new Record { Cat = cat, Item = item };
        _records.Add(made);
        return made;
    }

    /// <summary>⭐ `0x12BAF8(c, i, level, pct)`: a percent of 100 or more IS THE UNLOCK -- level + 1, percent 0
    /// (`slti 0x64`); then the record takes them only if that does not lower its level (`slt`).</summary>
    public void File(int cat, int item, int level, int percent)
    {
        var r = Get(cat, item);
        if (percent >= 100) { level += 1; percent = 0; }
        if (level >= r.Level) { r.Percent = percent; r.Level = level; }
    }

    /// <summary>`0x12B928`: 100 for a researched tier; the filed percent while a tier with a group is in progress; and
    /// a group-0 tier is AUTO-MARKED researched (`0x12BAD8` = File(.., tier, 100)) the first time it is asked about.</summary>
    public int Percent(int cat, int item, int tier)
    {
        int g = Group(cat, item, tier);
        var r = Get(cat, item);
        if (tier < r.Level) return 100;
        if (g != 0) return r.Percent;
        File(cat, item, tier, 100);
        return 100;
    }

    /// <summary>⭐ `0x12B6D0`, what every consumer asks: the debug key, or the tier is researched.</summary>
    public bool Available(int cat, int item, int tier) => AllResearched || Percent(cat, item, tier) == 100;

    /// <summary>`0x12BA08`: the item's level, first promoted across any group-0 tiers (a simple kind of group 0 climbs to
    /// 3, which nothing reads beyond "&gt; 0").</summary>
    public int Level(int cat, int item)
    {
        while (true)
        {
            int l = Get(cat, item).Level;
            if (l >= 3 || Group(cat, item, l) != 0) return l;
            File(cat, item, l, 100);
        }
    }

    /// <summary>`0x104BD0`: the upgrade research %. Over the ride kinds, every item with a level: total += 2, done +=
    /// level - 1; `done &lt; total ? done x 100 / total : 100`.</summary>
    public int UpgradePercent()
    {
        int total = 0, done = 0;
        foreach (int cat in new[] { 3, 6, 7, 1 })
            for (int i = 0; i < Count(cat); i++)
                if (Level(cat, i) is var l && l != 0) { total += 2; done += l - 1; }
        return done < total ? done * 100 / total : 100;
    }

    /// <summary>`0x104358(0xFFFF)`: anything of kinds 3, 7, 6, 1, 2, 4, 5 not yet available at tier 0, or upgrade
    /// research under 100 %. ⚠ Add-ons (kind 8) are not counted, so "everything researched" can sound with them locked.</summary>
    public bool AnythingLeft()
    {
        foreach (int cat in new[] { 3, 7, 6, 1, 2, 4, 5 })
            for (int i = 0; i < Count(cat); i++)
                if (!Available(cat, i, 0)) return true;
        return UpgradePercent() < 100;
    }
}
