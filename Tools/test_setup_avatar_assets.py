from __future__ import annotations

from pathlib import Path
import tempfile
import unittest
import zipfile

from Tools.setup_avatar_assets import ArchiveSafetyError, list_asset_files, safe_extract_zip


class AvatarArchiveFixtureTest(unittest.TestCase):
    def test_rejects_traversal_and_lists_valid_makehuman_assets(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            valid_archive = root / "valid.zip"
            valid_output = root / "valid_extracted"
            with zipfile.ZipFile(valid_archive, "w", compression=zipfile.ZIP_DEFLATED) as archive:
                archive.writestr("materials/skin.mhmat", "material fixture")
                archive.writestr("clothes/shirt.mhclo", "clothing fixture")
                archive.writestr("presets/neutral.mhm", "preset fixture")
                archive.writestr("textures/ignored.txt", "not a MakeHuman selection file")

            safe_extract_zip(valid_archive, valid_output)
            self.assertEqual(
                [
                    "clothes/shirt.mhclo",
                    "materials/skin.mhmat",
                    "presets/neutral.mhm",
                ],
                list_asset_files(valid_output),
            )

            unsafe_archive = root / "unsafe.zip"
            unsafe_output = root / "unsafe_extracted"
            with zipfile.ZipFile(unsafe_archive, "w", compression=zipfile.ZIP_DEFLATED) as archive:
                archive.writestr("../escape.mhm", "must not leave extraction root")

            with self.assertRaises(ArchiveSafetyError):
                safe_extract_zip(unsafe_archive, unsafe_output)
            self.assertFalse((root / "escape.mhm").exists())
            self.assertEqual([], list_asset_files(unsafe_output))


if __name__ == "__main__":
    unittest.main()
