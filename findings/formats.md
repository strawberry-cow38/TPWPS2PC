# What is inside a world archive

Measured on `DATA/JUNGLE.WAD` (2,545 files, 170 directories, all read). Counts are that archive's.

| ext | n | bytes out | what it is |
|---|---:|---:|---|
| `.tga` | 984 | 12,811,784 | source textures, ordinary Targa (type 2, 24 bpp, BGR, bottom-left origin) |
| `.gin` | 14 | 3,740,116 | unknown, and the largest per-file — 267 KB average |
| `.ssh` | 998 | 3,450,288 | **EA SHPS** image container, magic `SHPS`, first entry tagged `GIMX` (GIMEX). One per `.tga`, so: the converted-for-PS2 form |
| `.mps` | 113 | 2,986,292 | **model**, magic `0x183076E4`, 0x140-byte header |
| `.aps` | 89 | 1,414,064 | same magic family, 0x148-byte header — animation, probably |
| `.rss` | 90 | 155,681 | **ride behaviour script, PLAIN TEXT SOURCE** |
| `.sam` | 77 | 81,876 | **ride description, PLAIN TEXT** |
| `.md2` | 4 | 63,912 | Quake-2-style model |
| `.mtr` | 4 | 60,932 | unknown |
| `.rse` | 91 | 52,265 | magic `RSSEQ` — the assembled form of the `.rss` beside it |
| `.scc` | 78 | 7,024 | Visual SourceSafe status files, shipped by accident |

## ⭐⭐ `.rss` — the rides are SCRIPTED, and the scripts shipped as source

Every ride's behaviour is a program in an in-house assembly-like language, with the authors' own
comments and labels intact. 90 of them. They are not compiled-and-stripped: the `.rse` beside each
one is the assembled form, and **both** are on the disc.

**71 opcodes** appear across the corpus:

```
ADD ADDHEAD ADDOBJ BOUNCE BOUNCESETBASE BOUNCING BRANCH BRANCH_NV BRANCH_NZ BRANCH_PV BRANCH_Z
BUMP CMP COAST COPY CRIT_LOCK CRIT_UNLOCK DELHEAD DIPMUSIC DIV ENDSLICE EVENT FADEOBJ
FINDSCRIPTRAND FLUSHANIM FORCEUNBOUNCE FORCEUNLIMBO GETANIM_CH GETTIME GETTIMER GETVARINPARENT
HOP HUSH INLIMBO JSR KILLOBJ LIMBO LIMBOSPACE LOOPANIM MOD NAME RAND REMOVECHILD REPAIREFFECT
RETURN SCREAMLEVEL SETOBJPARAM SETREMOTEVAR SETREVERB SETTIMER SINGLESCREAM SPAWNCHILD SPAWNSOUND
STARTSCREAM STOPSCREAM SUB TEST TOUR TRIGANIM TRIGANIMSPEED TRIGANIM_CH TRIGWAITANIM TURBO
UNBOUNCE UNLIMBO WAIT WAIT4ANIM WAITANIM WALKGET WALKOFF WALKON
```

It is a real VM: a declared stack size (`#setstack`), named variables, labels, conditional branches
on zero / non-zero / overflow, subroutines (`JSR`/`RETURN`), critical sections (`CRIT_LOCK` /
`CRIT_UNLOCK`) and cooperative yields (`ENDSLICE`).

⭐ **The first twelve variables are a fixed ABI**, identical across scripts and clearly the host's
view of a ride: `LETMEON`, `LETMEOFF`, `CAPACITY`, `DURATION`, `BREAKSTAT`, `ONRIDE`, `RIDECLOSED`,
`BROKEN`, `WORN`, `RUNNING`, `PAD` ("unused at present"), `PARAM`. Those are the same quantities the
PSX port's ride lifecycle was reverse-engineered into from the binary — here they are named by the
people who wrote them.

The scripts `#include` headers that name the source tree:

```
\source\game\rsse\code\rsse_scriptdefs.h      the script language's own definitions
\source\game\soundint\{Events,Params,ThemedEvents}\...
\source\game\particle\code\par_lib.h
\..\tpwport\source\rsse\code\rsse_scriptdefs.h
\..\tpwport\source\audiosys\{GlobalEvent,GlobalRideEvent,JungleEvent}.h
```

⭐ **`tpwport`** — their own name for the port, sitting beside the original `game` tree. Some scripts
include from one and some from the other, so the disc carries both generations.

## ⭐ `.sam` — the ride balance table, in plain text

Header comment calls it a *"Theme Park 2 Ride Description File"*. Key/value with an ASCII-art
footprint. Fields observed: `Info.Id`, `Info.Name`, `Info.Shape`, `Info.Hoarding`,
`Info.RideTypeStringIndex`, `Info.DurationUnit`, `Info.AttractionValue`, `UsageInfo.RequiresTeleport`,
`UsageInfo.Min/MaxCapacity`, `UsageInfo.ExcitementLevel`, `Upgrades[n].{InitCapacity,
RedLineCapacity, QueueWaitTimeConstant, CostOfUpgrade, CostOfResearch}`, `Research.Group`,
`Attraction[n].NewBonus`, `Bumper.{WhichTrackType,BumperType}`.

`Info.Shape` and `Info.Hoarding` are the footprint and its fence drawn as characters, with the
entrance marked — the same footprint data the PSX port reads out of a binary table.

## `.mps` — the model (in progress)

```
0x00  u32   magic 0x183076E4          (identical in every file)
0x04  u32   0x140                      header size; .aps uses 0x148
0x08  char[32]  the file's own name
0x28  u16[] counts: materials, then two more that track file size
0x40  u32   0xC0 -> the material table
...   f32   a bounding box or base transform
0xC0  16-byte material records, last word = offset of that material's texture name
0xF0  the texture names as NUL-padded strings (".ssh", matching the folder beside the model)
0x140 f32   a 4x4 transform, 0.1 on the diagonal
```

Geometry is further in: two dense float regions holding values in ±4.14 for a 1×1-tile model, which
is the right magnitude for tile-space coordinates. Vertex/face layout **not yet established** — the
counts near 0x28 do not yet divide those regions cleanly, and nothing here should be read as if they
did.

## Not yet looked at

`MOVIES/*.MPC` (~285 MB), `AUDIO/**/*.MAP` + `*.SDT` bank pairs, `.gin`, `.mtr`, `ILINK.IRX` /
`ILSOCK.IRX`.
