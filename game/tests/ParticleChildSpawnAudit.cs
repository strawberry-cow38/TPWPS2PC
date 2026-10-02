using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>DECLARED rendered component fixture, NOT gameplay/player proof. BYO read-only disc;
/// no Viewer/audio, attachment following, native scheduler or 83->84 death-chain claim.
/// Cloned raw records below are explicitly synthetic controls, never production state writes.</summary>
public partial class ParticleChildSpawnAudit : Node3D
{
    readonly List<(RideParticles Fx, Node3D Holder)> _fixtures = new();
    int _checks;
    AssetLibrary _wad;
    Camera3D _camera;
    void Check(bool ok, string message)
    {
        int n = ++_checks;
        GD.Print($"PARTICLE CHILD SPAWN {(ok ? "ok" : "FAIL")}: [{n}] {message}");
        if (!ok) throw new Exception(message);
    }
    static T Read<T>(object value, string name) => (T)(value.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(name)).GetValue(value);
    static int Count(RideParticles fx, string name) => Read<ICollection>(fx, name).Count;
    static CpuParticles3D[] Nodes(Node3D holder) => holder.GetNode<Node3D>("Particles")
        .GetChildren().OfType<CpuParticles3D>().ToArray();
    static bool Near(Vector3 a, Vector3 b) => a.DistanceTo(b) < .0001f;
    static void Word(ParticleEffect e, int at, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(e.Raw.AsSpan(at, 4), value);
    static void Half(ParticleEffect e, int at, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(e.Raw.AsSpan(at, 2), value);
    (RideParticles Fx, Node3D Holder) Fixture(ParticleLibrary library)
    {
        var holder = new Node3D { Name = $"Fixture_{_fixtures.Count}", Position = new Vector3(.5f, 0, 0) };
        AddChild(holder); // normal tree attachment MUST precede RideParticles construction/Emit
        var fixture = (new RideParticles(holder, library, _wad), holder);
        _fixtures.Add(fixture);
        return fixture;
    }
    public override void _Process(double delta)
    {
        foreach (var f in _fixtures) f.Fx.Step(delta); // real scaled process delta; no manual sim ticks
    }
    void Drawable(CpuParticles3D node, string label)
    {
        Check(node.Mesh is QuadMesh && node.Mesh.SurfaceGetMaterial(0) is ShaderMaterial,
              label + " CPU node has mesh and shader material");
        var mat = (ShaderMaterial)node.Mesh.SurfaceGetMaterial(0);
        var tex = mat.GetShaderParameter("albedo_tex").AsGodotObject() as Texture2D;
        Check(mat.GetShaderParameter("textured").AsBool() && tex != null
              && tex.GetWidth() > 0 && tex.GetHeight() > 0,
              label + " real nonmissing PARTICLE.WAD sprite (not fallback dot)");
        Check(node.IsInsideTree() && node.IsVisibleInTree() && node.Emitting && node.Amount > 0
              && node.ScaleAmountMax > 0 && node.ColorRamp.Colors.Any(c => c.A > 0)
              && (node.Layers & _camera.CullMask) != 0 && _camera.IsPositionInFrustum(node.GlobalPosition),
              label + " effective tree/layer/frustum visibility and nonzero particle appearance");
    }
    public override async void _Ready()
    {
        bool passed = false;
        try
        {
            Check(DisplayServer.GetName() != "headless", "render/display required (not a headless proof)");
            _camera = new Camera3D { Current = true, Position = new Vector3(0, 4, 14) };
            AddChild(_camera); _camera.LookAt(new Vector3(0, 1, 0));
            string disc = OS.GetEnvironment("TPW_PS2_DISC");
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--disc" && i + 1 < args.Length) disc = args[++i];
                else if (args[i].StartsWith("--disc=", StringComparison.Ordinal)) disc = args[i][7..];
            Check(!string.IsNullOrWhiteSpace(disc), "disc provided by --disc or TPW_PS2_DISC");
            _wad = new AssetLibrary(disc); _wad.OpenWad("/DATA/PARTICLE.WAD");
            byte[] plb = _wad.Read(_wad.Wad.Find("/Tp2.plb"));
            var library = new ParticleLibrary(plb);
            foreach (int id in new[] { 75, 76, 77 })
            {
                Check(library[id].Name.StartsWith("Destroy")
                      && ParticleSpawnLinks.TryTwinkleChild(library, id, out var request)
                      && request == new ParticleSpawnLinks.ChildRequest(83, 0, 625, 0),
                      $"shipped Destroy{id} metadata requests attached immediate 83 at (0,625,0)");
                var f = Fixture(library); var where = new Vector3(id - 76, .25f, 0);
                var returned = f.Fx.Emit(id, where);
                var nodes = Nodes(f.Holder);
                Check(ReferenceEquals(returned, library[id]) && nodes.Length == 2 && f.Fx.Spawned == 2,
                      $"Destroy{id} API returns parent and creates exactly parent plus 83");
                var parent = nodes.Single(n => n.Name.ToString() == $"Fx_{id}_{library[id].Name}");
                var child = nodes.Single(n => n.Name.ToString() == $"Fx_83_{library[83].Name}");
                Check(Near(parent.Position, where) && Near(child.Position, where + new Vector3(0, 625f / 640, 0))
                      && Near(child.GlobalPosition, f.Holder.GlobalPosition + child.Position),
                      $"Destroy{id} actual CPU local/global positions use child offset at offzero fitting");
                Drawable(parent, $"Destroy{id}"); Drawable(child, $"Destroy{id}/83");
            }
            var probe = Fixture(library); probe.Fx.Emit(75, new Vector3(-2, 0, 0), probe: 1);
            Check(Nodes(probe.Holder).Length == 1 && Nodes(probe.Holder)[0].Name.ToString().StartsWith("Fx_75_"),
                  "probe1 isolates parent; no immediate child");
            foreach (var link in new[] { (4, 5), (51, 54), (60, 63), (62, 63), (78, 85) })
            {
                var f = Fixture(library); f.Fx.Emit(link.Item1, Vector3.Zero);
                Check(Nodes(f.Holder).Length == 1 && Nodes(f.Holder)[0].Name.ToString().StartsWith($"Fx_{link.Item1}_"),
                      $"Emit {link.Item1} introduces no child (including no LaserRing63 for 60/62)");
                Check(ParticleSpawnLinks.TryParticleChild(library, link.Item1, out var req)
                      && req.EffectId == link.Item2 && !ParticleSpawnLinks.TryTwinkleChild(library, link.Item1, out _),
                      $"native {link.Item1}->{link.Item2} known but deliberately OFF");
            }
            foreach (int id in new[] { 97, 99 })
            {
                var f = Fixture(library); var result = f.Fx.Emit(id, Vector3.Zero);
                Check(Nodes(f.Holder).Length == (result == null ? 0 : 1), $"{id} has no arbitrary child hook");
            }
            foreach (bool attach in new[] { true, false })
            {
                // DATA FIXTURE: cloned records, distinct parent/child offset words and explicit finite child.
                var clone = new ParticleLibrary((byte[])plb.Clone());
                Word(clone[75], 0x2c, 3200); Word(clone[75], 0x30, -640); Word(clone[75], 0x34, 640);
                Word(clone[75], 0xb0, 128); Half(clone[75], 0xb8, (short)(attach ? 1 : 0));
                Word(clone[83], 0x2c, 640); Word(clone[83], 0x30, 1280); Word(clone[83], 0x34, -1920);
                Half(clone[83], 0x26, 0); Word(clone[83], 0x38, 0); Word(clone[83], 0x3c, 64);
                Word(clone[83], 0x40, 0); Word(clone[83], 0xb0, 128); Word(clone[83], 0x5c, 0);
                var f = Fixture(clone); var where = new Vector3(-1, .5f, -1);
                var result = f.Fx.Emit(75, where, Vector3.Right, persistent: true);
                var parent = Nodes(f.Holder).Single(n => n.Name.ToString().StartsWith("Fx_75_"));
                var child = Nodes(f.Holder).Single(n => n.Name.ToString().StartsWith("Fx_83_"));
                Check(ReferenceEquals(result, clone[75]) && Near(parent.Position, where)
                      && Near(child.Position, where + (attach ? new Vector3(1, 2, 3) : Vector3.Zero)),
                      $"CLONED DATA attachment {attach}: child words not parent words; handedness /640 conversion");
                Check(!parent.OneShot && Near(parent.Direction, Vector3.Right) && child.OneShot
                      && Near(child.Direction, Vector3.Up) && Count(f.Fx, "_continuous") == 1
                      && Read<List<(CpuParticles3D Node, ulong Until)>>(f.Fx, "_live").Any(x => x.Node == child),
                      "CLONED DATA finite83 inherits neither parent persistent flag nor directional input");
                Drawable(child, "CLONED DATA 83");
            }
            // DATA FIXTURE routing refusals; these are not claims about retail Destroy records.
            foreach (var route in new[] { (-1, (byte)0), (75, (byte)0), (500, (byte)0), (83, (byte)1) })
            {
                var clone = new ParticleLibrary((byte[])plb.Clone());
                Word(clone[75], 0xb4, route.Item1); clone[75].Raw[0xba] = route.Item2;
                var f = Fixture(clone); var result = f.Fx.Emit(75, Vector3.Zero);
                Check(!ParticleSpawnLinks.TryTwinkleChild(clone, 75, out _)
                      && ReferenceEquals(result, clone[75]) && Nodes(f.Holder).Length == 1,
                      $"CLONED DATA no-child/self/attractor refusal ({route.Item1}, {route.Item2})");
            }
            var self83 = new ParticleLibrary((byte[])plb.Clone()); Word(self83[83], 0xb4, 83);
            var selfFixture = Fixture(self83); selfFixture.Fx.Emit(83, Vector3.Zero);
            Check(Nodes(selfFixture.Holder).Length == 1 && selfFixture.Fx.Spawned == 1,
                  "CLONED DATA supported child83 self-link cannot recurse");
            // DATA FIXTURE short finite clocks. Retirement is observed through NORMAL _Process,
            // a real timer and queued frames, not a forced deadline or manually invoked Step.
            var shortLife = new ParticleLibrary((byte[])plb.Clone());
            foreach (int id in new[] { 75, 83 })
            { Word(shortLife[id], 0x20, 1); Half(shortLife[id], 0x78, 1); Half(shortLife[id], 0x7a, 0); }
            // ⚠ The renderer now follows the native schedule (tinyclaw, 2026-10-02): a one-tick emitter
            // births on its only tick at the LAST quarter's rate, and Destroy75's is 0, so this clone's
            // parent would birth nothing and draw no node. The old estimate drew at least one regardless.
            // Give the clone a last-quarter rate of 1 so the parent really births and this control still
            // retires a parent AND a child.
            shortLife[75].Raw[0x6b] = 1;
            var timed = Fixture(shortLife); timed.Fx.Emit(75, Vector3.Zero);
            var timedNodes = Nodes(timed.Holder);
            Check(timedNodes.Length == 2, "CLONED DATA finite retirement control actually creates parent and child");
            await ToSignal(GetTree().CreateTimer(.7), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(timedNodes.All(n => !GodotObject.IsInstanceValid(n)) && Nodes(timed.Holder).Length == 0
                  && Count(timed.Fx, "_live") == 0,
                  "CLONED DATA finite parent and child retire through normal process/time");
            await ToSignal(GetTree().CreateTimer(.08), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            Check(_camera.Current && image != null && !image.IsEmpty(), "camera viewport completed actual render frames");
            passed = true;
        }
        catch (Exception e) { GD.PrintErr("PARTICLE CHILD SPAWN FAIL: " + e); }
        finally
        {
            try
            {
                var nodes = _fixtures.SelectMany(f => Nodes(f.Holder)).ToArray();
                foreach (var f in _fixtures) f.Fx.Clear();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Check(nodes.All(n => !GodotObject.IsInstanceValid(n)) && _fixtures.All(f =>
                      Nodes(f.Holder).Length == 0 && Count(f.Fx, "_live") == 0
                      && Count(f.Fx, "_continuous") == 0 && Count(f.Fx, "_orbit") == 0),
                      "Clear plus two real frames retires every finite/continuous node and lifecycle map");
            }
            catch (Exception e) { passed = false; GD.PrintErr("PARTICLE CHILD SPAWN cleanup FAIL: " + e); }
            try
            {
                // Queue teardown even when a preceding assertion failed.
                foreach (var f in _fixtures)
                    if (GodotObject.IsInstanceValid(f.Holder)) f.Holder.QueueFree();
                if (GodotObject.IsInstanceValid(_camera)) _camera.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Check(_fixtures.All(f => !GodotObject.IsInstanceValid(f.Holder)), "holders queued and retired normally");
                _wad?.Dispose();
            }
            catch (Exception e) { passed = false; GD.PrintErr("PARTICLE CHILD SPAWN teardown FAIL: " + e); }
            _fixtures.Clear();
        }
        if (passed) GD.Print($"PARTICLE CHILD SPAWN PASS checks={_checks}; explicit component fixture");
        GetTree().Quit(passed ? 0 : 1);
    }
}
