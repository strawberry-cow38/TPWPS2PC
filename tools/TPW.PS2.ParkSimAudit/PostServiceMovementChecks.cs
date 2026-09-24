using TPW.PS2.Data;

/// <summary>The destination deadline must not suppress state0's ordinary movement arm.
/// Use actual service/purchase scripts and a controlled arm1 draw, not a synthetic
/// completion marker. This tests the shared coordinator for both relief and shops.</summary>
static class PostServiceMovementChecks
{
    public static void Run(Model terrain, ParkPaths source, ParkCell at, ParkCell away,
        byte[] script, Animation aps, RideDefinition def, Func<string,byte[]> sibling,
        Action<bool,string> check)
    {
        void Check(bool ok,string label)=>check(ok,"post service movement: "+label);
        foreach(int cash in def.ProvidesRelief ? new[]{1234} : new[]{1234,299})
        {
            string tag=(def.ProvidesRelief?"relief":"shop")+cash;
            var paths=new ParkPaths(terrain);source.Field.Cells.CopyTo(paths.Field.Cells,0);
            var sim=new ParkSim(paths);
            var ride=sim.Add(1,tag,at,1,1,script,aps,1,at,at,out var fault,sibling:sibling,definition:def)
                ?? throw new InvalidOperationException(fault);
            sim.SetOpen(1,true);ride.Set("VAR_BROKEN",0);
            // rand6==1: the native ordinary-movement arm, even while rand300=1 leaves
            // facility selection unavailable. This intentionally never selects arm0.
            int draw=1;
            var v=new ParkVisitors(sim,new GuestWalk(paths),()=>draw){Needs=new VisitorNeeds(61){SecondsPerRise=1_000_000}};
            foreach(var key in v.Needs.Rates.Keys.ToArray())v.Needs.Rates[key]=new(0,0,false);
            v.Needs.Unknown78Bar=v.Needs.SickBar=v.Needs.ToiletBar=v.Needs.HungerBar=v.Needs.ThirstBar=101;
            var route=v.Walk.Route(at,away);
            if(route==null || route.Count<3)throw new InvalidOperationException("movement fixture needs two actual public edges");
            var nearby=route[2];
            int sounds=0;var soundIds=new List<int>();v.GuestSound=(_,eventId,_)=>
            {
                soundIds.Add(eventId);
                // Unrelated mood sounds are not service callbacks. The native happy event
                // may legitimately fire while this satisfied guest is walking away.
                if(eventId is VisitorNeeds.Sounds.Flush or VisitorNeeds.Sounds.LavatoryDoor or VisitorNeeds.Sounds.ShopTill)sounds++;
            };
            var g=v.Arrive(at,at);
            v.Needs.Set(g.Id,new VisitorWants { Cash=cash,Hunger=(byte)(def.ProvidesRelief?0:91),
                Toilet=(byte)(def.ProvidesRelief?91:0),Happiness=50,PreferredIntensity=90 });
            if(!v.SendTo(g,ride))throw new InvalidOperationException("cannot seed service visit");
            for(int tick=0;tick<6000 && v.Rides==0;tick++)v.Step(.04,()=>nearby);
            Check(v.Rides==1 && v.Boardings==1 && v.Relieved==(def.ProvidesRelief?1:0)
                && v.Purchases==(!def.ProvidesRelief && cash==1234?1:0),tag+" actually completes the intended service or refusal");
            var returned=v.Walk.Guests.Single(x=>x.Id==g.Id);
            var before=returned.Position;long clock=sim.Time;
            var needs=v.Needs.Of(g.Id);int visits=v.Rides,purchases=v.Purchases,played=sounds,balance=sim.Finances.Balance;
            for(int i=0;i<20;i++)v.Step(0,()=>nearby);
            Check(sim.Time==clock && returned.Position==before && v.Boardings==1,
                tag+" zero-time calls neither walk nor start another visit");
            for(int i=0;i<120;i++)v.Step(.04,()=>nearby);
            var after=v.Needs.Of(g.Id);
            Console.WriteLine($"  post service trace: {tag} elapsedTicks={(sim.Time-clock)/ParkSim.TickMilliseconds} "
                +$"cell={returned.Cell} steps={returned.Steps} state={returned.State} visits={v.Rides} buys={v.Purchases} "
                +$"cash={after.Cash} hunger={after.Hunger} toilet={after.Toilet} soundEvents={sounds}");
            Check(returned.Steps>0 && returned.Cell!=at && v.Plans[g.Id].Intent==VisitorIntent.Wandering,
                tag+" ordinary arm walks away before the facility deadline");
            draw=0; // now actively request facility selection while its old deadline is still pending
            for(int i=0;i<120;i++)v.Step(.04,()=>nearby);
            Console.WriteLine($"  post service window: {tag} visits={v.Rides}/{visits} boardings={v.Boardings} buys={v.Purchases}/{purchases} sounds={sounds}/{played} ids={string.Join(',',soundIds)} balance={sim.Finances.Balance}/{balance}");
            Check(v.Rides==visits && v.Boardings==1 && v.Purchases==purchases && sounds==played && sim.Finances.Balance==balance,
                tag+" movement retains the deadline against renewed selection purchase and sound");
            after=v.Needs.Of(g.Id);
            Check(after.Cash==needs.Cash && after.Hunger==needs.Hunger && after.Thirst==needs.Thirst && after.Toilet==needs.Toilet,
                tag+" walking preserves post-service needs rather than reseeding or serving again");
            Check(v.Plans.Count==1 && v.Walk.Guests.Count==1 && v.Needs.Has(g.Id),tag+" the original identity remains owned once");
        }
    }
}
