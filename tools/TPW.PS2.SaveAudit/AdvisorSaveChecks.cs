using System.Buffers.Binary;
using System.Text.Json;
using TPW.PS2.Data;

/// <summary>Disc-free CORE continuation checks. Run independently or integrate into the parent audit.</summary>
public static class AdvisorSaveChecks
{
    sealed class Producers : IAdvisorProducers
    {
        public int Reads;
        public short Produce(int index) { Reads++; return (short)(index * 3); }
        public int Months => 271;
        public int UpgradeResearchPercent => 1;
    }
    static string Json<T>(T s) => JsonSerializer.Serialize(s);
    static T Copy<T>(T s) => JsonSerializer.Deserialize<T>(Json(s))!;
    public static int Run() => Run(SyntheticCatalogue());
    /// <summary>Optional caller-loaded readonly disc catalogue; never opens or extracts a disc.</summary>
    public static int Run(AdvisorCatalogue catalogue)
    {
        int checks = 0, effects = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("Advisor save: " + name); checks++; }
        void Reject(Action action, string name)
        { try { action(); } catch (InvalidDataException) { checks++; return; } throw new Exception("Accepted " + name); }
        var headers = new byte[24];
        BinaryPrimitives.WriteUInt16LittleEndian(headers.AsSpan(2), 7);
        BinaryPrimitives.WriteInt16LittleEndian(headers.AsSpan(12), 6);
        short[] words = { 6, 56, 19, 7, 3, 0, 1, 0, -1, 0 };
        var ops = new byte[words.Length * 2];
        for (int i = 0; i < words.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(ops.AsSpan(i * 2), words[i]);
        var rules = new AdvisorRules(headers, ops); var prod = new Producers(); uint day = 70000;
        Func<uint> getDay = () => { effects++; return day; };
        var scheduler = new AdvisorScheduler(rules, prod, getDay);
        var sb = new AdvisorStateBindings(); sb.Add("rules", rules); sb.Add("producers", prod); sb.Add("day", getDay);
        foreach (int steps in new[] { 7, 16, 96 })
        {
            for (int i = 0; i < steps; i++) scheduler.Step(() => i % 3 == 0, _ => { }, _ => { });
            scheduler.AddCounter(4, 120);
            var snapshot = Copy(scheduler.CaptureState(sb, true)); int before = effects, reads = prod.Reads;
            var restored = AdvisorScheduler.FromState(snapshot, sb);
            Check(before == effects && reads == prod.Reads, "scheduler restore no reads");
            Check(Json(snapshot) == Json(restored.CaptureState(sb, true)), "scheduler complete roundtrip");
            for (int i = 0; i < 160; i++)
            {
                day++; scheduler.Step(() => true, _ => { }, _ => { }); restored.Step(() => true, _ => { }, _ => { });
                Check(Json(scheduler.CaptureState(sb, true)) == Json(restored.CaptureState(sb, true)), "scheduler future");
            }
            Reject(() => AdvisorScheduler.FromState(snapshot with { VariableCursor = 79 }, sb), "cursor");
            Reject(() => AdvisorScheduler.FromState(snapshot with { RulesFingerprint = "bad" }, sb), "rule fingerprint");
        }
        var clock = new ParkClock();
        var a = new ParkAdvisor(catalogue, rules, clock, prod, random: _ => 0);
        Action<int> sound = _ => effects++; Action<AdvisorPlayback> played = _ => effects++;
        Func<int, int, ushort, int> length = (_, _, _) => 400;
        var lip = new LipTrack(new byte[] { 0, 0, 0, 0, 0x40, 0x0d, 3, 0, 255, 255, 255, 255 });
        Func<int, int, LipTrack> lips = (_, _) => lip;
        a.SpeechLength = length; a.Lips = lips; a.Played = played; a.UiSound = sound; a.Stack.UiSound = sound;
        var ride = new object(); var restoredRide = new object();
        AdvisorStateBindings Bind(ParkAdvisor target, bool original)
        {
            var b = new AdvisorStateBindings();
            b.Add("catalogue", catalogue); b.Add("rules", rules); b.Add("clock", clock); b.Add("producers", prod);
            // This delegate's identity is saved, NOT the day value. Root rebinds to its restored clock.
            b.Add("advisor-day", original ? a.Scheduler.DayBinding : (Func<uint>)(() => unchecked((uint)clock.TotalDays)));
            b.Add("sound", sound); b.Add("played", played); b.Add("length", length); b.Add("lips", lips); b.Add("track", lip);
            b.Add("ride", original ? ride : restoredRide);
            b.Add("stack-tutorial", original ? a.Stack.TutorialEvent : (Action<int>)(n => target.TutorialEvent(n)));
            b.Add("stack-replay", original ? a.Stack.Replay : (Action<short>)(n => target.TutorialMessage(n)));
            return b;
        }
        var ab = Bind(a, true);
        void RoundTrip(string phase)
        {
            var s = Copy(a.CaptureState(ab, true)); string source = Json(s); int before = effects;
            var clone = ParkAdvisor.AllocateShell(); var cb = Bind(clone, false); clone.Hydrate(s, cb);
            Check(before == effects, phase + " no callbacks replay");
            Check(source == Json(clone.CaptureState(cb, true)), phase + " exact state");
            Reject(() => ParkAdvisor.FromState(s with { RingHead = 20 }, cb), "ring bounds");
            Reject(() => ParkAdvisor.FromState(s with { PendingObjectId = "unknown" }, cb), "unknown reference");
            Reject(() => ParkAdvisor.FromState(s with { SpeechLengthId = "ride" }, cb), "typed binding");
            Check(before == effects && source == Json(a.CaptureState(ab, true)), phase + " malformed no source effects");
            // Run both forks, including dedup/overflow, immediate interruption and future variant rotation.
            for (int i = 0; i < 220; i++)
            {
                if (i == 10) { a.Submit(7, ride); clone.Submit(7, restoredRide); }
                if (i == 80) { a.Submit(209); clone.Submit(209); }
                a.Update(); clone.Update();
                Check(Json(a.CaptureState(ab, true)) == Json(clone.CaptureState(cb, true)), phase + " continuation " + i);
            }
        }
        RoundTrip("start delay");
        for (int i = 0; i < 24; i++) a.Submit(i, ride);
        Check(a.RingCount == 19 && a.Overflows > 0, "full ring");
        a.Stack.AddGoalNotice("persist this goal text"); a.Stack.Add(AdvisorRecordType.Object, 8, ride);
        a.Stack.Open(); a.Stack.Press(AdvisorStackButtons.Next | AdvisorStackButtons.Select);
        RoundTrip("full ring and queued input/text");
        for (int i = 0; i < 1000 && a.State != AdvisorState.Cooldown; i++) a.Update();
        Check(a.State == AdvisorState.Cooldown, "cooldown reached"); RoundTrip("cooldown");
        a.Submit(208);
        for (int i = 0; i < 1000 && !a.Speaking; i++) a.Update();
        // The synthetic catalogue voices every record; a supplied catalogue may make 208 silent.
        if (a.Speaking) { a.Update(); Check(a.SpeechElapsedMs > 0, "voice progress"); RoundTrip("voiced progress/lips"); }
        a.EnterRideAlong(); RoundTrip("saved flags/ride along");
        a.ExitRideAlong(); a.Stack.MarkForRemoval(0); RoundTrip("pending removal");
        bool guarded = false; try { a.CaptureState(ab, false); } catch (InvalidOperationException) { guarded = true; }
        Check(guarded, "quiescence guard");
        var head = new AdvisorTimedHead(); head.Play(0, 1); head.Step(120); head.Play(14, 0);
        var headClone = new AdvisorTimedHead(); headClone.Channel.RestoreState(Copy(head.Channel.CaptureState()));
        for (int i = 0; i < 100; i++)
        {
            head.Step(40); headClone.Step(40);
            Check(Json(head.Channel.CaptureState()) == Json(headClone.Channel.CaptureState()), "queued head continuation");
        }
        bool busyGuard = false;
        var oldPlayed = a.Played;
        a.Played = _ => { try { a.CaptureState(ab, true); } catch (InvalidOperationException) { busyGuard = true; } };
        a.Submit(208);
        for (int i = 0; i < 1000 && !busyGuard; i++) a.Update();
        a.Played = oldPlayed;
        Check(busyGuard, "capture from update callback rejected");
        return checks;
    }
    // Minimal synthetic ELF accepted by the real immutable catalogue loader. No reflection or private data.
    static AdvisorCatalogue SyntheticCatalogue()
    {
        var data = new byte[0x1c0000]; const uint address = 0x100000, offset = 0x100;
        void U32(int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), v);
        void U16(int at, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at), v);
        int Map(uint v) => checked((int)(v - address + offset));
        data[0] = 127; data[1] = 69; data[2] = 76; data[3] = 70; data[4] = data[5] = 1;
        U32(28, 52); U16(42, 32); U16(44, 1); U32(52, 1); U32(56, offset); U32(60, address); U32(68, (uint)data.Length - offset);
        foreach (var (at, value) in new[] { (0x106420u, 0x2a020113u), (0x107918u, 0x24040038u), (0x107928u, 0x24426ac8u), (0x263934u, 0x2610ffffu) }) U32(Map(at), value);
        data[Map(0x2b0000)] = (byte)'X';
        for (int i = 0; i < AdvisorCatalogue.MessageCount; i++)
        {
            int at = Map(AdvisorCatalogue.TableAddress) + i * 56;
            U32(at, 0x2b0000); U16(at + 4, (ushort)(i + 1)); data[at + 6] = 4;
            for (int v = 0; v < 4; v++) U16(at + 8 + v * 12, (ushort)(v + 1));
        }
        return new AdvisorCatalogue(data);
    }
}
