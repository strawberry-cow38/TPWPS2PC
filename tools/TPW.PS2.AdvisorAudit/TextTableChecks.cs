using System.Buffers.Binary;
using System.Text;
using TPW.PS2.Data;

/// <summary>Small independent structural controls; no disc or extracted assets needed.</summary>
static class TextTableChecks
{
    public static int Run(bool extremeCounts = false)
    {
        int checks = 0, bad = 0;
        void Check(bool ok, string label)
        {
            checks++;
            if (!ok) { bad++; Console.Error.WriteLine("FAIL text table: " + label); }
        }
        void Reject(byte[] bytes, string label)
        {
            byte[] before = (byte[])bytes.Clone();
            try { TextDatabase.ParseTable(bytes); Check(false, label + " accepted"); }
            catch (InvalidDataException) { Check(true, label); }
            catch (Exception ex) { Check(false, label + " threw " + ex.GetType().Name + " instead of InvalidDataException"); }
            Check(bytes.AsSpan().SequenceEqual(before), label + " modified input");
        }
        byte[] Table(params string[] rows)
        {
            byte[][] encoded = rows.Select(Encoding.Latin1.GetBytes).ToArray();
            byte[] bytes = new byte[4 + 4 * rows.Length + encoded.Sum(s => s.Length + 1)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, rows.Length);
            int offset = 4 + 4 * rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4 + 4 * i), offset);
                encoded[i].CopyTo(bytes, offset); offset += encoded[i].Length + 1;
            }
            return bytes;
        }
        byte[] ChangeInt(byte[] bytes, int at, int value)
        {
            byte[] copy = (byte[])bytes.Clone(); BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(at), value); return copy;
        }
        try { TextDatabase.ParseTable(null); Check(false, "null input accepted"); }
        catch (ArgumentNullException) { Check(true, "null input has explicit API argument failure"); }
        Check(TextDatabase.ParseTable(Table("", "", "")).SequenceEqual(new[] { "", "", "" }),
              "consecutive/final empty rows meet the minimum-size bound exactly");
        // Literal-byte oracle: 64 empty strings start at byte 0x0104. This does
        // not use the table builder or BinaryPrimitives for its count/offsets.
        byte[] literal = new byte[324]; literal[0] = 64;
        for (int i = 0; i < 64; i++)
        { literal[4 + 4 * i] = (byte)(4 + i); literal[5 + 4 * i] = 1; }
        Check(TextDatabase.ParseTable(literal).SequenceEqual(Enumerable.Repeat("", 64)),
              "literal little-endian offsets above byte 255 preserve 64 empty rows");
        var rows = new[] { "", "KEY ", "KEY %d %s", "éÿ", "a\nb\tc" };
        byte[] valid = Table(rows), unchanged = (byte[])valid.Clone();
        Check(TextDatabase.ParseTable(valid).SequenceEqual(rows), "Latin1, empty rows, whitespace and format strings survive exactly");
        Check(valid.AsSpan().SequenceEqual(unchanged), "successful parse preserves input");
        Check(TextDatabase.ParseTable(Table()).Length == 0, "canonical zero-row table is accepted");
        for (int length = 0; length < 4; length++) Reject(new byte[length], "truncated count " + length);
        Reject(ChangeInt(Table(), 0, -1), "negative count");
        Reject(ChangeInt(Table("A"), 0, 1024), "count larger than the available directory/payload");
        long beforeCountReject = GC.GetAllocatedBytesForCurrentThread();
        Reject(ChangeInt(Table(), 0, 1_000_000), "million-row count in a four-byte input");
        long countRejectBytes = GC.GetAllocatedBytesForCurrentThread() - beforeCountReject;
        Check(countRejectBytes < 65536, "oversized count is rejected before allocating a count-sized output array");
        Reject(ChangeInt(new byte[8], 0, 2), "truncated offset directory");
        Reject(Table("")[..^1], "no room for a row terminator");
        Reject(Table("ABC")[..^1], "missing final NUL");
        var crossedRow = Table("A", "B"); crossedRow[13] = (byte)'X';
        Reject(crossedRow, "unterminated first row scans across the next declared offset");
        Reject(ChangeInt(Table("A"), 4, 0), "offset into count/header");
        Reject(ChangeInt(Table("A"), 4, 4), "offset into directory");
        Reject(ChangeInt(Table("A"), 4, -1), "negative offset");
        Reject(ChangeInt(Table("A"), 4, int.MaxValue), "offset beyond buffer");
        Reject(ChangeInt(Table("A"), 4, Table("A").Length), "offset exactly at EOF");
        Reject(ChangeInt(Table("A", "B"), 8, 12), "duplicate/overlapping offsets");
        var reversed = ChangeInt(ChangeInt(Table("A", "B"), 4, 14), 8, 12);
        Reject(reversed, "reversed rows");
        var firstGap = Table("A").ToList(); firstGap.Insert(8, 42);
        Reject(ChangeInt(firstGap.ToArray(), 4, 9), "gap before first string");
        var middleGap = Table("A", "B").ToList(); middleGap.Insert(14, 42);
        Reject(ChangeInt(middleGap.ToArray(), 8, 15), "gap between strings");
        Reject(Table("A").Concat(new byte[] { 0 }).ToArray(), "trailing zero after final row");
        Reject(Table("A").Concat(new byte[] { 42 }).ToArray(), "trailing nonzero byte after final row");
        Reject(Table().Concat(new byte[] { 0 }).ToArray(), "payload after zero-row table");
        // Enabled after count validation is fixed: never ask the old reader to
        // allocate arrays sized from hostile billion-row headers during reproduction.
        if (extremeCounts)
        {
            Reject(ChangeInt(Table(), 0, int.MaxValue), "maximum signed count in tiny input");
            Reject(ChangeInt(Table(), 0, int.MinValue), "maximum unsigned count high bit in tiny input");
        }
        for (int count = 1; count <= 16; count++)
        {
            string[] sample = Enumerable.Range(0, count).Select(i => new string((char)(1 + (i * 37) % 255), i % 7)).ToArray();
            Check(TextDatabase.ParseTable(Table(sample)).SequenceEqual(sample), "deterministic mixed-row roundtrip " + count);
        }
        Console.WriteLine($"TEXT TABLE {(bad == 0 ? "PASS" : "FAIL")}: {checks} checks, {bad} failures");
        return bad;
    }
}
