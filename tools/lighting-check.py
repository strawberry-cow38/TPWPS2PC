#!/usr/bin/env python3
"""Check disc identities and real Godot pixels, then prove the pixel gate rejects a mutation.

Requires a display (Xvfb works), Godot 4.6.2 mono and the owner's PAL disc. Artifacts stay
outside Git. No game data is copied into the repository.
"""
import argparse
import os
from pathlib import Path
import subprocess

REPO = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("disc", type=Path)
    parser.add_argument("output", type=Path, help="new artifact directory outside Git")
    parser.add_argument("--godot", required=True, help="Godot mono executable")
    args = parser.parse_args()
    output = args.output.resolve()
    if any((p / ".git").exists() for p in (output, *output.parents)):
        parser.error("artifacts must be outside Git")
    output.mkdir(parents=True, exist_ok=False)
    env = dict(os.environ, TPW_PS2_DISC=str(args.disc.resolve()), TPW_PS2_CULL="back")
    env.pop("TPW_LIGHTING_MUTATION", None)
    env.pop("TPW_LIGHTING_ARTIFACTS", None)

    def run(label, command, extra=None, expected=0, marker=None):
        log = output / (label + ".log")
        with log.open("w") as stream:
            result = subprocess.run(command, cwd=REPO, env=env | (extra or {}),
                                    stdout=stream, stderr=subprocess.STDOUT, timeout=240)
        report = log.read_text()
        if result.returncode != expected or (marker and marker not in report):
            raise RuntimeError(f"{label}: exit {result.returncode}, expected {expected}; see {log}\n{report[-3000:]}")
        print(f"{label}: expected exit {expected}; {log}", flush=True)

    run("build", ["dotnet", "build", "game/TPWPS2Viewer.csproj", "-v:q"])
    run("data", ["dotnet", "run", "--project", "tools/TPW.PS2.LightingAudit", "--", str(args.disc.resolve())],
        marker="LIGHTING DATA PASS")
    base = [args.godot, "--path", str(REPO / "game"), "--audio-driver", "Dummy"]
    scene = "res://tests/LightingAudit.tscn"
    for renderer in ("gl_compatibility", "forward_plus"):
        command = base + ["--rendering-method", renderer, scene]
        run(renderer, command, {"TPW_LIGHTING_ARTIFACTS": str(output / renderer)},
            marker="LIGHTING RENDER PASS")
    command = base + ["--rendering-method", "gl_compatibility", scene]
    run("cull-off", command, {"TPW_PS2_CULL": "off"}, marker="LIGHTING RENDER PASS")
    run("drop-directional", command, {"TPW_LIGHTING_MUTATION": "drop-directional"}, expected=2,
        marker="SPACE-CLIFFS framebuffer mismatch")
    run("restored", command, marker="LIGHTING RENDER PASS")
    print("LIGHTING GATE PASS: disc identities, pixels, mutation rejection and restored pixels")


if __name__ == "__main__":
    main()
