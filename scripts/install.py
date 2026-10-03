#!/usr/bin/env python3
"""Install or replace AIpet. Older copies under the previous name or in the other Applications folder are removed."""
import argparse
from pathlib import Path
import plistlib
import shutil
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]
NAME = 'AIpet.app'
LEGACY_NAME = 'AIpet.v1.app'
BUNDLE_ID = 'com.jinoisfree.taesik'
QUIT_WAIT = 15


def info(app):
    try:
        with open(app / 'Contents/Info.plist', 'rb') as file:
            return plistlib.load(file)
    except Exception:
        return {}


def is_aipet(app):
    """Only this app's own bundles count. A bundle that asks for Messages automation is a different product and is left alone."""
    values = info(app)
    return (app.is_dir() and not app.is_symlink() and values.get('CFBundleIdentifier') == BUNDLE_ID
            and 'NSAppleEventsUsageDescription' not in values)


def outdated(folders, destination):
    found = []
    for folder in folders:
        for name in (LEGACY_NAME, NAME):
            app = folder / name
            if app.resolve() != destination.resolve() and is_aipet(app):
                found.append(app)
    return found


def running(app):
    return subprocess.run(['/usr/bin/pgrep', '-f', str(app / 'Contents/MacOS/Taesik')], capture_output=True).returncode == 0


def quit_app(app):
    """Ask the pet to end and wait; the files are not touched while it is still running."""
    if not running(app):
        return
    subprocess.run(['/usr/bin/pkill', '-TERM', '-f', str(app / 'Contents/MacOS/Taesik')], capture_output=True)
    deadline = time.time() + QUIT_WAIT
    while running(app) and time.time() < deadline:
        time.sleep(0.1)
    if running(app):
        raise RuntimeError(f'실행 중인 이전 버전을 종료하지 못해 그대로 둡니다: {app}')


def verify(app):
    subprocess.run(['/usr/bin/codesign', '--verify', '--deep', '--strict', str(app)], check=True)
    assert (app / 'Contents/MacOS/Taesik').stat().st_mode & 0o111


def install(source, destination, folders):
    verify(source)
    for old in outdated(folders, destination):
        quit_app(old)
        shutil.rmtree(old)
        print(f'이전 버전 삭제: {old}')
    replacing = destination.exists() or destination.is_symlink()
    if replacing and not is_aipet(destination):
        raise RuntimeError(f'AIpet이 아닌 항목이 있어 중단합니다: {destination}')
    # The new copy is complete and verified before the installed one is removed.
    staged = destination.with_name(destination.name + '.new')
    if staged.exists():
        shutil.rmtree(staged)
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(source, staged, copy_function=shutil.copy)
    try:
        verify(staged)
        if replacing:
            quit_app(destination)
            shutil.rmtree(destination)
        staged.rename(destination)
    except Exception:
        shutil.rmtree(staged, ignore_errors=True)
        raise
    print(f'{"교체" if replacing else "설치"} 완료: {destination}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--destination', type=Path, default=Path.home() / 'Applications' / NAME)
    parser.add_argument('--source', type=Path, default=ROOT / 'dist' / NAME)
    args = parser.parse_args()
    install(args.source, args.destination, [Path.home() / 'Applications', Path('/Applications')])


if __name__ == '__main__':
    main()
