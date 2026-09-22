#!/bin/sh
# Build the gate's fixtures with ffmpeg. They are SYNTHETIC -- a 440 Hz tone, and 440 left against
# 660 right for the stereo pair -- so nothing here is game data and nothing needs the disc.
#
# ⚠ The fixtures are NOT committed: the reference decodes are ~1.2 MB of PCM that would never
# delta in git history. Generate them next to the gate and run it there.
set -e
FF=${FFMPEG:-ffmpeg}
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

for f in mpeg1 mpeg2 st_mpeg1 st_mpeg2; do
  case $f in mpeg1) r=ref1;; mpeg2) r=ref2;; st_mpeg1) r=st_ref1;; st_mpeg2) r=st_ref2;; esac
  ext=mp2
  $FF -hide_banner -loglevel error -y -i $f.$ext -f s16le $r.raw
done
echo "fixtures built; now run: dotnet run --project <repo>/tools/TPW.PS2.Mp2Gate -c Release"
