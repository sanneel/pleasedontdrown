"""Package the saved Unity project and current Windows build without caches or local account data."""
from pathlib import Path
import argparse
import zipfile

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT.parent / "Share"
OUT.mkdir(exist_ok=True)
parser = argparse.ArgumentParser()
parser.add_argument("--label", default="IslandFix")
args = parser.parse_args()
if not args.label.replace("-", "").isalnum():
    raise ValueError("Archive label must contain letters, digits or hyphens.")

def package(name, files, prefix="", readme=None):
    target = OUT / name
    if target.exists():
        raise FileExistsError(target)
    total = 0
    with zipfile.ZipFile(target, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=3, allowZip64=True) as archive:
        for path, relative in files:
            archive.write(path, prefix + relative.as_posix())
            total += 1
        if readme:
            archive.writestr("START_HERE.txt", readme)
    with zipfile.ZipFile(target) as archive:
        bad = archive.testzip()
        if bad:
            raise RuntimeError(f"ZIP integrity failed: {bad}")
    print(f"PASS: {target.name}: {total} files, {target.stat().st_size / 1048576:.1f} MiB; every ZIP entry CRC verified", flush=True)

build = ROOT / "Builds/Win64"
build_files = [(p, p.relative_to(build)) for p in sorted(build.rglob("*")) if p.is_file()
               and "Screenshots" not in p.relative_to(build).parts
               and not any("DoNotShip" in part for part in p.relative_to(build).parts)]
if not (build / "PleaseDontDrown.exe").is_file():
    raise FileNotFoundError("Build the Windows game first.")
package(f"PleaseDontDrown-{args.label}-Windows-2026-10-09.zip", build_files,
        readme="Extract this entire ZIP into a folder, then open PleaseDontDrown.exe.\nKeep PleaseDontDrown_Data, UnityPlayer.dll and all other included files alongside the game.\nUpdated 9 October 2026: island height/rocks repaired; basketball uses two hands for jumps and shooting, right hand for dribbling.\n")

folders = ("Assets", "Packages", "ProjectSettings", "ArtSource", "Docs", "Tools")
source_files = []
for folder in folders:
    for p in sorted((ROOT / folder).rglob("*")):
        if p.is_file() and "__pycache__" not in p.parts and p.suffix.lower() not in (".pyc", ".blend1"):
            source_files.append((p, p.relative_to(ROOT)))
for name in ("README.md", "steam_appid.txt", ".gitignore", ".gitattributes"):
    if (ROOT / name).is_file():
        source_files.append((ROOT / name, Path(name)))
package(f"PleaseDontDrown-{args.label}-UnityProject-2026-10-09.zip", source_files, prefix="PleaseDontDrown/")
