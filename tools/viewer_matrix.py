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

--standalone-case research-persistence is a SEPARATE two-Viewer component fixture, run at
640x360 and 1152x648. It requires exactly TWO JUNGLE/1 map witnesses and no all-researched
override. It does not relax the ordinary one-map cases or prove a full-world save/load.
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
    # The restored seaplane and ferry. JUNGLE loads the seaplane alone and passes on 8; every other park
    # runs 12, and its PASS line prints only after all of them, so the floor is JUNGLE's.
    'vehicles': ('ParkVehiclesSmoke', 'PARK VEHICLES SMOKE', 8),
    # Track rides: build menu -> station -> the track tool pressed round a loop -> riders on and off.
    # Terrain 1 builds the karts, terrain 2 the water ride (37, with its flow); the floor is the karts'
    # (34 with the zero-length leg's check).
    'trackride': ('TrackRideSmoke', 'TRACK RIDE SMOKE', 34),
    # Roller coasters: build menu -> station with its queue -> pylons pressed round a ring -> trains -> riders.
    # Terrain 2 builds the park's last listed coaster, so eight coasters; the floor is HALLOW's Hades
    # (65 with the valid-cell field's, the pylon edit's keys', the station chevrons', the refusal
    # reason's and the entry cell's checks).
    'coaster': ('CoasterSmoke', 'COASTER SMOKE', 65),
    # Staff: the laptop's Hire panel clicked through to a handyman, the real drop press, his walk
    # (section 0), a sweep of litter and vomit, a toilet cleaned and stamped. Every park runs the same
    # 53; the floor sits just under it.
    'staff': ('StaffSmoke', 'STAFF SMOKE', 50),
    # Mechanics: an ordinary ride forced below 10.0 (the one test hook) breaks and smokes through its
    # own script; a mechanic hired through the laptop, called from the ride's menu, repairs it (logical
    # 16, facing, the noise), the smoke is killed, the Details bar reads 100; then an upgrade installed
    # and paid for at completion; and the ride's hoarding raised, risen, measured in the world (turned
    # too), dropped and raised again for the upgrade. Every park runs the same 48; the floor sits just under it.
    'mechanic': ('MechanicSmoke', 'MECHANIC SMOKE', 46),
    # The pointer (strawberry, 2026-09-30): real pointer warps, not the capture override. A placed ride's
    # footprint tiles hover it and its box top off those tiles does not (the control the old box test fails);
    # the gate's tiles hover the gate; right-click offers Open Park, choosing it opens the park, and the gate
    # sounds its world's own clip once. Every park runs the same 13.
    'pointer': ('PointerTilesSmoke', 'POINTER TILES SMOKE', 13),
}
# ⭐ Parks START CLOSED, as on the console ([0x2B72A4], set only by Open Park 0x14E4C0; 2c274fe): no bus
# admits a guest until the laptop opens the gate. These scenes are ABOUT guests arriving, so they open it with
# the viewer's own `--laptop-park-open`; every other scene runs the closed default, which keeps it covered.
# Proof the gate reaches admission: without the flag all six fail on every park ("normal bus batch
# automatically acquired research entrance owners") and nothing else does.
PARK_OPEN_SCENES = {'entrance', 'rejected', 'readiness', 'departure', 'disruption', 'soak'}
# The seaplane and ferry are OFF by default since 3f4c44d (the PS2 never runs them); the vehicles smoke is the one
# scene about them, so it opts back in with the viewer's own `--seaplane-ferry`. Without it all 8 parks fail
# "loads seaplane ()" -- which is what this matrix read from 3f4c44d until the flag was added.
VEHICLE_SCENES = {'vehicles'}
MAP_LINE = re.compile(r'^\[map\] loaded world=([A-Z]+) terrain=(terrain_[12])\.mps$', re.M)
# Guards and entertainers: an entertainer and a guard hired through the laptop's tabs and the real press,
# guests stopping to watch a show and drawn facing him, a forced prank (the one test hook) with its
# YellowStink on screen, the catch, the guest's copy carried out through the entrance turnstile and
# dropped at the corridor start, and a sweep stopping the stink. Every park runs the same 38. Added as a
# statement of its own so the staff steps' parallel additions to the table above merge cleanly.
SCENES['guard'] = ('GuardSmoke', 'GUARD SMOKE', 38)
# Staff management: a crew of five hired through the hire tool, All Staff opened through the real
# Information menu and each page compared with its member, a researcher trained, a patrol area drawn with
# the mode-17 tool (the off-by-one stored), the mechanic fired, and a month ended through the viewer's own
# calendar advance with the wage bill debited. A statement of its own for the same merge reason.
SCENES['management'] = ('ManagementSmoke', 'MANAGEMENT SMOKE', 17)
# The advisor, step B: the greeting risen, spoken (the talk record its length picks, the mouth on the lip track)
# and dropped behind the talk pass with Music/SFX ducked to 25 and back; rule 47 fired through its real variables
# (the one hook: event counter 0x15) into the stack and the envelope's count; L2 opens it, the text shows, Delete
# removes it; a ride's breakdown jumps the camera and its delete takes its records; pause holds the voice; an
# immediate message interrupts the speech; a modal message locks the pad, flaps and is skipped with T; Tutorial is
# flag 0x40; Close Park and teardown; and (advisor-visuals) the envelope's square as a control, the head's origin, disc
# and root axes measured off the drawn nodes, the read box's wboxfill face, blue frame, depth order, Small.bff text in
# (48,48,48) centred on x 320, its inferred shadow and one framebuffer pixel. Every park runs the same 60; the floor
# sits just under it. A statement of its own for the same merge reason.
SCENES['advisor'] = ('AdvisorSmoke', 'ADVISOR SMOKE', 58)
# Sideshow native presentation: explicit UNPLACED real-DBA fixtures, not a player build tour.
# Positive/zero layouts, fixed authored controls despite flowing labels, canonical callbacks,
# hidden-field guards, native 1..1000 control edges and screen/subject/drag lifecycle.
# Receipts are draw arguments, not pixel review.
SCENES['sideshow-presentation'] = ('SideshowPresentationSmoke', 'SIDESHOW PRESENTATION SMOKE', 876)
SCENES['park-water'] = ('ParkWaterSmoke', 'PARK WATER SMOKE', 35)
SCENE_WITNESSES = {
    'park-water': (
        'shipping terrain load creates procedural water',
        'literal native placement for selected world and variant',
        'natural shipping process advances native water clock',
        'natural shipping wait changes actual uploaded heights',
        'natural shipping wait changes actual uploaded signed V',
        'shipping GPU-backed positions agree with native geometry and Z reflection',
        'shipping GPU-backed UVs agree with signed native narrowing',
        'shipping water stays still while paused',
        'real Close Park input opens lobby',
        'park water hidden in lobby',
        'normal Viewer teardown frees water owner',
        'normal Viewer teardown frees actual water instance',
        'real Pause key resumes shipping process',
        'shipping water resumes after input pause',
        'real filter toggle reaches procedural material',
        'real filter toggle restores procedural material',
        'hidden park drawable does not run behind lobby',
    ),
}



# Kept OUT of the ordinary eight-park scene cross-product: two initialized owners and
# explicit component data/quantum setup. A map-count exception is scoped to this named case.
STANDALONE_CASES = {
    'park-water-clock': {
        'scene': 'ParkWaterClockAudit', 'label': 'PARK WATER CLOCK',
        'minimum': 18, 'maps': (('JUNGLE', 'terrain_1'),),
        'user_args': ('--map=JUNGLE  terrain_1.mps', '--mode=park'),
        'resolutions': ('640x360', '1152x648'),
        'scope': 'declared public-engine TimeScale=0 control with normal Viewer and real Pause; not ordinary player/emulator proof',
        'numbered_checks': True,
        'witnesses': (
            'engine process delta actually becomes zero',
            'native wall clock advances despite zero engine delta',
            'native phase advances despite zero engine delta',
            'actual uploaded height moves despite zero engine delta',
            'actual uploaded V moves despite zero engine delta',
            'real Pause key still gates native clock',
            'wall time is discarded while input-paused',
            'clock-control Viewer retired normally',
            'clock-control actual water retired normally',
        ),
    },
    'procedural-water-component': {
        'scene': 'ProceduralParkWaterAudit', 'label': 'PROCEDURAL PARK WATER',
        'minimum': 28, 'maps': (), 'user_args': (),
        'resolutions': ('640x360', '1152x648'),
        'scope': 'declared native-water component with actual dynamic-mesh/raster-alpha controls; no player/emulator parity',
        'numbered_checks': True,
        'witnesses': (
            'actual uploaded heights move',
            'no horizontal geometry drift',
            'actual uploaded V advances signed interval',
            'pause does not advance/reupload',
            'paused zero-delta draw recovers oversized UV and updates actual mesh',
            'visible water over red underlay',
            'visible water over green underlay',
            'underlay survives through the actual alpha raster',
            'component water and fixture nodes retired',
        ),
    },
    'park-water-reset': {
        'scene': 'ParkWaterSmoke', 'label': 'PARK WATER RESET SMOKE',
        'user_args': ('--map=JUNGLE  terrain_1.mps', '--mode=park', '--water-reset-fixture'),
        'minimum': 57, 'maps': (('JUNGLE', 'terrain_1'), ('FANTASY', 'terrain_2')),
        'resolutions': ('640x360', '1152x648'),
        'scope': 'normal shipping Viewer plus declared direct ordinary-loader reset, real Pause/filter/Close Park input; no emulator parity',
        'numbered_checks': True,
        'witnesses': (
            'shipping terrain load creates procedural water',
            'map load creates fresh owner and initial UV/clock state',
            'old water hidden immediately before queued deletion',
            'old water instance freed on map switch',
            'exactly one live water drawable',
            'real Close Park input opens lobby',
            'park water hidden in lobby',
            'map load creates independent state with fresh native initial phase',
            *SCENE_WITNESSES['park-water'],
        ),
        'witness_counts': {
            'shipping terrain load creates procedural water': 2,
            'exactly one live water drawable': 2,
            'literal native placement for selected world and variant': 2,
            'natural shipping process advances native water clock': 2,
            'natural shipping wait changes actual uploaded heights': 2,
            'natural shipping wait changes actual uploaded signed V': 2,
            'shipping GPU-backed positions agree with native geometry and Z reflection': 2,
            'shipping GPU-backed UVs agree with signed native narrowing': 2,
        },
    },
    'research-persistence': {
        'scene': 'ResearchPersistenceSmoke', 'label': 'RESEARCH PERSISTENCE SMOKE',
        'user_args': ('--map=JUNGLE', '--mode=park'),
        'minimum': 24, 'maps': (('JUNGLE', 'terrain_1'), ('JUNGLE', 'terrain_1')),
        'resolutions': ('640x360', '1152x648'),
        'scope': 'explicit two-Viewer research-section component fixture; not full-world save/load or pixel proof',
    },
    'particle-child': {
        'scene': 'ParticleChildSpawnAudit', 'label': 'PARTICLE CHILD SPAWN',
        'minimum': 62, 'maps': (), 'user_args': (),
        'resolutions': ('640x360', '1152x648'),
        'scope': 'explicit rendered particle-child component fixtures; no Viewer/map/player or retail pixel proof',
        'numbered_checks': True,
        'witnesses': (
            'Destroy75 API returns parent and creates exactly parent plus 83',
            'Destroy76 API returns parent and creates exactly parent plus 83',
            'Destroy77 API returns parent and creates exactly parent plus 83',
            'Emit 60 introduces no child (including no LaserRing63 for 60/62)',
            'Emit 62 introduces no child (including no LaserRing63 for 60/62)',
            'CLONED DATA finite83 inherits neither parent persistent flag nor directional input',
            'CLONED DATA supported child83 self-link cannot recurse',
            'CLONED DATA finite parent and child retire through normal process/time',
            'Clear plus two real frames retires every finite/continuous node and lifecycle map',
            'holders queued and retired normally',
        ),
    },
    # The renderer's one-shot emission follows the native schedule (ParticleTemplate.Plan, ParticleBirthRuns).
    'particle-schedule': {
        'scene': 'ParticleScheduleAudit', 'label': 'PARTICLE SCHEDULE',
        'minimum': 18, 'maps': (), 'user_args': (),
        'resolutions': ('640x360', '1152x648'),
        'scope': 'explicit rendered native one-shot schedule component fixture; no Viewer/map/player or retail pixel proof',
        'numbered_checks': True,
        'witnesses': (
            'LaserRing63 every drawn particle lives 16..24 ticks, its own planned life',
            'LaserRing63 replacement starts once the first has died (one alive at a time, cap 1)',
            'CLONED DATA a plan with no births draws no parent but still spawns its Twinkle83 child',
            'Clear plus two real frames retires every node, parked runs included',
            'holders queued and retired normally',
        ),
    },
}


def case_environment(disc: Path, *, standalone: bool = False) -> dict:
    env = clean_environment(disc)  # removes inherited TPW_* overrides
    env.setdefault('LP_NUM_THREADS', '2')
    if not standalone:
        env['TPW_ALL_RESEARCHED'] = '1'  # ordinary build-menu smokes retain their opt-in
    return env


def standalone_runs(case: str) -> tuple[str, ...]:
    return STANDALONE_CASES[case]['resolutions']


def standalone_command(case: str, resolution: str, engine: Path, xvfb: str, disc: Path) -> list[str]:
    if resolution not in standalone_runs(case):
        raise ValueError(f'not a resolution for {case}: {resolution}')
    return [xvfb, '-a', str(engine), '--rendering-method', 'gl_compatibility',
            '--audio-driver', 'Dummy', '--resolution', resolution, '--path', 'game',
            f'res://tests/{STANDALONE_CASES[case]["scene"]}.tscn', '--',
            f'--disc={disc}', *STANDALONE_CASES[case]['user_args']]


def validate_standalone_launch(case: str, command: list[str], env: dict, disc: Path, *, resolution: str) -> None:
    # Validate the actual launch command, not a disconnected test-only recipe.
    if case not in STANDALONE_CASES:
        raise ValueError(f'no launch contract for {case}')
    if resolution not in standalone_runs(case):
        raise ValueError(f'not a registered standalone resolution: {resolution}')
    for flag, value in (('--resolution', resolution), ('--rendering-method', 'gl_compatibility'),
                        ('--audio-driver', 'Dummy'), ('--path', 'game')):
        if command.count(flag) != 1 or command.index(flag) + 1 >= len(command) or command[command.index(flag) + 1] != value:
            raise ValueError(f'standalone launch contract mismatch: {flag}')
    scene = f'res://tests/{STANDALONE_CASES[case]["scene"]}.tscn'
    if command.count(scene) != 1:
        raise ValueError('standalone launch has the wrong scene')
    if command.count('--') != 1:
        raise ValueError('standalone launch needs exactly one user-argument separator')
    user = command[command.index('--') + 1:]
    required = [f'--disc={disc}', *STANDALONE_CASES[case]['user_args']]
    if sorted(user) != sorted(required):
        raise ValueError(f'{case} requires exactly its registered user arguments')
    if 'TPW_ALL_RESEARCHED' in env or '--all-researched' in command or '--headless' in command:
        raise ValueError(f'{case} must render without the all-researched override')


def classify_standalone(case: str, run: dict) -> dict:
    spec = STANDALONE_CASES[case]
    text = run['text']
    failures = [line.strip() for line in text.splitlines() if ERROR.search(line.strip())]
    result = {'status': 'pass', 'failures': failures, 'case': case, 'scope': spec['scope']}
    for key, status in (('launch_error', 'launch_error'), ('timed_out', 'timeout'), ('truncated', 'truncated_log')):
        if run.get(key): return {**result, 'status': status}
    if run.get('raw_exit') != 0: return {**result, 'status': 'nonzero_exit'}
    if failures: return {**result, 'status': 'error_output'}
    loaded = MAP_LINE.findall(text)
    result['loaded'] = [list(pair) for pair in loaded]
    # Also count malformed/unexpected map lines, so an extra terrain_3 witness cannot disappear.
    map_lines = [line for line in text.splitlines() if line.startswith('[map] loaded ')]
    if not map_lines and spec['maps']: return {**result, 'status': 'no_map_witness'}
    if len(map_lines) != len(spec['maps']): return {**result, 'status': 'map_witness_count'}
    if tuple(loaded) != spec['maps']: return {**result, 'status': 'wrong_map'}
    pass_lines = [line for line in text.splitlines() if line.startswith(spec['label'] + ' PASS')]
    passes = re.findall(r'^' + re.escape(spec['label']) + r' PASS checks=(\d+);', text, re.M)
    if len(pass_lines) != 1 or len(passes) != 1: return {**result, 'status': 'missing_pass_witness'}
    result['checks'] = int(passes[0])
    if result['checks'] < spec['minimum']: return {**result, 'status': 'missing_coverage'}
    if spec.get('numbered_checks'):
        prefix = spec['label'] + ' ok:'
        rows = [line for line in text.splitlines() if line.startswith(prefix)]
        matches = [re.fullmatch(re.escape(prefix) + r' \[(\d+)\] (.+)', line) for line in rows]
        ids = [int(m[1]) for m in matches if m]
        if len(ids) != len(rows) or ids != list(range(1, result['checks'] + 1)):
            return {**result, 'status': 'missing_coverage'}
        receipts = [m[2] for m in matches]
        if (any(w not in receipts for w in spec.get('witnesses', ()))
                or any(receipts.count(w) != n for w, n in spec.get('witness_counts', {}).items())):
            return {**result, 'status': 'missing_coverage'}
    return result


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
    map_lines = [line for line in text.splitlines() if line.startswith('[map] loaded ')]
    if not map_lines: return {**result, 'status': 'no_map_witness'}
    if len(map_lines) != 1: return {**result, 'status': 'ambiguous_map_witness'}
    if loaded != [(world, f'terrain_{terrain}')]: return {**result, 'status': 'wrong_map'}
    passes = re.findall(r'^' + re.escape(label) + r' PASS checks=(\d+);', text, re.M)
    if len(passes) != 1: return {**result, 'status': 'missing_pass_witness'}
    result['checks'] = int(passes[0])
    if result['checks'] < minimum: return {**result, 'status': 'missing_coverage'}
    if scene in SCENE_WITNESSES:
        prefix = label + ' ok:'
        rows = [line for line in text.splitlines() if line.startswith(prefix)]
        matches = [re.fullmatch(re.escape(prefix) + r' \[(\d+)\] (.+)', line) for line in rows]
        ids = [int(match[1]) for match in matches if match]
        receipts = [match[2] for match in matches if match]
        if len(ids) != len(rows) or ids != list(range(1, result['checks'] + 1)):
            return {**result, 'status': 'missing_coverage'}
        if any(witness not in receipts for witness in SCENE_WITNESSES[scene]):
            return {**result, 'status': 'missing_coverage'}
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
    parser.add_argument('--standalone-case', choices=STANDALONE_CASES,
                        help='separate named fixture; cannot be mixed with --scenes/--parks')
    parser.add_argument('--scenes', nargs='+', choices=SCENES)
    parser.add_argument('--parks', nargs='+',
                        help='WORLD/1 or WORLD/2 (default: all eight)')
    args = parser.parse_args(argv)
    if args.standalone_case and (args.scenes is not None or args.parks is not None):
        parser.error('--standalone-case cannot be combined with --scenes or --parks')
    if not args.disc.is_file() or not args.godot.is_file(): parser.error('disc and Godot must be existing files')
    if not 0 < args.timeout <= 3600: parser.error('timeout must be positive and at most 3600 seconds')
    parks = []
    for text in dict.fromkeys(args.parks or ([] if args.standalone_case else [f'{w}/{t}' for w, t in PARKS])):
        match = re.fullmatch(r'([A-Z]+)/([12])', text)
        if not match or match.group(1) not in WORLDS: parser.error(f'not a park: {text}')
        parks.append((match.group(1), int(match.group(2))))
    repo, disc, engine = args.repo.resolve(), args.disc.resolve(), args.godot.resolve()
    try: out = fresh_output(args.out)
    except (OSError, ValueError) as ex: parser.error(str(ex))
    selected = [args.standalone_case] if args.standalone_case else list(dict.fromkeys(args.scenes or SCENES))
    manifest = {'schema_version': 1, 'scope': 'rendered normal-startup smokes; per-case loaded-map proof; not visual inspection',
                'selected': selected, 'parks': [f'{w}/{t}' for w, t in parks], 'status': 'running', 'results': []}
    if args.standalone_case:
        manifest.update(scope=STANDALONE_CASES[args.standalone_case]['scope'],
                        standalone_case=args.standalone_case,
                        expected_maps=[list(pair) for pair in STANDALONE_CASES[args.standalone_case]['maps']],
                        resolutions=list(standalone_runs(args.standalone_case)), all_researched=False)
    path = out / 'manifest.json'

    def save():
        temp = path.with_suffix('.json.tmp')
        temp.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
        temp.replace(path)

    def finish(status, code=1):
        manifest.update(status=status, runner_exit=code); save()
        print(f'{status}; runner_exit={code}; {path}', flush=True)
        return code

    env = case_environment(disc, standalone=args.standalone_case is not None)

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
        if args.standalone_case:
            case = args.standalone_case
            for resolution in standalone_runs(case):
                if output_snapshot(repo) != manifest['assemblies']: return finish('assemblies_changed_during_run')
                command = standalone_command(case, resolution, engine, args.xvfb, disc)
                validate_standalone_launch(case, command, env, disc, resolution=resolution)
                name = f'{case}-{resolution}'
                run, record = execute(name, command)
                record.update(classify_standalone(case, run), resolution=resolution)
                manifest['results'].append(record); save()
                print(f'{name}: {record["status"]} checks={record.get("checks")} loaded={record.get("loaded")}', flush=True)
        for world, terrain in parks:
            for scene in selected:
                if output_snapshot(repo) != manifest['assemblies']: return finish('assemblies_changed_during_run')
                name = f'{world}-{terrain}-{scene}'
                run, record = execute(name, [args.xvfb, '-a', str(engine), '--rendering-method', 'gl_compatibility',
                                             '--audio-driver', 'Dummy', '--resolution', '640x360', '--path', 'game',
                                             f'res://tests/{SCENES[scene][0]}.tscn', '--',
                                             map_argument(world, terrain), '--mode=park']
                                            + (['--laptop-park-open'] if scene in PARK_OPEN_SCENES else [])
                                            + (['--seaplane-ferry'] if scene in VEHICLE_SCENES else []))
                record.update(classify(scene, world, terrain, run), scene=scene)
                manifest['results'].append(record); save()
                print(f'{name}: {record["status"]} checks={record.get("checks")} loaded={record.get("loaded")}', flush=True)
        if output_snapshot(repo) != manifest['assemblies']: return finish('assemblies_changed_during_run')
        if source_snapshot(repo) != manifest['source_before']: return finish('source_changed_during_run')
        if any(r['status'] != 'pass' for r in manifest['results']): return finish('failed_cases')
        full = not args.standalone_case and set(selected) == set(SCENES) and set(parks) == set(PARKS)
        return finish('all_cases_passed' if full else 'selected_cases_passed', 0)
    except (OSError, ValueError, subprocess.SubprocessError) as ex:
        manifest['runner_error'] = f'{type(ex).__name__}: {ex}'
        return finish('runner_error')


if __name__ == '__main__':
    raise SystemExit(main())
