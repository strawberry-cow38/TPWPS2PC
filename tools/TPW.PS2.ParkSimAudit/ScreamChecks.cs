using TPW.PS2.Data;

/// <summary>The ride scream. ⭐ Every expectation is a number read out of `FUN_001BCFA8` and the
/// three functions it calls; the last block checks those ids against the owner's own disc, so a
/// table that is self-consistent but points at events that do not exist still fails.</summary>
static class ScreamChecks
{
    public static void Run(Disc disc, string world, Action<bool, string> check)
    {
        void Check(bool ok, string m) => check(ok, "scream: " + m);

        // ---- which voices, by rider count (FUN_001B94B8) ---------------------------------
        Check(RideScreams.StartId(0) == null, "nobody aboard starts nothing -- the console's `param_2 != 0` guard");
        Check(RideScreams.StartId(-3) == null, "a negative rider count starts nothing either");
        Check(RideScreams.StartId(1) == 0x47, "one rider -> 0x47");
        // ⭐ THE BOUNDARIES ARE THE POINT: 3/4 and 7/8 are where the console's `< 4` and `< 8` sit,
        // and an off-by-one here is exactly the bug this check exists to reject.
        Check(RideScreams.StartId(2) == 0x48 && RideScreams.StartId(3) == 0x48, "two and three -> 0x48");
        Check(RideScreams.StartId(4) == 0x49 && RideScreams.StartId(7) == 0x49, "four to seven -> 0x49");
        Check(RideScreams.StartId(8) == 0x4A && RideScreams.StartId(40) == 0x4A, "eight and up -> 0x4A");

        // ---- the level (FUN_001B96D8) ----------------------------------------------------
        Check(RideScreams.Level(20, 0) == 10, "the level is the MEAN of the pair, not the operand -- (20+0)/2");
        Check(RideScreams.Level(300, 300) == 100, "and it clamps at 100");
        Check(RideScreams.Level(-80, -80) == 0, "and at 0");
        Check(RideScreams.LevelSelector == 6, "it is set on sound parameter 6");

        // ---- the levelled one-shot: a 4x4 table (FUN_001B98B0) ---------------------------
        Check(RideScreams.LevelledSingleId(1, 0, 0) == 0x4B, "one rider, quietest band -> 0x4B");
        Check(RideScreams.LevelledSingleId(1, 50, 0) == 0x4C, "the band steps every 50, not every 100");
        Check(RideScreams.LevelledSingleId(1, 150, 0) == 0x4E && RideScreams.LevelledSingleId(1, 9999, 0) == 0x4E,
              "and saturates at band 3 -> 0x4E");
        Check(RideScreams.LevelledSingleId(2, 0, 0) == 0x4F && RideScreams.LevelledSingleId(4, 0, 0) == 0x53
           && RideScreams.LevelledSingleId(8, 0, 0) == 0x57, "the four rider rows are 0x4B/0x4F/0x53/0x57");
        // ⭐ AN INVARIANT, NOT A COPY OF THE TABLE: sixteen ids, contiguous, no gaps and no repeats.
        var table = new List<int>();
        foreach (int r in new[] { 1, 2, 4, 8 })
            for (int band = 0; band < 4; band++) table.Add(RideScreams.LevelledSingleId(r, band * 50, 0));
        Check(table.Distinct().Count() == 16 && table.Min() == 0x4B && table.Max() == 0x5A,
              $"the table is 0x4B..0x5A contiguous ({table.Count} entries, {table.Distinct().Count()} distinct)");

        // ---- the unlevelled one-shot, and the console's missing `else` (FUN_001BA440) ----
        Check(RideScreams.UnlevelledSingleIds(0).Count == 0, "nobody aboard plays nothing");
        // ⚠⚠ REPRODUCING A BUG, SO THE CHECK ASSERTS THE BUG. If somebody "fixes" the cascade into
        // an if/else chain these four fail, which is the entire point of having them.
        Check(RideScreams.UnlevelledSingleIds(1).SequenceEqual(new[] { 0x69, 0x6A, 0x6C, 0x6D }),
              "ONE rider fires FOUR overlapping lines -- the arms have no else");
        Check(RideScreams.UnlevelledSingleIds(3).SequenceEqual(new[] { 0x6A, 0x6C, 0x6D }), "two or three fire three");
        Check(RideScreams.UnlevelledSingleIds(7).SequenceEqual(new[] { 0x6C, 0x6D }), "four to seven fire two");
        Check(RideScreams.UnlevelledSingleIds(8).SequenceEqual(new[] { 0x6D }), "eight and up fire one");
        // ⭐ And the oddity that proves we are reading the cascade rather than reconstructing it.
        Check(Enumerable.Range(0, 40).All(r => !RideScreams.UnlevelledSingleIds(r).Contains(0x6B)),
              "0x6B is never played -- the cascade goes 0x69, 0x6A, 0x6C, 0x6D");

        // ---- the namespace, which is the easiest thing here to get wrong -----------------
        // ⚠⚠ The console passes 7 to FUN_00111428 and 7 is the AUDIO CATEGORY for kids; this
        // port's SoundGroup is the SCRIPT-side numbering, where kids is 6. A check that just
        // repeated the native constant would pass while every scream resolved in the staff map.
        Check((int)RideScreams.Group == 6, "the port group is the SCRIPT-side kids number (6), not the native category 7");
        Check(SoundCatalogue.MapFor(RideScreams.Group, world, 1) == "/AUDIO/GLOBAL/KIDSSFX.MAP",
              "and it resolves to KIDSSFX.MAP");
        Check(SoundCatalogue.MapFor((SoundGroup)7, world, 1) != "/AUDIO/GLOBAL/KIDSSFX.MAP",
              "CONTROL: the native number 7 would have picked a different map");

        // ---- and do these events actually exist on the owner's disc? ---------------------
        var cat = new SoundCatalogue(disc, world, 1);
        int miss = 0; var absent = new List<string>();
        var every = new List<int> { 0x47, 0x48, 0x49, 0x4A, 0x69, 0x6A, 0x6C, 0x6D };
        every.AddRange(table);
        foreach (int id in every.Distinct())
            if (cat.Resolve((int)RideScreams.Group, id) == null) { miss++; absent.Add($"0x{id:X}"); }
        Check(miss == 0, $"every scream event resolves in the kids map ({every.Distinct().Count()} ids{(miss == 0 ? "" : ", missing " + string.Join(",", absent))})");
        // ⭐ THE CONTROL THIS NEEDS: the lookup must be capable of saying no, or the line above is free.
        Check(cat.Resolve((int)RideScreams.Group, 60000) == null, "CONTROL: a made-up event id does NOT resolve");
    }
}
