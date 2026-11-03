"""Stage a clean NEW workshop item; never log in, create an item, or upload by itself."""
import argparse
import json
from pathlib import Path
import shutil
import tempfile

from package import ROOT


def quoted(value: str) -> str:
    if any(ord(c) < 32 and c not in "\n\r\t" for c in value):
        raise ValueError("Control character in metadata")
    return '"' + value.replace("\\", "\\\\").replace('"', '\\"').replace("\r", "").replace("\n", "\\n").replace("\t", "\\t") + '"'


def stage(destination: Path, title: str | None = None, cover: Path | None = None, description: Path | None = None, importer: Path | None = None):
    from package import build
    from PIL import Image
    import zipfile

    destination = destination.resolve()
    if destination.exists():
        raise ValueError("目标目录必须不存在，避免覆盖已有工坊内容")
    complete = title is not None or cover is not None or description is not None
    text = ""
    if complete:
        if not title or not cover or not description:
            raise ValueError("标题、封面和简介必须一起提供")
        if len(title.encode("utf-8")) > 127 or any(ord(c) < 32 for c in title):
            raise ValueError("标题过长或含控制字符")
        if description.stat().st_size > 7500:
            raise ValueError("简介过长")
        text = description.read_text(encoding="utf-8")
        if "待作者确认" in text:
            raise ValueError("请先补齐灵感感谢等简介占位内容")
        quoted(title); quoted(text)
        if cover.is_symlink() or not cover.is_file() or cover.stat().st_size > 1024 * 1024:
            raise ValueError("封面必须是 ≤1 MiB 的本地非链接图片")
        with Image.open(cover) as image:
            if image.format not in {"PNG", "JPEG"}: raise ValueError("封面仅支持 PNG/JPG")
            image.verify()
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=destination.parent) as temp:
        scratch = Path(temp)
        archive_path = scratch / "mod.zip"
        build(archive_path, importer)
        content = scratch / "stage/content/EmoteChat"
        content.mkdir(parents=True)
        with zipfile.ZipFile(archive_path) as archive:
            for item in archive.infolist():
                name = item.filename.removeprefix("LocalMods/EmoteChat/")
                if ".." in Path(name).parts or Path(name).is_absolute(): raise ValueError("Invalid archive path")
                target = content / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(archive.read(item))
        if complete:
            preview_name = "cover" + cover.suffix.lower()
            shutil.copyfile(cover, scratch / "stage" / preview_name)
            values = {
                "appid": "602960", "publishedfileid": "0",
                "contentfolder": (destination / "content/EmoteChat").as_posix(),
                "previewfile": (destination / preview_name).as_posix(),
                "visibility": "0", "title": title, "description": text,
                "changenote": "Initial preview",
            }
            vdf = '"workshopitem"\n{\n' + "".join("  " + quoted(k) + " " + quoted(v) + "\n" for k, v in values.items()) + '}\n'
            (scratch / "stage/create-new-item.vdf").write_text(vdf, encoding="utf-8")
        else:
            (scratch / "stage/PENDING-METADATA.json").write_text(json.dumps({"new_item": True, "appid": 602960, "pending": ["title", "cover", "inspiration credit in description"], "upload_attempted": False}, indent=2), encoding="utf-8")
        shutil.move(str(scratch / "stage"), str(destination))
    print("已准备工坊内容，未登录或上传：" + str(destination))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=ROOT / "dist/workshop-draft")
    parser.add_argument("--title")
    parser.add_argument("--cover", type=Path)
    parser.add_argument("--description", type=Path)
    parser.add_argument("--importer", type=Path)
    args = parser.parse_args()
    stage(args.output, args.title, args.cover, args.description, args.importer)
