using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using TPW.PS2.Data;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("usage: dotnet run --project tools/TPW.PS2.FontAudit -- <disc.bin> [output-directory]\n" +
        "       dotnet run --project tools/TPW.PS2.FontAudit -- --self-test");
    return 1;
}
int failures = 0;
void Check(bool ok, string message)
{
    if (!ok) { failures++; Console.Error.WriteLine("FAIL " + message); }
}

try
{
    SelfTests();
    if (args[0] == "--self-test") return failures == 0 ? 0 : 2;
    string output = args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "tpwps2-font-audit");
    Directory.CreateDirectory(output);

    // Canonical decoded data, NOT source-file hashes or counts. Initially obtained with a
    // separate Python byte walk after checking the executable and inspecting rendered sheets.
    // Includes every code->index mapping, metric and ordered coverage sample (see findings).
    var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        ["/Fonts/European/Console.bff"] = "292ab678daa21330045b630453b63198e189958c4c60eac5c2ec293fcd2f65ea",
        ["/Fonts/European/Large.bff"] = "6b78560c778aeb7197b3c879d32e11824621373357571c38e5857b86c0f90710",
        ["/Fonts/European/Small.bff"] = "29ec2dfccf88676254ef3c9c419ffb745f2a75624b597088f761d5c57a3b67c1",
        ["/Fonts/Jap/Console.bff"] = "292ab678daa21330045b630453b63198e189958c4c60eac5c2ec293fcd2f65ea",
        ["/Fonts/Jap/Large.bff"] = "aa05aa368a9cac5faa704cd61322d26ea2da16d6d646d59ddba1395f2adda083",
        ["/Fonts/Jap/Small.bff"] = "00a6b4408b5f752ddd8a57210df391f015277c659a426785c6c045e323115643"
    };
    using var disc = new Disc(args[0]);
    var wadEntry = disc.Files().Single(e => !e.IsDirectory &&
        e.Path.EndsWith("/DATA.WAD", StringComparison.OrdinalIgnoreCase));
    var wad = new WadArchive(disc.Read(wadEntry.Extent, wadEntry.Size));
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in wad.Entries.Where(e => e.Path.EndsWith(".bff", StringComparison.OrdinalIgnoreCase)))
    {
        Check(seen.Add(entry.Path), "duplicate font path " + entry.Path);
        try
        {
            byte[] raw = wad.Read(entry);
            var font = new BitmapFont(raw);
            string digest = Digest(font);
            Check(expected.TryGetValue(entry.Path, out var pin) && digest == pin,
                $"{entry.Path}: decoded mapping/metrics/pixel digest {digest}");
            CheckLayout(font, raw, entry.Path);
            CheckLookup(font, entry.Path);
            if (entry.Name.Equals("Console.bff", StringComparison.OrdinalIgnoreCase)) CheckConsoleShapes(font);
            string stem = entry.Path.Trim('/').Replace('/', '-');
            bool sjis = entry.Path.Contains("/Jap/", StringComparison.OrdinalIgnoreCase) &&
                !entry.Name.Equals("Console.bff", StringComparison.OrdinalIgnoreCase);
            Render(font, sjis, Path.Combine(output, stem));
            Console.WriteLine($"{entry.Path}: {font.Glyphs.Count} glyphs; decoded SHA256 {digest}");
        }
        catch (Exception ex) { Check(false, entry.Path + ": " + ex.Message); }
    }
    foreach (string path in expected.Keys) Check(seen.Contains(path), "missing font " + path);
    Console.WriteLine($"PGM specimens, atlases and code indexes: {Path.GetFullPath(output)}");
    Console.WriteLine($"{failures} failures. Checks ordered pixels, mappings, metrics, bounds, layout and malformed input.");
    Console.WriteLine("Cannot establish unknown fields, identify every glyph, or verify the game's GS rendering/localisation path.");
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
return failures == 0 ? 0 : 2;

string Digest(BitmapFont font)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write(font.Metric05); writer.Write(font.Metric06); writer.Write(font.LineAdvance);
    for (int code = 0; code <= ushort.MaxValue; code++)
    {
        if (!font.TryGetGlyph((ushort)code, out var g)) continue;
        writer.Write((ushort)code);
        foreach (int value in new[] { g.Index, g.Width, g.Height, g.Left, g.Top, g.Advance }) writer.Write(value);
        writer.Write(g.Coverage.Span);
    }
    return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
}

void CheckLookup(BitmapFont font, string path)
{
    // Independent linear range walk checks the binary-search boundaries and every gap, aliases
    // included. FFFF is a real mapped code in European fonts, not an automatic terminator.
    int range = 0;
    for (int code = 0; code <= ushort.MaxValue; code++)
    {
        while (range < font.Ranges.Count && font.Ranges[range].End < code) range++;
        bool mapped = range < font.Ranges.Count && font.Ranges[range].Start <= code;
        bool found = font.TryGetGlyph((ushort)code, out var glyph);
        Check(found == mapped && (!found || glyph.Index == code - font.Ranges[range].Subtract),
            $"{path}: lookup {code:x4}");
    }
}

void CheckLayout(BitmapFont font, byte[] raw, string path)
{
    int cursor = font.GlyphDataOffset + font.Glyphs.Count * 14;
    var referenced = new HashSet<int>();
    foreach (var range in font.Ranges)
        for (int c = range.Start; c <= range.End; c++) referenced.Add(c - range.Subtract);
    foreach (var g in font.Glyphs)
    {
        Check(referenced.Contains(g.Index), $"{path}: unreachable descriptor {g.Index}");
        if (g.Coverage.IsEmpty)
        {
            Check(g.Width == 0 && g.Height == 0 && g.RelativeDataOffset == 0,
                $"{path}: unexpected empty descriptor {g.Index}");
            continue;
        }
        long start = font.GlyphDataOffset + g.Index * 14L + g.RelativeDataOffset;
        Check(start == cursor, $"{path}: bitmap gap/overlap at glyph {g.Index}");
        cursor += (g.Width * g.Height + 1) / 2;
        if ((g.Width * g.Height & 1) != 0)
            Check((raw[cursor - 1] & 15) == 0, $"{path}: nonzero unused final nibble {g.Index}");
    }
    Check(cursor == raw.Length, path + ": unaccounted payload/trailing bytes");
}

void CheckConsoleShapes(BitmapFont font)
{
    font.TryGetGlyph('F', out var f); font.TryGetGlyph('R', out var r); font.TryGetGlyph('g', out var g);
    Check(f is { Width: 5, Height: 9, Left: 0, Top: 0, Advance: 6 } &&
        Enumerable.Range(0, 9).All(y => f.Coverage.Span[y * 5] == 15) &&
        Enumerable.Range(0, 5).All(x => f.Coverage.Span[x] == 15 && f.Coverage.Span[20 + x] == 15) &&
        f.Coverage.Span[9] == 0 && f.Coverage.Span[44] == 0,
        "Console F shape: left stem, top/middle bars, open lower right");
    Check(r is { Width: 8, Height: 9 } && r.Coverage.Span[50] == 0 &&
        r.Coverage.Span[54] == 9 && r.Coverage.Span[71] == 11,
        "Console R shape: antialiased leg descends to the right");
    Check(g is { Width: 6, Height: 9, Top: 2, Advance: 7 } &&
        g.Top + g.Height > f.Top + f.Height && g.Coverage.Span[48] == 8 && g.Coverage.Span[49] == 15,
        "Console g shape: descender below F with antialiased bottom stroke");
}

void Render(BitmapFont font, bool sjis, string stem)
{
    var lines = new List<ushort[]> {
        "FRg jpq 0123456789".Select(c => (ushort)c).ToArray(),
        "Theme Park World!".Select(c => (ushort)c).ToArray(),
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => (ushort)c).ToArray(),
        "abcdefghijklmnopqrstuvwxyz".Select(c => (ushort)c).ToArray()
    };
    // Explicit packed Shift-JIS codes, avoiding a platform encoding dependency.
    if (sjis) lines.Add(new ushort[] { 0x82a0, 0x82a2, 0x82a4, 0x82a6, 0x82a8, 0x20,
        0x834a, 0x834c, 0x834e, 0x8350, 0x8352, 0x20, 0x93fa, 0x967b }); // あいうえお カキクケコ 日本
    else
    {
        lines.Add("é Œ œ".Select(c => (ushort)c).ToArray());
        if (font.TryGetGlyph(0x03a9, out _)) lines.Add("Δ Ω µ π ∑ √".Select(c => (ushort)c).ToArray());
    }
    const int width = 640;
    int height = 16 + lines.Count * font.LineAdvance;
    var pixels = new byte[width * height];
    int y = 8;
    foreach (var line in lines)
    {
        int x = 8;
        foreach (ushort code in line)
        {
            if (!font.TryGetGlyph(code, out var glyph)) { Check(false, $"specimen missing {code:x4}: {stem}"); continue; }
            Blit(glyph, pixels, width, height, x + glyph.Left, y + glyph.Top);
            x += glyph.Advance;
        }
        y += font.LineAdvance;
    }
    Pgm(stem + "-specimen.pgm", pixels, width, height, 3);

    int cellWidth = font.Glyphs.Max(g => g.Width) + 8, cellHeight = font.Glyphs.Max(g => g.Height) + 8;
    int atlasWidth = cellWidth * 16, atlasHeight = cellHeight * ((font.Glyphs.Count + 15) / 16);
    var atlas = new byte[atlasWidth * atlasHeight];
    var index = new StringBuilder("index\tcodes\twidth\theight\tleft\ttop\tadvance\n");
    foreach (var glyph in font.Glyphs)
    {
        Blit(glyph, atlas, atlasWidth, atlasHeight,
            glyph.Index % 16 * cellWidth + 4, glyph.Index / 16 * cellHeight + 4);
        var codes = font.Ranges.SelectMany(r => Enumerable.Range(r.Start, r.End - r.Start + 1)
            .Where(c => c - r.Subtract == glyph.Index)).Select(c => c.ToString("X4"));
        index.AppendLine($"{glyph.Index}\t{string.Join(',', codes)}\t{glyph.Width}\t{glyph.Height}\t" +
            $"{glyph.Left}\t{glyph.Top}\t{glyph.Advance}");
    }
    Pgm(stem + "-atlas.pgm", atlas, atlasWidth, atlasHeight, 1);
    File.WriteAllText(stem + "-index.tsv", index.ToString());
}

void Blit(BitmapFont.Glyph glyph, byte[] pixels, int width, int height, int x, int y)
{
    for (int row = 0; row < glyph.Height; row++)
        for (int col = 0; col < glyph.Width; col++)
        {
            int tx = x + col, ty = y + row;
            if (tx < 0 || tx >= width || ty < 0 || ty >= height) continue;
            int at = ty * width + tx;
            pixels[at] = Math.Max(pixels[at], (byte)(glyph.Coverage.Span[row * glyph.Width + col] * 17));
        }
}

void Pgm(string path, byte[] pixels, int width, int height, int scale)
{
    using var file = File.Create(path);
    file.Write(Encoding.ASCII.GetBytes($"P5\n{width * scale} {height * scale}\n255\n"));
    for (int y = 0; y < height * scale; y++)
        for (int x = 0; x < width * scale; x++) file.WriteByte(pixels[y / scale * width + x / scale]);
}

void SelfTests()
{
    // Hand-authored fixture: 3x3 odd-width F-like mask, fractional coverage, signed bearings,
    // an empty space, a range alias, gaps, and a mapped FFFF. No extracted game bytes.
    byte[] Fixture()
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write("2FFB"u8); w.Write(new byte[] { 1, 9, 3, 14, 0xcc, 0xf8 });
        w.Write("ULGU"u8); w.Write(0x01000018u); w.Write((ushort)4);
        foreach (ushort value in new ushort[] { 0x20, 0x46, 0x52, 0xffff,
            0x20, 0x46, 0x52, 0xffff, 0x20, 0x45, 0x51, 0xfffd }) w.Write(value);
        w.Write("DGFB"u8); w.Write(0x01000030u);
        void Glyph(uint offset, byte width, byte height, sbyte left, sbyte top, byte advance)
        {
            w.Write(0u); w.Write(offset); w.Write(width); w.Write(height);
            w.Write(left); w.Write(top); w.Write(advance); w.Write((byte)0x57);
        }
        Glyph(0, 0, 0, 0, 0, 3); Glyph(28, 3, 3, -1, -2, 4); Glyph(19, 1, 2, 2, 1, 3);
        w.Write(new byte[] { 0xff, 0xf8, 0x00, 0xff, 0x00, 0x1e });
        return stream.ToArray();
    }
    byte[] good = Fixture(); var font = new BitmapFont(good);
    Check(font.TryGetGlyph('F', out var f) && f.Coverage.Span.SequenceEqual(new byte[] { 15, 15, 15, 8, 0, 0, 15, 15, 0 }) &&
        f.Left == -1 && f.Top == -2 && f.Advance == 4, "synthetic odd rows/nibbles/signed bearings");
    Check(font.TryGetGlyph('R', out var r) && ReferenceEquals(f, r), "synthetic alias");
    Check(font.TryGetGlyph(0xffff, out var last) && last.Coverage.Span.SequenceEqual(new byte[] { 1, 14 }), "FFFF is mapped");
    Check(font.TryGetGlyph(' ', out var space) && space.Coverage.IsEmpty && space.Advance == 3, "empty space advance");
    Check(!font.TryGetGlyph(0, out _) && !font.TryGetGlyph('G', out _) && !font.TryGetGlyph(0xfffe, out _), "range gaps");
    CheckLookup(font, "synthetic");

    var canvas = new byte[6 * 5];
    Blit(f, canvas, 6, 5, 2 + f.Left, 3 + f.Top);
    var placed = new byte[30];
    placed[7] = placed[8] = placed[9] = placed[19] = placed[20] = 255; placed[13] = 136;
    Check(canvas.AsSpan().SequenceEqual(placed), "render placement, orientation and coverage scaling");
    var clipped = new byte[4]; Blit(f, clipped, 2, 2, -1, -1);
    Check(clipped.AsSpan().SequenceEqual(new byte[] { 0, 0, 255, 0 }), "negative-origin clipping");

    void Reject(byte[] data, string label)
    {
        try { _ = new BitmapFont(data); Check(false, "accepted malformed " + label); }
        catch (InvalidDataException) { }
    }
    for (int length = 0; length < good.Length; length++) Reject(good[..length], "truncation at " + length);
    void BadByte(int at, byte value, string label) { var copy = (byte[])good.Clone(); copy[at] = value; Reject(copy, label); }
    void BadWord(int at, uint value, string label)
    {
        var copy = (byte[])good.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(at), value); Reject(copy, label);
    }
    BadByte(0, 0, "magic"); BadByte(4, 2, "version"); BadByte(10, 0, "map magic");
    BadByte(14, 23, "map size"); BadByte(17, 2, "map tag"); BadByte(18, 0, "empty ranges");
    BadByte(22, 0x10, "unordered ranges"); BadByte(30, 0x20, "overlapping ranges");
    BadByte(38, 0x47, "index underflow"); BadByte(44, 0, "glyph magic");
    BadWord(40, 0, "mapped index outside table"); BadByte(51, 2, "glyph tag");
    BadWord(48, 0x0100002f, "glyph length"); BadWord(66, 1, "unsupported bit 0");
    BadWord(66, 2, "unknown flags"); BadWord(70, uint.MaxValue, "offset overflow");
    BadWord(70, 0, "offset into descriptors"); BadByte(74, 255, "bitmap past EOF");
    Reject(good.Concat(new byte[] { 0 }).ToArray(), "trailing bytes");
    var padding = (byte[])good.Clone(); padding[8] = 1; padding[9] = 2; padding[79] = 99; padding[98] = 15;
    Check(Digest(new BitmapFont(padding)) == Digest(font), "opaque padding and unused last nibble");
    Console.WriteLine("Synthetic pixel, lookup, placement and rejection checks completed.");
}
