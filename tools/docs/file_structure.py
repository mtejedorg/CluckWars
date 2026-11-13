"""Keeps the generated parts of docs/FILE_STRUCTURE.md true.

Usage (from anywhere; Python 3, standard library only):

    python -I tools/docs/file_structure.py generate            print every generated block
    python -I tools/docs/file_structure.py check               compare the blocks in the doc with a fresh run
    python -I tools/docs/file_structure.py check --write       rewrite the blocks in the doc in place
    python -I tools/docs/file_structure.py check --doc <path>  check another copy of the doc

A generated block in the doc sits between

    <!-- BEGIN GENERATED: <name> -->
    <!-- END GENERATED: <name> -->

and holds exactly what `generate` prints for <name>. Everything outside the markers is written by hand.

Exit codes: 0 = blocks match (or were written), 1 = a block differs, is unknown or is missing,
2 = the repository or the doc cannot be read.

It reads the WORKING TREE (it is a developer tool, so it reflects uncommitted edits too). It walks only
`Assets/_Game` for counts and `Assets` for `.asmdef.meta` files; `Library/`, `Builds/`, `Captures/` and the
third-party folders are never counted, nor are folders Unity does not import (`Name~`, `.hidden`). Package assemblies are named from KNOWN_PACKAGE_ASMDEFS below; a GUID
that resolves nowhere prints as `GUID:<hex>` rather than failing.
"""
import argparse
import collections
import difflib
import json
import os
import re
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
GAME = "Assets/_Game"
SCRIPTS = GAME + "/Scripts"
DATA = GAME + "/Data"
WEAVE_CONFIG = "Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion"
ABILITY_REGISTRY = DATA + "/AbilityRegistry.asset"
DEFAULT_DOC = "docs/FILE_STRUCTURE.md"
LAYER_ORDER = ["CluckWars.Core", "CluckWars.Gameplay", "CluckWars.Presentation", "CluckWars.Diagnostics", "CluckWars.App"]
EDITOR_ASSEMBLY = "Assembly-CSharp-Editor"
DEFAULT_ASSEMBLY = "Assembly-CSharp"

# Package asmdefs live in Library/PackageCache, which this tool does not read (it is absent on a fresh clone).
# Their GUIDs are fixed by the package; read from Library/PackageCache/*/**.asmdef.meta on 2026-11-13.
KNOWN_PACKAGE_ASMDEFS = {
    "8c4dd21966739024fbd72155091d199e": "Unity.AI.Navigation",
    "75469ad4d38634e559750d17036d5f7c": "Unity.InputSystem",
    "df380645f10b7bc4b97d4f5eb6303d95": "Unity.RenderPipelines.Core.Runtime",
    "15fc0a57446b3144c949da3e2b9737a9": "Unity.RenderPipelines.Universal.Runtime",
    "2bafac87e7f4b9b418d9448d219b01ab": "UnityEngine.UI",
    "fe25561d224ed4743af4c60938a59d0b": "Unity.Services.Core",
    "5540e30183c82e84b954c033c388e06c": "Unity.Services.Authentication",
    "37e17ffe38d86ae48bc3207e83ffef88": "Unity.Services.Multiplayer",
}

GUID_RE = re.compile(r"^guid:\s*([0-9a-f]{32})", re.M)
SCRIPT_RE = re.compile(r"m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*([0-9a-f]{32})")
REF_RE = re.compile(r"guid:\s*([0-9a-f]{32}),\s*type:\s*2")
BLOCK_RE = re.compile(r"<!-- BEGIN GENERATED: ([\w-]+) -->\n(.*?)<!-- END GENERATED: \1 -->", re.S)


class RepoError(Exception):
    """The repository cannot be read the way this tool expects (exit 2)."""


# ---------------------------------------------------------------- reading

def path(rel):
    return os.path.join(REPO, *rel.split("/"))


def read_text(rel):
    try:
        with open(path(rel), encoding="utf-8-sig") as f:
            return f.read()
    except OSError as e:
        raise RepoError(f"cannot read {rel}: {e.strerror}") from None


def read_json(rel):
    try:
        return json.loads(read_text(rel))
    except ValueError as e:
        raise RepoError(f"{rel} is not valid JSON: {e}") from None


def meta_guid(rel):
    """The GUID in <rel>.meta, or None when the meta file is missing or has none."""
    try:
        m = GUID_RE.search(read_text(rel + ".meta"))
    except RepoError:
        return None
    return m.group(1) if m else None


def unity_visible(dirs):
    """Prune, in place, the folders Unity never imports (`Name~`, `.hidden`); they are not part of the project."""
    dirs[:] = sorted(d for d in dirs if not d.endswith("~") and not d.startswith("."))


def walk(root_rel):
    """Every file under root_rel as a repo-relative '/' path, sorted, .meta files excluded."""
    out = []
    for d, dirs, files in os.walk(path(root_rel)):
        unity_visible(dirs)
        rel_dir = os.path.relpath(d, REPO).replace(os.sep, "/")
        out.extend(f"{rel_dir}/{f}" for f in sorted(files) if not f.endswith(".meta"))
    return out


# ---------------------------------------------------------------- model

class Model:
    def __init__(self):
        if not os.path.isdir(path(SCRIPTS)):
            raise RepoError(f"{SCRIPTS} not found under {REPO}")
        self.game_files = walk(GAME)
        self.asmdefs = {}  # folder -> parsed asmdef
        self.asmrefs = {}  # folder -> assembly name
        for f in self.game_files:
            folder = f.rsplit("/", 1)[0]
            if f.endswith(".asmdef"):
                self.asmdefs[folder] = read_json(f)
            elif f.endswith(".asmref"):
                self.asmrefs[folder] = read_json(f).get("reference", "?")
        self.guid_names = dict(KNOWN_PACKAGE_ASMDEFS)
        for d, dirs, files in os.walk(path("Assets")):
            unity_visible(dirs)
            for f in files:
                if f.endswith(".asmdef.meta"):
                    rel = os.path.relpath(os.path.join(d, f), REPO).replace(os.sep, "/")[:-5]
                    g = meta_guid(rel)
                    if g:
                        self.guid_names[g] = read_json(rel).get("name", rel)
        self.weave = read_json(WEAVE_CONFIG).get("AssembliesToWeave", [])
        self.cs = [f for f in self.game_files if f.endswith(".cs")]

    def assembly_of(self, rel):
        folder = rel.rsplit("/", 1)[0]
        while folder.startswith(GAME):
            if folder in self.asmdefs:
                return self.asmdefs[folder].get("name", "?")
            if folder in self.asmrefs:
                return self.asmrefs[folder]
            folder = folder.rsplit("/", 1)[0]
        return EDITOR_ASSEMBLY if "/Editor/" in rel else DEFAULT_ASSEMBLY

    def ref_name(self, ref):
        if ref.startswith("GUID:"):
            return self.guid_names.get(ref[5:].lower(), ref)
        return ref


def short(rel):
    return rel[len(SCRIPTS) + 1:] if rel.startswith(SCRIPTS + "/") else rel


def layer_key(name):
    return (LAYER_ORDER.index(name), name) if name in LAYER_ORDER else (len(LAYER_ORDER), name)


# ---------------------------------------------------------------- blocks

def block_assemblies(m):
    counts = collections.Counter(m.assembly_of(f) for f in m.cs)
    lines = [
        "| Assembly | Defined in (`Scripts/`) | Folders attached by `.asmref` | References | Woven by Fusion | `.cs` files |",
        "|---|---|---|---|---|---|",
    ]
    for folder, asm in sorted(m.asmdefs.items(), key=lambda kv: layer_key(kv[1].get("name", "?"))):
        name = asm.get("name", "?")
        attached = sorted(short(k) for k, v in m.asmrefs.items() if v == name)
        refs = ", ".join(m.ref_name(r) for r in asm.get("references", [])) or "-"
        if asm.get("overrideReferences"):
            refs += "; prebuilt: " + (", ".join(asm.get("precompiledReferences") or []) or "none")
        lines.append(f"| `{name}` | `{short(folder)}` | {', '.join(f'`{a}`' for a in attached) or '-'} | {refs} | "
                     f"{'yes' if name in m.weave else 'no'} | {counts[name]} |")
    for name, where in ((EDITOR_ASSEMBLY, "built-in (any `Editor` folder)"), (DEFAULT_ASSEMBLY, "built-in (everything else)")):
        lines.append(f"| `{name}` | {where} | - | all `autoReferenced` assemblies | "
                     f"{'yes' if name in m.weave else 'no'} | {counts[name]} |")
    unknown = sorted(set(counts) - {a.get("name") for a in m.asmdefs.values()} - {EDITOR_ASSEMBLY, DEFAULT_ASSEMBLY})
    for name in unknown:
        lines.append(f"| `{name}` | (asmref target with no asmdef in `{GAME}`) | - | - | - | {counts[name]} |")
    lines.append("")
    lines.append(f"`.cs` counts cover `{GAME}` only. `AssembliesToWeave` in `{WEAVE_CONFIG}`: "
                 + ", ".join(f"`{w}`" for w in m.weave) + ".")
    return "\n".join(lines) + "\n"


def block_script_folders(m):
    by_dir = collections.defaultdict(list)
    for f in m.cs:
        if f.startswith(SCRIPTS + "/"):
            by_dir[f.rsplit("/", 1)[0]].append(f)
    lines = ["| Folder (`Assets/_Game/Scripts/`) | Assembly | `.cs` files directly in it |", "|---|---|---|"]
    for d in sorted(by_dir, key=lambda s: s.lower()):
        asms = collections.Counter(m.assembly_of(f) for f in by_dir[d])
        label = ", ".join(f"`{a}`" for a in sorted(asms, key=layer_key))
        lines.append(f"| `{short(d)}` | {label} | {len(by_dir[d])} |")
    return "\n".join(lines) + "\n"


def block_game_assets(m):
    by_dir = collections.defaultdict(collections.Counter)
    for f in m.game_files:
        if f.startswith(SCRIPTS + "/"):
            continue
        parts = f[len(GAME) + 1:].split("/")
        key = "/".join(parts[:min(3, len(parts) - 1)]) or "(root)"
        ext = os.path.splitext(f)[1].lower() or "(none)"
        by_dir[key][ext] += 1
    lines = ["| Folder (`Assets/_Game/`, depth 3; deeper files counted in their depth-3 folder) | Files by extension |",
             "|---|---|"]
    for d in sorted(by_dir, key=lambda s: s.lower()):
        exts = ", ".join(f"{n} `{e}`" for e, n in sorted(by_dir[d].items(), key=lambda kv: (-kv[1], kv[0])))
        lines.append(f"| `{d}` | {exts} |")
    return "\n".join(lines) + "\n"


def block_data(m):
    script_class = {}
    for f in m.cs:
        g = meta_guid(f)
        if g:
            script_class[g] = (f.rsplit("/", 1)[1][:-3], f)
    registry_guids = set(REF_RE.findall(read_text(ABILITY_REGISTRY))) if os.path.isfile(path(ABILITY_REGISTRY)) else set()
    rows = collections.defaultdict(list)  # (folder, class, script) -> [(asset, status)]
    for f in m.game_files:
        if not (f.startswith(DATA + "/") and f.endswith(".asset")):
            continue
        text = read_text(f)
        sm = SCRIPT_RE.search(text)
        cls, script = script_class.get(sm.group(1), (f"GUID:{sm.group(1)}", "")) if sm else ("(no script)", "")
        status = ""
        if "/Abilities/" in f:
            status = "retired" if re.search(r"^\s*Retired:\s*1\s*$", text, re.M) else (
                "registered" if meta_guid(f) in registry_guids else "not registered")
        rows[(f.rsplit("/", 1)[0][len(GAME) + 1:], cls, short(script))].append((f.rsplit("/", 1)[1], status))
    lines = ["| Folder (`Assets/_Game/`) | Class | Class file (`Scripts/`) | Assets (ability status) |", "|---|---|---|---|"]
    for (folder, cls, script), assets in sorted(rows.items(), key=lambda kv: (kv[0][0].lower(), kv[0][1].lower())):
        cells = ", ".join(f"`{a}`" + (f" ({s})" if s else "") for a, s in assets)
        lines.append(f"| `{folder}` | `{cls}` | `{script}` | {cells} |")
    statuses = collections.Counter(s for v in rows.values() for _, s in v if s)
    lines.append("")
    lines.append("Ability assets: " + ", ".join(f"{n} {s}" for s, n in sorted(statuses.items())) + ".")
    return "\n".join(lines) + "\n"


BLOCKS = collections.OrderedDict([
    ("assemblies", block_assemblies),
    ("script-folders", block_script_folders),
    ("game-assets", block_game_assets),
    ("data", block_data),
])


def generate_all():
    m = Model()
    return collections.OrderedDict((name, fn(m)) for name, fn in BLOCKS.items())


# ---------------------------------------------------------------- commands

def cmd_generate(_args):
    for name, text in generate_all().items():
        print(f"<!-- BEGIN GENERATED: {name} -->\n{text}<!-- END GENERATED: {name} -->\n")
    return 0


def cmd_check(args):
    doc_path = args.doc or path(DEFAULT_DOC)
    try:
        with open(doc_path, encoding="utf-8") as f:
            doc = f.read().replace("\r\n", "\n")
    except OSError as e:
        raise RepoError(f"cannot read the doc {doc_path}: {e.strerror}") from None
    fresh = generate_all()
    found = {mt.group(1): mt.group(2) for mt in BLOCK_RE.finditer(doc)}
    problems = []
    for name in found:
        if name not in fresh:
            problems.append(f"unknown generated block '{name}' (known: {', '.join(fresh)})")
    for name in fresh:
        if name not in found:
            problems.append(f"block '{name}' is missing from the doc (add its BEGIN/END markers)")
    stale = [n for n in fresh if n in found and found[n] != fresh[n]]
    if args.write and stale:
        doc = BLOCK_RE.sub(lambda mt: (f"<!-- BEGIN GENERATED: {mt.group(1)} -->\n{fresh[mt.group(1)]}"
                                       f"<!-- END GENERATED: {mt.group(1)} -->")
                           if mt.group(1) in fresh else mt.group(0), doc)
        with open(doc_path, "w", encoding="utf-8", newline="\n") as f:
            f.write(doc)
        print(f"rewrote {len(stale)} block(s) in {doc_path}: {', '.join(stale)}")
        stale = []
    for name in stale:
        problems.append(f"block '{name}' is out of date:\n" + "".join(difflib.unified_diff(
            found[name].splitlines(True), fresh[name].splitlines(True),
            fromfile=f"{os.path.basename(doc_path)} ({name})", tofile=f"generated ({name})", n=0)))
    if problems:
        print(f"{doc_path}: {len(problems)} problem(s). Fix by hand or run "
              f"`python -I tools/docs/file_structure.py check --write`.\n")
        print("\n".join(problems))
        return 1
    print(f"{doc_path}: {len(found)} generated block(s) match the working tree.")
    return 0


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("generate", help="print every generated block")
    check = sub.add_parser("check", help="compare (or with --write, rewrite) the generated blocks of the doc")
    check.add_argument("--doc", help=f"doc to check (default: {DEFAULT_DOC})")
    check.add_argument("--write", action="store_true", help="rewrite out-of-date blocks in place")
    args = parser.parse_args(argv)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    try:
        return cmd_generate(args) if args.command == "generate" else cmd_check(args)
    except RepoError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
