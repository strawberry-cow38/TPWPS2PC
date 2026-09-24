using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>Synthetic PCM and actual Godot players; no disc clips are extracted.
/// This tests playback evidence and lifecycle, not audible quality or retail sound policy.</summary>
public partial class RideSoundLifecycleAudit : Node3D
{
    int _bad, _checks;
    void Check(bool ok, string message)
    {
        GD.Print((ok ? "AUDIO LIFECYCLE ok: " : "AUDIO LIFECYCLE FAIL: ") + $"[{++_checks}] {message}");
        if (!ok) _bad++;
    }
    static T Field<T>(object value, string name) => (T)value.GetType().GetField(name).GetValue(value);
    static void Set(object value, string name, object field) => value.GetType().GetField(name).SetValue(value, field);
    static object Start(RideSounds sounds, AudioStreamWav stream, int ride = 1, int tag = 10, bool loop = false, bool positional = false)
        => typeof(RideSounds).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(sounds, new object[] { ride, tag, "synthetic PCM", stream, loop, positional ? (int)SoundGroup.LocalRide : -1, Vector3.Zero, "synthetic lifecycle fixture" });
    static AudioStreamWav Wave(bool loop)
    {
        short[] samples = Enumerable.Range(0, 2205).Select(i => (short)(4000 * Math.Sin(i * .2))).ToArray();
        byte[] data = new byte[samples.Length * 2]; Buffer.BlockCopy(samples, 0, data, 0, data.Length);
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = 22050, Stereo = false, Data = data,
            LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
            LoopBegin = 0, LoopEnd = samples.Length,
        };
    }
    async Task WaitSeconds(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    public override async void _Ready()
    {
        try
        {
            using var shortWave = Wave(false); using var loopWave = Wave(true);
            int shortReferences = shortWave.GetReferenceCount(), loopReferences = loopWave.GetReferenceCount();
            AddChild(new Camera3D { Current = true, Position = new Vector3(0, 0, 2) });
            foreach (bool positional in new[] { false, true })
            {
                string kind = positional ? "3D" : "2D";
                var shortSounds = new RideSounds(this, null, null);
                var shortVoice = Start(shortSounds, shortWave, positional: positional);
                // Let the actual mixer finish, deliberately without any RideSounds.Step poll.
                for (int i = 0; i < 20 && !Field<bool>(shortVoice, "Finished"); i++) await WaitSeconds(.1);
                Check(Field<bool>(shortVoice, "Finished"), kind + " real mixer Finished signal arrives before first observation");
                shortSounds.Step(.2);
                Check(Field<bool?>(shortVoice, "PlayingAt1") == false, kind + " first observation occurs after the short clip stopped");
                Check(shortSounds.Verdicts == 1 && shortSounds.StartedVoices == 1 && shortSounds.SilentVoices == 0,
                      kind + " finished-before-first-observation counts as started");
                Check(shortSounds.Live == 0, kind + " finished one-shot is released after its verdict");

                var cancelled = new RideSounds(this, null, null);
                var cancelledVoice = Start(cancelled, loopWave, loop: true, positional: positional);
                Node player = Field<Node>(cancelledVoice, "Player");
                if (player is AudioStreamPlayer3D p3) p3.Stop(); else ((AudioStreamPlayer)player).Stop();
                for (int i = 0; i < 8; i++) cancelled.Step(.001);
                Check(cancelled.Verdicts == 0, kind + " eight fast no-evidence polls remain pending before the time budget");
                cancelled.Step(.48);
                Check(cancelled.Verdicts == 0, kind + " stopped voice is still pending before half a second");
                cancelled.Step(.02);
                Check(!Field<bool>(cancelledVoice, "Finished") && Field<float>(cancelledVoice, "MaxPosition") == 0,
                      kind + " negative control has neither a finish signal nor observed position advancement");
                Check(cancelled.Verdicts == 1 && cancelled.StartedVoices == 0 && cancelled.SilentVoices == 1,
                      kind + " no playback evidence does not become a false started verdict");
                Check(cancelled.Live == 0, kind + " stopped unobserved voice is eventually released");
            }

            var pending = new RideSounds(this, null, null);
            var pendingVoice = Start(pending, loopWave, loop: true);
            var pendingPlayer = (AudioStreamPlayer)Field<Node>(pendingVoice, "Player");
            pendingPlayer.Stop(); pending.Step(.001);
            // Controlled diagnostic-state input: "playing on first poll" alone
            // must not count as mixer evidence. The Finished/position tests above
            // and below use the real mixer; this one injects only that recorded flag.
            Set(pendingVoice, "PlayingAt1", (bool?)true);
            Check(Field<float>(pendingVoice, "MaxPosition") == 0 && !Field<bool>(pendingVoice, "Finished"),
                  "recorded acceptance control has no position or completion evidence");
            for (int i = 1; i < 8; i++) pending.Step(.001);
            Check(Field<bool?>(pendingVoice, "PlayingAt1") == true && pending.Verdicts == 0,
                  "recorded acceptance without consumption is not failed after eight fast frames");
            pending.Step(.48);
            Check(pending.Verdicts == 0, "recorded acceptance remains pending before diagnostic deadline");
            pending.Step(.02);
            Check(pending.Verdicts == 1 && pending.StartedVoices == 0 && pending.SilentVoices == 1,
                  "recorded acceptance without consumption fails after the elapsed observation budget");
            pending.Clear();

            var late = new RideSounds(this, null, null);
            var lateVoice = Start(late, loopWave, loop: true);
            var latePlayer = (AudioStreamPlayer)Field<Node>(lateVoice, "Player");
            latePlayer.Stop(); late.Step(.01); // first poll sees no playback, but not enough evidence to judge yet
            latePlayer.Play();
            for (int i = 0; i < 40 && latePlayer.GetPlaybackPosition() <= 0; i++) await WaitSeconds(.02);
            late.Step(.1);
            Check(Field<bool?>(lateVoice, "PlayingAt1") == false && Field<float>(lateVoice, "MaxPosition") > 0,
                  "late-start fixture observes actual advancement after an initially stopped poll");
            Check(late.StartedVoices == 1 && late.SilentVoices == 0,
                  "actual later position advancement overrides a negative first-frame observation");
            late.Clear();

            var scoped = new RideSounds(this, null, null);
            var a = Start(scoped, loopWave, 1, 10, true);
            var b = Start(scoped, loopWave, 1, 11, true);
            var c = Start(scoped, loopWave, 2, 10, true);
            int aEnds = 0, bEnds = 0, cEnds = 0;
            Set(a, "OnEnd", (Action)(() => aEnds++)); Set(b, "OnEnd", (Action)(() => bEnds++));
            Set(c, "OnEnd", (Action)(() => cEnds++));
            scoped.Kill(1, "synthetic ride", 10, 0);
            Check(scoped.Live == 2 && aEnds == 1 && bEnds == 0 && cEnds == 0
                  && Field<Node>(a, "Player").IsQueuedForDeletion()
                  && !Field<Node>(b, "Player").IsQueuedForDeletion() && !Field<Node>(c, "Player").IsQueuedForDeletion(), "Kill affects only the requested ride/tag and invokes its ending once");
            scoped.Kill(1, "synthetic ride", 10, 0);
            Check(scoped.Live == 2 && aEnds == 1 && bEnds == 0 && cEnds == 0
                  && Field<Node>(a, "Player").IsQueuedForDeletion()
                  && !Field<Node>(b, "Player").IsQueuedForDeletion() && !Field<Node>(c, "Player").IsQueuedForDeletion(), "repeated Kill does not repeat an ending");
            scoped.Drop(1);
            Check(scoped.Live == 1 && aEnds == 1 && bEnds == 0 && cEnds == 0
                  && Field<Node>(b, "Player").IsQueuedForDeletion() && !Field<Node>(c, "Player").IsQueuedForDeletion(), "ride removal drops its remaining voices without spawning end effects");
            scoped.Clear();
            Check(scoped.Live == 0 && aEnds == 1 && bEnds == 0 && cEnds == 0
                  && Field<Node>(c, "Player").IsQueuedForDeletion(), "world clear removes remaining voices without end effects");

            var fading = new RideSounds(this, null, null);
            var fade = Start(fading, loopWave, 3, 20, true);
            var otherRide = Start(fading, loopWave, 4, 20, true);
            var otherTag = Start(fading, loopWave, 3, 21, true);
            int fadeEnds = 0; Set(fade, "OnEnd", (Action)(() => fadeEnds++));
            fading.Fade(3, "synthetic ride", 20, 0);
            Check(fading.Live == 3 && fadeEnds == 0 && !Field<Node>(fade, "Player").IsQueuedForDeletion(),
                  "Fade is not an immediate Kill");
            float initialVolume = ((AudioStreamPlayer)Field<Node>(fade, "Player")).VolumeDb;
            fading.Step(.4);
            Check(fading.Live == 3 && fadeEnds == 0
                  && ((AudioStreamPlayer)Field<Node>(fade, "Player")).VolumeDb < initialVolume
                  && !Field<bool>(otherRide, "Fading") && !Field<bool>(otherTag, "Fading"),
                  "fade reduces volume over time without affecting another ride or tag");
            fading.Step(.4);
            Check(fading.Live == 2 && fadeEnds == 1
                  && Field<Node>(fade, "Player").IsQueuedForDeletion()
                  && !Field<Node>(otherRide, "Player").IsQueuedForDeletion()
                  && !Field<Node>(otherTag, "Player").IsQueuedForDeletion(), "fade completes once and leaves other ride/tag voices alone");
            fading.Step(.1);
            Check(fading.Live == 2 && fadeEnds == 1
                  && Field<Node>(fade, "Player").IsQueuedForDeletion()
                  && !Field<Node>(otherRide, "Player").IsQueuedForDeletion()
                  && !Field<Node>(otherTag, "Player").IsQueuedForDeletion(), "finished fade cannot repeat its ending");
            fading.Clear();

            // Exercise the real Viewer reset entry point without running _Ready,
            // loading a disc or entering an interactive viewer session.
            using (var viewer = new Viewer())
            {
                try
                {
                    const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
                    // _Ready normally parents these eagerly-created roots. This fixture
                    // skips _Ready, so supply ownership explicitly before freeing Viewer.
                    viewer.AddChild(((Weather)typeof(Viewer).GetField("_weather", hidden).GetValue(viewer)).Root);
                    viewer.AddChild(((EntranceFlags)typeof(Viewer).GetField("_flags", hidden).GetValue(viewer)).Root);
                    viewer.AddChild(((ThoughtBubbles)typeof(Viewer).GetField("_thoughts", hidden).GetValue(viewer)).Root);
                    var worldSounds = new RideSounds(this, null, null);
                    Start(worldSounds, loopWave, 8, 99, true);
                    typeof(Viewer).GetField("_sounds", hidden).SetValue(viewer, worldSounds);
                    var particles = new RideParticles(this, null);
                    var particleRoot = (Node3D)typeof(RideParticles).GetField("_root", hidden).GetValue(particles);
                    var liveParticles = (List<(CpuParticles3D Node, ulong Until)>)typeof(RideParticles)
                        .GetField("_live", hidden).GetValue(particles);
                    var particle = new CpuParticles3D { Emitting = false };
                    particleRoot.AddChild(particle); liveParticles.Add((particle, Time.GetTicksMsec() + 60000));
                    typeof(Viewer).GetField("_burst", hidden).SetValue(viewer, particles);
                    typeof(Viewer).GetMethod("MakePathTool", hidden).Invoke(viewer, null);
                    Check(worldSounds.Live == 0 && typeof(Viewer).GetField("_sounds", hidden).GetValue(viewer) == null,
                          "actual world reset stops old voices and releases the world-specific catalogue holder");
                    Check(ReferenceEquals(typeof(Viewer).GetField("_burst", hidden).GetValue(viewer), particles),
                          "world reset retains the reusable global particle-library holder");
                    Check(liveParticles.Count == 0 && particle.IsQueuedForDeletion(),
                          "world reset clears old live particles independently of retaining their global library");
                    worldSounds.Clear(); // fixture cleanup even while reproducing a reset omission
                    particles.Clear();
                }
                finally { if (GodotObject.IsInstanceValid(viewer)) viewer.Free(); }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(GetChildren().OfType<Node3D>().All(root => root.GetChildCount() == 0),
                  "freed audio players leave no child voices after deferred deletion");
            // Audio retirement belongs to the mixer, not the render-frame count.
            // Wait within a bounded budget and require the original native ownership.
            for (int i = 0; i < 20 && (shortWave.GetReferenceCount() != shortReferences
                    || loopWave.GetReferenceCount() != loopReferences); i++) await WaitSeconds(.05);
            Check(shortWave.GetReferenceCount() == shortReferences && loopWave.GetReferenceCount() == loopReferences,
                  $"mixer retires stopped playback references (short {shortWave.GetReferenceCount()}/{shortReferences}, loop {loopWave.GetReferenceCount()}/{loopReferences})");
            GD.Print(_bad == 0 ? "AUDIO LIFECYCLE PASS" : $"AUDIO LIFECYCLE FAIL: {_bad}");
            GetTree().Quit(_bad == 0 ? 0 : 2);
        }
        catch (Exception e) { GD.PrintErr("AUDIO LIFECYCLE ERROR: " + e); GetTree().Quit(2); }
    }
}
