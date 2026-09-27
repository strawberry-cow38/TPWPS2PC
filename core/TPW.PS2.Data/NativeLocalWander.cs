using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

/// <summary>⭐⭐ `0x1913B8`, THE PERSON'S LOCAL WANDER (execution state 5) -- shared natively by
/// guests and staff (person `vt+0x15C`), ported here for the staff first; the guests' port does not
/// have it yet (`ParkVisitors` uses its own walk policy for arm 1). READ from the decompile and
/// MIPS, findings/staff-person.md §4.6. The tile bytes it reads come from <see cref="NativeTileView"/>,
/// which is an adapter; the control flow and every draw below are the native one.
///
/// Let `c` be the current cell and `t` its tile.
///
/// **A. On a path-like tile** (kind 2, 7, 8 or 13: `0x1E6338/0x1E6380/0x1E6390/0x1E6370`):
/// `n = rand(10)` steps. Each step's candidates are the inline-in-bounds neighbours of kind 2 or 13
/// whose direction bit (`0x364818`) is set in the CURRENT tile's link byte `+2`. None: stop. If the
/// previous direction is a candidate it is kept unless `rand(100) &lt; 20`; otherwise (or in that
/// 20 %) `prev = rand(4)` if unset, then a weighted draw over the candidates with
/// `0x364A38[prev][d]` (100 straight, 40 turn, 10 back): `r = rand(sum)`, and the loop at
/// `0x191B60` takes the first candidate whose RUNNING SUM is `&gt;= r` -- so the first candidate owns
/// `w0 + 1` of the sum, READ. One cell per step.
///
/// **B. Off the path:**
/// 1. Ring search, `r = 0 .. rand(5)+3`, each not-yet-blocked direction, cell `c + r*d` inside
///    `0x149D20`: a path → a ONE-slot route straight to its centre (old chain freed first; no slot →
///    state 0). Else kind 5, property 2, or non-path with property 0x10 blocks that direction.
///    `r = 0` is the standing cell, so standing on such a tile blocks all four.
/// 2. Map-centre fallback, 5 tries: `(W/2 + rand(20) - 10, W/2 + rand(20) - 10)` -- ⚠ BOTH halves
///    use **W** (`0x14E0F8` called twice at `0x1915C0/0x1915CC`), a native slip reproduced on purpose.
///    In `0x149D20`, a path, and admitted by a route request with flags **0x03** → the caller waits
///    in 0xB with mode 1 and flag 0x20.
/// 3. Random crawl, `n = rand(10)` steps over inline-in-bounds neighbours that are not kind 5, not
///    property 2, and either a path or without property 0x10. Keep the previous direction if it is a
///    candidate, else a uniform pick -- and the first step draws a `rand(4)` it then overwrites: one
///    wasted draw, READ. A step with no candidates does not move.
///
/// **Chain build (A and B.3):** free the old chain, then allocate from the LAST waypoint backwards,
/// linking forward. ⚠ On an allocation failure it STOPS and keeps the tail it has, so the head is a
/// later waypoint (native `0x192768` returning -1 breaks the loop); this is why the port does not
/// use <see cref="NativeRoutePool.TryBuild"/>, which rolls the whole list back. ONE SLOT PER CELL,
/// not collapsed by direction -- so the per-waypoint tiredness counts per cell here.
/// n = 0 builds nothing: head -1, and the caller's state 3 → 2 → arrival follows.</summary>
public static class NativeLocalWander
{
    public enum Outcome
    {
        /// <summary>B.1: a one-slot route to a path found by the ring search.</summary>
        DirectToPath,
        /// <summary>B.1 found a path but `0x192768` had no slot: the old chain is freed and the
        /// caller goes to state 0.</summary>
        DirectSlotExhausted,
        /// <summary>B.2: a flags-0x03 route request was admitted; the caller waits in 0xB.</summary>
        MapCentreRequested,
        /// <summary>A or B.3: a chain of one slot per cell (possibly empty, head -1).</summary>
        Chain,
    }

    public readonly record struct Result(Outcome Outcome, int Head, int Waypoints);

    /// <param name="request">`0x18DA78` with flags 0x03 and the given exact target; true when a
    /// request record was admitted. Called only by the map-centre fallback.</param>
    /// <param name="freeRoute">The caller's `0x192840(C+0x28)`, invoked exactly where the native
    /// frees the old chain (B.1 before its allocation, and before a chain build).</param>
    public static Result Run(NativeTileView tiles, NativeRoutePool pool, Point position,
                             Func<int, int> rand, Func<Point, bool> request, Action freeRoute)
    {
        ArgumentNullException.ThrowIfNull(tiles); ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(rand); ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freeRoute);
        int cx = position.X >> 8, cz = position.Z >> 8;
        var dirs = StaffTables.Directions;
        var waypoints = new List<Point>(10);                 // the scratch array at 0x396270

        int kind = tiles.Kind(cx, cz);
        bool pathLike = kind is NativeTileView.KindPath or NativeTileView.KindBuildingEntry or 8
                        or NativeTileView.KindQueueOnPath;
        if (pathLike)
        {
            // A. On a path-like tile.
            int prev = 0xffff;
            int steps = rand(10);
            var candidates = new int[4];
            for (int s = 0; s < steps; s++)
            {
                byte link = tiles.Links(cx, cz);
                int count = 0; bool keep = false;
                for (int d = 0; d < 4; d++)
                {
                    int x = cx + dirs[d].Dx, z = cz + dirs[d].Dz;
                    if (!tiles.InBounds(x, z)) continue;
                    int k = tiles.Kind(x, z);
                    if (k is not (NativeTileView.KindPath or NativeTileView.KindQueueOnPath)) continue;
                    if ((StaffTables.DirectionBits[d] & link) == 0) continue;
                    candidates[count++] = d;
                    if (prev == d) keep = true;
                }
                if (count == 0) break;
                // `!keep || rand(100) < 20`: the draw happens only when prev IS a candidate.
                if (!keep || rand(100) < 20)
                {
                    if (prev == 0xffff) prev = rand(4);
                    int sum = 0;
                    for (int i = 0; i < count; i++) sum += StaffTables.WanderWeights[prev, candidates[i]];
                    int r = rand(sum);
                    int pick = 0;
                    for (int acc = StaffTables.WanderWeights[prev, candidates[0]]; acc < r;
                         acc += StaffTables.WanderWeights[prev, candidates[pick]])
                        pick++;
                    prev = candidates[pick];
                }
                cx += dirs[prev].Dx; cz += dirs[prev].Dz;
                waypoints.Add(Centre(cx, cz));
            }
            return BuildChain(pool, waypoints, freeRoute);
        }

        // B.1 Ring search.
        var blocked = new bool[4];
        int rings = rand(5) + 4;
        for (int r = 0; r < rings; r++)
            for (int d = 0; d < 4; d++)
            {
                int x = cx + r * dirs[d].Dx, z = cz + r * dirs[d].Dz;
                if (!tiles.InBoundsInner(x, z) || blocked[d]) continue;
                int k = tiles.Kind(x, z);
                if (k == NativeTileView.KindPath)
                {
                    freeRoute();                                   // "way point not freed" debug print path
                    int slot = pool.Allocate();                    // 0x192768
                    if (slot == -1) return new Result(Outcome.DirectSlotExhausted, -1, 0);
                    pool.SetTarget(slot, Centre(x, z));            // 0x192560; Allocate left the link terminal
                    return new Result(Outcome.DirectToPath, slot, 1);
                }
                byte p = tiles.Properties(x, z);
                if (k == NativeTileView.KindBuilding || (p & 0x02) != 0 || (p & 0x10) != 0)
                    blocked[d] = true;
            }

        // B.2 Map-centre fallback, five tries. ⚠ Both coordinates from W (native slip, READ).
        int w = tiles.Width;
        for (int tries = 0; tries < 5; tries++)
        {
            int x = (w >> 1) + rand(20) - 10;
            int z = (w >> 1) + rand(20) - 10;
            if (tiles.InBoundsInner(x, z) && tiles.IsPath(x, z) && request(Centre(x, z)))
                return new Result(Outcome.MapCentreRequested, -1, 0);
        }

        // B.3 Random crawl.
        int previous = 0xffff;
        int crawl = rand(10);
        var cand = new int[4];
        for (int s = 1; s <= crawl; s++)
        {
            int count = 0; bool keep = false;
            for (int d = 0; d < 4; d++)
            {
                int x = cx + dirs[d].Dx, z = cz + dirs[d].Dz;
                if (!tiles.InBounds(x, z)) continue;
                int k = tiles.Kind(x, z); byte p = tiles.Properties(x, z);
                if (k == NativeTileView.KindBuilding || (p & 0x02) != 0) continue;
                if (k != NativeTileView.KindPath && (p & 0x10) != 0) continue;
                cand[count++] = d;
                if (previous == d) keep = true;
            }
            if (count == 0) continue;                              // no move; later steps find the same nothing
            if (previous == 0xffff) previous = rand(4);            // the wasted draw
            if (!keep) previous = cand[rand(count)];
            cx += dirs[previous].Dx; cz += dirs[previous].Dz;
            waypoints.Add(Centre(cx, cz));
        }
        return BuildChain(pool, waypoints, freeRoute);
    }

    static Point Centre(int x, int z) => new(unchecked((short)(x * 256 + 0x80)), unchecked((short)(z * 256 + 0x80)));

    /// <summary>The native chain build after both walks (`0x191C6C..0x191D40`).</summary>
    static Result BuildChain(NativeRoutePool pool, List<Point> waypoints, Action freeRoute)
    {
        freeRoute();
        int head = -1, built = 0;
        for (int i = waypoints.Count - 1; i >= 0; i--)
        {
            int slot = pool.Allocate();
            if (slot == -1) break;                                 // keep the tail already built
            pool.SetTarget(slot, waypoints[i]);
            pool.SetNext(slot, head);
            head = slot; built++;
        }
        return new Result(Outcome.Chain, head, built);
    }
}
