# Bus clocks, animation states and audio — parent follow-up

September24, PAL SLES_500.32 read only in memory from owner's disc. Raw instruction
logs /tmp/tpw-bus-clock-writer.log, /tmp/tpw-bus-frame-call.log,
/tmp/tpw-bus-state-tail.log and /tmp/tpw-bus-pose-parent.log. These are selected text
instructions, not extracted binaries. This follows the independent animation/arrival
traces and corrects the remaining upstream-delta question.

## Controller delta is NOT milliseconds

11E758 tests pause flag2ACABC and, when zero, does:

    2ACAB4 += 10000;  // ADDIU v0,v0,0x2710 at11E76C

10EEC0 calls11E758 at10EEC8 before13AF68 and13ADD0. 11E660 is another wrapper;
this is positive caller evidence, not a claim that every possible indirect writer
has been excluded. Pause helpers11E708/11E730 set/clear2ACABC and call220C68(0/1).

11E688 snapshots2ACAB4 and returns signed low32 `(new-old)<<7`. On its first call
it initializes the prior snapshot, returning zero. 1C4AA8 samples that at1C4AC4,
stores397640, and caps signed values greater than16384 to16384 at1C4ACC..DC.
There is no lower clamp. The known active outer-loop increment10000 therefore gives
1280000 before the cap, **16384 afterward**; it is not elapsed milliseconds.

Consequences for the already traced bus controller:

* Dwell0x50000 is **20 subtractions** at saturated delta16384.
* Outer delay0xC8000 is **50 subtractions** at that delta.
* The subtraction that reaches/crosses zero still skips state work; transition occurs
  on a subsequent eligible controller update.
* Initial random0..199 needs at most one positive saturated subtraction, not199ms.
* No elapsed counter increment yields delta0. Do not make a paused countdown consume
  render frames, or equate this clock with the animation clock.

These are exact counts under the stated saturated-delta path. The normal PAL
one-gametick/render, two-VBLANK pacing can map them to approximately0.8s/2s, but
nested pumps/gamespeed/update ownership remain the conditional boundary documented
in native-shop-flow.md. **Do not import PSX seconds or publish an unconditional
wall-time guarantee.** A long/unusual interval can also exercise signed wrapping;
this is a literal integer producer, not a float delta to “repair” silently.

## Native command phases, separate from Bus.RSE

1479A0 caches last-applied state at37E0D8 and returns without reissuing commands
when unchanged. Actual command parameter mapping is proved in bus-native-animation.md.

* State0:111D78 on saved sound handle; request special animation section12 (no current
  animation), record0/channel0/flags0/speed1. Do not interpret12 as an APS record.
* State1: create sound object described below, parameter20=0; section5 record0,
  channel0/flags0/speed1.
* State2: parameter20=51, stamp147158 clock at2B73A4; section5 record1.
* State3: parameter20=51, stamp that clock; section5 record2.

The native phase2 completion releases guests, then sets the controller dwell and
increments logical state3. The state3 command is not issued until that dwell expires.
This differs from simply playing the three APS records back-to-back. The final visible
pose/draw behavior of section12 must be checked in the renderer before calling it
“hidden” or “parked”; no visibility inference is licensed solely by the sentinel name.

## PS2 has a positive native audio path

This is different from the peer's bounded negative on the PSX build.
At147BC4 the bus calls111428(audio,1,6,position,&2B73A0,0), stores the created handle,
then111D40(audio,handle,0x14,0). First argument comes from110A68, the same audio-object
manager used by already traced visitor effects. The selector1 is **not** automatically
one of SoundCatalogue's public RSE sound-group numbers. Event6 is literal.

Initial point comes from entrance table bytes+6/+7, converted through149D90 and
fixed-point lanes. This is distinct from guest spawn point0 atbytes+0/+1.
The bus update147828 then follows its actual animated sound fitting:

    resource instance from2EAAD0[B+14]
    1F1F78(instance,0x200,1) -> fitting1
    1F2978(handle,&fitting,outXYZ)
    XYZ*256 -> signed16 narrowing -> arithmetic >>8 -> float XYZ
    111758(audio,savedHandle,&XYZ)

So it repositions one existing sound object, not a new event each render frame.
The narrowing/quantization steps are literal instructions, not a claim that a port
should substitute an unrelated ground or camera position.

After147158()-[2B73A4] is **strictly greater than2000**,147980 resets parameter0x14
to0. At equality2000 it does not. State2/3 write51, not Bus.RSE's75. Meaning of
parameter0x14, audio selector1's bank mapping, event-flag repeat/sustain consumer and
111D78's full stop/release behavior remain audio-lane trace targets. Do not name the
parameter pitch/volume/speed from those values alone. Relevant addresses sent to cow.
