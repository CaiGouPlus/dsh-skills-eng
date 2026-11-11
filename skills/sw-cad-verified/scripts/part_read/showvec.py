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
sel=['拖链挡板','2020铝方管111','2020铝方管380','10齿链轮','3M25','GB','3508','4轴']
print(f"{'part':<18}{'logV':>7}{'logA':>7}{'d0':>6}{'d1':>6}{'d2':>6}{'fill':>6}{'sv':>7}{' +x':>6}{' -x':>6}{' +y':>6}{' -y':>6}{' +z':>6}{' -z':>6}{'oth':>6}{'pl/f':>6}{'cy/f':>6}")
for r in rows:
    if not any(s in r['name'] for s in sel): continue
    d=sorted([r['bx'],r['by'],r['bz']],reverse=True); m=max(d) or 1
    fill=r['vol']/((r['bx']*r['by']*r['bz']) or 1)
    sv=r['area']/((r['vol']**(2/3)) or 1)
    h=[x/(r['area'] or 1) for x in r['hist']]
    f=[np.log(r['vol']+1),np.log(r['area']+1),d[0]/m,d[1]/m,d[2]/m,fill,sv]+h+[r['plane']/(r['faces'] or 1),r['cyl']/(r['faces'] or 1)]
    print(f"{r['name'][:16]:<18}"+''.join(f"{x:7.3f}" if i<7 else f"{x:6.2f}" for i,x in enumerate(f)))
