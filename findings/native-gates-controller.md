# Native Gates visual controller — not yet an individual turnstile identification

September24,2026. User asked whether the turnstiles were researched and suggested
another internal name. This note distinguishes a proven arch/door controller from
a still-unidentified per-guest physical barrier. Native admission queues and fee
handling alone do not establish which visual part moves.

## Positive asset and controller join

Catalog2BF2B8 has0x24-byte records. Records2BFA50/74/98/BC select Gates for worlds
2/1/0/3, category0x0E, ID0x1E9 (489), name pointer362F30. Category descriptor2BF288
supplies features.17B240 builds paths;17B578 uses world/variant lookup17D7E8.
This joins the per-world Features/Gates assets, not merely an English string.

Park init151498 separately creates this controller AFTER terrain init149958.
15178C..1517A4 allocates0x34, calls13C600 and stores3953E4. Vtable35EDE0 includes
init13C650, cleanup13C730, update13C780, animation advance13C8A8. Init13C674..80
checks489, allocates model230A98 and loads489 at13C69C..B8 into controller+30.
Live update dispatch is14C354..68; separate advance dispatch150D34..48.

13C780 compares park-open flag14E538 with controller+1C, gated by animation status.
Opening call13C808 requests section5/record1/speed1.0; closing13C888 requests
section5/record0/speed1.0. Model virtual+5C ->228958 ->17C5D8 handles category0x0E,
validates section/record and calls1ABC80 playback. Therefore an actual native
visual animation trigger is established, but it is PARK OPEN/CLOSED, not a guest
paying or crossing the booth line.

Jungle/Fantasy Gates.rss describe closed/open/broken commands and gate animations
and sounds. Those corroborate authored purpose, not proof that this native
controller executes RSS/RSE scripts. A script's existence is not its call site.

## Booth candidate, not mechanism proof

All eight terrain MPS models contain ticket_booths. Mesh indices by terrain1/2:
JUNGLE4/1, FANTASY25/24, HALLOW1/1, SPACE18/2. Its mesh+98 and+9C morph/UV run
pointers are zero in those inspected models. Jungle terrain APS track sets do not
target ticket_booths or Stumps; the other three worlds have no companion terrain
APS entries in the inspected directories. This bounds supplied animation data,
not procedural transforms or separately instantiated moving mechanisms.

Peer measured Jungle ticket_booths near x28.31..31.69,z14.88..16.12, with gate
wall/roof materials. That locates the entrance structure; it does not prove which
part is a turnstile or link individual admission to a moving barrier. Centre-post
meshes near the bus plaza are a different location, not a name-based substitute.

User was asked whether they mean the large themed arch doors or smaller barriers/
booths in front. Await that clarification before claiming this answered the
physical-turnstile request. No turnstile runtime changes made by this research.

The opening unfinished placement header in gates.md is historical: its later
2026-09-23 authored-z section supersedes that earlier coordinate question.
