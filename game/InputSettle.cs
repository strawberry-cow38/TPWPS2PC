using System;
using Godot;

namespace TPWPS2Viewer;

/// <summary>⭐⭐ A SCREEN TAKES NO INPUT UNTIL IT HAS ACTUALLY BEEN SHOWING FOR A MOMENT. Master,
/// 2026-10-01: "when clicking through to skip, wait for the thing you are skipping to actually start
/// playing for a moment before skipping" and "sometimes our clicks from the previous screen carry
/// through onto the lobby" (main menu -> lobby, on 6f5d979, which already had the press/release guard).
///
/// ⚠ The press/release guard (`_lobbyPressed`) catches a RELEASE whose press began elsewhere. It cannot
/// catch a whole extra click: the lobby is built synchronously, so clicks made while it loads sit in
/// the OS queue and arrive as complete press+release pairs the moment it is up. The same shape skips a
/// movie the frame it starts, and lets one click run through EA, BFLOGO and legal in a row.
///
/// ⭐ The time is counted in CLAMPED frame steps (at most 1/30 s each), so a load stall counts as one
/// short frame, not as the screen having been seen. A real-time clock would read the stall as time on
/// screen, and the queued clicks it was meant to swallow would arrive after it had opened.
///
/// ⚠ An adapter, not a console timing: the PS2's own screens poll the pad per update and have no
/// such window. The half second is a chosen number.
///
/// A Node so it ticks itself: the smokes switch off the Viewer's own _Process, and a settle ticked
/// there would never open.</summary>
public partial class InputSettle : Node
{
    public const double Seconds = 0.5;
    const double MaxStep = 1.0 / 30;

    /// <summary>Seconds of clamped frame time since <see cref="Reset"/> during which
    /// <see cref="While"/> held.</summary>
    public double Shown { get; private set; }
    /// <summary>Only count while this holds -- a movie counts once it is actually playing.</summary>
    public Func<bool> While { get; set; }
    public bool Ready => Shown >= Seconds;

    public void Reset() => Shown = 0;
    public override void _Process(double delta)
    {
        if (While == null || While()) Shown += Math.Clamp(delta, 0, MaxStep);
    }
}
