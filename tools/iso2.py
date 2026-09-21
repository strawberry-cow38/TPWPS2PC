import sys, struct
# Mode 2/2352: 16-byte sync+header, 8-byte subheader, 2048 user, 4 EDC, 276 ECC (form 1).
SEC, OFF, USR = 2352, 24, 2048
def sector(f, n):
    f.seek(n * SEC + OFF); return f.read(USR)

def walk(f, extent, length, path, out, depth=0):
    data = b''
    for i in range((length + USR - 1) // USR):
        data += sector(f, extent + i)
    p = 0
    while p < len(data):
        rl = data[p]
        if rl == 0:
            p = (p // USR + 1) * USR
            if p >= len(data): break
            continue
        ext  = struct.unpack_from('<I', data, p + 2)[0]
        size = struct.unpack_from('<I', data, p + 10)[0]
        flags = data[p + 25]
        nlen = data[p + 32]
        name = data[p + 33:p + 33 + nlen]
        p += rl
        if nlen == 1 and name in (b'\x00', b'\x01'): continue
        nm = name.split(b';')[0].decode('latin-1')
        full = path + '/' + nm
        if flags & 2:
            out.append((full + '/', ext, size))
            if depth < 8: walk(f, ext, size, full, out, depth + 1)
        else:
            out.append((full, ext, size))

f = open(sys.argv[1], 'rb')
pvd = sector(f, 16)
print('PVD id     :', pvd[1:6].decode('latin-1'), 'type', pvd[0])
print('system     :', pvd[8:40].decode('latin-1').strip())
print('volume     :', pvd[40:72].decode('latin-1').strip())
print('sectors    :', struct.unpack_from('<I', pvd, 80)[0])
root = pvd[156:156+34]
rext = struct.unpack_from('<I', root, 2)[0]
rlen = struct.unpack_from('<I', root, 10)[0]
print('root extent:', rext, 'len', rlen)
out = []
walk(f, rext, rlen, '', out)
print('%d entries' % len(out))
for nm, ext, size in out:
    print('%10d  %8d  %s' % (size, ext, nm))
