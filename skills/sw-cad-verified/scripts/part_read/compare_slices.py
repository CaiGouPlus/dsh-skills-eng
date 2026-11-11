# -*- coding: utf-8 -*-
"""两模型逐层截面积对比：原模型 vs 复刻件"""
import math
import numpy as np

A = r'D:\DSH\DSH\cae\part_read\out\p5015seat.tess.txt'
B = r'D:\DSH\DSH\cae\part_read\out_rebuild\5015_seat_rebuild.tess.txt'

def load(path):
    tris = []
    for line in open(path, encoding='utf-8-sig'):
        p = line.strip().split('|')
        if len(p) != 5:
            continue
        tris.append([[float(x) / 1000.0 for x in q.split(',')] for q in p[1:4]])
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

def area(segs, scale=6.0):
    if not segs:
        return 0.0
    us = [s[k][0] for s in segs for k in range(2)]
    vs = [s[k][1] for s in segs for k in range(2)]
    u0, u1, v0, v1 = min(us) - 1, max(us) + 1, min(vs) - 1, max(vs) + 1
    W = max(2, int(round((u1 - u0) * scale))); H = max(2, int(round((v1 - v0) * scale)))
    img = np.zeros((H, W), dtype=bool)
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
            c0 = max(0, int(round((xs[k] - u0) * scale))); c1 = min(W, int(round((xs[k + 1] - u0) * scale)))
            if c1 > c0:
                img[r, c0:c1] = True
    return float(np.count_nonzero(img)) / (scale * scale)

TA = load(A); TB = load(B)
print('原模型 triangles=%d   复刻 triangles=%d' % (TA.shape[0], TB.shape[0]))
print('\n%-8s %10s %10s %10s' % ('slice', 'orig mm2', 'rebuild', 'delta'))
for ax, vals in (('y', [-6, -3, -1, 1, 3, 6, 8, 11, 14, 17, 20, 23, 25, 26]), ('z', [-20, -15, -5, 5, 15, 20, 23, 26, 27.5]), ('x', [-20, -16, -10, 0, 10, 16, 20])):
    for v in vals:
        a = area(slice_segments(TA, ax, v)); b = area(slice_segments(TB, ax, v))
        flag = '  <<<' if abs(a - b) > 3 else ''
        print('%-8s %10.1f %10.1f %+10.1f%s' % ('%s=%g' % (ax, v), a, b, b - a, flag))
