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
            RunScrollingTextures(lib);
            GD.Print($"TEXTURE BINDING PASS: {_checks} surface checks across five models / four worlds");
            GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PrintErr("TEXTURE BINDING FAIL: " + ex); GetTree().Quit(2); }
    }

    /// <summary>The scrolling-texture subsystem, end to end in a live Godot.
    ///
    /// ⭐⭐ THIS EXISTS FOR ONE FAILURE IN PARTICULAR. The clock is a `global uniform`, and a
    /// global uniform whose parameter was never registered does not error -- it reads ZERO
    /// FOREVER, so every moving texture simply stands still and looks exactly like a feature
    /// that was never written. The data audit cannot see that; only a running engine can.</summary>
    void RunScrollingTextures(AssetLibrary lib)
    {
        Ps2Materials.TextureTime = 0f;
        Ps2Materials.TextureTime = 4.25f;
        if (Mathf.Abs(Ps2Materials.TextureTime - 4.25f) > 0.001f)
            throw new Exception($"texture clock did not round-trip: {Ps2Materials.TextureTime}");

        // The animated shader must actually expose the two knobs the C# sets.
        var probe = Ps2Materials.Animated(null, false, "cull_back", new Vector2(0f, 0.4f), 0.9f, Vector3.Zero);
        var names = new System.Collections.Generic.HashSet<string>();
        foreach (var u in probe.Shader.GetShaderUniformList())
            names.Add(((Godot.Collections.Dictionary)u)["name"].AsString());
        foreach (var want in new[] { "uv_scroll", "uv_spin", "water_wave" })
            if (!names.Contains(want)) throw new Exception($"animated shader has no `{want}` uniform");
        if ((float)probe.GetShaderParameter("uv_spin") != 0.9f) throw new Exception("uv_spin did not set");
        // ⭐⭐ THE CHECK THAT MATTERS, and the one the global-uniform version failed: the clock
        // must ARRIVE AT A MATERIAL. A TextureTime that only remembers its own number is exactly
        // what a frozen river looks like from the outside.
        Ps2Materials.TextureTime = 7.5f;
        if ((float)probe.GetShaderParameter("uv_time") != 7.5f)
            throw new Exception($"clock did not reach the material: {probe.GetShaderParameter("uv_time")}");
        _checks += 3;

        // ⭐ The deliverable itself: the JUNGLE Coconut's drink is a centred spiral and must come
        // out of AnimatedModel with a spin on the surface wearing `cn_nut2a`, and no spin on the
        // husk beside it. Master: "the 'liquid' in the coconut should twist".
        lib.OpenWad("/DATA/JUNGLE.WAD");
        var entry = lib.Wad.Entries.First(e => e.Path.EndsWith("Shops/Coconut/coconut.mps", StringComparison.OrdinalIgnoreCase));
        var model = new Model(lib.Read(entry));
        var built = new AnimatedModel(model, null, null, n =>
        {
            var img = lib.TextureNear(entry.Path, n);
            return (img == null ? null : ImageTexture.CreateFromImage(
                Image.CreateFromData(img.Width, img.Height, false, Image.Format.Rgba8, img.Pixels)), false);
        });
        try
        {
            if (built.MovingSurfaces == 0) throw new Exception("coconut has no moving surface");
            int spun = 0, still = 0;
            foreach (var (mesh, material, node) in built.Surfaces())
            {
                string tex = model.Materials[material] ?? "";
                float spin = node.MaterialOverride is ShaderMaterial sm
                    && sm.Shader.GetShaderUniformList().Count > 0
                    && sm.GetShaderParameter("uv_spin").VariantType != Variant.Type.Nil
                    ? (float)sm.GetShaderParameter("uv_spin") : 0f;
                if (TextureMotion.IsSwirl(tex)) { if (spin == 0f) throw new Exception($"{tex} does not spin"); spun++; }
                else { if (spin != 0f) throw new Exception($"{tex} spins and should not"); still++; }
            }
            if (spun != 1) throw new Exception($"expected exactly one swirl surface, found {spun}");
            GD.Print($"SCROLLING TEXTURE PASS: clock round-trips, {spun} swirl and {still} still surfaces on the Coconut");
            _checks += spun + still;
        }
        finally { built.Root.Free(); probe.Dispose(); }
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
            return cache[material] = (texture, data.Translucent);
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
