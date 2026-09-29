using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer.Tests;

public partial class PlayerPathSession
{
    // Call synchronously immediately after the postdraw capture. No pose/render writes.
    void ObserveGuestRender(string screenshotPath)
    {
        bool Valid(GodotObject o) => o != null && GodotObject.IsInstanceValid(o);
        float? Number(float n) => float.IsFinite(n) ? n : null;
        float?[] V(Vector3 v) => new[] { Number(v.X), Number(v.Y), Number(v.Z) };
        float?[] XY(Vector2 v) => new[] { Number(v.X), Number(v.Y) };
        float?[][] T(Transform3D t) => new[] { V(t.Basis.X), V(t.Basis.Y), V(t.Basis.Z), V(t.Origin) };
        object NodeInfo(Node3D n)
        {
            if (!Valid(n)) return new { valid = false };
            bool inTree = n.IsInsideTree();
            return new { valid = true, name = n.Name.ToString(), inTree,
                queuedForDeletion = n.IsQueuedForDeletion(), localVisible = n.Visible,
                effectiveVisible = n.IsVisibleInTree(), localTransform = T(n.Transform),
                globalTransform = inTree ? T(n.GlobalTransform) : null };
        }
        if (!Valid(viewer))
        {
            Log("guest-render", new { screenshotPath, error = "viewer unavailable", guests = Array.Empty<object>() });
            return;
        }
        var viewport = GetViewport(); // The viewport used by Shot(), not the cached viewer camera.
        var camera = Valid(viewport) ? viewport.GetCamera3D() : null;
        bool cameraReady = Valid(camera) && camera.IsInsideTree();
        var cameraTransform = cameraReady ? camera.GetCameraTransform() : Transform3D.Identity;
        var worldToCamera = cameraTransform.AffineInverse();
        var rect = Valid(viewport) ? viewport.GetVisibleRect() : default;
        object Point(Vector3 world)
        {
            if (!cameraReady) return new { world = V(world), projectionAvailable = false };
            var pixel = camera.UnprojectPosition(world);
            return new { world = V(world), projectionAvailable = true, pixels = XY(pixel),
                cameraDepth = Number(-(worldToCamera * world).Z),
                behindCamera = camera.IsPositionBehind(world),
                inFrustumPoint = camera.IsPositionInFrustum(world),
                inViewportPoint = rect.HasPoint(pixel) };
        }
        object Ancestors(Node node)
        {
            var result = new List<object>();
            if (!Valid(node)) return result.ToArray();
            for (Node p = node.GetParent(); Valid(p); p = p.GetParent())
            {
                if (p is Node3D n)
                    result.Add(new { name = n.Name.ToString(), type = n.GetClass().ToString(),
                        localVisible = (bool?)n.Visible, effectiveVisible = (bool?)n.IsVisibleInTree() });
                else
                    result.Add(new { name = p.Name.ToString(), type = p.GetClass().ToString(),
                        localVisible = (bool?)null, effectiveVisible = (bool?)null });
            }
            return result.ToArray();
        }
        object Surface(string meshLabel, int material, MeshInstance3D n)
        {
            if (!Valid(n)) return new { meshLabel, material, node = NodeInfo(n) };
            bool inTree = n.IsInsideTree();
            var mesh = n.Mesh;
            bool meshValid = Valid(mesh);
            var bounds = meshValid ? n.GetAabb() : default;
            var corners = new List<object>();
            if (meshValid && inTree)
            {
                var transform = n.GlobalTransform;
                // Explicitly transform all eight LOCAL AABB corners (not actor origins).
                for (int i = 0; i < 8; i++)
                {
                    var local = bounds.Position + new Vector3(
                        (i & 1) != 0 ? bounds.Size.X : 0,
                        (i & 2) != 0 ? bounds.Size.Y : 0,
                        (i & 4) != 0 ? bounds.Size.Z : 0);
                    corners.Add(new { index = i, local = V(local), projected = Point(transform * local) });
                }
            }
            return new { meshLabel, material, node = NodeInfo(n), layers = n.Layers,
                cameraCullMask = cameraReady ? (uint?)camera.CullMask : null,
                layersMatchingCamera = cameraReady ? (bool?)((n.Layers & camera.CullMask) != 0) : null,
                meshValid, meshResourcePath = meshValid ? mesh.ResourcePath : null,
                meshSurfaceCount = meshValid ? (int?)mesh.GetSurfaceCount() : null,
                localAabb = meshValid ? new[] { V(bounds.Position), V(bounds.Size) } : null,
                worldCorners = corners.ToArray(),
                centre = meshValid && inTree ? Point(n.GlobalTransform * bounds.GetCenter()) : null,
                ancestors = Ancestors(n) };
        }
        var seated = Read<Dictionary<int, (Transform3D At, string Where, Vector3 Forward, string Part, float PartTop)>>(viewer, "_seated");
        var actors = Read<Dictionary<int, Node3D>>(viewer, "_actors");
        var drawn = Read<Dictionary<int, (AnimatedModel Drawn, Aps.Record Sit, string Where)>>(viewer, "_drawn");
        var parts = Read<Dictionary<int, (Vector3 Feet, Vector3 HeadAt, List<MeshInstance3D> Body, string Head)>>(viewer, "_parts");
        var guests = new List<object>();
        if (seated != null)
            foreach (var seat in seated.ToArray())
            {
                Node3D actor = null;
                actors?.TryGetValue(seat.Key, out actor);
                var hasDrawn = drawn != null && drawn.ContainsKey(seat.Key);
                var d = hasDrawn ? drawn[seat.Key] : default;
                var hasParts = parts != null && parts.ContainsKey(seat.Key);
                var p = hasParts ? parts[seat.Key] : default;
                var heads = new List<object>();
                var hidden = d.Drawn == null ? null : Read<HashSet<int>>(d.Drawn, "_hidden");
                var model = d.Drawn == null ? null : Read<Model>(d.Drawn, "_model");
                if (d.Drawn != null && Valid(d.Drawn.Root))
                    foreach (var s in d.Drawn.Surfaces())
                        if (s.Mesh?.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0)
                            heads.Add(Surface(s.Mesh, s.Material, s.Node));
                guests.Add(new { guest = seat.Key, label = seat.Value.Where, origin = V(seat.Value.At.Origin),
                    seatTransform = T(seat.Value.At), projectedSeat = Point(seat.Value.At.Origin),
                    seatPart = seat.Value.Part, partTop = Number(seat.Value.PartTop),
                    actor = NodeInfo(actor), drawnPresent = hasDrawn, drawnLabel = d.Where,
                    drawnRoot = NodeInfo(d.Drawn?.Root),
                    parts = hasParts ? new { headLabel = p.Head, cachedFeet = V(p.Feet),
                        cachedHeadAt = V(p.HeadAt), bodyCount = p.Body?.Count } : null,
                    hiddenNodes = hidden?.OrderBy(n => n).ToArray(),
                    headMeshIndices = model?.Meshes.Where(m => m.Name?.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(m => new { m.Name, m.Index, hidden = hidden?.Contains(m.Index) }).ToArray(),
                    headSurfaces = heads.ToArray() });
            }
        Log("guest-render", new { screenshotPath, ticks = Read<int>(viewer, "_parkTicks"),
            camera = cameraReady ? new { node = NodeInfo(camera), renderTransform = T(cameraTransform),
                fov = camera.Fov, near = camera.Near, far = camera.Far,
                projection = camera.Projection.ToString(), size = camera.Size,
                keepAspect = camera.KeepAspect.ToString(), frustumOffset = XY(camera.FrustumOffset),
                hOffset = camera.HOffset, vOffset = camera.VOffset, cullMask = camera.CullMask } : null,
            viewport = new[] { XY(rect.Position), XY(rect.Size) }, guests = guests.ToArray(),
            interpretation = "Pixels are viewport coordinates; depth is positive along camera -Z. Bounds are mesh-instance local AABBs, not exact triangles. Centre/corner frustum tests are point tests, not full-bounds visibility or occlusion tests. No occlusion claim is made. Transform arrays are basis X/Y/Z then origin; nonfinite numbers are null. Cached parts are context only, not head geometry." });
    }
}
