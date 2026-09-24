using System;
using System.Collections.Generic;
using System.Linq;

namespace TPW.PS2.Data;

/// <summary>⭐⭐ THE JOIN FROM AN AUTHORED `.sam` TO THE RECORD THE GAME ACTUALLY LOADS.
///
/// The two disagree. `/Shops/Balloon/Balloon.sam` authors `HappinessEffect 15` and every compiled
/// row for that shop reads **10**; astraclaw hit the same split from the other side on sideshow
/// win percentage (a uniform authored 75 against a compiled 33). So a port that reads the text
/// files is right about what was WRITTEN and can be wrong about what RUNS.
///
/// ⭐⭐ JOINED ON IDENTITY, NEVER ON VALUES. Matching a `.sam` to the DBA row with the same
/// numbers would be circular -- it could only ever find the rows that already agree, and would
/// silently drop precisely the disagreements this exists to surface. The identity is the one
/// `findings/advisor.md` established from a traced consumer: payload `+4` is a localised
/// asset-name row, and every payload's row resolves to a `STR_GRAPHICS_<WORLD>_<PATH>` key --
/// which is exactly what <see cref="TextDatabase.GraphicsKey"/> builds from a wad path.
///
/// ⚠ FIRST MATCH WINS, because the retail lookup does: `arsdb` carries duplicate keys
/// (`FFFFFFFF` twice) and `0x10f248` returns the first. A dictionary that kept the last would
/// change an asset's identity while preserving the count of distinct keys.
///
/// ⚠ REGION MATTERS AND IS NOT GUESSED HERE. `arsdb` and `arsjapdb` are byte-identical;
/// `arsusdb` differs in exactly eight bytes -- four Ice Cream Shop records, `+0x32` 25 to 15 and
/// `+0x36` 10 to 15. The caller passes the archive it wants; this does not choose one.</summary>
public sealed class CompiledAssets
{
    readonly Dictionary<string, AssetResourceDatabase.Entry> _byIdentity = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Identities that appeared more than once. ⚠ Kept rather than dropped: an ambiguous
    /// join is a different failure from a missing one and wants a different fix.</summary>
    public IReadOnlyCollection<string> Ambiguous => _ambiguous;
    readonly List<string> _ambiguous = new();

    public int Count => _byIdentity.Count;

    public CompiledAssets(AssetResourceDatabase database, TextDatabase text)
    {
        if (database == null || text == null) return;
        foreach (var entry in database.Entries)
        {
            if (entry.TextRow >= (uint)text.Keys.Length) continue;
            string identity = text.Keys[entry.TextRow];
            if (string.IsNullOrEmpty(identity)) continue;
            // First match wins -- see the class note.
            if (!_byIdentity.TryAdd(identity, entry)) _ambiguous.Add(identity);
        }
    }

    /// <summary>The compiled record for a `.sam` at <paramref name="pathInWad"/> in
    /// <paramref name="world"/>, or null when the disc has none under that identity.
    /// ⚠ Null is a REPORTABLE outcome, not a reason to fall back silently -- the caller decides,
    /// and <see cref="Report"/> exists so a missing join is counted rather than absorbed.</summary>
    public AssetResourceDatabase.Entry For(string world, string pathInWad)
        => _byIdentity.TryGetValue(TextDatabase.GraphicsKey(world, pathInWad), out var e) ? e : null;

    /// <summary>What the join actually achieved over a set of `.sam` paths, in one line.
    /// ⭐ Coverage is N-of-M or it is nothing: "it worked" about a join that silently missed half
    /// the disc is the failure this reports rather than hides.</summary>
    public string Report(string world, IEnumerable<string> paths)
    {
        var all = paths.ToArray();
        var hit = all.Where(p => For(world, p) != null).ToArray();
        var missed = all.Except(hit).ToArray();
        return $"{world}: {hit.Length} of {all.Length} .sam joined to a compiled record"
             + (_ambiguous.Count == 0 ? "" : $"; {_ambiguous.Count} ambiguous identities")
             + (missed.Length == 0 ? "" : $"; MISSED {string.Join(" ", missed.Take(6))}"
                                        + (missed.Length > 6 ? $" +{missed.Length - 6} more" : ""));
    }
}
