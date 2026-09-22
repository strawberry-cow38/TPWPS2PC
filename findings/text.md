# `Text/translations/` — the localisation database

**Solved 2026-09-21.** `tools/textdb.py` reads it; the layout lives in its docstring.

Found while chasing something else: searching the disc for the literal `"Small Toilet"` to locate
the `.rse` script string table turned up `/DATA.WAD/Text/translations/usa/finalame.dat` as well.
The whole tree came with it.

## What is there

⚠⚠ **There are THREE regional trees and they ship DIFFERENT LANGUAGE SETS.** An earlier version of
this document described `usa/` and wrote its contents as if they were the contents. They are not:

| tree | binary tables | the difference |
|---|---|---|
| `eur/` | ame dut eng **fre** ger id ita jap spa swe | the superset, 10 |
| `usa/` | ame dut eng ger id ita jap spa swe | **no French** |
| `jap/` | dut eng **fre** ger id ita jap spa swe | **no American** |

So "read `Text/translations/`" silently returns whichever tree you happened to name. Pin the tree.

⭐ The reassuring half, measured rather than assumed: **all three `id.dat` are identical — 1,087 of
1,087 keys at the same index.** The key-to-index mapping is shared, and every table in every tree
holds exactly 1,087 rows. Only which LANGUAGES exist varies, never the indexing. `eur/` also ships
`final.dup` / `fre.dup` / `ger.dup` and `showdups.pl`, EA's duplicate-string tooling.

| file | what |
|---|---|
| `<lang>.dat` | one table per language, **1,087 entries each** |
| `id.dat` | the **symbolic keys**, same format, same count, same order |
| `include/trans.h` | the same list a **third** time, as a C `enum` |
| `final.dat`, `finalame.dat` | the plain-text masters, `[STR_KEY]` form, 338KB / 118KB |
| `kanji.table`, `kanji.lbmlist` | Shift-JIS → exported image ordinal and numbered LBM list; [consumer and regional identities](kanji-table.md). Not a Unicode conversion table or BFF descriptor index |
| `cleanlang.pl`, `showlang.pl` | EA's own build scripts, shipped to retail |

## Why it is proven rather than plausible

Three independent files state the same ordering and they agree:

* Every one of the nine tables validates its own arithmetic — first offset **equals** `4 + count*4`,
  offsets monotonic, last string ends **exactly** at EOF. Nine of nine.
* Splitting each `id.dat` entry at its first space gives **1,087 of 1,087 identical to `trans.h`'s
  enum at the same position**.
* ⚠ Control, because 1,087 of 1,087 is the kind of number that needs one: shuffling the key list
  and re-comparing scores **1 of 1,087**.

The text after that first space is the entry's **printf format spec** — 21 of 1,087 carry one, and
they are precisely the strings containing substitutions. `STR_SGRACE_BET` carries `%i%i%i%i` and
reads `Bet $%i on Racer %i\n Odds %i-%i`. The table states which strings take arguments, and of
what type, without anyone parsing the text to find out.

## Why it matters to the port

Keys encode the asset path, so this is the bridge from an asset to its name in any language:

    STR_GRAPHICS_JUNGLE_RIDES_MONKEY_MONKEY   ->  /JUNGLE/Rides/Monkey/Monkey
    Monkey.sam  Info.Name "Crazy Ape"         ->  eng.dat[1047] == "Crazy Ape"

`.sam` gives an English name; this gives all nine. A catalogue keyed on `Info.Name` is therefore
English-only by construction, which is a second reason — beyond the id collisions already recorded
— not to key on it.

## Open

* The three `kanji.table` files now have a managed reader and identity audit. Their loader/global-pointer assignment and downstream image-ID use remain unestablished; see [kanji-table.md](kanji-table.md).
* `jap/` and `usa/` differ: `usa/` has `final.dat` + `finalame.dat`, `jap/` has `finaljap.dat`.
  The kanji investigation also measured different `jap.dat` bytes: EUR/JAP 33,901 bytes,
  USA 33,655 bytes, with changed strings (for example row 3 is 収支 versus 銀行).
* ⚠ 1,066 of 1,087 `id.dat` entries end in a trailing space — the empty format spec. A parser that
  trims keys before comparing will not notice; one that does not will fail to match `trans.h` on
  1,066 of 1,087 and look catastrophically broken. Split at the first space, do not trim.
