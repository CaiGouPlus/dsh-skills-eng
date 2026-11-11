import struct, math, sys

def load_stl(path):
    with open(path,'rb') as f:
        hdr=f.read(80)
        n=struct.unpack('<I',f.read(4))[0]
        tris=[]
        for i in range(n):
            data=f.read(50)
            if len(data)<50: break
            vals=struct.unpack('<12f',data[:48])
            tris.append((vals[0:3],vals[3:6],vals[6:9]))
        return tris

def slice_y(tris,y0,eps=1e-6):
    y0+=eps
    segs=[]
    for t in tris:
        p=[(t[0][0],t[0][1],t[0][2]),(t[1][0],t[1][1],t[1][2]),(t[2][0],t[2][1],t[2][2])]
        d=[q[1]-y0 for q in p]
        pts=[]
        for i in range(3):
            a=p[i]; b=p[(i+1)%3]; da=d[i]; db=d[(i+1)%3]
            if (da<=0 and db>0) or (da>0 and db<=0):
                f=da/(da-db)
                pts.append((a[0]+(b[0]-a[0])*f, a[2]+(b[2]-a[2])*f))
        if len(pts)==2:
            segs.append((pts[0],pts[1]))
    return segs

def key(p,nd=4):
    return (round(p[0],nd),round(p[1],nd))

def loops(segs,nd=4):
    from collections import defaultdict
    adj=defaultdict(list)
    for i,(a,b) in enumerate(segs):
        adj[key(a,nd)].append((b,i))
        adj[key(b,nd)].append((a,i))
    used=[False]*len(segs)
    out=[]
    for i,(a,b) in enumerate(segs):
        if used[i]: continue
        used[i]=True
        loop=[a,b]
        cur=b; prev=a
        while True:
            nxt=None
            for (pp,j) in adj[key(cur,nd)]:
                if not used[j]:
                    nxt=pp; used[j]=True; break
            if nxt is None: break
            loop.append(nxt)
            prev,cur=cur,nxt
            if key(cur,nd)==key(loop[0],nd): break
        out.append(loop)
    return out

def area(loop):
    s=0
    for i in range(len(loop)):
        x1,y1=loop[i]; x2,y2=loop[(i+1)%len(loop)]
        s+=x1*y2-x2*y1
    return abs(s)/2

tris=load_stl('part.stl')
print('tris',len(tris))
for y0 in [0.2,20,42.5,85,120,169.8]:
    segs=slice_y(tris,y0)
    ls=loops(segs)
    areas=sorted([area(l) for l in ls if len(l)>=3],reverse=True)
    tot=sum(areas)
    print(f'y={y0:6.1f}  segs={len(segs):4d} loops={len(ls):2d} total_area={tot:8.3f} mm2  areas={[round(a,2) for a in areas[:8]]}')
