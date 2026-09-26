# Coasters (PS2): track geometry, meshes and pylons (area A, "how" pass)

Researched 2026-09-26 for strawberry (the "how" pass on roller coasters). Source: `SLES_500.32` (PAL)
decompiled in Ghidra 12.1.2 with the ghidra-emotionengine-reloaded extension. Every load-bearing constant
and branch was checked in raw R5900 MIPS (`tools/r5900dis.py`). Tables were read from the ELF, and disc data
through the port's own `TPW.PS2.Data` readers; the scratch tools stay local and only numbers are recorded here.
Overview and cross-file notes: `coasters.md`. The "what" survey: `coaster-survey.md`.

**READ** = seen in the decompile, the MIPS or the data. **INFERRED** = reasoned to.
Unit names used here:
- **cell** = one map square;
- **u** = 1/256 cell (the integer unit of node positions);
- **model unit** = 1/10 cell (the grid's scale, `tpw-tracks/findings/grid.md`);
- angles in **1/4096 turn**.

---------------------------------------------------------------------------------------------------

## 0. Short version

1. **One node = one segment.** The mesh a node owns is the curve arriving at it from its predecessor.
   It is a uniform Catmull-Rom through the 4 window nodes. 17 samples are taken at t = i/16, and every
   style extrudes 16 quads per band between them. (READ)
2. **Where the curve passes through each node.** The control point of a node is `(x, y_base +
   25.6·attachY + off, z)`:
   - `attachY` is the local y of the pylon model's `TrackDummyCentre` helper after the pylon is posed;
   - `off` is 0x60 u, except 0 for the two suspended coasters and 0x100 for Moonshot.
   The pylon height enters only through the pylon's loft animation. That animation is played
   **additively**:
   - the add branch is in `0x1a7f48` (READ), selected by a model-header flag that only pylons carry;
   - so `attachY = bind + 90·h/2560` model units, i.e. the track rises 0.9 u per unit of pylon
     height (INFERRED arithmetic);
   - the check against station models is mixed, 10 for and 3 against (§4.5).
3. **The frame is not derived from curvature.**
   - Each node carries a side vector `S = (cos θ·cos β, sin β, −sin θ·cos β)`. Here θ is the average
     of the incoming and outgoing chord headings, and β = −bank.
   - S is Catmull-Rom-interpolated like the positions.
   - up = normalize(T × S), where T is the curve derivative.
   - **There is no auto-banking**: bank is only the player's ±0x200. (READ)
4. **Loops are an explicit formula**, not a spline:
   `lerp(Pleadin, Ploop, t) + 3·sin(2πt)·F + (0, 3 − 3·cos 2πt, 0)`.
   - F is the approach direction.
   - The loop node sits exactly 1 cell to the side, so this is a true circle of radius 3 cells in
     an isotropic space.
   - The lead-in segment and the segment after the loop blend into and out of a straight line with
     a cosine ease. (READ)
5. **The 7 styles are hard-coded cross-sections**: flat ribbon, trough, rope, triangular beam, big
   tube, twin rails, and mine trough. Tables are in §5.
   - Every strip is double-sided, and every normal is (0,1,0).
   - The texture V coordinate is the arc length: 1 repeat per cell, or 4 per cell for the rope and
     chain bands.
   - The chain, the trough floors and Moonshot's tube scroll forward by 0.08 texture repeats per
     coaster update: 0.08 cells on 1× bands, 0.02 cells on the 4× chain. (READ)
6. **The lift or chain is found by simulation, not placed.** A test train is run from the station.
   - These samples are flagged "winch":
     - the station segment;
     - the first segment;
     - every span the train covers while it is still at the 0.04 speed floor, i.e. the climb to
       the first crest.
   - Only styles A and G draw the flag, as a scrolling middle band textured from style-table slot 1
     (`chain.ssh` for most coasters). (READ)
7. **Units: the coaster code uses 256 u per cell on all three axes.** (READ, plus a data check.)
   - It divides x, y and z by the same 256.
   - It measures Euclidean lengths.
   - The loop is a circle in that space.
   - Two station models' rail helpers agree with the formula to within 3 u (§8). That check is
     conditional on the additive pose.
   - The one outlier is the terrain base, `tile height × 4`, which only moves pylon feet.
8. **Two findings for the port's data library** (§4.4):
   - A fitting's node is `fitting index + header u16 @+0x34`, not `meshes + index`.
   - The engine adds or replaces an animated path translation depending on model header `+0x1c & 4`
     (in `0x1a7f48`, READ). Only the pylons set that flag. `AnimatedModel.cs:810` always replaces
     it.

---------------------------------------------------------------------------------------------------

## 1. The node class (`0x1997d0..0x1a3310`, 0x620 bytes)

### 1.1 Construction and ownership (READ)

- ctor `0x1997d0`:
  - base `0x109308`;
  - vtable `0x365510` at `+0x10`;
  - the second interface vtable `0x3654f8` at `+0x14` (one slot, delta −20 → `0x19c650` "create
    the track mesh");
  - clears the links;
  - `+0xcc = +0x614 = +0x61c = −1`;
  - `+0x5c = 0x230a98()` = a new **track model instance** (0x4c-byte object, instance vtable
    `0x36f290`);
  - `0x19b180` clears the 17 winch flags.
- **The pylon is the map-object model at `+8`** (returned by vt `+0x14`). The track is `+0x5c`.
  This corrects coaster-survey §4.2, which had them swapped:
  - `0x19d1e0` shows `+0x608` driving the vt `+0x14` model and `+0x60c` driving `+0x5c`;
  - `0x19cdd0` animates `+8`;
  - every style fn1 writes vertices into `*(+0x5c)+0xc` → `+0x70`.
- Owner `+0x28` = the coaster. It is set by `0x19c9f0`, which also zeroes the links, `+0x68..+0x78`
  and `+0xcc`, calls vt `+0x3c` (window) and dirties.

### 1.2 Field map

Fields not listed here are as in coaster-survey §4.2. R = reader, W = writer.

| off | type | meaning | W / R (all READ) |
|---|---|---|---|
| `+0x08` | ptr | pylon model instance (map-object base) | base; R `0x19a420`, `0x19cdd0`, `0x19cae0` (stack) |
| `+0x14` | vtbl | interface `0x3654f8`: model system → `0x19c650` (style fn0) | ctor |
| `+0x18,+0x1c,+0x20,+0x24` | ptr | window pp, p, self, n (§1.4) | W `0x19a760` |
| `+0x28` | ptr | owning coaster (0 = unowned, e.g. before placement) | W `0x19c9f0` |
| `+0x2c/+0x30` | ptr | ring prev / next | W `0x19c8d8`/`0x19c900` |
| `+0x34/+0x38` | ptr | node stacked above / below in the same cell | W `0x199c50`/`0x199dd0` |
| `+0x3c,+0x3e,+0x40` | s16 | x, **y_base**, z in u. x,z = cell·256 + 0x80 (cell centre). y_base = terrain / stack top / −0x100 (§4.1) | W `0x199fb8`, `0x19a058`, `0x19ca78` |
| `+0x44` | s32 | pylon height, 0..0x500 from the tool, step 0x14 | W `0x19c788`; R loft `0x19cdd0`, stack sums `0x19a1d8`/`0x19a6e8` |
| `+0x48` | s16 | pitch of the chord prev→self: −atan(dy/dh) in 1/4096 turn | W `0x19aa48`; getter `0x1a3220`; no reader in the geometry code (callers of the getter not traced) |
| `+0x4a` | s16 | heading of the chord prev→self, 1/4096 turn, 0 = +z, 0x400 = +x; +0x400 on kind-2 nodes | W `0x19aa48` |
| `+0x4c` | s16 | **half-turn to next**: kind 0 with a next only, else 0 (§3.1) | W `0x19cdd0`; R `0x19b208`, `0x19cdd0` |
| `+0x4e` | s16 | bank ±0x200 (forced 0 on Moonshot) | W `0x19c818` |
| `+0x50` | s16 | 3D chord length prev→self in u (isqrt) | W `0x19aa48` |
| `+0x52` | u8 | loop flag (1 on lead-in and loop nodes) | W `0x19c928` |
| `+0x53` | u8 | segment kind: 0 normal, 1 loop lead-in, 2 loop | W `0x19c9c0` |
| `+0x54` | s32 | valid; 0 → both textures become `red.ssh` (§5.1) | W `0x19aec0` |
| `+0x58` | s32 | cleared on position, bank or validity change; **no reader found** in the node, coaster or style code | |
| `+0x5c` | ptr | track model instance | ctor |
| `+0x60` | ptr | track mesh resource (`0x16a150` result) | W fn0; `0x19c650` stores it in instance `+0xc` |
| `+0x64` | ptr | strip descriptor array, 0x14 bytes each (§5.1) | W fn0; freed `0x1998f0` |
| `+0x68` / `+0x6c` | f32 | texture V carried in: 1× rail coordinate / 4× chain coordinate, wrapped to [0,1) | W previous node's `0x19b208` (`0x19ca68`/`0x19ca70`); exit node zeroed each update by `0x123698` |
| `+0x78` | s32 | mesh up to date (0 = rebuild) | W 0 by `0x19ca78` on the whole window; 1 by `0x19cc48` |
| `+0x7c..+0xa8` | f32[4][3] | control points P0..P3 in **cells** (u/256) | W `0x19b208` |
| `+0xac..+0xb8` | f32[4] | heading + half-turn per control point, unwrapped | W `0x19b208` |
| `+0xbc..+0xc8` | f32[4] | bank per control point | W `0x19b208` |
| `+0xcc` | s32 | loaded pylon model id (−1 = reload) | W `0x19aef8`, `0x19aec0` |
| `+0xd0` | f32 | **segment length** (§2.4) | W `0x19b208` |
| `+0xd8 + i·0x48`, i = 0..16 | rec | sample i (§2.3) | W `0x19b208`; flag W `0x19b1a0` |
| `+0x5a0..+0x5b4` | f32[6] | segment AABB min xyz, max xyz, padded ±2 cells | W `0x19b208`; R every fn1 → `0x16a458` (mesh bounds) |
| `+0x5b8..+0x5e4` | f32[4][3] | side vector S_k per control point (§3.1) | W `0x19b208`; R `0x19bda0` |
| `+0x5e8` | f32 | pylon's `TrackCentreDummy` record `+0x44` | W `0x19cdd0`; **no float reader found**: the `.text` imm-0x5e8 scan finds only this writer, `$sp` slots in style G fn1, and word loads in the `0x294xxx` library, which were not examined |
| `+0x5ec/+0x5f0/+0x5f4/+0x5f8` | f32 | pylon anim params rotate / incline (always 0.5) / bank / loft | W `0x19cdd0`, reset −1 by `0x19aef8` |
| `+0x5fc..+0x600` | s16[3] | cached pylon model position (x−0x80, y_base, z−0x80) | W `0x19cdd0` |
| `+0x608` / `+0x60c` | s32 | pylon visible / track visible | W `0x19d1e0` |

### 1.3 Node kinds and roles (READ)

| role | how it is identified | curve of its segment | pylon |
|---|---|---|---|
| normal pylon | `+0x53 = 0` | Catmull-Rom (§2.1) | drawn, posed (§4) |
| loop lead-in | `+0x52 = 1, +0x53 = 1` | CR eased into a straight line (§2.2) | drawn, height 0 |
| loop | `+0x52 = 1, +0x53 = 2` | vertical circle (§2.2) | drawn, height 0, yaw +90° |
| node after a loop | prev `+0x53 = 2` | CR eased out of a straight line (§2.2) | normal |
| station exit | address `coaster+0x794` | CR from the entry node through the station | **hidden** (`0x19d1e0`); y_base −0x100 |
| station entry | address `coaster+0x174` | CR from the last pylon (only when closed) | hidden; y_base −0x100 |
| stacked | `+0x38 ≠ 0` (a node below in the same cell) | normal | base = top of the pylon below (§4.1) |
| tool ghost | global `0x2ac438` | built like any node (`0x123500/0x123698` include it) | `0x19d1e0` hides both parts (the tool draws it some other way, not traced) |

- Loop placement (tool `0x11c668`, READ; the rest of the tool is area B):
  - The lead-in is laid on the cursor cell, and the loop node **one cell sideways**. The side is
    chosen from the approach d = cursor − previous node:
    - |dx| < |dz|: loop at x − sign(dz);
    - else: loop at z + sign(dx) (z+1 for dx ≥ 0).
    - So D = lead-in − loop is axis-aligned with |D| = 1 cell, and the loop always drifts toward
      −S (§3.1).
  - Square again right after a loop lays another loop node at `2·last − lead-in`, i.e. a second
    consecutive loop one more cell over.
  - Both nodes are laid with **height 0 and bank 0**: `jal 0x122028` with `a2 = a3 = 0`,
    `t0 = 1` (at `0x11c718`, `0x11c814`, `0x11c954`).

### 1.4 Ring and windows (READ `0x19a760`, `0x122060`, `0x121d68`, `0x123500`)

- **Ring:** entry `+0x174` → exit `+0x794` → pylon 0 … pylon n−1 → entry. Wiring:
  - `0x122060`: entry.next = exit, entry.prev = 0, exit.prev = entry;
  - `0x121d68`: new.prev = last, last.next = new;
  - `0x120868` closes the ring: last.next = entry.
- **Window** (`0x19a760`, via vt `+0x3c`, run every update for the ghost, the exit, each pylon and
  the entry, in that order, by `0x123500`):
  ```
  self = N
  next = N.next  ?? N
  prev = N.prev  ?? N
  pp   = prev.prev ?? prev
  window = (pp, prev, self, next)  ->  control points P0..P3
  ```
- **Segment index** (what a train position means, §2.4): 0 = exit's segment (entry → exit, through
  the station), 1 = pylon 0's (exit → p0), …, n = p(n−1)'s, n+1 = entry's (p(n−1) → entry).
- **Dirtying:** an edit calls `0x19ad28(node)`, which re-derives `0x19aa48` for prev, self, next and
  the whole stack. Each setter calls `0x19ca78`, which:
  - refreshes y_base;
  - recurses to the node above;
  - sets `+0x78 = 0` on all 4 window nodes.
  
  `0x123698` then rebuilds dirty meshes in ring order (`0x19cc48` → `0x19b208` + style fn1).
  `0x1239d8` forces a rebuild of all of them.

---------------------------------------------------------------------------------------------------

## 2. The spline

### 2.1 Control points and the Catmull-Rom (READ `0x19b208`, `0x19bda0`; MIPS checked)

- `P_k = ctrl(window[k])`, where `ctrl(n) = 0x19a420(n)`, divided by 256 on all three axes
  (`×0.00390625`). The formula is in §4.3.
- Normal segment (`+0x53 ≠ 2`), t ∈ [0,1], **uniform** parameterisation, tension ½:
  ```
  C(t)  = P0·(−t³+2t²−t)/2 + P1·(3t³−5t²+2)/2 + P2·(−3t³+4t²+t)/2 + P3·(t³−t²)/2
  C'(t) = P0·(−3t²+4t−1)/2 + P1·(9t²−10t)/2  + P2·(−9t²+8t+1)/2  + P3·(3t²−2t)/2
  S(t)  = same basis applied to the side vectors S0..S3 (+0x5b8), NOT normalised
  ```
  (0x19bda0 forms S(t) as CR(P+S) − CR(P); the basis is linear, so this equals CR(S).)
- Output of `0x19bda0(t, node, pos, side, tangent)`: pos = C(t), side = S(t), tangent = C'(t),
  none of them normalised. Special cases follow in §2.2.
- The curve passes through P1 at t = 0 and P2 at t = 1, and is C1 across nodes: both sides have
  tangent (next − prev)/2.

### 2.2 Loops, lead-in and lead-out (READ, MIPS `0x19bdec..0x19bf24`, `0x19c294..0x19c61c`)

**Loop segment** (self `+0x53 == 2`; P1 = lead-in, P2 = loop node):
```
D = (P1.x−P2.x, 0, P1.z−P2.z)          # the 1-cell sideways offset, from the loop node back to the lead-in
F = (−D.z, 0, D.x)                     # = the approach direction (see §1.3)
pos(t)  = (1−t)·P1 + t·P2 + 3·sin(2πt)·F + (0, 3 − 3·cos(2πt), 0)
side    = D                            # returned as the side vector, unnormalised
Vin     = (sin(2πt)·D.z, cos(2πt), −sin(2πt)·D.x)   # inward normal: up at t=0, down at t=½
tangent = normalize(D × Vin)
```
- Constants: 3.0 (`lui 0x4040` at `0x19be3c`) and 2π. Because |D| = 1 cell, the horizontal radius
  (3·|F|) equals the vertical radius 3: **a circle of radius 3 cells**, centred 3 cells above the
  entry.
- The car goes up the far side (t = ¼ is 3 ahead and 3 up), is inverted over the entry point at
  t = ½, and comes back to the start shifted 1 cell sideways. It exits at the loop node, heading F.
- The loop's bottom follows the lerp: y goes from P1.y to P2.y. Both nodes have pylon height 0,
  so both sit at ground attach height.

**Lead-in** (self `+0x53 == 1`; P2 = lead-in, P3 = loop node):
```
d = (P2.x−P3.x, 0, P2.z−P3.z)                        # = D of the following loop
L(t) = (P2.x + (1−t)(P1.x−P2.x)·d.z², P2.y, P2.z + (1−t)(P1.z−P2.z)·d.x²)
w  = ½ + ½·sin(πt − π/2)  (= (1 − cos πt)/2, 0→1)
pos     = (1−w)·C(t)  + w·L(t)
side    = (1−w)·S(t)  + w·d
tangent = (1−w)·C'(t) + w·(−d.z, 0, d.x)
```
The `·d.z²` / `·d.x²` factors project P1 onto the loop's travel axis. That is exact only for an
axis-aligned unit d, which the tool guarantees (§1.3). The segment flattens to the lead-in's height
and ends pointing along F.

**Lead-out** (self's prev `+0x53 == 2`; P0 = lead-in, P1 = loop node):
```
e = (P0.x−P1.x, 0, P0.z−P1.z)                        # = D again
L(t) = (P1.x + t(P2.x−P1.x)·e.z², P1.y, P1.z + t(P2.z−P1.z)·e.x²)
w  = (1 − cos πt)/2
pos = w·C(t) + (1−w)·L(t);  side = w·S(t) + (1−w)·e;  tangent = w·C'(t) + (1−w)·(−e.z, 0, e.x)
```
The lead-out leaves the loop node straight along F at the loop node's height and blends into the
spline. A second consecutive loop node is itself kind 2, so it takes the circle branch (which
returns early) and gets no blend; the lead-out applies after the last loop.

### 2.3 Samples and frames (READ `0x19b208`, loop bound `slti 0x11` at `0x19bb50`)

For i = 0..16, t = i/16 (step 0.0625):
```
(pos, S, T) = 0x19bda0(t)
N = T × S;  if N == 0: N = (0,1,0), T = S × N;  else N = normalize(N)
rec[i] = { pos, S (raw), N (unit), T (raw),
           len_i = |C((i+1)/16) − C(i/16)|,          # i = 16 uses t = 17/16 (extrapolated)
           V0 = v,  V1 = v + len_i,                  # rail coordinate, 1 per cell
           W0 = w,  W1 = w + 4·len_i,                # chain/rope coordinate, 4 per cell
           winch flag (+0x44 of the record) }
if i < 16: v = frac(V1), w = frac(W1)                # the wrap happens between quads
```
Record layout, from `+0xd8 + i·0x48`:

| off | field |
|---|---|
| +0x00 | pos |
| +0x0c | S |
| +0x18 | N |
| +0x24 | T |
| +0x30 | len |
| +0x34 | V0 |
| +0x38 | V1 |
| +0x3c | W0 |
| +0x40 | W1 |
| +0x44 | winch flag |

- Starting v and w are the node's `+0x68`/`+0x6c`. After the loop, **next.+0x68 = frac(V0₁₆ +
  len₁₆)** and next.+0x6c = frac(W0₁₆ + 4·len₁₆) (`0x19bc94`, `0x19bca0`). This is skipped when
  next is the exit node, which `0x123698` also resets to 0 each update.
  - So the texture phase jumps by the overshoot len₁₆ at every node (a small seam, READ).
- With θ = heading + half-turn and β = −bank·2π/4096, the per-control-point side vectors are
  (`0x19b70c..0x19b7bc`):
  ```
  S_k = (cos θ_k · cos β_k,  sin β_k,  −sin θ_k · cos β_k)
  ```
  - `0x28c828` is cos and `0x28c910` is sin. This was checked by the fdlibm kernel arity:
    `0x291530(x, y)` is k_cos and `0x291fd8(x, y, iy)` is k_sin.
  - θ_k are unwrapped in sequence: while θ_k > θ_{k−1} + 2048 subtract 4096; while
    θ_k < θ_{k−1} − 2048 add 4096.
- The mesh builders use pos, S and N only.

### 2.4 Arc length and what a "segment position" is (READ)

- `+0xd0` = Σ_{i=0}^{16} len_i: 17 chords, **including the extrapolated t = 1 → 17/16 chord**, so a
  segment's length is over-counted by roughly 1/16. (`add.s` into `+0xd0` for every i, `0x19bab8`.)
- There is **no arc-length reparameterisation**.
  - A train's position p (float) = segment index (§1.4) + t, where t is the spline parameter.
  - A car is posed at t = (p mod 1)·len/len = frac(p) on its segment (`0x1b19e0`, `0x1aeb00`).
  - Following cars step back car-spacing × 1 cell of "distance" d = t·len, borrowing prev.len when
    d < 0.
- Cars therefore move uniformly in t. Their true speed varies with |C'(t)| inside a segment; only
  the per-segment average is right. The speed → Δt rule is in `0x1b0518` (area C).
- Car pose `0x1aeb00`:
  - pos = C(t);
  - up = T × S, then S = up × T, all three normalised;
  - model position = pos × 256 (u);
  - orientation from (S, T).
  The car origin is the track point.

---------------------------------------------------------------------------------------------------

## 3. Heading, pitch and bank

### 3.1 The frame (READ; the handedness remark is INFERRED)

- **Heading per node** (`0x19aa48`), from base positions (`0x19a368`: y_base + stack height sum,
  x/z):
  ```
  dx, dz = self − prev        (or next − self if there is no prev)
  heading = atan2(dx, dz) in 1/4096 turn, [0, 0x1000): 0 = +z, 0x400 = +x, 0x800 = −z, 0xc00 = −x
  kind 2: heading += 0x400
  ```
  - It is computed as `(dx>0 ? 0x400 : 0xc00) − atan(dz·4096/dx)`, with the axis cases handled
    first.
  - atan is `0x195b40` = atan(r/4096)·4096/2π.
- **Half-turn** `+0x4c` (`0x19cdd0`, kind 0 with a next only):
  `d = next.heading − heading`; wrap d into [−0x800, 0x800]; `+0x4c = d >> 1` (arithmetic shift).
  - The frame heading θ = `+0x4a + +0x4c` = the **bisector** of the incoming and outgoing chords.
  - Lead-in and loop nodes keep their chord heading (the loop's is already +90°).
- **Pitch** `+0x48 = −atan(dy·4096/isqrt(dx²+dz²))`. **Nothing in the geometry uses it.** The
  frame's pitch comes only from the curve tangent T. (READ absence in `0x19b208`, `0x19bda0`, fn1s.)
- **Bank:**
  - S_k tilts by β = −bank. With forward F = (sin θ, 0, cos θ), `S = up × F = F rotated +90° about
    +y`, and positive bank lowers the +S edge.
  - up = N = normalize(T × S) (equals +y on a level, unbanked track).
  - Bank is interpolated only through S(t), so it varies smoothly between nodes.
  - **No auto-banking and no curvature term** exist in the node or spline code (READ). The `.sam`
    `fAutoBankDivisor`/`bNoAutoBanking` fields have no PS2 consumer (coaster-survey §2.5).
  - Moonshot's bank is forced to 0 (`0x19c818`).
- Whether +S is the rider's left or right depends only on the world's handedness. A port should use
  the same (x, z) axes as the rest of the park and apply the formulas as written (INFERRED; nothing
  here fixes it).

---------------------------------------------------------------------------------------------------

## 4. Heights and pylons

### 4.1 The base y_base (`0x19cae0`, READ)

| node | y_base (u) |
|---|---|
| station exit/entry | **−0x100** |
| ground pylon (no node below) | `0x149d90(cell_x·256, cell_z·256, 0)` = terrain height at the cell's **min corner**. Tile `+1` byte × 4, bilinear across 4 corners with the fraction = 0 here, so just tile(cx, cz)·4; off-map corners take the max of the others |
| stacked pylon | `(int)(M.y · 256)`, where M is the runtime matrix of the node below's fitting (flags 0x100000, id 1) = its stack-height helper (`StackHeightDummy` / `STACK_HEIGHT` / `StackHeightNode` / `STACK_DUMMY`; mapping in §4.4). MIPS `0x19cbcc..0x19cc2c` reads matrix +0x34. That this matrix is world-space in cells is INFERRED from the ×256 |

- Stack limits (area B detail): `0x19a6e8`, the sum of `+0x44` over the whole stack, must be
  ≤ 0x600.
- `0x19a368` (y_base + own + below heights) feeds only heading, pitch, distance and validity. It
  never feeds the spline.

### 4.2 Pylon pose (`0x19aef8` load, `0x19cdd0` per update; READ unless marked)

- Model id = style table `+0x00` (428..441). It is loaded on the pylon instance with vt `+0xc`.
  - **Bone Shaker's id 435 resolves to registry folder `coasta`**, so it draws Ghosta Coasta's
    pylon. (READ registry `0x2c1c34`: {StdPylon, 9, 435, coasta}. INFERRED that the loader uses that
    folder.)
- Four channels are started by `vt+0x5c(1.0, inst, section, 0, channel)`. Section = the `.aps`
  section, whose port names are the ride-script slot names:

  | channel | `.aps` section | driven by (`0x19cdd0`, via vt `+0x64` → `0x1ad2d8`: time = value × the channel's length, which is the record's `+4` duration as a float, clamped 0.0001 under it — READ `0x1acdd8`/`0x1ad180`) | what the data does (READ, all 14 pylons) |
  |---|---|---|---|
  | 0 loft | 3 | `clamp(+0x44 / 2560, 0, 1)` | vertex-morphs the post; moves `TrackDummyCentre` and the stacker on a 2-point linear path 0 → **90** model units over 100 frames (Gorilla Thrilla 0 → 80, Caterpillar −4.5 → 85.5) |
  | 1 rotate | 10 | (θ in radians wrapped to [0, 2π]) / 2π, with θ = `+0x4a + +0x4c` | yaw of the post: 0 → 360° over 100 frames |
  | 2 incline | 2 | **constant 0.5** | ±45° (±36° on some) about X on the dummy. 0.5 is neutral for the MORPH (MineCart's ±3.5 on the post's top ring is 0 at frame 10) but **not for the UVs**: the post's lofted ring gets V +0.498 there (READ, MineCart) |
  | 3 bank | 9 | `(+0x4e + 512) / 1024` | dummy rolls +45° (bank −512) … 0 … −45° (bank +512) about Z (not every pylon has section 9: Chak Atak, Hades and Caterpillar lack it). **Also deforms the post**: MineCart's morphs its top ring ±3.5 / ±2.6 in y (0 at bank 0) and moves its UVs, +0.25 V on the lofted ring and +0.33..+0.58 on the top section at bank 0 (READ) |

- `vt+0x6c` then applies the pose (→ `0x1ac6e8`). The re-pose is skipped when all four values are
  unchanged.
- The model is placed at `(x − 0x80, y_base, z − 0x80)` u, i.e. the cell's min corner (vt `+0x74` =
  `0x228af0`, ×1/256). The pylon meshes are authored centred on (5, ·, 5) model units, so the
  pylon stands in the cell centre.
- **The pose is additive** (READ `0x1a7f48`):
  - for a SplinePath track (flag 1), `translation += path(t)` when **model header `+0x1c` & 4**,
    else `translation = path(t)`;
  - rotation likewise composes instead of setting (`0x1a6bf8` vs `0x1a4ac8`).
  - All 14 `stdpylon.mps` have header `+0x1c = 0xa5` (bit 0x4 set). The station and car models
    sampled (coaster1, minecart, devil, shocker, cart, croccar) have `0xe9/0xa9/0xa1` (bit clear).
  - In additive mode `0x1ac6e8` first runs `0x1aa460` per channel. That this resets the nodes to
    their bind pose before accumulating is INFERRED (not decompiled); without a reset the pose
    would drift every re-pose.

### 4.3 The track height (READ chain; the numeric reading of attachY is INFERRED from data)

```
ctrl(n).x,z = +0x3c, +0x40                                        (cell centre, u)
ctrl(n).y   = y_base + (int)( attachY · 256/10 + off )            (0x19a420)
  attachY   = local translation y (record +0x44 = matrix M42) of the pylon helper found by
              fitting (flags 0x400000, id 2), else (0x400000, id 1)  -> TrackDummyCentre on all 14
  off       = 0x100 Moonshot (3,0,0); 0 Gorilla Thrilla (0,1,1) and The Shocker (3,1,1)
              [the two .sam "suspended" coasters]; else 0x60      (MIPS 0x19a508, 0x19a5b8)
```
With the additive loft, `attachY = bind_y + p0 + (p1 − p0)·L` and L = clamp(h/2560, 0, 1), so for
the usual pylon (p0 = 0, p1 = 90):

**y_track = y_base + 25.6·bind_y + 0.9·h + off**

That is 0.9 u of track rise per unit of pylon height. bind_y (model units) per pylon:

| pylon | bind_y | path | pylon | bind_y | path |
|---|---|---|---|---|---|
| minecart / coasta (also Bone Shaker) | 5 | 0→90 | c_scat | 5 | 0→90 |
| coaster1, c_hade | 6 | 0→90 | b_drip | 9.179 | 0→90 |
| coaster3 | 15.558 | 0→80 | cat_co | 9.5 | −4.5→85.5 |
| devil | 5.060 | 0→90 | candy_c | 5 | 0→90 |
| moonshot | −0.058 | 0→90 | megacost | 5 | 0→90 |
| shocker | 14.0 | 0→90 | shake (own file, not used) | 5 | 0→90 |

- `0x19a420` reads the helper's **local** y only. The parent mesh's offset is ignored, and so is the
  root's 0.1 scale, which the /10 accounts for.
- The model data must be per instance, since 14+ pylons of one model pose differently and each
  mesh rebuild reads its own. That is INFERRED; the index path runs through
  `0x2eaad0[instance+0x14]`.

### 4.4 Fittings → nodes (READ `0x19a4dc..0x19a518` against the data)

The engine resolves a fitting to a model node as `node = fitting index + header u16 @+0x34`:
- node < header `+0x30` (mesh count) → mesh record `+0x48` table, 0xa0 each;
- else → helper record `+0x4c` table, (node − meshes)·0x60.
- Then `+0x10` (the matrix) `+0x34` = M42.

In all 14 pylons `@+0x34 = 1`, while 12 of them have 2 meshes. Under the engine rule:
- (0x400000, id 2 or 1) lands on `TrackDummyCentre`/`TrackCentreDummy` in **every** file;
- (0x100000, id 1) lands on the stack-height helper in every file.

The port's `Model.Fittings` assigns `node = meshes + index`, which puts these on `TrackDummyOut` and
`base`. **For the port:** use `index + hdr[0x34]`. Only monkey.mps was checked by the port, where
the two rules may coincide.

### 4.5 Station nodes (READ `0x122060`)

- The exit node sits at the cell one step outside DBA `+0xbc`, and the entry at the cell outside
  `+0xc0` (coaster-survey §1.1). y_base = −0x100 and height = float tables `0x2acb60`/`0x2acb64`.
  Their pylon is loaded, posed and hidden.
- **Check against the station models** (the rail helpers in each `<name>.mps`, world y in cells ×
  256). These are predictions from §4.3 against the model, not a reading of the game:

  Computed by script, with the engine's int truncation. Δ = prediction − helper. The closer model is
  in bold.

  | coaster | H | additive (u) | Δ add | "replace" (u) | Δ replace | station helper (u) |
  |---|---|---|---|---|---|---|
  | Temple of Gloom | 375 | 305 | **−3** | 177 | −131 | `Railnode` 308.3 |
  | Chak Atak | 575 | 511 | **−1** | 357 | −155 | `*PylonPosn` 512.0 |
  | Hades | 420 | 371 | **+13** | 218 | −140 | 358.4 |
  | Dare Devil | 265 | 208 | **+29** | 78 | −101 | `track` 179.2 |
  | Scatty Batty | 175 | 125 | **+35** | −3 | −93 | 89.6 |
  | Gorilla Thrilla | 795 | 778 | **+36** | 380 | −362 | 742.4 |
  | Candy Coaster | 285 | 224 | **+43** | 96 | −86 | 181.5 |
  | Escape Velocity | 325 | 260 | **−48** | 132 | −176 | 308.3 |
  | Big Dripper | 290 | 335 | **+51** | 101 | −183 | 283.6 |
  | Caterpillar | 455 | 377 | **+52** | 134 | −191 | 325.0 |
  | Ghosta Coasta | 250 | 193 | +141 | 65 | **+13** | 52.3 |
  | Bone Shaker (coasta pylon) | 355 | 287 | +106 | 159 | **−23** | 181.5 |
  | The Shocker | 1470 | 1425 | +296 | 1067 | **−62** | 1128.6 |
  | Moonshot exit / entry | 1125 / 335 | 1011 / 300 | −72 / −146 | 1012 / 301 | −71 / −145 | `Start`/`EndPylonPosn` 1083.4 / 446.1 |

- **The comparison is mixed. It does not decide the question on its own:**
  - additive is closer on 10 coasters, 2 of them within 3 u;
  - "replace" is closer on 3 (Ghosta Coasta, Bone Shaker, The Shocker);
  - Moonshot is a tie, and both readings miss it.
- Ghosta Coasta and Temple of Gloom share one pylon model, and they favour opposite readings. So
  some of these PC-era helpers cannot be the height the PS2 table was tuned to, and the helpers are
  **not a reliable target**.
- The load-bearing evidence for "additive" is the READ `+0x1c & 4` add branch in `0x1a7f48`
  together with the pylon-only header flag. The stations only corroborate it in part.
- Moonshot's exit node is at 1125 (exit y ≈ 1011 u, about 4 cells) against the entry at 335.
  **Moonshot's station segment is hidden** (`0x19d1e0`: track visible = 0 for (3,0,0)'s exit
  node).

---------------------------------------------------------------------------------------------------

## 5. Track meshes

### 5.1 How a mesh is made (READ `0x19c650`, `0x19cc48`, `0x19d4d0`, `0x168bd0`, `0x169458`, `0x169608`, `0x1696a0`, `0x1691b0`)

- **Style table** `0x2e2b30 + (w·9 + p·3 + o)·0x2c`, where (w, p, o) is the triple after the
  test-park remap `0x11f930`:
  - `+0x00` pylon id;
  - `+0x04` GTexture dir;
  - `+0x08/+0x0c/+0x10` texture slots 0/1/2;
  - `+0x14/+0x1c/+0x24` fn0/fn1/fn2, each a C++ member pointer {s16 this-delta = 0, s16 vindex =
    −1 (non-virtual), fn}.
- **fn0 (create)**, called by the model system through interface `+0x14`:
  - frees the old strips (`0x1998f0`);
  - allocates N strip descriptors of 0x14 bytes (vtable `0x365660`):
    - `+0` = texture slot index;
    - `+0xc |= 1` = **double-sided** (every strip of every style);
    - `+0xe` = quad count;
  - builds the texture list {dir, slot name}, where **every slot becomes `red.ssh` when the node is
    invalid** (`+0x54 == 0`);
  - creates the mesh `0x16a150(desc, "Track", 1)`;
  - lays out the strips (`0x168bd0`, `0x168ec8`) and ADC bits (`0x1691b0`);
  - sets **every normal to (0, 1, 0)** (`0x1696a0(0, 1.0, 0, …)`); normals are never rewritten.
  - When validity flips, `0x19aec0` resets `+0xcc`. The pylon reload (`0x19aef8`) then re-registers
    `+0x14` as the provider of the track instance (vt `+0x1c`), which is how fn0 runs again with the
    red list (the trigger chain is INFERRED).
- **fn1 (build)**, `0x19cc48` when `+0x78 == 0` or forced: writes positions and UVs from the 17
  samples, then sets the bounds from `+0x5a0`.
- **fn2 (per update)**, `0x1237d0` → `0x19d4d0`, for every node every coaster update: rewrites the
  scrolling UVs.
- **Quad API:** `0x169458(x, y, z, …, strip, q, c)` sets the position of corner c ∈ 0..3 of quad q
  (`0x169608(u, v, …)` sets the UV, stored as s16 = value·4095; `0x1696a0` sets the normal as
  s8·127). With the double-sided flag, corners 0 and 1 are also written 4 slots on, making a
  6-vertex strip that draws both windings.
- **Every quad in every style is emitted in this corner order:**
  - c0 = next sample, left edge;
  - c1 = current sample, left edge;
  - c2 = next sample, right edge;
  - c3 = current sample, right edge;
  - triangles (c0, c1, c2) and (c1, c2, c3).
  - Quad q of a band spans samples q → q+1 (q = 0..15); a style's second band is quad q+16, and so
    on.
  - "left/right" below means the first/second edge named.
- **UV rule** (all styles):
  - V at corners c0, c2 is the end value (V1 or W1) of sample q, and at c1, c3 the start value (V0
    or W0).
  - U is a per-edge constant (tables below).
  - V = V0/V1 (1 repeat per cell of arc) or W0/W1 (4 per cell).
- **Scroll phase** φ = coaster `+0xe388`: φ −= 0.08 each coaster update, wrapped into [0,1)
  (`0x123698`). fn2 writes V + φ.
  - Because φ decreases, the pattern moves **forward** along the track at 0.08 V per update, i.e.
    0.08 cells/update on 1× bands and 0.02 cells/update on 4× bands.
  - The update rate itself is area C's.

### 5.2 Cross-sections

With P, S and N from sample i (S raw, N unit), all offsets are in cells (`fconst.py` scan of every
fn1 plus the decompile; style E's S/N assignment checked in MIPS).

**A: flat ribbon.** fn0 `0x19d870`, fn1 `0x19dd20`, fn2 `0x19e468`. Used by Dare Devil, Ghosta
Coasta, Bone Shaker, Caterpillar and The Shocker.
- Edges Q_k = P + k·S with k = −0.3, −0.075, +0.075, +0.3 (floats `0x2e2b00..0c`).
- Strips: 0 = tex slot 0, 32 quads; 1 = slot 1, 16 quads.

| band | quad | edges | U (c0/c1 → c2/c3) | V |
|---|---|---|---|---|
| strip 0 | q | Q0 → Q1 | 0.0 → 0.425 | V (static) |
| strip 0, winch off | q+16 | Q1 → Q3 | 0.425 → 1.0 | V |
| strip 1, winch off | q | zero-size | | |
| strip 0, winch on | q+16 | Q2 → Q3 | 0.575 → 1.0 | V |
| strip 1, winch on | q | Q1 → Q2 | 0 → 1 | **W + φ** (fn2) |

(U constants `0x2e2b10..1c` = 0, 0.425, 0.575, 1.0.)

**B: trough / flume.** fn0 `0x19e5a0`, fn1 `0x19eba8`, fn2 `0x19f580`. Used by Chak Atak, Hades
and Big Dripper.
- Points:
  - p0 = P − 0.36S + 0.1N (left rim);
  - p1 = P − 0.3S;
  - p2 = P − 0.5N (keel);
  - p3 = P + 0.3S;
  - p4 = P + 0.36S + 0.1N.
- Strips: 0 = slot 0 (water), 16 quads; 1 = slot 1 (uphill: `trak_sec2`/`slime_up`/`ratchet`),
  16 quads; 2 = slot 2 (walls: `trak_sec3`/`rib_side2`/`gutter`), 32 quads.
- **Floor choice per quad:** `next.p2.y · 0.99 < cur.p2.y` (absolute y in cells, i.e. level or
  descending within about 1 %) → water on strip 0 and strip 1 zeroed; else uphill on strip 1 and
  strip 0 zeroed.

| band | edges | U | V |
|---|---|---|---|
| floor (strip 0 or 1), q | p1 → p3 | 0 → 1 | V + φ (fn2 rewrites **both** floor strips) |
| wall strip 2, q | p0 → p2 | 1 → 0 | V (static) |
| wall strip 2, q+16 | p2 → p4 | 0 → 1 | V |

**C: rope.** fn0 `0x19f728`, fn1 `0x19fb98`, fn2 empty. Used by Gorilla Thrilla (suspended).
- A diamond tube of radius 0.05: r0 = P + 0.05N, r1 = P + 0.05S, r2 = P − 0.05N, r3 = P − 0.05S.
- One strip, slot 0 (`A_Rope`), 64 quads: q r0→r1, q+16 r1→r2, q+32 r2→r3, q+48 r3→r0.
- U 0 → 1 on each face, V = **W** (4 per cell), static.

**D: triangular beam.** fn0 `0x1a01b0`, fn1 `0x1a05a0`, fn2 empty. Used by Scatty Batty.
- d0 = P − 0.1S, d1 = P + 0.1S, d2 = P − 0.1N.
- One strip, slot 0 (`hw_pillar3`), 48 quads:
  - q d0→d1, U 0 → 0.5;
  - q+16 d1→d2, U 0.5 → 1;
  - q+32 d2→d0, U 0.5 → 1.
- V static. Slot 1 (`chain.ssh`) is never drawn.

**E: big tube.** fn0 `0x1a0a98`, fn1 `0x1a0f08`, fn2 `0x1a1368`. Used by Moonshot.
- e0 = P − 0.4S, e1 = P + 0.6N, e2 = P + 0.4S, e3 = P − 0.6N.
- One strip, slot 0 (`rail_tube`), 64 quads: q e0→e1, q+16 e1→e2, q+32 e2→e3, q+48 e3→e0.
- UVs only from fn2: U 0 → 1, V + φ on all faces.

**F: twin rails.** fn0 `0x1a1600`, fn1 `0x1a1968`, fn2 empty. Used by Escape Velocity.
- f_k = P + k·S with k = −0.3, −0.1, +0.1, +0.3 (`0x2e2b20..2c`).
- One strip, slot 0 (`sc_rail`), 32 quads:
  - q f0→f1, U **1 → 0** (mirrored);
  - q+16 f2→f3, U 0 → 1.
- V static. Slot 1 (`ss_post`) is never drawn.

**G: mine trough.** fn0 `0x1a1d28`, fn1 `0x1a23b0`, fn2 `0x1a2f68`. Used by Temple of Gloom and
Candy Coaster.
- Points:
  - g0 = P − 0.54S + 0.4N;
  - g1 = P − 0.3S;
  - g2 = P − 0.5N;
  - g3 = P + 0.3S;
  - g4 = P + 0.54S + 0.4N;
  - g5 = 0.575·g1 + 0.425·g3 = P − 0.045S;
  - g6 = 0.425·g1 + 0.575·g3 = P + 0.045S.
- Strips: 0 = slot 0 (`mc_rail1`/`curwir_trk`), 32 quads; 1 = slot 1 (`chain`), 16 quads; 2 =
  slot 2 (`mc_struts`/`mat_trk`), 32 quads.

| band | edges | U | V |
|---|---|---|---|
| strip 0, winch off, q | g1 → g3 | 0 → 1 | V |
| strip 0 q+16 and strip 1 q, winch off | zero-size | | |
| strip 0, winch on, q | g1 → g5 | 0 → 0.425 | V |
| strip 0, winch on, q+16 | g6 → g3 | 0.575 → 1 | V |
| strip 1, winch on, q | g5 → g6 | 0 → 1 | W + φ (fn2) |
| strip 2, q / q+16 | g2 → g0 / g2 → g4 | 0 → 0.95 | V |

- **No sleepers, rail tubes or supports exist as geometry** in any style. Rails, sleepers and chain
  links are all texture.
- "Winch" is read only by A and G. Only 0x19dd5c and 0x1a27b4 load `+0x11c` in `0x199000..0x1a3400`.

### 5.3 Visibility (READ `0x19d1e0`)

- The track is visible iff: it has an owner, it is not the ghost, prev ≠ 0, prev's base position
  ≠ own (x, y, z all compared), **and** it is not Moonshot's exit node.
- The entry node's track appears only once the ring is closed, i.e. when it has a prev.
- The pylon is visible iff: owned, not the ghost, and not a station node.

---------------------------------------------------------------------------------------------------

## 6. The lift (READ `0x1239d8`, `0x123b28`, `0x19b1a0`, `0x19b180`)

- **When:** `0x1230b0` (spawn trains) calls `0x1239d8` first. `0x1230b0` is itself called from
  state-10 `0x122af8` (closed, no trains) and after the test run `0x122d48`. It does nothing
  unless closed (`+0x148`) and valid (`+0x144`).

| step | does | exit condition |
|---|---|---|
| 1 | clear winch flags (`0x19b180`) on exit, p0 … p(n−1) (the entry node is not cleared) | |
| 2 | `0x123b28(0, 2.0)`: flag the station segment and the first segment | |
| 3 | spawn one train at position 1.0 (`0x1231e8`) and remember p | |
| 4 | run the state-0 step `0x1b0518` directly; if train `+0x254` is set, flag [p_old, p_new] | loop while p increases (it drops at the lap wrap) |
| 5 | remove trains (`0x1224c8`); force-rebuild meshes exit … p(n−1) (vt `+0x144`, arg 1) | |

- `+0x254` = 1 from spawn until the train's speed first exceeds 0.04 (coaster-survey §6.1). So the
  flagged span is **the station, the first segment, and the climb at the 0.04 speed floor up to
  the first crest**. That section is the chain lift (a behavioural reading of READ code).
- `0x123b28(a, b)`: start at the exit node, subtract whole segments from a (and b) to reach the
  segment holding a, then flag within segments, carrying over each segment boundary.
- `0x19b1a0(a, b, node)`: flag = 1 for sample i = ⌊16a⌋ … ⌊16b⌋. The mesh quad q reads flag q, so
  the chain is quantised to 1/16 segment.

---------------------------------------------------------------------------------------------------

## 7. Per-coaster summary for a port (READ tables; the per-row heights use §4.3)

| coaster (w,p,o) | style | slot 0 / 1 / 2 (`GTexture\`) | off | notes |
|---|---|---|---|---|
| Temple of Gloom (0,0,0) | G | mc_rail1 / chain / mc_struts | 0x60 | |
| Chak Atak (0,1,0) | B | water2 / trak_sec2 / trak_sec3 | 0x60 | pylon has no bank section |
| Gorilla Thrilla (0,1,1) | C | A_Rope / – / – | **0** | loft 0→80 |
| Hades (1,0,0) | B | Slime2 / slime_up / rib_side2 | 0x60 | no bank section |
| Dare Devil (1,0,1) | A | flmtrk / chain / – | 0x60 | |
| Scatty Batty (1,1,0) | D | hw_pillar3 / (chain unused) / – | 0x60 | |
| Ghosta Coasta (1,1,1) | A | gt_rail1 / chain / – | 0x60 | |
| Bone Shaker (1,1,2) | A | Track01 / Track02 / – | 0x60 | pylon 435 → coasta's |
| Big Dripper (2,0,0) | B | paint_flow / ratchet / gutter | 0x60 | |
| Caterpillar (2,0,1) | A | beanpole02 / beanleaf01 / – | 0x60 | loft −4.5→85.5, no bank section |
| Candy Coaster (2,1,0) | G | curwir_trk / chain / mat_trk | 0x60 | |
| Moonshot (3,0,0) | E | rail_tube / (rail_tube unused) / – | **0x100** | bank forced 0; station segment hidden; exit 1125 / entry 335 |
| Escape Velocity (3,1,0) | F | sc_rail / (ss_post unused) / – | 0x60 | |
| The Shocker (3,1,1) | A | S_RAIL_SPARK_01 ×2 | **0** | chain band uses the rail texture |

A port's per-frame work for one coaster:
1. Update the windows.
2. Pose each pylon (§4.2) and take `TrackDummyCentre`'s posed local y.
3. For dirty nodes, rebuild the 17 samples (§2.3) and the style's quads (§5.2).
4. Advance φ and rewrite the scrolled bands.

---------------------------------------------------------------------------------------------------

## 8. Units, settled for the coaster code (READ unless marked)

Every one of these treats y exactly like x and z:
- control points divide x, y and z by the same 256 (`0x19b208`);
- sample and arc lengths are 3D Euclidean (`sqrt(dx²+dy²+dz²)`, `0x19ba90`);
- node distance `+0x50` is the 3D isqrt in u (`0x19aa48`);
- the loop has vertical radius 3 and horizontal radius 3·|D| with |D| = 1 cell (§2.2);
- model units convert by ×256/10 (`0x19a420`), matching the grid's 10 model units per cell;
- the loft divisor is 2560 = 10 cells;
- car gravity is `speed += Δy·0.04`, with speed in the same float cells as distance.

**Coaster y is 256 u per cell, the same as x/z** (READ as code behaviour). Independently, the formula
reproduces Temple of Gloom's and Chak Atak's station rail helpers, which are model world y in cells,
to 3 u and 1 u (§4.5, data).

This corroboration is conditional on the additive pose. Under "replace", the two stations would
need different scales (147 and 179 u per cell), so no single scale fits them that way. The code
facts above stand without it.

The **terrain base** is the outlier: `0x149d90` returns `tile[+1]·4`, so a terrain step of 1 is
4 u = 1/64 cell. This is the same `<<2` the track-ride notes flagged. For coasters it only sets
where a ground pylon's foot is, and retail tiles are mostly 0 or 2 (8 u). This pass did not
reconcile it with the heightfield's 0..2 "model unit" envelope.

Useful magnitudes (u unless noted):

| quantity | value | cells |
|---|---|---|
| max pylon height | 0x500 = 1280 | 5 (track rise 0.9× = 1152 u) |
| stack cap | 0x600 | 6 |
| loop radius | 3.0 | 3 |
| track attach lift | 0x60 = 96 | 0.375 |
| cross-section widths | ±0.3 (A, F, trough floors), ±0.54 (G rims), 0.8×1.2 (E) | |

---------------------------------------------------------------------------------------------------

## Still unknown (with what was tried)

- **Reset to bind before additive posing.** `0x1aa460`, called in additive mode by `0x1ac6e8`, was
  not decompiled. The "bind + path" reading rests on:
  - the `+0x1c & 4` add branch in `0x1a7f48` (READ);
  - the station check, which is mixed: 10 coasters favour additive, 2 of them within 3 u, and 3
    favour "replace" (§4.5).
  A live read would settle it: a PCSX2 savestate showing a node's `+0x98` (P2.y) against its
  `+0x44` and `+0x3e`.
- **Whether the per-instance model data is really per instance.** It must be, or every pylon would
  share one pose. The chain `0x2eaad0[inst+0x14] → +8 → +4` was not traced to an allocator.
- **Handedness of S** (rider's left or right) in the port's frame. Nothing in this code fixes it
  (§3.1).
- **How the ghost node is drawn:** `0x19d1e0` hides both its parts. The tool's draw `0x11b2f8` (area
  B) was not read for it.
- **The fn0 re-creation trigger when validity flips** is INFERRED through `0x19aec0` → `+0xcc = −1`
  → `0x19aef8` → vt `+0x1c`. The model system's side was not read.
- **Readers:**
  - `+0x5e8` has none, found by a whole-`.text` imm scan;
  - `+0x58` has none among the node, coaster and style decompiles.
- **`0x16a150`'s texture descriptor fields** (the `u16 1` per entry, the `1` flag) were not decoded,
  so self-illumination and alpha of the track textures are unknown.
- **Tick rate of the coaster update**, which sets the real scroll speed. That is area C.
- **Terrain `tile·4` versus the heightfield envelope** (§8): untouched here.
