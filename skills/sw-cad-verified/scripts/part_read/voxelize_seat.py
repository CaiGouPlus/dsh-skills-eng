import numpy as np, math
from collections import defaultdict

H = 0.25
X0, X1 = -31.75, 31.75
Y0, Y1 = -8.90, 18.50
Z0, Z1 = -31.75, 31.75
NX = int(round((X1-X0)/H)); NY = int(round((Y1-Y0)/H)); NZ = int(round((Z1-Z0)/H))
print('grid', NX, NY, NZ, 'voxels', NX*NY*NZ)

tris=[]; fnorm=[]; fids=[]
for line in open('seat.tess.txt',encoding='utf-8-sig'):
    p=line.strip().split('|')
    if len(p)!=5 or not p[0].isdigit(): continue
    fid=int(p[0])
    v=np.array([[float(x) for x in q.split(',')] for q in p[1:4]])
    n=np.array([float(x) for x in p[4].split(',')])/1000.0
    tris.append(v); fids.append(fid); fnorm.append(n)
T=np.array(tris); FID=np.array(fids); FN=np.array(fnorm)
print('tris', T.shape[0], 'faces', len(set(fids)))

events=defaultdict(list)
for i in range(T.shape[0]):
    p0,p1,p2=T[i]
    ax,az=p0[0],p0[2]; bx,bz=p1[0],p1[2]; cx,cz=p2[0],p2[2]
    den=(bz-cz)*(ax-cx)+(cx-bx)*(az-cz)
    if abs(den)<1e-12: continue
    ix0=max(0,int(math.floor((min(ax,bx,cx)-X0)/H))); ix1=min(NX-1,int(math.ceil((max(ax,bx,cx)-X0)/H)))
    iz0=max(0,int(math.floor((min(az,bz,cz)-Z0)/H))); iz1=min(NZ-1,int(math.ceil((max(az,bz,cz)-Z0)/H)))
    if ix1<ix0 or iz1<iz0: continue
    ixs=np.arange(ix0,ix1+1); izs=np.arange(iz0,iz1+1)
    IX,IZ=np.meshgrid(ixs,izs)
    px=X0+IX*H; pz=Z0+IZ*H
    l1=((bz-cz)*(px-cx)+(cx-bx)*(pz-cz))/den
    l2=((cz-az)*(px-cx)+(ax-cx)*(pz-cz))/den
    l3=1-l1-l2
    m=(l1>=-1e-9)&(l2>=-1e-9)&(l3>=-1e-9)
    if not m.any(): continue
    y=l1*p0[1]+l2*p1[1]+l3*p2[1]
    for ix,iz,yy in zip(IX[m].ravel(),IZ[m].ravel(),y[m].ravel()):
        if Y0-0.5<=yy<=Y1+0.5: events[(int(ix),int(iz))].append(float(yy))

occ=np.zeros((NY,NX,NZ),dtype=bool)
odd=0
for (ix,iz),ys in events.items():
    ys.sort()
    if len(ys)%2: odd+=1; ys=ys[:-1]
    for k in range(0,len(ys),2):
        a=int(math.ceil((ys[k]-Y0)/H)); b=int(math.floor((ys[k+1]-Y0)/H))
        if b<a: continue
        a=max(0,a); b=min(NY-1,b)
        occ[a:b+1,ix,iz]=True
print('columns',len(events),'odd',odd)
vol=occ.sum()*H**3
print('voxel volume %.1f mm3 (SW 12535.5), err %.2f%%'%(vol, abs(vol-12535.5)/12535.5*100))
np.save('seat_occ.npy', occ)

def inside(x,y,z):
    ix=int((x-X0)/H); iy=int((y-Y0)/H); iz=int((z-Z0)/H)
    if ix<0 or iy<0 or iz<0 or ix>=NX or iy>=NY or iz>=NZ: return False
    return bool(occ[iy,ix,iz])

byface=defaultdict(list)
for i in range(T.shape[0]): byface[FID[i]].append(i)

res={}
for fid, idxs in byface.items():
    pts=T[idxs]; ns=FN[idxs]
    c=pts.reshape(-1,3).mean(0); n=ns.mean(0); n=n/(np.linalg.norm(n)+1e-12)
    res[fid]=(inside(*(c+n*(0.7*H))), inside(*(c-n*(0.7*H))), c, n)
outward=sum(1 for v in res.values() if (not v[0]) and v[1])
inward=sum(1 for v in res.values() if v[0] and (not v[1]))
weird=sum(1 for v in res.values() if v[0]==v[1])
print('faces: normal-outside=%d normal-inside=%d ambiguous=%d'%(outward,inward,weird))

cyl=[]
for line in open('seat.faces.txt',encoding='utf-8-sig'):
    p=line.strip().split('|')
    if len(p)<5 or p[0]!='C': continue
    fid=int(p[1]); vals=[float(x) for x in p[3].split(',')]
    cyl.append((fid, np.array(vals[0:3]), np.array(vals[3:6]), vals[6]))
print('\ncylinders:',len(cyl))

def ray_hits(hc,a,step):
    t=0.0
    while t<300:
        q=hc+a*(step*t)
        if inside(*q): return True
        if not (X0-H<=q[0]<=X1+H and Y0-H<=q[1]<=Y1+H and Z0-H<=q[2]<=Z1+H): return False
        t+=1.0
    return False

rows=[]
for fid,p,a,r in cyl:
    if fid not in res: continue
    c=res[fid][2]; n=res[fid][3]
    a=a/(np.linalg.norm(a)+1e-12)
    d=c-p; d=d-(d@a)*a
    if np.linalg.norm(d)<1e-9: continue
    radial=d/np.linalg.norm(d)
    kind='bore' if (n@radial)<0 else 'boss'
    hc=c-radial*r
    if kind=='bore':
        h1=ray_hits(hc,a,+1.0); h2=ray_hits(hc,a,-1.0)
        tt='through' if (not h1 and not h2) else ('blind' if (h1!=h2) else 'both-blocked')
    else:
        tt='-'
    rows.append((fid,r,kind,tt))
from collections import Counter
print(Counter([x[2] for x in rows]))
for r_ in sorted(rows,key=lambda x:(x[1],x[0])):
    print('  face %2d R=%.2f %-4s %s'%(r_[0],r_[1],r_[2],r_[3]))


from PIL import Image
for yv in [-7.0,-1.5,0.5,3.0,8.0,12.0,16.5]:
    k=int(round((yv-Y0)/H))
    img=(occ[k,:,:].T*255).astype(np.uint8)
    im=Image.fromarray(img).resize((NX*4,NZ*4),Image.NEAREST)
    fn='seat_slice_y%+05.1f.png'%yv
    im.save(fn)
    print('saved',fn)
