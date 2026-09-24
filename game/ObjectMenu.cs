using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>The menu a selected object opens -- "Edit Queue", "Delete" -- drawn with the game's
/// own panel art.
///
/// ⭐⭐ EVERY NUMBER HERE WAS READ OFF THE DISC, and the reading is in
/// `reference_tpw_gizmo_bar` / findings. The short version:
///
/// * The panel is TWO NESTED BOXES. Field `+72` of the native panel object picks the skin: 1 is
///   `Messfill` (blue), anything else is `wboxfill` (the pale ruled one). The game draws the same
///   widget class twice, which is why the popup looks like a lined sheet inside a blue frame.
/// * Both are 9-SLICE from 16x16 tiles, and the corners are MIRRORED, not rotated -- that is why
///   only one `Messcorner` ships. `Messcorner` cuts its TOP-RIGHT quadrant and `Messedge` carries
///   its band on the TOP row, both measured by counting pixels rather than assumed.
/// * The blue is ONE colour, `rgb(14,29,255)`, at TWO alphas: 110 for the body (~43%, genuinely
///   see-through) and 247 for the border. There is no second border colour.
/// * The ruled sheet is opaque, `rgb(189,240,240)` with a 2px `rgb(168,230,230)` rule at rows
///   7..8 of every tile -- so the ruling repeats every 16px however tall the panel is.
/// * Height is `40 + 32 * entries`: the per-entry step of 32 is read from the entry loop at
///   `0x15c180` (`addiu s3,s3,32`, in the branch's delay slot) and confirmed against two of
///   master's captures, which measure 144px and 207px for one and two entries at 2x.
///
/// ⚠ WIDTH IS NOT A FORMULA. Two captures of the real game give 504px and 656px for "Delete" and
/// "Edit Queue"; it fits the longest label, so this measures the text and pads it.</summary>
public sealed partial class ObjectMenu : Control
{
    /// <summary>Read from the entry loop at 0x15c180.</summary>
    public const int RowStep = 32;
    /// <summary>`height = Pad + RowStep * entries`, fitted to two captures at 2x.</summary>
    public const int Pad = 40;
    /// <summary>One tile, and the FRAME IS DRAWN A WHOLE TILE THICK. ⚠⚠ It used to be drawn
    /// `Inset` (8) thick while the fill tiled at 16, so the border sat at half the scale of
    /// everything else -- a too-thick bright band, a too-narrow gap, and a visible step where a
    /// corner met an edge. Master: "the corners are fine but the edges are messed up".</summary>
    public const int Tile = 16;

    /// <summary>Where the ruled sheet starts, measured from the frame's outer edge. ⭐ READ OFF
    /// THE REAL GAME: master's captures give a 5px bright band and a 15px translucent gap at 2x,
    /// so 2.5 and 7.5 in console units -- a 10-unit inset with the sheet drawn OVER the inner part
    /// of the frame tiles. Drawing the tiles whole and covering them is what produces a thin band
    /// and a wide gap from one 16px tile.</summary>
    public const int SheetInset = 10;

    /// <summary>The console's UI space is 512 wide; everything here is authored in those units.</summary>
    public const float NativeWidth = 512f;

    /// <summary>⭐⭐ SCALE IS A PERCENTAGE OF THE VIEWPORT, not a constant. Master: "the
    /// scale/position was based off the ps2 resolution. we should measure percentages and apply."
    /// A fixed 2x is only correct on a 1024-wide window; this keeps the panel the same fraction of
    /// the screen the console gives it at any size.</summary>
    float Scale => Mathf.Max(1f, GetViewportRect().Size.X / NativeWidth);

    /// <summary>Selected rows draw blue, the rest black -- read straight off master's capture.</summary>
    static readonly Color Chosen = new(0.07f, 0.12f, 0.98f);
    static readonly Color Plain = new(0.05f, 0.05f, 0.05f);

    // ⭐ ONE tile each ships; these are its flips and turns, built once at load. The game does
    // the same thing at draw time with `SETFLIP` / `ROTATE` -- doing it here keeps the draw a
    // plain blit, which is the only kind Godot tiles correctly.
    readonly ImageTexture _cTL, _cTR, _cBL, _cBR;
    readonly ImageTexture _eTop, _eBottom, _eLeft, _eRight;
    readonly ImageTexture _messFill, _boxFill;
    readonly FontText _font;
    readonly List<string> _entries = new();
    int _index;

    public bool Open { get; private set; }
    public int Index => _index;
    public string Selected => _index >= 0 && _index < _entries.Count ? _entries[_index] : null;
    /// <summary>Raised with the chosen caption when the player confirms.</summary>
    public event Action<string> Activated;

    ObjectMenu(ImageTexture[] corners, ImageTexture[] edges, ImageTexture fill, ImageTexture sheet, FontText font)
    {
        _cTR = corners[0]; _cBR = corners[1]; _cBL = corners[2]; _cTL = corners[3];
        _eTop = edges[0]; _eRight = edges[1]; _eBottom = edges[2]; _eLeft = edges[3];
        _messFill = fill; _boxFill = sheet; _font = font;
        MouseFilter = MouseFilterEnum.Ignore;   // the menu is driven by Viewer's input, not picked
        Visible = false;
        ZIndex = 100;
    }

    /// <summary>Build it from the disc, or answer null if the art is not there. ⭐ Reads UI.WAD
    /// through <see cref="AssetLibrary.ReadUi"/> so the park's open WAD is left alone.</summary>
    public static ObjectMenu Create(AssetLibrary lib, FontText font)
    {
        if (lib == null || font == null) return null;
        ImageTexture Load(string name)
        {
            var raw = lib.ReadUi(name);
            if (raw == null) return null;
            var ssh = new Ssh(raw);
            var img = Image.CreateFromData(ssh.Width, ssh.Height, false, Image.Format.Rgba8, ssh.Pixels);
            return ImageTexture.CreateFromImage(img);
        }
        var cornerRaw = lib.ReadUi("/messages/Messcorner.ssh");
        var edgeRaw = lib.ReadUi("/messages/Messedge.ssh");
        var fill = Load("/messages/Messfill.ssh");
        var sheet = Load("/messages/wboxfill.ssh");
        if (cornerRaw == null || edgeRaw == null || fill == null || sheet == null)
        {
            GD.PrintErr("[menu] UI.WAD panel art missing -- object menu stays off");
            return null;
        }
        // ⭐⭐ MEASURED, not assumed: `Messcorner` cuts its TOP-RIGHT quadrant (transparent pixels
        // per quadrant are TL 0, TR 17, BL 0, BR 0) and `Messedge` carries its band on the TOP row
        // (16 of 16 on top, 0 on the bottom). I got this wrong once by assuming top-left, and the
        // wrongly-turned corners were visible at a glance.
        var c = new Ssh(cornerRaw); var e = new Ssh(edgeRaw);
        var corners = new[] { Turn(c, 0, false, false), Turn(c, 0, false, true),
                              Turn(c, 0, true, true),   Turn(c, 0, true, false) };   // TR, BR, BL, TL
        var edges = new[] { Turn(e, 0, false, false), Turn(e, 1, false, false),
                            Turn(e, 0, false, true),  Turn(e, 3, false, false) };    // top, right, bottom, left
        return new ObjectMenu(corners, edges, fill, sheet, font);
    }

    /// <summary>One tile, optionally turned a quarter at a time and mirrored, as a texture.</summary>
    static ImageTexture Turn(Ssh src, int quarters, bool flipX, bool flipY)
    {
        int w = src.Width, h = src.Height;
        var px = src.Pixels;
        for (int q = 0; q < (quarters & 3); q++)
        {
            var rot = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int nx = h - 1 - y, ny = x;                       // 90 degrees clockwise
                    Array.Copy(px, (y * w + x) * 4, rot, (ny * h + nx) * 4, 4);
                }
            px = rot; (w, h) = (h, w);
        }
        if (flipX || flipY)
        {
            var f = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int sx = flipX ? w - 1 - x : x, sy = flipY ? h - 1 - y : y;
                    Array.Copy(px, (sy * w + sx) * 4, f, (y * w + x) * 4, 4);
                }
            px = f;
        }
        return ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.Rgba8, px));
    }

    /// <summary>Put the menu up with these captions, its top-left near <paramref name="at"/>.</summary>
    public void Show(IEnumerable<string> entries, Vector2 at)
    {
        _entries.Clear();
        _entries.AddRange(entries.Where(e => !string.IsNullOrEmpty(e)));
        if (_entries.Count == 0) { Hide(); return; }
        _index = 0;
        Open = true;
        Visible = true;
        // ⚠ Keep it on screen: the native draw culls rather than clamps, but a menu that opens
        // off the edge of a desktop window is just a menu the player cannot use.
        var size = Measure();
        var vp = GetViewportRect().Size;
        Position = new Vector2(
            Mathf.Clamp(at.X, 4, Mathf.Max(4, vp.X - size.X - 4)),
            Mathf.Clamp(at.Y, 4, Mathf.Max(4, vp.Y - size.Y - 4)));
        QueueRedraw();
    }

    public new void Hide() { Open = false; Visible = false; _entries.Clear(); QueueRedraw(); }

    public void Move(int delta)
    {
        if (!Open || _entries.Count == 0) return;
        _index = (_index + delta % _entries.Count + _entries.Count) % _entries.Count;
        QueueRedraw();
    }

    public void Confirm()
    {
        if (!Open) return;
        string pick = Selected;
        Hide();
        if (pick != null) Activated?.Invoke(pick);
    }

    /// <summary>The sheet's size in SCREEN pixels. ⭐ `40 + 32 * entries` tall, and wide enough
    /// for the longest caption -- the real game's width is content-driven, not a constant.</summary>
    Vector2 Measure()
    {
        int textW = _entries.Count == 0 ? 0 : _entries.Max(e => _font.Measure(e));
        int innerW = Math.Max(textW + RowStep, 6 * Tile);
        int innerH = Pad + RowStep * _entries.Count;
        return new Vector2((innerW + SheetInset * 2) * Scale, (innerH + SheetInset * 2) * Scale);
    }

    public override void _Draw()
    {
        if (!Open || _entries.Count == 0) return;
        int textW = _entries.Max(e => _font.Measure(e));
        int innerW = Math.Max(textW + RowStep, 6 * Tile);
        int innerH = Pad + RowStep * _entries.Count;

        // Outer translucent blue frame, then the opaque ruled sheet inside it.
        DrawBox(new Rect2(0, 0, innerW + SheetInset * 2, innerH + SheetInset * 2));
        DrawSheet(new Rect2(SheetInset, SheetInset, innerW, innerH));

        // ⭐ Rows are RowStep apart, which is the number the disc states; the first sits half a
        // pad down so the block is centred in the sheet.
        for (int i = 0; i < _entries.Count; i++)
        {
            var tex = _font.Render(_entries[i]);
            if (tex == null) continue;
            float w = tex.GetWidth(), h = tex.GetHeight();
            var at = new Vector2(
                (SheetInset + (innerW - w) / 2f) * Scale,
                (SheetInset + Pad / 2f + RowStep * i + (RowStep - h) / 2f) * Scale);
            DrawTextureRect(tex, new Rect2(at, new Vector2(w * Scale, h * Scale)), false,
                            i == _index ? Chosen : Plain);
        }
    }

    /// <summary>A 9-slice, tiled not stretched, with the corners MIRRORED as the game does them.
    /// ⚠ `Messcorner` cuts its TOP-RIGHT quadrant, so the top-right piece is the tile as shipped
    /// and the other three are flips of it -- rotating would put the arcs on the wrong diagonals.</summary>
    /// <summary>The 9-slice frame. ⭐ Every piece is a plain blit of a pre-turned tile, tiled by
    /// hand in <see cref="Tile2D"/> -- Godot's own `tile:true` repeats at the TEXTURE's size, not
    /// the drawn size, so at 2x it laid the border down as stripes with gaps between them.</summary>
    void DrawBox(Rect2 r)
    {
        float s = Scale, t = Tile * s;      // ⭐ a WHOLE tile thick; the sheet covers the inside
        var o = r.Position * s;
        var size = r.Size * s;
        Tile2D(_messFill, new Rect2(o + new Vector2(t, t), size - new Vector2(t * 2, t * 2)));
        Tile2D(_eTop, new Rect2(o + new Vector2(t, 0), new Vector2(size.X - t * 2, t)));
        Tile2D(_eBottom, new Rect2(o + new Vector2(t, size.Y - t), new Vector2(size.X - t * 2, t)));
        Tile2D(_eLeft, new Rect2(o + new Vector2(0, t), new Vector2(t, size.Y - t * 2)));
        Tile2D(_eRight, new Rect2(o + new Vector2(size.X - t, t), new Vector2(t, size.Y - t * 2)));
        DrawTextureRect(_cTL, new Rect2(o, new Vector2(t, t)), false);
        DrawTextureRect(_cTR, new Rect2(o + new Vector2(size.X - t, 0), new Vector2(t, t)), false);
        DrawTextureRect(_cBL, new Rect2(o + new Vector2(0, size.Y - t), new Vector2(t, t)), false);
        DrawTextureRect(_cBR, new Rect2(o + new Vector2(size.X - t, size.Y - t), new Vector2(t, t)), false);
    }

    /// <summary>The ruled sheet. ⭐ TILED IN 16px STEPS WITH A REMAINDER, which is literally what
    /// the native draw does (`rows = height >> 4`, then one short strip). Stretching instead would
    /// make the rule spacing depend on the panel's height, and the whole point of the game's
    /// stepping is that it does not.</summary>
    void DrawSheet(Rect2 r) => Tile2D(_boxFill, new Rect2(r.Position * Scale, r.Size * Scale));

    /// <summary>Repeat a tile across a rect at <see cref="Scale"/>, clipping the last row and
    /// column -- the remainder strip the native code draws explicitly.</summary>
    void Tile2D(Texture2D tex, Rect2 dest)
    {
        float step = Tile * Scale;
        for (float y = 0; y < dest.Size.Y; y += step)
        {
            float hh = Math.Min(step, dest.Size.Y - y);
            for (float x = 0; x < dest.Size.X; x += step)
            {
                float ww = Math.Min(step, dest.Size.X - x);
                DrawTextureRectRegion(tex,
                    new Rect2(dest.Position + new Vector2(x, y), new Vector2(ww, hh)),
                    new Rect2(0, 0, ww / Scale, hh / Scale));
            }
        }
    }
}
