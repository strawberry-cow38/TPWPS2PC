#!/usr/bin/env python3
"""Teeth for `ParkSimAudit --guard-only` (GuardChecks.cs): each mutation below is a plausible "fix" or
slip in the guards/entertainers port, and each must turn the audit RED. The source is restored in a
`finally`, then rebuilt. No disc data is read or written by this script; the audit reads the disc you
pass. (The shape is tools/mechanic_teeth.py's.)

  python3 tools/guard_teeth.py --disc DISC [--world JUNGLE --terrain 1] [name ...]

Exit 0 when every selected mutation went red, 1 when one survived, failed to build, or its pattern no
longer matches (the source moved: update the pattern, do not drop the mutation).

EXCLUDED: "the heckle does not push": the Shocked pop then finds an empty goal stack, which the port
refuses with an exception (natively it would read C+0x17) -- a crash, not a FAIL line.
"""
import argparse, pathlib, subprocess, sys

R = pathlib.Path(__file__).resolve().parent.parent
C = 'core/TPW.PS2.Data/'
G = C + 'Guard.cs'; E = C + 'Entertainer.cs'; S = C + 'ParkStaff.Security.cs'; P = C + 'ParkStaff.cs'
H = C + 'Handyman.cs'; V = C + 'ParkVisitors.cs'; D = C + 'GuestDecisionSchedule.cs'
# (name, file, old, new, which occurrence of old)
MUT = [
 # The chase and the catch.
 ('deadline-kept', G, 'Stamp = now;                                                   // ⭐ 0x1410E0', '// (deadline kept)', 0),
 ('queue-not-immune', S, 'if (plan.Intent == VisitorIntent.Queueing\n', 'if (false && plan.Intent == VisitorIntent.Queueing\n', 0),
 ('giveup-walking-5', G, 'Morale = (sbyte)Math.Max(0, Morale - GiveUpWalkingMorale);', 'Morale = (sbyte)Math.Max(0, Morale - GiveUpMorale);', 0),
 ('catch-bonus-mid-walk', G, 'if (Cell == new ParkCell(fine.X >> 8, fine.Z >> 8)) { Catch(body); return; }',
  'if (Cell == new ParkCell(fine.X >> 8, fine.Z >> 8)) { Catch(body); Morale = (sbyte)Math.Min(100, Morale + CatchMorale); return; }', 0),
 ('catch-tiredness-5', G, 'Tiredness = (sbyte)Math.Min(100, Tiredness + CatchTiredness);', 'Tiredness = (sbyte)Math.Min(100, Tiredness + 5);', 0),
 ('chase-speed-15', G, 'SpeedBits = ChaseSpeed;', 'SpeedBits = PatrolSpeed;', 0),
 ('no-removal-notice', S, 'foreach (var m in Active(StaffKind.Guard)) m.NoticeRemoved(t);', '', 0),
 ('eject-counted-home', V, 'ShowOut(guest, countDeparture: false);\n        Ejected++;', 'ShowOut(guest, countDeparture: true);\n        Ejected++;', 0),
 ('no-departure-notice', S, 'else if (_liveTargets.Contains(t.Id)) GuestRemoved(t.Id);', '', 0),
 # Dispatch and the camera rule.
 ('camera-strict', S, 'if (!(64u < (uint)(dx * dx + dz * dz))) return true;', 'if ((uint)(dx * dx + dz * dz) < 64u) return true;', 0),
 ('camera-ignores-status', S, 'if (f.Status == 0 || !f.IsCamera) continue;', 'if (!f.IsCamera) continue;', 0),
 ('dispatch-25-inclusive', S, 'if (camera.Value || d < 25)', 'if (camera.Value || d <= 25)', 0),
 ('dispatch-64-inclusive', S, 'if (!(d < bestDistance) || !(d < 64)) continue;', 'if (!(d < bestDistance) || !(d <= 64)) continue;', 0),
 ('dispatch-oldest-wins', S, 'if (!(d < bestDistance) || !(d < 64)) continue;', 'if (!(d <= bestDistance) || !(d < 64)) continue;', 0),
 ('dispatch-ignores-busy', S, 'if (m is not Guard g || g.Busy) continue;\n            int dx', 'if (m is not Guard g) continue;\n            int dx', 0),
 # The prank.
 ('stink-unconditional', S, 'if (item != null) Stinks.Add(cell.X, cell.Z);', 'Stinks.Add(cell.X, cell.Z);', 0),
 ('prank-counter-0x15', S, 'AdvisorEvent(0x14, 1);', 'AdvisorEvent(0x15, 1);', 0),
 ('prank-happiness-inclusive', S, 'if (!(happiness < PrankHappinessBelow)) return;', 'if (!(happiness <= PrankHappinessBelow)) return;', 0),
 ('prank-litter-shown', S, 'Litter.Drop(GuestFine(g), vomit: false, shown: false);', 'Litter.Drop(GuestFine(g), vomit: false, shown: true);', 0),
 ('arm-5-unmapped', D, 'if (arm == 5u) return GuestIdleAction.Prank;', '', 0),
 # The heckle and Shocked.
 ('heckle-reach-inclusive', S, 'if (best == null || !(bestDistance < HeckleReach)) return;', 'if (best == null || bestDistance > HeckleReach) return;', 0),
 ('heckle-no-counter', S, 'AdvisorEvent(2, 1);\n        best.Heckled', 'best.Heckled', 0),
 ('shock-no-drain', E, 'Morale = (sbyte)Math.Max(0, Morale - ShockedMorale);', '', 0),
 ('shock-reach-7', E, 'if (best == null || bestDistance > GuardReach)', 'if (best == null || bestDistance > GuardReach + 1)', 0),
 ('shock-no-pop', E, 'GoalDepth--;\n        State = Goals[GoalDepth];', 'GoalDepth--;\n        State = StateIdle;', 0),
 ('shock-deadline-inclusive', E, 'if (!(Stamp < now)) return;\n        Guard best', 'if (!(Stamp <= now)) return;\n        Guard best', 0),
 # The show.
 ('adjacency-diagonal', S, 'if ((uint)(dx * dx + dz * dz) < 2) return true;', 'if ((uint)(dx * dx + dz * dz) <= 2) return true;', 0),
 ('show-no-early-end', E, '&& Park.GuestAdjacent(Cell)) return;', ') return;', 0),
 ('show-600-inclusive', E, 'if (!(unchecked(Stamp + ShowTicks) < now)', 'if (!(unchecked(Stamp + ShowTicks) <= now)', 0),
 ('effector-freed-at-end', E, 'GoalDepth = 0; State = StateIdle;\n        Park.RaiseEntertainerSound(this, SoundTada, HandleTada);',
  'GoalDepth = 0; State = StateIdle; FreeEffector();\n        Park.RaiseEntertainerSound(this, SoundTada, HandleTada);', 0),
 ('show-no-rand3', E, 'if (Park.Random(ShowChance) == 0 && Park.GuestAdjacent(Cell)', 'if (Park.GuestAdjacent(Cell)', 0),
 # Watching.
 ('watch-level-ignored', S, 'StaffTables.EntertainerWatchTicks(best.Level)', 'StaffTables.EntertainerWatchTicks(0)', 0),
 ('watch-inclusive', S, 'if (w.Entertainer.State == Entertainer.StatePerforming && !(w.Until < now)) continue;',
  'if (w.Entertainer.State == Entertainer.StatePerforming && !(w.Until <= now)) continue;', 0),
 ('watch-outlives-show', S, 'if (w.Entertainer.State == Entertainer.StatePerforming && !(w.Until < now)) continue;', 'if (!(w.Until < now)) continue;', 0),
 ('watch-cooldown-600', S, 'public const uint WatchCooldownTicks = 0x384;', 'public const uint WatchCooldownTicks = 600;', 0),
 ('watch-happiness-6', S, 'public const int WatchHappiness = 5;', 'public const int WatchHappiness = 6;', 0),
 ('watcher-walks-on', P, 'Visitors.Walk.Paused = g => Visitors.Staff?.IsWatching(g.Id) == true;', '', 0),
 ('cooldown-drawn-at-sight', S, 'if (!_firstSeen.ContainsKey(g.Id)) _firstSeen[g.Id] = now;',
  'if (!_firstSeen.ContainsKey(g.Id)) { _firstSeen[g.Id] = now; SpawnCooldown(g.Id); }', 0),
 ('cooldown-from-now', S, '_watchCooldown[guest] = cooldown = unchecked(_firstSeen[guest] + (uint)Random(300));',
  '_watchCooldown[guest] = cooldown = unchecked(Now + (uint)Random(300));', 0),
 ('watch-facing-dz-first', S, 'w.FacingQuarterTurns = dx != 0 ? (dx < 0 ? 3 : 1) : (dz > 0 ? 0 : 2);', 'w.FacingQuarterTurns = dz != 0 ? (dz > 0 ? 0 : 2) : (dx < 0 ? 3 : 1);', 0),
 # The gate.
 ('gate-uncounted', S, 'internal void GateStage(Guard g) { Gate?.StageMember(); GateStaged++; }', 'internal void GateStage(Guard g) { GateStaged++; }', 0),
 ('gate-uncrossed', S, 'internal void GateCross(Guard g) { Gate?.CrossMember(); GateCrossed++; }', 'internal void GateCross(Guard g) { GateCrossed++; }', 0),
 ('gate-ignores-coordinator', S, 'if (Gate == null) GateEvent9();', 'GateEvent9();', 0),
 ('copy-kept-at-exit', G, 'Target = null; Leg = 0; GoalDepth = 0; State = StateBackToGate;\n                    DropCopy();',
  'Target = null; Leg = 0; GoalDepth = 0; State = StateBackToGate;', 0),
 ('legs-swapped', G, 'State = Leg == 0 ? StateToMouth : StateToExit;', 'State = Leg == 0 ? StateToExit : StateToMouth;', 0),
 ('no-carry-sound', G, 'Park.RaiseGuardSound(this, SoundCarrying, HandleCarrying);', '', 0),
 # The stink table and the sweep.
 ('sweep-no-stink-stop', H, 'Park.Stinks.Remove(litter.Cell.X, litter.Cell.Z);', '', 0),
 ('stink-cap-11', S, 'public const int Capacity = 10;', 'public const int Capacity = 11;', 0),
 ('stink-remove-shifts', S, '_entries[i] = _entries[^1];\n        _entries.RemoveAt(_entries.Count - 1);', '_entries.RemoveAt(i);', 0),
]
PROJECT = 'tools/TPW.PS2.ParkSimAudit'
DLL = PROJECT + '/bin/Release/net8.0/TPW.PS2.ParkSimAudit.dll'


def build():
    r = subprocess.run(['dotnet', 'build', PROJECT, '-c', 'Release', '--nologo', '-v', 'q'], cwd=R, capture_output=True, text=True)
    return r.returncode, r.stdout[-1500:]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--disc', required=True)
    ap.add_argument('--world', default='JUNGLE')
    ap.add_argument('--terrain', default='1')
    ap.add_argument('names', nargs='*')
    a = ap.parse_args()
    unknown = set(a.names) - {m[0] for m in MUT}
    if unknown:
        sys.exit(f'unknown mutation(s): {sorted(unknown)}')
    bad = 0
    for name, f, old, new, nth in MUT:
        if a.names and name not in a.names:
            continue
        path = R / f
        orig = path.read_text()
        try:
            if orig.count(old) <= nth:
                print(f'{name}: PATTERN GONE ({orig.count(old)} matches) -- update it'); bad += 1; continue
            i = -1
            for _ in range(nth + 1):
                i = orig.index(old, i + 1)
            path.write_text(orig[:i] + new + orig[i + len(old):])
            rc, out = build()
            if rc != 0:
                print(f'{name}: BUILD FAILED\n{out}'); bad += 1; continue
            r = subprocess.run(['timeout', '900', 'dotnet', DLL, a.disc, a.world, f'--terrain={a.terrain}', '--guard-only'],
                               cwd=R, capture_output=True, text=True)
            fails = [l.strip() for l in (r.stdout + r.stderr).splitlines() if l.strip().startswith('FAIL ')]
            if r.returncode != 0 and fails:
                print(f'{name}: RED -- {fails[0][:200]}', flush=True)
            else:
                print(f'{name}: SURVIVED (exit {r.returncode})', flush=True); bad += 1
        finally:
            path.write_text(orig)
    rc, _ = build()
    print(f'restored and rebuilt (exit {rc}); {bad} mutation(s) not red')
    sys.exit(1 if bad or rc else 0)


if __name__ == '__main__':
    main()
