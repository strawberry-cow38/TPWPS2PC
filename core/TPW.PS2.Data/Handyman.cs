namespace TPW.PS2.Data;

/// <summary>⭐⭐ THE HANDYMAN ("Cleaner"), vtable `0x35F9D8`, READ in
/// findings/staff-handymen-entertainers.md §3 against the decompile of `0x144BF8..0x145DC0`.
///
/// <code>
///   state 0     find work 0x1457E0: 1-in-16 sound 0xA1; tired/strike check; coin flip rand(2):
///               0 → nearest unclaimed litter, else the toilet score (falling back to litter only
///               when NO toilet qualifies); nothing → sound 0xA0, state 0xD (patrol)
///   state 0xB   waiting for the planner; event 1 → walk, event 2 → 0x144D60
///   state 2     0x144EC8: no slot and mode 7 → 0x1B (sweep), mode 0x12 → 0x33 (toilet, HIDDEN)
///   state 0x1B  0x145598: logical 16 every tick; when now &gt; deadline: morale +1 (vomit -6),
///               tiredness +5, unclaim, free the litter, state 0
///   state 0x33  0x1456D8: hidden; when now &gt; deadline: condition &lt; 40 → morale -10, tiredness +5;
///               else morale +5; tiredness +5; condition 100 and the day stamped; state 0, shown
/// </code>
/// `deadline = now + time[L]` at arrival and the test is strict, so a job lasts `time[L] + 1`
/// updates after the arrival tick. The tired/strike check runs only in state 0, so a handyman never
/// abandons a job to rest or strike: he finishes it. His patrol rectangle bounds ONLY where he idles;
/// neither search reads it (§3.9).</summary>
public sealed class Handyman : StaffMember
{
    /// <summary>Modes of the handyman's walks: 7 litter, 0x12 toilet (§3.4-§3.5).</summary>
    public const byte ModeLitter = 0x07, ModeToilet = 0x12;
    /// <summary>His own states: 0x1B "Cleaning up litter" (`0x10CA98`), 0x33 cleaning a toilet.</summary>
    public const byte StateSweeping = 0x1B, StateCleaningToilet = 0x33;
    /// <summary>Bank-8 (`AUDIO/GLOBAL/staf`) events: 0xA1 the 1-in-16 idle `cl_st01/04`
    /// (" EVT_CLEANER_STAND"), 0xA0 nothing-to-do `cl_wk03/04` (" EVT_CLEANER_WALK"). No sound on a
    /// sweep or clean completing (§3.10).</summary>
    public const int SoundIdle = 0xA1, SoundNothingToDo = 0xA0;

    internal Handyman(ParkStaff park, int poolSlot) : base(park, StaffKind.Handyman, poolSlot) { }

    /// <summary>`vt+0x18C` = `0x144D38`: `u32 [0x35FBE0 + 12*L]` -- 10/15/20/20/18. The base speed
    /// bits (15) are not used for him.</summary>
    public override int Speed => StaffTables.HandymanSpeed[Level];

    /// <summary>`0x1459B0`: 0x1B → sweep, 0x33 → toilet; 0 and the rest are the base's.</summary>
    protected override bool UpdateJobState()
    {
        if (State == StateSweeping) { Sweep(); return true; }
        if (State == StateCleaningToilet) { CleanToilet(); return true; }
        return false;
    }

    /// <summary>`vt+0x1A4` = `0x1457E0`, READ (MIPS `0x1457F0..0x1458E8`).</summary>
    protected override void FindWork()
    {
        if (Park.Random(16) == 0) Park.RaiseSound(this, SoundIdle);   // (rand_u32 & 0xF) == 0
        if (TiredOrStriking()) return;
        bool found = Park.Random(2) == 0 ? FindLitter() : FindToilet();
        if (found) return;
        Park.RaiseSound(this, SoundNothingToDo);
        GoalDepth = 0; State = StatePatrol;
    }

    /// <summary>⭐ `0x144FD8`, the litter search. Every item on the active list (`0x14D1F8`, newest
    /// first) with no claimant; Manhattan in CELLS, park-wide -- no distance limit, no patrol-rectangle
    /// test, no vomit preference, no reachability test. Strict `&lt;`, so of equally near items the
    /// NEWEST wins. Claim, mode 7, target, then route to the item's RAW 1/256 position with 0x11;
    /// refused → target 0, UNCLAIM, fail; admitted → stamp, push, state 0xB.</summary>
    bool FindLitter()
    {
        LitterItem best = null; uint bestDistance = uint.MaxValue;
        var me = Cell;
        foreach (var item in Park.Litter.Active)
        {
            var c = item.Cell;
            uint d = (uint)(Math.Abs(c.X - me.X) + Math.Abs(c.Z - me.Z));
            if ((best == null || d < bestDistance) && item.Claimant == null) { best = item; bestDistance = d; }
        }
        if (best == null) return false;
        Mode = ModeLitter; Target = best; best.Claimant = this;
        if (!Request(best.Position, 0x11))
        {
            Target = null; best.Claimant = null;                          // debug print 0x35F928
            return false;
        }
        Stamp = Park.Now; PushGoal(); State = StateWaitForRoute;
        return true;
    }

    /// <summary>⭐⭐ `0x145250`, the toilet search, EXACTLY AS SHIPPED (MIPS `0x14530C..0x14539C`):
    /// <code>
    ///   candidates: placed objects with vt+0x134 (DBA +0x2E bit 0) and condition c &lt; 60
    ///   score = |t.x - my.x| + |t.z - my.z| * (c + 1)          // unsigned; LOWEST wins
    /// </code>
    /// `mult $v1,$v1,$v0` at `0x14535C` multiplies ONLY the z distance; `0x14536C` adds the x. So a
    /// toilet in the handyman's own row is scored by `|dx|` alone however dirty it is, and one row
    /// away at c = 59 costs `|dx| + 60`. The intended `(|dx|+|dz|)*(c+1)` is INFERRED and NOT used.
    /// The position is `vt+0x74` = `0x1E1FA8`, the feature's ORIGIN cell, not its entry. Ties: strict
    /// `&lt;`, the first in the placed-object list wins (⚠ that list's order is not traced; the port
    /// uses <see cref="ParkStaff.Features"/>'s). No claim is taken or checked, so several handymen can
    /// be sent to one toilet. None qualifies → the litter search; a chosen toilet whose route is
    /// REFUSED returns 0 WITHOUT trying litter. The route targets the ENTRY cell's centre
    /// (`0x1E1760`), flags 0x11, mode 0x12; admitted → stamp, depth 0 (no push), state 0xB.</summary>
    bool FindToilet()
    {
        StaffFeature best = null; uint bestScore = uint.MaxValue;
        var me = Cell;
        foreach (var feature in Park.PlacedFeatures())
        {
            if (!feature.IsToilet || feature.Ride == null) continue;       // vt+0x134; ⚠ a toilet must be a live ParkRide
            int c = feature.Ride.Condition;                                // 0x130938
            if (c >= StaffTables.ToiletCandidateBelow) continue;
            uint score = unchecked((uint)(Math.Abs(feature.Origin.X - me.X)
                                        + Math.Abs(feature.Origin.Z - me.Z) * (c + 1)));
            if (best == null || score < bestScore) { best = feature; bestScore = score; }
        }
        if (best == null) return FindLitter();
        Target = best.Key; Mode = ModeToilet;
        if (!Request(CellCentre(best.Entry.X, best.Entry.Z), 0x11)) { Target = null; return false; }
        Stamp = Park.Now; GoalDepth = 0; State = StateWaitForRoute;
        return true;
    }

    /// <summary>`vt+0x144` = `0x144EC8`: with no slot, mode 7 → deadline `now + sweep[L]`, state 0x1B;
    /// mode 0x12 → deadline `now + toilet[L]`, state 0x33, HIDDEN (`vt+0x2C(0)`). Everything else,
    /// and every slot-present pass, is the base `0x1DB970`.</summary>
    protected override void SegmentEnd()
    {
        if (RouteSlot == -1 && Mode == ModeLitter)
        {
            FreeRoute();
            GoalDepth = 0; Stamp = unchecked(Park.Now + (uint)StaffTables.HandymanSweepTicks[Level]);
            State = StateSweeping;
            return;
        }
        if (RouteSlot == -1 && Mode == ModeToilet)
        {
            FreeRoute();
            GoalDepth = 0; Stamp = unchecked(Park.Now + (uint)StaffTables.HandymanToiletTicks[Level]);
            State = StateCleaningToilet; SetShown(false);
            return;
        }
        BaseSegmentEnd();
    }

    /// <summary>State 0x1B, `0x145598`, READ (MIPS `0x145598..0x1456D0`).</summary>
    void Sweep()
    {
        LogicalRequest = StaffTables.LogicalWork;                          // 16, every update
        if (!(Stamp < Park.Now)) return;                                   // `sltu`: deadline < now
        if (Target is not LitterItem litter || !litter.Active)
        {
            // ⚠ Managed guard: natively the target is always the claimed item (only its claimant can
            // remove it). Should the port ever lose it, finish the job rather than dereference it.
            State = StateIdle; Target = null; GoalDepth = 0;
            return;
        }
        Morale = (sbyte)(litter.Vomit ? Math.Max(0, Morale - 6) : Math.Min(100, Morale + 1));
        Tiredness = (sbyte)Math.Min(100, Tiredness + 5);
        litter.Claimant = null;
        // 0x1824A8(litter cell): stop a prank stink in the swept LITTER's cell -- the prank keyed it by the
        // GUEST's, up to 100/256 cell away, so an edge prank's stink outlives the sweep (findings §1.5).
        Park.Stinks.Remove(litter.Cell.X, litter.Cell.Z);
        Park.Litter.Remove(litter);                                        // 0x14B460, its ONLY caller
        State = StateIdle; Target = null; GoalDepth = 0;
    }

    /// <summary>State 0x33, `0x1456D8`, READ (MIPS `0x1456E8..0x1457A4`). The condition is read AT
    /// COMPLETION, so wear guests add during the clean counts; guests may use the toilet meanwhile
    /// (`+0xAC` is untouched and `0x1309F8` does not look at staff).</summary>
    void CleanToilet()
    {
        if (!(Stamp < Park.Now)) return;
        if (Target is ParkRide toilet)
        {
            if (toilet.Condition < StaffTables.ToiletFilthyBelow)
            {
                Morale = (sbyte)Math.Max(0, Morale - 10);
                Tiredness = (sbyte)Math.Min(100, Tiredness + 5);
            }
            else Morale = (sbyte)Math.Min(100, Morale + 5);
            Tiredness = (sbyte)Math.Min(100, Tiredness + 5);
            toilet.Service(Park.Clock.TotalDays);                          // 0x130978: 100 and +0xA8 = today
        }
        // ⚠ A non-ParkRide target cannot happen (FindToilet only targets live rides, and a demolished
        // one is caught by the removal notice first); it is finished without a clean.
        GoalDepth = 0; State = StateIdle; SetShown(true);
    }

    /// <summary>`vt+0x16C` = `0x144D60`: event 2 with mode 7 and a target → if the target is litter
    /// (class 0xD) UNCLAIM it (else a debug print), target 0, **state 0xD**, depth 0; 1 → walk;
    /// 3 → ignored; 5 → free the route, target 0, state 5 -- ⚠ WITHOUT releasing a claim, so a claimed
    /// item would stay claimed forever. No sender of code 5 was found (§9); kept as read. Else base.</summary>
    internal override void RouteEvent(int code)
    {
        switch (code)
        {
            case 2 when Mode == ModeLitter && Target != null:
                if (Target is LitterItem litter) litter.Claimant = null;
                Target = null; State = StatePatrol; GoalDepth = 0;
                return;
            case 1: GoalDepth = 0; State = StateWalk; return;
            case 3: return;
            case 5: FreeRoute(); GoalDepth = 0; Target = null; State = StateWander; return;
            default: BaseRouteEvent(code); return;
        }
    }

    /// <summary>`vt+0x194` = `0x145B48` (fire): unclaim a litter target, then `0x1925A8`.</summary>
    internal override void Release()
    {
        if (Target is LitterItem litter) litter.Claimant = null;
        base.Release();
    }

    /// <summary>`0x145AC8`, the route-reset release: unless sweeping or cleaning a toilet, unclaim a
    /// litter target and `0x1DC780`. A handyman mid-job keeps it.</summary>
    internal override void OnRouteReset()
    {
        if (State is StateSweeping or StateCleaningToilet) return;
        if (Target is LitterItem litter) litter.Claimant = null;
        base.OnRouteReset();
    }
}
