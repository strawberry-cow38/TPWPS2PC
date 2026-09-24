using System.Security.Cryptography;
using System.Text;
using TPW.PS2.Data;

/// <summary>Audit-only ownership census and identical-input replay, not retail guest AI.</summary>
static class GuestConservationChecks
{
    // Queue, offer, seats and exit mailbox can overlap within a single script's
    // handover. Union those representations per ride, but never across rides.
    internal static HashSet<int> Bodies(ParkRide ride) => ride.Queue.Concat(ride.Left)
        .Concat(ride.Host.Seats.Values).Append(ride.Get("VAR_LETMEON"))
        .Append(ride.Get("VAR_LETMEOFF")).Where(id => id > 0).ToHashSet();

    internal static string Census(ParkVisitors visitors, HashSet<int> expected)
    {
        if (!expected.SetEquals(visitors.Plans.Keys)) return "plan identity set changed";
        if (visitors.Walk.Guests.Any(g => !expected.Contains(g.Id))) return "unknown walking identity";
        var bodies = visitors.Sim.Rides.ToDictionary(r => r.Id, Bodies);
        if (bodies.Values.Any(ids => !ids.IsSubsetOf(expected))) return "unknown ride identity";
        foreach (int id in expected.Order())
        {
            var plan = visitors.Plans[id];
            if (plan.Guest != id) return $"plan {id} contains another guest identity";
            if (plan.Intent == VisitorIntent.Wandering && plan.RideId != 0) return $"wandering guest {id} retains ride intent";
            int walking = visitors.Walk.Guests.Count(g => g.Id == id);
            var owners = bodies.Where(p => p.Value.Contains(id)).Select(p => p.Key).ToArray();
            if (owners.Length > 1) return $"guest {id} represented in several live rides";
            switch (plan.Intent)
            {
                case VisitorIntent.Wandering:
                case VisitorIntent.Heading:
                    if (walking != 1 || owners.Length != 0) return $"walking guest {id} has inconsistent body ownership";
                    if (plan.Intent == VisitorIntent.Heading && !bodies.ContainsKey(plan.RideId))
                        return $"heading guest {id} references a missing ride";
                    break;
                case VisitorIntent.Queued:
                    if (walking != 0 || !bodies.ContainsKey(plan.RideId)
                        || owners.Any(owner => owner != plan.RideId)) return $"queued guest {id} has inconsistent ride ownership";
                    // Script transitions need not have a seat/queue/mailbox body at
                    // every slice. The retained plan owns that in-between identity.
                    break;
                case VisitorIntent.Recovering:
                    if (walking != 0 || owners.Length != 0 || plan.RideId != 0)
                        return $"recovering guest {id} still has a live body owner";
                    break;
                default: return $"guest {id} has unknown intent";
            }
        }
        return null;
    }

    internal static string Snapshot(ParkVisitors visitors)
    {
        var sim = visitors.Sim;
        return $"{sim.Time}/{visitors.Walk.Time}/{visitors.Boardings}/{visitors.Rides}|"
            + string.Join(';', visitors.Plans.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.Intent}:{p.Value.RideId}:{p.Value.At}"))
            + "|" + string.Join(';', visitors.Walk.Guests.OrderBy(g => g.Id).Select(g =>
                $"{g.Id}:{g.Cell}:{g.Next}:{g.Progress}:{g.Destination}:{g.State}:{g.RouteIndex}:{g.Steps}:{g.Reroutes}"))
            + "|" + string.Join(';', sim.Rides.OrderBy(r => r.Id).Select(r =>
                $"{r.Id}:{string.Join(',', ParkSim.RideVariables.Select(r.Get))}:{string.Join(',', r.Queue)}:"
                + $"{string.Join(',', r.Left)}:{string.Join(',', r.Host.Seats.OrderBy(s => s.Key).Select(s => $"{s.Key}={s.Value}"))}:{r.Slot}:{r.Variant}"));
    }

    public static void Run(Model terrain, ParkPaths sourcePaths, byte[] script, Animation animation, int capacity,
                           ParkCell entrance, ParkCell exit, Func<string, byte[]> sibling, int headSlots,
                           Action<bool, string> check)
    {
        void Check(bool ok, string message) => check(ok, "conservation: " + message);
        (ParkVisitors Visitors, ParkRide First, ParkRide Second, HashSet<int> Ids) Fresh()
        {
            var paths = new ParkPaths(terrain);
            sourcePaths.Field.Cells.CopyTo(paths.Field.Cells, 0);
            var sim = new ParkSim(paths);
            ParkRide Add(int id) => sim.Add(id, $"crowd ride {id}", entrance, 1, 1, script, animation,
                capacity, entrance, exit, out _, sibling: sibling, headSlots: headSlots)
                ?? throw new InvalidOperationException("Crowd fixture script did not start");
            var first = Add(1); var second = Add(2);
            sim.SetOpen(1, true); sim.SetOpen(2, true);
            first.Set("VAR_BROKEN", 0); second.Set("VAR_BROKEN", 0);
            var visitors = new ParkVisitors(sim, new GuestWalk(paths));
            var ids = new HashSet<int>();
            for (int i = 0; i < 8; i++)
            {
                var guest = visitors.Arrive(entrance, entrance); ids.Add(guest.Id);
                if (!visitors.SendTo(guest, i < 4 ? first : second))
                    throw new InvalidOperationException("Crowd fixture cannot reach its ride");
            }
            return (visitors, first, second, ids);
        }

        // Prove the census can reject bad states rather than merely passing itself.
        var duplicate = Fresh();
        duplicate.Visitors.Walk.Readmit(duplicate.Ids.Min(), entrance, entrance);
        Check(Census(duplicate.Visitors, duplicate.Ids) != null, "negative control rejects a duplicated walking body");
        var lost = Fresh(); lost.Visitors.Walk.Remove(lost.Ids.Min());
        Check(Census(lost.Visitors, lost.Ids) != null, "negative control rejects a missing walking body");
        var unknown = Fresh(); unknown.Visitors.Walk.Spawn(entrance, entrance);
        Check(Census(unknown.Visitors, unknown.Ids) != null, "negative control rejects an unregistered identity");
        var cross = Fresh(); cross.First.Join(cross.Ids.Min());
        Check(Census(cross.Visitors, cross.Ids) != null, "negative control rejects concurrent walking/ride ownership");
        var twoRides = Fresh(); twoRides.Visitors.Step(0, null);
        twoRides.Second.Join(twoRides.Ids.Min());
        Check(Census(twoRides.Visitors, twoRides.Ids) != null, "negative control rejects ownership in two live rides");

        (string Digest, int Ticks) Replay()
        {
            var fixture = Fresh(); var visitors = fixture.Visitors; var sim = visitors.Sim;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            string failure = null; int ticks = 0;
            void Sample()
            {
                failure ??= Census(visitors, fixture.Ids);
                hash.AppendData(Encoding.UTF8.GetBytes(Snapshot(visitors) + "\n"));
            }
            void Tick(double delta) { visitors.Step(delta, null); ticks++; Sample(); }
            Sample(); Tick(0);
            Check(visitors.Boardings == 8 && visitors.Walk.Guests.Count == 0,
                  "eight guests are handed to two independent ride instances");
            for (int i = 0; i < 6000 && !(fixture.First.Host.Seats.Count > 0 && fixture.Second.Host.Seats.Count > 0); i++) Tick(.04);
            Check(fixture.First.Host.Seats.Count > 0 && fixture.Second.Host.Seats.Count > 0,
                  "both real scripts seat guests before removal");
            sim.SetOpen(2, false); fixture.Second.Set("VAR_STARTNOW", 1);
            sim.Remove(1); Tick(0);
            Check(fixture.Ids.Order().Take(4).All(id => visitors.Walk.Guests.Count(g => g.Id == id) == 1),
                  "removing one ride returns all four of its queued/offered/seated guests");
            Check(fixture.Second.Host.Seats.Count > 0 && sim.Rides.Count == 1 && ReferenceEquals(sim.Rides[0], fixture.Second),
                  "the other ride and its actual seated riders survive removal");
            for (int i = 0; i < 12000 && visitors.Rides == 0; i++) Tick(.04);
            Check(visitors.Rides > 0 && fixture.Second.Fault == null,
                  "the surviving real script completes a ride after its neighbor is removed");
            sim.Clear(); Tick(0);
            for (int i = 0; i < 8; i++) Tick(.04);
            Check(visitors.Walk.Guests.Count == 8 && fixture.Ids.SetEquals(visitors.Walk.Guests.Select(g => g.Id))
                  && visitors.Plans.Values.All(p => p.Intent == VisitorIntent.Wandering),
                  "final Clear returns all eight original identities exactly once");
            Check(failure == null, $"per-step ownership census ({ticks} steps): {failure ?? "conserved"}");
            return (Convert.ToHexString(hash.GetHashAndReset()), ticks);
        }

        var firstRun = Replay(); var secondRun = Replay();
        Check(firstRun == secondRun, $"identical fixed-tick inputs reproduce the full sampled lifecycle ({firstRun.Ticks} steps, SHA256 {firstRun.Digest})");
    }
}
