namespace TPW.PS2.Data;

/// <summary>What a tile of path or queue costs to lay.
///
/// ⭐⭐ **PATH £10 A TILE, QUEUE £25 A TILE** -- read out of the PlayStation build's own
/// instructions, not inferred. `0x8001B580` sets both at boot:
///
/// <code>
///   0x8001b580  jal 0x8001b600 ; addiu a0,zero,10     -> path  price := 10   (gp+192)
///   0x8001b588  jal 0x8001b618 ; addiu a0,zero,25     -> queue price := 25   (gp+196)
/// </code>
///
/// with getters at `0x8001B60C` / `0x8001B624`, and a run accumulator at `gp+208`
/// (set `0x8001B630`, add `0x8001B63C`, read `0x8001B654`).
///
/// ⚠⚠ **THE VALUES ARE THE PSX BUILD'S, THE MECHANISM IS BOTH BUILDS'.** The PS2 executable
/// carries the same shape and it was traced first and independently: `FUN_0011AD60` and
/// `FUN_0011CEB0` -- the two path tools' activate handlers -- each set `tool + 0x3c` from
/// `*(u16 *)(obj + 0xD0)` of the object a virtual call returns, `0x11B2D4` copies that into
/// `tool + 0x08`, and `FUN_0011B898` charges it with
/// `FUN_00100750(bank, *(int *)(tool + 8) * 10)`. The PSX tool likewise "puts the total in its
/// +8" and spends `total * 10`. **Same field offset, same multiplier, two different compilers** --
/// which is why the mechanism is taken as read. What the PS2 image does NOT give is the two
/// NUMBERS: they arrive in a runtime object field, no scenario loader is traced, and there is no
/// savestate to read `0x3890D8` / `0x389330` out of. So the figures below are the PSX's, and this
/// paragraph is what to delete when a PS2 savestate settles them.
///
/// ⭐ **MONEY IS TENTHS, AND THE x10 IS THE CONSOLE'S OWN.** `0x8005002C` builds a money value
/// from a pounds figure as `((n &lt;&lt; 2) + n) &lt;&lt; 1` -- ten times n -- which is the same
/// tenths convention <see cref="Money"/> and <see cref="RideCatalogue.PlacementCost"/> already
/// use.</summary>
public static class PathPrices
{
    /// <summary>£10. `0x8001B584`.</summary>
    public const int PathPounds = 10;

    /// <summary>£25. `0x8001B58C`.</summary>
    public const int QueuePounds = 25;

    /// <summary>The price of one tile of <paramref name="kind"/>, in pounds.
    ///
    /// ⭐ THE TILE KINDS ARE THE CHOOSER'S OWN. `0x8004F3A4`: kind **2** or **13** takes the path
    /// price, kind **4** takes the queue price, and anything else leaves the price at the zero it
    /// was initialised to (`addu s4,zero,zero` at `0x8004F368`). Those three numbers are exactly
    /// <see cref="PathTool.Kind"/>'s values, which is why the enum can be switched on directly.
    /// ⚠ `Both` costs a PATH tile, not a queue one.</summary>
    public static int Pounds(PathTool.Kind kind) => kind switch
    {
        PathTool.Kind.Path or PathTool.Kind.Both => PathPounds,
        PathTool.Kind.Queue => QueuePounds,
        _ => 0,
    };

    /// <summary>`0x8005002C` -- a pounds figure as the money the till actually holds.</summary>
    public static int Tenths(int pounds) => ((pounds << 2) + pounds) << 1;

    /// <summary>Whether laying <paramref name="kind"/> on a cell that currently holds
    /// <paramref name="had"/> costs anything.
    ///
    /// ⭐⭐ **YOU PAY ONLY FOR A TILE THAT CHANGES.** `0x8004F484`-`0x8004F4AC` skips the
    /// accumulate on three arms: the cell is already exactly the kind being laid
    /// (`beq v1,s3`), the cell is <see cref="PathTool.Kind.Both"/> and a path is being laid over
    /// it (`v1 == 13 &amp;&amp; s3 == 2`), or the validator refused the cell outright. A path run
    /// walked back along itself is therefore free, and so is a run drawn through a junction.
    /// ⚠ The converse arms DO charge: path-over-queue and queue-over-both both change the cell,
    /// and both pay the price of the kind being laid.</summary>
    public static bool Charges(PathTool.Kind had, PathTool.Kind kind)
        => had != kind && !(had == PathTool.Kind.Both && kind == PathTool.Kind.Path);

    /// <summary>Whether a run whose total so far is <paramref name="pounds"/> may take another
    /// tile, given a till holding <paramref name="balance"/> tenths.
    ///
    /// ⭐⭐ **THE TILL MUST BE LEFT WITH SOMETHING.** `0x8004F3F4`-`0x8004F438` reads the running
    /// total, subtracts its tenths from the balance (`0x8004FFBC`), and compares against zero with
    /// `0x80050014`, which returns `*a &lt;= *b` -- `lw; lw; slt v0,v0,v1; xori v0,v0,1`. A
    /// non-zero answer branches to the refusal. So the test is `balance - total*10 &lt;= 0`
    /// REFUSES: you may spend down to a pound but never to nothing.
    ///
    /// ⚠ THE TOTAL IS THE ONE BEFORE THIS TILE. The check sits at `0x8004F3F4` and the accumulate
    /// at `0x8004F4AC`, so the tile being judged has not been added yet -- the run gets one more
    /// tile than a check-after ordering would allow, and that off-by-one is the console's.</summary>
    public static bool CanAfford(int balance, int pounds) => balance - Tenths(pounds) > 0;
}
