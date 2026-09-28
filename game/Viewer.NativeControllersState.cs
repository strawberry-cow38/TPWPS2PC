using System.Text.Json.Serialization;
using Godot;
using TPW.PS2.Data;
using Flow = TPW.PS2.Data.NativeEntranceFlow;
using Queues = TPW.PS2.Data.NativeRideQueues;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;
namespace TPWPS2Viewer;
public partial class Viewer
{
    // Trusted asset/provider installation only. Bus placement/traffic belongs to the bus join.
    public void BindNativeControllerAssets(NativeBusCatalogue catalogue, PathTool paths)
    { WC(_busCatalogue==null||ReferenceEquals(_busCatalogue,catalogue),"split bus catalogue"); _busCatalogue=catalogue; _paths=paths; }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record NativeRequestState(string Guest,int Mode,int Flags,uint Tick);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record NativeShapeState(string Ride,int X,int Z,ParkCell[] Cells,int Rotation);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record NativeControllersState
    {
        public required string NativeFlowId {get;init;}
        public required string RideQueuesId {get;init;}
        public required Flow.Snapshot Entrance {get;init;}
        public required Queues.State Queues {get;init;}
        public required string EntrancePrior {get;init;}
        public required string QueuePrior {get;init;}
        public required NativeRequestState[] Requests {get;init;}
        public required int[] RequestFlags {get;init;}
        public required NativeShapeState[] Shapes {get;init;}
        public required int ShapesTick {get;init;}
        public required uint Tick {get;init;}
        public required int Accepted {get;init;}
        public required int Rejected {get;init;}
        public required int Fee {get;init;}
        public required bool LegacyEntrance {get;init;}
        public required bool NativeRideQueues {get;init;}
    }
    Action<uint> CreateWorldEntranceTickHook()=>tick=>{
        _entrancePriorTick?.Invoke(tick);_entranceTick=tick;
        _busTraffic=_entranceFlow.Tick(tick,_nativeBus.Controller.State,_busTraffic);TickNativeAnimations();};
    Action<uint> CreateWorldQueueTickHook()=>tick=>{_rideQueuePriorTick?.Invoke(tick);_rideQueues?.Tick(tick);};
    Flow.Services CreateWorldEntranceServices()
    {
        WC(_busCatalogue!=null,"native entrance catalogue binding required");
        Point stage=EntranceCentre(_busCatalogue.StagingPoint);
        Point queue=EntranceCentre(_busCatalogue.IncomingQueuePoint);
        return new Flow.Services(
            n => _guestRng.Next(n),
            _ => new Point(unchecked((short)(stage.X + _guestRng.Next(256))), stage.Z),
            queue, RequestEntranceRoute, PumpEntranceRoutes,
            _ => true, // no extra admission gate; actual allocation is GuestWalk.NativeRoutes (1000 shared slots)
            NativeEntranceReady, // 191E10 under --native-guest-animation; otherwise the documented bypass
            () => 0x4000,
            g => NativeEntranceAcceptance.TryCharge(_entranceVisitors.Needs, g.Id, _sim.Finances,
                () => _entranceFee, EntranceValueSum, n => _guestRng.Next(n),
                () => _entranceAccepted++, cls => _sim?.AdvisorEvent?.Invoke(19, cls)),   // 0x210C78: counter 19
            EntranceExit,
            g => {
                _entranceRejected++;
                GD.Print($"[entrance] guest {g.Id} rejected: native outgoing journey under represented-activation phase adapter");
            },
            _guests.StepOwnedNative, exitCandidates: EntranceExitCandidates,
            busPoint: (g, index) => index == 0 ? EntranceCentre(_busCatalogue.Point0)
                : throw new InvalidOperationException("Ordinary departure RNG(1) must select point0."),
            requestDetailed: request => {
                _entranceRequestFlags.Add(request.Flags);
                _entranceRequests.Enqueue((request.Guest, request.Mode, request.Flags, _entranceTick));
                while (_entranceRequests.Count > 4096) _entranceRequests.Dequeue();
                // Flags reach the adapter but native 0x21/0x23 search policy is not yet reproduced by BFS.
                return RequestEntranceRoute(request.Token, request.Guest, request.Mode, request.From, request.Target);
            },
            recovery: (g, mode) => GD.Print($"[entrance] guest {g.Id}: mode{mode} native failure hold; the queue-8 adapter resumes it next update"),
            slotAdvanced: NativeSlotAdvanced);
    }
    Queues.Services CreateWorldQueueServices()=>new() {
        Shape=RideQueueShape, Random=n=>_guestRng.Next(n), Phase=g=>(uint)g.Id,
        Speed=g=>checked((sbyte)(15+g.Id*7%15)),
        AnimationReady=g=>!NativeIdleAllActive || NativeEntranceReady(g), SlotAdvanced=NativeSlotAdvanced };
    void NativeControllerPreflight()
    {
        WC(_entranceFlow==null ? _entranceWalk==null && _entranceVisitors==null && _entranceTickHook==null && _entrancePriorTick==null
            : ReferenceEquals(_entranceWalk,_guests) && ReferenceEquals(_entranceVisitors,_visitors) && _entranceTickHook!=null,"entrance owners/hooks");
        WC(_rideQueues==null ? _rideQueueWalk==null && _rideQueueVisitors==null && _rideQueueTickHook==null && _rideQueuePriorTick==null
            : ReferenceEquals(_rideQueueWalk,_guests) && ReferenceEquals(_rideQueueVisitors,_visitors) && _rideQueueTickHook!=null,"queue owners/hooks");
        WC(_entranceRequests.Count<=4096 && _queueShapes.Count<=100000,"native bounds");
    }
    void RegisterNativeCapture(WorldCoreRegistry r)
    {
        if(_entranceFlow!=null) {
            r.Register("native/entrance",_entranceFlow); r.Register("native/entrance/owner",_entranceFlow.StateOwner);
            r.Register("native/entrance/tick",_entranceTickHook);
            if(_entranceVisitors.NativeDeparture!=null)r.Register("native/entrance/departure",_entranceVisitors.NativeDeparture);
            int i=0;foreach(var g in _entranceFlow.ReferencedGuests.Where(g=>_entranceFlow.StateOf(g)!=null))
                r.Register("native/entrance/input/"+i++,_entranceFlow.StateInputs(g));
        }
        if(_rideQueues!=null) {
            r.Register("native/queues",_rideQueues);r.Register("native/queues/owner",_rideQueues.StateOwner);
            r.Register("native/queues/tick",_rideQueueTickHook);
            if(_rideQueueVisitors.NativeQueueMouth!=null)r.Register("native/queues/mouth",_rideQueueVisitors.NativeQueueMouth);
            if(_rideQueueVisitors.NativeQueueArrival!=null)r.Register("native/queues/arrival",_rideQueueVisitors.NativeQueueArrival);
            int i=0;foreach(var g in _rideQueues.ReferencedGuests.Where(_rideQueues.Owns))
                r.Register("native/queues/input/"+i++,_rideQueues.StateInputs(g));
        }
    }
    Flow.SnapshotBindings EntranceBindings(WorldCoreRegistry r,Flow.Services services,string member)=>new() {
        GuestGraph=r.Graph,ServicesId="native/entrance/services",OwnerId="native/entrance/owner",Services=services,
        IdentifyInputs=r.Id,MemberEvent9Id=member,MemberEvent9=member==null?null:r.Resolve<Action>(member) };
    Queues.StateBindings QueueBindings(WorldCoreRegistry r,Queues.Services services)=>new() {
        GuestGraph=r.Graph,ServicesId="native/queues/services",OwnerId="native/queues/owner",Services=services,
        IdentifyInputs=r.Id,IdentifyRide=r.Id,ResolveRide=r.Resolve<ParkRide> };
    NativeControllersState CaptureNativeControllers(WorldCoreRegistry r)=>new() {
        NativeFlowId=r.Id(_entranceFlow),RideQueuesId=r.Id(_rideQueues),
        Entrance=_entranceFlow?.CaptureState(EntranceBindings(r,_entranceFlow.StateServices,r.Id(_entranceFlow.MemberEvent9))),
        Queues=_rideQueues?.CaptureState(QueueBindings(r,_rideQueues.StateServices)),
        EntrancePrior=r.Id(_entrancePriorTick),QueuePrior=r.Id(_rideQueuePriorTick),
        Requests=_entranceRequests.Select(x=>new NativeRequestState(r.Id(x.Guest),x.Mode,x.Flags,x.Tick)).ToArray(),
        RequestFlags=_entranceRequestFlags.ToArray(),Shapes=_queueShapes.Select(p=>p.Value==null
            ?new NativeShapeState(r.Id(p.Key),0,0,null,0):new NativeShapeState(r.Id(p.Key),p.Value.Entrance.X,p.Value.Entrance.Z,p.Value.Cells.ToArray(),p.Value.Rotation)).ToArray(),
        ShapesTick=_queueShapesTick,Tick=_entranceTick,Accepted=_entranceAccepted,Rejected=_entranceRejected,
        Fee=_entranceFee,LegacyEntrance=_legacyEntrance,NativeRideQueues=_nativeRideQueues };
    void AllocateNativeControllers(NativeControllersState s,WorldCoreRegistry r)
    {
        WC(s!=null && s.Requests!=null && s.Requests.Length<=4096 && s.RequestFlags!=null && s.RequestFlags.Length<=4096
            && s.Shapes!=null && s.Shapes.Length<=100000,"native envelope bounds");
        WC((s.NativeFlowId==null)==(s.Entrance==null) && (s.RideQueuesId==null)==(s.Queues==null),"native IDs");
        _walkGrid=r.Paths;_guests=r.Walk;_sim=r.Sim;_visitors=r.Visitors;
        _legacyEntrance=s.LegacyEntrance;_nativeRideQueues=s.NativeRideQueues;
        _entranceTick=s.Tick;_entranceAccepted=s.Accepted;_entranceRejected=s.Rejected;_entranceFee=s.Fee;
        foreach(var x in s.Requests)_entranceRequests.Enqueue((r.Guest(x.Guest),x.Mode,x.Flags,x.Tick));
        foreach(int x in s.RequestFlags)WC(_entranceRequestFlags.Add(x),"duplicate native flag");
        _queueShapesTick=s.ShapesTick;
        foreach(var x in s.Shapes){WC(x.Cells==null || x.Cells.Length<=514,"shape bound");
            _queueShapes.Add(r.Resolve<ParkRide>(x.Ride),x.Cells==null?null:new(new(x.X,x.Z),x.Cells.ToArray(),x.Rotation));}
        // Pure provider creation after Runtime/RNG and guest/visitor/staff shells, before binding.
        r.bindings.BindNativeControllerProviders?.Invoke(r);
        if(s.Entrance!=null) {
            _entranceWalk=r.Walk;_entranceVisitors=r.Visitors;
            _entranceFlow=Flow.AllocateState(s.Entrance,r.Visitors,EntranceBindings(r,CreateWorldEntranceServices(),s.Entrance.MemberEvent9Id));
            r.Register(s.NativeFlowId,_entranceFlow);r.Register(s.Entrance.OwnerId,_entranceFlow.StateOwner);
            foreach(var x in s.Entrance.Entries)r.Register(x.InputsId,_entranceFlow.StateInputs(r.Graph.GuestByGraphId(x.GuestGraphId)));
            _entranceTickHook=CreateWorldEntranceTickHook();
            r.Register("native/entrance/tick",_entranceTickHook);
            r.Register("native/entrance/departure",(Func<Guest,bool>)(g=>_entranceFlow!=null&&_entranceFlow.TryDepart(g)));
        }
        if(s.Queues!=null) {
            _rideQueueWalk=r.Walk;_rideQueueVisitors=r.Visitors;
            _rideQueues=Queues.AllocateState(s.Queues,r.Visitors,QueueBindings(r,CreateWorldQueueServices()));
            r.Register(s.RideQueuesId,_rideQueues);r.Register(s.Queues.OwnerId,_rideQueues.StateOwner);
            foreach(var x in s.Queues.Members)r.Register(x.InputsId,_rideQueues.StateInputs(x.InputsId));
            _rideQueueTickHook=CreateWorldQueueTickHook();
            r.Register("native/queues/tick",_rideQueueTickHook);
            r.Register("native/queues/mouth",(Func<ParkRide,ParkCell?>)(ride=>_rideQueues?.Mouth(ride)));
            r.Register("native/queues/arrival",(Func<Guest,ParkRide,bool>)((g,ride)=>_rideQueues!=null&&_rideQueues.Arrive(g,ride)));
        }
        _entrancePriorTick=s.EntrancePrior==null?null:r.Resolve<Action<uint>>(s.EntrancePrior);
        _rideQueuePriorTick=s.QueuePrior==null?null:r.Resolve<Action<uint>>(s.QueuePrior);
    }
}
