# Native bus animation consumer — narrow primary-ELF trace

Timeboxed partial result. Read the two requested bus findings first. All new evidence
below is direct PAL ELF code/data, read only into memory from disc LBA262773,
Mode2/2352+24, length 0x2b1160; file0x1000 maps to VA0x100000. No binary/assets
extracted, no implementation or commits. Addresses are VAs.

## Result

**The native state's a2=1/2 really is the zero-based APS record index within
section5**, not a trigger or playback mode. The independent playback-channel
argument is a3=0. Native state2 selects **5:1 (260 frames)**, state3 selects
**5:2 (220 frames)** in the selected world's Bus1/Bus2 APS. These calls use
speed1.0 and flags0: **non-looping**, not a repeating bus animation.
The animation clock advances at **30 authored frames per second**, regardless
of display refresh rate. The two controller polls are **not interchangeable
“animation finished” tests**: one tests an endpoint/hold bit; the other tests
whether the current section is anything other than the no-animation sentinel12.

## Object adjustment, resource binding, setup

Let B be the object stored at37E0D4. Its vtable is36F290. The entries before
setup/command/update targets contain signed this-adjustments **-12**:

* 36F298 = {-12,0,228868}, target at vtable+0x0c.
* 36F2E8 = {-12,0,228958}, target at +0x5c.
* 36F2F8 = {-12,0,228938}, target at +0x6c.

Callers load the signed adjustment and add it before jalr. Each wrapper then
adds12. Thus **17BFF8/17C5D8/17C920 operate on B itself**, not B+12. Direct
poll calls on B are consistent with this. Ignoring the adjustment would give
incorrect field offsets.

Creation from the prior trace is149C90 ->1476E0 ->230A98; setup calls
228868 ->17BFF8(B,97,0,-1), then2278B8 (post-setup helper not expanded here).
17BFF8 resolves97 with17D7E8 and writes:

* B+4 = logical ID; B+8 = selected catalogue record index;
* B+0x10 = selected record+0x20 loaded-resource handle;
* B+0x18 = record+8 resource type (15), because a2=0 means use that type;
* a3=-1 means no alternate catalogue resource is resolved.

17C13C ->17BBF0 receives that resource handle. Type15's entry at
362A40+14*4 is17BF04, which calls1F8C18 with allocation/options0x32f.
1F8C18 resolves the resource handle via2EA8C8[handle], calls1F6230, and
registers the resulting instance in2EAAD0. 17BF44 stores the instance handle
at B+0x14. 1F6230 copies the loaded resource's header, including its animation
pointer+0x18, and allocates/copies per-channel 0x38-byte playback records at
instance+0x10 (count at+0x0e). The instance's model binding is+8.

Resource naming remains the already proven catalogue mapping:
Data\\<Jungle|Hallow|Fantasy|Space>\\features\\Bus1 or Bus2, type15.
The ordinary loader is17B778 ->1F8678 ->1F889C ->1F7ED8.
In that loader the MPS load is1F8030 ->166088; at1F8190..81A4 it
**replaces the extension with `.aps`** (literal36ABC8, helper1F7C58), calls
167110, and stores its result at stack+0x238, the template's animation+0x18
(template starts stack+0x220). The template is copied to the loaded resource
at1F8270..833C; the instance copy above preserves that pointer. This binds
animation to the **same-stem Bus1/Bus2 APS**, not Features/Bus/bus.RSE.
The internals of167110/APS relocation were not expanded in this timebox.

Bus1 versus Bus2 is the catalogue's world/variant selection, not the command's
record index. This pass does not determine which catalogue variant a particular
save currently selects. Both resources have the same supplied section5 lengths.

## Command parameters proved by indexing

17C5D8(B, section, record, channel, flags; f12=speed):

* Type15 dispatch table362AC0+14*4 =17C788.
* It gets instance=2EAAD0[B+0x14], animation=instance+0x18.
* 17D7C0 reads animation section count at+0x1c;17D7C8 returns
  animation.sectionTable(+0x20)+section*8. Section entry+0 is record count.
* 17C7B0..7D0 checks both section and record bounds. Valid requests reach
  17C814 ->1ABC80(instance,section,record,flags,channel;f12=speed).
* 1ABC80 chooses playback record = instance[+0x10]+channel*0x38.
* 1AB780 indexes APS sectionTable+section*8, reads its pointer+4, then adds
  **record*0x1c** (1AB918..938). It writes section/record into channel+0/+4
  and speed into channel+8 (1AB95C..970).

Thus the actual bus calls147C38..6C and147CA0..D4 become respectively
(section5,record1,channel0,flags0,speed1) and
(section5,record2,channel0,flags0,speed1). No duplicate-section shortcut is
used by type15 (that shortcut belongs to the17C708 branch for other types).

1ABC80 can **queue** a request behind an existing valid, unfinished,
non-held animation: 1ABD0C..44 checks currentFrame <= length, flags&6==0,
and incoming flags&2==0 (apart from special section13/14 commands).
Queued section/record/flags/speed go to channel+0x24/+0x28/+0x2c/+0x30.
Otherwise1ABE70 ->1AB780 starts it. Consequently do not assume every state
change immediately replaces an in-flight clip. Bus flags0 do not force replacement.

## Time, update and loop math

Channel layout established by consumer:

| Offset | Meaning |
|---|---|
|+0,+4|current section, record|
|+8|float playback speed|
|+0x0c|float current authored frame|
|+0x14|playback flags; bit1 (mask1) loop, mask2 start-hold, mask4 end-hold|
|+0x18,+0x1c|start clock, sampled clock|
|+0x20|float authored length (APS record+4)|

Update147828 ->vtable+0x6c ->228938 ->17C920 ->
1AC6E8(instance,1,0,B) ->1AC048 for each channel.
There is **no one-frame increment and no delta parameter** in that chain.
1AC0F0..10C selects cached clock2E71B8 normally,2E71BC if channel flags&0x40.
1AB710 computes, with an unsigned clock subtraction converted to float:

    currentFrame = ((clock - startClock) * 30.0 / 1000.0) * speed

Relevant manual COP1 decode:1AB764 `46000802` = mul.s f0,f1,f0;
1AB76C `46020003` = div.s f0,f0,f2;
1AB770 `46010002` = mul.s f0,f0,f1. Constants41F00000=30,
447A0000=1000. 1A8920 loads APS record+4 as length and initializes timing.
1A8AE8 refreshes the cached clocks using147158 (global2F07A8) and147140
(global2F0798); it also divides elapsed clock ticks by1000 to produce seconds.
The arithmetic establishes millisecond clock units/30Hz authored time. Clock
producer/pause policy upstream of those globals is outside this pass.

At1AC180..190, `46010034` is **c.lt.s f0,f1**, i.e. length < currentFrame.
So endpoint handling happens on the first update **strictly beyond** the length,
not necessarily at equality. Poll visibility is update-quantized.
With speed1 and a fresh start, nominal lengths are:

* 5:0 =220/30 =7.333333... seconds (resource fact; not a newly traced state call).
* 5:1 =260/30 =8.666666... seconds (native state2).
* 5:2 =220/30 =7.333333... seconds (native state3).

Return-duration math at1ABBA0..BBC is unsigned integer conversion of
length*1000/30; for these lengths, approximately7333/8666/7333ms (truncated
positive conversion). It does **not divide by speed**. Queued requests return
remaining-current nominal time plus new nominal duration (1ABD74..1ABE54).
Do not confuse those returned integer estimates with the floating clock endpoint.

1AB7B4..7F4 copies incoming flags&1 into the channel's loop bit.
1AC254..294 repeats the current section/record when that bit is set; otherwise
1AC298..2B8 issues special section14, whose1AB850..86C handling sets flags0x14
(end-hold plus dirty) without replacing the current section/record. A queued
request instead starts via1AC2C4..2EC. Separate instance flags&0x18 also have
special completion/deactivation branches at1AC1BC..250; their complete bus
configuration has not been audited. The bus command itself unambiguously
requests no looping. Do not infer a literal section14 APS resource: it is a
special playback operation.

## Polls: precise predicates, not guessed names

* 1477C8 returns1 if B is null. Otherwise17C880 resolves its instance and
  calls1AD308(instance,**0**): returns bool(channel0.flags & **4**).
  17C880 itself returns0 for missing instance. It forcibly selects channel0.
  This is an **end-hold flag poll**, not a direct elapsed-time comparison.
* 1477F8 returns0 if B is null. Otherwise17C8C8(B,0) calls1ACAF8 to read
  channel0's current section and returns **section !=12**. Missing instance
  also returns0. This is an animation-selected/active-section predicate,
  not “not finished”: the special14 end-hold path can leave section5 selected
  while the first poll becomes true.

No RSE WAITTRIGGER/status equivalence has been assumed. Native controller state
writers, guest release, and interpretation of these poll combinations belong to
the parent's separate trace. Unresolved here: full special instance-flag endpoint
policy, upstream clock pause/scaling, current-save Bus1/Bus2 selection, and any
runtime relationship to Bus.RSE. No invented arrival timer or implementation.
