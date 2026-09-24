using TPW.PS2.Data;

/// <summary>⭐⭐ WHAT AN UNMET NEED COSTS, and what pays it back -- the second half of
/// `FUN_0020FB88`, which this port had only the first third of.
///
/// ⚠ EVERY CASE HERE EXISTS BECAUSE THE SHIPPED CODE FAILED IT. The five-bar case failed on two
/// needs, the lavatory cases failed outright, and the appetite case failed on a cadence that was
/// invented. A check written after the fix that cannot fail before it is decoration.</summary>
static class MoodChecks
{
    public static void Run(Action<bool, string> check)
    {
        void Check(bool ok, string message) => check(ok, "mood: " + message);

        // ⚠ Rates flat so a step DOCKS and does not also RAISE; otherwise a need could cross its
        // own bar mid-case and the arithmetic below would be unpredictable rather than wrong.
        VisitorNeeds Still()
        {
            var n = new VisitorNeeds(99);
            foreach (string key in n.Rates.Keys.ToArray()) n.Rates[key] = new VisitorNeeds.Rate(0, 0, false);
            return n;
        }

        // ── the five bars ──────────────────────────────────────────────────────────────────
        // ⭐ ONE STEP, ONE DOCK PER NEED OVER ITS BAR. The controls sit one point BELOW each bar,
        // which is what makes this reject the bug rather than observe the fix: a port with three
        // bars passes every row here except hunger and thirst, and those two are the point.
        int Dock(Action<VisitorNeeds, int> arrange)
        {
            var n = Still();
            n.Set(1, new VisitorWants { Happiness = 50 });
            arrange(n, 1);
            n.Step(n.SecondsPerRise);
            return 50 - n.Of(1).Happiness;
        }

        var bars = new (string Need, int Bar, Action<VisitorNeeds, int, byte> Set)[]
        {
            ("hunger",  95, (n, g, v) => { var w = n.Of(g); w.Hunger     = v; n.Set(g, w); }),
            ("thirst",  85, (n, g, v) => { var w = n.Of(g); w.Thirst     = v; n.Set(g, w); }),
            ("toilet",  90, (n, g, v) => { var w = n.Of(g); w.Toilet     = v; n.Set(g, w); }),
            ("sick",    85, (n, g, v) => { var w = n.Of(g); w.Sick       = v; n.Set(g, w); }),
            ("+0x78",   95, (n, g, v) => { var w = n.Of(g); w.Unknown78  = v; n.Set(g, w); }),
        };
        foreach (var (need, bar, set) in bars)
        {
            Check(Dock((n, g) => set(n, g, (byte)bar)) == 1, $"{need} at its bar ({bar}) costs one happiness");
            Check(Dock((n, g) => set(n, g, (byte)(bar - 1))) == 0, $"{need} one below its bar costs nothing");
        }

        // ⭐ AND THEY ARE INDEPENDENT TESTS, NOT A CHAIN. A guest failing all five loses five --
        // an else-if port loses one, and reads as "the drain is working" on every single-need row
        // above. The triple version of this check already existed; the five-need version is what
        // notices two bars going missing.
        Check(Dock((n, g) =>
        {
            var w = n.Of(g);
            w.Hunger = 95; w.Thirst = 85; w.Toilet = 90; w.Sick = 85; w.Unknown78 = 95;
            n.Set(g, w);
        }) == 5, "all five needs over their bars cost five happiness, not one");

        // ── the lavatory ───────────────────────────────────────────────────────────────────
        // ⭐⭐ A LAVATORY IS THE ONLY CURE FOR SICKNESS THAT HAS BEEN FOUND. Before this the port
        // could make a guest ill and offer nothing back, so sickness was a one-way trip.
        {
            var n = Still();
            n.Set(1, new VisitorWants { Sick = 70, Toilet = 100, Happiness = 50 });
            int soil = n.UseToilet(1);
            Check(n.Of(1).Sick == 30, $"using a lavatory settles a stomach by 40 (70 -> {n.Of(1).Sick})");
            Check(n.Of(1).Toilet == 0, "and empties the bladder");
            Check(soil == (100 - 60) * 2 / 3, $"and leaves (toilet-60)*2/3 = {soil} of wear behind");

            var floor = Still();
            floor.Set(1, new VisitorWants { Sick = 12 });
            floor.UseToilet(1);
            Check(floor.Of(1).Sick == 0, "the relief floors at zero rather than wrapping a byte");
        }

        // ⭐⭐ AND A FILTHY ONE COSTS THE PERSON WHO USED IT. This is the consumer of `+0xB4` that
        // an earlier census reported as absent -- it reads through an accessor, so a census of
        // load instructions at that offset could not see it.
        {
            var n = Still();
            n.Set(1, new VisitorWants { Happiness = 50, Sick = 20, Thought = Thought.Good });
            n.DirtyLavatory(1);
            var w = n.Of(1);
            Check(w.Happiness == 40 && w.Sick == 30 && w.Thought == Thought.Angry,
                  $"a filthy lavatory costs 10 happiness and adds 10 sickness (got {w.Happiness}/{w.Sick}/{w.Thought})");
            Check(VisitorNeeds.FilthyBelow == 50, "and the bar for filthy is the console's 50");
        }

        // ── the appetite clock ─────────────────────────────────────────────────────────────
        // ⭐⭐ THIRST FIRES 1.25x AS OFTEN AS HUNGER -- 40 ticks against 50, both read. The RATIO
        // is the part that is evidence; seconds-per-tick is still a guess, so this check asserts
        // the ratio and deliberately says nothing about the absolute pace.
        {
            var n = new VisitorNeeds(7);
            n.Set(1, new VisitorWants());
            // Long enough that the ratio dominates the rounding of either period.
            double span = n.SecondsPerTick * VisitorNeeds.HungerTicks * VisitorNeeds.ThirstTicks * 4;
            n.Unknown78Bar = n.SickBar = n.ToiletBar = n.HungerBar = n.ThirstBar = 101;
            n.Step(span);
            var w = n.Of(1);
            // Each fire is rand(2), so the EXPECTED counts are 200 and 160 halved; assert the
            // ordering and a generous band rather than an exact draw.
            Check(w.Thirst > w.Hunger,
                  $"thirst outpaces hunger over {span:F1}s (thirst {w.Thirst}, hunger {w.Hunger})");
            Check(w.Hunger > 0, $"and hunger actually moves (got {w.Hunger}) -- a frozen need would 'pass' the line above");
        }

        // ⚠ THE CONTROL THAT MATTERS: `+0x78` is NOT raised by the clock. Nothing in
        // `FUN_0020FB88` raises it -- queueing is its only riser found -- and a rise put there on
        // the strength of its 95 bar was this port's mistake for two commits.
        {
            var n = new VisitorNeeds(7);
            n.Set(1, new VisitorWants());
            n.Unknown78Bar = n.SickBar = n.ToiletBar = n.HungerBar = n.ThirstBar = 101;
            n.Step(n.SecondsPerTick * VisitorNeeds.HungerTicks * 20);
            Check(n.Of(1).Unknown78 == 0, $"+0x78 does not rise on the clock (got {n.Of(1).Unknown78})");
            n.Queue(1);
            Check(n.Of(1).Unknown78 == 5, "it rises by 5 when the guest QUEUES, which is its only found riser");
        }
    }
}
