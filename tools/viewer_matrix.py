#!/usr/bin/env python3
"""Fresh Debug build + RENDERED normal-startup Viewer smokes (xvfb, gl_compatibility) across all 8 parks.

Each case must prove, from its OWN log, which park it ran. The viewer's canonical
`[map] loaded world=<WAD> terrain=<leaf>` line must appear exactly once and equal the request, and
the scene's exact `<SCENE> SMOKE PASS checks=N;` line must appear exactly once with N at or above
its minimum. A BYPASS witness or any other substring match is not a PASS.

Why this exists: on 2026-09-25 an uncommitted runner passed `--map=WORLD 2`, which matched no map
label. The viewer silently loaded FANTASY terrain_1, and four published "eight park" claims were
false. Exit 0 requires every selected case to pass. Exit 1 preserves the failure evidence. The
output directory must be new and outside Git. It is not a framebuffer or visual-inspection gate.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import subprocess

import audit_matrix
from audit_matrix import file_hash, run_process
from runtime_audit import (ERROR, clean_environment, engine_version, fresh_output, healthy, output_snapshot,
                           source_snapshot)

WORLDS = ('JUNGLE', 'HALLOW', 'FANTASY', 'SPACE')
PARKS = [(world, terrain) for world in WORLDS for terrain in (1, 2)]
# scene -> (exact PASS label, minimum checks). The minimums are floors below every observed real run.
SCENES = {
    'entrance': ('NativeEntranceFlowSmoke', 'NATIVE ENTRANCE FLOW SMOKE', 651),
    'rejected': ('NativeRejectedDepartureSmoke', 'NATIVE REJECTED DEPARTURE SMOKE', 2399),
    'readiness': ('NativeAnimationReadinessSmoke', 'NATIVE ANIMATION READINESS SMOKE', 6000),
    # 8 followed guests: 3 checks per retirement plus one per admitted leaver, over a fixed core.
    'departure': ('NativeOrdinaryDepartureSmoke', 'NATIVE ORDINARY DEPARTURE SMOKE', 40),
    # Per-update hold checks dominate this count (13144 on JUNGLE-1); the floor only proves they ran.
    'disruption': ('NativeDepartureDisruptionSmoke', 'NATIVE DEPARTURE DISRUPTION SMOKE', 1000),
    # Queue item 10. Per-update stop checks dominate (73473 on JUNGLE-1); the floor only proves it ran.
    'soak': ('NativeEntranceSoak', 'NATIVE ENTRANCE SOAK SMOKE', 5000),
    # The restored seaplane and ferry. JUNGLE loads neither and passes on 4; every other park runs 12,
    # and its PASS line prints only after all of them, so the floor is JUNGLE's.
    'vehicles': ('ParkVehiclesSmoke', 'PARK VEHICLES SMOKE', 4),
    # Track rides: build menu -> station -> the track tool pressed round a loop -> riders on and off.
    'trackride': ('TrackRideSmoke', 'TRACK RIDE SMOKE', 33),
    # Roller coasters: build menu -> station with its queue -> pylons pressed round a ring -> trains -> riders.
    'coaster': ('CoasterSmoke', 'COASTER SMOKE', 53),
}
MAP_LINE = re.compile(r'^\[map\] loaded world=([A-Z]+) terrain=(terrain_[12])\.mps$', re.M)


def map_argument(world: str, terrain: int) -> str:
    """The viewer matches --map against its map LABEL, `f"{WAD}  {leaf}"` with TWO spaces.
    The full label is the only unambiguous form."""
    if world not in WORLDS or terrain not in (1, 2):
        raise ValueError(f'no such park {world}/{terrain}')
    return f'--map={world}  terrain_{terrain}.mps'


def classify(scene: str, world: str, terrain: int, run: dict) -> dict:
    _, label, minimum = SCENES[scene]
    text = run['text']
    lines = [line.strip() for line in text.splitlines()]
    failures = [line for line in lines if ERROR.search(line)]
    result = {'status': 'pass', 'failures': failures, 'world': world, 'terrain': terrain}
    for key, status in (('launch_error', 'launch_error'), ('timed_out', 'timeout'), ('truncated', 'truncated_log')):
        if run.get(key): return {**result, 'status': status}
    if run.get('raw_exit') != 0: return {**result, 'status': 'nonzero_exit'}
    if failures: return {**result, 'status': 'error_output'}
    loaded = MAP_LINE.findall(text)
    result['loaded'] = [list(pair) for pair in loaded]
    if not loaded: return {**result, 'status': 'no_map_witness'}
    if len(loaded) != 1: return {**result, 'status': 'ambiguous_map_witness'}
    if loaded[0] != (world, f'terrain_{terrain}'): return {**result, 'status': 'wrong_map'}
    passes = re.findall(r'^' + re.escape(label) + r' PASS checks=(\d+);', text, re.M)
    if len(passes) != 1: return {**result, 'status': 'missing_pass_witness'}
    result['checks'] = int(passes[0])
    if result['checks'] < minimum: return {**result, 'status': 'missing_coverage'}
    return result


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--disc', type=Path, required=True)
    parser.add_argument('--godot', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--xvfb', default='xvfb-run')
    parser.add_argument('--timeout', type=float, default=900)
    parser.add_argument('--scenes', nargs='+', choices=SCENES, default=list(SCENES))
    parser.add_argument('--parks', nargs='+', default=[f'{w}/{t}' for w, t in PARKS],
                        help='WORLD/1 or WORLD/2 (default: all eight)')
    args = parser.parse_args(argv)
    if not args.disc.is_file() or not args.godot.is_file(): parser.error('disc and Godot must be existing files')
    if not 0 < args.timeout <= 3600: parser.error('timeout must be positive and at most 3600 seconds')
    parks = []
    for text in dict.fromkeys(args.parks):
        match = re.fullmatch(r'([A-Z]+)/([12])', text)
        if not match or match.group(1) not in WORLDS: parser.error(f'not a park: {text}')
        parks.append((match.group(1), int(match.group(2))))
    repo, disc, engine = args.repo.resolve(), args.disc.resolve(), args.godot.resolve()
    try: out = fresh_output(args.out)
    except (OSError, ValueError) as ex: parser.error(str(ex))
    selected = list(dict.fromkeys(args.scenes))
    manifest = {'schema_version': 1, 'scope': 'rendered normal-startup smokes; per-case loaded-map proof; not visual inspection',
                'selected': selected, 'parks': [f'{w}/{t}' for w, t in parks], 'status': 'running', 'results': []}
    path = out / 'manifest.json'

    def save():
        temp = path.with_suffix('.json.tmp')
        temp.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
        temp.replace(path)

    def finish(status, code=1):
        manifest.update(status=status, runner_exit=code); save()
        print(f'{status}; runner_exit={code}; {path}', flush=True)
        return code

    env = clean_environment(disc)
    env.setdefault('LP_NUM_THREADS', '2')

    def execute(name, command, timeout=None):
        result = run_process(command, repo=repo, log=out / f'{name}.log', timeout=timeout or args.timeout, environment=env)
        public = {k: v for k, v in result.items() if k != 'text'}
        public['log_sha256'] = file_hash(Path(result['log']))
        return result, public

    save()
    try:
        manifest.update(source_before=source_snapshot(repo), executing_runner={
                            str(p): file_hash(p) for p in (Path(__file__).resolve(), Path(audit_matrix.__file__).resolve())},
                        disc_sha256=file_hash(disc), engine_sha256=file_hash(engine))
        if manifest['source_before'].get('dirty'):
            manifest['landing_evidence'] = False  # a dirty tree can still be tested, never cited as landing
        version, manifest['engine_probe'] = execute('engine-version', [str(engine), '--version'], 15)
        manifest['engine_version'] = engine_version(version['text'])
        if not healthy(version) or not manifest['engine_version']: return finish('invalid_engine')
        build, manifest['build'] = execute('build', [args.dotnet, 'build', 'game/TPWPS2Viewer.csproj',
                                                  '-c', 'Debug', '-t:Rebuild', '--nologo'])
        if not healthy(build) or 'Build succeeded.' not in build['text']: return finish('build_failed')
        manifest['assemblies'] = output_snapshot(repo)
        if source_snapshot(repo) != manifest['source_before']: return finish('source_changed_during_build')
        save()
        for world, terrain in parks:
            for scene in selected:
                if output_snapshot(repo) != manifest['assemblies']: return finish('assemblies_changed_during_run')
                name = f'{world}-{terrain}-{scene}'
                run, record = execute(name, [args.xvfb, '-a', str(engine), '--rendering-method', 'gl_compatibility',
                                             '--audio-driver', 'Dummy', '--resolution', '640x360', '--path', 'game',
                                             f'res://tests/{SCENES[scene][0]}.tscn', '--',
                                             map_argument(world, terrain), '--mode=park'])
                record.update(classify(scene, world, terrain, run), scene=scene)
                manifest['results'].append(record); save()
                print(f'{name}: {record["status"]} checks={record.get("checks")} loaded={record.get("loaded")}', flush=True)
        if source_snapshot(repo) != manifest['source_before']: return finish('source_changed_during_run')
        if any(r['status'] != 'pass' for r in manifest['results']): return finish('failed_cases')
        full = set(selected) == set(SCENES) and set(parks) == set(PARKS)
        return finish('all_cases_passed' if full else 'selected_cases_passed', 0)
    except (OSError, ValueError, subprocess.SubprocessError) as ex:
        manifest['runner_error'] = f'{type(ex).__name__}: {ex}'
        return finish('runner_error')


if __name__ == '__main__':
    raise SystemExit(main())
