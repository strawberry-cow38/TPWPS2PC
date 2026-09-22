using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace TPW.PS2.Data;

/// <summary>The PAL executable's ordinary mesh light and clear-weather colours.
/// Values are read from the owner's ELF, not fitted to a render. See findings/lighting.md.</summary>
public sealed record Lighting(Vector3 Ambient, Vector3 Directional, Vector3 RayDirection,
                              Vector3 AmbientWeatherDelta, Vector3 DirectionalWeatherDelta,
                              float WeatherScale)
{
    public static Lighting Read(Disc disc)
    {
        var entry = disc.Files().Single(f => f.Path == "/SLES_500.32");
        return ReadExecutable(disc.Read(entry.Extent, entry.Size));
    }

    public static Lighting ReadExecutable(byte[] elf)
    {
        if (elf.Length < 52 || !elf.AsSpan(0, 6).SequenceEqual(new byte[] { 127, 69, 76, 70, 1, 1 }))
            throw new InvalidDataException("Lighting requires a little-endian ELF32 executable");
        uint U32(int off) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(off, 4));
        int U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(off, 2));
        int Offset(uint va, int size = 4)
        {
            int ph = checked((int)U32(28));
            for (int i = 0; i < U16(44); i++)
            {
                int p = ph + i * U16(42);
                if (U32(p) == 1 && va >= U32(p + 8) && (ulong)va + (uint)size <= (ulong)U32(p + 8) + U32(p + 16))
                    return checked((int)(U32(p + 4) + va - U32(p + 8)));
            }
            throw new InvalidDataException($"Lighting address 0x{va:x} is not in PT_LOAD");
        }
        // These hashes guard the interpretation, including the stores and the weather equation.
        // A different executable must be investigated, never silently assigned this build's light.
        void Guard(uint address, int length, string sha)
        {
            var actual = Convert.ToHexString(SHA256.HashData(elf.AsSpan(Offset(address, length), length)));
            if (!actual.Equals(sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unsupported lighting code at EE 0x{address:x}");
        }
        Guard(0x229ab0, 0x310, "0744db0b0a0b55b520d59f3dfd89ce7b7c9255bd0e727d9d5a466e6dd12eeb3f");
        Guard(0x23ec58, 0xb4, "057257cf7079ae543ce17e5dd95429b08e4a0f85432087ad05dd6ab616c26f5d");
        Guard(0x23f218, 0x17c, "932b20e62e7b15c0b7348390af13b7a10094b19d5e605e0ccf8530f4294a803d");
        Guard(0x227d98, 0x15c, "ac0c077709715d5006508afab27b078d6f319c5c306b6b8bba165bffd74d8eb1");
        Guard(0x22a788, 0x208, "606ba2123183c0328810e54eaa9fffe08faa7d98e6320f2974ecab903abe99ba");
        Guard(0x21a214, 0xa8, "a21ba2d0ad37b72bb0c8937fc1392a3d727a1428a3431b9b64cce0e2a51d8058");
        Guard(0x2a4420, 0x800, "152b46f596c7aacd4dfc6903bf2959ca10f037d0e8aec09ee9129353a7e2e3e7");
        Guard(0x2a4c28, 0x800, "623b5a76186c9ddc435ea88fb5990d21c4c06f899f66eb3bdc2e51a8bcb1d2e8");
        float Immediate(uint va)
        {
            uint hi = U32(Offset(va)), lo = U32(Offset(va + 4));
            if ((hi & 0xffff0000) != 0x3c010000) throw new InvalidDataException("Expected LUI $at");
            uint bits = (hi & 0xffff) << 16;
            if ((lo & 0xffff0000) == 0x34210000) bits |= lo & 0xffff;
            return BitConverter.UInt32BitsToSingle(bits);
        }
        // Constructor preserves the position flag in bit 0 of X/Y. Its source (0.6) has bit 0 clear.
        float ClearFlag(float value) => BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits(value) & ~1u);
        return new Lighting(new(Immediate(0x23ec64)), new(Immediate(0x23ecb4)),
            new(ClearFlag(Immediate(0x229b9c)), ClearFlag(Immediate(0x229bc8)), Immediate(0x229b20)),
            new(Immediate(0x23ecd0)), new(Immediate(0x23ecec)), Immediate(0x23f23c));
    }

    /// <summary>The weather controller's +0x64 state, in [0,1]. The viewer chooses clear (0)
    /// until it has the game's weather simulation; rain sprite visibility is not this state.</summary>
    public Lighting AtWeather(float amount)
    {
        if (!float.IsFinite(amount) || amount < 0 || amount > 1) throw new ArgumentOutOfRangeException(nameof(amount));
        float t = amount * WeatherScale, darkening = 2 * t - t * t;
        return this with { Ambient = Ambient - AmbientWeatherDelta * darkening,
                           Directional = Directional - DirectionalWeatherDelta * darkening };
    }

    /// <summary>EE 0x227d98 transforms the ray by the model basis, then normalises THAT vector.
    /// VU L009e consumes signed bytes without normalising the vertex normal.</summary>
    public Vector3 VertexColour(Vector3 rawNormal, Matrix4x4 modelToGame)
    {
        var localRay = Vector3.TransformNormal(RayDirection, Matrix4x4.Transpose(modelToGame));
        localRay = localRay.LengthSquared() > 0 ? Vector3.Normalize(localRay) : Vector3.Zero;
        float lambert = MathF.Max(0, Vector3.Dot(rawNormal, -localRay));
        var rgb = Vector3.Min(new(255), Ambient * 128 + Directional * lambert);
        return new(MathF.Truncate(rgb.X), MathF.Truncate(rgb.Y), MathF.Truncate(rgb.Z));
    }

    public static byte Modulate(byte texel, float vertexColour) =>
        (byte)Math.Clamp((int)(texel * vertexColour / 128), 0, 255);
}
