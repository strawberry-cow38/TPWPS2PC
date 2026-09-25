# Plan review: TPWPS2PC completion plan (reviewer 1, whole-plan architect)

All claims below were checked by reading the worktree at `/home/ec2-user/tpwps2-entrance` (uncommitted state included), `git log -p -- plan.md`, progress.md, the findings named, and the live evidence under `/tmp/tpw-cp1-final`. Where I inferred rather than checked, I say so.

## 1. Verdict

Not fit to be "worked through until done" as written, and the reason is structural rather than a wrong milestone. plan.md is 433 lines, and lines 58 to 330 are fourteen dated checkpoint sections appended one commit at a time between 58fbd88 and eab9c3b, four of which claim to be "current" and three of which describe work that has since shipped. The milestone bodies (M0 to M7, lines 331 to 420) have not been edited since the first commit 5d563ba on 2026-09-23, so they carry no status, and the work the branch is actually doing (native entrance lifecycle, 6300 lines of diff against origin/main) maps onto none of them. "Complete" (lines 9 to 16) includes Windows, launcher, audio-heard and UI gates that only humans can close, and the plan has no separate condition under which the agent stops. The milestone scope itself is sound and should be kept. The fix is to cut about 270 lines into progress.md, add one "Now" section, a milestone status table, a blocker and merge-criteria table for the research branch, and an agent-side definition of done. After that it is workable by one agent.

## 2. Concrete defects in the plan

**D1. Four sections claim to be current, and three are stale.**
- Line 3 (header): "Current priority: native entrance-group and departure-pressure guest lifecycle." Correct.
- Line 58, "Current user-priority queue", item 1: "Research the bus." Done and landed (progress.md 2073, main 98b5249). The user ID in that line, `strawberry1552630912481624105`, is not strawberry's ID.
- Line 104 to 130, "Current CP4/CP5 decision", ends at line 130 with "this section controls current priority." It is about shop terminal walking, shipped at 54a58af, and its "next fidelity gap" (weighted destination scoring) shipped at 7d262d0.
- Line 263, "Current review and priority decision", says "Next selection: after the purchase package lands..." That landed at 47281a6.
A successor reading top-down gets four answers. Fix: exactly one "Now" section, replaced in place at each checkpoint, never appended to.

**D2. Milestones carry no status.** `git log -p -- plan.md | grep '^+## '` shows every commit after 5d563ba added a checkpoint section and none touched M0 to M7. M0 items 1 and 2 are done (progress.md 49, the availability fix is `REQUIRED_CHECKS['availability']=30` in tools/audit_matrix.py). M1 is done (progress.md 90, 225, 651). M3 bullet 1 is done and its removal/replay bullets are done (progress.md 154, 197, 707). M7's clean-checkout gate ran at 6004151 (progress.md 680). None of this is visible in plan.md. Fix: a status table per milestone with the progress.md anchor and revision, and the remaining exit-gate items only.

**D3. The current work maps to no milestone.** M5 (lines 388 to 397) lists TOUR/BUMP/COAST census, track representation, needs/economy, and save/load. The entrance lifecycle, route planner, readiness, departure pressure and activation serial appear nowhere in M0 to M7. The nearest is M3's exit gate (line 374, "arrival, waiting, boarding, riding, exit and recovery"). So "work the milestones until done" would never reach the branch's work, and the branch's work never closes a milestone. Fix: add a milestone for the native guest lifecycle with the blocker table from D4 as its checklist.

**D4. The research branch's blockers and its merge path are not in the plan.** Line 3 names four blockers in one sentence. The branch itself lists six (progress.md 2296 to 2303 and 2412 to 2416, findings/native-incoming-controller.md items 1 to 6): planner, readiness, fee overrides, ordinary recovery, pressure population, RNG. Guard staging is a seventh (native-incoming-controller.md item 5). The plan never says what "release" of the branch means: merge to main as opt-in, or flip the default. Those need different blocker sets. Fix: a table with columns item, state, needed-to-merge-as-opt-in, needed-to-flip-default, trigger-if-deferred. My proposed values are in section 5.

**D5. The rendered-matrix runner that produced every eight-park claim is not in the repo, and the plan's own M1 rule would have caught today's defect.** `/tmp/tpw-chain.log` shows the runner is `/tmp/claude-1000/.../scratchpad/cp1-final.py`. tools/runtime_audit.py covers only the headless-safe scenes and none of the four Native*Smoke scenes (checked: grep of `SCENES` in tools/runtime_audit.py). Viewer.cs:1328 builds map labels as `WAD␣␣leaf` and Viewer.cs:509 matches with `Contains`, so `--map=JUNGLE 2` matched nothing and the default loaded. findings/bus-viewer-integration.md:77 already documented the correct form `--map="JUNGLE  terrain_2.mps"`. I confirmed the defect: `/tmp/tpw-departure-eight/JUNGLE-2-NativeEntranceFlowSmoke.log` line 29 loads `FANTASY.WAD/terrain/terrain_1.mps`. M1 (line 348) requires "a truncated/crashed audit must not look successful"; a runner outside tools/ was never under that rule. Fix: put the rendered runner in tools/ with the per-case loaded-terrain assertion (the fixed runner has it, the manifest shows `loaded: [JUNGLE, terrain_2]`) and a negative control (`--map=NOSUCH` must fail). Then audit which earlier claims used the bad form: progress.md 2476 "ALL16" is confirmed affected. The bus eight-park claim (README line 24, "cover all eight parks") cites the two-space form in its findings, so it is probably unaffected. I did not verify its logs.

**D6. The plan violates its own process rules, and nobody records that.** Line 47: "do not merely append another status report." Fourteen appended sections say otherwise. Line 41 (CP3): fire "after two consecutive audit-only packages without a newly reproduced production defect." The branch has added seven check families totalling 1495 required checks (tools/audit_matrix.py diff) and the only production defect found was branch-only (AssignEntranceRoute resetting Leaving, progress.md 2462). No CP3 record exists. The honest framing is that the branch is parity research, where the stopping condition is merge criteria, not a reproduced defect. Fix: say that in the plan and exempt the branch from CP3 explicitly, or apply CP3. Either is fine. Silent non-application is not.

**D7. The uncommitted readiness package has no handoff.** `git status` shows nine modified files and three new; `git diff progress.md` contains only the CP0 correction. Line 431 says the last pushed progress.md is the handoff. Right now it says the next package is the one already built. Fix: commit on the research branch with a progress entry before any planner work begins.

**D8. The "shared main tree" is not main.** `/home/ec2-user/tpwps2` is on local `main` at 326279e, reported by `git branch -vv` as `[origin/main: ahead 1, behind 251]`. The one extra commit is a duplicate of the availability fix that already landed under another SHA. Anyone reading that tree as main reads a tree 251 commits stale. Fix: the plan's working agreement (line 20) should name `origin/main` as the reference and the checkout should be fast-forwarded by whoever owns it. That is not mine to do in a read-only review.

**D9. "Complete" has no agent-side terminating condition.** Lines 9 to 16 require a usable launcher, documented platform requirements and manual rendered smoke tests. findings/release-checklist.md lists nine unchecked items that need Windows, a real disc path, and human ears and eyes. The brief says humans test at the end. Without an "agent-done" definition, "work until done" cannot terminate. Fix: section 6.

**D10. Ownership statements are stale and inconsistent.** Line 3: cow tools owns "RMB/listbox and shop-info UI." Line 100: "Astro owns coordinator/entrances/scorer; cow tools retains money/economy/idle." Line 22 refers to "its active viewer/needs/effects wiring." The brief says cow tools owns Viewer.cs UI, gait, money, audio and particles. Fix: one dated ownership table. Also record the rule the branch already follows in practice: touch Viewer.cs only through partial-class seams (Viewer.Entrance.cs, Viewer.NativeAnimation.cs). The branch's Viewer.cs diff against origin/main is five lines plus three uncommitted.

**D11. The planner's decoded details exist only in this conversation.** grep for `2E22D0`, `(x+z)&3`, or the open-list scan in findings/ finds nothing; only the kind-7 arm of 18C928 (native-shop-flow.md:184) and the 195E28()+100 budget with "units unknown" (native-guest-motion.md:111, native-route-slot-pool.md:97) are written down. The house rule is findings first. Fix: the planner package's first deliverable is `findings/native-route-planner.md`. Two decisions belong in it before code: the budget unit (decode it or label it port policy), and how a real-time budget coexists with M3's fixed-tick replay gate (line 374). A per-tick node budget is the only adapter that keeps replay deterministic, and it must be labelled.

**D12. A stale witness line sits beside every PASS under the opt-in.** Viewer.Bus.cs:72 prints "Zero inputs BYPASS the backlog reduction" unconditionally at bus load, and the manifest lists it as a witness for all twelve completed opt-in cases even though Viewer.Bus.cs:163 to 166 now feed the flow's counts. Fix: print the boundary line only when `_entranceFlow` is null, or have the flow's own witness supersede it.

## 3. Missing work

- **Planner findings and code** (D11), including the deterministic-budget decision.
- **Ordinary failure recovery.** The generic event-2 handler 20F790 (state 5, happiness minus RNG15, unknown78 plus RNG2) and the mode-14 alternate request with flags 0x23 are decoded in findings/native-rejected-departure.md but the port holds the guest with "owner retained" (Viewer.Entrance.cs:80). No milestone names this.
- **Pressure population for guests that did not come through the flow.** Legacy `WantsToGoHome` departures and guards do not contribute to the A4 tally (findings/bus-admission-pressure.md, "Ordinary legacy departing guests are not yet complete contributors"). Flipping the default requires the ordinary state-0 departure decision (20C930 to state 26 to 210D70) to be the producer for every guest. The plan does not name this as the real content of "complete activation/pressure population."
- **The fee rule from data.** Constructor 100470 uses 0 when 14E160()==2, else 150 (native-entrance-lifecycle.md:214). That is a data rule, not a UI. It should be implemented; the UI override belongs to M6.
- **A committed rendered runner** (D5), with the four Native*Smoke scenes and terrain assertions.
- **Correction of published claims** affected by the runner defect: progress.md 2476 to 2504 and, if its logs show the same defect, README line 24.
- **The default-flip decision itself**, as a CP5 with strawberry, with a film of default versus native on the same park and a list of residual differences.
- **M5's "re-census the 27/86 non-boarding rides"** has no evidence anywhere (grep of progress.md and findings finds only the line-76 note). Either do it or delete the bullet.

## 4. Cut or defer

- **Delete plan.md lines 58 to 330.** Every section has a fuller twin in progress.md (headings at 737, 772, 792, 817, 845, 1057, 1093, 1145, 1220, 1246, 1299, 1377, 1488, 1526, 1588). Append any that are not verbatim under one "Archived plan checkpoints" heading in progress.md.
- **Guard staging (second family, 3952AC).** Defer. Trigger: a measured entrance-count divergence attributable to guards, or the owner asks for staff. Porting staff is a subsystem, not a blocker.
- **RNG stream and order parity.** Defer permanently as a documented difference. Trigger: a console trace becomes available. Without the whole update order it cannot be reproduced, and it should not block merge or default.
- **Save/load (M5 bullet 4).** No support exists (grep of core and game finds nothing). Defer. Trigger: owner request.
- **Fee UI override.** Move to M6. Implement only the data rule now.
- **M6 advisor, lip, font and kanji consumers.** Defer unless the owner asks. They are readers without a game producer (progress.md 750 to 753).
- **New check families on the branch.** Stop adding required-count entries to tools/audit_matrix.py per package. A count that must be updated on every refactor is a maintenance tax. Keep a witness line per family instead. This is the CP3 rule applied.
- **M2 format reconciliation.** Only when a chosen consumer or a reproduced defect needs it, which is what line 363 already says.

## 5. Proposed revised priority order

Each item has its stopping condition. One agent, cow tools reviews Viewer.cs and Viewer.Bus.cs touches, humans test at the end.

1. **Land the readiness package.** Wait for the 24-scene matrix (12 of 24 done as of 01:30 UTC: JUNGLE 1/2 and HALLOW 1/2, all exit 0 with asserted terrain; FANTASY-1 in progress). Commit with a progress.md entry, correct the "ALL16" claims, push the research branch. Stop when: manifest shows 24/24 with asserted terrain, the 9 teeth mutations fail by name, and origin has the commit.
2. **Rendered runner into tools/.** Terrain assertion, `--map=NOSUCH` negative control, the four Native*Smoke scenes. Stop when: a clean checkout reproduces the 24-scene manifest from one command in the README.
3. **Rewrite plan.md to the structure in section 7.** Docs only. Stop when: plan.md is about 150 lines, has one "Now" section, the status table, the blocker table and the DoD, and the removed text is in progress.md.
4. **Native route planner.** Findings first (all addresses in the brief, budget unit decided, per-tick budget adapter labelled). Then `NativeRoutePlanner` in core with checks whose mutations reject BFS-shaped output (a fixture where the 1-of-4 permutation and tie rule make A* differ from BFS). Then replace `RequestEntranceRoute`/`PumpEntranceRoutes` behind the same opt-in. Stop when: core checks reject BFS, the 24-scene matrix still passes, cow tools has reviewed, and findings name what is still not native (search cancellation callbacks, 18DB70 callers).
5. **Ordinary failure recovery.** Port 20F790 and the flags-0x23 alternate request. Stop when: a fixture with the pool full and all 10 records busy shows the guest reaching state 5 with the decoded effects and continuing, and removing the alternate request fails by name.
6. **Pressure population.** Make the state-0 departure decision the producer for legacy departures so the tally covers every live guest. Stop when: `DeparturePressure` equals an independent census in a mixed accepted/rejected/going-home smoke, and guards are labelled absent.
7. **Fee rule from data.** Stop when: each of the 8 parks reads its fee with the address cited and the UI override is listed under M6.
8. **Merge to main as opt-in.** Stop when: fast-forward pushed, audit_matrix, runtime_audit and the rendered runner are green at that revision with exactly the two retail reds, README documents the two flags.
9. **Default-flip CP5 with strawberry.** Film default versus native on the same park, list residual differences (RNG, guards, section 0 drawn as section 1, update cadence). Stop when: strawberry decides. Yes means flip, rerun everything, update the README. No means record the trigger.
10. **Release handoff.** Repeat the 6004151 clean-checkout procedure at the merged revision, update release-checklist.md, hand the nine human items to strawberry and VoX. Stop when: the checklist names the exact revision and every open item is a human one.

## 6. Definition of done for the whole plan

**Agent-done** (all must hold at one revision; this is where "until it's done" terminates):
- plan.md has the section-7 structure, one "Now" section, and every milestone row has a status and a progress.md anchor.
- The research branch is on main, either opt-in or default, with strawberry's recorded decision.
- Each of the seven blockers (planner, readiness, fee rule, recovery, pressure population, RNG, guards) is either closed with a findings file and checks, or deferred with a written trigger, in the plan's blocker table.
- tools/audit_matrix.py, tools/runtime_audit.py and the committed rendered runner all pass at that revision with exactly the HALLOW Thrill Grill and SPACE Moon Buggies reds.
- Every new check family has teeth that fail by name.
- The clean-checkout build procedure passes at that revision.
- README and findings/release-checklist.md state supported features, flags, limitations and the corrected eight-park claim.
- The last progress.md entry names the revision, commands, reds and next action, and `git status` is clean.
- `.gitignore` still blocks disc-derived assets and none are committed.

**Release-done** (humans): the nine unchecked items in findings/release-checklist.md, signed by strawberry.

## 7. Proposed structure for the revised plan.md

```
# TPWPS2PC completion plan
Updated <date> at <revision>. One paragraph: where the port is.
## 1. What "complete" means          keep lines 7-16 verbatim
## 2. Working agreement              keep 18-26; add: origin/main is the reference;
                                     dated ownership table; partial-class seam rule
## 3. Now                            ONE section, <=25 lines: package, owner, stopping
                                     condition, evidence path, next review trigger.
                                     Replaced in place, never appended.
## 4. Milestone status               table: M0-M8 | done/partial/open/deferred |
                                     progress.md anchor + revision | remaining gate items
## 5. Native guest lifecycle (M8)    blocker table: item | state | needed for opt-in
   and merge criteria                merge | needed for default | trigger if deferred
## 6. Deferred with triggers         the section-4 list above
## 7. Definition of done             agent-done list; release-done = release-checklist.md
## 8. Checkpoint protocol            keep the table at 28-45 and the 5-item record
                                     format at 49-56; drop the surrounding prose
## 9. Tooling and handoff            keep 421-433
```

Delete lines 58 to 330 from plan.md and archive them in progress.md. progress.md stays append-only, with a one-line note at its top that the newest entry is at the bottom and is the handoff.
