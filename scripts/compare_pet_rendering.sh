#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/.build/pet-sharp-compare"
mkdir -p "$OUT/Pet" "$ROOT/.build/module-cache"
python3 "$ROOT/scripts/prepare_assets.py"
cp "$ROOT/.build/assets/spritesheet.png" "$ROOT/Resources/Pet/wave.png" "$ROOT/Resources/Pet/doze.png" "$OUT/Pet/"
xcrun swiftc -swift-version 5 -O -module-cache-path "$ROOT/.build/module-cache" \
 "$ROOT/Sources/Models.swift" "$ROOT/Sources/ScreenFocus.swift" "$ROOT/Sources/PetAnimation.swift" \
 "$ROOT/Sources/PetLayout.swift" "$ROOT/Sources/PetSpritePresentation.swift" \
 "$ROOT/Sources/PetSpriteContour.swift" "$ROOT/Sources/PetSpriteRasterizer.swift" "$ROOT/Sources/SpriteAtlas.swift" "$ROOT/Tests/LargeSpriteRenderingTests.swift" "$ROOT/Tests/SpriteRenderingTests.swift" \
 -o "$OUT/SpriteRenderingTests"
"$OUT/SpriteRenderingTests" "$OUT/Pet" "$OUT"
