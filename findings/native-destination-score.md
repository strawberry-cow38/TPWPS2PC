# Native destination scoring and history

Static instruction/dataflow evidence from SLES_500.32, read in memory from the owner's
MODE2/2352 disc. SHA256 `231771d7f39cc29e7573ce62f437af15d97737a64c1d1c49df8404ead0b7577a`.
No executable/assets extracted. This is not a console execution capture. The managed
coordinator still uses its needs-first/random selector as of946a89a; these findings
are the next consumer specification, not a claim that this behavior already ships.

## Calling frame and score equation: 20C138

`G` is the complete guest; `P` is the candidate's placed-base subobject. A virtual
slot means signed adjustment `s16[table+slot-4]` followed by target `u32[table+slot]`,
with `table=u32[P+10]` and the adjustment applied to P. Do not treat the paired table
as an unadjusted function-pointer array. For SHOP/FEATURE, complete object C=P-8.

1. Obtain rotated inside connection A through1E1760 and add origin from virtual+74.
   Narrow each sum to signed16. Bounds check149D20 requires `0<=x<mapX-1` and
   `0<=z<mapZ-1`, not merely `<mapX/mapZ`; failure returns-1.
2. Obtain guest integer cell via guest-base G+8 virtual+74 ->1925F8. Its position
   bytes atG+24/26 are signed16 fixed-point, arithmetic-right-shifted8. Fractional
   motion does not affect this score. Missing-connection(-1,-1) is not explicitly
   rejected before origin addition; do not invent that extra native branch.
3. For kind2 only, require virtual+1DC nonzero and1309F8(C) nonzero; else return-1.
4. `distance=abs(entry.x-guest.x)+abs(entry.z-guest.z)` (Manhattan, not route length).
   `D=100-clamp(trunc(distance*100/(mapX+mapZ)),0,100)`.
5. `preference=u16[2EEBD8+8*u8[G+7D]]`. Read ride value through virtual+1D4.
   `F=2*(50-min(abs(preference-value),50))`. Read+1D4 again;
   `E=1` if the second value is nonzero, else0 (not a signed-positive test).
6. Initialize product termB=0, weightBW=0. For kind4 SHOP, read product1D1D08(C).
   Products2/3 use threshold at2EEB90 (initial50); product6 uses2EEB94 (initial55).
   Only when signed happinessG+75 **exceeds** that threshold:
   `B=trunc((happy-threshold)*100/(100-threshold)); BW=3`.
   Other products have no such term. These thresholds are memory loads; runtime
   immutability has not been proved merely by reading the ELF initial data.
7. `T=NeedLookup(s8[G+7A],virtual+1E4)` and
   `H=NeedLookup(s8[G+77],virtual+1EC)`.
8. `U=ReliefLookup(s8[G+79],virtual+1DC!=0)` and
   `K=ReliefLookup(s8[G+76],virtual+1DC!=0)` (getter read again).
9. `score=trunc((D+E*F+3*T+3*H+4*U+2*K+BW*B)/(13+E+BW))`.
10. Fetch runtime candidate ID via+9C. For each independently matching guest history
    word at44/48/4C/50, divide the current score by5/4/3/2 respectively, sequentially.
    Signed divisions truncate toward zero. No final nonnegative clamp.

Fixed denominator13 includes the four needs weights even when their callbacks return
zero. Only preference weightE and product weightBW disappear when inactive. There is
no price/cash/willingness test, boredom input, or shop purchase-quality multiplier in
this resolved score. Do not import purchase-time product-arm behavior into selection.

Arithmetic details: native sums/products keep low32 bits; shifts are arithmetic where
specified. DIV truncates signed toward zero; the lookup index helpers use DIVU. They
have no bounds checks. A safe port should reject unsupported malformed inputs explicitly,
not call interpolation or clamping the native algorithm. Normal 0..100 guest needs fit.

### Instruction checkpoints

*20C380/384 and388/38C use BLTZL with SUBU negation in taken-only delay slots for abs.
*20C3B8 divides distance normalization signed. 20C3C0..3CC clamps the quotient.
*20C42C uses SLTU zero,value: nonzero, including negative returns.
*20C4A0 tests threshold<happy, strictly. Ordinary delay-slot subtraction does not enable
 the product term when the branch skips it.
*20C540/544 and56C/570 pass100 to ReliefLookup via MOVN in the JAL delay slot iff enabled.
*20C584 initializes LO withD;20C594 EE MADD addsE*F. It is not a plain multiplication.
*20C5A4 adds13 toBW,20C5BC addsE;20C5CC divides the whole numerator.
*20C620..630 and664..66C implement signed /4 and /2 with negative-value correction,
 not unconditional arithmetic shifts/floor division.

## Exact lookup tables, not fitted curves

`20C0F8`: guestBin=udiv(u32(need),10), inputBin=udiv(u32(input),10).
Return signedword at`36CE88 + 44*inputBin + 4*guestBin`.
Rows are inputBin0..10, columns guestBin0..10:

```
 0  0  0  0  0  0  0  0  0  0   0
 0  0  1  2  5  7 11 15 20 25  31
 0  0  1  4  7 11 16 21 28 36  44
 0  0  2  4  8 13 19 26 35 44  54
 0  0  2  5 10 15 22 30 40 51  63
 0  0  2  6 11 17 25 34 45 57  70
 0  0  3  6 12 19 27 37 49 62  77
 0  0  3  7 13 20 30 40 53 67  83
 0  0  3  8 14 22 32 43 57 72  89
 0  0  3  8 15 23 34 46 60 76  94
 0  1  4  9 16 25 36 49 64 81 100
```

`20C0B8`: if second argument zero, return0; otherwise return signedword at
`36D070 + 4*udiv(u32(need+5),5)`. The second argument enables the lookup, not scaling it.
Indices0..21:

```
-20 -20 -20 -20 -20 -20 -20 -20 0 1 2 4 7 11 17 26 37 53 73 100 100 100
```

For normal needs the index is floor(need/5)+1, not nearest-bin rounding. Enabled relief
can therefore contribute a NEGATIVE term for a guest who doesn't need it.

## Concrete SHOP specialization

Final table368080 resolves these slots:

| Slot | Adjust | Target | Value |
|---|---:|---|---|
| +74 | 0 | 1E1FA8 | placed origin, P+84/P+88 signed16 lanes |
| +9C | 0 | 1096D8 | runtime serial u32[P+0C] |
| +A4 | 0 | 1E1D68 | kind u8[P+96] |
| +1D4 | 0 | 1E5A98 | literal zero |
| +1DC | 0 | 1E5AA0 | literal zero |
| +1E4 | -8 | 1D1C08 | compiled payload+33 thirst byte |
| +1EC | -8 | 1D1C48 | compiled payload+32 hunger byte |
| +2DC | 0 | 1E1E48 | placed-state byte+9A in2/10/11 |

Product1D1D08 reads payload+30. Resource accesses use the adjusted complete object,
handle C+28,10FA30 and release10FA88. The thirst/hunger reads at1D1C2C/1D1C6C occur
in release-call delay slots; those reads are real, not skipped after release.

Thus rawShopScore=(D+3*T+3*H+BW*B)/(13+BW), signed truncating. Eligibility receives no
guest and is not an affordability filter. For other concrete final tables/producers,
see the updated ride-value-producer.md, rather than guessing base excitement or zero.

## Choice, history condition and runtime identity

20C6A8 returns success immediately if G+28 already holds a target. Otherwise it enumerates
through1E5AF0/1E5BA0/1E5CA8, checks virtual+2DC, and starts bestScore=0,best=null. A larger
signed score wins; equality replaces only if rand(2) is nonzero. Negative scores cannot
win. Zero-score candidates may win, or an all-zero population may produce no selection.
The exact enumeration order remains a separate integration dependency: equal-score
replacement is sequential and must not be rewritten as uniform choice among ties.
A chosen score<8 costs happiness5 floored at0, but does not reject the target.

The selected pointer is stored atG+28 in the ordinary branch delay slot20C778 regardless
of the next condition. Only when signedG+79>=99 and selected kind!=2 does20C7A0 call
history writer20C8D8. All kind2 candidates bypass it. This happens at SELECTION, before
route request, not on arrival or successful purchase. A route failure does not roll it back.

The writer is deliberately recorded literally:

```
p=G+48; i=1;
do { v=*(p-4); ++i; *p=v; p+=4; } while(i<4);
G[44]=selected.virtual_9C();
```

20C8F0 loads the previous word,20C8FC overwrites the next,20C904 increments the pointer
in the branch delay slot. Therefore `[a,b,c,d] -> [new,a,a,a]`, NOT `[new,a,b,c]`.
Independent scoring divisions amplify duplicates. No plausible FIFO repair is licensed.

Activation20BCD0 resets all four words toFFFFFFFF at20BF38..50. PoolOfPeople3952CC
construction creates100 objects ofstrideA8, but the reset is in activation, not merely
allocation. Acquisition14AC48 invokes guest table36CCE0 slot+34 (adjustment-8,target20BCD0)
for each use/reuse. Target invalidation211D80 and inspected service completion do not clear
history; persistence is across one active lifetime. This is a field-specific census, not
proof against all hypothetical whole-object restore/copy paths.

SHOP/FEATURE +9C ->1096D8 returnsP+0C (C+14). Common initialization1093B0 assigns the
serial from2AA73C and increments it; placed init1E0F30 calls it. Guest activation uses
the same allocator through191360. These are runtime object serials, NOT DBA keys, content
IDs or pool slots. A port that reuses numeric ride IDs needs instance-scoped serial identity
before it can safely store native-style history.

## Discriminating arithmetic controls for the eventual consumer

* map64x64, entry20,20 and guest15,17: Manhattan8 -> q6 ->D94.
  Entryx62 passes;63 fails. A missing(-1,-1) entrance plus origin10,10 can pass as9,9.
* Need99/input5 ->0;99/10 ->25;99/25 ->36;100/25 ->44.
  Need20/input90 ->3, not36 (table orientation control).
* Preference30/value50 and70/50 bothF60. Mismatch50/51 bothF0. Value0 removesE,
  not the other denominator weights. A negative nonzero getter still enablesE.
* Enabled relief need34 ->-20;35 ->0;40 ->1;90 ->100. Disabled100 ->0.
* SHOP D100,T=H=0,product2/3: happy50 ->score7;51 ->6;100 ->25.
  The first enabled bonus can LOWER the score because it adds denominator weight.
  Product6: happy55 ->7;56 ->6;75 ->14. Product7 happy100 still7.
* Generic D100 with U=K=-20 and other terms0: numerator-20, /13 ->-1, not-2 or0.
* Score97/history all matching ->97/5=19,/4=4,/3=1,/2=0, not first-match19.
  Negative-7 /4 ->-1 and /2 ->-3, not floor-rounded values.
* Full SHOP: D94,hunger90/input25,thirst90/input5,product0 ->(94+108)/13=15.
  Most-recent-ID match reduces it to3. Cash/willingness alone do not alter it.
* Sentinel history ->recordA ->[A,FFFFFFFF,FFFFFFFF,FFFFFFFF]; recordB ->[B,A,A,A];
  recordC ->[C,B,B,B]; recordC again ->[C,C,C,C]. Reset on activation, not service.

## Enumeration order resolved (September24 follow-up)

The previous enumeration dependency is now partly resolved by reading the iterator,
its jump tables, head getters and allocation writes together (not sorting by kind ID):

*1E5AF0 ->1E5B58 initializes iterator flag+4=0 and its pointer from14CBE0,
 pool39528C+8: ordinary ride head, complete pointer converted to placed pointer+8.
*1E5BA0 returns that pointer. 1E5CA8 reads the current candidate kind via virtual+A4.
 Table369E20 uses placed+130 as next link for coaster kind1; for all other kinds it
 subtracts8, reads complete+0, then adds8 if nonnull.
*On exhaustion1E5BA8 advances families through table369E00. Literal entries for
 kind1..7 are1E5C0C/1E5C7C/1E5BEC/1E5C38/1E5C54/1E5BFC/1E5C1C.
 This yields **ordinary3 -> track6 -> coaster1 -> tour7 -> shop4 -> sideshow5 ->
 feature2**, then stops. It is neither numeric kind order nor one global insertion list.
 Empty categories are skipped; shop/sideshow/feature skipping tests the flag+4, which
 the chooser's1E5AF0 initialization sets tozero.
*Head getters14CBE0/14CC90/14CCE8/14CC28/14CD78/14CDC0/14CD30 read+8 from
 pools39528C/395294/395298/395290/39529C/3952A4/3952A0 respectively.
*Allocators149FF0,14A0B8,14A180,14A558,14A620,14A6E8 prepend the activated
 complete object to pool+8, with its+0 pointing at the former head. Ordinary writes
 are14A058..70; track14A1E8..200; shop14A5C0..D8; feature14A688..6A0;
 sideshow14A750..768. Coaster14A4A0 prepends through its+130/+134 links at
 14A504..51C. Thus freshly activated objects precede older objects within each family.

This establishes normal allocation iteration, including recycled object activation.
It does not establish the order in which a savegame loader activates objects; do not
claim a port save/load ordering that has not been traced. Runtime serial identity and
pool link ordering are different inputs: don't use a reused display ID for either.

## Implementation review slice (not yet coordinator integration)

GuestDestinationScore implements the score/lookup/history/choice arithmetic. The
43 audit checks include a direct in-memory PT_LOAD read of all121 need words and all
legal relief input indices from the owner's executable. Hand-calculated controls
separately pin signed division, denominator, feature availability, product thresholds,
taste bands, literal forward history copying, negative rejection and sequential ties.
Eight deliberate arithmetic mutations each fail (table transpose, erase negative relief,
drop fixed denominator, FIFO rewrite, always replace ties, ignore duplicate history,
wrong map edge, enable taste for zero value). Those checks do NOT exercise ParkVisitors.
The coordinator still needs the actual candidate producer/eligibility/identity/ordering
integration; this helper's passing checks are not evidence that fresh visitors have
stopped selecting toilets through the shipping path.
