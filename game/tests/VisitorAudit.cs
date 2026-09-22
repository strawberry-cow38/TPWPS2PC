using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

public partial class VisitorAudit : Node3D
{
    // Authored plot extents retain exporter/bind-scale rounding below 0.001 cell. Keep the
    // sim -> node identity checks at 1e-5; comparisons against those mesh extents allow 1e-3.
    const float TileTolerance = 0.001f;
    static void Check(bool okay, string message) { if (!okay) throw new Exception(message); }
    static IEnumerable<Vector3> Vertices(Node3D root, Transform3D transform)
    {
        if (root is MeshInstance3D mesh && mesh.Visible)
            for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
                foreach (var v in mesh.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array()) yield return transform * v;
        foreach (var child in root.GetChildren().OfType<Node3D>())
            foreach (var v in Vertices(child, transform * child.Transform)) yield return v;
    }
    // Locate a triangle spanning the expected cell square in the actual ground mesh. Its
    // bounds centre lies on the tile's diagonal. Do not infer placement from a tile count.
    static (MeshInstance3D Mesh, Vector3 Centre) FloorTile(Park park, ParkCell cell)
    {
        var lo = new Vector3(park.Origin.X + cell.X * Park.CellSize, park.BaseY,
            park.Origin.Y + (park.Height - cell.Z - 1) * Park.CellSize);
        var hi = lo + new Vector3(Park.CellSize, 0, Park.CellSize);
        foreach (var mesh in park.GroundRoot.GetChildren().OfType<MeshInstance3D>().Where(m => m.Mesh is ArrayMesh))
            for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                var arrays = mesh.Mesh.SurfaceGetArrays(s);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                Vector3 V(int i) => mesh.GlobalTransform * vertices[indices.Length == 0 ? i : indices[i]];
                for (int i = 0; i < (indices.Length == 0 ? vertices.Length : indices.Length); i += 3)
                {
                    var bounds = new Aabb(V(i), Vector3.Zero).Expand(V(i + 1)).Expand(V(i + 2));
                    if (bounds.Position.DistanceTo(lo) < TileTolerance && bounds.End.DistanceTo(hi) < TileTolerance)
                        return (mesh, bounds.GetCenter());
                }
            }
        throw new Exception($"No drawn floor triangle at reversed-row cell {cell}: {lo} .. {hi}");
    }
    public override void _Ready()
    {
        try
        {
            using var world = new AssetLibrary(OS.GetEnvironment("TPW_PS2_DISC")); world.OpenWad("/DATA/SPACE.WAD");
            using var data = new AssetLibrary(OS.GetEnvironment("TPW_PS2_DISC")); data.OpenWad("/DATA/DATA.WAD");
            foreach (int terrain in new[] { 1, 2 })
            {
                var scenario = new VisitorScenario(world.Wad, "SPACE", terrain);
                using var view = new VisitorParkView(this, scenario, world, data);
                Check(ReferenceEquals(view.Park.Field, scenario.Simulation.Paths.Field), "renderer is using a different path grid");
                // Independent composed model bounds check the holder's placement, before any
                // animation can make a moving ride part look like a misplaced footprint.
                var (bindLo, bindHi) = Park.Bounds(scenario.RideModel);
                var centre = (bindLo + bindHi) / 2;
                var holderPosition = new Vector3(view.Park.Origin.X + (scenario.RideOrigin.X + scenario.RideWidth / 2f) * Park.CellSize - centre.X,
                    view.Park.BaseY - bindLo.Y,
                    view.Park.Origin.Y + (view.Park.Height - scenario.RideOrigin.Z - scenario.RideHeight / 2f) * Park.CellSize + centre.Z);
                var holder = view.Ride.Drawn.Root.GetParent<Node3D>();
                Check(holder.Position.DistanceTo(holderPosition) < 0.00001f,
                    "Orbiter holder is displaced from the SAM footprint");
                // The bind centre through the actual holder/mirrored model transform must also
                // land on the footprint's drawn cells. Construction APS debris is not its centre.
                var drawnCentre = holder.GlobalTransform * (view.Ride.Drawn.Root.Transform * centre);
                var footprint = view.Park.GroundRoot.GetChildren().OfType<MeshInstance3D>()
                    .Where(m => m.Mesh is BoxMesh).SelectMany(m => Vertices(m, m.GlobalTransform)).ToArray();
                var footprintBounds = new Aabb(footprint.First(), Vector3.Zero);
                foreach (var vertex in footprint) footprintBounds = footprintBounds.Expand(vertex);
                var footprintCentre = footprintBounds.GetCenter();
                Check(new Vector2(drawnCentre.X, drawnCentre.Z).DistanceTo(new Vector2(footprintCentre.X, footprintCentre.Z)) < TileTolerance,
                    $"Orbiter drawn bind centre {drawnCentre} is displaced from drawn footprint {footprintCentre}");
                var firstTile = FloorTile(view.Park, scenario.RideOrigin).Centre;
                var lastTile = FloorTile(view.Park, scenario.RideOrigin.Offset(scenario.RideWidth - 1, scenario.RideHeight - 1)).Centre;
                Check(new Vector2(footprintCentre.X, footprintCentre.Z).DistanceTo(
                    new Vector2((firstTile.X + lastTile.X) / 2, (firstTile.Z + lastTile.Z) / 2)) < 0.00001f,
                    "Orbiter footprint is displaced from the drawn ground cells");
                var ada = scenario.Simulation.Visitors.Single(g => g.Id == 101); var actor = view.Actors[101];
                Check(actor.GetMeta("guest_id").AsInt32() == 101, "Ada's rendered guest ID");
                // Select the source asset independently of the guest registry/presenter mapping.
                var refModel = new Model(data.Read(data.Wad.Find("/Chars/Girl1a/girl1a.mps")));
                var reference = new AnimatedModel(refModel, null, null, _ => (null, false)); reference.SetFrame(0);
                try
                {
                    var (lo, hi) = Park.DrawnBounds(reference.Root, inParent: true);
                    reference.Root.Position = new Vector3(-(lo.X + hi.X) / 2, -lo.Y, -(lo.Z + hi.Z) / 2);
                    var expected = Vertices(reference.Root, reference.Root.Transform).ToArray();
                    var drawn = actor.GetChildren().OfType<Node3D>().First();
                    Check(expected.Length > 0 && Vertices(drawn, drawn.Transform).SequenceEqual(expected), "Ada geometry differs from DATA/Chars/Girl1a");
                }
                finally { reference.Root.Free(); }
                // Find the actual floor triangles covering Ada's queue cell, then inspect their bound texture.
                var head = scenario.Simulation.QueueCells[0];
                var floor = FloorTile(view.Park, head).Mesh;
                var pixels = world.TextureNear(scenario.TerrainPath, "jpa_que1.ssh");
                using (var actual = ((StandardMaterial3D)floor.MaterialOverride).AlbedoTexture.GetImage())
                    Check(actual.GetData().SequenceEqual(pixels.Pixels), "queue floor texture bytes differ from jpa_que1");
                foreach (int t in new[] { 100, 1000, 7000, 7100, 9000, 10000, 27000, 39000, 41000, 57000 })
                {
                    scenario.AdvanceTo(t); view.Update();
                    if (OS.GetCmdlineUserArgs().Contains("--mutate-position")) actor.Position = Vector3.Zero;
                    var p = ada.Position;
                    var wanted = new Vector3(view.Park.Origin.X + p.X * Park.CellSize, view.Park.BaseY + p.Y,
                        view.Park.Origin.Y + (view.Park.Height - p.Z) * Park.CellSize);
                    Check(actor.Position.DistanceTo(wanted) <= 0.00001f, $"Ada rendered position identity at {t}ms: {actor.Position} vs {wanted}");
                    Check(actor.IsVisibleInTree() == (t is 100 or 1000 or 7000 or 7100 or 9000 or 39000 or 41000), $"Ada rendered visibility at {t}ms");
                    if (t is 7000 or 9000)
                    {
                        var tile = FloorTile(view.Park, ada.Cell);
                        Check(actor.GlobalPosition.DistanceTo(tile.Centre) <= TileTolerance,
                            $"Ada is displaced from her drawn queue tile at {t}ms: {actor.GlobalPosition} vs {tile.Centre}");
                        Check(tile.Mesh == floor, "Ada's drawn tile is not the queue floor");
                        GD.Print($"VISITOR POSITION SPACE/{terrain} t={t}: Ada={actor.GlobalPosition}, queue tile={tile.Centre}");
                    }
                    if (ada.NextCell is ParkCell next)
                    {
                        var direction = (FloorTile(view.Park, next).Centre - FloorTile(view.Park, ada.Cell).Centre).Normalized();
                        Check(actor.Basis.Z.DistanceTo(direction) < 0.00001f, $"Ada facing differs from drawn path direction at {t}ms");
                    }
                    Check(view.Ride.Record == scenario.Simulation.Host.Current.Record && view.Ride.Frame == scenario.Simulation.Host.Frame,
                        $"Orbiter APS binding at {t}ms");
                    if (t == 27000) Check(view.Ride.Record.SlotName == "Start", "Orbiter running animation identity");
                }
                GD.Print($"VISITOR POSITION SPACE/{terrain}: Orbiter bind centre={drawnCentre}, drawn footprint centre={footprintCentre}");
                GD.Print($"VISITOR GEOMETRY SPACE/{terrain}: Ada is Girl1a, exact position/facing/visibility at spawn, queue, board, unload, departure; reversed rows, drawn queue floor and ride footprint, live APS binding");
            }
            GD.Print("VISITOR GEOMETRY PASS"); GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PrintErr("VISITOR GEOMETRY FAIL: " + ex); GetTree().Quit(2); }
    }
}
