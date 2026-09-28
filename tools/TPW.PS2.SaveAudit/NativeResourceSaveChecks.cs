using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

/// <summary>Disc-free owner checks, not a whole-save/coordinator or live-lease restore test.</summary>
public static class NativeResourceSaveChecks
{
    public static void Run(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        string Json<T>(T value) => JsonSerializer.Serialize(value);
        T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(Json(value))!;
        void Equal<T>(T a, T b, string name) => check(Json(a) == Json(b), name);
        void Reject<T>(T state, Action<T> factory, Func<T> original, string name)
        {
            string before = Json(original());
            string input = Json(state);
            bool rejected = false;
            try { factory(state); }
            catch (ArgumentException) { rejected = true; }
            check(rejected, name + ": rejected");
            check(before == Json(original()) && input == Json(state), name + ": no mutation");
        }
        void RequiredMembers<T>(T state)
        {
            var original = JsonNode.Parse(Json(state))!.AsObject();
            foreach (string key in original.Select(p => p.Key))
            {
                var missing = original.DeepClone().AsObject();
                missing.Remove(key);
                bool rejected = false;
                try { JsonSerializer.Deserialize<T>(missing.ToJsonString()); }
                catch (JsonException) { rejected = true; }
                check(rejected, typeof(T).FullName + ": required " + key);
            }
        }

        var pool = new NativeRoutePool();
        check(pool.TryBuild(new[] { new Point(101, 203), new Point(-5, 999), new Point(9, 17) }, out int head),
            "resource route setup");
        pool.Reset(); // Nonzero inactive coordinates and links must survive capture.
        pool.Reset();
        int first = pool.Allocate();
        int middle = pool.Allocate();
        int last = pool.Allocate();
        pool.SetNext(first, last);
        pool.FreeOne(middle); // Allocation hole and reduced hint.
        var state = RoundTrip(pool.CaptureState());
        RequiredMembers(state);
        var restored = NativeRoutePool.FromState(state);
        Equal(state, restored.CaptureState(), "resource pool JSON exact round trip");
        check(state.Words[middle] != 0 && !restored.IsAllocated(middle), "inactive packed bits retained");
        check(pool.Allocate() == restored.Allocate(), "resource pool next allocation matches");
        pool.FreeChain(first);
        restored.FreeChain(first);
        Equal(pool.CaptureState(), restored.CaptureState(), "resource pool free-chain continuation");
        for (int i = 0; i <= NativeRoutePool.Capacity; i++)
            check(pool.Allocate() == restored.Allocate(), "resource pool allocation continuation through exhaustion");
        Equal(pool.CaptureState(), restored.CaptureState(), "resource pool exhausted state");
        pool.Reset(); restored.Reset();
        Equal(pool.CaptureState(), restored.CaptureState(), "resource pool reset continuation");

        var before = pool.CaptureState();
        pool.CaptureState().Words[0] ^= uint.MaxValue;
        Equal(before, pool.CaptureState(), "resource pool capture clone");
        restored = NativeRoutePool.FromState(state);
        var loadedBefore = restored.CaptureState();
        state.Words[0] ^= uint.MaxValue;
        Equal(loadedBefore, restored.CaptureState(), "resource pool restore input clone");
        state = RoundTrip(loadedBefore);
        restored = NativeRoutePool.FromState(state);
        restored.Allocate(); restored.Reset();
        Equal(loadedBefore, state, "resource pool live changes leave input alone");
        Equal(before, pool.CaptureState(), "resource pool owners independent");

        // Arbitrary inactive bits are not a link invariant, and the epoch can wrap to zero.
        var inactive = pool.CaptureState();
        inactive.Words[999] = 0xffff7fffu; // Low link bits 0x7ff; all unused bits retained.
        inactive.Words[998] = 0x12347456u; // Inactive, out-of-range stale low11 link.
        inactive = inactive with { ResetGeneration = ulong.MaxValue };
        var inactivePool = NativeRoutePool.FromState(RoundTrip(inactive));
        Equal(inactive, inactivePool.CaptureState(), "resource pool retains every inactive word bit");
        inactivePool.Reset();
        check(inactivePool.ResetGeneration == 0, "resource pool epoch wrap");
        Equal(inactivePool.CaptureState(), NativeRoutePool.FromState(inactivePool.CaptureState()).CaptureState(),
            "resource pool zero epoch accepted");

        var dangling = new NativeRoutePool();
        int a = dangling.Allocate(), b = dangling.Allocate();
        dangling.SetNext(a, b); dangling.FreeOne(b);
        var danglingCopy = NativeRoutePool.FromState(RoundTrip(dangling.CaptureState()));
        Equal(dangling.CaptureState(), danglingCopy.CaptureState(), "resource pool legitimate dangling link retained");
        bool danglingRejected = false;
        var danglingBefore = danglingCopy.CaptureState();
        try { danglingCopy.FreeChain(a); }
        catch (InvalidOperationException) { danglingRejected = true; }
        check(danglingRejected, "resource pool dangling free-chain still refuses");
        Equal(danglingBefore, danglingCopy.CaptureState(), "resource pool dangling refusal atomic");

        var shared = new NativeRoutePool();
        int left = shared.Allocate(), right = shared.Allocate(), tail = shared.Allocate();
        shared.SetNext(left, tail); shared.SetNext(right, tail);
        var sharedState = shared.CaptureState();
        sharedState.Words[left] |= 0x7800u; // Unused live bits are storage too, not a schema error.
        Equal(sharedState, NativeRoutePool.FromState(RoundTrip(sharedState)).CaptureState(),
            "resource pool shared tail and unused live bits retained");

        var valid = new NativeRoutePool();
        a = valid.Allocate(); b = valid.Allocate(); valid.SetNext(a, b);
        var p = valid.CaptureState();
        var badLink = (uint[])p.Words.Clone(); badLink[a] = (badLink[a] & ~0x7ffu) | 1000u;
        var cycle = (uint[])p.Words.Clone(); cycle[b] = (cycle[b] & ~0x7ffu) | (uint)a;
        var self = (uint[])p.Words.Clone(); self[a] = (self[a] & ~0x7ffu) | (uint)a;
        foreach (var bad in new[] {
            p with { SchemaVersion = 0 }, p with { SchemaVersion = 2 },
            p with { Words = null! }, p with { Words = new uint[999] }, p with { Words = new uint[1001] },
            p with { Hint = -1 }, p with { Hint = 1001 }, p with { Hint = 3 },
            p with { Available = -1 }, p with { Available = 1001 }, p with { Available = p.Available + 1 },
            p with { Words = badLink }, p with { Words = cycle }, p with { Words = self } })
            Reject(bad, s => NativeRoutePool.FromState(s), valid.CaptureState, "resource pool corrupt state");
        Reject<NativeRoutePool.State>(null!, s => NativeRoutePool.FromState(s), valid.CaptureState, "resource pool null state");

        var sequence = new NativeActivationSequence(uint.MaxValue - 1, "represented port lifetimes", true);
        check(sequence.Activate("guest") == uint.MaxValue - 1, "resource sequence setup serial");
        var sequenceState = RoundTrip(sequence.CaptureState());
        RequiredMembers(sequenceState);
        var sequenceCopy = NativeActivationSequence.FromState(sequenceState);
        Equal(sequence.CaptureState(), sequenceCopy.CaptureState(), "resource sequence JSON round trip");
        check(sequence.Activate("staff") == uint.MaxValue && sequenceCopy.Activate("staff") == uint.MaxValue,
            "resource sequence uint max serial");
        check(sequence.NextSerial == 0 && sequenceCopy.NextSerial == 0, "resource sequence uint wrap");
        foreach (string kind in new[] { "guest", "Guest", "staff", "new-family" })
            check(sequence.Activate(kind) == sequenceCopy.Activate(kind), "resource sequence serial continuation");
        Equal(sequence.CaptureState(), sequenceCopy.CaptureState(), "resource sequence counter continuation");
        check(sequenceCopy.Origin == "represented port lifetimes" && sequenceCopy.NativeHistoryVerified,
            "resource sequence provenance retained");
        var seqBefore = sequence.CaptureState();
        sequence.CaptureState().ActivationsByKind.Clear();
        Equal(seqBefore, sequence.CaptureState(), "resource sequence capture clone");
        var copyBefore = sequenceCopy.CaptureState();
        sequenceState.ActivationsByKind.Clear();
        Equal(copyBefore, sequenceCopy.CaptureState(), "resource sequence restore input clone");
        sequenceState = RoundTrip(seqBefore);
        sequenceCopy = NativeActivationSequence.FromState(sequenceState);
        sequenceCopy.Activate("another");
        Equal(seqBefore, sequenceState, "resource sequence live changes leave input alone");
        Equal(seqBefore, sequence.CaptureState(), "resource sequence owners independent");

        // No invented relation between serial and count; caller chooses the initial serial.
        var wrapped = seqBefore with {
            NextSerial = 27, Activations = ulong.MaxValue, NativeHistoryVerified = false,
            ActivationsByKind = new() { ["guest"] = ulong.MaxValue }
        };
        var wrapSequence = NativeActivationSequence.FromState(RoundTrip(wrapped));
        check(wrapSequence.Activate("guest") == 27 && wrapSequence.Activations == 0
            && wrapSequence.ActivationsByKind["guest"] == 0, "resource sequence ulong counter wrap");
        Equal(wrapSequence.CaptureState(), NativeActivationSequence.FromState(wrapSequence.CaptureState()).CaptureState(),
            "resource sequence zero wrapped kind count accepted");
        var modular = wrapped with { Activations = 0, ActivationsByKind = new() { ["a"] = ulong.MaxValue, ["b"] = 1 } };
        Equal(modular, NativeActivationSequence.FromState(modular).CaptureState(), "resource sequence modular counter sum");
        foreach (var bad in new[] {
            seqBefore with { SchemaVersion = 0 }, seqBefore with { SchemaVersion = 2 },
            seqBefore with { Origin = null! }, seqBefore with { Origin = " \t" },
            seqBefore with { ActivationsByKind = null! }, seqBefore with { Activations = seqBefore.Activations + 1 },
            seqBefore with { ActivationsByKind = new() { [""] = seqBefore.Activations } },
            seqBefore with { ActivationsByKind = new() { [" "] = seqBefore.Activations } } })
            Reject(bad, s => NativeActivationSequence.FromState(s), sequence.CaptureState, "resource sequence corrupt state");
        Reject<NativeActivationSequence.State>(null!, s => NativeActivationSequence.FromState(s), sequence.CaptureState,
            "resource sequence null state");
    }
}
