#!/usr/bin/env python
"""Turn a balance proposal into the delta to publish in UGS Remote Config key `balance`.

Usage:  python -I tools/balance/make_delta.py proposal.json   (or '-' to read the proposal from stdin)

Proposal (same shape as the delta, values may equal the shipped ones):
  {"note": "why", "assets": {"Snatch": {"Cooldown": 3.5}}, "kits": {"Bully": ["Peck","Snatch","WingSlam","SpeedBurst"]}}

Output: the schema-1 delta with only what DIFFERS from the shipped .asset files, ready to paste. Exit 1 and PROBLEMS
when BalanceBook would refuse the whole delta or ignore an entry: unknown asset or field, deny-listed field, wrong type
(whole numbers, bool), outside [Range]/[Min], negative where the shipped value is not, past the magnitude bound, and kits
that FixedKit.Problems would refuse. Field types and limits are read from the C# sources (schema.py), the shipped values
from the YAML; nothing is written. Enum fields accept the member name (as BalanceBook does) and are not range-checked.
Not checked here: that a Death Mark kit holds a stun source (the app checks it).
"""
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import schema  # noqa: E402

CLASS_BITS = {1: "Warrior", 2: "Speedy", 4: "Fatty", 8: "Assassin"}


def magnitude_bound(base):
    return max(1000.0, 100.0 * abs(base))


def check_value(name, field, f, value, ship_txt, problems):
    """Return the value to keep (or None when equal to shipped / refused)."""
    where = f"{name}.{field}"
    if f.kind == "bool":
        if not isinstance(value, bool):
            problems.append(f"{where}: expects true or false, got {value!r}")
            return None
        if ship_txt is not None and ship_txt in ("true", "false", "0", "1") and value == (ship_txt in ("true", "1")):
            return None
        return value
    if f.kind == "enum":
        if isinstance(value, str):
            return value        # member name: BalanceBook resolves it; cannot be compared with the numeric shipped value here
        problems.append(f"{where}: enum field, pass the member name as a string (e.g. \"StaticAoE\")")
        return None
    if f.kind not in ("float", "int"):
        problems.append(f"{where}: not a number or bool field ({f.kind}), BalanceBook ignores it")
        return None
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        problems.append(f"{where}: expects a number, got {value!r}")
        return None
    v = float(value)
    if not math.isfinite(v):
        problems.append(f"{where}: {value!r} is not a finite number")
        return None
    if f.kind == "int" and v != int(v):
        problems.append(f"{where}: expects a whole number, got {value}")
        return None
    ship = float(ship_txt) if ship_txt not in (None, "true", "false") else None
    if ship is not None and v < 0 <= ship:
        problems.append(f"{where}: {value} is negative where the shipped value ({ship_txt}) is not")
        return None
    if abs(v) > magnitude_bound(ship if ship is not None else 0.0):
        problems.append(f"{where}: {value} is past the magnitude bound")
        return None
    if f.lo is not None and v < f.lo or f.hi is not None and v > f.hi:
        rng = f"[{f.lo if f.lo is not None else '-inf'}, {f.hi if f.hi is not None else 'inf'}]"
        problems.append(f"{where}: {value} is outside the field's allowed range {rng}")
        return None
    if ship is not None and abs(v - ship) < 1e-6:
        return None
    return int(v) if f.kind == "int" else value


def main():
    src = sys.stdin.read() if len(sys.argv) < 2 or sys.argv[1] == "-" else Path(sys.argv[1]).read_text(encoding="utf-8")
    try:
        prop = json.loads(src)
    except ValueError as e:
        print(f"PROBLEMS: the proposal is not valid JSON: {e}", file=sys.stderr)
        return 1
    if not isinstance(prop, dict):
        print("PROBLEMS: the proposal must be a JSON object", file=sys.stderr)
        return 1

    assets = schema.load_assets()
    live = schema.registry(assets)
    classes = {n for n, lst in assets.items() for a in lst if "Classes" in a.path.parts}
    addressable = live | classes | {"MatchConfig"}
    problems, out_assets, out_kits = [], {}, {}

    for name, fields in (prop.get("assets") or {}).items():
        lst = assets.get(name)
        if name not in addressable or not lst:
            why = "retired or not in AbilityRegistry, the app ignores it" if lst else "no asset with this m_Name"
            problems.append(f"{name}: {why}")
            continue
        if len(lst) > 1:
            problems.append(f"{name}: {len(lst)} assets share this name, the app refuses the whole delta")
            continue
        if not isinstance(fields, dict):
            problems.append(f"{name}: must be an object of field: value")
            continue
        a, flds = lst[0], lst[0].fields
        for field, value in fields.items():
            if field in schema.DENIED:
                problems.append(f"{name}.{field}: deny-listed, the app skips it")
            elif field not in flds:
                problems.append(f"{name}.{field}: not a public field of {a.cls or 'the asset class'}, the app ignores it")
            else:
                kept = check_value(name, field, flds[field], value, a.scalars.get(field), problems)
                if kept is not None:
                    out_assets.setdefault(name, {})[field] = kept

    g = schema.guid_names(assets)
    for owner, kit in (prop.get("kits") or {}).items():
        lst = assets.get(owner)
        if not lst or not lst[0].is_passive or owner not in live:
            problems.append(f"kits.{owner}: not a registered specialization (passive)")
            continue
        p = lst[0]
        shipped = schema.kit_of(p, g)
        if not isinstance(kit, list) or not all(isinstance(x, str) for x in kit):
            problems.append(f"kits.{owner}: must be an array of ability names")
            continue
        errs = []
        if len(kit) != len(shipped):
            errs.append(f"has {len(kit)} abilities, the shipped kit has {len(shipped)}")
        for i, n in enumerate(kit):
            al = assets.get(n)
            if not al or n not in live or al[0].is_passive:
                errs.append(f"[{i}] '{n}' is not a registered active ability")
            elif kit.index(n) != i:
                errs.append(f"'{n}' is in the kit twice")
        mask = int(float(p.scalars.get("AllowedClasses", 0)))
        cls = CLASS_BITS.get(mask)
        if cls is None:
            errs.append(f"AllowedClasses ({mask}) is not exactly one class")
        else:
            if cls != "Assassin" and "Peck" not in kit:
                errs.append(f"a {cls} must carry its Peck")
            if cls == "Assassin" and "Peck" in kit:
                errs.append("an Assassin cannot forage, so its kit must not hold a Peck")
            if cls == "Speedy":
                for n in kit:
                    al = assets.get(n)
                    if al and float(al[0].scalars.get("TerrainTraversal", 0)) != 0:
                        errs.append(f"Speedy kits grant no terrain traversal, but '{n}' has one")
        if int(float(p.scalars.get("Subclass", 0))) == 7 and "MarkKill" not in kit:
            errs.append("the Spoiler kit must hold MarkKill")
        if errs:
            problems.extend(f"kits.{owner}: {e}" for e in errs)
        elif kit != shipped:
            out_kits[owner] = kit

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
    if out_kits:
        delta["kits"] = out_kits
    if "assets" not in delta and "kits" not in delta:
        print("No difference from the shipped assets: publish nothing (no key, or an empty one, plays the shipped numbers).",
              file=sys.stderr)
        return 0
    print(json.dumps(delta, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
