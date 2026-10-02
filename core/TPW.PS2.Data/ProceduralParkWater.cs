using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace TPW.PS2.Data;

/// <summary>
/// Pure-data reconstruction of PAL park water (22c858/22cb30/22cb90), not a renderer.
/// All coordinates are native, +Y up, with unmirrored Z. The caller chooses N (4..16),
/// advances state once per native draw, supplies storage, and handles texture/material,
/// projected-bound LOD, Z mirroring and the native constant normal (0,112,0).
/// No slope normals, visibility, weather, shaders or engine dependencies are generated.
/// </summary>
public static class ProceduralParkWater
{
    public const int MinimumDimension = 4;
    public const int MaximumDimension = 16;
    public const int NoiseSegmentCount = 128;
    public const int SkirtVertexCount = 8;

    public enum World { Jungle = 0, Hallow = 1, Fantasy = 2, Space = 3 }

    public readonly record struct SurfaceBounds(float MinX, float MaxX, float MinZ, float MaxZ, float BaseY);

    /// <summary>
    /// Constants and branch placement read from the owner's file-backed PAL ELF. Construction
    /// is guarded by hashes of the interpreted code, never silently applied to another build.
    /// The ELF is read in memory only; neither this type nor Read writes/extracts assets.
    /// </summary>
    public sealed class Profile
    {
        public float SpatialSpan { get; private init; }
        public float PhaseDivisor { get; private init; }
        public float InitialPhaseDivisor { get; private init; }
        public long InitialUvAccumulator { get; private init; }
        public float XYHeightScale { get; private init; }
        public float ZHeightScale { get; private init; }
        public float SampleScale { get; private init; }
        public float SurfaceOffset { get; private init; }
        public float BaseY { get; private init; }
        private float minXOffset, maxXOffset, minZ, maxZ;
        private readonly float[] suppliedX = new float[8];
        private Profile() { }

        public static Profile Read(Disc disc)
        {
            ArgumentNullException.ThrowIfNull(disc);
            var entry = disc.Files().Single(f => f.Path == "/SLES_500.32");
            return ReadExecutable(disc.Read(entry.Extent, entry.Size));
        }

        public static Profile ReadExecutable(byte[] elf)
        {
            ArgumentNullException.ThrowIfNull(elf);
            if (elf.Length < 52 || !elf.AsSpan(0, 6).SequenceEqual(new byte[] { 127, 69, 76, 70, 1, 1 }))
                throw new InvalidDataException("Park water requires a little-endian ELF32 executable");
            uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(offset, 4));
            int U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(offset, 2));
            uint ph = U32(28);
            int stride = U16(42), count = U16(44);
            if (stride < 32 || (ulong)ph + (ulong)stride * (uint)count > (ulong)elf.Length)
                throw new InvalidDataException("Invalid park-water ELF program-header table");
            for (int i = 0; i < count; i++)
            {
                int p = checked((int)(ph + (uint)(i * stride)));
                if (U32(p) == 1 && (U32(p + 16) > U32(p + 20) ||
                    (ulong)U32(p + 4) + U32(p + 16) > (ulong)elf.Length))
                    throw new InvalidDataException("Invalid park-water ELF PT_LOAD extent");
            }
            int Offset(uint address, int size = 4)
            {
                for (int i = 0; i < count; i++)
                {
                    int p = checked((int)(ph + (uint)(i * stride)));
                    if (U32(p) == 1 && address >= U32(p + 8) &&
                        (ulong)address + (uint)size <= (ulong)U32(p + 8) + U32(p + 16))
                        return checked((int)((ulong)U32(p + 4) + address - U32(p + 8)));
                }
                throw new InvalidDataException($"Park-water EE address 0x{address:x} is not file-backed PT_LOAD");
            }
            void Guard(uint address, int length, string hash)
            {
                var actual = Convert.ToHexString(SHA256.HashData(elf.AsSpan(Offset(address, length), length)));
                if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Unsupported park-water code at EE 0x{address:x}");
            }
            Guard(0x149958, 0x274, "a78ac681963137588defb9a0cbf1cf6bb856371401b3b9f1db27315eb934cc89");
            Guard(0x220fa0, 0x23c, "68392a128a051dd6f8bafe8cebaebc6dbaa46ec399af443a5a315add3191278a");
            Guard(0x22c858, 0x2d8, "b553c3445638f20a7915e0d0e2ab9f25dc599415add254caa0ec4c7bec03c57a");
            Guard(0x22cb30, 0x60, "d9d9a2e146bb159178b4a71029a6e5c75d7dd3cca7bc312a1150db76194b8964");
            Guard(0x22cb90, 0x21e0, "71a3360f586e51c3916c5bf5b969279f990dc9065dd47c91811ce1f6cc74674b");
            Guard(0x238cc8, 0x690, "2940502ba16acd0681934d0914dc200d00d377fa505d3ef72de1d57fe20acfcb");
            Guard(0x239358, 0x190, "1259a6b36143b3e0257eb3c9d503f76a905c6af9f4bedd85eb684255eca5d0a3");
            Guard(0x29cf08, 0x30, "c6b209965c879c2e5296339a4505465ff043c0e902b1f3ad5c8b253123cf470a");
            float Immediate(uint address)
            {
                uint hi = U32(Offset(address)), next = U32(Offset(address + 4));
                if ((hi & 0xffff0000) != 0x3c010000)
                    throw new InvalidDataException("Expected park-water LUI $at");
                uint bits = (hi & 0xffff) << 16;
                if ((next & 0xffff0000) == 0x34210000) bits |= next & 0xffff;
                return BitConverter.UInt32BitsToSingle(bits);
            }
            var result = new Profile
            {
                InitialPhaseDivisor = Immediate(0x22c940), // 100
                InitialUvAccumulator = U32(Offset(0x22cad4)) & 0xffff, // addiu v1,zero,0x1000
                PhaseDivisor = Immediate(0x22cac8), // 317
                SpatialSpan = Immediate(0x22cad8), // 12
                XYHeightScale = Immediate(0x22cae4), // .2
                ZHeightScale = Immediate(0x22caf0), // .6
                SampleScale = Immediate(0x22cd94), // .5
                SurfaceOffset = Immediate(0x22cec4), // .03
                BaseY = ClearFlag(Immediate(0x221158)), // bits bf0ccccc, not rounded -.55
                minXOffset = Immediate(0x22111c), // 6.125
                maxXOffset = Immediate(0x221128), // 18.875
                minZ = Immediate(0x149b84) - Immediate(0x221140), // 5.875 - 20.875
                maxZ = Immediate(0x149b84) + Immediate(0x221178) // 5.875 + .525
            };
            // Verify the native 149958 branches, not guessed centers or art bounds.
            result.suppliedX[0] = result.suppliedX[1] = Immediate(0x149aa4); // Jungle 23.125
            result.suppliedX[2] = Immediate(0x149b1c); // Hallow 37.125
            result.suppliedX[3] = Immediate(0x149b30); // Hallow 41.125
            result.suppliedX[4] = Immediate(0x149b70); // Fantasy 31.125
            result.suppliedX[5] = Immediate(0x149b9c); // Fantasy 33.125
            result.suppliedX[6] = Immediate(0x149ad4); // Space 29.125
            result.suppliedX[7] = Immediate(0x149ae4); // Space 41.125
            return result;
        }

        /// <summary>World IDs 0..3 and terrain variants 1/2 only. Native's "other" branch
        /// maps to variant 2; invalid inputs are rejected rather than fabricated.</summary>
        public SurfaceBounds Bounds(World world, int variant)
        {
            if ((uint)world > 3) throw new ArgumentOutOfRangeException(nameof(world));
            if (variant != 1 && variant != 2) throw new ArgumentOutOfRangeException(nameof(variant));
            float x = suppliedX[(int)world * 2 + variant - 1];
            return new(ClearFlag(x - minXOffset), ClearFlag(x + maxXOffset), minZ, maxZ, BaseY);
        }
    }

    /// <summary>29cf08, updating the full uint state but returning only the low 31 bits.
    /// A supplied state is explicit caller input, NOT a claim about emulator seed/history.</summary>
    public static uint NextRandom(ref uint state)
    {
        state = unchecked(state * 0x41c64e6dU + 0x3039U);
        return state & 0x7fffffffU;
    }

    /// <summary>
    /// Immutable 128-segment vec3 cubic table at native 339c60, generated by the first
    /// random-table portion of 238cc8. Exactly 384 RNG results are consumed, X/Y/Z in order.
    /// This does not generate its other tables or replay earlier/later global RNG consumers.
    /// </summary>
    public sealed class NativeNoise
    {
        private readonly Vector3[] a, b, c, d;
        private NativeNoise(ReadOnlySpan<Vector3> points)
        {
            a = new Vector3[NoiseSegmentCount]; b = new Vector3[NoiseSegmentCount];
            c = new Vector3[NoiseSegmentCount]; d = new Vector3[NoiseSegmentCount];
            for (int i = 0; i < NoiseSegmentCount; i++)
            {
                Vector3 p0 = points[i], p1 = points[(i + 1) & 127];
                Vector3 p2 = points[(i + 2) & 127], p3 = points[(i + 3) & 127];
                Vector3 difference = p0 - p1;
                a[i] = (p3 - p2) - difference;
                b[i] = difference - a[i];
                c[i] = p2 - p0;
                d[i] = p1;
            }
        }

        /// <summary>seed is the RNG state immediately BEFORE the table's first random call.
        /// Updated to the state immediately after its 384 calls; initial phase is a separate call.</summary>
        public static NativeNoise Generate(ref uint seed)
        {
            Span<Vector3> points = stackalloc Vector3[NoiseSegmentCount];
            for (int i = 0; i < points.Length; i++)
            {
                float x = RandomCoordinate(NextRandom(ref seed));
                float y = RandomCoordinate(NextRandom(ref seed));
                float z = RandomCoordinate(NextRandom(ref seed));
                points[i] = new(x, y, z);
            }
            return new NativeNoise(points);
        }

        public static NativeNoise Generate(Func<uint> random)
        {
            ArgumentNullException.ThrowIfNull(random);
            Span<Vector3> points = stackalloc Vector3[NoiseSegmentCount];
            for (int i = 0; i < points.Length; i++)
                points[i] = new(RandomCoordinate(random()), RandomCoordinate(random()), RandomCoordinate(random()));
            return new NativeNoise(points);
        }

        /// <summary>Explicit source points for deterministic tools/audits. Copies their cubic
        /// coefficients; does not retain the caller's storage. Requires 128 finite vec3s.</summary>
        public static NativeNoise FromSamples(ReadOnlySpan<Vector3> points)
        {
            if (points.Length != NoiseSegmentCount) throw new ArgumentException("Expected 128 vec3 samples", nameof(points));
            foreach (var point in points)
                if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                    throw new ArgumentException("Noise samples must be finite", nameof(points));
            return new NativeNoise(points);
        }

        /// <summary>239358 uses EE cvt.w.s (toward zero, NOT floor/nearest), then &amp;127.
        /// The fraction is input-trunc(input), including negative fractions. For nonnegative
        /// native phases this is a 128-periodic cubic. Rejects nonfinite/out-of-int-range inputs;
        /// this is not a general emulation of EE exceptional/saturating float arithmetic.</summary>
        public Vector3 Sample(float input)
        {
            int truncated = TruncateWord(input);
            int segment = truncated & 127;
            float t = input - (float)truncated, t2 = t * t, t3 = t2 * t;
            return a[segment] * t3 + b[segment] * t2 + c[segment] * t + d[segment];
        }

        private static float RandomCoordinate(uint random) => (random & 0x3fff) * (1f / 8192f) - 1f;
    }

    /// <summary>Mutable per-water-object fields, not a clock or FPS approximation. Phase is
    /// never reduced modulo 128. Advance is separate from Fill, so callers must not advance
    /// again when regenerating/reusing a grid. No assumed runtime seed is provided.</summary>
    public sealed class State
    {
        public Profile Profile { get; }
        public float Phase { get; private set; }
        public long UvAccumulator { get; private set; }

        /// <summary>Explicit starting phase/accumulator, also usable for restored state.</summary>
        public State(Profile profile, float phase, long uvAccumulator)
        {
            ArgumentNullException.ThrowIfNull(profile);
            if (!float.IsFinite(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
            Profile = profile; Phase = phase; UvAccumulator = uvAccumulator;
        }

        public static State Create(Profile profile, uint initialPhaseRandom)
        {
            ArgumentNullException.ThrowIfNull(profile);
            return new(profile, (initialPhaseRandom & 0x3fff) / profile.InitialPhaseDivisor, profile.InitialUvAccumulator);
        }

        public static State Create(Profile profile, Func<uint> random)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(random);
            return Create(profile, random());
        }

        public static State Create(Profile profile, ref uint seed)
        {
            ArgumentNullException.ThrowIfNull(profile);
            return Create(profile, NextRandom(ref seed));
        }

        /// <summary>22cb30: 32-bit delta*3 wraps, signed division by 2 truncates, sign-extends
        /// into the 64-bit accumulator. The comparison is UNSIGNED; subtract 0x1000 ONCE
        /// only when above 0x2000 (not >=, not modulo). Negative deltas follow these native
        /// arithmetic rules too; ordinary renderer deltas are nonnegative milliseconds.</summary>
        public void AdvanceMilliseconds(int delta)
        {
            UvAccumulator = unchecked(UvAccumulator + unchecked(delta * 3) / 2);
            Phase += (float)delta / Profile.PhaseDivisor;
            if (unchecked((ulong)UvAccumulator) > 0x2000UL)
                UvAccumulator = unchecked(UvAccumulator - 0x1000);
        }
    }

    public static int VertexCount(int dimension) { ValidateDimension(dimension); return dimension * dimension + SkirtVertexCount; }
    public static int IndexCount(int dimension) { ValidateDimension(dimension); return 6 * (dimension - 1) * (dimension - 1) + 12; }

    /// <summary>
    /// Writes N*N unique main vertices, indexed j*N+i (i increases X, j increases Z),
    /// then the two native four-vertex skirts. UVs are signed 16-bit native values /4096,
    /// including V subtraction modulo 65536. Arrays/spans may be larger; tails untouched.
    /// No heap allocations, no RNG consumption and no advance occur during Fill.
    /// Native packet flag bits in X/Y are removed, not treated as geometry; Z is unchanged.
    /// </summary>
    public static void Fill(NativeNoise noise, State state, SurfaceBounds bounds, int dimension,
                            Span<Vector3> positions, Span<Vector2> uvs)
    {
        ArgumentNullException.ThrowIfNull(noise);
        ArgumentNullException.ThrowIfNull(state);
        int count = VertexCount(dimension);
        if (positions.Length < count) throw new ArgumentException("Position storage too small", nameof(positions));
        if (uvs.Length < count) throw new ArgumentException("UV storage too small", nameof(uvs));
        if (!float.IsFinite(bounds.MinX) || !float.IsFinite(bounds.MaxX) ||
            !float.IsFinite(bounds.MinZ) || !float.IsFinite(bounds.MaxZ) || !float.IsFinite(bounds.BaseY) ||
            bounds.MinX >= bounds.MaxX || bounds.MinZ >= bounds.MaxZ)
            throw new ArgumentException("Expected finite, increasing water bounds", nameof(bounds));
        var profile = state.Profile;
        Span<Vector3> samples = stackalloc Vector3[MaximumDimension];
        Span<short> rawUv = stackalloc short[MaximumDimension];
        float denominator = dimension - 1, reciprocal = 1f / denominator;
        float uvStep = reciprocal * 32768f;
        float xStep = (bounds.MaxX - bounds.MinX) / denominator;
        float zStep = (bounds.MaxZ - bounds.MinZ) / denominator;
        int scroll = unchecked((ushort)state.UvAccumulator);
        for (int i = 0; i < dimension; i++)
        {
            samples[i] = noise.Sample((float)i * reciprocal * profile.SpatialSpan + state.Phase) * profile.SampleScale;
            // Actual draw is i * ((1/(N-1))*32768) - 4, not double/integer division.
            rawUv[i] = unchecked((short)TruncateWord((float)i * uvStep - 4f));
        }
        for (int j = 0; j < dimension; j++)
        {
            float zContribution = profile.ZHeightScale * samples[dimension - 1 - j].Z;
            for (int i = 0; i < dimension; i++)
            {
                float y = bounds.BaseY + profile.SurfaceOffset +
                    profile.XYHeightScale * (samples[dimension - 1 - i].X + samples[i].Y);
                int index = j * dimension + i;
                positions[index] = new(ClearFlag(bounds.MinX + xStep * (float)i),
                    ClearFlag(y + zContribution), bounds.MinZ + zStep * (float)j);
                uvs[index] = new(rawUv[i] / 4096f, unchecked((short)(rawUv[j] - scroll)) / 4096f);
            }
        }
        // 22cb90's exact eight appended vertices; second skirt is intentionally not
        // the whole back edge of the grid. Native literal units, not fitted margins.
        int start = dimension * dimension;
        float skirtY = ClearFlag(bounds.BaseY - .35f);
        for (int k = 0; k < SkirtVertexCount; k++)
        {
            float x = (k & 1) != 0 ? bounds.MaxX + 31f : bounds.MinX + (k < 4 ? -30f : 18f);
            float z = k < 2 ? bounds.MinZ - 20f : k < 6 ? bounds.MinZ : bounds.MinZ + 10f;
            int v = k < 2 ? -16384 : k < 4 ? 16384 : k < 6 ? -8192 : 4096;
            positions[start + k] = new(ClearFlag(x), skirtY, z);
            uvs[start + k] = new(((k & 1) == 0 ? -16384 : 16384) / 4096f,
                unchecked((short)(v - scroll)) / 4096f);
        }
    }

    /// <summary>Writes triangle-list indices for Fill's layout, geometric winding +Y in
    /// NATIVE coordinates. The native row-strip diagonals are preserved. Both skirts are
    /// independent quads, not connected to each other/main grid. A caller mirroring Z must
    /// handle its engine's winding convention; this API never mirrors coordinates/indices.</summary>
    public static void FillTriangleIndices(int dimension, Span<int> indices)
    {
        int count = IndexCount(dimension);
        if (indices.Length < count) throw new ArgumentException("Index storage too small", nameof(indices));
        int output = 0;
        for (int j = 0; j < dimension - 1; j++)
            for (int i = 0; i < dimension - 1; i++)
            {
                int a = j * dimension + i, b = a + dimension, c = a + 1, d = b + 1;
                indices[output++] = a; indices[output++] = b; indices[output++] = c;
                indices[output++] = c; indices[output++] = b; indices[output++] = d;
            }
        for (int k = 0; k < 2; k++)
        {
            int a = dimension * dimension + k * 4;
            indices[output++] = a; indices[output++] = a + 2; indices[output++] = a + 1;
            indices[output++] = a + 2; indices[output++] = a + 3; indices[output++] = a + 1;
        }
    }

    private static void ValidateDimension(int dimension)
    {
        if (dimension < MinimumDimension || dimension > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(dimension), "Native water dimension must be 4..16");
    }

    // EE cvt.w.s witnesses: 22ce24 (UV), 239358 (sample segment). Bounded inputs only:
    // explicit toward-zero conversion, avoiding .NET's undefined out-of-range cast behavior.
    private static int TruncateWord(float value)
    {
        if (!float.IsFinite(value) || value < -2147483648f || value >= 2147483648f)
            throw new ArgumentOutOfRangeException(nameof(value), "Expected finite signed-word conversion input");
        return (int)MathF.Truncate(value);
    }

    private static float ClearFlag(float value) =>
        BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits(value) & ~1U);
}
