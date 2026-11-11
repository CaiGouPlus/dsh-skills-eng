import numpy as np

def load(path):
    rows=[]
    for line in open(path,encoding='utf-8-sig'):
        p=line.rstrip('\n').split('|')
        if len(p)!=19: continue
        try:
            rows.append(dict(name=p[0],vol=float(p[1]),bx=float(p[2]),by=float(p[3]),bz=float(p[4]),
                area=float(p[5]),faces=int(p[6]),edges=int(p[7]),verts=int(p[8]),
                plane=int(p[9]),cyl=int(p[10]),other=int(p[11]),
                hist=[float(x) for x in p[12:19]]))
        except ValueError: pass
    return rows

rows=load('desc.txt')
F=[]; names=[]
for r in rows:
    d=sorted([r['bx'],r['by'],r['bz']],reverse=True)
    m=max(d) or 1
    fill=r['vol']/((r['bx']*r['by']*r['bz']) or 1)
    sv=r['area']/((r['vol']**(2/3)) or 1)
    h=[x/(r['area'] or 1) for x in r['hist']]
    f=[np.log(r['vol']+1),np.log(r['area']+1),d[0]/m,d[1]/m,d[2]/m,fill,sv]+h+[r['plane']/(r['faces'] or 1),r['cyl']/(r['faces'] or 1)]
    F.append(f); names.append(r['name'])
F=np.array(F)
F=(F-F.mean(0))/(F.std(0)+1e-9)
D=np.sqrt(((F[:,None,:]-F[None,:,:])**2).sum(-1))
print('nearest neighbours (z-scored euclidean):')
for i in range(len(names)):
    order=np.argsort(D[i])
    print(f'{names[i][:20]:<22} -> '+', '.join(f'{names[j][:16]}({D[i,j]:.2f})' for j in order[1:4]))
print()
print('closest pairs:')
pairs=[(D[i,j],i,j) for i in range(len(names)) for j in range(i+1,len(names))]
pairs.sort()
for d,i,j in pairs[:10]:
    print(f'  {d:6.2f}  {names[i][:20]:<22} {names[j][:20]}')
