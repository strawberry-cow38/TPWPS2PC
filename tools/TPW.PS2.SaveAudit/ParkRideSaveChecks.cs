using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;

/// <summary>Disc-free ride slice checks; no internal setters or reflection.</summary>
public static class ParkRideSaveChecks
{
    public static void Run(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        string Json(ParkRide.State s) => JsonSerializer.Serialize(s);
        ParkRide.State Read(string s) => JsonSerializer.Deserialize<ParkRide.State>(s)!;
        ParkRide.State Capture(ParkRide r) => r.CaptureState(null!, null!, null!, null!, null!, null!);
        ParkRide Restore(ParkRide.State s) => ParkRide.FromState(s, null!, null!, null!, null!, null!, null!);
        var ride = new ParkRide { Id = 7, Name = "fixture", Width = 3, Height = 2,
            Origin = new(-4, 9), Entrance = new(-5, 9), Exit = new(-1, 10),
            ServiceEntry = new(-4, 10), PlacementTurns = 3, ReliefUsesOccupancy = true };
        string empty = Json(Capture(ride));
        var defaults = Read(empty);
        check(defaults.Speed == null && defaults.Duration == null && defaults.Capacity == null
            && defaults.Life == null && defaults.SalePrice == null && defaults.ValueState == null
            && defaults.SideshowPrice == null && defaults.SideshowPrizeValue == null
            && defaults.SideshowWinPercentage == null, "ParkRide: capture preserves lazy nulls");
        check(Json(Capture(Restore(defaults))) == empty, "ParkRide: null JSON roundtrip");
        ride.Service(17); ride.Wear(23); ride.Book(19, -4); ride.Book(3, 2);
        ride.Join(9); ride.Join(2); ride.Join(9); ride.Join(4); // duplicates, deliberately NOT a palindrome
        ride.Speed = 0; ride.Duration = -7; ride.Capacity = 13; ride.SalePrice = 0;
        ride.SideshowPrizeValue = -9; ride.SideshowPrice = ushort.MaxValue; ride.SideshowWinPercentage = 0;
        ride.Quality = 83; ride.Setting0xAC = 41; ride.Setting0xC0 = 123;
        ride.ScreamHandle = 66; ride.ScreamLevel = 51; ride.CachedTrackWeight = 255;
        ride.CurrentTier = 2; ride.DestinationState = 10; ride.CoasterTrackClosed = true;
        ride.ForceReliabilityForTest(12345);
        string saved = Json(Capture(ride));
        var input = Read(saved);
        var restored = Restore(input);
        check(Json(Capture(restored)) == saved, "ParkRide: queues/settings/private totals JSON roundtrip");
        check(restored.Condition == 77 && restored.LastCleanedDay == 17 && restored.Takings == 22
            && restored.Profit == -2 && restored.Customers == 2, "ParkRide: private API-produced state");
        input.Queue[0] = 88;
        check(Json(Capture(restored)) == saved && Json(Capture(ride)) == saved, "ParkRide: restore arrays detached");
        var detached = Capture(restored); detached.Queue[1] = 88;
        check(Json(Capture(restored)) == saved, "ParkRide: capture arrays detached");

        // Stage inaccessible fields through the public DTO, without gameplay transitions on restore.
        var node = JsonNode.Parse(saved)!.AsObject();
        node["Life"] = -2; node["ServiceFlag"] = true; node["UpgradePending"] = true; node["NativeRiders"] = 6;
        node["Left"] = new JsonArray(8, 3, 8, 1); node["Ejected"] = new JsonArray(5, 1, 5, 2);
        node["Variables"] = new JsonArray("VAR_BROKEN", "VAR_ONRIDE");
        node["ValueState"] = JsonSerializer.SerializeToNode(new ParkRide.ValueState { Speed = 71, Duration = 6,
            CachedTrackWeight = 17, SideshowPrizeValue = -1, SideshowPrice = 9, SideshowWinPercentage = 27 });
        node["Speed"] = null; node["Duration"] = null; node["SideshowPrice"] = null;
        var stagedInput = Read(node.ToJsonString());
        var staged = Restore(stagedInput);
        check(Json(Capture(staged)) == Json(stagedInput) && staged.Life == -2 && staged.ServiceFlag
            && staged.UpgradePending && staged.NativeRiders == 6, "ParkRide: private flags/life/lazy cache preserved");
        check(staged.Speed == 71 && staged.Duration == 6 && staged.SideshowPrice == 9,
            "ParkRide: cached defaults remain distinct from nullable overrides");
        stagedInput.Left[0] = 100; stagedInput.Ejected[0] = 100; stagedInput.Variables[0] = "changed";
        check(staged.Left.SequenceEqual(new[] { 8, 3, 8, 1 }) && staged.Ejected.SequenceEqual(new[] { 5, 1, 5, 2 })
            && staged.Has("VAR_BROKEN"), "ParkRide: all ordered lists and variable set detached");

        void Reject(Action<JsonObject> edit, string why)
        {
            var bad = JsonNode.Parse(saved)!.AsObject(); edit(bad);
            bool rejected = false;
            try { Restore(Read(bad.ToJsonString())); }
            catch (ArgumentException) { rejected = true; }
            catch (JsonException) { rejected = true; }
            check(rejected && Json(Capture(ride)) == saved, "ParkRide rejects without source mutation: " + why);
        }
        foreach (string member in JsonNode.Parse(saved)!.AsObject().Select(p => p.Key))
            Reject(n => n.Remove(member), "missing required " + member);
        Reject(n => n["Version"] = 999, "version");
        Reject(n => n.Remove("Speed"), "missing required nullable override");
        Reject(n => n["Unknown"] = 0, "unknown schema member");
        Reject(n => n["Width"] = 0, "zero width"); Reject(n => n["Height"] = -1, "negative height");
        Reject(n => n["Origin"]!["X"] = int.MaxValue, "footprint overflow");
        Reject(n => n["Origin"]!.AsObject().Remove("Z"), "missing cell coordinate");
        Reject(n => n["PlacementTurns"] = 4, "rotation"); Reject(n => n["CurrentTier"] = 3, "tier");
        Reject(n => n["Life"] = 32768, "life narrowing"); Reject(n => n["Queue"] = null, "null queue");
        Reject(n => n["Variables"] = new JsonArray("x", "x"), "duplicate variable");
        Reject(n => n["Variables"] = new JsonArray((JsonNode?)null), "null variable");
        foreach (string key in new[] { "DefinitionKey", "MachineId", "HostId", "TrackId", "CoasterId", "AssignedMechanicId" })
            Reject(n => n[key] = "missing", "unresolved " + key);
        Reject(n => n["ValueState"] = new JsonObject { ["Speed"] = 5 }, "incomplete lazy cache");
        var definition = new RideDefinition(); var host = new RsePreviewHost(null);
        var linked = new ParkRide { Width = 1, Height = 1, Definition = definition, Host = host };
        var links = linked.CaptureState("fixture/definition", null!, "host/1", null!, null!, null!);
        var linkedCopy = ParkRide.FromState(Read(Json(links)), definition, null!, host, null!, null!, null!);
        check(ReferenceEquals(linkedCopy.Definition, definition) && ReferenceEquals(linkedCopy.Host, host),
            "ParkRide: resolved references retained, assets not embedded");
        bool mismatch = false;
        try { Capture(linked); } catch (ArgumentException) { mismatch = true; }
        check(mismatch, "ParkRide: capture fails closed on missing external IDs");
        mismatch = false;
        try { ParkRide.FromState(defaults, definition, null!, null!, null!, null!, null!); }
        catch (ArgumentException) { mismatch = true; }
        check(mismatch, "ParkRide: restore rejects unexpected reference for null ID");
        var missingStaff = JsonNode.Parse(saved)!.AsObject(); missingStaff["AssignedMechanicId"] = "staff/1";
        int resolutions = 0; mismatch = false;
        try { ParkRide.FromState(Read(missingStaff.ToJsonString()), null!, null!, null!, null!, null!,
            id => { resolutions++; return null!; }); }
        catch (ArgumentException) { mismatch = true; }
        check(mismatch && resolutions == 1, "ParkRide: unresolved staff fails closed");
    }
}
