using System.Reflection;
using System.Text.Json;
using Godot;

namespace TPWPS2Viewer.Tests;

/// <summary>Disc-free real-engine placement owner test. Optional -- --park-state-file=/path.json.
/// Shared ParkPaths/Field and full Viewer save integration are tested by their parent owner.</summary>
public partial class ParkPlacementSaveSmoke : Node
{
    int _checks;
    void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); _checks++; }
    static Park.State Clone(Park.State s) => JsonSerializer.Deserialize<Park.State>(JsonSerializer.Serialize(s));
    static Park.Footprint Rotate(Park.Footprint f, int n) => (Park.Footprint)typeof(Placement)
        .GetMethod("Rotate", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { f, n });
    static Node3D ModelNode(string key)
    {
        var node = new Node3D { Name = key };
        node.SetMeta("asset_key", key);
        node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One } });
        return node;
    }
    public override void _Ready()
    {
        Park source = null, restored = null;
        try
        {
            source = new Park { Origin = new(2, -13), BaseY = 1.25f,
                GroundMaterial = new StandardMaterial3D(),
                PlotSpace = new Park.Plot(new Transform3D(new Basis(Vector3.Up, 0.7f), new(12, 1, -20)),
                    new(-1, 0, -2), new(12, 1, 10)) };
            AddChild(source.Root);
            var playable = new bool[12, 10];
            for (int y = 0; y < 10; y++) for (int x = 0; x < 12; x++) playable[x, y] = true;
            playable[0, 0] = false;
            source.Build(12, 10, playable);
            source.Reserve(-1, 8, 4, 3);
            var ragged = Park.Footprint.From(new[] { "*2E", "*" });
            var turned = Rotate(ragged, 1); var turned3 = Rotate(ragged, 3);
            var one = Park.Footprint.From(new[] { "2" });
            Check(source.TryPlace(ModelNode("ragged"), turned, 101, "First", 1, 1, 1), "first placement");
            Check(source.TryPlace(ModelNode("ragged"), turned3, 102, "Second", 6, 1, 3), "second placement");
            Check(source.TryPlaceNear(null, one, 103, "Logical only"), "near placement");
            source.SetTerrain(ModelNode("terrain"));
            source.Root.Position = new(3, 2, 1); source.Root.Visible = false;
            source.GroundRoot.Position = new(0, 0.1f, 0); source.ShowGrass = false;
            var first = source.Placed[0].Node;
            // Actual placement-root pose, not a turn count or replay-derived position.
            first.Transform = new Transform3D(new Basis(new(1, 0, 0), new(0.2f, 2, 0), new(0, 0, -1)), new(8, 4, -7));
            first.Visible = false;
            ((Node3D)first.GetParent()).Rotation = new(0, 0.1f, 0);
            source.Placed[1].Node.TopLevel = true;
            var capture = new Park.StateBindings { IdentifyNode = (_, n) => n.GetMeta("asset_key").AsString() };
            var state = source.CaptureState(capture);
            string original = JsonSerializer.Serialize(state);
            Check(original.Contains("\"Width\":12") && original.Contains("\"EntryDX\""), "default JSON properties");
            var round = Clone(state);
            var fileArg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--park-state-file="));
            if (fileArg != null)
            {
                var path = fileArg["--park-state-file=".Length..];
                System.IO.File.WriteAllText(path, original);
                round = JsonSerializer.Deserialize<Park.State>(System.IO.File.ReadAllText(path));
            }
            restored = Park.FromState(round, new Park.StateBindings { BuildNode = ModelNode });
            Check(!restored.Root.IsInsideTree() && restored.Root.GetParent() == null, "unpublished stage");
            Check(restored.GroundRoot.GetChildCount() == 0 && restored.MaterialCount == 0, "no ground replay");
            Check(JsonSerializer.Serialize(restored.CaptureState(capture)) == original, "complete owner JSON roundtrip");
            Check(restored.Placed.Select(p => p.Id).SequenceEqual(new[] { 101, 102, 103 }), "placed order");
            Check(restored.OccupiedCells == 9 && source.OccupiedCells == 9, "ragged occupancy");
            Check(!ReferenceEquals(first, restored.Placed[0].Node) && restored.Placed[0].Node.Transform == first.Transform
                && !restored.Placed[0].Node.Visible, "fresh actual pose/visibility");
            Check(restored.LastX == source.LastX && restored.LastY == source.LastY, "last-near cursor");
            Check(restored.Placed[0].Fp.EntryDX == -ragged.EntryDY && restored.Placed[0].Fp.ExitDY == ragged.ExitDX,
                "rotated door facing");
            for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++)
            {
                Check(restored.PlacedAt(x, y)?.Id == source.PlacedAt(x, y)?.Id, "PlacedAt");
                Check(restored.CanPlace(one, x, y) == source.CanPlace(one, x, y), "CanPlace");
                Check(restored.Reserved(x, y) == source.Reserved(x, y), "reservation");
                Check(restored.IsPlayable(x, y) == source.IsPlayable(x, y) && restored.HasFloor(x, y) == source.HasFloor(x, y), "floor");
                Check(restored.CellCentre(x, y) == source.CellCentre(x, y) && restored.CellCorner(x, y) == source.CellCorner(x, y)
                    && restored.CellY(x, y) == source.CellY(x, y), "coordinates");
            }
            void Reject(Park.State bad)
            {
                int calls = 0;
                try { Park.FromState(bad, new Park.StateBindings { BuildNode = _ => { calls++; return first; } });
                    throw new InvalidOperationException("invalid state accepted"); }
                catch (ArgumentException) { Check(calls == 0, "whole DTO validated before factory"); }
                Check(JsonSerializer.Serialize(source.CaptureState(capture)) == original, "old Park untouched");
            }
            Reject(round with { Version = 999 });
            Reject(round with { Width = int.MaxValue });
            Reject(round with { Height = -1 });
            Reject(round with { Occupancy = Array.Empty<int>() });
            var bad = Clone(round); bad.Occupancy[0] = 901; Reject(bad);
            bad = Clone(round); bad.Placed[1] = bad.Placed[1] with { Id = 101 }; Reject(bad);
            bad = Clone(round); bad.Placed[1] = bad.Placed[1] with { X = 1, Y = 1, Footprint = bad.Placed[0].Footprint }; Reject(bad);
            bad = Clone(round); bad.Placed[1] = bad.Placed[1] with { X = int.MaxValue }; Reject(bad);
            bad = Clone(round); bad.Placed[1] = bad.Placed[1] with { Footprint = bad.Placed[1].Footprint with { ExitDX = 7 } }; Reject(bad);
            Reject(round with { BaseY = float.NaN });
            Reject(round with { Root = round.Root with { Transform = round.Root.Transform with { X = new(0, 0, 0) } } });
            Reject(round with { Terrain = round.Terrain with { Key = "" } });
            Reject(round with { HasField = true });
            try { JsonSerializer.Deserialize<Park.State>(original.Replace("\"Version\":1,", ""));
                throw new InvalidOperationException("missing required field accepted"); }
            catch (JsonException) { _checks++; }
            try { JsonSerializer.Deserialize<Park.State>(original.Replace("\"TopLevel\":false", "\"UnknownFlag\":false"));
                throw new InvalidOperationException("missing required node flag accepted"); }
            catch (JsonException) { _checks++; }
            void RejectFactory(Func<string, Node3D> factory)
            {
                try { Park.FromState(round, new Park.StateBindings { BuildNode = factory });
                    throw new InvalidOperationException("invalid factory accepted"); }
                catch (ArgumentException) { _checks++; }
            }
            var oldParent = first.GetParent(); var oldTransform = first.Transform;
            RejectFactory(_ => null);
            RejectFactory(_ => first);
            Check(GodotObject.IsInstanceValid(first) && !first.IsQueuedForDeletion() && first.GetParent() == oldParent
                && first.Transform == oldTransform, "live node not stolen/freed");
            var singleton = new Node3D { Position = new(9, 8, 7) };
            try
            {
                RejectFactory(_ => singleton);
                Check(GodotObject.IsInstanceValid(singleton) && singleton.GetParent() == null && singleton.Position == new Vector3(9, 8, 7),
                    "duplicate detached node not mutated/freed");
                int calls = 0;
                RejectFactory(_ => ++calls == 1 ? singleton : throw new ArgumentException("factory failure"));
                Check(GodotObject.IsInstanceValid(singleton) && singleton.GetParent() == null, "factory exception keeps caller ownership");
            }
            finally { singleton.Free(); }
            Check(JsonSerializer.Serialize(source.CaptureState(capture)) == original, "all rejections preserve source");
            AddChild(restored.Root);
            Check(JsonSerializer.Serialize(restored.CaptureState(capture)) == original, "publish preserves pose/TopLevel");
            // Bind before Rebuild; generated meshes are deliberately not in the DTO.
            restored.GroundMaterial = new StandardMaterial3D(); restored.CutFloor = (x, y) => x == 11 && y == 9;
            restored.Claimed = (x, y) => x == 10 && y == 9;
            restored.Rebuild();
            Check(restored.GroundRoot.GetChildCount() > 0 && !restored.GroundRoot.Visible, "deferred ground rebuild");
            Check(!restored.HasFloor(11, 9) && !restored.Vacant(10, 9), "external callbacks rebound");
            Check(restored.Remove(101) && restored.OccupiedCells == 5 && restored.CanPlace(turned, 1, 1), "remove clears id");
            Check(restored.PlacedAt(6 + turned3.EntryX, 1 + turned3.EntryY)?.Id == 102, "other placement remains");
            Check(restored.TryPlace(ModelNode("replacement"), turned, 104, "Replacement", 1, 1, 1)
                && restored.OccupiedCells == 9, "post-restore placement");
            Check(!restored.CanPlace(one, 1, 8), "reservation survives remove/placement");
            restored.ClearReservations(); Check(restored.CanPlace(one, 1, 8), "clear reservations");
            Check(source.OccupiedCells == 9 && source.Reserved(1, 8), "fresh logical arrays");
            Array.Fill(round.Occupancy, 0); Array.Fill(round.Placed[1].Footprint.Cells, false);
            Check(restored.OccupiedCells == 9 && restored.Placed[0].Fp.Occupied == 4, "restore detached from DTO arrays");
            var empty = new Park();
            try
            {
                var emptyState = Clone(empty.CaptureState(new Park.StateBindings()));
                var emptyCopy = Park.FromState(emptyState, new Park.StateBindings());
                try { Check(emptyCopy.Width == 0 && emptyCopy.OccupiedCells == 0 && emptyCopy.Playable == null, "unbuilt state"); }
                finally { emptyCopy.Root.Free(); }
            }
            finally { empty.Root.Free(); }
            GD.Print($"ParkPlacementSaveSmoke PASS ({_checks} checks)");
            GetTree().Quit(0);
        }
        catch (Exception e) { GD.PushError($"ParkPlacementSaveSmoke FAIL: {e}"); GetTree().Quit(1); }
        finally { restored?.Root.Free(); source?.Root.Free(); }
    }
}
