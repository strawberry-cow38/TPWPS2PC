using System.Numerics;

namespace TPW.PS2.Data;

/// <summary>`.aps` -- the PS2 animation format. Every offset here came out of the game's own
/// parser and players, never from inference; see findings/animation.md for the call sites.
///
/// ⚠ There are TWO track formats and one flag bit picks between them (<c>record flags &amp; 0x20</c>).
/// Reading one format's field list against the other's bytes produces fields that are
/// "mysteriously zero", which is the wrong turn this decode took for an hour.</summary>
public sealed class Animation
{
    public const uint Magic = 0x185AA030, Version = 0x148;
    public const int Fps = 30;

    public readonly byte[] D;
    byte U8(int o) => D[o];
    ushort U16(int o) => BitConverter.ToUInt16(D, o);
    short I16(int o) => BitConverter.ToInt16(D, o);
    uint U32(int o) => BitConverter.ToUInt32(D, o);
    float F32(int o) => BitConverter.ToSingle(D, o);

    public Animation(byte[] data)
    {
        D = data;
        if (U32(0) != Magic) throw new InvalidDataException($"not .aps: {U32(0):X8}");
        if (U32(4) != Version) throw new InvalidDataException($"unexpected .aps version {U32(4):X8}");
    }

    /// <summary>⚠ The section count is a BYTE at +0x1C and the table is POINTED TO by +0x20, not at it.</summary>
    public List<(int Count, int Offset)> Sections()
    {
        int n = U8(0x1C), tab = (int)U32(0x20);
        var outList = new List<(int, int)>();
        for (int i = 0; i < n; i++) outList.Add(((int)U32(tab + i * 8), (int)U32(tab + i * 8 + 4)));
        return outList;
    }

    public sealed class Record
    {
        public int Offset; public uint Flags;
        public int TrackCount, SmallCount, IndexCount;
        public int Tracks, Small, Index;
        /// <summary>Bit 0x20 selects 20-byte skeletal tracks over 48-byte ones.</summary>
        public bool Skeletal => (Flags & 0x20) != 0;
    }

    public Record ReadRecord(int r) => new()
    {
        Offset = r, Flags = U32(r), TrackCount = U16(r + 8), SmallCount = U16(r + 0x0A),
        IndexCount = (int)U32(r + 0x0C), Tracks = (int)U32(r + 0x10),
        Small = (int)U32(r + 0x14), Index = (int)U32(r + 0x18),
    };

    public IEnumerable<Record> Records()
    {
        foreach (var (count, off) in Sections())
            for (int i = 0; i < count; i++) yield return ReadRecord(off + i * 0x1C);
    }

    // ---- the four transform channels a node can be animated by ----

    /// <summary>Track flag bits, from <c>FUN_001a7f48</c>.</summary>
    [Flags] public enum TrackFlag : uint
    {
        Rotation = 0x08, Scale = 0x80, OrientAlongPath = 0x400,
        VertexMorph = 0x1000, Unknown0x24 = 0x10000, AlternatePlayer = 0x40000,
    }

    public uint TrackFlags(int track) => U32(track + 4);
    public int TrackNode(int track) => U16(track);
    public int TrackAt(Record rec, int i) => rec.Tracks + i * 0x30;

    /// <summary>Rotation keys: 12 bytes, <c>u16 time, u16 ?, int16 x,y,z,w</c> at 1/32768, SLERPed.
    /// Count is the u16 at <c>track+0x08</c>. 548 of 1,229 tracks -- the format's most common channel.</summary>
    public List<(int Time, Quaternion Q)> Rotation(int track)
    {
        if ((TrackFlags(track) & (uint)TrackFlag.Rotation) == 0) return null;
        int p = (int)U32(track + 0x14), n = U16(track + 8);
        if (p == 0 || n == 0) return null;
        var outList = new List<(int, Quaternion)>(n);
        for (int k = 0; k < n; k++)
        {
            int b = p + k * 12;
            outList.Add((U16(b), new Quaternion(I16(b + 4) / 32768f, I16(b + 6) / 32768f,
                                                I16(b + 8) / 32768f, I16(b + 10) / 32768f)));
        }
        return outList;
    }

    /// <summary>Scale keys: 16 bytes, <c>u16 time</c> then a float3, LINEAR. Count at <c>track+0x0A</c>.
    /// Applied by renormalising each of the matrix's basis vectors to the interpolated length.</summary>
    public List<(int Time, Vector3 S)> Scale(int track)
    {
        if ((TrackFlags(track) & (uint)TrackFlag.Scale) == 0) return null;
        int p = (int)U32(track + 0x18), n = U16(track + 0x0A);
        if (p == 0 || n == 0) return null;
        var outList = new List<(int, Vector3)>(n);
        for (int k = 0; k < n; k++)
            outList.Add((U16(p + k * 16),
                         new Vector3(F32(p + k * 16 + 4), F32(p + k * 16 + 8), F32(p + k * 16 + 12))));
        return outList;
    }

    /// <summary>The Catmull-Rom spline path at <c>track+0x10</c>: float3 control points on a 12-byte
    /// stride, indices taken modulo the point count so the curve wraps.</summary>
    public List<Vector3> SplinePath(int track)
    {
        int o = (int)U32(track + 0x10);
        if (o == 0) return null;
        int npts = U16(o + 4), pts = (int)U32(o + 8);
        if (pts == 0 || npts == 0) return null;
        var outList = new List<Vector3>(npts);
        for (int k = 0; k < npts; k++)
            outList.Add(new Vector3(F32(pts + k * 12), F32(pts + k * 12 + 4), F32(pts + k * 12 + 8)));
        return outList;
    }

    public static Vector3 CatmullRom(IReadOnlyList<Vector3> p, int i, float t)
    {
        int n = p.Count;
        Vector3 a = p[((i - 1) % n + n) % n], b = p[i % n], c = p[(i + 1) % n], e = p[(i + 2) % n];
        return 0.5f * ((-a + 3 * b - 3 * c + e) * t * t * t
                       + (2 * a - 5 * b + 4 * c - e) * t * t + (-a + c) * t + 2 * b);
    }

    /// <summary>Per-vertex MORPH animation at <c>track+0x20</c>.
    ///
    /// ⭐ One 32-bit word is a whole vertex: X = bits 0-9, Y = 10-19, Z = 20-29, each SIGNED 10-bit,
    /// dequantised as <c>value * scale + offset</c>. The game proves the field width itself by
    /// building the node's bounds as <c>offset - 512*scale .. offset + 511*scale</c>.</summary>
    public List<(int[] Times, Vector3[] Keys)> Morph(int track)
    {
        int h = (int)U32(track + 0x20);
        if (h == 0) return null;
        bool byteTimes = (U16(h) & 8) != 0;                     // ⚠ flag bit 0x08 picks the key-time WIDTH
        int nrec = U16(h + 2), ngroup = U16(h + 4);
        var off = new Vector3(F32(h + 0x0C), F32(h + 0x10), F32(h + 0x14));
        var scl = new Vector3(F32(h + 0x18), F32(h + 0x1C), F32(h + 0x20));
        int recs = (int)U32(h + 8), glist = (int)U32(h + 0x24), vdata = (int)U32(h + 0x28);

        var sched = new (int N, int[] T)[nrec];
        for (int k = 0; k < nrec; k++)
        {
            int r = recs + k * 12, n = U16(r), tl = (int)U32(r + 4);
            var t = new int[n];
            for (int j = 0; j < n; j++) t[j] = byteTimes ? U8(tl + j) : U16(tl + j * 2);
            sched[k] = (n, t);
        }
        var outList = new List<(int[], Vector3[])>(ngroup);
        int cur = vdata;
        for (int g = 0; g < ngroup; g++)
        {
            var (n, times) = sched[U16(glist + g * 2)];
            var keys = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                uint v = U32(cur + k * 4);
                keys[k] = new Vector3(Sx10(v, 22) * scl.X + off.X,
                                      Sx10(v, 12) * scl.Y + off.Y,
                                      Sx10(v, 2) * scl.Z + off.Z);
            }
            outList.Add((times, keys));
            cur += n * 4;
        }
        return outList;
    }

    /// <summary><c>(v &lt;&lt; shift) &gt;&gt; 22</c> as the R5900 does it: sign-extend one 10-bit field.</summary>
    static int Sx10(uint v, int shift)
    {
        int x = unchecked((int)(v << shift));
        return x >> 22;
    }

    /// <summary>node -> (appear frame, disappear frame or null), from <c>track+0x28</c>.
    ///
    /// The object is 4 bytes for a part that only appears and **8 for one that also goes away**; in
    /// the 8-byte form the int16 at +0x04 is NEGATIVE and its magnitude is the vanish frame. Crazy
    /// Ape: the crate appears at 28 and disappears at 100 -- the frame the ape bursts out and smashes
    /// it -- and the shards appear at 100 and vanish at 138. 29/29 across the archive.</summary>
    public Dictionary<int, (int Appear, int? Gone)> Visibility(Record rec)
    {
        var ptrs = AllPointers();
        var outMap = new Dictionary<int, (int, int?)>();
        for (int i = 0; i < rec.TrackCount; i++)
        {
            int t = TrackAt(rec, i), p = (int)U32(t + 0x28);
            if (p == 0) continue;
            int appear = U16(p + 2);
            int? gone = null;
            if (ArrayEnd(p, ptrs) - p >= 8 && I16(p + 4) < 0) gone = -I16(p + 4);
            outMap[TrackNode(t)] = (appear, gone);
        }
        return outMap;
    }

    /// <summary>Arrays are laid out contiguously, so the next pointer in the file ends this one.
    /// ⚠ It must be the next pointer of ANY kind -- a rotation array can be followed by a vertex
    /// stream. Agrees with the real count field on 548/548 rotation tracks.</summary>
    public int ArrayEnd(int off, List<int> pointers)
    {
        int i = pointers.BinarySearch(off);
        i = i < 0 ? ~i : i + 1;
        while (i < pointers.Count && pointers[i] <= off) i++;
        return i < pointers.Count ? pointers[i] : D.Length;
    }

    public List<int> AllPointers()
    {
        var set = new HashSet<int>();
        foreach (var (count, off) in Sections())
        {
            if (count == 0) continue;
            set.Add(off);
            for (int i = 0; i < count; i++)
            {
                var rec = ReadRecord(off + i * 0x1C);
                foreach (var v in new[] { rec.Tracks, rec.Small, rec.Index }) if (v != 0) set.Add(v);
                if (rec.Skeletal) continue;
                for (int k = 0; k < rec.TrackCount; k++)
                {
                    int t = rec.Tracks + k * 0x30;
                    for (int j = 0; j < 8; j++) { int q = (int)U32(t + 0x10 + j * 4); if (q != 0) set.Add(q); }
                    int h = (int)U32(t + 0x20);
                    if (h == 0) continue;
                    foreach (var o2 in new[] { 8, 0x24, 0x28 }) { int q = (int)U32(h + o2); if (q != 0) set.Add(q); }
                    int recs = (int)U32(h + 8);
                    for (int j = 0; j < U16(h + 2); j++) { int q = (int)U32(recs + j * 12 + 4); if (q != 0) set.Add(q); }
                }
            }
        }
        var list = set.ToList(); list.Sort(); return list;
    }

    /// <summary>The record's length in frames, across every channel it uses.</summary>
    public int Length(Record rec)
    {
        int max = 0;
        for (int i = 0; i < rec.TrackCount; i++)
        {
            int t = TrackAt(rec, i);
            var rot = Rotation(t); if (rot is { Count: > 0 }) max = Math.Max(max, rot[^1].Time);
            var sca = Scale(t); if (sca is { Count: > 0 }) max = Math.Max(max, sca[^1].Time);
            var mor = Morph(t);
            if (mor != null) foreach (var (times, _) in mor) if (times.Length > 0) max = Math.Max(max, times[^1]);
        }
        return max;
    }
}
