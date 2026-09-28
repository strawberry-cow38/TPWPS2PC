using Godot;
using System.Reflection;
using System.Text.Json;
using TPW.PS2.Data;
using Aps=TPW.PS2.Data.Animation;
namespace TPWPS2Viewer.Tests;

/// <summary>Actual Viewer API smoke, not a full ActorWorld publication test. Reflection is test-only:
/// scheduler presently has no public producer/day ownership accessors. Read-only disc, RAM assets.</summary>
public partial class ViewerAdvisorWorldSaveSmoke:Node
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Private).GetValue(o);
    static void Set(Viewer v,string name,object value)=>typeof(Viewer).GetField(name,Private).SetValue(v,value);
    static string Json(object o)=>JsonSerializer.Serialize(o);
    int checks;
    void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Free(Viewer v)
    {
        foreach(var root in new[]{Field<Weather>(v,"_weather").Root,Field<EntranceFlags>(v,"_flags").Root,Field<ThoughtBubbles>(v,"_thoughts").Root})
            if(root.GetParent()==null)root.Free();
        v.Free();
    }
    public override void _Ready()
    {
        Viewer source=null,loaded=null;AdvisorHead head=null;Viewer.StagedAdvisor staged=null;
        try {
            using var lib=new AssetLibrary("/home/ec2-user/tpw-ps2/tpw_ps2.bin");
            var catalogue=AdvisorCatalogue.Load(lib.Disc);
            var rules=new AdvisorRules(lib.ReadGeneric("/Generic/Advisor/headers.ass"),lib.ReadGeneric("/Generic/Advisor/opcodes.ass"));
            Check(rules.Rules.Count==106,"real 106 rules");
            lib.OpenWad("/DATA/DATA.WAD");
            var asset=lib.Rides.First(r=>r.Name.EndsWith("/advisor.mps",StringComparison.OrdinalIgnoreCase));
            var model=new Model(lib.Read(asset.Model));var aps=new Aps(lib.Read(asset.Animation));
            head=new AdvisorHead(model,aps,_=>(null,false));
            var headBindings=new AdvisorHead.SnapshotBindings {Drawn=new AnimatedModel.SnapshotBindings {
                ModelAssetId="head-model",AnimationAssetId="head-aps",Model=model,Animation=aps,Texture=_=>(null,false)}};
            source=new Viewer();loaded=new Viewer();
            var calendar=Field<ParkClock>(source,"_calendar");
            var terrain=(Model)typeof(ViewerStaffSaveSmoke).GetMethod("Terrain",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            var paths=new ParkPaths(terrain);var sim=new ParkSim(paths);Set(source,"_walkGrid",paths);Set(source,"_sim",sim);Set(source,"_guests",new GuestWalk(paths));
            var producers=new AdvisorProducers(calendar,sim);
            var advisor=new ParkAdvisor(catalogue,rules,calendar,producers){Head=head};advisor.Attach(sim,null,null);
            Set(source,"_advisor",catalogue);Set(source,"_advisorRules",rules);Set(source,"_parkAdvisor",advisor);Set(source,"_advisorHead",head);
            Set(source,"_advisorSpeechTried",true);Set(source,"_advisorBoxFontTried",true);Set(source,"_advisorUnavailable",true);
            Set(source,"_advisorL2",true);Set(source,"_advisorError","saved failure");
            Field<Dictionary<ushort,AudioStreamWav>>(source,"_advisorStreams").Add(7,null);
            Field<Dictionary<(int,int),AdvisorCatalogue.Binding>>(source,"_advisorBindings").Add((9,0),null);
            var assets=new Dictionary<string,object>{{"catalogue",catalogue},{"rules",rules}};
            var stackView=new AdvisorStackView();stackView.Configure(advisor.Stack,r=>r.Text,lib,()=>null,null);
            stackView.Allowed=true;typeof(AdvisorStackView).GetProperty("SmoothedScroll").SetValue(stackView,137);
            Set(source,"_advisorStackView",stackView);
            foreach(var p in stackView.StateAssets)if(p.Value!=null)assets.Add("stack/"+p.Key,p.Value);
            AdvisorProducers restoredProducers=null;
            Viewer.AdvisorStateServices Services(AdvisorProducers capture)=>new(){
                IdentifyAsset=o=>assets.Single(p=>ReferenceEquals(p.Value,o)).Key,ResolveAsset=id=>assets[id],
                CaptureProducers=capture,HeadModelId="head-model",HeadAnimationId="head-aps",Head=_=>headBindings,
                BindProducers=(b,p)=>{restoredProducers=p;b.Add("producers",p);},
                Stack=v=>{
                    var view=Field<AdvisorStackView>(v,"_advisorStackView");
                    return new(){Stack=Field<ParkAdvisor>(v,"_parkAdvisor").Stack,
                        Text=view?.StateText??(r=>r.Text),CountFont=view?.StateCountFont??(()=>null),
                        IdentifyAsset=o=>assets.Single(p=>ReferenceEquals(p.Value,o)).Key,ResolveAsset=id=>assets[id]};},
                Core=(v,a,h)=>{
                    var b=new AdvisorStateBindings();b.Add("catalogue",catalogue);b.Add("rules",rules);
                    var clock=Field<ParkClock>(v,"_calendar");b.Add("calendar",clock);b.Add("sim",Field<ParkSim>(v,"_sim"));if(h!=null)b.Add("head",h);
                    bool capturing=a?.Scheduler!=null;
                    if(capturing){b.Add("producers",a.Scheduler.StateProducers);
                        b.Add("day",a.Scheduler.StateDay);}
                    else b.Add("day",(Func<uint>)(()=>unchecked((uint)clock.TotalDays)));
                    v.BindStockAdvisorCallbacks(b,a,capturing);return b;
                }};
            // A real renderer mid-enter and stack pending input. No physical audio is needed.
            advisor.Stack.AddGoalNotice("retained notice");advisor.Stack.Open();advisor.Stack.Press(AdvisorStackButtons.Next);
            advisor.Submit(208);for(int i=0;i<7;i++)advisor.Update();
            head.Present(.4f,50,new Vector2(800,600),true);
            var saved=source.CaptureAdvisorState(Services(producers),true);
            string frozen=Json(saved);
            Viewer.WorldCoreBindings Bind()=>new(){TerrainKey="advisor-grid",Terrain=terrain,Simulation=new(){Scripts=new(){
                IdentifyProgram=_=>throw new Exception(),ResolveProgram=_=>throw new Exception(),IdentifyAnimation=_=>throw new Exception(),ResolveAnimation=_=>throw new Exception()}},
                Advisor=r=>Services(ReferenceEquals(r.Owner,source)?producers:null)};
            var world=source.CaptureWorldCoreState(Bind(),out _);
            string file=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"advisor-world-"+Guid.NewGuid()+".sav");
            try{ParkSaveFile.Write(file,world);world=ParkSaveFile.Read<Viewer.WorldCoreState>(file);}finally{System.IO.File.Delete(file);}
            var registry=loaded.RestoreWorldCoreState(world,Bind());staged=registry.UnpublishedAdvisor;
            Check(Json(world)==Json(loaded.CaptureWorldCoreState(Bind(),out _)),"central logical+advisor+stack FILE roundtrip");
            Check(Field<AdvisorStackView>(loaded,"_advisorStackView").SmoothedScroll==137,"nonzero stack smoothing preserved");
            Check(staged.Voice==null,"absent voice remains absent and cold");
            Check(!staged.Head.Overlay.IsInsideTree()&&staged.Head.Overlay.GetParent()==null,"head detached");
            Check(ReferenceEquals(restoredProducers.Clock,Field<ParkClock>(loaded,"_calendar"))&&!ReferenceEquals(restoredProducers.Clock,calendar),"new world producers");
            var newAdvisor=Field<ParkAdvisor>(loaded,"_parkAdvisor");
            Check(Json(saved)==Json(loaded.CaptureAdvisorState(Services(restoredProducers),true)),"exact actual Viewer roundtrip");
            for(int i=0;i<160;i++) {
                if(i%17==0){sim.AdvisorEvent(4,1);Field<ParkSim>(loaded,"_sim").AdvisorEvent(4,1);}
                advisor.Update();newAdvisor.Update();
                stackView.Pass();Field<AdvisorStackView>(loaded,"_advisorStackView").Pass();
                head.Present(.25f,50,new Vector2(800,600),true);staged.Head.Present(.25f,50,new Vector2(800,600),true);
                Check(Json(source.CaptureAdvisorState(Services(producers),true))==Json(loaded.CaptureAdvisorState(Services(restoredProducers),true)),"bounded real-head/core continuation "+i);
            }
            Check(Json(saved)==frozen,"checkpoint immutable");
            Check(advisor.Scheduler.Calls>0,"real scheduler continued");
            bool rejected=false;try{source.CaptureAdvisorState(Services(producers),false);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"nonquiescent rejected");
            GD.Print($"VIEWER ADVISOR WORLD SAVE PASS: {checks} checks (no full-world publication)");GetTree().Quit();
        } catch(Exception e){GD.PrintErr("VIEWER ADVISOR WORLD SAVE FAIL: "+e);GetTree().Quit(2);}
        finally {staged?.Dispose();head?.Free();if(source!=null){Field<AdvisorStackView>(source,"_advisorStackView")?.Free();Free(source);}if(loaded!=null)Free(loaded);}
    }
}
