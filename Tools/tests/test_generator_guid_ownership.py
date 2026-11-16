"""The asset generator must preserve authored extensions and reject broken scene bindings."""
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class GeneratorGuidOwnershipTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        shutil.copytree(ROOT / 'Assets', self.root / 'Assets')
        destination = self.root / 'docs/Game02_ClawMachine'
        destination.mkdir(parents=True)
        for name in ('generate_prototype.py', 'GeneratedAssets.json'):
            shutil.copy2(ROOT / 'docs/Game02_ClawMachine' / name, destination / name)

    def tearDown(self):
        self.temp.cleanup()

    def check_generator(self):
        return subprocess.run([sys.executable, '-B', str(self.root / 'docs/Game02_ClawMachine/generate_prototype.py'), '--check'],
                              capture_output=True, text=True, encoding='utf-8')

    def test_hand_authored_extension_keeps_its_existing_guid(self):
        source = self.root / 'Assets/ClawMachine/Scripts/Core/AuthorExtension.cs'
        source.write_text('// fixture\n', encoding='utf-8', newline='\n')
        meta = Path(str(source) + '.meta')
        content = 'fileFormatVersion: 2\nguid: 1234567890abcdef1234567890abcdef\nMonoImporter:\n  externalObjects: {}\n'
        meta.write_text(content, encoding='utf-8', newline='\n')
        result = self.check_generator()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual(content, meta.read_text(encoding='utf-8'))

    def test_scene_referenced_script_guid_change_is_rejected_without_overwrite(self):
        meta = self.root / 'Assets/ClawMachine/Scripts/Core/ClawGameManager.cs.meta'
        content = 'fileFormatVersion: 2\nguid: ffffffffffffffffffffffffffffffff\nMonoImporter:\n  externalObjects: {}\n'
        meta.write_text(content, encoding='utf-8', newline='\n')
        result = self.check_generator()
        self.assertNotEqual(0, result.returncode)
        self.assertIn('Unexpected existing GUID', result.stdout + result.stderr)
        self.assertEqual(content, meta.read_text(encoding='utf-8'))


if __name__ == '__main__': unittest.main()
