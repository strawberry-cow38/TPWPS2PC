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
REQUIRED_CHECKS = {'availability': 30, 'removal': 57, 'conservation': 20, 'needs_lifecycle': 50, 'disruption': 21, 'service_routing': 5, 'departure_recovery': 6, 'ride_effect_consumer': 33, 'compiled_purchase': 67, 'decision_scheduling': 28, 'terminal_walking': 90, 'post_service_movement': 18, 'native_destination_score': 43, 'native_destination_consumer': 29, 'native_relief': 78, 'native_ride_value': 70, 'native_bus_admission_inputs': 278, 'native_guest_motion_arithmetic': 55, 'native_guest_route_cursor': 76, 'native_walk_consumer': 87, 'native_entrance_flow': 113, 'native_entrance_acceptance': 23, 'native_route_pool': 1082, 'native_rejected_departure': 59, 'native_logical_animation': 62}
REQUIRED_WITNESSES = (
    'ok   native destination consumer: actual idle selector need0/sick0 rejects relief',
    'ok   native destination consumer: actual idle selector need90/sick0 chooses relief',
    'ok   native destination consumer: actual idle selector need0/sick90 chooses relief',
    'ok   native destination consumer: native construction state1 is not eligible',
    'ok   native destination consumer: actual idle chooser cannot route invalid compiled toilet into legacy Queued service',
    'ok   native destination score: all121 literal need entries agree with owner',
    'ok   native relief: +523 only enters finishing',
    'ok   native relief: +524 completes once',

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
