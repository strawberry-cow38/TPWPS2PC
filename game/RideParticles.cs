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
    public ParticleEffect Emit(int id, Vector3 where)
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

        var p = new CpuParticles3D
        {
            Amount = count,
            Lifetime = life,
            OneShot = true,
            // ⭐ Explosiveness is now DERIVED: the record says how much is a burst at spawn and
            // how much trickles out over the emitter's life, so the fraction born at once is the
            // burst's share of the total rather than a number I picked.
            Explosiveness = t.Burst > 0 && count > 0
                ? Mathf.Clamp(ParticleTemplate.DensityScaled(t.Burst, ParticleTemplate.RetailDensity) / (float)count, 0f, 1f)
                : (emitterSeconds <= 0f ? 1f : 0.1f),
            Emitting = false,
            ColorRamp = RampOf(e),
            Direction = Vector3.Up,
            Spread = 35f,
            InitialVelocityMin = size * 1.5f,
            InitialVelocityMax = size * 3.5f,
            Gravity = new Vector3(0, -1.5f, 0),
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
        p.AnimSpeedMin = p.AnimSpeedMax = sheet.Frames > 1 ? 1f : 0f;
        p.AnimOffsetMin = p.AnimOffsetMax = 0f;
        // ⚠⚠ POSITION BEFORE AddChild. A one-shot emits at the transform it had when it entered
        // the tree, so setting it afterwards puts the whole burst at the origin.
        p.Position = where;
        _root.AddChild(p);
        p.Emitting = true;
        _live.Add((p, Time.GetTicksMsec() + (ulong)(life * 1000) + 500));
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
