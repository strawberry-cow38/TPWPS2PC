using System;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>⭐⭐ THE GAME'S PANEL FRAME -- the nine-slice the console draws every message box and
/// context menu with, from `UI.WAD/messages/`.
///
/// ⭐ The art is a SPRITE REGISTRY entry, not a path chosen here: `FUN_00216028` registers
/// `messages\messcorner.ssh` as id **0x2F**, `messages\messfill.ssh` as **0x30**,
/// `messages\wboxfill.ssh` as **0x31** and `messages\messedge.ssh` next. All three of the box
/// pieces are **16x16**.
///
/// ⚠⚠ I ONCE CONCLUDED THERE WAS NO FRAME AT ALL. The widget path that looks like it should draw
/// one -- `FUN_00142610`'s four corners and four edges -- is **eight stubs**, and so is every
/// painter they call, so reading only the code said "nothing is drawn". Master, who has played
/// it: "there is a frame btw". The art settles it: `Messcorner`/`Messedge`/`Messfill` exist, are
/// registered, and this port was ALREADY drawing them for the context menu. An empty function
/// proves that function draws nothing -- it does not prove the thing is not drawn.
/// See `findings/lobby-menu.md`.</summary>
public sealed class UiPanel
{
    /// <summary>Every piece is 16x16 on the disc.</summary>
    public const int Tile = 16;

    readonly ImageTexture _cTL, _cTR, _cBL, _cBR;
    readonly ImageTexture _eTop, _eBottom, _eLeft, _eRight;
    readonly ImageTexture _fill, _sheet;

    UiPanel(ImageTexture[] corners, ImageTexture[] edges, ImageTexture fill, ImageTexture sheet)
    {
        _cTR = corners[0]; _cBR = corners[1]; _cBL = corners[2]; _cTL = corners[3];
        _eTop = edges[0]; _eRight = edges[1]; _eBottom = edges[2]; _eLeft = edges[3];
        _fill = fill; _sheet = sheet;
    }

    /// <summary>The ruled inner sheet (`wboxfill`), for callers that want it.</summary>
    public ImageTexture Sheet => _sheet;

    /// <summary>The pieces one at a time, already turned, for a caller that draws its own shape of frame --
    /// the advisor's message records (`0x108AB0`) blit `messfill` (0x30), `messcorner` (0x2F) raw and flipped in
    /// y, and `messedge` (0x32) raw, flipped in y and turned, which are exactly these; the advisor's read box
    /// (`0x142090`, <see cref="AdvisorStackView"/>) adds the left-hand three.</summary>
    public ImageTexture Fill => _fill;
    public ImageTexture CornerTopRight => _cTR;
    public ImageTexture CornerBottomRight => _cBR;
    /// <summary>`0x213668(prim, 1, 0)`: the corner flipped in x.</summary>
    public ImageTexture CornerTopLeft => _cTL;
    /// <summary>`0x213668(prim, 1, 1)`: the corner flipped in x and y.</summary>
    public ImageTexture CornerBottomLeft => _cBL;
    public ImageTexture EdgeTop => _eTop;
    public ImageTexture EdgeBottom => _eBottom;
    /// <summary>`0x2136D0(prim, 2)` (flags 0x1010), the quarter turn the record frames' side edge already uses.</summary>
    public ImageTexture EdgeRight => _eRight;
    /// <summary>`0x2136D0(prim, 0)` (flags 0x1020): the opposite quarter turn -- the right edge mirrored.</summary>
    public ImageTexture EdgeLeft => _eLeft;

    /// <summary>Load the set out of `UI.WAD`. ⚠ Through <see cref="AssetLibrary.ReadUi"/>, so the
    /// park's own open archive is left alone.</summary>
    public static UiPanel Load(AssetLibrary lib)
    {
        if (lib == null) return null;
        ImageTexture Tex(string name)
        {
            var raw = lib.ReadUi(name);
            if (raw == null) return null;
            var ssh = new Ssh(raw);
            var img = Image.CreateFromData(ssh.Width, ssh.Height, false, Image.Format.Rgba8, ssh.Pixels);
            return ImageTexture.CreateFromImage(img);
        }
        var cornerRaw = lib.ReadUi("/messages/Messcorner.ssh");
        var edgeRaw = lib.ReadUi("/messages/Messedge.ssh");
        var fill = Tex("/messages/Messfill.ssh");
        var sheet = Tex("/messages/wboxfill.ssh");
        if (cornerRaw == null || edgeRaw == null || fill == null || sheet == null) return null;

        // ⭐⭐ MEASURED, not assumed: `Messcorner` cuts its TOP-RIGHT quadrant (transparent pixels
        // per quadrant are TL 0, TR 17, BL 0, BR 0) and `Messedge` carries its band on the TOP row
        // (16 of 16 on top, 0 on the bottom). Assuming top-left gets the corners visibly wrong.
        var c = new Ssh(cornerRaw); var e = new Ssh(edgeRaw);
        var corners = new[] { Turn(c, 0, false, false), Turn(c, 0, false, true),
                              Turn(c, 0, true, true),   Turn(c, 0, true, false) };   // TR, BR, BL, TL
        var edges = new[] { Turn(e, 0, false, false), Turn(e, 1, false, false),
                            Turn(e, 0, false, true),  Turn(e, 3, false, false) };    // top, right, bottom, left
        return new UiPanel(corners, edges, fill, sheet);
    }

    /// <summary>The nine-slice, into any canvas. ⭐ Every piece is a plain blit of a pre-turned
    /// tile, tiled by hand -- Godot's own `tile:true` repeats at the TEXTURE's size rather than the
    /// drawn size, so at 2x it lays the border down as stripes with gaps between them.</summary>
    public void Draw(CanvasItem into, Rect2 rect, float scale)
    {
        float t = Tile * scale;
        var o = rect.Position;
        var size = rect.Size;
        Tile2D(into, _fill, new Rect2(o + new Vector2(t, t), size - new Vector2(t * 2, t * 2)), scale);
        Tile2D(into, _eTop, new Rect2(o + new Vector2(t, 0), new Vector2(size.X - t * 2, t)), scale);
        Tile2D(into, _eBottom, new Rect2(o + new Vector2(t, size.Y - t), new Vector2(size.X - t * 2, t)), scale);
        Tile2D(into, _eLeft, new Rect2(o + new Vector2(0, t), new Vector2(t, size.Y - t * 2)), scale);
        Tile2D(into, _eRight, new Rect2(o + new Vector2(size.X - t, t), new Vector2(t, size.Y - t * 2)), scale);
        into.DrawTextureRect(_cTL, new Rect2(o, new Vector2(t, t)), false);
        into.DrawTextureRect(_cTR, new Rect2(o + new Vector2(size.X - t, 0), new Vector2(t, t)), false);
        into.DrawTextureRect(_cBL, new Rect2(o + new Vector2(0, size.Y - t), new Vector2(t, t)), false);
        into.DrawTextureRect(_cBR, new Rect2(o + new Vector2(size.X - t, size.Y - t), new Vector2(t, t)), false);
    }

    /// <summary>Repeat a tile across a rect, clipping the last row and column -- the remainder
    /// strip the native draw does explicitly (`rows = height >> 4`, then one short strip).</summary>
    public static void Tile2D(CanvasItem into, Texture2D tex, Rect2 dest, float scale)
    {
        float step = Tile * scale;
        if (step <= 0f) return;
        for (float y = 0; y < dest.Size.Y; y += step)
        {
            float hh = Math.Min(step, dest.Size.Y - y);
            for (float x = 0; x < dest.Size.X; x += step)
            {
                float ww = Math.Min(step, dest.Size.X - x);
                into.DrawTextureRectRegion(tex,
                    new Rect2(dest.Position + new Vector2(x, y), new Vector2(ww, hh)),
                    new Rect2(0, 0, ww / scale, hh / scale));
            }
        }
    }

    /// <summary>One tile, optionally turned a quarter at a time and mirrored.</summary>
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
        var img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, px);
        return ImageTexture.CreateFromImage(img);
    }
}
