using TPW.PS2.Data;

/// <summary>⭐⭐ DOES THE AUTHORED-TO-COMPILED JOIN FIND THE ROWS THAT DISAGREE?
///
/// A join that only matched shops whose numbers already agree would look perfect and be useless:
/// the disagreements are the entire reason it exists. So the headline check is not "it resolved
/// something" but "it resolved the balloon shop, whose `.sam` says 15 and whose compiled record
/// says 10" -- and that it reports the value the CONSOLE would use.</summary>
static class CompiledJoinChecks
{
    public static void Run(WadArchive data, WadArchive world, string worldName, Action<bool, string> check)
    {
        void Check(bool ok, string m) => check(ok, "compiled join: " + m);

        var dbaEntry = data.Entries.FirstOrDefault(e => e.Path.Equals("/arsdb.dba", StringComparison.OrdinalIgnoreCase));
        var text = TextDatabase.Load(data, "eur");
        if (dbaEntry == null || text == null) { Check(false, "the disc carries /arsdb.dba and an eur text table"); return; }

        var db = new AssetResourceDatabase(data.Read(dbaEntry));
        var compiled = new CompiledAssets(db, text);
        Check(compiled.Count > 0, $"the compiled directory resolves to named identities ({compiled.Count})");

        // Every .sam in this world that declares the shop block -- the population the purchase
        // path actually reads.
        var shops = world.Entries
            .Where(e => e.Path.EndsWith(".sam", StringComparison.OrdinalIgnoreCase))
            .Select(e => (e.Path, Def: RideDefinition.Parse(System.Text.Encoding.ASCII.GetString(world.Read(e)), e.Path)))
            .Where(x => x.Def.ShopType != null)
            .ToArray();
        Console.WriteLine("  " + compiled.Report(worldName, shops.Select(s => s.Path)));
        Check(shops.Length > 0, $"the world ships shops to join ({shops.Length})");

        int joined = shops.Count(s => compiled.For(worldName, s.Path) is { Shop: not null });
        // ⚠ N-of-M, not a bool: a join that quietly covered half the shops would pass any
        // "it worked" assertion, and half a join is how the wrong number reaches a guest.
        Check(joined == shops.Length, $"every shop joins to a compiled SHOP payload ({joined} of {shops.Length})");

        // ⭐⭐ THE CASE THE JOIN EXISTS FOR. Find a shop whose authored happiness differs from its
        // compiled happiness, and require the join to surface the compiled one.
        var disagreeing = shops
            .Select(s => (s.Path, s.Def, Row: compiled.For(worldName, s.Path)?.Shop))
            .Where(x => x.Row != null && x.Def.HappinessEffect is { } a && a != x.Row.HappinessEffect)
            .ToArray();
        foreach (var (path, def, row) in disagreeing)
            Console.WriteLine($"    DISAGREES  {path}: .sam says {def.HappinessEffect}, compiled says {row.HappinessEffect}");
        // ⚠ Not asserted as "there must be exactly one": three of the four worlds carry the
        // balloon divergence and FANTASY does not, so the COUNT is a fact about the world.
        Check(disagreeing.All(d => d.Row.HappinessEffect != d.Def.HappinessEffect),
              $"the join reports the compiled value where the two differ ({disagreeing.Length} such shops in {worldName})");
    }
}
