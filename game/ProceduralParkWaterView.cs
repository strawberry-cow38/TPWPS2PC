using System;
using System.Buffers.Binary;
using Godot;
using TPW.PS2.Data;
using NativeWater = TPW.PS2.Data.ProceduralParkWater;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace TPWPS2Viewer;

/// <summary>The separate EE-generated park-water drawable (22cb90), not the terrain's
/// opaque A_SEA meshes. Native curve, placement and signed UV narrowing come from core.
/// Godot supplies projection/culling/rasterization; this is not GS pixel parity.</summary>
public sealed partial class ProceduralParkWaterView : Node3D
{
    public MeshInstance3D Surface { get; private set; }
    public NativeWater.State State { get; private set; }
    public NativeWater.SurfaceBounds Bounds { get; private set; }
    public int Dimension { get; private set; }
    public int Uploads { get; private set; }
    public int SurfaceBuilds { get; private set; }
    public int AdvancedMilliseconds { get; private set; }
    public float ClosestClipZ { get; private set; }
    public NVector3[] Positions => _positions;
    public NVector2[] Uvs => _uvs;
    public ShaderMaterial Material { get; private set; }

    NativeWater.NativeNoise _noise;
    ArrayMesh _mesh;
    NVector3[] _positions;
    NVector2[] _uvs;
    byte[] _vertices, _attributes;
    int _vertexStride, _attributeStride, _uvOffset, _customOffset;
    double _millisecondCarry;

    /// <summary>Exact global loader binding from 2210b4..2210d8. TextureNear searches the
    /// active world archive only, so it cannot resolve this DATA.WAD drawable's art.</summary>
    public static AssetLibrary.TextureImage ReadTexture(AssetLibrary library)
    {
        const string path = "/Generic/extra/justwater.ssh";
        var bytes = library.ReadGeneric(path)
            ?? throw new InvalidOperationException("Native park water texture justwater.ssh is missing");
        var decoded = new Ssh(bytes);
        return new AssetLibrary.TextureImage(decoded.Width, decoded.Height, decoded.Pixels,
            AssetLibrary.TextureFormat.Ssh, "/DATA/DATA.WAD", path);
    }

    public void Initialize(NativeWater.NativeNoise noise, NativeWater.State state,
                           NativeWater.World world, int variant, ImageTexture texture)
    {
        if (Surface != null) throw new InvalidOperationException("Water view already initialized");
        _noise = noise ?? throw new ArgumentNullException(nameof(noise));
        State = state ?? throw new ArgumentNullException(nameof(state));
        Bounds = state.Profile.Bounds(world, variant);
        Material = new ShaderMaterial
        {
            Shader = Ps2Materials.Shader(soft: true, cull: "cull_back", rawNormals: true,
                                         linearFilter: Ps2Materials.Bilinear)
        };
        Material.SetShaderParameter("albedo_tex", texture);
        Material.SetShaderParameter("has_tex", texture != null);
        Ps2Materials.BindLight(Material);
        _mesh = new ArrayMesh();
        Surface = new MeshInstance3D { Name = "NativeParkWater", Mesh = _mesh, MaterialOverride = Material };
        AddChild(Surface);
        Name = "ProceduralParkWater";
        Build(16);
    }

    /// <summary>The native consumer gets gated elapsed milliseconds sampled in 10ms units,
    /// not the simulation's fixed D. Retain sub-10ms remainder; discard paused elapsed time.
    /// Advancing occurs before visibility/LOD, as in the native drawable. Camera projection
    /// is translated through Godot; exact native camera/VU clipping is not claimed.</summary>
    public void Step(double seconds, bool running, Camera3D camera)
    {
        if (Surface == null) return;
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _millisecondCarry += seconds * 1000.0;
        int measured = checked((int)Math.Floor(_millisecondCarry / 10.0) * 10);
        _millisecondCarry -= measured;
        int elapsed = running ? measured : 0;
        long oldUv = State.UvAccumulator;
        // Even a zero-delta native draw performs the single-wrap recovery. After a long
        // hitch, paused frames can still reduce an accumulator above 0x2000 one step at a time.
        State.AdvanceMilliseconds(elapsed);
        if (elapsed != 0)
        {
            AdvancedMilliseconds = unchecked(AdvancedMilliseconds + elapsed);
        }
        int dimension = 16;
        if (camera != null && camera.IsInsideTree())
        {
            ClosestClipZ = MinimumClipZ(camera);
            dimension = SelectDimension(ClosestClipZ);
        }
        if (dimension != Dimension) Build(dimension);
        else if (elapsed != 0 || State.UvAccumulator != oldUv) Upload();
    }

    public static int SelectDimension(float closestClipZ)
    {
        if (!float.IsFinite(closestClipZ)) throw new ArgumentOutOfRangeException(nameof(closestClipZ));
        // 22cc38..22cca4: truncate nonnegative minimum clip-Z BEFORE multiplying by .5.
        int z = closestClipZ >= 0 ? checked((int)closestClipZ) : 0;
        return (int)Math.Clamp(17f - z * .5f, 4f, 16f);
    }

    float MinimumClipZ(Camera3D camera)
    {
        var inverse = camera.GlobalTransform.AffineInverse();
        var projection = camera.GetCameraProjection();
        float minimum = float.PositiveInfinity;
        // 22c858's expanded culling bounds. The source Y extent is zero; do not substitute
        // the bounding box of the opaque sea or a hand-picked view distance.
        for (int x = 0; x < 2; x++)
            for (int z = 0; z < 2; z++)
            {
                var native = new Vector3(x == 0 ? Bounds.MinX - 30f : Bounds.MaxX + 26f,
                                         Bounds.BaseY, -(z == 0 ? Bounds.MinZ - 25f : Bounds.MaxZ));
                var local = inverse * (GlobalTransform * native);
                var clip = projection * new Vector4(local.X, local.Y, local.Z, 1f);
                minimum = Math.Min(minimum, clip.Z);
            }
        return minimum;
    }

    void Build(int dimension)
    {
        SurfaceBuilds++;
        Dimension = dimension;
        int count = NativeWater.VertexCount(dimension);
        _positions = new NVector3[count];
        _uvs = new NVector2[count];
        NativeWater.Fill(_noise, State, Bounds, dimension, _positions, _uvs);
        var indices = new int[NativeWater.IndexCount(dimension)];
        NativeWater.FillTriangleIndices(dimension, indices);
        // SurfaceTool supplies the correct custom-channel layout once, not every frame.
        using var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.SetCustomFormat(0, SurfaceTool.CustomFormat.RgbFloat);
        for (int i = 0; i < count; i++)
        {
            tool.SetNormal(Vector3.Up);
            tool.SetCustom(0, new Color(0, 112, 0, 0)); // the native writer's signed-byte normal
            tool.SetUV(new Vector2(_uvs[i].X, _uvs[i].Y));
            var p = _positions[i];
            tool.AddVertex(new Vector3(p.X, p.Y, -p.Z));
        }
        // Native +Y winding becomes Godot's clockwise upward front after the Z reflection.
        foreach (int index in indices) tool.AddIndex(index);
        _mesh.ClearSurfaces();
        tool.Commit(_mesh, (uint)Mesh.ArrayFormat.FlagUseDynamicUpdate);
        var format = (RenderingServer.ArrayFormat)_mesh.SurfaceGetFormat(0);
        _vertexStride = checked((int)RenderingServer.MeshSurfaceGetFormatVertexStride(format, count));
        _attributeStride = checked((int)RenderingServer.MeshSurfaceGetFormatAttributeStride(format, count));
        _uvOffset = checked((int)RenderingServer.MeshSurfaceGetFormatOffset(format, count, (int)Mesh.ArrayType.TexUV));
        _customOffset = checked((int)RenderingServer.MeshSurfaceGetFormatOffset(format, count, (int)Mesh.ArrayType.Custom0));
        if (_vertexStride != 12 || _uvOffset + 8 > _attributeStride || _customOffset + 12 > _attributeStride)
            throw new InvalidOperationException("Unsupported dynamic native-water mesh layout");
        _vertices = new byte[count * _vertexStride];
        _attributes = new byte[count * _attributeStride];
        Upload();
    }

    void Upload()
    {
        NativeWater.Fill(_noise, State, Bounds, Dimension, _positions, _uvs);
        var low = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var high = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < _positions.Length; i++)
        {
            var p = _positions[i];
            int vertex = i * _vertexStride, attribute = i * _attributeStride;
            Put(_vertices, vertex, p.X); Put(_vertices, vertex + 4, p.Y); Put(_vertices, vertex + 8, -p.Z);
            Put(_attributes, attribute + _uvOffset, _uvs[i].X);
            Put(_attributes, attribute + _uvOffset + 4, _uvs[i].Y);
            Put(_attributes, attribute + _customOffset, 0);
            Put(_attributes, attribute + _customOffset + 4, 112);
            Put(_attributes, attribute + _customOffset + 8, 0);
            var godot = new Vector3(p.X, p.Y, -p.Z);
            low = low.Min(godot); high = high.Max(godot);
        }
        _mesh.SurfaceUpdateVertexRegion(0, 0, _vertices);
        _mesh.SurfaceUpdateAttributeRegion(0, 0, _attributes);
        // Region updates do not automatically recalculate bounds. Keep culling tied to the
        // actual uploaded vertices; no invented amplitude envelope or frozen load-time AABB.
        Surface.CustomAabb = new Aabb(low, high - low);
        Uploads++;
    }

    static void Put(byte[] buffer, int offset, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset, 4), value);
}