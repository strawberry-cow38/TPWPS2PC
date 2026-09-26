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
    /// <summary>PARTICLE.WAD, open, so the sprites can be pulled out of it. ⚠ Null is allowed and
    /// means the placeholder: a park with no art must still run.</summary>
    readonly AssetLibrary _wad;
    readonly Dictionary<int, (Texture2D Sheet, int Frames)> _sheets = new();
    readonly List<(CpuParticles3D Node, ulong Until)> _live = new();
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
    public ParticleEffect Emit(int id, Vector3 where, Vector3? fireAlong = null, int probe = 0)
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
        int count = Math.Clamp(t.ExpectedTotal(), 1, 200);
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
        GD.Print($"[fx] {e.Name}: dir {motion.Direction.Snapped(Vector3.One * 0.01f)} "
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
            Amount = probe > 0 ? 1 : count,
            OneShot = true,
            // ⭐ Explosiveness is now DERIVED: the record says how much is a burst at spawn and
            // how much trickles out over the emitter's life, so the fraction born at once is the
            // burst's share of the total rather than a number I picked.
            Lifetime = probe >= 3 ? Math.Max(life, 4f) : life,
            Explosiveness = probe > 0 ? 1f
                : t.Burst > 0 && count > 0
                ? Mathf.Clamp(ParticleTemplate.DensityScaled(t.Burst, ParticleTemplate.RetailDensity) / (float)count, 0f, 1f)
                : (emitterSeconds <= 0f ? 1f : 0.1f),
            Emitting = false,
            ColorRamp = RampOf(e),
            // ⭐⭐ THE MOTION IS THE RECORD'S NOW, not mine. Every one of these used to be a
            // number I picked -- straight up, a 35-degree cone, a speed derived from the SIZE of
            // all things, and a gravity of 1.5. See `Motion` for the conversion out of the
            // console's units.
            Direction = motion.Direction,
            Spread = probe > 0 ? 0f : motion.SpreadDegrees,
            InitialVelocityMin = probe == 4 ? 0f : probe > 0 ? motion.SpeedMax : motion.SpeedMin,
            InitialVelocityMax = probe == 4 ? 0f : motion.SpeedMax,
            Gravity = probe > 0 ? Vector3.Zero : new Vector3(0, -motion.Gravity, 0),
            DampingMin = probe >= 3 ? 0f : motion.Damping * EulerGain(motion, life),
            DampingMax = probe >= 3 ? 0f : motion.Damping * EulerGain(motion, life),
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
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            // ⭐⭐ NOTHING IS ADDITIVE. Master, who can see the real game, settled it outright:
            // "yeah they arent additive."
            //
            // ⚠⚠ THE BIT WAS NEVER EVIDENCE FOR IT. `+0x70` bit 2 became draw flag `0x20` and
            // there the trail stopped -- what `0x20` meant at the GS was never read, and the port
            // twice picked a POLARITY for it off the effect NAMES, in both directions. The full
            // 105-effect census then showed the names do not support either: three spark effects
            // sit on the clear side and Button, Repair and BuyLand sit on the set side. So this
            // was a coin landing on its edge, and master's answer is the first actual evidence
            // anyone has had about it.
            //
            // ⭐ The bit still means SOMETHING -- see ParticleTemplate.AdditiveBit, where the
            // surviving candidate is unlit/full-bright. It just does not mean this, and until it
            // is read it drives nothing.
            BlendMode = BaseMaterial3D.BlendModeEnum.Mix,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            // ⭐ The frames of THIS effect's own sprite, walked once over each particle's life --
            // the console steps them by remaining life at 0x189e78, so no loop.
            ParticlesAnimHFrames = Mathf.Max(1, sheet.Frames),
            ParticlesAnimVFrames = 1,
            ParticlesAnimLoop = false,
            // ⚠ BillboardMode.Particles drops ScaleAmount unless this is set.
            BillboardKeepScale = true,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = sheet.Sheet ?? Dot(),
            DisableReceiveShadows = true,
        };
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
        p.Position = where;
        _root.AddChild(p);
        p.Emitting = true;
        // ⚠ The cull deadline is WALL time while the particle ages on SCALED time, so slow motion
        // would free the probe mid-flight -- the one thing that would make the fix invisible.
        _live.Add((p, Time.GetTicksMsec()
                    + (ulong)(life * 1000 / Math.Max(0.01, Engine.TimeScale)) + 500));
        Spawned++;
        return e;
    }

    /// <summary>Drop the bursts that have finished. ⚠ A freed node answers as if it were alive
    /// right until it throws, so validity is checked and not assumed.</summary>
    public void Step()
    {
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

    public void Clear()
    {
        foreach (var (node, _) in _live) if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        _live.Clear();
    }
}
