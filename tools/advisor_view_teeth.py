#!/usr/bin/env python3
"""Teeth for the advisor's VIEW (game/tests/AdvisorSmoke.cs): each mutation below is a plausible slip in what
the player sees and hears -- the head's costume, mouth and visibility, the voice's length and bus, the ducking,
the stack's drawing and keys, the pad lock and the skip, the jump, leaving and teardown -- and each must turn
the rendered smoke RED on one park. The source is restored in a `finally`, then rebuilt. The smoke reads the
disc you pass; nothing is written from it. (The shape is tools/advisor_teeth.py's, with a Godot run in place
of the audit.)

  python3 tools/advisor_view_teeth.py --disc DISC --godot GODOT [--map 'JUNGLE  terrain_1.mps'] [name ...]

Exit 0 when every selected mutation went red, 1 when one survived, failed to build, or its pattern no longer
matches (the source moved: update the pattern, do not drop the mutation). A mutation whose `old` is a list of
(old, new) pairs makes every one of those edits, each on its first occurrence (a move is two edits).
"""
import argparse, os, pathlib, subprocess, sys

R = pathlib.Path(__file__).resolve().parent.parent
H = 'game/AdvisorHead.cs'; A = 'game/Viewer.Advisor.cs'; M = 'game/GameAudioMix.cs'; K = 'game/AdvisorStackView.cs'
V = 'game/Viewer.cs'; S = 'game/Viewer.Staff.cs'
FILL = ('        if (_panel != null)\n'
        '            for (int i = 0; i < h >> 4; i++) Put("fill", _panel.Sheet, R(x, y + FillRow * i, w, FillRow), Colors.White);\n')
TEXT = '                    Put("text", tex, R(BoxCentre - width / 2, BoxY + _textFont.LineAdvance * i, width, tex.GetHeight()), TextColour);\n                }\n'
# (name, file, old, new, which occurrence of old)
MUT = [
 # The head.
 ('no-costume', H, '        Dressed = selector;\n        Hide(14);', '        Dressed = selector;\n        if (selector >= 0) return;\n        Hide(14);', 0),
 ('mouth-not-written', H, 'if (_mouths[i] >= 0) Drawn.WriteNodeFlags(_mouths[i], i == shape ? 0u : 0x8000u, i == shape ? 0x8000u : 0u);', '{ }', 0),
 ('mouth-inverted', H, 'i == shape ? 0u : 0x8000u, i == shape ? 0x8000u : 0u', 'i == shape ? 0x8000u : 0u, i == shape ? 0u : 0x8000u', 0),
 ('head-up-when-held', H, '&& !(Channel.Record == ParkAdvisor.RecordExit && Channel.EndHeld);', ';', 0),
 # The voice.
 ('length-from-header', A, 'int ms = (int)Math.Round(wav.GetLength() * 1000.0);', 'int ms = b.Sound.Milliseconds;', 0),
 ('speech-ducked', A, 'new AudioStreamPlayer { Name = "AdvisorVoice", Bus = "Master" }', 'new AudioStreamPlayer { Name = "AdvisorVoice", Bus = GameAudioMix.SfxBus }', 0),
 ('voice-not-stopped', A, 'SpeechStopped = () => { if (_advisorVoice != null && _advisorVoice.Playing)', 'SpeechStopped = () => { if (false)', 0),
 # The ducking (0x1066B0 states 2/4, 0x151C00).
 ('ramp-16', M, 'public const int RampStep = 8;', 'public const int RampStep = 16;', 0),
 ('no-duck', M, 'if (settings.Sfx > DuckLevel) SfxTarget = DuckLevel;', '', 0),
 ('duck-not-restored', M, 'else { SfxTarget = settings.Sfx; MusicTarget = settings.Music; }', 'else { }', 0),
 # The stack's drawing and keys.
 ('count-off-by-one', K, '_countText = _countValue.ToString();', '_countText = (_countValue - 1).ToString();', 0),
 ('text-of-newest', K, 'if (_stack.IsOpen && _stack.Selected is { } sel)', 'if (_stack.IsOpen && _stack.Records.LastOrDefault() is { } sel)', 0),
 ('l2-ignored', A, 'if (stack.IsOpen || AdvisorStackMayOpen()) _advisorL2 = true;', '', 0),
 ('delete-is-select', A, 'case Key.Delete: stack.Press(AdvisorStackButtons.Delete); return true;', 'case Key.Delete: stack.Press(AdvisorStackButtons.Select); return true;', 0),
 ('focus-not-wired', A, 'adv.Stack.FocusObject = AdvisorFocus;', '', 0),
 ('jump-to-gone-ride', A, 'if (_sim == null || !_sim.Rides.Contains(ride))', 'if (_sim == null)', 0),
 # The pad lock and the skip.
 ('lock-leaks', A, 'if (AdvisorPadLocked) { GD.Print(', 'if (false) { GD.Print(', 0),
 ('skip-through-lock', A, '_parkAdvisor.SkipHeld = Input.IsKeyPressed(AdvisorTriangleKey);', '_parkAdvisor.SkipHeld = !AdvisorPadLocked && Input.IsKeyPressed(AdvisorTriangleKey);', 0),
 # Leaving and teardown.
 ('close-park-keeps-talking', V, 'AdvisorLeavingPark("close park (0x1510F8)"); ', '', 0),
 ('teardown-keeps-head', S, '        ResetAdvisor();                                                  // Viewer.Advisor.cs: the head freed, the voice stopped\n', '', 0),
 # The read box (ghidra_tpw/notes/advisor-V-visuals.md §1): face, geometry, frame, depth, text.
 ('box-face-messfill', K, 'Put("fill", _panel.Sheet,', 'Put("fill", _panel.Fill,', 0),
 ('box-margins-inward', K, 'int x = BoxX - MarginX, y = BoxY - MarginY, w = BoxWidth + 2 * MarginX, h = BoxHeight + 2 * MarginY;',
  'int x = BoxX + MarginX, y = BoxY + MarginY, w = BoxWidth - 2 * MarginX, h = BoxHeight - 2 * MarginY;', 0),
 ('box-centred-in-window', K, 'float X(int u) => BoxCentre * sx + (u - BoxCentre) * k;', 'float X(int u) => Native / 2 * sx + (u - BoxCentre) * k;', 0),
 ('box-fill-one-rect', K, 'for (int i = 0; i < h >> 4; i++) Put("fill", _panel.Sheet, R(x, y + FillRow * i, w, FillRow), Colors.White);',
  'Put("fill", _panel.Sheet, R(x, y, w, h), Colors.White);', 0),
 ('box-no-frame', K, '        if (_panel != null)\n        {\n            // Corners', '        if (false)\n        {\n            // Corners', 0),
 ('box-left-edge-unturned', K, 'Put("edge", _panel.EdgeLeft,', 'Put("edge", _panel.EdgeRight,', 0),
 ('fill-drawn-last', K, [(FILL, ''), (TEXT, TEXT + FILL)], None, 0),
 ('fill-recorded-not-drawn', K, '            DrawTextureRect(tex, rect, false, colour);', '            if (piece != "fill") DrawTextureRect(tex, rect, false, colour);', 0),
 ('box-font-console', A, 'ReadGeneric("/Fonts/European/Small.bff")', 'ReadGeneric("/Fonts/European/Console.bff")', 0),
 ('text-white', K, 'tex.GetHeight()), TextColour);', 'tex.GetHeight()), Colors.White);', 0),
 ('text-left-aligned', K, 'Put("text", tex, R(BoxCentre - width / 2,', 'Put("text", tex, R(BoxX,', 0),
 ('text-inset', K, 'Put("text", tex, R(BoxCentre - width / 2, BoxY + _textFont.LineAdvance * i,',
  'Put("text", tex, R(BoxCentre - width / 2, BoxY + MarginY + _textFont.LineAdvance * i,', 0),
 ('text-no-shadow', K, 'Put("shadow", tex,', 'if (false) Put("shadow", tex,', 0),
 # The control: an authored square must draw square by the HUD's mapping.
 ('envelope-stretched', K, 'new Vector2(Sprite, Sprite) * k);\n        if (_closed != null)', 'new Vector2(Sprite * sx, Sprite * k));\n        if (_closed != null)', 0),
 # The head's transform (§2.2): 0x16FD18(s, 4s/3, s) on the root, whose local y is depth; the origin at NDC (0.6, −0.5).
 ('head-4-3-uniform', H, 'new Vector3(Scale / r0.Length(), Scale * 4f / 3f / r1.Length(), Scale / r2.Length())',
  'new Vector3(Scale * 4f / 3f / r0.Length(), Scale * 4f / 3f / r1.Length(), Scale * 4f / 3f / r2.Length())', 0),
 ('head-4-3-on-screen-y', H, 'Scale * 4f / 3f / r1.Length(), Scale / r2.Length()', 'Scale / r1.Length(), Scale * 4f / 3f / r2.Length()', 0),
 ('head-no-depth-4-3', H, 'Scale * 4f / 3f / r1.Length()', 'Scale / r1.Length()', 0),
 ('head-anchor-no-aspect', H, 'new Vector3(AnchorX * aspect, AnchorY, AnchorZ)', 'new Vector3(AnchorX, AnchorY, AnchorZ)', 0),
 ('head-anchor-y-on-depth', H, 'new Vector3(AnchorX * aspect, AnchorY, AnchorZ)', 'new Vector3(AnchorX * aspect, AnchorZ, AnchorY)', 0),
]


def build():
    r = subprocess.run(['dotnet', 'build', 'game/TPWPS2Viewer.csproj', '-c', 'Debug', '--nologo', '-v', 'q'], cwd=R, capture_output=True, text=True)
    return r.returncode, r.stdout[-1500:]


def smoke(disc, godot, map_label):
    env = dict(os.environ, TPW_PS2_DISC=disc)
    env.pop('TPW_ADVISOR_SHOT', None)
    r = subprocess.run(['timeout', '900', 'xvfb-run', '-a', godot, '--rendering-method', 'gl_compatibility', '--audio-driver', 'Dummy',
                        '--resolution', '640x360', '--path', 'game', 'res://tests/AdvisorSmoke.tscn', '--', f'--map={map_label}', '--mode=park'],
                       cwd=R, capture_output=True, text=True, env=env)
    text = r.stdout + r.stderr
    passed = [l for l in text.splitlines() if l.startswith('ADVISOR SMOKE PASS')]
    failed = [l.strip() for l in text.splitlines() if 'ADVISOR SMOKE FAILED' in l]
    return r.returncode, passed, failed


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--disc', required=True)
    ap.add_argument('--godot', required=True)
    ap.add_argument('--map', default='JUNGLE  terrain_1.mps')
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
            edits = old if isinstance(old, list) else [(old, new)]
            text, gone = orig, None
            for o, n in edits:
                at = nth if len(edits) == 1 else 0
                if text.count(o) <= at:
                    gone = text.count(o); break
                i = -1
                for _ in range(at + 1):
                    i = text.index(o, i + 1)
                text = text[:i] + n + text[i + len(o):]
            if gone is not None:
                print(f'{name}: PATTERN GONE ({gone} matches) -- update it', flush=True); bad += 1; continue
            path.write_text(text)
            rc, out = build()
            if rc != 0:
                print(f'{name}: BUILD FAILED\n{out}', flush=True); bad += 1; continue
            code, passed, failed = smoke(a.disc, a.godot, a.map)
            if failed and not passed and code != 0:
                print(f'{name}: RED -- {failed[0][:240]}', flush=True)
            else:
                print(f'{name}: SURVIVED (exit {code}, {passed[:1]})', flush=True); bad += 1
        finally:
            path.write_text(orig)
    rc, _ = build()
    print(f'restored and rebuilt (exit {rc}); {bad} mutation(s) not red')
    sys.exit(1 if bad or rc else 0)


if __name__ == '__main__':
    main()
