#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BUILD="$ROOT/.build"
STAGING=$(mktemp -d /private/tmp/taesik-build.XXXXXX)
trap 'rm -rf "$STAGING"' EXIT
APP="$STAGING/AIpet.app"
OUTPUT="$ROOT/dist/AIpet.app"
mkdir -p "$BUILD/module-cache" "$APP/Contents/MacOS" "$APP/Contents/Resources/Pet"
python3 "$ROOT/scripts/prepare_assets.py"
xcrun swiftc -swift-version 5 -O -parse-as-library -module-cache-path "$BUILD/module-cache" -target arm64-apple-macosx13.0 -framework AppKit -framework SwiftUI "$ROOT"/Sources/*.swift -o "$BUILD/Taesik"
cp "$BUILD/Taesik" "$APP/Contents/MacOS/Taesik"
cp "$ROOT/Resources/Info.plist" "$APP/Contents/Info.plist"
cp "$ROOT/Resources/Pet/"* "$APP/Contents/Resources/Pet/"
cp "$BUILD/assets/spritesheet.png" "$APP/Contents/Resources/Pet/"
cp "$BUILD/assets/Taesik.icns" "$APP/Contents/Resources/Taesik.icns"
if [ -f "$ROOT/Resources/연결안내.html" ]; then cp "$ROOT/Resources/연결안내.html" "$APP/Contents/Resources/"; fi
codesign --force --sign - "$APP"
codesign --verify --deep --strict "$APP"
mkdir -p "$ROOT/dist"
ditto --norsrc --noextattr --noqtn "$APP" "$OUTPUT"
printf '빌드·임시 폴더 서명 검증 완료: %s\n' "$OUTPUT"
