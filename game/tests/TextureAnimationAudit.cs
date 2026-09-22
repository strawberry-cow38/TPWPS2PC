using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer.Tests;

/// <summary>Run this scene headless with TPW_PS2_DISC. Examines the actual surface shaders after
/// AnimatedModel.SetFrame, so a working decoder with a disconnected material update fails.</summary>
public partial class TextureAnimationAudit : Node
{
    int _checks;
    public override void _Ready()
    {
        try
        {
            using var lib = new AssetLibrary(OS.GetEnvironment("TPW_PS2_DISC"));
            Run(lib, "HALLOW", "rides/phantom/phantom.mps", 10, 30, 4, i => $"TV_PM_{i:00}");
            Run(lib, "JUNGLE", "Rides/TVSim/tvsim.mps", 7, 10, 2, i => $"tvs{i + 1}");
            Run(lib, "FANTASY", "Rides/bugstv/bugstv.mps", 19, 10, 2, i => $"tv{i + 1:00}");
            Run(lib, "SPACE", "Rides/tv_ride/tv_ride.mps", 19, 15, 4, i => $"TV_{i:0000}");
            // Texture-only APS records (no geometry tracks) must have a clock and animate too.
            RunLights(lib);
            RunViewerClock(lib);
            GD.Print($"TEXTURE BINDING PASS: {_checks} surface checks across five models / four worlds");
            GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PrintErr("TEXTURE BINDING FAIL: " + ex); GetTree().Quit(2); }
    }

    void Run(AssetLibrary lib, string world, string path, int slot, int count, int interval, Func<int, string> name)
    {
        lib.OpenWad("/DATA/" + world + ".WAD");
        var ride = lib.Rides.Single(r => r.Name.Equals(path, StringComparison.OrdinalIgnoreCase));
        var model = new Model(lib.Read(ride.Model));
        var anim = new Aps(lib.Read(ride.Animation));
        var record = anim.Records().First(r => r.Slot == 5);
        var cache = new Dictionary<string, (ImageTexture, bool)>(StringComparer.OrdinalIgnoreCase);
        (ImageTexture, bool) Texture(string material)
        {
            if (cache.TryGetValue(material, out var found)) return found;
            var data = lib.TextureNear(ride.Model.Path, material)
                ?? throw new InvalidDataException($"missing {world}/{path}/{material}");
            using var img = Image.CreateFromData(data.Width, data.Height, false, Image.Format.Rgba8, data.Pixels);
            var texture = ImageTexture.CreateFromImage(img);
            texture.ResourceName = System.IO.Path.GetFileNameWithoutExtension(data.SourcePath);
            return cache[material] = (texture, data.PartialAlpha * 100 > data.Width * data.Height);
        }
        var drawn = new AnimatedModel(model, anim, record, Texture);
        AddChild(drawn.Root);
        try
        {
            if (drawn.Frames != record.DurationFrames) throw new Exception("authored duration not used");
            var materialSlots = model.Meshes.SelectMany(m => model.Triangles(m).GroupBy(t => t.Material).Select(g => g.Key)).ToArray();
            var surfaces = drawn.Root.GetChildren().OfType<MeshInstance3D>().ToArray();
            var selected = surfaces.Where((s, i) => materialSlots[i] == slot).ToArray();
            if (selected.Length == 0) throw new Exception($"no surface for {world} slot {slot}");
            void At(float frame, string expected)
            {
                drawn.SetFrame(frame);
                foreach (var surface in selected)
                {
                    var shader = (ShaderMaterial)surface.MaterialOverride;
                    var bound = shader.GetShaderParameter("albedo_tex").AsGodotObject() as ImageTexture;
                    if (bound == null || !bound.ResourceName.Equals(expected, StringComparison.OrdinalIgnoreCase)
                        || !shader.GetShaderParameter("has_tex").AsBool())
                        throw new Exception($"{world} slot {slot} at {frame}: expected {expected}, got {bound?.ResourceName}");
                    _checks++;
                }
            }
            for (int i = 0; i < count; i++)
            {
                if (i > 0) At(i * interval - 0.25f, name(i - 1));
                At(i * interval, name(i));
            }
            At(0, name(0)); // loop/backward seek
            At(interval, name(1)); // resume
            GD.Print($"BINDING {world}: {count} authored choices, boundary holds and rewind passed");
        }
        finally
        {
            drawn.Root.Free();
            foreach (var value in cache.Values) value.Item1.Dispose();
        }
    }

    void RunLights(AssetLibrary lib)
    {
        lib.OpenWad("/DATA/HALLOW.WAD");
        var ride = lib.Rides.Single(r => r.Name.Equals("features/lights/LIGHTS.mps", StringComparison.OrdinalIgnoreCase));
        var model = new Model(lib.Read(ride.Model));
        var anim = new Aps(lib.Read(ride.Animation));
        var records = anim.Records().Where(r => r.Slot == 5).ToArray();
        for (int i = 0; i < 2; i++)
        {
            if (records[i].TrackCount != 0) throw new Exception("lights fixture acquired geometry tracks");
            var textures = model.MaterialTextures.SelectMany(f => f).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(n => n, n =>
            {
                var data = lib.TextureNear(ride.Model.Path, n) ?? throw new Exception("lights texture missing");
                using var image = Image.CreateFromData(data.Width, data.Height, false, Image.Format.Rgba8, data.Pixels);
                return ImageTexture.CreateFromImage(image);
            }, StringComparer.OrdinalIgnoreCase);
            var drawn = new AnimatedModel(model, anim, records[i], n => (textures[n], false));
            try
            {
                drawn.SetFrame(0);
                if (drawn.Frames != 10) throw new Exception("texture-only record duration lost");
                var slots = model.Meshes.SelectMany(m => model.Triangles(m).GroupBy(t => t.Material).Select(g => g.Key)).ToArray();
                foreach (var surface in drawn.Root.GetChildren().OfType<MeshInstance3D>().Where((s, j) => slots[j] == 0))
                {
                    var bound = ((ShaderMaterial)surface.MaterialOverride).GetShaderParameter("albedo_tex").AsGodotObject();
                    if (bound != textures[model.MaterialTextures[0][i]]) throw new Exception("texture-only record selection lost");
                    _checks++;
                }
            }
            finally { drawn.Root.Free(); foreach (var texture in textures.Values) texture.Dispose(); }
        }
    }

    void RunViewerClock(AssetLibrary lib)
    {
        // Exercise the real Viewer._Process -> AnimatedModel -> TextureNear/cache -> shader chain.
        // Reflection keeps test controls out of the product API.
        var fields = typeof(Viewer).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .ToDictionary(f => f.Name);
        lib.OpenWad("/DATA/HALLOW.WAD");
        var ride = lib.Rides.Single(r => r.Name.Equals("rides/phantom/phantom.mps", StringComparison.OrdinalIgnoreCase));
        var model = new Model(lib.Read(ride.Model));
        var anim = new Aps(lib.Read(ride.Animation));
        int main = anim.Records().ToList().FindIndex(r => r.Slot == 5);
        var viewer = new Viewer();
        fields["_wantWad"].SetValue(viewer, "HALLOW");
        fields["_wantRide"].SetValue(viewer, "phantom.mps");
        fields["_wantAnim"].SetValue(viewer, main.ToString());
        AddChild(viewer);
        viewer.SetProcess(false);
        try
        {
            var drawn = (AnimatedModel)fields["_current"].GetValue(viewer) ?? throw new Exception("Viewer failed to build Phantom");
            var cache = (Dictionary<string, (ImageTexture Tex, bool Soft)>)fields["_texCache"].GetValue(viewer);
            var slots = model.Meshes.SelectMany(m => model.Triangles(m).GroupBy(t => t.Material).Select(g => g.Key)).ToArray();
            var surfaces = drawn.Root.GetChildren().OfType<MeshInstance3D>().Where((s, i) => slots[i] == 10).ToArray();
            if (surfaces.Length == 0) throw new Exception("Viewer Phantom has no screen");
            void Expect(string filename)
            {
                if (!cache.TryGetValue(ride.Model.Path + "|" + filename, out var texture) || texture.Tex == null)
                    throw new Exception("Viewer did not resolve " + filename);
                foreach (var surface in surfaces)
                {
                    var bound = ((ShaderMaterial)surface.MaterialOverride).GetShaderParameter("albedo_tex").AsGodotObject();
                    if (bound != texture.Tex) throw new Exception("Viewer did not bind " + filename);
                    _checks++;
                }
            }
            Expect("TV_PM_00.ssh");
            viewer._Process(4.0 / Aps.Fps);
            Expect("TV_PM_01.ssh");
            fields["_playing"].SetValue(viewer, false);
            viewer._Process(4.0 / Aps.Fps);
            Expect("TV_PM_01.ssh");
            fields["_playing"].SetValue(viewer, true);
            fields["_time"].SetValue(viewer, 118.5f);
            viewer._Process(1.0 / Aps.Fps);
            Expect("TV_PM_00.ssh");
            if (Math.Abs((float)fields["_time"].GetValue(viewer) - 0.5f) > 0.001f)
                throw new Exception("Viewer loop discarded elapsed time");
            // A model without an APS has no duration. Keep its clock finite when wrapping.
            var still = new AnimatedModel(model, null, null, n => cache[ride.Model.Path + "|" + n]);
            try
            {
                fields["_current"].SetValue(viewer, still);
                fields["_time"].SetValue(viewer, 0f);
                viewer._Process(1.0 / Aps.Fps);
                if ((float)fields["_time"].GetValue(viewer) != 0f)
                    throw new Exception("Viewer static-model clock is not zero");
            }
            finally { fields["_current"].SetValue(viewer, drawn); still.Root.Free(); }
            GD.Print("VIEWER CLOCK PASS: advance, pause, authored-duration wrap, static model, scoped cache and shader binding");
        }
        finally
        {
            (fields["_lib"].GetValue(viewer) as AssetLibrary)?.Dispose();
            viewer.Free();
        }
    }
}
