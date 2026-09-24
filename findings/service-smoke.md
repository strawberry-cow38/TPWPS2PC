# Rendered small-toilet service smoke

`game/tests/StandingServiceSmoke.tscn` exercises normal `Viewer._Ready` and keeps the
real viewer tree, unlike the deliberately unready viewer in StandingServiceAudit.
It is a capture driver, not a new headless default-gate scene or physical-input test.

## Reproduction

Build the viewer Debug assembly, create a new output directory outside Git, then run
with the owner's disc read in place (no extracted assets):

```sh
TPW_PS2_DISC="$DISC" LIBGL_ALWAYS_SOFTWARE=1 xvfb-run -a -s '-screen 0 1280x720x24' \
  "$GODOT_MONO" --path game --rendering-method gl_compatibility --audio-driver Dummy \
  --resolution 1280x720 res://tests/StandingServiceSmoke.tscn -- \
  --mode=park --map=JUNGLE --service-shot=/tmp/new-service-capture/jungle.png
```

FANTASY, HALLOW and SPACE are also accepted through the existing viewer map argument.
The output must be a new absolute PNG filename in an existing directory outside Git
ancestry. The driver refuses existing serving/handback/park-view filenames.
It writes the requested serving image, `-after.png` after handback, and `-park.png` at
the default gameplay camera distances panned onto that same serving customer.

The driver uses the actual Features menu/arm/place callbacks with the existing cursor
cell override, lays a short path through the normal path callback and allows the real
script/coordinator to serve a seeded customer. Automatic arrivals are disabled; need
growth is frozen, toilet is91, cash1234 and competing needs are quiet. It manually calls
production TickPark/PresentScripted/PlaceActors while rendering actual process frames.
Viewer._Process is disabled after startup for deterministic captures. Therefore this is
**not an unmodified real-time frame-loop or input-latency test**. The information sidebar
is hidden and the first/after pictures use a deliberately framed close-up camera.

The displayed sprite's actual bound RGBA texture is compared with the in-memory decode
of DATA.WAD's `/Generic/bubbles/tbtoilet.ssh`, in addition to checking Thought.Toilet.
This observes the binding, not merely the name the code intended to request. There is
no raw/decoded asset export: outputs are rendered scene captures only.

## Observed results and limits

On September24 UTC2026, real Compatibility/OpenGL llvmpipe runs on Linux aarch64 with
Godot4.6.stable.mono.official.89cea1439 succeeded for all four worlds: place the facility,
route the urgent customer, keep a standing body while accepted, complete relief91→0,
retain cash1234 and restore one walking customer. The first four-world smoke preceded
the additional texture-binding and default-camera diagnostics; those were subsequently
verified in JUNGLE. This is not a claim that every final diagnostic ran in every world.
All produced1280×720 images. There were no ERROR/SCRIPT ERROR lines; the software display
reported its ordinary unsupported V-Sync warning. Dummy audio is **not audible sign-off**.

JUNGLE crops were reattached to the channel and directly inspected: the full body/legs
stand in the doorway facing inward during service; after handback the guest is on the
path and the doorway is empty behind them. The original small pale icon was hard to
identify and crowded against the hair. The peer's547fba2 change increased Size0.0042→
0.0088 and Height0.62→0.80. The new crop clearly shows the two-figure WC sign with clearance
above the hair. VoX independently identified the WC sign when zoomed in. This is review
of those crops, not a blanket sign-off of all generated images/worlds/zoom levels.

The peer corrected its first pixel estimate after excluding blond hair from its mask:
reported opaque-cloud bounds changed16×13→34×31 at the same close-up camera. Those are
**peer measurements**, distinct from the full texture-quad projection below. The earlier
23-pixel estimate should not be reused. Close-up tuning is not proof of park-zoom readability.

An independent read-only disc investigation also checked the mapping: the initializer
at0x216028 records `bubbles\\tbtoilet.ssh` with id7 (name/id stores at0x216BE0/0x216BEC),
and the guest selection writes7 at0x20CBD4 after reading need+0x79. DATA.WAD has one matching
non-alias entry, with one32×32 type0x85 image named `tbto`. The alpha plane agrees with the
matching source TGA to within1/255 (949/1024 exact). This supports ID/asset/entry/alpha
correctness; it is not an independent full RGB-decoder certification.

## Camera-unit correction and next policy

The game camera constants are **console units**, divided by TileUnits256 in Build.
Default Above2576 is10.0625 Godot units, and Behind1760 is6.875. Treating2576 as Godot
altitude gave a false "necessarily sub-pixel" conclusion, corrected in peerbe8e0cb.

The actual JUNGLE default-pose capture, panned onto the customer, records:

- Behind1760, Above2576, Dolly0, FOV75°.
- World eye(28.496094,10.0625,-28.074219).
- Full32-texel sprite quad projects to **11.46 pixels** across at1280×720.

That is the full quad, not the smaller opaque cloud or pictogram. It is small, but not
sub-pixel. We proposed a48–64px readability range, then **rejected it at the next review**:
the peer measured the cloud at10×8px and the entire outhouse at approximately32×45px in
that same park-view capture. A52px cloud would be wider than its building. The proposed
screen-stable solver was not shipped; peer6b0ff1f retains the existing values and world scale.

Chosen port policy: **indicator at park zoom, readable artwork close up**. This is a bounded
UI choice, not proof of the console's drawing rule or a claim that no other design could
work. Selection/hover/zoom-gating could be different future designs. The close-up retains
head clearance; crowding/DPI/zoom/interaction/native-platform behavior belongs on the final
human checklist, not an approval wait. Next independent work reviews the recently changed
needs-effect consumer semantics rather than reopening the same sizing question indefinitely.

Local capture/log roots are recorded in progress.md; no images/disc payloads are committed.
