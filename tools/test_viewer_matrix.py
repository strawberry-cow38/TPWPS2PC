"""Viewer matrix classification controls; no disc, engine or display required."""
import unittest

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


if __name__ == '__main__':
    unittest.main()
