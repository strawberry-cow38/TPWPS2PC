using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;
using System.Text.Json;

namespace TPWPS2Viewer.Tests;

/// <summary>Bounded, disc-backed rendering proof. No gameplay, audio or native export.</summary>
public partial class BouncyCowPreview : Node3D
{
    const string DefaultArt = "/home/ec2-user/astraclaw/astraclaw/state/scratch/1492558787561914542/bouncy-cow-output/bouncy-cow.glb";
    readonly List<Resource> owned = new();
    readonly List<object> materials = new();
    readonly List<object> outputs = new();
    Node3D cow, reference;
    Camera3D camera;
    Label label;
    int draws;
    bool finished;
    string folder;
    static float[] V(Vector3 v) => new[] { v.X, v.Y, v.Z };
    static string Setting(string key, string fallback) => string.IsNullOrWhiteSpace(OS.GetEnvironment(key)) ? fallback : OS.GetEnvironment(key);
    static IEnumerable<MeshInstance3D> Meshes(Node n)
    {
        if (n is MeshInstance3D m) yield return m;
        foreach (var child in n.GetChildren()) foreach (var mesh in Meshes(child)) yield return mesh;
    }
    T Own<T>(T resource) where T : Resource { owned.Add(resource); return resource; }
    static void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }

    public override async void _Ready()
    {
        folder = Setting("TPW_COW_PREVIEW_OUTPUT", System.IO.Path.Combine(System.IO.Path.GetDirectoryName(DefaultArt)!, "in-port-preview"));
        Directory.CreateDirectory(folder);
        GetTree().CreateTimer(75).Timeout += () => { if (!finished) { GD.PrintErr("BouncyCowPreview watchdog expired"); GetTree().Quit(3); } };
        int exit = 2;
        object cowMetrics = null, referenceMetrics = null;
        int? cowOnlyTriangles = null;
        var visibility = new List<object>();
        string failure = null;
        try
        {
            Require(OS.GetEnvironment("TPW_PS2_YAW180") != "1", "preview requires one root Z reflection, not yaw180");
            Require(OS.GetEnvironment("TPW_PS2_CULL") is "" or "back", "preview requires backface culling");
            using var lib = new AssetLibrary(Setting("TPW_PS2_DISC", "/home/ec2-user/tpw-ps2/tpw_ps2.bin"));
            Ps2Materials.Lighting = Lighting.Read(lib.Disc);
            Ps2Materials.Bilinear = true;
            lib.OpenWad("/DATA/JUNGLE.WAD");
            var modelEntry = lib.Wad.Find("/Rides/Bouncy/bouncy.mps");
            var model = new Model(lib.Read(modelEntry));
            var aps = new Aps(lib.Read(lib.Wad.Find("/Rides/Bouncy/bouncy.aps")));
            var idle = aps.Records().Where(r => r.Slot == 2).First();
            var textures = new Dictionary<string, (ImageTexture, bool)>(StringComparer.OrdinalIgnoreCase);
            var drawn = new AnimatedModel(model, aps, idle, name =>
            {
                if (textures.TryGetValue(name, out var cached)) return cached;
                var data = lib.TextureNear(modelEntry.Path, name);
                Require(data != null, "missing reference texture " + name);
                using var image = Image.CreateFromData(data.Width, data.Height, false, Image.Format.Rgba8, data.Pixels);
                image.GenerateMipmaps();
                var tex = Own(ImageTexture.CreateFromImage(image));
                materials.Add(new { source = "disc", name, data.SourceWad, data.SourcePath, data.Width, data.Height, data.Translucent });
                return textures[name] = (tex, data.Translucent);
            }, nativeNodeVisibility: true);
            reference = drawn.Root; reference.Name = "BellyBounceReference"; AddChild(reference); drawn.SetFrame(0);
            referenceMetrics = Metrics(reference);
            var refMeshes = Meshes(reference).ToArray();
            foreach (var m in refMeshes)
            {
                Own(m.Mesh); if (m.MaterialOverride is ShaderMaterial sm && !owned.Contains(sm)) Own(sm);
            }
            Require(Triangles(reference, true) == 330, "Idle2/variant0/frame0 must draw 330 reference triangles");
            Require(refMeshes.Where(m => m.Name.ToString().StartsWith("egg#") || m.Name.ToString().StartsWith("shell06#")).All(m => !m.Visible), "construction egg/shell must be hidden");

            string art = Setting("TPW_COW_GLB", DefaultArt);
            Require(System.IO.Path.IsPathRooted(art), "GLB import must use absolute external path");
            using (var document = new GltfDocument())
            using (var state = new GltfState())
            {
                Require(document.AppendFromFile(art, state) == Error.Ok, "GLTFDocument import failed");
                cow = new Node3D { Name = "OriginalCowArt", Scale = new Vector3(1, 1, -1) };
                AddChild(cow);
                var imported = document.GenerateScene(state) as Node3D ?? throw new Exception("GLTF scene missing");
                cow.AddChild(imported);
            }
            foreach (var m in Meshes(cow))
            {
                Own(m.Mesh);
                for (int s = 0; s < m.Mesh.GetSurfaceCount(); s++)
                {
                    var original = m.GetActiveMaterial(s) as BaseMaterial3D ?? throw new Exception("GLTF baseColor material missing");
                    Own(original);
                    var texture = original.AlbedoTexture ?? throw new Exception("embedded baseColorTexture missing for " + m.Name);
                    if (!owned.Contains(texture)) Own(texture);
                    Require(original.AlbedoColor == Colors.White, "nonwhite GLTF baseColor factor would need texture baking");
                    var arrays = m.Mesh.SurfaceGetArrays(s);
                    Require(arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array().Length == arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length, "UVs missing");
                    Require(arrays[(int)Mesh.ArrayType.Normal].AsVector3Array().Length > 0, "GLTF normals missing");
                    var material = Own(new ShaderMaterial { Shader = Ps2Materials.Shader(false, "cull_back", rawNormals: false, linearFilter: Ps2Materials.Bilinear) });
                    material.SetShaderParameter("albedo_tex", texture);
                    material.SetShaderParameter("has_tex", true);
                    Ps2Materials.BindLight(material);
                    m.SetSurfaceOverrideMaterial(s, material);
                    materials.Add(new { source = "GLTF embedded", part = m.Name.ToString(), surface = s, baseColor = original.AlbedoColor.ToHtml(), textureWidth = texture.GetWidth(), textureHeight = texture.GetHeight(), rawNormals = false, bilinear = true, cull = "cull_back", uvPreserved = true, ambient = V(material.GetShaderParameter("ps2_ambient").AsVector3()), directional = V(material.GetShaderParameter("ps2_directional").AsVector3()), reflectedRay = V(material.GetShaderParameter("ps2_ray").AsVector3()) });
                }
            }
            cowMetrics = Metrics(cow);
            cowOnlyTriangles = Meshes(cow).Where(m => m.Name.ToString() != "corner_origin_3x4_platform" && !m.Name.ToString().Contains("fence") && !m.Name.ToString().Contains("sign")).Sum(m => Triangles(m, true));
            Require(cowOnlyTriangles == 216, "actual cow-only triangle count must be 216");
            Require(Triangles(cow, true) == 336 && Meshes(cow).Count() == 28, "unexpected actual GLB parts/triangles");
            var env = Own(new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.16f, .18f, .20f), AmbientLightSource = Godot.Environment.AmbientSource.Disabled, TonemapMode = Godot.Environment.ToneMapper.Linear });
            AddChild(new WorldEnvironment { Environment = env });
            camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 6.2f, Near = .05f, Far = 100, Current = true, Position = new Vector3(1.5f, .7f, -2) + new Vector3(5, 5, -8) };
            AddChild(camera); camera.LookAt(new Vector3(1.5f, .7f, -2));
            Require(camera.Position.IsFinite() && camera.GlobalBasis.IsFinite(), "nonfinite camera");
            var ui = new CanvasLayer(); AddChild(ui);
            label = new Label { Position = new Vector2(24, 20) }; label.AddThemeFontSizeOverride("font_size", 24); ui.AddChild(label);
            Require(DisplayServer.GetName() != "headless", "Actual PNG capture requires a rendered display; headless metrics are not render proof.");
            reference.Visible = false; cow.Visible = true;
            label.Text = "Original Bouncy Cow  |  external GLB / port materials";
            using var baseline = await Capture("cow-in-port.png");
            // Actual framebuffer deltas with named parts suppressed: cull_back proof of visible features.
            foreach (var feature in new[] { "ground", "head", "eyes" })
            {
                var parts = Meshes(cow).Where(m => feature switch { "ground" => m.Name.ToString() == "corner_origin_3x4_platform", "head" => m.Name.ToString() == "happy_head", _ => m.Name.ToString().EndsWith("_eye") }).ToArray();
                Require(parts.Length > 0, "feature selection empty: " + feature);
                foreach (var p in parts) p.Visible = false;
                await Frames(4);
                using var without = GetViewport().GetTexture().GetImage();
                int changed = Difference(baseline, without);
                visibility.Add(new { feature, parts = parts.Select(p => p.Name.ToString()).ToArray(), changedPixels = changed, method = "cull_back framebuffer delta when suppressed" });
                foreach (var p in parts) p.Visible = true;
                Require(changed > 8, feature + " not visibly contributing with backface culling");
            }
            cow.Visible = false; reference.Visible = true;
            label.Text = "Belly Bounce  |  disc MPS + APS / Idle 2, variant 0, frame 0";
            using (await Capture("belly-bounce-reference.png")) { }
            cow.Visible = true;
            var right = camera.GlobalBasis.X.Normalized();
            cow.Position = -right * 2.85f; reference.Position = right * 2.85f;
            label.Text = "Original Bouncy Cow                                      Belly Bounce (disc reference)";
            using (await Capture("comparison-in-port.png")) { }
            exit = 0;
        }
        catch (Exception ex) { failure = ex.ToString(); GD.PrintErr("BOUNCY COW PREVIEW FAIL: " + failure); }
        finally
        {
            // Only this preview's nodes/resources. No Viewer, sound banks or AudioStreamPlayers instantiated.
            foreach (var child in GetChildren()) child.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (var resource in owned.DistinctBy(r => r.GetInstanceId()).Reverse()) if (IsInstanceValid(resource)) resource.Dispose();
            owned.Clear();
            var light = Ps2Materials.Lighting;
            var result = new { success = exit == 0, failure, glb = Setting("TPW_COW_GLB", DefaultArt), referenceName = "Belly Bounce", referencePose = new { slot = 2, variant = 0, frame = 0, nativeNodeVisibility = true }, cow = cowMetrics, cowOnlyTriangles, reference = referenceMetrics, rootReflection = new[] { 1, 1, -1 }, scalePolicy = "authored grid units; no normalization; same camera in all three views; comparison translations only", camera = new { position = new[] { 6.5f, 5.7f, -10f }, target = new[] { 1.5f, .7f, -2f }, orthographicSize = 6.2f, near = .05f, far = 100f, finite = true }, lighting = new { source = "Lighting.Read(AssetLibrary.Disc)", ambient = new[] { light.Ambient.X, light.Ambient.Y, light.Ambient.Z }, directional = new[] { light.Directional.X, light.Directional.Y, light.Directional.Z }, ray = new[] { light.RayDirection.X, light.RayDirection.Y, light.RayDirection.Z }, weatherAmount = Ps2Materials.DefaultWeatherAmount }, materials, visibility, outputs, realDrawFrames = draws, cleanup = "preview children QueueFree + two process frames; unique owned resources disposed; disc/library/document/state/images disposed; no audio nodes", displayBackend = DisplayServer.GetName(), visualReview = false, scope = "Static actual port render; not gameplay or native MPS/APS export" };
            File.WriteAllText(System.IO.Path.Combine(folder, "preview-results.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            finished = true;
            GD.Print($"BOUNCY COW PREVIEW {(exit == 0 ? "PASS" : "FAIL")} output={folder} drawFrames={draws}");
            GetTree().Quit(exit);
        }
    }
    async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) { await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); draws++; }
    }
    async Task<Image> Capture(string name)
    {
        await Frames(8);
        var image = GetViewport().GetTexture().GetImage();
        Require(image.GetWidth() == 1152 && image.GetHeight() == 648, "viewport must be 1152x648");
        string path = System.IO.Path.Combine(folder, name);
        Require(image.SavePng(path) == Error.Ok, "PNG save failed");
        outputs.Add(new { name, path, width = image.GetWidth(), height = image.GetHeight(), bytes = new FileInfo(path).Length });
        return image;
    }
    static int Difference(Image a, Image b)
    {
        byte[] x = a.GetData(), y = b.GetData(); int channels = x.Length / (a.GetWidth() * a.GetHeight()), changed = 0;
        for (int p = 0; p < x.Length; p += channels) if (Math.Abs(x[p] - y[p]) + Math.Abs(x[p+1] - y[p+1]) + Math.Abs(x[p+2] - y[p+2]) > 12) changed++;
        return changed;
    }
    static int Triangles(Node root, bool visible) => Meshes(root).Where(m => !visible || m.IsVisibleInTree()).Sum(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).Sum(s => { var a = m.Mesh.SurfaceGetArrays(s); int indices = a[(int)Mesh.ArrayType.Index].AsInt32Array().Length; return (indices > 0 ? indices : a[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length) / 3; }));
    static object Metrics(Node3D root)
    {
        var parts = new List<object>(); Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity), max = -min;
        foreach (var m in Meshes(root))
        {
            Vector3 lo = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity), hi = -lo; int tris = 0;
            for (int s = 0; s < m.Mesh.GetSurfaceCount(); s++)
            {
                var a = m.Mesh.SurfaceGetArrays(s); var vertices = a[(int)Mesh.ArrayType.Vertex].AsVector3Array(); int indices = a[(int)Mesh.ArrayType.Index].AsInt32Array().Length; tris += (indices > 0 ? indices : vertices.Length) / 3;
                foreach (var vertex in vertices) { var p = m.GlobalTransform * vertex; Require(p.IsFinite(), "nonfinite imported vertex"); lo = lo.Min(p); hi = hi.Max(p); }
            }
            if (m.IsVisibleInTree()) { min = min.Min(lo); max = max.Max(hi); }
            parts.Add(new { name = m.Name.ToString(), visible = m.IsVisibleInTree(), triangles = tris, surfaces = m.Mesh.GetSurfaceCount(), min = V(lo), max = V(hi) });
        }
        return new { meshInstances = parts.Count, visibleMeshInstances = Meshes(root).Count(m => m.IsVisibleInTree()), triangles = Triangles(root, false), visibleTriangles = Triangles(root, true), visibleBounds = new { min = V(min), max = V(max), sizeXYZ = V(max-min) }, parts };
    }
}
