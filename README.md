# TPWPS2PC

Notes on **Theme Park World (PlayStation 2, PAL)** — `SLES_500.32`, EA/Bullfrog.

Sibling of [TPW-PSXPC](https://github.com/strawberry-cow38/TPW-PSXPC), which ports the PSX release.
This is a **work-in-progress PS2-data-driven viewer and managed park simulation**, with
reverse-engineering evidence and executable audits. It is not a complete replacement for the
retail game. Readers, live consumers, tested behavior and remaining guesses are separate claims.

## Current development status — 2026-09-23

Start with [the completion plan](plan.md), [the latest progress ledger](progress.md), and
[the release/validation checklist](findings/release-checklist.md). The original research notebook
below contains explicitly historical sections; it is not the current backlog.

Implemented and exercised paths include compiled ride scripts, animated assets, guest walking/
ride handoffs, closed/broken-ride eligibility, ride-removal recovery, and guest-ID-based needs
storage. Needs rise rates/cadence are chosen port policy, not measured retail behavior. Track
services, full park-management behavior, advisor producers and several format fields remain
incomplete; rendering changes need separate visual checks.

The integrated park matrix currently passes JUNGLE/FANTASY and deliberately retains the exact
HALLOW Thrill Grill and SPACE Moon Buggies retail findings. An expected retail failure is not
a green pass. The launcher has independent failure-path checks, but recent launcher changes
still need Windows install/update/relaunch validation. See the checklist for precise evidence
and limitations rather than inferring release readiness from a successful build.

```sh
# .NET 8 and Python 3; use your own existing disc image in place.
dotnet run --project tools/TPW.PS2.LauncherAudit -c Release
python3 -m unittest discover -s tools -p test_audit_matrix.py
python3 tools/audit_matrix.py --disc "$DISC" --out "$NEW_EVIDENCE_DIR"
```

The matrix output directory must not already exist. Exit 0 means all selected worlds passed;
exit 1 means an unexpected result; exit 2 means only the precisely documented retail failures
remain. No disc payload is written by these commands.

## ⚠ Bring your own disc

**This repo contains no game data and never will** — no disc image, no executable, no extracted
models, textures or audio. `.gitignore` blocks them by extension. Every tool here runs against a
copy the user already owns, from their own machine.

A ROM site is not an authorised source no matter who owns the disc. The work in this repo was done
against the owner's own rip of their own copy.

## Early research measurements (historical snapshot)

These measurements predate the whole-disc census and current implementations. For the newer
inventory and its explicit limits, use [format-inventory.md](findings/format-inventory.md).
In particular, the old APS row below is historical, not the current animation-reader status.


| | |
|---|---|
| Disc | 621,734,736 B, 264,343 sectors, one `TRACK 01 MODE2/2352`. Sector user data is **2048 bytes at offset 24** |
| Filesystem | ISO9660, 209 entries: 13 `DATA/*.WAD`, 11 `MOVIES/*.MPC`, per-world `AUDIO/`, IOP modules |
| Executable | `SLES_500.32`, PS2 ELF, 32-bit LE MIPS, **one** PT_LOAD at `0x00100000`, entry `0x100008` |
| VU microcode | `.DVP.ovlytab`: five overlays, **6,944 bytes total**; three are one program in consecutive 2 KB banks |
| Archives | `FKNL` container, a **tree** of blocks; entries RefPacked unless they did not shrink |
| Compression | **EA RefPack (QFS)** |
| Models | **M3D2** — mesh table, scene graph, materials, positions, UVs and textures decoded |
| Textures | **SHPS/GM** — managed IPU and type 0x02 palette decoders; 5,695 of 5,695 pairs byte-identical to the reference; [differential results](findings/ipu.md) |
| Animation | `.aps`, magic `0x185AA030` — header and section table mapped, contents not |

**JUNGLE.WAD reads 2,545 / 2,545 files to their exact declared size, 0 failures**, and five further
archives (DATA, FANTASY, HALLOW, LOBBY, SPACE — **12,985 more files**) parse clean with the same
reader, which was built against JUNGLE alone.

## The thing worth knowing

⭐ **The PS2 release is the same game as the PSX one, not a different one.** Both call the first
world `jungle` internally (Lost Kingdom); the rides largely correspond and the shops map 1:1, with
PS2 adding about seven rides. So a port's **simulation** layer transfers from the PSX work; its
**renderer** does not — VU1 display lists and the GS are a separate implementation.

## Tools

The standalone SHPS scorer takes a directory of SSH/TGA pairs, matches stems and extensions
case-insensitively, and reports every image. It requires .NET 8:

```sh
dotnet run --project tools/TPW.PS2.SshScore -c Release -- /path/to/pairs
```

The decoder and scorer run without FFmpeg. See [managed IPU validation](findings/ipu.md) for
the differential harness, synthetic checks, exact populations, and source-image tolerances.

Audit every MPS material through the viewer's actual `AssetLibrary.TextureNear` resolver:

```sh
dotnet run --project tools/TPW.PS2.TextureAudit -c Release -- /path/to/disc.bin
# Include each successful resolution's source WAD, entry path and format:
dotnet run --project tools/TPW.PS2.TextureAudit -c Release -- /path/to/disc.bin --list
```

This requires .NET 8 and the owner's disc, without Godot or extracted fixtures. It checks the
6,007-reference population and exits nonzero on missing entries, decode errors or unreadable
archives/models. Every failed material is printed with its WAD, model and material slot.
The current measured result is **5,964 of 6,007 resolved; 43 of 6,007 unresolved**, so the gate
currently **fails**. See [resolver results and all unresolved names](findings/texture-fallback.md).

The disc tools below read a Mode 2/2352 track directly. Nothing copies a disc image.



Read and audit the three kanji export tables and two ride engine sound files:

```sh
dotnet run --project tools/TPW.PS2.LastFiveAudit -c Release -- /path/to/disc.bin
dotnet run --project tools/TPW.PS2.LastFiveAudit -c Release -- --self-test
```

The managed readers preserve regional character/image identities and the sound consumer's
sampled volume arithmetic. The audit fails on changed mappings, ordered glyph pixels, parsed
fields or evaluated curves; it includes malformed-input and count-preserving negative controls.
See [kanji consumer and Unicode-purpose correction](findings/kanji-table.md) and
[ride engine fields and unknown units](findings/ride-engine.md). The ledger retains three partly
decoded `.dba` files; this does not claim that every disc field is now understood.

Decode and audit all PS2 bitmap fonts, writing PGM text specimens and glyph atlases:

```sh
dotnet run --project tools/TPW.PS2.FontAudit -c Release -- /path/to/disc.bin /tmp/font-audit
dotnet run --project tools/TPW.PS2.FontAudit -c Release -- --self-test
```

The managed `BitmapFont` reader exposes character lookup, signed bearings, advance and four-bit
coverage. The audit fails on changes to ordered pixels, character mappings or metrics, and on
malformed input. It includes asymmetric shape checks; matching glyph totals is insufficient.
See [BFF evidence, visual inspection and limitations](findings/bff-font.md).

Audit the advisor rules and the executable's speech/text/lip catalogue across all regional text
sets and speech languages:

```sh
dotnet run --project tools/TPW.PS2.AdvisorAudit -- /path/to/disc.bin
# Print every decoded rule with its message identities:
dotnet run --project tools/TPW.PS2.AdvisorAudit -- /path/to/disc.bin --list-rules
```

The audit exits nonzero on structural or identity failures and includes count-preserving negative
controls. The sound browser displays advisor dialogue and lip associations; select an advisor
bank normally, or use `TPW_PS2_MODE=sounds TPW_PS2_SOUND=ADVISOR/ENGLISH TPW_PS2_PLAY=sp_001`.
`TPW_PS2_TEXT_REGION` selects `eur` (default), `usa`, or `jap`. See [advisor evidence and remaining
unknowns](findings/advisor.md), including the retail missing-lip defect and why `.dba` is a park
asset database rather than the speech table. The game-state producers and full advisor simulation
remain unimplemented.



Read and audit the regional park asset database payloads:

```sh
dotnet run --project tools/TPW.PS2.DbaAudit -- /path/to/disc.bin
dotnet run --project tools/TPW.PS2.DbaAudit -- /path/to/disc.bin --list
```

The reader exposes eight layouts, ride tiers, shop effects, research/cost fields, minigames and
footprint cells. The audit checks named identities and every decoded field across all three
regions, with external mutation options `--eur=FILE`, `--usa=FILE`, `--jap=FILE`, `--elf=FILE`.
Specialised fields remain unresolved; see [DBA evidence and exact gaps](findings/dba.md).


The four legacy sideshow `.MD2` models are selectable in the viewer, with their own texture
tables and validated `.mtr` companions. MTR holds node matrices and topology remaps; several
fields remain unknown. Three legacy texture choices are missing, and these models show their
static pose. See [MTR evidence, corrections and limits](findings/mtr.md).

```sh
dotnet run --project tools/TPW.PS2.MtrAudit -- /path/to/disc.bin
# Require complete texture availability (currently exits 2 for three missing choices):
dotnet run --project tools/TPW.PS2.MtrAudit -- /path/to/disc.bin --require-all-textures
```

Model texture animation uses explicit MPS filename lists and APS material/index keyframes,
at the existing 30-frame animation clock. See [animated texture evidence and limits](findings/animated-textures.md).
Audit the full disc's authored lists and keys:

```sh
dotnet run --project tools/TPW.PS2.TextureAnimationAudit -- /path/to/disc.bin
dotnet build game/TPWPS2Viewer.csproj
TPW_PS2_DISC=/path/to/disc.bin /path/to/godot --headless --path game res://tests/TextureAnimationAudit.tscn
```

The data audit currently exits 2: 408/418 multi-texture choices resolve, with ten missing
names reported individually. All 13,588 distinct key boundaries pass. The Godot 4.6.2 mono
audit passes 146 surface-binding checks, including the actual viewer's advance/pause/wrap
path; it does not compare framebuffer pixels or emulate particles/loading screens.

| | |
|---|---|
| `tools/iso2.py <bin>` | list the ISO9660 tree |
| `tools/isox.py <bin> <extent> <size> <out> [limit]` | extract one file |
| `tools/refpack.py` | `decompress(data, off) -> (bytes, declared_size, end_offset)` |
| `tools/wadtree.py` | `walk(data)` for the archive tree, `read(data, off, stored, decomp)` for one entry |

## Early open questions (historical, not the current backlog)

Many items in this original list now have readers and live consumers. Follow the current
[plan](plan.md), [format inventory](findings/format-inventory.md), and individual findings
instead of interpreting this retained notebook list as an implementation census.


- Per-file formats: `.RSE`, `.rss`, `.sam`, `.mps`, `.aps` (models/animation?), `.ssh` (EA SHPS image container)
- `MOVIES/*.MPC` — ~285 MB, codec unidentified
- `AUDIO/**/*.MAP` + `*.SDT` bank pairs
- `ILINK.IRX` / `ILSOCK.IRX` — the online features

## Historical format-list comparison — 2026-09-21

[OpenTPW](https://github.com/OpenTPW/OpenTPW) re-implements the **PC** release. Comparing format
coverage only — their status table and repo against this one, 2026-09-21:

**Only here:**

| | |
|---|---|
| `.aps` animation | no handler in their repo, and **no animation format on their table at all** — not ✅, ⚠️ or ❌ |
| `.plb` particles | **zero hits repo-wide**; no particle format listed |
| `FKNL` archive | their archive magics are `BFMU` `BFST` `BILZ` `DWFB`. **The PS2 `.WAD` is a different container from the PC one despite the shared extension.** |

**Both, with the PS2 side possibly further along:**

* `.rse` ride scripts — listed there as ⚠️ in progress; here the container plus six opcodes
  confirmed against matched `.rss` source.
* Models — theirs is `.MD2` at ⚠️, and their documentation link is `formats/m3d2.html`: the same
  format family under a different extension. Whether the ADC bit, the `mesh+0x66` batch count and
  the per-group material linkage hold on the PC variant is unchecked.
* EA RefPack — both, no divergence.

**Only there** (PC-side, or simply not needed yet): `.WCT` textures, `.SAM`, `.SDT`/`.MP2` audio,
`.BFMU`/`.BFST`/`.BFUM` strings, `.MAP`, `.TPWS`/`.INTS`/`.LAYS` saves, `.BF4` fonts, `.LIPS`,
`.MTR` materials, `.TQI` video.

⚠ **This is a comparison of format LISTS, deliberately not of implementations.** Their docs site may
carry more than the repo does, and a ⚠️ can mean anything from a stub to nearly finished. The point
of the exercise was to find what is genuinely unexplored, not to grade anyone's work — and the
answer is that **animation and particles appear to be undocumented for this game anywhere.**

## Original research priorities (historical)

These priorities describe the earlier research phase; DATA.WAD and compiled RSE execution
have since been exercised. They are retained as context, **not instructions for what to do next**.
The active work breakdown is [plan.md](plan.md).


⭐ **1. The PS2 ride scripts are documentation for the PSX port.** The PSX work
([TPW-PSXPC](https://github.com/strawberry-cow38/TPW-PSXPC)) carries ~100 findings files —
`ride-classes.md`, `ride-phase-lengths.md`, `animation-phases.md`, `behaviour.md`, `coaster-track.md`
— every one reverse-engineered from `TPW.BIN` by hand. The PS2 disc ships the same game's object
behaviour as **90 commented source scripts**:

```
RIDES      Monkey Mumbo Volcano Wateride GoKarts Coaster1 Coaster3 Bumper Bouncy IncaGod
           Spider TVSim Totem TourRide dizzyd king manic snake MineCart PorkPie Lookout
SHOPS      Balloon Burger Coconut Fries GiftShop IceCream Steak
SIDESHOWS  hyenas junspray pong sgpuzzle sgrace sgsquark sgwhack arc2x3
FEATURES   fountains bins toilets speakers gates statues bushes lights staff ferry bus
UPGRADES   lavajump mammtunn watertun
```

⚠ **As a cross-reference, never as a replacement.** The PSX binary remains ground truth for the PSX
port — PS2 and PSX are different builds and this repo spent a day learning what happens when a
plausible source is trusted over the authoritative one. The scripts confirm what was derived
correctly, explain *why* a timing is what it is, and point at the gaps that resisted disassembly.

**2. `DATA.WAD` — still never opened.** Guests, staff and the advisor live there, and it is the only
place the **20-byte skeletal animation path** can be exercised: it is fully decoded from the parser
and interpolators, and **zero files in JUNGLE.WAD use it**. Decoded-but-never-executed is the
weakest claim in this repo.

**3. Deliberately NOT next: finishing the `.rse` opcode table.** All 90 sources are on the disc, so
reading the ride logic does not need the compiled form — that only matters for *running* scripts.
Low value, feels productive.

The remaining `.aps` pointers (`+0x24` at 130 tracks, `+0x1c` at 11) are polish; the four transform
channels that matter are done.

## Running it



The **Visit the park** button runs a small visitor simulation on either SPACE terrain. Follow
Ada through paths, a queue, the Orbiter's real script and the exit. See [visitor mechanisms,
identity audit and limitations](findings/visitors.md). Characters currently move in bind pose.

```sh
dotnet run --project tools/TPW.PS2.VisitorAudit -- /path/to/disc.bin
tools/TPW.PS2.VisitorAudit/teeth.sh /path/to/disc.bin /path/to/godot-4.6.2-mono
```

The viewer's **Run ride scripts** button executes Crazy Ape, Spider, Bugs TV, Phantom and
Orbiter from their compiled disc scripts and drives their APS animations through a two-guest
cycle. [Runtime evidence, audit commands and explicit limits](findings/rse-vm.md).

```sh
dotnet run --project tools/TPW.PS2.RseAudit -- /path/to/disc.bin
```

**[Download the launcher](https://github.com/strawberry-cow38/TPWPS2PC/releases/tag/launcher)**, run
it, point it at your own Theme Park World (PS2) disc image, and press **Install and play**. It
clones this repository, builds the viewer and opens it. After that the same button reads **Update
and play** whenever `main` moves, and the launcher keeps *itself* up to date from that release — so
that download is the only manual step.

Needs the .NET 8 SDK, `git`, and **Godot 4.6.2 (mono)** — ⚠ that exact version: a 4.6 binary running
a project built against `Godot.NET.Sdk 4.6.2` loads no C# at all and shows an empty window with no
error.

⚠ **Bring your own disc.** Nothing here is bundled and nothing is downloaded from EA; the viewer
reads an image you already own.
