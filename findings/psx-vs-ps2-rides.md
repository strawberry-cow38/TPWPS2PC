# Rides: PSX against PS2

2026-09-25, tinyclaw, at strawberry's request.

## Sources

- **PSX** (SLES-026.88): the build-menu theme tables (rides A and B, 14 per theme) plus the coaster,
  track and tour records, from the PSX project's `fable/rides_themes.json` and `fable/records.json`.
- **PS2** (SLES-500.32, EUR): the compiled attraction table `arsdb.dba` (`TPW.PS2.DbaAudit --list`),
  kinds 1/3/6/7. That is what the console can build. Names come from the EUR `eng` text for each
  entry's `STR_GRAPHICS_` row.

Matching is by normalised name. Four spelling variants were merged by hand:
- Chac Atak / Chak Atak;
- Hocus Pocus / Hocus Pokus;
- The Areotron / The Aerotron;
- Bumble Buggies / "Bee Karts": the PS2 `FANTASY/Rides/gokarts`, whose `.sam` still says Bumble
  Buggies.

A ride renamed between versions would otherwise appear once on each side. Diplo-Dip against Dizzy
Dinos is the likeliest such pair, and it is unverified.

| World | Both | PSX only | PS2 only |
|---|---|---|---|
| Jungle | 17 | Aztec Bounce, Diplo-Dip, Inca Pot | Dizzy Dinos, King of the Swingers, Manic Mayan, Slither* |
| Halloween | 16 | Crazy Clown, Eye Slide, Jumping Skulls, Thrill Grill | Bone Shaker, Dare Devil (coasters), Ghost Train, Haunted House, Pumpkin Castle*, Spooky Spider* |
| Wonderland | 17 | Escargot A-Go Go, Flying Fishes | Rubber Ringos, Well Drop |
| Space | 18 | Romper Stomper | G-Force, Tubes of Zob, Uforia*, Whirligig, Wormhole |

\* The PSX has an attraction record for it, but it is in no PSX build table.

## Notes

- **Thrill Grill:** the PS2 HALLOW.WAD keeps `/rides/firepit/`, with its script, `.sam`, sign and
  textures, but no `.mps` or `.aps` in that folder, and it has no compiled table entry. It cannot be
  built on PS2. **Its mesh does ship:** `LOBBY.WAD/hallow2.mps`, the Halloween lobby diorama, uses all
  seven `fp_*` textures, and `hallow2.aps` sits beside it. An earlier line here said "no model
  anywhere on the disc". That came from a filename search and was corrected after strawberry had
  seen it in the model viewer. The audit's "missing slot 4" was the CryptKarts Jump add-on's
  `firepit.aps` found by stem (see visitors.md).
- **Whirligig:** a stray `SPACE/Rides/whirli.sam` has no model. The real bundle
  `SPACE/Rides/whirli/` has one.
- `Ghost Train` is the folder `HALLOW/Rides/bellt` (`.sam` name `_bellt`). `Phantom` is `<PHANTOM>`
  in its `.sam`.

## Park sizes

**PSX.** FOLIO.GAZ has eight map resources in the loader's layout: `u32 N; u32 tab[N]; u32 w; u32 h;
8-byte tiles` (0x800544E0; see the PSX project's `fable/paths.md`). They are entries 34/35, 116/117,
203/204 and 355/356, and all eight are **44×74**. Each has 38 tiles of pre-laid path. Buildable
tiles (flag `0x02` clear, inside the loader's `w−1 × h−1` bounds): 1997, 1922, 1821, 1845, 2010,
1837, 2153 and 2158. Which world each entry belongs to is not pinned.

**PS2.** Terrain grids and buildable (drawn) cells:

| Park | Grid | Buildable | Raised (0x40) |
|---|---|---|---|
| Jungle 1 | 64×76 | 3387 | 20 |
| Jungle 2 | 64×76 | 3314 | 20 |
| Halloween 1 | 96×52 | 3906 | 12 |
| Halloween 2 | 88×56 | 3924 | 33 |
| Wonderland 1 | 80×60 | 3928 | 12 |
| Wonderland 2 | 76×62 | 3604 | 88 |
| Space 1 | 96×54 | 4068 | 158 |
| Space 2 | 72×62 | 3176 | 152 |

Both versions use 256-unit tiles, and on both the no-build tile flag is bit 1. The PS2 fills it from
the drawn bit at 0x14E700. So the counts compare directly: a PS2 park has about 1.9× the buildable
ground of a PSX park.
