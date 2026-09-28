namespace TPW.PS2.Data;

/// <summary>The message request, the "8-byte object" (`0x107C90` ctor, `0x107CA8` id, `0x107CB0` object,
/// `0x107CC0` submit; findings/advisor-messages.md §1.1). ⚠ A request whose id is never set is id 276
/// (<see cref="UnsetId"/>, the ctor's default), past the 275-record table.</summary>
public readonly record struct AdvisorRequest(ushort Id, object Object = null)
{
    /// <summary>`0x107C90` writes 0x114.</summary>
    public const ushort UnsetId = 0x114;
    /// <summary>`+8`, set by `0x107CB0`: the request carries an object.</summary>
    public bool HasObject => Object != null;
}

/// <summary>The advisor's six states, `adv+2` (`0x1066B0`, table `0x358930`).</summary>
public enum AdvisorState : byte
{
    /// <summary>The 50-tick start delay.</summary>
    StartDelay = 0,
    /// <summary>Idle: the rule scheduler runs, and a pending/queued message is taken.</summary>
    Idle = 1,
    /// <summary>The head rises (enter animation, APS section 5 record 13); audio ducked.</summary>
    Entering = 2,
    /// <summary>The speech plays (talk record 0..6 looping, lips).</summary>
    Speaking = 3,
    /// <summary>The head drops (record 14).</summary>
    Exiting = 4,
    /// <summary>The 100-tick cooldown.</summary>
    Cooldown = 5,
}

/// <summary>⚠ ADAPTER for the advisor's head model (`adv+0x238`, `advisor.mps`/`.aps`, registry id 488): what
/// the state machine asks of it. The view supplies the real one (game/AdvisorHead.cs); null is the console's
/// own no-model path (every animation test passes at once). <see cref="AdvisorTimedHead"/> is the stand-in with
/// the records' lengths.</summary>
public interface IAdvisorHead
{
    /// <summary>`vt+0x5C` → `0x17C5D8`: play APS section 5 record <paramref name="record"/> at speed 1.0 --
    /// 13 enter (flags 0), 0..6 talk (flags 1, loop), 14 exit (flags 0, or 2 = cut the current one short).
    /// ⭐ Asking for the record already playing does NOTHING (category 13's arm compares first).</summary>
    void Play(int record, int flags);
    /// <summary>`0x17C880(model, 0)` (channel 0 end-held, flags &amp; 4) or `0x17C8C8(model, 0) == 0`: the
    /// state machine's "the animation is done" in states 2 and 4.</summary>
    bool ChannelDone { get; }
    /// <summary>⚠ Port only: one advisor pass of <paramref name="milliseconds"/> has begun (a head that
    /// animates at render rate may ignore it).</summary>
    void Step(int milliseconds);
    /// <summary>`0x107160` (via `0x107B90`, at the take of a VOICED message): dress the model for costume selector
    /// <paramref name="selector"/> (`adv+0x1F8`, the variant's `+2` byte) -- every prop hidden, hands and plain
    /// antennae shown, then the selector's fittings (findings/advisor-messages.md §4.6). A head with no model
    /// ignores it.</summary>
    void Dress(int selector) { }
    /// <summary>`0x105FC8(adv, shape)`: show mouth mesh <paramref name="shape"/> (0 normal, 1 aah, 2 eee, 3 ooh,
    /// 4 sss) and hide the other four -- bit `0x8000` of each mesh record's flags word (§4.4). A head with no
    /// model ignores it.</summary>
    void Mouth(int shape) { }
}

/// <summary>⭐ CHANNEL 0 of the advisor's model, as `0x17C5D8` (category 13), `0x1ABC80` (the request) and
/// `0x1AC048` (the update) drive it for the section-5 records the advisor asks for (findings/advisor-messages.md
/// §4.7; findings/bus-native-animation.md "Command parameters" and "Time, update and loop math"; re-read in
/// the MIPS `0x1ABD04..0x1ABE88` and `0x1AC180..0x1AC2EC` for this step):
/// <code>
///   request(record, flags):
///     the same record as the channel's current → nothing                        (0x17C5D8, category 13's arm)
///     the current is valid, frame ≤ length, not start/end-held (flags &amp; 6 == 0)
///       and the request has no flag 2        → QUEUED in the channel's one slot (+0x24.., 12 = empty)
///     else: flag 2 empties the slot; the record starts now                    (0x1ABE58..0x1ABE84 → 0x1AB780)
///   update: frame = elapsed·30/1000 (0x1AB710, speed 1.0); on the first update STRICTLY past the length
///     (`c.olt.s`, 0x1AC188): a queued record starts (it beats the loop); else a looping one starts again;
///     else the channel is END-HELD (flags |= 4, the record stays selected)
/// </code>
/// So a normal exit (record 14, flags 0) asked for while a talk record loops WAITS for that pass of the loop
/// to end -- the talk record was picked so that its length covers the speech (§4.3) -- while a cut exit
/// (flags 2: an interrupt, the skip) starts at once, and an exit asked for while the enter is still rising
/// waits for the head to finish rising.
///
/// ⚠ A restart (the loop, a queued record) begins at 0 on the update that found the end: the native
/// `0x1AB780` re-reads the clock then, so the overshoot past the length is dropped here too (INFERRED from
/// the start path; the timing initialiser `0x1A8920` was not re-read for this). "Section 12" (`0x17C8C8`) is
/// the empty value of the channel's section and of the queue slot (READ `0x1AC1B0..0x1AC1B4`): a channel with
/// no record here.</summary>
public sealed partial class AdvisorHeadChannel
{
    /// <summary>A record's authored length (APS record `+4`), frames.</summary>
    readonly Func<int, int> _frames;
    public AdvisorHeadChannel(Func<int, int> frames) { _frames = frames ?? throw new ArgumentNullException(nameof(frames)); }

    /// <summary>`0x1AB710`: `(clock − start)·30.0/1000.0`, at speed 1.0.</summary>
    public const int FramesPerSecond = 30;
    /// <summary>The empty section (`0x17C8C8` reads `section != 12`): no record on the channel.</summary>
    public const int None = -1;

    /// <summary>The record on channel 0, <see cref="None"/> before any.</summary>
    public int Record { get; private set; } = None;
    /// <summary>Channel `+0x14` bit 1 (from the request's flags &amp; 1).</summary>
    public bool Looping { get; private set; }
    /// <summary>Channel `+0x14` bit 4: past the end of a record that does not loop, nothing queued.</summary>
    public bool EndHeld { get; private set; }
    /// <summary>Elapsed time in the current record, ms.</summary>
    public int ElapsedMs { get; private set; }
    /// <summary>The queued request (`+0x24`/`+0x28`/`+0x2C`), <see cref="None"/> for the empty slot.</summary>
    public int Queued { get; private set; } = None;
    public int QueuedFlags { get; private set; }
    /// <summary>Records started, for the log and the checks.</summary>
    public int Starts { get; private set; }

    public int Length(int record) => record == None ? 0 : _frames(record);
    /// <summary>The authored frame now, `elapsed·30/1000`; a held record stays on its last frame.</summary>
    public float Frame => EndHeld ? Length(Record) : ElapsedMs * (float)FramesPerSecond / 1000f;

    /// <summary>`0x17C5D8` → `0x1ABC80(speed 1.0, section 5, record, flags, channel 0)`.</summary>
    public void Play(int record, int flags)
    {
        if (record == Record) return;                                      // 0x17C5D8: the same → nothing
        bool unfinished = Record != None && !EndHeld && ElapsedMs * (float)FramesPerSecond / 1000f <= Length(Record);
        if (unfinished && (flags & 2) == 0) { Queued = record; QueuedFlags = flags; return; }   // 0x1ABD50
        if ((flags & 2) != 0) Queued = None;                                // 0x1ABE64: +0x24 = 12
        Start(record, flags);
    }

    void Start(int record, int flags)
    {
        Record = record; Looping = (flags & 1) != 0; EndHeld = false; ElapsedMs = 0; Starts++;
    }

    /// <summary>`0x1AC048` after <paramref name="milliseconds"/> more on the clock.</summary>
    public void Step(int milliseconds)
    {
        if (Record == None || EndHeld) return;
        ElapsedMs += milliseconds;
        if (!(Length(Record) < ElapsedMs * (float)FramesPerSecond / 1000f)) return;   // 0x1AC188 c.olt.s
        if (Queued != None) { int q = Queued, f = QueuedFlags; Queued = None; Start(q, f); }   // 0x1AC2C4
        else if (Looping) { ElapsedMs = 0; Starts++; }                     // 0x1AC254: the same record again
        else EndHeld = true;                                               // 0x1AC298: special 14, flags |= 0x14
    }

    /// <summary>The state machine's "done" in states 2 and 4: `0x17C880` (end-held) or `0x17C8C8 == 0`
    /// (section 12, nothing selected).</summary>
    public bool Done => Record == None || EndHeld;
}

/// <summary>⚠ STAND-IN HEAD: no model, only the records' lengths in APS frames (findings/advisor-messages.md
/// §4.3, §4.7: enter 13 = 30, exit 14 = 20, talk 0..6 = 50, 75, 100, 125, 150, 225, 275) on the real channel
/// (<see cref="AdvisorHeadChannel"/>), advanced at 30 APS frames a second by the milliseconds the advisor passes
/// it -- the console's own rate: an APS channel's frame is `(ms − start)·30/1000` on the real-time ms clock
/// (findings/clock-rate.md §6). ⚠ What it lacks is the model itself; the view's head (game/AdvisorHead.cs) has
/// both.</summary>
public sealed class AdvisorTimedHead : IAdvisorHead
{
    /// <summary>`advisor.aps` section 5 record lengths, frames.</summary>
    public static int Frames(int record) => record switch
    {
        0 => 50, 1 => 75, 2 => 100, 3 => 125, 4 => 150, 5 => 225, 6 => 275, 13 => 30, 14 => 20, _ => 0,
    };
    public const int FramesPerSecond = AdvisorHeadChannel.FramesPerSecond;

    public AdvisorHeadChannel Channel { get; } = new(Frames);
    /// <summary>The record on channel 0, −1 before any.</summary>
    public int Record => Channel.Record;
    public bool Looping => Channel.Looping;
    /// <summary>Elapsed time in the current record, ms.</summary>
    public int ElapsedMs => Channel.ElapsedMs;

    public void Play(int record, int flags) => Channel.Play(record, flags);
    public bool ChannelDone => Channel.Done;
    public void Step(int milliseconds) => Channel.Step(milliseconds);
}

/// <summary>What <see cref="ParkAdvisor"/> presented (`0x1078E8`): for the log and for step B.</summary>
/// <param name="TextAdded">A message-stack record was appended (flags bit 0, row ≠ 310).</param>
/// <param name="SoundId">The current variant's 1-based sound id (0 = silent), read before the rotation.</param>
/// <param name="SpeechMs">The hook's length when a stream started, else 0.</param>
/// <param name="TalkRecord">The talk animation chosen by length, −1 without a stream.</param>
public readonly record struct AdvisorPlayback(ushort Id, AdvisorRecordType Kind, object Object, int Variant, ushort SoundId,
                                              short TextRow, bool TextAdded, int SpeechMs, int TalkRecord);

/// <summary>⭐⭐ THE ADVISOR (TheAdvisor, 0x270 bytes at `[0x2AA720]`; ctor `0x106080`, update `0x1066B0`, submit
/// `0x1075F0`), one per park, built at park start. READ in findings/advisor-messages.md §1..§4, §6, §11 and
/// findings/advisor-rules.md §1, §2, §4, re-read for this step against the decompiles and the MIPS
/// (`0x1066B0..0x106D80`, `0x107640`, `0x107760`, `0x1078E8`, `0x1075F0`, `0x107A90..0x107B20`).
///
/// ⭐ What it does, per pass (<see cref="Update"/>, called once per simulation pass on the same pass as the
/// staff, after guests and objects -- `0x151800` → `0x1066B0`):
/// <code>
///   0 StartDelay  50 ticks, then tutorial event 0 (stub), Idle, and -- not the test park -- the greeting
///                 (202 goals already achieved / 171 no gold ticket ever earned / 188) and the goal notices
///   1 Idle        flags bit 3: one scheduler step (AdvisorScheduler); then the pending immediate, else (flags
///                 0x20, ring not empty) the ring's OLDEST: enter animation if it has a voice, costume, modal
///   2 Entering    duck audio; when the head is down/done: play it (text to the stack, the voice) → 3
///   3 Speaking    until the speech ends (123 BANKRUPTED: game over) → head down, and in the same tick 4
///   4 Exiting     restore audio; when the head is done: modal → flags |= 0x21; 100 ticks → 5
///   5 Cooldown    unlock; count down (or end at once: flags 0x10 clear, or an immediate waiting) → 1
///   every tick, in states 2..4 of a MODAL message: the skip button (Triangle) -- see SkipHeld
/// </code>
/// ⭐ A SILENT message still costs the cycle: 1 + 1 + 1 + 100 ticks with no voice (§3.2).
///
/// ⭐ UNITS. A TICK is one call of <see cref="Update"/> = one simulation pass = one rendered frame, 40 ms at the
/// console's 25 passes a second (findings/clock-rate.md; <see cref="ParkSim.TickMilliseconds"/>), so the
/// 50-tick delay is 2 s and the 100-tick cooldown 4 s. The speech runs on the ms clock `0x147158`, measured here
/// through <see cref="MillisecondsPerTick"/>, and the head's APS frames are real-time 30 a second on that clock
/// (`currentFrame = (ms − start)·30/1000`, findings/clock-rate.md §4, §6) -- which settles the research's open
/// "tick ↔ 30 fps frame" question: 30 frames are 1000 ms, 25 passes -- and since a record ends on the first update
/// STRICTLY past its length (<see cref="AdvisorHeadChannel"/>), the enter holds 26 passes and the 20-frame exit 17.
///
/// ⚠ ADAPTERS, each labelled where it lives: <see cref="SpeechLength"/> (the stream's length, audio is the
/// view's), <see cref="Head"/> (the model), <see cref="SkipHeld"/> (the pad),
/// <see cref="GoalsAlreadyAchieved"/> and <see cref="GoldTicketsEarned"/> (the greeting's globals),
/// <see cref="GoalNotices"/> (the port has no goals record), the random stream.
///
/// ⚠ OUT OF SCOPE, said: the TUTORIAL -- the dispatcher `0x107390` → `0x206358` and its 22 handlers; its
/// entry points are the stubs <see cref="TutorialEvent"/> and <see cref="TutorialMessage"/> (`0x107C18`). The lip
/// step and the mouth shapes are here (<see cref="MouthShape"/>, the <see cref="Lips"/> and <see cref="Rand"/>
/// adapters); the voice, the ducking and the drawing are the view's (game/Viewer.Advisor.cs). The vestigial
/// idle timer (`+0x108`, `+0x225`, `0x107550`/`0x107BD8`) has no visible effect and is not ported -- ⚠ it
/// draws `rand(5)` from the game's one stream, which the port does not share.
///
/// ⚠ NOT SAVED (research §11): the ring, the pending and current message, the state, the rule object. The
/// message stack IS saved per park (<see cref="AdvisorMessageStack.CaptureState"/>). CORE snapshots are a
/// separate, complete continuation contract: see CaptureState/AllocateShell/Hydrate in ParkAdvisor.State.cs.</summary>
public sealed partial class ParkAdvisor
{
    // ---- flags byte adv+0 (findings/advisor-messages.md §1.2) -------------------------------------
    /// <summary>`0x01`: text to the message stack; opcode 8 acts.</summary>
    public const byte FlagText = 0x01;
    /// <summary>`0x02`: voice, head and modal handling.</summary>
    public const byte FlagVoice = 0x02;
    /// <summary>`0x08`: the rule object exists and runs; the event counters count.</summary>
    public const byte FlagRules = 0x08;
    /// <summary>`0x10`: enforce the 100-tick cooldown (an immediate may cut it).</summary>
    public const byte FlagCooldown = 0x10;
    /// <summary>`0x20`: queue mode (ids outside 208..274 queue instead of interrupting); the ring may drain.</summary>
    public const byte FlagQueue = 0x20;
    /// <summary>`0x40`: the tutorial dispatcher is active (the Tutorial On/Off option).</summary>
    public const byte FlagTutorial = 0x40;
    /// <summary>`0x151498`: every park starts at 0x37, `| 0x40` with the tutorial option, `| 8` unless the test park.</summary>
    public const byte ParkStartFlags = 0x37;
    /// <summary>`0x152668`: ride-along writes 6 (voice only: no text, no queue, no rules, no counters).</summary>
    public const byte RideAlongFlags = 6;

    /// <summary>`0x106080` `+4 = 0x32`.</summary>
    public const int StartDelayTicks = 0x32;
    /// <summary>State 4 → 5 `+4 = 100`.</summary>
    public const int CooldownTicks = 100;
    /// <summary>The countdown written on an interrupt and at the end of speech (unused by state 4 except as a count).</summary>
    public const int ExitCountdown = 0x32;
    /// <summary>`0x107760`: `% 0x14`.</summary>
    public const int RingSlots = 0x14;
    /// <summary>`0x1075F0`/`0x1074C0`: `(u16)(id − 0xD0) &lt; 0x43` -- the tutorial/welcome block 208..274.</summary>
    public const ushort ImmediateFirst = 0xD0, ImmediateCount = 0x43;
    /// <summary>123 BANKRUPTED: modal with voice on; its end (or skip) calls `0x13BDD0`.</summary>
    public const ushort Bankrupted = 0x7B;
    /// <summary>Greeting ids (state 0): goals already achieved, the Lost Kingdom first goals, the blank.</summary>
    public const ushort GreetingAchieved = 0xCA, GreetingFirst = 0xAB, GreetingOther = 0xBC;
    /// <summary>The skip's UI sound (`0x111150(audio, 0, 299, 0)`).</summary>
    public const int SoundSkip = 299;
    /// <summary>Head records (section 5): enter 13, exit 14.</summary>
    public const int RecordEnter = 13, RecordExit = 14;

    AdvisorCatalogue _catalogue;
    readonly (ushort Id, AdvisorRecordType Kind, object Object)[] _ring = new (ushort, AdvisorRecordType, object)[RingSlots];
    byte[] _count, _variant;

    /// <param name="producers">The rule object's variables (<see cref="AdvisorProducers"/>). Unused without flag 8.</param>
    /// <param name="testPark">`0x153410()` = `[0x2B72A8]`, the Rollercoaster Test Park: no rule object, no greeting.</param>
    /// <param name="tutorial">`0x1C3940()`, the profile's tutorial flag (Game Options' Tutorial On/Off).</param>
    /// <param name="world">`0x14E170()`, the world index -- messages 14 (4 in world 0), 54 and 69 (4 in world 3)
    /// get their variant counts from it; −1 = none of those.</param>
    /// <param name="random">`0x1448E0(n)` for the initial variants. ⚠ Natively the game's one stream; the port's
    /// streams are not the console's in any case. Default: a fixed seed.</param>
    public ParkAdvisor(AdvisorCatalogue catalogue, AdvisorRules rules, ParkClock clock, IAdvisorProducers producers,
                       bool testPark = false, bool tutorial = false, int world = -1, Func<int, int> random = null)
    {
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (random == null) { var rng = new Random(0xAD715); random = n => n <= 0 ? 0 : rng.Next(n); }
        TestPark = testPark;
        // 0x151498: 0x37, | 0x40 with the tutorial (0x230260("DisableTutorial") is a stub returning 0), | 8 off the test park.
        Flags = (byte)(ParkStartFlags | (tutorial ? FlagTutorial : 0) | (testPark ? 0 : FlagRules));
        SavedFlags = 0xFF;
        State = AdvisorState.StartDelay;
        Countdown = StartDelayTicks;
        // 0x106080: the rule object only with flag 8 -- the loader 0x10DA78 reads the day now.
        if ((Flags & FlagRules) != 0)
            Scheduler = new AdvisorScheduler(rules ?? throw new ArgumentNullException(nameof(rules)),
                                             producers ?? throw new ArgumentNullException(nameof(producers)),
                                             () => unchecked((uint)Clock.TotalDays));
        // 0x106338..0x106418: per message, the world's counts for 14/54/69; variant 0 for counts 0/1, else rand(count).
        _count = new byte[AdvisorCatalogue.MessageCount];
        _variant = new byte[AdvisorCatalogue.MessageCount];
        for (int i = 0; i < AdvisorCatalogue.MessageCount; i++)
        {
            byte c = catalogue.Messages[i].VariantCount;
            if (i == 0x36 || i == 0x45) c = (byte)(world == 3 ? 4 : 3);
            else if (i == 0x0E) c = (byte)(world == 0 ? 4 : 3);
            _count[i] = c;
            _variant[i] = c <= 1 ? (byte)0 : (byte)random(c);
        }
        Stack = new AdvisorMessageStack { TutorialEvent = n => TutorialEvent(n), Replay = r => TutorialMessage(r) };
        _mouthRand = new NewlibRand(0xAD715);
        Rand = _mouthRand.Next;
    }

    public ParkClock Clock { get; private set; }
    /// <summary>The message stack `0x3928C8` (a HUD object natively; per park here, as its save is).</summary>
    public AdvisorMessageStack Stack { get; private set; }
    /// <summary>The rule object `adv+0x264`, or null (test park).</summary>
    public AdvisorScheduler Scheduler { get; private set; }
    public bool TestPark { get; private set; }

    /// <summary>`adv+0`.</summary>
    public byte Flags { get; private set; }
    /// <summary>`adv+1`: flags saved by <see cref="EnterRideAlong"/>, 0xFF for none.</summary>
    public byte SavedFlags { get; private set; }
    /// <summary>`adv+2`.</summary>
    public AdvisorState State { get; private set; }
    /// <summary>`adv+4`, s16.</summary>
    public short Countdown { get; private set; }

    // ---- the ring (+8 + 8i, head +0xA8, tail +0xA9) and the pending immediate (+0xAC..+0xB4) ----
    /// <summary>`+0xA8`, the next write.</summary>
    public int RingHead { get; private set; }
    /// <summary>`+0xA9`, the next read (the OLDEST).</summary>
    public int RingTail { get; private set; }
    /// <summary>Messages held, 0..19 (empty is head == tail).</summary>
    public int RingCount => (RingHead - RingTail + RingSlots) % RingSlots;
    /// <summary>The held ids, oldest first.</summary>
    public IReadOnlyList<ushort> RingIds
    {
        get { var l = new List<ushort>(); for (int i = RingTail; i != RingHead; i = (i + 1) % RingSlots) l.Add(_ring[i].Id); return l; }
    }
    /// <summary>`+0xB4`/`+0xAC`: an immediate message is waiting, and which.</summary>
    public bool PendingSet { get; private set; }
    public ushort PendingId { get; private set; }
    /// <summary>Queue overflows (`"*** ADVISOR MESSAGE QUEUE OVERFLOW! ***"`, printed by the stub `0x107E48`).</summary>
    public int Overflows { get; private set; }

    // ---- the presentation (+0xB5..+0xB8, +0x1F8, +0x230, +0x23C..) --------------------------------
    /// <summary>`+0xB6`: the message being presented.</summary>
    public ushort CurrentId { get; private set; }
    /// <summary>`+0xB5`: the current message is modal (ids 208..274 and 123, with voice on).</summary>
    public bool Modal { get; private set; }
    /// <summary>`0x1817C0(1)`: all game pad input is locked (a modal message). The view honours it.</summary>
    public bool PadLocked { get; private set; }
    /// <summary>`0x13D800`/`0x13D870`: the HUD's four button labels are saved and slot 0 shows "Cancel"
    /// (row <see cref="CancelLabelRow"/>) while a modal message runs.</summary>
    public bool CancelLabelShown { get; private set; }
    public const int CancelLabelRow = 0x1C;
    /// <summary>`+0xB8`: the skip button was seen down.</summary>
    public bool SkipLatched { get; private set; }
    /// <summary>`+0x230`: the enter animation was played for the current message.</summary>
    public bool EnterPlayed { get; private set; }
    /// <summary>`+0x1F8`: the costume selector of the last VOICED message (a silent one keeps the previous), −1 before any.</summary>
    public int Costume { get; private set; } = -1;
    /// <summary>States 2 → 4: the music and SFX targets are ducked to 25 (`0x2ABE28`/`0x2ABE30`); state 4 restores.</summary>
    public bool Ducking { get; private set; }
    /// <summary>A speech stream is playing (`+0x23C` != 0 and still playing).</summary>
    public bool Speaking { get; private set; }
    /// <summary>The playing speech: message, variant, sound id, its length and how far in, ms (step B's voice and lips).</summary>
    public ushort SpeechMessage { get; private set; }
    public int SpeechVariant { get; private set; }
    public ushort SpeechSoundId { get; private set; }
    public int SpeechLengthMs { get; private set; }
    public int SpeechElapsedMs { get; private set; }
    /// <summary>`+0x234`: the lip gate -- set when a voice starts, toggled by each lip mark the track passes and
    /// cleared by its terminator (`0x105F30`), cleared by the head going down (`0x106600`).</summary>
    public bool LipGate { get; private set; }
    /// <summary>`+0x248`, the mouth shape 0..4 (`0x105FC8`: 0 normal, 1 aah, 2 eee, 3 ooh, 4 sss). The start of a
    /// voice STORES 0 without touching the meshes (`0x1078E8`); every exit calls `0x105FC8(0)`.</summary>
    public int MouthShape { get; private set; }
    /// <summary>`+0x244` != 0: the playing voice has a lip track (message 268's `PS2_` has none, so it flaps at
    /// random for its whole length -- §4.4).</summary>
    public bool LipTrackAttached => _lip != null;
    /// <summary>Mouth changes (`0x105FC8` calls) -- instrumentation.</summary>
    public long MouthChanges { get; private set; }
    LipTrack.Playback _lip;
    NewlibRand _mouthRand;
    /// <summary>Updates run -- instrumentation.</summary>
    public long Ticks { get; private set; }

    // ---- adapters and hooks --------------------------------------------------------------------------
    /// <summary>⚠ ADAPTER for `0x111150(audio, 0xB, sound, &amp;handle)` + `0x111990(handle)`: the speech stream's
    /// length in ms for (message, variant, 1-based sound id); 0 or less = no stream started (handle 0). The
    /// view supplies it (audio lives there); the audit supplies a fixed value. Null = no stream.</summary>
    public Func<int, int, ushort, int> SpeechLength { get; set; }
    /// <summary>⚠ The model; null = the console's no-model path.</summary>
    public IAdvisorHead Head { get; set; }
    /// <summary>Milliseconds one pass advances the speech and the head: 40, the console's pass at 25 a second
    /// (findings/clock-rate.md). ⚠ A ceiling natively -- a frame that overruns 40 ms is longer.</summary>
    public int MillisecondsPerTick { get; set; } = (int)ParkSim.TickMilliseconds;
    /// <summary>⚠ INPUT: the raw skip button (`0x1817A0(0) &amp; 0x200`, Triangle; Cross `0x40` under language 8,
    /// which this PAL build cannot select), read every pass BYPASSING the pad lock. The view sets it.</summary>
    public bool SkipHeld { get; set; }
    /// <summary>⚠ HOOK for `0x1C3790(world, park) == 3` (`0x3975C8[world][park]`, named from use): greet with
    /// 202 GOALS_ALREADY_ACHIEVED. The port keeps no goal status: null answers false.</summary>
    public Func<bool> GoalsAlreadyAchieved { get; set; }
    /// <summary>`[0x3975C0]` (<see cref="ParkAwards.GoldTicketsEarned"/>): 0 greets with 171. Null answers 0.</summary>
    public Func<int> GoldTicketsEarned { get; set; }
    /// <summary>⚠ HOOK for `0x16BA58(cal)`, the goal notices (type-4 stack records, rows 376/894/515) posted at
    /// the end of the start delay. The port has no goals record: null does nothing.</summary>
    public Action GoalNotices { get; set; }
    /// <summary>`0x13BDD0` (game over, INFERRED name): 123 BANKRUPTED ended or was skipped.</summary>
    public Action GameOver { get; set; }
    /// <summary>The skip's UI sound 299.</summary>
    public Action<int> UiSound { get; set; }
    /// <summary>Log hook: a request was submitted (true = immediate).</summary>
    public Action<AdvisorRequest, bool> Submitted { get; set; }
    /// <summary>Log hook: a message was presented (`0x1078E8`).</summary>
    public Action<AdvisorPlayback> Played { get; set; }
    /// <summary>⚠ ADAPTER for the lip tracks `0x106338` loads at park start into each variant (`record+8+12v+8`,
    /// from `data\audio\advisor\&lt;LANG&gt;\&lt;stem&gt;.lip`): (message, variant) → its track, or null (no stem, or
    /// no file -- 268's `PS2_`). The language is the view's; null = no tracks at all.</summary>
    public Func<int, int, LipTrack> Lips { get; set; }
    /// <summary>⚠ `0x29CF08` rand(), for the mouth's random shape (§4.4). Natively the game's one newlib stream;
    /// the port gives the advisor its own (<see cref="NewlibRand"/>, fixed seed) -- the same generator, not the
    /// console's draw order.</summary>
    public Func<int> Rand { get; set; }
    /// <summary>`0x111D78(audio, handle)`: a still-playing speech stream is stopped -- a new message's voice
    /// (`0x1078E8`), an interrupt (`0x107640`), or off the screen (`0x1072C8`). The view stops its stream.</summary>
    public Action SpeechStopped { get; set; }

    // ================================================================================================
    // Submitting (findings/advisor-messages.md §2).

    /// <summary>⭐ `0x1075F0`: IMMEDIATE when flags bit 0x20 is clear or the id is 208..274, else the ring.</summary>
    public void Submit(AdvisorRequest request)
    {
        bool immediate = (Flags & FlagQueue) == 0 || unchecked((ushort)(request.Id - ImmediateFirst)) < ImmediateCount;
        Submitted?.Invoke(request, immediate);
        if (immediate) AddImmediate(request);
        else Queue(request);
    }
    public void Submit(int id, object obj = null) => Submit(new AdvisorRequest(unchecked((ushort)id), obj));

    static AdvisorRecordType KindOf(AdvisorRequest r) =>
        r.HasObject ? AdvisorRecordType.Object
        : unchecked((ushort)(r.Id - ImmediateFirst)) < ImmediateCount ? AdvisorRecordType.Tutorial : AdvisorRecordType.Plain;

    AdvisorRecordType _pendingKind;
    object _pendingObject;

    /// <summary>⭐ `0x107640` AddImmediateMessage: in state 2 the entering message is abandoned (head down,
    /// state 4 -- a ring message stays in the ring, it is popped only in state 2's transition; an entering
    /// immediate is overwritten and lost); in state 3 the speech is stopped and the head cut out (the message
    /// is lost). Then the pending slot takes it, overwriting any earlier one.</summary>
    void AddImmediate(AdvisorRequest r)
    {
        if (State == AdvisorState.Entering) { Countdown = ExitCountdown; State = AdvisorState.Exiting; HeadDown(cut: false); }
        else if (State == AdvisorState.Speaking)
        {
            StopSpeech();                                                  // "SHUTTING UP ADVISOR in TheAdvisor::AddImmediateMessage"
            Countdown = ExitCountdown; State = AdvisorState.Exiting; HeadDown(cut: true);
        }
        PendingId = r.Id; _pendingKind = KindOf(r); _pendingObject = r.Object;
        PendingSet = true;
    }

    /// <summary>⭐ `0x107760` QueueMessage: dedup against what is still queued (tail..head), write at the head,
    /// head+1; head == tail → the OLDEST is dropped (tail+1). Empty is head == tail, so 19 fit.</summary>
    void Queue(AdvisorRequest r)
    {
        for (int i = RingTail; i != RingHead; i = (i + 1) % RingSlots)
            if (_ring[i].Id == r.Id) return;
        _ring[RingHead] = (r.Id, KindOf(r), r.Object);
        RingHead = (RingHead + 1) % RingSlots;
        if (RingHead == RingTail) { RingTail = (RingTail + 1) % RingSlots; Overflows++; }
    }

    // ================================================================================================
    // The update (findings/advisor-messages.md §3.2).

    /// <summary>⭐ `0x1066B0`, one call per simulation pass. The message stack's own pass (`0x108220`, run by
    /// the UI manager update `0x13D438` at the START of the pass) runs first: nothing between them in the
    /// pass touches the stack.</summary>
    public void Update()
    {
        if (_updating || !_hydrated) throw new InvalidOperationException("Advisor is busy or unhydrated");
        _updating = true;
        try {
        Stack.Update();
        Head?.Step(MillisecondsPerTick);
        Ticks++;
        switch (State)
        {
            case AdvisorState.StartDelay:
            {
                short was = Countdown; Countdown--;
                if (was != 1) break;
                TutorialEvent(0);                                          // 0x107390(adv, 0): WELCOME, out of scope
                State = AdvisorState.Idle;
                if (!TestPark)
                {
                    ushort id = GoalsAlreadyAchieved?.Invoke() == true ? GreetingAchieved
                              : (GoldTicketsEarned?.Invoke() ?? 0) == 0 ? GreetingFirst : GreetingOther;
                    Submit(new AdvisorRequest(id));
                    GoalNotices?.Invoke();                                 // 0x16BA58(cal)
                }
                break;
            }
            case AdvisorState.Idle:
                if ((Flags & FlagRules) != 0)
                    Scheduler?.Step(() => RingHead == RingTail, TextUi, id => Submit(new AdvisorRequest(id)));
                if (!PendingSet)
                {
                    if ((Flags & FlagQueue) != 0 && RingHead != RingTail)
                    {
                        Countdown = 0; State = AdvisorState.Entering;
                        Take(_ring[RingTail].Id);                          // the tail, not popped yet
                    }
                }
                else
                {
                    Countdown = ExitCountdown; State = AdvisorState.Entering;
                    Take(PendingId);
                }
                break;
            case AdvisorState.Entering:
                if (Head == null || Head.ChannelDone)
                {
                    if (!PendingSet)
                    {
                        var s = _ring[RingTail];                           // 0x107870: play the tail, then pop it
                        Play(s.Id, s.Kind, s.Object);
                        RingTail = (RingTail + 1) % RingSlots;
                    }
                    else { PendingSet = false; Play(PendingId, _pendingKind, _pendingObject); }   // 0x1078C8
                    State = AdvisorState.Speaking;
                }
                Ducking = true;                                            // sfx/music targets = min(setting, 25)
                break;
            case AdvisorState.Speaking:
                if (Speaking)
                {
                    SpeechElapsedMs += MillisecondsPerTick;                // 0x111CC8: still playing?
                    if (SpeechElapsedMs < SpeechLengthMs) { LipsAndMouth(); break; }   // 0x105F30, 0x106B54..0x106BEC
                    Speaking = false;
                }
                if (CurrentId == Bankrupted) GameOver?.Invoke();           // 0x13BDD0
                Countdown = ExitCountdown; State = AdvisorState.Exiting;
                HeadDown(cut: false);
                Exiting();                                                 // goto 0x106BF8: state 4 in the same tick
                break;
            case AdvisorState.Exiting:
                Exiting();
                break;
            case AdvisorState.Cooldown:
            {
                Modal = false; PadLocked = false; CancelLabelShown = false;  // +0xB5 = 0; 0x1817C0(0); 0x13D870
                Countdown--;
                if (Countdown < 1 || (Flags & FlagCooldown) == 0 || PendingSet) State = AdvisorState.Idle;
                break;
            }
        }
        // 0x107530: in states 2..4 of a modal message, the skip -- read raw, every tick.
        if (ModalActive)
        {
            bool down = SkipHeld;
            if (down) { SkipLatched = true; UiSound?.Invoke(SoundSkip); }
            // ⚠ The latch is never cleared by the skip: on the following ticks of state 4 it fires again (READ quirk).
            if (SkipLatched && !down)
            {
                PendingSet = false;
                GetTheAdvisorOffTheScreen();
                if (CurrentId == Bankrupted) GameOver?.Invoke();
            }
        }
        } finally { _updating = false; }
    }

    /// <summary>`0x107530`: the state is 2..4 and the message is modal.</summary>
    public bool ModalActive => (State is AdvisorState.Entering or AdvisorState.Speaking or AdvisorState.Exiting) && Modal;

    /// <summary>State 4's body: audio targets back to the settings, countdown − 1; when the head is done
    /// (or there is none) a modal message sets flags `| 0x21`, and the cooldown starts.</summary>
    void Exiting()
    {
        Ducking = false;
        Countdown--;
        if (Head != null && !Head.ChannelDone) return;
        if (ModalActive) Flags |= FlagText | FlagQueue;
        Countdown = CooldownTicks; State = AdvisorState.Cooldown;
    }

    /// <summary>State 1's take (enter animation if a model exists and the message's CURRENT variant has a
    /// sound; costume `0x107B90`; then `0x1074C0`).</summary>
    void Take(ushort id)
    {
        if (Head != null && SoundOf(id, CurrentVariant(id)) != 0)
        {
            Head.Play(RecordEnter, 0);
            Costume = _catalogue.Messages[id].Voices[CurrentVariant(id)].AnimationSelector;
            Head.Dress(Costume);                                           // 0x107B90: +0x1F8, then 0x107160
            EnterPlayed = true;
        }
        SetCurrent(id);
    }

    /// <summary>⭐ `0x106B54..0x106BEC`, each tick of state 3 while the stream plays (findings/advisor-messages.md
    /// §4.2, §4.4): the lip step `0x105F30` -- no track: nothing consumed, the gate stays set; a mark passed (its
    /// µs/1000 strictly below the ms since the voice began, one a tick): the gate flips; the terminator: the gate
    /// clears -- then the mouth:
    /// <code>
    ///   consumed, gate set      → 1 (aah)
    ///   consumed, gate clear    → 0
    ///   nothing,  gate set      → one time in five (rand % 5 == 0): (shape + (rand &amp; 3) + 1) % 5, else as it is
    ///   nothing,  gate clear    → 0
    /// </code>
    /// So a voice with no lip track (268) flaps at random for its whole length (READ consequence).</summary>
    void LipsAndMouth()
    {
        bool stepped = false;
        if (_lip != null)
        {
            LipGate = _lip.Advance(unchecked((uint)SpeechElapsedMs));      // 0x147158 − +0x240
            stepped = _lip.Stepped;
        }
        if (!stepped)
        {
            if (LipGate)
            {
                if (NextRand() % 5 == 0) SetMouth((MouthShape + (NextRand() & 3) + 1) % 5);
                return;
            }
        }
        else if (LipGate) { SetMouth(1); return; }
        SetMouth(0);
    }

    int NextRand() => Rand?.Invoke() ?? 0;

    /// <summary>`0x105FC8(adv, shape)`: `+0x248` = shape, and the model shows that mouth mesh only.</summary>
    void SetMouth(int shape)
    {
        MouthShape = shape;
        MouthChanges++;
        Head?.Mouth(shape);
    }

    /// <summary>`0x1074C0`: the current id; with the voice flag, a modal id (208..274, or 123) locks the pad,
    /// shows "Cancel" and clears the skip latch; any other clears the modal flag.</summary>
    void SetCurrent(ushort id)
    {
        CurrentId = id;
        if ((Flags & FlagVoice) == 0) return;
        if (unchecked((ushort)(id - ImmediateFirst)) < ImmediateCount || id == Bankrupted)
        { Modal = true; PadLocked = true; CancelLabelShown = true; SkipLatched = false; }
        else Modal = false;
    }

    /// <summary>⭐ `0x1078E8`, playing a message: with flags bit 0 and a text row ≠ 310 a stack record is
    /// appended (type = the slot's kind, the object); with bit 1 the voice -- a still-playing stream is stopped,
    /// the CURRENT variant's sound started (length → the talk animation), and the variant rotates (even with
    /// no sound).</summary>
    void Play(ushort id, AdvisorRecordType kind, object obj)
    {
        var m = id < AdvisorCatalogue.MessageCount ? _catalogue.Messages[id] : null;   // ⚠ id 276 reads past the table natively
        short row = unchecked((short)(m?.TextRow ?? AdvisorCatalogue.BlankTextRow));
        bool text = (Flags & FlagText) != 0 && row != AdvisorCatalogue.BlankTextRow;
        if (text) Stack.Add(kind, row, obj);                               // 0x1089A0 .. 0x13CFA8 → 0x108598
        int variant = -1, ms = 0, talk = -1; ushort sound = 0;
        if ((Flags & FlagVoice) != 0 && m != null)
        {
            StopSpeech();                                                  // "* ADVISOR STREAM ISNT STOPPED !"
            variant = _variant[id];
            sound = SoundOf(id, variant);
            if (sound != 0)
            {
                ms = SpeechLength?.Invoke(id, variant, sound) ?? 0;
                LipGate = true;
                MouthShape = 0;                                            // a store: the meshes stay as they are
                _lip = Lips?.Invoke(id, variant) is { } track ? new LipTrack.Playback(track) : null;   // +0x244
                SpeechMessage = id; SpeechVariant = variant; SpeechSoundId = sound; SpeechElapsedMs = 0;
                if (ms > 0)
                {
                    Speaking = true; SpeechLengthMs = ms;
                    talk = TalkRecord(ms);
                    Head?.Play(talk, 1);
                }
            }
            byte next = unchecked((byte)(variant + 1));
            _variant[id] = _count[id] <= next ? (byte)0 : next;
        }
        Played?.Invoke(new AdvisorPlayback(id, kind, obj, variant, sound, row, text, ms, talk));
    }

    /// <summary>⭐ `0x107A9C..0x107B1C` (MIPS): the talk record by speech length (ms, INFERRED unit: the thresholds
    /// are the records' 50/75/100/125/150/225/275 frames × 1000/30). ⚠ The research's table says "≤ 11000 → 6";
    /// the MIPS is `0x2AF7 &lt; len` → 3, so 11000 itself is record 3.</summary>
    public static int TalkRecord(int ms)
    {
        uint len = unchecked((uint)ms);
        if (len < 0x682) return 0;
        if (len < 0x9C4) return 1;
        if (len < 0xD05) return 2;
        if (len < 0x1046) return 3;
        if (len < 0x1388) return 4;
        if (len < 0x1D4C) return 5;
        if (len < 0x23CE) return 6;
        if (len < 0x2710) return 4;
        if (len < 0x2A51) return 5;
        return 0x2AF7 < len ? 3 : 6;
    }

    /// <summary>`0x106600(adv, cut)`: with a model and the enter played, the exit record 14 (flags 0 -- queued
    /// behind a talk loop, see <see cref="AdvisorHeadChannel"/> -- or 2, cut short: at once); `+0x230` = 0; the
    /// lips off (`+0x234` = 0, `+0x244` = 0); mouth 0 (`0x105FC8(0)`).</summary>
    void HeadDown(bool cut)
    {
        if (Head != null && EnterPlayed) Head.Play(RecordExit, cut ? 2 : 0);
        EnterPlayed = false;
        LipGate = false;
        _lip = null;
        SetMouth(0);
    }

    /// <summary>`0x111D78`, when the stream is still playing.</summary>
    void StopSpeech()
    {
        if (Speaking) SpeechStopped?.Invoke();
        Speaking = false;
    }

    /// <summary>⭐ `0x1072C8` GetTheAdvisorOffTheScreen: state 2 → 4 with the normal exit; state 3 → the speech
    /// stopped, the exit cut short, 4. Called by the skip, leaving the park (`0x1510F8`: Save Game, Quit, Close
    /// Park) and a minigame's start.</summary>
    public void GetTheAdvisorOffTheScreen()
    {
        if (State == AdvisorState.Entering) { Countdown = ExitCountdown; State = AdvisorState.Exiting; HeadDown(cut: false); }
        else if (State == AdvisorState.Speaking)
        {
            StopSpeech();
            Countdown = ExitCountdown; State = AdvisorState.Exiting; HeadDown(cut: true);
        }
    }

    // ================================================================================================
    // What the rules and the game call.

    /// <summary>⭐ `0x1073F0`, opcode 8: with flags bit 0 and a text row ≠ 310, retract the stack's FIRST
    /// record with that row (`0x108808`) -- so the log keeps one, latest, copy. Row 310 is skipped: such a
    /// message never has a record.</summary>
    public void TextUi(ushort id)
    {
        if ((Flags & FlagText) == 0 || id >= AdvisorCatalogue.MessageCount) return;
        short row = unchecked((short)_catalogue.Messages[id].TextRow);
        if (row == AdvisorCatalogue.BlankTextRow) return;
        Stack.RemoveByRow(row);
    }

    /// <summary>⭐ `0x1073C0(adv, j, d)`: an event counter, only while flags bit 3 is set (so ride-along's 6
    /// and the test park DROP events) -- then `0x10DDD8`.</summary>
    public void CountEvent(int j, int d)
    {
        if ((Flags & FlagRules) == 0) return;
        Scheduler?.AddCounter(j, d);
    }

    /// <summary>⭐ `0x14A7B0(object, 1)`, an object leaving the park -- the port's delete, through
    /// <see cref="ParkSim.Remove"/> (<paramref name="ride"/> null for a placement the sim does not run). What it does
    /// to the message stack, in the console's order (findings/advisor-messages.md §5.5):
    /// <list type="number">
    /// <item>the object's own removal `vt+0x10C` runs FIRST: for a ride that is `0x1E12E0` (READ: word `0x1E12E0` at
    /// slot `+0x10C` of the ride vtables `0x368080` and `0x369AA0`; `0x1E0E70` stores `0x369AA0` at the object's
    /// `+0x10`), which calls `0x1088E0` (<see cref="AdvisorMessageStack.RideRemoved"/>): the ride's first record is
    /// marked, every later one removed at once;</item>
    /// <item>then `0x13D8C0` (<see cref="AdvisorMessageStack.ObjectRemoved"/>) marks every type-2 record in turn,
    /// whatever its object -- so the LAST type-2 record is the one pending, and the ride's own mark from step 1
    /// is OVERWRITTEN (one pending slot). READ quirk, reproduced: with a later type-2 record about another ride,
    /// that one goes and the deleted ride's first record stays, still attached to it.</item>
    /// </list>
    /// ⚠ The port's delete is taken as the `0x14A7B0(obj, 1)` of `0x123FF8` (the second argument is what runs
    /// step 1); the other caller, `0x126898`, passes 0 (INFERRED which is the player's delete). ⚠ Nothing clears a
    /// QUEUED message's object (`0x107760`/`0x107870` never compare it): a ride's message still in the ring
    /// carries the ride when it is played.</summary>
    public void ObjectLeftPark(ParkRide ride)
    {
        if (ride != null) Stack.RideRemoved(ride);                         // vt+0x10C = 0x1E12E0 → 0x1088E0
        Stack.ObjectRemoved();                                             // 0x13D8C0
    }

    /// <summary>`0x13A178` (Game Options' Tutorial On/Off): the 0x40 bit only.</summary>
    public void SetTutorial(bool on) => Flags = on ? (byte)(Flags | FlagTutorial) : (byte)(Flags & ~FlagTutorial);

    /// <summary>`0x152668`: the mode `0x1DEBC8` starts ("ride-along", INFERRED name): off the screen, flags saved
    /// (`0x1072A0`), flags = 6. ⚠ The port has no ride-along: nothing calls this in play.</summary>
    public void EnterRideAlong()
    {
        GetTheAdvisorOffTheScreen();                                       // 0x1072C8
        SavedFlags = Flags;
        Flags = RideAlongFlags;
    }
    /// <summary>`0x1526D0` → `0x1072B0`: flags = saved; saved = 0xFF.</summary>
    public void ExitRideAlong()
    {
        Flags = SavedFlags;
        SavedFlags = 0xFF;
    }

    /// <summary>⚠ STUB, OUT OF SCOPE: `0x107390(adv, n)`, the tutorial dispatcher -- with flags 0x40 it runs
    /// `0x206358` over the 74-entry table `0x36C3A0` (22 live handlers). Returns 0, as it does with the bit clear.</summary>
    public int TutorialEvent(int n) => 0;

    /// <summary>⚠ STUB, OUT OF SCOPE: `0x107C18(adv, row)`, the tutorial message entry point -- natively: the
    /// first message ≥ 208 whose text row is <paramref name="row"/>, flags `&amp;= 0xDE`, submit. The stack's
    /// Replay calls it. Returns false: nothing is posted in this step.</summary>
    public bool TutorialMessage(short row) => false;

    // ================================================================================================
    // Direct emitters with the conditions the port can evaluate (findings/advisor-messages.md §8, §10.4).

    /// <summary>⭐ The coaster tool's Triangle `0x11BA00` then the finish `0x11BBD8` (findings/coaster-building.md
    /// §B, coaster-operation.md §5.1): an open ring posts 204 at once (`0x11BB70`); then 203 if the track is
    /// invalid (`+0x144 == 0`), else 204 AGAIN if open (`+0x148 == 0`), else tutorial event 0x49 (stub). The
    /// second 204 is deduplicated by the ring while the first is still queued.</summary>
    public void CoasterFinished(bool closed, bool valid)
    {
        if (!closed) Submit(new AdvisorRequest(0xCC));
        if (!valid) Submit(new AdvisorRequest(0xCB));
        else if (!closed) Submit(new AdvisorRequest(0xCC));
        else TutorialEvent(0x49);
    }

    /// <summary>⭐ The coaster station's Cross `0x11A858`: tutorial event 0x45 (stub), then 201 COASTER_STOCK_OUT
    /// when `0x14CCB0()` = `(park index == 2 ? 14 : 2) − coasters in use` is 0 -- the pool count already holds
    /// the coaster being placed (INFERRED "that used the last slot", coaster-building.md S11).</summary>
    public void CoasterPlaced(int coastersInUse, int parkIndex = 0)
    {
        TutorialEvent(0x45);
        if ((parkIndex == 2 ? 14 : 2) - coastersInUse == 0) Submit(new AdvisorRequest(0xC9));
    }

    /// <summary>⭐ Route the port's existing emitters through <see cref="Submit"/> and its counters through
    /// <see cref="CountEvent"/>: the ride side (with the ride attached, `0x107CB0`), the staff's own (strikes,
    /// ADD_MAX, research, upgrade refusals) and the calendar's (the award, the in-the-red chain).</summary>
    public void Attach(ParkSim sim, ParkStaff staff, ParkManagement management)
    {
        if (sim != null)
        {
            sim.Advisor = StateSimAdvisor;
            sim.AdvisorEvent = StateEvent;
            sim.ObjectRemoved = StateObjectRemoved;
        }
        if (staff != null) staff.Advisor = StateStaffAdvisor;
        if (management != null) management.Advisor = id => Submit(new AdvisorRequest(unchecked((ushort)id)));
    }

    // ================================================================================================
    // The catalogue as the state machine reads it.

    /// <summary>`record+7`, the message's current variant (random at park start, rotated by each voiced play).</summary>
    public int CurrentVariant(int id) => id < AdvisorCatalogue.MessageCount ? _variant[id] : 0;
    /// <summary>`record+6` after the world's overrides.</summary>
    public int VariantCount(int id) => id < AdvisorCatalogue.MessageCount ? _count[id] : 0;
    ushort SoundOf(int id, int variant) =>
        id < AdvisorCatalogue.MessageCount && (uint)variant < 4 ? _catalogue.Messages[id].Voices[variant].SoundId : (ushort)0;
}
