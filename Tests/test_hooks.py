import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
BIN = ROOT / 'dist/AIpet.v1.app/Contents/MacOS/Taesik'
SCRIPT = ROOT / 'scripts/configure_hooks.py'

class HookTests(unittest.TestCase):
    def test_hook_no_decision_no_private_content(self):
        with tempfile.TemporaryDirectory(prefix='taesik-hook-') as temporary:
            data = {'session_id': '../../escape', 'hook_event_name': 'PermissionRequest',
                    'cwd': '/work/test-project', 'tool_input': {'command': 'PRIVATE_SECRET'}}
            result = subprocess.run([str(BIN), '--hook', 'claude', '--event-root', temporary],
                                    input=json.dumps(data), text=True, capture_output=True, timeout=3)
            self.assertEqual((result.returncode, result.stdout, result.stderr), (0, '', ''))
            files = list(Path(temporary).glob('*.json'))
            self.assertEqual(len(files), 1)
            output = files[0].read_text()
            self.assertNotIn('PRIVATE_SECRET', output)
            self.assertEqual(json.loads(output)['state'], 'waiting')
            self.assertEqual(files[0].stat().st_mode & 0o777, 0o600)

    def test_malformed_input_is_silent(self):
        result = subprocess.run([str(BIN), '--hook', 'codex'], input='{broken', text=True, capture_output=True, timeout=3)
        self.assertEqual((result.returncode, result.stdout, result.stderr), (0, '', ''))

    def test_preview_and_create_only_and_idempotency(self):
        with tempfile.TemporaryDirectory(prefix='taesik-config-') as temporary:
            command = ['python3', str(SCRIPT), '--app', str(ROOT / 'dist/AIpet.v1.app'), '--home', temporary]
            preview = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(preview.returncode, 0)
            self.assertFalse((Path(temporary) / '.codex/hooks.json').exists())
            first = subprocess.run(command + ['--apply'], capture_output=True, text=True)
            self.assertEqual(first.returncode, 0)
            target = Path(temporary) / '.claude/settings.json'
            before = target.read_bytes()
            second = subprocess.run(command + ['--apply'], capture_output=True, text=True)
            self.assertEqual(second.returncode, 0)
            self.assertEqual(before, target.read_bytes())
            target.write_text('{"model":"existing"}')
            blocked = subprocess.run(command + ['--apply'], capture_output=True, text=True)
            self.assertNotEqual(blocked.returncode, 0)
            self.assertEqual(target.read_text(), '{"model":"existing"}')

    def test_settings_conflict_causes_no_partial_write(self):
        with tempfile.TemporaryDirectory(prefix='taesik-conflict-') as temporary:
            target = Path(temporary) / '.claude/settings.json'
            target.parent.mkdir()
            target.write_text('{"hooks": {"Stop": []}}')
            result = subprocess.run(['python3', str(SCRIPT), '--app', str(ROOT / 'dist/AIpet.v1.app'),
                                     '--home', temporary, '--apply'], capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse((Path(temporary) / '.codex/hooks.json').exists())

if __name__ == '__main__':
    unittest.main()
