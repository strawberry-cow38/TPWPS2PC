using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;
using Outcome = TPW.PS2.Data.NativeGuestMotion.Outcome;

/// <summary>Numeric controls for the read instruction paths, NOT a live GuestWalk
/// integration claim. Explicit negative/signed/fractional cases discriminate from
/// the existing whole-cell motion policy.</summary>
static class NativeGuestMotionChecks
{
    public static void Run(Action<bool,string> check)
    {
        void Check(bool ok,string why) => check(ok,"native guest motion arithmetic: "+why);
        Point Q(short x,short z) => NativeGuestMotion.DecodeTarget(
            NativeGuestMotion.EncodeTarget(0xdead87ffu,new(x,z)));

        foreach(var row in new (short X,short Z,short ExpectedX,short ExpectedZ)[] {
            (31,32,0,64), (95,96,64,128), (-33,-32,-64,0),
            (0x1280,0x2380,0x1280,0x2380), (0x1280,0x2340,0x1280,0x2340),
            (0x1280,0x2300,0x1280,0x2300), (0x1280,0x22c0,0x1280,0x22c0),
            (0x12a1,0x22a0,0x12c0,0x22c0),
            (short.MaxValue,short.MinValue,short.MinValue,short.MinValue) })
        {
            var encoded=NativeGuestMotion.EncodeTarget(0xdead85a5u,new(row.X,row.Z));
            Check(NativeGuestMotion.DecodeTarget(encoded)==new Point(row.ExpectedX,row.ExpectedZ),
                $"literal signed quarter target {row.X}/{row.Z} -> {row.ExpectedX}/{row.ExpectedZ}");
            Check((encoded&0x87ffu)==0x85a5u,"target encoding preserves live allocation/link bits");
        }
        Check(Q(-64,-256)==new Point(-64,-256),"negative encoded cells decode to negative signed fixed-point coordinates");
        Check((NativeGuestMotion.EncodeTarget(0,new(0,0))&0x8000)==0,
            "position write does not invent allocation flag");
        Check((NativeGuestMotion.EncodeTarget(0xffff,new(0,0))&0xffff)==0x87ff,
            "zero target clears BOTH old fractional fields, retains terminal/allocated bits");

        foreach(sbyte speed in new sbyte[]{-128,-1,0,4,5,15,29})
            Check(NativeGuestMotion.StepAmount(speed,0x4000)==Math.Max(5,(int)speed),
                $"signed active speed {speed}, minimum5 under saturated delta");
        Check(NativeGuestMotion.StepAmount(15,0x2000)==7,"half delta truncates15/2, not rounds");
        Check(NativeGuestMotion.StepAmount(15,128)==0,"small nonzero delta can move zero units");
        Check(NativeGuestMotion.StepAmount(15,0)==0,"zero delta does not manufacture a minimum movement");
        Check(NativeGuestMotion.StepAmount(15,-1)==262143,"negative delta retains native low32/logical shift behavior");
        Check(NativeGuestMotion.StepAmount(127,int.MaxValue)==131071,"multiply is low32, not widened arithmetic");

        Point start=new(0x1280,0x2380), target=new(0x1280,0x2340);
        var step=NativeGuestMotion.AdvanceCoordinates(start,target,15,0x4000,128,128);
        Check(step.Position==new Point(0x1280,0x2371) && step.Outcome==Outcome.Moving,
            "native quarter-cell queue step is15 raw units, not whole-cell movement");
        var close=NativeGuestMotion.AdvanceCoordinates(new(0x1280,0x2344),target,15,0x4000,128,128);
        Check(close.Position==target && close.Outcome==Outcome.AtWaypoint,"overshoot clamps exactly to decoded waypoint");
        var diagonal=NativeGuestMotion.AdvanceCoordinates(start,new(0x12c0,0x23c0),15,0x4000,128,128);
        Check(diagonal.Position==new Point(0x128f,0x238f),"axes each receive full step, no length normalization");
        var signed=NativeGuestMotion.AdvanceCoordinates(start,target,-1,0x4000,128,128);
        Check(signed.Position==new Point(0x1280,0x237b),"signed FF speed takes minimum5 instead of255");
        var tiny=start;
        for(int i=0;i<100;i++) tiny=NativeGuestMotion.AdvanceCoordinates(tiny,target,15,128,128,128).Position;
        Check(tiny==start,"no invented carry accumulates100 sub-step updates");
        var stationary=NativeGuestMotion.AdvanceCoordinates(target,target,15,0,128,128);
        Check(stationary.Position==target && stationary.Outcome==Outcome.AtWaypoint,
            "already-at-target completes waypoint even with zero movement delta");
        var zero=NativeGuestMotion.AdvanceCoordinates(start,target,15,0,128,128);
        Check(zero.Position==start && zero.Outcome==Outcome.Moving,"zero delta away from target is not completion");
        var negative=NativeGuestMotion.AdvanceCoordinates(start,target,15,-1,128,128);
        Check(negative.Position==target && negative.Outcome==Outcome.AtWaypoint,
            "native negative-delta large step still clamps rather than overshoots");

        foreach(var row in new (Point Start,Point Target,int Columns,int Rows)[] {
            (new(0,128),new(-64,128),128,128),
            (new(128,0),new(128,-64),128,128),
            (new(8190,128),new(8256,128),32,128),
            (new(128,8190),new(128,8256),128,32),
            (new(0,128),new(-64,192),128,128),
            (new(128,0),new(192,-64),128,128) })
        {
            var rejected=NativeGuestMotion.AdvanceCoordinates(row.Start,row.Target,15,0x4000,row.Columns,row.Rows);
            Check(rejected.Outcome==Outcome.OutsideGrid,"candidate signed cell is bounded before coordinate commit");
            Check(rejected.Position==row.Start,"out-of-grid step preserves BOTH old coordinates");
        }
        var inside=NativeGuestMotion.AdvanceCoordinates(new(8190,128),new(8256,128),15,0x4000,33,128);
        Check(inside.Position==new Point(8205,128) && inside.Outcome==Outcome.Moving,
            "same coordinate advances when actual map has that column");
        var current=start;int steps=0;
        for(;steps<8;steps++)
        {
            var moved=NativeGuestMotion.AdvanceCoordinates(current,target,15,0x4000,128,128);
            current=moved.Position;
            if(moved.Outcome==Outcome.AtWaypoint) { steps++;break; }
        }
        Check(steps==5 && current==target,"64-unit spacing reaches waypoint in four15-unit steps plus clamped4");
    }
}