using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

/// <summary>Actual bus model/controller adapter. It does not invent arrivals, sound graph
/// scheduling, catalogue variant selection, traffic gates or a clock source: those inputs
/// belong to the park integration. Kept separate from the unrelated Bus.RSE statuses.</summary>
public sealed class NativeBus
{
    public AnimatedModel Model { get; }
    public NativeBusController Controller { get; }
    public Node3D Root => Model.Root;

    public NativeBus(TPW.PS2.Data.Model model, Aps animation,
        Func<string,(ImageTexture Tex,bool Soft)> texture, uint initialRandomRaw, uint clock,
        Action<int,uint> stateCommand, Action<int> arrivalBatch)
    {
        Model = new AnimatedModel(model, animation, null, texture, nativeNodeVisibility: true);
        Model.SetFrame(0); // instantiated source pose, with native own-node visibility
        Controller = new NativeBusController(animation, initialRandomRaw, clock,
            Model.ActivateNativeRecord, Model.SetFrame, stateCommand, arrivalBatch);
    }

    public int Update(uint activeMilliseconds, int countdownDelta, int traffic,
        bool open, bool specialObjectAbsent, int flaggedGuestCount) =>
        Controller.Update(activeMilliseconds,countdownDelta,traffic,open,specialObjectAbsent,flaggedGuestCount);
}
