namespace TPW.PS2.Data;

/// <summary>⭐ The object class byte `+0x96` (`vt+0xA4` = `0x1E1D68`) for the FOUR ride classes a
/// mechanic can be sent to -- 1 coaster, 3 ordinary, 6 track, 7 tour -- and None for everything else
/// (2 feature, 4 shop, 5 sideshow, and any placement the port holds with no compiled record).
/// findings/staff-mechanics-guards.md §1.2: those four share one broken test (`vt+0xC4` = `0x1E2830`,
/// status 4 or 5) and one Life getter (`vt+0x2CC` = `0x1E1D58`) but each has its own wear gate,
/// breakdown check and enter-4/5/6 handlers.</summary>
public enum RideServiceClass : byte { None = 0, Coaster = 1, Ordinary = 3, Track = 6, Tour = 7 }

/// <summary>⭐⭐ AREA C, THE RIDE SIDE: the fields every one of the four ride classes carries on its
/// parent object (findings/staff-mechanics-guards.md §1.1; parent offsets).
///
/// ⚠ NOT <see cref="Condition"/>. That is the LAVATORY's `+0xB4`, a different class's field; a ride's
/// state of repair is <see cref="Reliability"/>.</summary>
public sealed partial class ParkRide
{
    /// <summary>⭐⭐ `+0xE4`, RELIABILITY, s32 20.12 fixed point: `0x64000` = 100.0. Written by the
    /// wear `0x117B88`, set back to 100.0 by the defaults `0x116120` and the repaired enter
    /// `0x116660`, reloaded by `0x116BA8` as a byte `&lt;&lt; 12`. Breakdown is DETERMINISTIC: below
    /// 10.0 (`0xA000`) a ride breaks (status 4), at 0 it breaks for good (5) -- no random failure
    /// exists (§1.2). ⚠ Internal set: the only writers are the ones named here, plus the labelled
    /// <see cref="ForceReliabilityForTest"/>.</summary>
    public int Reliability { get; internal set; } = ParkSim.FullReliability;

    /// <summary>`0x118228`: `+0xE4 &gt;&gt; 12`, the whole part -- what the ride screen's "State of Repair"
    /// bar (text 644) and All Rides' (128) draw.</summary>
    public int ReliabilityPercent => Reliability >> 12;

    int? _life;
    /// <summary>⭐ `+0x94`, s16 LIFE (getter `vt+0x2CC` = `0x1E1D58`, setter `vt+0x2C4` = `0x1E1CF0`).
    /// `0x116048` stores the tier-ZERO record's `+0x34` (`InitialCondition`, `lw v1,0x34(v0)` /
    /// `sh v1,0x94(s0)` at `0x1160D8..0x1160E0`) straight into it -- not through the setter, so no
    /// advisor -- and nothing ever raises it again: not a repair, not an upgrade. The wear takes one
    /// point per 15.0 of reliability lost; at 0 the ride is condemned (§1.4).
    /// ⚠ Read lazily from the record, so a ride the audits build with an object initializer has it too.</summary>
    public int Life { get => _life ?? InitialLife; internal set => _life = unchecked((short)value); }
    int InitialLife => Definition?.CompiledEntry is { HasRideTiers: true } e ? e.Tier(0).InitialCondition : 0;

    /// <summary>⭐ `+0x11C`, the SERVICE FLAG ("needs / has a mechanic job"): set by `0x118568`, cleared
    /// only by `0x118678` (the mechanic's `0x1786D0`) and at build (`0x116048`). While it is set the
    /// ride's script sees `VAR_BREAKSTAT` (variable 4) = 1 -- which is what makes a scripted ride smoke.</summary>
    public bool ServiceFlag { get; internal set; }

    /// <summary>⭐ `+0x80`, the ASSIGNED MECHANIC (natively his `C`): written only by `0x1E1DF8` --
    /// set by the dispatch `0x178CF8`, cleared by `0x1786D0`, `0x178458`, `0x179328`, `0x1794C8`.</summary>
    public StaffMember AssignedMechanic { get; internal set; }

    /// <summary>⭐ `+0x128`, UPGRADE PENDING: 1 from the request `0x1D5C00`, 0 from the install
    /// `0x116268` and the build `0x116048`. The ride panel's build `0x1D4C80` copies it to
    /// `ui+0x18E0`; that field's reader is not traced (§9).</summary>
    public bool UpgradePending { get; internal set; }

    /// <summary>`+0x120`, riders aboard, for the classes the port runs through the script handshake
    /// (ordinary): +1 on a LETMEON offer (`0x117C90`), -1 on a LETMEOFF collection (`0x117E08`).
    /// ⚠ ADAPTER: the port's handshake offers without the console's `0x1FA528` room test and head
    /// state 0x12, so this counts the port's offers. The track and coaster classes keep their own
    /// (<see cref="TrackRideSim.Riders"/>, <see cref="CoasterSim.Riders"/>).</summary>
    public int NativeRiders { get; internal set; }

    /// <summary>Which of the four ride classes this is, by its compiled record's kind (the port's
    /// stand-in for `+0x96`).</summary>
    public RideServiceClass ServiceClass => Definition?.CompiledEntry?.Kind switch
    {
        AssetResourceDatabase.AssetKind.Coaster => RideServiceClass.Coaster,
        AssetResourceDatabase.AssetKind.Ride => RideServiceClass.Ordinary,
        AssetResourceDatabase.AssetKind.TrackRide => RideServiceClass.Track,
        AssetResourceDatabase.AssetKind.TourRide => RideServiceClass.Tour,
        _ => RideServiceClass.None,
    };

    /// <summary>Whether the class's own operation runs here: an ordinary ride through its script, a
    /// track ride or coaster once its sim is attached. ⚠ A tour ride: the port has NO native tour
    /// class (`0x369F10` -- transports, `0x1E92E8`'s status logic, the wear `0x1EA000`); its script's
    /// TOUR handler is a stub (RseMachine.RideSubsystem), so it runs as a scripted ride and is served
    /// only for Life and upgrades, not wear (see <see cref="ParkSim"/>'s ride-service notes).</summary>
    internal bool ServiceOperated => ServiceClass switch
    {
        RideServiceClass.Ordinary or RideServiceClass.Tour => true,
        RideServiceClass.Track => Track != null,
        RideServiceClass.Coaster => Coaster != null,
        _ => false,
    };

    /// <summary>`+0x9A`, the status, as each class already models it: the track ride's and the
    /// coaster's own, else <see cref="DestinationState"/>.</summary>
    public byte Status => Track != null ? (byte)Track.Status : Coaster != null ? (byte)Coaster.Status : DestinationState;

    /// <summary>`vt+0xC4` = `0x1E2830`: `(u8)(status - 4) &lt; 2`, broken down (4 or 5).</summary>
    public bool Broken => unchecked((byte)(Status - 4)) < 2;

    /// <summary>`+0x98`, the NATIVE rotation 0..3 (`0x1E1DE8`). ⚠ ADAPTER: the port's
    /// <see cref="PlacementTurns"/> number the other way round -- one port turn is the console's
    /// rotation 3 (ShopEntrance's note; `0x1E2288`'s rotation-3 formula `(d-1-z, x)` is
    /// PlacedDestination's one-turn step) -- so this is `(4 - turns) &amp; 3`, and 0 for an unplaced
    /// headless fixture.</summary>
    public int NativeRotation => PlacementTurns is int t ? (4 - (t & 3)) & 3 : 0;

    /// <summary>⚠⚠ TEST HOOK, NOT A NATIVE WRITER. Sets <see cref="Reliability"/> (20.12) so a smoke
    /// can break a ride in seconds instead of thousands of ticks of occupied running. Nothing in the
    /// console writes reliability from outside the ride.</summary>
    public void ForceReliabilityForTest(int raw) => Reliability = raw;

    readonly List<int> _ejected = new();
    /// <summary>Guests the ride has sent out of its queue with event 7 (`0x117798(ride, 0)`: unlinked,
    /// `{0x35A548, 7}`, shown, re-registered), oldest first, since last read. <see cref="ParkVisitors"/>
    /// walks them back out, as it collects <see cref="Left"/>.</summary>
    public IReadOnlyList<int> Ejected => _ejected;
    internal void Ejects(int guest) => _ejected.Add(guest);
    public void ClearEjected() => _ejected.Clear();
}

/// <summary>⭐⭐ AREA C, THE RIDE SIDE OF WEAR, BREAKDOWN AND REPAIR (findings/staff-mechanics-guards.md
/// §1, coaster-operation.md §2-§4, track-ride-operation.md §3, §6), per class exactly as §1.2's table:
/// <code>
///   class     wear 0x117B88 applied                     breakdown check           enter 4 / 5 / 6
///   ordinary  script boarding pass 0x1166A8 when the    0x116D68: 2 and &lt;10.0 -> 4  0x1B8C28 / 0x1B8BF0 / flag
///             script reports running (vars 9, 5),                                 (queue only if Life&lt;1)
///             ticks 2/4/10/11
///   track     cars exist, status != 10 (0x2023B0)       0x200358: ANY and &lt;10 -> 4  0x2002D8 / parent / 0x200310
///   coaster   status-10 tick with riders, every          0x1228D0: 2 or 10          0x1229A0 / parent / flag
///             status-4 tick (0x122AF8 / 0x122BB8)
///   tour      ⚠ not ported (no native tour class here)  (0x1EA1C0)                 0x1E94B8 / parent / 0x1E94F8
///   all:      rel == 0 in 4 -> 5; the service flag set every update in 4/5; Life 0 -> condemned
/// </code>
///
/// ⚠ ADAPTERS, labelled where they are used: the tick counter (<see cref="Tick"/>, the sim's 40 ms
/// count, standing in for `[0x397644]`); the ordinary ride's rider count (<see cref="ParkRide.NativeRiders"/>);
/// the track ride's settings (the port keeps a second copy on <see cref="TrackRideSim"/>); the advisor
/// and sound sinks; test-park mode (`[0x2B72A8]`, which zeroes every wear rate) does not exist here.</summary>
public sealed partial class ParkSim
{
    /// <summary>100.0 in 20.12 (`0x116120` `lui 6; ori 0x4000`, `0x116660`).</summary>
    public const int FullReliability = 0x64000;
    /// <summary>10.0: every breakdown check's threshold (`0x116D68`, `0x200358`, `0x1228D0`: `rel &lt; 0xA000`).</summary>
    public const int BreakdownBelow = 0xA000;
    /// <summary>15.0: one Life point per this much reliability worn (`0x117B88`: `old/0xF000 - new/0xF000`).</summary>
    public const int ReliabilityPerLife = 0xF000;
    /// <summary>`0x153D10 → 0x153950(0x3953E8, &amp;[0x2B739C], 15, ride)`: fifteen slots.</summary>
    public const int UpgradeListCapacity = 15;

    /// <summary>Advisor messages the ride side raises (text rows, findings §1.5, §1.4).</summary>
    public const int AdvisorBreakdownImminent = 0x36, AdvisorNoMechanics = 0x37, AdvisorBusyMechanics = 0x38,
                     AdvisorMechanicOnIt = 0x39, AdvisorCondemned = 0x87;

    /// <summary>⚠ ADAPTER for `0x1C4930` = `[0x397644]`, the frame counter every `tick &amp; 3` test reads:
    /// the sim's executed 40 ms ticks (the same count <see cref="TrackRideSim.Step"/> is handed).
    /// <see cref="ParkStaff.Now"/> is the staff's own count of the same frames.</summary>
    public uint Tick => unchecked((uint)(Time / TickMilliseconds));

    readonly List<ParkRide> _upgrades = new();
    /// <summary>⭐ THE UPGRADE LIST `0x3953E8` (u32[15], count `[0x2B739C]`): rides waiting for a
    /// mechanic to install their next tier, in request order. Added by <see cref="ParkStaff.RequestUpgrade"/>
    /// (`0x124270`), removed on install (`0x178B10`), condemnation (`0x1169C0`) or demolition
    /// (`0x14B9D0`), cleared when the last mechanic is fired (`0x1542A0`). ⚠ Not saved natively either.</summary>
    public IReadOnlyList<ParkRide> UpgradeList => _upgrades;

    /// <summary>`0x153950` as `0x153D10` calls it: 1 if already present; 0 WITHOUT adding when the list
    /// holds 15; else append.</summary>
    internal bool AddUpgrade(ParkRide ride)
    {
        if (_upgrades.Contains(ride)) return true;
        if (_upgrades.Count >= UpgradeListCapacity) return false;
        _upgrades.Add(ride);
        return true;
    }
    /// <summary>`0x153D70 → 0x153B00`: remove, shifting the tail down.</summary>
    internal bool RemoveUpgrade(ParkRide ride) => _upgrades.Remove(ride);
    /// <summary>`0x1542A0`: n = 0.</summary>
    internal void ClearUpgrades() => _upgrades.Clear();

    /// <summary>⭐ The advisor sink: (message id, the ride attached by `0x107CB0`) -- <see cref="ParkAdvisor.Submit"/>
    /// (<see cref="ParkAdvisor.Attach"/> wires it, and the staff's own, <see cref="ParkStaff.Advisor"/>).</summary>
    public Action<int, ParkRide> Advisor { get; set; }

    /// <summary>`0x103658`, which of 0x37/0x38/0x39 the enter-5 handler posts. Set when a
    /// <see cref="ParkStaff"/> is attached (<see cref="ParkVisitors.Staff"/>); null = no staff, so
    /// "no mechanics" (0x37), which is what the console says with none hired.</summary>
    public Func<int> BreakdownMessage { get; set; }

    readonly short[] _advisorCounters = new short[22];
    /// <summary>`0x1073C0(adv, i, d)` = `0x10DDD8(adv[0x264], i, d)`: s16 slot i at `+0x9E + 2i`, clamped
    /// to +-30000 (findings §1.5). Slot 0 counts enters of 4, slot 1 enters of 5. ⚠ The consumer is
    /// not traced; kept so it is there when it is.</summary>
    public IReadOnlyList<short> AdvisorCounters => _advisorCounters;
    void Count(int slot, int delta)
    {
        _advisorCounters[slot] = (short)Math.Clamp(_advisorCounters[slot] + delta, -30000, 30000);
        RaiseAdvisorEvent(slot, delta);
    }

    /// <summary>⭐ The advisor's event-counter sink, `0x1073C0(adv, j, d)`: every writer site the port has
    /// raises through here (the rides' 0/1 above, the staff's 2/20/21, a queue quit's 3, the shops' 5..7,
    /// 9..12, 14..17, the entrance's 19) -- <see cref="ParkAdvisor.CountEvent"/>, which drops them unless the
    /// advisor's flags bit 3 is set. Null: nobody counts (the per-class arrays above still do).</summary>
    public Action<int, int> AdvisorEvent { get; set; }
    internal void RaiseAdvisorEvent(int j, int d) => AdvisorEvent?.Invoke(j, d);

    /// <summary>⭐ A ride-side sound: (ride, native audio category, event, positional). Category 2 is
    /// `AUDIO/GLOBAL/ride` (findings/sound.md's registry): 0x70 at enter 5, 0x18 on condemnation
    /// (non-positional), 0xB8/0xE1 on an upgrade. ⚠ The enter-4 call is `0x111150(audio, 0xC, 0xE2, 0)`,
    /// whose second argument is NOT that registry id (0x111150 special-cases 0xB, 0xE and 0xF; untraced),
    /// so 0xC is passed as read and a view should not map it.</summary>
    public Action<ParkRide, int, int, bool> RideSound { get; set; }

    /// <summary>Instrumentation: wear applications (each `0x117B88` that passed its phase test), and
    /// installs refused past the last tier (see <see cref="InstallUpgrade"/>).</summary>
    public long WearEvents { get; private set; }
    public int UpgradesPastLastTier { get; private set; }

    // --------------------------------------------------------------------------------------------
    // Wear.

    /// <summary>⭐ `vt+0x36C`, the wear rate per class, with `p` = 0 (riders -- what the wear uses) or
    /// 1 (the capacity setting -- what the Reliability readout uses):
    /// - ordinary `0x1B80E0` and tour `0x1E9E10`: <see cref="NativeRideReliability.Wear"/> against the
    ///   tier's CapacityParameter (`vt+0x344` = `0x117B28`, READ for both vtables);
    /// - coaster `0x1225F8`: the same formula against `0x1204D0` = cars/train x seats/car x 6;
    /// - track `0x201F78`: <see cref="NativeRideReliability.TrackWear"/> (a length third, then / 3).
    /// The tier is the ride's own (`+0x126`). ⚠ Test-park mode (`0x153410`) zeroes it natively; the port
    /// has no test park. ⚠ The track ride reads its OWN sim's speed/capacity: the port keeps the
    /// settings twice for that class (<see cref="TrackRideSim.Speed"/> drives the cars).</summary>
    public static int WearRate(ParkRide r, int p)
    {
        if (r?.Definition?.CompiledEntry is not { HasRideTiers: true } e) return 0;
        var t = e.Tier(r.CurrentTier);
        switch (r.ServiceClass)
        {
            case RideServiceClass.Track when r.Track != null:
                return NativeRideReliability.TrackWear(t.MinSpeedDamage, t.MinCapacityDamage, t.WearRate,
                    r.Track.Speed, p != 0 ? r.Track.Capacity : r.Track.Riders, t.CapacityParameter,
                    r.Track.Track.Pieces.Count);                                  // u8 +0x26F8
            case RideServiceClass.Coaster when r.Coaster != null:
                return NativeRideReliability.Wear(t.MinSpeedDamage, t.MinCapacityDamage, t.WearRate,
                    r.Speed, p != 0 ? r.Capacity : r.Coaster.Riders, CoasterMaxCapacity(r.Coaster));
            case RideServiceClass.Ordinary:
            case RideServiceClass.Tour:
                return NativeRideReliability.Wear(t.MinSpeedDamage, t.MinCapacityDamage, t.WearRate,
                    r.Speed, p != 0 ? r.Capacity : r.NativeRiders, t.CapacityParameter);
            default:
                return 0;
        }
    }

    /// <summary>`0x1204D0` (coaster `vt+0x344`): cars/train x seats/car x 6 (coaster-operation.md §3.1).</summary>
    public static int CoasterMaxCapacity(CoasterSim c) => c.Type.CarsPerTrain * c.Type.Seats * 6;

    /// <summary>⭐ `0x117B88` (`vt+0x364`), READ (decompile c4.c, MIPS in track-ride-operation.md §6.2):
    /// <code>
    ///   if ((tick &amp; 3) != 0) return;
    ///   old = rel; rel -= rate(0) &gt;&gt; 5;
    ///   Life(vt+0x2C4) = Life - (old/0xF000 - rel/0xF000);   // signed divides
    ///   if (Life &lt; 0) Life = 0;  if (rel &lt; 0) rel = 0;
    /// </code></summary>
    internal void ApplyWear(ParkRide r, uint tick)
    {
        if ((tick & 3) != 0) return;
        int old = r.Reliability;
        int now = unchecked(old - (WearRate(r, 0) >> 5));
        r.Reliability = now;
        SetLife(r, r.Life - (old / ReliabilityPerLife - now / ReliabilityPerLife));
        if (r.Life < 0) SetLife(r, 0);
        if (r.Reliability < 0) r.Reliability = 0;
        WearEvents++;
    }

    /// <summary>`0x1E1CF0` (`vt+0x2C4`): dropping from 1 or more to 0 or less posts advisor 0x87
    /// `RIDE_CONDEMNED` with the ride attached; the value is stored as s16 either way.</summary>
    void SetLife(ParkRide r, int value)
    {
        if (value < 1 && r.Life >= 1) Advisor?.Invoke(AdvisorCondemned, r);
        r.Life = value;
    }

    // --------------------------------------------------------------------------------------------
    // Status and its enter handlers.

    /// <summary>⭐ `vt+0x1F4` = `0x1E4D70`: store the status, then run the class's ENTER handler --
    /// every time, even when the status is unchanged (which is how a track ride below 10.0 re-empties
    /// its queue every update). The mirror <see cref="ParkRide.DestinationState"/> follows.
    /// <code>
    ///   4  ordinary 0x1B8C28: 0x1164D0 (advisor 0x36, counter 0, sound 0xC/0xE2), queue emptied ONLY if Life &lt; 1
    ///      coaster  0x1229A0: 0x1164D0 + queue emptied;  track 0x2002D8 / tour 0x1E94B8: queue emptied
    ///   5  parent 0x116550: advisor 0x103658 (0x37/0x38/0x39), counter 1, effect bank 2 0x70 at the ride;
    ///      ordinary 0x1B8BF0 also empties the queue
    ///   6  track 0x200310 removes every car; tour 0x1E94F8 every transport (none in the port); else flag only
    ///   7  parent 0x116660: set 2, reliability 100.0, set 10
    /// </code></summary>
    public void SetRideStatus(ParkRide r, int status)
    {
        byte s = unchecked((byte)status);
        if (r.Track != null) r.Track.EnterStatus((TrackRideStatus)s);
        else if (r.Coaster != null) r.Coaster.Status = s;
        r.DestinationState = s;
        var cls = r.ServiceClass;
        switch (s)
        {
            case 4:
                if (cls is RideServiceClass.Ordinary or RideServiceClass.Coaster)
                {
                    Advisor?.Invoke(AdvisorBreakdownImminent, r);       // 0x1164D0: 0x107CA8(msg, 0x36)
                    Count(0, 1);                                         // 0x1073C0(adv, 0, 1)
                    RideSound?.Invoke(r, 0x0C, 0xE2, false);             // 0x111150(audio, 0xC, 0xE2, 0)
                }
                if (cls == RideServiceClass.Ordinary ? r.Life < 1 : cls != RideServiceClass.None)
                    EmptyQueue(r);                                       // 0x117758 / 0x117798(ride, 0)
                break;
            case 5:
                if (cls == RideServiceClass.None) break;
                Advisor?.Invoke(BreakdownMessage?.Invoke() ?? AdvisorNoMechanics, r);  // 0x116550 → 0x103658
                Count(1, 1);                                             // 0x1073C0(adv, 1, 1)
                RideSound?.Invoke(r, 2, 0x70, true);                     // 0x111428(audio, 2, 0x70, pos, 0, 0)
                if (cls == RideServiceClass.Ordinary) EmptyQueue(r);     // 0x1B8BF0 → 0x117758(ride, 0)
                break;
            case 7:
                if (cls == RideServiceClass.None) break;
                SetRideStatus(r, 2);                                     // 0x1E4D08
                r.Reliability = FullReliability;                         // sw 0x64000, 0xE4
                SetRideStatus(r, 10);
                break;
        }
    }

    /// <summary>`0x117798(ride, 0)`: every guest standing in the queue is sent out with event 7, head
    /// first. ⚠ The port's queue is the ride's own list; the guests go to <see cref="ParkRide.Ejected"/>
    /// for <see cref="ParkVisitors"/> to walk out (the opt-in NativeRideQueues keeps its own members and
    /// empties them on its own Broken predicate).</summary>
    internal void EmptyQueue(ParkRide r)
    {
        while (r.TryTakeFromQueue(out int guest)) r.Ejects(guest);
    }

    // --------------------------------------------------------------------------------------------
    // Breakdown checks and the Life check.

    /// <summary>`0x116D68`, the ordinary ride's (via `0x1B8030`, first in its update `0x1B7F80`), READ
    /// (decompile c1.c): the service flag in 4/5; at 0 a 4 becomes 5; ⭐ only STATUS 2 below 10.0 breaks
    /// (after the empty `0x116038`).</summary>
    void OrdinaryBreakdown(ParkRide r)
    {
        if (r.Broken) Service(r, 1);
        if (r.Reliability == 0) { if (r.Status == 4) SetRideStatus(r, 5); }
        else if (r.Status == 2 && r.Reliability < BreakdownBelow) SetRideStatus(r, 4);
    }

    /// <summary>`0x200358`, the track ride's, READ (MIPS `0x20036C..0x2003F4`): ⭐ ANY status below 10.0
    /// is forced to 4, every update -- including 6, so a ride being repaired with 0 &lt; rel &lt; 10.0 goes
    /// back to 4 and re-empties its queue; the repair still completes (§3.4).</summary>
    internal void TrackBreakdown(ParkRide r)
    {
        if (r.Broken) Service(r, 1);
        if (r.Reliability == 0) { if (r.Status == 4) SetRideStatus(r, 5); }
        else if (r.Reliability < BreakdownBelow) SetRideStatus(r, 4);
    }

    /// <summary>`0x1228D0`, the coaster's, READ (MIPS `0x1228D0..0x12299C`): the parent's test widened to
    /// status 2 OR 10. ⚠ At 4 → 5 the console also kicks the player out of the ride-along camera
    /// (`0x122558`, `0x14DAF8(1)`); the port has no ride-along camera.</summary>
    internal void CoasterBreakdown(ParkRide r)
    {
        if (r.Broken) Service(r, 1);
        if (r.Reliability == 0) { if (r.Status == 4) SetRideStatus(r, 5); }
        else if (r.Status is 2 or 10 && r.Reliability < BreakdownBelow) SetRideStatus(r, 4);
    }

    /// <summary>⭐ `0x1169C0`'s tail, after every class's status tick (READ, decompile c1.c):
    /// <code>
    ///   if (Life == 0 &amp;&amp; flag == 0) { 0x118568(ride, 4); if (flag) 0x153D70(ride); if (!broken) set 4; }
    /// </code>
    /// A condemned ride: flag set for good (nothing but a finished mechanic job clears it, and none is
    /// ever dispatched to a Life-0 ride), off the upgrade list, status 4.</summary>
    void LifeCheck(ParkRide r)
    {
        if (!r.ServiceOperated) return;
        if (r.Life != 0 || r.ServiceFlag) return;
        Service(r, 4);
        if (r.ServiceFlag) RemoveUpgrade(r);
        if (!r.Broken) SetRideStatus(r, 4);
    }

    // --------------------------------------------------------------------------------------------
    // The service flag and VAR_BREAKSTAT.

    /// <summary>⭐ `0x118568(ride, kind)`, READ (MIPS `0x1185B0..0x118648`): the flag is ALWAYS set; if
    /// the ride's model has a script host, `0x1FA690(h, bits)` raises the floating status icon (bits 2
    /// for kind 1, 8 for kind 2, 4 for kind 4 -- the icon is INFERRED) and sets the script's
    /// VARIABLE 4 (`VAR_BREAKSTAT`) to 1; kind 4 (condemned) also plays bank 2 event 0x18,
    /// non-positional (`0x111428(…, 2, 0x18, {0,0,0}, 0, 1)`).</summary>
    internal void Service(ParkRide r, int kind)
    {
        if (r.Machine != null)
        {
            if ((kind & 2) != 0) WriteBreakStat(r, 1);                  // bits 8
            else if ((kind & 4) != 0) { WriteBreakStat(r, 1); RideSound?.Invoke(r, 2, 0x18, false); }  // bits 4
            else if ((kind & 1) != 0) WriteBreakStat(r, 1);             // bits 2
        }
        r.ServiceFlag = true;
    }

    /// <summary>`0x118678`: the flag cleared; with a model and host, `0x1FA700` fades the icon out and
    /// sets variable 4 back to 0 -- which is what sends a scripted ride to its "fixed" branch.
    /// ⚠ The model-ready test `(ride+8)->+0x24->vt+0x3C` is taken as passing.</summary>
    internal void ClearService(ParkRide r)
    {
        if (r.Machine != null) WriteBreakStat(r, 0);
        r.ServiceFlag = false;
    }

    /// <summary>`0x1C0E28(inst, 4, v)` on the instance `0x1C0DE8(ctrl+0x34)` finds -- the ride's own
    /// script. ⭐ BY INDEX, as the console writes it: variable 4 is `VAR_BREAKSTAT` in the common set
    /// every ride script declares; a program with fewer than five variables is left alone.</summary>
    static void WriteBreakStat(ParkRide r, int value)
    {
        if (r.Machine?.Program.VariableNames.Count > 4) r.Machine[4] = value;
    }

    /// <summary>A script variable BY INDEX, 0 when the program has fewer (the running test `0x1FA608`
    /// reads index 9 and 5 through `0x1C10D8`).</summary>
    static int ScriptVariable(ParkRide r, int index) =>
        r.Machine?.Program.VariableNames.Count > index ? r.Machine[index] : 0;

    // --------------------------------------------------------------------------------------------
    // The scripted classes' status tick.

    /// <summary>The status tick for a ride the port runs through its script. ⭐ An ORDINARY ride:
    /// breakdown check first (`0x1B7F80` calls `0x1B8030` before the parent), then the parent's status
    /// tick -- ticks 2, 4, 10 and 11 run the boarding pass `0x1166A8` (<see cref="BoardingPass"/>), 5
    /// and 6 are empty (`0x1E5100`/`0x1E5108`: nobody boards or gets off during a repair).
    /// ⚠ Statuses 1 and 3 keep the port's old <see cref="Handshake"/> (natively construction and the
    /// empty closed tick); anything that is not one of the four classes is untouched.
    /// ⚠ A TOUR ride: no native tour class here (see <see cref="ParkRide.ServiceOperated"/>); its
    /// handshake runs as before except that ticks 4/5/6 (which natively only unload, `0x1E96F0` etc.)
    /// offer nobody.</summary>
    void ScriptedStatusTick(ParkRide r, uint tick)
    {
        switch (r.ServiceClass)
        {
            case RideServiceClass.Ordinary:
                OrdinaryBreakdown(r);
                switch (r.Status)
                {
                    case 2: case 4: case 10: case 11: BoardingPass(r, tick); break;
                    case 5: case 6: case 7: break;
                    default: Handshake(r); break;
                }
                return;
            case RideServiceClass.Tour when r.Status is 4 or 5 or 6:
                CollectLeaver(r);
                return;
            default:
                Handshake(r);
                return;
        }
    }

    /// <summary>⭐ `0x1166A8`, the ordinary ride's boarding pass, READ (decompile c4.c), over the port's
    /// handshake (the host offers by writing LETMEON and collects LETMEOFF):
    /// <code>
    ///   offer accepted  → 0x117C90 (riders +1); status != 4 → set 10
    ///   running (var 9 &amp;&amp; var 5, 0x1FA608) → WEAR vt+0x364; status != 4 → set 2
    ///   leaver          → 0x117E08 (riders -1); status != 4 → set 11
    /// </code>
    /// ⭐ The three status writes are what bring a repaired ride (enter 7 ends in 10) back to 2, the
    /// only status its breakdown check `0x116D68` breaks from. ⚠ The console offers only when
    /// `0x1FA528` (ONRIDE vs CAPACITY) says there is room and the head stands in state 0x12; the port
    /// offers whenever LETMEON is free, as <see cref="Handshake"/> always has.</summary>
    void BoardingPass(ParkRide ride, uint tick)
    {
        if (ride.Has("VAR_LETMEON") && ride.Get("VAR_LETMEON") == 0 && ride.TryTakeFromQueue(out int boarding))
        {
            ride.Set("VAR_LETMEON", boarding);
            ride.NativeRiders++;
            if (ride.Status != 4) SetRideStatus(ride, 10);
        }
        if (ScriptVariable(ride, 9) != 0 && ScriptVariable(ride, 5) != 0)
        {
            ApplyWear(ride, tick);
            if (ride.Status != 4) SetRideStatus(ride, 2);
        }
        int leaving = ride.Get("VAR_LETMEOFF");
        if (leaving != 0)
        {
            ride.Leaves(leaving); ride.Set("VAR_LETMEOFF", 0);
            ride.NativeRiders = Math.Max(0, ride.NativeRiders - 1);
            if (ride.Status != 4) SetRideStatus(ride, 11);
        }
    }

    void CollectLeaver(ParkRide ride)
    {
        int leaving = ride.Get("VAR_LETMEOFF");
        if (leaving != 0) { ride.Leaves(leaving); ride.Set("VAR_LETMEOFF", 0); }
    }

    // --------------------------------------------------------------------------------------------
    // Upgrades.

    /// <summary>⭐ `0x116268(ride, silent)`, READ (decompile c1.c, MIPS `0x116268..0x116300`): pending
    /// cleared; if tier &lt; 3: tier + 1, the NEW tier's defaults (<see cref="ApplyTierDefaults"/>), the
    /// NEW tier's cost (record + tier x 0x34 + 0x50) x 10 debited through `0x100698` -- at COMPLETION,
    /// with no check that the park can afford it -- and unless silent, bank 2 event 0xB8 (0xE1 for tier
    /// 3) at the ride.
    /// ⚠⚠ PORT DEVIATION past tier 2: the console's bound admits tier 3 and would read it from past the
    /// record's three tiers (payload 0x20 + 3 x 0x34 = 0xBC: the Extra bytes, then the footprint). The
    /// port does not read past the record: such an install only clears the flag and is counted in
    /// <see cref="UpgradesPastLastTier"/>. The ride screen offers an upgrade only while a tier above
    /// exists, so the Details page never gets there.</summary>
    public void InstallUpgrade(ParkRide r, bool silent)
    {
        r.UpgradePending = false;
        if (r.CurrentTier >= 3) return;                                  // sltiu 3 at 0x116288
        if (r.Definition?.CompiledEntry is not { HasRideTiers: true } e || r.CurrentTier >= 2)
        {
            UpgradesPastLastTier++;
            return;
        }
        r.CurrentTier++;
        ApplyTierDefaults(r);
        Finances.Debit(e.Tier(r.CurrentTier).PurchaseCost * 10);        // 0x100698(fin, cost * 10)
        if (!silent) RideSound?.Invoke(r, 2, r.CurrentTier < 3 ? 0xB8 : 0xE1, true);
    }

    /// <summary>⭐ `0x116120`, READ (MIPS `0x116120..0x116264`), of the ride's CURRENT tier: reliability
    /// 100.0, run timer `+0x122` = 0, capacity = max(1, MaxCap &gt;&gt; 1) (`vt+0x344`: the tier's
    /// CapacityParameter, a coaster's `0x1204D0`), speed = MinSpeed + ((MaxSpeed - MinSpeed) &gt;&gt; 1),
    /// duration = max(1, MaxDuration &gt;&gt; 1). ⚠ The native setters also forward to the ride's script
    /// (`0x118240`/`0x1182A8`/`0x118310`), which the port's settings do not (nor do the laptop's
    /// sliders). ⚠ A track ride's second copy on its sim is reset too.</summary>
    internal static void ApplyTierDefaults(ParkRide r)
    {
        r.Reliability = FullReliability;
        if (r.Definition?.CompiledEntry is not { HasRideTiers: true } e) return;
        var t = e.Tier(r.CurrentTier);
        int max = r.Coaster != null ? CoasterMaxCapacity(r.Coaster) : t.CapacityParameter;
        int capacity = (max >> 1) > 0 ? max >> 1 : 1;
        int speed = t.MinSpeed + ((t.MaxSpeed - t.MinSpeed) >> 1);
        int duration = (t.MaxDuration >> 1) > 0 ? t.MaxDuration >> 1 : 1;
        r.Capacity = capacity; r.Speed = speed; r.Duration = duration;
        if (r.Track != null)
        {
            r.Track.Capacity = capacity; r.Track.Speed = speed; r.Track.Duration = duration;
            r.Track.RunTimer = 0;
        }
    }

    // --------------------------------------------------------------------------------------------
    // Where a mechanic stands.

    /// <summary>⭐ `vt+0x17C` = `0x116EC0` (all four classes), READ (MIPS `0x116EC0..0x117030`): the origin
    /// cell plus the rotated connection A (`0x1E1760`/`0x1E2288`, or (-1,-1) UNROTATED when either
    /// offset is negative, `0x369A30`), then ONE STEP along `d = (rotation + record+0x14) &amp; 3`:
    /// 0 z-1, 1 x-1, 2 z+1, 3 x+1 -- the cell just outside the entrance door, where the port lays the
    /// queue stub (ShopEntrance: `inside + dir == approach`). ⚠ A fixture with no record or placement
    /// uses its <see cref="ParkRide.Entrance"/>, else its origin.</summary>
    public static ParkCell WorkCell(ParkRide r)
    {
        var e = r.Definition?.CompiledEntry;
        if (e == null || r.PlacementTurns == null) return r.Entrance ?? r.Origin;
        var a = e.ConnectionA;
        int rot = r.NativeRotation, w = e.Width, d = e.Depth;
        int x, z;
        if (a.X < 0 || a.Z < 0) (x, z) = (-1, -1);
        else (x, z) = rot switch
        {
            0 => (a.X, a.Z),
            1 => (a.Z, w - 1 - a.X),
            2 => (w - 1 - a.X, d - 1 - a.Z),
            _ => (d - 1 - a.Z, a.X),
        };
        var cell = r.Origin.Offset(x, z);
        return ((rot + a.Direction) & 3) switch
        {
            0 => cell.Offset(0, -1),
            1 => cell.Offset(-1, 0),
            2 => cell.Offset(0, 1),
            _ => cell.Offset(1, 0),
        };
    }
}
