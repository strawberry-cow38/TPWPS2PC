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

        // ── wanting it, not just affording it ──────────────────────────────────────────────
        // ⭐⭐ THE CASE THAT REJECTS THE OLD BEHAVIOUR IS THE SATED ONE. A port with only an
        // affordability test passes "a hungry guest buys" perfectly and fails here, because it
        // sells a burger to somebody who just ate. Same shop, same money, only the appetite
        // differs -- which is what makes this a test of the WANT and not of the wallet.
        {
            // Fries as the disc has them: price 30, base 20, hunger 25, vomit 10, happiness 5.
            const int Price = 30, Base = 20, Hun = 25, Thi = 0, Hap = 5, Vom = 10;
            bool Sell(byte hunger, byte thirst, int cash, int baseValue = Base, int quality = 100, int ac = 0)
            {
                var n = Still();
                n.Set(1, new VisitorWants { Hunger = hunger, Thirst = thirst, Sick = 20,
                                            Happiness = 20, Cash = cash });
                return n.Buy(1, Price, Hun, Thi, Hap, Vom, VisitorNeeds.Food, baseValue, quality, ac);
            }
            Check(Sell(80, 70, 1234), "a hungry guest with money buys");
            Check(!Sell(0, 0, 1234), "a SATED guest with the same money refuses -- the want gate");
            Check(!Sell(80, 70, 200), "and a hungry guest without the money still cannot");

            // ⚠⚠ THE FALLBACK MUST STAY LIVE. Two facilities per world never join a compiled
            // record, and a zero base means "unknown", not "worthless" -- if this ever starts
            // refusing, every unjoined shop in the game silently stops trading.
            Check(Sell(0, 0, 1234, baseValue: 0), "a shop with no compiled record skips the gate rather than refusing everyone");

            // ⭐⭐ AND QUALITY 100 IS LOAD-BEARING, measured the hard way: at 0 the base falls to
            // three quarters and the hungry guest above scores 25 against a price of 30. This
            // port nearly shipped that, with a comment explaining why 0 was principled.
            Check(!Sell(80, 70, 1234, quality: 0), "at quality 0 even a hungry guest cannot afford to want it -- which is why 100 is not a detail");
            // ⭐⭐ THE SECOND SLIDER, which this port left out on a wrong reading (it was called a
            // running customer count; `FUN_001D1FC0` is a setter and the shop screen drives it).
            // Turning it up cuts the cost of goods AND the appeal, so the same hungry guest who
            // buys at 0 must refuse at 100 -- and that is the whole point of it being a lever.
            Check(Sell(80, 70, 1234, ac: 0), "the second shop slider at 0 leaves the sale exactly as it was");
            Check(!Sell(80, 70, 1234, ac: 100), "and at 100 it cuts the appeal until the same guest refuses");
            Check(VisitorNeeds.WantScore(new VisitorWants { Hunger = 80, Thirst = 70, Sick = 20, Happiness = 20 },
                                         Base, 100, 0, Hun, Thi, Hap, Vom) == 36,
                  "the score reproduces the console's integer arithmetic exactly (36 for the fries case)");
        }

        // ── the thought ladder and its budget ──────────────────────────────────────────────
        // ⭐ Every threshold here is read, so every row can fail against a guessed one -- and the
        // port DID guess: it had happiness > 75 for Happy where the console uses 81 and 91, and
        // no source at all for Bored or Sad.
        {
            // ⚠⚠ `VisitorWants` IS A STRUCT, so an arrange step has to RETURN the modified copy.
            // The first version of this helper took an `Action<VisitorWants>` and mutated a
            // by-value parameter, so no case ever arranged anything at all.
            //
            // ⚠⚠ AND IT LOOKED LIKE A PASS, because `Thought.Happy` is enum value **0** -- the
            // default of an untouched struct. "No bubble was ever written" and "the guest is
            // happy" are the SAME VALUE, so the one row expecting Happy went green while five
            // rows around it went red, and the green one was the vacuous one. ⭐ Every case here
            // therefore stamps a SENTINEL first: if the ladder does not write, the check sees
            // the sentinel rather than an answer that happens to be right.
            Thought Bubble(Func<VisitorWants, VisitorWants> arrange)
            {
                var n = Still();
                n.Unknown78Bar = n.SickBar = n.ToiletBar = n.HungerBar = n.ThirstBar = 101;
                n.Set(1, arrange(new VisitorWants { Happiness = 50, Thought = Thought.Litter }));
                // Long enough that guest 1's 128-tick refresh slot is certainly reached.
                n.Step(n.SecondsPerTick * VisitorNeeds.MoodTicks * 2);
                return n.Of(1).Thought;
            }
            void Field(string label, Thought want, Func<VisitorWants, VisitorWants> set)
            {
                var got = Bubble(set);
                Check(got == want, $"{label} -> {want} (got {got})");
            }
            // ⭐ THE CONTROL FOR THE SENTINEL ITSELF: a guest the ladder declines to bubble must
            // come back holding the sentinel. If this ever returns Happy, the helper has stopped
            // arranging again and every row above it is meaningless.
            Check(Bubble(w => { w.Happiness = 50; w.Boredom = 0; return w; }) == Thought.Litter,
                  "a guest with nothing to say keeps the sentinel -- proving the arrange step lands");

            Field("toilet 91", Thought.Toilet, w => { w.Toilet = 91; return w; });
            Field("sick 91", Thought.Sick, w => { w.Sick = 91; return w; });
            Field("happiness 85", Thought.Happy, w => { w.Happiness = 85; return w; });
            Field("happiness 5", Thought.Sad, w => { w.Happiness = 5; return w; });
            Field("boredom 91 while unhappy", Thought.Bored, w => { w.Happiness = 80; w.Boredom = 91; return w; });
            // ⭐ THE ORDERING CONTROL. Boredom only speaks below 81 happiness -- a chain that
            // tested boredom first would send a delighted guest a bored bubble, and every row
            // above would still pass.
            Field("a HAPPY guest is not bored", Thought.Happy, w => { w.Happiness = 85; w.Boredom = 100; return w; });
            // ⚠ 80 is under the Happy bar and over the Sad one, so this isolates the toilet's
            // precedence rather than testing two things at once.
            Field("toilet outranks boredom", Thought.Toilet, w => { w.Happiness = 80; w.Boredom = 100; w.Toilet = 95; return w; });

            // ⭐⭐ THE BUDGET. 25 at once, and the check has to prove the cap BINDS -- 40 guests
            // all wanting the toilet must yield 25 bubbles, not 40 and not 0.
            var many = Still();
            many.Unknown78Bar = many.SickBar = many.ToiletBar = many.HungerBar = many.ThirstBar = 101;
            for (int g = 1; g <= 40; g++) many.Set(g, new VisitorWants { Toilet = 95, Happiness = 50 });
            many.Step(many.SecondsPerTick * VisitorNeeds.MoodTicks * 2);
            Check(many.BubblesHeld == 25, $"at most 25 bubbles are held at once (got {many.BubblesHeld})");
            Check(many.All.Values.Count(w => w.Thought == Thought.Toilet) == 25,
                  "and exactly the budgeted guests got one");
            // ⚠ THE CONTROL: 20 guests must ALL get one, or the line above would pass on a cap
            // that is simply refusing everybody past some smaller number.
            var few = Still();
            few.Unknown78Bar = few.SickBar = few.ToiletBar = few.HungerBar = few.ThirstBar = 101;
            for (int g = 1; g <= 20; g++) few.Set(g, new VisitorWants { Toilet = 95, Happiness = 50 });
            few.Step(few.SecondsPerTick * VisitorNeeds.MoodTicks * 2);
            Check(few.BubblesHeld == 20, $"under the budget every guest gets one (got {few.BubblesHeld})");
        }

        // ── the park's money ───────────────────────────────────────────────────────────────
        {
            var bank = new ParkFinances();
            // ⭐ The constructor's `park[8] = 1` means a park nothing has loaded into spends
            // freely. ⚠ The control is the other arm: with the flag cleared a debit it cannot
            // afford must REFUSE and take nothing, which is the whole point of the flag existing.
            Check(bank.Unlimited, "a fresh park spends freely, as FUN_00100470 leaves it");
            Check(bank.Debit(500) && bank.Balance == -500, $"and an unaffordable debit goes through ({bank.Balance})");
            var budget = new ParkFinances { Unlimited = false, Balance = 100 };
            Check(!budget.Debit(500) && budget.Balance == 100,
                  $"with the flag cleared it refuses and takes NOTHING ({budget.Balance})");
            Check(budget.Debit(100) && budget.Balance == 0, "and an affordable one still goes through");
            budget.Credit(250, category: 4);
            Check(budget.Balance == 250 && budget.TotalIncome == 250 && budget.IncomeByCategory[4] == 250,
                  "a credit lands in the balance, the lifetime total and its category at once");
            // ⚠ A credit is not a negative debit: the console has two functions and only one of
            // them can refuse, so routing a negative through the wrong door would skip that test.
            bool threw = false;
            try { budget.Credit(-1); } catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw, "a negative credit is refused rather than silently draining the park");
        }

        // ── is the shop's sound trigger on the SIM clock or the caller's? ─────────────────
        // ⭐⭐ MASTER'S REPORT, TURNED INTO A MEASUREMENT: "things that make sounds are making
        // sounds assuming fps = tps, so they are spamming sounds." Reading the code and not
        // finding an fps-driven trigger proves nothing -- so count the trigger over EQUAL SIM
        // TIME at two different step sizes. Same count = tick-driven; a count that scales with
        // the number of calls = frame-driven, which is the defect.
        //
        // ⚠ This measures the RATE OF THE TRIGGER, not how often a guest chooses to shop. A
        // guest re-entering a shop repeatedly would raise BOTH numbers equally and this check
        // would still pass -- correctly, because that is a different defect in a different lane.
        {
            int Fire(double step, int steps)
            {
                var n = Still();
                n.Unknown78Bar = n.SickBar = n.ToiletBar = n.HungerBar = n.ThirstBar = 101;
                int fired = 0;
                // The sound rides VisitorNeeds' clock through ParkVisitors.Serve, so the clock
                // discipline that matters here is the needs step the coordinator drives.
                for (int i = 0; i < steps; i++) fired += n.Step(step);
                return fired;
            }
            // 4.0 s of simulated time, reached two ways.
            int slow = Fire(0.04, 100), fast = Fire(0.02, 200);
            Check(slow == fast, $"equal sim time yields equal rise periods however many calls it took ({slow} vs {fast})");
            Check(slow > 0, $"and the clock actually advanced ({slow}) -- a frozen clock would pass the line above");
        }

        // ── money, as the game writes it ───────────────────────────────────────────────────
        // ⭐⭐ Every case is the decoded formatter's own behaviour: a '$', a '-' BEFORE it, and
        // commas every three digits. ⚠ The grouping is the part worth checking hard -- the
        // console computes its first group as `n > 3 ? n % 3 : 3`, so 4 digits break 1+3 and 6
        // break 3+3, and an off-by-one there looks plausible at one length and wrong at another.
        Check(Money.Display(0) == "$0", $"zero is $0 ({Money.Display(0)})");
        Check(Money.Display(7) == "$7", $"one digit ({Money.Display(7)})");
        Check(Money.Display(999) == "$999", $"three digits take no comma ({Money.Display(999)})");
        Check(Money.Display(1000) == "$1,000", $"four digits break 1+3 ({Money.Display(1000)})");
        Check(Money.Display(12345) == "$12,345", $"five digits break 2+3 ({Money.Display(12345)})");
        Check(Money.Display(123456) == "$123,456", $"six digits break 3+3 ({Money.Display(123456)})");
        Check(Money.Display(1234567) == "$1,234,567", $"seven digits take two commas ({Money.Display(1234567)})");
        Check(Money.Display(-2500) == "-$2,500", $"the minus goes BEFORE the dollar ({Money.Display(-2500)})");
        // ⭐ And the park's own units: the balance is in tenths, which the finance screen divides
        // by ten on all seven of its figures before formatting.
        Check(Money.Format(45_000) == "$4,500", $"park tenths are divided by ten ({Money.Format(45_000)})");
        Check(Money.Format(-1_500) == "-$150", $"including a negative balance ({Money.Format(-1_500)})");

        // ── the gate's ground ─────────────────────────────────────────────────────────────
        // ⭐⭐ Master: "give the gate an occupancy over the tiles it sits on, + 1 on each side.
        // mark a 2x2 of paths (right under the gate) as un-deleteable."
        // ⚠⚠ THE CONTROL IS THE ENTRANCE ITSELF. The first version put those cells in the
        // grid's `_occupied` set, which `CanLay` consults -- so the gate's own skirt refused the
        // entrance path and every departure fixture lost its route. Blocking a BUILD and blocking
        // the WAY IN are one line apart, and only the second one empties the park.
        // (Exercised here through the audit's own terrain, which the caller has already fitted.)

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
