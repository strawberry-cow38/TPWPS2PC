using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>
/// ⭐⭐ STAFF MANAGEMENT IN THE PARK (findings/staff-management.md; spec SPEC-staff-management.md):
/// the calendar's month and week now DO something -- strikes, then wages, at every month change, and the
/// Security Award's weekly check -- through <see cref="ParkManagement"/>, the same core object the audit
/// drives; and the patrol-area tool (mode 17) draws its rectangle on the ground and sets it on a staff
/// member, off-by-one and all.
///
/// ⭐ For the laptop's staff screens (cow tools builds them): <see cref="BeginPatrolArea"/> is Single
/// Staff's "Set Patrol Area", <see cref="ZoomToStaff"/> its "Zoom To"; Fire, Training and Kick Out are
/// <see cref="ParkStaff"/>'s own calls (they need nothing of the view).
///
/// ⚠ ADAPTERS, each said once here and again where it lives:
/// - the port has no in-park advisor: a message with text is "posted" to the status line and the log
///   (<see cref="PostAdvisor"/>); a silent one (row 310) is logged; voices are not played;
/// - the UI sounds (0xAF, 0x12F, 0x1F, 0xDB, 0xC5) are cued in the UI group, at the camera;
/// - the staff room's 0xBC ambience is started by polling the room's status (core, <see cref="ParkStaff.StaffRoomAmbience"/>).
/// </summary>
public partial class Viewer
{
    /// <summary>The calendar driver `0x16B060`, bound each frame to this park's finances and staff.</summary>
    ParkManagement _management;
    /// <summary>The patrol-area tool (mode 17) while it owns the cursor, or null.</summary>
    StaffPatrolTool _patrolTool;

    /// <summary>⭐ One frame of the calendar, `0x16B060`: the clock, then (on a month change) strikes and
    /// wages, and (on days 1, 8, 15, 22, 29) the weekly pass. Replaces the bare
    /// <see cref="ParkClock.Advance"/> whose three outs this view used to discard.</summary>
    void AdvanceCalendar(int frameUnits)
    {
        _management ??= new ParkManagement(_calendar, _awards)
        {
            Advisor = id => PostAdvisor(id, "award"),
            UiSound = id => ManagementUiSound(id),
        };
        _management.Finances = _sim?.Finances;
        _management.Staff = _staff;
        int months = _management.MonthChanges;
        int before = _sim?.Finances.Balance ?? 0;
        _management.Advance(frameUnits);
        if (_management.MonthChanges != months)
            GD.Print($"[calendar] month end {_calendar.Format()}: wages {Money.Format(_management.LastWages)} "
                   + $"(balance {Money.Format(before)} -> {Money.Format(_sim?.Finances.Balance ?? 0)}); strikes "
                   + string.Join(" ", StaffTables.TypeCodeOrder.Select(k => $"{k}={_staff?.StrikeStage(k) ?? 0}{(_staff?.IsStriking(k) == true ? "!" : "")}")));
    }

    /// <summary>The management hooks every park's <see cref="ParkStaff"/> gets (called from EnsureStaff).</summary>
    void AttachManagement()
    {
        if (_staff == null) return;
        _staff.Advisor = id => PostAdvisor(id, "staff");
        _staff.UiSound = ManagementUiSound;
        // ⚠ 0x14BC28 moves the CURSOR to the member it focuses; this port's game camera follows its own
        // pan cursor, so it is pointed at him instead.
        _staff.Focus = m => { if (m.Active) LookAtCell(m.Cell.X, m.Cell.Z); };
        _staff.StaffRoomAmbience = StaffRoomAmbience;
    }

    /// <summary>⚠ The port has no in-park advisor, so a message the game would post is SHOWN on the status
    /// line when it has text (the catalogue's row is not 310) and logged either way. ⭐ Of the strike
    /// ladder only UNHAPPY (0x16..0x1A) and HAPPIER (0x25..0x29) have text; VERY_UNHAPPY, STRIKING and
    /// STRIKE_END_BAD are silent on PS2, so a strike starts and ends without a word.</summary>
    void PostAdvisor(int id, string from)
    {
        try { _advisor ??= _lib?.Disc == null ? null : AdvisorCatalogue.Load(_lib.Disc); }
        catch (Exception e) { GD.PrintErr($"[advisor] catalogue unreadable: {e.Message}"); }
        var msg = _advisor != null && id >= 0 && id < _advisor.Messages.Count ? _advisor.Messages[id] : null;
        if (msg != null && msg.HasText)
        {
            string text = _text?.Text("eng", msg.TextRow)?.Replace("\n", " ") ?? $"#{msg.TextRow}";
            Status(text);
            GD.Print($"[advisor] posted 0x{id:X} {msg.SymbolicKey} ({from}): {text}");
        }
        else GD.Print($"[advisor] 0x{id:X} {msg?.SymbolicKey ?? "?"} ({from}) is silent (text row 310) -- logged, not posted");
    }

    /// <summary>A management UI sound, in the UI group, at the camera (⚠ the category-0 `0x111150` calls
    /// are not positional).</summary>
    void ManagementUiSound(int eventId)
    {
        _sounds ??= MakeSounds();
        var at = _cam != null ? _cam.GlobalPosition : Vector3.Zero;
        _sounds?.Cue(0, "management", _parkTicks * ParkSim.TickMilliseconds, RseOpcode.EVENT, UiSoundGroup, -1, eventId, 0, at);
    }

    /// <summary>⭐ `0x130510`/`0x130498`: a staff room open (status 2) loops bank 8 event 0xBC at the room
    /// until it is removed -- occupied or not, and the Laser Show too (it carries the staff-room bit).</summary>
    void StaffRoomAmbience(StaffFeature room, bool start)
    {
        int id = room.Ride?.Id ?? (room.Key is int k ? k : 0);
        _sounds ??= MakeSounds();
        if (_sounds == null) return;
        if (!start) { _sounds.Drop(id); GD.Print($"[staff] staff room at {room.Origin}: ambience 0xBC stopped (removed)"); return; }
        var centre = new Vector3(room.Origin.X + 0.5f, 0, room.Origin.Z + 0.5f);
        _sounds.Cue(id, "staff room", _parkTicks * ParkSim.TickMilliseconds, RseOpcode.ADDOBJ, (int)SoundGroup.GlobalStaff, -1,
                    StaffTables.SoundStaffRoomAmbience, 0xB8, GuestWorld(centre, room.Origin));
        GD.Print($"[staff] staff room at {room.Origin} opened (status 2): ambience 0xBC started");
    }

    // ---------------------------------------------------------------------------------------------
    // Zoom To (0x124360).

    /// <summary>⭐ Single Staff's "Zoom To": the camera to `vt+0xCC` of the member = his fine position
    /// (<see cref="StaffMember.ZoomTarget"/>). ⚠ `0x125388` moves the console's cursor/camera; this points
    /// the game camera at the cell.</summary>
    public void ZoomToStaff(StaffMember m)
    {
        if (m == null || !m.Active) return;
        var p = m.ZoomTarget;
        LookAtCell(p.X >> 8, p.Z >> 8);
    }

    // ---------------------------------------------------------------------------------------------
    // The patrol-area tool, mode 17 (findings/staff-management.md §4).

    /// <summary>⭐ Single Staff's "Set Patrol Area" (`0x1242A0` → `0x125460(toolmgr, 17)`): the tool takes the
    /// member (`0x129068`), enters (`0x128C48`) and owns the cursor; the laptop closes, as it does for the
    /// hire tool. Left click = Cross (first corner, then the second), right click = Triangle.</summary>
    public void BeginPatrolArea(StaffMember m)
    {
        if (_staff == null || m == null || !m.Active) { Status("nobody to set a patrol area for"); return; }
        if (_patrolTool != null) EndPatrolTool();
        if (_hireHeld != null) CancelHireTool();
        if (_place.Active) { _place.Clear(); _ghostView?.Clear(); }
        if (_toolOpen) CloseTool();
        _shopPanel?.Hide(); _laptopBack.Clear(); _shopPanel?.ShowBalance(null);
        _patrolTool = new StaffPatrolTool(_staff, m);
        Status($"patrol area for {m.Kind} {m.PoolSlot + 1} -- left-click one corner, then the other; right-click to step back");
        GD.Print($"[staff] patrol tool (mode 17) for {m}");
    }

    /// <summary>The tool's cursor `0x128C68` and draw `0x128CE8`, every frame: the member is held, and the
    /// rectangle between the first corner and the cursor is lit with marker 0xA5 (165, teal) -- the SAME
    /// ground overlay the other tools draw (<see cref="GhostMarkers"/>, `0x1504F8`).</summary>
    void UpdatePatrolTool()
    {
        if (_patrolTool == null) return;
        if (!_patrolTool.Member.Active) { EndPatrolTool(); return; }
        if (!CursorCell(out int x, out int y)) return;
        _patrolTool.MoveCursor(new ParkCell(x, y));
        ShowPatrolHighlight();
    }

    void ShowPatrolHighlight()
    {
        if (_patrolTool == null || _ghostView == null || _park == null) return;
        var (hx, hz, w, d) = _patrolTool.Highlight;
        var cells = new List<(int, int, int, int)>();
        for (int dz = 0; dz < d; dz++)
            for (int dx = 0; dx < w; dx++)
                if (hx + dx >= 0 && hz + dz >= 0 && hx + dx < _park.Width && hz + dz < _park.Height)
                    cells.Add((hx + dx, hz + dz, StaffPatrolTool.HighlightTexture, 0));
        _ghostView.ShowTurnedCells(cells, _park);
    }

    /// <summary>The tool's Cross `0x128D98`: the first press places the corner, the second stores the
    /// rectangle (corner + 1, cursor) and leaves.</summary>
    void PressPatrolTool()
    {
        if (_patrolTool == null) return;
        if (!CursorCell(out int x, out int y)) { Status("that click was not over the park"); return; }
        _patrolTool.MoveCursor(new ParkCell(x, y));
        var m = _patrolTool.Member;
        if (_patrolTool.Press())
        {
            GD.Print($"[staff] patrol area set on {m}: x {m.PatrolX0}..{m.PatrolX1}, z {m.PatrolZ0}..{m.PatrolZ1} "
                   + $"(walks [{m.PatrolX0},{m.PatrolX1}) x [{m.PatrolZ0},{m.PatrolZ1}))");
            Status($"{m.Kind} {m.PoolSlot + 1} now patrols x {m.PatrolX0}-{m.PatrolX1}, z {m.PatrolZ0}-{m.PatrolZ1}");
            EndPatrolTool();
            return;
        }
        ShowPatrolHighlight();
        Status($"first corner at ({x},{y}) -- left-click the opposite corner");
    }

    /// <summary>The tool's Triangle `0x128E78`: forget a placed corner and stay, else leave.</summary>
    void CancelPatrolTool()
    {
        if (_patrolTool == null) return;
        if (_patrolTool.Cancel()) { EndPatrolTool(); Status("patrol area unchanged"); }
        else { ShowPatrolHighlight(); Status("corner cleared -- left-click the first corner again"); }
    }

    void EndPatrolTool()
    {
        if (_patrolTool != null && _patrolTool.Open) _patrolTool.Cancel();
        _patrolTool = null;
        _ghostView?.Clear();
    }
}
