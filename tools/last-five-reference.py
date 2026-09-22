"""Independent reference walk for the LastFiveAudit decoded pins; never rewrites goldens.

Usage: python3 tools/last-five-reference.py DATA.WAD GRC.ENG WTR.ENG
Inputs are extracted owner-disc assets, not committed fixtures. See findings/ride-engine.md
and findings/kanji-table.md for the consumer evidence behind the arithmetic.
"""
import hashlib
import struct
import sys
from pathlib import Path
import wadtree


def kanji_digest(data):
    words = struct.unpack('<%dH' % (len(data) // 2), data)
    # Enumerate the three consumer ranges directly; do not call the managed lookup.
    domains = list(range(0x8140, 0x84bf)) + list(range(0x8540, 0x8797)) + list(range(0x889f, 0x9873))
    lookup = dict(zip(domains, words[1:]))
    result = bytearray(struct.pack('<H', words[0]))
    for code in range(65536):
        ordinal = lookup.get(code, 65535)
        result += struct.pack('<Hh', code, -1 if ordinal == 65535 else ordinal)
    return hashlib.sha256(result).hexdigest()


def engine_digest(data):
    count, high = struct.unpack_from('<HH', data)
    p = 4
    layers = []
    for _ in range(count):
        layers.append(struct.unpack_from('<8iB', data, p))
        p += 33
    n, = struct.unpack_from('<I', data, p)
    p += 4
    curves = []
    for _ in range(n):
        curves.append((*struct.unpack_from('<3i', data, p), data[p+12:p+44]))
        p += 44
    nslots, = struct.unpack_from('<I', data, p)
    p += 4
    slots = []
    for _ in range(nslots):
        flag, = struct.unpack_from('<I', data, p)
        p += 4
        strings = []
        if flag:
            for _ in range(2):
                length, = struct.unpack_from('<I', data, p)
                p += 4
                strings.append(data[p:p+length])
                p += length
        slots.append((flag, strings))
    parameters, = struct.unpack_from('<I', data, p)
    assert parameters == 0 and p + 4 == len(data)
    result = bytearray(struct.pack('<Hi', high, count))
    for layer in layers:
        result += struct.pack('<8iB', *layer)
    result += struct.pack('<i', len(curves))
    for start, end, layer, samples in curves:
        result += struct.pack('<3i', start, end, layer) + samples
    result += struct.pack('<i', len(slots))
    for flag, strings in slots:
        result += struct.pack('<I', flag)
        for value in strings:
            result += struct.pack('<i', len(value)) + value
    result += struct.pack('<I', parameters)
    def trunc_div(a, b):
        return (abs(a) // abs(b)) * (-1 if (a < 0) != (b < 0) else 1)
    for x in range(-1, 1002):
        percentages = [100, 100]
        matches = 0
        for start, end, layer, samples in curves:
            if start <= x <= end:
                percentages[layers[layer][8]] = samples[min(31, (x-start)*32//(end-start))]
                matches += 1
                if matches == 2:
                    break
        result += struct.pack('<i2B', x, *percentages)
        active = [(i, r) for i, r in enumerate(layers) if r[0] <= x <= r[1] and r[6] and r[7]][:2]
        result += struct.pack('<i', len(active))
        for i, (start, end, vs, ve, ps, pe, sound, bank, channel) in active:
            volume = vs + trunc_div((ve-vs)*(x-start), end-start)
            parameter = ps + trunc_div((pe-ps)*(x-start), end-start)
            percent = percentages[channel]
            volume127 = min(127, volume*127//100)*percent//100
            result += struct.pack('<iBiiiBi', i, channel, sound, volume, parameter, percent, volume127)
    return hashlib.sha256(result).hexdigest()


if __name__ == '__main__':
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    wad = Path(sys.argv[1]).read_bytes()
    entries = wadtree.walk(wad)
    for locale in ['eur', 'jap', 'usa']:
        path = '/Text/translations/' + locale + '/kanji.table'
        _, off, stored, size = next(e for e in entries if e[0].lower() == path.lower())
        print(locale, kanji_digest(wadtree.read(wad, off, stored, size)))
    for name in sys.argv[2:]:
        print(Path(name).name, engine_digest(Path(name).read_bytes()))
