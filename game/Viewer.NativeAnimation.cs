using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

/// <summary>
/// OPT-IN, and inside the experimental entrance only: the native logical-animation dispatcher
/// drives the guests the entrance flow owns (findings/native-guest-animation-readiness.md).
///
/// The readiness predicate 191E10 replaces the entrance experiment's `ready=true` bypass, so a
/// guest whose request is 13 does not step until its model has actually COMMITTED 9 or 13 at a
/// playback boundary. The dispatcher's (section, variant) is what gets drawn, so the wait on
/// screen and the wait in the simulation are the same record.
///
/// Requests come from the decoded producers only:
/// - 11 at activation (20BCD0 writes N+38 unconditionally at 20BD34);
/// - 13 at every route-slot advance (191D78 with a1=0, reached from 20D628);
/// - 2106E8's idle picks [14,5,6,13] while the flow holds the guest in state 0B (awaiting a
///   route result): 1920D0 dispatches execution state 0B through guest vtable +164.
/// The entrance modes' completion handlers 20DB8C/20DC1C/20DC28/20DC70 write no request.
///
/// ⚠ PORT ADAPTERS, each labelled rather than guessed away:
/// - ONE model update per 40 ms park tick, after the flow's guest steps and the request push.
///   The native per-frame order and cadence of 1ACFC0 against the guest update were not found.
///   Cadence matters here: a model in F re-rolls at every update.
/// - Every owned guest is pushed. The native pushes only a shown guest (B+2C bit 0, set via
///   192C10), whose producer for bus-born guests was not traced.
/// - The random stream is NewlibRand, the same LCG as 29CF08, not the console's shared stream.
/// - Section 0 is a baked-vertex walk (1A7E18) that AnimatedModel cannot play. It is DRAWN
///   with the section-1 skeletal walk, which is the same 16 frames and a measured-equivalent
///   stride. Its TIMING is still section 0's own record.
/// - 1C4930's update counter is taken to be the park tick, for the N+2C stamp and 2106E8's
///   120-update hold. The picker's draws come from the flow's 1448E0 stream (_guestRng).
///
/// `--native-idle-all` (queue item 5, research only) extends the DRAWING to every ordinary guest,
/// so strawberry can compare it with the id%6 idles. An ordinary walker requests 13 while walking
/// and 11 when it arrives (20D628's writes at 20DAE8/20DB10/20DB34). Logical 11 is the weighted
/// idle: 93 of 99 weight holds the last pose. ⚠ Adapter: ordinary walkers are NOT gated on 191E10,
/// because the legacy walker moves them; only what is drawn follows the dispatcher.
/// </summary>
public partial class Viewer
{
    bool _nativeGuestAnimation;
    bool NativeGuestAnimationRequested => _nativeGuestAnimation
        || ResearchFlags.Value.Contains("--native-guest-animation");
    NativeLogicalAnimationTable _logicalAnimations;
    readonly NewlibRand _nativeAnimationRand = new();
    readonly Dictionary<Guest, NativeGuestAnimation> _nativeAnimations = new(ReferenceEqualityComparer.Instance);
    // What NativeDrawnRecord last handed AnimatedModel, per guest id: re-seed only on change.
    readonly Dictionary<int, Aps.Record> _nativeDrawn = new();
    public int NativeAnimationWaits { get; private set; }
    bool _nativeIdleAll;
    bool NativeIdleAllActive => NativeAnimationActive
        && (_nativeIdleAll |= ResearchFlags.Value.Contains("--native-idle-all"));

    bool NativeAnimationActive => _nativeGuestAnimation |= NativeGuestAnimationRequested;

    NativeGuestAnimation NativeAnimation(Guest guest)
    {
        if (_nativeAnimations.TryGetValue(guest, out var existing)) return existing;
        _logicalAnimations ??= NativeLogicalAnimationTable.Read(_lib.Disc);
        var own = OwnAnimation(guest.Id);
        // 1ACFC0 validates against the MODEL's own animation set (17D7C0/17D7C8 counts), so
        // durations come from the guest's own .aps even when its tracks are drawn from Boy1a's.
        var lengths = new Dictionary<(int, int), int>();
        if (own != null)
            foreach (var section in own.Records().GroupBy(r => r.Slot))
            {
                int variant = 0;
                foreach (var record in section) lengths[(section.Key, variant++)] = record.DurationFrames;
            }
        var created = new NativeGuestAnimation(_logicalAnimations,
            (s, v) => lengths.TryGetValue((s, v), out int n) ? n : null) { Requested = 11, Stamp = _parkTicks };
        _nativeAnimations[guest] = created;
        return created;
    }

    Aps OwnAnimation(int id)
    {
        string model = GuestModels[(id - 1) % GuestModels.Length];
        string path = System.IO.Path.ChangeExtension(model, ".aps");
        if (_charAnims.TryGetValue(path, out var aps)) return aps;
        // The flow can ask before MakeActor has loaded this rig. Load it the same way (SittingRecord
        // caches the own file under this path); an empty duration table would leave the model in F
        // forever and a 13 request would never be permitted -- a port-made deadlock.
        if (_charLib == null) { _charLib = new AssetLibrary(_discPath); _charLib.OpenWad("/DATA/DATA.WAD"); }
        SittingRecord(model);
        return _charAnims.TryGetValue(path, out aps) ? aps : null;
    }

    /// <summary>191E10 for an entrance-owned guest: its model is its actor, never "no visual".</summary>
    bool NativeEntranceReady(Guest guest)
    {
        if (!NativeAnimationActive) return true; // the experiment's documented bypass
        var animation = NativeAnimation(guest);
        bool ready = animation.PermitsMovement;
        if (!ready) NativeAnimationWaits++;
        return ready;
    }

    void NativeSlotAdvanced(Guest guest)
    {
        if (!NativeAnimationActive) return;
        var animation = NativeAnimation(guest);
        animation.Requested = (animation.Requested & ~0x1f) | 13; // 191D78, a1 = 0
    }

    /// <summary>After the flow's tick: push each owned guest's request (1921D0), advance its model
    /// by one park tick (1ACFC0), and hand back guests the flow no longer owns.</summary>
    void TickNativeAnimations()
    {
        if (!NativeAnimationActive || _entranceFlow == null || _entranceWalk == null) return;
        bool all = NativeIdleAllActive;
        foreach (var guest in _entranceWalk.Guests)
        {
            bool owned = _entranceFlow.Owns(guest);
            if (!owned && !all) continue;
            var animation = NativeAnimation(guest);
            if (owned && _entranceFlow.StateOf(guest) == NativeEntranceFlow.State.Pending)
                animation.IdlePick(_parkTicks, n => _guestRng.Next(n), _logicalAnimations.IdleStates);
            if (!owned) OrdinaryRequest(guest, animation);
            animation.Push();
            animation.Update(ParkSim.TickMilliseconds, _nativeAnimationRand.Next);
        }
        foreach (var gone in _nativeAnimations.Keys
                     .Where(g => _entranceFlow?.Owns(g) != true && !(all && _entranceWalk.IsLive(g))).ToArray())
            ReleaseNativeAnimation(gone);
    }

    /// <summary>--native-idle-all with NO entrance flow, e.g. cow tools' `--idle-scene`, where ParkVisitors
    /// is suppressed and so the flow never exists. Called once per park tick from TickNativeBus, after
    /// the guests have stepped. Every guest is ordinary here.</summary>
    void TickNativeAnimationsWithoutFlow()
    {
        if (_entranceFlow != null || _guests == null || !NativeIdleAllActive) return;
        foreach (var guest in _guests.Guests)
        {
            var animation = NativeAnimation(guest);
            OrdinaryRequest(guest, animation);
            animation.Push();
            animation.Update(ParkSim.TickMilliseconds, _nativeAnimationRand.Next);
        }
        foreach (var gone in _nativeAnimations.Keys.Where(g => !_guests.IsLive(g)).ToArray())
            ReleaseNativeAnimation(gone);
    }

    /// <summary>--native-idle-all only: an ordinary walker's logical request, written on the change.
    /// Walking -> 13 (as each route advance does); stopped -> 11 (20D628's completion writes).
    /// Seated and riding guests never reach the Gait hook, so their state does not matter here.</summary>
    void OrdinaryRequest(Guest guest, NativeGuestAnimation animation)
    {
        int want = guest.State == GuestState.Walking ? 13 : 11;
        if ((animation.Requested & 0x1f) == want) return;
        animation.Requested = (animation.Requested & ~0x1f) | want;
        if (want == 11) animation.Stamp = _parkTicks;
    }

    void ReleaseNativeAnimation(Guest guest)
    {
        _nativeAnimations.Remove(guest);
        // cow tools: without this, Gait() finds its old record still "playing" and resumes it at
        // (elapsed + owned ticks) % duration -- a phase pop on handback. Re-seed instead.
        _nativeDrawn.Remove(guest.Id); _gaitRec.Remove(guest.Id); _gaitFrom.Remove(guest.Id);
    }

    void ResetNativeAnimations()
    {
        foreach (var guest in _nativeAnimations.Keys.ToArray()) ReleaseNativeAnimation(guest);
        _nativeDrawn.Clear();
        NativeAnimationWaits = 0;
    }

    /// <summary>Gait hook: true when this guest's pose was drawn from the dispatcher.</summary>
    bool NativeDrawnRecord(int id, float alpha)
    {
        if (_nativeAnimations.Count == 0 || !NativeAnimationActive) return false; // cheap test first: per guest per frame
        NativeGuestAnimation animation = null;
        foreach (var (guest, a) in _nativeAnimations)
            if (guest.Id == id) { animation = a; break; }
        if (animation == null || !_drawn.TryGetValue(id, out var d) || !_walkRec.TryGetValue(id, out var w)) return false;

        var (slot, variant, frame) = animation.Held is { } held
            ? (held.Slot, held.Variant, (float)animation.Duration)
            : (animation.Slot, animation.Variant,
               animation.Frame + alpha * ParkSim.TickMilliseconds * NativeGuestAnimation.FramesPerSecond / 1000f);
        // Inactive with nothing held: this model has never been posed by the dispatcher (a fresh
        // guest still in F). Leave the actor as constructed rather than invent a pose.
        if (slot == NativeAnimationDescriptor.Inactive) return true;
        var record = DrawableRecord(w.Anim, slot, variant);
        if (record == null) return true; // a record this rig cannot draw: leave the last pose
        if (!_nativeDrawn.TryGetValue(id, out var shown) || shown != record)
        {
            d.Drawn.UseRecord(record);
            _nativeDrawn[id] = record;
        }
        d.Drawn.SetFrame(Math.Min(frame, Math.Max(0, record.DurationFrames - 0.0001f)));
        return true;
    }

    static Aps.Record DrawableRecord(Aps anim, int slot, int variant)
    {
        if (anim == null) return null;
        // Section 0 is the baked-vertex walk; draw its skeletal equivalent (see summary).
        int drawSlot = slot == 0 ? 1 : slot, drawVariant = slot == 0 ? 0 : variant;
        var record = anim.Records().Where(r => r.Slot == drawSlot).Skip(drawVariant).FirstOrDefault();
        return record is { Skeletal: true, Shared: false } ? record : null;
    }
}
