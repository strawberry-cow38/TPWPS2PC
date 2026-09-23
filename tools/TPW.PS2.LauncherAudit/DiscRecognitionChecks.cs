using System.Buffers.Binary;
using System.Text;
using TPW.PS2.Launcher;

/// <summary>Minimal synthetic reader-layout fixtures, no proprietary disc payload.
/// These exercise the launcher's path/entry predicate, not complete ISO validation.</summary>
static class DiscRecognitionChecks
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

    static void Fixture(string path, string folder, string filename, bool directory = false)
    {
        const int sector = 2352, offset = 24, userSize = 2048;
        byte[] image = new byte[24 * sector];
        Record("\0", 20, userSize, true).CopyTo(image, 16 * sector + offset + 156);
        if (folder == null) Record(filename, 22, 0, directory).CopyTo(image, 20 * sector + offset);
        else
        {
            Record(folder, 21, userSize, true).CopyTo(image, 20 * sector + offset);
            Record(filename, 22, 0, directory).CopyTo(image, 21 * sector + offset);
        }
        File.WriteAllBytes(path, image);
    }

    public static void Run(Action<bool, string> check, string ownedDisc = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "tpw-disc-locator-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string fixture = Path.Combine(root, "fixture.bin");
            foreach (var (folder, filename, directory, accept, label) in new[]
            {
                ("DATA", "JUNGLE.WAD;1", false, true, "exact expected file path"),
                ("data", "jungle.wad;1", false, true, "case-insensitive expected file path"),
                ("DATA", "NOTJUNGLE.WAD;1", false, false, "misleading suffix is not the expected file"),
                ("OTHER", "JUNGLE.WAD;1", false, false, "same filename in the wrong folder is rejected"),
                ((string)null, "JUNGLE.WAD;1", false, false, "same filename at disc root is rejected"),
                ("DATA", "JUNGLE.WAD;1", true, false, "a directory named like the archive is rejected"),
            })
            {
                Fixture(fixture, folder, filename, directory);
                check(DiscLocator.Identify(fixture).CanLaunch == accept, "disc recognition: " + label);
            }
            check(DiscLocator.Identify(null).Status == DiscStatus.None, "disc recognition: no selection has a distinct result");
            check(DiscLocator.Identify(Path.Combine(root, "missing.bin")).Status == DiscStatus.NotFound,
                  "disc recognition: nonexistent path is not a readable disc");
            File.WriteAllBytes(fixture, new byte[16]);
            check(DiscLocator.Identify(fixture).Status == DiscStatus.NotADisc,
                  "disc recognition: truncated input becomes an informative failure, not an exception");
            string empty = Path.Combine(root, "empty"); Directory.CreateDirectory(empty);
            check(DiscLocator.Identify(empty).Status == DiscStatus.NotFound, "disc recognition: empty folder is not a disc");
        }
        finally { Directory.Delete(root, recursive: true); }
        if (ownedDisc != null)
            check(DiscLocator.Identify(ownedDisc).CanLaunch, "disc recognition: user's real disc identified in place");
    }
}
