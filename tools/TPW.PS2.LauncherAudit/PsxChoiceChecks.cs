using System.Buffers.Binary;
using System.Text;
using TPW.PS2.Data;
using TPW.PS2.Launcher;

/// <summary>The optional PSX disc, on synthetic images only: no PSX payload, no real disc needed.
/// A fixture is a minimal ISO9660 volume with SYSTEM.CNF, a FOLIO.GAZ that holds one English-looking
/// string table, and TPW.BIN. It is written as a 2048-byte ISO, a raw 2352+24 image, and a cue
/// pointing at the raw one. It is not a known build, so a readable one reports UnknownBuild.</summary>
static class PsxChoiceChecks
{
    static byte[] Record(string name, int extent, int size, bool directory)
    {
        byte[] text = Encoding.ASCII.GetBytes(name);
        int length = 33 + text.Length; if ((length & 1) != 0) length++;
        byte[] record = new byte[length]; record[0] = (byte)length;
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(2), extent);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(10), size);
        record[25] = directory ? (byte)2 : (byte)0; record[32] = (byte)text.Length;
        text.CopyTo(record, 33); return record;
    }

    /// <summary>`u32 n=1; u32 0x17; {offset, size}` and one string table with 120 strings, two of them
    /// the English markers PsxText looks for.</summary>
    static byte[] Folio(int marker = 0x17)
    {
        var strings = Enumerable.Range(0, 120).Select(i => i switch { 3 => "Build", 4 => "Pick up the litter", _ => $"s{i}" }).ToArray();
        var table = new List<byte>();
        table.AddRange(BitConverter.GetBytes(strings.Length));
        int at = 4 + 4 * strings.Length;
        var body = new List<byte>();
        foreach (var s in strings) { table.AddRange(BitConverter.GetBytes(at + body.Count)); body.AddRange(Encoding.Latin1.GetBytes(s)); body.Add(0); }
        table.AddRange(body);
        var pack = new List<byte>();
        pack.AddRange(BitConverter.GetBytes(1)); pack.AddRange(BitConverter.GetBytes(marker));
        pack.AddRange(BitConverter.GetBytes(16)); pack.AddRange(BitConverter.GetBytes(table.Count));
        pack.AddRange(table);
        return pack.ToArray();
    }

    static void Image(string path, int sectorSize, int userOffset, bool withFolio = true, int marker = 0x17, string boot = "SLES_026.88")
    {
        var files = new List<(string Name, byte[] Data)>
        {
            ("SYSTEM.CNF;1", Encoding.ASCII.GetBytes($"BOOT=cdrom:\\{boot};1\r\nTCB=4\r\n")),
            ("TPW.BIN;1", new byte[4096]),
        };
        if (withFolio) files.Add(("FOLIO.GAZ;1", Folio(marker)));
        int sector = 22;
        var placed = new List<(string Name, int Extent, byte[] Data)>();
        foreach (var f in files) { placed.Add((f.Name, sector, f.Data)); sector += (f.Data.Length + 2047) / 2048; }
        var user = new byte[(sector + 1) * 2048];
        var pvd = user.AsSpan(16 * 2048);
        pvd[0] = 1; Encoding.ASCII.GetBytes("CD001").CopyTo(pvd[1..]);
        Record("\0", 20, 2048, true).CopyTo(pvd[156..]);
        int p = 20 * 2048;
        foreach (var f in placed) { var r = Record(f.Name, f.Extent, f.Data.Length, false); r.CopyTo(user, p); p += r.Length; }
        foreach (var f in placed) f.Data.CopyTo(user, f.Extent * 2048);
        int sectors = user.Length / 2048;
        var image = new byte[sectors * sectorSize];
        for (int s = 0; s < sectors; s++) Array.Copy(user, s * 2048, image, s * sectorSize + userOffset, 2048);
        File.WriteAllBytes(path, image);
    }

    public static void Run(Action<bool, string> check, string ownedPsx = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "tpw-psx-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string iso = Path.Combine(root, "psx fixture [a].iso"), bin = Path.Combine(root, "psx fixture (b).bin");
            string cue = Path.Combine(root, "psx fixture.cue");
            Image(iso, 2048, 0);
            Image(bin, 2352, 24);
            File.WriteAllText(cue, $"FILE \"{Path.GetFileName(bin)}\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n");
            var a = PsxDisc.Identify(iso); var b = PsxDisc.Identify(bin); var c = PsxDisc.Identify(cue);
            check(a.Status == PsxDisc.Status.UnknownBuild && a.Layout == "2048" && a.BootId == "SLES_026.88",
                  $"psx disc: a 2048-byte ISO is read and, not being the known build, is UnknownBuild ({a.Status}, {a.Layout})");
            check(b.Status == PsxDisc.Status.UnknownBuild && b.Layout == "2352+24", $"psx disc: a raw 2352+24 image is read ({b.Status}, {b.Layout})");
            check(c.Status == PsxDisc.Status.UnknownBuild && c.Layout == "2352+24", "psx disc: a cue sheet is followed to its bin");

            string noFolio = Path.Combine(root, "no folio.iso"), badFolio = Path.Combine(root, "bad folio.iso"), ps2Boot = Path.Combine(root, "ps2.iso");
            Image(noFolio, 2048, 0, withFolio: false);
            Image(badFolio, 2048, 0, marker: 0x18);
            check(PsxDisc.Identify(noFolio).Status == PsxDisc.Status.NotTpw, "psx disc: no FOLIO.GAZ is not Theme Park World PSX");
            check(PsxDisc.Identify(badFolio).Status == PsxDisc.Status.NotTpw, "psx disc: a FOLIO.GAZ without the 0x17 marker is refused");
            File.WriteAllText(Path.Combine(root, "junk.iso"), "not a disc");
            check(PsxDisc.Identify(Path.Combine(root, "junk.iso")).Status == PsxDisc.Status.NotADisc, "psx disc: junk is NotADisc, not an exception");
            check(PsxDisc.Identify(Path.Combine(root, "missing.iso")).Status == PsxDisc.Status.NotFound, "psx disc: a missing path is NotFound");
            check(PsxDisc.Identify(null).Status == PsxDisc.Status.None, "psx disc: no selection is None");

            string memory = Path.Combine(root, "psx-disc.txt");
            var choice = new PsxDiscChoice(memory);
            check(choice.LaunchPath == null && choice.Load().Status == PsxDisc.Status.None, "psx choice: nothing chosen passes nothing");
            choice.Choose(Path.Combine(root, "missing.iso"));
            check(choice.LaunchPath == null && !File.Exists(memory), "psx choice: an unreadable choice is neither passed nor remembered");
            choice.Choose(badFolio);
            check(choice.LaunchPath == null && !File.Exists(memory), "psx choice: a refused image is neither passed nor remembered");
            choice.Choose(iso);
            check(choice.LaunchPath == iso && File.ReadAllText(memory) == iso && choice.Contents == "0 attractions, 0 park maps",
                  $"psx choice: a readable image is passed, remembered and described ({choice.Contents})");
            var again = new PsxDiscChoice(memory);
            check(again.Load().Readable && again.LaunchPath == iso, "psx choice: the remembered image is re-identified on the next run");
            again.Clear();
            check(again.LaunchPath == null && !File.Exists(memory), "psx choice: Clear forgets it");
        }
        finally { Directory.Delete(root, recursive: true); }
        if (ownedPsx != null)
        {
            var real = PsxDisc.Identify(ownedPsx);
            check(real.Status == PsxDisc.Status.Ok, $"psx disc: user's real PSX disc identified in place ({real.Message})");
        }
    }
}
