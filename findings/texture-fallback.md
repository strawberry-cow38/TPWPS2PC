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
