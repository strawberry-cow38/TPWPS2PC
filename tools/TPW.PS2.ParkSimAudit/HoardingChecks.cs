using System.Numerics;
using TPW.PS2.Data;

/// <summary>⭐ THE RIDE HOARDING, CORE HALF (findings/ride-hoarding.md; RideHoarding.cs): every `.sam` on the
/// disc parsed through <see cref="HoardingGrid"/> and held to the research's census, the geometry built
/// for every block over its real model's root, and the state machine's timing, heights and texture rules.
/// The ride-service half -- a real breakdown raising it, the mechanic's repair lowering it, an upgrade, a
/// condemnation -- runs inside <see cref="MechanicChecks"/> on its fixture (HoardingService below).
///
/// ⚠ The census numbers are the research's own measurements (hoarding.md §3-§5, made with a scratch
/// emulator over this port's decode), not independent of the port's parser -- but this parser was written
/// from the MIPS, not from that emulator, so the two agreeing on 1940 edges is not one program twice.</summary>
static class HoardingChecks
{
    public static void Run(Disc disc, Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "hoarding: " + label);
        var exe = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        Executable(disc.Read(exe.Extent, exe.Size), Check);
        Census(disc, Check);
        State(Check);
    }

    // =============================================================================================
    // The constants, read back out of the executable rather than trusted from the transcription.

    static void Executable(byte[] elf, Action<bool, string> Check)
    {
        uint U32(int off) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(off, 4));
        int U16(int off) => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(off, 2));
        int At(uint va)
        {
            int ph = checked((int)U32(28));
            for (int i = 0; i < U16(44); i++)
            {
                int p = ph + i * U16(42);
                if (U32(p) == 1 && va >= U32(p + 8) && va < U32(p + 8) + U32(p + 16)) return checked((int)(U32(p + 4) + va - U32(p + 8)));
            }
            throw new InvalidDataException($"0x{va:x} is not file-backed");
        }
        uint Word(uint va) => U32(At(va));
        float Float(uint va) => BitConverter.Int32BitsToSingle(unchecked((int)Word(va)));
        // The table 0x2ac268: {u32 char, u32 value, u32 mask} x 16.
        var table = Enumerable.Range(0, 16).Select(i => ((char)Word(0x2ac268 + (uint)i * 12), Word(0x2ac26c + (uint)i * 12), (byte)Word(0x2ac270 + (uint)i * 12))).ToList();
        Check(table.Count == HoardingGrid.Characters.Count && table.All(t => t.Item2 == 0 && HoardingGrid.Characters.TryGetValue(t.Item1, out byte m) && m == t.Item3),
              $"table: 0x2ac268's 16 {{char, value, mask}} entries are HoardingGrid.Characters, value 0 in every one ({string.Join(" ", table.Select(t => $"{t.Item1}={t.Item3:X2}"))})");
        // 0x1f39b8: two `lui/ori` constants into $f0/$f1, then eight `swc1 $fN, off($a0)`.
        float Imm(uint lui, uint ori) => BitConverter.Int32BitsToSingle(unchecked((int)((Word(lui) & 0xFFFF) << 16 | (Word(ori) & 0xFFFF))));
        var reg = new[] { Imm(0x1f39b8, 0x1f39bc), Imm(0x1f39c4, 0x1f39c8) };
        var slots = new float[8];
        int stores = 0;
        for (uint va = 0x1f39d4; va <= 0x1f39f4; va += 4)
        {
            uint w = Word(va);
            if (w >> 26 != 0x39 || (w >> 21 & 31) != 4) continue;              // swc1 ft, off($a0)
            slots[(w & 0xFFFF) / 4] = reg[w >> 16 & 31]; stores++;
        }
        var o = HoardingGeometry.OneByOneOffsets;
        Check(stores == 8 && Enumerable.Range(0, 4).All(k => o[k].X == slots[2 * k] && o[k].Y == slots[2 * k + 1]),
              $"1x1 offsets: 0x1f39b8's eight stores are ({string.Join("), (", Enumerable.Range(0, 4).Select(k => $"{slots[2 * k]:0.0}, {slots[2 * k + 1]:0.0}"))}) "
              + "-- o2 is (+0.3, +0.3), the port's table");
        Check(Imm(0x1f5a5c, 0x1f5a60) == RideHoarding.RiseRate && Imm(0x1f5abc, 0x1f5ac0) == RideHoarding.DropRate
              && Imm(0x1f57c0, 0x1f57c4) == 0.8f && Imm(0x1f57d8, 0x1f57dc) == 0.8f && (Word(0x1f57f0) & 0xFFFF) == 0x3e80
              && (Word(0x1f57f8) & 0xFFFF) == 0x4080 && (Word(0x1f57b0) & 0xFFFF) == 0x3f80 && (Word(0x1f5804) & 0xFFFF) == 0x3f80,
              $"rates and shape: 0x1f5a5c loads {Imm(0x1f5a5c, 0x1f5a60)} and 0x1f5abc {Imm(0x1f5abc, 0x1f5ac0)} a second; 0x1f5750 builds 1.0, 0.8 (odd), 0.8, 0.25 and 4.0");
        double D(uint va) => BitConverter.Int64BitsToDouble((long)((ulong)Word(va + 4) << 32 | Word(va)));
        Check(Float(0x36a938) == float.MaxValue && D(0x36a940) == (double)0.001f && D(0x36a948) == (double)0.001f && D(0x36a950) == (double)(2 * MathF.PI),
              $"offset search and key: FLT_MAX start (0x36a938), the 0.001f thresholds widened to double (0x36a940, 0x36a948), 2 pi as a double of the float (0x36a950)");
        var ee = HoardingGeometry.Ee.AtanHi.Concat(HoardingGeometry.Ee.AtanLo).Concat(HoardingGeometry.Ee.AT).ToArray();
        Check(Enumerable.Range(0, ee.Length).All(i => Float(0x37ce78 + (uint)i * 4) == ee[i]),
              $"atanf: the {ee.Length} floats at 0x37ce78 (atanhi, atanlo, aT) are the key's newlib constants");
    }

    // =============================================================================================
    // The disc: every .sam, WAD by WAD, each block parsed, sized, counted and built.

    static readonly Dictionary<char, (int Chars, int Blocks)> NotesChars = new()
    {
        ['.'] = (737, 140), ['^'] = (195, 130), [']'] = (222, 132), ['_'] = (75, 49), ['['] = (220, 129),
        ['F'] = (159, 159), ['7'] = (159, 159), ['L'] = (146, 146), ['J'] = (144, 144), ['n'] = (4, 4),
        ['='] = (0, 0), ['H'] = (0, 0), ['C'] = (0, 0), ['U'] = (0, 0), ['3'] = (0, 0), ['O'] = (0, 0),
    };
    static readonly Dictionary<int, int> NotesPanels = new()
    { [3] = 4, [7] = 16, [8] = 2, [9] = 7, [10] = 20, [11] = 50, [12] = 4, [13] = 1, [14] = 27, [16] = 14, [18] = 18 };

    static void Census(Disc disc, Action<bool, string> Check)
    {
        int sams = 0, blocks = 0, parsed = 0, sizeMatch = 0, edges = 0, offPerimeter = 0, entranceGaps = 0, oneEntranceGap = 0;
        int resolved = 0, gated = 0, cornersNoSecond = 0, corners = 0, orderDiffers = 0, oneByOne = 0;
        var faults = new List<string>();
        var charCount = new Dictionary<char, (int Chars, int Blocks)>();
        var panelsPer = new Dictionary<int, int>();
        var lengths = new List<float>();
        var gateFaults = new List<string>();
        var differs = new List<string>();
        HoardingGeometry dripper = null;
        foreach (var file in disc.Files().Where(f => !f.IsDirectory && f.Path.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase)))
        {
            WadArchive wad;
            try { wad = new WadArchive(disc.Read(file.Extent, file.Size)); } catch { continue; }
            var cat = new RideCatalogue();
            cat.AddWad(wad, file.Path);
            foreach (var def in cat.All)
            {
                sams++;
                if (def.Hoarding is not { } rows) continue;
                blocks++;
                var grid = HoardingGrid.Parse(rows, out string fault);
                if (grid == null) { faults.Add($"{def.Source}: {fault}"); continue; }
                parsed++;
                foreach (var g in rows.SelectMany(r => r).Where(c => c != ' ').GroupBy(c => c))
                {
                    var had = charCount.GetValueOrDefault(g.Key);
                    charCount[g.Key] = (had.Chars + g.Count(), had.Blocks + 1);
                }
                // The Shape, through the same parser rules (spaces skipped, rows reversed); '.' is empty.
                var shape = (def.Shape ?? Array.Empty<string>()).Select(r => r.Replace(" ", "")).ToArray();
                int sw = shape.Length == 0 ? 0 : shape.Max(r => r.Length), sh = shape.Length;
                if (sw == grid.Width && sh == grid.Height) sizeMatch++;
                bool Occupied(int x, int z) => x >= 0 && z >= 0 && x < sw && z < sh
                                              && x < shape[sh - 1 - z].Length && shape[sh - 1 - z][x] != '.';
                char ShapeAt(int x, int z) => Occupied(x, z) ? shape[sh - 1 - z][x] : '.';
                edges += grid.EdgeCount;
                int gapsHere = entranceGaps;
                for (int z = 0; z < grid.Height; z++)
                    for (int x = 0; x < grid.Width; x++)
                    {
                        byte b = grid.Mask(x, z);
                        foreach (var (bit, dx, dz) in new[] { (1, 0, 1), (4, 1, 0), (0x10, 0, -1), (0x40, -1, 0) })
                        {
                            bool perimeter = Occupied(x, z) && !Occupied(x + dx, z + dz);
                            if ((b & bit) != 0 && !perimeter) offPerimeter++;
                            if ((b & bit) == 0 && perimeter && ShapeAt(x, z) == '2') entranceGaps++;
                        }
                    }
                if (entranceGaps - gapsHere == 1) oneEntranceGap++;
                // The geometry, over the model's root when it resolves (and nothing when it does not).
                List<Vector2> root = new();
                var entry = def.ModelPath == null ? null
                    : wad.Entries.FirstOrDefault(e => !WadArchive.IsAlias(e) && e.Path.Equals(def.ModelPath, StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                {
                    resolved++;
                    if (HoardingGeometry.RootVertices(new Model(wad.Read(entry)), out var xz, out string why)) { gated++; root = xz; }
                    else gateFaults.Add($"{def.Name}: {why}");
                }
                var geo = HoardingGeometry.Build(grid, root);
                panelsPer[geo.Panels.Count] = panelsPer.GetValueOrDefault(geo.Panels.Count) + 1;
                lengths.AddRange(geo.Panels.Select(p => p.Length));
                if (grid.Width == 1 && grid.Height == 1) oneByOne++;
                else
                {
                    cornersNoSecond += geo.CornersWithoutSecond;
                    for (int z = 0; z < grid.Height; z++) for (int x = 0; x < grid.Width; x++) if (grid.Mask(x, z) != 0) corners += 4;
                }
                // The control: the same sort in the host's round-to-nearest arithmetic.
                var host = geo.Panels.Select((p, i) => (Key: HoardingGeometry.HostSortKey(grid.Width * 0.5f, grid.Height * 0.5f, p.CellX, p.CellZ,
                                                                                      Array.IndexOf(new byte[] { 1, 4, 0x10, 0x40 }, p.Side)), p))
                                     .OrderBy(t => t.Key).ThenBy(t => Insertion(grid, t.p)).Select(t => t.p.Slot).ToList();
                if (!host.SequenceEqual(Enumerable.Range(0, geo.Panels.Count))) { orderDiffers++; differs.Add($"{def.Name} {grid.Width}x{grid.Height}"); }
                if (def.Name == "Big Dripper") dripper = geo;
            }
        }
        Check(sams == 321 && blocks == 163,
              $"census: {blocks} of the disc's {sams} .sam carry Info.Hoarding (research: 163 of 321)");
        Check(parsed == 163 && faults.Count == 0,
              $"parse (0x112fa0 over table 0x2ac268): {parsed} of {blocks} blocks parse" + (faults.Count > 0 ? $" -- {string.Join("; ", faults.Take(4))}" : ""));
        Check(sizeMatch == 163, $"size: {sizeMatch} of {parsed} blocks are exactly their Info.Shape's width x height (research: 163/163)");
        Check(edges == 1940, $"edges: {edges} set edge bits over {parsed} blocks, one quad each (research: 1940)");
        var wrongChars = NotesChars.Where(kv => charCount.GetValueOrDefault(kv.Key) != kv.Value).Select(kv => kv.Key).ToList();
        Check(wrongChars.Count == 0 && charCount.Keys.All(NotesChars.ContainsKey),
              $"characters: all 16 of the table counted as the research has them ({string.Join(" ", NotesChars.Keys.Select(c => $"{c}:{charCount.GetValueOrDefault(c).Chars}/{charCount.GetValueOrDefault(c).Blocks}"))})"
              + (wrongChars.Count > 0 ? $" -- DIFFER: {string.Join("", wrongChars)}" : ""));
        Check(offPerimeter == 0 && entranceGaps == 163 && oneEntranceGap == 163,
              $"outline: {offPerimeter} fence edges off the Shape's perimeter (research: 0), and {entranceGaps} unfenced perimeter edges on an "
              + $"entrance cell `2`, exactly one in {oneEntranceGap} of {parsed} blocks -- the queue's way in (research: 163, one per block)");
        string dist = string.Join(", ", panelsPer.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"));
        Check(panelsPer.Count == NotesPanels.Count && NotesPanels.All(kv => panelsPer.GetValueOrDefault(kv.Key) == kv.Value),
              $"panels per block: {dist} (research: 3:4, 7:16, 8:2, 9:7, 10:20, 11:50, 12:4, 13:1, 14:27, 16:14, 18:18)");
        Check(resolved == 155 && gated == 155 && gateFaults.Count == 0,
              $"root gate (header +0x70, flag 0x40 clear, <= 512 vertices): {gated} of {resolved} resolvable models pass, "
              + $"{blocks - resolved} blocks have no model the port resolves (research: 155 pass, 8 unresolved)"
              + (gateFaults.Count > 0 ? $" -- {string.Join("; ", gateFaults.Take(3))}" : ""));
        lengths.Sort();
        float P(double q) => lengths[(int)(lengths.Count * q)];
        int exactlyOne = lengths.Count(l => Math.Abs(l - 1f) < 0.001f);
        Check(lengths.Count == 1940 && Math.Abs(P(0.5) - 0.95f) < 0.005f && Math.Abs(P(0.1) - 0.90f) < 0.005f && Math.Abs(P(0.9) - 1.00f) < 0.005f
              && Math.Abs(lengths[0] - 0.40f) < 0.005f && Math.Abs(lengths[^1] - 1.50f) < 0.005f && exactlyOne == 790,
              $"geometry: {lengths.Count} panels, length median {P(0.5):F3}, p10 {P(0.1):F3}, p90 {P(0.9):F3}, range {lengths[0]:F3}..{lengths[^1]:F3}, "
              + $"{exactlyOne} exactly 1.0 (the research's emulator: 0.95 / 0.90 / 1.00, 0.40..1.50, 790)");
        Check(corners == 5280 && cornersNoSecond == 256,
              $"corner offsets (0x1f39f8): {cornersNoSecond} of {corners} corners find no second vertex 0.001 past the nearest and take 0 (research: 256 of 5280)");
        Check(oneByOne == 4 && HoardingGeometry.OneByOneOffsets[2] == new Vector2(0.3f, 0.3f),
              $"1x1 (0x1f39b8): {oneByOne} blocks take the fixed table, whose o2 is (+0.3, +0.3) as the MIPS stores it (the research's table says -0.3)");
        // The sort: Big Dripper's 4x3 top-left panel sits on the up-left diagonal of its centre.
        var last = dripper?.Panels[^1];
        var first = dripper?.Panels[0];
        Check(dripper != null && last is { CellX: 0, Side: HoardingGrid.PlusZ } l && l.CellZ == dripper.Grid.Height - 1
              && first is { CellX: 0, Side: HoardingGrid.MinusX } f && f.CellZ == dripper.Grid.Height - 1,
              $"order (0x1f3c70): Big Dripper's first panel is the -x edge of the top-left cell and its LAST the +z edge of that cell, the midpoint "
              + $"on the up-left diagonal whose key truncates to {last?.Key:R} on the EE (host rounding: 0, first) -- slot 0 {first?.CellX},{first?.CellZ} side 0x{first?.Side:X}");
        Check(orderDiffers > 0 && differs.Contains("Big Dripper 4x3"),
              $"order control: host round-to-nearest keys order {orderDiffers} of {parsed} blocks differently -- the instrument can see the rounding "
              + $"({string.Join(", ", differs.Take(6))}{(differs.Count > 6 ? ", ..." : "")})");
    }

    /// <summary>The build's insertion index of a panel: row-major, then bits 1, 4, 0x10, 0x40.</summary>
    static int Insertion(HoardingGrid grid, HoardingPanel p)
    {
        int i = 0;
        for (int z = 0; z < grid.Height; z++)
            for (int x = 0; x < grid.Width; x++)
                foreach (byte bit in new byte[] { 1, 4, 0x10, 0x40 })
                {
                    if ((grid.Mask(x, z) & bit) == 0) continue;
                    if (x == p.CellX && z == p.CellZ && bit == p.Side) return i;
                    i++;
                }
        return -1;
    }

    // =============================================================================================
    // The state: 0x1f5948 / 0x1f5ab0 / 0x1f5c10 / 0x1f5750.

    static RideHoarding Fence(int n)
    {
        // A 1-row block of n top edges: only the panel count matters to the state.
        var grid = HoardingGrid.Parse(new[] { new string('^', n) }, out _);
        return new RideHoarding(HoardingGeometry.Build(grid, Array.Empty<Vector2>()));
    }

    static void State(Action<bool, string> Check)
    {
        foreach (float dt in new[] { 0.04f, 1f / 60 })
        {
            var h = Fence(11);
            h.Raise(2);
            int up = 0, stop = -1, down = 0, hide = -1;
            while (h.Progress < 1f && up < 1000) { h.Tick(dt); up++; }
            for (int i = 1; i < 5 && stop < 0; i++) { h.Tick(dt); if (!h.Rising) stop = i; }
            h.Lower();
            while (h.Progress > 0f && down < 1000) { h.Tick(dt); down++; }
            for (int i = 1; i < 5 && hide < 0; i++) { h.Tick(dt); if (!h.Shown) hide = i; }
            // Within two frames: the float sum of 300 steps of 0.2/60 falls a hair short of 1 and takes a 301st.
            Check(Math.Abs(up * dt - 5f) <= 2 * dt && Math.Abs(down * dt - 1f / 0.3f) <= 2 * dt && stop == 1 && hide == 1
                  && h.Texture == HoardingTexture.Closed && h.Flags == (RideHoarding.Built | 0x100),
                  $"timing at dt {dt:F4} s: up in {up} ticks = {up * dt:F3} s (1/0.2 = 5.0), raising clears {stop} tick later; down in {down} = {down * dt:F3} s "
                  + $"(1/0.3 = 3.33), hidden {hide} tick later with the texture back to Closed and kind 0x100 (flags 0x{h.Flags:X})");
        }
        {
            var h = Fence(11);
            h.Raise(2);
            h.Tick(0f);
            bool pausedHeld = h.Progress == 0f && h.Rising && h.Shown;
            for (int i = 0; i < 200; i++) h.Tick(0.04f);
            var heights = Enumerable.Range(0, 11).Select(h.Height).ToList();
            Check(pausedHeld && heights.Where((_, i) => i % 2 == 0).All(v => v == 1f) && heights.Where((_, i) => i % 2 == 1).All(v => v == 0.8f),
                  $"heights (0x1f5750): fully up, even slots 1.0 and odd 0.8 ({string.Join(" ", heights.Select(v => v.ToString("0.##")))}); a paused tick (dt 0) holds p at 0");
        }
        {
            // Panel i starts at p = 0.8 i/N and is up 0.25 later.
            int n = 14; bool ok = true;
            for (int i = 0; i < n; i++)
            {
                float start = 0.8f * i / n;
                ok &= RideHoarding.PanelHeight(i, n, start - 0.001f) == 0f
                   && RideHoarding.PanelHeight(i, n, start + 0.01f) > 0f
                   && Math.Abs(RideHoarding.PanelHeight(i, n, start + 0.25f) - (i % 2 == 0 ? 1f : 0.8f)) < 1e-4f;
            }
            float raw17 = RideHoarding.PanelHeight(17, 18, 1f) / 0.8f;
            Check(ok && Math.Abs(raw17 - 0.9778f) < 0.0005f && RideHoarding.PanelHeight(16, 18, 1f) == 1f && RideHoarding.PanelHeight(15, 16, 1f) == 0.8f,
                  $"rise order: over N = {n} panel i starts at p = 0.8 i/N and is up 0.25 later; for N = 18 the last slot peaks at {raw17:F4} "
                  + $"(x 0.8 = {RideHoarding.PanelHeight(17, 18, 1f):F4}) while N = 16's last still reaches its full 0.8");
        }
        {
            // 0x1f5948's texture precedence.
            var h = Fence(7);
            h.Raise(2); var t1 = h.Texture;
            h.Raise(4); var t2 = h.Texture;
            h.Raise(2); var t3 = h.Texture;
            h.Raise(8); var t4 = h.Texture;
            h.Raise(2); var t5 = h.Texture;
            h.Raise(1); var t6 = h.Texture;
            Check(t1 == HoardingTexture.Hoarding && t2 == HoardingTexture.Condemn && t3 == HoardingTexture.Condemn
                  && t4 == HoardingTexture.Upgrade && t5 == HoardingTexture.Hoarding && t6 == HoardingTexture.Hoarding,
                  $"textures (0x1f5948): broken {t1}; condemned {t2}; broken again keeps {t3}; upgrade {t4} replaces Condemn; broken over an "
                  + $"upgrade {t5}; bits 1 on a raised fence changes nothing ({t6})");
        }
        {
            // Progress is not reset: half-way down, a raise continues from there. And a lower on a hidden fence
            // only sets the rate.
            var h = Fence(9);
            h.Raise(2);
            for (int i = 0; i < 200; i++) h.Tick(0.04f);
            h.Lower();
            for (int i = 0; i < 40; i++) h.Tick(0.04f);
            float mid = h.Progress;
            h.Raise(2);
            h.Tick(0.04f);
            var idle = Fence(9);
            idle.Lower();
            idle.Tick(0.04f);
            Check(mid > 0.4f && mid < 0.6f && Math.Abs(h.Progress - (mid + 0.008f)) < 1e-5f && h.Rising && !h.Dropping
                  && idle.Flags == RideHoarding.Built && idle.Rate == RideHoarding.DropRate && idle.Progress == 0f,
                  $"re-raise: caught at p {mid:F3} on the way down, a raise rises again FROM THERE ({h.Progress:F3}); a lower on a hidden fence "
                  + $"only sets the rate (flags 0x{idle.Flags:X})");
        }
    }
}

/// <summary>The ride-service half, on the mechanic fixture: the fence follows the REAL `0x118568`/`0x118678`
/// calls (<see cref="ParkSim.HoardingRaise"/>/<see cref="ParkSim.HoardingLower"/>), not a direct poke.</summary>
static partial class MechanicChecks
{
    /// <summary>Each ride's fence, driven by the park's hoarding sinks and ticked 40 ms a park step.</summary>
    sealed class Fences
    {
        public readonly Dictionary<ParkRide, RideHoarding> By = new(ReferenceEqualityComparer.Instance);
        /// <summary>Every sink call: bits 2/4/8 for a raise, 0 for a lower, with the fence's progress at that moment.</summary>
        public readonly List<(ParkRide Ride, int Bits, float Progress)> Events = new();
        public RideHoarding Of(ParkRide r) => By.TryGetValue(r, out var h) ? h : null;
        public void Attach(ParkSim sim)
        {
            RideHoarding For(ParkRide r)
            {
                if (By.TryGetValue(r, out var h)) return h;
                var grid = HoardingGrid.Parse(r.Definition?.Hoarding, out _);
                return grid == null ? null : By[r] = new RideHoarding(HoardingGeometry.Build(grid, Array.Empty<Vector2>()));
            }
            sim.HoardingRaise = (r, bits) => { var h = For(r); Events.Add((r, bits, h?.Progress ?? -1)); h?.Raise(bits); };
            sim.HoardingLower = r => { var h = For(r); Events.Add((r, 0, h?.Progress ?? -1)); h?.Lower(); };
        }
        public void Tick() { foreach (var h in By.Values) h.Tick(ParkSim.TickMilliseconds / 1000f); }
    }

    static void HoardingService(Fixture f, Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "hoarding: " + label);
        string name = f.Ordinary.Stem[(f.Ordinary.Stem.LastIndexOf('/') + 1)..];
        {
            // A breakdown raises it with Hoarding.ssh; the repair's completion (0x1786D0 → 0x118678) drops it.
            var p = f.NewPark(80);
            var fences = new Fences(); fences.Attach(p.Sim);
            var r = f.Open(p, p.At(2, 4));
            var a = f.Ordinary.Record.ConnectionA;
            var mouth = p.At(2 + a.X, 2);
            p.Staff.QueueMouth = ride => ReferenceEquals(ride, r) ? mouth : null;
            var m = HireAt(p, p.At(12, 2));
            p.Rng.Override = n => n == 2 ? 0 : null;
            for (int t = 0; t < 20; t++) { p.Step(); fences.Tick(); }
            bool quietBefore = fences.Events.Count == 0 && fences.Of(r) == null;
            r.ForceReliabilityForTest(0x5000);
            int brokeAt = -1, fullAt = -1, clearedAt = -1, hiddenAt = -1, t0 = -1;
            for (int t = 0; t < 4000 && hiddenAt < 0; t++)
            {
                bool flag = r.ServiceFlag;
                p.Step(); fences.Tick();
                var h = fences.Of(r);
                if (brokeAt < 0 && h is { Shown: true }) { brokeAt = t; t0 = fences.Events.Count; }
                if (fullAt < 0 && h is { Progress: 1f }) fullAt = t;
                if (clearedAt < 0 && flag && !r.ServiceFlag) clearedAt = t;
                if (clearedAt >= 0 && hiddenAt < 0 && h is { Shown: false }) hiddenAt = t;
            }
            var fence = fences.Of(r);
            var firstRaise = fences.Events.FirstOrDefault();
            Check(quietBefore && firstRaise.Ride == r && firstRaise.Bits == 2 && brokeAt >= 0 && fence != null
                  && fence.Geometry.Panels.Count == HoardingGrid.Parse(r.Definition.Hoarding, out _).EdgeCount,
                  $"{name}: nothing before the breakdown; then 0x116D68 → 0x118568(ride, 1) raises its hoarding with bits 2 (Hoarding.ssh) through "
                  + $"ParkSim.HoardingRaise, {fence?.Geometry.Panels.Count} panels from its own Info.Hoarding");
            Check(fullAt > brokeAt && Math.Abs((fullAt - brokeAt + 1) * 0.04f - 5f) <= 0.08f,
                  $"{name}: fully up {fullAt - brokeAt + 1} park ticks after it appeared ({(fullAt - brokeAt + 1) * 0.04f:F2} s at 40 ms; 5.0 s)");
            var lowers = fences.Events.Where(e => e.Ride == r && e.Bits == 0).ToList();
            Check(clearedAt > fullAt && lowers.Count == 1 && lowers[0].Progress == 1f && hiddenAt > clearedAt
                  && Math.Abs((hiddenAt - clearedAt) * 0.04f - 1f / 0.3f) <= 0.12f && fence.Texture == HoardingTexture.Closed
                  && fences.Events.Skip(t0).Where(e => e.Bits != 0).All(e => e.Bits == 2),
                  $"{name}: the repair's end (0x118678) lowers it once from full height; hidden {hiddenAt - clearedAt} ticks later "
                  + $"({(hiddenAt - clearedAt) * 0.04f:F2} s; 3.33 s) with the texture back to Closed -- every raise meanwhile was bits 2 (m {m.RepairDispatches} repairs)");
        }
        {
            // An upgrade: 0x178A38 → 0x118568(ride, 2) raises Upgrade.ssh; the install's end lowers it.
            var p = f.NewPark(81);
            var fences = new Fences(); fences.Attach(p.Sim);
            var r = f.Open(p, p.At(2, 4));
            r.ForceReliabilityForTest(0x40000);
            var m = Hire(p, 1);
            p.Staff.RequestUpgrade(r);
            Drop(p, m, p.At(10, 2));
            p.Rng.Override = n => n == 2 ? 1 : null;
            HoardingTexture during = HoardingTexture.Closed;
            int installed = -1;
            for (int t = 0; t < 3000 && installed < 0; t++)
            {
                p.Step(); fences.Tick();
                if (fences.Of(r) is { Shown: true } h) during = h.Texture;
                if (r.CurrentTier == 1) installed = t;
            }
            for (int t = 0; t < 200; t++) { p.Step(); fences.Tick(); }
            Check(installed > 0 && during == HoardingTexture.Upgrade && fences.Events.Where(e => e.Bits != 0).All(e => e.Bits == 8)
                  && fences.Events.Count(e => e.Bits == 0) == 1 && fences.Of(r) is { Shown: false, Texture: HoardingTexture.Closed },
                  $"{name}: an upgrade install raises it with bits 8 (Upgrade.ssh) and its end lowers it to hidden ({string.Join(",", fences.Events.Select(e => e.Bits).Distinct())})");
        }
        {
            // Condemned: 0x1169C0 → 0x118568(ride, 4), then the breakdown's bits 2 every update keep Condemn; nothing lowers it.
            var p = f.NewPark(82);
            var fences = new Fences(); fences.Attach(p.Sim);
            var r = f.Open(p, p.At(2, 4));
            r.Life = 1;
            r.ForceReliabilityForTest(ParkSim.ReliabilityPerLife * 2 + 1);
            for (int g = 1; g <= 20; g++) r.Join(9400 + g);
            int ticks = 0;
            while (r.Life > 0 && ticks < 4000) { p.Step(); fences.Tick(); ticks++; }
            for (int t = 0; t < 400; t++) { p.Step(); fences.Tick(); }
            var h = fences.Of(r);
            int fours = fences.Events.Count(e => e.Bits == 4), twos = fences.Events.Count(e => e.Bits == 2);
            Check(r.Life == 0 && fours == 1 && twos > 100 && fences.Events.All(e => e.Bits != 0)
                  && h is { Shown: true, Texture: HoardingTexture.Condemn, Progress: 1f },
                  $"{name}: condemned, raised once with bits 4 (Condemn.ssh); the {twos} bits-2 raises of the breakdown check after it keep Condemn "
                  + $"(&0x600), and 400 ticks later it is still up and never lowered");
        }
    }
}
