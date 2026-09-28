#nullable enable
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

// Assets are supplied by the parent and must remain immutable, including Animation.D/Record fields.
// Full APS hashing includes tracks and keys, not merely durations: equal-length replacements are not equivalent.
internal static class NativeAnimationSnapshotAssets
{
    internal static void Identity(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 256) throw new InvalidDataException("invalid presentation provider identity");
    }
    internal static void Match(string saved, string actual)
    {
        if (saved == null || saved.Length != 64 || saved != actual) throw new InvalidDataException("presentation asset fingerprint mismatch");
    }
    internal static List<(int Count, int Offset)> Sections(Animation a)
    {
        ArgumentNullException.ThrowIfNull(a);
        byte[] d = a.D;
        if (d == null || d.Length < 0x24 || d.Length > 256 * 1024 * 1024)
            throw new InvalidDataException("invalid APS size");
        uint tab = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x20, 4));
        if ((ulong)tab + (uint)d[0x1c] * 8 > (ulong)d.Length) throw new InvalidDataException("invalid APS section table");
        var result = a.Sections();
        long total = 0;
        foreach (var (count, offset) in result)
            if (count < 0 || (total += count) > 65536 || offset < 0 ||
                (count != 0 && (long)offset + (long)count * 0x1c > d.Length))
                throw new InvalidDataException("invalid APS record table");
        return result;
    }
    internal static string Fingerprint(Animation a)
    {
        _ = Sections(a);
        return Convert.ToHexString(SHA256.HashData(a.D));
    }
    internal static string Fingerprint(NativeLogicalAnimationTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        using var bytes = new MemoryStream();
        using var w = new BinaryWriter(bytes);
        for (int i = 0; i < NativeLogicalAnimationTable.LogicalCount; i++)
        {
            var row = table.Variants(i); w.Write(row.Count);
            foreach (var d in row)
            {
                w.Write(d.FirstSlot); w.Write(d.FirstVariant); w.Write(d.MainSlot); w.Write(d.MainVariant);
                w.Write(d.LastSlot); w.Write(d.LastVariant); w.Write(d.Flags); w.Write(d.Weight);
            }
        }
        foreach (int idle in table.IdleStates) w.Write(idle);
        w.Flush();
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }
    internal static Animation.Record Record(Animation a, int slot, int variant)
    {
        var sections = Sections(a);
        if ((uint)slot >= (uint)sections.Count || (uint)variant >= (uint)sections[slot].Count)
            throw new InvalidDataException("presentation record index outside supplied APS");
        int offset = checked(sections[slot].Offset + variant * 0x1c);
        if (BinaryPrimitives.ReadUInt32LittleEndian(a.D.AsSpan(offset + 4, 4)) > int.MaxValue)
            throw new InvalidDataException("invalid APS duration");
        var r = a.ReadRecord(offset); r.Slot = slot;
        return r;
    }
    internal static void SameRecord(Animation.Record a, Animation.Record b)
    {
        if (a.Offset != b.Offset || a.Slot != b.Slot || a.Flags != b.Flags || a.DurationFrames != b.DurationFrames ||
            a.TrackCount != b.TrackCount || a.SmallCount != b.SmallCount || a.IndexCount != b.IndexCount ||
            a.Tracks != b.Tracks || a.Small != b.Small || a.Index != b.Index)
            throw new InvalidDataException("mutable record no longer matches supplied immutable APS");
    }
}

public sealed partial class NativeLogicalAnimationControl
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot
    {
        public required int Version { get; init; }
        public required string TableFingerprint { get; init; }
        public required byte Current { get; init; }
        public required byte Variant { get; init; }
        public required byte Phase { get; init; }
        public required byte Pending { get; init; }
    }
    public Snapshot CaptureState() => new() { Version = 1,
        TableFingerprint = NativeAnimationSnapshotAssets.Fingerprint(table),
        Current = Current, Variant = Variant, Phase = Phase, Pending = Pending };

    public static void ValidateState(Snapshot s, NativeLogicalAnimationTable table)
    {
        ArgumentNullException.ThrowIfNull(s);
        NativeAnimationSnapshotAssets.Match(s.TableFingerprint, NativeAnimationSnapshotAssets.Fingerprint(table));
        if (s.Version != 1 || s.Phase > 2 ||
            (s.Current != None && (s.Current >= NativeLogicalAnimationTable.LogicalCount || s.Variant >= table.Variants(s.Current).Count)) ||
            (s.Pending != None && s.Pending >= NativeLogicalAnimationTable.LogicalCount))
            throw new InvalidDataException("invalid logical animation control");
        // Current=None can retain a previously selected variant after leaving phase 2.
        if (s.Current == None && (s.Phase != 0 || s.Variant >= Enumerable.Range(0, NativeLogicalAnimationTable.LogicalCount).Max(i => table.Variants(i).Count)))
            throw new InvalidDataException("invalid inactive logical animation control");
    }
    public static NativeLogicalAnimationControl FromState(Snapshot s, NativeLogicalAnimationTable table)
    {
        ValidateState(s, table);
        return new NativeLogicalAnimationControl(table) { Current = s.Current, Variant = s.Variant, Phase = s.Phase, Pending = s.Pending };
    }
}

public sealed partial class NativeGuestAnimation
{
    /// <summary>Explicit fresh staged providers. Duration is an APS lookup (not a mutable duration source).
    /// RNG is EXTERNAL and may be shared by many models: restore it once at its owning scope using
    /// NewlibRand.RestoreState, then supply that same fresh object to every model and to Update(..., random.Next).
    /// Restore verifies its CURRENT state without drawing or rewinding it. IdlePick's guest RNG remains external.</summary>
    public sealed class SnapshotBindings
    {
        public required Animation Animation { get; init; }
        public required NativeLogicalAnimationTable Table { get; init; }
        public required string DurationId { get; init; }
        public required Func<int, int, int?> Duration { get; init; }
        public required string RandomId { get; init; }
        public required NewlibRand Random { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RecordIndex
    {
        public required int Slot { get; init; }
        public required int Variant { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot
    {
        public required int Version { get; init; }
        public required string AnimationFingerprint { get; init; }
        public required string DurationId { get; init; }
        public required string RandomId { get; init; }
        public required uint RandomState { get; init; }
        public required NativeLogicalAnimationControl.Snapshot Control { get; init; }
        public required int Requested { get; init; }
        public required int Stamp { get; init; }
        public required int Slot { get; init; }
        public required int Variant { get; init; }
        public required float Frame { get; init; }
        public required float Duration { get; init; }
        public required RecordIndex? Held { get; init; }
        public required int Boundaries { get; init; }
    }

    public Snapshot CaptureState(SnapshotBindings b)
    {
        CheckBindings(b);
        // Do not ask Duration to identify records: callbacks may observe or mutate the live world.
        var s = new Snapshot { Version = 1, AnimationFingerprint = NativeAnimationSnapshotAssets.Fingerprint(b.Animation),
            DurationId = b.DurationId, RandomId = b.RandomId, RandomState = b.Random.CaptureState(),
            Control = Control.CaptureState(), Requested = Requested, Stamp = Stamp, Slot = Slot, Variant = Variant,
            Frame = Frame, Duration = Duration, Held = Held is { } h ? new RecordIndex { Slot = h.Slot, Variant = h.Variant } : null,
            Boundaries = Boundaries };
        ValidateState(s, b);
        return s;
    }
    static void CheckBindings(SnapshotBindings b)
    {
        ArgumentNullException.ThrowIfNull(b);
        NativeAnimationSnapshotAssets.Identity(b.DurationId); NativeAnimationSnapshotAssets.Identity(b.RandomId);
        ArgumentNullException.ThrowIfNull(b.Duration); ArgumentNullException.ThrowIfNull(b.Random);
        ArgumentNullException.ThrowIfNull(b.Animation); ArgumentNullException.ThrowIfNull(b.Table);
    }
    public static void ValidateState(Snapshot s, SnapshotBindings b)
    {
        ArgumentNullException.ThrowIfNull(s); CheckBindings(b);
        NativeAnimationSnapshotAssets.Identity(s.DurationId); NativeAnimationSnapshotAssets.Identity(s.RandomId);
        NativeAnimationSnapshotAssets.Match(s.AnimationFingerprint, NativeAnimationSnapshotAssets.Fingerprint(b.Animation));
        NativeLogicalAnimationControl.ValidateState(s.Control, b.Table);
        if (s.Version != 1 || s.DurationId != b.DurationId || s.RandomId != b.RandomId || s.RandomState != b.Random.CaptureState() ||
            (uint)s.Slot > NativeAnimationDescriptor.Inactive || s.Variant < 0 || s.Variant > 65535 ||
            !float.IsFinite(s.Frame) || s.Frame < 0 || !float.IsFinite(s.Duration) || s.Duration < 0)
            throw new InvalidDataException("invalid guest animation state or staged provider identity/state");
        if (s.Slot != NativeAnimationDescriptor.Inactive)
        {
            var r = NativeAnimationSnapshotAssets.Record(b.Animation, s.Slot, s.Variant);
            if (r.DurationFrames <= 0 || s.Duration != (float)r.DurationFrames || s.Frame > s.Duration || s.Held != null)
                throw new InvalidDataException("invalid active guest animation record");
        }
        else if (s.Held is { } h)
        {
            if ((uint)h.Slot >= NativeAnimationDescriptor.Inactive || h.Variant != s.Variant)
                throw new InvalidDataException("invalid held guest animation index");
            var r = NativeAnimationSnapshotAssets.Record(b.Animation, h.Slot, h.Variant);
            // Inactive updates keep adding time; Frame need not stay at Duration.
            if (r.DurationFrames <= 0 || s.Duration != (float)r.DurationFrames || s.Frame < s.Duration)
                throw new InvalidDataException("invalid held guest animation record");
        }
        else if (s.Duration != 0 || s.Variant != 0)
            throw new InvalidDataException("inactive guest without a held record must have initial duration/variant");
    }
    /// <summary>Pure construction with no Push/Advance/Update, duration lookup, or random draw.</summary>
    public static NativeGuestAnimation FromState(Snapshot s, SnapshotBindings bindings)
    {
        ValidateState(s, bindings);
        return new NativeGuestAnimation(s, bindings);
    }
    NativeGuestAnimation(Snapshot s, SnapshotBindings b)
    {
        duration = b.Duration; Control = NativeLogicalAnimationControl.FromState(s.Control, b.Table);
        Requested = s.Requested; Stamp = s.Stamp; Slot = s.Slot; Variant = s.Variant;
        Frame = s.Frame; Duration = s.Duration; Boundaries = s.Boundaries;
        Held = s.Held is { } h ? (h.Slot, h.Variant) : null;
    }
}
