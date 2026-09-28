using Godot;
using System.Reflection;
using System.Text.Json;
using TPW.PS2.Data;
namespace TPWPS2Viewer.Tests;
public partial class ViewerRuntimeSaveSmoke:Node
{
    int checks;void C(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static string Json(object o)=>JsonSerializer.Serialize(o);
    static T Field<T>(Viewer v,string name)=>(T)typeof(Viewer).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(v);
    public override void _Ready(){try{
        var source=new Viewer();var restored=new Viewer();
        try{
            // Test-only access to ACTUAL Viewer providers; never used by the production serializer.
            var clock=Field<ConsoleClock>(source,"_parkClock");clock.Advance(.061);
            var rng=Field<SnapshotRandom>(source,"_guestRng");for(int i=0;i<47;i++)rng.Next(i+1);
            Field<NewlibRand>(source,"_nativeAnimationRand").Next();Field<NewlibRand>(source,"_staffAnimationRand").Next();
            var s=source.CaptureRuntimeState() with{ParkTicks=991,GateClosed=true,EntranceFee=287,EntranceAccepted=11,
                BusElapsedMs=8123.875,BusTraffic=2,BusParameter20=51,BusParameterStamp=7900,BusParameterPendingReset=true,
                BusClockAdvancedForFrame=true,NativeGuestAnimation=true,NativeIdleAll=true,AdvisorL2=true,HireCarried=true};
            string p=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"tpw-viewer-runtime-"+Guid.NewGuid().ToString("N")+".save");
            Viewer.RuntimeState loaded;try{ParkSaveFile.Write(p,s);loaded=ParkSaveFile.Read<Viewer.RuntimeState>(p);}finally{System.IO.File.Delete(p);}
            source.RestoreRuntimeState(s);restored.RestoreRuntimeState(loaded);
            C(Json(s)==Json(restored.CaptureRuntimeState()),"actual Viewer scalar FILE roundtrip");
            var rclock=Field<ConsoleClock>(restored,"_parkClock");clock=Field<ConsoleClock>(source,"_parkClock");
            for(int i=0;i<500;i++){double dt=i%39==0?.4:(i%5+1)*.007;
                C(clock.Advance(dt)==rclock.Advance(dt)&&Json(clock.CaptureState())==Json(rclock.CaptureState()),"fractional clock/stall continuation");
                C(Field<SnapshotRandom>(source,"_guestRng").Next()==Field<SnapshotRandom>(restored,"_guestRng").Next(),"shared viewer stream continuation");}
            string before=Json(restored.CaptureRuntimeState());bool bad=false;
            try{restored.RestoreRuntimeState(s with{Clock=s.Clock with{Alpha=.99f}});}catch(InvalidDataException){bad=true;}
            C(bad&&before==Json(restored.CaptureRuntimeState()),"bad clock rejects before provider mutation");
        }finally{
            // These constructor-owned nodes are normally attached by _Ready. This scalar-only
            // fixture deliberately never enters the tree, so it owns their disposal explicitly.
            foreach(var v in new[]{source,restored}) {
                Field<Weather>(v,"_weather").Root.Free();Field<EntranceFlags>(v,"_flags").Root.Free();
                Field<ThoughtBubbles>(v,"_thoughts").Root.Free();v.Free();
            }
        }
        GD.Print($"VIEWER RUNTIME SAVE PASS: {checks} checks (scalars only, not whole Viewer)");GetTree().Quit();
    }catch(Exception e){GD.PrintErr("VIEWER RUNTIME SAVE FAIL: "+e);GetTree().Quit(2);}}
}
