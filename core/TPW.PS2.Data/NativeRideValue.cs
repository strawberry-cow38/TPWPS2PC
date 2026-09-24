#nullable enable

namespace TPW.PS2.Data;

/// <summary>Concrete families' native +1D4 value callbacks. See findings/ride-value-producer.md
/// and the shop table in findings/native-destination-score.md. This is a pure calculation,
/// not a script-variable binding, runtime-state updater, or observation of a live console.</summary>
public static class NativeRideValue
{
    /// <summary>Current instance fields, not authored SAM values. Speed/duration are signed
    /// operating settings. Track weight is the cached modulo-256 byte, NOT a piece count.
    /// Sideshow prize is a word; price and win percentage are unsigned halfword instance fields
    /// (the compiled initial win percentage is only a byte). Unused family fields are ignored.</summary>
    public readonly record struct State(int Speed, int Duration, byte CachedTrackWeight = 0,
        int SideshowPrizeValue = 0, ushort SideshowPrice = 0, ushort SideshowWinPercentage = 0);

    /// <summary>Conditional constructor defaults: tier-zero midpoint speed and half maximum
    /// duration (at least one), track cache zero, and compiled sideshow prize/price/win.
    /// Returns null for a missing record or unsupported family. Non-operating families have
    /// zero speed/duration placeholders. Call once to seed mutable state, not on every update.</summary>
    public static State? CreateDefaultState(AssetResourceDatabase.Entry? entry)
    {
        if (entry == null) return null;
        if (entry.HasRideTiers)
        {
            var tier = entry.Tier(0);
            return new State(unchecked(tier.MinSpeed + ((tier.MaxSpeed - tier.MinSpeed) >> 1)),
                Math.Max(1, tier.MaxDuration >> 1));
        }
        if (entry.Kind == AssetResourceDatabase.AssetKind.Sideshow)
        {
            var show = entry.Sideshow;
            return new State(0, 0, SideshowPrizeValue: show.PrizeValue,
                SideshowPrice: show.InitialPrice, SideshowWinPercentage: show.WinPercentage);
        }
        return entry.Kind is AssetResourceDatabase.AssetKind.Shop or AssetResourceDatabase.AssetKind.Feature
            ? new State(0, 0) : null;
    }

    /// <summary>Compute from compiled base and explicit current state. Null means unsupported,
    /// never an invented 45. Products/additions wrap to their native low 32 bits before signed
    /// shifts; division truncates toward zero. There is deliberately no final lower clamp.</summary>
    public static int? Calculate(AssetResourceDatabase.Entry? entry, State state)
    {
        if (entry == null) return null;
        var kind = entry.Kind;
        switch (kind)
        {
            case AssetResourceDatabase.AssetKind.Shop:
            case AssetResourceDatabase.AssetKind.Feature:
                return 0; // Final slot -> 1E5A98, independent of compiled base and state.
            case AssetResourceDatabase.AssetKind.Sideshow:
                return Sideshow(entry.BaseExcitement, state);
            case AssetResourceDatabase.AssetKind.Ride:       // 1B82D0
            case AssetResourceDatabase.AssetKind.Coaster:    // 1227D8
            case AssetResourceDatabase.AssetKind.TourRide:   // 1EA038
            case AssetResourceDatabase.AssetKind.TrackRide:  // 202188
                break;
            default:
                return null;
        }

        unchecked
        {
            int basis = entry.BaseExcitement;
            // Tour has no early return. Track's bonus must not revive a zero compiled base.
            if (kind != AssetResourceDatabase.AssetKind.TourRide && basis == 0) return 0;
            if (kind == AssetResourceDatabase.AssetKind.TrackRide)
                basis += state.CachedTrackWeight >> 1;
            int speed = Math.Clamp((state.Speed << 12) / 100, 0xC00, 0x1400);
            int duration = state.Duration << 12;
            if (kind != AssetResourceDatabase.AssetKind.Coaster) duration /= 5;
            duration = Math.Clamp(duration, 0xC00, 0x1400);
            int multiplier = (speed * duration) >> 12;
            return Math.Min(100, (basis * multiplier) >> 12);
        }
    }

    static int Sideshow(int basis, State state)
    {
        unchecked
        {
            int difference = state.SideshowPrizeValue - state.SideshowPrice;
            int term = difference <= 0 ? difference : (difference * difference) >> 4;
            int candidate = term + state.SideshowWinPercentage / 3 + 50;
            // Branch order matters for compiled bases outside 0..100; this is not a clamp.
            if (candidate < 0) return basis;
            if (candidate < 101) return Math.Max(basis, candidate);
            return 100;
        }
    }
}
