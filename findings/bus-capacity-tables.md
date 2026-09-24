# Bus represented-entry tables — narrow producer trace (timeboxed partial)

Primary evidence: owner's disc ELF read in memory only, LBA262773,
Mode2/2352+24, size0x2B1160, file0x1000 -> VA0x100000. No extraction,
private files, gameplay changes or commits. Read bus-native-demand.md and
 dba.md. This note does not repeat the already resolved ceiling formula.

## Producer, not uninitialized BSS

**158CF0 is the positive population producer.** With a1==0xFFFF and a0!=0,
it constructs four 0xC8-byte table images on the stack and copies them to
395488,395580,395678,395750. 1599D8 calls it with (1,0xFFFF);1599F8 calls
it with (0,0xFFFF), the teardown branch. The latter destroys resource
handles; it is not an alternate research-filtered population path.

The tables contain pointers to **compiled ELF key arrays**, and counts
loaded from adjacent compiled words, not a runtime scan of all SAMs or
a filter of unlocked DBA records. Construction has no research availability
condition. The unrecognized copy opcodes are ordinary LDL/LDR and SDL/SDR
pairs (opcodes0x1A/0x1B/0x2C/0x2D), with an aligned LD/SD alternative.
They copy the stack image, not compute counts. Examples:158E7C stores the
pointer2B7658 at stack+8;158E94 stores [2B7678]=8 at+14;159078..A4 copies
this image into395488. Other images built15911C..284,159380..4F0,
1595B8..730 and copied immediately afterwards.

## Literal shape and selector producers

12A550 returns singleton **2B2A78**, initializing it through12A4B0 when
its first word is zero. Initialization saves:

* singleton+4 =14E160() =[3952E8];
* singleton+8 =14E170() =[3952E4].

These are **latched** at initialization, not reread from the park every getter.
150E20 receives an upstream object P and stores **P+2C ->3952E8** and
**P+30 ->3952E4** at150E50/54. This supplies a concrete owner/park-state
boundary for a production adapter: select by these two fields, not by a
resource count. 150E20 is referenced in the table at3602FC; the upstream
filedata/world object's constructor and its scenario-field writers were
not reached before the timebox. Thus exact named scenario labels and the
filedata-to-P producer remain open; P is not asserted to be a particular
recovered C++ class.

Let w=singleton+8, s=singleton+4 and B=[360850+4*w].
The four pointers above are w=0,1,2,3, respectively. Their keys identify
Jungle, Halloween, Fantasy, Space (identity join from dba.md).
B+4 holds declared variant counts3,2,2,2. These counts are construction
data, not a proof that every declared variant is chosen in retail gameplay.

For each type the table has three pointer slots followed by three count
slots (unused variant slots zero-filled):

|Type|Key pointer at B+4*s+|Count at B+4*s+|Record stride|
|---|---:|---:|---:|
|3 ordinary ride|08|14|4|
|7 tour|20|2C|4|
|6 track ride|38|44|4|
|8 track upgrade|50|5C|16|
|1 coaster|68|74|4|

Other categories follow at80/8C,98/A4,B0/BC, but do not enter this ceiling.
12B1B8 dispatch and12B360/398/3D0 read the ordinary key arrays. Type8's
actual key getter is12B408: pointer+16*i, first word. Do not use its unusual
12B1B8 fallback as the upgrade-list enumeration.

## Ordered startup key lists (decimal keys, position = category index)

These are the positive initializer's exact table images, **not a console
memory snapshot**. Read list order literally; do not sort keys or DBA entries.

|w / s|type3|type6|type7|type8|type1|sum of five counts|
|---|---|---|---|---|---|---:|
|0 / 0|221,222,223,224,226,228,230,234|220|—|237,236|225|12|
|0 / 1|216,215,227,231,233,219,229|235|232|238|217,218|12|
|0 / 2|—|—|—|—|225,217,218,132,169,133,135,170,47,52,51,371,370,376|14|
|1 / 0|136,139,140,130,144,128,168|146|145|167|132,169|12|
|1 / 1|131,134,137,141,142,143,129|138|—|165,166|133,135,170|13|
|2 / 0|46,48,55,57,58,60,83|62|—|—|47,52|10|
|2 / 1|49,63,50,53,54,56|59|61|81,82|51|11|
|3 / 0|365,366,372,374,379,380,403,383|381|—|402|371|11|
|3 / 1|364,368,369,373,377,378,405,382|367|375|—|370,376|12|

The 0/2 row is especially important: initializer pointers exist for other
categories, but their compiled count words are **zero**, e.g.2B77E0,
2B784C,2B7854,2B7850. Do not infer a populated all-world ride catalog
from allocated array space. Later mutations of these special-variant counts
have not been exhaustively excluded. Nor is 0/2 established as a playable
scenario. Its 14 coasters do not authorize an allSAMcount implementation.

Reproduction anchors for pointers (type order3,6,7,8,1):

* 0/0:2B7658,2B76D0,null,395438,2B76C8.
* 0/1:2B76F0,2B7710,2B76E8,395458,2B7720.
* 0/2:2B77C0,2B7848,null,395468,2B7808.
* 1/0:2B7860,2B7888,2B7880,395550,2B7898.
* 1/1:2B7920,2B7948,null,395560,2B7958.
* 2/0:2B79E0,2B7A08,null,395648,2B7A18.
* 2/1:2B7AA0,2B7AC8,2B7AC0,395658,2B7AD8.
* 3/0:2B7B58,2B7B88,null,395740,2B7B90.
* 3/1:2B7C10,2B7C40,2B7C38,null,2B7C58.

Type8 keys are explicitly constructed at158D2C..DA0,1590AC..108,
15931C..36C,159588..59C. Their16-byte records are key at+0, zero at+4,
resource handle at+8. A nonnull pointer does not override a zero count.

## Identity propagation / research distinction

12A698 enumerates **type8 only**, not all eight DBA categories: it calls
12B070, initializes each record's handle from its key, obtains its payload
through12AD78 ->10F0B0, and writes loop index to payload+2 through12B528
at12A710. The earlier general description of12A710 as category enumeration
must not be broadened into proof it assigns all ordinary ride indices.

Placed-object setter1E1E90 is literally `sb a1,0x97(a0)`; getter1E1E98
is `lbu v0,0x97(a0)`. Base initialization1E0F30 preserves input **a2** in
s1 and passes it to that setter at1E0F8C. Thus byte97 is the caller-supplied
category index (truncated to8bits), **not the persistent DBA key**.
The upstream placement virtual-call argument producer was not closed in
this timebox. Direct setter callers also occur at1D1AAC and1D2518 for
shop/sideshow paths; these categories are not in this denominator.

The denominator tables are **scenario catalog entries**, including entries
not initially unlocked, rather than the current research completion set.
Positive evidence: fixed compiled key/count copies have no availability
predicate; the later singleton allocation12A878 sizes category state arrays
from those complete counts. Research-group/availability accessors described
in dba.md are separate consumers. Do not describe these as *all researchable
DBA records globally*: scenario selection restricts the catalog, and some
catalog records start available without research.

## Adapter boundary and bounded remaining unknowns

Available now: exact normal-variant ordered catalogs, startup denominators,
world/variant selector storage and latching, actual BSS population, and
placed-index storage width/constructor input. A production boundary can
accept the native (P+30,P+2C) selection plus active objects' (type,index)
without inventing allSAMcount or filtering the catalog to unlocked assets.

Still required for a fully automatic filedata adapter:

1. Trace the3602FC virtual owner callback back to P's constructor, and
   P+2C/+30 writers to the filedata/scenario identifiers. No guessed mapping
   from scenario display names to s=0/1 is supplied here.
2. Close the placement caller's a2 producer into the corresponding catalog
   ordinal; do not substitute DBA directory order or persistent key.
3. Establish reachability and any later population of the special0/2 variant;
   exclude other indirect writes before calling the startup tables immutable.
4. Verify startup/lifetime ordering of1599D8 versus first12A550 and park
   switch teardown/reinitialization. No console execution test was performed.

This is the timebox return, not a claim those remaining joins are complete.
