import contextlib
import importlib.util
import io
from pathlib import Path
import plistlib
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('install', Path(__file__).resolve().parents[1] / 'scripts/install.py')
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


def app(path, identifier=m.BUNDLE_ID, automation=False, marker='old', version='1.0.3', executable=m.EXECUTABLE):
    (path / 'Contents/MacOS').mkdir(parents=True)
    values = {'CFBundleIdentifier': identifier, 'CFBundleExecutable': executable, 'CFBundleShortVersionString': version}
    if automation:
        values['NSAppleEventsUsageDescription'] = 'synthetic'
    with open(path / 'Contents/Info.plist', 'wb') as file:
        plistlib.dump(values, file)
    (path / 'Contents/MacOS/Taesik').write_text(marker)
    (path / 'Contents/MacOS/Taesik').chmod(0o755)
    return path


def marker(path):
    return (path / 'Contents/MacOS/Taesik').read_text()


class InstallTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.home = Path(self.temp.name)
        self.user = self.home / 'user'
        self.system = self.home / 'system'
        self.user.mkdir(); self.system.mkdir()
        self.source = app(self.home / 'dist/AIpet.app', marker='new', version='1.0.4')
        self.destination = self.user / m.NAME
        self.folders = [self.user, self.system]

    def run_install(self, **options):
        output = io.StringIO()
        with patch.object(m.subprocess, 'run') as run, patch.object(m, 'running', return_value=False), \
                contextlib.redirect_stdout(output):
            m.install(self.source, self.destination, self.folders, home=self.home, **options)
        self.run_calls = [call.args[0] for call in run.call_args_list]
        return output.getvalue()

    def names(self, folder):
        return sorted(p.name for p in folder.iterdir())

    def test_fresh_install(self):
        self.run_install()
        self.assertEqual(marker(self.destination), 'new')
        self.assertTrue((self.destination / 'Contents/MacOS/Taesik').stat().st_mode & 0o111)
        self.assertEqual(self.names(self.user), [m.NAME])

    def test_old_name_and_other_folder_copies_are_removed(self):
        legacy = app(self.user / m.LEGACY_NAME)
        other = app(self.system / m.NAME)
        self.run_install()
        self.assertFalse(legacy.exists()); self.assertFalse(other.exists())
        self.assertEqual(self.names(self.user), [m.NAME])

    def test_existing_install_is_replaced(self):
        app(self.destination)
        self.assertIn('1.0.3 → 1.0.4', self.run_install())
        self.assertEqual(marker(self.destination), 'new')
        self.assertEqual(self.names(self.user), [m.NAME])

    def test_backups_of_this_app_are_removed(self):
        app(self.destination)
        app(self.user / 'AIpet.v1.app.bak-20261004-004255')
        app(self.system / 'AIpet.app.bak-1')
        self.run_install()
        self.assertEqual(self.names(self.user), [m.NAME]); self.assertEqual(self.names(self.system), [])

    def test_other_products_are_never_touched(self):
        kept = [app(self.user / m.LEGACY_NAME, automation=True, version='1.1.0'),
                app(self.user / 'AIpet.v1.app.bak-20261004-004255', automation=True, version='1.1.0'),
                app(self.system / m.NAME, identifier='com.example.other'),
                app(self.system / m.LEGACY_NAME, executable='Other'),
                app(self.user / 'AIpet-Other.app')]
        output = self.run_install()
        for path in kept:
            self.assertEqual(marker(path), 'old')
        # A name outside this app's own is not even looked at; the rest are named in the report.
        self.assertEqual(output.count('건드리지 않음'), 4)
        self.assertEqual(marker(self.destination), 'new')

    def test_foreign_item_at_destination_stops_before_anything_is_removed(self):
        legacy = app(self.user / m.LEGACY_NAME)
        for foreign in ({'identifier': 'com.example.other'}, {'automation': True}):
            app(self.destination, **foreign)
            with self.assertRaises(RuntimeError):
                self.run_install()
            self.assertEqual(marker(self.destination), 'old'); self.assertTrue(legacy.is_dir())
            self.assertEqual(self.names(self.user), [m.NAME, m.LEGACY_NAME])
            m.shutil.rmtree(self.destination)

    def test_failed_copy_keeps_the_previous_version(self):
        legacy = app(self.user / m.LEGACY_NAME)
        with patch.object(m.shutil, 'copytree', side_effect=OSError('disk full')), self.assertRaises(OSError):
            self.run_install()
        self.assertEqual(marker(legacy), 'old')
        self.assertEqual(self.names(self.user), [m.LEGACY_NAME])

    def test_copy_that_will_not_quit_is_kept(self):
        app(self.destination)
        legacy = app(self.system / m.LEGACY_NAME)
        with patch.object(m.subprocess, 'run'), patch.object(m, 'running', return_value=True), patch.object(m, 'QUIT_WAIT', 0):
            with self.assertRaises(RuntimeError):
                m.quit_app(self.destination)
            with self.assertRaises(RuntimeError):
                m.install(self.source, self.destination, self.folders, home=self.home)
        self.assertEqual(marker(self.destination), 'old'); self.assertTrue(legacy.is_dir())
        self.assertEqual(self.names(self.user), [m.NAME])

    def test_failed_verification_leaves_install_untouched(self):
        app(self.destination)
        legacy = app(self.user / m.LEGACY_NAME)
        with patch.object(m, 'verify', side_effect=[None, RuntimeError('bad copy')]), patch.object(m, 'running', return_value=False):
            with self.assertRaises(RuntimeError):
                m.install(self.source, self.destination, self.folders, home=self.home)
        self.assertEqual(marker(self.destination), 'old'); self.assertTrue(legacy.is_dir())
        self.assertEqual(self.names(self.user), [m.NAME, m.LEGACY_NAME])

    def test_failed_swap_puts_the_installed_copy_back(self):
        app(self.destination)
        rename = Path.rename

        def failing(path, target):
            if path.name == m.NAME and path.parent != self.user:
                raise OSError('swap failed')
            return rename(path, target)

        with patch.object(m.Path, 'rename', failing), self.assertRaises(OSError):
            self.run_install()
        self.assertEqual(marker(self.destination), 'old')
        self.assertEqual(self.names(self.user), [m.NAME])

    def test_lower_version_needs_force(self):
        app(self.destination, version='1.0.10')
        with self.assertRaises(RuntimeError):
            self.run_install()
        self.assertEqual(marker(self.destination), 'old')
        self.run_install(force=True)
        self.assertEqual(marker(self.destination), 'new')

    def test_other_products_do_not_decide_the_version(self):
        app(self.user / m.LEGACY_NAME, automation=True, version='1.2.1')
        app(self.user / 'AIpet.app.bak-1', version='9.0')
        self.run_install()
        self.assertEqual(marker(self.destination), 'new')

    def test_running_pet_is_reopened(self):
        app(self.destination)
        self.run_install()
        self.assertNotIn(['/usr/bin/open', str(self.destination)], self.run_calls)
        for launch, expected in ((True, True), (False, False)):
            with patch.object(m.subprocess, 'run') as run, patch.object(m, 'running', return_value=True), \
                    patch.object(m, 'quit_app'), contextlib.redirect_stdout(io.StringIO()):
                m.install(self.source, self.destination, self.folders, home=self.home, launch=launch)
            self.assertEqual(['/usr/bin/open', str(self.destination)] in [c.args[0] for c in run.call_args_list], expected)

    def test_hooks_pointing_at_a_removed_copy_are_reported_not_edited(self):
        legacy = app(self.user / m.LEGACY_NAME)
        other = app(self.user / m.LEGACY_NAME.replace('.app', '.app.bak-1'), automation=True)
        (self.home / '.claude').mkdir()
        settings = self.home / '.claude/settings.json'
        text = f'{{"hooks": ["{m.executable(legacy)} --hook claude", "{m.executable(other)} --hook claude"]}}'
        settings.write_text(text)
        output = self.run_install()
        self.assertEqual(output.count('훅 경로를 고쳐야 합니다'), 1)
        self.assertIn(m.executable(legacy), output); self.assertIn(m.executable(self.destination), output)
        self.assertEqual(settings.read_text(), text)


if __name__ == '__main__':
    unittest.main()
