import re, sys, math
import numpy as np

def load(path):
    rows=[]
    for line in open(path,encoding='utf-8-sig'):
        line=line.rstrip('\n')
        if '|' not in line: continue
        p=line.split('|')
        if len(p)!=19: continue
        try:
            rows.append(dict(
                name=p[0], vol=float(p[1]), bx=float(p[2]), by=float(p[3]), bz=float(p[4]),
                area=float(p[5]), faces=int(p[6]), edges=int(p[7]), verts=int(p[8]),
                plane=int(p[9]), cyl=int(p[10]), other=int(p[11]),
                hist=[float(x) for x in p[12:19]]))
        except ValueError:
            continue
    return rows

def vec(r):
    dims=sorted([r['bx'],r['by'],r['bz']],reverse=True)
    m=max(dims) or 1
    fill=r['vol']/(r['bx']*r['by']*r['bz'] or 1)
    sv=r['area']/(r['vol']**(2/3)) if r['vol']>0 else 0
    h=[x/(r['area'] or 1) for x in r['hist']]
    return np.array(dims[0]/m*[1,1,1] if False else [dims[0]/m,dims[1]/m,dims[2]/m]+[fill,sv]+h+[r['plane']/(r['faces'] or 1), r['cyl']/(r['faces'] or 1)])

rows=load('desc.txt')
print('loaded',len(rows))
V=np.array([vec(r) for r in rows])
names=[r['name'] for r in rows]

# cosine similarity
Vn=V/np.linalg.norm(V,axis=1,keepdims=True)
S=Vn@Vn.T
short=[n[:14] for n in names]
print('\ncosine similarity:')
print('               '+' '.join(f'{s:>14}' for s in short))
for i,s in enumerate(short):
    print(f'{s:>14} '+' '.join(f'{S[i,j]:14.3f}' for j in range(len(short))))
print('\nnearest neighbor:')
for i,s in enumerate(short):
    order=np.argsort(-S[i])
    nb=[short[j] for j in order[1:3]]
    print(f'  {s:>20} -> {nb[0]} ({S[i,order[1]]:.3f}), {nb[1]} ({S[i,order[2]]:.3f})')

# plane families of the chain-guard from faces log
print('\nplane families (拖链挡板):')
fam={}
pat=re.compile(r'face\s+\d+\s+plane\s+area=\s*([\d.]+)\s+mm2\s+n=\(([-\d.]+),([-\d.]+),([-\d.]+)\)')
for line in open('part.faces.log',encoding='utf-8-sig'):
    m=pat.search(line)
    if not m: continue
    a=float(m.group(1)); n=(round(float(m.group(2)),2),round(float(m.group(3)),2),round(float(m.group(4)),2))
    fam.setdefault(n,[0,0]); fam[n][0]+=1; fam[n][1]+=a
for n,(c,a) in sorted(fam.items(),key=lambda kv:-kv[1][1]):
    print(f'  n={n}  faces={c:2d}  area={a:9.2f} mm2')
