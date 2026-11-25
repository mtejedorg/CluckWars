import unittest

from support import DataCopyCase
import json

import kitlab_map as km
import kitlab_to_proposal as k2p
import delta_to_kitlab as d2k
from test_make_delta import run_make_delta

DUMP = {
    "abilityvals": {"Snatch": {"values": {"Cooldown": 3.5}}, "Ambush": {"values": {"Cooldown": 2}}},
    "classstats": {"Warrior": {"values": {"move": 11, "carry": 16}}},
    "kits": {
        "Bully": {"passive": "Bully", "slots": ["Peck", "Shadowstep", "WingSlam", "SpeedBurst"], "stats": {"cdr": 30}},
        "Thief": {"passive": "Thief", "slots": ["Snatch", "Shadowstep", "Invisibility", "FeatherAura"], "stats": {"steal": 50}},
        "Relentless": {"passive": "Bulwark", "slots": ["Peck", "SpeedBurst", "Slow", "Snatch"]},
    },
    "custom": {"c1": {"name": "Egg Toss", "cat": "Control"}},
}


class KitLabTests(DataCopyCase):
    def test_proposal_mapping(self):
        p = k2p.convert(DUMP)
        self.assertEqual(p["assets"]["Snatch"], {"Cooldown": 3.5})
        self.assertEqual(p["assets"]["Warrior"], {"MoveSpeed": 11, "CargoCapacity": 16})
        self.assertAlmostEqual(p["assets"]["Bully"]["CooldownMultiplier"], 0.7)
        self.assertAlmostEqual(p["assets"]["Thief"]["StealMultiplier"], 1.5)
        self.assertEqual(p["kits"], {"Bully": ["Peck", "Shadowstep", "WingSlam", "SpeedBurst"]})
        needs = " ".join(p["needsBuild"])
        self.assertIn("Ambush", needs)
        self.assertIn("passive swapped", needs)
        self.assertIn("Egg Toss", needs)

    def test_passive_percentages(self):
        d = {"kits": {"Bulwark": {"passive": "Bulwark", "stats": {"cc": 65, "kb": 25}},
                      "Featherfoot": {"passive": "Featherfoot", "stats": {"bank": 110}},
                      "Slippery": {"passive": "Slippery", "stats": {"slowIn": 60}}}}
        a = k2p.convert(d)["assets"]
        self.assertEqual(a["Bulwark"], {"DurationMultiplier": 0.65, "KnockbackMultiplier": 0.25})
        self.assertEqual(a["Featherfoot"], {"DepositRateMultiplier": 1.1})
        self.assertEqual(a["Slippery"], {"Resist": 0.6})
        # Relentless left the roster on 2026-11-25: its ccOut mapping still exists (the asset is kept), but an edit cannot become a delta.
        self.assertEqual(km.PASSIVE["ccOut"][0], "OutgoingDurationMultiplier")
        self.assertAlmostEqual(km.PASSIVE["ccOut"][1](130), 1.3)

    def test_the_retired_relentless_goes_to_needs_build_not_into_the_delta(self):
        p = k2p.convert({"kits": {"Relentless": {"passive": "Relentless", "stats": {"ccOut": 130}}}})
        self.assertNotIn("Relentless", p.get("assets", {}))
        self.assertTrue(any("Relentless" in n and "retired" in n for n in p["needsBuild"]), p["needsBuild"])
        rc, out, err = run_make_delta({"assets": {"Relentless": {"OutgoingDurationMultiplier": 1.3}}})
        self.assertEqual(rc, 1)
        self.assertIn("Relentless: retired or not in AbilityRegistry", err)

    def test_reverse_roundtrip(self):
        p = k2p.convert(DUMP)
        delta = {"schema": 1, "assets": p["assets"], "kits": p["kits"]}
        docs = d2k.convert(delta)
        self.assertEqual(docs["abilityvals"]["Snatch"]["values"], {"Cooldown": 3.5})
        self.assertEqual(docs["classstats"]["Warrior"]["values"], {"move": 11, "carry": 16})
        self.assertEqual(docs["kits"]["Bully"]["stats"], {"cdr": 30})
        self.assertEqual(docs["kits"]["Bully"]["slots"], ["Peck", "Shadowstep", "WingSlam", "SpeedBurst"])
        self.assertEqual(docs["kits"]["Thief"]["stats"], {"steal": 50})
        self.assertEqual(docs["unsupported"], [])
        again = k2p.convert(docs)
        self.assertEqual(again["assets"], p["assets"])
        self.assertEqual(again["kits"], p["kits"])

    WARRIOR_KITS = {
        "Bully": {"peckSlow": 50, "hitStun": 3, "hitShove": 4, "hitLock": 5},
        "Enforcer": {"peckSlow": 30, "fresh": 1, "debtSteal": 3, "debtCap": 2, "debtSecs": 8},
    }
    WARRIOR_FIELDS = {
        "Bully": {"PeckCooldownMultiplier": 1.5, "HitStealStun": 3, "HitStealShove": 4, "HitStealLockSeconds": 5},
        "Enforcer": {"PeckCooldownMultiplier": 1.3, "FreshSeconds": 1, "DebtSteal": 3, "DebtCap": 2, "DebtSeconds": 8},
    }

    def test_warrior_kit_fields_round_trip_for_every_key(self):
        dump = {"kits": {k: {"passive": k, "stats": dict(v)} for k, v in self.WARRIOR_KITS.items()}}
        p = k2p.convert(dump)
        self.assertEqual(p["needsBuild"], [])
        for name, fields in self.WARRIOR_FIELDS.items():
            for field, value in fields.items():
                self.assertAlmostEqual(p["assets"][name][field], value, msg=f"{name}.{field}")
        docs = d2k.convert({"schema": 1, "assets": p["assets"]})
        self.assertEqual(docs["unsupported"], [])
        for name, stats in self.WARRIOR_KITS.items():
            self.assertEqual(docs["kits"][name]["stats"], stats, name)
        again = k2p.convert(docs)
        self.assertEqual(again["assets"], p["assets"])

    def test_every_new_stat_key_is_covered_by_the_round_trip(self):
        new_keys = {"peckSlow", "hitStun", "hitShove", "hitLock", "fresh", "debtSteal", "debtCap", "debtSecs"}
        covered = {k for stats in self.WARRIOR_KITS.values() for k in stats}
        self.assertEqual(new_keys, covered)
        for key in new_keys:
            self.assertIn(key, km.PASSIVE)
        self.assertNotIn("FreshBiteShare", [m[0] for m in km.PASSIVE.values()], "the deny-listed field has no Kit Lab key")

    def test_a_kit_lab_edit_of_the_bully_take_or_the_enforcer_debt_steal_is_a_valid_delta(self):
        dump = {"kits": {"Bully": {"passive": "Bully", "stats": {"hitStun": 3}},
                         "Enforcer": {"passive": "Enforcer", "stats": {"debtSteal": 3}}}}
        proposal = k2p.convert(dump, note="G8 check")
        rc, out, err = run_make_delta(proposal)
        self.assertEqual(rc, 0, err)
        delta = json.loads(out)
        self.assertEqual(delta["assets"], {"Bully": {"HitStealStun": 3}, "Enforcer": {"DebtSteal": 3}})

    def test_an_edit_that_equals_the_shipped_value_leaves_no_delta(self):
        # Bully's shipped stun take is 2 and the Enforcer's Peck slowdown 20 percent: typing them back changes nothing.
        proposal = k2p.convert({"kits": {"Bully": {"passive": "Bully", "stats": {"hitStun": 2}},
                                         "Enforcer": {"passive": "Enforcer", "stats": {"peckSlow": 20}}}})
        rc, out, err = run_make_delta(proposal)
        self.assertEqual(rc, 0, err)
        self.assertEqual(out.strip(), "", "nothing differs from the shipped assets: nothing to publish")

    def test_peck_cooldown_multiplier_min_is_read_from_the_named_constant(self):
        import schema
        f = schema.class_fields("EnforcerPassiveSO")["PeckCooldownMultiplier"]
        self.assertEqual((f.kind, f.lo), ("float", 0.25))   # [Min(PeckCastRules.MinCooldownMultiplier)]: BalanceBook refuses less
        rc, out, err = run_make_delta({"assets": {"Bully": {"PeckCooldownMultiplier": 0.1}}})
        self.assertEqual(rc, 1)
        self.assertIn("outside the field's allowed range", err)

    def test_the_export_marks_the_retired_passive_and_gives_the_new_fields_their_limits(self):
        import io
        import contextlib
        import export_kitlab_data as ex
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            ex.main()
        g = json.loads(buf.getvalue())
        retired = {p["id"]: p["retired"] for p in g["passives"]}
        self.assertTrue(retired["Relentless"])
        self.assertFalse(retired["Enforcer"])
        self.assertFalse(retired["Bully"])
        self.assertEqual(g["lim"]["Enforcer"]["DebtSeconds"][0], 0.5)
        self.assertIn("DebtCap", g["ints"]["Enforcer"])
        self.assertEqual(g["lim"]["Bully"]["HitStealStun"][0], 0.0)
        enforcer = next(p for p in g["passives"] if p["id"] == "Enforcer")
        self.assertTrue(enforcer["desc"].rstrip().endswith("food."), "the folded Description is joined, not cut at its first line")

    def test_the_page_mirrors_the_python_mapping_and_the_deny_list(self):
        import re
        import schema
        from pathlib import Path
        page = (Path(__file__).resolve().parents[1] / "kitlab" / "kitlab.template.html").read_text(encoding="utf-8")
        passive = re.search(r"var DP_PASSIVE=\{(.*?)\};", page, re.S).group(1)
        for key, (field, _, _) in km.PASSIVE.items():
            self.assertIn(f"{key}:['{field}'", passive, f"the page's DP_PASSIVE lacks {key} -> {field}")
        deny = set(re.findall(r"(\w+):1", re.search(r"var DP_DENY=\{(.*?)\};", page).group(1)))
        self.assertEqual(deny, set(schema.DENIED), "DP_DENY must mirror BalanceBook.DeniedFields (schema.DENIED)")
        order = re.search(r"var ORDER=\[(.*?)\];", page).group(1)
        self.assertIn("'Enforcer'", order)
        self.assertNotIn("Relentless", order, "the retired passive has no card")
        for key in ("peckSlow", "hitStun", "hitShove", "hitLock", "fresh", "debtSteal", "debtCap", "debtSecs"):
            self.assertIn("{k:'" + key + "'", page, f"the stats panel has no row for {key}")

    def test_peck_cast_maps_both_ways(self):
        p = k2p.convert({"classstats": {"Fatty": {"values": {"peckCast": 0.6}}}})
        self.assertEqual(p["assets"]["Fatty"], {"PeckCastSeconds": 0.6})
        docs = d2k.convert({"schema": 1, "assets": p["assets"]})
        self.assertEqual(docs["classstats"]["Fatty"]["values"], {"peckCast": 0.6})

    def test_peck_cast_field_range_is_read_from_the_source(self):
        import schema
        f = schema.class_fields("ChickenStatsSO")["PeckCastSeconds"]
        self.assertEqual(f.kind, "float")
        self.assertEqual((f.lo, f.hi), (0.2, 1.2))

    def test_match_config_is_unsupported(self):
        docs = d2k.convert({"schema": 1, "assets": {"MatchConfig": {"FoodTargetToWin": 50}}})
        self.assertEqual(len(docs["unsupported"]), 1)


if __name__ == "__main__":
    unittest.main()
