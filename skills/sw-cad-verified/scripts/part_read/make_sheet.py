# -*- coding: utf-8 -*-
"""5015 安装座 —— 出图版（四视图拼版 + 关键数据）"""
import os
from PIL import Image, ImageDraw, ImageFont

OUT = r'%DSH_WORKSPACE%\cae\part_read\out'
DST = r'%DSH_WORKSPACE%\cae\part_read\out\5015_seat_sheet.png'

FONT = r'C:\Windows\Fonts\msyh.ttc'
FONTB = r'C:\Windows\Fonts\msyhbd.ttc'

def f(size, bold=False):
    return ImageFont.truetype(FONTB if bold else FONT, size)

views = [
    ('p5015seat.iso.png',   '轴测 Iso'),
    ('p5015seat.front.png', '前视（沿 -Z，电机安装面 XZ 平面投影）'),
    ('p5015seat.top.png',   '上视（沿 -Y）'),
    ('p5015seat.right.png', '右视（沿 +X）'),
]

CW, CH = 780, 585          # 每格图区
PAD = 26
HEAD = 132
CAP = 44
W = CW * 2 + PAD * 3
H = HEAD + (CH + CAP) * 2 + PAD * 2

sheet = Image.new('RGB', (W, H), (250, 250, 252))
d = ImageDraw.Draw(sheet)

# 标题区
d.rectangle([0, 0, W, HEAD], fill=(38, 50, 66))
d.text((PAD, 18), '5015 安装座（云台）· 几何读图', font=f(40, True), fill=(255, 255, 255))
d.text((PAD, 74), '材料 6061  |  体积 12 408 mm³  |  质量 33.5 g  |  包围盒 42.9 × 33.9 × 49.9 mm  |  1 实体 64 面',
       font=f(21), fill=(196, 210, 226))

for i, (fn, cap) in enumerate(views):
    r, c = divmod(i, 2)
    x = PAD + c * (CW + PAD)
    y = HEAD + r * (CH + CAP) + PAD
    im = Image.open(os.path.join(OUT, fn)).convert('RGB')
    im.thumbnail((CW, CH), Image.LANCZOS)
    bg = Image.new('RGB', (CW, CH), (232, 234, 238))
    bg.paste(im, ((CW - im.width) // 2, (CH - im.height) // 2))
    sheet.paste(bg, (x, y))
    d.rectangle([x, y, x + CW - 1, y + CH - 1], outline=(176, 182, 192))
    d.text((x + 6, y + CH + 8), cap, font=f(22), fill=(40, 50, 66))

sheet.save(DST)
print('saved', DST, sheet.size)

# ---- 派生数据 ----
V = 12407.9699           # mm3
A = 7713.9326            # mm2
M = 33.5015              # g
rho = 2.70e-3            # g/mm3 (6061)
bx, by, bz = 42.903, 33.926, 49.852
print('density check  = %.4f g/cm3' % (M / V))
print('fill ratio     = %.1f %%' % (100 * V / (bx * by * bz)))
print('V/A (mean wall) = %.2f mm' % (V / A))
print('mass if 7075  = %.2f g' % (V * 2.81e-3))
print('mass if 3mm plate + 2mm ribs (approx -25%%) = %.1f g' % (M * 0.75))
