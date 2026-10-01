#!/usr/bin/env python3
"""Reproducible ParkSimAudit matrix. Known retail failures stay red, never PASS.

Exit 0: every selected audit passed. Exit 1: unexpected failure/build failure.
Exit 2: only the exact documented retail failures remain (raw exits preserved).
No disc data is extracted. Logs/manifests live in an explicitly supplied output dir.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import time

WORLDS = ('JUNGLE', 'FANTASY', 'HALLOW', 'SPACE')
EXPECTED = {
    'HALLOW': 'every ride that boarded nobody is one polling a dead track subsystem: Thrill Grill',
    'SPACE': 'every ride that calls WALKON timed its legs from real node positions: Moon Buggies',
}
PROJECT = 'tools/TPW.PS2.ParkSimAudit'
ASSEMBLY = PROJECT + '/bin/Release/net8.0/TPW.PS2.ParkSimAudit.dll'
MAX_LOG_BYTES = 8 * 1024 * 1024
# Minimum assertions in the current integrated ParkSimAudit. A stale binary or
# accidentally omitted helper must not turn missing lifecycle coverage into PASS.
REQUIRED_CHECKS = {'availability': 30, 'queue_walk': 7, 'track_ride': 43, 'coaster': 44, 'removal': 57, 'conservation': 20, 'needs_lifecycle': 50, 'disruption': 21, 'service_routing': 5, 'departure_recovery': 6, 'ride_effect_consumer': 33, 'compiled_purchase': 77, 'decision_scheduling': 28, 'terminal_walking': 90, 'post_service_movement': 18, 'native_destination_score': 43, 'native_destination_consumer': 29, 'native_relief': 78, 'native_ride_value': 70, 'native_bus_admission_inputs': 278, 'native_guest_motion_arithmetic': 55, 'native_guest_route_cursor': 76, 'native_walk_consumer': 87, 'native_entrance_flow': 113, 'native_entrance_acceptance': 23, 'native_route_pool': 1082, 'native_rejected_departure': 71, 'native_logical_animation': 62, 'native_ride_queue': 17, 'native_ride_queue_walked': 18, 'staff': 80, 'mechanic': 66}
REQUIRED_WITNESSES = (
    'ok   native destination consumer: actual idle selector need0/sick0 rejects relief',
    'ok   native destination consumer: actual idle selector need90/sick0 chooses relief',
    'ok   native destination consumer: actual idle selector need0/sick90 chooses relief',
    'ok   native destination consumer: native construction state1 is not eligible',
    'ok   native destination consumer: actual idle chooser cannot route invalid compiled toilet into legacy Queued service',
    'ok   native destination score: all121 literal need entries agree with owner',
    'ok   native relief: +523 only enters finishing',
    'ok   native relief: +524 completes once',
    'ok   native ride queue: L-shaped queue: the line turns only at the CENTRE of the corner cell',
    'ok   native ride queue walked: all seven stand still at their own 117340 spots',
    'ok   native ride queue walked: closing a ride leaves its seven waiting (no 117798 event on the state-3 path)',
    'ok   native ride queue walked: demolition (event 10) hands all seven back where they stood',

    'ok   post service movement: relief1234 ordinary arm walks away before the facility deadline',
    'ok   post service movement: shop1234 ordinary arm walks away before the facility deadline',
    'ok   post service movement: shop299 ordinary arm walks away before the facility deadline',
    'ok   decision scheduling: native arm1 remains available before facility deadline and consumes both draws',

    'ok   terminal walking: cash1234 coordinator cannot board from the stub',
    'ok   terminal walking: cash299 real handback retains inside position after success or refusal',
    'ok   terminal walking: remove50 actual same-ID replacement cannot inherit an inside guest',
    'ok   terminal walking: turn3 real approach leg has interpolated walking progress',

    'ok   decision scheduling: cash299 park deadline survives eightfold appetite rate change',
    'ok   decision scheduling: strict boundary rejects stored300 plus extra60 equality',
    'ok   decision scheduling: cash1234 zero-time calls cannot reboard the same shop',
    'ok   decision scheduling: cash299 zero-time calls cannot reboard the same shop',
    'ok   decision scheduling: cash299 eligible later decision can revisit instead of a permanent blacklist',

    'ok   compiled purchase: bare-world source path attaches the named shop independently of region loop',
    'ok   compiled purchase: archive-qualified source path attaches the named shop independently of region loop',
    'ok   compiled purchase: product alone reverses the transfer and selects its own bladder amount',
    'ok   compiled purchase: usa ice cream keeps its regional hunger 15/vomit 15',
    'ok   compiled purchase: eur product7 falls through to all food effects at initial q2 zero',
    'ok   compiled purchase: jap costume handback changes preference to14 without reseeding or food effects',
    'ok   compiled purchase: eur 299 cash refuses the 300-unit sale with no debit or effects at real handback',
    'ok   compiled purchase: eur a refused visit is no customer but still one satisfaction visit',
    'ok   compiled purchase: sideshow: five losing games score exactly 50',
    'ok   compiled purchase: eur exactly300 cash buys once rather than being rejected at the boundary',
    'ok   ride effect consumer: value 55, sickness 20 becomes 20',
    'ok   ride effect consumer: preference 30, value 81 awards band 5',
    'ok   needs lifecycle: completion preserves nonzero preference and applies its middle band',

    'ok   departure recovery: repaired departure resumes and reaches the gate',
    'ok   service routing: unreachable nearest does not degrade urgent errand to the distracting ride',
    'ok   availability regression exercised',
    'ok   removal regression exercised',
    'ok   conservation: identical fixed-tick inputs reproduce the full sampled lifecycle',
    'ok   needs lifecycle: clock control actually applies four rises',
    'ok   needs lifecycle: normal completion applies the configured effect once',
    'ok   disruption: identical disruption inputs replay the entire observed ledger',
    'ok   disruption: late-run negative control catches changed cash through ordinary per-step sampling',
    'ok   disruption: late-run negative control catches orphan needs through ordinary per-step sampling',
    # Staff (findings/staff-person.md, staff-handymen-entertainers.md): the shipped toilet score and
    # the no-staff-room consequence are the two behaviours most likely to be "fixed" by accident.
    'ok   staff: toilet score: |dx| + |dz|*(c+1) EXACTLY as shipped',
    'ok   staff: no staff room: a tired handyman never works again',
    # Mechanics (findings/staff-mechanics-guards.md): the dead duplicated 0x153D40 branch and Call
    # Mechanic's wrong-byte predicate are shipped behaviour that reads like a bug to fix.
    'ok   mechanic: priority: the duplicated 0x153D40 is DEAD',
    'ok   mechanic: call mechanic: the predicate tests the MODE byte for 0x32',
)

# Staff step 4, guards and entertainers (GuardChecks.cs, findings/staff-mechanics-guards.md §5-§6,
# staff-handymen-entertainers.md §4-§5): 51 and 29 on every park. A statement of its own, so the staff
# steps' parallel edits to the table above merge cleanly. The one-leg chase and the prank's allocation
# gate are shipped behaviour that reads like a bug to fix; the camera rule is the guard's one range; the
# lazy spawn-cooldown draw keeps a park with no show off the guests' stream (the entrance soak's flip).
REQUIRED_CHECKS.update({'guard': 51, 'entertainer': 29})
REQUIRED_WITNESSES += (
    'ok   guard: one leg: dispatched with deadline',
    'ok   guard: prank with the litter pool full (40): no litter, NO STINK',
    'ok   guard: camera rule: a camera 8 cells from the PRANKSTER',
    'ok   entertainer: spawn cooldown: 400 ticks with no show draw rand(300) 0 times',
)

# Staff step 5, management (ManagementChecks.cs, findings/staff-management.md): 62 on every park. A
# statement of its own, so parallel staff edits merge cleanly. The first-month ring gap and the patrol
# tool's off-by-one are shipped behaviour that reads like a bug to fix; the strike-then-wages month end
# and the once-a-game Security Award are the two orders most likely to be "tidied".
REQUIRED_CHECKS.update({'management': 60})
REQUIRED_WITNESSES += (
    'ok   management: wages high: with no income at all it cannot fire before the THIRD month end',
    'ok   management: patrol tool: first corner the SMALLER',
    'ok   management: strike walk: the strike starts at a month change and that month is paid in full',
    'ok   management: award: the hidden-award bit lives in [0x3975E8], not the park',
)

# The advisor at runtime, step A (AdvisorChecks.cs, findings/advisor-rules.md, advisor-messages.md): 66 on
# every park; the floor sits just under it. A statement of its own for the same merge reason. The warm-up's
# 16th call, the v52/v53 copies, a silent message costing the whole cycle and the flag-gated counters are
# shipped behaviour that reads like a bug to fix; rules 0 and 48 are the disc's own rules over the port's
# produced variables. Step B adds 10 (76): the lip step and the mouth from a disc lip track, the cut exit,
# the costume, and an object leaving the park -- whose 0x13D8C0 overwriting the ride's own pending mark is
# again shipped behaviour that reads like a bug.
REQUIRED_CHECKS.update({'advisor': 74})
REQUIRED_WITNESSES += (
    'ok   advisor: scheduler: the warm-up refreshes 5 a call for 15 calls, the 16th refreshes the last 4 AND considers rule 0',
    'ok   advisor: v52/v53: v52 copies VARIABLE 75',
    'ok   advisor: states: a SILENT message',
    'ok   advisor: counters: events count with flags bit 3; ride-along (flags 6) DROPS them',
    'ok   advisor: rules: real rule 0 over the producers',
    'ok   advisor: rules: real rule 48',
    'ok   advisor: lips: the gate starts SET and flips on the tick each of',
    'ok   advisor: mouth: with no lip track the gate stays set',
    'ok   advisor: removal: with a later type-2 record about ANOTHER ride',
)

# The ride hoarding (HoardingChecks.cs and MechanicChecks' HoardingService/HoardingStations, findings/ride-hoarding.md):
# 31 on every park. A statement of its own for the same merge reason. The 1x1 table's outward o2, the diagonal panel
# sorted LAST by the EE's truncating FPU, a condemned ride keeping Condemn under the breakdown's raises, and a
# re-raise resuming from wherever the drop had got to are shipped behaviour that reads like a bug to fix.
REQUIRED_CHECKS.update({'hoarding': 31})
REQUIRED_WITNESSES += (
    'ok   hoarding: 1x1 offsets: 0x1f39b8',
    'ok   hoarding: order (0x1f3c70): Big Dripper',
    'ok   hoarding: textures (0x1f5948): broken Hoarding; condemned Condemn; broken again keeps Condemn',
    'ok   hoarding: re-raise: caught at p',
)

# Surface fittings (SurfaceSeatChecks.cs, Model.SurfaceFrame): 8 on every park -- disc-wide, so the same 8. Every
# surface fitting on the disc against its exporter-written helper, with a control per claim that must fail.
REQUIRED_CHECKS.update({'surface_seat': 8})
REQUIRED_WITNESSES += (
    'ok   surface seat: the point: 302 of 302 sit on their helper',
    'ok   surface seat: the facing bit: one bit of noise',
)

# The trampoline (BounceChecks.cs, RseMachine.BounceHeight): 8, disc-wide. The loader's two defaults are read back
# out of the executable, so a port constant that drifts from 0x32 / 1 goes red here, not in a picture.
REQUIRED_CHECKS.update({'bounce': 8})
REQUIRED_WITNESSES += (
    'ok   bounce: the loader writes 1 to inst+0x70',
    'ok   bounce: 4 BOUNCE rides',
)

# The music (MusicChecks.cs, MusicSequencer): 13 on every park -- disc-wide, so the same 13. The witnesses are the
# finding and its control: the park's guest value, written as the console writes it, never leaves level 1, and
# the same value on the event's own selector does -- so a sequencer that simply cannot move fails the pair.
REQUIRED_CHECKS.update({'music': 13})
REQUIRED_WITNESSES += (
    'ok   music: a full park written as selector 2: no slot takes it',
    'ok   music: the control, the same 90 on the event\'s own selector 4 plays the top level',
)

# The laptop's statistics (ParkStatsChecks.cs): 12 on every park -- disc-wide, so the same 12. The witnesses are the
# year roll's instruction words, the bucket loop's assigning store and the rating against a constructed census.
REQUIRED_CHECKS.update({'parkstats': 12})
REQUIRED_WITNESSES += (
    'ok   parkstats: the year roll 0x100EF8 copies 0x12dc -> 0x12e0 and 0x12e4 -> 0x12e8, then zeroes both',
    'ok   parkstats: Park Statistics\' walk 0x186D38 stores each getter\'s result into the bucket',
    'ok   parkstats: the rating of 3 rides (one at tier 2), 6 shops, 1 sideshow, 12 features and nobody: 27',
)

# The park's loans (LoanChecks.cs): 9 on every park, disc-wide. The witnesses are the month end's words (the balance
# ring before the walk and the one debit) and the payoff that leaves the lender lent.
REQUIRED_CHECKS.update({'loans': 9})
REQUIRED_WITNESSES += (
    'ok   loans: the month end 0x100A18 files the balance ring (0x100A70) BEFORE the loan walk (+0x28, +0x14, +0x18; decrements at 0x100AA8/0x100AB0) and the single debit (0x100B18)',
    'ok   loans: a paid-off loan is still TAKEN: nothing sets +0x28 back, so each lender lends once per park',
)

# Research (ResearchChecks.cs): 14 on every park, disc-wide. The witnesses are the executable claims and the fresh
# JUNGLE park's 13 items -- the figure a live savestate corroborates by another route.
REQUIRED_CHECKS.update({'research': 14})
REQUIRED_WITNESSES += (
    'ok   research: 0x1B6880 (start a project) has exactly two callers, the Research screen and the save loader',
    'ok   research: JUNGLE park 1 starts with exactly the 13 items whose tier-0 group is 0',
    'ok   research: advisor research mask bug is in the ELF',
)

# Advisor research (AdvisorResearchChecks.cs): 291 disc-wide checks. Count this as its OWN family:
# "advisor:" does not prove "advisor research:" ran. The witnesses preserve the mask bug, distinguish
# filed partial/complete progress, and require max-per-type upgrades including status0 placements.
REQUIRED_CHECKS.update({'advisor_research': 291})
REQUIRED_WITNESSES += (
    'ok   advisor research: bit4 research selects track AND coaster: 1/2',
    'ok   advisor research: File99 is NOT unlock',
    'ok   advisor research: File100 updates existing producer research 5/10',
    'ok   advisor research: same-key duplicates MAX tier1, not sum2',
    'ok   advisor research: max tier2 even on status0 duplicate',
)

# Sideshow presentation: disc-backed canonical/visibility fixtures are their own family;
# renderer/callback/lifecycle evidence is provided separately by SideshowPresentationSmoke.
REQUIRED_CHECKS.update({'sideshow_presentation': 136})
REQUIRED_WITNESSES += (
    'ok   sideshow presentation: explicit core fixture: raw prize 0: explicit label-grid fixture matches literal native Y slots',
    'ok   sideshow presentation: explicit core fixture: native 0x1D8490: beq v0,zero to0x1D84B0',
    'ok   sideshow presentation: explicit core fixture: native 0x1D84AC: addiu row,row,32',
    'ok   sideshow presentation: explicit core fixture: spinner bounds: literal min1/max1000',
    'ok   sideshow presentation: explicit core fixture: spinner bounds: raw1001 active decrement expected999',
)


def classify(world: str, raw_exit: int | None, text: str, *, timed_out: bool = False,
             truncated: bool = False, launch_error: str | None = None, terrain: int | None = None) -> dict:
    lines = [line.strip() for line in text.splitlines() if line.strip()]
    # A case proves WHICH PARK it ran from its own output (2026-09-25: a viewer runner's unmatched
    # --map silently repeated one park, and every repeat passed).
    if terrain is not None and not any(line.startswith(f'{world} terrain_{terrain}:') for line in lines):
        return {'raw_exit': raw_exit, 'failures': [], 'status': 'wrong_park'}
    failures = [line[5:] for line in lines if line.startswith('FAIL ')]
    counts = {category: sum(line.startswith(f"ok   {category.replace('_', ' ')}:") for line in lines)
              for category in REQUIRED_CHECKS}
    evidence = {'raw_exit': raw_exit, 'failures': failures,
                **{f'{category}_checks': count for category, count in counts.items()}}
    if launch_error:
        return {**evidence, 'status': 'launch_error'}
    if timed_out:
        return {**evidence, 'status': 'timeout'}
    if truncated:
        return {**evidence, 'status': 'truncated_log'}
    if not lines or lines[-1] not in ('PASS', 'FAIL: 1'):
        return {**evidence, 'status': 'unexpected_failure' if raw_exit else 'incomplete_output'}
    if (any(counts[category] < minimum for category, minimum in REQUIRED_CHECKS.items())
            or any(not any(line.startswith(witness) for line in lines) for witness in REQUIRED_WITNESSES)):
        return {**evidence, 'status': 'missing_coverage'}
    if raw_exit == 0 and lines[-1] == 'PASS' and not failures:
        return {**evidence, 'status': 'unexpected_pass' if world in EXPECTED else 'pass'}
    if raw_exit == 1 and lines[-1] == 'FAIL: 1' and len(failures) == 1 and world in EXPECTED:
        # The ride list BEFORE the prose must match exactly. Familiar names inside
        # an unrelated/new failure do not license accepting that failure.
        cause, sep, explanation = failures[0].partition(' -- KNOWN ')
        if cause == EXPECTED[world] and sep and explanation.strip():
            return {**evidence, 'status': 'known_retail_failure'}
    return {**evidence, 'status': 'unexpected_failure'}


def run_process(command: list[str], *, repo: Path, log: Path, timeout: float,
                environment: dict | None = None) -> dict:
    env = dict(os.environ if environment is None else environment, MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0',
               DOTNET_CLI_TELEMETRY_OPTOUT='1')
    started = time.monotonic()
    timed_out, launch_error, raw_exit = False, None, None
    with log.open('w', encoding='utf-8') as output:
        try:
            proc = subprocess.Popen(command, cwd=repo, env=env, stdout=output,
                                    stderr=subprocess.STDOUT, start_new_session=os.name == 'posix')
        except OSError as error:
            launch_error = type(error).__name__
            output.write(f'\n[audit runner: cannot execute: {launch_error}]\n')
        else:
            try:
                proc.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                timed_out = True
                try:
                    if os.name == 'posix':
                        os.killpg(proc.pid, signal.SIGKILL)
                    else:
                        proc.kill()
                except ProcessLookupError:
                    pass
                try:
                    proc.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    output.write('\n[audit runner: process cleanup timed out]\n')
                output.write('\n[audit runner: timed out]\n')
            raw_exit = proc.returncode  # observed status, never a synthetic 124/127
    with log.open('rb') as source:
        data = source.read(MAX_LOG_BYTES + 1)
    return {'command': command, 'log': str(log), 'raw_exit': raw_exit,
            'elapsed_seconds': round(time.monotonic() - started, 3), 'timed_out': timed_out, 'launch_error': launch_error,
            'truncated': len(data) > MAX_LOG_BYTES, 'text': data[:MAX_LOG_BYTES].decode('utf-8', 'replace')}


def git_info(repo: Path) -> dict:
    def query(*args):
        try:
            result = subprocess.run(['git', *args], cwd=repo, capture_output=True, text=True, timeout=10)
            return result.stdout.strip() if result.returncode == 0 else None
        except (OSError, subprocess.TimeoutExpired):
            return None
    status = query('status', '--porcelain')
    return {'revision': query('rev-parse', 'HEAD'), 'dirty': None if status is None else bool(status)}


def file_hash(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open('rb') as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--disc', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--worlds', nargs='+', choices=WORLDS, default=list(WORLDS))
    parser.add_argument('--terrains', nargs='+', type=int, choices=(1, 2), default=[1, 2],
                        help="each world's first and/or second park (default both: all eight parks)")
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--timeout', type=float, default=120)
    parser.add_argument('--no-build', action='store_true', help='reuse a build; recorded explicitly in the manifest')
    args = parser.parse_args(argv)
    if not args.disc.is_file():
        parser.error('--disc must name the existing user-supplied disc file')
    if not 0 < args.timeout <= 3600:
        parser.error('--timeout must be positive and at most 3600 seconds')
    repo, disc, out = args.repo.resolve(), args.disc.resolve(), args.out.resolve()
    try:
        out.mkdir(parents=True, exist_ok=False)
    except FileExistsError:
        parser.error('--out must be a new directory; previous evidence will not be overwritten')
    manifest = {'schema_version': 1, **git_info(repo), 'disc_sha256': file_hash(disc),
                'disc_bytes': disc.stat().st_size, 'build_skipped': args.no_build, 'results': []}
    # Only a clean, freshly built tree is landing evidence; anything else is a local experiment.
    manifest['landing_evidence'] = manifest.get('dirty') is False and not args.no_build
    manifest_path = out / 'manifest.json'

    def save():
        temporary = manifest_path.with_suffix('.json.tmp')
        temporary.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
        temporary.replace(manifest_path)

    if not args.no_build:
        build = run_process([args.dotnet, 'build', PROJECT, '-c', 'Release', '--nologo'],
                            repo=repo, log=out / 'build.log', timeout=args.timeout)
        manifest['build'] = {key: value for key, value in build.items() if key != 'text'}
        if build['raw_exit'] != 0:
            manifest['status'] = 'build_failed'
            save()
            print(f'BUILD_FAILED raw_exit={build["raw_exit"]}; {manifest_path}')
            return 1
    for world in dict.fromkeys(args.worlds):
        for terrain in dict.fromkeys(args.terrains):
            run = run_process([args.dotnet, ASSEMBLY, str(disc), world, f'--terrain={terrain}'], repo=repo,
                              log=out / f'{world.lower()}-{terrain}.log', timeout=args.timeout)
            verdict = classify(world, run['raw_exit'], run['text'], timed_out=run['timed_out'], truncated=run['truncated'],
                               launch_error=run['launch_error'], terrain=terrain)
            row = {key: value for key, value in run.items() if key != 'text'}
            row.update(verdict, world=world, terrain=terrain)
            manifest['results'].append(row)
            save()  # preserve completed parks if the next one is interrupted
            coverage = ' '.join(f'{category}={row.get(f"{category}_checks")}' for category in REQUIRED_CHECKS)
            print(f'{world}/{terrain}: {row["status"].upper()} raw_exit={row["raw_exit"]} {coverage}')
    statuses = {row['status'] for row in manifest['results']}
    if statuses - {'pass', 'known_retail_failure'}:
        exit_code, status = 1, 'unexpected_result'
    elif 'known_retail_failure' in statuses:
        exit_code, status = 2, 'known_retail_failures_remain'
    else:
        exit_code, status = 0, 'all_selected_passed'
    manifest.update(status=status, runner_exit=exit_code)
    save()
    print(f'{status}; runner_exit={exit_code}; landing_evidence={manifest["landing_evidence"]}; {manifest_path}')
    return exit_code


if __name__ == '__main__':
    raise SystemExit(main())
