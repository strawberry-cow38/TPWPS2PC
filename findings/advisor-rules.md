<!-- Research notes from the advisor 'what -> how/why' pass (2026-09-28), copied from ~/ghidra_tpw/notes. Paths under ~/ghidra_tpw and scratchpad refer to the research box. -->

# Advisor, area R: the RULES side -- when a rule runs and what its variables mean

Agent `advR`, 2026-09-28. PAL `SLES_500.32`, Ghidra copy `~/ghidra_tpw/agent_advR`, decompiles in
`~/ghidra_tpw/agent_advR/out/r1.c .. r6.c`, MIPS from `r5900dis.py` / the full listing. Rule dump:
`out/list-rules.txt` (the port's own `TPW.PS2.AdvisorAudit --list-rules`, PASS), rendered with variable
names in `out/rules-rendered.md`; the 275-message table with English text in `out/messages.tsv`
(scratch dumper `out/msgdump/`, reads the disc through the port's DLL; nothing built in the repo).

READ = seen in decompile/MIPS/data at the cited address. INFERRED = reasoned. Port names are from
`~/tpw-coasters` (tinyclaw/next, 29049a5), read only.

This area stops where the interpreter asks for message M (opcode 7 `0x107c90/0x107ca8/0x107cc0`, opcode 8
`0x1073f0`). What I saw on the way is in section 9 for the messaging side.

---------------------------------------------------------------------------------------------------

## 0. Short version

- **One advisor update per simulation pass, i.e. per frame, only in the running park scene** (`0x151800`
  state 3 -> `0x1066b0`, after guests `0x152a18` and objects `0x14be60`). Not while paused (`[0x2b7294]`),
  not while leaving the park (`[0x2b7290]`), not after scene slot `+0x34` (`0x1524d8`) set `[0x3952d8]`.
  The "SpeedUp" 30-pass branch is dead (`0x230260` is a stub returning 0). READ.
- **The scheduler `0x10dc50` runs only while the advisor is idle (state 1) and flags bit 3 is set.** Bit 3
  is set at park start unless the park is the Rollercoaster Test Park (`[0x2b72a8]`), and is cleared for
  the whole of ride-along mode (`0x152668` saves the flags and writes 6; `0x1526d0` restores;
  "ride-along" is an INFERRED name for the mode `0x1debc8` starts, entered from `0x124434` and the
  cursor's "Ride" prompt `0x13782c`). No option clears it: "Tutorial On/Off" flips bit 6 only. READ.
- **Per call: refresh ONE state variable (5 per call for the first 16 calls), then, if the advisor's
  20-slot ring is empty, consider ONE rule, round robin.** A rule is due when `next < day` (u32). Result 0
  -> `next = day + delay`; 1 -> `lastFail = day`; 2 -> nothing. The cursor advances whether or not the
  rule was due, but NOT when the ring gate fails. READ.
- **"Held > N days" (opcode 9) means "no comparison of this rule has failed at any evaluation for more
  than N days"**: every one of the 50 rules with opcode 9 puts it after all its comparisons, and no rule has
  an effect before a comparison. READ from the dump.
- **Day = `cal+0x10` = the port's `ParkClock.TotalDays`**, +1 per `0xF0000` frame units. Frame units per
  pass are `min(0x4000, 128 x 10000)` = `0x4000` (READ `0x11e758`, `0x11e688`, `0x1c4aa8`), so **a day is
  60 passes**, not the 240 of `ParkClock.TicksPerDay` (see §3 -- a conflict in the port, flagged).
- **All 79 variables resolved** (table §5). Two are not what their rules expect: **v52 is a copy of v75**
  (ticket-price counter) and **v53 is a copy of v76** (stink-bomb counter): the producer table sends 52 to
  the counter path with index -4, and case 53 has no `break` and falls into it (MIPS `0x10e458`). READ.
- **All 22 counters' writers found by census** (§6); counter 4 has no writer, counter 8 no reader.
- **106 rules in plain terms** in §7. 18 are pure bookkeeping (resets), **50 post a message with no text
  row and no voice**, 15 text only, 1 voice only (rule 50), 22 text + voice. Five rules read a variable
  whose READ meaning contradicts the message text: 1, 12, 50, 76, 87.
- **Nothing of the rule state is saved.** The rule object is rebuilt at every park start, and a saved
  park's calendar is restored AFTER it is built, so every rule of a resumed park starts with `next = 0`,
  `lastFail = 0` and `v78 = (s16)day`. READ order (`0x151688` build, `0x15172c` load).

---------------------------------------------------------------------------------------------------

## 1. Who runs the scheduler, and how often

### 1.1 Callers (census: `jal` + ELF word search + `lui/addiu` pairs; all READ)

| routine | callers | nothing else |
|---|---|---|
| scheduler `0x10dc50` | one: `0x106884` in advisor update `0x1066b0`, state 1 | no data pointer, no `lui/addiu` pair |
| loader `0x10da78` | one: `0x1062ec` in advisor ctor `0x106080` | same |
| advisor update `0x1066b0` | one: `0x151964` in park-scene update `0x151800` | same |
| advisor ctor `0x106080` | one: `0x151688` in park start `0x151498` | same |
| advisor teardown `0x106488` | one: `0x150ff0` in park teardown `0x150e80` | same |
| table free `0x10dc20` | one: `0x106554` in `0x106488` | |
| counter add `0x10ddd8` | one: `0x1073d8` in `0x1073c0` (20 callers, §6) | |

### 1.2 The frame chain (READ, MIPS `0x10eec0`, `0x1518a0..0x151990`)

```
0x10eec0 (main loop, once per frame)
  0x11e758      [0x2acab4] += 10000 unless [0x2acabc]            (timer)
  0x13af68 -> 0x1c4aa8   D = [0x397640] = min(0x4000, ([0x2acab4]-prev) << 7)
                         -> scene vt+0x24 = 0x151800 (scene vtable 0x3602f0)
0x151800, park state [0x395284] == 3:
  if [0x3952d8]          -> 0x1c4c28 / 0x1c4ce8 only (no sim)     set by scene vt+0x34 = 0x1524d8
  elif 0x151248() ([0x2b7290], set by 0x1510f8 "leave park")      -> nothing
  else 0x230ba0()   pause handling: pad bit 0x10 toggles 0x153888; controller absent > 6 frames pauses
       if 0x153930() ([0x2b7294] paused)                           -> nothing
       else repeat n = (0x230260("SpeedUp") ? 30 : 1) = 1 times:
            0x13d438(0x395090)      UI manager update
            0x18d7f8()
            if [0x395090] == 0:  0x14efb0() (weather),  0x16b060(cal) (calendar, month/week work)
            0x152a18()              guests
            [0x2eeb9c] = 0; 0x14be60()   objects
            0x1066b0([0x2aa720])    ADVISOR UPDATE
       0x151c00()
```

So the advisor update runs **once per sim pass = once per frame** while the park runs, and **it does not
test `[0x395090]`**: when that word is non-zero the calendar (and objects) stand still but the advisor
update still runs (READ). The word's meaning is not identified (§11); the port's other notes call it the
object-freeze test.

### 1.3 The advisor update's states (only what gates the rules; READ `0x1066b0`, table `0x358930`)

| state `+2` | what it does | exit |
|---|---|---|
| 0 | countdown `+4` (50 at ctor) | at 0: tutorial event 0 (`0x107390`), state 1; unless test park, post a greeting (0xca if `0x1c3790(world,map)==3`, else 0xab if `[0x3975c0]==0`, else 0xbc) and `0x16ba58(cal)` (goal texts) |
| **1 idle** | **if flags bit 3: `0x10dc50(+0x264)`**; then if `+0xb4` (immediate message pending) present it; else if flags bit 5 and ring non-empty, dequeue and present | -> 2 |
| 2..5 | presentation (messaging side) | back to 1 |

The scheduler therefore never runs while a message is on screen (states 2..5). A rule whose message is
queued in call N is dequeued in the same update N (the dequeue follows the scheduler call).

### 1.4 Gates on the rules, all states (READ unless marked)

| condition | scheduler | counters (`0x1073c0`) | where |
|---|---|---|---|
| park not in scene state 3 / leaving / `[0x3952d8]` | no | writers still run if their code runs | `0x151800` |
| paused `[0x2b7294]` | no (no sim pass) | no (no sim) | `0x1518e8` |
| `[0x395090] != 0` | **yes**; day frozen | yes | `0x151930` guards only weather+calendar |
| advisor busy (state != 1) | no | yes | `0x1066b0` |
| flags bit 3 clear (test park; ride-along mode) | no | **no** (`0x1073c8` tests bit 3) | `0x106874`, `0x1073c0` |
| ring head `+0xa8` != tail `+0xa9` | refresh only | yes | `0x10dcfc..0x10dd08` |
| first 16 calls after construction | refresh only (5/call) | yes | `0x10dc6c..0x10dcf4` |
| tutorial on/off (bit 6) | no effect | no effect | nothing in `0x10dc50`/`0x1066b0` state 1 tests it |
| free-build (`[0x2a60b8]`) | no effect | no effect | no read of `0x60b8` in `0x106000..0x10e6a0` |

---------------------------------------------------------------------------------------------------

## 2. Construction and reset

### 2.1 `0x2aa720` holds the ADVISOR, not the rule object

Park start `0x151498` (READ): `flags = 0x37`; `if 0x1c3940() ([0x2e98b8], the Tutorial option) &&
!0x230260("DisableTutorial") (stub, 0)` -> `0x77`; `if !0x153410() ([0x2b72a8], test park)` -> `|= 8`.
Allocates 0x270 bytes, `0x10f600(adv+0xc0)`, `+0x110 = +0x114 = 0`, `0x115ae8(adv+0x118)`,
**`[0x2aa720] = adv`** (the only writer, `0x151684`), `0x106080(adv, flags)`. Later in the same function:
`0x100690(park, 300000)` (opening balance), **`0x1c30e8(world, map)`: if `0x1c3670` -> `0x1c4348` ->
chunk loader `0x15fbf8` -> `0x160da0` -> calendar load `0x16d030`, which restores
`cal+0x04/08/0c/10/18/1c/20` and zeroes `+0x14`** (READ chain; "resume a saved park" INFERRED from the
chunk loader), then `[0x2b7288] = 1` (park running).

Advisor flags byte `adv+0` (bits READ from their readers; names INFERRED where marked):

| bit | reader | effect |
|---|---|---|
| 0 (1) | `0x1073f0` | opcode 8 (text) acts only when set |
| 1 (2) | `0x1074c0` | messages 0xd0..0x112 and 0x7b become modal (`+0xb5`) |
| 3 (8) | ctor, `0x1066b0`, `0x1073c0`, `0x106488` | the rule object exists / is run / counts events / is freed |
| 4 (0x10) | state 5 | early return to idle when a message is pending |
| 5 (0x20) | `0x1075f0` | queue (set) or immediate (clear) submission |
| 6 (0x40) | `0x107390` | tutorial events delivered to `adv+0x268` |

Writers of `adv+0` (census of every `lw -0x58e0(..)` followed by `sb ..,0(..)`, plus the ctor): ctor
`0x106080`; `0x107c18` (`&= 0xde`, tutorial message); state 4 of `0x1066b0` (`|= 0x21` after a modal
message); `0x13a178` (Tutorial On/Off: flips 0x40 and writes `[0x2e98b8]` via `0x1c3950`); `0x152668`
(ride-along: `0x1072c8` off screen, `0x1072a0` save to `adv+1`, **write 6**); `0x1dd588` (`&= 0xdf`) /
`0x1dd8e8` (`|= 0x20`), 10 paired call sites (`0x193144`/`0x193458`, then `0x1c989c..0x1d031c`;
INFERRED minigame screens). `0x1526d0` restores via `0x1072b0` (`adv+0 = adv+1; adv+1 = 0xff`).

### 2.2 The rule object (0xec bytes, `0x17a370(0xec)`, pointer at `adv+0x264`), READ

| offset | type | meaning | writers |
|---|---|---|---|
| `+0x00 + 2i` | s16 x 79 | state variable i (v78 at `+0x9c`) | loader (0), producers `0x10de38`, scheduler (`+0x9c`), interpreter SET |
| `+0x9e + 2j` | s16 x 22 | event counter j (= v(56+j)) | loader (0), `0x10ddd8`, SET mirror `0x10e60c` |
| `+0xca` | u16 | variable refresh cursor 0..78 | loader, scheduler |
| `+0xcc` | u16 | rule cursor 0..105 | loader, scheduler |
| `+0xce` | u8 | warm-up done | loader (0), scheduler (1) |
| `+0xd0` | ptr | `headers.ass` buffer (12-byte records) | loader |
| `+0xd4` | u8 | rule count = size/12 = 106 | loader |
| `+0xd8` | ptr | `opcodes.ass` buffer | loader |
| `+0xe0` | u16 | v53 latch: month index when upgrades first researched | loader (0), producer 53 |
| `+0xe4` | u32 | bit 0 = that latch set | loader (0), producer 53 |
| `+0xe8` | ptr | vtable `0x358ae0` (slot +8 -> dtor `0x107e98`) | ctor site `0x1062e8` |

Loader `0x10da78`: zero v0..v78; zero `+0xca, +0xcc, +0xce, +0xe0, +0xe4`; load `Data\Generic\Advisor\
headers.ass` (`0x3597f0`) and `opcodes.ass` (`0x359818`) through `0x131898/0x131c78`; `+0xd4 = size/12`
(`divu` then `sb`); printf "Num advisor rule scripts is %d"; **for every rule: `next(+4) = 0`,
`lastFail(+8) = 0x16b218(cal)`**; zero the 22 counters. Teardown: `0x106488` -> `0x10dc20` frees both
buffers (`0x17a3c0`), then vtable dtor with flag 3 frees the object.

**Nothing else touches the rule object**: its pointer is loaded only at `0x106558`, `0x10655c`,
`0x106888`, `0x1073dc` (census of `0x264(` loads; the other hits are unrelated UI objects that pass their
own `+0x264` to vtable calls). So it is never saved, and its only resets are construction and SET.

### 2.3 New park, load, options (READ; consequence INFERRED)

- **Leaving a park** (`0x150e80`): `0x16aef0` destroys the calendar, `0x106488` the advisor; the next
  park builds both fresh (`0x16af70` zeroes `cal+0..0x20`).
- **New park:** day 0 at construction -> `lastFail = 0`, `next = 0`; v78 counts days since park start
  until a rule's first failure.
- **Resumed park:** the advisor is built at day 0 (`0x151688`) and the calendar restored afterwards
  (`0x15172c`). Every rule is due immediately and sees `v78 = (s16)(D - 0)`; any "held > N" guard with
  `N < D` passes at the first evaluation. Counters restart at 0.
- **Options:** the only advisor-looking option is Tutorial On/Off (text rows 210/843, keys
  `STR_FRONTEND_SPEECH_ON/OFF`); it flips bit 6 only. There is no rule on/off, no reset.

---------------------------------------------------------------------------------------------------

## 3. The day

- **Source:** `0x16b218(0x16ae90())` = `cal+0x10` (READ) = `ParkClock.TotalDays` in the port. The
  scheduler reads it at `0x10dd10..0x10dd1c`; so does the loader.
- **Advance:** `0x16b240` (from `0x16b060`, once per sim pass unless `[0x395090]`): `cal+0 += D`; when
  `> 0xEFFFF`: `cal+0x10 += 1`, `cal+0x14 -= 1`, day-of-month/month/year roll (port `ParkClock.Advance`).
- **D:** `0x1c4920` returns `[0x397640]`, written only by `0x1c4aa8` (`min(0x4000, 0x11e688())`) and
  `0x1c4990` (0, scene init); `0x11e688` = `(counter - prev) << 7`; `0x11e758` adds 10000 to the counter
  once per `0x10eec0` pass unless `[0x2acabc]`. So **D = 0x4000 per pass** and **one day = 0xF0000 / 0x4000
  = 60 passes** (READ arithmetic; agrees with the port's `findings/coaster-trains.md` §2.2 and
  `bus-native-clock-and-audio.md`). ⚠ **The port's `ParkClock.UnitsPerTick = 0x1000` (240 ticks a day)
  disagrees.** At the port's 25 passes a second that is 2.4 s a day, a 60-day rule delay 2.4 minutes.
  Wall-clock pass rate is not established here.
- **v4 "months"** = `cal+4 + 12 x cal+8`, both zero at calendar construction, so it is **months since the
  park was created**, restored on load. `ParkManagement.MonthsElapsed` counts the same thing in one session.

---------------------------------------------------------------------------------------------------

## 4. The scheduler, reimplementable (READ `0x10dc50` decompile + MIPS `0x10dc50..0x10ddd4`)

```
update(ro):                                   // advisor idle, flags bit 3
  if !ro.warm:                                // up to 5 refreshes
      repeat 5: produce(ro, ro.cv); ro.cv = (ro.cv + 1) % 79
                if ro.cv == 0: ro.warm = 1; break
      if !ro.warm: return
  else: produce(ro, ro.cv); ro.cv = (ro.cv + 1) % 79
  if adv.head != adv.tail: return             // cursor NOT advanced
  day = cal.TotalDays                          // u32
  r = rules[ro.cr]
  if r.next < day:                             // unsigned, strict
      v[78] = (s16)(day - (u16)r.lastFail)     // sw at +8, lhu read: width quirk
      res = interpret(r)                       // 0 END, 1 compare failed, 2 elapsed blocked
      if res == 0: r.next = day + (u16)r.delay // u32
      elif res == 1: r.lastFail = day
  ro.cr = (ro.cr + 1) % 106                    // also when not due
```

- `produce(i)` for `i >= 79` does nothing (`sltiu 0x4f` at `0x10de60`); index 78 has no producer
  (`0x10e498` is the common return).
- Consequences (INFERRED arithmetic): a full variable sweep takes 79 idle updates, a rule sweep 106; at 60
  passes a day a rule is looked at about every 1.8 days of idle time, and values are up to 79 updates
  stale. A due rule fires the first time it is looked at after `day > next`, so the real interval is
  `delay + 1` days plus the sweep and busy time.
- Delays 10000..30000 (rules 60, 65, 71, 77, 81, 85, 89, 90, 93, 94, 96) are once per park in practice.

Interpreter facts that matter for scheduling (READ `0x10e4b8..0x10e69c`): unknown opcode words are
skipped one word at a time (`sltiu a0,9` at `0x10e4e4`); SET mirrors to the counter for index `>= 56`
(`slti 0x38`), so SET 78 would write `+0xca` (the refresh cursor) -- no program does it; ADD does not
mirror, and there is no ADD in the programs. All 106 programs' SETs store 0.

---------------------------------------------------------------------------------------------------

## 5. The 79 state variables

Producer dispatch `0x10de38` with jump table `0x359860` (79 words, READ from the ELF). "Held" in the port
column = the port can compute it from a named member; "LACKS" = no port member for it. Pools (READ,
accessors `0x14cba8..0x14d6d0`): `+8` list head, `+0xc` active count; ordinary rides `0x39528c`, tour
`0x395290`, track `0x395294`, coasters `0x395298` (next at `+0x130`), shops `0x39529c`, features `0x3952a0`,
sideshows `0x3952a4`, guards `0x3952ac`, mechanics `0x3952b0`, handymen `0x3952b4`, researchers
`0x3952b8`, entertainers `0x3952bc`, litter `0x3952c0`, guests `0x3952cc`. "Held-on-cursor" = minus 1 while
the build/hire tool `[0x3951d0]` carries one (modes 5 ordinary, 6 tour, 7 track, 0xb coaster, 0xe shop,
0x10 sideshow; 1 + held type for staff).

| v | producer | reads | formula, clamp, units | R/I | port |
|---|---|---|---|---|---|
| 0 | `0x10de88` `0x14e538` | `[0x2b72a4]` | park open 0/1 (set by `0x14e4c0` Open Park, which also stores month index at `[0x2b7298]`; cleared `0x14e528`) | R | LACKS a park-level flag (`CoasterSim.ParkOpen`, `LaptopMainMenu.VisibleMain(parkOpen)` only) |
| 1 | `0x10de98` | guest pool, `0x211c80(g)` | `n = guests; n==0 ? 0 : #{class==1}*100/n` | R | LACKS the classifier; bytes in `VisitorNeeds` |
| 2 | `0x10dee4` | same | class 2 % | R | LACKS |
| 3 | `0x10dfb0` | `cal+0x10` | `min(day, 30000)` (unsigned) | R | `ParkClock.TotalDays` (unread by rules) |
| 4 | `0x10dfd0` | `cal+4`, `cal+8` | `min(month + 12*year, 30000)`: months since park creation | R | `Month + 12*Year` |
| 5 | `0x10e010` | `cal+8` | `min(year, 30000)` | R | `ParkClock.Year` (unread) |
| 6..10 | `0x14d830 0x14d7c0 0x14d750 0x14d7f8 0x14d788` | ent/mech/guard/res/handy pool counts | count, held-on-cursor minus 1 (`0x14d6e0`: `[0x3951d0]==1` and tool `vt+0xac` == type 1..5) | R | `ParkStaff.AdvisorCount` |
| 11 | `0x10e080` | v6..v10 | 16-bit sum | R | `FillAdvisorVariables` |
| 12 | `0x14d690` | guest pool `+0xc` | guests (active pool objects) | R | guest count (no one member; `FillAdvisorVariables` leaves it to the guest side) |
| 13 | `0x14d208` | litter pool `+0xc` | litter objects | R | `ParkLitter.Count` |
| 14 | `0x103970(0xf)` | 4 ride pools `+0xc` | ordinary+tour+track+coaster, each held-on-cursor minus 1; any status | R | partial: `ParkSim.Rides` (no cursor adjustment) |
| 15 | `0x103970(0x100)` | shop pool | shops, held minus 1 | R | partial |
| 16 | `0x103970(0x200)` | sideshow pool | sideshows, held minus 1 | R | partial |
| 17 | `0x103970(0xf0)` | feature list | features with status `+0xa2 != 0`, every class | R | LACKS (`FeatureCount` refuses 0x10) |
| 18/19/20 | `0x103970(0x20/0x80/0x40)` | feature list | toilets (`vt+0x134`) / staff rooms (`0x1308c8`, DBA `+0x2e` bit 1) / cameras (`0x130858`, bit 3), status != 0, classed once in that order | R | `ParkStaff.FeatureCount` |
| 21 | `0x103b20(0xf)` | research DB, 4 ride pools | ride **variety**: `avail` = types i of kinds 3,7,6,1 with `0x12b6d0(db,k,i,0)`; `built` = distinct item indices (`+0x97`) placed with status != 0 (50-slot array per kind; a kind with `avail==0` is skipped); `built >= avail ? 100 : built*100/avail` | R | `AdvisorProducers.VarietyPercent(0xF)` + live catalogue/keyed census |
| 22 | `0x1044b0(0xf)` | research DB | ride **research %**: researched types / all types over kinds 3,7,6,1 (bit 4 also counts kind 1; bit 8 is never tested); `>=` -> 100 | R | `AdvisorProducers.ResearchedPercent(0xF)` (native mask quirk preserved) |
| 23 | `0x104a40` | ride pools, `0x12ba08` | **upgrades in use**: over kinds 3,6,7,1, for each item type with a placed instance: `a += maxTierPlaced` (`obj+0x126`), `b += level-1` (`0x12ba08`); `a >= b ? 100 : a*100/b` | R | `AdvisorProducers.InstalledUpgradePercent()` (MAX per type, any status) |
| 24 | `0x104bd0` | `0x12ba08` | **upgrade research %**: types with `L=level != 0`: `a += 2`, `b += L-1`; `b >= a ? 100 : b*100/a` | R | `ResearchDatabase.UpgradePercent()` via the advisor producer |
| 25/26 | `0x103b20/0x1044b0(0x100)` | kind 4 | shop variety / shop research % | R | `AdvisorProducers.VarietyPercent/ResearchedPercent(0x100)` |
| 27/28 | `(0x200)` | kind 5 | sideshow variety / research % | R | `AdvisorProducers.VarietyPercent/ResearchedPercent(0x200)` |
| 29/30 | `(0xf0)` | kind 2 | feature variety (types classed by DBA `+0x2e`) / research % | R | `AdvisorProducers.VarietyPercent/ResearchedPercent(0xF0)` |
| 31 | `0x104760` | feature list, `+0xb4` | toilets (any status): `n==0 ? 0 : 100 - Σcleanliness/n` (16-bit sum) = average dirtiness % | R; "cleanliness" I (`0x130978` sets 100 on service, `0x130948` subtracts use) | partial: toilet service in `Handyman.cs` |
| 32 | `0x104ce0(0x20)` | features, grid `0x37e218` | toilet coverage, 8x the covered fraction of 16x16 blocks (staff-management §11.4) | R | `ParkStaff.FeatureCoverage(0x20)` |
| 33..36 | `0x1053a8(1,2,4,8)` | mech, handy, guard, ent | % WITHOUT a patrol area: `set < n ? 100 - set*100/n : 0` (`C+0x44 != 0xFFFF0000`) | R | `ParkStaff.NoPatrolAreaPercent` |
| 37..40 | `0x104fb0(1,2,4,8)` | same | patrol coverage of path | R | `ParkStaff.PatrolCoverage` (unread except v39) |
| 41..45 | `0x105538(1,2,4,8,0x10)` | + researchers | `ΣL*100/(4n)`, 0 with none | R | `ParkStaff.TrainingPercent` |
| 46 | `0x16c988(cal,2)` | `cal+0x311` | mechanics striking | R | `IsStriking(Mechanic)` (unread) |
| 47 | `0x10e270` | guest list `+0x75` | `n==0 ? 0 : min(100, (u32)Σhappiness / n)` (`divu`) | R | LACKS (bytes in `VisitorNeeds.Happiness`) |
| 48 | `0x10e2d0` | park `+4` balance (tenths) | `clamp(balance/10/100, -30000, 30000)`: **hundreds of currency units** ($30,000 opening = 300) | R | `ParkFinances.Balance / 1000` |
| 49 | `0x10e32c` | wage/income rings | wages > income in both of the last two months | R | `ParkFinances.WagesHigh` |
| 50 | `0x103718(0x3ff)` | raw pool counts | **park size** = `6*(4 ride pools, incl. cursor) + 4*shops + 4*sideshows + 5*every feature in the list (no status test)`, cap 30000 | R | LACKS |
| 51 | `0x1b69f0(0x1b6798())` | manager `0x2e7540` | 1 if any of the 5 slots `+0x18+0x1c*i` is non-zero (a project running) | R | `ResearchManager.ActiveCount > 0` |
| 52 | default path `0x10e484` | `ro+0x96` | **= v75** (`0x9e + 2*(52-56)`) | R | copy of v75 |
| 53 | `0x10e40c` then falls into `0x10e484` | `ro+0x98` | **= v76**; side effect: first time `0x104bd0() != 0`, `+0xe0 = months`, `+0xe4` bit 0 set (the intended "months since upgrades" is computed and overwritten) | R | copy of v76 |
| 54 | `0x10df34` | guests | class 3 % (toilet need) | R | LACKS |
| 55 | `0x105798` | all staff `C+0x4b` | max tiredness over masks 1,2,4,8,0x10 | R | `ParkStaff.MaxTiredness()` |
| 56..77 | `0x10e484` | `ro+0x9e+2j` | counter j = i - 56 | R | `ParkStaff.AdvisorEvents` (3 of 22 raised) |
| 78 | none | written by the scheduler | days since last failed comparison | R | scheduler |

The need classifier `0x211c80(g)` (MIPS `0x211c80..0x211d24`, signed bytes; field names from
`findings/visitors.md`): hunger `+0x77 >= 81` and thirst `+0x7a >= 81` -> 7; hunger `>= 81` -> 2; thirst
`>= 76` -> 1; toilet `+0x79 >= 76` -> 3; sick `+0x76 >= 76` -> 4; happiness `+0x75 >= 76` -> 5; boredom
`+0x7b >= 76` -> 6; happiness `< 25` -> 8; else 0. So v1 needs thirst >= 76 AND hunger <= 80, v2 hunger
>= 81 AND thirst <= 80, v54 toilet >= 76 AND hunger <= 80 AND thirst <= 75: a guest counts in at most one.

Unread by any rule: v3, v5, v37, v38, v40, v46, v60, v64 (and v78 only through opcode 9).

---------------------------------------------------------------------------------------------------

## 6. The 22 event counters (`0x1073c0(adv, j, d)` -> `0x10ddd8`)

`0x1073c0`: only if flags bit 3. `0x10ddd8`: `0 <= j < 22`; `c = (s16)(c + d)`, then clamp to
`[-30000, 30000]` (MIPS `0x10ddd8..0x10de28`). Census: 20 `jal 0x1073c0` sites, no data pointer; the only
other stores into `ro+0x9e..0xc9` are the loader's zeroing and SET (§2.2). Resets are construction and
the listed rules' `SET 0`.

| j | v | writer (site) | event | d | reset by rules |
|---|---|---|---|---|---|
| 0 | 56 | `0x116518` in `0x1164d0` | ride enters status 4 (breakdown imminent; message 0x36 with the ride attached, sound 0xe2). Called by coaster `0x1229a0` and ordinary `0x1b8c28`; track rides' own enter-4 `0x2002d8` never calls it. Enter handlers run on every set-status, changed or not (`coaster-operation.md` §2.2) | +1 | 57 (300 d) |
| 1 | 57 | `0x1165a0` in `0x116550` | ride enters status 5 (reliability 0; message `0x103658()` = 0x37 no mechanics / 0x39 a free one / 0x38 all busy) | +1 | 58 (300 d) |
| 2 | 58 | `0x20cec0` in `0x20c930` arm 2; `0x20d1fc` in `0x20d010(g,1)` | heckle (`rand(6)==2`, `rand(1000)<[0x2eeb64]`, an entertainer within Manhattan < 5); prank (arm 5) | +1 | 69, 73 (after firing only) |
| 3 | 59 | `0x21059c` in `0x210428` | a queueing guest's impatience `+0x78` reached 81: leaves the queue (`native-ride-queue.md`) | +8 | 5 (180 d), 76 |
| 4 | 60 | none found | -- | -- | -- |
| 5 | 61 | `0x20e848` in `0x20e1a0` | balloon bought (product 3) and handed out (guest had none, `0x14add8` allocated one) | +1 | 6 (60 d), 7 |
| 6 | 62 | `0x20e6dc` | costume bought (product 2) | +1 | 8 (60 d), 9 |
| 7 | 63 | `0x20e8ac` | gift bought (product 6) | +1 | 10 (60 d), 11 |
| 8 | 64 | `0x1377a8` in `0x137040` | cursor rests on a sideshow whose hover label becomes row 11 "Play" (also tutorial event 0x44); every frame it holds | +1 | none (unread) |
| 9 | 65 | `0x20e9d0` | food purchase attempt (products 0,4,7), bought or not | `trunc((W-P)/2)` | 16 (180 d), 17, 18 |
| 10 | 66 | `0x20e9ec` | merchandise attempt (2,3,6) | same | 19, 20, 21 |
| 11 | 67 | `0x20e9d0` | product 5 attempt ("restaurant" by the message names) | same | 22, 23, 24 |
| 12 | 68 | `0x20e9d0` | drink attempt (1) | same | 25, 26, 27 |
| 13 | 69 | `0x20ed1c` in `0x20ec00` | sideshow attempt | `trunc((W-P)/2)` | 28, 29, 30 |
| 14 | 70 | `0x1d1ee4` in `0x1d1e68` | food attempt | `trunc((x-50)/4)` | 31, 32, 33 |
| 15 | 71 | `0x1d1f38` | merchandise attempt | same | 34, 35, 36 |
| 16 | 72 | `0x1d1f00` | product 5 attempt | same | 37, 38, 39 |
| 17 | 73 | `0x1d1f1c` | drink attempt | same | 40, 41, 42 |
| 18 | 74 | `0x1d2bfc` in `0x1d2bb0` | sideshow attempt | same | 43, 44, 45 |
| 19 | 75 | `0x210c78` in `0x210b38` | park entrance attempt that passed `fee < cash`: ticket class 1 cheap / 0 / -1 dear / -2 refused (`NativeEntranceAcceptance.Classify`); added unless "ForceKidsToEnter" (stub 0) | class | 13 (200 d), 14, 15 |
| 20 | 76 | `0x20d20c` in `0x20d010(g,1)` | prank (arm 5: `rand(6)==5`, happiness < `[0x2eeb98]`, `rand(1000) < [0x2eeb68]`) -- the stink bomb | +1 | 46 |
| 21 | 77 | `0x20d4fc` in `0x20d010(g,0)` | arm 3: litter need `+0x74 >= 90` and no bin within Manhattan 5: litter dropped, `+0x74 = 0` | +1 | 47 |

- **W, P, x** (READ `0x20e1a0`, `0x20ec00`, `0x1d1e68`): shop `W = ((cost*125/100) * (thirst*T/100 +
  hunger*H/100 + 100 - sick*V/100 + (100-happy)*E/100) / 100) * (happy+100)/100` with the shop's
  getters; `P = shop+0xb8`; bought iff `P < W` and cash `>= P*10`. Sideshow `W = 0x20eb20` (debug print
  "cp = %d, pv = %d"), `P = 0x1d2a30`. `x = max(0, (happyAfter - happyBefore) * 5)`, so an attempt that
  changes nothing adds -12 and a +10 happiness purchase adds 0. `0x1d1e68` also does shop `+0xb0 += x`,
  `+0xb4 += 1`.
- The port raises counters 2, 0x14 (`ParkStaff.Security`) and 0x15 (litter); `NativeEntranceAcceptance`
  computes counter 19's class but does not feed it.

---------------------------------------------------------------------------------------------------

## 7. All 106 rules in plain terms

Header delay in days; "held > N" = opcode 9; message tags: **TV** text + voice, **T** text only, **V**
voice only, **silent** = text row 310 and no sound. Every rule that speaks has `TextUi M` immediately
before `Message M`; rules 104/105 show two text slots (both row 310, so no-ops). `:=0` = counter reset.

| # | delay | condition, in plain terms | message(s) |
|---|---|---|---|
| 0 | 60 | park 4+ months old, park closed, at least one ride | 0 OPEN_PARK TV |
| 1 | 60 | no cameras, some guards, **v52 (= ticket counter v75) < 10** | 12 STAFF_GUARD_BUILD_CAMERA TV |
| 2 | 2000 | first 2 years, fewer than 4 rides, guests present, park open, held > 120 | 98 ADD_HYPE_NEWPARK silent |
| 3 | 60 | more than 15 guests, more than 10% thirsty, held > 10 | 1 VISITORS_THIRSTY TV |
| 4 | 60 | more than 15 guests, more than 10% hungry, held > 10 | 2 VISITORS_HUNGRY TV |
| 5 | 180 | always: queue-quit counter := 0 | -- |
| 6 | 60 | always: balloons := 0 | -- |
| 7 | 300 | more than 20 balloons since the last reset; := 0 | 114 CONGRAT_BALLOONS silent |
| 8 | 60 | always: costumes := 0 | -- |
| 9 | 300 | more than 20 costumes; := 0 | 132 CONGRATS_COSTUMES silent |
| 10 | 60 | always: gifts := 0 | -- |
| 11 | 300 | more than 20 gifts; := 0 | 115 CONGRAT_GIFTS silent |
| 12 | 10 | more than 1 guest, **average happiness > 1**, held > 10 | 104 CONGRAT_VISITORS_HAPPY silent |
| 13 | 200 | always: ticket counter := 0 | -- |
| 14 | 60 | ticket counter > 1 (net "cheap"); := 0 after the message | 74 PRICING_TICKET_TOO_CHEAP T |
| 15 | 60 | ticket counter < -1 (net "dear"); := 0 | 73 PRICING_TICKET_TOO_EXPENSIVE T |
| 16 | 180 | always: food value := 0 | -- |
| 17 | 192 | > 20 guests, food value < -150; := 0 | 136 PRICING_FOOD_POOR_VALUE silent |
| 18 | 193 | > 20 guests, food value > 150; := 0 | 146 CONGRAT_PRICING_FOOD_GREAT_VALUE silent |
| 19 | 180 | always: merchandise value := 0 | -- |
| 20 | 194 | > 20 guests, merch value < -150; := 0 | 137 PRICING_SHOPS_POOR_VALUE silent |
| 21 | 195 | > 20 guests, merch value > 150; := 0 | 147 CONGRAT_PRICING_SHOPS_GREAT_VALUE silent |
| 22 | 180 | always: product-5 value := 0 | -- |
| 23 | 196 | > 20 guests, product-5 value < -150; := 0 | 138 PRICING_RESTAURANT_POOR_VALUE silent |
| 24 | 197 | > 20 guests, product-5 value > 150; := 0 | 148 CONGRAT_PRICING_RESTAURANT_GREAT_VALUE silent |
| 25 | 180 | always: drink value := 0 | -- |
| 26 | 198 | > 20 guests, drink value < -150; := 0 | 139 PRICING_DRINKS_POOR_VALUE silent |
| 27 | 199 | > 20 guests, drink value > 150; := 0 | 149 CONGRAT_PRICING_DRINKS_GREAT_VALUE silent |
| 28 | 180 | always: sideshow value := 0 | -- |
| 29 | 191 | > 20 guests, sideshow value < -150; := 0 | 140 PRICING_SIDESHOWS_POOR_VALUE silent |
| 30 | 192 | > 20 guests, sideshow value > 150; := 0 | 150 CONGRAT_PRICING_SIDESHOWS_GREAT_VALUE silent |
| 31 | 180 | always: food satisfaction := 0 | -- |
| 32 | 193 | > 20 guests, food satisfaction < -150; := 0 | 141 PRICING_FOOD_POOR_SATISFACTION silent |
| 33 | 194 | > 20 guests, food satisfaction > 150; := 0 | 151 CONGRAT_PRICING_FOOD_GREAT_SATISFACTION silent |
| 34 | 180 | always: merch satisfaction := 0 | -- |
| 35 | 195 | > 20 guests, merch satisfaction < -150; := 0 | 142 PRICING_SHOPS_POOR_SATISFACTION silent |
| 36 | 196 | > 20 guests, merch satisfaction > 150; := 0 | 152 CONGRAT_PRICING_SHOPS_GREAT_SATISFACTION silent |
| 37 | 180 | always: product-5 satisfaction := 0 | -- |
| 38 | 197 | > 20 guests, product-5 satisfaction < -150; := 0 | 143 PRICING_RESTAURANT_POOR_SATISFACTION silent |
| 39 | 198 | > 20 guests, product-5 satisfaction > 150; := 0 | 153 CONGRAT_PRICING_RESTAURANT_GREAT_SATISFACTION silent |
| 40 | 180 | always: drink satisfaction := 0 | -- |
| 41 | 199 | > 20 guests, drink satisfaction < -150; := 0 | 144 PRICING_DRINKS_POOR_SATISFACTION silent |
| 42 | 190 | > 20 guests, drink satisfaction > 150; := 0 | 154 CONGRAT_PRICING_DRINKS_GREAT_SATISFACTION silent |
| 43 | 180 | always: sideshow satisfaction := 0 | -- |
| 44 | 191 | > 20 guests, sideshow satisfaction < -150; := 0 | 145 PRICING_SIDESHOWS_POOR_SATISFACTION silent |
| 45 | 192 | > 20 guests, sideshow satisfaction > 150; := 0 | 155 CONGRAT_PRICING_SIDESHOWS_GREAT_SATISFACTION silent |
| 46 | 60 | a stink-bomb prank since the last one; := 0 | 99 ADD_PRANK_SBOMB TV |
| 47 | 60 | a guest dropped litter with no bin near; := 0 | 100 ADD_PRANK_LITTER TV |
| 48 | 30 | no staff room, some staff member tiredness > 15, held > 50 | 128 ADD_NEED_STAFF_ROOM TV |
| 49 | 100 | > 20 guests and no staff of any kind, held > 60 | 129 ADD_HIRE_STAFF_GENERAL silent |
| 50 | 250 | mechanics, handymen, guards, entertainers all hired and **every one has a patrol area**, held > 60 | 130 ADD_SET_PATROLS_GENERAL V |
| 51 | 720 | 3 years in, some staff, nobody of any kind trained, held > 10 | 131 ADD_TRAIN_STAFF_GENERAL TV |
| 52 | 400 | open, guards, guard training > 90%, trouble counter < 10, held > 30 | 105 CONGRAT_SECURITY_TRAINED silent |
| 53 | 400 | 2+ researchers, researcher training > 90%, held > 30 | 106 CONGRAT_RESEARCHERS_TRAINED silent |
| 54 | 400 | open, 2+ handymen, handyman training > 90%, fewer than 10 litter, held > 30 | 107 CONGRAT_HANDYMEN_TRAINED silent |
| 55 | 400 | open, 2+ entertainers, training > 90%, queue-quit counter < 10, held > 30 | 108 CONGRAT_ENTERTAINERS_TRAINED silent |
| 56 | 400 | open, 2+ mechanics, training > 90%, fewer than 3 breakdown warnings and 3 breakdowns, held > 60 | 109 CONGRAT_MECHANICS_TRAINED silent |
| 57 | 300 | always: breakdown warnings := 0 | -- |
| 58 | 300 | always: breakdowns := 0 | -- |
| 59 | 300 | exactly 5 mechanics, their training < 40%, held > 90 | 93 ADD_MAX_MECHANICS_V2 silent |
| 60 | 30000 | 2 years in, mechanics, none trained, held > 60 | 17 STAFF_TRAIN_MECHANICS T |
| 61 | 360 | 2 years in, mechanics, none has a patrol area, held > 60 | 8 STAFF_SET_MECHANIC_AREA TV |
| 62 | 280 | 5+ mechanics, park size < 70, held > 30 | 85 ADD_LOTS_MECHANICS silent |
| 63 | 30 | no handymen, more than 10 litter, held > 10 | 4 STAFF_HIRE_HANDYMEN_1 TV |
| 64 | 300 | exactly 5 handymen, training < 40%, held > 90 | 89 ADD_MAX_HANDYMEN_V2 silent |
| 65 | 30000 | 2 years in, handymen, none trained, held > 60 | 18 STAFF_TRAIN_HANDYMEN T |
| 66 | 360 | 24+ months in, handymen, none has an area, held > 10 | 10 STAFF_SET_HANDYMAN_AREA TV |
| 67 | 290 | 5+ handymen, park size < 80, held > 30 | 83 ADD_LOTS_HANDYMAN silent |
| 68 | 30 | always: v52 := 0 (overwritten at v52's next refresh) | -- |
| 69 | 90 | no guards, more than 5 trouble events, held > 10; trouble := 0 before the message | 5 STAFF_HIRE_GUARDS_1 TV |
| 70 | 300 | exactly 5 guards, training < 40%, held > 90 | 95 ADD_MAX_GUARDS_V2 silent |
| 71 | 30000 | 2 years in, guards, none trained | 19 STAFF_TRAIN_GUARDS T |
| 72 | 360 | 24+ months in, guards, none has an area, held > 10 | 13 STAFF_SET_GUARD_AREA TV |
| 73 | 300 | guards, no cameras, guard patrol coverage < 60%, more than 4 trouble events, held > 10; trouble := 0 | 80 GENERAL_LAX_SECURITY silent |
| 74 | 260 | 5+ guards, park size < 90 | 86 ADD_LOTS_SECURITY silent |
| 75 | 180 | no entertainers, more than 30 guests, held > 10 | 6 STAFF_HIRE_ENTERTAINERS_1 TV |
| 76 | 300 | exactly 5 entertainers, **queue-quit counter > 40** (not training), held > 90; := 0 | 91 ADD_MAX_ENTERTAINERS_V2 silent |
| 77 | 30000 | 2 years in, entertainers, none trained | 20 STAFF_TRAIN_ENTERTAINERS T |
| 78 | 360 | 24+ months in, entertainers, none has an area, held > 10 | 15 STAFF_SET_ENTERTAINER_AREA TV |
| 79 | 320 | 4+ entertainers, park size < 50 | 84 ADD_LOTS_ENTERTAINERS silent |
| 80 | 360 | 4+ months in, no researchers, every available ride type built, held > 10 | 7 STAFF_HIRE_RESEARCHERS_1 TV |
| 81 | 30000 | 2 years in, researchers, none trained, research of rides, upgrades, shops, sideshows, features all incomplete, held > 20 | 21 STAFF_TRAIN_RESEARCHERS T |
| 82 | 300 | exactly 5 researchers, training < 20%, held > 90 | 97 ADD_MAX_RESEARCHERS_V2 silent |
| 83 | 310 | 5+ researchers, park size < 90 | 87 ADD_LOTS_RESEARCHERS silent |
| 84 | 60 | every available ride type built, ride research incomplete, researchers, nothing being researched, held > 10 | 49 RIDES_RESEARCH_MORE TV |
| 85 | 12500 | 7+ months in, no rides, held > 30 | 50 RIDES_BUILD_RIDES T |
| 86 | 180 | 7+ months in, rides, researchers, no upgrade researched, held > 10 | 51 RIDES_NO_UPGRADES TV |
| 87 | 60 | some upgrade researched, none in use, **v53 (= stink-bomb counter v76) > 2**, held > 1 | 53 RIDES_USE_UPGRADES TV |
| 88 | 300 | 16+ months in, 5+ shops, shop variety < 40%, held > 60 | 58 SHOPS_POOR_SELECTION silent |
| 89 | 10000 | every available shop type built, shop research incomplete, nothing being researched, held > 1 | 59 SHOPS_RESEARCH_MORE T |
| 90 | 10000 | 7+ months in, no shops, 3+ guests, held > 10 | 60 SHOPS_BUILD_SHOPS T |
| 91 | 300 | 12+ months in, park size > 50, 1..4 shops, held > 30 | 61 SHOPS_BUILD_MORE_SHOPS silent |
| 92 | 300 | 13+ months in, 4+ sideshows, sideshow variety < 40%, held > 40 | 62 SIDESHOWS_POOR_SELECTION silent |
| 93 | 20000 | every available sideshow built, research incomplete, nothing being researched, held > 100 | 63 SIDESHOWS_RESEARCH_MORE T |
| 94 | 10000 | 8+ months in, no sideshows, 3+ guests | 64 SIDESHOWS_BUILD_SIDESHOWS T |
| 95 | 300 | 23+ months in, park size > 100, 1..3 sideshows | 65 SIDESHOWS_BUILD_MORE_SIDESHOWS silent |
| 96 | 20000 | every available feature type built, feature research incomplete, nothing being researched, held > 100 | 67 FEATURES_RESEARCH_MORE T |
| 97 | 100 | 17+ months in, 3+ shops, no features at all, 3+ guests | 68 FEATURES_BUILD_FEATURES T |
| 98 | 60 | 7+ months in, no toilets, more than 10% need a toilet | 69 FEATURES_NO_TOILETS TV |
| 99 | 250 | 20+ months in, toilets, toilet coverage < 70, held > 60 | 70 FEATURES_FAR_TO_TOILETS silent |
| 100 | 300 | 15+ months in, park size > 100, fewer than 4 toilets, held > 20 | 71 FEATURES_NOT_ENOUGH_TOILETS T |
| 101 | 60 | no handymen, toilets on average more than 25% dirty, held > 10 | 72 FEATURES_DIRTY_TOILETS_1 TV |
| 102 | 400 | bank > 1000 hundreds ($100,000), average happiness < 50, held > 30 | 81 ADD_WEALTH_HIGH silent |
| 103 | 60 | wages above income two months running, held > 1 | 82 ADD_WAGES_HIGH TV |
| 104 | 300 | 11+ months in, bank below 100 hundreds ($10,000), held > 50 | text 133 + 134 (row 310), message 133 CASH_LOW silent |
| 105 | 300 | 11+ months in, bank below 50 hundreds ($5,000), held > 25 | text 133 + 134 (row 310), message 134 CASH_LOW_2 silent |

"N+ months in" = `v4 > N-1`. "Park size" = v50. "Every available X built" = variety == 100, which is also
true when no X is available (READ `0x103b20` returns 100 when `built >= avail`).

**Rules whose variable I resolved but whose READ meaning disagrees with the message** (text from the
`id.dat` rows, which the runtime does not select for silent messages):
- **1**: v52 is v75, the ticket counter; with rules 14/15 resetting it, `< 10` is almost always true, so
  rule 1 is effectively "no cameras and some guards" (INFERRED).
- **12**: average happiness `> 1` is nearly always true, so it fires at most every 11 days whenever there
  are 2+ guests (message silent; INFERRED).
- **50**: `NoArea% == 0` for all four kinds means everyone HAS an area; `id.dat` row 1026 says "Try
  setting patrol areas". Inverted (INFERRED intent). It is the only voice-only message.
- **76**: reads the queue-quit counter where its siblings 59/64/70/82 read training %.
- **87**: v53 is the stink-bomb counter (reset by rule 46 whenever it is > 0), so "use your upgrades"
  needs 3+ stink bombs between rule-46 firings: rare (INFERRED).

No rule reads an unresolved variable.

---------------------------------------------------------------------------------------------------

## 8. Gating, collected (item 5)

| what | effect on the rules | READ |
|---|---|---|
| Rollercoaster Test Park (`[0x2b72a8]`, practice flag) | no rule object at all; no greeting in state 0 | `0x151498`, `0x1067dc` |
| Park start | fresh object: v = 0, counters 0, `next = 0`, `lastFail = day(0)`; 16 warm-up calls; first scheduler call 50 updates after start (state 0) | §2 |
| Resumed save | as park start, then calendar restored -> all rules due, `v78 = D` | `0x15172c` after `0x151688` |
| Leaving the park | advisor and calendar destroyed | `0x150e80` |
| Pause (`[0x2b7294]`: pad bit 0x10, controller lost) | no sim pass, no advisor update, no counters | `0x230ba0`, `0x1518e8` |
| `[0x395090] != 0` (meaning not identified) | calendar frozen, scheduler still runs: a due rule can fire once (then `next > day`), v78 stops growing | `0x151930` |
| Advisor presenting (states 2..5) | no refresh, no evaluation | `0x1066b0` |
| Ring not empty | refresh only | `0x10dd08` |
| Ride-along mode (`0x152668` .. `0x1526d0`) | flags = 6: no scheduler, **counters drop events**, text off, queue off, tutorial off | MIPS `0x152690..0x15269c` |
| Tutorial (bit 6) | none on the rules; tutorial messages 0xd0..0x112 posted by `0x107c18` clear bits 0 and 5 until a modal message ends (state 4 sets `0x21`) | `0x107c18`, `0x1066b0` |
| Options | only Tutorial On/Off (bit 6) | `0x13a178` |

The tutorial is a separate object at `adv+0x268` (0x24 bytes, ctor `0x205830`, vtable `0x36c220`); game
code sends it events through `0x107390(adv, n)` (59 call sites), dispatched by `0x206358` through the
74-entry table `0x36c3a0`, only while flags bit 6. Its messages come back through `0x108ff8` (dialog type
3) -> `0x107c18(adv, textRow)`, which finds the message >= 0xd0 whose text row matches. The rule VM never
reads or writes the tutorial, and the tutorial never touches `adv+0x264`.

---------------------------------------------------------------------------------------------------

## 9. Passed on to the messaging side (not investigated further)

- Opcode 8 runs BEFORE opcode 7 in every speaking rule: the text (`0x1073f0` -> `0x108808` on
  `0x3928c8`) is issued the moment the rule fires, even if the voice then waits in the queue. Skipped when
  flags bit 0 is clear or the row is 310.
- `0x1075f0`: immediate (`0x107640`) when flags bit 5 is clear OR the id is in 0xd0..0x112; else the
  20-slot ring `0x107760` (dedup by id; when full, the tail advances and "*** ADVISOR MESSAGE QUEUE
  OVERFLOW! ***" is printed). Immediate interrupts a message in state 2 or 3 (-> state 4) and sets `+0xb4`.
- 50 rules post messages with no text and no voice. Whether those still take the advisor through states
  2..5 (and so block the scheduler for 150+ updates each) decides how fast the rules cycle.
- Greeting ids 0xca/0xab/0xbc and the goal texts `0x16ba58` come from state 0, not from the VM.

---------------------------------------------------------------------------------------------------

## 10. For the port (what exists, what to add)

- **Exists:** the VM (`AdvisorRules.Evaluate`), staff producers (`ParkStaff.FillAdvisorVariables`:
  v6..v11, v13, v18..v20, v33..v46, v49, v55), `ParkFinances.WagesHigh`/`Balance`, `ParkClock.TotalDays`,
  `ResearchManager.ActiveCount`/`ItemLevel`, `ParkStaff.FeatureCoverage(0x20)`, three counters,
  `NativeEntranceAcceptance.Classify` (counter 19's delta).
- **Missing:** the scheduler of §4 with its warm-up, cursors, ring gate and per-rule `next/lastFail`; the
  advisor idle state as the only caller; producers v0, v1, v2, v12, v14..v17, v21..v31, v47, v48, v50,
  v52/v53 copies, v54; counters 0, 1, 3, 5..19 at the sites in §6; the flags bit 3 gates (test park,
  ride-along); the reset of everything at park start and the resume order.
- **Check first:** `ParkClock.UnitsPerTick` (0x1000) against D = 0x4000 (§3). It scales every delay.

---------------------------------------------------------------------------------------------------

## 11. Still unknown (tried)

- `[0x395090]` (UI manager `0x1497b0()` word 0): only the zero store `0x1512cc` is direct; searched
  `0x5090(` stores, `addiu ..,0x5090` sites and zero-offset stores in `0x13c000..0x13f000`. Not found.
- Wall-clock rate of a sim pass (so of a day): D is fixed at 0x4000; the frame pacing is not established.
- `0x1524d8` (scene slot +0x34, sets `[0x3952d8]`): who invokes it was not traced.
- `0x1dd588`/`0x1dd8e8` (queue bit 5 off/on) callers are not identified by name (INFERRED minigames).
- Whether a silent message occupies the advisor (messaging side).
- Product 5 = "restaurant" is only the message names' word; the DBA note calls product 5 "cauldron".
