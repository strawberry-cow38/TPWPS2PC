using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>Queue item 10: the busy-park soak. The entrance runs under the executable's own LoadsOfKids batch
/// rule (min(20, 20-W)), so the incoming groups fill past 11 and the ONE group flip has to happen. Then the
/// bus is switched off and everyone left is made broke: a mass exodus through state 26 whose sticky A4
/// pressure must reach 30 and refuse a batch. Conservation (births = WentHome + discarded + live) is checked
/// throughout, and the park must drain to an empty flow with every shared route slot free.
/// Fixture inputs: LoadsOfKids, the cash drop to 50 and the admission off-switch. The fee is the ordinary
/// constructor 150, not a fixture price. Not native startup/search/readiness parity.</summary>
public partial class NativeEntranceSoak : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo Member(string n) => typeof(Viewer).GetField(n, Hidden) ?? throw new MissingMemberException(n);
    static T Field<T>(Viewer v,string n)=>(T)Member(n).GetValue(v);
    static void Set(Viewer v,string n,object o)=>Member(n).SetValue(v,o);
    static object Call(Viewer v,string n,params object[] args)
    {
        var m=typeof(Viewer).GetMethod(n,Hidden)??throw new MissingMethodException(n);
        args=args.Concat(m.GetParameters().Skip(args.Length).Select(p=>p.DefaultValue)).ToArray();
        return m.Invoke(v,args);
    }
    int checks;string detail="";
    void Check(bool ok,string why) { if(!ok)throw new InvalidOperationException(why);checks++; }
    public override async void _Ready()
    {
        Viewer viewer=null;int exit=2;
        try
        {
            Check(DisplayServer.GetName()!="headless","rendering display required");
            viewer=new Viewer{Name="Viewer"};
            Set(viewer,"_nativeLoadsOfKids",true); // the executable's batch cheat rule: fills the entrance
            AddChild(viewer);viewer.SetProcess(false);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            var park=Field<Park>(viewer,"_park");
            var lib=Field<AssetLibrary>(viewer,"_lib");
            var terrain=Field<Model>(viewer,"_terrainModel");
            var entry=Field<ParkEntrance>(viewer,"_entranceTable").Fit(terrain.Field,
                ParkEntrance.WalkwayColumnFromPoles(terrain),out _);
            var path=new List<(int,int)>();
            for(int z=entry.ZEnd;z<entry.ZEnd+8;z++)path.Add((entry.XCol,z));
            Call(viewer,"LayLeg",path,PathTool.Kind.Path,0);Call(viewer,"RefreshFloor");
            Check((bool)Call(viewer,"OpenGate"),"ordinary guest layer opens");
            Call(viewer,"ShowBuildCategory","Rides");
            var rows=Field<List<int>>(viewer,"_buildRows");
            int chosen=Enumerable.Range(0,rows.Count).First(row=>
            {
                var d=(RideDefinition)Call(viewer,"DefinitionFor",lib.Rides[rows[row]].Model);
                var initial=NativeRideValue.CreateDefaultState(d?.CompiledEntry);
                return d?.Shape!=null && initial.HasValue && NativeRideValue.Calculate(d.CompiledEntry,initial.Value)>8;
            });
            Call(viewer,"ArmFromList",chosen);
            var blueprint=Field<Placement>(viewer,"_place");bool placed=false;
            for(int z=entry.ZEnd+10;z<park.Field.Height-10&&!placed;z++)
                for(int x=12;x<park.Field.Width-10&&!placed;x++)
                    if(blueprint.Fits(park,x,z))
                    {
                        Set(viewer,"_cursorOverride",(x,z));
                        try{Call(viewer,"PlaceHeld");}finally{Set(viewer,"_cursorOverride",null);}
                        placed=park.Placed.Count==1;
                    }
            Call(viewer,"CloseTool");
            Check(placed,"actual build menu/PlaceHeld registered a positive-value native attraction");
            int t=0;
            while(Field<int>(viewer,"_busAdmitted")==0&&t++<1200)Call(viewer,"StepPark",.04);
            var walk=Field<GuestWalk>(viewer,"_guests");
            var visitors=Field<ParkVisitors>(viewer,"_visitors");
            var flow=Field<NativeEntranceFlow>(viewer,"_entranceFlow");
            var catalogue=Field<NativeBusCatalogue>(viewer,"_busCatalogue");
            Check(flow!=null&&walk.Guests.Count>0,"normal bus batch automatically acquired research entrance owners");
            var bus=Field<NativeBus>(viewer,"_nativeBus").Controller;
            var actors=Field<Dictionary<int,Node3D>>(viewer,"_actors");
            int Births()=>Field<int>(viewer,"_busAdmitted");
            int conserved=0;
            void Conserve(string when)
            {
                int births=Births(),home=visitors.WentHome,gone=visitors.DiscardedEntranceGuests,live=walk.Guests.Count;
                Check(births==home+gone+live,$"{when}: births {births} = went home {home} + discarded {gone} + live {live}");
                Check(actors.Count==live,$"{when}: one actor per live guest ({actors.Count} actors, {live} guests)");
                conserved++;
            }
            int peakGroup=0,peakPressure=0,peakLive=0,ticks=0;
            async System.Threading.Tasks.Task Step()
            {
                Call(viewer,"StepPark",.04);ticks++;
                peakGroup=Math.Max(peakGroup,Math.Max(flow.Counts.Group0,flow.Counts.Group1));
                peakPressure=Math.Max(peakPressure,flow.DeparturePressure);
                peakLive=Math.Max(peakLive,walk.Guests.Count);
                foreach(var o in flow.Observations)Check(!o.Stopped,$"guest {o.Guest.Id} never stops: {o.Failure}");
                if(ticks%100==0)Conserve($"tick {ticks}");
                if(ticks%16==0)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            // BUSY: LoadsOfKids batches at the constructor fee.
            for(int i=0;i<3000;i++)await Step();
            Check(flow.GroupFlips>0,$"an incoming group reached 11 and the ONE flip sent a guest to the other group ({flow.GroupFlips} flips, peak group {peakGroup})");
            Check(Births()>=40,$"the soak was busy: {Births()} bus births");
            // EXODUS: no more births, and everyone the entrance has handed over goes broke (20C930's cash<100).
            Set(viewer,"_guestCap",0);
            int broke=0,vetoesBefore=bus.PressureVetoes;
            for(int i=0;i<15000&&walk.Guests.Count>0;i++)
            {
                foreach(var g in walk.Guests.Where(g=>!flow.Owns(g)&&visitors.Needs.Has(g.Id)&&visitors.Needs.Of(g.Id).Cash>=100).ToArray())
                {var w=visitors.Needs.Of(g.Id);w.Cash=50;visitors.Needs.Set(g.Id,w);broke++;}
                await Step();
            }
            int vetoes=bus.PressureVetoes-vetoesBefore;
            Check(peakPressure>=30&&vetoes>0,$"the exodus drove sticky pressure to {peakPressure} and the 30-guest test refused {vetoes} batch requests");
            Conserve("quiescence");
            Check(walk.Guests.Count==0&&flow.Observations.Count==0&&flow.Counts==(0,0)&&flow.StagingPending==0,
                "the park drained: no live guest, flow entry, incoming membership or staging count remains");
            Check(walk.NativeRoutes.Available==NativeRoutePool.Capacity,"all 1000 shared route slots are free at quiescence");
            Check(visitors.DiscardedEntranceGuests==0&&visitors.WentHome==Births(),"every birth went home through a departure; none was discarded");
            detail=$"ticks={ticks} births={Births()} wentHome={visitors.WentHome} peakLive={peakLive} peakGroup={peakGroup} flips={flow.GroupFlips} peakPressure={peakPressure} vetoes={vetoes} madeBroke={broke} holdsResumed={flow.HoldsResumed} conservationChecks={conserved}";
            exit=0;
        }
        catch(Exception e){GD.PrintErr($"NATIVE ENTRANCE SOAK SMOKE FAIL checks={checks}: {e}");}
        finally
        {
            try
            {
                if(viewer!=null&&IsInstanceValid(viewer))
                {Call(viewer,"ResetNativeBus");Field<RideSounds>(viewer,"_sounds")?.Clear();viewer.QueueFree();}
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
            }
            catch(Exception e){exit=2;GD.PrintErr("NATIVE ENTRANCE SOAK cleanup: "+e);}
        }
        if(exit==0)GD.Print($"NATIVE ENTRANCE SOAK SMOKE PASS checks={checks}; {detail}; LoadsOfKids busy entrance then broke exodus; conservation, flip, veto and drain; fee150; fixture inputs LoadsOfKids/cash50/admission-off");
        GetTree().Quit(exit);
    }
}
