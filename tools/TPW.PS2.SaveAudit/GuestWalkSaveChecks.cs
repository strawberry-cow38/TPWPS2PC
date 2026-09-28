using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

/// <summary>Walking-owner tests only. No whole-save/Viewer/controller persistence claim.</summary>
public static class GuestWalkSaveChecks
{
    static string Json<T>(T value) => JsonSerializer.Serialize(value);
    static T Round<T>(T value) => JsonSerializer.Deserialize<T>(Json(value))!;
    static void Required<T>(T value, Action<bool, string> check)
    {
        var node = JsonNode.Parse(Json(value))!.AsObject();
        foreach (string key in node.Select(p => p.Key).ToArray())
        {
            var bad = node.DeepClone().AsObject(); bad.Remove(key); bool rejected = false;
            try { JsonSerializer.Deserialize<T>(bad.ToJsonString()); } catch (JsonException) { rejected = true; }
            check(rejected, typeof(T).Name + " requires " + key);
        }
        node["UnknownMember"] = 0; bool unknown = false;
        try { JsonSerializer.Deserialize<T>(node.ToJsonString()); } catch (JsonException) { unknown = true; }
        check(unknown, typeof(T).Name + " rejects unknown fields");
    }

    public static void Run(Action<bool, string> check)
    {
        void C(bool ok, string why) => check(ok, "walking cursor save: " + why);
        // NativeGuestRouteChecks/NativeWalkConsumerChecks controls: signed raw positions,
        // quarter-cell target, speed15 and native fixed point delta0x4000 (not seconds).
        var source = new NativeGuestRoute(new Point(640, 640), new[] { new Point(640, 576), new Point(640, 384) });
        source.Step(15, 0x4000, 8, 8, true);
        var state = Round(source.CaptureState()); var poolState = Round(source.Pool.CaptureState());
        var pool = NativeRoutePool.FromState(poolState);
        var restored = NativeGuestRoute.FromState(state, pool);
        C(ReferenceEquals(restored.Pool, pool) && !ReferenceEquals(source.Pool, pool), "adopts same fresh pool, not a rebuilt copy");
        Required(state, C); Required(state.Position, C);
        C(Json(state) == Json(restored.CaptureState()), "all cursor fields roundtrip mid-segment");
        string untouched = Json(source.CaptureState());
        restored.Step(15, 0x4000, 8, 8, false);
        C(Json(restored.CaptureState()) == untouched, "readiness wait preserves pose/control state");
        bool sawRetainedSlot = false, sawFreedSlot = false;
        for (int i = 0; i < 30; i++)
        {
            var a = source.Step(15, 0x4000, 8, 8, true);
            var b = restored.Step(15, 0x4000, 8, 8, true);
            C(a == b && Json(source.CaptureState()) == Json(restored.CaptureState())
                && Json(source.Pool.CaptureState()) == Json(pool.CaptureState()), "exact continuation " + i);
            sawRetainedSlot |= restored.ExecutionState == 2 && restored.SlotIndex >= 0;
            sawFreedSlot |= pool.Available > poolState.Available;
            // Every release/no-slot/completion phase itself is a restorable state.
            restored = NativeGuestRoute.FromState(Round(restored.CaptureState()), pool);
        }
        C(sawRetainedSlot && sawFreedSlot && restored.Finished && pool.Available == NativeRoutePool.Capacity,
            "deferred release and completion free the SAME adopted pool");
        C(!poolState.Words.SequenceEqual(pool.CaptureState().Words), "freed slot allocation bits changed");
        C(Json(poolState) == Json(NativeRoutePool.FromState(poolState).CaptureState()), "input pool DTO unchanged");
        restored.Dispose();
        C(Json(restored.CaptureState()) == Json(NativeGuestRoute.FromState(restored.CaptureState(), pool).CaptureState()), "disposed cursor retained");
        var failing = new NativeGuestRoute(new Point(128, 128), new[] { new Point(-128, 128) });
        failing.Step(127, 0x40000, 8, 8, true);
        C(failing.Failed && Json(failing.CaptureState()) == Json(NativeGuestRoute.FromState(failing.CaptureState(),
            NativeRoutePool.FromState(failing.Pool.CaptureState())).CaptureState()), "bounds failure retains exact previous position/control");
        foreach (var bad in new[] { state with { Version = 9 }, state with { Position = null! },
            state with { Generation = state.Generation + 1 }, state with { SlotIndex = 1000 },
            state with { FacingQuarterTurns = 4 }, state with { Finished = true }, state with { ExecutionState = 1 } })
        {
            string before = Json(pool.CaptureState()); bool rejected = false;
            try { NativeGuestRoute.FromState(bad, pool); } catch (ArgumentException) { rejected = true; }
            C(rejected && before == Json(pool.CaptureState()), "invalid cursor rejected without pool mutation");
        }
        bool inactive = false;
        try { NativeGuestRoute.FromState(state, pool); } catch (ArgumentException) { inactive = true; }
        C(inactive, "current slot referencing a freed slot is rejected, never reallocated");
    }

    public static void Run(Disc disc, Action<bool, string> check)
    {
        void C(bool ok, string why) => check(ok, "guest walk save: " + why);
        var archive = disc.Files().Single(f => f.Path.Equals("/DATA/JUNGLE.WAD", StringComparison.OrdinalIgnoreCase));
        var wad = new WadArchive(disc.Read(archive.Extent, archive.Size));
        const string terrainPath = "/terrain/terrain_1.mps", gridKey = "JUNGLE" + terrainPath;
        var terrain = new Model(wad.Read(wad.Find(terrainPath)));
        byte[] assetCells = (byte[])terrain.Field.Cells.Clone();
        var entrance = ParkEntrance.Read(disc);
        var paths = new ParkPaths(terrain); paths.SetEntrance(entrance);
        var entry = entrance.Fit(terrain.Field, ParkEntrance.WalkwayColumnFromPoles(terrain), out _);
        var start = new ParkCell(entry.XCol, entry.ZEnd - 2); var next = start.Offset(0, -1);
        C(paths.Open(start) && paths.Open(next), "known NativeWalkConsumerChecks corridor fixture");
        var ground = ParkGroundSnapshot.Capture(paths, paths.Field, null!, gridKey, "unused");
        ParkPaths FreshPaths() => ParkGroundSnapshot.Restore(ground, gridKey, "unused", terrain, null!).Paths;
        Point centre = new(checked((short)(start.X * 256 + 128)), checked((short)(start.Z * 256 + 128)));
        Point quarter = new(centre.X, (short)(centre.Z - 64));
        var owner = new object(); bool ready = true; int reads = 0, advances = 0;
        var inputs = new NativeMotionInputs(() => { reads++; return 15; }, () => 0x4000, () => ready)
            { SlotAdvanced = () => advances++ };
        var terminal = new GuestTerminal(new ParkRide { Id = 31 }, start, next, () => true);
        Action<uint> before = _ => { }; Func<Guest, bool> paused = _ => false;
        Func<ParkCell, ParkCell, bool> queue = (_, _) => true;
        GuestWalk.StateBindings Bindings(object o, NativeMotionInputs input, GuestTerminal t) => new()
        {
            IdentifyNativeOwner = value => ReferenceEquals(value, o) ? "owner" : throw new ArgumentException(),
            ResolveNativeOwner = id => id == "owner" ? o : throw new ArgumentException(),
            IdentifyNativeInputs = value => ReferenceEquals(value, input) ? "motion" : throw new ArgumentException(),
            ResolveNativeInputs = id => id == "motion" ? input : throw new ArgumentException(),
            IdentifyTerminal = value => ReferenceEquals(value, t) ? "terminal" : throw new ArgumentException(),
            ResolveTerminal = id => id == "terminal" ? t : throw new ArgumentException(),
            IdentifyBeforeStep = _ => "before", ResolveBeforeStep = id => id == "before" ? before : throw new ArgumentException(),
            IdentifyQueueStep = _ => "queue", ResolveQueueStep = id => id == "queue" ? queue : throw new ArgumentException(),
            IdentifyPaused = _ => "paused", ResolvePaused = id => id == "paused" ? paused : throw new ArgumentException()
        };
        var bindings = Bindings(owner, inputs, terminal);
        var walk = new GuestWalk(paths) { BeforeStep = before, Paused = paused, QueueStep = queue };
        var legacy = walk.Spawn(start, next);
        var native = walk.Spawn(start, start);
        C(walk.BeginNativeRoute(native, owner, new[] { quarter, centre }, inputs), "native lease installed");
        var duplicate = walk.Readmit(legacy.Id, start, next);
        var inactive = walk.ReadmitTerminal(91, terminal); walk.Remove(inactive.Id);
        var atTerminal = walk.ReadmitTerminal(92, terminal);
        var targeted = walk.Spawn(start, start);
        C(walk.SendToTerminal(targeted, terminal), "target-terminal permission retained separately from occupied permission");
        walk.Advance(.053); // mid-legacy edge, mid-native segment, nonzero carry
        var capture = walk.CaptureGraph(gridKey, bindings, new[] { inactive });
        var state = Round(capture.Snapshot);
        C(legacy.Progress == 40 && walk.NativeRouteState(native, owner)!.Value.Position != centre && state.Carry == 13,
            "genuine midsegment legacy/native plus carry");
        C(capture.GuestGraphId(legacy) != capture.GuestGraphId(duplicate) && legacy.Id == duplicate.Id, "graph IDs distinct from numeric IDs");
        Required(state, C); Required(state.Guests[0], C); Required(state.Guests[0].Cell, C);
        Required(state.Guests.Single(g => g.NativeLease != null).NativeLease!, C);
        string sourceBefore = Json(walk.CaptureState(gridKey, bindings, new[] { inactive }));
        var staged = GuestWalk.AllocateState(state, gridKey, FreshPaths());
        C(!staged.IsHydrated && !ReferenceEquals(staged.Walk.Paths, paths), "two-phase shells/fresh paths");
        var newOwner = new object(); int newReads = 0, newAdvances = 0;
        // Real cycle-binding seam: a controller/input closure can refer to a guest shell and
        // the exact staged walker/pool before the walker's native lease is hydrated.
        Guest nativeShell = staged.GuestByGraphId(capture.GuestGraphId(native));
        var newInputs = new NativeMotionInputs(() => { newReads++; return 15; }, () => 0x4000,
            () => ready && staged.Walk.IsLive(nativeShell)) { SlotAdvanced = () => newAdvances++ };
        var newTerminal = new GuestTerminal(new ParkRide { Id = 31 }, start, next, () => true);
        var newBindings = Bindings(newOwner, newInputs, newTerminal);
        staged.Hydrate(newBindings);
        var restored = staged.Walk;
        Guest newInactive = staged.GuestByGraphId(capture.GuestGraphId(inactive));
        C(staged.IsHydrated && reads == 1 && newReads == 0 && advances == 0 && newAdvances == 0,
            "binding invokes no motion functions");
        C(!restored.IsLive(newInactive) && restored.IsLive(nativeShell) && !ReferenceEquals(inactive, newInactive)
            && restored.Guests.Select(g => g.Id).SequenceEqual(walk.Guests.Select(g => g.Id)), "ordered live and inactive identities");
        C(Json(state) == Json(restored.CaptureState(gridKey, newBindings, new[] { newInactive })), "ALL fields/refs/pool roundtrip");
        C(sourceBefore == Json(walk.CaptureState(gridKey, bindings, new[] { inactive }))
            && assetCells.SequenceEqual(terrain.Field.Cells), "capture/restore never mutates source/assets");
        // Detached arrays in both directions, even across delayed hydration.
        state.OrderedLiveGuests[0] = 999; state.Guests[0].Route![0] = new() { X = -1, Z = -1 };
        state.SharedNativeRoutePool.Words[0] ^= 0xffff;
        C(Json(capture.Snapshot) == Json(restored.CaptureState(gridKey, newBindings, new[] { newInactive })), "DTO edits cannot change fresh owners");
        int originalReads = reads;
        ready = false;
        var savedPose = nativeShell.Position;
        walk.Advance(.04); restored.Advance(.04);
        C(nativeShell.Position == savedPose && reads == originalReads && newReads == 0, "readiness waiting continuation");
        ready = true;
        for (int i = 0; i < 35; i++)
        {
            double delta = i % 3 == 0 ? .019 : .047;
            C(walk.Advance(delta) == restored.Advance(delta)
                && Json(walk.CaptureState(gridKey, bindings, new[] { inactive }))
                    == Json(restored.CaptureState(gridKey, newBindings, new[] { newInactive })), "exact public Advance continuation " + i);
        }
        C(restored.NativeRouteState(nativeShell, newOwner) is { Finished: true }
            && restored.NativeRoutes.Available == NativeRoutePool.Capacity && advances == newAdvances,
            "completion frees shared walker pool and keeps completed lease");
        C(!restored.BeginNativeRoute(staged.GuestByGraphId(capture.GuestGraphId(duplicate)), newOwner, new[] { centre }, newInputs),
            "restored duplicate liveness counts still refuse native assignment");
        C(walk.Spawn(start, start).Id == restored.Spawn(start, start).Id, "lastId continuation (readmitted IDs do not advance it)");
        var empty = new GuestWalk(FreshPaths()); var waiting = empty.Spawn(start, start);
        for (int i = 0; i < NativeRoutePool.Capacity; i++) empty.NativeRoutes.Allocate();
        C(empty.AssignNativeRoute(waiting, owner, new[] { quarter }, inputs) == GuestWalk.NativeAssignment.Exhausted,
            "real exhaustion retains empty native lease");
        var exhausted = empty.CaptureGraph(gridKey, bindings);
        var restoredEmpty = GuestWalk.FromState(Round(exhausted.Snapshot), gridKey, FreshPaths(), newBindings);
        C(Json(exhausted.Snapshot) == Json(restoredEmpty.Walk.CaptureState(gridKey, newBindings)), "exhausted pool/empty cursor snapshot");
        // The future controller sees exactly this pool, frees one slot, and retries without any rebuild.
        empty.NativeRoutes.FreeOne(13); restoredEmpty.Walk.NativeRoutes.FreeOne(13);
        C(empty.AssignNativeRoute(waiting, owner, new[] { quarter }, inputs)
            == restoredEmpty.Walk.AssignNativeRoute(restoredEmpty.GuestByGraphId(exhausted.GuestGraphId(waiting)), newOwner, new[] { quarter }, newInputs)
            && Json(empty.CaptureState(gridKey, bindings)) == Json(restoredEmpty.Walk.CaptureState(gridKey, newBindings)), "exhaustion recovery uses same pool identity/hint");
        void Reject(GuestWalk.State bad, string expected = gridKey, GuestWalk.StateBindings? resolver = null)
        {
            string stable = Json(walk.CaptureState(gridKey, bindings, new[] { inactive })); bool rejected = false;
            try { GuestWalk.FromState(bad, expected, FreshPaths(), resolver ?? newBindings); }
            catch (ArgumentException) { rejected = true; }
            C(rejected && stable == Json(walk.CaptureState(gridKey, bindings, new[] { inactive }))
                && assetCells.SequenceEqual(terrain.Field.Cells), "invalid DTO/refs leave source/assets unchanged");
        }
        var valid = capture.Snapshot;
        GuestWalk.State Replace(int index, GuestWalk.GuestStateData g)
        { var guests = valid.Guests.ToArray(); guests[index] = g; return valid with { Guests = guests }; }
        Reject(valid with { Version = 99 }); Reject(valid, "wrong-independent-grid");
        Reject(valid with { Width = valid.Width + 1 }); Reject(valid with { OrderedLiveGuests = new[] { 999 } });
        Reject(valid with { OrderedLiveGuests = new[] { 1, 1 } });
        Reject(Replace(1, valid.Guests[1] with { GraphId = valid.Guests[0].GraphId }));
        Reject(Replace(0, valid.Guests[0] with { Cell = null! }));
        Reject(Replace(0, valid.Guests[0] with { RouteIndex = 999 }));
        int ni = Array.FindIndex(valid.Guests, g => g.NativeLease != null);
        var n = valid.Guests[ni]; var lease = n.NativeLease!;
        Reject(Replace(ni, n with { NativeLease = lease with { OwnerId = "unknown" } }));
        Reject(Replace(ni, n with { NativeLease = lease with { InputsId = "unknown" } }));
        Reject(Replace(ni, n with { NativeLease = lease with { AutomaticStep = !lease.AutomaticStep } }));
        Reject(Replace(ni, n with { NativeLease = lease with { Cursor = lease.Cursor with { SlotIndex = 1000 } } }));
        Reject(Replace(0, valid.Guests[0] with { NativeLease = lease, Route = null!, Progress = 0,
            RouteIndex = lease.Cursor.SlotIndex })); // two cursors must not own the same chain
        Reject(Replace(0, valid.Guests[0] with { TargetTerminalId = "missing-terminal" }));
        Reject(valid with { PausedId = "unknown-paused" });
        // Manual controller ownership: ordinary Advance must remain inert; the trusted
        // fresh controller resolves its own lease and advances it explicitly.
        var manualInput = inputs with { AutomaticStep = false };
        var newManualInput = newInputs with { AutomaticStep = false };
        var manualBindings = Bindings(owner, manualInput, terminal);
        var newManualBindings = Bindings(newOwner, newManualInput, newTerminal);
        var manualWalk = new GuestWalk(FreshPaths()); var manualGuest = manualWalk.Spawn(start, start);
        C(manualWalk.BeginNativeRoute(manualGuest, owner, new[] { quarter }, manualInput), "manual owner fixture");
        manualWalk.SetNativeFacing(manualGuest, owner, 3);
        var manualGraph = manualWalk.CaptureGraph(gridKey, manualBindings);
        var manualRestored = GuestWalk.FromState(manualGraph.Snapshot, gridKey, FreshPaths(), newManualBindings);
        var manualShell = manualRestored.GuestByGraphId(manualGraph.GuestGraphId(manualGuest));
        manualWalk.Advance(.04); manualRestored.Walk.Advance(.04);
        C(manualShell.Position == manualGuest.Position && manualShell.NativeHeading == -System.Numerics.Vector3.UnitX
            && manualRestored.Walk.NativeRouteState(manualShell, newOwner)!.Value.Position == centre,
            "manual flag and explicit facing survive automatic Advance without stepping");
        manualWalk.StepOwnedNative(manualGuest, owner); manualRestored.Walk.StepOwnedNative(manualShell, newOwner);
        C(Json(manualWalk.CaptureState(gridKey, manualBindings)) == Json(manualRestored.Walk.CaptureState(gridKey, newManualBindings)),
            "explicit owner continuation retains cursor and same pool");
        C(assetCells.SequenceEqual(terrain.Field.Cells), "all disc-backed operations kept assets read-only in RAM");
    }
}
