# -*- coding: utf-8 -*-
"""ASCII 层析：逐层打印截面（不翻转、带坐标标尺），用来读形状"""
import math
import numpy as np

TESS = r'%DSH_WORKSPACE%\cae\part_read\out\p5015seat.tess.txt'

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

def ascii_slice(segs, span, ncol=78, nrow=46):
    u0, u1, v0, v1 = span
    grid = [[' '] * ncol for _ in range(nrow)]
    for r in range(nrow):
        v = v1 - (r + 0.5) * (v1 - v0) / nrow        # top row = v1
        xs = []
        for (a, b) in segs:
            if (a[1] <= v < b[1]) or (b[1] <= v < a[1]):
                f = (v - a[1]) / (b[1] - a[1])
                xs.append(a[0] + (b[0] - a[0]) * f)
        xs.sort()
        for k in range(0, len(xs) - 1, 2):
            c0 = int(round((xs[k] - u0) / (u1 - u0) * ncol))
            c1 = int(round((xs[k + 1] - u0) / (u1 - u0) * ncol))
            for c in range(max(0, c0), min(ncol, c1)):
                grid[r][c] = '#'
    return grid

T = load(TESS)
mins = T.reshape(-1, 3).min(axis=0); maxs = T.reshape(-1, 3).max(axis=0)
SPAN_XZ = (mins[0] - 1, maxs[0] + 1, mins[2] - 1, maxs[2] + 1)
SPAN_XY = (mins[0] - 1, maxs[0] + 1, mins[1] - 1, maxs[1] + 1)

def show(axis, val):
    if axis in ('y', 'z'):
        span = SPAN_XZ
        hdr = '  x: %.1f .. %.1f (left->right)   v: z %.1f .. %.1f (bottom->top)' % (span[0], span[1], span[2], span[3])
    else:
        span = SPAN_XY
        hdr = '  x: %.1f .. %.1f   v: y %.1f .. %.1f' % (span[0], span[1], span[2], span[3])
    segs = slice_segments(T, axis, val)
    g = ascii_slice(segs, span)
    print('\n===== %s = %.2f =====  %s' % (axis, val, hdr))
    # x ruler
    n = 78
    print('     ' + ''.join(('|' if i % 10 == 0 else '.') for i in range(n)))
    for r, row in enumerate(g):
        v = span[3] - (r + 0.5) * (span[3] - span[2]) / len(g)
        print('%+6.1f' % v + ''.join(row))

for y in (-5.6, -2.0, 2.0, 5.3, 8.0, 12.0, 18.0, 23.0, 25.8):
    show('y', y)
