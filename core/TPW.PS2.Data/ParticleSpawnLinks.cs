namespace TPW.PS2.Data;

/// <summary>The immediate PARTICLE-child request in 0x18b5a8/0x18b0f8.
/// This is not an emitter simulator: attractor children, particle death and emitter expiry
/// are separate consumers. The offset is the CHILD template's velocity words, used here as
/// position units (spawn shifts them left 4 against positions in 10240ths of a cell).
/// Attachment following and parent/child lifetime coupling are not implemented by this plan.</summary>
public static class ParticleSpawnLinks
{
    public readonly record struct ChildRequest(int EffectId, int X, int Y, int Z);

    /// <summary>Resolve a non-attractor immediate child, retaining the native self-link refusal.
    /// Returned offsets are in native position units (640 per cell), before handedness conversion.</summary>
    public static bool TryParticleChild(ParticleLibrary library, int parentId, out ChildRequest request)
    {
        request = default;
        var parent = library?[parentId];
        if (parent == null) return false;
        var p = ParticleTemplate.Of(parent);
        int id = p.ChildEffect;
        if (id < 0 || id == parentId || p.ChildIsAttractor || library[id] is not { } child)
            return false;
        var offset = p.AttachChild ? ParticleTemplate.Of(child).EmitterVelocity : default;
        request = new ChildRequest(id, offset.X, offset.Y, offset.Z);
        return true;
    }
}