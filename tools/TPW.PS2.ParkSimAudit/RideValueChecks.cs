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
            .Select(e => (e.Path, Def: RideDefinition.Parse(Encoding.ASCII.GetString(world.Read(e)), e.Path)))
            .Where(x => x.Def.ShopType == null)
            .ToArray();
        // ⚠ `Attach` is shop-only by design, so rides are looked up directly.

        int seen = 0, bracketsNeutral = 0;
        foreach (var (path, def) in rides)
        {
            var rec = compiled.For(worldName, path);
            if (rec == null || !rec.HasRideTiers) continue;
            var t = rec.Tier(0);
            if (t.MinSpeed == 0 && t.MaxSpeed == 0 && t.MinDuration == 0 && t.MaxDuration == 0) continue;
            seen++;
            bool neutral = t.MinSpeed <= 100 && 100 <= t.MaxSpeed && t.MinDuration <= 5 && 5 <= t.MaxDuration;
            if (neutral) bracketsNeutral++;
            if (seen <= 8)
                Console.WriteLine($"  ride value: {System.IO.Path.GetFileName(path),-22} base {rec.BaseExcitement,4}"
                                + $"  speed {t.MinSpeed}..{t.MaxSpeed}  duration {t.MinDuration}..{t.MaxDuration}"
                                + (neutral ? "  (brackets 100/5)" : "  <- does NOT bracket 100/5"));
        }
        Check(seen > 0, $"the world ships rides with compiled speed/duration ranges ({seen})");
        Console.WriteLine($"  ride value: {bracketsNeutral} of {seen} bracket speed 100 and duration 5");
    }
}
