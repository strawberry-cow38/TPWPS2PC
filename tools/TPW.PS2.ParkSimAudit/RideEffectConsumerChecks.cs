using TPW.PS2.Data;

/// <summary>Original 0x20EDD8 consumer: sickness only when the ride value is at least56.
/// Cases are literal reference outputs, not a restatement of the port's float expression.
/// The numeric ride input's per-ride sourcing remains separate from these effect semantics.</summary>
static class RideEffectConsumerChecks
{
    public static void Run(Model terrain, Action<bool, string> check)
    {
        void Check(bool ok, string text) => check(ok, "ride effect consumer: " + text);
        var paths = new ParkPaths(terrain);
        var defaults = new ParkVisitors(new ParkSim(paths), new GuestWalk(paths));
        Check(defaults.RideHappiness == 15 && defaults.RideSickScale == 1212f / 4096f && defaults.RideBoredomScale == 1f,
              "defaults match the three original executable image constants");
        var cases = new (int Value, byte Before, byte After)[]
        {
            (0,20,20), (26,1,1), (29,20,20), (30,20,20), (45,20,20), (55,20,20),
            (56,20,27), (57,20,27), (58,20,28), (100,20,40), (56,99,100),
        };
        foreach (var c in cases)
        {
            var needs = new VisitorNeeds(42);
            needs.Set(1, new VisitorWants { Happiness = 20, Sick = c.Before, Unknown78 = 90, Boredom = 50,
                Hunger = 17, Thirst = 19, Toilet = 31, Litter = 9, Cash = 1234, Thought = Thought.Good });
            needs.Ride(1, c.Value, defaults.RideHappiness, defaults.RideSickScale, defaults.RideBoredomScale);
            var after = needs.Of(1);
            Check(after.Sick == c.After, $"value {c.Value}, sickness {c.Before} becomes {c.After} (actual {after.Sick})");
            Check(after.Happiness == 35 && after.Unknown78 == Math.Max(0, 90 - c.Value)
                  && after.Cash == 1234 && after.Hunger == 17 && after.Thirst == 19 && after.Toilet == 31
                  && after.Litter == 9 && after.Thought == Thought.Good,
                  $"value {c.Value} gates sickness only, without reseeding unrelated needs");
        }
        // Both signs of mismatch and each boundary, with a fallback that cannot accidentally
        // equal any of the real bands. These records deliberately exercise nonzero preference.
        foreach (var c in new (byte Preferred, int Value, int Gain)[]
                 { (30,50,15), (30,51,10), (30,80,10), (30,81,5),
                   (90,70,15), (90,69,10), (90,40,10), (90,39,5), (50,50,15) })
        {
            var needs = new VisitorNeeds(42);
            needs.Set(1, new VisitorWants { Happiness = 20, PreferredIntensity = c.Preferred, Cash = 1234 });
            needs.Ride(1, c.Value, 7, defaults.RideSickScale, defaults.RideBoredomScale);
            var after = needs.Of(1);
            Check(after.Happiness == 20 + c.Gain && after.PreferredIntensity == c.Preferred && after.Cash == 1234,
                  $"preference {c.Preferred}, value {c.Value} awards band {c.Gain}, not fallback7");
        }
        var empty = new VisitorNeeds(42); empty.Ride(99, 56, 15, 1212f/4096f, 1f);
        Check(!empty.Has(99), "an effect does not mint a missing guest record");
    }
}
