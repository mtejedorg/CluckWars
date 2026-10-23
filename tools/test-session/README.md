# tools/test-session - multi-device test-session collection

Collection tooling for the session in `docs/TEST_SESSION_PLAN_2026-10.md`. No game code. Works in
PowerShell 7 and Windows PowerShell 5.1 (`powershell -ExecutionPolicy Bypass -File ...` if scripts are blocked).
Everything is UTC and lands in `Captures/session-<yyyyMMdd-HHmmss>/` (git-ignored).

| Script | What it does |
|---|---|
| `start.ps1` | session dir + `session.json`, `adb logcat -c`, background `logcat -v threadtime` -> `phone.log`, rolling `screenrecord` -> `screenrec/`, optional APK install, EXE clients (`client<i>.log`), phone launch with intent extras. `-DryRun`/`-WhatIf` prints the commands only. |
| `get-code.ps1` | prints the auto-host join code (`AutoTest code=XXXXXX`) from phone.log / client logs / Editor.log. `start.ps1 -WaitCode` does the same inline. |
| `stop.ps1` | finalizes the last video chunk, stops logcat, slices `Editor.log` (bytes written since start) to `editor.log`, runs `merge.ps1`. |
| `merge.ps1` | `merged.log` (one UTC timeline, per-source column) + `findings.txt` (Error/Warn, Exception, Perf hitches, Visual problems, cross-peer Sync mismatches / ghost chickens, build-SHA mismatch). |
| `shot.ps1` | `-Phone` / `-Desktop` / `-All` PNGs named with the UTC time so they line up with `merged.log`. |
| `selftest.ps1` | runs merge on `fixtures/session-fixture` and asserts the findings. |

Typical run: `start.ps1 ...` -> play -> `shot.ps1 -All` whenever something looks wrong -> `stop.ps1`.

Heuristics worth knowing (all documented at the top of `merge.ps1`): Sync fields must differ on 2
consecutive paired heartbeats to count; heartbeats pair by `t=` within +-3 ticks and 15 s wall clock (so a
pair count of 0 in the findings header means the peers never shared ticks - widen `-TickTolerance`);
Perf/Visual finding rules are keyed on the `Perf`/`Visual` source tags and the `p99=` / `*offenders*=N` tokens.
