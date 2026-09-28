using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;

/// <summary>Disc-free real track consumer checks. No private setters, reflection or replay on
/// restore. This covers these two owners, not a whole park save or Viewer load.</summary>
public static class TrackRideSaveChecks
{
    const string GroundKey = "fixture/track-ground/revision-1";
    static string Json<T>(T value) => JsonSerializer.Serialize(value);
    static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(Json(value))!;

    // The public rectangle fixture from ParkSimAudit/TrackRideChecks: 22 pieces, both bend hands.
    static TrackLayout Loop(TrackGround ground, int rotation = 0, int side = 1)
    {
        var l = new TrackLayout(new(40, 40), rotation, ground);
        var e = l.ExitCell; var r = l.ReturnCell;
        int ox = Math.Sign(e.X - r.X), oz = Math.Sign(e.Z - r.Z);
        int sx = -oz * side, sz = ox * side;
        foreach (var c in new[] { e.Offset(6 * ox, 6 * oz), e.Offset(6 * ox + 8 * sx, 6 * oz + 8 * sz),
            e.Offset(-8 * ox + 8 * sx, -8 * oz + 8 * sz), e.Offset(-8 * ox, -8 * oz), r }) l.Add(c);
        return l;
    }

    static ParkCell Box(TrackPiece p) => (p.Type & 3) switch
    {
        0 => p.Anchor.Offset(-1, 0), 1 => p.Anchor.Offset(-1, -2),
        2 => p.Anchor.Offset(0, -1), _ => p.Anchor.Offset(-2, -1)
    };

    public static void Run(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        void Check(bool ok, string why) => check(ok, "track snapshot: " + why);
        void Reject(Action action, string why)
        {
            bool rejected = false;
            try { action(); }
            catch (ArgumentException) { rejected = true; }
            catch (JsonException) { rejected = true; }
            Check(rejected, "reject " + why);
        }
        void Required<T>(T value)
        {
            var n = JsonNode.Parse(Json(value))!.AsObject();
            foreach (var key in n.Select(p => p.Key).ToArray())
            {
                var bad = n.DeepClone().AsObject(); bad.Remove(key);
                Reject(() => JsonSerializer.Deserialize<T>(bad.ToJsonString()), typeof(T).Name + " missing " + key);
            }
        }

        int groundQueries = 0;
        var ground = new TrackGround { World = 0, Park = 0, Bridged = _ => { groundQueries++; return false; } };
        var layout = Loop(ground);
        // Buy in reverse chain order, with different kinds; never sort upgrade records on save.
        foreach (int from in new[] { 12, 3 })
        {
            int at = Enumerable.Range(from, layout.Pieces.Count - from - 2)
                .First(i => layout.Pieces[i].Type is >= 4 and <= 7 && layout.Pieces[i + 1].Type == layout.Pieces[i].Type);
            Check(layout.AddUpgrade(from == 12 ? 1 : 0, Box(layout.Pieces[at])), "upgrade fixture purchase");
        }
        var ls = Copy(layout.CaptureState(GroundKey));
        Check(ls.Upgrades.Length == 2 && ls.Upgrades[0].Index > ls.Upgrades[1].Index, "non-sorted upgrades fixture");
        Required(ls); Required(ls.Station); Required(ls.Pieces[0]); Required(ls.Pieces[0].Samples[0]); Required(ls.Upgrades[0]);
        groundQueries = 0;
        var restoredLayout = TrackLayout.FromState(ls, GroundKey, ground);
        Check(groundQueries == 0 && Json(ls) == Json(restoredLayout.CaptureState(GroundKey)), "layout JSON, no rebuild or ground query");
        Check(!ReferenceEquals(layout.Pieces[0], restoredLayout.Pieces[0])
            && !ReferenceEquals(layout.Pieces[0].Samples, restoredLayout.Pieces[0].Samples)
            && ReferenceEquals(restoredLayout.Ground, ground), "layout owns new pieces/samples and resolved ground");
        string stableLayout = Json(restoredLayout.CaptureState(GroundKey));
        ls.Pieces[0].Samples[0] = ls.Pieces[0].Samples[0] with { Yaw = -999 };
        ls.Waypoints[1] = ls.Waypoints[1] with { X = 4 }; ls.Upgrades[0] = ls.Upgrades[0] with { Kind = 0 };
        Check(Json(restoredLayout.CaptureState(GroundKey)) == stableLayout, "all layout input arrays detached");
        var capturedLayout = restoredLayout.CaptureState(GroundKey);
        capturedLayout.Pieces[0].Samples[0] = capturedLayout.Pieces[0].Samples[0] with { Height = -321 };
        capturedLayout.Pieces[1] = capturedLayout.Pieces[0]; capturedLayout.Waypoints[1] = capturedLayout.Waypoints[0];
        capturedLayout.Upgrades[0] = capturedLayout.Upgrades[1];
        Check(Json(restoredLayout.CaptureState(GroundKey)) == stableLayout, "all layout capture arrays detached");
        restoredLayout.Pieces[0].Samples[0] = default;
        Check(Json(layout.CaptureState(GroundKey)) == stableLayout, "fresh live samples do not alias original");

        // Open and partially laid chains are authoritative, including zero or noncanonical baked
        // samples. Preserve Closed independently instead of guessing it from the final waypoint.
        var open = new TrackLayout(new(40, 40), 0, ground);
        open.Add(open.ExitCell.Offset(6, 0));
        var partial = Copy(open.CaptureState(GroundKey));
        partial.Pieces[1].Samples[2] = new TrackLayout.SampleState
            { P1X = 71, P1Z = -19, P2X = 100, P2Z = 4, Height = -8, Yaw = 7168 };
        partial = partial with { Pieces = partial.Pieces.Take(2).ToArray() };
        var partialCopy = TrackLayout.FromState(partial, GroundKey, ground);
        Check(!partialCopy.Closed && Json(partial) == Json(partialCopy.CaptureState(GroundKey)), "partial open chain retained without re-laying");
        var independentClosed = layout.CaptureState(GroundKey) with { Closed = false };
        Check(Json(independentClosed) == Json(TrackLayout.FromState(independentClosed, GroundKey, ground).CaptureState(GroundKey)),
            "Closed is stored, not inferred");
        // Open upgrade indices differ by one from the laid slot; future editing must still agree.
        var openUpgrade = Loop(ground); openUpgrade.RemoveLast();
        int pair = Enumerable.Range(3, openUpgrade.Pieces.Count - 5).First(i => openUpgrade.Pieces[i].Type is >= 4 and <= 7
            && openUpgrade.Pieces[i + 1].Type == openUpgrade.Pieces[i].Type);
        openUpgrade.AddUpgrade(0, Box(openUpgrade.Pieces[pair]));
        var openClone = TrackLayout.FromState(Copy(openUpgrade.CaptureState(GroundKey)), GroundKey, ground);
        Check(openUpgrade.Upgrades.Count == 1 && openUpgrade.Upgrades[0].Index == pair - 1, "open upgrade fixture");
        openUpgrade.Add(openUpgrade.ReturnCell); openClone.Add(openClone.ReturnCell);
        Check(Json(openUpgrade.CaptureState(GroundKey)) == Json(openClone.CaptureState(GroundKey)), "future close preserves upgrade slot semantics");

        var valid = layout.CaptureState(GroundKey);
        foreach (var bad in new[] { valid with { Version = 0 }, valid with { GroundKey = "wrong" },
            valid with { GroundWorld = 1 }, valid with { GroundPark = 1 }, valid with { Rotation = 4 },
            valid with { Waypoints = null! }, valid with { Waypoints = Array.Empty<TrackLayout.CellState>() },
            valid with { Waypoints = new TrackLayout.CellState[TrackLayout.MaxWaypoints + 1] },
            valid with { Pieces = null! }, valid with { Pieces = new TrackLayout.PieceState[TrackLayout.MaxPieces + 1] },
            valid with { Upgrades = null! }, valid with { Upgrades = new TrackLayout.UpgradeState[TrackLayout.MaxUpgrades + 1] },
            valid with { Station = valid.Station with { X = int.MaxValue } } })
            Reject(() => TrackLayout.FromState(bad, GroundKey, ground), "layout version/key/range/length");
        foreach (Action<TrackLayout.State> corrupt in new Action<TrackLayout.State>[] {
            s => s.Pieces[0] = s.Pieces[0] with { Type = TrackPieces.Count },
            s => s.Pieces[0] = s.Pieces[0] with { Samples = new TrackLayout.SampleState[3] },
            s => s.Pieces[0].Samples[0] = null!, s => s.Waypoints[0] = s.Waypoints[1],
            s => s.Upgrades[0] = s.Upgrades[0] with { Index = s.Pieces.Length },
            s => s.Upgrades[0] = s.Upgrades[0] with { Kind = 16 } })
        {
            var bad = Copy(valid); corrupt(bad);
            Reject(() => TrackLayout.FromState(bad, GroundKey, ground), "malformed piece/sample/upgrade reference");
        }
        Reject(() => TrackLayout.FromState(valid, "independently-wrong", ground), "independent key binding");
        Reject(() => TrackLayout.FromState(valid, GroundKey, new TrackGround { World = 1 }), "resolved ground metadata");
        Reject(() => TrackLayout.FromState(valid, GroundKey, null!), "unresolved ground");
        Reject(() => TrackLayout.FromState(null!, GroundKey, ground), "null layout state");
        Check(Json(valid) == Json(layout.CaptureState(GroundKey)), "all rejected layouts leave original untouched");

        foreach (bool karts in new[] { false, true })
        {
            var g = new TrackGround { World = 0, Park = karts ? 0 : 1 };
            var track = Loop(g, karts ? 2 : 0, karts ? -1 : 1);
            var live = new TrackRideSim(track, seed: 3) { Capacity = 4, Duration = 2, Speed = 71 };
            var queue = new Queue<int>(new[] { 91, 13, 77, 42, 88, 29, 65, 10 });
            live.TakeHead = () => queue.TryDequeue(out int id) ? id : null;
            uint tick = 0;
            while (tick < 1000 && !(live.Status == TrackRideStatus.Unloading && live.Cars.Count == 4
                && live.Cars.Any(c => c.Speed != 0)
                && (!karts || live.CaptureState().Cars.Any(c => c.Timer > 0)))) live.Step(++tick);
            Check(tick < 1000 && live.Cars.Count == 4 && live.RunTimer > 0, $"{karts}: loaded midrace and nonzero run timer");
            var saved = Copy(live.CaptureState());
            Check(saved.Random.Inext != 0 && saved.Cars.Any(c => karts ? c.Timer > 0 : c.WobbleRate != 0), "advanced RNG and live car timers/wobble");
            Required(saved); Required(saved.Cars[0]);
            int queries = 0;
            var stagedGround = new TrackGround { World = g.World, Park = g.Park,
                Bridged = _ => { queries++; throw new InvalidOperationException("restore queried ground"); } };
            var freshTrack = TrackLayout.FromState(Copy(track.CaptureState(GroundKey)), GroundKey, stagedGround);
            var clone = TrackRideSim.FromState(saved, freshTrack);
            Check(queries == 0 && ReferenceEquals(clone.Track, freshTrack) && !ReferenceEquals(clone.Track, live.Track)
                && !ReferenceEquals(clone.Cars[0], live.Cars[0]) && clone.Wear == null && clone.BreakdownCheck == null
                && clone.TakeHead() == null && Json(clone.CaptureState()) == Json(saved), "allocation-only fresh sim; callbacks await parent binding");
            var cloneQueue = new Queue<int>(queue);
            clone.TakeHead = () => cloneQueue.TryDequeue(out int id) ? id : null;
            var eventsA = new List<string>(); var eventsB = new List<string>();
            void Bind(TrackRideSim sim, List<string> events)
            {
                sim.Released += id => events.Add($"release:{id}");
                sim.Boarded += (id, car) => events.Add($"board:{id}:{car.Index}");
                sim.SoundCue += (car, cue) => events.Add($"sound:{car.Index}:{cue}");
                sim.Wear = () => events.Add("wear");
                sim.BreakdownCheck = () => events.Add("breakdown-check");
            }
            Bind(live, eventsA); Bind(clone, eventsB);
            string initial = Json(saved);
            saved.Cars[0] = saved.Cars[0] with { Total = -99 }; saved.Random.SeedArray[1] ^= 1;
            Check(Json(clone.CaptureState()) == initial && Json(live.CaptureState()) == initial, "sim input car and RNG arrays detached");
            var detached = clone.CaptureState(); detached.Cars[0] = detached.Cars[0] with { Guest = -99 }; detached.Random.SeedArray[2] ^= 1;
            Check(Json(clone.CaptureState()) == initial, "sim capture car/RNG arrays detached");
            bool moved = false, matched = true, geometry = true;
            int released = 0, boarded = 0, sounds = 0;
            for (int step = 0; step < 6000; step++)
            {
                // Real public controls, including Closed while cars are already moving.
                if (step == 17) { live.SetOpen(false); clone.SetOpen(false); }
                if (step == 41) { live.SetOpen(true); clone.SetOpen(true); }
                live.Step(++tick); clone.Step(tick);
                string now = Json(live.CaptureState());
                moved |= now != initial;
                matched &= now == Json(clone.CaptureState()) && eventsA.SequenceEqual(eventsB) && queue.SequenceEqual(cloneQueue);
                for (int i = 0; i < live.Cars.Count; i++)
                    geometry &= track.Position(live.Cars[i].Distance, live.Cars[i].Lateral)
                        == freshTrack.Position(clone.Cars[i].Distance, clone.Cars[i].Lateral);
                released += eventsA.Count(e => e.StartsWith("release:"));
                boarded += eventsA.Count(e => e.StartsWith("board:"));
                sounds += eventsA.Count(e => e.StartsWith("sound:"));
                eventsA.Clear(); eventsB.Clear();
            }
            Check(matched && geometry, $"{karts}: every field/car order/RNG/event/queue/position continues identically for 6000 public Steps");
            Check(moved && released == 8 && boarded == 4 && live.Cars.Count == 0 && live.Status == TrackRideStatus.Loading
                && (!karts || sounds > 0), $"{karts}: non-inert controls, complete releases and next boarding cycle (sounds={sounds})");
            Check(Json(track.CaptureState(GroundKey)) == Json(freshTrack.CaptureState(GroundKey)), "separately owned layouts remain identical");

            var simState = JsonSerializer.Deserialize<TrackRideSim.State>(initial)!;
            foreach (var bad in new[] { simState with { Version = 2 }, simState with { Status = (TrackRideStatus)255 },
                simState with { Random = null! }, simState with { Cars = null! },
                simState with { Cars = new TrackRideSim.CarState[TrackRideSim.SnapshotCarLimit + 1] } })
                Reject(() => TrackRideSim.FromState(bad, freshTrack), "sim version/status/array bound");
            foreach (Action<TrackRideSim.State> corrupt in new Action<TrackRideSim.State>[] {
                s => s.Cars[0] = null!, s => s.Cars[0] = s.Cars[0] with { IsKart = !s.Karts },
                s => s.Cars[0] = s.Cars[0] with { Heading = -1 }, s => s.Cars[0] = s.Cars[0] with { State = 7 },
                s => s.Cars[0] = s.Cars[0] with { Index = -1 }, s => s.Random.SeedArray[1] = -1 })
            {
                var bad = Copy(simState); corrupt(bad);
                Reject(() => TrackRideSim.FromState(bad, freshTrack), "car fields/RNG");
            }
            Reject(() => TrackRideSim.FromState(simState, null!), "unresolved track");
            Reject(() => TrackRideSim.FromState(null!, freshTrack), "null sim state");
            var noRng = JsonNode.Parse(initial)!.AsObject(); noRng["Random"]!["SeedArray"] = new JsonArray(0);
            Reject(() => TrackRideSim.FromState(JsonSerializer.Deserialize<TrackRideSim.State>(noRng.ToJsonString())!, freshTrack), "RNG length");
            // Native u16 distance and sbyte lap can wrap; do not incorrectly validate against
            // length/lap target. Settings are writable integers, not normalized during restore.
            var edge = Copy(simState) with { Speed = -1, Duration = -2, Capacity = 0, RunTimer = int.MinValue };
            edge.Cars[0] = edge.Cars[0] with { Distance = ushort.MaxValue, Lap = sbyte.MinValue,
                Total = int.MinValue, Timer = byte.MaxValue, PushBack = -10 };
            var edgeCopy = TrackRideSim.FromState(Copy(edge), freshTrack);
            Check(Json(edge) == Json(edgeCopy.CaptureState()), "raw narrow-field wrapping/settings preserved without gameplay normalization");
            // Distinct sentinels cover even inactive class-specific storage and statuses whose
            // normal enter handlers reset timers or unload cars. FromState must not run those.
            var raw = Copy(simState);
            raw.Cars[0] = new TrackRideSim.CarState
            {
                Guest = null, IsKart = karts, Index = 7, Distance = 65000, Total = -17001,
                Lap = -17, Lateral = -29, Heading = 4095, Speed = -23, Target = 251, Rank = 9,
                Finished = true, Colour = 3, BaseMax = 241, BaseAccel = 231, Accel = 221,
                State = 6, Timer = 211, Aggressive = true, PushBack = -191, Wobble = -181, WobbleRate = 171
            };
            foreach (var status in Enum.GetValues<TrackRideStatus>())
            {
                var staged = raw with { Status = status, RunTimer = 123 };
                var owner = TrackRideSim.FromState(Copy(staged), freshTrack);
                Check(Json(staged) == Json(owner.CaptureState()), "all car storage and status " + status + " hydrate without enter handler");
            }
        }
    }
}
