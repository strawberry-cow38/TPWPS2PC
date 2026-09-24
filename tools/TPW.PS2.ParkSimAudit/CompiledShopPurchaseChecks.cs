using System.Text;
using TPW.PS2.Data;

/// <summary>Named compiled records, explicit regions and real shop-script handbacks.
/// Tests verified initial q1=100/q2=0, not mutable quality or full carried-object behavior.</summary>
static class CompiledShopPurchaseChecks
{
    public static void Run(Model terrain, ParkPaths sourcePaths, ParkCell entrance, ParkCell exit,
                           WadArchive data, WadArchive world, string worldName, Action<bool, string> check)
    {
        void Check(bool ok, string text) => check(ok, "compiled purchase: " + text);
        var named = worldName switch
        {
            "JUNGLE" => (Stem: "/Shops/Balloon/Balloon", Key: 239u, Price: 45, Cost: 30, Authored: 15),
            "HALLOW" => (Stem: "/Shops/vampshop/vampshop", Key: 153u, Price: 45, Cost: 30, Authored: 15),
            "FANTASY" => (Stem: "/Shops/fatfairy/fatfairy", Key: 66u, Price: 45, Cost: 30, Authored: 10),
            "SPACE" => (Stem: "/Shops/droid/droid", Key: 388u, Price: 50, Cost: 35, Authored: 15),
            _ => throw new ArgumentException("unknown fixture world " + worldName),
        };
        string iceStem = worldName is "HALLOW" or "SPACE" ? "/Shops/ices/ices" : "/Shops/IceCream/IceCream";
        uint iceKey = worldName switch { "JUNGLE" => 245, "HALLOW" => 151, "FANTASY" => 69, _ => 391 };
        var drinkCase = worldName switch
        {
            "JUNGLE" => (Stem: "/Shops/Coconut/Coconut", Key: 241u, Price: 30),
            "HALLOW" => (Stem: "/shops/pumpkin/Pumpkin", Key: 152u, Price: 30),
            "FANTASY" => (Stem: "/Shops/drinks/drinks", Key: 65u, Price: 30),
            _ => (Stem: "/Shops/drinks/drinks", Key: 387u, Price: 35),
        };
        var friesCase = worldName switch
        {
            "JUNGLE" => (Stem: "/Shops/Fries/Fries", Key: 243u, Price: 30, H: 25, V: 10),
            "HALLOW" => (Stem: "/shops/gchips/gchips", Key: 149u, Price: 30, H: 20, V: 15),
            "FANTASY" => (Stem: "/Shops/fries/Fries", Key: 67u, Price: 30, H: 20, V: 15),
            _ => (Stem: "/Shops/fries/fries", Key: 389u, Price: 35, H: 20, V: 10),
        };
        var costumeCase = worldName switch
        {
            "JUNGLE" => (Stem: "/Shops/Cost_Shp/Cost_shp", Key: 242u, Price: 60),
            "HALLOW" => (Stem: "/shops/witch/Witch", Key: 154u, Price: 60),
            "FANTASY" => (Stem: "/Shops/bee/bee", Key: 64u, Price: 60),
            _ => (Stem: "/Shops/costume/Costume", Key: 386u, Price: 50),
        };
        VisitorWants Initial(int cash = 1234) => new() { Cash = cash, Hunger = 80, Thirst = 70, Toilet = 10,
            Happiness = 20, Sick = 20, Litter = 9, Unknown78 = 62, Boredom = 50, PreferredIntensity = 90 };
        RideDefinition Definition(string stem, bool bareSource = false)
        {
            var entry = world.Find(stem + ".sam") ?? throw new InvalidDataException("missing " + stem);
            // Path-shape controls below are independent of the regional purchase loop.
            string prefix = bareSource ? worldName : "/DATA/" + worldName + ".WAD";
            return RideDefinition.Parse(Encoding.ASCII.GetString(world.Read(entry)), prefix + entry.Path);
        }
        (ParkVisitors Visitors, VisitorWants After, int Id) Purchase(string stem, RideDefinition definition, int cash = 1234)
        {
            var paths = new ParkPaths(terrain); sourcePaths.Field.Cells.CopyTo(paths.Field.Cells, 0);
            var sim = new ParkSim(paths);
            var script = world.Find(stem + ".rse") ?? throw new InvalidDataException("no shop script " + stem);
            string folder = stem[..(stem.LastIndexOf('/') + 1)];
            var aps = world.Find(stem + ".aps");
            if (aps == null)
            {
                var nearby = world.Entries.Where(e => e.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                    && !e.Path[folder.Length..].Contains('/') && e.Path.EndsWith(".aps", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (nearby.Length == 1) aps = nearby[0];
            }
            if (aps == null) throw new InvalidDataException("no unambiguous shop animation " + stem);
            byte[] Sibling(string name) => world.Find(folder + name) is { } e ? world.Read(e) : null;
            var ride = sim.Add(1, stem, entrance, 1, 1, world.Read(script), new Animation(world.Read(aps)), 1,
                               entrance, exit, out var fault, sibling: Sibling, definition: definition)
                       ?? throw new InvalidOperationException(fault);
            sim.SetOpen(1, true); ride.Set("VAR_BROKEN", 0);
            var visitors = new ParkVisitors(sim, new GuestWalk(paths))
                { Needs = new VisitorNeeds(731) { SecondsPerRise = 1_000_000 } };
            foreach (string key in visitors.Needs.Rates.Keys.ToArray()) visitors.Needs.Rates[key] = new VisitorNeeds.Rate(0,0,false);
            var guest = visitors.Arrive(entrance, entrance);
            visitors.Needs.Set(guest.Id, Initial());
            if (!visitors.SendTo(guest, ride)) throw new InvalidOperationException("shop fixture cannot route");
            visitors.Step(0, null);
            // Transaction-time affordability, after routing: future route filtering must not
            // invalidate the consumer guard. Stop on physical handback, even if payment fails.
            visitors.Needs.Set(guest.Id, Initial(cash));
            for (int tick = 0; tick < 6000 && visitors.Rides == 0; tick++) visitors.Step(.04, null);
            sim.SetOpen(1, false);
            return (visitors, visitors.Needs.Of(guest.Id), guest.Id);
        }
        var pathCompiled = new CompiledAssets(new AssetResourceDatabase(data.Read(data.Find("/arsdb.dba"))),
                                              TextDatabase.Load(data, "eur"));
        foreach (bool bare in new[] { false, true })
        {
            var shaped = Definition(named.Stem, bare);
            Check(pathCompiled.Attach(new[] { shaped }, out var shapeReport) == 1
                  && shaped.Compiled == pathCompiled.For(worldName, named.Stem + ".sam")?.Shop
                  && shaped.HappinessEffect == 10 && shaped.PricePerUse == named.Price,
                  $"{(bare ? "bare-world" : "archive-qualified")} source path attaches the named shop independently of region loop ({shapeReport})");
        }
        var arms = new VisitorNeeds(419);
        arms.Set(1, Initial()); arms.Set(2, Initial());
        Check(arms.Buy(1, 30, 11, 17, 5, 3, 0) && arms.Buy(2, 30, 11, 17, 5, 3, 1),
              "identical-input food and drink controls both transact");
        var foodControl = arms.Of(1); var drinkControl = arms.Of(2);
        Check(foodControl.Hunger == 69 && foodControl.Thirst == 87 && foodControl.Toilet == 21
              && drinkControl.Hunger == 91 && drinkControl.Thirst == 53 && drinkControl.Toilet == 27,
              "product alone reverses the transfer and selects its own bladder amount");
        foreach (string region in new[] { "eur", "usa", "jap" })
        {
            string dbName = region switch { "usa" => "/arsusdb.dba", "jap" => "/arsjapdb.dba", _ => "/arsdb.dba" };
            var db = new AssetResourceDatabase(data.Read(data.Find(dbName) ?? throw new InvalidDataException(dbName)));
            var text = TextDatabase.Load(data, region) ?? throw new InvalidDataException("missing text " + region);
            var compiled = new CompiledAssets(db, text);
            var a = Definition(named.Stem); var ice = Definition(iceStem);
            var drink = Definition(drinkCase.Stem);
            var fries = Definition(friesCase.Stem);
            var costume = Definition(costumeCase.Stem);
            Check(compiled.For(worldName, named.Stem + ".sam") is { } row && row.Key == named.Key && row.Shop != null,
                  $"{region} named shop resolves to key {named.Key}, not a matching effect profile");
            Check(compiled.For(worldName, iceStem + ".sam") is { } iceRow && iceRow.Key == iceKey && iceRow.Shop != null,
                  $"{region} named ice cream resolves to key {iceKey}");
            Check(a.Int("UsageInfo.HappinessEffect") == named.Authored,
                  $"{region} authored control remains {named.Authored} before attachment");
            Check(compiled.Attach(new[] { a, ice, drink, fries, costume }, out var report) == 5 && a.Compiled != null && ice.Compiled != null
                  && report.StartsWith("5 of 5", StringComparison.Ordinal), $"{region} all five definitions are actually attached");
            Check(a.Compiled.Product == 3 && a.HappinessEffect == 10 && a.PricePerUse == named.Price && a.CostOfGoods == named.Cost,
                  $"{region} named shop consumes compiled 10 and price/cost {named.Price}/{named.Cost}");
            int hunger = region == "usa" ? 15 : 25, vomit = region == "usa" ? 15 : 10;
            Check(ice.Compiled.Product == 4 && ice.HungerEffect == hunger && ice.VomitEffect == vomit && ice.ThirstEffect == 5
                  && ice.HappinessEffect == 5 && ice.PricePerUse == 30,
                  $"{region} ice cream keeps its regional hunger {hunger}/vomit {vomit}");
            var bought = Purchase(named.Stem, a);
            Check(bought.Visitors.Purchases == 1 && bought.Visitors.Rides == 1 && bought.Visitors.Boardings == 1,
                  $"{region} named shop genuinely completes one scripted purchase");
            Check(bought.After.Happiness == 30 && bought.After.Cash == 1234 - 10 * named.Price,
                  $"{region} purchase spends ten times compiled price and awards initial-quality base10, not authored happiness");
            Check(bought.After.Hunger == 80 && bought.After.Thirst == 70 && bought.After.Toilet == 10
                  && bought.After.Sick == 20 && bought.After.Litter == 9 && bought.After.PreferredIntensity == 90,
                  $"{region} named purchase preserves unrelated needs and preference");
            // ⭐⭐ AND THE PARK GETS PAID, which it did not until now -- the guest's money was
            // debited and simply vanished. `FUN_001D18E8` credits the MARGIN, not the price:
            // the cost of goods is a real cost, so a 30 sale on a 20 base earns the park 100 in
            // the same x10 units the guest is charged in.
            var shopRecord = a.Compiled ?? throw new InvalidDataException("named shop lost its compiled record");
            int margin = (named.Price - shopRecord.BaseCostOfGoods) * 10;
            // ⚠ A DELTA, not the absolute. The park now opens at ParkFinances.OpeningBalance,
            // and asserting the total made this a test of the starting figure as much as of the
            // credit -- it failed the moment that figure stopped being zero, which is the tell
            // that it was measuring the wrong thing.
            int earned = bought.Visitors.Sim.Finances.Balance - ParkFinances.OpeningBalance;
            Check(earned == margin,
                  $"{region} the park earns the MARGIN, not the price ({earned} for a {named.Price} sale on a {shopRecord.BaseCostOfGoods} base)");
            // ⚠ THE CONTROL THAT STOPS THIS BEING A TAUTOLOGY: the guest's x10 debit and the
            // park's x10 credit are read from different functions, and if they had been read in
            // different units this would be the line that noticed. The park must earn strictly
            // LESS than the guest paid, and more than nothing.
            Check(margin > 0 && margin < 10 * named.Price,
                  $"{region} and it is strictly between nothing and what the guest paid ({margin} of {10 * named.Price})");
            var till = bought.Visitors.Sim.Rides[0];
            Check(till.Takings == named.Price && till.Profit == named.Price - shopRecord.BaseCostOfGoods,
                  $"{region} the shop books gross {till.Takings} and margin {till.Profit} separately");
            var dessert = Purchase(iceStem, ice);
            Check(dessert.Visitors.Purchases == 1 && dessert.Visitors.Rides == 1 && dessert.Visitors.Boardings == 1,
                  $"{region} ice cream genuinely completes one scripted purchase");
            Check(dessert.After.Hunger == 80 - hunger && dessert.After.Toilet == 10 + hunger
                  && dessert.After.Sick == 20 + vomit && dessert.After.Thirst == 75
                  && dessert.After.Happiness == 25 && dessert.After.Cash == 934
                  && dessert.After.Litter is >= 39 and <= 63 && dessert.After.PreferredIntensity == 90,
                  $"{region} real handback applies that region's hunger/toilet/vomit arithmetic");
            for (int tick = 0; tick < 50; tick++) dessert.Visitors.Step(.04, null);
            Check(dessert.Visitors.Purchases == 1 && dessert.Visitors.Needs.Of(dessert.Id).Equals(dessert.After),
                  $"{region} later ticks do not repeat or reseed the purchase");
            Check(compiled.For(worldName, drinkCase.Stem + ".sam")?.Key == drinkCase.Key
                  && drink.Compiled?.Product == 1 && drink.ThirstEffect == 40 && drink.HungerEffect == 0
                  && drink.VomitEffect == 10 && drink.HappinessEffect == 5 && drink.PricePerUse == drinkCase.Price,
                  $"{region} named drink retains key/product/amounts, not an effect-profile guess");
            var beverage = Purchase(drinkCase.Stem, drink);
            Check(beverage.Visitors.Purchases == 1 && beverage.Visitors.Rides == 1
                  && beverage.After.Cash == 1234 - 10 * drinkCase.Price && beverage.After.Thirst == 30
                  && beverage.After.Hunger == 80 && beverage.After.Toilet == 50 && beverage.After.Sick == 30
                  && beverage.After.Happiness == 25 && beverage.After.Litter is >= 39 and <= 63,
                  $"{region} real drink handback lowers thirst and raises toilet by its own reduction");
            Check(compiled.For(worldName, friesCase.Stem + ".sam")?.Key == friesCase.Key
                  && fries.Compiled?.Product == 7 && fries.HungerEffect == friesCase.H && fries.ThirstEffect == 0
                  && fries.VomitEffect == friesCase.V && fries.PricePerUse == friesCase.Price,
                  $"{region} named fries retain product7 and their own compiled amounts");
            var chips = Purchase(friesCase.Stem, fries);
            Check(chips.Visitors.Purchases == 1 && chips.Visitors.Rides == 1
                  && chips.After.Cash == 1234 - 10 * friesCase.Price && chips.After.Hunger == 80 - friesCase.H
                  && chips.After.Toilet == 10 + friesCase.H && chips.After.Sick == 20 + friesCase.V
                  && chips.After.Thirst == 70 && chips.After.Happiness == 25 && chips.After.Litter is >= 39 and <= 63,
                  $"{region} product7 falls through to all food effects at initial q2 zero");
            Check(compiled.For(worldName, costumeCase.Stem + ".sam")?.Key == costumeCase.Key
                  && costume.Compiled?.Product == 2 && costume.HappinessEffect == 15 && costume.PricePerUse == costumeCase.Price,
                  $"{region} named costume resolves its real product and price");
            var dressed = Purchase(costumeCase.Stem, costume);
            var expectedCostume = Initial(1234 - 10 * costumeCase.Price);
            expectedCostume.Happiness = 35; expectedCostume.PreferredIntensity = 14;
            Check(dressed.Visitors.Purchases == 1 && dressed.Visitors.Rides == 1 && dressed.After.Equals(expectedCostume),
                  $"{region} costume handback changes preference to14 without reseeding or food effects");
            var refused = Purchase(iceStem, ice, 299);
            Check(refused.Visitors.Rides == 1 && refused.Visitors.Purchases == 0 && refused.After.Equals(Initial(299)),
                  $"{region} 299 cash refuses the 300-unit sale with no debit or effects at real handback");
            var exact = Purchase(iceStem, ice, 300);
            Check(exact.Visitors.Rides == 1 && exact.Visitors.Purchases == 1 && exact.After.Cash == 0
                  && exact.After.Hunger == 80 - hunger && exact.After.Thirst == 75,
                  $"{region} exactly300 cash buys once rather than being rejected at the boundary");
            string missing = "/Shops/__not_on_disc__/missing.sam";
            Check(compiled.For(worldName, missing) == null && compiled.Report(worldName, new[] { missing }).Contains("MISSED", StringComparison.Ordinal),
                  $"{region} missing identity is reported, not matched by a profile");
        }
    }
}
