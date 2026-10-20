# Multi-device test session — plan (drafted 2026-10-20)

Devices: **Pixel 9** (APK, adb `57080DLAQ0030B`) · **Windows EXE** client(s) · **Unity Editor** (host, console + MCP).
Goal: Epic 1 of `docs/BACKLOG.md` (CW-100…109) with the emphasis on **visual integrity and glitch-hunting**. Every finding goes to `docs/BACKLOG.md` §9 as `CW-2xx`.

---

## 1. Preflight findings (state on 2026-10-20)

| Finding | Consequence |
|---|---|
| `Builds/Android/CluckWars-1.0.apk` is from **Oct 17 19:50**; `Builds/Windows/CluckWars.exe` from **Oct 13**. HEAD (`f0cdcbf`) includes the Oct 19 arena-reveal gate. | **Both builds are stale — rebuild both from the commit under test**, otherwise CW-102/103 can't be tested at all. |
| `adb devices` is empty right now. | Expected until the phone is plugged in (USB debugging + unlock + "Always allow"). |
| `ai-game-developer` MCP: `ECONNREFUSED`; `Temp/UnityLockfile` exists. | Editor is closed (stale lock) or still loading. Fallback: JSON-RPC over HTTP (memory: *unity-mcp-bridge-over-http*). Remember the MCP saturation after Android builds → use `Android + Restore Windows Target`. |
| `ProjectContext` log level = `Debug` (1). | Good for the session. Do not drop to `Warn`; use `Verbose` only per-bug. |
| `UnityLogService` stamps `[mm:ss.fff]` since **process start**. | **Logs from three devices cannot be merged on a timeline.** Fix in §2 (biggest enabler). |
| No frame-hitch logger, no periodic state heartbeat, no render-integrity probe. `DebugHud` is IMGUI/F1 only (a phone has no F1). | Add in §2. |
| adb taps are unreliable (landscape app on a ROTATION_0 panel). | Menu navigation on the phone cannot be driven by me. See §2 item 7 (auto-join) — otherwise you tap menus, I do everything else. |

---

## 2. Instrumentation to add (dev/diagnostic only, behind Developer Mode / `Debug` log level, `ILogService` only)

1. **Session stamp + wall clock.** Prefix every line with UTC wall time (`HH:mm:ss.fff`) and, once at boot, log one `Session` line: build SHA, platform, device model, GPU, graphics API, quality tier, Performance Mode, resolution, refresh rate, battery temp where available. This is what lets me merge phone + EXE + Editor logs by time. *(`UnityLogService`, `ProjectInstaller`.)*
2. **Hitch detector** (`PerfProbe`, tag `Perf`): per-frame check on `Time.unscaledDeltaTime`; log any frame > 50 ms (and > 100 ms as Warn) with match state, current phase (reveal / intro / active), active ability count, GC alloc delta. Plus a **1 Hz-at-Debug / 5 s summary line**: avg/p95/p99 frame ms, CPU/GPU `FrameTimingManager` if supported, mem, GC count. This is the CW-101/102 evidence.
3. **State heartbeat** (tag `Sync`) every 2 s per peer: `GameManager.State`, time remaining, tick, each chicken's `PlayerId / pos(x,z) / HP / cargo / control state`, each base's food total, pile amounts. Diffing the heartbeats across peers finds desyncs, ghost chickens, cargo/score drift (CW-103, CW-109).
4. **Visual-integrity probe** (tag `Visual`), run at: end of reveal, then every 10 s, and on demand (F-key / 3-finger tap): scan live `Renderer`s for null / error shaders / `Hidden/InternalErrorShader`, materials whose shader `isSupported == false`, magenta-fallback names, NaN/Inf transforms, renderers outside the arena bounds, chickens with 0 active renderers, camera count / culling mask / cover state. Log counts plus offenders' hierarchy paths. Catches pink/cyan (CW-104) objectively instead of by eye.
5. **Ready-gate + transition breadcrumbs** (extend `ArenaReadiness`, `MatchOverlays`): log every reveal step with timings (already partly there), cover on/off, GET READY hold duration, late-join covered time, `RenderTierPolicy` decisions, Reduced Motion state. Add `Reveal` first-use markers for hit spark / invisibility / fence hole / telegraph so a hitch can be attributed to a first-use.
6. **On-device debug overlay for touch** (reuse `DebugHud`): a Developer-Mode toggle (corner tap, 5×) instead of F1, plus lines for FPS p95, hitch count, `Visual` offender count, peer RTT/packet loss. Lets me read the phone screen via `screencap`.
7. **Auto-join for the phone** (Developer Mode only): intent extras `cw_autojoin=<CODE> cw_class=<X>` → skips menu taps (`am start … -e cw_autojoin ABC123`). Removes the adb-tap problem entirely. Guarded so production builds ignore it. *(Needs your OK — it is new code in the menu path.)*
8. **Network quality line** (tag `Fusion`): RTT, loss, resim/ interpolation stats every 5 s; log `OnPlayerJoined/Left`, master change, runner shutdown reason with the reveal state at that moment.

All additions are read-only (no gameplay writes), pass through `ILogService`, and get EditMode tests where pure (hitch classifier, stamp formatter, integrity-scan rules).

**Collection scripts (no game code):** `tools/test-session/start.ps1` — sets `$ADB/$DEV`, `adb logcat -c`, starts logcat → `Captures/session-<ts>/phone.log`, `adb shell screenrecord` rolling 3-min chunks, EXE clients via `run-clients.ps1`, and `merge.ps1` that merges the three logs by wall-clock into `merged.log` and greps `Error|Exception|Warn|Perf|Visual` into `findings.txt`.

---

## 3. Session timeline (≈ 2.5 h)

| # | Phase | Who | Pass criteria |
|---|---|---|---|
| P0 | **Gate:** EditMode suite (1899), console clean, build SHA recorded | me | all green |
| P1 | **Builds:** Windows EXE + Android APK from the same SHA (`Android + Restore Windows Target`); `adb install -r`; confirm SHA appears in the `Session` lines of all 3 clients | me | same SHA on all three |
| P2 | **Phone solo smoke** (10 min): joystick, 4 hexes, camera follow, notch/safe-area, menu layouts (CW-108). `screenrecord` running. | you tap, I capture | FPS p95 within 30-fps budget, no `Visual` offenders |
| P3 | **Reveal gate on device** (CW-102): 5 cold starts + 5 restarts; read `ArenaReadiness` times; look for first-use hitches | me from logs | no hitch > 100 ms after cover drops; cover over touch HUD |
| P4 | **Editor host + phone join** (coop-test Phases 1–3): reveal sync with a joiner still warming (CW-103), late-join into Active, movement both ways, `Sync` heartbeats agree ≤ 1 batch | me + your join taps (or auto-join) | heartbeats agree, joiner stays covered until ready |
| P5 | **4-seat match: Editor + phone + 2 EXEs**: full match to win and to the 45 s timer; every ability on every class once; Assassin execute credit (CW-050); defensive combo stacks (CW-113) | you drive EXEs, I watch logs | no desync, no `Error`, scores equal on all 4 |
| P6 | **Visual sweep** (§4) on phone + EXE + Editor, both quality tiers, Performance Mode on/off (CW-101/104/105/106) | me | checklist below |
| P7 | **Abuse / fault pass:** host leaves mid-match (master migration), phone backgrounded 10 s then resumed, airplane-mode toggle, screen rotation lock, Android Back, low battery saver, lock screen mid-match, rapid ability spam, wrong/lowercase code, 5th client refused | me + you | clean recovery or clean `SESSION ENDED` → Bootstrap |
| P8 | **Soak:** 10 min back-to-back matches on the phone, watch temp/FPS decay and memory (CW-106) | me | no growth in mem, FPS stable |
| P9 | **Triage:** merged log → `findings.txt`; each issue gets repro, side, SHA, evidence path → BACKLOG §9 | me | every finding has a CW-2xx id |

---

## 4. Visual-integrity protocol

Evidence per check: `adb exec-out screencap` (phone), `screenshot-game-view` (Editor), window capture (EXE), plus `screenrecord` for anything transient. Capture the **same moment** on all devices by command timestamp.

Checklist (each at the match camera, at the 4 corners and centre pile):
- **Shaders/materials:** no pink/cyan/black on ghost, fence holes, haze, grass detail, mound occlusion, ring ghost, stencil mark (CW-104); `Visual` probe offenders = 0.
- **Shadows:** bias/acne, peter-panning, shimmer when the camera moves (CW-105); Performance Mode swap (CW-106).
- **Reveal:** no flash of un-built arena before cover drops; cover over joystick/hexes; no black frame at GO; Reduced Motion variant.
- **Chickens:** feet height, pose at 3/2/GO (CW-006), nameplate orientation (CW-029), cargo on body, rival ring + edge arrows (stacking, HUD zones — CW-022), hit flash/stop, invisibility/ghost, doppelganger grey.
- **HUD/UI:** notch & safe area, no element under the cut-out, text clipping at long code, focus ring after touch, scale at 20:9, no overlap of timer/score/status strip.
- **Fences/props:** hairline cracks (CW-030), z-fighting, popping LOD/fence-fade, scarecrow vs coop, rim path.
- **Transitions:** menu → game → end overlay → restart → Bootstrap: no stale objects, no double VFX, no leftover zones/decoys.
- **Cross-peer parity:** same object looks the same on all devices at the same instant (positions, scores, animation state).
- **Perf-correlated glitches:** every hitch in `Perf` is matched to the screen recording to see whether it was visible.

---

## 5. Bug capture rules

- IDs continue from `CW-200` in a new `## 9. Test session 2026-10-20 findings` table in `docs/BACKLOG.md` (Type Bug / Pri / Title / Repro / Evidence path / Side(s) / SHA).
- P1 = crash, desync, unplayable, or reveal/visual break on the phone; P2 = visible glitch with workaround; P3 = polish.
- Existing tickets get **verified/failed notes** instead of duplicates (e.g. "CW-102 PASS on Pixel 9, SHA …").
- No fixes during the session unless a blocker; fixes are scheduled afterwards via `code-architect` → `senior-dev`.

---

## 6. Needs from you (afternoon)

1. Phone: USB debugging on, **screen timeout long / stay awake**, battery > 50 %, unlocked, accept the adb prompt, same Wi-Fi/internet as the PC. Charge via the PC cable.
2. Decide: OK to add **auto-join intent extras** (§2.7)? Otherwise you tap the phone menus.
3. How many people/hands: can you drive the 2 EXE clients and phone menus while I read logs? (Else P5 is 3 seats: Editor + phone + 1 EXE.)
4. Confirm the commit under test is `develop` @ `f0cdcbf` (I'd tag the session SHA in the logs).

## 7. Risks

- MCP bridge saturation after the Android build (mitigated by *Restore Windows Target*).
- HYPERVISOR_ERROR crashes on this PC (memory: *hypervisor-crash-known-pc-issue*) — verify state and resume.
- Editor in Play Mode must stay the host for the whole run; Editor exit = host leaves = master migration (which is P7, so do it on purpose, not by accident).
