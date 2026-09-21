namespace TPW.PS2.Data;

/// <summary>The localisation database under `DATA.WAD/Text/translations/`. Layout:
/// <code>u32 count, count x u32 absolute offset, then NUL-terminated strings</code>
///
/// Verified on every table: the first offset is always `4 + count*4`, offsets are monotonic, and
/// the last string ends **exactly** at EOF. Every table holds **1,087** entries in the same order.
///
/// ⚠ There are **three regional trees**, not one -- `eur/`, `jap/` and `usa/`, each with its own
/// language set, and they are not identical: `usa/` ships no `fre.dat` and `jap/` no `ame.dat`.
/// Reading "the" translations directory finds whichever one you happened to name.
///
/// `id.dat` is not a language. It is the symbolic key for each row, and
/// `include/trans.h` ships the same list a third time as a C enum whose ordinal is the index.
///
/// ⚠ **1,066 of the 1,087 `id.dat` entries end in a trailing space.** The key is everything before
/// the first space and the remainder is a printf format spec -- 21 rows carry a non-empty one, and
/// those are exactly the strings that take arguments. Trimming the line loses the distinction
/// between "no arguments" and "not looked at".</summary>
public sealed class TextDatabase
{
    public readonly string Locale;
    /// <summary>Symbolic key per row, from `id.dat`, without its format spec.</summary>
    public readonly string[] Keys;
    /// <summary>The printf spec per row. <c>null</c> means the row had no space at all,
    /// <c>""</c> means it ended in the trailing space that IS the empty spec, and a non-empty
    /// string is a real spec. Collapsing the first two loses the difference between "takes no
    /// arguments" and "was never given a field", which is 1,066 rows against 21.</summary>
    public readonly string?[] Formats;
    /// <summary>language file stem (`eng`, `ger`, ...) -> that language's 1,087 strings.</summary>
    public readonly Dictionary<string, string[]> Languages = new(StringComparer.OrdinalIgnoreCase);

    readonly Dictionary<string, int> _byKey = new(StringComparer.OrdinalIgnoreCase);

    TextDatabase(string locale, string[] keys, string?[] formats)
    {
        Locale = locale; Keys = keys; Formats = formats;
        for (int i = 0; i < keys.Length; i++) _byKey[keys[i]] = i;
    }

    public int IndexOf(string key) => _byKey.TryGetValue(key, out var i) ? i : -1;

    /// <summary>A row in one language, or null if that language or row is absent.</summary>
    public string? Text(string language, int row) =>
        Languages.TryGetValue(language, out var t) && row >= 0 && row < t.Length ? t[row] : null;

    public static string[] ParseTable(byte[] d)
    {
        int n = BitConverter.ToInt32(d, 0);
        var outp = new string[n];
        for (int i = 0; i < n; i++)
        {
            int o = BitConverter.ToInt32(d, 4 + i * 4);
            int e = Array.IndexOf(d, (byte)0, o);
            outp[i] = System.Text.Encoding.Latin1.GetString(d, o, (e < 0 ? d.Length : e) - o);
        }
        return outp;
    }

    /// <summary>Load one regional tree, e.g. `eur`.</summary>
    public static TextDatabase? Load(WadArchive data, string locale)
    {
        var dir = $"/Text/translations/{locale}/";
        byte[]? Read(string stem)
        {
            foreach (var e in data.Entries)
            {
                if (WadArchive.IsAlias(e)) continue;
                if (!e.Path.EndsWith(dir + stem + ".dat", StringComparison.OrdinalIgnoreCase)) continue;
                try { return data.Read(e); } catch { return null; }
            }
            return null;
        }
        var idRaw = Read("id");
        if (idRaw is null) return null;

        var rows = ParseTable(idRaw);
        var keys = new string[rows.Length];
        var fmts = new string?[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            // Split at the FIRST space, never trim: the trailing space is the empty format spec.
            int sp = rows[i].IndexOf(' ');
            keys[i] = sp < 0 ? rows[i] : rows[i][..sp];
            fmts[i] = sp < 0 ? null : rows[i][(sp + 1)..];
        }
        var db = new TextDatabase(locale, keys, fmts);
        foreach (var lang in new[] { "eng", "ame", "fre", "ger", "ita", "spa", "dut", "swe", "jap" })
            if (Read(lang) is byte[] raw) db.Languages[lang] = ParseTable(raw);
        return db;
    }

    /// <summary>The `STR_GRAPHICS_` key for an asset path, e.g. `JUNGLE` +
    /// `Rides/Monkey/Monkey.sam` -> `STR_GRAPHICS_JUNGLE_RIDES_MONKEY_MONKEY`.
    ///
    /// ⚠ This is how a ride reaches its PLAYER-FACING name, and it is not `Info.Name`. Over the
    /// 260 `STR_GRAPHICS` keys that resolve to a `.sam`, **69 disagree with that file's
    /// `Info.Name`** -- `Loudspeaker` is shown as `UFOs`, `Garden Trowel` as `Trowel`,
    /// `Chac Atak` as `Chak Atak`. `Info.Name` is the designer's internal label; the table is what
    /// the player reads. Joining on the full path rather than the stem moves only 2 of the 260, so
    /// those 69 are a real difference and not a mis-join.</summary>
    public static string GraphicsKey(string world, string pathInWad)
    {
        var p = pathInWad.Trim('/');
        int dot = p.LastIndexOf('.');
        if (dot > 0) p = p[..dot];
        return "STR_GRAPHICS_" + world.ToUpperInvariant() + "_" + p.ToUpperInvariant().Replace('/', '_');
    }
}
