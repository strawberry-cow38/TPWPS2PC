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
| Models | **M3D2** — mesh table decoded, positions decoded and drawing |

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
