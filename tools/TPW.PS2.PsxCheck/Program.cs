using TPW.PS2.Data;

// The optional PSX disc: identify it, read it, and hold the reader to answers known from the
// TPW-PSX project's reports. Usage:
//   dotnet run --project tools/TPW.PS2.PsxCheck -- <psx image> [--ps2=<ps2 image>] [--list]
// No PSX image (argument or TPW_PSX_DISC) is a SKIP, not a failure: PSX support is optional.
int bad = 0, ok = 0;
void Check(bool pass, string line) { Console.WriteLine((pass ? "  ok   " : "  FAIL ") + line); if (pass) ok++; else bad++; }

string psx = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Environment.GetEnvironmentVariable("TPW_PSX_DISC");
string ps2 = args.FirstOrDefault(a => a.StartsWith("--ps2="))?[6..] ?? Environment.GetEnvironmentVariable("TPW_PS2_DISC");
if (string.IsNullOrEmpty(psx))
{
    Console.WriteLine("SKIP no PSX disc given (argument or TPW_PSX_DISC); PSX support is optional");
    return 0;
}

var id = PsxDisc.Identify(psx);
Console.WriteLine($"{id.Status}: {id.Message}");
if (!id.Readable) { Console.WriteLine("FAIL: not readable as Theme Park World PSX"); return 1; }

// Controls: the checker must be able to say no.
if (!string.IsNullOrEmpty(ps2) && File.Exists(ps2))
{
    var other = PsxDisc.Identify(ps2);
    Check(other.Status == PsxDisc.Status.NotTpw,
        $"control: the PS2 disc is refused as TPW PSX ({other.Status}: {other.Message})");
}
Check(PsxDisc.Identify(Path.Combine(Path.GetTempPath(), "no-such-psx-disc.bin")).Status == PsxDisc.Status.NotFound,
    "control: a missing path is NotFound");
Check(PsxDisc.BootId("BOOT=cdrom:\\SLES_026.88;1\r\nTCB=4\r\n") == "SLES_026.88"
      && PsxDisc.BootId("BOOT2 = cdrom0:\\SLES_500.32;1\r\n") == null,
    "BOOT is read and a PS2 BOOT2 line is not taken for one");

using var disc = PsxDisc.Open(psx);
var folio = disc.Folio;
var header = new byte[8 + 8 * 2 + 4];
BitConverter.GetBytes(2).CopyTo(header, 0); BitConverter.GetBytes(0x18).CopyTo(header, 4);
Check(PsxFolio.Validate(header) != null, "control: a pack whose second word is not 0x17 is refused");

var text = disc.Text;
var attractions = disc.Attractions;
var maps = disc.Maps;
var byType = attractions.GroupBy(a => a.Type).ToDictionary(g => g.Key, g => g.Count());
Console.WriteLine($"FOLIO {folio.Entries.Count} entries; English text entry {text.Entry}, {text.Count} strings; "
    + $"{attractions.Count} attraction records ({string.Join(", ", byType.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}); "
    + $"{maps.Count} park maps ({string.Join(", ", maps.Select(m => $"{m.Folio}:{m.Width}x{m.Height}"))})");

if (id.Status == PsxDisc.Status.Ok && id.BootId == "SLES_026.88")
{
    Check(folio.Entries.Count == 422, "FOLIO has 422 entries (folio.md §1.4)");
    Check(text.Entry == 407 && text.Count == 1031, $"English is entry 407 with 1031 strings (found {text.Entry}, {text.Count})");
    Check(text[992] == "Crazy Ape", "text 992 is Crazy Ape, so the table picked is English");
    var want = new Dictionary<PsxAttractionType, int>
    {
        [PsxAttractionType.RollerCoaster] = 12, [PsxAttractionType.Feature] = 91, [PsxAttractionType.Ride] = 59,
        [PsxAttractionType.Shop] = 37, [PsxAttractionType.Sideshow] = 33, [PsxAttractionType.TrackRide] = 8,
        [PsxAttractionType.TourRide] = 4,
    };
    Check(attractions.Count == 244 && want.All(k => byType.GetValueOrDefault(k.Key) == k.Value) && !byType.ContainsKey(PsxAttractionType.TrackUpgrade),
        "244 attraction records: 12 coasters, 91 features, 59 rides, 37 shops, 33 sideshows, 8 track, 4 tour");
    PsxAttraction At(int f) => attractions.FirstOrDefault(a => a.Folio == f);
    Check(At(220) is { Type: PsxAttractionType.Ride, Name: "Crazy Ape" } && At(195) is { Type: PsxAttractionType.Feature, Name: "Loudspeaker" }
          && At(237) is { Type: PsxAttractionType.Shop, Name: "Fries" } && At(248) is { Type: PsxAttractionType.Sideshow, Name: "Idol Smash" },
        "known records by FOLIO entry: 220 Crazy Ape, 195 Loudspeaker, 237 Fries, 248 Idol Smash");
    Check(attractions.Any(a => a.Type == PsxAttractionType.Ride && a.Name == "Thrill Grill"), "Thrill Grill is a PSX ride");
    Check(attractions.Where(a => a.Type == PsxAttractionType.Feature).All(a => a.FootprintWidth >= 1 && a.FootprintDepth >= 1)
          && attractions.All(a => a.FootprintWidth <= 8 && a.FootprintDepth <= 8),
        "every footprint is between 1 and 8 cells a side");
    int[] mapEntries = { 34, 35, 116, 117, 203, 204, 355, 356 };
    Check(maps.Select(m => m.Folio).SequenceEqual(mapEntries) && maps.All(m => m.Width == 44 && m.Height == 74),
        "eight park maps, entries 34/35 116/117 203/204 355/356, all 44x74");
    int[] buildable = { 1997, 1922, 1821, 1845, 2010, 1837, 2153, 2158 };
    var counted = maps.Select(m => Enumerable.Range(0, m.Width * m.Height).Count(i => m.Buildable(i % m.Width, i / m.Width))).ToArray();
    Check(counted.SequenceEqual(buildable), $"buildable tiles per map {string.Join(",", counted)}");
    Check(maps.All(m => Enumerable.Range(0, m.Width * m.Height).Count(i => m.InBounds(i % m.Width, i / m.Width) && m.Type(i % m.Width, i / m.Width) == 2) == 38),
        "each map has 38 tiles of pre-laid path");
}
else Console.WriteLine("(not the PAL build that has been read: known-answer checks skipped)");

if (args.Contains("--list"))
    foreach (var a in attractions.OrderBy(a => a.Type).ThenBy(a => a.Name))
        Console.WriteLine($"  {a.Type,-13} {a.Folio,4}  {a.FootprintWidth}x{a.FootprintDepth}  {a.Name}");

Console.WriteLine(bad == 0 ? $"PASS PSX disc: {ok} checks" : $"FAIL: {bad}");
return bad == 0 ? 0 : 1;
