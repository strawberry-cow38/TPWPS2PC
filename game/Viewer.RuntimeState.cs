using System.Text.Json.Serialization;
using TPW.PS2.Data;
namespace TPWPS2Viewer;
public partial class Viewer
{
    // Outer simulation/provider scalars, not a complete Viewer. References, renderers, route
    // mailbox, placement order, guest caches, diagnostics and active tools are separate roots.
    // Restore only into an UNPUBLISHED Viewer. No Reset/Ensure/tick/provider callback is invoked.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RuntimeState
    {
        public required int Version {get;init;}
        public required ConsoleClock.Snapshot Clock {get;init;}
        public required int ParkTicks {get;init;}
        public required SnapshotRandom.State GuestRandom {get;init;}
        public required uint NativeAnimationRandom {get;init;}
        public required uint StaffAnimationRandom {get;init;}
        public required bool GateClosed {get;init;}
        public required int GateEvery {get;init;}
        public required int GuestCap {get;init;}
        public required int GuestStage {get;init;}
        public required int GuestSince {get;init;}
        public required bool NativeGuestAnimation {get;init;}
        public required bool NativeIdleAll {get;init;}
        public required int NativeAnimationWaits {get;init;}
        public required bool LegacyEntrance {get;init;}
        public required int EntranceFee {get;init;}
        public required uint EntranceTick {get;init;}
        public required int EntranceAccepted {get;init;}
        public required int EntranceRejected {get;init;}
        public required double BusElapsedMs {get;init;}
        public required bool BusClockAdvancedForFrame {get;init;}
        public required int BusTraffic {get;init;}
        public required int BusParameter20 {get;init;}
        public required uint BusParameterStamp {get;init;}
        public required bool BusParameterPendingReset {get;init;}
        public required int BusBatches {get;init;}
        public required int BusAdmitted {get;init;}
        public required bool BusLoadFailed {get;init;}
        public required string BusSourceKey {get;init;}
        public required bool NativeLoadsOfKids {get;init;}
        public required bool AdvisorL2 {get;init;}
        public required bool AdvisorTriangle {get;init;}
        public required bool HireCarried {get;init;}
        public required int StaffWorld {get;init;}
        public required int StaffPark {get;init;}
    }
    public RuntimeState CaptureRuntimeState()=>new(){Version=1,Clock=_parkClock.CaptureState(),ParkTicks=_parkTicks,
        GuestRandom=_guestRng.CaptureState(),NativeAnimationRandom=_nativeAnimationRand.CaptureState(),StaffAnimationRandom=_staffAnimationRand.CaptureState(),
        GateClosed=_gateClosed,GateEvery=_gateEvery,GuestCap=_guestCap,GuestStage=_guestStage,GuestSince=_guestSince,
        NativeGuestAnimation=_nativeGuestAnimation,NativeIdleAll=_nativeIdleAll,NativeAnimationWaits=NativeAnimationWaits,
        LegacyEntrance=_legacyEntrance,EntranceFee=_entranceFee,EntranceTick=_entranceTick,EntranceAccepted=_entranceAccepted,EntranceRejected=_entranceRejected,
        BusElapsedMs=_busElapsedMs,BusClockAdvancedForFrame=_busClockAdvancedForFrame,BusTraffic=_busTraffic,BusParameter20=BusParameter20,
        BusParameterStamp=_busParameterStamp,BusParameterPendingReset=_busParameterPendingReset,BusBatches=_busBatches,BusAdmitted=_busAdmitted,
        BusLoadFailed=_busLoadFailed,BusSourceKey=_busSourceKey,NativeLoadsOfKids=_nativeLoadsOfKids,AdvisorL2=_advisorL2,AdvisorTriangle=_advisorTriangle,
        HireCarried=_hireCarried,StaffWorld=_staffWorld,StaffPark=_staffPark};
    public void RestoreRuntimeState(RuntimeState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if(IsInsideTree())throw new InvalidOperationException("Runtime restore requires an unpublished Viewer");
        if(s.Version!=1||!double.IsFinite(s.BusElapsedMs)||s.BusElapsedMs<0||s.BusElapsedMs>long.MaxValue
            ||s.BusSourceKey?.Length>1024)throw new InvalidDataException("Invalid Viewer runtime state");
        ConsoleClock.ValidateState(s.Clock);var rng=SnapshotRandom.FromState(s.GuestRandom);
        _parkClock.RestoreState(s.Clock);_parkTicks=s.ParkTicks;_guestRng=rng;
        _nativeAnimationRand.RestoreState(s.NativeAnimationRandom);_staffAnimationRand.RestoreState(s.StaffAnimationRandom);
        _gateClosed=s.GateClosed;_gateEvery=s.GateEvery;_guestCap=s.GuestCap;_guestStage=s.GuestStage;_guestSince=s.GuestSince;
        _nativeGuestAnimation=s.NativeGuestAnimation;_nativeIdleAll=s.NativeIdleAll;NativeAnimationWaits=s.NativeAnimationWaits;
        _legacyEntrance=s.LegacyEntrance;_entranceFee=s.EntranceFee;_entranceTick=s.EntranceTick;_entranceAccepted=s.EntranceAccepted;_entranceRejected=s.EntranceRejected;
        _busElapsedMs=s.BusElapsedMs;_busClockAdvancedForFrame=s.BusClockAdvancedForFrame;_busTraffic=s.BusTraffic;BusParameter20=s.BusParameter20;
        _busParameterStamp=s.BusParameterStamp;_busParameterPendingReset=s.BusParameterPendingReset;_busBatches=s.BusBatches;_busAdmitted=s.BusAdmitted;
        _busLoadFailed=s.BusLoadFailed;_busSourceKey=s.BusSourceKey;_nativeLoadsOfKids=s.NativeLoadsOfKids;
        _advisorL2=s.AdvisorL2;_advisorTriangle=s.AdvisorTriangle;_hireCarried=s.HireCarried;_staffWorld=s.StaffWorld;_staffPark=s.StaffPark;
    }
}
