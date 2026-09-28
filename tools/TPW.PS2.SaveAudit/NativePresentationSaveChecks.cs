using System.Buffers.Binary;
using System.Text.Json;
using TPW.PS2.Data;

/// <summary>Disc-free native playback continuation. No renderer or extracted/private assets.</summary>
public static class NativePresentationSaveChecks
{
    static string Json<T>(T x) => JsonSerializer.Serialize(x);
    static T Round<T>(T x) => JsonSerializer.Deserialize<T>(Json(x))!;
    static void W(byte[] b, int p, uint x) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p, 4), x);

    static Animation Asset()
    {
        var b = new byte[0x300]; W(b, 0, Animation.Magic); W(b, 4, Animation.Version);
        b[0x1c] = 12; W(b, 0x20, 0x30); int at = 0x90;
        for (int s = 0; s < 12; s++)
        {
            int count = s == 5 ? 3 : s < 4 ? 2 : 0;
            W(b, 0x30 + s * 8, (uint)count); W(b, 0x34 + s * 8, (uint)at);
            for (int v = 0; v < count; v++, at += 0x1c) W(b, at + 4, (uint)(3 + v * 3));
        }
        return new Animation(b);
    }
    static NativeLogicalAnimationTable Table()
    {
        // A single synthetic ELF load segment includes the fixed table addresses. 33 descriptors.
        const uint first = NativeLogicalAnimationTable.FirstDescriptor;
        int Off(uint x) => checked((int)(x - first + 0x100));
        var b = new byte[Off(NativeLogicalAnimationTable.IdleTable) + 16];
        b[0] = 127; b[1] = 69; b[2] = 76; b[3] = 70; b[4] = 1; b[5] = 1;
        W(b, 28, 52); b[42] = 32; b[44] = 1;
        W(b, 52, 1); W(b, 56, 0x100); W(b, 60, first); W(b, 68, (uint)b.Length - 0x100);
        uint ptr = first;
        for (int logical = 0; logical < 22; logical++)
        {
            int count = logical == 11 ? 6 : logical == 21 ? 7 : 1;
            int row = Off(NativeLogicalAnimationTable.TableAddress) + logical * 8;
            W(b, row, ptr); W(b, row + 4, (uint)count);
            for (int v = 0; v < count; v++, ptr += 32)
            {
                int p = Off(ptr);
                W(b, p, logical == 9 ? 2u : 15u); W(b, p + 4, 0);
                W(b, p + 8, logical == 0 ? 14u : logical == 13 ? 1u : (uint)(v % 2));
                W(b, p + 12, (uint)(v % 2));
                W(b, p + 16, logical == 9 || logical == 13 ? 3u : 15u);
                W(b, p + 20, 0); W(b, p + 24, logical == 11 ? 2u : logical == 0 ? 1u : 0u);
                W(b, p + 28, 20);
            }
        }
        for (int i = 0; i < 4; i++) b[Off(NativeLogicalAnimationTable.IdleTable) + i * 4] = (byte)new[] { 9, 13, 11, 21 }[i];
        return new NativeLogicalAnimationTable(b);
    }
    sealed class BusSink
    {
        internal readonly List<string> Events = new();
        internal NativeBusController? Owner;
        internal NativeBusController.SnapshotBindings Bindings => new() {
            ServicesId = "bus-services", Bind = r => { Inspect(); Events.Add($"bind:{r.Slot}:{r.Offset}"); },
            Sample = f => { Inspect(); Events.Add($"sample:{BitConverter.SingleToInt32Bits(f)}"); },
            StateCommand = (s, c) => { Inspect(); Events.Add($"state:{s}:{c}"); },
            RequestBatch = n => { Inspect(); Events.Add($"batch:{n}"); } };
        void Inspect() { if (Owner != null && Owner.State is < 0 or > 3) throw new Exception("callback saw unhydrated owner"); }
    }
    sealed class GuestSink
    {
        internal int Calls;
        internal NativeGuestAnimation? Owner;
        internal readonly NewlibRand Random = new(0xabcdef01);
        internal NativeGuestAnimation.SnapshotBindings Bindings(Animation a, NativeLogicalAnimationTable t) => new() {
            Animation = a, Table = t, DurationId = "guest-aps", RandomId = "shared-newlib", Random = Random,
            Duration = (s, v) => {
                Calls++;
                if (Owner != null && Owner.Control.Phase > 2) throw new Exception("duration saw unhydrated control");
                var sections = a.Sections();
                return (uint)s < sections.Count && (uint)v < sections[s].Count ? a.ReadRecord(sections[s].Offset + v * 0x1c).DurationFrames : null;
            } };
    }

    public static int Run()
    {
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("native presentation save: " + why); checks++; }
        void Reject(Action a, string why) { try { a(); } catch (InvalidDataException) { checks++; return; } throw new Exception("accepted " + why); }
        var asset = Asset(); var table = Table();
        var liveSink = new BusSink(); var init = liveSink.Bindings;
        var bus = new NativeBusController(asset, 0, uint.MaxValue - 300, init.Bind, init.Sample, init.StateCommand, init.RequestBatch);
        liveSink.Owner = bus;
        bool progress = false, hold = false, dwell = false, wrap = false, veto = false, batch = false, lag = false;
        uint clock = uint.MaxValue - 300;
        for (int tick = 0; tick < 100; tick++)
        {
            var state = Round(bus.CaptureState("bus-services"));
            progress |= state.RecordVariant >= 0 && !state.EndHold;
            hold |= state.EndHold; dwell |= state.DwellRemaining > 0; lag |= state.State != state.AppliedState;
            var leftSink = new BusSink(); var rightSink = new BusSink();
            var left = NativeBusController.FromState(state, asset, leftSink.Bindings); leftSink.Owner = left;
            var right = NativeBusController.FromState(state, new Animation((byte[])asset.D.Clone()), rightSink.Bindings); rightSink.Owner = right;
            Check(leftSink.Events.Count == 0 && rightSink.Events.Count == 0, "bus restore called callback");
            // Compare restored branch against the original, too; a mutually wrong restore cannot pass.
            liveSink.Events.Clear();
            uint next = unchecked(clock + 100); wrap |= next < clock;
            int traffic = tick % 7 == 0 ? 1 : 0, flagged = tick % 3 == 0 ? 31 : 0;
            int actual = bus.Update(next, 0x28000, traffic, true, true, flagged);
            int result = right.Update(next, 0x28000, traffic, true, true, flagged);
            Check(actual == result && Json(bus.CaptureState("bus-services")) == Json(right.CaptureState("bus-services")), "bus original continuation");
            Check(liveSink.Events.SequenceEqual(rightSink.Events), "bus fresh callbacks differ");
            Check(bus.PresentationFrame(next, .5f) == right.PresentationFrame(next, .5f), "presentation clock continuation");
            veto |= bus.PressureVetoes > 0; batch |= liveSink.Events.Any(e => e.StartsWith("batch:"));
            for (int j = 1; j <= 12; j++)
            {
                uint c = unchecked(clock + (uint)j * 100);
                left.Update(c, 0x28000, 0, true, true, 0);
            }
            var replaySink = new BusSink(); var replay = NativeBusController.FromState(state, asset, replaySink.Bindings); replaySink.Owner = replay;
            for (int j = 1; j <= 12; j++) replay.Update(unchecked(clock + (uint)j * 100), 0x28000, 0, true, true, 0);
            Check(Json(left.CaptureState("bus-services")) == Json(replay.CaptureState("bus-services")) && leftSink.Events.SequenceEqual(replaySink.Events), "bus long replay");
            clock = next;
        }
        Check(progress && hold && dwell && wrap && veto && batch && lag, "bus scenario coverage");
        var badBus = bus.CaptureState("bus-services"); var quiet = new BusSink();
        Reject(() => NativeBusController.FromState(badBus with { Frame = float.NaN }, asset, quiet.Bindings), "NaN bus");
        Reject(() => NativeBusController.FromState(badBus with { RecordVariant = 3 }, asset, quiet.Bindings), "bus record index");
        Reject(() => NativeBusController.FromState(badBus with { ServicesId = "wrong" }, asset, quiet.Bindings), "bus service identity");
        var changed = (byte[])asset.D.Clone(); changed[^1] ^= 1;
        Reject(() => NativeBusController.FromState(badBus, new Animation(changed), quiet.Bindings), "equal-duration changed asset");
        Check(quiet.Events.Count == 0, "malformed bus invoked callbacks");

        var source = new GuestSink(); var bindings = source.Bindings(asset, table);
        var guest = new NativeGuestAnimation(table, bindings.Duration) { Requested = 9, Stamp = int.MaxValue - 10 }; source.Owner = guest;
        bool pending = false, readiness = false, held = false, phase2 = false, multi = false;
        for (int tick = 0; tick < 100; tick++)
        {
            if (tick % 10 == 0) guest.Requested = new[] { 9, 13, 11, 21, 0 }[(tick / 10) % 5];
            guest.Push(tick % 13 == 0 ? 2 : 0);
            pending |= guest.Control.Pending != 255; readiness |= !guest.PermitsMovement;
            var saved = Round(guest.CaptureState(bindings));
            var fresh = new GuestSink(); fresh.Random.RestoreState(saved.RandomState);
            var freshBindings = fresh.Bindings(asset, table);
            int oldCalls = source.Calls; uint before = fresh.Random.CaptureState();
            var restored = NativeGuestAnimation.FromState(saved, freshBindings); fresh.Owner = restored;
            Check(source.Calls == oldCalls && fresh.Calls == 0 && fresh.Random.CaptureState() == before, "guest restore called duration/random");
            Check(Json(saved) == Json(restored.CaptureState(freshBindings)), "guest roundtrip exact");
            if (tick % 17 == 0)
            {
                int drawA = tick, drawB = tick;
                guest.IdlePick(unchecked(int.MaxValue + tick), n => (drawA++ * 7) % n, table.IdleStates);
                restored.IdlePick(unchecked(int.MaxValue + tick), n => (drawB++ * 7) % n, table.IdleStates);
                Check(guest.Requested == restored.Requested && drawA == drawB, "idle stamp and external guest RNG continuation");
            }
            float dt = tick % 3 == 0 ? 100 : 50;
            guest.Update(dt, source.Random.Next); restored.Update(dt, fresh.Random.Next);
            Check(Json(guest.CaptureState(bindings)) == Json(restored.CaptureState(freshBindings)), "guest original step continuation");
            Check(guest.PermitsMovement == restored.PermitsMovement && source.Calls - oldCalls == fresh.Calls, "guest readiness and fresh duration callbacks");
            held |= guest.Held != null; phase2 |= guest.Control.Phase == 2; multi |= guest.Control.Variant > 0;
            // Continue both detached models through several boundaries and weighted rerolls.
            var forkState = guest.CaptureState(bindings); var forkSink = new GuestSink(); forkSink.Random.RestoreState(forkState.RandomState);
            var forkBindings = forkSink.Bindings(asset, table); var fork = NativeGuestAnimation.FromState(forkState, forkBindings); forkSink.Owner = fork;
            for (int j = 0; j < 10; j++) { fork.Update(100, forkSink.Random.Next); restored.Update(100, fresh.Random.Next); }
            Check(Json(fork.CaptureState(forkBindings)) == Json(restored.CaptureState(freshBindings)), "guest boundary replay");
        }
        Check(pending && readiness && held && phase2 && multi, "guest scenario coverage");
        var bad = guest.CaptureState(bindings); var rejectSink = new GuestSink(); rejectSink.Random.RestoreState(bad.RandomState);
        var rb = rejectSink.Bindings(asset, table);
        Reject(() => NativeGuestAnimation.FromState(bad with { Slot = 1000 }, rb), "guest slot");
        Reject(() => NativeGuestAnimation.FromState(bad with { Control = bad.Control with { Phase = 3 } }, rb), "guest phase");
        Reject(() => NativeGuestAnimation.FromState(bad with { RandomId = "wrong" }, rb), "guest rng identity");
        Reject(() => NativeGuestAnimation.FromState(bad with { RandomState = bad.RandomState ^ 1 }, rb), "guest rng current state");
        Reject(() => NativeGuestAnimation.FromState(bad with { Frame = float.PositiveInfinity }, rb), "guest infinity");
        Check(rejectSink.Calls == 0 && rejectSink.Random.CaptureState() == bad.RandomState, "malformed guest touched providers");
        try { JsonSerializer.Deserialize<NativeGuestAnimation.Snapshot>("{}"); throw new Exception("missing required members accepted"); }
        catch (JsonException) { checks++; }
        return checks;
    }
}
