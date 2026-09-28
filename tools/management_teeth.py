#!/usr/bin/env python3
"""Teeth for `ParkSimAudit --management-only` (ManagementChecks.cs): each mutation below is a plausible
"fix" or slip in the staff-management port (wages, strikes, training, research, the patrol tool,
kick-out, the advisor producers, the Security Award), and each must turn the audit RED. The source is
restored in a `finally`, then rebuilt. No disc data is read or written by this script; the audit reads
the disc you pass. (The shape is tools/guard_teeth.py's.)

  python3 tools/management_teeth.py --disc DISC [--world JUNGLE --terrain 1] [name ...]

Exit 0 when every selected mutation went red, 1 when one survived, failed to build, or its pattern no
longer matches (the source moved: update the pattern, do not drop the mutation).

EXCLUDED, with the reason:
- "wages before strikes" (swapping the two calls in ParkManagement.MonthChanged): the wage reads state
  0xF, which only a member's own update changes -- never the month change -- so the order is not
  observable through wages; it is kept because the console has it, and said so in the code.
- "coverage 100 only when path == 0": with every path block covered, covered*100/path is 100 anyway.
"""
import argparse, pathlib, subprocess, sys

R = pathlib.Path(__file__).resolve().parent.parent
C = 'core/TPW.PS2.Data/'
F = C + 'ParkFinances.cs'; T = C + 'StaffTables.cs'; M = C + 'ParkStaff.Management.cs'; S = C + 'StaffMember.cs'
PM = C + 'ParkManagement.cs'; RM = C + 'ResearchManager.cs'; RS = C + 'Researcher.cs'; P = C + 'ParkStaff.cs'
# (name, file, old, new, which occurrence of old)
MUT = [
 # Wages and the month end.
 ('monthend-no-debit', F, 'Debit(loans + wagesTenths);', '// (no debit)', 0),
 ('period-reads-first-month', F, 'if (!(k < PeriodCount)) return -1;', 'if (k > PeriodCount) return -1;', 0),
 ('wages-high-one-month', F, 'IncomeInPeriod(1) / 10 < WagesInPeriod(1) / 10 && IncomeInPeriod(2) / 10 < WagesInPeriod(2) / 10',
  'IncomeInPeriod(1) / 10 < WagesInPeriod(1) / 10', 0),
 ('paywage-no-accumulator', F, 'WageAccumulator += amount;\n        return Debit(amount);', 'return Debit(amount);', 0),
 ('freebuild-debits', F, 'if (FreeBuild) return true;', '', 0),
 ('prorata-current-month', S, 'Park.Clock.DaysInPreviousMonth);', 'ParkClock.DaysInMonth[Park.Clock.Month]);', 0),
 ('striker-paid', S, 'StaffTables.ProRatedWage(Kind, Level, State == StateStriking,', 'StaffTables.ProRatedWage(Kind, Level, false,', 0),
 ('fire-not-paid', P, 'Sim.Finances.PayWage(wage * 10);', '', 0),
 # Strikes.
 ('strike-base-swapped', T, 'StrikeMessageBase = { 3, 0, 2, 4, 1 };', 'StrikeMessageBase = { 0, 3, 2, 4, 1 };', 0),
 ('energy-threshold-inclusive', M, 'if (energy / n < StaffTables.StrikeEnergyBelow) return true;',
  'if (energy / n <= StaffTables.StrikeEnergyBelow) return true;', 0),
 ('morale-threshold-exclusive', M, 'return !((uint)StaffTables.StrikeMoraleAtMost < (uint)(morale / n));',
  'return morale / n < StaffTables.StrikeMoraleAtMost;', 0),
 ('strike-single-employee', M, 'if (n < StaffTables.StrikeMinimumStaff) return false;', 'if (n < 1) return false;', 0),
 ('strike-end-runs-ladder', M, 'else StrikeLadder(kind);', 'StrikeLadder(kind);', 0),
 ('happier-at-any-stage', M, 'if (_strikeStage[t] == 1) Advisor', 'if (_strikeStage[t] >= 1) Advisor', 0),
 ('stage5-keeps-striking', M, 'case 5: SetStriking(kind, false);', 'case 5: SetStriking(kind, true);', 0),
 ('strike-gate-ignored', M, 'if (!DebugEveryoneStrikes && !ParkRunning) return;', '', 0),
 ('month-end-skips-strikes', PM, 'Staff?.MonthlyStrikeCheck(Clock.Month);', '', 0),
 # The calendar and the award.
 ('weekly-every-6', PM, 'Clock.Day % 7 == 0', 'Clock.Day % 6 == 0', 0),
 ('weekly-not-on-the-1st', PM, 'if (dayRolled && Clock.Day != day && Clock.Day % 7 == 0)',
  'if (dayRolled && Clock.Day != 0 && Clock.Day % 7 == 0)', 0),
 ('award-threshold-inclusive', PM, 'Staff.FeatureCoverage(0x40) > StaffTables.SecurityAwardCoverageAbove',
  'Staff.FeatureCoverage(0x40) >= StaffTables.SecurityAwardCoverageAbove', 0),
 ('award-every-week', PM, 'if (!Awards.HasHiddenAward(StaffTables.SecurityAwardBit)', 'if (true', 0),
 ('coverage-true-fraction', M, 'return count * 100 / n & 0xFFFF;', 'return count * 100 / (n * 8) & 0xFFFF;', 0),
 ('coverage-ignores-status', M, 'if (f.Status == 0) continue;\n            bool match', 'bool match', 0),
 ('camera-block-8', M, 'int bx = f.Origin.X >> 4, bz = f.Origin.Z >> 4;', 'int bx = f.Origin.X >> 3, bz = f.Origin.Z >> 4;', 0),
 ('featurecount-ignores-status', M, 'if (f.Status == 0) continue;\n            int bit', 'int bit', 0),
 # The producers.
 ('patrol-coverage-exclusive', M, 'for (int c = m.PatrolX0 >> 2; c <= m.PatrolX1 >> 2; c++)',
  'for (int c = m.PatrolX0 >> 2; c < m.PatrolX1 >> 2; c++)', 0),
 ('no-area-inverted', M, 'return set < n ? (100 - set * 100 / n) & 0xFFFF : 0;', 'return n == 0 ? 0 : set * 100 / n;', 0),
 ('training-percent-over-5', M, 'levels * 100 / (n * 4)', 'levels * 100 / (n * 5)', 0),
 ('advisor-count-ignores-hire', M, 'Count(kind) - (_hireHeld != null && _hireHeld.Kind == kind ? 1 : 0);', 'Count(kind);', 0),
 ('max-tiredness-first', M, 'if (max < m.Tiredness) max = m.Tiredness;', 'if (max == 0) max = m.Tiredness;', 0),
 # Training.
 ('training-affordability-inclusive', M, 'if (money.Balance < cost * 10)', 'if (money.Balance <= cost * 10)', 0),
 ('training-no-focus', M, 'Focus?.Invoke(member);', '', 0),
 ('training-no-reset', S, 'if (Level < level) { Morale = 100; Tiredness = 0; }', '', 0),
 ('training-to-level-5', M, 'public bool TrainingOffered(StaffMember member) => member.Level < StaffTables.MaxLevel;',
  'public bool TrainingOffered(StaffMember member) => member.Level <= StaffTables.MaxLevel;', 0),
 ('training-cost-next-level', S, 'StaffTables.TrainingCost(Kind, Level, freeBuild)', 'StaffTables.TrainingCost(Kind, Math.Min(Level + 1, 4), freeBuild)', 0),
 ('training-no-sound', M, 'UiSound?.Invoke(StaffTables.UiSoundTrained);', '', 0),
 # Research.
 ('research-budget-100', RM, 'Budget = StaffTables.ResearchBudgetInitial;', 'Budget = 100;', 0),
 ('research-share-ignores-n', RM, '/ (n * 100u);', '/ 100u;', 0),
 ('research-no-tiredness', RS, 'int t = Tiredness + extra;', 'int t = Tiredness;', 0),
 ('research-chance-inclusive', RS, 'if (r < StaffTables.ResearchChanceBelow)', 'if (r <= StaffTables.ResearchChanceBelow)', 0),
 ('research-draw-after-tired', RS, 'int r = Park.Random(StaffTables.ResearchChanceOutOf);\n        if (TiredOrStriking()) return;',
  'if (TiredOrStriking()) return;\n        int r = Park.Random(StaffTables.ResearchChanceOutOf);', 0),
 ('research-complete-strict', RM, 'if (!(s.Progress < s.Required)) Complete(s);', 'if (s.Progress > s.Required) Complete(s);', 0),
 ('research-feature-as-shop', RM, '2 => 0x4F,', '2 => 0x4D,', 0),
 ('research-7e-inverted', RM, '(MechanicCount?.Invoke() ?? 1) == 0 ? 0x7E : 0x4C', '(MechanicCount?.Invoke() ?? 1) != 0 ? 0x7E : 0x4C', 0),
 ('research-item-kept', RM, 's.Item = -1;\n    }', '}', 0),
 ('research-start-from-zero', RM, 's.Progress = unchecked((uint)percentDone * s.Required) / 100u;', 's.Progress = 0;', 0),
 # The patrol tool.
 ('patrol-no-off-by-one', M, 'new ParkCell(unchecked((short)(Corner.X + 1)), unchecked((short)(Corner.Z + 1)))', 'Corner', 0),
 ('patrol-highlight-exclusive', M, '(x1 - x0 + 1) & 0xFF', '(x1 - x0) & 0xFF', 0),
 ('patrol-cancel-always-leaves', M, 'if (!CornerPlaced) { Open = false; return true; }', '{ Open = false; return true; }', 0),
 ('patrol-cursor-no-hold', M, 'if (!CornerPlaced) Corner = cell;\n        Member.SetHeld(true);', 'if (!CornerPlaced) Corner = cell;', 0),
 ('patrol-no-cash-sound', M, '_staff.UiSound?.Invoke(StaffTables.UiSoundCash);', '', 0),
 # The Staff Room.
 ('kickout-resets-tiredness', M, '{ m.EndRest(); n++; }', '{ m.EndRest(); m.Tiredness = 0; n++; }', 0),
 ('resting-ignores-room', M, 'm.State == StaffMember.StateResting && Equals(m.Target, room));', 'm.State == StaffMember.StateResting);', 0),
 ('kickout-order', T, '{ StaffKind.Mechanic, StaffKind.Researcher, StaffKind.Handyman, StaffKind.Entertainer, StaffKind.Guard };',
  '{ StaffKind.Researcher, StaffKind.Mechanic, StaffKind.Handyman, StaffKind.Entertainer, StaffKind.Guard };', 0),
 ('ambience-every-poll', M, 'if ((!had || old.Status != f.Status) && f.Status == 2)', 'if (f.Status == 2)', 0),
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
            r = subprocess.run(['timeout', '900', 'dotnet', DLL, a.disc, a.world, f'--terrain={a.terrain}', '--management-only'],
                               cwd=R, capture_output=True, text=True)
            fails = [l.strip() for l in (r.stdout + r.stderr).splitlines() if l.strip().startswith('FAIL ')]
            crashed = r.returncode != 0 and not fails and 'Unhandled exception' in (r.stdout + r.stderr)
            if r.returncode != 0 and fails:
                print(f'{name}: RED -- {fails[0][:200]}', flush=True)
            elif crashed:
                print(f'{name}: SURVIVED as a CRASH, not a FAIL line (exit {r.returncode})', flush=True); bad += 1
            else:
                print(f'{name}: SURVIVED (exit {r.returncode})', flush=True); bad += 1
        finally:
            path.write_text(orig)
    rc, _ = build()
    print(f'restored and rebuilt (exit {rc}); {bad} mutation(s) not red')
    sys.exit(1 if bad or rc else 0)


if __name__ == '__main__':
    main()
