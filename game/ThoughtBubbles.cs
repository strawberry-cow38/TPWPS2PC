using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>The thought bubble over a visitor's head.
///
/// ⭐⭐ THE ART AND ITS IDS ARE THE ENGINE'S. `FUN_00216028` names sixteen textures
/// `bubbles\tb*.ssh` and gives each an id; <see cref="Thought"/> is that table. Five of the ids
/// are confirmed a second time by the guest decision at `0x20C930` writing them into the guest's
/// `+0x40`. Nothing here chooses which picture means what.
///
/// ⚠ The six extra bubbles on the disc (`tbconfused`, `tbsconfused`, `tbshappy`, `tbssad`,
/// `tbsstrike`, `tbstired`) are NOT loaded: the executable never names them, so nothing is known
/// about when they would show.
///
/// ⚠ SIZE AND HEIGHT ARE CHOSEN. The console's own bubble geometry has not been read; these are
/// set to sit above a walking guest and read at the game camera's distance.</summary>
public sealed class ThoughtBubbles
{
    public Node3D Root { get; } = new() { Name = "Thoughts" };

    /// <summary>⚠ Chosen -- see the class note -- but no longer chosen BLIND. Measured off two of
    /// astraclaw's real captures of a served customer, before and after:
    ///
    ///   0.0042 -> cloud **16 x 13 px**   illegible: the glyph inside is a smudge
    ///   0.0088 -> cloud **34 x 31 px**   legible: reads as the two-figure WC sign
    ///
    /// 2.12x observed against 2.10x applied, which is the agreement that says the knob and the
    /// picture are connected. Height rises with it because the sprite is CENTRED on its position:
    /// growing it drops the lower edge by half the gain, and at 0.62 the kid's hair already
    /// overlapped the bottom of the cloud.
    ///
    /// ⚠⚠ THE FRAMING IS A CLOSE-UP, NOT GAMEPLAY, so these numbers size the bubble for the
    /// pose the feature is *inspected* at, not for play. What play needs is still open.
    ///
    /// ⚠⚠ AND "SUB-PIXEL AT PARK VIEW" WAS WRONG BY A FACTOR OF 256 -- twice, in this comment and
    /// in the class note above it. `GameCamera` keeps its pose in RAW GAME UNITS and `Build`
    /// divides by `TileUnits` (256) on the way out, so the reset pose `EyeY = 0xA10` is **10.06
    /// Godot units** up, not 2576, with `Behind = 0x6E0` = 6.875 -- about **12.2 units** of slant
    /// range to a guest. At that distance a bubble is small but nowhere near sub-pixel, and
    /// "however big you make it, it vanishes" simply does not follow. astraclaw caught it.
    ///
    /// ⭐ I had taken a raw-unit constant out of a comment and used it as a Godot distance. The
    /// tell was available and ignored: 2576 units of altitude over a park whose cells are ONE
    /// unit across would put the camera two and a half thousand tiles up.
    ///
    /// ⭐⭐ AND THE DEFAULT-POSE MEASUREMENT SAYS LEAVE IT WORLD-SCALED. astraclaw captured the
    /// reset pose (raw 2576 up, 1760 behind, dolly 0, fov 75) and measured the full texture quad
    /// at **11.46 px**; the opaque cloud in that same frame is **10 x 8 px**, so the art fills
    /// 0.873 of the quad. We had agreed a provisional 48-64 px readable range and a screen-stable
    /// sprite to hit it -- and the capture kills that plan outright:
    ///
    /// ⚠⚠ AT THE PARK VIEW THE WHOLE OUTHOUSE IS ABOUT **32 x 45 px**. A 52 px cloud would be
    /// WIDER THAN THE BUILDING IT STANDS ON, and would cover the ride it is describing. There is
    /// no size that is readable at park zoom and not absurd, so "readable at every zoom" is not a
    /// target anyone can hit -- it had to be measured to be seen, because 48-64 px sounds modest
    /// right up until you learn what else is 45 px.
    ///
    /// ⭐ Which makes the plain world-scaled sprite RIGHT, not merely untuned: 34 px up close
    /// where the player is looking at one guest, 10 px at park view where it is an INDICATOR that
    /// somebody wants something -- and at that zoom the customer is hidden behind the hut
    /// entirely, so a marker is the only thing a bubble could usefully be. A screen-stable sprite
    /// was written for this and then deleted rather than left switched off: an unused knob reads
    /// like a decision.
    ///
    /// ⚠ STILL NOT READ: what the console does. `FUN_00216028` registers the sixteen textures and
    /// their ids and says nothing about size, so "indicator far, readable near" is a port choice
    /// that happens to be forced by the geometry -- not a decoded rule.
    ///
    /// ⚠ And the first pass of this measurement reported 23 px, not 16: the brightness threshold
    /// was catching the kid's blond hair as well as the cloud. Re-measured with a blue-biased
    /// white that hair cannot satisfy. An instrument that flatters the before-picture understates
    /// the very problem it is there to find.</summary>
    public float Height { get; set; } = 0.80f;
    /// ⭐ Master asked for bigger after seeing one in play: 0.0088 measured 34 px of cloud in the
    /// inspection close-up, so 0.0140 puts it near 54 -- a little over half again. ⚠ A STEP, not
    /// a settled value: they said "bigger" without a number, so this is a legible increment they
    /// can push further rather than my guess at where they want to stop.
    public float Size { get; set; } = 0.0140f;

    readonly Dictionary<Thought, ImageTexture> _art = new();
    readonly Dictionary<int, Sprite3D> _live = new();

    bool _said;
    public string Report { get; private set; } = "not loaded";
    public bool Ready => _art.Count > 0;

    /// <summary>The file each thought is drawn from, straight off the table the executable builds.
    /// ⚠ Normal is deliberately absent from this map's USE, not from the game: `tbnormal.ssh`
    /// exists and is id 2, but a bubble over every idle guest in the park is not what the console
    /// looks like. <see cref="Show"/> skips it; the art is still loaded so that is a display
    /// choice in one place rather than a missing asset.</summary>
    static readonly Dictionary<Thought, string> Art = new()
    {
        [Thought.Happy] = "tbhappy", [Thought.VeryUnhappy] = "tbsuhappy",
        [Thought.Normal] = "tbnormal", [Thought.Sad] = "tbsad",
        [Thought.Bored] = "tbbored", [Thought.Sick] = "tbsick",
        [Thought.Scared] = "tbscared", [Thought.Toilet] = "tbtoilet",
        [Thought.Thirsty] = "tbthirsty", [Thought.BadQueue] = "tbqueuebad",
        [Thought.Angry] = "tbangry", [Thought.Hungry] = "tbhungry",
        [Thought.Good] = "tbgood", [Thought.Bad] = "tbbad",
        [Thought.HungryAndThirsty] = "tbhungthir", [Thought.Litter] = "tblitter",
    };

    /// <summary><paramref name="read"/> fetches a shared asset out of DATA.WAD.</summary>
    public string Load(Func<string, byte[]> read)
    {
        _art.Clear();
        _said = false;
        var missing = new List<string>();
        foreach (var (thought, stem) in Art)
        {
            try
            {
                var bytes = read($"/Generic/bubbles/{stem}.ssh");
                if (bytes == null) { missing.Add(stem); continue; }
                var ssh = new Ssh(bytes);
                var img = Image.CreateFromData(ssh.Width, ssh.Height, false, Image.Format.Rgba8, ssh.Pixels);
                _art[thought] = ImageTexture.CreateFromImage(img);
            }
            catch (Exception e) { missing.Add($"{stem} ({e.Message})"); }
        }
        return Report = $"{_art.Count} of {Art.Count} bubbles"
                      + (missing.Count == 0 ? "" : $"; MISSING {string.Join(" ", missing)}");
    }

    /// <summary>Put <paramref name="thought"/> over the guest standing at <paramref name="at"/>.
    /// Normal takes the bubble away rather than drawing one.</summary>
    public void Show(int guest, Thought thought, Vector3 at)
    {
        if (thought == Thought.Normal || !_art.TryGetValue(thought, out var tex)) { Hide(guest); return; }
        if (!_live.TryGetValue(guest, out var sprite) || !GodotObject.IsInstanceValid(sprite))
        {
            sprite = new Sprite3D
            {
                Name = $"thought{guest}",
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Shaded = false,
                Transparent = true,
                // ⚠ The bubble belongs to the guest under it, so it must not be hidden by the
                // crowd in front: the console draws them over everything.
                NoDepthTest = true,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
                RenderPriority = 2,
                // ⭐⭐ MIRRORED. Master, looking at one: "the needs bubbles need to be bigger and
                // mirrored horizontally." The cloud's tail and the asymmetric glyphs -- the WC
                // sign's two figures, the litter, the arrow-ish ones -- come out the wrong way
                // round without this.
                //
                // ⚠ AND THE CAUSE MAY NOT BE THE BUBBLE. These are the first ASYMMETRIC `.ssh`
                // art this port has looked at closely: a texture of grass or wood is mirrored
                // just as wrongly and nobody can tell. So flipping here fixes what master sees
                // and does NOT establish that the decoder is right for everything else -- if the
                // flip is in `Ssh`, every sign, face and logo on the disc is reversed too and
                // this line is papering over it. Worth one asymmetric non-bubble texture to find
                // out; not done.
                FlipH = true,
            };
            Root.AddChild(sprite);
            _live[guest] = sprite;
        }
        sprite.Texture = tex;
        // ⭐ AN A/B, not a setting. A bubble that does not appear could be mis-sized, mis-placed
        // or not drawn at all, and those need different fixes -- driving the size to something
        // absurd tells the three apart in one render. See feedback: prove the instrument can move.
        var over = System.Environment.GetEnvironmentVariable("TPW_WANT_SIZE");
        sprite.PixelSize = over != null && float.TryParse(over, out var px) ? px : Size;
        sprite.Position = at + new Vector3(0f, Height, 0f);
        sprite.Visible = true;
        // ⚠ ONE LINE, ONCE. A picture with no bubble in it cannot tell "nobody wants anything"
        // from "the sprite is somewhere else"; this says where the first one actually went.
        if (!_said)
        {
            _said = true;
            // ⚠ REPORT WHAT IS ACTUALLY SET, not what the field says: the size can be overridden
            // and printing `Size` made a forced 1.6-unit sprite report itself as 0.13.
            GD.Print($"[want] first bubble: guest {guest} {thought} at {sprite.GlobalPosition} "
                   + $"({tex.GetWidth()}x{tex.GetHeight()} texels @ {sprite.PixelSize} = "
                   + $"{tex.GetWidth() * sprite.PixelSize:F2} units wide); visible={sprite.Visible} "
                   + $"inTree={sprite.IsVisibleInTree()} rootInTree={Root.IsVisibleInTree()} "
                   + $"layers={sprite.Layers} parent={Root.GetParent()?.Name}");
        }
    }

    public void Hide(int guest)
    {
        if (_live.TryGetValue(guest, out var s) && GodotObject.IsInstanceValid(s)) s.Visible = false;
    }

    /// <summary>⚠ Take away the bubbles of people who are no longer here. Guest ids are reused, so
    /// a sprite left behind would sit over a stranger -- the same trap <see cref="VisitorNeeds"/>
    /// guards with its own reconcile.</summary>
    public void Sweep(ICollection<int> alive)
    {
        foreach (int id in _live.Keys.Where(id => !alive.Contains(id)).ToArray())
        {
            if (_live[id] is { } s && GodotObject.IsInstanceValid(s)) s.QueueFree();
            _live.Remove(id);
        }
    }

    public void Clear()
    {
        foreach (var s in _live.Values) if (GodotObject.IsInstanceValid(s)) s.QueueFree();
        _live.Clear();
    }
}
