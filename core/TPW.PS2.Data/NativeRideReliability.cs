#nullable enable

namespace TPW.PS2.Data;

/// <summary>⭐⭐ A PLACED RIDE'S RELIABILITY, LIVE FROM ITS THREE SLIDERS.
///
/// ⚠⚠ THIS FILE EXISTS BECAUSE I GOT IT WRONG. I reported that reliability was not recomputed
/// from the sliders, on the strength of a caller census over `FUN_00198A98` -- the shopfront
/// preview's arithmetic -- which has exactly one caller. That census was true and the conclusion
/// drawn from it was not: it said nothing about any OTHER function computing reliability, and one
/// does. Master, 2026-09-27: *"it absolutely does recomp reliability live. your ctrl f just
/// failed. dont assume, actually research."* Right on both counts.
///
/// ⭐ The way in was the screen's own draw rather than a search. `FUN_001D5210` fills its four
/// bars from `vt+0x1D4` (excitement), **`vt+0x2EC`** (reliability), `FUN_00118228`
/// (`ride[0xE4] &gt;&gt; 12`, the worn condition) and `FUN_00118238` (`ride[0x94]`, life).
///
/// `vt+0x2EC` is `FUN_001183F0`:
/// <code>
///   wear     = vt[0x36C](ride, 1)
///   duration = vt[0x304](ride)
///   v        = (wear * duration * 9) >> 15
///   return 100 - min(v, 100)
/// </code>
///
/// and `vt+0x36C` is per family -- `0x1B80E0` ride, `0x1E9E10` tour, `0x201F78` track -- which are
/// exactly the three functions an earlier census had already flagged as reading BOTH speed and
/// capacity. The evidence was in my own output and I had not followed it.
///
/// ⭐ THE CONTROL THIS IS CHECKED BY: <see cref="RideCatalogue.ShopfrontReliability"/> is this same
/// arithmetic frozen at the middle. Its `Half(p) = ((0x1000-p) * 0x800 >> 12) + p` is the speed
/// term with `(speed &lt;&lt; 12)/100` equal to `0x800`, i.e. speed 50, and the capacity term at half
/// of maximum; and it multiplies by the tier's AVERAGE duration where this uses the live one. So
/// evaluating this at speed 50, half capacity and the average duration must reproduce the
/// shopfront number exactly -- see <see cref="MatchesShopfront"/>.</summary>
public static class NativeRideReliability
{
    /// <summary>The wear term, `vt+0x36C` with its second argument 1 -- the form the reliability
    /// getter uses, which takes the ride's CURRENT capacity rather than a seat count.
    /// ⚠ The quadratic above `0xCCB` is the console's: past four fifths of maximum capacity the
    /// penalty stops being linear. `(cp - 0xCCC) >> 6`, squared, added on.</summary>
    public static int Wear(int minSpeedDamage, int minCapacityDamage, int wearRate,
                           int speed, int capacity, int maxCapacity)
    {
        unchecked
        {
            int sp = SpeedTerm(minSpeedDamage, speed);
            if (maxCapacity == 0) return 0;          // the console traps here; answering 0 is kinder
            return ((sp + CapacityTerm(minCapacityDamage, capacity, maxCapacity)) / 2) * wearRate;
        }
    }

    /// <summary>The speed half every family shares (`0x1B80E0`, `0x1225F8`, `0x201F78`).</summary>
    static int SpeedTerm(int minSpeedDamage, int speed)
    {
        unchecked
        {
            int sp = (0x1000 - minSpeedDamage) * ((speed << 12) / 100) >> 12;
            // ⚠ NOT a clamp, a branch: below 100 the damage is ADDED, at or above it the two are
            // averaged with unity. Reproduced as written rather than tidied into one expression.
            return speed < 100 ? sp + minSpeedDamage : (minSpeedDamage + sp + 0x1000) / 2;
        }
    }

    /// <summary>The load half every family shares: `(load &lt;&lt; 12) / max`, then the quadratic knee
    /// above four fifths. ⚠ `maxCapacity` must not be 0 (the console traps; callers answer 0).</summary>
    static int CapacityTerm(int minCapacityDamage, int load, int maxCapacity)
    {
        unchecked
        {
            int cp = (load << 12) / maxCapacity;
            int t = (0x1000 - minCapacityDamage) * cp;
            int capTerm = (t >> 12) + minCapacityDamage;
            if (cp > 0xCCB)
            {
                int e = (cp - 0xCCC) >> 6;
                capTerm = minCapacityDamage + (t >> 12) + e * e;
            }
            return capTerm;
        }
    }

    /// <summary>⭐ `0x201F78`, THE TRACK RIDE's wear term (vt `+0x36C`), READ in MIPS
    /// `0x201F78..0x202158` (findings/track-ride-operation.md §6.1): the same speed and load halves
    /// plus a track-length third, `lenTerm = (pieces &lt;&lt; 12) / 30` with `pieces` the u8 at ride
    /// `+0x26F8` (`lbu` at `0x202128`), and the three are averaged -- `/ 3` at `0x202150` -- before
    /// the wear rate multiplies (`0x202158`). So a longer track wears faster.</summary>
    public static int TrackWear(int minSpeedDamage, int minCapacityDamage, int wearRate,
                                int speed, int load, int maxCapacity, int pieces)
    {
        unchecked
        {
            int sp = SpeedTerm(minSpeedDamage, speed);
            if (maxCapacity == 0) return 0;          // the console traps (break 7 at 0x2020B8); 0 is kinder
            int capTerm = CapacityTerm(minCapacityDamage, load, maxCapacity);
            int lenTerm = ((pieces & 0xFF) << 12) / 30;
            return ((lenTerm + sp + capTerm) / 3) * wearRate;
        }
    }

    /// <summary>`FUN_001183F0`: the wear term against the live duration, inverted into a percent.
    /// ⚠ The console clamps only the upper end, so a negative product returns ABOVE 100. Kept.</summary>
    public static int FromWear(int wear, int duration)
    {
        unchecked
        {
            int v = wear * duration * 9 >> 15;
            return 100 - (v < 101 ? v : 100);
        }
    }

    /// <summary>Reliability for a ride at its current settings, or null without a tier.</summary>
    /// <param name="tier">The ride's CURRENT tier (`ParkRide.CurrentTier`, the `+0x126` byte), not
    /// zero: every tier getter on the disc strides by it, so an upgraded ride has different
    /// damages and a different capacity bound.</param>
    public static int? Calculate(AssetResourceDatabase.Entry? entry, int speed, int capacity,
                                 int duration, int tier = 0)
    {
        if (entry is not { HasRideTiers: true }) return null;
        var t = entry.Tier(tier);
        int max = t.CapacityParameter <= 0 ? 1 : t.CapacityParameter;
        return FromWear(Wear(t.MinSpeedDamage, t.MinCapacityDamage, t.WearRate,
                             speed, Math.Clamp(capacity, 1, max), max), duration);
    }

    /// <summary>⭐ THE CONTROL, as an assertion a caller can run: at speed 50 and half capacity the
    /// wear term must equal the shopfront preview's `inner`, because that is the same arithmetic
    /// with those two values substituted. If this ever returns false, one of the two readings is
    /// wrong and neither should be trusted until it is settled.</summary>
    public static bool MatchesShopfront(AssetResourceDatabase.Entry? entry)
    {
        // ⚠ Tier ZERO on purpose: the shopfront figure this checks against is about an unbuilt
        // ride, which has no tier yet.
        if (entry is not { HasRideTiers: true }) return false;
        var t = entry.Tier(0);
        int max = t.CapacityParameter <= 0 ? 1 : t.CapacityParameter;
        int Half(int p) => ((0x1000 - p) * 0x800 >> 12) + p;
        int expected = ((Half(t.MinSpeedDamage) + Half(t.MinCapacityDamage)) / 2) * t.WearRate;
        return Wear(t.MinSpeedDamage, t.MinCapacityDamage, t.WearRate, 50, max / 2, max) == expected;
    }
}
