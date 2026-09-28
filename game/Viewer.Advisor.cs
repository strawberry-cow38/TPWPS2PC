using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>
/// ⭐⭐ THE ADVISOR IN THE PARK, step A (findings/advisor-rules.md, findings/advisor-messages.md): the core
/// <see cref="ParkAdvisor"/> -- the rule scheduler over the real disc rules, the 20-slot queue, the six-state
/// presentation and the message stack -- built with each park's staff and updated on the simulation pass,
/// after the guests and the staff (<see cref="TickAdvisor"/>). Every message the port's systems raise goes
/// through it (the rides with the ride attached, the staff, research, the calendar's award and in-the-red
/// chain, the coaster tool).
///
/// ⭐ MINIMAL BY DESIGN: this view only LOGS what is submitted and what is presented. The console never shows
/// the advisor's text as a subtitle -- it goes to the message stack (L2) -- so the old status-line "post" is
/// gone. Step B draws the head, plays the voice and lips, ducks the audio and draws the stack.
///
/// ⚠ ADAPTERS, each said once here: the speech length is the English bank's SDT length
/// (<see cref="AdvisorSpeechMs"/>); the head is the core's timed stand-in (<see cref="AdvisorTimedHead"/>);
/// the open-park flag is the bus's (visitors running, gate not closed); the placed-object census is the sim's
/// scripted placements plus the scriptless ones the build tool registered; the skip button is not wired
/// (only modal messages -- the tutorial's, which is out of scope, and 123 BANKRUPTED -- read it).
/// </summary>
public partial class Viewer
{
    /// <summary>This park's advisor, or null before its staff exist.</summary>
    ParkAdvisor _parkAdvisor;
    AdvisorRules _advisorRules;
    SoundBank _advisorSpeech;
    bool _advisorSpeechTried, _advisorUnavailable;

    /// <summary>⭐ Build the park's advisor with its staff (called from EnsureStaff, so once per park): the
    /// catalogue from the executable, the 106 rules from `Generic/Advisor/*.ass`, the producers over this
    /// park's clock, sim, staff and guests; then route every emitter through it.</summary>
    void AttachAdvisor()
    {
        _parkAdvisor = null;
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
        var adv = new ParkAdvisor(_advisor, _advisorRules, _calendar, producers, testPark: false,
                                  tutorial: _settings.Tutorial, world: _staffWorld, random: n => n <= 0 ? 0 : rng.Next(n))
        {
            Head = new AdvisorTimedHead(),
            SpeechLength = AdvisorSpeechMs,
            GoldTicketsEarned = () => _awards.GoldTicketsEarned,
            UiSound = ManagementUiSound,
            GameOver = () => GD.Print("[advisor] 0x13BDD0 game over (123 BANKRUPTED ended) -- not ported, the park goes on"),
            Submitted = (r, immediate) => GD.Print($"[advisor] submitted 0x{r.Id:X} {AdvisorKey(r.Id)} "
                + $"{(immediate ? "immediately" : "to the queue")}{(r.Object is ParkRide ride ? $" about {DisplayName(ride)}" : "")}"),
            Played = AdvisorPlayed,
        };
        adv.Stack.UiSound = ManagementUiSound;
        adv.Attach(_sim, _staff, null);                                    // the calendar's: SubmitAdvisor below
        _parkAdvisor = adv;
        GD.Print($"[advisor] built for this park: flags 0x{adv.Flags:X2}, {_advisorRules.Rules.Count} rules, world {_staffWorld}; "
               + "first scheduler call after the 50-tick start delay");
    }

    /// <summary>`0x1066B0` on the simulation pass: once per executed staff tick, after the guests and the
    /// staff (the native order: guests `0x152A18`, objects `0x14BE60`, then the advisor).</summary>
    void TickAdvisor(uint passes)
    {
        if (_parkAdvisor == null) return;
        for (uint i = 0; i < passes; i++) _parkAdvisor.Update();
    }

    /// <summary>The calendar's messages (the award, the in-the-red chain) go to this park's advisor.</summary>
    void SubmitAdvisor(int id, string from)
    {
        if (_parkAdvisor != null) _parkAdvisor.Submit(id);
        else GD.Print($"[advisor] 0x{id:X} {AdvisorKey(id)} ({from}) raised with no park advisor -- dropped");
    }

    string AdvisorKey(int id) => _advisor != null && id >= 0 && id < _advisor.Messages.Count ? _advisor.Messages[id].SymbolicKey : "?";

    void AdvisorPlayed(AdvisorPlayback p)
    {
        string text = p.TextAdded ? _text?.Text("eng", p.TextRow)?.Replace("\n", " ") ?? $"#{p.TextRow}" : null;
        GD.Print($"[advisor] presented 0x{p.Id:X} {AdvisorKey(p.Id)}: "
               + (p.TextAdded ? $"text row {p.TextRow} to the message stack ({_parkAdvisor?.Stack.Count ?? 0} held): {text}" : "no text")
               + (p.SoundId != 0 ? $"; voice sound {p.SoundId} variant {p.Variant}, {p.SpeechMs} ms, talk record {p.TalkRecord}" : "; silent")
               + (p.Object is ParkRide r ? $"; about {DisplayName(r)}" : ""));
    }

    /// <summary>⚠ ADAPTER for `0x111150(audio, 0xB, sound)` + `0x111990`: the stream's length is the English
    /// speech bank's SDT header length (`SoundBank.Sound.Milliseconds`) for the 1-based sound id. The unit is
    /// INFERRED ms by the research (§4.3). No bank → 0: no stream, the state machine moves straight on.</summary>
    int AdvisorSpeechMs(int message, int variant, ushort soundId)
    {
        if (!_advisorSpeechTried)
        {
            _advisorSpeechTried = true;
            try
            {
                var f = _lib.Disc.Files().FirstOrDefault(x => x.Path.Equals("/AUDIO/ADVISOR/ENGLISH/SPCHHD.SDT", StringComparison.OrdinalIgnoreCase));
                if (f != null) _advisorSpeech = new SoundBank(_lib.ReadDisc(f));
                else GD.PrintErr("[advisor] no AUDIO/ADVISOR/ENGLISH/SPCHHD.SDT -- speech lengths are 0");
            }
            catch (Exception e) { GD.PrintErr($"[advisor] speech bank unreadable: {e.Message}"); }
        }
        int i = soundId - 1;
        return _advisorSpeech != null && i >= 0 && i < _advisorSpeech.Sounds.Count ? _advisorSpeech.Sounds[i].Milliseconds : 0;
    }

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
