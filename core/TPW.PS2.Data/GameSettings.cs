namespace TPW.PS2.Data;

/// <summary>⭐ The four things Game Options owns (`main_gameoptions`, menu id 1), in ONE place so
/// there is a single owner to capture and restore. Layout and behaviour:
/// `findings/hardcoded-screens.md`; draw `0x13a258`, input `0x139df0`.
///
/// ⚠ These are GAME settings, not park settings: the console keeps them in globals that outlive
/// any one park (`DAT_002abe1c` / `DAT_002abe20` for the volumes, a bit in `*DAT_002aa720` for the
/// tutorial, a word on the pad object for vibration). A save that filed them per-park would
/// restore the wrong ones after loading a different park.</summary>
public sealed class GameSettings
{
    /// <summary>⭐ The console's own range and default. `DAT_002abe1c`/`DAT_002abe20` are both
    /// `0x40` in the disc image -- half volume -- and the constructor clamps whatever they hold
    /// into 0..128 before seeding the sliders.</summary>
    public const int MaxVolume = 128, DefaultVolume = 0x40;

    /// <summary>⚠ TWO UNITS PER FRAME WHILE HELD, not one. The slider widget's step is
    /// `widget+0x30 = 0x200` in 16.16, and the constructor overrides the base's `0x100` to get it,
    /// so a held direction takes 64 frames to cross the 0..128 range. Stepping by 1 would feel
    /// right and be half the console's speed.</summary>
    public const int SliderStep = 2;

    int _music = DefaultVolume, _sfx = DefaultVolume;

    /// <summary>`DAT_002abe20`, 0..128. Written every frame to the audio object via
    /// `FUN_001118a8`.</summary>
    public int Music { get => _music; set => _music = Clamp(value); }

    /// <summary>`DAT_002abe1c`, 0..128. `FUN_001118f0` posts TWO messages for this one: the level
    /// itself and a second channel at `min(3 * sfx, 100)`.</summary>
    public int Sfx { get => _sfx; set => _sfx = Clamp(value); }

    /// <summary>Bit `0x40` of `*DAT_002aa720`, the advisor's flag byte -- which is why the row's
    /// text is `STR_FRONTEND_SPEECH_ON` / `_OFF` rather than a tutorial string of its own.</summary>
    public bool Tutorial { get; set; } = true;

    /// <summary>A word on the pad object (`*[DAT_002e982c]`).</summary>
    public bool Vibration { get; set; } = true;

    static int Clamp(int v) => v < 0 ? 0 : v > MaxVolume ? MaxVolume : v;

    /// <summary>Raised when a volume changes, so the host can push it at the mixer. The console
    /// writes both levels out EVERY frame unconditionally; this fires only on a change, which is
    /// the same result without the per-frame traffic.</summary>
    public event System.Action<GameSettings> Changed;

    public void SetMusic(int v) { if (Music != Clamp(v)) { Music = v; Changed?.Invoke(this); } }
    public void SetSfx(int v)   { if (Sfx   != Clamp(v)) { Sfx   = v; Changed?.Invoke(this); } }

    /// <summary>⚠ The second SFX channel the console also posts: `min(3 * sfx, 100)`. Kept here
    /// rather than at the call site because it is part of what "the SFX level" means on this
    /// hardware, and the channel it addresses is not identified yet.</summary>
    public int SecondarySfxChannel => System.Math.Min(3 * Sfx, 100);

    /// <summary>A slider's 0..100 fill for a 0..128 value.</summary>
    public static int Fill(int volume) => Clamp(volume) * 100 / MaxVolume;

    // ------------------------------------------------------------------ save/load

    /// <summary>The snapshot, for the save coordinator (astraclaw's `CaptureState`/`RestoreState`
    /// shape). ⚠ Versioned by the save itself, not here.</summary>
    public readonly record struct State(int Music, int Sfx, bool Tutorial, bool Vibration);

    public State CaptureState() => new(Music, Sfx, Tutorial, Vibration);

    public void RestoreState(State s)
    {
        Music = s.Music; Sfx = s.Sfx; Tutorial = s.Tutorial; Vibration = s.Vibration;
        Changed?.Invoke(this);
    }
}
