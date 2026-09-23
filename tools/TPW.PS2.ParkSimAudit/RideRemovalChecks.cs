using TPW.PS2.Data;

/// <summary>Managed-layer removal policy, not a claim about the console's evacuation logic.
/// Each case owns a separate copied path grid, sim, script instance and visitor ledger.</summary>
static class RideRemovalChecks
{
    /// <summary>Explicit synthetic park layout over real terrain/ride data, so
    /// lifecycle regressions can run even when retail entrance integration is broken.
    /// This does not replace or green the ordinary whole-park audit.</summary>
    public static bool RunIsolated(Model terrain, WadArchive wad, Action<bool, string> check)
    {
        var paths = new ParkPaths(terrain);
        int material = Enumerable.Range(1, paths.Materials.Count - 1)
            .FirstOrDefault(i => i <= byte.MaxValue && ParkPaths.Classify(paths.Materials[i]) == ParkPathKind.Path);
        if (material == 0) { check(false, "isolated removal fixture has no path material"); return false; }
        var anchors = paths.Cells.Where(c => Enumerable.Range(0, 3)
            .Select(dx => c.Offset(dx, 0)).All(p => paths.CanLay(p) && !paths.SceneryBlocks(p))).Take(1).ToArray();
        if (anchors.Length == 0) { check(false, "isolated removal fixture has no three-cell buildable corridor"); return false; }
        for (int dx = 0; dx < 3; dx++) paths.Lay(anchors[0].Offset(dx, 0), material);
        var entrance = anchors[0].Offset(1, 0); var exit = anchors[0].Offset(2, 0);
        foreach (var entry in wad.Entries.Where(e => e.Path.StartsWith("/Rides/", StringComparison.OrdinalIgnoreCase)
                     && e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Path, StringComparer.Ordinal))
        {
            string stem = entry.Path[..^4];
            var sam = wad.Find(stem + ".sam"); var mps = wad.Find(stem + ".mps"); var aps = wad.Find(stem + ".aps");
            if (sam == null || mps == null || aps == null) continue;
            byte[] script = wad.Read(entry); var program = new RseProgram(script);
            if (!program.Instructions.Any(i => i.Opcode == RseOpcode.ADDHEAD)
                || program.Instructions.Any(i => i.Opcode is RseOpcode.TOUR or RseOpcode.BUMP or RseOpcode.COAST)
                || !new[] { "VAR_LETMEON", "VAR_LETMEOFF", "VAR_RIDECLOSED", "VAR_BROKEN" }
                    .All(v => program.VariableNames.Contains(v, StringComparer.OrdinalIgnoreCase))) continue;
            var model = new Model(wad.Read(mps));
            if (model.HeadSlotCount == 0) continue;
            var animation = new Animation(wad.Read(aps));
            var definition = RideDefinition.Parse(System.Text.Encoding.ASCII.GetString(wad.Read(sam)), sam.Path);
            string directory = entry.Path[..(entry.Path.LastIndexOf('/') + 1)];
            byte[] Sibling(string child)
            {
                var found = wad.Find(directory + child);
                return found == null ? null : wad.Read(found);
            }
            Console.WriteLine($"isolated removal fixture: {entry.Path}, synthetic corridor {entrance}->{exit}; retail entrance not exercised");
            Run(terrain, paths, script, animation, definition.UpgradeCapacity(0) ?? 1,
                entrance, exit, Sibling, model.HeadSlotCount, check);
            return true;
        }
        check(false, "isolated removal fixture found no suitable real non-track ride");
        return false;
    }

    public static void Run(Model terrain, ParkPaths sourcePaths, byte[] script, Animation animation, int capacity,
                           ParkCell entrance, ParkCell exit, Func<string, byte[]> sibling, int headSlots,
                           Action<bool, string> check)
    {
        ParkRide Add(ParkSim sim) => sim.Add(1, "removal regression", entrance, 1, 1, script, animation,
                                           capacity, entrance, exit, out _, sibling: sibling, headSlots: headSlots)
            ?? throw new InvalidOperationException("Removal fixture script did not start");
        (ParkPaths Paths, ParkSim Sim, ParkVisitors Visitors, ParkRide Ride, Guest Guest) Fresh()
        {
            var paths = new ParkPaths(terrain);
            sourcePaths.Field.Cells.CopyTo(paths.Field.Cells, 0);
            var sim = new ParkSim(paths); var ride = Add(sim);
            sim.SetOpen(ride.Id, true); ride.Set("VAR_BROKEN", 0);
            var visitors = new ParkVisitors(sim, new GuestWalk(paths));
            var guest = visitors.Arrive(entrance, entrance);
            return (paths, sim, visitors, ride, guest);
        }
        void Check(bool ok, string message) => check(ok, "removal: " + message);
        void Queue(ParkVisitors visitors, ParkRide ride, Guest guest)
        {
            Check(visitors.SendTo(guest, ride), "fixture guest can reach the ride");
            visitors.Step(0, null);
            Check(visitors.Plans[guest.Id].Intent == VisitorIntent.Queued && !visitors.Walk.Guests.Contains(guest),
                  "fixture hands guest ownership to the ride");
        }
        bool Returned(ParkVisitors visitors, int id) => visitors.Walk.Guests.Count(g => g.Id == id) == 1
            && visitors.Plans[id].RideId == 0 && visitors.Plans[id].Intent == VisitorIntent.Wandering;

        var queued = Fresh(); Queue(queued.Visitors, queued.Ride, queued.Guest);
        int boarded = queued.Visitors.Boardings;
        queued.Sim.Remove(queued.Ride.Id); queued.Visitors.Step(0, null);
        Check(Returned(queued.Visitors, queued.Guest.Id), "deleting a queued ride restores the same guest once");
        Check(queued.Visitors.Rides == 0 && queued.Visitors.Boardings == boarded,
              "evacuation does not invent a completed ride or another boarding");
        queued.Sim.Remove(queued.Ride.Id);
        for (int i = 0; i < 3; i++) queued.Visitors.Step(0, null);
        Check(Returned(queued.Visitors, queued.Guest.Id), "repeated removal and steps do not duplicate the guest");
        var recoveredWalker = queued.Visitors.Walk.Guests.FirstOrDefault(g => g.Id == queued.Guest.Id);
        Check(recoveredWalker != null && !queued.Visitors.SendTo(recoveredWalker, queued.Ride),
              "a stale removed ride reference is rejected even for a currently tracked walker");

        var offered = Fresh(); Queue(offered.Visitors, offered.Ride, offered.Guest);
        offered.Sim.SetOpen(offered.Ride.Id, false);
        offered.Sim.Advance(.04);
        Check(!offered.Ride.Queue.Contains(offered.Guest.Id) && offered.Ride.Get("VAR_LETMEON") == offered.Guest.Id,
              "fixture exercises the pending-offer mailbox, not merely the queue");
        offered.Sim.Remove(offered.Ride.Id); offered.Visitors.Step(0, null);
        Check(Returned(offered.Visitors, offered.Guest.Id), "a pending offered guest survives ride removal");

        var riding = Fresh(); Queue(riding.Visitors, riding.Ride, riding.Guest);
        for (int i = 0; i < 6000 && !riding.Ride.Host.Seats.Values.Contains(riding.Guest.Id); i++)
            riding.Visitors.Step(.04, null);
        Check(riding.Ride.Host.Seats.Values.Contains(riding.Guest.Id), "fixture actually seats a guest using the real script");
        int finishedBefore = riding.Visitors.Rides;
        riding.Sim.Remove(riding.Ride.Id); riding.Visitors.Step(0, null);
        Check(Returned(riding.Visitors, riding.Guest.Id)
              && !riding.Sim.Rides.Any(r => r.Host.Seats.Values.Contains(riding.Guest.Id)),
              "a seated guest returns without remaining in any live ride's seats");
        Check(riding.Visitors.Rides == finishedBefore, "removing a running ride is not counted as finishing it");

        var reused = Fresh(); Queue(reused.Visitors, reused.Ride, reused.Guest);
        reused.Sim.Remove(reused.Ride.Id);
        var replacement = Add(reused.Sim); // closed, same numeric id, different instance
        reused.Visitors.Step(0, null);
        Check(Returned(reused.Visitors, reused.Guest.Id) && replacement.Queue.Count == 0,
              "reusing the numeric ride id cannot inherit the old ride's guest");

        var heading = Fresh();
        var starts = ParkPaths.Neighbours(entrance).Where(heading.Paths.Open).ToArray();
        Check(starts.Length > 0, "heading fixture has a neighboring public path cell");
        if (starts.Length > 0)
        {
            var walker = heading.Guest;
            Check(heading.Visitors.Walk.Send(walker, starts[0]), "heading fixture can move the original guest away");
            for (int i = 0; i < 100 && walker.State != GuestState.Arrived; i++) heading.Visitors.Walk.Advance(.04);
            Check(walker.State == GuestState.Arrived && heading.Visitors.Walk.Guests.Count == 1
                  && heading.Visitors.Plans.Count == 1, "heading fixture conserves its one original identity");
            Check(heading.Visitors.SendTo(walker, heading.Ride), "heading fixture has a route");
            heading.Visitors.Step(.04, null);
            var position = walker.Position;
            heading.Sim.Remove(heading.Ride.Id); heading.Visitors.Step(0, null);
            Check(Returned(heading.Visitors, walker.Id) && walker.Position == position,
                  "heading guest loses ride intent without teleporting or being recreated");
        }

        var fallback = Fresh(); Queue(fallback.Visitors, fallback.Ride, fallback.Guest);
        int pathMaterial = fallback.Paths.Field.Material(entrance.X, entrance.Z);
        foreach (var cell in new[] { entrance, exit }.Distinct())
            fallback.Paths.Field.Cells[(cell.Z * fallback.Paths.Field.Width + cell.X) * 2 + 1] = 0;
        Check(fallback.Paths.Cells.Any(fallback.Paths.Open), "fallback fixture retains public paths");
        fallback.Sim.Remove(fallback.Ride.Id); fallback.Visitors.Step(0, null);
        Check(Returned(fallback.Visitors, fallback.Guest.Id)
              && fallback.Paths.Open(fallback.Visitors.Walk.Guests.Single(g => g.Id == fallback.Guest.Id).Cell),
              "missing entrance/exit uses a remaining public path");

        var stranded = Fresh(); Queue(stranded.Visitors, stranded.Ride, stranded.Guest);
        for (int i = 1; i < stranded.Paths.Field.Cells.Length; i += 2) stranded.Paths.Field.Cells[i] = 0;
        stranded.Paths.SetEntrance(null);
        Check(!stranded.Paths.Cells.Any(stranded.Paths.Walkable), "no-ground fixture genuinely has nowhere to stand");
        stranded.Sim.Remove(stranded.Ride.Id); stranded.Visitors.Step(0, null);
        Check(stranded.Visitors.Plans.TryGetValue(stranded.Guest.Id, out var pending)
              && pending.Intent.ToString() == "Recovering" && pending.RideId == 0
              && !stranded.Visitors.Walk.Guests.Any(g => g.Id == stranded.Guest.Id),
              "no walkable ground retains explicit recovery ownership instead of dropping the guest");
        stranded.Paths.Lay(entrance, pathMaterial);
        stranded.Visitors.Step(0, null);
        Check(Returned(stranded.Visitors, stranded.Guest.Id), "restoring ground readmits the preserved guest exactly once");
        stranded.Visitors.Step(0, null);
        Check(stranded.Visitors.Walk.Guests.Count(g => g.Id == stranded.Guest.Id) == 1,
              "recovered guests are not readmitted a second time");

        foreach (bool beforeMailboxCollection in new[] { true, false })
        {
            var finished = Fresh(); Queue(finished.Visitors, finished.Ride, finished.Guest);
            finished.Ride.Set("VAR_STARTNOW", 1);
            bool Reported() => beforeMailboxCollection
                ? finished.Ride.Get("VAR_LETMEOFF") == finished.Guest.Id
                : finished.Ride.Left.Contains(finished.Guest.Id);
            for (int i = 0; i < 12000 && !Reported(); i++) finished.Sim.Advance(.04);
            Check(Reported(), "completion boundary fixture observes " + (beforeMailboxCollection ? "exit mailbox" : "collected Left"));
            finished.Sim.Remove(finished.Ride.Id); finished.Visitors.Step(0, null);
            Check(Returned(finished.Visitors, finished.Guest.Id) && finished.Visitors.Rides == 1,
                  "removal preserves a completion already reported by the script");
            finished.Visitors.Step(0, null);
            Check(finished.Visitors.Rides == 1, "preserved completion is counted once");
        }

        var normal = Fresh(); Queue(normal.Visitors, normal.Ride, normal.Guest);
        normal.Ride.Set("VAR_STARTNOW", 1);
        for (int i = 0; i < 12000 && !normal.Ride.Left.Contains(normal.Guest.Id); i++) normal.Sim.Advance(.04);
        Check(normal.Ride.Left.Contains(normal.Guest.Id), "normal-return fixture has a script-reported leaver");
        normal.Sim.SetOpen(normal.Ride.Id, false);
        normal.Visitors.Step(0, null);
        Check(Returned(normal.Visitors, normal.Guest.Id) && normal.Visitors.Rides == 1,
              "normal completion still returns and counts the guest once");
        normal.Visitors.Step(0, null);
        Check(normal.Visitors.Rides == 1 && normal.Visitors.Walk.Guests.Count == 1,
              "normal completion does not duplicate the body or count");

        var delayed = Fresh(); Queue(delayed.Visitors, delayed.Ride, delayed.Guest);
        delayed.Ride.Set("VAR_STARTNOW", 1);
        for (int i = 0; i < 12000 && !delayed.Ride.Left.Contains(delayed.Guest.Id); i++) delayed.Sim.Advance(.04);
        Check(delayed.Ride.Left.Contains(delayed.Guest.Id), "delayed completion fixture has a real script handback");
        delayed.Sim.SetOpen(delayed.Ride.Id, false);
        for (int i = 1; i < delayed.Paths.Field.Cells.Length; i += 2) delayed.Paths.Field.Cells[i] = 0;
        delayed.Paths.SetEntrance(null);
        delayed.Visitors.Step(0, null);
        Check(delayed.Visitors.Plans[delayed.Guest.Id].Intent == VisitorIntent.Recovering
              && delayed.Visitors.Walk.Guests.Count == 0 && delayed.Visitors.Rides == 0,
              "completion with no ground retains ownership and does not count a nonexistent readmission");
        delayed.Paths.Lay(exit, pathMaterial);
        delayed.Visitors.Step(0, null);
        Check(Returned(delayed.Visitors, delayed.Guest.Id) && delayed.Visitors.Rides == 1,
              "restoring an exit finishes delayed normal readmission");
        delayed.Visitors.Step(0, null);
        Check(delayed.Visitors.Rides == 1 && delayed.Visitors.Walk.Guests.Count == 1,
              "delayed completion counts and readmits exactly once");

        var clear = Fresh(); Queue(clear.Visitors, clear.Ride, clear.Guest);
        clear.Sim.Clear(); clear.Visitors.Step(0, null);
        Check(Returned(clear.Visitors, clear.Guest.Id), "clearing ride instances cannot orphan visitor ownership");

        GuestConservationChecks.Run(terrain, sourcePaths, script, animation, capacity, entrance, exit, sibling, headSlots, check);
        NeedsLifecycleChecks.Run(terrain, sourcePaths, script, animation, capacity, entrance, exit, sibling, headSlots, check);
    }
}
