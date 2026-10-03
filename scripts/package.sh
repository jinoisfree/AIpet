#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="$ROOT/dist/AIpet.app"
VERSION=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$APP/Contents/Info.plist")
ARCHIVE="$ROOT/dist/AIpet-$VERSION-arm64.zip"
if [ -e "$ARCHIVE" ]; then
    printf '기존 패키지를 보존합니다: %s\n' "$ARCHIVE" >&2
    exit 1
fi
python3 - "$APP" "$ARCHIVE" <<'PY'
from pathlib import Path
import shutil, subprocess, sys, tempfile
with tempfile.TemporaryDirectory(prefix='taesik-package-', dir='/private/tmp') as temporary:
    clean = Path(temporary) / 'AIpet.app'
    shutil.copytree(sys.argv[1], clean, copy_function=shutil.copy)
    subprocess.run(['/usr/bin/codesign', '--verify', '--deep', '--strict', str(clean)], check=True)
    subprocess.run(['/usr/bin/ditto', '-c', '-k', '--keepParent', '--norsrc', '--noextattr', '--noqtn', str(clean), sys.argv[2]], check=True)
    extract = Path(temporary) / 'verify'
    subprocess.run(['/usr/bin/ditto', '-x', '-k', sys.argv[2], str(extract)], check=True)
    app = extract / 'AIpet.app'
    subprocess.run(['/usr/bin/codesign', '--verify', '--deep', '--strict', str(app)], check=True)
    assert (app / 'Contents/MacOS/Taesik').stat().st_mode & 0o111
    print('압축 해제 후 서명·실행 권한 검증: 통과')
PY
shasum -a 256 "$ARCHIVE"
