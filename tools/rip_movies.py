"""Rip `/MOVIES/*.MPC` off the PS2 disc to Ogg Theora -- the one video format Godot 4 plays
out of the box (`VideoStreamTheora`, whose `File` takes a runtime path, so the output does NOT
have to be imported into the project and can live in the game's install or user directory).

⭐ **ffmpeg reads the raw `.MPC` directly.** The container is EA's `SCHl`/`MPCh` stream and
ffmpeg's `ea` demuxer already knows it, so there is no decoder to write: `MPCh` chunks are plain
**MPEG-2 video** and `SCDl` is **EA-XA ADPCM** (`adpcm_ea`). `tools/mpc.py` parses the same
container by hand and the two agree on every field, which is how the format was confirmed rather
than assumed. This script only has to lift the file off the disc and hand it over.

⚠⚠ **THE MOVIES ARE ANAMORPHIC.** The eight story movies are 640x352 flagged `SAR 11:15`, i.e.
a 4:3 display aspect; the three `FE*` front-end movies are 640x480. Godot's `VideoStreamPlayer`
does not honour a stream's sample aspect, so the correction is BAKED here (`scale=...,setsar=1`)
rather than left as metadata. Skip that and every story movie plays vertically squashed -- which
looks like a bad rip of a fine file.

⚠ Two frame rates, and they are not interchangeable: the story movies run at **30 fps**, the
`FE*` ones at **25**. ffmpeg carries that across on its own; it is recorded here because a
hand-built pipeline that assumes one rate silently desyncs the other set.

⚠ The last audio packet of a file makes `adpcm_ea` print "mismatch in coded sample count". It is
cosmetic: the decoded vorbis duration matches the source's `num_samples / sample_rate` to four
decimal places, so nothing is lost. Do not "fix" it by re-muxing.

Usage:
    python3 tools/rip_movies.py <disc.bin> <outdir> [--ffmpeg PATH] [--force] [--only NAME]

Outputs `<outdir>/<NAME>.ogv`. Existing outputs are kept unless `--force`, so the whole thing is
safe to run as a first-run step: the second launch does nothing. ⚠ `<outdir>` is the game's
install/user directory -- NEVER the repository, which ships no game data.
"""
import os, subprocess, sys, tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import iso2
import mpc

# The console renders 4:3. Square-pixel output at this size covers both source shapes.
TARGET_W, TARGET_H = 640, 480


def movies(disc):
    """[(name, extent, size)] for every `/MOVIES/*.MPC`, from the disc's own directory records."""
    with open(disc, 'rb') as f:
        pvd = iso2.sector(f, 16)
        root = pvd[156:156 + 34]
        import struct
        rext = struct.unpack_from('<I', root, 2)[0]
        rlen = struct.unpack_from('<I', root, 10)[0]
        out = []
        iso2.walk(f, rext, rlen, '', out)
    found = []
    for name, ext, size in out:
        clean = name.split(';')[0]
        if clean.upper().startswith('/MOVIES/') and clean.upper().endswith('.MPC'):
            found.append((os.path.basename(clean)[:-4], ext, size))
    return sorted(found)


def rip_one(disc, name, extent, size, outdir, ffmpeg, force):
    """Lift one movie off the disc and transcode it. Returns (ok, message)."""
    dest = os.path.join(outdir, name + '.ogv')
    if os.path.exists(dest) and not force:
        return True, 'kept (already ripped)'

    # ⚠ Written to a real file rather than piped: ffmpeg's `ea` demuxer seeks, and a pipe makes it
    # misreport the streams instead of failing outright.
    tmp = None
    try:
        raw = mpc.disc_file(disc, extent, size)
        fd, tmp = tempfile.mkstemp(suffix='.MPC')
        with os.fdopen(fd, 'wb') as fh:
            fh.write(raw)
        cmd = [ffmpeg, '-y', '-hide_banner', '-v', 'error', '-i', tmp,
               '-c:v', 'libtheora', '-q:v', '7',
               '-vf', 'scale=%d:%d,setsar=1' % (TARGET_W, TARGET_H),
               '-c:a', 'libvorbis', '-q:a', '4', dest]
        p = subprocess.run(cmd, capture_output=True, text=True)
        if p.returncode != 0:
            # ⚠ Report ffmpeg's own words. A generic "transcode failed" hides a missing encoder,
            # which is the one failure a user can actually act on.
            return False, 'ffmpeg failed: ' + (p.stderr.strip().splitlines() or ['(no output)'])[-1]
        if not os.path.exists(dest) or os.path.getsize(dest) == 0:
            return False, 'ffmpeg returned 0 but wrote nothing'
        return True, 'ripped %.1f MB -> %.1f MB' % (size / 1e6, os.path.getsize(dest) / 1e6)
    finally:
        if tmp and os.path.exists(tmp):
            os.remove(tmp)


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2
    disc, outdir = argv[1], argv[2]
    ffmpeg = 'ffmpeg'
    force = '--force' in argv
    only = None
    for i, a in enumerate(argv):
        if a == '--ffmpeg' and i + 1 < len(argv):
            ffmpeg = argv[i + 1]
        if a == '--only' and i + 1 < len(argv):
            only = argv[i + 1].upper()

    os.makedirs(outdir, exist_ok=True)
    found = movies(disc)
    if not found:
        print('no /MOVIES/*.MPC on %s -- is that the game disc?' % disc)
        return 1

    ok = fail = 0
    for name, extent, size in found:
        if only and name.upper() != only:
            continue
        good, msg = rip_one(disc, name, extent, size, outdir, ffmpeg, force)
        print('%-10s %s' % (name, msg))
        ok, fail = (ok + 1, fail) if good else (ok, fail + 1)
    print('-- %d ok, %d failed, into %s' % (ok, fail, outdir))
    return 1 if fail else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
