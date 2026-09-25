using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;
using Flow = TPW.PS2.Data.NativeEntranceFlow;

/// <summary>Real owner/walker departure consumer with explicit serials and controlled
/// deferred route service. Not proof of native startup history or search policy.</summary>
static class NativeDepartureChecks
{
    public static void Run(Disc disc, Action<bool,string> check)
    {
        void C(bool ok,string why)=>check(ok,"native rejected departure: "+why);
        var file=disc.Files().Single(f=>f.Path=="/DATA/JUNGLE.WAD");
        var wad=new WadArchive(disc.Read(file.Extent,file.Size));
        var terrain=new Model(wad.Read(wad.Find(AuditPark.Mps)));
        var elf=disc.Files().Single(f=>f.Path=="/SLES_500.32");
        var entrances=ParkEntrance.ReadExecutable(disc.Read(elf.Extent,elf.Size));
        var entry=entrances.Fit(terrain.Field,ParkEntrance.WalkwayColumnFromPoles(terrain),out _);
        Fixture New() {var paths=new ParkPaths(terrain);paths.SetEntrance(entrances);return new(paths,new(entry.XCol,entry.ZEnd-2),C);}
        var seq=new NativeActivationSequence(40,"explicit fixture shared history");
        C(seq.Activate("placed")==40&&seq.Activate("guest")==41&&seq.Activate("litter")==42
            &&seq.Activate("guest")==43&&seq.Activations==4,"activation sequence shared across represented families, not guest-only IDs");
        C(!seq.NativeHistoryVerified&&seq.Origin.Contains("fixture")&&seq.ActivationsByKind["guest"]==2,
            "sequence origin stays explicitly unverified; family census does not assert native startup parity");
        var wrap=new NativeActivationSequence(uint.MaxValue,"raw-word wrap fixture",true);
        C(wrap.Activate("guest")==uint.MaxValue&&wrap.Activate("guard")==0&&wrap.NextSerial==1,"activation postincrement wraps as a native word");
        bool noOrigin=false;try{_ = new NativeActivationSequence(0,"");}catch(ArgumentException){noOrigin=true;}
        C(noOrigin,"no implicit serial origin hidden in constructor");
        var table=NativeBusCatalogue.Read(disc,new(0,0));
        C(table.ImageInitialActivationCounter==0,"owner ELF counter image seed read separately from live activation history");

        Success(); Phase32(); Refusal(); Alternate(false); Alternate(true); AlternateRecordFull(); Recovery(); Pressure(); CallbackFaults(); OrdinarySeam();

        // Queue item 7's boundaries. The positive path (an admitted guest leaving through 26) needs a
        // real admission and is the rendered NativeOrdinaryDepartureSmoke; these are the refusals.
        void OrdinarySeam()
        {
            var f=New();var stranger=f.Visitors.Arrive(f.Start,f.Start);
            C(!f.Flow.TryDepart(stranger)&&!f.Flow.Owns(stranger)&&!stranger.HasNativeRoute
                &&f.Visitors.Plans[stranger.Id].Intent==VisitorIntent.Wandering,
                "ordinary departure refuses a guest this flow never admitted: no birth serial is minted and nothing changes");
            var entering=f.Add(63);var before=f.O(entering);
            C(!f.Flow.TryDepart(entering)&&f.O(entering)==before,"ordinary departure refuses a guest the flow already owns");
            static void Broke(Fixture x,Guest g){var w=x.Visitors.Needs.Of(g.Id);w.Cash=50;x.Visitors.Needs.Set(g.Id,w);}
            var legacy=New();var walker=legacy.Visitors.Arrive(legacy.Start,legacy.Start);int offered=0;
            legacy.Visitors.NativeDeparture=g=>{offered++;return false;};Broke(legacy,walker);legacy.Step();
            C(offered>0&&(legacy.Visitors.WentHome==1||legacy.Visitors.Plans[walker.Id].Intent==VisitorIntent.Leaving),
                $"a declined native departure leaves the legacy walk to the gate untouched (offered {offered}, went home {legacy.Visitors.WentHome})");
            var taken=New();var kept=taken.Visitors.Arrive(taken.Start,taken.Start);
            Broke(taken,kept);
            int accepted=0;taken.Visitors.NativeDeparture=g=>{accepted++;return true;};taken.Step();
            C(accepted>0&&taken.Visitors.WentHome==0&&taken.Walk.IsLive(kept)&&taken.Visitors.Plans[kept.Id].Intent!=VisitorIntent.Leaving,
                $"an accepted native departure suppresses the legacy gate walk entirely (offered {accepted})");
        }

        void CallbackFaults()
        {
            foreach (var (handle, ack) in new[] {(true, false), (false, true), (true, true)})
            {
                var f=New();f.Add(63);f.Step(); // first request accepted, callback still pending
                var handlerError=new Exception("controlled result-handler fault");
                var ackError=new Exception("controlled acknowledgement fault");
                f.HandlerFault=handle?handlerError:null;f.AckFault=ack?ackError:null;
                Exception caught=null;try{f.Step();}catch(Exception error){caught=error;}
                C(f.Acknowledgements==1&&f.ActiveRequests.Count==0,
                    $"acknowledgement attempted exactly once after handler fault={handle}, ack fault={ack}");
                C(handle&&ack ? caught is AggregateException both
                        &&both.InnerExceptions.Count==2&&ReferenceEquals(both.InnerExceptions[0],handlerError)
                        &&ReferenceEquals(both.InnerExceptions[1],ackError)
                    : handle ? ReferenceEquals(caught,handlerError)
                    : caught is InvalidOperationException onlyAck&&ReferenceEquals(onlyAck.InnerException,ackError),
                    $"both callback diagnoses preserved and distinguished: handler={handle}, ack={ack}");
            }
        }

        void Success()
        {
            var f=New();var g=f.Add(63);var original=f.Visitors.Needs.Of(g.Id);int balance=f.Visitors.Sim.Finances.Balance;
            if(!f.Until(()=>f.O(g).State==Flow.State.Rejected,"booth rejection reached"))return;
            C(g.Id!=63&&f.O(g).Serial==63&&f.Visitors.Plans[g.Id].Intent==VisitorIntent.Leaving,
                "rejection keeps owner/identity but marks native departure with explicit serial, not Guest.Id");
            C(!f.Requests.Any(r=>r.Request.Mode==14)&&f.Rejections==1&&f.Visits==1,"rejection is not an immediate route, repeated acceptance, or removal");
            f.Step();
            C(f.O(g).DeferredSticky&&f.Flow.DeparturePressure==0&&f.StageCalls==2,
                "first phase mismatch sets A4 after tally, without calling staging helper");
            f.Step();C(f.Flow.DeparturePressure==1,"sticky A4 contributes beginning with the next active pass");
            if(!f.Until(()=>f.Requests.Any(r=>r.Request.Mode==14),"phase63 request"))return;
            var initial=f.Requests.First(r=>r.Request.Mode==14);
            C(initial.Tick==63&&initial.Request.Flags==0x21&&initial.Request.Priority==0,"mode14 request uses serial phase63 and flags21/priority0");
            C(f.O(g).DeferredSticky&&f.O(g).State==Flow.State.Pending,"successful request retains sticky A4 and awaits result");
            if(!f.Until(()=>f.O(g) is {State:Flow.State.Staged,OutgoingDirection:true},"outgoing stage reached"))return;
            C(f.Flow.Counts==(0,0)&&f.Flow.StagingPending==1&&g.NativeHeading==-System.Numerics.Vector3.UnitZ,
                "mode14 contributes staging, not incoming group1, and explicitly faces pi");
            int calls=f.StageCalls;
            int[] reserved=f.ReserveOutput();f.Step();
            C(f.O(g).State==Flow.State.CrossStaging&&f.StageCalls==calls&&f.Flow.StagingPending==1,
                "full real output pool blocks outgoing mode16 before helper/RNG and before P decrement");
            f.Walk.NativeRoutes.FreeOne(reserved[^1]);f.Step();
            C(f.O(g) is {State:Flow.State.Moving,Mode:16,OutgoingDirection:true}
                &&f.StageCalls==calls+1&&f.Flow.StagingPending==1,"freed slot resumes same outgoing identity without prematurely decrementing P");
            f.ReleaseOutput(reserved[..^1]);
            if(!f.Until(()=>f.O(g).State==Flow.State.RequestBus,"outgoing mode16 exhausted"))return;
            C(f.Flow.Counts==(0,0)&&f.Flow.StagingPending==0&&f.Rolls.Count(r=>r.N==2)==1,
                "outgoing mode16 does not choose/register an incoming group; only incoming traversal drew RNG2");
            f.Step();
            C(f.Requests.Last().Request is {Mode:9,Flags:1,Priority:0}&&f.Rolls.Count(r=>r.N==1)==1
                &&f.BusReads==1,"state30 consumes RNG1 and submits final point0 route, flags1");
            if(!f.Until(()=>!f.Flow.Owns(g),"same rejected guest reaches bus point and leaves"))return;
            C(!f.Walk.IsLive(g)&&!f.Visitors.Plans.ContainsKey(g.Id)&&!f.Visitors.Needs.Has(g.Id)
                &&f.Visitors.WentHome==1&&f.Visitors.DiscardedEntranceGuests==0&&f.LeavingStable,"mode9 removes real walker/plan/needs as a departure, not teardown");
            C(f.AtDeparture==f.Bus&&f.CashAtDeparture==original.Cash&&f.Visitors.Sim.Finances.Balance==balance
                &&f.Visitors.Sim.Finances.TotalIncome==0,"rejected guest reaches bus point without fee or respawn");
            C(f.Flow.DeparturePressure==1&&f.Walk.NativeRoutes.Available==1000,"removal pass still counts old sticky flag and returns every owned slot");
            f.Step();C(f.Flow.DeparturePressure==0&&f.Flow.Observations.Count==0,"next pass has no departed contribution or owner reference");
        }
        void Phase32()
        {
            var f=New();var g=f.Add(32);
            if(!f.Until(()=>f.Requests.Any(r=>r.Request.Mode==14),"phase32 discrimination"))return;
            C(f.Requests.First(r=>r.Request.Mode==14).Tick==96&&f.O(g).Serial==32,
                "six-bit phase32 waits until96 after rejection, not64 from a five-bit mask or Guest.Id");
        }
        void Refusal()
        {
            var f=New();f.RefuseInitial=1;var g=f.Add(63);
            if(!f.Until(()=>f.Requests.Any(r=>r.Request.Mode==14),"initial synchronous refusal"))return;
            C(f.O(g) is {State:Flow.State.Rejected,PendingToken:null,DeferredSticky:true,AlternateRequestFlag:false},
                "synchronous refusal retains26/A4, not asynchronous fallback semantics");
            int calls=f.StageCalls;
            while(f.Tick<126)f.Step();
            C(f.Requests.Count(r=>r.Request.Mode==14)==1&&f.StageCalls==calls,"nonmatching phases neither retry nor consume helper RNG");
            f.Step();C(f.Tick==127&&f.Requests.Count(r=>r.Request.Mode==14)==2&&f.O(g).State==Flow.State.Pending,
                "initial refusal retries on next full64 period");
        }
        void Alternate(bool failAgain)
        {
            var f=New();f.FailInitial=1;f.FailAlternate=failAgain?1:0;var g=f.Add(63);
            if(!f.Until(()=>f.Requests.Any(r=>r.Request.Flags==0x23),"asynchronous alternate submitted"))return;
            C(f.Requests.Last().Tick==64&&f.O(g) is {State:Flow.State.Pending,AlternateRequestFlag:true,DeferredSticky:true},
                "event2 submits flags23 immediately off-phase and marks accepted alternate");
            C(f.AlternateSawCompletingRecord,"alternate submission occurs while the completing request record is still occupied");
            f.Step();
            if(failAgain)
            {
                C(f.O(g) is {State:Flow.State.DecisionBoundary,AlternateRequestFlag:true,DeferredSticky:true,Mode:14}
                    &&f.RecoveryModes.SequenceEqual(new[]{14}),"second mode14 failure selects state0 and preserves flag20/A4, not state26");
                int count=f.Requests.Count;for(int i=0;i<70;i++)f.Step();
                C(f.Requests.Count==count&&f.Flow.Owns(g)&&f.Flow.DeparturePressure==1&&f.Visitors.WentHome==0,
                    "unported ordinary recovery is explicit owned boundary, not fake retry or disappearance");
            }
            else
            {
                C(f.O(g) is {State:Flow.State.Moving,Mode:14,AlternateRequestFlag:false,DeferredSticky:true},
                    "actual alternate output success clears flag20 but never sticky A4");
                if(!f.Until(()=>!f.Flow.Owns(g),"accepted alternate completes departure"))return;
                C(f.Visitors.WentHome==1&&f.Walk.NativeRoutes.Available==1000,"alternate success reaches actual departure cleanup");
            }
        }
        void AlternateRecordFull()
        {
            var f=New();f.FailInitial=1;f.ReservedRequests=9;f.RequestCapacity=10;var g=f.Add(63);
            if(!f.Until(()=>f.Requests.Any(r=>r.Request.Flags==0x23),"alternate request record saturation"))return;
            C(f.O(g) is {State:Flow.State.Pending,PendingToken:null,AlternateRequestFlag:false,DeferredSticky:true}
                &&f.ActiveRequests.Count==0&&f.AlternateSawCompletingRecord,
                "full10 records refuse in-callback alternate; old record frees afterward, state0B/token-null persists");
            int count=f.Requests.Count;for(int i=0;i<70;i++)f.Step();
            C(f.Requests.Count==count&&f.Flow.Owns(g),"alternate admission refusal is not silently retried on the next phase");
        }
        void Recovery()
        {
            var f=New();f.FailBus=1;var g=f.Add(63);
            if(!f.Until(()=>f.Requests.Any(r=>r.Request.Mode==9),"final request before generic failure"))return;
            var w=f.Visitors.Needs.Of(g.Id);w.Happiness=255;w.Unknown78=128;f.Visitors.Needs.Set(g.Id,w);
            f.Step();
            C(f.O(g) is {State:Flow.State.RecoveryBoundary,Mode:9,Group:-1,DeferredSticky:true}
                &&f.RecoveryModes.SequenceEqual(new[]{9}),"mode9 event2 goes to state5, not state30 or removal");
            C(f.CapturedRecovery.Happiness==0&&f.CapturedRecovery.Unknown78==129&&f.CapturedRecovery.Cash==w.Cash,
                "generic failure uses signed happiness/unknown78 bytes and no fee or reseed");
            C(f.Flow.Owns(g)&&f.Visitors.WentHome==0&&f.Flow.DeparturePressure==1,"generic recovery boundary retains identity and sticky contribution");
        }
        void Pressure()
        {
            var f=New();f.RefuseInitial=int.MaxValue;
            var guests=Enumerable.Range(0,30).Select(i=>f.Add((uint)(63+64*i))).ToArray();
            if(!f.Until(()=>f.Flow.DeparturePressure==30,"thirty sticky active contributions",4096))return;
            C(f.Visitors.WentHome==0&&guests.All(f.Flow.Owns)&&f.Flow.Counts==(0,0),
                "thirty-pressure comes from actual sticky active flags, not queue length or completed departures");
            C(f.Flow.Observations.All(o=>o.DeferredSticky&&o.State==Flow.State.Rejected),"pressure fixture retains thirty independently owned phase/refusal states");
            f.Flow.Clear((_,_)=>{},f.Visitors.DiscardEntranceGuest);f.Step();
            var fresh=f.Add(5);
            C(!f.O(fresh).DeferredSticky&&f.Flow.DeparturePressure==0,"new activation cannot inherit retired cohort A4");
        }
    }

    sealed class Fixture
    {
        readonly Action<bool,string> check;
        public readonly GuestWalk Walk;public readonly ParkVisitors Visitors;public readonly Flow Flow;
        public readonly ParkCell Start;public readonly Point Centre,Stage,Bus;
        public uint Tick;public int Traffic,StageCalls,BusReads,Visits,Rejections;
        public int RefuseInitial,FailInitial,FailAlternate,FailBus,ReservedRequests;
        public int RequestCapacity=int.MaxValue;
        public readonly List<(uint Tick,Flow.RouteRequest Request)> Requests=new();
        public readonly List<(uint Tick,int N)> Rolls=new();
        public readonly HashSet<ulong> ActiveRequests=new();
        public readonly List<int> RecoveryModes=new();
        public VisitorWants CapturedRecovery;public Point AtDeparture;public int CashAtDeparture;
        public bool AlternateSawCompletingRecord;
        public bool LeavingStable = true;
        public Exception HandlerFault,AckFault;public int Acknowledgements;
        readonly List<Flow.RouteResult> results=new();readonly Dictionary<ulong,int> modes=new();
        bool mode9Effects;
        public Fixture(ParkPaths paths,ParkCell start,Action<bool,string> c)
        {
            check=c;Start=start;Centre=new((short)(start.X*256+128),(short)(start.Z*256+128));
            Stage=new((short)(Centre.X+64),(short)(Centre.Z-64));Bus=new(Centre.X,(short)(Centre.Z-256));
            Walk=new(paths);Visitors=new(new ParkSim(paths),Walk,()=>0){Needs=new VisitorNeeds(71){SecondsPerRise=1_000_000}};
            foreach(var key in Visitors.Needs.Rates.Keys.ToArray())Visitors.Needs.Rates[key]=new(0,0,false);
            Flow=new(Visitors,new Flow.Services(
                Random,_=>{StageCalls++;return Stage;},Centre,
                (_,_,_,_,_)=>throw new InvalidOperationException("detailed requests required"),Pump,
                _=>true,_=>true,()=>0x4000,_=>{Visits++;return false;},_=>Centre,_=>Rejections++,Walk.StepOwnedNative,
                trace: trace=>{
                    if(trace.Event=="route assigned"&&HandlerFault!=null)throw HandlerFault;
                    if(trace.Event=="route assigned"&&trace.Entry.Mode==9){var w=Visitors.Needs.Of(trace.Entry.Guest.Id);CashAtDeparture=w.Cash;}
                },
                busPoint:(_,index)=>{if(index!=0)throw new InvalidOperationException("RNG1 must yield0");BusReads++;return Bus;},
                requestDetailed:Request,afterResult:r=>{Acknowledgements++;ActiveRequests.Remove(r.Token);mode9Effects=false;if(AckFault!=null)throw AckFault;},
                recovery:(g,mode)=>{RecoveryModes.Add(mode);CapturedRecovery=Visitors.Needs.Of(g.Id);}));
            Walk.BeforeStep=t=>{Tick=t;Traffic=Flow.Tick(t,0,Traffic);};
        }
        int Random(int n){Rolls.Add((Tick,n));return mode9Effects?(n==15?14:n==2?1:0):0;}
        bool Request(Flow.RouteRequest r)
        {
            Requests.Add((Tick,r));
            if(r.Flags==0x23)AlternateSawCompletingRecord|=ActiveRequests.Any(t=>modes[t]==14);
            if(r.Mode==14&&r.Flags==0x21&&RefuseInitial>0){RefuseInitial--;return false;}
            if((long)ActiveRequests.Count+ReservedRequests>=RequestCapacity)return false;
            ActiveRequests.Add(r.Token);modes[r.Token]=r.Mode;
            bool fail=false;
            if(r.Mode==14&&r.Flags==0x21&&FailInitial>0){FailInitial--;fail=true;}
            if(r.Flags==0x23&&FailAlternate>0){FailAlternate--;fail=true;}
            if(r.Mode==9&&FailBus>0){FailBus--;fail=true;}
            results.Add(new(r.Token,r.Guest,fail?null:new[]{r.Target},fail?"controlled native event2":null));return true;
        }
        IEnumerable<Flow.RouteResult> Pump()
        {
            var a=results.ToArray();results.Clear();mode9Effects=a.Any(r=>!r.Succeeded&&modes[r.Token]==9);return a;
        }
        public Guest Add(uint serial)
        {
            var g=Visitors.Arrive(Start,Start);Flow.Add(g,15,serial);return g;
        }
        public Flow.Observation O(Guest g)=>Flow.Observations.Single(o=>ReferenceEquals(o.Guest,g));
        public void Step()
        {
            Visitors.Step(.04,()=>Start);
            LeavingStable &= Flow.Observations.All(o=>o.Accepted!=false || !o.Serial.HasValue
                || Visitors.Plans[o.Guest.Id].Intent==VisitorIntent.Leaving);
        }
        public bool Until(Func<bool> done,string why,int limit=512)
        {
            for(int i=0;i<limit&&!done();i++)
            {
                // Capture genuine pre-removal point/cash on the final no-slot dispatch, not a fake despawn target.
                foreach(var o in Flow.Observations.Where(o=>o.Mode==9&&o.State==Flow.State.Moving))
                {AtDeparture=Flow.Position(o.Guest).Value;CashAtDeparture=Visitors.Needs.Of(o.Guest.Id).Cash;}
                Step();
            }
            bool ok=done();check(ok,why+" within bounded real consumer ticks");return ok;
        }
        public int[] ReserveOutput(){var a=new List<int>();int s;while((s=Walk.NativeRoutes.Allocate())>=0)a.Add(s);return a.ToArray();}
        public void ReleaseOutput(IEnumerable<int> slots){foreach(int s in slots)Walk.NativeRoutes.FreeOne(s);}
    }
}
