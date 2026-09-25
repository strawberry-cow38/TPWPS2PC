using Godot;
using System;
using System.Collections.Generic;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>The laptop's own UI sounds, played from the disc's UI bank.
///
/// ⭐⭐ THE BANK NAMES THEM. `/AUDIO/GLOBAL/UIHD.SDT` holds 26 sounds and exactly four of them are
/// called `mkgui01`..`mkgui04` -- GUI sounds, saying so in their own filenames, in the same bank
/// <see cref="ToolSounds"/> already reads for the build tool. That is the same standard of
/// evidence `rj_laypath_2` and `connectPath` were picked on, and it is why the candidate SET is
/// not a guess.
///
/// ⚠⚠ WHICH ONE IS WHICH IS *NOT* IN THE NAME, AND IS NOT CLAIMED HERE. `mkgui01` 105ms,
/// `mkgui02` 386ms, `mkgui03` 75ms, `mkgui04` 94ms -- three short ticks and one longer one. The
/// assignment below is made on LENGTH (the long one opens, the short ones move and choose) and is
/// a CHOICE, not a decode. `ToolSounds` has the same seam and says so: "a name that merely suits
/// a job is not evidence for it". Master can hear the console; this is the question to put to
/// them, and one constant changes each answer.
///
/// ⭐ `blnl_error1` for a refusal is the exception -- it is named, it is the one the build tool
/// already uses for a refusal, and it appears in no other bank on the disc.
///
/// ⚠ Every failure here is a missing SOUND, never a missing laptop: a park with no audio must
/// still be navigable.</summary>
public sealed class LaptopSounds
{
    public enum Cue { Open, Close, Move, Choose, Back, Refused }

    /// <summary>Index into `/AUDIO/GLOBAL/UIHD.SDT`, with how each was chosen.</summary>
    static readonly (Cue Cue, int Index, string Name, bool Named)[] Wanted =
    {
        (Cue.Open,    24, "mkgui02.vag",     false),  // 386ms -- the long one; CHOICE
        (Cue.Close,   23, "mkgui01.vag",     false),  // 105ms; CHOICE
        (Cue.Move,     0, "mkgui03.vag",     false),  // 75ms, the shortest; CHOICE
        (Cue.Choose,   1, "mkgui04.vag",     false),  // 94ms; CHOICE
        (Cue.Back,    12, "Select3.vag",     false),  // 43ms; CHOICE
        (Cue.Refused,  3, "blnl_error1.vag", true),   // named, and the tool's own refusal
    };

    readonly Dictionary<Cue, AudioStreamPlayer> _voices = new();
    public string Report { get; private set; } = "not loaded";

    public LaptopSounds(AssetLibrary lib, Node owner)
    {
        try
        {
            var entry = lib.SoundBanks().Find(
                e => e.Path.EndsWith("/AUDIO/GLOBAL/UIHD.SDT", StringComparison.OrdinalIgnoreCase));
            if (entry == null) { Report = "no /AUDIO/GLOBAL/UIHD.SDT on this disc"; return; }
            var bank = new SoundBank(lib.ReadDisc(entry));
            var got = new List<string>();
            foreach (var (cue, index, name, named) in Wanted)
            {
                if (index >= bank.Sounds.Count) { got.Add($"{cue}=OUTOFRANGE"); continue; }
                var s = bank.Sounds[index];
                // ⚠ A CHECK THAT CAN FAIL, exactly as ToolSounds has: the index is worth nothing
                // unless the sound sitting there is still the one that was named. A different
                // disc build would move them, and a wrong index plays the WRONG sound, which is
                // worse than silence because it sounds deliberate.
                if (s.IsEmpty || !s.Name.StartsWith(name.Split('.')[0], StringComparison.OrdinalIgnoreCase))
                { got.Add($"{cue}=MISSING({s.Name})"); continue; }
                var stream = Decode(bank, s);
                if (stream == null) { got.Add($"{cue}=undecodable"); continue; }
                var player = new AudioStreamPlayer { Stream = stream, Bus = "Master" };
                owner.AddChild(player);
                _voices[cue] = player;
                got.Add($"{cue}={s.Name}{(named ? "" : "?")}");
            }
            Report = string.Join(" ", got);
        }
        catch (Exception e) { Report = $"the UI bank would not read: {e.Message}"; }
    }

    static AudioStream Decode(SoundBank bank, SoundBank.Sound s)
    {
        if (!s.IsAdpcm) return null;
        var pcm = Vag.Decode(bank.Data, s.Start, s.End);
        if (pcm == null || pcm.Length == 0) return null;
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = 22050, Stereo = false, Data = bytes,
        };
    }

    /// <summary>⭐ A voice each, so a choose landing on top of a move does not cut it.
    /// ⚠ Move is the one that fires on every row the cursor crosses, so it restarts rather than
    /// queueing -- a held arrow key must not build a backlog of ticks.</summary>
    public void Play(Cue cue)
    {
        if (!_voices.TryGetValue(cue, out var p)) return;
        if (cue == Cue.Move && p.Playing) p.Stop();
        p.Play();
    }
}
