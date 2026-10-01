namespace TPW.PS2.Data;

/// <summary>⭐ WHAT EACH PARK CAN EVER BUILD, AND IN WHAT ORDER -- the console's per-park catalogue, `PTR_DAT_00360850[world]`,
/// filled at boot by `0x158CF0` from key arrays compiled into the executable (findings/research.md §1.1). Not on the disc, so
/// carried here as a table, the way <see cref="TrackUpgrades"/> carries its column.
///
/// For each park slot and each kind (the DBA's <see cref="AssetResourceDatabase.AssetKind"/>, 1..8), the DBA KEYS in list
/// order: an item's position in its list IS its catalogue index, the number the research database, the research screen
/// and the build menu all speak. All 347 keys resolve in `arsdb.dba` with the kind of their list (research §1.1, three
/// routes agree with the live savestate count). Park slot 2 is JUNGLE's Rollercoaster Test Park.</summary>
public static class ResearchCatalogue
{
    // [world * 3 + park][kind]; index 0 of each row is unused.
    static readonly uint[][][] Lists =
    {
        new uint[][] { new uint[] { }, new uint[] { 225 }, new uint[] { 179, 182, 184, 186, 188, 190, 192, 193, 194, 197, 198, 200, 202, 204, 205, 207, 208 }, new uint[] { 221, 222, 223, 224, 226, 228, 230, 234 }, new uint[] { 239, 240, 241, 242, 243, 244, 245, 246 }, new uint[] { 247, 249, 251, 253 }, new uint[] { 220 }, new uint[] { }, new uint[] { 237, 236 } },   // JUNGLE 0
        new uint[][] { new uint[] { }, new uint[] { 217, 218 }, new uint[] { 178, 180, 183, 185, 187, 189, 191, 193, 195, 196, 198, 199, 201, 203, 204, 206, 207, 208 }, new uint[] { 216, 215, 227, 231, 233, 219, 229 }, new uint[] { 239, 240, 241, 242, 243, 244, 245, 246 }, new uint[] { 252, 250, 248, 606 }, new uint[] { 235 }, new uint[] { 232 }, new uint[] { 238 } },   // JUNGLE 1
        new uint[][] { new uint[] { }, new uint[] { 225, 217, 218, 132, 169, 133, 135, 170, 47, 52, 51, 371, 370, 376 }, new uint[] { 179, 190, 203, 206, 197, 195, 207 }, new uint[] { }, new uint[] { 239 }, new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { } },   // JUNGLE 2
        new uint[][] { new uint[] { }, new uint[] { 132, 169 }, new uint[] { 100, 102, 103, 105, 106, 108, 109, 110, 111, 113, 114, 115, 116, 117, 120, 121, 122, 462 }, new uint[] { 136, 139, 140, 130, 144, 128, 168 }, new uint[] { 147, 148, 149, 150, 151, 152, 153, 154 }, new uint[] { 155, 160, 158, 156 }, new uint[] { 146 }, new uint[] { 145 }, new uint[] { 167 } },   // HALLOW 0
        new uint[][] { new uint[] { }, new uint[] { 133, 135, 170 }, new uint[] { 101, 104, 105, 107, 110, 112, 113, 115, 118, 119, 120, 121, 458, 459, 460, 461, 463, 464 }, new uint[] { 131, 134, 137, 141, 142, 143, 129 }, new uint[] { 147, 148, 149, 150, 151, 152, 153, 154 }, new uint[] { 159, 157, 161, 607 }, new uint[] { 138 }, new uint[] { }, new uint[] { 165, 166 } },   // HALLOW 1
        new uint[][] { new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { } },   // HALLOW 2
        new uint[][] { new uint[] { }, new uint[] { 47, 52 }, new uint[] { 26, 27, 29, 31, 32, 34, 36, 37, 40, 465, 467, 469, 470, 85, 471, 472, 475, 477 }, new uint[] { 46, 48, 55, 57, 58, 60, 83 }, new uint[] { 64, 65, 66, 67, 68, 69, 70, 71 }, new uint[] { 74, 77, 73 }, new uint[] { 62 }, new uint[] { }, new uint[] { } },   // FANTASY 0
        new uint[][] { new uint[] { }, new uint[] { 51 }, new uint[] { 26, 28, 30, 84, 33, 35, 39, 465, 466, 468, 469, 470, 473, 474, 475, 476, 478 }, new uint[] { 49, 63, 50, 53, 54, 56 }, new uint[] { 64, 65, 66, 67, 68, 69, 70, 71 }, new uint[] { 76, 72, 75, 605 }, new uint[] { 59 }, new uint[] { 61 }, new uint[] { 81, 82 } },   // FANTASY 1
        new uint[][] { new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { } },   // FANTASY 2
        new uint[][] { new uint[] { }, new uint[] { 371 }, new uint[] { 338, 340, 342, 344, 346, 347, 348, 350, 353, 355, 356, 357, 358, 479, 481, 483, 484 }, new uint[] { 365, 366, 372, 374, 379, 380, 403, 383 }, new uint[] { 385, 386, 387, 388, 389, 390, 391, 392 }, new uint[] { 395, 397, 396 }, new uint[] { 381 }, new uint[] { }, new uint[] { 402 } },   // SPACE 0
        new uint[][] { new uint[] { }, new uint[] { 370, 376 }, new uint[] { 339, 341, 343, 345, 346, 347, 352, 354, 356, 357, 358, 480, 482, 483, 485 }, new uint[] { 364, 368, 369, 373, 377, 378, 405, 382 }, new uint[] { 385, 386, 387, 388, 389, 390, 391, 392 }, new uint[] { 393, 394, 398, 608 }, new uint[] { 367 }, new uint[] { 375 }, new uint[] { } },   // SPACE 1
        new uint[][] { new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { }, new uint[] { } },   // SPACE 2
    };

    static uint[][] Park(int world, int park)
        => world is >= 0 and < 4 && park is >= 0 and < 3 ? Lists[world * 3 + park] : null;

    /// <summary>The kind's keys in this park, in catalogue order; empty when the park has none.</summary>
    public static IReadOnlyList<uint> Keys(int world, int park, AssetResourceDatabase.AssetKind kind)
        => Park(world, park) is { } p && (int)kind is > 0 and < 9 ? p[(int)kind] : System.Array.Empty<uint>();

    /// <summary>A key's catalogue index for its kind in this park, or -1 when the park does not list it.</summary>
    public static int IndexOf(int world, int park, AssetResourceDatabase.AssetKind kind, uint key)
        => Keys(world, park, kind) is var keys ? IndexIn(keys, key) : -1;

    static int IndexIn(IReadOnlyList<uint> keys, uint key)
    {
        for (int i = 0; i < keys.Count; i++) if (keys[i] == key) return i;
        return -1;
    }

    /// <summary>Whether a (world, park) has a catalogue at all.</summary>
    public static bool Has(int world, int park) => Park(world, park) != null;
}
