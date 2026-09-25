using System.Reflection;
using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer.Tests;

/// <summary>The entrance experiment with --native-guest-animation: actual bus birth, actual
/// placement, actual actors, readiness from the 2AAD48 dispatcher (191E10). REJECTS:
/// - a guest stepping while its request is 13 and its model has not committed 9/13;
/// - a vacuous run where no guest ever waits;
/// - a wait that never ends (deadlock);
/// - a drawn record that is not the dispatcher's.
/// NOT covered: native frame order and cadence, the shown-bit producer and the console's random
/// stream -- all labelled adapters in Viewer.NativeAnimation.cs.</summary>
public partial class NativeAnimationReadinessSmoke : Node3D
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
    int checks;
    void Check(bool ok,string why) { if(!ok)throw new InvalidOperationException(why);checks++; }
    readonly record struct Before(int Requested,byte Current,int State,int Slot,NativeGuestMotion.Point Position);

    public override async void _Ready()
    {
        Viewer viewer=null;int exit=2;
        try
        {
            Check(DisplayServer.GetName()!="headless","rendering display required");
            viewer=new Viewer{Name="Viewer"};
            Set(viewer,"_nativeGuestAnimation",true);
            Set(viewer,"_entranceFee",10);
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
                        try {Call(viewer,"PlaceHeld");}finally{Set(viewer,"_cursorOverride",null);}
                        placed=park.Placed.Count==1;
                    }
            Call(viewer,"CloseTool");
            Check(placed,"actual build menu placed a positive-value attraction");
            int t=0;
            while(Field<int>(viewer,"_busAdmitted")==0&&t++<1200)Call(viewer,"StepPark",.04);
            var walk=Field<GuestWalk>(viewer,"_guests");
            var flow=Field<NativeEntranceFlow>(viewer,"_entranceFlow");
            var owner=typeof(NativeEntranceFlow).GetField("_owner",Hidden)!.GetValue(flow);
            var anims=Field<Dictionary<Guest,NativeGuestAnimation>>(viewer,"_nativeAnimations");
            var drawn=Field<Dictionary<int,(AnimatedModel Drawn,Aps.Record Sit,string Where)>>(viewer,"_drawn");
            Check(flow!=null&&walk.Guests.Count>0,"bus batch acquired entrance owners");
            // Several real batches, not one: a single bus guest reaches its first walk request two
            // ticks after birth and rarely has a record in progress then, so one guest cannot show
            // a wait reliably. Admission stops once 12 guests have been owned; the run then drains.
            var owned=new HashSet<Guest>(ReferenceEqualityComparer.Instance);
            IEnumerable<Guest> batch()=>owned;

            var blockedTicks=new Dictionary<int,int>();var released=new HashSet<int>();
            var waitMs=new Dictionary<int,int>();int gated=0,drawnChecked=0,held=0,skipped=0,moved=0;
            var sections=new SortedDictionary<int,int>();var requests=new SortedSet<int>();
            // Optional film (TPW_NATIVE_ANIM_FILM=<dir>): follow the first guest seen waiting and
            // capture it every other tick until 24 ticks after it moves again. Off in the matrix.
            string film=System.Environment.GetEnvironmentVariable("TPW_NATIVE_ANIM_FILM");
            Guest filmed=null;int filmAfter=-1,shots=0;var camera=Field<Camera3D>(viewer,"_cam");
            if(film!=null)
            {
                // Every piece of UI goes: the build menu opened by the fixture covered the guest in the
                // first film (2026-09-25), which was generated but failed inspection.
                System.IO.Directory.CreateDirectory(film);
                foreach(var layer in viewer.FindChildren("*","CanvasLayer",true,false).OfType<CanvasLayer>())layer.Visible=false;
                foreach(var ui in viewer.GetChildren().OfType<Control>())ui.Visible=false;
            }
            async System.Threading.Tasks.Task Capture(Guest g,string label)
            {
                var actor=Field<Dictionary<int,Node3D>>(viewer,"_actors")[g.Id];
                // Aim at the DRAWN body: the actor node's origin is not where the mirrored model
                // renders (the second film framed empty ground beside the guest).
                var meshes=actor.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null).ToList();
                Check(meshes.Count>0,$"guest {g.Id} has a drawn body to film");
                var box=meshes.Select(m=>m.GlobalTransform*m.GetAabb()).Aggregate((x,y)=>x.Merge(y));
                var at=box.GetCenter();
                // Two angles per frame: a single fixed angle put the bus-stop pole between the camera
                // and the waiting guest (second film, 2026-09-25).
                int frame=shots++;
                foreach(var (side,offset) in new[]{("a",new Vector3(-0.9f,0.5f,-0.9f)),("b",new Vector3(0.9f,0.5f,-0.9f))})
                {
                    camera.GlobalPosition=at+offset;camera.LookAt(at);
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    using var image=GetViewport().GetTexture().GetImage();
                    string name=System.IO.Path.Combine(film,$"{frame:D3}{side}-{label}.png");
                    Check(image.SavePng(name)==Error.Ok,"saved film frame "+name);
                }
            }
            for(int i=0;i<9000&&(owned.Count<12||owned.Any(flow.Owns));i++)
            {
                foreach(var g in walk.Guests) if(flow.Owns(g)) owned.Add(g);
                if(owned.Count>=12) Set(viewer,"_guestCap",0);
                var before=new Dictionary<Guest,Before>(ReferenceEqualityComparer.Instance);
                foreach(var g in batch())
                    if(flow.Owns(g)&&anims.TryGetValue(g,out var a)&&walk.NativeRouteState(g,owner) is {} s)
                        before[g]=new(a.Requested&0x1f,a.Control.Current,s.ExecutionState,s.SlotIndex,s.Position);
                int tick0=Field<int>(viewer,"_parkTicks");
                Call(viewer,"StepPark",.04);
                Call(viewer,"PlaceActors",1f);
                if(Field<int>(viewer,"_parkTicks")-tick0!=1){skipped++;continue;}
                foreach(var (g,b) in before)
                {
                    requests.Add(b.Requested);
                    if(b.State!=3||b.Slot<0)continue; // the gate is only read at state 3 with a slot
                    gated++;
                    var after=flow.Owns(g)?walk.NativeRouteState(g,owner):null;
                    bool blocked=(b.Requested is 9 or 13)&&b.Current is not (9 or 13);
                    if(blocked)
                    {
                        if(film!=null&&filmed==null)filmed=g;
                        Check(after is {} a2&&a2.Position==b.Position,
                            $"guest {g.Id}: request {b.Requested}, current {b.Current:X2}: stepped anyway (readiness bypassed)");
                        blockedTicks[g.Id]=blockedTicks.GetValueOrDefault(g.Id)+1;
                        waitMs[g.Id]=waitMs.GetValueOrDefault(g.Id)+(int)ParkSim.TickMilliseconds;
                    }
                    else if(after is {} a3&&a3.Position!=b.Position)
                    {
                        moved++;
                        if(blockedTicks.ContainsKey(g.Id))released.Add(g.Id);
                        if(ReferenceEquals(g,filmed)&&filmAfter<0)filmAfter=0;
                    }
                }
                foreach(var g in batch())
                {
                    if(!flow.Owns(g)||!anims.TryGetValue(g,out var a)||!drawn.TryGetValue(g.Id,out var d))continue;
                    if(a.Held is {} h){held++;continue;}
                    if(a.Slot==NativeAnimationDescriptor.Inactive)continue;
                    sections[a.Slot]=sections.GetValueOrDefault(a.Slot)+1;
                    var rec=d.Drawn.Record;
                    Check(rec!=null&&rec.Slot==(a.Slot==0?1:a.Slot),
                        $"guest {g.Id}: dispatcher section {a.Slot} but drawn slot {rec?.Slot}");
                    Check(rec.DurationFrames==(int)a.Duration,
                        $"guest {g.Id}: drawn record lasts {rec.DurationFrames}, dispatcher's record {a.Duration} (own .aps)");
                    drawnChecked++;
                }
                if(filmed!=null&&filmAfter<24&&flow.Owns(filmed)&&anims.TryGetValue(filmed,out var fa)&&i%2==0)
                {
                    string phase=filmAfter>=0?"moving":fa.Control.Current is 9 or 13?"ready":"WAIT";
                    await Capture(filmed,$"t{Field<int>(viewer,"_parkTicks")}-{phase}-L{fa.Control.Current}-s{fa.Slot:X}v{fa.Variant}-f{fa.Frame:F0}");
                }
                if(filmAfter>=0&&filmAfter<24)filmAfter++;
                if(i%16==0)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            int waits=Field<int>(viewer,"<NativeAnimationWaits>k__BackingField");
            GD.Print($"[native-animation] gated={gated} moved={moved} blocked guests={blockedTicks.Count}/{owned.Count} "
                   +$"blocked ticks={blockedTicks.Values.Sum()} readiness refusals={waits} max wait={(waitMs.Count==0?0:waitMs.Values.Max())} ms "
                   +$"sections drawn={string.Join(",",sections.Select(kv=>$"{kv.Key}:{kv.Value}"))} held={held} requests={string.Join(",",requests)} skipped={skipped}");
            var table=Field<NativeLogicalAnimationTable>(viewer,"_logicalAnimations");
            Check(requests.Contains(11)&&requests.Contains(13)&&requests.IsSubsetOf(table.IdleStates.Append(11).Append(13)),
                $"decoded producers only: requests seen {string.Join(",",requests)} (activation 11, advance 13, 2106E8 picks {string.Join(",",table.IdleStates)})");
            Check(owned.Count>=12,$"followed {owned.Count} real bus guests across batches");
            Check(gated>0&&moved>0,$"the gate was read and guests moved ({gated} gated, {moved} moves)");
            Check(blockedTicks.Count>0,"at least one actual guest waited on its animation (not vacuous)");
            Check(waits>=blockedTicks.Values.Sum(),"the viewer counted a refusal for every observed wait");
            Check(blockedTicks.Keys.All(released.Contains),
                $"every waiting guest moved again after its model committed 13 ({released.Count} of {blockedTicks.Count})");
            Check(drawnChecked>0&&sections.ContainsKey(0),$"drawn records checked against the dispatcher ({drawnChecked}), including the section-0 walk");
            Check(!owned.Any(flow.Owns),"no deadlock: every followed bus guest completed the entrance handoff");
            Check(anims.Count==0,"handback released every dispatcher animation");
            Check(Field<int>(viewer,"_entranceAccepted")+Field<int>(viewer,"_entranceRejected")>=owned.Count,"every followed guest reached the fee decision");
            Call(viewer,"ResetNativeBus");
            exit=0;
        }
        catch(Exception e){GD.PrintErr($"NATIVE ANIMATION READINESS SMOKE FAIL checks={checks}: {e}");}
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
            catch(Exception e){exit=2;GD.PrintErr("NATIVE ANIMATION READINESS cleanup: "+e);}
        }
        if(exit==0)GD.Print($"NATIVE ANIMATION READINESS SMOKE PASS checks={checks}; actual bus guests wait on committed 9/13; research adapters labelled");
        GetTree().Quit(exit);
    }
}
