namespace TPW.PS2.Data;

/// <summary>What a visitor is thinking, and the art the game draws for it.
///
/// ⭐⭐ THE IDS ARE THE ENGINE'S OWN, not an order invented here. `FUN_00216028` builds the UI
/// texture table as 24-byte records of `{ name, _, _, _, id, flag }`, and the sixteen whose name
/// is `bubbles\tb*.ssh` carry exactly these ids. Five of them are then confirmed a second time by
/// the guest decision at `0x20C930`, which writes the id into the guest's `+0x40`: toilet 7,
/// thirsty 8, hungry 11, hungry-and-thirsty 14, and angry 10 on the unhappiness path.
///
/// ⚠ The LAPTOP icons (`laptop\thoughts\ti*.ssh`) are a SEPARATE id space, 4..20, in a different
/// order, and with `confused` where this list has `litter`. Do not index one with the other.
///
/// ⚠ `/Generic/bubbles/` also ships `tbconfused, tbsconfused, tbshappy, tbssad, tbsstrike,
/// tbstired` — six the executable never names. Cut, or laptop-only. Nothing draws them.</summary>
public enum Thought
{
    Happy = 0,
    VeryUnhappy = 1,
    Normal = 2,
    Sad = 3,
    Bored = 4,
    Sick = 5,
    Scared = 6,
    Toilet = 7,
    Thirsty = 8,
    BadQueue = 9,
    Angry = 10,
    Hungry = 11,
    Good = 12,
    Bad = 13,
    HungryAndThirsty = 14,
    Litter = 15,
}

/// <summary>One visitor's wants, as the console keeps them.
///
/// ⭐⭐ EVERY FIELD IS A BYTE CLAMPED TO 0..100, and that is the engine's own rule rather than a
/// convention adopted here: both spawn paths (`FUN_0020BCD0` and `FUN_00211A00`) write each need
/// then immediately clamp below at 0 and above at 100, and the accessor bank at `0x212330..` does
/// the same on every set.</summary>
public struct VisitorWants
{
    /// <summary>`+0x75`. Spawns at exactly 50 — the only need seeded to a constant.</summary>
    public byte Happiness;
    /// <summary>`+0x76`. A shop's DBA "vomit increase" (key `0x36`) is added here; above 92 the
    /// guest is sick. See findings/dba.md.</summary>
    public byte Sick;
    /// <summary>`+0x77`. A shop's DBA "hunger reduction" (key `0x32`) is subtracted from here.</summary>
    public byte Hunger;
    /// <summary>`+0x79`. ⭐ The same hunger reduction is ALSO ADDED here — eating fills the
    /// bladder — which with its spawn distribution is what identifies this field.</summary>
    public byte Toilet;
    /// <summary>`+0x7A`. A shop's DBA "thirst reduction" (key `0x33`) is subtracted from here.</summary>
    public byte Thirst;

    /// <summary>`+0x74`, LITTER CARRIED -- and the engine says so in its own words. Above 89 it
    /// runs `FUN_0020D010`, whose debug labels are `"litter"`, `"litter bin"`, `"drop litter"` and
    /// `"bin litter"`, and that function sets this byte back to **0**. You carry rubbish until you
    /// drop it or find a bin. ⭐ Which is also where the `Litter` bubble (id 15) comes from.</summary>
    public byte Litter;

    /// <summary>`+0x78`. ⚠⚠ STILL UNNAMED -- AND IT IS NOT BOREDOM, which this file asserted for
    /// most of a day. `FUN_0020C6A8` adds **+5** while the guest queues and `FUN_0020EDD8`
    /// subtracts the RIDE'S OWN INTENSITY from it (scale 1.0), so it rises with waiting and falls
    /// with excitement -- which is why boredom was the obvious guess.
    ///
    /// ⭐⭐ THE THOUGHT LADDER SETTLES IT AGAINST THAT GUESS. `FUN_0020FB88`'s bubble chain asks
    /// `FUN_0020F888(guest, guest[0x7B], ...)` and on a hit writes bubble id **4**, which
    /// `FUN_00216028`'s own table calls `tbbored`. The engine points the BORED picture at
    /// `+0x7B`, not at this. See <see cref="Boredom"/>.
    ///
    /// ⚠ So what IS it? A meter that queueing raises, an exciting ride lowers by its own
    /// intensity, and that docks happiness above 95. "Craving excitement" fits and is NOT READ --
    /// the same trap one level down, so it keeps the offset for a name until something names it.
    /// ⚠ NOTHING IN `FUN_0020FB88` RAISES IT on a timer, unlike hunger and thirst; queueing is
    /// the only riser found.</summary>
    public byte Unknown78;

    /// <summary>`+0x7B`, **BOREDOM** -- and the name is now READ rather than guessed, which it was
    /// not this morning. `FUN_0020FB88`'s thought chain tests this byte through
    /// `FUN_0020F888(guest, guest[0x7B], 0x40, 2, allowed)` and on a hit sets the guest's bubble
    /// to id **4**, which is `tbbored` in the table `FUN_00216028` builds. The engine itself
    /// points the bored picture at this offset.
    ///
    /// ⭐ Three independent behaviours agree, which is why the name is worth taking: the bored
    /// BUBBLE comes from here; `FUN_0020C930` only asks whether to go home while it is below 99
    /// and leaves unconditionally at 99; and finishing ANY facility use takes `rand(20)` off it
    /// (`FUN_0020EDD8`, before the per-kind switch, so a shop and a lavatory count too). Bored
    /// enough to go home, and doing something relieves it. Seeded `rand(50)`.
    ///
    /// ⚠ This was <c>Unknown7B</c> and labelled "had-enough / going-home" -- right about the
    /// mechanism, wrong that it was nameless.</summary>
    public byte Boredom;

    /// <summary>`+0x60`, a word, not a byte and not clamped. Spawns at `(rand(300) + 200) * 10`,
    /// so 2000..4990. Below 100 the guest goes home.</summary>
    public int Cash;

    /// <summary>`+0x40`, the id the thought bubble is drawn from.</summary>
    public Thought Thought;

    /// <summary>⭐⭐ WHAT THIS GUEST ACTUALLY WANTS OUT OF A RIDE. `FUN_0020C078` returns
    /// <c>*(u16*)(&amp;DAT_002eebd8 + guest[0x7d] * 8)</c> -- so `+0x7d` is a PERSONALITY index and
    /// the preference is a per-type constant. The table is 8 entries of stride 8 and its first
    /// u16s are read straight off the image: **90, 30, 50, 75, 100, 45, 60, 70**.
    ///
    /// ⚠⚠ EIGHT RECORDS ARE VERIFIED; THE TABLE'S LENGTH IS NOT. I first wrote that index 10
    /// holding two `1.0f` proved the table ends at eight -- it does not. The getter has **no
    /// bounds check**, so a larger `+0x7d` would simply read that float data as an intensity;
    /// what the neighbours look like is a fact about LAYOUT, not about extent. The length can
    /// only come from whatever constrains `+0x7d`, and that is exactly what is unread. astraclaw
    /// caught it -- the mirror of the "not found is not not-there" point I had made an hour
    /// earlier, made by me in the opposite direction.
    ///
    /// ⭐⭐ AND INDEX 8 IS REACHABLE, SO THE TABLE IS NOT EIGHT ENTRIES. The purchase path's
    /// shop-kind arm 2 does `guest[0x7d] = 8` outright (`0x20e6b8`), and there are **five** `sb`
    /// writes to `+0x7d` in the guest range. So the row I dismissed as "other data" is a row the
    /// game can select, and my "eight verified records" was a FLOOR, not a count -- which is
    /// exactly what astraclaw said when they refused to let adjacency bound the table.
    ///
    /// ⚠ WHAT IS NOT READ is what sets `+0x7d` AT SPAWN: `FUN_0020BCD0` writes every other
    /// need byte and never touches it, so the starting personality comes from elsewhere and this
    /// port picks uniformly from the eight VERIFIED records. The values are the game's; the
    /// choice is not, and neither is the belief that eight is all of them.</summary>
    public byte PreferredIntensity;
}

/// <summary>The visitors' wants: hunger, thirst, the toilet, and how they feel about it.
///
/// ⭐ READ OUT OF THE EXECUTABLE, and decompiled rather than hand-disassembled — the first pass by
/// hand had the rise function backwards (see <see cref="RollHigh"/>).
///
///   `FUN_0020BCD0`   spawn: every need's starting distribution        <see cref="Spawn"/>
///   `FUN_00211A00`   spawn from a 26-byte template, `base + roll(spread + 1)`
///   `FUN_00211968`   the centred roll, `(rand(n) + rand(n)) / 2`      <see cref="RollCentred"/>
///   `FUN_002119B0`   the high roll, `n - rand(n) * rand(n) / n`       <see cref="RollHigh"/>
///   `FUN_0020C930`   what a guest does about it, and every threshold  <see cref="Decide"/>
///   `FUN_00216028`   the bubble ids                                   <see cref="Thought"/>
///
/// ⚠⚠ WHAT IS NOT READ: **THE RISE**. Not the rates -- the rise itself. I traced the 26-byte
/// record and reported it as the per-tick rates; it is not. `FUN_001603B0` reads **ONE** record
/// off the level blob, OUTSIDE its loop, and hands the SAME record to `FUN_00211A00` for every
/// visitor it creates:
///
///     record = read(0x1a);  for (i = 0; i &lt; park.visitorCount; i++) spawn(newGuest(), record);
///
/// So it is a per-level SPAWN TEMPLATE -- this park's visitors' starting personality -- and there
/// are two spawn paths, this one and `FUN_0020BCD0`'s hardcoded distributions, both virtual.
///
/// ⚠ And the code that raises a need over time has NOT been found. The need setters at
/// `0x212330..` have **no `jal` callers at all**, so they are reached through a vtable built at
/// runtime; the same is true of `FUN_0020BCD0`. (That claim has a control: the identical scan
/// finds the one caller of `FUN_00211A00` and of the selection-box drawer, both known-called.)
///
/// So <see cref="Rates"/> and <see cref="SecondsPerRise"/> are a PORT INVENTION, not a decode --
/// the roll SHAPES are the console's, borrowed from the spawn, and nothing else about them is.
/// They are kept in one table so that finding the real rise is a data change.
///
/// ⚠ Keyed by GUEST ID, never held on a walking Guest object: readmission after a ride preserves
/// the id but builds a NEW Guest (astraclaw, reviewing the boundary). <see cref="Reconcile"/>
/// against the coordinator's live ids, or a reused id inherits a dead stranger's hunger.</summary>
public sealed class VisitorNeeds
{
    public const byte Full = 100;

    /// <summary>⭐⭐ THE ONE BAR FOR WANTING SOMETHING, and it is READ: `FUN_0020F888` opens with
    /// `if (need &lt; 0x5b) return 0` and the caller passes hunger, thirst or toilet into the same
    /// function -- so 91 is a single shared threshold rather than three constants that happen to
    /// agree. <see cref="Decide"/>'s `&gt; 90` is this same number; routing uses it too.</summary>
    public const int Urgent = 91;

    /// <summary>The litter a purchase leaves before its random part -- `DAT_002EEB60`, read as
    /// **30** from the image and loaded at `0x20E504`. ⚠ Not the authored `LitterEffect`.</summary>
    public const int LitterBase = 30;

    /// <summary>The compiled `Product` byte, which is the purchase path's switch selector --
    /// verified against the compiled rows: burger 0, drinks 1, costume 2, balloon 3, ice cream 4,
    /// gift 6. ⚠ NOT the authored `UsageInfo.ShopType`.</summary>
    public const int Food = 0, Drink = 1, Costume = 2, Trinket = 3;

    /// <summary>What a costume shop leaves a guest preferring: row **8** of the preference table
    /// at `DAT_002EEBD8`, read as **14** off the image. ⚠ Held separately from
    /// <see cref="Preferences"/> ON PURPOSE -- that array is what ordinary spawning draws from,
    /// and whether a guest can START as personality 8 is a different question with no evidence
    /// behind it yet. Putting the row in the array would answer it by accident.</summary>
    public const byte CostumePreference = 14;

    /// <summary>⭐⭐ WHY THE COMPILED HAPPINESS IS THE PAYOUT AT DEFAULTS. The console scales it by
    /// `(q1 - q2/15) / 100`, and shop setup explicitly writes **q1 = 100, q2 = 0** at
    /// `0x1D1870/7C` -- so the scale is `(100 - 0)/100 = 1` and a new shop pays its compiled base
    /// exactly. astraclaw verified the defaults. ⚠ It stays a DEFAULT, not a constant: whatever
    /// moves q1 and q2 later is unread, so this port has no shop quality at all rather than a
    /// wrong one.</summary>
    public const int QualityDefaultNumerator = 100;

    /// <summary>The first eight verified preferred-intensity records at `DAT_002eebd8`;
    /// this is the port's selection set, not a proved bound on the original table.
    /// See <see cref="VisitorWants.PreferredIntensity"/>.</summary>
    public static readonly byte[] Preferences = { 90, 30, 50, 75, 100, 45, 60, 70 };

    /// <summary>⭐⭐ A RIDE ONLY TURNS A STOMACH IF IT IS FIERCE ENOUGH, and the bar is READ:
    /// The comparison at `0x20F248` and branch at `0x20F24C` skip sickness below56, and
    /// there is no other arm -- below it sickness is left exactly alone.
    ///
    /// ⚠⚠ THIS CLASS USED TO SAY THE OPPOSITE. "Sickness is measured against 30, so an intensity
    /// below that settles the stomach" -- it does not; nothing here ever settles a stomach, and
    /// the port reduced sickness below30 and added it at30..55, including default45.
    /// Found by astraclaw reading the consumer rather than the constant. The 30 is real, but
    /// it is the pivot INSIDE the term, not a threshold around it.</summary>
    public const int SickeningIntensity = 56;

    /// <summary>How much happiness a ride gives, by how badly it matched the rider's taste --
    /// `|preferred - intensity|` against the console's own two bands, paying `DAT_002eeb44/40/3c`
    /// = **15 / 10 / 5**. ⭐ So the flat 15 this port used was only the best case.</summary>
    public static int RideHappinessFor(int mismatch) => mismatch < 21 ? 15 : mismatch < 51 ? 10 : 5;

    /// <summary>⭐⭐ AN UNMET NEED MAKES A GUEST UNHAPPY, and the rule is READ rather than shaped
    /// here. `FUN_0020FB88` runs three identical tests over the guest and docks **one happiness
    /// for each need at or above its own threshold**, clamped at zero:
    ///
    /// <code>
    ///   if (DAT_002eeb4c &lt;= guest[0x78]) guest[0x75]--;   // +0x78,   95
    ///   if (DAT_002eeb50 &lt;= guest[0x76]) guest[0x75]--;   // sickness, 85
    ///   if (DAT_002eeb54 &lt;= guest[0x79]) guest[0x75]--;   // toilet,   90
    ///   if (DAT_002eeb58 &lt;= guest[0x77]) guest[0x75]--;   // hunger,   95
    ///   if (DAT_002eeb5c &lt;= guest[0x7a]) guest[0x75]--;   // thirst,   85
    /// </code>
    ///
    /// ⚠⚠ THERE ARE **FIVE**, AND THIS FILE SHIPPED THREE. The two missing ones are the two
    /// needs the whole feature is about -- hunger and thirst -- so a starving guest was the only
    /// kind whose suffering cost them nothing. The first read of this function stopped at the
    /// third test because three consecutive identical blocks look like the whole run; they were
    /// the whole run of what had been scrolled to. ⭐ Reading a decompile to the END of the
    /// function is not optional, and "I saw the pattern" is where it stops being read.
    ///
    /// ⭐ Five separate globals rather than one shared bar -- unlike <see cref="Urgent"/>, which
    /// IS one number for three needs. Worth keeping distinct: they are different questions.
    ///
    /// ⚠ THE VALUES ARE READ FROM THE IMAGE, not from a running machine. That is the weaker of
    /// the two readings -- see the port's own rule that an image is not authority for a runtime
    /// global -- but unlike the classic case these are non-zero, sit in an ordered run, and land
    /// exactly where thresholds belong on a 0..100 need. A savestate would settle it.</summary>
    public int Unknown78Bar { get; set; } = 95;
    public int SickBar { get; set; } = 85;
    public int ToiletBar { get; set; } = 90;
    public int HungerBar { get; set; } = 95;
    public int ThirstBar { get; set; } = 85;

    /// <summary>How fast each need rises per step, in the console's own `base + roll(spread + 1)`
    /// shape. ⚠ THE NUMBERS ARE CHOSEN, the shape is not -- see the class note.</summary>
    public sealed record Rate(byte Base, byte Spread, bool High);

    /// <summary>⚠ PARTLY READ NOW. The roll SHAPES are the console's (hunger, thirst and the
    /// toilet take the high roll because that is the helper `FUN_00211A00` passes them at SPAWN,
    /// and the rest take the centred one).
    ///
    /// ⭐⭐ HUNGER AND THIRST ARE NO LONGER INVENTED. `FUN_0020FB88` ends with two risers:
    ///
    /// <code>
    ///   if (now % DAT_002eeb6c == guest[0x14] % DAT_002eeb6c)  guest[0x77] += rand(2);  // 50
    ///   if (now % DAT_002eeb70 == guest[0x14] % DAT_002eeb70)  guest[0x7a] += rand(2);  // 40
    /// </code>
    ///
    /// So each is **+0 or +1**, and hunger fires every **50** ticks against thirst's **40** --
    /// the console's own `rand(2)`, which is exactly this table's `Rate(0, 1)`. ⭐ The periods
    /// are what <see cref="TicksPerRise"/> now carries, and the RATIO between them (thirst 1.25x
    /// as often as hunger) is read even though seconds-per-tick is not.
    ///
    /// ⚠ The toilet, sickness and litter rates ARE still invented: nothing in this function
    /// raises them, so their riser lives somewhere not yet found.</summary>
    public Dictionary<string, Rate> Rates { get; } = new()
    {
        ["hunger"] = new Rate(0, 1, High: false),
        ["thirst"] = new Rate(0, 1, High: false),
        ["toilet"] = new Rate(0, 1, High: true),
        ["sick"] = new Rate(0, 0, High: false),
        ["litter"] = new Rate(0, 1, High: false),
    };

    /// <summary>How much SIMULATED time passes between one rise and the next.
    ///
    /// ⚠⚠ IT IS SIMULATED SECONDS, NOT CALLS, AND THE FIRST VERSION COUNTED CALLS. astraclaw's
    /// independent check caught it: one simulated second took hunger 10 -> 35 at 25 Hz and
    /// 10 -> 60 at 50 Hz, because the rise was driven by how often the coordinator happened to be
    /// called rather than by the clock. A need that fills faster on a faster machine is a bug in
    /// any sim, and it would have read as "the chosen rates are too high".
    ///
    /// ⚠ CHOSEN, and it now drives only the needs whose cadence is NOT read -- the toilet,
    /// sickness and litter. Hunger and thirst have their own clock: see
    /// <see cref="SecondsPerTick"/>. It also remains the period this method RETURNS, so the
    /// queue's per-period cost is charged on exactly the clock it was charged on before.</summary>
    public double SecondsPerRise { get; set; } = 2.56;
    double _sinceRise;

    /// <summary>The console's own tick, the unit `FUN_001C4930` counts in.
    ///
    /// ⭐⭐ WHY THIS EXISTS: `FUN_0020FB88` raises hunger every **50** ticks and thirst every
    /// **40**, each by `rand(2)`. Those periods are READ, so expressing them needs a clock that
    /// ticks -- and putting them on it replaces TWO invented rates with ONE invented number.
    ///
    /// ⭐⭐ AND IT IS NOT INVENTED ANY MORE -- IT IS THE PARK'S OWN TICK. This shipped for an hour
    /// as a guessed 60 Hz. astraclaw then read the native counter: it is driven by
    /// "gameticks/rendertick", default 1, behind a default **two-VBLANK** render throttle -- and
    /// this is a **PAL** disc (`SLES_500.32`), so two VBLANKs of a 50 Hz field rate is **25 Hz**.
    ///
    /// ⭐ Which is the number <see cref="ParkSim.TickMilliseconds"/> already carries, arrived at
    /// independently ("the console runs its logic at 25 a second"). Two routes to one rate is
    /// corroboration; deriving from it rather than restating it means the same native clock can
    /// never end up with two different port rates, which is exactly what astraclaw asked for.
    ///
    /// ⚠ THIS IS A REAL PACE CHANGE, said out loud: 40 ms against the guessed 16.7 makes hunger
    /// and thirst fill about 2.4x slower than the hour they spent at 60 Hz. "I improved the
    /// model" is how a silent difficulty change ships.</summary>
    public double SecondsPerTick { get; set; } = ParkSim.TickMilliseconds / 1000d;
    /// <summary>`DAT_002EEB6C` and `DAT_002EEB70`, read from the image.</summary>
    public const int HungerTicks = 50, ThirstTicks = 40;
    /// <summary>`FUN_0020FB88` refreshes a guest's mood bubble on `tick % 0x7f == guest % 0x7f`.
    /// ⚠ 0x7f is a MASK (`&amp; 0x7f`), so the period is 128 and the comparison is against the
    /// guest's own id -- which is what spreads the refresh across the crowd instead of every
    /// guest asking for a bubble on the same tick. That stagger is load-bearing: see
    /// <see cref="BubbleBudget"/>.</summary>
    public const int MoodTicks = 128;
    double _sinceTick;
    long _tick;
    int _hungerTicks, _thirstTicks;

    /// <summary>⭐⭐ THE CONSOLE DRAWS AT MOST **25** THOUGHT BUBBLES AT ONCE. `DAT_002E28D0` is a
    /// global count and every site that gives a guest a bubble does the same dance: if the count
    /// is below `0x19`, raise it and set bit 3 of `guest[0x34]` to mark this guest as holding
    /// one. A guest who already holds one keeps it without taking a second.
    ///
    /// ⭐ It is a presentation budget, not a simulation one -- the mood is computed either way,
    /// only the picture is rationed -- which is why it belongs next to the refresh period rather
    /// than inside the ladder.</summary>
    public int BubbleBudget { get; set; } = 25;
    readonly HashSet<int> _holdingBubble = new();
    public int BubblesHeld => _holdingBubble.Count;

    readonly Dictionary<int, VisitorWants> _byGuest = new();
    readonly Random _rng;

    public VisitorNeeds(int seed = 0) => _rng = new Random(seed);

    public IReadOnlyDictionary<int, VisitorWants> All => _byGuest;
    public bool Has(int guest) => _byGuest.ContainsKey(guest);
    public VisitorWants Of(int guest) => _byGuest.TryGetValue(guest, out var w) ? w : default;
    public void Set(int guest, VisitorWants w) => _byGuest[guest] = w;

    /// <summary>`rand(n)` as `FUN_001448E0` computes it: the RNG modulo n, so 0..n-1.</summary>
    int Rand(int n) => n <= 0 ? 0 : _rng.Next(n);

    /// <summary>`FUN_00211968`: two rolls averaged. Centred on (n-1)/2, triangular.</summary>
    public int RollCentred(int n) => (Rand(n) + Rand(n)) / 2;

    /// <summary>`FUN_002119B0`: `n - rand(n) * rand(n) / n`.
    ///
    /// ⚠⚠ THIS IS BIASED **HIGH**, AND READING IT BY HAND GOT IT BACKWARDS. The product of two
    /// rolls over n is biased low, and the function SUBTRACTS it from n -- so the result clusters
    /// near n and only occasionally drops. I reported it as biased-low off a hand disassembly and
    /// the decompiler corrected it. The same product WITHOUT the subtraction is what the spawn
    /// uses for the toilet and thirst, which really is biased low, and the two are easy to
    /// confuse precisely because they share a shape.</summary>
    public int RollHigh(int n) => n <= 0 ? 0 : n - Rand(n) * Rand(n) / n;

    static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > Full ? Full : v);

    /// <summary>A visitor arriving at the park, seeded exactly as `FUN_0020BCD0` seeds one.
    ///
    /// ⭐ The two biased-low needs are the tell that pinned them: `rand(100) * rand(100) / 100`
    /// clusters hard at zero, and the only two needs seeded that way are the toilet and thirst --
    /// people arrive at a theme park neither bursting nor parched. Every other need is a flat
    /// `rand(n)` and happiness is the constant 50.</summary>
    public VisitorWants Spawn(int guest)
    {
        var w = new VisitorWants
        {
            Litter = Clamp(Rand(40)),
            Happiness = Clamp(50),
            Sick = Clamp(Rand(50)),
            Hunger = Clamp(Rand(70)),
            Cash = (Rand(300) + 200) * 10,
            Unknown78 = Clamp(Rand(40)),
            Toilet = Clamp(Rand(100) * Rand(100) / 100),
            Thirst = Clamp(Rand(100) * Rand(100) / 100),
            Boredom = Clamp(Rand(50)),
            PreferredIntensity = Preferences[Rand(Preferences.Length)],
            Thought = Thought.Normal,
        };
        _byGuest[guest] = w;
        return w;
    }

    /// <summary>Let one step of time pass for everyone. ⚠ Needs only ever RISE here; they fall by
    /// being met -- see <see cref="Buy"/>.</summary>
    /// <summary>Let <paramref name="seconds"/> of SIMULATED time pass for everyone.
    ///
    /// ⚠ Zero seconds ages nobody -- a step that moves no clock must not move a need either --
    /// and the caller still reconciles afterwards, because a guest can be retired on a zero-time
    /// step.</summary>
    /// <summary>What an unmet need does to a mood -- see <see cref="Unknown78Bar"/>. One point of
    /// happiness per need over its bar, per period, floored at zero.
    ///
    /// ⭐ This is what makes the needs MATTER. They rose, the bubbles appeared, the guest walked
    /// to a shop -- and ignoring one cost them nothing at all, so a park with no lavatory was
    /// indistinguishable from a good one until the guest happened to go home for another reason.
    /// ⚠ Three independent tests, NOT an else-if chain: the console docks a guest once per need,
    /// so somebody bored AND bursting loses two.</summary>
    void Fret()
    {
        foreach (int guest in _byGuest.Keys.ToArray())
        {
            var w = _byGuest[guest];
            int drop = (w.Unknown78 >= Unknown78Bar ? 1 : 0)
                     + (w.Sick >= SickBar ? 1 : 0)
                     + (w.Toilet >= ToiletBar ? 1 : 0)
                     + (w.Hunger >= HungerBar ? 1 : 0)
                     + (w.Thirst >= ThirstBar ? 1 : 0);
            if (drop == 0) continue;
            w.Happiness = Clamp(w.Happiness - drop);
            _byGuest[guest] = w;
        }
    }

    /// <returns>How many rise periods elapsed, so a caller can charge a per-period cost of its
    /// own on the SAME clock -- the queue's, which only <see cref="ParkVisitors"/> knows who owes.
    /// ⭐ Returning it beats exposing the accumulator: there is still exactly one clock.</returns>
    public int Step(double seconds)
    {
        if (seconds <= 0d) return 0;
        Appetite(seconds);
        _sinceRise += seconds;
        if (_sinceRise < SecondsPerRise) return 0;
        // ⚠ One rise per elapsed period, not one per call: a long step owes several.
        int rises = (int)(_sinceRise / SecondsPerRise);
        _sinceRise -= rises * SecondsPerRise;
        for (int i = 0; i < rises; i++) { Rise(); Fret(); }
        return rises;
    }

    /// <summary>Hunger and thirst, on the console's own periods -- the one part of the rise that
    /// is read. ⭐ Counted rather than looped per tick: a 2.5 s step is 150 ticks, and walking
    /// every guest 150 times to apply at most three rolls each is the same answer at 50x the
    /// cost. ⚠ The rolls are still applied INDIVIDUALLY (n draws of `rand(2)`, not one draw
    /// times n) because the console draws once per fire and the distributions differ.</summary>
    /// <summary>Actually executed native-style guest updates, shared with destination scheduling.
    /// SecondsPerTick is still an explicit port cadence; this does not prove a PS2 wall rate.</summary>
    public long UpdateTicks => _tick;

    void Appetite(double seconds)
    {
        _sinceTick += seconds;
        int ticks = (int)(_sinceTick / SecondsPerTick);
        if (ticks <= 0) return;
        _sinceTick -= ticks * SecondsPerTick;

        long before = _tick; _tick += ticks;
        Moods(before, _tick);
        _hungerTicks += ticks; int hunger = _hungerTicks / HungerTicks; _hungerTicks %= HungerTicks;
        _thirstTicks += ticks; int thirst = _thirstTicks / ThirstTicks; _thirstTicks %= ThirstTicks;
        if (hunger == 0 && thirst == 0) return;

        foreach (int guest in _byGuest.Keys.ToArray())
        {
            var w = _byGuest[guest];
            for (int i = 0; i < hunger; i++) w.Hunger = Clamp(w.Hunger + Roll(Rates["hunger"]));
            for (int i = 0; i < thirst; i++) w.Thirst = Clamp(w.Thirst + Roll(Rates["thirst"]));
            _byGuest[guest] = w;
        }
    }

    /// <summary>⭐⭐ `FUN_0020FB88`'s THOUGHT LADDER -- first hit wins, and every threshold is
    /// read. `FUN_0020F888` fires at **&gt;= 91** (<see cref="Urgent"/>) and `FUN_0020F968` at
    /// **&lt; 10**:
    ///
    /// <code>
    ///   toilet     &gt;= 91           -> 7  Toilet
    ///   sick       &gt;= 91           -> 5  Sick
    ///   happiness  &gt;= 91           -> 1
    ///   happiness  &lt;  10           -> 3  Sad
    ///   happiness  &lt;  81:
    ///        boredom &gt;= 91         -> 4  Bored
    ///        26..74, 1 chance in 10 -> 1
    ///        otherwise              -> no bubble, and the slot is GIVEN BACK
    ///   happiness  &gt;= 81           -> 0  Happy
    /// </code>
    ///
    /// ⭐ This fills in the rules <see cref="Decide"/> could not: Bored and Sad had no source at
    /// all, and the happy end had a guessed 75 where the console uses 81 and 91.
    ///
    /// ⚠⚠ IT IS A SECOND BUBBLE SOURCE, NOT A REPLACEMENT. `FUN_0020C930` -- what this port calls
    /// <see cref="Decide"/> -- writes the same field when a guest chooses what to do, and it is
    /// the one that knows about a burger van being nearby. Both run on the console and the later
    /// write wins; this one runs only every 128 ticks, so it refreshes slowly rather than
    /// stamping over the decision bubble every frame.
    ///
    /// ⚠ NOT PORTED: `FUN_0020FA78(guest, 0|1|2)` runs alongside every arm and is unconditional
    /// -- a FACE, separate from the bubble, with three values (neutral / happy / queasy). This
    /// port has no guest expression to put it on.
    ///
    /// ⭐ Note the ladder disagrees with this port's enum: id 1 sits ABOVE id 0 on the same
    /// field with the same face, at 91 against 81, while the sad end is taken by 3. The enum
    /// calls id 1 `VeryUnhappy`. The engine's own ordering says it is the stronger HAPPY. Not
    /// renamed here: the art has not been looked at, and the picture and the behaviour should
    /// agree before a name changes.</summary>
    void Moods(long from, long to)
    {
        if (to <= from) return;
        foreach (int guest in _byGuest.Keys.ToArray())
        {
            // The console's own stagger: this guest refreshes on ticks congruent to its id.
            long phase = ((guest % MoodTicks) - from) % MoodTicks;
            if (phase < 0) phase += MoodTicks;
            if (from + phase >= to) continue;

            var w = _byGuest[guest];
            Thought? picked =
                  w.Toilet    >= Urgent ? Thought.Toilet
                : w.Sick      >= Urgent ? Thought.Sick
                : w.Happiness >= Urgent ? Thought.VeryUnhappy
                : w.Happiness <  10     ? Thought.Sad
                : w.Happiness >= 81     ? Thought.Happy
                : w.Boredom   >= Urgent ? Thought.Bored
                : w.Happiness is >= 26 and <= 74 && Rand(10) == 0 ? Thought.VeryUnhappy
                : null;

            if (picked == null)
            {
                // ⭐ The console GIVES THE SLOT BACK here rather than leaving a stale bubble up.
                _holdingBubble.Remove(guest);
                continue;
            }
            // Already holding one? Keep it and do not take a second.
            if (!_holdingBubble.Contains(guest))
            {
                if (_holdingBubble.Count >= BubbleBudget) continue;
                _holdingBubble.Add(guest);
            }
            w.Thought = picked.Value;
            _byGuest[guest] = w;
        }
    }

    void Rise()
    {
        foreach (int guest in _byGuest.Keys.ToArray())
        {
            var w = _byGuest[guest];
            w.Toilet = Clamp(w.Toilet + Roll(Rates["toilet"]));
            w.Sick = Clamp(w.Sick + Roll(Rates["sick"]));
            w.Litter = Clamp(w.Litter + Roll(Rates["litter"]));
            _byGuest[guest] = w;
        }
    }

    int Roll(Rate r) => r.Base + (r.High ? RollHigh(r.Spread + 1) : RollCentred(r.Spread + 1));

    /// ⚠ <paramref name="thirstReduction"/> keeps its name for its callers, and what happens to
    /// it DEPENDS ON THE ARM: food adds it, drink subtracts it. ⭐ Litter IS applied now --
    /// `LitterBase` (30, read from `DAT_002EEB60`) plus `rand(25)`, on the food and drink arms
    /// only. ⚠ NOT the .sam's `LitterEffect`, which is 50 for a burger and is not what runs.
    /// ⚠⚠ This comment said the opposite of both until astraclaw read it against the body:
    /// a stale comment is worse than none, because it is trusted.
    /// <summary>A purchase, with the shop's own DBA effects. ⭐ All four come straight out of
    /// findings/dba.md's decode of the purchase path at `0x20E380..0x20E45C`, including the one
    /// that reads oddly and is right: the hunger reduction is subtracted from hunger AND added to
    /// the toilet.</summary>
    /// <summary>⭐⭐ HOW BADLY THIS GUEST WANTS THIS THING, which is what the console weighs
    /// against the price. Read whole from `0x20E1A0` and its accessors:
    ///
    /// <code>
    ///   base   = record[0x2e] * ((quality &gt;&gt; 2) + 75 - (customers &gt;&gt; 2)) / 100   // FUN_001D1B08
    ///   desire = thirst*ThirstReduction/100 + hunger*HungerReduction/100 + 100
    ///          - sick*VomitIncrease/100 + (100-happiness)*HappinessEffect/100
    ///   score  = base * 125/100 * desire/100 * (happiness+100)/100
    /// </code>
    ///
    /// ⭐ Every accessor in it resolved against this port's OWN compiled-record parser, which is
    /// the corroboration that matters: `FUN_001D1D08` reads `record[0x30]` and the parser calls
    /// that `Product`; `FUN_001D1B88` reads `[0x34]` = `HappinessEffect`; `FUN_001D1CC8` reads
    /// `[0x36]` = `VomitIncrease`. Two independent routes to one layout.
    /// ⚠ And it renames one thing by implication: `[0x2e]` is parsed as `BaseCostOfGoods`, and
    /// what the executable does with it is scale a DESIRE, not a cost.
    ///
    /// ⚠⚠ THE CUSTOMER TERM IS DELIBERATELY NOT PASSED. `FUN_001D1FB8` reads the shop's running
    /// customer count at `+0xAC` and the formula SUBTRACTS a quarter of it -- so at 300 lifetime
    /// customers the base reaches zero and the shop never sells again. The console must reset or
    /// decay that counter and **nothing found so far does it** (the lavatory arm zeroes `+0xAC`,
    /// shops have no equivalent yet read). Porting a term that only ever falls would hand this
    /// port a slow, silent shop death and call it fidelity. It goes in when its reset is read.
    ///
    /// ⚠ Quality (`+0xBA`) IS passed, and is 0 until something sets it -- which is the console's
    /// own arithmetic on an unset field, `(0 &gt;&gt; 2) + 75`, not a fallback invented here.</summary>
    public static int WantScore(VisitorWants w, int baseValue, int quality,
                                int hungerReduction, int thirstReduction,
                                int happinessEffect, int vomitIncrease)
    {
        int bass = baseValue * ((quality >> 2) + 75) / 100;
        int desire = w.Thirst * thirstReduction / 100
                   + w.Hunger * hungerReduction / 100
                   + 100
                   - w.Sick * vomitIncrease / 100
                   + (100 - w.Happiness) * happinessEffect / 100;
        return bass * 125 / 100 * desire / 100 * (w.Happiness + 100) / 100;
    }

    /// <param name="baseValue">`record[0x2e]`, the compiled `BaseCostOfGoods`. ⚠⚠ ZERO MEANS
    /// UNKNOWN AND SKIPS THE WANT TEST, on purpose and loudly: the identity join has known
    /// failures (two facilities per world do not resolve), and a shop whose record is missing
    /// would otherwise score 0, refuse everyone, and read exactly like a broken want system.
    /// A definition that never joined should behave as it did before the gate existed.</param>
    public bool Buy(int guest, int price, int hungerReduction, int thirstReduction,
                    int happinessEffect, int vomitIncrease, int product = Food,
                    int baseValue = 0, int quality = 0)
    {
        if (!_byGuest.TryGetValue(guest, out var w)) return false;
        // ⭐⭐ WANTING IT COMES FIRST, AND THE PORT HAD NO SUCH TEST. `0x20E1A0` gates the whole
        // block on `price < score && price * 10 <= cash` -- TWO conditions, and this had one. A
        // guest who could afford something bought it however little they wanted it, so a park of
        // burger vans fed people who were not hungry. See <see cref="WantScore"/>.
        if (System.Environment.GetEnvironmentVariable("TPW_WANT_TRACE") != null)
            System.Console.Error.WriteLine($"[want] price={price} base={baseValue} q={quality} "
              + $"hun={w.Hunger}/{hungerReduction} thi={w.Thirst}/{thirstReduction} "
              + $"sick={w.Sick}/{vomitIncrease} hap={w.Happiness}/{happinessEffect} "
              + $"score={WantScore(w, baseValue, quality, hungerReduction, thirstReduction, happinessEffect, vomitIncrease)}");
        if (baseValue > 0 && price >= WantScore(w, baseValue, quality,
                                                hungerReduction, thirstReduction,
                                                happinessEffect, vomitIncrease)) return false;
        // ⚠⚠ AFFORDABILITY SECOND, AND IN THE SAME x10 UNITS. A guest who cannot afford it does
        // not pay, and does not eat either. Applying the effects and letting cash go negative
        // would feed the park for free and look like generosity rather than a missing guard.
        if (w.Cash < price * 10) return false;
        w.Cash -= price * 10;

        // ⭐⭐ THE COMPILED PRODUCT SELECTS THE ARM, and it must: adding thirst unconditionally
        // feeds a burger correctly and makes a DRINK SHOP raise thirst too, which is the exact
        // regression astraclaw caught in the previous patch. Food and drink are MIRRORS -- each
        // lowers the need it serves, raises the other, and adds ITS OWN amount to the bladder.
        // ⚠ Selected on the compiled `Product` byte, never on the authored `ShopType` and never
        // on which effect happens to be larger.
        switch (product)
        {
            // ⭐⭐ 7 FALLS THROUGH INTO FOOD. `case 7:` carries NO `break` -- it bumps thirst by
            // `FUN_001D1FB8(shop) / 15` and then runs the entire food arm. I had it as an
            // unmodelled arm that did nothing, which was wrong twice over: it is not unmodelled
            // and it is not nothing. ⚠ That getter is q2, which shop setup writes as **0**, so
            // the bump is `0 / 15 = 0` at default quality and product 7 IS food until something
            // moves q2 -- and what moves it is unread, which is the only honest gap here.
            case Food: case 4: case 5: case 7:
                w.Hunger = Clamp(w.Hunger - hungerReduction);
                w.Toilet = Clamp(w.Toilet + hungerReduction);
                w.Thirst = Clamp(w.Thirst + thirstReduction);
                w.Sick = Clamp(w.Sick + vomitIncrease);
                w.Happiness = Clamp(w.Happiness + happinessEffect);
                w.Litter = Clamp(w.Litter + LitterBase + Rand(25));
                break;
            case Drink:
                w.Thirst = Clamp(w.Thirst - thirstReduction);
                w.Toilet = Clamp(w.Toilet + thirstReduction);
                w.Hunger = Clamp(w.Hunger + hungerReduction);
                w.Sick = Clamp(w.Sick + vomitIncrease);
                w.Happiness = Clamp(w.Happiness + happinessEffect);
                w.Litter = Clamp(w.Litter + LitterBase + Rand(25));
                break;
            case Costume:
                // ⭐ A costume shop CHANGES WHO YOU ARE: the arm writes `guest[0x7d] = 8`, the
                // personality index, so the guest leaves wanting a different kind of ride.
                // ⚠⚠ `Preferences.Length > 8` was ALWAYS FALSE -- the array holds eight entries,
                // indices 0..7 -- so the promised change silently never happened. A guard written
                // against an array that cannot satisfy it is dead code wearing a safety check.
                // astraclaw caught it. ⚠ Row 8 is deliberately NOT added to `Preferences`:
                // that array is what ordinary spawning draws from, and whether a new guest can
                // BE personality 8 is a separate, unverified policy.
                w.PreferredIntensity = CostumePreference;
                // ⚠ The costume arm's happiness scales by q1 ALONE -- `(effect * q1) / 100`, not
                // the food arm's `(q1 - q2/15)`. q1 defaults to 100, so unscaled is correct at a
                // new shop and wrong the moment quality moves, like everywhere else.
                w.Happiness = Clamp(w.Happiness + happinessEffect);
                // ⚠⚠ THIS IS PART OF THE COSTUME FEATURE, NOT THE WHOLE OF IT, and saying so is
                // the point -- a documented boundary is a TODO wearing a hat, so here is the hat
                // off. The console's arm also:
                //   `+0x34 |= 0x80`            a flag on the guest, unread
                //   `FUN_0020BC70(guest)`      actor-side, presumably the visible change of dress
                //   `FUN_001073C0(…, 6, 1)`    a global call, kind unidentified
                // None of those are modelled. So a guest here leaves wanting different rides and
                // looking exactly as they did, which is a THIRD of the feature -- not "costumes
                // work". astraclaw asked for this limitation to be explicit rather than implied
                // by a commit message nobody will read again.
                break;
            case Trinket: case 6:
                // Balloons and the gift shop: happiness and nothing else. No litter, no needs.
                w.Happiness = Clamp(w.Happiness + happinessEffect);
                break;
            default:
                // ⚠⚠ ARM 7 IS NOT MODELLED. It does `+0x7a += n / 15` and the getter supplying
                // `n` has not been identified, so a guest pays and receives NOTHING rather than
                // receiving a number I made up. Loud by omission; astraclaw's controls will see
                // it as a shop that does nothing, which is the truth about this port.
                w.Happiness = Clamp(w.Happiness + happinessEffect);
                break;
        }
        _byGuest[guest] = w;
        return true;
    }

    /// <summary>What a guest is thinking, and therefore which bubble is over their head.
    ///
    /// ⭐⭐ EVERY THRESHOLD HERE IS THE CONSOLE'S, off `FUN_0020C930`:
    ///
    ///   hunger &gt; 90 AND thirst &gt; 90   -> id 14, the combined bubble, tested FIRST
    ///   hunger, then thirst, then toilet -> ids 11, 8, 7, each after finding a facility
    ///        ⭐ and the bar for wanting one is READ, not inferred: `FUN_0020F888` opens with
    ///        `if (need &lt; 0x5b) return 0` -- a single shared threshold of **91** for all three,
    ///        which the caller passes the need into. So "&gt; 90" here is the console's number and
    ///        the same one in each case, rather than three constants that happen to agree.
    ///   sick &gt; 92 (and 1 roll in 4)     -> vomits
    ///   happiness &lt; 3                   -> id 10 and a park alert
    ///   happiness &lt; 5, or cash &lt; 100 -> goes home
    ///   `+0x74` &gt; 89                    -> `FUN_0020D010(guest, 0)`
    ///
    /// ⚠ THE ORDER IS THE CONSOLE'S TOO, and it matters: the combined want is checked before
    /// either single one, so a guest who is both does not simply read as hungry.
    ///
    /// ⚠ What is NOT read is what a guest thinks when nothing is urgent -- the console picks its
    /// idle behaviour with `rand(6)` and only some arms set a thought at all. Normal is returned
    /// here, and Bored/Sad/Good/Bad/Scared/BadQueue/Litter have no rule yet.</summary>
    public Thought Decide(int guest, bool foodNearby, bool drinkNearby, bool toiletNearby)
    {
        if (!_byGuest.TryGetValue(guest, out var w)) return Thought.Normal;
        var t = Thought.Normal;
        if (w.Hunger >= Urgent && w.Thirst >= Urgent) t = Thought.HungryAndThirsty;
        else if (w.Sick > 92) t = Thought.Sick;
        else if (w.Happiness < 3) t = Thought.Angry;
        else if (foodNearby && w.Hunger >= Urgent) t = Thought.Hungry;
        else if (drinkNearby && w.Thirst >= Urgent) t = Thought.Thirsty;
        else if (toiletNearby && w.Toilet >= Urgent) t = Thought.Toilet;
        else if (w.Happiness < 25) t = Thought.Sad;
        else if (w.Happiness > 75) t = Thought.Happy;
        w.Thought = t;
        _byGuest[guest] = w;
        return t;
    }

    /// <summary>⚠ `happiness &lt; 5` or `cash &lt; 100` -- but only while `+0x7B` is below 99.
    /// ⭐ AT 99 OR ABOVE THE GUEST GOES HOME REGARDLESS, which is the other half of
    /// `FUN_0020C930`'s gate and the half a "leaves when unhappy" reading would miss: the check is
    /// `if (+0x7B &lt; 99) { ...reasons... }` with the leave flag set unconditionally after it.</summary>
    public bool WantsToGoHome(int guest)
        => _byGuest.TryGetValue(guest, out var w)
           && (w.Boredom >= 99 || w.Happiness < 5 || w.Cash < 100);

    /// <summary>Getting off a ride, as `FUN_0020EDD8` has it.
    ///
    /// ⭐ THE SHAPE IS READ. Happiness up; sickness by the ride's own value measured against **30**,
    /// and ONLY when the ride is above 55 -- a gentle one leaves it alone, it is never
    /// settled; `+0x78` down by that value;
    /// `+0x7B` down by `rand(20)`.
    ///
    /// ⚠ AND IT LEAVES THE TOILET ALONE, although `FUN_0020EDD8` empties it in the same breath.
    /// That function is one exit path serving rides AND facilities, and emptying a bladder is the
    /// toilet's doing, not a rollercoaster's -- so it lives in <see cref="UseToilet"/> and a caller
    /// picks. This comment used to describe the console's whole function while the code did only
    /// half of it, which astraclaw caught reviewing the contract; a doc that overstates its method
    /// is worse than no doc, because the next reader believes it.
    /// ⭐ Untouched here, and the contract depends on it: `Cash`, `Hunger`, `Thirst`, `Toilet`,
    /// `Litter` and `Thought`. Cash is the tripwire a continuity check watches for a reseed.
    ///
    /// The image constants are now read: sickness1212/4096, boredom4096/4096 and happiness
    /// bands15/10/5. Scale arguments remain explicit port/test parameters; no claim is made
    /// that arbitrary overrides preserve the executable's fixed-point overflow semantics.</summary>
    /// ⚠ <paramref name="happinessGain"/> is now a FALLBACK: when the guest has a preference
    /// the band is computed from the mismatch instead -- see <see cref="RideHappinessFor"/>.
    public void Ride(int guest, int intensity, int happinessGain, float sickScale, float boredomScale)
    {
        if (!_byGuest.TryGetValue(guest, out var w)) return;
        // ⭐ Happiness by how well the ride matched them, not a flat payout.
        int gain = w.PreferredIntensity > 0
                 ? RideHappinessFor(Math.Abs(w.PreferredIntensity - intensity))
                 : happinessGain;
        w.Happiness = Clamp(w.Happiness + gain);
        // ⭐⭐ GATED, and the gate is the whole correction: below 56 the console branches past
        // this entirely. It never subtracts -- a gentle ride leaves a stomach exactly as it was.
        if (intensity >= SickeningIntensity)
            w.Sick = Clamp(w.Sick + (int)(sickScale * (intensity - 30)));
        w.Unknown78 = Clamp(w.Unknown78 - (int)(boredomScale * intensity));
        w.Boredom = Clamp(w.Boredom - Rand(20));
        _byGuest[guest] = w;
    }

    /// <summary>Waiting, as the function labelled `"Toilet"` has it: happiness down for the wait,
    /// and **+5** to `+0x78`. ⚠ NOT boredom -- see <see cref="VisitorWants.Unknown78"/>; queueing
    /// is the only riser that byte has, and what it is a need FOR is still unread.</summary>
    public void Queue(int guest, int happinessCost = 5)
    {
        if (!_byGuest.TryGetValue(guest, out var w)) return;
        w.Happiness = Clamp(w.Happiness - happinessCost);
        w.Unknown78 = Clamp(w.Unknown78 + 5);
        _byGuest[guest] = w;
    }

    /// <summary>Using the toilet. ⭐ The need goes to zero, and whatever it was OVER 60 is handed
    /// back as the mess left behind -- `(toilet - 60) * 2 / 3`, which `FUN_0020EDD8` passes to the
    /// facility itself. Returns that, or 0 for a guest who was not desperate.
    ///
    /// ⭐⭐ AND IT SETTLES A STOMACH BY **40**. `FUN_0020EDD8`'s lavatory arm does
    /// `guest[0x76] -= 0x28` floored at zero, two lines after emptying the bladder -- so a
    /// lavatory is this port's only cure for sickness, and it was missing. A park could make
    /// guests ill and offer no way back.</summary>
    public int UseToilet(int guest)
    {
        if (!_byGuest.TryGetValue(guest, out var w)) return 0;
        int soil = w.Toilet < 61 ? 0 : (w.Toilet - 60) * 2 / 3;
        w.Toilet = 0;
        w.Sick = Clamp(w.Sick - ToiletSicknessRelief);
        _byGuest[guest] = w;
        return soil;
    }

    /// <summary>`FUN_0020EDD8`: `guest[0x76] -= 0x28` on the lavatory arm.</summary>
    public const int ToiletSicknessRelief = 40;

    /// <summary>⭐⭐ A FILTHY LAVATORY COSTS THE GUEST WHO USED IT -- and this file spent a day
    /// saying it cost nobody anything.
    ///
    /// `FUN_0020EDD8`, immediately after wearing the facility down, reads its condition back
    /// through `FUN_00130938` -- a one-line `return facility[0xb4]` -- and below **50**:
    ///
    /// <code>
    ///   guest bubble = 10 (tbangry)
    ///   guest[0x75] -= 10        // happiness
    ///   guest[0x76] += 10        // sickness
    /// </code>
    ///
    /// ⚠⚠ THE EARLIER "NOTHING READS IT BACK" WAS A CENSUS OF `lb`/`lbu` AT `+0xB4`, and this
    /// read is inside an accessor function, so the census could not see it. The note at the time
    /// said exactly that -- "not found, not not-there" -- and it was right to; the lesson is that
    /// the hedge was load-bearing, not decorative. A negative search needs a control that it
    /// MUST hit, and this one had none.
    ///
    /// ⭐ Which also gives the condition a consumer at last: `Wear` had a reader nowhere, so a
    /// cleaner was a feature with no effect. Now dirt has a price and `Service()` buys it off.
    ///
    /// ⚠ Read the condition AFTER the wear, as the console does -- the guest who made the mess
    /// can be the one who is disgusted by it.</summary>
    public const int FilthyBelow = 50;

    public void DirtyLavatory(int guest)
    {
        if (!_byGuest.TryGetValue(guest, out var w)) return;
        w.Happiness = Clamp(w.Happiness - 10);
        w.Sick = Clamp(w.Sick + 10);
        w.Thought = Thought.Angry;
        _byGuest[guest] = w;
    }

    /// <summary>⚠⚠ FORGET WHOEVER IS NO LONGER IN THE PARK. Guest ids are REUSED, and a stale
    /// entry means the next person with that id inherits a dead stranger's hunger. The
    /// coordinator's `Plans.Keys` is the live set, including guests in recovery (astraclaw's
    /// contract, agreed 2026-09-23); call this after its Step.</summary>
    public int Reconcile(IEnumerable<int> live)
    {
        var keep = live as ISet<int> ?? live.ToHashSet();
        // ⚠ A departed guest must hand its bubble slot back, or the budget leaks and the park
        // eventually shows none at all -- the same reused-id trap the needs table itself guards.
        // ⚠ Hoisted off `keep`: building the set inside the predicate would rebuild it per id.
        _holdingBubble.RemoveWhere(id => !keep.Contains(id));
        var gone = _byGuest.Keys.Where(g => !keep.Contains(g)).ToArray();
        foreach (int g in gone) _byGuest.Remove(g);
        return gone.Length;
    }
}
