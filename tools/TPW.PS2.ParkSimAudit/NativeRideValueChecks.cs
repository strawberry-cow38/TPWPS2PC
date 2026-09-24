using System.Buffers.Binary;
using TPW.PS2.Data;
using NativeAssetKind = TPW.PS2.Data.AssetResourceDatabase.AssetKind;
using State = TPW.PS2.Data.NativeRideValue.State;

/// <summary>Asset-free producer checks with literal independent expectations. These do not
/// establish live state lifecycles, script bindings, or shipping caller integration.</summary>
static class NativeRideValueChecks
{
    public static void Run(Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "native ride value: " + label);
        void Value(NativeAssetKind kind, int basis, State state, int expected, string label)
        {
            int? actual = NativeRideValue.Calculate(Entry(kind, basis), state);
            Check(actual == expected, $"{label}: expected {expected}, got {actual?.ToString() ?? "null"}");
        }

        var ordinary = Entry(NativeAssetKind.Ride, 40);
        var initial = NativeRideValue.CreateDefaultState(ordinary);
        Check(initial == new State(50, 30), "tier0 constructor is speed50/duration30/cache0");
        Check(initial.HasValue && NativeRideValue.Calculate(ordinary, initial.Value) == 37,
            "Belly Bounce conditional defaults: A3072 Z5120 M3840, 153600 >>12 =37");
        foreach (var kind in new[] { NativeAssetKind.Ride, NativeAssetKind.Coaster, NativeAssetKind.TourRide, NativeAssetKind.TrackRide })
        {
            Check(NativeRideValue.CreateDefaultState(Entry(kind, 40)) == new State(50, 30),
                $"{kind} uses operating defaults, not non-operating placeholders");
            Value(kind, 0, new State(int.MinValue, int.MaxValue, 255), 0,
                $"{kind} zero base (track ignores bonus; tour arithmetic also yields zero)");
            Value(kind, 40, new State(100, 5), kind == NativeAssetKind.Coaster ? 50 : 40,
                $"{kind} neutral ordinary duration versus coaster upper band");
        }
        // Pure results cannot distinguish tour's zero arithmetic from a hypothetical early
        // return. The production branch explicitly excludes tour; no instrumentation invented.
        Value(NativeAssetKind.Ride, 40, new State(0, 0), 22, "both lower bands: M2304, 92160 >>12");
        Value(NativeAssetKind.Ride, 40, new State(125, 30), 62, "both upper bands: M6400");
        Value(NativeAssetKind.Ride, 40, new State(100, 4), 31, "duration /5 truncates 16384 to3276");
        Value(NativeAssetKind.Ride, 40, new State(125, 6), 59, "both fixed-point stages truncate, M6143");
        Value(NativeAssetKind.Ride, 90, new State(125, 30), 100, "signed upper cap");
        Value(NativeAssetKind.Ride, -1, new State(50, 30), -1, "arithmetic shift floors negative product, no lower cap");
        Value(NativeAssetKind.Ride, -40, new State(125, 30), -63, "negative nonintegral result is not division toward zero");
        Value(NativeAssetKind.Ride, int.MaxValue, new State(100, 5), -1, "base product is low32, not widened long");
        Value(NativeAssetKind.Ride, 0x40000000, new State(100, 5), 0, "base product can wrap exactly to zero");
        Value(NativeAssetKind.Ride, 40, new State(int.MaxValue, 5), 30, "speed shifts to -4096 before signed /100");
        Value(NativeAssetKind.Ride, 40, new State(100, int.MaxValue), 30, "duration shifts to -4096 before signed /5");
        Value(NativeAssetKind.Ride, 40, new State(-1, -1), 22, "negative settings select lower bands");
        Value(NativeAssetKind.Ride, 40, new State(100, 1), 30, "ordinary /5 lower duration control");
        Value(NativeAssetKind.Coaster, 40, new State(100, 1), 40, "coaster direct duration shift: NO /5");
        Value(NativeAssetKind.Coaster, 40, new State(100, 2), 50, "coaster duration2 already upper band");
        Value(NativeAssetKind.Coaster, 40, new State(100, int.MaxValue), 30, "coaster direct shift wraps signed");
        Value(NativeAssetKind.TourRide, 40, new State(50, 30, 255), 37, "tour uses /5 and ignores track byte");
        Value(NativeAssetKind.TrackRide, 40, new State(100, 5, 5), 42, "track odd byte5 adds floor half2 before products");
        Value(NativeAssetKind.TrackRide, 40, new State(100, 5, 1), 40, "track byte1 bonus is zero");
        Value(NativeAssetKind.TrackRide, 1, new State(100, 5, 255), 100, "track byte255 is unsigned, bonus127");
        Value(NativeAssetKind.TrackRide, 0, new State(100, 5, 255), 0, "track zero early-out occurs BEFORE bonus127");
        Value(NativeAssetKind.TrackRide, int.MaxValue, new State(100, 5, 2), 0, "track base addition wraps before multiplication");
        Value(NativeAssetKind.Ride, 40, new State(100, 5, 255, int.MaxValue, 65535, 65535), 40,
            "ordinary ignores all other family state");

        Check(NativeRideValue.CreateDefaultState(Entry(NativeAssetKind.Ride, 40, 2, 5, 0)) == new State(3, 1),
            "odd speed range floors midpoint; zero max duration becomes1");
        Check(NativeRideValue.CreateDefaultState(Entry(NativeAssetKind.Ride, 40, 10, 3, -1)) == new State(6, 1),
            "default midpoint uses signed shift, including negative range");
        Check(NativeRideValue.CreateDefaultState(Entry(NativeAssetKind.Ride, 40, int.MinValue, int.MaxValue, 60))
              == new State(int.MaxValue, 30), "default subtraction and addition wrap low32");

        var show = Entry(NativeAssetKind.Sideshow, 0);
        var showInitial = NativeRideValue.CreateDefaultState(show);
        Check(showInitial == new State(0, 0, 0, 30, 10, 33), "sideshow constructor uses compiled30/10/33");
        Check(showInitial.HasValue && NativeRideValue.Calculate(show, showInitial.Value) == 86,
            "ARC2X3 conditional defaults: 400 >>4 +11 +50 =86 even with base0");
        void Show(int basis, int prize, ushort price, ushort win, int expected, string label)
            => Value(NativeAssetKind.Sideshow, basis, new State(int.MinValue, int.MaxValue, 255, prize, price, win), expected, label);
        Show(0, 10, 20, 33, 51, "nonpositive difference is linear, -10+11+50");
        Show(0, 10, 10, 0, 50, "zero difference");
        Show(0, 11, 10, 2, 50, "positive square and win both truncate");
        Show(0, 11, 10, 3, 51, "win3 first division boundary");
        Show(90, 30, 10, 33, 90, "base wins below101");
        Show(-10, 0, 51, 0, -10, "negative candidate returns negative base, no lower clamp");
        Show(-10, 0, 50, 0, 0, "candidate0 uses max instead of negative arm");
        Show(135, 0, 51, 0, 135, "negative candidate returns base above100");
        Show(135, 28, 0, 0, 135, "candidate99 preserves out-of-range base");
        Show(135, 28, 0, 3, 135, "candidate100 still preserves out-of-range base");
        Show(135, 28, 0, 6, 100, "candidate101 returns100 even below base");
        Show(7, 50000, 0, 0, 7, "square low32 is negative; signed >>4 selects negative candidate arm");
        Show(0, 65536, 0, 0, 50, "word prize not narrowed to ushort; square wraps to0");
        Show(0, int.MaxValue, 0, 0, 50, "max signed prize square wraps to1");
        Show(0, int.MinValue, 1, 0, 50, "difference wraps positive before branch and square");
        Show(0, 0, 21800, 65535, 95, "win is unsigned instance u16, not compiled u8 or signed short");
        Show(7, 0, 65535, 0, 7, "price is unsigned instance u16");

        foreach (var kind in new[] { NativeAssetKind.Shop, NativeAssetKind.Feature })
        {
            Value(kind, int.MaxValue, new State(int.MinValue, int.MaxValue, 255, 50000, 65535, 65535),
                0, $"{kind} literal zero callback regardless of state/base");
            Check(NativeRideValue.CreateDefaultState(Entry(kind, 99)) == new State(0, 0),
                $"{kind} unused operating fields are explicit placeholders");
        }
        foreach (var kind in new[] { NativeAssetKind.TrackUpgrade, (NativeAssetKind)0, (NativeAssetKind)65535 })
        {
            Check(NativeRideValue.Calculate(Entry(kind, 45), new State(100, 5)) == null,
                $"unsupported kind {(ushort)kind} refuses, not45 or zero");
            Check(NativeRideValue.CreateDefaultState(Entry(kind, 45)) == null,
                $"unsupported kind {(ushort)kind} cannot gain defaults");
        }
        Check(NativeRideValue.Calculate(null, new State(100, 5)) == null
              && NativeRideValue.CreateDefaultState(null) == null, "missing entry refuses value and defaults");
    }

    // Synthetic compiled layout; no copyrighted assets, SAM parsing or production formula
    // used to derive expectations. Other tiers deliberately disagree with tier0.
    static AssetResourceDatabase.Entry Entry(NativeAssetKind kind, int basis, int minSpeed = 1,
        int maxSpeed = 100, int maxDuration = 60)
    {
        int extent = kind switch
        {
            NativeAssetKind.Coaster => 0xb4, NativeAssetKind.Ride => 0xa0, NativeAssetKind.TrackRide => 0xe8,
            NativeAssetKind.TourRide => 0xb8, NativeAssetKind.Shop => 0x18, NativeAssetKind.Sideshow => 0x14, _ => 0x10
        };
        var bytes = new byte[32 + extent + 4];
        void I32(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
        U16(0, (ushort)kind);
        bytes[8] = bytes[10] = 1;
        I32(24, basis);
        I32(28, extent);
        if (kind is NativeAssetKind.Ride or NativeAssetKind.Coaster or NativeAssetKind.TrackRide or NativeAssetKind.TourRide)
        {
            I32(0x38, minSpeed); I32(0x3c, maxSpeed); I32(0x44, maxDuration);
            I32(0x38 + 52, 900); I32(0x3c + 52, 1000); I32(0x44 + 52, 100);
        }
        else if (kind == NativeAssetKind.Sideshow)
        {
            U16(0x2c, 10); U16(0x2e, 30); bytes[0x30] = 33;
        }
        return new AssetResourceDatabase.Entry(0, 0, bytes);
    }
}
