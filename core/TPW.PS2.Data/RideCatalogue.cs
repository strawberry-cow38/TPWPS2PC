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

    public string? Name => Fields.TryGetValue("Info.Name", out var v) ? v : null;
    public string[]? Shape => Blocks.TryGetValue("Info.Shape", out var v) ? v : null;
    public string[]? Hoarding => Blocks.TryGetValue("Info.Hoarding", out var v) ? v : null;

    public int? Int(string key) =>
        Fields.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : null;

    public float? Float(string key) =>
        Fields.TryGetValue(key, out var v) &&
        float.TryParse(v, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : null;

    public int? Id => Int("Info.Id");
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
