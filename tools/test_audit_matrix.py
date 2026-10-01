"""Classifier negative controls. No real disc, build, credentials or external service."""
import unittest

from audit_matrix import EXPECTED, classify, REQUIRED_CHECKS, REQUIRED_WITNESSES

COVERAGE = '\n'.join([f'  ok   {category.replace("_", " ")}: filler'
                        for category, count in REQUIRED_CHECKS.items() if category.startswith('native_')
                        for _ in range(count)] +
                     ['  '+w for w in REQUIRED_WITNESSES if w.startswith('ok   native ')] +
                     ['  ok   queue walk: check'] * 7 +
                     ['  ok   track ride: check'] * 43 +
                     ['  ok   coaster: check'] * 44 +
                     ['  ok   post service movement: check'] * 15 +
                     ['  ok   post service movement: relief1234 ordinary arm walks away before the facility deadline', '  ok   post service movement: shop1234 ordinary arm walks away before the facility deadline', '  ok   post service movement: shop299 ordinary arm walks away before the facility deadline'] +
                     ['  ok   terminal walking: check'] * 86 +
                     ['  ok   terminal walking: cash1234 coordinator cannot board from the stub', '  ok   terminal walking: cash299 real handback retains inside position after success or refusal', '  ok   terminal walking: remove50 actual same-ID replacement cannot inherit an inside guest', '  ok   terminal walking: turn3 real approach leg has interpolated walking progress'] +
                     ['  ok   decision scheduling: check'] * 22 +
                     ['  ok   decision scheduling: native arm1 remains available before facility deadline and consumes both draws'] +
                     ['  ok   decision scheduling: cash299 park deadline survives eightfold appetite rate change'] +
                     ['  ok   decision scheduling: strict boundary rejects stored300 plus extra60 equality', '  ok   decision scheduling: cash1234 zero-time calls cannot reboard the same shop', '  ok   decision scheduling: cash299 zero-time calls cannot reboard the same shop', '  ok   decision scheduling: cash299 eligible later decision can revisit instead of a permanent blacklist'] +
                     ['  ok   compiled purchase: check'] * 67 +
                     ['  ok   compiled purchase: bare-world source path attaches the named shop independently of region loop',
                      '  ok   compiled purchase: archive-qualified source path attaches the named shop independently of region loop',
                      '  ok   compiled purchase: product alone reverses the transfer and selects its own bladder amount',
                      '  ok   compiled purchase: usa ice cream keeps its regional hunger 15/vomit 15',
                      '  ok   compiled purchase: eur product7 falls through to all food effects at initial q2 zero',
                      '  ok   compiled purchase: jap costume handback changes preference to14 without reseeding or food effects',
                      '  ok   compiled purchase: eur 299 cash refuses the 300-unit sale with no debit or effects at real handback',
                      '  ok   compiled purchase: eur exactly300 cash buys once rather than being rejected at the boundary',
                      '  ok   compiled purchase: eur a refused visit is no customer but still one satisfaction visit (0x1D1E68 runs at the common exit)',
                      '  ok   compiled purchase: sideshow: five losing games score exactly 50 -- the most a game can make (5 x 10), so a sideshow\'s satisfaction bar never fills past half'] +
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
                      '  ok   disruption: late-run negative control catches orphan needs through ordinary per-step sampling'] +
                     ['  ok   staff: check'] * 78 +
                     ['  ok   staff: toilet score: |dx| + |dz|*(c+1) EXACTLY as shipped picks the same-row c59 toilet',
                      '  ok   staff: no staff room: a tired handyman never works again, he patrols forever'] +
                     ['  ok   mechanic: check'] * 64 +
                     ['  ok   mechanic: priority: the duplicated 0x153D40 is DEAD -- 5 coin-1 decisions repair nothing',
                      '  ok   mechanic: call mechanic: the predicate tests the MODE byte for 0x32 (refused) -- the shipped slip'])



def known(world):
    return COVERAGE + '\n  FAIL ' + EXPECTED[world] + ' -- KNOWN documented retail evidence\nFAIL: 1\n'


# Staff step 4 (guards and entertainers): kept as its own statement, as in audit_matrix.py, and
# after known() so the staff steps' parallel edits to the list above merge cleanly.
COVERAGE += '\n' + '\n'.join(['  ok   guard: check'] * 48 +
                              ['  ok   guard: one leg: dispatched with deadline 3600 (now + 3600), the re-route stored now over it',
                               '  ok   guard: prank with the litter pool full (40): no litter, NO STINK (beqz at 0x20D148)',
                               '  ok   guard: camera rule: a camera 8 cells from the PRANKSTER (d2 64 <= 64) lets a guard 7 away be sent'] +
                              ['  ok   entertainer: check'] * 28 +
                              ['  ok   entertainer: spawn cooldown: 400 ticks with no show draw rand(300) 0 times; the first show draws it 1 time(s)'])

# Staff step 5 (management): its own statement, for the same merge reason.
COVERAGE += '\n' + '\n'.join(['  ok   management: check'] * 56 +
                              ['  ok   management: wages high: with no income at all it cannot fire before the THIRD month end (False,False,True)',
                               '  ok   management: patrol tool: first corner the SMALLER -- highlighted (1, 2, 3, 4), walked (2, 1, 4, 3)',
                               '  ok   management: strike walk: the strike starts at a month change and that month is paid in full ($100)',
                               '  ok   management: award: the hidden-award bit lives in [0x3975E8], not the park -- a second park wins nothing'])
# The advisor, step A: its own statement, for the same merge reason.
COVERAGE += '\n' + '\n'.join(['  ok   advisor: check'] * 65 +
                              ['  ok   advisor: scheduler: the warm-up refreshes 5 a call for 15 calls, the 16th refreshes the last 4 AND considers rule 0, then 1 a call',
                               '  ok   advisor: v52/v53: v52 copies VARIABLE 75 (the ticket counter\'s last snapshot, not the counter)',
                               '  ok   advisor: states: a SILENT message (0x3) still costs the cycle -- the next is played 103 ticks later',
                               '  ok   advisor: counters: events count with flags bit 3; ride-along (flags 6) DROPS them; its end restores the flags',
                               '  ok   advisor: rules: real rule 0 over the producers -- a placed ride, park closed posts OPEN_PARK',
                               '  ok   advisor: rules: real rule 48 -- a handyman at tiredness 40 posts NEED_STAFF_ROOM 0x80',
                               '  ok   advisor: lips: the gate starts SET and flips on the tick each of sp_001\'s marks',
                               '  ok   advisor: mouth: with no lip track the gate stays set (until the head goes down)',
                               '  ok   advisor: removal: with a later type-2 record about ANOTHER ride, deleting the first ride removes the other\'s'])

# The ride hoarding: its own statement, for the same merge reason.
COVERAGE += '\n' + '\n'.join(['  ok   hoarding: check'] * 27 +
                              ['  ok   hoarding: 1x1 offsets: 0x1f39b8\'s eight stores are (0.3, -0.3), (-0.3, -0.3), (0.3, 0.3), (0.3, 0.3)',
                               '  ok   hoarding: order (0x1f3c70): Big Dripper\'s first panel is the -x edge of the top-left cell and its LAST the +z edge',
                               '  ok   hoarding: textures (0x1f5948): broken Hoarding; condemned Condemn; broken again keeps Condemn; upgrade Upgrade replaces Condemn',
                               '  ok   hoarding: re-raise: caught at p 0.520 on the way down, a raise rises again FROM THERE (0.528)'])


# Surface fittings (SurfaceSeatChecks.cs): its own statement, for the same merge reason. Real lines from a
# --surface-seat-only run, trimmed; the two witnesses are the point and the facing-bit claims.
COVERAGE += '\n' + '\n'.join(['  ok   surface seat: 302 surface fittings on 37 models across the disc',
                              '  ok   surface seat: the point: 302 of 302 sit on their helper\'s bind origin to 1e-3',
                              '  ok   surface seat: the point control: with the normal flipped, 301 seats leave their helper',
                              '  ok   surface seat: the gate: 270 of 302 parents move their vertices in some record',
                              '  ok   surface seat: the turn: 140 turned seats, median 0.28 deg and at most 22.9 from their helper\'s axes',
                              '  ok   surface seat: the turn control: WITHOUT the fitting\'s angles the face frame is a median 133.6 deg off',
                              '  ok   surface seat: the order control: Rx Ry Rz instead of Rz Ry Rx puts a seat 176 deg off',
                              '  ok   surface seat: the facing bit: one bit of noise in the drawn vertex 2\'s y leaves the face alone'])


# The trampoline (BounceChecks.cs): its own statement, for the same merge reason. Real lines from a --bounce-only
# run, trimmed; the witnesses are the loader's pad base and the disc-wide pad census.
COVERAGE += '\n' + '\n'.join(['  ok   bounce: the loader writes 0x32 to inst+0xC0 (0x1C002C/0x1C0060), and a new ride starts there: 50',
                              '  ok   bounce: the loader writes 1 to inst+0x70 (0x1C0034/0x1C0064), so the first bouncer stands on pad 1: #7 pad 1',
                              '  ok   bounce: the ticker\'s speed is inst+0xC0 / 200 + 0.8, the 0.8 read at 0x366CC0',
                              '  ok   bounce: 4 BOUNCE rides (Jelly 5 from pad 3, Brainb 9 from pad 1, Bouncy 9 from pad 1, Bouncy 10 from pad 1): 33 of 33',
                              '  ok   bounce: the control: counted from pad 0 instead of the loader\'s 1, only 29 of 33 would -- the base is visible',
                              '  ok   bounce: the hump: 0.80 at the bottom (BOUNCESETBASE 8 tenths), 2.30 at the top',
                              '  ok   bounce: never below the pad: with the pad at 1.2 the bottom is the pad and the top clears it',
                              '  ok   bounce: the get-off window (first 200 ms) peaks at 1.67, well under the 2.30 mid-air'])


# The music (MusicChecks.cs): its own statement, for the same merge reason. Real lines from a --music-only run,
# trimmed; the witnesses are the selector-2 finding and its selector-4 control.
COVERAGE += '\n' + '\n'.join(['  ok   music: the park writes the guest value as selector 2: 0x151C58 calls 0x111E08',
                              '  ok   music: the chooser steers on the event\'s own Word12: 0x244E08 writes event+0x12 to rec[0]',
                              '  ok   music: a parameter lands only where the selector matches: 0x2462A0 compares rec[i]',
                              '  ok   music: flags 4 | 2 | 0x400 make the graph class: 0x2474A8 calls 0x24C5E0 (vtable 0x371450)',
                              '  ok   music: park music, event 2 in all four worlds: JUNGLE 6 sets, flags 0x606, Word12 4, top band 90',
                              '  ok   music: lobby music, event 6 in all four worlds: JUNGLE 3 clips flags 0x206',
                              '  ok   music: a full park written as selector 2: no slot takes it, the chooser reads 0',
                              '  ok   music: the control, the same 90 on the event\'s own selector 4 plays the top level and 45 a middle one',
                              '  ok   music: lobby clips: 0 of 300 follow themselves (0) and all 3 play',
                              '  ok   music: the first clip of each map decodes as 22050 Hz stereo',
                              '  ok   music: the lead, re-measured on the 4 of them loud at both ends: median 1153 samples against 1152',
                              '  ok   music: past the pool: 150 guests make value 90, and 20 clips play on the top level in every world',
                              '  ok   music: a missed clip draw: 0 of 800 steered two-hour runs stop with ClampDraws'])


# The laptop's statistics (ParkStatsChecks.cs): its own statement, for the same merge reason. Real lines from a
# --parkstats-only run; the witnesses are the year roll's words and the rating.
COVERAGE += '\n' + '\n'.join([
                              '  ok   parkstats: the year roll 0x100EF8 copies 0x12dc -> 0x12e0 and 0x12e4 -> 0x12e8, then zeroes both',
                              '  ok   parkstats: Park Statistics\' walk 0x186D38 stores each getter\'s result into the bucket (0x186EAC sw v0,0(t0) in the delay slot of jal 0x16B378) and only counts (s1++): it assigns, it does not sum',
                              '  ok   parkstats: money in/out: this year 70/0 after the roll, last year 500/200; the lifetime Cash In/Out (570/200) do not roll -- the control that they are different fields',
                              '  ok   parkstats: the last-year park value snapshot lands at month end 13 (count % 12 == 0 && != 0, before the increment), and the value ring holds it (1300) -- one month after a calendar year',
                              '  ok   parkstats: before the first month end every series reads 0 (the getter\'s k >= count)',
                              '  ok   parkstats: three month ends: people 10, 14, 4; arrivals 14 (the 2nd month\'s whole headcount -- the getter\'s k < count) then 0 (the park shrank: clamped), happiness 75% (300/4), time 10d, December rating 50',
                              '  ok   parkstats: k = 0 reads as k = 1: Park Statistics\' \'now\' is LAST month-end\'s recording, not the live park',
                              '  ok   parkstats: People Visited counts admissions (2); days in park is today minus the arrival stamp (10)',
                              '  ok   parkstats: the rating of 3 rides (one at tier 2), 6 shops, 1 sideshow, 12 features and nobody: 27 = 4 + 1 + 10 + 2 + 10',
                              '  ok   parkstats: the caps: 30 rides all at tier 3 rate 30 = 20 + 10, not 45 + 30 -- the control that the terms are capped',
                              '  ok   parkstats: Park Statistics\' walk assigns rather than sums: the first two-month bucket is 5 where the finance builder makes 11; People\'s max is the fixed 110',
                              '  ok   parkstats: a year of the calendar: 12 month ends recorded (people 5, happiness 50%, rating 63), the December sample 63, and the year roll moved 900 to last year',
])


# The park's loans (LoanChecks.cs): its own statement. Real lines from a --loans-only run; the witnesses are the
# month end's instruction words and the payoff that never re-opens the lender.
COVERAGE += '\n' + '\n'.join([
                              '  ok   loans: the month end 0x100A18 files the balance ring (0x100A70) BEFORE the loan walk (+0x28, +0x14, +0x18; decrements at 0x100AA8/0x100AB0) and the single debit (0x100B18)',
                              "  ok   loans: taking Mr Byrne's loan credits $100,000 as income and books $172,800 over 36 months at $4,800",
                              '  ok   loans: taking it a second time is refused, silently, and moves no money -- the only refusal there is',
                              '  ok   loans: Ms West: 10,000 x 1.15^2 = 13,225 compounded, but the page and the debt are the RE-DERIVED $551 x 24 = $13,224',
                              '  ok   loans: one month end with $500 of wages: the repayment and the wages leave in one debit ($5,300), both in Cash Out, but only the wages reach the wage total and ring ($500)',
                              "  ok   loans: the Bank Balance ring holds $130,000, the balance BEFORE the month's bills, not the $124,700 after them",
                              '  ok   loans: after 36 month ends it is paid off ($0, 0 months), the 37th takes only its wages, and the record still shows its $4,800 repayment',
                              '  ok   loans: a paid-off loan is still TAKEN: nothing sets +0x28 back, so each lender lends once per park',
                              "  ok   loans: two loans owe $186,024 (the Balance Sheet's Loans row), and the gold tickets' money figure drops by exactly their interest x 10 (760240) the moment they are taken",
])

# Research (ResearchChecks.cs): its own statement. Real lines from a --research-only run; the witnesses are the two
# executable claims (only the screen and the loader start a project; 100 % is the unlock) and the fresh park's 13.
COVERAGE += '\n' + '\n'.join([
                              '  ok   research: 0x1B6880 (start a project) has exactly two callers, the Research screen and the save loader (0x1B5644, 0x1B6764): nothing starts research on its own',
                              '  ok   research: 0x12BAF8 tests the filed percent against 100 (slti s1, 0x64 at 0x12BB3C): reaching it IS the unlock',
                              "  ok   research: all 347 catalogue keys resolve in arsdb.dba with their list's kind (0 do not)",
                              "  ok   research: JUNGLE park 1 starts with exactly the 13 items whose tier-0 group is 0 (the live savestate's 13): 222 226 228 225 193 198 204 208 240 241 243 247 249",
                              "  ok   research: SPACE park 2 starts with its 16 -- a different list, so the rule is not one park's constant",
                              "  ok   research: the AllResearched debug key opens all 41 of JUNGLE park 1's items (the control)",
                              '  ok   research: a fresh JUNGLE park: thresholds [1,1,1,1], Rides offers 221 223 224 230 220, Shops 242 245 246, Sideshows 251 253, and Upgrades nothing (no ride built)',
                              '  ok   research: ride 221 researched by a level-0 researcher in 38 quanta: available, level 1, message 0x4B, and the slot idle',
                              '  ok   research: nothing starts the next item: the row stays idle, and 221 is no longer a candidate',
                              "  ok   research: Crazy Ape's first upgrade: offered on Upgrades only once one is built, researched in 10 quanta to level 2, and with no mechanic hired the message is 0x7E (hire one) rather than 0x4C",
])

# Executable checks added with the advisor/research producer read: these belong to
# research, not the separately counted advisor-research fixture family.
COVERAGE += '\n' + '\n'.join([
    '  ok   research: advisor research mask bug is in the ELF: the same bit4 gates track and coaster (count call0x12B0A0, availability kind1)',
    '  ok   research: advisor variety feature flags read in order1/8/2: toilet, camera, staff room',
    '  ok   research: advisor variety reads nonzero object status and counts a50-entry distinct-type set',
    '  ok   research: upgrade-use helper stores the largest tier+1 and loops over placements using tier+0x126',
])

# These are classifier fixtures, NOT simulated game or disc evidence. Pin the count
# to291 independently of REQUIRED_CHECKS so deleting/lowering that requirement fails
# the tests. The five non-filler lines are actual labels from the core fixture output.
ADVISOR_RESEARCH_LABELS = (
    '  ok   advisor research: bit4 research selects track AND coaster: 1/2: expected 50, got 50',
    '  ok   advisor research: File99 is NOT unlock: expected 40, got 40',
    '  ok   advisor research: File100 updates existing producer research 5/10: expected 50, got 50',
    '  ok   advisor research: same-key duplicates MAX tier1, not sum2: expected 50, got 50',
    '  ok   advisor research: max tier2 even on status0 duplicate: expected 100, got 100',
)
COVERAGE += '\n' + '\n'.join(['  ok   advisor research: classifier fixture'] * (291 - len(ADVISOR_RESEARCH_LABELS))
                             + list(ADVISOR_RESEARCH_LABELS))


class AdvisorResearchCoverageTests(unittest.TestCase):
    def test_requirement_is_registered_at_the_actual_floor(self):
        self.assertEqual(REQUIRED_CHECKS.get('advisor_research'), 291)
        self.assertEqual(REQUIRED_CHECKS['research'], 14)

    def test_full_family_and_manifest_count(self):
        row = classify('JUNGLE', 0, COVERAGE + '\nPASS')
        self.assertEqual(row['status'], 'pass')
        self.assertEqual(row['advisor_research_checks'], 291)
        self.assertEqual(row['research_checks'], 14)

    def test_entire_family_cannot_disappear(self):
        text = '\n'.join(line for line in COVERAGE.splitlines()
                         if not line.strip().startswith('ok   advisor research:'))
        row = classify('JUNGLE', 0, text + '\nPASS')
        self.assertEqual(row['status'], 'missing_coverage')
        self.assertEqual(row['advisor_research_checks'], 0)

    def test_one_missing_assertion_is_not_full_coverage(self):
        text = COVERAGE.replace('  ok   advisor research: classifier fixture\n', '', 1)
        row = classify('JUNGLE', 0, text + '\nPASS')
        self.assertEqual(row['advisor_research_checks'], 290)
        self.assertEqual(row['status'], 'missing_coverage')

    def test_count_cannot_replace_a_semantic_witness(self):
        for label in ADVISOR_RESEARCH_LABELS:
            with self.subTest(label=label):
                text = COVERAGE.replace(label, '  ok   advisor research: classifier fixture', 1)
                row = classify('JUNGLE', 0, text + '\nPASS')
                self.assertEqual(row['advisor_research_checks'], 291)
                self.assertEqual(row['status'], 'missing_coverage')

    def test_known_retail_failure_still_requires_this_family(self):
        for world in ('HALLOW', 'SPACE'):
            with self.subTest(world=world):
                text = '\n'.join(line for line in known(world).splitlines()
                                 if not line.strip().startswith('ok   advisor research:'))
                self.assertEqual(classify(world, 1, text)['status'], 'missing_coverage')


class ClassificationTests(unittest.TestCase):
    def test_staff_family_count_and_witnesses_required(self):
        for text in ('\n'.join(x for x in COVERAGE.splitlines() if 'staff:' not in x),
                     COVERAGE.replace('  ok   staff: check\n', '', 1),
                     COVERAGE.replace('toilet score: |dx| + |dz|*(c+1) EXACTLY as shipped', 'toilet score: some formula')):
            self.assertEqual(classify('JUNGLE', 0, text + '\nPASS')['status'], 'missing_coverage')
        self.assertEqual(classify('JUNGLE', 0, COVERAGE + '\nPASS')['status'], 'pass')

    def test_mechanic_family_count_and_witnesses_required(self):
        for text in ('\n'.join(x for x in COVERAGE.splitlines() if 'mechanic:' not in x),
                     COVERAGE.replace('  ok   mechanic: check\n', '', 1),
                     COVERAGE.replace('the duplicated 0x153D40 is DEAD', 'the duplicated 0x153D40 repairs'),
                     COVERAGE.replace('the predicate tests the MODE byte', 'the predicate tests the STATE byte')):
            self.assertEqual(classify('JUNGLE', 0, text + '\nPASS')['status'], 'missing_coverage')
        self.assertEqual(classify('JUNGLE', 0, COVERAGE + '\nPASS')['status'], 'pass')

    def test_hoarding_family_count_and_witnesses_required(self):
        for text in ('\n'.join(x for x in COVERAGE.splitlines() if 'hoarding:' not in x),
                     COVERAGE.replace('  ok   hoarding: check\n', '', 1),
                     COVERAGE.replace('broken again keeps Condemn', 'broken again shows Hoarding'),
                     COVERAGE.replace('order (0x1f3c70): Big Dripper', 'order (0x1f3c70): some ride')):
            self.assertEqual(classify('JUNGLE', 0, text + '\nPASS')['status'], 'missing_coverage')
        self.assertEqual(classify('JUNGLE', 0, COVERAGE + '\nPASS')['status'], 'pass')

    def test_surface_seat_family_count_and_witnesses_required(self):
        for text in ('\n'.join(x for x in COVERAGE.splitlines() if 'surface seat:' not in x),
                     COVERAGE.replace('  ok   surface seat: the gate:', '  ok   unrelated: the gate:', 1),
                     COVERAGE.replace('302 of 302 sit on their helper', '301 of 302 sit on their helper'),
                     COVERAGE.replace('the facing bit: one bit of noise', 'the facing bit: something else')):
            self.assertEqual(classify('JUNGLE', 0, text + '\nPASS')['status'], 'missing_coverage')
        self.assertEqual(classify('JUNGLE', 0, COVERAGE + '\nPASS')['status'], 'pass')

    def test_bounce_family_count_and_witnesses_required(self):
        for text in ('\n'.join(x for x in COVERAGE.splitlines() if 'bounce:' not in x),
                     COVERAGE.replace('  ok   bounce: the hump:', '  ok   unrelated: the hump:', 1),
                     COVERAGE.replace('the loader writes 1 to inst+0x70', 'the loader writes 0 to inst+0x70'),
                     COVERAGE.replace('4 BOUNCE rides', '3 BOUNCE rides')):
            self.assertEqual(classify('JUNGLE', 0, text + '\nPASS')['status'], 'missing_coverage')
        self.assertEqual(classify('JUNGLE', 0, COVERAGE + '\nPASS')['status'], 'pass')

    def test_music_family_count_and_witnesses_required(self):
        for text in ('\n'.join(x for x in COVERAGE.splitlines() if 'music:' not in x),
                     COVERAGE.replace('  ok   music: lobby clips:', '  ok   unrelated: lobby clips:', 1),
                     COVERAGE.replace('written as selector 2: no slot takes it', 'written as selector 2: level 6'),
                     COVERAGE.replace('the control, the same 90 on the event\'s own selector 4 plays the top level', 'the control: skipped')):
            self.assertEqual(classify('JUNGLE', 0, text + '\nPASS')['status'], 'missing_coverage')
        self.assertEqual(classify('JUNGLE', 0, COVERAGE + '\nPASS')['status'], 'pass')

    def test_research_family_count_and_witnesses_required(self):
        for text in ('\n'.join(x for x in COVERAGE.splitlines() if 'research:' not in x),
                     COVERAGE.replace('  ok   research: all 347 catalogue keys', '  ok   unrelated: all 347 catalogue keys', 1),
                     COVERAGE.replace('has exactly two callers', 'has three callers'),
                     COVERAGE.replace('starts with exactly the 13 items', 'starts with exactly the 41 items')):
            self.assertEqual(classify('JUNGLE', 0, text + '\nPASS')['status'], 'missing_coverage')
        self.assertEqual(classify('JUNGLE', 0, COVERAGE + '\nPASS')['status'], 'pass')

    def test_loans_family_count_and_witnesses_required(self):
        for text in ('\n'.join(x for x in COVERAGE.splitlines() if 'loans:' not in x),
                     COVERAGE.replace('  ok   loans: taking it a second time', '  ok   unrelated: taking it a second time', 1),
                     COVERAGE.replace('and the single debit (0x100B18)', 'and two debits'),
                     COVERAGE.replace('nothing sets +0x28 back', 'payoff sets +0x28 back')):
            self.assertEqual(classify('JUNGLE', 0, text + '\nPASS')['status'], 'missing_coverage')
        self.assertEqual(classify('JUNGLE', 0, COVERAGE + '\nPASS')['status'], 'pass')

    def test_parkstats_family_count_and_witnesses_required(self):
        for text in ('\n'.join(x for x in COVERAGE.splitlines() if 'parkstats:' not in x),
                     COVERAGE.replace('  ok   parkstats: k = 0 reads', '  ok   unrelated: k = 0 reads', 1),
                     COVERAGE.replace('then zeroes both', 'then keeps both'),
                     '\n'.join(x for x in COVERAGE.splitlines() if 'it assigns, it does not sum' not in x),
                     COVERAGE.replace('result into the bucket (0x186EAC', 'running sum (0x186EAC'),
                     COVERAGE.replace('12 features and nobody: 27', '12 features and nobody: 26')):
            self.assertEqual(classify('JUNGLE', 0, text + '\nPASS')['status'], 'missing_coverage')
        self.assertEqual(classify('JUNGLE', 0, COVERAGE + '\nPASS')['status'], 'pass')

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
        self.assertEqual(row['compiled_purchase_checks'], 77)
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



class GuardFamily(unittest.TestCase):
    def test_guard_family_counts_and_witnesses_required(self):
        for text in ('\n'.join(x for x in COVERAGE.splitlines() if 'guard:' not in x),
                     '\n'.join(x for x in COVERAGE.splitlines() if 'entertainer:' not in x),
                     COVERAGE.replace('  ok   entertainer: check\n', '', 1),
                     COVERAGE.replace('NO STINK', 'a stink'),
                     COVERAGE.replace('one leg: dispatched', 'two legs: dispatched'),
                     COVERAGE.replace('with no show draw rand(300) 0 times', 'with no show draw rand(300) 2 times')):
            self.assertEqual(classify('JUNGLE', 0, text + '\nPASS')['status'], 'missing_coverage')
        self.assertEqual(classify('JUNGLE', 0, COVERAGE + '\nPASS')['status'], 'pass')

class ManagementFamily(unittest.TestCase):
    def test_management_family_counts_and_witnesses_required(self):
        for text in ('\n'.join(x for x in COVERAGE.splitlines() if 'management:' not in x),
                     COVERAGE.replace('  ok   management: check\n', '', 1),
                     COVERAGE.replace('first corner the SMALLER', 'first corner the smaller'),
                     COVERAGE.replace('before the THIRD month end', 'before the second month end')):
            self.assertEqual(classify('JUNGLE', 0, text + '\nPASS')['status'], 'missing_coverage')
        self.assertEqual(classify('JUNGLE', 0, COVERAGE + '\nPASS')['status'], 'pass')

if __name__ == '__main__':
    unittest.main()
