using Godot;
using TPW.PS2.Data;
using NVector = System.Numerics.Vector3;

namespace TPWPS2Viewer.Tests;

/// <summary>Render the actual AnimatedModel material on controlled patches carrying named disc
/// vertices and texels. Exit 2 on missing geometry, wrong normal bytes, light binding or pixels.</summary>
public partial class LightingAudit : Node
{
    public override async void _Ready()
    {
        try
        {
            string discPath = OS.GetEnvironment("TPW_PS2_DISC");
            using var disc = new Disc(discPath);
            Ps2Materials.Lighting = Lighting.Read(disc);
            using var lib = new AssetLibrary(discPath);
            foreach (var fixture in new[] {
                (World: "SPACE", Mesh: "CLIFFS", Texture: "sfl_bas1.ssh", Normal: new NVector(66,108,-4), Gs: 100, X: 0, Y: 1, Texel: new Vector3I(101,26,144), Output: new Vector3I(78,20,112)),
                (World: "HALLOW", Mesh: "road_rhs01", Texture: "grd_ctr1.ssh", Normal: new NVector(0,127,0), Gs: 119, X: 0, Y: 0, Texel: new Vector3I(59,58,69), Output: new Vector3I(54,53,64)),
                (World: "HALLOW", Mesh: "ticket_booths", Texture: "gte_rof1.ssh", Normal: new NVector(83,-21,93), Gs: 64, X: 52, Y: 24, Texel: new Vector3I(89,86,78), Output: new Vector3I(44,43,39)),
                (World: "JUNGLE", Mesh: "A_ROAD", Texture: "grd_ctr1.ssh", Normal: new NVector(0,127,0), Gs: 119, X: 0, Y: 0, Texel: new Vector3I(59,58,69), Output: new Vector3I(54,53,64)),
                (World: "FANTASY", Mesh: "gatebase01", Texture: "jgr_bas1.ssh", Normal: new NVector(0,-127,0), Gs: 119, X: 0, Y: 0, Texel: new Vector3I(8,162,4), Output: new Vector3I(7,150,3)) })
            {
                lib.OpenWad($"/DATA/{fixture.World}.WAD");
                var entry = lib.Wad.Entries.Single(e => e.Path.Equals("/terrain/terrain_1.mps", StringComparison.OrdinalIgnoreCase));
                var model = new Model(lib.Read(entry));
                var mesh = model.Meshes.Single(m => m.Name == fixture.Mesh);
                var tri = model.Triangles(mesh)[0];
                Require(model.Materials[tri.Material] == fixture.Texture, fixture.Mesh + " material changed");
                var raw = model.Vertices(mesh).Normal[tri.A] * 127;
                raw = new(MathF.Round(raw.X), MathF.Round(raw.Y), MathF.Round(raw.Z));
                Require(raw == fixture.Normal, fixture.Mesh + " normal identity");
                var gs = Ps2Materials.Lighting.VertexColour(raw, model.WorldTransforms()[mesh.Offset]);
                Require(gs == new NVector(fixture.Gs), fixture.Mesh + " GS colour identity");
                var texture = lib.TextureNear(entry.Path, fixture.Texture) ?? throw new Exception("missing fixture texture");
                int at = (fixture.Y * texture.Width + fixture.X) * 4;
                var texel = new Vector3I(texture.Pixels[at], texture.Pixels[at+1], texture.Pixels[at+2]);
                Require(texel == fixture.Texel, fixture.Mesh + " texel identity");
                using var image = Image.CreateFromData(1, 1, false, Image.Format.Rgba8,
                    new byte[] { (byte)texel.X, (byte)texel.Y, (byte)texel.Z, 255 });
                using var onePixel = ImageTexture.CreateFromImage(image);
                var drawn = new AnimatedModel(model, null, null, _ => (onePixel, false));
                AddChild(drawn.Root);
                drawn.SetFrame(0);
                var actual = drawn.Root.GetChildren().OfType<MeshInstance3D>().Single(s => s.Name == fixture.Mesh + "#0");
                var custom = actual.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Custom0].AsFloat32Array();
                Require(custom.Length >= 3 && custom[0] == raw.X && custom[1] == raw.Y && custom[2] == raw.Z,
                    fixture.Mesh + " uploaded raw normals");
                var material = (ShaderMaterial)actual.MaterialOverride;
                if (OS.GetEnvironment("TPW_LIGHTING_MUTATION") == "drop-directional")
                {
                    string source = material.Shader.Code;
                    Require(source.Contains("ps2_directional * n_dot_l"), "mutation target missing");
                    material.Shader = new Shader { Code = source.Replace("ps2_directional * n_dot_l", "vec3(0.0) * n_dot_l") };
                    GD.Print("MUTATION: removed directional term from the actual surface shader");
                }
                // Real mirrored model basis, stripped of position so the patch is centred.
                var basis = actual.GlobalBasis;
                drawn.Root.Visible = false;
                await Probe($"{fixture.World}-{fixture.Mesh}", material, basis, raw, fixture.Output);
                // Blended twin must carry the same RGB equation. Alpha 1 isolates lighting.
                var soft = (ShaderMaterial)material.Duplicate();
                soft.Shader = Ps2Materials.Shader(true, "cull_back");
                await Probe($"{fixture.World}-{fixture.Mesh}-blend", soft, basis, raw, fixture.Output);
                drawn.Root.QueueFree();
            }
            // Synthetic floor follows the same byte-domain equation and ignores Godot ambient.
            using var greyImage = Image.CreateFromData(1,1,false,Image.Format.Rgba8,new byte[]{128,128,128,255});
            using var grey = ImageTexture.CreateFromImage(greyImage);
            await Probe("generated-floor", Ps2Materials.Ground(grey), Basis.Identity, new(0,127,0), new(119,119,119), customNormal: false);
            // A 126 byte must not be silently normalised to 127 by Godot or our vertex shader.
            var byteNormal = new ShaderMaterial { Shader = Ps2Materials.Shader(false, "cull_back") };
            byteNormal.SetShaderParameter("albedo_tex", grey);
            Ps2Materials.BindLight(byteNormal);
            await Probe("raw-byte-126", byteNormal, Basis.Identity, new(0,126,0), new(118,118,118));
            GD.Print("LIGHTING RENDER PASS: named surfaces, raw normal upload, opaque/blend, mirrored basis and generated floor");
            GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PrintErr("LIGHTING RENDER FAIL: " + ex); GetTree().Quit(2); }
    }

    static void Require(bool ok, string why) { if (!ok) throw new Exception(why); }

    async Task Probe(string label, ShaderMaterial material, Basis basis, NVector raw, Vector3I expected, bool customNormal = true)
    {
        var viewport = new SubViewport { Size = new Vector2I(96,96), OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport);
        var root = new Node3D(); viewport.AddChild(root);
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        if (customNormal) st.SetCustomFormat(0, SurfaceTool.CustomFormat.RgbFloat);
        foreach (var p in new[] {new Vector3(-1,-1,0),new Vector3(-1,1,0),new Vector3(1,1,0),
                                 new Vector3(-1,-1,0),new Vector3(1,1,0),new Vector3(1,-1,0)})
        {
            st.SetNormal(new Vector3(raw.X,raw.Y,raw.Z)/127);
            st.SetUV(Vector2.Zero);
            if (customNormal) st.SetCustom(0,new Color(raw.X,raw.Y,raw.Z,0));
            st.AddVertex(p);
        }
        var surface = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material, Basis = basis };
        root.AddChild(surface);
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal,
            Size = Math.Max(basis.X.Length(),basis.Y.Length())*3, Near = .0001f, Far = 1000,
            Position = basis * new Vector3(0,0,4), Current = true };
        root.AddChild(camera); camera.LookAt(Vector3.Zero, (basis * Vector3.Up).Normalized());
        root.AddChild(new WorldEnvironment { Environment = new Godot.Environment {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Magenta,
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.Green, AmbientLightEnergy = 8 } });
        for (int i=0;i<3;i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var result = viewport.GetTexture().GetImage();
        string output = OS.GetEnvironment("TPW_LIGHTING_ARTIFACTS");
        if (!string.IsNullOrWhiteSpace(output)) { Directory.CreateDirectory(output); result.SavePng(System.IO.Path.Combine(output,label.Replace(' ','_')+".png")); }
        var c = result.GetPixel(48,48);
        var got = new Vector3I(Mathf.RoundToInt(c.R*255),Mathf.RoundToInt(c.G*255),Mathf.RoundToInt(c.B*255));
        GD.Print($"PIXEL {label}: expected={expected} got={got}");
        Require(Math.Abs(got.X-expected.X)<=1 && Math.Abs(got.Y-expected.Y)<=1 && Math.Abs(got.Z-expected.Z)<=1, label+" framebuffer mismatch");
        viewport.QueueFree();
    }
}
