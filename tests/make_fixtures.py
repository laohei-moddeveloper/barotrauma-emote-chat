"""Generate synthetic, non-user fixtures for cross-language integration checks."""
import importlib.util
from pathlib import Path
import sys
from PIL import Image, ImageDraw

root = Path(__file__).parents[1]
spec = importlib.util.spec_from_file_location("importer", root / "tools/import_emotes.py")
importer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(importer)
out = Path(sys.argv[1]) if len(sys.argv) > 1 else root / ".test-fixtures"
out.mkdir(parents=True, exist_ok=True)
image = Image.new("RGBA", (128, 64), (0, 100, 200, 128))
image.save(out / "still.png")
frames = []
for i in range(12):
    frame = Image.new("RGBA", (96, 96), (0, 0, 0, 0))
    ImageDraw.Draw(frame).ellipse((i * 4, 24, i * 4 + 24, 48), fill=(255, 180, 0, 255))
    frames.append(frame)
frames[0].save(out / "motion.gif", save_all=True, append_images=frames[1:], duration=100, loop=0, disposal=2)
for source in (out / "still.png", out / "motion.gif"):
    (out / (source.stem + ".ecp")).write_bytes(importer.convert(source))
