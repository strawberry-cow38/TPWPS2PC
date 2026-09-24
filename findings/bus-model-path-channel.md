# Bus motion channel0x200: authored MPS curve plus APS progress

September24 parent trace; owner-disc reads in memory only. This corrects the old
animation.md conclusion that the path has no on-disc data. No renderer change yet.
Selected text evidence: /tmp/tpw-bus-external-path.log, /tmp/tpw-bus-mps-relocation.log,
/tmp/tpw-bus-model-spline-reloc.log, /tmp/tpw-bus-progress-eval.log,
/tmp/tpw-bus-parent-spline.log. No binary/assets extracted.

## Why current playback does not move the body

All eight Bus1/Bus2 APS files have the three known slot5 records. Body node0 tracks
carry flags20E00/E00/20E00: they use0x200 motion and0x400 facing, not the+10 inline
path reader. AnimatedModel.UseRecord only fills its path/facing dictionaries from
Animation.SplineAt(track), which reads+10; it returnsnull on these body tracks.
The existing census in animation.md already identified96 carriers and the dead
orient gate. It need not be repeated or mistaken for a complete implementation.
The three animation records have flags4, no small/index records; wheel tracks use
rotation flags18 (plus visibility20000 in the first/last records).

## Positive loader-to-file link

MPS loader169D48 validates the MPS magic/version, then calls169AA0 at169E24.
169B3C..48 relocates **model-header+78** by adding the loaded file base.
169C28 reads **u16[header+2C]** as path-table count;169C30 reads+78;169C38..54 walks
**16-byte** descriptors and calls168728, which relocates descriptor pointers+8/+C.
This is not an unidentified runtime-only allocation.

Animation caller1A87F8 uses its model-header argument for mesh count+30, mesh table+48
and helper table+4C; at1A8894 it passes that same header as a2 into1A7F48. The latter
saves a2 in s5. Thus its later `s5+78` is this model table, not proof of a player-laid
path. The model instance/resource loader chains are recorded in bus-native-animation.md.

For each bus model the first descriptor at file3F84 has:

    flags = 2; pointCount(u16 at+4) = 45; points pointer(+8) = 3F94;
    optional pointer(+C) = 0

Points are float XYZ triples. The world-specific files carry authored coordinates;
do not synthesize the route from the entrance tile or culling bounds.

## Binding uses the PARENT spline helper

At1A82C8 the evaluator loads **currentNode+4** (parent pointer), tests its flags bit8,
and at1A83AC..B8 reads the **parent's u16+52**. This selects descriptor
`model[+78] + 16*index`. The bus body parent is helper at3D0, flags80000058, index0.
Wheel parents are the body mesh at190, flags201; they use their rotation channel.

Correction to the first inventory wording: reading+52 on all three meshes returned0,
but those zeros were NOT proof of their path references. The consumer reads the parent,
and only the body has this external-path track. Matching zero fields do not create a
shared motion channel on the wheels; they inherit the body's transform normally.

## APS progress is also in the file

Bus body track+1C points to a pointer. In all eight APS files the three records use
file offsets19C,7A4,DF4; their first words point to1A0,7A8,DF8 respectively.
The target data are floats, e.g. first words42C7AB88,42C82D59,..., not u16 key times.
This is why trying strides8/12/16 and interpreting floats as time keys failed.

1A83BC..D8 dereferences that pointer and calls1AE450(samples,recordDuration,&out,
curveFlags&1;f12=currentFrame). The sampler uses integer/fractional frame parts:

* At exact frame==duration, decrement the sample index and add1 to the fraction.
* Topology bit1 set: next index wraps modulo duration. Clear: the last sample holds.
* Interpolate the two float percentage values. If their absolute difference exceeds50,
  add100 to the smaller endpoint first, interpolate, then fmod(result,100).
* The caller independently normalizes with fmod(progress+1000,100).

28CC90's error-name pointer37C1B0 is the literal **fmod**, corroborating the positive
wrapper/core call28E090. This is not division by100 inferred from a constant. The
modf-style integer/fraction helper at28C5C0 must be retained when porting the sampler.
Inputs outside the caller's0..duration range are not a license to invent extrapolation.

The sample count used by the native accessor is duration, not duration+1: the exact
endpoint branch avoids reading sample[duration]. Validate actual file extents, not an
invented count field on this track.

## Topology and curve type are separate

1A8414..444: Bezier flag2 uses pointCount/3; other curves use pointCount.
1A8444..84D0 then tests **flags&1**: set uses that many spans; clear uses one fewer.
After normalized progress, `u = spanCount*progress/100`, integer part selects a span,
and fraction selects within it. Bezier caller uses i=3*span+1; same existing evaluator
1AD9A8, with point indices modulo pointCount. Other dispatches use flags8 linear or
otherwise Catmull-Rom, just as the inline path evaluator does.

**Bus flags2 => open, 45 points =>14 spans.** It is NOT a15-span closed loop merely
because45 is divisible by3 or because the shared evaluator wraps its point indices.
The peer's closed-loop prediction was explicitly rejected against this caller and
retracted. The old inline-path relation `(keys-1)*3+1` also cannot be imposed on this
separate model-descriptor format, which has no inline time-key array.

Facing0x400's exact tangent/matrix consumer is the next implementation prerequisite;
do not reuse the dead viewer's frame+0.1 tangent helper blindly. The native caller
samples along curve fraction, and its orientation/0x800 interaction still needs closure.

## Section12 cleanup correction

The partial visual note initially left source-vs-current transform ambiguous.
Parent decoded the LDL/LDR operands explicitly:1AA734 word6AA70017 loads from **s5**,
not s4. s4 is the instance-model node; s5 is the corresponding SOURCE-model node
selected from stack+88. The block copies source node+10 to stack+40, then1704F0 copies
that matrix to instance node+10 (a1). This is bind/source transform restoration, not
retaining the last current matrix. Native cleanup also clears old node hiding and
restores affected geometry as documented in bus-native-visual.md.

Consequently a bus stop/section12 adapter needs source-pose restoration with the proper
old-node visibility cleanup. Merely freezing the last frame or setting Root.Visible=false
is not the native instruction. Final pixels/culling still require actual authored poses
and a rendered check; no screenshot has yet verified this bus path.
