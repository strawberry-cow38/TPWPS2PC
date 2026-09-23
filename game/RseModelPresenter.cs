using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

/// <summary>The same binding used by the ride preview and its headless geometry audit.</summary>
public sealed class RseModelPresenter : IDisposable
{
    readonly Node3D _parent;
    readonly Model _model;
    readonly Aps _animation;
    readonly Func<string, (ImageTexture Tex, bool Soft)> _texture;
    public AnimatedModel Drawn { get; private set; }
    /// <summary>The mesh behind the drawing, for a caller that needs the node table --
    /// the RSE's EVENT instructions name nodes by index and the world matrices are keyed
    /// by file offset.</summary>
    public Model Model => _model;
    public Aps.Record Record { get; private set; }
    public float Frame { get; private set; }
    public RseModelPresenter(Node3D parent, Model model, Aps animation,
        Func<string, (ImageTexture Tex, bool Soft)> texture)
    { _parent = parent; _model = model; _animation = animation; _texture = texture; }

    /// <summary>⭐⭐ THE MODEL IS KEPT, NOT REBUILT. This used to `Free()` the whole AnimatedModel
    /// and construct a new one every time the script changed animation, which threw away every
    /// piece of state the console carries ACROSS a change: the texture each material is showing
    /// (retained at `0x1a6b60`) and, worse, which nodes are hidden (`0x1a7f48` sets flag `0x10` on
    /// the NODE, and a record with no visibility track for it touches nothing).
    ///
    /// ⚠ That is why Crazy Ape's smashed crate and its shards came back the moment the ride left
    /// its Create animation: rebuilt model, blank state, everything visible. `UseRecord` re-points
    /// the channels in place and leaves the state alone, which is what the PS2 does.</summary>
    public void Update(RsePreviewHost host)
    {
        var record = host.Current?.Record;
        if (Drawn == null)
        {
            Record = record;
            Drawn = new AnimatedModel(_model, _animation, record, _texture);
            _parent.AddChild(Drawn.Root);
        }
        else if (Record != record)
        {
            Record = record;
            Drawn.UseRecord(record);
        }
        Frame = host.Frame;
        Drawn.SetFrame(Frame);
    }
    public void Dispose() { Drawn?.Root.Free(); Drawn = null; }
}
