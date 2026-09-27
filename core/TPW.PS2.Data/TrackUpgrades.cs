namespace TPW.PS2.Data;

/// <summary>An add-on bought for a track ride: ride `+0x288A + 2i` = {chain index, kind | 0x10}
/// (0x202B18). <see cref="Index"/> is the chain position of the first straight it sits on, less one
/// while the loop was open when it was bought.</summary>
public readonly record struct TrackUpgrade(int Index, int Kind);

/// <summary>⭐ Which add-ons each park sells, and how each moves the cars.
///
/// THE CATALOGUE. Kind-8 purchases are indexed by position in a per-park list (the tool's
/// `0x12bc10(mgr, 8, idx)`, `0x12ad78`, `0x12b070`), and those lists are not on the disc: they are
/// built at boot by the static initialiser `0x158CF0`, which writes each world's struct at
/// `0x360850[world]` (bss `0x395488`, `0x395580`, `0x395678`, `0x395750`): list pointers at +0x50 /
/// +0x54 for the first and second park, counts at +0x5C / +0x60. Read by emulating that function
/// to its return (784 steps). The keys below are DBA record keys.
///
/// Two routes agree on every row: the piece table `0x1FD6C0` gives, for add-on kinds 0 and 1 of each
/// park, the kind-8 DBA selector (`+0x2c`) of MammTunn 11 / LavaJump 12, WaterTun 6, Ogre 9,
/// Chopper 12 / Firepit 11, BeeJump 9 / HoneyPot 8, Meteor 8 -- the same records, in the same order,
/// and the shape-12/13 mesh ids in `0x2ECAD0` are these keys too. FANTASY's first park carries a
/// one-entry list (key 62) with a count of 0, and SPACE's second park has no list at all
/// (`0x1FD6C0` returns null there too): those two parks sell no add-ons.</summary>
public static class TrackUpgrades
{
    static readonly uint[][] Lists =
    {
        new uint[] { 237, 236 },   // JUNGLE 1, Dino Karts: MammTunn, LavaJump
        new uint[] { 238 },        // JUNGLE 2, Splish Splash: WaterTun
        new uint[] { 167 },        // HALLOW 1, Ooze Crooz: Ogre
        new uint[] { 165, 166 },   // HALLOW 2, CryptKarts: Chopper, Firepit
        new uint[0],               // FANTASY 1, Taptastic Rapids: none
        new uint[] { 81, 82 },     // FANTASY 2, Bumble Buggies: BeeJump, HoneyPot
        new uint[] { 402 },        // SPACE 1, The Blobulator: Meteor
        new uint[0],               // SPACE 2, Space Racers: none
    };

    /// <summary>The DBA keys of the add-ons a park sells, in catalogue order: position = kind.</summary>
    public static IReadOnlyList<uint> ForPark(int world, int park) => Lists[(world & 3) * 2 + (park & 1)];

    /// <summary>The kind (catalogue position) of an add-on in a park, or −1 when the park does not sell it.</summary>
    public static int KindOf(int world, int park, uint key) => Array.IndexOf(Lists[(world & 3) * 2 + (park & 1)], key);

    // 0x2EDD60 + w·0x120 + p·0x60 + kind·0x20 + i·8: s16 x, y, z. Every z on the disc is 0, and so is
    // every entry of kind 2; x is 256 wherever the park has that kind, which centres the lanes at 512
    // in the 4-wide frame.
    static readonly short[,,] X =
    {
        { { 256, 256 }, { 256,   0 } },
        { { 256,   0 }, { 256, 256 } },
        { { 256,   0 }, { 256, 256 } },
        { { 256,   0 }, {   0,   0 } },
    };
    static readonly short[,,,] Y =
    {
        { { { 0, -128, -128, -128 }, { 0, 64, 64, 64 } },   { { 0, 0, 0, 0 },     { 0, 0, 0, 0 } } },
        { { { 0, 0, 0, 0 },          { 0, 0, 0, 0 } },       { { 0, 0, -64, -64 }, { 0, 128, 128, 128 } } },
        { { { 0, 0, 0, 0 },          { 0, 0, 0, 0 } },       { { 64, 64, 64, 64 }, { 0, 0, 0, 0 } } },
        { { { 0, 0, 0, 0 },          { 0, 0, 0, 0 } },       { { 0, 0, 0, 0 },     { 0, 0, 0, 0 } } },
    };

    /// <summary>Sample <paramref name="i"/>'s offset for an add-on of <paramref name="kind"/> (0 or 1;
    /// 2 is all zero): added to both lane points in x/z and to the height.</summary>
    public static (int X, int Y, int Z) Offset(int world, int park, int kind, int i)
        => kind is < 0 or > 1 ? (0, 0, 0) : (X[world & 3, park & 1, kind], Y[world & 3, park & 1, kind, i & 3], 0);
}
