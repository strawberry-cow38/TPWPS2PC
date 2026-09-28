using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;

/// <summary>Disc-free manager leaves. Run explicitly from the enclosing audit runner.</summary>
public static class StaffLeafSaveChecks
{
    static string Json<T>(T v) => JsonSerializer.Serialize(v);
    static T Copy<T>(T v) => JsonSerializer.Deserialize<T>(Json(v))!;
    sealed class Stream
    {
        internal uint Word = 17;
        internal int Draws;
        internal int Next(int n) { Draws++; Word = unchecked(Word * 1664525 + 1013904223); return (int)(Word % (uint)n); }
    }
    public static int Run()
    {
        int count = 0;
        void C(bool ok, string why) { if (!ok) throw new InvalidOperationException("staff leaves: " + why); count++; }
        void Reject(Action action, string why)
        {
            bool rejected = false;
            try { action(); } catch (ArgumentException) { rejected = true; } catch (JsonException) { rejected = true; }
            C(rejected, why);
        }
        void Required<T>(T value)
        {
            // Every property, including nullable bindings, on every nested DTO is required.
            void Visit(JsonNode node, List<object> path)
            {
                if (node is JsonObject obj)
                    foreach (var p in obj.ToArray()) {
                        var root = JsonNode.Parse(Json(value))!; JsonNode at = root;
                        foreach (var key in path) at = key is int i ? at[i]! : at[(string)key]!;
                        at.AsObject().Remove(p.Key);
                        Reject(() => JsonSerializer.Deserialize<T>(root.ToJsonString()), "required " + typeof(T).Name + "/" + p.Key);
                        if (p.Value is JsonObject or JsonArray) Visit(p.Value, new(path) { p.Key });
                    }
                else if (node is JsonArray array)
                    for (int i = 0; i < array.Count; i++)
                        if (array[i] is JsonObject or JsonArray) Visit(array[i]!, new(path) { i });
            }
            Visit(JsonNode.Parse(Json(value))!, new());
        }
        string NoId(Delegate _) => throw new InvalidOperationException("unexpected callback capture");
        Delegate NoResolve(string _) => throw new InvalidOperationException("unexpected callback resolve");
        StaffMember NoStaff(string _) => throw new InvalidOperationException("unexpected staff resolve");
        var rng = new Stream();
        var candidates = new StaffCandidateDatabase(rng.Next);
        var cs = Copy(candidates.CaptureState());
        cs.Records[3] = cs.Records[3] with { Available = false, NameRow = 321, RecordConstant = 99 };
        var candidateCopy = StaffCandidateDatabase.FromState(cs);
        C(Json(cs) == Json(candidateCopy.CaptureState()) && rng.Draws == 50, "candidate immutable data/availability/no rolls");
        cs.Records[0] = cs.Records[0] with { Motivation = 789 };
        C(candidateCopy.All[0].Motivation != 789, "candidate detached records");
        Required(candidateCopy.CaptureState());
        Reject(() => StaffCandidateDatabase.FromState(cs with { Records = cs.Records.Reverse().ToArray() }), "candidate order");

        int eventsA = 0, eventsB = 0;
        var research = new ResearchManager { Researched = _ => eventsA++, Advisor = _ => eventsA++,
            AllResearched = () => eventsA++, AnythingLeftToResearch = () => false, ItemLevel = (_, _) => 2,
            MechanicCount = () => 0 };
        var restoredResearch = new ResearchManager { Researched = _ => eventsB++, Advisor = _ => eventsB++,
            AllResearched = () => eventsB++, AnythingLeftToResearch = () => false, ItemLevel = (_, _) => 2,
            MechanicCount = () => 0 };
        Dictionary<string, Delegate> Bindings(ResearchManager m) => new() { ["r"] = m.Researched,
            ["a"] = m.Advisor, ["all"] = m.AllResearched, ["left"] = m.AnythingLeftToResearch,
            ["level"] = m.ItemLevel, ["mechanics"] = m.MechanicCount };
        var ba = Bindings(research); var bb = Bindings(restoredResearch);
        string KeyA(Delegate d) => ba.Single(p => ReferenceEquals(p.Value, d)).Key;
        string KeyB(Delegate d) => bb.Single(p => ReferenceEquals(p.Value, d)).Key;
        research.Start(2, 1, 4, 17); research.Start(0, 8, 7, 29); research.Contribute(2);
        var rs = Copy(research.CaptureState(KeyA));
        var shells = restoredResearch.Slots.ToArray();
        restoredResearch.RestoreState(rs, key => bb[key]);
        C(eventsB == 0 && Json(rs) == Json(restoredResearch.CaptureState(KeyB)), "research no events/exact state");
        C(shells.SequenceEqual(restoredResearch.Slots) && restoredResearch.ProjectSlot(shells[2]) == 2
            && ReferenceEquals(shells[2], restoredResearch.ProjectAt(2)), "research shell identity");
        for (int i = 0; i < 20; i++) {
            research.Contribute(5); restoredResearch.Contribute(5);
            C(Json(research.CaptureState(KeyA)) == Json(restoredResearch.CaptureState(KeyB)) && eventsA == eventsB,
                "research pulse continuation");
        }
        string before = Json(restoredResearch.CaptureState(KeyB));
        Reject(() => restoredResearch.RestoreState(rs with { AdvisorKey = "left" }, key => bb[key]), "wrong callback type");
        C(before == Json(restoredResearch.CaptureState(KeyB)), "research invalid binding atomic");
        Required(rs);
        var nullResearch = new ResearchManager().CaptureState(NoId);
        restoredResearch.RestoreState(Copy(nullResearch), NoResolve);
        C(restoredResearch.Advisor == null && restoredResearch.ItemLevel == null, "research null presence overwrites bindings");

        var activationA = new NativeActivationSequence(99, "leaf audit");
        var litter = new ParkLitter(rng.Next, activationA);
        for (int i = 0; i < 6; i++) litter.Drop(new(400, 700), i % 2 == 0, i % 3 == 0);
        // Remove is internal because gameplay only calls it after the handyman's sweep. Invoke
        // that exact operation, not a DTO mutation, without widening the production gameplay API.
        var remove = typeof(ParkLitter).GetMethod("Remove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        void Sweep(ParkLitter p, LitterItem item) => remove.Invoke(p, new object[] { item });
        Sweep(litter, litter.Active[2]); Sweep(litter, litter.Active[0]);
        var ls = Copy(litter.CaptureState(_ => "staff", NoId));
        var streamB = new Stream { Word = rng.Word, Draws = rng.Draws };
        Func<int,int> sharedRandomB = streamB.Next;
        var activationB = NativeActivationSequence.FromState(Copy(activationA.CaptureState()));
        int draws = streamB.Draws;
        var litterB = ParkLitter.FromState(ls, sharedRandomB, activationB, NoStaff, NoResolve);
        C(draws == streamB.Draws && ReferenceEquals(litterB.RandomSource, sharedRandomB)
            && ReferenceEquals(litterB.Activations, activationB), "litter shared resources/no constructor rolls");
        C(Json(ls) == Json(litterB.CaptureState(_ => "staff", NoId)), "all litter stale slots and ordered lists");
        for (int i = 0; i < 50; i++) {
            if (i % 3 == 0 && litter.Count > 0) { Sweep(litter, litter.Active[^1]); Sweep(litterB, litterB.Active[^1]); }
            litter.Drop(new(800, 900), i % 2 == 0); litterB.Drop(new(800, 900), i % 2 == 0);
            C(Json(litter.CaptureState(_ => "staff", NoId)) == Json(litterB.CaptureState(_ => "staff", NoId))
                && rng.Word == streamB.Word && rng.Draws == streamB.Draws
                && Json(activationA.CaptureState()) == Json(activationB.CaptureState()), "litter add/sweep continuation");
        }
        Required(ls);
        var badFree = (int[])ls.Free.Clone(); badFree[0] = badFree[1];
        string seqBefore = Json(activationB.CaptureState()); draws = streamB.Draws;
        Reject(() => ParkLitter.AllocateState(ls with { Free = badFree }, sharedRandomB, activationB, NoResolve), "duplicate litter slot");
        C(draws == streamB.Draws && seqBefore == Json(activationB.CaptureState()), "malformed litter no effects");

        // Two-phase staff/litter cycle, including an intentionally stale inactive claim.
        var paths = new ParkPaths(Terrain());
        var walk = new GuestWalk(paths);
        var staffOwner = new ParkStaff(new ParkVisitors(new ParkSim(paths), walk), new ParkClock(),
            new NativeActivationSequence(0, "leaf staff fixture"), _ => 0);
        var staffShell = staffOwner.Free(StaffKind.Handyman)[0];
        var claimed = Copy(ls);
        claimed.Slots[0] = claimed.Slots[0] with { ClaimantKey = "staff/0" };
        int litterEvents = 0;
        Action<LitterItem> added = _ => litterEvents++;
        Action<LitterItem> removed = _ => litterEvents++;
        claimed = claimed with { AddedKey = "added", RemovedKey = "removed" };
        Delegate ResolveLitter(string key) => key == "added" ? added : removed;
        string LitterKey(Delegate d) => ReferenceEquals(d, added) ? "added" : "removed";
        var stagedLitter = ParkLitter.AllocateState(claimed, sharedRandomB, activationB, ResolveLitter);
        var memberState = Copy(staffShell.CaptureState(_ => throw new InvalidOperationException()))
            with { TargetKey = "litter/0" };
        staffShell.RestoreState(memberState, key => key == "litter/0" ? stagedLitter.Slots[0] : throw new ArgumentException());
        C(stagedLitter.Slots[0].Claimant == null && litterEvents == 0, "unpublished litter shells/no callbacks");
        stagedLitter.HydrateClaimants(claimed, key => key == "staff/0" ? staffShell : throw new ArgumentException());
        C(ReferenceEquals(staffShell.Target, stagedLitter.Slots[0])
            && ReferenceEquals(stagedLitter.Slots[0].Claimant, staffShell)
            && !stagedLitter.Slots[0].Active, "cycle and stale inactive claimant preserved");
        C(Json(claimed) == Json(stagedLitter.CaptureState(_ => "staff/0", LitterKey)), "claimed callback-bound snapshot exact");
        var invalidClaim = Copy(claimed);
        invalidClaim.Slots[1] = invalidClaim.Slots[1] with { ClaimantKey = "missing" };
        Reject(() => stagedLitter.HydrateClaimants(invalidClaim, key => key == "staff/0" ? staffShell : null!), "unresolved claimant");
        C(ReferenceEquals(stagedLitter.Slots[0].Claimant, staffShell) && stagedLitter.Slots[1].Claimant == null
            && litterEvents == 0, "claimant failure no writes/callbacks");
        var newItem = stagedLitter.Drop(new(300, 300), false);
        Sweep(stagedLitter, newItem);
        C(litterEvents == 2, "rebound add/sweep callbacks execute only on continuation");
        var rides = new List<ParkRide> { new() { Width = 2, Height = 2, Origin = new(3, 3), ServiceEntry = new(3, 3) } };
        int calls = 0;
        Func<IEnumerable<ParkRide>> placed = () => { calls++; return rides; };
        var tiles = new NativeTileView(paths, placed); tiles.Refresh(); rides.Clear();
        var ts = Copy(tiles.CaptureState("paths", _ => "placed"));
        var tileCopy = NativeTileView.FromState(ts, "paths", paths, _ => placed);
        C(calls == 1 && ReferenceEquals(paths, tileCopy.Paths) && ReferenceEquals(placed, tileCopy.PlacedProvider), "tile shared paths/binding no provider call");
        C(tileCopy.Kind(3, 3) == NativeTileView.KindBuildingEntry && Json(ts) == Json(tileCopy.CaptureState("paths", _ => "placed")), "tile stale cache exact");
        tiles.Refresh(); tileCopy.Refresh();
        C(calls == 3 && tileCopy.Kind(3, 3) == tiles.Kind(3, 3) && tileCopy.Kind(3, 3) != NativeTileView.KindBuildingEntry, "tile refresh continuation");
        Required(ts);
        Reject(() => NativeTileView.FromState(ts with { Buildings = new[] { ts.Buildings[0], ts.Buildings[0] } }, "paths", paths, _ => placed), "duplicate cache cells");
        Reject(() => NativeTileView.FromState(ts, "other", paths, _ => placed), "paths registry mismatch");
        C(calls == 3, "malformed tiles no provider invocation");
        return count;
    }
    static Model Terrain()
    {
        var data = new byte[0x240];
        void U(int at, uint v) => BitConverter.GetBytes(v).CopyTo(data, at);
        void F(int at, float v) => BitConverter.GetBytes(v).CopyTo(data, at);
        U(0, Model.Magic); U(0x44, 0x140); U(0x48, 0x80); data[0x30] = 1;
        U(0x14c, 8); U(0x150, 8); F(0x158, 1);
        for (int i = 0; i < 64; i++) data[0x170 + i * 2 + 1] = 1;
        U(0xd4, 0x220); System.Text.Encoding.ASCII.GetBytes("heightfield\0").CopyTo(data, 0x220);
        for (int i = 0; i < 4; i++) F(0x90 + i * 20, 1);
        F(0x100, 8 * 1.004f); F(0x108, 8 * 1.004f);
        var model = new Model(data); model.Materials.Add("sentinel"); model.Materials.Add("jpa_str1"); return model;
    }
}
