# Which texture does the engine load? Read out of the executable, not inferred

**Answered 2026-09-22 from `SLES_500.32`** (the PS2 boot ELF, 2,822,496 bytes, at the ISO root).
This retires the "24 versus 473 materials need the decoder" question: **both were answers to a
lookup rule that the engine does not use.**

## What the executable actually contains

    .ssh   219 occurrences        .tga   8 occurrences        .TGA   1

And the engine **builds** `.ssh` paths programmatically. Three format strings, no `.tga` equivalent:

    0x2624d0   %s1%s.ssh
    0x262518   %skid%04d.ssh
    0x262c78   %s.ssh

⭐ **Every single `.tga` reference in the executable is a sky texture, and they form one hardcoded
per-world table** at 0x260f60–0x261060 — a directory string followed by its two filenames, four
times:

    Data/Jungle/Sky/     Jungle_back.tga     Jungle_front2.tga
    Data/Space/Sky/      Space_back.tga      Space_front2.tga
    Data/Hallow/Sky/     Hallow_back.tga     Hallow_front2.tga
    Data/Fantasy/Sky/    Fantasy_back.tga    Fantasy_front2.tga

## The answer

**The engine loads `.ssh` for every material, and `.tga` for exactly eight hardcoded sky textures.**

There is no extension-fallback rule and no directory search to reverse-engineer: material names in
`.mps` already carry `.ssh` (all 6,007 of them), and the executable turns names into `.ssh` paths by
`sprintf`. The eight skies are not found by a rule at all — they are named in the binary.

### What that does to the counts

| claim | status |
|---|---|
| 5,493 resolve to a `.tga`, 473 need the decoder | describes what is **available**, not what is used |
| 5,942 resolve to a `.tga`, 24 need the decoder | same, under a wider resolver |
| **the engine uses `.ssh` for all 6,007** | what it does |

Both counts were correct about availability and neither described behaviour. The 449 references they
disagreed over are a property of the two search rules, and the engine runs neither of them.

⭐ Fifth independent route to the same eight files. They are: the only SHPS **type 0x02**
(uncompressed) entries on the disc; the only **paletted** TGAs on the disc; the four `*_front2`
carrying a **lying header** that claims true-colour at 8bpp; the eight that failed to decode before
type 0x02 was implemented; and now the only eight the executable names as `.tga`.

## Consequences for the port

* **The SSH decoder is required for 5,999 of 6,007 materials**, not 24. It is not optional and not a
  fallback — it is the path.
* Preferring the `.tga` where one exists would make the port look **better than the PS2**, because
  the TGA is the pre-compression source. That is a legitimate choice and now an informed one, but it
  is a deviation from the hardware, not a fidelity fix.
* The 2,153 pairs outside the scoring tolerance remain encoder loss, and now demonstrably so: the
  player never sees the TGA, so the difference between our decode and it is exactly what the encoder
  discarded.

⚠ **Scope of this claim.** This is read from the string table and the format strings — it shows what
the binary contains and constructs, not the control flow that consumes them. A loader could in
principle still substitute an extension at runtime. Nothing in 219 `.ssh` references and 8 named
skies suggests it does, and there is no `%s.tga` builder for it to use, but the call sites have not
been disassembled.
