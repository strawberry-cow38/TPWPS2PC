using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;

/// <summary>Optional disc-backed owner audit. Assets stay in RAM; no UI or whole-park claim.</summary>
public static class ParkPathsSaveChecks
{
    public static void Run(Disc disc, Action<bool, string> check)
    {
        string Json<T>(T value) => JsonSerializer.Serialize(value);
        var table = ParkEntrance.Read(disc);
        foreach (string world in new[] { "JUNGLE", "FANTASY", "HALLOW", "SPACE" })
        {
            var file = disc.Files().Single(f => f.Path == "/DATA/" + world + ".WAD");
            var wad = new WadArchive(disc.Read(file.Extent, file.Size));
            for (int number = 1; number <= 2; number++)
            {
                string path = $"/terrain/terrain_{number}.mps", key = world + path;
                void C(bool ok, string why) => check(ok, key + ": " + why);
                var terrain = new Model(wad.Read(wad.Find(path) ?? throw new InvalidDataException(path)));
                var assetCells = (byte[])terrain.Field.Cells.Clone();
                var source = new ParkPaths(terrain);
                C(Json(source.CaptureState(key)) == Json(ParkPaths.FromState(source.CaptureState(key), key, terrain).CaptureState(key)),
                    "pre-entrance state round trip");
                source.SetEntrance(table);
                int material = Enumerable.Range(1, source.Materials.Count - 1)
                    .First(i => ParkPaths.Classify(source.Materials[i]) == ParkPathKind.Path);
                var head = source.Cells.First(c => Enumerable.Range(0, 5).All(x => source.CanBuild(c.Offset(x, 0))));
                var line = Enumerable.Range(0, 5).Select(x => head.Offset(x, 0)).ToArray();
                foreach (var c in line.Take(4)) source.Lay(c, material);
                var occupied = source.Cells.Where(c => !line.Contains(c) && source.CanBuild(c)
                    && source.Kind(c) == ParkPathKind.None).Take(3).Reverse().ToArray();
                source.Occupy(occupied);
                source.Field.Cells[(head.Z * source.Field.Width + head.X) * 2] ^= 0x20;
                source.Field.Step = 3.5f;
                var captured = source.CaptureState(key);
                var dto = JsonSerializer.Deserialize<ParkPaths.State>(Json(captured))!;
                var restored = ParkPaths.FromState(dto, key, terrain);
                foreach (string member in JsonNode.Parse(Json(captured))!.AsObject().Select(p => p.Key).ToArray())
                {
                    var missing = JsonNode.Parse(Json(captured))!.AsObject(); missing.Remove(member); bool rejected = false;
                    try { JsonSerializer.Deserialize<ParkPaths.State>(missing.ToJsonString()); } catch (JsonException) { rejected = true; }
                    C(rejected, "schema requires " + member);
                }
                bool unknownRejected = false;
                try { JsonSerializer.Deserialize<ParkPaths.State>(Json(captured).Insert(1, "\"Unknown\":0,")); }
                catch (JsonException) { unknownRejected = true; }
                C(unknownRejected, "unknown schema member rejected");
                C(Json(captured) == Json(restored.CaptureState(key)), "every logical field/order JSON round trip");
                C(!ReferenceEquals(source.Field, restored.Field) && !ReferenceEquals(source.Field.Cells, captured.Cells)
                    && !ReferenceEquals(dto.Cells, restored.Field.Cells) && !ReferenceEquals(terrain.Field, restored.Field), "fresh field and byte ownership");
                C(source.Origin == restored.Origin && source.Materials.SequenceEqual(restored.Materials), "asset bindings/origin");
                C(source.EntranceCells.SequenceEqual(restored.EntranceCells) && source.Protected.SequenceEqual(restored.Protected)
                    && source.GateHold.SequenceEqual(restored.GateHold) && source.EntranceEntry == restored.EntranceEntry,
                    "entrance/protection/gate order and entry");
                C(source.Cells.All(c => source.Walkable(c) == restored.Walkable(c) && source.CanBuild(c) == restored.CanBuild(c)
                    && source.CanLay(c) == restored.CanLay(c) && source.SceneryBlocks(c) == restored.SceneryBlocks(c)
                    && source.EntranceKind(c) == restored.EntranceKind(c) && source.IsProtected(c) == restored.IsProtected(c)
                    && source.GateHolds(c) == restored.GateHolds(c)), "all-cell behavior including occupancy");
                C(occupied.All(c => !restored.CanLay(c)), "occupied cells block new paths");
                C(source.Route(head, line[3])!.SequenceEqual(restored.Route(head, line[3])!), "actual laid path BFS");
                var entrance = source.EntranceCells.ToArray();
                C(entrance.Length > 0 && source.Route(entrance[0], entrance[^1]) is { Count: > 1 } route
                    && route.SequenceEqual(restored.Route(entrance[0], entrance[^1])!), "actual entrance BFS");
                var continued = ParkPaths.FromState(captured, key, terrain);
                source.Lay(line[4], material); continued.Lay(line[4], material);
                C(source.Route(head, line[4])!.SequenceEqual(continued.Route(head, line[4])!)
                    && Json(source.CaptureState(key)) == Json(continued.CaptureState(key)), "post-restore edit and route continuation");
                C(captured.Cells.SequenceEqual(dto.Cells) && restored.Field.Material(line[4].X, line[4].Z)
                    == captured.Cells[(line[4].Z * captured.Width + line[4].X) * 2 + 1], "live edits do not change snapshots/other owner");
                string stable = Json(restored.CaptureState(key)), sourceStable = Json(source.CaptureState(key));
                dto.Cells[0] ^= 0xff; dto.Occupied[0] = new ParkCell(-1, -1);
                dto.Entrance[0] = new ParkCell(-1, -1); dto.EntranceKinds[0] = dto.EntranceKinds[0] with { Kind = 99 };
                dto.Scenery[0] = dto.Protected[0] = dto.GateHold[0] = new ParkCell(-1, -1);
                C(stable == Json(restored.CaptureState(key)) && sourceStable == Json(source.CaptureState(key)),
                    "DTO mutation cannot change restored/source owner");
                var detached = source.CaptureState(key); detached.Cells[0] ^= 0xff; detached.Occupied[0] = new ParkCell(-1, -1);
                C(sourceStable == Json(source.CaptureState(key)), "capture arrays detached from live source");
                void Reject(ParkPaths.State bad, string expected = "")
                {
                    string before = Json(source.CaptureState(key)); bool rejected = false;
                    try { ParkPaths.FromState(bad, expected == "" ? key : expected, terrain); }
                    catch (ArgumentException) { rejected = true; }
                    C(rejected && before == Json(source.CaptureState(key)) && assetCells.SequenceEqual(terrain.Field.Cells),
                        "invalid DTO rejected without source/asset mutation");
                }
                Reject(captured with { Version = 99 }); Reject(captured with { TerrainKey = "wrong" });
                Reject(captured, "independently-wrong"); Reject(captured with { Width = captured.Width + 1 });
                Reject(captured with { CellStride = 3 }); Reject(captured with { Cells = captured.Cells[..^1] });
                Reject(captured with { Occupied = new[] { occupied[0], occupied[0] } });
                Reject(captured with { Protected = new[] { new ParkCell(-1, 0) } });
                Reject(captured with { EntranceKinds = captured.EntranceKinds.Skip(1).ToArray() });
                Reject(captured with { Step = float.NaN });
                C(assetCells.SequenceEqual(terrain.Field.Cells), "all edits/restore leave authored terrain untouched");
            }
        }
    }
}
