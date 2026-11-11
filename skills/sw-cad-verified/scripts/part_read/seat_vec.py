import math
from collections import defaultdict

def load():
    rows=[]
    for line in open('seat.faces.txt',encoding='utf-8-sig'):
        p=line.rstrip('\n').split('|')
        if len(p)<5: continue
        code=p[0]; idx=int(p[1]); area=float(p[2]); vals=[float(x) for x in p[3].split(',')] if p[3] not in ('','0') else []
        box=[float(x) for x in p[4].split(',')] if p[4]!='-' else None
        rows.append((code,idx,area,vals,box))
    return rows
rows=load()
print('faces:',len(rows))
codes=defaultdict(int)
for c in rows: codes[c[0]]+=1
print('face types:',dict(codes))

# bbox from boxes
xs=[];ys=[];zs=[]
for c,i,a,v,b in rows:
    if b: xs+= [b[0],b[1]]; ys+=[b[2],b[3]]; zs+=[b[4],b[5]]
print('bbox approx x[%.2f,%.2f] y[%.2f,%.2f] z[%.2f,%.2f]'%(min(xs),max(xs),min(ys),max(ys),min(zs),max(zs)))

# planes
def canon(n):
    n=list(n)
    for c in n:
        if abs(c)>1e-6:
            if c<0: n=[-x for x in n]
            break
    return n
fam=defaultdict(list)
for c,i,a,v,b in rows:
    if c!='P': continue
    n=canon(v[0:3]); p=v[3:6]
    d=sum(n[k]*p[k] for k in range(3))
    fam[tuple(round(x,3) for x in n)].append((round(d,2),a))
print('\nplane families:')
for n,v in sorted(fam.items(), key=lambda kv:-sum(x[1] for x in kv[1])):
    ds=sorted(set(x[0] for x in v))
    print('  n=%-26s faces=%2d area=%9.2f d=%s'%(str(n),len(v),sum(x[1] for x in v),ds))

# cylinders: group by radius then axis
cg=defaultdict(list)
for c,i,a,v,b in rows:
    if c!='C': continue
    p=v[0:3]; ax=v[3:6]; r=v[6]
    cg[round(r,2)].append((p,ax,b,a))
print('\ncylinder radius groups:')
for r,v in sorted(cg.items()):
    print('  R%.2f  faces=%2d  area=%9.2f'%(r,len(v),sum(x[3] for x in v)))
    # coaxial grouping by axis dir + line distance
    groups=[]
    for p,ax,b,a in v:
        placed=False
        for g in groups:
            ax0=g['ax']; p0=g['p']
            cross=[ax[1]*ax0[2]-ax[2]*ax0[1], ax[2]*ax0[0]-ax[0]*ax0[2], ax[0]*ax0[1]-ax[1]*ax0[0]]
            if max(abs(x) for x in cross)<0.02:
                # distance between lines
                dvec=[p[k]-p0[k] for k in range(3)]
                # perpendicular distance
                proj=sum(dvec[k]*ax[k] for k in range(3))
                perp=[dvec[k]-proj*ax[k] for k in range(3)]
                if math.sqrt(sum(x*x for x in perp))<0.02:
                    g['faces'].append((p,ax,b,a)); placed=True; break
        if not placed:
            groups.append(dict(p=p,ax=ax,faces=[(p,ax,b,a)]))
    for g in groups:
        zs_=[f[2][4] for f in g['faces'] if f[2]]; ze_=[f[2][5] for f in g['faces'] if f[2]]
        print('     axis dir=(%.3f,%.3f,%.3f) point=(%.2f,%.2f,%.2f) span=%.2f..%.2f'%(
            g['ax'][0],g['ax'][1],g['ax'][2],g['p'][0],g['p'][1],g['p'][2],min(zs_),max(ze_)))

# cones & tori
print('\ncones (K):')
for c,i,a,v,b in rows:
    if c!='K': continue
    print('  p=(%.2f,%.2f,%.2f) axis=(%.2f,%.2f,%.2f) R=%.2f angle=%.4f rad (%.2f deg) area=%.2f'%(
        v[0],v[1],v[2],v[3],v[4],v[5],v[6],v[7],math.degrees(v[7]),a))
print('\ntori (T):')
for c,i,a,v,b in rows:
    if c!='T': continue
    print('  center=(%.2f,%.2f,%.2f) axis=(%.2f,%.2f,%.2f) R=%.2f r=%.2f area=%.2f'%(v[0],v[1],v[2],v[3],v[4],v[5],v[6],v[7],a))
