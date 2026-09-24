using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>Authored standing point for 1x1 relief facilities and 2x2 shops.
/// Missing-coordinate shops may separately opt into a labelled actual-arrival-stub policy.
/// Larger/script-owned service paths and authored coordinates are never guessed here.
/// Uses Placement's row mirror and quarter turns; no guessed queue spacing.</summary>
public readonly record struct StandingServicePose(Vector2 CellPoint, ParkCell HeightCell, Vector2I Inward)
{
    /// <summary>Port policy: hold at the coordinator's actual arrival cell, not an authored
    /// counter position. The nullable SAM fields are deliberately left untouched.</summary>
    public bool IsEntryStubFallback { get; init; }

    /// <summary>Only a small selling shop with BOTH stand keys absent can use this fallback.
    /// Partial/malformed supplied data is not absence, and larger service modes stay separate.
    /// The point comes from real placement; it is not a decoded console parser default.</summary>
    public static bool TryCreateAtEntryStub(RideDefinition definition, int turns, ParkCell? entrance,
                                            out StandingServicePose pose)
    {
        pose = default;
        if (definition?.Shape == null || !definition.Sells || entrance is not ParkCell cell
            || definition.Fields.ContainsKey("UsageInfo.EntryCellStandPosX")
            || definition.Fields.ContainsKey("UsageInfo.EntryCellStandPosY")
            || definition.Blocks.ContainsKey("UsageInfo.EntryCellStandPosX")
            || definition.Blocks.ContainsKey("UsageInfo.EntryCellStandPosY")) return false;
        var fp = Park.Footprint.From(definition.Shape);
        if (fp.Width != 2 || fp.Height != 2 || fp.EntryX < 0 || fp.EntryY < 0) return false;
        int dx = fp.EntryDX, dy = -fp.EntryDY;
        for (int turn = 0; turn < (turns & 3); turn++) (dx, dy) = (-dy, dx);
        pose = new StandingServicePose(new Vector2(cell.X + .5f, cell.Z + .5f), cell, new Vector2I(-dx, -dy))
            { IsEntryStubFallback = true };
        return true;
    }

    public static bool TryCreate(RideDefinition definition, ParkCell origin, int turns, out StandingServicePose pose)
    {
        pose = default;
        if (definition?.Shape == null) return false;
        var fp = Park.Footprint.From(definition.Shape);
        bool smallRelief = definition.ProvidesRelief && fp.Width == 1 && fp.Height == 1;
        bool smallShop = definition.Sells && fp.Width == 2 && fp.Height == 2;
        if ((!smallRelief && !smallShop) || fp.EntryX < 0 || fp.EntryY < 0) return false;
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
