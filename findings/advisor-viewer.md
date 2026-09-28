# The advisor, seen and heard: step B in the viewer

Built 2026-09-28 against the PAL `SLES_500.32` and the owner's disc, on top of step A's core
(`ParkAdvisor`, `AdvisorScheduler`, `AdvisorMessageStack`). The research is
[advisor-messages.md](advisor-messages.md) and [advisor.md](advisor.md). This note records what this step
READ, MEASURED and INFERRED on the way. Addresses are ELF virtual addresses.

Code: `game/AdvisorHead.cs` (the head), `game/AdvisorStackView.cs` (the stack), `game/GameAudioMix.cs` (the
ducking), `game/Viewer.Advisor.cs` (the wiring), `core/TPW.PS2.Data/ParkAdvisor.cs` (lips, mouth, costume
call, `AdvisorHeadChannel`, `ObjectLeftPark`). Checks: `tools/TPW.PS2.ParkSimAudit/AdvisorChecks.cs`
(`--advisor-only`), `game/tests/AdvisorSmoke.cs` (the viewer matrix's `advisor` scene),
`tools/advisor_teeth.py` and `tools/advisor_view_teeth.py`.

## READ in this step

**The HUD sprite ids** (research "Still unknown"). `0x216028` builds the UI sprite table at `0x2EEFF0`:
entries of 0x18 bytes, name pointer at `+0`, id at `+0x10`. The table is built by straight-line stores,
which this step evaluated register by register:

| id | file (UI.WAD) | used by |
|---|---|---|
| `0x2A` | `messages\letterclosed.ssh` | the envelope; an unselected record (`0x108D40`, `0x108E98`) |
| `0x2B` | `messages\letteropen.ssh` | the selected record |
| `0x2C` | `messages\tutorial.ssh` | a tutorial record (type 3) |
| `0x2F` | `messages\messcorner.ssh` | a record's frame corners (`0x108AB0`) |
| `0x30` | `messages\messfill.ssh` | a record's bar |
| `0x32` | `messages\messedge.ssh` | a record's frame edges |

The same table gives `0x2D` `gticket\gticket.ssh`, `0x2E` `ultimatec\star.ssh` (the HUD's counters, as
[awards.md](awards.md) already had them) and `0x31` `messages\wboxfill.ssh`. The research named the table
`0x2EEFF4`.

**The stack's draw constants.** `0x108D40`: the count `"%d"` (`0x358C00`) at (`0x50`, `0x1D6 − [0x2AA738]`) =
(80, 430), font 1, white, drop shadow 2,2 (`0x20B258`). The envelope (`0x2A`) is 40×40 at (32, 420).
`0x108E98`: sprite at layer 0, then `0x108AB0` at layer 4. `0x108AB0`: `0x30` at (0, y), (x+40)×40; `0x2F`
8×8 at (x+40, y−8) and flipped at (x+40, y+40); `0x32` (x+40)×8 at (0, y−8) and flipped at (0, y+40), and
turned, 8×40, at (x+40, y).

**The head's transform** (`0x1066B0`): the floats at `0x2AA714`/`0x2AA718`/`0x2AA71C` are 0.6, −0.5, 0.013.
**The model's axes** (bind pose, measured through the port's reader): world y is up (every hat at +y, the
body at −y), x is across, and the face (eyes, mouth) is at z ≈ −0.2, in front of the head disc at z ≈ 0. It
looks down −z, the side the constructor's look-at camera stands on (eye (0, 0, −256), `adv+0x118`).
**The root node** `Position Dummy` carries a bind scale of 0.02 and turns local z into world y.

**The costume** (`0x107160`, `0x106FA8`, `0x1070B8`, `0x106E30`, `0x106EF0`). The reset hides fittings 14,
12, 1, 2, 3, 16, 17, 18, 4..11, 13 and shows 21, 19, 20. Each selector then shows its hat, and every hat but
the Kiosk's hides the plain antennae 19/20. Selector 6 (Axe) shows the axe antennae 16/17. Selector 9 (Kiosk)
also hides the right hand (21) and shows the Spatula (14). Fittings are looked up in space 0x400
(`0x1F1F78`), and the node is the fitting's index + `u16 @0x34` (11 on `advisor.mps`). Show clears `0x10`
and sets `0x80000000`; hide sets `0x80000010`. The protection bit keeps a fitting through the next record's
visibility transition, and the talk records do track the right hand.

**The mouth** (`0x105FC8`): bit `0x8000` of the mesh record's flags word, cleared on the chosen mesh and set
on the other four. A consequence: its only callers are `0x106600` and state 3, and `advisor.mps` ships all
five mouth meshes without the bit. So until the first call, the first voice draws all five mouths overlaid.
The port keeps that.

**Channel 0** (`0x1ABC80`, `0x1AC048`, MIPS `0x1ABD04..0x1ABE88`, `0x1AC180..0x1AC2EC`):
- A request QUEUES in the channel's one slot (`+0x24`; section 12 = empty) when the current record is valid
  and unfinished (frame ≤ length), not start/end-held (`flags & 6`), and the request has no flag 2.
- A flag-2 request empties the slot and starts at once.
- A record ends on the first update STRICTLY past its length (`c.olt.s` at `0x1AC188`). The queued record
  then starts, and it beats the loop.

So a normal exit (record 14, flags 0) waits out the talk record's pass. The talk record was chosen to cover
the speech (§4.3). A cut exit (the skip, an interrupt) starts at once. The 30-frame enter is 26 passes of
40 ms, not 25.

**The registry for world 4** (`0x17B324..0x17B334`): the world folder is read at `[0x2BF2A0 + world·4]` with
no bound. For world 4 that word is `0x2BF2B0`, the shared "Data". So category 13 (flags 0x80), entry 287
`Advisor`, resolves to `DATA.WAD/Generic/Advisor/Advisor.mps`. The port's reader used to refuse it.

**An object leaving the park.** In `0x14A7B0(obj, 1)` the object's `vt+0x10C` runs before `0x13D8C0`.
`0x1E0E70` stores vtable `0x369AA0` at the object's `+0x10`. The word at `0x369BAC`, which is `+0x10C` of that
vtable, is `0x1E12E0`, and so is the same slot of `0x368080`. `0x1E12E0` calls `0x1088E0`. So the ride's own
record removal comes first. `0x13D8C0` then marks every type-2 record, which overwrites the ride's single
pending mark: with a later type-2 record about another ride, that one goes and the deleted ride's first
record stays. Nothing clears a queued message's object. Of the two callers, `0x123FF8` passes 1 and
`0x126898` passes 0.

## MEASURED

**The box's face.** The widget's font is not traced. Over the 95 advisor text rows with authored line breaks
(`eng`, `eur`), in the 228 px between the box's margins:

| face | widest authored line | rows too wide | most lines |
|---|---|---|---|
| `Console.bff` | 217 px | 0 | 7 × 14 = 98 of 152 px |
| `Small.bff` | 288 px | 54 | 7 × 21 = 147 |
| `Large.bff` | 387 px | 83 | 7 × 30 = 210 (5 too tall) |

So the port draws the box's text in `Console.bff`. The count beside the envelope is font 1, `Large.bff`
(READ).

**The speech length.** The state machine is fed the decoded stream's length. For 171 (`Adds_14.mp2`) that is
23,301 ms, against the SDT header's 23,185 ms.

## INFERRED, and said where it lives

- **The projection** is not traced. The overlay takes (0.6, −0.5) in the frame the HUD glyph blit uses
  (`0x2137C0`: x/256 − 1, 1 − y/256 over the 512-square space), which puts the head at console (410, 384),
  lower right. The 0.013 REPLACES the root's bind scale of 0.02. Multiplied instead, the head would be 0.3% of
  the screen wide. Replaced, the enter animation's 52-unit rise starts the bug exactly below the bottom edge,
  and a render 80 ms into the rise shows only the hat's crown above it. The 4/3 is on the vertical. Wider
  than 4:3, both axes take the vertical factor, the HUD's own rule for its glyphs.
- **Hidden when idle.** The console never unregisters the model. After an exit it is below the frame, and
  its bind pose before the first message would stand up wearing every prop.
- **The speech is not ducked** (which audio group plays it is unknown). The port's sounds go on a new SFX bus
  whose gain is `live / setting`, the console's group-3 drop as a ratio, because the port never applied the
  settings' absolute level.
- **Pause** pauses the voice with the park; no stream pause was found natively.
- **The layer order** (a record's frame behind its letter), **no border on the box**, **white text**, the
  **jump target** (the ride's footprint centre; `vt+0xCC` of a ride is not traced) and **refusing a ride that
  has gone**.

## Checks

- `ParkSimAudit --advisor-only`: 77 checks (step A had 66). New: the head's registry path, the cut exit, the
  lips from `ENGLISH/sp_001.lip` (the gate flips on ticks 56, 71, 102), the mouth's four rules, the no-track
  flap, the costume call, and four removal cases through `ParkSim.Remove`.
  `tools/audit_matrix.py` floors it at 74.
- `tools/advisor_teeth.py`: step A's 53 mutations plus 14 new ones, all red (see the run below).
- `AdvisorSmoke`: 49 checks per park, each reading the view's output (drawn meshes, the voice player, the bus
  gains, the stack view's drawn count and text, the camera). Viewer matrix scene `advisor`, floor 47.
- `tools/advisor_view_teeth.py`: 20 mutations of the view, each required to turn the smoke red.

## Still open

The projection and render layer 4; the audio group of the speech; whether the stream pauses; the widget's
font, colour and border; `vt+0xCC` of a ride; the tutorial (Replay stays inert); the game-over flow after 123;
the goal notices (no goals record in the port).
