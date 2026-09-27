namespace TPW.PS2.Data;

/// <summary>Footprints this port declares differently from the `.sam` on the disc.
///
/// ⚠⚠ EVERY ENTRY IS A DELIBERATE DEPARTURE AND NEEDS A REASON. The disc's `Info.Shape` is the
/// default and should stay the default; this exists so a correction is one visible, argued list
/// rather than a special case buried in the placement path.</summary>
public static class ShapeOverrides
{
    /// <summary>⭐ DINO KARTS IS 4x4. Master: "the dino karts footprint should be 4x4."
    ///
    /// `GoKarts.sam` declares `**** / **** / *2N*` -- 4 wide by THREE deep -- while the model
    /// measures 4.00 x 4.00 with its origin on the shape's own corner, so it covers a row of cells
    /// the footprint never claims and the grass stays under it. That is what master saw as Dino
    /// Karts "overlapping terrain tiles".
    ///
    /// ⭐ The replacement is not invented: it is the shape of `Wateride.sam`, the other JUNGLE
    /// track ride, which is `**** / **** / **** / *2N*` -- the same 4-wide station with one more
    /// plain row and the doors still on the edge row. GoKarts is that shape with a row missing, so
    /// the row goes back where its sibling has it and the entrance/exit stay put.</summary>
    static readonly (string Leaf, string[] Shape)[] Table =
    {
        ("gokarts.sam", new[] { "****", "****", "****", "*2N*" }),
    };

    /// <summary>The corrected shape for a `.sam`, or null to use the disc's own.</summary>
    public static string[] For(string samPath)
    {
        if (string.IsNullOrEmpty(samPath)) return null;
        int slash = samPath.LastIndexOfAny(new[] { '/', '\\' });
        string leaf = (slash >= 0 ? samPath[(slash + 1)..] : samPath).ToLowerInvariant();
        foreach (var (name, shape) in Table) if (leaf == name) return shape;
        return null;
    }
}
