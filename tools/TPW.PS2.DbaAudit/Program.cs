using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TPW.PS2.Data;
using Db = TPW.PS2.Data.AssetResourceDatabase;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: dotnet run --project tools/TPW.PS2.DbaAudit -- <disc.bin> [--eur=DBA] [--usa=DBA] [--jap=DBA] [--elf=ELF] [--list]");
    return 2;
}
int failures = 0;
void Check(bool ok, string why)
{
    if (!ok) { failures++; Console.Error.WriteLine("FAIL " + why); }
}
void Reject(Action action, string why)
{
    try { action(); Check(false, "accepted " + why); }
    catch (InvalidDataException) { }
}
string[] Resource(string name)
{
    var assembly = Assembly.GetExecutingAssembly();
    using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + name)));
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith('#')).ToArray();
}
byte[] Override(string name, Func<byte[]> original)
{
    var option = args.Skip(1).SingleOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal));
    return option == null ? original() : File.ReadAllBytes(option[(name.Length + 1)..]);
}
string Projection(Db.Entry e, TextDatabase text)
{
    var a = e.ConnectionA; var b = e.ConnectionB;
    object[] common = { (ushort)e.Kind, e.RuntimeCategoryIndex, e.TextRow, e.Width, e.UnknownHeightByte,
        e.Depth, e.Unknown0B, a.X, a.Z, a.Direction, b.X, b.Z, b.Direction, e.Minigame, e.BaseExcitement, e.TypeDataLength };
    var body = new List<object>();
    if (e.HasRideTiers)
        for (int i = 0; i < 3; i++)
        {
            var t = e.Tier(i);
            body.AddRange(new object[] { t.UnknownFlags, t.MinSpeedDamage, t.MinCapacityDamage, t.WearRate,
                t.CapacityParameter, t.InitialCondition, t.MinSpeed, t.MaxSpeed, t.MinDuration,
                t.MaxDuration, t.ResearchGroup, t.ResearchWork, t.PurchaseCost });
        }
    else
    {
        var c = e.SimpleEconomy;
        body.AddRange(new object[] { c.PurchaseCost, c.ResearchWork, c.ResearchGroup });
        if (e.Shop is {} s)
            body.AddRange(new object[] { s.InitialPrice, s.BaseCostOfGoods, s.Product, s.Unknown31,
                s.HungerReduction, s.ThirstReduction, s.HappinessEffect, s.Unknown35, s.VomitIncrease, s.Unknown37 });
        if (e.Sideshow is {} g) body.AddRange(new object[] { g.InitialPrice, g.PrizeValue, g.WinPercentage });
        if (e.RawFeatureFlags is byte f) body.Add(f);
    }
    var cells = new List<string>();
    for (int z = 0; z < e.Depth; z++)
        for (int x = 0; x < e.Width; x++)
        {
            var c = e.Cell(x, z);
            cells.Add($"{c.TileId}:{c.RawFlags}:{c.QuarterTurns}:{(c.TerrainBit14 ? 1 : 0)}:{(c.TerrainBit15 ? 1 : 0)}:{(c.UsesDefaultTerrain ? 1 : 0)}");
        }
    return string.Join('\t', e.Key, e.Offset, text.Keys[e.TextRow], string.Join(',', common),
        string.Join(',', body), Convert.ToHexString(e.Extra.Span), string.Join(',', cells));
}
try
{
    foreach (string option in args.Skip(1))
        if (option != "--list" && !new[] { "--eur=", "--usa=", "--jap=", "--elf=" }.Any(option.StartsWith))
            throw new ArgumentException("Unknown option " + option);
    using var disc = new Disc(args[0]);
    byte[] DiscFile(string path)
    {
        var f = disc.Files().Single(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        return disc.Read(f.Extent, f.Size);
    }
    var wad = new WadArchive(DiscFile("/DATA/DATA.WAD"));
    byte[] FileInWad(string path) => wad.Read(wad.Entries.Single(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase)));
    var elf = new Elf(Override("--elf", () => DiscFile("/SLES_500.32")));
    foreach (var line in Resource("consumer-words.tsv"))
    {
        var p = line.Split('\t');
        uint address = uint.Parse(p[0], NumberStyles.HexNumber);
        uint expected = uint.Parse(p[1], NumberStyles.HexNumber);
        Check(elf.U32(address) == expected, $"ELF 0x{address:x}: {p[2]}");
    }
    foreach (var (address, value) in new[] {
        (0x366260u, "getMinSpeed = %d"), (0x366278u, "getMaxSpeed = %d"),
        (0x366290u, "getMinDuration = %d"), (0x3662a8u, "getMaxDuration = %d"),
        (0x3662c0u, "getMinSpeedDamage = %d"), (0x3662d8u, "getMinCapacityDamage = %d"),
        (0x3662f8u, "getWearRate = %d") })
        Check(elf.Bytes(address, value.Length + 1).SequenceEqual(Encoding.ASCII.GetBytes(value + "\0")), "ELF diagnostic " + value);

    var goldens = Resource("retail-identities.tsv").ToDictionary(l => uint.Parse(l.Split('\t')[0]));
    var databases = new Dictionary<string, Db>();
    var raw = new Dictionary<string, byte[]>();
    var texts = new Dictionary<string, TextDatabase>();
    foreach (string region in new[] { "eur", "usa", "jap" })
    {
        string file = region switch { "eur" => "/arsdb.dba", "usa" => "/arsusdb.dba", _ => "/arsjapdb.dba" };
        var bytes = Override("--" + region, () => FileInWad(file));
        var db = new Db(bytes); databases.Add(region, db); raw.Add(region, bytes);
        var text = TextDatabase.Load(wad, region) ?? throw new InvalidDataException("Missing text region " + region);
        texts.Add(region, text);
        var lines = new List<string>();
        foreach (var e in db.Entries)
        {
            if (e.TextRow >= text.Keys.Length) throw new InvalidDataException($"{region} {e.Key}: invalid text row");
            Check(text.Keys[e.TextRow].StartsWith("STR_GRAPHICS_"), $"{region} {e.Key}: non-asset text identity");
            string line = Projection(e, text); lines.Add(line);
            if (goldens.TryGetValue(e.Key, out var golden))
            {
                // Four independently named USA ice-cream records have different gameplay effects.
                if (region == "usa" && e.Key is 245 or 151 or 69 or 391)
                {
                    var fields = golden.Split('\t');
                    var body = fields[4].Split(','); body[7] = "15"; body[11] = "15";
                    fields[4] = string.Join(',', body);
                    byte[] extra = Convert.FromHexString(fields[5]); extra[6] = 15; extra[10] = 15;
                    fields[5] = Convert.ToHexString(extra); golden = string.Join('\t', fields);
                }
                Check(line == golden, $"{region} {e.Key}: named payload {text.Keys[e.TextRow]} differs from independent golden");
            }
            if (args.Contains("--list")) Console.WriteLine(region + "\t" + line);
        }
        foreach (uint key in goldens.Keys) Check(db.Find(key) != null, $"{region}: missing named asset {key}");
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join('\n', lines) + "\n"))).ToLowerInvariant();
        string expectedDigest = region == "usa" ? "6d87e518ae3d17e4d04eff02fa024152cdc64eac81bf1381ecce84f4aa5914bf" :
            "b0b6fb641f96ab8af4ddfdc65a67d3e201d2602df0563e96b3bd72d086f02d51";
        Check(digest == expectedDigest, $"{region}: decoded identity/field/footprint projection SHA256 {digest}");
        Check(db.DirectoryGap.Span.SequenceEqual(new byte[12]), region + " retail directory gap");
        var duplicate = db.Entries.Where(e => e.Key == uint.MaxValue).ToArray();
        Check(duplicate.Select(e => text.Keys[e.TextRow]).SequenceEqual(new[] {
            "STR_GRAPHICS_SPACE_RIDES_ZOB_ZOB", "STR_GRAPHICS_SPACE_UPGRADES_ZOB2_ZOB2" }) &&
            ReferenceEquals(db.Find(uint.MaxValue), duplicate[0]), region + " duplicate-key identities / first match");
        var bounce = db.Find(215);
        Check(text.Text("eng", (int)bounce.TextRow) == "Belly Bounce", region + " Belly Bounce English identity");
        Check(bounce.ConnectionA.IsPresent && bounce.ConnectionB.IsPresent && !db.Find(178).ConnectionA.IsPresent,
            region + " connection signed sentinels");
        Check(db.Find(220).Minigame == 7 && db.Find(250).Minigame == 5 && db.Find(606).Minigame == 10,
            region + " Dino Karts / Jungle Puzzle / Pong minigame identities");
        Console.WriteLine($"DBA {region}: checked named assets, all decoded projections, unknown bytes, and footprint cells");
    }
    Check(raw["eur"].AsSpan().SequenceEqual(raw["jap"]), "EUR/JAP must be identical");
    var expectedUs = (byte[])raw["eur"].Clone();
    foreach (uint key in new uint[] { 245, 151, 69, 391 })
    {
        int offset = databases["eur"].Find(key).Offset;
        expectedUs[offset + 0x32] = 15; expectedUs[offset + 0x36] = 15;
        var eu = databases["eur"].Find(key).Shop; var us = databases["usa"].Find(key).Shop;
        Check(eu.HungerReduction == 25 && eu.VomitIncrease == 10 && us.HungerReduction == 15 && us.VomitIncrease == 15,
            $"ice cream {key}: hunger/vomit regional effects");
    }
    Check(expectedUs.AsSpan().SequenceEqual(raw["usa"]), "USA differs beyond the four named ice-cream effects");

    // Boundaries, signed sentinels, and preservation controls use real records, not dummy counts.
    var original = raw["eur"]; var eur = databases["eur"]; int bounceOffset = eur.Find(215).Offset;
    byte[] Change(int offset, params byte[] value)
    {
        var copy = (byte[])original.Clone(); value.CopyTo(copy, offset); return copy;
    }
    Reject(() => new Db(original[..^1]), "truncated last payload");
    Reject(() => new Db(original.Concat(new byte[] { 0 }).ToArray()), "unconsumed byte after payloads");
    Reject(() => new Db(Change(bounceOffset, 9, 0)), "unknown payload kind");
    Reject(() => new Db(Change(bounceOffset + 28, 255, 255, 255, 255)), "overflowing type-data offset");
    Reject(() => new Db(Change(bounceOffset + 8, 4)), "same directory, wrong footprint width");
    Reject(() => new Db(Change(20, 0, 0, 0, 0)), "overlapping second directory span");
    var index = new Db(Change(bounceOffset + 2, 0x34, 0x12)).Find(215);
    Check(index.Kind == Db.AssetKind.Ride && index.RuntimeCategoryIndex == 0x1234, "runtime index must not change kind");
    var noConnection = new Db(Change(bounceOffset + 12, 0xfe, 0xff)).Find(215);
    Check(noConnection.ConnectionA.X == -2 && !noConnection.ConnectionA.IsPresent, "any negative coordinate is absent");
    int grid = bounceOffset + eur.Find(215).FootprintOffset;
    var defaultCell = new Db(Change(grid, 0xfe, 0xff, 0x0d, 0x80)).Find(215).Cell(0, 0);
    Check(defaultCell.TileId == -2 && defaultCell.UsesDefaultTerrain && defaultCell.RawFlags == 0x800d,
        "negative tile and unknown high flags must survive");
    var explicitCell = new Db(Change(grid, 7, 0, 0x0d, 0)).Find(215).Cell(0, 0);
    Check(!explicitCell.UsesDefaultTerrain && explicitCell.QuarterTurns == 1 && explicitCell.TerrainBit14 && explicitCell.TerrainBit15,
        "explicit tile flags (retail uses only the negative branch)");
    var padding = new Db(Change(bounceOffset + 0xbd, 0xab, 0xcd, 0xef)).Find(215);
    Check(padding.Extra.Span.SequenceEqual(new byte[] { 1, 0xab, 0xcd, 0xef }) && padding.Tier(0) == eur.Find(215).Tier(0),
        "unknown tail preserved without contaminating tiers");
    // A legal value mutation must defeat the identity golden despite every extent being unchanged.
    var altered = new Db(Change(bounceOffset + 0x24, 5)).Find(215);
    Check(Projection(altered, texts["eur"]) != goldens[215], "valid-but-wrong constant MinSpeedDamage escaped the golden");
    var wrongRow = new Db(Change(bounceOffset + 4, 0x4b, 0, 0, 0)).Find(215);
    Check(Projection(wrongRow, texts["eur"]) != goldens[215], "valid-but-wrong asset name escaped the golden");
    Console.WriteLine(failures == 0 ? "PASS DBA payload audit (remaining semantic gaps: findings/dba.md)" : $"FAIL {failures} DBA checks");
}
catch (Exception e)
{
    Console.Error.WriteLine("FAIL " + e.Message); return 1;
}
return failures == 0 ? 0 : 1;

// Audit-only ELF32 PT_LOAD mapping, so signatures are independent of file placement.
sealed class Elf
{
    readonly byte[] data;
    readonly List<(uint Address, uint Offset, uint Size)> segments = new();
    public Elf(byte[] bytes)
    {
        data = bytes;
        if (data.Length < 52 || !data.AsSpan(0, 6).SequenceEqual(new byte[] { 127, 69, 76, 70, 1, 1 }))
            throw new InvalidDataException("Expected little-endian ELF32");
        uint ph = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(28));
        ushort stride = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(42));
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(44));
        if (stride < 32 || (ulong)ph + (ulong)stride * count > (ulong)data.Length)
            throw new InvalidDataException("Invalid ELF program headers");
        for (int i = 0; i < count; i++)
        {
            var h = data.AsSpan((int)ph + i * stride, 32);
            if (BinaryPrimitives.ReadUInt32LittleEndian(h) != 1) continue;
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(h[4..]);
            uint address = BinaryPrimitives.ReadUInt32LittleEndian(h[8..]);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(h[16..]);
            if ((ulong)offset + size > (ulong)data.Length) throw new InvalidDataException("Truncated ELF segment");
            segments.Add((address, offset, size));
        }
    }
    public ReadOnlySpan<byte> Bytes(uint address, int length)
    {
        foreach (var s in segments)
            if (address >= s.Address && (ulong)address + (uint)length <= (ulong)s.Address + s.Size)
                return data.AsSpan(checked((int)((ulong)s.Offset + address - s.Address)), length);
        throw new InvalidDataException($"Unmapped ELF address {address:x}");
    }
    public uint U32(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(address, 4));
}
