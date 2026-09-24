# Native selection eligibility and relief occupancy: independent follow-up

September24. Static primary-byte trace of the owner's SLES_500.32, loaded only in
memory from LBA262773, MODE2/2352 +24, size2B1160; file1000 -> VA100000.
No assets/executable exported, no gameplay code changed. This is not a console capture.
Read alongside native-destination-score.md, ride-value-producer.md and native-shop-flow.md.

## Final +2DC dispatch (signed adjustment at slot-4)

| Family | Final table | Adjustment | Target |
|---|---|---:|---|
| ordinary |366330|0|1E1E48|
| coaster |35B060|0|120530|
| tour |369F10|0|1E1E48|
| track |36BBF0|0|1E1E48|
| shop |368080|0|1E1E48|
| sideshow |3683B8|0|1E1E48|
| feature |35DC70|0|1E1E48|

Let P be the placed-base pointer. 1E1E48 calls1E1D70, which returns u8[P+9A].
Exactly:

```
baseEligible = state == 2 || state == 10 || state == 11
coasterEligible = baseEligible && u32[P+148] != 0
```

12053C calls the base predicate;120544 loads P+148;120550 word0003100A is
`MOVZ v0,zero,v1`, not an unknown unconditional assignment. It zeros the result
when P+148 is zero. Other six tables have no additional eligibility test here.
No candidate callback receives the guest, reads cash, queue length, LETMEON,
capacity, guest needs, or a live relief-occupant count.

The coaster field is a **track-closure flag**, not guest occupancy: initializer
11FC74 clears it;121D68 adding a piece calls1207F0 to compare its supplied X/Z
against virtual+1B4, and with a nonempty track closes the links via120868.
120890/89C link the endpoints;1208C0 writes1 to P+148. Ordinary piece addition
clears it at121FDC; removal122460 clears it at1224B0, full reset1223D0 at122414.
Save120580 encodes its nonzero value as bit4000 of record+94 (1205E8..614);
restore1207B8..CC routes that saved state through120868. Do not implement
coaster eligibility as merely “open” if track closure is not represented.

## State producers and the Open/Closed/Building adapter boundary

All seven tables' +1F4 setter is1E4D70 (adjust0). It stores the low byte atP+9A
at1E4D88, then invokes a per-state entry callback using jump table369A40.
The state-update table is DIFFERENT:369A70, consumed by1E5138. In particular,
state0 skips a handler; state1 -> virtual+25C, state2 ->+264, state3 ->+26C,
state10 ->+2A4, state11 ->+2AC. Do not assume the two tables share indices.

Decisive transitions:

* Common initializer1E0F30 sets state0 via the setter at1E0F98..A8.
* Placement1E1238 sets state1 at1E1258..6C. Entry1E4CA8 zeros the animation
  accumulator/frame and sets P+90=0. State1's +25C ->1E4F58 advances the
  construction transform/clock; when1E4E88 reports completion,1E5090..A4
  sets state10. Thus construction is ineligible, not merely “script exists”.
* SHOP state10 handler+2A4 ->1D1D48 sets2. FEATURE counterpart1309C8 sets2.
  Native construction therefore reaches a selectable state on this normal path;
  the current port's “start with VAR_RIDECLOSED=1” is a separate adapter choice.
* Paired control callbacks virtual+FC ->1E2738 and+104 ->1E2768 set2 and3
  respectively. The helper1E26D8 toggles2 ->3, any other input ->2. State3 is
  outside the eligible set; the normal Open/Closed adapter corresponds to the
  selectable operating states versus this nonselectable control state.
* Ordinary operating updater1166A8 changes to10 after successful resource
  boarding (11677C..B0), to2 when1FA608 reports RUNNING(index9) and
  ONRIDE(index5) both nonzero (1167E4..838), and to11 on a returned guest
  from1FA418 (11689C..8F0). Therefore busy/running/boarding/alighting does
  NOT by itself make the attraction ineligible. States10/11 are expressly allowed.
* Breakdown transitions116D68 set4 from2 and then5 when its condition reaches0;
  both are excluded. The predicate itself tests native state, not VAR_BROKEN.

This closes the numeric predicate and the construction/operating distinction.
It does NOT prove equality of native state and the managed script variables at
all times, or a complete native lifecycle scheduler. The current checked source
has no independent ParkRide native state: SetOpen only writes VAR_RIDECLOSED,
and Takes additionally requires VAR_LETMEON, an entrance, no fault and !BROKEN.
Those are port transport/safety policies, not the resolved virtual+2DC formula.
For a bounded production adapter, retain explicit lifecycle availability:
Building/Closed/removed/broken => false; normal Open => true, including running
and serving. Treat that collapse as an adapter, with coaster closure separate.
Do not call “Has(VAR_LETMEON)” a native selection gate. If a selected family's
transport is unsupported, report/filter that separately as a port capability
limit; native SHOP and relief guest-layer service do not require that mailbox.

## 1309F8: the alleged “resource statekind2” is APS section5 COUNT == 2

This corrects the unresolved resource-state wording in native-shop-flow.md.
There is no generic resource-class enum read at this point. C=P-8 for FEATURE.
The literal pointer chain at130A28..70 is:

```
renderObject = u32[C+10]
index        = u32[renderObject+14]
resource     = u32[2EAAD0 + 4*index]
aps          = u32[resource+18]
sectionTable = u32[aps+20]
count        = u32[sectionTable+28]   // +0x28 == 5 * 8
special      = count == 2
```

Proof of type, not inferred from value2:

*1F7ED8 appends the APS extension via string36ABC8 at1F8194..9C, then
 calls167110 at1F81A0. This checks magic185AA030 and version148 at167190..BC,
 calls relocator167250, and returns the APS base.
*1F81B4 stores that return at stack+238, offset18 of the temporary resource
 starting atstack+220. The copy1F827C..833C preserves that resource field.
*Resource instancing1F6230 copies that resource header (1F6334..63D8), including
 +18;1F8C18 publishes the instance in2EAAD0 (1F8D18..30).
*167264..70 relocates APS+20;167284 reads section-count BYTE at+1C, and
 167298/2A4 walks its descriptors with stride8.1672D8 reads descriptor+4 as
 record-array pointer. Independently,1F95F8 indexes the SAME chain by
 section*8 and variant*1C. Thus descriptor+0 is record count.
*1FAD40 repeats the exact section5 count==2 check and requests animation
 slot5 variant1 on entry (1FAD78..88), variant0 on release (1FAD9C..AC).

1309F8 first calls FEATURE virtual+134: adjust-8,target130718, which returns
compiled payload+2E bit0 (same classification as relief callback130780).
Its complete result is:

```
if !compiledReliefBit: return 1
if renderObject == null || index == 0 || resource == null || aps == null: return 1
if APS.section5.count != 2: return 1
return u32[C+AC] XOR 1
```

Native does not check section count or a null descriptor-table pointer before
that raw table access. A safe parser should reject malformed assets rather than
pretend their unchecked native behavior is established. With normal boolean AC,
this is available iff unoccupied, ONLY for the exact two-variant slot5 case.
Counts0/1/3 are not equivalent to2. A missing resource/APS follows the available
branch, not an automatic reservation. For malformed AC=2, literal XOR returns3
(nonzero), NOT a generic `occupancy == 0`; the supported lifecycle writes0/1.
Initializer13045C clears AC; arrival20D79C sets1 and completion20F038 clears0.

Direct callers found in executable code are20C340 (score) and20D6F0 (arrival).
For kind2, score requires BOTH virtual+1DC !=0 AND this availability !=0;
failure returns-1. The same test is repeated on arrival before service begins.
Do not reserve the feature just because it was selected or someone is walking
there. Non-special relief still writes AC during native service, but1309F8
ignores it: “any relief guest busy => candidate unavailable” is not this code.

## Proposed production boundary and discriminating checks

Use the actual identity-bound APS supplied to ParkSim.Add. The existing
`Animation.Sections()` already exposes `(Count,Offset)`; marker is
`sections.Count > 5 && sections[5].Count == 2`, NOT APS variant flags, current
animation slot, an RSE variable, capacity, indoors, or SAM ProvidesRelief alone.
`RsePreviewHost` privately groups records by slot but currently exposes no
section-count metadata; retain a derived marker on the placed instance when
loading, or expose validated metadata. `AssetResourceDatabase.RawFeatureFlags`
supplies the separate relief bit. Do not put an APS-derived fact into the DBA
flags or infer the marker from a facility name.

The production boundary needs native-state eligibility, coaster closure when
supported, compiled relief classification, the APS marker, and actual service
occupancy as separate inputs. Occupancy changes at service entry/completion,
not queue offer/LETMEON or destination selection. Lifetime cancellation cleanup
may be a necessary conservative port policy; do not label it a traced native
branch. Multiple occupants for non-special relief must not be prohibited solely
by this selector predicate.

Controls: state1/3/4/5 false;2/10/11 true for the common callback. Coaster flag0
rejects even state2; flag1 preserves the base result. Relief AC1 + APS count2
rejects; AC1 + count1 or3 remains available. AC0 + count2 accepts. Missing APS
returns available. Non-relief kind2 still fails the separate relief-support test.
No-LETMEON alone changes none of these native results. A relief guest already
heading to the feature must encounter the repeated availability check on arrival.
