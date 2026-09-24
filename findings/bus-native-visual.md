# Native bus visual integration — four-minute partial return

> **September24 lifecycle correction:** bus state0's section12 request is rejected
> by the type15 section bounds check before the lower-level cleanup handler.
> Do not infer source-pose restoration from that rejected request. Native node10
> is self-hidden, while20 prunes descendants. See `bus-native-lifecycle.md` for
> the caller/loader/draw proofs and executable consumer controls.

Read all existing bus-native-*.md. New instructions read from the owner disc into
memory only, LBA262773, 2352-byte sectors +24, size0x2B1160,
file0x1000 -> VA0x100000, using ../tpw-selector-disasm.py. No extraction,
game-code changes or commits. Addresses below are VAs. This is deliberately a
partial result: **root placement is constrained; final state0 pixels are not yet
proved**. In particular, section12 is not enough evidence to implement hide.

## Placement: the six floats are NOT a bus world transform

Let B=[37E0D4], and allocation G=B-12. The signed -12 vtable adjustments matter.

* Constructor227158 allocates a 0x40-byte matrix, stores its pointer at G+38
  (B+2C), then227200 calls170380 on it. 170340 clears all16 words;
  170394..3AC sets offsets0,14,28,3C to1. **The graphics root starts identity.**
* 147728..754 passes f12..f17 = **0,0,-10,70,1.5,10** to17D280(B).
* 17D280 does NOT access the graphics matrix. It reads M=[B+0C], and
  writes two vectors at **M+50 and M+60**, each with w=1. The low flag bit
  of each vector's x/y is preserved. Values are (0,0,-10,1) and
  (70,1.5,10,1), modulo those embedded bits.
* 17BF94..FA4 establishes M = instance[+8][+4], the instantiated model
  header. Draw22837C..3BC passes **M+50 to21F3C0**, the same rejection
  helper used on mesh+70 at227CD4..CE4. These are the model's culling
  bounds, **not translation / Euler angles / scale**. Never turn70 or1.5
  into a world offset or bus scale.
* Graphics virtual slot36F320/324 is {-12,228CF0};228CF0 returns G+38's
  matrix pointer. The update/fitting path1F1BE4..BF4 uses that very slot.
* The graphics draw virtual at **36F374 =2282E0** (table36F360 on G+8)
  reads G+38 at228348, composing it with renderer matrix2F0380 through
  21E930 at228350. A special draw flag0x200000 selects another camera
  matrix branch, not a bus-specific placement. Mesh draw227C60 then
  composes mesh+10 at227CCC and performs per-mesh bounds rejection.

In the creation/setup/command chain inspected here there is **no bus-specific
write replacing that identity graphics root**, no entrance-point translation,
and no per-world rotation. Authored model/node/APS coordinates therefore must
be preserved under this root; guest spawn point0 is not the bus origin.
This is a bounded positive initialization/draw trace, not an exhaustive search
for every later indirect matrix writer. The supplied BusSAMDontApplyOffset=1
is consistent with it but was not used as proof or as evidence Bus.RSE runs.

## Registration: object survives a section12 command

147758 ->17CE10(B). It checks B+0C and low four B flags, gets manager310D48
through230A88, then calls graphics slot+BC:36F348/34C={-12,228860}.
228860 returns its input unchanged: **the registered object is G, not B**.
Manager virtual slot+24 receives (G,0,-1,-1,-1,-1). B flag2 is then set.
The inverse17CEC8 uses manager slot+2C and clears flag2. That inverse is NOT
part of section12 handling traced below. Manager's live vtable/population and
its dispatch to graphics slot36F374 remain the unexpanded edge;310D48 starts
zero in the ELF and must not be treated as its runtime vtable.

## State0 / section12: cleanup, not an explicit visibility switch

Previous findings prove state0 requests section12,record0,channel0,flags0.
The new positive consumer chain is:

    1AB780 ->1ABA20..4C ->1AA460(instance, oldRecord, null)
                                      -> channel.section=12
    subsequent1AC048: section==12 -> immediate exit at1AC084

The cleanup call happens when an old section exists and incoming flags&0xC
is zero (the bus's flags0 meet this); an already-section12 channel skips it.
This is NOT a freeze-last-frame instruction alone and NOT an APS record12.

1AA460 first clears node hidden bit0x10 on the old record's listed nodes,
except nodes protected by bit0x80000000 (1AA510..558;1AA5A8..60C).
It clears additional old animation flags using mask0xF40000, or0x800000
for the model's alternate format, and has geometry/channel cleanup later:
1AA7DC..860 copies source-model geometry for affected morph flags;1AA874..8A8
copies another per-strip stream for flag0x10000. Thus blanket retention of
all old pose/visibility state is demonstrably wrong.

**What is not settled:** the exact post-cleanup node transform/bind outcome.
The transform branch1AA724..7B8 copies the current node+10 matrix to a stack
temporary and calls1704F0; that helper begins by copying its third argument
back to its second. It is not legitimate to label that observed copy alone
"restore bind matrix". The subsequent hierarchy/rebuild ownership, relevant
model flags, and the bus node's authored endpoint/bind position were not
resolved in this timebox. Neither this command nor the inspected draw entry
contains a direct section12 -> hidden test. Normal model/mesh culling remains.
Therefore **not proven hidden, not proven offscreen, and not proven full
bind-pose retention/restoration**. It remains registered; it stops channel
sampling and runs selective cleanup. This is a concrete narrower result,
not permission to guess state0's on-screen result.

## Existing AnimatedModel API and adapter recommendation (no edits)

In this worktree the actual API is constructor(model,aps,record,texture),
UseRecord(record), SetFrame(float), and exposed Root. There is **no centering
parameter or Center method in AnimatedModel.cs**. Centering, if used by a
caller, is external Root/parent manipulation. Root defaults to Z mirror
(1,1,-1), with a debug yaw180 environment alternative. WorldAt uses model
WorldTransforms and APS paths replace local translation, recomposing the
hierarchy; it does not subtract geometry bounds.

Recommended source-grounded active-phase adapter:

1. Use the catalogue-selected world's Features/Bus1 or Bus2 MPS and same-stem
   APS, preserving authored coordinates and unit scale. Leave native root
   identity; apply the project's common native-to-Godot/world conversion
   exactly once. Account for AnimatedModel's built-in Z mirror when attaching
   to a parent already doing conversion. Do NOT center on bounds, anchor to
   visitor spawn, or invent per-world nudges.
2. Select section5 records0/1/2 for native states1/2/3. UseRecord followed by
   SetFrame with the proved30Hz authored clock; no modulo looping. Preserve
   native queue/hold/dwell ownership documented in bus-native-animation.md
   and bus-native-clock-and-audio.md; this renderer class is not the controller.
3. **Do not equate UseRecord(null) with proved native section12 parity.** It
   clears playback dictionaries and resets some geometry but deliberately
   retains _hidden across records. Native cleanup above actively clears hidden
   bits on specified old nodes. A later SetFrame with null record returns model
   bind WorldTransforms; native final matrix behavior is still a target.
   Root.Visible=false would instead invent a hide command. Keep section12 as
   an explicit unresolved visual adapter operation until that cleanup is closed.

## Exact next targets

* Finish1AA460 through1AA948,1704F0 through its return, and downstream
  hierarchy rebuilding/dirty-node processing, with bus model flags in hand.
  This distinguishes retained matrices from bind reset without visual guessing.
* Resolve manager310D48 initialization/vtable slot+24 registration and its
  dispatch to2282E0, then the mesh walk228614 onward (and2290A8) for hidden-bit
  tests. The object/matrix/vtable edges above are already positively identified.
* In-memory asset inspection only: bus model authored root/bind bounds and
  node tracks at5:2 endpoint, followed by exact cleanup simulation. Test all
  world/catalogue variants; the result should be authored coordinates, not
  eight hand-tuned placement corrections.

Unavoidable remaining visual unknown is the native section12 cleanup output,
not an unexplained per-world placement offset. No unconditional claim of
visible/offscreen pixels or full draw-manager traversal is made here.


## Parent follow-up supersedes the transform ambiguity

See bus-model-path-channel.md. The critical LDL/LDR bases at1AA734 onward are s5
(source node), while a1 is s4+10 (instance node). Completing the unaligned-load decode
establishes source/bind matrix restoration. Do not retain the earlier current-matrix
interpretation; exact visibility/geometry cleanup remains as above and final bus pixels
still need a rendered control. The newly found missing0x200 path consumer is the main
active-phase renderer blocker, not an absent bus model or a per-world coordinate nudge.
