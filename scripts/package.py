from pathlib import Path
import hashlib
import subprocess
import zipfile
from version import read_version

root = Path(__file__).resolve().parents[1]
dist = root / "dist"
runtime = dist / "FrameFlow"
version = read_version()
binary_zip = dist / f"FrameFlow-V{version}-win-x64.zip"
source_zip = dist / f"FrameFlow-V{version}-source.zip"
with zipfile.ZipFile(binary_zip, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
    for file in sorted(runtime.rglob("*")):
        if file.is_file() and file.suffix != ".pdb":
            relative = file.relative_to(runtime)
            # A release must not include the local user's playback history,
            # bookmarks, window state or render caches.
            if relative.parts[0] == "portable_config" and relative.as_posix() not in (
                "portable_config/mpv.conf",
            ):
                continue
            archive.write(file, "FrameFlow/" + relative.as_posix())
files = subprocess.check_output([r"C:\Program Files\Git\cmd\git.exe", "ls-files", "--cached", "--others", "--exclude-standard", "-z"], cwd=root).decode("utf-8").split("\0")
with zipfile.ZipFile(source_zip, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
    for name in sorted(set(files)):
        if name and not name.startswith("tools/") and (root / name).is_file():
            archive.write(root / name, "FrameFlow-source/" + name)
with (dist / f"FrameFlow-V{version}-SHA256.txt").open("w", encoding="utf-8") as output:
    for file in (binary_zip, runtime / "FrameFlow.exe", source_zip):
        digest = hashlib.file_digest(file.open("rb"), "sha256").hexdigest()
        output.write(f"{digest}  {file.relative_to(dist)}\n")
        print(file.name, file.stat().st_size, digest)
