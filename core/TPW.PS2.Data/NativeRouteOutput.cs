using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

/// <summary>18D358 output-node selection, applied to an explicit parent chain.
/// Using this on the port's BFS path does NOT establish native search/tie-break parity.</summary>
public static class NativeRouteOutput
{
    public static IReadOnlyList<Point> FromCells(IReadOnlyList<ParkCell> cells, Point requestedEndpoint)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var chain = cells.ToArray();
        if (chain.Length == 0) throw new ArgumentException("Successful native search output requires a goal node.", nameof(cells));
        var backwards = new List<Point>();
        int previous = 0xffff;
        for (int i = chain.Length - 1; i >= 0; i--)
        {
            // Native search-node coordinates are bytes, not arbitrary signed world cells.
            // Root's synthetic direction0 is significant; do NOT unconditionally skip start.
            int direction = 0;
            if (i > 0)
            {
                int dx = unchecked((byte)chain[i - 1].X) - unchecked((byte)chain[i].X);
                int dz = unchecked((byte)chain[i - 1].Z) - unchecked((byte)chain[i].Z);
                direction = dx != 0 ? dx > 0 ? 6 : 2 : dz > 0 ? 0 : 4;
            }
            if (direction == previous) continue;
            var point = backwards.Count == 0 ? requestedEndpoint : new Point(
                unchecked((short)((chain[i].X << 8) | 0x80)), unchecked((short)((chain[i].Z << 8) | 0x80)));
            backwards.Add(NativeGuestMotion.DecodeTarget(NativeGuestMotion.EncodeTarget(0, point)));
            previous = direction;
        }
        backwards.Reverse(); // allocator then prepends endpoint-first, preserving native handle order
        return backwards.AsReadOnly();
    }
}
