# TPWPS2PC

Notes on **Theme Park World (PlayStation 2, PAL)** — `SLES_500.32`, EA/Bullfrog.

Sibling of [TPW-PSXPC](https://github.com/strawberry-cow38/TPW-PSXPC), which ports the PSX release.
This repo is **research only**: what the disc holds, how its containers are shaped, and what that
implies for a port. Nothing here is a port yet.

## ⚠ Bring your own disc

**This repo contains no game data and never will** — no disc image, no executable, no extracted
models, textures or audio. `.gitignore` blocks them by extension. Every tool here runs against a
copy the user already owns, from their own machine.

A ROM site is not an authorised source no matter who owns the disc. The work in this repo was done
against the owner's own rip of their own copy.

## What is established

| | |
|---|---|
| Disc | 621,734,736 B, 264,343 sectors, one `TRACK 01 MODE2/2352`. Sector user data is **2048 bytes at offset 24** |
| Filesystem | ISO9660, 209 entries: 13 `DATA/*.WAD`, 11 `MOVIES/*.MPC`, per-world `AUDIO/`, IOP modules |
| Executable | `SLES_500.32`, PS2 ELF, 32-bit LE MIPS, **one** PT_LOAD at `0x00100000`, entry `0x100008` |
| VU microcode | `.DVP.ovlytab`: five overlays, **6,944 bytes total**; three are one program in consecutive 2 KB banks |
| Archives | `FKNL` container, a **tree** of blocks; entries RefPacked unless they did not shrink |
| Compression | **EA RefPack (QFS)** |
| Models | **M3D2** — mesh table, scene graph, materials, positions, UVs and textures decoded |
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

All read a Mode 2/2352 track directly. Nothing copies a disc image.

| | |
|---|---|
| `tools/iso2.py <bin>` | list the ISO9660 tree |
| `tools/isox.py <bin> <extent> <size> <out> [limit]` | extract one file |
| `tools/refpack.py` | `decompress(data, off) -> (bytes, declared_size, end_offset)` |
| `tools/wadtree.py` | `walk(data)` for the archive tree, `read(data, off, stored, decomp)` for one entry |

## Open

- Per-file formats: `.RSE`, `.rss`, `.sam`, `.mps`, `.aps` (models/animation?), `.ssh` (EA SHPS image container)
- `MOVIES/*.MPC` — ~285 MB, codec unidentified
- `AUDIO/**/*.MAP` + `*.SDT` bank pairs
- `ILINK.IRX` / `ILSOCK.IRX` — the online features

## What this repo has that OpenTPW does not

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

## What this is for next

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
