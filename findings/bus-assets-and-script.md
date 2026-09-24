# Bus: asset identity and script contract, September 24, 2026

Read from the owner's PS2 disc in memory, not an extracted asset tree. Textual inventory
and instruction output: /tmp/tpw-bus-inventory-clean.log. A broad initial worker timed
out; the narrower parent-run C# reader completed all four world WADs plus DATA.WAD.
These are asset/script facts, not proof that the native bus runs this script.

## The geometry exists, in separate bundles

Every world carries these case-insensitively resolved bundles:

* `Features/Bus/bus.RSE`, `bus.rss`, `Bus.sam`: shared named behavior/metadata bundle.
* `Features/bus1/bus1.mps` and `.aps`, with its textures.
* `Features/bus2/bus2.mps` and `.aps`, with its textures.

Both animation files in every world have three records in section/slot5, with lengths
**220,260,220 frames**. Other sections0..11 have count0. This establishes the actual
three-variant animation resources, not three audio stages. Bus1 versus Bus2 selection
must come from the native catalogue consumer, not a guess from the names.

All four bus.RSE files have identical SHA256:
`501b94bf633c461b2c1d4d12932fab54d2b8f14ebe8e406bf5404c1cab223211`.
SAM IDs are JUNGLE1600/FANTASY4600/HALLOW2600/SPACE3600. All say name Bus,
HasQueue0, WhichUIType4 (not shown in UI), DontApplyOffset1. The authored comment says
animation is relative to world(0,0), not object position. This does not by itself prove
how the native scene transforms that world or positions a bus in the port.

A search only beside Bus.sam would miss both real models. The catalogue's prior phrase
“script-only entity” describes that bundle, NOT a claim that the bus has no graphics.

## Literal RSSE contract

Variables0..3: VAR_TRIGGER, VAR_STATUS, VAR_SCRIPTID, VAR_TEMP.
The script NAME is **Bus**. FINDSCRIPTRAND at codeword4 requests **Traffic Lights**,
not the park gate. This corrects the casual “finds a gate” comment in RseMachine.

| Codeword | Action |
|---:|---|
|2|wait2000ms; then resolve Traffic Lights once into variable2|
|7/10|clear trigger; status1|
|13|ADDOBJ8,1,6,tag1; SETOBJPARAM(tag1,param20,value0)|
|22..37|trigger animation5:0; wait returned duration minus3000ms; param20=75; wait animation; wait1000ms|
|39..45|status2; yield until external trigger is nonzero|
|47..60|wait1500ms; param20=0; remote Traffic Lights variable0=1; status3; clear trigger|
|63..82|trigger animation5:1; wait duration minus2000ms; param20=75; wait animation; remote variable0=0; wait1000ms|
|84..90|status4; yield until external trigger|
|92..106|status0; wait1000ms; param20=0; wait2000ms; clear trigger; status5|
|109..120|wait animation5:2; status6; KILLOBJtag1; yield until external trigger|
|122|loop to codeword7 (not back through initial directory lookup)|

This shows an external controller is necessary to advance the script's trigger waits.
It does NOT identify that controller, prove the script is used by the PS2 native bus,
or establish that its status values equal native bus states. SETOBJPARAM20's meaning
must be read in the sound/object consumer; currently the managed host only records it.
Do not name it “speed”, “volume” or “fade” from these values alone.

## Native model creation is positively located

See bus-native-catalogue.md. Bus1/Bus2 have logical catalogue ID **97/0x61**,
NOT SAM ID1600 and NOT flags0x61. Type15 maps to the features path. A real native caller
149C90 ->1476E0 tests that loaded catalogue resource, allocates a graphics object,
stores it at37E0D4 and initializes it through228868 with ID97.

Parent independently checked the1476E0 raw instruction sequence and wrapper targets:
228868 ->17BFF8(this+0C), then2278B8; animation-command wrapper228958 ->17C5D8;
update wrapper228938 ->17C920. These are concrete next consumers, not an inference
from a Bus string being present. Global395280 feeds the native bus state setter;
its writers and the guest admission/drop-off handoff remain the next trace.

No bus implementation has been added. In particular no guessed arrival timer, borrowed
sound-bank stage list, or arbitrary spawn count substitutes for the native controller.
