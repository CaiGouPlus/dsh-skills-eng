# -*- coding: utf-8 -*-
"""5015 安装座 —— 层析图版：把零件沿三个方向切片，一次看清所有台阶/窗口/孔"""
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFont

TESS = r'D:\DSH\DSH\cae\part_read\out\p5015seat.tess.txt'
OUT = r'D:\DSH\DSH\cae\part_read\out\5015_layers.png'
FONT = r'C:\Windows\Fonts\msyh.ttc'

def load(path):
    tris = []
    for line in open(path, encoding='utf-8-sig'):
        p = line.strip().split('|')
        if len(p) != 5:
            continue
        v = [[float(x) / 1000.0 for x in q.split(',')] for q in p[1:4]]
        tris.append(v)
    return np.array(tris, dtype=float)

def slice_segments(T, axis, val):
    ai = {'x': 0, 'y': 1, 'z': 2}[axis]
    others = [i for i in range(3) if i != ai]
    d = T[:, :, ai] - val
    segs = []
    for t in range(T.shape[0]):
        p = T[t]; dd = d[t]
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

def raster(segs, scale, span):
    u0, u1, v0, v1 = span
    W = int(round((u1 - u0) * scale)); H = int(round((v1 - v0) * scale))
    img = np.zeros((H, W), dtype=np.uint8)
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
            c0 = int(round((xs[k] - u0) * scale)); c1 = int(round((xs[k + 1] - u0) * scale))
            if c1 > c0:
                img[r, c0:c1] = 255
    return img

T = load(TESS)
print('triangles', T.shape[0])
mins = T.reshape(-1, 3).min(axis=0); maxs = T.reshape(-1, 3).max(axis=0)
print('bbox mm', np.round(mins, 3), np.round(maxs, 3))

# ---- 面积-位置曲线，找台阶 ----
def area_profile(axis, a, b, step=0.25):
    out = []
    x = a
    while x <= b:
        segs = slice_segments(T, axis, x)
        if segs:
            span = (mins[0] - 1, maxs[0] + 1, mins[2] - 1, maxs[2] + 1) if axis == 'y' else \
                   (mins[0] - 1, maxs[0] + 1, mins[1] - 1, maxs[1] + 1)
            img = raster(segs, 4.0, span)
            out.append((x, float(np.count_nonzero(img)) / 16.0))
        x += step
    return out

for ax, a, b in (('y', -7.2, 26.5), ('z', -21.3, 28.3)):
    prof = area_profile(ax, a, b)
    print('\n--- area profile along %s ---' % ax)
    prev = None
    for x, A in prof:
        d = '' if prev is None else ('  dA=%+.1f' % (A - prev))
        j = ''
        if prev is not None and abs(A - prev) > 3:
            j = '  <<< STEP'
        print('%s=%6.2f  A=%8.1f mm2%s%s' % (ax, x, A, d, j))
        prev = A
