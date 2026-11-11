import struct, sys

def load_stl(path):
    with open(path,'rb') as f:
        f.read(80)
        n=struct.unpack('<I',f.read(4))[0]
        tris=[]
        for i in range(n):
            data=f.read(50)
            if len(data)<50: break
            v=struct.unpack('<12f',data[:48])
            tris.append(((v[0],v[1],v[2]),(v[3],v[4],v[5]),(v[6],v[7],v[8])))
        return tris

def section_area(tris,y0):
    """Signed shoelace sum over directed intersection segments (consistent winding)."""
    s=0.0
    n=0
    for (p0,p1,p2) in tris:
        p=(p0,p1,p2)
        d=[q[1]-y0 for q in p]
        pts=[]
        for i in range(3):
            a=p[i]; b=p[(i+1)%3]; da=d[i]; db=d[(i+1)%3]
            if (da<0 and db>=0) or (da>=0 and db<0):
                f=da/(da-db)
                pts.append((a[0]+(b[0]-a[0])*f, a[2]+(b[2]-a[2])*f))
        if len(pts)==2:
            (x1,z1),(x2,z2)=pts
            s += (x1*z2 - x2*z1)
            n+=1
    return s/2.0, n

tris=load_stl('part.stl')
print('tris',len(tris))
for y0 in [0.2,5,20,42.5,60,85,120,150,169.8]:
    a,n=section_area(tris,y0)
    print(f'y={y0:6.1f}  area={a:10.3f} mm2   segs={n}')
