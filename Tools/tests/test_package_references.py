"""A pinned package asset is distinct from an unknown or missing project asset."""
import json
from pathlib import Path
import tempfile
import unittest

from test_delivery_tools import fixture, guid
from validate_project import check_project, TMP_SETTINGS_GUID


class PackageReferenceChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        fixture(self.root)
        asset = self.root / "Assets/TMPSettings.asset"
        asset.write_text("MonoBehaviour:\n  m_Script: {fileID: 11500000, guid: " + TMP_SETTINGS_GUID + ", type: 3}\n")
        Path(str(asset) + ".meta").write_text("fileFormatVersion: 2\nguid: " + guid("TMPSettings") + "\nNativeFormatImporter:\n  externalObjects: {}\n")

    def tearDown(self):
        self.temp.cleanup()

    def pins(self, manifest_version="3.0.6", lock_version="3.0.6"):
        (self.root / "Packages/manifest.json").write_text(json.dumps({"dependencies": {"com.unity.textmeshpro": manifest_version}}))
        (self.root / "Packages/packages-lock.json").write_text(json.dumps({"dependencies": {"com.unity.textmeshpro": {"version": lock_version, "source": "registry"}}}))

    def test_external_settings_requires_declared_dependency(self):
        self.assertTrue(any("Unresolved GUID" in error for error in check_project(self.root)))

    def test_verified_exact_pin_allows_source_only_checkout(self):
        self.pins()
        self.assertEqual([], check_project(self.root))

    def test_different_lock_pin_cannot_silently_allow_asset(self):
        self.pins(lock_version="3.0.9")
        self.assertTrue(any("Unresolved GUID" in error for error in check_project(self.root)))

    def test_wrong_cached_script_meta_is_rejected(self):
        self.pins()
        meta = self.root / "Library/PackageCache/com.unity.textmeshpro@3.0.6/Scripts/Runtime/TMP_Settings.cs.meta"
        meta.parent.mkdir(parents=True)
        meta.write_text("guid: " + "1" * 32 + "\n")
        self.assertTrue(any("package metadata" in error for error in check_project(self.root)))

    def test_verified_pin_does_not_allow_unknown_other_guid(self):
        self.pins()
        asset = self.root / "Assets/TMPSettings.asset"
        asset.write_text(asset.read_text().replace(TMP_SETTINGS_GUID, "2" * 32))
        self.assertTrue(any("Unresolved GUID" in error for error in check_project(self.root)))


if __name__ == "__main__":
    unittest.main()
