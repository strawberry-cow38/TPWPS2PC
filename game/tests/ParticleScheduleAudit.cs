using System.Collections;
using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>DECLARED rendered component fixture for the native one-shot schedule in the renderer
/// (RideParticles.Emit -> ParticleTemplate.Plan -> ParticleBirthRuns), NOT gameplay or player proof: no
/// Viewer, map or retail pixels. Lives are drawn at random, as the console's are, so the checks are
/// properties over many emits (counts, ranges, starts and stops), never one exact draw.</summary>
public partial class ParticleScheduleAudit : Node3D
{
    readonly List<(RideParticles Fx, Node3D Holder)> _fixtures = new();
    int _checks;
    AssetLibrary _wad;
    Camera3D _camera;
    const float Tick = ParticleTemplate.TickMilliseconds / 1000f;
    void Check(bool ok, string message)
    {
        int n = ++_checks;
        GD.Print($"PARTICLE SCHEDULE {(ok ? "ok" : "FAIL")}: [{n}] {message}");
        if (!ok) throw new Exception(message);
    }
    static T Read<T>(object value, string name) => (T)(value.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(name)).GetValue(value);
    static int Count(RideParticles fx, string name) => Read<ICollection>(fx, name).Count;
    static CpuParticles3D[] Top(Node3D holder) => holder.GetNode<Node3D>("Particles")
        .GetChildren().OfType<CpuParticles3D>().ToArray();
    /// <summary>One emit's nodes: the first run and the later runs parented under it.</summary>
    static CpuParticles3D[] Runs(CpuParticles3D first) =>
        new[] { first }.Concat(first.GetChildren().OfType<CpuParticles3D>()).ToArray();
    (RideParticles Fx, Node3D Holder) Fixture(ParticleLibrary library)
    {
        var holder = new Node3D { Name = $"Fixture_{_fixtures.Count}" };
        AddChild(holder); // the holder must be in the tree before RideParticles is built or Emit runs
        var fixture = (new RideParticles(holder, library, _wad), holder);
        _fixtures.Add(fixture);
        return fixture;
    }
    public override void _Process(double delta)
    {
        foreach (var f in _fixtures) f.Fx.Step(delta); // real scaled process delta, as the Viewer drives it
    }
    async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
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

            // ⭐ LaserRing63: astraclaw's reading allows 1 or 2 births, the old estimate drew 5 (11 with the
            // scaled -2 interval). Forty emits, each its own node tree.
            var ring = ParticleTemplate.Of(library[63]);
            int one = 0, two = 0, tops = 0; bool singles = true, lifeInRange = true, waits = true;
            CpuParticles3D pairFirst = null, pairSecond = null;
            for (int i = 0; i < 40; i++)
            {
                var f = Fixture(library); f.Fx.Emit(63, new Vector3(i % 8 - 4, 1, -(i / 8)));
                var top = Top(f.Holder); tops += top.Length;
                if (top.Length != 1) continue;
                var runs = Runs(top[0]);
                if (runs.Length == 1) one++; else if (runs.Length == 2) two++;
                singles &= runs.All(n => n.Amount == 1);
                lifeInRange &= runs.All(n => n.Lifetime >= 16 * Tick - 1e-4f && n.Lifetime <= 24 * Tick + 1e-4f);
                if (runs.Length == 2)
                {
                    waits &= top[0].Emitting && !runs[1].Emitting;
                    pairFirst ??= top[0]; pairSecond ??= runs[1];
                }
            }
            Check(tops == 40 && one + two == 40 && singles,
                  $"LaserRing63 x40: one node tree per emit, 1 or 2 single-particle runs each, never 5 ({one} x1, {two} x2)");
            Check(one > 0 && two > 0, $"LaserRing63 both outcomes drawn over 40 emits ({one} one-birth, {two} two-birth)");
            Check(lifeInRange, "LaserRing63 every drawn particle lives 16..24 ticks, its own planned life");
            Check(waits && pairSecond != null, "LaserRing63 the replacement is parked at emit time while the first births at once");
            // The replacement waits for the first to die: born at tick L+2 against the first's life of L ticks.
            double firstLife = pairFirst.Lifetime;
            await Seconds(firstLife - 0.06f);
            Check(GodotObject.IsInstanceValid(pairSecond) && !pairSecond.Emitting,
                  $"LaserRing63 replacement still parked {firstLife - 0.06f:F2}s in, inside the first particle's {firstLife:F2}s life");
            await Seconds(0.06f + 3 * Tick + 0.15f);
            Check(GodotObject.IsInstanceValid(pairSecond) && pairSecond.Emitting,
                  "LaserRing63 replacement starts once the first has died (one alive at a time, cap 1)");
            Drawable(pairSecond, "LaserRing63 replacement run");

            // ⭐ FirePuff8: uncapped, so its plan is the same every time -- 22 births, against the old 28.
            var puff = ParticleTemplate.Of(library[8]);
            var puffPlan = puff.Plan(new Random(8));
            var puffRuns = ParticleBirthRuns.For(puff, puffPlan);
            bool puffOk = true; int puffDrawn = 0;
            for (int i = 0; i < 10; i++)
            {
                var f = Fixture(library); f.Fx.Emit(8, new Vector3(i - 5, 2, 0));
                var top = Top(f.Holder);
                if (top.Length != 1) { puffOk = false; continue; }
                var runs = Runs(top[0]);
                puffDrawn = runs.Sum(n => n.Amount);
                puffOk &= runs.Length == puffRuns.Count && puffDrawn == puffPlan.BirthTicks.Count
                          && top[0].Emitting && runs.Skip(1).All(n => !n.Emitting);
            }
            Check(puffOk && puffPlan.BirthTicks.Count == 22 && puff.ExpectedTotal() == 28,
                  $"FirePuff8 x10: {puffDrawn} particles in {puffRuns.Count} runs, the plan's {puffPlan.BirthTicks.Count}, not the old estimate {puff.ExpectedTotal()}");

            // ⭐ Flames55: cap 20 runs as 7, so births wait for deaths -- drawn one by one, each its own life.
            var flames = ParticleTemplate.Of(library[55]);
            int jitter = flames.Life >> 2, low = int.MaxValue, high = 0; bool flamesOk = true;
            for (int i = 0; i < 10; i++)
            {
                var f = Fixture(library); f.Fx.Emit(55, new Vector3(i - 5, 3, 0));
                var top = Top(f.Holder);
                if (top.Length != 1) { flamesOk = false; continue; }
                var runs = Runs(top[0]);
                int births = runs.Sum(n => n.Amount);
                // ⚠ The tree is the first run plus exactly one child per later run -- a copy made after
                // children were attached carries them along, and that once grew to 2^15 nodes here.
                flamesOk &= top[0].GetChildren().OfType<CpuParticles3D>().All(c => c.GetChildCount() == 0);
                low = Math.Min(low, births); high = Math.Max(high, births);
                flamesOk &= runs.All(n => n.Amount == 1 && n.Lifetime >= (flames.Life - jitter + 1) * Tick - 1e-4f
                                          && n.Lifetime <= (flames.Life + jitter - 1) * Tick + 1e-4f);
            }
            // ⚠ The bound is DERIVED, not sampled: this once said 14..18, the range 50 census plans happened to
            // show, and the first rerun drew 19. Cap c slots fill on ticks 1..c; a slot born at tick k is
            // reborn at k + life + 2. Every slot is reborn at least once even at the longest life, and none
            // a third time even at the shortest, so births lie in [2c, 3c].
            int cap = ParticleTemplate.DensityScaled(flames.MaxLive, flames.EffectiveDensity());
            int lmin = flames.Life - jitter + 1, lmax = flames.Life + jitter - 1, span = flames.EmitterLife;
            bool derived = cap + lmax + 2 <= span && 1 + 3 * (lmin + 2) > span;
            Check(flamesOk && derived && low >= 2 * cap && high <= 3 * cap,
                  $"Flames55 x10: cap-gated, single-particle runs with their own lives, {low}..{high} births within the derived {2 * cap}..{3 * cap} (old estimate {flames.ExpectedTotal()})");

            // ⭐ The continuous path is untouched apart from honouring NoDensityScaling: Bubbles keeps its -5.
            var bubbles = ParticleTemplate.Of(library[58]);
            var b = Fixture(library); b.Fx.Emit(58, new Vector3(0, 4, 0));
            var bubbleTop = Top(b.Holder);
            Check(bubbleTop.Length == 1 && !bubbleTop[0].OneShot && Runs(bubbleTop[0]).Length == 1
                  && bubbleTop[0].Amount == bubbles.SteadyPopulation() && bubbles.NoDensityScaling
                  && Math.Abs(bubbles.SteadyRatePerSecond() - 1f / (5 * Tick)) < 1e-3f,
                  $"Bubbles58 continuous: one looping node of {bubbleTop[0].Amount}, its unscaled -5 interval kept");

            // ⭐ A plan that births nothing draws nothing, but its child is still made at spawn, as on the
            // console. Cloned data: Destroy75 cut to a one-tick emitter, which births at its last quarter's
            // rate -- 0 for Destroy75.
            var silent = new ParticleLibrary((byte[])plb.Clone());
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(silent[75].Raw.AsSpan(0x20, 4), 1);
            var s = Fixture(silent); s.Fx.Emit(75, new Vector3(0, 5, 0));
            var silentTop = Top(s.Holder);
            Check(ParticleTemplate.Of(silent[75]).Plan(new Random(1)).BirthTicks.Count == 0
                  && silentTop.Length == 1 && silentTop[0].Name.ToString().StartsWith("Fx_83_"),
                  "CLONED DATA a plan with no births draws no parent but still spawns its Twinkle83 child");

            // ⭐ A parked run must die with its parent, before its timer, without touching a freed node.
            var gone = Fixture(library); gone.Fx.Emit(8, Vector3.Zero);
            var goneNodes = Runs(Top(gone.Holder)[0]);
            gone.Fx.Clear();
            await Seconds(0.5f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(goneNodes.All(n => !GodotObject.IsInstanceValid(n)) && Count(gone.Fx, "_live") == 0,
                  $"FirePuff8 cleared at once: all {goneNodes.Length} runs freed, parked timers fire on nothing");

            // ⭐ And a finished one retires through normal process time, children with it.
            var done = Fixture(library); done.Fx.Emit(8, new Vector3(3, 0, 0));
            var doneNodes = Runs(Top(done.Holder)[0]);
            await Seconds(puff.Life * Tick + puffPlan.BirthTicks.Max() * Tick + 0.9f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(doneNodes.All(n => !GodotObject.IsInstanceValid(n)) && Top(done.Holder).Length == 0
                  && Count(done.Fx, "_live") == 0,
                  "FirePuff8 retires parent and parked runs together once its last particle is done");

            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            Check(_camera.Current && image != null && !image.IsEmpty(), "camera viewport completed actual render frames");
            passed = true;
        }
        catch (Exception e) { GD.PrintErr("PARTICLE SCHEDULE FAIL: " + e); }
        finally
        {
            try
            {
                var nodes = _fixtures.SelectMany(f => Top(f.Holder)).SelectMany(Runs).ToArray();
                foreach (var f in _fixtures) f.Fx.Clear();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Check(nodes.All(n => !GodotObject.IsInstanceValid(n)) && _fixtures.All(f =>
                      Top(f.Holder).Length == 0 && Count(f.Fx, "_live") == 0
                      && Count(f.Fx, "_continuous") == 0 && Count(f.Fx, "_orbit") == 0),
                      "Clear plus two real frames retires every node, parked runs included");
            }
            catch (Exception e) { passed = false; GD.PrintErr("PARTICLE SCHEDULE cleanup FAIL: " + e); }
            try
            {
                foreach (var f in _fixtures)
                    if (GodotObject.IsInstanceValid(f.Holder)) f.Holder.QueueFree();
                if (GodotObject.IsInstanceValid(_camera)) _camera.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Check(_fixtures.All(f => !GodotObject.IsInstanceValid(f.Holder)), "holders queued and retired normally");
                _wad?.Dispose();
            }
            catch (Exception e) { passed = false; GD.PrintErr("PARTICLE SCHEDULE teardown FAIL: " + e); }
            _fixtures.Clear();
        }
        if (passed) GD.Print($"PARTICLE SCHEDULE PASS checks={_checks}; explicit component fixture");
        GetTree().Quit(passed ? 0 : 1);
    }
    void Drawable(CpuParticles3D node, string label)
    {
        Check(node.Mesh is QuadMesh && node.Mesh.SurfaceGetMaterial(0) is ShaderMaterial
              && node.IsInsideTree() && node.IsVisibleInTree() && node.Amount > 0
              && node.ScaleAmountMax > 0 && node.ColorRamp.Colors.Any(c => c.A > 0),
              label + " is a drawable CPU node in the tree with a shader material");
    }
}
