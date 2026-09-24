using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>The sounds a ride's script asks for, played.
///
/// ⭐⭐ THE SCRIPT CHOOSES THEM AND THE DISC NAMES THEM. `EVENT 3 -1 8` in Crazy Ape's bytecode is
/// group `OBJ_SOUND_LOC_RID`, event 8, at the ride itself; the park's own `RIDESFX.MAP` says event
/// 8 is sound 1 of `RIDEHD.SDT`, and that sound is called `apeoooooC.vag`. Nothing here picks a
/// clip: <see cref="SoundCatalogue"/> resolves the id the script wrote against the map the group
/// names, and the clip's own bytes are decoded and handed to a player standing where the
/// instruction said.
///
/// What is READ and what is CHOSEN, kept apart:
/// * read: the group, the node, the event id, the map's clips and their durations, the bank's
///   codec, 22,050 Hz for PS-ADPCM (measured over all 356 clips, see the sound browser).
/// * ⚠ chosen: `EVENT` is a one-shot and `ADDOBJ` a loop. An ADDOBJ object is something the script
///   later KILLOBJs or FADEOBJs by the tag it gave (`ADDOBJ 3 -1 EVT_GRAVE1 10` … `FADEOBJ 10`), and a
///   sound worth killing is one that would otherwise go on; EVENT passes the default tag 1000 and
///   `KILLOBJ 1000` is what a broken ride does to its one-shots. The console's object list at
///   instance `+0xb0` has not been walked, so this is a reading of the scripts, not of the handler.
/// * ⚠ chosen: a three-set event is start, loop, end -- `nl_creak_start`, `nl_creak_1..4`,
///   `nl_creak_end` name themselves that way and `nl_bump_1..3` follow -- and a set's clip is
///   picked by a random draw against the entry thresholds, whose shape (0x3fff, 0x7ffe, 0xbffd,
///   0xfffc) is cumulative. Neither has been read from a consumer.
/// * ⚠ chosen: positional voices carry Godot's default attenuation with <see cref="MaxDistance"/>
///   units of reach. The console's falloff has not been read.
///
/// ⭐ EVERY CUE IS LOGGED TWICE, because "it resolves" and "it plays" are different facts: once
/// when the script fires it, with the clip's name and place, and once eight frames later with
/// whether the voice was playing and how far its playback position had moved. A resolved clip that
/// nothing plays is the intensity readout that printed the right number while the guests were
/// handed a different one.</summary>
public sealed class RideSounds
{
    public const float MaxDistance = 80f;

    sealed class Voice
    {
        public int Ride, Tag, Serial;
        public string Name = "", Line = "";
        public Node Player;                 // AudioStreamPlayer3D or AudioStreamPlayer
        public bool Loop, OneShot;
        public int Frames;
        public bool? PlayingAt1;
        public float MaxPosition;
        public int FirstAdvanceFrame = -1;
        public double Elapsed;
        public bool Finished, Verdict;
        public bool Fading; public float Db;
        /// <summary>A loop still to be started once this (its start clip) has finished.</summary>
        /// <summary>The end clip to play when this loop is killed.</summary>
    }

    readonly Node3D _root;
    readonly SoundCatalogue _park1, _park2;
    readonly Random _rng;
    readonly Dictionary<string, AudioStreamWav> _streams = new();
    readonly List<Voice> _voices = new();
    int _serial;

    public int Cued { get; private set; }
    public int Resolved { get; private set; }
    public int Unresolved { get; private set; }
    public int StartedVoices { get; private set; }
    public int SilentVoices { get; private set; }
    public int Verdicts { get; private set; }
    /// <summary>One line per cue and one per verdict, in order, for the census.</summary>
    public List<string> Census { get; } = new();

    public RideSounds(Node3D parent, SoundCatalogue park1, SoundCatalogue park2, int seed = 1)
    {
        _park1 = park1; _park2 = park2; _rng = new Random(seed);
        _root = new Node3D { Name = "RideSounds" };
        parent.AddChild(_root);
    }

    static bool Positional(int kind) => kind is (int)SoundGroup.LocalRide or (int)SoundGroup.GlobalRide
        or (int)SoundGroup.GlobalKids or (int)SoundGroup.GlobalStaff or (int)SoundGroup.GlobalBumper;

    /// <summary>The clip, decoded, as a Godot stream. ⚠ ONE STREAM PER LOOP FLAG: the loop points
    /// live on the stream object, so a clip used both ways gets two.</summary>
    AudioStreamWav Stream(SoundCatalogue cat, SoundCatalogue.Resolved r, SoundCatalogue.ResolvedClip c, bool loop)
    {
        string key = $"{r.Map}|{c.Bank}|{c.Index}|{loop}";
        if (_streams.TryGetValue(key, out var cached)) return cached;
        AudioStreamWav wav = null;
        var bank = cat.BankOf(r, c);
        var s = bank != null && c.Index >= 1 && c.Index <= bank.Sounds.Count ? bank.Sounds[c.Index - 1] : null;
        if (s != null && !s.IsEmpty)
        {
            if (s.IsAdpcm)
            {
                var pcm = Vag.Decode(bank.Data, s.Start, s.End);
                var bytes = new byte[pcm.Length * 2];
                Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
                wav = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = 22050, Stereo = false, Data = bytes };
                if (loop) { wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward; wav.LoopBegin = 0; wav.LoopEnd = pcm.Length; }
            }
            else if (s.IsMpeg)
            {
                var raw = new byte[s.End - s.Start];
                Array.Copy(bank.Data, s.Start, raw, 0, raw.Length);
                var dec = Mpeg.DecodeToPcm16(raw);
                if (dec != null)
                {
                    var (pcm, rate, ch) = dec.Value;
                    wav = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = ch == 2, Data = pcm };
                    if (loop) { wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward; wav.LoopBegin = 0; wav.LoopEnd = pcm.Length / 2 / ch; }
                }
            }
        }
        _streams[key] = wav;
        return wav;
    }

    /// <summary>A random draw against the set's cumulative thresholds.</summary>
    SoundCatalogue.ResolvedClip Pick(IReadOnlyList<SoundCatalogue.ResolvedClip> set)
    {
        if (set.Count == 0) return null;
        int r = _rng.Next(0, 0x10000);
        foreach (var c in set) if (c.Threshold >= r) return c;
        return set[^1];
    }

    Voice Start(int ride, int tag, string name, AudioStreamWav wav, bool loop, int kind, Vector3 at, string line)
    {
        Node player;
        if (Positional(kind))
        {
            var p3 = new AudioStreamPlayer3D { Stream = wav, MaxDistance = MaxDistance, Bus = "Master", Name = $"snd{_serial}" };
            // ⚠ POSITION BEFORE AddChild, like the particles: the voice is placed at the transform
            // it enters the tree with.
            p3.Position = at;
            _root.AddChild(p3);
            player = p3;
        }
        else
        {
            var p2 = new AudioStreamPlayer { Stream = wav, Bus = "Master", Name = $"snd{_serial}" };
            _root.AddChild(p2);
            player = p2;
        }
        var v = new Voice { Ride = ride, Tag = tag, Serial = ++_serial, Name = name, Player = player, Loop = loop, OneShot = !loop, Line = line };
        // ⭐ The mixer's own word that it consumed the clip to the end -- the strongest "it played"
        // there is, and the one a position poll cannot give for a clip shorter than a frame.
        if (player is AudioStreamPlayer3D a3) a3.Finished += () => v.Finished = true;
        else if (player is AudioStreamPlayer a2) a2.Finished += () => v.Finished = true;
        if (player is AudioStreamPlayer3D q3) q3.Play(); else if (player is AudioStreamPlayer q2) q2.Play();
        _voices.Add(v);
        return v;
    }

    /// <summary>A script's `EVENT`/`ADDOBJ` with a sound group. <paramref name="at"/> is where the
    /// instruction's node is (the ride root for -1); <paramref name="fellBack"/> says the node was
    /// asked for and did not resolve, so the voice is at the root instead and the line says so.</summary>
    public void Cue(int rideId, string ride, long scriptMs, RseOpcode op, int kind, int node, int id, int tag, Vector3 at, bool fellBack = false)
    {
        Cued++;
        var cat = _park1; var r = cat?.Resolve(kind, id); int park = 1;
        if (r == null && _park2 != null) { cat = _park2; r = cat.Resolve(kind, id); park = 2; }
        string head = $"[snd] {scriptMs / 1000.0,7:F1}s {ride,-22} {op,-7} {(SoundGroup)kind,-13} node {node,3} evt {id,3} tag {tag,4}";
        if (r == null)
        {
            Unresolved++;
            string miss = $"{head} -> (no event {id} in {System.IO.Path.GetFileName(SoundCatalogue.MapFor((SoundGroup)kind, _park1?.World ?? "?", 1) ?? "?")} of either park)";
            Census.Add(miss); GD.Print(miss);
            return;
        }
        Resolved++;
        // ⚠⚠ "ADDOBJ MEANS LOOP" WAS AN INFERENCE AND IT IS WRONG. findings/sound.md said so in
        // its own words -- "a reading of the scripts ... not of the object list at instance
        // +0xb0, which has not been walked" -- and master heard the consequence: bins and
        // loudspeakers humming forever, with no guest anywhere near them.
        //
        // ⭐⭐ THE DATA SETTLES IT, AND NAMES ITSELF. `End.RSE` does
        // `ADDOBJ group 9 evt 186` and that event resolves to ONE set holding **`WinOneShot.mp2`**
        // -- a clip the authors called a one-shot, played on an endless loop by this port because
        // of the opcode it arrived on. Its neighbours are the same shape: a firework burst and a
        // `woooosh`, one set each. Meanwhile `bus.RSE`'s `ADDOBJ` resolves to FOUR sets --
        // `mk_bus_1 | nl_bus_stop | nl_bus_idle | pullaway3b` -- an approach, an idle to sit on,
        // and a pull-away. That is what a thing that genuinely loops looks like in this data.
        //
        // ⭐ So looping is a property of the EVENT, not of the instruction: an event with the
        // start/loop/end structure has a middle to sustain, and a single-set event does not.
        // `ADDOBJ` still means "add an object" -- something persistent that `KILLOBJ tag` can
        // stop -- which is exactly what the tag semantics say and costs nothing to keep.
        //
        // ⚠ Sets of exactly two are left as one-shots: nothing in the data has been read that
        // says which of the two would be the sustaining half, and guessing that is how the last
        // inference got here.
        bool loop = op == RseOpcode.ADDOBJ;   // object lifetime, not sustain -- see below
        // ⭐⭐ ONE LIVE OBJECT PER TAG, AND WITHOUT THIS THEY STACK FOREVER. Master, playing:
        // "loadspeakers and bins are spamming sounds forever ... crazy ape spams a snort sound
        // mid cycle." Reproduced in this repo's own census before touching anything: over one
        // 60-second JUNGLE run there are **46 ADDOBJ against 14 KILLOBJ**, and Crazy Ape's
        // `boil000/boil002` -- the snort -- is re-issued at 7.1s, 20.2s, 33.2s, 46.3s and 59.4s.
        // Each one started ANOTHER looping voice on top of the last, so by a minute in there are
        // five snorts running together and it grows without bound.
        //
        // ⭐ The script language settles what the right behaviour is: `ADDOBJ` names an object
        // and `KILLOBJ tag` / `FADEOBJ tag` act on "the object with that tag" -- singular, and
        // this file's own Kill already looks it up that way. An instruction that re-adds a tag
        // that is already live is restarting that object, not creating a second one.
        //
        // ⚠⚠ NOT A DECODED RULE. The console keeps its objects in a list at instance `+0xb0`
        // which nobody has walked, so whether IT replaces or stacks is unread. What IS certain is
        // that the current behaviour is wrong -- five overlapping snorts is not a thing the game
        // does -- and one-per-tag is the reading the tag semantics support.
        //
        // ⚠ LOOPS ONLY. A one-shot is allowed to overlap itself: two guests can cry at once, and
        // a cycle's worth of clangs are meant to pile up. Only an endless voice needs replacing.
        if (loop)
        {
            var already = _voices.Where(v => v.Ride == rideId && v.Tag == tag).ToList();
            if (already.Count > 0)
            {
                string dup = $"{head} -> replaces {already.Count} live voice(s) on the same tag: "
                           + string.Join(", ", already.Select(v => v.Name));
                Census.Add(dup); GD.Print(dup);
                foreach (var v in already) Free(v);
            }
        }
        // ⭐⭐ A SET IS AN ALTERNATIVE, NOT A STAGE. Master: loudspeakers "are meant to play a
        // sound from their respective banks on a timer/random", and ours "just cycl[ed] at the
        // end of each sfx". The data is unambiguous once you read the names:
        //
        //   Speaker1  3 sets: TP BEAST 1 | TP BEAST 4 | TP BEAST 7
        //   Speaker3  3 sets: TP CRICKETS 2 | TP FROG 1 | frog3
        //   Staff     6 sets: cough | crackle | newspaper | slurp | sniff | tapspoon
        //
        // Those are peers. Treating three sets as start/loop/end chained them -- play the first,
        // sustain the second forever, then the third -- which is precisely the cycling that was
        // reported. ⚠ This is the THIRD rule this file has had for looping: first the opcode,
        // then the set count, now neither. Each was an inference about structure standing in for
        // a field nobody had read.
        //
        // ⭐ So: pick ONE set at random, then a clip within it by the data's own weights, and
        // play it once. The repetition is the SCRIPT'S -- that is what a loudspeaker's timer is,
        // and what `ADDOBJ` plus a tag is for.
        //
        // ⚠⚠ NOTHING SUSTAINS ANY MORE, INCLUDING THE BUS, whose four sets really do read as
        // approach / stop / idle / pull-away. That is a real loss and it is deliberate: a silent
        // bus is a smaller wrong than four loudspeakers screeching without end, and I cannot tell
        // the two apart from structure alone. ⭐ WHAT WOULD SETTLE IT is already named in
        // findings/sound.md as unread -- the L2 record's `+0x10` flags (0, 4, 6, 8, 0x406, 0xc06)
        // and its `+0xC` word (3300, 4600, 5700, 2300, 1000, 3200, 4000, 5999), which look very
        // like a repeat interval in milliseconds. Read those and sustain comes back as data.
        var sets = Enumerable.Range(0, r.Sets).Select(i => r.Clips.Where(c => c.Set == i).ToList())
                             .Where(l => l.Count > 0).ToList();
        bool repeats = (r.Flags & RepeatFlag) != 0 && r.Word0C > 0;
        bool sustains = !repeats && (r.Flags & SustainFlag) != 0;
        if (loop && repeats)
        {
            DropRepeats(rideId, tag);
            _repeats.Add(new Repeater { Ride = rideId, Tag = tag, Kind = kind, At = at, Catalogue = cat,
                                        Event = r, IntervalSeconds = r.Word0C / 1000.0,
                                        Due = r.Word0C / 1000.0, Head = head });
        }
        var bank = sets.Count > 0 ? sets[_rng.Next(sets.Count)] : r.Clips;
        var chosen = Pick(bank);
        var stream = chosen == null ? null : Stream(cat, r, chosen, sustains);
        string place = $"at ({at.X:F1},{at.Y:F1},{at.Z:F1}){(fellBack ? " ROOT (fitting did not resolve)" : "")}{(park == 2 ? " park-2 map" : "")}";
        string line = $"{head} -> {chosen?.Bank}[{chosen?.Index}] {chosen?.Name} {chosen?.Milliseconds}ms "
                    + $"{(repeats ? $"every {r.Word0C}ms" : sustains ? "CONTINUOUS" : "one-shot")}"
                    + $" from {sets.Count} set(s) flags 0x{r.Flags:x4} {place}"
                    + (stream == null ? "  ⚠ NO STREAM (undecodable or missing bank)" : $" {(stream.Stereo ? "stereo" : "mono")} {stream.MixRate}Hz");
        Census.Add(line); GD.Print(line);
        if (stream != null) Start(rideId, tag, chosen.Name, stream, sustains, kind, at, line);
    }

    /// <summary>⭐⭐ A SOUND OBJECT THAT FIRES AGAIN ON ITS OWN TIMER, which is what master
    /// described from the first report -- loudspeakers "play a sound from their respective banks
    /// on a timer/random" -- and what three rules inferred from STRUCTURE all failed to produce.
    ///
    /// ⭐⭐⭐ IT IS A FIELD, AND IT WAS PARSED AND CARRIED ALL ALONG. The L2 record's `+0x10`
    /// flags word, which `findings/sound.md` lists as unread, has **bit 0x400 set on exactly the
    /// things that should repeat** and clear on everything else:
    ///
    ///   0x0404  Speaker1..4, Staff      0x0406  PelBin, bus        -> repeat
    ///   0x0008  Fountain, MamFount fallz2                          -> continuous
    ///   0x0000  woooosh, mortar, every Toilet event                -> once
    ///   0x0006  fireworks, gulp         0x0200  WinOneShot         -> once
    ///
    /// and `+0xC` is the interval in milliseconds: 4300 for the speakers and staff, 3200 for the
    /// bin, 4000 for the bus, 3000 for the fountain. ⚠ Every case matches what the object IS,
    /// which is the corroboration -- but this is a correlation over ~20 events, NOT a consumer
    /// walked in the executable. The bit could carry more than "repeats".
    ///
    /// ⭐ It also retires an invention: the bus is `0x0406` like the bin, so its four sets are
    /// ALTERNATIVES on a 4-second timer, not the approach/stop/idle/pull-away sequence this file
    /// claimed to be sacrificing an hour ago. There was nothing to sacrifice.</summary>
    const int RepeatFlag = 0x400, SustainFlag = 0x008;

    sealed class Repeater
    {
        public int Ride, Tag, Kind;
        public Vector3 At;
        public SoundCatalogue Catalogue;
        public SoundCatalogue.Resolved Event;
        public double IntervalSeconds, Due;
        public string Head;
    }
    readonly List<Repeater> _repeats = new();
    public int Repeating => _repeats.Count;

    void DropRepeats(int ride, int tag)
        => _repeats.RemoveAll(t => t.Ride == ride && (tag < 0 || t.Tag == tag));

    static bool IsPlaying(Node p) => p is AudioStreamPlayer3D a ? a.Playing : p is AudioStreamPlayer b && b.Playing;
    static float Position(Node p) => p is AudioStreamPlayer3D a ? a.GetPlaybackPosition() : p is AudioStreamPlayer b ? b.GetPlaybackPosition() : -1;
    static void Stop(Node p) { if (p is AudioStreamPlayer3D a) a.Stop(); else if (p is AudioStreamPlayer b) b.Stop(); }
    static void Volume(Node p, float db) { if (p is AudioStreamPlayer3D a) a.VolumeDb = db; else if (p is AudioStreamPlayer b) b.VolumeDb = db; }

    void Free(Voice v)
    {
        if (v.Player != null && GodotObject.IsInstanceValid(v.Player)) { Stop(v.Player); v.Player.QueueFree(); }
        _voices.Remove(v);
    }

    /// <summary>`KILLOBJ tag`: the ride's objects with that tag stop now; a loop with an end clip
    /// plays it. Logged with the script time, because a loop's END is as much a fact of the
    /// timeline as its start -- an assembled track has to stop the grunt where the script did.</summary>
    public void Kill(int rideId, string ride, int tag, long scriptMs)
    {
        var hit = _voices.Where(v => v.Ride == rideId && v.Tag == tag).ToList();
        string line = $"[snd] {scriptMs / 1000.0,7:F1}s {ride,-22} KILLOBJ tag {tag,4} -> stops {hit.Count}: {string.Join(", ", hit.Select(v => v.Name))}";
        Census.Add(line); GD.Print(line);
        DropRepeats(rideId, tag);
        foreach (var v in hit) Free(v);
    }

    /// <summary>`FADEOBJ tag`: the same, over half a second. ⚠ The half second is ours.</summary>
    public void Fade(int rideId, string ride, int tag, long scriptMs)
    {
        var hit = _voices.Where(v => v.Ride == rideId && v.Tag == tag).ToList();
        string line = $"[snd] {scriptMs / 1000.0,7:F1}s {ride,-22} FADEOBJ tag {tag,4} -> fades {hit.Count}: {string.Join(", ", hit.Select(v => v.Name))}";
        Census.Add(line); GD.Print(line);
        DropRepeats(rideId, tag);
        foreach (var v in hit) v.Fading = true;
    }

    /// <summary>The ride is gone (bulldozed, or the park rebuilt): everything it owned stops.</summary>
    public void Drop(int rideId)
    {
        DropRepeats(rideId, -1);
        foreach (var v in _voices.Where(v => v.Ride == rideId).ToList()) Free(v);
    }

    /// <summary>How long a voice may show no evidence before it is called silent.
    ///
    /// ⚠ DIAGNOSTIC POLICY, NOT RETAIL TIMING -- astraclaw's words, and the right framing: the
    /// console has no such window, this is only how long an observer waits before concluding
    /// nothing is being consumed. In SECONDS, so it means the same thing at any frame rate.</summary>
    public const double ObservationWindow = 0.5;

    public void Step(double delta)
    {
        // ⚠ The interval is the EVENT's own, so a 4.3 s speaker stays 4.3 s at any frame rate --
        // it counts seconds, not calls.
        for (int i = _repeats.Count - 1; i >= 0; i--)
        {
            var t = _repeats[i];
            t.Due -= delta;
            if (t.Due > 0) continue;
            t.Due += t.IntervalSeconds;
            var pool = Enumerable.Range(0, t.Event.Sets).Select(k => t.Event.Clips.Where(c => c.Set == k).ToList())
                                 .Where(l => l.Count > 0).ToList();
            var pick = Pick(pool.Count > 0 ? pool[_rng.Next(pool.Count)] : t.Event.Clips);
            var wav = pick == null ? null : Stream(t.Catalogue, t.Event, pick, false);
            if (wav != null) Start(t.Ride, t.Tag, pick.Name, wav, false, t.Kind, t.At, t.Head);
        }

        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            var v = _voices[i];
            if (v.Player == null || !GodotObject.IsInstanceValid(v.Player)) { _voices.RemoveAt(i); continue; }
            v.Frames++; v.Elapsed += delta;
            if (v.Frames == 1) v.PlayingAt1 = IsPlaying(v.Player);
            float pos = Position(v.Player);
            if (pos > v.MaxPosition) { v.MaxPosition = pos; if (v.FirstAdvanceFrame < 0) v.FirstAdvanceFrame = v.Frames; }
            // ⭐ THE COLUMN THAT MATTERS, judged as early as the evidence allows. Playing one frame
            // on says the engine accepted the voice; a playback position past zero says the mixer
            // is consuming it (under --audio-driver Dummy included, which is what a render is);
            // Finished says it consumed the whole clip. ⚠ IT USED TO READ THE POSITION ONCE, EIGHT
            // FRAMES ON: a walk film runs near 105 ms a frame, so a 314 ms clip had finished and
            // stopped before the read and reported 0 -- two "VOICE DID NOT START" verdicts on
            // clips that had played to the end. The elapsed seconds are printed beside the frame
            // count for exactly that reason.
            // ⚠⚠ A TIME WINDOW, NOT A FRAME COUNT -- the THIRD instance of one disease in this
            // method. The give-up was `v.Frames >= 8`, chosen when "a walk film runs near 105 ms a
            // frame" (the note below) made it roughly 840 ms. The census loop now runs frames in
            // single-digit milliseconds, so eight frames had become **0.02 s** and `ape_crunch3`
            // was declared silent 20 ms after being accepted. A frame count standing in for a
            // time budget is only ever right at one frame rate.
            //
            // ⭐ THE SHAPE IS astraclaw's AND IT IS BETTER THAN SWAPPING THE UNIT. Evidence wins
            // IMMEDIATELY -- a finished clip or an advanced position needs no waiting -- and the
            // absence of evidence stays PENDING until the window is up rather than failing early.
            // Swapping the count for a time budget alone would have thrown away the early-out.
            if (!v.Verdict && (v.MaxPosition > 0 || v.Finished || v.Elapsed >= ObservationWindow))
            {
                v.Verdict = true; Verdicts++;
                // ⭐⭐ FINISHED IS PROOF ON ITS OWN -- a clip cannot finish without having played.
                // `PlayingAt1` used to GATE this, and it is only set one Step after the voice was
                // created, so a clip that finished before that first poll was judged silent having
                // played to the end. astraclaw reproduced it with a synthetic short clip:
                // `playing@+1f=False, finished=True -> VOICE DID NOT START`.
                //
                // ⚠⚠ AND THIS IS THE SECOND TIME IN THIS METHOD. The note below records the
                // first: the position was read once, eight frames on, and two clips that had run
                // to the end reported 0. That fix moved the position read earlier but left
                // PlayingAt1 as a REQUIREMENT -- so the predicate was shaped wrong, not timed
                // wrong, and moving the observation earlier could never have finished the job.
                //
                // ⚠ It does not become unconditional: a voice that never played has no Finished
                // signal and no position advance, which astraclaw's stopped/no-finish control
                // holds us to. PlayingAt1 stays in the printed line as corroboration.
                bool started = v.Finished || v.MaxPosition > 0;
                if (started) StartedVoices++; else SilentVoices++;
                string verdict = $"[snd]   #{v.Serial} {v.Name}: playing@+1f={v.PlayingAt1} first advance@+{v.FirstAdvanceFrame}f "
                               + $"max position {v.MaxPosition:F3}s finished={v.Finished} after {v.Frames}f={v.Elapsed:F2}s -> {(started ? "VOICE STARTED" : "VOICE DID NOT START")}";
                Census.Add(verdict); GD.Print(verdict);
            }
            if (v.Fading)
            {
                v.Db -= (float)(delta * 80);
                Volume(v.Player, v.Db);
                if (v.Db < -60) Free(v);
                continue;
            }
            // ⚠ `ThenLoop`/`OnEnd` lived here to hand a finished start clip to its loop and to
            // play a loop's end clip. Both went with the start/loop/end path -- see Cue -- and
            // are DELETED rather than left assigned-by-nothing, because an unused hook reads like
            // a decision to the next person. They come back when the map's own repeat field is
            // read and sustain is restored from data.
            if (v.Verdict && (v.Finished || !IsPlaying(v.Player))) Free(v);
        }
    }

    public int Live => _voices.Count;

    public string Summary() =>
        $"[snd] census: {Cued} cues, {Resolved} resolved to a clip, {Unresolved} with no event in any map; "
      + $"{Verdicts} voices judged: {StartedVoices} started (position advanced, or the clip finished), "
      + $"{SilentVoices} did not; {Live} live now";

    public void Clear()
    {
        // ⚠ The timers too, or a rebuilt park keeps firing the old one's speakers.
        _repeats.Clear();
        foreach (var v in _voices.ToList()) Free(v);
    }
}
