// Algorithm adapted from dotnet/runtime v8.0.0, Random.Net5CompatImpl.cs.
// Licensed to the .NET Foundation under the MIT license.
// See licenses/DOTNET-RUNTIME-MIT.txt for the full notice and source URL.
using System;

namespace TPW.PS2.Data;

/// <summary>
/// Explicit-state equivalent of seeded .NET 8 System.Random for the methods exposed here.
/// Not Random.Shared, the unseeded generator, a cryptographic RNG, or thread-safe.
/// State snapshots are detached copies; no seed replay or runtime reflection is used.
/// </summary>
public sealed class SnapshotRandom
{
    public const int StateVersion = 1;
    private int[] seedArray;
    private int inext;
    private int inextp;

    /// <summary>Versioned JSON-ready state. All members must be present on deserialization.</summary>
    public sealed class State
    {
        public required int Version { get; init; }
        public required int[] SeedArray { get; init; }
        public required int Inext { get; init; }
        public required int Inextp { get; init; }
    }

    public SnapshotRandom(int seed)
    {
        // The historical initialization deliberately uses unchecked Int32 arithmetic.
        unchecked
        {
            seedArray = new int[56];
            int subtraction = seed == int.MinValue ? int.MaxValue : Math.Abs(seed);
            int mj = 161803398 - subtraction;
            seedArray[55] = mj;
            int mk = 1;
            int ii = 0;
            for (int i = 1; i < 55; i++)
            {
                if ((ii += 21) >= 55) ii -= 55;
                seedArray[ii] = mk;
                mk = mj - mk;
                if (mk < 0) mk += int.MaxValue;
                mj = seedArray[ii];
            }
            for (int k = 1; k < 5; k++)
            {
                for (int i = 1; i < 56; i++)
                {
                    int n = i + 30;
                    if (n >= 55) n -= 55;
                    seedArray[i] -= seedArray[1 + n];
                    if (seedArray[i] < 0) seedArray[i] += int.MaxValue;
                }
            }
            inext = 0;
            inextp = 21;
        }
    }

    private SnapshotRandom(State state) => RestoreState(state);

    public State CaptureState() => new State
    {
        Version = StateVersion,
        SeedArray = (int[])seedArray.Clone(),
        Inext = inext,
        Inextp = inextp
    };

    /// <summary>Validates and copies before changing any live state.</summary>
    /// <exception cref="ArgumentException">Null, unsupported, or malformed state.</exception>
    public void RestoreState(State state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Version != StateVersion)
            throw new ArgumentException("Unsupported random state version.", nameof(state));
        if (state.SeedArray == null || state.SeedArray.Length != 56)
            throw new ArgumentException("Random state requires a 56-element array.", nameof(state));
        // Slot zero is unused; the two cursors stay 21 positions apart modulo 55.
        // Inext == 0 is the constructor's sentinel and cannot reappear after a draw.
        if (state.Inext < 0 || state.Inext > 55 || state.Inextp < 1 || state.Inextp > 55 ||
            state.Inextp != (state.Inext + 20) % 55 + 1)
            throw new ArgumentException("Invalid random state cursors.", nameof(state));
        var copy = (int[])state.SeedArray.Clone();
        if (copy[0] != 0)
            throw new ArgumentException("Random state slot zero must be zero.", nameof(state));
        for (int i = 1; i < copy.Length; i++)
            if (copy[i] < 0 || copy[i] >= int.MaxValue)
                throw new ArgumentException("Random state sample is out of range.", nameof(state));
        seedArray = copy;
        inext = state.Inext;
        inextp = state.Inextp;
    }

    public static SnapshotRandom FromState(State state) => new SnapshotRandom(state);

    public int Next() => InternalSample();

    public int Next(int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return (int)(Sample() * maxValue);
    }

    public int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue)
            throw new ArgumentOutOfRangeException(nameof(minValue), "minValue exceeds maxValue.");
        long range = (long)maxValue - minValue;
        return range <= int.MaxValue
            ? (int)(Sample() * range) + minValue
            : (int)((long)(GetSampleForLargeRange() * range) + minValue);
    }

    public double NextDouble() => Sample();

    public void NextBytes(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        NextBytes(buffer.AsSpan());
    }

    public void NextBytes(Span<byte> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = unchecked((byte)InternalSample());
    }

    private double Sample() => InternalSample() * (1.0 / int.MaxValue);

    private double GetSampleForLargeRange()
    {
        int result = InternalSample();
        if (InternalSample() % 2 == 0) result = -result;
        double d = result;
        d += int.MaxValue - 1;
        d /= 2 * (uint)int.MaxValue - 1;
        return d;
    }

    private int InternalSample()
    {
        int next = inext + 1;
        if (next >= 56) next = 1;
        int nextp = inextp + 1;
        if (nextp >= 56) nextp = 1;
        int result = seedArray[next] - seedArray[nextp];
        if (result == int.MaxValue) result--;
        if (result < 0) result += int.MaxValue;
        seedArray[next] = result;
        inext = next;
        inextp = nextp;
        return result;
    }
}
