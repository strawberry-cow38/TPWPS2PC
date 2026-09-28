using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

/// <summary>Actual bus model/controller adapter. It does not invent arrivals, sound graph
/// scheduling, catalogue variant selection, traffic gates or a clock source: those inputs
/// belong to the park integration. Kept separate from the unrelated Bus.RSE statuses.</summary>
public sealed partial class NativeBus
{
    public AnimatedModel Model { get; }
    public NativeBusController Controller { get; }
    public Node3D Root => Model.Root;
    readonly TPW.PS2.Data.Model sourceModel;
    readonly Aps sourceAnimation;
    readonly string originalModelFingerprint;
    float sampledFrame;
    bool sampledCurrentRecord = true;

    void Bind(Aps.Record record)
    {
        Model.ActivateNativeRecord(record);
        sampledCurrentRecord = false;
    }

    void Sample(float frame)
    {
        Model.SetFrame(frame);
        sampledFrame = frame;
        sampledCurrentRecord = true;
    }

    public NativeBus(TPW.PS2.Data.Model model, Aps animation,
        Func<string,(ImageTexture Tex,bool Soft)> texture, uint initialRandomRaw, uint clock,
        Action<int,uint> stateCommand, Action<int> arrivalBatch)
    {
        sourceModel = model; sourceAnimation = animation;
        originalModelFingerprint = Fingerprint(model.D);
        Model = new AnimatedModel(model, animation, null, texture, nativeNodeVisibility: true);
        Model.SetFrame(0); // instantiated source pose, with native own-node visibility
        Controller = new NativeBusController(animation, initialRandomRaw, clock,
            Bind, Sample, stateCommand, arrivalBatch);
    }

    /// <summary>Sample between park ticks using the same authored curve, facing, wheels and
    /// visibility channel. No matrix blending across records and no simulation advancement.</summary>
    public void Present(uint activeMilliseconds, float fractionalMilliseconds = 0)
    {
        if (Controller.Active)
            Sample(Controller.PresentationFrame(activeMilliseconds, fractionalMilliseconds));
    }

    public int Update(uint activeMilliseconds, int countdownDelta, int traffic,
        bool open, bool specialObjectAbsent, int flaggedGuestCount) =>
        Controller.Update(activeMilliseconds,countdownDelta,traffic,open,specialObjectAbsent,flaggedGuestCount);
}
