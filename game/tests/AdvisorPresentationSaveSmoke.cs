using System.Text.Json;
using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer.Tests;

/// <summary>Read-only disc assets, no extraction and no Viewer. Run with --headless --audio-driver Dummy
/// res://tests/AdvisorPresentationSaveSmoke.tscn. Compares actual renderer geometry/flags/poses, not core intent.</summary>
public partial class AdvisorPresentationSaveSmoke : Node
{
    int checks;
    void Check(bool ok,string why) { if(!ok) throw new Exception(why); checks++; }
    static string Json(object x) => JsonSerializer.Serialize(x);
    static T Disk<T>(T x) where T:class {
        string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"tpw-advisor-"+Guid.NewGuid()+".sav");
        try {ParkSaveFile.Write(path,x);return ParkSaveFile.Read<T>(path);}finally{System.IO.File.Delete(path);}
    }
    void Reject(Action a,string why) { try { a(); } catch(InvalidDataException) { checks++; return; } throw new Exception(why); }
    public override async void _Ready()
    {
        try {
            Head();
            await Voice();
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            GD.Print($"ADVISOR PRESENTATION SAVE PASS: {checks} checks"); GetTree().Quit();
        } catch(Exception e) { GD.PrintErr("ADVISOR PRESENTATION SAVE FAIL: "+e); GetTree().Quit(2); }
    }
    void Head()
    {
        using var lib=new AssetLibrary("/home/ec2-user/tpw-ps2/tpw_ps2.bin");
        lib.OpenWad("/DATA/DATA.WAD");
        var asset=lib.Rides.First(r=>r.Name.EndsWith("/advisor.mps",StringComparison.OrdinalIgnoreCase));
        var model=new Model(lib.Read(asset.Model)); var aps=new Aps(lib.Read(asset.Animation));
        var immutable=(byte[])model.D.Clone();
        var textures=new Dictionary<string,(ImageTexture,bool)>();
        (ImageTexture,bool) Texture(string name) {
            if(textures.TryGetValue(name,out var cached)) return cached;
            var image=lib.Texture(asset,name);
            return textures[name]=image==null?(null,false):(ImageTexture.CreateFromImage(Image.CreateFromData(
                image.Width,image.Height,false,Image.Format.Rgba8,image.Pixels)),image.Translucent);
        }
        var bindings=new AdvisorHead.SnapshotBindings {Drawn=new AnimatedModel.SnapshotBindings {
            ModelAssetId="advisor:model",AnimationAssetId="advisor:aps",Model=model,Animation=aps,Texture=Texture }};
        AdvisorHead.Snapshot Capture(AdvisorHead h)=>h.CaptureState("advisor:model","advisor:aps");
        void Compare(AdvisorHead a,AdvisorHead b) {
            Check(Json(Capture(a))==Json(Capture(b)),"head, channel, bound, actual draw/flag/pose/viewport snapshot equality");
            Check(a.MouthsDrawn.SequenceEqual(b.MouthsDrawn),"actual drawn lips");
            var aa=a.Drawn.Surfaces().ToArray(); var bb=b.Drawn.Surfaces().ToArray();
            Check(aa.Length==bb.Length,"render surface count");
            for(int i=0;i<aa.Length;i++) Check(aa[i].Node.Transform==bb[i].Node.Transform &&
                aa[i].Node.Visible==bb[i].Node.Visible,"actual retained surface transform and visibility");
            foreach(var m in model.Meshes) {
                Check(a.Drawn.MeshDrawn(m.Index)==b.Drawn.MeshDrawn(m.Index),"mesh flags/visibility");
                var p=a.Drawn.LivePositions(m.Offset);var q=b.Drawn.LivePositions(m.Offset);
                Check(p==null?q==null:p.SequenceEqual(q),"rendered vertex pose");
            }
        }
        var head=new AdvisorHead(model,aps,Texture);
        var untouched=new AdvisorHead(model,aps,Texture);
        string untouchedBefore=Json(Capture(untouched));
        void Cut(Action<AdvisorHead,int> advance=null,int ticks=0) {
            var saved=Capture(head); string frozen=Json(saved);
            var loaded=AdvisorHead.FromState(Disk(saved),bindings);
            try {
                Check(loaded.Overlay.GetParent()==null && !loaded.Overlay.IsInsideTree(),"detached stage");
                Compare(head,loaded);
                for(int i=0;i<ticks;i++) { advance(head,i); advance(loaded,i); Compare(head,loaded); }
                Check(frozen==Json(saved),"checkpoint immutable after continuation");
                Check(immutable.SequenceEqual(model.D),"registry model bytes unchanged");
                Check(untouchedBefore==Json(Capture(untouched)),"other head sharing registry unaffected");
            } finally { loaded.Overlay.Free(); }
        }
        try {
            Check(head.Dressed==-1 && head.MouthShown==-1,"initial head selectors unshown, not core mouth zero");
            Cut();
            Reject(()=>AdvisorHead.FromState(Capture(head) with {MouthShown=99},bindings),"reject mouth");
            Reject(()=>AdvisorHead.FromState(Capture(head) with {ViewWidth=int.MaxValue},bindings),"reject dimensions");
            Reject(()=>AdvisorHead.FromState(Capture(head) with {PivotTransform=new[]{float.NaN}},bindings),"reject matrix");
            head.Play(ParkAdvisor.RecordEnter,2);
            head.Present(.37f,50,new Vector2(801,603),true);
            Cut();
            foreach(int costume in Enumerable.Range(0,15)) {
                head.Dress(costume); head.Mouth(costume%5);
                // Unsampled record change MUST preserve the previous rendered surface poses.
                head.Play(costume%7,3);
                Cut((h,i)=> { h.Step(50); h.Mouth((costume+i)%5); h.Present(.63f,50,new Vector2(973,611),true); },3);
            }
            head.Play(0,3);head.Play(ParkAdvisor.RecordExit,0);
            Check(head.Channel.Queued==ParkAdvisor.RecordExit,"queued exit precondition");
            Cut((h,i)=> {h.Step(100);h.Present(.5f,100,new Vector2(640,512),true);},300);
            Check(head.Channel.EndHeld && head.Channel.Record==ParkAdvisor.RecordExit,"exit endhold reached");
            Cut();
            // Channel can be restored/modified independently: do not infer the retained binding from it.
            head.Channel.Play(2,3); Cut();
            head.Present(1,50,new Vector2(640,512),false);Cut();
        } finally {head.Overlay.Free();untouched.Overlay.Free();}
    }
    async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    async Task Voice()
    {
        var empty=AdvisorVoiceState.CaptureState(null,"");
        using(var absent=AdvisorVoiceState.FromState(Disk(empty),new AdvisorVoiceState.SnapshotBindings {
            SoundAssetId="",Stream=null,Bus="Master"})) {
            Check(absent.Player==null,"no voice stays absent"); absent.ApplyAfterCommit();
        }
        // Four seconds of synthetic PCM: no disc sound, no filepath from the DTO.
        var wave=new AudioStreamWav {Format=AudioStreamWav.FormatEnum.Format16Bits,MixRate=22050,
            Stereo=false,Data=new byte[22050*2*4]};
        var binding=new AdvisorVoiceState.SnapshotBindings {SoundAssetId="test:immutable-wave",Stream=wave,Bus="Master"};
        var original=new AudioStreamPlayer {Stream=wave};AddChild(original);
        try {
            var idle=AdvisorVoiceState.CaptureState(original,binding.SoundAssetId);
            using(var stopped=AdvisorVoiceState.FromState(Disk(idle),binding)) {
                AddChild(stopped.Player);stopped.ApplyAfterCommit();Check(!stopped.Player.Playing,"stopped stream not started");
            }
            original.Play(.75f);await Wait(.1);
            Check(original.Playing && original.GetPlaybackPosition()>=.7f,"dummy AudioServer active and sought");
            foreach(bool pause in new[]{false,true}) {
                original.StreamPaused=pause;await Wait(.04);
                var saved=AdvisorVoiceState.CaptureState(original,binding.SoundAssetId);
                Check(saved.Playing==original.Playing && saved.HasPlayback && saved.Paused==pause && saved.PositionSeconds>=.7,"capture active or paused playback");
                using var staged=AdvisorVoiceState.FromState(Disk(saved),binding);
                Check(staged.Player.GetParent()==null && !staged.Player.Playing,"stage has NO playback");
                AddChild(staged.Player);await Wait(.05);
                Check(!staged.Player.Playing && !staged.Player.HasStreamPlayback(),"attachment alone cannot autoplay");
                staged.ApplyAfterCommit();await Wait(.04);
                Check(staged.Player.Playing==!pause && staged.Player.HasStreamPlayback() && staged.Player.StreamPaused==pause,"publish playing/paused state");
                Check(Math.Abs(staged.Player.GetPlaybackPosition()-saved.PositionSeconds)<.2,"publish resumes at captured seek position");
                if(pause) {
                    // Dummy mixer reports one queued 256-sample block asynchronously after pause.
                    // Keep the immediate seek bound above; test stationarity after that drains.
                    await Wait(.15);
                    float at=staged.Player.GetPlaybackPosition();await Wait(.12);
                    Check(Math.Abs(staged.Player.GetPlaybackPosition()-at)<.01,$"paused voice position remains fixed: {at} -> {staged.Player.GetPlaybackPosition()}");
                }
                bool duplicate=false;try {staged.ApplyAfterCommit();}catch(InvalidOperationException){duplicate=true;}
                Check(duplicate,"publish cannot start voice twice");
            }
            Reject(()=>AdvisorVoiceState.FromState(idle with {PositionSeconds=double.NaN},binding),"reject nonfinite seek");
            Reject(()=>AdvisorVoiceState.FromState(idle with {SoundAssetId="/tmp/save-supplied.wav"},binding),"reject untrusted asset substitution");
        } finally {original.StreamPaused=false;original.Stop();original.Stream=null;original.Free();wave.Dispose();}
        await Wait(.1);
    }
}
