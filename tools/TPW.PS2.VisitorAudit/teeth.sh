#!/usr/bin/env bash
set -euo pipefail
export MSBUILDDISABLENODEREUSE=1
if [[ $# -lt 1 || $# -gt 2 ]]; then echo 'usage: teeth.sh disc.bin [godot-4.6.2-mono]' >&2; exit 2; fi
cd "$(dirname "$0")/../.."
disc=$1
project=tools/TPW.PS2.VisitorAudit
work=$(mktemp -d /tmp/tpw-visitor-teeth.XXXXXX)
sim=core/TPW.PS2.Data/VisitorSimulation.cs
vm=core/TPW.PS2.Data/RseMachine.cs
view=game/VisitorParkView.cs
cp "$sim" "$work/sim.cs"
cp "$vm" "$work/vm.cs"
cp "$view" "$work/view.cs"
restore() { cp "$work/sim.cs" "$sim"; cp "$work/vm.cs" "$vm"; cp "$work/view.cs" "$view"; }
trap restore EXIT
run() { dotnet run --project "$project" -- "$disc" > "$work/$1.log" 2>&1; }
expect_failure() {
    local name=$1 message=$2 code=0
    run "$name" || code=$?
    if [[ $code != 1 ]] || ! rg -F "$message" "$work/$name.log"; then
        cat "$work/$name.log"; echo "Expected audit failure missing: $name (exit $code)" >&2; exit 1
    fi
    echo "TEETH $name: expected failure, exit $code"
}
run baseline
python3 - <<'PY'
from pathlib import Path
p = Path('core/TPW.PS2.Data/VisitorSimulation.cs')
s = p.read_text(); old = 'g.EdgeProgress += 100;'
assert s.count(old) == 1
p.write_text(s.replace(old, 'g.EdgeProgress += 0;'))
PY
expect_failure frozen_motion 'Ada movement at 100ms: expected 100, got 0'
restore
python3 - <<'PY'
from pathlib import Path
p = Path('core/TPW.PS2.Data/RseMachine.cs')
s = p.read_text(); old = '_stack[_guestTop++] = LastValue = V(0);'
assert s.count(old) == 1
p.write_text(s.replace(old, '_stack[_guestTop++] = LastValue = V(0) + 1;'))
PY
expect_failure wrong_guest 'Boarding identity lost: Ada (101) is absent from RSSE HUSH stack'
restore
run restored
if [[ $# == 2 ]]; then
    godot=$2
    build_game() { (cd game && dotnet build -m:1 -p:UseSharedCompilation=false) > "$work/$1-build.log" 2>&1; }
    geometry() {
        local name=$1; shift
        TPW_PS2_DISC="$disc" "$godot" --headless --path game res://tests/VisitorAudit.tscn "$@" > "$work/$name.log" 2>&1
    }
    expect_geometry_failure() {
        local name=$1 message=$2 code=0; shift 2
        geometry "$name" "$@" || code=$?
        if [[ $code != 2 ]] || ! rg -F "$message" "$work/$name.log"; then
            cat "$work/$name.log"; echo "Expected geometry identity failure missing: $name (exit $code)" >&2; exit 1
        fi
        echo "TEETH $name: expected failure, exit $code"
    }
    build_game game
    geometry geometry
    expect_geometry_failure rendered_position 'Ada rendered position identity at 100ms' -- --mutate-position
    # Revert the production sim -> world row conversion, leaving the audit unchanged.
    python3 - <<'PY'
from pathlib import Path
p = Path('game/VisitorParkView.cs')
s = p.read_text(); old = '(Park.Height - p.Z) * Park.CellSize'
assert s.count(old) == 1
p.write_text(s.replace(old, 'p.Z * Park.CellSize'))
PY
    build_game unreversed_rows
    expect_geometry_failure unreversed_rows 'Ada rendered position identity at 100ms'
    restore
    build_game restored
    geometry geometry-restored
fi
echo "PASS: baseline and restored audits pass; mutations fail by identity. Logs: $work"
