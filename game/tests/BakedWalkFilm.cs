using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer.Tests;

/// <summary>Section 0, the baked-vertex walk (0x1a7e18), drawn through the shipping AnimatedModel:
/// Handyman and FatMechanic, which have no skeletal walk at all, filmed from the side and from a
/// rear three-quarter at eight frames of one loop into strips under $TPW_BAKED_FILM.
/// findings/baked-walk.md.
///
/// Measured on the DRAWN positions (AnimatedModel.LivePositions), not the parser's:
/// - every drawn vertex equals the parser's group for the truncated frame, through mesh+0x98;
/// - frame 3.999 draws exactly frame 3 (no interpolation);
/// - on the lowest drawn mesh, the lowest third's left and right forward (Z) means swing in
///   anti-phase over the loop.
/// ⚠ CONTROL: the same build with TPW_PS2_BAKED=off (the port before this channel) must FAIL the
/// swing -- and its correlation is not read, because over zero variance it is rounding residue
/// that can come out at -1.000 (BakedWalkChecks). Its strip is filmed too, so the pictures carry
/// their own control.</summary>
public partial class BakedWalkFilm : Node3D
{
    int checks;
    void Check(bool value, string why) { if (!value) throw new Exception(why); checks++; GD.Print("  ok   " + why); }

    static readonly (string Stem, string Name)[] Cast =
    {
        ("/Chars/Handyman/handyman", "handyman"),
        ("/Chars/FatMechanic/FatMechanic", "fatmechanic"),
    };
    const int Shots = 8, CellW = 240, CellH = 320;

    public override async void _Ready()
    {
        int exit = 2;
        try
        {
            string film = System.Environment.GetEnvironmentVariable("TPW_BAKED_FILM") ?? throw new InvalidOperationException("TPW_BAKED_FILM unset");
            System.IO.Directory.CreateDirectory(film);
            using var lib = new AssetLibrary(OS.GetEnvironment("TPW_PS2_DISC"));
            lib.OpenWad("/DATA/DATA.WAD");
            AddChild(new WorldEnvironment { Environment = new Godot.Environment
                { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.55f, 0.7f, 0.85f) } });
            var camera = new Camera3D { Current = true, Fov = 30 };
            AddChild(camera);
            GetViewport().GetWindow().Size = new Vector2I(CellW, CellH);
            var summary = new System.Text.StringBuilder();

            foreach (var (stem, name) in Cast)
            {
                var model = new Model(lib.Read(lib.Wad.Find(stem + ".mps")));
                var aps = new Aps(lib.Read(lib.Wad.Find(stem + ".aps")));
                var rec = aps.Records().Single(r => r.Slot == 0);
                var textures = new Dictionary<string, (ImageTexture, bool)>();
                (ImageTexture, bool) Texture(string material)
                {
                    if (material == null) return (null, false);
                    if (textures.TryGetValue(material, out var t)) return t;
                    var tex = lib.TextureNear(stem + ".mps", material);
                    if (tex == null) return textures[material] = (null, false);
                    var img = Image.CreateFromData(tex.Width, tex.Height, false, Image.Format.Rgba8, tex.Pixels);
                    img.GenerateMipmaps();
                    return textures[material] = (ImageTexture.CreateFromImage(img), tex.Translucent);
                }

                foreach (bool played in new[] { true, false })
                {
                    System.Environment.SetEnvironmentVariable("TPW_PS2_BAKED", played ? null : "off");
                    var drawn = new AnimatedModel(model, aps, rec, Texture);
                    AddChild(drawn.Root);
                    try
                    {
                        string leg = played ? name : name + " CONTROL (TPW_PS2_BAKED=off)";
                        Check(drawn.BakedParts == (played ? aps.BakedVertexTracks(rec).Count : 0),
                            $"{leg}: {drawn.BakedParts} parts driven by baked tracks");
                        int loop = rec.DurationFrames;   // frames 0..duration-1; the extra table frame closes it
                        var legs = Legs(drawn, model, aps, rec);
                        var left = new double[loop]; var right = new double[loop];
                        for (int f = 0; f < loop; f++)
                        {
                            drawn.SetFrame(f);
                            var live = drawn.LivePositions(legs.Mesh.Offset);
                            left[f] = legs.Left.Average(v => (double)live[v].Z);
                            right[f] = legs.Right.Average(v => (double)live[v].Z);
                            if (played) Check(DrawnIsParsed(drawn, model, aps, rec, f), $"{leg} frame {f}: every drawn vertex is the parsed group through mesh+0x98");
                        }
                        double swingL = left.Max() - left.Min(), swingR = right.Max() - right.Min();
                        bool moves = swingL > 3000 && swingR > 3000;
                        double corr = Pearson(left, right);
                        string gait = $"{legs.Mesh.Name} lowest third L{legs.Left.Length}/R{legs.Right.Length} verts: forward swing {swingL:F0}/{swingR:F0} units, L/R correlation {corr:F3} over {loop} frames";
                        if (played)
                        {
                            Check(moves && corr < -0.85, $"{leg}: {gait}");
                            drawn.SetFrame(3f); var at3 = drawn.LivePositions(legs.Mesh.Offset).ToArray();
                            drawn.SetFrame(3.999f); var at399 = drawn.LivePositions(legs.Mesh.Offset).ToArray();
                            drawn.SetFrame(4f); var at4 = drawn.LivePositions(legs.Mesh.Offset).ToArray();
                            Check(at3.SequenceEqual(at399) && !at399.SequenceEqual(at4), $"{leg}: 3.999 draws frame 3 exactly, 4.0 does not (truncated)");
                        }
                        else Check(!moves, $"{leg}: the same measurement FAILS -- {gait} (correlation over no motion is residue)");
                        summary.AppendLine($"{leg}: {gait}");

                        // Two rows: the SIDE (forward is mesh Z, so look along X) and a rear three-quarter,
                        // because one axis cannot tell a torso pitch from a twist. Framed on the loop's box.
                        var box = Bounds(drawn, loop);
                        var centre = box.GetCenter();
                        float reach = Mathf.Max(box.Size.Y, box.Size.Z) * 0.5f / Mathf.Tan(Mathf.DegToRad(camera.Fov * 0.5f)) * 1.1f;
                        var views = new[] { new Vector3(1, 0, 0), new Vector3(0.7f, 0.15f, -0.7f).Normalized() };
                        var strip = Image.CreateEmpty(CellW * Shots, CellH * views.Length, false, Image.Format.Rgba8);
                        for (int row = 0; row < views.Length; row++)
                        {
                            camera.GlobalPosition = centre + views[row] * (reach + Mathf.Max(box.Size.X, box.Size.Z) * 0.5f);
                            camera.LookAt(centre);
                            for (int s = 0; s < Shots; s++)
                            {
                                int f = s * loop / Shots;
                                drawn.SetFrame(f);
                                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                                using var image = GetViewport().GetTexture().GetImage();
                                image.Convert(Image.Format.Rgba8);
                                strip.BlitRect(image, new Rect2I(0, 0, Math.Min(CellW, image.GetWidth()), Math.Min(CellH, image.GetHeight())), new Vector2I(s * CellW, row * CellH));
                            }
                        }
                        string path = System.IO.Path.Combine(film, $"{name}-{(played ? "s0" : "control")}.png");
                        Check(strip.SavePng(path) == Error.Ok, $"saved {System.IO.Path.GetFileName(path)} (frames 0,{loop / Shots},..,{(Shots - 1) * loop / Shots})");
                    }
                    finally { drawn.Root.QueueFree(); System.Environment.SetEnvironmentVariable("TPW_PS2_BAKED", null); }
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(film, "gait.txt"), summary.ToString());
            GD.Print($"BAKED WALK FILM PASS: {checks} checks\n{summary}strips generated, not yet inspected");
            exit = 0;
        }
        catch (Exception e) { GD.PrintErr("BAKED WALK FILM FAIL: " + e); }
        GetTree().Quit(exit);
    }

    /// <summary>The lowest shown mesh at frame 0; its lowest third by Y, split at the median X. The
    /// same rule as BakedWalkChecks, over per-vertex indices. ⚠ Chosen from the PARSED frame 0, not
    /// from what is drawn, so the control leg measures exactly the vertices the played leg does.</summary>
    static (Model.Mesh Mesh, int[] Left, int[] Right) Legs(AnimatedModel drawn, Model model, Aps aps, Aps.Record rec)
    {
        drawn.SetFrame(0);
        var shown = drawn.Surfaces().Where(s => s.Node.Visible).Select(s => s.Mesh).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var track = aps.BakedVertexTracks(rec).Where(t => shown.Contains(model.Meshes[t.Node].Name))
                       .OrderBy(t => Enumerable.Range(0, t.Groups).Min(g => t.Position(0, g).Y)).First();
        var mesh = model.Meshes[track.Node];
        var p0 = model.AnimVertexMap(mesh).Select(g => track.Position(0, g)).ToArray();
        float lo = p0.Min(p => p.Y), hi = p0.Max(p => p.Y);
        var low = Enumerable.Range(0, p0.Length).Where(v => p0[v].Y < lo + (hi - lo) / 3).ToArray();
        float mid = low.Select(v => p0[v].X).OrderBy(x => x).ElementAt(low.Length / 2);
        return (mesh, low.Where(v => p0[v].X < mid).ToArray(), low.Where(v => p0[v].X >= mid).ToArray());
    }

    static bool DrawnIsParsed(AnimatedModel drawn, Model model, Aps aps, Aps.Record rec, int frame)
    {
        foreach (var track in aps.BakedVertexTracks(rec))
        {
            var mesh = model.Meshes[track.Node];
            var live = drawn.LivePositions(mesh.Offset);
            var map = model.AnimVertexMap(mesh);
            if (live == null || live.Count != map.Length) return false;
            for (int v = 0; v < map.Length; v++) if (live[v] != track.Position(frame, map[v])) return false;
        }
        return true;
    }

    /// <summary>World-space box of every shown surface over the loop.</summary>
    static Aabb Bounds(AnimatedModel drawn, int loop)
    {
        Aabb? box = null;
        for (int f = 0; f < loop; f += Math.Max(1, loop / 8))
        {
            drawn.SetFrame(f);
            foreach (var (_, _, node) in drawn.Surfaces())
            {
                if (!node.Visible || node.Mesh == null) continue;
                var b = node.GlobalTransform * node.GetAabb();
                box = box is { } a ? a.Merge(b) : b;
            }
        }
        return box ?? throw new InvalidOperationException("nothing drawn");
    }

    static double Pearson(double[] a, double[] b)
    {
        double ma = a.Average(), mb = b.Average(), ab = 0, aa = 0, bb = 0;
        for (int i = 0; i < a.Length; i++) { ab += (a[i] - ma) * (b[i] - mb); aa += (a[i] - ma) * (a[i] - ma); bb += (b[i] - mb) * (b[i] - mb); }
        return ab / Math.Sqrt(aa * bb);
    }
}
