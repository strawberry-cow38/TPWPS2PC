# Ride-value producer trace: family identity matters

Partial evidence from the owner's SLES_500.32 image, read in memory from the disc.
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
