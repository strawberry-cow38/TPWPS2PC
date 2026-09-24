#!/usr/bin/env python3
"""Fresh Debug build + named headless-safe Godot scenes. Not a framebuffer/listening gate.

Exit 0 requires every selected scene's exit, PASS witnesses and minimum coverage.
Exit 1 preserves failure evidence. Output must be a new directory outside Git.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess

import audit_matrix
from audit_matrix import file_hash, git_info, run_process

SCENES = {
    'visitor': 'VisitorAudit', 'rse': 'RseAnimationAudit',
    'texture': 'TextureAnimationAudit', 'mtr': 'MtrAudit',
    'advisor': 'AdvisorBrowserAudit', 'audio': 'RideSoundLifecycleAudit',
    'standing': 'StandingServiceAudit', 'shops': 'CompiledShopViewerAudit',
}
CASES = [f'{world}/{terrain}' for world in ('FANTASY', 'SPACE', 'HALLOW') for terrain in (1, 2)]
OUTPUT = Path('game/.godot/mono/temp/bin/Debug')
ERROR = re.compile(r'(^ERROR:|^SCRIPT ERROR:|\bFAIL(?:\b|:)|Unhandled exception|instances leaked|resources still in use)', re.M)


def classify(scene: str, run: dict) -> dict:
    text = run['text']
    lines = [line.strip() for line in text.splitlines()]
    failures = [line for line in lines if ERROR.search(line)]
    warnings = [line for line in lines if line.startswith('WARNING:')]
    result = {'status': 'pass', 'failures': failures, 'warnings': warnings}
    for key, status in (('launch_error', 'launch_error'), ('timed_out', 'timeout'), ('truncated', 'truncated_log')):
        if run.get(key): return {**result, 'status': status}
    if run.get('raw_exit') != 0: return {**result, 'status': 'nonzero_exit'}
    if failures: return {**result, 'status': 'error_output'}
    prefixes = []
    if scene in ('advisor', 'audio', 'standing', 'shops'):
        label = {'advisor': 'ADVISOR BROWSER', 'audio': 'AUDIO LIFECYCLE', 'standing': 'STANDING SERVICE', 'shops': 'COMPILED SHOP VIEWER'}[scene]
        numbered = [re.fullmatch(re.escape(label) + r' ok: \[(\d+)\] (.+)', line)
                    for line in lines if line.startswith(label + ' ok:')]
        ids = [int(match.group(1)) for match in numbered if match]
        if len(ids) != len(numbered) or ids != list(range(1, len(ids) + 1)):
            return {**result, 'status': 'duplicate_or_missing_assertion_ids'}
        lines = [re.sub(r'^(ADVISOR BROWSER|AUDIO LIFECYCLE|STANDING SERVICE|COMPILED SHOP VIEWER) ok: \[\d+\] ', r'\1 ok: ', line) for line in lines]
    if scene in ('visitor', 'rse'):
        label = 'VISITOR GEOMETRY' if scene == 'visitor' else 'RSE ANIMATION'
        prefixes = [f'{label} PASS {case}:' for case in CASES] + [f'{label} PASS:']
    elif scene == 'texture':
        prefixes = ['VIEWER CLOCK PASS:']
        counts = [int(m.group(1)) for m in re.finditer(r'^TEXTURE BINDING PASS: (\d+) surface checks across five models / four worlds$', text, re.M)]
        result['surface_checks'] = counts[0] if len(counts) == 1 else 0
        if len(counts) != 1 or counts[0] < 146: return {**result, 'status': 'missing_coverage'}
    elif scene == 'mtr':
        prefixes = ['MTR SURFACES PASS: all four named mesh/material/texture witnesses, geometry and transforms']
    elif scene == 'advisor':
        prefixes = ['ADVISOR BROWSER ok: usa/French exact subtitle',
                    'ADVISOR BROWSER ok: jap/German exact subtitle',
                    'ADVISOR BROWSER ok: teardown begins with speech still active',
                    'ADVISOR BROWSER ok: all replaced/active speech streams retire']
        summaries = re.findall(r'^ADVISOR BROWSER PASS: (\d+) checks, 0 failures$', text, re.M)
        checks = sum(line.startswith('ADVISOR BROWSER ok:') for line in lines)
        result['checks'] = checks
        if len(summaries) != 1 or int(summaries[0]) != checks or checks < 45:
            return {**result, 'status': 'missing_coverage'}
    elif scene == 'shops':
        prefixes = [f'COMPILED SHOP VIEWER ok: {world} live definition consumes compiled happiness10'
                    for world in ('JUNGLE', 'HALLOW', 'FANTASY', 'SPACE')]
        prefixes += ['COMPILED SHOP VIEWER ok: re-index replaces the catalogue',
                     'COMPILED SHOP VIEWER ok: re-index does not retain SPACE']
        summaries = re.findall(r'^COMPILED SHOP VIEWER PASS: (\d+) checks, 0 failures$', text, re.M)
        checks = sum(line.startswith('COMPILED SHOP VIEWER ok:') for line in lines)
        result['checks'] = checks
        if len(summaries) != 1 or int(summaries[0]) != checks or checks < 18:
            return {**result, 'status': 'missing_coverage'}
    elif scene == 'standing':
        prefixes = ['STANDING SERVICE ok: real script accepts customer before satisfaction',
                    'STANDING SERVICE ok: explicit host hiding suppresses standing body',
                    'STANDING SERVICE ok: same numeric ride ID does not inherit removed owner',
                    'STANDING SERVICE ok: satisfied guest clears visible toilet thought']
        prefixes += [f'STANDING SERVICE ok: placed quarter turn {turn} completes real relief without reseeding'
                     for turn in range(4)]
        prefixes += [f'STANDING SERVICE ok: placed quarter turn {turn} authored stand is nearer its real stub than its mirror'
                     for turn in range(4)]
        summaries = re.findall(r'^STANDING SERVICE PASS: (\d+) checks, 0 failures$', text, re.M)
        checks = sum(line.startswith('STANDING SERVICE ok:') for line in lines)
        result['checks'] = checks
        if len(summaries) != 1 or int(summaries[0]) != checks or checks < 74:
            return {**result, 'status': 'missing_coverage'}
    elif scene == 'audio':
        prefixes = ['AUDIO LIFECYCLE ok: 2D eight fast no-evidence polls remain pending',
                    'AUDIO LIFECYCLE ok: 3D eight fast no-evidence polls remain pending',
                    'AUDIO LIFECYCLE ok: actual world reset stops old voices',
                    'AUDIO LIFECYCLE ok: world reset retains the reusable global particle-library holder',
                    'AUDIO LIFECYCLE ok: world reset clears old live particles',
                    'AUDIO LIFECYCLE ok: mixer retires stopped playback references']
        result['checks'] = sum(line.startswith('AUDIO LIFECYCLE ok:') for line in lines)
        if result['checks'] < 37 or lines.count('AUDIO LIFECYCLE PASS') != 1:
            return {**result, 'status': 'missing_coverage'}
    else:
        return {**result, 'status': 'unknown_scene'}
    missing = [prefix for prefix in prefixes if sum(line.startswith(prefix) for line in lines) != 1]
    return {**result, 'status': 'missing_coverage' if missing else 'pass', 'missing_witnesses': missing}


def clean_environment(disc: Path, inherited=None) -> dict:
    env = dict(os.environ if inherited is None else inherited)
    for key in list(env):
        if key.startswith('TPW_'): del env[key]
    env.update(TPW_PS2_DISC=str(disc), LIBGL_ALWAYS_SOFTWARE='1')
    return env


def fresh_output(path: Path) -> Path:
    path = path.resolve()
    if any((parent / '.git').exists() for parent in (path, *path.parents)):
        raise ValueError('output must be outside Git ancestry')
    path.mkdir(parents=True, exist_ok=False)
    return path


def source_snapshot(repo: Path) -> dict:
    # Git ignores are not compiler ignores: an ignored Extra.cs can still compile.
    suffixes = {'.cs', '.csproj', '.props', '.targets', '.tscn', '.gdshader', '.py'}
    generated = {'.git', '.godot', 'bin', 'obj', '__pycache__', '.venv', 'node_modules'}
    names = []
    for root in ('game', 'core', 'tools'):
        for directory, dirs, files in os.walk(repo / root, followlinks=False):
            dirs[:] = sorted(d for d in dirs if d not in generated)
            if any((Path(directory) / d).is_symlink() for d in dirs):
                raise ValueError('source directory symlinks require explicit provenance support')
            for name in sorted(files):
                path = Path(directory) / name
                if path.suffix in suffixes or name == 'project.godot': names.append(path)
    names += [repo / name for name in ('Directory.Build.props', 'Directory.Build.targets', 'global.json')
              if (repo / name).is_file()]
    if any(not path.resolve().is_relative_to(repo.resolve()) for path in names):
        raise ValueError('source-file symlink leaves the selected repository')
    files = {str(path.relative_to(repo)): file_hash(path) for path in sorted(names)}
    if 'game/TPWPS2Viewer.csproj' not in files: raise ValueError('missing viewer source')
    digest = hashlib.sha256(json.dumps(files, sort_keys=True).encode()).hexdigest()
    return {**git_info(repo), 'source_sha256': digest, 'files': files}


def runner_snapshot() -> dict:
    return {str(path): file_hash(path) for path in (Path(__file__).resolve(), Path(audit_matrix.__file__).resolve())}


def output_snapshot(repo: Path) -> dict:
    directory = repo / OUTPUT
    files = {str(p.relative_to(directory)): file_hash(p) for p in sorted(directory.rglob('*'))
             if p.is_file() and (p.suffix == '.dll' or p.name.endswith(('.deps.json', '.runtimeconfig.json')))}
    if not {'TPWPS2Viewer.dll', 'TPW.PS2.Data.dll'}.issubset(files):
        raise ValueError('fresh Debug viewer/core assemblies are missing')
    return files


def engine_version(text: str) -> str | None:
    versions = [version for version in re.findall(r'^\d+\.\d+(?:\.[^\s.]+)+$', text.strip(), re.M)
                if 'mono' in version.split('.')]
    return versions[0] if len(versions) == 1 else None


def healthy(run: dict) -> bool:
    return run.get('raw_exit') == 0 and not any(run.get(k) for k in ('launch_error', 'timed_out', 'truncated'))


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--disc', type=Path, required=True)
    parser.add_argument('--godot', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--timeout', type=float, default=120)
    parser.add_argument('--scenes', nargs='+', choices=SCENES, default=list(SCENES))
    args = parser.parse_args(argv)
    if not args.disc.is_file() or not args.godot.is_file(): parser.error('disc and Godot must be existing files')
    if not 0 < args.timeout <= 3600: parser.error('timeout must be positive and at most 3600 seconds')
    repo, disc, engine = args.repo.resolve(), args.disc.resolve(), args.godot.resolve()
    try: out = fresh_output(args.out)
    except (OSError, ValueError) as ex: parser.error(str(ex))
    selected = list(dict.fromkeys(args.scenes))
    manifest = {'schema_version': 1, 'scope': 'headless scene callbacks/geometry/lifetime, not framebuffer or audible output',
                'selected': selected, 'build_skipped': False, 'status': 'running', 'results': []}
    path = out / 'manifest.json'

    def save():
        temp = path.with_suffix('.json.tmp')
        temp.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
        temp.replace(path)

    def finish(status, code=1):
        manifest.update(status=status, runner_exit=code, full_gate_passed=status == 'all_scenes_passed'); save()
        print(f'{status}; runner_exit={code}; {path}', flush=True)
        return code

    env = clean_environment(disc)

    def execute(name, command, timeout=None):
        result = run_process(command, repo=repo, log=out / f'{name}.log', timeout=timeout or args.timeout, environment=env)
        public = {k: v for k, v in result.items() if k != 'text'}
        public['log_sha256'] = file_hash(Path(result['log']))
        return result, public

    save()
    try:
        manifest.update(source_before=source_snapshot(repo), executing_runner=runner_snapshot(), disc_sha256=file_hash(disc),
                        disc_bytes=disc.stat().st_size, engine_path=str(engine), engine_sha256=file_hash(engine))
        version, manifest['engine_probe'] = execute('engine-version', [str(engine), '--version'], min(args.timeout, 15))
        manifest['engine_version'] = engine_version(version['text'])
        if not healthy(version) or not manifest['engine_version']: return finish('invalid_engine')
        sdk, manifest['dotnet_probe'] = execute('dotnet-version', [args.dotnet, '--version'], min(args.timeout, 15))
        manifest['dotnet_version'] = sdk['text'].strip()
        if not healthy(sdk): return finish('invalid_dotnet')
        build, manifest['build'] = execute('build', [args.dotnet, 'build', 'game/TPWPS2Viewer.csproj',
                                                  '-c', 'Debug', '-t:Rebuild', '--nologo'])
        if not healthy(build) or 'Build succeeded.' not in build['text']: return finish('build_failed')
        manifest['assemblies'] = output_snapshot(repo)
        if source_snapshot(repo) != manifest['source_before']: return finish('source_changed_during_build')
        save()
        for scene in selected:
            if output_snapshot(repo) != manifest['assemblies']: return finish('assemblies_changed_during_run')
            run, record = execute(scene, [str(engine), '--headless', '--audio-driver', 'Dummy', '--path', 'game',
                                          f'res://tests/{SCENES[scene]}.tscn'])
            record.update(classify(scene, run), scene=scene)
            manifest['results'].append(record); save()
            print(f'{scene}: {record["status"]} raw_exit={record["raw_exit"]}', flush=True)
        manifest['source_after'] = source_snapshot(repo)
        if manifest['source_before'] != manifest['source_after']: return finish('source_changed_during_run')
        if output_snapshot(repo) != manifest['assemblies']: return finish('assemblies_changed_during_run')
        if runner_snapshot() != manifest['executing_runner']: return finish('runner_changed_during_run')
        if file_hash(disc) != manifest['disc_sha256'] or file_hash(engine) != manifest['engine_sha256']:
            return finish('input_changed_during_run')
        if any(r['status'] != 'pass' for r in manifest['results']): return finish('failed_scenes')
        return finish('all_scenes_passed' if set(selected) == set(SCENES) else 'selected_scenes_passed', 0)
    except (OSError, ValueError, subprocess.SubprocessError) as ex:
        manifest['runner_error'] = f'{type(ex).__name__}: {ex}'
        return finish('runner_error')


if __name__ == '__main__':
    raise SystemExit(main())
