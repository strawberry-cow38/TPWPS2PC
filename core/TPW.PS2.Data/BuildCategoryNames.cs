namespace TPW.PS2.Data;

using Kind = AssetResourceDatabase.AssetKind;

/// <summary>⭐⭐ THE CONSOLE'S OWN BUILD CATEGORIES -- its classification AND its names.
///
/// The names are the `STR_PURCHASE_*` family in the localisation database, and they read exactly
/// as master asked for them: "Rides", "Track Rides", "Roller Coasters", "Shops", "Sideshows",
/// "Features". The same family gives the Build screen's own row labels (780 Purchase Cost, 375
/// Balance, 969 No. Owned), which were decoded separately from `FUN_00198b48` and agree.
///
/// ⭐⭐ AND THE MEMBERSHIP IS THE COMPILED RECORD'S, not the archive's folders.
/// `AssetResourceDatabase.AssetKind` already separates `TrackRide` (6) from `TourRide` (7) from
/// `Ride` (3) -- the game says what each thing IS, so nothing here has to infer it. The folders do
/// NOT: they file the karts, the water ride and the tour ride under `Rides/` with everything else,
/// which is why a folder-based split could never have produced master's six.
///
/// ⚠ I had gone looking in the `.sam` fields for a discriminator and found none that split them
/// (`Bumper.WhichTrackType` is 1 on Dino Karts and 0 on Splish Splash; the `SupplementalMeshes`
/// count makes The Hot Pot look more like a track ride than Jurassic Tours), and was about to ask
/// for a Ghidra pass. Tinyclaw pointed at the compiled record, which already carried it. The
/// lesson is the cheaper one: ask what the data ALREADY says before deciding it is not there.</summary>
public static class BuildCategoryNames
{
    /// <summary>⭐ The console's kinds, its string id for each, and -- by position -- the order the
    /// build menu lists them in. Master: "build should be ordered Rides, Track rides, Roller
    /// Coasters, Shops, Sideshows, Features", with Tour Rides beside its sibling.</summary>
    public static readonly (Kind Kind, int TextId)[] Order =
    {
        (Kind.Ride,         1024),   // STR_PURCHASE_RIDES           "Rides"
        (Kind.TrackRide,     362),   // STR_PURCHASE_TRACKRIDES      "Track Rides"
        (Kind.TourRide,      811),   // STR_PURCHASE_TOURRIDES       "Tour Rides"
        (Kind.Coaster,       457),   // STR_PURCHASE_COASTER         "Roller Coasters"
        (Kind.Shop,          513),   // STR_PURCHASE_SHOPS           "Shops"
        (Kind.Sideshow,      522),   // STR_PURCHASE_SIDESHOWS       "Sideshows"
        (Kind.Feature,       242),   // STR_PURCHASE_FEATURES        "Features"
        (Kind.TrackUpgrade,  454),   // STR_SINGLER_TRACKUPGRADES    "Addons"
    };

    /// <summary>Where a kind sorts. ⚠⚠ A kind the table does not name sorts AFTER every one it
    /// does, rather than being dropped -- hiding a category would hide buildable things, and an
    /// unnamed kind is a gap in this table, not a thing that should not exist.</summary>
    public static int Rank(Kind k)
    {
        for (int i = 0; i < Order.Length; i++) if (Order[i].Kind == k) return i;
        return Order.Length;
    }

    /// <summary>The console's string id for a kind, or 0 when the table does not name it.</summary>
    public static int TextId(Kind k)
    {
        int r = Rank(k);
        return r < Order.Length ? Order[r].TextId : 0;
    }

    /// <summary>A last-resort English name, for a kind with no string id and for a park whose
    /// text database did not load. ⚠ Never shown when the disc can answer.</summary>
    public static string Fallback(Kind k) => k switch
    {
        Kind.Ride => "Rides",      Kind.TrackRide => "Track Rides", Kind.TourRide => "Tour Rides",
        Kind.Coaster => "Roller Coasters", Kind.Shop => "Shops",    Kind.Sideshow => "Sideshows",
        Kind.Feature => "Features", Kind.TrackUpgrade => "Addons",  _ => k.ToString(),
    };
}
