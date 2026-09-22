"""Check the M3D2 per-triangle FACING flag (bit 0 of each vertex's Y word) against two things it
cannot have been fitted to: the stored vertex normals, and gravity.

    python3 tools/winding_check.py <dir-of-.mps> [terrain.mps]

For every model: how often the outward order `m3d2.triangles` derives from the flag agrees with
the stored per-vertex normals (a smoothed, lighting-only quantity -- it is the control, not the
authority). For the terrain: the GROUND control, every triangle of every ground mesh (LAND_*,
A_SEA_*, RIVERBED_*, surface*) must face +Y.

Recorded result, JUNGLE.WAD 2026-09-22: 60,084 / 60,537 triangles agree across 112 models
(99.25%); ground 2,618 / 2,619 face up. See findings/formats.md.
"""
import glob, os, struct, sys
sys.path.insert(0, os.path.dirname(__file__))
import m3d2


def cross(u, v): return (u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0])
def sub(u, v): return (u[0]-v[0], u[1]-v[1], u[2]-v[2])
def dot(u, v): return u[0]*v[0]+u[1]*v[1]+u[2]*v[2]


def mesh_census(m, mesh):
    """(agree, disagree, tie, up, down, flat): the outward normal vs the stored normals, and vs
    +Y. A tie is a zero dot product -- a degenerate triangle or zero stored normals -- and is
    reported rather than counted as either, ⚠ which an earlier `>= 0` silently did."""
    agree = disagree = tie = up = down = flat = 0
    for a, b, c, n in m3d2.batches(m, mesh):
        pos = [struct.unpack_from('<3f', m, a + k*12) for k in range(n)]
        nor = [tuple(v/127 for v in struct.unpack_from('<3b', m, c + k*3)) for k in range(n)]
        for i0, i1, i2 in m3d2._strip_triangles(m, a, n):
            rh = cross(sub(pos[i1], pos[i0]), sub(pos[i2], pos[i0]))
            ns = tuple(nor[i0][j] + nor[i1][j] + nor[i2][j] for j in range(3))
            d = dot(rh, ns)
            if d < 0: disagree += 1
            elif d > 0: agree += 1
            else: tie += 1
            if rh[1] < 0: down += 1
            elif rh[1] > 0: up += 1
            else: flat += 1
    return agree, disagree, tie, up, down, flat


GROUND = ('LAND_', 'A_SEA_', 'RIVERBED_0', 'surface', 'Surface', 'river')


def main():
    d = sys.argv[1]
    tot = [0, 0, 0]
    rows = []
    for f in sorted(glob.glob(os.path.join(d, '*.mps'))):
        m = open(f, 'rb').read()
        ag = dis = tie = 0
        for mesh in m3d2.meshes(m):
            a, b, t, _, _, _ = mesh_census(m, mesh); ag += a; dis += b; tie += t
        if ag + dis == 0: continue
        rows.append((ag / (ag + dis), os.path.basename(f), ag, dis, tie))
        tot[0] += ag; tot[1] += dis; tot[2] += tie
    rows.sort()
    print('agreement with stored normals, worst first (ties = zero dot, excluded from the %)')
    for r in rows[:10]: print(f'  {100*r[0]:6.2f}%  {r[1]:<22} agree {r[2]:6}  disagree {r[3]:4}  ties {r[4]}')
    print(f'ALL {len(rows)} models: agree {tot[0]} / {tot[0]+tot[1]} decided = {100*tot[0]/(tot[0]+tot[1]):.2f}%'
          f'  (+{tot[2]} ties, {tot[0]+tot[1]+tot[2]} triangles)')
    if len(sys.argv) > 2:
        m = open(sys.argv[2], 'rb').read()
        up = down = flat = 0
        for mesh in m3d2.meshes(m):
            if not mesh['name'].startswith(GROUND) or mesh['name'].endswith('B'): continue
            _, _, _, u, dn, fl = mesh_census(m, mesh); up += u; down += dn; flat += fl
        print(f'GROUND control ({os.path.basename(sys.argv[2])}): {up} face up, {down} face down, {flat} vertical/degenerate')


if __name__ == '__main__':
    main()
