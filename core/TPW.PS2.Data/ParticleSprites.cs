namespace TPW.PS2.Data;

/// <summary>⭐⭐ WHICH TEXTURE A PARTICLE EFFECT DRAWS, read from the executable.
///
/// The resolver is `FUN_00182680(group, frame)`:
/// <code>
///   if (0x1f &lt; group - 1U) return 0;          // group 1..32
///   return images[ tbl32[group] + frame / 2 ];  // tbl32 @ 0x364058, images @ DAT_002c4038
/// </code>
/// An effect names its group at `+0x94` and its frame count at `+0x96`; `frame / 2` is why two
/// logical frames share one image, and it is why the source art is numbered 0000, 0002, 0004.
///
/// <see cref="GroupBase"/> is the 32-word table at `0x364058`, dumped from `SLES_500.32`.
/// <see cref="Images"/> is the 74-entry manifest, which `FUN_001826d8` writes out longhand into
/// the pool at `0x2c3b98` (allocated `FUN_00235208(0x2c3b98, 0x4a)` -- 0x4a = 74, exactly the
/// range the table spans).
///
/// ⭐⭐ THE SELF-CHECK THAT SAYS THIS IS RIGHT: every one of the 31 live groups resolves to
/// exactly ONE filename prefix, complete and in order -- g9 is the whole of `PA1a`, g11 the whole
/// of `PA1e`, g16..g31 the sixteen single-image `PB1a`..`PB1p`. Thirty-one for thirty-one, with no
/// group straddling a prefix and no prefix split between groups. An off-by-one anywhere would
/// shear every run.
///
/// ⚠ Index 0 (`PA1r0000`) is named by no group -- group 32 reads 0, which the resolver treats as
/// none -- so it is the null slot.</summary>
public static class ParticleSprites
{
    /// <summary>`0x364058`: group -> first image. Group *n* owns `GroupBase[n] .. GroupBase[n+1]-1`;
    /// index 0 is unused and group 32 reads 0, meaning no sprite.</summary>
    public static readonly int[] GroupBase =
    {
        0, 1, 2, 3, 7, 11, 18, 19, 20, 21, 29, 33, 41, 49, 52, 57,
        58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 0,
    };

    /// <summary>The 74 textures, in the order `FUN_001826d8` registers them. They live in
    /// `/DATA/PARTICLE.WAD` under `/Textures/`.</summary>
    public static readonly string[] Images =
    {
        "PA1r0000", "PA1w0000", "PA1s0000", "PA1c0000", "PA1c0002", "PA1c0004", "PA1c0006",
        "PA1b0000", "PA1b0002", "PA1b0004", "PA1b0006", "PA1f0000", "PA1f0002", "PA1f0004",
        "PA1f0006", "PA1f0008", "PA1f0010", "PA1f0012", "PA1z0000", "PA1n0000", "PA1u0000",
        "PA1a0000", "PA1a0002", "PA1a0004", "PA1a0006", "PA1a0008", "PA1a0010", "PA1a0012",
        "PA1a0014", "PA1d0000", "PA1d0002", "PA1d0004", "PA1d0006", "PA1e0000", "PA1e0002",
        "PA1e0004", "PA1e0006", "PA1e0008", "PA1e0010", "PA1e0012", "PA1e0014", "PA1g0000",
        "PA1g0002", "PA1g0004", "PA1g0006", "PA1g0008", "PA1g0010", "PA1g0012", "PA1g0014",
        "PA1h0000", "PA1h0002", "PA1h0004", "PA1i0000", "PA1i0002", "PA1i0004", "PA1i0006",
        "PA1i0008", "PA1j0000", "PB1a0000", "PB1b0000", "PB1c0000", "PB1d0000", "PB1e0000",
        "PB1f0000", "PB1g0000", "PB1h0000", "PB1i0000", "PB1j0000", "PB1k0000", "PB1l0000",
        "PB1m0000", "PB1n0000", "PB1o0000", "PB1p0000",
    };

    /// <summary>The texture an effect shows at a logical frame, or null when it has no sprite.
    /// ⚠ The console's own guard: a group outside 1..32 draws nothing, and so does frame count 0
    /// (eleven effects, Sparks among them, are untextured and were never sprites at all).</summary>
    public static string For(int group, int frame)
    {
        if (group < 1 || group > 32) return null;
        int i = GroupBase[group] + frame / 2;
        return i > 0 && i < Images.Length ? Images[i] : null;
    }

    /// <summary>The WAD path of a texture name.</summary>
    public static string Path(string image) => $"/Textures/{image}.ssh";
}
