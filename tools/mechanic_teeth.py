#!/usr/bin/env python3
"""Teeth for `ParkSimAudit --mechanic-only` (MechanicChecks.cs): each mutation below is a plausible
"fix" or slip in the mechanics port, and each must turn the audit RED. The source is restored in a
`finally`, then rebuilt. No disc data is read or written by this script; the audit reads the disc
you pass.

  python3 tools/mechanic_teeth.py --disc DISC [--world JUNGLE --terrain 1] [name ...]

Exit 0 when every selected mutation went red, 1 when one survived, failed to build, or its pattern
no longer matches (the source moved: update the pattern, do not drop the mutation).

EXCLUDED as EQUIVALENT: "Call Mechanic tries the next mechanic when the dispatch fails". 0x178CF8's
outcome depends only on the RIDE (broken, Life, assigned), and 0x124158 returns before the loop for an
assigned ride, so no mechanic can succeed where the first failed: no fixture can tell the two apart.
"""
import argparse, pathlib, subprocess, sys

R = pathlib.Path(__file__).resolve().parent.parent
C = 'core/TPW.PS2.Data/'
M = C + 'Mechanic.cs'; S = C + 'RideService.cs'; P = C + 'ParkStaff.cs'
CO = C + 'CoasterSim.cs'; TR = C + 'TrackRideSim.cs'
# (name, file, old, new, which occurrence of old)
MUT = [
 ('dead-branch-fixed', M, 'var again = NearestUpgrade();', 'var again = NearestBrokenRide();', 0),
 ('coin-inverted', M, 'if (Park.Random(2) == 0)\n        {\n            var broken', 'if (Park.Random(2) != 0)\n        {\n            var broken', 0),
 ('tie-last-wins', M, 'if (score < bestScore) { best = r; bestScore = score; }', 'if (score <= bestScore) { best = r; bestScore = score; }', 0),
 ('broken-ignores-assignment', M, 'if (r.AssignedMechanic != null && !ReferenceEquals(r.AssignedMechanic, this)) continue;', '', 0),
 ('condemned-repairable', M, 'if (repair && (!ride.Broken || ride.Life == 0)) return false;', 'if (repair && !ride.Broken) return false;', 0),
 ('predicate-state-not-mode', M, '&& Mode != 0x32;', '&& State != 0x32;', 0),
 ('waypoint-cost-1', M, 'Morale = (sbyte)Math.Max(0, Morale - 2);', 'Morale = (sbyte)Math.Max(0, Morale - 1);', 0),
 ('waypoint-cost-all-modes', M, 'if (Mode == ModeRepair)\n        {\n            Morale', 'if (Mode != ModeLeave)\n        {\n            Morale', 0),
 ('repair-deadline-inclusive', M, 'if (Stamp != 0 && Stamp < now) { GoalDepth = 0; State = StateOpeningRide; }', 'if (Stamp != 0 && Stamp <= now) { GoalDepth = 0; State = StateOpeningRide; }', 0),
 ('repair-T-level0', M, 'Stamp = unchecked(Park.Now + (uint)StaffTables.MechanicWorkTicks[Level]);', 'Stamp = unchecked(Park.Now + (uint)StaffTables.MechanicWorkTicks[0]);', 0),
 ('install-T-level0', M, 'Stamp = unchecked(Park.Now + (uint)StaffTables.MechanicWorkTicks[Level]);', 'Stamp = unchecked(Park.Now + (uint)StaffTables.MechanicWorkTicks[0]);', 1),
 ('handoff-from-head', M, 'for (int i = IndexIn(list) + 1; i < list.Count; i++)', 'for (int i = 0; i < list.Count; i++)', 0),
 ('opening-morale-5', M, 'Morale = (sbyte)Math.Min(100, Morale + 10);', 'Morale = (sbyte)Math.Min(100, Morale + 5);', 0),
 ('find-work-no-tiredness', M, 'Tiredness = (sbyte)Math.Min(100, Tiredness + 6);', 'Tiredness = (sbyte)Math.Min(100, Tiredness + 5);', 0),
 ('chatter-every-tick', M, 'if (Park.Now % StaffTables.MechanicChatterPeriod == 0) return;', '', 0),
 ('repair-noise-restarts', M, 'if (!Park.IsHandlePlaying(this, HandleRepairNoise)) Park.RaiseSound', 'Park.RaiseSound', 0),
 ('advisor-38-39-swapped', M, 'if (m is Mechanic mech && mech.Available) return ParkSim.AdvisorMechanicOnIt;\n        return ParkSim.AdvisorBusyMechanics;', 'if (m is Mechanic mech && mech.Available) return ParkSim.AdvisorBusyMechanics;\n        return ParkSim.AdvisorMechanicOnIt;', 0),
 ('pending-only-if-added', M, 'ride.UpgradePending = true;\n        return added', 'ride.UpgradePending = added;\n        return added', 0),
 ('request-no-strike-refusal', M, 'if (IsStriking(StaffKind.Mechanic)) return UpgradeRequest.MechanicsOnStrike;', '', 0),
 ('fire-last-keeps-list', P, 'if (Count(StaffKind.Mechanic) == 0) Sim.ClearUpgrades();', '', 0),
 ('wear-every-other-tick', S, 'if ((tick & 3) != 0) return;', 'if ((tick & 1) != 0) return;', 0),
 ('wear-shift-4', S, '(WearRate(r, 0) >> 5)', '(WearRate(r, 0) >> 4)', 0),
 ('life-per-16', S, 'public const int ReliabilityPerLife = 0xF000;', 'public const int ReliabilityPerLife = 0x10000;', 0),
 ('ordinary-breaks-any-status', S, 'else if (r.Status == 2 && r.Reliability < BreakdownBelow) SetRideStatus(r, 4);', 'else if (r.Reliability < BreakdownBelow) SetRideStatus(r, 4);', 0),
 ('threshold-9', S, 'public const int BreakdownBelow = 0xA000;', 'public const int BreakdownBelow = 0x9000;', 0),
 ('track-status-gated', S, '        else if (r.Reliability < BreakdownBelow) SetRideStatus(r, 4);\n    }\n\n    /// <summary>`0x1228D0`', '        else if (r.Status == 2 && r.Reliability < BreakdownBelow) SetRideStatus(r, 4);\n    }\n\n    /// <summary>`0x1228D0`', 0),
 ('coaster-only-2', S, 'else if (r.Status is 2 or 10 && r.Reliability < BreakdownBelow)', 'else if (r.Status is 2 && r.Reliability < BreakdownBelow)', 0),
 ('ordinary-zero-in-2-breaks', S, 'if (r.Reliability == 0) { if (r.Status == 4) SetRideStatus(r, 5); }', 'if (r.Reliability == 0) { if (r.Status is 2 or 4) SetRideStatus(r, 5); }', 0),
 ('repair-no-full-reliability', S, 'r.Reliability = FullReliability;                         // sw 0x64000, 0xE4', '', 0),
 ('ordinary-4-always-empties', S, 'if (cls == RideServiceClass.Ordinary ? r.Life < 1 : cls != RideServiceClass.None)', 'if (cls != RideServiceClass.None)', 0),
 ('ordinary-5-keeps-queue', S, 'if (cls == RideServiceClass.Ordinary) EmptyQueue(r);', '', 0),
 ('condemned-stays-listed', S, 'if (r.ServiceFlag) RemoveUpgrade(r);', '', 0),
 ('breakstat-index-5', S, 'if (r.Machine?.Program.VariableNames.Count > 4) r.Machine[4] = value;', 'if (r.Machine?.Program.VariableNames.Count > 5) r.Machine[5] = value;', 0),
 ('boarding-no-wear', S, 'ApplyWear(ride, tick);\n            if (ride.Status != 4) SetRideStatus(ride, 2);', 'if (ride.Status != 4) SetRideStatus(ride, 2);', 0),
 ('no-condemned-advisor', S, 'if (value < 1 && r.Life >= 1) Advisor?.Invoke(AdvisorCondemned, r);', '', 0),
 ('cost-old-tier', S, 'Finances.Debit(e.Tier(r.CurrentTier).PurchaseCost * 10);', 'Finances.Debit(e.Tier(r.CurrentTier - 1).PurchaseCost * 10);', 0),
 ('cost-x1', S, 'Finances.Debit(e.Tier(r.CurrentTier).PurchaseCost * 10);', 'Finances.Debit(e.Tier(r.CurrentTier).PurchaseCost);', 0),
 ('install-sound-b9', S, "r.CurrentTier < 3 ? 0xB8 : 0xE1", "r.CurrentTier < 3 ? 0xB9 : 0xE1", 0),
 ('defaults-full-capacity', S, 'int capacity = (max >> 1) > 0 ? max >> 1 : 1;', 'int capacity = max > 0 ? max : 1;', 0),
 ('workcell-step-0-flipped', S, '0 => cell.Offset(0, -1),', '0 => cell.Offset(0, 1),', 0),
 ('native-rotation-unflipped', S, 'PlacementTurns is int t ? (4 - (t & 3)) & 3 : 0;', 'PlacementTurns is int t ? t & 3 : 0;', 0),
 ('upgrade-list-16', S, 'public const int UpgradeListCapacity = 15;', 'public const int UpgradeListCapacity = 16;', 0),
 ('coaster-no-status4-wear', CO, 'if (Status == 4) Wear?.Invoke();', '', 0),
 ('coaster-wear-without-riders', CO, 'if (Track.Closed && Riders != 0) Wear?.Invoke();', 'if (Track.Closed) Wear?.Invoke();', 0),
 ('track-wear-while-loading', TR, 'if (_cars.Count > 0 && Status != TrackRideStatus.Loading && Track.Length > 0)\n        {\n            Wear?.Invoke();', 'if (_cars.Count > 0) Wear?.Invoke();\n        if (_cars.Count > 0 && Status != TrackRideStatus.Loading && Track.Length > 0)\n        {\n', 0),
 ('track-6-keeps-cars', TR, 'while (_cars.Count > 0) Remove(0);', '{ }', 1),
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
            r = subprocess.run(['timeout', '900', 'dotnet', DLL, a.disc, a.world, f'--terrain={a.terrain}', '--mechanic-only'],
                               cwd=R, capture_output=True, text=True)
            fails = [l.strip() for l in (r.stdout + r.stderr).splitlines() if l.strip().startswith('FAIL ')]
            if r.returncode != 0 and fails:
                print(f'{name}: RED -- {fails[0][:200]}')
            else:
                print(f'{name}: SURVIVED (exit {r.returncode})'); bad += 1
        finally:
            path.write_text(orig)
    rc, _ = build()
    print(f'restored and rebuilt (exit {rc}); {bad} mutation(s) not red')
    sys.exit(1 if bad or rc else 0)


if __name__ == '__main__':
    main()
