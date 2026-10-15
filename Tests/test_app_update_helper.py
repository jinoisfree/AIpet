"""Native updater handoff using isolated signed apps; never starts the user's pet."""
from pathlib import Path
import plistlib
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
import uuid


ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / 'dist/AIpet.app'


@unittest.skipUnless(sys.platform == 'darwin' and APP.exists(), 'Build the Mac app first.')
class AppUpdateHelperTests(unittest.TestCase):
    def test_copied_helper_waits_replaces_relaunches_and_removes_old_app(self):
        source_info = plistlib.loads((ROOT / 'Resources/Info.plist').read_bytes())
        built_info = plistlib.loads((APP / 'Contents/Info.plist').read_bytes())
        if built_info != source_info:
            self.skipTest('Rebuild dist/AIpet.app from the current sources first.')
        with tempfile.TemporaryDirectory(prefix='aipet-helper-test-') as temporary:
            root = Path(temporary).resolve()
            destination = root / 'AIpet.app'
            shutil.copytree(APP, destination)
            work = root / ('.aipet-update-' + uuid.uuid4().hex)
            staged = work / 'AIpet.app'
            executable = staged / 'Contents/MacOS/Taesik'
            executable.parent.mkdir(parents=True)
            # The new app is a tiny signed fixture that only writes a local marker.
            # open -n must launch this exact app, even if a real AIpet is running.
            source = root / 'RelaunchFixture.swift'
            source.write_text('''import Foundation
let marker = Bundle.main.bundleURL.deletingLastPathComponent().appendingPathComponent("restarted.txt")
try Data("new fixture restarted".utf8).write(to: marker)
''')
            subprocess.run(['xcrun', 'swiftc', str(source), '-o', str(executable)], check=True, timeout=60)
            major, minor, patch = map(int, built_info['CFBundleShortVersionString'].split('.'))
            version = f'{major}.{minor}.{patch + 1}'
            fixture_info = {
                'CFBundleIdentifier': 'com.jinoisfree.taesik', 'CFBundleExecutable': 'Taesik',
                'CFBundleName': 'AIpet Update Test', 'CFBundlePackageType': 'APPL',
                'CFBundleShortVersionString': version, 'CFBundleVersion': '1',
                'LSBackgroundOnly': True,
            }
            (staged / 'Contents/Info.plist').write_bytes(plistlib.dumps(fixture_info))
            subprocess.run(['/usr/bin/codesign', '--force', '--sign', '-', str(staged)], check=True, capture_output=True, timeout=30)
            cache = Path.home() / 'Library/Caches/AIpet/Updates'
            download = cache / ('download-' + uuid.uuid4().hex)
            download.mkdir(parents=True)
            helper = download / 'AIpetUpdate'
            shutil.copy2(APP / 'Contents/MacOS/Taesik', helper)
            helper.chmod(0o700)
            settings = root / 'settings.json'
            settings.write_text('{"petName":"unchanged","petScale":125}')
            saved_settings = settings.read_bytes()
            parent = subprocess.Popen(['/bin/sleep', '2'])
            process = None
            try:
                process = subprocess.Popen([str(helper), '--install-update', str(parent.pid), str(work),
                                            str(destination), version, str(download)],
                                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                time.sleep(0.2)
                self.assertIsNone(process.poll(), 'Helper exited before the parent.')
                self.assertEqual(plistlib.loads((destination / 'Contents/Info.plist').read_bytes()), built_info)
                parent.wait(timeout=5)  # Reap the parent so kill(pid, 0) observes termination.
                self.assertEqual(process.wait(timeout=15), 0)
                deadline = time.monotonic() + 10
                marker = root / 'restarted.txt'
                while not marker.exists() and time.monotonic() < deadline:
                    time.sleep(0.1)
                self.assertEqual(marker.read_text(), 'new fixture restarted')
                self.assertEqual(plistlib.loads((destination / 'Contents/Info.plist').read_bytes()), fixture_info)
                self.assertFalse(work.exists(), 'Successful update must remove the backup/staging directory.')
                self.assertFalse(download.exists(), 'Successful update must remove the helper/download directory.')
                self.assertEqual(settings.read_bytes(), saved_settings)
            finally:
                for child in (process, parent):
                    if child is not None and child.poll() is None:
                        child.kill()
                        child.wait(timeout=5)
                shutil.rmtree(download, ignore_errors=True)
                # Remove only this temporary fixture's Launch Services registration.
                subprocess.run(['/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister',
                                '-u', str(destination)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=10)


if __name__ == '__main__':
    unittest.main()
