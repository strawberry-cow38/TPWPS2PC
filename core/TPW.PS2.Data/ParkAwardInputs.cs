namespace TPW.PS2.Data;

/// <summary>A placed object's footprint as `0x152FB0` reads it: cell origin (`vt+0x74`, two shorts) and extent
/// (`vt+0x84`, `vt+0x94`).</summary>
public readonly record struct AwardFootprint(int X, int Z, int Width, int Depth);

/// <summary>One placed object as the weekly pass sees it: its compiled kind (rides 3 ordinary, 7 tour, 6 track,
/// 1 coaster; 4 shop; 5 sideshow; 2 feature), status `+0xA2` (0 inactive), a feature's DBA `+0x2E` flags,
/// a ride's tier byte `+0x126`, its footprint and its DBA purchase cost.</summary>
public readonly record struct AwardPlacement(AssetResourceDatabase.AssetKind Kind, byte Status, byte FeatureFlags,
    int Tier, AwardFootprint Footprint, int PurchaseCost)
{
    public bool IsRide => Kind is AssetResourceDatabase.AssetKind.Ride or AssetResourceDatabase.AssetKind.TourRide
        or AssetResourceDatabase.AssetKind.TrackRide or AssetResourceDatabase.AssetKind.Coaster;
    /// <summary>`0x1307E8`: DBA `+0x2E` bit 2, a litter bin (the same bit guests look for, ParkVisitors arm 3).</summary>
    public bool IsBin => Kind == AssetResourceDatabase.AssetKind.Feature && (FeatureFlags & 4) != 0;
}

/// <summary>⭐ What the weekly pass `0x16BC70` asks of the park beyond the calendar and the money -- the four
/// hidden awards still unported before 2026-10-02 and the fourth goal (findings/awards.md, "The weekly pass,
/// whole"). Each is a hook because the port keeps placements in the view; a null hook keeps its award shut.
///
/// ⚠ "Held on the cursor": the native counts behind the fourth goal (`0x14CF88/58/28`, `0x14CE68/98/C8/F8`)
/// subtract the object a build tool is carrying. The port's build tool holds a ghost, not a pool object, so
/// the viewer passes plain counts (as <see cref="AdvisorProducers"/> says of the same pools).</summary>
public sealed class ParkAwardInputs
{
    /// <summary>`0x153248`: rides of every kind -- the ordinary, tour, track, coaster and tour-transport pools
    /// (`0x39528C`, `0x395290`, `0x395294`, `0x395298`, `0x3952C8 PoolOfTourTransports`), each its count at
    /// `+0xC`. Upgrade and Aesthetic need more than 7, Path Economy more than 9.</summary>
    public Func<int> Rides { get; set; }

    /// <summary>`0x16BB80`: every ride on the ride iterator (`0x1E5B20(it, 1)`) has a non-zero tier byte `+0x126`
    /// (<see cref="ParkRide.CurrentTier"/>) -- true for no rides at all.</summary>
    public Func<bool> AllRidesUpgraded { get; set; }

    /// <summary>`0x16BBD0`: the summed `0x12BC10` value of every placed feature (`0x14CD30`), which for a feature
    /// (kind 2) is its DBA `+0x20`, the purchase cost (<see cref="AssetResourceDatabase.Entry.SimpleEconomy"/>).</summary>
    public Func<int> FeatureCost { get; set; }

    /// <summary>`0x152FB0(5, 2)`, the Green award -- see <see cref="ShopsHaveBins"/>.</summary>
    public Func<bool> Green { get; set; }

    /// <summary>`0x1531D8`: grid cells whose type byte is 2 (`0x1E6338`), the laid path. ⚠ INFERRED that type 2 is
    /// the port's <see cref="ParkPathKind.Path"/>.</summary>
    public Func<int> PathCells { get; set; }

    /// <summary>The fourth goal's counts: sideshows `0x3952A4`, shops `0x39529C`, features `0x3952A0`, and the
    /// four ride pools summed (ordinary, tour, track, coaster -- not the tour transports).</summary>
    public Func<(int Sideshows, int Shops, int Features, int Rides)> StarterCounts { get; set; }

    /// <summary>⭐ The inputs over a placement census (<paramref name="census"/>, re-read on every call) and the
    /// path grid. ⚠ INFERRED: one tour transport (`0x3952C8`) per tour ride, so a tour ride counts twice in
    /// <see cref="Rides"/> -- the pool's members are the vehicles cow tools' tour rides fly, one per station.
    /// ⚠ A scriptless placement has no live ride, so no tier: such a ride reads tier 0, never upgraded.</summary>
    public static ParkAwardInputs FromCensus(Func<IReadOnlyList<AwardPlacement>> census, Func<int> pathCells)
    {
        IReadOnlyList<AwardPlacement> All() => census?.Invoke() ?? Array.Empty<AwardPlacement>();
        int Count(IReadOnlyList<AwardPlacement> all, AssetResourceDatabase.AssetKind k)
        {
            int n = 0;
            foreach (var p in all) if (p.Kind == k) n++;
            return n;
        }
        return new ParkAwardInputs
        {
            Rides = () =>
            {
                var all = All();
                int n = 0;
                foreach (var p in all) if (p.IsRide) n++;
                return n + Count(all, AssetResourceDatabase.AssetKind.TourRide);
            },
            AllRidesUpgraded = () =>
            {
                foreach (var p in All()) if (p.IsRide && p.Tier == 0) return false;
                return true;
            },
            FeatureCost = () =>
            {
                int sum = 0;
                foreach (var p in All()) if (p.Kind == AssetResourceDatabase.AssetKind.Feature) sum += p.PurchaseCost;
                return sum;
            },
            Green = () =>
            {
                var all = All();
                var shops = new List<AwardFootprint>();
                var bins = new List<AwardFootprint>();
                foreach (var p in all)
                {
                    if (p.Status == 0) continue;
                    if (p.Kind == AssetResourceDatabase.AssetKind.Shop) shops.Add(p.Footprint);
                    else if (p.IsBin) bins.Add(p.Footprint);
                }
                return ShopsHaveBins(Count(all, AssetResourceDatabase.AssetKind.Shop), shops, bins);
            },
            PathCells = pathCells,
            StarterCounts = () =>
            {
                var all = All();
                int rides = 0;
                foreach (var p in all) if (p.IsRide) rides++;
                return (Count(all, AssetResourceDatabase.AssetKind.Sideshow), Count(all, AssetResourceDatabase.AssetKind.Shop),
                        Count(all, AssetResourceDatabase.AssetKind.Feature), rides);
            },
        };
    }

    /// <summary>⭐ `0x152FB0(minShops, d)`, READ (`0x152FB0..0x1531D0`): false with fewer than
    /// <paramref name="minShops"/> shops in the pool (`0x39529C +0xC`, every shop, active or not); otherwise every
    /// ACTIVE shop (status `+0xA2` != 0) needs an active placed object that is a bin (DBA `+0x2E` bit 2,
    /// `0x1307E8`) whose footprint comes within <paramref name="distance"/> cells of the shop's on both axes, the
    /// first shop without one answers false. ⚠ Five shops none of which is active pass -- the loop has nothing
    /// to test; and an active shop with no active object at all fails outright (the inner list is empty).</summary>
    public static bool ShopsHaveBins(int shopsInPool, IReadOnlyList<AwardFootprint> activeShops,
                                     IReadOnlyList<AwardFootprint> activeBins, int minShops = 5, int distance = 2)
    {
        if (shopsInPool < minShops) return false;
        foreach (var s in activeShops)
        {
            bool found = false;
            foreach (var b in activeBins)
            {
                if (b.X + b.Width < s.X - distance) continue;            // the bin ends left of the shop's reach
                if (s.X + distance + s.Width < b.X) continue;            // or starts right of it
                if (s.Z - distance > b.Z + b.Depth) continue;            // above it
                if (s.Z + distance + s.Depth < b.Z) continue;            // or below it
                found = true;
                break;
            }
            if (!found) return false;
        }
        return true;
    }
}
