# Native bus batch inputs and eligibility — bounded follow-up

Primary owner-disc ELF read in memory only: LBA262773, Mode2/2352+24,
size0x2B1160, file0x1000 -> VA0x100000. Used ../tpw-selector-disasm.py;
no binary/asset extraction, gameplay changes, or commits. Read
bus-native-arrivals.md first. This is a timeboxed partial, not a complete
semantic reconstruction. Addresses are VAs; descriptive labels are not symbols.
Parent separately resolved the 2ACAB4 clock producer; not re-investigated here.

## 1. Literal 16B7B8..16B8E0 before semantic translation

Incoming manager is not used. Construct iterator at stack via1E5AF0, get
first pointer with1E5BA0, advance with1E5CA8. 1E5B58 starts from14CBE0()
(pool[39528C]+8), adjusting nonnull allocation by+8; when empty it falls
through other pools using1E5BA8. Thus this is an object traversal, not a
read of a single global "rating". Full iterator membership is UNREAD.

Accumulator starts **10**, but becomes **0** if the first pointer is null
(movz16B7F0). For each object O:

* r =1448E0(10), hence **0..9**; s1=20+r, s2=2, s4=0.
* Obtain type T through O's vtable at O+0x10, signed adjustment+0xA0,
  function+0xA4.
* Jump table361CE0 contains, for T=1..7:
  16B860,16B890,16B860,16B894,16B844,16B860,16B860.
* T=5: call virtual slot adjustment+0x1D0/function+0x1D4, add return V
  to s1.
* T=1,3,6,7: call118470(O), **discard its return**; s4=20. Then call
  that same virtual slot; multiply its return V by unsigned byte O+0x126,
  add low32 product to s1.
* T=2: s2=10. T=4 or outside1..7: no additional changes.
* Accumulate unsigned division `u32(s1+s4)/s2` into the 32-bit accumulator.

118470 reads manager through16AE90/16B218, subtracts unsigned halfword
O+0x124, and returns; that result is not the multiplier at16B884.
Manual decode of **00432018** at16B884: R5900 MULT writes **a0** as well
as LO; 16B88C adds a0. **000001CD** at16B89C is BREAK7 (the compiler's
zero-divisor guard), not an unknown arithmetic step.

Translation justified so far: **randomized sum over traversed objects,
with type-specific virtual contributions**, used as batch-count input A.
Not a proved park rating, demand meter, or guest total. For an empty iterator
A=0. For type2 each contribution is2; type4/default is10..14. Other
contributions, overall range, virtual return initial values/setters, and
O+0x126 setters: **UNREAD**. Byte load bounds that multiplier to0..255,
but does not identify its meaning. No nonnegative/overflow-free assertion
for the virtual-return paths.

## 2. Literal 16D1E8: distinct represented-type fraction, not fixed capacity

Starts local counters K=0,N=0; obtains object from12A550. It calls16D140
with the following (type, number-to-test) pairs:

|type|number getter|table offset|
|---|---|---|
|3|12AFE0|0x14|
|6|12B040|0x44|
|7|12B010|0x2C|
|8|12B070|0x5C|
|1|12B0A0|0x74|

Each getter literally returns a word at
`[360850 + 4*object[+8]] + 4*object[+4] + tableOffset`.
For each index i from0 to number-1 (no iterations when number<=0),16D140
calls12AC78(object,type,i), increments N, and increments K iff return!=0.

12AC78 **counts** objects in the active linked list from14DA50()=[395208]:
matching virtual type slot+0xA4 AND1E1E98(O)==i. 1E1E98 returns unsigned
byte **O+0x97**. It follows O+0 to the next item. Multiple matches for the
same type/index count only once in K, because16D1A0 converts the result
to Boolean. This is positive producer/consumer evidence for **represented
(type,index) entries among enumerated entries**; no attractiveness or
physical seating interpretation is needed.

Return at16D298..2D8 is:

    min_signed(100, 25 + signed_div(low32(K*75), N))

Manual MULT16D2A8 writes v0 as well as LO. BREAK7 at16D2B0 guards N==0;
there is **no normal zero-denominator default of25**. When N>0 and the
counts/products fit, 0<=K<=N implies output25..100. No fixed initial
return is proved: it depends on enumerated tables and active objects.
Its bus consumer subtracts active guest pool count153DA0(), so it acts
as a guest-count ceiling in that consumer, not a fixed bus capacity.

Table pointers360850 are395488,395580,395678,395750, all **BSS**, not
file-backed initial table contents. ELF PT_LOAD file-backed end37E008,
memory end3AF354. Do not interpret bytes at a naive file mapping of those
VAs as table words. Loader initial contents are zero; runtime population,
object[+4]/[+8] selectors, actual table counts and allowed selector ranges:
**UNREAD**. Likewise byte+0x97 setters are UNREAD.

## 3. Release gates: actual producers

### [2B72A4]: explicit open flag, not inferred from arithmetic

ELF initial word **0**. 14E528 writes0;14E4C0 writes1 at14E50C and also
stores16B228(manager)+12*16B230(manager) at2B7298. 14E538 returns the flag.
Initializer149C2C clears it, then149C38 calls230260 with literal ELF string
**"OpenPark"** at360060; when nonzero149C48 calls14E4C0. This explicit
configuration name supports calling it the **open-park flag**. Observed
setters write only0/1; indirect/save restoration writers not exhaustively
excluded. Other direct callers of the on helper:153444,1552DC,15FE58,
1C7C5C; off helper:155380 (plus initializer). Their broader UI/event
identities are UNREAD. Initial live flag can therefore be1 depending on
OpenPark; do not hardcode the ELF0 as the whole initialization sequence.

### 152730/152740: active allocated object pointer

152730 returns **[3953CC]**, and152740 simply wraps it. This is not a
Boolean weather or pause accessor. Explicit zero initialization1515D0;
BSS loader initial0. 152668 preserves input selector, calls1DEBC8(selector),
and stores its returned pointer at1526B4. 1DEBC8 switches selectors1..10
and allocates/constructs differing objects (e.g. selector dispatch targets
1DEC00 allocates0x200 then1D0310;1DEC18 allocates0x188 then1CF268).
It stores selector at constructed object+0x24. Full class identities and
out-of-range behavior are **UNREAD**.

1526D0 calls object's virtual slot+0x0C via table at object+0x158 with
argument3, then clears3953CC at152714. Calls14DAF8(2) on installation,
14DAF8(1) on removal. The proved gate is **no installed object in3953CC**;
not enough evidence here to name the object a particular disaster/event.
Direct installation callers124434,13782C; removal caller15133C. Upstream
selector semantics and lifetime scheduling remain UNREAD.

### [2EEB9C]: per-update count of flagged guest objects

ELF initializer **0**. At151960 it is explicitly cleared in the delay slot
of the call to14BE60 (controller update), so this is not a monotonically
increasing lifetime arrivals counter. At211724..734, if guest allocation
field **N+0xA4 !=0**, increment2EEB9C by1. Activation20BCF4 clears that
field. Observed setter210E64 writes1: one path after18DA78 returns0,
another path after14B368,14B9D0,192D50 and related state/list operations.
Those conditions' gameplay meaning is **UNREAD**; do not call this count
all guests, bus occupants, or a rating. Observed field writes establish
0/1 in these paths, not an exhaustive whole-program field-write proof.
No saturation in the increment. Scheduler coverage needed for an exact
per-update maximum / whether every flagged guest contributes once.

The existing controller consumer14C2EC requires **signed count<30**.
Together with the other gates, completed bus state2 attempts a batch only
if open flag!=0,3953CC==0,and2EEB9C<30. It still advances/dwells when a
gate fails (see arrivals note). Ordering of guest updates relative to the
release test is not fully re-traced here.

## 4. Count constants, setter boundary, implementation dependencies

Re-read16B614..6B0 confirms the previous formula, with neutral names:

    ordinary attempts = min_signed(
        signed_div(low32((A + B)*0x1333), Q),
        20 - (word3953D0 + word3953D4),
        representedEntryCeiling - activeGuestPoolCount)

ELF B=[2B9734]=0,Q=[2B9730]=81920. Runtime B/Q setters **UNREAD**;
immediate-address search is not proof they cannot be changed indirectly.
Override[2B9738] initial0;16D490 writes the result of230258("LoadsOfKids")
at16D4B8, gated by a1==0xFFFF and a0!=0. Result range UNREAD; consumer
only tests zero/nonzero. Override attempts=min_signed(20,20-W), bypassing
the A and ceiling clamps, **but still calculating A and the ceiling first**.
Thus even override consumes their RNG/calls and does not avoid an invalid
zero denominator. W's exact producer meaning/range remains **UNREAD**;
149C1C/20 explicitly zero its two words. Attempts<=0 activate none;
pool-acquisition failures reduce successes, not number of loop attempts.

Bounded implementation dependencies:

1. Implement only the already traced batch loop, eligibility comparisons,
   timer/state ordering and pooled activation contract; replace the port's
   invented one-per50ticks/cap12, not with another guessed constant.
2. Supply A through a producer interface until iterator pools, concrete
   virtual slot+0x1D4 implementations and byte+0x126 writers are resolved.
   Preserve RNG call order and unsigned per-object division when resolved.
3. Supply the enumerated-entry tables, type/index identity and active-list
   membership before claiming native ceiling values. Do not fabricate
   25 for N==0, and do not read BSS tables out of ELF file bytes.
4. Resolve W producers, live B/Q values, active3953CC class lifecycle,
   guest+0xA4 meaning and update ordering for fully named eligibility.
5. Integrate the parent's independent clock result separately. No fixed
   cadence, unconditional eligibility, physical bus capacity, or 100%
   gameplay parity claim follows from this partial trace.

## Parent cross-check against already resolved producers

The iterator membership/order is already established in native-destination-score.md:
3,6,1,7,4,5,2, newest active allocation first per pool. The +1D4 virtual producers are
already resolved for all seven concrete families in ride-value-producer.md. Those two
items are no longer project-wide UNREAD merely because this narrow pass did not repeat
them. The traversal here does not add the destination chooser's eligibility filter.

The byte O+126 is the **current upgrade tier**, not an unidentified age multiplier.
Parent re-read1178AC..D0: it indexes the compiled52-byte tier records with this byte.
Initializer116040 (stores116070/74) sets tier2 when configuration230260("Upgrades")
is nonzero, otherwise tier0. String35A4F0 is exactly Upgrades; the negative ADDIU low
half must be sign-extended, not incorrectly mapped to36A4F0. Other positive stores at
11629C,116C88,118AA4 remain to classify fully. Thus a normal tier0 ordinary ride's
V*tier term is zero; its contribution is floor((40+rand10)/2),20..24. Do not silently
change this into V or an age factor because the unused118470 return looks suggestive.
118470 was independently reread: manager time minus u16[O+124], returned without a
store to126. Its discarded return is not the tier producer.

### PSX prediction checked, not transplanted

Peer supplied a PSX trace in which the ride's +20 term is gated by age<=1 day.
Parent checked the PS2 instructions after that prediction:16B864 calls118470;
16B868 (delay slot) unconditionally assigns s4=20. 16B86C loads the vtable,
16B874 overwrites v0 with its+1D4 target, and16B878 calls it. There is no branch
on the age return in this interval. 118470 itself has no store/side effect that
could turn this into an age-conditioned bonus. Thus **PS2 unconditionally adds
both20 and tier*value for kinds1/3/6/7** in this routine. Preserve the difference.
The nonempty-list base10 and three-way count min do agree across the two independently
read binaries. The PSX example's tuningB=100 is not the PS2 ELF initializerB=0.
