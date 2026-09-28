using Godot;
using TPW.PS2.Data;
using System.Text.Json;

namespace TPWPS2Viewer.Tests;

/// <summary>Run with --headless --audio-driver Dummy. Reads the owner disc through the existing
/// catalogue, never extracts it. File roundtrip, real scheduling ticks, isolated stage, new
/// providers, graph repeat/sustain/one-shot/fade and rejection. Ordinary-repeat coverage is not claimed.</summary>
public partial class RideSoundsSaveSmoke : Node
{
    int checks;
    void Check(bool ok,string why) { if(!ok) throw new Exception(why); checks++; }
    static string Json(object o) => JsonSerializer.Serialize(o);
    static void Pause(Node root)
    {
        foreach(var p in root.GetChildren()) {
            if(p is AudioStreamPlayer a) a.StreamPaused=true;
            if(p is AudioStreamPlayer3D b) b.StreamPaused=true;
            Pause(p);
        }
    }
    // Mixer cursor is wall-clock data: assert it separately with tolerance, never pretend it
    // advances deterministically with simulation ticks. All logical/diagnostic fields stay exact.
    static RideSounds.Snapshot Logical(RideSounds.Snapshot s) => s with {
        Voices=s.Voices.Select(v=>v with {Seek=0}).ToArray()
    };
    public override void _Ready()
    {
        try {
            using var library=new AssetLibrary("/home/ec2-user/tpw-ps2/tpw_ps2.bin");
            var cat=new SoundCatalogue(library.Disc,"JUNGLE",1);
            var parent=new Node3D();AddChild(parent);
            var sounds=new RideSounds(parent,cat,null,2718);
            int oldCalls=0,newCalls=0;
            Vector3 oldAt=new(1,2,3),newAt=oldAt;
            sounds.Follow(40,10,()=>{oldCalls++;return oldAt;});
            sounds.Follow(41,11,()=>null);
            sounds.ParameterValue=(r,p)=>0;
            var events=new List<SoundCatalogue.Resolved>();
            foreach(int kind in Enum.GetValues<SoundGroup>().Select(g=>(int)g))
                for(int id=0;id<256;id++) { var e=cat.Resolve(kind,id);if(e!=null) events.Add(e); }
            var graph=events.First(e=>e.Group==SoundGroup.GlobalRide && e.Id==93 && SfxEventMachine.IsGraph(e.Source) && (e.Flags&0x400)!=0 && e.Word0C>0);
            // The bounded catalogue census has no plain repeating event matching the old
            // fixture assumption. Exercise a SECOND real graph, rather than pretend it does.
            var repeat=events.First(e=>(e.Group!=graph.Group||e.Id!=graph.Id) && SfxEventMachine.IsGraph(e.Source) && (e.Flags&0x400)!=0 && e.Word0C>0);
            var sustain=events.First(e=>(e.Flags&0x408)==8);
            var once=events.First(e=>(e.Flags&0x408)==0 && e.Clips.Count>0);
            var choices=new[]{graph,repeat,sustain,once};
            for(int i=0;i<choices.Length;i++) {
                var e=choices[i];sounds.Cue(40+i,"save-smoke",0,i==3?RseOpcode.EVENT:RseOpcode.ADDOBJ,
                    (int)e.Group,-1,e.Id,10+i,oldAt);
            }
            Pause(parent);
            // A cut before the first link selection leaves the graph RNG at seed 1, so a
            // broken restore that resets it to 1 passes vacuously. Cross a REAL transition.
            int warm=0;
            while(sounds.CaptureState("disc/JUNGLE/park1/v1","world/sounds").GraphRandom==1 && warm++<2000) {
                sounds.AdvanceSim(.04);Pause(parent);
            }
            Check(sounds.CaptureState("disc/JUNGLE/park1/v1","world/sounds").GraphRandom!=1,"PRECONDITION: graph RNG consumed by real transition before cut");
            sounds.Step(.02);
            sounds.Fade(42,"save-smoke",12,40);sounds.Step(.1);
            var s=sounds.CaptureState("disc/JUNGLE/park1/v1","world/sounds");
            Check(s.Voices.Length>0 && s.Repeats.Length>=2,"nonvacuous real-bank graph and timers");
            Check(s.Voices.Any(v=>v.Fading) && s.Moving.Length==2,"mid-fade and moving bindings");
            string path=ProjectSettings.GlobalizePath("user://ride-sounds-smoke.save");
            try { ParkSaveFile.Write(path,s);s=ParkSaveFile.Read<RideSounds.Snapshot>(path); }
            finally { System.IO.File.Delete(path);System.IO.File.Delete(path+".bak"); }
            var assets=sounds.CaptureBindings(s.AssetsId,s.ServicesId);
            var bindings=new RideSounds.SnapshotBindings {AssetsId=assets.AssetsId,ServicesId=assets.ServicesId,
                Park1=cat,Park2=null,Events=assets.Events,Streams=assets.Streams,
                Moving=new Dictionary<RideSounds.OwnerKey,Func<Vector3?>> {
                    [new(40,10)]=()=>{newCalls++;return newAt;},[new(41,11)]=()=>null},ParameterValue=(r,p)=>0};
            string before=Json(Logical(sounds.CaptureState(s.AssetsId,s.ServicesId)));
            int calls=oldCalls;
            using var staged=RideSounds.FromState(s,bindings);
            Check(staged.IsDetachedAndStopped && oldCalls==calls && newCalls==0,"stage stopped; no delegates replayed");
            Check(before==Json(Logical(sounds.CaptureState(s.AssetsId,s.ServicesId))),"stage did not mutate old world");
            void Reject(RideSounds.Snapshot bad) {
                bool rejected=false;try {using var ignored=RideSounds.FromState(bad,bindings);}
                catch(InvalidDataException) {rejected=true;}
                Check(rejected,"malformed state fails closed");
            }
            Reject(s with {Version=99});Reject(s with {AssetsId="untrusted"});
            Reject(s with {Repeats=s.Repeats.Select((t,i)=>i==0?t with {Due=double.NaN}:t).ToArray()});
            Reject(s with {CachedStreams=s.CachedStreams.Append("/untrusted/file.wav").ToArray()});
            var nextParent=new Node3D();AddChild(nextParent);
            var clone=staged.CommitAfterWorldAccepted(nextParent);
            var restored=clone.CaptureState(s.AssetsId,s.ServicesId);
            Check(Json(Logical(s))==Json(Logical(restored)),"all retained logical state after file/commit");
            for(int i=0;i<s.Voices.Length;i++)
                Check(Math.Abs(s.Voices[i].Seek-restored.Voices[i].Seek)<.05,"actual player seek within mixer tolerance");
            for(int tick=0;tick<250;tick++) {
                oldAt=newAt=new Vector3(tick,2,3);
                sounds.AdvanceSim(.04);clone.AdvanceSim(.04);Pause(parent);Pause(nextParent);
                // Tick scheduling and the shared RNGs must agree through many real transitions.
                var a=sounds.CaptureState(s.AssetsId,s.ServicesId);var b=clone.CaptureState(s.AssetsId,s.ServicesId);
                Check(Json(Logical(a))==Json(Logical(b)),"real AdvanceSim continuation "+tick);
            }
            Check(newCalls>0,"new world's moving provider used by later graph voices");
            sounds.Clear();clone.Clear();parent.Free();nextParent.Free();
            var rng=new SfxEventMachine.Rng(0xdeadbeef);for(int i=0;i<91;i++)rng.Next();
            var rng2=SfxEventMachine.Rng.FromState(rng.CurrentState);
            for(int i=0;i<100;i++)Check(rng.Next()==rng2.Next(),"LCG current-state continuation");
            GD.Print($"RIDE SOUNDS SAVE PASS: {checks} checks");GetTree().Quit();
        } catch(Exception e) {GD.PrintErr("RIDE SOUNDS SAVE FAIL: "+e);GetTree().Quit(2);}
    }
}
