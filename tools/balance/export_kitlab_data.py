#!/usr/bin/env python
"""Print the Kit Lab page's embedded data (`var G=...`) from the shipped .asset files and C# sources, so it cannot drift.

Usage:  python -I tools/balance/export_kitlab_data.py > g.json

Output: {"classes","abilities","passives","lim","ints"} (a passive carries `retired`; the page leaves a retired one out). `lim` = {asset: {field: [min|null, max|null]}} and `ints` =
{asset: [whole-number fields]} come from the [Range]/[Min] attributes and field types (schema.py), so the page can
refuse the values BalanceBook would refuse. Abilities: every non-passive ability asset, `inRegistry` says whether
AbilityRegistry.asset lists it; `trav` is its TerrainTraversal. Reads only; never writes.
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import schema  # noqa: E402

CAT = {0: "Steal", 1: "Control", 2: "Defense", 3: "Utility"}
CLASS_BITS = [(1, "Warrior"), (2, "Speedy"), (4, "Fatty"), (8, "Assassin")]
NOT_NUMS = {"AccentKey", "Category", "TerrainTraversal", "JumpTier", "AllowedClasses", "PeckSlotPreEquippedBy",
            "SignaturePreEquippedBy", "BotRole", "CastMotion", "Subclass", "SlotKind"}
CLASS_STATS = ("MoveSpeed", "TurnSpeed", "CargoCapacity", "PeckAmount", "PeckCooldown", "PeckCastSeconds", "Scale")


def num(v):
    return float(v == "true") if v in ("true", "false") else float(v)


def field(text, name):
    import re
    # Unity folds a long scalar over indented continuation lines: join them, or a long Description reads cut off.
    m = re.search(rf"^  {name}: (.*(?:\n    .*)*)$", text, re.M)
    v = " ".join(part.strip() for part in m.group(1).split("\n")).strip() if m else ""
    if v[:1] in "\"'" and v[-1:] == v[:1]:
        v = v[1:-1].replace('\\"', '"')
    return v


def main():
    assets = schema.load_assets()
    live = schema.registry(assets)
    g = schema.guid_names(assets)
    abilities, passives, classes, lim, ints = [], [], [], {}, {}

    def limits(a):
        fl = a.fields
        lim[a.name] = {k: [f.lo, f.hi] for k, f in fl.items() if f.kind in ("float", "int") and (f.lo is not None or f.hi is not None)}
        ints[a.name] = [k for k, f in fl.items() if f.kind == "int"]

    for name in sorted(assets):
        a = assets[name][0]
        if "Abilities" not in a.path.parts:
            continue
        sc = {k: num(v) for k, v in a.scalars.items() if not k.startswith("m_")}
        mask = int(sc.get("AllowedClasses", 0))
        nums = {k: v for k, v in sc.items() if k not in NOT_NUMS}
        cls = [n for bit, n in CLASS_BITS if mask & bit]
        if a.is_passive:
            passives.append({"id": name, "name": field(a.text, "DisplayName"), "desc": field(a.text, "Description"),
                             "cls": cls[0] if cls else "", "subclass": int(sc.get("Subclass", 0)), "nums": nums,
                             "retired": bool(int(sc.get("Retired", 0))),   # Relentless since 2026-11-25: the page shows no card for it
                             "kit": schema.kit_of(a, g)})
        else:
            abilities.append({"id": name, "name": field(a.text, "DisplayName"), "desc": field(a.text, "Description"),
                              "cat": CAT.get(int(sc.get("Category", 3)), "Utility"), "classes": cls, "nums": nums,
                              "trav": int(sc.get("TerrainTraversal", 0)), "inRegistry": name in live})
        if name in live:
            limits(a)

    for name in sorted(assets):
        a = assets[name][0]
        if "Classes" in a.path.parts:
            stats = {k: num(v) for k, v in a.scalars.items() if k in CLASS_STATS}
            classes.append({"id": name, "name": name, "stats": stats, "label": "Sneaky" if name == "Assassin" else name})
            limits(a)

    json.dump({"classes": classes, "abilities": abilities, "passives": passives, "lim": lim, "ints": ints},
              sys.stdout, separators=(",", ":"))
    print()


if __name__ == "__main__":
    main()
