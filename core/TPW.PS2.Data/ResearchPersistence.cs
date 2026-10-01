namespace TPW.PS2.Data;

/// <summary>The meaningful native research section, not a whole save file: catalogue-ordered
/// {percent,level} pairs, then budget and five {active,category,item} triples. World/park context
/// and final stream alignment belong to the outer coordinator. See findings/research.md §5.
/// Save is ONE invocation of 0x1C2968 and intentionally has its database-query side effects;
/// the native whole-save sizing and writing passes invoke that routine separately.</summary>
public static class ResearchPersistence
{
    public const int ManagerBytes = 1 + 3 * ResearchManager.SlotCount;
    static readonly int[] KindOrder = { 3, 7, 6, 1, 2, 4, 5, 8 };

    static ResearchDatabase Database(ResearchManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.Database ?? throw new InvalidOperationException("research persistence requires the park database");
    }

    /// <summary>Payload length only, with NO Level/Percent queries. This is not the native
    /// mutating sizing traversal. The test-park flag skips both database and manager payloads.</summary>
    public static int Length(ResearchDatabase db, bool testPark = false)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (testPark) return 0;
        if (!ResearchCatalogue.Has(db.World, db.Park)) throw new InvalidOperationException("unknown research catalogue context");
        return checked(2 * KindOrder.Sum(db.Count) + ManagerBytes);
    }

    /// <summary>0x1C2968 plus 0x1B66C0/0x1B7678. MUTATES group-zero records through normal
    /// queries; captures L before asking Percent at that L. Returned bytes do not alias live state.
    /// No progress, weights, completion flags, thresholds or debug switches are serialized.</summary>
    public static byte[] Save(ResearchManager manager, bool testPark = false)
    {
        var db = Database(manager);
        var bytes = new byte[Length(db, testPark)];
        if (testPark) return bytes;
        int offset = 0;
        foreach (int cat in KindOrder)
            for (int item = 0; item < db.Count(cat); item++)
            {
                byte level = unchecked((byte)db.Level(cat, item));
                bytes[offset++] = unchecked((byte)db.Percent(cat, item, level));
                bytes[offset++] = level;
            }
        bytes[offset++] = unchecked((byte)manager.Budget);
        foreach (var slot in manager.Slots)
        {
            bytes[offset++] = slot.Active ? (byte)1 : (byte)0;
            bytes[offset++] = unchecked((byte)slot.Category);
            bytes[offset++] = unchecked((byte)slot.Item);
        }
        return bytes;
    }

    /// <summary>0x160AC0 then 0x1B6720: File ALL database pairs before restoring budget/restarting
    /// active slots. Ordinarily the caller supplies a fresh park database/manager. This routine
    /// does NOT wipe them: inactive triples are ignored and native busy/threshold refusals remain
    /// ignored. Whole fixed-point progress is reconstructed by StartResearch, not serialized.
    /// Length/active-key validation is a host safety check absent from the unsafe native loader;
    /// it happens before any state writes. Context is supplied by the outer save, not this payload.</summary>
    public static void Load(ResearchManager manager, ReadOnlySpan<byte> bytes, bool testPark = false)
    {
        var db = Database(manager);
        int expected = Length(db, testPark);
        if (bytes.Length != expected) throw new InvalidDataException($"research section length {bytes.Length}, expected {expected}");
        if (testPark) return;
        int managerOffset = expected - ManagerBytes;
        for (int slot = 0; slot < ResearchManager.SlotCount; slot++)
        {
            int offset = managerOffset + 1 + 3 * slot;
            if (bytes[offset] == 0) continue;
            int cat = bytes[offset + 1];
            int item = unchecked((sbyte)bytes[offset + 2]); // native LB, unlike category's LBU
            if (cat < 1 || cat > 8 || item < 0 || item >= db.Count(cat))
                throw new InvalidDataException($"research active slot {slot} has invalid catalogue key {cat}/{item}");
        }
        int at = 0;
        foreach (int cat in KindOrder)
            for (int item = 0; item < db.Count(cat); item++)
            {
                int percent = bytes[at++];
                int level = bytes[at++];
                db.File(cat, item, level, percent);
            }
        manager.SetBudget(bytes[at++]);
        for (int slot = 0; slot < ResearchManager.SlotCount; slot++)
        {
            bool active = bytes[at++] != 0; // any nonzero requests a restart
            int cat = bytes[at++];
            int item = unchecked((sbyte)bytes[at++]);
            if (active) manager.StartResearch(slot, cat, item); // native ignores the result
        }
    }
}