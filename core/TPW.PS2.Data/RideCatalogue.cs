namespace TPW.PS2.Data;

/// <summary>A `.sam` ride description -- the game's own design data, shipped as plain text. See
/// findings/formats.md. 321 files on the disc, every one fully printable, 309 carrying a name.
///
/// The grammar, measured rather than assumed:
/// <list type="bullet">
/// <item>`#` opens a comment line.</item>
/// <item>`key` whitespace `value`, and then OPTIONALLY MORE TEXT. `UsageInfo.MinCapacity\t\t\t1\t\tper
/// car` is a real line: the value is the first token and the rest is the author talking to
/// themselves. Reading the whole remainder as the value breaks 25 `InitCapacity` lines.</item>
/// <item>A quoted value may contain spaces -- `Info.Name  "Mole Whack"`.</item>
/// <item>A key alone on its line introduces an ASCII-art block fenced by `---`, which is how
/// `Info.Shape` and `Info.Hoarding` are drawn.</item>
/// <item>⚠ A key CAN REPEAT inside one file -- 14 of the 321 do it, `ices.sam` giving all four
/// stand positions twice. Last one wins, which is what a line-ordered config means by an
/// override, and it is a decision rather than a discovery: nothing in the file says so.</item>
/// </list>
/// </summary>
public sealed class RideDefinition
{
    /// <summary>Every key/value in the file, last-wins. The long tail -- coaster cross-sections,
    /// pylon controls, car types -- is 434 keys across the corpus and is left here rather than
    /// modelled, because most of it is indexed arrays only the track builder will want.</summary>
    public readonly Dictionary<string, string> Fields = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The `---` fenced ASCII-art blocks, by key. `Info.Shape` is the footprint, one
    /// string per row, `*` a cell and `2` the entrance; `Info.Hoarding` is the sign.</summary>
    public readonly Dictionary<string, string[]> Blocks = new(StringComparer.OrdinalIgnoreCase);

    public string Source = "";

    /// <summary>The ride's model inside its WAD, or null when it has none. A ride is a DIRECTORY
    /// BUNDLE -- `bfbin/` holds `bfbin.sam`, `bfbin.mps`, `bfbin.aps` and `bfbin.rse` -- so the
    /// model is found beside the definition rather than named by it.</summary>
    public string? ModelPath;
    /// <summary>The ride's animation, same rule.</summary>
    public string? AnimationPath;
    /// <summary>Several `.mps` sat in the directory and none matched the stem, so no single model
    /// is the ride's. 12 of 321 -- kept as a stated ambiguity rather than resolved by picking one.</summary>
    public bool ModelAmbiguous;

    public string? Name => Fields.TryGetValue("Info.Name", out var v) ? v : null;

    /// <summary>The `Engine*Override` fields of a fixed feature's `.sam`. `Gates.sam` ships once
    /// per world and the numbers differ per park:
    ///   JUNGLE 45,16 6x3 · SPACE 45,15 6x4 · HALLOW 45,16 6x3 · FANTASY 45,16 6x5
    ///
    /// ⚠⚠ **THE FOOTPRINT IS A NO-BUILD ZONE, NOT THE FEATURE'S SIZE, AND THE MAP OFFSET IS NOT
    /// WHERE THE MESH GOES.** Master, after I shipped the opposite: "the footprint height/width
    /// is the width of the no-build zone the gate creates around it", and the gate "sits in the
    /// sea instead of at the entrance".
    ///
    /// ⚠⚠ AND THE "VALIDATION" THAT LET IT SHIP WAS VACUOUS. Fantasy's `gatebase01` pad centres
    /// at z 21.00 and its .sam gives `MapOffsetY + FootprintHeight` = 16 + 5 = 21, so I called
    /// the formula proven. That is ONE number agreeing ONCE, with no second case able to
    /// disagree -- n = 1 is not a control, it is a coincidence with a rationalisation attached.
    /// The semantics were never checked either: a radius has no business being added to a
    /// coordinate however well the arithmetic lands, and a jump from a -2.29 bias to ~19 should
    /// have been the tell by itself.
    ///
    /// These are exposed because they are real fields worth having. **Nothing places a mesh from
    /// them.** What the map offset IS in, and which frame, is unread.</summary>
    public int? MapOffsetX => Int("Info.EngineMapOffsetOverrideX");
    public int? MapOffsetY => Int("Info.EngineMapOffsetOverrideY");
    /// <summary>⚠ The NO-BUILD ZONE around the feature, per master -- not its own extent.</summary>
    public int? NoBuildWidthOverride => Int("Info.EngineFootprintWidthOverride");
    /// <summary>⚠ The NO-BUILD ZONE around the feature, per master -- not its own extent.</summary>
    public int? NoBuildHeightOverride => Int("Info.EngineFootprintHeightOverride");

    /// <summary>⚠ "this is a fixed item whose animation should be played relative to world (0,0),
    /// not the object pos" -- the .sam's own words. Exposed and UNUSED.</summary>
    public bool DontApplyOffset => Int("Info.DontApplyOffset") == 1;
    public string[]? Shape => Blocks.TryGetValue("Info.Shape", out var v) ? v : null;
    public string[]? Hoarding => Blocks.TryGetValue("Info.Hoarding", out var v) ? v : null;

    public int? Int(string key) =>
        Fields.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : null;

    public float? Float(string key) =>
        Fields.TryGetValue(key, out var v) &&
        float.TryParse(v, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : null;

    public int? Id => Int("Info.Id");

    /// <summary>The world a ride belongs to, from the thousands digit of its id.
    /// <c>1</c> JUNGLE, <c>2</c> HALLOW, <c>3</c> SPACE, <c>4</c> FANTASY. Measured over all 305
    /// rides carrying a numeric id: bands 1-4 are **pure**, each appearing in exactly one WAD and
    /// no other -- 69, 79, 72 and 70 rides respectively.
    ///
    /// ⚠ Band <c>5</c> is NOT a world. Its 15 rides are the sideshows and they appear in all four
    /// WADs, so a 5xxx ride's world is the archive it was found in, not its id. `Gopher Whack` is
    /// 5303 in JUNGLE and `Mole Whack` is 5308 in FANTASY: even the shared category gets a distinct
    /// id and name per world.</summary>
    public int? IdBand => Id / 1000;
    public bool IsSideshow => IdBand == 5;
    public int? ExcitementLevel => Int("UsageInfo.ExcitementLevel");
    public int? MinCapacity => Int("UsageInfo.MinCapacity");
    public int? MaxCapacity => Int("UsageInfo.MaxCapacity");
    public int? PricePerUse => Int("UsageInfo.InitPricePerUse");
    public int? CostOfGoods => Int("UsageInfo.InitCostOfGoods");
    public int? ResearchGroup => Int("Research.Group");

    /// <summary>Capacity at upgrade tier <paramref name="tier"/> (0, 1, 2). The PSX port reads the
    /// same three numbers out of the PSX executable; they agree to the digit on every flat ride and
    /// differ on every tracked one -- see findings/formats.md before copying them anywhere.</summary>
    public int? UpgradeCapacity(int tier) => Int($"Upgrades[{tier}].InitCapacity");
    public int? UpgradeCost(int tier) => Int($"Upgrades[{tier}].CostOfUpgrade");
    public int? ResearchCost(int tier) => Int($"Upgrades[{tier}].CostOfResearch");

    static readonly char[] Ws = { ' ', '\t' };

    public static RideDefinition Parse(string text, string source = "")
    {
        var r = new RideDefinition { Source = source };
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            int k = 0;
            while (k < line.Length && (char.IsLetterOrDigit(line[k]) || line[k] is '_' or '.' or '[' or ']')) k++;
            if (k == 0) continue;
            var key = line[..k];
            var rest = line[k..].Trim();

            if (rest.Length == 0)
            {
                // A bare key introduces a `---` fenced block. If no fence follows it is a flag.
                if (i + 1 < lines.Length && lines[i + 1].Trim() == "---")
                {
                    var rows = new List<string>();
                    int j = i + 2;
                    while (j < lines.Length && lines[j].Trim() != "---") { rows.Add(lines[j].TrimEnd()); j++; }
                    r.Blocks[key] = rows.ToArray();
                    i = j;
                }
                else r.Fields[key] = "";
                continue;
            }

            if (rest[0] == '"')
            {
                int end = rest.IndexOf('"', 1);
                r.Fields[key] = end > 0 ? rest[1..end] : rest.Trim('"');
            }
            else
            {
                // First token only -- everything after it is a trailing comment.
                int sp = rest.IndexOfAny(Ws);
                r.Fields[key] = (sp < 0 ? rest : rest[..sp]).TrimEnd(',');
            }
        }
        return r;
    }
}

/// <summary>Every ride on a disc, keyed by `Info.Id`.
///
/// ⚠ **The id is the identity, not the name.** 309 of the 321 `.sam` files carry a name, and those
/// 309 hold only **211 distinct names** but **301 distinct ids**. 36 names repeat across worlds and
/// **32 of those 36 carry a different id in every copy** -- `Litter Bin` is 4413 in FANTASY, 2411
/// in HALLOW, 1406 in JUNGLE and 3415 in SPACE. Keying a catalogue by name silently merges four
/// differently-priced rides into one.</summary>
public sealed class RideCatalogue
{
    public readonly List<RideDefinition> All = new();
    public readonly Dictionary<int, RideDefinition> ById = new();

    /// <summary>Rides that carry no `Info.Id`, kept rather than dropped. A catalogue that quietly
    /// discards its awkward rows is how a count comes out looking clean.</summary>
    public readonly List<RideDefinition> Unnumbered = new();

    /// <summary>Rides whose id was already taken. `ById` keeps the last, so without this list the
    /// dictionary would eat them and the only trace would be a distinct-id count a few short of
    /// the numbered count -- exactly the kind of quiet shortfall worth printing.</summary>
    public readonly List<RideDefinition> IdCollisions = new();

    /// <summary>Every ride on the disc. ⚠ This reads all 16 WADs -- roughly 600 MB of sector
    /// reads -- so a caller that only needs one archive should use <see cref="AddWad"/> against a
    /// <see cref="WadArchive"/> it already has open instead of paying for the other fifteen.</summary>
    public static RideCatalogue Load(Disc disc)
    {
        var cat = new RideCatalogue();
        foreach (var w in disc.Files())
        {
            if (w.IsDirectory || !w.Path.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase)) continue;
            WadArchive wad;
            try { wad = new WadArchive(disc.Read(w.Extent, w.Size)); } catch { continue; }
            cat.AddWad(wad, w.Path);
        }
        return cat;
    }

    /// <summary>Add one already-open archive's rides. The index it builds is per-WAD, which is why
    /// <see cref="Resolve"/> can look models up without rescanning every entry per ride.</summary>
    public void AddWad(WadArchive wad, string wadPath)
    {
        // ⚠ Built once per archive. Resolving each ride by scanning wad.Entries was 321 rides x 2
        // lookups x ~14,675 entries, which is why opening the viewer took minutes rather than
        // seconds -- an O(rides x entries) walk hidden behind a method that reads like a lookup.
        var byDir = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in wad.Entries)
        {
            if (WadArchive.IsAlias(e)) continue;
            int slash = e.Path.LastIndexOf('/');
            var dir = slash < 0 ? "" : e.Path[..(slash + 1)];
            if (!byDir.TryGetValue(dir, out var list)) byDir[dir] = list = new List<string>();
            list.Add(e.Path);
        }
        foreach (var e in wad.Entries)
        {
            if (WadArchive.IsAlias(e)) continue;
            if (!e.Path.EndsWith(".sam", StringComparison.OrdinalIgnoreCase)) continue;
            byte[] data;
            try { data = wad.Read(e); } catch { continue; }
            var def = RideDefinition.Parse(System.Text.Encoding.Latin1.GetString(data), wadPath + e.Path);
            Resolve(def, byDir, e.Path);
            All.Add(def);
            if (def.Id is not int id) { Unnumbered.Add(def); continue; }
            if (ById.ContainsKey(id)) IdCollisions.Add(def);
            ById[id] = def;
        }
    }

    /// <summary>Find a ride's model and animation, which sit beside its `.sam` rather than being
    /// named by it. Measured over all 321 definitions:
    /// <list type="bullet">
    /// <item><b>287</b> have a `.mps` whose stem matches the `.sam` exactly.</item>
    /// <item><b>1</b> has a single `.mps` under a different stem, taken as the ride's.</item>
    /// <item><b>12</b> have several and no stem match -- left ambiguous on purpose. Picking the
    /// first would give a ride the wrong body and nothing would say so.</item>
    /// <item><b>21</b> have no model at all. These are script-only entities -- `bus`, `end`,
    /// `sign1`, `firepit` -- carrying an `.rse` and no geometry, so a missing model is a kind of
    /// ride rather than a failure to find one.</item>
    /// </list>
    /// ⚠ Directory case is not reliable: the same bundle appears as `/Features/bus` and
    /// `/features/bus`. Compare paths case-insensitively or you will find one of the two.</summary>
    static void Resolve(RideDefinition def, Dictionary<string, List<string>> byDir, string samPath)
    {
        int slash = samPath.LastIndexOf('/');
        var dir = slash < 0 ? "" : samPath[..(slash + 1)];
        var stem = Path.GetFileNameWithoutExtension(samPath);

        if (!byDir.TryGetValue(dir, out var siblings)) siblings = new List<string>();

        string? Pick(string ext)
        {
            var inDir = siblings
                .Where(p => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var exact = inDir.FirstOrDefault(
                p => string.Equals(Path.GetFileNameWithoutExtension(p), stem, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            if (inDir.Count == 1) return inDir[0];
            if (inDir.Count > 1 && ext == ".mps") def.ModelAmbiguous = true;
            return null;
        }

        def.ModelPath = Pick(".mps");
        def.AnimationPath = Pick(".aps");
    }
}
