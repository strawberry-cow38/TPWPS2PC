using TPW.PS2.Data;

/// <summary>Availability regressions using a disc-backed ride in independent simulations.
/// No script ticks run: zero-delta steps exercise delivery/selection without the ride
/// changing the flags under test. None of the audit's long-running park state is reused.</summary>
static class VisitorAvailabilityChecks
{
    public static void Run(ParkPaths paths, byte[] script, Animation animation, int capacity,
                           ParkCell entrance, ParkCell exit, Func<string, byte[]> sibling,
                           int headSlots, Action<bool, string> check)
    {
        (ParkRide Ride, ParkVisitors Visitors, Guest Guest) Fresh()
        {
            var sim = new ParkSim(paths);
            var ride = sim.Add(1, "availability regression", entrance, 1, 1, script, animation,
                               capacity, entrance, exit, out var fault, sibling: sibling, headSlots: headSlots);
            if (ride == null) throw new InvalidOperationException("Availability fixture failed: " + fault);
            sim.SetOpen(ride.Id, true);
            ride.Set("VAR_BROKEN", 0);
            var visitors = new ParkVisitors(sim, new GuestWalk(paths));
            return (ride, visitors, visitors.Arrive(entrance, entrance));
        }

        void Check(bool ok, string message) => check(ok, "availability: " + message);

        var healthy = Fresh();
        Check(healthy.Visitors.Takes(healthy.Ride) && healthy.Visitors.Open.Contains(healthy.Ride),
              "an open, unbroken ride is selectable");
        Check(healthy.Visitors.SendTo(healthy.Guest, healthy.Ride), "an open ride accepts an explicit destination");
        healthy.Visitors.Step(0, null);
        Check(healthy.Visitors.Boardings == 1 && healthy.Ride.Queue.Contains(healthy.Guest.Id)
              && !healthy.Visitors.Walk.Guests.Contains(healthy.Guest), "an open ride actually receives its guest");

        foreach (var (label, closed, broken) in new[] { ("closed", 1, 0), ("broken", 0, 1), ("both", 1, 1) })
        {
            var (ride, visitors, guest) = Fresh();
            ride.Set("VAR_RIDECLOSED", closed);
            ride.Set("VAR_BROKEN", broken);
            Check(!visitors.Takes(ride), label + " ride is ineligible");
            Check(!visitors.Open.Contains(ride), label + " ride is absent from automatic destination choices");
            var before = visitors.Plans[guest.Id];
            Check(!visitors.SendTo(guest, ride) && visitors.Plans[guest.Id] == before
                  && guest.Destination == entrance, label + " ride rejects SendTo without changing the guest's plan");
            visitors.Step(0, () => entrance);
            Check(visitors.Boardings == 0 && ride.Queue.Count == 0 && visitors.Walk.Guests.Contains(guest),
                  label + " ride is not selected or joined by an idle guest");
            ride.Set("VAR_RIDECLOSED", 0);
            ride.Set("VAR_BROKEN", 0);
            Check(visitors.Takes(ride) && visitors.SendTo(guest, ride), label + " ride accepts guests after recovery");
            visitors.Step(0, null);
            Check(visitors.Boardings == 1 && ride.Queue.Contains(guest.Id), label + " ride can board after recovery");

            // It was valid when routed, but closes/breaks before the handoff at its entrance.
            var late = Fresh();
            Check(late.Visitors.SendTo(late.Guest, late.Ride), label + " arrival-race control has a valid route");
            late.Ride.Set("VAR_RIDECLOSED", closed);
            late.Ride.Set("VAR_BROKEN", broken);
            late.Visitors.Step(0, null);
            Check(late.Visitors.Boardings == 0 && late.Ride.Queue.Count == 0
                  && late.Visitors.Walk.Guests.Contains(late.Guest)
                  && late.Visitors.Plans[late.Guest.Id].Intent == VisitorIntent.Wandering
                  && late.Visitors.Plans[late.Guest.Id].RideId == 0,
                  label + " after routing keeps the arriving guest in the park instead of queuing");
        }

        // The variable declarations differ by script. Get's absent-variable fallback
        // must remain valid: availability checks cannot index missing optional flags.
        var optional = Fresh();
        optional.Ride.Set("VAR_RIDECLOSED", 1);
        optional.Ride.Set("VAR_BROKEN", 1);
        var withoutFlags = new ParkRide
        {
            Id = optional.Ride.Id, Entrance = entrance, Machine = optional.Ride.Machine,
            Host = optional.Ride.Host, Variables = new HashSet<string> { "VAR_LETMEON" },
        };
        Check(optional.Visitors.Takes(withoutFlags), "missing optional flags default to available without throwing");
        Check(!optional.Visitors.Takes(null), "a null ride is still ineligible");
        Check(!optional.Visitors.Takes(new ParkRide { Machine = optional.Ride.Machine }),
              "missing entrance/boarding capability remains ineligible");
    }
}
