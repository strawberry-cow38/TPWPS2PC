using Godot;

namespace TPWPS2Viewer;

public sealed partial class LaptopShopScreen
{
    bool _captureRowDraws;
    /// <summary>Opt-in audit receipts from the actual draw sites, in viewport pixels. Off by default;
    /// this does not change presentation or input. Receipts are geometry/data evidence, not proof that
    /// a human inspected pixels or that a glyph survived occlusion.</summary>
    public bool CaptureRowDraws
    {
        get => _captureRowDraws;
        set { if (_captureRowDraws != value) _rowDraws.Clear(); _captureRowDraws = value; }
    }
    public readonly record struct RowDraw(int TextId, string CellText, int CellFraction,
                                         string Text = null, int Fraction = 0, Vector2? LabelAt = null,
                                         Vector2? ValueAt = null, Rect2? WidgetBox = null, Rect2? ArrowBox = null);
    readonly Dictionary<int, RowDraw> _rowDraws = new();
    public IReadOnlyDictionary<int, RowDraw> RowsDrawn => _rowDraws;

    void TraceRow(int row, int textId, string text, int fraction)
    { if (CaptureRowDraws) _rowDraws[row] = new RowDraw(textId, text, fraction); }
    void TraceLabel(int row, Vector2 at)
    { if (CaptureRowDraws) _rowDraws[row] = _rowDraws[row] with { LabelAt = at }; }
    void TraceValue(int row, string text, Vector2 at)
    { if (CaptureRowDraws) _rowDraws[row] = _rowDraws[row] with { Text = text, ValueAt = at }; }
    void TraceWidget(int row, Rect2 box, int fraction)
    { if (CaptureRowDraws) _rowDraws[row] = _rowDraws[row] with { WidgetBox = box, Fraction = fraction }; }
    void TraceArrows(int row, Rect2 box)
    { if (CaptureRowDraws) _rowDraws[row] = _rowDraws[row] with { ArrowBox = box }; }
    void DrawRowValue(int row, string text, Vector2 at, float scale, Color tint, string justify)
    { TraceValue(row, text, at); DrawRun(text, at, scale, tint, justify); }
    void DrawRowBar(int row, Rect2 box, int fraction, float scale, Color? tint)
    { TraceWidget(row, box, fraction); DrawBar(box, fraction, scale, tint); }
    void DrawRowSlider(int row, Rect2 box, int fraction, float scale, bool selected)
    { TraceWidget(row, box, fraction); DrawSlider(box, fraction, scale, selected); }
}