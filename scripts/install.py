#!/usr/bin/env python3
"""Install a new copy only; preserve executable modes and refuse replacement."""
import argparse
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--destination', type=Path, default=Path.home() / 'Applications/AIpet.v1.app')
args = parser.parse_args()
source = ROOT / 'dist/AIpet.v1.app'
if args.destination.exists() or args.destination.is_symlink():
    parser.error(f'기존 설치본 보존: {args.destination}')
subprocess.run(['/usr/bin/codesign', '--verify', '--deep', '--strict', str(source)], check=True)
args.destination.parent.mkdir(parents=True, exist_ok=True)
shutil.copytree(source, args.destination, copy_function=shutil.copy)
subprocess.run(['/usr/bin/codesign', '--verify', '--deep', '--strict', str(args.destination)], check=True)
assert (args.destination / 'Contents/MacOS/Taesik').stat().st_mode & 0o111
print(f'설치 완료: {args.destination}')
