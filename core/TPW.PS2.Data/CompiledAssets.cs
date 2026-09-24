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

    /// <summary>⭐⭐ ATTACH THE COMPILED RECORD TO EVERY SHOP DEFINITION, and this is the ONE
    /// implementation -- the viewer calls it and so does the audit.
    ///
    /// ⚠⚠ IT EXISTS BECAUSE THE TWO DIVERGED ONCE. The viewer had its own copy of this loop and
    /// ran it BEFORE the catalogue was populated, so it walked an empty list and attached
    /// nothing; the checks stayed green because they exercised the lookup helper instead of the
    /// wiring. A check that tests a different implementation than the one that ships is not a
    /// check of anything. astraclaw found it.
    ///
    /// ⚠ The `.sam` source is disc-absolute (`/DATA/JUNGLE.WAD/Shops/...`) and the identity wants
    /// world plus WAD-RELATIVE path, so the split happens here rather than at each call site.</summary>
    public int Attach(IEnumerable<RideDefinition> definitions, out string report)
    {
        var shops = definitions.Where(d => d.ShopType != null).ToArray();
        int attached = 0;
        var missed = new List<string>();
        foreach (var d in shops)
        {
            var (world, path) = Split(d.Source);
            if (path == null) { missed.Add(d.Source); continue; }
            var entry = For(world, path);
            if (entry?.Shop is { } shop) { d.Compiled = shop; d.CompiledEntry = entry; attached++; }
            else missed.Add(d.Source);
        }
        // ⚠ "0 joined, 0 missed" is exactly what an EMPTY input looks like and it reads as
        // success. Name that case, so an ordering bug cannot hide inside a tidy line again.
        report = shops.Length == 0
            ? "NOTHING TO JOIN -- no definition declared a shop block"
            : $"{attached} of {shops.Length} shops joined"
              + (missed.Count == 0 ? "" : $"; MISSED {string.Join(" ", missed.Take(4))}");
        return attached;
    }

    /// <summary>World and WAD-relative path out of a definition's `Source`.
    ///
    /// ⚠⚠ TWO SHAPES REACH HERE AND ONLY ONE WAS HANDLED. `RideCatalogue.AddWad` stamps whatever
    /// `wadPath` it is given: the viewer passes `/DATA/JUNGLE.WAD` and gets
    /// `/DATA/JUNGLE.WAD/Shops/...`, while a caller passing a bare world name gets
    /// `JUNGLE/Shops/...`. The first version looked for an element ending `.WAD` and silently
    /// missed EVERY definition under the second, reporting "0 of 8 joined" -- found the moment a
    /// check ran through the real catalogue instead of the lookup helper.</summary>
    static (string World, string Path) Split(string source)
    {
        var p = (source ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        int wi = Array.FindIndex(p, x => x.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase));
        if (wi >= 0) return (p[wi][..^4], string.Join('/', p.Skip(wi + 1)));
        // No archive element: the first component is the world, as a bare name leaves it.
        return p.Length >= 2 ? (p[0], string.Join('/', p.Skip(1))) : (null, null);
    }

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
