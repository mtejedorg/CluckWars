"""A balance delta can carry the three pile-food amounts of MatchConfig (D1b: pile food is balance data)."""
import json
import subprocess
import sys
import unittest

from support import BAL


def make_delta(proposal):
    p = subprocess.run([sys.executable, "-I", str(BAL / "make_delta.py"), "-"], input=json.dumps(proposal),
                       capture_output=True, text=True)
    return p.returncode, p.stdout, p.stderr


class PileFoodDeltaTests(unittest.TestCase):
    def test_delta_can_carry_the_pile_food_fields(self):
        rc, out, err = make_delta({"assets": {"MatchConfig": {"PersonalPileFood": 12, "ContestedPileFood": 16, "CentrePileFood": 44}}})
        self.assertEqual(rc, 0, err)
        self.assertEqual(json.loads(out)["assets"]["MatchConfig"],
                         {"PersonalPileFood": 12, "ContestedPileFood": 16, "CentrePileFood": 44})

    def test_negative_food_is_refused(self):
        rc, _, err = make_delta({"assets": {"MatchConfig": {"CentrePileFood": -1}}})
        self.assertEqual(rc, 1)
        self.assertIn("CentrePileFood", err)


if __name__ == "__main__":
    unittest.main()
