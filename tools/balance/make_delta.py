#!/usr/bin/env python
"""Turn a balance proposal into the delta to publish in UGS Remote Config key `balance`.

Usage:  python -I tools/balance/make_delta.py proposal.json   (or '-' for stdin)

Proposal (same shape as the delta, but values may equal the shipped ones):
  {"note": "why", "assets": {"Snatch": {"Cooldown": 3.5, "StealAmount": 5}}}

Output: the delta with only the values that DIFFER from the shipped .asset files, schema 1, ready to paste.
Reads the assets, never writes them. Exit 1 and a reason for an unknown asset, an unknown field, a deny-listed
field (mirror of BalanceBook.DeniedFields) or a non-scalar value. Enum fields are numeric in the .asset files, so
pass the enum NAME only if the shipped value cannot be compared: such a field is reported, not silently dropped.
Kits ("kits": {...}) are passed through unchanged: the app checks them against the registry, this tool cannot.
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / "Assets" / "_Game" / "Data"

# Mirror of BalanceBook.DeniedFields (Assets/_Game/Scripts/Balance/BalanceBook.cs). Keep in sync.
DENIED = {
    "AllowedClasses", "PeckSlotPreEquippedBy", "SignaturePreEquippedBy", "Category", "BotRole", "AccentKey",
    "TerrainTraversal", "JumpTier", "CastMotion", "Retired",   # AbilityBaseSO
    "Subclass",                                                  # PassiveAbilitySO
    "MaxPlayers", "TargetingMode", "LoadoutMode",                # MatchConfigSO
}

NAME = re.compile(r"^  m_Name: (.+)$", re.M)
FIELD = re.compile(r"^  ([A-Za-z_][A-Za-z0-9_]*): (-?[0-9.eE+-]+|true|false)\s*$", re.M)


def load_assets():
    assets = {}
    for p in DATA.rglob("*.asset"):
        text = p.read_text(encoding="utf-8", errors="replace")
        m = NAME.search(text)
        if not m:
            continue
        assets.setdefault(m.group(1).strip(), []).append((p, text))
    return assets


def number(token):
    return float(token)


def main():
    src = sys.stdin.read() if len(sys.argv) < 2 or sys.argv[1] == "-" else Path(sys.argv[1]).read_text(encoding="utf-8")
    prop = json.loads(src)
    problems, out_assets = [], {}
    assets = load_assets()

    for name, fields in (prop.get("assets") or {}).items():
        found = assets.get(name)
        if not found:
            problems.append(f"{name}: no asset with this m_Name under Assets/_Game/Data")
            continue
        if len(found) > 1:
            problems.append(f"{name}: {len(found)} assets share this name, the app refuses the whole delta")
            continue
        text = found[0][1]
        shipped = {m.group(1): m.group(2) for m in FIELD.finditer(text)}
        for field, value in fields.items():
            if field in DENIED:
                problems.append(f"{name}.{field}: deny-listed, the app skips it")
                continue
            if field not in shipped:
                problems.append(f"{name}.{field}: not a numeric/bool field of the asset (enums: edit the number in the .asset or ask senior-dev)")
                continue
            if isinstance(value, bool):
                same = shipped[field] == str(value).lower() or shipped[field] == str(int(value))
            elif isinstance(value, (int, float)):
                try:
                    same = abs(number(shipped[field]) - float(value)) < 1e-6
                except ValueError:
                    problems.append(f"{name}.{field}: shipped value {shipped[field]!r} is not a number")
                    continue
            else:
                problems.append(f"{name}.{field}: value must be a number or bool, got {value!r}")
                continue
            if not same:
                out_assets.setdefault(name, {})[field] = value

    if problems:
        print("PROBLEMS (nothing to publish until fixed):", file=sys.stderr)
        for p in problems:
            print("  - " + p, file=sys.stderr)
        return 1

    delta = {"schema": 1}
    if prop.get("note"):
        delta["note"] = prop["note"]
    if out_assets:
        delta["assets"] = out_assets
    if prop.get("kits"):
        delta["kits"] = prop["kits"]
    if "assets" not in delta and "kits" not in delta:
        print("No difference from the shipped assets: publish nothing (an empty key is the 'base' hash 5465b825).", file=sys.stderr)
        return 0
    print(json.dumps(delta, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
