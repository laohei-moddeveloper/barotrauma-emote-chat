import importlib.util
from pathlib import Path
import tempfile
import sys
import unittest
import zipfile

ROOT = Path(__file__).parents[1]
spec = importlib.util.spec_from_file_location("packager", ROOT / "tools/package.py")
packager = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packager)


class PackageTests(unittest.TestCase):
    def test_workshop_staging_without_publishing(self):
        sys.path.insert(0, str(ROOT / "tools"))
        try:
            from prepare_workshop import stage, quoted
            from PIL import Image
            with tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                draft = root / "draft"
                stage(draft)
                self.assertTrue((draft / "PENDING-METADATA.json").exists())
                self.assertFalse((draft / "create-new-item.vdf").exists())
                self.assertTrue((draft / "content/EmoteChat/filelist.xml").exists())
                self.assertFalse(any(draft.rglob("*.ecp")))
                with self.assertRaises(ValueError): stage(draft)
                cover = root / "cover.png"
                Image.new("RGB", (32, 32)).save(cover)
                description = root / "description.txt"
                description.write_text("测试简介\n感谢测试用灵感提供者", encoding="utf-8")
                with self.assertRaises(ValueError): stage(root / "bad", "Title\nInjection", cover, description)
                ready = root / "ready"
                stage(ready, 'Test "Title"', cover, description)
                vdf = (ready / "create-new-item.vdf").read_text(encoding="utf-8")
                self.assertIn('"publishedfileid" "0"', vdf)
                self.assertIn('Test \\"Title\\"', vdf)
                self.assertIn('\\n', quoted("a\nb"))
        finally:
            sys.path.pop(0)

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
