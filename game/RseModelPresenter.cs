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
    public Aps.Record Record { get; private set; }
    public float Frame { get; private set; }
    public RseModelPresenter(Node3D parent, Model model, Aps animation,
        Func<string, (ImageTexture Tex, bool Soft)> texture)
    { _parent = parent; _model = model; _animation = animation; _texture = texture; }

    public void Update(RsePreviewHost host)
    {
        var record = host.Current?.Record;
        if (Drawn == null || Record != record)
        {
            Drawn?.Root.Free();
            Record = record;
            Drawn = new AnimatedModel(_model, _animation, record, _texture);
            _parent.AddChild(Drawn.Root);
        }
        Frame = host.Frame;
        Drawn.SetFrame(Frame);
    }
    public void Dispose() { Drawn?.Root.Free(); Drawn = null; }
}
