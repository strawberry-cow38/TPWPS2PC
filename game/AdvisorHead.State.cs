using Godot;
using System.Text.Json.Serialization;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

public sealed partial class AdvisorHead
{
    /// <summary>Trusted registry bindings, not paths interpreted from the save. Texture must be
    /// a side-effect-free asset resolver; staging never invokes advisor callbacks.</summary>
    public sealed class SnapshotBindings
    {
        public required AnimatedModel.SnapshotBindings Drawn { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot
    {
        public required int Version { get; init; }
        public required int Dressed { get; init; }
        public required int MouthShown { get; init; }
        public required int Bound { get; init; }
        public required AdvisorHeadChannel.Snapshot Channel { get; init; }
        public required AnimatedModel.Snapshot Drawn { get; init; }
        public required int ViewWidth { get; init; }
        public required int ViewHeight { get; init; }
        public required int ViewUpdateMode { get; init; }
        public required bool OverlayVisible { get; init; }
        public required float[] PivotTransform { get; init; }
        public required float[] RootAdjustment { get; init; }
        public required float[] CameraTransform { get; init; }
        public required float CameraSize { get; init; }
        public required float CameraNear { get; init; }
        public required float CameraFar { get; init; }
    }

    static float[] StatePack(Transform3D t) => new[] { t.Basis.X.X,t.Basis.X.Y,t.Basis.X.Z,
        t.Basis.Y.X,t.Basis.Y.Y,t.Basis.Y.Z,t.Basis.Z.X,t.Basis.Z.Y,t.Basis.Z.Z,t.Origin.X,t.Origin.Y,t.Origin.Z };
    static Transform3D StateTransform(float[] a) => new(new Basis(new(a[0],a[1],a[2]),
        new(a[3],a[4],a[5]),new(a[6],a[7],a[8])),new(a[9],a[10],a[11]));
    static void StateRequire(bool ok, string why) { if (!ok) throw new InvalidDataException("AdvisorHead state: " + why); }

    public Snapshot CaptureState(string modelAssetId, string animationAssetId)
    {
        StateRequire(_camera.Projection == Camera3D.ProjectionType.Orthogonal &&
            _camera.KeepAspect == Camera3D.KeepAspectEnum.Height, "external camera mode unsupported");
        return new() { Version=1, Dressed=Dressed, MouthShown=MouthShown, Bound=_bound,
            Channel=Channel.CaptureState(), Drawn=Drawn.CaptureState(modelAssetId,animationAssetId),
            ViewWidth=_view.Size.X, ViewHeight=_view.Size.Y, ViewUpdateMode=(int)_view.RenderTargetUpdateMode,
            OverlayVisible=Overlay.Visible, PivotTransform=StatePack(_pivot.Transform),
            RootAdjustment=StatePack(new Transform3D(_rootRescale,_rootBindOrigin)),
            CameraTransform=StatePack(_camera.Transform), CameraSize=_camera.Size, CameraNear=_camera.Near, CameraFar=_camera.Far };
    }

    /// <summary>Detached overlay, private writable renderer model. No Play/Dress/Mouth/Step/Present
    /// or frame sampling. Caller attaches Overlay only after committing the complete loaded world.
    /// Captures stock head presentation, not arbitrary external UI/material/camera-mode edits.</summary>
    public static AdvisorHead FromState(Snapshot s, SnapshotBindings b)
    {
        ArgumentNullException.ThrowIfNull(s); ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(b.Drawn); ArgumentNullException.ThrowIfNull(b.Drawn.Animation);
        StateRequire(!b.Drawn.ShareReadOnlyModel,"head requires private writable model");
        StateRequire(s.Version==1 && s.Dressed is >= -1 and <= 255 && s.MouthShown is >= -1 and <= 4, "version/selectors");
        StateRequire(s.Bound is >= -1 and <= 14 && s.Channel != null && s.Drawn != null, "bindings/channel");
        StateRequire(s.ViewWidth is >= 1 and <= 16384 && s.ViewHeight is >= 1 and <= 16384 &&
            Enum.IsDefined((SubViewport.UpdateMode)s.ViewUpdateMode), "viewport bounds");
        foreach(var a in new[]{s.PivotTransform,s.RootAdjustment,s.CameraTransform})
            StateRequire(a != null && a.Length==12 && a.All(float.IsFinite), "transform");
        StateRequire(float.IsFinite(s.CameraSize) && s.CameraSize>0 && s.CameraSize<=1e6f &&
            float.IsFinite(s.CameraNear) && float.IsFinite(s.CameraFar) && s.CameraNear>0 &&
            s.CameraFar>s.CameraNear && s.CameraFar<=1e6f, "camera bounds");
        // Validate channel before allocating any Godot nodes. No length-dependent replay.
        var channel = new AdvisorHeadChannel(_=>0); channel.RestoreState(s.Channel);
        var renderer = AnimatedModel.FromState(s.Drawn,b.Drawn);
        AdvisorHead head = null;
        try {
            head = new AdvisorHead(b.Drawn.Model,b.Drawn.Animation,b.Drawn.Texture,renderer,s);
            head.Channel.RestoreState(s.Channel); head._bound=s.Bound;
            head.Dressed=s.Dressed; head.MouthShown=s.MouthShown;
            head._view.Size=new(s.ViewWidth,s.ViewHeight);
            head._view.RenderTargetUpdateMode=(SubViewport.UpdateMode)s.ViewUpdateMode;
            head.Overlay.Visible=s.OverlayVisible; head._pivot.Transform=StateTransform(s.PivotTransform);
            head._camera.Transform=StateTransform(s.CameraTransform); head._camera.Size=s.CameraSize;
            head._camera.Near=s.CameraNear; head._camera.Far=s.CameraFar;
            return head;
        } catch { if(head != null) head.Overlay.Free(); else renderer.Root.Free(); throw; }
    }
}
