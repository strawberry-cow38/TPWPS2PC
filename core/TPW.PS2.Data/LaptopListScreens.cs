namespace TPW.PS2.Data;

using Kind = AssetResourceDatabase.AssetKind;

/// <summary>⭐⭐ THE FIVE "ALL ..." LAPTOP SCREENS ARE ONE CLASS FILTERED BY <see cref="Kind"/>.
///
/// The console does not implement these five separately. Each is the same list class -- the base
/// constructed by `FUN_0010c928`, whose vtable is 17 slots and which every one of them inherits
/// without adding a single method of its own beyond the destructor, the per-frame forwarder and
/// the lifecycle hook. What a subclass supplies is this file: a title, and a set of asset kinds.
///
/// ⭐ HOW THE KINDS WERE READ. Each constructor pushes numbers into the list object at `this+0x2f4`
/// through `FUN_0015c710` -- All Rides pushes 3, 7, 6, 1. Those are
/// <see cref="AssetResourceDatabase.AssetKind"/> values, and four of the five screens land on
/// exactly the set their title promises with nothing to interpret. The corroboration is that the
/// same enum was derived from the OTHER end for the build menu -- see
/// <see cref="BuildCategoryNames"/>, which got it from the `STR_PURCHASE_*` names and master's
/// requested ordering. Two routes, one table.
///
/// ⭐ THE TITLE is the id the constructor hands to `FUN_00165948(this+0xa0, id)`, resolved through
/// `Text/translations/eur/id.dat`. That one call names every screen class in the executable.
///
/// ⭐⭐ WHAT `FUN_0015c710` ACTUALLY DOES, and it names the All Toilets filter. It is not a
/// "register a tab" call: it walks the world's object list from `FUN_0014da50`, asks each object
/// its kind through a virtual at `vtable+0xa4`, and appends every match to the screen's list. So
/// the number IS a kind, and the call POPULATES.
///
/// ⚠⚠ AND IT CARRIES ONE SPECIAL CASE, FOR KIND 2 ONLY. When the kind is `Feature`, it asks the
/// object a second question -- a virtual at `vtable+0x134` -- and SKIPS it when that returns zero.
/// That is the extra narrowing All Toilets needs, found by reading the populate function rather
/// than assumed away: toilets are Features that answer that predicate. <see cref="Toilets"/> is
/// still <c>Complete: false</c> because the predicate itself is not decoded -- what it asks is
/// unread -- but it is no longer an unexplained gap, it is one named virtual.
///
/// ⚠ ALL STAFF REGISTERS NO KIND AT ALL. Staff are not assets, so no <see cref="Kind"/> applies and
/// its list comes from somewhere else. Its entry carries an empty set, which is the truth, not a
/// gap to be filled in with a plausible kind.
///
/// ⚠ BAR COUNTS COME FROM THE CONSTRUCTOR, NOT THE DRAW. `FUN_00115468` constructs a bar and
/// `FUN_001da630` a slider, and counting those per constructor is checkable against the `.sce`.
/// Counting them in the DRAW does not work for these screens: `main_i_ride`'s scene declares three
/// bars and its draw calls the bar function zero times, because the base draws them.
///
/// ⚠⚠ AND A SCENE ELEMENT IS NOT ALWAYS ONE WIDGET. `main_i_staff` builds THREE bars against a
/// single element named `infobars`. So <see cref="Bars"/> is the constructor's count, which is the
/// number of widgets; the scene's element is a region to place them in.</summary>
public sealed record LaptopListScreen(
    string SceneFile,
    int MenuId,
    int TitleTextId,
    Kind[] Kinds,
    int Bars,
    bool Complete = true)
{
    /// <summary>⭐ All Rides: every ride kind there is -- the constructor pushes 3, 7, 6, 1, which is
    /// Ride, TourRide, TrackRide and Coaster. Three bars (Excitement, State of Repair, Remaining
    /// Life), matching its scene exactly.</summary>
    public static readonly LaptopListScreen Rides = new(
        "main_i_ride.sce", 13, 432, new[] { Kind.Ride, Kind.TourRide, Kind.TrackRide, Kind.Coaster }, 3);

    /// <summary>All Shops: kind 4. One bar (satisfaction), matching its scene.</summary>
    public static readonly LaptopListScreen Shops = new(
        "main_i_shop.sce", 16, 419, new[] { Kind.Shop }, 1);

    /// <summary>All Sideshows: kind 5. Two bars (excitement, satisfaction), matching its scene.</summary>
    public static readonly LaptopListScreen Sideshows = new(
        "main_i_sideshow.sce", 20, 184, new[] { Kind.Sideshow }, 2);

    /// <summary>⚠ All Staff: NO kind is registered, because staff are not assets. Three bars against
    /// one scene element named `infobars`. Its source list is not decoded, hence not Complete.</summary>
    public static readonly LaptopListScreen Staff = new(
        "main_i_staff.sce", 15, 999, System.Array.Empty<Kind>(), 3, Complete: false);

    /// <summary>⚠ All Toilets: registers `Feature`, which also covers bins and benches. The
    /// populate call narrows it with a per-object predicate applied to kind 2 alone
    /// (`vtable+0x134`); that predicate is not decoded, so this stays incomplete. One bar
    /// (cleanliness).</summary>
    public static readonly LaptopListScreen Toilets = new(
        "main_i_bathroom.sce", 26, 848, new[] { Kind.Feature }, 1, Complete: false);

    public static readonly LaptopListScreen[] All = { Rides, Shops, Sideshows, Staff, Toilets };

    /// <summary>Does this screen list that kind? ⚠ False for every kind when the screen registers
    /// none, which is All Staff -- a caller must not read "no kinds" as "all kinds".</summary>
    public bool Lists(Kind k) => System.Array.IndexOf(Kinds, k) >= 0;
}
