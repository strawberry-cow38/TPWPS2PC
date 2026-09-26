# Roller coasters (PS2): what they are made of

Researched 2026-09-26 for strawberry, the "what" pass on roller coasters. Sources: the coasters' own files
on the PS2 disc, read through the port's `TPW.PS2.Data` readers, and `SLES_500.32` decompiled in Ghidra 12.1.2
(R5900 extension), with constants checked in raw MIPS. The "how" is in `coaster-geometry.md`,
`coaster-building.md`, `coaster-trains.md` and `coaster-operation.md`. Where those correct this survey, `coasters.md`
lists the corrections.

**READ** = seen in the decompile/MIPS/data. **INFERRED** = reasoned to. Where a name is the designers'
(debug string, text key, `.sam` comment) it says so; other names are mine.

---------------------------------------------------------------------------------------------------

## 0. Short version

- **Coasters are a completely different machine from track rides.** A track ride is a 2-cell-lattice
  loop of fixed 2×2 pieces (48-entry type table). A coaster is a **ring of up to 32 player-placed
  pylons** (plus two fixed station nodes), and the track between consecutive pylons is a
  **Catmull-Rom spline through 4 pylon positions**, extruded into a mesh by one of **7 native
  per-coaster mesh builders**. There is **no piece-type table and no piece meshes** on disc. (READ)
- **Real elevation.** Each pylon has a height 0..1280 (tool step 20), pylons stand on the terrain,
  can be **stacked** in one cell, and each carries a **bank** angle ±512/4096 turn (±45°). (READ)
- **Special pieces are node kinds, not shapes:** a **loop** is two extra nodes (kind "loop lead-in"
  and "loop"); the loop segment is a vertical circle of radius 3 cells. No corkscrews, no brakes as
  pieces. A lift/winch section is not placed either: it appears to be derived from where the train
  runs at the 0.04 speed floor (INFERRED). (READ/INFERRED as marked in §4)
- **Trains run on gravity.** Each car gains speed `0.04 × (height lost)` per step; the train averages
  its cars and loses 0.1 %/step (4 %/step on the station segments); speed never drops below 0.04; a
  loop freezes the speed. Trains block each other at 0.75 segment. (READ, constants checked in MIPS)
- **The script is dead code again.** Every coaster ships `<name>.rss` using `COAST` sub-opcodes; the
  PS2 `COAST` handler `0x1c14e0` is a stub like `BUMP` (GETQUEUE and GETPEEP answer 0, the rest
  discard). Native trains board guests straight from the queue. (READ)
- **`coaster.sam` is the PC-era track description** (cross-section polygons, pylon/car/train/coaster
  physics settings). Its full field schema is still in the ELF (`0x2acf1c..0x2b28a4`) but no consumer
  was found, and the native per-coaster tables contradict it (e.g. cars per train). Treat it as
  intent, not behaviour. (READ absence of refs, INFERRED conclusion)
- **2 coasters per normal park; 14 in the "Rollercoaster Test Park"** (park index 2), which is unlocked
  by building an "Ultimate Coaster". The pool holds 14 objects of 0xe390 bytes. (READ)
- **The track's shape only reaches the stats screen**, the rating text and the Ultimate award. The
  ride value (Excitement) is `B × speed factor × duration factor` with duration fixed at 1; no track
  term, unlike track rides. (READ formula; "only" = no other reader found, see §8)

---------------------------------------------------------------------------------------------------

## 1. The coasters

14 compiled records of `AssetKind` 1 (EUR `DATA.WAD:/arsdb.dba`), all type-data extent `0xb4`.
Park = the world's `terrain_1`/`terrain_2` native attraction list (`NativeBusCatalogue.Keys(1)`,
lists at `0x2b76c8..` per `NativeBusCatalogue.cs`). **Ordinal** = position in that list; the object
stores it at `+0x97` (`0x1e1e98`) and every per-coaster native table is indexed
`[world][park 0..2][ordinal 0..2]`. The ordinal join is confirmed by the car-model table `0x2e7220`,
whose ids 442..455 are the model-registry entries `cart/minecart`, `croccar/coaster1`, `ape/coaster3`,
`maggot/c_hade`, `car/devil`, `bat/c_scat`, `cart/coasta`, `car/shake`, `car/b_drip`, `caterbd/cat_co`,
`car/candy_c`, `car/moonshot`, `cart/megacost`, `car/shocker` (registry `0x2bf2b8..0x2c32fc`, 0x24-byte
entries {name, 0, category, id, 1, folder}). (READ)

| world | park | ord | key | folder | player name (EUR text) | base exc. `+0x18` | footprint W×D | style | cars/train `0x2acc80` | car spacing `0x2acc84` | loops `0x2acad0` | station node heights exit/entry `0x2acb60/64` | sound family `+0xd3&3` |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| JUNGLE | 1 | 0 | 225 | `Rides/MineCart` | Temple of Gloom | 90 | 4×3 | G | 4 | 1.0 | yes | 375 / 375 | 2 |
| JUNGLE | 2 | 0 | 217 | `Rides/Coaster1` | Chak Atak | 90 | 2×3 | B (trough) | 1 | 1.0 | no | 575 / 575 | 1 |
| JUNGLE | 2 | 1 | 218 | `Rides/Coaster3` | Gorilla Thrilla | 95 | 4×4 | C | 1 | 1.0 | yes | 795 / 795 | 2 |
| HALLOW | 1 | 0 | 132 | `rides/c_hade` | Hades | 90 | 3×3 | B (trough) | 1 | 1.0 | no | 420 / 420 | 1 |
| HALLOW | 1 | 1 | 169 | `rides/devil` | Dare Devil | 90 | 4×3 | A | 2 | **1.25** | no | 265 / 265 | 1 |
| HALLOW | 2 | 0 | 133 | `rides/c_scat` | Scatty Batty | 95 | 4×5 | D | 2 | 1.0 | yes | 175 / 175 | 0 |
| HALLOW | 2 | 1 | 135 | `rides/coasta` | Ghosta Coasta | 90 | 3×3 | A | 2 | 1.0 | yes | 250 / 250 | 2 |
| HALLOW | 2 | 2 | 170 | `rides/shake` | Bone Shaker | 90 | 4×3 | A | 3 | 1.0 | yes | 355 / 355 | 2 |
| FANTASY | 1 | 0 | 47 | `Rides/b_drip` | Big Dripper | 90 | 4×3 | B (trough) | 2 | 1.0 | no | 290 / 290 | 1 |
| FANTASY | 1 | 1 | 52 | `Rides/cat_co` | Caterpillar Coaster | 90 | 4×3 | A | 3 | 1.0 | yes | 455 / 455 | 2 |
| FANTASY | 2 | 0 | 51 | `Rides/candy_c` | Candy Coaster | 95 | 4×3 | G | 4 | 1.0 | yes | 285 / 285 | 0 |
| SPACE | 1 | 0 | 371 | `Rides/moonshot` | Moonshot | 90 | 5×3 | E | 1 | 1.0 | yes | **1125 / 335** | 0 |
| SPACE | 2 | 0 | 370 | `Rides/megacost` | Escape Velocity | 90 | 4×3 | F | 2 | 1.0 | yes | 325 / 325 | 0 |
| SPACE | 2 | 1 | 376 | `Rides/shocker` | The Shocker | 95 | 5×3 | A | 1 | 1.0 | no | 1470 / 1470 | 0 |

Notes (all READ unless marked):
- **"loops"** is table `0x2acad0` (u32 [4][3][3], read by `0x122e48`). It selects the Square-button
  label "Loop" (`STR_PYLON_TYPE_LOOP`, text row 0x414) versus blank, and gates the Square handler
  `0x11b828 → 0x11c668` (loop placement). The three troughs, Dare Devil and The Shocker cannot loop.
- **Car spacing** `0x2acc84` (float, via `0x123430`) is subtracted per car when placing a train
  (`0x1b19e0`): distance between cars along the curve. Only Dare Devil differs (1.25).
- **Sound family** = DBA `+0xd3` bits 0–1 (`0x122ce8`, the "bits 24–25" of dba.md). Family 1 swaps
  the train's sound events (`0x1af330`); it is exactly the three troughs plus Dare Devil. The trough
  event maps use `EVT_WATER` where the others use `EVT_COAST_RUMBLE` (READ the `.rss`); that family 1
  means "wet" is INFERRED.
- **Park index 2 ("Rollercoaster Test Park")**: `0x11f930` remaps `(world, 2, ord 0..13)` through
  `0x2ace10` (12-byte {world, park, ord} entries) to the 14 home triples in table order above, so the
  test park can build all 14. Stock `0x14ccb0` = (park index == 2 ? 14 : 2) − used (MIPS
  `0x14ccc0..0x14ccd4`). Text: `STR_FRONTEND_ROLLERCOASTER_TEST_PARK` "Rollercoaster Test Park",
  `STR_COASTER_TEST_PARK_CHOOSE` "Choose your Rollercoasters", `STR_ADVMES_ULTIMATE_COASTER` "...you've
  opened the Rollercoaster Test Park". The link from the Ultimate rating to the unlock is INFERRED from
  that text plus the award call `0x1542b0` in `0x122ed0` (§6.4).
- Player names come from `STR_GRAPHICS_*` rows (e.g. "Chak Atak"); the `.sam` `Info.Name` differs in
  places ("Chac Atak", "Boneshaker", "Shocker").
- **PS2-only coasters:** Dare Devil and Bone Shaker have **no `coaster.sam`** (the other 12 do), which
  fits `psx-vs-ps2-rides.md` listing them as PS2-only.

### 1.1 DBA kind-1 type data `+0xbc..+0xd3` (the unresolved "coaster geometry bytes")

| off | Chak Atak example | meaning | consumer |
|---|---|---|---|
| `+0xbc` s16,s16 | (0, 1) | local cell of the **track exit** (start of the track) | `0x11fdd0` (vt `+0x194`): rotate by station rotation (`0x1e2288`), add anchor `+0x84/+0x88`, then step one cell in direction `((d0>>28)&3) + rotation` (0: z−1, 1: x−1, 2: z+1, 3: x+1). READ |
| `+0xc0` s16,s16 | (1, 1) | local cell of the **track entry** (where the loop closes) | `0x120288` (vt `+0x1b4`) same, direction bits 30–31; `0x1200e0` (vt `+0x1ac`) unstepped. READ |
| `+0xc4` 2×s16 | (140, 140) | unknown; −64/−128/−256/−512/−100, Moonshot (564, −220), Shocker (970, 970) | none found. Not read |
| `+0xc8` u32 | 0x10 | unknown (0 or 16) | none found |
| `+0xcc` u32 | 0x80 | unknown (128; Moonshot 0) | none found |
| `+0xd0` u16 | 100 | **price per pylon** (100 in all 14) | tool enter `0x11ad60` @`0x11aeb4` and `0x11ceb0` @`0x11cf4c` copy it to tool `+0x3c`; charged ×10. READ |
| `+0xd2` u8 | 4 | unknown (4/5/6) | none found |
| `+0xd3` u8 | 0xDD | bits 0–1 sound family; bits 4–5 exit direction; bits 6–7 entry direction | `0x122ce8`, `0x120020` (vt `+0x19c`), `0x120080` (vt `+0x1bc`). READ |

Tiers (all 14): `MinDuration = MaxDuration = 1`, speed 1..100, `CapacityParameter` e.g. 18/24/30, wear
5/3/2. The capacity parameter is not used for coasters (vt `+0x344` overridden, §3.2). (READ)

---------------------------------------------------------------------------------------------------

## 2. Their files

### 2.1 Folder contents (READ, WAD listing (local scratch))

Every coaster folder has the same shape; **no track-piece meshes**:

| file | what | notes |
|---|---|---|
| `<name>.mps` + `.aps` | the **station** model (the ride's own model) | e.g. `coaster1.mps`, `minecart.mps`, `shake.mps` |
| car mesh | the car | `cart` (MineCart, coasta, megacost), `croccar` (Coaster1), `ape` (Coaster3, no `.aps`), `maggot` (c_hade), `car` (devil, shake, b_drip, candy_c, moonshot, shocker), `bat` (c_scat), `caterbd`/`caterhd`/`catertl` (cat_co) |
| `stdpylon.mps` + `.aps` | the **pylon** (support) | animated/morphed per pylon (§4.6). megacost also has `md2back/stdpylon.*` |
| `<name>.rss`/`.RSE` | the ride script | `COAST` sub-opcodes, §2.3 |
| `EventMap.rss`/`.RSE` | sound event map | devil has no `.rss` source, only `.RSE`; shake has `.rss` only |
| `<name>.sam` | ride description | §2.4 |
| `coaster.sam` | PC track/coaster description | 12 of 14 (not devil, shake), §2.5 |
| `textures/` | station/car textures | 36–56 files |
| `GTexture/` | **track textures** | every one has `red.tga/.ssh` plus the style's textures (§4.5) |
| `sign/` | park-sign textures | missing for Coaster1, Coaster3, c_hade, c_scat, moonshot, shocker |
| `vssver.scc` | SourceSafe residue | |

- **Unused on PS2 (INFERRED from the registry):** `caterhd` and `catertl` ship on disc but have no
  string in the ELF; only `caterbd` (id 451) is registered, so every Caterpillar car is drawn with the
  body mesh.
- **Pylon registry quirk:** pylon ids 428..441 (category 9, name `StdPylon`) are per coaster, in the
  ordinal order above, but entry 435 (Bone Shaker's slot) names folder `coasta`, not `shake`. Whether
  Bone Shaker therefore draws Ghosta Coasta's pylon is not established.
- `DATA.WAD:/Ultimate/<coaster>/Textures/sign_{eng,fre,ger,jap,jpn}.*` and `/Ultimate/Sharetex/` hold
  more coaster texture copies (`TRACK01`, `curwir_trk`, `gt_pylon*`, `mc_pylon*`, `sc_pylon`,
  `RAIL_TUBE` ...). Their consumer was not traced.
- Coaster sounds: `/AUDIO/RIDES/COASTHD.SDT` (no map traced).

### 2.2 How the track is textured (READ)

The track textures are **named in the executable**, not in `coaster.sam`: per-coaster **style table**
`0x2e2b30` (§4.5) holds the GTexture directory and up to 3 `.ssh` names, e.g. MineCart
`mc_rail1 / chain / mc_struts`, Coaster1 `water2 / trak_sec2 / trak_sec3`, Shocker
`S_RAIL_SPARK_01` twice. Strings `0x3650e8..0x3654a8`.

### 2.3 The script layer, and why it doesn't matter

`Coaster1.rss` (all 14 are this template; devil/shake/c_scat/space add `LOOPANIM`/`WALKON/WALKOFF`,
devil `#setwalk 40`):

1. `SPAWNSOUND "EventMap.rse"`, `WAITANIM ANIM_Create`, `COAST_INITIALISE`, `COAST_SETCLOSED 1`;
2. wait while `VAR_RIDECLOSED`;
3. loop: `SETCAPACITY VAR_CAPACITY`, `SETCLOSED`, `SETWORN`; `GETPEEP` → `VAR_LETMEOFF` (one off);
   under `CRIT_LOCK`, if `GETQUEUE` and `VAR_LETMEON` → `ADDPEEP`, `VAR_ONRIDE += 1`;
4. broken: smoke particle, `SETBROKE 1`, wait, `REPAIREFFECT`, `WAIT 3000`, `SETBROKE 0`.

`.sam` `Info.RunsContinuously 1` on all 14: the intent is a continuously running ride that boards
while trains circulate.

**COAST selectors** (numbers from the compiled `.RSE`, `tools/rse.py`): 1 ADDPEEP, 2 GETQUEUE,
3 GETPEEP, 4 SETBROKE, 5 SETCLOSED, 6 SETCAPACITY, 7 SETWORN, 8 INITIALISE.

**`COAST` handler `0x1c14e0` is a stub** (READ, matches `rse-vm.md`): selectors 1,4,5,6,7 fetch and
discard the operand (`0x1bbd18`), 8 fetches and discards, 2 and 3 set `LastValue (+0x48) = 0` and write
0 into the variable operand. So GETQUEUE is always 0 (the script never admits) and GETPEEP always 0
(never lets anyone off). The coaster overrides every status tick that would run the ordinary boarding
routine `0x1166a8` (vt `+0x264/+0x2a4/+0x2ac`, §3.2) and boards natively (§6.3).

**Event maps:** `EVT_WATER` (troughs) or `EVT_COAST_RUMBLE`, then `EVT_COAST`, `EVT_STRETCH`, two
`EVT_LOC_NULL`; parameters `<WORLD>_COASTER_STATE` and `PYLON_STATE`. The native sound calls
(`0x111428(bank, event, pos, handle, loop)`) are listed in §6.5; the id-to-name mapping is not done.

### 2.4 `<name>.sam` (ride description; READ from disc)

- `Bumper.WhichTrackType 3`, `Bumper.BumperType 0` ("???????"), `Bumper.{N,E,S,W}{X,Y}Adjust` = N(2,1)
  E(1,0) S(0,1) W(1,2) "some constants to line up the Track sections with the rest of the co-ordinate
  system". These do **not** equal the DBA `+0xbc/+0xc0` pairs; consumer not traced (for track rides
  they turned out to be immediates in code).
- `UsageInfo.MaxCapacity` / `Upgrades[i].InitCapacity`: e.g. MineCart 24 / 16,19,22; Chak Atak 6 /
  6,6,6; Moonshot 3. Capacity on PS2 is computed natively instead (§3.2).
- `Info.RunsContinuously 1`, `Info.DurationUnit 0`, `Info.RideTypeStringIndex` 2 (3 for the troughs),
  `UsageInfo.ExcitementLevel` 90/95/100, `Info.Hoarding`, `Info.Shape` (matches DBA footprints).
- No `SupplementalMeshes` in any coaster `.sam` (car/pylon meshes come from the registry).

### 2.5 `coaster.sam` (PC track description; READ from disc and ELF)

Each file opens with a designer comment ("Chak Atak - Lost Kingdom water flume", "Gorilla Thrilla -
Lost Kingdom advanced coaster", "Big Dripper - Fantasy log flume", "Hades - Halloween log flume",
"Scatty Batty - Halloween less than simple coaster", "Catapilla Coaster - Wonder Land advanced
coaster", ...) and defines:

| block | fields (schema order) | examples |
|---|---|---|
| `asTextureData[20]` | `pcTextureFilename`, `bIsSelfIlluminating`, `fScrollRate` | `water2.tga` scroll 1.2 |
| `asCrossSectionPoints1..20[20]` | `fX fY fU fNx fNy` | the trough profile (5 points + UV-scroll duplicates) |
| `asCrossSectionEdges1..20[20]` | `usTexture usCSPoint1 usCSPoint2` | |
| `asCrossSections[20]` | `usCSPointBlockIndex usCSEdgeBlockIndex usTrackEdge usBlendToCSPointBlockIndex fBlendLength` | dry, wet, dry→wet, wet→dry |
| `asCrossSectionSelects[20]` | `fSpeedRange* fDirectionRange* fPitchRange* fBankRange* usCrossSectionIndex usBlendFrom/To...` | wet when pitch −90..−1 |
| `asPylonControls[2]` | `pcMeshFilename uiPylonTypeUniqueID bDontDisableBaseOnStack fCollisionPossibleRadius fPylonStraightDotThreshold uiPylonWidthCells uiPylonLengthCells uiMaxStackedPylons fMinHeight fMaxHeight fMaxHeightDiffPerGSquareDist f{Entrance}{Min,Max}XZAngleAt{Min,Max}Dist fMaxRotnAngleDeviance fMaxLoopRotnAngleDeviance uiMinPlaceDist uiMaxPlaceDist fMaxLoopHeightDifference fCostPerUnit fCostPer2DLength fCostPerHeight` + `{Loft,Rotate,Incline,Bank,LoopRotate}Anim{StartCap,EndCap,Multiplier,Add}` | `StdPylon`, stacked 3–4, cost 40/10/10 |
| `asCarTypes[3]` | `bBendCar uiCarTypeUniqueID pcMeshFilename fFrontDistanceAdd fRearDistanceAdd fYOffset fAnimSpeedMultiplier` | Caterpillar: `caterhd`/`caterbd`/`catertl` |
| `sTrainType` | `uiMaxCars uiFront/Centre/RearCarTypeIndex fFront/Std/EndCarSpacing` | |
| `asTrackCollisionPoints[6]` | `fCollisionX fCollisionY` | |
| `sCoasterType` | `fCurviness fStraightDivisor fAutoBankDivisor fAccelerationPerHeight fUphill/DownhillAccelModifier fInitialPylonLoftHeight uiMaxTrains uiTrainReleaseRate bSpecialIsTrough bIsSuspendedCoaster bNoAutoBanking bVisibleEndPylon uiInvalidCrossSection uiWinchCrossSection fTrackHeightAdjust fWinchSpeed fMinSpeed fMaxSpeedAtMin/MaxSetting fFrictionMultiplier fBendElevationLow/High fGravity{X,Y,Z} fForceMultiplier fInitial{Sickness,Excitement,Uncomfortableness} f{GForce,UpsideDown}SicknessModifier f{Speed,GForce,UpsideDown,Bank}ExcitementModifier f{XGForce,Inverted}UncomfortableModifier f{Normal,Terror}Scream{StartExciteDelta,SustainExcitement} fEntranceSizeInterval iTrackSectionMesh{Verts,Faces}` | Chak Atak `fAccelerationPerHeight 0.8`, `fFrictionMultiplier 0.95`, `uiMaxTrains 20` |

**The schema is in the ELF** as 0x3c-byte records {u32 type, char name[], u32 count, u32} from
`0x2acf1c` to `0x2b28a4` (type 3 = array with count, 2 = struct open, 7 = float, 6/5 = integer,
10 = string; my labels). It sits next to the Bumper schema pattern documented for track rides.

**No consumer found (READ the search, INFERRED the conclusion):** Ghidra shows 0 references to the
schema records; there is no aligned 4-byte pointer to any record; a `lui/addiu` scan of `.text` for
values in `0x2acf00..0x2b2900` gave only false hits (stale `lui` registers, spot-checked at
`0x132044`, `0x1353d0`). The PS2 behaviour disagrees with the file where they overlap:
- cars per train: `.sam uiMaxCars` 4/1/4/1/6/4/1/6/3/1/4/3 vs native `0x2acc80` 4/1/1/1/2/2/2/2/3/4/1/2/1
  (9 of 12 differ);
- gravity/friction are single hard-coded constants (§6.2), not per-coaster `fAccelerationPerHeight` /
  `fFrictionMultiplier`;
- track textures come from the ELF style table, and the cross sections from native mesh code (§4.5).

---------------------------------------------------------------------------------------------------

## 3. The native coaster class (`CCoaster`)

Name from debug strings `0x35b010` "  CCoaster::Save", `0x35b028` "  CCoaster::Load"; pool name
`0x35fe30` "PoolOfCoasters"; overflow message `0x35b048` "TRACK PYLON OVERFLOW". (READ)

### 3.1 Pool, constructor, allocation (READ, MIPS `0x1481b4..0x1481c8`)

- **Pool** in `0x147eb0` at `0x1481b4..0x148330`: `0x17a370(0xc71f0)` = 0x10 header + **14 × 0xe390**
  bytes (loop counter `s3 = 0xd .. −1`), registered as "PoolOfCoasters" (`0x15fa38(pool, 0x35fe30,
  0xc71f0, 0xe390, 0xe)`), published at `0x395298`. Free list through `+0x130/+0x134`.
- **Per-object construction** (inline in the pool builder, and standalone ctor `0x123d80`): base
  `0x1e0e60`, vtable `0x35a560` then **`0x35b060` at `C+0x10`** (C = P for coasters, no `+8`
  subobject), `0x1a3310(+0xa0)`, lists `+0xf4` and `+0x108` (self-linked), **34 node objects** of
  0x620 bytes via `0x1997d0` at `+0x174`, `+0x794`, `+0xdb4 + 32×0x620`, a train sub-pool header at
  `+0xd1b4` (`0x1df0b0`, `0x15f9f8(.., 0x11b8)`) with **6 trains** of 0x2f4 bytes via `0x1af050` at
  `+0xd1c4`, then `+0x170 = +0x148 = 0`.
- **Destructor** `0x11f998` (vt `+0x0c`): trains `0x1af118`, nodes via their vt `+0x0c`, station nodes
  `0x199878`, restore base vtable, `0x1e0ed0`; also `0x19b130(0x2ac438)` (the tool's ghost node).
- **Allocator** `0x14a4a0`: pop from `0x395298`, call vt `+0x154` (init, `0x11fae0`) with the ordinal,
  `0x1e1d60(obj, 1)` (kind 1).
- **Build menu** `0x197f48`: kind 1 → **tool mode 11** (jump table `0x364ec0`, target `0x19804c`,
  tutorial event 0x1b). (Kind 6 → mode 7, kind 8 → mode 10.)

### 3.2 Vtable `0x35b060` against the base `0x35a560`

8-byte entries `{s16 delta, s16 0, u32 fn}`; the table has 110 entries (`+0x004..+0x36c`, then 0).
All deltas are 0. `**` = differs from the base ride table `0x35a560`.

```
+004 {0,000000}   | +00c {0,11f998}** | +014 {0,109528}   | +01c {0,1e5728}   | +024 {0,109538}   | +02c {0,109540}
+034 {0,1093b0}   | +03c {0,122a48}** | +044 {0,120560}** | +04c {0,118a68}   | +054 {0,1e1440}   | +05c {0,1e1420}
+064 {0,1e1e00}   | +06c {0,1e1f00}   | +074 {0,1e1fa8}   | +07c {0,1e1d78}   | +084 {0,1e15d0}   | +08c {0,1e1668}
+094 {0,1e1670}   | +09c {0,1096d8}   | +0a4 {0,1e1d68}   | +0ac {0,1e5ab8}   | +0b4 {0,1e1de0}   | +0bc {0,1e1ed8}
+0c4 {0,1e2830}   | +0cc {0,1e2030}   | +0d4 {0,1e1ce8}   | +0dc {0,109798}   | +0e4 {0,1097a0}   | +0ec {0,1097a8}
+0f4 {0,117280}   | +0fc {0,1e2738}   | +104 {0,1e2768}   | +10c {0,11fd80}** | +114 {0,1e2798}   | +11c {0,123ee8}**
+124 {0,123960}** | +12c {0,118a30}   | +134 {0,109878}   | +13c {0,122c80}** | +144 {0,1e5730}   | +14c {0,1e5778}
+154 {0,11fae0}** | +15c {0,1e0f30}   | +164 {0,11fd28}** | +16c {0,1e1460}   | +174 {0,1e1508}   | +17c {0,116ec0}
+184 {0,1e5840}   | +18c {0,1e5848}   | +194 {0,11fdd0}** | +19c {0,120020}** | +1a4 {0,1e5960}   | +1ac {0,1200e0}**
+1b4 {0,120288}** | +1bc {0,120080}** | +1c4 {0,1e3978}   | +1cc {0,118990}   | +1d4 {0,1227d8}** | +1dc {0,116030}
+1e4 {0,118a78}   | +1ec {0,118a80}   | +1f4 {0,1e4d70}   | +1fc {0,1e4c98}   | +204 {0,1e4ca8}   | +20c {0,1164a8}
+214 {0,1e4cc8}   | +21c {0,1229a0}** | +224 {0,116550}   | +22c {0,1e4cf8}   | +234 {0,116660}   | +23c {0,1e4d38}
+244 {0,1e4d48}   | +24c {0,1e4d58}   | +254 {0,1e4d68}   | +25c {0,1e4f58}   | +264 {0,122ab0}** | +26c {0,1e50f0}
+274 {0,122bb8}** | +27c {0,122b88}** | +284 {0,122b90}** | +28c {0,1e5110}   | +294 {0,1181d8}   | +29c {0,1181e0}
+2a4 {0,122af8}** | +2ac {0,122b80}** | +2b4 {0,1e1708}   | +2bc {0,1e19f0}   | +2c4 {0,1e1cf0}   | +2cc {0,1e1d58}
+2d4 {0,1e4b10}   | +2dc {0,120530}** | +2e4 {0,122c98}** | +2ec {0,122578}** | +2f4 {0,118398}   | +2fc {0,122568}**
+304 {0,1183e8}   | +30c {0,118378}   | +314 {0,122570}** | +31c {0,1183c8}   | +324 {0,117888}   | +32c {0,1178e8}
+334 {0,117948}   | +33c {0,1179a8}   | +344 {0,1204d0}** | +34c {0,117a08}   | +354 {0,117a68}   | +35c {0,117ac8}
+364 {0,117b88}   | +36c {0,1225f8}** | +374 {0,000000}
```

State slots (READ `0x1e5138` update jump table `0x369a70`; entry table `0x369a40` via `0x1e4d70`):
update for state s is `+0x25c + 8(s−1)`, entry for state s is `+0x1fc + 8s`.

| slot | fn | role (READ unless marked) |
|---|---|---|
| `+0x00c` | `0x11f998` | destructor |
| `+0x03c` | `0x122a48` | **per-update**: `0x123500` (node neighbour windows), `0x123598` (pylon models), `0x123618` (pylon animation), `0x123698` (mesh rebuild + texture phase + validity → `+0x144`), `0x1237d0` (style per-frame fn), `0x123840` (node visibility), `0x1238c0` (**step every train**), `0x1228d0` (breakdown check), base `0x1169c0` |
| `+0x044` | `0x120560` | → base `0x116a70` (per-frame; a wrapper) |
| `+0x10c` | `0x11fd80` | remove: delete trains, clear pylons, unregister station nodes, base `0x116458` |
| `+0x11c` | `0x123ee8` | returns pylon count `+0x170` → list box shows "Edit Track" (nonzero) or "Build Track" (0) |
| `+0x124` | `0x123960` | counts editable pylons (walk `+0x794 → +0x174`, `0x121cf8`: not a station node, `+0x53 == 0`) |
| `+0x13c` | `0x122c80` | `closed (+0x148) && valid (+0x144)` |
| `+0x154` | `0x11fae0` | init(ordinal), §3.3 |
| `+0x164` | `0x11fd28` | place/commit: base `0x1186d8`, `+0x144 = 0`, **lay the two station nodes** `0x122060`, reset ghost |
| `+0x194` / `+0x19c` | `0x11fdd0` / `0x120020` | track exit cell / its direction (DBA `+0xbc`, `+0xd3` bits 4–5) |
| `+0x1ac` / `+0x1b4` / `+0x1bc` | `0x1200e0` / `0x120288` / `0x120080` | track entry cell raw / stepped / direction (DBA `+0xc0`, bits 6–7) |
| `+0x1d4` | `0x1227d8` | ride value (Excitement), §7 |
| `+0x21c` | `0x1229a0` | **entry into state 4** (broken): base `0x1164d0` + `0x117798(obj, 0)` |
| `+0x264` | `0x122ab0` | state 2 update = state-10 handler then state-11 handler |
| `+0x274` | `0x122bb8` | state 4 update: wear `+0x364`, then `+0x2a4`, `+0x2ac` |
| `+0x27c` | `0x122b88` | state 5 update: empty |
| `+0x284` | `0x122b90` | state 6 update: `+0x2ac` only |
| `+0x2a4` | `0x122af8` | state 10 update: if closed and no trains, **spawn trains** `0x1230b0`; `0x1e4e88`; if closed and riders `+0x120 ≠ 0`, **wear** `+0x364` (= `0x117b88`); then `+0x2ac` |
| `+0x2ac` | `0x122b80` | state 11 update: empty |
| `+0x2dc` | `0x120530` | selectable = base (state 2/10/11) **and `+0x148`** (closed), as `native-selection-eligibility.md` found |
| `+0x2e4` | `0x122c98` | **ride-along camera** on train `+0x13c` (or `+0x140`): `0x1b1b48` |
| `+0x2ec` | `0x122578` | `100 − min(100, (slot36c(1) × duration × 9) >> 12)` (a reliability/condition readout; INFERRED) |
| `+0x2fc` / `+0x314` | `0x122568` / `0x122570` | get/set `+0xec` (capacity setting). The base setter `0x1183a0` also forwards to the script (`0x1182a8`); **the coaster's does not** |
| `+0x344` | `0x1204d0` | **capacity** = cars/train (`0x2acc80`) × `+0x138` × 6 |
| `+0x36c` | `0x1225f8` | wear rate (speed, load `+0x120`/capacity, tier damage getters) |

### 3.3 Fields (offsets from the object, C = P)

| off | type | meaning | written / read by (READ unless marked) |
|---|---|---|---|
| `+0x10` | ptr | vtable `0x35b060` | pool, `0x123d80` |
| `+0x20`, `+0x28` | ptr | shared resource-cache entry (table `0x2aae08`, 80 × 0x2c) and its `+0x20` | init |
| `+0x60` | s16 | a float read from the station model (`0x17c578`) + 1; meaning not traced | init |
| `+0x78` | u32 | DBA key (`0x12b440(db, ordinal)`: list `+0x68` of the world's DB) | init |
| `+0x84/+0x88` | s16 | station anchor cell x/z | base |
| `+0x97` | u8 | **ordinal** in the park's coaster list | `0x1e1e98` |
| `+0x9a` | u8 | status (2 open, 3 closed, 4/5 broken, 10/11 ...) | base setter `0x1e4d70` |
| `+0xe4` | s32 | reliability; `< 0xa000` in state 2/10 → state 4; 0 in state 4 → 5 | `0x1228d0`, wear `0x117b88` |
| `+0xe8` / `+0xf0` | s32 | speed / duration settings (base) | ride value |
| `+0xec` | s32 | capacity setting | `0x122568/0x122570` |
| `+0xf4` | list | the queue (INFERRED); its head is boarded if its type (vt `+0xdc`) is 0x12 | train state 4 `0x1b1858`, `0x123278` |
| `+0x120` | s16 | riders aboard (INFERRED from use as load numerator in `0x1225f8` and as the wear gate) | base |
| `+0x126` | u8 | tier (base, for `0x117a08` etc.) | base |
| `+0x138` | u8 | **seats per car**: `0x1ae928` loads car model `0x2e7220[w][p][o]` and counts its type-0x80 markers (`0x17d360 → 0x1f2070(res, 0x80)`). "Seats" INFERRED | init |
| `+0x13c` | s32 | ride-camera train index, −1 none | init, `0x122558`, `0x122c98` |
| `+0x140` | s32 | default camera train (0) | init |
| `+0x144` | s32 | **track valid**: every node's `+0x54` set (incl. station nodes) (`0x1229d0`) | `0x123698` each update, place |
| `+0x148` | s32 | **closed** (the eligibility flag) | set `0x120868`; cleared by add `0x121d68`, remove-last `0x122460`, clear `0x1223d0`, reopen `0x1234d8`; saved as bit 14 of record `+0x94` |
| `+0x14c..+0x168` | 8 × f32 | **ride statistics** from the test run: duration, length, max speed, drops, steepest drop, max +g, max −g, max lateral g | §6.4 |
| `+0x170` | s32 | **pylon count** (max 32) | add/remove |
| `+0x174` | node | **station entry node** (end of the ring; loop closes onto it) | `0x122060` |
| `+0x794` | node | **station exit node** (start of the ring) | `0x122060` |
| `+0xdb4 + i×0x620` | node[32] | **pylons**, in placement order | `0x121d68` |
| `+0xd1b4` | pool hdr | train sub-pool | ctor |
| `+0xd1c4 + i×0x2f4` | train[6] | trains | `0x1231e8` |
| `+0xe37c` | s32 | train count (≤ 6) | `0x1231e8`, `0x1224c8` |
| `+0xe384` | s32 | 1 after init / after trains cleared, 0 after spawn (flag; reader not traced) | |
| `+0xe388` | f32 | texture-scroll phase, −0.08 per update wrapped to [0,1) | `0x123698`; read by style fn2 (`0x19e468`) |

Save `0x120580` / load `0x1206d8` (READ): record `+0x94` bits 8–13 pylon count, bit 14 closed; per
pylon 14 bytes from `+0x96`: position (8 bytes, vt `+0x74`), `+0x54` valid, height (`0x19a1d0`), bank
`+0x4e`, kind bytes `+0x52`, `+0x53`. Load re-adds each pylon with `0x121d68` and closes if bit 14/15.

---------------------------------------------------------------------------------------------------

## 4. The track

### 4.1 Representation: a ring of nodes (READ)

- **Nodes** are 0x620-byte objects, class ctor `0x1997d0`, vtable **`0x365510`** (41 entries, a
  map-object base `0x109xxx` with overrides) plus a second interface vtable **`0x3654f8`** at `+0x14`
  (one entry, delta −20, `0x19c650` = the style's mesh-create fn). Each owns a model instance (`+0x5c`,
  the pylon) and a mesh (`+0x64`, the track section named "Track", string `0x3654f0`).
- **Ring**: `+0x794` (station exit) → pylon 0 → … → pylon n−1 → `+0x174` (station entry) → back to
  `+0x794`. Links `+0x2c` prev, `+0x30` next (`0x122060` wires entry→exit; `0x120868` closes last→entry).
  So the ring has **n + 2 segments**, and the segment "at" a node is the curve arriving at it.
- **Stacking**: `+0x34` node above, `+0x38` node below in the same cell (`0x199c50`/`0x199dd0`);
  a new pylon placed on a cell holding one of this coaster's pylons is stacked on top of it
  (`0x121d68`). Total stack height ≤ 0x600 (`0x19a6e8`).
- **Map registration**: `0x14da60`/`0x14dac0` add/remove the node as a map object (found again by
  `0x14c688(cell, 9)`, so kind 9 = coaster node, INFERRED); `0x19a828` marks the pylon footprint tiles
  with `0x1e6138(tile, 10)`, `0x19a930` clears them.

### 4.2 Node fields (READ)

| off | type | meaning | writer |
|---|---|---|---|
| `+0x18,+0x1c,+0x20,+0x24` | ptr×4 | spline window: prev-prev, prev, self, next | `0x19a760` (vt `+0x3c`) |
| `+0x28` | ptr | owning coaster | `0x19c9f0` |
| `+0x2c/+0x30/+0x34/+0x38` | ptr | prev / next / stacked-above / stacked-below | `0x19c8d8`, `0x19c900`, `0x199c50`, `0x199dd0` |
| `+0x3c,+0x3e,+0x40` | s16 | position x, y, z in 1/256 cell (x, z at cell centre +0x80) | `0x199fb8`, `0x19a058`, `0x19c950` |
| `+0x44` | s32 | **pylon height** (0..0x500 from the tool); `0x19a1d8` sums the stack | `0x19c788` |
| `+0x48` | s16 | pitch to prev (−atan(dy/dh)) | `0x19aa48` |
| `+0x4a` | s16 | heading, 4096/turn (+0x400 on loop nodes) | `0x19aa48` |
| `+0x4e` | s16 | **bank**, ±0x200 from the tool (forced 0 for SPACE park 1 ord 0, Moonshot) | `0x19c818` |
| `+0x50` | s16 | 3D distance to prev (1/256 cell) | `0x19aa48` |
| `+0x52` | u8 | 1 = part of a loop (skips the pylon rules), else 0 | `0x19c928` |
| `+0x53` | u8 | **segment kind**: 0 normal, 1 loop lead-in, 2 loop | `0x19c9c0` |
| `+0x54` | s32 | valid (drawn red and blocks "finish" when 0, INFERRED for the red) | `0x19aec0` |
| `+0x58`, `+0x78` | s32 | dirty flags (spline / mesh) | many |
| `+0x5c`, `+0x64` | ptr | pylon model / track mesh | ctor, style fn0 |
| `+0x7c..+0xab` | 4 × f32[3] | spline control points (positions /256, i.e. in cells) | `0x19b208` |
| `+0xcc` | s32 | loaded pylon model id (−1 none) | `0x19aef8` |
| `+0xd0` | f32 | **segment arc length** (sum over samples) | `0x19b208` |
| `+0xd4 + i×0x48`, i = 0..16 | record | 17 samples per segment (pos at `+0x04`); `+0x48` of each record (`+0x11c` for i = 0) = "winch" flag | `0x19b208`, `0x19b1a0` |
| `+0x5b8..` | f32[3]×4 | per-control-point frame vectors (heading/bank) | `0x19b208` |
| `+0x5ec..+0x604` | | pylon animation state | `0x19cdd0`, `0x19aef8` |
| `+0x608/+0x60c` | s32 | visibility flags | `0x19d1e0` |
| `+0x610..+0x61c` | handles | tool ratchet sounds (bank 2, event 0x45) | `0x199ae8`, `0x1999e8` |

### 4.3 The curve (READ `0x19bda0`, `0x19b208`)

- **Normal segment** (`+0x53 == 0`): uniform **Catmull-Rom** on the four control points P0..P3
  (`+0x7c,+0x88,+0x94,+0xa0`) with the standard basis `((−t³+2t²−t)/2, (3t³−5t²+2)/2,
  (−3t³+4t²+t)/2, (t³−t²)/2)`; the same basis is applied to the frame vectors at `+0x5b8` to get the
  up/side vector; the tangent comes from the derivative basis.
- **Loop** (`+0x53 == 2`): position = lerp(P1, P2, t) + a sideways offset `3·sin(2πt)` perpendicular
  to (P1−P2) in xz, and height `+ 3 − 3·cos(2πt)`: a vertical circle of **radius 3** (cells) that
  drifts one track-width sideways (constants `lui 0x4040` = 3.0 at `0x19be3c`, `0x19bf2c`).
- **Lead-in** (`+0x53 == 1`): the spline is blended by a cosine ease into a straight line along
  (P2 − P3); likewise the segment after a loop blends out of it.
- `0x19b208` rebuilds the control points from the window nodes (`0x19a420`), unwraps headings by
  ±2048, builds the bank frames with sin/cos, and fills the 17 samples and the arc length `+0xd0`.
- There is **no whole-track rebuild** like the track rides' `0x2009c0`: an edit calls `0x19ad28` on
  the node (re-derive `0x19aa48` for prev/self/next and the stack), which marks meshes dirty; each
  update `0x123698` rebuilds dirty meshes (`vt +0x144` = `0x19cc48` → `0x19b208` + style fn1).

### 4.4 Heights, terrain and the station connection (READ unless marked)

- **Real elevation.** A pylon's base y comes from the terrain (`0x19cae0` → `0x149d90(x, z)`), or the
  top marker of the pylon below it; the track point is base + the stack's height sum (`0x19a368`).
  The tool clamps a height to 0..0x500 in steps of 0x14, the bank to ±0x200 in steps of 0x14
  (MIPS `0x11caf4..0x11cb8c`).
- **Station nodes** have base y = −0x100 (`0x19cae0`) and height = per-coaster floats `0x2acb60`
  (exit) / `0x2acb64` (entry) (table in §1; READ in `0x122060`). Moonshot's exit (1125) is much higher
  than its entry (335).
- **Station connection**: the exit node sits one cell outside DBA `+0xbc`, the entry node one cell
  outside `+0xc0` (§1.1). Adding a pylon **on the entry cell** (`vt +0x1b4`, compare `0x1207f0`)
  when at least one pylon exists **closes the ring** (`0x120868`) instead of placing a pylon.
- Units of y are **unresolved**, as for track rides (tile height byte × 4, object y `<<8`, spline
  floats /256). The how pass needs one consistent reading.

### 4.5 Track meshes: the per-coaster style table (READ)

`0x2e2b30 + ((world·3 + park)·3 + ord) × 0x2c`: {u32 pylon model id, char* GTexture dir,
char* tex[3], 3 × member-function pointer `{s16 delta 0, s16 −1, fn}`}. Readers `0x19aef8` (pylon id),
`0x19c650` (fn0), `0x19cc48` (fn1), `0x19d4d0` (fn2), plus `0x19f82c`, `0x1a02b4`, `0x1a0b9c`, `0x1a1704`.

| style | fn0 create mesh | fn1 build section | fn2 per update | coasters |
|---|---|---|---|---|
| A | `0x19d870` | `0x19dd20` | `0x19e468` (scrolls UVs by `+0xe388`) | Dare Devil, Ghosta Coasta, Bone Shaker, Caterpillar, The Shocker |
| B | `0x19e5a0` | `0x19eba8` | `0x19f580` | Chak Atak, Hades, Big Dripper (the three troughs) |
| C | `0x19f728` | `0x19fb98` | `0x1a01a8` (empty) | Gorilla Thrilla |
| D | `0x1a01b0` | `0x1a05a0` | `0x1a0a90` (empty) | Scatty Batty |
| E | `0x1a0a98` | `0x1a0f08` | `0x1a1368` | Moonshot |
| F | `0x1a1600` | `0x1a1968` | `0x1a1d20` (empty) | Escape Velocity |
| G | `0x1a1d28` | `0x1a23b0` | `0x1a2f68` | Temple of Gloom, Candy Coaster |

- fn0 allocates the "Track" mesh and its strips (`0x16a150`, `0x168bd0`, `0x1696a0`); fn1 writes
  vertices from the 17 samples with hard-coded cross-section offsets (floats `0x2e2b00..0x2e2b2c`:
  ±0.3, ±0.075, 0.425, 0.575, ...) via `0x169458/0x169608`. So **the cross sections are native code,
  one per style**, not `coaster.sam`. (READ calls; "cross-section" INFERRED from the use.)
- Every GTexture folder holds `red.*`, and `"red.ssh"` sits beside `"TrackCentreDummy"`/`"Track"` at
  `0x3654c0..0x3654f0`: INFERRED to be the invalid-segment texture.
- `.sam` claims `bIsSuspendedCoaster` for Gorilla Thrilla and The Shocker; natively only Gorilla
  Thrilla has its own style (C). The Shocker uses the standard style A.

### 4.6 Pylons (READ)

- Model per coaster = style-table id (428..441), loaded in `0x19aef8`, which starts 4 animation
  channels (3, 10, 2, 9); `0x19cdd0` sets them each update from heading, bank and height/2560 (clamped
  0..1). This matches the `.sam` `Loft/Rotate/Incline/BankAnim` idea: **the pylon mesh is morphed**
  to meet the track.

### 4.7 Validity (READ `0x1216d8`, run per node by `0x121c80` and live by the tool)

A pylon is valid when, among other tests: its horizontal distance to the previous node
(`0x19adf0`) is in **[0x300, 0x800]** (3..8 cells); the heading change from the previous node is
under 0x400 (0x200 next to the station exit); no other coaster node is on it or in its 8 neighbour
cells (table `0x2acec8`); the stack limits hold (`0x19a6e8`; a stack-depth count `< 2`); the
segment clears scenery, terrain and other track (`0x121000`: spline sampled every 0.1, map objects
via `0x14c688`, other segments within ±10 cells via `0x1209b0`/`0x1208e0`); and a closing pylon meets
the station heading. Loop nodes (`+0x52 == 1`) skip the distance, heading, occupancy and clearance
tests but not the 8-neighbour test. The exact rule set is for the how pass.

---------------------------------------------------------------------------------------------------

## 5. The coaster building tool

Mode table `0x2b2a10`; tool objects are in `.bss`, vtables stored by the static ctor at
`0x125f60..0x12611c` (READ). The three coaster modes:

| mode | object | vtable | role | reached from |
|---|---|---|---|---|
| 11 | `0x3890a0` | `0x35ad58` | place the **station** (shared placement fns `0x126558/0x1268f0/0x126898/0x126778/0x126808`) | build menu, kind 1 |
| 12 | `0x3890c0` | `0x35acd0` | **build/extend the track** (lay pylons) | after the station; list box "Build Track"/"Edit Track" (`0x1240e8`, rows 0 / 0xc) |
| 13 | `0x389318` | `0x35ac48` | **edit pylons** | list box "Edit Pylons" (`STR_LISTBOX_EDIT_PYLONS`, `0x124118`, tutorial event 0x3a) |

Slot map (READ):

| slot | 11 station | 12 build track | 13 edit pylons |
|---|---|---|---|
| `+0x0c` enter | `0x11a7c0` (take a coaster from the pool, `0x14a4a0`) | `0x11ad60` | `0x11ceb0` |
| `+0x14` cursor | `0x126558` | `0x11aef0` | same |
| `+0x1c` draw | `0x1268f0` | `0x11b2f8` | same |
| `+0x2c` Cross | `0x11a858` place | `0x11b550` **Place** pylon | same |
| `+0x34` Triangle | `0x1271f0` | `0x11ba00` **Exit** / finish | same |
| `+0x3c` Circle | `0x126778` rotate | `0x11b898` **Undo** | `0x11cf88` restore backup (cancel) |
| `+0x44` Square | `0x126808` rotate | `0x11b828` **Loop** (if allowed) / Prev | same |
| `+0x64/+0x74` | | `0x11aa68` / `0x11aa60` set/get ride | same |
| `+0x7c` | | `0x11d1f8` | same |

Button bar (`0x13e340(ui, a, b, c, d)` with text rows; labels READ from the text DB):
build = Exit (0x1ff) / Undo (0x395) / Place (0x1b) / Loop (0x414) or blank; pylon sub-mode = Exit /
Next (0x248) / Move (0x118) / Prev (0x184); stats screen = Back (0x221) / OK (0x243). Cursor readouts
`STR_CURSORCOASTER_HEIGHT` "Height", `STR_CURSORCOASTER_BANKING` "Banking"; counter "Pylon Stock"
(`STR_PYLONS_LEFT`) = 32 − pylons (`0x11b2f8`).

What exists (READ; details for the how pass):
- **Station (11)**: Cross needs the placeable bit (`0x1e1e08`), commits (`vt +0x164` → station nodes),
  debits, sound 0x8e, → mode 12; advisor **201** `STR_ADVMES_COASTER_STOCK_OUT` when that used the
  last slot (`0x14ccb0() == 0`).
- **Build (12)**: a ghost node `0x2ac438` follows the cursor; valid cells in a ±8 window are scanned
  4 rows per frame (`0x11aa70`) and highlighted (`0x11acc8`). Cross adds a pylon (`0x121d68`) at the
  cursor with the current height/bank/kind, or closes the ring on the entry cell (tutorial event 0x46).
  Circle removes the last pylon (`0x122460`, and the paired lead-in of a loop, `0x11b7b0`) and refunds.
  Square places a **loop** (`0x11c668`): a node (`+0x52=1, +0x53=1`) then, one cell sideways, a node
  (`1, 2`), each charged; Square right after a loop adds a further loop node at 2 × cursor − (the node
  before the loop). Closing the ring leaves the tool (or selects the first pylon). Triangle ends: not closed →
  advisor **204** `STR_ADVMES_COASTER_TRACK_INCOMPLETE`; invalid (`+0x144 == 0`) → **203**
  `STR_ADVMES_COASTER_COLLISION`; then the **test run** and the **stats screen** (§6.4); OK leaves to
  mode 3 (queue) when the build started from mode 11, else mode 0.
- **Pylon sub-mode** (globals `0x2ac428` = sub-mode, `0x2ac42c` = moving, `0x2ac430` = stats screen,
  `0x2ac434` = came from station): Next/Prev walk the ring (`0x11c468`/`0x11c540`), Move picks up the
  pylon, D-pad Up/Down = height ±0x14, Left/Right = bank ∓/±0x14 (`0x11cac8`).
- **Edit (13)** backs up every pylon (0x18 bytes each, `0x11cbf0`) and Circle restores them
  (`0x11cd38`).
- **Cost**: per pylon DBA `+0xd0` (100) × 10 (debit `0x126408`/`0x100698`, refund `0x100750`).
  `.sam` `fCostPerUnit/2DLength/Height` are not used (INFERRED, §2.5).
- Stock: 32 pylons (`0x121d68`: `slti count, 0x20` at `0x121dac`, "TRACK PYLON OVERFLOW").

---------------------------------------------------------------------------------------------------

## 6. Cars and trains

### 6.1 Objects (READ)

- **Train**: 0x2f4-byte plain struct, **no vtable**. Ctor `0x1af050`, dtor `0x1af118`, init
  `0x1af198(train, coaster, index)`, remove `0x1af250`. Holds a sub-pool of **4 cars** (0x8c each) at
  `+0x1c`; `cars/train` from `0x2acc80`.

  | off | meaning |
  |---|---|
  | `+0x08` | position, f32 in segment units [0, n+2): integer = segments after `+0x794`, fraction along it |
  | `+0x1c + i×0x8c` | cars |
  | `+0x24c` / `+0x250` | coaster / train index |
  | `+0x254` | 1 from spawn until speed first exceeds 0.04 |
  | `+0x258` | **train state** 0..4 |
  | `+0x260/+0x264/+0x268`, `+0x26c..+0x2a0`, `+0x2c0..+0x2e0` | sound handles, params, timers |
  | `+0x2a4` | state timer (set 0x27100 on entering states 1 and 3, `0x1b1b08`) |
  | `+0x2a8` | car cursor for unload/board; `+0x2ac` boarding attempts (40) |
  | `+0x2b0` | previous speed; `+0x2b4` current segment node; `+0x2bc` blocked flag |

- **Car**: 0x8c-byte struct, **no vtable**. Ctor `0x1ae898` (model instance `+0x40` via `0x230a98`),
  dtor `0x1ae8d0`, init `0x1ae9e0` (load car model `0x2e7220[w][p][o]`), pose `0x1aeb00`, step
  `0x1aed90`, stats `0x1aeec0`, board `0x1aec68`, unload one `0x1aed00`.

  | off | meaning |
  |---|---|
  | `+0x08..+0x10` | position (cells) |
  | `+0x14`, `+0x20`, `+0x2c` | forward / up / side vectors (normalised `0x1ae7a8`, cross `0x1ae830`) |
  | `+0x44` | coaster; `+0x48..` rider list; `+0x70` rider count (≤ `coaster+0x138`) |
  | `+0x74` | t along the segment; `+0x78` **speed**; `+0x7c` segment node; `+0x80..+0x88` next position |

### 6.2 The step (READ, constants checked in MIPS)

Per coaster update `0x1238c0` removes all trains unless the ring is closed and valid, then (unless
state 5) runs `0x1b0480` per train: `0x1af858` (per-frame) and the state function.

| state | fn | does | exit |
|---|---|---|---|
| 0 run | `0x1b0518` | if the gap to the train ahead (`0x1232f8`) < **0.75** segment: cars' speed 0, hold (`+0x2bc = 1`). Else speed = mean of `0x1aed90(car)` × (1 − f), **f = 0.04 on the station segments** (node `+0x174`/`+0x794`), **0.001 elsewhere**; **floor 0.04**; on a loop node (`+0x53 == 2`) with speed ≥ 0.08 keep the previous speed. Position += speed / segment length (`0x1b19e0`). | position ≥ n + 2 + 0.75 → wrap, state 1 |
| 1 stop | `0x1b16b0` | timer −= frame delta (`0x397640`) | ≤ 0 → 2 |
| 2 unload | `0x1b16f0` | one rider per call off the current car (`0x1aed00` → `0x117e08`) | all empty → 3, 40 attempts |
| 3 wait | `0x1b1778` | timer; if train index+1 (`0x1b25c0`, INFERRED to be the one behind) is blocked (`+0x2bc`) → go | timer out: attempts−1 → 4, or < 0 → 0 |
| 4 board | `0x1b1858` | if not the camera train and seats remain: take the queue head from `coaster+0xf4`, set coaster state 10 (unless 4), `0x1aec68` (→ `0x117c90`) | → 3 (or 0) |

- **Gravity (car step `0x1aed90`)**: advance distance `t·len + speed` along the curve (walking `+0x30`),
  re-evaluate the spline, then `speed += (y_before − y_after) × 0.04` (`lui 0x3d23 / ori 0xd70a` at
  `0x1aee7c`). Speed is in cells per step. So PS2 physics is gravity with one constant, friction and a
  speed floor. The floor of 0.04 acting as the lift chain, and `+0x254` marking the lift section, are
  INFERRED (`0x1239d8` flags the samples a train covers while `+0x254` is set, then forces a mesh
  rebuild: plausibly the `chain.ssh` "winch" section).
- **Trains**: `0x1230b0` spawns clamp((pylons/3 + 2) / cars-per-train, 2, 6) trains (`0x1231e8`, max 6),
  at positions n+2, n+1, ... wrapped, i.e. one segment apart back from the ring's end (spacing INFERRED
  from the loop, not simulated). **Cars** are laid back along the curve by the car
  spacing (`0x1b19e0`, `0x123430`).
- **Capacity** = cars/train × seats/car × 6 (`0x1204d0`).

### 6.3 Boarding (READ)

Guests go from the queue straight into cars (train state 4), and off one at a time (state 2) — the
same result the stubbed script wanted, done natively. Busy/boarding sets status 10; nothing in the
coaster sets it back to 2 (the base open/close toggles do); what that means for the status machine is
for the how pass.

### 6.4 The test run, statistics and rating (READ)

- **Test run** `0x122d48` (on finishing the track, `0x11bbd8`): zero `+0x14c..+0x168`, one train at 1.0,
  step it until its position stops increasing, accumulating per step `0x1aeec0`: duration += 1/30,
  max speed = speed × 175, max vertical +g / −g, max lateral g; then per segment `0x19d5f0`: length,
  steepest drop (degrees), number of drops; then respawn the trains.
- **Stats screen** `0x11bd28`: `STR_COASTERSTATS_*` Duration (secs), Length (meters), Maximum Speed
  (kph), Number of Drops, Steepest Drop (deg), Max Vert +Gs, Max Vert −Gs, Max Lat Gs, "Coaster Rating:".
- **Rating** `0x122ed0`: **"Ultimate Rollercoaster"** (text 0x256) if lateral < 0.5 and 55 ≤ speed ≤ 70
  and 1 < drops < 4, which also calls `0x1542b0(world, park, ord)` (the award). Otherwise table
  `0x2acda0` [lateral < 0.5 / < 1 / else][speed ≤ 50 / ≤ 75 / else][drops ≤ 1 / ≤ 6 / else] → 27
  `STR_COASTER_RATING_{BORING,CALM,VIOLENT}_{SLOW,MEDIUM,FAST}_{TAME,NORMAL,LOTS}` strings
  ("Too Slow" ... "Too Scary").

### 6.5 Sounds and camera (READ, names unmapped)

- Train loop sounds `0x1af330` (bank 9/10 events 2/0x17, or bank 4/5 event 0x11 when audible,
  family from `+0xd3&3`); scream sets bank 7 chosen by car-0 rider count (<2, <4, <8, else) and
  triggered by pitch and speed in `0x1b0518`; drop "whoosh" by height bands 4500/6283/8066/9850.
  Audibility `0x1b0410` depends on the camera mode `0x395288` (3 = riding).
- **Ride-along camera** `0x1b1b48`: pad turns the view (yaw ±90°, pitch ±60°) with per-coaster eye
  offsets `0x2e72b0[w][p][o]` and camera shake.

---------------------------------------------------------------------------------------------------

## 7. Upgrades, add-ons and value

- **No track add-ons.** All 10 DBA kind-8 "track upgrade" records belong to track rides
  (`track-ride-geometry.md` §5); nothing in the coaster code reads kind 8. (READ absence in the coaster
  class; INFERRED for the rest of the binary.)
- **Tier upgrades exist** (3 DBA tiers: cost, research, wear rate 5/3/2, damage getters).
  `STR_ADVMES_COASTER_UPGRADED` (advisor 205) exists, but no `0x107ca8(…, 0xcd)` call was found by a
  scan for `li a1, imm` before `jal 0x107ca8`. What an upgrade changes for a coaster beyond the generic
  tier fields is **not established**: capacity ignores the tier (`0x1204d0`), and cars/trains come from
  tier-less tables.
- **Ride value** `0x1227d8` (READ; formula as in `ride-value-producer.md`):
  `min(100, B × Q(speed·4096/100) × Q(duration·4096) >> 24)` with `Q = clamp(0xc00..0x1400)`, B = DBA
  `+0x18`. Duration is always 1 (MaxDuration 1), so the factor is 1.0; at the default speed 50 the
  speed factor is 0.75: **67** for B = 90, **71** for B = 95 (computed). No track term.
- Advisor messages (records at `0x2a6ac8`, 56 bytes): 110/111 `CONGRAT_BIG/FUN_COASTER`, 166
  `GOLD_TICKET_ROLLER_COASTER`, 201 `COASTER_STOCK_OUT`, 203 `COASTER_COLLISION`, 204
  `COASTER_TRACK_INCOMPLETE`, 205 `COASTER_UPGRADED`, 269 `WELCOME_COASTER_PARK`, 270/271
  `TUT_PYLON_PLACEMENT/EDIT`.

---------------------------------------------------------------------------------------------------

## 8. Parallels with track rides, and where they differ

| aspect | track rides | coasters |
|---|---|---|
| class | `CTrkRide`, vtable `0x36bbf0`, 2 × 0x28d8, adjust −8 | `CCoaster`, vtable `0x35b060`, 14 × 0xe390 (2 per park, 14 in the Test Park), adjust 0 |
| track | ≤ 34 waypoints on a 2-cell lattice → ≤ 36 fixed 2×2 pieces | ≤ 32 pylons anywhere 3–8 cells apart → Catmull-Rom segments |
| piece types | 48-entry table `0x2ee1e0` | none; node kinds (normal / loop lead-in / loop) + 7 native mesh styles `0x2e2b30` |
| meshes | 10 piece meshes per ride folder | none on disc; generated from samples; only the pylon mesh ships |
| rebuild | whole track re-laid on every edit (`0x2009c0`) | local: the edited node and its neighbours (`0x19ad28`), meshes lazily |
| height | flat (80/144/170), bridges 256, add-ons | real: pylon heights, stacking, terrain base, loops |
| station link | fixed station pieces from `0x2ee54a` | two fixed nodes from DBA `+0xbc/+0xc0/+0xd3` and heights `0x2acb60` |
| closure | last waypoint == return point → `+0x1c6` | pylon on the entry cell → `+0x148` (the eligibility flag) |
| pose | baked 4 samples/piece, double lerp | 17 samples/segment for the mesh; cars evaluate the spline directly |
| cars | one car per guest, fixed speeds, race logic | trains of 1–4 cars, gravity + friction + floor, blocking |
| boarding | 1 guest / 20 ticks, batch launch | per train at the station, straight from the queue |
| script | `BUMP` stub | `COAST` stub |
| value | base + Σ piece weights / 2 | base only (duration fixed at 1) |
| add-ons | ≤ 3 kind-8 upgrades | none |
| tool | modes 7–10 | modes 11–13 |

---------------------------------------------------------------------------------------------------

## What is not read yet (for the "how" pass)

Four independent areas. Each lists the functions and data to decompile in depth; addresses already
named above are the entry points.

### A. Track geometry and meshes (node class, curve, styles)
- Node class `0x1997d0..0x19d860`: `0x19aa48` (pitch/heading/distance), `0x19b208` (control points,
  bank frames, 17 samples, arc length), `0x19bda0` (Catmull-Rom, loop circle incl. the 5.0 constant at
  `0x19bf34`, lead-in/out blends), `0x19a420`, `0x19a368`/`0x19a1d8` (stack heights), `0x19cae0`
  (terrain/stack base), `0x19ca78`/`0x19ad28` (dirtying), `0x19cdd0` (pylon morph channels), `0x19aef8`,
  `0x19d1e0`, `0x19c650`/`0x19cc48`/`0x19d4d0` (style dispatch).
- The 7 mesh styles (fn0/fn1/fn2 in §4.5), cross-section constants `0x2e2b00..0x2e2b2c`, the
  per-sample record layout (`+0xd4 + i×0x48`), the red/invalid and chain textures.
- The lift section: `0x1239d8`, `0x123b28`, `0x19b1a0`, and how the flagged samples change the mesh.
- The units of y (terrain, node, float) — shared with the track-ride gap.

### B. Building: tool, rules, station link, save
- Tool modes 11–13: `0x11a7c0`, `0x11a858`, `0x11ad60`, `0x11aef0`, `0x11aa70`/`0x11acc8`, `0x11b2f8`,
  `0x11b550`, `0x11b7b0`, `0x11b828`, `0x11b898`, `0x11ba00`, `0x11bbd8`/`0x11bca8`, `0x11c118`..`0x11c668`
  (loop placement), `0x11c9f8`, `0x11cac8`, `0x11cbf0`/`0x11cd38`, `0x11ceb0`, `0x11cf88`, `0x11d1f8`;
  globals `0x2ac428..0x2ac438`, `0x37e23c/0x37e240`, `0x2aca58/0x2aca60`.
- Pylon add/remove/close/reopen: `0x121d68`, `0x122460`, `0x120868`, `0x1234d8`, `0x122320`, `0x1223d0`,
  stacking `0x199c50/0x199dd0/0x199c90`.
- Validity: `0x1216d8`, `0x121c80`, `0x121000`, `0x1209b0`, `0x1208e0`, `0x120f28`, `0x122c18`,
  `0x121cf8`, `0x19a6e8`, `0x19adf0`; which rule raises 203.
- Station link: `0x11fdd0`, `0x1200e0`, `0x120288`, `0x120020/0x120080`, `0x122060`, tables
  `0x2acb60/64`; the `.sam` `Bumper.*Adjust` consumer (if any); DBA `+0xc4..+0xcf`, `+0xd2`.
- Costs and refunds (`0x126408`, `0x100698`, `0x100750`), save/load `0x120580`/`0x1206d8`.

### C. Trains, cars and physics
- Train/car: `0x1af050..0x1b25c0`: `0x1af198`, `0x1b19e0`, `0x1aeb00`, `0x1aed90`, `0x1b0518` (the whole
  speed rule: floor, friction, loop hold, lap wrap, `+0x254`), `0x1b16b0`, `0x1b16f0`, `0x1b1778`,
  `0x1b1858`, `0x1b1b08` (timers 0x27100), `0x1af858`, `0x1b25c0`, `0x1232f8` (gap), `0x1230b0`/`0x1231e8`
  (spawn spacing), `0x1224c8`, `0x123430`.
- Tick rate: frame delta `0x397640` (`0x1c4920`) and whether 1/30 s per step (stats) matches it.
- Boarding/unloading hooks `0x1aec68 → 0x117c90`, `0x1aed00 → 0x117e08`, seat markers
  `0x17d360 → 0x1f2070(res, 0x80)`; the queue list `+0xf4` and the guest type check.
- Sounds `0x1af330`, `0x1af800`, scream/drop logic in `0x1b0518`; ride camera `0x1b1b48`, `0x122c98`,
  `0x2e72b0`.

### D. Operation, statistics, value and the Test Park
- Status overrides: `0x122a48`, `0x122ab0`, `0x122af8`, `0x122bb8`, `0x122b90`, `0x1229a0 → 0x117798`,
  `0x1228d0` (breakdown), `0x123698`/`0x1229d0` (validity → `+0x144`), `0x1e4e88`; who returns the
  coaster to status 2.
- Wear and capacity: `0x1225f8`, `0x117b88`, `0x122578`, `0x1204d0`, `0x122568/0x122570` (capacity not
  forwarded to the script), `0x1ae928`.
- Value and eligibility: `0x1227d8`, `0x120530`; confirm no other reader of `+0x14c..+0x168` (an
  `lwc1/swc1` imm scan found other hits at `0x1b8438`, `0x1bcfa8`, `0x1f6d3c`, ... that look like other
  objects, not verified).
- Stats and rating: `0x122d48`, `0x1aeec0`, `0x19d5f0` (scale factors: ×175 kph, g units), `0x122ed0`,
  table `0x2acda0`, award `0x1542b0`, stats screen `0x11bd28`.
- Test Park: park index 2, `0x11f930`/`0x2ace10`, `0x14ccb0`, the unlock path from the Ultimate rating,
  its terrain and attraction lists.
- Upgrades: what a coaster tier changes; `STR_ADVMES_COASTER_UPGRADED` raiser.
- The `coaster.sam` schema `0x2acf1c..0x2b28a4`: a definitive consumer search (pointer tables built at
  run time, the generic SAM parser `0x1f98c8`/vtable `0x36ad90` from the track-ride notes).

## Still unknown (tried)

- Station and loop heights in world units: not settled (see §4.4).
- DBA `+0xc4..+0xcf`, `+0xd2`: no reader found in the coaster class, tool, node or train code read here.
- Bone Shaker's pylon registry entry naming `coasta`: not checked against the style table's pylon id use.
- Event names behind the native sound ids: not mapped.
