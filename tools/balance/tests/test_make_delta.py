import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from support import BAL
import schema


def run_make_delta(proposal):
    """Run make_delta.py on a proposal against the shipped Data folder (read only); return (exit code, stdout, stderr)."""
    with tempfile.TemporaryDirectory(prefix="make-delta-test-") as tmp:
        path = Path(tmp) / "proposal.json"
        path.write_text(json.dumps(proposal), encoding="utf-8")
        done = subprocess.run([sys.executable, "-I", str(BAL / "make_delta.py"), str(path)], capture_output=True, text=True)
        return done.returncode, done.stdout, done.stderr


class MakeDeltaDenyTests(unittest.TestCase):
    def test_fresh_bite_share_is_deny_listed_like_the_app_does(self):
        # The game plays no food bite (Maestro 2026-11-25): BalanceBook.DeniedFields lists FreshBiteShare, and schema.DENIED mirrors it.
        self.assertIn("FreshBiteShare", schema.DENIED)
        rc, out, err = run_make_delta({"assets": {"Enforcer": {"FreshBiteShare": 0.5}}})
        self.assertEqual(rc, 1, out)
        self.assertIn("Enforcer.FreshBiteShare: deny-listed", err)

    def test_the_other_enforcer_fields_are_addressable(self):
        rc, out, err = run_make_delta({"assets": {"Enforcer": {"DebtSteal": 1, "FreshSeconds": 1}}})
        self.assertEqual(rc, 0, err)
        self.assertEqual(json.loads(out)["assets"]["Enforcer"], {"DebtSteal": 1, "FreshSeconds": 1})


if __name__ == "__main__":
    unittest.main()
