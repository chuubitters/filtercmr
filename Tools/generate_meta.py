"""Create missing Unity .meta files with deterministic GUIDs (md5 of the asset path).

Unity would generate random GUIDs on first import; pre-generating stable ones keeps
script references identical on every machine and clone. Existing .meta files are never
touched. Usage:  python3 Tools/generate_meta.py
"""
import hashlib
from pathlib import Path

ASSETS = Path(__file__).resolve().parents[1] / "UnityProject" / "Assets"

FOLDER = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
SCRIPT = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
ASMDEF = """fileFormatVersion: 2
guid: {guid}
AssemblyDefinitionImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def guid_for(rel: str) -> str:
    return hashlib.md5(("filtercmr:" + rel).encode()).hexdigest()


def main() -> int:
    created = 0
    for p in sorted([ASSETS, *ASSETS.rglob("*")]):
        if p == ASSETS or p.name.startswith(".") or p.suffix == ".meta":
            continue
        if any(part.startswith(".") for part in p.relative_to(ASSETS).parts):
            continue
        if p.is_dir():
            tpl = FOLDER
        elif p.suffix == ".cs":
            tpl = SCRIPT
        elif p.suffix == ".asmdef":
            tpl = ASMDEF
        else:
            continue  # other asset types: let Unity create them
        meta = p.with_name(p.name + ".meta")
        if meta.exists():
            continue
        rel = "Assets/" + p.relative_to(ASSETS).as_posix()
        meta.write_text(tpl.format(guid=guid_for(rel)), newline="\n")
        created += 1
        print("created", meta.relative_to(ASSETS.parent))
    print(f"{created} meta file(s) created")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
