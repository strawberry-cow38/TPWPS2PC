using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using TPW.PS2.Data;
using NativeWater = TPW.PS2.Data.ProceduralParkWater;

namespace TPWPS2Viewer.Tests;

/// <summary>Declared component fixture: actual native profile/art and Godot upload/raster,
/// with explicit seed and an artificial underlay. NOT a player-path or emulator parity test.</summary>
public partial class ProceduralParkWaterAudit : Node3D
{
    int _checks, _bad;
    void Check(bool ok, string label)
    {
        _checks++;
        if (!ok) _bad++;
        if (ok) GD.Print($"PROCEDURAL PARK WATER ok: [{_checks}] {label}");
        else GD.PrintErr($"PROCEDURAL PARK WATER FAIL: [{_checks}] {label}");
    }

    public override void _Ready() => CallDeferred(nameof(Start));

    async void Start()
    {
        ProceduralParkWaterView view = null;
        ImageTexture texture = null;
        Camera3D camera = null;
        MeshInstance3D underlay = null;
        try
        {
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            string disc = args.FirstOrDefault(a => a.StartsWith("--disc="))?[7..]
                ?? System.Environment.GetEnvironmentVariable("TPW_PS2_DISC");
            if (string.IsNullOrEmpty(disc)) throw new InvalidOperationException("--disc required");
            using var library = new AssetLibrary(disc);
            library.OpenWad("/DATA/JUNGLE.WAD");
            var profile = NativeWater.Profile.Read(library.Disc);
            Ps2Materials.Lighting = Lighting.Read(library.Disc);
            var source = ProceduralParkWaterView.ReadTexture(library);
            Check(source.SourceWad.EndsWith("DATA.WAD", StringComparison.OrdinalIgnoreCase), "global DATA art");
            Check(source.SourcePath.Contains("/Generic/extra/justwater.", StringComparison.OrdinalIgnoreCase), "actual bound texture");
            Check(source.Width == 64 && source.Height == 64 && source.Translucent, "64x64 soft alpha art");
            int lo = 255, hi = 0;
            for (int i = 3; i < source.Pixels.Length; i += 4) { lo = Math.Min(lo, source.Pixels[i]); hi = Math.Max(hi, source.Pixels[i]); }
            Check(lo > 0 && hi < 250 && lo < hi, $"source alpha {lo}..{hi}, not opaque");
            using var image = Image.CreateFromData(source.Width, source.Height, false, Image.Format.Rgba8, source.Pixels);
            image.GenerateMipmaps();
            texture = ImageTexture.CreateFromImage(image);
            uint seed = 1;
            var noise = NativeWater.NativeNoise.Generate(ref seed);
            var state = new NativeWater.State(profile, 10.25f, 4096);
            view = new ProceduralParkWaterView();
            AddChild(view);
            view.Initialize(noise, state, NativeWater.World.Jungle, 1, texture);
            Check(view.Dimension == 16 && view.Positions.Length == 264, "max-detail native grid plus eight skirts");
            Check(view.Surface.Mesh.GetSurfaceCount() == 1, "single indexed dynamic surface");
            Check(view.Material.Shader.Code.Contains("ALPHA = c.a;"), "alpha-enabled PS2 material");
            Check(view.Material.GetShaderParameter("albedo_tex").AsGodotObject() == texture, "art reaches actual material");
            var mesh = (ArrayMesh)view.Surface.Mesh;
            var first = mesh.SurfaceGetArrays(0);
            var oldVertices = first[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var oldUvs = first[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var rawNormals = first[(int)Mesh.ArrayType.Custom0].AsFloat32Array();
            Check(rawNormals.Length == 264 * 3 && rawNormals[0] == 0 && rawNormals[1] == 112 && rawNormals[2] == 0,
                  "native constant normals in CUSTOM0, not recomputed slopes");
            Check(Math.Abs(oldVertices[0].X - 17) < .0001 && Math.Abs(oldVertices[0].Z - 15) < .0001,
                  "actual mirrored native front corner");
            var rid = mesh.GetRid();
            int builds = view.SurfaceBuilds;
            var positionsArray = view.Positions;
            var uvArray = view.Uvs;
            view.Step(.04, true, null);
            await Frame();
            var updated = mesh.SurfaceGetArrays(0);
            var newVertices = updated[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var newUvs = updated[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            Check(state.UvAccumulator == 4156 && Math.Abs(state.Phase - (10.25f + 40f / 317)) < .00001,
                  "gated 40ms reaches native state once");
            Check(view.Surface.Mesh == mesh && mesh.GetRid() == rid && view.SurfaceBuilds == builds
                && ReferenceEquals(positionsArray, view.Positions) && ReferenceEquals(uvArray, view.Uvs),
                  "mesh and geometry buffers reused without surface rebuild");
            Check(newVertices.Zip(oldVertices).Any(v => Math.Abs(v.First.Y - v.Second.Y) > .0001), "actual uploaded heights move");
            Check(newVertices.Select((v, i) => Math.Abs(v.X - oldVertices[i].X) < .00001 && Math.Abs(v.Z - oldVertices[i].Z) < .00001).All(v => v),
                  "no horizontal geometry drift");
            Check(Math.Abs(newUvs[0].Y - oldUvs[0].Y + 60f / 4096) < .00001, "actual uploaded V advances signed interval");
            Check(newVertices.Select((v, i) => Math.Abs(v.Y - view.Positions[i].Y) < .00001).All(v => v), "GPU-backed mesh matches core Y");
            int uploads = view.Uploads;
            float phase = state.Phase;
            long uv = state.UvAccumulator;
            view.Step(.1, false, null);
            Check(view.Uploads == uploads && state.Phase == phase && state.UvAccumulator == uv, "pause does not advance/reupload");
            view.Step(.009, true, null);
            Check(view.Uploads == uploads, "sub-10ms retains remainder");
            view.Step(.0011, true, null);
            Check(view.AdvancedMilliseconds == 50 && state.UvAccumulator == uv + 15, "10ms clock consumes remainder once");
            Check(ProceduralParkWaterView.SelectDimension(-1) == 16 && ProceduralParkWaterView.SelectDimension(0) == 16,
                  "negative/near clip-Z clamps to max detail");
            Check(ProceduralParkWaterView.SelectDimension(10) == 12 && ProceduralParkWaterView.SelectDimension(10.9f) == 11,
                  "LOD truncates only after half-depth and clamps");
            Check(ProceduralParkWaterView.SelectDimension(26) == 4 && ProceduralParkWaterView.SelectDimension(100) == 4,
                  "far detail clamps to four");
            long preHitch = state.UvAccumulator;
            view.Step(6, true, null);
            long afterHitch = unchecked(preHitch + 9000 - 4096);
            Check(state.UvAccumulator == afterHitch && afterHitch > 8192,
                  "long hitch performs only one native wrap");
            phase = state.Phase; uploads = view.Uploads;
            view.Step(0, false, null);
            Check(state.UvAccumulator == afterHitch - 4096 && state.Phase == phase && view.Uploads == uploads + 1,
                  "paused zero-delta draw recovers oversized UV and updates actual mesh");

            Check(Math.Abs(ProceduralParkWaterView.NativeClipZ(20, .75f, 500, 60) - 9.278918f) < .00002,
                  "native boot lens produces scaled pre-divide clip Z");
            Check(ProceduralParkWaterView.SelectDimension(ProceduralParkWaterView.NativeClipZ(20, .75f, 500, 60)) == 12
                && ProceduralParkWaterView.SelectDimension(20) == 7, "view depth is not native clip depth for LOD");
            var bounds = view.Bounds;
            var centre = new Vector3((bounds.MinX + bounds.MaxX) * .5f, 0, -(bounds.MinZ + bounds.MaxZ) * .5f);
            camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 16, Current = true,
                                        Position = centre + new Vector3(0, 12, 0), Near = .1f, Far = 200 };
            AddChild(camera);
            camera.LookAt(centre, Vector3.Back);
            view.Step(0, false, camera);
            float nativeClip = view.ClosestClipZ;
            camera.Near = .001f; camera.Far = 2000; camera.Fov = 120;
            view.Step(0, false, camera);
            Check(Math.Abs(nativeClip - 5.542728f) < .00002 && view.ClosestClipZ == nativeClip && view.Dimension == 14,
                  "actual LOD uses decoded native lens, not Godot projection or reverse Z");
            var underlayMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                                                           AlbedoColor = new Color(1, 0, 0) };
            underlay = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(100, 100) },
                                               MaterialOverride = underlayMaterial, Position = centre + new Vector3(0, -2, 0) };
            AddChild(underlay);
            await Frame(); await Frame();
            Color red = await Pixel();
            Check(red.B > .04f, "visible water over red underlay");
            underlayMaterial.AlbedoColor = new Color(0, 1, 0);
            await Frame(); await Frame();
            Color green = await Pixel();
            Check(green.B > .04f, "visible water over green underlay");
            Check(Math.Abs(green.R - red.R) + Math.Abs(green.G - red.G) > .2f,
                  "underlay survives through the actual alpha raster");
            GD.Print($"[water-raster] red={red} green={green}");
            string output = System.Environment.GetEnvironmentVariable("TPW_WATER_AUDIT_SHOT");
            if (!string.IsNullOrEmpty(output))
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var shot = GetViewport().GetTexture().GetImage();
                shot.SavePng(output);
            }

        }
        catch (Exception ex) { Check(false, ex.ToString()); }
        finally
        {
            if (underlay != null && IsInstanceValid(underlay)) underlay.QueueFree();
            if (camera != null && IsInstanceValid(camera)) camera.QueueFree();
            if (view != null && IsInstanceValid(view)) view.QueueFree();
            await Frame(); await Frame();
            Check(view != null && !IsInstanceValid(view)
                && (camera == null || !IsInstanceValid(camera))
                && (underlay == null || !IsInstanceValid(underlay)), "component water and fixture nodes retired");
            texture?.Dispose();
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            if (_bad == 0) GD.Print($"PROCEDURAL PARK WATER PASS checks={_checks}; declared component, not emulator parity");
            else GD.PrintErr($"PROCEDURAL PARK WATER FAIL checks={_checks} failures={_bad}");
            GetTree().Quit(_bad == 0 ? 0 : 1);
        }
    }

    async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    async Task<Color> Pixel()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        return image.GetPixel(image.GetWidth() / 2, image.GetHeight() / 2);
    }
}