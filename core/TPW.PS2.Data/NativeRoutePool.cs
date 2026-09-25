using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

/// <summary>
/// Shared packed output-slot storage at native 3AE1B8, implementing
/// 192728/192768/1927F8/192840. This is not the planner, search-node pool or
/// request-record pool. The 11-bit link range is not the slot capacity.
/// </summary>
/// <remarks>
/// SAFETY: range, allocation-state and cycle checks are managed policies, not
/// checks in those native routines. Invalid indices throw ArgumentOutOfRangeException;
/// unallocated access, double free and cycles throw InvalidOperationException.
/// IsAllocated is the deliberate exception to the unallocated-read guard.
/// Instances are not thread-safe. Callers must own chains exclusively: structural
/// validation cannot establish ownership or detect another cursor sharing a tail.
/// </remarks>
public sealed class NativeRoutePool
{
    // 192770: literal SLTI 0x3E8, independent of the 0x7FF terminal link.
    public const int Capacity = 1000;
    private const uint AllocatedBit = 0x8000u;
    private const uint LinkMask = 0x7ffu;
    private readonly uint[] words = new uint[Capacity];

    /// <summary>Forward allocation hint at native 37E118 (192768/1927F8).</summary>
    public int Hint { get; private set; }
    /// <summary>Free-slot count at native 2E2924 (192764/192768/1927F8).</summary>
    public int Available { get; private set; }
    /// <summary>Derived from the native free-slot count, not a separate resource.</summary>
    public int AllocatedCount => Capacity - Available;

    /// <summary>
    /// Managed-only reset epoch; changes on every Reset, including construction.
    /// Cursors may compare their captured epoch before accessing recycled indices.
    /// This is not a native field or protection against ordinary free/reallocation.
    /// </summary>
    public ulong ResetGeneration { get; private set; }

    /// <summary>Initially zero-filled managed storage, followed by native 192728 reset.</summary>
    public NativeRoutePool() => Reset();

    /// <summary>
    /// 192728..192764: clear only bit15 of all 1000 words, hint=0, available=1000.
    /// Links and packed coordinates survive. All existing handles are invalidated.
    /// </summary>
    public void Reset()
    {
        for (int i = 0; i < Capacity; i++)
            words[i] &= ~AllocatedBit;
        Hint = 0;
        Available = Capacity;
        ResetGeneration = unchecked(ResetGeneration + 1);
    }

    /// <summary>
    /// 192768: scan forward without wrapping; preserve everything except the
    /// allocation bit and link. Failure returns -1 without changing hint or count.
    /// </summary>
    public int Allocate()
    {
        for (int i = Hint; i < Capacity; i++)
        {
            if ((words[i] & AllocatedBit) != 0)
                continue;
            words[i] = (words[i] & ~LinkMask) | AllocatedBit | LinkMask;
            Hint = i + 1;
            Available--;
            return i;
        }
        return -1;
    }

    /// <summary>
    /// 1927F8: free exactly one slot, preserving link/coordinates, incrementing
    /// availability and lowering the hint. Does not unlink inbound references.
    /// SAFETY: rejects invalid indices and double free before changing anything.
    /// </summary>
    public void FreeOne(int index)
    {
        ValidateAllocated(index);
        words[index] &= ~AllocatedBit;
        Available++;
        Hint = Math.Min(Hint, index);
    }

    /// <summary>Read a packed table word (3AE1B8); SAFETY: requires a live slot.</summary>
    public uint ReadWord(int index)
    {
        ValidateAllocated(index);
        return words[index];
    }

    /// <summary>192768 allocation-bit test; SAFETY: validates the table index.</summary>
    public bool IsAllocated(int index)
    {
        ValidateIndex(index);
        return (words[index] & AllocatedBit) != 0;
    }

    /// <summary>
    /// 192840/191D78: low11 successor, with 0x7FF mapped to -1.
    /// SAFETY: nonterminal successors must also reference allocated pool slots.
    /// </summary>
    public int Next(int index)
    {
        int next = DecodeNext(ReadWord(index));
        if (next != -1)
            ValidateAllocated(next);
        return next;
    }

    /// <summary>1924D0: decode a live slot using the native coordinate codec.</summary>
    public Point Target(int index) => NativeGuestMotion.DecodeTarget(ReadWord(index));

    /// <summary>192560: encode coordinates, preserving allocation and link bits.</summary>
    public void SetTarget(int index, Point target)
    {
        ValidateAllocated(index);
        words[index] = NativeGuestMotion.EncodeTarget(words[index], target);
    }

    /// <summary>
    /// 18D54C..18D570: install the low11 link, preserving all other bits; -1 is
    /// terminal. SAFETY: validates the entire prospective tail before writing,
    /// rejecting dangling links and cycles (including a link back to this slot).
    /// </summary>
    public void SetNext(int index, int next)
    {
        ValidateAllocated(index);
        ValidateChain(next, index);
        words[index] = (words[index] & ~LinkMask)
            | (next == -1 ? LinkMask : (uint)next);
    }

    /// <summary>
    /// 192840: -1 is a no-op; save each successor BEFORE 1927F8 frees its slot.
    /// SAFETY: validate the whole chain first so a dangling link or cycle cannot
    /// loop or partially free it. The caller must exclusively own this chain.
    /// </summary>
    public void FreeChain(int head)
    {
        ValidateChain(head);
        while (head != -1)
        {
            int next = DecodeNext(words[head]);
            FreeOne(head);
            head = next;
        }
    }

    /// <summary>
    /// 18D358 builder's output-list portion: prepend already-selected forward
    /// waypoints in reverse order (18D54C..18D570), then publish the head.
    /// Targets use 192560 packing, with no extra compression or cell-centering.
    /// Direction compression/search-node selection belongs to the caller's path
    /// service, not this pool. Empty input succeeds with head=-1.
    /// </summary>
    /// <remarks>
    /// SAFETY: copy/validate all input before allocating any slots. Every Point
    /// is encodable by the native wrapping codec; no grid-bound restriction is
    /// invented. Callers must synchronize concurrent mutation during the copy.
    /// 18D488 -> 18D574 -> 192840: allocation failure frees the private partial
    /// list and returns false/head=-1; ordinary frees, not a saved hint, roll back.
    /// Existing caller-owned routes are not disposed by this helper.
    /// </remarks>
    public bool TryBuild(IReadOnlyList<Point> forwardWaypoints, out int head)
    {
        head = -1;
        ArgumentNullException.ThrowIfNull(forwardWaypoints);
        int count = forwardWaypoints.Count;
        if (count < 0)
            throw new ArgumentException("Waypoint count cannot be negative.", nameof(forwardWaypoints));
        var snapshot = new Point[count];
        for (int i = 0; i < count; i++)
            snapshot[i] = forwardWaypoints[i];
        if (forwardWaypoints.Count != count)
            throw new InvalidOperationException("Waypoint list changed while being copied.");

        int partialHead = -1;
        for (int i = snapshot.Length - 1; i >= 0; i--)
        {
            int slot = Allocate();
            if (slot == -1)
            {
                FreeChain(partialHead);
                return false;
            }
            SetTarget(slot, snapshot[i]);
            // Private reverse-prepend construction cannot introduce a cycle: slot is
            // freshly allocated and the prior partial list contains only older slots.
            // Avoid the public SetNext's whole-tail safety scan on every append.
            words[slot] = (words[slot] & ~LinkMask) | (partialHead == -1 ? LinkMask : (uint)partialHead);
            partialHead = slot;
        }
        head = partialHead;
        return true;
    }

    private static int DecodeNext(uint word)
    {
        int next = (int)(word & LinkMask);
        return next == (int)LinkMask ? -1 : next;
    }

    private static void ValidateIndex(int index)
    {
        if ((uint)index >= Capacity)
            throw new ArgumentOutOfRangeException(nameof(index), index,
                "Route slot index must be between 0 and 999.");
    }

    private void ValidateAllocated(int index)
    {
        ValidateIndex(index);
        if ((words[index] & AllocatedBit) == 0)
            throw new InvalidOperationException($"Route slot {index} is not allocated.");
    }

    // Managed SAFETY only: raw 192840 has neither preflight nor a cycle guard.
    // Bounded stack storage avoids allocating validation objects after slot allocation.
    private void ValidateChain(int head, int forbidden = -1)
    {
        if (head == -1)
            return;
        Span<bool> seen = stackalloc bool[Capacity];
        seen.Clear();
        while (head != -1)
        {
            ValidateAllocated(head);
            if (head == forbidden || seen[head])
                throw new InvalidOperationException("Route chain contains a cycle.");
            seen[head] = true;
            head = DecodeNext(words[head]);
        }
    }
}
