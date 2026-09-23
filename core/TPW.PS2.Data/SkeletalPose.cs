using System.Numerics;

namespace TPW.PS2.Data;

/// <summary>Sampling a skeletal record the way <c>FUN_001a8da8</c> does, into the per-node bone
/// matrices <see cref="Model.Skin.Deform"/> consumes. Every rule here is the sampler's own:
///
/// <list type="bullet">
/// <item>A channel with fewer than two keys is read straight from key 0, with no search.</item>
/// <item>Otherwise the keys are scanned from the first pair upward for the first pair whose
/// LATER time is at or after <c>floor(frame) + 1</c>; <c>t = (frame - t0) / (t1 - t0)</c>. If
/// no pair qualifies -- the frame is past the last key -- the last pair is used at <c>t = 1</c>,
/// which is how a record holds its final pose. ⚠ There is no clamp: a frame before the first
/// key extrapolates backwards, and every character track on the disc starts at time 0 so it
/// never happens (the audit counts them).</item>
/// <item>Rotation keys SLERP through <c>FUN_00167a48</c>, position keys lerp through
/// <c>FUN_00167d18</c>, and each node's 4x4 is <see cref="Model.BoneMatrix"/>.</item>
/// <item>⚠ THERE IS NO HIERARCHY. A track's rotation and position are the bone's whole
/// transform in the mesh's space; nothing composes parents, and a node the record does not key
/// is left with whatever its slot held. That is the runtime's design, not a shortcut here.</item>
/// </list></summary>
public static class SkeletalPose
{
    /// <summary>The sampler's key search: index of the pair to blend and the blend factor.</summary>
    public static (int Index, float T) Segment(IReadOnlyList<int> times, float frame)
    {
        int n = times.Count;
        if (n < 2) return (0, 0f);
        int next = ((int)frame + 1) & 0xFFFF;
        for (int i = 0; i < n - 1; i++)
        {
            if (next > times[i + 1]) continue;
            float span = times[i + 1] - times[i];
            // ⚠ A repeated time would divide by zero in the game; the disc never asks it to.
            return (i, span == 0 ? 0f : (frame - times[i]) / span);
        }
        return (n - 2, 1f);
    }

    /// <summary><c>FUN_00167a48</c>, verbatim -- including the two things a textbook SLERP does
    /// differently. It never flips the second key onto the near hemisphere, so a pair with a
    /// negative dot product takes the long way round (the exporter's keys are consistent, so it
    /// does not show); and when the keys are antipodal (dot &lt;= -0.999) it blends the first key
    /// against a perpendicular of it over a quarter turn -- and in that branch the w component is
    /// written once from the first key's z and not blended at all. Normalised on the way out.</summary>
    public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
    {
        float d = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        Quaternion o;
        if (d + 1f > 0.001f)
        {
            float fa, fb;
            if (1f - d > 0.001f)
            {
                float w = MathF.Acos(d), s = MathF.Sin(w);
                fa = MathF.Sin((1f - t) * w) / s;
                fb = MathF.Sin(t * w) / s;
            }
            else { fa = 1f - t; fb = t; }
            o = new Quaternion(fa * a.X + fb * b.X, fa * a.Y + fb * b.Y, fa * a.Z + fb * b.Z, fa * a.W + fb * b.W);
        }
        else
        {
            // The perpendicular (-y, x, -w, z), blended over pi/2; w is left as the first key's z.
            float fa = MathF.Sin((1f - t) * 1.5707964f), fb = MathF.Sin(t * 1.5707964f);
            o = new Quaternion(fa * a.X + fb * -a.Y, fa * a.Y + fb * a.X, fa * a.Z + fb * -a.W, a.Z);
        }
        float inv = 1f / MathF.Sqrt(o.X * o.X + o.Y * o.Y + o.Z * o.Z + o.W * o.W);
        return new Quaternion(o.X * inv, o.Y * inv, o.Z * inv, o.W * inv);
    }

    /// <summary>The track's rotation at a frame; identity when the track carries no rotation
    /// keys (the game would read through a null pointer; no character track does this).</summary>
    public static Quaternion RotationAt(Animation.SkeletalTrack track, float frame)
    {
        var keys = track.Rot;
        if (keys.Count == 0) return Quaternion.Identity;
        if (keys.Count < 2) return keys[0].Q;
        var (i, t) = Segment(keys.Select(k => k.Time).ToArray(), frame);
        return Slerp(keys[i].Q, keys[i + 1].Q, t);
    }

    /// <summary>The track's position key at a frame, still in the key's own (Z-up, integer)
    /// frame -- <see cref="Model.BoneMatrix"/> does the swap. Zero when the track has none.</summary>
    public static Vector3 PositionAt(Animation.SkeletalTrack track, float frame)
    {
        var keys = track.Pos;
        if (keys.Count == 0) return Vector3.Zero;
        if (keys.Count < 2) return keys[0].P;
        var (i, t) = Segment(keys.Select(k => k.Time).ToArray(), frame);
        return keys[i].P * (1f - t) + keys[i + 1].P * t;
    }

    /// <summary>One matrix per node slot for the record at a frame. Slots the record does not key
    /// are identity here -- the game leaves them as stack garbage, and the audit checks that no
    /// skin on the disc references a bone its records leave unkeyed.</summary>
    public static Matrix4x4[] At(IReadOnlyList<Animation.SkeletalTrack> tracks, float frame, int slots)
    {
        var pose = new Matrix4x4[slots];
        Array.Fill(pose, Matrix4x4.Identity);
        foreach (var t in tracks)
        {
            if (t.Node < 0 || t.Node >= slots) continue;
            pose[t.Node] = Model.BoneMatrix(RotationAt(t, frame), PositionAt(t, frame));
        }
        return pose;
    }
}
