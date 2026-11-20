import unittest

from support import DataCopyCase
import kitlab_to_proposal as k2p
import delta_to_kitlab as d2k

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
                      "Slippery": {"passive": "Slippery", "stats": {"slowIn": 60}},
                      "Relentless": {"passive": "Relentless", "stats": {"ccOut": 130}}}}
        a = k2p.convert(d)["assets"]
        self.assertEqual(a["Bulwark"], {"DurationMultiplier": 0.65, "KnockbackMultiplier": 0.25})
        self.assertEqual(a["Featherfoot"], {"DepositRateMultiplier": 1.1})
        self.assertEqual(a["Slippery"], {"Resist": 0.6})
        self.assertEqual(a["Relentless"], {"OutgoingDurationMultiplier": 1.3})

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

    def test_match_config_is_unsupported(self):
        docs = d2k.convert({"schema": 1, "assets": {"MatchConfig": {"FoodTargetToWin": 50}}})
        self.assertEqual(len(docs["unsupported"]), 1)


if __name__ == "__main__":
    unittest.main()
