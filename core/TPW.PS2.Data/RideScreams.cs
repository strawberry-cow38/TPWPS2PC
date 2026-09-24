namespace TPW.PS2.Data;

/// <summary>The ride scream: which guest voices a ride plays while people are on it, how loud,
/// and the console's own bug in the one-shot path.
///
/// Master's standing job is "wiring up guests and the RSEs for all the rides", and the scream is
/// an RSE service the scripts have always asked for and this port has always refused --
/// `RseMachine` routes `STARTSCREAM`/`STOPSCREAM`/`SINGLESCREAM`/`SCREAMLEVEL` to the host and
/// findings/rse-vm.md records them as not implemented.
///
/// ⭐ Every number below is READ, from the opcode dispatcher `FUN_001BCFA8` and the three
/// functions it calls. The scripts drive it off the rider count: `STARTSCREAM VAR_ONRIDE 20`,
/// `SINGLESCREAM VAR_ONRIDE -1` (findings/scripts.md).
///
/// <code>
///   case 0x56 STARTSCREAM a b                                     (0x1BCFA8 +0x1193)
///       handle = FUN_001B94B8(inst[0xD0], a, b, inst[0xC0], x,y,z)
///       inst[0xD0] = inst[0x48] = handle
///   case 0x57 STOPSCREAM        -> reads inst[0xD0], stops it, writes 0 back
///   case 0x58 SINGLESCREAM a b  -> b >= 0 ? FUN_001B98B0(a, b, inst[0xC0], x,y,z)
///                                         : FUN_001BA440(a, x,y,z)
///   case 0x59 SCREAMLEVEL l     -> inst[0xD0] = FUN_001B96D8(inst[0xD0], l, inst[0xC0])
/// </code>
///
/// ⚠⚠ **`inst + 0xD0` IS THE LIVE SCREAM HANDLE, AND THE SAME OFFSET MEANS SOMETHING ELSE ON
/// ANOTHER CLASS.** findings/paths.md reads a u16 at `+0xD0` as the path tool's per-tile price.
/// These are different classes -- the scream stores a u32 handle with `sw`, the path tool reads a
/// u16 with `lhu`, and only three `lhu …,0xd0(…)` sites exist in the image -- but the collision is
/// recorded here so nobody "confirms" one reading with the other. An offset is not a meaning until
/// you know which class you are in.</summary>
public static class RideScreams
{
    /// <summary>⚠⚠ THE PORT'S GROUP NUMBER, NOT THE CONSOLE'S. The native calls all pass
    /// `FUN_00111428(audio, **7**, id, …)`, and native category 7 is `AUDIO/GLOBAL/kids` -- but
    /// that is the AUDIO-CATEGORY numbering, and this port's <see cref="SoundGroup"/> is the
    /// script-side `OBJ_SOUND_*` numbering, where the same file is **6**. Carrying the native 7
    /// across would silently select `STAFSFX.MAP` and every scream would resolve to a staff line
    /// or to nothing. The two namespaces are not an offset apart either: native 0/1/2 are ui/amb/
    /// ride where the script side is 9/8/5.</summary>
    public const SoundGroup Group = SoundGroup.GlobalKids;

    /// <summary>⚠ A PORT TAG, NOT A CONSOLE ONE. The console keeps the running scream by the
    /// handle at `inst + 0xD0`; this port's sound layer addresses voices by a script tag, so the
    /// loop gets a reserved tag of its own and <see cref="ParkRide.ScreamHandle"/> records that it
    /// is running. Negative because a script's own tags are object ids and never are.</summary>
    public const int Tag = -0x5C;

    /// <summary>The one-shots get a separate reserved tag: `STOPSCREAM` stops the LOOP only, and
    /// sharing a tag would have it cut off a one-shot the script never asked it to touch.</summary>
    public const int SingleTag = -0x5D;

    /// <summary>`FUN_001B96D8` sets the running scream's level on sound parameter **6**. See
    /// findings/sound.md: a parameter id means whatever the event's own table says it means.</summary>
    public const int LevelSelector = 6;

    /// <summary>`FUN_001B94B8`: the looping scream, picked by how many people are aboard --
    /// exactly one, two or three, four to seven, or eight and up.
    ///
    /// ⚠⚠ THE THRESHOLD IS THE RIDER COUNT, NOT THE FOOTPRINT. Two places in this repo said
    /// footprint (findings/visitors.md's note, and the audit's own effect label "build, footprint
    /// 1"). The function tests `param_2`, the value `STARTSCREAM` takes from `VAR_ONRIDE`, and its
    /// guard is `handle == 0 && param_2 != 0` -- a placed ride's footprint is never 0, a rider
    /// count is. <returns>null when nobody is aboard, which is the console's "do nothing".</returns></summary>
    public static int? StartId(int riders) => riders <= 0 ? null
        : riders == 1 ? 0x47 : riders < 4 ? 0x48 : riders < 8 ? 0x49 : 0x4A;

    /// <summary>`FUN_001B96D8`: `clamp((a + b) / 2, 0, 100)`, integer division. `a` is the
    /// script's operand and `b` the ride's own <see cref="ParkRide.Setting0xC0"/>.</summary>
    public static int Level(int a, int b) => Math.Clamp((a + b) / 2, 0, 100);

    /// <summary>`FUN_001B98B0`: the LEVELLED one-shot, a 4x4 table -- four rider buckets by four
    /// loudness bands, ids `0x4B..0x5A` contiguous. The band is `min((b + c) / 50, 3)`, and
    /// ⚠ unlike <see cref="Level"/> it is NOT clamped below, so a negative pair gives a negative
    /// band; the console indexes with it anyway and this reproduces that rather than hiding it
    /// behind a clamp the code does not have.</summary>
    public static int LevelledSingleId(int riders, int b, int c)
    {
        int band = Math.Min((b + c) / 50, 3);
        int row = riders == 1 ? 0x4B : riders < 4 ? 0x4F : riders < 8 ? 0x53 : 0x57;
        return row + band;
    }

    /// <summary>`FUN_001BA440`: the UNLEVELLED one-shot -- the arm `SINGLESCREAM VAR_ONRIDE -1`
    /// takes, because its second operand is negative.
    ///
    /// ⚠⚠ **THIS IS A CONSOLE BUG AND IT IS REPRODUCED ON PURPOSE.** The four arms are `if`s with
    /// **no `else` and no early return** -- the only `return` is past the last one:
    ///
    /// <code>
    ///   if (riders == 1) play 0x69 ;  if (riders &lt; 4) play 0x6A ;
    ///   if (riders &lt; 8) play 0x6C ;  play 0x6D                       (unconditional)
    /// </code>
    ///
    /// So one rider fires **four** overlapping voice lines, two or three fire three, four to seven
    /// fire two, and only eight-plus plays a single one. ⚠ And `0x6B` is skipped entirely -- the
    /// cascade goes 0x69, 0x6A, 0x6C, 0x6D -- so id 107 is never played by this function at all.
    /// Both look like a slip, and this port is a faithful base: it plays them all.</summary>
    public static IReadOnlyList<int> UnlevelledSingleIds(int riders)
    {
        if (riders <= 0) return Array.Empty<int>();
        var ids = new List<int>(4);
        if (riders == 1) ids.Add(0x69);
        if (riders < 4) ids.Add(0x6A);
        if (riders < 8) ids.Add(0x6C);
        ids.Add(0x6D);
        return ids;
    }
}
