# Native bus admission-pressure and minigame gate

Later entry/staging joins are in `native-entrance-lifecycle.md`; in particular,
mode14's N+28=1 is a pre-list direction marker, and state0's departure-state write
continues into its six-way action switch rather than returning immediately.

September24 2026, owner-disc raw MIPS and authorized partial C. Descriptive names,
not recovered source symbols. Complements bus-native-arrivals.md.

## Guest+A4 is a sticky departure-deferral latch

Activation20BCD0 clears A4 at20BCF4. Active update is2113A8: vtable36CCE0 slot+3C,
adjustment−8 at+38, so allocation base N is the correct coordinate for A4.
N+37 state26h dispatches210D70. The decision path20C930 sets that state at20CA68/70
for departure conditions including happiness<5 and cash<100. Entrance rejection
210C98→210D48/4C can also select it. Positive acceptance charges entrance cash,
increments manager+20, and references the ForceKidsToEnter control at36CB70.

In210D70, with14E288()==1, compare (N+14&63) to (1C4930()&63). A mismatch writes
A4=1 at210E64 WITHOUT trying a route. Matching phase obtains1532D8's point, sets
movement mode N+36=14, and calls18DA78. Failure setsA4; success changes state0B
and timestamps the guest BUT DOES NOT CLEAR A4.

Successful mode14 movement reaches20DC44: group N+28=1,state2E,153298 increments
3953D8. The departure chain later reaches state30;211220 routes to the bus point
with movement mode9, and36C9C0[9]→20DB38 returns it to the pool through14B368.
Removal20BFD0 does not clearA4; later activation does. Another20F588 path can
return a mode14 guest to state0 without clearing it. Therefore neither "currently
leaving" nor "failed route" nor "work pending" is an exact replacement.

## Counter ordering is part of the contract

151954 updates entrance groups;151960 resets2EEB9C immediately before14BE60.
14BE60 walks active list395208;14BF70 savesnext before guest virtual14BF7C.
211724..734 increments2EEB9C when A4 is ALREADY nonzero, before state dispatch
and before guest34 bit40's update-suppression check. Later bus14C2EC tests count<30.
New A4 setters contribute next pass, while a counted guest removed later still
contributed this pass. Earlier group-update contributions are discarded by reset.
Do not substitute a post-update collection size.

Current port does NOT implement this departure-deferral lifecycle or ordered
contribution counter. Passing zero must be labelled unsupported/bypassed, not
claimed to be a native-equivalent measured zero. It belongs in guest lifetime
scheduling, not VisitorNeeds. This is distinct from the already-traced entrance
list count W used in the numeric batch bound.

## Installed3953CC is an attraction-minigame session

124420 reads selected attraction DBA payload+16, then124434→152668→1DEBC8 builds
that selector. Named data controls: GoKarts7,SGPUZZLE5,PONG10.13773C..88 independently
consumes the same field. Factory3698C0 selector7→1DEC78→1CB1D0 installs vtable367B18
and binds the attraction through its common base.151320 updates the installed
object through1DEAC0; state8 triggers1526D0 destruction and clears3953CC at152714.
Thus the bus gate tests session existence, not pause/weather/disaster/build mode.
An absent minigame subsystem can correctly represent "no session"; that does NOT
justify representing the separate departure-deferral count as exact zero.

Coverage: scans recovered known activation,setter,counter andconsumer positives;
no exhaustive exclusion of indirect/aliased/save-restoration writes is claimed.
