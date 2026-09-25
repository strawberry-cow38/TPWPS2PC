using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer.Tests;

/// <summary>
/// End-of-StandingServiceAudit fixture. Like its caller, this deliberately does NOT run
/// Viewer._Ready, mouse input, or normal startup. It exercises the real coordinator,
/// standing-pose consumer, character loader/animation and thought-art presentation in a
/// live tree. The caller owns the viewer, libraries, stage and final resource teardown.
/// </summary>
public static class NativeReliefPresentationChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo Member(string name) => typeof(Viewer).GetField(name, Hidden)
        ?? throw new MissingMemberException("Viewer." + name);
    static T Field<T>(Viewer viewer, string name) => (T)Member(name).GetValue(viewer);
    static void Set(Viewer viewer, string name, object value) => Member(name).SetValue(viewer, value);
    static object Call(Viewer viewer, string name, params object[] args) =>
        (typeof(Viewer).GetMethod(name, Hidden) ?? throw new MissingMemberException("Viewer." + name))
        .Invoke(viewer, args);

    static VisitorNeeds FreezeRates()
    {
        var needs = new VisitorNeeds(871) { SecondsPerRise = 1_000_000 };
        foreach (string key in needs.Rates.Keys.ToArray()) needs.Rates[key] = new(0, 0, false);
        needs.Unknown78Bar = needs.SickBar = needs.ToiletBar = needs.HungerBar = needs.ThirstBar = 101;
        return needs;
    }

    static bool FullBody(Node3D actor) => actor != null && GodotObject.IsInstanceValid(actor)
        && actor.IsInsideTree() && !actor.IsQueuedForDeletion()
        && actor.FindChildren("*body*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
            .Any(m => m.Mesh != null && m.IsVisibleInTree())
        && actor.FindChildren("*legs*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
            .Any(m => m.Mesh != null && m.IsVisibleInTree());

    public static async System.Threading.Tasks.Task Run(Viewer viewer, Node3D stage,
        AssetLibrary library, Model terrain, string disc, Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "native relief presentation: " + label);
        async System.Threading.Tasks.Task Frames()
        {
            await stage.ToSignal(stage.GetTree(), SceneTree.SignalName.ProcessFrame);
            await stage.ToSignal(stage.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        const string stem = "/Features/Toilet/Toilet";
        var wad = library.Wad;
        var definition = RideDefinition.Parse(Encoding.ASCII.GetString(wad.Read(wad.Find(stem + ".sam"))),
            "/DATA/JUNGLE.WAD" + stem + ".sam");
        // In-memory archive reads only. Do not replace the caller's loaded JUNGLE library.
        using (var data = new AssetLibrary(disc))
        {
            data.OpenWad("/DATA/DATA.WAD");
            var compiled = new CompiledAssets(new AssetResourceDatabase(data.Wad.Read(data.Wad.Find("/arsdb.dba"))),
                TextDatabase.Load(data.Wad, "eur"));
            compiled.Attach(new[] { definition }, out _);
            Check(definition.CompiledEntry != null
                && definition.CompiledEntry == compiled.For("JUNGLE", stem + ".sam"),
                "named Small Toilet joins DATA.WAD through production CompiledAssets.Attach");
        }
        var rec = definition.CompiledEntry ?? throw new InvalidOperationException("Missing JUNGLE Small Toilet compiled entry");
        Check(rec.Kind == AssetResourceDatabase.AssetKind.Feature && (rec.RawFeatureFlags.GetValueOrDefault() & 1) != 0
            && rec.Width > 0 && rec.Depth > 0 && rec.ConnectionA.IsPresent && rec.ConnectionA.Direction <= 3,
            "real compiled relief flag and full connection/footprint geometry");
        var paths = new ParkPaths(terrain);
        for (int i = 1; i < paths.Field.Cells.Length; i += 2) paths.Field.Cells[i] = 0;
        bool Fits(ParkCell c) => Enumerable.Range(-4, rec.Width + 8).All(x =>
            Enumerable.Range(-4, rec.Depth + 8).All(z => paths.CanLay(c.Offset(x, z))));
        // A distinct, reproducible origin; optionally rerun at another literal X,Z.
        string configured = OS.GetEnvironment("TPW_NATIVE_RELIEF_ORIGIN");
        ParkCell origin;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var parts = configured.Split(',');
            if (parts.Length != 2) throw new ArgumentException("TPW_NATIVE_RELIEF_ORIGIN must be X,Z");
            origin = new ParkCell(int.Parse(parts[0]), int.Parse(parts[1]));
            if (!Fits(origin)) throw new ArgumentException("Configured native relief origin does not fit");
        }
        else origin = paths.Cells.Where(Fits).Skip(7).First();
        var a = rec.ConnectionA;
        var entry = origin.Offset(a.X, a.Z);
        var direction = a.Direction switch
        {
            0 => new ParkCell(0, -1), 1 => new ParkCell(-1, 0),
            2 => new ParkCell(0, 1), _ => new ParkCell(1, 0)
        };
        var stub = entry.Offset(direction.X, direction.Z);
        var middle = stub.Offset(direction.X, direction.Z);
        var start = middle.Offset(direction.X, direction.Z);
        int material = Enumerable.Range(1, paths.Materials.Count - 1)
            .First(i => ParkPaths.Classify(paths.Materials[i]) == ParkPathKind.Path);
        foreach (var cell in new[] { start, middle, stub }) paths.Lay(cell, material);
        paths.Occupy(Enumerable.Range(0, rec.Width).SelectMany(x =>
            Enumerable.Range(0, rec.Depth).Select(z => origin.Offset(x, z))));
        Check(paths.Route(start, stub)?.Count == 3 && paths.Open(start) && paths.Open(middle) && paths.Open(stub)
            && !paths.Open(entry) && !paths.CanLay(entry) && paths.Route(start, entry) == null,
            $"three-cell public route at {origin}; occupied inside entry is not global path");
        var script = wad.Read(wad.Find(stem + ".rse"));
        var aps = new Aps(wad.Read(wad.Find(stem + ".aps")));
        byte[] Sibling(string name) => wad.Find("/Features/Toilet/" + name) is { } e ? wad.Read(e) : null;
        var sim = new ParkSim(paths);
        var ride = sim.Add(971, "Small Toilet", origin, rec.Width, rec.Depth, script, aps, 1,
            stub, stub, out var fault, sibling: Sibling, definition: definition, placementTurns: 0)
            ?? throw new InvalidOperationException(fault);
        sim.SetOpen(ride.Id, true); ride.Set("VAR_BROKEN", 0);
        Check(ride.NativeRelief && ride.ServiceEntry == entry, "Add computes validated ServiceEntry and enables NativeRelief");
        var visitors = new ParkVisitors(sim, new GuestWalk(paths), () => 0) { Needs = FreezeRates() };
        Set(viewer, "_sim", sim); Set(viewer, "_visitors", visitors); Set(viewer, "_guests", visitors.Walk);
        // Retire the previous fixture's bodies through the real consumer before IDs restart.
        Call(viewer, "PlaceActors", 1f);
        await Frames();
        var actors = Field<Dictionary<int, Node3D>>(viewer, "_actors");
        var standing = Field<Dictionary<int, Transform3D>>(viewer, "_standing");
        var guestRoot = Field<Node3D>(viewer, "_guestRoot");
        var thoughts = Field<ThoughtBubbles>(viewer, "_thoughts");
        Check(thoughts.Ready && thoughts.Root.IsInsideTree(), "parent's real disc thought artwork is live");
        var root = new Node3D { Name = "NativeReliefService" }; stage.AddChild(root);
        Call(viewer, "RegisterStandingService", ride, root, 0);
        var registered = Field<Dictionary<ParkRide, (Node3D Root, StandingServicePose Pose)>>(viewer, "_standingPlaces");
        Check(registered.TryGetValue(ride, out var place) && place.Root == root && root.IsInsideTree(),
            "production registration has a live root (standing fallback would draw without native guard)");
        var guest = visitors.Arrive(start, start);
        int id = guest.Id;
        var initial = visitors.Needs.Of(id);
        initial.Toilet = 100; initial.Cash = 1234; initial.Hunger = initial.Thirst = initial.Sick = 0;
        initial.Happiness = 75; visitors.Needs.Set(id, initial);
        Check(visitors.SendTo(guest, ride), "real coordinator accepts physical approach");
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                visitors.Step(.04, null);
                Set(viewer, "_parkTicks", Field<int>(viewer, "_parkTicks") + 1);
            }
        }
        void Present() { Call(viewer, "StandingRiders"); Call(viewer, "PlaceActors", 1f); }
        void Record(string phase)
        {
            var n = visitors.Needs.Of(id);
            GD.Print($"NATIVE RELIEF PRESENTATION {phase} tick={sim.Time / 40} ms={sim.Time} guest={id} "
                + $"owner={visitors.QueuedOwner(id)?.Id.ToString() ?? "none"} intent={visitors.Plans[id].Intent} "
                + $"hidden={visitors.ServiceHidden(id)} deadline={visitors.ReliefDeadline(id)?.ToString() ?? "none"} "
                + $"toilet={n.Toilet} cash={n.Cash} hunger={n.Hunger} thirst={n.Thirst} sick={n.Sick} happiness={n.Happiness} "
                + $"boardings={visitors.Boardings} rides={visitors.Rides} relieved={visitors.Relieved}");
        }
        bool NoBubble() => !thoughts.Root.GetChildren().OfType<Sprite3D>().Any(b => b.Visible);
        bool NoOtherOwner() => (!ride.Host.Visibility.TryGetValue(id, out var visibility) || visibility.Visible)
            && !ride.Host.Seats.Values.Contains(id) && !ride.Host.Walkers.ContainsKey(id)
            && !ParkSim.Chain(ride.Machine).Any(m => m.GuestIds.Contains(id));
        ReliefServiceClock Clock()
        {
            var visits = (IDictionary)(typeof(ParkVisitors).GetField("_reliefVisits", Hidden)
                ?? throw new MissingMemberException("ParkVisitors._reliefVisits")).GetValue(visitors);
            var visit = visits[id] ?? throw new InvalidOperationException("Missing native visit");
            return (ReliefServiceClock)visit.GetType().GetProperty("Clock").GetValue(visit);
        }
        Tick(1); Present();
        actors.TryGetValue(id, out var original);
        Check(guest.State == GuestState.Walking && guest.Progress > 0 && FullBody(original),
            "BEFORE service actual walking character has visible mesh body and legs");
        // ⭐ The toilet bubble comes from the 128-tick mood ladder on THIS guest's slot; SendTo takes
        // no decision action, so nothing raises it on arrival. Step to the slot computed exactly as
        // `Moods` does and assert after it, inside the same 50-step approach, so the walk timeline
        // below is untouched. A fixed Tick(n) would only fit one guest id.
        var needsTick = typeof(VisitorNeeds).GetField("_tick", Hidden)
            ?? throw new MissingMemberException("VisitorNeeds._tick");
        long NeedsTick() => (long)needsTick.GetValue(visitors.Needs);
        long now = NeedsTick(), phase = ((id % VisitorNeeds.MoodTicks) - now) % VisitorNeeds.MoodTicks;
        if (phase < 0) phase += VisitorNeeds.MoodTicks;
        long slot = now + phase;
        int waited = 0;
        for (; NeedsTick() <= slot && waited < 49; waited++) Tick(1);
        Present();
        Check(NeedsTick() > slot, $"guest {id}'s mood slot (needs tick {slot}) is reached inside the approach, after {waited} steps");
        Check(thoughts.Root.GetChildren().OfType<Sprite3D>().Any(b => b.Visible && b.Texture != null),
            "BEFORE service real toilet bubble is visible");
        Tick(49 - waited); Present();
        Check(guest.Cell == stub && !visitors.ServiceHidden(id) && visitors.Boardings == 0 && FullBody(actors.GetValueOrDefault(id)),
            "public stub arrival is still visible, not servicing");
        Tick(1); Present();
        Check(guest.Cell == stub && guest.Next == entry && guest.Progress == 40
            && visitors.Walk.Guests.Contains(guest) && FullBody(actors.GetValueOrDefault(id)),
            "blocked entry reached by physical interpolated terminal leg, not teleport");
        Tick(24);
        uint entered = (uint)(sim.Time / 40);
        var insidePosition = guest.Position;
        Record("entered");
        Check(guest.Cell == entry && insidePosition == ParkPaths.Centre(entry)
            && visitors.Plans[id].At == entry && visitors.Plans[id].Intent == VisitorIntent.Servicing
            && visitors.ServiceHidden(id) && visitors.ReliefDeadline(id) == entered + 522
            && !visitors.Walk.Guests.Any(g => g.Id == id),
            "inside-centre arrival starts Servicing with literal +522 deadline");
        Check(visitors.QueuedOwner(id) == ride && NoOtherOwner() && registered.ContainsKey(ride),
            "native hiding discriminator: live standing owner, no host hide, seat, WALK or HUSH ownership");
        Present(); await Frames();
        Check(!actors.ContainsKey(id) && !standing.ContainsKey(id) && !GodotObject.IsInstanceValid(original)
            && guestRoot.GetChildCount() == 0, "DURING service body is absent after two process frames, not just invisible");
        Check(NoBubble(), "DURING service no visible or orphan thought bubble");
        sim.SetOpen(ride.Id, false); // No second visit; closure must not cut residence short.
        Tick(522); Present(); await Frames(); Record("+522-wait");
        Check((uint)(sim.Time / 40) == entered + 522 && visitors.ServiceHidden(id) && !Clock().Finishing
            && visitors.Relieved == 0 && visitors.Rides == 0 && visitors.Needs.Of(id).Toilet == 100
            && visitors.Needs.Of(id).Cash == 1234, "+522 equality remains hidden and unrelieved");
        Check(!actors.ContainsKey(id) && guestRoot.GetChildCount() == 0 && NoBubble() && NoOtherOwner(),
            "+522 presentation remains absent without alternate hiding authority");
        Tick(1); Present(); Record("+523-state22");
        Check((uint)(sim.Time / 40) == entered + 523 && visitors.ServiceHidden(id) && Clock().Finishing
            && visitors.Relieved == 0 && visitors.Needs.Of(id).Toilet == 100 && !actors.ContainsKey(id) && NoBubble(),
            "+523 enters finishing only, still no body or relief");
        Tick(1); Present(); await Frames(); Record("+524-completion");
        var returned = visitors.Walk.Guests.SingleOrDefault(g => g.Id == id);
        Check((uint)(sim.Time / 40) == entered + 524 && returned != null
            && !visitors.ServiceHidden(id) && visitors.ReliefDeadline(id) == null
            && visitors.Relieved == 1 && visitors.Rides == 1 && visitors.Boardings == 1,
            "+524 reveals SAME original guest identity and completes exactly once");
        Check(returned?.Cell == entry && returned.Next == null && returned.Progress == 0
            && returned.Position == insidePosition && returned.Position == ParkPaths.Centre(entry),
            "native release retains physical g.Cell and identical inside-centre position");
        var expectedWorld = (Vector3)Call(viewer, "GuestWorld",
            new Vector3(insidePosition.X, insidePosition.Y, insidePosition.Z), entry);
        var park = Field<Park>(viewer, "_park");
        actors.TryGetValue(id, out var revealed);
        Check(FullBody(revealed) && revealed.Position.DistanceTo(expectedWorld) < .0001f
            && Mathf.Abs(revealed.Position.Y - (park.CellY(entry.X, entry.Z) + insidePosition.Y)) < .0001f,
            "revealed full body uses GuestWorld and physical entry floor, not public stub");
        Check(actors.Count == 1 && guestRoot.GetChildCount() == 1
            && visitors.Walk.Guests.Count(g => g.Id == id) == 1 && !standing.ContainsKey(id),
            "exactly one body and one walker after reveal, no standing duplicate");
        Check(visitors.Needs.Of(id).Cash == 1234 && visitors.Needs.Of(id).Toilet == 0 && NoBubble(),
            "completion preserves Cash, clears Toilet and removes toilet thought");
        // Leave the isolated fixture alive for the caller's normal final teardown.
    }
}
