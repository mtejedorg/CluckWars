#!/usr/bin/env python
"""Turn a published delta into the Kit Lab database documents it corresponds to (the inverse of kitlab_to_proposal.py).

Usage:  python -I tools/balance/delta_to_kitlab.py delta.json > docs.json   ('-' reads stdin)

Output: {"abilityvals": {id: {"values": {...}}}, "classstats": {Class: {"values": {...}}},
         "kits": {passiveId: {"passive": id, "slots": [...], "stats": {...}}}, "unsupported": [...]}
Class fields map back through MoveSpeed->move etc., passive multipliers back to percentages (CooldownMultiplier 0.75 ->
cdr 25), every other asset field to abilityvals. `unsupported` lists what the Kit Lab has no place for (MatchConfig, a
passive field other than the percentage ones). The documents hold only the edits; a clean database means no documents.
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import kitlab_map as km  # noqa: E402
import schema  # noqa: E402

INV_BASE = {v: k for k, v in km.BASEKEY.items()}
INV_PASSIVE = {m[0]: (k, m[2]) for k, m in km.PASSIVE.items()}


def convert(delta, assets=None):
    assets = assets if assets is not None else schema.load_assets()
    abilityvals, classstats, kits, unsupported = {}, {}, {}, []
    for name, fields in sorted((delta.get("assets") or {}).items()):
        lst = assets.get(name)
        a = lst[0] if lst else None
        is_class = a is not None and "Classes" in a.path.parts
        for field, v in sorted(fields.items()):
            if is_class and field in INV_BASE:
                classstats.setdefault(name, {"values": {}})["values"][INV_BASE[field]] = v
            elif a is not None and a.is_passive and field in INV_PASSIVE:
                key, inv = INV_PASSIVE[field]
                kits.setdefault(name, {"passive": name, "slots": schema.kit_of(a, schema.guid_names(assets)), "stats": {}})[
                    "stats"][key] = km.r4(inv(v))
            elif name == "MatchConfig" or is_class or (a is not None and a.is_passive):
                unsupported.append(f"{name}.{field}: the Kit Lab has no place for it")
            else:
                abilityvals.setdefault(name, {"values": {}})["values"][field] = v
    g = schema.guid_names(assets)
    for pid, slots in sorted((delta.get("kits") or {}).items()):
        p = (assets.get(pid) or [None])[0]
        doc = kits.setdefault(pid, {"passive": pid, "slots": list(slots), "stats": {}})
        doc["slots"] = list(slots)
    return {"abilityvals": abilityvals, "classstats": classstats, "kits": kits, "unsupported": unsupported}


def main(argv=None):
    argv = list(sys.argv[1:] if argv is None else argv)
    src = sys.stdin.read() if not argv or argv[0] == "-" else Path(argv[0]).read_text(encoding="utf-8")
    print(json.dumps(convert(json.loads(src)), indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
