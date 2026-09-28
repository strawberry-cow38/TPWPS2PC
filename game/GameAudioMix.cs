using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>
/// ⭐ THE MUSIC AND SFX LEVELS THE WAY THE CONSOLE MOVES THEM, for the advisor's ducking
/// (findings/advisor-messages.md §4.8; READ `0x1066B0` states 2 and 4, `0x151C00`, `0x1118A8`, `0x1118F0`).
///
/// <code>
///   state 2 (every tick)   if (Sfx   &gt; 25) sfxTarget   = 25        0x2ABE28 ← 0x19
///                          if (Music &gt; 25) musicTarget = 25        0x2ABE30 ← 0x19
///   state 4                targets = the settings (0x2ABE1C / 0x2ABE20)
///   0x151C00, once a scene update after the passes:
///       live &gt; target: |live − target| &lt; 9 ? target : live − 8     (and the same upwards)
///       SFX → 0x1118F0 (group 3 = v, group 0 = min(3v, 100)); Music → 0x1118A8 (group 1)
/// </code>
///
/// ⚠ ADAPTERS, each said once here:
/// - THE PORT HAS NO MUSIC AND ITS SOUNDS NEVER WORE THE SETTINGS' LEVELS: every player sat on Master at
///   unity and nothing subscribes to <see cref="GameSettings.Changed"/>. So the buses carry the ramp as a
///   RATIO of the setting -- `live / setting`, unity at rest, 25/64 fully ducked at the default -- which is
///   the console's relative drop for group 3 (`0x24E030` applies `v/100` per group) and leaves the port's rest
///   level where it was. The Music bus is made for the day music plays; the levels are modelled either way.
/// - Which group carries which sound is not established: the port's sounds go on the SFX bus as group 3's
///   (the SFX level itself). Group 0 would only fall to `min(3·25, 100)/min(3·v, 100)`.
/// - The advisor's speech stays on Master, NOT ducked: which group plays the stream is not established
///   (research "Still unknown"), so it is left out rather than guessed into one.
/// - Pause (`0x153888`) sets both targets to 0; the port's pause is not wired to this (not the advisor's).
/// </summary>
public sealed class GameAudioMix
{
    public const string MusicBus = "Music", SfxBus = "SFX";
    /// <summary>`0x1066B0` state 2's immediate `0x19`.</summary>
    public const int DuckLevel = 0x19;
    /// <summary>`0x151C00`'s step, and its snap: within 8 lands on the target.</summary>
    public const int RampStep = 8;

    /// <summary>The live levels (`0x111A88` music, `0x111A38` sfx), 0..128.</summary>
    public int Music { get; private set; } = -1;
    public int Sfx { get; private set; } = -1;
    /// <summary>`0x2ABE30` / `0x2ABE28`.</summary>
    public int MusicTarget { get; private set; } = -1;
    public int SfxTarget { get; private set; } = -1;
    /// <summary>The gain last put on each bus, linear -- the OUTPUT a check reads.</summary>
    public float MusicGain { get; private set; } = 1f;
    public float SfxGain { get; private set; } = 1f;

    /// <summary>Make sure the two buses exist (sending to Master) and hand back the SFX bus's name for a
    /// player to use. Safe to call any number of times; with no audio server it answers Master.</summary>
    public static string EnsureSfxBus()
    {
        Ensure(MusicBus);
        return Ensure(SfxBus);
    }

    static string Ensure(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0) return name;
        int at = AudioServer.BusCount;
        AudioServer.AddBus(at);
        AudioServer.SetBusName(at, name);
        AudioServer.SetBusSend(at, "Master");
        return AudioServer.GetBusIndex(name) >= 0 ? name : "Master";
    }

    /// <summary>One scene update: the targets as states 2..4 leave them, then `0x151C00`'s ramp, then the buses.
    /// <paramref name="ducking"/> is <see cref="ParkAdvisor.Ducking"/> (state 2 until state 4).</summary>
    public void Step(GameSettings settings, bool ducking)
    {
        if (settings == null) return;
        if (Music < 0) { Music = MusicTarget = settings.Music; Sfx = SfxTarget = settings.Sfx; }
        if (ducking)
        {
            if (settings.Sfx > DuckLevel) SfxTarget = DuckLevel;           // 0x1066B0 state 2
            if (settings.Music > DuckLevel) MusicTarget = DuckLevel;
        }
        else { SfxTarget = settings.Sfx; MusicTarget = settings.Music; }   // state 4 (and a slider moved)
        Sfx = Ramp(Sfx, SfxTarget);
        Music = Ramp(Music, MusicTarget);
        MusicGain = Ratio(Music, settings.Music);
        SfxGain = Ratio(Sfx, settings.Sfx);
        Apply(MusicBus, MusicGain);
        Apply(SfxBus, SfxGain);
    }

    /// <summary>`0x151C00`: toward the target by 8, landing on it from within 8.</summary>
    public static int Ramp(int live, int target)
    {
        if (live == target) return live;
        int gap = Math.Abs(live - target);
        if (gap < 9) return target;
        return live > target ? live - RampStep : live + RampStep;
    }

    static float Ratio(int live, int setting) => setting <= 0 ? 1f : Math.Clamp(live / (float)setting, 0f, 1f);

    static void Apply(string bus, float gain)
    {
        int i = AudioServer.GetBusIndex(bus);
        if (i < 0) return;
        AudioServer.SetBusVolumeDb(i, gain <= 0f ? -80f : Mathf.LinearToDb(gain));
    }

    /// <summary>Back to the settings at once (a park torn down with the advisor ducked).</summary>
    public void Reset(GameSettings settings)
    {
        if (settings == null) return;
        Music = MusicTarget = settings.Music; Sfx = SfxTarget = settings.Sfx;
        MusicGain = SfxGain = 1f;
        Apply(MusicBus, 1f); Apply(SfxBus, 1f);
    }
}
