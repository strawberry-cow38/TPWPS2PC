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
        Def = def; Display = display; Id = id; Base = fp; Turns = 0;
        Turned = fp;
    }

    public void Clear() { Def = null; Display = null; Turned = default; Turns = 0; }

    /// <summary>A quarter turn, instantly. ⚠ The console only ever turns ONE WAY (+1); there is no
    /// anticlockwise button, and adding one would be a control the game does not have.</summary>
    public void Turn()
    {
        if (!Active) return;
        Turns = (Turns + 1) & 3;
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
    /// ⚠ Found by stepping off the footprint, NOT from the compass letter the exit carries. That
    /// letter is read -- N, S, E or W -- but what it means is not established: `b_drip` puts its N
    /// beside the entrance on the last row while `acorn` puts an S on the FIRST row, which no
    /// single reading of "faces north" fits. Stepping out of the shape needs no such reading.</summary>
    public (int X, int Y)? OutsideOf((int X, int Y) door, int cursorX, int cursorY)
    {
        if (!Active) return null;
        var (cx, cy) = CornerFor(cursorX, cursorY);
        int fx = door.X - cx, fy = door.Y - cy;
        foreach (var (dx, dy) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
        {
            int nx = fx + dx, ny = fy + dy;
            bool inside = nx >= 0 && ny >= 0 && nx < Turned.Width && ny < Turned.Height
                       && Turned.Cells[nx, ny];
            if (!inside) return (cx + nx, cy + ny);
        }
        return null;
    }

    /// <summary>Turn a footprint a quarter at a time. ⭐ The ENTRY turns with it -- a ride rotated
    /// with its door left where it was would have visitors walking into a wall.</summary>
    static Park.Footprint Rotate(Park.Footprint fp, int turns)
    {
        var cells = fp.Cells; int w = fp.Width, h = fp.Height;
        int ex = fp.EntryX, ey = fp.EntryY;
        int xx = fp.ExitX, xy = fp.ExitY;
        for (int t = 0; t < (turns & 3); t++)
        {
            var next = new bool[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    next[h - 1 - y, x] = cells[x, y];
            if (ex >= 0 && ey >= 0) (ex, ey) = (h - 1 - ey, ex);
            if (xx >= 0 && xy >= 0) (xx, xy) = (h - 1 - xy, xx);
            cells = next; (w, h) = (h, w);
        }
        return new Park.Footprint(w, h, cells, ex, ey, xx, xy);
    }
}
