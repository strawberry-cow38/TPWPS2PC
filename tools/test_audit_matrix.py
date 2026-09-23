"""Classifier negative controls. No real disc, build, credentials or external service."""
import unittest

from audit_matrix import EXPECTED, classify

COVERAGE = '\n'.join(['  ok   availability: check'] * 30 +
                     ['  ok   availability regression exercised a real ride with both availability flags'] +
                     ['  ok   removal: check'] * 57 +
                     ['  ok   removal regression exercised a real non-track ride with seats'] +
                     ['  ok   conservation: check'] * 19 +
                     ['  ok   conservation: identical fixed-tick inputs reproduce the full sampled lifecycle (100 steps, SHA256 ' + 'A' * 64 + ')'] +
                     ['  ok   needs lifecycle: check'] * 23 +
                     ['  ok   needs lifecycle: clock control actually applies four rises rather than passing with no updates'])



def known(world):
    return COVERAGE + '\n  FAIL ' + EXPECTED[world] + ' -- KNOWN documented retail evidence\nFAIL: 1\n'


class ClassificationTests(unittest.TestCase):
    def test_clean_worlds_pass(self):
        for world in ('JUNGLE', 'FANTASY'):
            self.assertEqual(classify(world, 0, COVERAGE + '\nPASS')['status'], 'pass')

    def test_known_reds_are_not_pass(self):
        for world in ('HALLOW', 'SPACE'):
            row = classify(world, 1, known(world))
            self.assertEqual(row['status'], 'known_retail_failure')
            self.assertEqual(row['raw_exit'], 1)

    def test_green_known_red_world_requires_review(self):
        self.assertEqual(classify('HALLOW', 0, COVERAGE + '\nPASS')['status'], 'unexpected_pass')

    def test_extra_failure_cannot_hide_behind_known_name(self):
        output = known('HALLOW').replace('FAIL: 1', '  FAIL unexpected Thrill Grill issue\nFAIL: 2')
        self.assertEqual(classify('HALLOW', 1, output)['status'], 'unexpected_failure')

    def test_extra_ride_in_same_failure_is_not_accepted(self):
        output = known('SPACE').replace('Moon Buggies -- KNOWN', 'Moon Buggies, Another Ride -- KNOWN')
        self.assertEqual(classify('SPACE', 1, output)['status'], 'unexpected_failure')

    def test_familiar_name_without_exact_cause_is_not_accepted(self):
        output = COVERAGE + '\n  FAIL new issue with Thrill Grill -- KNOWN label\nFAIL: 1'
        self.assertEqual(classify('HALLOW', 1, output)['status'], 'unexpected_failure')

    def test_crash_or_timeout_does_not_become_known_failure(self):
        self.assertEqual(classify('HALLOW', 134, known('HALLOW'))['status'], 'unexpected_failure')
        self.assertEqual(classify('HALLOW', 124, known('HALLOW'), timed_out=True)['status'], 'timeout')

    def test_truncated_logs_are_not_accepted(self):
        self.assertEqual(classify('JUNGLE', 0, COVERAGE + '\nPASS', truncated=True)['status'], 'truncated_log')

    def test_missing_checks_or_summary_are_not_pass(self):
        self.assertEqual(classify('JUNGLE', 0, 'PASS')['status'], 'missing_coverage')
        self.assertEqual(classify('JUNGLE', 0, COVERAGE)['status'], 'incomplete_output')
        self.assertEqual(classify('JUNGLE', 0, '')['status'], 'incomplete_output')

    def test_missing_lifecycle_suites_are_not_pass(self):
        for category in ('availability', 'removal', 'conservation', 'needs lifecycle'):
            output = '\n'.join(line for line in COVERAGE.splitlines()
                               if f'ok   {category}:' not in line) + '\nPASS'
            with self.subTest(category=category):
                self.assertEqual(classify('JUNGLE', 0, output)['status'], 'missing_coverage')

    def test_partial_lifecycle_suite_is_not_pass(self):
        for category in ('removal', 'conservation', 'needs lifecycle'):
            output = COVERAGE.replace(f'  ok   {category}: check\n', '', 1) + '\nPASS'
            with self.subTest(category=category):
                self.assertEqual(classify('JUNGLE', 0, output)['status'], 'missing_coverage')

    def test_missing_removal_or_replay_witness_is_not_pass(self):
        for witness in ('removal regression exercised', 'identical fixed-tick inputs reproduce', 'clock control actually applies four rises'):
            output = COVERAGE.replace(witness, 'something else') + '\nPASS'
            with self.subTest(witness=witness):
                self.assertEqual(classify('JUNGLE', 0, output)['status'], 'missing_coverage')

    def test_known_red_requires_lifecycle_coverage_too(self):
        output = '\n'.join(line for line in known('HALLOW').splitlines()
                           if 'ok   conservation:' not in line)
        self.assertEqual(classify('HALLOW', 1, output)['status'], 'missing_coverage')

    def test_lifecycle_counts_recorded_in_manifest_row(self):
        row = classify('JUNGLE', 0, COVERAGE + '\nPASS')
        self.assertEqual(row['availability_checks'], 30)
        self.assertEqual(row['removal_checks'], 57)
        self.assertEqual(row['conservation_checks'], 20)
        self.assertEqual(row['needs_lifecycle_checks'], 24)

    def test_suppressed_failure_exit_is_not_pass(self):
        self.assertEqual(classify('HALLOW', 0, known('HALLOW'))['status'], 'unexpected_failure')

    def test_wrong_world_does_not_inherit_exception(self):
        self.assertEqual(classify('JUNGLE', 1, known('HALLOW'))['status'], 'unexpected_failure')

    def test_summary_count_must_match(self):
        self.assertEqual(classify('SPACE', 1, known('SPACE').replace('FAIL: 1', 'FAIL: 2'))['status'],
                         'unexpected_failure')


class ProcessEvidenceTests(unittest.TestCase):
    def test_launch_failure_has_no_fabricated_child_exit(self):
        from pathlib import Path
        from tempfile import TemporaryDirectory
        from audit_matrix import run_process
        with TemporaryDirectory() as directory:
            root = Path(directory)
            result = run_process([str(root / 'missing-executable')], repo=root, log=root / 'out.log', timeout=1)
            self.assertIsNone(result['raw_exit'])
            self.assertEqual(result['launch_error'], 'FileNotFoundError')
            verdict = classify('JUNGLE', result['raw_exit'], result['text'], launch_error=result['launch_error'])
            self.assertEqual(verdict['status'], 'launch_error')

    def test_timeout_preserves_actual_returncode(self):
        import sys
        from pathlib import Path
        from tempfile import TemporaryDirectory
        from audit_matrix import run_process
        with TemporaryDirectory() as directory:
            root = Path(directory)
            result = run_process([sys.executable, '-c', 'import time; time.sleep(30)'],
                                 repo=root, log=root / 'out.log', timeout=.1)
            self.assertTrue(result['timed_out'])
            self.assertIsNotNone(result['raw_exit'])
            self.assertNotEqual(result['raw_exit'], 124)

    def test_existing_output_directory_is_preserved(self):
        import contextlib
        import io
        from pathlib import Path
        from tempfile import TemporaryDirectory
        from audit_matrix import main
        with TemporaryDirectory() as directory:
            root = Path(directory)
            disc = root / 'fake.bin'
            disc.write_bytes(b'synthetic fixture, not disc data')
            out = root / 'evidence'
            out.mkdir()
            previous = out / 'manifest.json'
            previous.write_text('keep previous evidence')
            with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
                main(['--disc', str(disc), '--out', str(out)])
            self.assertEqual(previous.read_text(), 'keep previous evidence')


if __name__ == '__main__':
    unittest.main()
