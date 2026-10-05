#!/usr/bin/env python3
"""Redraw the wave from the upstream sheet: a shorter arm and a slimmer body. Run by hand; the build only reads the result."""
import bisect
import math
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
PET = ROOT / 'Resources' / 'Pet'
W, H = 192, 208
INK = (16, 12, 12, 255)
# Each line is narrowed about PIVOT: the face a little, the belly side more, the back most.
PIVOT, BAND, FACE, BELLY, BACK = 100.0, 22.0, 0.94, 0.90, 0.76
FACE_ENDS, BODY_BEGINS = 78, 116
# The raised arm in the second and third frames: where it leaves the body, the paw's tip, and its left edge.
ARMS = {1: ((128.5, 81), (140.5, 28.5), [(118.8, 81), (118.8, 60), (124.0, 45.5), (121.6, 39.5)]),
        2: ((127.5, 82), (139.5, 27.5), [(118.8, 82), (118.8, 60), (123.4, 45.5), (121.0, 39.5)])}
WRIST, CUT = 33, 13


def ease(v):
    v = min(1.0, max(0.0, v))
    return v*v*(3 - 2*v)


def sample(im, x, y):
    """Bilinear, on colour weighted by alpha, so edges against nothing stay clean."""
    x0, y0 = math.floor(x), math.floor(y)
    acc = [0.0]*4
    for dx, dy in ((0, 0), (1, 0), (0, 1), (1, 1)):
        xx, yy = x0 + dx, y0 + dy
        if not (0 <= xx < W and 0 <= yy < H):
            continue
        w = (1 - abs(x - xx))*(1 - abs(y - yy))
        r, g, b, a = im.getpixel((xx, yy))
        acc[0] += r*a*w; acc[1] += g*a*w; acc[2] += b*a*w; acc[3] += a*w
    if acc[3] < 1:
        return (0, 0, 0, 0)
    return (round(acc[0]/acc[3]), round(acc[1]/acc[3]), round(acc[2]/acc[3]), round(acc[3]))


def shorter_arm(im, base, tip, left, pillow):
    """Take CUT px out of the forearm, between the body and the wrist; the paw itself moves down whole."""
    length = math.hypot(tip[0] - base[0], tip[1] - base[1])
    u = ((tip[0] - base[0])/length, (tip[1] - base[1])/length)
    n = (-u[1], u[0])
    keep = (WRIST - CUT)/WRIST
    arm = Image.new('L', (W, H), 0)
    ImageDraw.Draw(arm).polygon(left + [(tip[0] - 18.5, tip[1] - 6), (tip[0] - 6, tip[1] - 12), (tip[0] + 8, tip[1] - 12),
                                        (tip[0] + 18, tip[1] - 2), (tip[0] + 16, tip[1] + 14), (base[0] + 21, base[1] + 6),
                                        (base[0] - 12, base[1] + 4)], fill=255)
    behind = Image.new('L', (W, H), 0)  # where the pillow lies behind the arm
    ImageDraw.Draw(behind).polygon([(100, 44.5), (128, 44.5), (132.5, 50), (134.5, 60), (136.5, 70), (138.5, 82),
                                    (144, 100), (110, 100)], fill=255)
    out = im.copy()

    def along(x, y):
        return (x - base[0])*u[0] + (y - base[1])*u[1]

    for y in range(H):
        for x in range(W):
            if arm.getpixel((x, y)) and along(x + 0.5, y + 0.5) >= 0:
                out.putpixel((x, y), pillow if behind.getpixel((x, y)) else (0, 0, 0, 0))
    # The pillow's top edge carries on to where the paw now is.
    ImageDraw.Draw(out).line([(119.5, 42.4), (130.5, 42.9)], fill=INK, width=3)
    for y in range(H):
        for x in range(W):
            t2 = along(x + 0.5, y + 0.5)
            if not arm.getpixel((x, y)) or t2 < 0:
                continue
            side = (x + 0.5 - base[0])*n[0] + (y + 0.5 - base[1])*n[1]
            t = t2/keep if t2 < keep*WRIST else WRIST + (t2 - keep*WRIST)
            sx, sy = base[0] + t*u[0] + side*n[0] - 0.5, base[1] + t*u[1] + side*n[1] - 0.5
            if not (0 <= sx < W - 1 and 0 <= sy < H - 1) or not arm.getpixel((round(sx), round(sy))):
                continue
            nearest = im.getpixel((round(sx), round(sy)))
            if nearest[3] <= 40 or nearest[2] > nearest[0] + 18:  # nothing there, or pillow
                continue
            p = sample(im, sx, sy)
            under = out.getpixel((x, y))
            a = p[3]/255
            out.putpixel((x, y), tuple(round(p[i]*a + under[i]*(1 - a)) for i in range(3)) + (max(p[3], under[3]),))
    return out


def is_cat(p):
    r, g, b, a = p
    if a < 128 or b - r > 18:  # nothing there, or pillow
        return False
    return min(r, g, b) > 200 or (30 <= r <= 110 and r - b > 15)  # fur, or the brown patch


def slimmer(im):
    """Narrow the body and leave the face nearly as it was. The outline along the back is carried over whole so the
    line keeps its weight, and the pillow's outer edge stays put; the plain blue between the two takes up the difference."""
    px = im.load()
    widths = [0.0]
    for x in range(W):
        widths.append(widths[-1] + BACK + (BELLY - BACK)*ease((x + 0.5 - (PIVOT - BAND))/(2*BAND)))

    def total(x):
        k = min(W - 1, max(0, int(x)))
        return widths[k] + (widths[k + 1] - widths[k])*(x - k)

    def body(x):
        return PIVOT + total(x) - total(PIVOT)

    def face(x):
        return PIVOT + (x - PIVOT)*FACE

    # Where the cat begins on each line, never jumping more than 2 px from one line to the next.
    first = [next((x for x in range(W) if is_cat(px[x, y])), W) for y in range(H)]
    for y in range(1, H):
        first[y] = min(first[y], first[y - 1] + 2)
    for y in range(H - 2, -1, -1):
        first[y] = min(first[y], first[y + 1] + 2)
    out = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    po = out.load()
    for y in range(H):
        u = ease((y - FACE_ENDS)/(BODY_BEGINS - FACE_ENDS))
        inner, outer = first[y] + 0.5, first[y] - 4.0  # the outline along the back
        edge = body(inner)
        opaque = next((x for x in range(W) if px[x, y][3] >= 128), None)
        pillow = opaque + 5.0 if opaque is not None and opaque + 5 < outer - 2 else None

        def moved(x):
            if x >= inner:
                return body(x)
            if x >= outer:
                return edge - (inner - x)
            start = edge - (inner - outer)
            if pillow is None:
                return start - (outer - x)
            if x <= pillow:
                return face(x)
            return face(pillow) + (start - face(pillow))*(x - pillow)/(outer - pillow)

        lands = [(1 - u)*face(x) + u*moved(x) for x in range(W + 1)]  # where each pixel boundary ends up
        row = [tuple(c*p[3]/255 for c in p[:3]) + (p[3],) for p in (px[x, y] for x in range(W))]
        acc = [[0.0]*4 for _ in range(W)]
        for i in range(W*4):  # four samples to the pixel
            t = (i + 0.5)/4
            k = bisect.bisect_right(lands, t) - 1
            if k < 0 or k >= W:
                continue
            sx = k + (t - lands[k])/(lands[k + 1] - lands[k]) - 0.5
            a0 = min(W - 1, max(0, int(sx // 1)))
            a1 = min(W - 1, a0 + 1)
            w = min(1.0, max(0.0, sx - a0))
            for c in range(4):
                acc[i // 4][c] += (row[a0][c]*(1 - w) + row[a1][c]*w)/4
        for x in range(W):
            r, g, b, a = acc[x]
            po[x, y] = (round(r*255/a), round(g*255/a), round(b*255/a), round(a)) if a >= 1 else (0, 0, 0, 0)
    return out


def draw():
    atlas = Image.open(PET / 'spritesheet.webp').convert('RGBA')
    frames = [atlas.crop((c*W, 3*H, c*W + W, 4*H)) for c in range(4)]
    pillow = frames[0].getpixel((122, 52))
    sheet = Image.new('RGBA', (W*4, H), (0, 0, 0, 0))
    for column, frame in enumerate(frames):
        if column in ARMS:
            base, tip, left = ARMS[column]
            frame = shorter_arm(frame, base, tip, left, pillow)
        sheet.paste(slimmer(frame), (column*W, 0))
    sheet.save(PET / 'wave.png')
    print('손 흔들기 그림을 다시 그렸습니다:', PET / 'wave.png')


if __name__ == '__main__':
    draw()
