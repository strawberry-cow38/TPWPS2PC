using System.Numerics;

namespace TPW.PS2.Data;

/// <summary>APS track 0x200: frame-sampled percentages of a curve owned by the MPS.
/// Loader 169AA0/168728; parent binding 1A82C8/1A83AC; sampling 1AE450/1A8444.
/// This is NOT the inline, time-keyed spline at track+0x10.</summary>
public sealed class ModelPathChannel
{
    public uint CurveFlags { get; }
    public uint TrackFlags { get; }
    public IReadOnlyList<Vector3> Points { get; }
    public IReadOnlyList<float> Progress { get; }
    public int Duration => Progress.Count;
    public bool Closed => (CurveFlags & 1) != 0;
    public int SpanCount => ((CurveFlags & 2) != 0 ? Points.Count / 3 : Points.Count) - (Closed ? 0 : 1);
    public bool AddTranslation { get; }

    public ModelPathChannel(uint curveFlags, uint trackFlags, Vector3[] points, float[] progress, bool addTranslation = false)
    {
        CurveFlags = curveFlags; TrackFlags = trackFlags;
        Points = Array.AsReadOnly((Vector3[])points.Clone());
        Progress = Array.AsReadOnly((float[])progress.Clone());
        AddTranslation = addTranslation;
        if (SpanCount < 1 || Duration < 1 || points.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
            || progress.Any(p => !float.IsFinite(p))) throw new InvalidDataException("invalid model-path curve/progress");
    }

    static void Range(byte[] data, int offset, long bytes)
    {
        if (offset < 0 || bytes < 0 || offset + bytes > data.Length)
            throw new InvalidDataException("model-path pointer/count outside resource");
    }
    static uint Word(byte[] data, int at) { Range(data, at, 4); return BitConverter.ToUInt32(data, at); }
    static int Pointer(byte[] data, int at) => checked((int)Word(data, at));
    static ushort Half(byte[] data, int at) { Range(data, at, 2); return BitConverter.ToUInt16(data, at); }

    public static ModelPathChannel Read(Model model, Animation animation, Animation.Record record, int track)
    {
        if (record.Skeletal || model.IsLegacyMd2) return null;
        uint flags = animation.TrackFlags(track);
        // Inline path wins if both flags are present (1A82EC).
        if ((flags & 0x201) != 0x200) return null;
        int node = animation.TrackNode(track);
        int parent = model.NodeParent(node);
        if (parent < 0) return null;
        int parentOffset = model.NodeOffset(parent);
        if ((Word(model.D, parentOffset) & 8) == 0) return null;
        int index = Half(model.D, parentOffset + 0x52), count = Half(model.D, 0x2c);
        if (index >= count) throw new InvalidDataException("model-path parent index outside curve table");
        int table = Pointer(model.D, 0x78);
        if (table == 0) throw new InvalidDataException("missing model-path table");
        Range(model.D, table, count * 16L);
        int curve = table + index * 16, n = Half(model.D, curve + 4), pts = Pointer(model.D, curve + 8);
        if (pts == 0) throw new InvalidDataException("missing model-path points");
        Range(model.D, pts, n * 12L);
        var points = new Vector3[n];
        for (int i = 0; i < n; i++) points[i] = new(BitConverter.ToSingle(model.D, pts + i * 12),
            BitConverter.ToSingle(model.D, pts + i * 12 + 4), BitConverter.ToSingle(model.D, pts + i * 12 + 8));
        int indirect = Pointer(animation.D, track + 0x1c);
        if (indirect == 0) throw new InvalidDataException("missing model-path progress indirection");
        int samples = Pointer(animation.D, indirect);
        if (samples == 0) throw new InvalidDataException("missing model-path progress samples");
        Range(animation.D, samples, record.DurationFrames * 4L);
        var progress = new float[record.DurationFrames];
        for (int i = 0; i < progress.Length; i++) progress[i] = BitConverter.ToSingle(animation.D, samples + i * 4);
        return new ModelPathChannel(Word(model.D, curve), flags, points, progress, (Word(model.D, 0x1c) & 4) != 0);
    }

    /// <summary>1AE450: float samples, shortest percentage wrap, no extra endpoint sample.</summary>
    public float Percentage(float frame)
    {
        if (!float.IsFinite(frame) || frame < 0 || frame > Duration) throw new ArgumentOutOfRangeException(nameof(frame));
        int i = (int)frame; float t = frame - i;
        if (i == Duration) { i--; t++; }
        int next = Closed ? (i + 1) % Duration : Math.Min(i + 1, Duration - 1);
        float a = Progress[i], b = Progress[next];
        bool wrap = MathF.Abs(a - b) > 50;
        if (wrap) { if (a < b) a += 100; else b += 100; }
        float value = (1 - t) * a + t * b;
        return wrap ? value % 100 : value;
    }

    public (Vector3 Position, Vector3 Direction) Sample(float frame)
    {
        float u = SpanCount * ((Percentage(frame) + 1000) % 100) / 100;
        int span = (int)u; float t = u - span;
        Vector3 P(int i) => Points[(i % Points.Count + Points.Count) % Points.Count];
        if ((CurveFlags & 2) != 0)
        {
            int i = span * 3;
            Vector3 a = P(i), b = P(i + 1), c = P(i + 2), d = P(i + 3);
            // 1ADBB0: exact derivative of the Bernstein polynomial, not a frame difference.
            var direction = 3 * (-a + 3 * b - 3 * c + d) * t * t
                          + 2 * (3 * a - 6 * b + 3 * c) * t + (-3 * a + 3 * b);
            return (Animation.CubicBezier(Points, i + 1, t), direction);
        }
        if ((CurveFlags & 8) != 0) return (Vector3.Lerp(P(span), P(span + 1), t), P(span + 1) - P(span));
        // Native fallback 1A86A8 calls the position evaluator at t+0.1 WITHOUT subtracting
        // the current position. Preserve that call, rather than repair it to a derivative.
        return (Animation.CatmullRom(Points, span, t), Animation.CatmullRom(Points, span, t + .1f));
    }

    /// <summary>1A85DC translation; 1A8730/1A6508 orientation. Facing writes UNIT axes;
    /// unlike the old unused viewer facing code, the native call does not restore bind scale.</summary>
    public Matrix4x4 Apply(Matrix4x4 local, float frame)
    {
        // Native caller skips this channel outside the authored duration.
        if (frame < 0 || frame > Duration) return local;
        var (position, direction) = Sample(frame);
        local.Translation = AddTranslation ? local.Translation + position : position;
        if ((TrackFlags & 0x400) == 0) return local;
        Vector3 up = (TrackFlags & 8) != 0 ? new(local.M21, local.M22, local.M23) : Vector3.UnitY;
        var forward = Vector3.Normalize(direction);
        var right = Vector3.Normalize(Vector3.Cross(up, forward));
        up = Vector3.Cross(forward, right);
        local.M11 = right.X; local.M12 = right.Y; local.M13 = right.Z;
        local.M21 = up.X; local.M22 = up.Y; local.M23 = up.Z;
        local.M31 = forward.X; local.M32 = forward.Y; local.M33 = forward.Z;
        return local;
    }
}
