using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

public sealed partial class AnimatedModel
{
    readonly string _assetFingerprint;
    bool _sharedReadOnlyModel;
    readonly Dictionary<int, uint> _originalFlags = new();
    static string StateHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    static string RenderMode => $"{Compose}/{BakedOff}/{CullMode}";

    public sealed class SnapshotBindings
    {
        public required string ModelAssetId { get; init; }
        public required string AnimationAssetId { get; init; } // empty only for no animation
        public required Model Model { get; init; } // immutable external asset, NEVER written
        public required Aps Animation { get; init; }
        public required Func<string, (ImageTexture Tex, bool Soft)> Texture { get; init; }
        public bool ShareReadOnlyModel {get;init;}
        public string UvRewriteId {get;init;}
        public Action<Model.Mesh,IReadOnlyList<System.Numerics.Vector3>,List<Godot.Vector2>> UvRewrite {get;init;}
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PartSnapshot
    {
        public required int Node { get; init; }
        public required float[] Positions { get; init; }
        public required float[] Uvs { get; init; }
        public required float[] LayerPositions { get; init; } // null means absent
        public required float[] LayerUvs { get; init; }
        public required float[][] Transforms { get; init; } // includes externally forced matrices
        public required bool[] Visible { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot
    {
        public required int Version { get; init; }
        public required string UvRewriteId {get;init;}
        public required string ModelAssetId { get; init; }
        public required string AnimationAssetId { get; init; }
        public required string ModelFingerprint { get; init; }
        public required string AnimationFingerprint { get; init; }
        public required string RenderConfiguration { get; init; }
        public required int RecordOffset { get; init; } // -1: bind only
        public required bool NativeVisibility { get; init; }
        public required bool SkeletalHideLists { get; init; }
        public required bool OrdinaryVisibility { get; init; }
        public required float Frame { get; init; }
        public required bool Posed { get; init; }
        public required float[] Pose { get; init; }
        public required int[] HiddenNodes { get; init; }
        public required int[] TextureChoices { get; init; }
        public required int[] FlagNodes { get; init; }
        public required uint[] NodeFlags { get; init; }
        public required int[] WorldNodes { get; init; } // null: no WorldAt yet
        public required float[] WorldMatrices { get; init; }
        public required int[] OverriddenNodes { get; init; }
        public required PartSnapshot[] Parts { get; init; }
        public required float[] RootTransform { get; init; }
        public required bool RootVisible { get; init; }
        public required bool RootTopLevel { get; init; }
    }

    static float[] Pack(Transform3D t) => new[] {t.Basis.X.X,t.Basis.X.Y,t.Basis.X.Z,t.Basis.Y.X,t.Basis.Y.Y,t.Basis.Y.Z,
        t.Basis.Z.X,t.Basis.Z.Y,t.Basis.Z.Z,t.Origin.X,t.Origin.Y,t.Origin.Z};
    static Transform3D Transform(float[] v) => new(new Basis(new(v[0],v[1],v[2]),new(v[3],v[4],v[5]),new(v[6],v[7],v[8])),new(v[9],v[10],v[11]));
    static float[] Pack(Matrix4x4 m) => new[] {m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
    static Matrix4x4 Matrix(float[] v, int i) => new(v[i],v[i+1],v[i+2],v[i+3],v[i+4],v[i+5],v[i+6],v[i+7],v[i+8],v[i+9],v[i+10],v[i+11],v[i+12],v[i+13],v[i+14],v[i+15]);
    static float[] Pack(IEnumerable<System.Numerics.Vector3> v) => v?.SelectMany(x=>new[]{x.X,x.Y,x.Z}).ToArray();
    static float[] Pack(IEnumerable<Godot.Vector2> v) => v?.SelectMany(x=>new[]{x.X,x.Y}).ToArray();
    static List<System.Numerics.Vector3> Positions(float[] v) => v == null ? null : Enumerable.Range(0,v.Length/3).Select(i=>new System.Numerics.Vector3(v[3*i],v[3*i+1],v[3*i+2])).ToList();
    static List<Godot.Vector2> Uvs(float[] v) => v == null ? null : Enumerable.Range(0,v.Length/2).Select(i=>new Godot.Vector2(v[2*i],v[2*i+1])).ToList();
    static void Require(bool ok, string why) { if (!ok) throw new InvalidDataException("AnimatedModel state: " + why); }
    static void Floats(float[] v, int length) => Require(v != null && v.Length == length && v.All(float.IsFinite), "invalid finite array");
    static void Id(string id) => Require(!string.IsNullOrWhiteSpace(id) && id.Length <= 1024, "invalid asset ID");

    /// <summary>Copies retained CPU render inputs, including unsampled record switches and hidden
    /// parts whose last pose predates Frame. No SetFrame, effects, sounds, or simulation calls.</summary>
    public Snapshot CaptureState(string modelAssetId, string animationAssetId, string uvRewriteId = null)
    {
        Id(modelAssetId); if (_anim != null) Id(animationAssetId); else Require(animationAssetId == "", "no-animation ID must be empty");
        Require(!_model.IsLegacyMd2, "legacy MD2 unsupported");
        Require((UvRewrite==null)==(uvRewriteId==null),"UV rewrite requires explicit provider binding");
        if(uvRewriteId!=null)Id(uvRewriteId);
        Require(Root.GetChildCount() == _parts.Sum(p=>p.Surfaces.Length), "extra root children unsupported");
        foreach(var p in _parts)
            for(int i=0;i<p.Surfaces.Length;i++)
                Require(p.Surfaces[i].GetParent()==Root && p.Surfaces[i].Mesh==p.EmittedMeshes[i] &&
                    p.Surfaces[i].MaterialOverride==_materials[p.SurfaceMaterial[i]], "external mesh/material/topology edits unsupported");
        var original = (byte[])_model.D.Clone();
        foreach (var (node, flags) in _originalFlags) BitConverter.TryWriteBytes(original.AsSpan(_model.NodeOffset(node),4),flags);
        Require(StateHash(original) == _assetFingerprint, "untracked mutable model bytes");
        var world = LastWorld?.OrderBy(x=>x.Key).ToArray();
        var flagsNodes = _originalFlags.Keys.OrderBy(x=>x).ToArray();
        return new Snapshot {
            Version=1, UvRewriteId=uvRewriteId, ModelAssetId=modelAssetId, AnimationAssetId=animationAssetId,
            ModelFingerprint=_assetFingerprint, AnimationFingerprint=_anim == null ? "" : StateHash(_anim.D), RenderConfiguration=RenderMode,
            RecordOffset=Record?.Offset ?? -1, NativeVisibility=_nativeNodeVisibility, SkeletalHideLists=_skeletalHideLists,
            OrdinaryVisibility=_ordinaryVisibility, Frame=_now, Posed=_posed, Pose=_poseScratch?.SelectMany(Pack).ToArray(),
            HiddenNodes=CaptureHiddenNodes(), TextureChoices=(int[])_textureIndices.Clone(), FlagNodes=flagsNodes,
            NodeFlags=flagsNodes.Select(n=>BitConverter.ToUInt32(_model.D,_model.NodeOffset(n))).ToArray(),
            WorldNodes=world?.Select(x=>x.Key).ToArray(), WorldMatrices=world?.SelectMany(x=>Pack(x.Value)).ToArray(),
            OverriddenNodes=OverriddenNodes.OrderBy(x=>x).ToArray(),
            Parts=_parts.Select(p=>new PartSnapshot {Node=p.NodeOffset,Positions=Pack(p.LivePos),Uvs=Pack(p.LiveUv),
                LayerPositions=Pack(p.LayerPos),LayerUvs=Pack(p.LayerUv),Transforms=p.Surfaces.Select(s=>Pack(s.Transform)).ToArray(),
                Visible=p.Surfaces.Select(s=>s.Visible).ToArray()}).ToArray(),
            RootTransform=Pack(Root.Transform), RootVisible=Root.Visible, RootTopLevel=Root.TopLevel
        };
    }

    /// <summary>Fresh detached renderer with a private Model (or explicitly shared read-only asset). Does not
    /// sample any frame. CPU geometry is uploaded directly; the next ordinary SetFrame continues
    /// using rebound channels. Coordinator owns world registry, callback values and shader clock.
    /// External mesh/material edits and arbitrary extra Root children are not supported.</summary>
    public static AnimatedModel FromState(Snapshot s, SnapshotBindings b)
    {
        ArgumentNullException.ThrowIfNull(s); ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(b.Model); ArgumentNullException.ThrowIfNull(b.Texture);
        Id(s.ModelAssetId); if (b.Animation != null) Id(s.AnimationAssetId);
        Require(s.Version == 1 && !b.Model.IsLegacyMd2 && s.RenderConfiguration == RenderMode, "version/model/render mode");
        Require(b.ModelAssetId == s.ModelAssetId && b.AnimationAssetId == s.AnimationAssetId &&
            StateHash(b.Model.D) == s.ModelFingerprint && (b.Animation == null ? "" : StateHash(b.Animation.D)) == s.AnimationFingerprint, "asset binding mismatch");
        Require(s.UvRewriteId==b.UvRewriteId && (s.UvRewriteId==null)==(b.UvRewrite==null),"UV rewrite provider mismatch");
        if(s.UvRewriteId!=null)Id(s.UvRewriteId);
        int count=b.Model.Meshes.Count+b.Model.HelperCount;
        Require(count <= 65536, "node bound");
        void Nodes(int[] a) => Require(a != null && a.Length <= count && a.Distinct().Count()==a.Length && a.All(n=>n>=0&&n<count), "node indices");
        Nodes(s.HiddenNodes); Nodes(s.FlagNodes); Nodes(s.OverriddenNodes);
        Require(s.NodeFlags != null && s.NodeFlags.Length == s.FlagNodes.Length, "node flags");
        Require(float.IsFinite(s.Frame) && s.Frame >= 0 && s.Frame <= 1e9f, "frame");
        Floats(s.RootTransform,12);
        if(s.Pose != null) Floats(s.Pose,b.Model.HelperCount*16);
        Require(!s.Posed || s.Pose != null, "missing pose");
        Require((s.WorldNodes==null)==(s.WorldMatrices==null), "world null pairing");
        if(s.WorldNodes != null) {
            Require(s.WorldNodes.Length==count && s.WorldNodes.Distinct().Count()==count && s.WorldNodes.All(b.Model.BindLocals.ContainsKey), "world offsets");
            Floats(s.WorldMatrices,count*16);
        }
        Require(s.TextureChoices != null && s.TextureChoices.Length==b.Model.MaterialTextures.Count, "texture slots");
        for(int i=0;i<s.TextureChoices.Length;i++) Require(s.TextureChoices[i]>=0 && s.TextureChoices[i]<Math.Max(1,b.Model.MaterialTextures[i].Length), "texture index");
        var meshes=b.Model.Meshes.Where(m=>b.Model.Triangles(m).Count>0).ToArray();
        Require(s.Parts != null && s.Parts.Length == meshes.Length, "parts");
        long total=0;
        for(int i=0;i<meshes.Length;i++) {
            var p=s.Parts[i];var mesh=meshes[i];int vertices=b.Model.Vertices(mesh).Item1.Count;
            total+=vertices;Require(total<=2000000 && p!=null && p.Node==mesh.Offset,"part/vertex bound");
            Floats(p.Positions,vertices*3);Floats(p.Uvs,vertices*2);
            if(p.LayerPositions!=null) Floats(p.LayerPositions,vertices*3);
            if(p.LayerUvs!=null) Floats(p.LayerUvs,vertices*2);
            int surfaces=b.Model.Triangles(mesh).Select(t=>t.Material).Distinct().Count();
            Require(p.Transforms!=null&&p.Transforms.Length==surfaces&&p.Visible!=null&&p.Visible.Length==surfaces,"surfaces");
            foreach(var t in p.Transforms) Floats(t,12);
        }
        Aps.Record rec=null;
        if(s.RecordOffset!=-1) {
            rec=b.Animation?.Records().SingleOrDefault(r=>r.Offset==s.RecordOffset);
            Require(rec!=null,"record not in asset");
        }
        Require(!b.ShareReadOnlyModel || s.FlagNodes.Length==0,"shared character asset cannot receive private flags");
        var model=b.ShareReadOnlyModel?b.Model:new Model((byte[])b.Model.D.Clone());
        var result=new AnimatedModel(model,b.Animation,rec,b.Texture,s.NativeVisibility,s.SkeletalHideLists);
        try {
            result._sharedReadOnlyModel=b.ShareReadOnlyModel;
            result.UvRewrite=b.UvRewrite;
            // Only private bytes, never BindWorld/BindLocals/BindParents or the resolver's asset.
            for(int i=0;i<s.FlagNodes.Length;i++) result.WriteNodeFlags(s.FlagNodes[i],s.NodeFlags[i],uint.MaxValue);
            result._ordinaryVisibility=s.OrdinaryVisibility;
            result.RestoreHiddenNodes((int[])s.HiddenNodes.Clone());
            result._now=s.Frame;result._posed=s.Posed;
            result._poseScratch=s.Pose==null?null:Enumerable.Range(0,s.Pose.Length/16).Select(i=>Matrix(s.Pose,i*16)).ToArray();
            result.LastWorld=s.WorldNodes==null?null:s.WorldNodes.Select((n,i)=>(n,i)).ToDictionary(x=>x.n,x=>Matrix(s.WorldMatrices,x.i*16));
            result.OverriddenNodes=new HashSet<int>(s.OverriddenNodes);
            for(int i=0;i<s.TextureChoices.Length;i++) {
                int choice=s.TextureChoices[i];
                if(result._materials.TryGetValue(i,out var mat) && choice!=result._textureIndices[i]) result.SetTexture(mat,i,choice);
                result._textureIndices[i]=choice;
            }
            for(int i=0;i<s.Parts.Length;i++) {
                var p=result._parts[i];var saved=s.Parts[i];
                p.LayerPos=Positions(saved.LayerPositions)?.ToArray();p.LayerUv=Uvs(saved.LayerUvs)?.ToArray();
                result.EmitGeometry(p,Positions(saved.Positions),Uvs(saved.Uvs));
                for(int j=0;j<p.Surfaces.Length;j++) {p.Surfaces[j].Transform=Transform(saved.Transforms[j]);p.Surfaces[j].Visible=saved.Visible[j];}
            }
            result.Root.TopLevel=s.RootTopLevel;result.Root.Transform=Transform(s.RootTransform);result.Root.Visible=s.RootVisible;
            return result;
        } catch { result.Root.Free();throw; }
    }
}
