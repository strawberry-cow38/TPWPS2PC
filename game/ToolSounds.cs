using Godot;
using System;
using System.Collections.Generic;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>The build tool's sounds, played from the disc's own UI bank.
///
/// ⭐⭐ THEY ARE NAMED FOR WHAT THEY ARE. `/AUDIO/GLOBAL/UIHD.SDT` holds 26 sounds and three of
/// the four the path tool needs say so in their own filename: `rj_laypath_2` for laying a run,
/// `connectPath` for a run that joins, `blnl_error1` for a refusal. They appear in that bank and
/// NOWHERE ELSE on the disc -- a search of every one of the 41 banks finds `connectPath` and
/// `rj_laypath_2` once each -- so there is no second candidate to weigh them against.
///
/// ⭐ And the lengths fit the jobs: 43ms for the tick that starts a run, 101ms to lay, 180ms to
/// connect, 325ms for the refusal buzz.
///
/// The events come from the PSX report's §9, where all four are emitted by ONE function, the click
/// handler: refused, first click, run laid, and -- layered on top of the last, on its own voice so
/// neither cuts the other -- the run connected. That layering is why every cue has a player of its
/// own here rather than sharing one.
///
/// ⚠ WHICH sound the first click makes is the one INFERRED rather than read: `Select3` is the only
/// selection tick in the bank and it is 43ms, but nothing names it for this tool. The other three
/// are named.</summary>
public sealed class ToolSounds
{
    public enum Cue { Start, Lay, Connect, Refused, Undo }

    /// <summary>Index into `/AUDIO/GLOBAL/UIHD.SDT`.</summary>
    static readonly (Cue Cue, int Index, string Name)[] Wanted =
    {
        (Cue.Start, 12, "Select3.vag"),          // ⚠ inferred
        (Cue.Lay, 11, "rj_laypath_2.vag"),       // named
        (Cue.Connect, 6, "connectPath.vag"),     // named
        (Cue.Refused, 3, "blnl_error1.vag"),     // named
        (Cue.Undo, 13, "tearup.vag"),            // ⚠ a choice: the bank's demolish sound
    };

    readonly Dictionary<Cue, AudioStreamPlayer> _voices = new();
    public string Report { get; private set; } = "not loaded";

    /// <summary>Read the bank and decode the cues. ⚠ Every failure is a missing SOUND, never a
    /// missing tool: a park with no audio must still be buildable.</summary>
    public ToolSounds(AssetLibrary lib, Node owner)
    {
        try
        {
            var entry = lib.SoundBanks().Find(
                e => e.Path.EndsWith("/AUDIO/GLOBAL/UIHD.SDT", StringComparison.OrdinalIgnoreCase));
            if (entry == null) { Report = "no /AUDIO/GLOBAL/UIHD.SDT on this disc"; return; }
            var bank = new SoundBank(lib.ReadDisc(entry));
            var got = new List<string>();
            foreach (var (cue, index, name) in Wanted)
            {
                if (index >= bank.Sounds.Count) continue;
                var s = bank.Sounds[index];
                // ⚠ A CHECK THAT CAN FAIL. The index is only worth anything if the sound sitting
                // there is still the one that was named -- a different disc build would move them,
                // and a wrong index would play the wrong sound rather than none.
                if (s.IsEmpty || !s.Name.StartsWith(name.Split('.')[0], StringComparison.OrdinalIgnoreCase))
                { got.Add($"{cue}=MISSING"); continue; }
                var stream = Decode(bank, s);
                if (stream == null) { got.Add($"{cue}=undecodable"); continue; }
                var player = new AudioStreamPlayer { Stream = stream, Bus = "Master" };
                owner.AddChild(player);
                _voices[cue] = player;
                got.Add($"{cue}={s.Name}");
            }
            Report = string.Join(" ", got);
        }
        catch (Exception e) { Report = $"the UI bank would not read: {e.Message}"; }
    }

    static AudioStream Decode(SoundBank bank, SoundBank.Sound s)
    {
        if (!s.IsAdpcm) return null;   // the four cues are all PS-ADPCM
        var pcm = Vag.Decode(bank.Data, s.Start, s.End);
        if (pcm == null || pcm.Length == 0) return null;
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        // 22050 Hz, measured across all 356 PS-ADPCM sounds on the disc -- see PlaySelected.
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = 22050, Stereo = false, Data = bytes,
        };
    }

    /// <summary>⭐ Each cue has its OWN player, so the connect sound layers over the lay sound
    /// instead of cutting it -- which is what the game does, on separate voices.</summary>
    public void Play(Cue cue)
    {
        if (_voices.TryGetValue(cue, out var p)) p.Play();
    }
}
