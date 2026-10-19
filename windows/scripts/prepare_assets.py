#!/usr/bin/env python3
"""Validate original assets and produce lossless Windows resources."""
import hashlib
import json
import sys
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
PET = ROOT / 'Resources' / 'Pet'
OUTPUT = ROOT / 'windows/src/AIpet.App/Assets/Generated'
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
    assert json.loads((PET / 'pet.json').read_text(encoding='utf-8'))['spriteVersionNumber'] == 2
    for name, checksum in [('wave', WAVE), ('doze', DOZE)]:
        image = Image.open(PET / (name + '.png')).convert('RGBA')
        assert image.size == (768, 208) and hashlib.sha256(image.tobytes()).hexdigest() == checksum, name + ' art changed'
    approval = Image.open(PET / 'approval-phone.png').convert('RGBA')
    assert approval.size == (1536, 208) and hashlib.sha256(approval.tobytes()).hexdigest() == APPROVAL, 'Approval phone art changed'
    atlas = Image.open(source).convert('RGBA')
    assert atlas.size == (1536, 2288), 'Atlas size changed'
    # The wave starts at idle and keeps the idle body below the raised arm.
    idle = atlas.crop((0, 0, 192, 208))
    wave = Image.open(PET / 'wave.png').convert('RGBA')
    assert wave.crop((0, 0, 192, 208)).tobytes() == idle.tobytes(), 'Wave starts from the idle pose'
    for column in range(1, 4):
        assert wave.crop((column*192, 111, column*192 + 192, 208)).tobytes() == idle.crop((0, 111, 192, 208)).tobytes(), 'Wave keeps the idle body'
    # The phone shake also keeps the idle body below the arms.
    for column in range(8):
        assert approval.crop((column*192, 110, column*192 + 192, 208)).tobytes() == idle.crop((0, 110, 192, 208)).tobytes(), 'Phone shake keeps the idle body'
    OUTPUT.mkdir(parents=True, exist_ok=True)
    atlas.save(OUTPUT / 'spritesheet.png')
    assert Image.open(OUTPUT / 'spritesheet.png').tobytes() == atlas.tobytes(), 'PNG pixels changed'
    cat = atlas.crop((0, 0, 192, 208))
    canvas = Image.new('RGBA', (256, 256))
    frame = cat.resize((round(256 * 192 / 208), 256), Image.Resampling.LANCZOS)
    canvas.alpha_composite(frame, ((256 - frame.width) // 2, 0))
    sizes = [(n, n) for n in (16, 24, 32, 48, 64, 128, 256)]
    canvas.save(OUTPUT / 'AIpet.ico', format='ICO', sizes=sizes)
    assert Image.open(OUTPUT / 'AIpet.ico').ico.sizes() == set(sizes)
    print('원본 체크섬·크기·PNG 픽셀 일치·ICO 7개 크기: 통과')
    for name in ('spritesheet.png', 'AIpet.ico'):
        target = OUTPUT / name
        print(f'{target.relative_to(ROOT)}: {target.stat().st_size} bytes, SHA256 {hashlib.sha256(target.read_bytes()).hexdigest()}')

if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    prepare()
