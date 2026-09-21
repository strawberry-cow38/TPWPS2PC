import sys, struct
SEC, OFF, USR = 2352, 24, 2048
def extract(binpath, extent, size, out, limit=None):
    n = size if limit is None else min(size, limit)
    with open(binpath,'rb') as f, open(out,'wb') as o:
        left, s = n, extent
        while left > 0:
            f.seek(s*SEC+OFF); o.write(f.read(min(USR,left)))
            left -= USR; s += 1
    return n
if __name__ == '__main__':
    b, ext, size, out = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), sys.argv[4]
    lim = int(sys.argv[5]) if len(sys.argv) > 5 else None
    print('wrote', extract(b, ext, size, out, lim), 'bytes to', out)
