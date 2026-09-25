using System.Buffers.Binary;

namespace TPW.PS2.Data;

/// <summary>One 32-byte dispatcher descriptor (eight u32 words). The pair meanings come from the
/// consumers 10E988/10EA38, not from the layout: first plays once on entry, main is the body,
/// last plays once on the way out before a pending request commits. Slot
/// <see cref="Inactive"/> (F) is absent/inactive -- 1ACFC0 treats it as a record boundary, it
/// is never APS section 15.</summary>
public readonly record struct NativeAnimationDescriptor(
    int FirstSlot, int FirstVariant, int MainSlot, int MainVariant,
    int LastSlot, int LastVariant, uint Flags, uint Weight)
{
    public const int Inactive = 0xF;
    /// <summary>flags&amp;3 == 0: phase 1 replays main at every boundary (10EB94).</summary>
    public bool Loops => (Flags & 3) == 0;
}

/// <summary>
/// The logical-animation table at 2AAD48: 22 entries of (descriptor pointer, count), read from
/// the owner's PAL executable. Its only readers are 10E800 and 10EA38, and neither bound-checks
/// the logical. The descriptors tile 2AA928..2AAD48 in logical order and end exactly where the
/// table begins. That is checked here, so a different build fails loudly instead of
/// being misread. Guests use logicals as the low five bits of N+38 (see
/// findings/native-guest-animation-readiness.md); logical 13 plays section 0 and logical 9
/// plays section 1. A logical is NOT an APS section number.
/// </summary>
public sealed class NativeLogicalAnimationTable
{
    public const uint TableAddress = 0x2aad48, FirstDescriptor = 0x2aa928, IdleTable = 0x2eec18;
    public const int LogicalCount = 22, IdleCount = 4;
    readonly NativeAnimationDescriptor[][] rows;
    /// <summary>2106E8's picks: bytes at 2EEC18 + 4*i, bounded by its own 1448E0(4) at 210758.</summary>
    public IReadOnlyList<int> IdleStates { get; }

    public IReadOnlyList<NativeAnimationDescriptor> Variants(int logical) =>
        (uint)logical < (uint)rows.Length ? Array.AsReadOnly(rows[logical])
            : throw new ArgumentOutOfRangeException(nameof(logical), $"no logical {logical} in the 2AAD48 table");

    public static NativeLogicalAnimationTable Read(Disc disc)
    {
        var entry = disc.Files().SingleOrDefault(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        if (entry == null) throw new InvalidDataException("the logical-animation table needs the PAL SLES_500.32 executable");
        return new NativeLogicalAnimationTable(disc.Read(entry.Extent, entry.Size));
    }

    public NativeLogicalAnimationTable(byte[] elf)
    {
        if (elf.Length < 52 || !elf.AsSpan(0, 6).SequenceEqual(new byte[] { 127, 69, 76, 70, 1, 1 }))
            throw new InvalidDataException("the logical-animation table requires a little-endian ELF32 executable");
        uint U32(int off) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(off, 4));
        int U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(off, 2));
        int Offset(uint va, int size)
        {
            int ph = checked((int)U32(28));
            for (int i = 0; i < U16(44); i++)
            {
                int p = ph + i * U16(42);
                if (U32(p) == 1 && va >= U32(p + 8) && (ulong)va + (uint)size <= (ulong)U32(p + 8) + U32(p + 16))
                    return checked((int)(U32(p + 4) + va - U32(p + 8)));
            }
            throw new InvalidDataException($"logical-animation address 0x{va:x} is not in a file-backed PT_LOAD");
        }
        rows = new NativeAnimationDescriptor[LogicalCount][];
        uint expected = FirstDescriptor;
        for (int logical = 0; logical < LogicalCount; logical++)
        {
            int at = Offset(TableAddress + (uint)logical * 8, 8);
            uint pointer = U32(at), count = U32(at + 4);
            if (pointer != expected || count == 0 || count > 255)
                throw new InvalidDataException($"logical {logical}: descriptor 0x{pointer:x} x{count} does not continue the contiguous run at 0x{expected:x}");
            var row = new NativeAnimationDescriptor[count];
            int d = Offset(pointer, checked((int)count * 32));
            for (int v = 0; v < count; v++, d += 32)
            {
                int W(int k) => checked((int)U32(d + k * 4));
                row[v] = new(W(0), W(1), W(2), W(3), W(4), W(5), U32(d + 24), U32(d + 28));
                foreach (int slot in new[] { row[v].FirstSlot, row[v].MainSlot, row[v].LastSlot })
                    if ((uint)slot > NativeAnimationDescriptor.Inactive)
                        throw new InvalidDataException($"logical {logical} variant {v}: slot {slot} is past the sentinel F");
            }
            rows[logical] = row;
            expected = pointer + count * 32;
        }
        if (expected != TableAddress)
            throw new InvalidDataException($"descriptors end at 0x{expected:x}, not at the table 0x{TableAddress:x}");
        var idle = new int[IdleCount];
        int at0 = Offset(IdleTable, IdleCount * 4);
        for (int i = 0; i < IdleCount; i++)
            if ((idle[i] = elf[at0 + i * 4] & 0x1f) >= LogicalCount)
                throw new InvalidDataException($"idle pick {i} is logical {idle[i]}, off the 2AAD48 table");
        IdleStates = Array.AsReadOnly(idle);
    }
}

/// <summary>The C library LCG behind 29CF08 (newlib rand: state*1103515245+12345, low 31 bits,
/// state in the reent block at 3535E4+58). ⚠ Same generator, NOT the console's stream: 46 call
/// sites share that state, so a port cannot reproduce which draw lands here. It only matters
/// for multi-variant logicals (11, 21); 9 and 13 have one descriptor and draw nothing.</summary>
public sealed class NewlibRand
{
    uint state;
    public NewlibRand(uint seed = 1) { state = seed; }
    public int Next() { state = unchecked(state * 1103515245u + 12345u); return (int)(state & 0x7fffffff); }
}

/// <summary>
/// One model's four-byte animation control block at model+4C, driven exactly as 10E910 (request),
/// 10EA38 (advance at a playback record boundary, called only from 1ACFC0 at 1AD078) and 10EC48
/// (current) drive it. Byte +0 is current, +1 variant, +2 phase, +3 pending, and FF means none.
/// A new model starts as 10ECF0 left the loader's template at 1F8188, and 1F6230 copies that
/// template into every instance: current FF, variant 0, pending FF. Phase is 0 because 29C370
/// zeroed the template at 1F7F40.
///
/// ⚠ Requesting a state does not make it current. The request only queues pending. The commit
/// happens inside <see cref="Advance"/>, and when the old descriptor has a last pair it happens
/// one record later. The movement predicate reads <see cref="Current"/>, never Pending.
/// </summary>
public sealed class NativeLogicalAnimationControl
{
    public const byte None = 0xff;
    readonly NativeLogicalAnimationTable table;
    public NativeLogicalAnimationControl(NativeLogicalAnimationTable table) =>
        this.table = table ?? throw new ArgumentNullException(nameof(table));
    public byte Current { get; private set; } = None;
    public byte Variant { get; private set; }
    public byte Phase { get; private set; }
    public byte Pending { get; private set; } = None;

    NativeAnimationDescriptor? Descriptor => Current == None ? null : table.Variants(Current)[Variant];

    /// <summary>10E910. Flag 20 clears pending before the equality test; a request equal to
    /// CURRENT returns without touching pending (a queued different request survives it). Flag 2
    /// first calls 1ACF20, which backdates the playback start by the record's whole duration so
    /// the next 1ACFC0 update is a boundary: cut the current record short, not skip a last pair.
    /// <paramref name="cutCurrentRecord"/> is that effect on the caller's actual playback.</summary>
    public void Request(int logical, int flags, Action cutCurrentRecord)
    {
        ArgumentNullException.ThrowIfNull(cutCurrentRecord);
        _ = table.Variants(logical); // native trusts it; a logical off the table would read garbage later
        if ((flags & 0x20) != 0) Pending = None;
        if (logical == Current) return;
        if ((flags & 2) != 0) cutCurrentRecord();
        Pending = (byte)logical;
    }

    /// <summary>10EA38 at a playback record boundary: the (APS section, variant) to play next.
    /// <paramref name="rand"/> is consulted only by multi-variant selection (10E800).</summary>
    public (int Slot, int Variant) Advance(Func<int> rand)
    {
        ArgumentNullException.ThrowIfNull(rand);
        var old = Descriptor;
        if (Pending != None)
        {
            if (Phase < 2 && old is { LastSlot: not NativeAnimationDescriptor.Inactive } leaving)
            {
                Phase = 2; // the old last pair plays; nothing commits yet
                return (leaving.LastSlot, leaving.LastVariant);
            }
            byte variant = 0;
            var next = Select(Pending, ref variant, old, rand, out bool retainedOld);
            // ⚠ 10EA38 passes UNINITIALIZED stack as the variant byte, and 10E800's flag-4
            // retention path returns the old descriptor without writing it. The pending logical
            // would commit with a garbage variant. Refuse loudly rather than invent one.
            if (retainedOld)
                throw new NotSupportedException($"native uninitialized-variant path: pending {Pending} retained a flag-4 descriptor of {Current}");
            var output = Enter(next);
            Current = Pending; Pending = None; Variant = variant;
            return output;
        }
        if (old is not { } d) { Current = None; return (NativeAnimationDescriptor.Inactive, 0); }
        switch (Phase)
        {
            case 0: Phase = 1; return (d.MainSlot, d.MainVariant);
            case 1:
                if (d.Loops) return (d.MainSlot, d.MainVariant);
                if (d.LastSlot != NativeAnimationDescriptor.Inactive) return (d.LastSlot, d.LastVariant); // phase stays 1
                if ((d.Flags & 2) != 0) return Reroll(d, rand);
                return (NativeAnimationDescriptor.Inactive, 0); // a finished one-shot keeps its logical as current
            case 2:
                if ((d.Flags & 2) != 0) return Reroll(d, rand);
                Current = None; Phase = 0; // the last pair ended with the pending request withdrawn
                return (NativeAnimationDescriptor.Inactive, 0);
            default:
                throw new InvalidOperationException($"phase {Phase}: native 10EA38 leaves its outputs unwritten");
        }
    }

    /// <summary>10E988: re-select within the CURRENT logical. The caller's variant byte is
    /// pre-set from +1, so a flag-4 retention keeps it.</summary>
    (int, int) Reroll(NativeAnimationDescriptor old, Func<int> rand)
    {
        byte variant = Variant;
        var next = Select(Current, ref variant, old, rand, out _);
        var output = Enter(next);
        Variant = variant;
        return output;
    }

    (int, int) Enter(NativeAnimationDescriptor d)
    {
        if (d.FirstSlot != NativeAnimationDescriptor.Inactive) { Phase = 0; return (d.FirstSlot, d.FirstVariant); }
        Phase = 1;
        return (d.MainSlot, d.MainVariant);
    }

    /// <summary>10E800. Fewer than two descriptors: variant 0, no draw. An old descriptor with
    /// flag 4 draws rand%400 and is RETAINED at &gt;=100 (variant not written); below 100 that
    /// same remainder is the weighted roll. Otherwise the roll is rand%100. Cumulative
    /// unsigned weights; no hit falls back to variant 0.</summary>
    NativeAnimationDescriptor Select(int logical, ref byte variant, NativeAnimationDescriptor? old,
                                     Func<int> rand, out bool retainedOld)
    {
        retainedOld = false;
        var rows = table.Variants(logical);
        if (rows.Count < 2) { variant = 0; return rows[0]; }
        uint roll;
        if (old is { } o && (o.Flags & 4) != 0)
        {
            roll = (uint)(Draw(rand) % 400);
            if (roll >= 100) { retainedOld = true; return o; }
        }
        else roll = (uint)(Draw(rand) % 100);
        uint sum = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            sum += rows[i].Weight;
            if (roll < sum) { variant = (byte)i; return rows[i]; }
        }
        variant = 0;
        return rows[0];
    }

    static int Draw(Func<int> rand)
    {
        int r = rand();
        return r >= 0 ? r : throw new InvalidOperationException("newlib rand returns 0..0x7fffffff");
    }

    /// <summary>191E10 after its visual lookup. No visual object permits motion. A requested
    /// low-5 state other than 9/13 permits it. Otherwise only a CURRENT logical of 9 or 13
    /// permits it, and the two need not be equal. A visual with handle 0 reads FF through 10EC48
    /// and so blocks. That makes it different from having no visual.</summary>
    public static bool MovementPermitted(int requestedWord, bool hasVisual, byte current)
    {
        if (!hasVisual) return true;
        int requested = requestedWord & 0x1f;
        if (requested != 9 && requested != 13) return true;
        return current == 9 || current == 13;
    }

    public bool PermitsMovement(int requestedWord) => MovementPermitted(requestedWord, true, Current);
}

/// <summary>
/// A guest model's playback (the struct at model+10) driven beside its control block, as 1ACFC0
/// drives it. The frame runs at 30 per second on the playback's millisecond clock (1AD21C..50).
/// Between record boundaries nothing changes. At a boundary, or while inactive (slot F), it asks
/// <see cref="NativeLogicalAnimationControl.Advance"/> for the next (section, variant).
///
/// A valid answer continues the frame through fmod, or restarts it at 0 when coming from F.
/// A changed record starts at min(carry, duration - 0.0001).
///
/// An answer the model cannot play has two cases: section F, or a section/variant the model's
/// .aps lacks, which the 17D7C0/17D7C8 counts reject. In both, the old record is posed at its
/// full duration and the slot becomes F. The guest HOLDS its final pose; this is not bind pose
/// and not a loop (1AD254..288, when the caller 1F2E70 applies poses).
///
/// <see cref="Requested"/> is the guest's N+38 word; its low five bits are the logical.
/// <see cref="Push"/> is what the guest's visual sync (211D28 -> 192438 -> 1921D0) does on every
/// update of a shown guest. Which of guest update, push and model update comes first within one
/// frame was NOT found: see findings/native-guest-animation-readiness.md.
/// </summary>
public sealed class NativeGuestAnimation
{
    public const float FramesPerSecond = 30f;
    readonly Func<int, int, int?> duration;
    public NativeLogicalAnimationControl Control { get; }
    public int Requested { get; set; }
    public int Slot { get; private set; } = NativeAnimationDescriptor.Inactive; // 1ACCC0 at creation
    public int Variant { get; private set; }
    public float Frame { get; private set; }
    public float Duration { get; private set; }
    /// <summary>The record whose final pose is held while <see cref="Slot"/> is F, or null.</summary>
    public (int Slot, int Variant)? Held { get; private set; }
    public int Boundaries { get; private set; }

    /// <param name="duration">(section, variant) -> frame count of that record in THIS guest's
    /// .aps, or null when the model has no such record.</param>
    public NativeGuestAnimation(NativeLogicalAnimationTable table, Func<int, int, int?> duration)
    {
        Control = new NativeLogicalAnimationControl(table);
        this.duration = duration ?? throw new ArgumentNullException(nameof(duration));
    }

    public bool PermitsMovement => Control.PermitsMovement(Requested);
    /// <summary>N+2C: the update-counter stamp 20BD30..38 takes at activation.</summary>
    public int Stamp { get; set; }

    /// <summary>2106E8, the guest update for execution state 0B. <paramref name="now"/> is the
    /// 1C4930 update counter and <paramref name="random"/> is 1448E0(n), the guest stream.
    /// Request 11 is held until 120 updates after the stamp and then picked. Any other request is
    /// re-picked on a 1-in-10 draw (rand(100) &lt; 10), whenever it happens.</summary>
    public void IdlePick(int now, Func<int, int> random, IReadOnlyList<int> picks)
    {
        bool idle = (Requested & 0x1f) == 11;
        if (unchecked((uint)(Stamp + 0x78)) < (uint)now) { if (!idle && random(100) >= 10) return; }
        else if (idle || random(100) >= 10) return;
        Requested = (Requested & ~0x1f) | picks[random(picks.Count)];
    }

    /// <summary>1921D0's request. flags is 2 only when guest bit B+2C&amp;200 is set (140880's
    /// item attach), otherwise 0. The cut is 1ACF20: the next update finds the record complete.</summary>
    public void Push(int flags = 0) => Control.Request(Requested & 0x1f, flags, () =>
    {
        if (Slot != NativeAnimationDescriptor.Inactive) Frame = Duration;
    });

    /// <summary>One model update after <paramref name="milliseconds"/> of playback clock.</summary>
    public void Update(float milliseconds, Func<int> rand)
    {
        if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        Frame += milliseconds * FramesPerSecond / 1000f;
        if (Slot != NativeAnimationDescriptor.Inactive && Frame < Duration) return;
        Boundaries++;
        var (slot, variant) = Control.Advance(rand);
        int? frames = slot == NativeAnimationDescriptor.Inactive ? null : duration(slot, variant);
        if (frames is int length && length > 0)
        {
            Frame = Slot == NativeAnimationDescriptor.Inactive ? 0f : Frame % Duration;
            if (slot != Slot || variant != Variant)
            {
                Slot = slot; Variant = variant; Duration = length;
                Frame = Math.Min(Frame, length - 0.0001f);
            }
            Held = null;
            return;
        }
        if (Slot != NativeAnimationDescriptor.Inactive) { Frame = Duration; Held = (Slot, Variant); }
        Slot = NativeAnimationDescriptor.Inactive;
    }
}
