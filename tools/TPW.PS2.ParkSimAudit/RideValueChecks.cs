using System.Text;
using TPW.PS2.Data;

/// <summary>⭐⭐ WHAT A RIDE IS WORTH TO A GUEST, which this port invented as a flat 45.
/// `FUN_001B82D0` is the ordinary attraction's `+0x1D4` producer -- the very slot
/// `FUN_0020EDD8`'s ride arm calls -- and it is read, not guessed.</summary>
static class RideValueChecks
{
    public static void Run(WadArchive data, WadArchive world, string worldName, Action<bool, string> check)
    {
        void Check(bool ok, string m) => check(ok, "ride value: " + m);

        var dbaEntry = data.Entries.FirstOrDefault(e => e.Path.Equals("/arsdb.dba", StringComparison.OrdinalIgnoreCase));
        var text = TextDatabase.Load(data, "eur");
        if (dbaEntry == null || text == null) { Check(false, "the disc carries /arsdb.dba and an eur text table"); return; }
        var compiled = new CompiledAssets(new AssetResourceDatabase(data.Read(dbaEntry)), text);

        var rides = world.Entries
            .Where(e => e.Path.EndsWith(".sam", StringComparison.OrdinalIgnoreCase))
            // ⚠ THE PRODUCTION SOURCE SHAPE. `Attach` infers the world from a disc-absolute
            // `/DATA/<WORLD>.WAD/...` source; handing it the bare WAD-relative path leaves it
            // unable to split one and it joins nothing. The first version of this check did
            // exactly that and reported "0 records" -- correctly, and for a reason that was
            // about the FIXTURE rather than the code under test.
            .Select(e => (e.Path, Def: RideDefinition.Parse(Encoding.ASCII.GetString(world.Read(e)),
                                                           "/DATA/" + worldName + ".WAD" + e.Path)))
            .Where(x => x.Def.ShopType == null)
            .ToArray();
        // ⭐⭐ THROUGH THE SHIPPING JOIN, NOT A PRIVATE LOOKUP. The first version of this file
        // called `compiled.For(...)` itself, so it passed happily while `Attach` -- the method
        // the viewer and the sim actually use -- joined shops only and left every placed ride
        // with no compiled record at all. A check that reaches around the wiring tests the
        // lookup and never the wiring. astraclaw caught it; the warning was already written in
        // `Attach`'s own docstring.
        compiled.Attach(rides.Select(r => r.Def), out string joinReport);
        Console.WriteLine("  ride value: " + joinReport);
        Check(rides.Count(r => r.Def.CompiledEntry != null) > 0,
              "the SHIPPING join gives placed rides a compiled record");

        int seen = 0, bracketsNeutral = 0;
        foreach (var (path, def) in rides)
        {
            var rec = def.CompiledEntry;
            if (rec == null || !rec.HasRideTiers) continue;
            var t = rec.Tier(0);
            if (t.MinSpeed == 0 && t.MaxSpeed == 0 && t.MinDuration == 0 && t.MaxDuration == 0) continue;
            seen++;
            // ⭐ The traced default setter `0x116120`: midpoint speed, half the maximum duration.
            var placed = new ParkRide { Definition = def };
            int wantSpeed = t.MinSpeed + ((t.MaxSpeed - t.MinSpeed) >> 1), wantDur = Math.Max(1, t.MaxDuration >> 1);
            if (placed.Speed == wantSpeed && placed.Duration == wantDur) bracketsNeutral++;
            if (seen <= 6)
                Console.WriteLine($"  ride value: {System.IO.Path.GetFileName(path),-22} base {rec.BaseExcitement,4}"
                                + $"  speed {t.MinSpeed}..{t.MaxSpeed} -> {placed.Speed}"
                                + $"  duration {t.MinDuration}..{t.MaxDuration} -> {placed.Duration}"
                                + $"  = {placed.Value?.ToString() ?? "(not this family)"}");
        }
        Check(seen > 0, $"the world ships rides with compiled speed/duration ranges ({seen})");
        // ⭐⭐ THE FAMILY GUARD, and it needs a control on BOTH sides or it is just an assertion
        // that something returns null. An ORDINARY ride must produce a value; a COASTER -- whose
        // `+0x1D4` is a different function with different arithmetic -- must produce none, so the
        // caller falls back instead of being handed the wrong family's number.
        ParkRide Bare(RideDefinition d) => new() { Definition = d };
        var ordinary = rides.FirstOrDefault(r => r.Def.CompiledEntry?.Kind == AssetResourceDatabase.AssetKind.Ride);
        var coaster  = rides.FirstOrDefault(r => r.Def.CompiledEntry?.Kind == AssetResourceDatabase.AssetKind.Coaster);
        // ⭐⭐ PLACEMENT COSTS, over every joined definition in the world. `FUN_0012BC10` reads
        // record[0x50] for the tiered families and record[0x20] for the rest, and `FUN_00126408`
        // charges ten times it. ⚠ Checked as a POPULATION, not one example: the two fields sit
        // 48 bytes apart, so a definition taking the wrong branch reads some neighbouring word
        // and would look like a plausible price on any single ride.
        var priced = rides.Where(r => r.Def.CompiledEntry != null).ToArray();
        int costed = priced.Count(r => r.Def.PlacementCost is > 0);
        Check(priced.Length > 0 && costed == priced.Length,
              $"every joined ride has a placement cost ({costed} of {priced.Length})");
        // ⚠ THE CONTROL: an unjoined definition must say UNKNOWN rather than free.
        var bare = RideDefinition.Parse("Info.Name\t\"nothing\"\n", "synthetic/none.sam");
        Check(bare.PlacementCost == null, "and a definition with no compiled record has no cost, rather than a free one");
        // ⚠⚠ A FLOOR OF 1000 WAS FITTED TO JUNGLE AND FAILED IN SPACE, where something costs
        // 500 tenths -- fifty dollars, perfectly reasonable for a small feature. That is the
        // FOURTH check tonight written against one world's numbers. ⭐ What the check is really
        // for is a branch reading the WRONG WORD, 48 bytes from the right one, and that failure
        // shows as zero, negative, or astronomical -- never as a slightly-low price. So assert
        // THAT, and let the data say what a thing is worth.
        var costs = priced.Select(r => r.Def.PlacementCost ?? 0).ToArray();
        Check(costs.All(c => c > 0 && c < 100_000_000),
              $"and every cost is a positive, non-absurd figure ({costs.Min()}..{costs.Max()} tenths)");
        // ⚠ AND BOTH BRANCHES ARE EXERCISED, or the family split is untested: a world ships
        // tiered rides and untiered features, and each must produce a cost.
        Check(priced.Any(r => r.Def.CompiledEntry.HasRideTiers) && priced.Any(r => !r.Def.CompiledEntry.HasRideTiers),
              "and both the tiered and untiered cost fields are exercised");

        if (ordinary.Def != null)
            Check(Bare(ordinary.Def).Value is > 0, $"an ordinary ride has a value ({Bare(ordinary.Def).Value})");
        else Check(false, "the world ships an ordinary ride to value");
        if (coaster.Def != null)
            Check(Bare(coaster.Def).Value == null, "a COASTER refuses -- its producer is a different function and is not ported");
        else Check(false, "the world ships a coaster as the control");
        // ⭐⭐ ASSERTED AGAINST THE TRACED SETTER, every ride in the world. ⚠ The check recomputes
        // the formula rather than comparing a stored number, so it fails if `ParkRide` ever
        // quietly reverts to a chosen default -- which is what it did until astraclaw read it.
        Check(bracketsNeutral == seen,
              $"every ride opens at the traced default -- midpoint speed, half the maximum duration ({bracketsNeutral} of {seen})");
    }
}
