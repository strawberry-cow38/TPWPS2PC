namespace TPW.PS2.Data;

/// <summary>One canonical row's presentation. A null label slot hides its label/hit band; values,
/// widgets and arrows have independent visibility because the console does NOT collapse whole rows.
/// Label slots move only the label grid and shared-column values, never authored widget elements.
/// Row indices/cells/callbacks remain those of <see cref="LaptopScreen.Rows"/>.</summary>
public readonly record struct LaptopRowPresentation(int? LabelSlot, bool ValueVisible = true,
                                                   bool WidgetVisible = true, bool ArrowsVisible = true)
{
    static readonly IReadOnlyList<LaptopRowPresentation> Prize = Array.AsReadOnly(
        Enumerable.Range(0, 9).Select(i => new LaptopRowPresentation(i)).ToArray());
    static readonly IReadOnlyList<LaptopRowPresentation> NoPrize = Array.AsReadOnly(new[]
    {
        new LaptopRowPresentation(0),                 // Customers
        new LaptopRowPresentation(null, false, false, false), // Winners AND its value disappear
        new LaptopRowPresentation(1),                 // Takings, up one step
        new LaptopRowPresentation(2),                 // Profit
        new LaptopRowPresentation(3),                 // Excitement label; fixed bar does not move
        new LaptopRowPresentation(4),                 // Satisfaction label; fixed bar does not move
        new LaptopRowPresentation(null, false, false, false), // Chance label/slider hidden
        new LaptopRowPresentation(null, true, false, false),  // Prize digits STILL draw; no label/arrows
        new LaptopRowPresentation(5),                 // Price label flows; digits/arrows stay authored
    });

    /// <summary>READ `0x1D8288`: `0x1D8488` / `0x1D2A48` test the RAW prize against zero.
    /// There are still nine canonical rows, even when only six labels are drawn.</summary>
    public static IReadOnlyList<LaptopRowPresentation> Sideshow(int prize) => prize == 0 ? NoPrize : Prize;
}