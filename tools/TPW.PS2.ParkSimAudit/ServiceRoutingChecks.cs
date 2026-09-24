using TPW.PS2.Data;

/// <summary>Disconnected nearest facility must not turn an urgent errand into a random ride.
/// Both reachability and distance are controlled, with a real alternative attraction to expose
/// the fallback. Eventual relief alone cannot distinguish the broken routing policy.</summary>
static class ServiceRoutingChecks
{
    public static void Run(Model terrain, byte[] script, Animation animation, RideDefinition definition,
                           Func<string, byte[]> sibling, Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "service routing: " + label);
        var paths = new ParkPaths(terrain);
        // In-memory fixture only: remove authored paths so a distant connection cannot bridge
        // the isolated near cell. Keep all terrain buildability/height bytes unchanged.
        for (int i = 1; i < paths.Field.Cells.Length; i += 2) paths.Field.Cells[i] = 0;
        var start = paths.Cells.First(c => Enumerable.Range(0, 5).All(x => paths.CanLay(c.Offset(x, 0)))
                                          && paths.CanLay(c.Offset(0, 2)));
        int material = Enumerable.Range(1, paths.Materials.Count - 1)
            .First(i => ParkPaths.Classify(paths.Materials[i]) == ParkPathKind.Path);
        for (int x = 0; x < 5; x++) paths.Lay(start.Offset(x, 0), material);
        var near = start.Offset(0, 2); var far = start.Offset(4, 0); paths.Lay(near, material);
        var sim = new ParkSim(paths); var visitors = new ParkVisitors(sim, new GuestWalk(paths), () => 2)
            { Needs = new VisitorNeeds(2026) };
        foreach (string key in visitors.Needs.Rates.Keys.ToArray()) visitors.Needs.Rates[key] = new VisitorNeeds.Rate(0, 0, false);
        ParkRide Place(int id, ParkCell at, bool relief)
        {
            // Same real handover script for the distractor, but no relief definition: what the
            // facility IS, not whether its script finishes, selects satisfaction.
            var ride = sim.Add(id, relief ? "toilet" : "distractor", at, 1, 1, script, animation, 1,
                               at, start, out var fault, sibling: sibling, definition: relief ? definition : null)
                       ?? throw new InvalidOperationException(fault);
            sim.SetOpen(id, true); ride.Set("VAR_BROKEN", 0); return ride;
        }
        Place(1, near, true); var target = Place(2, far, true); Place(3, start.Offset(1, 0), false);
        Check(visitors.Walk.Route(start, near) == null && visitors.Walk.Route(start, far) != null,
              "near facility is disconnected while farther facility is reachable");
        Check(Math.Abs(near.X-start.X)+Math.Abs(near.Z-start.Z) < Math.Abs(far.X-start.X)+Math.Abs(far.Z-start.Z),
              "disconnected facility really is the nearest candidate");
        var guest = visitors.Arrive(start, start);
        var wants = visitors.Needs.Of(guest.Id); wants.Toilet = 100; wants.Cash = 1234;
        wants.Hunger = wants.Thirst = wants.Sick = 0; wants.Happiness = 50; visitors.Needs.Set(guest.Id, wants);
        visitors.Step(0, null);
        Check(visitors.Plans[guest.Id].Intent == VisitorIntent.Heading && guest.Destination == far,
              "unreachable nearest does not degrade urgent errand to the distracting ride");
        for (int tick = 0; tick < 6000 && visitors.Relieved == 0; tick++) visitors.Step(.04, null);
        Check(visitors.Relieved == 1 && visitors.Rides == 1 && visitors.Boardings == 1,
              "exactly one facility used before relief, not merely eventual success");
        Check(visitors.Needs.Of(guest.Id).Toilet == 0 && visitors.Needs.Of(guest.Id).Cash == 1234,
              "reachable service answers the need without reseeding cash");
    }
}
