using TPW.PS2.Data;
using D = TPW.PS2.Data.NativeAnimationDescriptor;

/// <summary>The 2AAD48 table read from the owner's executable, and the 10E910/10EA38 control
/// block driven through its decoded paths. REJECTS: readiness taken from the requested byte,
/// readiness by requested==current equality, logical number used as an APS section, a request
/// that commits immediately, a last pair skipped on the way out, a finished one-shot that forgets
/// its logical, and a fresh model that starts in some logical. NOT exercised here: the viewer's
/// playback boundaries and guest request producers.</summary>
static class NativeLogicalAnimationChecks
{
    public static void Run(Disc disc, Action<bool,string> check)
    {
        void Check(bool ok,string why) => check(ok,"native logical animation: "+why);
        var table=NativeLogicalAnimationTable.Read(disc);
        IReadOnlyList<D> V(int l) => table.Variants(l);
        const int F=D.Inactive;

        // ---- the table, against the dump in findings/native-guest-animation-readiness.md ----
        int descriptors=Enumerable.Range(0,NativeLogicalAnimationTable.LogicalCount).Sum(l=>V(l).Count);
        Check(descriptors==33,$"22 logicals hold 33 contiguous descriptors (read {descriptors})");
        Check(V(9) is [(F,0,1,0,F,0,0,0)],"logical 9 = main APS section 1, looping, no first/last");
        Check(V(13) is [(F,0,0,0,F,0,0,0)],"logical 13 = main APS section 0, looping, no first/last");
        // Sentinel F is not a section: logical 15's first slot is F (absent), not "section 15".
        int coincide=Enumerable.Range(0,22).Count(l=>V(l).Any(d=>new[]{d.FirstSlot,d.MainSlot,d.LastSlot}.Any(s=>s==l && s!=F)));
        Check(coincide==0,$"no logical plays the APS section of its own number ({coincide} of 22; rejects slot==logical)");
        Check(V(16) is [(4,0,5,0,6,0,0,0)],"logical 16 carries first 4, main 5, last 6");
        Check(V(12) is [(F,0,4,0,F,0,1,0)],"logical 12 is a one-shot: flags 1, no last pair");
        Check(V(11).Count==8 && V(11).Sum(d=>(long)d.Weight)==100 && V(11)[0]==new D(F,0,F,0,F,0,2,93)
              && Enumerable.Range(1,6).All(v=>V(11)[v]==new D(F,0,2,v-1,F,0,6,1)) && V(11)[7]==new D(F,0,6,0,F,0,2,1),
            "logical 11: 93% inactive, six section-2 idles at 1% with flag 4, section 6 at 1%");
        Check(V(21).Count==5 && V(21).Sum(d=>(long)d.Weight)==100,"logical 21: five weighted variants summing to 100");
        Check(Throws(()=>table.Variants(22)),"logical 22 is off the table, not wrapped or defaulted");

        // A shifted table must not be silently accepted: move the table pointer and re-read.
        var entry=disc.Files().Single(f=>f.Path.Equals("/SLES_500.32",StringComparison.OrdinalIgnoreCase));
        var elf=disc.Read(entry.Extent,entry.Size);
        int tableAt=FileOffset(elf,NativeLogicalAnimationTable.TableAddress);
        var broken=(byte[])elf.Clone(); broken[tableAt+13*8]^=0x20;
        Check(Throws(()=>new NativeLogicalAnimationTable(broken)),"a descriptor pointer off the contiguous run is rejected (control)");
        var clean=new NativeLogicalAnimationTable(elf);
        Check(clean.Variants(13)[0]==V(13)[0],"byte[] and disc readers agree (control for the mutation above)");

        // ---- a fresh control block (10ECF0 on the template, copied by 1F6230) ----
        int never() => throw new InvalidOperationException("drew rand where 10E800 must not");
        var c=new NativeLogicalAnimationControl(table);
        Check(c.Current==0xff && c.Pending==0xff && c.Variant==0 && c.Phase==0,"fresh model: current FF, pending FF, variant 0, phase 0");
        Check(!c.PermitsMovement(13),"fresh model blocks a walk request: FF is not 9/13");
        Check(c.Advance(never)==(F,0) && c.Current==0xff,"fresh model with nothing pending plays inactive and stays FF");

        // ---- request then commit: pending is not current ----
        int cuts=0; void Cut() => cuts++;
        c.Request(13,0,Cut);
        Check(c.Pending==13 && c.Current==0xff,"request queues pending; current unchanged");
        Check(!c.PermitsMovement(13),"requested 13 with current FF blocks (rejects readiness from the requested byte)");
        Check(c.Advance(never)==(0,0) && c.Current==13 && c.Pending==0xff && c.Phase==1,
            "first boundary commits 13 and plays its main section 0 (no first pair)");
        Check(c.PermitsMovement(13),"current 13 permits requested 13");
        Check(c.PermitsMovement(9),"current 13 permits requested 9 (rejects requested==current equality)");
        Check(c.Advance(never)==(0,0) && c.Phase==1,"13 loops its main at every boundary");
        Check(cuts==0,"no flag 2, no cut");

        // ---- entering 16 with flag 2, leaving through its last pair ----
        c.Request(16,2,Cut);
        Check(cuts==1 && c.Pending==16,"flag 2 cuts the current record once and queues 16");
        Check(c.Advance(never)==(4,0) && c.Current==16 && c.Phase==0,"13 has no last pair: 16 commits and plays first 4");
        Check(c.PermitsMovement(11) && !c.PermitsMovement(13),"current 16: requested 11 moves, requested 13 waits");
        Check(c.Advance(never)==(5,0) && c.Phase==1,"then main 5");
        Check(c.Advance(never)==(5,0),"main 5 loops (flags 0)");
        c.Request(13,0,Cut);
        Check(c.Advance(never)==(6,0) && c.Current==16 && c.Phase==2 && c.Pending==13,
            "leaving 16 plays last 6 WITHOUT committing 13");
        Check(!c.PermitsMovement(13),"guest still waits while the last pair plays (rejects a skipped last pair)");
        Check(c.Advance(never)==(0,0) && c.Current==13 && c.Phase==1,"next boundary commits 13");
        Check(c.PermitsMovement(13),"and only then may the guest move");

        // ---- request equality and flag 20 ----
        c.Request(16,0,Cut); c.Request(13,0,Cut);
        Check(c.Pending==16 && cuts==1,"a request equal to current returns early and leaves the queued 16");
        c.Request(13,0x20,Cut);
        Check(c.Pending==0xff,"flag 20 clears pending even when the request equals current");

        // ---- one-shot keeps its logical ----
        var o=new NativeLogicalAnimationControl(table);
        o.Request(12,0,Cut); o.Advance(never);
        Check(o.Current==12 && o.Advance(never)==(F,0) && o.Current==12 && o.Advance(never)==(F,0),
            "finished one-shot 12 plays inactive but stays current 12 (not FF)");

        // ---- pending withdrawn during a last pair ----
        var w=new NativeLogicalAnimationControl(table);
        w.Request(16,0,Cut); w.Advance(never); w.Advance(never);
        w.Request(13,0,Cut); w.Advance(never);
        w.Request(16,0x20,Cut);
        Check(w.Pending==0xff && w.Phase==2,"flag 20 withdrew 13 during 16's last pair");
        Check(w.Advance(never)==(F,0) && w.Current==0xff && w.Phase==0,"phase 2 with nothing pending ends at FF, phase 0");

        // ---- weighted selection (10E800) with literal draws ----
        foreach(var (draw,variant,slot) in new[]{(0,0,F),(92,0,F),(93,1,2),(98,6,2),(99,7,6),(193,1,2)})
        {
            var s=new NativeLogicalAnimationControl(table);
            s.Request(11,0,Cut);
            var outp=s.Advance(()=>draw);
            Check(s.Variant==variant && outp.Slot==slot,$"logical 11 draw {draw} -> variant {variant}, section {slot:X}");
        }
        var idle=new NativeLogicalAnimationControl(table);
        idle.Request(11,0,Cut); idle.Advance(()=>95);
        Check(idle.Variant==3 && idle.Phase==1,"entered idle variant 3 (flags 6)");
        Check(idle.Advance(()=>100)==(2,2) && idle.Variant==3,"flag 4 retention: rand%400 >= 100 keeps variant 3");
        // A queue, not a constant: a second draw must be able to differ from the first, or an
        // implementation that draws twice passes (a constant stub let that mutant survive).
        var draws=new Queue<int>(new[]{400+97,3});
        Check(idle.Advance(draws.Dequeue)==(2,4) && idle.Variant==5 && draws.Count==1,
            "rand%400 < 100 re-rolls with the SAME remainder, one draw (97 -> variant 5)");
        var still=new NativeLogicalAnimationControl(table);
        still.Request(11,0,Cut); still.Advance(()=>0);
        Check(still.Advance(()=>94)==(2,1) && still.Variant==2,"inactive variant 0 re-rolls every boundary (flag 2, no flag 4)");
        idle.Request(21,0,Cut);
        Check(Throws(()=>idle.Advance(()=>100)),"pending 21 from a flag-4 idle hits the uninitialized-variant path: refused, not invented");

        // ---- idle picks (2106E8 reads 2EEC18 + 4*i, bounded by 1448E0(4)) ----
        Check(table.IdleStates.SequenceEqual(new[]{14,5,6,13}),$"2106E8 picks {string.Join(",",table.IdleStates)} = 14,5,6,13");
        int noDraw(int n) => throw new InvalidOperationException("2106E8 drew where it must return first");
        var p=new NativeGuestAnimation(table,(s2,v2)=>16){Requested=11,Stamp=0};
        p.IdlePick(120,noDraw,table.IdleStates);
        Check((p.Requested&0x1f)==11,"request 11 is held until the stamp is 120 updates old, without a draw");
        var picks=new Queue<int>(new[]{1});
        p.IdlePick(121,n=>n==4?picks.Dequeue():throw new InvalidOperationException("11 past 120 must pick directly"),table.IdleStates);
        Check((p.Requested&0x1f)==5 && picks.Count==0,"past 120 updates, 11 picks at once with one rand(4): index 1 -> 5");
        p.Requested=0x40|13;
        p.IdlePick(50,n=>n==100?10:throw new InvalidOperationException(),table.IdleStates);
        Check(p.Requested==(0x40|13),"a non-11 request survives rand(100) = 10");
        var two=new Queue<int>(new[]{9,3});
        p.IdlePick(50,n=>two.Dequeue(),table.IdleStates);
        Check(p.Requested==(0x40|13) && two.Count==0,"rand(100) = 9 re-picks (index 3 -> 13), keeping the upper bits");

        // ---- playback beside the control (1ACFC0) with kid-shaped durations ----
        int? Kid(int s2,int v2) => (s2,v2) switch { (0,0)=>16, (1,0)=>16, (2,<6)=>new[]{16,16,32,16,32,32}[v2], (6,0)=>40, _=>null };
        var g=new NativeGuestAnimation(table,Kid){Requested=11};
        g.Push(); g.Update(40,()=>0);
        Check(g.Control.Current==11 && g.Slot==F && g.Held==null,"11 commits at the first update; draw 0 is inactive, nothing to hold");
        g.Update(40,()=>99);
        Check(g.Slot==6 && g.Frame==0f && g.Duration==40f,"a later re-roll of 99 starts section 6 (40 frames) at frame 0");
        g.Requested=13; g.Push();
        int waited=0; while(!g.PermitsMovement && waited<100){ g.Update(40,never); waited++; }
        Check(waited==34,$"13 waits out the 40-frame record at 1.2 frames per 40 ms update: {waited} updates (expect 34)");
        Check(g.Slot==0 && Math.Abs(g.Frame-0.8f)<1e-3,$"commit carries the overshoot: section 0 from frame {g.Frame:F2} (expect 0.80)");
        g.Push(2);
        Check(g.Control.Pending==0xff && g.Frame!=g.Duration,"flag 2 with request equal to current: no cut");
        g.Requested=12; g.Push(2);
        Check(g.Frame==g.Duration && g.Control.Pending==12,"flag 2 cuts: the record reads as complete");
        g.Update(40,never);
        Check(g.Slot==F && g.Held==(0,0) && g.Frame==16f && g.Control.Current==12,
            "logical 12's section 4 is absent from this model: the old record's final pose is HELD, slot F");

        // ---- 191E10 predicate ----
        Check(NativeLogicalAnimationControl.MovementPermitted(13,false,0xff),"no visual object permits motion");
        Check(!NativeLogicalAnimationControl.MovementPermitted(13,true,0xff),"visual with handle 0 reads FF and blocks (distinct from no visual)");
        Check(NativeLogicalAnimationControl.MovementPermitted(0x20|11,true,0xff),"only the low five bits are the request");
        Check(!NativeLogicalAnimationControl.MovementPermitted(0x40|13,true,16),"high bits do not hide a 13 request");
    }

    static bool Throws(Action a){ try{ a(); return false; } catch(Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException or NotSupportedException){ return true; } }

    static int FileOffset(byte[] elf,uint va)
    {
        uint U32(int o)=>BitConverter.ToUInt32(elf,o); int U16(int o)=>BitConverter.ToUInt16(elf,o);
        int ph=(int)U32(28);
        for(int i=0;i<U16(44);i++){ int p=ph+i*U16(42);
            if(U32(p)==1 && va>=U32(p+8) && va<U32(p+8)+U32(p+16)) return (int)(U32(p+4)+va-U32(p+8)); }
        throw new InvalidDataException("not file-backed");
    }
}
