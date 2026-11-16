import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('street_validator', ROOT/'docs/Game03_StreetCrossing/tools/validate_street.py')
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)

class StreetAuditBootstrapTests(unittest.TestCase):
    def setUp(self):
        self.path = validator.GAME/'Scripts/Validation/StreetRuntimeAudit.cs'
        self.source = self.path.read_text(encoding='utf-8')

    def test_only_the_exact_opt_in_after_scene_load_entry_is_allowed(self):
        self.assertFalse(validator.disallowed_global_bootstrap(self.path, self.source))
        self.assertTrue(validator.disallowed_global_bootstrap(self.path.with_name('OtherAudit.cs'), self.source))
        self.assertTrue(validator.disallowed_global_bootstrap(self.path, self.source.replace('AfterSceneLoad', 'BeforeSceneLoad')))
        self.assertTrue(validator.disallowed_global_bootstrap(self.path, self.source + '\n[RuntimeInitializeOnLoadMethod]'))
        self.assertTrue(validator.disallowed_global_bootstrap(self.path, self.source.replace('GameRuntimeAudit.TryStart(new StreetRuntimeAudit());', 'new GameObject("Unexpected");')))

    def test_ordinary_runtime_sources_remain_free_of_global_bootstraps(self):
        for path in (validator.GAME/'Scripts').rglob('*.cs'):
            with self.subTest(path=path):
                self.assertFalse(validator.disallowed_global_bootstrap(path, path.read_text(encoding='utf-8')))

if __name__ == '__main__':
    unittest.main()
