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
        /// <summary>Authored playback duration at record +4; the clock consumes this at
        /// 0x1a8938 and 0x1a8a18. Keys can extend beyond this duration.</summary>
        public int DurationFrames;
        /// <summary>Which SECTION this record came from -- the animation's named slot.</summary>
        public int Slot = -1;
        public string SlotName => SlotNames.TryGetValue(Slot, out var n) ? n : $"slot {Slot}";
        public int TrackCount, SmallCount, IndexCount;
        public int Tracks, Small, Index;
        /// <summary>Bit 0x20 selects 20-byte skeletal tracks over 48-byte ones.</summary>
        public bool Skeletal => (Flags & 0x20) != 0;
        /// <summary>Bit 0x80: THE TRACKS ARE NOT IN THIS FILE. Boy2a/3a/4a each carry 15 records
        /// that declare 22 tracks and a NULL track pointer, and reuse Boy1a's animation -- 15 KB
        /// files against its 68 KB. Measured over all 265 records in DATA.WAD the split is exact:
        /// 0x80 set if and only if <c>Tracks == 0</c>, with nothing on either off-diagonal.</summary>
        public bool Shared => (Flags & 0x80) != 0;
    }

    public Record ReadRecord(int r) => new()
    {
        Offset = r, Flags = U32(r), DurationFrames = checked((int)U32(r + 4)),
        TrackCount = U16(r + 8), SmallCount = U16(r + 0x0A),
        IndexCount = U16(r + 0x0C), Tracks = (int)U32(r + 0x10),
        Small = (int)U32(r + 0x14), Index = (int)U32(r + 0x18),
    };

    public readonly record struct TextureKey(ushort Time, ushort TextureIndex);

    /// <summary>APS record flag 2: material slot, key count, pointer to (u16 time, u16 index).
    /// Consumer 0x1a6b08, dispatched by 0x1a88b8. The similarly placed skeletal/visibility
    /// data is not this channel; a nonzero SmallCount alone is not a texture track.</summary>
    public sealed record TextureTrack(int Material, TextureKey[] Keys)
    {
        /// <summary>Last stored key at or before the truncated nonnegative APS frame.
        /// Before the first key, retain the previous choice (0x1a6b60–0x1a6bd4).</summary>
        public int Sample(float frame, int previous)
        {
            if (!float.IsFinite(frame) || frame < 0) throw new ArgumentOutOfRangeException(nameof(frame));
            for (int i = Keys.Length - 1; i >= 0; i--)
                if (Keys[i].Time <= MathF.Truncate(frame)) return Keys[i].TextureIndex;
            return previous;
        }
    }

    public List<TextureTrack> TextureTracks(Record rec)
    {
        var tracks = new List<TextureTrack>();
        if (rec == null || rec.Skeletal || (rec.Flags & 2) == 0) return tracks;
        if (rec.SmallCount != 0 && (rec.Small <= 0 || (long)rec.Small + rec.SmallCount * 8L > D.Length))
            throw new InvalidDataException($"APS record 0x{rec.Offset:x}: invalid texture track table");
        for (int i = 0; i < rec.SmallCount; i++)
        {
            int entry = rec.Small + i * 8, count = U16(entry + 2);
            int keys = checked((int)U32(entry + 4));
            if (count != 0 && (keys <= 0 || (long)keys + count * 4L > D.Length))
                throw new InvalidDataException($"APS record 0x{rec.Offset:x}, texture track {i}: invalid keys");
            var values = new TextureKey[count];
            for (int j = 0; j < count; j++)
                values[j] = new TextureKey(U16(keys + j * 4), U16(keys + j * 4 + 2));
            tracks.Add(new TextureTrack(U16(entry), values));
        }
        return tracks;
    }

    /// <summary>⭐⭐ THE SECTION INDEX IS A NAMED ANIMATION SLOT, and the names come free from the
    /// ride scripts the disc ships as source: `WAITANIM ANIM_Create 0` in the `.rss` sits against
    /// the operand `0` in the matching `.rse`. Harvested over **352 script pairs, 928 observations,
    /// every symbol resolving to exactly one slot with no disagreement**.
    ///
    /// It reads true on the data: Crazy Ape's build animation is in slot 0 (Create), and the Super
    /// Bog -- a portaloo, which does not build itself -- has NOTHING in slot 0 and its only
    /// animation in slot 5 (Main), which is exactly what its script does.</summary>
    public static readonly Dictionary<int, string> SlotNames = new()
    {
        [0] = "Create", [2] = "Idle", [3] = "Load", [4] = "Start", [5] = "Main",
        [6] = "End", [7] = "Unload", [9] = "Break", [10] = "Repair", [11] = "Other",
    };

    public IEnumerable<Record> Records()
    {
        int slot = 0;
        foreach (var (count, off) in Sections())
        {
            for (int i = 0; i < count; i++)
            {
                var r = ReadRecord(off + i * 0x1C);
                r.Slot = slot;
                yield return r;
            }
            slot++;
        }
    }

    // ---- the four transform channels a node can be animated by ----

    /// <summary>Track flag bits, from <c>FUN_001a7f48</c>.</summary>
    [Flags] public enum TrackFlag : uint
    {
        SplinePath = 0x01, Rotation = 0x08, Scale = 0x80, OrientAlongPath = 0x400,
        VertexMorph = 0x1000, Unknown0x24 = 0x10000, Visibility = 0x20000,
        AlternatePlayer = 0x40000,
    }

    /// <summary>One 20-byte skeletal track: a node, quaternion keys and position keys.</summary>
    public sealed class SkeletalTrack
    {
        public int Node;
        public List<(int Time, Quaternion Q)> Rot = new();
        public List<(int Time, Vector3 P)> Pos = new();
    }

    public int SkeletalTrackAt(Record rec, int i) => rec.Tracks + i * 0x14;

    /// <summary>The 20-byte track form, used by every character in DATA.WAD and by nothing in
    /// JUNGLE.WAD. Rotation keys are 10 bytes (u16 time, int16 x y z w at 1/32768) and position
    /// keys are 8 (u16 time, int16 x y z); the counts are BYTES at +0x08 and +0x09.
    ///
    /// Measured over all 25 character `.aps`: 49,839 rotation keys, every one a unit quaternion
    /// (|q| in 0.99995..1.00001), and all 81,287 key times non-decreasing.</summary>
    public List<SkeletalTrack> SkeletalTracks(Record rec)
    {
        // ⚠ A record with Shared set points at nothing. Reading it walks off the end of the file.
        if (!rec.Skeletal || rec.Tracks == 0) return null;
        var outList = new List<SkeletalTrack>(rec.TrackCount);
        for (int i = 0; i < rec.TrackCount; i++)
        {
            int t = SkeletalTrackAt(rec, i);
            int nr = U8(t + 8), np = U8(t + 9);
            int kr = (int)U32(t + 0x0C), kp = (int)U32(t + 0x10);
            var st = new SkeletalTrack { Node = U16(t) };
            if (kr != 0)
                for (int k = 0; k < nr; k++)
                {
                    int b = kr + k * 10;
                    st.Rot.Add((U16(b), QuatAt(b + 2)));
                }
            if (kp != 0)
                for (int k = 0; k < np; k++)
                {
                    int b = kp + k * 8;
                    st.Pos.Add((U16(b), new Vector3(I16(b + 2), I16(b + 4), I16(b + 6))));
                }
            outList.Add(st);
        }
        return outList;
    }

    /// <summary>⚠⚠ REVERTED 2026-09-21: the order IS <b>x, y, z, w</b>. `TPW_PS2_QUAT=wxyz` selects
    /// the other reading, which is kept only so the experiment can be repeated.
    ///
    /// ⚠⚠⚠ AND THE REASON I GOT IT WRONG IS THE PART WORTH KEEPING. I switched the default to
    /// w,x,y,z on ONE fixed asset plus a control that could not move: Crazy Ape rendered 0 of
    /// 518,400 pixels different either way. **An asset that is INSENSITIVE to a change cannot be
    /// evidence FOR it.** It proves only that the change did not break that asset. The owner had
    /// the viewer open: "it broke pretty much every ride". The right control was a ride that DOES
    /// visibly rotate, and there are hundreds of them.
    ///
    /// The original finding still stands and is still unexplained: the Super Bog's bind matrix is a
    /// +90 pitch about X and its first rotation key, read as x,y,z,w, is a -90 pitch about X, so the
    /// two cancel and the sign lies flat. Something else resolves that -- most likely the rotation
    /// channel's mode bits, 0x10 (2,185 tracks) and 0x40 (565), which gate no pointer and of which
    /// every rotation track carries exactly one and never both.
    ///
    /// The old summary, for the record: both validations this format carried -- |q| == 32767 on the
    ///
    /// ⚠⚠ The order had never been tested. Both validations this format carried -- |q| == 32767 on
    /// the 12-byte keys, and 49,839 unit quaternions on the 20-byte ones -- are invariant under a
    /// PERMUTATION of the four fields, so they proved where the components are and not which is
    /// which. A test that cannot fail on the thing you are claiming is not evidence for it.
    ///
    /// The owner found it from a picture: the Super Bog's sign was "rotated -90 degrees pitch". Its
    /// bind matrix is a +90 pitch about X and its first key read as x,y,z,w is a -90 pitch about X,
    /// so the two cancelled and the sign lay flat. Read as w,x,y,z the same key is a rotation about
    /// Z, the bind's pitch survives, and the sign stands up and spins -- which is what it does.
    ///
    /// Control: Crazy Ape renders **pixel for pixel identical** under either order, so the change
    /// cannot have broken what was already right. `TPW_PS2_QUAT=xyzw` restores the old reading.</summary>
    static readonly bool WFirst =
        (Environment.GetEnvironmentVariable("TPW_PS2_QUAT") ?? "").ToLowerInvariant() == "wxyz";

    Quaternion QuatAt(int o) => WFirst
        ? new Quaternion(I16(o + 2) / 32768f, I16(o + 4) / 32768f, I16(o + 6) / 32768f, I16(o) / 32768f)
        : new Quaternion(I16(o) / 32768f, I16(o + 2) / 32768f, I16(o + 4) / 32768f, I16(o + 6) / 32768f);

    /// <summary>One vertex-group's UV keyframes on the 0x10000 channel.</summary>
    public readonly record struct UvKey(int Time, float U, float V);

    /// <summary>⭐⭐ THE MOVING-TEXTURE CHANNEL, and the port's real one. `TrackFlag` still calls
    /// bit 0x10000 `Unknown0x24` after the payload pointer at <c>track + 0x24</c>, which is the
    /// only non-zero field in a track of this kind.
    ///
    /// Layout, read off the consumer <c>0x1ad378</c> (reached from <c>0x1a7f48</c>, which ends by
    /// testing the track's <c>+4 &amp; 0x10000</c>):
    ///
    /// <code>
    /// t+0x04  u32   entry count
    /// t+0x08  ptr   entries, 4 bytes each: u16 start, u16 keyCount
    /// t+0x10  ptr   key times, u16, indexed by start
    /// t+0x14  ptr   key values, two floats (u,v), indexed by start
    /// </code>
    ///
    /// Sizes close to the byte on `coconut.aps`: 49 entries x 4 runs from 0x12fc exactly to the
    /// times at 0x13c0; 280 times x 2 exactly to the values at 0x15f0; 280 values x 8 to EOF.
    ///
    /// ⭐ An ENTRY is a vertex GROUP, not a vertex: <see cref="Model.UvVertexMap"/> maps the mesh's
    /// vertices onto these entries through the run list at mesh +0x9c.</summary>
    public List<UvKey[]> UvTrack(int track)
    {
        if ((TrackFlags(track) & 0x10000) == 0) return null;
        int h = (int)U32(track + 0x24);
        if (h == 0) return null;
        int n = (int)U32(h + 4);
        int entries = (int)U32(h + 8), times = (int)U32(h + 0x10), values = (int)U32(h + 0x14);
        if (n <= 0 || entries == 0 || times == 0 || values == 0) return null;
        var outList = new List<UvKey[]>(n);
        for (int i = 0; i < n; i++)
        {
            int start = U16(entries + i * 4), count = U16(entries + i * 4 + 2);
            var keys = new UvKey[count];
            for (int k = 0; k < count; k++)
            {
                int j = start + k;
                keys[k] = new UvKey(U16(times + j * 2), F32(values + j * 8), F32(values + j * 8 + 4));
            }
            outList.Add(keys);
        }
        return outList;
    }

    /// <summary>Sample one group's UV at a time in APS frames.
    ///
    /// ⭐ LINEAR, because the consumer is: it finds the first key whose time exceeds now, takes
    /// <c>f = (now - t[k-1]) / (t[k] - t[k-1])</c> and lerps the two UV pairs. There is NO easing
    /// on this channel, unlike the rotation channel's curve index.
    ///
    /// ⚠ Past the last key the consumer falls back to index <c>count-2</c> with f = 1, which is
    /// the last key exactly; that is what the hold below reproduces.</summary>
    public static (float U, float V) SampleUv(UvKey[] keys, float frame)
    {
        if (keys == null || keys.Length == 0) return (0f, 0f);
        for (int k = 0; k < keys.Length; k++)
        {
            if (frame >= keys[k].Time) continue;
            if (k == 0) return (keys[0].U, keys[0].V);
            var a = keys[k - 1]; var b = keys[k];
            int span = b.Time - a.Time;
            if (span <= 0) return (b.U, b.V);
            float f = (frame - a.Time) / span;
            return (a.U + (b.U - a.U) * f, a.V + (b.V - a.V) * f);
        }
        var last = keys[^1];
        return (last.U, last.V);
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
            outList.Add((U16(b), QuatAt(b + 4)));
        }
        return outList;
    }

    /// <summary>⭐⭐ THE ROTATION KEY'S SECOND u16 IS AN EASING CURVE INDEX, and the curves live at
    /// <c>track+0x2c</c> -- which this file has carried as "302 tracks, unexplored" since the format
    /// was cracked. From the evaluator, <c>FUN_001a7f48</c>:
    ///
    /// <code>
    /// e = *(u16*)(keys + i*0xc + 2);
    /// if (e != 0xffff) {
    ///     c = (byte*)track[0x2c] + e*8;      // EIGHT bytes
    ///     x = t * 8.999995;  j = (int)x;  f = x - j;
    ///     lo = j == 0 ? 0.0 : c[min(j,8)-1]/255.0;
    ///     hi = j >= 8 ? 1.0 : c[j]/255.0;
    ///     t  = (1-f)*lo + f*hi;              // and SLERP with THAT
    /// }
    /// </code>
    ///
    /// So a curve is eight bytes read as the interior of a ten-point ramp from 0 to 1 over nine
    /// equal intervals. <c>0xffff</c> means linear. Without it a spin is at the wrong ANGLE for the
    /// frame, which is what the owner saw as the super toilet's sign being placed wrong.</summary>
    public float Ease(int track, int keyIndex, float t)
    {
        int keys = (int)U32(track + 0x14);
        if (keys == 0) return t;
        int e = U16(keys + keyIndex * 12 + 2);
        int table = (int)U32(track + 0x2C);
        if (e == 0xFFFF || table == 0 || table + e * 8 + 8 > D.Length) return t;
        int c = table + e * 8;
        float x = Math.Clamp(t, 0f, 1f) * 8.999995f;
        int j = (int)x;
        float f = x - j;
        float lo = j == 0 ? 0f : D[c + Math.Min(j, 8) - 1] / 255f;
        float hi = j >= 8 ? 1f : D[c + j] / 255f;
        return (1f - f) * lo + f * hi;
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

    /// <summary>A spline path AND the times that drive it -- how a ride moves a car along a curve.
    ///
    /// ⭐ The key stream at <c>+0x0C</c> is plain u32 FRAME TIMES, and how many control points each
    /// key covers is chosen by spline flag <c>0x02</c>. Measured over every path on the disc:
    ///
    /// * flags 0x12 (bit 0x02 SET): <c>points == (keys-1)*3 + 1</c> on <b>855 of 855</b> -- one key
    ///   every THIRD control point.
    /// * flags 0x18 (bit clear): <c>points == keys</c> on all <b>613</b> -- one key per point.
    /// * all <b>1,468</b> paths have ascending key times.
    ///
    /// Nothing else appears, so the two modes are the whole story.</summary>
    public sealed class Path
    {
        public uint Flags;
        public List<Vector3> Points;
        public int[] Times;
        /// <summary>Flag 0x02: cubic BEZIER, each key covering three control points.</summary>
        public bool Bezier => (Flags & 2) != 0;
        /// <summary>Flag 0x08: straight LINES between the control points.</summary>
        public bool Linear => (Flags & 8) != 0;

        /// <summary>⭐⭐ THREE CURVE TYPES, NOT ONE, and the spline's own flags pick between them --
        /// read out of the evaluator's dispatch at <c>FUN_001a7f48</c>:
        ///
        /// <code>
        /// if (spline->flags &amp; 2)      FUN_001ad9a8   // cubic Bezier, index i*3 + 1
        /// else if (spline->flags &amp; 8) FUN_001adda0   // straight LINE, idx -> idx+1
        /// else                           FUN_001ade90   // Catmull-Rom
        /// </code>
        ///
        /// ⚠⚠ Every path in the game was being sampled as Catmull-Rom. On the disc that is **855
        /// Bezier paths and 613 linear ones, and 0 that actually want Catmull-Rom** -- so the curve
        /// was wrong on all 1,468 of them, rounding off corners that should be square and bending
        /// segments that should be straight.
        ///
        /// `FUN_001ad9a8` is the textbook Bernstein cubic, verbatim:
        /// <c>(-p0+3p1-3p2+p3)t^3 + (3p0-6p1+3p2)t^2 + (-3p0+3p1)t + p0</c>, with every index taken
        /// modulo the point count so the curve wraps -- and the caller's <c>i*3 + 1</c> makes
        /// segment j use points 3j..3j+3, the classic chained-Bezier layout that
        /// <c>points == (keys-1)*3 + 1</c> describes exactly.</summary>
        public Vector3 At(float frame)
        {
            int n = Times.Length;
            if (n == 0 || Points.Count == 0) return Vector3.Zero;
            if (n == 1) return Points[0];
            float f = Math.Clamp(frame, Times[0], Times[n - 1]);
            int j = 0;
            while (j < n - 2 && f >= Times[j + 1]) j++;
            int span = Times[j + 1] - Times[j];
            float u = span > 0 ? Math.Clamp((f - Times[j]) / span, 0f, 1f) : 0f;
            if (Bezier) return CubicBezier(Points, j * 3 + 1, u);
            if (Linear)
            {
                int c = Points.Count;
                return Vector3.Lerp(Points[((j % c) + c) % c], Points[(((j + 1) % c) + c) % c], u);
            }
            return CatmullRom(Points, j, u);
        }

        /// <summary>The direction of travel at a frame, for the orient-along-path channel.</summary>
        public Vector3 Tangent(float frame)
        {
            var a = At(frame);
            var b = At(frame + 0.1f);
            var d = b - a;
            if (d.LengthSquared() < 1e-12f) { d = At(frame) - At(frame - 0.1f); }
            return d.LengthSquared() < 1e-12f ? Vector3.UnitZ : Vector3.Normalize(d);
        }
    }

    public Path SplineAt(int track)
    {
        int o = (int)U32(track + 0x10);
        if (o == 0) return null;
        int npts = U16(o + 4), nkeys = U16(o + 6);
        int pts = (int)U32(o + 8), keys = (int)U32(o + 0x0C);
        if (pts == 0 || npts == 0 || keys == 0 || nkeys == 0) return null;
        var p = new Path { Flags = U32(o), Points = new List<Vector3>(npts), Times = new int[nkeys] };
        for (int k = 0; k < npts; k++)
            p.Points.Add(new Vector3(F32(pts + k * 12), F32(pts + k * 12 + 4), F32(pts + k * 12 + 8)));
        for (int k = 0; k < nkeys; k++) p.Times[k] = (int)U32(keys + k * 4);
        return p;
    }

    /// <summary>The Bernstein cubic the game uses, with the same wrap. <c>i</c> is the caller's
    /// already-scaled index, so the control points are <c>i-1, i, i+1, i+2</c>.</summary>
    public static Vector3 CubicBezier(IReadOnlyList<Vector3> p, int i, float t)
    {
        int n = p.Count;
        Vector3 At(int k) => p[((k % n) + n) % n];
        Vector3 p0 = At(i - 1), p1 = At(i), p2 = At(i + 1), p3 = At(i + 2);
        return (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t
             + (3 * p0 - 6 * p1 + 3 * p2) * t * t
             + (-3 * p0 + 3 * p1) * t + p0;
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
        // ⚠⚠ `track+0x20` IS A UNION AND THE FLAGS PICK THE MEANING, exactly like the record-level
        // 0x20 bit. Gating on VertexMorph costs nothing in the rides -- across JUNGLE.WAD's 1,229
        // tracks the flag is set if and only if the pointer is present, 290 and 939, nothing on
        // either off-diagonal -- but 81 character tracks carry a pointer here under flag 0x40000
        // (AlternatePlayer) and are NOT morph headers. Reading one walks off the end of the file.
        if ((TrackFlags(track) & (uint)TrackFlag.VertexMorph) == 0) return null;
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

    /// <summary>node -> its VISIBILITY TIMELINE, from <c>track+0x28</c>.
    ///
    /// ⭐⭐ Read out of the evaluator, <c>FUN_001a7f48</c>: it is not an appear/disappear pair, it
    /// is a LIST of <c>track+0x0C</c> signed int16 frame times. The game scans it BACKWARDS for the
    /// last entry whose magnitude is at or before the current frame, then sets the node's hidden
    /// bit if that entry is &lt;= 0 and clears it if it is positive:
    ///
    /// <code>
    /// if ((short)*found &lt; 1) node->state |= 0x10; else node->state &amp;= ~0x10;
    /// </code>
    ///
    /// Crazy Ape reads back exactly right: <c>m_crate [0, 28, -100]</c> and
    /// <c>m_shards [0, 100, -138]</c> -- the crate arrives at 28 and is smashed at 100, the shards
    /// fly at 100 and are gone by 138.
    ///
    /// ⚠⚠ Over the whole disc: 2,033 timelines, whose magnitudes ASCEND on all 2,033 and whose
    /// signs ALTERNATE on all 2,033 -- so it really is a show/hide timeline. **157 of them have
    /// more than three entries** (up to 25), and the old reader could only hold one appear and one
    /// disappear, so every one of those parts blinked on and off at the wrong times.</summary>
    public Dictionary<int, int[]> Visibility(Record rec)
    {
        var outMap = new Dictionary<int, int[]>();
        for (int i = 0; i < rec.TrackCount; i++)
        {
            int t = TrackAt(rec, i);
            if ((TrackFlags(t) & (uint)TrackFlag.Visibility) == 0) continue;
            int p = (int)U32(t + 0x28), n = U16(t + 0x0C);
            if (p == 0 || n == 0 || p + n * 2 > D.Length) continue;
            var list = new int[n];
            for (int k = 0; k < n; k++) list[k] = I16(p + k * 2);
            outMap[TrackNode(t)] = list;
        }
        return outMap;
    }

    /// <summary>Whether a timeline shows its node at a frame. Before the first entry applies, the
    /// node holds the state that entry is about to change it OUT of.</summary>
    public static bool VisibleAt(int[] timeline, float now)
    {
        if (timeline == null || timeline.Length == 0) return true;
        for (int k = timeline.Length - 1; k >= 0; k--)
            if (Math.Abs(timeline[k]) <= now) return timeline[k] > 0;
        return timeline[0] <= 0;
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
        // ⚠ The 20-byte form must not be walked with the 48-byte stride. Branch FIRST: the old
        // code read a skeletal record's tracks at +0x30 intervals, threw, and the caller's blanket
        // catch turned that into a NullReferenceException three frames away.
        if (rec.Skeletal)
        {
            foreach (var t in SkeletalTracks(rec) ?? new List<SkeletalTrack>())
            {
                if (t.Rot.Count > 0) max = Math.Max(max, t.Rot[^1].Time);
                if (t.Pos.Count > 0) max = Math.Max(max, t.Pos[^1].Time);
            }
            return max;
        }
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
