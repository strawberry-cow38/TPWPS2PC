using System.Collections.Generic;
using System.Linq;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>⭐⭐ RESEARCH, the console's (findings/research.md): a per-park database of what is researched, the Research
/// screen on which the PLAYER picks every project, and a build menu that offers only this park's catalogue, and only what
/// is researched. Nothing researches on its own: a fresh park's five rows read "Nothing" until a project is chosen, and a
/// researcher does the work.
///
/// ⚠ `--all-researched` (or `TPW_ALL_RESEARCHED=1`) is the console's own debug key "AllResearched" (`0x2B3070`), which
/// makes every item available. ⚠ The port goes one step further under it and drops the per-park catalogue filter too,
/// so the build menu is the whole world library as it was before research landed -- which is what the older harnesses
/// were written against.</summary>
public partial class Viewer
{
    /// <summary>The DBA the compiled records came from (<see cref="AttachCompiledRecords"/>).</summary>
    AssetResourceDatabase _arsDb;
    ResearchDatabase _researchDb;
    bool _allResearched = System.Environment.GetEnvironmentVariable("TPW_ALL_RESEARCHED") == "1";

    /// <summary>This park's research state, made on first use and dropped with the park (MakePathTool), as the console
    /// wipes its list when the asset db is rebuilt for a new park.</summary>
    ResearchDatabase ResearchDb
        => _researchDb ??= _arsDb == null || !ResearchCatalogue.Has(TrackWorld, TrackPark) ? null
           : new ResearchDatabase(TrackWorld, TrackPark, _arsDb) { AllResearched = _allResearched };

    /// <summary>Hands the database and the built-count to the park's research manager.</summary>
    void AttachResearch()
    {
        if (_staff == null) return;
        var mgr = _staff.Research;
        mgr.Database ??= ResearchDb;
        mgr.BuiltCount ??= ResearchBuiltCount;
    }

    /// <summary>A placed thing's catalogue identity (kind, index), or null for one with no compiled record or not listed.</summary>
    (int Cat, int Item)? CatalogueItem(AssetResourceDatabase.AssetKind? kind, uint? key)
    {
        if (kind is not { } k || key is not { } dba) return null;
        int i = ResearchCatalogue.IndexOf(TrackWorld, TrackPark, k, dba);
        return i < 0 ? null : ((int)k, i);
    }

    (int Cat, int Item)? CatalogueItem(ParkRide r)
        => CatalogueItem(r?.Definition?.CompiledEntry?.Kind, r?.Definition?.CompiledEntry?.Key);

    /// <summary>`0x12AC78(cat, item)`: how many of a catalogue item stand in the park.</summary>
    int ResearchBuiltCount(int cat, int item)
        => _sim?.Rides.Count(r => CatalogueItem(r) is { } c && c.Cat == cat && c.Item == item) ?? 0;

    /// <summary>⭐ The build menu's research test (`0x15CA78` with research off, §4.1): an item is offered when this park's
    /// catalogue lists it and `0x12B6D0(kind, index, tier 0)` says it is researched. A row with no compiled record cannot
    /// be looked up and is offered, as before.</summary>
    bool ResearchedHere(AssetLibrary.RideAssets r)
    {
        if (_allResearched || ResearchDb is not { } db) return true;
        if (BuildKind(r) is not { } kind || DbaKey(r) is not uint key) return true;
        int i = ResearchCatalogue.IndexOf(TrackWorld, TrackPark, kind, key);
        return i >= 0 && db.Available((int)kind, i, 0);
    }

    /// <summary>The ride panel's upgrade test (`0x1D4A38`): tier T is offered only while `T &lt; Level` -- the upgrade has been
    /// researched on the Upgrades row.</summary>
    bool UpgradeResearched(ParkRide r, int tier)
        => _allResearched || ResearchDb is not { } db || CatalogueItem(r) is not { } c || tier < db.Level(c.Cat, c.Item);

    /// <summary>A catalogue item's display name: its record's text row (`0x12AE78 -> 0x12B548`).</summary>
    string ResearchName(int cat, int item)
    {
        var keys = ResearchCatalogue.Keys(TrackWorld, TrackPark, (AssetResourceDatabase.AssetKind)cat);
        if (item < 0 || item >= keys.Count || _arsDb?.Find(keys[item]) is not { } e) return $"#{cat}:{item}";
        return TextRow((int)e.TextRow) is { Length: > 0 } t ? t : $"#{keys[item]}";
    }

    // ---- The Research screen's picking (§3.2): mode 0 the rows, mode 2 one row's candidate list ------------------------

    /// <summary>The row being picked for (`this+0x2F4` in mode 2), or -1 in mode 0.</summary>
    int _researchPickRow = -1;
    List<(int Cat, int Item)> _researchPick = new();
    int _researchPickSel;

    /// <summary>The highlighted candidate's text: its name, or "Nothing" past the end -- the entry the list always ends with.</summary>
    string ResearchPickText()
        => _researchPickSel < _researchPick.Count
            ? ResearchName(_researchPick[_researchPickSel].Cat, _researchPick[_researchPickSel].Item)
            : TextRow(LaptopScreen.ResearchNothingTextId);

    /// <summary>⭐ A click on a research row. In mode 0 it opens that row's list (`0x1B5528`): the catalogue items passing
    /// `0x1B7208`, then "Nothing", the cursor on the first. Clicking the row again ACCEPTS: "Nothing" stops the row's
    /// project (`0x1B71D8`); anything else stops it and starts the pick (`0x1B55E8` -> `0x1B6880`). Either way, back to mode 0.</summary>
    void ResearchRowClicked(int row)
    {
        var mgr = _staff?.Research;
        if (mgr == null || row < 0 || row >= ResearchManager.SlotCount) return;
        if (_researchPickRow != row)
        {
            _researchPickRow = row;
            _researchPick = mgr.Candidates(row);
            _researchPickSel = 0;
            ShowLaptopLevel();
            return;
        }
        var pick = _researchPickSel < _researchPick.Count ? _researchPick[_researchPickSel] : ((int, int)?)null;
        mgr.Stop(row);
        string said;
        if (pick is { } p)
        {
            bool started = mgr.StartResearch(row, p.Item1, p.Item2);
            said = started ? $"researching {ResearchName(p.Item1, p.Item2)}" : $"{ResearchName(p.Item1, p.Item2)} cannot be started";
            GD.Print($"[research] row {row}: {said} (slot {mgr.Slots[row]})");
        }
        else said = "research stopped on that row";
        _researchPickRow = -1;
        ShowLaptopLevel();
        Status(said);
    }

    /// <summary>Up/Down (or the wheel) in mode 2: step the candidates, wrapping, only when there is more than one entry.</summary>
    bool ResearchPage(int by)
    {
        if (_laptopBack.Count == 0 || _laptopBack[^1].Kind != "research" || _researchPickRow < 0) return false;
        int n = _researchPick.Count + 1;
        if (n > 1) { _researchPickSel = ((_researchPickSel + by) % n + n) % n; ShowLaptopLevel(); }
        return true;
    }

    /// <summary>Back in mode 2 returns to the rows and STAYS on the screen (`0x1B58A4..0x1B58BC` cancels the leave request).</summary>
    bool ResearchBack()
    {
        if (_laptopBack.Count == 0 || _laptopBack[^1].Kind != "research" || _researchPickRow < 0) return false;
        _researchPickRow = -1;
        ShowLaptopLevel();
        return true;
    }
}
