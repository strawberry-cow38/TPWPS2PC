using System.Buffers.Binary;

namespace TPW.PS2.Data;

/// <summary>One effect in `Tp2.plb`.
///
/// ⭐⭐ WHAT IS ESTABLISHED AND WHAT IS NOT, kept apart on purpose. <see cref="Name"/>,
/// <see cref="Sprite"/> and <see cref="Ramp"/> are read: the name is where `tools/plb.py` proved
/// it from the loader, the sprite pair is consumed at `0x146290`/`0x189e78`, and the ramp checks
/// out against the effects' own names -- Fire is orange, Smoke is white, ApeSmoke is grey, and a
/// wrong byte order would make all three nonsense. The rest of the 320-byte record is given as
/// <see cref="Raw"/> with the offsets named as CANDIDATES, because their meaning comes from the
/// shape of their values and not from a consumer, and a number that reads plausibly is exactly
/// the kind of thing that turns into a fact by being used.</summary>
public sealed class ParticleEffect
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>(sprite group, how many logical frames it has).</summary>
    public (int Group, int Frames) Sprite { get; init; }
    /// <summary>Sixteen steps of colour over the particle's life, `0xAARRGGBB`.</summary>
    public uint[] Ramp { get; init; } = Array.Empty<uint>();
    public byte[] Raw { get; init; } = Array.Empty<byte>();

    public int RawAt(int offset) => BinaryPrimitives.ReadInt32LittleEndian(Raw.AsSpan(offset, 4));

    /// <summary>⚠⚠ CANDIDATE, and the reason the code no longer hardcodes additive blending.
    /// `+0x70` is a BINARY field across the library -- 0 on 56 effects and 4 on the other 49 --
    /// and the two effects whose right answer is already known land on opposite sides: `Sparks`
    /// reads 0 and `ApeSnot` reads 4. A spark glows; snot does not.
    ///
    /// ⭐ Drawing everything ADDITIVELY is what "way too opaque" looks like: additive blending
    /// cannot darken, so over a bright park it saturates towards white and no amount of alpha
    /// makes it read as translucent. Half the library was being drawn that way.
    ///
    /// ⚠ NO CONSUMER READ. This is a two-known-answers test over a binary field, not the
    /// executable's own branch. `+0x58` (a 0..3 enum) and `+0x71` (0 or 32, adjacent and probably
    /// the same flags word) also separate the two and are the next candidates if this is wrong.
    ///
    /// ⚠⚠ **AND THE TWO-EFFECT ARGUMENT DOES NOT SURVIVE THE FULL CENSUS** (2026-09-26,
    /// `--particle-unread`). Bit 2 is SET on 49 effects and CLEAR on 56, and BOTH sides hold
    /// glowing and non-glowing things:
    ///   SET   includes Fire, Flames, every Explode/Firework/Twinkle, LaserRing, BeamUp,
    ///         GoldenTicket -- but also YellowStink, GreenFumes, ApeSnot, Button, Repair,
    ///         BuyLand, Upgrade, MessageTag1, EndOfMessage.
    ///   CLEAR includes Smoke, Steam, Splash, Bubbles -- but also Sparks, CoasterSparks,
    ///         BigSparks, GoldSparkles, DemonFire, FlyFire, PlasmaSphere.
    /// "A spark glows; snot does not" picked the one pair that suits the reading. Sparks is not
    /// alone on its side and snot is not alone on its: three separate SPARK effects are clear and
    /// two STINK effects are set.
    ///
    /// ⭐ A hypothesis that fits the whole SET list where "additive" does not: bit 2 is
    /// **unlit / full-bright**, not a blend mode. Fire, explosions, fireworks, twinkles, keys,
    /// buttons, Repair, BuyLand and the Create/Destroy feedback puffs are all things that should
    /// ignore scene lighting; smoke, steam, splash and bubbles are things that should take it.
    /// That is a CANDIDATE too, and it is recorded so the next pass has two readings to separate
    /// rather than one to confirm.
    ///
    /// ⭐⭐ Either way the honest status is UNKNOWN, not "additive". The value kept here is the
    /// one already shipped -- changing a guess for a different guess is not progress -- but it is
    /// no better supported than its opposite, and it should not be cited as decoded.
    public bool Additive => (RawAt(0x70) & 4) != 0;

    /// <summary>⚠ CANDIDATE, from the shape of the values: `+0x74` is 100 for Sparks, 300 for
    /// MumboPuff, 800 for ApeSnot and 1500 for Fire and ApeSmoke -- an ordering that matches how
    /// long each of those should hang about, in milliseconds. No consumer read.</summary>
    [Obsolete("+0x74 is the START SIZE, not a lifetime -- READ from 0x146290/0x220878, drawn " +
              "width = size/5120 cells. Use ParticleTemplate.StartSize / EndSize, and " +
              "ParticleTemplate.Life for the lifetime.")]
    public int LifetimeGuess => RawAt(0x74);

    /// <summary>⚠ CANDIDATE: `+0x78` is 8 for Sparks, 25 for MumboPuff, 40 for ApeSmoke, 75 for
    /// ApeSnot, 100 for Fire. Read here as how many particles the effect makes.</summary>
    [Obsolete("+0x78 is the particle's LIFE IN TICKS, not a count -- READ from 0x1888a8. " +
              "There is no count field: emission is ParticleTemplate.Burst plus the per-quarter " +
              "Rate bytes, capped by MaxLive and scaled by the retail density. Use " +
              "ParticleTemplate.ExpectedTotal().")]
    public int CountGuess => RawAt(0x78);

    /// <summary>⚠ CANDIDATE: `+0x44`, `+0x48` and `+0x4c` are usually three EQUAL numbers -- 9 for
    /// Fire, 15 for ApeSmoke, 18 for MumboPuff, 31 for ApeSnot -- which is the shape of a size or
    /// a speed given per axis. Read here as a size.</summary>
    public int SizeGuess => RawAt(0x44);

    /// <summary>The ramp step at a fraction of the particle's life, as (r, g, b, a) bytes.</summary>
    public (byte R, byte G, byte B, byte A) ColourAt(float life)
    {
        if (Ramp.Length == 0) return (255, 255, 255, 255);
        // ⭐⭐ THE RAMP RUNS END-TO-START. Sampled forwards, 71 of the 105 effects get MORE opaque
        // as they age and only 8 fade out, and 58 begin at alpha 0 against 17 that end there --
        // i.e. almost every effect on the disc would wink into existence invisible and then
        // vanish at its most solid. Reversed, 71 fade out and 58 end at nothing, which is what a
        // puff of snot, a spark and a cloud of green all actually do.
        //
        // That is master's report: "right shape, too opaque". The shape was always right (it
        // comes from the sprite's own alpha); what was missing was the FADE, because the fade was
        // being played backwards and the particle was at maximum alpha at the instant it died.
        //
        // ⚠ THIS IS A READING FROM THE DATA, NOT FROM A CONSUMER. Nobody has walked the
        // executable's ramp lookup. The evidence is a 71-against-8 census plus the fact that the
        // alternative is physically silly, which is strong but is not the same as having read it.
        // If the PS2's sampler is ever decompiled, this is the line it settles.
        int i = Math.Clamp((int)((1f - life) * Ramp.Length), 0, Ramp.Length - 1);
        uint c = Ramp[i];
        return ((byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)(c >> 24));
    }
}

/// <summary>`Tp2.plb` -- the 105 particle effects, from `/DATA/PARTICLE.WAD`.
///
/// ⭐⭐ THIS IS WHAT `EVENT 1` AND `EVENT 2` NAME. `0x1bbf28` resolves the instruction's node and
/// hands the third operand to `0x18b5a8`/`0x18b0f8`, which bound it at `0x69` -- 105 -- and copy a
/// per-type template. This file declares exactly 105 records of 320 bytes. The mapping validates
/// itself: the ride named **Mumbo** asks for effect **31**, and effect 31 is named `MumboPuff`;
/// **Crazy Ape** asks for **22** from two nodes over and over, and 22 is `ApeSnot`.
///
/// ⚠⚠ THE TEMPLATES ARE ZERO IN THE EXECUTABLE. `0x2ce508` reads as 105 records of nothing in the
/// image because the loader fills them from this file. Read the file, never the image.</summary>
public sealed class ParticleLibrary
{
    public IReadOnlyList<ParticleEffect> Effects { get; }
    public ParticleEffect this[int id] => id >= 0 && id < Effects.Count ? Effects[id] : null;

    /// <summary>Where the sixteen colour steps start, and how many there are.</summary>
    public const int RampOffset = 0xD8, RampSteps = 16;

    public ParticleLibrary(byte[] plb)
    {
        if (plb == null || plb.Length < 8) throw new InvalidDataException("Tp2.plb is empty");
        int count = BinaryPrimitives.ReadInt32LittleEndian(plb.AsSpan(0, 4));
        int size = BinaryPrimitives.ReadInt32LittleEndian(plb.AsSpan(4, 4));
        // ⚠ A CHECK THAT CAN FAIL. 0x18b5a8 bounds the id at 105 and the record must be big enough
        // to hold a name at +0x118, so a file that disagrees is not this library.
        if (count <= 0 || count > 1024 || size < 0x120 || 8 + (long)count * size > plb.Length)
            throw new InvalidDataException($"Tp2.plb declares {count} records of {size}, which does not fit");

        var all = new List<ParticleEffect>(count);
        for (int i = 0; i < count; i++)
        {
            int o = 8 + i * size;
            int end = plb.AsSpan(o + 0x118, size - 0x118).IndexOf((byte)0);
            var ramp = new uint[RampSteps];
            for (int k = 0; k < RampSteps; k++)
                ramp[k] = BinaryPrimitives.ReadUInt32LittleEndian(plb.AsSpan(o + RampOffset + k * 4, 4));
            all.Add(new ParticleEffect
            {
                Id = i,
                Name = end <= 0 ? "" : System.Text.Encoding.ASCII.GetString(plb, o + 0x118, end),
                Sprite = (BinaryPrimitives.ReadInt16LittleEndian(plb.AsSpan(o + 0x94, 2)),
                          BinaryPrimitives.ReadInt16LittleEndian(plb.AsSpan(o + 0x96, 2))),
                Ramp = ramp,
                Raw = plb[o..(o + size)],
            });
        }
        Effects = all;
    }

    public ParticleEffect Find(string name) =>
        Effects.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>⭐ THE RAMP READ BACK AGAINST THE NAMES, which is the only check available:
    /// nobody wrote down that `+0xd8` is a colour in `A, R, G, B` order, so the evidence is that
    /// reading it that way makes six effects match what they are called -- `Fire` (190, 116, 0),
    /// `Flames` (190, 128, 0), `DemonFire` (184, 101, 36), `Smoke` and `BigSmokePuff` white,
    /// `ApeSmoke` (128, 128, 128) and `GreenSmokePuff` (0, 223, 0). No other byte order gets more
    /// than one of those right.
    ///
    /// ⚠⚠ THE FIRST VERSION OF THIS TEST REPORTED TEN FAILURES AND EVERY ONE WAS ITS OWN FAULT.
    /// It matched "fire" inside `Firework1` and `FireworkLaser`, which are green because fireworks
    /// are, and flagged `GreenSmokePuff` for being coloured when its name says so. A test that
    /// rejects correct data is worse than no test: it spends the reader's attention and then
    /// trains them to ignore it. The families below are the ones whose names genuinely fix a
    /// colour, and `Firework` is excluded by name rather than by silently dropping failures.
    ///
    /// ⚠ `SmokeTrailR`, `SmokeTrailB` and `SmokeTrailW` come out pale green, cyan and blue. If
    /// those letters meant red, blue and white the ordering would be wrong -- but no ordering
    /// makes all three right, and one that did would break the six above. Recorded as an open
    /// oddity: the letters are probably not colours.</summary>
    public IEnumerable<(ParticleEffect Effect, string Why)> RampDisagreements()
    {
        foreach (var e in Effects)
        {
            if (e.Ramp.All(c => c == 0)) continue;
            string n = e.Name;
            bool Has(string s) => n.Contains(s, StringComparison.OrdinalIgnoreCase);
            if (Has("Firework") || Has("Trail")) continue;   // named above, and said why
            var (r, g, b, _) = e.ColourAt(0.5f);
            int spread = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            if (Has("Green") && !(g > r && g > b))
                yield return (e, $"a green whose midpoint is not green: ({r}, {g}, {b})");
            else if ((Has("Fire") || Has("Flame")) && r <= b)
                yield return (e, $"a fire whose midpoint is not warm: ({r}, {g}, {b})");
            else if (Has("Smoke") && !Has("Torch") && spread > 64)
                yield return (e, $"a smoke whose midpoint is coloured: ({r}, {g}, {b})");
        }
    }
}
