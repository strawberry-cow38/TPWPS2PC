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
REQUIRED_CHECKS = {'availability': 30, 'removal': 57, 'conservation': 20, 'needs_lifecycle': 24}
REQUIRED_WITNESSES = (
    'ok   availability regression exercised',
    'ok   removal regression exercised',
    'ok   conservation: identical fixed-tick inputs reproduce the full sampled lifecycle',
    'ok   needs lifecycle: clock control actually applies four rises',
)


def classify(world: str, raw_exit: int | None, text: str, *, timed_out: bool = False,
             truncated: bool = False, launch_error: str | None = None) -> dict:
    lines = [line.strip() for line in text.splitlines() if line.strip()]
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


def run_process(command: list[str], *, repo: Path, log: Path, timeout: float) -> dict:
    env = dict(os.environ, MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0',
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
        run = run_process([args.dotnet, ASSEMBLY, str(disc), world], repo=repo,
                          log=out / f'{world.lower()}.log', timeout=args.timeout)
        verdict = classify(world, run['raw_exit'], run['text'], timed_out=run['timed_out'], truncated=run['truncated'], launch_error=run['launch_error'])
        row = {key: value for key, value in run.items() if key != 'text'}
        row.update(verdict, world=world)
        manifest['results'].append(row)
        save()  # preserve completed worlds if the next one is interrupted
        coverage = ' '.join(f'{category}={row[f"{category}_checks"]}' for category in REQUIRED_CHECKS)
        print(f'{world}: {row["status"].upper()} raw_exit={row["raw_exit"]} {coverage}')
    statuses = {row['status'] for row in manifest['results']}
    if statuses - {'pass', 'known_retail_failure'}:
        exit_code, status = 1, 'unexpected_result'
    elif 'known_retail_failure' in statuses:
        exit_code, status = 2, 'known_retail_failures_remain'
    else:
        exit_code, status = 0, 'all_selected_passed'
    manifest.update(status=status, runner_exit=exit_code)
    save()
    print(f'{status}; runner_exit={exit_code}; {manifest_path}')
    return exit_code


if __name__ == '__main__':
    raise SystemExit(main())
