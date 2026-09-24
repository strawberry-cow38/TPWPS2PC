using System.Text;
using TPW.PS2.Data;

/// <summary>Actual coordinator choices, no direct call to the scoring helper as a substitute.</summary>
static class NativeDestinationChecks
{
    public static void Run(Model terrain,WadArchive data,WadArchive world,string worldName,Action<bool,string> check)
    {
        void Check(bool ok,string label)=>check(ok,"native destination consumer: "+label);
        string stem=worldName switch {"JUNGLE"=>"/Features/Toilet/Toilet","HALLOW"=>"/features/horloo/horloo",
            _=>"/Features/loo/loo"};
        var def=RideDefinition.Parse(Encoding.ASCII.GetString(world.Read(world.Find(stem+".sam"))),
                                    "/DATA/"+worldName+".WAD"+stem+".sam");
        var compiled=new CompiledAssets(new AssetResourceDatabase(data.Read(data.Find("/arsdb.dba"))),TextDatabase.Load(data,"eur"));
        compiled.Attach(new[]{def},out _);
        var rec=def.CompiledEntry ?? throw new InvalidDataException("Native selector fixture not joined");
        Check(rec.Kind==AssetResourceDatabase.AssetKind.Feature && rec.RawFeatureFlags==1,
              "named small toilet uses shipping compiled identity/relief bit");
        string folder=stem[..(stem.LastIndexOf('/')+1)];
        var aps=world.Find(stem+".aps");
        byte[] Sibling(string name)=>world.Find(folder+name) is {} e ? world.Read(e) : null;
        ParkPaths Ground()
        {
            var paths=new ParkPaths(terrain);
            for(int i=1;i<paths.Field.Cells.Length;i+=2) paths.Field.Cells[i]=0;
            return paths;
        }
        var finder=Ground();
        var origin=finder.Cells.First(c=>Enumerable.Range(-3,rec.Width+6).All(x=>
            Enumerable.Range(-3,rec.Depth+6).All(z=>finder.CanLay(c.Offset(x,z)))));
        var entry=origin.Offset(rec.ConnectionA.X,rec.ConnectionA.Z);
        var direction=rec.ConnectionA.Direction switch {0=>new ParkCell(0,-1),1=>new ParkCell(-1,0),2=>new ParkCell(0,1),_=>new ParkCell(1,0)};
        var stub=entry.Offset(direction.X,direction.Z); var start=stub.Offset(direction.X,direction.Z);
        int material=Enumerable.Range(1,finder.Materials.Count-1).First(i=>ParkPaths.Classify(finder.Materials[i])==ParkPathKind.Path);
        (ParkVisitors V, ParkRide R, Guest G) Fixture(byte toilet,byte sick,bool wrongStub=false)
        {
            var paths=Ground(); paths.Lay(stub,material); paths.Lay(start,material);
            var sim=new ParkSim(paths);
            var ride=sim.Add(71,stem,origin,rec.Width,rec.Depth,world.Read(world.Find(stem+".rse")),
                aps==null?null:new Animation(world.Read(aps)),1,wrongStub?start:stub,stub,out var fault,Sibling,definition:def,placementTurns:0)
                ?? throw new InvalidDataException(fault);
            sim.SetOpen(ride.Id,true);
            var needs=new VisitorNeeds(33){SecondsPerRise=1_000_000};
            foreach(var key in needs.Rates.Keys.ToArray()) needs.Rates[key]=new(0,0,false);
            needs.Unknown78Bar=needs.SickBar=needs.ToiletBar=needs.HungerBar=needs.ThirstBar=101;
            var v=new ParkVisitors(sim,new GuestWalk(paths),()=>0){Needs=needs};
            var guest=v.Arrive(start,start);
            needs.Set(guest.Id,new VisitorWants{Cash=1234,Happiness=80,PreferredIntensity=50,Toilet=toilet,Sick=sick});
            Check(ride.DestinationEntry==entry && ride.NativeRelief==!wrongStub && ride.DestinationEligible,
                  $"need{toilet}/sick{sick} wrongStub={wrongStub} fixture distinguishes scoring entry and validated service entry");
            return(v,ride,guest);
        }
        foreach(var (toilet,sick,want) in new[]{(0,0,false),(34,0,false),(90,0,true),(0,90,true),(100,0,true)})
        {
            var(v,r,g)=Fixture((byte)toilet,(byte)sick);
            v.Step(0,null); // destination decision, not an explicit SendTo
            Check((v.Plans[g.Id].Intent==VisitorIntent.Heading)==want,
                  $"actual idle selector need{toilet}/sick{sick} {(want?"chooses":"rejects")} relief");
            for(int i=0;i<60;i++) v.Step(.04,null);
            Check(v.Boardings==(want?1:0) && v.ServiceHidden(g.Id)==want && v.Relieved==0,
                  $"need{toilet}/sick{sick} physical boardings prove selection ran, not just a score helper");
            if(!want) Check(v.Walk.Guests.Contains(g) && v.Needs.Of(g.Id).Cash==1234 && v.Needs.Of(g.Id).Happiness==80,
                           "rejected fresh guest remains visible without a visit or low-score penalty");
        }
        {
            var(v,r,g)=Fixture(100,0); v.Sim.SetOpen(r.Id,false); v.Step(0,null);
            Check(v.Plans[g.Id].Intent==VisitorIntent.Wandering && v.Boardings==0,"closed relief cannot win even at maximal need");
            v.Sim.SetOpen(r.Id,true); r.DestinationState=1; v.Step(0,null);
            Check(v.Plans[g.Id].Intent==VisitorIntent.Wandering,"native construction state1 is not eligible");
            r.DestinationState=10; v.Step(0,null);
            Check(v.Plans[g.Id].Intent==VisitorIntent.Heading,"native operating state10 remains selectable control");
        }
        {
            var(v,r,g)=Fixture(100,0); r.Set("VAR_BROKEN",1); v.Step(0,null);
            Check(v.Plans[g.Id].Intent==VisitorIntent.Wandering,"broken relief cannot be selected");
        }
        {
            var(v,r,g)=Fixture(100,0,wrongStub:true);
            Check(r.RequiresNativeServiceEntry && r.ServiceEntry==null && r.DestinationEntry!=null
                  && v.Walk.Paths.Open(r.Entrance.Value),
                  "wrong reachable stub preserves compiled scoring geometry but fails native service validation");
            Check(!v.Takes(r) && !v.SendTo(g,r),"invalid physical service geometry refuses explicit transport instead of RSE fallback");
            for(int i=0;i<300;i++) v.Step(.04,null);
            Check(v.Plans[g.Id].Intent==VisitorIntent.Wandering && v.Boardings==0 && v.Relieved==0
                  && !v.ServiceHidden(g.Id) && r.Queue.Count==0,
                  "actual idle chooser cannot route invalid compiled toilet into legacy Queued service");
        }
        // Object identity rather than ID orders/penalties: property-level producer control.
        var first=new ParkRide{Id=9,Definition=def}; var second=new ParkRide{Id=2,Definition=def};
        Check(PlacedDestination.InNativeOrder(new[]{first,second}).SequenceEqual(new[]{second,first}),
              "family pool enumerates newest activation first, not numeric ID order");
    }
}
