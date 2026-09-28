using System.Text.Json;
using TPW.PS2.Data;

/// <summary>Real ParkVisitors/Needs/Walk/ParkSim FILE continuation with occupied disc rides.
/// No staff, admission controller, asynchronous requests or Viewer/presentation claim.</summary>
static class DiscVisitorGraphSaveChecks
{
    sealed record Cut
    {
        public required ParkPaths.State Ground {get;init;}
        public required ParkSim.ScriptedState Sim {get;init;}
        public required GuestWalk.State Walk {get;init;}
        public required VisitorNeeds.State Needs {get;init;}
        public required ParkVisitors.State Visitors {get;init;}
    }
    static string Json<T>(T value)=>JsonSerializer.Serialize(value);
    public static void Run(Disc disc,Action<bool,string> check)
    {
        foreach(string world in new[]{"FANTASY","HALLOW","SPACE"})
        {
            var file=disc.Files().Single(f=>f.Path=="/DATA/"+world+".WAD");
            var wad=new WadArchive(disc.Read(file.Extent,file.Size));var scene=new VisitorScenario(wad,world);
            byte[] script=wad.Read(wad.Find(scene.RideStem+".rse")!);var program=new RseProgram(script);
            string gridKey=world+"/terrain_1",programKey=world+scene.RideStem+"/rse",animKey=world+scene.RideStem+"/aps",definitionKey=world+scene.RideStem+"/sam";
            var scripts=new ParkSim.ScriptedBindings
            {
                IdentifyProgram=p=>new(programKey,p,null!),ResolveProgram=k=>k==programKey?new(k,program,null!):throw new ArgumentException(),
                IdentifyAnimation=_=>animKey,ResolveAnimation=k=>k==animKey?scene.RideAnimation:throw new ArgumentException(),
                IdentifyDefinition=d=>ReferenceEquals(d,scene.Definition)?definitionKey:throw new ArgumentException(),
                ResolveDefinition=k=>k==definitionKey?scene.Definition:throw new ArgumentException()
            };
            ParkVisitors.StateBindings References(GuestWalk.GuestGraph graph,ParkVisitors visitors,VisitorNeeds needs,
                ParkSim sim,Action<int,int,ParkCell> sound)=>new()
            {
                GuestGraph=graph,
                IdentifyReference=o=>ReferenceEquals(o,needs)?"needs":ReferenceEquals(o,sound)?"sound":
                    o is ParkRide r&&sim.Rides.Any(x=>ReferenceEquals(x,r))?"ride/"+r.Id:throw new ArgumentException("Unknown visitor binding"),
                ResolveReference=k=>k=="needs"?needs:k=="sound"?sound:k.StartsWith("ride/")?
                    sim.Rides.Single(r=>r.Id==int.Parse(k[5..])):throw new ArgumentException(k)
            };
            VisitorNeeds.StateBindings NeedsBindings(ParkVisitors v)=>new()
            {
                IdentifySounded=d=>d.Equals(v.StateForwardSound)?"visitor/forward-sound":throw new ArgumentException("sound bundle"),
                ResolveSounded=k=>k=="visitor/forward-sound"?v.StateForwardSound:throw new ArgumentException(k),
                IdentifyNearbyLitter=d=>d.Equals(v.StateNearbyLitter)?"visitor/litter":throw new ArgumentException("litter bundle"),
                ResolveNearbyLitter=k=>k=="visitor/litter"?v.StateNearbyLitter:throw new ArgumentException(k)
            };
            Cut Capture(ParkVisitors v,Action<int,int,ParkCell> sound)
            {
                var graph=v.Walk.CaptureGraph(gridKey,null!,v.ReferencedGuests);
                return new(){Ground=v.Walk.Paths.CaptureState(gridKey),Sim=v.Sim.CaptureScriptedState(scripts),Walk=graph.Snapshot,
                    Needs=v.Needs.CaptureState(NeedsBindings(v)),Visitors=v.CaptureState(References(graph,v,v.Needs,v.Sim,sound))};
            }
            var paths=scene.Simulation.Paths;var walk=new GuestWalk(paths);var sim=new ParkSim(paths);
            var head=scene.Simulation.QueueCells[0];var away=head.Offset(0,6);
            var ride=sim.Add(91,scene.Definition.Name,scene.RideOrigin,scene.RideWidth,scene.RideHeight,script,scene.RideAnimation,
                scene.Definition.UpgradeCapacity(0)??1,head,scene.Simulation.ExitPortal,out var fault,
                headSlots:scene.RideModel.Fittings.Count(f=>(f.Flags&0x80)!=0),definition:scene.Definition);
            check(ride!=null&&fault==null,world+": visitor graph loaded disc script");sim.SetOpen(91,true);
            var needs=new VisitorNeeds(41);var original=new ParkVisitors(sim,walk){Needs=needs,AutoService=false};
            var soundsA=new List<string>();var soundsB=new List<string>();
            Action<int,int,ParkCell> soundA=(id,code,cell)=>soundsA.Add($"{sim.Time}:{id}:{code}:{cell}");original.GuestSound=soundA;
            var rider=original.Arrive(head,head);original.SendTo(rider,ride!);
            original.Arrive(away,away.Offset(2,0));
            long cutAt=0;
            for(int i=0;i<8000;i++)
            {
                original.Step(.04,()=>away);
                if(ride!.Machine.GuestIds.Count>0&&original.Plans[rider.Id].Intent==VisitorIntent.Queued){cutAt=sim.Time;break;}
            }
            check(cutAt>0&&ride!.Fault==null,world+": actual coordinator has a guest aboard and another walking");
            original.Step(.013,()=>away);
            Cut saved=Capture(original,soundA);
            string path=Path.Combine(Path.GetTempPath(),"tpw-visitors-"+Guid.NewGuid().ToString("N")+".tpwsave");
            try{ParkSaveFile.Write(path,saved);saved=ParkSaveFile.Read<Cut>(path);}finally{if(File.Exists(path))File.Delete(path);}
            var freshPaths=ParkPaths.FromState(saved.Ground,gridKey,scene.Terrain);
            var freshSim=ParkSim.FromScriptedState(saved.Sim,freshPaths,scripts);
            var freshWalk=GuestWalk.AllocateState(saved.Walk,gridKey,freshPaths);
            VisitorNeeds freshNeeds=null!;ParkVisitors resumed=null!;
            Action<int,int,ParkCell> soundB=(id,code,cell)=>soundsB.Add($"{freshSim.Time}:{id}:{code}:{cell}");
            var earlyRefs=References(freshWalk,resumed,freshNeeds,freshSim,soundB);
            resumed=ParkVisitors.AllocateState(saved.Visitors,freshSim,earlyRefs);
            freshNeeds=VisitorNeeds.FromState(saved.Needs,NeedsBindings(resumed));
            freshWalk.Hydrate(null!);
            resumed.HydrateStateBindings(References(freshWalk,resumed,freshNeeds,freshSim,soundB));
            check(soundsB.Count==0&&Json(saved)==Json(Capture(resumed,soundB)),world+": two-phase whole visitor graph/file roundtrip, no sounds or spawn replay");
            soundsA.Clear();int before=original.Rides;bool equal=true;int divergence=-1;
            for(int i=0;i<2000;i++)
            {
                // New arrivals exercise continuing allocator and RNG, not just an inert cohort.
                if(i is 200 or 900){original.Arrive(away,away.Offset(1,0));resumed.Arrive(away,away.Offset(1,0));}
                double dt=i%3==0?.017:i%3==1?.023:.08;original.Step(dt,()=>away);resumed.Step(dt,()=>away);
                if(i%20==0&&Json(Capture(original,soundA))!=Json(Capture(resumed,soundB))) {equal=false;if(divergence<0)divergence=i;}
                if(!soundsA.SequenceEqual(soundsB)){equal=false;if(divergence<0)divergence=i;}
                soundsA.Clear();soundsB.Clear();
            }
            check(equal,$"{world}: actual ParkVisitors.Step + needs, decisions, walk, RSE complete state/events (first mismatch {divergence})");
            check(original.Rides>before&&freshSim.Time>saved.Sim.Time&&ride.Fault==null&&freshSim.Rides[0].Fault==null,
                world+": saved rider really unloads, returns and keeps living after restore");
            Console.WriteLine($"Visitor graph FILE PASS {world}: cut={cutAt}ms, continued={sim.Time-saved.Sim.Time}ms, finished rides={original.Rides-before}");
        }
    }
}
