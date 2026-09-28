using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;

/// <summary>Disc-free checks using the actual APS reader and actual preview host, not a mock.</summary>
public static class RseHostSaveChecks
{
    // Minimal authored APS: two slots, two variants each, no tracks. Playback only needs
    // duration, but these records still pass through Animation.Records on each fresh host.
    static Animation Fixture()
    {
        var bytes = new byte[0x40 + 4 * 0x1c];
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
        U32(0, Animation.Magic); U32(4, Animation.Version);
        bytes[0x1c] = 2; U32(0x20, 0x28);
        U32(0x28, 2); U32(0x2c, 0x40); U32(0x30, 2); U32(0x34, 0x78);
        for (int i = 0; i < 4; i++) U32(0x44 + i * 0x1c, (uint)(30 + i * 30));
        return new Animation(bytes);
    }

    public static void Run(Action<bool, string> check)
    {
        const string key = "fixture/preview-aps/v1";
        string Json(RsePreviewHost h) => JsonSerializer.Serialize(h.CaptureState(key));
        RsePreviewHost.State Decode(string json) => JsonSerializer.Deserialize<RsePreviewHost.State>(json)!;
        void Equal(RsePreviewHost a, RsePreviewHost b, string why) => check(Json(a) == Json(b), "RseHost: " + why);
        var live = new RsePreviewHost(Fixture()) { HeadSlots = 4 };
        live.PlayAnimationSpeed(0, 1, 2000); // two seconds at double speed, ends at 1000
        live.AdvanceTo(250);
        live.PlayAnimation(1, 1, true); // pending loop
        live.PlayAnimationOn(1, 1, 0, true);
        live.PlayAnimationOn(-2, 0, 0, false);
        live.HeadAt(0, 101); live.HeadAt(3, 202);
        live.WalkerPose(101, 2, 3, 4, 555, -90);
        live.GuestVisible(101, false); live.GuestVisible(202, true);
        var opcode = Enum.GetValues<RseOpcode>()[0];
        live.TryEffect(opcode, new[] { 8, 9 });
        live.AdvanceTo(400);
        string saved = Json(live);
        var dto = Decode(saved);
        check(dto.Queued is { Start: 1000, Loop: true, Slot: 1, Variant: 1 } &&
            dto.Current.Speed == 2000 && dto.Channels.Count == 2, "RseHost: pending/rate/channel fixture");
        var restored = new RsePreviewHost(Fixture());
        int callbacks = 0, nodes = 0;
        restored.EffectRequested += _ => callbacks++;
        restored.WalkerMoved += _ => callbacks++;
        restored.HeadChanged += _ => callbacks++;
        restored.NodeSource = (_, _) => { nodes++; return (1f, 2f, 3f); };
        var binding = restored.NodeSource;
        restored.RestoreState(dto, key);
        Equal(live, restored, "JSON roundtrip complete logical state");
        check(live.Channels.Keys.SequenceEqual(restored.Channels.Keys),
            "RseHost: public channel iteration order preserved (fixture inserts1 before-2)");
        check(live.Seats.Keys.SequenceEqual(restored.Seats.Keys)&&live.Walkers.Keys.SequenceEqual(restored.Walkers.Keys)
            &&live.Visibility.Keys.SequenceEqual(restored.Visibility.Keys),"RseHost: guest-map iteration order preserved");
        check(callbacks == 0 && nodes == 0 && ReferenceEquals(binding, restored.NodeSource),
            "RseHost: restore preserves bindings without emission/query");
        check(!ReferenceEquals(live.Current.Record, restored.Current.Record), "RseHost: target asset records resolved locally");
        dto.Seats.Clear(); dto.Channels.Clear(); dto.LastEffect.Arguments[0] = -1;
        dto.Walkers[0] = default; dto.Visibility[0] = default;
        check(Json(restored) == saved, "RseHost: restore detached all mutable DTO storage");
        var detached = restored.CaptureState(key);
        detached.Seats.Clear(); detached.Channels.Clear(); detached.LastEffect.Arguments[0] = -1;
        detached.Walkers[0] = default; detached.Visibility[0] = default;
        check(Json(restored) == saved, "RseHost: capture detached all mutable DTO storage");

        void Reject(Action<JsonObject> edit, string why, string expected = key)
        {
            var bad = JsonNode.Parse(saved)!.AsObject(); edit(bad);
            string before = Json(restored); int c = callbacks, n = nodes;
            bool rejected = false;
            try { restored.RestoreState(Decode(bad.ToJsonString()), expected); }
            catch (ArgumentException) { rejected = true; }
            catch (JsonException) { rejected = true; }
            check(rejected && before == Json(restored) && callbacks == c && nodes == n,
                "RseHost: reject atomically/silently " + why);
        }
        Reject(s => s["Version"] = 99, "version");
        Reject(s => s["AssetKey"] = "other", "asset key");
        Reject(_ => { }, "expected identity", "different binding");
        Reject(s => s["Time"] = -1, "negative time");
        Reject(s => s["HeadSlots"] = -1, "head slots");
        Reject(s => s["Current"]!["Slot"] = 99, "unknown slot");
        Reject(s => s["Current"]!["Variant"] = 99, "unknown variant");
        Reject(s => s["Current"]!["Speed"] = 0, "zero rate");
        Reject(s => s["Queued"]!["Start"] = 401, "queue timing");
        Reject(s => s["Current"] = null, "orphan queue");
        Reject(s => s["Channels"]!["0"] = s["Current"]!.DeepClone(), "channel zero");
        Reject(s => s["Channels"]!["1"] = null, "null channel");
        Reject(s => s["Seats"]!["0"] = 0, "empty seat member");
        Reject(s => s["Walkers"]![0]!["Time"] = 401, "future walker");
        Reject(s => s["Walkers"]!.AsArray().Add(s["Walkers"]![0]!.DeepClone()), "duplicate walker");
        Reject(s => s["Visibility"]!.AsArray().Add(s["Visibility"]![0]!.DeepClone()), "duplicate visibility");
        Reject(s => s["LastEffect"]!["Arguments"] = null, "effect payload after valid fields");
        Reject(s => s["LastEffect"]!["Time"] = -1, "effect timestamp");
        foreach (string name in new[] { "Channels", "Seats", "Walkers", "Visibility" })
            Reject(s => s[name] = null, "null " + name);
        foreach (string name in JsonNode.Parse(saved)!.AsObject().Select(p => p.Key))
            Reject(s => s.Remove(name), "missing required " + name);
        foreach (string name in JsonNode.Parse(saved)!["Current"]!.AsObject().Select(p => p.Key))
            Reject(s => s["Current"]!.AsObject().Remove(name), "missing playback " + name);

        foreach (long t in new long[] { 999, 1000, 1250, 5000 })
        {
            live.AdvanceTo(t); restored.AdvanceTo(t);
            Equal(live, restored, "continue at " + t);
            check(live.Frame == restored.Frame && live.FrameOn(1) == restored.FrameOn(1) &&
                live.AnimationRemainingOn(1) == restored.AnimationRemainingOn(1) &&
                live.AnimationRemainingOn(-2) == restored.AnimationRemainingOn(-2), "RseHost: playback observations " + t);
        }
        void Continue(RsePreviewHost h)
        {
            h.HeadAt(0, 0); h.HeadAt(1, 303); h.GuestVisible(101, true);
            h.WalkerPose(202, 3, 7, 1, 1000, 45); h.TryEffect(opcode, new[] { 42 });
            h.PlayAnimationSpeed(0, 0, 500); h.PlayAnimation(1, 0, false);
            h.FlushAnimation(); h.PlayAnimation(0, 1, false);
        }
        Continue(live); Continue(restored);
        Equal(live, restored, "continue membership/effect/flush and requeue");
        check(callbacks == 4, "RseHost: rebound callbacks still active for subsequent operations");
        restored.TryNodePosition(1, 2, out float x, out _, out _);
        check(nodes == 1 && x == 1, "RseHost: rebound node source remains active");
        var empty = new RsePreviewHost(null);
        var emptyCopy = new RsePreviewHost(null);
        emptyCopy.RestoreState(Decode(Json(empty)), key);
        Equal(empty, emptyCopy, "empty host/null playback and effect");
        bool missingRecord = false;
        try { emptyCopy.RestoreState(Decode(saved), key); }
        catch (ArgumentException) { missingRecord = true; }
        check(missingRecord && Json(empty) == Json(emptyCopy), "RseHost: actual bound asset must contain saved records");
    }
}
