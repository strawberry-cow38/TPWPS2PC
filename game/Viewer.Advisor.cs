using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

/// <summary>
/// ⭐⭐ THE ADVISOR IN THE PARK (findings/advisor-rules.md, findings/advisor-messages.md): the core
/// <see cref="ParkAdvisor"/> -- the rule scheduler over the real disc rules, the 20-slot queue, the six-state
/// presentation, the lips and the message stack -- built with each park's staff and updated on the simulation
/// pass, after the guests and the staff (<see cref="TickAdvisor"/>); and what the player sees and hears of it:
/// <list type="bullet">
/// <item>the HEAD, registry model 488, rising, talking and dropping in the lower right, dressed per message and
/// lip-synced (<see cref="AdvisorHead"/>);</item>
/// <item>the VOICE, the message's speech played through the sound browser's decode (<see cref="DecodeSound"/>) and
/// the catalogue's join (<see cref="AdvisorCatalogue.Bind"/>), its real length fed back to the state machine;</item>
/// <item>the DUCKING of Music and SFX to 25 while the head is up, ramped by 8 a pass (<see cref="GameAudioMix"/>);</item>
/// <item>the MESSAGE STACK, the envelope with its count, opened with L2 (<see cref="AdvisorStackView"/>).</item>
/// </list>
/// The console never shows the advisor's text as a subtitle: a message with a text row goes to the stack when it
/// is played, and a voice-only one never does.
///
/// ⭐ THE PAD, ON A KEYBOARD: L2 is <see cref="AdvisorL2Key"/> (C), Triangle is <see cref="AdvisorTriangleKey"/>
/// (T). With the stack open: Up/Down move the cursor (pad bits 1/2: Up is towards the newer records, which are
/// drawn higher), Delete is Circle, Enter or Space is Cross (Select; on a tutorial record it is Replay, which is
/// ⚠ inert: the tutorial is out of scope). While a modal message plays (123, the
/// tutorial block) every game key and click is refused -- the pad lock -- and T, read raw every pass, skips it.
///
/// ⚠ ADAPTERS, each said once here: the speech bank and lip directory are ENGLISH and the text language `eng`,
/// the port's own choice everywhere else, with the text region from TPW_PS2_TEXT_REGION; the length fed to the
/// state machine is the decoded stream's (the console's `0x111990` reads its stream record); the speech is on
/// Master, not ducked (which audio group plays it is not established); while the park is paused the voice is
/// paused with it (the console's pause stops the advisor and the ms clock, and no stream pause was found -- it
/// is left in step with the frozen lips rather than talking over them); a record's jump-to-ride refuses a ride
/// that has left the park (natively `vt+0xCC` of whatever the pointer then addresses); the open-park flag is the
/// bus's (visitors running, gate not closed); the placed-object census is the sim's scripted placements plus the
/// scriptless ones the build tool registered; the button bar is not drawn (the status line says the keys).
/// </summary>
public partial class Viewer
{
    /// <summary>This park's advisor, or null before its staff exist.</summary>
    ParkAdvisor _parkAdvisor;
    AdvisorRules _advisorRules;
    SoundBank _advisorSpeech;
    bool _advisorSpeechTried, _advisorUnavailable;
    /// <summary>The drawn head (null: the model would not build -- then the core's timed stand-in runs).</summary>
    AdvisorHead _advisorHead;
    AdvisorStackView _advisorStackView;
    AudioStreamPlayer _advisorVoice;
    readonly GameAudioMix _audioMix = new();
    readonly Dictionary<(int Message, int Variant), AdvisorCatalogue.Binding> _advisorBindings = new();
    readonly Dictionary<ushort, AudioStreamWav> _advisorStreams = new();
    FontText _advisorBoxFont;
    bool _advisorBoxFontTried, _advisorSettingsBound;
    /// <summary>HUD edges waiting for the next pass (`0x13D940` reads them in the HUD update).</summary>
    bool _advisorL2, _advisorTriangle;

    /// <summary>⚠ The pad's L2 and Triangle, on the keyboard.</summary>
    public const Key AdvisorL2Key = Key.C, AdvisorTriangleKey = Key.T;
    string AdvisorAudioLanguage => SpeechLanguage;
    string AdvisorTextLanguage => TextLanguage;

    /// <summary>`0x1817C0(1)`: a modal message holds the pad.</summary>
    bool AdvisorPadLocked => _parkAdvisor?.PadLocked == true && _mode == Mode.Park && !_lobbyMode;

    /// <summary>⭐ Build the park's advisor with its staff (called from EnsureStaff, so once per park): the
    /// catalogue from the executable, the 106 rules from `Generic/Advisor/*.ass`, the producers over this
    /// park's clock, sim, staff and guests, the head, the voice and the stack's view; then route every emitter
    /// through it.</summary>
    void AttachAdvisor()
    {
        ResetAdvisor();
        if (_staff == null || _sim == null || _advisorUnavailable) return;
        try
        {
            _advisor ??= AdvisorCatalogue.Load(_lib.Disc);
            _advisorRules ??= new AdvisorRules(_lib.ReadGeneric("/Generic/Advisor/headers.ass") ?? throw new InvalidDataException("no headers.ass"),
                                               _lib.ReadGeneric("/Generic/Advisor/opcodes.ass") ?? throw new InvalidDataException("no opcodes.ass"));
        }
        catch (Exception e)
        {
            _advisorUnavailable = true;
            GD.PrintErr($"[advisor] unavailable: {e.Message} -- messages are dropped");
            return;
        }
        var producers = new AdvisorProducers(_calendar, _sim, _staff, _visitors)
        {
            ParkOpen = () => _visitors != null && !_gateClosed,           // ⚠ the bus's adapter for [0x2B72A4]
            Placements = AdvisorPlacements,
        };
        // ⚠ Its own random stream for the initial variants: drawing ~275 from the guests' stream here would
        // shift every guest after it (natively it IS the one stream; the port's are not the console's anyway).
        var rng = new Random(0xAD715);
        _advisorHead = BuildAdvisorHead();
        var adv = new ParkAdvisor(_advisor, _advisorRules, _calendar, producers, testPark: false,
                                  tutorial: _settings.Tutorial, world: _staffWorld, random: n => n <= 0 ? 0 : rng.Next(n))
        {
            Head = (IAdvisorHead)_advisorHead ?? new AdvisorTimedHead(),
            SpeechLength = AdvisorSpeak,
            SpeechStopped = () => { if (_advisorVoice != null && _advisorVoice.Playing) { _advisorVoice.Stop(); GD.Print("[advisor] 0x111D78: the voice is stopped"); } },
            Lips = AdvisorLip,
            GoldTicketsEarned = () => _awards.GoldTicketsEarned,
            UiSound = ManagementUiSound,
            GameOver = () => GD.Print("[advisor] 0x13BDD0 game over (123 BANKRUPTED ended) -- not ported, the park goes on"),
            Submitted = (r, immediate) => GD.Print($"[advisor] submitted 0x{r.Id:X} {AdvisorKey(r.Id)} "
                + $"{(immediate ? "immediately" : "to the queue")}{(r.Object is ParkRide ride ? $" about {DisplayName(ride)}" : "")}"),
            Played = AdvisorPlayed,
        };
        adv.Stack.UiSound = ManagementUiSound;
        adv.Stack.FocusObject = AdvisorFocus;
        // ⚠ Cross on a TUTORIAL record is Replay (0x107C18): inert here -- the tutorial is out of scope, and the core's
        // entry point is a stub (ParkAdvisor.TutorialMessage). Said in the log when it is pressed.
        var replay = adv.Stack.Replay;
        adv.Stack.Replay = row => { GD.Print($"[advisor] Replay of tutorial row {row} (0x107C18): not ported -- the tutorial is out of scope"); replay?.Invoke(row); };
        adv.Attach(_sim, _staff, null);                                    // the calendar's: SubmitAdvisor below
        _parkAdvisor = adv;
        EnsureAdvisorViews();
        _advisorStackView?.Configure(adv.Stack, AdvisorRecordText, _lib, () => _hudFont, AdvisorBoxFont());
        if (!_advisorSettingsBound)
        {
            // ⭐ Tutorial On/Off is the advisor's flag 0x40 and nothing more (0x13A178): Game Options flips both
            // (GameOptionChose); a restored settings block reaches it here.
            _advisorSettingsBound = true;
            _settings.Changed += s => _parkAdvisor?.SetTutorial(s.Tutorial);
        }
        GD.Print($"[advisor] built for this park: flags 0x{adv.Flags:X2}, {_advisorRules.Rules.Count} rules, world {_staffWorld}; "
               + $"head {(_advisorHead != null ? _advisorHead.Summary : "the timed stand-in (no model)")}; "
               + $"stack {_advisorStackView?.Report}; L2 = {AdvisorL2Key}, Triangle = {AdvisorTriangleKey}; "
               + "first scheduler call after the 50-tick start delay");
    }

    /// <summary>The head from registry id 488 -- its own parse, since its node flags are written at run time.</summary>
    AdvisorHead BuildAdvisorHead()
    {
        string path = RegistryModelPath(AdvisorHead.RegistryId);
        if (path == null) return null;
        try
        {
            EnsureCharLib();
            var entry = _charLib.Wad.Find(path) ?? throw new InvalidDataException($"DATA.WAD has no {path}");
            var model = new Model((byte[])_charLib.Read(entry).Clone());
            var aps = CharAps(path) ?? throw new InvalidDataException($"no .aps beside {path}");
            return new AdvisorHead(model, aps, mat => CharTexture(path, mat));
        }
        catch (Exception e)
        {
            GD.PrintErr($"[advisor] head {path} would not build: {e.Message} -- the timed stand-in runs instead");
            return null;
        }
    }

    /// <summary>The overlay under the HUD, the stack's view above it, the voice's player: made once, kept
    /// across parks; the head's overlay is per park.</summary>
    void EnsureAdvisorViews()
    {
        GameAudioMix.EnsureSfxBus();
        if (_advisorVoice == null)
        {
            // ⚠ On Master, not the SFX bus: the speech is not ducked (see GameAudioMix).
            _advisorVoice = new AudioStreamPlayer { Name = "AdvisorVoice", Bus = "Master" };
            AddChild(_advisorVoice);
        }
        if (_uiRoot == null) return;
        if (_advisorStackView == null || !IsInstanceValid(_advisorStackView))
        {
            _advisorStackView = new AdvisorStackView();
            _uiRoot.AddChild(_advisorStackView);
        }
        if (_advisorHead != null && _advisorHead.Overlay.GetParent() == null)
        {
            // ⭐ The head is 3D content: under every 2D element of the HUD and the laptop.
            _uiRoot.AddChild(_advisorHead.Overlay);
            _uiRoot.MoveChild(_advisorHead.Overlay, 0);
        }
        _uiRoot.MoveChild(_advisorStackView, Math.Min(1, _uiRoot.GetChildCount() - 1));
    }

    /// <summary>⭐ The read box's face, `Small.bff` (READ: `0x1DD0E0` selects font id 0, and `0x20BA28` names record 0
    /// "Small.bff" under `Data\Fonts\EUROPEAN`; see <see cref="AdvisorStackView"/>).</summary>
    FontText AdvisorBoxFont()
    {
        if (_advisorBoxFontTried) return _advisorBoxFont;
        _advisorBoxFontTried = true;
        try
        {
            var bff = _lib?.ReadGeneric("/Fonts/European/Small.bff");
            if (bff != null) _advisorBoxFont = new FontText(new BitmapFont(bff));
            else GD.PrintErr("[advisor] /Fonts/European/Small.bff not found -- the stack's box draws no text");
        }
        catch (Exception e) { GD.PrintErr($"[advisor] Small.bff would not load: {e.Message}"); }
        return _advisorBoxFont;
    }

    /// <summary>`0x1066B0` on the simulation pass: once per executed staff tick, after the guests and the
    /// staff (the native order: guests `0x152A18`, objects `0x14BE60`, then the advisor). Before each: the raw
    /// skip button and the HUD's L2/Triangle edges (`0x13D438` → `0x13D940`); after all of them, the mixer
    /// (`0x151C00`).</summary>
    void TickAdvisor(uint passes)
    {
        if (_parkAdvisor == null) return;
        for (uint i = 0; i < passes; i++)
        {
            // ⚠ INPUT: `0x1817A0(0) & 0x200` -- Triangle read RAW every pass, the pad lock notwithstanding.
            _parkAdvisor.SkipHeld = Input.IsKeyPressed(AdvisorTriangleKey);
            if (_advisorL2 || _advisorTriangle)
            {
                bool wasOpen = _parkAdvisor.Stack.IsOpen;
                _parkAdvisor.Stack.Hud(_advisorL2, _advisorTriangle);
                _advisorL2 = _advisorTriangle = false;
                if (_parkAdvisor.Stack.IsOpen != wasOpen)
                    Status(_parkAdvisor.Stack.IsOpen
                        ? $"messages -- Up/Down, Delete deletes, Enter goes to the ride, {AdvisorL2Key} or {AdvisorTriangleKey} closes"
                        : "messages closed");
            }
            _parkAdvisor.Update();
            _advisorStackView?.Pass();
        }
        _audioMix.Step(_settings, _parkAdvisor.Ducking);
    }

    /// <summary>The frame: the head at the park clock's alpha, the stack's view, the voice held with a pause.</summary>
    void PresentAdvisor()
    {
        bool park = _mode == Mode.Park && !_lobbyMode && _mainMenu is not { Open: true } && _parkAdvisor != null;
        _advisorHead?.Present(_parkClock.Alpha, _parkAdvisor?.MillisecondsPerTick ?? (int)ParkSim.TickMilliseconds,
                              GetViewport().GetVisibleRect().Size, park);
        if (_advisorStackView != null && IsInstanceValid(_advisorStackView))
        {
            _advisorStackView.Allowed = park && HudVisible && _shopPanel is not { Open: true };   // 0x108568: *HUD == 0
            _advisorStackView.Present();
        }
        if (_advisorVoice != null) _advisorVoice.StreamPaused = !ParkSimulationRunning;
    }

    /// <summary>⭐ The advisor's keys, first in the key handler. True when the key was taken.</summary>
    bool AdvisorKeyInput(InputEventKey k)
    {
        if (_parkAdvisor == null || _mode != Mode.Park || _lobbyMode || _mainMenu is { Open: true }) return false;
        // 0x1817C0(1): every game button reads nothing while a modal message plays; the skip is read raw per pass.
        if (AdvisorPadLocked) { GD.Print($"[advisor] pad locked (modal 0x{_parkAdvisor.CurrentId:X}): {k.Keycode} refused"); return true; }
        // The HUD update runs on the pass (0x151800): paused, it does not; a laptop screen owns the pad (*HUD != 0).
        if (!ParkSimulationRunning || _shopPanel is { Open: true } || !HudVisible) return false;
        var stack = _parkAdvisor.Stack;
        if (k.Keycode == AdvisorL2Key)
        {
            if (stack.IsOpen || AdvisorStackMayOpen()) _advisorL2 = true;
            return true;
        }
        if (!stack.IsOpen) return false;
        switch (k.Keycode)
        {
            case AdvisorTriangleKey: _advisorTriangle = true; return true;
            case Key.Up: stack.Press(AdvisorStackButtons.Next); return true;           // pad bit 1: cursor + 1, newer
            case Key.Down: stack.Press(AdvisorStackButtons.Previous); return true;     // pad bit 2
            case Key.Delete: stack.Press(AdvisorStackButtons.Delete); return true;     // Circle (logical 10)
            case Key.Enter: case Key.KpEnter: case Key.Space:
                stack.Press(AdvisorStackButtons.Select); return true;                  // Cross (logical 11)
        }
        return false;
    }

    /// <summary>⚠ `0x13D940`'s closed-state test, the view's half: no tool in the hand and no menu up.</summary>
    bool AdvisorStackMayOpen() =>
        !_toolOpen && !_place.Active && _trackTool == null && _addonTool == null && _coasterTool == null
        && _hireHeld == null && _patrolTool == null && _deleteTool == null && _objMenu is not { Open: true };

    /// <summary>`0x108FF8` on a type-2 record: `0x125388(cursor manager, vt+0xCC(object))`, the camera to the
    /// ride. ⚠ `vt+0xCC` of a ride is not traced: the port's other ride zooms aim at the footprint's centre, and
    /// so does this. ⚠ A ride that has left the park is refused -- natively the pointer is followed wherever it
    /// now leads (<see cref="ParkAdvisor.ObjectLeftPark"/>: a record can outlive its ride).</summary>
    void AdvisorFocus(object obj)
    {
        if (obj is not ParkRide ride) { GD.Print("[advisor] select: the record has no ride"); return; }
        if (_sim == null || !_sim.Rides.Contains(ride))
        {
            GD.Print($"[advisor] select: {DisplayName(ride)} (id {ride.Id}) has left the park -- no jump");
            Status("that ride is gone");
            return;
        }
        GD.Print($"[advisor] select: the camera to {DisplayName(ride)} (id {ride.Id})");
        LookAtCell(ride.Origin.X + ride.Width / 2, ride.Origin.Z + ride.Height / 2);
    }

    /// <summary>Leaving the park (`0x1510F8`: Save Game, Quit, Close Park) calls GetTheAdvisorOffTheScreen.</summary>
    void AdvisorLeavingPark(string why)
    {
        if (_parkAdvisor == null) return;
        GD.Print($"[advisor] {why}: GetTheAdvisorOffTheScreen (state {_parkAdvisor.State})");
        _parkAdvisor.GetTheAdvisorOffTheScreen();
    }

    /// <summary>Park teardown (`0x150E80` → `TheAdvisor::Delete 0x106488`): the speech stopped, the model freed,
    /// the audio back at the settings.</summary>
    void ResetAdvisor()
    {
        if (_parkAdvisor != null) GD.Print("[advisor] the park is torn down: the advisor and its head are freed");
        _parkAdvisor = null;
        if (_advisorVoice != null && IsInstanceValid(_advisorVoice)) { _advisorVoice.Stop(); _advisorVoice.Stream = null; }
        _advisorHead?.Free();
        _advisorHead = null;
        _advisorL2 = _advisorTriangle = false;
        _advisorBindings.Clear();
        _audioMix.Reset(_settings);
    }

    /// <summary>The calendar's messages (the award, the in-the-red chain) go to this park's advisor.</summary>
    void SubmitAdvisor(int id, string from)
    {
        if (_parkAdvisor != null) _parkAdvisor.Submit(id);
        else GD.Print($"[advisor] 0x{id:X} {AdvisorKey(id)} ({from}) raised with no park advisor -- dropped");
    }

    string AdvisorKey(int id) => _advisor != null && id >= 0 && id < _advisor.Messages.Count ? _advisor.Messages[id].SymbolicKey : "?";

    /// <summary>A stack record's words: `translate(row)` (`0x1DFA58`) from the text database now, or its own.</summary>
    string AdvisorRecordText(AdvisorStackRecord r) =>
        r.Row == -1 ? r.Text ?? "" : _text?.Text(AdvisorTextLanguage, r.Row) ?? $"#{r.Row}";

    void AdvisorPlayed(AdvisorPlayback p)
    {
        string text = p.TextAdded ? _text?.Text(AdvisorTextLanguage, p.TextRow)?.Replace("\n", " ") ?? $"#{p.TextRow}" : null;
        GD.Print($"[advisor] presented 0x{p.Id:X} {AdvisorKey(p.Id)}: "
               + (p.TextAdded ? $"text row {p.TextRow} to the message stack ({_parkAdvisor?.Stack.Count ?? 0} held): {text}" : "no text")
               + (p.SoundId != 0 ? $"; voice sound {p.SoundId} variant {p.Variant}, {p.SpeechMs} ms, talk record {p.TalkRecord}" : "; silent")
               + (p.Object is ParkRide r ? $"; about {DisplayName(r)}" : ""));
    }

    // ---------------------------------------------------------------------------------------------
    // The voice and the lips.

    SoundBank AdvisorSpeechBank()
    {
        if (!_advisorSpeechTried)
        {
            _advisorSpeechTried = true;
            try
            {
                var f = _lib.Disc.Files().FirstOrDefault(x => x.Path.Equals($"/AUDIO/ADVISOR/{AdvisorAudioLanguage.ToUpperInvariant()}/SPCHHD.SDT", StringComparison.OrdinalIgnoreCase));
                if (f != null) _advisorSpeech = new SoundBank(_lib.ReadDisc(f));
                else GD.PrintErr($"[advisor] no AUDIO/ADVISOR/{AdvisorAudioLanguage.ToUpperInvariant()}/SPCHHD.SDT -- the advisor is silent");
            }
            catch (Exception e) { GD.PrintErr($"[advisor] speech bank unreadable: {e.Message}"); }
        }
        return _advisorSpeech;
    }

    /// <summary>`LIPS.WAD`, shared with the sound browser.</summary>
    WadArchive AdvisorLipArchive()
    {
        if (_advisorLips != null) return _advisorLips;
        try
        {
            var f = _lib.Disc.Files().FirstOrDefault(x => x.Path.Equals("/DATA/LIPS.WAD", StringComparison.OrdinalIgnoreCase));
            if (f != null) _advisorLips = new WadArchive(_lib.ReadDisc(f));
        }
        catch (Exception e) { GD.PrintErr($"[advisor] LIPS.WAD unreadable: {e.Message}"); }
        return _advisorLips;
    }

    /// <summary>The catalogue's join for (message, variant) in the port's language -- the sound browser's
    /// (<see cref="AdvisorCatalogue.Bind"/>): the SDT member, the lip track, the text.</summary>
    AdvisorCatalogue.Binding AdvisorBinding(int message, int variant)
    {
        if (_advisorBindings.TryGetValue((message, variant), out var b)) return b;
        var bank = AdvisorSpeechBank();
        var lips = AdvisorLipArchive();
        if (bank == null || lips == null || _advisor == null || _text == null) return null;
        try { b = _advisor.Bind(message, variant, AdvisorAudioLanguage, bank, lips, _text, AdvisorTextLanguage); }
        catch (Exception e) { GD.PrintErr($"[advisor] 0x{message:X} variant {variant}: {e.Message}"); b = null; }
        return _advisorBindings[(message, variant)] = b;
    }

    /// <summary>⭐ `0x111150(audio, 0xB, sound, &amp;handle)` + `0x111990(handle)`: start the message's speech and
    /// hand back its length in ms (0 = no stream). The stream is the browser's decode of the SDT member the
    /// sound id selects (1-based, `0x263934`); ⚠ the length is the DECODED stream's, the one the player hears
    /// -- the SDT header's is logged beside it.</summary>
    int AdvisorSpeak(int message, int variant, ushort soundId)
    {
        var b = AdvisorBinding(message, variant);
        var bank = AdvisorSpeechBank();
        if (b?.Sound == null || b.Sound.IsEmpty || bank == null) return 0;
        if (!_advisorStreams.TryGetValue(soundId, out var wav))
        {
            try
            {
                wav = DecodeSound(bank, b.Sound, out string note);
                GD.Print($"[advisor] voice {b.Sound.Name}: {note}");
            }
            catch (Exception e) { GD.PrintErr($"[advisor] voice {b.Sound.Name} would not decode: {e.Message}"); wav = null; }
            _advisorStreams[soundId] = wav;
        }
        if (wav == null) return 0;
        EnsureAdvisorViews();
        _advisorVoice.Stream = wav;
        _advisorVoice.Play();
        _advisorVoice.StreamPaused = !ParkSimulationRunning;
        int ms = (int)Math.Round(wav.GetLength() * 1000.0);
        GD.Print($"[advisor] speaks 0x{message:X} variant {variant}: sound {soundId} = {b.Sound.Name}, {ms} ms decoded "
               + $"(SDT header {b.Sound.Milliseconds} ms), lips {(b.Lip != null ? $"{b.LipPath} ({b.Lip.Microseconds.Count} marks)" : $"none ({b.LipPath ?? "no stem"})")}");
        return ms;
    }

    /// <summary>⚠ The loaded lip track of (message, variant): `data\audio\advisor\English\&lt;stem&gt;.lip` =
    /// `LIPS.WAD/English/&lt;stem&gt;.LIP`, or null (268's `PS2_` has none).</summary>
    LipTrack AdvisorLip(int message, int variant) => AdvisorBinding(message, variant)?.Lip;

    /// <summary>⚠ ADAPTER, the placed-object census the pool producers read (v14..v17, v31, v50): every sim
    /// placement (<see cref="AdvisorProducers.DefaultPlacements"/>), then every scriptless placement the build
    /// tool registered whose compiled record is known, taken as standing (status 1), as the staff's
    /// <see cref="StaffPlacedFeatures"/> does.</summary>
    IEnumerable<AdvisorPlacement> AdvisorPlacements()
    {
        var sim = _sim;
        var inSim = new HashSet<int>();
        if (sim != null)
        {
            foreach (var r in sim.Rides) inSim.Add(r.Id);
            foreach (var p in AdvisorProducers.DefaultPlacements(sim)) yield return p;
        }
        if (_park == null) yield break;
        var live = _park.Placed.Select(p => p.Node).ToHashSet();
        foreach (var p in _busPlacements)
        {
            if (inSim.Contains(p.RuntimeId) || !IsInstanceValid(p.Node) || !live.Contains(p.Node)) continue;
            if (p.Definition?.CompiledEntry is not { } e) continue;
            byte flags = e.Kind == AssetResourceDatabase.AssetKind.Feature ? e.RawFeatureFlags.GetValueOrDefault() : (byte)0;
            yield return new AdvisorPlacement(e.Kind, 1, flags, null);
        }
    }
}
