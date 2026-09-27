# Staff (PS2), how pass, area A: the staff person

Researched 2026-09-27 by agent `sA`. Scope: the object shared by all five staff types (person base
plus staff base), its lifecycle, the per-tick machine, movement, animation, the cursor/focus
question, and the save record. Jobs (handyman, entertainer, mechanic, guard), management and UI
belong to areas B, C and D. This file states only the interface they need from A.

Sources:
- `SLES_500.32` in Ghidra 12.1.2, own copy `~/ghidra_tpw/agent_sA`, outputs in
  `~/ghidra_tpw/agent_sA/out/` (`p1.c` is the person base `0x1912f8..0x193200` plus
  pools, the tick counters, the planner admission and the save lists; `p2.c` has the object-list
  readers and the focus readers; `p3.c` the hire tool; `p4.c`/`p5.c` the visual and model set;
  `p6.c` the save header; `p7.c` the planner pump; `x1..x3.c` are xref lists).
- The what-pass decompiles `~/ghidra_tpw/agent_staff/out/s1..s4.c`, re-read wherever I rely on
  them.
- Raw MIPS through `r5900dis.py` and the full listing. Every load-bearing constant below was
  checked there, with the address given.
- ELF tables read with `~/ghidra_tpw/agent_staff/out/py/elf.py`.
- The disc, through the what-pass's built scan tool (`staffscan logical`, `staffscan aps`, run
  read-only from my out dir).
- The port (`~/tpw-coasters`, read-only): `NativeGuestMotion.cs`, `NativeGuestRoute.cs`,
  `GuestWalk*.cs`, `NativeRoutePool.cs`, `NativeLogicalAnimation.cs`, `NativeEntranceFlow.cs`,
  `ParkVisitors.cs`, `game/Viewer.NativeAnimation.cs`, `findings/native-*.md`.

Labels: **READ** means seen in decompile, MIPS or data. **INFERRED** means reasoned from READ
facts. Offsets are `C+` (the object the vtable methods receive) unless marked `P+`, where
`P = C - 8` is the pool slot that the subclass functions take.

---------------------------------------------------------------------------------------------------

## 0. Short version

1. **One tick is one frame.** The frame function `0x10eec0` runs the update and then the render.
   The update is `0x13af68 → 0x1c4aa8`: it sets D, then scene `vt+0x24` = `0x151800`, which calls
   `0x14be60` and so every staff `vt+0x3c`. The render is `0x13add0 → 0x1c4a58`: it bumps the tick
   counter `[0x397644]`, then scene `vt+0x1c` = `0x151268 → 0x150d60 → 0x150cd8` (visual sync).
   D is `0x4000` every tick, so **walk speed is in 1/256 cell per tick**. (READ)
2. **Tiredness from walking is per waypoint, not per tick.** `0x1db970` adds +1 only in person
   state 2 with a route slot still present. That state is entered once per reached waypoint.
   It adds only when `(tick & 3) == (serial & 3)`, so about 1 waypoint in 4 counts.
   - Planner routes have one waypoint per direction change, plus the endpoint.
   - The local wander has one waypoint per cell.
   (READ; the rate of about 1/4 is INFERRED)
3. **The "frozen while focused" test in every staff update is dead.**
   - `0x14be60` clears the focus `[0x3953c0]` at `0x14bf38` before its object loop.
   - The loop never assigns an object of class 10 (staff) or 11 (guest) (`0x14c000..0x14c028`).
   - The only other writer, `0x14bc28` (from training `0x1ff610`), runs outside the loop.
   - So the one freeze that can happen is the hold flag `C+0x2c & 0x40`.
   - Staff cannot be picked with the map cursor at all. `0x13d178` still has a class-10 branch that
     sets the hold flag, so that code is vestigial. (READ; "cannot happen" is INFERRED from the
     complete writer list)
4. **The entertainer costume is decided by world and park.** The model-registry lookup `0x17d7e8`
   matches the entry's variant against **park index + 1** (`[0x3952e8] + 1`):
   - park 0 → dino / franky / flower / spaceman;
   - park 1 → hunter / vampire / gnome / alien;
   - park index 2 matches nothing, so no model; the front end only offers parks 0 and 1.
   (READ)
5. **Each staff `.aps` section holds at most one record.** The what-pass's section table shows frame
   counts, not record counts.
   - Work logical 16 is first s4, main s5, last s6. Handyman, researcher and all costumes except
     Hunter lack s4 and s6.
   - Walk logical 13 is s0, a **baked-vertex record** (the AlternatePlayer `0x1a7e18`). The port
     cannot play it. The port draws guest s0 as s1, but **only Guard has an s1**. (READ)
6. **Nothing sets walk logical 13 when a walk starts.** It is re-asserted only at each waypoint
   retire (`0x191d78` with a1 = 0; guards pass 1 and never re-assert). So the first leg of every
   errand keeps the previous activity's logical. After work that logical is 16, and 191E10
   gating only applies to 9/13, so the person slides with the work loop playing. At the first
   waypoint it requests 13 and then waits until the model commits it.
   (READ code; the visible effect is INFERRED from the port's model of `10E910/10EA38`)
7. **The tiredness ≥ 91 morale drain in `0x1dba90` is unreachable.**
   - Its only callers are the five find-work functions, which run in state 0.
   - In state 0, tiredness ≥ 81 already returns "go rest". (READ)
8. **Load runs activation twice**: once through the allocator `vt+0x34` and once through the load
   person part `0x192480`. That consumes 2 activation serials and 4 draws from the guest random
   stream. Tiredness and morale are re-rolled, not saved. (READ)
9. **Route requests** (all with `param_7 = 0`):
   - `0x11`: patrol, rest, litter, toilet, mechanic legs, guard re-chase.
   - `0x23`: strike walk, guard exit retry.
   - `0x21`: guard to the gate. `0x01`: guard exit and back. `0x80`: guard patrol point.
   - `0x03`: the wander's map-centre fallback.
   - The pump `0x18d7f8` services only the **newest** ordinary request each frame (LIFO). (READ)
10. **For the port:**
    - As-is: coordinates, step arithmetic, the slot pool and the route cursor.
    - As-is: readiness, the logical table, control and playback.
    - Needs a staff branch: the state-2 advance hook, Guest-typed leases, Viewer wiring and
      show/hide.
    - Missing: the planner with flags, the tile kinds and link byte, the local wander, the
      state/mode dispatcher, the baked-vertex walk, the model registry rule, and save.

---------------------------------------------------------------------------------------------------

## 1. The object

### 1.1 Layout and ownership (READ)

- **Pool slot `P`.** `P+0/P+4` are the pool's own free/active list links. `P+8 = C` is the
  object.
- **`C+0/C+4`** are the *map-object* list links (list head `[0x395208]`, pushed by `0x14da60`,
  unlinked by `0x14dac0`).
- **`C+8`** is the visual. It is created once per slot at pool construction:
  - `0x109308` (base object ctor) calls `0x230a98`.
  - That allocates a 0x4c-byte object and constructs it with `0x227158`.
  - `C+8` points at object `+0xc`.
  - The visual's interface vtable is at visual `+0x24` = **`0x36f290`**, 8-byte entries, delta −12.
- Constructors (all READ): `0x109308` sets base vt `0x358c90` and the visual; `0x1912f8` sets
  person vt `0x364870`, `C+0x38 = 2`, `C+0x14 = 0`; `0x1db5b8` sets staff vt `0x3694b0`,
  `C+0x3c = 0`; then the subclass ctor sets `P+0/P+4 = 0` and the subclass vtable (guard also
  `P+0x58 = 0`).

### 1.2 Field map (`C+` offsets; subclass fields at `P+0x58..` belong to B/C/D)

| off | type | meaning | written by | read by |
|---|---|---|---|---|
| `+0x00/+0x04` | ptr | map-object list next/prev | `0x14da60` (push at head), `0x14dac0` (unlink, zeroes both) | `0x14be60`, `0x150cd8`, `0x14c688`, `0x15c710/0x15c7c8`, `0x12ac78` |
| `+0x08` | ptr | visual (interface at visual+0x24 = vt `0x36f290`) | `0x109308` | `0x1921d0`, `0x191e10` (via `vt+0x14` = `0x109528`: `lw v0,8(a0)`), `0x192c10`, `0x1925a8`, activations |
| `+0x0c` | u32 | activation serial; low 2 bits are the **tick phase** | `0x1093b0` (from `0x191360`) | `0x1db970`, `0x1dba90`, `0x1dbf50` (`& 3`) |
| `+0x10` | ptr | vtable (staff: §2.2 of staff.md) | ctors | every virtual call |
| `+0x14` | s32 | goal-stack depth | many (set 0 on most transitions) | pushes |
| `+0x18..+0x1b` | u8[4] | goal stack | pushes in `0x1db808`, `0x1dbb80`, `0x1913b8` (map-centre branch), job requests | only the entertainer's Shocked pop (area B) |
| `+0x1c/+0x1e` | s16 | x / z in 1/256 cell | `0x191e98` (walk), `0x192480` (load), hire carry `0x128760` | everywhere |
| `+0x20` | ptr/word | job target (staff room for rest; jobs: B/C) | `0x1dbb80`, jobs | `0x1928f8` (removal notice), jobs |
| `+0x24` | u32 | stamp/deadline in ticks; staff base writes `now` when entering 0xb | `0x1dc558`, `0x1db808`, `0x1dbb80`, `0x1913b8`, jobs | jobs (no base reader found) |
| `+0x28` | s16 | current route slot (−1 none) | `0x191d78`, `0x191e98`, `0x1913b8`, planner builder `0x18d358` (owner+0x28), `0x192840` callers | walk |
| `+0x2a` | u8 | (getter `0x192d60`, no staff use found) | — | — |
| `+0x2c` | u16 | person flags, §1.3 | §1.3 | §1.3 |
| `+0x2e` | u8 | **mode**, what the current walk is for (activation sets 2) | `0x191360`, requesters | arrival `vt+0x144`, events `vt+0x16c` |
| `+0x2f` | u8 | **execution state** | everywhere | dispatchers |
| `+0x30` | u32 | low 5 bits: requested logical animation | §5.3 | `0x1921d0` (push), `0x191e10` (readiness) |
| `+0x34` | f32 | facing, radians: 0, π/2, π, 3π/2 | `0x191e98`; jobs (mech, guard) | `0x1921d0` (visual yaw = π − facing, wrapped) |
| `+0x38` | u32 | = 2 from the person ctor; passed by `0x1921d0` to the flag-8 marker | `0x1912f8` | `0x1921d0` |
| `+0x3c` | ptr | candidate record (CStaffDatabase, area D) | subclass activations | `vt+0x5c` = `0x1dc908` (`lw v0,0x3c(a0)`) |
| `+0x40` | s32 | hire day (calendar day count) | `0x1db618`, load `0x1dc178` | `0x1dc458` (days employed), save |
| `+0x44..+0x47` | s8×4 | patrol rect x0, z0, x1, z1 in cells; unset = `00 00 ff ff` | `0x1dc540` (reset), `0x1dc490` (min/max of two corners), load | `0x1dc558`, `0x1dc4f8`, `0x1dc698` (`word != 0xffff0000`), save |
| `+0x48` bits 0–2 | u3 | skill level 0..4 | activation (candidate `+0x14 & 7`), load, `0x1dc968` (training setter, §1.4) | `0x1dc958`, handy/mech speed and time tables, wage |
| `+0x48` bits 3–7 | u5 | base walk speed | activation `\| 0x78` (15), guard `0x1413e0` (15) / `0x1417d0` (30), `0x1dc938` (setter, no direct callers) | `0x1dc928` (`lbu; dsrl 3`) |
| `+0x49` | u8 | candidate slot 0..4 | `vt+0x1c4` = `0x1dc918` (allocator, load) | `vt+0x1cc` = `0x1dc920` (activations, save) |
| `+0x4a` | s8 | candidate motivation copy, clamped 0..100 | `0x1db6dc..0x1db6f8` | **none found** (§11) |
| `+0x4b` | s8 | tiredness 0..100 | activation `rand(30)`, `0x1db970`, `0x1dbf50`, jobs, `0x1dc968` | `0x1dba90`, UI, strike check |
| `+0x4c` | s8 | morale 0..100 | activation `70+rand(30)`, `0x1dbf50`, jobs, `0x1dc968` | UI, strike check |

Displayed motivation is `((100 − C+0x4b) + C+0x4c) / 2` (`0x1dc428`). The first subclass field is
`C+0x50 = P+0x58`. The object ends at `P+0x60/+0x64/+0x68/+0x6c` by type (pool strides
`0x60` handy/res, `0x64` mech, `0x68` ent, `0x6c` guard, from the `0x15fa38` registrations
at `0x147eb0`).

### 1.3 `C+0x2c` flag bits (READ)

| bit | meaning | set by | cleared by | effect |
|---|---|---|---|---|
| `0x0001` | shown | `0x192c10(1)`: activation `0x191360`, `0x1dbfd0` (rest end), `0x1928f8`, job ends | `0x192c10(0)`: rest arrival `0x1db970`, toilet `0x144ec8`, … | gates the visual sync `0x192438`; calls visual `vt+0xac` = `0x17c598` (model flag `0x8000000`) |
| `0x0008` | extra marker (count `[0x2e28d0]`, max 25) | `0x192ca0(1)`, **no callers** | `0x192ca0(0)` | `0x1921d0` second half |
| `0x0020` | person: "wander sent me to the map centre" (`0x1913b8` at `0x191958`); guard: "exit re-requested with 0x23" | `0x1913b8`, guard `0x1406d8` | guard event 1 only | only guards read it |
| `0x0040` | **held**: the whole update is skipped | `0x1928b0` (via `0x1dc780`), hire carry `0x128760` (every frame), `0x13d178` (class-10 focus, unreachable), route reset `0x18c340`, setter `0x192d68` (no callers) | hire drop `0x128888`, grab cancel `0x128c08`, patrol tool `0x128e10/0x128e8c`, list-box cancel `0x13d29c`, reset end `0x18c4b8` | §6 |
| `0x0100` | skip visual sync | setter `0x192d88`: **no callers** | same | `0x1921d0` does nothing while set |
| `0x0200` | next logical push cuts the current record (flags 2) | guard carry `0x140880` | `0x1921d0` after every push | §5.4 |

### 1.4 Small base methods with no Ghidra function (READ from MIPS)

| addr | slot | body |
|---|---|---|
| `0x1920b8` | `vt+0x154` (state 1) | `C+0x14 = 0; C+0x2f = 5` |
| `0x1920c8` | `vt+0x164` (state 0xb) | `jr ra` (empty; the guest overrides it with the idle picker `0x2106e8`) |
| `0x1db800` | `vt+0x1a4` base | empty (every subclass overrides it) |
| `0x1dbb78` | `vt+0x174` think | empty in the base and all five subclasses |
| `0x1dc900` | `vt+0x4c` | `return 1` (selectable) |
| `0x1dc908` | `vt+0x5c` | `return C+0x3c` |
| `0x1dc910` | `vt+0x54` | empty ("release candidate" is a no-op) |
| `0x1dc918/0x1dc920` | `vt+0x1c4/+0x1cc` | set / get `C+0x49` |
| `0x1dc928` | `vt+0x18c` base speed | `C+0x48 >> 3` |
| `0x1dc938` | (not in the vtable) | set speed `(a1 & 0x1f) << 3`; no direct callers |
| `0x1dc958` | (not in the vtable) | get level `C+0x48 & 7`; no direct callers |
| `0x1dc968` | (not in the vtable) | set level: if old level < new, morale = 100 and tiredness = 0; then level = `new & 7`. No direct callers. Same effect as training `0x1ff610` (area D) |
| `0x1dc2a0` | `vt+0xa4` | `return 10` (object class) |
| `0x1dca60` | `vt+0x1e4` | `return 1` |
| `0x1dca68` | `vt+0x1ec` | `return state != 0x32` |
| `0x192dc8` | `vt+0xdc` | `return C+0x2f` |
| `0x193088/0x193090/0x193098` | `vt+0x84/+0x8c/+0x94` | `return 1` (a 1×1 footprint) |

---------------------------------------------------------------------------------------------------

## 2. Lifecycle

### 2.1 Pools, once per park (READ, `0x147eb0`)

Pools are built in this order: guards `0x22c` (stride `0x6c`), mechanics `0x204` (`0x64`),
handymen `0x1f0` (`0x60`), researchers `0x1f0` (`0x60`), entertainers `0x218` (`0x68`).

For each pool:
1. Allocate the whole block (`0x17a370`).
2. Run the slot ctor 5 times.
3. Register it with `0x15fa38(block, name, size, stride, 5)`.
4. Zero the header `+4` free, `+8` active, `+0xc` count.
5. Push slots 0..4 onto the free list **at the head**, so the first allocation gets slot 4.

The pool is freed in `0x149038`.

### 2.2 Allocation (READ, `0x14af30`/`0x14b008`/`0x14b0e0`/`0x14b1b8`/`0x14b290`)

1. Pop the free-list head and **push it at the head of the active list**, so the active list is
   newest first. Increment the count.
   - There is no guard for an empty free list: `piVar4[6]` of NULL. The hire UI caps a type at 5.
2. `vt+0x1c4(C, candidateSlot)`: `C+0x49` = slot.
3. `vt+0x34(C)`: activation (§2.3).
4. `0x14da60(C)`: register as a map object, **pushed at the head** of `[0x395208]`, so it is
   updated before everything registered earlier. The call is idempotent (it scans first).

Callers: the hire tool `0x128690`, and the loaders `0x160448` (guards), `0x1604e8` (researchers),
`0x160588` (mechanics), `0x160628` (handymen), `0x1606c8` (entertainers).

### 2.3 Activation (READ; subclass → `0x1db618` → `0x191360`)

The subclass activation (`0x1783a8` mech, `0x12e0a8` ent, `0x144c90` handy, `0x140600` guard,
`0x1b5fc0` res):
1. Zero its own `P+0x58..` handles.
2. `cand = 0x12ad28(0x12a550(), kind, vt+0x1cc())`, with kind 0 mech, 1 ent, 2 handy, 3 guard,
   4 res; then `C+0x3c = cand`, `cand+0x1c = 0` (hired).
3. `0x1db618(C)`.
4. Set the model: `(C+8)->vt+0x24->+0xc(visual, id, 0, −1)`, with id 0x1ab / 0x1a8 / 0x1aa /
   0x1a9 / 0x1a7. That is `0x228868 → 0x17bff8(visual, id, 0, −1)` (§5.6).
5. `0x17ce10(visual)`: add to the scene. It sets visual `+0 |= 2` when a model handle exists.
6. Guard only: prints `"Member ID = %d"` through the stub `0x1092d0`, which prints nothing.

`0x1db618` runs, in order:
1. `0x191360`:
   - `0x1093b0`: `C+0xc = [0x2aa73c]++`;
   - `C+0x2e = 2` (mode), `C+0x28 = −1`, `C+0x20 = 0`, `C+0x2f = 0`, `C+0x2c = 0`;
   - `vt+0x2c(C, 1)`: shown.
2. `C+0x30` logical = **18**.
3. Morale `C+0x4c = 70 + rand(30)` (70..99). The clamps to 0..100 never bind.
4. Tiredness `C+0x4b = rand(30)` (0..29).
5. Level `= cand+0x14 & 7`. `C+0x4a = cand+0x18`, clamped.
6. `vt+0x54` (empty), `0x1dc540` (patrol rect unset), speed bits = 15.
7. `C+0x40 = 0x16b218(0x16ae90())` (the day count).

`rand(n)` is `0x1448e0` = `0x144870() % n` (divu), the same stream the guests and the planner's
direction order use.

### 2.4 Hire placement (READ; area D owns the tool)

1. The tool's enter `0x128690` allocates and then calls `0x1dc780 → 0x1928b0`:
   - target 0, route freed, depth 0, **hold `|= 0x40`**, state 0.
2. Every frame the carry `0x128760` sets `C+0x1c/+0x1e` = the cursor's world position
   (`0x3951b0+0x2c`, 1/256 cell, **not snapped**) and re-sets 0x40. The visual sync keeps
   drawing it, with logical 18 = s3.
3. Cross `0x128918` accepts when `0x1e65b8(tile)` passes. It refuses tile kinds 4, 5, 7, 8, 10
   and 12, and property bits `+7 & 0x10`, `+7 & 1` and `+7 & 2`, so path and plain ground pass.
   Then `0x128888`:
   - clear 0x40, state 0, depth 0;
   - `vt+0x1d4` = `0x1db750`: logical **13**;
   - sound 0x12f, debit (area D).
4. The next tick runs find work at the dropped fractional position.

### 2.5 Per tick (READ, frame chain in §0)

- **Update:** `0x14be60` walks `[0x395208]` newest first. For each object it saves `next`, calls
  `vt+0x3c`, and then (while no focus has been found yet) runs the hover test (§6).
- **Each staff `vt+0x3c`** (`0x1459b0` handy, `0x1791a8` mech, `0x12e160` ent, `0x141580` guard,
  `0x1b6308` res):
  1. Return at once if `C+0x2c & 0x40` or `0x14e1a0() == C` (the second test is dead, §6).
  2. `vt+0x174` (empty; mech skips it).
  3. Switch on `C+0x2f`: the subclass's own states, else state 0 → `vt+0x1a4`, else `0x1dc018`.
- **`0x1dc018`:** calls `vt+0x174` again, then handles 0 → `vt+0x1a4`, 0xd → `0x1dc6b0`,
  0xf → `vt+0x1ac`, 0x1a → `vt+0x1b4`, 0x31 → `0x1dbb80`, 0x32 → `0x1dbf50`. Anything else goes to
  the person dispatcher `0x1920d0`: 1 → `vt+0x154`, 2 → `vt+0x144(C, 0)`, 3 → `vt+0x14c`,
  5 → `vt+0x15c`, 0xb → `vt+0x164`, other → `"bad state %X : %d : %d"` (`0x12beb0`) and nothing.
- **Render:** `0x150cd8` walks the same list and calls `vt+0x44` = `0x192438`. If shown, that
  calls `0x1921d0(C, 0)` (§5.4).
- **Hazard** (INFERRED): an update that frees the *next* object in the list leaves the saved `next`
  pointing at an unlinked object whose `+0` is 0. The loop updates that object and then stops, so
  everything after it skips one tick. For example, a handyman freeing litter that sits right after
  him. The port need not copy this.

### 2.6 Fire and free (READ; the wage part is area D)

1. List box Fire `0x124300` takes the UI's selected object (`0x1497b0()+0x88`).
2. `vt+0x1f4`: the base `0x1dc6f0` (ent `0x12e768` also frees the effector) sets candidate
   `+0x1c = 1`, frees the route (`C+0x28 = −1`) and pays the pro-rata wage.
3. `0x14b608(C)`:
   - `vt+0x194` release: the subclass drops its job, then `0x1925a8` calls visual
     `vt+0x24(visual, 0)` (`0x227940 → 0x17c300`, removal from the render lists; INFERRED name)
     and frees the route again;
   - dispatch on `vt+0xac` to the pool free: `0x14dac0` (unlink from the map list), count−1, move
     from active to the **free list head**.
4. If the last mechanic is gone, `0x1542a0` clears the upgrade list.

The slot is not destructed. Its fields stay stale until the next activation.

- **No cancel of a pending route request.** `0x18db70` (cancel by owner) has no direct caller, and
  none of the functions above calls it.
- INFERRED consequence: firing a staff member who is waiting in 0xb lets the request finish later.
  The builder then writes a route head into the freed slot's `C+0x28` and `vt+0x16c` sets state 3
  on it. The next activation overwrites `C+0x28 = −1` without freeing, which leaks those output
  slots until the route reset `0x192728`.

### 2.7 Load (READ; §9 has the record)

The loaders run in the order guards, researchers, mechanics, handymen, entertainers (called from
`0x15fc80..0x15fca0`). For each of `header[+0xb..+0xf]` records:
1. `rec = 0x15fd48(0x10, 1)`.
2. `alloc(rec[8])`: the first activation.
3. `0x1dc178(C, rec)`:
   - `0x192480`: **`vt+0x34` again** (the second activation: new serial, new morale and tiredness,
     model set again), then `x = rec[0]`, `z = rec[2]`;
   - `vt+0x1c4(rec[8])`, level `= rec[9] & 7`;
   - `rec[9] & 0x80` → state 0xf, depth 0, logical 15; else logical 13;
   - rect `= rec[0xc..0xf]`, hire day `= rec[4]`.

Because allocation pushes at the head, **each save/load cycle reverses the active-list order**.
The save walks the list newest first and the load pushes each record at the head. That list order
is what "first available mechanic" and similar scans see. (INFERRED from READ push order)

---------------------------------------------------------------------------------------------------

## 3. State machine

### 3.1 Person and staff-base states (READ)

"Tick" means one call of the staff update. "Phase" means `(now & 3) == (C+0xc & 3)`, with
`now = [0x397644]` (`0x1c4930`).

| state | handler | what it does | exit | cadence |
|---|---|---|---|---|
| 0 | subclass `vt+0x1a4` (find work) | 1-in-16 idle sound, then `vt+0x1bc` = `0x1dba90` (§3.4), then the job search (B/C) | a job state (usually via 0xb), 0xd (nothing to do), 0x1a (strike), 0x31 (tired) | one tick |
| 1 | `0x1920b8` | → 5 | 5 | one tick (no staff producer found) |
| 2 | subclass `vt+0x144` (base `0x1db970`) | **slot present**: on phase, tiredness +1 (max 100); then `0x191d78(C, a1)`: logical 13 if a1 = 0, retire one slot, next slot, state 3. **No slot**: arrival by mode (§3.2) | 3, or the arrival target | once per waypoint, plus once at the end |
| 3 | `vt+0x14c` = `0x191e98` (guard override `0x140b48`) | walk step (§4.2) | 2 on exact arrival at the slot target; 0 with the chain freed if the step leaves the grid; stays 3 while blocked by readiness | every tick |
| 5 | `vt+0x15c` = `0x1913b8` | local wander (§4.6), builds a chain | 3 (mode 1), 0xb (map-centre request, mode 1, flag 0x20), or 0 (direct-slot alloc failed) | one tick |
| 0xb | `vt+0x164` = `0x1920c8` (empty) | wait for the planner | event 1 → 3; event 2 → §3.3 | 1..∞ ticks (§4.5) |
| 0xd | `0x1dc6b0` → `0x1dc558` | patrol target (§4.7) | 0xb (mode 1) or 5 | one tick |
| 0xf | `vt+0x1ac` = `0x1db900` | striking, stands (logical 15 set on entry) | the type's strike flag clears (`0x16c988`) → 0, logical 13 | every tick |
| 0x1a | `vt+0x1b4` = `0x1db808` | up to 10 tries of a route to a strike point (§4.8), flags 0x23 | 0xb (mode 5), or stays 0x1a and retries next tick | every tick until admitted |
| 0x31 | `0x1dbb80` | nearest usable staff room → route to its entry cell, flags 0x11 | 0xb (mode 0x11, target = room); none → 0xd; request refused → stays 0x31 | one tick normally |
| 0x32 | `0x1dbf50` | resting (hidden): on phase, morale +1 and tiredness −2 | tiredness ≤ 0 → `0x1dbfd0`: shown, target 0, **state 0xd** | every tick; one quantum per 4 ticks |

Subclass job states are listed in staff.md §5, and I checked the dispatch switches:

| type | states handled in its own update |
|---|---|
| handyman `0x1459b0` | 0x1b, 0x33 |
| mechanic `0x1791a8` | 0xe, 0x10, 0x11, 0x34, 0x36, 0x38, 0x39, 0x3a |
| entertainer `0x12e160` | 0xc, 0x20 |
| guard `0x141580` | 0x21, 0x27, 0x2e (no handler: waits), 0x2f, 0x30, 0x37, 0x3b, 0x3c, 0x3d |
| researcher `0x1b6308` | 0x1f |

Everything else goes through `0x1dc018`.

### 3.2 Arrival: state 2 with no slot (READ)

The base `0x1db970` first sets `depth = 0, state = 0`, then switches on the mode:

| mode | result |
|---|---|
| 5 (strike walk) | state 0xf, logical 15 |
| 1 (patrol/wander) | mode = 2, stays 0 (find work next tick) |
| 0x11 (staff room) | state 0x32, `vt+0x2c(0)` hides |
| any other | stays 0 |

Subclass overrides handle their own modes and fall back to `0x1db970`:
- handyman `0x144ec8`: 7, 0x12;
- mechanic `0x178be8`: 6, 0x14, 0x16; it also applies morale −2 and tiredness −2 on every
  slot-present pass with mode 6, **before** the base +1;
- guard `0x140cd8`: 8, 9, 0xe, 0xf, 0x10, 0x15; it calls the base with **a1 = 1** and also plays
  `0x1409e8` on every pass;
- entertainer `0x12dc88` and researcher `0x1b6068`: base only.

### 3.3 Events: `vt+0x16c`, base `0x1db768` (READ)

The code comes from the event record's `vt+0xc`. The planner notifies 1 or 2 (route-planner.md).

| code | base result |
|---|---|
| 1 (route built) | state 3, depth 0 |
| 2 (route failed) | mode 5 → state 0 (find work, which restarts the strike walk); mode 1 → state 5 (wander); else → state 0xd |
| other | ignored |

Overrides:
- handyman `0x144d60`: 2 with mode 7 releases the litter claim → 0xd; 3 is ignored; 5 drops the job
  and goes to state 5;
- mechanic `0x178458`: 2 with mode 6/0x14 hands the ride on;
- guard `0x1406d8`: 1 also clears flag 0x20; 2 with mode 9 retries with 0x23 once; 9 → 0x2f.

The goal stack is pushed before 0xb but event 1/2 zero the depth, so the push is never popped
(INFERRED: vestigial, and the stack cannot overflow).

### 3.4 Tired/strike check `0x1dba90` (READ, MIPS `0x1dba90..0x1dbb74`)

```
if strike(type = vt+0xac):       vt+0x1dc (0x1dc9c8: free route, target 0); state 0x1a; return 1
if tiredness >= 81 and state == 0: state 0x31; return 1
if tiredness >= 91 and phase:     morale -= 1 (min 0)          <- unreachable
return 0
```

The only callers are the five find-work functions (`0x1413e0`, `0x1457e0`, `0x178dd8`, `0x1b60a8`,
`0x12de40`). They run in state 0, so the third line never runs. Consequences:
- A worker **finishes the current job before joining a strike**, because the check is only in find
  work.
- Rest ends in 0xd (patrol), not 0. The strike check therefore waits for the patrol walk to end.

### 3.5 Base cadence, as numbers

- **Walking tiredness:** +1 per waypoint on a phase tick, ≈ 1/4 per waypoint.
  - A planner route with k turns has k+1 waypoints, so about (k+1)/4 per errand.
  - The wander has one waypoint per cell, so about 0.25 per cell.
  - Mechanic errands (mode 6) net −2 tiredness per waypoint, plus the +1 on phase.
- **Resting:** from tiredness T, ceil(T/2) quanta of 4 ticks each. At the 81 threshold that is
  41 quanta, 161..164 ticks. Morale rises +1 per quantum.
- **Striking:** no tiredness or morale change in the base.

---------------------------------------------------------------------------------------------------

## 4. Movement

### 4.1 Speed (READ, tables from the ELF)

| type | `vt+0x18c` | L0 | L1 | L2 | L3 | L4 |
|---|---|---|---|---|---|---|
| handyman | `0x144d38`: `u32 [0x35fbe0 + 12·L]` | 10 | 15 | 20 | 20 | 18 |
| mechanic | `0x179308`: `u16 [0x3627ca + 4·L]` | 9 | 12 | 14 | 16 | 18 |
| entertainer, researcher | base `0x1dc928` = `C+0x48 >> 3` | 15 | 15 | 15 | 15 | 15 |
| guard | base | 15 (idle); 30 while chasing (`0x1417d0 \| 0xf0`); back to 15 in find work `0x1413e0` | | | | |

Units: 1/256 cell per tick. The step is `max(5, speed) × D >> 14` (`0x191e98`, logical shift), and
D = `[0x397640]` = min(`(counter − prev) << 7`, 0x4000). The counter gains 10000 per frame
(`0x11e758`, `0x11e688` shift at `0x11e6e0`), so D = **0x4000** and the step equals the speed
(coaster-trains.md §2.2; I checked the arithmetic).

Ticks per cell = 256/speed: 9 → 28.4, 10 → 25.6, 12 → 21.3, 14 → 18.3, 15 → 17.1, 16 → 16,
18 → 14.2, 20 → 12.8, 30 → 8.5. Levels ≥ 5 would index garbage (L4 is the maximum).

### 4.2 Walk step `0x191e98` (READ; identical for guests)

With a slot present:
1. Unless `[0x2e2920]` (file value 0), readiness `0x191e10` must pass (§5.5).
2. Decode the target (`0x1924d0`).
3. Facing: dx ≠ 0 → dx > 0 ? π/2 : 3π/2; else dz > 0 ? 0 : π.
4. Step each axis independently, clamped to ±step.
5. If the new cell is outside `[0, W)×[0, H)` (`0x14e0f8`/`0x14e108`), free the whole chain,
   set state 0 and slot −1, and commit nothing.
6. Otherwise commit. On an exact match with the target, state 2.

With no slot: state 2 at once.

**Cost of a waypoint:** the arrival tick moves; the next tick is state 2 and does not move. The
end of a route adds three ticks after the final contact: retire → 3, no slot → 2, then the arrival
dispatch.

### 4.3 Route requests made by staff (READ, args at each `jal 0x18da78`)

`0x18da78(C, x, z, tx, tz, flags, 0)` → `0x18c698`:
- start cell `x>>8, z>>8` and goal cell `tx>>8, tz>>8`, clamped to the map with debug prints;
- exact target kept at request `+0x1c/+0x20`, flags at `+0x2c`, budget `+0x30 = 200`, target tile
  `+0x3c`, priority `+0x34 = param_7`.

It returns 0 when there is no free request record (10) or no free search node (2000).

| caller | flags | target |
|---|---|---|
| `0x1dc558` patrol | `0x11` | random path cell in the rect, centre |
| `0x1dbb80` rest | `0x11` | staff room entry cell (`vt+0x74 + 0x1e1760`), centre |
| `0x1db808` strike | `0x23` | §4.8 |
| `0x1913b8` wander fallback | `0x03` | map-centre path cell |
| `0x144fd8` litter, `0x145250` toilet | `0x11` | B |
| `0x178ef8`, `0x178fe0`, `0x1790c8` mechanic | `0x11` | C |
| guard `0x1410bc` re-chase | `0x11` | C |
| guard `0x141234` | `0x21` | C |
| guard `0x141368` | `0x80` | C |
| guard `0x141a34`, `0x141acc` | `0x01` | C |
| guard `0x1407e8` (exit retry) | `0x23` | C |

What each flag bit admits in the A* (native-route-planner.md, kind table read there):

| bit | admits |
|---|---|
| `0x01` | path kinds 2, 12, 13 (2 and 13 only leaving along the tile's link byte `+2`) |
| `0x02` | open ground kind 0, when `0x1e61e0` holds, at cost 2 |
| `0x08` | kind 5 |
| `0x10` | queue kind 4 (direction-checked) |
| `0x20` | kind 14 (the entrance mouth) |
| `0x40` | kind 1 |
| `0x80` | **restricted mode**: only kinds 2, 12 and 14, cost 1, no direction check |

Always: kind 8 is passable, and kinds 5 and 7 are passable only as the request's target tile. So
`0x11` means paths and queues, `0x23` paths plus grass plus the mouth, and `0x03` paths plus grass.

### 4.4 The route that comes back (READ; native-route-slot-pool.md)

- The builder `0x18d358` emits **one slot per direction change**, walking parents back from the
  goal. The first emitted slot is the exact requested endpoint (quarter-cell quantised by
  `0x192560`); turn slots are cell centres.
- The head goes to `C+0x28`, and any old route is freed first. Slots come from the shared pool of
  1000 at `0x3ae1b8`, the same pool the guests use.

### 4.5 Latency and the pump (READ `0x18d7f8`, `0x18c698`, `0x18da78`)

- Per frame, the pump (called in `0x151800` **before** `0x14be60`) services the active list's
  **head**. That is the newest request, because admission pushes at the head. It keeps going only
  through requests whose `+0x34 == 1`, until about 99 node expansions (the call counter
  `0x195e28` + 100).
- All staff requests pass `param_7 = 0`, so only one staff or guest ordinary request is serviced
  per frame, newest first.
- A request made during frame N's object loop is serviced at the earliest in frame N+1's pump.
  The notify sets state 3, and the first step happens later in that same frame.
- The budget of 200 is decremented only when the request is serviced. A request starved by newer
  ones does not time out (INFERRED from READ).
- 10 request records are shared with the guests. If they are all in use:
  - patrol falls to the wander;
  - the strike walk and rest retry every tick.

### 4.6 Local wander `0x1913b8` (state 5; READ, shared with guests)

Let `(cx, cz)` be the current cell and `t` its tile (`0x14e138`). The direction table is
`0x364a18` (8-byte entries `{s16 dx, pad, s16 dz, pad}`): dir0 (+1,0), dir1 (0,+1), dir2 (−1,0),
dir3 (0,−1).

Two different bounds tests are used:
- A and B.3 test `0 ≤ x < W`, `0 ≤ z < H` inline.
- B.1 and B.2 use `0x149d20`: `0 ≤ x < W−1`, `0 ≤ z < H−1`, which excludes the last row and
  column.

**A. On a path-like tile** (kind 2, 7, 8 or 13: `0x1e6338/0x1e6380/0x1e6390/0x1e6370`):
1. `n = rand(10)` steps (0..9), `prev = 0xffff`.
2. At each step:
   - Candidates are the in-bounds neighbours of kind 2 or 13 whose direction bit
     (`0x364818` = 4, 0x10, 0x40, 1) is set in the **current** tile's link byte `+2`.
   - If there are none, stop.
   - If prev is a candidate and `rand(100) ≥ 20`, keep prev.
   - Otherwise (prev not a candidate, or the 20 % case):
     - if prev = 0xffff, set prev = `rand(4)`;
     - weights are `0x364a38[prev][d]`: 100 same, 40 perpendicular, 10 reverse;
     - draw `r = rand(sum of the candidates' weights)`;
     - take the first candidate whose running sum is **≥ r**, so the first candidate gets
       w0 + 1 of the sum (loop at `0x191b60` onward).
   - Move one cell and record its centre.
3. Build the chain.

**B. Off-path:**
1. **Ring search.** `r` runs from 0 to `rand(5)+3`. For each of the 4 directions not yet blocked,
   look at `cell = c + r·d`, in bounds:
   - if it is a path (kind 2): a **one-slot direct route** to its centre. Free any old chain first
     (debug print "way point not freed"). If the slot allocation fails, state 0. Otherwise state 3,
     mode 1.
   - else, if the tile is kind 5, or has `+7 & 2`, or is non-path with `+7 & 0x10`, block that
     direction.
   - `r = 0` examines the standing cell itself, so standing on such a tile blocks all four
     directions.
2. **Map-centre fallback**, up to 5 tries: the cell `(W/2 + rand(20) − 10, W/2 + rand(20) − 10)`.
   - ⚠ Both halves use **W** (`0x14e0f8` called twice, `0x1915c0/0x1915cc`); a slip unless the map
     is square.
   - It must be in bounds and a path, and admitted with flags **0x03**.
   - Then: mode 1, `C+0x2c |= 0x20`, stamp, push, state 0xb.
3. **Random crawl.** `n = rand(10)` steps over in-bounds neighbours that are not kind 5, not
   `+7 & 2`, and either path or without `+7 & 0x10`.
   - Keep the previous direction if it is a candidate, else pick a uniform random candidate.
   - The first step draws a `rand(4)` that is then overwritten, so one draw is wasted.
   - A step with no candidates does not move; the remaining steps then find the same nothing.

**Chain build (A and B.3):**
- Free the old chain, then allocate from the **last** waypoint backwards, linking forward.
  Waypoints are stored at the scratch array `0x396270`.
- If an allocation fails, stop. The head is then a later waypoint, reached by straight clamped
  stepping (INFERRED).
- State 3, mode 1, depth 0. With n = 0 the slot is −1, so state 3 → 2 → arrival.
- **One slot per cell** (not collapsed), so the per-waypoint tiredness counts per cell here.

### 4.7 Patrol `0x1dc558` (state 0xd; READ)

```
dx = x1 - x0; dz = z1 - z0            (s8 fields C+0x44..0x47)
if dx <= 0 or dz <= 0: return 0       -> state 5 (wander)
repeat 10: x = x0 + rand(dx); z = z0 + rand(dz)
    if in_bounds(x,z) and kind(x,z)==2 and request(C, pos, (x<<8)+0x80, (z<<8)+0x80, 0x11):
        C+0x24 = now; state 0xb; mode 1; depth 0; return 1
return 0                               -> state 5
```

- **The max column and row are never chosen**, and neither is kind 13. A one-wide area
  (x0 == x1 or z0 == z1) is treated like no area. (READ; whether the drawn area includes the max
  edge is area D's `0x128ce8`)
- Confinement is only of the target. The route may leave the rect, arrival goes to find work, and
  the wander fallback is not bounded.
- No job search reads the rect (staff.md §3.6; I did not re-scan).
- Default loop with no area: 0 → 0xd → 5 → 3…2 → 0. That is about 5 ticks plus the walk, with a
  job search every cycle.

### 4.8 Strike point `0x1497c0` (READ, MIPS `0x1497c0..0x14985c`)

Let `e = 0x2b71b0 + world·0x36 + park·0x12`. Then:

`x = e[+0x10]·256 + rand(512)`, `z = e[+0x11]·256 + 0x80`.

- `e[+0x10]` is XCol, the walkway's left column (29/47/43/39/37/47/35 in the filled entries).
- `e[+0x11]` is ZEnd = 19 in every entry: **the first park row past the walkway mouth**.
- So strikers spread over cells (XCol, 19) and (XCol+1, 19) at a random fractional x. The
  endpoint slot rounds it to a quarter cell.
- `0x1db808` also looks up that tile (`0x14e138`) and discards the result.

### 4.9 Staff room target `0x1dbb80` (READ; the room semantics are area D)

- Candidates are placed objects (`0x14cd30` list) with `0x1308c8` (DBA `+0x2e` bit 1) and room
  `+0xa2 ≠ 0`.
- Distance is |Δ| summed over the three halfword lanes of `vt+0x74`. ⚠ The person's `vt+0x74` =
  `0x1925f8` writes only lanes 0 (x>>8) and 2 (z>>8). Lanes 1 and 3 are stack residue
  (`sp+0x12/0x16` never written), so the middle term is undefined. It is harmless only if every
  room reports the same lane 1 (INFERRED; D should check the feature side).
- The first candidate is always taken, and later ones replace it only on a strictly smaller
  distance. Ties therefore keep the earlier room in `0x14cd30` list order.

---------------------------------------------------------------------------------------------------

## 5. Animation

### 5.1 Logical rows used by staff (READ, table `0x2aad48` via `staffscan logical`)

| logical | descriptor(s) |
|---|---|
| 11 | 8 variants: w93 main **F** (hold); w1 each main s2/0..s2/5; w1 main s6/0; flags 2 (re-roll at boundaries) |
| 13 | main s0/0, loops |
| 15 | main s7/0, loops |
| 16 | first s4/0, main s5/0 (loops), last s6/0 |
| 17 | first s3/0, main s3/1, last s3/2 |
| 18 | main s3/0, loops |

### 5.2 Staff `.aps` sections (READ, `staffscan aps`; ONE record or none per section; numbers are frames, S = skeletal)

| model | s0 | s1 | s2 | s3 | s4 | s5 | s6 | s7 |
|---|---|---|---|---|---|---|---|---|
| Handyman | 32 baked | — | 32S | 32S | — | 80S | — | 96S |
| FatMechanic | 32 baked | — | 32S | 32S | 32S | 32S | 40S | 32S |
| Guard | 32 baked | **32S** | 32S | 32S | 41S | 32S | 36S | 32S |
| Researcher | 32 baked | — | 150S | 32S | — | 60S | — | 32S |
| Alien / Dino / Flower | 32 baked | — | 60/40/64S | 32S | — | 32/104/32S | — | 40/64/64S |
| Franky / Gnome / Vampire | 32 baked | — | 32/40/60S | 32S | — | 64/158/64S | — | 60/64/80S |
| Hunter | 32 baked | — | 40S | 32S | **12S** | 74S | — | 74S |
| Spaceman | **64** baked | — | 32S | 32S | — | 81S | — | 80S |

"baked" means a non-skeletal record with flags 0x01, the AlternatePlayer walk that `0x1a7e18`
plays by writing vertices (native-guest-animation-readiness.md: "Section 0 is a walk"). Guests
have the same s0 plus a 16-frame skeletal s1.

### 5.3 Which logical each state requests (READ writers; MIPS scan of `sw …,0x30/0x38(…)` after `& ~0x1f`)

| moment | logical | writer | plays |
|---|---|---|---|
| activation (incl. hire carry) | 18 | `0x1db644` | s3 loop |
| hire drop | 13 | `0x128888 → vt+0x1d4` = `0x1db750` | s0 walk |
| load | 13, or 15 if striking | `0x1dc178` | |
| every waypoint retire (non-guard) | 13 | `0x191d78` (a1 = 0) at `0x191da0` | |
| strike point reached | 15 | `0x1db9f8` | s7 loop |
| strike ends | 13 | `0x1db95c` | |
| handyman sweep (each tick in 0x1b) | 16 | `0x1455c0` | s5 (no s4/s6) |
| mechanic repair / install | 16 | `0x178880`, `0x178b10` | s4 → s5 → s6 |
| mechanic leaves ride | 13 | `0x178714` | |
| entertainer performs / show ends | 16 / **11** | `0x12df70` / `0x12e08c` | s5 / mostly hold |
| researcher quantum | 16 | `0x1b61e8` | s5 |
| guard carry / at gate / drop | 16 / 16 / 13 | `0x140970` / `0x141024` / `0x1409d8` | |
| resting, cleaning toilet | (hidden, no write) | | |

Consequences, each INFERRED from these READ writes plus the port's model of `10E910/10EA38/1ACFC0`:
- **First-leg slide.** After any job, state 0 → route → state 3 keeps logical 16/11/18. `0x191e10`
  gates movement only when the request is 9 or 13, so the person moves at full speed while the
  model plays the work loop. At the first waypoint `0x191d78` requests 13. Movement then blocks
  until the model commits 13: the rest of the current s5 cycle (for example up to 80 frames for
  the handyman), plus one update for each missing "last" section (s6).
- **Standing in 0xb after a patrol arrival** keeps 13, so the walk cycle plays in place while
  waiting for the planner.
- **Missing sections.** A descriptor slot the model lacks poses the old record at its end and
  becomes F; the next update continues (port `NativeGuestAnimation.Update`). So 16 on
  handyman/researcher/costumes costs one held update before s5, and one after it when a new
  request comes.
- **Entertainer logical 11:** 93 % hold (F), 1 % s2/0 fidget, 5 % variants s2/1..5 that no staff
  `.aps` has (hold), 1 % s6 (absent, hold). It re-rolls every model update while held.
- **Guards never re-assert 13 at waypoints** (they pass a1 = 1). They set it only in `0x140990`.

### 5.4 Visual sync (READ `0x192438`/`0x1921d0`; the guest's `0x211d28` is only a delta thunk to the same code)

Every frame, for each map object, `0x150cd8` calls `0x230868()` then `vt+0x44`. For staff that is
`0x192438`: if shown, `0x1921d0(C, 0)`. Unless `C+0x2c & 0x100`, that does:
1. visual `vt+0x54(π − facing)`, wrapped to [0, 2π);
2. visual `vt+0x5c(1.0, logical = C+0x30 & 0x1f, 0, 0, flags)` with flags = 2 if `C+0x2c & 0x200`,
   else 0 (this is the request `10E910`);
3. visual `vt+0x74(x, height, z)`, with height = `0x149d90(x, z, 0)`;
4. clear 0x200.

A held (0x40) staff member is still synced, which is how the hire carry is drawn.

### 5.5 Readiness `0x191e10` (READ)

```
visual = vt+0x14(C) = C+8
if visual and (logical == 13 or logical == 9):
    return current(visual+0x14 handle) is 13 or 9    (0x10ec48; handle 0 -> 0xff -> blocked)
return 1
```

### 5.6 Model and costume (READ `0x17bff8`, `0x17d7e8`, registry `0x2bf2b8`, entries 0x24 bytes)

`0x17d7e8(id)` scans the registry. Its count `[0x2c3300]` is set at run time. It returns the
**first** entry with `+0xc == id` that also passes both checks below:
- world: `+4 == [0x3952e4]`, or `+4 == 4`, or the flag
  `F = (park == 2 && (+0x10 & 8))`;
- variant: `(+0x10 & ~8) == 0`, or `(+0x10 & ~8) == [0x3952e8] + 1`, or `F`.

The staff entries (indices 275..286):

| id | world | variant | name |
|---|---|---|---|
| 423 | 4 | 0 | Researcher |
| 424 | 2 | 1 / 2 | flower / gnome |
| 424 | 1 | 1 / 2 | franky / vampire |
| 424 | 0 | 1 / 2 | dino / hunter |
| 424 | 3 | 1 / 2 | spaceman / alien |
| 425 | 4 | 0 | Guard |
| 426 | 4 | 0 | Handyman |
| 427 | 4 | 0 | FatMechanic |

None has bit 8. I simulated the rule over every world and park:
- **park 0 wears variant 1** (JUNGLE dino, HALLOW franky, FANTASY flower, SPACE spaceman);
- **park 1 wears variant 2** (hunter, vampire, gnome, alien);
- park 2 gets **no model**. The visual handle is then 0, so readiness blocks the first requested
  13 forever (INFERRED).

The front end only produces parks 0/1 (bus-catalogue-identity-join.md table `0x36dd00`), and
world/park are written by `0x150e20` at `0x150e50/54`. All costumed entertainers in one park wear
the same costume. The other four staff match in every park.

`0x17bff8(visual, id, 0, −1)` stores the id, looks it up, and sets the category from the registry
(7) because the argument is 0. It calls visual `vt+0x24(…, 0)`, with no secondary model because
the last argument is −1. On a hit it builds the instance through `0x17bbf0`.

### 5.7 Show and hide

`vt+0x2c` = `0x192c10(C, on)` toggles `C+0x2c` bit 0 and calls visual `vt+0xac` = `0x17c598`,
which sets or clears model `+0x1c` bit `0x8000000` and stores `on` at visual `+0x20`. Hidden
staff are not synced.

After a route reset, a resting staff member is released to state 0 by `0x1928b0` **without**
being shown. It stays invisible until the next show (INFERRED from READ `0x145ac8`/`0x1928b0`;
handymen in 0x1b/0x33 and mechanics in work states are skipped by their reset functions).

---------------------------------------------------------------------------------------------------

## 6. Cursor, focus and freezing (READ; the conclusions are INFERRED where marked)

- **Focus `[0x3953c0]`, all writers** (xref of `0x3953c0` plus MIPS):
  - `0x14bf38` clears it at the start of every `0x14be60` (unconditional, before the
    `[0x395090]` pause test);
  - the loop assigns it at `0x14c0c8/0x14c0e0`;
  - `0x14c1c8/0x14c1e4` assign the park gate object `0x395220` after the loop;
  - the reset `0x149958` (`0x149a34`);
  - `0x14bc28`.
- **The loop's hover test.** It runs only while the focus is still null. It calls the object's
  `vt+0x4c` (selectable), then **skips class 0xb and class 10** (`0x14c000`, `0x14c020`), then
  tests the cursor cell against the `vt+0x74` / `vt+0x84` / `vt+0x94` footprint. A hit becomes the
  focus (with sound 0x32 when 0 < class < 0x13).
- **`0x14bc28`** has exactly one caller: training `0x1ff610`. It sets the focus to the staff member
  and moves the cursor there (`0x1253e0`) unless UI mode `[0x3951d0]` is 0xc or 0xd. It runs in UI
  code outside the object loop, so the next loop's clear wipes it before any staff update.
- **So the staff update's `0x14e1a0() == C` test never fires.** The only freeze is the hold flag
  0x40 (§1.3).
- **No class-10 picker exists.**
  - `0x14c688(cell, class)` is called with classes 0, 1, 3, 5, 6, 7 and 9, never 10.
  - `0x13d178` (Cross on the focus) has a case 10 that sets 0x40 and opens the list box.
  - A matching cancel (`0x13d288..0x13d2a0`) clears it.
  - Both are unreachable because the focus is never staff.
  - PS2 staff are reached through the laptop (All Staff and Single Staff: area D).
- The list-box actions (`0x124300` Fire and the rest) act on the UI's selection `0x1497b0()+0x88`.
  `0x13cfc8` copies the focus there. How the laptop fills it for staff is area D.

---------------------------------------------------------------------------------------------------

## 7. Tiredness and morale: what the base owns (READ)

| event | tiredness | morale | where |
|---|---|---|---|
| activation | = rand(30) | = 70+rand(30) | `0x1db618` |
| waypoint retire on phase | +1 (max 100) | — | `0x1db970` |
| mechanic waypoint in mode 6 | −2 (min 0) | −2 (min 0) | `0x178be8` (before the above) |
| resting, per phase tick | −2 | +1 (max 100) | `0x1dbf50` |
| training to a higher level | = 0 | = 100 | `0x1ff610`; also `0x1dc968` |
| tiredness ≥ 91 drain | — | −1 per phase | `0x1dba90`, **unreachable** |
| jobs | B/C | B/C | |

Thresholds: find work sends to rest at tiredness **≥ 81** (`slti 0x51` on `lb`). The strike
ladder reads averages monthly (area D).

---------------------------------------------------------------------------------------------------

## 8. Interface for areas B, C and D

- **Enter a walk:**
  - call `0x18da78(C, C+0x1c, C+0x1e, tx, tz, flags, 0)`;
  - on nonzero set `C+0x2e = mode`, push, `C+0x2f = 0xb`, `C+0x24 = now`;
  - handle arrival in your `vt+0x144` when `C+0x28 == −1` (else call `0x1db970(C, a1)`);
  - handle event 2 in your `vt+0x16c` (else the base `0x1db768`).
  - Modes used (READ): 1 patrol/wander, 2 idle default, 5 strike, 6 repair, 7 litter, 8 chase,
    9 exit, 0xe/0xf gate legs, 0x10 gate straight, 0x11 staff room, 0x12 toilet, 0x14 install,
    0x15 guard patrol point, 0x16 leave ride.
- **Leave the job:** set `C+0x2f = 0` (find work next tick) or 0xd. Nothing resets the logical,
  so set 13 yourself if the first leg should not slide (§5.3).
- **Freeze** for a tool: `0x1dc780` (clears the job and route, state 0, hold). **Unfreeze:**
  `C+0x2c &= ~0x40`, state 0.
- **Hide/show:** `vt+0x2c(C, 0/1)`.
- **Removal notice** `vt+0x19c` = `0x1928f8(C, obj)`: if `C+0x20 == obj`, target 0, route freed,
  state 0, **shown**.
- **Per-type release** on route reset: `0x179328`, `0x1418e0`, `0x12e590`, `0x145ac8`, `0x1b63f8`.
  Each ends in `0x1dc780` except for the states it protects.
- **Tick phase** helper for B/C: `(0x1c4930() & 3) == (C+0xc & 3)`.
- **Staff lists:** pool `+8` via `0x14d228` (guards), `0x14d640` (handy), `0x14d650` (mech),
  `0x14d660` (res), `0x14d670` (ent). The elements are **P**, newest first.

---------------------------------------------------------------------------------------------------

## 9. Save record (READ `0x1c1ec8`, `0x1c25c8..0x1c2748`, `0x1dc0e8`, `0x192468`; load §2.7)

**Header**, 0x12 bytes at the start of the save (`0x1c1ec8`):

| bytes | content |
|---|---|
| `+0` | u16 `0x3039` |
| `+2` | world |
| `+3` | park |
| `+4..+0xa` | seven other object counts (`0x14cba8`, `0x14cc58`, `0x14cbf0`, `0x14cca0`, `0x14ccf8`, `0x14cd40`, `0x14cd88`) |
| `+0xb` | guards |
| `+0xc` | researchers |
| `+0xd` | mechanics |
| `+0xe` | handymen |
| `+0xf` | entertainers |
| `+0x10` | guest count (`0x14d690`) |
| `+0x11` | unwritten |

**Staff blocks.** `0x1c1b08` writes them after `0x1c2328`, in the order guards, researchers,
mechanics, handymen, entertainers. Each walks its active list and writes 16 bytes per staff member
(`0x1c1e10(buf, 0x10, 1)`):

| off | size | content |
|---|---|---|
| `+0` | s16 | x, 1/256 cell (`0x192468`) |
| `+2` | s16 | z |
| `+4` | s32 | hire day `C+0x40` |
| `+8` | u8 | candidate slot `C+0x49` |
| `+9` | u8 | level `C+0x48 & 7`, `\| 0x80` if state == 0xf |
| `+0xa..+0xb` | — | **unwritten** (stack residue in `0x1c25c8`'s buffer; the loader ignores them) |
| `+0xc..+0xf` | s8×4 | patrol rect x0, z0, x1, z1 |

**Not saved:**
- tiredness, morale, speed, state (other than striking), mode, target, route, facing, logical,
  hold, serial;
- `C+0x4a` is re-copied from the candidate.

Everything else is rebuilt by the double activation. A loaded staff member starts in state 0 at
the saved spot, or in 0xf striking (and the strike flag itself is saved by the calendar, area D).

---------------------------------------------------------------------------------------------------

## 10. Mapping to the port (the port has no staff object; LaptopListScreens only knows the screen)

| person machinery | native | port piece | verdict |
|---|---|---|---|
| coordinates, slot codec | 1/256 cell s16, `0x192560`/`0x1924d0` | `NativeGuestMotion.Point`, `EncodeTarget`/`DecodeTarget` | **reuse as-is** |
| walk arithmetic | `0x191e98` step `max(5,s)·D>>14`, bounds, facing | `NativeGuestMotion.StepAmount`/`AdvanceCoordinates`; facing in `NativeGuestRoute.Step` (same rule: dx<0→3, dx>0→1, dz>0→0, else 2) | **reuse as-is**; pass the staff speed (§4.1) as `Speed` and `0x4000` as `Delta` (`NativeRideQueue` already does) |
| output slot pool | `0x3ae1b8`, 1000, shared by all persons | `NativeRoutePool` (via `GuestWalk.NativeRoutes`) | **reuse as-is**, the SAME instance as guests; staff must not get their own pool |
| route cursor, state 3/2 phases | `0x191e98`/`0x191d78`, arrival later | `NativeGuestRoute` (Guest-agnostic class; internal ctor adopts a head) | **reuse, staff branch at the slot-advance**. On each retire with a slot present, apply: phase tiredness +1; mechanic mode-6 −2/−2 *before* it; logical 13 unless guard. `NativeMotionInputs.SlotAdvanced` is the right seam but is documented as observational, so either relax that or wrap the cursor. On `Completed`, run §3.2 in the same update |
| route leases and ownership | none natively | `GuestWalk.AssignNativeRoute`, `NativeWalkLease` (typed on `Guest`) | **staff branch**: a Staff-owned cursor holder. The Guest lease API cannot hold a staff member |
| path → slots | `0x18d358` one slot per turn | `NativeRouteOutput.FromCells` | **reuse for planner routes**; **do not** use it for the wander, which is one slot per cell (use `NativeRoutePool.TryBuild` on the cell list) |
| planner and request service | `0x18da78`, A* `0x18cf30/0x18c928`, flags 0x11/0x23/0x21/0x01/0x80/0x03, LIFO pump, 10 records, notify 1/2 → `vt+0x16c` | BFS `ParkPaths.Route`/`GuestWalk.RouteFor` (no flags, no costs, no link directions); `NativeEntranceFlow.Services.Request/Pump` shape (entrance- and Guest-typed) | **missing**. Staff need a flag-aware request/result service with the async 0xb wait. The shape of the entrance flow's Request/Pump is the model to generalise |
| native tile array | kind byte, link byte `+2`, property `+7` (0x14e138) | `ParkPaths` has None/Path/Queue plus entrance cells | **missing** for kinds 0/1/5/7/8/10/12/14, links and properties. Strike (0x23), wander, rest (kind 5/7 targets) and drop validity (`0x1e65b8`) depend on them |
| local wander | `0x1913b8` | not ported (`ParkVisitors.cs:883`) | **missing**, shared with guests; implement once |
| dispatcher, state/mode/goal stack | `0x1920d0`, `C+0x2e/+0x2f/+0x14` | guests use `VisitorIntent` plans | **missing**. The staff machine is small (§3); build it natively rather than as intents |
| readiness | `0x191e10` | `NativeLogicalAnimationControl.MovementPermitted` | **reuse as-is** |
| logical table, control, playback | `0x2aad48`, `10E910/10EA38/1ACFC0` | `NativeLogicalAnimationTable`, `NativeLogicalAnimationControl`, `NativeGuestAnimation` | **reuse as-is**. Feed durations from the staff model's own `.aps` (§5.2); logical 11's missing variants must hold, as the class already does |
| visual sync | `0x192438`/`0x1921d0` (same code for guests) | `Viewer.NativeAnimation.cs` keyed by `Guest`, `Push()` | **staff branch** in the Viewer: push `C+0x30` every frame, including while held, and the 0x200 cut flag for the guard carry |
| section-0 walk | baked-vertex AlternatePlayer `0x1a7e18` | the Viewer draws guest s0 as skeletal s1 | **missing for staff**: only Guard has an s1. Handyman, mechanic, researcher and the 8 costumes need the vertex player, or a labelled non-native stand-in |
| model and costume choice | `0x17d7e8` (id, world, park+1 variant) | guests use the port's own model choice | **missing**; the rule in §5.6 is small |
| show/hide | `C+0x2c` bit 0 via `0x192c10` | `ParkVisitors.ServiceHidden` (relief-only) | **staff branch** (rest, toilet) |
| activation serial | `0x1093b0` | `NativeActivationSequence` | **reuse as-is**; count 1 per hire and **2 per loaded staff member** |
| random stream | `0x1448e0` guest stream | injected `Func<int,int>` per component | **reuse** the guests' injected stream; the native interleaving is not reproducible anyway (label it) |
| tick counter and phase | `[0x397644]`, bumped per frame in render | `ParkSim` 40 ms tick / `DecisionTick` | **adapter**, like the guests' `Stamp`. The phase test needs `now` and the serial's low 2 bits |
| update order | `[0x395208]` newest first; pump before the objects | port-owned loops | **missing** (matters only for same-frame interactions and the pump latency) |
| cursor picking | none for class 10 | — | **nothing to port** (laptop only) |
| save/load | §9 | none | **missing** |

---------------------------------------------------------------------------------------------------

## 11. Corrections to staff.md (READ)

- §2.5/§5: the focus half of the freeze is dead code (§6). Only `C+0x2c & 0x40` freezes.
- §3.6 "Walking (`0x1db970`): every 4th tick tiredness +1" should read: **+1 per waypoint
  retire on a phase tick**. The function runs only in person state 2 (§3.1).
- §3.6: the tiredness ≥ 91 morale drain is unreachable (§3.4).
- §1.4 section table: the numbers are frames. Every staff section has 0 or 1 record, and s0 is the
  baked-vertex walk, not a layer.
- §1.1 costume variant: resolved, park index + 1 (§5.6).
- §2.1 per-tick update: READ, not INFERRED. `0x14be60` walks `[0x395208]`, which `0x14da60`
  pushes at the head, so the order is newest first.
- §4.9: activation runs twice on load (§2.7).
- §2.2: `vt+0x054` (`0x1dc910`) is empty; `vt+0x1d4` (`0x1db750`) is called by the hire drop
  `0x128888`.
- §3.6 strike point: x = XCol·256 + rand(512), z = row ZEnd (19)·256 + 128 (§4.8).
- §2.3 `C+0x2c` bits: add 0x1 shown, 0x8, 0x100, and the person-level 0x20 set by the wander
  (§1.3).

## 12. Still unknown (tried)

- **`C+0x4a` reader.** A whole-ELF scan of `lb/lbu/sb …, 0x4a(` and `…, 0x52(` near other
  staff-field offsets finds only the activation (`0x1db6dc..0x1db6f8`) plus ride and UI structs.
  Loads through computed offsets were not searched.
- **Seconds per tick.** Everything here is per frame (`0x10eec0` call). The frame rate, and the
  port's 40 ms tick versus ParkClock's "0x1000 a tick" claim against D = 0x4000, were not settled.
  That calendar question belongs to area D.
- **Who sends handyman event 5**, and whether anything sends event 3 (`0x18d7a0` has no direct
  caller). I did not search indirect calls.
- **Whether any code sets `C+0x2c` 0x100 or 0x8 on staff.** The setters `0x192d88` and
  `0x192ca0` have no xrefs and are not in any staff vtable; `ori …0x100` sites outside person code
  were not audited.
- **The model update's place in the frame** (`1ACFC0` against the update and render), and so the
  exact length of the §5.3 first-leg block. It depends on the order already open in
  native-guest-animation-readiness.md.
- **Lane 1 of the feature's `vt+0x74`** in the rest search (§4.9). I did not read the placed-object
  side.
- **The drawn extent of the patrol area** against the excluded max column and row (`0x128ce8`,
  area D).
- **`0x230868`**, called per object before the visual sync in `0x150cd8`. Not read.
