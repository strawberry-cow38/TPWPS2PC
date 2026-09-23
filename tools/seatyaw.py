"""Every 0x80 fitting's world orientation on a ride, against the ride's body mesh.

Written to answer "are riders 180 degrees out in their seats" and it says NO CONSTANT EXISTS:
seats within one ride differ from each other (dodgems are parked at five angles, volcano's
sixteen are a ring), so the helper basis is authored per-seat facing and nothing may blanket-flip
it. See findings/visitors.md.

⚠ The `body` reference is a HEURISTIC -- last of m_body/body/m_base, else mesh 0 -- so per-ride
offsets are soft. The load-bearing output is that seats within one ride disagree.

  python3 tools/seatyaw.py /mnt/bigdisk/tpw-ps2/mps/monkey.mps
"""
import struct, sys, math, re
NAME = re.compile(rb'[^\0]*\0')
def dump(p, verbose=True):
    m = open(p,'rb').read()
    if struct.unpack_from('<I', m, 0)[0] != 0x183076E4: return None
    HELPER  = struct.unpack_from('<I', m, 0x4C)[0]
    MESHTBL = struct.unpack_from('<I', m, 0x48)[0]
    NMESH   = struct.unpack_from('<H', m, 0x30)[0]
    FITTBL  = struct.unpack_from('<I', m, 0x74)[0]
    NFIT    = struct.unpack_from('<H', m, 0x36)[0]
    helpers=[]; o=HELPER
    while o+0x60 <= len(m) and struct.unpack_from('<I', m, o)[0] & 0x80000000:
        helpers.append(o); o += 0x60
    def nm(o):
        q = struct.unpack_from('<I', m, o+0x54)[0]
        if not (0 < q < len(m)): return ""
        return NAME.match(m,q).group()[:-1].decode('latin1','replace')
    def loc(o):  return struct.unpack_from('<16f', m, o+0x10)
    def par(o):  return struct.unpack_from('<I', m, o+4)[0]
    nodes = {MESHTBL+i*160:('mesh',i) for i in range(NMESH)}
    nodes.update({h:('helper',i) for i,h in enumerate(helpers)})
    def mul(a,b):
        out=[0.0]*16
        for r in range(4):
            for c in range(4): out[r*4+c]=sum(a[r*4+k]*b[k*4+c] for k in range(4))
        return tuple(out)
    def world(o,d=0):
        pa=par(o)
        return loc(o) if (pa==0 or pa not in nodes or d>32) else mul(loc(o), world(pa,d+1))
    def zyaw(t):
        z=(t[8],t[9],t[10]); L=math.sqrt(sum(c*c for c in z)) or 1
        return math.degrees(math.atan2(z[0]/L, z[2]/L))
    def det(t):
        a=(t[0],t[1],t[2]); b=(t[4],t[5],t[6]); c=(t[8],t[9],t[10])
        return (a[0]*(b[1]*c[2]-b[2]*c[1]) - a[1]*(b[0]*c[2]-b[2]*c[0]) + a[2]*(b[0]*c[1]-b[1]*c[0]))
    seats=[]
    for i in range(NFIT):
        fo=FITTBL+i*20
        flags,fid = struct.unpack_from('<II', m, fo)
        if (flags & 0x80) and i < len(helpers): seats.append((i,helpers[i],fid,flags))
    if not seats: return None
    body = None
    for i in range(NMESH):
        o=MESHTBL+i*160
        if nm(o).lower() in ('m_body','body','m_base'): body = (nm(o), zyaw(world(o)))
    if body is None and NMESH:
        o=MESHTBL; body=(nm(o)+" (first mesh)", zyaw(world(o)))
    ys = [zyaw(world(h)) for _,h,_,_ in seats]
    ds = [det(world(h)) for _,h,_,_ in seats]
    rel = [((y - body[1] + 180) % 360) - 180 for y in ys]
    if verbose:
        print(f"{p.split('/')[-1]:16s} {len(seats):2d} seats  body '{body[0]}' yaw {body[1]:+7.1f}")
        for (i,h,fid,fl),y,r in zip(seats,ys,rel):
            print(f"   fit#{i:2d} id{fid:3d} '{nm(h)}' worldZ yaw {y:+7.1f}  = body {r:+7.1f}")
    return (p.split('/')[-1], len(seats), body[1], ys, rel, ds)
if __name__ == '__main__':
    for a in sys.argv[1:]: dump(a)
