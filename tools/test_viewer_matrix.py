"""Viewer matrix classification controls; no disc, engine or display required."""
import contextlib
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import viewer_matrix as vm


def output(text='', **extra):
    return {**dict(raw_exit=0, text=text, timed_out=False, truncated=False, launch_error=None), **extra}


def good(scene='entrance', world='JUNGLE', terrain=2, checks=None):
    _, label, minimum = vm.SCENES[scene]
    return '\n'.join([
        f'[map] loaded world={world} terrain=terrain_{terrain}.mps',
        '[bus] boundary: UNPORTED entrance-group queues ... Zero inputs BYPASS the backlog reduction',
        f'{label} PASS checks={checks if checks is not None else minimum}; actual bus/placement/queues'])


class MapArgument(unittest.TestCase):
    def test_full_label_with_two_spaces(self):
        self.assertEqual(vm.map_argument('JUNGLE', 2), '--map=JUNGLE  terrain_2.mps')

    def test_regression_old_single_space_form_is_never_produced(self):
        # 2026-09-25: `--map=JUNGLE 2` matched no label and every park-2 case ran FANTASY-1.
        for world, terrain in vm.PARKS:
            argument = vm.map_argument(world, terrain)
            self.assertNotRegex(argument, r'^--map=[A-Z]+ [12]$')
            self.assertIn(f'{world}  terrain_{terrain}.mps', argument)

    def test_all_eight_parks_are_distinct(self):
        self.assertEqual(len({vm.map_argument(w, t) for w, t in vm.PARKS}), 8)

    def test_unknown_park_is_refused(self):
        with self.assertRaises(ValueError): vm.map_argument('NOWHERE', 1)
        with self.assertRaises(ValueError): vm.map_argument('JUNGLE', 3)


class Classify(unittest.TestCase):
    def test_good_case_passes_and_records_what_loaded(self):
        result = vm.classify('entrance', 'JUNGLE', 2, output(good()))
        self.assertEqual(result['status'], 'pass')
        self.assertEqual(result['loaded'], [['JUNGLE', 'terrain_2']])
        self.assertEqual(result['checks'], 651)

    def test_regression_wrong_park_fails_even_though_the_scene_passed(self):
        # The exact 2026-09-25 shape: asked for JUNGLE-2, loaded FANTASY-1, scene PASS.
        result = vm.classify('entrance', 'JUNGLE', 2, output(good(world='FANTASY', terrain=1)))
        self.assertEqual(result['status'], 'wrong_map')

    def test_missing_map_witness_fails(self):
        text = '\n'.join(line for line in good().splitlines() if not line.startswith('[map]'))
        self.assertEqual(vm.classify('entrance', 'JUNGLE', 2, output(text))['status'], 'no_map_witness')

    def test_two_map_witnesses_are_ambiguous(self):
        text = good() + '\n[map] loaded world=JUNGLE terrain=terrain_2.mps'
        self.assertEqual(vm.classify('entrance', 'JUNGLE', 2, output(text))['status'], 'ambiguous_map_witness')

    def test_regression_bypass_is_not_a_pass(self):
        # The old runner used `'PASS' in line`, which accepted "... BYPASS ..." witnesses.
        text = '\n'.join(line for line in good().splitlines() if ' SMOKE PASS ' not in line)
        self.assertIn('BYPASS', text)
        self.assertEqual(vm.classify('entrance', 'JUNGLE', 2, output(text))['status'], 'missing_pass_witness')

    def test_another_scenes_pass_line_does_not_count(self):
        text = good(scene='rejected')
        self.assertEqual(vm.classify('entrance', 'JUNGLE', 2, output(text))['status'], 'missing_pass_witness')

    def test_below_minimum_is_missing_coverage(self):
        text = good(checks=650)
        self.assertEqual(vm.classify('entrance', 'JUNGLE', 2, output(text))['status'], 'missing_coverage')

    def test_nonzero_exit_and_error_output_fail_first(self):
        self.assertEqual(vm.classify('entrance', 'JUNGLE', 2, output(good(), raw_exit=2))['status'], 'nonzero_exit')
        self.assertEqual(vm.classify('entrance', 'JUNGLE', 2, output(good() + '\nERROR: boom'))['status'], 'error_output')
        self.assertEqual(vm.classify('entrance', 'JUNGLE', 2, output(good(), timed_out=True))['status'], 'timeout')

    def test_smoke_fail_line_is_error_output(self):
        text = good() + '\nNATIVE ENTRANCE FLOW SMOKE FAIL checks=3: boom'
        self.assertEqual(vm.classify('entrance', 'JUNGLE', 2, output(text))['status'], 'error_output')


class SideshowPresentationRegression(unittest.TestCase):
    def test_registered_fixture_and_independent_full_coverage_floor(self):
        self.assertEqual(vm.SCENES['sideshow-presentation'],
                         ('SideshowPresentationSmoke', 'SIDESHOW PRESENTATION SMOKE', 876))
        self.assertNotIn('sideshow-presentation', vm.PARK_OPEN_SCENES)

    def test_literal_complete_fixture_passes_and_missing_checks_do_not(self):
        text = ('[map] loaded world=JUNGLE terrain=terrain_1.mps\n'
                'SIDESHOW PRESENTATION SMOKE PASS checks=876; explicit unplaced integration fixtures')
        self.assertEqual(vm.classify('sideshow-presentation','JUNGLE',1,output(text))['status'],'pass')
        self.assertEqual(vm.classify('sideshow-presentation','JUNGLE',1,output(text.replace('checks=876','checks=875')))['status'],'missing_coverage')

    def test_fixture_failure_is_not_hidden_by_exit_zero_or_another_scenes_pass(self):
        text=good('sideshow-presentation','JUNGLE',1,checks=876)
        self.assertEqual(vm.classify('sideshow-presentation','JUNGLE',1,
            output(text+'\nSIDESHOW PRESENTATION SMOKE FAIL: fixed price arrows moved'))['status'],'error_output')
        self.assertEqual(vm.classify('sideshow-presentation','JUNGLE',1,
            output(good('management','JUNGLE',1,checks=876)))['status'],'missing_pass_witness')


class ResearchPersistenceStandalone(unittest.TestCase):
    CASE = 'research-persistence'
    MAP = '[map] loaded world=JUNGLE terrain=terrain_1.mps'
    PASS = 'RESEARCH PERSISTENCE SMOKE PASS checks=24; explicit client component fixture'

    def good(self):
        return self.MAP + '\n' + self.MAP + '\n' + self.PASS

    def classify(self, text=None, **extra):
        return vm.classify_standalone(self.CASE, output(self.good() if text is None else text, **extra))

    def test_registered_separately_with_literal_complete_contract(self):
        self.assertNotIn(self.CASE, vm.SCENES)
        spec = vm.STANDALONE_CASES[self.CASE]
        self.assertEqual(spec['scene'], 'ResearchPersistenceSmoke')
        self.assertEqual(spec['minimum'], 24)
        self.assertEqual(spec['maps'], (('JUNGLE', 'terrain_1'),) * 2)
        self.assertEqual(vm.standalone_runs(self.CASE), ('640x360', '1152x648'))
        result = self.classify()
        self.assertEqual(result['status'], 'pass')
        self.assertEqual(result['loaded'], [['JUNGLE', 'terrain_1']] * 2)
        self.assertEqual(result['checks'], 24)
        self.assertIn('not full-world', result['scope'])

    def test_missing_extra_wrong_and_malformed_maps_are_rejected(self):
        for text, status in [(self.PASS, 'no_map_witness'),
                             (self.MAP + '\n' + self.PASS, 'map_witness_count'),
                             (self.good() + '\n' + self.MAP, 'map_witness_count'),
                             (self.good() + '\n[map] loaded world=JUNGLE terrain=terrain_3.mps', 'map_witness_count')]:
            with self.subTest(text=text): self.assertEqual(self.classify(text)['status'], status)
        for first, second in [(self.MAP, '[map] loaded world=SPACE terrain=terrain_1.mps'),
                              ('[map] loaded world=FANTASY terrain=terrain_1.mps', self.MAP),
                              (self.MAP, '[map] loaded world=JUNGLE terrain=terrain_2.mps'),
                              (self.MAP, '[map] loaded world=JUNGLE terrain=terrain_3.mps')]:
            self.assertEqual(self.classify(first + '\n' + second + '\n' + self.PASS)['status'], 'wrong_map')

    def test_pass_count_shape_label_and_floor_cannot_be_forged(self):
        for replacement in ['', self.PASS + '\n' + self.PASS,
                            'MANAGEMENT SMOKE PASS checks=24;',
                            'RESEARCH PERSISTENCE SMOKE BYPASS checks=24;',
                            self.PASS.replace('checks=24', 'checks=unknown'),
                            self.PASS + '\nRESEARCH PERSISTENCE SMOKE PASS checks=bad;']:
            self.assertEqual(self.classify(self.good().replace(self.PASS, replacement))['status'], 'missing_pass_witness')
        self.assertEqual(self.classify(self.good().replace('checks=24', 'checks=23'))['status'], 'missing_coverage')

    def test_failure_exit_and_cleanup_do_not_hide_behind_pass(self):
        for failure in ['RESEARCH PERSISTENCE SMOKE FAIL: load skipped',
                        'RESEARCH PERSISTENCE SMOKE cleanup FAIL: node still alive',
                        'ERROR: resources still in use at exit']:
            self.assertEqual(self.classify(self.good() + '\n' + failure)['status'], 'error_output')
        for extra, status in [({'raw_exit': 1}, 'nonzero_exit'), ({'timed_out': True}, 'timeout'),
                              ({'truncated': True}, 'truncated_log'), ({'launch_error': 'absent'}, 'launch_error')]:
            self.assertEqual(self.classify(**extra)['status'], status)

    def test_ordinary_one_map_validation_remains_strict(self):
        for world, terrain in vm.PARKS:
            text = good('pointer', world, terrain)
            self.assertEqual(vm.classify('pointer', world, terrain, output(text))['status'], 'pass')
            text += f'\n[map] loaded world={world} terrain=terrain_{terrain}.mps'
            self.assertEqual(vm.classify('pointer', world, terrain, output(text))['status'], 'ambiguous_map_witness')

    def test_actual_launch_contract_and_inherited_override_isolation(self):
        disc = Path('/authorized/local/disc.bin')
        with patch.dict(os.environ, {'TPW_ALL_RESEARCHED': '1', 'TPW_OLD_OVERRIDE': 'bad'}, clear=True):
            fixture = vm.case_environment(disc, standalone=True)
            ordinary = vm.case_environment(disc)
        self.assertNotIn('TPW_ALL_RESEARCHED', fixture)
        self.assertNotIn('TPW_OLD_OVERRIDE', fixture)
        self.assertEqual(fixture['TPW_PS2_DISC'], str(disc))
        self.assertEqual(ordinary['TPW_ALL_RESEARCHED'], '1')
        ordinary['TPW_ALL_RESEARCHED'] = 'changed'
        self.assertNotIn('TPW_ALL_RESEARCHED', fixture)
        for resolution in vm.standalone_runs(self.CASE):
            command = vm.standalone_command(self.CASE, resolution, Path('/godot'), 'xvfb-run', disc)
            vm.validate_standalone_launch(self.CASE, command, fixture, disc, resolution=resolution)
            self.assertIn('res://tests/ResearchPersistenceSmoke.tscn', command)
            self.assertEqual(command[command.index('--resolution') + 1], resolution)
            for flag in [f'--disc={disc}', '--map=JUNGLE', '--mode=park']:
                with self.subTest(missing=flag), self.assertRaises(ValueError):
                    vm.validate_standalone_launch(self.CASE, [x for x in command if x != flag], fixture, disc, resolution=resolution)
            for changed in [command + ['--all-researched'], command + ['--map=SPACE'],
                            ['--headless'] + command, command + ['--'],
                            [x for x in command if x != '--resolution'],
                            [x.replace(resolution, '800x600') for x in command],
                            [x.replace('ResearchPersistenceSmoke.tscn', 'OtherSmoke.tscn') for x in command],
                            [x.replace('gl_compatibility', 'forward_plus') for x in command]]:
                with self.assertRaises(ValueError): vm.validate_standalone_launch(self.CASE, changed, fixture, disc, resolution=resolution)
            with self.assertRaises(ValueError):
                vm.validate_standalone_launch(self.CASE, command, ordinary, disc, resolution=resolution)
        with self.assertRaises(ValueError):
            vm.standalone_command(self.CASE, '800x600', Path('/godot'), 'xvfb-run', disc)

    def test_mixed_selectors_are_refused_before_launch(self):
        required = ['--disc', '/not-read', '--godot', '/not-run', '--out', '/not-written',
                    '--standalone-case', self.CASE]
        for flags in [['--scenes', 'pointer'], ['--parks', 'JUNGLE/1']]:
            with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as raised:
                vm.main(required + flags)
            self.assertEqual(raised.exception.code, 2)

    def run_mock_main(self, *, standalone=True, text=None, command_mutation=None, default_selection=False, snapshot_control=None):
        # Exercise main's REAL scheduling and launch builder without any disc/engine/process.
        with tempfile.TemporaryDirectory(dir='/tmp') as temp:
            root = Path(temp); disc = root / 'disc'; engine = root / 'godot'; out = root / 'output'
            disc.touch(); engine.touch()
            calls = []
            def process(command, *, repo, log, timeout, environment):
                calls.append((list(command), dict(environment)))
                if 'build' in command: body = 'Build succeeded.'
                elif '--version' in command: body = 'test-engine'
                elif standalone: body = self.good() if text is None else text
                else:
                    scene = next(key for key, (leaf, _, _) in vm.SCENES.items()
                                 if f'res://tests/{leaf}.tscn' in command)
                    request = next(x for x in command if x.startswith('--map=')).removeprefix('--map=')
                    world, leaf = request.split('  ')
                    body = good(scene, world, int(leaf[len('terrain_')]))
                return {**output(body), 'log': str(log)}
            args = ['--disc', str(disc), '--godot', str(engine), '--out', str(out), '--repo', str(root)]
            args += ['--standalone-case', self.CASE] if standalone else [] if default_selection else ['--scenes', 'pointer', '--parks', 'JUNGLE/1']
            original = vm.standalone_command
            def changed(*a, **kw):
                command = original(*a, **kw)
                return command_mutation(command) if command_mutation else command
            source_calls = 0; output_calls = 0
            def sources(repo):
                nonlocal source_calls
                source_calls += 1
                return {'dirty': False, 'generation': 2 if snapshot_control == 'source' and source_calls == 3 else 1}
            def assemblies(repo):
                nonlocal output_calls
                output_calls += 1
                # Standalone: build snapshot, pre-run1, pre-run2, post-run2.
                return {'dll': 'changed' if snapshot_control == 'assembly' and output_calls == 4 else 'stable'}
            with patch.object(vm, 'run_process', side_effect=process), \
                    patch.object(vm, 'source_snapshot', side_effect=sources), \
                    patch.object(vm, 'output_snapshot', side_effect=assemblies), \
                    patch.object(vm, 'file_hash', return_value='synthetic-test-hash'), \
                    patch.object(vm, 'engine_version', return_value='test-engine'), \
                    patch.object(vm, 'standalone_command', side_effect=changed), \
                    contextlib.redirect_stdout(io.StringIO()):
                code = vm.main(args)
            return code, json.loads((out / 'manifest.json').read_text()), calls

    def test_main_schedules_two_resolutions_not_eight_parks(self):
        code, manifest, calls = self.run_mock_main()
        self.assertEqual(code, 0)
        self.assertEqual(manifest['status'], 'selected_cases_passed')
        self.assertEqual(manifest['parks'], [])
        self.assertFalse(manifest['all_researched'])
        runs = [c for c, env in calls if 'res://tests/ResearchPersistenceSmoke.tscn' in c]
        self.assertEqual(len(runs), 2)
        self.assertEqual([r['resolution'] for r in manifest['results']], ['640x360', '1152x648'])
        self.assertEqual([r['checks'] for r in manifest['results']], [24, 24])
        for command, env in calls: self.assertNotIn('TPW_ALL_RESEARCHED', env)

    def test_main_rejects_actual_bad_command_and_incomplete_fixture(self):
        code, manifest, calls = self.run_mock_main(command_mutation=lambda c: [x for x in c if x != '--map=JUNGLE'])
        self.assertEqual(code, 1)
        self.assertEqual(manifest['status'], 'runner_error')
        self.assertFalse(manifest['results'])
        self.assertEqual(len(calls), 2)  # probe and build only; invalid renderer never launched
        code, manifest, calls = self.run_mock_main(text=self.good().replace('checks=24', 'checks=23'))
        self.assertEqual(code, 1)
        self.assertEqual(manifest['status'], 'failed_cases')
        self.assertEqual([r['status'] for r in manifest['results']], ['missing_coverage'] * 2)

    def test_main_rejects_sources_or_assemblies_changed_during_final_run(self):
        for control, expected in [('source', 'source_changed_during_run'),
                                  ('assembly', 'assemblies_changed_during_run')]:
            code, manifest, calls = self.run_mock_main(snapshot_control=control)
            self.assertEqual(code, 1)
            self.assertEqual(manifest['status'], expected)
            self.assertEqual([r['status'] for r in manifest['results']], ['pass', 'pass'])

    def test_main_ordinary_defaults_still_schedule_all_scenes_and_eight_parks(self):
        code, manifest, calls = self.run_mock_main(standalone=False, default_selection=True)
        self.assertEqual(code, 0)
        self.assertEqual(manifest['status'], 'all_cases_passed')
        self.assertEqual(len(manifest['results']), len(vm.SCENES) * 8)
        self.assertEqual({(r['world'], r['terrain'], r['scene']) for r in manifest['results']},
                         {(w, t, s) for w, t in vm.PARKS for s in vm.SCENES})

    def test_main_ordinary_case_still_uses_one_map_and_override(self):
        code, manifest, calls = self.run_mock_main(standalone=False)
        self.assertEqual(code, 0)
        self.assertEqual(len(manifest['results']), 1)
        self.assertEqual(manifest['results'][0]['loaded'], [['JUNGLE', 'terrain_1']])
        self.assertNotIn('standalone_case', manifest)
        for command, env in calls: self.assertEqual(env['TPW_ALL_RESEARCHED'], '1')


if __name__ == '__main__':
    unittest.main()
