#!/bin/sh
# Build the gate's fixtures with ffmpeg. They are SYNTHETIC -- a 440 Hz tone, and 440 left against
# 660 right for the stereo pair -- so nothing here is game data and nothing needs the disc.
#
# ⚠⚠ WRITES INTO A DIRECTORY OF ITS OWN, NEVER THE CURRENT ONE. The first version wrote to $PWD
# with no argument; run from the repo root it dropped 1.35 MB of untracked wav and mp2 into the
# working tree, one `git add -A` away from being committed -- which is exactly how 108 build
# artifacts got into this repo's history earlier the same night. The directory name is also in
# .gitignore, because a convention and a guard are not the same thing.
#
# usage: make-fixtures.sh [target-dir]      default: ./mp2-fixtures
set -e
DIR=${1:-mp2-fixtures}
FF=${FFMPEG:-ffmpeg}
mkdir -p "$DIR"
cd "$DIR"

$FF -hide_banner -loglevel error -y -f lavfi -i sine=frequency=440:duration=3 -ar 44100 -ac 1 src44.wav
$FF -hide_banner -loglevel error -y -i src44.wav -ar 22050 src22.wav
$FF -hide_banner -loglevel error -y -f lavfi -i sine=frequency=440:duration=3 \
    -f lavfi -i sine=frequency=660:duration=3 \
    -filter_complex "[0:a][1:a]amerge=inputs=2[a]" -map "[a]" -ar 44100 -ac 2 st44.wav
$FF -hide_banner -loglevel error -y -i st44.wav -ar 22050 st22.wav

$FF -hide_banner -loglevel error -y -i src44.wav -c:a mp2 -b:a 96k  mpeg1.mp2
$FF -hide_banner -loglevel error -y -i src22.wav -c:a mp2 -b:a 48k  mpeg2.mp2
$FF -hide_banner -loglevel error -y -i st44.wav  -c:a mp2 -b:a 192k st_mpeg1.mp2
$FF -hide_banner -loglevel error -y -i st22.wav  -c:a mp2 -b:a 96k  st_mpeg2.mp2

$FF -hide_banner -loglevel error -y -i mpeg1.mp2    -f s16le ref1.raw
$FF -hide_banner -loglevel error -y -i mpeg2.mp2    -f s16le ref2.raw
$FF -hide_banner -loglevel error -y -i st_mpeg1.mp2 -f s16le st_ref1.raw
$FF -hide_banner -loglevel error -y -i st_mpeg2.mp2 -f s16le st_ref2.raw

echo "fixtures in $(pwd)"
echo "run the gate from there:  dotnet <repo>/tools/TPW.PS2.Mp2Gate/bin/Release/net8.0/tpwps2mp2gate.dll"
echo "⚠ check the EXIT CODE directly -- a pipe replaces it with the pipeline's last command."
