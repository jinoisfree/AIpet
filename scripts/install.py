#!/usr/bin/env python3
"""Install or replace AIpet. Once the new copy is in place, older copies and their backups are removed."""
import argparse
from pathlib import Path
import plistlib
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
NAME = 'AIpet.app'
LEGACY_NAME = 'AIpet.v1.app'
BUNDLE_ID = 'com.jinoisfree.taesik'
EXECUTABLE = 'Taesik'
QUIT_WAIT = 15


def read_plist(path):
    try:
        with open(path, 'rb') as file:
            return plistlib.load(file)
    except Exception:
        return {}


def info(app):
    return read_plist(app / 'Contents/Info.plist')


def is_aipet(app):
    """Only this app's own bundles count. A bundle that asks for Messages automation is a different product and is left alone."""
    values = info(app)
    return (app.is_dir() and not app.is_symlink() and values.get('CFBundleIdentifier') == BUNDLE_ID
            and values.get('CFBundleExecutable') == EXECUTABLE and 'NSAppleEventsUsageDescription' not in values)


def version(app):
    return str(info(app).get('CFBundleShortVersionString', ''))


def version_key(text):
    return tuple(int(part) if part.isdigit() else 0 for part in text.split('.'))


def is_backup(app):
    return app.name not in (NAME, LEGACY_NAME)


def scan(folders, destination):
    """Bundles under this app's current or previous name, and their backups, split into this app's copies and everything else."""
    copies, others = [], []
    for folder in folders:
        try:
            entries = sorted(folder.iterdir())
        except OSError:
            continue
        for app in entries:
            named = any(app.name == base or app.name.startswith(base + '.bak-') for base in (NAME, LEGACY_NAME))
            if named and app.resolve() != destination.resolve():
                (copies if is_aipet(app) else others).append(app)
    return copies, others


def executable(app):
    return str(app / 'Contents/MacOS' / EXECUTABLE)


def running(app):
    return subprocess.run(['/usr/bin/pgrep', '-f', executable(app)], capture_output=True).returncode == 0


def quit_app(app):
    """Ask the pet to end and wait; the files are not touched while it is still running."""
    if not running(app):
        return
    subprocess.run(['/usr/bin/pkill', '-TERM', '-f', executable(app)], capture_output=True)
    deadline = time.time() + QUIT_WAIT
    while running(app) and time.time() < deadline:
        time.sleep(0.1)
    if running(app):
        raise RuntimeError(f'실행 중인 이전 버전을 종료하지 못해 그대로 둡니다: {app}')


def verify(app):
    subprocess.run(['/usr/bin/codesign', '--verify', '--deep', '--strict', str(app)], check=True)
    assert (app / 'Contents/MacOS' / EXECUTABLE).stat().st_mode & 0o111


def stale_hooks(removed, home):
    """Hook settings that still point at a removed copy. They are reported, never edited."""
    found = []
    for path in (home / '.claude/settings.json', home / '.codex/hooks.json'):
        try:
            text = path.read_text()
        except (OSError, UnicodeDecodeError):
            continue
        found += [(path, old) for old in removed if old in text]
    return found


def install(source, destination, folders, force=False, launch=True, home=None):
    verify(source)
    replacing = destination.exists() or destination.is_symlink()
    if replacing and not is_aipet(destination):
        raise RuntimeError(f'AIpet이 아닌 항목이 있어 중단합니다: {destination}')
    old, others = scan(folders, destination)
    for app in others:
        print(f'다른 제품으로 보고 건드리지 않음: {app}')
    # Backups and other products never decide which version is current.
    current = ([destination] if replacing else []) + [app for app in old if not is_backup(app)]
    before = max((version(app) for app in current), key=version_key, default='')
    after = version(source)
    if before and version_key(after) < version_key(before) and not force:
        raise RuntimeError(f'설치된 {before}보다 낮은 {after}입니다. 그래도 설치하려면 --force를 지정하세요.')
    live = ([destination] if replacing else []) + old
    was_running = any(running(app) for app in live)
    removed = [executable(app) for app in old]
    destination.parent.mkdir(parents=True, exist_ok=True)
    work = Path(tempfile.mkdtemp(prefix='.aipet-install-', dir=destination.parent))
    try:
        # The new copy is complete and verified before any installed copy is quit, moved or removed.
        staged = work / NAME
        shutil.copytree(source, staged, copy_function=shutil.copy)
        verify(staged)
        for app in live:
            quit_app(app)
        previous = work / 'previous.app'
        if replacing:
            destination.rename(previous)
        try:
            staged.rename(destination)
        except Exception:
            if replacing:
                previous.rename(destination)
            raise
    finally:
        shutil.rmtree(work, ignore_errors=True)
    print(f'{"교체" if replacing else "설치"} 완료: {destination} ({before + " → " if before else ""}{after})')
    kept = []
    for app in old:
        try:
            shutil.rmtree(app)
            print(f'이전 버전 삭제: {app}')
        except OSError as error:
            kept.append(f'{app} ({error})')
    for path, old_executable in stale_hooks(removed, home or Path.home()):
        print(f'훅 경로를 고쳐야 합니다: {path}\n  {old_executable}\n  → {executable(destination)}')
    if launch and was_running:
        subprocess.run(['/usr/bin/open', str(destination)])
    if kept:
        raise RuntimeError('이전 버전을 삭제하지 못했습니다: ' + ', '.join(kept))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--destination', type=Path, default=Path.home() / 'Applications' / NAME)
    parser.add_argument('--source', type=Path, default=ROOT / 'dist' / NAME)
    parser.add_argument('--force', action='store_true', help='빌드와 소스의 버전이 다르거나 설치본보다 낮아도 설치')
    parser.add_argument('--no-launch', action='store_true', help='실행 중이던 펫을 교체 후 다시 열지 않음')
    args = parser.parse_args()
    expected = str(read_plist(ROOT / 'Resources/Info.plist').get('CFBundleShortVersionString', ''))
    built = version(args.source)
    if built != expected and not args.force:
        sys.exit(f'빌드된 앱은 {built or "버전 없음"}, 소스는 {expected}입니다. scripts/build.sh를 다시 실행하세요.')
    try:
        install(args.source, args.destination, [Path.home() / 'Applications', Path('/Applications')],
                force=args.force, launch=not args.no_launch)
    except RuntimeError as error:
        sys.exit(str(error))


if __name__ == '__main__':
    main()
