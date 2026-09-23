using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Db = TPW.PS2.Data.AssetResourceDatabase;

/// <summary>Preservation evidence only. Offsets/masks come from findings/dba.md;
/// no new gameplay meaning or retail constant is imposed on unresolved storage.</summary>
static class UnknownFieldCoverage
{
    readonly record struct Field(string Name, int Offset, int Length, byte Mask = 0xff);
    readonly record struct Probe(int Ordinal, string Field, int Offset, byte Mask);

    static IEnumerable<Field> Fields(Db.Entry e)
    {
        yield return new("common-09", 0x09, 1);
        yield return new("common-0b", 0x0b, 1);
        if (e.HasRideTiers)
            for (int tier = 0; tier < 3; tier++)
                yield return new($"tier-{tier}-raw-flags", 0x20 + tier * 0x34, 4);
        switch (e.Kind)
        {
            case Db.AssetKind.Feature:
                yield return new("feature-2c-2d", 0x2c, 2);
                yield return new("feature-unresolved-flag-bits", 0x2e, 1, 0xf0);
                yield return new("feature-2f", 0x2f, 1);
                break;
            case Db.AssetKind.Ride:
                yield return new("ride-control-and-tail", 0xbc, 4);
                break;
            case Db.AssetKind.Shop:
                yield return new("shop-31", 0x31, 1);
                yield return new("shop-35", 0x35, 1);
                yield return new("shop-37", 0x37, 1);
                break;
            case Db.AssetKind.Sideshow:
                yield return new("sideshow-31-33", 0x31, 3);
                break;
            case Db.AssetKind.Coaster:
                yield return new("coaster-c4-d2", 0xc4, 15);
                // Keep the established connection directions (bits 28..31) intact.
                yield return new("coaster-unresolved-packed-bits", 0xd3, 1, 0x0f);
                break;
            case Db.AssetKind.TrackRide:
                yield return new("track-bc-107", 0xbc, 0x4c);
                break;
            case Db.AssetKind.TourRide:
                yield return new("tour-bc-d7", 0xbc, 0x1c);
                break;
            case Db.AssetKind.TrackUpgrade:
                yield return new("upgrade-2c-2f", 0x2c, 4);
                break;
            default: throw new InvalidDataException("Uncovered DBA kind " + e.Kind);
        }
        for (int cell = 0; cell < e.Width * e.Depth; cell++)
        {
            yield return new($"footprint-{cell}-high-low-byte", e.FootprintOffset + 4 * cell + 2, 1, 0xf0);
            yield return new($"footprint-{cell}-high-byte", e.FootprintOffset + 4 * cell + 3, 1);
        }
    }

    static List<Probe> Probes(Db db)
    {
        var probes = new List<Probe>();
        var kinds = new HashSet<Db.AssetKind>();
        for (int ordinal = 0; ordinal < db.Entries.Count; ordinal++)
        {
            var e = db.Entries[ordinal];
            kinds.Add(e.Kind);
            var masks = new Dictionary<int, byte>();
            foreach (var f in Fields(e))
            {
                if (f.Offset < 0 || f.Length < 1 || f.Offset > e.Payload.Length - f.Length || f.Mask == 0)
                    throw new InvalidDataException($"Unknown-field descriptor out of bounds: {e.Kind} {f.Name}");
                for (int offset = f.Offset; offset < f.Offset + f.Length; offset++)
                {
                    if ((masks.GetValueOrDefault(offset) & f.Mask) != 0)
                        throw new InvalidDataException($"Overlapping unknown masks: {e.Kind} +{offset:x}");
                    masks[offset] = (byte)(masks.GetValueOrDefault(offset) | f.Mask);
                    for (int bit = 0; bit < 8; bit++)
                        if ((f.Mask & (1 << bit)) != 0) probes.Add(new(ordinal, f.Name, offset, (byte)(1 << bit)));
                }
            }
        }
        if (!kinds.SetEquals(Enum.GetValues<Db.AssetKind>()))
            throw new InvalidDataException("Unknown-field coverage must exercise all eight retail DBA kinds");
        if (probes.Count == 0) throw new InvalidDataException("Unknown-field preservation coverage was vacuous");
        return probes;
    }

    // The intentionally stable projection excludes only the declared unresolved
    // values. A mutation must not leak into neighboring, already-decoded fields.
    static string KnownProjection(Db.Entry e)
    {
        var text = new StringBuilder();
        text.AppendJoin('|', e.Key, e.Offset, e.RawKindWord, e.TextRow, e.Width, e.Depth,
                        e.ConnectionA, e.ConnectionB, e.Minigame, e.BaseExcitement, e.TypeDataLength);
        if (e.HasRideTiers)
            for (int i = 0; i < 3; i++)
            {
                var t = e.Tier(i);
                text.Append('|').AppendJoin(',', t.MinSpeedDamage, t.MinCapacityDamage, t.WearRate,
                    t.CapacityParameter, t.InitialCondition, t.MinSpeed, t.MaxSpeed, t.MinDuration,
                    t.MaxDuration, t.ResearchGroup, t.ResearchWork, t.PurchaseCost);
            }
        else text.Append('|').Append(e.SimpleEconomy);
        if (e.Shop is {} s)
            text.Append('|').AppendJoin(',', s.InitialPrice, s.BaseCostOfGoods, s.Product,
                s.HungerReduction, s.ThirstReduction, s.HappinessEffect, s.VomitIncrease);
        if (e.Sideshow is {} g) text.Append('|').AppendJoin(',', g.InitialPrice, g.PrizeValue, g.WinPercentage);
        if (e.RawFeatureFlags is byte flags) text.Append('|').Append(flags & 0x0f);
        if (e.Kind == Db.AssetKind.Coaster)
            text.Append('|').Append(Convert.ToHexString(e.Payload.Span.Slice(0xbc, 8)))
                .Append('|').Append(e.Payload.Span[0xd3] & 0xf0);
        for (int z = 0; z < e.Depth; z++)
            for (int x = 0; x < e.Width; x++)
            {
                var c = e.Cell(x, z);
                text.Append('|').AppendJoin(',', c.TileId, c.QuarterTurns, c.TerrainBit14,
                                            c.TerrainBit15, c.UsesDefaultTerrain);
            }
        return text.ToString();
    }

    static byte ReadExposedByte(Db.Entry e, int offset)
    {
        if (offset == 9) return e.UnknownHeightByte;
        if (offset == 11) return e.Unknown0B;
        if (e.HasRideTiers && offset >= 0x20 && offset < 0xbc)
        {
            int relative = offset - 0x20, tier = relative / 0x34, fieldByte = relative % 0x34;
            if (fieldByte >= 4) throw new InvalidDataException("Descriptor touches a decoded tier field");
            return (byte)(e.Tier(tier).UnknownFlags >> (8 * fieldByte));
        }
        if (offset >= e.FootprintOffset)
        {
            int relative = offset - e.FootprintOffset, cell = relative / 4, fieldByte = relative % 4;
            if (fieldByte < 2) throw new InvalidDataException("Descriptor touches a footprint tile selector");
            return (byte)(e.Cell(cell % e.Width, cell / e.Width).RawFlags >> (8 * (fieldByte - 2)));
        }
        if (e.Shop is {} s)
            return offset switch { 0x31 => s.Unknown31, 0x35 => s.Unknown35, 0x37 => s.Unknown37,
                                   _ => throw new InvalidDataException("Descriptor touches a decoded shop field") };
        if (e.Sideshow is {} g) return g.Unknown31To33.Span[offset - 0x31];
        if (e.Kind == Db.AssetKind.Feature && offset == 0x2e) return e.RawFeatureFlags.Value;
        return e.Extra.Span[offset - (e.HasRideTiers ? 0xbc : 0x2c)];
    }

    // Independent delta oracle for Program.cs's documented seven-column canonical
    // format. Update only the raw numeric/hex positions that the selected bit owns.
    // Merely observing "some string changed" would miss a broken typed projection
    // whenever its redundant Extra hex still changed correctly.
    static string ExpectedProjection(string before, Db.Entry e, byte[] expected, int offset)
    {
        var columns = before.Split('\t');
        if (columns.Length != 7) throw new InvalidDataException("Canonical DBA projection shape changed");
        var common = columns[3].Split(','); var body = columns[4].Split(',');
        if (offset == 9) common[4] = expected[offset].ToString(CultureInfo.InvariantCulture);
        else if (offset == 11) common[6] = expected[offset].ToString(CultureInfo.InvariantCulture);
        else if (e.HasRideTiers && offset < 0xbc)
        {
            int tier = (offset - 0x20) / 0x34;
            body[tier * 13] = BinaryPrimitives.ReadUInt32LittleEndian(expected.AsSpan(0x20 + tier * 0x34))
                .ToString(CultureInfo.InvariantCulture);
        }
        else if (offset >= e.FootprintOffset)
        {
            int cell = (offset - e.FootprintOffset) / 4;
            var cells = columns[6].Split(','); var fields = cells[cell].Split(':');
            fields[1] = BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(e.FootprintOffset + cell * 4 + 2))
                .ToString(CultureInfo.InvariantCulture);
            cells[cell] = string.Join(':', fields); columns[6] = string.Join(',', cells);
        }
        else if (e.Kind == Db.AssetKind.Shop)
        {
            int field = offset switch { 0x31 => 6, 0x35 => 10, 0x37 => 12,
                                         _ => throw new InvalidDataException("Unexpected shop projection probe") };
            body[field] = expected[offset].ToString(CultureInfo.InvariantCulture);
        }
        else if (e.Kind == Db.AssetKind.Feature && offset == 0x2e)
            body[3] = expected[offset].ToString(CultureInfo.InvariantCulture);
        int extraStart = e.HasRideTiers ? 0xbc : 0x2c;
        columns[3] = string.Join(',', common); columns[4] = string.Join(',', body);
        columns[5] = Convert.ToHexString(expected.AsSpan(extraStart, e.FootprintOffset - extraStart));
        return string.Join('\t', columns);
    }

    static string Validate(Db original, Db changed, Probe p, Func<Db.Entry, string> projection,
                           string fullBefore, string knownBefore, int[] unknownOffsets,
                           Func<Db.Entry, int, byte> readUnknown = null)
    {
        if (changed.Entries.Count != original.Entries.Count) return "entry count changed";
        if (!changed.DirectoryGap.Span.SequenceEqual(original.DirectoryGap.Span)) return "directory gap changed";
        for (int i = 0; i < original.Entries.Count; i++)
        {
            var before = original.Entries[i]; var after = changed.Entries[i];
            if (before.Key != after.Key || before.Offset != after.Offset || before.Payload.Length != after.Payload.Length)
                return $"directory identity/span changed at ordinal {i}";
            if (i != p.Ordinal && !before.Payload.Span.SequenceEqual(after.Payload.Span))
                return $"unrelated payload changed at ordinal {i}";
        }
        var old = original.Entries[p.Ordinal]; var entry = changed.Entries[p.Ordinal];
        var expected = old.Payload.ToArray(); expected[p.Offset] ^= p.Mask;
        if (!entry.Payload.Span.SequenceEqual(expected)) return "payload did not preserve exactly the selected bit mutation";
        int extraStart = entry.HasRideTiers ? 0xbc : 0x2c;
        if (!entry.Extra.Span.SequenceEqual(expected.AsSpan(extraStart, entry.FootprintOffset - extraStart)))
            return "Extra view differs from expected raw bytes";
        readUnknown ??= ReadExposedByte;
        foreach (int offset in unknownOffsets)
            if (readUnknown(entry, offset) != expected[offset])
                return $"raw/unknown accessor differs at +0x{offset:x}";
        if (KnownProjection(entry) != knownBefore) return "unrelated decoded fields changed";
        if (projection(entry) != ExpectedProjection(fullBefore, old, expected, p.Offset))
            return "canonical projection differs from the independent raw-byte delta";
        return null;
    }

    public static void Run(string region, byte[] raw, Db original, Func<Db.Entry, string> projection,
                           Action<bool, string> check, bool exhaustive)
    {
        var probes = Probes(original);
        // Regression census for the existing retail corpus, not a semantic claim.
        // Removing a descriptor must not silently shrink the advertised coverage.
        check(probes.Count == 57664, region + " unresolved-mask probe census changed (expected 57664)");
        var offsets = original.Entries.Select(e => Fields(e)
            .SelectMany(f => Enumerable.Range(f.Offset, f.Length)).Distinct().ToArray()).ToArray();
        // Controls exercise the checker itself: silently normalizing a mutation,
        // changing another record, or omitting unknowns from the projection must fail.
        var control = probes[0];
        var baseEntry = original.Entries[control.Ordinal];
        string full = projection(baseEntry), known = KnownProjection(baseEntry);
        check(Validate(original, original, control, projection, full, known, offsets[control.Ordinal]) != null,
              region + " unknown coverage control: discarded mutation must fail");
        var sample = (byte[])raw.Clone(); sample[baseEntry.Offset + control.Offset] ^= control.Mask;
        var changed = new Db(sample);
        check(Validate(original, changed, control, _ => full, full, known, offsets[control.Ordinal]) != null,
              region + " unknown coverage control: invisible canonical mutation must fail");
        check(Validate(original, changed, control, projection, full, known, offsets[control.Ordinal],
                       (entry, at) => (byte)(ReadExposedByte(entry, at) ^ (at == 0x0b ? 1 : 0))) != null,
              region + " unknown coverage control: collateral unknown-accessor corruption must fail");
        var shopProbe = probes.First(p => original.Entries[p.Ordinal].Kind == Db.AssetKind.Shop && p.Offset == 0x31);
        var shop = original.Entries[shopProbe.Ordinal];
        var shopBytes = (byte[])raw.Clone(); shopBytes[shop.Offset + shopProbe.Offset] ^= shopProbe.Mask;
        string shopBefore = projection(shop);
        string BrokenTypedProjection(Db.Entry entry)
        {
            var columns = projection(entry).Split('\t'); var values = columns[4].Split(',');
            values[6] = shopBefore.Split('\t')[4].Split(',')[6];
            columns[4] = string.Join(',', values); return string.Join('\t', columns);
        }
        check(Validate(original, new Db(shopBytes), shopProbe, BrokenTypedProjection, shopBefore,
                       KnownProjection(shop), offsets[shopProbe.Ordinal]) != null,
              region + " unknown coverage control: Extra-only change cannot hide a stale typed projection");
        int other = (control.Ordinal + 1) % original.Entries.Count;
        sample[original.Entries[other].Offset + 0x0b] ^= 1;
        check(Validate(original, new Db(sample), control, projection, full, known, offsets[control.Ordinal]) != null,
              region + " unknown coverage control: unrelated record mutation must fail");
        Console.WriteLine($"DBA {region}: unresolved-mask layout covers {original.Entries.Count} entries, 8 kinds, {probes.Count} one-bit probes; exhaustive={exhaustive}");
        if (!exhaustive) return;
        var fullBefore = original.Entries.Select(projection).ToArray();
        var knownBefore = original.Entries.Select(KnownProjection).ToArray();
        int failed = 0, duplicateProbes = 0;
        var duplicateOrdinals = new HashSet<int>();
        foreach (var probe in probes)
        {
            var source = original.Entries[probe.Ordinal];
            var bytes = (byte[])raw.Clone(); bytes[source.Offset + probe.Offset] ^= probe.Mask;
            string error;
            try { error = Validate(original, new Db(bytes), probe, projection,
                                   fullBefore[probe.Ordinal], knownBefore[probe.Ordinal], offsets[probe.Ordinal]); }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
            if (source.Key == uint.MaxValue) { duplicateProbes++; duplicateOrdinals.Add(probe.Ordinal); }
            if (error != null && ++failed <= 20)
                check(false, $"{region} ordinal={probe.Ordinal} key={source.Key:x8} kind={source.Kind} field={probe.Field} +0x{probe.Offset:x} mask=0x{probe.Mask:x2}: {error}");
        }
        check(failed == 0, $"{region}: {failed}/{probes.Count} unknown-preservation mutations failed");
        check(duplicateOrdinals.Count == original.Entries.Count(e => e.Key == uint.MaxValue) && duplicateOrdinals.Count > 1,
              region + " unknown coverage exercised distinct duplicate-key records by ordinal");
        Console.WriteLine($"DBA {region}: unknown preservation {probes.Count - failed}/{probes.Count}; duplicate-key probes={duplicateProbes}, distinct ordinals={duplicateOrdinals.Count}; meanings remain unresolved");
    }
}
