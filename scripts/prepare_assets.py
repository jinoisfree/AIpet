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
# The idle pose with its right paw lifted, drawn by draw_wave.py and pinned by its pixels.
WAVE = '69aaf32cb6f7a0840c562f1aebac7d47e90bc10cd6ef63f654656c18158b0d73'
# The idle pose asleep, nodding with its mouth open and drooling, drawn by draw_doze.py.
DOZE = '966271ce8e97c979b5f14fa49616e0edb498ebf3580504a61791a51893de61a1'
# The idle pose with a sulky face, shaking its phone at the wrist.
APPROVAL = 'e44747393e6313fd91fd40aa50ea5465a1ff1b3eb01492cc957be105356bcb57'

def prepare():
    source = PET / 'spritesheet.webp'
    assert hashlib.sha256(source.read_bytes()).hexdigest() == EXPECTED, 'Upstream asset hash changed'
    assert json.loads((PET / 'pet.json').read_text())['spriteVersionNumber'] == 2
    wave = Image.open(PET / 'wave.png').convert('RGBA')
    assert wave.size == (768, 208) and hashlib.sha256(wave.tobytes()).hexdigest() == WAVE, 'Wave art changed'
    doze = Image.open(PET / 'doze.png').convert('RGBA')
    assert doze.size == (768, 208) and hashlib.sha256(doze.tobytes()).hexdigest() == DOZE, 'Doze art changed'
    approval = Image.open(PET / 'approval-phone.png').convert('RGBA')
    assert approval.size == (1536, 208) and hashlib.sha256(approval.tobytes()).hexdigest() == APPROVAL, 'Approval phone art changed'
    atlas = Image.open(source).convert('RGBA')
    assert atlas.size == (1536, 2288)
    # The wave starts at idle and keeps the idle body below the raised arm.
    idle = atlas.crop((0, 0, 192, 208))
    assert wave.crop((0, 0, 192, 208)).tobytes() == idle.tobytes(), 'Wave starts from the idle pose'
    for column in range(1, 4):
        assert wave.crop((column*192, 111, column*192 + 192, 208)).tobytes() == idle.crop((0, 111, 192, 208)).tobytes(), 'Wave keeps the idle body'
    # The phone shake also keeps the idle body below the arms.
    for column in range(8):
        assert approval.crop((column*192, 110, column*192 + 192, 208)).tobytes() == idle.crop((0, 110, 192, 208)).tobytes(), 'Phone shake keeps the idle body'
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
