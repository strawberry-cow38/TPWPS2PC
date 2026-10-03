#!/usr/bin/env python3
"""Original Bouncy Cow art, stdlib only. No source art/geometry is loaded.

python3 tools/bouncy_cow_art.py --check
python3 tools/bouncy_cow_art.py --out /absolute/output --check --preview

Units are grid units, Y up, +Z faces the entrance; corner-origin 3 x 4 base.
GLB UVs use top-left image convention; OBJ vt flips V for conventional loaders.
The preview rasterizer is private to this generator, not the shared renderer.
No decoded animation, rig, physics or game-integration claims are made.
"""
import argparse
import json
import math
import struct
import zlib
from collections import Counter
from pathlib import Path

DEFAULT_OUT = Path.cwd() / 'bouncy-cow-art'
# Measured scalar brief from the owner-disc Belly Bounce census; no source geometry/pixels.
REFERENCE_BRIEF = {'name': 'Belly Bounce', 'footprint': [3, 4], 'visibleTriangles': 330, 'bodyTriangles': 174, 'bodyBounds': {'min': [0.5693095, 0.001763916, 0.6206372], 'max': [2.4829001, 1.8159593, 3.5361016], 'sizeXYZ': [1.9135907, 1.8141954, 2.9154644], 'centre': [1.5261048, 0.90886164, 2.0783694]}, 'bellyBase': '#FEE3A7', 'bellyTextureSize': [64, 64], 'height': 1.815959}
PI = math.pi


def add(a, b): return tuple(x+y for x, y in zip(a, b))
def sub(a, b): return tuple(x-y for x, y in zip(a, b))
def mul(a, s): return tuple(x*s for x in a)
def dot(a, b): return sum(x*y for x, y in zip(a, b))
def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def unit(a): return mul(a, 1/math.sqrt(dot(a, a)))
def bounds(points):
    lo = [min(p[k] for p in points) for k in range(3)]
    hi = [max(p[k] for p in points) for k in range(3)]
    return dict(min=lo, max=hi, sizeXYZ=[hi[k]-lo[k] for k in range(3)])


class Paint:
    def __init__(self, w, h, color):
        self.w, self.h = w, h
        self.p = [tuple(color)]*(w*h)

    def pixel(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h: self.p[y*self.w+x] = tuple(c)

    def ellipse(self, cx, cy, rx, ry, c):
        for y in range(max(0, int(cy-ry-1)), min(self.h, int(cy+ry+2))):
            for x in range(max(0, int(cx-rx-1)), min(self.w, int(cx+rx+2))):
                if ((x-cx)/rx)**2+((y-cy)/ry)**2 <= 1: self.pixel(x, y, c)

    def line(self, x0, y0, x1, y1, color, width=1):
        steps = max(1, int(max(abs(x1-x0), abs(y1-y0))*2))
        for i in range(steps+1):
            t = i/steps
            self.ellipse(x0+(x1-x0)*t, y0+(y1-y0)*t, width/2+.1, width/2+.1, color)

    def grain(self, strength=3):
        # Coarse, deterministic brush mottling, not photographic/noise assets.
        for y in range(self.h):
            for x in range(self.w):
                n = ((x//2*13+y//3*7+x//9*3) % 7-3)*strength/3
                self.pixel(x, y, [max(0, min(255, int(v+n))) for v in self.p[y*self.w+x]])

    def png(self):
        def chunk(tag, data):
            return struct.pack('>I', len(data))+tag+data+struct.pack('>I', zlib.crc32(tag+data)&0xffffffff)
        rows = b''.join(b'\0'+bytes(c for p in self.p[y*self.w:(y+1)*self.w] for c in p) for y in range(self.h))
        return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR', struct.pack('>IIBBBBB', self.w, self.h, 8, 2, 0, 0, 0))+chunk(b'IDAT', zlib.compress(rows, 9))+chunk(b'IEND', b'')


FONT = {
 'B':['110','101','110','101','110'], 'O':['010','101','101','101','010'],
 'U':['101','101','101','101','111'], 'N':['101','111','111','111','101'],
 'C':['111','100','100','100','111'], 'Y':['101','101','010','010','010'],
 'W':['101','101','111','111','101'], ' ':['000']*5,
}


def textures():
    t = {}
    t['hide'] = Paint(64,64,(249,240,216))
    # Original irregular silhouette islands on a seam-safe spherical hide map.
    for cx,cy,rx,ry in [(13,21,10,9),(40,38,12,10),(53,17,6,8),(22,50,7,5)]:
        for y in range(64):
            for x in range(64):
                angle = math.atan2((y-cy)/ry, (x-cx)/rx)
                r = ((x-cx)/rx)**2+((y-cy)/ry)**2
                if r < (1+.13*math.sin(angle*5)+.08*math.cos(angle*3))**2:
                    t['hide'].pixel(x,y,(42,39,43))
    t['hide'].grain(3)
    t['belly'] = Paint(64,64,(254,227,167))
    # Raised cream bounce pad: hand-painted soft rim and dashed inflatable seam.
    for y in range(64):
        for x in range(64):
            r = math.hypot((x-31.5)/31.5,(y-31.5)/31.5)
            if r > .86: t['belly'].pixel(x,y,(232,198,137))
            elif r > .80: t['belly'].pixel(x,y,(255,237,188))
            if .90 < r < .94 and int((math.atan2(y-31.5,x-31.5)+PI)*24)%2 == 0:
                t['belly'].pixel(x,y,(180,139,89))
    t['belly'].grain(2)
    t['cream'] = Paint(32,32,(248,234,202)); t['cream'].grain(3)
    t['muzzle'] = Paint(32,32,(245,157,167))
    t['muzzle'].ellipse(15,11,14,8,(255,177,183))
    for x in (9,23):
        t['muzzle'].ellipse(x,11,3,2.5,(129,67,77))
        t['muzzle'].ellipse(x-.6,10.3,1.2,.7,(98,53,63))
        t['muzzle'].ellipse(x-2,7,2,.7,(255,211,203))
    for x in range(7,26):
        y = 20+4*(1-((x-16)/10)**2)
        t['muzzle'].ellipse(x,y,.9,.9,(112,61,67))
    t['muzzle'].ellipse(6,20,1.5,1,(210,102,123))
    t['muzzle'].ellipse(26,20,1.5,1,(210,102,123)); t['muzzle'].grain(2)
    t['eye'] = Paint(32,32,(248,234,202))
    t['eye'].ellipse(16,18,10,11,(255,249,232))
    t['eye'].ellipse(16,18,6,8,(51,39,44))
    t['eye'].ellipse(13,14,2,2,(255,255,241))
    t['eye'].line(8,5,23,3,(72,48,47),2)
    t['ear'] = Paint(32,32,(247,233,203))
    t['ear'].ellipse(16,16,12,9,(229,145,158))
    t['ear'].ellipse(16,15,9,5,(249,176,179)); t['ear'].grain(2)
    t['horn'] = Paint(32,32,(255,237,184))
    for y in (21,25,29): t['horn'].line(0,y,31,y,(214,187,130))
    t['horn'].grain(2)
    t['hoof'] = Paint(32,32,(64,55,64))
    t['hoof'].ellipse(11,10,8,3,(91,80,87))
    t['hoof'].line(16,17,16,31,(32,29,36),2); t['hoof'].grain(3)
    t['tail'] = Paint(32,32,(248,234,202))
    for y in range(11):
        for x in range(32): t['tail'].pixel(x,y,(52,43,49))
    t['tail'].grain(2)
    t['grass'] = Paint(32,32,(112,149,102)); t['grass'].grain(5)
    for x,y in [(4,9),(19,23),(27,7),(9,28)]:
        t['grass'].line(x,y,x-2,y-3,(143,175,116)); t['grass'].line(x,y,x+2,y-4,(88,129,85))
    t['wood'] = Paint(32,32,(190,135,82)); t['wood'].grain(4)
    for x in (4,13,25): t['wood'].line(x,0,x+2,31,(162,106,68))
    t['sign'] = Paint(128,64,(67,99,91))
    for x in range(128):
        for y in range(64):
            if x<4 or x>123 or y<4 or y>59: t['sign'].pixel(x,y,(244,218,164))
    def label(text,y,scale):
        x = (128-(len(text)*4-1)*scale)//2
        for ch in text:
            for r,row in enumerate(FONT[ch]):
                for col,pix in enumerate(row):
                    if pix=='1':
                        for yy in range(scale):
                            for xx in range(scale): t['sign'].pixel(x+col*scale+xx,y+r*scale+yy,(255,235,182))
            x += 4*scale
    label('BOUNCY',11,3); label('COW',34,4)
    return t


class Part:
    def __init__(self, name, material, cow=False):
        self.name, self.material, self.cow = name, material, cow
        self.positions, self.normals, self.uvs, self.indices = [], [], [], []

    def tri(self, pts, uvs, normals=None, outward=None):
        normal = cross(sub(pts[1],pts[0]), sub(pts[2],pts[0]))
        if normals is None:
            if outward is not None and dot(normal,outward)<0:
                pts=[pts[0],pts[2],pts[1]]; uvs=[uvs[0],uvs[2],uvs[1]]
                normal=mul(normal,-1)
            normals=[unit(normal)]*3
        elif dot(normal,add(add(normals[0],normals[1]),normals[2]))<0:
            pts=[pts[0],pts[2],pts[1]]; uvs=[uvs[0],uvs[2],uvs[1]]; normals=[normals[0],normals[2],normals[1]]
        self.indices.extend(range(len(self.positions),len(self.positions)+3))
        self.positions.extend(pts); self.normals.extend(normals); self.uvs.extend(uvs)


def ellipsoid(name, material, center, radius, segments, rings, axis='y', angle=0, cow=True, projected=False):
    p = Part(name,material,cow)
    def rotate(v):
        x,y,z=v
        if axis=='z': y,z=z,y
        return (x*math.cos(angle)-y*math.sin(angle),x*math.sin(angle)+y*math.cos(angle),z)
    def sample(theta, phi, u):
        q=(math.sin(theta)*math.sin(phi),math.cos(theta),math.sin(theta)*math.cos(phi))
        local=rotate(q)
        pos=add(center,tuple(local[i]*radius[i] for i in range(3)))
        n=unit(tuple(local[i]/radius[i] for i in range(3)))
        uv=(.5+.5*local[0], .5-.5*local[1]) if projected else (u,theta/PI)
        return pos,n,uv
    def emit(a,b,c): p.tri([v[0] for v in (a,b,c)],[v[2] for v in (a,b,c)],[v[1] for v in (a,b,c)])
    for s in range(segments):
        u0,u1=s/segments,(s+1)/segments
        phi0,phi1=2*PI*u0,2*PI*u1
        top=sample(0,(phi0+phi1)/2,(u0+u1)/2)
        bottom=sample(PI,(phi0+phi1)/2,(u0+u1)/2)
        rows=[(sample(PI*j/(rings+1),phi0,u0),sample(PI*j/(rings+1),phi1,u1)) for j in range(1,rings+1)]
        emit(top,*rows[0])
        for j in range(rings-1):
            a,b=rows[j]; c,d=rows[j+1]; emit(a,c,d); emit(a,d,b)
        emit(bottom,rows[-1][1],rows[-1][0])
    return p


def box(name, material, lo, hi, cow=False):
    p=Part(name,material,cow); center=mul(add(lo,hi),.5)
    v=[(hi[0] if i&1 else lo[0],hi[1] if i&2 else lo[1],hi[2] if i&4 else lo[2]) for i in range(8)]
    for a,b,c,d in [(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)]:
        outward=sub(mul(add(v[a],v[c]),.5),center)
        for ids,uv in [((a,b,c),[(0,1),(1,1),(1,0)]),((a,c,d),[(0,1),(1,0),(0,0)])]:
            p.tri([v[i] for i in ids],uv,outward=outward)
    return p


def cone(name, material, base, tip, radius):
    p=Part(name,material,True)
    axis=unit(sub(tip,base)); u=unit(cross(axis,(0,0,1))); v=cross(axis,u)
    ring=[add(base,add(mul(u,radius*math.cos(i*PI/2)),mul(v,radius*math.sin(i*PI/2)))) for i in range(4)]
    for i in range(4):
        a,b=ring[i],ring[(i+1)%4]
        p.tri([a,b,tip],[(i/4,1),((i+1)/4,1),((i+.5)/4,0)],outward=sub(mul(add(a,b),.5),base))
    for i in (1,2): p.tri([ring[0],ring[i],ring[i+1]],[(.5,.5),(0,1),(1,1)],outward=mul(axis,-1))
    return p


def build():
    parts=[]
    parts.append(ellipsoid('inflatable_hide','hide',(1.5,.62,1.82),(.79,.56,1.14),8,3))
    pad=Part('cream_oval_bounce_pad','belly',True)
    def point(r,i):
        a=2*PI*i/10; x,z=.65*r*math.cos(a),.89*r*math.sin(a)
        return ((1.5+x,1.24-.24*r*r,1.82+z),unit((.48*x/(.65**2),1,.48*z/(.89**2))),(.5+.5*r*math.cos(a),.5+.5*r*math.sin(a)))
    def emit(a,b,c): pad.tri([v[0] for v in (a,b,c)],[v[2] for v in (a,b,c)],[v[1] for v in (a,b,c)])
    for i in range(10):
        c=point(0,0); a,b=point(.53,i),point(.53,i+1); d,e=point(1,i),point(1,i+1)
        emit(c,a,b); emit(a,d,e); emit(a,e,b)
    parts.append(pad)
    parts.append(ellipsoid('happy_head','cream',(1.5,1.22,2.95),(.42,.36,.39),6,2))
    parts.append(ellipsoid('pink_muzzle_and_smile','muzzle',(1.5,1.14,3.28),(.36,.23,.22),6,1,axis='z',projected=True))
    for side,label in [(-1,'left'),(1,'right')]:
        parts.append(ellipsoid(label+'_ear','ear',(1.5+side*.48,1.40,2.96),(.30,.16,.095),4,1,axis='z',angle=side*.32,projected=True))
        parts.append(cone(label+'_cream_horn','horn',(1.5+side*.27,1.48,2.83),(1.5+side*.33,1.815959,2.79),.10))
        # Smiling face panels sit outside head surface and face forward/upward.
        eye=Part(label+'_eye','eye',True)
        cx=1.5+side*.20
        q=[(cx-.105,1.31,3.267),(cx+.105,1.31,3.267),(cx+.105,1.51,3.147),(cx-.105,1.51,3.147)]
        eye.tri(q[:3],[(0,1),(1,1),(1,0)],outward=(0,.6,1))
        eye.tri([q[0],q[2],q[3]],[(0,1),(1,0),(0,0)],outward=(0,.6,1)); parts.append(eye)
        for z,label2 in [(.94,'hind'),(2.59,'fore')]:
            parts.append(ellipsoid(label+'_'+label2+'_leg','hide',(1.5+side*.62,.43,z),(.29,.22,.31),4,1))
            parts.append(ellipsoid(label+'_'+label2+'_chunky_hoof','hoof',(1.5+side*.78,.29,z+.04),(.18,.22,.23),4,1,axis='z',projected=True))
    parts.append(cone('tail_with_painted_tuft','tail',(1.5,.43,.86),(1.40,.38,.60),.085))
    floor=Part('corner_origin_3x4_platform','grass')
    for x in range(3):
        for z in range(4):
            q=[(x,0,z),(x+1,0,z),(x+1,0,z+1),(x,0,z+1)]
            floor.tri(q[:3],[(0,0),(1,0),(1,1)],outward=(0,1,0))
            floor.tri([q[0],q[2],q[3]],[(0,0),(1,1),(0,1)],outward=(0,1,0))
    parts.append(floor)
    for x,label in [(.15,'left'),(2.85,'right')]:
        for z,label2 in [(.62,'rear'),(2.58,'front')]:
            parts.append(box(label+'_'+label2+'_fence_post','wood',(x-.055,0,z-.055),(x+.055,.55,z+.055)))
        parts.append(box(label+'_fence_rail','wood',(x-.04,.35,.62),(x+.04,.45,2.58)))
    parts.append(box('sign_stem','wood',(.35,0,3.66),(.45,.70,3.76)))
    # Explicit front UV mapping: not a randomly oriented box face.
    sign=box('original_bouncy_cow_sign','wood',(.06,.62,3.70),(.74,1.00,3.76))
    sign.material='sign'
    # +Z face corresponds to second box face (triangles 2,3).
    for j,uv in zip(range(6,12),[(0,1),(0,0),(1,0),(0,1),(1,0),(1,1)]): sign.uvs[j]=uv
    # Other faces sample only border, avoiding repeated text on sides/back.
    for j in list(range(6))+list(range(12,36)): sign.uvs[j]=(.015,.015)
    parts.append(sign)
    return parts


def write_obj(out, parts, tex):
    mtl=[]
    for name in tex:
        mtl += ['newmtl '+name,'Ka 0.25 0.25 0.25','Kd 1 1 1','Ks 0 0 0','Ns 0','illum 1','map_Kd '+name+'.png','']
    (out/'bouncy-cow.mtl').write_text('\n'.join(mtl))
    lines=['# Original Y-up +Z Bouncy Cow. Corner-origin grid units.','mtllib bouncy-cow.mtl']; offset=1
    for p in parts:
        lines += ['o '+p.name,'usemtl '+p.material]
        lines += ['v '+' '.join(f'{x:.8f}' for x in v) for v in p.positions]
        lines += [f'vt {u:.8f} {1-v:.8f}' for u,v in p.uvs]
        lines += ['vn '+' '.join(f'{x:.8f}' for x in v) for v in p.normals]
        for i in range(0,len(p.indices),3):
            lines.append('f '+' '.join(f'{j+offset}/{j+offset}/{j+offset}' for j in p.indices[i:i+3]))
        offset+=len(p.positions)
    (out/'bouncy-cow.obj').write_text('\n'.join(lines)+'\n')


def write_glb(out, parts, tex):
    blob=bytearray(); views=[]; accessors=[]
    def view(data,target=None):
        while len(blob)%4: blob.append(0)
        v=dict(buffer=0,byteOffset=len(blob),byteLength=len(data))
        if target: v['target']=target
        views.append(v); blob.extend(data); return len(views)-1
    def accessor(values,typ,component=5126,position=False):
        flat=[x for v in values for x in v] if typ!='SCALAR' else values
        data=struct.pack('<'+('f' if component==5126 else 'H')*len(flat),*flat)
        a=dict(bufferView=view(data,34963 if typ=='SCALAR' else 34962),componentType=component,count=len(values),type=typ)
        if position:
            b=bounds(values); a['min']=b['min']; a['max']=b['max']
        accessors.append(a); return len(accessors)-1
    names=list(tex); images=[]
    for name in names: images.append(dict(name=name,bufferView=view(tex[name].png()),mimeType='image/png'))
    materials=[dict(name=n,pbrMetallicRoughness=dict(baseColorTexture=dict(index=i),metallicFactor=0,roughnessFactor=1),doubleSided=False) for i,n in enumerate(names)]
    meshes=[]
    for p in parts:
        attributes={'POSITION':accessor(p.positions,'VEC3',position=True),'NORMAL':accessor(p.normals,'VEC3'),'TEXCOORD_0':accessor(p.uvs,'VEC2')}
        meshes.append(dict(name=p.name,primitives=[dict(attributes=attributes,indices=accessor(p.indices,'SCALAR',5123),material=names.index(p.material),mode=4)]))
    doc=dict(asset=dict(version='2.0',generator='Original bouncy_cow_art.py / stdlib'),scene=0,scenes=[dict(nodes=list(range(len(parts))))],nodes=[dict(name=p.name,mesh=i) for i,p in enumerate(parts)],meshes=meshes,materials=materials,textures=[dict(sampler=0,source=i) for i in range(len(names))],samplers=[dict(magFilter=9728,minFilter=9728,wrapS=33071,wrapT=33071)],images=images,bufferViews=views,accessors=accessors,buffers=[dict(byteLength=len(blob))],extras=dict(units='grid units, not metres',frame='Y up, +Z entrance, corner origin',originalArt=True,animation='none: static original design'))
    js=json.dumps(doc,separators=(',',':')).encode(); js+=b' '*((-len(js))%4)
    binary=bytes(blob)+b'\0'*((-len(blob))%4)
    glb=struct.pack('<4sII',b'glTF',2,12+8+len(js)+8+len(binary))+struct.pack('<I4s',len(js),b'JSON')+js+struct.pack('<I4s',len(binary),b'BIN\0')+binary
    (out/'bouncy-cow.glb').write_bytes(glb)


def read_png(data):
    assert data[:8]==b'\x89PNG\r\n\x1a\n'
    at=8; packed=b''; dimensions=None
    while at<len(data):
        n=struct.unpack_from('>I',data,at)[0]; tag=data[at+4:at+8]; payload=data[at+8:at+8+n]
        assert zlib.crc32(tag+payload)&0xffffffff==struct.unpack_from('>I',data,at+8+n)[0]
        if tag==b'IHDR':
            w,h,depth,color,comp,filt,interlace=struct.unpack('>IIBBBBB',payload)
            assert (depth,color,comp,filt,interlace)==(8,2,0,0,0); dimensions=(w,h)
        if tag==b'IDAT': packed+=payload
        at+=12+n
    assert tag==b'IEND' and at==len(data)
    w,h=dimensions; raw=zlib.decompress(packed); assert len(raw)==h*(1+3*w)
    assert all(raw[y*(1+3*w)]==0 for y in range(h))
    pixels=b''.join(raw[y*(1+3*w)+1:(y+1)*(1+3*w)] for y in range(h))
    return dimensions,pixels


def check(out,parts,tex):
    checks=[]; triangles=sum(len(p.indices)//3 for p in parts)
    cow=sum(len(p.indices)//3 for p in parts if p.cow)
    assert 174<=cow<=220 and 310<=triangles<=350,(cow,triangles)
    checks.append(f'budget: cow {cow}, total {triangles}; honest tolerance 174–220 / 310–350')
    for p in parts:
        assert len(p.positions)==len(p.normals)==len(p.uvs)
        assert len(p.indices)%3==0 and all(0<=i<len(p.positions) for i in p.indices)
        assert all(math.isfinite(x) for rows in (p.positions,p.normals,p.uvs) for v in rows for x in v)
        assert all(abs(dot(n,n)-1)<1e-5 for n in p.normals)
        assert all(0<=x<=1 for uv in p.uvs for x in uv)
        for j in range(0,len(p.indices),3):
            ids=p.indices[j:j+3]; a,b,c=[p.positions[i] for i in ids]
            n=cross(sub(b,a),sub(c,a)); assert dot(n,n)>1e-12,(p.name,j)
            assert dot(n,tuple(sum(p.normals[i][k] for i in ids) for k in range(3)))>1e-8,(p.name,j,'winding')
    checks.append('indices, finite geometry/UVs, unit normals, nondegenerate faces, outward winding vs normals')
    b=bounds([v for p in parts for v in p.positions]); floor=next(p for p in parts if p.name.startswith('corner_origin'))
    assert bounds(floor.positions)['min']==[0,0,0] and bounds(floor.positions)['max']==[3,0,4]
    assert b['min']==[0,0,0] and b['max']==[3,1.815959,4]
    checks.append('exact corner-origin 3x4 base; assembly contained, Y-up; rest height 1.815959')
    for name,t in tex.items():
        dims,pixels=read_png((out/(name+'.png')).read_bytes())
        assert dims==(t.w,t.h) and pixels==bytes(x for p in t.p for x in p)
        assert dims==((128,64) if name=='sign' else (64,64) if name in ('hide','belly') else (32,32))
    checks.append('all PNG CRCs/decompression/pixels and 32/64 texture sizes; sign 128x64')
    data=(out/'bouncy-cow.glb').read_bytes(); magic,version,length=struct.unpack_from('<4sII',data)
    assert magic==b'glTF' and version==2 and length==len(data)
    n,tag=struct.unpack_from('<I4s',data,12); assert tag==b'JSON'
    doc=json.loads(data[20:20+n]); size,tag=struct.unpack_from('<I4s',data,20+n); assert tag==b'BIN\0'
    binary=data[28+n:]; assert len(binary)==size
    assert len(binary)-doc['buffers'][0]['byteLength'] in range(4)
    for view in doc['bufferViews']:
        assert view['byteOffset']%4==0 and view['byteOffset']+view['byteLength']<=len(binary)
    def unpack(i):
        a=doc['accessors'][i]; v=doc['bufferViews'][a['bufferView']]; width={'SCALAR':1,'VEC2':2,'VEC3':3}[a['type']]
        fmt='f' if a['componentType']==5126 else 'H'; n=a['count']*width
        assert struct.calcsize('<'+fmt*n)==v['byteLength']
        vals=struct.unpack_from('<'+fmt*n,binary,v['byteOffset'])
        return list(vals) if width==1 else list(zip(*(iter(vals),)*width))
    exported=0
    for p,mesh in zip(parts,doc['meshes']):
        prim=mesh['primitives'][0]; ids=unpack(prim['indices']); assert ids==p.indices
        for key,expected in [('POSITION',p.positions),('NORMAL',p.normals),('TEXCOORD_0',p.uvs)]:
            got=unpack(prim['attributes'][key]); assert len(got)==len(expected)
            assert all(abs(a-b)<1e-6 for av,bv in zip(got,expected) for a,b in zip(av,bv))
        assert doc['materials'][prim['material']]['name']==p.material
        exported+=len(ids)//3
    assert exported==triangles
    for name,img in zip(tex,doc['images']):
        v=doc['bufferViews'][img['bufferView']]; embedded=binary[v['byteOffset']:v['byteOffset']+v['byteLength']]
        assert embedded==(out/(name+'.png')).read_bytes(); read_png(embedded)
    checks.append('GLB header/chunks/alignment, decoded attributes/indices/materials, embedded PNG round-trip')
    # OBJ independently parse indices, UV flip, normals and positions.
    verts=[]; uvs=[]; norms=[]; faces=[]
    for line in (out/'bouncy-cow.obj').read_text().splitlines():
        words=line.split()
        if not words: continue
        if words[0]=='v': verts.append(tuple(map(float,words[1:])))
        if words[0]=='vt': uvs.append(tuple(map(float,words[1:])))
        if words[0]=='vn': norms.append(tuple(map(float,words[1:])))
        if words[0]=='f': faces.append([tuple(map(int,w.split('/'))) for w in words[1:]])
    assert len(faces)==triangles
    assert all(len(f)==3 and all(1<=v<=len(verts) and 1<=t<=len(uvs) and 1<=n<=len(norms) for v,t,n in f) for f in faces)
    for got,expected in zip(verts,[v for p in parts for v in p.positions]): assert all(abs(a-b)<1e-7 for a,b in zip(got,expected))
    for got,expected in zip(uvs,[v for p in parts for v in p.uvs]): assert abs(got[0]-expected[0])<1e-7 and abs(got[1]-(1-expected[1]))<1e-7
    assert len(norms)==len(verts)==sum(len(p.positions) for p in parts)
    checks.append('OBJ triangle/indices/positions and flipped-V UV round-trip; matching MTL maps')
    for name in tex: assert 'map_Kd '+name+'.png' in (out/'bouncy-cow.mtl').read_text()
    return dict(passed=True,checks=checks)


def preview(out,parts,tex,name,eye):
    """Tiny orthographic opaque rasterizer. Preview only, no mesh transforms."""
    w,h=840,760; image=Paint(w,h,(239,229,209)); depth=[-1e9]*(w*h)
    view=unit(eye); right=unit(cross((0,1,0),view)); up=cross(view,right)
    target=(1.5,.65,2); scale=145
    light=unit((-.5,1,.8))
    def project(v):
        q=sub(v,target); return (w/2+dot(q,right)*scale,h/2-dot(q,up)*scale,dot(q,view))
    for p in parts:
        texture=tex[p.material]
        for at in range(0,len(p.indices),3):
            ids=p.indices[at:at+3]; pos=[p.positions[i] for i in ids]; q=[project(v) for v in pos]
            fn=cross(sub(pos[1],pos[0]),sub(pos[2],pos[0]))
            if dot(fn,view)<=0: continue
            x0,y0,_=q[0]; x1,y1,_=q[1]; x2,y2,_=q[2]
            den=(y1-y2)*(x0-x2)+(x2-x1)*(y0-y2)
            if abs(den)<1e-9: continue
            for y in range(max(0,int(min(v[1] for v in q))),min(h,int(max(v[1] for v in q))+2)):
                for x in range(max(0,int(min(v[0] for v in q))),min(w,int(max(v[0] for v in q))+2)):
                    a=((y1-y2)*(x+.5-x2)+(x2-x1)*(y+.5-y2))/den
                    b=((y2-y0)*(x+.5-x2)+(x0-x2)*(y+.5-y2))/den; c=1-a-b
                    if min(a,b,c)<-1e-7: continue
                    weights=(a,b,c); z=sum(t*v[2] for t,v in zip(weights,q)); idx=y*w+x
                    if z<=depth[idx]: continue
                    depth[idx]=z
                    u,v=(sum(t*p.uvs[i][k] for t,i in zip(weights,ids)) for k in range(2))
                    normal=unit(tuple(sum(t*p.normals[i][k] for t,i in zip(weights,ids)) for k in range(3)))
                    shade=.72+.28*max(0,dot(normal,light))
                    color=texture.p[min(texture.h-1,max(0,int(v*texture.h)))*texture.w+min(texture.w-1,max(0,int(u*texture.w)))]
                    image.p[idx]=tuple(int(value*shade) for value in color)
    (out/name).write_bytes(image.png())


def scalar_reference(path):
    # Only retain aggregate/scalar metadata. No vertex/morph/texture pixel data.
    d=json.loads(path.read_text())
    if isinstance(d.get('footprint'), list): return d
    meshes=d['meshMetrics']; body=next(m for m in meshes if m['Name']=='jb_bd')
    visible=[m for m in meshes if m.get('visibleAtIdle0')]
    belly=next((t for t in d['textures'] if t.get('material')=='Bd_tum.ssh' and t.get('selectedByAssetLibrary')),None)
    return dict(name=d['identity']['Name'],footprint=[d['footprint']['width'],d['footprint']['depth']],visibleTriangles=sum(m['decodedTriangles'] for m in visible),bodyTriangles=body['decodedTriangles'],bodyBounds=body['transformedIdle0Bounds'],bellyBase='#FEE3A7',bellyTextureSize=[belly['Width'],belly['Height']] if belly else [64,64],height=1.815959)


def main():
    parser=argparse.ArgumentParser(description=__doc__,formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--out',type=Path,default=DEFAULT_OUT)
    parser.add_argument('--reference-metrics',type=Path,help='optional independently measured scalar report; embedded measured brief is the default')
    parser.add_argument('--check',action='store_true',help='verify generated mesh, PNG, GLB and OBJ; fail on errors')
    parser.add_argument('--preview',action='store_true',help='also render two original static PNG previews (not texture assets)')
    args=parser.parse_args(); out=args.out.resolve(); out.mkdir(parents=True,exist_ok=True)
    reference=scalar_reference(args.reference_metrics) if args.reference_metrics else REFERENCE_BRIEF
    tex=textures(); parts=build()
    for name,t in tex.items(): (out/(name+'.png')).write_bytes(t.png())
    write_glb(out,parts,tex); write_obj(out,parts,tex)
    validation=check(out,parts,tex) if args.check else dict(passed=None,note='not requested; run --check')
    if args.preview:
        preview(out,parts,tex,'preview-front.png',(4,3.5,6))
        preview(out,parts,tex,'preview-top.png',(0,8,.001))
    cowparts=[p for p in parts if p.cow]
    total=sum(len(p.indices)//3 for p in parts); cow=sum(len(p.indices)//3 for p in cowparts)
    metrics=dict(schemaVersion=1,asset='Original Bouncy Cow',frame=dict(origin=[0,0,0],up='+Y',front='+Z',viewer='reflect Z externally; reverse winding if baking reflection',units='grid units, not metres'),referenceScalars=reference,measuredVsDesign=dict(measured='Reference JSON scalar metadata only; visible reference is 330, not all 582 triangles including hidden construction meshes.',design='Entire geometry, texture pixels, UVs and sign authored procedurally here. Purple dinosaur replaced by original ivory/black cow. Same 3x4 footprint and height target; not a traced silhouette.',belly='Original oval dome bounce surface, base #FEE3A7 with painted rim/stitches. Not recovered geometry.',animation='Static rest pose; no animation, preview deformation, morphs or decoded tracks.'),totals=dict(triangles=total,cowTriangles=cow,referenceTotalDelta=total-reference['visibleTriangles'],referenceBodyDelta=cow-reference['bodyTriangles'],parts=len(parts),exportVertexSlots=sum(len(p.positions) for p in parts),assemblyBounds=bounds([v for p in parts for v in p.positions]),cowBounds=bounds([v for p in cowparts for v in p.positions])),budget=dict(cowTarget=[174,220],assemblyTarget=330,checkAssemblyTolerance=[310,350]),textures={name:dict(file=name+'.png',width=t.w,height=t.h,format='RGB8 PNG',original=True,filter='nearest',colorSpace='sRGB') for name,t in tex.items()},parts=[dict(name=p.name,role='cow' if p.cow else 'platform/prop',triangles=len(p.indices)//3,vertexSlots=len(p.positions),bounds=bounds(p.positions),materials=[p.material],textureSizes=[[tex[p.material].w,tex[p.material].h]],uvMapping='explicit per-corner normalized UV; projected feature maps or seam-split spherical hide') for p in parts],materialTriangleCounts=dict(Counter({name:sum(len(p.indices)//3 for p in parts if p.material==name) for name in tex})),validation=validation,limitations=['No collision/ride seats/path markers/rig/game integration.','Low-poly intersections are intentional inflatable assembly joints; not a watertight collision mesh.','Platform is a single-sided zero-thickness tiled plane.','Eye panels are explicit opaque cream-backed quads, not decals with transparency.','Rest previews use simple smooth diffuse lighting; no PBR detail, no reference renders/assets.','GLB is authored +Z. A baked negative-Z transform requires winding/normal handling by importer.'])
    (out/'asset-metrics.json').write_text(json.dumps(metrics,indent=2)+'\n')
    (out/'README-original-art.txt').write_text('BOUNCY COW — original low-poly first art prototype\n\nFiles: bouncy-cow.glb (embedded PNGs), bouncy-cow.obj + bouncy-cow.mtl\nExternal texture PNGs: only 32x32/64x64, sign 128x64.\nPreview PNGs, if requested, are illustrations and are not used by materials.\n\n'+f'Actual budget: cow {cow} triangles; assembly {total} (reference visible {reference["visibleTriangles"]}).\n'+ 'Frame: corner (0,0,0), platform X 0..3 / Z 0..4, Y up, head/entrance +Z.\nUse native frame or reflect Z in viewer; no baked reflection.\nOBJ UV V is flipped from GLB to match common image-loader conventions.\nOriginal seedless deterministic procedural paint, geometry and sign; no game art copied.\nNo animation or decoded deformation. No rig/collision/ride behavior.\nSee asset-metrics.json for per-part bounds, design/measurement distinctions and checks.\n')
    print(json.dumps(dict(output=str(out),cowTriangles=cow,totalTriangles=total,bounds=metrics['totals']['assemblyBounds'],validation=validation),indent=2))


if __name__=='__main__': main()
