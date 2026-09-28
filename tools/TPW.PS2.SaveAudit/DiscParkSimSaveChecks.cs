using System.Security.Cryptography;
using System.Text.Json;
using TPW.PS2.Data;

/// <summary>Real scripted ParkSim checkpoint, not a whole Viewer/guest/terrain snapshot.</summary>
static class DiscParkSimSaveChecks
{
    public static void Run(Disc disc, Action<bool,string> check)
    {
        string Json<T>(T value) => JsonSerializer.Serialize(value);
        string Shape(RseProgram p) => Json(new { p.VariableNames, p.StackSize, p.SliceBudget,
            p.WalkCapacity, p.BounceCapacity, p.LimboCapacity, p.Instructions,
            Strings=p.Instructions.SelectMany(i=>i.Operands).Where(o=>o.Tag==0x10)
                .Select(o=>new {o.Index, Text=p.StringAt(o.Index)}).ToArray() });
        foreach (string world in new[]{"FANTASY","HALLOW","SPACE"})
        {
            var file=disc.Files().Single(f=>f.Path=="/DATA/"+world+".WAD");
            var wad=new WadArchive(disc.Read(file.Extent,file.Size));
            var fixture=new VisitorScenario(wad,world);
            string stem=fixture.RideStem, folder=stem[..(stem.LastIndexOf('/')+1)];
            byte[] Read(string name)=>wad.Read(wad.Find(name)??throw new InvalidDataException(name));
            string Key(string path,byte[] bytes)=>world+path+":"+Convert.ToHexString(SHA256.HashData(bytes));
            var scripts=wad.Entries.Where(e=>e.Path.StartsWith(folder,StringComparison.OrdinalIgnoreCase)
                && e.Path.EndsWith(".rse",StringComparison.OrdinalIgnoreCase)).ToArray();
            var byKey=new Dictionary<string,ParkSim.ScriptAsset>();
            var byPath=new Dictionary<string,ParkSim.ScriptAsset>(StringComparer.OrdinalIgnoreCase);
            var byShape=new Dictionary<string,ParkSim.ScriptAsset>();
            foreach(var entry in scripts)
            {
                byte[] bytes=wad.Read(entry);var p=new RseProgram(bytes);
                var a=new ParkSim.ScriptAsset(Key(entry.Path,bytes),p,world+folder);
                byKey.Add(a.Key,a);byPath.Add(entry.Path,a);byShape.TryAdd(Shape(p),a);
            }
            string animationKey=Key(stem+".aps",Read(stem+".aps"));
            string definitionKey=Key(stem+".sam",Read(stem+".sam"));
            ParkSim.ScriptAsset Child(string name)
            {
                var path=folder+name;
                return byPath.GetValueOrDefault(path)??byPath.GetValueOrDefault(path+".rse")!;
            }
            int restoreEffects=0;
            var bindings=new ParkSim.ScriptedBindings
            {
                IdentifyProgram=p=>byShape[Shape(p)] with {Program=p}, ResolveProgram=key=>byKey[key],
                ResolveChild=(_,name)=>Child(name),
                IdentifyAnimation=_=>animationKey,ResolveAnimation=key=>key==animationKey?fixture.RideAnimation:throw new ArgumentException(),
                IdentifyDefinition=d=>ReferenceEquals(d,fixture.Definition)?definitionKey:throw new ArgumentException(),
                ResolveDefinition=key=>key==definitionKey?fixture.Definition:throw new ArgumentException(),
                BindCallbacks=(_,hosts)=>{foreach(var host in hosts.Values)host.EffectRequested+=_=>restoreEffects++;}
            };
            var source=new ParkSim(new ParkPaths(fixture.Terrain));
            var ride=source.Add(51,fixture.Definition.Name,fixture.RideOrigin,fixture.RideWidth,fixture.RideHeight,
                Read(stem+".rse"),fixture.RideAnimation,fixture.Definition.UpgradeCapacity(0)??1,null,null,out var fault,
                sibling:name=>{var e=wad.Find(folder+name)??wad.Find(folder+name+".rse");return e==null?null!:wad.Read(e);},
                headSlots:fixture.RideModel.Fittings.Count(f=>(f.Flags&0x80)!=0),definition:fixture.Definition);
            check(ride!=null&&fault==null,world+": actual ParkSim ride constructed");
            source.SetOpen(51,true);for(int i=101;i<=124;i++)ride!.Join(i);
            long cut=0;
            for(int i=0;i<6000;i++)
            {
                source.Advance(.04);
                if(ride!.Machine.GuestIds.Count>0&&ride.Host.Frame>0){cut=source.Time;break;}
            }
            check(cut>0&&ride!.Fault==null,world+": ParkSim script really boarded a guest");
            source.Advance(.013);source.Finances.CreditByKind(4,17);source.Finances.MonthEnd(3);
            source.HandleOf(ride!.Machine);ride.Book(12,-3);
            var before=source.CaptureScriptedState(bindings);
            string path=Path.Combine(Path.GetTempPath(),"tpw-parkgraph-"+Guid.NewGuid().ToString("N")+".tpwsave");
            ParkSim restored;
            try
            {
                ParkSaveFile.Write(path,before);
                restored=ParkSim.FromScriptedState(ParkSaveFile.Read<ParkSim.ScriptedState>(path),new ParkPaths(fixture.Terrain),bindings);
            }
            finally{if(File.Exists(path))File.Delete(path);}
            check(restoreEffects==0&&Json(restored.CaptureScriptedState(bindings))==Json(before),world+": file restores complete scripted graph without creation/effect replay");
            check(restored.Rides[0].Machine.GuestIds.SequenceEqual(ride.Machine.GuestIds)
                &&!ReferenceEquals(restored.Rides[0],ride)&&!ReferenceEquals(restored.Rides[0].Host,ride.Host),world+": fresh objects keep boarded guests");
            int initialPc=ride.Machine.Pc;bool changed=false;
            for(int i=0;i<1200;i++)
            {
                double dt=i%3==0?.017:i%3==1?.023:.08;
                source.Advance(dt);restored.Advance(dt);
                changed|=ride.Machine.Pc!=initialPc;
                check(source.Time==restored.Time&&ride.Machine.Pc==restored.Rides[0].Machine.Pc
                    &&BitConverter.SingleToInt32Bits(ride.Frame)==BitConverter.SingleToInt32Bits(restored.Rides[0].Frame),world+": live park PC/time/frame continuation");
                if(i%25==0)check(Json(source.CaptureScriptedState(bindings))==Json(restored.CaptureScriptedState(bindings)),world+": every private graph state continues identically");
            }
            check(changed&&ride.Fault==null&&restored.Rides[0].Fault==null,world+": continuation is running, not an inert/faulted equality");
            Console.WriteLine($"ParkSim checkpoint {world}: cut={cut}ms, continued={source.Time-cut}ms, fresh file graph PASS (paths/visitors not saved)");
        }
    }
}
