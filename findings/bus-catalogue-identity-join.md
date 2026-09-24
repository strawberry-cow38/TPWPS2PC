# Native bus catalog identity and ordinary park selection joins

September24 2026. Authorized partial C consulted first; critical MIPS/vtable/data
reads from owner-disc ELF in memory. This closes two explicit gaps in
bus-capacity-tables.md; no claim of full scenario-save decoding.

## Placed category ordinal: K = catalog[w,s,T][i], O+97 = byte(i)

The build list does NOT renumber category ordinals when it filters the menu.
15C880 stores12A550's singleton at widget L+E0. Category wrappers15CC60/CCB0/
CD00/CD50 pass full native counts for kinds3/6/7/1 to15CA78. That routine iterates
original catalog ordinal i, resolves12AE78(singleton,T,i), and passes accepted
entries to15C928 with a1=T,a2=i. 15C934/944 append T/i at L+EC+8*n/L+F0+8*n,
where n is the independently compacted visible-list count. 15D0E8→15D138 returns
stored i for selected row j;15D110→15D148 returns T. 15D158→15D180 independently
looks up the selected persistent key through12B1B8(singleton,T,i).

Normal build-menu branch197F48 (Q+838==0), L=Q+2F4, saves i at197FE0..FEC.
1980A4 passes uint16(i) into125460's a2. Kind1/3/6/7 selects tool11/5/7/6.
125460 saves it in s3 and passes unchanged a2 to the tool virtual initializer.

|Kind|Tool object/vtable|Tool initializer|Key getter|Factory|Concrete initializer|
|---|---|---|---|---|---|
|1|3890A0/35AD58|11A7C0|12B440|14A4A0|11FAE0|
|3|389000/35B830|126920|12B360|149FF0|1B7A98|
|6|389570/35C098|129070|12B3D0|14A180|1FFDA8|
|7|389020/35B7A8|126AC8|12B398|14A0B8|1E8F28|

Each tool gets K from catalog index i, checks195E20(K), but passes i to the factory.
Concrete initializers preserve i, call the same key getter, store K at O+78, and
pass the retained i as a2 to base virtual1E0F30. Decisive paired uses:
- kind1:11FB2C/30 key lookup;11FC98 a2=s7,11FCA8 basecall.
- kind3:1B7AD0/D4 lookup;1B7BF4 a2=s6,1B7C04 basecall.
- kind6:1FFDE0/E4 lookup;1FFF18 a2=s6,1FFF28 basecall.
- kind7:1E8F60/64 lookup;1E9084 a2=s6,1E9094 basecall.

Vtables35B060,366330,36BBF0,369F10 all have1E0F30 at+15C. Concrete virtual+154
has adjustment0 for coaster,−8 for kinds3/6/7. Their common placed base O is A+8,
not the allocation base A. 1E0F44 saves a2;1E0F8C/90 calls1E1E90, which SBs it
at O+97. Independent reverse join152D58/68 reads byte97 then calls12B1B8(T,i),
comparing the reconstructed K with incoming persistent keys.

Thus production can invert the CORRECTLY SELECTED ordered category list to find i
from a unique K. Refuse missing/ambiguous entries; do not use DBA directory order,
sorted-key order, or compacted menu row. Concrete unsorted control: Jungle park2
ordinary keys216→0,215→1. Save restore15FF84/160034/1600D4/160174 passes serialized
byte+94 as the same ordinal, though the complete save schema is outside scope.
Kind8 is not proven by these four constructors; its payload ordinal assignment
at12A710 was already separately read. Startup list contents remain those in
bus-capacity-tables.md.

## Normal world/variant → WAD, terrain, bus and park audio

P is constructed on the stack by13B780: base1C4908, then vtable3602F0 at P+28.
3602FC is vtable+0C's initialization callback150E20, NOT the table base.
149678(P,world,variant) writes variant P+2C at1496FC/714 and world P+30 at149720.
Normal13B7E4..F0 passes13B780's incoming world/variant. Separate13B7C0..D8 can
select world0/variant2; do not treat that as an ordinary selectable park.

Ordinary front-end13B3C8 runs selection object U through217A60/13B050. Selected
row byteU+7B indexes28-byte entries:217A30 reads variant U+97+1C*i;217A48 reads
world U+96+1C*i. Population217568..5D4 copies source+6/+7 from table36DD00.
Its first eight ordinary records hold(0,0),(0,1),(1,0),(1,1),(2,0),(2,1),(3,0),(3,1).
13C254..68 passes those outputs to13B780. Manager13AB50→1C4970 invokes150E20,
which writes3952E8/3952E4 from P+2C/P+30. This is a positive selection chain.

149678's resource-group switch maps native world0/1/2/3 to registered WAD groups
2/4/3/5. Registration131EEC..F64 names these Jungle/Hallow/Fantasy/Space. The
resource world-name table2BF2A0 agrees. WAD REGISTRATION ORDER IS DIFFERENT.

17D7E8 computes s+1, strips special8 from catalog+10, compares that selection
value (not an arbitrary variant bitmask). Ordinary terrain records:

|w|terrain_1 record, ID, selector|terrain_2 record, ID, selector|
|---|---|---|
|0|2C095C,D2,9|2C0980,D3,2|
|1|2BFF3C,7B,1|2BFF60,7C,2|
|2|2BF4B0,29,1|2BF4D4,2A,2|
|3|2C11CC,167,1|2C11F0,168,2|

Name pointers362D78/362D88 identifyterrain_1/terrain_2. Resource category8 maps
to directoryterrain at2BF258. 17B578→17B610→17B240 positively enumerates/selects
and loads the records, rather than merely observing both assets on disc.

Bus logical61/type15 uses the SAME selector:
w0 Bus1/Bus2 records2BFB70/2BFB94;w1 2BFB28/2BFB4C;
w2 2BFAE0/2BFB04;w3 2BFBB8/2BFBDC. Bus1 selector1 (Jungle9),Bus2 selector2.
Independent named-resource control147D10→111AD8:111B28 adds1 to s;111B48 formats
"%sPARK%d/" into the appropriate world's audio directory.

For an EXPLICIT ordinary-park viewer selection, actual world WAD and selected
terrain_1/2 therefore authorize w and s=0/1, Bus1/2 and PARK1/2 respectively.
Jungle terrain_1/Bus1 also carries special8 eligibility for s=2. Preserve the
ordinary-mode assumption; terrain identity alone is not an inverse for all modes.
Localized labels/scenario-file IDs remain unjoined and are unnecessary for this
bounded ordinary-resource adapter. Bus entry-point table stride is explicitly
0x36 per world /0x12 per variant (hex), not decimal36/12; parent rechecked14E2B0.

## Startup/switch lifecycle

Global constructor chain100098→12E7A8→293048→292F98 walks table2A5E48 backward;
entry2A5EA0 invokes1599D8 before application startup2314E8. This positively puts
table construction on startup, not a claim about every earlier constructor call.
Outgoing park13AC5C→1C49D8 invokes vtable+14 target150E80.150FE8→12A5B8 destroys
and clears singleton2B2A78 at12A5DC. Replacement initialization13AC78 then stores
new selectors, so next lazy12A550 relatches them. Do not retain the prior park's
catalog instance across a map switch.
