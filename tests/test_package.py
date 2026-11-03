import importlib.util
from pathlib import Path
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).parents[1]
spec = importlib.util.spec_from_file_location("packager", ROOT / "tools/package.py")
packager = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packager)


class PackageTests(unittest.TestCase):
    def test_allowlist_excludes_personal_and_game_files(self):
        with tempfile.TemporaryDirectory() as temp:
            target = Path(temp) / "preview.zip"
            packager.build(target)
            with zipfile.ZipFile(target) as archive:
                names = archive.namelist()
                self.assertIn("LocalMods/EmoteChat/filelist.xml", names)
                self.assertIn("LocalMods/EmoteChat/CSharp/Client/Client.cs", names)
                self.assertFalse(any(n.endswith((".ecp", ".dll", "names.json")) for n in names))
                self.assertFalse(any(".." in Path(n).parts or n.startswith("/") for n in names))
                self.assertTrue(all(".git/" not in n and ".venv/" not in n for n in names))
                self.assertEqual(len(names), len(set(names)))
            first = target.read_bytes()
            packager.build(target)
            self.assertEqual(first, target.read_bytes())


if __name__ == "__main__":
    unittest.main()
