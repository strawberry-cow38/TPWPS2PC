using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;

/// <summary>Disc-backed PathTool owner audit; no extraction, Viewer, or whole-park claim.</summary>
public static class PathToolSaveChecks
{
    public static void Run(Disc disc, Action<bool, string> check)
    {
        const string terrainKey = "JUNGLE/terrain/terrain_1.mps", piecesKey = "SLES_500.32/path-pieces";
        void C(bool ok, string why) => check(ok, "PathTool snapshot: " + why);
        string Json<T>(T value) => JsonSerializer.Serialize(value);
        T Copy<T>(T value) => JsonSerializer.Deserialize<T>(Json(value))!;
        var file = disc.Files().Single(f => f.Path == "/DATA/JUNGLE.WAD");
        var wad = new WadArchive(disc.Read(file.Extent, file.Size));
        var terrain = new Model(wad.Read(wad.Find("/terrain/terrain_1.mps")!));
        // The existing reader obtains the authoritative tables from the executable, NOT UI.WAD.
        var pieces = PathPieces.Read(disc);
        var original = new PathTool(terrain, pieces);
        C(original.Ready, "disc tables and real terrain bound");
        var field = terrain.Field;
        var grass = field.Cells.ToArray();
        var cold = original.CaptureState(terrainKey, piecesKey);
        C(Json(cold) == Json(PathTool.FromState(Copy(cold), terrainKey, piecesKey, terrain, pieces,
            new Model.HeightField { Width = field.Width, Height = field.Height, Cells = grass.ToArray(), Step = field.Step })
            .CaptureState(terrainKey, piecesKey)), "cold empty graph roundtrip");
        var unready = new PathTool(terrain, null);
        var unreadyState = unready.CaptureState(terrainKey, piecesKey);
        C(!PathTool.FromState(unreadyState, terrainKey, piecesKey, terrain, null, field).Ready,
            "missing pieces binding remains unready");
        PathTool.State Save(PathTool t) => t.CaptureState(terrainKey, piecesKey);
        Model.HeightField Fresh() => new() { Width = field.Width, Height = field.Height,
            Step = field.Step, Cells = field.Cells.ToArray() };
        PathTool Restore(PathTool.State s, Model.HeightField f) =>
            PathTool.FromState(s, terrainKey, piecesKey, terrain, pieces, f);
        var head = (from cy in Enumerable.Range(2, field.Height - 8)
                    from cx in Enumerable.Range(2, field.Width - 8)
                    where Enumerable.Range(0, 6).All(dx => Enumerable.Range(0, 6)
                        .All(dy => original.CanLay(cx + dx, cy + dy))) select (X: cx, Y: cy)).First();
        int x = head.X, y = head.Y;
        int At(int dx, int dy) => (y + dy) * field.Width + x + dx;
        original.SetWalkway(new[] { (x + 5, y + 5) });
        original.AddDoor(x, y, 41, true, 1, 0);
        original.AddDoor(x, y + 2, 41, false, 1, 0);
        original.BeginLeg(); original.Lay(x + 3, y + 1); original.Lay(x + 4, y + 1);
        original.BeginLeg();
        original.Lay(x + 1, y, PathTool.Kind.Queue, 41, PathPieces.East);
        original.Lay(x + 2, y, PathTool.Kind.Queue, 41, PathPieces.West | PathPieces.South);
        original.BeginLeg();
        original.Lay(x + 2, y + 1, PathTool.Kind.Queue, 41, PathPieces.North | PathPieces.East);
        original.Lay(x + 3, y + 1, PathTool.Kind.Queue, 41, PathPieces.West);
        original.Lay(x + 1, y + 3, PathTool.Kind.Queue, 42, PathPieces.East);
        var state = Copy(Save(original));
        C(state.Kinds.Contains(PathTool.Kind.Both) && state.Turns.Any(t => t != 0)
            && state.Owners.Contains(42) && state.Doors.Length == 2 && state.Walkway.Length == 1,
            "real multi-leg corner/owner/Both/path/door/walkway fixture");
        var aEvents = new List<string>(); var bEvents = new List<string>();
        void Bind(PathTool t, List<string> events)
        {
            t.KindChanged += (cx, cy) => events.Add($"kind:{cx},{cy}");
            t.GroundUnknown += (cx, cy) => events.Add($"ground:{cx},{cy}");
        }
        Bind(original, aEvents);
        var shared = Fresh(); var bytes = shared.Cells.ToArray();
        var restored = Restore(state, shared);
        C(Json(state) == Json(Save(restored)) && bytes.SequenceEqual(shared.Cells) && aEvents.Count == 0,
            "fresh restore copies full state without repick, field writes or source events");
        Bind(restored, bEvents);
        void Both(Action<PathTool> action, string why)
        {
            action(original); action(restored);
            C(Json(Save(original)) == Json(Save(restored)) && field.Cells.SequenceEqual(shared.Cells)
                && aEvents.SequenceEqual(bEvents), why + ": all state, shared grid and event order");
        }
        // Recording without BeginLeg must append to the same active object in the undo stack.
        Both(t => t.Lay(x + 4, y + 2), "continue active leg");
        C(Save(restored).LegObjects[^1].Any(w => w.At == At(4, 2)), "active alias records into undo object");
        Both(t => C(!t.TearUp(x + 1, y) && !t.TearUp(x + 3, y + 1), "standalone queue/Both deletion refused"), "refused deletion");
        Both(t => C(t.UndoLeg(), "undo succeeds"), "undo actual latest leg");
        C(shared.Cells[At(4, 2) * 2 + 1] == grass[At(4, 2) * 2 + 1]
            && Save(restored).ActiveLegId == -1 && restored.LegCount == 2, "undo returns original grass; active null with older legs");
        // Cold restore of a null active leg must not select the last remaining stack entry.
        restored = Restore(Copy(Save(original)), shared); Bind(restored, bEvents);
        Both(t => t.Lay(x + 4, y + 2), "lay with null active leg");
        Both(t => { t.BeginLeg(); t.Lay(x + 2, y + 1, PathTool.Kind.Queue, 41, PathPieces.North | PathPieces.East);
            t.Lay(x + 3, y + 1, PathTool.Kind.Queue, 41, PathPieces.West);
            t.Lay(x + 1, y + 3, PathTool.Kind.Queue, 42, PathPieces.East); }, "new queue leg");
        Both(t => C(t.ClearQueue(99) == 0, "unrelated ride cannot clear queue"), "wrong owner");
        Both(t => C(t.ClearQueue(41) == 3, "ride clears its own three queue cells"), "owner deletion");
        C(restored.KindAt(x + 3, y + 1) == PathTool.Kind.Path && restored.OwnerAt(x + 1, y + 3) == 42
            && shared.Cells[At(1, 0) * 2 + 1] == grass[At(1, 0) * 2 + 1], "clear restores grass, preserves other ride and demotes Both");
        Both(t => t.RemoveDoors(41), "remove ride doors");
        Both(t => { C(t.TearUp(x + 4, y + 2), "plain path deleted");
            t.RepickAfterTear(new[] { (x + 4, y + 2) }); }, "plain path tear");
        C(shared.Cells[At(4, 2) * 2 + 1] == grass[At(4, 2) * 2 + 1], "plain path deletion returns original grass");
        // Current ClearQueue removes Both's before record. Preserve that behavior, including its
        // GroundUnknown event, rather than silently fixing gameplay in the snapshot implementation.
        Both(t => t.TearUp(x + 3, y + 1), "demoted Both ground-unknown continuation");
        C(bEvents.Any(e => e.StartsWith("ground:")), "GroundUnknown event rebound by caller");
        Both(t => t.UndoLeg(), "undo after delete");
        Both(t => t.Undo(), "full Undo");
        C(aEvents.Count > 0, "continuation exercised actual events");

        // Identity is observable in both directions, without exposing a private field.
        var external = Fresh(); var identity = Restore(state, external);
        external.Cells[At(4, 4) * 2] |= 1;
        C(!identity.CanLay(x + 4, y + 4) && original.CanLay(x + 4, y + 4), "external shared field mutation observed, no clone");
        external.Cells[At(4, 4) * 2] &= 0xfe;
        identity.Lay(x + 4, y + 4);
        C(external.Cells[At(4, 4) * 2 + 1] != grass[At(4, 4) * 2 + 1], "restored lay writes parent's exact field");

        // Artificial graph states cover references the public actions cannot currently produce.
        var earlier = state with { ActiveLegId = 0 };
        var earlierTool = Restore(earlier, Fresh()); earlierTool.Lay(x + 4, y + 4);
        C(Save(earlierTool).LegObjects[0].Any(w => w.At == At(4, 4))
            && !Save(earlierTool).LegObjects[^1].Any(w => w.At == At(4, 4)), "active reference can alias earlier leg");
        var unlisted = state with { UndoLegIds = state.UndoLegIds[..^1] };
        var unlistedTool = Restore(unlisted, Fresh());
        C(Json(unlisted) == Json(Save(unlistedTool)), "nonnull unlisted active leg preserved");
        unlistedTool.UndoLeg(); unlistedTool.Lay(x + 4, y + 4);
        var u = Save(unlistedTool);
        C(u.ActiveLegId >= 0 && !u.UndoLegIds.Contains(u.ActiveLegId)
            && u.LegObjects[u.ActiveLegId].Any(w => w.At == At(4, 4)), "unlisted active remains live across unrelated undo");

        var dto = Copy(state); var detached = Restore(dto, Fresh()); var stable = Json(Save(detached));
        dto.Kinds[0] = PathTool.Kind.Both; dto.Turns[0] = 3; dto.Owners[0] = 99; dto.Runs[0] = 255;
        dto.Walkway[0] = 0; if (dto.Bridge.Length > 0) dto.Bridge[0] = 0;
        dto.PathSprites[0] = 0; dto.QueueSprites[0] = 0; dto.MaterialKeys[0] = "bad";
        dto.Doors[0] = dto.Doors[0] with { Ride = 99 };
        dto.OriginalTilesBefore[0] = dto.OriginalTilesBefore[0] with { Tile = 255 };
        dto.LegObjects[0][0] = dto.LegObjects[0][0] with { At = 0 }; dto.UndoLegIds[0] = 1;
        C(stable == Json(Save(detached)), "restore owns every mutable array and leg object");
        var capture = Save(detached); capture.Kinds[0] = PathTool.Kind.Both;
        capture.LegObjects[0][0] = capture.LegObjects[0][0] with { At = 0 };
        C(stable == Json(Save(detached)), "capture arrays deeply detached");
        void Reject(PathTool.State bad)
        {
            var target = Fresh(); string sourceBefore = Json(Save(original)); var targetBefore = target.Cells.ToArray(); var sourceBytes = field.Cells.ToArray();
            bool rejected = false;
            try { Restore(bad, target); } catch (ArgumentException) { rejected = true; }
            C(rejected && sourceBefore == Json(Save(original)) && targetBefore.SequenceEqual(target.Cells) && sourceBytes.SequenceEqual(field.Cells),
                "corruption rejected without source/target field mutation");
        }
        Reject(state with { Version = 2 }); Reject(state with { TerrainKey = "wrong" });
        Reject(state with { PathPiecesKey = "wrong" }); Reject(state with { Width = int.MaxValue });
        Reject(state with { Kinds = state.Kinds[..^1] });
        var kinds = state.Kinds.ToArray(); kinds[0] = (PathTool.Kind)3; Reject(state with { Kinds = kinds });
        var runs = state.Runs.ToArray(); runs[0] = 256; Reject(state with { Runs = runs });
        var turns = state.Turns.ToArray(); turns[0] = 4; Reject(state with { Turns = turns });
        Reject(state with { Walkway = new[] { -1 } }); Reject(state with { Bridge = new[] { 0, 0 } });
        Reject(state with { Doors = new[] { state.Doors[0], state.Doors[0] } });
        Reject(state with { OriginalTilesBefore = new[] { state.OriginalTilesBefore[0], state.OriginalTilesBefore[0] } });
        Reject(state with { ActiveLegId = state.LegObjects.Length }); Reject(state with { ActiveLegId = -2 });
        Reject(state with { UndoLegIds = new[] { 0, 0 } }); Reject(state with { UndoLegIds = new[] { -1 } });
        Reject(state with { UndoLegIds = Array.Empty<int>(), ActiveLegId = -1 });
        var legs = state.LegObjects.ToArray(); legs[0] = new[] { legs[0][0], legs[0][0] };
        Reject(state with { LegObjects = legs }); Reject(state with { QueueSprites = new[] { 0, 1, 2, 3 } });
        Reject(state with { HasPieces = false }); Reject(state with { Turns = null! });
        Reject(state with { MaterialKeys = new[] { "wrong-table" } });
        Reject(state with { Doors = new[] { state.Doors[0] with { Dx = 2 } } });
        Reject(state with { LegObjects = new PathTool.CellState[][] { null! } });
        Reject(state with { PathSprites = null! });
        var wrongGrid = Fresh(); wrongGrid.Width--; var wrongBytes = wrongGrid.Cells.ToArray();
        bool wrongGridRejected = false;
        try { Restore(state, wrongGrid); } catch (ArgumentException) { wrongGridRejected = true; }
        C(wrongGridRejected && wrongBytes.SequenceEqual(wrongGrid.Cells), "wrong staged field rejected untouched");
        void Schema<T>(T value)
        {
            var obj = JsonNode.Parse(Json(value))!.AsObject();
            foreach (var key in obj.Select(p => p.Key).ToArray())
            {
                var missing = obj.DeepClone().AsObject(); missing.Remove(key); bool rejected = false;
                try { JsonSerializer.Deserialize<T>(missing.ToJsonString()); } catch (JsonException) { rejected = true; }
                C(rejected, "required schema member " + typeof(T).Name + "." + key);
            }
            obj["Unexpected"] = 1; bool unknown = false;
            try { JsonSerializer.Deserialize<T>(obj.ToJsonString()); } catch (JsonException) { unknown = true; }
            C(unknown, "unknown member rejected " + typeof(T).Name);
        }
        Schema(state); Schema(state.Doors[0]); Schema(state.OriginalTilesBefore[0]); Schema(state.LegObjects[0][0]);
    }
}
