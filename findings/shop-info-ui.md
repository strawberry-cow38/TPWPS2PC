# The shop's info panel, and the special ingredient

Master: "can u research the info UI? lets do the shop config info ui", then "keep searching and
figure it out". Evidence is this disc's `SLES_500.32` (vaddr = file offset + `0xFF000`) and its
`arsdb.dba`. Static analysis, not a console trace.

## The panel

Built at **`0x108458`**:

| what | value |
|---|---|
| size | **260 x 180** |
| y | **210** |
| x | **`(screenWidth - 260) / 2`** -- centred, width asked of `0x20a120` |
| skin | sprite **48 = `Messfill`** (the blue frame, not the ruled sheet) |

⭐ It asks the engine for the screen width rather than hardcoding one, so the original is already
resolution-independent here; a port that bakes 512 in would be less faithful, not more.

The panel object's fields are the shared widget ones (`+8`/`+10` x/y, `+20`/`+22` w/h, `+28`
visible, `+72` skin), plus `+92 = 16`, `+96 = 14`, `+76 = 114` for this panel.

## The rows

Content builder at **`0x1d7288`** onward, one `0x138580(panel, textId, x, y, ...)` per row, in
this order: **Customers · Cost of Goods · Takings · Profit · Satisfaction · Quality**, then
**Sale Price** at `0x1d7530`. Row geometry is read from globals rather than baked: x at
`[0x2E9E08]`, first y at `[0x2E9E0C]`, step at `[0x2E9E68]`.

⭐ **Quality, Sale Price and Satisfaction each occur EXACTLY ONCE in the whole executable**, all
inside `0x1d73xx..0x1d75xx`. That uniqueness is what identifies the region as this panel rather
than a plausible-looking neighbour.

Captions are `STR_SINGLESHOP_*` in `Text/translations/<tree>/`: title **Single Shop**, tabs
**Details** (`_INFORMATION`) and **Options**.

## The special ingredient

From `0x1d1f60`:

```
id = shopIngredientId()                        ; 0x1d1d08
if (id == 2 || id == 3 || id == 6) return 0    ; no special ingredient
return *(u32*)(0x2E9A78 + id * 4)              ; -> text index
```

A flat `u32` array at **`0x2E9A78`**, indexed by **id x 4**:

| id | caption | id | caption |
|---|---|---|---|
| 0 | Fat | 4 | Sugar |
| 1 | Ice | 7 | Salt |
| 2, 3, 6 | none (guarded before the load) | others | 0 |

The id is **DBA data**, not a UI value: `0x1d1d08` calls **`0x10fa30`** -- the payload accessor
established in [dba.md](dba.md) -- and reads **one byte at payload `+0x30`**.

⭐ Checked against the shipped `arsdb.dba`: key **241 (JUNGLE Coconut) = 01 Ice**, 243 = 07 Salt,
245 = 04 Sugar, and 242 / 244 = 02 / 06. The data contains **exactly the guarded ids**, so that
guard is live code rather than something read into it, and a coconut drink resolving to Ice is an
independent sanity check on the whole chain.

## Two ways this was got wrong first

⚠ **The table was called "runtime-populated, nearly all zeros" and abandoned.** It is static and
was always in the file; the zeros are the array's UNUSED SLOTS. A 12-byte stride had been invented
to explain "irregular gaps" that were never gaps. Xref'ing the base (`0x2e9a78`, materialised once
at `0x1d1f94`) and reading the indexing code settles in one step what byte-pattern fitting cannot:
absence of structure in a dump is not absence of structure.

⚠ **"Salt" is text index 1**, which collides with the commonest immediate in MIPS. A scan for the
ingredient captions "found" Salt six times in code and every hit was an ordinary
`addiu rX, zero, 1`. Sanity-check a low-numbered text id against how common that immediate is
before believing the hits.

## Not established

* The **Options** tab's slider layout and value ranges. `BARSLIDE`/`BARPROG`/`BARKNOB` are the art
  (grey track, yellow fill) but nothing here reads their geometry or the price step.
* Which shop each DBA key is, beyond Coconut = 241 from the shop audit's own fixture.
* Nothing here is a claim about how the panel is *reached* -- the listbox virtual that decides a
  selected object's entries is still undecoded (see the gizmo/listbox notes).
