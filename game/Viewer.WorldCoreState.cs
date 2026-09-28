using System.Text.Json;
using System.Text.Json.Serialization;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

public partial class Viewer
{
    // A logical WORLD cut, not a total Viewer save or a publication API. All calls require
    // a quiescent boundary. Ready/process, render/audio, tools and UI are NOT enabled here.
    public sealed class WorldCoreBindings
    {
        public required string TerrainKey { get; init; }
        public required Model Terrain { get; init; }
        public required ParkSim.StateBindings Simulation { get; init; }
        public Func<WorldCoreRegistry,BusStateBindings> Bus {get;init;}
        public Func<WorldCoreRegistry,AdvisorStateServices> Advisor {get;init;}
        // Trusted provider VALUES, not closure serialization. The root must account for every
        // stateful provider here, then build new-world delegates from these values on load.
        public IReadOnlyDictionary<string, JsonElement> ProviderValues { get; init; } = new Dictionary<string, JsonElement>();
        public IReadOnlyDictionary<string, object> CaptureProviders { get; init; } = new Dictionary<string, object>();
        public Func<string, WorldCoreRegistry, IReadOnlyDictionary<string, JsonElement>, object> RestoreProvider { get; init; }
        // Retained integer render slots MUST be assigned by their owner; never guess by Guest.Id.
        public IReadOnlyDictionary<int, Guest> GuestCacheOwners { get; init; } = new Dictionary<int, Guest>();
        public IEnumerable<Guest> RetainedGuests { get; init; } = Array.Empty<Guest>();
        public IEnumerable<ParkRide> RetainedRides { get; init; } = Array.Empty<ParkRide>();
        // Guest body terminal fields are internal to CORE; supply detached/occupied targets
        // not in Visitors.ReferencedTerminals. Missing targets fail during CaptureGraph.
        public IEnumerable<GuestTerminal> RetainedTerminals { get; init; } = Array.Empty<GuestTerminal>();
        // Exact CanEnter provider identity, without evaluating it. GuestTerminal has no owner DTO.
        public Func<GuestTerminal, string> TerminalProvider { get; init; }
        // Verified host VALUES for detached VMs; their actual host is intentionally not public.
        public IReadOnlyDictionary<RseMachine, RsePreviewHost> RetiredMachineHosts { get; init; } = new Dictionary<RseMachine, RsePreviewHost>();
        public Func<ParkPaths, Park.StateBindings> ParkBindings { get; init; }
        public Action<WorldCoreRegistry> BindNativeControllerProviders { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record WorldTerminal(string Id, string Ride, int X, int Z, int EntryX, int EntryZ, string Provider);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record WorldFeature(string Id, string Ride, string Key, int X, int Z, int EntryX, int EntryZ, byte Flags, byte Status);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record WorldRetiredRide(string Id, ParkRide.State Ride, RseMachine.State Machine, RsePreviewHost.State Host);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record WorldCoreState
    {
        public required int Version { get; init; }
        public required string TerrainKey { get; init; }
        public required RuntimeState Runtime { get; init; }
        public required ParkPaths.State Paths { get; init; }
        public required Park.State Park { get; init; }
        public required ParkSim.State Simulation { get; init; }
        public required string[] Rides { get; init; }
        public required WorldRetiredRide[] Retired { get; init; }
        public required WorldTerminal[] Terminals { get; init; }
        public required WorldFeature[] Features { get; init; }
        public required GuestWalk.State Walk { get; init; }
        public required Dictionary<int, string> GuestCacheOwners { get; init; }
        public required VisitorNeeds.State Needs { get; init; }
        public required ParkVisitors.State Visitors { get; init; }
        public required ParkStaff.State Staff { get; init; }
        public required ParkClock.State Calendar { get; init; }
        public required ParkAwards.State Awards { get; init; }
        public required ParkManagement.State Management { get; init; }
        public required NativeActivationSequence.State Activations { get; init; }
        public required NativeEntranceMailbox.State Mailbox { get; init; }
        public required NativeControllersState NativeControllers { get; init; }
        public required BusState Bus {get;init;}
        public required ViewerAdvisorState Advisor {get;init;}
        public required Dictionary<string, JsonElement> Providers { get; init; }
    }

    /// <summary>Snapshot-local object IDs, NOT display IDs. Keep this registry with staged
    /// render joins. GuestId/Guest, MemberId/Member and LitterId/Litter fit the existing render
    /// binding signatures. A missing owner is an error, never a fallback to a live-world object.</summary>
    public sealed class WorldCoreRegistry
    {
        readonly Dictionary<string, object> objects = new(StringComparer.Ordinal);
        readonly Dictionary<object, string> ids = new(ReferenceEqualityComparer.Instance);
        internal readonly Dictionary<int, Guest> cacheOwners = new();
        internal WorldCoreBindings bindings;
        internal IReadOnlyDictionary<string, JsonElement> providers;
        public Viewer Owner { get; internal set; }
        public ParkPaths Paths { get; internal set; }
        public ParkSim Sim { get; internal set; }
        public GuestWalk.GuestGraph Graph { get; internal set; }
        public GuestWalk Walk => Graph?.Walk;
        public ParkVisitors Visitors { get; internal set; }
        public VisitorNeeds Needs { get; internal set; }
        public ParkStaff Staff { get; internal set; }
        public ParkClock Calendar { get; internal set; }
        public ParkAwards Awards { get; internal set; }
        public NativeActivationSequence Activations { get; internal set; }
        public ParkManagement Management { get; internal set; }
        public Park Park { get; internal set; }
        public bool Hydrated { get; internal set; }
        public StagedAdvisor UnpublishedAdvisor {get;internal set;}
        public IReadOnlyDictionary<string,object> References=>objects;
        internal void Register(string id, object value)
        {
            WC(!string.IsNullOrWhiteSpace(id) && id.Length <= 1024 && value != null, "registry entry");
            WC(!objects.ContainsKey(id) && !ids.ContainsKey(value), "registry identity collision: " + id);
            objects.Add(id, value); ids.Add(value, id);
        }
        public string Id(object value)
        {
            if (value == null) return null;
            if (ids.TryGetValue(value, out var id)) return id;
            // Method-group properties create equivalent delegates, not persistent instances.
            if (value is Delegate)
                foreach (var p in objects) if (p.Value is Delegate && p.Value.Equals(value)) return p.Key;
            throw new NotSupportedException("WORLD: unregistered owner/provider " + value.GetType().FullName);
        }
        public object Resolve(string id)
        {
            if (id == null) return null;
            if (objects.TryGetValue(id, out var value)) return value;
            WC(id.StartsWith("provider/", StringComparison.Ordinal), "unresolved owner: " + id);
            value = bindings.RestoreProvider?.Invoke(id[9..], this, providers);
            WC(value is Delegate, "provider must resolve to a fresh delegate: " + id);
            Register(id, value); return value;
        }
        public T Resolve<T>(string id) where T : class => Resolve(id) as T
            ?? throw new InvalidDataException("WORLD: wrong reference type: " + id);
        public string GuestId(Guest g) => Id(g);
        public Guest Guest(string id) => Resolve<Guest>(id);
        public Guest GuestAtSlot(int slot) => cacheOwners.TryGetValue(slot, out var g) ? g
            : throw new InvalidDataException("WORLD: unassigned retained guest cache slot");
        public string MemberId(StaffMember m) => Id(m);
        public StaffMember Member(string id) => Resolve<StaffMember>(id);
        public string LitterId(LitterItem l) => Id(l);
        public LitterItem Litter(string id) => Resolve<LitterItem>(id);
        internal void RegisterGuests()
        {
            foreach (var p in Graph.GuestsByGraphId) Register("guest/" + p.Key, p.Value);
        }
        internal void RegisterVisitors()
        {
            if (Visitors == null) return;
            Register("visitors", Visitors);
            Register("visitors/sound", Visitors.StateForwardSound);
            Register("visitors/litter", Visitors.StateNearbyLitter);
        }
        internal void RegisterStaff()
        {
            if (Staff == null) return;
            Register("staff", Staff); Register("staff/paused", Staff.StatePaused);
            foreach (var kind in StaffTables.PoolBuildOrder)
                for (int i = 0; i < StaffTables.PoolSize; i++) Register($"member/{(int)kind}/{i}", Staff.StateMember(kind, i));
            foreach (var item in Staff.Litter.Slots) Register("litter/" + item.PoolSlot, item);
        }
        internal GuestWalk.StateBindings WalkBindings() => new() {
            IdentifyTerminal=t=>Id(t), ResolveTerminal=Resolve<GuestTerminal>,
            IdentifyNativeOwner=Id, ResolveNativeOwner=Resolve,
            IdentifyNativeInputs=i=>Id(i), ResolveNativeInputs=Resolve<NativeMotionInputs>,
            IdentifyBeforeStep=d=>Id(d), ResolveBeforeStep=Resolve<Action<uint>>,
            IdentifyQueueStep=d=>Id(d), ResolveQueueStep=Resolve<Func<ParkCell,ParkCell,bool>>,
            IdentifyPaused=d=>Id(d), ResolvePaused=Resolve<Func<Guest,bool>> };
        internal ParkVisitors.StateBindings VisitorBindings() => new() { GuestGraph=Graph, IdentifyReference=Id, ResolveReference=Resolve };
        internal ParkStaff.StateBindings StaffBindings() => new() { GuestGraph=Graph, PathsKey=bindings.TerrainKey,
            ClockKey="calendar", ActivationsKey="activations", IdentifyReference=Id, ResolveReference=Resolve };
        internal VisitorNeeds.StateBindings NeedsBindings() => new() { IdentifySounded=d=>Id(d), IdentifyNearbyLitter=d=>Id(d),
            ResolveSounded=Resolve<Action<int,int>>, ResolveNearbyLitter=Resolve<Func<int,(int Plain,int Vomit)>> };
    }
    static void WC(bool ok, string reason) { if (!ok) throw new InvalidDataException("WORLD core: " + reason); }
    void WorldCorePreflight(WorldCoreBindings b)
    {
        WC(_sim != null && _guests != null && _walkGrid != null, "requires initialized sim/walk/grid");
        WC(ReferenceEquals(_sim.Paths,_walkGrid) && ReferenceEquals(_guests.Paths,_walkGrid), "split path owners");
        WC(_visitors == null || ReferenceEquals(_visitors.Sim,_sim) && ReferenceEquals(_visitors.Walk,_guests), "visitor owners");
        WC(_staff == null || _visitors != null && ReferenceEquals(_staffVisitors,_visitors), "staff visitor owner");
        NativeControllerPreflight();
        WC(b.Advisor!=null || _parkAdvisor==null&&_advisorHead==null&&_advisorVoice==null&&_advisorStackView==null
            &&_advisor==null&&_advisorRules==null&&_advisorStreams.Count==0&&_advisorBindings.Count==0
            &&!_advisorUnavailable&&!_advisorSpeechTried&&!_advisorBoxFontTried,"advisor binding required for existing owners/caches");

    }

    /// <summary>Captures the ACTUAL _sim/_guests/_visitors.Needs/_staff (there is no Viewer
    /// _walk or _needs field). Return the source registry too, for render capture. Other render,
    /// audio, vehicle presentation, scene, tool and UI owners remain separate snapshots.</summary>
    public WorldCoreState CaptureWorldCoreState(WorldCoreBindings b, out WorldCoreRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(b); WorldCorePreflight(b);
        var r = new WorldCoreRegistry { Owner=this, bindings=b, providers=b.ProviderValues, Paths=_walkGrid, Sim=_sim,
            Visitors=_visitors, Needs=_visitors?.Needs, Staff=_staff, Calendar=_calendar, Awards=_awards,
            Activations=_nativeActivations, Management=_management, Park=_park };
        r.Register("paths",r.Paths); r.Register("sim",r.Sim); r.Register("finances",r.Sim.Finances);
        r.Register("calendar",r.Calendar); r.Register("awards",r.Awards);
        if (r.Activations != null) r.Register("activations",r.Activations);
        r.RegisterVisitors(); if(r.Needs!=null)r.Register("needs",r.Needs); r.RegisterStaff();
        RegisterBusPlacementNodes(r);
        RegisterNativeCapture(r);
        foreach(var p in b.CaptureProviders) { WC(p.Value is Delegate,"external mutable owner is not included: "+p.Key); r.Register("provider/"+p.Key,p.Value); }
        var guests = (_visitors?.ReferencedGuests ?? Array.Empty<Guest>()).Concat(_nativeAnimations.Keys)
            .Concat(_entranceFlow?.ReferencedGuests ?? Array.Empty<Guest>())
            .Concat(_rideQueues?.ReferencedGuests ?? Array.Empty<Guest>()).Concat(_entranceRequests.Select(x=>x.Guest))
            .Concat(_entranceResults.ReferencedGuests).Concat(b.GuestCacheOwners.Values).Concat(b.RetainedGuests)
            .Concat(_staff?.ReferencedStateObjects.OfType<Guest>() ?? Array.Empty<Guest>()).Distinct<Guest>(ReferenceEqualityComparer.Instance).Take(GuestWalk.StateGuestLimit+1).ToArray();
        WC(guests.Length<=GuestWalk.StateGuestLimit,"retained guest bound");
        var terminals = (_visitors?.ReferencedTerminals ?? Array.Empty<GuestTerminal>())
            .Concat(b.RetainedTerminals)
            .Distinct<GuestTerminal>(ReferenceEqualityComparer.Instance).ToArray();
        var features = (_staff?.ReferencedStateObjects.OfType<StaffFeature>() ?? Array.Empty<StaffFeature>()).ToArray();
        var rides = _sim.Rides.Concat(_rideQueues?.ReferencedRides ?? Array.Empty<ParkRide>()).Concat(_queueShapes.Keys).Concat(_visitors?.ReferencedRides ?? Array.Empty<ParkRide>()).Concat(b.RetainedRides)
            .Concat(terminals.Select(t=>t.Owner)).Concat(features.Select(f=>f.Ride).Where(x=>x!=null))
            .Concat(_staff?.ReferencedStateObjects.OfType<ParkRide>() ?? Array.Empty<ParkRide>())
            .Distinct<ParkRide>(ReferenceEqualityComparer.Instance).Take(4097).ToArray();
        WC(rides.Length<=4096 && terminals.Length<=100000 && features.Length<=100000,"registry bounds");
        for(int i=0;i<rides.Length;i++)r.Register("ride/"+i,rides[i]);
        for(int i=0;i<terminals.Length;i++)r.Register("terminal/"+i,terminals[i]);
        for(int i=0;i<features.Length;i++)r.Register("feature/"+i,features[i]);
        r.Graph=_guests.CaptureGraph(b.TerrainKey,r.WalkBindings(),guests);r.RegisterGuests();
        foreach(var p in b.GuestCacheOwners) { WC(p.Value.Id==p.Key,"cache owner slot mismatch");r.cacheOwners.Add(p.Key,p.Value); }
        // Bounded retired adapter: preserve actual detached scalar/VM/host state. Native vehicles,
        // linked VM graphs and assigned retired mechanics require a larger owner closure; refuse.
        var retired=new List<WorldRetiredRide>();
        foreach(var ride in rides.Where(x=>!_sim.Rides.Contains(x))) {
            WC(ride.Track==null&&ride.Coaster==null&&ride.AssignedMechanic==null,"retired native vehicle/mechanic owner not included");
            var m=ride.Machine;WC(m==null || m.Parent==null&&m.Child==null&&m.SoundChild==null,"retired linked VM graph not included");
            WC(m==null || b.RetiredMachineHosts.TryGetValue(m,out var actualHost)&&ReferenceEquals(actualHost,ride.Host),"verified retired VM host binding required");
            WC(rides.All(other=>ReferenceEquals(other,ride) || (m==null||!ReferenceEquals(other.Machine,m)) && (ride.Host==null||!ReferenceEquals(other.Host,ride.Host))),"shared retired VM/host owner not included");
            var a=m==null?null:b.Simulation.Scripts.IdentifyProgram(m.Program);
            WC(a==null || ReferenceEquals(a.Program,m.Program)&&a.SiblingScope==null,"retired spawning VM not included");
            var host=ride.Host?.CaptureState(b.Simulation.Scripts.IdentifyAnimation(ride.Host));
            retired.Add(new(r.Id(ride),ride.CaptureState(ride.Definition==null?null:b.Simulation.Scripts.IdentifyDefinition(ride.Definition),
                m==null?null:"machine",host==null?null:"host",null,null,r.MemberId),
                m?.CaptureState(a.Key,_=>throw new InvalidDataException("retired VM reference")),host));
            WC(retired[^1].Machine==null || retired[^1].Machine.RandomKind==RseMachine.RandomStateKind.OwnedSnapshotRandom,"retired external VM RNG not included");
        }
        foreach(var terminal in terminals) {
            string provider=b.TerminalProvider?.Invoke(terminal);
            WC(provider!=null && provider.StartsWith("provider/",StringComparison.Ordinal) && b.CaptureProviders.TryGetValue(provider[9..],out var enter) && enter is Func<bool>,"terminal CanEnter provider binding required");
        }
        var state=new WorldCoreState { Version=1,TerrainKey=b.TerrainKey,Runtime=CaptureRuntimeState(),
            Paths=_walkGrid.CaptureState(b.TerrainKey),Park=_park?.CaptureState(b.ParkBindings?.Invoke(_walkGrid)
                ?? throw new InvalidDataException("WORLD: Park geometry bindings required")),
            Simulation=_sim.CaptureState(b.Simulation,r.MemberId),Rides=_sim.Rides.Select(r.Id).ToArray(),Retired=retired.ToArray(),
            Terminals=terminals.Select(t=>new WorldTerminal(r.Id(t),r.Id(t.Owner),t.Approach.X,t.Approach.Z,t.Entry.X,t.Entry.Z,
                b.TerminalProvider?.Invoke(t) ?? throw new InvalidDataException("WORLD: terminal provider identity required"))).ToArray(),
            Features=features.Select(f=>new WorldFeature(r.Id(f),r.Id(f.Ride),r.Id(f.Key),f.Origin.X,f.Origin.Z,f.Entry.X,f.Entry.Z,f.Flags,f.Status)).ToArray(),
            Walk=r.Graph.Snapshot,GuestCacheOwners=r.cacheOwners.ToDictionary(p=>p.Key,p=>r.Id(p.Value)),
            Needs=r.Needs?.CaptureState(r.NeedsBindings()),Visitors=r.Visitors?.CaptureState(r.VisitorBindings()),
            Staff=r.Staff?.CaptureState(r.StaffBindings()),Calendar=_calendar.CaptureState(),Awards=_awards.CaptureState(),
            Management=_management?.CaptureState(r.Id),Activations=_nativeActivations?.CaptureState(),Mailbox=_entranceResults.CaptureState(r.Graph),
            NativeControllers=CaptureNativeControllers(r),Bus=CaptureBusState(r,b.Bus?.Invoke(r)??new BusStateBindings()),
            Advisor=b.Advisor==null?null:CaptureAdvisorState(b.Advisor(r),true),
            Providers=b.ProviderValues.ToDictionary(p=>p.Key,p=>p.Value.Clone()) };
        r.Hydrated=true;registry=r;return state;
    }

    /// <summary>Fresh detached Viewer only. Owner allocation -> staff -> hydration closes the
    /// mechanic cycle. No Add/Hire/Create/submit/replay. Caller callbacks are trusted pure binders.
    /// Failure discards the staged registry, never mutates the source world. No scene publication.</summary>
    public WorldCoreRegistry RestoreWorldCoreState(WorldCoreState s, WorldCoreBindings b)
    {
        ArgumentNullException.ThrowIfNull(s);ArgumentNullException.ThrowIfNull(b);
        WC(!IsInsideTree()&&GetParent()==null&&_sim==null&&_guests==null&&_visitors==null&&_staff==null&&_park==null&&_walkGrid==null,
            "fresh detached Viewer required");
        WC(s.Version==1&&s.TerrainKey==b.TerrainKey&&s.Rides!=null&&s.Rides.Length<=4096&&s.Retired!=null&&s.Retired.Length<=4096
            &&s.Terminals!=null&&s.Terminals.Length<=100000&&s.Features!=null&&s.Features.Length<=100000
            &&s.Providers!=null&&s.Providers.Count<=10000&&s.GuestCacheOwners!=null&&s.GuestCacheOwners.Count<=100000,"envelope bounds/version");
        WC(s.Staff==null || s.Visitors!=null&&s.Activations!=null,"staff owner dependencies");
        // Providers may close over the shared RNG object, so restore it BEFORE resolving any
        // delegate. Restoring RuntimeState afterwards would replace that object under them.
        RestoreRuntimeState(s.Runtime);
        var r=new WorldCoreRegistry {Owner=this,bindings=b,providers=s.Providers,Calendar=_calendar,Awards=_awards};
        // These readonly owners belong only to this new unpublished Viewer.
        _calendar.RestoreState(s.Calendar);_awards.RestoreState(s.Awards);
        r.Paths=ParkPaths.FromState(s.Paths,b.TerrainKey,b.Terrain);r.Register("paths",r.Paths);
        r.Register("calendar",r.Calendar);r.Register("awards",r.Awards);
        StageBusState(s.Bus,r,b.Bus?.Invoke(r)??new BusStateBindings());
        r.Activations=s.Activations==null?null:NativeActivationSequence.FromState(s.Activations);
        if(r.Activations!=null)r.Register("activations",r.Activations);
        r.Sim=ParkSim.AllocateState(s.Simulation,r.Paths,b.Simulation);r.Register("sim",r.Sim);r.Register("finances",r.Sim.Finances);
        WC(r.Sim.Rides.Count==s.Rides.Length,"ride index count");
        for(int i=0;i<s.Rides.Length;i++)r.Register(s.Rides[i],r.Sim.Rides[i]);
        foreach(var saved in s.Retired) {
            WC(saved.Ride.AssignedMechanicId==null&&saved.Ride.TrackId==null&&saved.Ride.CoasterId==null,"retired unsupported owner");
            RsePreviewHost host=null;RseMachine machine=null;
            if(saved.Host!=null) {host=new RsePreviewHost(b.Simulation.Scripts.ResolveAnimation(saved.Host.AssetKey));host.RestoreState(saved.Host,saved.Host.AssetKey);}
            if(saved.Machine!=null) {
                WC(saved.Machine.RandomKind==RseMachine.RandomStateKind.OwnedSnapshotRandom,"retired external VM RNG");
                var a=b.Simulation.Scripts.ResolveProgram(saved.Machine.ProgramKey);
                WC(a!=null&&a.Key==saved.Machine.ProgramKey&&a.SiblingScope==null,"retired program binding");
                machine=new RseMachine(a.Program,host,directory:r.Sim);
                machine.RestoreState(saved.Machine,a.Key,_=>throw new InvalidDataException("WORLD retired linked VM"));
            }
            r.Register(saved.Id,ParkRide.FromState(saved.Ride,saved.Ride.DefinitionKey==null?null:b.Simulation.Scripts.ResolveDefinition(saved.Ride.DefinitionKey),
                machine,host,null,null,r.Member));
        }
        r.Graph=GuestWalk.AllocateState(s.Walk,b.TerrainKey,r.Paths);r.RegisterGuests();
        foreach(var p in s.GuestCacheOwners){var g=r.Guest(p.Value);WC(g.Id==p.Key,"cache owner slot");r.cacheOwners.Add(p.Key,g);}
        foreach(var t in s.Terminals)r.Register(t.Id,new GuestTerminal(r.Resolve<ParkRide>(t.Ride),new(t.X,t.Z),new(t.EntryX,t.EntryZ),r.Resolve<Func<bool>>(t.Provider)));
        // Feature keys may refer to rides/other registered owners, not arbitrary old objects.
        foreach(var f in s.Features)r.Register(f.Id,new StaffFeature(new(f.X,f.Z),new(f.EntryX,f.EntryZ),f.Flags,f.Status,
            f.Ride==null?null:r.Resolve<ParkRide>(f.Ride),r.Resolve(f.Key)));
        r.Visitors=s.Visitors==null?null:ParkVisitors.AllocateState(s.Visitors,r.Sim,r.VisitorBindings());r.RegisterVisitors();
        r.Needs=s.Needs==null?null:VisitorNeeds.FromState(s.Needs,r.NeedsBindings());if(r.Needs!=null)r.Register("needs",r.Needs);
        r.Staff=s.Staff==null?null:ParkStaff.AllocateState(s.Staff,r.Visitors,r.Calendar,r.Activations,r.StaffBindings());r.RegisterStaff();
        AllocateNativeControllers(s.NativeControllers,r);
        r.Graph.Hydrate(r.WalkBindings());r.Staff?.HydrateState(s.Staff,r.StaffBindings());
        _entranceFlow?.HydrateStateBindings(); _rideQueues?.HydrateStateBindings();
        r.Visitors?.HydrateStateBindings(r.VisitorBindings());r.Sim.HydrateStaffState(r.Member);
        if(s.Management!=null){r.Management=new ParkManagement(r.Calendar,r.Awards);r.Management.RestoreState(s.Management,r.Resolve);}
        var mailbox=NativeEntranceMailbox.FromState(s.Mailbox,r.Graph);
        // Last potentially allocating scene owner. Park bindings must use this fresh path grid.
        if(s.Park!=null)r.Park=Park.FromState(s.Park,b.ParkBindings?.Invoke(r.Paths)
            ?? throw new InvalidDataException("WORLD: fresh Park geometry bindings required"));
        _walkGrid=r.Paths;_sim=r.Sim;_guests=r.Walk;_visitors=r.Visitors;_staff=r.Staff;
        _staffVisitors=r.Staff==null?null:r.Visitors;_management=r.Management;_nativeActivations=r.Activations;
        _entranceResults=mailbox;_park=r.Park;
        RegisterBusPlacementNodes(r);CompleteBusStateJoin(r);
        r.Hydrated=true;
        if(s.Advisor!=null)r.UnpublishedAdvisor=JoinAdvisorState(s.Advisor,b.Advisor?.Invoke(r)
            ??throw new InvalidDataException("Advisor world bindings required"),r);
        return r;
    }
}
