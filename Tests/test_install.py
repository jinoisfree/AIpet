import importlib.util
from pathlib import Path
import plistlib
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('install', Path(__file__).resolve().parents[1] / 'scripts/install.py')
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


def app(path, identifier=m.BUNDLE_ID, automation=False, marker='old'):
    (path / 'Contents/MacOS').mkdir(parents=True)
    values = {'CFBundleIdentifier': identifier}
    if automation:
        values['NSAppleEventsUsageDescription'] = 'synthetic'
    with open(path / 'Contents/Info.plist', 'wb') as file:
        plistlib.dump(values, file)
    (path / 'Contents/MacOS/Taesik').write_text(marker)
    (path / 'Contents/MacOS/Taesik').chmod(0o755)
    return path


class InstallTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.user = Path(self.temp.name) / 'user'
        self.system = Path(self.temp.name) / 'system'
        self.user.mkdir(); self.system.mkdir()
        self.source = app(Path(self.temp.name) / 'dist/AIpet.app', marker='new')
        self.destination = self.user / m.NAME
        self.folders = [self.user, self.system]

    def run_install(self):
        with patch.object(m.subprocess, 'run'), patch.object(m, 'running', return_value=False):
            m.install(self.source, self.destination, self.folders)

    def test_fresh_install(self):
        self.run_install()
        self.assertEqual((self.destination / 'Contents/MacOS/Taesik').read_text(), 'new')
        self.assertTrue((self.destination / 'Contents/MacOS/Taesik').stat().st_mode & 0o111)

    def test_old_name_and_other_folder_copies_are_removed(self):
        legacy = app(self.user / m.LEGACY_NAME)
        other = app(self.system / m.NAME)
        self.run_install()
        self.assertFalse(legacy.exists()); self.assertFalse(other.exists())
        self.assertEqual(sorted(p.name for p in self.user.iterdir()), [m.NAME])

    def test_existing_install_is_replaced(self):
        app(self.destination)
        self.run_install()
        self.assertEqual((self.destination / 'Contents/MacOS/Taesik').read_text(), 'new')
        self.assertEqual(sorted(p.name for p in self.user.iterdir()), [m.NAME])

    def test_other_products_are_never_touched(self):
        different = app(self.user / m.LEGACY_NAME, automation=True)
        stranger = app(self.system / m.NAME, identifier='com.example.other')
        self.run_install()
        self.assertTrue(different.is_dir()); self.assertTrue(stranger.is_dir())

    def test_foreign_item_at_destination_stops_the_install(self):
        app(self.destination, identifier='com.example.other')
        with self.assertRaises(RuntimeError):
            self.run_install()
        self.assertEqual((self.destination / 'Contents/MacOS/Taesik').read_text(), 'old')
        self.assertFalse((self.user / 'AIpet.app.new').exists())

    def test_copy_that_will_not_quit_is_kept(self):
        app(self.destination)
        with patch.object(m.subprocess, 'run'), patch.object(m, 'running', return_value=True), patch.object(m, 'QUIT_WAIT', 0):
            with self.assertRaises(RuntimeError):
                m.quit_app(self.destination)
            with self.assertRaises(RuntimeError):
                m.install(self.source, self.destination, self.folders)
        self.assertEqual((self.destination / 'Contents/MacOS/Taesik').read_text(), 'old')
        self.assertFalse((self.user / 'AIpet.app.new').exists())

    def test_failed_verification_leaves_install_untouched(self):
        app(self.destination)
        with patch.object(m, 'verify', side_effect=[None, RuntimeError('bad copy')]), patch.object(m, 'running', return_value=False):
            with self.assertRaises(RuntimeError):
                m.install(self.source, self.destination, self.folders)
        self.assertEqual((self.destination / 'Contents/MacOS/Taesik').read_text(), 'old')
        self.assertFalse((self.user / 'AIpet.app.new').exists())


if __name__ == '__main__':
    unittest.main()
