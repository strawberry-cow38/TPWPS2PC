using System.Text;
using TPW.PS2.Data;

static class TerminalWalkingChecks
{
    public static void Run(Model terrain, WadArchive data, WadArchive world, string worldName, Action<bool,string> check)
    {
        void Check(bool ok,string text)=>check(ok,"terminal walking: "+text);
        ParkPaths Empty()
        {
            var p=new ParkPaths(terrain);
            for(int i=1;i<p.Field.Cells.Length;i+=2)p.Field.Cells[i]=0;
            return p;
        }
        var paths=Empty();
        var centre=paths.Cells.First(c=>Enumerable.Range(-3,7).All(x=>Enumerable.Range(-3,7).All(z=>paths.CanLay(c.Offset(x,z)))));
        int material=Enumerable.Range(1,paths.Materials.Count-1).First(i=>ParkPaths.Classify(paths.Materials[i])==ParkPathKind.Path);
        var dirs=new[]{new ParkCell(0,-1),new ParkCell(1,0),new ParkCell(0,1),new ParkCell(-1,0)};
        for(int turn=0;turn<4;turn++)
        {
            paths=Empty();var outwards=dirs[turn];var approach=centre.Offset(outwards.X,outwards.Z);
            var start=approach.Offset(outwards.X,outwards.Z);
            paths.Lay(start,material);paths.Lay(approach,material);
            bool live=true;
            var owner=new ParkRide{Id=17};
            var token=new GuestTerminal(owner,approach,centre,()=>live);
            var walk=new GuestWalk(paths);var g=walk.Spawn(start,start);var stranger=walk.Spawn(start,start);
            Check(walk.Route(start,centre)==null && !paths.Walkable(centre),$"turn{turn} footprint is not globally opened");
            Check(walk.SendToTerminal(g,token) && g.Destination==centre,$"turn{turn} named terminal is reachable");
            Check(!walk.Send(stranger,centre),$"turn{turn} another guest cannot borrow destination permission");
            for(int i=0;i<25;i++)walk.Step();
            Check(g.Cell==approach && g.State==GuestState.Walking,$"turn{turn} arrival at stub is not service arrival");
            walk.Step();var pos=g.Position;
            Check(g.Next==centre && g.Progress==40 && g.Position!=ParkPaths.Centre(centre),$"turn{turn} real approach leg has interpolated walking progress");
            Check(!walk.Send(g,start) && g.Position==pos,$"turn{turn} mid-edge retarget cannot teleport");
            live=false; // closing/deleting cannot erase the position already being walked
            for(int i=0;i<24;i++)walk.Step();
            Check(g.Cell==centre && g.Next==null && g.Progress==0,$"turn{turn} committed edge finishes continuously despite revoked entry");
            Check(walk.Send(g,start),$"turn{turn} occupied terminal retains a route out after closure");
            walk.Step();
            Check(g.Next==approach && g.Progress==40,$"turn{turn} egress starts with the reverse edge, not a jump");
            for(int i=0;i<49;i++)walk.Step();
            Check(g.Cell==start && g.State==GuestState.Arrived && !walk.SendToTerminal(g,token),$"turn{turn} revoked owner cannot admit a new visit");
            live=true;Check(walk.SendToTerminal(g,token),$"turn{turn} restored owner permits a fresh physical visit");
            live=false;for(int i=0;i<26;i++)walk.Step();
            Check(g.State==GuestState.Stranded && g.Cell==approach,$"turn{turn} revocation before commitment strands rather than entering");
        }

        paths=Empty();
        var north=centre.Offset(0,-1);var east=centre.Offset(1,0);
        paths.Lay(east,material);
        var isolated=new GuestWalk(paths);var wrongSide=isolated.Spawn(east,east);
        var directed=new GuestTerminal(new ParkRide{Id=23},north,centre,()=>true);
        Check(!isolated.SendToTerminal(wrongSide,directed) && wrongSide.Cell==east && wrongSide.State==GuestState.Arrived,
              "wrong-side neighbour cannot enter when the directed approach is absent and failure preserves walk");

        paths=Empty();paths.Lay(north,material);
        var retainedWalk=new GuestWalk(paths);var oldOwner=new ParkRide{Id=31};
        var oldToken=new GuestTerminal(oldOwner,north,centre,()=>false);
        var held=retainedWalk.ReadmitTerminal(999,oldToken);
        var replacement=new GuestTerminal(new ParkRide{Id=31},north,centre,()=>true);
        Check(!retainedWalk.SendToTerminal(held,replacement) && held.Cell==centre,
              "same-ID same-position replacement cannot inherit occupied doorway permission");
        bool duplicateRejected=false;
        try { retainedWalk.ReadmitTerminal(999,oldToken); } catch(ArgumentException) { duplicateRejected=true; }
        Check(duplicateRejected && retainedWalk.Guests.Count==1,"terminal readmission cannot duplicate a live identity");
        Check(retainedWalk.Send(held,north),"demolished owner's retained doorway still permits egress");
        for(int i=0;i<25;i++)retainedWalk.Step();
        Check(held.Cell==north && retainedWalk.SendToTerminal(held,replacement),
              "replacement is usable only after a physical exit and fresh approach");

        string stem=worldName is "HALLOW" or "SPACE" ? "/Shops/ices/ices" : "/Shops/IceCream/IceCream";
        var def=RideDefinition.Parse(Encoding.ASCII.GetString(world.Read(world.Find(stem+".sam"))),"/DATA/"+worldName+".WAD"+stem+".sam");
        var compiled=new CompiledAssets(new AssetResourceDatabase(data.Read(data.Find("/arsdb.dba"))),TextDatabase.Load(data,"eur"));
        Check(compiled.Attach(new[]{def},out _)==1 && def.CompiledEntry?.Kind==AssetResourceDatabase.AssetKind.Shop,
              "real named shop retains full identity-joined compiled geometry");
        var rec=def.CompiledEntry;var a=rec.ConnectionA;
        var entries=worldName=="HALLOW" ? new[]{new ParkCell(1,0),new ParkCell(1,1),new ParkCell(0,1),new ParkCell(0,0)}
            : new[]{new ParkCell(0,0),new ParkCell(1,0),new ParkCell(1,1),new ParkCell(0,1)};
        Check(rec.Width==2 && rec.Depth==2 && a.Direction==0 && a.X==entries[0].X && a.Z==entries[0].Z,
              "named retail connection matches literal two-by-two fixture");
        for(int turn=0;turn<4;turn++)
        {
            var point=centre.Offset(entries[turn].X,entries[turn].Z);
            Check(ShopEntrance.Inside(rec,centre,turn,2,2,point.Offset(dirs[turn].X,dirs[turn].Z))==point,
                  $"compiled turn{turn} entry and outward link match independent literal oracle");
        }
        // Turn0 connection is asserted directly against the bytes, not a helper round-trip.
        var origin=centre;
        var entry=origin.Offset(a.X,a.Z);
        var direction=a.Direction switch{0=>new ParkCell(0,-1),1=>new ParkCell(-1,0),2=>new ParkCell(0,1),_=>new ParkCell(1,0)};
        var stub=entry.Offset(direction.X,direction.Z);var publicStart=stub.Offset(direction.X,direction.Z);
        Check(ShopEntrance.Inside(rec,origin,0,rec.Width,rec.Depth,stub)==entry,
              "compiled inside cell is one step beyond the public stub");
        Check(ShopEntrance.Inside(rec,origin,0,rec.Width,rec.Depth,stub.Offset(1,1))==null,
              "mismatched placement refuses a guessed entrance");
        string folder=stem[..(stem.LastIndexOf('/')+1)];
        var aps=world.Find(stem+".aps") ?? world.Entries.Single(e=>e.Path.StartsWith(folder,StringComparison.OrdinalIgnoreCase)
            && !e.Path[folder.Length..].Contains('/') && e.Path.EndsWith(".aps",StringComparison.OrdinalIgnoreCase));
        byte[] Sibling(string name)=>world.Find(folder+name) is {} e ? world.Read(e) : null;
        foreach(int cash in new[]{1234,299})
        {
            paths=Empty();paths.Lay(stub,material);paths.Lay(publicStart,material);
            var sim=new ParkSim(paths);
            var ride=sim.Add(1,"physical shop",origin,rec.Width,rec.Depth,world.Read(world.Find(stem+".rse")),new Animation(world.Read(aps)),1,
                stub,stub,out var fault,sibling:Sibling,definition:def,placementTurns:0) ?? throw new InvalidOperationException(fault);
            sim.SetOpen(1,true);ride.Set("VAR_BROKEN",0);
            var v=new ParkVisitors(sim,new GuestWalk(paths),()=>0){Needs=new VisitorNeeds(42){SecondsPerRise=1_000_000}};
            foreach(var key in v.Needs.Rates.Keys.ToArray())v.Needs.Rates[key]=new(0,0,false);
            var g=v.Arrive(publicStart,publicStart);
            v.Needs.Set(g.Id,new VisitorWants{Cash=cash,Hunger=91,Happiness=50,PreferredIntensity=90});
            Check(ride.ServiceEntry==entry && v.SendTo(g,ride),$"cash{cash} coordinator consumes compiled entrance");
            for(int i=0;i<25;i++)v.Step(.04,null);
            Check(g.Cell==stub && v.Boardings==0 && v.Walk.Guests.Contains(g),$"cash{cash} coordinator cannot board from the stub");
            v.Step(.04,null);
            Check(g.Next==entry && g.Progress==40 && v.Boardings==0,$"cash{cash} body stays on walking layer throughout approach");
            for(int i=0;i<24;i++)v.Step(.04,null);
            Check(g.Cell==entry && v.Boardings==1 && v.Plans[g.Id].At==entry,$"cash{cash} boarding occurs only at inside cell centre");
            for(int i=0;i<6000 && v.Rides==0;i++)v.Step(.04,null);
            var returned=v.Walk.Guests.SingleOrDefault(x=>x.Id==g.Id);
            Check(v.Rides==1 && v.Purchases==(cash==1234?1:0) && returned?.Cell==entry && returned.Next==null,
                  $"cash{cash} real handback retains inside position after success or refusal");
            Check(returned!=null && v.Walk.Send(returned,publicStart),$"cash{cash} returned guest can route back onto public ground");
            v.Step(.04,null);
            Check(returned?.Next==stub && returned.Progress==40,$"cash{cash} post-service departure is an actual reverse walking leg");
        }
        foreach(int ticks in new[]{25,26,50})
        {
            paths=Empty();paths.Lay(stub,material);paths.Lay(publicStart,material);
            var sim=new ParkSim(paths);
            ParkRide Place()
            {
                var r=sim.Add(71,"deletion fixture",origin,rec.Width,rec.Depth,world.Read(world.Find(stem+".rse")),new Animation(world.Read(aps)),1,
                    stub,stub,out var fault,sibling:Sibling,definition:def,placementTurns:0) ?? throw new InvalidOperationException(fault);
                sim.SetOpen(71,true);r.Set("VAR_BROKEN",0);return r;
            }
            var owner=Place();var v=new ParkVisitors(sim,new GuestWalk(paths),()=>0){Needs=new VisitorNeeds(71){SecondsPerRise=1_000_000}};
            foreach(var key in v.Needs.Rates.Keys.ToArray())v.Needs.Rates[key]=new(0,0,false);
            var g=v.Arrive(publicStart,publicStart);v.Needs.Set(g.Id,new VisitorWants{Cash=1234,Hunger=91,Happiness=50});
            Check(v.SendTo(g,owner),$"remove{ticks} real coordinator begins the controlled approach");
            for(int i=0;i<ticks;i++)v.Step(.04,null);
            var before=g.Position;sim.Remove(owner.Id);v.Step(0,null);
            var survivor=v.Walk.Guests.Single(x=>x.Id==g.Id);
            Check(survivor.Position==before && v.Rides==0 && v.Purchases==0 && v.Needs.Of(g.Id).Cash==1234,
                  $"remove{ticks} deletion preserves position identity and needs without awarding service");
            if(ticks==26)for(int i=0;i<24;i++)v.Step(.04,null);
            var replacementRide=Place();
            if(ticks!=25)
                Check(survivor.Cell==entry && !v.SendTo(survivor,replacementRide),
                      $"remove{ticks} actual same-ID replacement cannot inherit an inside guest");
            else
                Check(survivor.Cell==stub && v.Boardings==0,"remove25 pre-entry demolition leaves guest outside unboarded");
            sim.SetOpen(71,false);
            Check(v.Walk.Send(survivor,publicStart),$"remove{ticks} surviving guest has an escape route");
            v.Step(.04,null);
            Check(survivor.Progress==40 && survivor.Next==(ticks==25?publicStart:stub),
                  $"remove{ticks} recovery leaves by a real walking edge");
        }
    }
}
