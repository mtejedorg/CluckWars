#!/usr/bin/env python
"""Turn a dump of the Kit Lab database into a proposal for make_delta.py.

Usage:  python -I tools/balance/kitlab_to_proposal.py dump.json [--note TEXT] > proposal.json   ('-' reads stdin)

Input (the artifact database collections, document id -> document):
  {"abilityvals": {"Snatch": {"values": {"Cooldown": 3.5}}},
   "classstats":  {"Warrior": {"values": {"move": 11}}},
   "kits":        {"Bully": {"passive": "Bully", "slots": ["Peck","Snatch","WingSlam","Shadowstep"], "stats": {"cdr": 30}}}}
Output: {"note", "assets", "kits", "needsBuild": [...], "leftOut": [...]}; make_delta.py reads note/assets/kits and ignores
the rest. Same mapping as the page's buildDelta (kitlab_map.py): class base stats move/carry/peckAmt/peckCd/peckCast/turn/size ->
MoveSpeed/CargoCapacity/PeckAmount/PeckCooldown/PeckCastSeconds/TurnSpeed/Scale; passive percentages cdr/steal/cc/slowIn/kb/bank/ccOut ->
CooldownMultiplier=1-v/100, StealMultiplier=1+v/100, DurationMultiplier, Resist, KnockbackMultiplier,
DepositRateMultiplier, OutgoingDurationMultiplier = v/100; a slot swap with an unchanged passive -> `kits`.
"needsBuild": passive swaps, NEW (custom:) abilities and retired moves, which a delta cannot carry. Deviation from the page:
the page silently leaves out a value that fails a limit; here make_delta.py refuses the whole proposal and says why.
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import kitlab_map as km  # noqa: E402
import schema  # noqa: E402


def convert(dump, assets=None, note=None):
    assets = assets if assets is not None else schema.load_assets()
    live = schema.registry(assets)
    g = schema.guid_names(assets)
    out_assets, out_kits, build, left = {}, {}, [], []

    for aid, doc in sorted((dump.get("abilityvals") or {}).items()):
        for key, v in sorted(((doc or {}).get("values") or {}).items()):
            lst = assets.get(aid)
            if not lst or aid not in live:
                build.append(f"{aid} is retired or not in the registry: its values cannot be set from a delta")
            elif not km.is_num(v):
                left.append(f"{aid}.{key}: {v!r} is not a number")
            else:
                out_assets.setdefault(aid, {})[key] = v

    for cls, doc in sorted((dump.get("classstats") or {}).items()):
        for k, v in sorted(((doc or {}).get("values") or {}).items()):
            field = km.BASEKEY.get(k)
            if not field:
                continue
            ship = ((assets.get(cls) or [None])[0]).scalars.get(field) if assets.get(cls) else None
            if ship is None:
                build.append(f"{cls} has no shipped {field}, so it cannot be set from a delta")
            elif km.is_num(v):
                out_assets.setdefault(cls, {})[field] = v

    for pid, doc in sorted((dump.get("kits") or {}).items()):
        doc = doc or {}
        p = (assets.get(pid) or [None])[0]
        if p is None or not p.is_passive:
            continue
        label = f"{pid}"
        if doc.get("passive", pid) != pid:
            build.append(f"{label}: passive swapped to {doc.get('passive')} (needs a build)")
            continue
        slots = doc.get("slots")
        if slots and slots != schema.kit_of(p, g):
            bad = [s for s in slots if s.startswith("custom:") or s not in live]
            if bad:
                left.append(f"{label} kit not published: {', '.join(bad)} is a NEW or retired move")
            else:
                out_kits[pid] = list(slots)
        for sk, v in sorted((doc.get("stats") or {}).items()):
            m = km.PASSIVE.get(sk)
            if not m or not km.is_num(v):
                continue
            if p.scalars.get(m[0]) is None:
                build.append(f"{label}: {sk} edit has no matching shipped field ({m[0]}), so it needs a build")
                continue
            out_assets.setdefault(pid, {})[m[0]] = km.r4(m[1](v))

    for cid, c in sorted((dump.get("custom") or {}).items()):
        build.append(f"NEW {'passive' if (c or {}).get('cat') == 'Passive' else 'ability'} \"{(c or {}).get('name', cid)}\" needs a build")

    prop = {"note": note or "Kit Lab edit"}
    if out_assets:
        prop["assets"] = out_assets
    if out_kits:
        prop["kits"] = out_kits
    prop["needsBuild"] = build
    prop["leftOut"] = left
    return prop


def main(argv=None):
    argv = list(sys.argv[1:] if argv is None else argv)
    note = None
    if "--note" in argv:
        i = argv.index("--note")
        note = argv[i + 1]
        del argv[i:i + 2]
    src = sys.stdin.read() if not argv or argv[0] == "-" else Path(argv[0]).read_text(encoding="utf-8")
    print(json.dumps(convert(json.loads(src), note=note), indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
