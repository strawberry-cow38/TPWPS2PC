using System.Buffers.Binary;
using TPW.PS2.Data;

/// <summary>
/// Explicit core/presentation fixtures, backed by read-only disc layout and ELF checks.
/// See findings/sideshow-conditional-layout.md. NOT rendered/player proof: no panel,
/// hitboxes, callbacks, subject lifecycle, drag continuity or writeback is exercised here.
/// Native assets are read in memory through Disc/WadArchive/SceneLayout, never extracted.
/// </summary>
static class SideshowPresentationChecks
{
    public static void Run(Disc disc, Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "sideshow presentation: explicit core fixture: " + label);

        // Literal oracles, not generated from the implementation's rows or visibility rules.
        var canonical = new LaptopRow[]
        {
            new(707, LaptopRowKind.Value),
            new(637, LaptopRowKind.Value),
            new(949, LaptopRowKind.Money),
            new(238, LaptopRowKind.Money),
            new(899, LaptopRowKind.Bar, "ExcitementBar"),
            new(743, LaptopRowKind.Bar, "SatisfactionBar"),
            new(295, LaptopRowKind.Slider, "ChanceofWinningSlider"),
            new(832, LaptopRowKind.Money, "PricePerGameValue", "CostOfPrizeArrow"),
            new(190, LaptopRowKind.Money, "CostOfPrizeValue", "PricePerGameArrow"),
        };
        var nonzero = new LaptopRowPresentation[]
        {
            new(0, true, true, true), new(1, true, true, true), new(2, true, true, true),
            new(3, true, true, true), new(4, true, true, true), new(5, true, true, true),
            new(6, true, true, true), new(7, true, true, true), new(8, true, true, true),
        };
        var zero = new LaptopRowPresentation[]
        {
            new(0, true, true, true),
            new(null, false, false, false), // Winners value must disappear too.
            new(1, true, true, true),
            new(2, true, true, true),
            new(3, true, true, true),       // Bar stays visible at its authored frame.
            new(4, true, true, true),
            new(null, false, false, false), // Chance widget is hidden, not just its label.
            new(null, true, false, false),  // Unlabelled prize digits still draw; arrows do not.
            new(5, true, true, true),       // Price digits AND arrows remain visible.
        };
        int?[] nonzeroSlots = { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        int?[] zeroSlots = { 0, null, 1, 2, 3, 4, null, null, 5 };

        var screen = LaptopScreen.Sideshow;
        Check(screen.SceneFile == "main_i_sideshow_data.sce" && screen.MenuId == 0x13,
            "canonical sideshow scene/menu identity");
        Check(screen.LabelElement == "InfoText" && screen.ValueElement == "InfoValues"
              && screen.TitleElement == "SideshowName" && screen.ModelElement == "Model",
            "canonical shared label/value and title/model aliases");
        Check(screen.Rows.Count == 9, "nine canonical rows, never a compacted row list");
        Check(screen.Rows.Select(r => r.TextId).SequenceEqual(new[] { 707, 637, 949, 238, 899, 743, 295, 832, 190 }),
            "literal canonical TextIds [707,637,949,238,899,743,295,832,190]");
        Check(screen.WidgetStep == 0, "WidgetStep=0: authored bars, slider, digits and arrows do not flow");
        Check(screen.LabelsOnGrid && screen.StepBase == 0, "LabelsOnGrid=true, StepBase=0");
        for (int i = 0; i < 9; i++)
            Check(i < screen.Rows.Count && screen.Rows[i] == canonical[i],
                $"canonical index {i}: {canonical[i]} (prize832 and price190 retain crossed VALUE aliases, adjacent ARROW aliases)");

        var heldPrize = LaptopRowPresentation.Sideshow(1);
        var heldZero = LaptopRowPresentation.Sideshow(0);
        foreach (int rawPrize in new[] { 1, 30, -1 })
        {
            var plan = LaptopRowPresentation.Sideshow(rawPrize);
            Check(plan.Count == 9, $"raw prize {rawPrize}: nine-row nonzero plan");
            Check(plan.Select(r => r.LabelSlot).SequenceEqual(nonzeroSlots),
                $"raw prize {rawPrize}: literal label slots [0,1,2,3,4,5,6,7,8]; negative is NONZERO too");
            for (int i = 0; i < 9; i++)
                Check(i < plan.Count && plan[i] == nonzero[i],
                    $"raw prize {rawPrize}, canonical row {i}: label {i}, value/widget/arrows ALL visible");
        }
        Check(heldZero.Count == 9, "raw prize 0: still nine canonical rows");
        Check(heldZero.Select(r => r.LabelSlot).SequenceEqual(zeroSlots),
            "raw prize 0: literal label slots [0,null,1,2,3,4,null,null,5]");
        string[] zeroDescriptions =
        {
            "Customers unchanged", "Winners label AND value hidden", "Takings label slot1",
            "Profit label slot2", "Excitement label slot3, bar visible", "Satisfaction label slot4, bar visible",
            "Chance label/value/widget/arrows hidden", "Prize VALUE VISIBLE; label/widget/arrows hidden",
            "Price label slot5; value/widget/arrows visible, canonical callback index8 unchanged",
        };
        for (int i = 0; i < 9; i++)
            Check(i < heldZero.Count && heldZero[i] == zero[i], $"raw prize 0, canonical row {i}: {zeroDescriptions[i]}");

        Immutable(heldPrize, nonzero, 1, Check);
        Immutable(heldZero, zero, 0, Check);
        // Fresh state means uncontaminated results, NOT a requirement for fresh allocations.
        // Sharing immutable plans is valid. Retain both old references while interleaving calls.
        foreach (int rawPrize in new[] { 0, 1, 0, 30, -1, 0 })
        {
            var returned = LaptopRowPresentation.Sideshow(rawPrize);
            Check(returned.SequenceEqual(rawPrize == 0 ? zero : nonzero)
                  && heldZero.SequenceEqual(zero) && heldPrize.SequenceEqual(nonzero),
                $"call/return raw prize {rawPrize}: literal fresh state and previously returned plans unchanged");
        }
        Check(screen.Rows.SequenceEqual(canonical) && LaptopScreen.Sideshow.Rows.SequenceEqual(canonical),
            "presentation calls and attempted caller mutations leave all canonical row indices/kinds/aliases unchanged");

        SpinnerBounds(Check);
        AuthoredScene(disc, Check);
        Executable(disc, Check);
    }

    static void Immutable(IReadOnlyList<LaptopRowPresentation> plan, LaptopRowPresentation[] expected,
                          int rawPrize, Action<bool, string> Check)
    {
        var poison = new LaptopRowPresentation(99, false, false, false);
        bool rejected = false;
        if (plan is IList<LaptopRowPresentation> list)
        {
            try { list[0] = poison; }
            catch (NotSupportedException) { rejected = list.IsReadOnly; }
        }
        else rejected = plan is not LaptopRowPresentation[];
        Check(rejected && plan.SequenceEqual(expected), $"raw prize {rawPrize}: generic list setter cannot mutate returned plan");

        rejected = false;
        if (plan is System.Collections.IList untyped)
        {
            try { untyped[0] = poison; }
            catch (NotSupportedException) { rejected = untyped.IsReadOnly; }
        }
        else rejected = true;
        Check(rejected && plan.SequenceEqual(expected), $"raw prize {rawPrize}: non-generic list setter cannot mutate returned plan");

        var cell = plan[0];
        cell = cell with { LabelSlot = 99, ValueVisible = false, WidgetVisible = false, ArrowsVisible = false };
        Check(cell == poison && plan.SequenceEqual(expected),
            $"raw prize {rawPrize}: changing a returned record COPY cannot change its source cell");
        var detached = plan.ToArray();
        detached[0] = poison;
        Check(detached[0] == poison && plan.SequenceEqual(expected),
            $"raw prize {rawPrize}: caller-owned array mutation is isolated");
        Check(LaptopRowPresentation.Sideshow(rawPrize).SequenceEqual(expected),
            $"raw prize {rawPrize}: next call returns unpoisoned literal state after all mutation attempts");
    }

    static void AuthoredScene(Disc disc, Action<bool, string> Check)
    {
        var entry = disc.Files().Single(f => f.Path.Equals("/DATA/MENUS.WAD", StringComparison.OrdinalIgnoreCase));
        var menus = new WadArchive(disc.Read(entry.Extent, entry.Size));
        // Deliberately use literal authored names, NOT LaptopScreen's mapping as the oracle.
        var scene = SceneLayout.Parse(menus.Read(menus.Find("/main_i_sideshow_data.sce")));
        void Position(string name, int x, int y)
        {
            var e = scene[name];
            Check(e.HasValue && e.Value.X == x && e.Value.Y == y,
                $"real MENUS.WAD authored {name}: X{x}, Y{y} (independent of row mapping)");
        }
        void Widget(string name, int x, int y)
        {
            var e = scene[name];
            Check(e.HasValue && e.Value.X == x && e.Value.Y == y
                  && e.Value.Width == 72 && e.Value.Height == 22,
                $"real MENUS.WAD authored {name}: X{x}, Y{y}, size72x22; no whole-row compaction");
        }
        Position("InfoText", 45, 115);
        Position("PricePerGameValue", 275, 340);
        Position("CostOfPrizeValue", 275, 372);
        Position("CostOfPrizeArrow", 200, 353);
        Position("PricePerGameArrow", 200, 384);
        Widget("ExcitementBar", 215, 246);
        Widget("SatisfactionBar", 215, 277);
        Widget("ChanceofWinningSlider", 215, 308);

        int?[] fullY = { 115, 147, 179, 211, 243, 275, 307, 339, 371 };
        int?[] zeroY = { 115, null, 147, 179, 211, 243, null, null, 275 };
        foreach (int rawPrize in new[] { 1, 30, -1, 0 })
        {
            var origin = scene["InfoText"];
            // This computes explicit grid-fixture coordinates, not the viewer's rendered rectangles.
            var y = LaptopRowPresentation.Sideshow(rawPrize)
                .Select(r => r.LabelSlot.HasValue && origin.HasValue
                    ? (int?)(origin.Value.Y + 32 * r.LabelSlot.Value) : null);
            Check(origin.HasValue && origin.Value.X == 45 && y.SequenceEqual(rawPrize == 0 ? zeroY : fullY),
                $"raw prize {rawPrize}: explicit label-grid fixture matches literal native Y slots; authored widgets stay separate");
        }
    }

    static void SpinnerBounds(Action<bool,string> Check)
    {
        Check(SideshowSpinner.Min==1 && SideshowSpinner.Max==1000,"spinner bounds: literal min1/max1000");
        (int Raw,int Loaded)[] loads={(-1,-1),(0,0),(1,1),(999,999),(1000,1000),(1001,1000),(65535,1000),(int.MaxValue,1000)};
        foreach(var (raw,expected) in loads)
            Check(SideshowSpinner.Load(raw)==expected,$"spinner bounds: raw{raw} setup expected{expected}; upper clip only");
        (int Raw,int Down,int Idle,int Up)[] cases={
            (-1,1,1,1),(0,1,1,1),(1,1,1,2),(999,998,999,1000),(1000,999,1000,1000),
            (1001,999,1000,1000),(65535,999,1000,1000),(int.MaxValue,999,1000,1000),(int.MinValue,1000,1,1),
        };
        foreach(var (raw,down,idle,up) in cases)
        {
            Check(SideshowSpinner.Step(raw,-1)==down,$"spinner bounds: raw{raw} active decrement expected{down}");
            Check(SideshowSpinner.Step(raw,0)==idle,$"spinner bounds: raw{raw} active no-input expected{idle}; arithmetic fixture, not focus proof");
            Check(SideshowSpinner.Step(raw,1)==up,$"spinner bounds: raw{raw} active increment expected{up}");
        }
    }

    static void Executable(Disc disc, Action<bool, string> Check)
    {
        var entry = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        byte[] elf = disc.Read(entry.Extent, entry.Size);
        uint U32(int off) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(off, 4));
        int U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(off, 2));
        uint Word(uint address)
        {
            int ph = checked((int)U32(28));
            for (int i = 0; i < U16(44); i++)
            {
                int p = ph + i * U16(42);
                if (U32(p) != 1) continue;
                uint va = U32(p + 8), size = U32(p + 16);
                if (address >= va && (ulong)address + 4 <= (ulong)va + size)
                    return U32(checked((int)(U32(p + 4) + address - va)));
            }
            throw new InvalidDataException($"no sideshow executable word at 0x{address:X}");
        }
        Check(Word(0x1D8488) == 0x0C074A92,
            "native draw0x1D8288: 0x1D8488 is jal raw-prize getter0x1D2A48, NOT a function start");
        uint branch = Word(0x1D8490);
        long target = 0x1D8490L + 4 + (short)(branch & 0xFFFF) * 4L;
        Check((branch >> 26) == 4 && ((branch >> 21) & 31) == 2
              && ((branch >> 16) & 31) == 0 && target == 0x1D84B0,
            "native 0x1D8490: beq v0,zero to0x1D84B0; raw prize ==0, not a signed-positive test");
        uint step = Word(0x1D84AC);
        Check((step >> 26) == 9 && (step & 0xFFFF) == 32
              && ((step >> 21) & 31) == ((step >> 16) & 31),
            "native 0x1D84AC: addiu row,row,32; conditional Winner row advances the label grid");
        Check(new[] { Word(0x1D2A48), Word(0x1D2A4C), Word(0x1D2A50) }.Contains(0x8C8200A8),
            "native raw-prize getter0x1D2A48 reads lw v0,0xA8(a0), a full word, not formatted money/Winners/chance");
        Check(Word(0x1D7FD4)==0x24100001 && Word(0x1D7FD8)==0x241303E8,
            "native spinner setup: literal min1/max1000 loaded at0x1D7FD4/0x1D7FD8");
        Check(Word(0x1D800C)==0xACD00070 && Word(0x1D8018)==0xACD30074
            && Word(0x1D8058)==0xACB30074 && Word(0x1D8064)==0xACB00070,
            "native spinner setup: both prize/price receive the SAME min1/max1000 fields");
        Check(Word(0x1D8040)==0x0045182A && Word(0x1D8044)==0x00A3100A
            && Word(0x1D808C)==0x0062202A && Word(0x1D8094)==0x0044180A,
            "native spinner setup: raw values use signed upper clip, no incoming lower clip");
        Check(Word(0x1D2A34)==0x948200CC && Word(0x1D2A2C)==0xA48500CC,
            "native price getter/setter: lhu/sh unsigned16, not signed minus1");
        Check(Word(0x1D2A4C)==0x8C8200A8 && Word(0x1D2A54)==0xAC8500A8,
            "native prize getter/setter: lw/sw signed32 word");
        Check(Word(0x207AA4)==0xAE040060 && Word(0x207A98)==0x24040006,
            "native spinner mode6: wrapping bit0 absent");
        Check(Word(0x207B70)==0x00A21023 && Word(0x207B8C)==0x00431021,
            "native spinner arithmetic: subu/addu 32-bit non-trapping steps");
        Check(Word(0x207BE0)==0x0043102A && Word(0x207BEC)==0xAE030068
            && Word(0x207BFC)==0x0062102A && Word(0x207C08)==0xAE030068,
            "native active spinner: signed lower AND upper clamps, not wrapping");
    }
}
