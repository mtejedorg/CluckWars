# Test strategy — what we learned on 2026-10-20 and the rules that follow

Written after Maestro found by hand a bug (CW-221: *"the second match starts where the first one ended"*, solo) that a full
unattended multi-device session had run straight past. The bug was in our own logs for hours. This document is the
post-mortem and the standing rules. Tools: `tools/test-session/` (see its README).

## 1. What went wrong (the process, not only the code)

| # | Failure | Evidence |
|---|---|---|
| 1 | **Believed a log line instead of checking state.** `[Chicken] Teleported to (21.8, 0.05, -21.8)` was printed for the host's chicken; its position never changed. | `Sync` lines at every round ≥ 2 start |
| 2 | **Triaged only the categories I expected** (deposits, fps, hitches) and skipped `SYNC=93…266` that every report showed. The master chicken diverged across peers by 13 m. | findings.txt of every 4-seat run |
| 3 | **Never tested the most common player path.** Solo (Single mode vs 3 bots) was not run once; neither were rounds ≥ 2 of anything before the loop tooling existed. | no solo scenario existed |
| 4 | **Ignored a suspiciously clean number.** `Winner … total=15,0` four rounds in a row was written off as "autoplay is weak"; it was the end-of-round bonus, i.e. nothing had ever been banked (CW-216 — 185 rejected deposits). | merged.log |
| 5 | **Harness defects looked like game defects** (autoplay never foraged, two EXEs shared one UGS identity) and cost cycles; I only found them by reading code after the fact. | CW-209, autoplay rewrites |
| 6 | **Reported success on the metrics I had** (fps, reveal, hitches) while the core loop (score, round restart) was broken. A green dashboard of the wrong things. | session summaries |
| 7 | **Remote-peer-only behaviour had no coverage.** Everything before Phase 8 was verified with the Editor host + bots; objects without a `NetworkTransform` are only wrong on clients. | CW-216, CW-217 |

## 2. Standing rules

1. **Evidence is state, not logs.** A claim ("reset", "teleported", "banked") is only accepted when a *state* sample before and
   after shows it (positions, cargo, base totals, pile amounts, timers). Logs are for finding where to look.
2. **Every bug fix adds a rule.** A bug is not closed until an invariant (in `merge.ps1`, the in-game `Invariant` probe or an
   EditMode test) fails on the old build and passes on the new one. CW-221 → `INVARIANT round N start …` rules + self-test fixture.
3. **Zero triage debt.** Every category in `findings.txt` (`ERROR EXCEPTION SYNC GHOST INVARIANT`) must be 0, or each remaining
   item has a backlog entry saying why it is acceptable. `verify.ps1` turns this into PASS/FAIL (exit code) — nobody decides by
   skimming.
4. **Mode × round matrix, always.** Every scenario runs ≥ 3 rounds, because "round 1 is fine" proves nothing about reset code.
   Modes: `solo`, `shared2`, `shared4`, `hostleave` (`scenario-pc.ps1 -Mode …`). A change in GameManager / spawning / Fusion /
   round-reset code requires all four.
5. **Property: round N start == round 1 start.** Positions, cargo, bases, piles, cooldowns, timers must be identical. Cheap, generic,
   catches every "state leaks across rounds" bug without knowing what to look for.
6. **Cross-peer parity is a first-class check.** Same world on every peer: `Sync` heartbeat pairs + the `World` hash line. Any
   persistent divergence fails the run.
7. **Fix the harness first.** If a number looks wrong (0 food, nobody moves), prove the harness works (autoplay forages, joins
   succeed) before drawing conclusions about the game. Log harness decisions (`AutoPlay`, `AutoTest`) so this takes minutes.
8. **Suspiciously clean or repeated numbers are bugs until explained.** Identical totals, identical positions, zero counts.
9. **Don't narrow to what I was asked about.** When a report is produced, read all of it, then say what I did *not* check.
10. **Humans are the last layer, not the first.** Devices/eyes are for look-and-feel (visuals, haptics, layout) after layers 0–3
    are green.

## 3. Layers

| Layer | What | How | Gate |
|---|---|---|---|
| 0 | Pure rules | EditMode suite (`tests-run`/batchmode), ≈ 1 960 tests | green before any build |
| 1 | State invariants | in-game `Invariant` probe + `merge.ps1` INVARIANT rules (round-start corner/cargo/base/pile, quadrant uniqueness, round-N==round-1 piles) | 0 violations |
| 2 | Scenario matrix (PC only) | `scenario-pc.ps1 -Mode solo|shared2|shared4|hostleave -Rounds 3` | `VERDICT: PASS` |
| 3 | Differential | cross-peer `Sync` + `World` hash; SHA parity across peers | 0 SYNC/GHOST, one SHA |
| 4 | Device / human | phone smoke, visual sweep, haptics, notch, portrait/landscape | checklist in `docs/TEST_SESSION_PLAN_2026-10.md` §4 |

Build rule: build every platform from **one commit** (merge.ps1 flags SHA mismatch). CLI builds need the cloud project id (CW-200).

## 4. Checklists

**New or changed networked object** → does it have a `NetworkTransform` (or a stamped position)? Add it to the `World` hash; check
positions on a *client*, not only the host.

**New state transition (round end/start, respawn, match end)** → write down the expected state (positions, cargo, timers, cooldowns,
control states, bases, piles) and add it to `RoundInvariants`/`merge.ps1` before the code is "done".

**New RPC with authority checks** → run shared4 and grep for `rejected RPC`; a non-zero count with honest clients is a bug.

**Mode coverage** → solo / host / join / late-join / host-leave / reconnect. Missing mode = untested mode; say so in the report.

## 5. Commands

```powershell
.\tools\test-session\scenario-pc.ps1 -Mode solo      -Rounds 3   # exit 0 = PASS
.\tools\test-session\scenario-pc.ps1 -Mode shared4   -Rounds 3
.\tools\test-session\scenario-pc.ps1 -Mode hostleave -Rounds 3
.\tools\test-session\verify.ps1 -Session Captures\session-<utc>   # re-judge an old capture
.\tools\test-session\selftest.ps1                                  # tests the checker itself
```
