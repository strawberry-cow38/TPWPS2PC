#!/usr/bin/env python3
"""Teeth for `ParkSimAudit --advisor-only` (AdvisorChecks.cs): each mutation below is a plausible "fix" or
slip in the advisor's runtime (the scheduler, the producers' quirks, submit routing, the ring, the six
states, modal skipping, the message stack, the counters' gate, the park-start reset, the direct emitters),
and each must turn the audit RED. The source is restored in a `finally`, then rebuilt. No disc data is read
or written by this script; the audit reads the disc you pass. (The shape is tools/management_teeth.py's.)

  python3 tools/advisor_teeth.py --disc DISC [--world JUNGLE --terrain 1] [name ...]

Exit 0 when every selected mutation went red, 1 when one survived, failed to build, or its pattern no
longer matches (the source moved: update the pattern, do not drop the mutation).

Step B added 14: the head's channel (the exit queued behind the talk loop, the cut, the endpoint, the queue
beating the loop), the lips and mouth, the costume call, and the removal path from ParkSim.Remove.

EXCLUDED, with the reason:
- "read lastFail as a u32, not a u16" (`(ushort)_lastFail[cr]` -> `_lastFail[cr]`): an EQUIVALENT mutant.
  v78 is stored as an s16, and the low 16 bits of `day - lastFail` and of `day - (u16)lastFail` are the
  same, so the native `lhu` cannot be observed through v78 (the check pins the s16 wrap instead).
- "pop the ring before playing" (0x107870 plays the tail slot, then advances the tail): the same slot is
  played either way.
- "a lip mark consumed at EQUAL ms" (`<` -> `<=` in LipTrack.Playback): equivalent over sp_001, whose marks'
  whole ms (2226, 2812, 4058) never land on a 40 ms pass; the strict comparison is pinned by
  tools/TPW.PS2.AdvisorAudit's own lip checks.
"""
import argparse, pathlib, subprocess, sys

R = pathlib.Path(__file__).resolve().parent.parent
C = 'core/TPW.PS2.Data/'
A = C + 'ParkAdvisor.cs'; S = C + 'AdvisorScheduler.cs'; K = C + 'AdvisorMessageStack.cs'; P = C + 'AdvisorProducers.cs'
PM = C + 'ParkManagement.cs'; RM = C + 'ResearchManager.cs'; ME = C + 'Mechanic.cs'; RS = C + 'RideService.cs'
PS = C + 'ParkSim.cs'
# (name, file, old, new, which occurrence of old)
MUT = [
 # The scheduler (0x10DC50).
 ('warmup-4-a-call', S, 'public const int WarmUpRefreshes = 5;', 'public const int WarmUpRefreshes = 4;', 0),
 ('warmup-no-eval-on-16th', S, 'if (VariableCursor == 0) { Warm = true; break; }',
  'if (VariableCursor == 0) { Warm = true; Last = new StepReport(refreshed, false, -1, false, null, Array.Empty<AdvisorRules.Effect>()); return; }', 0),
 ('two-rules-a-call', S, 'RuleCursor = (RuleCursor + 1) % RuleCount;', 'RuleCursor = (RuleCursor + 2) % RuleCount;', 0),
 ('gate-advances-cursor', S, 'if (!ringEmpty()) { Last', 'if (!ringEmpty()) { RuleCursor = (RuleCursor + 1) % RuleCount; Last', 0),
 ('no-ring-gate', S, 'if (!ringEmpty()) { Last', 'if (false) { Last', 0),
 ('due-inclusive', S, 'bool due = _next[cr] < day;', 'bool due = _next[cr] <= day;', 0),
 ('next-without-delay', S, '_next[cr] = unchecked(day + _rules.Rules[cr].DelayDays);', '_next[cr] = day;', 0),
 ('lastfail-on-held-block', S, 'else if (e.Result == AdvisorRules.Result.ConditionFailed) _lastFail[cr] = day;', 'else _lastFail[cr] = day;', 0),
 ('v78-saturates', S, 'Variables[ElapsedVariable] = unchecked((short)(day - (ushort)_lastFail[cr]));',
  'Variables[ElapsedVariable] = (short)Math.Min(day - _lastFail[cr], (uint)short.MaxValue);', 0),
 ('loader-lastfail-zero', S, '_lastFail[i] = _day();', '_lastFail[i] = 0;', 0),
 ('v52-reads-the-counter', S, 'Variables[52] = Variables[75];', 'Variables[52] = Counters[19];', 0),
 ('v53-breaks', S, 'Variables[53] = Variables[76];                             // fall-through into 0x10E484', '', 0),
 ('counter-no-clamp', S, 'if (c > CounterLimit) c = CounterLimit;', '', 0),
 # Submitting and the ring (0x1075F0, 0x107640, 0x107760).
 ('immediate-only-by-flag', A, '(Flags & FlagQueue) == 0 || unchecked((ushort)(request.Id - ImmediateFirst)) < ImmediateCount',
  '(Flags & FlagQueue) == 0', 0),
 ('immediate-signed-range', A, '(Flags & FlagQueue) == 0 || unchecked((ushort)(request.Id - ImmediateFirst)) < ImmediateCount',
  '(Flags & FlagQueue) == 0 || request.Id - ImmediateFirst < ImmediateCount', 0),
 ('ring-no-dedup', A, 'if (_ring[i].Id == r.Id) return;', '', 0),
 ('ring-drops-newest', A, 'if (RingHead == RingTail) { RingTail = (RingTail + 1) % RingSlots; Overflows++; }',
  'if (RingHead == RingTail) { RingHead = (RingHead - 1 + RingSlots) % RingSlots; Overflows++; }', 0),
 ('ring-21-slots', A, 'public const int RingSlots = 0x14;', 'public const int RingSlots = 0x15;', 0),
 ('interrupt-in-2-pops-the-ring', A,
  'if (State == AdvisorState.Entering) { Countdown = ExitCountdown; State = AdvisorState.Exiting; HeadDown(cut: false); }\n        else if (State == AdvisorState.Speaking)\n        {\n            StopSpeech();                                                  // "SHUTTING UP',
  'if (State == AdvisorState.Entering) { Countdown = ExitCountdown; State = AdvisorState.Exiting; HeadDown(cut: false); RingTail = (RingTail + 1) % RingSlots; }\n        else if (State == AdvisorState.Speaking)\n        {\n            StopSpeech();                                                  // "SHUTTING UP', 0),
 ('interrupt-keeps-speaking', A, 'StopSpeech();                                                  // "SHUTTING UP', '// "SHUTTING UP', 0),
 # The six states (0x1066B0).
 ('start-delay-49', A, 'public const int StartDelayTicks = 0x32;', 'public const int StartDelayTicks = 0x31;', 0),
 ('cooldown-99', A, 'public const int CooldownTicks = 100;', 'public const int CooldownTicks = 99;', 0),
 ('silent-is-free', A, '                if (CurrentId == Bankrupted) GameOver?.Invoke();           // 0x13BDD0',
  '                if (SpeechSoundId == 0) { State = AdvisorState.Idle; break; }\n                if (CurrentId == Bankrupted) GameOver?.Invoke();           // 0x13BDD0', 0),
 ('state3-waits-a-tick', A, 'Exiting();                                                 // goto 0x106BF8: state 4 in the same tick', '', 0),
 ('cooldown-ignores-pending', A, 'if (Countdown < 1 || (Flags & FlagCooldown) == 0 || PendingSet) State', 'if (Countdown < 1 || (Flags & FlagCooldown) == 0) State', 0),
 ('greeting-inverted', A, '(GoldTicketsEarned?.Invoke() ?? 0) == 0 ? GreetingFirst : GreetingOther', '(GoldTicketsEarned?.Invoke() ?? 0) != 0 ? GreetingFirst : GreetingOther', 0),
 ('talk-11000-is-6', A, "return 0x2AF7 < len ? 3 : 6;", "return 0x2AF8 < len ? 3 : 6;", 0),
 # Modal and the skip (0x1074C0, 0x107530).
 ('bankrupt-not-modal', A, 'if (unchecked((ushort)(id - ImmediateFirst)) < ImmediateCount || id == Bankrupted)',
  'if (unchecked((ushort)(id - ImmediateFirst)) < ImmediateCount)', 0),
 ('skip-clears-latch', A, 'PendingSet = false;\n                GetTheAdvisorOffTheScreen();', 'PendingSet = false; SkipLatched = false;\n                GetTheAdvisorOffTheScreen();', 0),
 ('modal-end-no-flags', A, 'if (ModalActive) Flags |= FlagText | FlagQueue;', '', 0),
 # Text and the stack (0x1078E8, 0x1073F0, 0x108598, 0x108808, 0x13D8C0, 0x1C2D50).
 ('text-for-row-310', A, 'bool text = (Flags & FlagText) != 0 && row != AdvisorCatalogue.BlankTextRow;', 'bool text = (Flags & FlagText) != 0;', 0),
 ('opcode8-no-retract', A, '        Stack.RemoveByRow(row);\n    }', '    }', 0),
 ('text-ignores-flag', A, 'bool text = (Flags & FlagText) != 0 && row != AdvisorCatalogue.BlankTextRow;', 'bool text = row != AdvisorCatalogue.BlankTextRow;', 0),
 ('stack-31', K, 'public const int Capacity = 32;', 'public const int Capacity = 31;', 0),
 ('removebyrow-flush-first', K,
  '        for (int i = 0; i < Count; i++)\n        {\n            if (_slots[i].Row != row) continue;\n            FlushRemoval();\n            MarkForRemoval(i);',
  '        FlushRemoval();\n        for (int i = 0; i < Count; i++)\n        {\n            if (_slots[i].Row != row) continue;\n            MarkForRemoval(i);', 0),
 ('objectremoved-first', K, 'if (_slots[i].Type == AdvisorRecordType.Object) MarkForRemoval(i);',
  'if (_slots[i].Type == AdvisorRecordType.Object) { MarkForRemoval(i); break; }', 0),
 ('save-keeps-goals', K, 'if (r.Type == AdvisorRecordType.Goal) continue;', '', 0),
 ('open-at-newest', K, 'Cursor = 0; ScrollTop = 0; Deleting = false; IsOpen = true;', 'Cursor = Count - 1; ScrollTop = 0; Deleting = false; IsOpen = true;', 0),
 # The counters (0x1073C0).
 ('counters-ignore-flags', A, '        if ((Flags & FlagRules) == 0) return;\n        Scheduler?.AddCounter(j, d);', '        Scheduler?.AddCounter(j, d);', 0),
 ('ride-events-not-forwarded', RS, '        RaiseAdvisorEvent(slot, delta);\n', '', 0),
 # The producers.
 ('needclass-thirst-81', P, 'if (thirst > 75) return 1;', 'if (thirst > 80) return 1;', 0),
 ('parksize-standing-only', P, 'default: if (p.Kind == AssetResourceDatabase.AssetKind.Feature) s += 5; break;',
  'default: if (p.Kind == AssetResourceDatabase.AssetKind.Feature && p.Status != 0) s += 5; break;', 0),
 ('rides-standing-only', P, 'else if ((mask & PoolBit(p.Kind)) != 0) n++;', 'else if ((mask & PoolBit(p.Kind)) != 0 && p.Status != 0) n++;', 0),
 ('balance-no-clamp', P, 'return b > 30000 ? 30000 : b < -30000 ? -30000 : b;', 'return b;', 0),
 ('park-closed-by-default', P, 'case 0: return S(ParkOpen?.Invoke() ?? true ? 1 : 0);', 'case 0: return S(ParkOpen?.Invoke() ?? false ? 1 : 0);', 0),
 # Park start (0x151498, 0x10DA78).
 ('no-rule-object-off-test-park', A, '(byte)(ParkStartFlags | (tutorial ? FlagTutorial : 0) | (testPark ? 0 : FlagRules))',
  '(byte)(ParkStartFlags | (tutorial ? FlagTutorial : 0))', 0),
 # Direct emitters.
 ('coaster-204-once', A, '        if (!closed) Submit(new AdvisorRequest(0xCC));\n        if (!valid)', '        if (!valid)', 0),
 ('coaster-stock-3', A, 'if ((parkIndex == 2 ? 14 : 2) - coastersInUse == 0)', 'if ((parkIndex == 2 ? 14 : 3) - coastersInUse == 0)', 0),
 ('ride-without-object', A, 'sim.Advisor = (id, ride) => Submit(new AdvisorRequest(unchecked((ushort)id), ride));',
  'sim.Advisor = (id, ride) => Submit(new AdvisorRequest(unchecked((ushort)id)));', 0),
 ('red-no-206', PM, 'if (RedMonths == 1) { Advisor?.Invoke(MessageInTheRed); Advisor?.Invoke(MessageRedFirst); }',
  'if (RedMonths == 1) { Advisor?.Invoke(MessageRedFirst); }', 0),
 ('red-from-the-second', PM, 'else if (RedMonths == 3) Advisor?.Invoke(MessageRedThird);', 'else if (RedMonths >= 2) Advisor?.Invoke(MessageRedThird);', 0),
 ('red-never-resets', PM, 'if (Finances.Balance >= 0) RedMonths = 0;', 'if (Finances.Balance >= 0) { }', 0),
 ('research-276-dropped', RM, '_ => AdvisorRequest.UnsetId,', '_ => null,', 0),
 ('upgrade-refusal-silent', ME, '{ Advisor?.Invoke(StaffTables.UpgradeNoMechanicsMessage); return UpgradeRequest.NoMechanics; }',
  '{ return UpgradeRequest.NoMechanics; }', 0),
 # Step B: the head's channel (0x1ABC80, 0x1AC048).
 ('exit-never-queues', A, 'if (unfinished && (flags & 2) == 0) { Queued = record; QueuedFlags = flags; return; }   // 0x1ABD50', '', 0),
 ('cut-exit-queues', A, 'if (unfinished && (flags & 2) == 0) {', 'if (unfinished) {', 0),
 ('endpoint-inclusive', A, 'if (!(Length(Record) < ElapsedMs * (float)FramesPerSecond / 1000f)) return;',
  'if (!(Length(Record) <= ElapsedMs * (float)FramesPerSecond / 1000f)) return;', 0),
 ('loop-beats-queue', A, 'if (Queued != None) { int q = Queued, f = QueuedFlags; Queued = None; Start(q, f); }   // 0x1AC2C4\n        else if (Looping)',
  'if (Looping) { ElapsedMs = 0; Starts++; }\n        else if (Queued != None) { int q = Queued, f = QueuedFlags; Queued = None; Start(q, f); }\n        else if (false)', 0),
 # Step B: the lips and the mouth (0x105F30, 0x106B54..0x106BEC, 0x106600) and the costume (0x107B90).
 ('lip-gate-ignored', A, 'LipGate = _lip.Advance(unchecked((uint)SpeechElapsedMs));', '_lip.Advance(unchecked((uint)SpeechElapsedMs));', 0),
 ('mouth-aah-is-2', A, 'else if (LipGate) { SetMouth(1); return; }', 'else if (LipGate) { SetMouth(2); return; }', 0),
 ('mouth-flaps-with-gate-clear', A, '        if (!stepped)\n        {\n            if (LipGate)', '        if (!stepped)\n        {\n            if (true)', 0),
 ('mouth-never-random', A, 'if (NextRand() % 5 == 0) SetMouth', 'if (false) SetMouth', 0),
 ('headdown-keeps-mouth', A, '        _lip = null;\n        SetMouth(0);\n    }', '        _lip = null;\n    }', 0),
 ('costume-not-dressed', A, '            Head.Dress(Costume);                                           // 0x107B90: +0x1F8, then 0x107160\n', '', 0),
 # Step B: an object leaving the park (0x14A7B0 → 0x1E12E0 → 0x1088E0, then 0x13D8C0).
 ('removal-not-wired', A, '            sim.ObjectRemoved = ObjectLeftPark;\n', '', 0),
 ('sim-remove-silent', PS, '        ObjectRemoved?.Invoke(_rides.FirstOrDefault(r => r.Id == id));\n', '', 0),
 ('removal-object-first', A,
  '        if (ride != null) Stack.RideRemoved(ride);                         // vt+0x10C = 0x1E12E0 → 0x1088E0\n        Stack.ObjectRemoved();                                             // 0x13D8C0',
  '        Stack.ObjectRemoved();\n        if (ride != null) Stack.RideRemoved(ride);', 0),
 ('removal-ride-only', A, '        Stack.ObjectRemoved();                                             // 0x13D8C0\n', '', 0),
]
PROJECT = 'tools/TPW.PS2.ParkSimAudit'
DLL = PROJECT + '/bin/Release/net8.0/TPW.PS2.ParkSimAudit.dll'


def build():
    r = subprocess.run(['dotnet', 'build', PROJECT, '-c', 'Release', '--nologo', '-v', 'q'], cwd=R, capture_output=True, text=True)
    return r.returncode, r.stdout[-1500:]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--disc', required=True)
    ap.add_argument('--world', default='JUNGLE')
    ap.add_argument('--terrain', default='1')
    ap.add_argument('names', nargs='*')
    a = ap.parse_args()
    unknown = set(a.names) - {m[0] for m in MUT}
    if unknown:
        sys.exit(f'unknown mutation(s): {sorted(unknown)}')
    bad = 0
    for name, f, old, new, nth in MUT:
        if a.names and name not in a.names:
            continue
        path = R / f
        orig = path.read_text()
        try:
            if orig.count(old) <= nth:
                print(f'{name}: PATTERN GONE ({orig.count(old)} matches) -- update it'); bad += 1; continue
            i = -1
            for _ in range(nth + 1):
                i = orig.index(old, i + 1)
            path.write_text(orig[:i] + new + orig[i + len(old):])
            rc, out = build()
            if rc != 0:
                print(f'{name}: BUILD FAILED\n{out}'); bad += 1; continue
            r = subprocess.run(['timeout', '900', 'dotnet', DLL, a.disc, a.world, f'--terrain={a.terrain}', '--advisor-only'],
                               cwd=R, capture_output=True, text=True)
            fails = [l.strip() for l in (r.stdout + r.stderr).splitlines() if l.strip().startswith('FAIL ')]
            crashed = r.returncode != 0 and not fails and 'Unhandled exception' in (r.stdout + r.stderr)
            if r.returncode != 0 and fails:
                print(f'{name}: RED -- {fails[0][:200]}', flush=True)
            elif crashed:
                print(f'{name}: SURVIVED as a CRASH, not a FAIL line (exit {r.returncode})', flush=True); bad += 1
            else:
                print(f'{name}: SURVIVED (exit {r.returncode})', flush=True); bad += 1
        finally:
            path.write_text(orig)
    rc, _ = build()
    print(f'restored and rebuilt (exit {rc}); {bad} mutation(s) not red')
    sys.exit(1 if bad or rc else 0)


if __name__ == '__main__':
    main()
