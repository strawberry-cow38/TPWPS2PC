using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>Presentation of the managed park, using the existing terrain tile builder and RSSE
/// APS presenter. Characters are the disc's bind-pose meshes; seat attachments and gait skinning
/// are still absent. Every visible guest node is keyed by the simulation's guest ID.</summary>
public sealed class VisitorParkView : IDisposable
{
    public Park Park { get; } = new();
    public VisitorScenario Scenario { get; }
    public RseModelPresenter Ride { get; }
    readonly Dictionary<int, Node3D> _actors = new();
    public IReadOnlyDictionary<int, Node3D> Actors => _actors;
    readonly Dictionary<string, (ImageTexture, bool)> _textures = new();

    public VisitorParkView(Node3D parent, VisitorScenario scenario, AssetLibrary world, AssetLibrary data)
    {
        Scenario = scenario; parent.AddChild(Park.Root);
        var terrain = new AnimatedModel(scenario.Terrain, null, null,
            name => Texture(world, scenario.TerrainPath, name));
        terrain.SetFrame(0); Park.SetTerrain(terrain.Root);
        var plot = Park.AuthoredPlot(scenario.Terrain) ?? throw new InvalidDataException("No authored plot");
        // Same mirror and plot origin as the normal park viewer.
        Park.Origin = new Vector2(plot.Min.X, -plot.Max.Z); Park.BaseY = 0;
        Park.SetPaths(scenario.Simulation.Paths);
        var ground = new Dictionary<int, Material>();
        Park.MaterialForCell = index =>
        {
            if (ground.TryGetValue(index, out var material)) return material;
            var texture = index == 0 ? (null, false) : Texture(world, scenario.TerrainPath, scenario.Terrain.Materials[index]);
            material = new StandardMaterial3D { AlbedoTexture = texture.Item1,
                AlbedoColor = texture.Item1 == null ? new Color(0.30f, 0.40f, 0.25f) : Colors.White,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled, TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                Roughness = 1, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled };
            ground[index] = material; return material;
        };
        Park.Build(Park.Paths.Field.Width, Park.Paths.Field.Height);
        // Place once using the bind model. The holder keeps that transform when RSSE replaces
        // its child for a new APS record; construction debris cannot move the ride's origin.
        var holder = new Node3D { Name = "Orbiter" };
        var bind = new AnimatedModel(scenario.RideModel, null, null, _ => (null, false));
        bind.SetFrame(0); holder.AddChild(bind.Root);
        var fp = Park.Footprint.From(scenario.Definition.Shape);
        if (!Park.TryPlace(holder, fp, scenario.Definition.Id.Value, scenario.Definition.Name,
            scenario.RideOrigin.X, scenario.RideOrigin.Z)) throw new InvalidOperationException("Rendered ride placement rejected");
        bind.Root.Free();
        Ride = new RseModelPresenter(holder, scenario.RideModel, scenario.RideAnimation,
            name => Texture(world, scenario.RideStem + ".mps", name));
        foreach (var guest in scenario.Simulation.Visitors)
        {
            var model = new Model(data.Read(data.Wad.Find(guest.ModelPath)
                ?? throw new InvalidDataException($"Missing character {guest.ModelPath}")));
            var drawn = new AnimatedModel(model, null, null, name => Texture(data, guest.ModelPath, name));
            drawn.SetFrame(0);
            var actor = new Node3D { Name = $"Guest_{guest.Id}_{guest.Name}" };
            actor.SetMeta("guest_id", guest.Id); actor.SetMeta("model_path", guest.ModelPath);
            actor.AddChild(drawn.Root);
            var (lo, hi) = Park.DrawnBounds(drawn.Root, inParent: true);
            drawn.Root.Position = new Vector3(-(lo.X + hi.X) / 2, -lo.Y, -(lo.Z + hi.Z) / 2);
            actor.AddChild(new Label3D { Text = guest.Name, Position = new Vector3(0, hi.Y - lo.Y + 0.12f, 0),
                FontSize = 32, PixelSize = 0.01f, OutlineSize = 8, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true });
            Park.Root.AddChild(actor); _actors.Add(guest.Id, actor);
        }
        Update();
    }
    (ImageTexture, bool) Texture(AssetLibrary lib, string owner, string name)
    {
        string key = lib.WadName + owner + "|" + name;
        if (_textures.TryGetValue(key, out var cached)) return cached;
        var pixels = lib.TextureNear(owner, name);
        if (pixels == null) return (null, false);
        using var image = Image.CreateFromData(pixels.Width, pixels.Height, false, Image.Format.Rgba8, pixels.Pixels);
        var texture = (ImageTexture.CreateFromImage(image), pixels.PartialAlpha * 100 > pixels.Width * pixels.Height);
        _textures[key] = texture; return texture;
    }
    public void Update()
    {
        Ride.Update(Scenario.Simulation.Host);
        foreach (var guest in Scenario.Simulation.Visitors)
        {
            var actor = _actors[guest.Id]; var p = guest.Position;
            actor.Position = new Vector3(Park.Origin.X + p.X, Park.BaseY + p.Y, Park.Origin.Y + p.Z);
            actor.Visible = guest.State is VisitorState.Walking or VisitorState.Queuing or VisitorState.Alighting or VisitorState.Leaving;
            if (guest.NextCell is ParkCell next)
                actor.Rotation = new Vector3(0, Mathf.Atan2(next.X - guest.Cell.X, next.Z - guest.Cell.Z), 0);
        }
    }
    public void Dispose()
    {
        Ride?.Dispose(); Park.Root.Free();
        foreach (var value in _textures.Values) value.Item1.Dispose(); _textures.Clear();
    }
}
