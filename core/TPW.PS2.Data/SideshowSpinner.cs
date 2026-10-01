namespace TPW.PS2.Data;

/// <summary>The Sideshow prize/price controls' numeric step, not a clamp on stored game state.
/// Setup 0x1D7F48 loads min(raw,1000), with no incoming lower clamp. An ACTIVE non-wrapping
/// spinner at 0x207B10 then adds its signed step and clamps to 1..1000. Prize0 writeback is
/// separately suppressed by 0x1D8118; the caller must retain that guard.</summary>
public static class SideshowSpinner
{
    public const int Min = 1;
    public const int Max = 1000;

    public static int Load(int raw) => Math.Min(raw, Max);

    /// <summary>A control event on a freshly bound/loaded spinner. Native addu/subu do not trap
    /// on 32-bit overflow. This does not implement focus, passive updates or sound feedback.</summary>
    public static int Step(int raw, int by) => Math.Clamp(unchecked(Load(raw) + by), Min, Max);
}