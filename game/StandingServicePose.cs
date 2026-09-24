using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>Authored standing point for the bounded 1x1 relief-service slice.
/// Uses Placement's row mirror and quarter turns; no guessed queue spacing.</summary>
public readonly record struct StandingServicePose(Vector2 CellPoint, ParkCell HeightCell, Vector2I Inward)
{
    public static bool TryCreate(RideDefinition definition, ParkCell origin, int turns, out StandingServicePose pose)
    {
        pose = default;
        if (definition?.ProvidesRelief != true || definition.Shape == null) return false;
        var fp = Park.Footprint.From(definition.Shape);
        if (fp.Width != 1 || fp.Height != 1 || fp.EntryX < 0 || fp.EntryY < 0) return false;
        var sx = definition.EntryStandX;
        var sy = definition.EntryStandY;
        if (sx is not float x || sy is not float y || !float.IsFinite(x) || !float.IsFinite(y)) return false;
        x += fp.EntryX; y = fp.Height - (fp.EntryY + y);
        int width = fp.Width, height = fp.Height;
        int ex = fp.EntryX, ey = fp.Height - 1 - fp.EntryY;
        int dx = fp.EntryDX, dy = -fp.EntryDY;
        for (int turn = 0; turn < (turns & 3); turn++)
        {
            (x, y) = (height - y, x);
            (ex, ey) = (height - 1 - ey, ex);
            (dx, dy) = (-dy, dx);
            (width, height) = (height, width);
        }
        pose = new(new Vector2(origin.X + x, origin.Z + y), origin.Offset(ex, ey), new Vector2I(-dx, -dy));
        return true;
    }
}
