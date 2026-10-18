"""Public version is shared by the EXE badge and release archive names."""
import argparse
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src/MpvNet.Windows/MpvNet.Windows.csproj"


def next_version(current: int) -> int:
    value = current + 1
    while "4" in str(value):
        value += 1
    return value


def read_version() -> int:
    value = int(re.search(r"<ModernVersion>(\d+)</ModernVersion>", PROJECT.read_text(encoding="utf-8")).group(1))
    if value <= 0 or "4" in str(value):
        raise ValueError("Public versions must be positive and must not contain 4.")
    return value


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--next", action="store_true", help="Advance once, skipping every version containing 4.")
    options = parser.parse_args()
    value = read_version()
    if options.next:
        previous = value
        value = next_version(value)
        content = PROJECT.read_text(encoding="utf-8")
        content = re.sub(r"<ModernVersion>\d+</ModernVersion>", f"<ModernVersion>{value}</ModernVersion>", content, count=1)
        PROJECT.write_text(content, encoding="utf-8", newline="\n")
        for document in (ROOT / "README.md", ROOT / "docs/modern-player.md"):
            text = document.read_text(encoding="utf-8")
            text = re.sub(r"^当前版本.*$", lambda match: match.group(0).replace(f"V{previous}", f"V{value}"), text, flags=re.MULTILINE)
            document.write_text(text, encoding="utf-8", newline="\n")
    print(f"V{value}")
