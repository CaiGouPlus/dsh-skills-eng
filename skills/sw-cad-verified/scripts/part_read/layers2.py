# -*- coding: utf-8 -*-
"""5015 安装座 —— 逐层测量：每个 Y 截面的材料 bbox + 所有孔/窗口的 bbox"""
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFont

TESS = r'D:\DSH\DSH\cae\part_read\out\p5015seat.tess.txt'
OUT = r'D:\DSH\DSH\cae\part_read\out\5015_layers.png'
FONT = r'C:\Windows\Fonts\msyh.ttc'
SCALE = 6.0

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

def raster(segs, span, scale=SCALE):
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

def blobs(mask, v0, scale, min_area=0.8):
    """4-连通区域 -> (面积 mm2, bbox in model coords)"""
    H, W = mask.shape
    seen = np.zeros_like(mask, dtype=bool)
    out = []
    for r in range(H):
        for c in range(W):
            if not mask[r, c] or seen[r, c]:
                continue
            stack = [(r, c)]; seen[r, c] = True
            rs = []; cs = []
            while stack:
                y, x = stack.pop()
                rs.append(y); cs.append(x)
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    ny, nx = y + dy, x + dx
                    if 0 <= ny < H and 0 <= nx < W and mask[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        stack.append((ny, nx))
            a = len(rs) / (scale * scale)
            if a < min_area:
                continue
            out.append((a, (min(cs) / scale, max(cs) / scale, v0 + min(rs) / scale, v0 + max(rs) / scale)))
    return out

T = load(TESS)
mins = T.reshape(-1, 3).min(axis=0); maxs = T.reshape(-1, 3).max(axis=0)
SPAN_XZ = (mins[0] - 2, maxs[0] + 2, mins[2] - 2, maxs[2] + 2)
SPAN_XY = (mins[0] - 2, maxs[0] + 2, mins[1] - 2, maxs[1] + 2)

def report(axis, val):
    segs = slice_segments(T, axis, val)
    if not segs:
        return None
    if axis in ('y', 'z'):   # XZ plane
        span, v0 = SPAN_XZ, SPAN_XZ[2]
        axname = 'X / Z'
    else:
        span, v0 = SPAN_XY, SPAN_XY[2]
        axname = 'X / Y'
    img = raster(segs, span)
    mat = blobs(img > 127, v0, SCALE, 1.0)
    hole = blobs(img <= 127, v0, SCALE, 1.0)
    # outer contour = largest material blob bbox; interior holes = holes inside it
    print('\n== %s = %6.2f mm  (plane %s) ==' % (axis, val, axname))
    if mat:
        tot = sum(b[0] for b in mat)
        print('   material area = %.1f mm2 in %d blob(s)' % (tot, len(mat)))
        for a, bb in sorted(mat, key=lambda z: -z[0])[:6]:
            print('     mat A=%7.2f  u[%6.2f,%6.2f] v[%6.2f,%6.2f]' % (a, bb[0], bb[1], bb[2], bb[3]))
    holes = [h for h in hole if h[0] < 600]
    print('   holes/windows: %d' % len(holes))
    for a, bb in sorted(holes, key=lambda z: -z[0])[:10]:
        print('     hole A=%6.2f  u[%6.2f,%6.2f] v[%6.2f,%6.2f]  (%.2f x %.2f)' %
              (a, bb[0], bb[1], bb[2], bb[3], bb[1] - bb[0], bb[3] - bb[2]))
    return img

if __name__ == '__main__':
    for y in (-5.6, -2.0, 2.0, 5.3, 8.0, 12.0, 18.0, 23.0, 25.8):
        report('y', y)
