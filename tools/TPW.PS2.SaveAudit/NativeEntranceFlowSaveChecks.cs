using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;
using Flow = TPW.PS2.Data.NativeEntranceFlow;

/// <summary>Disc-free bounded owner test. Provider values below are copied OUTSIDE the
/// entrance DTO; this is deliberately not a total save. Pending results use the same mailbox owner as the Viewer.</summary>
public static class NativeEntranceFlowSaveChecks
{
    static string Json<T>(T value) => JsonSerializer.Serialize(value);
    static T Round<T>(T value) => JsonSerializer.Deserialize<T>(Json(value))!;

    sealed class Provider
    {
        internal GuestWalk Walk = null!;
        internal int Calls, RandomIndex, Accepts, Acknowledgements;
        internal bool ExitOpen, FailDeparture, RefuseAlternate;
        internal Action? OnPump;
        internal NativeEntranceMailbox Mail = new();
        internal readonly List<string> Events = new();
        internal Flow.Services Services = null!;
        internal Provider(GuestWalk walk)
        {
            Walk = walk;
            Services = new(n => { Calls++; return RandomIndex++ % n; },
                g => { Calls++; return new(640, 640); }, new(640, 640),
                (_, _, _, _, _) => throw new Exception("detailed provider expected"),
                () => { Calls++; OnPump?.Invoke(); return Mail.Drain(); },
                _ => { Calls++; return true; }, _ => { Calls++; return true; },
                () => { Calls++; return 0x4000; },
                g => { Calls++; Accepts++; Events.Add("accept:" + g.Id); return g.Id % 2 != 0; },
                _ => { Calls++; return ExitOpen ? new Point(640, 640) : null; },
                g => { Calls++; Events.Add("reject:" + g.Id); },
                (g, owner) => { Calls++; Walk.StepOwnedNative(g, owner); },
                trace: t => { Calls++; Events.Add(t.Event + ":" + t.Entry.Guest.Id); },
                busPoint: (_, _) => { Calls++; return new(640, 640); },
                requestDetailed: r =>
                {
                    Calls++; Events.Add($"request:{r.Token}:{r.Guest.Id}:{r.Mode}:{r.Flags}");
                    if (RefuseAlternate && r.Flags == 0x23) return false;
                    var result = new Flow.RouteResult(r.Token, r.Guest,
                        FailDeparture && r.Mode == 14 ? null : new[] { r.Target },
                        FailDeparture && r.Mode == 14 ? "deterministic failure" : null);
                    Mail.Enqueue(result); Mail.Enqueue(result); // second completion MUST be stale
                    return true;
                },
                afterResult: r => { Calls++; Acknowledgements++; Events.Add("ack:" + r.Token); },
                recovery: (g, mode) => { Calls++; Events.Add($"recover:{g.Id}:{mode}"); },
                slotAdvanced: g => { Calls++; Events.Add("slot:" + g.Id); });
        }
        internal void CopyFrom(Provider p, Func<Guest, Guest> guest)
        {
            Calls = p.Calls; RandomIndex = p.RandomIndex; Accepts = p.Accepts;
            Acknowledgements = p.Acknowledgements; ExitOpen = p.ExitOpen;
            FailDeparture = p.FailDeparture; RefuseAlternate = p.RefuseAlternate;
            Events.AddRange(p.Events);

        }
    }

    sealed class World
    {
        internal ParkPaths Paths = null!;
        internal ParkSim Sim = null!;
        internal ParkVisitors Visitors = null!;
        internal GuestWalk Walk = null!;
        internal Flow Flow = null!;
        internal Provider Provider = null!;
        internal Guest? Extra;
        internal int Traffic;
        internal GuestWalk.GuestGraph Graph()
        {
            var inputs = Flow.ReferencedGuests.Where(Flow.Owns)
                .ToDictionary(Flow.StateInputs, g => "entrance/input/" + g.Id);
            return Walk.CaptureGraph("entrance/grid", new()
            {
                IdentifyNativeOwner = o => ReferenceEquals(o, Flow.StateOwner) ? "entrance/owner" : throw new ArgumentException(),
                IdentifyNativeInputs = i => inputs[i]
            }, Visitors.ReferencedGuests.Concat(Flow.ReferencedGuests).Concat(Provider.Mail.ReferencedGuests)
                .Concat(Extra == null ? Array.Empty<Guest>() : new[] { Extra }));
        }
        internal Flow.SnapshotBindings Bind(GuestWalk.GuestGraph graph) => new()
        {
            GuestGraph = graph, OwnerId = "entrance/owner", ServicesId = "entrance/provider",
            Services = Provider.Services,
            IdentifyInputs = i => "entrance/input/" + Flow.ReferencedGuests.Single(g => Flow.Owns(g)
                && ReferenceEquals(Flow.StateInputs(g), i)).Id
        };
        internal Flow.Snapshot Save() { var g = Graph(); return Flow.CaptureState(Bind(g)); }
        internal void Tick(uint tick) => Traffic = Flow.Tick(tick, 2, Traffic);
    }

    static World New(int count = 6)
    {
        var w = new World { Paths = new ParkPaths(Terrain()) };
        w.Walk = new(w.Paths); w.Sim = new(w.Paths); w.Visitors = new(w.Sim, w.Walk);
        w.Provider = new(w.Walk); w.Flow = new(w.Visitors, w.Provider.Services);
        for (int i = 0; i < count; i++)
        {
            var g = w.Visitors.Arrive(new(2, 2), new(2, 2));
            w.Flow.Add(g, 15, (uint)g.Id);
        }
        return w;
    }

    public sealed record FileBundle(ParkPaths.State Paths, GuestWalk.State Walk,
        ParkVisitors.State Visitors, Flow.Snapshot Flow, NativeEntranceMailbox.State Mailbox);
    static FileBundle FileCut(World w, GuestWalk.GuestGraph g)
    {
        string path=Path.Combine(Path.GetTempPath(),"tpw-entrance-"+Guid.NewGuid().ToString("N")+".save");
        try {
            ParkSaveFile.Write(path,new FileBundle(w.Paths.CaptureState("entrance/grid"),g.Snapshot,
                w.Visitors.CaptureState(new(){GuestGraph=g}),w.Flow.CaptureState(w.Bind(g)),w.Provider.Mail.CaptureState(g)));
            return ParkSaveFile.Read<FileBundle>(path);
        } finally {File.Delete(path);File.Delete(path+".bak");}
    }

    static World Restore(World source, Action<bool, string> check)
    {
        var graph = source.Graph();
        var bundle = FileCut(source,graph);
        var snapshot = bundle.Flow;
        var visitors = bundle.Visitors;
        var fresh = new World { Paths = ParkPaths.FromState(bundle.Paths,
            "entrance/grid", Terrain()), Traffic = source.Traffic };
        var g = GuestWalk.AllocateState(bundle.Walk, "entrance/grid", fresh.Paths);
        fresh.Walk = g.Walk; fresh.Sim = new(fresh.Paths);
        fresh.Visitors = ParkVisitors.AllocateState(visitors, fresh.Sim, new() { GuestGraph = g });
        fresh.Provider = new(fresh.Walk);
        Guest Map(Guest old) => g.GuestByGraphId(graph.GuestGraphId(old));
        fresh.Provider.CopyFrom(source.Provider, Map); // other EXTERNAL provider values
        fresh.Provider.Mail = NativeEntranceMailbox.FromState(bundle.Mailbox, g);
        fresh.Extra = source.Extra == null ? null : Map(source.Extra);
        int before = fresh.Provider.Calls;
        fresh.Flow = Flow.AllocateState(snapshot, fresh.Visitors, fresh.Bind(g));
        bool blocked = false;
        try { fresh.Flow.Tick(0, 0, 0); } catch (InvalidOperationException) { blocked = true; }
        check(blocked, "unhydrated shell cannot tick");
        var inputs = snapshot.Entries.ToDictionary(e => e.InputsId,
            e => fresh.Flow.StateInputs(g.GuestByGraphId(e.GuestGraphId)));
        // Mutating caller DTO after allocation must not mutate the shell's orders or fields.
        Array.Reverse(snapshot.Active);
        g.Hydrate(new()
        {
            ResolveNativeOwner = id => id == "entrance/owner" ? fresh.Flow.StateOwner : throw new ArgumentException(),
            ResolveNativeInputs = id => inputs[id]
        });
        fresh.Visitors.HydrateStateBindings(new() { GuestGraph = g });
        fresh.Flow.HydrateStateBindings();
        check(before == fresh.Provider.Calls, "zero callbacks/RNG/request/step during restore");
        check(Json(source.Save()) == Json(fresh.Save()), "full detached file roundtrip");
        check(Json(source.Provider.Mail.CaptureState(graph))==Json(fresh.Provider.Mail.CaptureState(g)),
            "pending result arrays/order/failures/identity through actual file");
        check(!ReferenceEquals(source.Flow.StateOwner, fresh.Flow.StateOwner), "fresh owner shell");
        return fresh;
    }

    static void Required<T>(T dto,Action<bool,string> check)
    {
        var json=JsonNode.Parse(Json(dto))!.AsObject();
        foreach(var key in json.Select(p=>p.Key).ToArray()) {
            var missing=json.DeepClone().AsObject();missing.Remove(key);bool rejected=false;
            try{JsonSerializer.Deserialize<T>(missing.ToJsonString());}catch(JsonException){rejected=true;}
            check(rejected,"mailbox required field "+key);
        }
    }

    public static int Run()
    {
        int count = 0;
        void C(bool ok, string why)
        { if (!ok) throw new InvalidOperationException("entrance save: " + why); count++; }
        var source = New(); var restored = Restore(source, C);
        var seen = new HashSet<Flow.State>();
        bool sawBothGroups = false, sawPending = false, sawAdmitted = false;
        for (uint tick = 0; tick < 240; tick++)
        {
            source.Provider.ExitOpen = restored.Provider.ExitOpen = tick >= 90;
            source.Tick(tick); restored.Tick(tick);
            var a = source.Save(); var b = restored.Save();
            foreach (var e in a.Entries) seen.Add(e.State);
            sawBothGroups |= a.Group0.Length != 0 && a.Group1.Length != 0;
            sawPending |= a.Entries.Any(e => e.Token.HasValue);
            sawAdmitted |= a.Admitted.Length != 0;
            C(Json(a) == Json(b) && source.Traffic == restored.Traffic, "real Tick state/order continuation " + tick);
            C(Json(source.Graph().Snapshot) == Json(restored.Graph().Snapshot), "real shared cursor/pool continuation " + tick);
            C(source.Provider.Calls == restored.Provider.Calls && source.Provider.RandomIndex == restored.Provider.RandomIndex
                && source.Provider.Events.SequenceEqual(restored.Provider.Events), "ordered external effects " + tick);
            if (tick is 0 or 1 or 3 or 8 or 16 or 32 or 33 or 48 or 64 or 65 or 80 or 100 or 160)
                restored = Restore(restored, C);
            if (tick == 145)
            {
                var ga = source.Flow.ReferencedGuests.First(g => !source.Flow.Owns(g));
                var gb = restored.Flow.ReferencedGuests.Single(g => g.Id == ga.Id);
                C(source.Flow.TryDepart(ga) && restored.Flow.TryDepart(gb), "admitted serial/baseline reused for ordinary departure");
            }
        }
        C(sawBothGroups && sawPending && sawAdmitted, "non-inert groups, pending requests and admitted inventory");
        C(seen.Contains(Flow.State.Accepted) && seen.Contains(Flow.State.Rejected)
            && seen.Contains(Flow.State.Staged) && seen.Contains(Flow.State.Waiting), "multiple lifecycle states captured");
        C(source.Provider.Accepts == 6 && restored.Provider.Accepts == 6, "accept-once through blocked exit and repeated restores");
        C(source.Provider.Acknowledgements > 12, "stale duplicate results acknowledged");

        // Mode14 failure: accepted alternate and decision hold; separately refused alternate
        // leaves Pending without a token. Both are real Tick states, not rewritten fixtures.
        foreach (bool refuse in new[] { false, true })
        {
            var w = New(2); w.Provider.FailDeparture = true; w.Provider.RefuseAlternate = refuse;
            bool found = false;
            for (uint t = 0; t < 240; t++)
            {
                w.Tick(t);
                if (!w.Flow.Observations.Any(o => refuse ? o.State == Flow.State.Pending && !o.PendingToken.HasValue
                    : o.State == Flow.State.DecisionBoundary)) continue;
                var f = Restore(w, C);
                for (uint next = t + 1; next < t + 40; next++)
                { w.Tick(next); f.Tick(next); C(Json(w.Save()) == Json(f.Save()), "alternate/hold continuation"); }
                found = true; break;
            }
            C(found, refuse ? "tokenless pending captured" : "alternate failure hold captured");
        }

        var inactive = New(1); inactive.Tick(0); inactive.Tick(1);
        var gone = inactive.Flow.ReferencedGuests.Single();
        inactive.Visitors.DiscardEntranceGuest(gone, inactive.Flow.StateOwner);
        inactive.Provider.Mail.Enqueue(new(1, gone, new[] { new Point(640, 640) }, null));
        inactive.Extra = new GuestWalk(inactive.Paths).Readmit(gone.Id, new(2, 2), new(2, 2));
        C(inactive.Flow.ReferencedGuests.Contains(gone), "vanished entry exposed before safety cleanup");
        var ig = inactive.Graph();
        C(ig.GuestGraphId(gone) != ig.GuestGraphId(inactive.Extra), "actual object graph IDs distinguish reused numeric ID");
        var freshInactive = Restore(inactive, C);
        inactive.Tick(2); freshInactive.Tick(2);
        C(Json(inactive.Save()) == Json(freshInactive.Save()) && freshInactive.Flow.ReferencedGuests.Count == 0,
            "vanished membership cleanup and stale pending results continue");

        var malformed = New(2); malformed.Tick(0);
        var mg = malformed.Graph(); var binding = malformed.Bind(mg);
        var good = malformed.Flow.CaptureState(binding);
        var mailbox=new NativeEntranceMailbox();var body=malformed.Flow.ReferencedGuests[0];
        mailbox.Enqueue(new(0,body,null,null)); // allowed stale/missing outcome, not repaired
        mailbox.Enqueue(new(ulong.MaxValue,body,Array.Empty<Point>(),"failed"));
        mailbox.Enqueue(new(7,body,new[]{new Point(-17,321)},null));
        var mailDto=Round(mailbox.CaptureState(mg));
        var loadedMail=NativeEntranceMailbox.FromState(mailDto,mg);
        C(Json(mailbox.CaptureState(mg))==Json(loadedMail.CaptureState(mg)),"null/empty/failure/stale tokens preserved");
        mailDto.Pending[2].Waypoints![0]=new(){X=9,Z=9};
        var batch=loadedMail.Drain();
        C(batch.Length==3 && batch[0].Waypoints==null && batch[1].Waypoints!.Length==0
            && batch[2].Waypoints![0]==new Point(-17,321) && loadedMail.Count==0,"detached points and pump drains once");
        C(loadedMail.Drain().Length==0,"empty pump does not replay results");
        bool badMail=false;
        try{NativeEntranceMailbox.FromState(mailDto with {Pending=new[]{mailDto.Pending[0] with {GuestGraphId=999999}}},mg);}
        catch(ArgumentException){badMail=true;}
        C(badMail && mailbox.Count==3,"bad pending reference cannot mutate original mailbox");
        Required(mailDto,C);Required(mailDto.Pending[0],C);Required(mailDto.Pending[2].Waypoints![0],C);

        void Bad(Flow.Snapshot bad, string why)
        {
            int calls = malformed.Provider.Calls; string before = Json(malformed.Save());
            bool rejected = false;
            try { Flow.ValidateSnapshot(bad, malformed.Visitors, binding); } catch (ArgumentException) { rejected = true; }
            C(rejected && calls == malformed.Provider.Calls && before == Json(malformed.Save()), "reject without side effects: " + why);
        }
        Bad(good with { Version = 9 }, "version");
        Bad(good with { Active = new[] { good.Active[0], good.Active[0] } }, "duplicate membership");
        Bad(good with { Active = Array.Empty<int>() }, "missing membership");
        Bad(good with { ServicesId = "wrong" }, "service binding");
        Bad(good with { OwnerId = "wrong" }, "owner binding");
        Bad(good with { StagingPending = -1 }, "counter");
        Bad(good with { NextToken = 0 }, "future tokens");
        void Row(Flow.EntrySnapshot row, string why) => Bad(good with { Entries = new[] { row, good.Entries[1] } }, why);
        Row(good.Entries[0] with { GuestGraphId = 999 }, "guest reference");
        Row(good.Entries[0] with { InputsId = "wrong" }, "input lease reference");
        Row(good.Entries[0] with { State = (Flow.State)999 }, "state enum");
        Row(good.Entries[0] with { Group = 2 }, "group range");
        Row(good.Entries[0] with { Mode = 123 }, "mode");
        Row(good.Entries[0] with { Token = good.Entries[1].Token }, "duplicate token");
        Row(good.Entries[0] with { Accepted = true }, "accept latch");
        Bad(good with { Admitted = new[] { new Flow.AdmittedSnapshot { GuestGraphId = good.Entries[0].GuestGraphId,
            Serial = 7, Baseline = 15 } } }, "admitted/owned overlap");
        var node = JsonNode.Parse(Json(good))!.AsObject();
        foreach (string key in node.Select(x => x.Key).ToArray())
        {
            var missing = node.DeepClone().AsObject(); missing.Remove(key); bool rejected = false;
            try { JsonSerializer.Deserialize<Flow.Snapshot>(missing.ToJsonString()); } catch (JsonException) { rejected = true; }
            C(rejected, "required JSON field " + key);
        }
        node["Unknown"] = 0; bool unknown = false;
        try { JsonSerializer.Deserialize<Flow.Snapshot>(node.ToJsonString()); } catch (JsonException) { unknown = true; }
        C(unknown, "unknown JSON field");
        bool midTickRejected = false;
        malformed.Provider.OnPump = () =>
        {
            try { malformed.Flow.CaptureState(binding); } catch (InvalidOperationException) { midTickRejected = true; }
        };
        malformed.Tick(1); C(midTickRejected, "mid-callback capture rejected");
        return count;
    }

    // Authored public Model bytes, no disc extraction or private fixtures.
    static Model Terrain()
    {
        var data = new byte[0x240];
        void U(int at, uint value) => BitConverter.GetBytes(value).CopyTo(data, at);
        void F(int at, float value) => BitConverter.GetBytes(value).CopyTo(data, at);
        U(0, Model.Magic); U(0x44, 0x140); U(0x48, 0x80); data[0x30] = 1;
        U(0x14c, 8); U(0x150, 8); F(0x158, 1);
        for (int i = 0; i < 64; i++) data[0x170 + i * 2 + 1] = 1;
        U(0xd4, 0x220); System.Text.Encoding.ASCII.GetBytes("heightfield\0").CopyTo(data, 0x220);
        for (int i = 0; i < 4; i++) F(0x90 + i * 20, 1);
        F(0x100, 8 * 1.004f); F(0x108, 8 * 1.004f);
        var result = new Model(data); result.Materials.Add("sentinel"); result.Materials.Add("jpa_str1");
        return result;
    }
}
