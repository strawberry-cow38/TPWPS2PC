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
        (int Toilet, int Relieved, int Boardings, int Completed, int Soil) Visit(
            bool relief, bool open, byte startingNeed, int distractors = 0)
        {
            var paths = new ParkPaths(terrain);
            sourcePaths.Field.Cells.CopyTo(paths.Field.Cells, 0);
            // ⚠ A COPY HAS NO GATE. Field.Cells carries the laid path but not the entrance
            // registration, and without it `ParkVisitors.Gate` is null and NOBODY CAN LEAVE --
            // which would make the departure cases below pass while testing nothing at all.
            paths.SetEntrance(entranceTable);
            var visitors = new ParkVisitors(new ParkSim(paths), new GuestWalk(paths)) { Needs = new VisitorNeeds(4242) };
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
                    visitors.Soil.TryGetValue(ride.Id, out var s) ? s : 0);
        }

        // ── the case the feature exists for ────────────────────────────────────────────────────
        var relieved = Visit(relief: true, open: true, startingNeed: 100);
        Check(relieved.Boardings > 0, $"a bursting guest reaches the lavatory and goes in ({relieved.Boardings} boardings)");
        Check(relieved.Relieved == 1, $"they are served exactly once ({relieved.Relieved})");
        Check(relieved.Toilet == 0, $"and come out with the need answered (toilet {relieved.Toilet}, was 100)");
        // ⭐ (100-60)*2/3 = 26, from the decoded soil arithmetic -- asserted as the number rather
        // than "> 0", because "> 0" passes for any formula at all.
        Check(relieved.Soil == 26, $"the mess is the decoded (need-60)*2/3 = 26 ({relieved.Soil})");

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
            w.Hunger = 0; w.Thirst = 0; w.Toilet = 0; w.Sick = 0; w.Unknown7B = 0;
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

        var content = Leave(cash: 5000, happiness: 80);
        Check(content.Home == 0, $"a solvent, happy guest stays ({content.Home} went home)");
        Check(content.Walkers == 1, $"and is still walking about ({content.Walkers})");
        Check(content.Rows == 1, $"with their record intact ({content.Rows})");
    }
}
