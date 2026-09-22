using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer.Tests;

public partial class RseAnimationAudit : Node3D
{
    public override void _Ready()
    {
        try
        {
            using var lib = new AssetLibrary(OS.GetEnvironment("TPW_PS2_DISC"));
            lib.OpenWad("/DATA/JUNGLE.WAD");
            byte[] Read(string ext) => lib.Read(lib.Wad.Find("/Rides/Monkey/Monkey" + ext));
            var model = new Model(Read(".mps")); var aps = new Aps(Read(".aps"));
            var preview = new RseRidePreview(new RseProgram(Read(".rse")), aps);
            using var presenter = new RseModelPresenter(this, model, aps, _ => (null, false));
            Vector3[] first = null;
            for (int t = 0; t <= 35900; t += 100)
            {
                preview.Tick(t);
                // Bind through the actual demo presenter at construction, load, start and Main.
                if (t is not (0 or 7500 or 7700 or 19000 or 19500 or 35900)) continue;
                presenter.Update(preview.Host);
                if (t is 19000 or 19500)
                {
                    if (presenter.Record.Slot != 5 || preview.Host.AnimationVariant != 1)
                        throw new Exception($"t={t}: expected Main variant 1");
                    // Create=215 frames; Start=335. Main starts at 7700 + trunc(335/30*1000).
                    float expectedFrame = (t - 18866) * 30 / 1000f;
                    if (Math.Abs(presenter.Frame - expectedFrame) > 0.001f)
                        throw new Exception($"t={t}: expected APS frame {expectedFrame}, got {presenter.Frame}");
                    if (OS.GetCmdlineUserArgs().Contains("--mutate-freeze")) presenter.Drawn.SetFrame(0);
                    var record = aps.Records().Where(r => r.Slot == 5).ElementAt(1);
                    var reference = new AnimatedModel(model, aps, record, _ => (null, false));
                    try
                    {
                        reference.SetFrame(expectedFrame);
                        var actual = Vertices(presenter.Drawn); var expected = Vertices(reference);
                        if (!actual.SequenceEqual(expected)) throw new Exception($"t={t}: rendered vertex identity differs from Main:1 at frame {expectedFrame}");
                        if (first == null) first = actual;
                        else if (first.SequenceEqual(actual)) throw new Exception("Main geometry did not move between frames 4.02 and 19.02");
                        GD.Print($"RSE GEOMETRY t={t}: Main:1 frame={expectedFrame}; rendered vertices match explicit APS reference");
                    }
                    finally { reference.Root.Free(); }
                }
            }
            if (!preview.Completed || preview.Machine[5] != 0 || preview.Machine[9] != 0)
                throw new Exception("Ape did not unload and stop at 35900ms");
            GD.Print("RSE ANIMATION PASS: script-selected record, exact frame, moving geometry and completed cycle");
            GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PrintErr("RSE ANIMATION FAIL: " + ex); GetTree().Quit(2); }
    }
    static Vector3[] Vertices(AnimatedModel model) => model.Root.GetChildren().OfType<MeshInstance3D>()
        .Where(s => s.Visible).SelectMany(s => ((ArrayMesh)s.Mesh).SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex]
            .AsVector3Array().Select(v => s.Transform * v)).ToArray();
}
