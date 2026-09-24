# Native PS2 bus controller and arrivals — primary trace, timeboxed partial

Read bus-native-catalogue.md and bus-native-animation.md first. Evidence below is
primary PAL ELF memory only: owner disc tpw_ps2.bin, LBA262773,
Mode2/2352+24, size0x2b1160, file0x1000 -> VA0x100000. Used the local
selector reader; no extraction, gameplay changes, or commits. Function names
below are descriptive labels, not recovered symbols. All addresses are VAs.

## Positive result: actual bus-to-guest activation chain

**14BE60 (controller update), at14C308 ->16B5C0 (batch arrivals),
at16B6E0 ->14AC48 (acquire/activate pooled guest), at14ACE0 ->
vtable36CCE0+0x34 =20BCD0 (guest activation).**

The indirect last edge is proved, not inferred from a failed direct-call search:
20BBD0 constructs the guest, writing vtable36CCE0 at guest allocation+0x18
(20BBF0..20BC00). 14AC48 returns allocation N; its embedded object is N+8.
At14ACD4 it reads N+0x18, loads signed adjustment from table+0x30 and
function from+0x34. Data36CD10 contains adjustment **-8**, and36CD14
contains **20BCD0**. Thus jalr14ACE0 passes N, exactly the object base
expected by20BCD0. That callee calls191360(N+8), resets N+0xa4, sets
low five bits of N+0x38 to0x0b, samples1C4930 into N+0x2c, and does
further randomized initialization (not fully expanded).

14AC48 uses pool pointer[3952CC]: if pool+4 is null it returns null.
Otherwise it increments pool+0x0c, unlinks a node from the free chain and
links it at pool+8, invokes activation, then14ACE8 ->14DA60(N+8).
14DA60 inserts the embedded object into the list rooted at395208, avoiding
an already-present node. This is **pooled activation**, not a heap allocation
per arriving guest. Earlier pool allocation/population remains untraced.

Other positive callers of14AC48:1603F0 and16B74C. The latter is the
single-arrival helper16B740, directly called at14C220 when[2B736C] is
nonzero; this flag is then cleared. That is separate from the bus batch.

## Controller states, writers, and release condition

14BE60 includes the controller14C22C..14C350:

* Always updates native bus through14C22C ->147828 first.
* Delta D =1C4920() =[397640], sampled at14C200.
* If outer delay[39527C]>0, subtract D and skip all state/poll work.
* Else if dwell[395278]>0, subtract D and skip state/poll work.
* Otherwise14C258 calls1479A0([395280]).
* If state==1 and[3953E0] is neither0 nor2, stop this tick.
* Otherwise set3953E0 to2 when state==2, and0 for other states.
* Advance only if1477C8() is true **or**1477F8() is false. Per the
  separately proved animation trace, that means end-hold OR no active section;
  it is not two equivalent animation-finished tests.
* For completed state2, call batch arrivals only if14E538()!=0,
  152740()==0, and signed[2EEB9C]<30. 14E538 reads2B72A4.
  14C2FC gets16AE90() (manager getter), then14C308 calls16B5C0(manager,0).
* Regardless of those release gates, completed state2 writes **0x50000**
  to395278 at14C314. Then increment395280 at14C330.
* Max state is[2B71A8], whose ELF initializer is **3**. If incremented state
  exceeds max, write state0 at14C338 and outer delay **0xC8000** at14C350.

So batch activation occurs on the state2 completion path, immediately before
state becomes3. State3's animation command is issued only on a subsequent
eligible controller update, after dwell expires. Poll-skipping delay ticks still
call147828. Timers may cross below zero; the tick doing subtraction does not
also execute the state transition.

Initialization149C60 zeros395278;149C68 zeros395280;149C64 ->144870,
unsigned remainder modulo200, writes39527C in delay slot149C94 of
149C90 ->1476E0 (native bus creation). Initial delay is **0..199 in native
controller units**, not a proven 0..199 milliseconds.

Other observed state consumer14BE18 checks state2 in a3953DC/3953D8/
3953E0 coordination path. State writers positively established here are
149C68,14C330,14C338; this is not an exhaustive claim about indirect writes.

### Timer units: do NOT transplant PSX timings or APS clock units

1C4920 simply returns397640. Writer1C4AA8 calls11E688(&397638), stores
its result at397640, and caps signed values above0x4000 to0x4000.
11E688 computes **(new[2ACAB4] - previous) <<7**, updating previous.
Thus controller delay uses a scaled delta, distinct from the millisecond APS
clock documented in bus-native-animation.md. Raw thresholds0x50000 and
0xC8000 correspond to2560 and6400 units of that upstream counter before
per-update capping. Counter producer/rate not traced here: **no conversion
to seconds claimed**. 1C4930 returns397644, a separate update counter
incremented at1C4A74; it is not this delta.

## Spawn position: shared point0, no animated bus position input

Batch16B5C0 preserves a1 as point selector; this caller passes **0**.
14E288 returns constant1. Only selector-1 takes a random-point path.
16B6B4 calls **14E290(selector)** once, before the guest loop16B6E0..718.
Every successful guest gets the same point's horizontal coordinates:

* 16B70C stores point.x at (N+8)+0x1c = N+0x24;
* 16B710 stores point.z at (N+8)+0x1e = N+0x26.
* 16B6F4 separately calls1448E0(8), storing its result at N+0x7d.
  1448E0 is unsigned PRNG remainder; this gives0..7, not position jitter.

14E290 reads point bytes from

    2B71B0 + [3952E4]*0x36 + [3952E8]*0x12 + selector*2
    x = byte[0]*256 +128
    z = byte[1]*256 +128

It also calls149D90(x,z,0) to fill the middle halfword of its packed point;
its precise height semantics need further tracing. The arrival loop copies
only x and z. **This PS2 code independently proves the tile-center calculation
and one shared point before the loop.** No bus-object/world transform is
supplied to this path; it does not spawn at the animated bus position.

Primary table point0 byte pairs, indexed by3952E4 row then3952E8 column:

|row|column0|column1|column2|
|---|---|---|---|
|0|(26,6)|(26,6)|(26,6)|
|1|(44,6)|(40,6)|(0,0)|
|2|(36,6)|(34,6)|(0,0)|
|3|(44,6)|(32,6)|(0,0)|

Zero table entries are literal data, not proof that those combinations are
valid gameplay selections. Do not substitute PSX pairs or identify the
point as a particular named exit without mapping these selectors.

## Batch count: bounded, not a fixed bus capacity

16B61C..6B0 computes attempt count C. Let:

* A =16B7B8(manager), a calculated demand-like value (internals incomplete);
* B =[2B9734], initializer0;
* Q =[2B9730], initializer81920 (0x14000);
* W =1527B0(0)+1527B0(1) =[3953D0]+[3953D4];
* H =16D1E8(manager) -153DA0(); latter returns pool[3952CC]+0x0c.

Ordinary path when[2B9738]==0 (ELF default):

    C = min(signed_div(low32((A+B)*0x1333), Q), 20-W, H)

No invented interpretation of H as a fixed cap:16D1E8 is a separate
capacity-like computation still to expand. Override path when[2B9738]!=0
sets C=min(20,20-W), bypassing the demand/H clamps. If C<=0 there are no
attempts. Otherwise it makes exactly C calls to14AC48, decrementing count
also on null result. Hence successful activations can be fewer than C.
All happen in this one loop/update; there is no per-guest release timer here.

Critical manual decode: R5900 mult at16B634 writes rd=v0 as well as LO.
16B698 `0083880a` = movz s1,a0,v1;16B6A8 `0043880b` = movn s1,v0,v1;
16B6B0 `00a4880b` = movn s1,a1,a0. These implement the clamps above.
Packed-point unpacking16B6BC..6C4 is dsrl32/dsrl: shifts48,16,32;
the UNKNOWN operations in14E290 include LDL/LDR and SDL/SDR packed copies,
plus DSLL/DSLL32/DSRL32, not extra spawn calls or coordinate transforms.

## Exact next targets / unresolved boundary

1. Producer of counter2ACAB4 (11E6F8 accessor), to prove controller seconds
   and pause/scaling semantics. Do not label raw0x50000 as milliseconds.
2. Expand16B7B8..16B8E0 for demand,16D1E8 onward for capacity, and writers
   of2B9730/34/38; formula/clamps above are already direct control evidence.
3. 152730 (wrapped by152740), writers2B72A4 and2EEB9C, to name release
   eligibility gates. No claim that arrivals are unconditional.
4. Pool3952CC construction and callers20BBD0 to prove preallocation count;
   activation through20BCD0 is already positively linked by the vtable.
5. 149D90 for point height; upstream callers of14BE60 and containing
   initializer ending149D1C, if broader scheduler/init names are required.

No RSE status/WAITTRIGGER IDs have been equated with these native states.

## Parent verification: ordinary personality selection is finally constrained

Both batch and single-arrival callers set the personality index after successful pooled
activation. Batch16B6F4 calls1448E0 with8 in its delay slot, then16B6FC storesv0 atN+7D.
Single16B77C loads8,16B784 calls1448E0,16B78C stores the same byte. Parent read both
instruction sequences directly, not by attributing nearby calls to spawn.

This proves rows0..7 are selected uniformly in these ordinary arrival paths. It does NOT
prove the preference table has only eight rows; the costume path demonstrably selects8.
The port's existing eight-entry ordinary spawn distribution now has a decoded consumer
basis, whereas its complete cross-system RNG ordering is still not native parity.
Single16B740 also obtains point0 and copies its x/z, independently corroborating batch
position logic. Source personality comments on the peer's VisitorNeeds lane were flagged
for correction; no behavior change was made in this research branch.

The scaled controller delta producer is now resolved in bus-native-clock-and-audio.md:
active outer update increments the source by10000, delta helper shifts7, then caps to4000.
Do not retain this section's earlier “upstream producer unread” as the current conclusion.
