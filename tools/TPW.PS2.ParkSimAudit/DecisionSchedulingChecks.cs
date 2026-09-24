using System.Text;
using TPW.PS2.Data;

static class DecisionSchedulingChecks
{
    public static void Run(Model terrain, ParkPaths source, ParkCell entrance, ParkCell exit,
                           WadArchive data, WadArchive world, string worldName, Action<bool,string> check)
    {
        void Check(bool ok, string label) => check(ok, "decision scheduling: " + label);
        int draws = 0;
        var gate = new GuestDecisionSchedule(() => { draws++; return 0; });
        Check(gate.CanSelect(1, 0) && draws == 0, "new guest is not assigned an invented completion delay");
        gate.Completed(1, 100);
        Check(!gate.CanSelect(1,100) && draws == 1, "completion cannot retry on the same counter or consume random draws");
        Check(!gate.CanSelect(1,460), "strict boundary rejects stored300 plus extra60 equality");
        Check(gate.CanSelect(1,461), "next counter admits native random-arm-zero selection");
        Check(!gate.CanSelect(1,461), "same-counter repeated call is not another lottery ticket");
        Check(gate.CanSelect(1,462), "failed routing retains the original gate rather than postponing it again");
        gate.Forget(1);
        Check(gate.Count == 0 && gate.CanSelect(1,462), "successful explicit route can release the gate");
        var values = new Queue<int>(new[]{299,0,299,0,299,5,0,0,0});
        var upper = new GuestDecisionSchedule(() => values.Dequeue());
        upper.Completed(7,100); // deadline699, threshold1058 on the first two attempts
        Check(!upper.CanSelect(7,1058) && upper.CanSelect(7,1059), "maximum completion and threshold draws preserve strict comparison");
        Check(!upper.CanSelect(7,1060) && upper.CanSelect(7,1061), "nonzero decision arm does not choose even beyond deadline");
        gate.Completed(1,700); gate.Completed(2,800); gate.Reconcile(new[]{2});
        Check(gate.Count == 1 && gate.CanSelect(1,700), "retirement and reused IDs do not inherit an old deadline");
        gate.Completed(2,900);
        Check(!gate.CanSelect(2,900), "new completion replaces previous state for the same live guest");
        gate.Reconcile(Array.Empty<int>());
        Check(gate.Count == 0, "all retired identities release scheduling storage");

        string stem = worldName is "HALLOW" or "SPACE" ? "/Shops/ices/ices" : "/Shops/IceCream/IceCream";
        var definition = RideDefinition.Parse(Encoding.ASCII.GetString(world.Read(world.Find(stem+".sam"))),
                                             "/DATA/"+worldName+".WAD"+stem+".sam");
        var compiled = new CompiledAssets(new AssetResourceDatabase(data.Read(data.Find("/arsdb.dba"))), TextDatabase.Load(data,"eur"));
        Check(compiled.Attach(new[]{definition},out _) == 1 && definition.Compiled?.Product == 4 && definition.PricePerUse == 30,
              "real named compiled shop fixture is not accidentally a ride");
        foreach (int cash in new[]{1234,299})
        {
            var paths = new ParkPaths(terrain); source.Field.Cells.CopyTo(paths.Field.Cells,0);
            var sim = new ParkSim(paths);
            string folder = stem[..(stem.LastIndexOf('/')+1)];
            var aps = world.Find(stem+".aps") ?? world.Entries.Single(e=>e.Path.StartsWith(folder,StringComparison.OrdinalIgnoreCase)
                && !e.Path[folder.Length..].Contains('/') && e.Path.EndsWith(".aps",StringComparison.OrdinalIgnoreCase));
            byte[] Sibling(string name) => world.Find(folder+name) is {} e ? world.Read(e) : null;
            var ride = sim.Add(1,"decision fixture",entrance,2,2,world.Read(world.Find(stem+".rse")),new Animation(world.Read(aps)),1,
                entrance,entrance,out var fault,sibling:Sibling,definition:definition) ?? throw new InvalidOperationException(fault);
            sim.SetOpen(1,true); ride.Set("VAR_BROKEN",0);
            var v = new ParkVisitors(sim,new GuestWalk(paths),()=>0)
                { Needs = new VisitorNeeds(42) { SecondsPerRise=1_000_000 } };
            foreach (var key in v.Needs.Rates.Keys.ToArray()) v.Needs.Rates[key]=new(0,0,false);
            var g = v.Arrive(entrance,exit);
            v.Needs.Set(g.Id,new VisitorWants { Cash=cash,Hunger=91,Happiness=50,PreferredIntensity=90 });
            Check(v.SendTo(g,ride), $"cash{cash} fixture can reach the actual shop");
            uint completedAt=0;
            for(int tick=0;tick<6000 && v.Rides==0;tick++)
            {
                completedAt=unchecked((uint)(sim.Time / ParkSim.TickMilliseconds));
                v.Step(.04,()=>exit);
            }
            Check(v.Rides==1 && v.Boardings==1 && v.Purchases==(cash==1234?1:0)
                  && v.Needs.Of(g.Id).Hunger==(cash==1234?66:91),
                  $"cash{cash} actual handback distinguishes successful and refused purchase");
            Check(v.Plans[g.Id].Intent==VisitorIntent.Wandering && v.QueuedOwner(g.Id)==null,
                  $"cash{cash} completion does not immediately retarget a destination");
            long clock=(sim.Time / ParkSim.TickMilliseconds);
            for(int i=0;i<20;i++) v.Step(0,()=>exit);
            Check((sim.Time / ParkSim.TickMilliseconds)==clock && v.Boardings==1 && v.Plans[g.Id].Intent==VisitorIntent.Wandering,
                  $"cash{cash} zero-time calls cannot reboard the same shop");
            v.Needs.SecondsPerTick=.005; // 8x appetite speed must NOT accelerate destination scheduling
            while(unchecked((uint)(sim.Time / ParkSim.TickMilliseconds))<unchecked(completedAt+360u)) v.Step(.04,()=>exit);
            Check(v.Boardings==1 && v.Plans[g.Id].Intent==VisitorIntent.Wandering,
                  $"cash{cash} park deadline survives eightfold appetite rate change");
            for(int i=0;i<8 && v.Boardings==1;i++) v.Step(.04,()=>exit);
            Check(v.Boardings==2 && ReferenceEquals(v.QueuedOwner(g.Id),ride),
                  $"cash{cash} eligible later decision can revisit instead of a permanent blacklist");
        }
    }
}
