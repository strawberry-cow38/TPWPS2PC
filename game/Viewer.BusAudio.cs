using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>⭐⭐ THE BUS'S SOUND. The seam astraclaw drew: they own the controller, the phase
/// states and the traced fitting position in `Viewer.Bus.cs`; this file owns everything that
/// touches <see cref="RideSounds"/>, and neither reaches into the other.
///
/// The bus plays native category 1 -- `AUDIO/GLOBAL/amb` -- event 6, which is the four-set graph
/// `mk_bus_1 | nl_bus_stop | nl_bus_idle | pullaway3b`: an approach, a stop, an idle to sit on and
/// a pull-away, cycling under parameter 20. See `SfxEventMachine` and findings/sound.md.
///
/// ⚠⚠ NATIVE CATEGORY 1 IS **NOT** GROUP 1. The port's <see cref="SoundGroup"/> is the script-side
/// `OBJ_SOUND_*` numbering and the native `a1` is the audio-category numbering; `amb` is native 1
/// and script-side **8**. The same mismatch would have put every ride scream in the staff map.</summary>
public partial class Viewer
{
    /// <summary>`AUDIO/GLOBAL/amb`, event 6. Native `FUN_00111428(audio, 1, 6, ...)`.</summary>
    const SoundGroup BusSoundGroup = SoundGroup.GlobalAmbient;
    const int BusSoundEvent = 6, BusParameterId = 20;

    bool _busAudioLive, _busParameterChained;

    /// <summary>⚠ A PORT CHOICE, AND THE ONLY ONE HERE. The controller's states are 0 idle,
    /// 1 approach, 2 at the stop, 3 departing; the sound starts when it leaves 0 and stops when
    /// it returns. That the ENGINE NOTE spans the whole visit is not itself read -- what is read
    /// is that state 1 creates the audio object and state 0 releases it (`0x147BC4` creates on the
    /// non-zero arm, `0x147A1C` calls the release). Labelled rather than implied.</summary>
    partial void BusAudioStateChanged(int state, uint activeMs)
    {
        if (_sounds == null) return;
        if (state == 0) { StopBusAudio(); return; }
        if (_busAudioLive) return;              // one voice per visit, not one per phase
        ChainBusParameter();
        // ⭐ Registered BEFORE the cue: Follow decides that this owner's voices are positional at
        // all, and a voice created without it would be a flat AudioStreamPlayer for its whole life.
        _sounds.Follow(BusSoundOwner, BusSoundTag, BusSoundPosition);
        var at = BusSoundPosition();
        _sounds.Cue(BusSoundOwner, "bus", (long)_busElapsedMs, RseOpcode.ADDOBJ,
                    (int)BusSoundGroup, -1, BusSoundEvent, BusSoundTag, at ?? Vector3.Zero,
                    fellBack: at == null);
        _busAudioLive = true;
        GD.Print($"[bus.audio] {activeMs}ms state={state} -> {BusSoundGroup} event {BusSoundEvent}"
               + $" at {(at is { } p ? $"({p.X:F1},{p.Y:F1},{p.Z:F1})" : "no fitting yet")}");
    }

    /// <summary>Called every bus update. ⭐ The position is handled by the Follow source, which
    /// `RideSounds.Step` samples per frame -- including for the clips the graph starts later, which
    /// is the half a one-shot position misses. Nothing to push here but the parameter, and that is
    /// read through the chained accessor rather than written.</summary>
    partial void UpdateBusAudio()
    {
        if (_sounds == null || !_busAudioLive) return;
        // ⚠ Re-assert the follow source: a park rebuild can clear the sound layer underneath us,
        // and a silently unfollowed voice would pin the bus's engine where it last was.
        _sounds.Follow(BusSoundOwner, BusSoundTag, BusSoundPosition);
    }

    partial void ResetBusAudio() => StopBusAudio();

    void StopBusAudio()
    {
        if (!_busAudioLive) return;
        _busAudioLive = false;
        _sounds?.Kill(BusSoundOwner, "bus", BusSoundTag, (long)_busElapsedMs);
        _sounds?.Follow(BusSoundOwner, BusSoundTag, null);
        GD.Print("[bus.audio] stopped");
    }

    /// <summary>⚠ CHAINED, NOT REPLACED, AND ONCE. `ParameterValue` already answers parameter 6
    /// for ride screams; overwriting it would silence those, and re-wrapping every frame would
    /// build a delegate chain that grows without bound. The bus answers only its own owner and
    /// only parameter 20 -- `BusParameter20`, which astraclaw's controller drives to 51 on states
    /// 2 and 3 and back to 0 after a strict 2000 active milliseconds.</summary>
    void ChainBusParameter()
    {
        if (_busParameterChained || _sounds == null) return;
        _busParameterChained = true;
        var previous = _sounds.ParameterValue;
        _sounds.ParameterValue = (ride, parameter) =>
            ride == BusSoundOwner && parameter == BusParameterId
                ? BusParameter20
                : previous?.Invoke(ride, parameter) ?? 0;
    }
}
