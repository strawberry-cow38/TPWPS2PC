# The baked-vertex walk: track flag 0x40000, player 0x1a7e18

September 27, 2026, tinyclaw, branch `tinyclaw/baked-walk`. Every character's section 0 is its walk
(logical 13, native-guest-animation-readiness.md "Section 0 is a walk"). It is stored as whole poses,
one per frame, not as bones. Handyman, FatMechanic, Researcher and the eight entertainer costumes have
no other walk: of the staff, only Guard has a section 1. The port can now play it
(`Animation.BakedVertexTracks`, `AnimatedModel`). The Viewer does not use it yet.

READ means read off the owner's PAL executable at the address given. INFERRED means read off the
data, with the count it held on.

## Who calls the player (READ)

- The model updater 0x1acfc0 calls the record player 0x1a8da8 twice: at 0x1ad29c for the running
  frame, and at 0x1ad278 for a held pose (below).
- 0x1a8da8 branches on record flag 0x20. When it is clear, it walks the 48-byte tracks
  (0x1a9414..0x1a94a0). For each track, node = u16 track+0:
  - if node < model+0x30 (mesh count), the object is the mesh at model+0x48 + node×0xA0;
  - otherwise it is a helper at model+0x4C + (node − count)×0x60.
- If `track+4 & 0x40000` (0x1a942c, 0x1a9464), it calls **0x1a7e18(object, \*(track+0x20))** at 0x1a9474.
  `$f12` = playback+0xC is loaded in the delay slot.
- Otherwise it calls the morph player 0x1a6d68 with its additive argument 0 (0x1a9498).

⚠ **The ride evaluator 0x1a7f48 never calls 0x1a7e18.** The only reference is 0x1a9474. 0x1a7f48
sends flag 0x41000 to the morph player (0x1a807c). A note that the player is "called from
0x1a7f48" is wrong.

The loader's 0x1674b0 relocates track+0x20 through 0x1676d8 when 0x40000 is set. That function
relocates exactly ONE word, header+0 (0x1676d8..0x1676e4). So the rest of the header holds values,
not pointers. This is why `Morph()` and `AllPointers()` must not read a baked header as a morph
header.

## The header and table

| where | type | meaning | status |
|---|---|---|---|
| header+0 | ptr | the frame table | READ 0x1a7e44; always header+8 on the disc |
| header+4 | u16 | triples stored | **not read by the player**. Equals groups × (duration + 1) on 81 of 81 tracks (INFERRED) |
| header+6 | u16 | groups | READ 0x1a7e3c |
| table | int16 x, y, z | frame f, group g at (f×groups + g)×6 | READ 0x1a7e3c..0x1a7e54 |

The table runs to the next pointer in the file, give or take 2 bytes of padding, on every track.

## The player, 0x1a7e18 (READ)

```
run = mesh+0x98                               ; 0x1a7e30
f   = __fixsfsi(playback+0xC)                 ; 0x1a7e34 -> 0x297b68: TRUNCATES
p   = *(header+0) + f*groups*6                ; 0x1a7e3c..0x1a7e54
repeat groups times (header+6):
    x, y, z = (float) int16 p[0..2]; p += 6    ; cvt.s.w, no scale, 0x1a7e70..0x1a7eb0
    do  e = *run++                            ; 0x1a7ec0
        v = [mesh+0x68]+0xC + (e & 0xfffc)    ; the mesh's vertex words
        v.z = z; v.x = x, keeping bit 0 of v.x; v.y = y, keeping bit 0 of v.y   ; 0x1a7ee0..0x1a7f20
    while (e & 2)
```

- **No interpolation.** The frame is truncated, and a pose is stored for every frame.
- **The value replaces the vertex.** There is no additive argument, so the model header's
  +0x1c bit 4 does not apply to this channel.
- The run list is mesh+0x98, the one the morph player and the skin write through
  (`Model.AnimVertexMap`). One run per group, and bit 1 means "the next entry continues this group".
- Bit 0 of the X and Y words are the strip's ADC and facing flags (formats.md). The port reads
  those from the file, not from positions.
- **Nothing is bounds-checked.** The frame index relies entirely on the clock.

## The frame the clock hands it (READ, 0x1acfc0)

- **Running:** 0x1ad04c..0x1ad06c keeps playing while frame < duration (+0x20). So the running call
  sees f = 0 .. duration − 1.
- **Held pose:** the dispatcher can return nothing playable (0x1ad0ac → 0x1ad254). If the apply flag
  is set and the slot is not F, the updater:
  1. stores the duration into +0xC (0x1ad268..0x1ad274);
  2. poses once (0x1ad278);
  3. sets the slot to F.

  That call sees **f = duration**.

So the table needs duration + 1 frames, and every one has exactly that. The extra frame closes the
loop:

- it equals frame 0 on 79 of 81 tracks;
- it is within 1.0 unit on Franky's body;
- it is within 4.7 units on Handyman's body.

## Measured on the disc

24 files and 81 tracks. Every `/Chars` .aps has exactly one section-0 record, with flags 0x01, and
every track of it carries exactly flag 0x40000.

| models | duration | tracks |
|---|---:|---:|
| Boy1a–4a, Girl1a–4a | 16 | 3 (head, body, legs) |
| FantasyKid, HallowKid, JungleKid, SpaceKid | 16 | 2 |
| Handyman, Researcher | 32 | 3 |
| Guard, Alien | 32 | 2 |
| FatMechanic, Dino, Flower, Franky, Gnome, Hunter, Vampire | 32 | 5 |
| Spaceman | 64 | 4 |

- **Every mesh is tracked.** No model has a mesh that its section 0 leaves alone (24 of 24).
  Boy2a–4a carry their OWN section-0 tracks, unlike their skeletal records, which are Boy1a's
  (`Shared`).
- **groups = mesh+0x98 run-list groups** on 81 of 81.
- **The map and the space are right.** Frame 0, fanned out through the run list, lands on the mesh's
  own authored vertices:
  - within 1.4–1.6 units (int16 rounding) on every mesh of Handyman, Boy1a, Girl1a and Girl3a;
  - within 5.9 on Girl2a.

  A vertex-order map misses Handyman by 35,391 units.
- **Where the authored vertices are a different pose, the walk is still in the same space.** This
  covers FatMechanic, Researcher, Guard and the costumes; Dino's authored body sits 48,000 units
  off. On each body mesh, compared against the port's own skinned pose of section 3, frame 0, in the
  same file:
  - centroids are within about 500–3,900 units;
  - the best-frame median vertex error is 1,500–4,900 units;
  - the bodies are 18,000–37,000 units tall.

  Boy2a–4a have no skeletal record of their own to compare against.
- **Props that sit out the walk are collapsed.** 19 tracks never move, and every one of them
  collapses its whole mesh to a single point:
  - the Dino's rocks;
  - the Flower's bugs;
  - Franky's eyeballs;
  - the Gnome's pots;
  - the Hunter's guns 2 and 3;
  - FatMechanic's `toolboxfree`;
  - Spaceman's `raygunHAND`;
  - the Vampire's bats.

  All except the bats are also on the record's +0x18 hide list, which `AnimationNodeVisibility`
  applies on activation. The bats are drawn by the port but have zero area, so nothing shows.
- **The legs swing in anti-phase.** Measured on the lowest shown mesh: its lowest third by height
  (Y is up), split left and right at the median X, taking the mean forward Z per frame over one loop.

  | model | L/R correlation | swing L / R (units) | frames |
  |---|---:|---|---:|
  | Handyman | −0.985 | 7808 / 6554 | 32 |
  | FatMechanic | −0.903 | 4607 / 7724 | 32 |
  | Boy1a | −0.928 | 4889 / 6599 | 16 |
  | JungleKid | −0.943 | 4745 / 6047 | 16 |

  The earlier −0.96 and −0.97 for the two kids came from a different split, with the same sign and
  size. On Dino (−0.61) and Spaceman (−0.74) this split takes in more than legs. Those two are not
  asserted, and they are not evidence against a walk.
- ⚠ **A static pose does not give NaN.** The left and right means are constant only up to double
  rounding, and the Pearson correlation of that residue read −1.000 on Researcher, Guard and the
  drawn FatMechanic, and +1.000 on Handyman. That would be a pass for "anti-phase" on a figure that
  does not move. Every leg check therefore requires swing first.

## The port

- **`Animation.BakedVertexTracks(record)`** returns `BakedVertexTrack { Node, Groups, Frames, Xyz }`
  with `FrameIndex(now)`, `Position(frame, group)` and `Sample(now)`.
  - It returns an empty list for a skeletal record.
  - `FrameIndex` truncates like `__fixsfsi`. It refuses (`ArgumentOutOfRangeException`) a frame
    outside `[0, Frames)`: the console cannot reach one, so asking for one is a clock bug.
  - frame == duration is legal: it is the held pose.
- **`AnimatedModel`** gives each part its track:
  - `UseRecord(s0)` binds the tracks. A run list that disagrees with the track's groups throws
    `InvalidDataException` rather than half-drawing.
  - `SetFrame(now)` fans the truncated frame's pose out through `AnimMap`. The result replaces the
    vertex and is never additive.
  - `BakedParts` counts the parts driven by a baked track.
  - `TPW_PS2_BAKED=off` restores the old behaviour (bind pose), for A/B runs.
- `Animation.AllPointers()` no longer reads a baked header's +0x08, +0x24 and +0x28 as morph
  pointers. `Length()` counts a baked table's last frame.

```csharp
var s0 = aps.Records().Single(r => r.Slot == 0);   // one record, flags 0x01, not Shared
drawn.UseRecord(s0);                               // also applies s0's hide list (placard, props)
drawn.SetFrame(frame);                             // 0 <= frame <= s0.DurationFrames, truncated
```

## Not done here

- **Guests are still DRAWN with section 1** when the dispatcher asks for section 0
  (`Viewer.NativeAnimation.cs`, `DrawableRecord`), and staff are not wired into the Viewer.
- **`AnimatedModel.ActivateNativeRecord`** (the bus) still refuses records with a hide list. Every
  staff and costume section 0 has one.
- **What a skinned mesh shows on the first frame after switching** from a skeletal record to this
  one is not modelled. The console leaves the last writer's vertices in the buffer until 0x1a7e18
  runs, while the port rebuilds at frame 0 on `UseRecord`. This is invisible at the updater's
  cadence.

## Checks

- **`ParkSimAudit --baked-walk-only`: 23 checks.** These are:
  - the census above;
  - the Handyman map closure, against a vertex-order map;
  - the legs, each against a static control that must fail;
  - continuity, against a shuffled frame order that must fail;
  - truncation, against an interpolating sampler that must differ.

  Two parser mutations were run by hand and reverted:
  - a frame stride one group short gives **9 failures**;
  - rounding instead of truncating gives **1 failure**.
- **`game/tests/BakedWalkFilm.tscn`: 78 checks.** It runs on the DRAWN positions:
  - every drawn vertex equals the parsed group, for all 32 frames of both models;
  - the harness repeats the 3.999 truncation check on the drawn vertices;
  - the legs, measured per vertex: Handyman −0.988 at 7775 / 5901 units, FatMechanic −0.894 at
    4233 / 8078 units;
  - the `TPW_PS2_BAKED=off` control must fail. It swings 0 / 0, with correlation residue +1.000
    and −1.000.

  It writes side and rear three-quarter strips for each leg. Run it with:

  ```sh
  cd game && dotnet build && cd ..
  TPW_PS2_DISC="$DISC" TPW_BAKED_FILM=/tmp/baked LIBGL_ALWAYS_SOFTWARE=1 xvfb-run -a -s '-screen 0 1280x720x24' \
    "$GODOT" --path game --rendering-method gl_compatibility --audio-driver Dummy --resolution 240x320 \
    res://tests/BakedWalkFilm.tscn
  ```

  In the strips, Handyman alternates his feet with the broom held out in front. FatMechanic's
  shoulders twist against his hips, and his toolbox swings with his arm. The rear view is what
  separates that twist from a forward pitch, which the side view alone cannot do. The controls
  stand still.
