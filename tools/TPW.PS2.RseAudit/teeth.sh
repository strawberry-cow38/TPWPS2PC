#!/usr/bin/env bash
# Mutants must fail for the intended identity mismatch, not merely return nonzero.
set -euo pipefail
disc=${1:?usage: teeth.sh /path/to/disc.bin [/path/to/godot]}
cd "$(dirname "$0")/../.."
audit_log_dir=$(mktemp -d)
trap 'rm -rf "$audit_log_dir"' EXIT
dotnet run --project tools/TPW.PS2.RseAudit -- "$disc"
for mutation in add sub; do
    result=0
    dotnet run --no-build --project tools/TPW.PS2.RseAudit -- "$disc" "--mutate-$mutation" > "$audit_log_dir/$mutation.log" 2>&1 || result=$?
    test "$result" = 1
    if test "$mutation" = add; then
        rg -F 'child tick 1: VAR_TEMP: expected 25, got 1' "$audit_log_dir/$mutation.log"
    else
        rg -F 'spider timeout t=800: expected 10000, got -10000' "$audit_log_dir/$mutation.log"
    fi
done
if test "$#" -ge 2; then
    dotnet build game/TPWPS2Viewer.csproj
    TPW_PS2_DISC="$disc" "$2" --headless --path game res://tests/RseAnimationAudit.tscn > "$audit_log_dir/geometry.log" 2>&1
    rg -F 'RSE ANIMATION PASS:' "$audit_log_dir/geometry.log"
    result=0
    TPW_PS2_DISC="$disc" "$2" --headless --path game res://tests/RseAnimationAudit.tscn -- --mutate-freeze > "$audit_log_dir/freeze.log" 2>&1 || result=$?
    test "$result" = 2
    rg -F 't=19000: rendered vertex identity differs from Main:1 at frame 4.02' "$audit_log_dir/freeze.log"
fi
echo 'PASS: each deliberately broken execution failed its specific identity assertion'
