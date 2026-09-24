using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

public partial class Viewer
{
    // Native audio objects are not placed-ride IDs. Keep this owner separate from
    // positive placed IDs and owner0 guest one-shots.
    const int BusSoundOwner = int.MinValue + 97, BusSoundTag = 1;
    NativeBus _nativeBus;
    Model _nativeBusMesh;
    NativeBusCatalogue _busCatalogue;
    string _busSourceKey;
    bool _busLoadFailed;
    double _busElapsedMs;
    bool _busClockAdvancedForFrame;
    int _busTraffic;
    int BusParameter20 { get; set; }
    uint _busParameterStamp;
    bool _busParameterPendingReset;
    uint BusClock => unchecked((uint)(long)(Math.Floor(_busElapsedMs / 10) * 10));
    int _busBatches, _busAdmitted;
    readonly List<(Node3D Node,int RuntimeId,RideDefinition Definition)> _busPlacements = new();

    partial void BusAudioStateChanged(int state,uint activeMs);
    partial void UpdateBusAudio();
    partial void ResetBusAudio();

    void ResetNativeBus()
    {
        ResetBusAudio();
        if (_nativeBus?.Root is {} root && IsInstanceValid(root)) root.QueueFree();
        _nativeBus=null;_nativeBusMesh=null;_busCatalogue=null;_busSourceKey=null;
        _busLoadFailed=false;_busElapsedMs=0;_busClockAdvancedForFrame=false;
        _busTraffic=0;BusParameter20=0;_busParameterPendingReset=false;
        _busBatches=0;_busAdmitted=0;_busPlacements.Clear();
    }

    void RegisterBusPlacement(Node3D node,int runtimeId,RideDefinition definition)
    {
        _busPlacements.Add((node,runtimeId,definition));
        if(definition?.CompiledEntry==null)
            GD.Print($"[bus] placed object {runtimeId} has no compiled identity; omitted from native demand/catalog representation");
    }

    bool EnsureNativeBus()
    {
        if(_nativeBus!=null) return true;
        if(_lib==null || _terrainPath==null || _terrainModel?.Field==null || _guests==null) return false;
        string key=_lib.WadName+":"+_terrainPath;
        if(_busLoadFailed && _busSourceKey==key) return false;
        _busSourceKey=key;
        try
        {
            var selection=NativeParkSelection.Ordinary(_lib.WadName,_terrainPath);
            _busCatalogue=NativeBusCatalogue.Read(_lib.Disc,selection);
            if(!_guests.Paths.Open(_busCatalogue.Point0))
                throw new InvalidDataException("native bus point0 is not on the installed entrance corridor");
            string stem=selection.BusStem;
            var assets=_lib.Rides.Single(r=>r.Name.Equals($"features/{stem}/{stem}.mps",StringComparison.OrdinalIgnoreCase));
            _nativeBusMesh=_lib.LoadModel(assets);
            var animation=new Aps(_lib.Read(assets.Animation));
            _nativeBus=new NativeBus(_nativeBusMesh,animation,m=>TextureNear(assets.Model.Path,m),
                unchecked((uint)_guestRng.Next()),BusClock,NativeBusState,AdmitBusBatch);
            _nativeBus.Root.Name="NativeBus";
            AddChild(_nativeBus.Root); // authored model/world coordinates; one root Z conversion
            GD.Print($"[bus] loaded {key} -> {stem}; point0={_busCatalogue.Point0}; catalog={_busCatalogue.TotalEntries}");
            GD.Print("[bus] boundary: UNPORTED entrance-group queues and sticky departure-deferral pressure. Zero inputs BYPASS the backlog reduction (entrance bound stays20) and departure-pressure veto; busy-park admissions can exceed native. No attraction-minigame session exists in this port.");
            return true;
        }
        catch(Exception e)
        {
            _busLoadFailed=true;
            GD.PrintErr($"[bus] cannot initialize {key}: {e.Message}");
            return false;
        }
    }

    void NativeBusState(int state,uint activeMs)
    {
        if(state is 0 or 1) { BusParameter20=0;_busParameterPendingReset=false; }
        else { BusParameter20=51;_busParameterStamp=activeMs;_busParameterPendingReset=true; }
        BusAudioStateChanged(state,activeMs);
        GD.Print($"[bus.phase] {activeMs}ms state={state} parameter20={BusParameter20}");
    }

    /// <summary>Native fitting1/space200, transformed by its animated node. Quantization
    /// precedes the common renderer-space conversion (147828→111758).</summary>
    Vector3? BusSoundPosition()
    {
        if(_nativeBus?.Model.LastWorld==null || _nativeBusMesh?.FindFitting(1,0x200) is not {Node:>=0} fit)
            return null;
        if(!_nativeBus.Model.LastWorld.TryGetValue(_nativeBusMesh.NodeOffset(fit.Node),out var matrix)) return null;
        var p=System.Numerics.Vector3.Transform(_nativeBusMesh.FittingLocal(fit),matrix);
        static float Quantize(float f)=>unchecked((short)(int)(f*256)) >> 8;
        return _nativeBus.Root.GlobalTransform*new Vector3(Quantize(p.X),Quantize(p.Y),Quantize(p.Z));
    }

    void PresentNativeBus()
    {
        if (_nativeBus == null) return;
        // Deliberately do not use the ten-ms quantized simulation BusClock for rendering.
        // The active elapsed clock is shared, but presentation retains its fractional ms.
        double whole = Math.Floor(_busElapsedMs);
        _nativeBus.Present(unchecked((uint)(long)whole), (float)(_busElapsedMs - whole));
    }

    void TickNativeBus()
    {
        if(!_busClockAdvancedForFrame) _busElapsedMs+=ParkSim.TickMilliseconds; // explicit fixed-time capture winding
        if(!EnsureNativeBus()) return;
        uint now=BusClock;
        if(_busParameterPendingReset && unchecked(now-_busParameterStamp)>2000)
        { BusParameter20=0;_busParameterPendingReset=false; }
        // One executed park update follows the traced saturated countdown-delta route.
        // This is deliberately NOT elapsed-ms delta: animation catches elapsed time,
        // countdowns count the updates actually executed by the park owner.
        _busTraffic=_nativeBus.Update(now,0x4000,_busTraffic,
            open:_visitors!=null && !_gateClosed,specialObjectAbsent:true,flaggedGuestCount:0);
        UpdateBusAudio();
    }

    IReadOnlyList<NativeBusDemand.Attraction> BusObjects()
    {
        var live=_park.Placed.Select(p=>p.Node).ToHashSet();
        _busPlacements.RemoveAll(p=>!IsInstanceValid(p.Node) || !live.Contains(p.Node));
        var result=new List<NativeBusDemand.Attraction>();
        foreach(var p in _busPlacements)
        {
            var record=p.Definition?.CompiledEntry;
            if(record==null) continue;
            var ride=_sim?.Rides.FirstOrDefault(r=>r.Id==p.RuntimeId);
            int? value=ride?.Value;
            if(value==null && NativeRideValue.CreateDefaultState(record) is {} initial)
                value=NativeRideValue.Calculate(record,initial);
            // Type8 is a capacity entry but is not visited by the demand iterator.
            if(value==null && (int)record.Kind!=8)
                throw new InvalidDataException($"unresolved native value for placed {record.Key}/{record.Kind}");
            // Standard new-instance tier0 (116070/74). No operating upgrade-tier UI is
            // implemented yet; the separate TrackUpgrade family is not this tier byte.
            result.Add(new((int)record.Kind,record.Key,value??0,0));
        }
        return result;
    }

    void AdmitBusBatch(int pointSelector)
    {
        if(pointSelector!=0) throw new InvalidOperationException("unsupported native bus drop-off selector");
        if(_visitors==null || _busCatalogue==null) return;
        if(_guestCap==0) return; // explicit existing capture/fixture auto-admission off switch, not a native cap
        _busBatches++;
        var objects=BusObjects();
        var ordered=new List<NativeBusDemand.Attraction>();
        foreach(int kind in new[]{3,6,1,7,4,5,2})
            for(int i=objects.Count-1;i>=0;i--) if(objects[i].Kind==kind) ordered.Add(objects[i]);
        int score=NativeBusDemand.Score(ordered,n=>_guestRng.Next(n));
        int ceiling=_busCatalogue.Ceiling(objects.Select(o=>(o.Kind,o.Key)));
        int population=_visitors.Plans.Keys.Concat(_guests.Guests.Select(g=>g.Id)).Distinct().Count();
        // Zero is a permissive bypass, not a neutral contribution. Native entrance groups
        // are not Walk.Guests or path occupancy; do not substitute either as a guessed count.
        var bounds=NativeBusDemand.Bounds(score,_busCatalogue.DemandOffset,_busCatalogue.DemandDivisor,
            entranceGroupCount:0,ceiling,population);
        int requested=bounds.Requested;
        var at=_busCatalogue.Point0;
        int admitted=0;
        if(_guests.Paths.Open(at))
            for(int i=0;i<requested;i++) { _visitors.Arrive(at,at);admitted++; }
        _busAdmitted+=admitted;
        GD.Print($"[bus.arrivals] {BusClock}ms request={_busBatches} score={score} ceiling={ceiling} population={population} bounds[demand={bounds.Demand},entrance={bounds.Entrance},headroom={bounds.Headroom}] requested={requested} admitted={admitted} point0={at}");
    }
}
