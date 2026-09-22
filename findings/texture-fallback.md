# TGA first, SSH fallback: real resolver coverage and the remaining failing gate

Measured 2026-09-22 on the owner's PAL disc, through `game/AssetLibrary.cs`.
**The requested zero-unresolved gate is not met:** 5,964 of 6,007 material references resolve,
43 of 6,007 remain unresolved, and 0 of 6,007 throw during decode. The audit exits 2.

## Implementation and population

`AssetLibrary` indexes TGA and SSH separately by filename, retaining
`StringComparer.OrdinalIgnoreCase` for both the folders and texture names. It completes the
folder-to-parent-to-shared TGA search before starting the same SSH search. A nearer SSH cannot
displace a TGA that was already selected farther out. The folder walk now includes the archive
root, whose internal key is the empty string; `/Textures/` belongs there.

`TextureImage` exposes `Width`, `Height`, top-row-first RGBA8 `Pixels`, `PartialAlpha`, and
`ClearTexels`, plus `Format`, `SourceWad`, and `SourcePath`. It leaves decoder pixels untouched
and uses Targa's existing alpha thresholds for both formats. The viewer logs the selected source
when populating its texture cache and reports missing entries and decode failures separately.
The resolver propagates decode exceptions with the selected entry and requesting material in the
message. An existing but corrupt TGA does not silently turn into a different SSH image.

`tools/TPW.PS2.TextureAudit` links the **actual** `game/AssetLibrary.cs` as a compile item. It has
no lookup implementation of its own and no Godot dependency. It opens every WAD, selects every
`.mps` entry, constructs `TPW.PS2.Data.Model`, and calls `TextureNear(entry.Path, material)` once
for every material-table slot. Duplicate names, backup models, and unused slots stay in the count.
It reads **16 of 16 WADs**, **496 of 496 models**, and **6,007 of 6,007 material references**.

The gate requires the expected material population, all discovered archives/models readable,
and zero unresolved references or decode exceptions. Each failed material is printed with its
WAD, model path, slot and name; `--list` also prints every successful source. No WAD, image, pixel
buffer or fixture is written by the audit.

## What was tried, including failures

All material counts below come from `TextureNear`, compiled from the production source at that
stage. The baseline probe also linked that file directly. These are measured changes in the
resolver, not competing estimates from alternate audit lookup rules.

| Resolver run | TGA | SSH | Unresolved | Decode threw | Exit |
|---|---:|---:|---:|---:|---:|
| Original TGA-only resolver | 5,493 of 6,007 | 0 of 6,007 | 514 of 6,007 | 0 of 6,007 | 2 |
| Add SSH, retain original root-skipping walk | 5,493 of 6,007 | 24 of 6,007 | 490 of 6,007 | 0 of 6,007 | 2 |
| Include archive root; final production policy | 5,940 of 6,007 | 24 of 6,007 | 43 of 6,007 | 0 of 6,007 | 2 |
| Deliberately remove SSH lookup | 5,940 of 6,007 | 0 of 6,007 | 67 of 6,007 | 0 of 6,007 | 2 |
| Deliberately pass empty bytes to SSH decoder | 5,940 of 6,007 | 0 of 6,007 | 43 of 6,007 | 24 of 6,007 | 2 |
| Diagnostic SSH-only selection | 0 of 6,007 | 5,964 of 6,007 | 43 of 6,007 | 0 of 6,007 | 2 |
| Restored production policy, rerun | 5,940 of 6,007 | 24 of 6,007 | 43 of 6,007 | 0 of 6,007 | 2 |

The initial extension-only change was insufficient: **447 of 490 remaining failures** were
LOBBY references whose TGAs were already indexed at the unvisited root. This also corrects the
historical statement that the lobby was almost entirely SSH-only.

The fallback fault test edited the production selection line to remove
`?? FindTexture(modelPath, stem + ".ssh")`, rebuilt, ran the same audit, then restored it.
Unresolved increased by **24 of 6,007 references**, from **43 of 6,007** to **67 of 6,007**.
Both runs exit 2 because the original gate already fails. This proves the audit detects lost
fallback coverage; it does **not** claim a zero-to-nonzero transition that never occurred.
The empty-byte mutation separately proves the decoder exception category remains observable.
All temporary source mutations were restored in a `finally` block, and the restored audit and
viewer were rebuilt afterward. No mutation switch is left in production.

SSH-only selection was a diagnostic, rejected as the port policy because the requested policy
prefers TGA. It has the same unresolved population. No blanket cross-WAD or sibling-folder search,
fuzzy spelling match, guessed alias, placeholder, or exclusion of material slots was added to
make the gate appear successful.

## Why the remaining references are still failures

The diagnostic inventory used all **14,678 of 14,678 WAD entries**, including **5,764 of 5,764 SSH
entries** and **5,695 of 5,695 TGA-named entries**. It compares stems case-insensitively to explain
the resolver's already-reported failures; it does not supply an alternate resolution result.

| Remaining population | References |
|---|---:|
| Same-named texture exists in a different, unrelated folder of the open WAD | 2 of 43 |
| Same-named texture exists only in another WAD | 25 of 43 |
| No same-stem entry of any extension in any WAD | 16 of 43 |

The same-WAD cases are `trans01.ssh` on FANTASY's water ride, with a candidate in
`/Upgrades/beejump/textures/`, and `door.ssh` on HALLOW's vampire shop, with a candidate in
`/rides/ratrace/textures/`. Picking a different ride's texture by basename is precisely the
misassociation the scoped resolver was built to prevent.

The other-WAD cases include DATA's balloon models referring to world-specific shared textures,
FANTASY's `PGOKARTS.mps` carrying HALLOW texture names, and SPACE's `arm.ssh` matching names in
DATA and JUNGLE. Filename availability alone has not established which candidate is intended.

| Names with no same-stem WAD entry | References among the remaining failures |
|---|---:|
| `flowerpot.ssh` | 1 of 43 |
| `Mutant_Eye.ssh` | 1 of 43 |
| `Brain_Glow.ssh` | 1 of 43 |
| `hpa_ctr2.ssh` | 1 of 43 |
| `ant_spike.ssh` | 1 of 43 |
| `jpa_que4.ssh` | 8 of 43 |
| `wr_flap.ssh` | 3 of 43 |

These are statements about the full filename inventory. An intended alias or runtime substitution
could still exist; none was established in this job. Resolving these references needs evidence
for such mappings or an explicit policy for missing art. The gate remains failing meanwhile.

Every final unresolved reference, verbatim from the audit (slot numbers are zero-based):

```text
UNRESOLVED /DATA/DATA.WAD/Chars/Gnome/gnome.mps [6] 'flowerpot.ssh'
UNRESOLVED /DATA/DATA.WAD/Generic/Advisor/advisor.mps [28] 'Mutant_Eye.ssh'
UNRESOLVED /DATA/DATA.WAD/Generic/MiscMesh/BalFantasy.mps [0] 'balloonf.ssh'
UNRESOLVED /DATA/DATA.WAD/Generic/MiscMesh/BalHallow.mps [0] 'balloonh.ssh'
UNRESOLVED /DATA/DATA.WAD/Generic/MiscMesh/BalJungle.mps [0] 'balloonj.ssh'
UNRESOLVED /DATA/DATA.WAD/Generic/MiscMesh/BalSpace.mps [0] 'dr_balloon.ssh'
UNRESOLVED /DATA/DATA.WAD/Generic/MiscMesh/testpuke.mps [0] 'd_POOL3.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [0] 'PURPLE_BASE.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [1] 'hp_base1.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [2] 'hp_base6.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [3] 'hp_base7.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [4] 'hp_base.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [5] 'h_skull.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [6] 'h_bell.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [7] 'h_glow2.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [8] 'h_roof.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [9] 'h_tower.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [10] 'h_wall1.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [11] 'h_roof4.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [13] 'h_flame.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [14] 'h_wtop2.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [15] 'h_bridge.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [16] 'h_wood.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [17] 'h_wtop.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [18] 'h_trkunder.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/gokarts/PGOKARTS.mps [19] 'fire0.ssh'
UNRESOLVED /DATA/FANTASY.WAD/Rides/wateride/wr_trckh_a.mps [0] 'trans01.ssh'
UNRESOLVED /DATA/FANTASY.WAD/terrain/terrain_1.mps [61] 'jpa_que4.ssh'
UNRESOLVED /DATA/FANTASY.WAD/terrain/terrain_2.mps [61] 'jpa_que4.ssh'
UNRESOLVED /DATA/HALLOW.WAD/rides/brainb/brainb.mps [13] 'Brain_Glow.ssh'
UNRESOLVED /DATA/HALLOW.WAD/rides/gokarts/PGOKARTS.mps [13] 'hpa_ctr2.ssh'
UNRESOLVED /DATA/HALLOW.WAD/shops/vampshop/vampshop.mps [14] 'door.ssh'
UNRESOLVED /DATA/HALLOW.WAD/terrain/terrain_1.mps [61] 'jpa_que4.ssh'
UNRESOLVED /DATA/HALLOW.WAD/terrain/terrain_2.mps [61] 'jpa_que4.ssh'
UNRESOLVED /DATA/JUNGLE.WAD/Rides/Wateride/Pwateride.mps [20] 'wr_flap.ssh'
UNRESOLVED /DATA/JUNGLE.WAD/Rides/Wateride/wr_trckh.mps [4] 'wr_flap.ssh'
UNRESOLVED /DATA/JUNGLE.WAD/Rides/Wateride/wr_trckh_u.mps [4] 'wr_flap.ssh'
UNRESOLVED /DATA/JUNGLE.WAD/terrain/terrain_1.mps [77] 'jpa_que4.ssh'
UNRESOLVED /DATA/JUNGLE.WAD/terrain/terrain_2.mps [73] 'jpa_que4.ssh'
UNRESOLVED /DATA/LOBBY.WAD/space1.mps [59] 'ant_spike.ssh'
UNRESOLVED /DATA/SPACE.WAD/Rides/whirli/whirli.mps [16] 'arm.ssh'
UNRESOLVED /DATA/SPACE.WAD/terrain/terrain_1.mps [63] 'jpa_que4.ssh'
UNRESOLVED /DATA/SPACE.WAD/terrain/terrain_2.mps [63] 'jpa_que4.ssh'
```

## Regression results

| Check | Before | After |
|---|---:|---:|
| Existing synthetic assertions | 2,164 of 2,164 | 2,164 of 2,164 |
| Paired SSH within scorer tolerance | 3,542 of 5,695 | 3,542 of 5,695 |
| Paired SSH decoded | 5,695 of 5,695 | 5,695 of 5,695 |
| FLAT RGB exact | 19 of 121 | 19 of 121 |
| FLAT RGBA exact | 2 of 121 | 2 of 121 |
| Uppercase `.TGA` partners decoded | 1,132 of 1,132 | 1,132 of 1,132 |
| Disc checker models read | 496 of 496 | 496 of 496 |
| Disc checker TGAs decoded | 5,694 of 5,694 | 5,694 of 5,694 |
| Disc checker APS files read | 374 of 374 | 374 of 374 |

All **5,764 of 5,764 scorer records** are identical before/after, including failures and missing
partners. Scorer exit is 2 before and after; the self-tests and full disc checker exit 0 before
and after. The complete disc checker report is unchanged. Its TGA count separately names the
**1 of 5,695 TGA-named entries** that is actually PNG, exactly as before this job.

An external probe called the original and final `TextureNear` for **6,007 of 6,007 slots** and
stored only dimensions, alpha counts, names and SHA-256 hashes. All **5,493 of 5,493 previously
resolved references** have identical RGBA hashes, dimensions and alpha classifications after
the change. The final viewer Release build succeeds. No decoder or scorer source was changed.

## Type 0x02 skies

The skies are not special-cased by the resolver. A separate probe enumerated the **8 of 8 SHPS
type 0x02 entries**, called `TextureNear` from each entry's folder, and compared the returned
pixels with the directly selected decoder. Normal TGA preference selected TGA for **8 of 8**;
temporarily skipping TGA selected SSH for **8 of 8**. In both runs the wrapper matched its direct
decoder byte-for-byte for **8 of 8** images.

The back skies match across formats for **4 of 8 pairs**; the cloud layers differ for **4 of 8
pairs**. Their SSH representation decodes as opaque with the existing `Ssh` class, while the TGA
contains the cloud opacity. This is the pre-existing channel-representation difference recorded
in [the type 0x02 investigation](ipu.md#type-0x02-derivation-and-final-scorer), not a new resolver conversion.
The sky names occur in **0 of 6,007 MPS material slots**; they do not belong in the material-audit
denominator. The old subtraction of skies from the material total has been corrected in
[texture-lookup.md](texture-lookup.md).

## Reproduction

```sh
dotnet run --project tools/TPW.PS2.TextureAudit -c Release -- /path/to/disc.bin --list
dotnet run --project tools/TPW.PS2.SshScore -c Release -- --self-test
dotnet run --project tools/TPW.PS2.SshScore -c Release -- /path/to/pairs --json /outside/repo/score.json
dotnet run --project tools/TPW.PS2.Check -c Release -- /path/to/disc.bin
dotnet build game/TPWPS2Viewer.csproj -c Release
```

For the fallback fault test, temporarily remove the SSH lookup from `TextureNear`, rebuild and
run the same audit, then restore the source and rebuild again. Do not use `--no-build` across a
source mutation. For the decode fault test, temporarily pass `Array.Empty<byte>()` to `new Ssh`
in that same method. Neither test edits the disc or decoder.

This run's raw logs, filename inventory, probes, hashes, scorer JSON and source-mutation harness
are outside the repository at `/tmp/tpw-texfallback-validation/`. The disc and existing scorer
inputs stayed under `/home/ec2-user/tpw-ps2/`. No game bytes or textures were added to this worktree.

## Status: UNFINISHED, deliberately, and this is the shape of what is left

**Closed on 2026-09-22 by the project owner: "everything in the game looks textured. its fine."**
Recorded rather than fixed, because the remaining 43 are not one problem and neither half is urgent.

    material references   6007 of 6007
      resolved to .tga    5940
      resolved to .ssh      24        <- these rendered BLANK before this change
      UNRESOLVED            43
      decode threw           0

**29 of the 43 are a resolver-scope question, not missing art.** Those textures exist on the disc;
the per-WAD walk does not reach them. `h_bell`, `h_roof`, `h_skull` and the rest sit in
`HALLOW.WAD/rides/gokarts/textures`; `hp_base`, `hp_base1`, `hp_base6`, `hp_base7` and `h_glow2` sit
in **`DATA.WAD/Ultimate/Sharetex`**, a global pool no per-archive search reaches. Widening the
search closes these; the risk in doing so is the one already recorded in `AssetLibrary` — a flat
by-name search once put Mumbo's sign on Crazy Ape, because 22 rides each ship a `sign_eng.tga`.
**Widen carefully or not at all.**

**14 of the 43 cannot be closed.** Five stems appear in no form anywhere on the disc:

    flowerpot   hpa_ctr2   jpa_que4 (x8)   mutant_eye   wr_flap (x3)

Dead references in EA's own art. `jpa_que4` is requested by **SPACE**'s terrain while carrying a
*jungle* prefix, which reads like a copied model nobody re-pointed.

⚠ **So the floor is 14, and the gate this job was given ("zero unresolved") was unmeetable.** That
is the second acceptance bar set in this repo that cannot be reached by construction — the other
being flat-colour exactness in `ssh-colour.md`, where 26 of 117 references are unreachable by any
input. Both were honest and ungameable. Both were partly measuring something impossible. **A gate
being unfittable is not the same as it being achievable**, and the way to tell is to ask what the
best possible implementation would score before adopting the number.

⚠ And a counting note, because it nearly went out wrong: the first split was reported as 13/30. The
grep behind it matched `[a-z0-9_]+` and four of the stems are `Brain_Glow`, `Mutant_Eye`,
`PURPLE_BASE`, `d_POOL3`. The real split is **14/29**.

## The ground tiles, and why HALLOW looks like it has none (2026-09-22)

Prompted by a report from `cow tools` while laying the park floor: *"HALLOW has no `*_bas*` tile at
all where JUNGLE/FANTASY have `jgr_bas2..6`."* True as stated, and misleading as a conclusion — it
is the same resolver-scope case as the rest of this file. Every `*_bas*` stem on the disc:

| stem | lives in |
|---|---|
| `jgr_bas1` | `DATA.WAD/Ultimate/Sharetex`, `FANTASY.WAD/sharetex`, `HALLOW.WAD/sharetex`, `JUNGLE.WAD/Sharetex`, `LOBBY.WAD/Textures` |
| `jgr_bas2..6` | `DATA.WAD/Ultimate/Sharetex`, `FANTASY.WAD/terrain/textures`, `JUNGLE.WAD/terrain/textures` |
| `hrk_bas1..7` | **`HALLOW.WAD/sharetex`** (`hrk_bas1` also in `DATA.WAD/Ultimate/Sharetex`, `LOBBY.WAD/Textures`) |
| `hgr_bas2` | `DATA.WAD/Ultimate/Sharetex`, `HALLOW.WAD/sharetex`, `LOBBY.WAD/Textures` |
| `sfl_bas1..6` | `SPACE.WAD/terrain/textures` (`sfl_bas2` also `SPACE.WAD/sharetex`, `LOBBY.WAD/Textures`) |
| `bmp_bas3` | `SPACE.WAD/Rides/bumper/textures` — a bumper-car texture that merely matches the pattern |

**HALLOW's ground set is `hrk_bas1..7` plus `hgr_bas2`, and it is in `sharetex`, not `terrain`.**
Nothing is missing. A walk rooted at `/terrain/` finds ground for three worlds and none for the
fourth, which looks exactly like absent art.

Three traps in here, all of them about names rather than pixels:

1. **The world prefix does not follow the world.** `FANTASY.WAD/terrain/textures` ships `jgr_bas2..6`
   — *jungle's* tiles, under a jungle prefix, with no `fgr_*` anywhere. Choosing a tile set by
   deriving a prefix from the world name works for JUNGLE, HALLOW and SPACE and silently picks
   nothing for FANTASY.
2. **The directory name mixes case too**, not just the files: `JUNGLE.WAD/Sharetex` against
   `HALLOW.WAD/sharetex` against `DATA.WAD/Ultimate/Sharetex`. Case-folding only the leaf is not
   enough; fold the whole path.
3. **The filename case mixing is not a HALLOW quirk.** Extensions mix inside every world —
   `JUNGLE.WAD/terrain` alone holds `jpa_ctr1.TGA` next to `jpa_cnr1.tga`, 28 of its 159 terrain
   files being upper-case. What *is* HALLOW-specific is a leading capital on the **stem**:
   `Jpa_icn1`, `Jpa_squ1` and 8 others, against 0 such in JUNGLE and SPACE.

`game/AssetLibrary.cs` is already `StringComparer.OrdinalIgnoreCase` on every dictionary and every
path comparison, so none of this reaches our resolver. It reaches anything written beside it.
