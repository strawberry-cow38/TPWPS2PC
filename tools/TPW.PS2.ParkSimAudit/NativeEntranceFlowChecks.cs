using TPW.PS2.Data;
using DiscImage = TPW.PS2.Data.Disc;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;
using Flow = TPW.PS2.Data.NativeEntranceFlow;

/// <summary>
/// Real controller -> ParkVisitors -> GuestWalk consumers on the owner's JUNGLE corridor.
/// Routes below are explicit legal fixture points, NOT a native pathfinder or decoded
/// native queue policy. No shadow guest/cursor, private state edits, or admission defaults.
/// </summary>
static class NativeEntranceFlowChecks
{
    // Disc is the repository's disc-image type. Standalone form returns failures, like
    // Program's 'bad' count; the callback overload fits the other consumer checks.
    public static int Run(DiscImage disc)
    {
        int bad = 0, count = 0;
        Run(disc, (ok, why) =>
        {
            count++;
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + why);
            if (!ok) bad++;
        });
        Console.WriteLine($"native entrance flow: {count} checks, {bad} failures");
        return bad;
    }

    public static void Run(DiscImage disc, Action<bool, string> check)
    {
        void Check(bool ok, string why) => check(ok, "native entrance flow: " + why);
        var archive = disc.Files().Single(f => f.Path.Equals("/DATA/JUNGLE.WAD", StringComparison.OrdinalIgnoreCase));
        var wad = new WadArchive(disc.Read(archive.Extent, archive.Size));
        var terrain = new Model(wad.Read(wad.Find("/terrain/terrain_1.mps")));
        var executable = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        var entrance = ParkEntrance.ReadExecutable(disc.Read(executable.Extent, executable.Size));
        var entry = entrance.Fit(terrain.Field, ParkEntrance.WalkwayColumnFromPoles(terrain), out _);
        var start = new ParkCell(entry.XCol, entry.ZEnd - 2);
        Fixture New(int group = 0)
        {
            var paths = new ParkPaths(terrain);
            paths.SetEntrance(entrance);
            return new Fixture(paths, start, group, Check);
        }
        var ground = New();
        Check(!entry.Empty && ground.Walk.Paths.Open(start) && ground.Walk.Paths.Open(start.Offset(0, -1)),
            "authored JUNGLE corridor is open; queue base is an explicit fixture centre, not native row+4/+5");
        Accepted();
        Queues(0);
        Queues(1);
        BlockedHead();
        MultiTick();
        Broadcast();
        Threshold(10, 0);
        Threshold(11, 1);
        Retries();
        StaleIdentity();
        Rejected();
        Teardown();
        DiscardCensus();

        void Accepted()
        {
            var f = New();
            f.Traffic = 2; // hold staged entries until the test starts the coordinator episode
            f.Ready = false;
            f.ExitAvailable = false;
            var g = f.Add(7);
            var original = g.Position;
            var needs = f.Visitors.Needs.Of(g.Id);
            f.Step();
            Check(f.Obs(g) is { State: Flow.State.Pending, Mode: 15, PendingToken: not null }
                && f.Flow.StagingPending == 0 && f.Flow.EpisodeProcessed == 0
                && g.HasNativeRoute && g.Position == original && f.Results.Count == 1,
                "0x0B means a submitted request, not completion; empty manual lease covers async gap");
            f.Step(); f.Step(); f.Step();
            Check(f.Obs(g).State == Flow.State.Moving && g.Position == original
                && f.ReadyReads == 3 && f.DeltaReads == 0 && f.Flow.StagingPending == 0
                && f.WanderCalls == 0 && f.Visitors.Plans[g.Id].Intent == VisitorIntent.Entering,
                "next-BeforeStep delivery assigns route; readiness blocks movement and ordinary AI");
            f.Ready = true;
            f.Step();
            Check(Raw(g) == new Point(f.Centre.X, (short)(f.Centre.Z - 7)) && f.DeltaReads == 1,
                "explicit baseline 7 moves seven raw units once, not again in generic Walk.Step");
            if (!f.Until(() => f.Obs(g).State == Flow.State.Staged, "mode15 deferred completion")) return;
            Check(f.Flow.StagingPending == 1 && f.Flow.EpisodeProcessed == 0 && f.Obs(g).StageCounted,
                "only mode15 completion increments P; mode15 request/arrival did not increment R");
            f.Step(); f.Step();
            Check(f.Flow.StagingPending == 1 && f.Traffic == 2 && f.Obs(g).State == Flow.State.Staged,
                "E=2 is not cleared or activated by coordinator; staged lease remains owned");
            f.Traffic = 0;
            f.Step();
            Check(f.Obs(g) is { State: Flow.State.Moving, Mode: 16, StageCounted: true }
                && f.Flow.StagingPending == 1 && f.Flow.EpisodeProcessed == 0 && f.Traffic == 1,
                "event9 is between groups and active: assigns mode16 same tick but is not completion");
            if (!f.Until(() => f.Obs(g).State == Flow.State.RequestQueue, "mode16 completion")) return;
            Check(f.Flow.StagingPending == 0 && f.Flow.EpisodeProcessed == 1 && !f.Obs(g).StageCounted
                && f.Flow.Counts == (0, 0), "mode16 completion alone decrements P/increments R, before registration");
            f.Step();
            Check(f.Obs(g) is { State: Flow.State.Pending, Mode: 11, Speed: 15, InQueue: true }
                && f.Flow.Counts == (1, 0), "mode11 registers before async request and selects speed15");
            var before = Raw(g);
            f.Step();
            Check(Raw(g) == new Point(before.X, (short)(before.Z + 15)) && g.Progress == 0 && g.Route == null,
                "mode11 0x4000 delta at speed15 is exactly 15/256 cell, not double-stepped");
            if (!f.Until(() => f.Obs(g).State == Flow.State.Waiting, "mode11 reaches waiting")) return;
            Check(f.AcceptCalls == 0 && f.Flow.Counts == (1, 0), "queue route completion is not acceptance");
            if (!f.Until(() => f.Obs(g).State == Flow.State.Accepted, "phase0 head acceptance")) return;
            Check(f.Obs(g) is { Group: -1, Speed: 7, BaselineSpeed: 7, Accepted: true, AcceptanceAttempted: true }
                && f.Flow.Counts == (0, 0) && f.AcceptCalls == 1 && f.Visitors.Needs.Of(g.Id).Cash == needs.Cash - 100,
                "head evaluates once, restores baseline, and charges the existing identity once");
            for (int i = 0; i < 5; i++) f.Step();
            Check(f.ExitCalls == 5 && f.AcceptCalls == 1 && f.Obs(g).State == Flow.State.Accepted
                && g.HasNativeRoute && f.WanderCalls == 0,
                "accepted with no exit goal retries each tick without recharging or releasing");
            f.ExitAvailable = true;
            // Walk.Step still invokes the real BeforeStep consumer; omit Idle for the exact
            // handback observation (a subsequent Visitors.Step is the ordinary AI control).
            if (!f.Until(() => !f.Flow.Owns(g), "mode13 normal handback", visitors: false)) return;
            needs.Cash -= 100;
            Check(ReferenceEquals(f.Walk.Guests.Single(), g) && !g.HasNativeRoute && Raw(g) == f.Centre
                && f.Visitors.Plans[g.Id].Intent == VisitorIntent.Wandering
                && SameNeeds(f.Visitors.Needs.Of(g.Id), needs) && f.Flow.Observations.Count == 0,
                "mode13 releases at centre with same guest, plan, charged cash and numeric needs; no old flow reference");
            Check(f.Sequence(g, "added; empty lease acquired", "request accepted (not route completion)",
                "route assigned", "route completion dispatched", "event9", "route assigned",
                "route completion dispatched", "queue registered", "request accepted (not route completion)",
                "route assigned", "route completion dispatched", "queue head released",
                "accepted once; awaiting exit goal", "no exit goal; acceptance not repeated",
                "route assigned", "mode13 released to ordinary visitor"), "accepted trace follows controller lifecycle order");
            f.Visitors.Step(0, f.Wander);
            Check(f.WanderCalls > 0, "positive control: ordinary wandering resumes after handback");
            f.Audit();
        }

        void Queues(int group)
        {
            var f = New(group);
            f.ExitAvailable = false;
            var guests = Enumerable.Range(0, 3).Select(_ => f.Add(15)).ToArray();
            if (!f.Until(() => f.Flow.Observations.All(o => o.State == Flow.State.Waiting), $"group{group} three waiting members")) return;
            var requests = f.Requests.Where(r => r.Mode == 11).ToArray();
            Check(requests.Length == 3 && requests.Select(r => r.Guest).Distinct().Count() == 3
                && requests.Select(r => r.Target).SequenceEqual(Enumerable.Range(0, 3)
                    .Select(i => new Point(f.Centre.X, (short)(f.Centre.Z - 64 * i))))
                && f.Flow.Counts == (group == 0 ? 3 : 0, group == 1 ? 3 : 0),
                $"group{group}: append order has base.X (no group1 x+256), quarter-cell spacing, three distinct real guests");
            Check(requests.Select(r => r.Guest).SequenceEqual(guests.Reverse()),
                $"group{group}: prepend active/allocation ordering is observed at queue registration");
            var head = requests[0].Guest;
            var tail = requests.Skip(1).Select(r => r.Guest).ToArray();
            while (((f.Tick + 1) & 31u) != (uint)group)
            {
                f.Step();
                if (f.Tick > 96) break;
            }
            Check(f.AcceptCalls == 0 && f.Flow.Observations.All(o => o.State == Flow.State.Waiting),
                $"group{group}: nonmatching phases do not release even waiting members");
            f.Step();
            var release = f.Events.Where(e => e.Trace.Event == "queue head released").ToArray();
            var moves = f.Events.Where(e => e.Tick == f.Tick && e.Trace.Event == "event6").ToArray();
            Check(release.Length == 1 && ReferenceEquals(release[0].Trace.Entry.Guest, head)
                && (release[0].Tick & 31u) == (uint)group && f.AcceptCalls == 1
                && f.Flow.Counts == (group == 0 ? 2 : 0, group == 1 ? 2 : 0),
                $"group{group}: only the waiting head releases at phase{group}, not the entire list");
            Check(moves.Select(e => e.Trace.Entry.Guest).SequenceEqual(tail)
                && tail.All(g => f.Obs(g) is { State: Flow.State.Moving, Mode: 12, Speed: 15 })
                && moves.All(e => e.Trace.Entry.State == Flow.State.Reposition),
                $"group{group}: event6 reaches each remaining member and assigns its own speed15 mode12 route");
            Check(f.DirectTargets.TakeLast(2).SequenceEqual(new[] { f.Centre, new Point(f.Centre.X, (short)(f.Centre.Z - 64)) }),
                $"group{group}: reposition targets recomputed per predecessor, not one shared head target");
            var positions = tail.Select(Raw).ToArray();
            f.Step();
            Check(tail.Select(Raw).SequenceEqual(positions.Select(p => new Point(p.X, (short)(p.Z + 15)))),
                $"group{group}: each reposition takes one 15-raw step through the real consumer");
            if (!f.Until(() => tail.All(g => f.Obs(g).State == Flow.State.Waiting), $"group{group} reposition completes")) return;
            Check(tail.Select(Raw).SequenceEqual(new[] { f.Centre, new Point(f.Centre.X, (short)(f.Centre.Z - 64)) })
                && f.AcceptCalls == 1, $"group{group}: mode12 completion waits for a later release phase");
            // Keep observing the ready replacement head THROUGH the half-period.
            // Stopping just after repositioning cannot distinguish &15 from &31.
            while (((f.Tick + 1) & 31u) != (uint)group && f.Tick < 96) f.Step();
            Check(f.AcceptCalls == 1, $"group{group}: replacement head cannot release at the half-period phase");
            f.Step();
            Check(f.AcceptCalls == 2 && (f.Tick & 31u) == (uint)group,
                $"group{group}: second head releases on the next full32-tick phase");
            f.Audit();
        }

        void BlockedHead()
        {
            var f = New();
            f.ExitAvailable = false;
            for (int i = 0; i < 3; i++) f.Add(15);
            if (!f.Until(() => f.Requests.Count(r => r.Mode == 11) == 3, "blocked-head queue submissions")) return;
            var members = f.Requests.Where(r => r.Mode == 11).Select(r => r.Guest).ToArray();
            f.Blocked = members[0];
            if (!f.Until(() => members.Skip(1).All(g => f.Obs(g).State == Flow.State.Waiting),
                "followers wait while queue head is animation-blocked")) return;
            while ((f.Tick & 31u) != 0) f.Step();
            Check(f.Obs(members[0]).State == Flow.State.Moving && f.AcceptCalls == 0
                && f.Flow.Counts == (3, 0) && members.Skip(1).All(g => f.Obs(g).State == Flow.State.Waiting)
                && !f.Events.Any(e => e.Trace.Event is "queue head released" or "event6"),
                "phase0 cannot skip an unfinished head to release waiting followers; no spurious event6");
            f.Blocked = null;
            if (!f.Until(() => f.AcceptCalls == 1, "unblocked head eventually releases")) return;
            Check(ReferenceEquals(f.Events.Single(e => e.Trace.Event == "queue head released").Trace.Entry.Guest, members[0]),
                "readiness recovery still releases the original head, not an overtaking follower");
            f.Audit();
        }

        void MultiTick()
        {
            var f = New();
            var g = f.Add(15);
            f.StepBatch();
            Check(f.Tick == 3 && f.Obs(g) is { State: Flow.State.Moving, Mode: 15 }
                && Raw(g) == new Point(f.Centre.X, (short)(f.Centre.Z - 30))
                && f.ReadyReads == 2 && f.DeltaReads == 2 && f.Flow.StagingPending == 0,
                "one .12-second Visitors.Step executes three BeforeSteps: submit, deliver+15, then +15 (no double step)");
            f.Audit();
        }

        void Broadcast()
        {
            var f = New();
            f.Traffic = 2;
            for (int i = 0; i < 12; i++) f.Add(15);
            if (!f.Until(() => f.Flow.Observations.All(o => o.State == Flow.State.Staged), "twelve staged before coordinator episode")) return;
            Check(f.Flow.StagingPending == 12 && f.Flow.EpisodeProcessed == 0 && f.Traffic == 2,
                "batch12 initializes P before starting E=0 episode; no accidental midbatch coordinator reset");
            f.Traffic = 0;
            f.Step();
            var broadcastTick = f.Tick;
            Check(f.Events.Count(e => e.Tick == broadcastTick && e.Trace.Event == "event9") == 12
                && f.Flow.Observations.All(o => o.Mode == 16 && o.State == Flow.State.Moving)
                && f.Flow.StagingPending == 12 && f.Flow.EpisodeProcessed == 0,
                "coordinator broadcasts to all twelve staged identities, not an eleven-person quota");
            if (!f.Until(() => f.Flow.Observations.All(o => o.State == Flow.State.RequestQueue), "all twelve complete mode16")) return;
            Check(f.Flow.EpisodeProcessed == 12 && f.Flow.StagingPending == 0
                && f.Events.Count(e => e.Trace.Event == "route completion dispatched" && e.Trace.Entry.Mode == 16) == 12,
                "already-broadcast batch crosses R=11 threshold to R=12; P/R change on completion only");
            f.Step();
            Check(f.Traffic == 0 && f.Flow.Counts == (12, 0),
                "P=0 clears E next coordinator pass; simultaneous selections precede registration (not a capacity veto)");
            f.Audit();
            f.Flow.Clear((_, _) => { }, (g, _) => f.Walk.Remove(g.Id));
            Check(f.Flow.StagingPending == 0 && f.Flow.EpisodeProcessed == 0 && f.Flow.Counts == (0, 0),
                "teardown resets completed episode and both membership lists");
        }

        void Threshold(int existing, int expectedGroup)
        {
            var f = New();
            f.ExitAvailable = false;
            for (int i = 0; i < existing; i++) f.Add(15);
            if (!f.Until(() => f.Requests.Count(r => r.Mode == 11) == existing,
                $"threshold fixture has {existing} registered members")) return;
            f.Blocked = f.Requests.First(r => r.Mode == 11).Guest; // never-ready head prevents any release
            var extra = f.Add(15);
            if (!f.Until(() => f.Requests.Any(r => r.Mode == 11 && ReferenceEquals(r.Guest, extra)),
                $"new guest selects group against actual count {existing}")) return;
            Check(f.Obs(extra).Group == expectedGroup && f.AcceptCalls == 0,
                $"literal11 threshold: existing{existing} selects group{expectedGroup}, not a capacity guess");
            Check(f.Flow.Counts == (existing + (expectedGroup == 0 ? 1 : 0), expectedGroup == 1 ? 1 : 0),
                $"literal11 threshold changes membership only by this one guest at existing{existing}");
            f.Audit();
        }

        void Retries()
        {
            var f = New();
            f.RefuseQueue = 2;
            f.FailQueue = 1;
            var g = f.Add(15);
            if (!f.Until(() => f.Requests.Any(r => r.Mode == 11), "first refused queue request")) return;
            Check(f.Obs(g) is { State: Flow.State.RequestQueue, InQueue: true, PendingToken: null }
                && f.Flow.Counts == (1, 0) && f.Results.Count == 0,
                "immediate queue refusal retains membership and lease, no pending phantom completion");
            f.Step();
            Check(f.Flow.Counts == (1, 0) && f.Obs(g).State == Flow.State.RequestQueue
                && f.Events.Count(e => e.Trace.Event == "queue registered") == 1,
                "second immediate refusal does not append duplicate membership");
            f.Step();
            var failedToken = f.Obs(g).PendingToken;
            Check(f.Obs(g).State == Flow.State.Pending && f.Flow.Counts == (1, 0),
                "accepted retry remains pending until next pump, even when service queues a failure");
            f.Step();
            Check(f.Obs(g) is { State: Flow.State.Pending, InQueue: true }
                && f.Obs(g).PendingToken != failedToken && f.Flow.Counts == (1, 0)
                && f.Events.Count(e => e.Trace.Event == "mode11 event2; retry queue") == 1,
                "async failure consumes token once, retries from queue state same pass, no duplicate membership");
            // Duplicate old failure and success must not replace the new pending route.
            f.Results.Insert(0, new Flow.RouteResult(failedToken.Value, g, null, "duplicate failure"));
            f.Results.Insert(1, new Flow.RouteResult(failedToken.Value, g, new[] { f.Staging }, null));
            if (!f.Until(() => f.Obs(g).State == Flow.State.Waiting, "retry reaches real waiting state")) return;
            Check(Raw(g) == f.Centre && f.Flow.Counts == (1, 0)
                && f.Events.Count(e => e.Trace.Event == "queue registered") == 1
                && f.Events.Count(e => e.Trace.Event == "route assigned" && e.Trace.Entry.Mode == 11) == 1
                && f.Requests.Select(r => r.Token).Distinct().Count() == f.Requests.Count,
                "late duplicate results ignored; successful queue route assigned once; every retry has unique token");
            f.Audit();
        }

        void StaleIdentity()
        {
            var f = New();
            f.HoldResults = true;
            var old = f.Add(15);
            f.Step();
            var token = f.Obs(old).PendingToken.Value;
            f.Walk.Remove(old.Id);
            // Readmit is the public walk identity-reuse API. The existing Entering plan
            // is intentional: a numeric-ID lookup would wrongly act on this newcomer.
            var fresh = f.Walk.Readmit(old.Id, f.Start, f.Start);
            f.Flow.Add(fresh, 15);
            f.Step(false);
            var freshToken = f.Obs(fresh).PendingToken;
            Check(!f.Flow.Owns(old) && !old.HasNativeRoute && f.Flow.Owns(fresh)
                && fresh.Id == old.Id && !ReferenceEquals(old, fresh) && freshToken != token,
                "external removal unlinks old reference despite immediate display-ID reuse");
            // Both exact-old-guest and wrong-token/current-guest stale snapshots.
            f.Results.Insert(0, new Flow.RouteResult(token, fresh, new[] { f.Centre }, null));
            f.HoldResults = false;
            f.Step(false);
            Check(f.Flow.Observations.Count == 1 && ReferenceEquals(f.Flow.Observations[0].Guest, fresh)
                && Raw(fresh) == new Point(f.Centre.X, (short)(f.Centre.Z - 15))
                && f.Events.Count(e => e.Trace.Event == "route assigned") == 1
                && f.Flow.StagingPending == 0 && f.AcceptCalls == 0,
                "old-object and wrong-token callbacks ignored; only new identity's snapshot moves it once");
            f.Audit();
        }

        void Rejected()
        {
            var f = New();
            f.AcceptResult = false;
            var g = f.Add(15, unhappy: true);
            var wants = f.Visitors.Needs.Of(g.Id);
            if (!f.Until(() => f.Obs(g).State == Flow.State.Rejected, "rejected boundary reached")) return;
            var position = g.Position;
            for (int i = 0; i < 70; i++) f.Step();
            Check(f.AcceptCalls == 1 && f.RejectCalls == 1 && f.ExitCalls == 0
                && f.Obs(g) is { State: Flow.State.Rejected, Accepted: false, InQueue: false }
                && f.Flow.Owns(g) && g.HasNativeRoute && g.Position == position,
                "rejected callback once across multiple release phases; retained lease is not departure completion");
            Check(f.Visitors.Needs.WantsToGoHome(g.Id) && f.Visitors.WentHome == 0 && f.WanderCalls == 0
                && f.Visitors.Plans[g.Id].Intent == VisitorIntent.Entering && SameNeeds(f.Visitors.Needs.Of(g.Id), wants)
                && ReferenceEquals(f.Walk.Guests.Single(), g),
                "unhappy rejected identity cannot be stolen by ordinary home/wander AI; no charge or reseed");
            f.Audit();
        }

        void DiscardCensus()
        {
            var f = New(); f.HoldResults = true;
            var a = f.Add(15); var b = f.Add(15);
            f.Step();
            int cancelled = 0;
            f.Flow.Clear((_, _) => cancelled++, f.Visitors.DiscardEntranceGuest);
            Check(cancelled == 2 && f.Visitors.DiscardedEntranceGuests == 2
                && f.Visitors.WentHome == 0 && f.AcceptCalls == 0,
                "explicit teardown counts discards separately from departures and admission attempts");
            Check(!f.Walk.IsLive(a) && !f.Walk.IsLive(b) && !f.Visitors.Plans.Any()
                && !f.Visitors.Needs.Has(a.Id) && !f.Visitors.Needs.Has(b.Id)
                && f.Flow.Position(a) == null && f.Flow.Position(b) == null,
                "discard accounting corresponds to real plan/needs/identity/coordinate removal");
            f.Flow.Clear((_, _) => cancelled++, f.Visitors.DiscardEntranceGuest);
            Check(f.Visitors.DiscardedEntranceGuests == 2 && cancelled == 2,
                "repeat clear cannot double count discarded identities");
            var fresh = f.Add(15);
            Check(f.Flow.Position(fresh) == f.Centre && f.Flow.Position(a) == null,
                "integer source coordinate is exposed only for the current live flow identity");
        }

        void Teardown()
        {
            var f = New();
            f.HoldResults = true;
            var a = f.Add(15);
            var b = f.Add(15);
            f.Step();
            var oldResults = f.Results.ToArray();
            var cancelled = new List<(ulong, Guest)>();
            var resolved = new List<Guest>();
            f.Flow.Clear((token, g) => cancelled.Add((token, g)), (g, _) =>
            {
                resolved.Add(g);
                f.Walk.Remove(g.Id); // explicit parent teardown policy, not fractional handback
            });
            Check(cancelled.Count == 2 && oldResults.All(r => cancelled.Contains((r.Token, r.Guest)))
                && resolved.SequenceEqual(new[] { b, a }) && !a.HasNativeRoute && !b.HasNativeRoute,
                "Clear cancels each pending request and resolves each real lease in allocation order");
            Check(f.Flow.Observations.Count == 0 && !f.Flow.Owns(a) && !f.Flow.Owns(b)
                && f.Flow.Counts == (0, 0) && f.Flow.StagingPending == 0 && f.Flow.EpisodeProcessed == 0,
                "Clear drops observable old references, membership, pending and episode counters");
            f.Flow.Clear((_, _) => cancelled.Add((0, a)), (_, _) => resolved.Add(a));
            Check(cancelled.Count == 2 && resolved.Count == 2, "second Clear is empty, no repeated policy callbacks");
            f.Walk.Clear(); // force display-ID reuse through the public reset API
            var fresh = f.Add(15);
            f.Step();
            var token = f.Obs(fresh).PendingToken.Value;
            Check(fresh.Id == a.Id && !ReferenceEquals(fresh, a) && oldResults.All(r => token > r.Token),
                "Clear never resets request tokens even when Walk resets numeric IDs");
            f.HoldResults = false;
            f.Step();
            Check(f.Flow.Observations.Count == 1 && ReferenceEquals(f.Flow.Observations[0].Guest, fresh)
                && Raw(fresh) == new Point(f.Centre.X, (short)(f.Centre.Z - 15))
                && f.Events.Count(e => e.Trace.Event == "route assigned") == 1,
                "late Clear-era callbacks cannot resurrect old guests or steal fresh ID's lease");
            f.Audit();

            var staged = New();
            staged.Traffic = 2;
            var counted = staged.Add(15);
            if (!staged.Until(() => staged.Flow.StagingPending == 1, "counted staging teardown fixture")) return;
            int cancellations = 0, resolutions = 0;
            staged.Flow.Clear((_, _) => cancellations++, (guest, _) =>
            {
                resolutions++;
                staged.Walk.Remove(guest.Id);
            });
            Check(cancellations == 0 && resolutions == 1 && !counted.HasNativeRoute
                && !staged.Flow.Owns(counted) && staged.Flow.Observations.Count == 0
                && staged.Flow.StagingPending == 0 && staged.Flow.EpisodeProcessed == 0,
                "Clear of counted staging drops P and old reference without cancelling an already consumed token");
        }
    }

    static Point Raw(Guest g) => new(checked((short)(g.Position.X * 256)), checked((short)(g.Position.Z * 256)));
    static bool SameNeeds(VisitorWants actual, VisitorWants expected)
    {
        actual.Thought = expected.Thought; // independent HUD thought selection is not numeric needs decay
        return actual.Equals(expected);
    }

    sealed record Submission(uint Tick, ulong Token, Guest Guest, int Mode, Point From, Point Target);
    sealed record EventAt(uint Tick, Flow.TraceEvent Trace);

    sealed class Fixture
    {
        readonly Action<bool, string> _check;
        public readonly GuestWalk Walk;
        public readonly ParkVisitors Visitors;
        public readonly Flow Flow;
        public readonly ParkCell Start;
        public readonly Point Centre, Staging;
        public readonly List<Submission> Requests = new();
        public readonly List<Flow.RouteResult> Results = new();
        public readonly List<EventAt> Events = new();
        public readonly List<Point> DirectTargets = new();
        public bool Ready = true, HoldResults, ExitAvailable = true, AcceptResult = true;
        public int Traffic, RefuseQueue, FailQueue, ReadyReads, DeltaReads, AcceptCalls, RejectCalls, ExitCalls, WanderCalls;
        public uint Tick;
        public Guest Blocked;
        int _hooks, _steps;
        bool _ordered = true, _counters = true;

        public Fixture(ParkPaths paths, ParkCell start, int group, Action<bool, string> check)
        {
            _check = check;
            Start = start;
            Centre = new(checked((short)(start.X * 256 + 128)), checked((short)(start.Z * 256 + 128)));
            Staging = new(Centre.X, (short)(Centre.Z - 64)); // near current position, exactly representable
            Walk = new GuestWalk(paths);
            Visitors = new ParkVisitors(new ParkSim(paths), Walk, () => 0)
                { Needs = new VisitorNeeds(71) { SecondsPerRise = 1_000_000 } };
            foreach (var key in Visitors.Needs.Rates.Keys.ToArray()) Visitors.Needs.Rates[key] = new(0, 0, false);
            Flow = new Flow(Visitors, new Flow.Services(
                n => group == 0 ? 0 : Math.Min(1, n - 1),
                _ => Staging, Centre, Request, Pump,
                target => { DirectTargets.Add(target); return true; },
                g => { ReadyReads++; return Ready && !ReferenceEquals(g, Blocked); },
                () => { DeltaReads++; return 0x4000; },
                g =>
                {
                    AcceptCalls++;
                    if (AcceptResult)
                    {
                        var wants = Visitors.Needs.Of(g.Id);
                        wants.Cash -= 100; // explicit fake fee service acting on the real needs record
                        Visitors.Needs.Set(g.Id, wants);
                    }
                    return AcceptResult;
                },
                _ => { ExitCalls++; return ExitAvailable ? Centre : (Point?)null; },
                _ => RejectCalls++, Walk.StepOwnedNative,
                e => Events.Add(new EventAt(Tick, e))));
            Walk.BeforeStep = tick =>
            {
                Tick = tick;
                _hooks++;
                int at = Events.Count, p = Flow.StagingPending, r = Flow.EpisodeProcessed;
                bool reset = p != 0 && Traffic == 0;
                Traffic = Flow.Tick(tick, busState: 2, traffic: Traffic);
                var batch = Events.Skip(at).ToArray();
                int up = batch.Count(e => e.Trace.Event == "route completion dispatched" && e.Trace.Entry.Mode == 15);
                int down = batch.Count(e => e.Trace.Event == "route completion dispatched" && e.Trace.Entry.Mode == 16);
                int vanished = batch.Count(e => e.Trace.Event.StartsWith("SAFETY:") && e.Trace.Entry.StageCounted);
                _counters &= Flow.StagingPending == p + up - down - vanished
                    && Flow.EpisodeProcessed == (reset ? 0 : r) + down;
            };
        }

        bool Request(ulong token, Guest g, int mode, Point from, Point target)
        {
            Requests.Add(new Submission(Tick, token, g, mode, from, target));
            if (mode == 11 && RefuseQueue > 0) { RefuseQueue--; return false; }
            if (mode == 11 && FailQueue > 0)
            {
                FailQueue--;
                Results.Add(new Flow.RouteResult(token, g, null, "fixture asynchronous failure"));
            }
            else Results.Add(new Flow.RouteResult(token, g, new[] { target }, null));
            return true;
        }

        IEnumerable<Flow.RouteResult> Pump()
        {
            if (HoldResults) return Array.Empty<Flow.RouteResult>();
            // Materialized batch: submissions later in this tick cannot complete inline.
            var snapshot = Results.ToArray();
            Results.Clear();
            foreach (var result in snapshot)
            {
                var request = Requests.LastOrDefault(r => r.Token == result.Token);
                // Time may restart following Walk.Clear; compare service invocations there
                // through the explicit pending assertions instead of a reset walk timestamp.
                if (request != null && request.Tick == Tick) _ordered = false;
            }
            return snapshot;
        }

        public Guest Add(sbyte baseline, bool unhappy = false)
        {
            var g = Visitors.Arrive(Start, Start);
            Visitors.Needs.Set(g.Id, new VisitorWants
                { Cash = 1234, Happiness = (byte)(unhappy ? 0 : 80), Hunger = 12, Thirst = 13, Toilet = 14 });
            Flow.Add(g, baseline);
            return g;
        }
        public Flow.Observation Obs(Guest g) => Flow.Observations.Single(o => ReferenceEquals(o.Guest, g));
        public ParkCell Wander() { WanderCalls++; return Start.Offset(0, -1); }
        public void Step(bool visitors = true)
        {
            _steps++;
            if (visitors) Visitors.Step(.04, Wander); else Walk.Step();
        }
        public void StepBatch()
        {
            _steps += 3;
            Visitors.Step(.12, Wander);
        }
        public bool Until(Func<bool> done, string why, bool visitors = true)
        {
            int budget = 256;
            while (!done() && budget-- > 0) Step(visitors);
            bool ok = done();
            _check(ok, why + " within 256 executed ticks");
            return ok;
        }
        public bool Sequence(Guest g, params string[] sequence)
        {
            int next = 0;
            foreach (var e in Events.Where(e => ReferenceEquals(e.Trace.Entry.Guest, g)))
                if (next < sequence.Length && e.Trace.Event == sequence[next]) next++;
            return next == sequence.Length;
        }
        public void Audit()
        {
            _check(_hooks == _steps && _ordered, "one BeforeStep per executed tick; pump only delivers earlier submissions");
            _check(_counters, "every tick: P changes only at mode15/16 completion (or vanished cleanup), R at mode16/episode reset");
        }
    }
}
