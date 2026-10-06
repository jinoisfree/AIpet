#!/usr/bin/env python3
"""Draw the idle pose asleep: one eye shut, then both, then the head nodding forward a little and a little more.
Run by hand; the build only reads the result."""
import bisect
from pathlib import Path
import math
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
PET = ROOT / 'Resources' / 'Pet'
W, H = 192, 208
EYES = ((61.5, 54.6, 3.6), (88.3, 49.7, 3.2))   # each eye's middle and half its width; the second sits beside the phone
INK = (16, 12, 12, 255)
NODS = (0, 3, 6)           # how far the head comes down in each frame
HEAD_ENDS, BODY_BEGINS = 80, 112   # everything above moves whole; the body below stays put


def ease(v):
    v = min(1.0, max(0.0, v))
    return v*v*(3 - 2*v)


def nod(im, drop):
    """Lower the head, the phone and the paws holding it; the chest under them takes up the difference.
    The pillow on either side of the head stays where it is."""
    if drop == 0:
        return im
    px = im.load()
    out = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    po = out.load()
    for x in range(W):
        across = ease((x - 20)/12)*(1 - ease((x - 124)/10))
        lands = [y + drop*across*(1 - ease((y - HEAD_ENDS)/(BODY_BEGINS - HEAD_ENDS))) for y in range(H + 1)]
        column = [tuple(c*p[3]/255 for c in p[:3]) + (p[3],) for p in (px[x, y] for y in range(H))]
        for y in range(H):
            acc = [0.0]*4
            for part in range(4):  # four samples to the pixel
                t = y + (part + 0.5)/4
                k = bisect.bisect_right(lands, t) - 1
                if k < 0 or k >= H:
                    continue
                sy = k + (t - lands[k])/(lands[k + 1] - lands[k]) - 0.5
                a0 = min(H - 1, max(0, int(sy // 1)))
                a1 = min(H - 1, a0 + 1)
                w = min(1.0, max(0.0, sy - a0))
                for c in range(4):
                    acc[c] += (column[a0][c]*(1 - w) + column[a1][c]*w)/4
            r, g, b, a = acc
            po[x, y] = (round(r*255/a), round(g*255/a), round(b*255/a), round(a)) if a >= 1 else (0, 0, 0, 0)
    return out


def shut(im, eyes):
    """Paint an open eye out and draw it shut: a nearly flat line sagging a little in the middle, the way a sleeping eye is drawn."""
    k = 8
    fur = im.getpixel((75, 50))
    tilt = math.atan2(EYES[1][1] - EYES[0][1], EYES[1][0] - EYES[0][0])
    over = Image.new('RGBA', (W*k, H*k), (0, 0, 0, 0))
    d = ImageDraw.Draw(over)
    for x, y, half in eyes:
        d.ellipse([(x - 4.6)*k, (y - 4.6)*k, (x + 4.6)*k, (y + 4.6)*k], fill=fur)
        points = []
        for i in range(17):
            t = -1 + i/8
            along, down = t*half, 0.8*(1 - t*t) - 0.2
            points.append(((x + along*math.cos(tilt) - down*math.sin(tilt))*k, (y + along*math.sin(tilt) + down*math.cos(tilt))*k))
        d.line(points, fill=INK, width=round(2.3*k), joint='curve')
        for px, py in (points[0], points[-1]):
            d.ellipse([px - 1.15*k, py - 1.15*k, px + 1.15*k, py + 1.15*k], fill=INK)
    out = im.copy()
    out.alpha_composite(over.convert('RGBa').resize((W, H), Image.BOX).convert('RGBA'))
    return out


def draw():
    atlas = Image.open(PET / 'spritesheet.webp').convert('RGBA')
    idle = atlas.crop((0, 0, W, H))
    asleep = shut(idle, EYES)
    frames = [shut(idle, EYES[:1])] + [nod(asleep, drop) for drop in NODS]
    sheet = Image.new('RGBA', (W*len(frames), H), (0, 0, 0, 0))
    for column, frame in enumerate(frames):
        sheet.paste(frame, (column*W, 0))
    sheet.save(PET / 'doze.png')
    print('잠든 얼굴을 그렸습니다:', PET / 'doze.png')


if __name__ == '__main__':
    draw()
