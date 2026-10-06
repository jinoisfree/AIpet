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
# The slimmer wave drawn by draw_wave.py, pinned by its pixels so that only an intended redraw changes it.
WAVE = 'b0beffe97a9e093f92a56009ec341c924b9e4e49c36f8ff295e0ef4a7a9ea1ba'
# The idle pose asleep and nodding, drawn by draw_doze.py and pinned the same way.
DOZE = '9e5073f5811656ebdaba62eaedc6d060006092cfca4ca8239f401575f0a2bf99'

def prepare():
    source = PET / 'spritesheet.webp'
    assert hashlib.sha256(source.read_bytes()).hexdigest() == EXPECTED, 'Upstream asset hash changed'
    assert json.loads((PET / 'pet.json').read_text())['spriteVersionNumber'] == 2
    wave = Image.open(PET / 'wave.png').convert('RGBA')
    assert wave.size == (768, 208) and hashlib.sha256(wave.tobytes()).hexdigest() == WAVE, 'Wave art changed'
    doze = Image.open(PET / 'doze.png').convert('RGBA')
    assert doze.size == (768, 208) and hashlib.sha256(doze.tobytes()).hexdigest() == DOZE, 'Doze art changed'
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
