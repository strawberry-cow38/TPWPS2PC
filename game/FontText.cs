using Godot;
using System;
using System.Collections.Generic;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>⭐⭐ TEXT DRAWN WITH THE GAME'S OWN FONT, because a Godot label is not the money
/// display master asked for.
///
/// `findings/bff-font.md` decoded `DATA.WAD/Fonts/{European,Jap}/{Console,Large,Small}.bff` down
/// to per-glyph coverage, advance, bearings and the line advance, all against the executable's
/// own loader and its metric vtable. Nothing in `game/` had ever used it -- the reader existed,
/// the audit wrote specimens, and every string on screen was still Godot's default face.
///
/// ⚠ Each glyph's `Coverage` is ONE BYTE PER PIXEL holding 0..15, which the font audit scales by
/// 17 to reach 0..255. That is the only transform; there is no packing to undo.
///
/// ⭐ A run is composed into a single image and handed over as one texture, rather than a node
/// per glyph: the money changes rarely, so this recomposes only when the string does.</summary>
public sealed class FontText
{
    readonly BitmapFont _font;
    readonly Dictionary<string, ImageTexture> _cache = new();

    public FontText(BitmapFont font) => _font = font ?? throw new ArgumentNullException(nameof(font));

    public int LineAdvance => _font.LineAdvance;

    /// <summary>The pixel width a run would occupy, by the font's own advances.</summary>
    public int Measure(string text)
    {
        int w = 0;
        foreach (char c in text) if (_font.TryGetGlyph(c, out var g)) w += g.Advance;
        return w;
    }

    /// <summary>⭐ One texture for the whole run. ⚠ White with the coverage as ALPHA: the console
    /// passes a colour constant to its text call and this port does not yet read those, so the
    /// tint is applied by the caller (`Modulate`) rather than baked in here -- which keeps the
    /// unread part visible instead of hidden inside a bitmap.</summary>
    public ImageTexture Render(string text)
    {
        if (_cache.TryGetValue(text, out var cached)) return cached;
        int width = Math.Max(1, Measure(text));
        int height = 1, top = 0;
        foreach (char c in text)
            if (_font.TryGetGlyph(c, out var g)) { height = Math.Max(height, g.Height + g.Top); top = Math.Max(top, g.Top); }
        var pixels = new byte[width * height * 4];
        int pen = 0;
        foreach (char c in text)
        {
            if (!_font.TryGetGlyph(c, out var g)) continue;
            var cov = g.Coverage.Span;
            for (int row = 0; row < g.Height; row++)
                for (int col = 0; col < g.Width; col++)
                {
                    int x = pen + g.Left + col, y = g.Top + row;
                    if (x < 0 || x >= width || y < 0 || y >= height) continue;
                    int at = (y * width + x) * 4;
                    byte a = (byte)Math.Min(255, cov[row * g.Width + col] * 17);
                    if (a <= pixels[at + 3]) continue;
                    pixels[at] = pixels[at + 1] = pixels[at + 2] = 255;
                    pixels[at + 3] = a;
                }
            pen += g.Advance;
        }
        var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
        var tex = ImageTexture.CreateFromImage(image);
        // ⚠ Bounded: a money readout has few distinct strings, but a caller could feed it
        // anything, and an unbounded cache of textures is a leak with a friendly face.
        if (_cache.Count > 64) _cache.Clear();
        _cache[text] = tex;
        return tex;
    }
}
