"""One local publisher entrypoint, clean payload, finalized bilingual metadata."""
import argparse
from pathlib import Path
import shutil
import tempfile
import zipfile
import json
from package import ROOT, log
from prepare_workshop import stage


def build(output: Path, importer: Path):
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as temp:
        draft = Path(temp) / "workshop"
        stage(draft, (ROOT / "workshop/title.en.txt").read_text(encoding="utf-8").strip(), ROOT / "workshop/cover.jpg", ROOT / "workshop/description.en.txt", importer)
        # Publish-New.ps1 persists the new item ID. Do not ship a competing create-again VDF.
        (draft / "create-new-item.vdf").unlink()
        publication = json.loads((ROOT / "docs/PUBLICATION.json").read_text(encoding="utf-8"))
        existing_id = publication.get("workshop_item_id")
        if existing_id:
            if not str(existing_id).isdigit(): raise ValueError("Invalid existing Workshop ID")
            (draft / "publish-state.json").write_text(json.dumps({"project": "barotrauma-chat-with-emote-v01", "file_id": str(existing_id), "app_id": 602960, "status": "existing-item-update"}, indent=2), encoding="utf-8")
        for name in ("Publish-New.ps1", "Publish-Workshop.ps1", "Update-Localizations.ps1", "title.en.txt", "title.zh-CN.txt", "description.en.txt", "description.zh-CN.txt", "cover.jpg"):
            shutil.copyfile(ROOT / "workshop" / name, draft / name)
        shutil.copyfile(ROOT / "docs/LOCAL_CODEX_HANDOFF.zh-CN.md", draft / "LOCAL_CODEX_HANDOFF.zh-CN.md")
        with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
            for path in sorted(draft.rglob("*")):
                if path.is_file(): archive.write(path, path.relative_to(Path(temp)).as_posix())
    log("Workshop upload bundle (not uploaded): " + str(output))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--importer", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=ROOT / "dist/EmoteChat-0.3.0-workshop-update.zip")
    args = parser.parse_args()
    build(args.output, args.importer)
