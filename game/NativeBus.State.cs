using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

public sealed partial class NativeBus
{
    // IDs are resolved by the coordinator, never by paths embedded in a save file.
    public sealed class SnapshotBindings
    {
        public required string ModelAssetId { get; init; }
        public required string AnimationAssetId { get; init; }
        public required string ServicesId { get; init; }
        public required TPW.PS2.Data.Model Model { get; init; }
        public required Aps Animation { get; init; }
        public required Func<string, (ImageTexture Tex, bool Soft)> Texture { get; init; }
        public required Action<int, uint> StateCommand { get; init; }
        public required Action<int> ArrivalBatch { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot
    {
        public required int Version { get; init; }
        public required string ModelAssetId { get; init; }
        public required string AnimationAssetId { get; init; }
        public required string ModelFingerprint { get; init; }
        public required NativeBusController.Snapshot Controller { get; init; }
        // Basis columns then origin: JSON does not depend on Godot struct serializers.
        public required float[] RootTransform { get; init; }
        public required bool Visible { get; init; }
        public required bool TopLevel { get; init; }
        public required float PresentationFrame { get; init; }
        public required int[] HiddenNodes {get;init;}
    }

    static string Fingerprint(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    static void Identity(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length>1024) throw new InvalidDataException("missing bus binding identity");
    }

    // Deliberately bounded: retained texture history still needs an AnimatedModel owner.
    // Hidden flags ARE explicit state below; do not infer them from predecessor records.
    static void ValidateDerivedPose(TPW.PS2.Data.Model model, Aps animation)
    {
        var section = animation.Sections()[5];
        var records = Enumerable.Range(0, section.Count)
            .Select(i => animation.ReadRecord(section.Offset + i * 0x1c)).ToArray();
        if (records.Any(r => r.Skeletal || r.Shared || r.IndexCount != 0 || animation.TextureTracks(r).Count != 0))
            throw new InvalidDataException("bus pose requires ordinary records without retained texture state");

    }

    public Snapshot CaptureState(string modelAssetId, string animationAssetId, string servicesId)
    {
        Identity(modelAssetId); Identity(animationAssetId);
        if (!sampledCurrentRecord)
            throw new InvalidDataException("bus cut immediately after bind retains the previous pose; sample via Present before capture");
        if (Fingerprint(sourceModel.D) != originalModelFingerprint)
            throw new InvalidDataException("bus model was mutated; native node flag state is not supported");
        var controller = Controller.CaptureState(servicesId);
        ValidateDerivedPose(sourceModel, sourceAnimation);
        if (!ReferenceEquals(Model.Record, Controller.Record))
            throw new InvalidDataException("bus model record was changed outside its adapter");
        var t = Root.Transform;
        var s = new Snapshot { Version = 1, ModelAssetId = modelAssetId, AnimationAssetId = animationAssetId,
            ModelFingerprint = originalModelFingerprint, Controller = controller,
            RootTransform = new[] { t.Basis.X.X,t.Basis.X.Y,t.Basis.X.Z,t.Basis.Y.X,t.Basis.Y.Y,t.Basis.Y.Z,
                t.Basis.Z.X,t.Basis.Z.Y,t.Basis.Z.Z,t.Origin.X,t.Origin.Y,t.Origin.Z },
            Visible = Root.Visible, TopLevel = Root.TopLevel, PresentationFrame = sampledFrame,HiddenNodes=Model.CaptureHiddenNodes() };
        ValidatePresentation(s, sourceAnimation);
        return s;
    }

    static void ValidatePresentation(Snapshot s, Aps animation)
    {
        if (s.Version != 1 || s.RootTransform == null || s.RootTransform.Length != 12 ||
            s.RootTransform.Any(f => !float.IsFinite(f)) || !float.IsFinite(s.PresentationFrame) || s.PresentationFrame < 0)
            throw new InvalidDataException("invalid bus presentation");
        int variant = s.Controller.RecordVariant;
        float max = variant < 0 ? 0 : animation.ReadRecord(animation.Sections()[5].Offset + variant * 0x1c).DurationFrames;
        if (s.PresentationFrame > max) throw new InvalidDataException("bus presentation frame outside record");
    }

    /// <summary>Build a detached staging root. No gameplay constructor, initial RNG,
    /// controller Bind/Sample, state command or batch replay. Caller owns publishing/freeing
    /// the returned root, and must not publish until the entire park graph is accepted.</summary>
    public static NativeBus FromState(Snapshot s, SnapshotBindings b)
    {
        ArgumentNullException.ThrowIfNull(s); ArgumentNullException.ThrowIfNull(b);
        Identity(s.ModelAssetId); Identity(s.AnimationAssetId); Identity(b.ServicesId);
        ArgumentNullException.ThrowIfNull(b.Model); ArgumentNullException.ThrowIfNull(b.Animation);
        ArgumentNullException.ThrowIfNull(b.Texture); ArgumentNullException.ThrowIfNull(b.StateCommand);
        ArgumentNullException.ThrowIfNull(b.ArrivalBatch);
        if (b.ModelAssetId != s.ModelAssetId || b.AnimationAssetId != s.AnimationAssetId ||
            s.Controller == null || b.ServicesId != s.Controller.ServicesId || Fingerprint(b.Model.D) != s.ModelFingerprint)
            throw new InvalidDataException("bus asset/service binding mismatch");
        NativeBusController.ValidateState(s.Controller, b.Animation);
        ValidatePresentation(s, b.Animation);
        ValidateDerivedPose(b.Model, b.Animation);
        int count=b.Model.Meshes.Count+b.Model.HelperCount;
        if(s.HiddenNodes==null||s.HiddenNodes.Length>count||s.HiddenNodes.Any(n=>n<0||n>=count)||s.HiddenNodes.Distinct().Count()!=s.HiddenNodes.Length)
            throw new InvalidDataException("invalid bus retained visibility");
        return new NativeBus(s, b);
    }

    NativeBus(Snapshot s, SnapshotBindings b)
    {
        sourceModel = b.Model; sourceAnimation = b.Animation; originalModelFingerprint = s.ModelFingerprint;
        Model = new AnimatedModel(b.Model, b.Animation, null, b.Texture, nativeNodeVisibility: true);
        try
        {
            Controller = NativeBusController.FromState(s.Controller, b.Animation, new NativeBusController.SnapshotBindings {
                ServicesId = b.ServicesId, Bind = Bind, Sample = Sample,
                StateCommand = b.StateCommand, RequestBatch = b.ArrivalBatch });
            // Explicit pose rebuild, not controller activation. Accepted local staging only.
            if (Controller.Active) Model.UseRecord(Controller.Record);
            Model.SetFrame(s.PresentationFrame);
            // Restore the ACTUAL retained state. No guessed predecessor playback history.
            Model.RestoreHiddenNodes(s.HiddenNodes);
            sampledFrame = s.PresentationFrame; sampledCurrentRecord = true;
            var v = s.RootTransform;
            Root.TopLevel = s.TopLevel;
            Root.Transform = new Transform3D(new Basis(new(v[0],v[1],v[2]),new(v[3],v[4],v[5]),new(v[6],v[7],v[8])),new(v[9],v[10],v[11]));
            Root.Visible = s.Visible;
        }
        catch { Root.Free(); throw; }
    }
}
