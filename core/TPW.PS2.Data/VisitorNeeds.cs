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

    /// <summary>`+0x74`. ⚠ NOT IDENTIFIED. Above 89 it triggers `FUN_0020D010(guest, 0)`, and the
    /// sibling call `(guest, 1)` is what an unhappy guest does — so the pair is very likely litter
    /// and vandalism. Which is which, and what this counts, is NOT read. Named by its offset.</summary>
    public byte Unknown74;
    /// <summary>`+0x78`. ⚠ NOT IDENTIFIED. Seeded `rand(40)` and read by the shop scorer.</summary>
    public byte Unknown78;
    /// <summary>`+0x7B`. ⚠ NOT IDENTIFIED. Below 99 it gates the whole leave-the-park check.</summary>
    public byte Unknown7B;

    /// <summary>`+0x60`, a word, not a byte and not clamped. Spawns at `(rand(300) + 200) * 10`,
    /// so 2000..4990. Below 100 the guest goes home.</summary>
    public int Cash;

    /// <summary>`+0x40`, the id the thought bubble is drawn from.</summary>
    public Thought Thought;
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
/// ⚠⚠ WHAT IS NOT READ: THE RATES. The console's needs rise from a 26-byte record it streams out
/// of a data file (`FUN_0015FD48` hands out successive records by bumping `DAT_00395C84`), giving
/// a `base` and a `spread` per need. The file has not been found, so <see cref="Rates"/> holds
/// CHOSEN numbers in the engine's own shape. The shapes and the thresholds are read; the rates
/// are not, and they are kept in one table so that finding the file is a data change.
///
/// ⚠ Keyed by GUEST ID, never held on a walking Guest object: readmission after a ride preserves
/// the id but builds a NEW Guest (astraclaw, reviewing the boundary). <see cref="Reconcile"/>
/// against the coordinator's live ids, or a reused id inherits a dead stranger's hunger.</summary>
public sealed class VisitorNeeds
{
    public const byte Full = 100;

    /// <summary>How fast each need rises per step, in the console's own `base + roll(spread + 1)`
    /// shape. ⚠ THE NUMBERS ARE CHOSEN, the shape is not -- see the class note.</summary>
    public sealed record Rate(byte Base, byte Spread, bool High);

    /// <summary>⚠ CHOSEN. Hunger, thirst and the toilet use the HIGH roll because that is which
    /// helper `FUN_00211A00` passes them; the rest use the centred one, for the same reason.</summary>
    public Dictionary<string, Rate> Rates { get; } = new()
    {
        ["hunger"] = new Rate(0, 2, High: true),
        ["thirst"] = new Rate(0, 2, High: true),
        ["toilet"] = new Rate(0, 1, High: true),
        ["sick"] = new Rate(0, 0, High: false),
        ["unknown74"] = new Rate(0, 1, High: false),
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
            Unknown74 = Clamp(Rand(40)),
            Happiness = Clamp(50),
            Sick = Clamp(Rand(50)),
            Hunger = Clamp(Rand(70)),
            Cash = (Rand(300) + 200) * 10,
            Unknown78 = Clamp(Rand(40)),
            Toilet = Clamp(Rand(100) * Rand(100) / 100),
            Thirst = Clamp(Rand(100) * Rand(100) / 100),
            Unknown7B = Clamp(Rand(50)),
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
    public void Step(double seconds)
    {
        if (seconds <= 0d) return;
        _sinceRise += seconds;
        if (_sinceRise < SecondsPerRise) return;
        // ⚠ One rise per elapsed period, not one per call: a long step owes several.
        int rises = (int)(_sinceRise / SecondsPerRise);
        _sinceRise -= rises * SecondsPerRise;
        for (int i = 0; i < rises; i++) Rise();
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
            w.Unknown74 = Clamp(w.Unknown74 + Roll(Rates["unknown74"]));
            _byGuest[guest] = w;
        }
    }

    int Roll(Rate r) => r.Base + (r.High ? RollHigh(r.Spread + 1) : RollCentred(r.Spread + 1));

    /// <summary>A purchase, with the shop's own DBA effects. ⭐ All four come straight out of
    /// findings/dba.md's decode of the purchase path at `0x20E380..0x20E45C`, including the one
    /// that reads oddly and is right: the hunger reduction is subtracted from hunger AND added to
    /// the toilet.</summary>
    public void Buy(int guest, int price, int hungerReduction, int thirstReduction,
                    int happinessEffect, int vomitIncrease)
    {
        if (!_byGuest.TryGetValue(guest, out var w)) return;
        w.Cash -= price;
        w.Hunger = Clamp(w.Hunger - hungerReduction);
        w.Toilet = Clamp(w.Toilet + hungerReduction);
        w.Thirst = Clamp(w.Thirst - thirstReduction);
        w.Happiness = Clamp(w.Happiness + happinessEffect);
        w.Sick = Clamp(w.Sick + vomitIncrease);
        _byGuest[guest] = w;
    }

    /// <summary>What a guest is thinking, and therefore which bubble is over their head.
    ///
    /// ⭐⭐ EVERY THRESHOLD HERE IS THE CONSOLE'S, off `FUN_0020C930`:
    ///
    ///   hunger &gt; 90 AND thirst &gt; 90   -> id 14, the combined bubble, tested FIRST
    ///   hunger, then thirst, then toilet -> ids 11, 8, 7, each after finding a facility
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
        if (w.Hunger > 90 && w.Thirst > 90) t = Thought.HungryAndThirsty;
        else if (w.Sick > 92) t = Thought.Sick;
        else if (w.Happiness < 3) t = Thought.Angry;
        else if (foodNearby && w.Hunger > 90) t = Thought.Hungry;
        else if (drinkNearby && w.Thirst > 90) t = Thought.Thirsty;
        else if (toiletNearby && w.Toilet > 90) t = Thought.Toilet;
        else if (w.Happiness < 25) t = Thought.Sad;
        else if (w.Happiness > 75) t = Thought.Happy;
        w.Thought = t;
        _byGuest[guest] = w;
        return t;
    }

    /// <summary>⚠ `happiness &lt; 5` or `cash &lt; 100`, and `FUN_0020C930` only asks at all while
    /// `+0x7B` is below 99.</summary>
    public bool WantsToGoHome(int guest)
        => _byGuest.TryGetValue(guest, out var w) && w.Unknown7B < 99 && (w.Happiness < 5 || w.Cash < 100);

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
