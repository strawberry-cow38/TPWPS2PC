using System.Buffers.Binary;
using TPW.PS2.Data;

/// <summary>⭐ THE TRAMPOLINE (strawberry, 2026-09-29: "guests invisible riding belly bounce"). A BOUNCE ride
/// seats nobody -- its guests go into the script's bounce table and the ticker `0x1BB888` stands them on a
/// pad fitting (space 0x800) and bounces them (RseMachine.BounceHeight). The viewer now draws them from that
/// table; this holds the core half: the loader's two defaults read back out of the executable, every pad
/// of every BOUNCE script on the disc resolving on its own model, and the height's shape.</summary>
static class BounceChecks
{
    public static void Run(Disc disc, Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "bounce: " + label);
        var exe = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        Executable(disc.Read(exe.Extent, exe.Size), Check);
        Pads(disc, Check);
        Height(Check);
    }

    // The loader 0x1BFDF8 initialises the instance: `li t2, 0x32 ... sh t2, 0xC0(s2)` and `li t0, 1 ... sw t0,
    // 0x70(s2)`, and the ticker's speed constant is the double 0.8 at 0x366CC0.
    static void Executable(byte[] elf, Action<bool, string> Check)
    {
        uint U32(int off) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(off, 4));
        int U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(off, 2));
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
        Check(U32(At(0x1c002c)) == 0x240a0032 && U32(At(0x1c0060)) == 0xa64a00c0 && new ParkRide().Setting0xC0 == 0x32,
              $"the loader writes 0x32 to inst+0xC0 (0x1C002C/0x1C0060), and a new ride starts there: {new ParkRide().Setting0xC0}");
        var probe = new RseMachine(Program(Bouncer));
        probe.RunSlice(0);
        probe.RunSlice(10);
        Check(U32(At(0x1c0034)) == 0x24080001 && U32(At(0x1c0064)) == 0xae480070 && probe.Bouncers is [(7, 1, _)],
              $"the loader writes 1 to inst+0x70 (0x1C0034/0x1C0064), so the first bouncer stands on pad 1: "
              + string.Join(" ", probe.Bouncers.Select(b => $"#{b.Guest} pad {b.Node}")));
        Check(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(elf.AsSpan(At(0x366cc0), 8))) == 0.8,
              "the ticker's speed is inst+0xC0 / 200 + 0.8, the 0.8 read at 0x366CC0");
    }

    // `0: BOUNCE 7 5` (guest 7 on for five seconds), then `3: ENDSLICE; 4: BRANCH @3` to idle.
    static readonly uint[] Bouncer =
    {
        0x80000000u | (uint)RseOpcode.BOUNCE, 7, 5,
        0x80000000u | (uint)RseOpcode.ENDSLICE,
        0x80000000u | (uint)RseOpcode.BRANCH, 0x20000003,
    };

    /// <summary>The smallest RSSE image the loader accepts: no variables, four bounce slots, no strings.</summary>
    static RseProgram Program(uint[] code)
    {
        var d = new byte[52 + code.Length * 4 + 4];
        "RSSE"u8.CopyTo(d);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(4), 0x10f51);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(12), 4);     // stack
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(16), 10);    // instructions a slice
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(24), 4);     // bounce slots
        "Pad Pad Pad Pad "u8.CopyTo(d.AsSpan(32));
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(48), (uint)code.Length);
        for (int i = 0; i < code.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(52 + i * 4), code[i]);
        return new RseProgram(d);                                      // the trailing zero is the empty string block
    }

    // ⭐ Every BOUNCE ride on the disc: every slot its guests can reach -- the .sam's largest capacity, never more
    // than the script's table -- on a pad of its own model. Script-only WADs (FRSE, HRSE...) carry no model and
    // are the same scripts again; they are counted and skipped.
    static void Pads(Disc disc, Action<bool, string> Check)
    {
        int rides = 0, slots = 0, resolved = 0, resolvedAtZero = 0, scriptOnly = 0, spare = 0;
        var misses = new List<string>(); var names = new List<string>();
        foreach (var w in disc.Files().Where(f => !f.IsDirectory && f.Path.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase)))
        {
            WadArchive wad;
            try { wad = new WadArchive(disc.Read(w.Extent, w.Size)); } catch { continue; }
            foreach (var entry in wad.Entries.Where(x => x.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase) && !WadArchive.IsAlias(x)))
            {
                RseProgram prog;
                try { prog = new RseProgram(wad.Read(entry)); } catch { continue; }
                if (!prog.Instructions.Any(i => i.Opcode == RseOpcode.BOUNCE)) continue;
                var dir = entry.Path[..(entry.Path.LastIndexOf('/') + 1)];
                WadArchive.Entry Beside(string ext) => wad.Entries.FirstOrDefault(x => x.Path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)
                    && x.Path.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && !WadArchive.IsAlias(x));
                if (Beside(".mps") is not { } mps) { scriptOnly++; continue; }
                var model = new Model(wad.Read(mps));
                int capacity = prog.BounceCapacity;
                if (Beside(".sam") is { } sam)
                {
                    var def = RideDefinition.Parse(System.Text.Encoding.ASCII.GetString(wad.Read(sam)), sam.Path);
                    int most = Enumerable.Range(0, 3).Select(t => def.UpgradeCapacity(t) ?? 0).Max();
                    if (most > 0) { spare += Math.Max(0, capacity - most); capacity = Math.Min(capacity, most); }
                }
                int bse = prog.Instructions.FirstOrDefault(i => i.Opcode == RseOpcode.BOUNCESETNODE) is { } set ? (int)set.Operands[0].Word : 1;
                rides++; names.Add($"{Path.GetFileNameWithoutExtension(entry.Path)} {capacity} from pad {bse}");
                for (int s = 0; s < capacity; s++)
                {
                    slots++;
                    if (model.FindFitting(s + bse, 0x800) is { Node: >= 0 }) resolved++;
                    else misses.Add($"{Path.GetFileName(entry.Path)} slot {s} pad {s + bse}");
                    if (model.FindFitting(s, 0x800) is { Node: >= 0 }) resolvedAtZero++;
                }
            }
        }
        Check(rides >= 2 && resolved == slots,
              $"{rides} BOUNCE rides ({string.Join(", ", names)}): {resolved} of {slots} reachable slots stand on a pad of their own model"
              + $" ({spare} table slots past the .sam's largest capacity, {scriptOnly} script-only copies skipped)"
              + (misses.Count > 0 ? $" -- missing: {string.Join(", ", misses.Take(6))}" : ""));
        Check(resolvedAtZero < slots,
              $"the control: counted from pad 0 instead of the loader's 1, only {resolvedAtZero} of {slots} would -- the base is visible");
    }

    // The ticker's hump: rest at the bottom, (key % 7 + 12) tenths up at the top, never below the pad.
    static void Height(Action<bool, string> Check)
    {
        const int key = 3, setting = 50, baseTenths = 8;
        float At(long t, float pad = 0f) => RseMachine.BounceHeight(1000 + t, 1000, key, setting, baseTenths, 0f, pad);
        // speed 50/200 + 0.8 = 1.05: phase-ms = t * 1.05; the top of the hump is at 320 * pi/2 phase-ms.
        long top = (long)Math.Round(320 * Math.PI / 2 / 1.05);
        float lo = At(0), hi = At(top), peak = (baseTenths + key % 7 + 12f) / 10f;
        Check(Math.Abs(lo - 0.8f) < 1e-4f && Math.Abs(hi - peak) < 0.01f,
              $"the hump: {lo:F2} at the bottom (BOUNCESETBASE 8 tenths), {hi:F2} at the top ({peak:F2} = (8 + {key} % 7 + 12) / 10)");
        Check(At(0, 1.2f) == 1.2f && At(top, 1.2f) > 1.2f,
              "never below the pad: with the pad at 1.2 the bottom is the pad and the top clears it");
        // UNBOUNCE only lets a guest off in the first fifth of each raw second; that must be the low end.
        float offWindow = Enumerable.Range(0, 20).Max(i => At(i * 10)), midAir = At(top);
        Check(offWindow < midAir - 0.5f,
              $"the get-off window (first 200 ms) peaks at {offWindow:F2}, well under the {midAir:F2} mid-air -- guests leave at the bottom");
    }
}
