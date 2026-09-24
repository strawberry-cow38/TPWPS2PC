# Bus native lifecycle: caller guards, visibility, and two clocks

September24 2026. Owner-disc primary MIPS read in memory; authorized partial C
corpus consulted outside Git. No executable/assets committed or extracted here.
The implementation is a controller plus real model adapter on the research branch;
park population/traffic/audio integration is NOT complete.

## Retraction: state0 does not enter the section12 cleanup handler

The earlier animation/visual notes correctly decoded the *lower-level* special12
handler, but omitted the bus wrapper's guard. That omission changed the claimed
behavior. Type15 is dispatched to17C788 by17C5D8. At17C7B0..E0:

1. 17D7C0 returns the APS section count (byte+1C).
2. unsigned requestedSection < count must hold.
3. requestedRecord < selected section's record count must also hold.
4. Only then does17C814 call1ABC80.

All eight Bus1/Bus2 APS files have12sections (0..11). Native state0 requests12,
so it is **rejected by the wrapper**. The downstream section12 handler is not
reached through this bus call. State0's sound stop remains independently reachable.
A new channel starts inactive/section12; rejecting the initial request does not
create a problem. After a completed departure, the same rejection leaves section5
record2 in end-hold, including its last visibility and pose. The next accepted5:0
request is what activates a new record. Do not restore the source pose at state0.

Parent checked raw instructions in /tmp/tpw-bus-command-guards.log. Regression:
turning the rejected command into a playback reset fails the actual lifecycle test.
This is the same class of reachability error as the earlier guest flush: finding a
callee's code is insufficient evidence the selected caller can execute it.

## Native visibility is not indiscriminate ancestor hiding

All eight resources have these five nodes:

|Node|Flags|Parent|Role|
|---|---|---|---|
|0|201|4|body mesh|
|1|201|0|wheel mesh|
|2|201|0|wheel mesh|
|3|80000250|0|leaf helper|
|4|80000058|none|spline/root helper|

The body parent is node4, not node3. Node3 happens to be the helper included in
visibility tracks; conflating those two would hide the entire bus forever.

Instantiation1F6230→1F5F28→16A8E0 copies meshCount*A0 and helperCount*60 through
29C1BC; node+0 flag words survive. Header flag additions at16AA14..3C are MODEL
flags, not clearing the node's authored10.

Draw2280E0, reached by2282E0, tests **node flags&8050** at22810C..38 to skip drawing
that node. Separately,228140..84 tests **flags&20** to prune child traversal. Next
sibling is+8, first child+C. The independent228DF0 traversal has the same split.
Therefore root helper4's authored10 does not hide the body, and body10 alone does
not hide its wheels. Parent rechecked the decisive raw tail; see
/tmp/tpw-bus-parent-clock-visibility.log.

The bus explicitly keys all three mesh nodes:
- 5:0: nodes0..3 timelines[0,+2]; hide on first sample, reveal from frame2.
- 5:1: no visibility keys; retain current state.
- 5:2: nodes0..3 timelines[-218]; hide from frame218.
- node4 is unlisted throughout.

Ordinary accepted record activation is also a cleanup site:1AB938..958 calls
1AA460(old,new) when old section exists and incomingflags&C==0. Cleanup clears10
on OLD-LISTED unprotected nodes, preserving protected80000000 nodes and unlisted
state. Special12 uses(old,null), but its bus wrapper call is rejected as above.
The old blanket claim “every record switch retains all visibility” is too broad.

`AnimatedModel` now has an explicit native-node visibility mode and bounded
`ActivateNativeRecord` API (ordinary morph records, no index-list/skeletal cleanup).
Existing RSE binding semantics are not silently changed. `NativeBus` uses that
mode: initial helper10 is loaded, self-hide and subtree-prune are distinct, and
ordinary activation refreshes visibility without inventing a frame-zero evaluation.
The API is deliberately narrower than a complete native animation player.

## Animation milliseconds are hardware elapsed, not executed sim ticks

Positive producer chain, independent of countdown397640:
- 21B038..50 registers225128 as timer interrupt handler9.
- 21B068..84 programs timer0 modeDC2,target1680 (5760). The nearby diagnostic
  names compare5760/frequency100. The initialized route is nominal100Hz.
- 225168..180 increments64-bit310CA8 by one;225190 acknowledges modeFC2.
- 220C78 samples310CA8, differences previous2F07C0, multiplies by10, and ALWAYS
  updates that previous snapshot.
- 2F0798 always accumulates those milliseconds. 2F07A8 accumulates only when
  gate2F07B8 is nonzero. Gate starts1;220C68 writes it.
- 11E71C/740 disable/enable;1C55F8/1C56C8 also operate the gate.
- 225FD4 pumps220C78 before the manager's guard. 2312FC calls225FC8 AFTER the
  configured game-update loop2312DC..F4, not once per iteration of that loop.
- 147158/147140 return the low32 clock bits;1A8AFC..B18 caches those in2E71B8/BC.

Parent rechecked220C78's raw loads/stores. Word00C3182F at220C98 is DSUBU even
though the small local disassembler prints UNKNOWN. No new clock semantics were
inferred from that missing decoder label.

The bus uses flags0/speed1, so the gated clock gives unsigned elapsed*30/1000
frames. Ordinary initialized bus instance flags&18==0: factory0x32F is NOT the
instance flags word; type15 catalog options produce template200000 and factory
options add4/4000. Model-header40 would add instance10, but all eight bus MPS
headers areA1 (40clear). This establishes the ordinary initialization route,
not absence of every possible later flag mutation.

The ordinary endpoint is strictly duration<frame;1AC298's special14 holds at the
authored duration, not the overshoot frame. Its selected section remains5.
A stall accumulates hardware elapsed time, unlike ParkSim's capped catch-up clock.
`NativeBusController` therefore takes injected active milliseconds and separate
signed countdown delta. It does not quietly substitute Sim.Time for both.
Pause-edge sampling granularity and unusual timer reconfiguration remain bounds;
220C68 itself does not sample the hardware timer at the instant of a gate change.

## Controller contract and consumer evidence

`NativeBusController` uses the actual APS durations, wrapper bounds and phases0..3.
Creation invokes state0. Each update samples animation FIRST, then spends outer or
dwell countdown (crossing zero still skips state work), then applies the cached
state command. The state1 traffic check is AFTER that command. State2 completion
requests a batch at point0 only if open && specialObjectAbsent && flaggedCount<30;
it always sets dwell50000. Wrapping afterstate3 sets outerC8000. No RSE statuses,
new arrival interval, inferred batch size, or general animation queue is supplied.
Under this controller, every new record follows inactive/end-hold; the general
pending-request branch is unreachable without an external state intervention.

`NativeBusAudit` instantiates the real game adapter and all eight resources:
976 checks cover the full first visit and restart, real surfaces, traffic block,
strict/end-clamped frames,20/50countdown boundaries, all release gates and the
29/30control, unchanged clock, signed delta, unsigned wrap, source helper visibility,
subtree-pruning contrast, and section12 rejection preserving hidden departure pose.
Synthetic duration3 makes exact equality reachable at100ms, with101ms completing.

Six independent mutations fail: premature equality completion; invented per-call
frame clock; treating special12 as reachable; inherited10 hiding; skipped ordinary
transition cleanup; hidden-pose freezing. Originals restored and rebuilt. These tests do NOT establish
park integration or audio overlap timing; the adapter's batch callback is still a
request boundary, not actual guest admission.


A final consumer check caught another lifecycle seam: hiding a native node must
not freeze its transform at the last VISIBLE frame. Otherwise an ordinary record
activation can reveal that stale pose before the next animation sample. Native
mode now updates hidden geometry/poses too; existing adapters retain their prior
skip policy. The test compares hidden endpoint surfaces with the evaluated endpoint
world matrix, and deliberately restoring the skip makes it fail.

Merged regression evidence at peer main14db25b:
- /tmp/tpw-native-bus-runtime-final: all10 scenes PASS; bus968 and modelpath1574
  before the last hidden-pose control expansion.
- /tmp/tpw-native-bus-reveal-audit.log: expanded native bus976 PASS afterward.
- /tmp/tpw-native-bus-matrix-final: JUNGLE/FANTASY PASS, exact two known retail reds.
- /tmp/tpw-native-bus-python-final.log:63 runner tests PASS.
- /tmp/tpw-native-bus-mutation-*.log: all six mutations fail and are restored.
No screenshot or listening approval is asserted by these headless checks.
