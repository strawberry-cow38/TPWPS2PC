# Ride effects: the consumer is more than its constants

Checked against the owner's SLES_500.32 image read in memory from the disc. This is
instruction/image evidence for that build, not a savestate observation or all-version
claim. No executable/texture assets were extracted. Peer implementation is staged in
ride-sickness-gate (2027a5b, documentation correction0ce3bf7) and integrated with the
independent controls described below.

## Verified original behavior

FUN_0020EDD8 retains the ride callback's value at0x20F1C4. Preference getter0x20C078
reads guest byte+0x7D, multiplies by8 and loads the unsigned halfword from0x2EEBD8+index*8.
After the getter call at0x20F1C8, subu at0x20F1D0 and the signed branch/negation at
0x20F1D4–1DC form the absolute preference mismatch. Unsigned comparisons at0x20F1E0/1E8
against51/21 select the image globals0x2EEB3C/40/44: mismatch0..20 gains15 happiness,
21..50 gains10, and51+ gains5. Happiness is capped at100. Flat15 was only the best band.

At0x20F248 `slti v0,s0,56`, followed by bne at0x20F24C to0x20F28C, skips the sickness
block when the ride value is below56. The subtraction of30 at0x20F258 belongs **inside**
that arm;30 is not the gate. At0x20F25C the image constant1212 is loaded;0x20F264–26C
multiply, shift left12 and arithmetic-shift right24, then add/clamp sickness at100.
Boredom has its own block at0x20F294–29C with constant4096, outside the sickness gate.

This disproves the previous comment that gentle rides reduce sickness through this
consumer. The old port reduced it below30 and incorrectly increased it at30..55,
including the chosen default45. The initial review concern—negative fixed-point
rounding—was not the fix: that negative arm is unreachable in this original consumer.

## Evidence boundaries and port choices

The first eight stride-8 records are verified:90,30,50,75,100,45,60,70. The next groups
contain integer/float-looking data, but **the getter has no bounds check**. That layout
is not proof of the original table length/index domain. What assigns guest+0x7D remains
unread. Uniform selection among the eight verified records is explicitly a port choice.

PreferredIntensity0 means unspecified in the port and preserves the configurable
happinessGain fallback. Nonzero preferences use the bands. The floating scale arguments
remain port/test overrides; this work does not claim arbitrary overrides reproduce
original fixed-point overflow semantics. The source of each real ride's callback value
is still a separate integration gap; the default RideIntensity45 remains chosen.

## Discriminating tests

`RideEffectConsumerChecks` has33 checks, run through ParkSimAudit in each world:

- Verified default image constants; literal sickness outputs at0,26,29,30,45,55,56,
  57,58,100 and an upper-clamp case.
- Happiness/boredom still occur below the sickness gate; unrelated needs/cash are not reset.
- Both signs of preference mismatch and exact20/21/50/51 edges. These use explicit
  nonzero preferences and fallback7, which cannot accidentally equal any15/10/5 band.
- No creation of a missing guest row.

Before the core gate, the new controls produced exactly four sickness failures (0,26,
45,55). Corrected source passes all33. The older completion lifecycle fixture is now
intensity60, Sick61, Happiness83, Unknown78=62, with explicit unspecified preference0;
one configured effect gives Sick76/Happiness90/Unknown78=32, and a double effect does
not clamp away the evidence. A second actual script-completion/readmission case uses
preference90 against ride value60, requires middle-band happiness93 rather than fallback90,
preserves preference/cash, and rejects repeat effects. Lifecycle coverage rises45→50.
The disruption replay ledger also includes the new preference field.

Eight production mutations were rejected by targeted assertions: missing sickness gate,
gate55, gate57, gating the entire effect, flat happiness, wrong lower band edge, wrong
upper band edge and signed rather than absolute mismatch. The restored source passed.

The matrix requires33 consumer and50 lifecycle checks plus named gate/preference
witnesses; omission cannot silently pass. All four worlds pass those checks, with only
the exact existing HALLOW Thrill Grill/SPACE Moon Buggies retail failures (raw1/matrix2).
All seven runtime scenes pass, including standing74; TPW.PS2.Check exits0;53 Python
runner/classifier tests pass. This is not completion of every visitor feature or a
claim that the original personality assignment/per-ride input producer has been decoded.
