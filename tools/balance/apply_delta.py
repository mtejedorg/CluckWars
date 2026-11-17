#!/usr/bin/env python
"""Promote a tested balance delta into the shipped .asset files (BL-183): edits only the scalar lines it must.

Usage (run with `python -I`):
  apply_delta.py delta.json            dry run (default): prints the exact lines it would change
  apply_delta.py delta.json --apply    writes them
  apply_delta.py --dry-run -           (stdin)

Rules: the delta must pass make_delta.py first (same check as publish_delta.py); every other byte of each file is kept
(read and written as bytes, only the `  Field: value` line or the `Kit:` guid line changes); `kits` rewrites the guid of the
changed entries of the `Kit:` list; an enum given by member name is refused (the numeric value is not known here, edit it
by hand); a field with no scalar line in the asset is refused; the run is refused when any target file has uncommitted
changes (`git status --porcelain`); `.meta` files are never touched. After applying, run the EditMode suite and expect
the published delta to become empty (the assets now equal it).
"""
import json
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import publish_delta  # noqa: E402
import schema  # noqa: E402

KIT_LINE = re.compile(r"^(  - \{fileID: \d+, guid: )([0-9a-f]{32})(, type: \d+\}\s*)$")


class ApplyError(Exception):
    pass


def fmt_number(v, ship_txt):
    if isinstance(v, bool):
        return ("true" if v else "false") if ship_txt in ("true", "false") else ("1" if v else "0")
    if isinstance(v, int) or (isinstance(v, float) and v == int(v) and abs(v) < 1e15):
        return str(int(v))
    return publish_delta._float32_token(v)


def git_dirty(paths, cwd):
    p = subprocess.run(["git", "status", "--porcelain", "--"] + [str(x) for x in paths], capture_output=True, text=True,
                       cwd=str(cwd))
    if p.returncode != 0:
        raise ApplyError("git status failed: " + p.stderr.strip())
    return [l for l in p.stdout.splitlines() if l.strip()]


def plan(delta, assets):
    """[(path, line_index, old_line, new_line)] without writing. Raises ApplyError."""
    g = schema.guid_names(assets)
    by_name = {n: guid for guid, n in g.items()}
    edits = []

    def asset_for(name):
        lst = assets.get(name)
        if not lst and name == "MatchConfig":
            lst = [a for l in assets.values() for a in l if a.path.stem == "MatchConfig"]
        if not lst or len(lst) != 1:
            raise ApplyError(f"{name}: no single asset with this name")
        return lst[0]

    for name, fields in sorted((delta.get("assets") or {}).items()):
        a = asset_for(name)
        lines = a.text.splitlines(keepends=True)
        for field, v in sorted(fields.items()):
            if isinstance(v, str):
                raise ApplyError(f"{name}.{field}: enum by name ('{v}') cannot be mapped here, edit it by hand")
            pat = re.compile(rf"^  {re.escape(field)}: (-?[0-9.eE+-]+|true|false)(\s*)$")
            idx = [i for i, l in enumerate(lines) if pat.match(l.rstrip("\r\n"))]
            if len(idx) != 1:
                raise ApplyError(f"{name}.{field}: no single scalar line in {a.path.name}")
            m = pat.match(lines[idx[0]].rstrip("\r\n"))
            eol = lines[idx[0]][len(lines[idx[0]].rstrip("\r\n")):]
            new = f"  {field}: {fmt_number(v, m.group(1))}{m.group(2)}{eol}"
            if new != lines[idx[0]]:
                edits.append((a.path, idx[0], lines[idx[0]], new))

    for owner, kit in sorted((delta.get("kits") or {}).items()):
        a = asset_for(owner)
        lines = a.text.splitlines(keepends=True)
        start = [i for i, l in enumerate(lines) if l.rstrip("\r\n") == "  Kit:"]
        if len(start) != 1:
            raise ApplyError(f"kits.{owner}: no Kit: list in {a.path.name}")
        i = start[0] + 1
        entries = []
        while i < len(lines) and KIT_LINE.match(lines[i].rstrip("\r\n")):
            entries.append(i)
            i += 1
        if len(entries) != len(kit):
            raise ApplyError(f"kits.{owner}: the asset lists {len(entries)} abilities, the delta {len(kit)}")
        for pos, ability in zip(entries, kit):
            if ability not in by_name:
                raise ApplyError(f"kits.{owner}: no .meta guid known for '{ability}'")
            m = KIT_LINE.match(lines[pos].rstrip("\r\n"))
            eol = lines[pos][len(lines[pos].rstrip("\r\n")):]
            new = m.group(1) + by_name[ability] + m.group(3) + eol
            if new != lines[pos]:
                edits.append((a.path, pos, lines[pos], new))
    return edits


def apply_edits(edits):
    by_path = {}
    for path, idx, old, new in edits:
        by_path.setdefault(path, []).append((idx, new))
    for path, items in by_path.items():
        raw = path.read_bytes().decode("utf-8")
        lines = raw.splitlines(keepends=True)
        for idx, new in items:
            lines[idx] = new
        path.write_bytes("".join(lines).encode("utf-8"))


def main(argv=None, validate=None, dirty_check=None, out=print):
    argv = list(sys.argv[1:] if argv is None else argv)
    apply = "--apply" in argv
    argv = [a for a in argv if a not in ("--apply", "--dry-run")]
    src = sys.stdin.read() if not argv or argv[0] == "-" else Path(argv[0]).read_text(encoding="utf-8")
    try:
        delta = json.loads(src)
        ok, msg = (validate or publish_delta.validate_with_make_delta)(delta)
        if not ok:
            raise ApplyError("the delta did not pass make_delta.py:\n" + msg)
        assets = schema.load_assets()
        edits = plan(delta, assets)
        if not edits:
            out("Nothing to change: the assets already hold these values.")
            return 0
        paths = sorted({e[0] for e in edits})
        dirty = (dirty_check or (lambda p: git_dirty(p, schema.DATA)))(paths)
        if dirty:
            raise ApplyError("target files have uncommitted changes, commit or stash them first:\n  " + "\n  ".join(dirty))
        for path, idx, old, new in edits:
            out(f"{path}:{idx + 1}\n  - {old.rstrip()}\n  + {new.rstrip()}")
        if not apply:
            out(f"DRY RUN: {len(edits)} line(s) in {len(paths)} file(s) would change. Re-run with --apply.")
            return 0
        apply_edits(edits)
        out(f"Wrote {len(edits)} line(s) in {len(paths)} file(s). Run the EditMode suite; the published delta is now redundant.")
        return 0
    except (ApplyError, ValueError) as e:
        print(f"ERROR: {e}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
