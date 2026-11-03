import hashlib
import importlib.util
import io
from pathlib import Path
import struct
import tempfile
import unittest
import zlib

from PIL import Image, ImageDraw

spec = importlib.util.spec_from_file_location("importer", Path(__file__).parents[1] / "tools/import_emotes.py")
importer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(importer)


def unpack(data):
    magic, w, h, frames, reserved = struct.unpack_from("<4sHHHH", data)
    assert magic == b"ECP1" and reserved == 0
    delays = struct.unpack_from("<" + "H" * frames, data, 12)
    pixels = zlib.decompress(data[12 + frames * 2:], -15)
    return w, h, frames, delays, pixels


class ImportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def test_png_alpha_and_resize(self):
        source = self.root / "test.png"
        Image.new("RGBA", (512, 256), (200, 100, 50, 128)).save(source)
        data = importer.convert(source)
        w, h, count, delays, pixels = unpack(data)
        self.assertEqual((w, h, count), (128, 64, 1))
        self.assertEqual(pixels[:4], bytes((100, 50, 25, 128)))
        self.assertEqual(len(pixels), w * h * count * 4)
        self.assertEqual(data, importer.convert(source))

    def test_gif_compositing_timing_and_frame_cap(self):
        source = self.root / "animated.gif"
        frames = []
        for i in range(120):
            frame = Image.new("RGBA", (96, 64), (0, 0, 0, 0))
            ImageDraw.Draw(frame).rectangle((i % 70, 10, i % 70 + 20, 30), fill=(255, 100, i, 255))
            frames.append(frame)
        frames[0].save(source, save_all=True, append_images=frames[1:], duration=40, loop=0, disposal=2)
        w, h, count, delays, pixels = unpack(importer.convert(source))
        self.assertTrue(1 < count <= 48)
        self.assertEqual(sum(delays), 4800)
        self.assertGreaterEqual(min(delays), 80)
        self.assertGreater(len(set(pixels[i:i + w * h * 4] for i in range(0, len(pixels), w * h * 4))), 1)
        # Decoded bytes outside the moving rectangle remain transparent, not accumulated trails.
        self.assertEqual(pixels[:4], b"\0\0\0\0")

    def test_rejects_other_format_and_oversize(self):
        bmp = self.root / "x.bmp"
        Image.new("RGB", (4, 4)).save(bmp)
        with self.assertRaises(ValueError): importer.convert(bmp)
        large = self.root / "large.png"
        Image.new("RGB", (1025, 8)).save(large)
        with self.assertRaises(ValueError): importer.convert(large)
        blob = self.root / "blob.gif"
        blob.write_bytes(b"\0" * (8 * 1024 * 1024 + 1))
        with self.assertRaises(ValueError): importer.convert(blob)

    def test_rejects_symlink_and_truncation(self):
        source = self.root / "test.png"
        Image.new("RGB", (64, 64)).save(source)
        link = self.root / "link.png"
        link.symlink_to(source)
        with self.assertRaises(ValueError): importer.convert(link)
        source.write_bytes(source.read_bytes()[:40])
        with self.assertRaises(OSError): importer.convert(source)


if __name__ == "__main__":
    unittest.main()
