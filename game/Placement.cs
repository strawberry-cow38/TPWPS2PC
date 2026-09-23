using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>Something picked out of the build menu and held over the park, before it is put down.
///
/// ⭐⭐ THE CORNER COMES FROM THE CURSOR, NOT THE OTHER WAY ROUND. The console's placement tools
/// all share one move: every frame the footprint's corner becomes `cursor - (w >> 1, d >> 1)` of
/// the TURNED footprint (PSX 0x8001C454), so what follows the pointer is the middle of the thing
/// and it stays centred as it turns. Turning is a quarter at a time and instant -- no ease
/// (0x8001C6BC / 0x8001C750).
///
/// ⭐ And the press is gated the same way the path tool's is: it goes down only if NO tile of it
/// refuses. That is why the ghost is per-tile rather than one badge on the whole shape.</summary>
public sealed class Placement
{
    public RideDefinition Def { get; private set; }
    public string Display { get; private set; }
    public int Id { get; private set; }
    public Park.Footprint Base { get; private set; }
    public int Turns { get; private set; }

    /// <summary>⭐⭐ ONLY A RIDE HAS A QUEUE. Master: "only rides get the queues. sideshows, shops,
    /// etc have a combined entry/exit node on one tile". So this is the BUILD CATEGORY -- the
    /// archive folder the thing came out of -- and not a guess read off its .sam. The stand-in it
    /// replaces ("declares a RideTypeStringIndex and has an entrance") gave a queue to anything
    /// with a door, which is most of the shops.</summary>
    public bool IsRide { get; private set; }

    /// <summary>What kind of laid ground is on a cell. ⚠ A DELEGATE: the blueprint has to know
    /// whether a stub would land on path or on queue, and the path tool is built later and per
    /// park -- holding it would freeze a null, which this file's neighbours have done before.</summary>
    public Func<int, int, PathTool.Kind> GroundAt { get; set; }

    public bool Active => Def != null;

    /// <summary>The footprint as it currently stands, turned.</summary>
    public Park.Footprint Turned { get; private set; }

    public void Arm(RideDefinition def, string display, int id, Park.Footprint fp, bool isRide = false)
    {
        IsRide = isRide;
        // ⚠⚠ A MIRROR, NOT A HALF TURN. The shape is authored in the game's own frame and the plot
        // draws in one that is mirrored in Z ONLY -- grid +x is world +X, but grid +y is world -Z,
        // because the scene root carries Scale(1,1,-1). A reflection in one axis is not a rotation,
        // and correcting it with a half turn leaves the shape reflected in the OTHER axis:
        // R180 = flipX . flipY, so applying R180 where flipY was wanted is flipX left over. Master,
        // looking at what went down: first "the placement ghost is 180 degrees rotated", then --
        // after the half turn -- "ride entry/exit is mirrored horizontally". Those two readings
        // together name flipY exactly, and the transform of the plot says the same thing
        // independently, which is why it is this and not another guess at a quarter turn.
        //
        // Correcting it HERE means the cells, both doors and the exit's facing all come along;
        // correcting it in the drawing would leave the doors where they were.
        Def = def; Display = display; Id = id; Base = MirrorRows(fp); Turns = 0;
        Turned = Base;
    }

    /// <summary>Flip a footprint top to bottom: row y becomes row h-1-y. ⭐ The doors come with it
    /// and the exit's FACING flips in y with them -- a facing left alone would point back into the
    /// ride from a door that had moved to the other side.</summary>
    static Park.Footprint MirrorRows(Park.Footprint fp)
    {
        int w = fp.Width, h = fp.Height;
        if (w <= 0 || h <= 0) return fp;
        var cells = new bool[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                cells[x, h - 1 - y] = fp.Cells[x, y];
        int ey = fp.EntryY >= 0 ? h - 1 - fp.EntryY : -1;
        int xy = fp.ExitY >= 0 ? h - 1 - fp.ExitY : -1;
        return new Park.Footprint(w, h, cells, fp.EntryX, ey, fp.ExitX, xy, fp.ExitDX, -fp.ExitDY,
                                  fp.EntryDX, -fp.EntryDY);
    }

    public void Clear() { Def = null; Display = null; Turned = default; Turns = 0; IsRide = false; }

    /// <summary>A quarter turn, instantly. ⚠ The console only ever turns ONE WAY -- there is no
    /// anticlockwise button on it -- but master asked for both, and a port on a keyboard is not
    /// bound to a pad's button count. Clockwise is +1.</summary>
    public void Turn(int quarters = 1)
    {
        if (!Active) return;
        Turns = (Turns + quarters) & 3;
        Turned = Rotate(Base, Turns);
    }

    /// <summary>Where the shape's corner sits for a cursor on this cell.</summary>
    public (int X, int Y) CornerFor(int cursorX, int cursorY)
        => (cursorX - (Turned.Width >> 1), cursorY - (Turned.Height >> 1));

    /// <summary>Every cell the shape covers from that corner, with whether the park will take it.</summary>
    public IEnumerable<(int X, int Y, bool Ok)> Cells(Park park, int cursorX, int cursorY)
    {
        var (cx, cy) = CornerFor(cursorX, cursorY);
        for (int fy = 0; fy < Turned.Height; fy++)
            for (int fx = 0; fx < Turned.Width; fx++)
            {
                if (!Turned.Cells[fx, fy]) continue;
                int x = cx + fx, y = cy + fy;
                // ⭐⭐ THE BODY MAY NOT STAND ON LAID GROUND. Master: "dont allow placing the entire
                // shop/sideshow/feature over paths. just the path tile is valid." Only the door's
                // STUB may overlap, because a stub and the path it lands on are the same ground;
                // the thing itself would be standing in the middle of a walkway.
                //
                // ⚠ Rides too, though master was looking at shops. It is the same invariant they
                // gave from the other side -- "paths should be invalid on tiles occupied by rides,
                // they shouldnt delete the rides" -- and a rule that held one way round and not
                // the other would just be the same bug wearing the other hat.
                yield return (x, y, park.IsPlayable(x, y) && park.Vacant(x, y)
                                 && (GroundAt?.Invoke(x, y) ?? PathTool.Kind.None) == PathTool.Kind.None);
            }
    }

    /// <summary>The two tiles that stick OUT of the shape -- the one outside the entrance and the
    /// one outside the exit -- with whether the park will take them.
    ///
    /// ⭐⭐ THEY ARE PART OF THE THING. Master: "the entrance/exit tiles that stick out should
    /// always be created with the ride, and they should be part of the footprint when being
    /// placed". So the press is gated on them as much as on the body: a ride whose door opens onto
    /// the sea or onto another ride's wall is a ride nobody can reach, and it must refuse BEFORE
    /// it goes down rather than leave a stub that cannot be laid afterwards.
    ///
    /// ⚠ The entrance's is a QUEUE tile and the exit's is a PATH tile -- they are different kinds
    /// of ground, which is why they come back labelled rather than as a bare pair.</summary>
    public IEnumerable<(int X, int Y, bool Entrance, bool Queue, bool Ok)> Stubs(Park park, int cursorX, int cursorY)
    {
        foreach (var (door, entrance) in new[] { (DoorFor(cursorX, cursorY), true), (ExitFor(cursorX, cursorY), false) })
        {
            if (door is not { } d) continue;
            if (OutsideOf(d, cursorX, cursorY) is not { } o) continue;
            // ⭐ A QUEUE ONLY OUTSIDE A RIDE'S ENTRANCE. Everything else -- a shop's counter, a
            // sideshow's stall -- is a combined node on one tile, and the ground outside it is
            // ordinary path that people walk both ways over.
            bool queue = IsRide && entrance;
            var had = GroundAt?.Invoke(o.X, o.Y) ?? PathTool.Kind.None;
            // ⭐⭐ A PATH STUB MAY LAND ON PATH. Master: "allow overlapping that point over other
            // paths. (but not queues)". A shop's one node is a path tile and a path tile is what
            // is already there, so the two are the same ground and the stub simply joins it --
            // refusing would mean a shop could never be set down against a path anybody had laid,
            // which is where shops go.
            //
            // ⚠ NEVER ON A QUEUE, and a QUEUE stub never on either. A queue laid onto path is the
            // one cell that is BOTH, and that junction belongs to somebody running a queue INTO a
            // path on purpose -- made by a placement it would appear without anyone asking.
            bool free = had == PathTool.Kind.None
                     || (!queue && had == PathTool.Kind.Path);
            yield return (o.X, o.Y, entrance, queue,
                          park.IsPlayable(o.X, o.Y) && park.Vacant(o.X, o.Y) && free);
        }
    }

    /// <summary>Whether every tile of it -- body and stubs -- will be taken.</summary>
    public bool Fits(Park park, int cursorX, int cursorY)
        => Cells(park, cursorX, cursorY).All(c => c.Ok) && Stubs(park, cursorX, cursorY).All(s => s.Ok);

    /// <summary>The entrance tile, turned with the shape. ⚠ Null when the shape does not declare
    /// one: most scenery has no door, and drawing an entrance marker on a tree would be inventing
    /// one.</summary>
    public (int X, int Y)? DoorFor(int cursorX, int cursorY)
    {
        if (!Active || Turned.EntryX < 0 || Turned.EntryY < 0) return null;
        var (cx, cy) = CornerFor(cursorX, cursorY);
        return (cx + Turned.EntryX, cy + Turned.EntryY);
    }

    /// <summary>The exit tile, the one the shape marks with a compass letter. ⚠ Rarer than the
    /// entrance -- 86 of them against 171 entrances -- so most things have none and none is drawn.</summary>
    public (int X, int Y)? ExitFor(int cursorX, int cursorY)
    {
        if (!Active || Turned.ExitX < 0 || Turned.ExitY < 0) return null;
        var (cx, cy) = CornerFor(cursorX, cursorY);
        return (cx + Turned.ExitX, cy + Turned.ExitY);
    }

    /// <summary>The tile just OUTSIDE a door -- where a queue begins and where a path from the
    /// exit begins, which is what the console starts its runs on.
    ///
    /// ⭐⭐ THE EXIT USES ITS OWN FACING, out of the shape string. All 86 compass cells on the disc
    /// point out of their footprint under `N` = +y, `E` = +x, with no exceptions, so the letter
    /// says which tile without any stepping about. ⚠ I had this as unusable on one test of four.
    ///
    /// ⚠ THE ENTRANCE HAS NO LETTER, so it is still found by stepping off the shape -- and that is
    /// a coin toss for some of them: 137 entrances on the disc have exactly one free side, but 34
    /// have more than one. The order below is the tie-break, and it is a CHOICE, not a reading.</summary>
    public (int X, int Y)? OutsideOf((int X, int Y) door, int cursorX, int cursorY)
    {
        if (!Active) return null;
        var (cx, cy) = CornerFor(cursorX, cursorY);
        int fx = door.X - cx, fy = door.Y - cy;

        bool Inside(int nx, int ny) => nx >= 0 && ny >= 0 && nx < Turned.Width && ny < Turned.Height
                                    && Turned.Cells[nx, ny];

        // The exit knows which way it faces; take it and nothing else.
        if (fx == Turned.ExitX && fy == Turned.ExitY && (Turned.ExitDX != 0 || Turned.ExitDY != 0))
        {
            int nx = fx + Turned.ExitDX, ny = fy + Turned.ExitDY;
            return Inside(nx, ny) ? null : (cx + nx, cy + ny);
        }
        // ⭐⭐ AND SO DOES THE ENTRANCE, NOW. Its facing was worked out once in the authored frame
        // and has turned with the shape ever since, so it no longer re-answers in fixed grid
        // directions every time the thing is rotated.
        if (fx == Turned.EntryX && fy == Turned.EntryY && (Turned.EntryDX != 0 || Turned.EntryDY != 0))
        {
            int nx = fx + Turned.EntryDX, ny = fy + Turned.EntryDY;
            return Inside(nx, ny) ? null : (cx + nx, cy + ny);
        }

        foreach (var (dx, dy) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
            if (!Inside(fx + dx, fy + dy)) return (cx + fx + dx, cy + fy + dy);
        return null;
    }

    /// <summary>Which way a door's marker arrow points: from the tile OUTSIDE the door back at
    /// the door itself.
    ///
    /// ⭐⭐ BOTH ARROWS POINT AT THE RIDE. Read off the console's own art: `168.tga` is a green
    /// arrow hugging the tile's top edge pointing out through it, `169.tga` an orange one at the
    /// same edge pointing back in. So the marked tile is the one outside, its top edge is the
    /// edge it shares with the ride, and green says "in here" while orange says "out of here" --
    /// one facing serves both, and the only thing that changes between them is the colour.</summary>
    public static (int Dx, int Dy) FacingOf((int X, int Y) door, (int X, int Y) outside)
        => (door.X - outside.X, door.Y - outside.Y);

    /// <summary>Turn a footprint a quarter at a time. ⭐ The ENTRY turns with it -- a ride rotated
    /// with its door left where it was would have visitors walking into a wall.</summary>
    static Park.Footprint Rotate(Park.Footprint fp, int turns)
    {
        var cells = fp.Cells; int w = fp.Width, h = fp.Height;
        int ex = fp.EntryX, ey = fp.EntryY;
        int xx = fp.ExitX, xy = fp.ExitY;
        int xdx = fp.ExitDX, xdy = fp.ExitDY;
        int edx = fp.EntryDX, edy = fp.EntryDY;
        for (int t = 0; t < (turns & 3); t++)
        {
            var next = new bool[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    next[h - 1 - y, x] = cells[x, y];
            if (ex >= 0 && ey >= 0) (ex, ey) = (h - 1 - ey, ex);
            if (xx >= 0 && xy >= 0) (xx, xy) = (h - 1 - xy, xx);
            // ⭐ The FACING turns with the cell, by the same map: a point (x,y) goes to (h-1-y, x),
            // so a direction (dx,dy) goes to (-dy, dx). A door whose tile moved and whose facing
            // did not would point into the ride.
            (xdx, xdy) = (-xdy, xdx);
            // ⭐ AND THE ENTRANCE'S FACING TURNS BY THE SAME MAP. It is the whole of the shop bug:
            // a door whose tile moved and whose facing did not opens onto a side that is no longer
            // there.
            (edx, edy) = (-edy, edx);
            cells = next; (w, h) = (h, w);
        }
        return new Park.Footprint(w, h, cells, ex, ey, xx, xy, xdx, xdy, edx, edy);
    }
}
