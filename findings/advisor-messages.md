<!-- Research notes from the advisor 'what -> how/why' pass (2026-09-28), copied from ~/ghidra_tpw/notes. Paths under ~/ghidra_tpw and scratchpad refer to the research box. -->

# The advisor, message side (PS2 PAL `SLES_500.32`): from "message M was asked for" to what the player sees and hears

Researched 2026-09-28 (agent `advM`). The rule VM's variables and producers are another agent's
area; what this pass saw of them on the way is in §12.

Sources:
- `SLES_500.32` in Ghidra 12.1.2 (own copy `~/ghidra_tpw/agent_advM`). Decompiles:
  `~/ghidra_tpw/agent_advM/out/a1.c` (whole advisor module `0x105d00..0x108e00` + xrefs),
  `a2.c` (every direct emitter, tutorial dispatcher `0x205830..0x206400`, helpers), `a3.c` (park
  lifecycle, options, message-stack save/load, audio), `a4.c`..`a10.c` (UI, language, awards,
  calendar, save/load wrappers, audio message handlers).
- Raw MIPS from the full listing (`scratchpad/full.s`) for every constant that matters; scripts
  `out/tut.py` (tutorial dispatch table), `out/calls.py` (argument at each `jal`), `out/advw.py`
  (every access to the advisor object through global `0x2aa720`), `out/elf.py` (ELF reader).
- The disc through the port's library from a scratch project `~/ghidra_tpw/agent_advM/out/advscan`
  (copy of the prebuilt `TPW.PS2.Data.dll`; nothing built in the repo): message table dump
  `out/msgtable.txt`, `advisor.mps`/`advisor.aps` dumps, text rows, the 106 rules' message ids.

**READ** = seen in decompile, MIPS or data. **INFERRED** = reasoned to. Class/function names in
quotes are the game's own debug strings (`TheAdvisor::AddImmediateMessage` etc., `0x358a20..`).

Units: **tick** = one call of the advisor update `0x1066b0`, which the park scene update
`0x151800` makes once per simulation pass (§3.1); its wall-clock rate is not established here
(lifecycle.md says the same of the sim tick). **ms** = the game's millisecond clock `0x147158`
(`0x2f07a8`). **frames** = APS frames at 30 fps.

---------------------------------------------------------------------------------------------------

## 0. Short version

- **Submit** (`0x107cc0 → 0x1075f0`, READ): a message is routed **immediately** (interrupting
  whatever the advisor is doing) when advisor flag `0x20` is clear **or** the id is 208..274 (the
  tutorial/welcome block); otherwise it is **queued** in a 20-slot FIFO ring (dedup by id against
  what is still queued; when full the oldest is dropped, so 19 fit). There is no priority other
  than that.
- **Presentation is a 6-state machine** run once per tick (§3.2): 50-tick start delay → idle →
  "enter" animation → speak (voice + lip-synced mouth) → "exit" animation → **100-tick cooldown**
  → idle. One message at a time; the next waits for the previous one's speech **and** its cooldown.
  Silent messages still cost the cycle (≈100 ticks).
- **The advisor's text is not a subtitle.** A message with a text row (≠ 310) is appended to the
  **message stack** (`0x3928c8`, 32 records): a HUD envelope with a count at the bottom left; the
  player opens it with **L2**, scrolls, reads the text in a 260-wide box, deletes, jumps to the
  attached ride, or replays a tutorial. Nothing reads the current message's text while it speaks
  (READ, §5). Voice-only messages (row 310) never reach the stack.
- **The head is a 3D model**, `Data\Generic\Advisor\advisor.mps` + `.aps` (registry id 488,
  category 13), drawn at (0.6, −0.5, 0) with scale (0.013, 0.013·4/3, 0.013) — INFERRED lower right
  of the screen. It pops up (APS section 5 record 13, 30 frames), loops a talk animation chosen by
  the speech's length (records 0..6), drops back (record 14, 20 frames). The lip track drives five
  mouth meshes `Mouth - Normal/Aah/Eee/Ooh/Sss`; each message's variant byte picks a **hat/prop
  costume** (hardhat for mechanics, security hat for guards, …).
- **Opcode 8** removes the message-stack record with the same text row (so the log keeps one copy);
  every rule that sends a MESSAGE does TEXT_UI of the same id first. Row 310 is skipped because
  such a message never has a record.
- **Modal messages** (ids 208..274 and 123 BANKRUPTED, when voice is on) lock the whole game's pad
  input, show a "Cancel" prompt, and can be skipped with **Triangle** (Cross in the Japanese
  layout, which this PAL build cannot select). 123 ending (or being skipped) triggers game over.
- **Direct emitters**: 25 functions outside the VM (§8) plus 22 handlers behind a 74-event
  **tutorial dispatcher** (`0x107390`, 21 reachable events → messages 172, 223..274; §7). The port
  has 7 of them (§10).
- **Options**: Game Options has Music, SFX, **Tutorial On/Off** (flag `0x40` only), Vibration.
  There is no advisor on/off and no speech volume. The advisor ducks Music and SFX to 25 while it
  speaks. The message stack is saved per park; the advisor queue, current message and rule state
  are not saved (READ, §11).

---------------------------------------------------------------------------------------------------

## 1. Objects

### 1.1 The message request (the "8-byte object"; READ `0x107c90..0x107cbc`)

Callers put 16 bytes on the stack; 9 are used:

| off | type | meaning | written by |
|---|---|---|---|
| `+0` | u16 | message id | ctor `0x107c90` sets **0x114 (276)**; `0x107ca8(msg,id)` sets it |
| `+4` | u32 | attached object (ride etc.) | `0x107cb0(msg,obj)` |
| `+8` | u8 | "has object" | ctor 0; `0x107cb0` sets 1 |

⚠ A request whose id is never set is id 276, past the 275-record table (see §8, research).

### 1.2 TheAdvisor (`0x270` bytes, pointer in global `0x2aa720`; READ ctor `0x106080`, park init `0x151498`)

| off | type | meaning | readers / writers |
|---|---|---|---|
| `+0` | u8 | **flags** (below) | ctor param; `0x13a178` (tutorial toggle); `0x107c18`; `0x1066b0` state 4; `0x152668`/`0x1526d0`; minigame `0x1dd588`/`0x1dd8e8` |
| `+1` | u8 | saved flags, 0xff = none | `0x1072a0` save, `0x1072b0` restore |
| `+2` | u8 | state 0..5 (§3.2) | `0x1066b0`, `0x107640`, `0x1072c8` |
| `+4` | u16 | countdown (ticks) | states 0, 4, 5 |
| `+8 + 8·i` | 20 × 8 | queue ring slots `{u16 id, u8 kind, u8 pad, u32 object}` | `0x107760` write, `0x107870` pop |
| `+0xa8` | u8 | ring head (next write) | `0x107760`; rule scheduler gate `0x10dcfc` |
| `+0xa9` | u8 | ring tail (next read) | `0x107760` (overflow), `0x107870` |
| `+0xac` | u16 | pending immediate message id | `0x107640` |
| `+0xae` | u8 | its kind | `0x107640` |
| `+0xb0` | u32 | its object | `0x107640` |
| `+0xb4` | u8 | immediate pending | `0x107640` sets; states 1/2, skip clear |
| `+0xb5` | u8 | current message is modal | `0x1074c0`; state 5 clears |
| `+0xb6` | u16 | current message id | `0x1074c0` |
| `+0xb8` | u8 | skip button latched | skip logic; `0x1074c0` clears |
| `+0xc0`,`+0xc8` | ptr | ref-counted resource handle from pool `0x2aae08` (key 0) and its payload | ctor; freed `0x10f720` in `0x150e80`; purpose not traced |
| `+0x108` | u32 | vestigial idle timer (ms, capped 50/tick) | `0x107550`; see §4.9 |
| `+0x118..` | | a look-at camera (eye (0,0,−256), target 0, up (0,−4096,0); `0x115a40`/`0x115ae8`) | ctor only; its use not traced |
| `+0x1f8` | u8 | costume selector of the playing variant | `0x107b90` → `0x107160` |
| `+0x225` | u8 | vestigial random 0..4 | `0x107bd8` |
| `+0x22c` | u32 | model registered for drawing | `0x105db8` (ctor), `0x105e00` (no callers) |
| `+0x230` | u32 | entry animation was played | state 1; `0x106600` clears |
| `+0x234` | u32 | lip gate (mouth active) | play; `0x105f30` toggles |
| `+0x238` | ptr | advisor model instance (`0x230a98`, loaded id 488) | ctor; Delete |
| `+0x23c` | u32 | speech stream handle | `0x111150(…, 0xb, …, &+0x23c)` |
| `+0x240` | u32 | speech start time (ms) | play |
| `+0x244` | ptr | next lip mark | play; `0x105f30` |
| `+0x248` | u32 | current mouth shape 0..4 | `0x105fc8` |
| `+0x24c..0x25c` | 5 × u32 | mesh indices of the 5 mouth meshes | `0x106d88` |
| `+0x260` | u32 | mesh index of `Bug Head` | `0x106d88`; never read |
| `+0x264` | ptr | rule VM object (0xec bytes) — only if flag 8 | ctor → `0x10da78` |
| `+0x268` | ptr | tutorial object (0x24 bytes, vtable `0x36c220`) | ctor `0x205830` |

**Flags** (READ; every test of byte `+0` enumerated in MIPS, §0 of `advw.py` output):

| bit | meaning | tested at |
|---|---|---|
| `0x01` | add text to the message stack; opcode 8 active | `0x1078e8`, `0x1073f0` |
| `0x02` | voice + head + modal handling | `0x1078e8`, `0x1074c0` |
| `0x04` | never tested | — |
| `0x08` | rule VM exists and runs; event counters count | ctor, `0x1066b0`, `0x1073c0`, Delete |
| `0x10` | enforce the 100-tick cooldown (an immediate message may cut it) | state 5 |
| `0x20` | queue mode (non-208..274 ids queue instead of interrupting); queue may drain | `0x1075f0`, state 1 |
| `0x40` | tutorial dispatcher active | `0x107390`, options |

Initial value (`0x151498`, READ): `0x37`; `|0x40` when the profile's tutorial flag
(`0x1c3940` = `0x2e98b8`) is set and the debug switch `"DisableTutorial"` (`0x230260(0x360208)`)
is not; `|0x08` when not in test-park mode (`0x153410` = `0x2b72a8 == 0`). So a normal park is
`0x7f` or `0x3f`, a test park `0x77`/`0x37`.

Other writers: minigame start `0x152668` saves flags and sets **6** (voice only: no stack text, no
queue mode, no rules, no tutorial, no cooldown); `0x1526d0` restores. Minigame object ctor
`0x1dd588` clears `0x20`, its dtor `0x1dd8e8` sets it. Tutorial replay `0x107c18` clears `0x21`;
the end of any modal message sets `0x21` (state 4). Options toggle `0x13a178` flips `0x40`.

### 1.3 Queue slot "kind" (READ `0x107640`, `0x107760`)

`kind = has-object ? 2 : (id − 208 < 67 ? 3 : 0)`. It travels with the message and becomes the
message-stack record's type (§5): 2 = linked to an object (Select jumps the camera there),
3 = tutorial (Replay), 0 = plain. Type 4 is used only by goal notices (§5.5).

---------------------------------------------------------------------------------------------------

## 2. Submitting

```
0x107cc0(msg):  0x1075f0(*0x2aa720, msg)
0x1075f0(adv, msg):                                   // READ MIPS 0x1075f0..0x10763c
    if (adv.flags & 0x20) == 0  or  (u16)(msg.id - 0xd0) < 0x43:  AddImmediateMessage 0x107640
    else:                                                          QueueMessage 0x107760
```

### 2.1 AddImmediateMessage `0x107640` (READ; debug name from string `0x358a20`)

```
if state == 2:   countdown = 50; state = 4; 0x106600(adv, 0)        // exit anim, normal
elif state == 3: if stream playing: stop it (0x111d78)              // "SHUTTING UP ADVISOR in
                 handle = 0; countdown = 50; state = 4;             //  TheAdvisor::AddImmediateMessage"
                 0x106600(adv, 1)                                   // exit anim, cut short (flag 2)
pending = {id, kind, object}; pendingFlag(+0xb4) = 1                // overwrites any earlier pending
```

Consequences (READ from the state machine):

| state when it arrives | effect |
|---|---|
| 0 start delay | kept; plays after the delay |
| 1 idle | plays next tick (it is checked before the ring) |
| 2 entering | the entering message is abandoned **but stays in the ring** (it is popped only on entering state 3) and plays later; an entering *immediate* is overwritten and lost |
| 3 speaking | speech stopped, head cut out, the message is **lost** |
| 4 exiting | exit finishes, then state 5 |
| 5 cooldown | with flag `0x10`: cooldown ends next tick |

### 2.2 QueueMessage `0x107760` (READ MIPS)

```
i = tail
while i != head:  if slot[i].id == msg.id: return           // dedup only against still-queued
                  i = (i + 1) % 20
slot[head] = {msg.id, kind, msg.object}
head = (head + 1) % 20
if head == tail:  tail = (tail + 1) % 20                     // "*** ADVISOR MESSAGE QUEUE OVERFLOW! ***"
```

Empty is `head == tail`, so at most **19** are held; the 20th insert drops the oldest. No
priority, no replacement of what is playing; the message being played and the pending immediate
are not checked by dedup. The overflow print `0x107e48` is a stub.

---------------------------------------------------------------------------------------------------

## 3. Draining and timing

### 3.1 Who calls the update (READ `0x151800`)

`0x151800` (scene update; one simulation pass) runs, while the park is up (`0x395284 == 3`),
`0x3952d8 == 0` (set by `0x1524d8`, which also clears the advisor's init flag `0x2aa6fa`; INFERRED
"leaving"), no exit requested (`0x2b7290`, set by `0x1510f8`) and **not paused** (`0x2b7294 == 0`;
game bit `0x10` = Start toggles it, a disconnected pad for > 6 frames sets it — `0x230ba0`):

```
for n passes (1; 30 with debug "SpeedUp"):
    0x13d438(HUD)            // message-stack update + HUD input
    0x18d7f8
    if [0x395090] == 0:      // no laptop screen open
        0x14efb0; calendar 0x16b060
    guests 0x152a18; objects 0x14be60
    0x1066b0(advisor)        // ← every pass, laptop open or not
0x151c00                     // audio mixer (volume ramps, §4.8)
```

So the advisor keeps running (and speaking, and evaluating rules) under laptop screens, while the
calendar is frozen; it stops completely while paused.

### 3.2 The state machine `0x1066b0` (READ, MIPS `0x1066b0..0x106d80`)

Every tick first: position the head (§4.5), take `dt = min(50, clock − last)` from the pause-aware
clock `0x1498b8` (only the vestigial timer uses it).

| state | does | exit |
|---|---|---|
| **0** start | `countdown−−` (ctor sets 50) | at 0: tutorial event 0 (`0x107390(adv,0)`: WELCOME 268, or 269 in park index 2, once per save); state 1; unless test park: submit **202** `GOALS_ALREADY_ACHIEVED` if `0x3975c8[world][park] == 3`, else **171** `LOST_KINGDOM_FIRST_GOALS` if the lifetime gold-ticket count `0x3975c0` is 0, else **188** (blank, silent); then post the three goal notices (§5.5) |
| **1** idle | if flag 8: run one rule-scheduler step `0x10dc50` (which itself needs `head == tail`). Then if an immediate is pending → take `+0xac`; else if flag `0x20` and ring not empty → take the **tail** slot (not popped yet). For the taken message: if a model exists and its current variant has a sound: start **enter** anim (section 5, record 13, flags 0), apply the variant's costume (§4.6), `+0x230 = 1`. `0x1074c0`: current id; modal handling (§3.3) | state 2 (same tick) |
| **2** entering | duck Music/SFX targets to ≤ 25 (§4.8) | when no model, or channel 0 is end-held (`0x17c880`: flags & 4), or its section is 12 (`0x17c8c8`): if immediate pending → clear it, **play** `+0xac`; else **pop** tail and play it (§4). State 3 |
| **3** speaking | while the stream `+0x23c` plays: vestigial timer; lip step `0x105f30`; mouth shape (§4.4) | stream ended (or never started): if id == 123 → `0x13bdd0` (game over, INFERRED); countdown 50; **exit** anim (record 14, flags 0) if `+0x230`; lips off, mouth 0; state 4 |
| **4** exiting | restore Music/SFX targets from the settings; countdown−− (unused) | channel 0 end-held (or no model, or section 12): if modal → flags \|= `0x21`; countdown = **100**; state 5 |
| **5** cooldown | `+0xb5 = 0`; unlock pad input `0x1817c0(0)`; restore HUD button labels `0x13d870`; countdown−− | countdown ≤ 0, or flag `0x10` clear, or (flag `0x10` and an immediate is pending) → state 1 |

Then, in states 2..4 of a **modal** message (`0x107530`), the skip logic (§3.3).

Cost of one message: ~2 ticks + enter (30 frames) + speech + exit (20 frames) + 100 ticks
(READ; the frame↔tick ratio is not established). A silent message (no voice in its current
variant): no enter/exit, ~3 ticks + 100.

Rules run **only in state 1 with an empty ring**, one rule per tick (§12).

### 3.3 Modal messages and skipping (READ `0x1074c0`, `0x1066b0` tail, `0x1072c8`)

`0x1074c0(adv, id)`: store id; if flag `0x02`: modal ⇔ `id − 208 < 67 or id == 123`. Modal:
`+0xb5 = 1`, **`0x1817c0(1)` locks all game pad input** (`0x181700`, `0x181860` and friends return 0
while `0x2c3588` is set), `0x13d800(HUD)` saves the four HUD button labels and shows
**"Cancel"** (row 28) in slot 0 (Triangle in the standard layout, INFERRED order from
coaster-building.md), clears the skip latch.

Skip (every tick in states 2..4 of a modal message): read the **raw** pad word `0x1817a0(0)`
(bypasses the lock) & mask; mask = game bit `0x200`, or `0x40` when the language index is 8
(JAPANESE) — the same masks as logical button 1 in `0x363f40`/`0x363fa8`. The pad remap `0x230278`
turns libpad bit `0x10` into `0x200` and `0x40` into `0x40`; under the standard libpad bit order
those are **Triangle** and **Cross** (INFERRED names, as in track-ride-tool.md). Pressed → latch `+0xb8`, UI sound 299 (`0x12b`). Released after a press → clear the
pending immediate, **GetTheAdvisorOffTheScreen** `0x1072c8` (state 2 → 4 with normal exit; state 3
→ stop stream, cut exit, 4), and if the id is 123 → `0x13bdd0`.

⚠ The latch is not cleared by the skip, so on the following ticks of state 4 it fires again: any
immediate message that arrives then is cancelled, and 123's hook runs again (READ quirk).

Language 8 is unreachable in this build: the language index comes from the frontend's 4-way
choice through map `0x360630` = {ENGLISH, GERMAN, FRENCH, ENGLISH} (`0x13b1c0 → 0x155908 →
0x1553d8`; ENGLISH when debug "SkipFrontEnd"). READ.

---------------------------------------------------------------------------------------------------

## 4. Playing a message `0x1078e8(adv, slot)` (READ MIPS `0x1078e8..0x107b88`)

Message record = `0x2a6ac8 + 0x38·id` (fields as `AdvisorCatalogue.cs`: `+4` text row, `+6`
variant count, `+7` current variant, `+8 + 12·v` {u16 sound id, u8 costume selector, u8, ptr lip
stem, ptr loaded lip}).

```
if flags & 1 and row != 310:                       // text
    rec = new stack record (0x1089a0): type = slot.kind, row = row, object = slot.object
    0x13cfa8 → 0x108598: append to the message stack (§5)
if flags & 2:                                      // voice + head
    stop a still-playing stream ("* ADVISOR STREAM ISNT STOPPED !")
    v = record.+7
    if sound[v] != 0:
        0x111150(audio, class 0xb, sound[v], &handle)          // §4.1
        lipGate = 1; start = ms clock; lipPtr = loadedLip[v]; mouth = 0
        if handle: len = 0x111990(handle); talk anim = table §4.3, flags 1 (loop)
    record.+7 = (v + 1) % count                    // rotation happens even with no sound
```

The variant rotates only when the voice flag is set. Initial `+7` is `rand % count` for count ≥ 2
(`0x1448e0`, at park load); counts of messages 14, 54, 69 are set per world at load (14: 4 in
world 0 else 3; 54 and 69: 4 in world 3 else 3). READ `0x106338..0x106418`.

### 4.1 Voice (resolved)

- Class 11 bank = descriptor `0x2abf40`, registered by `0x111c30` as **`AUDIO/ADVISOR/<LANG>/`**
  (format `"%s%s/"` `0x359d00` with `"AUDIO/ADVISOR/"` `0x359d08`) and bank name **`SPCH`**
  (`0x359d18`) → `SPCHHD.SDT` / `SPCHBANK.MAP` / `SPCHSFX.MAP`. `<LANG>` = `0x2b74c0[lang]`;
  re-registered whenever the language changes (`0x1553d8`).
- Sound id is 1-based into the SDT (findings/advisor.md). Streamed (the handle is a stream
  handle, "ADVISOR STREAM" string; READ that it is stopped with `0x111d78`).
- On this disc only ENGLISH, FRENCH, GERMAN exist; the PAL language map can only produce those
  three (§3.3), so there is no fallback case in practice.

### 4.2 Lips (resolved)

- Loaded at park start for every variant with a stem: `data\audio\advisor\<LANG>\<stem>.lip`
  (`0x358888`) → `LIPS.WAD/<English|French|German>/<stem>.LIP` (findings/advisor.md). Message 268's
  stem `PS2_` has no file: its lip pointer stays 0.
- `0x105f30` (per tick in state 3): no track → returns 0 (gate stays as set = 1). Mark
  `0xffffffff` → gate 0, return 1. Else if `mark/1000 < now − start` (ms, unsigned) → next mark,
  gate ^= 1, return 1. At most one mark per tick.

### 4.3 Talk animation by speech length (READ MIPS `0x107a9c..0x107b1c`)

`len` = the stream record's length (`0x111990` → `0x24f258`). The thresholds equal the APS
record durations × 1000/30, so `len` is ms (INFERRED):

| len (ms) | record | its length |
|---|---|---|
| < 1666 | 0 | 50 f |
| < 2500 | 1 | 75 f |
| < 3333 | 2 | 100 f |
| < 4166 | 3 | 125 f |
| < 5000 | 4 | 150 f |
| < 7500 | 5 | 225 f |
| < 9166 | 6 | 275 f |
| < 10000 | 4 | |
| < 10833 | 5 | |
| ≤ 11000 | 6 | |
| > 11000 | 3 | |

Played looping (flags 1). Records 7..12 are never requested.

### 4.4 Mouth (READ `0x106b54..0x106bec`, `0x105fc8`, `0x106d88`)

Five meshes found by name (case-insensitive `0x29d808`): shape 0 `mouth - normal`, 1 `mouth - aah`,
2 `mouth - eee`, 3 `mouth - ooh`, 4 `mouth - sss` (strings `0x36a508..0x36a548`; meshes 5..9 of
`advisor.mps`, textures `Mouth1a..e`). `0x105fc8(shape)` clears bit `0x8000` of the chosen mesh
record's word 0 and sets it on the other four (show one, hide four; the hide meaning is INFERRED
from this exclusive pattern).

Each tick in state 3:

| lip step | gate after | shape |
|---|---|---|
| a mark was consumed | 1 | 1 (aah) |
| a mark was consumed / terminator | 0 | 0 (normal) |
| nothing consumed | 1 | with probability 1/5 (`rand % 5 == 0`): `(shape + (rand & 3) + 1) % 5`; else unchanged |
| nothing consumed | 0 | 0 |

So a message without a lip track (268) flaps randomly for its whole length (READ consequence).
Start of speech and every exit set shape 0.

### 4.5 The head: model, place, size (READ `0x106080`, `0x1066b0` top; model data from the disc)

- `0x17bb48(0x1e8)` then `0x230a98()` + `vt+0xc(0x1e8, 0, −1)`: registry `0x2bf2b8` entry 287 =
  {name `Advisor`, world 4 (any), category 13, id 488}; category 13 = flags `0x80`, folder
  `Generic\Advisor` (`0x2bf218+13·8`). Files: **`DATA.WAD/Generic/Advisor/advisor.mps`**
  (32 meshes, 4 helpers `Position Dummy`, `Head Dummy`, `RHand Dummy`, `LHand Dummy`),
  **`advisor.aps`** (only section 5, 15 records, all morph/rigid, no skinning), textures in
  `Generic/Advisor/textures/`.
- Drawn: registered once in the ctor (`0x105db8 → 0x17c9d8`, scene `vt+0x24(…, 4, −1…)`); the
  hide function `0x105e00` has no callers, so it is always registered (READ).
- Every tick: transform `*(model+0xc)+0x70`: `+0x40 = 0.6`, `+0x44 = −0.5`, `+0x48 = 0`
  (globals `0x2aa714/718`, only reader `0x1066b0`), scale `0x16fd18(s, 4s/3, s)` with
  `s = 0.013` (`0x2aa71c`), then `vt+0x6c`. INFERRED: a screen-space overlay, lower right, the
  4/3 compensating a 4:3 aspect. The projection itself is not traced.
- Bind pose = the end of the enter animation (`Bug Head` at 0, `Body` z −21.2), so before the first
  message the head stands "up" (INFERRED: visible) until the first exit lowers it.

### 4.6 Costume (READ `0x107b90`, `0x107160`, `0x1070b8`, `0x106fa8`)

At message start (only if its current variant has a sound) the variant's selector byte → hide all
props, show hands and plain antennae, then:

| selector | shows (fitting id → mesh) | used by (catalogue) |
|---|---|---|
| 5 | 1 Fantasy Helm | TUT_FANT_* |
| 6 | 3 Axe + 16/17 axe antennae | TUT_HALL_* |
| 7 | 7 Handyman Hat | handyman messages |
| 8 | 5 Hardhat | mechanic / breakdown |
| 9 | 8 Kiosk Hat + 14 Spatula (hides right hand) | food, drink, shops |
| 10 | 4 Pith Helmet | LOST_KIN, 171 |
| 11 | 10 Research Hat | research |
| 12 | 2 Space Helmet | TUT_SPACE_* |
| 13 | 6 Security Hat | guards |
| 14 | 9 Ticket Hat | OPEN_PARK, pricing |
| 15, 16, other | none (plain bug) | tutorial, general |

Hats other than Kiosk also hide the plain antennae (19, 20). Fittings: flags `0x401/0x411` in
`advisor.mps`, looked up by `0x1f1f78(model, 0x400, id)`. Never used: 11 Bowler Hat, 12 Bowler
tie, 13 Fast Food Hat, 18 Mutant Eye. A silent message keeps the previous costume (INFERRED from
the branch).

### 4.7 Animations (READ calls; disc data)

`vt+0x5c` → `0x228958 → 0x17c5d8`: category 13 shares the arm that asks the current channel-0
section/record and **does nothing if it is the same**, else `0x1abc80(speed 1.0, section 5,
record, flags)`. Flag 1 = loop, 2 = cut the current record short (bus-native-animation.md,
native-guest-animation-readiness.md).

| use | record | frames | what moves |
|---|---|---|---|
| enter | 13 | 30 | `Body` path z −73.2 → −21.2, `Bug Head` z −52.0 → 0, head pitch −20° → 0 |
| exit | 14 | 20 | reverse to z −74.4 / −53.2, head pitch → −30° |
| talk | 0..6 | 50..275 | `Head Dummy` wobble, hand rotations, eye/shut-eye visibility (blinks) |

INFERRED: rises from below the frame and sinks back.

### 4.8 Ducking (READ `0x1066b0` states 2/4, `0x151c00`, `0x139df0`, `0x1118a8`, `0x1118f0`)

- Settings: Music `0x2abe20`, SFX `0x2abe1c` (slider max `0x80`). Targets: `0x2abe30`, `0x2abe28`.
- State 2, every tick: `if SFX > 25: sfxTarget = 25`; `if Music > 25: musicTarget = 25`.
  State 4: targets = settings.
- `0x151c00` ramps the live volumes toward the targets by 8 per pass: Music → group 1
  (`0x1118a8`), SFX → group 3 and group 0 = `min(3·v, 100)` (`0x1118f0`); `0x24e030` applies
  `v/100`. Which group carries the advisor stream is not established.
- Pause (`0x153888`) sets both targets to 0 and stops the ms clock (`0x11e708`), which freezes lip
  timing.

### 4.9 Vestigial idle timer

`0x107550` adds `dt` to `+0x108` and, each time it passes `0x2aa6f0[+0x225]`
({50, 75, 125, 225, 300}), picks a new random `+0x225` ≠ old. Nothing else reads `+0x225` or
`+0x108` (MIPS scan) — no visible effect (READ).

---------------------------------------------------------------------------------------------------

## 5. Text: the message stack (object `0x3928c8`; READ `0x107ec8..0x109100`, HUD `0x13d4b0`, `0x13d940`)

### 5.1 Structure

| off | meaning |
|---|---|
| `+0` | HUD object (`0x395090`) |
| `+4` | open |
| `+8` | deleting (blocks input) |
| `+0xc` | count (max 32) |
| `+0x10` | cursor (0 = oldest) |
| `+0x14` | scroll top |
| `+0x18` | smoothed scroll (px), `+= (top·70 − it) >> 2` |
| `+0x1c` | index pending removal (−1) |
| `+0x140` | selected record |
| `+0x144 + 0x120·i` | 32 records |
| `+0x2544` | text box widget |

Record (0x120 bytes): `+0` type (0/2/3/4), `+4` state (0, 1 sliding out, 2 selected),
`+8` char[256] own text, `+0x108` s16 text row (−1 = own text), `+0x10c` = 3600 (only copied,
never read: no expiry), `+0x110/+0x114/+0x118` slide counters/offsets, `+0x11c` object.

### 5.2 Adding (`0x108598`)

If count ≥ 32: remove index 0 ("WARNING :: MESSAGE STACK OVERFLOW - REMOVING OLDEST MESSAGE").
Copy to index `count++`; UI sound **0x1e**. New = highest index.

### 5.3 Drawing (`0x108348`, only when no laptop screen: `*HUD == 0`)

- Always: envelope sprite `0x2a` 40×40 at (32, 420) and the count `"%d"` at (80, 430)
  (`0x108d40`, y = 0x1d6 − 40).
- Records: x = 32 + slide, y = 250 − 70·i + scroll; visible when x > 0 and −70 < y < 581, pushed
  left near the edges (y > 258: −2(y−250); y < 70: −2(70−y)); sprite `0x2c` for tutorial records,
  `0x2b` selected, `0x2a` others, framed by `0x108ab0`. Closed: all slide out to −64 in reverse
  order; open: they slide in one by one (counter per record), the selected one to +24.
- Open: the selected record's text in a box (`0x108458`): x = (screenW − 260)/2, y = 210,
  w 260, h 180, margins 16×14, background (48,48,48); text = `translate(row)` or its own text.
- Sprite ids are keys of a runtime sprite table (`0x2156a0`); their files are not resolved here.

### 5.4 Input (standard layout; READ `0x13d940`, `0x1080a8`; buttons via `0x181250`, pad remap `0x230278`)

| button | closed | open |
|---|---|---|
| **L2** (logical 5, game bit `0x800` ← libpad `0x0001`; name INFERRED) | open (if count > 0; cursor and scroll reset to 0 = the OLDEST record; tutorial event 14; sound 0x1f) | close |
| Triangle (logical 1) | — | close (sound 0x1f) |
| Up / Down | — | cursor ±1 (sound 0xd6), scroll keeps it within 4 |
| Circle (logical 10) | — | **Delete**: slide out, then remove (sound 0x1f) |
| Cross (logical 11) | — | type 2 **Select**: camera to the object (`0x125388`) and close; type 3 **Replay**: `0x107c18(row)` |

Labels (`0x13e340`): Close (967), Delete (573), Select (477) / Replay (262) / blank.
Replay `0x107c18`: id = the first ≥ 208 whose row equals the record's (unbounded scan), clears
flags `0x21` (so it plays at once and is not re-added) and submits.
Removing the last record closes the stack.

### 5.5 Other writers of the stack (READ)

- Goal notices `0x16ba58` (advisor state 0 end, not test park): for each goal bit 1..3 not yet
  done, row 376 / 894 / 515 (game text not reproduced) formatted with the goal
  value, type **4** (`0x16b9f8`). Type 4 is not saved.
- Minigame `0x1deac0` shows a record built on the fly in the same box (not added).
- Ride removed (`0x1e12e0 → 0x1088e0`): removes the records whose object is that ride.
- Any object removed (`0x14a7b0 → 0x13d8c0`): marks the **last** type-2 record for removal,
  whatever its object (no comparison; one pending slot). READ quirk.
- Opcode 8 (§6).

### 5.6 Nothing else shows text

No code outside the advisor module reads `+0xac`/`+0xb6` (advw.py census of all 107 uses of
`0x2aa720`). There is no subtitle; the port's "post to status line" is not the console's
behaviour.

---------------------------------------------------------------------------------------------------

## 6. Opcode 8 (`0x10e63c → 0x1073f0 → 0x108808`; READ)

```
0x1073f0(adv, id): if flags & 1 and row(id) != 310:  0x108808(stack, row)
0x108808(stack, row): flush a pending removal; find the FIRST record with record.row == row;
                      if found remove it now ("trying to remove %d" / " removed" / " failed")
```

It is **"retract the old copy from the message log"**, not a UI of its own. On the disc every one
of the 88 rules with a MESSAGE executes TEXT_UI of the same id first (rules 104/105 retract both
133 and 134), so the log keeps one, latest, copy (READ, `advscan rules`). The new copy is added
only when the message is played. Row 310 is skipped because a record with row 310 cannot exist
(`0x1078e8` never adds one) — the search could only fail (INFERRED why). Opcode 7 by contrast is the
full submit of §2. `0x1d5c00` also calls `0x1073f0` (ids 0x7c, 0x7d: row 310, no-ops).

---------------------------------------------------------------------------------------------------

## 7. Tutorial dispatcher `0x107390(adv, event)` (READ `0x206358`, jump table `0x36c3a0`, vtable `0x36c220`)

If flag `0x40`: `event < 74` → `tutorial.vt[slot](tutorial)`. A live handler submits its message
once and sets its done byte (tutorial object `+8..+0x20`); the byte block is saved/loaded with the
global save (`"savetutorial"`/`"loadtutorial"`, `0x205900`/`0x2058a8`, via `0x1c1fc8`/`0x15fe90`).
All other events hit `return 0` stubs.

| event | done byte | message | caller |
|---|---|---|---|
| 0 | `+0x1b` / `+0x20` | 269 WELCOME_COASTER_PARK (park index 2) / 268 WELCOME_MAIN | advisor state 0 |
| 9 | `+0xc` | 225 TUT_RIDE_EXIT | `0x127e80` |
| 11 | `+0xb` | 224 TUT_RIDE_QUEUE | ride place `0x1269b0`, `0x126b58` |
| 15 | `+0xf` | 231 TUT_LAPTOP_MAIN_MENU | `0x16e438` |
| 16 | `+0x13` | 244 TUT_RESEARCH_SCREEN | research screen `0x1b5410` |
| 21 | `+0x14` | 248 TUT_AWARDS | `0x1861b0` |
| 31 | `+0xe` | 228 TUT_STAFF_HIRE | build menu `0x197c40` |
| 42 | `+0x10` | 234 TUT_RIDE_UPGRADE | ride screen `0x1d5c00`, `0x1d5dc8` |
| 43 | `+8` | 235 TUT_SHOP_INFO | `0x1d6f68` |
| 44 | `+0x11` | 237 TUT_SIDESHOW_INFO | `0x1d8118` |
| 50 | `+0xd` | 226 TUT_ITEM_HIGHLIGHT | object loop `0x14be60` |
| 61 | `+0x1e` | 262 TUT_PATHED_RIDE_BLUEPRINT | build menu `0x197f48` (kind 6) |
| 62 | `+0x1f` | 263 TUT_PATHED_RIDE_UPGRADE | `0x129cf0` |
| 66 | `+0x17` | 257 TUT_FIRST_PERSON | `0x14f820` |
| 67 | `+0x16` | 223 TUT_BLUEPRINT_PLACEMENT | `0x126490` |
| 68 | `+0x18` | 172 MINI_GAME_ALERT | `0x137040` |
| 69 | `+9` | 270 TUT_PYLON_PLACEMENT | coaster place `0x11a858` |
| 70 | `+0x12` | 271 TUT_PYLON_EDIT | `0x11b550` |
| 71 | `+0x15` | 272 TUT_COASTER_LOOP | not called by any `jal` found |
| 72 | `+0x19` | 273 TUT_COASTER_COMPLETE | not called by any `jal` found |
| 73 | `+0x1a` | 274 COASTER_STATS_2 | coaster finish `0x11bbd8` |

⚠ Ghidra drops the submit calls in `0x205f18` and `0x205fc8`; the MIPS has them (events 43, 69).
`vt+0x120` (`0x205b00`, message 228) is unreachable. Calls with stub events: 2..6, 8, 10, 12..14,
17..20, 22, 24..26, 29, 30, 32..41, 46, 51, 57, 58, 60, 63..65 (`out/tutcalls.txt`).

---------------------------------------------------------------------------------------------------

## 8. Direct emitters: census (READ; 59 `jal 0x107cc0` = Ghidra's 59 xrefs; no data-word or `lui/addiu` pointer to any submit function)

Voice/text: **V** voice, **T** text row ≠ 310, **—** silent (no voice, row 310).

| function | when | id(s) | V/T | obj |
|---|---|---|---|---|
| `0x10e610` VM opcode 7 | rule completes a MESSAGE | operand | — | |
| `0x1066b0` state 0 | 50 ticks after park start, not test park | 202 / 171 / 188 | V / V / — | |
| `0x205958..0x206128` | tutorial events (§7) | 172, 223..274 | mixed | |
| `0x107c18` | stack Replay | 208..274 by row | | |
| `0x1164d0` | ride status → 4 (ordinary `0x1b8c28`, coaster `0x1229a0`); +event counter 0 | 0x36 BREAKDOWN_IMMINENT | T | ride |
| `0x116550` | ride status → 5 (vtable slots `0x35a784`, `0x35b284`, `0x36a134`, `0x36be14`; ordinary via `0x1b8bf0`); +counter 1 | `0x103658`: 0x37 when no mechanics (`0x14d6b0() == 0`), else 0x39 or 0x38 by the mechanics' states (port `Mechanic.cs:453`) | 0x37 V+T, 0x38/0x39 T | ride |
| `0x1e1cf0` | ride Life setter (`vt+0x2c4`) crossing from ≥ 1 to ≤ 0 | 0x87 RIDE_CONDEMNED | T | ride |
| `0x16c120` | monthly, per staff type with staff, striking → strike ends | `0x2a+b` STRIKE_END_BAD | — | |
| `0x16c2a0` | monthly ladder (b = {mech 0, handy 1, guard 2, ent 3, res 4}, table `0x361d98`) | 0x16+b UNHAPPY (V+T), 0x1b+b, 0x20+b, 0x2a+b (—), 0x25+b HAPPIER (V+T) | | |
| `0x16b060` | month end with negative balance: red-months 1 / 3 / 4 | 206 then 121 / 122 / **123 (modal, game over)** | 206 V, 121/122 T, 123 — | |
| `0x16bb38` ← `0x16bc70`: day counter `cal+0xc` changed and `% 7 == 0`; goals record present, not test park; each once (goal bits `cal+0x24`, hidden-award bits), each +1 gold ticket and sound 0xc5 | goal+0xc ≤ cal+0x20 → 0x9d; money `0x100e40` > goal+0x10·10 → 0x9e; park open and (month+12·year − start)/12 ≥ goal+0x14 → 0x9f; goal+0x30 set, `0x14cf88/58/28` true and four counts sum > 1 → 0xaa; hidden: `0x104ce0(0x40)` > 0x50 → 0xa0; `0x153248` > 7 and `0x16bb80` → 0xa1; `0x153248` > 7 and `0x16bbd0` ≥ goal+0x18 → 0xa2; `0x152fb0(5,2)` → 0xa3; `0x153248` > 9 and `0x1531d8` ≤ goal+0x1c → 0xa4 (meanings of the predicates not traced) | V+T each | |
| `0x1b74a0` ← `0x1b7388` | research project complete | cat 1/3/6/7: level < 2 → 0x4b, else no mechanics → 0x7e, else 0x4c; 2 → 0x4f; 4 → 0x4d; 5 → 0x4e; 8 → 0x4c; other → **id 276** | T (0x7e —) | |
| `0x128918` | hire tool drop (vt `0x35bd48`+0x2c): that type's count == 5 | 0x5c mech, 0x5a ent, 0x58 handy, 0x5e guard, 0x60 res | T | |
| `0x11a858` | coaster place, stock left 0 (`0x14ccb0`: (park 2 ? 14 : 2) − n) | 201 COASTER_STOCK_OUT | V+T | |
| `0x11ba00` | coaster Triangle, ring not closed | 204 | V | |
| `0x11bbd8` | coaster finish: `+0x144 == 0` / `+0x148 == 0` | 203 / 204 | V | |
| `0x1269b0` | ride place (vt `0x35b830`+0x2c): 15 − rides = 0 / 5 | 0xbe / 0xbd | V+T | |
| `0x126b58` | ride place (vt `0x35b7a8`): 3 − n (`0x14cbf0`) = 0 | 200 | V+T | |
| `0x126cd0` | shop place: 20 − n = 0 / 5 | 0xc0 / 0xbf | V+T | |
| `0x126e18` | sideshow place: 10 − n = 0 / 5 | 0xc2 / 0xc1 | V+T | |
| `0x126fa0` | feature place: 45 − n = 0 / 5 | 0xc4 / 0xc3 | V+T | |
| `0x129100` | track-ride station place: 2 − n = 0 | 199 | V+T | |
| `0x1d5c00` | ride screen "upgrade": no mechanics / mechanics striking | 207 UPGRADE_NO_MECHANICS / 0x7d | V / — | |
| `0x1c7c00` | laptop menu "Open Park" (row 617) | 0x66 PARK_NOW_OPEN | — | |
| `0x1552b0` | park gate object (`0x395220`, vtable `0x360370` +0xfc) opens the park | 0x66 | — | |
| `0x1de1f0` | minigame won: first win per save (not test park) / else | 179 / 177 | — | |
| `0x1de2d8` | minigame lost | 178 | — | |

Silent entries still occupy the advisor for ~100 ticks and block the rules meanwhile.

---------------------------------------------------------------------------------------------------

## 9. Event counters passed on the way (for the rules agent)

`0x1073c0(adv, n, δ)` → `0x10ddd8(rules, n, δ)` only with flag 8. Call sites and `n` (READ
`calls.py`): `0x116518` 0 (breakdown imminent), `0x1165a0` 1 (breakdown), `0x20d1fc` 2,
`0x21059c` 3, `0x20e848` 5, `0x20e6dc` 6, `0x20e8ac` 7, `0x1377a8` 8, `0x20e9ec` 10,
`0x20e9d0` 12, `0x20ed1c` 13, `0x1d1ee4` 14, `0x1d1f38` 15, `0x1d1f00` 16, `0x1d1f1c` 17,
`0x1d2bfc` 18, `0x210c78` 19, `0x20d20c` 20, `0x20d4fc` 21, `0x20cec0` (register).

---------------------------------------------------------------------------------------------------

## 10. Port cross-check (`~/tpw-coasters/core`, every `Advisor?.Invoke(` and advisor constant)

Matches: ride 0x36 / 0x37..0x39 / 0x87 with the ride attached (`RideService.cs:261,291,300`);
strikes (`ParkStaff.Management.cs:77..127`, offsets `{3,0,2,4,1}`); ADD_MAX at 5
(`ParkStaff.cs:277`); research (`ResearchManager.cs:161`); security award 0xa0
(`ParkManagement.cs:103`).

Mismatches and gaps:
1. `Mechanic.cs:481-492`: "the two texts are blank rows, so the player hears only the sound" —
   **207 has a voice** (sound 37, `PS2_18`); only 0x7d is silent.
2. `ResearchManager.cs:168`: "any other category posts an EMPTY message" — it posts **id 276**
   (ctor default), past the table (INFERRED unreachable: categories 1..8 are all handled).
3. `game/Viewer.Management.cs:18,67`: messages go to the status line; the console never displays
   the text while speaking — it goes to the message stack (§5), and a voice-only message is still
   *spoken* (the viewer logs it as silent).
4. Not in the port: startup 202/171/188 and goal notices; finance 206/121/122/123
   (`ParkManagement.cs:17` says so); goals/awards 0x9d, 0x9e, 0x9f, 0xaa, 0xa1..0xa4 (only 0xa0);
   stock messages 0xbd..0xc4, 199, 200, 201; coaster 203/204 (the viewer prints them,
   `Viewer.Coasters.cs:677`); 207/0x7d are returned as enum values, not posted; 0x66; minigame
   177/178/179; every tutorial message; message-stack Replay.
5. `RideService.cs` posts status-5 messages for every class except None; the console reaches
   `0x116550` through four vtable slots (which classes own them was not traced here).

---------------------------------------------------------------------------------------------------

## 11. Options, save/load, park change

- **Game Options** (`0x139df0`, labels `0x2b6288..`): Music (399), SFX (823), **Tutorial On/Off**
  (210/843, keys `STR_FRONTEND_SPEECH_ON/OFF`; toggles flag `0x40` and the profile flag
  `0x2e98b8`), Vibration, Save Game, Quit Current Game. The frontend options show the same tutorial
  flag (`0x180098`). No advisor on/off, no speech volume.
- **Message log**: the message stack is the only re-readable history (§5); 32 records.
- **Park start** (`0x151498`): new TheAdvisor (empty ring, state 0, 50 ticks); HUD init
  `0x13ce70 → 0x107f90` empties the stack; then the park's saved state is loaded if present
  (`0x1c30e8 → 0x15fbf8`, which reads the stack last, `0x1608e8`), then the global block
  (`0x1c3208 → 0x15fcf0`) incl. the tutorial done bytes.
- **Park end** (`0x150e80`): save park state (`0x1c30a0 → 0x1c1b08`, stack last `0x1c2d50`),
  TheAdvisor::Delete `0x106488` (stops speech, frees model, rules, lips), free.
- Stack save format: u8 count (type ≠ 4 only); per record: align 2, s16 row, if −1: u8 len + text;
  u8 type; u8 object kind or 0xff; if type 2 with object: + u8 index in its kind's list
  (`0x153ff8`/`0x153db0`).
- **Not saved** (READ: no save/load function touches TheAdvisor except the tutorial block): the
  queue, the pending/current message, the state, the rule VM state (§12).
- Leaving the park (`0x1510f8`: Save Game, Quit, Close Park) and minigame start call
  **GetTheAdvisorOffTheScreen** `0x1072c8`.

---------------------------------------------------------------------------------------------------

## 12. Passed on for the rules agent (READ unless marked)

- The scheduler `0x10dc50` runs only in advisor state 1 with flag 8 and returns early unless the
  ring is empty; with a message playing or cooling down no rule is evaluated.
- The rule object lives inside TheAdvisor and is rebuilt on every park start; nothing saves it.
- Every rule message is TEXT_UI + MESSAGE of the same id (§6).
- Test parks have no rule VM (flag 8 clear).

---------------------------------------------------------------------------------------------------

## Still unknown (what was tried)

- **Screen projection of the head** (§4.5): the 0.6/−0.5/0.013 transform and render layer 4 are
  READ; the renderer's use of them and of camera `+0x118` was not traced.
- **Which audio group plays the advisor stream**, so whether ducking touches it (§4.8): the group
  numbers are READ from `0x24f130/0x24f1c0/0x24eef0`; the stream's group assignment is not.
- **Tick ↔ 30 fps frame ratio**: the timings mix ticks (countdowns) and APS frames.
- **What channel section 12 means** in the state-2/4 tests (`0x17c8c8`): the advisor APS has no
  section 12; INFERRED an idle/none value.
- **Pool resource `+0xc0`** (key 0): handle class `0x359a78`; payload not identified.
- **HUD sprites** `0x2a/0x2b/0x2c/0x2f/0x30/0x32`: runtime sprite table `0x2eeff4`; files not
  resolved.
- **Events 71/72**: handlers exist; no caller found by `jal` census, data-word or `lui/addiu` search
  for the dispatcher (`0x107390` has 59 `jal` sites, all listed).
- **Goal status `0x3975c8[world][park] == 3`** and the language field `+0x50`: named from use only.
- **Whether speech keeps playing under pause**: the pause stops the advisor update and the ms clock;
  no stream pause call was found in the advisor code.
