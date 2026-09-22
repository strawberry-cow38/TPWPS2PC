using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using TPW.PS2.Data;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: dotnet run --project tools/TPW.PS2.LastFiveAudit -c Release -- <disc.bin|--self-test>");
    return 1;
}
int failures = 0;
void Check(bool ok, string message)
{
    if (!ok) { failures++; Console.Error.WriteLine("FAIL " + message); }
}
void Reject(Action action, string message)
{
    try { action(); Check(false, "accepted " + message); }
    catch (InvalidDataException) { }
}
string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
try
{
    SelfTests();
    if (args[0] == "--self-test")
    {
        Console.WriteLine($"{failures} failures in synthetic boundary, channel, ordering and malformed-input checks.");
        return failures == 0 ? 0 : 2;
    }
    using var disc = new Disc(args[0]);
    var files = disc.Files();
    byte[] Iso(string path)
    {
        var e = files.Single(x => !x.IsDirectory && x.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        return disc.Read(e.Extent, e.Size);
    }
    var wad = new WadArchive(Iso("/DATA/DATA.WAD"));
    byte[] Wad(string path) => wad.Read(wad.Entries.Single(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase)));
    // Pins are independent Python byte walks + the traced consumer, not regenerated on audit.
    Check(Hash(Iso("/SLES_500.32")) == "231771d7f39cc29e7573ce62f437af15d97737a64c1d1c49df8404ead0b7577a",
        "consumer ELF is not the reviewed executable");
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var sjis = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    var fonts = new[] { new BitmapFont(Wad("/Fonts/Jap/Large.bff")), new BitmapFont(Wad("/Fonts/Jap/Small.bff")) };
    var anchors = new[] {
        (Code: (ushort)0x82a0, Unicode: "あ", Index: 118, Eur: 335, Usa: 349,
            Large: "26fdcfb21e9494ffc2eeca85752b769557d94e10dbf4597917e6e7fb2300fb09",
            Small: "3584e91af2ffbd10ff2992d450c6452a9c99f7731da98328ec77dbf0fa33cc4e"),
        (Code: (ushort)0x93fa, Unicode: "日", Index: 589, Eur: 611, Usa: 645,
            Large: "ee29093fdbe4c3ec743d01aa5cee6a7f5021a935e2923597d40798c15ca9c417",
            Small: "a8e15a6928ddc66a57cba18fd5f5ddae3de85e5cd08860ce38f20b4a21adfad6"),
        (Code: (ushort)0x967b, Unicode: "本", Index: 651, Eur: 219, Usa: 226,
            Large: "920edab4397bb4c716c3f5adf946aeca80bb27f070ac1a051581385d9e32a881",
            Small: "9d93da67621084cb59b8200e2f34156b525860b1208fe2e132f6d32ace7a7c31")
    };
    foreach (string locale in new[] { "eur", "jap", "usa" })
    {
        string path = $"/Text/translations/{locale}/kanji.table";
        byte[] raw = Wad(path);
        var table = new KanjiTable(raw);
        string expected = locale == "usa" ? "c2d40c3ebc36621e089f4b182afe01f014210f33a9abbead0c0e356ef7492fd3" :
            "2630322281ed528fffcd35b5ffb6bad12f7d91fcc02ebe1105c6ff02f2fe8aee";
        string digest = KanjiDigest(table);
        Check(digest == expected, path + " code/ordinal digest " + digest);
        var codes = Enumerable.Range(0, 65536).Where(c => table.TryGetImageOrdinal((ushort)c, out _))
            .OrderBy(c => c & 255).ThenBy(c => c >> 8).ToArray();
        var list = Encoding.Latin1.GetString(Wad($"/Text/translations/{locale}/kanji.lbmlist"))
            .Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        Check(list.Length == table.ImageCount, path + " export-list extent");
        for (int i = 0; i < codes.Length; i++)
        {
            table.TryGetImageOrdinal((ushort)codes[i], out int ordinal);
            Check(ordinal == i, $"{path}: code {codes[i]:x4} byte-sorted export ordinal");
            string prefix = locale;
            Check(ordinal < list.Length && list[ordinal] == $"report/text/translations/{prefix}\\{ordinal:D4}.lbm",
                $"{path}: code {codes[i]:x4} exact LBM filename");
        }
        foreach (var a in anchors)
        {
            Check(table.TryGetImageOrdinal(a.Code, out int ordinal) && ordinal == (locale == "usa" ? a.Usa : a.Eur),
                $"{path}: ordinal identity for {a.Code:x4}");
            Check(sjis.GetString(new[] { (byte)(a.Code >> 8), (byte)a.Code }) == a.Unicode,
                $"{a.Code:x4}: Unicode identity");
            for (int f = 0; f < fonts.Length; f++)
                Check(fonts[f].TryGetGlyph(a.Code, out var glyph) && glyph.Index == a.Index &&
                    Hash(glyph.Coverage.ToArray()) == (f == 0 ? a.Large : a.Small),
                    $"{path}: {a.Unicode} ordered glyph pixels in Japanese font {f}");
            Console.WriteLine($"{locale} SJIS {a.Code:x4} = U+{(int)a.Unicode[0]:X4} {a.Unicode}; export {ordinal:D4}.lbm; internal {ordinal + KanjiTable.RuntimeBase}; BFF descriptor {a.Index}");
        }
        Check(table.TryGetImageOrdinal(0x8144, out int hole) == (locale == "usa") && hole == (locale == "usa" ? 15 : -1),
            path + " USA-only U+FF0E FULLWIDTH FULL STOP entry");
        Check(!table.TryGetImageOrdinal(0x9863, out _) && !table.TryGetImageOrdinal(0x9872, out _), path + " omitted tail is bounded");
        // Every proper prefix loses at least one required ordinal or splits a word.
        for (int length = 0; length < raw.Length; length++)
        {
            int n = length;
            Reject(() => new KanjiTable(raw[..n]), path + " truncated at " + n);
        }
        byte[] swapped = (byte[])raw.Clone();
        (swapped[2], swapped[4]) = (swapped[4], swapped[2]); // 0 <-> 5, same counts and valid structure.
        Check(KanjiDigest(new KanjiTable(swapped)) != expected, path + " identity negative control");
        Console.WriteLine(path + " decoded SHA256 " + digest);
    }
    Check(Wad("/Text/translations/eur/kanji.table").SequenceEqual(Wad("/Text/translations/jap/kanji.table")), "EUR/JAP byte identity");
    foreach (string name in new[] { "GRC", "WTR" })
    {
        byte[] raw = Iso($"/AUDIO/RIDES/{name}.ENG");
        var engine = new RideEngine(raw);
        string expected = name == "GRC" ? "e5936792cb777ad8fe5fa1ecf5198b9010f20c85c980b5d21c25901e9b685de2" :
            "c83c8f22e94c3467bcad8affd0b8937a7bd28c6bda9818fbd8dc68a20ba92bc8";
        string digest = EngineDigest(engine);
        Check(digest == expected, name + " ordered fields and evaluated response digest " + digest);
        Check(engine.Layers.Select(l => l.SoundId).SequenceEqual(name == "GRC" ? new[] { 17, 1 } : new[] { 19, 18 }),
            name + " sound identities");
        Check(engine.ExportSlots[0].Name == name.ToLowerInvariant(), name + " export-name identity");
        var state = engine.Evaluate(500);
        Check(state.Select(s => s.CurvePercent).SequenceEqual(new byte[] { 50, 49 }) &&
            state.Select(s => s.Volume127).SequenceEqual(new[] { 63, 40 }) &&
            state.Select(s => s.Parameter).SequenceEqual(new[] { 12, 3 }), name + " midpoint amplitude and second parameter");
        for (int length = 0; length < raw.Length; length++)
        {
            int n = length;
            Reject(() => new RideEngine(raw[..n]), name + " truncated at " + n);
        }
        byte[] reversed = (byte[])raw.Clone(); Array.Reverse(reversed, 0x56, 32);
        Check(EngineDigest(new RideEngine(reversed)) != expected, name + " reversed-ramp negative control");
        foreach (var mutation in new (int Offset, uint Value)[] {
            (0, 0x0008ffff), (8, 0), (70, uint.MaxValue), (82, 2), (78, 0),
            (162, uint.MaxValue), (170, uint.MaxValue), (261, 1) })
        {
            byte[] bad = (byte[])raw.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(mutation.Offset), mutation.Value);
            Reject(() => new RideEngine(bad), name + " invalid field at " + mutation.Offset);
        }
        byte[] invalidChannel = (byte[])raw.Clone(); invalidChannel[36] = 2;
        Reject(() => new RideEngine(invalidChannel), name + " channel outside two-slot storage");
        Reject(() => new RideEngine(raw.Concat(new byte[1]).ToArray()), name + " trailing byte");
        Console.WriteLine(name + " decoded/evaluated SHA256 " + digest);
    }
    Console.WriteLine($"{failures} failures. Ordered mappings, Unicode/glyph identities, layer fields, curves, evaluation and malformed inputs checked.");
    Console.WriteLine("Kanji runtime loading and downstream image IDs remain unestablished; ENG input/secondary-parameter units remain unknown.");
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
return failures == 0 ? 0 : 2;

string KanjiDigest(KanjiTable table)
{
    using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
    w.Write(table.ImageCount);
    for (int code = 0; code <= ushort.MaxValue; code++)
    {
        table.TryGetImageOrdinal((ushort)code, out int id);
        w.Write((ushort)code); w.Write((short)id);
    }
    return Hash(stream.ToArray());
}

string EngineDigest(RideEngine e)
{
    using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
    w.Write(e.HeaderHigh); w.Write(e.Layers.Count);
    foreach (var l in e.Layers)
    {
        foreach (int x in new[] { l.Start, l.End, l.VolumeStart, l.VolumeEnd, l.ParameterStart, l.ParameterEnd, l.SoundId, l.StoredBankId }) w.Write(x);
        w.Write(l.Channel);
    }
    w.Write(e.Curves.Count);
    foreach (var c in e.Curves) { w.Write(c.Start); w.Write(c.End); w.Write(c.LayerIndex); w.Write(c.Samples.Span); }
    w.Write(e.ExportSlots.Count);
    foreach (var s in e.ExportSlots)
    {
        w.Write(s.Present);
        if (s.Present == 0) continue;
        foreach (string value in new[] { s.Directory, s.Name }) { var b = Encoding.Latin1.GetBytes(value); w.Write(b.Length); w.Write(b); }
    }
    w.Write(e.AdditionalParameterCount);
    for (int input = -1; input <= 1001; input++)
    {
        w.Write(input); w.Write(e.EvaluateCurvePercent(input));
        var states = e.Evaluate(input); w.Write(states.Count);
        foreach (var s in states)
        {
            w.Write(s.LayerIndex); w.Write(s.Channel); w.Write(s.SoundId); w.Write(s.VolumePercent);
            w.Write(s.Parameter); w.Write(s.CurvePercent); w.Write(s.Volume127);
        }
    }
    return Hash(stream.ToArray());
}

void SelfTests()
{
    var limits = new (ushort Code, int Slot)[] {
        (0x813f, -1), (0x8140, 0), (0x84be, 0x37e), (0x84bf, -1), (0x853f, -1),
        (0x8540, 0x37f), (0x8796, 0x5d5), (0x8797, -1), (0x889e, -1),
        (0x889f, 0x5d6), (0x9872, 0x15a9), (0x9873, -1), (0xffff, -1)
    };
    foreach (var (code, slot) in limits)
    {
        Check(KanjiTable.SlotForCode(code) == slot, $"range boundary {code:x4}");
        if (slot >= 0) Check(KanjiTable.CodeForSlot(slot) == code, $"inverse boundary {slot:x}");
    }
    byte[] tiny = { 2, 0, 1, 0, 255, 255, 0, 0 };
    var t = new KanjiTable(tiny);
    Check(t.TryGetImageOrdinal(0x8140, out int id) && id == 1 &&
        !t.TryGetImageOrdinal(0x8141, out _) && t.TryGetImageOrdinal(0x8142, out int last) && last == 0,
        "synthetic non-monotonic image identities");
    foreach (byte[] bad in new[] { new byte[] { 0, 0, 255, 255 }, new byte[] { 1, 0, 254, 255 },
        new byte[] { 1, 0, 1, 0 }, new byte[] { 2, 0, 0, 0, 0, 0 }, new byte[] { 2, 0, 0, 0 } })
        Reject(() => new KanjiTable(bad), "invalid kanji ordinal/count");
    using var stream = new MemoryStream();
    using (var w = new BinaryWriter(stream, Encoding.UTF8, true))
    {
        w.Write((ushort)2); w.Write((ushort)8);
        // Deliberately swap channel numbers; curves refer to layer indices, not channels.
        foreach (byte channel in new byte[] { 1, 0 })
        {
            foreach (int value in new[] { 10, 42, 100, 100, -6, 12, 7, 1 }) w.Write(value);
            w.Write(channel);
        }
        w.Write(3);
        for (int curve = 0; curve < 3; curve++)
        {
            w.Write(10); w.Write(42); w.Write(curve == 1 ? 1 : 0);
            for (int sample = 0; sample < 32; sample++) w.Write((byte)(curve == 2 ? 99 : sample + 10 * curve));
        }
        w.Write(0); w.Write(0);
    }
    var e = new RideEngine(stream.ToArray());
    Check(e.EvaluateCurvePercent(9).SequenceEqual(new byte[] { 100, 100 }), "outside interval uses default 100");
    Check(e.EvaluateCurvePercent(10).SequenceEqual(new byte[] { 10, 0 }), "start, curve reference and channel indirection");
    Check(e.EvaluateCurvePercent(11).SequenceEqual(new byte[] { 11, 1 }), "sample changes without interpolation");
    Check(e.EvaluateCurvePercent(42).SequenceEqual(new byte[] { 41, 31 }), "inclusive end clamps to sample 31; only first two matching curves");
    Check(e.EvaluateCurvePercent(43).SequenceEqual(new byte[] { 100, 100 }) && e.Evaluate(43).Count == 0, "outside layer extent");
    Check(e.Evaluate(26).Select(s => s.Parameter).SequenceEqual(new[] { 3, 3 }), "signed second parameter interpolation");
}
