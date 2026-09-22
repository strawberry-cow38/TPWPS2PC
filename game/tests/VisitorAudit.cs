using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

public partial class VisitorAudit : Node3D
{
    static void Check(bool okay, string message) { if (!okay) throw new Exception(message); }
    static IEnumerable<Vector3> Vertices(Node3D root, Transform3D transform)
    {
        if (root is MeshInstance3D mesh && mesh.Visible)
            for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
                foreach (var v in mesh.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array()) yield return transform * v;
        foreach (var child in root.GetChildren().OfType<Node3D>())
            foreach (var v in Vertices(child, transform * child.Transform)) yield return v;
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
                var holderPosition = new Vector3(view.Park.Origin.X + scenario.RideOrigin.X + scenario.RideWidth / 2f - centre.X,
                    -bindLo.Y, view.Park.Origin.Y + scenario.RideOrigin.Z + scenario.RideHeight / 2f + centre.Z);
                Check(view.Ride.Drawn.Root.GetParent<Node3D>().Position.DistanceTo(holderPosition) < 0.00001f,
                    "Orbiter holder is displaced from the SAM footprint");
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
                float x = view.Park.Origin.X + head.X, z = view.Park.Origin.Y + head.Z;
                var floor = view.Park.GroundRoot.GetChildren().OfType<MeshInstance3D>().Single(m =>
                    m.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Contains(new Vector3(x, 0, z)) &&
                    ((StandardMaterial3D)m.MaterialOverride).AlbedoTexture ==
                    ((StandardMaterial3D)view.Park.MaterialForCell(scenario.Simulation.Paths.MaterialIndex("jpa_que1.ssh"))).AlbedoTexture);
                var pixels = world.TextureNear(scenario.TerrainPath, "jpa_que1.ssh");
                using (var actual = ((StandardMaterial3D)floor.MaterialOverride).AlbedoTexture.GetImage())
                    Check(actual.GetData().SequenceEqual(pixels.Pixels), "queue floor texture bytes differ from jpa_que1");
                foreach (int t in new[] { 100, 1000, 7000, 10000, 27000, 39000, 41000, 57000 })
                {
                    scenario.AdvanceTo(t); view.Update();
                    if (OS.GetCmdlineUserArgs().Contains("--mutate-position")) actor.Position = Vector3.Zero;
                    var p = ada.Position;
                    var wanted = new Vector3(view.Park.Origin.X + p.X, p.Y, view.Park.Origin.Y + p.Z);
                    Check(actor.Position.DistanceTo(wanted) <= 0.00001f, $"Ada rendered position identity at {t}ms: {actor.Position} vs {wanted}");
                    Check(actor.IsVisibleInTree() == (t is 100 or 1000 or 7000 or 39000 or 41000), $"Ada rendered visibility at {t}ms");
                    Check(view.Ride.Record == scenario.Simulation.Host.Current.Record && view.Ride.Frame == scenario.Simulation.Host.Frame,
                        $"Orbiter APS binding at {t}ms");
                    if (t == 27000) Check(view.Ride.Record.SlotName == "Start", "Orbiter running animation identity");
                }
                GD.Print($"VISITOR GEOMETRY SPACE/{terrain}: Ada is Girl1a, exact position/visibility at spawn, queue, board, unload, departure; shared queue floor texture and live APS binding");
            }
            GD.Print("VISITOR GEOMETRY PASS"); GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PrintErr("VISITOR GEOMETRY FAIL: " + ex); GetTree().Quit(2); }
    }
}
