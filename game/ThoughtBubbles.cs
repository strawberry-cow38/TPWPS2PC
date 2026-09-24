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

    /// <summary>⚠ Chosen -- see the class note -- but no longer chosen BLIND. Measured off
    /// astraclaw's first real capture of a served customer (1280x720, ordinary game camera): the
    /// bubble came out **23 px wide**, 1.8% of frame width, which leaves about twelve pixels of
    /// actual pictogram. The cloud reads as a cloud; what is IN it does not, so toilet, hungry and
    /// angry are the same grey smudge -- and a bubble whose whole job is to say WHICH want is
    /// pressing has failed if you cannot tell them apart.
    ///
    /// ⭐ Sized by arithmetic rather than by eye: 0.0042 gave 23 px, so 0.0088 gives ~48, which is
    /// the smallest a 32-px glyph reads at. Height rises with it because the sprite is CENTRED on
    /// its position -- doubling the size drops its lower edge by half the gain, and at 0.62 the
    /// kid's hair already overlapped the bottom of the cloud in that same capture.
    ///
    /// ⚠ ONE informed iteration, not a tuning session: this is a computed target from a measured
    /// starting point, and it wants ONE re-capture to confirm rather than a series of guesses.</summary>
    public float Height { get; set; } = 0.80f;
    public float Size { get; set; } = 0.0088f;

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
