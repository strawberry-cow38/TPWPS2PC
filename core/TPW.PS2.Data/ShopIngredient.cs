namespace TPW.PS2.Data;

/// <summary>A shop's special ingredient -- the "Salt / Sugar / Ice / Fat" row on its info panel.
///
/// ⭐⭐ READ, not chosen. `0x1d1f60` in `SLES_500.32`:
///
/// <code>
/// id = shopIngredientId()                        ; 0x1d1d08
/// if (id == 2 || id == 3 || id == 6) return 0    ; no special ingredient
/// return *(u32*)(0x2E9A78 + id * 4)              ; -> text index
/// </code>
///
/// A flat `u32` array indexed by id, with the three ids above short-circuited BEFORE the load.
/// The id itself is DBA data: `0x1d1d08` goes through `0x10fa30` -- the payload accessor in
/// findings/dba.md -- and reads ONE BYTE at payload <c>+0x30</c>.
///
/// ⭐ Verified against the shipped `arsdb.dba`: key 241 (the JUNGLE Coconut) reads 01 = Ice,
/// 243 = 07 Salt, 245 = 04 Sugar, and 242/244 = 02/06. The data contains exactly the guarded ids,
/// so the guard is live code rather than something read into it.
///
/// ⚠ This table was nearly written off as "runtime-populated, nearly all zeros". It is static and
/// was always in the file -- the zeros are the array's UNUSED SLOTS. See findings/shop-info-ui.md.</summary>
public static class ShopIngredient
{
    /// <summary>Where the array lives, for anyone checking this against the executable.</summary>
    public const uint TableAddress = 0x2E9A78;

    /// <summary>The DBA payload byte the id comes from.</summary>
    public const int PayloadOffset = 0x30;

    /// <summary>The array as the disc holds it: text-table indices, 0 where the slot is unused.
    /// ⚠ INDICES, not strings -- the game stores the same thing, and the captions live in
    /// `Text/translations/`. <see cref="CaptionOf"/> is only for callers with no text table.</summary>
    static readonly int[] TextIds = { 804, 379, 0, 0, 142, 0, 0, 1 };

    /// <summary>Ids the code refuses before it ever indexes the table.</summary>
    public static bool Guarded(int id) => id is 2 or 3 or 6;

    /// <summary>The text-table index for a shop's ingredient row, or 0 for none.</summary>
    public static int TextIdFor(int id)
        => Guarded(id) || id < 0 || id >= TextIds.Length ? 0 : TextIds[id];

    /// <summary>The ingredient id for a compiled shop, from its DBA payload, or -1 if the payload
    /// is too short to hold one.</summary>
    public static int IdFrom(ReadOnlySpan<byte> payload)
        => payload.Length > PayloadOffset ? payload[PayloadOffset] : -1;

    /// <summary>⚠ A DISPLAY FALLBACK for callers without the text table, and the English is
    /// hard-coded here only because the ids above are the real answer. Anything that can reach
    /// `Text/translations/` should resolve <see cref="TextIdFor"/> instead and get the player's
    /// own language.</summary>
    public static string CaptionOf(int id) => TextIdFor(id) switch
    {
        804 => "Fat",
        379 => "Ice",
        142 => "Sugar",
        1 => "Salt",
        _ => null,
    };
}
