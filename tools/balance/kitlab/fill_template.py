#!/usr/bin/env python
"""Fill kitlab.template.html with fresh page data: replaces the single `var G=/*KITLAB_DATA*/{};` line by the output of
export_kitlab_data.py and writes the page to the path given (default: kitlab.page.html next to the template; it is a build
product, delete it after publishing, do not commit it).

Usage:  python -I tools/balance/kitlab/fill_template.py [out.html]
"""
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
MARK = b"var G=/*KITLAB_DATA*/{};"


def main(argv):
    out = Path(argv[0]) if argv else HERE / "kitlab.page.html"
    template = (HERE / "kitlab.template.html").read_bytes()
    if template.count(MARK) != 1:
        print("ERROR: the template must hold exactly one placeholder line", file=sys.stderr)
        return 2
    data = subprocess.run([sys.executable, "-I", str(HERE.parent / "export_kitlab_data.py")], capture_output=True, check=True).stdout.strip()
    out.write_bytes(template.replace(MARK, b"var G=" + data + b";"))
    print(f"wrote {out} ({out.stat().st_size} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
