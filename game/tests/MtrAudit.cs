using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>Checks the four named MD2 witnesses through production AnimatedModel surfaces.
/// This is binding/geometry validation, not a framebuffer or original-game comparison.</summary>
public partial class MtrAudit : Node
{
    public override void _Ready()
    {
        try
        {
            using var lib = new AssetLibrary(OS.GetEnvironment("TPW_PS2_DISC"));
            lib.OpenWad("/DATA/JUNGLE.WAD");
            Run(lib, "Sideshow/sgrace/sgrace.MD2", "new02", 10, "dinohead1");
            Run(lib, "Sideshow/sgsquark/glove.MD2", "ears01", 0, "gloves");
            Run(lib, "Sideshow/sgsquark/sgsquark.MD2", "sq_body", 11, "sq_feather");
            Run(lib, "Sideshow/sgwhack/sgwhack.MD2", "hammer", 5, "gb_hammer2");
            GD.Print("MTR SURFACES PASS: all four named mesh/material/texture witnesses, geometry and transforms");
            GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PrintErr("MTR SURFACES FAIL: " + ex); GetTree().Quit(2); }
    }

    void Run(AssetLibrary lib, string path, string meshName, int slot, string textureName)
    {
        var ride = lib.Rides.Single(r => r.Name == path);
        if (ride.Animation != null) throw new Exception("legacy MD2 must not inherit the MPS animation");
        var model = lib.LoadModel(ride);
        if (model.Companion == null) throw new Exception("viewer model path did not load MTR");
        var cache = new Dictionary<string, (ImageTexture Tex, bool Soft)>();
        (ImageTexture, bool) Texture(string name)
        {
            if (cache.TryGetValue(name, out var found)) return found;
            var data = lib.TextureNear(ride.Model.Path, name);
            if (data == null) return (null, false);
            using var image = Image.CreateFromData(data.Width, data.Height, false, Image.Format.Rgba8, data.Pixels);
            var texture = ImageTexture.CreateFromImage(image);
            texture.ResourceName = System.IO.Path.GetFileNameWithoutExtension(data.SourcePath);
            return cache[name] = (texture, data.PartialAlpha * 100 > data.Width * data.Height);
        }
        var drawn = new AnimatedModel(model, null, null, Texture);
        AddChild(drawn.Root);
        try
        {
            drawn.SetFrame(0);
            var bindings = model.Meshes.SelectMany(m => model.Triangles(m).GroupBy(t => t.Material)
                .Select(g => (Mesh: m, Material: g.Key, Triangles: g.ToArray()))).ToArray();
            int index = Array.FindIndex(bindings, b => b.Mesh.Name == meshName && b.Material == slot);
            if (index < 0) throw new Exception($"no surface for {path}/{meshName}/{slot}");
            var surface = drawn.Root.GetChildren().OfType<MeshInstance3D>().ElementAt(index);
            var shader = (ShaderMaterial)surface.MaterialOverride;
            var bound = shader.GetShaderParameter("albedo_tex").AsGodotObject() as ImageTexture;
            if (bound?.ResourceName != textureName || !shader.GetShaderParameter("has_tex").AsBool())
                throw new Exception($"{meshName}: expected {textureName}, bound {bound?.ResourceName}");
            var mesh = bindings[index].Mesh;
            var expected = model.Vertices(mesh).Pos;
            var arrays = surface.Mesh.SurfaceGetArrays(0);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var triangle = bindings[index].Triangles[0];
            // The renderer may reverse winding; identify all three points of this specific face.
            var want = new[] { expected[triangle.A], expected[triangle.B], expected[triangle.C] }
                .Select(v => new Vector3(v.X, v.Y, v.Z)).ToArray();
            if (vertices.Length < 3 || !vertices.Take(3).All(v => want.Any(w => w.IsEqualApprox(v))))
                throw new Exception($"{meshName}: selected surface has another face's geometry");
            var world = model.WorldTransforms()[mesh.Offset];
            if (!surface.Transform.Origin.IsEqualApprox(new Vector3(world.M41, world.M42, world.M43)) ||
                drawn.Root.Scale != new Vector3(1, 1, -1))
                throw new Exception($"{meshName}: hierarchy/coordinate conversion not applied");
            GD.Print($"BINDING {path}/{meshName} -> {textureName}");
        }
        finally
        {
            drawn.Root.Free();
            foreach (var texture in cache.Values) texture.Tex.Dispose();
        }
    }
}
