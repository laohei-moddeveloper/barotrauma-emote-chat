"""Local-only image normalization. Network peers never receive original image files."""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import struct
import sys
import warnings
import zlib

from PIL import Image, ImageOps

MAX_FILE = 256 * 1024
MAX_INPUT = 8 * 1024 * 1024
Image.MAX_IMAGE_PIXELS = 1024 * 1024
warnings.simplefilter("error", Image.DecompressionBombWarning)


def convert(source: Path) -> bytes:
    if source.is_symlink() or not source.is_file() or source.stat().st_size > MAX_INPUT:
        raise ValueError("源文件必须是非链接文件，且不超过 8 MiB")
    # Bounded snapshot: changes to the file while importing cannot expand this read.
    with source.open("rb") as file:
        data = file.read(MAX_INPUT + 1)
    if len(data) > MAX_INPUT:
        raise ValueError("文件超过限制")
    with Image.open(io.BytesIO(data)) as image:
        if image.format not in {"PNG", "JPEG", "GIF"}:
            raise ValueError("仅支持 PNG、JPG、GIF")
        if max(image.size) > 1024 or image.width * image.height > 1024 * 1024:
            raise ValueError("源图片最长边不超过 1024 像素")
        count = getattr(image, "n_frames", 1)
        if count > 240:
            raise ValueError("源动画不超过 240 帧")
        frames, delays, elapsed, next_sample = [], [], 0, 0
        for index in range(count):
            image.seek(index)
            delay = max(20, min(int(image.info.get("duration", 100)), 10000))
            end = elapsed + delay
            if elapsed >= 10000:
                break
            if not frames or elapsed >= next_sample:
                frame = ImageOps.exif_transpose(image.convert("RGBA"))
                frame.thumbnail((128, 128), Image.Resampling.LANCZOS)
                if frames and frame.size != frames[0].size:
                    raise ValueError("动画画布尺寸不一致")
                frames.append(frame.copy())
                delays.append(0)
                next_sample = elapsed + 100
            delays[-1] += min(end, 10000) - elapsed
            elapsed = end
        # Preserve timing when sampling down to 48 frames; last short interval is clamped.
        while len(frames) > 48:
            new_frames, new_delays = [], []
            for i in range(0, len(frames), 2):
                new_frames.append(frames[i]); new_delays.append(sum(delays[i:i + 2]))
            frames, delays = new_frames, new_delays
        delays = [max(80, min(d, 10000)) for d in delays]
        if sum(delays) > 10000:
            excess = sum(delays) - 10000
            for i in range(len(delays) - 1, -1, -1):
                take = min(excess, delays[i] - 80); delays[i] -= take; excess -= take
            if excess:
                raise ValueError("动画时间超过限制")
        # If photographic animation is too large, reduce spatial resolution, preserving timing.
        for side in (128, 96, 64, 48, 32):
            resized = []
            for frame in frames:
                copy = frame.copy(); copy.thumbnail((side, side), Image.Resampling.LANCZOS)
                # MonoGame uses premultiplied alpha.
                raw = bytearray(copy.tobytes())
                for i in range(0, len(raw), 4):
                    alpha = raw[i + 3]
                    for c in range(3):
                        raw[i + c] = (raw[i + c] * alpha + 127) // 255
                resized.append(bytes(raw))
            compressor = zlib.compressobj(6, zlib.DEFLATED, -15)
            payload = compressor.compress(b"".join(resized)) + compressor.flush()
            w, h = copy.size
            result = struct.pack("<4sHHHH", b"ECP1", w, h, len(frames), 0)
            result += struct.pack("<" + "H" * len(delays), *delays) + payload
            if len(result) <= MAX_FILE:
                return result
        raise ValueError("压缩后仍超过 256 KiB")


def main():
    parser = argparse.ArgumentParser(description="将本地 PNG/JPG/GIF 导入 Emote Chat")
    parser.add_argument("sources", nargs="+", type=Path)
    tool_path = Path(sys.executable) if getattr(sys, "frozen", False) else Path(__file__)
    package_root = tool_path.resolve().parents[1]
    default_library = package_root / "Library" if (package_root / "filelist.xml").exists() else package_root / "LocalMod/EmoteChat/Library"
    parser.add_argument("--library", type=Path, default=default_library)
    args = parser.parse_args()
    args.library.mkdir(parents=True, exist_ok=True)
    failed = False
    for source in args.sources:
        try:
            result = convert(source)
            digest = hashlib.sha256(result).hexdigest()
            # Names never travel over the network. Only the digest is used as a file name.
            target = args.library / (digest + ".ecp")
            temp = target.with_suffix(".tmp")
            if target.is_symlink() or temp.is_symlink():
                raise ValueError("目标文件不能是符号链接")
            temp.write_bytes(result); os.replace(temp, target)
            index_path = args.library / "names.json"
            if index_path.is_symlink() or index_path.with_suffix(".tmp").is_symlink():
                raise ValueError("索引文件不能是符号链接")
            names = {}
            if index_path.exists() and index_path.stat().st_size <= 32768:
                try:
                    names = json.loads(index_path.read_text(encoding="utf-8"))
                    if not isinstance(names, dict): names = {}
                except (ValueError, OSError):
                    names = {}
            names[digest] = "".join(c for c in source.stem if c.isprintable() and c not in "‖<>")[:24]
            if len(names) > 128:
                names = dict(list(names.items())[-128:])
            index_temp = index_path.with_suffix(".tmp")
            index_temp.write_text(json.dumps(names, ensure_ascii=False), encoding="utf-8")
            os.replace(index_temp, index_path)
            print(f"已导入 {source.name}: {digest[:12]} ({len(result)} bytes)")
        except (OSError, ValueError, EOFError, Image.DecompressionBombError, Image.DecompressionBombWarning) as error:
            print(f"导入失败 {source.name}: {error}"); failed = True
    raise SystemExit(1 if failed else 0)


if __name__ == "__main__":
    main()
