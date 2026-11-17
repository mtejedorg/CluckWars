import contextlib
import io
import json
import tempfile
import time
import unittest
from pathlib import Path

from support import DELTA, ENVIRON, OK, FakeHttp
import publish_delta as pd


def run(argv, http, environ=ENVIRON, validate=OK, history=None):
    lines = []
    err = io.StringIO()
    with contextlib.redirect_stderr(err):
        rc = pd.main(argv, http=http, environ=environ, validate=validate, history_dir=history, out=lines.append)
    return rc, "\n".join(lines), err.getvalue()


class HashTests(unittest.TestCase):
    def test_baseline_is_the_hash_of_empty_delta(self):
        self.assertEqual(pd.fnv1a32("{}"), "5465b825")

    def test_canonical_sorts_and_drops_note(self):
        d = {"schema": 1, "note": "x", "assets": {"B": {"Z": 1.5, "A": 2}, "A": {"F": True}}, "kits": {"K": ["a", "b"]}}
        self.assertEqual(pd.canonical(d), '{"assets":{"A":{"F":true},"B":{"A":2,"Z":1.5}},"kits":{"K":["a","b"]},"schema":1}')

    def test_float_tokens(self):
        self.assertEqual(pd.canonical({"assets": {"A": {"F": 0.1}}}), '{"assets":{"A":{"F":0.1}},"schema":1}')
        self.assertEqual(pd.canonical({"assets": {"A": {"F": 5.16}}}), '{"assets":{"A":{"F":5.16}},"schema":1}')

    def test_enum_by_name_has_no_offline_hash(self):
        self.assertIsNone(pd.expected_hash({"assets": {"A": {"K": "StaticAoE"}}}))


class PublishTests(unittest.TestCase):
    def setUp(self):
        self.hist = Path(tempfile.mkdtemp(prefix="hist-"))
        self.dfile = self.hist / "delta.json"
        self.dfile.write_text(json.dumps(DELTA))

    def test_dry_run_prints_and_changes_nothing(self):
        http = FakeHttp(other_settings=[{"key": "other", "type": "int", "value": 1}])
        rc, out, _ = run(["publish", str(self.dfile)], http, history=self.hist)
        self.assertEqual(rc, 0)
        self.assertIn("Target environment: dev", out)
        self.assertIn("Expected bal=", out)
        self.assertIn("+ Snatch.Cooldown = 3.5", out)
        self.assertIn("DRY RUN", out)
        self.assertEqual(http.puts(), [])
        self.assertEqual(list(self.hist.glob("latest-*")), [])

    def test_missing_variable_is_named_and_secret_not_printed(self):
        env = dict(ENVIRON)
        del env["UGS_PROJECT_ID"]
        rc, out, err = run(["publish", str(self.dfile)], FakeHttp(), environ=env, history=self.hist)
        self.assertEqual(rc, 2)
        self.assertIn("UGS_PROJECT_ID", err)
        self.assertNotIn("sekret", out + err)

    def test_production_apply_needs_the_human_flag(self):
        http = FakeHttp()
        rc, _, err = run(["publish", str(self.dfile), "--env", "production", "--apply"], http, history=self.hist)
        self.assertEqual(rc, 2)
        self.assertIn("--confirm-production", err)
        self.assertEqual(http.puts(), [])

    def test_apply_merges_writes_history_and_verifies(self):
        other = {"key": "other", "type": "int", "value": 7}
        http = FakeHttp(other_settings=[other])
        rc, out, _ = run(["publish", str(self.dfile), "--apply"], http, history=self.hist)
        self.assertEqual(rc, 0, out)
        self.assertEqual(len(http.puts()), 1)
        settings = http.configs["dev"]["value"]
        self.assertIn(other, settings)
        self.assertEqual([s for s in settings if s["key"] == "balance"], [{"key": "balance", "type": "json", "value": DELTA}])
        self.assertEqual(http.configs["production"]["value"], [other])
        entry = json.loads((self.hist / "latest-dev.json").read_text())
        self.assertEqual(entry["delta"], DELTA)
        self.assertEqual(entry["env"], "dev")
        self.assertEqual(entry["bal"], pd.expected_hash(DELTA))
        self.assertTrue(entry["verified"])
        self.assertEqual(len(list(self.hist.glob("2*-dev.json"))), 1)
        self.assertNotIn("sekret", (self.hist / "latest-dev.json").read_text() + out)

    def test_readback_mismatch_is_reported(self):
        class Lossy(FakeHttp):
            def request(self, method, url, headers, body=None):
                if method == "PUT":
                    body = json.dumps({"type": "settings", "value": [{"key": "balance", "type": "json", "value": {"schema": 1}}]}).encode()
                return super().request(method, url, headers, body)
        rc, _, err = run(["publish", str(self.dfile), "--apply"], Lossy(), history=self.hist)
        self.assertEqual(rc, 2)
        self.assertIn("differs", err)

    def test_fetch_prints_published_delta(self):
        rc, out, _ = run(["fetch", "--env", "dev"], FakeHttp(balance=DELTA), history=self.hist)
        self.assertEqual(rc, 0)
        self.assertIn('"Cooldown": 3.5', out)
        rc, out, _ = run(["fetch", "--env", "dev"], FakeHttp(), history=self.hist)
        self.assertIn("5465b825", out)

    def test_rollback_republishes_and_writes_a_new_entry(self):
        http = FakeHttp()
        run(["publish", str(self.dfile), "--apply"], http, history=self.hist)
        first = sorted(self.hist.glob("2*-dev.json"))[0]
        time.sleep(1.1)
        rc, _, _ = run(["rollback", str(first), "--apply"], http, history=self.hist)
        self.assertEqual(rc, 0)
        entries = sorted(self.hist.glob("2*-dev.json"))
        self.assertEqual(len(entries), 2)
        self.assertEqual(json.loads(entries[1].read_text())["action"], "rollback")

    def test_refuses_when_validation_fails(self):
        http = FakeHttp()
        rc, _, err = run(["publish", str(self.dfile), "--apply"], http, validate=lambda d: (False, "boom"), history=self.hist)
        self.assertEqual(rc, 2)
        self.assertIn("boom", err)
        self.assertEqual(http.puts(), [])

    def test_real_make_delta_validator_refuses_a_bad_delta(self):
        ok, msg = pd.validate_with_make_delta({"schema": 1, "assets": {"Snatch": {"NoSuchField": 1}}})
        self.assertFalse(ok)
        self.assertIn("NoSuchField", msg)

    def test_real_make_delta_validator_accepts_a_good_delta(self):
        ok, msg = pd.validate_with_make_delta(DELTA)
        self.assertTrue(ok, msg)


if __name__ == "__main__":
    unittest.main()
