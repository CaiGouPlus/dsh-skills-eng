# -*- coding: utf-8 -*-
"""滑槽 —— 原件 vs 复刻件 拼版（轴测 + 前视 + 关键数据）

用法: python sheet_slide.py
"""
import os
from PIL import Image, ImageDraw, ImageFont

OUT = r'%DSH_WORKSPACE%\cae\part_read\out'
RB = r'%DSH_WORKSPACE%\cae\rebuild'
DST = os.path.join(OUT, 'slide_sheet.png')

FONT = r'C:\Windows\Fonts\msyh.ttc'
FONTB = r'C:\Windows\Fonts\msyhbd.ttc'


def f(size, bold=False):
    return ImageFont.truetype(FONTB if bold else FONT, size)


def bmp2png(src, dst):
    Image.open(src).convert('RGB').save(dst, quality=95)
    return dst


pairs = [
    ('iso',   os.path.join(OUT, 'slide.iso.bmp'),   os.path.join(RB, 'slide_iso.bmp'),   '轴测'),
    ('front', os.path.join(OUT, 'slide.front.bmp'), os.path.join(RB, 'slide_front.bmp'), '前视（沿 -Z）'),
]

CW, CH = 660, 495
PAD = 24
HEAD = 118
ROWLAB = 40
CAP = 40
W = CW * 2 + PAD * 3
H = HEAD + (CH + CAP + ROWLAB) * 2 + PAD * 2 + 60

sheet = Image.new('RGB', (W, H), (250, 250, 252))
d = ImageDraw.Draw(sheet)
d.rectangle([0, 0, W, HEAD], fill=(38, 52, 74))
d.text((PAD, 16), '滑槽（发射机构v1.3）· 原件 vs 独立复刻', font=f(30, True), fill=(255, 255, 255))
d.text((PAD, 58), '左：原件 91EA94CE（6 实特征，含 2 个"加了又切"的空转）    右：复刻 5 实特征',
       font=f(17), fill=(200, 215, 235))
d.text((PAD, 84), '体积 1978.1479 / 1978.1479 mm³（0.0000%）   面积 1924.3026 / 1924.3026 mm²   质量 5.3410 / 5.3410 g   22 面 50 边 28 点 全同',
       font=f(16), fill=(180, 200, 225))

y = HEAD + PAD
for key, a, b, lab in pairs:
    d.text((PAD, y - 4), lab, font=f(19, True), fill=(40, 55, 80))
    y += ROWLAB
    for k, src in enumerate((a, b)):
        x = PAD + k * (CW + PAD)
        d.rectangle([x - 1, y - 1, x + CW + 1, y + CH + 1], outline=(190, 198, 210))
        try:
            im = Image.open(src).convert('RGB')
            im.thumbnail((CW, CH), Image.LANCZOS)
            sheet.paste(im, (x + (CW - im.width) // 2, y + (CH - im.height) // 2))
        except Exception as e:
            d.text((x + 20, y + 20), 'ERR %s' % e, font=f(15), fill=(200, 60, 60))
        tag = '原件' if k == 0 else '复刻'
        d.text((x + 8, y + CH + 8), tag, font=f(17, True), fill=(38, 52, 74) if k == 0 else (20, 120, 80))
    y += CH + CAP

d.text((PAD, H - 48), '坐标：前视基准面 (x,y)，板厚方向 z 0→5；弧形槽 R20–R26，跨角 75°（±37.5° 对称于 +Y）',
       font=f(15), fill=(110, 120, 135))
sheet.save(DST)
print('saved', DST, sheet.size)
