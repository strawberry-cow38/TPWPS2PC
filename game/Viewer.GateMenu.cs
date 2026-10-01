using System.Collections.Generic;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>⭐ THE GATE, FROM THE POINTER -- its right-click menu and its opening sound. strawberry, 2026-09-30:
/// "wire up a rmb context menu for the gate, same as other rmb-able things with just an open park option there,
/// wire up the gate opening sound".
///
/// ⭐⭐ THE OPENING SOUND IS ON THE DISC AND THE PS2 NEVER PLAYS IT. `Gates.rss` opens with
/// `EVENT OBJ_SOUND_LOC_AMB 1 EVT_UI_GATEOPEN`, compiled as ids 190 (JUNGLE), 157 (HALLOW), 127 (FANTASY) and
/// 155 (SPACE). None of them is an event of the park's `AMBSFX.MAP`. What each map DOES carry, as its last three
/// events, is two empty sets and one clip named for the gate: JUNGLE 241/242/243 -> `jungleGteOp.vag`, HALLOW
/// 213/214/215 -> `hallowGteOp.vag`, FANTASY 183 -> `FantGteOp.vag`, SPACE 212/213/214 -> `spacegate1.vag`,
/// identical in both parks of each world. The order is the cue names SORTED -- CLOSENORMAL, CLOSESLAM, OPEN --
/// where the script's header declares them OPEN, CLOSENORMAL, CLOSESLAM: the sound build renumbered its events
/// and the scripts were never rebuilt against it. So the cue reaches no event and the gate opens in silence.
/// Same shape as the park music's selector 2 against 4 (<see cref="MusicSequencer"/>), and like that one it is
/// wired to what the data plainly meant, on request. The two close events have no clip in any world, so there is
/// nothing to wire for closing.</summary>
public partial class Viewer
{
    /// <summary>`STR_MAINMENU_OPEN_PARK`, the laptop's own row (<see cref="LaptopMainMenu.OpenParkIndex"/>).</summary>
    const int OpenParkTextId = 617;
    string OpenParkCaption => TextRow(OpenParkTextId) is { Length: > 0 } s ? s : "Open Park";

    /// <summary>What the gate's menu offers: Open Park, while the park is closed -- the laptop's own condition
    /// (`FUN_0014e538 == 0`), so the two routes can never disagree about whether it may be pressed.</summary>
    IEnumerable<string> GateMenuEntries()
    {
        if (!_laptopParkOpen) yield return OpenParkCaption;
    }

    /// <summary>⭐ `0x14E4C0`: the open flag `[0x2B72A4]` and the month it happened `[0x2B7298]`. One routine for
    /// every way in (the laptop's row, the gate's menu), so they cannot drift.</summary>
    void OpenPark(string from)
    {
        if (_laptopParkOpen) return;
        _laptopParkOpen = true;
        // ⚠ The ABSOLUTE month index, the same figure AdvisorProducers.Months uses
        // (`Clock.Month + 12 * Clock.Year`), not the month-of-year.
        _parkOpenedMonth = _calendar == null ? 0 : _calendar.Month + 12 * _calendar.Year;
        GD.Print($"[park] OPEN (0x14E4C0) from {from}: [0x2B72A4]=1, opened in month {_parkOpenedMonth} ([0x2B7298])");
        Status("the park is open -- the bus starts bringing guests in");
    }

    const int GateSoundOwner = int.MinValue + 98, GateSoundTag = 1;

    /// <summary>The park `AMBSFX.MAP` event holding each world's gate-open clip (see the class note).</summary>
    static int? GateOpenEvent(string world) => world switch
    {
        "JUNGLE" => 243, "HALLOW" => 215, "FANTASY" => 183, "SPACE" => 214, _ => null,
    };

    /// <summary>Whether this opening has sounded -- the script's `VAR_STATUS`: the cue fires when an open
    /// command meets a gate that is not already open, and a close re-arms it.</summary>
    bool _gateOpenCued;

    /// <summary>Called from the gate's own animation step, before it moves. A park that LOADS open starts at
    /// frame 0 and makes no sound; one opened from closed does, once.</summary>
    void StepGateSound()
    {
        if (!_laptopParkOpen) { _gateOpenCued = false; return; }
        if (_gateOpenCued) return;
        _gateOpenCued = true;
        if (_parkTime <= 0.001f) return;                    // already standing open
        string world = System.IO.Path.GetFileNameWithoutExtension(_lib?.WadName ?? "").ToUpperInvariant();
        if (GateOpenEvent(world) is not { } id) { GD.Print($"[gate] no gate-open event for {world}"); return; }
        _sounds ??= MakeSounds();
        var at = _gate?.Root is { } root && IsInstanceValid(root) ? root.GlobalPosition : Vector3.Zero;
        _sounds?.Cue(GateSoundOwner, "gate", (long)(_parkTicks * ParkSim.TickMilliseconds), RseOpcode.EVENT,
                     (int)SoundGroup.LocalAmbient, 1, id, GateSoundTag, at);
    }
}
