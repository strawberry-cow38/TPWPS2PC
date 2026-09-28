using System.Security.Cryptography;
using System.Text.Json;
using TPW.PS2.Data;

/// <summary>Disc-backed runtime consumer, not a whole-park save. Let the actual visitor fixture
/// board a real scripted ride; save its VM/host while guests are on it; restore into new owners
/// without running Create, then drive both with identical external mailbox acknowledgements.</summary>
static class DiscRseSaveChecks
{
    sealed class Cut
    {
        public required RseMachine.State Machine { get; init; }
        public required RsePreviewHost.State Host { get; init; }
    }
    public static void Run(Disc disc,Action<bool,string> check)
    {
        string Json<T>(T s)=>JsonSerializer.Serialize(s);
        foreach(string world in new[]{"FANTASY","HALLOW","SPACE"})
        {
            var file=disc.Files().Single(f=>f.Path=="/DATA/"+world+".WAD");
            var wad=new WadArchive(disc.Read(file.Extent,file.Size));
            var scenario=new VisitorScenario(wad,world);
            var machine=scenario.Simulation.Machine;var host=scenario.Simulation.Host;
            long cut=0;
            for(long t=100;t<=240_000;t+=100)
            {
                scenario.AdvanceTo(t);
                if(machine.GuestIds.Count>0&&host.Current!=null&&host.Frame>0){cut=t;break;}
            }
            check(cut>0&&machine.Fault==null,world+": actual visitor fixture boarded guests before snapshot");
            if(cut==0||machine.Fault!=null)continue;
            string Key(string ext)
            {
                var e=wad.Find(scenario.RideStem+ext)??throw new InvalidDataException("Missing fixture asset");
                return world+scenario.RideStem+ext+":"+Convert.ToHexString(SHA256.HashData(wad.Read(e)));
            }
            string programKey=Key(".rse"),hostKey=Key(".aps");
            Cut loaded;
            string snapshotFile=Path.Combine(Path.GetTempPath(),"tpw-script-"+Guid.NewGuid().ToString("N")+".tpwsave");
            try
            {
                ParkSaveFile.Write(snapshotFile,new Cut
                {
                    Machine=machine.CaptureState(programKey,_=>throw new InvalidOperationException("Unexpected external machine in this fixture")),
                    Host=host.CaptureState(hostKey)
                });
                loaded=ParkSaveFile.Read<Cut>(snapshotFile);
            }
            finally{if(File.Exists(snapshotFile))File.Delete(snapshotFile);}
            var state=loaded.Machine;var hstate=loaded.Host;
            var cloneHost=new RsePreviewHost(scenario.RideAnimation);
            var clone=new RseMachine(machine.Program,cloneHost);
            int emitted=0;cloneHost.EffectRequested+=_=>emitted++;
            cloneHost.RestoreState(hstate,hostKey);clone.RestoreState(state,programKey,null!);
            check(emitted==0,world+": restore did not replay Create/effects");
            check(Json(clone.CaptureState(programKey,null!))==Json(state),world+": full real VM state restored");
            check(Json(cloneHost.CaptureState(hostKey))==Json(hstate),world+": actual animation/seat/walk state restored");
            check(clone.GuestIds.SequenceEqual(machine.GuestIds)&&clone.GuestIds.Count>0,world+": boarded guest identities retained, not replaced");
            var last=Json(hstate);bool changed=false;int initialPc=machine.Pc;bool executed=false;
            for(int tick=1;tick<=1200;tick++)
            {
                long now=cut+tick*40L;
                // The same surrounding guest system acknowledges both copies. No new guests:
                // this fixture isolates continuation of the already-running VM/host owners.
                foreach(var m in new[]{machine,clone})
                {
                    if(m["VAR_LETMEOFF"]!=0)m["VAR_LETMEOFF"]=0;
                }
                host.AdvanceTo(now);cloneHost.AdvanceTo(now);
                machine.RunSlice(now);clone.RunSlice(now);
                check(machine.Pc==clone.Pc&&machine.Yield==clone.Yield&&machine.Fault==clone.Fault,
                    world+": restored real script follows the same execution path");
                check(machine.GuestIds.SequenceEqual(clone.GuestIds),world+": guest stack agrees throughout continuation");
                check(BitConverter.SingleToInt32Bits(host.Frame)==BitConverter.SingleToInt32Bits(cloneHost.Frame),
                    world+": resumed authored animation frame agrees exactly");
                if(tick%40==0)
                {
                    var current=Json(host.CaptureState(hostKey));changed|=current!=last;
                    check(Json(machine.CaptureState(programKey,null!))==Json(clone.CaptureState(programKey,null!)),world+": all private VM fields continue identically");
                    check(current==Json(cloneHost.CaptureState(hostKey)),world+": all host channels/effects/occupancy continue identically");
                }
                executed|=machine.Pc!=initialPc;
            }
            check(changed&&executed,world+": continuation changed playback and script position, not an inert equality check");
            Console.WriteLine($"DISC RSE SAVE PASS world={world} cut={cut} guests={state.GuestTop} continuation=48000ms");
        }
    }
}
