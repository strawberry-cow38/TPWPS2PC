using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>The particles a ride's script asks for, made visible.
///
/// ⭐⭐ THE SCRIPT CHOOSES THEM AND THE DISC DESCRIBES THEM. `EVENT 2 1 22` in Crazy Ape's
/// bytecode means "effect 22 at node 1", effect 22 in `Tp2.plb` is called `ApeSnot`, and the
/// sixteen colours it fades through are in that same record. Nothing here picks an effect, a
/// colour or a place: the id comes from the ride's own script, the colour ramp from the game's
/// own library, and the position from the node the instruction named.
///
/// ⭐⭐ THE SPRITE IS NOW THE DISC'S. `0x182680` resolves a group and frame to a real texture --
/// `images[tbl32[group] + frame/2]`, the table at `0x364058` and the 74-name manifest written out
/// longhand by `FUN_001826d8` -- so ApeSnot draws `PA1e0000..0014` and the drinks shop draws
/// `PA1u0000`, both out of PARTICLE.WAD. See `ParticleSprites`. Every effect used to get a
/// generated white dot; only the eleven UNTEXTURED ones do now.
///
/// ⚠⚠ THE MOTION IS STILL OURS. What is read is the id, the node, the colour ramp and now the
/// sprite; what is NOT wired is the emitter's velocity model, its spread or its gravity -- the
/// record HAS them (`+0x2c`, `+0x24`, `+0x28`, decoded in `ParticleTemplate`) and this file still
/// moves particles the way it invented. So a burst is in the right place, at the right moment, in
/// the right colours, wearing the right art, and moving the way THIS file says. Said plainly
/// because the last three of those looked like a finished feature while the sprite was a
/// placeholder.</summary>
public sealed class RideParticles
{
    readonly Node3D _root;
    readonly ParticleLibrary _library;
    /// <summary>Draws each one-shot's native plan: particle lives within the record's jitter, which decide
    /// when a capped emitter has room again (<see cref="ParticleTemplate.Plan"/>).</summary>
    readonly Random _rng = new();
    /// <summary>PARTICLE.WAD, open, so the sprites can be pulled out of it. ⚠ Null is allowed and
    /// means the placeholder: a park with no art must still run.</summary>
    readonly AssetLibrary _wad;
    readonly Dictionary<int, (Texture2D Sheet, int Frames)> _sheets = new();
    readonly List<(CpuParticles3D Node, ulong Until)> _live = new();
    /// <summary>⭐⭐ THE BIRTH POINT ORBITS -- master's "long straight line of sparkles".
    ///
    /// `0x1888a8` pushes each newborn <see cref="ParticleTemplate.RadialOffset"/> units along
    /// <see cref="ParticleTemplate.OffsetAngle"/> in XZ, and the emitter tick `0x1893f8` advances
    /// that angle by <see cref="ParticleTemplate.OffsetAngularVelocity"/> every 31 ms. So the place
    /// particles are born from SWEEPS ROUND A CIRCLE while they rise: a helix. `Repair` emits one
    /// sparkle per tick for 120 ticks at 200/4096 of a turn per tick -- 121 sparkles over 5.9
    /// revolutions of a 0.98-cell circle, which is the spiral master could see was missing.
    ///
    /// ⚠⚠ NOTHING IN THE TREE READ THE THREE FIELDS, so all 121 were born at ONE POINT with 0
    /// degrees of spread and no speed jitter -- identical motion from an identical place is a
    /// LINE, which is exactly what he got. They were skipped because the decoder's own summary
    /// claimed the angular velocity was zero on every shipped record; it is non-zero on all three
    /// records that use a radial offset. See `ParticleTemplate.RadialOffset`.
    ///
    /// ⭐ DONE BY MOVING THE EMITTER, NOT BY SHAPING THE EMISSION. A Godot emission shape hands out
    /// RANDOM points, which would give a cylinder of sparkles; the console's birth point is a
    /// single marching one. Moving the node reproduces it exactly -- each frame's newborns appear
    /// wherever the node is now -- and it needs <see cref="CpuParticles3D.LocalCoords"/> OFF so the
    /// ones already born stay where they were born instead of being dragged round with it.
    ///
    /// ⚠ THE RADIUS AND THE RATE ARE THE RECORD'S; THE SENSE IS NOT. Which of X and Z takes the
    /// sine is not in the disassembly notes -- `findings/particles.md` records the sine table and
    /// the 12-bit turn, not the axis pair -- so whether the helix twists with the clock or against
    /// it is this file's choice, made to match the port's usual handedness flip (console +Z is our
    /// -Z). Said plainly because every other number here is measured.</summary>
    readonly List<(CpuParticles3D Node, Vector3 Centre, float Radius, float RadPerSec, float Angle)> _orbit = new();

    /// <summary>The record's radial offset as a displacement, for an angle in RADIANS.</summary>
    static Vector3 Orbit(float radius, float radians) =>
        radius == 0f ? Vector3.Zero
        : new Vector3(radius * Mathf.Cos(radians), 0f, -radius * Mathf.Sin(radians));
    static Texture2D _dot;

    public RideParticles(Node3D parent, ParticleLibrary library, AssetLibrary wad = null)
    {
        _library = library;
        _wad = wad;
        _root = new Node3D { Name = "Particles" };
        parent.AddChild(_root);
    }

    public int Spawned { get; private set; }

    /// <summary>⭐⭐ THE CONSOLE'S OWN MOTION, converted into Godot's units.
    ///
    /// The record states all of it and `ParticleTemplate` documents the arithmetic:
    /// <see cref="ParticleTemplate.ParticleVelocity"/> is per TICK in position units, positions are
    /// cells x <see cref="ParticleTemplate.PositionUnitsPerCell"/>, and a tick is
    /// <see cref="ParticleTemplate.TickMilliseconds"/>. So a speed in cells per second is
    /// `v / 640 / 0.031`, and a gravity -- which `0x189e78` subtracts from Y velocity EVERY tick,
    /// making it an acceleration -- is that again per tick, `g / 640 / 0.031^2`.
    ///
    /// ⭐ `RadialSpeed` nonzero means the record wants a BURST, not a jet: `0x1888a8` gives a
    /// random XZ direction at `(rand &amp; 0x7fff) % v` with Y a signed `rand % v`. That is a
    /// sphere, so the cone opens to 180 degrees and the direction stops mattering.
    ///
    /// ⭐⭐ DRAG IS EXPONENTIAL, AND SO IS THIS NOW. The console does `v -= v * drag >> 10` every
    /// tick -- decay proportional to the speed, a fifth gone per tick for ApeSnot. Godot's damping
    /// subtracts a CONSTANT per second, so my first version matched only the total TRAVEL and got
    /// the journey wrong: the console dumps most of its speed in the first few ticks and crawls,
    /// mine slowed evenly.
    ///
    /// ⭐ `DampingCurve` fixes the shape. Damping is sampled as `DampingMax * curve(t/life)`, so
    /// setting `curve(s) = e^(-lambda * s * life)` and `DampingMax = lambda * v0` gives
    /// `dv/dt = -lambda*v0*e^(-lambda t)`, which integrates to exactly `v(t) = v0*e^(-lambda t)`.
    /// With `lambda = -ln(1 - drag/1024) / tick` that reproduces the console's velocity EXACTLY at
    /// every tick boundary: `v(n*tick) = v0 * k^n`.
    ///
    /// ⚠ Two residuals, named rather than buried. (1) Total travel is ~11% shorter than the
    /// console's, because the console holds each tick's velocity constant across the tick (a
    /// rectangle sum) while this integrates the curve -- 0.73 cells against 0.82 for ApeSnot. I
    /// chose to match the VELOCITY exactly rather than bend lambda to fix the distance; the
    /// difference is under a tenth of a cell. (2) `DampingMax` needs the particle's OWN v0 and one
    /// material serves them all, so it uses the mean of the speed range -- for ApeSnot that range
    /// is 5.34..5.95, about 10% wide, and a wider spread would drift.
    ///
    /// ⚠ SUPERSEDED NOTE, kept because the reasoning was wrong in an instructive way: The console does
    /// `v -= v * drag >> 10` every tick -- exponential decay, a fifth per tick for ApeSnot -- and
    /// Godot's `Damping` subtracts a CONSTANT per second, which is a different CURVE. I first
    /// wrote that off as "owed" and then did the arithmetic: ApeSnot leaves at 5.65 cells/s and
    /// lives 2.33s, so undamped it travels **13 cells** where the console's drag carries it
    /// **0.82** -- sixteen times too far. That is not a missing refinement, it is smoke that
    /// shoots off the screen instead of hanging by the ape's face.
    ///
    /// I matched total travel with a linear damping and called the easing "still wrong". It was;
    /// the curve was the fixable part all along.</summary>
    /// <summary>⭐⭐ THE EMISSION AREA, from the record's own extent (`+0x44..0x4C`).
    ///
    /// A zero extent stays a POINT -- most effects are one, and a puff that should come from a
    /// single spot must not be spread. The ones that carry an extent are the ones master noticed:
    /// the drinks shop's bubbles are a disc, so they rise from scattered points rather than one
    /// stream.
    ///
    /// ⚠ The DISC is the default and the box is the exception (`+0xc2`), which is the opposite
    /// way round from how it reads. And `+0xa8` means born on the disc's EDGE, not inside it --
    /// a ring rather than a filled circle, which Godot does with an inner radius.</summary>
    static CpuParticles3D.EmissionShapeEnum EmissionOf(
        ParticleTemplate t, out Vector3 box, out float ringRadius, out float ringInner, out float ringHeight)
    {
        const float cell = ParticleTemplate.PositionUnitsPerCell;
        box = Vector3.Zero; ringRadius = 0f; ringInner = 0f; ringHeight = 0f;
        var (ex, ey, ez) = t.Extent;
        if (ex == 0 && ey == 0 && ez == 0) return CpuParticles3D.EmissionShapeEnum.Point;

        float x = Math.Abs(ex) / cell, y = Math.Abs(ey) / cell, z = Math.Abs(ez) / cell;
        if (t.BoxShape)
        {
            box = new Vector3(x, y, z);
            return CpuParticles3D.EmissionShapeEnum.Box;
        }
        // ⚠ A disc with no radius is not a disc; fall back to the point rather than to a ring of
        // radius zero, which Godot draws as a single line of particles.
        if (x <= 0f) return CpuParticles3D.EmissionShapeEnum.Point;
        ringRadius = x;
        ringInner = t.RingEdge ? x : 0f;            // `+0xa8`: on the edge
        ringHeight = t.PositiveYOnly ? y : y * 2f;  // `+0xa0`: one-sided in Y
        return CpuParticles3D.EmissionShapeEnum.Ring;
    }

    static (Vector3 Direction, float SpreadDegrees, float SpeedMin, float SpeedMax, float Gravity,
            float Damping, float Lambda) Motion(ParticleTemplate t, Vector3? fireAlong)
    {
        const float cell = ParticleTemplate.PositionUnitsPerCell;
        float tick = ParticleTemplate.TickMilliseconds / 1000f;
        float PerSecond(float v) => v / cell / tick;

        float gravity = PerSecond(t.Gravity) / tick;   // an acceleration: per tick, per tick

        // ⚠ k is the per-tick fraction the console removes; 0 means no drag and no damping.
        // ⚠ `k` here is the FRACTION REMAINING per tick, not the fraction removed.
        float kept = 1f - t.Drag / 1024f;
        float lambda = t.Drag > 0 && kept > 0f && kept < 1f ? -Mathf.Log(kept) / tick : 0f;
        // ⭐⭐ THE DAMPING MUST BE KEYED TO THE **FASTEST** PARTICLE, NOT THE AVERAGE.
        // Godot damps SUBTRACTIVELY -- `|v| -= damping(t) * dt`, clamped at a dead stop -- while
        // the console multiplies, `v *= k` per tick. The curve makes damping(t) = D*e^(-lambda t),
        // which reproduces `v = v0*e^(-lambda t)` EXACTLY, but only for the one speed where
        // D == lambda*v0. Godot draws a particle's speed and its damping from INDEPENDENT randoms,
        // so D cannot track each particle's own v0.
        //
        // Keying D to the middle of the speed range left every particle born above it with a
        // permanent residual v0 - D/lambda and it crawled away forever. MEASURED, not reasoned:
        // the single-particle probe (--fx-probe, RideScriptDemo) fitted lambda = 4.95/s against
        // the record's 7.72 -- a log fit reads low exactly when the speed decays to a non-zero
        // floor instead of to zero. Keying D to SpeedMax makes the fastest particle exact and
        // every slower one stop a little early, which is also what the console does: it moves
        // particles in INTEGER units, so anything under one unit per tick has already stopped.
        float Damping(float vMax) => lambda > 0f && vMax > 0f ? lambda * vMax : 0f;

        // ⭐⭐ EVENT 2: the fitting points, and DirectionSpeed says how hard. The template's own
        // velocity never runs for these -- see the parameter's note on Emit.
        if (fireAlong is { } along && t.DirectionSpeed != 0)
        {
            float ds = PerSecond(t.DirectionSpeed);
            float j = PerSecond(t.VelocityJitter);
            float sp = Math.Abs(ds);
            return (ds >= 0 ? along : -along,
                    Mathf.Clamp(sp > 0.001f ? Mathf.RadToDeg(Mathf.Atan2(j, sp)) : (j > 0f ? 180f : 0f), 0f, 180f),
                    Math.Max(0f, sp - j), Math.Max(0.01f, sp + j), gravity,
                    Damping(Math.Max(0.01f, sp + j)), lambda);
        }

        if (t.RadialSpeed > 0)
        {
            float r = PerSecond(t.RadialSpeed);
            // ⚠ The XZ speed is `rand % v` and Y is a SIGNED `rand % v`, so the fastest particle
            // is the one that rolls high on both -- but Godot draws one speed per particle and
            // fires it along a cone, so the range is 0..v and the cone is the whole sphere.
            return (Vector3.Up, 180f, 0f, Math.Max(0.01f, r), gravity, Damping(r), lambda);
        }

        var (vx, vy, vz) = t.ParticleVelocity;
        var v3 = new Vector3(vx, vy, vz);
        float speed = PerSecond(v3.Length());
        // ⚠ A record with no velocity at all still has to point somewhere; up is the console's own
        // default for `EVENT 1` and the jitter is what actually moves it.
        var dir = v3.LengthSquared() > 0 ? v3.Normalized() : Vector3.Up;
        // ⭐ The cone comes from the jitter the birth adds per axis, against the speed it is added
        // to: a big jitter on a slow particle is a wide spray, the same jitter on a fast one is
        // barely a wobble. With no speed at all the jitter IS the motion, so it opens right up.
        float jitter = PerSecond(t.VelocityJitter);
        float spread = speed > 0.001f
            ? Mathf.RadToDeg(Mathf.Atan2(jitter, speed))
            : (jitter > 0f ? 180f : 0f);
        return (dir, Mathf.Clamp(spread, 0f, 180f), Math.Max(0f, speed - jitter),
                Math.Max(0.01f, speed + jitter), gravity,
                Damping(Math.Max(0.01f, speed + jitter)), lambda);
    }

    /// <summary>`e^(-lambda * s * life)` over the particle's life, as a Godot `Curve`.
    ///
    /// ⚠ The points are spaced QUADRATICALLY, not evenly. At ApeSnot's lambda of 7.7 per second
    /// the curve has fallen to a thousandth within the first eighth of its life, so evenly spaced
    /// samples would put almost every point in the flat tail and let a straight line cut the
    /// corner exactly where all the motion is.
    ///
    /// ⚠ Returns null when there is no drag -- a flat curve of 1.0 would be harmless but says
    /// "damping, shaped", and null says "no damping", which is the truth.</summary>
    /// ⭐⭐ THE CURVE IS RIGHT AND THE ANSWER IS STILL SHORT, BECAUSE THE ENGINE INTEGRATES IT
    /// BY FORWARD EULER. Godot subtracts `damping(age) * dt` from the speed once a frame, and at
    /// 60fps this decay removes 12% per step -- far too stiff for that to approximate the
    /// continuous `v = v0*e^(-lambda t)`. The stepped speed runs out early and CLAMPS AT ZERO,
    /// which is what eats the tail: the in-engine stepper measured 0.669 cells against the
    /// record's 0.770, and the rendered particle measured 0.670. Three digits of agreement, so
    /// the loss is arithmetic, not the particle system.
    ///
    /// So solve for the gain that makes the DISCRETE sum land on the continuous answer, using the
    /// same loop the engine runs rather than a closed form -- which order Godot applies damping
    /// and motion in is exactly the sort of assumption that has been wrong twice today.
    /// ⚠ The gain is frame-rate dependent (that is the nature of the error); 60fps is nominal.
    static float EulerGain((Vector3 Direction, float SpreadDegrees, float SpeedMin, float SpeedMax,
                               float Gravity, float Damping, float Lambda) m, float life)
    {
        if (m.Lambda <= 0f || m.Damping <= 0f || m.SpeedMax <= 0f) return 1f;
        var curve = DecayCurve(m.Lambda, life);
        float want = m.SpeedMax / m.Lambda;
        float Travelled(float gain)
        {
            float v = m.SpeedMax, travel = 0f, dt = 1f / 60f;
            for (float age = 0f; age < life && v > 0f; age += dt)
            {
                travel += v * dt;
                v = Math.Max(0f, v - m.Damping * gain * curve.Sample(age / life) * dt);
            }
            return travel;
        }
        // Less damping -> further, so the bracket is inverted; 20 halvings is well under a pixel.
        float lo = 0.05f, hi = 1f;
        if (Travelled(lo) < want) return lo;
        for (int i = 0; i < 20; i++)
        {
            float mid = (lo + hi) / 2f;
            if (Travelled(mid) < want) hi = mid; else lo = mid;
        }
        return (lo + hi) / 2f;
    }

    /// The linear control: damping held at its peak for the whole life, i.e. constant
    /// deceleration. Its frame-to-frame displacement ratio must NOT be constant.
    static Curve FlatCurve()
    {
        var c = new Curve { MinValue = 0f, MaxValue = 1f };
        c.AddPoint(new Vector2(0f, 1f));
        c.AddPoint(new Vector2(1f, 1f));
        return c;
    }

    static Curve DecayCurve(float lambda, float life)
    {
        if (lambda <= 0f || life <= 0f) return null;
        var c = new Curve { MinValue = 0f, MaxValue = 1f };
        const int n = 40;
        // ⚠⚠ GIVE THE POINTS THEIR TANGENTS. `AddPoint(position)` leaves both tangents at ZERO,
        // so Godot's cubic Hermite leaves and enters every point FLAT -- a staircase of
        // smoothsteps, not an exponential. The total damping came out close enough to hide it,
        // which is why it survived: the rendered particle matched the intended TRAVEL to 1% while
        // its measured decay constant sat at 6.5/s against the record's 7.72. A curve can be
        // wrong in shape and right in area. The analytic slope is d/ds exp(-lambda*s*life).
        float decay = lambda * life;
        for (int i = 0; i <= n; i++)
        {
            float s = (i / (float)n) * (i / (float)n);      // dense at birth
            float v = Mathf.Exp(-decay * s);
            float slope = -decay * v;
            c.AddPoint(new Vector2(s, v), slope, slope);
        }
        return c;
    }

    /// <summary>⭐⭐ THE EFFECT'S OWN SPRITE, as a strip of its frames.
    ///
    /// `ParticleSprites` holds the executable's two tables -- group -> first image (`0x364058`)
    /// and the 74-name manifest (`FUN_001826d8`) -- so a group and a frame count resolve to real
    /// files in PARTICLE.WAD. ApeSnot is `PA1e0000..PA1e0014`; Bubbles is the single `PA1u0000`.
    ///
    /// ⭐ They are laid side by side into ONE image and the material is told how many frames it
    /// holds, because a `CpuParticles3D` draws every particle with the SAME material: the only way
    /// each particle can be at its own point in the animation is `ParticlesAnimHFrames` plus the
    /// per-particle animation offset Godot keeps. One texture, N frames, each particle its own.
    ///
    /// ⚠ The console steps the frame by REMAINING LIFE (`(N-1) - (N-1) * remaining / initial`, at
    /// `0x189e78`), i.e. once forward over the particle's life and no loop. `ParticlesAnimLoop`
    /// is left off and the speed is 1 so the strip is walked exactly once.
    ///
    /// ⚠ A frame count of 0 means UNTEXTURED -- eleven effects, Sparks among them -- and those
    /// keep the generated dot, which is what they should have had all along.</summary>
    (Texture2D Sheet, int Frames) SheetFor(ParticleEffect e)
    {
        if (_sheets.TryGetValue(e.Id, out var had)) return had;
        var made = Build(e);
        _sheets[e.Id] = made;
        return made;
    }

    (Texture2D, int) Build(ParticleEffect e)
    {
        if (_wad == null || e.Raw.Length < 0x98) return (null, 0);
        int group = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(e.Raw.AsSpan(0x94, 2));
        int frames = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(e.Raw.AsSpan(0x96, 2));
        if (frames <= 0) return (null, 0);
        // ⭐ The console's own arithmetic: frame/2, so N logical frames are (N+1)/2 images.
        var names = new List<string>();
        for (int f = 0; f < frames; f += 2)
            if (ParticleSprites.For(group, f) is { } n && (names.Count == 0 || names[^1] != n))
                names.Add(n);
        if (names.Count == 0) return (null, 0);

        var imgs = new List<Image>();
        foreach (var n in names)
        {
            try
            {
                var raw = _wad.Read(_wad.Wad.Find(ParticleSprites.Path(n)));
                if (raw == null) continue;
                var ssh = new Ssh(raw);
                imgs.Add(Image.CreateFromData(ssh.Width, ssh.Height, false, Image.Format.Rgba8, ssh.Pixels));
            }
            catch (Exception ex) { GD.PrintErr($"[fx] {e.Name}: {n}.ssh would not decode: {ex.Message}"); }
        }
        if (imgs.Count == 0) return (null, 0);

        // ⚠ EVERY FRAME MUST BE THE SAME SIZE or the strip's cells do not line up. They are all
        // one group's art so they should be; a mismatch is blitted into the first frame's box
        // rather than silently shearing the animation, and it says so.
        int w = imgs[0].GetWidth(), h = imgs[0].GetHeight();
        var sheet = Image.CreateEmpty(w * imgs.Count, h, false, Image.Format.Rgba8);
        for (int i = 0; i < imgs.Count; i++)
        {
            if (imgs[i].GetWidth() != w || imgs[i].GetHeight() != h)
            {
                GD.PrintErr($"[fx] {e.Name}: frame {i} is {imgs[i].GetWidth()}x{imgs[i].GetHeight()}, "
                          + $"not {w}x{h} -- rescaled to keep the strip square");
                imgs[i].Resize(w, h);
            }
            sheet.BlitRect(imgs[i], new Rect2I(0, 0, w, h), new Vector2I(i * w, 0));
        }
        GD.Print($"[fx] {e.Name}: group {group}, {frames} frames -> {imgs.Count} images "
               + $"({names[0]}{(names.Count > 1 ? ".." + names[^1] : "")}) {w}x{h}");
        return (ImageTexture.CreateFromImage(sheet), imgs.Count);
    }

    /// <summary>A soft round blob, made here. ⚠ NOT the game's sprite: PARTICLE.WAD's images are
    /// indexed through a table in the executable that has not been read, so this stands in for
    /// them and is one grey dot for every effect.</summary>
    static Texture2D Dot()
    {
        if (_dot != null) return _dot;
        const int n = 32;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp(1f - d, 0f, 1f);
                img.SetPixel(x, y, new Color(1, 1, 1, a * a));
            }
        return _dot = ImageTexture.CreateFromImage(img);
    }

    /// <summary>The effect's own sixteen steps, as a Godot ramp.</summary>
    static Gradient RampOf(ParticleEffect e)
    {
        var g = new Gradient();
        // ⚠⚠ A GRADIENT CANNOT BE EMPTIED, AND THE OLD CODE'S COMMENT DESCRIBED AN INTENT IT DID
        // NOT ACHIEVE. It was `RemovePoint(1); RemovePoint(0);` to clear Godot's two default
        // points before adding the effect's own -- but `remove_point` refuses at
        // `points.size() <= 1`, so the SECOND call always failed and left one default point
        // behind, blended into every burst. It failed loudly in Godot's log
        // ("Condition \"points.size() <= 1\" is true") and silently in the picture, because an
        // ERROR is printed and execution carries on.
        //
        // ⭐ Assigning Offsets and Colors REPLACES the ramp wholesale, so there is nothing to
        // remove. Offsets first: it sizes the point array, and Colors then fills it.
        var offsets = new float[e.Ramp.Length];
        var colors = new Color[e.Ramp.Length];
        for (int i = 0; i < e.Ramp.Length; i++)
        {
            var (r, gr, b, a) = e.ColourAt((i + 0.5f) / e.Ramp.Length);
            offsets[i] = i / (float)(e.Ramp.Length - 1);
            colors[i] = new Color(r / 255f, gr / 255f, b / 255f, a / 255f);
        }
        g.Offsets = offsets;
        g.Colors = colors;
        return g;
    }

    /// <summary>Put one burst of <paramref name="id"/> at <paramref name="where"/>.</summary>
    /// <param name="fireAlong">The fitting's direction, for an `EVENT 2`. ⭐⭐ When it is given
    /// the template's own <see cref="ParticleTemplate.ParticleVelocity"/> is DEAD and the velocity
    /// is `direction * DirectionSpeed` -- read end to end from `0x1bbf28` case 2 -> `0x1b9388`
    /// (position + direction, scaled 1024) -> `0x18b0f8` (`dir * DirectionSpeed >> 10`, the 1024
    /// and the shift cancelling). Null means `EVENT 1`, which does use the template's velocity.
    /// ⚠ Master spotted this from a video -- ApeSnot puffing straight up instead of out of the
    /// ape's nose -- and my own decode notes had already said EVENT 2 replaces the velocity. I had
    /// applied the EVENT 1 path to an EVENT 2 effect.</param>
    /// <param name="probe">⚠⚠ THE MOTION PROBE -- A MEASURING INSTRUMENT, NEVER GAMEPLAY.
    /// 0 = off. 1 = the shipped exponential drag. 2 = a LINEAR CONTROL (flat damping = constant
    /// deceleration). 3 = a RULER: no damping at all, so the particle flies at exactly v0 cells/s
    /// forever and the pixels it covers per second CALIBRATE pixels-per-cell. Without that, a
    /// lambda read off the screen is a shape with no scale -- I had two runs disagreeing about
    /// distance and no way to tell whether the drag was wrong or my pixel ruler was. Both modes strip everything that would blur a reading: ONE particle, no
    /// spread, no speed jitter, NO GRAVITY -- so the only thing shaping the path is the damping
    /// curve under test. Mode 2 exists because a test that cannot fail measures nothing: if the
    /// control also reads as a constant frame-to-frame ratio, the instrument is broken and the
    /// run says nothing about the drag.</param>
    /// ⚠ PROBE ONLY. Multiplies the damping actually set on the emitter, so a sweep can ask the
    /// engine the one question the arithmetic cannot: does the distance a particle covers scale
    /// as 1/D, the way constant deceleration must? If it does, the magnitude is honoured and the
    /// curve is the loss; if it does not, the magnitude itself is not what was asked for.
    public static float ProbeDampingScale = 1f;

    /// <summary>⚠ <paramref name="persistent"/> is the script's ADDOBJ: the console's ADDOBJ adds
    /// an OBJECT the script can later KILLOBJ, where EVENT is a one-shot -- this port's own sound
    /// code says exactly that and the particle path was ignoring it.
    ///
    /// ⭐ It matters for ONE effect, and the census is why I am comfortable changing it: of the 16
    /// particle effects any script ADDOBJs, 15 are already `Immortal` so nothing moves. The
    /// sixteenth is **`Flies` (id 9)**, ADDOBJ'd by `Toilet.rse` and `SupBog.rse` with an emitter
    /// life of 100000 ticks -- about 51 minutes, unmistakably meant to persist -- and under the
    /// old reading it fired one burst and stopped. Flies over a dirty toilet, once.</summary>
    public ParticleEffect Emit(int id, Vector3 where, Vector3? fireAlong = null, int probe = 0,
                               bool persistent = false)
    {
        var e = _library?[id];
        if (e == null || e.Ramp.All(c => c == 0)) return null;
        // ⚠⚠ NOT WHILE THE HOLDER IS OUT OF THE TREE. `Emitting = true` makes Godot ask for the
        // node's global transform, and a node whose ANCESTOR chain is detached answers that with
        // `get_global_transform: !is_inside_tree()` -- an engine error, not an exception, so it
        // scrolls past and the burst silently never happens.
        //
        // ⭐ astraclaw's rendered audit caught it against the construction puff, which fires from
        // ride placement -- and placement runs in scenes that build their park before the holder
        // is attached. The check belongs HERE rather than at that one call site: every caller has
        // the same exposure and only this function knows what it is about to touch.
        if (_root == null || !GodotObject.IsInstanceValid(_root) || !_root.IsInsideTree()) return null;
        // ⚠ ONE CONTINUOUS EMITTER PER PLACE. A looping node is never culled on a deadline, so a
        // script that asks twice would leave two running for ever and double the density.
        if (_continuous.TryGetValue(ContinuousKey(id, where), out var already))
        {
            if (GodotObject.IsInstanceValid(already)) return _library?[id];
            _continuous.Remove(ContinuousKey(id, where));
        }

        // ⭐⭐ THE RECORD IS NOW READ FROM THE CODE THAT RUNS IT (findings/particles.md,
        // ParticleTemplate). Everything this block used to guess was wrong:
        //   +0x74 is the START SIZE, not a lifetime      (+0xa6 is the end size)
        //   +0x78 is the particle's LIFE IN TICKS, not a count; a tick is 31 ms
        //   there is NO count field at all -- emission is a burst plus per-quarter rate bytes,
        //   capped by a max-live, and the whole lot scaled by the retail density 400/1024
        //
        // ⭐ That last part is master's bug. ApeSnot emits about EIGHT particles over 0.31 s;
        // this code was drawing SEVENTY-FIVE at once, which is why bursts read as solid paint
        // however transparent each one was. "piles", exactly.
        var t = ParticleTemplate.Of(e);
        // ⭐⭐ AN IMMORTAL EMITTER LOOPS; IT DOES NOT FIRE ONCE. Master: "the drinks shop produced a
        // SINGLE bubble particle and then never again lol." Bubbles is burst 0, EmitterLife 0,
        // Immortal 1, rate -5 -- and ExpectedTotal sums the rate over Max(EmitterLife, 0) = ZERO
        // ticks, returns 0, and the clamp below turned that into one particle fired one-shot. That
        // is 25 of the library's 105 records: every continuous effect in the game, Smoke, Splash,
        // Steam, WaterFall, TorchSmoke, Spray, SteamJet, MudJet and the drinks shop's bubbles.
        //
        // ⭐ Godot emits `Amount` particles per `Lifetime` seconds while looping, so for a
        // continuous emitter Amount IS the steady-state population -- rate times how long one
        // particle lasts. Bubbles: one every 5 ticks over a 60-tick life = 12 alive at a time.
        bool loops = t.Immortal || persistent;
        // ⭐⭐ A ONE-SHOT FOLLOWS THE NATIVE SCHEDULE, not a total. Master, 2026-10-02: "perform astra's
        // fix on the actual game" -- astraclaw's reading (findings/particle-emission-scheduling.md) of
        // what the console's emitter does tick by tick: the burst, then each tick's rate, gated by the
        // live cap BEFORE the countdown runs, with every particle's own randomised life deciding when a
        // slot opens again. ExpectedTotal summed the rates and ignored the cap: LaserRing drew 5 where
        // the console allows 1 or 2, FirePuff 28 where it births 22, Flames 80 against 14..18.
        // ⚠ Probes keep their single isolated particle, and continuous emitters their steady population.
        List<int> planLives = null; IsolatedParticleSchedule.Trace plan = null;
        List<ParticleBirthRuns.Run> runs = null; bool gated = false;
        if (!loops && probe == 0)
        {
            planLives = new List<int>();
            plan = t.Plan(_rng, planLives);
            gated = t.CapGated(plan);
            runs = ParticleBirthRuns.For(t, plan);
        }
        int count = loops ? t.SteadyPopulation() : plan != null ? plan.BirthTicks.Count : Math.Clamp(t.ExpectedTotal(), 1, 200);
        float life = Math.Clamp(t.Life * ParticleTemplate.TickMilliseconds / 1000f, 0.05f, 8f);
        // Drawn width is size/5120 CELLS and one cell is one unit here; start and end differ, so
        // the scale range is the record's own taper rather than an invented 0.5..1 spread.
        float size0 = t.StartSize / 5120f, size1 = t.EndSize / 5120f;
        float size = Math.Max(size0, size1);
        // The emitter's own life decides how long the rate bytes keep firing; a one-shot burst
        // finishing inside that is the common case. ⚠ 37 effects are IMMORTAL and never stop.
        float emitterSeconds = t.Immortal ? life
            : Math.Clamp(t.EmitterLife * ParticleTemplate.TickMilliseconds / 1000f, 0f, 8f);

        var motion = Motion(t, fireAlong);
        // ⚠ Printed so the numbers can be checked against the record rather than judged by eye:
        // a puff that looks plausible and a puff that is right are different claims.
        GD.Print($"[fx] {e.Name}: {(loops ? $"CONTINUOUS {count} alive "
                     + $"({t.SteadyRatePerSecond():F1}/s x {life:F2}s life, cap {t.MaxLive})"
                     : plan != null
                        ? $"one-shot {count} by the native plan, births at ticks [{string.Join(",", plan.BirthTicks)}] "
                          + $"in {runs.Count} run(s){(gated ? ", cap-gated" : "")} (old estimate {t.ExpectedTotal()})"
                        : $"one-shot {count}")} | "
               + $"dir {motion.Direction.Snapped(Vector3.One * 0.01f)} "
               + $"spread {motion.SpreadDegrees:F0}deg speed {motion.SpeedMin:F2}..{motion.SpeedMax:F2} "
               + $"cells/s gravity {motion.Gravity:F2} cells/s2 "
               + $"(raw v={t.ParticleVelocity} jitter={t.VelocityJitter} radial={t.RadialSpeed} "
               + $"g={t.Gravity} drag={t.Drag} dirspeed={t.DirectionSpeed} "
               + $"along={(fireAlong is { } fa ? fa.Snapped(Vector3.One * 0.01f).ToString() : "(EVENT 1)")} "
               + $"-> lambda {motion.Lambda:F2}/s, damping peak {motion.Damping:F1} cells/s2 "
               + $"x{EulerGain(motion, life):F3} euler gain, "
               + $"half-speed at {(motion.Lambda > 0 ? (0.693f / motion.Lambda).ToString("F3") : "-")}s)");
        // ⭐⭐ MEASURE THE CURVE, DON'T INFER IT. Three fixes in a row each moved the rendered
        // decay the right way and none of them closed the gap, which per the usual lesson means
        // the DIAGNOSIS is wrong, not the size of the correction. So step Godot's own damping
        // loop here, over the very Curve object that is about to be handed to the emitter, and
        // print what it predicts. If this says 0.77 cells and the render says 0.67, the loss is
        // inside the particle system; if this says 0.67 too, it is the curve and it is visible
        // right here. `Sample` vs `SampleBaked` are both run because Curve bakes to 100 UNIFORM
        // samples and this curve puts most of its shape in the first 5% of its domain.
        if (probe > 0)
        {
            var dbg = probe == 3 ? null : probe == 2 ? FlatCurve() : DecayCurve(motion.Lambda, life);
            foreach (var baked in new[] { false, true })
            {
                float v = motion.SpeedMax, travel = 0f, dt = 1f / 62f, half = -1f;
                for (float age = 0f; age < life && v > 0f; age += dt)
                {
                    float c = dbg == null ? 1f
                        : baked ? dbg.SampleBaked(age / life) : dbg.Sample(age / life);
                    travel += v * dt;
                    v = Math.Max(0f, v - motion.Damping * EulerGain(motion, life) * c * dt);
                    if (half < 0f && v <= motion.SpeedMax / 2f) half = age;
                }
                GD.Print($"[probe] curve stepped ({(baked ? "SampleBaked" : "Sample")}): "
                       + $"travel {travel:F4} cells, half-speed at {half:F4}s "
                       + $"(record wants {motion.SpeedMax / Math.Max(motion.Lambda, 1e-6f):F4} cells, "
                       + $"{0.693f / Math.Max(motion.Lambda, 1e-6f):F4}s)");
            }
        }
        if (probe > 0)
            GD.Print($"[probe] mode {probe} ({(probe == 2 ? "LINEAR CONTROL" : "exponential")}): "
                   + $"v0 {motion.SpeedMax:F3} cells/s, life {life:F3}s, lambda {motion.Lambda:F3}/s. "
                   + $"Predicted per-frame displacement ratio at 60fps: "
                   + $"{(probe == 3 ? "1.0000 (RULER: undamped, constant speed)" : probe == 2 ? "FALLING (not constant)" : Mathf.Exp(-motion.Lambda / 60f).ToString("F4"))}");
        var p = new CpuParticles3D
        {
            Name = $"Fx_{e.Id}_{e.Name}",
            // ⚠ At least 1: Godot logs an ERROR for 0, and a plan that births nothing builds this node only to
            // drop it (see `drawn` below).
            Amount = probe > 0 ? 1 : Math.Max(count, 1),
            // ⚠ A looping emitter must NOT be one-shot, and its Explosiveness must be 0 or Godot
            // dumps the whole population at the start of every cycle instead of trickling it.
            OneShot = !loops,
            // ⭐ Explosiveness is now DERIVED: the record says how much is a burst at spawn and
            // how much trickles out over the emitter's life, so the fraction born at once is the
            // burst's share of the total rather than a number I picked.
            Lifetime = probe >= 3 ? Math.Max(life, 4f) : life,
            Explosiveness = loops ? 0f : probe > 0 ? 1f
                : t.Burst > 0 && count > 0
                ? Mathf.Clamp(ParticleTemplate.DensityScaled(t.Burst, ParticleTemplate.RetailDensity) / (float)count, 0f, 1f)
                : (emitterSeconds <= 0f ? 1f : 0.1f),
            Emitting = false,
            ColorRamp = RampOf(e),
            // ⭐⭐ PARTICLES ARE BORN OVER AN AREA, NOT AT A POINT. Master, 2026-09-27: "the
            // bubbles are meant to emit from multiple random points, but they all come up as one
            // stream". They did: this port DECODED the emission extent and then never used it, so
            // every particle was born at the emitter's exact position.
            //
            // ⭐ `0x1888a8` births at the emitter's position plus, when any extent is non-zero,
            // either a BOX `(rand % X, rand % Y, rand % Z)` when `+0xc2`, or otherwise a DISC in XZ
            // at a random 12-bit angle with radius `X` -- on the EDGE when `+0xa8`, anywhere inside
            // it otherwise -- with Y taken from the box and one-sided when `+0xa0`.
            //
            // ⚠ Godot's box extents are HALF-extents, which is what the console's signed
            // `rand % X` gives (±X); only Y is one-sided, and `+0xa0` says when.
            EmissionShape = EmissionOf(t, out var boxExtents, out var ringRadius,
                                       out var ringInner, out var ringHeight),
            EmissionBoxExtents = boxExtents,
            EmissionRingRadius = ringRadius,
            EmissionRingInnerRadius = ringInner,
            EmissionRingHeight = ringHeight,
            EmissionRingAxis = Vector3.Up,
            // ⭐⭐ THE MOTION IS THE RECORD'S NOW, not mine. Every one of these used to be a
            // number I picked -- straight up, a 35-degree cone, a speed derived from the SIZE of
            // all things, and a gravity of 1.5. See `Motion` for the conversion out of the
            // console's units.
            Direction = motion.Direction,
            Spread = probe > 0 ? 0f : motion.SpreadDegrees,
            InitialVelocityMin = probe == 4 ? 0f : probe > 0 ? motion.SpeedMax : motion.SpeedMin,
            InitialVelocityMax = probe == 4 ? 0f : motion.SpeedMax,
            Gravity = probe > 0 ? Vector3.Zero : new Vector3(0, -motion.Gravity, 0),
            DampingMin = probe >= 3 ? 0f
                : motion.Damping * EulerGain(motion, life) * (probe > 0 ? ProbeDampingScale : 1f),
            DampingMax = probe >= 3 ? 0f
                : motion.Damping * EulerGain(motion, life) * (probe > 0 ? ProbeDampingScale : 1f),
            // ⭐ Mode 2 is the control. Godot damps `v -= damping(life fraction) * delta`, so a
            // FLAT curve is constant deceleration -- the linear easing -- and the curved one is
            // D*e^(-lambda*t) against v0*e^(-lambda*t), which is the console's `v *= k` per tick.
            DampingCurve = probe >= 3 ? null : probe == 2 ? FlatCurve()
                : DecayCurve(motion.Lambda, life),
            ScaleAmountMin = Math.Max(0.01f, Math.Min(size0, size1)),
            ScaleAmountMax = Math.Max(0.02f, Math.Max(size0, size1)),
            // ⚠⚠ A CpuParticles3D DRAWS A MESH, NOT A TEXTURE. It has no Texture property at all,
            // and with Mesh left null it emits perfectly happily and renders NOTHING -- the
            // counter goes up, the log looks right, the screen stays empty. Found by emitting a
            // control burst at the camera's focus, owing nothing to the node table or the script:
            // that did not appear either, which said the emitter was wrong rather than the place.
            Mesh = new QuadMesh { Size = Vector2.One },
            // ⚠ And it is culled by an AABB Godot cannot infer for a one-shot, so the burst
            // vanishes the moment its ORIGIN leaves the frame.
            VisibilityAabb = new Aabb(new Vector3(-8, -8, -8), new Vector3(16, 16, 16)),
        };
        // ⭐⭐ THE REAL SPRITE. Resolved before the material because the material has to be told
        // how many frames the strip holds. ⚠ Falls back to the generated dot, which is now only
        // what the eleven UNTEXTURED effects get -- and what they should always have got.
        var sheet = SheetFor(e);
        // ⭐⭐ THE GS'S MODULATE, NOT STANDARDMATERIAL3D'S: see Ps2Materials.ParticleShader. Unshaded, mixed,
        // billboarded per particle with its scale kept, and the strip walked once -- as before; only the
        // colour arithmetic changed.
        // ⭐⭐ NOTHING IS ADDITIVE. Master, who can see the real game, settled it outright: "yeah they
        // arent additive." `+0x70` bit 2 became draw flag `0x20` and there the trail stopped; see
        // ParticleTemplate.AdditiveBit, where the surviving candidate is unlit/full-bright.
        var mat = new ShaderMaterial { Shader = Ps2Materials.ParticleShader };
        mat.SetShaderParameter("albedo_tex", sheet.Sheet ?? Dot());
        mat.SetShaderParameter("frames_h", Mathf.Max(1, sheet.Frames));
        mat.SetShaderParameter("textured", sheet.Sheet != null);
        ((QuadMesh)p.Mesh).Material = mat;
        // ⭐ ONE PASS THROUGH THE STRIP PER PARTICLE. Speed 1 with AnimLoop off walks the frames
        // exactly once over a particle's life, which is what `0x189e78` does by indexing on
        // remaining life. ⚠ Offset stays 0: every particle starts at frame 0, because the console
        // starts each one at its own birth rather than at a random point in the animation.
        // ⚠ THE PROBE FREEZES THE STRIP. The puff walks 8 drawings over its life and they are not
        // all centred the same, so a tracked centroid wobbles ~2px -- comparable to the whole
        // early-frame displacement being measured. Holding one drawing removes the wobble from the
        // instrument without touching the motion. (Gameplay keeps the animation, obviously.)
        p.AnimSpeedMin = p.AnimSpeedMax = probe > 0 ? 0f : sheet.Frames > 1 ? 1f : 0f;
        p.AnimOffsetMin = p.AnimOffsetMax = 0f;
        // ⚠⚠ POSITION BEFORE AddChild. A one-shot emits at the transform it had when it entered
        // the tree, so setting it afterwards puts the whole burst at the origin.
        // ⭐⭐ THE RADIAL OFFSET, AND THE ANGLE IT SWEEPS (see _orbit). A record with no offset is
        // unaffected: `Orbit` returns zero and nothing is registered, which is 102 of the 105.
        float orbitRadius = t.RadialOffset / (float)ParticleTemplate.PositionUnitsPerCell;
        float orbitAngle = t.OffsetAngle / (float)ParticleTemplate.AngleUnitsPerTurn * Mathf.Tau;
        float orbitRate = t.OffsetAngularVelocity / (float)ParticleTemplate.AngleUnitsPerTurn
                        * Mathf.Tau / (ParticleTemplate.TickMilliseconds / 1000f);
        if (orbitRadius != 0f)
            GD.Print($"[fx] {e.Name}: radial offset {t.RadialOffset} ({orbitRadius:F3} cells) at "
                   + $"angle {t.OffsetAngle}/4096, sweeping {t.OffsetAngularVelocity}/tick = "
                   + $"{Mathf.RadToDeg(orbitRate):F0} deg/s"
                   + (orbitRate != 0f
                        ? $" -- a turn every {Mathf.Tau / Mathf.Abs(orbitRate):F2}s, "
                          + $"{Mathf.Abs(orbitRate) * emitterSeconds / Mathf.Tau:F1} over the emission"
                        : " -- STATIC (no sweep)"));
        // ⚠⚠ POSITION BEFORE AddChild, as below -- and the offset has to be IN that position, not
        // applied on the first Step, or the burst's first particles come out of the centre.
        p.Position = where + Orbit(orbitRadius, orbitAngle);
        // ⚠ The already-born must NOT follow the emitter round; see _orbit. Godot's default is
        // already false, stated because the whole effect depends on it.
        p.LocalCoords = false;
        float deadline = life;
        // ⚠ A plan can birth NOTHING (a one-tick emitter whose last-quarter rate is 0). Nothing is drawn
        // then, but the emit still happened: the child request below is made at spawn on the console
        // (`0x18b5a8` reads `+0xb4` after the burst, whatever was born), so it must not be skipped.
        bool drawn = runs == null || runs.Count > 0;
        if (!drawn)
        {
            GD.Print($"[fx] {e.Name}: the native plan births nothing -- not drawn");
            p.QueueFree();
        }
        else
        {
            if (runs != null)
                deadline = DrawRuns(p, runs, planLives, life,
                    runLife => (motion.Damping * EulerGain(motion, runLife), DecayCurve(motion.Lambda, runLife)));
            _root.AddChild(p);
            p.Emitting = true;
            StartPendingRuns();
            if (orbitRadius != 0f && orbitRate != 0f)
                _orbit.Add((p, where, orbitRadius, orbitRate, orbitAngle));
        }
        // ⚠ The cull deadline is WALL time while the particle ages on SCALED time, so slow motion
        // would free the probe mid-flight -- the one thing that would make the fix invisible.
        // ⚠⚠ AND THE CULL MUST NOT KILL IT. The deadline is one particle-lifetime, which is right
        // for a burst and fatal for an emitter that is supposed to run for ever -- it would put the
        // bubbles back to a few seconds and then silence. So a continuous emitter is kept until its
        // holder goes or the script stops its object (<see cref="Stop"/>, `KILLOBJ tag`), and
        // `Emit` refuses to start a second one in the same place rather than stacking them.
        if (drawn && loops) _continuous[ContinuousKey(id, where)] = p;
        else if (drawn) _live.Add((p, Time.GetTicksMsec()
                    + (ulong)(deadline * 1000 / Math.Max(0.01, Engine.TimeScale)) + 500));
        Spawned++;
        // Native spawn calls the non-directional spawn API for a particle child, even when the
        // parent was directional. Use the child's OWN offset words, not the parent's velocity.
        // Probe modes intentionally isolate one effect for motion measurement; gameplay uses 0.
        // Roll out ONLY the audited Twinkle83 request. Other native links remain deliberately
        // deferred: enabling LaserRing63 also exposes an unvalidated total/live-cap scheduler.
        // This is not full ChildEffect support or the Twinkle83->84 particle-death chain.
        if (probe == 0 && ParticleSpawnLinks.TryTwinkleChild(_library, id, out var child))
        {
            var offset = new Vector3(child.X, child.Y, -child.Z) / ParticleTemplate.PositionUnitsPerCell;
            var made = Emit(child.EffectId, where + offset);
            GD.Print($"[fx] child {e.Id}/{e.Name} -> {child.EffectId}/{made?.Name ?? "(not drawn)"} "
                   + $"initial offset ({offset.X:F4},{offset.Y:F4},{offset.Z:F4}); immediate spawn only");
        }
        return e;
    }

    /// <summary>⭐ The plan's runs (<see cref="ParticleBirthRuns"/>) onto CPU emitters. <paramref name="first"/> takes
    /// the first run and starts now; each later run is a copy parented UNDER it, so the effect stays one
    /// node to orbit, cull and count, started by a timer at its own tick. ⚠ Delays count from the plan's
    /// FIRST birth, not from the spawn: an effect with no burst births on tick 1, and drawing that 31 ms
    /// later than the request is not worth a frame of nothing. A run of one particle takes that particle's
    /// own planned life, so a capped emitter's replacement appears as its predecessor goes; a longer run
    /// keeps the record's base life. Returns the seconds until the last particle has died.</summary>
    float DrawRuns(CpuParticles3D first, List<ParticleBirthRuns.Run> runs, List<int> lives, float life,
                   Func<float, (float Damping, Curve Curve)> dampingFor)
    {
        float tick = ParticleTemplate.TickMilliseconds / 1000f;
        float end = 0f;
        // ⚠⚠ EVERY COPY BEFORE ANY CHILD. `Duplicate` copies a node's children too, so copying `first`
        // after earlier runs had been parented under it doubled the tree per run: 16 runs for Flames
        // were 2^15 nodes and froze the game. Caught by ParticleScheduleAudit hanging inside Emit(55).
        var nodes = new List<CpuParticles3D> { first };
        for (int k = 1; k < runs.Count; k++) nodes.Add((CpuParticles3D)first.Duplicate());
        for (int k = 0; k < runs.Count; k++)
        {
            var run = runs[k];
            var node = nodes[k];
            float runLife = run.Count == 1 ? Math.Clamp(lives[run.FirstBirth] * tick, 0.05f, 8f) : life;
            node.Amount = run.Count;
            node.Lifetime = runLife;
            // Godot births Amount evenly over Lifetime x (1 - explosiveness): a run of n ticks g apart.
            node.Explosiveness = run.Ticks <= 1 ? 1f : Mathf.Clamp(1f - run.Ticks * run.Gap * tick / runLife, 0f, 1f);
            if (runLife != life)
            {
                var (damping, curve) = dampingFor(runLife);
                node.DampingMin = node.DampingMax = damping;
                node.DampingCurve = curve;
            }
            float delay = (run.FirstTick - runs[0].FirstTick) * tick;
            end = Math.Max(end, delay + run.Ticks * run.Gap * tick + runLife);
            if (k == 0) continue;
            node.Name = $"{first.Name}_run{k}";
            node.Position = Vector3.Zero;
            node.Emitting = false;
            first.AddChild(node);
            _pendingRuns.Add((node, delay));
        }
        return end;
    }

    /// <summary>Later runs waiting for their tick: started by <see cref="StartPendingRuns"/> once the first
    /// node is in the tree, since a timer needs a tree.</summary>
    readonly List<(CpuParticles3D Node, float Delay)> _pendingRuns = new();
    void StartPendingRuns()
    {
        foreach (var (node, delay) in _pendingRuns)
        {
            if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree()) continue;
            // ⚠ Scaled time, the clock the particles age on; the node may be gone by the time it fires.
            var timer = node.GetTree().CreateTimer(delay);
            timer.Timeout += () => { if (GodotObject.IsInstanceValid(node) && node.IsInsideTree()) node.Emitting = true; };
        }
        _pendingRuns.Clear();
    }

    /// <summary>⚠ INFERRED STOP PATH for a continuous emitter: the script's `KILLOBJ tag` on the object its
    /// `ADDOBJ` started. A breakdown's smoke is exactly that -- snake, slide and bigapple raise
    /// `ADDOBJ(2, node, 16 Smoke2, tag 1)` when VAR_BREAKSTAT goes to 1 and their fixed branch says
    /// `KILLOBJ(1)` (core probe over the scripts, 2026-09-27) -- so without this a repaired ride smoked
    /// for ever. That KILLOBJ reaches particle objects as it does sounds is the tag semantics' reading
    /// (the console's object list at instance +0xB0 is not walked). Emission stops, the particles
    /// alive finish their life, then the node is freed. Returns whether one was running there.</summary>
    public bool Stop(int id, Vector3 where)
    {
        var key = ContinuousKey(id, where);
        if (!_continuous.Remove(key, out var node) || !GodotObject.IsInstanceValid(node)) return false;
        node.Emitting = false;
        _live.Add((node, Time.GetTicksMsec() + (ulong)(node.Lifetime * 1000 / Math.Max(0.01, Engine.TimeScale)) + 500));
        Stopped++;
        return true;
    }
    public int Stopped { get; private set; }

    /// <summary>Drop the bursts that have finished. ⚠ A freed node answers as if it were alive
    /// right until it throws, so validity is checked and not assumed.</summary>
    /// <summary>Continuous emitters, keyed by effect and by the cell they stand in -- rounded,
    /// because the same request arriving twice will not carry bit-identical floats.</summary>
    readonly Dictionary<(int, int, int, int), CpuParticles3D> _continuous = new();
    static (int, int, int, int) ContinuousKey(int id, Vector3 at) =>
        (id, Mathf.RoundToInt(at.X * 4), Mathf.RoundToInt(at.Y * 4), Mathf.RoundToInt(at.Z * 4));

    /// <param name="delta">⚠ The SCALED frame time, the clock the particles themselves age on --
    /// the orbit has to slow down with them under slow motion or the helix shears. Required rather
    /// than defaulted so a new caller cannot silently freeze the sweep.</param>
    public void Step(double delta)
    {
        // ⭐ Sweep the orbiting emitters before the cull, so a node freed this frame is dropped
        // from both lists in the same pass. See _orbit for what this is and why it moves the node.
        for (int i = _orbit.Count - 1; i >= 0; i--)
        {
            var o = _orbit[i];
            if (o.Node == null || !GodotObject.IsInstanceValid(o.Node) || !o.Node.IsInsideTree())
            { _orbit.RemoveAt(i); continue; }
            float angle = o.Angle + o.RadPerSec * (float)delta;
            o.Node.Position = o.Centre + Orbit(o.Radius, angle);
            _orbit[i] = (o.Node, o.Centre, o.Radius, o.RadPerSec, angle);
        }

        ulong now = Time.GetTicksMsec();
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var (node, until) = _live[i];
            if (node == null || !GodotObject.IsInstanceValid(node)) { _live.RemoveAt(i); continue; }
            if (now < until) continue;
            node.QueueFree();
            _live.RemoveAt(i);
        }
    }

    /// <summary>⭐⭐ DROP EVERY LIVE EMITTER -- BOTH KINDS. Called when the park is torn down for
    /// another map (Viewer's reset, beside `_sounds?.Clear()`).
    ///
    /// ⚠⚠ THIS USED TO CLEAR ONLY `_live` AND LEAVE `_continuous` STANDING, which is master's
    /// "particle emitters not deleting when switching maps". A looping emitter is never culled on a
    /// deadline -- that is the whole point of the `_continuous` map -- so nothing else would ever
    /// have taken it down.
    ///
    /// ⚠ And it had a SECOND face that is easy to miss: the stale KEYS matter as much as the nodes.
    /// `Emit` returns early when `_continuous` already holds a valid node for an (effect, cell)
    /// pair, so a survivor from the old park could ALSO suppress the same effect on the new one --
    /// old particles that will not die, and new particles that never start, from one missing line.
    ///
    /// ⚠ Freed outright rather than handed to `_live` the way <see cref="Stop"/> does: Stop fades a
    /// ride's effect gracefully while the park keeps running, but here the park itself is going
    /// away and there is nothing left for them to fade in front of.</summary>
    public void Clear()
    {
        foreach (var (node, _) in _live) if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        _live.Clear();
        foreach (var node in _continuous.Values) if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        _continuous.Clear();
        _orbit.Clear();
    }
}
