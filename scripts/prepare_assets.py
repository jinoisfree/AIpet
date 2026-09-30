#!/usr/bin/env python3
"""Extract lossless build assets; never alter the upstream WebP or manifest."""
import hashlib
import json
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
PET = ROOT / 'Resources' / 'Pet'
OUTPUT = ROOT / '.build' / 'assets'
EXPECTED = '316b98b0445cdf0ac50dda91128e5772366f49787a4634addbfb1409c99dca35'

def prepare():
    source = PET / 'spritesheet.webp'
    assert hashlib.sha256(source.read_bytes()).hexdigest() == EXPECTED, 'Upstream asset hash changed'
    assert json.loads((PET / 'pet.json').read_text())['spriteVersionNumber'] == 2
    atlas = Image.open(source).convert('RGBA')
    assert atlas.size == (1536, 2288)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    atlas.save(OUTPUT / 'spritesheet.png')
    assert Image.open(OUTPUT / 'spritesheet.png').tobytes() == atlas.tobytes()
    iconset = OUTPUT / 'Taesik.iconset'
    iconset.mkdir(exist_ok=True)
    cat = atlas.crop((0, 0, 192, 208))
    for size in [16, 32, 128, 256, 512]:
        for scale in [1, 2]:
            width = size * scale
            canvas = Image.new('RGBA', (width, width))
            frame = cat.resize((round(width * 192 / 208), width), Image.Resampling.LANCZOS)
            canvas.alpha_composite(frame, ((width - frame.width) // 2, 0))
            canvas.save(iconset / f'icon_{size}x{size}{"@2x" if scale == 2 else ""}.png')
    canvas.save(OUTPUT / 'Taesik.icns', format='ICNS')
    print('태식이 원본 체크섬·PNG 픽셀 일치: 통과')

if __name__ == '__main__':
    prepare()
