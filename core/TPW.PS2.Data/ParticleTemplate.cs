using System.Buffers.Binary;

namespace TPW.PS2.Data;

/// <summary>The 320-byte record behind one `Tp2.plb` effect, read the way `SLES_500.32` reads it.
///
/// ⭐⭐ EVERY FIELD HERE IS LABELLED `READ` OR `CANDIDATE`, AND `READ` NAMES THE CONSUMER. A record
/// is not a template that gets interpreted: the loader (`0x1467b8`) copies it byte-for-byte into
/// the template table at `0x2ce508`, and a spawn (`0x18b5a8` for `EVENT 1`, `0x18b0f8` for
/// `EVENT 2`) copies the whole 320 bytes again into a live emitter slot at `0x2c46cc + slot*320`
/// and then runs it in place. So the record IS the emitter struct, runtime fields and all, and
/// the file carries stale runtime values (positions at `+0x14`, list links) that the game
/// overwrites at spawn. The walk: the two spawns, the birth function `0x1888a8`, the per-particle
/// update `0x189e78`, the emitter tick `0x1893f8` with its rate function `0x188428`, the emitter
/// mover `0x189270`, the ring mode `0x18c120`, the density scaler `0x18aff0`, the attractor tick
/// `0x189948`, the render-list builder `0x146290`, the render pass `0x220878` and the sprite
/// submit `0x233458`/`0x22a068`, plus the setter API at `0x18a920..0x18ad48`. Two decoders were
/// used -- Ghidra with the R5900 extension, and capstone via `tools/r5900dis.py` -- and the
/// field sets they produce for the birth, update and spawn functions are identical. A census of
/// every code reference to the 320 emitter-field addresses found no reader outside that walk
/// except the setter API, which was then read too. Full account in `findings/particles.md`.
///
/// ⚠ `CANDIDATE` is used exactly once below, on what <see cref="AdditiveBit"/> MEANS, and it says
/// what is still missing.</summary>
public sealed class ParticleTemplate
{
    readonly byte[] _r;

    public ParticleTemplate(byte[] raw)
    {
        if (raw == null || raw.Length < 0x140) throw new ArgumentException("a Tp2.plb record is 320 bytes", nameof(raw));
        _r = raw;
    }

    public static ParticleTemplate Of(ParticleEffect e) => new(e.Raw);

    int I32(int o) => BinaryPrimitives.ReadInt32LittleEndian(_r.AsSpan(o, 4));
    uint U32(int o) => BinaryPrimitives.ReadUInt32LittleEndian(_r.AsSpan(o, 4));
    short I16(int o) => BinaryPrimitives.ReadInt16LittleEndian(_r.AsSpan(o, 2));
    sbyte I8(int o) => (sbyte)_r[o];

    // ---- units, from the consumers ------------------------------------------------------------

    /// <summary>READ. Positions are cells x 640: `EVENT` hands `0x18b5a8` the node position x 10240
    /// (`0x1bbf28`), the spawn stores it `>> 4`, and the render list gives it back `<< 4` at
    /// `0x146290` for `0x220878` to multiply by 1/10240. Velocities are the same unit per tick.</summary>
    public const int PositionUnitsPerCell = 640;

    /// <summary>READ. A particle's drawn width and height in cells is `size * 2 / 10240`: `0x146290`
    /// doubles the size into the render entry and `0x220878` scales it by 1/10240 into the same
    /// world space as the positions. So `size / 5120` cells.</summary>
    public const int SizeUnitsPerCell = 5120;

    /// <summary>READ. The particle system is stepped in fixed ticks of 31 MILLISECONDS (32.3 Hz):
    /// `0x1f6af0` runs `0x18af90` once per 31 units of the game clock `0x2f07a8`, catching up at
    /// most 1500 units; `0x220c78` advances that clock by 10 per count of the timer-0 interrupt
    /// (`0x225128`, INTC 9), and `0x21afc0` programs timer 0 with MODE `0xdc2` (bus clock / 256 =
    /// 576 kHz) and TARGET `0x1680` = 5760, which is 100 Hz -- the executable even logs it as
    /// "timer compare value %d (frequency %d)" with (0x1680, 100). So a unit is a millisecond.
    /// Sparks' 8-tick life is a quarter of a second; ApeSnot's 75 is 2.3 s; a 10-tick emitter is
    /// done in 0.31 s.</summary>
    public const int TickMilliseconds = 31;

    /// <summary>READ. `0x1f6938` initialises the system with density 400 (`0x220800` →
    /// `0x18a3d0(0, 400, 1024)`), in 1/1024ths. `0x18aff0` multiplies <see cref="Burst"/>,
    /// <see cref="MaxLive"/> and the four <see cref="Rate"/> bytes by it at every spawn unless
    /// <see cref="NoDensityScaling"/>; a value that scales to 0 becomes 1. ⚠ So the retail game runs
    /// the library at 39%, and a burst of 40 is 15 on the console. The setter at `0x18a4f0` has no
    /// callers, so 400 is what every park ever ran.</summary>
    public const int RetailDensity = 400;

    /// <summary>READ. `0x1466f0` builds a 512-entry sine table scaled by 256, and every use is
    /// `table * r * 4 >> 10`, which is exactly `sin * r`. Angles into it are 12-bit turns
    /// (4096 = 360°): <see cref="OffsetAngle"/>, <see cref="ShapeRotation"/>, <see cref="OffsetAngularVelocity"/>.</summary>
    public const int AngleUnitsPerTurn = 4096;

    /// <summary>READ. Particle rotation and the spin fields are 16-bit turns: `0x220878` scales the
    /// render entry's angle by 2π/65536.</summary>
    public const int SpinUnitsPerTurn = 65536;

    /// <summary>READ. The generator is `seed = seed * 0x343fd + 0x269ec3; r = seed >> 16` with an
    /// ARITHMETIC shift (`sra` at `0x18b7cc`), so `r % n` is symmetric in (-n, n) and the sites
    /// that mask `r & 0x7fff` first are one-sided 0..n-1. Which form each field uses is stated on it.</summary>
    public const int RngMultiplier = 0x343fd, RngIncrement = 0x269ec3;

    // ---- emitter: lifetime and motion (the EMITTER is a body of its own) ----------------------

    /// <summary>READ `0x1893f8`: decremented every tick unless <see cref="Immortal"/>; the emitter
    /// has expired once negative, spawns <see cref="OnExpiryEffect"/> on the first expired tick and
    /// is freed when its last particle dies. `0x18b5a8` copies it to `+0xd4` so `0x188428` can tell
    /// which quarter of the life it is in. Ticks.</summary>
    public int EmitterLife => I32(0x20);

    /// <summary>READ `0x1893f8`: nonzero, and <see cref="EmitterLife"/> never counts down. 37 of
    /// the 105 effects are immortal -- Fire, Smoke, Steam -- and stop only when something with the
    /// handle sets their life (`0x18aae0`).</summary>
    public bool Immortal => I16(0x26) != 0;

    /// <summary>READ `0x1893f8`: when the emitter's life runs out, this effect is spawned at the
    /// emitter's position. -1 = none. Firework1 (60 → FW1explosion) and FireworkLaser
    /// (62 → LaserFWexplode) are the rocket-then-burst pairs. <see cref="OnExpiryIsAttractor"/> picks the pool.</summary>
    public int OnExpiryEffect => I16(0x10);
    public bool OnExpiryIsAttractor => _r[0xbb] != 0;

    /// <summary>READ `0x189270`, `0x1893f8`: the emitter itself moves by this every tick (the firework
    /// rocket rising at 109/tick), with <see cref="EmitterDrag"/> and <see cref="EmitterGravity"/>.
    /// Also added to every new particle's velocity when <see cref="InheritEmitterVelocity"/>, and
    /// `0x18b5a8` (kind 1 only) jitters each component by a signed `rand % `<see cref="EmitterVelocityJitter"/>.
    /// ⚠ The same three words are read by the spawn as a placement OFFSET (x16) for a CHILD effect
    /// that <see cref="AttachChild"/> attaches -- one field, two uses. Setter `0x18a9d8`.</summary>
    public (int X, int Y, int Z) EmitterVelocity => (I32(0x2c), I32(0x30), I32(0x34));
    public int EmitterVelocityJitter => I16(0x12);
    /// <summary>READ `0x189270`: `v -= v * drag >> 10` per tick.</summary>
    public int EmitterDrag => I16(0x24);
    /// <summary>READ `0x189270`: subtracted from the emitter's Y velocity per tick.</summary>
    public int EmitterGravity => I16(0x28);
    /// <summary>READ `0x189270`: the moving emitter bounces off the terrain height callback
    /// (`0x2c46c4`, halving its velocity). Zero on every shipped record.</summary>
    public bool BounceOffGround => I16(0x76) != 0;

    // ---- emission: how many, how often ---------------------------------------------------------

    /// <summary>READ `0x18b5a8`/`0x18b0f8`: particles born in the spawn call itself, before the
    /// first tick. Density-scaled (`0x18aff0`). Only 15 effects have one; Fire's is 40.</summary>
    public int Burst => I32(0x60);

    /// <summary>READ `0x1893f8`: no emission while this many particles of the emitter are alive.
    /// Density-scaled.</summary>
    public int MaxLive => I16(0x64);

    /// <summary>READ `0x188428`: particles born per tick in each QUARTER of the emitter's life --
    /// [0] for the first quarter through [3] for the last; an immortal emitter (or one whose
    /// `+0xd4` copy is 0) always uses [0]. A NEGATIVE value is one particle every -n ticks
    /// (`0x1893f8`, countdown at `+0x66`). Density-scaled per byte, floor 1 unless already 0.
    /// MumboPuff is (20, 1, 1, 1): a puff then a dribble.</summary>
    public (sbyte Q1, sbyte Q2, sbyte Q3, sbyte Q4) Rate => (I8(0x68), I8(0x69), I8(0x6a), I8(0x6b));

    /// <summary>READ `0x18b5a8`/`0x18b0f8`: skips the `0x18aff0` density pass.</summary>
    public bool NoDensityScaling => _r[0xc0] != 0;

    /// <summary>READ `0x18b5a8`/`0x18b0f8` via the TEMPLATE table (`0x2ce5c9`): when the global at
    /// `0x2c46a8` is set the effect is not spawned at all. 97 of 105 are marked; the eight that are
    /// not (NULL, Repair, RepairData, Avatar, OtherAvatar, Key, MessageTag1, EndOfMessage) are
    /// interface, not scenery.</summary>
    public bool Optional => _r[0xc1] != 0;

    /// <summary>READ `0x18b5a8`, `0x1893f8`, `0x18c008`: 0 is the normal emitter; 1 hands every
    /// spawn and tick to `0x18c120`, an expanding ring whose radius (`+0xcc`) grows by
    /// <see cref="VelocityJitter"/> per tick and births `(radius/128 + 32) * rate` particles around
    /// its edge, flung outward. Any other value emits nothing. Zero on every shipped record.</summary>
    public int EmissionMode => I16(0x0e);

    // ---- birth: where a particle starts --------------------------------------------------------

    /// <summary>READ `0x1888a8`: the emission volume, in position units. All zero → every particle
    /// is born exactly at the emitter. Otherwise <see cref="BoxShape"/> chooses: a box of
    /// `rand % X, rand % Y, rand % Z` (signed, so ±), or a DISC in XZ of radius X at a random
    /// angle -- the full radius when <see cref="RingEdge"/>, else `rand % X` -- and Y as the box.
    /// Y is one-sided (0..Y) when <see cref="PositiveYOnly"/>. Setter `0x18acc0`.</summary>
    public (int X, int Y, int Z) Extent => (I32(0x44), I32(0x48), I32(0x4c));
    public bool BoxShape => _r[0xc2] != 0;
    public bool RingEdge => I16(0xa8) != 0;
    public bool PositiveYOnly => I16(0xa0) != 0;

    /// <summary>READ `0x1888a8` → `0x188828`: the local XZ offset is rotated by this 12-bit angle
    /// before the emitter position is added. Setter `0x18ad48` stores `-angle & 0xfff`.</summary>
    public int ShapeRotation => I32(0xac);

    /// <summary>READ `0x1888a8`: after the volume, the particle is pushed <see cref="RadialOffset"/>
    /// units along <see cref="OffsetAngle"/> in XZ, and `0x1893f8` advances that angle by
    /// <see cref="OffsetAngularVelocity"/> per tick (mod 4096) -- a spiral. Key and Repair use it;
    /// the angular velocity is zero on every shipped record.</summary>
    public int RadialOffset => I16(0xc4);
    public int OffsetAngle => I32(0x54);
    public int OffsetAngularVelocity => I32(0xc8);

    // ---- birth: how a particle moves ------------------------------------------------------------

    /// <summary>READ `0x1888a8`: the velocity every particle starts with, plus a signed
    /// `rand % `<see cref="VelocityJitter"/> per axis -- used when <see cref="RadialSpeed"/> is 0.
    /// `EVENT 2` replaces it: `0x18b0f8` stores `direction * `<see cref="DirectionSpeed"/>` >> 10`,
    /// so the template's own value is what `EVENT 1` gets (nearly always straight up). Setter `0x18a920`.</summary>
    public (int X, int Y, int Z) ParticleVelocity => (I32(0x38), I32(0x3c), I32(0x40));
    public int VelocityJitter => I16(0x50);
    public int DirectionSpeed => I32(0xb0);

    /// <summary>READ `0x1888a8`: nonzero makes the particle a burst instead -- a random XZ direction
    /// at speed `(rand & 0x7fff) % v`, and Y a signed `rand % v`. Sparks is 37, the fireworks 125.
    /// Setter `0x18ac18`.</summary>
    public int RadialSpeed => I32(0x5c);

    /// <summary>READ `0x1888a8`: add the emitter's own velocity to the newborn's.</summary>
    public bool InheritEmitterVelocity => _r[0x6c] != 0;

    // ---- life: how a particle ages ---------------------------------------------------------------

    /// <summary>READ `0x1888a8`, `0x189e78`: ticks to live, plus a signed `rand % (life/4)` per
    /// particle (`0x18c058` in ring mode uses `life/2`). The remaining count is what drives the ramp,
    /// the size and the sprite frame, all as `remaining / initial`. ApeSnot is 75, Sparks 8.</summary>
    public int Life => I32(0x78);

    /// <summary>READ `0x189e78`: `v -= v * drag >> 10` per tick. ApeSnot 218: a fifth per tick.</summary>
    public int Drag => I32(0x7c);

    /// <summary>READ `0x189e78`: subtracted from Y velocity every tick. Negative values lift.</summary>
    public int Gravity => I32(0x80);

    /// <summary>READ `0x189e78`: a signed `rand % v` kick per axis per tick, when any component is
    /// nonzero (the spawn caches that test at `+0x6f`).</summary>
    public (int X, int Y, int Z) Turbulence => (I32(0x84), I32(0x88), I32(0x8c));

    /// <summary>READ `0x1888a8`, `0x189e78`: size at birth and at death, interpolated on
    /// `remaining / initial`. Units per <see cref="SizeUnitsPerCell"/>. ApeSnot grows 800 → 6000.
    /// ⚠ `+0x74` was previously read as a lifetime in milliseconds -- it is a SIZE, a short.</summary>
    public int StartSize => I16(0x74);
    public int EndSize => I16(0xa6);

    /// <summary>READ `0x1888a8`, `0x189e78`: rotation advances by `SpinMin + rand % (SpinMax - SpinMin)`
    /// per tick (one-sided), in 16-bit turns; <see cref="RandomStartRotation"/> picks the birth angle
    /// at random instead of 0.</summary>
    public int SpinMin => I16(0xbe);
    public int SpinMax => I16(0xc6);
    public bool RandomStartRotation => _r[0x6d] != 0;

    /// <summary>READ `0x189e78`: when a particle dies this effect is spawned where it was. -1 = none.
    /// Firework2 → 7 (Explode3): every shell bursts.</summary>
    public int DeathEffect => I32(0x98);

    /// <summary>READ `0x189e78`: particles die on the spot once the emitter has expired.</summary>
    public bool DieWithEmitter => _r[0xaa] != 0;

    // ---- colour and sprite ----------------------------------------------------------------------

    /// <summary>READ `0x1888a8`, `0x189e78`: 0 = the ramp, indexed by `remaining * 15 / initial` --
    /// so a newborn shows step 15 and a dying particle step 0: ⭐ THE RAMP RUNS END-TO-START, from
    /// the consumer this time and not the census. 1 = a random step every tick (Sparks flicker).
    /// Anything else = a random step chosen at birth and kept.</summary>
    public int ColourMode => I32(0x58);

    /// <summary>READ `0x189e78` (`+0xd8 + step*4`), `0x146290`: the sixteen steps, each a
    /// little-endian `0xAARRGGBB`. `0x146290` swaps bytes 0 and 2 before the GS sees it, which is
    /// what a B,G,R,A memory order needs and R,G,B,A would not.</summary>
    public uint Ramp(int step) => U32(0xd8 + Math.Clamp(step, 0, 15) * 4);

    /// <summary>READ `0x146290` (group, count), `0x189e78` (count → logical frame
    /// `(N-1) - (N-1)*remaining/initial`), `0x182680` (group + frame/2 → texture handle).
    /// A count of 0 draws the particle with no texture at all: Sparks, BigSparks, CoasterSparks.</summary>
    public (int Group, int Frames) Sprite => (I16(0x94), I16(0x96));

    /// <summary>READ `0x146290`, `0x220878`: the per-effect render word. `0x146290` ORs in 2 (or
    /// `0x18002` for <see cref="ScreenSpace"/>) and `0x220878` tests exactly one bit of it: bit 2
    /// (value 4) adds `0x60` to the draw flags of every particle of the effect, and NOTHING ELSE in
    /// the record changes how a particle is drawn. The library uses only 0, 4, 0x2000 and 0x2004;
    /// bit 13 has no reader on the walked path.</summary>
    public uint RenderFlags => U32(0x70);

    /// <summary>⚠⚠ IT IS **NOT** THE ADDITIVE FLAG. Master, who can see the real game: "yeah they
    /// arent additive." The name is kept because it is what every note and commit called it, but
    /// nothing blends on it any more -- see `RideParticles`.
    ///
    /// ⭐ The surviving candidate is UNLIT / FULL-BRIGHT. That is the reading that fits the whole
    /// SET list where additive never did: Fire, explosions, fireworks, twinkles, keys, Button,
    /// Repair, BuyLand and the Create/Destroy feedback puffs should all ignore scene lighting,
    /// while smoke, steam, splash and bubbles should take it. Unread, and flagged as such.
    ///
    /// ⚠ CANDIDATE -- the bit is READ, its meaning is not yet. Bit 2 of
    /// <see cref="RenderFlags"/> is set on Fire, Flames, the explosions, every Twinkle and Sparkle,
    /// ApeSnot and the fireworks' bursts, and clear on Smoke, Steam, Splash, WaterFall, Bubbles,
    /// MumboPuff, ApeSmoke and the three bare Sparks. Draw flag `0x20` is what it becomes at
    /// `0x220878`; the flush that turns `0x20` into a GS ALPHA register had not been read when this
    /// was written. On the names it is ADDITIVE -- which is the OPPOSITE polarity to the earlier
    /// `(flags & 4) == 0` guess -- but a name is not a read. See `findings/particles.md`.</summary>
    public bool AdditiveBit => (U32(0x70) & 4) != 0;

    /// <summary>READ `0x146290`, `0x18b5a8`, `0x1893f8`: a screen-space effect -- positions are
    /// projected as `(x*16 - 50000)/49, (z*16 - 37500)/37`, depth is <see cref="ScreenDepth"/>, the
    /// size is `>> 6`, and no bounding box is kept. Eleven of them: Button, BuyLand, GoldenTicket,
    /// the Keys, the message tags.</summary>
    public bool ScreenSpace => _r[0x05] != 0;
    public int ScreenDepth => I16(0x52);

    /// <summary>READ `0x146290`: draw each particle this many thousandths of the way from the
    /// emitter to where it really is. 1000 on every shipped record; `ADDOBJ`'s fourth operand
    /// sets it through `0x18abc8` (`0x1bbf28`), which is what that operand IS.</summary>
    public int DrawTowardEmitterPerMille => I16(0xa2);

    /// <summary>READ `0x146290`: not drawn. Setter `0x18ab28`.</summary>
    public bool Hidden => I16(0x2a) != 0;

    // ---- links to other effects and to the attractors --------------------------------------------

    /// <summary>READ `0x18b5a8`/`0x18b0f8`: spawned alongside, at the same position, and the slot's
    /// `+0xb4` then holds the child's handle. -1 = none. Destroy1..3 bring Twinkle (83), Destroy4
    /// TwinkleSm. A record naming itself is refused (`-1`) to stop the recursion.</summary>
    public int ChildEffect => I32(0xb4);
    /// <summary>READ spawn, `0x1893f8`: the child is offset by ITS OWN `+0x2c..` x16, flagged at
    /// `+0xab`, and moved to the parent's position every tick.</summary>
    public bool AttachChild => I16(0xb8) != 0;
    /// <summary>READ spawn, `0x1893f8`: the child comes from the attractor pool (`0x18bda0`,
    /// 20 templates of 104 bytes -- the `.plb`'s second table) instead of this one.</summary>
    public bool ChildIsAttractor => _r[0xba] != 0;
    /// <summary>READ `0x1893f8`: the child's life is set to -1 when this emitter expires.</summary>
    public bool ChildDiesWithParent => I16(0xbc) != 0;

    /// <summary>READ `0x1888a8` (into particle `+0x2a`), `0x189948`: an attractor acts on particles
    /// whose class matches its own, or on all of them when its class is -1.</summary>
    public int AttractorClass => I8(0x04);
    /// <summary>READ `0x189948`: particles of this emitter ignore class -1 attractors.</summary>
    public bool AttractorImmune => I32(0x9c) != 0;

    // ---- what the executable never reads -------------------------------------------------------

    /// <summary>The bytes no consumer touches. `+0x90` (1 on record 0 only), `+0xa4` (1 on NULL,
    /// YellowStink, GreenPuke, GreenFumes) and `+0xc3` (1 on the two Avatars) are nonzero in the
    /// file and referenced by NO code in the census; `+0x00..0x03`, `+0x06..0x0d`, `+0x14..0x1f`,
    /// `+0x66`, `+0x6e..0x6f`, `+0xab`, `+0xcc..0xd7` and `+0x128..0x13f` are runtime state the
    /// spawn overwrites. See the byte account in `findings/particles.md`.</summary>
    public int Unread90 => I32(0x90);
    public int UnreadA4 => I16(0xa4);
    public int UnreadC3 => I8(0xc3);

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>What `0x18aff0` leaves of a count at a given density (1024 = 100%): `n * d >> 10`,
    /// clamped to 1 if it was positive and scaled to 0, and to 127 for the rate bytes.</summary>
    public static int DensityScaled(int n, int density, bool isByte = false)
    {
        if (n <= 0) return n;
        int v = n * density >> 10;
        if (isByte && v > 0x7f) v = 0x7f;
        return v == 0 ? 1 : v;
    }

    /// <summary>The particles this record makes over a whole emitter life at the retail density,
    /// ignoring the <see cref="MaxLive"/> cap: burst + sum of the per-quarter rates. Negative rates
    /// (one every n ticks) are counted as such. An immortal emitter has no total; this returns the
    /// count over <see cref="EmitterLife"/> ticks as if it were mortal, which is what a script that
    /// stops it after that long would see.</summary>
    public int ExpectedTotal(int density = RetailDensity)
    {
        int burst = DensityScaled(Burst, density);
        var (q1, q2, q3, q4) = Rate;
        int life = Math.Max(EmitterLife, 0);
        int total = burst;
        for (int t = 0; t < life; t++)
        {
            // 0x188428: phase = (remaining * 4) / initial, remaining counting down from life.
            int remaining = life - t;
            int phase = life == 0 ? 3 : remaining * 4 / life;
            sbyte r = phase switch { 0 => q4, 1 => q3, 2 => q2, _ => q1 };
            int n = DensityScaled(r, density, isByte: true);
            total += n >= 0 ? n : (t % -n == 0 ? 1 : 0);
        }
        return total;
    }
}
