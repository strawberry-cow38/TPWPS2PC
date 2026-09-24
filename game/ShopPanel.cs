using Godot;
using System;
using System.Collections.Generic;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>A shop's info panel -- the "Single Shop" screen.
///
/// ⭐⭐ THE GEOMETRY IS THE CONSOLE'S, read at `0x108458` and written up in
/// findings/shop-info-ui.md:
///
/// * **260 x 180**, at **y = 210**
/// * **x = (screenWidth - 260) / 2** -- centred, and the console asks the engine for the width
///   (`0x20a120`) rather than baking one in
/// * skin sprite **48 = `Messfill`**, the blue frame -- NOT the ruled sheet the little object
///   menu uses
/// * `+92 = 16` and `+96 = 14`, used here as the side margin and the row height
///
/// ⭐⭐ AND IT IS SIZED AS A FRACTION OF THE SCREEN, not in fixed pixels. Master: "remember
/// measuring percentages of screen space instead of fixed px values." The console authors this
/// in a 512-wide space, so 260 of those units is 50.8% of the width at ANY resolution; baking
/// 260 device pixels would shrink the panel to nothing on a big window and overflow a small one.
///
/// ⚠ WHAT IS NOT READ: the row positions. The console takes x, the first y and the row step from
/// globals (`[0x2E9E08]`, `[0x2E9E0C]`, `[0x2E9E68]`) that are written at init and read 0 in the
/// executable image. The margin and row height below are the panel's OWN `+92`/`+96`, which at
/// least come off this panel's setup rather than being invented -- but they are a stand-in for
/// those three globals, not a decode of them.</summary>
public sealed partial class ShopPanel : Control
{
    /// <summary>The console's UI space: everything below is in these units.</summary>
    public const float NativeWidth = 512f;
    public const int PanelWidth = 260, PanelHeight = 180, PanelY = 210;
    /// <summary>`+92` and `+96` off the panel's own setup.</summary>
    public const int Margin = 16, RowHeight = 14;

    static readonly Color Ink = new(0.05f, 0.05f, 0.05f);
    static readonly Color Heading = new(0.07f, 0.12f, 0.98f);

    readonly ImageTexture _cTL, _cTR, _cBL, _cBR, _eTop, _eBottom, _eLeft, _eRight, _fill;
    readonly FontText _font;
    readonly List<(string Label, string Value)> _rows = new();
    string _title = "";

    public bool Open { get; private set; }

    /// <summary>A fraction of the viewport, never a pixel count.</summary>
    float Scale => Mathf.Max(1f, GetViewportRect().Size.X / NativeWidth);

    ShopPanel(ImageTexture[] c, ImageTexture[] e, ImageTexture fill, FontText font)
    {
        _cTR = c[0]; _cBR = c[1]; _cBL = c[2]; _cTL = c[3];
        _eTop = e[0]; _eRight = e[1]; _eBottom = e[2]; _eLeft = e[3];
        _fill = fill; _font = font;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        ZIndex = 110;
    }

    public static ShopPanel Create(AssetLibrary lib, FontText font)
    {
        if (lib == null || font == null) return null;
        var cornerRaw = lib.ReadUi("/messages/Messcorner.ssh");
        var edgeRaw = lib.ReadUi("/messages/Messedge.ssh");
        var fillRaw = lib.ReadUi("/messages/Messfill.ssh");
        if (cornerRaw == null || edgeRaw == null || fillRaw == null)
        {
            GD.PrintErr("[shop] UI.WAD panel art missing -- shop panel stays off");
            return null;
        }
        var c = new Ssh(cornerRaw); var e = new Ssh(edgeRaw);
        // Corner cuts its TOP-RIGHT quadrant and the edge band is on the TOP row, both measured.
        var corners = new[] { Tile(c, 0, false, false), Tile(c, 0, false, true),
                              Tile(c, 0, true, true),   Tile(c, 0, true, false) };
        var edges = new[] { Tile(e, 0, false, false), Tile(e, 1, false, false),
                            Tile(e, 0, false, true),  Tile(e, 3, false, false) };
        var fill = Tile(new Ssh(fillRaw), 0, false, false);
        return new ShopPanel(corners, edges, fill, font);
    }

    static ImageTexture Tile(Ssh src, int quarters, bool flipX, bool flipY)
    {
        int w = src.Width, h = src.Height; var px = src.Pixels;
        for (int q = 0; q < (quarters & 3); q++)
        {
            var rot = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    Array.Copy(px, (y * w + x) * 4, rot, (x * h + (h - 1 - y)) * 4, 4);
            px = rot; (w, h) = (h, w);
        }
        if (flipX || flipY)
        {
            var f = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    Array.Copy(px, ((flipY ? h - 1 - y : y) * w + (flipX ? w - 1 - x : x)) * 4,
                               f, (y * w + x) * 4, 4);
            px = f;
        }
        return ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.Rgba8, px));
    }

    /// <summary>Put the panel up for a shop. ⭐ The row ORDER is the console's, read from the
    /// builder at `0x1d7288`: Customers, Cost of Goods, Takings, Profit, Satisfaction, Quality,
    /// then Sale Price. The ingredient row follows it because it belongs to the same screen.</summary>
    public void ShowFor(ParkRide shop, string displayName)
    {
        _rows.Clear();
        _title = displayName ?? shop?.Name ?? "Single Shop";
        if (shop == null) { Hide(); return; }

        var settings = shop.Definition?.CompiledEntry?.Shop;
        int price = settings?.InitialPrice ?? 0;
        int baseCost = settings?.BaseCostOfGoods ?? 0;
        // ⭐ The console's own arithmetic, from ParkSim's decode of the want base: the cost moves
        // with Quality and with Setting0xAC, both quartered. Not a display fudge -- the same
        // expression the purchase path uses.
        int cost = baseCost * ((shop.Quality >> 2) + 75 - (shop.Setting0xAC >> 2)) / 100;

        _rows.Add(("Customers", shop.Customers.ToString()));
        _rows.Add(("Cost of Goods", Money(cost)));
        _rows.Add(("Takings", Money(shop.Takings)));
        _rows.Add(("Profit", Money(shop.Profit)));
        // ⚠ Satisfaction is shop[0xb0]/shop[0xb4]: the accumulator at +0xB0 is decoded but not yet
        // tracked here, so this says so rather than printing a zero that looks like a rating.
        _rows.Add(("Satisfaction", "--"));
        _rows.Add(("Quality", shop.Quality.ToString()));
        _rows.Add(("Sale Price", Money(price)));

        var payload = shop.Definition?.CompiledEntry?.Payload ?? default;
        int ingredient = payload.Length > 0 ? ShopIngredient.IdFrom(payload.Span) : -1;
        string caption = ingredient >= 0 ? ShopIngredient.CaptionOf(ingredient) : null;
        if (caption != null) _rows.Add((caption, "--"));

        Open = true; Visible = true;
        QueueRedraw();
    }

    public new void Hide() { Open = false; Visible = false; _rows.Clear(); QueueRedraw(); }

    static string Money(int v) => v < 0 ? $"-${-v:N0}" : $"${v:N0}";

    public override void _Draw()
    {
        if (!Open) return;
        float s = Scale;
        var vp = GetViewportRect().Size;
        // ⭐ Centred, exactly as the console does it -- against the LIVE viewport width, so the
        // panel keeps its share of the screen instead of drifting off a wider one.
        float x = (vp.X - PanelWidth * s) / 2f;
        float y = PanelY * s;
        DrawFrame(new Rect2(x, y, PanelWidth * s, PanelHeight * s));

        float left = x + Margin * s, right = x + (PanelWidth - Margin) * s;
        // ⚠ THE ROW STEP IS THE FONT'S OWN LINE ADVANCE when that is taller than the panel's
        // `+96`. The console takes this from a global (`[0x2E9E68]`) that reads 0 in the image, so
        // there is no number to copy; what there IS is the rule that rows must not collide, and
        // the font states its own advance. `+96` stays as the floor.
        float step = Math.Max(RowHeight, _font.LineAdvance) * s;
        float row = y + Margin * s;
        DrawRun(_title, left, row, s, Heading);
        row += step * 1.5f;
        foreach (var (label, value) in _rows)
        {
            DrawRun(label, left, row, s, Ink);
            if (value != null) DrawRun(value, right - _font.Measure(value) * s, row, s, Ink);
            row += step;
        }
    }

    void DrawRun(string text, float x, float y, float s, Color tint)
    {
        var tex = _font.Render(text);
        if (tex == null) return;
        DrawTextureRect(tex, new Rect2(x, y, tex.GetWidth() * s, tex.GetHeight() * s), false, tint);
    }

    /// <summary>The same 9-slice the object menu draws, at this panel's scale: whole-tile borders
    /// with the body tiled under them, corners MIRRORED rather than rotated.</summary>
    void DrawFrame(Rect2 r)
    {
        float s = Scale, t = 16 * s;
        var o = r.Position; var size = r.Size;
        TileRect(_fill, new Rect2(o + new Vector2(t, t), size - new Vector2(t * 2, t * 2)));
        TileRect(_eTop, new Rect2(o + new Vector2(t, 0), new Vector2(size.X - t * 2, t)));
        TileRect(_eBottom, new Rect2(o + new Vector2(t, size.Y - t), new Vector2(size.X - t * 2, t)));
        TileRect(_eLeft, new Rect2(o + new Vector2(0, t), new Vector2(t, size.Y - t * 2)));
        TileRect(_eRight, new Rect2(o + new Vector2(size.X - t, t), new Vector2(t, size.Y - t * 2)));
        DrawTextureRect(_cTL, new Rect2(o, new Vector2(t, t)), false);
        DrawTextureRect(_cTR, new Rect2(o + new Vector2(size.X - t, 0), new Vector2(t, t)), false);
        DrawTextureRect(_cBL, new Rect2(o + new Vector2(0, size.Y - t), new Vector2(t, t)), false);
        DrawTextureRect(_cBR, new Rect2(o + new Vector2(size.X - t, size.Y - t), new Vector2(t, t)), false);
    }

    /// <summary>⚠ Hand-tiled. Godot's `tile:true` repeats at the TEXTURE's size, not the drawn
    /// size, which at any scale above 1 lays the art down as stripes with gaps.</summary>
    void TileRect(Texture2D tex, Rect2 dest)
    {
        float step = 16 * Scale;
        for (float yy = 0; yy < dest.Size.Y; yy += step)
        {
            float hh = Math.Min(step, dest.Size.Y - yy);
            for (float xx = 0; xx < dest.Size.X; xx += step)
            {
                float ww = Math.Min(step, dest.Size.X - xx);
                DrawTextureRectRegion(tex,
                    new Rect2(dest.Position + new Vector2(xx, yy), new Vector2(ww, hh)),
                    new Rect2(0, 0, ww / Scale, hh / Scale));
            }
        }
    }
}
