"""Classifier negative controls. No real disc, build, credentials or external service."""
import unittest

from audit_matrix import EXPECTED, classify, REQUIRED_CHECKS, REQUIRED_WITNESSES

COVERAGE = '\n'.join([f'  ok   {category.replace("_", " ")}: filler'
                        for category, count in REQUIRED_CHECKS.items() if category.startswith('native_')
                        for _ in range(count)] +
                     ['  '+w for w in REQUIRED_WITNESSES if w.startswith('ok   native ')] +
                     ['  ok   queue walk: check'] * 7 +
                     ['  ok   track ride: check'] * 43 +
                     ['  ok   post service movement: check'] * 15 +
                     ['  ok   post service movement: relief1234 ordinary arm walks away before the facility deadline', '  ok   post service movement: shop1234 ordinary arm walks away before the facility deadline', '  ok   post service movement: shop299 ordinary arm walks away before the facility deadline'] +
                     ['  ok   terminal walking: check'] * 86 +
                     ['  ok   terminal walking: cash1234 coordinator cannot board from the stub', '  ok   terminal walking: cash299 real handback retains inside position after success or refusal', '  ok   terminal walking: remove50 actual same-ID replacement cannot inherit an inside guest', '  ok   terminal walking: turn3 real approach leg has interpolated walking progress'] +
                     ['  ok   decision scheduling: check'] * 22 +
                     ['  ok   decision scheduling: native arm1 remains available before facility deadline and consumes both draws'] +
                     ['  ok   decision scheduling: cash299 park deadline survives eightfold appetite rate change'] +
                     ['  ok   decision scheduling: strict boundary rejects stored300 plus extra60 equality', '  ok   decision scheduling: cash1234 zero-time calls cannot reboard the same shop', '  ok   decision scheduling: cash299 zero-time calls cannot reboard the same shop', '  ok   decision scheduling: cash299 eligible later decision can revisit instead of a permanent blacklist'] +
                     ['  ok   compiled purchase: check'] * 59 +
                     ['  ok   compiled purchase: bare-world source path attaches the named shop independently of region loop',
                      '  ok   compiled purchase: archive-qualified source path attaches the named shop independently of region loop',
                      '  ok   compiled purchase: product alone reverses the transfer and selects its own bladder amount',
                      '  ok   compiled purchase: usa ice cream keeps its regional hunger 15/vomit 15',
                      '  ok   compiled purchase: eur product7 falls through to all food effects at initial q2 zero',
                      '  ok   compiled purchase: jap costume handback changes preference to14 without reseeding or food effects',
                      '  ok   compiled purchase: eur 299 cash refuses the 300-unit sale with no debit or effects at real handback',
                      '  ok   compiled purchase: eur exactly300 cash buys once rather than being rejected at the boundary'] +
                     ['  ok   ride effect consumer: check'] * 31 +
                     ['  ok   ride effect consumer: value 55, sickness 20 becomes 20',
                      '  ok   ride effect consumer: preference 30, value 81 awards band 5'] +
                     ['  ok   departure recovery: check'] * 5 +
                     ['  ok   departure recovery: repaired departure resumes and reaches the gate'] +
                     ['  ok   service routing: check'] * 4 +
                     ['  ok   service routing: unreachable nearest does not degrade urgent errand to the distracting ride'] +
                     ['  ok   availability: check'] * 30 +
                     ['  ok   availability regression exercised a real ride with both availability flags'] +
                     ['  ok   removal: check'] * 57 +
                     ['  ok   removal regression exercised a real non-track ride with seats'] +
                     ['  ok   conservation: check'] * 19 +
                     ['  ok   conservation: identical fixed-tick inputs reproduce the full sampled lifecycle (100 steps, SHA256 ' + 'A' * 64 + ')'] +
                     ['  ok   needs lifecycle: check'] * 47 +
                     ['  ok   needs lifecycle: clock control actually applies four rises rather than passing with no updates',
                      '  ok   needs lifecycle: normal completion applies the configured effect once without reseeding unaffected fields',
                      '  ok   needs lifecycle: completion preserves nonzero preference and applies its middle band instead of fallback7'] +
                     ['  ok   disruption: check'] * 18 +
                     ['  ok   disruption: identical disruption inputs replay the entire observed ledger',
                      '  ok   disruption: late-run negative control catches changed cash through ordinary per-step sampling',
                      '  ok   disruption: late-run negative control catches orphan needs through ordinary per-step sampling'])



def known(world):
    return COVERAGE + '\n  FAIL ' + EXPECTED[world] + ' -- KNOWN documented retail evidence\nFAIL: 1\n'


class ClassificationTests(unittest.TestCase):
    def test_post_service_motion_cannot_be_omitted_or_replaced_with_counts(self):
        for label in ('post service movement:',
                      'relief1234 ordinary arm walks away before the facility deadline',
                      'shop299 ordinary arm walks away before the facility deadline',
                      'native arm1 remains available before facility deadline and consumes both draws'):
            text='\n'.join(x for x in COVERAGE.splitlines() if label not in x)
            self.assertEqual(classify('JUNGLE',0,text+'\nPASS')['status'],'missing_coverage')

    def test_terminal_walking_count_and_physical_witnesses_required(self):
        for text in ('\n'.join(x for x in COVERAGE.splitlines() if 'terminal walking:' not in x),
                     COVERAGE.replace('  ok   terminal walking: check\n', '', 1),
                     COVERAGE.replace('cash1234 coordinator cannot board from the stub', 'unrelated check'),
                     COVERAGE.replace('remove50 actual same-ID replacement cannot inherit an inside guest', 'unrelated check')):
            self.assertEqual(classify('JUNGLE', 0, text + '\nPASS')['status'], 'missing_coverage')

    def test_decision_schedule_coverage_and_witnesses_required(self):
        for text in ('\n'.join(x for x in COVERAGE.splitlines() if 'decision scheduling:' not in x),
                     COVERAGE.replace('  ok   decision scheduling: check\n', '', 1),
                     COVERAGE.replace('cash299 zero-time calls cannot reboard the same shop', 'unrelated check'),
                     COVERAGE.replace('strict boundary rejects stored300 plus extra60 equality', 'unrelated check')):
            self.assertEqual(classify('JUNGLE', 0, text + '\nPASS')['status'], 'missing_coverage')

    def test_purchase_coverage_cannot_be_omitted_or_short(self):
        for text in ('\n'.join(line for line in COVERAGE.splitlines() if 'compiled purchase:' not in line),
                     COVERAGE.replace('  ok   compiled purchase: check\n', '', 1)):
            self.assertEqual(classify('JUNGLE', 0, text + '\nPASS')['status'], 'missing_coverage')

    def test_purchase_count_cannot_replace_regional_or_transaction_witness(self):
        for witness in ('bare-world source path attaches the named shop independently of region loop',
                        'archive-qualified source path attaches the named shop independently of region loop',
                        'usa ice cream keeps its regional hunger 15/vomit 15',
                        'product7 falls through to all food effects at initial q2 zero',
                        'costume handback changes preference to14 without reseeding or food effects',
                        '299 cash refuses the 300-unit sale', 'exactly300 cash buys once'):
            text = COVERAGE.replace(witness, 'unrelated check') + '\nPASS'
            self.assertEqual(classify('JUNGLE', 0, text)['status'], 'missing_coverage')

    def test_missing_effect_consumer_or_preference_lifecycle_is_not_green(self):
        for label in ('ride effect consumer:', 'completion preserves nonzero preference'):
            text = '\n'.join(line for line in COVERAGE.splitlines() if label not in line) + '\nPASS'
            self.assertEqual(classify('JUNGLE', 0, text)['status'], 'missing_coverage')

    def test_missing_departure_helper_is_not_green(self):
        text = '\n'.join(line for line in COVERAGE.splitlines() if 'departure recovery:' not in line) + '\nPASS'
        self.assertEqual(classify('JUNGLE', 0, text)['status'], 'missing_coverage')

    def test_missing_routing_helper_is_not_green(self):
        text = '\n'.join(line for line in COVERAGE.splitlines() if 'service routing:' not in line) + '\nPASS'
        self.assertEqual(classify('JUNGLE', 0, text)['status'], 'missing_coverage')

    def test_missing_disruption_coverage_cannot_pass(self):
        text = COVERAGE.replace('  ok   disruption: check', '', 1) + '\nPASS'
        self.assertEqual(classify('JUNGLE', 0, text)['status'], 'missing_coverage')

    def test_disruption_count_without_late_control_cannot_pass(self):
        text = COVERAGE.replace('late-run negative control catches changed cash through ordinary per-step sampling', 'other check') + '\nPASS'
        self.assertEqual(classify('JUNGLE', 0, text)['status'], 'missing_coverage')

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
        for witness in ('removal regression exercised', 'identical fixed-tick inputs reproduce', 'clock control actually applies four rises',
                        'normal completion applies the configured effect once'):
            output = COVERAGE.replace(witness, 'something else') + '\nPASS'
            with self.subTest(witness=witness):
                self.assertEqual(classify('JUNGLE', 0, output)['status'], 'missing_coverage')

    def test_known_red_requires_lifecycle_coverage_too(self):
        output = '\n'.join(line for line in known('HALLOW').splitlines()
                           if 'ok   conservation:' not in line)
        self.assertEqual(classify('HALLOW', 1, output)['status'], 'missing_coverage')

    def test_lifecycle_counts_recorded_in_manifest_row(self):
        row = classify('JUNGLE', 0, COVERAGE + '\nPASS')
        self.assertEqual(row['compiled_purchase_checks'], 67)
        self.assertEqual(row['availability_checks'], 30)
        self.assertEqual(row['removal_checks'], 57)
        self.assertEqual(row['conservation_checks'], 20)
        self.assertEqual(row['needs_lifecycle_checks'], 50)

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



class ParkIdentity(unittest.TestCase):
    """2026-09-25: a case must prove which park it ran from its own output."""
    def test_right_park_line_is_required_when_a_terrain_is_named(self):
        text = 'JUNGLE terrain_2: 128x128; entrance ok\n' + COVERAGE + '\nPASS\n'
        self.assertNotEqual(classify('JUNGLE', 0, text, terrain=2)['status'], 'wrong_park')

    def test_regression_other_park_output_is_wrong_park(self):
        text = 'JUNGLE terrain_1: 128x128; entrance ok\n' + COVERAGE + '\nPASS\n'
        self.assertEqual(classify('JUNGLE', 0, text, terrain=2)['status'], 'wrong_park')

    def test_no_park_line_is_wrong_park(self):
        self.assertEqual(classify('JUNGLE', 0, COVERAGE + '\nPASS\n', terrain=1)['status'], 'wrong_park')


if __name__ == '__main__':
    unittest.main()
