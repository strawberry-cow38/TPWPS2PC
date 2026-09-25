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

## Features, shops and sideshows

Same sources and method as the rides. On the PSX the build-table names include unresolved `?0x..`
ids. Those are folio entries whose record `fable/records.json` did not parse. They were resolved
from the ripped entry: the record sits at `data + data[0x14]`, with `{u32 type, u32 name text id}`,
and the name comes from the English text table (`fable/text.py`). Examples: 0xC3 is Loudspeaker and
0x13 is Litter Bin.

- **Shops** are identical: all four worlds have the same eight on both versions. "Fries" is
  "Fries Shop".
- **Sideshows:** every PSX world has a themed strength game and a themed bash game. The PS2 has a
  generic "Strength Test" and renamed bashes (Gopher, Devil, Mole and Alien Bash). These are probably
  renames, not cuts; that is unverified. Busta Block is new on the PS2 in every world.
- **Features:** the PS2 has 26 to 30 per world against 12 to 14. The additions are scenery and the
  "shows". The Loudspeaker is the one PSX feature missing from every PS2 world. "Security Camera"
  in PSX Space is "Security Cameras" on the PS2.

Raw per-world output, by normalised name, before those manual notes:

```
#### features
== JUNGLE: psx 13 ps2 30
  both: Large Tree, Litter Bin, Mammoth Fountain, Security Camera, Small Rock, Small Tree, Staff Room, Super Toilet, Toilet
  psx only: Big Palm, Fountain, Loudspeaker, Small Palm
  ps2 only: Colourful Bush, Golden Idol, Golden Statue, Huge Hollow Rock, Huge Leafy Rock, Large Rock Pillar, Lava Fountain, Leafy Bush, Medium Bush, Round Fountain, Screeches, Small Bush, Small Rock Pillar, Stone Head, Stone Statue, Strange Deep, Tiny Rock, Tropical Flower, Undergrowth, Wild Beasts, Wooden Log
== HALLOW: psx 14 ps2 30
  both: Demon Statue, Gargoyle, Gravestone, Huge Rock, Large Rock Pillar, Litter Bin, Security Camera, Small Toilet, Staff Room, Super Toilet
  psx only: Brown Bush, Green Bush, Loudspeaker, Pumpkin
  ps2 only: Finger, Firework Show, Furry Fiends, Giant Pumpkin, Kid Creosote Fountain, Large Tree, Medium Bush, Medium Rock, Medium Tree, Monster Hand, Night Creatures, Small Bush, Small Rock, Small Tree, Spooky Spirits, Tentacle, The Bells, Tiny Rock, Tower, Trident
== FANTASY: psx 12 ps2 30
  both: Bricks, Card Statue, Donut, Grass, Large Flower, Litter Bin, Plant Pot, Security Camera, Small Toilet, Staff Room, Super Toilet
  psx only: speaker
  ps2 only: Candy Cane, Cards, Clown Capers, Coin, Crayon, Fairy Frolics, Fork, Fountain, Hurry Up Havoc, Lollipop, Lucky Card, Medium Flower, Mushroom, Pencil, Small Flower, Sun Flower, Sweet, Teapot, Trowel
== SPACE: psx 13 ps2 26
  both: Alien Bubbler, Gyrotron, Large Tree, Litter Bin, Medium Bush, Small Bush, Small Toilet, Staff Room, Strange Growth, Super Toilet
  psx only: Loudspeaker, Plasmatrope, Security Camera
  ps2 only: Antenna, Comet Comms, Crater, Giant Robot, Golden Rocks, Ground Control, Large Crystals, Laser Show, Medium Tree, Obelisk, Pulsar, Security Cameras, Small Crystals, Space Dust, Tower, UFOs
#### shops
== JUNGLE: psx 8 ps2 8
  both: Balloon Shop, Burger Shop, Costume Shop, Drinks Shop, Gift Shop, Ice Cream Shop, Restaurant
  psx only: Fries
  ps2 only: Fries Shop
== HALLOW: psx 8 ps2 8
  both: Balloon Shop, Burger Shop, Costume Shop, Drinks Shop, Fries Shop, Gift Shop, Ice Cream Shop, Restaurant
  psx only: 
  ps2 only: 
== FANTASY: psx 8 ps2 8
  both: Balloon Shop, Burger Shop, Costume Shop, Drinks Shop, Fries Shop, Gift Shop, Ice Cream Shop, Restaurant
  psx only: 
  ps2 only: 
== SPACE: psx 8 ps2 8
  both: Balloon Shop, Burger Shop, Costume Shop, Drinks Shop, Fries Shop, Gift Shop, Ice Cream Shop, Restaurant
  psx only: 
  ps2 only: 
#### sideshows
== JUNGLE: psx 6 ps2 8
  both: Arcade, Dino Racing, Giant Puzzle
  psx only: Idol Smash, Strength Bird, Sun Shooter
  ps2 only: Busta Block, Gopher Bash, Jungle Spray, Laughing Hyenas, Strength Test
== HALLOW: psx 6 ps2 8
  both: Arcade, Fortune Teller, Giant Puzzle, Pumpkin Shy, Shooter
  psx only: Bone Crusher
  ps2 only: Busta Block, Devil Bash, Strength Test
== FANTASY: psx 6 ps2 7
  both: Aqua Spray, Arcade, Fruit Shy, Giant Puzzle
  psx only: Strength Flower, Worm Bash
  ps2 only: Busta Block, Mole Bash, Strength Test
== SPACE: psx 6 ps2 7
  both: Arcade, Giant Puzzle, Martian Mooners, UFO Blaster
  psx only: Martian Mash, Strength Rocket
  ps2 only: Alien Bash, Busta Block, Strength Test
```
