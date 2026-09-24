# Ride-value producer trace: family identity matters

Evidence from the owner's SLES_500.32 image, read in memory from the disc.
**September24 follow-up below resolves the concrete ordinary/feature/track/tour/coaster
final tables and callbacks.** The earlier unresolved base-table discussion records why
that additional trace was necessary; it is no longer the latest producer boundary.
No runtime producer is wired by this finding. It specifically prevents replacing the
chosen global45 with a plausible-looking SAM field and calling that retail parity.

## Shared consumer dispatch

FUN_0020EDD8 obtains the placed-object pointer from guest+0x28 at0x20F1AC, reads its
vtable pointer at object+0x10, then loads the signed adjustment at table+0x1D0 and the
function pointer at+0x1D4. The jalr at0x20F1BC applies the adjustment in its delay slot;
0x20F1C4 retains the returned value. The adjustment/function pair is not an ordinary
unadjusted function-pointer array.

Table0x35A560 has adjustment0/target0x1E5A98 at that slot; the target returns zero.
The component constructor0x1188B0 installs that table (store0x1188E8); the later routine
0x118928 also installs it. This is **not yet a proof that a concrete ordinary ride's
final dynamic table is this one**. A base-constructor table is insufficient when a later
constructor can replace it. The narrow next ordinary-family probe is a known kind3
asset's creation path through its final installed table, then only this virtual slot.

## Corrected identity: table0x3683B8 is sideshow-backed

Our first read mislabeled this as an ordinary-ride implementation and misread the
positive-square shift as8. Both claims are retracted. The resource initializer and
existing DBA consumer evidence identify it as sideshow-backed, and raw instruction
0x00028103 at0x1D2AD0 has shift amount**4**. Do not propagate the first interpretation.

The table entry at0x368588/58C contains adjustment-8 and target0x1D2A68. The callback
receives the complete object, eight bytes before the base subobject held by the guest.
It obtains a resource pointer through slots+0x58/+0x5C and0x1E1420 →0x10FA30 →0x10F0B0
→0x10F248, and reads payload+0x18 at0x1D2A98. The resource database global is0x2AADFC.

The initializer0x1D21F0 is itself indirectly dispatched by table+0x150/+0x154, with
adjustment-8. Stores are relative to the complete object:

| Instance field | Initial resource field | Evidence |
|---|---|---|
| +A8 word | payload+2E u16, prize value | load0x1D2388, setter call0x1D238C, store0x1D2A54 |
| +CC u16 | payload+2C u16, initial price | call0x1D2374/load delay0x1D2378, store0x1D2A2C |
| +CE u16 | payload+30 u8, win percentage | load0x1D237C/call0x1D2380, store0x1D2A3C |

These match AssetResourceDatabase.Sideshow and the previously documented charging,
prize and win-test consumers in dba.md. Repeated offsets in a different facility class
are **not evidence** that these fields mean service time/condition. The service-decay
hypothesis suggested during the first trace is not supported by this object mapping.

For normal input ranges the callback uses d=prize−price; its term is d when nonpositive,
otherwise the arithmetic-right-shift-by4 of the low-word square. It adds floor(win/3)+50,
then selects against the payload base and100. The observed branch ordering returns the
base for a negative candidate, max(base,candidate) below101, otherwise100. Do not silently
replace this with an all-input clamp if a mod supplies a base outside0..100.

These are mutable values, not necessarily permanent initial metadata: the save/restore
pair0x1D2410/0x1D24C0 writes/reads record+0C/+0E/+18; control-update0x1D8118 also writes
the setters, with distinct zero-prize behavior. Control internals/scheduling are not yet
fully traced. Direct-call absence does not rule out virtual writers.

## Authored, compiled and computed are three separate layers

A fresh all-region DBA golden audit independently confirms ARC2X3 key247:
price10, prize30, win percentage33, and on-disc base-excitement0 in EUR/USA/JAP.
Its named key is STR_GRAPHICS_JUNGLE_SIDESHOW_ARC2X3_ARC2X3. Thus absence of a prize
property in its old SAM does not make the resource initializer's input unknowable, and
the SAM's InitChanceOfLoosing75 is not a replacement for the compiled win percentage.
If those loaded values are unchanged by other initialization/control paths, the traced
sideshow calculation gives86. That is a conditional initial-state calculation, **not an
observation of a live console callback** or proof it is used on every guest exit path.

Coaster, tour and track families have other overrides. Their complete formulas and the
ordinary family's final dynamic dispatch remain open. Do not apply the sideshow formula
to them or infer the full producer from the semantics/range of ExcitementLevel.

## Priority adjustment

A proven authored/compiled mismatch in shop effects is a more immediate bounded delivery
than further unbounded dispatch research. Peer owns a runtime compiled-data join; independent
validation must use full symbolic identities and explicit regions, not equivalent effect
tuples. Named expectations: Balloon239, VampShop153, FatFairy66 and Droid388 all have compiled
happiness10 in all three DBAs. Droid's price/cost is50/35, others45/30. Ice-cream keys245,
151,69,391 retain their real EUR/JAP versus USA hunger/vomit differences.

Even compiled fields are not always final outcomes: the documented happiness consumer at
0x20E450 scales the base by shop quality. Test the join and the port's explicit default
separately; do not claim raw base10 proves every live console purchase pays10.


## Final-family follow-up: pool construction closes the base-vtable gap

Static code/dataflow, not a live-console capture. Parent independently reread paired
slots and decisive stores/arithmetic words from the original hashed image. No assets
or executable extracted. P denotes the placed-base pointer, C the complete object.
For ordinary/feature/tour/track C=P-8; for the traced coaster pool object C=P.

| Kind | Final table | +1D4 ride value (adjust,target) | +1DC relief | +1E4/+1EC needs |
|---|---|---|---|---|
|3 ordinary |366330 |-8,1B82D0 |0,116030 ->0 |0,118A78 /0,118A80 ->0 |
|1 coaster |35B060 |0,1227D8 |0,116030 ->0 |same zero pair |
|7 tour |369F10 |-8,1EA038 |0,116030 ->0 |same zero pair |
|6 track |36BBF0 |-8,202188 |0,116030 ->0 |same zero pair |
|2 feature |35DC70 |0,1E5A98 ->0 |-8,130780 |0,1E5AA8 /0,1E5AB0 ->0 |

The adjustment is the signed halfword four bytes before the function pointer.
FFF8 is-8, not a large positive displacement. Zero targets explicitly setv0=0 in
JR RA delay slots. This is not a census of adjacent plausible-looking tables.

### Concrete construction/allocation provenance

Compiled family dispatcher12B1B8 uses jump table35C320 indexed kind-1. Kind1/2/3/6/7
select category key lists at+68/+80/+08/+38/+20 respectively. Matching pool allocators
and final initializer slots establish these concrete families:

| Family | Allocator | Pool global | Final +154 initializer | Key lookup |
|---|---|---|---|---|
| ordinary |149FF0 |39528C |-8,1B7A98 |1B7AD0->12B360 (+08) |
| feature |14A620 |3952A0 |-8,1302D8 |130310->12B478 (+80) |
| coaster |14A4A0 |395298 |0,11FAE0 |11FB2C->12B440 (+68) |
| tour |14A0B8 |395290 |-8,1E8F28 |1E8F60->12B398 (+20) |
| track |14A180 |395294 |-8,1FFDA8 |1FFDE0->12B3D0 (+38) |

Ordinary standalone constructor1B8EE0 installs base35A560 at1B8F24, then replaces
that same field with366330 at1B8F58. Actual pool construction147EB0 does likewise:
147F38 stores base,147F68 stores final366330. At the final store s0=C+110 and the
instruction is `AE15FF08` (SW s5,-F8(s0)), i.e.C+18. Pool stride140, pointer published
147FDC. Allocator149FF0 dispatches initializer at14A080..8C, then writes kind3 via
1E1D60. Placement126964 reaches that allocator after ordinary key lookup12694C.

Feature pool calls base1E0E60 at14848C and stores final35DC70 atC+18 at148494,
strideC0; publishes3952A0 at148504. Allocation14A620 dispatches final initializer
and writes kind2 at14A6C4/C8. Placement126F34 reaches it after feature key126F1C.
Standalone130CB8 independently installs35DC70.

Coaster pool replaces base35A560 (148208) with35B060 at148248. Tour replaces its
base with369F10 at148074. Track pool calls154DA0 at14813C: base35A560 store154E04,
final36BBF0 store154E3C atC+18. Later36B670 stores belong to embedded track pieces,
not replacements of the attraction table. Final shared+15C initializer1E0F30 sets
placement/kind/index and does not overwrite these final tables.

This proves the statically reachable pool-created families; it does not claim every
other table is unreachable or that a particular instance was observed on-console.

### Actual computed ride values (ordinary/tour/coaster/track)

All read signed compiled baseB atpayload+18 through resource accessor+5C->1E1420,
not the SAM text. Operating getters resolve in all four tables as:

*+2F4 ->118398 (adjust0), returns signedP+E8: speedS.
*+304 ->1183E8 (adjust0), returns signedP+F0: durationD.

For C=P-8, those are C+F0 and C+F8. Do not confuse complete/base-relative offsets.
Define `Q(x)=clamp(x,0xC00,0x1400)` and preserve signed32 low-word arithmetic:

```
A = Q(trunc((S << 12)/100))
Z = Q(trunc((D << 12)/5))       // ordinary, tour, track
Z = Q(D << 12)                // coaster: NO /5
M = low32(A*Z) >> 12
value = minSigned(100, low32(B_effective*M) >> 12)
```

Right shifts are arithmetic; there is no final lower clamp. Ordinary/coaster/track
return0 early when B==0. Tour runs arithmetic even then. For ordinary/tour/coaster,
B_effective=B. Track adds `(u8[C+1D1] >>1)`, AFTER the base-zero early return.

Evidence: ordinary1B8300 loadsB,1B8318/1C early-out,1B8354 /100,1B837C /5,
1B83A4/A8 multiplier product/shift,1B83AC/B0 base product/shift,1B83B4/B8 upper100.
Coaster122804 load,12281C/20 early-out,122854..58 /100,12285C direct duration shift,
12289C..B0 products/cap. Tour1EA070 load,1EA0B4/B8 /100,1EA0BC..EC /5,
1EA118..130 products/cap. Track2021BC load,2021D4/D8 early-out,2021E0 bonus byte,
2021E8 halve,2021F8 add,202270..284 products/cap.

Conditional hand-computable example: Belly Bounce compiled tier0 B40, speedbounds1..100,
maxDuration60 -> initialS50,D30 ->A3072,Z5120,M3840 ->value37. Not raw40 and not
port-global45. This is a conditional prediction of that initial state, not a live
callback observation or a guarantee that later settings remain unchanged.

### Live settings, initializers, and bridge boundary

Setters+30C->118378 and+31C->1183C8 write P+E8/P+F0 at118384/1183D4. Control
updates1D5124..138 and1D5170..184 call them. Restore116BA8 reads speedu16 record+8A
and durationu8 record+8D and calls setters116C18/116C48; save116AA0 writes those at
116AEC/116AFC. These are mutable operating settings, not permanent base-excitement data.

Default setter116120 computes:
`speed=minSpeed+((maxSpeed-minSpeed)>>1)` and `duration=max(1,maxDuration>>1)`.
Tier getters use currentbyteP+126 and34-byte stride (minSpeed payload+38+34*tier,
maxDuration payload+44+34*tier; see1178B0..D0,1179D0..F0).

Parent follow-up on forwarding: speed setter calls118240; it reads P+E8 then routes
through resource wrapper1FA818 ->1C0DE8 ->1C1068. Duration118310 reads P+F0 and
routes through1FA858 ->1C0DE8 ->1C0E28 with argumentindex3. The exact speed scaling/
script-variable binding at the final bridge is not established here. Current managed
ParkSim starts VAR_DURATION=1; do NOT assume that it represents the same operating
setting as the native defaultD. Resolve this before treating current script variables
as faithful score/ride-effect inputs or overwriting them en masse.

Track byteC+1D1 is cached runtime state. Recompute200C20 clears it, reads piececount
C+26F8, iterates stride108 fromC+1D8 and calls1FE9B0 (signedpiecebyte+EC). Table36BB20
weights rawcodes20..23/40..51 by4,24..31/36..39 by2, all others by1. SB at200CB0
wraps accumulation modulo256. Init1FFF3C clears it; caller200BF8 triggers recompute.
Do not substitute an unbounded piece count or silently ignore its update lifecycle.

### Relief support is compiled classification, not condition

Feature130780 reads payloadbyte+2E at1307B4 and ANDs1 at1307C0, returning the result
at1307CC. `AssetResourceDatabase.RawFeatureFlags &1` is therefore this callback.
Kind2 alone is insufficient: scenery in the same family returns0. This getter does
not inspect cleanliness, price, occupancy or quality. The feature's value/thirst/hunger
callbacks are zero. Native relief arrival and hiding are documented separately in
native-shop-flow.md; no-LIMBO in a script does not answer visibility at the guest layer.

Further bridge boundary checked directly after the family trace:1C1068 gates on global
2E9818/non-null machine and stores speed with SH at machine+C0 (not a named ordinary
script variable). 1C0E28 validates nonnegative variable index against machine+8C and
stores into the word array atmachine+1C. Duration's caller passes index3 unchanged.
Binding index3 to a specific retail variable/managed script API and reproducing the
separate speed/time-scaling field still require their consumer checks; an identical
integer alone does not prove current managed VAR_DURATION/animation timing parity.
