using System.Buffers.Binary;
using System.Text;

namespace TPW.PS2.Data;

/// <summary>ISO-level ride .ENG sound layers and sampled volume curves. Consumer: 0x2426c0.
/// Supports the shipped variant (no additional parameter groups). Unknowns are preserved;
/// input units, the second interpolated parameter's units, and HeaderHigh are not invented.</summary>
public sealed class RideEngine
{
    public ushort HeaderHigh { get; }
    public IReadOnlyList<Layer> Layers { get; }
    public IReadOnlyList<VolumeCurve> Curves { get; }
    public IReadOnlyList<ExportSlot> ExportSlots { get; }
    public uint AdditionalParameterCount { get; }

    public sealed record Layer(int Start, int End, int VolumeStart, int VolumeEnd,
        int ParameterStart, int ParameterEnd, int SoundId, int StoredBankId, byte Channel);
    public sealed record VolumeCurve(int Start, int End, int LayerIndex, ReadOnlyMemory<byte> Samples);
    public sealed record ExportSlot(uint Present, string Directory, string Name);
    public sealed record LayerState(int LayerIndex, byte Channel, int SoundId, int VolumePercent,
        int Parameter, byte CurvePercent, int Volume127);

    public RideEngine(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        int p = 0;
        void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidDataException("ENG: " + message);
        }
        byte[] Bytes(int n)
        {
            Require(n >= 0 && n <= data.Length - p, "truncated field");
            var value = data.AsSpan(p, n).ToArray(); p += n; return value;
        }
        uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
        int I32() => unchecked((int)U32());
        int Count(int stride)
        {
            uint n = U32();
            Require(n <= (data.Length - p) / stride, "count exceeds remaining bytes");
            return (int)n;
        }
        string String()
        {
            uint n = U32();
            Require(n <= data.Length - p, "string exceeds remaining bytes");
            return Encoding.Latin1.GetString(Bytes((int)n));
        }
        uint header = U32();
        HeaderHigh = (ushort)(header >> 16);
        int count = (ushort)header;
        Require(count <= (data.Length - p) / 33, "layer count exceeds remaining bytes");
        var layers = new Layer[count];
        for (int i = 0; i < count; i++)
        {
            var layer = new Layer(I32(), I32(), I32(), I32(), I32(), I32(), I32(), I32(), Bytes(1)[0]);
            Require(layer.Start < layer.End, "empty/reversed layer interval");
            Require(layer.Channel < 2, "channel exceeds the consumer's two slots");
            Require(layer.VolumeStart is >= 0 and <= 100 && layer.VolumeEnd is >= 0 and <= 100,
                "unsupported volume percentage");
            layers[i] = layer;
        }
        var curves = new VolumeCurve[Count(44)];
        for (int i = 0; i < curves.Length; i++)
        {
            int start = I32(), end = I32(), layer = I32();
            byte[] samples = Bytes(32);
            Require(start < end && layer >= 0 && layer < count, "invalid curve interval/reference");
            Require(samples.All(x => x <= 100), "unsupported curve percentage");
            curves[i] = new(start, end, layer, samples);
        }
        var slots = new ExportSlot[Count(4)];
        for (int i = 0; i < slots.Length; i++)
        {
            uint present = U32();
            slots[i] = present == 0 ? new(0, null, null) : new(present, String(), String());
        }
        AdditionalParameterCount = U32();
        Require(AdditionalParameterCount == 0, "additional parameter groups are not yet supported");
        Require(p == data.Length, "unexpected trailing bytes");
        Layers = Array.AsReadOnly(layers); Curves = Array.AsReadOnly(curves);
        ExportSlots = Array.AsReadOnly(slots);
    }

    /// <summary>0x241b28: default 100%, scan curves in file order, inclusive intervals,
    /// floor((input-start)*32/(end-start)), clamp to 31. Stops after two matching curves.
    /// This evaluates the supplied input; the caller's minimum-input clamp is separate.</summary>
    public byte[] EvaluateCurvePercent(int input)
    {
        byte[] output = { 100, 100 };
        int matched = 0;
        foreach (var curve in Curves)
        {
            if (input < curve.Start || input > curve.End) continue;
            int sample = (int)Math.Min(31, ((long)input - curve.Start) * 32 / ((long)curve.End - curve.Start));
            output[Layers[curve.LayerIndex].Channel] = curve.Samples.Span[sample];
            if (++matched == 2) break;
        }
        return output;
    }

    /// <summary>Layer evaluation for the shipped 0..1000 intervals, with unity body gain.
    /// The second interpolated parameter is deliberately unnamed. Sound-bank IDs are rebound
    /// by 0x243540, so StoredBankId is not a global bank-group identity.</summary>
    public IReadOnlyList<LayerState> Evaluate(int input)
    {
        var curve = EvaluateCurvePercent(input);
        var result = new List<LayerState>();
        for (int i = 0; i < Layers.Count && result.Count < 2; i++)
        {
            var layer = Layers[i];
            if (input < layer.Start || input > layer.End || layer.SoundId == 0 || layer.StoredBankId == 0) continue;
            int Lerp(int start, int end) => checked((int)(start + ((long)end - start) *
                ((long)input - layer.Start) / ((long)layer.End - layer.Start)));
            int volume = Lerp(layer.VolumeStart, layer.VolumeEnd);
            byte percent = curve[layer.Channel];
            result.Add(new(i, layer.Channel, layer.SoundId, volume,
                Lerp(layer.ParameterStart, layer.ParameterEnd), percent, Math.Min(127, volume * 127 / 100) * percent / 100));
        }
        return result.AsReadOnly();
    }
}
