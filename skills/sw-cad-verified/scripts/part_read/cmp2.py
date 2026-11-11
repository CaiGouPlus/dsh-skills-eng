# -*- coding: utf-8 -*-
"""逐层截面积对比：cmp2.py <a.tess.txt> <b.tess.txt>

对每个方向按固定站位切两层网格，比较两块面积。判据：|Δ面积| < 3 mm²。
（面积用闭合折线鞋带公式，弧线被三角剖分折线化，8 位精度足够比较两模型。）
"""
import sys
import numpy as np


def load(path):
    tris = []
    for line in open(path, encoding='utf-8-sig'):
        p = line.strip().split('|')
        if len(p) != 5:
            continue
        tris.append([[float(x) / 1000.0 for x in q.split(',')] for q in p[1:4]])
    return np.array(tris, dtype=float)


def segs_at(T, axis, val):
    ai = {'x': 0, 'y': 1, 'z': 2}[axis]
    others = [i for i in range(3) if i != ai]
    d = T[:, :, ai] - val
    out = []
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
            out.append(pts)
    return out


def section_area(T, axis, val, nrow=2000):
    """截面面积：按行取精确区间长度 × 行高（中点求积）。

    不用有向线段求和：三角剖分给出的线段方向不一致，有向面积会互相抵消
    （实测把 415 mm² 算成 1.18 mm²）。逐行 even-odd 区间法对两个模型用同一
    网格，绝对值和差值都可信。
    """
    ai = {'x': 0, 'y': 1, 'z': 2}[axis]
    oi = [i for i in range(3) if i != ai]
    mn = T.reshape(-1, 3).min(axis=0)
    mx = T.reshape(-1, 3).max(axis=0)
    v0, v1 = mn[oi[1]], mx[oi[1]]
    u0, u1 = mn[oi[0]], mx[oi[0]]
    u0 -= 0.5; u1 += 0.5
    dv = (v1 - v0) / nrow
    S = segs_at(T, axis, val)
    if not S:
        return 0.0
    total = 0.0
    for i in range(nrow):
        v = v0 + (i + 0.5) * dv
        xs = []
        for (a, b) in S:
            if (a[1] <= v < b[1]) or (b[1] <= v < a[1]):
                f = (v - a[1]) / (b[1] - a[1])
                xs.append(a[0] + (b[0] - a[0]) * f)
        xs.sort()
        for k in range(0, len(xs) - 1, 2):
            total += (xs[k + 1] - xs[k]) * dv
    return total


def main():
    A = load(sys.argv[1])
    B = load(sys.argv[2])
    for T, n in ((A, 'A'), (B, 'B')):
        mn = T.reshape(-1, 3).min(axis=0)
        mx = T.reshape(-1, 3).max(axis=0)
        print('%s bbox  x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]  tris=%d'
              % (n, mn[0], mx[0], mn[1], mx[1], mn[2], mx[2], T.shape[0]))
    mn = np.minimum(A.reshape(-1, 3).min(axis=0), B.reshape(-1, 3).min(axis=0))
    mx = np.maximum(A.reshape(-1, 3).max(axis=0), B.reshape(-1, 3).max(axis=0))

    plans = []
    for v in (0.6, 1.0, 1.5, 2.5, 3.5, 4.4):
        plans.append(('z', v))
    for v in (13.5, 15.0, 17.0, 19.0, 22.0, 25.0, 27.0, 30.0, 33.0):
        plans.append(('y', v))
    for v in (-18.0, -12.0, -6.0, 0.0, 6.0, 12.0, 18.0):
        plans.append(('x', v))

    print()
    print('  axis  station     A(mm2)      B(mm2)      dA')
    worst = 0.0
    worst_at = ''
    for axis, v in plans:
        aa = section_area(A, axis, v)
        bb = section_area(B, axis, v)
        d = bb - aa
        flag = '  <-- 超差' if abs(d) >= 3.0 else ''
        print('  %-5s %8.2f %11.4f %11.4f %+8.4f%s' % (axis, v, aa, bb, d, flag))
        if abs(d) > worst:
            worst = abs(d)
            worst_at = '%s=%.2f' % (axis, v)
    print()
    print('最大截面积差 = %.4f mm2  @ %s   判据 < 3 mm2  ->  %s'
          % (worst, worst_at, 'PASS' if worst < 3.0 else 'FAIL'))


main()
