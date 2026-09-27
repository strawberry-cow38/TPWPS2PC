namespace TPW.PS2.Data;

/// <summary>⚠⚠ THE PORT'S STAND-IN FOR THE NATIVE RUNTIME TILE -- AN ADAPTER FROM END TO END.
///
/// Natively every cell is an 8-byte record at `[0x3952EC] + (z*[0x3952F0] + x)*8` (`0x14E138`):
/// byte `+0` is the KIND, `+2` the direction-LINK byte, `+7` a PROPERTY byte
/// (findings/native-route-planner.md, findings/paths.md). The staff code reads all three: the route
/// planner's passable set per request flag, the local wander `0x1913B8`, the patrol's "is a path"
/// `0x1E6338` and the hire drop's `0x1E65B8`. This port keeps none of them -- <see cref="ParkPaths"/>
/// has None/Path/Queue plus the entrance cells -- so this class DERIVES the three bytes from what
/// the port does keep, and every derivation below says what it stands in for.
///
/// What maps cleanly (READ on both sides):
/// - laid path → kind **2**; laid queue → kind **4** (native-ride-queue.md);
/// - the entrance walkway → kind **0x0C** with property 8, its mouth → kind **0x0E**, exactly as
///   `0x14E5B0` paints them (<see cref="ParkPaths.EntranceKind"/>);
/// - the JUNGLE bridge deck → kind 2 with property bits 0x80|0x02, which `0x14E5B0` sets on those
///   four cells (findings/paths.md). ⚠ Applied to any bridge deck the port recognises.
///
/// What is INFERRED or chosen, and labelled where it is used:
/// - ⚠ **Placed rides and features** → kind **5** over their footprint and kind **7** on their
///   inside entry cell. Kinds 5 and 7 are unnamed natively; the planner admits 5 with flag 0x08 or as
///   the request's target and 7 ONLY as the target, and the staff's rest and toilet requests target
///   a feature's entry cell (findings/staff-person.md §10 "rest (kind 5/7 targets)"), which is what
///   this reading reproduces. Kinds 8 and 10 have no port producer.
/// - ⚠ **Kind 13** (a queue laid onto a path, the only join between the two networks) has no
///   port producer: a cell is path OR queue material here.
/// - ⚠ **Everything else is ground, kind 0**, with property bit 2 ("no-build", tiles.md) set
///   wherever <see cref="ParkPaths.CanBuild"/> refuses -- the terrain's own no-build bit, fixed
///   scenery and the gate's hold all fold onto that one bit. Bits 0x01 and 0x10 are never set: what
///   they mean natively is not read.
/// - ⚠ **The link byte** (builder `0x1E70F0`, unread) is taken as ALL FOUR directions (0x55) on
///   every tile, which is the port's BFS's own rule. Natively a path tile links only to the
///   neighbours the link builder joined, so the planner's direction checks on kinds 2/4/7/13 and the
///   on-path wander's candidate filter are both looser here than on the console.
/// - ⚠ `0x1E61E0`, the open-ground test behind flag 0x02, is unread; it is taken as "no no-build
///   bit", by analogy with its sibling `0x1E63C0` (kind 0 without property 2), so a request that
///   may cross grass still cannot walk off the plot or through scenery.</summary>
public sealed class NativeTileView
{
    public const int KindNone = -1, KindGround = 0, KindPath = 2, KindNever = 3, KindQueue = 4,
                     KindBuilding = 5, KindBuildingEntry = 7, KindWalkway = 0x0C, KindQueueOnPath = 0x0D,
                     KindMouth = 0x0E;
    /// <summary>Property byte `+7` bits this view can produce.</summary>
    public const byte PropertyNoBuild = 0x02, PropertyWalkway = 0x08, PropertyBridge = 0x80;
    /// <summary>The link byte this view reports for every tile: all four direction bits of
    /// <see cref="StaffTables.DirectionBits"/> (N 0x01, E 0x04, S 0x10, W 0x40).</summary>
    public const byte AllLinks = 0x55;

    public ParkPaths Paths { get; }
    readonly Func<IEnumerable<ParkRide>> _placed;
    readonly Dictionary<ParkCell, int> _buildings = new();

    /// <param name="placed">Every placed ride and feature whose footprint should read as a
    /// building, normally <c>() =&gt; sim.Rides</c>. Snapshotted by <see cref="Refresh"/>.</param>
    public NativeTileView(ParkPaths paths, Func<IEnumerable<ParkRide>> placed)
    {
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _placed = placed ?? (() => Array.Empty<ParkRide>());
    }

    public int Width => Paths.Field.Width;
    public int Height => Paths.Field.Height;

    /// <summary>Re-read the placed footprints. <see cref="ParkStaff.Update"/> calls it once a tick,
    /// so the same tick sees one consistent set of buildings.</summary>
    public void Refresh()
    {
        _buildings.Clear();
        foreach (var ride in _placed())
        {
            if (ride == null) continue;
            for (int x = 0; x < Math.Max(1, ride.Width); x++)
                for (int z = 0; z < Math.Max(1, ride.Height); z++)
                    _buildings[ride.Origin.Offset(x, z)] = KindBuilding;
            // The inside entry: a shop's or lavatory's validated ServiceEntry, else a placed
            // feature's rotated connection A (0x1E1760, PlacedDestination.Entry).
            ParkCell? entry = ride.ServiceEntry
                ?? (ride.Definition?.CompiledEntry?.Kind == AssetResourceDatabase.AssetKind.Feature ? ride.DestinationEntry : null);
            if (entry is { } e && _buildings.ContainsKey(e)) _buildings[e] = KindBuildingEntry;
        }
    }

    /// <summary>`0x14E0F8`/`0x14E108` inline: 0 &lt;= x &lt; W and 0 &lt;= z &lt; H.</summary>
    public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Height;
    /// <summary>`0x149D20`: 0 &lt;= x &lt; W-1 and 0 &lt;= z &lt; H-1 -- ⚠ the LAST row and column are
    /// excluded, READ (findings/staff-person.md §4.6). The patrol, the wander's ring search and its
    /// map-centre fallback use this one; the walk step and the wander's crawl use the inline test.</summary>
    public bool InBoundsInner(int x, int z) => x >= 0 && z >= 0 && x < Width - 1 && z < Height - 1;

    /// <summary>The tile's kind byte `+0`, or <see cref="KindNone"/> off the map.</summary>
    public int Kind(int x, int z)
    {
        var c = new ParkCell(x, z);
        if (!InBounds(x, z)) return KindNone;
        if (Paths.EntranceKind(c) is int entrance) return entrance;
        if (_buildings.TryGetValue(c, out int building)) return building;
        return Paths.Kind(c) switch
        {
            ParkPathKind.Path => KindPath,
            ParkPathKind.Queue => KindQueue,
            _ => KindGround,
        };
    }
    public int Kind(ParkCell c) => Kind(c.X, c.Z);

    /// <summary>The property byte `+7` as this view can derive it (see the class note).</summary>
    public byte Properties(int x, int z)
    {
        var c = new ParkCell(x, z);
        return Kind(x, z) switch
        {
            KindPath => Paths.IsBridge(c) ? (byte)(PropertyBridge | PropertyNoBuild) : (byte)0,
            KindWalkway => PropertyWalkway,
            KindGround => Paths.CanBuild(c) ? (byte)0 : PropertyNoBuild,
            _ => 0,
        };
    }
    public byte Properties(ParkCell c) => Properties(c.X, c.Z);

    /// <summary>The link byte `+2`. ⚠ Always <see cref="AllLinks"/>: see the class note.</summary>
    public byte Links(int x, int z) => AllLinks;

    /// <summary>`0x1E6338`: kind 2, a path.</summary>
    public bool IsPath(int x, int z) => Kind(x, z) == KindPath;

    /// <summary>⭐ THE PLANNER'S PASSABLE SET, per request flag, from its jump table at `0x364690`
    /// over kinds 0..14 (18C928; findings/native-route-planner.md, staff-person.md §4.3):
    /// <code>
    ///   0x80 set: ONLY kinds 2, 12, 14 (restricted mode, no direction check)
    ///   kind 0  flag 0x02 (and 1E61E0)   kind 1  flag 0x40     kind 2   flag 0x01 (dir)
    ///   kind 4  flag 0x10 (dir)          kind 5  flag 0x08 or target
    ///   kind 7  target only (dir)        kind 8  always        kind 12  flag 0x01
    ///   kind 13 flag 0x01 (dir)          kind 14 flag 0x20     3, 6, 9..11, 15+: never
    /// </code>
    /// So 0x11 is paths + queues, 0x23 paths + open ground + the mouth, 0x03 paths + open ground.
    /// ⚠ Direction checks pass (links are all four, class note); costs are the BFS's, all 1.</summary>
    public static bool Admits(int kind, byte properties, int flags, bool target)
    {
        if ((flags & 0x80) != 0) return kind is KindPath or KindWalkway or KindMouth;
        return kind switch
        {
            KindGround => (flags & 0x02) != 0 && (properties & PropertyNoBuild) == 0, // ⚠ 1E61E0 as "no no-build bit"
            1 => (flags & 0x40) != 0,
            KindPath => (flags & 0x01) != 0,
            KindQueue => (flags & 0x10) != 0,
            KindBuilding => (flags & 0x08) != 0 || target,
            KindBuildingEntry => target,
            8 => true,
            KindWalkway => (flags & 0x01) != 0,
            KindQueueOnPath => (flags & 0x01) != 0,
            KindMouth => (flags & 0x20) != 0,
            _ => false,
        };
    }
    public bool Admits(ParkCell c, int flags, bool target) => Admits(Kind(c), Properties(c), flags, target);

    /// <summary>⭐ `0x1E65B8`, the hire tool's placement test (`0x128918`): the tile's kind is NOT
    /// 4, 5, 7, 8, 10 or 12, and its property byte has none of 0x01, 0x02, 0x10. So path and plain
    /// ground pass; queues, buildings, the walkway and no-build ground do not (findings/
    /// staff-management.md §2.2). ⚠ What the port CANNOT test: property bits 0x01 and 0x10 (never
    /// produced here); kinds 8 and 10 (no producer); and the mouth (kind 0x0E), which passes the kind
    /// test and whose `+7` byte is not read -- this view reports 0 for it, so it is accepted.</summary>
    public bool AcceptsStaffDrop(ParkCell c)
    {
        int kind = Kind(c);
        if (kind == KindNone) return false;
        if (kind is KindQueue or KindBuilding or KindBuildingEntry or 8 or 10 or KindWalkway) return false;
        return (Properties(c) & 0x13) == 0;
    }
}
