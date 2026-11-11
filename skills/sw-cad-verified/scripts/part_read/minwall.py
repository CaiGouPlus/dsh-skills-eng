# -*- coding: utf-8 -*-
"""截面最小壁厚（板内间隙）：minwall.py <tess.txt> [axis=val ...] [-k N]

用途：给流程 P1 第④层"微分"产出「最小壁厚分布」，并直接给出 DFM 关心的
      孔到边、槽到边、板内薄壁的**实测值**（不靠人眼量图）。

做法：
  1) 把一层的边界线段**串成闭合环**（这一步是必须的：切片线段是按三角面顺序
     给出的，没有环序；不串环就无法区分"相邻的两段"和"隔着材料的两段"）。
  2) 不同环之间（孔 vs 外轮廓）一律算"不相邻" → 直接给孔到边距离。
  3) 同一环上，只比较**沿环序号距离 > k** 的段对 → 排除相邻段（相邻必然为 0）。

为什么不能靠"线段序号差"直接判相邻（第一版就栽在这里）：
  线段顺序由三角面顺序决定，与边界走向无关，序号差没有任何几何意义，
  结果会把真正相邻的段当成薄壁、把真实薄壁漏掉，输出一片 0.0000。

注意（避免误读）：
  - 这是**板内（面内）**间隙，不是沿厚度方向的壁厚；等厚平板沿厚度恒为板厚。
  - 只在给定站位成立；薄壁若只出现在某些高度，要扫多个站位。
  - 3D 曲面/内部空腔的最小壁厚需沿法向打射线，本工具不覆盖。

用法：
  python minwall.py x.tess.txt                  # 默认扫 z 的 5 个站位
  python minwall.py x.tess.txt z 2.5 z 4.5      # 指定站位
  python minwall.py x.tess.txt z 3.0 -k 10      # 调相邻排除窗口
"""
import sys
import numpy as np

K = 6


def load(path):
    tris = []
    for line in open(path, encoding='utf-8-sig'):
        p = line.strip().split('|')
        if len(p) != 5:
            continue
        tris.append([[float(x) / 1000.0 for x in q.split(',')] for q in p[1:4]])
    print('loaded %d triangles from %s' % (len(tris), path))
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


def key(pt, q=1e-6):
    return (round(pt[0] / q), round(pt[1] / q))


def build_loops(segs, q=1e-6):
    """把线段串成闭合环。返回 [ [(a,b), (a,b), ... ] ]，每环内线段首尾相接。"""
    segs = [(np.array(a, float), np.array(b, float)) for a, b in segs]
    # 节点 -> 该节点上的线段索引
    node2seg = {}
    for idx, (a, b) in enumerate(segs):
        node2seg.setdefault(key(a, q), []).append(idx)
        node2seg.setdefault(key(b, q), []).append(idx)

    used = [False] * len(segs)
    loops = []
    for s0 in range(len(segs)):
        if used[s0]:
            continue
        # 从一个未使用的段出发，朝两个方向走
        used[s0] = True
        chain = [segs[s0]]
        # 向前
        cur = s0
        cur_pt = segs[s0][1]
        while True:
            nx = None
            for cand in node2seg.get(key(cur_pt, q), []):
                if cand != cur and not used[cand]:
                    nx = cand
                    break
            if nx is None:
                break
            used[nx] = True
            a, b = segs[nx]
            if np.linalg.norm(a - cur_pt) > np.linalg.norm(b - cur_pt):
                a, b = b, a
            chain.append((a, b))
            cur, cur_pt = nx, b
        # 向后（把起点反向补齐）
        cur = s0
        cur_pt = segs[s0][0]
        head = []
        while True:
            nx = None
            for cand in node2seg.get(key(cur_pt, q), []):
                if cand != cur and not used[cand]:
                    nx = cand
                    break
            if nx is None:
                break
            used[nx] = True
            a, b = segs[nx]
            if np.linalg.norm(b - cur_pt) > np.linalg.norm(a - cur_pt):
                a, b = b, a
            head.append((b, a))
            cur, cur_pt = nx, a
        loops.append(list(reversed(head)) + chain)
    return loops


def pt_seg(p, a, b):
    ab = b - a
    L2 = float(ab.dot(ab))
    if L2 < 1e-18:
        q = a
    else:
        t = max(0.0, min(1.0, float((p - a).dot(ab)) / L2))
        q = a + t * ab
    return float(np.linalg.norm(p - q)), q


def cross(o, p, q):
    return (p[0] - o[0]) * (q[1] - o[1]) - (p[1] - o[1]) * (q[0] - o[0])


def seg_seg(a1, a2, b1, b2, eps=1e-12):
    """两线段（2D）最小距离。相交返回 0。"""
    o1 = cross(a1, a2, b1)
    o2 = cross(a1, a2, b2)
    o3 = cross(b1, b2, a1)
    o4 = cross(b1, b2, a2)
    if (o1 * o2 < -eps) and (o3 * o4 < -eps):
        return 0.0, 0.5 * (a1 + b1)
    cand = []
    for p, s, t in ((a1, b1, b2), (a2, b1, b2), (b1, a1, a2), (b2, a1, a2)):
        d, q = pt_seg(p, s, t)
        cand.append((d, q))
    return min(cand, key=lambda z: z[0])


def min_clearance(loops, k=K):
    """跨环全比；同环按【沿环弧长】隔开足够远才比。

    为什么同环不能用"段数间隔"当排除条件（第一版 bug）：
      Ø3.2 的小孔被剖分成 48 段、周长只有 10 mm，间隔 7 段的两条弦在空间上
      仍只差 1.2 mm —— 被当成"壁厚 1.21"。跨环那一支一直是对的（1.400 / 0.200
      与解析值一致），错的是同环这一支。
      改用弧长判据：只有沿环走行距离 > 周长/4 的段对才算"隔着材料的两侧"。
    """
    best = None
    # 不同环之间：一律比较（孔 vs 外轮廓）
    for i in range(len(loops)):
        for j in range(i + 1, len(loops)):
            for a1, a2 in loops[i]:
                for b1, b2 in loops[j]:
                    d, q = seg_seg(a1, a2, b1, b2)
                    if best is None or d < best[0]:
                        best = (d, q, i, j)
    # 同一环内：按弧长距离排除相邻
    for li, lp in enumerate(loops):
        n = len(lp)
        seglen = [float(np.linalg.norm(lp[t][1] - lp[t][0])) for t in range(n)]
        per = sum(seglen)
        if per <= 0:
            continue
        # 前缀和，用于 O(1) 求沿环距离
        pre = [0.0]
        for t in range(n):
            pre.append(pre[-1] + seglen[t])
        thresh = per / 4.0
        for a in range(n):
            for b in range(a + 1, n):
                along = pre[b] - pre[a]
                along = min(along, per - along)
                if along <= thresh:
                    continue
                d, q = seg_seg(lp[a][0], lp[a][1], lp[b][0], lp[b][1])
                if best is None or d < best[0]:
                    best = (d, q, li, li)
    return best


def main():
    global K
    path = sys.argv[1]
    rest = sys.argv[2:]
    if '-k' in rest:
        i = rest.index('-k')
        K = int(rest[i + 1])
        rest = rest[:i] + rest[i + 2:]

    T = load(path)
    mn = T.reshape(-1, 3).min(axis=0)
    mx = T.reshape(-1, 3).max(axis=0)
    print('bbox x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]  k=%d'
          % (mn[0], mx[0], mn[1], mx[1], mn[2], mx[2], K))

    if rest:
        plan = [(rest[i], float(rest[i + 1])) for i in range(0, len(rest), 2)]
    else:
        z0, z1 = mn[2], mx[2]
        plan = [('z', z0 + (z1 - z0) * f) for f in (0.10, 0.30, 0.50, 0.70, 0.90)]

    print()
    print('  axis  station  最小壁厚(mm)   位置(x,y)             环数/段数')
    worst = None
    for axis, v in plan:
        S = segs_at(T, axis, v)
        loops = build_loops(S)
        r = min_clearance(loops)
        nseg = sum(len(l) for l in loops)
        if r is None:
            print('  %-5s %7.2f      (无有效线段)' % (axis, v))
            continue
        d, q, i, j = r
        print('  %-5s %7.2f %12.4f   (%8.3f,%8.3f)   %2d/%4d'
              % (axis, v, d, q[0], q[1], len(loops), nseg))
        if worst is None or d < worst[0]:
            worst = (d, axis, v, q, len(loops))
    print()
    if worst:
        print('最小壁厚 = %.4f mm  @ %s=%.2f, 位置 (%.3f, %.3f), 该层 %d 个环'
              % (worst[0], worst[1], worst[2], worst[3][0], worst[3][1], worst[4]))
    print('说明：这是【板内面内】间隙（孔到边/槽到边/薄壁），不是沿厚度方向的壁厚。')
    print('      只在所列站位成立；薄壁若只出现在某些高度，请加站位。')
    print('      判据：低于规则表阈值时走三级处置（取舍/FEA/停下），不要直接判不合格。')
    print('⚠ 精度看三角面密度：本切片继承模型剖分的 MaxFacetWidth，圆被多边形化后')
    print('  弧面附近的读数会偏小或偏大。绝对值请先用更细剖分复测；本工具更擅长')
    print('  「定位哪一处最薄」和「比较两模型/两版本谁更薄」，而不是直接当合格证。')


main()
