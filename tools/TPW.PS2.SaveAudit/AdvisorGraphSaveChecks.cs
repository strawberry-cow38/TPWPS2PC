using System.Text.Json;
using TPW.PS2.Data;
public static class AdvisorGraphSaveChecks
{
    static string Json<T>(T value)=>JsonSerializer.Serialize(value);
    sealed record FileGraph(ParkClock.State Clock,ParkSim.ScriptedState Sim,AdvisorProducers.Snapshot Producers,
        ParkAdvisor.Snapshot Advisor,AdvisorHeadChannel.Snapshot Head,bool Open);
    sealed class World
    {
        internal ParkClock Clock=new();internal ParkPaths Paths=new(StaffMemberSaveChecks.Terrain());
        internal ParkSim Sim=null!;internal ParkAdvisor Advisor=null!;internal AdvisorProducers Producers=null!;
        internal AdvisorTimedHead Head=new();internal bool Open=true;
        internal readonly List<string> Events=new();internal AdvisorStateBindings Bindings=new();
        internal readonly Dictionary<string,object> Callbacks=new();
        internal readonly LipTrack Lip=new(new byte[]{0,0,0,0,0x40,0x0d,3,0,255,255,255,255});
        internal World(){
            Callbacks["open"]=(Func<bool>)(()=>Open);Callbacks["played"]=(Action<AdvisorPlayback>)(p=>Events.Add("play/"+p.Id+"/"+p.Variant));
            Callbacks["sound"]=(Action<int>)(s=>Events.Add("sound/"+s));Callbacks["length"]=(Func<int,int,ushort,int>)((_,_,_)=>800);
            Callbacks["lips"]=(Func<int,int,LipTrack>)((_,_)=>Lip);
        }
        internal void RegisterAssets(AdvisorCatalogue c,AdvisorRules r){Bindings.Add("catalogue",c);Bindings.Add("rules",r);
            Bindings.Add("clock",Clock);Bindings.Add("sim",Sim);Bindings.Add("head",Head);Bindings.Add("track",Lip);
            foreach(var p in Callbacks)Bindings.Add(p.Key,p.Value);}
        internal void RegisterAdvisor(bool original){Bindings.Add("producers",Producers);
            Bindings.Add("day",original?Advisor.Scheduler.DayBinding:(Func<uint>)(()=>unchecked((uint)Clock.TotalDays)));
            Bindings.Add("tutorial",original?Advisor.Stack.TutorialEvent:(Action<int>)(n=>Advisor.TutorialEvent(n)));
            Bindings.Add("replay",original?Advisor.Stack.Replay:(Action<short>)(n=>Advisor.TutorialMessage(n)));}
        internal FileGraph Save()=>new(Clock.CaptureState(),Sim.CaptureScriptedState(new()),Producers.CaptureState(Bindings),
            Advisor.CaptureState(Bindings,true),Head.Channel.CaptureState(),Open);
        internal void Tick(){Clock.Advance(0x4000,out _,out _,out _);Sim.Advance(.04);Advisor.Update();}
    }
    static World Create(AdvisorCatalogue c,AdvisorRules r){var w=new World();w.Sim=new(w.Paths);w.Producers=new(w.Clock,w.Sim){ParkOpen=(Func<bool>)w.Callbacks["open"]};
        w.Advisor=new(c,r,w.Clock,w.Producers,random:_=>0){Head=w.Head,SpeechLength=(Func<int,int,ushort,int>)w.Callbacks["length"],
            Lips=(Func<int,int,LipTrack>)w.Callbacks["lips"],Played=(Action<AdvisorPlayback>)w.Callbacks["played"],UiSound=(Action<int>)w.Callbacks["sound"]};
        w.RegisterAssets(c,r);w.RegisterAdvisor(true);return w;}
    static World Load(World source,AdvisorCatalogue c,AdvisorRules r,Action<bool,string> check)
    {
        var path=Path.Combine(Path.GetTempPath(),"tpw-advisor-"+Guid.NewGuid().ToString("N")+".save");FileGraph s;
        try{ParkSaveFile.Write(path,source.Save());s=ParkSaveFile.Read<FileGraph>(path);}finally{File.Delete(path);File.Delete(path+".bak");}
        var w=new World{Open=s.Open};w.Clock.RestoreState(s.Clock);w.Sim=ParkSim.FromScriptedState(s.Sim,w.Paths,new());
        w.Head.Channel.RestoreState(s.Head);w.RegisterAssets(c,r);w.Producers=AdvisorProducers.FromState(s.Producers,w.Bindings);
        w.Advisor=ParkAdvisor.AllocateShell();w.RegisterAdvisor(false);w.Advisor.Hydrate(s.Advisor,w.Bindings);
        check(w.Events.Count==0,"no voice/sound callbacks on file restore");check(Json(source.Save())==Json(w.Save()),"full advisor provider/head FILE graph");
        w.Events.AddRange(source.Events);return w;
    }
    public static int Run(Disc disc)
    {
        var data=disc.Files().Single(f=>f.Path.Equals("/DATA/DATA.WAD",StringComparison.OrdinalIgnoreCase));
        var wad=new WadArchive(disc.Read(data.Extent,data.Size));
        byte[] Find(string name)=>wad.Read(wad.Entries.Single(e=>e.Path.Equals(name,StringComparison.OrdinalIgnoreCase)));
        var c=AdvisorCatalogue.Load(disc);
        var r=new AdvisorRules(Find("/Generic/Advisor/headers.ass"),Find("/Generic/Advisor/opcodes.ass"));
        int checks=0;void C(bool ok,string why){checks++;if(!ok)throw new Exception("advisor graph: "+why);}
        var source=Create(c,r);source.Advisor.Submit(48);source.Advisor.Submit(56);
        var loaded=Load(source,c,r,C);bool speaking=false;
        for(int i=0;i<1600;i++){
            if(i==75){source.Open=loaded.Open=false;source.Sim.Finances.Credit(3000);loaded.Sim.Finances.Credit(3000);}
            if(i==320)foreach(var w in new[]{source,loaded}){w.Open=true;w.Advisor.Submit(208);}
            if(i==600)foreach(var w in new[]{source,loaded})w.Advisor.Scheduler.AddCounter(19,2);
            source.Tick();loaded.Tick();speaking|=source.Advisor.Speaking;
            C(Json(source.Save())==Json(loaded.Save())&&source.Events.SequenceEqual(loaded.Events),"real producers/clock/head/lips continuation "+i);
            if(i==51||i==97||i==201||i==601)loaded=Load(loaded,c,r,C);
        }
        C(speaking&&source.Advisor.Scheduler.Calls>0&&source.Advisor.Scheduler.Evaluations>0,"speech and real 106-rule scheduler exercised");
        C(!ReferenceEquals(source.Sim,loaded.Sim)&&!ReferenceEquals(source.Clock,loaded.Clock)&&!ReferenceEquals(source.Producers,loaded.Producers),"fresh producer roots");
        return checks;
    }
}
