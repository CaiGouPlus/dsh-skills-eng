import struct
import numpy as np

def load_stl(path):
    with open(path,'rb') as f:
        f.read(80)
        n=struct.unpack('<I',f.read(4))[0]
        data=np.frombuffer(f.read(n*50),dtype=np.uint8).reshape(n,50)
        v=np.frombuffer(data[:,:48].tobytes(),dtype='<f4').reshape(n,12)
        return v[:,0:9].reshape(n,3,3).astype(np.float64)

tris=load_stl('part.stl')
print('tris',len(tris))

xmin,xmax=-10.1,2.7
zmin,zmax=-0.1,43.1
N=400
xs=np.linspace(xmin,xmax,N)
zs=np.linspace(zmin,zmax,N)
X,Z=np.meshgrid(xs,zs)
Xf=X.ravel(); Zf=Z.ravel()
dx=(xmax-xmin)/N; dz=(zmax-zmin)/N
cell=dx*dz

def section_area(y0):
    counts=np.zeros(Xf.shape,dtype=np.int16)
    for t in tris:
        p0,p1,p2=t
        # project to (x,z)
        ax,az=p0[0],p0[2]; bx,bz=p1[0],p1[2]; cx,cz=p2[0],p2[2]
        den=(bz-cz)*(ax-cx)+(cx-bx)*(az-cz)
        if abs(den)<1e-12: continue
        l1=((bz-cz)*(Xf-cx)+(cx-bx)*(Zf-cz))/den
        l2=((cz-az)*(Xf-cx)+(ax-cx)*(Zf-cz))/den
        l3=1-l1-l2
        m=(l1>=-1e-9)&(l2>=-1e-9)&(l3>=-1e-9)
        if not m.any(): continue
        # y at (x,z)
        y=l1*p0[1]+l2*p1[1]+l3*p2[1]
        counts[m]+=(y[m]>y0).astype(np.int16)
    inside=(counts%2)==1
    return inside.sum()*cell

for y0 in [0.2,5,20,42.5,85,120,150,169.8]:
    print(f'y={y0:6.1f}  area={section_area(y0):9.3f} mm2')
