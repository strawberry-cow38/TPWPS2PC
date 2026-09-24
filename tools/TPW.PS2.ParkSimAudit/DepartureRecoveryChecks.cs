using TPW.PS2.Data;

/// <summary>A guest committed to going home must retry after a broken path is repaired.
/// Checks owner/needs conservation while blocked, not a snap or forced retirement.</summary>
static class DepartureRecoveryChecks
{
    public static void Run(Model terrain, ParkPaths source, ParkEntrance entrance, ParkCell start,
                           Action<bool, string> check)
    {
        void Check(bool ok, string text) => check(ok, "departure recovery: " + text);
        var paths = new ParkPaths(terrain); source.Field.Cells.CopyTo(paths.Field.Cells, 0);
        paths.SetEntrance(entrance);
        var gate = paths.EntranceCells.OrderBy(c => c.Z).ThenBy(c => c.X).First();
        var route = paths.Route(start, gate) ?? throw new InvalidOperationException("departure fixture has no initial route");
        var cut = route.Skip(2).First(c => !paths.IsEntrance(c));
        // Keep only this actual route, excluding accidental alternate paths from the terrain.
        var retain = route.ToHashSet();
        foreach (var cell in paths.Cells)
            if (!retain.Contains(cell)) paths.Field.Cells[(cell.Z * paths.Field.Width + cell.X) * 2 + 1] = 0;
        int index = (cut.Z * paths.Field.Width + cut.X) * 2 + 1;
        byte before = paths.Field.Cells[index];
        var visitors = new ParkVisitors(new ParkSim(paths), new GuestWalk(paths)) { Needs = new VisitorNeeds(77) };
        foreach (string key in visitors.Needs.Rates.Keys.ToArray()) visitors.Needs.Rates[key] = new VisitorNeeds.Rate(0,0,false);
        var guest = visitors.Arrive(start, start);
        var want = visitors.Needs.Of(guest.Id); want.Cash = 0; want.Happiness = 80;
        want.Hunger = want.Thirst = want.Toilet = want.Sick = want.Boredom = 0;
        visitors.Needs.Set(guest.Id, want); visitors.Step(0, null);
        Check(visitors.Plans[guest.Id].Intent == VisitorIntent.Leaving, "guest actually commits to leaving before disruption");
        paths.Field.Cells[index] = 0;
        // Allow the coordinator to retry: Stranded need not persist across a whole Step.
        // Movement on the still-valid first edge is allowed; crossing the broken route is not.
        for (int tick = 0; tick < 200; tick++) visitors.Step(.04, null);
        Check(visitors.WentHome == 0 && guest.Cell != gate && guest.Steps > 0
              && visitors.Walk.Route(guest.Cell, gate) == null,
              "broken route prevents departure after actual movement, without pinning retry state");
        Check(visitors.Needs.Has(guest.Id) && visitors.Walk.Guests.Count(g => g.Id == guest.Id) == 1,
              "blocked departure retains one guest and its needs");
        paths.Field.Cells[index] = before;
        Check(visitors.Walk.Route(guest.Cell, gate) != null, "repair restores an actual route to the gate");
        for (int tick = 0; tick < 6000 && visitors.WentHome == 0; tick++) visitors.Step(.04, null);
        Check(visitors.WentHome == 1, "repaired departure resumes and reaches the gate");
        Check(!visitors.Plans.ContainsKey(guest.Id) && !visitors.Needs.Has(guest.Id) && !visitors.Walk.Guests.Any(g => g.Id == guest.Id),
              "successful resumed departure retires ownership and needs together");
    }
}
