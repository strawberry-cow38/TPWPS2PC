using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;

/// <summary>Disc-free seeded .NET 8 compatibility and detached-state checks.</summary>
public static class SnapshotRandomChecks
{
    public static void Run(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        string Json(SnapshotRandom.State state) => JsonSerializer.Serialize(state);
        SnapshotRandom.State RoundTrip(SnapshotRandom.State state) =>
            JsonSerializer.Deserialize<SnapshotRandom.State>(Json(state))!;

        // Exercise zero-width calls too: unlike empty byte buffers, they consume a draw.
        void Mixed(Random expected, SnapshotRandom actual, int rounds, string label)
        {
            for (int i = 0; i < rounds; i++)
            {
                string name = $"{label}, round {i}: ";
                check(expected.Next() == actual.Next(), name + "Next");
                check(expected.Next(0) == actual.Next(0), name + "Next(0)");
                check(expected.Next(1) == actual.Next(1), name + "Next(1)");
                check(expected.Next(37) == actual.Next(37), name + "Next(37)");
                check(expected.Next(int.MaxValue) == actual.Next(int.MaxValue), name + "Next(max)");
                check(expected.Next(-91, 113) == actual.Next(-91, 113), name + "signed small range");
                check(expected.Next(-8, -8) == actual.Next(-8, -8), name + "equal negative bounds");
                check(expected.Next(int.MinValue, int.MinValue) == actual.Next(int.MinValue, int.MinValue), name + "equal minimum bounds");
                check(expected.Next(int.MaxValue, int.MaxValue) == actual.Next(int.MaxValue, int.MaxValue), name + "equal maximum bounds");
                check(expected.Next(int.MinValue, -1) == actual.Next(int.MinValue, -1), name + "Int32.MaxValue range");
                check(expected.Next(int.MinValue, 0) == actual.Next(int.MinValue, 0), name + "first large range");
                check(expected.Next(int.MinValue, int.MaxValue) == actual.Next(int.MinValue, int.MaxValue), name + "full signed range");
                check(expected.Next(-1, int.MaxValue) == actual.Next(-1, int.MaxValue), name + "large asymmetric range");
                check(BitConverter.DoubleToInt64Bits(expected.NextDouble()) ==
                    BitConverter.DoubleToInt64Bits(actual.NextDouble()), name + "NextDouble exact bits");
                int length = i % 67; // Empty, short, and longer than the subtractive ring.
                var a = new byte[length];
                var b = new byte[length];
                expected.NextBytes(a);
                actual.NextBytes(b);
                check(a.AsSpan().SequenceEqual(b), name + "NextBytes array");
                a = Enumerable.Repeat((byte)0xA5, length + 4).ToArray();
                b = (byte[])a.Clone();
                expected.NextBytes(a.AsSpan(2, length));
                actual.NextBytes(b.AsSpan(2, length));
                check(a.AsSpan().SequenceEqual(b), name + "NextBytes sliced span and guards");
                check(expected.Next() == actual.Next(), name + "next draw after bytes");
            }
        }

        foreach (int seed in new[] { 0, 1, 7, 11, 0x5747, int.MinValue, int.MaxValue })
        {
            var expected = new Random(seed);
            var actual = new SnapshotRandom(seed);
            // Initial sentinel cursors and post-wrap cursors both survive JSON.
            var initial = RoundTrip(actual.CaptureState());
            actual = SnapshotRandom.FromState(initial);
            check(Json(initial) == Json(actual.CaptureState()), $"seed {seed}: initial JSON");
            Mixed(expected, actual, 256, $"seed {seed}");
            var saved = RoundTrip(actual.CaptureState());
            var restored = new SnapshotRandom(9876);
            restored.NextBytes(new byte[93]);
            restored.RestoreState(saved);
            var clone = SnapshotRandom.FromState(saved);
            check(Json(saved) == Json(restored.CaptureState()), $"seed {seed}: restored JSON");
            Mixed(expected, restored, 128, $"seed {seed}, restored continuation");
            // Original and independent clone must produce exactly that same continuation.
            var replay = new Random(seed);
            var warmup = new SnapshotRandom(seed);
            Mixed(replay, warmup, 256, $"seed {seed}, reference warmup");
            Mixed(replay, clone, 128, $"seed {seed}, clone continuation");
            check(Json(restored.CaptureState()) == Json(clone.CaptureState()), $"seed {seed}: identical final states");
            check(Json(saved) == Json(actual.CaptureState()), $"seed {seed}: clone never advances original");
        }

        var source = new SnapshotRandom(11);
        source.NextBytes(new byte[81]);
        var captured = source.CaptureState();
        var independent = SnapshotRandom.FromState(captured);
        string sourceBefore = Json(source.CaptureState());
        captured.SeedArray[1] ^= 1;
        check(sourceBefore == Json(source.CaptureState()), "capture array detached from live generator");
        check(sourceBefore == Json(independent.CaptureState()), "FromState copies input array");
        var input = RoundTrip(source.CaptureState());
        independent.RestoreState(input);
        input.SeedArray[55] ^= 1;
        check(sourceBefore == Json(independent.CaptureState()), "RestoreState copies input array");
        independent.Next();
        check(sourceBefore == Json(source.CaptureState()), "advancing clone leaves source alone");
        string cloneBefore = Json(independent.CaptureState());
        source.Next();
        source.Next();
        check(cloneBefore == Json(independent.CaptureState()), "advancing source leaves clone alone");

        void Reject(SnapshotRandom.State bad, string name)
        {
            string before = Json(source.CaptureState());
            bool rejected = false;
            try { source.RestoreState(bad); }
            catch (ArgumentException) { rejected = true; }
            check(rejected, name + ": rejected");
            check(before == Json(source.CaptureState()), name + ": no partial mutation");
            rejected = false;
            try { SnapshotRandom.FromState(bad); }
            catch (ArgumentException) { rejected = true; }
            check(rejected, name + ": FromState rejected");
        }
        Reject(null!, "null state");
        void BadMember(string key, JsonNode? value, string name)
        {
            var node = JsonNode.Parse(sourceBefore)!.AsObject();
            node[key] = value;
            Reject(JsonSerializer.Deserialize<SnapshotRandom.State>(node.ToJsonString())!, name);
        }
        BadMember("Version", 0, "old version");
        BadMember("Version", 2, "future version");
        BadMember("SeedArray", null, "null array");
        BadMember("SeedArray", new JsonArray(), "empty array");
        BadMember("SeedArray", JsonSerializer.SerializeToNode(new int[55]), "short array");
        BadMember("SeedArray", JsonSerializer.SerializeToNode(new int[57]), "long array");
        foreach (int cursor in new[] { -1, 56, int.MinValue, int.MaxValue })
        {
            BadMember("Inext", cursor, "invalid Inext " + cursor);
            BadMember("Inextp", cursor, "invalid Inextp " + cursor);
        }
        BadMember("Inextp", 0, "zero Inextp");
        BadMember("Inext", 0, "sentinel with wrong paired cursor");
        BadMember("Inextp", 1, "inconsistent cursor separation");
        foreach (var (index, value) in new[] { (0, 1), (1, -1), (55, -1), (55, int.MaxValue) })
        {
            var bad = RoundTrip(source.CaptureState());
            bad.SeedArray[index] = value;
            Reject(bad, $"array[{index}] = {value}");
        }
        var original = JsonNode.Parse(sourceBefore)!.AsObject();
        foreach (string key in original.Select(p => p.Key).ToArray())
        {
            var missing = original.DeepClone().AsObject();
            missing.Remove(key);
            bool rejected = false;
            try { JsonSerializer.Deserialize<SnapshotRandom.State>(missing.ToJsonString()); }
            catch (JsonException) { rejected = true; }
            check(rejected, "required JSON member " + key);
        }

        void InvalidCall<T>(Action call, string name) where T : ArgumentException
        {
            string before = Json(source.CaptureState());
            bool rejected = false;
            try { call(); }
            catch (T) { rejected = true; }
            check(rejected, name + ": exception type");
            check(before == Json(source.CaptureState()), name + ": no draw");
        }
        InvalidCall<ArgumentOutOfRangeException>(() => source.Next(-1), "negative max");
        InvalidCall<ArgumentOutOfRangeException>(() => source.Next(int.MinValue), "minimum max");
        InvalidCall<ArgumentOutOfRangeException>(() => source.Next(1, -1), "reversed bounds");
        InvalidCall<ArgumentNullException>(() => source.NextBytes((byte[])null!), "null byte array");
        string emptyBefore = Json(source.CaptureState());
        source.NextBytes(Array.Empty<byte>());
        source.NextBytes(Span<byte>.Empty);
        check(emptyBefore == Json(source.CaptureState()), "empty array and span consume no draws");
    }
}
