using System.Text;
using System.Text.RegularExpressions;
using TPW.PS2.Data;

/// <summary>Native guest-state residence, not the facility's RSE animation duration.
/// All assets stay in the supplied archives; missing named fixtures are failures, not skips.</summary>
static class NativeReliefChecks
{
    public static void Run(Model terrain, WadArchive data, WadArchive world, string worldName,
                           Action<bool, string> check)
    {
        void Check(bool ok, string text) => check(ok, "native relief: " + text);
        // Literal numbers deliberately do not derive the oracle from Duration.
        var clock = new ReliefServiceClock(100);
        Check(clock.Deadline == 622 && clock.LastTick == 100 && !clock.Finishing && !clock.Completed,
              "clock starts at entered+522 without progressing");
        for (int i = 0; i < 10; i++)
            Check(!clock.Advance(100) && clock.LastTick == 100 && !clock.Finishing,
                  "repeated entry counter cannot age residence " + i);
        Check(!clock.Advance(621) && !clock.Finishing, "+521 still waiting");
        Check(!clock.Advance(622) && !clock.Finishing && clock.LastTick == 622, "+522 equality still waiting");
        Check(!clock.Advance(623) && clock.Finishing && !clock.Completed, "+523 only enters finishing");
        Check(!clock.Advance(623) && !clock.Completed, "same finishing counter cannot complete");
        Check(clock.Advance(624) && clock.Completed && clock.LastTick == 624, "+524 completes once");
        Check(!clock.Advance(624) && !clock.Advance(625), "completion never pays twice");
        var zero = new ReliefServiceClock(0);
        Check(zero.Deadline == 522 && !zero.Advance(0) && !zero.Advance(522)
              && !zero.Finishing && !zero.Advance(523) && zero.Finishing && zero.Advance(524),
              "zero-origin literal boundaries also hold");

        string[] stems = worldName.ToUpperInvariant() switch
        {
            "JUNGLE" => new[] { "/Features/Toilet/Toilet", "/Features/SupBog/SupBog" },
            "HALLOW" => new[] { "/features/horloo/horloo", "/features/horsuloo/horsuloo" },
            "FANTASY" => new[] { "/Features/loo/loo", "/Features/royaloo/royaloo" },
            "SPACE" => new[] { "/features/loo/loo", "/features/loo_big/loo_big" },
            _ => throw new ArgumentException("Unknown native relief world " + worldName)
        };
        var compiled = new CompiledAssets(new AssetResourceDatabase(data.Read(data.Find("/arsdb.dba"))),
                                          TextDatabase.Load(data, "eur"));
        ParkPaths Empty()
        {
            var p = new ParkPaths(terrain);
            for (int i = 1; i < p.Field.Cells.Length; i += 2) p.Field.Cells[i] = 0;
            return p;
        }
        VisitorNeeds FreezeNeeds()
        {
            var n = new VisitorNeeds(871) { SecondsPerRise = 1_000_000 };
            foreach (var key in n.Rates.Keys.ToArray()) n.Rates[key] = new(0, 0, false);
            n.Unknown78Bar = n.SickBar = n.ToiletBar = n.HungerBar = n.ThirstBar = 101;
            return n;
        }
        VisitorWants Initial() => new() { Cash = 1234, Toilet = 100, Sick = 65, Hunger = 37,
            Thirst = 42, Happiness = 75, Unknown78 = 12, Litter = 9, Boredom = 20, PreferredIntensity = 90 };
        // Thought is refreshed by the live mood producer even when arithmetic rise rates
        // are frozen. Continuity pins every stored need and cash, not an obsolete bubble.
        bool SameNeeds(VisitorWants actual,VisitorWants expected)
            => (actual with { Thought=expected.Thought }).Equals(expected);
        void Ticks(ParkVisitors v, int count) { for (int i = 0; i < count; i++) v.Step(.04, null); }

        foreach (string stem in stems)
        {
            // Catch per named case so a missing super toilet cannot hide the other case (or world).
            try
            {
                string sam = Encoding.ASCII.GetString(world.Read(world.Find(stem + ".sam")
                    ?? throw new InvalidDataException("missing SAM " + stem)));
                RideDefinition Parse(string text) => RideDefinition.Parse(text, "/DATA/" + worldName + ".WAD" + stem + ".sam");
                var def = Parse(sam);
                compiled.Attach(new[] { def }, out _); // return value counts shops, not joined feature geometry
                Check(def.CompiledEntry != null && def.CompiledEntry == compiled.For(worldName, stem + ".sam"),
                      stem + " real shared Attach joins exact named identity");
                var rec = def.CompiledEntry ?? throw new InvalidDataException("missing compiled " + stem);
                Check(rec.Kind == AssetResourceDatabase.AssetKind.Feature && (rec.RawFeatureFlags.GetValueOrDefault() & 1) != 0,
                      stem + " compiled feature bit1 is the relief authority");
                // Authoring and placement are independent controls, not substitutes for the compiled flag.
                var unjoined = Parse(sam);
                var noAuthoredRelief = Parse(Regex.Replace(sam, @"(ProvidesRelief\s+)\d+", "${1}0"));
                Check(!noAuthoredRelief.ProvidesRelief, stem + " authored-off control really disables SAM relief");
                compiled.Attach(new[] { noAuthoredRelief }, out _);
                Check(noAuthoredRelief.CompiledEntry == rec,
                      stem + " authored-off definition still attaches by identity");
                var other = world.Entries.First(e => e.Path.EndsWith(".sam", StringComparison.OrdinalIgnoreCase)
                    && compiled.For(worldName, e.Path) is { Kind: AssetResourceDatabase.AssetKind.Feature } f
                    && (f.RawFeatureFlags.GetValueOrDefault() & 1) == 0);
                // A deliberately conflicting SAM attached to a real non-relief identity must
                // not turn that compiled feature into a toilet, even with an inside cell.
                var wrongFlag = RideDefinition.Parse(sam, "/DATA/" + worldName + ".WAD" + other.Path);
                compiled.Attach(new[] { wrongFlag }, out _);
                Check(wrongFlag.CompiledEntry != null && !new ParkRide
                    { Definition = wrongFlag, ServiceEntry = new ParkCell(0, 0) }.NativeRelief,
                    stem + " compiled non-relief bit vetoes authored toilet metadata");
                var paths = Empty();
                var origin = paths.Cells.First(c => Enumerable.Range(-3, rec.Width + 6).All(x =>
                    Enumerable.Range(-3, rec.Depth + 6).All(z => paths.CanLay(c.Offset(x, z)))));
                var a = rec.ConnectionA;
                var entry = origin.Offset(a.X, a.Z);
                var dir = a.Direction switch { 0 => new ParkCell(0, -1), 1 => new ParkCell(-1, 0),
                                               2 => new ParkCell(0, 1), _ => new ParkCell(1, 0) };
                var stub = entry.Offset(dir.X, dir.Z);
                var start = stub.Offset(dir.X, dir.Z);
                Check(a.IsPresent && a.Direction <= 3 && ShopEntrance.Inside(rec, origin, 0, rec.Width, rec.Depth, stub) == entry,
                      stem + " feature connection A admits inside geometry");
                Check(ShopEntrance.Inside(rec, origin, 0, rec.Width, rec.Depth, stub.Offset(1, 1)) == null,
                      stem + " wrong stub cannot synthesize service entry");
                Check(!new ParkRide { Definition = def }.NativeRelief
                      && !new ParkRide { Definition = unjoined, ServiceEntry = entry }.NativeRelief
                      && new ParkRide { Definition = noAuthoredRelief, ServiceEntry = entry }.NativeRelief,
                      stem + " native eligibility requires compiled relief AND validated placement, not SAM");
                int material = Enumerable.Range(1, paths.Materials.Count - 1)
                    .First(i => ParkPaths.Classify(paths.Materials[i]) == ParkPathKind.Path);
                var script = world.Read(world.Find(stem + ".rse") ?? throw new InvalidDataException("missing RSE " + stem));
                string folder = stem[..(stem.LastIndexOf('/') + 1)];
                var aps = world.Find(stem + ".aps") ?? world.Entries.FirstOrDefault(e =>
                    e.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && !e.Path[folder.Length..].Contains('/')
                    && e.Path.EndsWith(".aps", StringComparison.OrdinalIgnoreCase));
                // FANTASY genuinely has no APS: null animation is supported, never skip its service.
                byte[] Sibling(string name) => world.Find(folder + name) is { } e ? world.Read(e) : null;
                ParkRide Place(ParkSim sim)
                {
                    var r = sim.Add(71, stem, origin, rec.Width, rec.Depth, script,
                        aps == null ? null : new Animation(world.Read(aps)), 1, stub, stub, out var fault,
                        sibling: Sibling, definition: def, placementTurns: 0) ?? throw new InvalidOperationException(fault);
                    sim.SetOpen(r.Id, true); r.Set("VAR_BROKEN", 0);
                    return r;
                }
                (ParkVisitors V, ParkRide R, Guest G) Fixture()
                {
                    var p = Empty(); p.Lay(stub, material); p.Lay(start, material);
                    var sim = new ParkSim(p); var r = Place(sim);
                    var v = new ParkVisitors(sim, new GuestWalk(p), () => 0) { Needs = FreezeNeeds() };
                    var g = v.Arrive(start, start); v.Needs.Set(g.Id, Initial());
                    Check(r.NativeRelief && r.ServiceEntry == entry && v.SendTo(g, r), stem + " real placed native visit routes");
                    return (v, r, g);
                }
                var (v, ride, guest) = Fixture();
                Ticks(v, 25);
                Check(guest.Cell == stub && !v.ServiceHidden(guest.Id) && v.Boardings == 0,
                      stem + " public stub arrival cannot hide or serve");
                v.Step(.04, null);
                Check(guest.Next == entry && guest.Progress == 40 && v.Walk.Guests.Contains(guest) && v.Boardings == 0,
                      stem + " approach is a physical interpolated leg");
                Ticks(v, 24);
                uint entered = (uint)(v.Sim.Time / 40);
                Check(guest.Cell == entry && v.Plans[guest.Id].At == entry && v.Plans[guest.Id].Intent == VisitorIntent.Servicing
                      && v.ServiceHidden(guest.Id) && !v.Walk.Guests.Any(g => g.Id == guest.Id) && v.Boardings == 1
                      && v.ReliefDeadline(guest.Id) == entered + 522 && ride.Queue.Count == 0 && ride.OnRide == 0,
                      stem + " hides only at inside centre, in Servicing with native deadline");
                // Inject an apparent RSE handback. It must neither complete nor bypass native residence.
                Check(ride.Has("VAR_LETMEOFF"), stem + " RSE handback injection targets a declared variable");
                ride.Set("VAR_LETMEOFF", guest.Id); ride.Set("VAR_DURATION", 0); ride.Set("VAR_LETMEON", 0);
                for (int i = 0; i < 10; i++) v.Step(0, null);
                Check(v.ServiceHidden(guest.Id) && v.Rides == 0 && v.Relieved == 0 && SameNeeds(v.Needs.Of(guest.Id),Initial()),
                      stem + " zero calls and RSE handback/duration cannot pay relief");
                long before = v.Sim.Time;
                v.Step(100, null);
                Check(v.Sim.Time == before + 320 && v.ServiceHidden(guest.Id) && v.ReliefDeadline(guest.Id) == entered + 522,
                      stem + " dropped 100-second frame ages only eight executed 40ms ticks");
                v.Sim.SetOpen(ride.Id, false);
                Ticks(v, 514); // 8 + 514 = 522, independent of the production Duration constant.
                Check(v.ServiceHidden(guest.Id) && v.Rides == 0 && v.Relieved == 0 && SameNeeds(v.Needs.Of(guest.Id),Initial()),
                      stem + " +522 has no early relief despite closure/handback");
                v.Step(.04, null);
                Check(v.ServiceHidden(guest.Id) && v.Rides == 0 && SameNeeds(v.Needs.Of(guest.Id),Initial()),
                      stem + " +523 is finishing, not completion");
                v.Step(0, null);
                Check(v.ServiceHidden(guest.Id) && v.Relieved == 0, stem + " zero call cannot finish state22");
                v.Step(.04, null);
                var returned = v.Walk.Guests.SingleOrDefault(g => g.Id == guest.Id);
                var expected = Initial(); expected.Toilet = 0; expected.Sick = 25;
                Check(v.Rides == 1 && v.Relieved == 1 && v.Purchases == 0 && v.Boardings == 1
                      && !v.ServiceHidden(guest.Id) && v.ReliefDeadline(guest.Id) == null
                      && returned?.Cell == entry && returned.Next == null && returned.Progress == 0,
                      stem + " +524 closed-midservice finishes and readmits same identity inside");
                Check(SameNeeds(v.Needs.Of(guest.Id),expected) && ride.Condition == 74 && v.Sim.Finances.Balance == 0,
                      stem + " handback relieves bladder/sickness once, preserves cash and all other needs");
                Ticks(v, 30); v.Step(0, null);
                Check(v.Rides == 1 && v.Relieved == 1 && SameNeeds(v.Needs.Of(guest.Id),expected) && ride.Condition == 74,
                      stem + " later ticks cannot duplicate completion");

                var (busy, owner, first) = Fixture();
                var second = busy.Arrive(start, start); busy.Needs.Set(second.Id, Initial());
                Check(busy.SendTo(second, owner), stem + " second guest is already approaching before occupancy");
                Ticks(busy, 50);
                // 1309F8 gates AC only when actual APS section5 has exactly two records.
                // A larger toilet without that pair accepts both visitors; do not impose a
                // universal occupancy rule merely because the Small Toilet is single-user.
                bool gates = aps != null && new Animation(world.Read(aps)).Sections() is {} ss
                    && ss.Count>5 && ss[5].Count==2;
                Check(owner.ReliefUsesOccupancy==gates,stem+" availability marker comes from actual APS section5 count");
                Check(busy.ServiceHidden(first.Id) && busy.ServiceHidden(second.Id)==!gates
                      && (busy.ReliefDeadline(second.Id)!=null)==!gates
                      && busy.Walk.Guests.Any(g => g.Id == second.Id)==gates
                      && busy.Boardings == (gates?1:2) && busy.Relieved == 0
                      && SameNeeds(busy.Needs.Of(second.Id),Initial()),
                      stem + " second guest follows exact APS-count occupancy predicate");
                busy.Sim.Remove(owner.Id);
                var replacement = Place(busy.Sim); // Replace BEFORE reconciliation: equal ID is not equal ownership.
                busy.Step(0, null);
                var aborted = busy.Walk.Guests.SingleOrDefault(g => g.Id == first.Id);
                Check(aborted?.Cell == entry && aborted.Next == null && !busy.ServiceHidden(first.Id)
                      && busy.ReliefDeadline(first.Id) == null && busy.Rides == 0 && busy.Relieved == 0
                      && SameNeeds(busy.Needs.Of(first.Id),Initial()) && owner.Condition == 100,
                      stem + " deletion aborts without payout and reveals at retained inside entry");
                Check(aborted != null && !busy.SendTo(aborted, replacement) && !busy.ServiceHidden(first.Id),
                      stem + " same-ID replacement cannot inherit service or occupied doorway");
                busy.Sim.SetOpen(replacement.Id, false);
                Ticks(busy, 550);
                Check(busy.Rides == 0 && busy.Relieved == 0 && SameNeeds(busy.Needs.Of(first.Id),Initial())
                      && SameNeeds(busy.Needs.Of(second.Id),Initial()), stem + " aborted timer cannot later pay either identity");
                Check(aborted != null && busy.Walk.Send(aborted, start), stem + " deleted-owner terminal retains physical egress");
                busy.Step(.04, null);
                Check(aborted?.Next == stub && aborted.Progress == 40, stem + " abort recovery exits by real walking edge");
            }
            catch (Exception e)
            {
                Check(false, stem + " fixture failed: " + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
