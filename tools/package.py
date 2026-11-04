"""Explicit publication allowlist; never copy installed games or personal libraries."""
import argparse
import hashlib
import sys
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def log(message: str):
    encoding = getattr(sys.stdout, "encoding", None) or "utf-8"
    print(message.encode(encoding, errors="backslashreplace").decode(encoding))


def build(output: Path, importer: Path | None = None):
    files = {}
    package = ROOT / "LocalMod/EmoteChat"
    for path in sorted((package / "CSharp").rglob("*.cs")):
        files["LocalMods/EmoteChat/" + path.relative_to(package).as_posix()] = path
    files["LocalMods/EmoteChat/filelist.xml"] = package / "filelist.xml"
    for name in ("import_emotes.py", "requirements.txt"):
        files["LocalMods/EmoteChat/Tools/" + name] = ROOT / "tools" / name
    if importer:
        if not importer.is_file() or importer.suffix.lower() != ".exe":
            raise ValueError("Importer executable not found")
        files["LocalMods/EmoteChat/Tools/EmoteImporter.exe"] = importer
    for name in ("README.md", "LICENSE"):
        files[name] = ROOT / name
    for name in ("SECURITY.md", "VALIDATION.md", "RESULTS.txt", "FIXES-0.2.0.zh-CN.md", "FIXES-0.1.1.zh-CN.md"):
        files["docs/" + name] = ROOT / "docs" / name
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
        for name, path in files.items():
            if path.is_symlink(): raise ValueError("Refusing symlink: " + str(path))
            info = zipfile.ZipInfo(name, (2026, 11, 3, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, path.read_bytes())
    log(f"{output.name}: {len(files)} files, SHA256 {hashlib.sha256(output.read_bytes()).hexdigest()}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=ROOT / "dist/EmoteChat-0.2.0.zip")
    parser.add_argument("--importer", type=Path)
    args = parser.parse_args()
    build(args.output, args.importer)
