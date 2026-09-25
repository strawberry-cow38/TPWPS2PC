using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>Queue item 8: no stranded guests. Admitted guests are made broke and, in the same instant, the
/// park path they would leave by is dug up. Their mode-14 requests then fail into the state-0 hold, which
/// the adapter must resume rather than keep. The path is laid again and every guest must still leave
/// through the native departure, with no lease held at the end. Explicit fee10, cash50 and path removal
/// are the fixture's inputs. The corridor itself cannot be dug up in this port, so the bus leg (state 5)
/// is covered headless in ParkSimAudit, not here.</summary>
public partial class NativeDepartureDisruptionSmoke : Node3D
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
            Set(viewer,"_entranceFee",10); // explicit admission fixture, not scenario default
            AddChild(viewer);viewer.SetProcess(false);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            var park=Field<Park>(viewer,"_park");
            var lib=Field<AssetLibrary>(viewer,"_lib");
            var terrain=Field<Model>(viewer,"_terrainModel");
            var entry=Field<ParkEntrance>(viewer,"_entranceTable").Fit(terrain.Field,
                ParkEntrance.WalkwayColumnFromPoles(terrain),out _);
            var path=new List<(int,int)>();
            for(int z=entry.ZEnd;z<entry.ZEnd+16;z++)path.Add((entry.XCol,z));
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
            // Follow 8 real bus-born guests past the booth, exactly as the ordinary departure smoke does.
            var followed=new HashSet<Guest>(ReferenceEqualityComparer.Instance);
            var serials=new Dictionary<Guest,uint?>(ReferenceEqualityComparer.Instance);
            var retired=new HashSet<Guest>(ReferenceEqualityComparer.Instance);
            var actors=Field<Dictionary<int,Node3D>>(viewer,"_actors");
            var gridBus=ParkPaths.Centre(catalogue.Point0);
            var paths=Field<PathTool>(viewer,"_paths");
            int wentHome=visitors.WentHome,held=0,maxHeldRun=0;
            var heldRun=new Dictionary<Guest,int>(ReferenceEqualityComparer.Instance);
            void Observe()
            {
                foreach(var o in flow.Observations.Where(o=>followed.Contains(o.Guest)))
                {
                    Check(!o.Stopped,$"guest {o.Guest.Id} never stops: {o.Failure}");
                    bool hold=o.State is NativeEntranceFlow.State.DecisionBoundary or NativeEntranceFlow.State.RecoveryBoundary;
                    heldRun[o.Guest]=hold?heldRun.GetValueOrDefault(o.Guest)+1:0;
                    if(hold)held++;
                    maxHeldRun=Math.Max(maxHeldRun,heldRun[o.Guest]);
                    Check(heldRun[o.Guest]<=1,$"guest {o.Guest.Id} is not left in a state-0/5 hold (held {heldRun[o.Guest]} updates)");
                }
            }
            async System.Threading.Tasks.Task Step(int i)
            {
                var live=followed.Where(walk.IsLive).ToArray();
                var positions=live.ToDictionary(g=>g,g=>g.Position,ReferenceEqualityComparer.Instance);
                var wasOwned=live.ToDictionary(g=>g,g=>flow.Owns(g),ReferenceEqualityComparer.Instance);
                Call(viewer,"StepPark",.04);
                foreach(var g in live.Where(g=>!walk.IsLive(g)))
                {
                    Check(wasOwned[g],"a guest retires only from the native departure, never the legacy gate walk");
                    Check(positions[g]==gridBus,"retirement occurs at bus point0, not where the path was dug up");
                    Check(!actors.ContainsKey(g.Id)&&!visitors.Plans.ContainsKey(g.Id)&&!visitors.Needs.Has(g.Id),
                        "actor, plan and needs row disappear together");
                    retired.Add(g);
                }
                Observe();
                if(i%16==0)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            for(int i=0;i<9000&&(followed.Count<8||followed.Any(g=>walk.IsLive(g)&&visitors.Plans[g.Id].Intent==VisitorIntent.Entering));i++)
            {
                foreach(var o in flow.Observations)
                    if(followed.Add(o.Guest))serials[o.Guest]=o.Serial;
                if(followed.Count>=8)Set(viewer,"_guestCap",0); // never spawn or remove a guest by hand
                await Step(i);
            }
            var stayers=followed.Where(g=>walk.IsLive(g)&&!flow.Owns(g)).ToArray();
            Check(followed.Count>=8&&stayers.Length>=2,$"{stayers.Length} of {followed.Count} followed guests are admitted, unowned visitors before the disruption");
            // THE DISRUPTION. Only the fixture's own leg can be dug up (the park's pre-laid path is not
            // on the undo stack), so whether it strands anyone depends on where guests stand. FANTASY-1
            // stranded nobody on the first try (2026-09-25: 0 holds). So dig, ask the route adapter's own
            // BFS who is now cut off from the booth, and re-lay and dig again until somebody is.
            // Those guests, and only those, go broke.
            var entryPath=new List<(int,int)>();
            for(int z=entry.ZEnd;z<entry.ZEnd+16;z++)entryPath.Add((entry.XCol,z));
            var staging=catalogue.StagingPoint;Guest[] cut=Array.Empty<Guest>();int digs=0;
            for(int attempt=0;attempt<12&&cut.Length==0;attempt++)
            {
                if(attempt>0){Call(viewer,"LayLeg",entryPath,PathTool.Kind.Path,0);Call(viewer,"RefreshFloor");for(int i=0;i<120;i++)await Step(i);}
                Check(paths.UndoLeg(),"the fixture's park path is dug up through the path tool's own undo");
                Call(viewer,"RefreshFloor");digs++;
                cut=followed.Where(g=>walk.IsLive(g)&&!flow.Owns(g)&&walk.Route(g.Cell,staging)==null).ToArray();
            }
            Check(cut.Length>0,$"digging up the leg cut {cut.Length} admitted guests off from the booth (after {digs} digs)");
            foreach(var g in cut){var w=visitors.Needs.Of(g.Id);w.Cash=50;visitors.Needs.Set(g.Id,w);}
            int resumedBefore=flow.HoldsResumed,handbacksBefore=flow.DepartureHandbacks;
            for(int i=0;i<640;i++)await Step(i);
            int handbacks=flow.DepartureHandbacks-handbacksBefore,resumed=flow.HoldsResumed-resumedBefore;
            Check(handbacks>0,$"with no way home, a leaver's failed departure was handed back to ordinary visiting ({handbacks} handbacks, {resumed} holds resumed)");
            Check(held>0,"the disruption really drove guests into the native state-0 hold (it is not a vacuous pass)");
            // THE REPAIR: the same path is laid again, and every followed guest must now leave.
            Call(viewer,"LayLeg",entryPath,PathTool.Kind.Path,0);Call(viewer,"RefreshFloor");
            for(int i=0;i<8000&&retired.Count<followed.Count;i++)await Step(i);
            Check(retired.Count==followed.Count&&visitors.WentHome-wentHome==followed.Count&&visitors.DiscardedEntranceGuests==0,
                $"every followed guest left through a real departure once the path returned ({retired.Count} of {followed.Count})");
            Check(walk.NativeRoutes.Available==NativeRoutePool.Capacity&&flow.Counts==(0,0)&&flow.StagingPending==0&&flow.Observations.Count==0,
                "no lease, route slot, membership or staging count is held at quiescence");
            detail=$"followed={followed.Count} cutOff={cut.Length} digs={digs} handbacks={handbacks} holdsResumed={resumed} heldObservations={held} maxHeldRun={maxHeldRun}";
            exit=0;
        }
        catch(Exception e){GD.PrintErr($"NATIVE DEPARTURE DISRUPTION SMOKE FAIL checks={checks}: {e}");}
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
            catch(Exception e){exit=2;GD.PrintErr("NATIVE DEPARTURE DISRUPTION cleanup: "+e);}
        }
        if(exit==0)GD.Print($"NATIVE DEPARTURE DISRUPTION SMOKE PASS checks={checks}; {detail}; park path dug up under broke leavers, then relaid; every held departure resumed and every guest left via point0; explicit fee10/cash50/path-removal fixture; queue-8 adapter, not native 20C930/1913B8");
        GetTree().Quit(exit);
    }
}
