# Bouncy Cow — first static art prototype

Requested by strawberry on2026-10-03, message1555912558660157532.
Separate branch off main19a367a; no shared AnimatedModel, GPU path, game data or gameplay edits.

## Measured reference, not an online remake

Owner-disc /DATA/JUNGLE.WAD/Rides/Bouncy/Bouncy.sam declares Id1100, Name Belly Bounce;
EUR graphics text row197 joins the same identity. Model bouncy.mps, animation bouncy.aps.
Built reference is Idle slot2 variant0 frame0, not Create frame0 (bare floor). The Create99
endpoint differs slightly from Idle0 and is not asserted identical.
Footprint is3x4 occupied cells, corner-origin, entrance column1,row3. The transformed visible
assembly is approximately3x4x1.815959 grid units (not metres),330 triangles. Dinosaur alone174.
Hidden egg/shell construction meshes are excluded; all8 meshes total582 triangles.
Body textures use32x32 face/nose/mouth/eyes/feet and64x64 body/belly; sign128x64. The belly's
measured most-common colour is#FEE3A7. Native whole-body animation is vertex morph, not a rig.
The private scalar reference report contains the per-part transforms,counts and texture data.
No original mesh vertices or image pixels are loaded by the new art generator.

## New original asset

- Original procedural geometry and painted PNGs: cream/black cow hide,pink muzzle,ears,
  horns,chunky split hooves,oval bounce pad,small fence and new Bouncy Cow sign.
- Whole assembly336 triangles vs330 reference (+6); cow216 vs174 dinosaur (+42).
- Whole size3x4x1.815959,corner-origin. Cow-only bounds differ deliberately from dinosaur;
  size/triangle-budget agreement is NOT proof of identical silhouette or artistic fidelity.
-12 original textures,32/64 pixels except128x64 sign. GLB embeds the PNGs; OBJ/MTL is an
  alternate export. These are original output files,not shipped assets or raw disc exports.
- Static rest-pose only. No bounce/inflation animation,morph correspondence,rig,seats,
  collision,RSE hookup,build-menu registration or native MPS/APS export is supplied yet.

## Actual checks

Portable stdlib generator tools/bouncy_cow_art.py --check validates finite/indexed geometry,
nondegenerate faces,normals/winding,dimensions/budgets,PNG CRC/pixels,GLB chunks/attributes,
and OBJ/MTL UV/index round-trips. Repeated generation in a different output directory yields
byte-identical GLB,OBJ,MTL and all12 PNG files. The embedded reference brief is measured scalar
metadata; --reference-metrics can supply an independent report. The budget tolerance is a chosen
art-production constraint,not a decoded native clamp.

BouncyCowPreview imports the ACTUAL exported GLB in the game process,with the existing
Ps2Materials shader,Lighting.Read(owner disc),bilinear textures and backface culling. Original
Belly Bounce uses actual AssetLibrary+AnimatedModel at Idle0. No model normalization is applied;
both have the same camera/units,with translations only for side-by-side framing.

Main render passes after36 real draw frames,1152x648,local llvmpipe/X11. Imported336 triangles,
cow216,and reference330 match the independent counts. Removing named cow features changes the
actual framebuffer: ground36682 pixels,head2069,eyes774; geometry is not silently culled/blank.
Camera and original-material receipts are retained in preview-results.json. Build has0 errors;
no exit/resource-leak errors in the successful render. Worker sandbox could not access X11;
its failed/headless attempts remain failed evidence,not substituted as render proof.

Private outputs live in ../bouncy-cow-output,not Git: GLB,OBJ/MTL,PNG textures,actual port PNGs,
measurement/verification JSON and bouncy-cow-prototype.zip. The public branch contains ONLY
original generator,standalone preview source/scene and this note; no art/geometry binaries.
No assistant visual-review claim is made. Aesthetic likeness requires the owner's inspection
of the actual renders; numeric checks alone cannot establish that.
