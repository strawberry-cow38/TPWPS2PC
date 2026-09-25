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
  textures. There is no `.mps` or `.aps` anywhere on the disc, and it has no compiled table entry.
  It cannot be built on PS2. The audit's "missing slot 4" was the CryptKarts Jump add-on's
  `firepit.aps` found by stem (see visitors.md).
- **Whirligig:** a stray `SPACE/Rides/whirli.sam` has no model. The real bundle
  `SPACE/Rides/whirli/` has one.
- `Ghost Train` is the folder `HALLOW/Rides/bellt` (`.sam` name `_bellt`). `Phantom` is `<PHANTOM>`
  in its `.sam`.
