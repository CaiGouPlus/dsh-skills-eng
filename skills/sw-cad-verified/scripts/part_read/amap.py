import numpy as np
rng=np.random.default_rng(7)
T=[]
for line in open(r'D:\DSH\DSH\cae\part_read\out\p5015seat.tess.txt',encoding='utf-8-sig'):
    p=line.strip().split('|')
    if len(p)!=5: continue
    T.append([[float(x)/1000.0 for x in q.split(',')] for q in p[1:4]])
T=np.array(T)
AX={'x':0,'y':1,'z':2}
def seg_at(axis,val):
    ai=AX[axis]; o=[i for i in range(3) if i!=ai]
    d=T[:,:,ai]-val; out=[]
    for t in range(len(T)):
        p=T[t]; dd=d[t]; sp=[]
        for i in range(3):
            j=(i+1)%3; a,b=dd[i],dd[j]
            if (a<=0<b) or (b<=0<a):
                f=a/(a-b); q=p[i]+(p[j]-p[i])*f
                sp.append((q[o[0]],q[o[1]]))
        if len(sp)==2: out.append(sp)
    return np.array(out)
def inside(axis,val,pts,tries=5,jit=0.0131):
    S=seg_at(axis,val); a=S[:,0,:]; b=S[:,1,:]
    acc=np.zeros(len(pts))
    for t in range(tries):
        u=pts[:,0]+rng.uniform(-jit,jit,len(pts)); v=pts[:,1]+rng.uniform(-jit,jit,len(pts))
        cross=((a[None,:,1]>v[:,None])!=(b[None,:,1]>v[:,None]))
        f=np.where(cross,(v[:,None]-a[None,:,1])/np.where(cross,b[None,:,1]-a[None,:,1],1.0),0.0)
        xs=a[None,:,0]+(b[None,:,0]-a[None,:,0])*f
        cnt=((xs>u[:,None])&cross).sum(axis=1)
        acc+=(cnt%2)
    return acc>tries/2
def amap(axis,val,u0,u1,v0,v1,step=1.0,names=('u','v')):
    us=np.arange(u0,u1+1e-9,step); vs=np.arange(v1,v0-1e-9,-step)
    U,V=np.meshgrid(us,vs)
    m=inside(axis,val,np.c_[U.ravel(),V.ravel()]).reshape(U.shape)
    print('=== %s=%+.2f  rows=%s cols=%s  (# material) ==='%(axis,val,names[1],names[0]))
    print('       '+''.join(str(int(abs(x))%10) for x in us))
    for i,v in enumerate(vs):
        print('%6.1f '%v+''.join('#' if x else '.' for x in m[i]))
if __name__=='__main__':
    import sys
    amap('y',2.5,-22,22,-22,29)
    print()
    amap('y',-2.0,-22,22,-22,29)
