using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>⭐⭐ THE MUSIC -- the park's (`MUSIC/MUSSFX.MAP` event 2) and the lobby's (`LOBBY/LOBMSFX.MAP`
/// event 6), one at a time, on the Music bus. Which set and which clip is <see cref="MusicSequencer"/>'s, read
/// from the console's event instance; this file only turns its choices into sound.
///
/// ⭐ WHERE THEY START AND STOP (READ):
/// - Park: `0x147D10` -> `0x111AD8` -> `0x111E30(audio, world)` loads `%sMUSIC/` `MUS` and plays event 2;
///   `0x151C00`, once a scene update, hands it `guests * 90 / 100` as selector 2 (see MusicSequencer for
///   why that value never steers it).
/// - Lobby: `0x2195C0`, on entering and on EVERY selection move (`0x2186B0` and `0x219420` call it after
///   writing the record), first stops all eight lobby handles (`0x219528`, `0x111D78(.., 0)`), then points
///   category 0xE at the record's world (`0x111BF8`) and plays event 6 looped. So a move restarts the music
///   even inside one world, and a new clip is drawn.
///
/// ⭐ GAPLESS BY CONSTRUCTION. The stems are 11..42-second pieces of one tempo meant to butt together, so they
/// are fed sample-exact into one <see cref="AudioStreamGenerator"/> rather than started as players -- a player
/// per clip leaves a mix-buffer's gap at every seam. ⚠ A clip decodes in ~90 ms (MP2, measured on the
/// Graviton box), which would be a hitch every 17 s on the main thread, so the next clip is CHOSEN and
/// decoded <see cref="LeadSeconds"/> before the current one ends. That reads the steering value up to that
/// long early -- nothing in READ mode, where the value never moves. And each clip plays only its music, the
/// map's length after one frame of codec lead (<see cref="MusicClip"/>), or every seam would stumble 0.1 s.</summary>
public sealed class GameMusic
{
    public const float LeadSeconds = 3f;
    const float BufferSeconds = 0.5f;

    readonly Node _host;
    readonly Disc _disc;
    AudioStreamPlayer _player;
    AudioStreamGeneratorPlayback _playback;
    int _mixRate;

    SoundCatalogue _catalogue;
    SoundCatalogue.Resolved _resolved;
    MusicSequencer _seq;
    string _label;

    Vector2[] _now; int _at; string _nowName;
    Task<(Vector2[] Pcm, int Rate, string Name, int Set)?> _next;

    /// <summary>One line per start, clip and stop, for the census and for anyone asking what played.</summary>
    public List<string> Census { get; } = new();
    public int ClipsStarted { get; private set; }
    int _pieceClips;
    public int Underruns { get; private set; }
    /// <summary>What is playing, "park JUNGLE" / "lobby HALLOW", or null.</summary>
    public string Playing => _seq == null ? null : _label;
    public MusicSequencer Sequencer => _seq;
    /// <summary>Where the park's guest value goes. ⭐ ON BY DEFAULT (strawberry, 2026-09-30, on hearing the PS2
    /// wires it to the wrong id: "wire it then"): the event's own steering selector, so the levels play as their
    /// bands were authored. Off (`--music-ps2`) is the console's <see cref="MusicSequencer.ParkGuestSelector"/>,
    /// which no slot takes -- level 1 forever.</summary>
    public bool ByGuests { get; set; } = true;

    public GameMusic(Node host, Disc disc) { _host = host; _disc = disc; }

    public void PlayPark(string world) => Play(SoundGroup.NativeMusic, 2, world, $"park {world}");

    public void PlayLobby(string world) => Play(SoundGroup.NativeLobbyMusic, 6, world, $"lobby {world}");

    void Play(SoundGroup group, int evId, string world, string label)
    {
        Stop();
        if (_disc == null || string.IsNullOrEmpty(world)) return;
        try
        {
            _catalogue = new SoundCatalogue(_disc, world);
            _resolved = _catalogue.Resolve(group, evId);
        }
        catch (Exception e) { Log($"[music] {label}: no map ({e.Message})"); return; }
        if (_resolved?.Source == null) { Log($"[music] {label}: {SoundCatalogue.MapFor(group, world.ToUpperInvariant(), 1)} has no event {evId}"); return; }
        _seq = new MusicSequencer(_resolved.Source);
        _label = label;
        var ev = _resolved.Source;
        Log($"[music] {label}: event {evId} of {_resolved.Map}, flags 0x{ev.Flags:x4}, {ev.Sets.Count} set(s), "
          + $"{(_seq.IsGraph ? $"graph class, steered by selector {_seq.SteeringSelector}" : "one set, no steering")}");
        var first = _seq.Start();
        if (first == null) { Log($"[music] {label}: nothing to play -- {_seq.Stopped}"); _seq = null; return; }
        var clip = Decode(_catalogue, _resolved, first.Value);
        if (clip == null) { Log($"[music] {label}: the first clip did not decode"); _seq = null; return; }
        EnsurePlayer(clip.Value.Rate);
        Begin(clip.Value);
    }

    public void Stop()
    {
        if (_seq != null) Log($"[music] {_label}: stop after {_pieceClips} clip(s)");
        _pieceClips = 0;
        _seq = null; _now = null; _at = 0; _next = null; _resolved = null; _catalogue = null;
        if (_player != null && GodotObject.IsInstanceValid(_player)) _player.Stop();
        _playback = null;
    }

    /// <summary>`0x111E08` and friends: a parameter write on the playing event, matched the console's way.</summary>
    public bool SetParameter(int selector, int value) => _seq?.SetParameter(selector, value) ?? false;

    /// <summary>`0x151C00`'s write: `guests * 90 / 100`, truncated (`0x297B68`), to selector 2.</summary>
    public void ParkGuests(int guests)
    {
        if (_seq == null || !_label.StartsWith("park")) return;
        int v = (int)(guests * 90f / 100f);
        SetParameter(ByGuests ? _seq.SteeringSelector : MusicSequencer.ParkGuestSelector, v);
    }

    /// <summary>Every frame: keep the generator's buffer full, and have the next clip ready before it is due.</summary>
    public void Step()
    {
        if (_seq == null || _playback == null || _now == null) return;
        int want = _playback.GetFramesAvailable();
        while (want > 0 && _seq != null)
        {
            if (_at >= _now.Length && !Advance()) break;
            if (_now == null) break;
            int n = Math.Min(want, _now.Length - _at);
            var chunk = new Vector2[n];
            Array.Copy(_now, _at, chunk, 0, n);
            _playback.PushBuffer(chunk);
            _at += n; want -= n;
        }
        if (_seq != null && _next == null && _now != null && _now.Length - _at <= LeadSeconds * _mixRate) ChooseNext();
    }

    void ChooseNext()
    {
        var pick = _seq.Next();
        if (pick == null)
        {
            Log($"[music] {_label}: the sequence ends after {_nowName} -- {_seq.Stopped}");
            _next = Task.FromResult<(Vector2[], int, string, int)?>(null);
            return;
        }
        // ⚠ The decode runs off the main thread, so it captures what it reads: a Stop() meanwhile nulls the
        // fields, not these. The bank itself was read (and cached) by the first, synchronous decode.
        var (p, cat, res) = (pick.Value, _catalogue, _resolved);
        _next = Task.Run(() => Decode(cat, res, p));
    }

    /// <summary>The current clip is spent: swap in the next, or pad with silence and say so if it is late.</summary>
    bool Advance()
    {
        if (_next == null) ChooseNext();
        if (!_next.IsCompleted)
        {
            Underruns++;
            if (Underruns <= 3) Log($"[music] {_label}: the next clip was not decoded in time -- a gap (underrun {Underruns})");
            return false;
        }
        var c = _next.Result;
        _next = null;
        if (c == null) { _seq = null; _now = null; return false; }
        Begin(c.Value);
        return true;
    }

    void Begin((Vector2[] Pcm, int Rate, string Name, int Set) c)
    {
        if (c.Rate != _mixRate) Log($"[music] {_label}: {c.Name} is {c.Rate} Hz against the stream's {_mixRate} -- plays off-pitch");
        _now = c.Pcm; _at = 0; _nowName = c.Name;
        ClipsStarted++; _pieceClips++;
        Log($"[music] {_label}: set {c.Set} {c.Name} {c.Pcm.Length / (float)c.Rate:F1}s"
          + (_seq.SteeringSelector != 0 ? $", steering selector {_seq.SteeringSelector} = {_seq.SteeringValue}" : ""));
    }

    static (Vector2[] Pcm, int Rate, string Name, int Set)? Decode(SoundCatalogue cat, SoundCatalogue.Resolved res, (int Set, int Clip) at)
    {
        var src = res.Source.Sets[at.Set].Clips[at.Clip];
        // Resolved.Clips is every set flattened in order; find this one by set and position.
        int flat = 0;
        for (int s = 0; s < at.Set; s++) flat += res.Source.Sets[s].Clips.Count;
        var rc = res.Clips[flat + at.Clip];
        var bank = cat.BankOf(res, rc);
        var snd = bank != null && src.Sound >= 1 && src.Sound <= bank.Sounds.Count ? bank.Sounds[src.Sound - 1] : null;
        if (snd == null || snd.IsEmpty || !snd.IsMpeg) return null;
        var raw = new byte[snd.End - snd.Start];
        Array.Copy(bank.Data, snd.Start, raw, 0, raw.Length);
        var dec = Mpeg.DecodeToPcm16(raw);
        if (dec == null) return null;
        var (pcm, rate, ch) = dec.Value;
        // ⭐ Only the music: one frame of codec lead off the front, the frame padding off the back (MusicClip).
        var (start, count) = MusicClip.Content(pcm.Length / 2 / ch, src.Milliseconds, rate);
        var outp = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            int f = start + i;
            float l = BitConverter.ToInt16(pcm, (f * ch) * 2) / 32768f;
            float r = ch == 2 ? BitConverter.ToInt16(pcm, (f * ch + 1) * 2) / 32768f : l;
            outp[i] = new Vector2(l, r);
        }
        return (outp, rate, rc.Name, at.Set);
    }

    void EnsurePlayer(int rate)
    {
        if (_player == null || !GodotObject.IsInstanceValid(_player) || _mixRate != rate)
        {
            if (_player != null && GodotObject.IsInstanceValid(_player)) _player.QueueFree();
            GameAudioMix.EnsureSfxBus();                       // makes the Music bus too
            _mixRate = rate;
            _player = new AudioStreamPlayer
            {
                Name = "Music",
                Stream = new AudioStreamGenerator { MixRate = rate, BufferLength = BufferSeconds },
                Bus = AudioServer.GetBusIndex(GameAudioMix.MusicBus) >= 0 ? GameAudioMix.MusicBus : "Master",
            };
            _host.AddChild(_player);
        }
        _player.Play();
        _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        if (_playback == null) Log($"[music] {_label}: no generator playback -- silent");
    }

    void Log(string line) { Census.Add(line); GD.Print(line); }
}
