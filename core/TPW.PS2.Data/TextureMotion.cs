namespace TPW.PS2.Data;

/// <summary>How one texture moves on the surface wearing it: a UV scroll, a twist about the
/// texture's middle, or neither.
///
/// ⭐⭐ THE ENGINE'S OWN SHAPE, not one invented here. `SLES_500.32` carries a reflection schema
/// of 60-byte field descriptors, and one of its records is a per-texture struct:
///
///     0x2acf20  pcTextureFilename
///     0x2acf5c  bIsSelfIlluminating
///     0x2acf98  fScrollRate
///     0x2acfd4  asTextureData          <- the array those three fields describe
///
/// So a texture in this game carries a FILENAME, a self-illuminating flag and a SCROLL RATE. That
/// is the mechanism the port was missing, and it is a property of the TEXTURE -- which is why
/// looking for a "this surface is water" flag on the material never found one and never could.
///
/// ⭐ The feature is confirmed a second time, and under the engine's own name, in the attraction
/// schema, where two sibling switches sit next to each other:
///
///     0x2e7c90  TurnOffAnimatingTextures     <- the APS frame-replacement player (already ported)
///     0x2e7ccc  TurnOffScrollingTextures     <- THIS, and it was not ported
///
/// An attraction can turn scrolling off, so scrolling is ON by default and is a separate
/// subsystem from frame replacement. Those two names are the whole argument that this file
/// describes something real rather than something desirable.
///
/// ⭐⭐ SUPERSEDED IN PART, AND READ THE NOTE: `fScrollRate` is the COASTER TRACK's authored
/// field and is NOT how the park's moving textures work. Those are an APS animation channel --
/// track flag `0x10000`, payload at `track + 0x24`, per-vertex UV keyframes linearly interpolated
/// by `0x1ad378`. The Coconut's keys are a measured circle about (1.5, 0.5) turning a constant
/// 4.8 degrees a frame, i.e. one revolution per its 75-frame `Main` record. So the RATE IS READ
/// after all, per object, from the record's own length -- see `AnimatedModel.AuthoredSpin` and
/// findings/animated-textures.md. `SwirlRadiansPerSecond` below is only the fallback for a model
/// that has no such track.
///
/// ⚠⚠ WHAT IS NOT READ: the `fScrollRate` VALUES. `asTextureData` belongs to the coaster/track
/// schema (its neighbours are `asCrossSectionPoints1..12`, `asCarTypes`, `sCoasterType`), and no
/// `.sam` ships on this disc -- the only authored files are three `.dba`. The texture names
/// themselves live in an index-addressed table, not pointed at by any `lui`/`addiu` pair: a sweep
/// for references to `justwater.ssh` returns nothing while the same sweep finds eight references
/// to a control address. So no per-texture rate could be read off this disc, and the RATES below
/// are still the PSX's measured one-row-a-frame. Which textures move is read; how fast is not.</summary>
public readonly record struct TextureMotion(float ScrollU, float ScrollV, float Spin)
{
    /// <summary>A texture that does not move.</summary>
    public static readonly TextureMotion Still = new(0f, 0f, 0f);

    public bool Moves => ScrollU != 0f || ScrollV != 0f || Spin != 0f;

    /// <summary>⭐ The PSX rolls a flagged sprite by ONE ROW PER FRAME and re-uploads it. At the
    /// console's 25 frames a second over a 64-high texture that is 25/64 of the texture a second.
    /// This is the one number here that was measured rather than chosen.</summary>
    public const float ScrollTexelsPerTick = 1f;
    public const float AssumedTextureHeight = 64f;

    /// <summary>⚠ FALLBACK ONLY, for a swirl with no `0x10000` track to state its own rate.
    /// The Coconut does have one and turns at 2.513 rad/s (one turn per 75 frames at 30fps);
    /// this 0.9 was a guess and was about 2.8x too slow, which is exactly the kind of number
    /// that survives because it "looks fine". Radians a second.</summary>
    public const float SwirlRadiansPerSecond = 0.9f;

    /// <summary>Texture-name stems whose surface is moving water.
    ///
    /// ⭐⭐ `dk_water` IS ON THIS LIST BECAUSE IT IS THE SAME IMAGE AS `wr_water`. Decoded and
    /// compared, `dk_water3` correlates with `wr_water3` at r = 0.9946 with no offset, at a mean
    /// luminance ratio of 0.321 -- it is that texture in shadow, texel for texel. The control,
    /// `wr_water3` against an unrelated texture, correlates at r = 0.012, so the test discriminates.
    /// Two pixel-aligned copies of one surface MUST move together; leaving the dark one out is
    /// what made the shadow under the jungle bridge sit still while the river slid underneath it.
    ///
    /// ⚠ This list is a FILTER, and a filter is where the next missing surface will hide. It is
    /// written as stems rather than exact names so a numbered sibling cannot fall through, and
    /// `MovingWaterNames` exists so an audit can print what it actually matched.</summary>
    public static readonly string[] WaterStems =
        { "wr_water", "dk_water", "jri_sur", "jri_lak", "justwater" };

    /// <summary>Texture stems that are a swirl of liquid seen from above, which twists in place.
    ///
    /// ⭐ READ OFF THE ART, not the name. Both are decoded spirals centred in their own frame:
    /// `cn_nut2a` (64x64, the JUNGLE Coconut's drink) and `FDrink_Liquid` (32x32, the FANTASY
    /// drinks stall). A spiral drawn centred in its texture is drawn to be turned about that
    /// centre; nothing else explains why the art is a spiral at all. Master, watching the game:
    /// "the 'liquid' in the coconut should twist".</summary>
    public static readonly string[] SwirlStems = { "cn_nut2a", "fdrink_liquid" };

    static bool Has(string name, string[] stems)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var s in stems)
            if (name.Contains(s, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static bool IsWater(string textureName) => Has(textureName, WaterStems);
    public static bool IsSwirl(string textureName) => Has(textureName, SwirlStems);

    /// <summary>The motion a MODEL's texture gets. ⚠ The terrain's water is NOT resolved here:
    /// the sea and the river run at different angles and only the top sea plane waves, which are
    /// facts about the terrain's layout rather than about a texture, so <see cref="Water"/> keeps
    /// that decision. This answers for a texture on a placed model -- a shop, a ride, a prop.</summary>
    public static TextureMotion ForModelTexture(string textureName)
    {
        if (IsSwirl(textureName)) return new TextureMotion(0f, 0f, SwirlRadiansPerSecond);
        if (IsWater(textureName))
        {
            float v = ScrollTexelsPerTick * 25f / AssumedTextureHeight;
            return new TextureMotion(0f, v, 0f);
        }
        return Still;
    }
}
