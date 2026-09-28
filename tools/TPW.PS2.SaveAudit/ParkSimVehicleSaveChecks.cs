using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;

/// <summary>Actual mixed native/script ParkSim file checkpoint and resumed consumer.
/// Does not save terrain, walking guests, staff, UI or external asset registries.</summary>
static class ParkSimVehicleSaveChecks
{
    static string Json<T>(T value)=>JsonSerializer.Serialize(value);
    public static void Run(Action<bool,string> check)
    {
        var bytes=ParkSimSaveChecks.Script(false);var program=new RseProgram(bytes);
        int terrainCalls=0,callbackBinds=0;
        var ground=new TrackGround{World=0,Park=0,Bridged=_=>{terrainCalls++;return false;}};
        var bindings=new ParkSim.StateBindings
        {
            Scripts=new ParkSim.ScriptedBindings
            {
                IdentifyProgram=p=>new("native-script/v1",p,null!),ResolveProgram=_=>new("native-script/v1",program,null!),
                IdentifyAnimation=_=>"headless/v1",ResolveAnimation=_=>null!,BindCallbacks=(_,_)=>callbackBinds++
            },
            IdentifyTrackGround=_=>"map/v1",ResolveTrackGround=key=>key=="map/v1"?ground:throw new ArgumentException(),
            IdentifyCoasterType=_=>"minecart/v1",ResolveCoasterType=key=>key=="minecart/v1"?CoasterType.All[0]:throw new ArgumentException(),
            IdentifyCoasterGround=_=>"map/v1",ResolveCoasterGround=key=>key=="map/v1"?((_,_)=>{terrainCalls++;return 0;}):throw new ArgumentException()
        };
        var original=new ParkSim(null!);
        ParkRide Add(int id)
        {
            var r=original.Add(id,"native "+id,new(40,40),3,2,bytes,null!,4,null,null,out var error);
            check(r!=null&&error==null,"native graph: passive script asset loaded");
            for(int i=1;i<=500;i++)r!.Join(id*1000+i);
            return r!;
        }
        TrackLayout Layout()
        {
            var l=new TrackLayout(new(40,40),0,ground);var e=l.ExitCell;var r=l.ReturnCell;
            int ox=Math.Sign(e.X-r.X),oz=Math.Sign(e.Z-r.Z),sx=-oz,sz=ox;
            foreach(var cell in new[]{e.Offset(6*ox,6*oz),e.Offset(6*ox+8*sx,6*oz+8*sz),
                e.Offset(-8*ox+8*sx,-8*oz+8*sz),e.Offset(-8*ox,-8*oz),r})l.Add(cell);
            return l;
        }
        var boat=Add(1);var kart=Add(2);var train=Add(3);
        // Two native sims intentionally share one layout; preserve that graph identity.
        var layout=Layout();var boats=original.AttachTrack(1,layout,17,false);var karts=original.AttachTrack(2,layout,19,true);
        boats.Duration=1;karts.Duration=1;boats.Capacity=4;karts.Capacity=4;
        var ring=new CoasterTrack(CoasterType.All[0],new(40,41),3,new(35,41));
        (int X,int Z)[] oval={(46,41),(51,45),(51,51),(46,55),(40,55),(34,55),(29,51),(29,46),(31,41)};
        int[] heights={1280,0,0,0,0,0,0,375,375};
        for(int i=0;i<oval.Length;i++)ring.AddPylon(new(oval[i].X,oval[i].Z),heights[i],0,false,CoasterNodeKind.Normal);
        ring.AddPylon(new(35,41),0,0,false,CoasterNodeKind.Normal);
        original.AttachCoaster(3,ring);
        foreach(var r in original.Rides)original.SetOpen(r.Id,true);
        int warm=0;
        while(warm++<15000)
        {
            original.Advance(.04);
            if(boats.Riders>0&&karts.Riders>0&&train.Coaster.Riders>0&&train.Coaster.Trains.Any(t=>t.State==CoasterTrainState.Run))break;
        }
        check(warm<15000,"native graph: boats, karts and coaster all occupied before checkpoint");
        original.Advance(.013);long cut=original.Time;
        var saved=original.CaptureState(bindings);int queries=terrainCalls;
        string path=Path.Combine(Path.GetTempPath(),"tpw-native-"+Guid.NewGuid().ToString("N")+".tpwsave");
        ParkSim loaded;
        try {ParkSaveFile.Write(path,saved);loaded=ParkSim.FromState(ParkSaveFile.Read<ParkSim.State>(path),null!,bindings);}
        finally {if(File.Exists(path))File.Delete(path);}
        check(queries==terrainCalls&&callbackBinds==1&&Json(saved)==Json(loaded.CaptureState(bindings)),
            "native graph: full file roundtrip, no terrain rebuild/init/defaults/events");
        check(saved.TrackLayouts.Length==1&&saved.TrackRides.Length==2
            &&ReferenceEquals(loaded.Rides[0].Track.Track,loaded.Rides[1].Track.Track)
            &&!ReferenceEquals(loaded.Rides[0].Track.Track,layout),"native graph: shared layout identity preserved in fresh owners");
        check(!ReferenceEquals(loaded.Rides[2].Coaster.Track.Exit,ring.Exit)
            &&loaded.Rides[2].Coaster.Trains.All(t=>t.Cars.All(c=>loaded.Rides[2].Coaster.Track.Owns(c.Node))),
            "native graph: all coaster car pointers resolve into restored topology");
        var sourceEvents=new List<string>();var targetEvents=new List<string>();
        void Listen(ParkSim park,List<string> log)
        {
            foreach(var r in park.Rides)
            {
                if(r.Track is {} t) {t.Boarded+=(g,c)=>log.Add($"{r.Id}:board:{g}:{c.Index}");t.Released+=g=>log.Add($"{r.Id}:release:{g}");t.SoundCue+=(c,n)=>log.Add($"{r.Id}:sound:{c.Index}:{n}");}
                if(r.Coaster is {} c) {c.Released+=g=>log.Add($"{r.Id}:release:{g}");c.Scream+=(t,n)=>log.Add($"{r.Id}:scream:{t.Index}:{n}");}
            }
        }
        Listen(original,sourceEvents);Listen(loaded,targetEvents);
        long wear=original.WearEvents;int left=original.Rides.Sum(r=>r.Left.Count);int queued=original.Rides.Sum(r=>r.Queue.Count);
        bool all=true;int first=-1;
        for(int i=0;i<2000;i++)
        {
            double dt=i%3==0?.017:i%3==1?.023:.08;original.Advance(dt);loaded.Advance(dt);
            // Entire native owner state (car RNG/order, private cooldowns, geometry) plus
            // the actual ParkSim queue/release/wear callbacks, not just standalone Step.
            if(i%20==0&&Json(original.CaptureState(bindings))!=Json(loaded.CaptureState(bindings))){all=false;if(first<0)first=i;}
            if(!sourceEvents.SequenceEqual(targetEvents)){all=false;if(first<0)first=i;}
            sourceEvents.Clear();targetEvents.Clear();
        }
        check(all,$"native graph: real ParkSim.Advance resumes every owner and event (first mismatch {first})");
        check(original.Time>cut&&original.WearEvents>wear&&original.Rides.Sum(r=>r.Left.Count)>left
            &&original.Rides.Sum(r=>r.Queue.Count)<queued,"native graph: non-inert wear, boarding and release after load");
        string stable=Json(original.CaptureState(bindings));
        void Reject(Action<JsonObject> change,string name)
        {
            var n=JsonNode.Parse(Json(saved))!.AsObject();change(n);bool rejected=false;int bindsBefore=callbackBinds;
            try {ParkSim.FromState(JsonSerializer.Deserialize<ParkSim.State>(n.ToJsonString())!,null!,bindings);}
            catch(ArgumentException){rejected=true;}catch(JsonException){rejected=true;}
            check(rejected&&callbackBinds==bindsBefore&&Json(original.CaptureState(bindings))==stable,"native graph rejects without source mutation: "+name);
        }
        Reject(n=>n["TrackRides"]![0]!["LayoutId"]="missing","missing layout");
        Reject(n=>n["Core"]!["Rides"]![1]!["TrackId"]="t0","one sim bound to two rides");
        Reject(n=>n["Coasters"]![0]!["TrackId"]="missing","missing coaster topology");
        Reject(n=>n["TrackLayouts"]![0]!["Layout"]!["GroundKey"]="different","foreign ground");
        Reject(n=>n["Core"]!["Rides"]![0]!["TrackId"]=null,"unowned native sim");
        Reject(n=>n["TrackRides"]![1]!["Id"]="t0","duplicate sim ID");
        Reject(n=>n.Remove("Coasters"),"missing required native owner table");
        Console.WriteLine($"Native ParkSim file checkpoint PASS: boats/karts/coaster, cut={cut}ms, continuation={original.Time-cut}ms");
    }
}
