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

    public bool Active => Def != null;

    /// <summary>The footprint as it currently stands, turned.</summary>
    public Park.Footprint Turned { get; private set; }

    public void Arm(RideDefinition def, string display, int id, Park.Footprint fp)
    {
        // ⚠⚠ HALF A TURN, ALWAYS. The shape is authored in the game's own frame and the plot draws
        // in one mirrored on both axes -- the root mirrors Z and the rows are laid out reversed --
        // so a footprint taken straight from the .sam comes out back to front and upside down.
        // Master, looking at it: "the placement ghost is 180 degrees rotated from what it should
        // be". Correcting it HERE means the cells, both doors and the exit's facing all come along;
        // correcting it in the drawing would leave the doors where they were.
        Def = def; Display = display; Id = id; Base = Rotate(fp, 2); Turns = 0;
        Turned = Base;
    }

    public void Clear() { Def = null; Display = null; Turned = default; Turns = 0; }

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
                yield return (x, y, park.IsPlayable(x, y) && park.Vacant(x, y));
            }
    }

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

        foreach (var (dx, dy) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
            if (!Inside(fx + dx, fy + dy)) return (cx + fx + dx, cy + fy + dy);
        return null;
    }

    /// <summary>Turn a footprint a quarter at a time. ⭐ The ENTRY turns with it -- a ride rotated
    /// with its door left where it was would have visitors walking into a wall.</summary>
    static Park.Footprint Rotate(Park.Footprint fp, int turns)
    {
        var cells = fp.Cells; int w = fp.Width, h = fp.Height;
        int ex = fp.EntryX, ey = fp.EntryY;
        int xx = fp.ExitX, xy = fp.ExitY;
        int xdx = fp.ExitDX, xdy = fp.ExitDY;
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
            cells = next; (w, h) = (h, w);
        }
        return new Park.Footprint(w, h, cells, ex, ey, xx, xy, xdx, xdy);
    }
}
