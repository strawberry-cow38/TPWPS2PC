# Compiled shop purchases: identity, region, and actual handback

Evidence checkpoint: 2026-09-24. This is an initial-settings needs/cash implementation,
not a claim that shop economics, carried objects, costumes or mutable quality are complete.
See [compiled-shop-viewer.md](compiled-shop-viewer.md) for the actual Viewer catalogue join.

## Three distinct layers

1. Authored SAM text: useful source data, but not necessarily the shipped runtime value.
2. Loaded DBA record: joined by payload asset-name row through the graphics text key, not by
   similarity of prices/effects. The three regional DBAs remain explicit inputs.
3. Consumer: `0x20E1A0` dispatches on compiled Product and transforms those values. Correct
   field decoding alone does not establish correct signs, cash units, selection or effects.

The independent `CompiledShopPurchaseChecks` exercises actual shop RSE/APS handbacks through
`ParkSim` and `ParkVisitors`, not just direct calls to a reimplemented arithmetic helper.
Every world runs EUR, USA and JAP explicitly, with controlled initial needs, a frozen rise
clock, and a one-transaction stopping condition. It stops on physical handback even when a
sale is refused; waiting for Purchases would accidentally permit repeated visits.

## Traced original contract

Addresses refer to this disc's SLES_500.32, read in memory from the user's image. Getter
`0x1D1D08` loads the compiled Product byte at payload+0x30 (`0x1D1D2C`); consumer call
`0x20E33C` dispatches through table `0x36CA60`. This is not inferred from SAM ShopType.

Shop vtable `0x368080`:

| Input | Callback / load | Compiled payload |
|---|---|---|
| Thirst T | +1E4 → 1D1C08; load 1D1C2C | +33 |
| Hunger H | +1EC → 1D1C48; load 1D1C6C | +32 |
| Happiness C | 1D1B88; load 1D1BAC | +34 |
| Vomit V | 1D1CC8; load 1D1CEC | +36 |

Both virtual entries carry adjustment -8; the call applies it to shop+8. The reused
register/decompiler temporary does **not** make H, T and V one amount.

For ordinary valid bounded needs/effects, products 0/4/5 do:

```
hunger -= H; toilet += H; sick += V;
happy += C * (q1 - q2/15) / 100;
thirst += T;
litter += runtime_base + rand(25);
```

The hunger and toilet calls independently re-read the same H getter. Thirst is an **add**
(`0x20E4DC`), not a reduction. Product 1 reverses the served need: thirst decreases by T,
toilet increases by T, hunger increases by H, with the remaining food/drink effects.

Product 7 jumps to `0x20E36C`: it first adds q2/15 to thirst, then **falls through** at
`0x20E3AC` into the whole food arm. It is not a happy-only or unimplemented no-op branch.
At initial q2=0 the extra increment is zero. The test cannot distinguish this prefix from
plain food; the original instruction trace, not a zero-q2 test, establishes its existence. For future non-default quality, retain the two
sequential thirst updates/caps rather than combining them without proof.

Litter uses the initial image byte **30** at `0x2EEB60`, loaded at `0x20E504`, plus the
0..24 remainder from `rand(25)`. It is not SAM LitterEffect (e.g. burger's authored50).
The port uses that initial value; subsequent changes to the global are not established.

Products 3/6 do not receive food's hunger/thirst/toilet/sickness/litter changes. Product3's
first eligible purchase awards C*q1/100. Its original ownership bit can suppress repeat
happiness; that ownership/carried-object path is **not modelled**. Our named balloon-family
fixtures are first purchases, not evidence for repeat ownership behavior.

Product2 stores personality index8 (`0x20E6B8`). The getter `0x20C078` reads u16 at
`0x2EEBD8 + index*8`; row8 at `0x2EEC18` is **14**. The port changes the preferred intensity
and adds C*q1/100 happiness. Row8 is deliberately separate from the existing eight-value
random-spawn policy: selectable after a purchase does not prove selectable at spawn.

Costume presentation is incomplete: the original also ORs guest+34 with0x80, invokes
`0x20BC70`, and makes global call `0x1073C0(...,6,1)`. The helper uses the actor at guest+10,
a separate byte at guest+A0 and table2EEBA0, calls actor virtual+0C with argument597, then
calls17CE10. Those actor/resource/global operations are not implemented by changing a
preference byte. Guests do not yet acquire the original visible costume here.

Original arithmetic narrows some increments to signed low bytes and often clamps only
one side. These controls use valid retail values and do not establish parity for malformed
or out-of-range inputs; the port's general two-sided Clamp is not a byte-perfect emulator.

## Cash and initial quality are verified, not guessed normalization

Original spawn keeps cash in ×10 units (`(rand(300)+200)*10`, 0x20BDDC..BE04).
The purchase checks cash >=10*price at0x20E2F8..30C and debits10*price at0x20E32C..340.
Debiting the bare compiled price undercharged tenfold. Affordability and debit must ship
together; failed payment must not feed the guest or increment Purchases.

Let S be the complete SHOP object:

* q1: getter1D1F50 reads u16 S+BA; setter1D1F58 writes it.
* q2: getter1D1FB8 reads word S+AC; setter1D1FC0 writes it.
* Initializer1D16C8 explicitly invokes setters with100 at1D1870 and0 at1D187C.
* UI paths expose bounds50..100 and0..100 respectively, while raw setters do not clamp.
  Save/restore also handles these fields; UI bounds are not proof of universal bounds.

Thus unscaled compiled happiness is correct at verified **initial** q1=100/q2=0.
Mutable quality/control/restore behavior remains absent, not a decoded constant. Costume
and trinket happiness use q1 alone; food/drink use q1-q2/15. Do not merge the two formulas
when mutable quality is implemented.

The original also checks willingness and maintains shop/park accounting. The port's cash
and needs checks do not cover those systems. “All purchases faithful” would be an overclaim.

## Independent fixtures and known counterexamples

Named first balloon-family purchase: JUNGLE Balloon key239, HALLOW VampShop153,
FANTASY FatFairy66, SPACE Droid388. All compiled happiness10; authored values15/15/10/15.
Droid's compiled price/cost is50/35, the others45/30: grouping by matching effect/price tuple
would misidentify it. Starting cash1234 and happy20 become1234-10*price and30; unrelated
needs/litter/preference stay unchanged.

Ice creams: keys245/151/69/391. All regions price30, thirst5, happiness5. EUR/JAP hunger25,
vomit10; USA hunger15,vomit15. Starting H80/T70/toilet10/sick20 becomes H55/T75/toilet35/
sick30 in EUR/JAP, H65/T75/toilet25/sick35 in USA. Cash becomes934; litter9 becomes39..63.
The expected numbers are literals tied to named records, not recomputed from the same getters
whose plumbing the test is meant to check.

Named drinks, fries(product7) and costumes each retain their own full identities and amounts
in every world. Real handbacks pin opposite drink transfer, product7 food fall-through,
and costume preference14 without reseeding. A synthetic same-effects food/drink pair isolates
the selector from effect magnitude: H11/T17 yields different direction and bladder amounts.

Transaction-time cash299 refuses an ice-cream price30 without any effects or counter; cash300
buys once. Cash is set after routing so a future route affordability filter cannot vacuously
pass the handback guard test. Later ticks must not pay or reseed an already completed purchase.

There are67 independent-helper assertions per world:21 for each region plus2 same-input
selector controls and2 explicit source-path attachment checks independent of region choice. The matrix requires the count and named region/transaction/arm witnesses;
an omitted helper or stale binary is not allowed to look green. Known HALLOW Thrill Grill
and SPACE Moon Buggies failures remain separate, unchanged retail-data findings.

## Regression evidence for this package

The initial65 checks pass in each of JUNGLE/FANTASY/HALLOW/SPACE. Nine restored-source mutations
are rejected through the new helper itself (not compiler failure or unrelated assertions):
bare-price debit18 failures; food subtracts thirst7; drinks use food4; balloon gains litter3;
costume preference omitted3; product7 omitted3; missing affordability3; strict rather than
inclusive affordability3; Serve ignores compiled Product9. Each initial mutated run completed all65
assertions; original source and the normal build were restored afterward.

The four-world matrix retains only the two exact retail findings (matrix exit2, raw exit1
in each affected world). Fresh Debug runtime gate: all8scenes pass, including actual viewer
compiled attachment and standing-service ownership. Whole-disc TPW.PS2.Check exits0.
Runner tests:56 pass. These are headless/runtime/consumer results, not a newly rendered or
human-played shop flow; that is the next bounded integration check.

Peer review then decoupled source-path shape from region selection: the final67 assertions
include independent archive-qualified and bare-world controls even if the region loop changes.
A tenth mutation disabling only the bare-world split fails exactly that new assertion while
completing all67. The restored final matrix passes all67 in each world and retains only the
same two retail reds. Peer independently test-merged the earlier65-check candidate and caught
three core mutations through our checks (selector10, bare debit18, food sign7 failures).
Product7's zero-q2 behavior is intentionally indistinguishable from food in these tests;
its nonzero prefix remains established by instruction evidence, not by this default fixture.
