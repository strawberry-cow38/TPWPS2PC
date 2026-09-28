using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

/// <summary>One litter object: a slot of PoolOfLitter (`0x3952C0`, 0x30 bytes, vtable `0x361038`,
/// class `0xD`). READ, findings/staff-handymen-entertainers.md §1.1.</summary>
public sealed class LitterItem
{
    internal LitterItem(int poolSlot, int variant) { PoolSlot = poolSlot; Variant = variant; }

    /// <summary>Which of the 40 slots.</summary>
    public int PoolSlot { get; }
    /// <summary>`P+0x20`: `rand(6)`, rolled ONCE per slot at pool construction (`0x15E1C8`) and
    /// never again, so a slot keeps its look across every reuse.</summary>
    public int Variant { get; }
    /// <summary>`P+0x14`: the activation serial `0x15E280` takes from the shared `0x1093B0`.</summary>
    public uint Serial { get; internal set; }
    /// <summary>`P+0x1C/+0x1E`, 1/256 cell.</summary>
    public Point Position { get; internal set; }
    /// <summary>`vt+0x74` = `0x15E890` (the same code as the person's): the cell.</summary>
    public ParkCell Cell => new(Position.X >> 8, Position.Z >> 8);
    /// <summary>`P+0x24`: vomit. Set by `0x15E368` (only if clear), cleared on free (`0x15E638`).</summary>
    public bool Vomit { get; internal set; }
    /// <summary>`P+0x2C`: the handyman who claimed it (`0x144FD8`), or null.</summary>
    public StaffMember Claimant { get; internal set; }
    /// <summary>Whether the model is shown (`vt+0xAC` on the instance). The two producers ported
    /// here show it (`0x15E5F0` from `0x20D4CC` and `0x210A54`); the prank and the reload do not.</summary>
    public bool Shown { get; internal set; }
    /// <summary>On the pool's active list.</summary>
    public bool Active { get; internal set; }

    /// <summary>`0x15E468`'s registry id: 487 `puke` when vomit, else 598/599/600
    /// (`litter1..3`) for variants 0-1 / 2-3 / 4-5. ⚠ Natively the model is only rebuilt when the
    /// "needs refresh" flag `P+0x28` is consumed at a show; for the two producers ported here that is
    /// always the model this returns.</summary>
    public int ModelId => Vomit ? 487 : Variant < 2 ? 598 : Variant < 4 ? 599 : 600;
}

/// <summary>⭐⭐ THE PARK'S LITTER: a pool of **40**, and a litter object has NO behaviour of its
/// own. Its update `0x15E350` is `jr ra`, there is no age field, and the ONLY thing that ever removes
/// one is a handyman finishing a sweep (`0x14B460` has one caller, `0x145598`). Pool full ⇒ the new
/// litter is silently not made. READ, findings/staff-handymen-entertainers.md §1.
///
/// - Allocate `0x14AD08`: pop the free list, push at the HEAD of the active list (so iteration is
///   newest first, and "the newest of equally near items" wins a handyman's tie), count+1, activation
///   (serial; vomit, refresh and claimant cleared), register the map object.
/// - Place `0x15E2B8`: `x = pos.x + rand(200) - 100`, `z = pos.z + rand(200) - 100` (1/256 cell, so
///   -100..+99 around the producer).
/// - Free `0x14B460`: clear vomit, hide, unregister, back to the free list, count-1. It sends NO
///   "object removed" notice, which is safe only because only the claimant removes it.
///
/// ⭐ The third producer, the guest's PRANK (hidden litter plus a `YellowStink` in the stink table
/// `0x1822D0/0x1824A8`), is <see cref="ParkStaff.Prank"/>; the sweep stops the stink.
/// ⚠ NOT PORTED, and said so: test-park mode (`0x153410` → `DAT_002B72A8`), which makes allocation
/// fail; the load scatter `0x14D8D8` and the two-count save `0x14D868`
/// (positions and claims are never saved natively). Save/load is out of scope for this step.</summary>
public sealed partial class ParkLitter
{
    public const int Capacity = 40;

    readonly LitterItem[] _slots = new LitterItem[Capacity];
    readonly List<LitterItem> _free = new();       // index 0 = head
    readonly List<LitterItem> _active = new();     // index 0 = head = newest
    readonly Func<int, int> _random;
    readonly NativeActivationSequence _activations;

    /// <param name="random">`rand(n)` = `0x1448E0`, 0..n-1 (the guest stream natively).</param>
    public ParkLitter(Func<int, int> random, NativeActivationSequence activations)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _activations = activations ?? throw new ArgumentNullException(nameof(activations));
        // 0x147EB0 then 0x148DD0: every slot constructed (variant rand(6) in 0x15E1C8), then pushed
        // onto the free list. ⚠ The free-list push order is not read for litter; the staff pools
        // push at the head (slot 4 first out), and the same is assumed here.
        for (int i = 0; i < Capacity; i++) _slots[i] = new LitterItem(i, _random(6));
        for (int i = 0; i < Capacity; i++) _free.Insert(0, _slots[i]);
    }

    /// <summary>The active list, newest first (the handyman's search order, `0x14D1F8`).</summary>
    public IReadOnlyList<LitterItem> Active => _active;
    /// <summary>`+0xC`: the active count; advisor variable 13 (`0x14D208`).</summary>
    public int Count => _active.Count;
    /// <summary>Instrumentation: made, refused for a full pool, and removed by a sweep.</summary>
    public int Made { get; private set; }
    public int RefusedFull { get; private set; }
    public int Swept { get; private set; }

    /// <summary>Raised after a new item is placed, and after one is swept away, for a view.</summary>
    public Action<LitterItem> Added, Removed;

    /// <summary>`0x14AD08` + `0x15E2B8`, then `0x15E368` for vomit and `0x15E5F0` when shown: a new
    /// item near <paramref name="at"/>, or null when all 40 are in use.</summary>
    public LitterItem Drop(Point at, bool vomit, bool shown = true)
    {
        if (_free.Count == 0) { RefusedFull++; return null; }
        var item = _free[0];
        _free.RemoveAt(0);
        _active.Insert(0, item);
        item.Active = true;
        item.Serial = _activations.Activate("litter");                  // 0x15E280 -> 0x1093B0
        item.Vomit = false; item.Claimant = null; item.Shown = false;
        int x = at.X + _random(200) - 100;                             // 0x15E2B8, x then z
        int z = at.Z + _random(200) - 100;
        item.Position = new Point(unchecked((short)x), unchecked((short)z));
        if (vomit) item.Vomit = true;                                  // 0x15E368
        item.Shown = shown;                                            // 0x15E5F0
        Made++;
        Added?.Invoke(item);
        return item;
    }

    /// <summary>`0x14B460`. ⚠ Internal: the handyman's sweep is its only native caller.</summary>
    internal void Remove(LitterItem item)
    {
        if (item == null || !item.Active) throw new InvalidOperationException("litter item is not on the active list");
        item.Vomit = false; item.Shown = false; item.Claimant = null;  // 0x15E638
        item.Active = false;
        _active.Remove(item);
        _free.Insert(0, item);
        Swept++;
        Removed?.Invoke(item);
    }

    /// <summary>Items within Manhattan distance &lt; 2 of a cell -- the guest's every-64-tick test
    /// (`slti $a0,$a0,2` at `0x20FF00`), split into plain and vomit.</summary>
    public (int Plain, int Vomit) Near(ParkCell cell)
    {
        int plain = 0, vomit = 0;
        foreach (var item in _active)
        {
            var c = item.Cell;
            if (Math.Abs(c.X - cell.X) + Math.Abs(c.Z - cell.Z) >= 2) continue;
            if (item.Vomit) vomit++; else plain++;
        }
        return (plain, vomit);
    }
}
