using TPW.PS2.Data;

/// <summary>⭐⭐ DOES A WANT ACTUALLY GET ANSWERED? Everything else about visitor needs can look
/// wired up while the loop is dead: the need rises, the bubble appears, and the guest queues for a
/// rollercoaster and comes back needing it more. These run the whole errand -- rise, route, use,
/// relief -- against the game's OWN toilet script and .sam.
///
/// ⚠ EVERY CASE HERE MUST BE ABLE TO FAIL. A park containing one toilet relieves a guest even with
/// no routing at all, because <see cref="ParkVisitors.Idle"/>'s random fallback would pick it --
/// so "relieved" on its own proves nothing. The negatives are what give the positive its meaning:
/// a CLOSED toilet must leave the need alone, and a real RIDE must leave it alone, and the second
/// one is the case that fails if <see cref="ParkVisitors.Serve"/> ever dispatches by "did they
/// complete something" rather than by what the place is.</summary>
static class ServiceChecks
{
    const int Steps = 6000;
    const double Tick = 0.04;

    public static void Run(Model terrain, ParkPaths sourcePaths, ParkEntrance entranceTable,
                           ParkCell[] corridor, ParkCell exit,
                           byte[] toiletScript, Animation toiletAps, RideDefinition toiletDef, Func<string, byte[]> toiletSibling,
                           byte[] rideScript, Animation rideAps, RideDefinition rideDef, Func<string, byte[]> rideSibling, int rideSeats,
                           Action<bool, string> check)
    {
        void Check(bool ok, string message) => check(ok, "service: " + message);

        // The authored facts the rest of this rests on. If these ever stop holding, the cases
        // below would still "pass" while testing nothing.
        Check(toiletDef.ProvidesRelief, $"the fixture's own .sam marks it a lavatory ({toiletDef.Name})");
        Check(!rideDef.ProvidesRelief, $"the control ride's .sam does NOT ({rideDef.Name})");

        /// <summary>One park, one guest, one facility, run to completion.</summary>
        (int Toilet, int Relieved, int Boardings, int Completed, int Soil, int Condition) Visit(
            bool relief, bool open, byte startingNeed, int distractors = 0)
        {
            var paths = new ParkPaths(terrain);
            sourcePaths.Field.Cells.CopyTo(paths.Field.Cells, 0);
            // ⚠ A COPY HAS NO GATE. Field.Cells carries the laid path but not the entrance
            // registration, and without it `ParkVisitors.Gate` is null and NOBODY CAN LEAVE --
            // which would make the departure cases below pass while testing nothing at all.
            paths.SetEntrance(entranceTable);
            var visitors = new ParkVisitors(new ParkSim(paths), new GuestWalk(paths)) { Needs = new VisitorNeeds(4242) };
            // ⚠⚠ AND THE MOOD BARS PUT OUT OF REACH, for the same reason as the rates. An unmet
            // need now DOCKS HAPPINESS, so a guest held at toilet 100 to test service drains to
            // nothing, trips the go-home path and walks out -- and a departed guest's record
            // reads as zeros, which made the closed-lavatory control report "the need was
            // cleared". That is the drain working, arriving in a check about something else.
            visitors.Needs.Unknown78Bar = visitors.Needs.SickBar = visitors.Needs.ToiletBar =
                visitors.Needs.HungerBar = visitors.Needs.ThirstBar = 101;
            // ⚠ FROZEN ON PURPOSE. The need must be the one this case set, not that plus whatever
            // rose during the walk -- otherwise the mess arithmetic below is unpredictable and the
            // assertion would have to be loosened until it stopped saying anything.
            foreach (string key in visitors.Needs.Rates.Keys.ToArray())
                visitors.Needs.Rates[key] = new VisitorNeeds.Rate(0, 0, false);

            ParkRide Place(int id, ParkCell at, bool loo)
            {
                var r = visitors.Sim.Add(id, loo ? "fixture toilet" : "fixture ride", at, 1, 1,
                                         loo ? toiletScript : rideScript,
                                         loo ? toiletAps : rideAps,
                                         (loo ? toiletDef : rideDef).UpgradeCapacity(0) ?? 1,
                                         at, exit, out string fault,
                                         sibling: loo ? toiletSibling : rideSibling,
                                         headSlots: loo ? 0 : rideSeats,
                                         definition: loo ? toiletDef : rideDef)
                        ?? throw new InvalidOperationException("service fixture would not start: " + fault);
                visitors.Sim.SetOpen(r.Id, true);
                r.Set("VAR_BROKEN", 0);
                return r;
            }
            // ⚠ THE FACILITY UNDER TEST GOES FURTHEST AWAY. The guest starts at the far end of the
            // corridor, so corridor[0] is the longest walk and every distractor sits BETWEEN them
            // and it -- which is what makes the mixed case below mean something.
            var ride = Place(1, corridor[0], relief);
            visitors.Sim.SetOpen(ride.Id, open);
            for (int i = 0; i < distractors && i + 1 < corridor.Length; i++) Place(2 + i, corridor[i + 1], false);

            // ⚠ ON THE CORRIDOR, NOT AT THE MOUTH. A copied ParkPaths carries the laid path
            // cells but not the entrance registration, so the mouth is not walkable ground here --
            // and the far end of the corridor is a better start anyway: the guest has to actually
            // walk somewhere, which is the part being tested.
            var guest = visitors.Arrive(exit, exit);
            var w = visitors.Needs.Of(guest.Id);
            w.Toilet = startingNeed;
            // Nothing else may be urgent, or the guest has two errands and the one under test is
            // not necessarily the one they run.
            w.Hunger = 0; w.Thirst = 0; w.Sick = 0; w.Happiness = 50;
            // ⚠ SOLVENT ON PURPOSE. A guest under 100 cash now goes home, and a service case
            // whose guest wandered out of the park would fail for a reason that is not about
            // service at all.
            w.Cash = 5000;
            visitors.Needs.Set(guest.Id, w);

            for (int i = 0; i < Steps && visitors.Relieved == 0; i++) visitors.Step(Tick, () => exit);
            return (visitors.Needs.Of(guest.Id).Toilet, visitors.Relieved, visitors.Boardings, visitors.Rides,
                    visitors.Soil.TryGetValue(ride.Id, out var s) ? s : 0, ride.Condition);
        }

        // ── the case the feature exists for ────────────────────────────────────────────────────
        var relieved = Visit(relief: true, open: true, startingNeed: 100);
        Check(relieved.Boardings > 0, $"a bursting guest reaches the lavatory and goes in ({relieved.Boardings} boardings)");
        Check(relieved.Relieved == 1, $"they are served exactly once ({relieved.Relieved})");
        Check(relieved.Toilet == 0, $"and come out with the need answered (toilet {relieved.Toilet}, was 100)");
        // ⭐ (100-60)*2/3 = 26, from the decoded soil arithmetic -- asserted as the number rather
        // than "> 0", because "> 0" passes for any formula at all.
        Check(relieved.Soil == 26, $"the mess is the decoded (need-60)*2/3 = 26 ({relieved.Soil})");
        // ⭐ AND IT IS THE FACILITY THAT WORE DOWN, not a tally beside it: the console keeps a
        // condition at `+0xb4` that use depletes toward zero and a service call resets to 100.
        // Asserting both halves catches a `Soil` view that stops tracking the field it is a view of.
        Check(relieved.Condition == 74, $"the lavatory's own condition fell 100 -> 74 ({relieved.Condition})");

        // ── negative: nowhere to go ───────────────────────────────────────────────────────────
        var shut = Visit(relief: true, open: false, startingNeed: 100);
        Check(shut.Relieved == 0, $"a CLOSED lavatory serves nobody ({shut.Relieved})");
        Check(shut.Toilet == 100, $"and the need is left exactly where it was ({shut.Toilet})");

        // ── negative: the wrong kind of place ─────────────────────────────────────────────────
        // ⭐⭐ THE CASE THAT CATCHES A DISPATCH BUG. This guest DOES complete something, so any
        // "they finished, so serve them" reading relieves them here and this line goes red.
        var rode = Visit(relief: false, open: true, startingNeed: 100);
        Check(rode.Completed > 0, $"the control guest really did complete a ride ({rode.Completed} completed)");
        Check(rode.Relieved == 0, $"a RIDE relieves nobody however desperate they are ({rode.Relieved})");
        Check(rode.Toilet == 100, $"and leaves the need untouched ({rode.Toilet})");

        // ── the routing itself ────────────────────────────────────────────────────────────────
        // ⭐⭐ THE CASE THE OTHERS CANNOT MAKE. In a park holding ONE toilet, a guest reaches it
        // with no routing at all -- Idle's random fallback has nothing else to pick. So this park
        // holds several rides, all of them CLOSER, and asks a harder question: the guest must use
        // the lavatory and NOTHING ELSE FIRST. `Completed == 1` is that claim -- one facility
        // used, and it was the far one they needed. Delete the Errand call in Idle and this is
        // the line that goes red while every other case above stays green.
        var routed = Visit(relief: true, open: true, startingNeed: 100, distractors: corridor.Length - 1);
        Check(corridor.Length >= 4, $"the routing case had rides to ignore ({corridor.Length - 1} distractors)");
        Check(routed.Relieved == 1, $"a desperate guest walks PAST closer rides to the lavatory ({routed.Relieved} served)");
        Check(routed.Completed == 1, $"and rode nothing on the way ({routed.Completed} facility used in total)");
        Check(routed.Toilet == 0, $"arriving with the need answered (toilet {routed.Toilet})");

        // ── what a purchase actually does ────────────────────────────────────────────────────
        // ⭐⭐ Two bugs astraclaw's verified getter mapping exposed, both silent: a purchase
        // charged the bare price where the console charges TEN TIMES it (cash is kept in the
        // x10 units the spawn seeds), and it QUENCHED thirst where the console RAISES it.
        (int Cash, int Hunger, int Thirst, int Toilet, int Litter) Bought(int price, int hunger, int thirst, int cash = 5000, int product = VisitorNeeds.Food)
        {
            var n = new VisitorNeeds(11);
            var w = n.Spawn(1);
            w.Litter = 0;
            w.Cash = cash; w.Hunger = 50; w.Thirst = 50; w.Toilet = 20; w.Sick = 0; w.Happiness = 50;
            n.Set(1, w);
            n.Buy(1, price, hunger, thirst, happinessEffect: 5, vomitIncrease: 0, product: product);
            var a2 = n.Of(1);
            return (a2.Cash, a2.Hunger, a2.Thirst, a2.Toilet, a2.Litter);
        }
        var burger = Bought(price: 30, hunger: 25, thirst: 0);
        Check(burger.Cash == 5000 - 300, $"a purchase charges TEN times the price ({5000 - burger.Cash} for a price of 30)");
        Check(burger.Hunger == 25, $"and feeds the guest ({burger.Hunger}, was 50)");
        // ⭐ The toilet rises by the HUNGER amount -- the console re-reads the same getter.
        Check(burger.Toilet == 45, $"filling them fills the bladder by the same amount ({burger.Toilet}, was 20)");

        // ⭐ LITTER: base 30 (DAT_002EEB60) plus rand(25), so a purchase always drops at least 30
        // and never more than 54. Asserting the RANGE rather than a value keeps the random part
        // random instead of pinning this seed's draw.
        Check(burger.Litter >= 30 && burger.Litter <= 54,
              $"a purchase drops litter: base 30 + rand(25) ({burger.Litter})");

        // ⚠⚠ AFFORDABILITY, IN x10 UNITS AND BEFORE THE EFFECTS. A guest who cannot pay must not
        // eat either -- applying effects and letting cash go negative feeds the park for free.
        var broke2 = Bought(price: 30, hunger: 25, thirst: 0, cash: 299);
        Check(broke2.Cash == 299, $"a guest who cannot afford it is not charged ({broke2.Cash})");
        Check(broke2.Hunger == 50, $"and is not fed either ({broke2.Hunger}, unchanged)");
        // ⭐ THE BOUNDARY, both sides: 300 is exactly affordable at a price of 30.
        var exact = Bought(price: 30, hunger: 25, thirst: 0, cash: 300);
        Check(exact.Cash == 0 && exact.Hunger == 25, $"exactly enough buys ({exact.Cash} left, hunger {exact.Hunger})");

        // ⭐⭐ THE ARMS. Food and drink are MIRRORS, and a single unconditional rule cannot be
        // right for both: adding thirst feeds a burger correctly and makes a DRINK SHOP raise the
        // thirst it exists to quench. Selected on the compiled Product byte.
        var drink = Bought(price: 30, hunger: 0, thirst: 40, product: VisitorNeeds.Drink);
        Check(drink.Thirst == 10, $"a drink shop QUENCHES thirst ({drink.Thirst}, was 50)");
        Check(drink.Toilet == 60, $"and fills the bladder by ITS OWN amount ({drink.Toilet}, was 20)");
        // ⚠ THE CONTROL THAT SEPARATES THE ARMS. The same numbers on the food arm must do the
        // opposite to thirst -- if this matched the line above, Product would not be selecting.
        var asFood = Bought(price: 30, hunger: 0, thirst: 40, product: VisitorNeeds.Food);
        Check(asFood.Thirst == 90, $"while the food arm RAISES it on the same numbers ({asFood.Thirst})");

        // ⭐ Balloons take no litter and touch no need -- a trinket is not a meal.
        var balloon = Bought(price: 45, hunger: 0, thirst: 0, product: VisitorNeeds.Trinket);
        Check(balloon.Litter == 0, $"a balloon drops no litter ({balloon.Litter})");
        Check(balloon.Hunger == 50 && balloon.Thirst == 50 && balloon.Toilet == 20,
              $"and leaves every need alone (h{balloon.Hunger} t{balloon.Thirst} b{balloon.Toilet})");

        // ⭐ PRODUCT 7 FALLS THROUGH INTO FOOD -- `case 7:` has no break. At default quality its
        // extra thirst bump is q2/15 = 0, so it must behave EXACTLY like food, not like nothing.
        var seven = Bought(price: 30, hunger: 25, thirst: 0, product: 7);
        var asFood7 = Bought(price: 30, hunger: 25, thirst: 0, product: VisitorNeeds.Food);
        Check(seven.Hunger == asFood7.Hunger && seven.Toilet == asFood7.Toilet && seven.Cash == asFood7.Cash,
              $"product 7 falls through to food, not to nothing (h{seven.Hunger} b{seven.Toilet})");
        // ⚠ THE CONTROL: it must not be a no-op. If the arm did nothing, hunger would read 50.
        Check(seven.Hunger != 50, $"and is not the do-nothing arm it was written as ({seven.Hunger})");

        // ⭐ A costume changes what rides the guest wants -- to row 8 of the preference table, 14.
        // ⚠ The old guard `Preferences.Length > 8` was always false, so this silently did nothing.
        var pref = PreferenceAfterCostume();
        Check(pref == VisitorNeeds.CostumePreference, $"a costume shop changes the rider's taste to row 8 ({pref})");
        Check(!VisitorNeeds.Preferences.Contains(VisitorNeeds.CostumePreference),
              "and row 8 is NOT in the ordinary spawn table, which is a separate unverified policy");

        int PreferenceAfterCostume()
        {
            var n = new VisitorNeeds(5);
            var w = n.Spawn(1); w.Cash = 5000; w.PreferredIntensity = 90; n.Set(1, w);
            n.Buy(1, 60, 0, 0, happinessEffect: 15, vomitIncrease: 0, product: VisitorNeeds.Costume);
            return n.Of(1).PreferredIntensity;
        }

        var icecream = Bought(price: 30, hunger: 15, thirst: 5);
        // ⚠⚠ THE SIGN. Ice cream's own thirst is 5, and it must go UP. Subtracting would read 45.
        Check(icecream.Thirst == 55, $"and eating makes them THIRSTIER, not less ({icecream.Thirst}, was 50)");
        Check(icecream.Hunger == 35, $"while still feeding them ({icecream.Hunger}, was 50)");

        // ── an unmet need costs you your mood ─────────────────────────────────────────────────
        // ⭐⭐ `FUN_0020FB88` docks ONE happiness per need at or above its own bar, per period.
        // Until this was wired, ignoring a need cost the guest nothing: they rose, the bubble
        // appeared, and a park with no lavatory in it was indistinguishable from a good one.
        (int Happy, int Left) Mood(byte toilet, byte sick, byte bored)
        {
            var paths = new ParkPaths(terrain);
            sourcePaths.Field.Cells.CopyTo(paths.Field.Cells, 0);
            paths.SetEntrance(entranceTable);
            var visitors = new ParkVisitors(new ParkSim(paths), new GuestWalk(paths)) { Needs = new VisitorNeeds(8) };
            foreach (string key in visitors.Needs.Rates.Keys.ToArray())
                visitors.Needs.Rates[key] = new VisitorNeeds.Rate(0, 0, false);
            var g = visitors.Arrive(exit, exit);
            var w = visitors.Needs.Of(g.Id);
            w.Cash = 5000; w.Happiness = 100;
            w.Toilet = toilet; w.Sick = sick; w.Unknown78 = bored; w.Hunger = 0; w.Thirst = 0;
            visitors.Needs.Set(g.Id, w);
            for (int i = 0; i < 400; i++) visitors.Step(Tick, () => exit);
            return (visitors.Needs.Has(g.Id) ? visitors.Needs.Of(g.Id).Happiness : -1, visitors.WentHome);
        }

        var patient = Mood(toilet: 0, sick: 0, bored: 0);
        var bursting = Mood(toilet: 95, sick: 0, bored: 0);
        var wretched = Mood(toilet: 95, sick: 90, bored: 99);
        // ⭐ THE CONTROL FIRST: with nothing over a bar, nothing may move -- otherwise "happiness
        // falls" would pass for any drain at all, including one that fires unconditionally.
        Check(patient.Happy == 100, $"a guest with no unmet need keeps their mood ({patient.Happy})");
        Check(bursting.Happy < 100, $"one need over its bar wears it down ({bursting.Happy})");
        // ⚠⚠ THREE TESTS, NOT AN ELSE-IF: the console docks once PER need, so somebody bored AND
        // sick AND bursting must fall roughly three times as fast. Asserting only "it falls"
        // cannot tell the chain from the sum.
        Check(100 - wretched.Happy >= (100 - bursting.Happy) * 2,
              $"three unmet needs cost about three times as much ({100 - wretched.Happy} vs {100 - bursting.Happy})");

        // ── what a ride does to a stomach ─────────────────────────────────────────────────────
        // ⭐⭐ THE GATE, and the case that was silently WRONG: the port applied the sickness term
        // unconditionally, so every gentle ride -- including at the default intensity of 45 --
        // CURED sickness instead of leaving it alone. `0x20F248` branches past the term unless
        // the ride is above 55, and has no other arm. Found by astraclaw.
        (int Sick, int Happy) Rode(int intensity, byte startSick, byte preferred)
        {
            var needs = new VisitorNeeds(3);
            var w = needs.Spawn(1);
            w.Sick = startSick; w.Happiness = 50; w.PreferredIntensity = preferred;
            needs.Set(1, w);
            needs.Ride(1, intensity, happinessGain: 15, sickScale: 1212f / 4096f, boredomScale: 1f);
            return (needs.Of(1).Sick, needs.Of(1).Happiness);
        }

        // ⭐ BOTH SIDES OF THE BAR, one below and one above, or the check cannot tell a gate from
        // a constant. 55 is the last value that must do nothing; 56 the first that must not.
        Check(Rode(55, 40, 60).Sick == 40, $"a ride of 55 leaves a stomach exactly alone ({Rode(55, 40, 60).Sick}, was 40)");
        Check(Rode(45, 40, 60).Sick == 40, $"and so does the port's own default of 45 ({Rode(45, 40, 60).Sick})");
        Check(Rode(56, 40, 60).Sick > 40, $"a ride of 56 turns it ({Rode(56, 40, 60).Sick})");
        // ⚠ THE REGRESSION IN ONE LINE. Ungated, intensity 45 gives 40 + 0.296*(45-30) = 44 -- but
        // the OLD bug cured instead: the port's sign made gentle rides subtract. Either way it moves,
        // and "does not move" is the only correct answer.
        Check(Rode(20, 40, 60).Sick == 40, $"and a very gentle ride neither turns NOR settles it ({Rode(20, 40, 60).Sick})");

        // ⭐⭐ HAPPINESS IS BANDED BY TASTE, not flat: |preferred - intensity| against 21 and 51,
        // paying the console's own 15 / 10 / 5.
        Check(Rode(60, 0, 60).Happy == 65, $"a ride that matches the rider pays 15 ({Rode(60, 0, 60).Happy - 50})");
        Check(Rode(90, 0, 60).Happy == 60, $"a middling mismatch pays 10 ({Rode(90, 0, 60).Happy - 50})");
        Check(Rode(100, 0, 20).Happy == 55, $"and a bad one pays 5 ({Rode(100, 0, 20).Happy - 50})");

        // ── going home ────────────────────────────────────────────────────────────────────────
        // ⭐⭐ `WantsToGoHome` WAS DECODED, DOCUMENTED, CHECKED FOR ITS ARITHMETIC AND CALLED FROM
        // NOWHERE. Dead code is indistinguishable from a working feature unless something asks
        // the park for the outcome, so these ask: a broke guest must end up outside the park with
        // no record left behind, and a solvent happy one must still be in it.
        (int Home, int Rows, int Walkers) Leave(int cash, byte happiness)
        {
            var paths = new ParkPaths(terrain);
            sourcePaths.Field.Cells.CopyTo(paths.Field.Cells, 0);
            paths.SetEntrance(entranceTable);
            var visitors = new ParkVisitors(new ParkSim(paths), new GuestWalk(paths)) { Needs = new VisitorNeeds(77) };
            foreach (string key in visitors.Needs.Rates.Keys.ToArray())
                visitors.Needs.Rates[key] = new VisitorNeeds.Rate(0, 0, false);
            var g = visitors.Arrive(exit, exit);
            var w = visitors.Needs.Of(g.Id);
            w.Cash = cash; w.Happiness = happiness;
            w.Hunger = 0; w.Thirst = 0; w.Toilet = 0; w.Sick = 0; w.Boredom = 0;
            visitors.Needs.Set(g.Id, w);
            for (int i = 0; i < Steps && visitors.WentHome == 0; i++) visitors.Step(Tick, () => exit);
            return (visitors.WentHome, visitors.Needs.All.Count, visitors.Walk.Guests.Count);
        }

        var broke = Leave(cash: 0, happiness: 80);
        Check(broke.Home == 1, $"a guest with no money left goes home ({broke.Home})");
        Check(broke.Walkers == 0, $"and is no longer in the park ({broke.Walkers} walkers)");
        // ⚠ THE ID-REUSE TRAP. A needs row outliving its guest is inherited by whoever gets that
        // id next, which reads as a visitor who arrived already miserable.
        Check(broke.Rows == 0, $"leaving no needs record behind ({broke.Rows} rows)");

        var miserable = Leave(cash: 5000, happiness: 2);
        Check(miserable.Home == 1, $"so does a thoroughly miserable one ({miserable.Home})");

        // ⭐ THE CONTROL. Without this, "everyone leaves immediately" passes all three above.
        // ── waiting in a queue ────────────────────────────────────────────────────────────────
        // ⭐⭐ `VisitorNeeds.Queue` -- decoded off `FUN_0020C6A8`, eighteen references from these
        // checks, and CALLED FROM NOWHERE until now, so standing in a queue was free. Found by
        // tools/dead_port_audit.py. ⚠ PEAK, not final: `Ride` SUBTRACTS from the same byte that
        // `Queue` adds to, so a completed ride can erase the evidence -- the sign difference is
        // what makes the two separable, and sampling the high-water mark is what survives it.
        (int Peak78, int LowHappy) Wait(bool withRide)
        {
            var paths = new ParkPaths(terrain);
            sourcePaths.Field.Cells.CopyTo(paths.Field.Cells, 0);
            paths.SetEntrance(entranceTable);
            var visitors = new ParkVisitors(new ParkSim(paths), new GuestWalk(paths)) { Needs = new VisitorNeeds(31) };
            foreach (string key in visitors.Needs.Rates.Keys.ToArray())
                visitors.Needs.Rates[key] = new VisitorNeeds.Rate(0, 0, false);
            if (withRide)
            {
                var r = visitors.Sim.Add(1, "queue fixture", corridor[0], 1, 1, rideScript, rideAps,
                                         rideDef.UpgradeCapacity(0) ?? 1, corridor[0], exit, out string fault,
                                         sibling: rideSibling, headSlots: rideSeats, definition: rideDef)
                        ?? throw new InvalidOperationException("queue fixture would not start: " + fault);
                visitors.Sim.SetOpen(r.Id, true);
                r.Set("VAR_BROKEN", 0);
                // ⚠⚠ ONE SEAT, AND THAT IS THE WHOLE POINT. With an idle ride and a single guest
                // the handover happens within a tick or two and NOBODY EVER WAITS a rise period,
                // so the first version of this check read 0 and would have read 0 however right
                // the code was. A queue needs more people than seats before it is a queue.
                r.Set("VAR_CAPACITY", 1);
            }
            var ids = new List<int>();
            for (int i = 0; i < 8; i++)
            {
                var g = visitors.Arrive(exit, exit);
                ids.Add(g.Id);
                var w = visitors.Needs.Of(g.Id);
                w.Cash = 5000; w.Happiness = 100; w.Unknown78 = 0;
                w.Hunger = 0; w.Thirst = 0; w.Toilet = 0; w.Sick = 0;
                visitors.Needs.Set(g.Id, w);
            }
            int peak = 0, low = 100;
            for (int i = 0; i < 4000; i++)
            {
                visitors.Step(Tick, () => exit);
                foreach (int id in ids)
                {
                    if (!visitors.Needs.Has(id)) continue;
                    var n = visitors.Needs.Of(id);
                    peak = Math.Max(peak, n.Unknown78);
                    low = Math.Min(low, n.Happiness);
                }
            }
            return (peak, low);
        }

        var waited = Wait(withRide: true);
        Check(waited.Peak78 > 0, $"standing in a queue costs the guest something ({waited.Peak78} boredom at its peak)");
        Check(waited.LowHappy < 100, $"and wears their patience down ({waited.LowHappy} happiness at its lowest)");
        // ⭐ THE CONTROL. Same park, same clock, nothing to queue for: if this also moved, the
        // effect would be the ordinary needs rise and not the queue at all.
        var unqueued = Wait(withRide: false);
        Check(unqueued.Peak78 == 0, $"a guest with no queue to stand in pays nothing ({unqueued.Peak78})");
        Check(unqueued.LowHappy == 100, $"and keeps their patience ({unqueued.LowHappy})");

        // ── the guest who cannot afford anything ─────────────────────────────────────────────
        // ⭐⭐ MASTER'S BUG, AS A CHECK. A guest with more than the go-home floor (100) but less
        // than a purchase (price * 10) can neither buy nor leave -- so they walked to the shop,
        // were refused, kept the identical need, and walked straight back. Forever. Master saw it
        // as guests "stuck on the stub path tile and repeatedly visiting the shop".
        //
        // ⚠ THE FIX IS NOT A CASH FILTER. The console does not test cash when choosing what it
        // wants (three call sites in FUN_0020C930, none reads +0x60). It lets boredom rise until
        // the mood drain grinds the guest down and they go home on their own -- and OUR boredom
        // never rose, because `Rise` skipped it. This asserts the loop terminates.
        (int Home, int Purchases, int Bored) Broke()
        {
            var paths = new ParkPaths(terrain);
            sourcePaths.Field.Cells.CopyTo(paths.Field.Cells, 0);
            paths.SetEntrance(entranceTable);
            var visitors = new ParkVisitors(new ParkSim(paths), new GuestWalk(paths)) { Needs = new VisitorNeeds(1234) };
            // ⚠⚠ A SHOP DEFINITION, NOT THE RIDE'S. The first version of this check handed the
            // ride's own definition to the fixture, so `Serve` took the RIDE branch: nothing was
            // ever sold (making `Purchases == 0` pass vacuously) and `Ride()` SUBTRACTED the
            // boredom this check is about, pinning it near zero on every visit. It reproduced a
            // guest happily riding a free ride forever, not a guest who cannot afford lunch.
            // Synthesised rather than loaded because this fixture has no WadArchive to read one.
            var shopDef = RideDefinition.Parse(
                "Info.Name\t\"unaffordable shop\"\n" +
                "UsageInfo.ShopType\t2\n" +
                "UsageInfo.InitPricePerUse\t30\n" +
                "UsageInfo.HungerEffect\t25\n", "synthetic/shop.sam");
            var shop = visitors.Sim.Add(1, "unaffordable shop", corridor[0], 1, 1, rideScript, rideAps,
                                        1, corridor[0], exit, out string fault,
                                        sibling: rideSibling, headSlots: rideSeats, definition: shopDef)
                       ?? throw new InvalidOperationException(fault);
            visitors.Sim.SetOpen(shop.Id, true); shop.Set("VAR_BROKEN", 0);
            var g = visitors.Arrive(exit, exit);
            var w = visitors.Needs.Of(g.Id);
            // Hungry, and holding strictly between the go-home floor and the price of a meal.
            w.Hunger = 100; w.Cash = 250; w.Happiness = 60; w.Unknown78 = 0;
            w.Thirst = 0; w.Toilet = 0; w.Sick = 0;
            visitors.Needs.Set(g.Id, w);
            int peakBored = 0;
            for (int i = 0; i < 40000 && visitors.WentHome == 0; i++)
            {
                visitors.Step(Tick, () => exit);
                if (visitors.Needs.Has(g.Id)) peakBored = Math.Max(peakBored, visitors.Needs.Of(g.Id).Unknown78);
            }
            return (visitors.WentHome, visitors.Purchases, peakBored);
        }
        var broke3 = Broke();
        Check(broke3.Purchases == 0, $"a guest who cannot afford the shop buys nothing ({broke3.Purchases})");
        // ⚠⚠ THIS LINE USED TO ASSERT `>= 20` AND THE ASSERTION WAS WRONG TWICE OVER. It was
        // added alongside a timed rise on `+0x78`, justified by that byte's 95 bar -- and the
        // bubble chain in `FUN_0020FB88` then showed `+0x78` is NOT boredom (boredom is `+0x7B`,
        // which the engine points `tbbored` at) AND that nothing in that function raises `+0x78`
        // on a clock at all. So the rise went, and with it the only thing that made 20 true.
        //
        // ⭐ What is left is the queue's own **+5**, which IS read (`FUN_0020C6A8`).
        //
        // ⚠⚠ AND THE FIRST REPLACEMENT ASSERTED `== 5`, WHICH WAS THE JUNGLE NUMBER AND NOT AN
        // INVARIANT. HALLOW and FANTASY read 0 -- their fixture geometry never puts the guest in
        // a queue at all -- so a rule fitted to one world's observation failed in two others. ⭐
        // Replacing an overclaim with a number I had just watched go by is the same mistake in a
        // smaller font.
        //
        // What IS invariant: the ONLY thing that moves `+0x78` is the queue, in steps of 5. A
        // timed rise coming back reads as a large, non-multiple value. ⚠ "It moves at all" is
        // NOT this case's job and must not be asserted here -- the queue case above owns that,
        // with its own no-ride control.
        Check(broke3.Bored % 5 == 0 && broke3.Bored <= 50,
              $"nothing but the queue's +5 moves +0x78 ({broke3.Bored})");
        // ⭐⭐ AND THIS IS THE CHECK THAT ACTUALLY MATTERED ALL ALONG -- measured, not assumed:
        // it passed WITHOUT the boredom rise, which is how the rise was found not to be the fix.
        // The queue's happiness cost alone grinds them under the go-home floor.
        // ⚠ Master's "stuck on the stub tile" is a DIFFERENT defect: astraclaw traced it to a
        // decision timer the console sets on shop completion (even on a refusal) plus recency
        // penalties in the selector, and to an approach leg that stops one cell short of the
        // compiled entrance. Neither is this check's business; it only proves no infinite loop.
        Check(broke3.Home == 1, $"and they eventually give up and go home rather than looping forever ({broke3.Home})");

        // ── somebody cleans the lavatories ──────────────────────────────────────────────────
        // ⭐⭐ THE RATCHET CHECK. Wear costs condition, condition below 50 costs the next guest
        // 10 happiness, and with no handyman that is permanent -- every guest angry forever and
        // the park empties. Master saw it on the first playtest. This asserts recovery, and the
        // CONTROL asserts the dirt is real when the stand-in is switched off, so a check that
        // simply never wore anything could not pass both.
        (int Dirty, int Clean) Handyman()
        {
            var paths = new ParkPaths(terrain);
            sourcePaths.Field.Cells.CopyTo(paths.Field.Cells, 0);
            paths.SetEntrance(entranceTable);

            int Run(bool auto)
            {
                var visitors = new ParkVisitors(new ParkSim(paths), new GuestWalk(paths))
                    { Needs = new VisitorNeeds(1234), AutoService = auto };
                var loo = visitors.Sim.Add(1, "fixture toilet", corridor[0], 1, 1, toiletScript, toiletAps,
                                           1, corridor[0], exit, out string fault,
                                           sibling: toiletSibling, definition: toiletDef)
                          ?? throw new InvalidOperationException(fault);
                visitors.Sim.SetOpen(loo.Id, true);
                loo.Wear(100);
                if (loo.Condition != 0) throw new InvalidOperationException("fixture failed to soil the lavatory");
                // Past one service period, whichever way the switch is set.
                for (int i = 0; i < (int)(visitors.SecondsPerService / Tick) + 10; i++)
                    visitors.Step(Tick, () => exit);
                return loo.Condition;
            }
            return (Run(auto: false), Run(auto: true));
        }
        // ⭐⭐ A GUEST WITH NO NEEDS MUST NOT VISIT A LAVATORY. The random "something to do" pick
        // drew from every open facility, lavatories included, so a guest who had just arrived
        // queued for the toilet -- repeatedly, in a park with little else built.
        // ⚠ THE CONTROL IS THE SAME PARK WITH A FULL BLADDER: if that one does not get relieved,
        // this check is passing because routing is broken rather than because the filter works.
        (int Idle, int Bursting) ToiletOnly()
        {
            int Run(byte toilet)
            {
                var paths = new ParkPaths(terrain);
                sourcePaths.Field.Cells.CopyTo(paths.Field.Cells, 0);
                paths.SetEntrance(entranceTable);
                var visitors = new ParkVisitors(new ParkSim(paths), new GuestWalk(paths)) { Needs = new VisitorNeeds(99) };
                foreach (string key in visitors.Needs.Rates.Keys.ToArray())
                    visitors.Needs.Rates[key] = new VisitorNeeds.Rate(0, 0, false);
                visitors.Needs.Unknown78Bar = visitors.Needs.SickBar = visitors.Needs.ToiletBar =
                    visitors.Needs.HungerBar = visitors.Needs.ThirstBar = 101;
                var loo = visitors.Sim.Add(1, "fixture toilet", corridor[0], 1, 1, toiletScript, toiletAps,
                                           1, corridor[0], exit, out string fault,
                                           sibling: toiletSibling, definition: toiletDef)
                          ?? throw new InvalidOperationException(fault);
                visitors.Sim.SetOpen(loo.Id, true); loo.Set("VAR_BROKEN", 0);
                var g = visitors.Arrive(exit, exit);
                var w = visitors.Needs.Of(g.Id);
                w.Toilet = toilet; w.Hunger = 0; w.Thirst = 0; w.Sick = 0; w.Happiness = 100;
                visitors.Needs.Set(g.Id, w);
                for (int i = 0; i < 3000; i++) visitors.Step(Tick, () => exit);
                return visitors.Relieved;
            }
            return (Run(0), Run(100));
        }
        // ⭐⭐ THE GATE'S GROUND, checked where a real fitted grid exists. Occupancy must refuse
        // a BUILD and still permit the path -- ⚠ and the second half is the control that matters,
        // because putting these cells in the wrong set blocked the way into the park entirely.
        {
            var grid = new ParkPaths(terrain);
            sourcePaths.Field.Cells.CopyTo(grid.Field.Cells, 0);
            grid.SetEntrance(entranceTable);
            Check(grid.GateHold.Count == 16, $"the gate holds its 2x2 grown by one on each side ({grid.GateHold.Count} cells)");
            Check(grid.Protected.Count == 4, $"and protects a 2x2 of paths under it ({grid.Protected.Count})");
            Check(grid.Protected.All(c => grid.GateHolds(c)), "every protected path is inside the gate's own hold");
            Check(grid.GateHold.All(c => !grid.CanBuild(c)), "nothing can be built on the gate's ground");
            // ⚠⚠ THE CONTROL, STATED AS THE INVARIANT THE BUG ACTUALLY BROKE. The first version
            // asserted `CanLay` outright on those cells and failed -- correctly, and for a reason
            // about the FIXTURE: the mouth is walkway, which was never `Buildable`. What the bug
            // did was add the gate's cells to the set `CanLay` consults, so the honest test is
            // that registering the entrance CHANGES NO `CanLay` ANSWER anywhere on the grid.
            var before = new ParkPaths(terrain);
            sourcePaths.Field.Cells.CopyTo(before.Field.Cells, 0);
            int changed = grid.Cells.Count(c => before.CanLay(c) != grid.CanLay(c));
            Check(changed == 0, $"CONTROL: the gate's hold changes no laying answer on the whole grid ({changed} cells differ)");
            Check(grid.Cells.Count(before.CanLay) > 0, "and the grid had layable cells to begin with, so that is not vacuous");
        }
        var loneLoo = ToiletOnly();
        Check(loneLoo.Idle == 0, $"a guest with an empty bladder never visits the only lavatory in the park ({loneLoo.Idle})");
        Check(loneLoo.Bursting > 0, $"CONTROL: the same park DOES relieve a desperate one ({loneLoo.Bursting})");

        var loos = Handyman();
        Check(loos.Clean == 100, $"a worn lavatory is serviced back to 100 ({loos.Clean})");
        Check(loos.Dirty == 0, $"and the CONTROL shows the wear was real -- unserviced it stays filthy ({loos.Dirty})");

        // ── going home over a path that breaks and is mended ─────────────────────────────────
        // ⭐⭐ THE ONE-WAY DOOR. A `Leaving` guest whose path was dug up under them stayed stuck
        // FOREVER -- not because anything was stale, but because they left a state with no way
        // back: the stranded-recovery clause only matched `Heading`, and resetting the plan alone
        // leaves the WALK state Stranded so the `!= Arrived` guard skips them for good. Every
        // snapshot of that guest looks individually fine, which is why it needs a DURATION to see.
        // Reproduced by astraclaw; this is the permanent one on my side of the line.
        (int Home, int Stuck) Detour()
        {
            var paths = new ParkPaths(terrain);
            sourcePaths.Field.Cells.CopyTo(paths.Field.Cells, 0);
            paths.SetEntrance(entranceTable);
            var visitors = new ParkVisitors(new ParkSim(paths), new GuestWalk(paths)) { Needs = new VisitorNeeds(5150) };
            foreach (string key in visitors.Needs.Rates.Keys.ToArray())
                visitors.Needs.Rates[key] = new VisitorNeeds.Rate(0, 0, false);
            var g = visitors.Arrive(exit, exit);
            var w = visitors.Needs.Of(g.Id);
            w.Cash = 0; w.Happiness = 80;             // broke: they want to leave
            w.Hunger = 0; w.Thirst = 0; w.Toilet = 0; w.Sick = 0; w.Boredom = 0;
            visitors.Needs.Set(g.Id, w);

            var ground = (byte[])paths.Field.Cells.Clone();
            for (int i = 0; i < 60; i++) visitors.Step(Tick, () => exit);   // set off for the gate
            // dig the corridor out from under them
            for (int i = 0; i < paths.Field.Cells.Length; i++) paths.Field.Cells[i] = 0;
            for (int i = 0; i < 600 && visitors.WentHome == 0; i++) visitors.Step(Tick, () => exit);
            int strandedWhileBroken = visitors.WentHome == 0 ? 1 : 0;
            // mend it
            ground.CopyTo(paths.Field.Cells, 0);
            for (int i = 0; i < 6000 && visitors.WentHome == 0; i++) visitors.Step(Tick, () => exit);
            return (visitors.WentHome, strandedWhileBroken);
        }

        var detour = Detour();
        Check(detour.Stuck == 1, $"a guest cannot reach the gate while the path is dug up ({detour.Stuck})");
        Check(detour.Home == 1, $"and gets there once it is mended ({detour.Home} went home)");

        var content = Leave(cash: 5000, happiness: 80);
        Check(content.Home == 0, $"a solvent, happy guest stays ({content.Home} went home)");
        Check(content.Walkers == 1, $"and is still walking about ({content.Walkers})");
        Check(content.Rows == 1, $"with their record intact ({content.Rows})");
    }
}
