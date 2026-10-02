using System.Buffers.Binary;
using System.Numerics;
using TPW.PS2.Data;
using Water = TPW.PS2.Data.ProceduralParkWater;

/// <summary>Default audit family: asset-free numeric oracles except the owner's guarded ELF profile.
/// Literal branch/UV/cubic/phase expectations, not comparisons between two API paths.
/// Does not claim native execution or a reconstructed runtime seed.</summary>
static class ProceduralParkWaterChecks
{
    public static void Run(Disc disc, Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "procedural water: " + label);
        bool Near(float a, float b) => MathF.Abs(a - b) < .00002f;
        bool Near3(Vector3 a, Vector3 b) => Near(a.X, b.X) && Near(a.Y, b.Y) && Near(a.Z, b.Z);
        bool Throws(Action action)
        {
            try { action(); return false; }
            catch (ArgumentException) { return true; }
            catch (InvalidDataException) { return true; }
        }
        var profile = Water.Profile.Read(disc);
        Check(profile.SpatialSpan == 12 && profile.PhaseDivisor == 317 &&
              profile.InitialPhaseDivisor == 100 && profile.InitialUvAccumulator == 4096 &&
              profile.XYHeightScale == .2f && profile.ZHeightScale == .6f &&
              profile.SampleScale == .5f && profile.SurfaceOffset == .03f,
              "guarded constructor/draw constants");
        Check(BitConverter.SingleToUInt32Bits(profile.BaseY) == 0xbf0cccccU, "base Y packet flag cleared");
        float[] minX = { 17, 17, 31, 35, 25, 27, 23, 35 };
        float[] maxX = { 42, 42, 56, 60, 50, 52, 48, 60 };
        for (int w = 0; w < 4; w++)
            for (int v = 1; v <= 2; v++)
            {
                var b = profile.Bounds((Water.World)w, v);
                int k = w * 2 + v - 1;
                Check(b.MinX == minX[k] && b.MaxX == maxX[k] && b.MinZ == -15 && b.MaxZ == 6.4f &&
                      BitConverter.SingleToUInt32Bits(b.BaseY) == 0xbf0cccccU, $"149958 placement {w}/{v}");
            }
        Check(Throws(() => profile.Bounds((Water.World)4, 1)) &&
              Throws(() => profile.Bounds(Water.World.Jungle, 0)), "invalid placement rejected");

        uint seed = 1;
        uint[] random = { 1103527590, 377401575, 662824084, 1147902781, 2035015474, 368800899 };
        foreach (uint expected in random)
            Check(Water.NextRandom(ref seed) == expected, $"literal LCG output {expected}");
        Check(seed == 0x95fb7483U, "LCG retains high state bit while returning only low 31");
        int calls = 0;
        var generated = Water.NativeNoise.Generate(() => (uint)(calls++));
        // Segment 0 at t=0 is source p1: RNG results 3,4,5, not p0's 0,1,2.
        Check(calls == 384 && generated.Sample(0) == new Vector3(-8189f / 8192, -8188f / 8192, -8187f / 8192),
              "384 ordered RNG calls and sample integer selects p1");
        Check(generated.Sample(127) == new Vector3(-1, -8191f / 8192, -8190f / 8192),
              "last segment wraps p1 to point zero");

        var points = new Vector3[128];
        points[0] = new(1, 2, 3); points[1] = new(-2, 4, 1);
        points[2] = new(3, -1, -4); points[3] = new(0, 2, 5);
        var cubic = Water.NativeNoise.FromSamples(points);
        // Segment0 coefficients: a=(-6,5,7), b=(9,-7,-5), c=(2,-3,-7), d=(-2,4,1).
        Check(cubic.Sample(.5f) == new Vector3(.5f, 1.375f, -2.875f), "literal cubic half sample");
        Check(cubic.Sample(-.5f) == new Vector3(0, 3.125f, 2.375f), "negative uses truncation, not floor");
        Check(cubic.Sample(127.5f) == new Vector3(-1, 3.875f, 3), "wrapped four-point stencil");
        Check(cubic.Sample(128.5f) == new Vector3(.5f, 1.375f, -2.875f), "period 128 for positive phase");
        points[1] = new(999);
        Check(cubic.Sample(0) == new Vector3(-2, 4, 1), "source storage is not retained");
        Check(Throws(() => cubic.Sample(float.NaN)) && Throws(() => cubic.Sample(2147483648f)),
              "bounded conversion contract rejects exceptional inputs");

        var state = Water.State.Create(profile, 0xffffU);
        Check(Near(state.Phase, 163.83f) && state.UvAccumulator == 4096, "explicit initial random masked /100");
        state = new(profile, 2, 8191);
        state.AdvanceMilliseconds(1);
        Check(state.UvAccumulator == 8192 && Near(state.Phase, 2.0031546f), "odd delta truncates; 8192 does not wrap");
        state.AdvanceMilliseconds(1);
        Check(state.UvAccumulator == 4097 && Near(state.Phase, 2.0063091f), "strict greater-than wrap");
        state = new(profile, 0, 4096);
        state.AdvanceMilliseconds(10000);
        Check(state.UvAccumulator == 15000 && Near(state.Phase, 31.545742f), "large delta subtracts once, not modulo");
        state = new(profile, 0, 0);
        state.AdvanceMilliseconds(-1);
        Check(state.UvAccumulator == -4097 && Near(state.Phase, -.003154574f), "signed division and unsigned 64-bit threshold");
        state = new(profile, 0, 4096);
        state.AdvanceMilliseconds(int.MaxValue);
        Check(state.UvAccumulator == 1073741822L, "native 32-bit multiply wraps before divide");

        // Ramp points yield component-scaled f(t)=k+1+2t^3-3t^2+2t.
        // At phases 10.25,14.25,18.25,22.25 the X samples BEFORE halving are
        // 11.34375,15.34375,19.34375,23.34375. Other components are 2X and 3X.
        for (int k = 0; k < points.Length; k++) points[k] = new(k, 2 * k, 3 * k);
        var ramp = Water.NativeNoise.FromSamples(points);
        state = new(profile, 10.25f, 0x12349000L);
        var bounds = profile.Bounds(Water.World.Jungle, 1);
        var positions = new Vector3[25]; var uvs = new Vector2[25];
        positions[24] = new(999); uvs[24] = new(999);
        Water.Fill(ramp, state, bounds, 4, positions, uvs);
        Check(Near3(positions[0], new(17, 25.0925f, -15)) &&
              Near3(positions[9], new(25.333334f, 18.2925f, -.7333336f)), "halved cubic + reversed-X/Z height oracle");
        int[] rawU = { -4, 10918, 21841, 32764 };
        int[] rawV = { 28668, -25946, -15023, -4100 };
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
                Check(uvs[j * 4 + i] == new Vector2(rawU[i] / 4096f, rawV[j] / 4096f), $"N4 signed narrowed UV {i},{j}");
        Vector3[] skirts = { new(-13, -.9f, -35), new(73, -.9f, -35),
            new(-13, -.9f, -15), new(73, -.9f, -15), new(35, -.9f, -15),
            new(73, -.9f, -15), new(35, -.9f, -5), new(73, -.9f, -5) };
        float[] skirtV = { 3, 3, -5, -5, 5, 5, -8, -8 };
        for (int k = 0; k < 8; k++)
            Check(Near3(positions[16 + k], skirts[k]) &&
                  uvs[16 + k] == new Vector2((k & 1) == 0 ? -4 : 4, skirtV[k]), $"literal skirt {k}");
        Check(positions[24] == new Vector3(999) && uvs[24] == new Vector2(999) &&
              state.Phase == 10.25f && state.UvAccumulator == 0x12349000L, "Fill leaves tails/state untouched");
        Water.Fill(ramp, new(profile, 10.25f, 65535), bounds, 4, positions, uvs);
        Check(uvs[0].Y == -3f / 4096 && uvs[15].Y == 32765f / 4096, "scroll uses low halfword with signed narrowing");
        var indices = new int[67]; indices[66] = -99;
        Water.FillTriangleIndices(4, indices);
        Check(indices.AsSpan(0, 12).SequenceEqual(new[] { 0, 4, 1, 1, 4, 5, 1, 5, 2, 2, 5, 6 }), "native row-strip diagonals");
        Check(indices.AsSpan(54, 12).SequenceEqual(new[] { 16, 18, 17, 18, 19, 17, 20, 22, 21, 22, 23, 21 }) &&
              indices[66] == -99, "independent upward skirts, untouched index tail");
        for (int n = 4; n <= 16; n++)
        {
            var p = new Vector3[Water.VertexCount(n)]; var uv = new Vector2[p.Length];
            var ix = new int[Water.IndexCount(n)];
            Water.Fill(ramp, state, bounds, n, p, uv); Water.FillTriangleIndices(n, ix);
            bool up = true;
            for (int k = 0; k < ix.Length; k += 3)
                up &= Vector3.Cross(p[ix[k + 1]] - p[ix[k]], p[ix[k + 2]] - p[ix[k]]).Y > 0;
            Check(p.Length == n * n + 8 && ix.Length == 6 * (n - 1) * (n - 1) + 12 && up, $"N{n} counts and +Y winding");
        }
        Check(Throws(() => Water.VertexCount(3)) && Throws(() => Water.IndexCount(17)) &&
              Throws(() => Water.Fill(ramp, state, bounds, 4, new Vector3[23], new Vector2[24])) &&
              Throws(() => Water.FillTriangleIndices(4, new int[65])), "dimension/storage contracts");

        var entry = disc.Files().Single(f => f.Path == "/SLES_500.32");
        byte[] elf = disc.Read(entry.Extent, entry.Size);
        uint U32(int p) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(p, 4));
        int ph = checked((int)U32(28));
        int stride = BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(42, 2));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(44, 2));
        for (int k = 0; k < count; k++)
        {
            int p = ph + k * stride;
            if (U32(p) != 1 || U32(p + 8) > 0x22ce24 || (ulong)U32(p + 8) + U32(p + 16) <= 0x22ce24) continue;
            int offset = checked((int)(U32(p + 4) + 0x22ce24 - U32(p + 8)));
            elf[offset] ^= 1; // change the actual cvt.w.s witness, not a synthetic file header
            Check(Throws(() => Water.Profile.ReadExecutable(elf)), "changed conversion code fails interpretation guard");
            break;
        }
    }
}
