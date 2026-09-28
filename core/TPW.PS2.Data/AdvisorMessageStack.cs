namespace TPW.PS2.Data;

/// <summary>A message-stack record's type, `record+0` (findings/advisor-messages.md §1.3, §5.1). It is
/// the queue slot's kind carried over (`0x107640`/`0x107760`: `hasObject ? 2 : (id − 208 &lt; 67 ? 3 :
/// 0)`), or 4 for a goal notice (`0x16B9F8`).</summary>
public enum AdvisorRecordType : byte
{
    /// <summary>A plain message.</summary>
    Plain = 0,
    /// <summary>Linked to an object: Cross is "Select", the camera goes there (`0x108FF8` → `0x125388`).</summary>
    Object = 2,
    /// <summary>A tutorial message (ids 208..274): Cross is "Replay" (`0x107C18`).</summary>
    Tutorial = 3,
    /// <summary>A goal notice with its own text (`0x16BA58` → `0x16B9F8`). ⚠ Never saved (`0x1C2D50` skips 4).</summary>
    Goal = 4,
}

/// <summary>One of the stack's 32 records (0x120 bytes at `stack + 0x144 + 0x120·i`; READ `0x1089A0`,
/// `0x1090D0`, `0x108598`, findings/advisor-messages.md §5.1).</summary>
public sealed class AdvisorStackRecord
{
    /// <summary>`+0`.</summary>
    public AdvisorRecordType Type { get; internal set; }
    /// <summary>`+4`: 0 idle, 1 sliding out (being deleted), 2 selected.</summary>
    public int State { get; internal set; }
    /// <summary>`+8`, char[256]: the record's own text, used when <see cref="Row"/> is −1 (goal notices,
    /// or a loaded record whose row was −1). Null otherwise.</summary>
    public string Text { get; internal set; }
    /// <summary>`+0x108`, s16: the text row (`translate(row)`), or −1 for <see cref="Text"/>.</summary>
    public short Row { get; internal set; } = -1;
    /// <summary>`+0x10C` = 3600 (`0x1089A0`): only ever copied, never read -- there is no expiry.</summary>
    public int Lifetime { get; internal set; } = StackRecordLifetime;
    /// <summary>`+0x110`: the slide counter (`0x108A20` counts it up, `0x108A70` down). ⚠ NOT moved when the
    /// records below a removed one shift down (`0x1090D0` copies every field but this and `+0x118`), so it
    /// stays with the SLOT -- reproduced.</summary>
    public int SlideCounter { get; internal set; }
    /// <summary>`+0x114`: the horizontal slide offset in pixels, −64 = off screen (`0x1089E0`).</summary>
    public int SlideX { get; internal set; } = -64;
    /// <summary>`+0x118`: written 0 by `0x1089E0`, never moved by a shift; the draw adds it to y.</summary>
    public int SlideY { get; internal set; }
    /// <summary>`+0x11C`: the attached object (a ride, where the emitter attached one), or null.</summary>
    public object Object { get; internal set; }

    const int StackRecordLifetime = 0xE10;

    /// <summary>`0x1089A0`: state 0, row −1, +0x10C = 3600, then `0x1089E0` (slide 0, −64, 0).</summary>
    internal void Reset()
    {
        Type = AdvisorRecordType.Plain; State = 0; Text = null; Row = -1; Lifetime = StackRecordLifetime; Object = null;
        ResetSlide();
    }
    /// <summary>`0x1089E0`.</summary>
    internal void ResetSlide() { SlideCounter = 0; SlideX = -64; SlideY = 0; }
    /// <summary>`0x108598`'s whole-record copy (0x120 bytes).</summary>
    internal void CopyAll(AdvisorStackRecord s)
    {
        Type = s.Type; State = s.State; Text = s.Text; Row = s.Row; Lifetime = s.Lifetime;
        SlideCounter = s.SlideCounter; SlideX = s.SlideX; SlideY = s.SlideY; Object = s.Object;
    }
    /// <summary>`0x1090D0`, the shift on removal: type, state, text, row, +0x10C, +0x114, object -- NOT
    /// +0x110 or +0x118.</summary>
    internal void CopyShifted(AdvisorStackRecord s)
    {
        Type = s.Type; State = s.State; Text = s.Text; Row = s.Row; Lifetime = s.Lifetime; SlideX = s.SlideX; Object = s.Object;
    }
}

/// <summary>The stack's buttons for one pass, as edges (pressed this frame). ⚠ The viewer maps them; the
/// core never reads a pad. Game bits per `0x1080A8`/`0x13D940` (findings/advisor-messages.md §5.4):
/// <see cref="Next"/> = pad bit 1 (cursor + 1, towards the NEWER records), <see cref="Previous"/> = bit 2
/// (cursor − 1), <see cref="Delete"/> = logical 10 (Circle), <see cref="Select"/> = logical 11 (Cross).
/// L2 and Triangle open/close through <see cref="AdvisorMessageStack.Hud"/>.</summary>
[Flags]
public enum AdvisorStackButtons
{
    None = 0,
    Next = 1,
    Previous = 2,
    Delete = 4,
    Select = 8,
}

/// <summary>⭐⭐ THE MESSAGE STACK, `0x3928C8` (findings/advisor-messages.md §5, §6, §11; READ `0x107EC8`..
/// `0x109100`, HUD `0x13D940`, `0x13D8C0`, save `0x1C2D50`, load `0x1608E8`).
///
/// The advisor's text is not a subtitle: a message with a text row (≠ 310) is appended here when it is
/// PLAYED, and the player reads it later with L2. This class is the stack's data and its methods; the view
/// draws it (game/AdvisorStackView.cs: the envelope and count at (32, 420)/(80, 430), the records at
/// y = 250 − 70·i + scroll, the 260×180 text box at y 210).
///
/// ⭐ It is SAVED per park (<see cref="CaptureState"/>/<see cref="RestoreState"/>, the shape of
/// `0x1C2D50`/`0x1608E8`); the advisor's queue, current message and rule state are not (research §11).
///
/// ⚠ Its per-pass update (<see cref="Update"/>) is `0x108220` as `0x13D438` calls it, once per simulation
/// pass while the park runs (<see cref="ParkAdvisor.Update"/> calls it first, where the UI manager
/// update sits in the pass).</summary>
public sealed class AdvisorMessageStack
{
    /// <summary>`0x108598`: `count &gt; 0x1F` removes index 0 before adding.</summary>
    public const int Capacity = 32;
    /// <summary>The blank row: a message with it never gets a record (`0x1078E8`), and opcode 8 skips it (`0x1073F0`).</summary>
    public const short BlankRow = AdvisorCatalogue.BlankTextRow;
    /// <summary>UI sounds (`0x111150(audio, 0, n, 0)`): 0x1E a record added, 0x1F open/close/delete, 0xD6 the cursor.</summary>
    public const int SoundAdded = 0x1E, SoundOpenClose = 0x1F, SoundCursor = 0xD6;
    /// <summary>The Cross label (`0x1080A8` → `0x13E340`): 477 Select (type 2), 262 Replay (type 3), else 310
    /// blank; slot 0 is 967 Close and slot 1 573 Delete.</summary>
    public const int LabelClose = 0x3C7, LabelDelete = 0x23D, LabelSelect = 0x1DD, LabelReplay = 0x106;
    /// <summary>`0x107FB8`'s tutorial event on opening.</summary>
    public const int TutorialEventOpened = 0xE;
    /// <summary>`0x1080A8`: the scroll keeps the cursor within this many rows of the top.</summary>
    public const int VisibleRows = 4;

    readonly AdvisorStackRecord[] _slots = new AdvisorStackRecord[Capacity];

    public AdvisorMessageStack()
    {
        for (int i = 0; i < Capacity; i++) { _slots[i] = new AdvisorStackRecord(); _slots[i].Reset(); }
    }

    /// <summary>`+0xC`, 0..32.</summary>
    public int Count { get; private set; }
    /// <summary>The live records, oldest first (index 0).</summary>
    public IReadOnlyList<AdvisorStackRecord> Records => _slots.Take(Count).ToArray();
    /// <summary>`+4`.</summary>
    public bool IsOpen { get; private set; }
    /// <summary>`+8`: a Delete is sliding out; input is blocked until it is removed.</summary>
    public bool Deleting { get; private set; }
    /// <summary>`+0x10`: the selected index, 0 = the OLDEST record.</summary>
    public int Cursor { get; private set; }
    /// <summary>`+0x14`: the first visible row.</summary>
    public int ScrollTop { get; private set; }
    /// <summary>`+0x1C`: the index pending removal, −1 for none.</summary>
    public int PendingRemoval { get; private set; } = -1;
    /// <summary>`+0x140`: the selected record (set by the update when open), or null.</summary>
    public AdvisorStackRecord Selected { get; private set; }
    /// <summary>The Cross label row for the selected record (see <see cref="LabelSelect"/>).</summary>
    public int CrossLabel => Selected?.Type switch { AdvisorRecordType.Object => LabelSelect, AdvisorRecordType.Tutorial => LabelReplay, _ => BlankRow };

    /// <summary>A UI sound (category 0).</summary>
    public Action<int> UiSound { get; set; }
    /// <summary>⚠ `0x107390(adv, 0xE)`, the tutorial event the opening sends (the dispatcher is OUT of scope:
    /// <see cref="ParkAdvisor.TutorialEvent"/> is a stub).</summary>
    public Action<int> TutorialEvent { get; set; }
    /// <summary>Select on a type-2 record: `0x125388(cursor manager, vt+0xCC(object))`, the camera to the
    /// object. The view's.</summary>
    public Action<object> FocusObject { get; set; }
    /// <summary>Replay on a type-3 record: `0x107C18(adv, row)` -- ⚠ a stub in this step
    /// (<see cref="ParkAdvisor.TutorialMessage"/>).</summary>
    public Action<short> Replay { get; set; }

    AdvisorStackButtons _pressed;
    /// <summary>Queue button edges for the next <see cref="Update"/> (the view calls this from its input).</summary>
    public void Press(AdvisorStackButtons buttons) => _pressed |= buttons;

    // ------------------------------------------------------------------------------------------------
    // Adding and removing.

    /// <summary>⭐ `0x108598`: if the stack holds 32, index 0 is marked and flushed ("WARNING :: MESSAGE STACK
    /// OVERFLOW - REMOVING OLDEST MESSAGE"); the record is copied to index <see cref="Count"/>, the count
    /// grows, and UI sound 0x1E plays.</summary>
    public void Add(AdvisorRecordType type, short row, object obj = null, string text = null)
    {
        if (Count > Capacity - 1) { MarkForRemoval(0); FlushRemoval(); }
        var fresh = new AdvisorStackRecord();
        fresh.Reset();                                                    // 0x1089A0 on the caller's stack copy
        fresh.Type = type; fresh.Row = row; fresh.Object = obj; fresh.Text = row == -1 ? text : null;
        _slots[Count].CopyAll(fresh);
        Count++;
        UiSound?.Invoke(SoundAdded);
    }

    /// <summary>`0x16B9F8`: a record with its own text (row −1), type 4, no object -- the goal notices.
    /// Called by the viewer's <see cref="ParkAdvisor.GoalNotices"/> with <see cref="ParkGoals.Notices"/>.</summary>
    public void AddGoalNotice(string text) => Add(AdvisorRecordType.Goal, -1, null, text);

    /// <summary>`0x1087C8`: mark one index for removal (one pending slot; a second mark overwrites the first).</summary>
    public void MarkForRemoval(int index) => PendingRemoval = index;

    /// <summary>⭐ `0x1086C0`, the flush: if an index is pending and the stack is not empty -- a sliding-out
    /// pending record ends the delete; the records above shift down one (`0x1090D0`); the count drops (to 0:
    /// the stack closes, `0x13D418`); the cursor stays if it is below the removed index, else becomes
    /// `index − 1` (clamped to 0). Then the selection and the pending index are cleared.
    /// ⚠ A pending index at or past the count still drops the count: the tail goes (reproduced).</summary>
    public void FlushRemoval()
    {
        if (PendingRemoval < 0) return;
        int i = PendingRemoval;
        if (Count != 0)
        {
            if (i < Capacity && _slots[i].State == 1) Deleting = false;
            for (int k = i; k < Count - 1; k++) _slots[k].CopyShifted(_slots[k + 1]);
            Count--;
            if (Count == 0) Close();                                      // 0x13D418
            if (!(Cursor < i)) Cursor = i - 1;
            if (Cursor < 0) Cursor = 0;
        }
        Selected = null;
        PendingRemoval = -1;
    }

    /// <summary>⭐ `0x108808`, what opcode 8 (`0x1073F0`) does: find the FIRST record whose row equals
    /// <paramref name="row"/>; if found, flush a pending removal, mark THAT index and flush it. Returns
    /// whether one was found. ⚠ The index is taken BEFORE the first flush, so an older pending removal
    /// below it shifts the stack and the record removed is the one after the match (READ, MIPS
    /// `0x108860..0x108890`) -- reproduced.</summary>
    public bool RemoveByRow(short row)
    {
        for (int i = 0; i < Count; i++)
        {
            if (_slots[i].Row != row) continue;
            FlushRemoval();
            MarkForRemoval(i);
            FlushRemoval();
            return true;
        }
        return false;                                                     // " failed"
    }

    /// <summary>`0x1088E0`, a ride removed (`0x1E12E0`): the FIRST record whose object is it is marked for
    /// removal at the next flush; every LATER one is flushed at once (with the same index shift as
    /// <see cref="RemoveByRow"/>, so the record after each removed one is skipped). Reached from a delete through
    /// <see cref="ParkSim.Remove"/> → <see cref="ParkAdvisor.ObjectLeftPark"/>, BEFORE <see cref="ObjectRemoved"/>.</summary>
    public void RideRemoved(object ride)
    {
        int first = -1;
        for (int i = 0; i < Count; i++)
        {
            if (!ReferenceEquals(_slots[i].Object, ride)) continue;
            if (first == -1) first = i;
            else { FlushRemoval(); MarkForRemoval(i); FlushRemoval(); }
        }
        if (first != -1) MarkForRemoval(first);
    }

    /// <summary>`0x13D8C0`, ANY object removed (`0x14A7B0`): every type-2 record in turn is marked, so the
    /// LAST type-2 record is the one pending -- whatever its object (no comparison; READ quirk). It runs AFTER
    /// <see cref="RideRemoved"/> (`0x14A7B0` calls the object's `vt+0x10C` = `0x1E12E0` first), so it overwrites
    /// that call's pending mark -- see <see cref="ParkAdvisor.ObjectLeftPark"/>.</summary>
    public void ObjectRemoved()
    {
        for (int i = 0; i < Count; i++)
            if (_slots[i].Type == AdvisorRecordType.Object) MarkForRemoval(i);
    }

    // ------------------------------------------------------------------------------------------------
    // Open, close, input.

    /// <summary>`0x107FB8` (L2 while closed): only with records -- the cursor and top placed, not deleting, open,
    /// the pending removal DROPPED (−1), tutorial event 0xE, every record's slide reset, UI sound 0x1F. Returns
    /// whether it opened.
    ///
    /// ⚠⚠ DEVIATION, asked for by strawberry (2026-09-28): it opens on the NEWEST record. The console writes 0 to
    /// the cursor, the top and the smoothed scroll (MIPS `0x107FEC..0x107FF8`), and a record is appended at
    /// the end (`0x108598`), so there it opens on the OLDEST -- in a new park, the first goal notice. The top is
    /// placed where a run of Next presses would leave it, with the newest as the last of the four rows the
    /// scroll keeps in view; the view snaps its smoothed scroll there (AdvisorStackView).</summary>
    public bool Open()
    {
        if (Count == 0) return false;
        Cursor = Count - 1; ScrollTop = Math.Max(0, Count - VisibleRows);
        Deleting = false; IsOpen = true; PendingRemoval = -1;
        TutorialEvent?.Invoke(TutorialEventOpened);
        for (int i = 0; i < Count; i++) _slots[i].ResetSlide();
        UiSound?.Invoke(SoundOpenClose);
        return true;
    }

    /// <summary>`0x108068` (via `0x13D418`): if open, close with UI sound 0x1F.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        UiSound?.Invoke(SoundOpenClose);
    }

    /// <summary>The stack's half of `0x13D940`: open → L2 (logical 5) or Triangle (logical 1) closes; closed →
    /// L2 opens. ⚠ The closed-state preconditions of `0x13D940` (no laptop screen, no tool, no pending HUD
    /// state) are the view's to test before it calls this.</summary>
    public void Hud(bool l2, bool triangle)
    {
        if (IsOpen) { if (l2 || triangle) Close(); }
        else if (l2) Open();
    }

    /// <summary>⭐ `0x108220`, once a pass: every record not sliding out is slid (in when open `0x108A20`,
    /// out when closed `0x108A70`) and the cursor's becomes the selected one (state 2, else 0); a record
    /// sliding out (state 1) steps `0x1089F8` and, when off screen, is marked for removal and the delete
    /// ends. Then, when open with records, not deleting and a selected record not sliding out, the input
    /// `0x1080A8`; then the flush `0x1086C0`.</summary>
    public void Update()
    {
        var pressed = _pressed; _pressed = AdvisorStackButtons.None;
        for (int i = 0; i < Count; i++)
        {
            var r = _slots[i];
            if (r.State == 1)
            {
                if (SlideOut(r)) { MarkForRemoval(i); Deleting = false; }
                continue;
            }
            if (!IsOpen) SlideClosed(r, ScrollTop);
            else SlideOpen(r, i, ScrollTop);
            if (i == Cursor) { Selected = r; r.State = 2; }
            else r.State = 0;
        }
        if (IsOpen && Count != 0 && !Deleting && Selected != null && Selected.State != 1) Input(pressed);
        FlushRemoval();
    }

    /// <summary>`0x1080A8`: Next/Previous move the cursor (sound 0xD6) and the scroll keeps it within
    /// <see cref="VisibleRows"/>; Delete starts the slide-out (sound 0x1F); Select runs `0x108FF8`.</summary>
    void Input(AdvisorStackButtons pressed)
    {
        if ((pressed & AdvisorStackButtons.Next) != 0)
        {
            if (Cursor < Count - 1) { Cursor++; UiSound?.Invoke(SoundCursor); }
        }
        else if ((pressed & AdvisorStackButtons.Previous) != 0)
        {
            if (Cursor != 0) { Cursor--; UiSound?.Invoke(SoundCursor); }
        }
        else if ((pressed & AdvisorStackButtons.Delete) != 0)
        {
            Deleting = true;
            Selected.State = 1;
            UiSound?.Invoke(SoundOpenClose);
        }
        if (Cursor < ScrollTop) ScrollTop--;
        if (!(Cursor - ScrollTop < VisibleRows)) ScrollTop++;
        if ((pressed & AdvisorStackButtons.Select) != 0) SelectRecord(Selected);
    }

    /// <summary>`0x108FF8`: type 2 → the camera to the object, not deleting, close; type 3 → Replay
    /// (`0x107C18(adv, row)`). Then `0x181688(0)` (untraced pad call).</summary>
    void SelectRecord(AdvisorStackRecord r)
    {
        if (r.Type == AdvisorRecordType.Object)
        {
            FocusObject?.Invoke(r.Object);
            Deleting = false;
            Close();
        }
        else if (r.Type == AdvisorRecordType.Tutorial) Replay?.Invoke(r.Row);
    }

    /// <summary>`0x1089F8`: `x += (−64 − x) &gt;&gt; 1`; done when `x &lt; −63`.</summary>
    static bool SlideOut(AdvisorStackRecord r)
    {
        r.SlideX += (-0x40 - r.SlideX) >> 1;
        return r.SlideX < -0x3F;
    }

    /// <summary>`0x108A20` (open): until `counter/2 ≥ i − top` the record waits off screen (−64) and the
    /// counter climbs, so they come in one by one; then it slides to 0, or +24 when selected. `x += (t − x) &gt;&gt; 1`.
    /// ⚠ "Selected" is the record's state from the PREVIOUS pass (the update sets it after the slide).</summary>
    static void SlideOpen(AdvisorStackRecord r, int i, int top)
    {
        int target;
        if ((r.SlideCounter >> 1) < i - top) { target = -0x40; r.SlideCounter++; }
        else target = r.State == 2 ? 0x18 : 0;
        r.SlideX += (target - r.SlideX) >> 1;
    }

    /// <summary>`0x108A70` (closed): the counter counts down and the record stays at 0 while
    /// `counter/2 − top &gt; 0`, else heads for −64; `x += (t − x) &gt;&gt; 2`.</summary>
    static void SlideClosed(AdvisorStackRecord r, int top)
    {
        int target = -0x40;
        if (r.SlideCounter > 0)
        {
            r.SlideCounter--;
            if ((r.SlideCounter >> 1) - top > 0) target = 0;
        }
        r.SlideX += (target - r.SlideX) >> 2;
    }

    // ------------------------------------------------------------------------------------------------
    // Save and load (per park; findings/advisor-messages.md §11).

    /// <summary>One saved record, the shape of `0x1C2D50`: s16 row; if −1 the text (u8 length + bytes);
    /// u8 type; u8 object kind or 0xFF; for a type-2 record with an object, u8 its index in its kind's list
    /// (`vt+0xA4` and `0x153FF8`).</summary>
    public readonly record struct SavedRecord(short Row, string Text, byte Type, byte ObjectKind, byte ObjectIndex)
    {
        public bool HasObject => ObjectKind != 0xFF;
    }

    /// <summary>The per-park snapshot for the save coordinator (astraclaw's `CaptureState`/`RestoreState`).
    /// Only the records -- open/cursor/selection are not saved (`0x1C2D50` writes none of them). ⚠ The
    /// advisor's ring, pending/current message, state and the rule object have NO save shape on purpose:
    /// nothing saves them natively (research §11).</summary>
    public readonly record struct State(IReadOnlyList<SavedRecord> Records);

    /// <summary>`0x1C2D50`: every record but type 4, oldest first. <paramref name="identify"/> gives an
    /// attached object's (kind, index in its kind's list) -- the port's objects are rides; a type-2 record
    /// whose object it cannot identify is saved without one (0xFF), as a null object is.</summary>
    public State CaptureState(Func<object, (byte Kind, byte Index)?> identify)
    {
        var list = new List<SavedRecord>();
        for (int i = 0; i < Count; i++)
        {
            var r = _slots[i];
            if (r.Type == AdvisorRecordType.Goal) continue;
            byte kind = 0xFF, index = 0;
            if (r.Type == AdvisorRecordType.Object && r.Object != null && identify?.Invoke(r.Object) is { } id)
            { kind = id.Kind; index = id.Index; }
            list.Add(new SavedRecord(r.Row, r.Row == -1 ? r.Text ?? "" : null, (byte)r.Type, kind, index));
        }
        return new State(list);
    }

    /// <summary>`0x1608E8`, run at park start AFTER the HUD init emptied the stack (`0x13CE70` →
    /// `0x107F90`): each record is re-added through `0x108598` (so the add sound plays per record, as
    /// natively). <paramref name="resolve"/> answers `0x153DB0(kind, index)`.</summary>
    public void RestoreState(State state, Func<byte, byte, object> resolve)
    {
        Clear();
        foreach (var s in state.Records ?? Array.Empty<SavedRecord>())
            Add((AdvisorRecordType)s.Type, s.Row, s.HasObject ? resolve?.Invoke(s.ObjectKind, s.ObjectIndex) : null, s.Text);
    }

    /// <summary>`0x107F90` (HUD init, `0x13CE70`): empty, closed, nothing pending.</summary>
    public void Clear()
    {
        Count = 0; IsOpen = false; Deleting = false; Cursor = 0; ScrollTop = 0; PendingRemoval = -1; Selected = null;
        foreach (var s in _slots) s.Reset();
    }
    // CORE graph snapshot: unlike the native save above, retains ALL slots, goals, input and UI progress.
    public sealed record CoreRecord([property: System.Text.Json.Serialization.JsonRequired] AdvisorRecordType Type, [property: System.Text.Json.Serialization.JsonRequired] int State, [property: System.Text.Json.Serialization.JsonRequired] string Text, [property: System.Text.Json.Serialization.JsonRequired] short Row, [property: System.Text.Json.Serialization.JsonRequired] int Lifetime, [property: System.Text.Json.Serialization.JsonRequired] int SlideCounter, [property: System.Text.Json.Serialization.JsonRequired] int SlideX, [property: System.Text.Json.Serialization.JsonRequired] int SlideY, [property: System.Text.Json.Serialization.JsonRequired] string ObjectId);
    public sealed record CoreState
    {
        public required int Version { get; init; }
        public required CoreRecord[] Slots { get; init; }
        public required int Count { get; init; }
        public required bool IsOpen { get; init; }
        public required bool Deleting { get; init; }
        public required int Cursor { get; init; }
        public required int ScrollTop { get; init; }
        public required int PendingRemoval { get; init; }
        public required int SelectedSlot { get; init; }
        public required AdvisorStackButtons Pressed { get; init; }
        public required string UiSoundId { get; init; }
        public required string TutorialEventId { get; init; }
        public required string FocusObjectId { get; init; }
        public required string ReplayId { get; init; }
    }
    /// <summary>Caller must freeze the park between passes (including UI input and callbacks).</summary>
    public CoreState CaptureCoreState(AdvisorStateBindings b, bool quiescent)
    {
        if (!quiescent) throw new InvalidOperationException("Stack capture requires quiescence");
        return new CoreState { Version = 1, Slots = _slots.Select(r => new CoreRecord(r.Type, r.State, r.Text,
            r.Row, r.Lifetime, r.SlideCounter, r.SlideX, r.SlideY, b.Identify(r.Object))).ToArray(),
            Count = Count, IsOpen = IsOpen, Deleting = Deleting, Cursor = Cursor, ScrollTop = ScrollTop,
            PendingRemoval = PendingRemoval, SelectedSlot = Selected == null ? -1 : Array.IndexOf(_slots, Selected),
            Pressed = _pressed, UiSoundId = b.Identify(UiSound), TutorialEventId = b.Identify(TutorialEvent),
            FocusObjectId = b.Identify(FocusObject), ReplayId = b.Identify(Replay) };
    }
    public static AdvisorMessageStack FromCoreState(CoreState s, AdvisorStateBindings b)
    {
        if (s == null || s.Version != 1 || s.Slots?.Length != Capacity || (uint)s.Count > Capacity ||
            (uint)s.Cursor >= Capacity || (uint)s.ScrollTop >= Capacity || s.PendingRemoval < -1 ||
            s.SelectedSlot < -1 || s.SelectedSlot >= Capacity || ((int)s.Pressed & ~15) != 0 ||
            s.Slots.Any(r => r == null || !Enum.IsDefined(r.Type) || r.State < 0 || r.State > 2 ||
                r.SlideCounter < 0 || r.SlideX < -64 || r.SlideX > 24))
            throw new InvalidDataException("Invalid advisor message stack");
        // Validate/resolve EVERY reference before writing a destination. No Add, Clear, Update, sounds or events.
        var objects = s.Slots.Select(r => b.Resolve<object>(r.ObjectId)).ToArray();
        var sound = b.Resolve<Action<int>>(s.UiSoundId); var tutorial = b.Resolve<Action<int>>(s.TutorialEventId);
        var focus = b.Resolve<Action<object>>(s.FocusObjectId); var replay = b.Resolve<Action<short>>(s.ReplayId);
        var result = new AdvisorMessageStack { Count = s.Count, IsOpen = s.IsOpen, Deleting = s.Deleting,
            Cursor = s.Cursor, ScrollTop = s.ScrollTop, PendingRemoval = s.PendingRemoval, _pressed = s.Pressed,
            UiSound = sound, TutorialEvent = tutorial, FocusObject = focus, Replay = replay };
        for (int i = 0; i < Capacity; i++)
        {
            var r = s.Slots[i]; var d = result._slots[i];
            d.Type = r.Type; d.State = r.State; d.Text = r.Text; d.Row = r.Row; d.Lifetime = r.Lifetime;
            d.SlideCounter = r.SlideCounter; d.SlideX = r.SlideX; d.SlideY = r.SlideY; d.Object = objects[i];
        }
        result.Selected = s.SelectedSlot < 0 ? null : result._slots[s.SelectedSlot];
        return result;
    }

}
