using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;

/// <summary>Disc-free owner tests. Intentionally not wired into Program here; no full-save claim.</summary>
public static class VisitorNeedsSaveChecks
{
    static string Json<T>(T value) => JsonSerializer.Serialize(value);
    static T Round<T>(T value) => JsonSerializer.Deserialize<T>(Json(value))!;

    public static void Run(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        void C(bool ok, string why) => check(ok, "visitor needs save: " + why);
        // Same live entries are not enough: a removal leaves a reusable enumeration slot.
        // Compare real public mutations to an independent stock Dictionary, checkpointing
        // frequently, then keep adding/removing and ticking both restored/original owners.
        var churn=new VisitorNeeds(21);var stock=new Dictionary<int,VisitorWants>();
        for(int id=1;id<=5;id++){var w=new VisitorWants{Hunger=(byte)id};churn.Set(id,w);stock[id]=w;}
        churn.Reconcile(new[]{2,4,5});stock.Remove(1);stock.Remove(3);
        var resumed=VisitorNeeds.FromState(Round(churn.CaptureState()));
        var operations=new Random(901);
        for(int operation=0;operation<350;operation++)
        {
            int id=operations.Next(-5,45);
            if(operation%4==0)
            {
                int[] keep=stock.Keys.Where(k=>k%3!=Math.Abs(id)%3).ToArray();
                churn.Reconcile(keep);resumed.Reconcile(keep);
                foreach(int key in stock.Keys.Where(k=>!keep.Contains(k)).ToArray())stock.Remove(key);
            }
            else
            {
                var w=new VisitorWants{Hunger=(byte)(operation%255),Cash=operation};
                churn.Set(id,w);resumed.Set(id,w);stock[id]=w;
            }
            C(churn.All.Keys.SequenceEqual(stock.Keys)&&resumed.All.Keys.SequenceEqual(stock.Keys),"stock map free-slot order after churn "+operation);
            C(Json(churn.CaptureState())==Json(resumed.CaptureState()),"complete state after churn "+operation);
            if(operation%17==0)resumed=VisitorNeeds.FromState(Round(resumed.CaptureState()));
        }
        // A field census tripwire, not a reflective serializer. New simulation fields need an
        // explicit schema/mapping decision even if they aren't visible through a UI readout.
        var wantsFields = new[] { "Happiness", "Sick", "Hunger", "Toilet", "Thirst", "Litter",
            "Unknown78", "Boredom", "Cash", "Thought", "PreferredIntensity" };
        C(typeof(VisitorWants).GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(f => f.Name).Order().SequenceEqual(wantsFields.Order()), "complete wants field census");
        var needsFields = new[] { "_sinceRise", "_sinceTick", "_tick", "_hungerTicks", "_thirstTicks",
            "_holdingBubble", "_byGuest", "_rng", "Sounded", "<Unknown78Bar>k__BackingField",
            "<SickBar>k__BackingField", "<ToiletBar>k__BackingField", "<HungerBar>k__BackingField",
            "<ThirstBar>k__BackingField", "<Rates>k__BackingField", "<SecondsPerRise>k__BackingField",
            "<SecondsPerTick>k__BackingField", "<BubbleBudget>k__BackingField", "<NearbyLitter>k__BackingField" };
        C(typeof(VisitorNeeds).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(f => f.Name).Order().SequenceEqual(needsFields.Order()), "complete needs field census");

        var defaults = new VisitorNeeds().CaptureState();
        C(defaults.Preferences.SequenceEqual(new byte[] { 90, 30, 50, 75, 100, 45, 60, 70 }), "compiled Preferences bytes");
        C(defaults.Rates.Select(r => $"{r.Key}:{r.Value.Base}:{r.Value.Spread}:{r.Value.High}")
            .SequenceEqual(new[] { "hunger:0:1:False", "thirst:0:1:False", "toilet:0:1:True", "sick:0:0:False" }),
            "default rate values and enumeration order unchanged");
        C(BitConverter.DoubleToInt64Bits(defaults.SecondsPerRise) == BitConverter.DoubleToInt64Bits(2.56) &&
            BitConverter.DoubleToInt64Bits(defaults.SecondsPerTick) == BitConverter.DoubleToInt64Bits(.04), "default cadence bits");
        C(Json(defaults) == Json(VisitorNeeds.FromState(Round(defaults)).CaptureState()), "empty/default roundtrip");
        foreach (int seed in new[] { 0, 19, -1, int.MinValue, int.MaxValue })
        {
            var expected = new Random(seed); var actual = new VisitorNeeds(seed);
            for (int i = 0; i < 80; i++)
            {
                int n = i + 2;
                C(actual.RollCentred(n) == (expected.Next(n) + expected.Next(n)) / 2, "seeded .NET centred compatibility");
                C(actual.RollHigh(n) == n - expected.Next(n) * expected.Next(n) / n, "seeded .NET high compatibility");
            }
        }

        // Preserve raw setters' byte/word semantics, not the Clamp applied by gameplay setters.
        var raw = new VisitorWants { Happiness = 255, Sick = 254, Hunger = 253, Toilet = 252,
            Thirst = 251, Litter = 250, Unknown78 = 249, Boredom = 248, Cash = int.MinValue,
            Thought = Thought.BadQueue, PreferredIntensity = 247 };
        C(Json(raw.CaptureState()) == Json(VisitorWants.FromState(Round(raw.CaptureState())).CaptureState()),
            "all raw wants values preserved without clamping/wrapping cash");
        var rawOwner = new VisitorNeeds { BubbleBudget = -1, Unknown78Bar = -10, HungerBar = int.MaxValue };
        rawOwner.Set(int.MinValue, raw); rawOwner.Set(0, raw); rawOwner.Set(int.MaxValue, raw);
        rawOwner.Rates.Clear(); rawOwner.Rates.Add("custom", null!); rawOwner.Rates.Add("", new(255, 254, true));
        C(Json(rawOwner.CaptureState()) == Json(VisitorNeeds.FromState(Round(rawOwner.CaptureState())).CaptureState()),
            "integer IDs, signed overrides, empty/custom/missing/null rates stay exact");
        var signedZero = defaults with { SinceRise = -0d, SinceTick = -0d };
        var zero = VisitorNeeds.FromState(Round(signedZero)).CaptureState();
        C(BitConverter.DoubleToInt64Bits(zero.SinceRise) == BitConverter.DoubleToInt64Bits(-0d) &&
            BitConverter.DoubleToInt64Bits(zero.SinceTick) == BitConverter.DoubleToInt64Bits(-0d), "signed zero clock bits");

        var eventsA = new List<string>(); var eventsB = new List<string>();
        int resolutions = 0;
        VisitorNeeds.StateBindings Bind(List<string> events) => new()
        {
            IdentifySounded = _ => "needs/sounds/v1", IdentifyNearbyLitter = _ => "park/litter/v1",
            ResolveSounded = id => { resolutions++; return id == "needs/sounds/v1" ?
                (g, sound) => events.Add($"sound:{g}:{sound}") : null!; },
            ResolveNearbyLitter = id => { resolutions++; return id == "park/litter/v1" ?
                g => { events.Add($"litter:{g}"); return (g % 7 == 0 ? 1 : 0, g % 11 == 0 ? 1 : 0); } : null!; }
        };
        var bindingsA = Bind(eventsA); var bindingsB = Bind(eventsB);
        var a = new VisitorNeeds(8675309) { BubbleBudget = 3, SecondsPerRise = .73 };
        a.Sounded = bindingsA.ResolveSounded("needs/sounds/v1");
        a.NearbyLitter = bindingsA.ResolveNearbyLitter("park/litter/v1");
        foreach (int id in Enumerable.Range(0, 36).Reverse())
        {
            var w = a.Spawn(id); // Real activation of this ID owner, not invented guest refs.
            w.Happiness = (byte)(id % 3 == 0 ? 95 : id % 3 == 1 ? 5 : 60);
            w.Hunger = 96; w.Thirst = 97; w.Toilet = (byte)(id % 4 == 0 ? 95 : 0);
            w.Sick = (byte)(id % 5 == 0 ? 96 : 0); w.Boredom = 97;
            a.Set(id, w);
        }
        a.Step(1.997); // tick49, hunger49/thirst9, both clocks retain fractional carries.
        var saved = Round(a.CaptureState(bindingsA));
        C(saved.Tick == 49 && saved.HungerTicks == 49 && saved.ThirstTicks == 9 &&
            saved.SinceRise > 0 && saved.SinceTick > 0, "checkpoint is mid-cadence, not an inert origin");
        C(a.BubblesHeld == 3 && saved.Guests.Count(g => g.Wants.Thought != Thought.Normal) >= 3,
            "real ticks consumed the bubble budget");
        string sourceBefore = Json(a.CaptureState(bindingsA));
        eventsA.Clear(); eventsB.Clear(); resolutions = 0;
        var b = VisitorNeeds.FromState(saved, bindingsB);
        C(eventsA.Count == 0 && eventsB.Count == 0 && resolutions == 2, "pure binding only, no effects on hydration");
        C(sourceBefore == Json(a.CaptureState(bindingsA)) && sourceBefore == Json(b.CaptureState(bindingsB)),
            "JSON copies every field without touching original");
        C(!ReferenceEquals(a.All, b.All) && !ReferenceEquals(a.Rates, b.Rates) &&
            !ReferenceEquals(a.Rates["hunger"], b.Rates["hunger"]), "fresh ownership including rate records");

        int totalRises = 0, purchases = 0, soil = 0;
        string firstRandom = Json(saved.Random);
        for (int step = 0; step < 160; step++)
        {
            var receiptsA = new List<string>(); var receiptsB = new List<string>();
            void Inputs(VisitorNeeds owner, List<string> receipts)
            {
                int id = step % 36;
                if (step % 9 == 0)
                {
                    receipts.Add("spawn:" + Json(owner.Spawn(1000 + step).CaptureState()));
                    // Newly activated guests get known urgent needs; decisions really take slots.
                    var w = owner.Of(1000 + step); w.Toilet = 100; w.Happiness = 95;
                    owner.Set(1000 + step, w); owner.Decide(1000 + step, true, true, true);
                }
                if (step % 5 == 0) receipts.Add("buy:" + owner.Buy(id, 3, 35, 28, 9, 2,
                    step % 10 == 0 ? VisitorNeeds.Food : VisitorNeeds.Drink));
                if (step % 7 == 0) receipts.Add("toilet:" + owner.UseToilet(id));
                if (step % 11 == 0) owner.Ride(id, 70, 15, 1212f / 4096, 1);
                if (step % 13 == 0) owner.DirtyLavatory(id);
                if (step % 3 == 0) owner.Queue(id);
                owner.Decide(id, true, true, true);
                receipts.Add("home:" + owner.WantsToGoHome(id));
                if (step == 70)
                {
                    // Removal frees bubbles and integer IDs may be reused (no object remapping).
                    receipts.Add("gone:" + owner.Reconcile(owner.All.Keys.Where(g => g < 1000).ToArray()));
                    foreach (int recycled in Enumerable.Range(0, 8).Select(i => 1000 + i * 9)) owner.Spawn(recycled);
                }
            }
            Inputs(a, receiptsA); Inputs(b, receiptsB);
            C(receiptsA.SequenceEqual(receiptsB), "activation/satisfaction results " + step);
            purchases += receiptsA.Count(s => s == "buy:True");
            soil += receiptsA.Where(s => s.StartsWith("toilet:")).Sum(s => int.Parse(s[7..]));
            double seconds = new[] { .013, .027, .11, .04, .8, 1.597, 0d }[step % 7];
            int risesA = a.Step(seconds), risesB = b.Step(seconds); totalRises += risesA;
            C(risesA == risesB && Json(a.CaptureState(bindingsA)) == Json(b.CaptureState(bindingsB)),
                "all DTO fields + RNG + rise return after independent tick " + step);
            C(eventsA.SequenceEqual(eventsB), "sound/litter event ordering " + step);
            C(a.All.Keys.All(g => a.ThoughtOf(g) == b.ThoughtOf(g)), "budget visibility " + step);
        }
        C(totalRises > 30 && purchases > 20 && soil > 0 && eventsA.Any(s => s.StartsWith("sound:")) &&
            eventsA.Any(s => s.StartsWith("litter:")) && Json(a.CaptureState(bindingsA).Random) != firstRandom,
            "non-inert ticks, purchases, relief, callbacks and RNG controls");
        string unchangedA = Json(a.CaptureState(bindingsA));
        b.Step(.827); b.Spawn(-27); b.Rates["hunger"] = new(55, 2, true);
        C(unchangedA == Json(a.CaptureState(bindingsA)), "mutating restored owner never changes original");
        string unchangedB = Json(b.CaptureState(bindingsB));
        a.UseToilet(1); a.Rates["toilet"] = new(7, 3, false);
        C(unchangedB == Json(b.CaptureState(bindingsB)), "mutating original never changes restored owner");
        var detached = Round(saved); var fresh = VisitorNeeds.FromState(detached, bindingsB);
        string freshBefore = Json(fresh.CaptureState(bindingsB));
        detached.Random.SeedArray[1] ^= 1; detached.Preferences[0] ^= 1;
        detached.HoldingBubble[0] = int.MinValue; detached.Guests[0] = detached.Guests[0] with { Id = -999 };
        detached.Rates[0] = detached.Rates[0] with { Value = null! };
        C(freshBefore == Json(fresh.CaptureState(bindingsB)), "no DTO arrays alias a restored owner");
        var fromCapture = fresh.CaptureState(bindingsB); fromCapture.Random.SeedArray[1] ^= 1;
        fromCapture.Guests[0] = fromCapture.Guests[1]; fromCapture.HoldingBubble[0] = -234;
        fromCapture.Preferences[0] ^= 1; fromCapture.Rates[0] = fromCapture.Rates[1];
        C(freshBefore == Json(fresh.CaptureState(bindingsB)), "capture arrays detached from live owner");

        // Counterfactuals would fail if the continuation comparison above were merely inert.
        var cadenceControl = VisitorNeeds.FromState(saved with { SinceTick = 0, HungerTicks = 0 }, bindingsB);
        var cadenceGood = VisitorNeeds.FromState(saved, bindingsB);
        cadenceControl.Step(.005); cadenceGood.Step(.005);
        C(cadenceControl.CaptureState(bindingsB).Tick != cadenceGood.CaptureState(bindingsB).Tick,
            "dropping partial tick changes next tick");
        var rngControl = VisitorNeeds.FromState(saved with { Random = new SnapshotRandom(8675309).CaptureState() }, bindingsB);
        C(Json(rngControl.Spawn(9000).CaptureState()) != Json(VisitorNeeds.FromState(saved, bindingsB).Spawn(9000).CaptureState()),
            "restoring seed instead of RNG state changes the next activation");
        var budgetControl = new VisitorNeeds { BubbleBudget = 0 }; var budgetGood = new VisitorNeeds { BubbleBudget = 2 };
        foreach (var owner in new[] { budgetControl, budgetGood })
        {
            owner.Set(0, new VisitorWants { Happiness = 95, Thought = Thought.Normal });
            owner.Step(.04);
        }
        C(budgetGood.ThoughtOf(0) == Thought.VeryUnhappy && budgetControl.ThoughtOf(0) == Thought.Normal,
            "budget control changes real mood publication");
        var satisfaction = new VisitorNeeds(3);
        satisfaction.Set(0, new VisitorWants { Hunger = 99, Thirst = 98, Toilet = 80, Sick = 50, Cash = 1000 });
        C(satisfaction.Buy(0, 1, 30, 0, 0, 0) && satisfaction.Of(0).Hunger == 69 && satisfaction.Of(0).Litter >= 30,
            "purchase really satisfies hunger and consumes RNG for litter");
        C(satisfaction.UseToilet(0) == 26 && satisfaction.Of(0).Toilet == 0 && satisfaction.Of(0).Sick == 10,
            "lavatory really satisfies needs");

        void Reject(VisitorNeeds.State bad, string why, bool beforeBindings = true)
        {
            string original = Json(a.CaptureState(bindingsA));
            byte[] prefs = (byte[])VisitorNeeds.Preferences.Clone();
            int priorResolutions = resolutions, priorEvents = eventsA.Count + eventsB.Count;
            bool rejected = false;
            try { _ = VisitorNeeds.FromState(bad, bindingsB); } catch (ArgumentException) { rejected = true; }
            C(rejected && original == Json(a.CaptureState(bindingsA)) && prefs.SequenceEqual(VisitorNeeds.Preferences) &&
                priorEvents == eventsA.Count + eventsB.Count, why + ": fails without source/global/effect mutation");
            if (beforeBindings) C(resolutions == priorResolutions, why + ": validation precedes external resolution");
        }
        Reject(null!, "null"); Reject(saved with { Version = 100 }, "future schema");
        Reject(saved with { Guests = null! }, "null guests"); Reject(saved with { HoldingBubble = null! }, "null slots");
        Reject(saved with { Rates = null! }, "null rates"); Reject(saved with { Preferences = null! }, "null Preferences");
        Reject(saved with { Random = null! }, "null RNG");
        Reject(saved with { Guests = new VisitorNeeds.GuestState[VisitorNeeds.StateGuestLimit + 1] }, "oversized guests");
        Reject(saved with { Rates = new VisitorNeeds.RateEntryState[VisitorNeeds.StateRateLimit + 1] }, "oversized rates");
        Reject(saved with { Guests = saved.Guests.Append(saved.Guests[0]).ToArray() }, "duplicate guest ID");
        Reject(saved with { HoldingBubble = new[] { -555 } }, "unknown bubble ID");
        Reject(saved with { HoldingBubble = new[] { saved.Guests[0].Id, saved.Guests[0].Id } }, "duplicate bubble ID");
        Reject(saved with { Guests = new[] { saved.Guests[0] with { Wants = null! } }, HoldingBubble = Array.Empty<int>() }, "null wants");
        Reject(saved with { Guests = new[] { saved.Guests[0] with { Wants = saved.Guests[0].Wants with { Version = 2 } } },
            HoldingBubble = Array.Empty<int>() }, "wants schema");
        Reject(saved with { Guests = new[] { saved.Guests[0] with { Wants = saved.Guests[0].Wants with { Thought = (Thought)16 } } },
            HoldingBubble = Array.Empty<int>() }, "invalid thought ID");
        Reject(saved with { Rates = new[] { saved.Rates[0], saved.Rates[0] } }, "duplicate rate key");
        Reject(saved with { Rates = new[] { saved.Rates[0] with { Key = null! } } }, "null rate key");
        Reject(saved with { SecondsPerRise = 0 }, "zero rise period"); Reject(saved with { SecondsPerTick = double.NaN }, "NaN tick");
        Reject(saved with { SinceRise = double.PositiveInfinity }, "infinite carry"); Reject(saved with { SinceTick = -1 }, "negative carry");
        Reject(saved with { Tick = -1 }, "negative tick"); Reject(saved with { HungerTicks = 50 }, "hunger phase overflow");
        Reject(saved with { ThirstTicks = -1 }, "negative thirst phase");
        Reject(saved with { SoundedId = " " }, "empty binding ID");
        Reject(saved with { NearbyLitterId = "unresolved" }, "unknown external binding ID", false);
        var badRandom = Round(saved); badRandom.Random.SeedArray[0] = 1; Reject(badRandom, "RNG words");
        Reject(saved with { Random = new SnapshotRandom.State { Version = 1, Inext = 0, Inextp = 21, SeedArray = new int[55] } }, "short RNG array");
        var differentTable = Round(saved); differentTable.Preferences[0] ^= 1; Reject(differentTable, "mismatched GAME table");
        bool missingBindings = false;
        try { a.CaptureState(); } catch (ArgumentException) { missingBindings = true; }
        C(missingBindings, "capture refuses to silently discard live callbacks");
        bool unresolvedBindings = false;
        try { VisitorNeeds.FromState(saved); } catch (ArgumentException) { unresolvedBindings = true; }
        C(unresolvedBindings, "restore requires external callback identity lookup");

        void Required<T>(T dto)
        {
            var node = JsonNode.Parse(Json(dto))!.AsObject();
            foreach (string key in node.Select(p => p.Key).ToArray())
            {
                var bad = node.DeepClone().AsObject(); bad.Remove(key); bool rejected = false;
                try { JsonSerializer.Deserialize<T>(bad.ToJsonString()); } catch (JsonException) { rejected = true; }
                C(rejected, typeof(T).FullName + " requires " + key);
            }
            node["UnknownMember"] = 1; bool unknown = false;
            try { JsonSerializer.Deserialize<T>(node.ToJsonString()); } catch (JsonException) { unknown = true; }
            C(unknown, typeof(T).FullName + " rejects unknown members");
        }
        Required(saved); Required(saved.Guests[0]); Required(saved.Guests[0].Wants);
        Required(saved.Rates[0]); Required(saved.Rates[0].Value);
        foreach (int invalidByte in new[] { -1, 256 })
        {
            var node = JsonNode.Parse(Json(raw.CaptureState()))!.AsObject(); node["Hunger"] = invalidByte;
            bool rejected = false;
            try { JsonSerializer.Deserialize<VisitorWants.State>(node.ToJsonString()); } catch (JsonException) { rejected = true; }
            C(rejected, "JSON byte narrowing never silently wraps " + invalidByte);
        }
        byte[] globalBefore = (byte[])VisitorNeeds.Preferences.Clone();
        try
        {
            VisitorNeeds.Preferences[0] = 255;
            var custom = new VisitorNeeds().CaptureState();
            var customOwner = VisitorNeeds.FromState(Round(custom));
            C(customOwner.Spawn(1).PreferredIntensity <= 255 && custom.Preferences[0] == 255 && VisitorNeeds.Preferences[0] == 255,
                "nondefault global table is validated, never normalized");
            Reject(saved, "global config changed since save");
        }
        finally { globalBefore.CopyTo(VisitorNeeds.Preferences, 0); }
        C(globalBefore.SequenceEqual(VisitorNeeds.Preferences), "default global bytes preserved");
    }
}
