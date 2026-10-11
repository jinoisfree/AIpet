#!/usr/bin/env python3
"""Validate original assets and produce lossless Windows resources."""
import hashlib
import json
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
PET = ROOT / 'Resources' / 'Pet'
OUTPUT = ROOT / 'windows/src/AIpet.App/Assets/Generated'
EXPECTED = '316b98b0445cdf0ac50dda91128e5772366f49787a4634addbfb1409c99dca35'
WAVE = 'b0beffe97a9e093f92a56009ec341c924b9e4e49c36f8ff295e0ef4a7a9ea1ba'
DOZE = '9e5073f5811656ebdaba62eaedc6d060006092cfca4ca8239f401575f0a2bf99'

def prepare():
    source = PET / 'spritesheet.webp'
    assert hashlib.sha256(source.read_bytes()).hexdigest() == EXPECTED, 'Upstream asset hash changed'
    assert json.loads((PET / 'pet.json').read_text())['spriteVersionNumber'] == 2
    for name, checksum in [('wave', WAVE), ('doze', DOZE)]:
        image = Image.open(PET / (name + '.png')).convert('RGBA')
        assert image.size == (768, 208) and hashlib.sha256(image.tobytes()).hexdigest() == checksum, name + ' art changed'
    atlas = Image.open(source).convert('RGBA')
    assert atlas.size == (1536, 2288), 'Atlas size changed'
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
    prepare()
