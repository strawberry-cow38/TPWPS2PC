using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace TPW.PS2.Data;

/// <summary>Bounds checks shared by the legacy MD2 and MTR readers.</summary>
internal sealed class CheckedBinary(byte[] data, string format)
{
    public byte[] Data { get; } = data ?? throw new ArgumentNullException(nameof(data));
    public void Range(int offset, long size)
    {
        if (offset < 0 || size < 0 || (long)offset + size > Data.Length)
            throw new InvalidDataException($"{format}: range 0x{offset:X}+{size} outside {Data.Length} bytes");
    }
    public ushort U16(int o) { Range(o, 2); return BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(o)); }
    public uint U32(int o) { Range(o, 4); return BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(o)); }
    public int Int(int o)
    {
        uint v = U32(o);
        if (v > int.MaxValue) throw new InvalidDataException($"{format}: integer at 0x{o:X} exceeds supported range");
        return (int)v;
    }
    public int Table(int field, int count, int stride)
    {
        int p = Int(field);
        if (count > 0 && p == 0) throw new InvalidDataException($"{format}: null table at 0x{field:X}");
        Range(p, (long)count * stride);
        return p;
    }
    public float Float(int o)
    {
        float v = BitConverter.UInt32BitsToSingle(U32(o));
        if (!float.IsFinite(v)) throw new InvalidDataException($"{format}: nonfinite float at 0x{o:X}");
        return v;
    }
    public string Name(int o, int size)
    {
        Range(o, size);
        int end = Array.IndexOf(Data, (byte)0, o, size);
        if (end <= o) throw new InvalidDataException($"{format}: empty or unterminated name at 0x{o:X}");
        return Encoding.Latin1.GetString(Data, o, end - o);
    }
    public Matrix4x4 Matrix(int o) => new(
        Float(o), Float(o+4), Float(o+8), Float(o+12),
        Float(o+16), Float(o+20), Float(o+24), Float(o+28),
        Float(o+32), Float(o+36), Float(o+40), Float(o+44),
        Float(o+48), Float(o+52), Float(o+56), Float(o+60));
}
