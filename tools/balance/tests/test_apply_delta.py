import contextlib
import io
import json
import unittest

from support import DataCopyCase, OK
import apply_delta as ad

DELTA = {"schema": 1, "assets": {"Snatch": {"Cooldown": 3.5, "StealAmount": 6}, "Warrior": {"MoveSpeed": 11}},
         "kits": {"Bully": ["Peck", "Snatch", "WingSlam", "SpeedBurst"]}}


def run(delta_path, *flags, dirty=lambda p: []):
    out, err = [], io.StringIO()
    with contextlib.redirect_stderr(err):
        rc = ad.main([str(delta_path), *flags], validate=OK, dirty_check=dirty, out=out.append)
    return rc, "\n".join(out), err.getvalue()


class ApplyTests(DataCopyCase):
    def write_delta(self, d):
        p = self.tmp / "delta.json"
        p.write_text(json.dumps(d))
        return p

    def snapshot(self):
        return {p: p.read_bytes() for p in self.data.rglob("*") if p.is_file()}

    def test_dry_run_writes_nothing(self):
        before = self.snapshot()
        rc, out, _ = run(self.write_delta(DELTA))
        self.assertEqual(rc, 0)
        self.assertIn("DRY RUN", out)
        self.assertIn("+   Cooldown: 3.5", out)
        self.assertEqual(before, self.snapshot())

    def test_apply_changes_only_the_scalar_and_kit_lines(self):
        before = self.snapshot()
        rc, out, _ = run(self.write_delta(DELTA), "--apply")
        self.assertEqual(rc, 0, out)
        after = self.snapshot()
        changed = [p for p in before if before[p] != after[p]]
        self.assertEqual(sorted(p.name for p in changed), ["Bully.asset", "Snatch.asset", "Warrior.asset"])
        total = 0
        for p in changed:
            a, b = before[p].decode().splitlines(keepends=True), after[p].decode().splitlines(keepends=True)
            self.assertEqual(len(a), len(b))
            for x, y in zip(a, b):
                if x != y:
                    total += 1
                    if "guid" in x:
                        self.assertTrue(x.startswith("  - {fileID: 11400000, guid: "))
                    else:
                        self.assertEqual(x.split(":")[0], y.split(":")[0])
        self.assertEqual(total, 4)   # Cooldown, StealAmount, MoveSpeed, one Kit guid (Shadowstep -> SpeedBurst)
        self.assertIn("  Cooldown: 3.5\n", after[next(p for p in changed if p.name == "Snatch.asset")].decode())

    def test_enum_by_name_is_refused(self):
        rc, _, err = run(self.write_delta({"schema": 1, "assets": {"FeatherAura": {"AreaKind": "StaticAoE"}}}), "--apply")
        self.assertEqual(rc, 2)
        self.assertIn("by hand", err)

    def test_missing_scalar_line_is_refused(self):
        rc, _, err = run(self.write_delta({"schema": 1, "assets": {"Snatch": {"NoSuch": 1}}}))
        self.assertEqual(rc, 2)
        self.assertIn("NoSuch", err)

    def test_dirty_target_is_refused(self):
        self.git_init()
        target = self.data / "Abilities" / "Snatch.asset"
        target.write_bytes(target.read_bytes() + b"# dirty\n")
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            rc = ad.main([str(self.write_delta(DELTA)), "--apply"], validate=OK, out=lambda s: None)   # real git check
        self.assertEqual(rc, 2)
        self.assertIn("uncommitted", err.getvalue())
        self.assertIn(b"# dirty", target.read_bytes())

    def test_clean_repo_passes_the_git_check(self):
        self.git_init()
        rc = ad.main([str(self.write_delta(DELTA))], validate=OK, out=lambda s: None)
        self.assertEqual(rc, 0)


if __name__ == "__main__":
    unittest.main()
