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

    /// <summary>`+0x78`. ⚠ CANDIDATE: boredom. THE NAME IS NOT READ; THE ARITHMETIC IS.
    /// `FUN_0020C6A8` -- the function labelled `"Toilet"` -- adds **+5** while the guest waits, and
    /// `FUN_0020EDD8`, which ends a ride, SUBTRACTS from it scaled by that ride's own value. Up
    /// while queueing, down when entertained, seeded `rand(40)`, and read by the what-shall-I-do
    /// scorer. That is boredom's shape; nothing in the executable names it.</summary>
    public byte Unknown78;

    /// <summary>`+0x7B`. ⚠ CANDIDATE: had-enough / going-home. Arithmetic read, name not.
    /// `FUN_0020C930` only ASKS whether to leave while this is below 99 -- at 99 or above the
    /// guest goes regardless -- and ending a ride takes `rand(20)` off it. A meter that sends you
    /// home when it maxes and that rides push back down. Seeded `rand(50)`.</summary>
    public byte Unknown7B;

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
    ///   if (DAT_002eeb4c &lt;= guest[0x78]) guest[0x75]--;   // boredom, 95
    ///   if (DAT_002eeb50 &lt;= guest[0x76]) guest[0x75]--;   // sickness, 85
    ///   if (DAT_002eeb54 &lt;= guest[0x79]) guest[0x75]--;   // toilet,   90
    /// </code>
    ///
    /// ⭐ Three separate globals rather than one shared bar -- unlike <see cref="Urgent"/>, which
    /// IS one number for three needs. Worth keeping distinct: they are different questions.
    ///
    /// ⚠ THE VALUES ARE READ FROM THE IMAGE, not from a running machine. That is the weaker of
    /// the two readings -- see the port's own rule that an image is not authority for a runtime
    /// global -- but unlike the classic case these are non-zero, sit in an ordered run, and land
    /// exactly where thresholds belong on a 0..100 need. A savestate would settle it.</summary>
    public int BoredomBar { get; set; } = 95;
    public int SickBar { get; set; } = 85;
    public int ToiletBar { get; set; } = 90;

    /// <summary>How fast each need rises per step, in the console's own `base + roll(spread + 1)`
    /// shape. ⚠ THE NUMBERS ARE CHOSEN, the shape is not -- see the class note.</summary>
    public sealed record Rate(byte Base, byte Spread, bool High);

    /// <summary>⚠⚠ INVENTED, not read -- see the class note. The roll SHAPES are the console's
    /// (hunger, thirst and the toilet take the high roll because that is the helper
    /// `FUN_00211A00` passes them at SPAWN, and the rest take the centred one); the base, the
    /// spread and the cadence are all mine.</summary>
    public Dictionary<string, Rate> Rates { get; } = new()
    {
        ["hunger"] = new Rate(0, 2, High: true),
        ["thirst"] = new Rate(0, 2, High: true),
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
    /// ⚠ CHOSEN. The console applies its rise on a cadence that has not been read; 2.56 s
    /// fills a need over roughly two park minutes. The moment the rate file turns up this becomes
    /// data like the rest.</summary>
    public double SecondsPerRise { get; set; } = 2.56;
    double _sinceRise;

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
            Unknown7B = Clamp(Rand(50)),
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
    /// <summary>What an unmet need does to a mood -- see <see cref="BoredomBar"/>. One point of
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
            int drop = (w.Unknown78 >= BoredomBar ? 1 : 0)
                     + (w.Sick >= SickBar ? 1 : 0)
                     + (w.Toilet >= ToiletBar ? 1 : 0);
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
        _sinceRise += seconds;
        if (_sinceRise < SecondsPerRise) return 0;
        // ⚠ One rise per elapsed period, not one per call: a long step owes several.
        int rises = (int)(_sinceRise / SecondsPerRise);
        _sinceRise -= rises * SecondsPerRise;
        for (int i = 0; i < rises; i++) { Rise(); Fret(); }
        return rises;
    }

    void Rise()
    {
        foreach (int guest in _byGuest.Keys.ToArray())
        {
            var w = _byGuest[guest];
            w.Hunger = Clamp(w.Hunger + Roll(Rates["hunger"]));
            w.Thirst = Clamp(w.Thirst + Roll(Rates["thirst"]));
            w.Toilet = Clamp(w.Toilet + Roll(Rates["toilet"]));
            w.Sick = Clamp(w.Sick + Roll(Rates["sick"]));
            w.Litter = Clamp(w.Litter + Roll(Rates["litter"]));
            _byGuest[guest] = w;
        }
    }

    int Roll(Rate r) => r.Base + (r.High ? RollHigh(r.Spread + 1) : RollCentred(r.Spread + 1));

    /// ⚠ <paramref name="thirstReduction"/> keeps its name for its callers and is ADDED, not
    /// subtracted -- see the body. ⚠ `+0x74` litter (`base + rand(25)`) is still NOT applied:
    /// the base is not the .sam's `LitterEffect` (50 for a burger against an observed 30), so
    /// its source is unidentified and inventing one is how a wrong number gets a comment.
    /// <summary>A purchase, with the shop's own DBA effects. ⭐ All four come straight out of
    /// findings/dba.md's decode of the purchase path at `0x20E380..0x20E45C`, including the one
    /// that reads oddly and is right: the hunger reduction is subtracted from hunger AND added to
    /// the toilet.</summary>
    public bool Buy(int guest, int price, int hungerReduction, int thirstReduction,
                    int happinessEffect, int vomitIncrease)
    {
        if (!_byGuest.TryGetValue(guest, out var w)) return false;
        // ⚠⚠ AFFORDABILITY FIRST, AND IN THE SAME x10 UNITS. `0x20E1A0` gates the WHOLE block on
        // `price * 10 <= cash` -- so a guest who cannot afford it does not pay, and does not eat
        // either. Applying the effects and letting cash go negative would feed the park for free
        // and look like generosity rather than a missing guard. astraclaw asked for this to be
        // kept with the tenfold debit; they are one change, not two.
        if (w.Cash < price * 10) return false;
        // ⚠⚠ TEN TIMES THE PRICE. `0x20E1A0` does `cash += price * -10`, and cash is kept in the
        // same x10 units the spawn seeds it in (`(rand(300)+200) * 10`). This charged the bare
        // price and undercharged every purchase by an order of magnitude.
        w.Cash -= price * 10;
        w.Hunger = Clamp(w.Hunger - hungerReduction);
        // ⭐ The toilet rises by the HUNGER amount, traced rather than assumed: the default arm
        // re-reads the same `+0x1ec` getter for `+0x79` that it subtracted from `+0x77`.
        w.Toilet = Clamp(w.Toilet + hungerReduction);
        // ⚠⚠ EATING MAKES YOU THIRSTIER -- IT DOES NOT QUENCH YOU. The console ADDS the shop's own
        // thirst value to `+0x7a` (getter `+0x1e4`); this subtracted it, so every purchase was
        // slaking a thirst the game intends to create. astraclaw verified the getter mapping
        // (`+1E4` thirst, `+1EC` hunger) and that ice cream raises thirst by its own 5 rather
        // than by any function of its hunger 15.
        w.Thirst = Clamp(w.Thirst + thirstReduction);
        w.Happiness = Clamp(w.Happiness + happinessEffect);
        w.Sick = Clamp(w.Sick + vomitIncrease);
        // ⭐ LITTER, and its base is READ: `DAT_002EEB60` is 30 in the image, loaded at
        // `0x20E504` and added to `rand(25)`. ⚠ It is NOT the .sam's `LitterEffect` (50 for a
        // burger) -- I had assumed the authored field and astraclaw found the actual byte, which
        // is the third time tonight the authored text was not what runs.
        w.Litter = Clamp(w.Litter + LitterBase + Rand(25));
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
           && (w.Unknown7B >= 99 || w.Happiness < 5 || w.Cash < 100);

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
        w.Unknown7B = Clamp(w.Unknown7B - Rand(20));
        _byGuest[guest] = w;
    }

    /// <summary>Waiting, as the function labelled `"Toilet"` has it: happiness down for the wait,
    /// and **+5** to `+0x78` -- the byte whose arithmetic reads as boredom.</summary>
    public void Queue(int guest, int happinessCost = 5)
    {
        if (!_byGuest.TryGetValue(guest, out var w)) return;
        w.Happiness = Clamp(w.Happiness - happinessCost);
        w.Unknown78 = Clamp(w.Unknown78 + 5);
        _byGuest[guest] = w;
    }

    /// <summary>Using the toilet. ⭐ The need goes to zero, and whatever it was OVER 60 is handed
    /// back as the mess left behind -- `(toilet - 60) * 2 / 3`, which `FUN_0020EDD8` passes to the
    /// facility itself. Returns that, or 0 for a guest who was not desperate.</summary>
    public int UseToilet(int guest)
    {
        if (!_byGuest.TryGetValue(guest, out var w)) return 0;
        int soil = w.Toilet < 61 ? 0 : (w.Toilet - 60) * 2 / 3;
        w.Toilet = 0;
        _byGuest[guest] = w;
        return soil;
    }

    /// <summary>⚠⚠ FORGET WHOEVER IS NO LONGER IN THE PARK. Guest ids are REUSED, and a stale
    /// entry means the next person with that id inherits a dead stranger's hunger. The
    /// coordinator's `Plans.Keys` is the live set, including guests in recovery (astraclaw's
    /// contract, agreed 2026-09-23); call this after its Step.</summary>
    public int Reconcile(IEnumerable<int> live)
    {
        var keep = live as ISet<int> ?? live.ToHashSet();
        var gone = _byGuest.Keys.Where(g => !keep.Contains(g)).ToArray();
        foreach (int g in gone) _byGuest.Remove(g);
        return gone.Length;
    }
}
