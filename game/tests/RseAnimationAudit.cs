using Godot;
using TPW.PS2.Data;
using TPW.PS2.Audits;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer.Tests;

public partial class RseAnimationAudit : Node3D
{
    public override void _Ready()
    {
        string context = "JUNGLE Ape regression";
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
                presenter.Update(preview.Host);
                // Bind through the actual demo presenter at construction, load, start and Main.
                if (t is not (0 or 7500 or 7700 or 19000 or 19500 or 35900)) continue;
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
                    var reference = Reference(model, aps, record);
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
            foreach (string world in new[] { "FANTASY", "SPACE", "HALLOW" })
            {
                lib.OpenWad($"/DATA/{world}.WAD");
                foreach (int terrain in new[] { 1, 2 })
                {
                    var evidence = new VisitorExpectations(lib.Wad, world, terrain, line => { context = line; GD.Print(line); });
                    AuditVisitors(lib, evidence);
                }
            }
            GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PrintErr($"RSE ANIMATION FAIL [{context}]: " + ex); GetTree().Quit(2); }
    }
    void AuditVisitors(AssetLibrary lib, VisitorExpectations evidence)
    {
        var scenario = new VisitorScenario(lib.Wad, evidence.World, evidence.TerrainNumber);
        evidence.CheckLayout(scenario);
        using var presenter = new RseModelPresenter(this, scenario.RideModel, scenario.RideAnimation, _ => (null, false));
        // Read a separate APS/model reference; expected slot, absolute start and frame come
        // from RSS/APS arithmetic, never from the VM host's reported playback.
        var model = new Model(lib.Read(lib.Wad.Find(evidence.Stem + ".mps")));
        var aps = new Aps(lib.Read(lib.Wad.Find(evidence.Stem + ".aps")));
        presenter.Update(scenario.Simulation.Host);
        foreach (var play in evidence.Animations)
        {
            Vector3[] first = null;
            foreach (long t in new[] { (play.Start + 99) / 100 * 100 + 100, (play.Start + 99) / 100 * 100 + 600 })
            {
                // Bugs TV's ten-frame End is shorter than the second sample interval.
                if (t >= play.Resume) continue;
                while (scenario.Simulation.Time < t)
                {
                    scenario.AdvanceTo(scenario.Simulation.Time + 100);
                    presenter.Update(scenario.Simulation.Host);
                }
                float frame = (t - play.Start) * 30 / 1000f;
                var record = aps.Records().First(r => r.Slot == play.Slot);
                if (presenter.Record.Slot != play.Slot || scenario.Simulation.Host.AnimationVariant != 0
                    || Math.Abs(presenter.Frame - frame) > 0.001f)
                    throw new Exception($"{evidence.Label} t={t}: expected slot {play.Slot}:0 frame {frame}, got {presenter.Record.Slot} frame {presenter.Frame}");
                if (OS.GetCmdlineUserArgs().Contains("--mutate-freeze")) presenter.Drawn.SetFrame(0);
                var reference = Reference(model, aps, record);
                try
                {
                    reference.SetFrame(frame);
                    var actual = Vertices(presenter.Drawn);
                    if (actual.Length == 0 || !actual.SequenceEqual(Vertices(reference)))
                        throw new Exception($"{evidence.Label} t={t}: rendered vertex identity differs from {record.SlotName}:0 frame {frame}");
                    if (first != null && first.SequenceEqual(actual))
                        throw new Exception($"{evidence.Label} {record.SlotName}: geometry did not move between sampled frames");
                    first = actual;
                    GD.Print($"RSE GEOMETRY {evidence.Label} t={t}: {record.SlotName}:0 frame={frame}; rendered vertices match explicit APS reference; {evidence.Counts}");
                }
                finally { reference.Root.Free(); }
            }
        }
        scenario.AdvanceTo(evidence.DepartAt);
        if (scenario.Simulation.Visitors.Any(g => g.State != VisitorState.Departed)
            || scenario.Simulation.Machine.GuestIds.Count != 0 || scenario.Simulation.Machine["VAR_ONRIDE"] != 0)
            throw new Exception(evidence.Label + ": stranded rider after the derived departure");
        GD.Print($"RSE ANIMATION PASS {evidence.Evidence}: exact Start/Main/End geometry and frames; all four guests departed");
    }
    static AnimatedModel Reference(Model model, Aps aps, Aps.Record target)
    {
        var create = aps.Records().First(r => r.Slot == 0);
        var reference = new AnimatedModel(model, aps, create, _ => (null, false));
        try
        {
            // The presenter now retains node visibility across record changes. Establish it
            // from the disc's completed Create/Load/Start/Main path, not a fresh visible model
            // (which resurrects Ape's destroyed crate). Require the tail to be stable across
            // the early-resume window before using a completed record as the reference.
            foreach (var record in new[] { create }.Concat(aps.Records()
                .Where(r => r.Slot >= 3 && r.Slot < target.Slot).GroupBy(r => r.Slot).Select(g => g.First())))
            {
                foreach (var timeline in aps.Visibility(record).Values)
                    for (int f = Math.Max(0, record.DurationFrames - 10); f <= record.DurationFrames; f++)
                        if (Aps.VisibleAt(timeline, f) != Aps.VisibleAt(timeline, record.DurationFrames))
                            throw new Exception($"{record.SlotName} visibility changes in its final ten frames; reference needs its exact switch frame");
                reference.UseRecord(record); reference.SetFrame(record.DurationFrames);
            }
            reference.UseRecord(target);
            return reference;
        }
        catch { reference.Root.Free(); throw; }
    }
    static Vector3[] Vertices(AnimatedModel model) => model.Root.GetChildren().OfType<MeshInstance3D>()
        .Where(s => s.Visible).SelectMany(s => ((ArrayMesh)s.Mesh).SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex]
            .AsVector3Array().Select(v => s.Transform * v)).ToArray();
}
