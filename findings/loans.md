# Loans after the accept: repayment, Existing Loans, and every reader of the loan records

Researched 2026-10-01. Source: `SLES_500.32` (PAL, native addresses, `vaddr = file offset + 0xFF000`).
Ghidra decompiles (seat `agent_loans`, plus a whole-program dump of `0x100000..0x2B0000`, 7,294
functions, 0 failures), hand-read MIPS for every load-bearing line, `tools/re/xref.py` with controls,
an offset scanner over the ELF, and `Text/translations/eur/{eng,id}.dat`.

Tags: **READ** = seen in code at the cited address. **INFERRED** = follows from READ facts by
arithmetic or by the absence of something, and says what it rests on.

Builds on `findings/finance-screens.md` §3 and "Existing Loans (page 4) for contrast". Corrections to
that file and to `ParkFinances.cs` are collected in §5.

---------------------------------------------------------------------------------------------------

## 0. The record, with every writer and reader

`loan[i] = park + 0xC + i*0x2C`, i = 0..3 (Mr Byrne, Ms Dabb, Mr Howell, Ms West). Money fields are
**whole dollars**; the park's own accumulators are tenths.

| off | meaning | written by | read by |
|---:|---|---|---|
| +0x00 | lender index | `0x17E504` (init) | `0x17E670` → name |
| +0x04 | amount borrowed $ | init `0x17E558/0x17E578/0x17E598`; New Loan `0x135F38` every frame while available; load `0x17E6D4` | accept `0x100CF0`; `0x17E604`; Existing Loans `0x135C2C`; save `0x17E690` |
| +0x08 | term, **months** (`years*12`, `0x135F48..0x135F54`) | ⚠ init `0x17E59C` writes the max term in **years** (3 or 2) here -- harmless, see §0.1; New Loan `0x135F54`; load `0x17E6F8` | `0x17E5FC`, `0x17E63C`; Existing Loans `0x135C30` (`/12`); save `0x17E6B0` |
| +0x0C | rate % | init only | `0x17E5F8`; New Loan draw; Existing Loans `0x135C68` |
| +0x10 | months remaining | init `0x17E5B0`; New Loan `0x135F4C`; **month end `0x100AB0` (−1)**; load `0x17E700` | Existing Loans `0x135CA8`; save `0x17E6B8` |
| +0x14 | outstanding $ | `0x17E658`; init `0x17E5BC`; **month end `0x100AA8` (−pay)**; load `0x17E6F0` | month end `0x100A84`; `0x100EB4`; Existing Loans `0x135CC8`; save `0x17E6A8` |
| +0x18 | monthly repayment $ | `0x17E654`; init `0x17E5B4` (0, then recomputed); load `0x17E6E8` | month end `0x100A88`; New Loan draw; Existing Loans `0x135C88`; save `0x17E6A0` |
| +0x1C | max loan $ | init only | New Loan |
| +0x20 | max term, years | init only | New Loan |
| +0x24 | total repayable $ | `0x17E65C`; init `0x17E5AC`; load `0x17E70C` | New Loan draw; save `0x17E6C0` |
| +0x28 | **1 available / 0 taken** | park ctor `0x1004B0` (=1); accept `0x100CE0` (=0); load `0x17E6E0` (= saved byte != 0) | month end `0x100A78`; `0x100EA8`; menu `0x134150`; list fill `0x1347D8`; accept `0x100CC0`; New Loan; Existing Loans `0x135A98`; save `0x17E698` (low byte) |

**Nothing else writes `+0x28`.** INFERRED from: the whole-program dump's census of every function
that walks a `0x2C` stride (§4), the offset census, and xref of the accept (one caller, `0x135F88`).
In particular the month end never sets it back to 1 (§1.3).

### 0.1 The init writes years into the months field (READ, harmless)

`FUN_0017E4F0` (`0x17E4F0`) stores `a0` = 3 (Mr Byrne) or 2 into both `+0x20` (`0x17E550`) and
`+8` (`0x17E59C`), copies `+8` into `+0x10` (`0x17E5B0`) and quotes with it -- so an untouched record
holds a 3- or 2-"month" quote. It never reaches a player: the New Loan updater overwrites `+4`, `+8`,
`+0x10`, `+0x14`, `+0x18`, `+0x24` every frame for the shown lender while it is available
(`0x135F2C..0x135F54`), and the only accept is in the same function, later in the same frame
(`0x135F88`). A port can initialise `+8 = maxTerm*12` and lose nothing.

---------------------------------------------------------------------------------------------------

## 1. The month end's loan handling (Q1)

### 1.1 The walk -- `FUN_00100A18`, MIPS `0x100A18..0x100AD4` (READ)

```
park[0xBC + (period % 144)*4] = park[4]              // 0x100A54..0x100A70  balance ring, BEFORE any debit
park[0x12C0] = 0                                      // 0x100A74
loans = 0
for i in 0..3:                                        // t0 = park+0xC, stride 0x2C (0x100AD4)
    if loan[i].+0x28 != 0: continue                   // 0x100A78 (bnel): available slots are skipped
    pay = min(loan.+0x14, loan.+0x18)                 // 0x100A84..0x100A94  slt+movn: SIGNED min
    if pay != 0:                                      // 0x100A98
        loan.+0x14 -= pay                             // 0x100AA4..0x100AA8  outstanding
        loan.+0x10 -= 1                               // 0x100AAC..0x100AB0  months remaining
    loans += pay*10                                   // 0x100AB4 (R5900 3-op mult) / 0x100AC0
    park[0x12C0] += pay*10                            // 0x100AB8..0x100AC4
```

- **Decrement of `+0x10`**: `0x100AB0`. **Reduction of `+0x14`**: `0x100AA8`. **The repayment
  `+0x18`** is read at `0x100A88` and capped by the outstanding, so the last payment is never more
  than what is owed. All READ.
- No interest accrues at the month end: the only arithmetic on the record is the subtraction above.
  The interest is entirely in the quote (`FUN_0017E5D0`, finance-screens.md §3). READ.
- **First payment**: at the first month end after the accept, whatever day it was taken -- the walk
  has no "taken this month" test. READ (absence in `0x100A78..0x100AD4`).
- **Payoff arithmetic** (INFERRED from READ `0x17E650..0x17E65C` + the walk): the quote sets
  `+0x14 = +0x18 * +8` exactly and New Loan sets `+0x10 = +8`, so after `+8` month ends both
  `+0x14` and `+0x10` reach 0 on the same month end, and the `min` never actually caps. Mr Byrne
  nominal: 36 × $4,800, outstanding `172,800 − 4,800k` and remaining `36 − k` after month end k.

### 1.2 Which accumulators the repayment goes through (READ)

Wages are computed next and **one debit carries both** (`0x100AD8..0x100B18`):

```
W = FUN_001008B8(park)                                // 0x100ADC  staff wages, already x10
park[0x12D0] += W                                     // 0x100AEC..0x100B00  wages only
park[0xE3C + (period%144)*4] = W                      // 0x100B1C (jal delay slot)  wages only
FUN_00100698(park, loans + W)                         // 0x100AF0 / 0x100B18  ONE debit, return value ignored
```

`FUN_00100698` (`0x100698..0x100748`), when it takes money:
`park[4] −= amt` (`0x100700`), **`park[0x12E4] += amt`** (year's Money Out, `0x100708`),
**`park[0x12D4] += amt`** (lifetime Cash Out, `0x10070C`), **spending ring
`park[0xBFC + (period%144)*4] += amt`** (`0x100724..0x100730`).

So a repayment lands in: balance, Cash Out `0x12D4`, Money Out this year `0x12E4`, and the month's
spending-ring slot. **Not** in the wage accumulator `0x12D0` or the wage ring `0xE3C`.

`park+0x12C0` -- "loan repayments at the last month end, ×10" -- is zeroed at `0x1003F4` (dead init),
`0x100510` (ctor) and `0x100A74`, and read only by its own `+=` at `0x100AB8`. **No other reader
exists** (offset scan for `0x12C0` over the whole image: the five hits above and nothing else; the
same scan finds all of them, which is the control). It is also **not saved** (absent from
`FUN_00101618`'s list). INFERRED: a write-only field; a port need not keep it.

Balance-sheet consequences (INFERRED from the READ formulas in finance-screens.md §2):
Cash Out includes every repayment; **Purchases (= Cash Out − Staff Wages, computed in the draw)
therefore includes loan repayments**; the Loans row (Σ outstanding) drops by the same amount.

### 1.3 When the term runs out (READ)

Nothing happens. Once `+0x14 == 0`, `pay = min(0, rep) = 0`, the `beqz` at `0x100A98` skips both
decrements, and `0` is added to the debit. **`+0x28` stays 0**: the walk never writes it, and its
only writers are listed in §0. Consequences (INFERRED from those writers):

- The lender **cannot lend again** for the life of that park object: New Loan keeps showing
  "Loan Taken" for it and the accept refuses silently (`0x100CC4`, `+0x28 == 0` → return 0).
- The record stays in Existing Loans with Remaining "0mnths", Outstanding "$0", and **Repayment
  still showing the old monthly figure** (`+0x18` is never cleared).
- The "Existing Loans" menu row never goes away once any loan is taken.
- The flag returns to 1 only when the park singleton is rebuilt (`FUN_001005D8` lazily allocates
  0x12F4 bytes and runs the ctor `FUN_00100470`, `0x100600..0x10061C`; `FUN_00100638` frees it,
  called once, from the park teardown `FUN_00150E80`), or when a load restores a saved 1. A save made
  after the loan restores 0 (`0x17E6D8..0x17E6E0`).

### 1.4 When the balance cannot cover it (READ)

The debit refuses only when `park+8 == 0 && park[4] < amt` (signed `slt`, `0x1006CC..0x1006DC`),
and then takes **nothing** -- neither the loans nor the wages. The month end ignores the result.

- **In practice it always pays.** `park+8` is set to 1 by the ctor (`0x10049C`) and no other writer
  is known (staff-management.md §6.3's search). That search left the save loader open; it is closed
  here: the load `FUN_00101DD8` restores the balance through `FUN_00100690` and the totals, and never
  writes `park+8`, and the save `FUN_00101618` does not store it. So the repayment comes off and
  **the balance may go negative**.
- If `park+8` were 0 and the debit refused: the record has ALREADY been reduced (`0x100AA8`,
  `0x100AB0`) and `0x12D0`/wage ring already filed before the debit (`0x100B00`, `0x100B1C`).
  INFERRED: that month's repayment and wages would be forgiven, not deferred.
- **Free-build** (`DAT_002A60B8 != 0` or `FUN_00154428() != 0`): the debit returns 1 and takes nothing
  (`0x1006B4`, `0x1006C4`). INFERRED: loans are repaid for free in free-build, and the records still
  count down.
- What a negative balance does next is not loan-specific. The calendar's red-month counter
  `cal+0x18` (`0x16B0CC..0x16B160`) compares **only the balance** (`bgez` at `0x16B0CC`). Outstanding
  debt plays no part in it: the `FUN_00100E88` call at `0x16B164` is dead (result unused, its `/10`
  survives only as divide-by-zero traps at `0x16B174`/`0x16B188`). Counts 1/3/4 raise advisor
  messages 206+121 / 122 / 123, and 123 is the game-over modal per advisor-messages.md:578. Any credit
  that leaves the balance ≥ 0 clears the counter (`FUN_00100750` → `FUN_0016AE90()+0x18 = 0`, and
  `FUN_0016AE90` is the calendar singleton that `FUN_0016B060` is called with: dump line 56561).

### 1.5 Exact order on a month change (READ, `FUN_0016B060` `0x16B060..0x16B1EC`)

1. `0x16B084` `FUN_0016B240`: advance the calendar.
2. If the month changed: `0x16B098` `FUN_0016C120` (strikes).
3. `0x16B0BC` `FUN_00100A18(park)`:
   1. balance ring `+0xBC[p] = balance` (`0x100A70`) -- **before** loans and wages;
   2. `+0x12C0 = 0`;
   3. **loan walk, slots 0..3 in order** (§1.1);
   4. **wages** `W`, `+0x12D0 += W`, wage ring `+0xE3C[p] = W`;
   5. **one debit of `loans + W`** (`0x100B18`);
   6. `FUN_00100750(park, 0)` (`0x100B24`): files nothing, clears the red counter if balance ≥ 0;
   7. park value ring `+0x107C[p]` (`0x100B58`);
   8. if `p != 0 && p % 12 == 0`: `+0x12EC = value`, `+0x12F0 = balance` (`0x100B80..0x100B8C`);
   9. `p += 1`, clear the next slot of the six rings (`0x100BAC..`).
4. Red-month check on the balance (`0x16B0C4..0x16B190`).
5. `0x16B194` `FUN_0016B478` (stats recorder); `cal+0x1C += 1`.
6. If the year changed: `0x16B1BC` `FUN_00100EF8` (year roll).
7. Separately, on a day change with `day % 7 == 0`: `0x16B1E8` `FUN_0016BC70` (goals, which read
   loans through `FUN_00100E40`, §4).

---------------------------------------------------------------------------------------------------

## 2. Existing Loans (Q2) -- page 4: draw `FUN_001359F0`, update `FUN_00135970`

### 2.1 The selection model: the list at `this+0x9C0`

Class `FUN_0015BB58` (base vtable `0x360EC8`), re-vtabled to **`0x35E5C8`** at `0x1342A0`. Every
reference to `this+0x9C0` in the image: ctor `0x1341FC`, placement `FUN_00134058`, fill `0x1347B0`,
update `0x135980`, dtor `0x136AFC` (offset census; it finds the two sites already known, which is the
control). The draw reads its fields directly (`this+0xA0C/0xC2C/0xDBC`).

**Fill -- `FUN_00134760`, `0x134780..0x134804` (READ).** Runs every frame while the MENU is up
(`FUN_00134828` passes the highlighted row, or −1 for a disabled row), and acts only when the
highlighted row **changes** (`this+0x57C != row`, `0x134784`) **to 4**. Then:

```
FUN_0015C5E0(list)                     // 0x1347BC: clear -- count 0, index -1, 100 flags zeroed
for i in 0..3:                         // park = this+0x2F8, park+0xC+i*0x2C
    if loan[i].+0x28 == 0:             // 0x1347D8  -- TAKEN slots only
        FUN_0015C680(list, u16[0x35E640+2i], i)   // 0x1347EC
```

`0x35E640` = {328, 329, 331, 332} = `STR_FINANCE_LOAN_1..4` "Loan 1".."Loan 4" (330 is
`STR_SGRACE_BET`, skipped). The label is by **slot**, not position: if only Mr Howell is taken the
single entry is "Loan 3". `FUN_0015C680` stores the data `i` at `list+0x3FC[count]`; `FUN_0015C550`
then stores the string at `+0xDC[count]`, **enabled = 1** at `+0x26C[count]`, `count++`, sets the
skip-disabled flag `+0xD4`, and **sets the index `+0x4C = 0` on every add**. So the first taken loan
is selected the moment the list fills. READ.

⚠ **The list is never drawn.** Its draw slot (`0x35E5C8+0x14` = `FUN_0015BC40`) is called by nobody
for this instance (census above), so the "Loan N" labels and `FUN_00134058`'s placement are dead
data. The page shows only the selected record (§2.2). READ (by census).

**Stepping -- `FUN_0015C368`, called at `0x135984` (READ).**
- count == 0: Cross plays `0xAF` (error) and nothing else.
- count ≥ 2: pad bit 1 → index −1, bit 2 → index +1, sound `0xD6` each; wrap (`< 0` → `count−1`,
  `≥ count` → 0). With count == 1 the index cannot move.
- Bits 1/2 are **up/down** (parkstats-screens.md:81, hardcoded-screens.md:130, the same pad bits
  every finance list uses). ⚠ The page draws a left/right-style arrows glyph (§2.2), but left/right
  (bits 4/8) do nothing here.
- Skip mode is on (`+0xD4 = 1`), so the step loops while `vt+0x2C` (`FUN_0015DF88`: `return
  +0x26C[idx]`) is 0. Every entry is enabled, so it never skips.
- Cross: sound `0x12F`, returns 1, and **the return value is ignored** (`0x135984`, nothing tests
  `v0`). Cross on this page does nothing but chime.

**Back -- `FUN_00135970`, `0x13598C..0x1359D4` (READ).** Polls the root back flag (`vt+0x3C`, set by
`FUN_00164D00` on Triangle with sound `0x130`). When set it clears it (`vt+0x74(this,0)`), calls
the menu list's `vt+0x34` = **`FUN_001C96D8`, a bare getter (`return +0x25C`) whose result is
discarded**, and writes `menu+0x260 = 0` → the menu shows again. **The menu is NOT rebuilt here**
(New Loan's back does call `FUN_001340C0`, `0x135D98` path), so the highlight stays on row 4 and the
list is not refilled on return. Tutorial event `0x19`, gizmo legend blank (`FUN_00164B70(this,0)`),
both from `FUN_00134828`.

### 2.2 The draw -- `FUN_001359F0`, MIPS `0x1359F0..0x135D94` (READ unless marked)

Constants (image, no writer; `xref.py` finds only loads at `0x135ACC/0x135AC4/0x135D48/0x135D50`
and in four other functions, and the same run finds the control store `0x133EA8 → 0x2B5CE4`):
**X = `[0x35F574]` = 16, Y = `[0x35F578]` = 64** (the same origin the dead panel helper
`FUN_00164E78` reads). z for text = `param_4 + this+0xC` = 0 + 100 = **100**. Font: Large
(`FUN_0020A958(dev,1)`, `0x135A2C`; no push/pop on this page).

⭐⭐ **Every string on this page is drawn CENTRED on its x.** The draw dispatcher calls
`FUN_00138998(ctx)` every frame immediately before the page draw (`0x1349FC`). It does `ctx+0x18 = 1;
FUN_001389C0(ctx, 1)` → `FUN_0020B248(dev, 1)` → `dev+0x10 = 1` (`0x1389A4`, `0x1389CC`,
`0x1389D0`). The other pages then set their `.sce` justify. **`FUN_001359F0` calls no justify
setter**: its callees are font select (writes `dev+0xC` only, `0x20A974..0x20A9B4`), colour (font
record only, `0x20A900..`), the sprite and text calls. And `FUN_0020ACF8` reads `dev+0x10` at
`0x20ADB0` and, when it is 1, does `x -= width >> 1` (`0x20AE38..0x20AE50`). So mode 1 holds for the
whole page.

| element | x (centre) | y | colour | value / text |
|---|---:|---:|---|---|
| header label | X+0x55 = **101** | Y+0x3C = **124** | yellow (255,255,0) `0x35F560` | 102 `STR_FINANCE_LENDER` "Lender" |
| lender name | X+0xCD = **221** | 124 | yellow | `FUN_0017E670(loan)` (plain C string, not localised) |
| arrows glyph | sprite, see below | | amber | |
| row 1 | 101 / 221 | **156** | amber (200,130,0) `0x35F540` | 77 `STR_FINANCE_BORROWED` "Borrowed" / money(`+0x04`) |
| row 2 | 101 / 221 | **188** | amber | 1022 `STR_FINANCE_TERM` "Term" / years(`+0x08 / 12`) |
| row 3 | 101 / 221 | **220** | amber | 387 `STR_FINANCE_INTEREST` "Interest" / percent(`+0x0C`) **→ shows "20", see ⚠** |
| row 4 | 101 / 221 | **252** | amber | 60 `STR_FINANCE_REPAYMENT` "Repayment" / money(`+0x18`) |
| row 5 | 101 / 221 | **284** | amber | 994 `STR_FINANCE_REMAINING` "Remaining" / months(`+0x10`) |
| row 6 | 101 / 221 | **316** | amber | 434 `STR_FINANCE_OUTSTANDING` "Outstanding" / money(`+0x14`) |
| "No Loan Taken" | X+0x82 = **146** | Y+0x54 = **148** | amber | 154 `STR_FINANCE_NO_LOAN_TAKEN` |

- Row step: literal `0x20` = 32 (`0x135BF8`), not the line advance. Labels come from `u16[0x35E670]`
  = {102, 77, 1022, 387, 60, 994, 434} (`0x135BD4`, `0x135CE4`). Values come from a jump table at
  `0x35E510` = {`0x135C24`, `0x135C30`, `0x135C64`, `0x135C84`, `0x135CA4`, `0x135CC4`}, all formatted
  into the shared buffer `0x3ADF08`.
- Colours: amber at entry (`0x135A48`), yellow for the header pair (`0x135ADC`), amber for the rows
  (`0x135BEC`).

**Answers to the unit questions (READ):**
- **Borrowed** = money, whole dollars (`FUN_00142908`: `-`, `$`, thousands groups) -- e.g. "$100,000".
- **Term** = **years**: `+8` (months) **/ 12**, signed (`0x135C44`), through `FUN_00142A50` → digits
  + 624 "yr" if 1 else 794 "yrs", no space -- e.g. "3yrs".
- **Interest** = the **rate %** (`+0xC`), not a money amount.
- **Repayment** = money per month (`+0x18`).
- **Remaining** = **months** (`+0x10`) through `FUN_00142AC0` → digits + 10 "mnth" if 1 else 349
  "mnths" -- e.g. "36mnths", "0mnths" after payoff.
- **Outstanding** = money still owed, whole dollars (`+0x14`), including the interest not yet repaid.

⚠⚠ **The Interest value loses its `%` on this page.** The value column is drawn by `FUN_00138630`,
which passes the formatted value as the **format string** of `vsprintf` (`0x13866C`: `a1 = t0`;
`0x138688 jal 0x2A1248`). The label column uses `FUN_00138580`, which formats the text-table string
instead. `FUN_00142A10` builds "20%" (digits, then `%` and NUL, `0x142A34..0x142A3C`). In the libc's
`vfprintf` (`0x29FA28`), the character after `%` is NUL; `ch − 0x20` is out of the switch's range
(`0x29FC78..0x29FC80`), and the default case is `beqz ch, done` (`0x2A035C`). So the trailing `%`
prints nothing and the row reads **"20"** ("15" for Ms West). INFERRED as the on-screen result; every
step is READ. **New Loan is not affected**: it draws its values with `FUN_00138798` directly
(`0x136018` body), so its Interest reads "20%". Money, year and month strings contain no `%` and pass
through unchanged.

**Arrows glyph (READ; not in finance-screens.md).** A stack sprite of the class New Loan uses
(`FUN_00143B18`, vtable `0x35F6F8`; its draw slot `+0x2C` = `FUN_00143BC0`), with **`+0x30 = 0`**
(`FUN_00143BB8(sprite, 0)`, `0x135B04`; New Loan leaves it at 1):
- position `(X+0x10F, Y+0x3C + h/2)` = (287, 139), `z = param_4` = **0** (`0x135B18..0x135B2C`), size
  `h × h` with `h = FUN_0020AA08` = the Large line advance, 30 (bff-font.md).
- colour set to yellow (`0x135B68`) and then **overwritten with amber** (`0x135B90`). The second call
  wins, since `FUN_00212798` just stores `+0x1C/+0x20/+0x24`.
- `FUN_00143BC0` with `+0x30 == 0` blits UI texture `#0x3C` at **top-left `(x − w/2, y − h/2)` =
  (272, 124)**, 30×30, and calls `FUN_002136D0(tex, 0)`. That call clears bits `0x1030` and sets
  `0x1020` in the texture's flag word; mode 1 would set `0x30`, mode 2 `0x1010`. With `+0x30 == 1`
  (New Loan) the blit is at `(x + w/2, y − h/2)` and `FUN_002136D0` is not called. What those bits
  do (a mirror is the obvious guess, and **is not read**) is undetermined, and so is the state New
  Loan inherits after this page has set them on the shared texture object.

### 2.3 "No Loan Taken" -- condition (READ) and reachability (INFERRED)

`0x135A50..0x135AA0`:
```
idx = list+0x4C
sel = (list+0x26C[idx] != 0) ? idx : -1          // movn at 0x135A64; idx = -1 also yields -1
loan = sel < 0 ? 0 : this+0x9B0[ list+0x3FC[sel] ]   // 0x135A70..0x135A80
if loan == 0 || loan.+0x28 != 0 → "No Loan Taken" only
```
`this+0x9B0..0x9BC` are four record pointers. The ctor zeroes them and the count `+0x9AC`
(`0x1343F4` and the loop before it). The modal loop `FUN_001C78D0` appends `park+0xC`, `+0x38`,
`+0x64`, `+0x90` inline right after construction (`0x1C792C..0x1C7990`); `0x136D20` is the
out-of-line form of the same append. So `this+0x9B0[i] = &loan[i]`.

INFERRED **unreachable in normal play**. Row 4 exists only if some slot is taken (`0x134150`), the
list is filled from the same test (`0x1347D8`) whenever the highlight arrives on row 4, every add
sets the index to 0, and nothing can clear `+0x28` back to 1 while the laptop is open. So on page 4
`count ≥ 1` and the selected record is always taken. It is a defensive branch; port it anyway.

---------------------------------------------------------------------------------------------------

## 3. The finance menu's row 4 rule (Q3) -- `FUN_001340C0`, `0x13412C..0x134168` (READ)

```
for i in 0..3:                          // v1 = park+0x34 = park+0xC+0x28, stride 0x2C (0x134158)
    if *(park+0x34+i*0x2C) == 0:        // 0x134150..0x134154
        append(list, u16[0x35E638] = 472 "Existing Loans", enabled=1)   // 0x13415C..0x134164
        return
```
"Some slot has `+0x28 == 0`", exactly as finance-screens.md states. `FUN_001340C0` runs at
construction (`FUN_00134188`) and on New Loan's back. It does **not** run on Existing Loans' back
(§2.1). The balance sheet's back was not re-read here. Since `+0x28` never returns to 1 within a
park (§1.3), the row appears at the first accept and stays.

---------------------------------------------------------------------------------------------------

## 4. Everything else that reads the loan records (Q4)

Census method: every function in the whole-program dump that walks a `0x2C` stride or indexes
`* 0x2C`, plus xrefs of the record helpers (`0x17E4F0`, `0x17E5D0`, `0x17E670`, `0x17E480`,
`0x100CA8`, `0x100E88`, `0x100E40`, `0x17E690`, `0x17E6D0`). Unrelated `0x2C`-stride walkers (the
resource pool `0x2AAE08` at `0x10F620..0x110260`, `0x106080`, `0x150E80`; the class at `0x17E430`)
were opened and excluded.

- **Balance sheet "Loans" row**: `FUN_00134B98` → `FUN_00100E88` (`0x134C54`) = Σ `+0x14 × 10` over
  taken slots (`madd` loop `0x100EA8..0x100EC4`), shown `/10` → whole dollars outstanding.
- **Gold-ticket money goal**: `FUN_0016BC70` (weekly, `0x16B1E8`) → `FUN_00100E40` (`0x16BD04`) =
  Cash In `0x12D8` − Σ outstanding×10 − Cash Out `0x12D4` (`0x100E50..0x100E70`), tested
  `> goal+0x10 × 10` → message `0x9E` (advisor-messages.md:579). INFERRED arithmetic: accepting
  credits `A×10` to Cash In and adds `T×10` of debt, so the figure drops by exactly the
  **total interest × 10** at once. Each repayment then moves Cash Out and the debt equally, so it
  never changes again.
- **Calendar** `0x16B164`: `FUN_00100E88` called and its result discarded (dead; §1.4).
- **Save** `FUN_00101618` (caller `FUN_001C2930`) → `FUN_0017E690` per slot into
  `buf+0x280 + i*0x1C`: `[0] +4, [1] +8, [2] +0x10, [3] +0x14, [4] +0x18, [5] +0x24, byte[0x18] +0x28`
  (`0x17E690..0x17E6C8`). Lender index, rate, max loan and max term are **not** saved; the ctor's
  constants stand.
- **Load** `FUN_00101DD8` (caller `FUN_001608A8`, chunk `FUN_0015FD48(0x328,1)`) → `FUN_0017E6D0`,
  the exact inverse; `+0x28 = (byte != 0)` (`0x17E6DC`).
- **Finance menu** row 4 (`FUN_001340C0`), **Existing Loans** (`FUN_00134760`, `FUN_001359F0`),
  **New Loan** (`FUN_00136018`, `FUN_00135D98`), **accept** (`FUN_00100CA8`) -- covered above and in
  finance-screens.md.
- **Indirect, through the accept's credit** (INFERRED from READ `0x100CFC` → `FUN_00100750`): the
  principal is ordinary income. It lands in Cash In `0x12D8`, the year's Money In `0x12DC` and the
  income ring `0x2FC`, so Finance Statistics' Money In series and the advisor's WAGES_HIGH test
  (income(1), income(2) from that ring, `ParkFinances.WagesHigh`) both see it.
- **Advisor rules, awards/medals**: no reader. `FUN_00100E88` has 3 callers and `FUN_00100E40` has 1
  (above), no advisor variable producer walks the records, and advisor-rules.md / awards.md name no
  loan variable. INFERRED negative.
- **Dead**: `FUN_00100290` (an alternative park init that zeroes the totals and initialises the
  records through `FUN_0017E498` with a rotating global lender index `DAT_002C33A8`). It has no `jal`
  caller, no aligned pointer in the image and no address materialisation (`xref.py`; control
  `0x1359F0` finds its caller).

---------------------------------------------------------------------------------------------------

## 5. Corrections to existing documents and to the port

**finance-screens.md**
1. "`FUN_00135970` ... call `FUN_001340C0` on back" -- **not for Existing Loans**: its back calls
   only the getter `FUN_001C96D8` and clears `menu+0x260` (`0x1359A8..0x1359D4`). The menu is not
   rebuilt and the highlight stays on row 4.
2. "Entering row 4 also pre-fills the existing-loans list" -- it fills when the **highlight moves
   onto** row 4 (`FUN_00134760` runs every menu frame and compares `this+0x57C`), not on accept.
3. Existing Loans positions (101,124), (221,124), rows `124 + 32i`, "No Loan Taken" (146,148) are
   **right as anchors**, but every string is **centred** on them (§2.2), not drawn from the pen.
4. Add: the arrows glyph at top-left (272,124), 30×30, amber, z 0, `+0x30 = 0`; the yellow header
   pair; and the Interest row reading "20", not "20%".

**`ParkFinances.cs`**
5. `MonthEnd` files `_balance[slot]` **after** the debit. The console files `+0xBC[p]` **before**
   loans and wages (`0x100A70`), so the Bank Balance series is the balance as the month closed
   before its bills. ⚠ The port's series is a month's wages+repayments lower.
6. `MonthEnd`'s loan line (`loans = 0`) is right in shape. The walk to port is §1.1, including the
   `pay != 0` guard on the decrements and the signed `min`.
7. ⭐ Outside the brief but load-bearing for the doc comment: the park ctor **does** write the balance.
   `0x1004C0..0x1004CC` is `FUN_00100690(park, 0x186A0)`, and `FUN_00100690` is the setter
   `park[4] = a1`. So a fresh park object starts at **100,000 tenths = $10,000**. The comment's "the
   park constructor never writes `park[4]`" is contradicted. Whether a scenario or save then
   overrides it (the load does, from `buf+0x2F0`) is not traced here, so this does not by itself
   refute master's $30,000.
8. Also outside the brief: the save stores every money total `/10` and the load multiplies by 10
   (`FUN_00101618` / `FUN_00101DD8`), so a save/load round trip drops the tenths of every
   accumulator. The balance itself is saved raw.

**Lenders.cs** -- consistent with this file. A port of the take should credit through
`ParkFinances.Credit(amount*10)` with no category (that is what `0x100CFC` does), and a port of the
month end needs §1.1 against the four records.

---------------------------------------------------------------------------------------------------

## 6. Not determined, and what would settle it

1. **The arrows texture flags `0x1020/0x30/0x1010`** (`FUN_002136D0`): what they do to texture
   `#0x3C`, and whether New Loan's arrows change after Existing Loans has been shown. Settle by
   reading the consumer of `*(tex+0x1C)` in `FUN_00213980`/the GS packet build, or with a PCSX2
   capture of New Loan before and after visiting Existing Loans.
2. **"20" vs "20%"** is READ end to end, but no capture confirms it. One PCSX2 screenshot of Existing
   Loans with any loan taken settles it, and the centring with it.
3. **Exact repayment figures** (fixed-point `pow`, finance-screens.md §6.1): unchanged by this work.
4. **`park+8`'s other writers**: none known (staff-management.md §6.3 plus the save/load checked
   here). A scenario loader that sets it would make the "refused debit forgives the month" path live.
5. **Who seeds the balance after the ctor's $10,000** (scenario/save load order on park entry):
   not traced.

---------------------------------------------------------------------------------------------------

## 7. Port checklist

- **Take** (`FUN_00100CA8`): if available → `available = false`, `Credit(amount*10)` (income: Cash
  In, year Money In, income ring). Record: `months = remaining = years*12`, `total = outstanding =
  repayment*months`.
- **Month end**, before wages and inside the same debit: for each taken record,
  `pay = min(outstanding, repayment)`; if `pay != 0` { `outstanding -= pay; remaining -= 1` };
  `loans += pay*10`. Then `Debit(loans + wages)` once, ignoring the result. File the balance ring
  **before** this.
- **Never** set `available` back on payoff. Save/load must carry `available`, amount, months,
  remaining, outstanding, repayment and total.
- **Existing Loans**: up/down cycles the taken records (slot order, wrap, sound `0xD6`, only when
  ≥ 2); Cross only chimes `0x12F`; Triangle returns to the menu without rebuilding it. Draw centred:
  "Lender"+name yellow at (101,124)/(221,124); six amber rows at y 156..316 step 32, labels centred
  at 101, values at 221; Interest **without** `%`; arrows glyph `#0x3C` 30×30 top-left (272,124)
  amber, z 0.
- **Menu**: append "Existing Loans" iff any record is taken; it never disappears.
- **Balance sheet**: Loans = Σ outstanding (dollars); repayments are inside Cash Out and so inside
  Purchases.
- **Gold-ticket money goal**: `CashIn − 10·Σoutstanding − CashOut > goal·10`.
