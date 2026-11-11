import re, math
pat=re.compile(r'face\s+\d+\s+plane\s+area=\s*([\d.]+)\s+mm2\s+n=\(([-\d.]+),([-\d.]+),([-\d.]+)\)\s+p=\(([-\d.]+),([-\d.]+),([-\d.]+)\)')
planes=[]
for line in open('part.faces.log',encoding='utf-8-sig'):
    m=pat.search(line)
    if not m: continue
    a=float(m.group(1)); n=[float(m.group(2)),float(m.group(3)),float(m.group(4))]; p=[float(m.group(5)),float(m.group(6)),float(m.group(7))]
    # canonical sign
    for c in n:
        if abs(c)>1e-6:
            if c<0: n=[-x for x in n]
            break
    d=sum(n[i]*p[i] for i in range(3))
    planes.append((a,n,d))
fam={}
for a,n,d in planes:
    k=tuple(round(x,2) for x in n)
    fam.setdefault(k,[]).append((d,a))
print("plane families (canonical normal), d = n·p :")
for k,v in sorted(fam.items(), key=lambda kv:-sum(x[1] for x in kv[1])):
    ds=sorted(d for d,_ in v)
    print(f"  n={k}  faces={len(v):2d}  area={sum(a for _,a in v):9.2f}  d={[round(x,2) for x in ds]}")
print("\nparallel-plane gaps < 30 mm (thickness / depth candidates):")
items=[]
for k,v in fam.items():
    ds=sorted(set(round(d,2) for d,_ in v))
    for i in range(len(ds)):
        for j in range(i+1,len(ds)):
            gap=abs(ds[j]-ds[i])
            if gap<30: items.append((gap,k,ds[i],ds[j]))
items.sort()
seen=set()
for gap,k,d1,d2 in items:
    key=(k,round(d1,2),round(d2,2))
    if key in seen: continue
    seen.add(key)
    print(f"  {gap:6.2f} mm   n={k}  between d={d1} and {d2}")
