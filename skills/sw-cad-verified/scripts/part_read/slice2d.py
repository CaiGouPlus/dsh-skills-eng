import sys, math
import numpy as np
from PIL import Image, ImageDraw

TESS = r'D:\DSH\DSH\cae\part_read\out\p5015seat.tess.txt'
OUT = r'D:\DSH\DSH\cae\part_read\out'

def load(path):
    tris = []
    for line in open(path, encoding='utf-8-sig'):
        p = line.strip().split('|')
        if len(p) != 5:
            continue
        v = [[float(x)/1000.0 for x in q.split(',')] for q in p[1:4]]   # tess comes in units of 1e-3 mm
        tris.append(v)
    return np.array(tris, dtype=float)   # (N,3,3) mm

def slice_segments(T, axis, val):
    ai = {'x': 0, 'y': 1, 'z': 2}[axis]
    others = [i for i in range(3) if i != ai]
    d = T[:, :, ai] - val
    segs = []
    for t in range(T.shape[0]):
        p = T[t]
        dd = d[t]
        pts = []
        for i in range(3):
            j = (i + 1) % 3
            a, b = dd[i], dd[j]
            if (a <= 0 < b) or (b <= 0 < a):
                f = a / (a - b)
                q = p[i] + (p[j] - p[i]) * f
                pts.append((q[others[0]], q[others[1]]))
        if len(pts) == 2:
            segs.append(pts)
    return segs

def rasterize(segs, scale=16.0, pad=3.0, label=''):
    us = [s[k][0] for s in segs for k in range(2)]
    vs = [s[k][1] for s in segs for k in range(2)]
    u0, u1 = min(us) - pad, max(us) + pad
    v0, v1 = min(vs) - pad, max(vs) + pad
    W = int(round((u1 - u0) * scale)); H = int(round((v1 - v0) * scale))
    img = np.zeros((H, W), dtype=np.uint8)
    # bucket segments by row range for speed
    bucket = [[] for _ in range(H)]
    for (a, b) in segs:
        if abs(a[1] - b[1]) < 1e-9:
            continue
        r0 = max(0, int(math.floor((min(a[1], b[1]) - v0) * scale)))
        r1 = min(H - 1, int(math.ceil((max(a[1], b[1]) - v0) * scale)))
        for r in range(r0, r1 + 1):
            bucket[r].append((a, b))
    for r in range(H):
        v = v0 + (r + 0.5) / scale
        xs = []
        for (a, b) in bucket[r]:
            if (a[1] <= v < b[1]) or (b[1] <= v < a[1]):
                f = (v - a[1]) / (b[1] - a[1])
                xs.append(a[0] + (b[0] - a[0]) * f)
        xs.sort()
        for k in range(0, len(xs) - 1, 2):
            c0 = int(round((xs[k] - u0) * scale))
            c1 = int(round((xs[k + 1] - u0) * scale))
            if c1 > c0:
                img[r, c0:c1] = 255
    area = float(np.count_nonzero(img)) / (scale * scale)
    rgb = np.zeros((H, W, 3), dtype=np.uint8)
    rgb[:, :, 0] = img; rgb[:, :, 1] = img; rgb[:, :, 2] = img
    im = Image.fromarray(rgb, 'RGB')
    d = ImageDraw.Draw(im)
    # 5 mm grid
    u = math.ceil(u0 / 5) * 5
    while u < u1:
        c = int(round((u - u0) * scale))
        d.line([(c, 0), (c, H)], fill=(40, 90, 160), width=1)
        d.text((c + 2, 2), '%g' % u, fill=(120, 180, 255))
        u += 5
    v = math.ceil(v0 / 5) * 5
    while v < v1:
        r = int(round((v1 - v) * scale))
        d.line([(0, r), (W, r)], fill=(40, 90, 160), width=1)
        d.text((2, r + 1), '%g' % v, fill=(120, 180, 255))
        v += 5
    d.rectangle([0, 0, W - 1, H - 1], outline=(200, 200, 200))
    return im, area

def main():
    T = load(TESS)
    print('triangles', T.shape[0])
    jobs = [('y', 2.0, 'sec_y+2.0.png'), ('y', -2.0, 'sec_y-2.0.png'),
            ('y', 5.5, 'sec_y+5.5.png'), ('y', 15.0, 'sec_y+15.png'),
            ('x', 0.0, 'sec_x00.png'), ('z', 19.0, 'sec_z19.png'), ('z', 24.0, 'sec_z24.png')]
    for axis, val, name in jobs:
        segs = slice_segments(T, axis, val)
        if not segs:
            print(name, 'no segments'); continue
        im, area = rasterize(segs, 16.0, 2.0)
        p = OUT + '\\' + name
        im.save(p)
        print('%-18s %s  segs=%d  section area = %.2f mm2  (%dx%d)' % (name, p, len(segs), area, im.size[0], im.size[1]))

main()
