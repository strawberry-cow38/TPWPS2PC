"""Independent log controls for bounded water A/B; no engine/disc required."""
import unittest

import park_water_perf as perf


RECEIPTS = (
    'rendered performance display', 'normal explicit startup without immediate-quit benchmark',
    'normal Viewer startup completes', 'actual JUNGLE/1 performance map',
    'load-time water switch matches experiment', 'experiment draws water or omits only its drawable',
    'current performance camera is bound', 'finite natural-frame measurement samples',
    'bounded monotonic measurement excludes loading and warm-up',
    'camera transform and projection remain fixed throughout measurement',
    'water advances normally or remains absent throughout measurement',
    'normal teardown retires actual Viewer', 'normal teardown retires actual water',
    'no performance Viewer remains',
)


def good(enabled=True):
    return '\n'.join(['[map] loaded world=JUNGLE terrain=terrain_1.mps',
        "[aim] 'A_SEA_02': 1 surfaces, centre (22, -1.05, 7.5), size (16, 0.1, 25)"]
        + [f'PARK WATER PERF ok: [{n}] {s}' for n, s in enumerate(RECEIPTS, 1)]
        + [f'PARK WATER PERF RESULT enabled={int(enabled)} samples=100 median_ms=73.4 p95_ms=90.1 draw_calls=165.0 allocated_bytes_per_frame=15000.0 wall_seconds=9.1 camera={"A"*64} grid_min={12 if enabled else 0} grid_max={12 if enabled else 0} clip_min={9.278918 if enabled else 0} clip_max={9.278918 if enabled else 0}',
           'PARK WATER PERF PASS checks=14; bounded same-build A/B'])


def run(text):
    return dict(text=text, raw_exit=0, timed_out=False, truncated=False, launch_error=None)


class PerformanceControls(unittest.TestCase):
    def test_independent_floor_and_both_experiments(self):
        self.assertEqual(perf.MINIMUM, 14)
        for enabled in (False, True):
            self.assertEqual(perf.classify(enabled, run(good(enabled)))['status'], 'pass')

    def test_baseline_exit_warning_is_not_waived(self):
        text = good(False) + '\nWARNING: ObjectDB instances leaked at exit (run with --verbose for details).'
        self.assertEqual(perf.classify(False, run(text))['status'], 'error_output')

    def test_missing_retirement_or_clock_cannot_be_replaced_with_filler(self):
        for receipt in RECEIPTS:
            with self.subTest(receipt=receipt):
                self.assertEqual(perf.classify(True, run(good().replace(receipt, 'filler')))['status'], 'missing_coverage')

    def test_numbered_count_and_floor(self):
        for text in [good().replace('checks=14;', 'checks=13;'),
                     good().replace('ok: [2]', 'ok: [1]'),
                     good().replace('PARK WATER PERF ok: [1]', 'missing: [1]')]:
            self.assertEqual(perf.classify(True, run(text))['status'], 'missing_coverage')

    def test_wrong_or_repeated_map_fails(self):
        for text in [good().replace('terrain_1.mps', 'terrain_2.mps'),
                     good() + '\n[map] loaded world=JUNGLE terrain=terrain_1.mps']:
            self.assertEqual(perf.classify(True, run(text))['status'], 'wrong_map')

    def test_wrong_missing_or_repeated_experiment_result_fails(self):
        text = good()
        result = next(x for x in text.splitlines() if ' PERF RESULT ' in x)
        for changed in [text.replace('enabled=1', 'enabled=0'), text.replace(result, ''), text + '\n' + result]:
            self.assertEqual(perf.classify(True, run(changed))['status'], 'wrong_experiment')

    def test_missing_or_failed_aim_cannot_pass(self):
        text = good()
        aim = next(x for x in text.splitlines() if x.startswith('[aim]'))
        for changed in [text.replace(aim, ''), text.replace(aim, "[aim] no mesh matches 'A_SEA_02'. The terrain has:")]:
            self.assertEqual(perf.classify(True, run(changed))['status'], 'wrong_camera')

    def test_malformed_extra_result_row_cannot_disappear(self):
        for extra in ['PARK WATER PERF RESULT enabled=1 samples=NaN',
                      'PARK WATER PERF RESULT enabled=1 samples=100 median_ms=-1',
                      'PARK WATER PERF RESULT broken']:
            self.assertEqual(perf.classify(True, run(good() + '\n' + extra))['status'], 'wrong_experiment')

    def test_invalid_sample_count_or_time_fails(self):
        for changed in [good().replace('samples=100', 'samples=19'),
                        good().replace('median_ms=73.4', 'median_ms=0.0'),
                        good().replace('p95_ms=90.1', 'p95_ms=50.0'),
                        good().replace('wall_seconds=9.1', 'wall_seconds=15.0')]:
            self.assertEqual(perf.classify(True, run(changed))['status'], 'invalid_measurement')


    def test_measured_far_view_is_not_the_initial_grid_log(self):
        for enabled in (False, True):
            text = good(enabled) + '\n[water-lod-view] declared diagnostic eye depth=20; camera only, not native player view'
            self.assertEqual(perf.classify(enabled, run(text), lod_depth=20)['status'], 'pass')
        far = good() + '\n[water-lod-view] declared diagnostic eye depth=20; camera only, not native player view'
        for changed in [far.replace('grid_min=12', 'grid_min=11'),
                        far.replace('grid_max=12', 'grid_max=13'),
                        far.replace('9.278918', '19.0'),
                        far.replace('eye depth=20', 'eye depth=21'), good()]:
            self.assertEqual(perf.classify(True, run(changed), lod_depth=20)['status'], 'wrong_lod_view')
        self.assertEqual(perf.classify(True, run(far))['status'], 'unexpected_lod_view')

    def test_missing_or_impossible_lod_measurement_is_rejected(self):
        for changed in [good().replace('grid_min=12', 'grid_min=0'),
                        good().replace('grid_max=12', 'grid_max=17'),
                        good().replace('clip_max=9.278918', 'clip_max=8.0')]:
            self.assertEqual(perf.classify(True, run(changed))['status'], 'invalid_lod_measurement')
        self.assertEqual(perf.classify(False, run(good(False).replace('grid_min=0', 'grid_min=12')))['status'], 'invalid_lod_measurement')
        self.assertEqual(perf.classify(True, run(good().replace(' grid_min=12 grid_max=12 clip_min=9.278918 clip_max=9.278918', '')))['status'], 'wrong_experiment')

    def test_malformed_numeric_receipts_reject_without_raising(self):
        far = good() + '\n[water-lod-view] declared diagnostic eye depth=20; camera only, not native player view'
        for text in [far.replace('clip_min=9.278918', 'clip_min=..'),
                     far.replace('median_ms=73.4', 'median_ms=..')]:
            self.assertEqual(perf.classify(True, run(text), lod_depth=20)['status'], 'wrong_experiment')
        self.assertEqual(perf.classify(True, run(far.replace('eye depth=20', 'eye depth=..')), lod_depth=20)['status'], 'wrong_lod_view')
        fractional = far.replace('eye depth=20', 'eye depth=20.5').replace('9.278918', '9.529669')
        self.assertEqual(perf.classify(True, run(fractional), lod_depth=20.5)['status'], 'pass')


if __name__ == '__main__':
    unittest.main()