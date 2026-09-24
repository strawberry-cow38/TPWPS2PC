"""Runtime runner failure controls; no disc, engine or network required."""
import contextlib
import io
import json
import itertools
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

import runtime_audit as audit


def output(text='', **extra):
    return dict(raw_exit=0, text=text, timed_out=False, truncated=False, launch_error=None, **extra)


def raw_witness(scene):
    if scene in ('visitor', 'rse'):
        name = 'VISITOR GEOMETRY' if scene == 'visitor' else 'RSE ANIMATION'
        return '\n'.join([f'{name} PASS: summary'] + [f'{name} PASS {case}: tested' for case in audit.CASES])
    if scene == 'texture': return 'VIEWER CLOCK PASS: tested\nTEXTURE BINDING PASS: 146 surface checks across five models / four worlds'
    if scene == 'mtr': return 'MTR SURFACES PASS: all four named mesh/material/texture witnesses, geometry and transforms'
    if scene == 'advisor':
        return '\n'.join(['ADVISOR BROWSER ok: tested'] * 41 + [
            'ADVISOR BROWSER ok: usa/French exact subtitle tested',
            'ADVISOR BROWSER ok: jap/German exact subtitle tested',
            'ADVISOR BROWSER ok: teardown begins with speech still active',
            'ADVISOR BROWSER ok: all replaced/active speech streams retire',
            'ADVISOR BROWSER PASS: 45 checks, 0 failures'])
    if scene == 'shops':
        return '\n'.join(['COMPILED SHOP VIEWER ok: tested'] * 12 +
            [f'COMPILED SHOP VIEWER ok: {world} live definition consumes compiled happiness10'
             for world in ('JUNGLE', 'HALLOW', 'FANTASY', 'SPACE')] + [
             'COMPILED SHOP VIEWER ok: re-index replaces the catalogue',
             'COMPILED SHOP VIEWER ok: re-index does not retain SPACE',
             'COMPILED SHOP VIEWER PASS: 18 checks, 0 failures'])
    if scene == 'standing':
        return '\n'.join(['STANDING SERVICE ok: tested'] * 62 + [
            'STANDING SERVICE ok: real script accepts customer before satisfaction',
            'STANDING SERVICE ok: explicit host hiding suppresses standing body',
            "STANDING SERVICE ok: same numeric ride ID does not inherit removed owner's customer",
            'STANDING SERVICE ok: satisfied guest clears visible toilet thought',
            *[f'STANDING SERVICE ok: placed quarter turn {turn} completes real relief without reseeding' for turn in range(4)],
            *[f'STANDING SERVICE ok: placed quarter turn {turn} authored stand is nearer its real stub than its mirror' for turn in range(4)],
            'STANDING SERVICE PASS: 74 checks, 0 failures'])
    return '\n'.join(['AUDIO LIFECYCLE ok: tested'] * 31 + [
        'AUDIO LIFECYCLE ok: 2D eight fast no-evidence polls remain pending',
        'AUDIO LIFECYCLE ok: 3D eight fast no-evidence polls remain pending',
        'AUDIO LIFECYCLE ok: actual world reset stops old voices',
        'AUDIO LIFECYCLE ok: world reset retains the reusable global particle-library holder',
        'AUDIO LIFECYCLE ok: world reset clears old live particles',
        'AUDIO LIFECYCLE ok: mixer retires stopped playback references', 'AUDIO LIFECYCLE PASS'])


def witness(scene):
    text = raw_witness(scene)
    if scene in ('advisor', 'audio', 'standing', 'shops'):
        number = 0
        lines = []
        for line in text.splitlines():
            if ' ok: ' in line:
                number += 1
                line = line.replace(' ok: ', f' ok: [{number}] ', 1)
            lines.append(line)
        return '\n'.join(lines)
    return text


class Classification(unittest.TestCase):
    def test_all_scenes_require_their_own_witnesses(self):
        for scene in audit.SCENES:
            with self.subTest(scene=scene):
                self.assertEqual(audit.classify(scene, output(witness(scene)))['status'], 'pass')
                self.assertNotEqual(audit.classify(scene, output('PASS'))['status'], 'pass')

    def test_transport_failures_override_green_output(self):
        for key, value, expected in [('raw_exit', 2, 'nonzero_exit'), ('raw_exit', None, 'nonzero_exit'),
                                     ('timed_out', True, 'timeout'), ('truncated', True, 'truncated_log'),
                                     ('launch_error', 'OSError', 'launch_error')]:
            run = output(witness('audio')); run[key] = value
            self.assertEqual(audit.classify('audio', run)['status'], expected)

    def test_errors_after_pass_override_success(self):
        for error in ['ERROR: failed later', 'SCRIPT ERROR: failed', 'AUDIO LIFECYCLE FAIL: x',
                      'WARNING: ObjectDB instances leaked at exit', 'ERROR: 1 resources still in use']:
            self.assertEqual(audit.classify('audio', output(witness('audio') + '\n' + error))['status'], 'error_output')

    def test_ordinary_warning_is_recorded_not_hidden(self):
        result = audit.classify('audio', output(witness('audio') + '\nWARNING: fixture warning'))
        self.assertEqual(result['status'], 'pass'); self.assertEqual(len(result['warnings']), 1)

    def test_every_visitor_world_terrain_required(self):
        for scene in ('visitor', 'rse'):
            for case in audit.CASES:
                text = '\n'.join(line for line in witness(scene).splitlines() if case + ':' not in line)
                self.assertEqual(audit.classify(scene, output(text))['status'], 'missing_coverage')

    def test_duplicate_world_cannot_replace_missing_world(self):
        self.assertEqual(audit.classify('visitor', output(witness('visitor').replace('SPACE/2:', 'SPACE/1:')))['status'], 'missing_coverage')

    def test_texture_needs_count_and_clock(self):
        for text in [witness('texture').replace('146', '145'), witness('texture').splitlines()[1]]:
            self.assertEqual(audit.classify('texture', output(text))['status'], 'missing_coverage')

    def test_advisor_count_must_match_summary_and_minimum(self):
        for text in [witness('advisor').replace('45 checks', '44 checks'), witness('advisor').replace('ADVISOR BROWSER ok: [1] tested\n', '', 1)]:
            self.assertIn(audit.classify('advisor', output(text))['status'], ('missing_coverage', 'duplicate_or_missing_assertion_ids'))

    def test_named_coverage_cannot_be_replaced_by_dummy_checks(self):
        for scene, old in [('advisor', 'usa/French exact subtitle'), ('audio', 'actual world reset stops old voices')]:
            self.assertEqual(audit.classify(scene, output(witness(scene).replace(old, 'unrelated control')))['status'], 'missing_coverage')

    def test_audio_minimum_and_duplicate_pass(self):
        for text in [witness('audio').replace('AUDIO LIFECYCLE ok: [1] tested\n', '', 1), witness('audio') + '\nAUDIO LIFECYCLE PASS']:
            self.assertIn(audit.classify('audio', output(text))['status'], ('missing_coverage', 'duplicate_or_missing_assertion_ids'))

    def test_duplicate_assertion_line_cannot_replace_another_check(self):
        for scene in ('advisor', 'audio', 'standing', 'shops'):
            text = witness(scene).replace('ok: [2] tested', 'ok: [1] tested')
            self.assertEqual(audit.classify(scene, output(text))['status'], 'duplicate_or_missing_assertion_ids')

    def test_shops_require_each_live_world_and_matching_count(self):
        text = witness('shops')
        for damaged in [text.replace('18 checks', '17 checks'),
                        text.replace('SPACE live definition consumes compiled happiness10', 'unrelated assertion'),
                        text.replace('re-index replaces the catalogue', 'unrelated assertion')]:
            self.assertEqual(audit.classify('shops', output(damaged))['status'], 'missing_coverage')

    def test_standing_requires_actual_placement_lifecycle_and_matching_count(self):
        text = witness('standing')
        for damaged in [text.replace('74 checks', '73 checks'),
                        text.replace('placed quarter turn 2 completes real relief without reseeding', 'unrelated assertion'),
                        text.replace('explicit host hiding suppresses standing body', 'unrelated assertion')]:
            self.assertEqual(audit.classify('standing', output(damaged))['status'], 'missing_coverage')

    def test_source_snapshot_includes_ignored_and_linked_sources(self):
        with tempfile.TemporaryDirectory(dir='/tmp' if os.name == 'posix' else None) as temp:
            repo = Path(temp)
            for name in ['game/TPWPS2Viewer.csproj', 'game/Extra.cs', 'tools/TPW.PS2.VisitorAudit/VisitorExpectations.cs', 'game/obj/Generated.cs']:
                path = repo / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text('source')
            (repo / '.gitignore').write_text('game/Extra.cs\n')
            with mock.patch.object(audit, 'git_info', return_value={'revision': 'x', 'dirty': False}):
                first = audit.source_snapshot(repo)
                (repo / 'game/Extra.cs').write_text('changed')
                second = audit.source_snapshot(repo)
            self.assertIn('game/Extra.cs', first['files'])
            self.assertIn('tools/TPW.PS2.VisitorAudit/VisitorExpectations.cs', first['files'])
            self.assertNotIn('game/obj/Generated.cs', first['files'])
            self.assertNotEqual(first['source_sha256'], second['source_sha256'])

    def test_output_snapshot_requires_core_and_hashes_dependencies(self):
        with tempfile.TemporaryDirectory(dir='/tmp' if os.name == 'posix' else None) as temp:
            repo = Path(temp); output = repo / audit.OUTPUT; output.mkdir(parents=True)
            (output / 'TPWPS2Viewer.dll').write_bytes(b'viewer')
            with self.assertRaises(ValueError): audit.output_snapshot(repo)
            (output / 'TPW.PS2.Data.dll').write_bytes(b'core')
            (output / 'TPWPS2Viewer.deps.json').write_text('{}')
            first = audit.output_snapshot(repo)
            (output / 'TPWPS2Viewer.deps.json').write_text('{"changed":true}')
            self.assertNotEqual(first, audit.output_snapshot(repo))

    def test_executing_runner_provenance_is_not_the_repo_argument(self):
        snapshot = audit.runner_snapshot()
        self.assertIn(str(Path(audit.__file__).resolve()), snapshot)
        self.assertIn(str(Path(audit.audit_matrix.__file__).resolve()), snapshot)

    def test_engine_probe_requires_mono(self):
        self.assertEqual(audit.engine_version('4.6.stable.mono.official.89cea1439\n'), '4.6.stable.mono.official.89cea1439')
        self.assertIsNone(audit.engine_version('4.6.stable.official.89cea1439'))
        self.assertIsNone(audit.engine_version('4.6.stable.notmono.official'))
        self.assertIsNone(audit.engine_version('4.6.stable.mono.x\n4.7.stable.mono.x'))

    def test_environment_clears_capture_and_test_switches(self):
        env = audit.clean_environment(Path('/disc'), {'TPW_PS2_SHOT': '/bad', 'TPW_WANT_SHOT': '/bad2', 'TPW_PS2_CULL': 'off', 'PATH': '/bin'})
        self.assertEqual(env, {'PATH': '/bin', 'TPW_PS2_DISC': '/disc', 'LIBGL_ALWAYS_SOFTWARE': '1'})

    def test_output_refuses_existing_or_git_ancestry(self):
        with tempfile.TemporaryDirectory(dir='/tmp' if os.name == 'posix' else None) as temp:
            root = Path(temp); out = root / 'evidence'
            self.assertEqual(audit.fresh_output(out), out)
            with self.assertRaises(FileExistsError): audit.fresh_output(out)
            (root / '.git').write_text('gitdir: elsewhere')
            with self.assertRaises(ValueError): audit.fresh_output(root / 'new')

    def test_matrix_process_accepts_explicit_environment(self):
        with tempfile.TemporaryDirectory(dir='/tmp' if os.name == 'posix' else None) as temp:
            result = audit.run_process([sys.executable, '-c', 'import os; print(os.environ["TPW_FIXTURE"])'],
                repo=Path(temp), log=Path(temp) / 'process.log', timeout=5, environment={'TPW_FIXTURE': 'isolated'})
            self.assertEqual(result['text'].strip(), 'isolated'); self.assertEqual(result['raw_exit'], 0)


class MainControls(unittest.TestCase):
    def exercise(self, *, bad_build=False, mutate_source=False, mutate_binary=False, bad_scene=False,
                 full=False, end_source=False, end_binary=False, invalid_engine=False, transport=None):
        with tempfile.TemporaryDirectory(dir='/tmp' if os.name == 'posix' else None) as temp:
            root = Path(temp); disc = root / 'disc.bin'; engine = root / 'godot'; out = root / 'evidence'
            disc.write_bytes(b'disc'); engine.write_bytes(b'engine')
            calls = []
            def fake(command, **kw):
                calls.append(command)
                if '--version' in command: text = ('4.6.stable.official.x' if invalid_engine else '4.6.stable.mono.official.x') if command[0] == str(engine) else '8.0.0'
                elif 'build' in command: text = 'Build failed.' if bad_build else 'Build succeeded.'
                else:
                    scene = next(key for key, name in audit.SCENES.items() if command[-1].endswith('/' + name + '.tscn'))
                    text = witness(scene) + ('\nERROR: runtime fault' if bad_scene else '')
                kw['log'].write_text(text)
                result = dict(output(text), command=command, log=str(kw['log']))
                if '--headless' in command and transport: result[transport] = True
                return result
            source = {'revision': 'abc', 'dirty': True, 'files': {}, 'source_sha256': 'same'}
            snapshots = ([source, {**source, 'source_sha256': 'changed'}] if mutate_source else
                         [source, source, {**source, 'source_sha256': 'changed'}] if end_source else itertools.repeat(source))
            assemblies = ([{'core.dll': 'a'}, {'core.dll': 'b'}] if mutate_binary else
                          [{'core.dll': 'a'}, {'core.dll': 'a'}, {'core.dll': 'b'}] if end_binary else itertools.repeat({'core.dll': 'a'}))
            with mock.patch.object(audit, 'source_snapshot', side_effect=snapshots), mock.patch.object(audit, 'output_snapshot', side_effect=assemblies), mock.patch.object(audit, 'run_process', side_effect=fake), contextlib.redirect_stdout(io.StringIO()):
                code = audit.main(['--disc', str(disc), '--godot', str(engine), '--out', str(out), '--repo', str(root), '--scenes', *(list(audit.SCENES) if full else ['audio', 'audio'])])
            return code, json.loads((out / 'manifest.json').read_text()), calls

    def test_default_full_suite_is_distinguished_from_subset(self):
        code, manifest, _ = self.exercise(full=True)
        self.assertEqual(code, 0); self.assertEqual(manifest['status'], 'all_scenes_passed')
        self.assertTrue(manifest['full_gate_passed']); self.assertEqual(len(manifest['results']), len(audit.SCENES))

    def test_invalid_engine_never_builds_or_runs_scenes(self):
        code, manifest, calls = self.exercise(invalid_engine=True)
        self.assertEqual(code, 1); self.assertEqual(manifest['status'], 'invalid_engine')
        self.assertFalse(any('build' in command or '--headless' in command for command in calls))

    def test_final_source_and_binary_drift_are_failures(self):
        for options, status in [({'end_source': True}, 'source_changed_during_run'),
                                ({'end_binary': True}, 'assemblies_changed_during_run')]:
            code, manifest, _ = self.exercise(**options)
            self.assertEqual(code, 1); self.assertEqual(manifest['status'], status)
            self.assertFalse(manifest['full_gate_passed'])

    def test_timeout_and_truncation_survive_orchestration(self):
        for flag, status in [('timed_out', 'timeout'), ('truncated', 'truncated_log')]:
            code, manifest, _ = self.exercise(transport=flag)
            self.assertEqual(code, 1); self.assertEqual(manifest['results'][0]['status'], status)

    def test_success_builds_once_deduplicates_and_records_provenance(self):
        code, manifest, calls = self.exercise()
        self.assertEqual(code, 0); self.assertEqual(manifest['status'], 'selected_scenes_passed'); self.assertFalse(manifest['full_gate_passed'])
        self.assertEqual(manifest['selected'], ['audio']); self.assertFalse(manifest['build_skipped'])
        self.assertIn('-t:Rebuild', next(c for c in calls if 'build' in c))
        self.assertEqual(manifest['source_before'], manifest['source_after'])
        self.assertEqual(len(manifest['results']), 1); self.assertIn('log_sha256', manifest['results'][0])

    def test_failed_build_never_runs_scene(self):
        code, manifest, calls = self.exercise(bad_build=True)
        self.assertEqual(code, 1); self.assertEqual(manifest['status'], 'build_failed')
        self.assertFalse(any('--headless' in c for c in calls))

    def test_source_change_refuses_build_provenance(self):
        code, manifest, _ = self.exercise(mutate_source=True)
        self.assertEqual(code, 1); self.assertEqual(manifest['status'], 'source_changed_during_build')

    def test_dependency_change_refuses_scene(self):
        code, manifest, calls = self.exercise(mutate_binary=True)
        self.assertEqual(code, 1); self.assertEqual(manifest['status'], 'assemblies_changed_during_run')
        self.assertFalse(any('--headless' in c for c in calls))

    def test_scene_error_is_saved_even_with_zero_exit(self):
        code, manifest, _ = self.exercise(bad_scene=True)
        self.assertEqual(code, 1); self.assertEqual(manifest['status'], 'failed_scenes')
        self.assertEqual(manifest['results'][0]['status'], 'error_output')


if __name__ == '__main__':
    unittest.main()
