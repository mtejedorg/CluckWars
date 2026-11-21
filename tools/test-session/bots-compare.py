#!/usr/bin/env python3
"""BL-208: compare the per-seat banked food of several runs of the same seed and seats (different cw_timescale).

Usage: python -I tools/test-session/bots-compare.py LABEL=path/to/client1.log [LABEL=...]

Reads the MATCH SUMMARY blocks (one per round: "P3 (Warrior): Kills=0, Deposited=10.0, Stunned=0"), prints one row per round and
seat with the deposited food of every run and the spread (max - min), then the Invariant FAIL / Error counts of each log.
The spread is the number to report: it is what still varies between speeds, in food units, not a pass/fail verdict.
"""
import re
import sys

ROW = re.compile(r"^\s+(P\d) \((\w+)\): Kills=(\d+), Deposited=([\d.,]+), Stunned=(\d+)")


def summaries(path):
    rounds, cur = [], None
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            if "=== MATCH SUMMARY ===" in line:
                cur = {}
                rounds.append(cur)
                continue
            if cur is None:
                continue
            m = ROW.match(line)
            if m:
                cur[m.group(1) + "/" + m.group(2)] = (float(m.group(4).replace(",", ".")), int(m.group(3)))
            elif line.startswith("Winner Deposited") or line.startswith("["):
                cur = None
    return rounds


def count(path, needle):
    with open(path, encoding="utf-8", errors="replace") as f:
        return sum(1 for line in f if needle in line)


def main(argv):
    runs = []
    for a in argv:
        label, _, path = a.partition("=")
        runs.append((label, path, summaries(path)))
    rounds = min(len(r[2]) for r in runs)
    print("round seat".ljust(22) + "".join(l.rjust(10) for l, _, _ in runs) + "   spread")
    worst = 0.0
    for i in range(rounds):
        seats = sorted(set().union(*[set(r[2][i]) for r in runs]))
        for s in seats:
            vals = [r[2][i].get(s, (float("nan"), 0))[0] for r in runs]
            spread = max(vals) - min(vals)
            worst = max(worst, spread)
            print(f"{i + 1} {s}".ljust(22) + "".join(f"{v:10.1f}" for v in vals) + f"   {spread:6.1f}")
    print(f"\nlargest spread {worst:.1f} food over {rounds} round(s)")
    for label, path, _ in runs:
        print(f"{label}: Invariant FAIL lines={count(path, 'Invariant FAIL')}  Error lines={count(path, '[Error]')}  "
              f"Telemetry steal={count(path, '[Telemetry] steal')} peckcut={count(path, '[Telemetry] peckcut')} "
              f"kill={count(path, '[Telemetry] kill')} contact={count(path, '[Telemetry] contact')}")


if __name__ == "__main__":
    main(sys.argv[1:])
