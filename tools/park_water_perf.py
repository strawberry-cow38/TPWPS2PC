#!/usr/bin/env python3
"""Bounded, same-build native park-water A/B using normal Viewer startup and clean retirement.

This is not a 4080 comparison or emulator benchmark. It preserves error/leak gating and
records every sample run separately. Only the documented load-time omission switch changes.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
import re

from audit_matrix import run_process
from runtime_audit import ERROR, fresh_output, output_snapshot, source_snapshot
from viewer_matrix import case_environment, map_argument

LABEL = 'PARK WATER PERF'
MINIMUM = 14
RECEIPTS = (
    'rendered performance display',
    'normal explicit startup without immediate-quit benchmark',
    'normal Viewer startup completes',
    'actual JUNGLE/1 performance map',
    'load-time water switch matches experiment',
    'experiment draws water or omits only its drawable',
    'current performance camera is bound',
    'finite natural-frame measurement samples',
    'bounded monotonic measurement excludes loading and warm-up',
    'camera transform and projection remain fixed throughout measurement',
    'water advances normally or remains absent throughout measurement',
    'normal teardown retires actual Viewer',
    'normal teardown retires actual water',
    'no performance Viewer remains',
)
RESULT = re.compile(r'^PARK WATER PERF RESULT enabled=([01]) samples=(\d+) median_ms=([\d.]+) '
                    r'p95_ms=([\d.]+) draw_calls=([\d.]+) allocated_bytes_per_frame=([\d.]+) '
                    r'wall_seconds=([\d.]+) camera=([A-F0-9]{64})$', re.M)


def classify(enabled: bool, run: dict) -> dict:
    text = run['text']
    errors = [line.strip() for line in text.splitlines() if ERROR.search(line.strip())]
    result = {'status': 'pass', 'enabled': enabled, 'errors': errors}
    for key, status in [('launch_error', 'launch_error'), ('timed_out', 'timeout'), ('truncated', 'truncated_log')]:
        if run.get(key): return {**result, 'status': status}
    if run.get('raw_exit') != 0: return {**result, 'status': 'nonzero_exit'}
    if errors: return {**result, 'status': 'error_output'}
    maps = [line for line in text.splitlines() if line.startswith('[map] loaded ')]
    if maps != ['[map] loaded world=JUNGLE terrain=terrain_1.mps']:
        return {**result, 'status': 'wrong_map'}
    passes = re.findall(r'^PARK WATER PERF PASS checks=(\d+);', text, re.M)
    pass_rows = [line for line in text.splitlines() if line.startswith(LABEL + ' PASS')]
    if len(passes) != 1 or len(pass_rows) != 1:
        return {**result, 'status': 'missing_pass_witness'}
    count = int(passes[0])
    rows = [line for line in text.splitlines() if line.startswith(LABEL + ' ok:')]
    numbered = [re.fullmatch(r'PARK WATER PERF ok: \[(\d+)\] (.+)', line) for line in rows]
    if (count < MINIMUM or len(numbered) != count or any(match is None for match in numbered)
            or [int(match[1]) for match in numbered] != list(range(1, count + 1))
            or any(receipt not in [match[2] for match in numbered] for receipt in RECEIPTS)):
        return {**result, 'status': 'missing_coverage'}
    raw_metrics = [line for line in text.splitlines() if line.startswith(LABEL + ' RESULT')]
    metrics = RESULT.findall(text)
    aims = [line for line in text.splitlines() if line.startswith('[aim]')]
    if (len(aims) != 1 or not re.fullmatch(r"\[aim\] 'A_SEA_02': [1-9]\d* surfaces, centre .+, size .+", aims[0])):
        return {**result, 'status': 'wrong_camera'}
    if len(raw_metrics) != 1 or len(metrics) != 1 or int(metrics[0][0]) != int(enabled):
        return {**result, 'status': 'wrong_experiment'}
    on, samples, median, p95, draws, allocated, wall, camera = metrics[0]
    values = list(map(float, (median, p95, draws, allocated, wall)))
    if (int(samples) < 20 or not all(math.isfinite(x) for x in values)
            or values[0] <= 0 or values[1] < values[0] or values[2] <= 0 or values[3] < 0
            or not 8.5 <= values[4] <= 12):
        return {**result, 'status': 'invalid_measurement'}
    return {**result, 'checks': count, 'samples': int(samples), 'median_ms': values[0],
            'p95_ms': values[1], 'draw_calls': values[2], 'allocated_bytes_per_frame': values[3],
            'wall_seconds': values[4], 'camera': camera}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--disc', type=Path, required=True)
    parser.add_argument('--godot', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args(argv)
    repo = args.repo.resolve()
    disc, engine = args.disc.resolve(strict=True), args.godot.resolve(strict=True)
    out = fresh_output(args.out)
    source = source_snapshot(repo)
    manifest = {'scope': 'same-host rendered JUNGLE/1 A/B; camera-only A_SEA_02; no emulator/hardware parity',
                'source': source, 'results': []}

    def save(): (out / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')

    build = run_process(['dotnet', 'build', 'game/TPWPS2Viewer.csproj', '--nologo'],
                        repo=repo, log=out / 'build.log', timeout=120)
    manifest['build'] = {k: v for k, v in build.items() if k != 'text'}
    save()
    if build['raw_exit'] != 0: return 1
    assemblies = output_snapshot(repo)
    manifest['assemblies'] = assemblies
    manifest['landing_evidence'] = not source['dirty']
    for index, enabled in enumerate((False, True, True, False), 1):
        if source != source_snapshot(repo) or assemblies != output_snapshot(repo):
            manifest['status'] = 'source_or_assembly_changed'; save(); return 1
        env = case_environment(disc, standalone=True)
        env['TPW_PARK_MESH'] = 'A_SEA_02'  # same ordinary camera-only debug framing on both sides
        if not enabled: env['TPW_NATIVE_PARK_WATER'] = '0'
        command = ['xvfb-run', '-a', str(engine), '--rendering-method', 'gl_compatibility',
                   '--audio-driver', 'Dummy', '--resolution', '640x360', '--path', 'game',
                   'res://tests/ParkWaterPerfAudit.tscn', '--', map_argument('JUNGLE', 1), '--mode=park']
        run = run_process(command, repo=repo, log=out / f'{index}-water-{int(enabled)}.log',
                          timeout=90, environment=env)
        row = {k: v for k, v in run.items() if k != 'text'}
        row.update(classify(enabled, run))
        if manifest['results'] and row.get('camera') != manifest['results'][0].get('camera'):
            row['status'] = 'camera_changed_between_runs'
        manifest['results'].append(row); save()
        print(index, enabled, row['status'], row.get('median_ms'), flush=True)
        if row['status'] != 'pass': return 1
    if source != source_snapshot(repo) or assemblies != output_snapshot(repo):
        manifest['status'] = 'source_or_assembly_changed'; save(); return 1
    manifest['status'] = 'all_four_runs_passed'; save()
    return 0


if __name__ == '__main__':
    raise SystemExit(main())