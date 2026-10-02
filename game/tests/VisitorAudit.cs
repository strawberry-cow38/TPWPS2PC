using Godot;
using TPW.PS2.Data;
using TPW.PS2.Audits;

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
    // Placement measures each material surface's local AABB through its bind transform.
    // Derive that envelope directly from the disc's indexed triangles, without asking the
    // placed node or Park.DrawnBounds for the answer. Rotated Bugs TV decorations make the
    // surface-box envelope differ from Park.Bounds' tight vertex bounds (Orbiter hid this).
    static (Vector3 Min, Vector3 Max) BindSurfaceBounds(Model model)
    {
        var corners = new List<Vector3>();
        var transforms = model.WorldTransforms();
        var floorMeshes = model.Meshes.Where(m => m.Name.Contains("floor", StringComparison.OrdinalIgnoreCase)
            && model.Triangles(m).Any()).ToArray();
        foreach (var mesh in floorMeshes.Length > 0 ? floorMeshes : model.Meshes.ToArray())
        {
            var vertices = model.Vertices(mesh).Pos;
            foreach (var surface in model.Triangles(mesh).GroupBy(t => t.Material))
            {
                var points = surface.SelectMany(t => new[] { vertices[t.A], vertices[t.B], vertices[t.C] }).ToArray();
                var lo = points.Aggregate(System.Numerics.Vector3.Min);
                var hi = points.Aggregate(System.Numerics.Vector3.Max);
                for (int i = 0; i < 8; i++)
                {
                    var p = new System.Numerics.Vector3((i & 1) == 0 ? lo.X : hi.X,
                        (i & 2) == 0 ? lo.Y : hi.Y, (i & 4) == 0 ? lo.Z : hi.Z);
                    p = System.Numerics.Vector3.Transform(p, transforms[mesh.Offset]);
                    corners.Add(new Vector3(p.X, p.Y, p.Z));
                }
            }
        }
        Check(corners.Count > 0, "ride has no bind surfaces");
        var bounds = new Aabb(corners[0], Vector3.Zero);
        foreach (var p in corners) bounds = bounds.Expand(p);
        return (bounds.Position, bounds.End);
    }
    // Locate a triangle spanning the expected cell square in the actual ground mesh. Its
    // bounds centre lies on the tile's diagonal. Do not infer placement from a tile count.
    static (MeshInstance3D Mesh, Vector3 Centre) FloorTile(Park park, VisitorExpectations evidence, ParkCell cell)
    {
        // Derive corners from the disc marker's local bounds and bind scale, without asking
        // Park.Origin/PlotSpace/CellCorner or the guest node for the expected tile position.
        var a = evidence.DrawnPosition(new System.Numerics.Vector3(cell.X, 0, cell.Z));
        var b = evidence.DrawnPosition(new System.Numerics.Vector3(cell.X + 1, 0, cell.Z + 1));
        var lo = new Vector3(Math.Min(a.X, b.X), 0, Math.Min(a.Z, b.Z));
        var hi = new Vector3(Math.Max(a.X, b.X), 0, Math.Max(a.Z, b.Z));
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
        string context = "census unavailable (assets not read)";
        try
        {
            using var world = new AssetLibrary(OS.GetEnvironment("TPW_PS2_DISC"));
            using var data = new AssetLibrary(OS.GetEnvironment("TPW_PS2_DISC")); data.OpenWad("/DATA/DATA.WAD");
            var verdicts = new List<string>();
            foreach (string worldName in new[] { "FANTASY", "SPACE", "HALLOW" })
            {
                world.OpenWad($"/DATA/{worldName}.WAD");
                foreach (int terrain in new[] { 1, 2 })
                {
                    var evidence = new VisitorExpectations(world.Wad, worldName, terrain, s => { context = s; GD.Print(s); });
                    var scenario = new VisitorScenario(world.Wad, worldName, terrain);
                    evidence.CheckLayout(scenario);
                    using var view = new VisitorParkView(this, scenario, world, data);
                    Check(ReferenceEquals(view.Park.Field, scenario.Simulation.Paths.Field), "renderer is using a different path grid");
                    // Independent composed model bounds check the holder's placement, before any
                    // animation can make a moving ride part look like a misplaced footprint.
                    var (bindLo, bindHi) = BindSurfaceBounds(scenario.RideModel);
                    var centre = (bindLo + bindHi) / 2;
                    // ⭐ THE HOLDER IS ANCHORED BY THE MODEL'S OWN ORIGIN, then oversized for the seam
                    // (Park.TryPlace, 00e5cd9 + a2c5100/66206bb). Built from the SAM footprint and the
                    // MEASURED corner sequence (`TPW_PLACE_AUDIT=1`: the origin sat on the predicted corner
                    // at every turn), not from Park's own arithmetic:
                    //   turns 0 -> (minX, maxZ)  1 -> (maxX, maxZ)  2 -> (maxX, minZ)  3 -> (minX, minZ)
                    // then scaled about the footprint centre by (W + 2·overlap) / W per axis.
                    // ⚠ This audit expected the old rule -- the bind box CENTRED on the footprint -- and went
                    // red on 09-26 by 0.045 / 0.0092 in FANTASY/1. Both residues close exactly: the bind
                    // centre is (2.005, 2.0592) against a cell centre of 2, and the 0.05 seam overlap the
                    // centring branch never applied.
                    const int turns = 0;                                   // VisitorParkView places unturned
                    float x0 = view.Park.Origin.X + evidence.Origin.X * Park.CellSize, x1 = x0 + evidence.Width * Park.CellSize;
                    float z1 = view.Park.Origin.Y + (view.Park.Height - evidence.Origin.Z) * Park.CellSize, z0 = z1 - evidence.Height * Park.CellSize;
                    var (cornerX, cornerZ) = turns switch { 0 => (x0, z1), 1 => (x1, z1), 2 => (x1, z0), _ => (x0, z0) };
                    float fpW = evidence.Width * Park.CellSize, fpH = evidence.Height * Park.CellSize;
                    float sx = (fpW + 2f * Park.RideSeamOverlap) / fpW, sz = (fpH + 2f * Park.RideSeamOverlap) / fpH;
                    float fcx = (x0 + x1) / 2, fcz = (z0 + z1) / 2;
                    var holderPosition = new Vector3(fcx + (cornerX - fcx) * sx, view.Park.CellY(evidence.Origin.X, evidence.Origin.Z),
                                                     fcz + (cornerZ - fcz) * sz);
                    var holder = view.Ride.Drawn.Root.GetParent<Node3D>();
                    Check(holder.Position.DistanceTo(holderPosition) < 0.00001f,
                        $"Ride holder is not at its origin corner, seam-oversized: actual={holder.Position}, expected={holderPosition}, bind={bindLo}..{bindHi}");
                    Check(holder.Basis.Column0.IsEqualApprox(new Vector3(sx, 0, 0)) && holder.Basis.Column1.IsEqualApprox(Vector3.Up)
                          && holder.Basis.Column2.IsEqualApprox(new Vector3(0, 0, sz)),
                        $"Ride holder carries the seam scale ({sx}, 1, {sz}) and no turn: {holder.Basis}");
                    // A control that this check SEES the rule: the old centring expectation must be
                    // somewhere else, or a return to it would pass here unnoticed.
                    var centred = new Vector3(fcx - centre.X, holderPosition.Y, fcz + centre.Z);
                    Check(centred.DistanceTo(holderPosition) > 0.001f,
                        $"the origin-corner and centring rules are told apart here ({centred.DistanceTo(holderPosition):F4} apart)");
                    // The bind centre through the actual holder/mirrored model transform must also
                    // land on the footprint's drawn cells. Construction APS debris is not its centre.
                    var drawnCentre = holder.GlobalTransform * (view.Ride.Drawn.Root.Transform * centre);
                    // Park no longer draws debug BoxMesh baseplates. Measure the ground
                    // triangles at every claimed SAM cell instead; they still exist here
                    // because VisitorParkView builds the floor before placing the ride.
                    var claimed = scenario.Simulation.Paths.Cells.Where(c => !view.Park.Vacant(c.X, c.Z)).ToArray();
                    Check(claimed.ToHashSet().SetEquals(evidence.Footprint), "Rendered ride claims different SAM cells");
                    var footprint = claimed.Select(c => FloorTile(view.Park, evidence, c).Centre).ToArray();
                    var footprintBounds = new Aabb(footprint.First(), Vector3.Zero);
                    foreach (var vertex in footprint) footprintBounds = footprintBounds.Expand(vertex);
                    var footprintCentre = footprintBounds.GetCenter();
                    // ⚠ Not "the bind centre IS the footprint centre" any more: anchoring by the origin puts
                    // a lopsided model's overhang on the side its author put it. What must still hold is that
                    // the drawn bind centre, through the real holder and mirrored child, lies over the ride's
                    // own cells.
                    Check(drawnCentre.X >= x0 && drawnCentre.X <= x1 && drawnCentre.Z >= z0 && drawnCentre.Z <= z1,
                        $"Ride drawn bind centre {drawnCentre} is not over its own footprint {x0}..{x1} x {z0}..{z1} (centre {footprintCentre})");
                    var firstTile = FloorTile(view.Park, evidence, evidence.Origin).Centre;
                    var lastTile = FloorTile(view.Park, evidence, evidence.Origin.Offset(evidence.Width - 1, evidence.Height - 1)).Centre;
                    Check(new Vector2(footprintCentre.X, footprintCentre.Z).DistanceTo(
                        new Vector2((firstTile.X + lastTile.X) / 2, (firstTile.Z + lastTile.Z) / 2)) < 0.00001f,
                        "Ride footprint is displaced from the drawn ground cells");
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
                    var head = evidence.Head;
                    var floor = FloorTile(view.Park, evidence, head).Mesh;
                    var pixels = world.TextureNear(scenario.TerrainPath, "jpa_que1.ssh");
                    using (var actual = ((StandardMaterial3D)floor.MaterialOverride).AlbedoTexture.GetImage())
                        Check(actual.GetData().SequenceEqual(pixels.Pixels), "queue floor texture bytes differ from jpa_que1");
                    foreach (long t in new[] { 0L, 100, 1000, evidence.QueueAt, evidence.QueueAt + 100, evidence.Board[0] - 1000,
                        evidence.Board[0] - 100, evidence.Board[0], evidence.RunningAt, evidence.StartAnimationAt + 100,
                        evidence.Unload[^1] - 100, evidence.Unload[^1], evidence.AdaAck - 100, evidence.AdaAck,
                        evidence.DepartAt - 100, evidence.DepartAt }.Distinct().OrderBy(t => t))
                    {
                        scenario.AdvanceTo(t); view.Update();
                        if (t >= 100 && OS.GetCmdlineUserArgs().Contains("--mutate-position")) actor.Position = Vector3.Zero;
                        if (t >= 100 && worldName == "HALLOW" && terrain == 1 && OS.GetCmdlineUserArgs().Contains("--mutate-hallow-x"))
                            actor.Position = new Vector3(2 * view.Park.Origin.X + view.Park.Width * Park.CellSize - actor.Position.X,
                                actor.Position.Y, actor.Position.Z);
                        var pose = evidence.AdaAt(t);
                        Check(ada.State == pose.State && ada.Cell == pose.Cell && ada.NextCell == pose.Next && ada.EdgeProgress == pose.Progress,
                            $"Ada simulation differs from derived path/timeline at {t}ms");
                        var p = pose.Position;
                        var wanted = new Vector3(view.Park.Origin.X + p.X * Park.CellSize, view.Park.BaseY + p.Y,
                            view.Park.Origin.Y + (view.Park.Height - p.Z) * Park.CellSize);
                        Check(actor.Position.DistanceTo(wanted) <= 0.00001f, $"{evidence.Label} Ada rendered position identity at {t}ms: {actor.Position} vs {wanted}");
                        Check(actor.IsVisibleInTree() == pose.Visible, $"Ada rendered visibility at {t}ms");
                        if (pose.Visible)
                        {
                            var position = evidence.DrawnPosition(p);
                            Check(actor.GlobalPosition.DistanceTo(new Vector3(position.X, position.Y, position.Z)) < TileTolerance,
                                $"Ada differs from disc-derived tile position at {t}ms");
                        }
                        if (t == evidence.QueueAt || t == evidence.Board[0] - 1000)
                        {
                            var tile = FloorTile(view.Park, evidence, pose.Cell);
                            Check(actor.GlobalPosition.DistanceTo(tile.Centre) <= TileTolerance,
                                $"Ada is displaced from her drawn queue tile at {t}ms: {actor.GlobalPosition} vs {tile.Centre}");
                            Check(tile.Mesh == floor, "Ada's drawn tile is not the queue floor");
                            GD.Print($"VISITOR POSITION {evidence.Label} t={t}: Ada={actor.GlobalPosition}, queue tile={tile.Centre}");
                        }
                        if (pose.Next is ParkCell next)
                        {
                            var direction = (FloorTile(view.Park, evidence, next).Centre - FloorTile(view.Park, evidence, pose.Cell).Centre).Normalized();
                            Check(actor.Basis.Z.DistanceTo(direction) < 0.00001f, $"Ada facing differs from drawn path direction at {t}ms");
                        }
                        Check(view.Ride.Record == scenario.Simulation.Host.Current.Record && view.Ride.Frame == scenario.Simulation.Host.Frame,
                            $"Ride APS binding at {t}ms");
                        if (t == evidence.StartAnimationAt + 100) Check(view.Ride.Record.SlotName == "Start", "Ride running animation identity");
                    }
                    GD.Print($"VISITOR POSITION {evidence.Label}: Ride bind centre={drawnCentre}, drawn footprint centre={footprintCentre}");
                    verdicts.Add(evidence.Evidence);
                    GD.Print($"VISITOR GEOMETRY PASS {evidence.Evidence}: Ada is Girl1a, exact position/facing/visibility at spawn, queue, board, unload, departure; reversed rows, drawn queue floor and ride footprint, live APS binding");
                }
            }
            GD.Print("VISITOR GEOMETRY PASS: " + string.Join("; ", verdicts)); GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PrintErr($"VISITOR GEOMETRY FAIL [{context}]: {ex}"); GetTree().Quit(2); }
    }
}
