using System.Text;

namespace TPW.PS2.Data;

/// <summary>One menu screen's LAYOUT, read from the `.sce` file that authors it.
///
/// ⭐⭐ THE COORDINATES OF EVERY UI SCREEN ARE DATA, NOT CODE. `MENUS.WAD` holds 29 plain-text
/// `.sce` files, one per screen, and they are the source of every row, column, width and height
/// the menus use. This matters because the obvious place to look -- the executable -- has the
/// layout globals reading ZERO in the image, which reads as "written at init, unknowable". They
/// are written at init: from these files.
///
/// The chain, for the shop screen, all of it verified:
///
///   Data/menus/main_i_shop_data.sce
///     -> FUN_001d68b0            the binder, menu id 0x11
///        FUN_00177978(scene, name)  find a uielem by name   (case-insensitive: the file says
///                                                            `textoptions`, the exe `TextOptions`)
///        FUN_00177ac8(scene)        -> the frame, as [row, col, width, height]
///        FUN_00177b08(scene)        -> the text justification
///     -> globals 0x2e9c48..0x2e9cec
///     -> FUN_001d70c8            the draw
///
/// ⭐ The frame accessor's element ORDER is established, not assumed. The binder stores each
/// element into globals at ascending addresses, and for all nine elements the result is a
/// consistent `{x = col, y = row, w, h}` rect -- e.g. `SatisfactionBar` puts `f[1]` at `0x2e9c50`
/// and `f[0]` at `0x2e9c54`, so `f[1]` is the x. Nine independent elements agreeing on one
/// interpretation is the evidence; a single element would not have been.
///
/// ⚠ WHAT THIS PARSER DOES NOT DO: `&lt;language english&gt;` is read as a wrapper and ignored. Every
/// shipped file on this disc carries exactly one language block, so there is nothing yet to choose
/// between; a disc with more would need this to select one rather than flatten them.</summary>
public sealed class SceneLayout
{
    /// <summary>One `&lt;uielem&gt;`: its frame, and its justification when it declares one.
    ///
    /// ⚠ `Width`/`Height` are 0 when the element does not declare a second `&lt;frame&gt;` line.
    /// A text element has no size in these files -- it is a pen position -- so 0 means
    /// "not authored", not "zero-sized". <see cref="HasSize"/> says which.</summary>
    public readonly record struct Element(int Row, int Col, int Width, int Height, string Justify)
    {
        public bool HasSize => Width > 0 || Height > 0;
        public int X => Col;
        public int Y => Row;
    }

    readonly Dictionary<string, Element> _elements = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The `&lt;menu&gt;` name the file declares, or "" if it declares none.</summary>
    public string Name { get; private set; } = "";

    public IReadOnlyDictionary<string, Element> Elements => _elements;

    /// <summary>The element of that name, or null when the screen does not author one.
    /// ⚠ Answering null is a real case, not an error: the draw code guards every lookup the
    /// same way (`if (FUN_00177978(...) != 0)`), so a missing element means "do not draw it".</summary>
    public Element? this[string name]
        => _elements.TryGetValue(name, out var e) ? e : null;

    public static SceneLayout Parse(byte[] utf8) => Parse(Encoding.Latin1.GetString(utf8 ?? Array.Empty<byte>()));

    public static SceneLayout Parse(string text)
    {
        var layout = new SceneLayout();
        if (string.IsNullOrEmpty(text)) return layout;

        string current = null;
        int row = 0, col = 0, width = 0, height = 0;
        string justify = null;

        void Flush()
        {
            if (current != null) layout._elements[current] = new Element(row, col, width, height, justify);
            current = null; row = col = width = height = 0; justify = null;
        }

        foreach (var raw in text.Split('\n'))
        {
            // `;` starts a comment, and the files are CRLF.
            var line = raw.Replace('\r', ' ');
            int semi = line.IndexOf(';');
            if (semi >= 0) line = line[..semi];
            line = line.Trim();
            if (line.Length == 0 || line[0] != '<') continue;

            int close = line.IndexOf('>');
            var tag = (close < 0 ? line[1..] : line[1..close]).Trim();
            if (tag.Length == 0) continue;

            if (tag.StartsWith("/uielem", StringComparison.OrdinalIgnoreCase)) { Flush(); continue; }

            var parts = tag.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0].ToLowerInvariant())
            {
                case "menu":
                    if (parts.Length > 1) layout.Name = parts[1];
                    break;
                case "uielem":
                    // A file that omits `</uielem>` would otherwise merge two elements into one.
                    Flush();
                    if (parts.Length > 1) current = parts[1];
                    break;
                case "frame":
                    // ⭐ An element authors its frame over MORE THAN ONE line: `row`/`col` on the
                    // first and `width`/`height` on a second. Merging rather than replacing is the
                    // whole reason this is accumulated instead of parsed per line.
                    foreach (var p in parts[1..])
                    {
                        var (k, v) = Split(p);
                        if (v == null) continue;
                        switch (k)
                        {
                            case "row": row = v.Value; break;
                            case "col": col = v.Value; break;
                            case "width": width = v.Value; break;
                            case "height": height = v.Value; break;
                        }
                    }
                    break;
                case "text":
                    foreach (var p in parts[1..])
                    {
                        int eq = p.IndexOf('=');
                        if (eq > 0 && p[..eq].Equals("justify", StringComparison.OrdinalIgnoreCase))
                            justify = p[(eq + 1)..];
                    }
                    break;
            }
        }
        Flush();
        return layout;
    }

    static (string Key, int? Value) Split(string pair)
    {
        int eq = pair.IndexOf('=');
        if (eq <= 0) return (null, null);
        return int.TryParse(pair[(eq + 1)..], out int v)
            ? (pair[..eq].ToLowerInvariant(), v)
            : (pair[..eq].ToLowerInvariant(), null);
    }
}
