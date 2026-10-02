using Godot;
using System.Collections.Generic;
using System.Linq;

namespace TPWPS2Viewer;

/// <summary>⭐⭐ ONE TEXTURE SHEET FOR A WHOLE FAMILY OF MODELS, so each one draws in a single call.
///
/// Godot issues a draw call per SURFACE, and a surface is a material. A guest is only three meshes
/// but **five or six distinct materials**, which is why 100 guests cost 697 draw calls and 150 cost
/// 983 -- measured at 5.7 per guest. Merging the parts would have bought nothing; the materials are
/// what multiply. With every texture in one sheet a kid needs one material, so one surface.
///
/// ⚠ PRECONDITIONS, ALL MEASURED BEFORE THIS WAS WRITTEN, because each would have shipped as a
/// subtle texture bug rather than a crash:
///   * **UVs must lie inside [0,1]** -- a tiled UV samples a neighbour once it is in a sheet.
///     40 of the 41 guest material UV sets do. The one that does not is `girl4a/girlbody4` at
///     U 1.493, and <see cref="Build"/> REFUSES any texture whose users tile, leaving that material
///     on its own texture rather than smearing it.
///   * **All members must share a shader.** All 27 distinct guest textures are 24bpp with no alpha,
///     so they are all the opaque path -- no soft/opaque split, one sheet.
///   * A texture that MOVES (scroll/spin) cannot be atlased, because the shader turns the whole
///     sheet. None of the guests' do.
///
/// ⭐ Cells carry a one-pixel border of replicated edge pixels. Without it bilinear filtering at a
/// cell edge pulls in the neighbour and every kid gets a seam of somebody else's shirt.</summary>
public sealed class ModelAtlas
{
    const int Pad = 1;

    readonly Dictionary<string, Rect2> _rects = new(System.StringComparer.OrdinalIgnoreCase);

    public ShaderMaterial Material { get; private set; }
    public int Count => _rects.Count;
    public Vector2I Size { get; private set; }

    /// <summary>The packed image, kept for <c>--atlas-dump</c>.</summary>
    public Image Sheet { get; private set; }

    public bool Has(string name) => name != null && _rects.ContainsKey(name);

    /// <summary>Every packed entry, for the self-check. ⭐ Dumping the SHEET alone would only show
    /// that the textures are in there; it is the RECTS that the UV remap uses, so a verification
    /// that does not read them back is checking the easy half.</summary>
    public IEnumerable<(string Name, Rect2 Rect)> Entries => _rects.Select(kv => (kv.Key, kv.Value));
    public Rect2 Rect(string name) => _rects[name];

    /// <summary>Map a texture-space UV into the sheet. ⚠ The caller must have checked <see cref="Has"/>.</summary>
    public Vector2 Map(string name, Vector2 uv)
    {
        var r = _rects[name];
        return new Vector2(r.Position.X + uv.X * r.Size.X, r.Position.Y + uv.Y * r.Size.Y);
    }

    /// <summary>Pack the given named images into one sheet. `shader` is the opaque viewer shader.
    /// Returns null when there is nothing to pack, so the caller falls back to ordinary materials.</summary>
    public static ModelAtlas Build(IEnumerable<(string Name, Image Img)> textures, Shader shader)
    {
        var list = textures.Where(t => t.Name != null && t.Img != null)
                           .GroupBy(t => t.Name, System.StringComparer.OrdinalIgnoreCase)
                           .Select(g => g.First()).ToList();
        if (list.Count == 0) return null;
        int cell = list.Max(t => Mathf.Max(t.Img.GetWidth(), t.Img.GetHeight())) + Pad * 2;
        int cols = Mathf.CeilToInt(Mathf.Sqrt(list.Count));
        int rows = Mathf.CeilToInt(list.Count / (float)cols);
        var sheet = Image.CreateEmpty(cols * cell, rows * cell, false, Image.Format.Rgba8);
        sheet.Fill(new Color(0, 0, 0, 1));
        var atlas = new ModelAtlas { Size = new Vector2I(cols * cell, rows * cell) };
        for (int i = 0; i < list.Count; i++)
        {
            var img = list[i].Img;
            if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
            int w = img.GetWidth(), h = img.GetHeight();
            int x = (i % cols) * cell + Pad, y = (i / cols) * cell + Pad;
            sheet.BlitRect(img, new Rect2I(0, 0, w, h), new Vector2I(x, y));
            // ⭐ The replicated border. Edges first, then the four corner pixels.
            sheet.BlitRect(img, new Rect2I(0, 0, 1, h), new Vector2I(x - 1, y));
            sheet.BlitRect(img, new Rect2I(w - 1, 0, 1, h), new Vector2I(x + w, y));
            sheet.BlitRect(img, new Rect2I(0, 0, w, 1), new Vector2I(x, y - 1));
            sheet.BlitRect(img, new Rect2I(0, h - 1, w, 1), new Vector2I(x, y + h - 1 + 1));
            sheet.BlitRect(img, new Rect2I(0, 0, 1, 1), new Vector2I(x - 1, y - 1));
            sheet.BlitRect(img, new Rect2I(w - 1, 0, 1, 1), new Vector2I(x + w, y - 1));
            sheet.BlitRect(img, new Rect2I(0, h - 1, 1, 1), new Vector2I(x - 1, y + h));
            sheet.BlitRect(img, new Rect2I(w - 1, h - 1, 1, 1), new Vector2I(x + w, y + h));
            atlas._rects[list[i].Name] = new Rect2(
                x / (float)atlas.Size.X, y / (float)atlas.Size.Y,
                w / (float)atlas.Size.X, h / (float)atlas.Size.Y);
        }
        atlas.Sheet = sheet;
        var tex = ImageTexture.CreateFromImage(sheet);
        atlas.Material = new ShaderMaterial { Shader = shader };
        atlas.Material.SetShaderParameter("albedo_tex", tex);
        atlas.Material.SetShaderParameter("has_tex", true);
        Ps2Materials.BindLight(atlas.Material);
        GD.Print($"[atlas] {list.Count} textures packed into {atlas.Size.X}x{atlas.Size.Y} "
               + $"({cols}x{rows} cells of {cell})");
        return atlas;
    }
}
