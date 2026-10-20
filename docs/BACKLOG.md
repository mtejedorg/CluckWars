# Cluck Wars — Product Backlog

**Compiled 2026-10-19** from: `docs/HANDOFF-PHASE8D-2026-10.md`, `docs/superpowers/handoffs/*`, `docs/HANDOFF-COOP-REPORT-QUICKWINS-2026-08.md`, `docs/HANDOFF-NETWORKING-2026-07.md`, `docs/STATE.md` ("Stays open" / "Not verified" / "Outstanding" notes), `docs/ROADMAP.md` (unchecked items + post-demo phases), GitHub PRs, and git branches / worktrees.

**Legend.** Type: Decision · Task · Bug · Verify (needs a device / multiplayer run) · Chore · Epic. Priority: P1 (blocks a test session / ship) · P2 (should do soon) · P3 (nice to have). Suggested owner is the agent the project routing table would use. **"Check first"** = the source note is old and may already be done; confirm before spending time.

Status of the repo at compile time: `develop` @ `cca5e51`, EditMode 1899/1899.

---

## 0. Decisions queue (blocks other tickets — Maestro)

| ID | Decision | Recommendation | Unblocks |
|---|---|---|---|
| CW-001 | **Doppelganger decoy colours:** keep rival ring + edge arrow grey, or give them the caster's colour (touches gameplay lookups)? | Leave grey (matches nameplate, reveals nothing extra). | closes E1 follow-up |
| CW-002 | **D12 scarecrow vs coop:** sliding 1.2 m breaks the 4 m arm-tip rule (max slide ≈ 0.15 m). Relax rule to ~3 m for scarecrows, move the coop, or slide the scarecrow off the fence line? | Relax to ~3 m (scarecrows are walk-through). | CW-040 |
| CW-003 | **D13 rim path visibility:** accept the small gain, or push the first stretch ≥ 3 m off the fence (x ≤ 21.7, a design change)? | Accept unless it still reads wrong on device. | CW-041 |
| CW-004 | **D17 pad lines** are the ability-cone outline (`AbilitySlotOverlay`), not paths. Keep or remove? | Keep unless playtesters call it noise. | CW-042 |
| CW-005 | **Arena reveal hold style:** hold on GET READY (shipped) vs a held "3". | Keep GET READY. | closes CW-020 |
| CW-006 | **Chicken pose during 3-2-1:** chicken lies sprawled at "3" / "2" and stands at GO. Intended? | Decide; pre-existing. | CW-021 |
| CW-007 | **When does `feature/progression` merge** into `develop` (PR #1 open, conflicting, 164 commits behind)? | Rebase after CW-010, then merge. | CW-010–CW-014 |
| CW-008 | **Edge-arrow stacking:** two rivals pushed out by the same HUD zone can overlap. Spread them? | Yes, small follow-up. | CW-022 |
| CW-009 | **Status strip in arrow safe-zones:** include it? (Appears only while a status is active.) | No, until playtest shows overlap. | CW-022 |

---

## 1. Epic — Device & multiplayer verification (Pixel 9 + gamepad session)

Everything Phase 6–8 shipped "pending the hardware session". One session, fresh Windows + Android build (`Cluck Wars / Build / Windows + Android`, `Ctrl+Shift+B`), use the `coop-test` skill for two-peer parts.

| ID | Type | Pri | Title | Source |
|---|---|---|---|---|
| CW-100 | Verify | P1 | Fresh Windows + Android build from `develop`; install on Pixel 9 | ROADMAP Phase 6/7 |
| CW-101 | Verify | P1 | GPU frame time on Mobile tier at the fixed corner **and** centre pile with 4 live chickens, Performance Mode on and off. Decides E5 (Performance Mode scope: soft shadows, render scale, outside props, ~4.5k-tri chicken LOD). Needs a centre-pile / live-bots mode in `measure_phase8_cost.py` (not supported today) | STATE chunk D |
| CW-102 | Verify | P1 | Arena reveal gate on device: `adb logcat -s Unity:*` → `ArenaReadiness` gate times; first hit spark / Invisibility / rival fence hole / telegraph show no first-use hitch; cover over the touch HUD; Reduced Motion | STATE arena reveal |
| CW-103 | Verify | P1 | Two-peer reveal sync: host START with a joiner still warming (GET READY holds ≤ 18 s); late joiner into Active stays covered until ready | STATE arena reveal |
| CW-104 | Verify | P2 | Ghost, fence-hole, haze, grass-detail, mound-occlusion shaders on real GLES3 / Vulkan (no pink / cyan) | STATE chunk D |
| CW-105 | Verify | P2 | Mobile shadow bias and distance (normal 1→0.4, depth 1→0.5, distance 30→26) | STATE chunk D |
| CW-106 | Verify | P2 | Performance Mode twin-fence memory | STATE chunk D |
| CW-107 | Verify | P2 | Haptics (Buzz When Hit + VIBRATE merge), edge-cancel feel, real Android Back, real pad B, parked mouse cursor, two-peer aim replication | STATE Phase 6/7 |
| CW-108 | Verify | P2 | Real-device UI: notch / safe area, soft-keyboard Done, focus ring after touch, scrollbar thumb drag, hint placement, HUD slider drag, 6-tile invite at long code (crowds SHARE), mastery/menu layouts | STATE Phase 5/7 |
| CW-109 | Verify | P2 | Host + Join live: waiting line, host-left notice, MP body copy on the Leave sheet (unit-tested only) | STATE Phase 4/6 |
| CW-110 | Verify | P3 | Pixel 9 legibility of the 0.22 / 0.035 ring stroke | STATE (old) |
| CW-111 | Verify | P3 | 4-device LAN validation / "third player can't connect" diagnosis; Windows ↔ Android LAN smoke | ROADMAP Phase 2–4 · **Check first** (likely superseded by UGS work) |
| CW-112 | Verify | P3 | Networking-handoff editor verification checklist (NetworkTransform on Chicken.prefab, master-client departure, batched RPCs) | HANDOFF-NETWORKING · **Check first** |
| CW-113 | Verify | P2 | Playtest stacked defensive combos (Egg Shell + Turtle; Turtle + Roll & Push + Spine Coat) before any cap | ROADMAP |

---

## 2. Epic — Render & load polish (post chunk D)

| ID | Type | Pri | Title | Notes |
|---|---|---|---|---|
| CW-020 | Chore | P3 | Close reveal-hold-style decision (CW-005) | |
| CW-021 | Task | P3 | Chicken stand-up timing during countdown (after CW-006) | `ChickenVisuals` |
| CW-022 | Task | P2 | Edge arrows: spread stacked arrows; optionally include status strip (CW-008/009) | `RivalIndicator.cs`, `HudControlZones.cs`; ui-designer |
| CW-023 | Task | P2 | Shader variant warm-up proper: record a `ShaderVariantCollection` / Unity 6 `GraphicsStateCollection` from one Play session (ghost, fence holes, E3 grass detail, D4 hedge/surround, D3 mound occlusion, D8 ring ghost + stencil mark, D14 band) and warm in Bootstrap. Est. ½–1 day. Do after CW-102 shows whether device hitches | render-pipeline-artist |
| CW-024 | Task | P3 | Extend reveal warm-up to what it skips: floating combat-text font, ability-zone objects (created by first cast), ring ghost | |
| CW-025 | Bug | P3 | `tools/ui_capture/intro_probe.py` is stale (`RecAudio` lacks `IAudioService.SetMusicVolume`) — fix or delete | tooling |
| CW-026 | Chore | P3 | Capture/cost tool: add centre-pile + live-bots mode to `measure_phase8_cost.py` (enables CW-101) | tooling |
| CW-027 | Task | P3 | Look into render CPU rise 0.6–0.8 → 0.9–1.2 ms between capture rounds (likely tool change, was already present with D14 off) | |
| CW-028 | Bug | P3 | Chicken feet hover ~0.08 m (models seated on capsule bottom) | STATE chunk A follow-up |
| CW-029 | Bug | P3 | Rival nameplates look rotated in non-match-camera overview captures only | STATE chunk A follow-up (capture artifact?) |
| CW-030 | Bug | P3 | 1 642-tri fence still shows hairline cracks at post caps up close; `Prop_feed_silo` BoxCollider still sized to the stripped plate | STATE chunk B |

---

## 3. Epic — Arena layout leftovers (blocked on decisions)

| ID | Type | Pri | Title | Blocked by |
|---|---|---|---|---|
| CW-040 | Task | P3 | D12: separate scarecrows from coops, keeping 4-fold symmetry and A5/C9 rules | CW-002 |
| CW-041 | Task | P3 | D13: make rim path visible from the owner's seat | CW-003 |
| CW-042 | Task | P3 | D17: remove / restyle the ability-cone outline across the pad | CW-004 |
| CW-043 | Task | P3 | Compendium sync: `docs/site/index.html` — "Engine & rendering" bullet (~L1182) is stale vs ART.md §2; pile slow note (~L574) should say "within 1 m of the pile". Republish to artifact 1eP5RtYLXhCBmVziDe9SNZ | — |

---

## 4. Epic — Gameplay correctness & branches

| ID | Type | Pri | Title | Notes |
|---|---|---|---|---|
| CW-050 | Bug | P1 | **Assassin execute credit** — branch `fix/assassin-execute-credit` (2 commits, 343 lines, `AssassinExecute` / `ChickenCargo` / `ChickenCombat` + tests, worktree `CluckWars-execute`) is **not merged** and 165 commits behind `develop`. Phase 7 security follow-up (`7cb18c6`) touched the same area. **Check first** whether `develop` already fixes it; if not, rebase, `qa-reviewer`, merge; verify bounty on proxy in Shared PvP with a real multi-client run | senior-dev + qa-reviewer |
| CW-051 | Bug | P2 | Progression keeps one extra line in `AssassinExecute.Press` (`AnnounceExecuteSteal`) that must be preserved when CW-050 and CW-007 merge | |
| CW-052 | Chore | P3 | Egg Shell defence branch `fix/egg-shell-defence` is at `1fba816` (0 ahead) — Egg Shell is now "Full shell" on `develop`; delete branch + worktree `CluckWars-eggshell` | cleanup |
| CW-053 | Bug | P2 | Warrior / Speedy / Assassin class passives (Mighty / Bracer / Opportunist) are permanently inert since the 2026-08-13 damage removal; spec screen makes them more visible. Re-design or remove | mechanics-designer; menu handoff §4 |
| CW-054 | Task | P3 | Peck's reach is untaught (range is pile-relative; needs a pile-side visual) | STATE (old) |
| CW-055 | Bug | P3 | `AbilityTelegraph.BuildLineMaterial()` silently returns null (magenta) and leaks a Material per instance — add log + shared material | **Check first** |
| CW-056 | Bug | P3 | `RivalIndicator.Awake` gates self-injection on `ProjectContext.HasInstance` (forbidden by CONVENTIONS) | **Check first** (file was rewritten in E1 / arrows) |
| CW-057 | Task | P2 | Balance pass from 4-player playtest data: cooldown tiers, pile/collision slow, control durations, passive magnitudes, bot radii and Hunt↔Flee hysteresis (use `BalanceEditorWindow`; guard with the SCT oracle ±0.5 s test) | mechanics-designer; ROADMAP "dedicated test session" |
| CW-058 | Task | P2 | Wing Slam (12 s, longest CD on the spam class) flagged for re-tuning (GDD §11) | mechanics-designer |
| CW-059 | Chore | P3 | Roll & Push "knockback" deferred item, weighted/open random bot loadouts from the full registry, probability tables per class | ROADMAP deferred · **Check first** |

---

## 5. Epic — Progression (PR #1, `feature/progression`)

State: slices 0–4 done, 809/809 at the time; **PR #1 open, CONFLICTING**, 7 commits ahead / 164 behind `develop`.

| ID | Type | Pri | Title | Notes |
|---|---|---|---|---|
| CW-010 | Task | P1 | Decide and execute merge strategy for `feature/progression` (CW-007): rebase onto `develop`, resolve conflicts (Assassin line CW-051, `QualitySettings` schema bump chore commit), re-run full suite | senior-dev |
| CW-011 | Verify | P2 | Live test: ramp progression, weekly goals / daily task, identity persistence across sessions | |
| CW-012 | Task | P2 | GDD §8 / §11 rewrite for the shipped progression system | docs; read numbers from `.asset`s and code, never GDD prose |
| CW-013 | Task | P2 | Compendium `docs/site/index.html` sync for progression (same pass as CW-012) and republish | |
| CW-014 | Bug | P3 | Mastery ring overlaps class-name last two characters on chips (`CharacterSelect.uxml` / `CluckWarsTheme.uss`) | ui-designer · **Check first** (menu redesign merged since) |
| CW-015 | Chore | P3 | Remove stale worktree `CluckWars-turtle` / branch `fix/ability-description-damage` (fully merged) | `git worktree remove ../CluckWars-turtle` |
| CW-016 | Epic | P3 | Post-demo Phase 12: account progression, cosmetic skins (Phase 13 seasonal, Phase 14 flavour themes) | ROADMAP |

---

## 6. Epic — Menu redesign (PR #2 merged)

| ID | Type | Pri | Title | Notes |
|---|---|---|---|---|
| CW-060 | Verify | P2 | The menu-redesign handoff says the work was verified against the wrong Unity project; later phases (4–7) rebuilt much of the UI, so re-verify the two-step Choose Chicken → Loadout flow in the real Editor (shared callout panel reads OK with 4 cards? `SeedForcedAbilities` equips signature abilities correctly?) | tester · **Check first** |
| CW-061 | Chore | P3 | Worktree `.claude/worktrees/cluck-wars-menu-ui-a62862` (detached) and branch `claude/cluck-wars-menu-ui-a62862` — remove after CW-060 | cleanup |

---

## 7. Epic — Coop-report quick wins (2026-08 handoff)

The handoff's progress log was never filled in. **Check first** each against STATE.md (many appear shipped through Phase 4–7: Quick Play, pre-filled loadout, progressive ramp, nameplates, cargo on body, mastery).

| ID | Type | Pri | Title |
|---|---|---|---|
| CW-070 | Verify | P3 | A1 Quick Play button · A2 pre-filled loadout · A3 progressive pool · A5 category filter · A7 first-match-as-tutorial |
| CW-071 | Verify | P3 | B2 leader wears a target · B3 cargo readable on the body |
| CW-072 | Verify | P3 | C1c walls ability-permeable by design · C2b loud telegraph for Immovable · C3c "you are marked" warning |
| CW-073 | Verify | P3 | D1a jitter props/piles per match · D2c per-class mastery stats · D3a local leaderboard / personal bests |
| CW-074 | Chore | P3 | Fill the handoff's Progress Log or retire the doc |

---

## 8. Epic — Infrastructure, networking & release

| ID | Type | Pri | Title | Notes |
|---|---|---|---|---|
| CW-080 | Task | P3 | Server-Mode migration (Phase 11): dedicated server, `IsAuthorityPeer` abstraction, UGS Relay only if Photon is replaced (ADR exists) | architecture |
| CW-081 | Task | P3 | Networking-handoff "out of scope" items: pickup pooling, M5 scatter, other pending design decisions | HANDOFF-NETWORKING §out of scope |
| CW-082 | Chore | P2 | Resolve ROADMAP stale checkboxes (Phases 2–7 list `[ ]`/`[~]` items that later phases superseded) so the backlog and ROADMAP agree | code-architect |
| CW-083 | Chore | P3 | Prune old branches: `claude/silly-nobel-b22985`, `recovery-uxml`, `backup/chicken-models-full`, `feat/chicken-models-skeletal-animation` (14 ahead / 217 behind — confirm merged by content, memory says wired) | cleanup |
| CW-084 | Chore | P3 | `Design/docs/ART.md` and Design tokens JSX still carry old player hexes (P1 is now #3FE8E8) | mirror of claude.ai design project |
| CW-085 | Chore | P3 | Move stale `Temp/__Backupscenes` aside kept at `..\CluckWars_tmp_backup` — delete when sure | local |
| CW-086 | Chore | P3 | iOS export is off by default (no Mac); revisit with hardware | CLAUDE.md |

---

## Suggested order of attack

1. **CW-001…009 decisions** (15 minutes of Maestro time; unblocks 6 tickets).
2. **CW-050 / CW-051 / CW-010** — land the two unmerged branches (Assassin execute, progression) while `develop` is quiet.
3. **Hardware session (Epic 1)** — one sitting covers CW-100…109; its results gate CW-023, CW-101 → E5.
4. **CW-012/013/043** documentation + compendium pass in one go.
5. Gameplay balance (CW-057/058) after real 4-player data; progression release (CW-011).
6. Cleanup tickets (CW-015, 052, 061, 083) any time.

---

## 9. Test session 2026-10-20 findings (Pixel 10 Pro + Windows EXE; build `db0cfad+`)

Evidence lives in `Captures/session-<utc>/` (git-ignored). SHA = commit under test. New IDs continue from CW-200.

| ID | Type | Pri | Title | Evidence / notes |
|---|---|---|---|---|
| CW-200 | Bug | P1 | **Headless (`-batchmode`) builds carry no UGS cloud project id** → every Host/Join fails with `CreateLobby failed: Some services couldn't be initialized` on Android **and** Windows. `ProjectSettings.asset` has `cloudProjectId:` empty; only a build made from a signed-in Editor injects it. Any CLI/CI build is silently multiplayer-dead. Fix: commit the id (or inject it in `CluckWarsBuildMenu`) and add a build guard that fails when it is empty. Project id `14249009-abbe-423c-8c0c-8391b8974857` (from history) used as a temporary local override for the session. | phone.log / client1.log 19:53Z |
| CW-201 | Bug | P2 | Headless APK installs as `com.DefaultCompany.CluckWars` (empty `applicationIdentifier`), side by side with the older `com.UnityTechnologies.com.unity.template.urpblank` install; `coop-test` skill and docs still name the old id. Pin the identifier in ProjectSettings and update the skill/docs. | `pm list packages` |
| CW-202 | Chore | P2 | UGS init failure hid its inner exceptions (diagnosis cost ~15 min). **Fixed** `5c6625a` (logs every inner exception). | |
| CW-203 | Chore | P3 | Stale-install trap: `start.ps1` launched the old package after installing the new one. **Fixed** `5791ab7`. | |
| CW-204 | Chore | P3 | A crash during a 14-min IL2CPP build (Kernel-Power 41, no dump, Hyper-V active) cost one build cycle — known PC issue (see memory `hypervisor-crash-known-pc-issue`). Build in smaller steps / avoid concurrent GPU load. | Event log 20:30:36 |
| CW-205 | Bug | P2 | **Deposit RPC from a chicken that is not at its base.** Session 20:08Z: `RPC_AddFood(0,28)` from `[Player:3]` for corner 3 was **rejected by the host twice** (`no chicken that caller is entitled to speak for is standing at corner 3…`) while P3 (Fatty, cargo 40) was at the arena centre (x≈2, z≈1) and finished the round carrying 39. The server rejection is correct; the open question is *what sent the client-side call* (autoplay fake input? optimistic credit?). Re-test with the corrected autoplay that really deposits; if it reproduces without autoplay, P1. | merged.log 20:10:14Z / 20:10:21Z |
| CW-206 | Bug | P2 | **Portrait layout of the match HUD (phone lying flat).** `defaultScreenOrientation: 4` (auto-rotate, all four) so the game runs in portrait on the Pixel 10 Pro; coop-test/docs say "landscape-locked" — confirm intent. In portrait the **timer box covers the leaderboard's food-count column** (P2/You rows) and a rival **edge arrow overlaps the SLOW status strip** (CW-009 evidence). Camera framing is tight (chicken fills ~35 % of the width). | shots/phone-…r1-mid.png, mid-match |
| CW-207 | Bug | P2 | `Non-Legacy animations cannot be sampled outside the Editor without an Animator` — **6 753 warnings in one Windows client log** (one match + menu). Some object samples a non-legacy clip without an Animator in players (silent CPU cost + log spam). Find the owner (`AnimationClip.SampleAnimation` / Animation component on a prop or chicken). | client1.log |
| CW-208 | Verify | P3 | Load hitches: phone 500 ms in the lobby→match load (frame 414, `WaitingForPlayers`), PC clients 200–355 ms at scene load (menu phase). Expected for scene load but they land *after* the cover? Check the reveal gate hides them (CW-102). | findings.txt PERF |
| CW-209 | Chore | P3 | Two EXE clients on one PC share the anonymous UGS identity → the 2nd cannot join a lobby the 1st created/joined (`player is already a member of the lobby`). Add a per-process auth profile (dev tools: `-cw_profile N`) so multi-client PC tests are reliable. | client2.log 20:12Z |
| CW-210 | Chore | P3 | `VisualProbe` outOfBounds rule flagged the intentional `MapSurroundings` art (39 renderers, Warn every 10 s) — probe false positive. Being fixed on the instrumentation branch. | |
| CW-211 | Verify | P2 | **CW-101 data point (Pixel 10 Pro, Mobile tier, Vulkan, perf mode ON, 30 fps cap):** 4-corner arena at the fixed camera runs `p95=33.5 ms (30 fps)`, `gpu≈3 ms`, 11–12 hitches>50 ms per round (mostly load). GPU headroom large at this quality. Pixel 9 / mid-range data still missing. | phone.log Perf lines |
| CW-212 | Bug | P2 | **Portrait HUD collisions (4 players, Pixel 10 Pro).** The `★ FIRST TO 40` pill sits on top of the local **"You" nameplate**; the nameplate is oversized (≈50 % of the screen width) and covers the chicken's head; a rival nameplate (**P3**) is half hidden behind the leaderboard; the top-centre class label (`ASSASSIN`) stacks with the HUD. Same root cause as CW-206 (portrait width). | session-203530 shots phone auto2 |
| CW-213 | Verify | P3 | CW-006 evidence: at countdown **"1"** the local chicken is still the flattened blob (back only) at its base; it stands at GO. Confirms the pose is held through 3-2-1 on device; decide (CW-006). Reveal/fence-fade disc around the chicken looks right (no pink/cyan) — CW-104 partially confirmed on Vulkan (fence holes). | phone auto4 |
| CW-214 | Bug | P1 | **Test-harness autoplay v2 never forages** (cargo 0 in 4 rounds, host chicken parked at (0,19.5)) — tooling defect on the instrumentation branch, tracked until fixed; see commit log. Not a game bug unless the same stuck position reproduces with a human (check fence at z≈19–21 on the host's side of the arena). | session-203530 Sync lines |
| CW-215 | Chore | P3 | `merge.ps1` expects `client*.log`; scenario script now writes them accordingly. SHA mismatch flagged between the phone APK (built at `333f673`) and the EXE (`bc370a7`) because a tooling-only commit landed between the two player builds — build both from one SHA next time. | findings.txt |
| CW-216 | Bug | **P1** | **Remote players cannot bank food in Shared mode.** 4-seat run (host EXE + 2 EXE joiners + Pixel, 3 rounds, session 21:04Z): **all 185** `RPC_AddFood` calls from non-host peers were rejected by the host (`no chicken that caller is entitled to speak for is standing at corner N with a matching HomeCornerIndex`), while the clients logged `Deposited complete` and their autoplay reported "at base" at pos ≈ (8.3, 8.3) — the chickens' corners are at (±22, ±22). Only the Spoiler's end-of-round +15 ever reached a base, so every round ended `total=15,0` with the host as "winner". Hypothesis: the PlayerBase position (or corner↔position mapping) differs between the host and clients. Anti-cheat validation itself looks right; the peers disagree about where the base is. Under investigation (diagnostics + possible fix on `test/instrumentation-2026-10-20`). **Supersedes CW-205.** | session-210430 merged.log (rejections from 21:05:11Z) |
| CW-216-fix | Bug | P1 | **FIXED on `test/instrumentation-2026-10-20` (`4328d36`, `fix(deposit)`), verified live.** Root cause: `PlayerBase` and `FoodPile` have no `NetworkTransform`, so Fusion does not send their spawn position — every client created all 4 bases at the prefab root (9.47, 0, 9.38) and all 9 piles at the origin; the host validated deposits against the real corners and (correctly) rejected them. Fix: spawn position stamped as a networked value, applied on every non-owner peer. Re-run (4 seats, 3 rounds, 21:29Z, SHA `4328d36` on all 4 devices): **0 rejections** (was 185/185), every peer logs identical `Base corner=.. pos=±21.8,±21.8`, remote clients bank food. **Needs review + merge to `develop` (touches `PlayerBase`, `FoodPile`, `MapGenerator`).** | session-212850 |
| CW-217 | Bug | P2 | Same defect class: `AbilityZone.prefab` (and likely other spawned world objects without `NetworkTransform`: pickups, decoys, telegraphs?) appear at the prefab root on non-owner peers. Audit every `NetworkObject` prefab that is spawned with a position: either `NetworkTransform` or the new position stamp. Add a prefab guard test (the fix added one for base/pile only). | senior-dev note |
| CW-102-res | Verify | P1→pass | **CW-102 (reveal gate on device) — PASS on Pixel 10 Pro / Mobile tier / Vulkan.** Arena ready in ≈3.5 s (`Gate Tier 0.9 s, Scene 2.9 s, Warmup/Camera 3.5 s`), GET READY held 3.0 s, late chicken class triggers an overview re-run; **no hitch > 50 ms in any Active/intro frame over 4 rounds** (all 8 hitches fall in menu/loading/lobby/covered). Frame time locked 30 fps: avg 33.3 ms, p95 33.5, p99 33.6, mem ≈ 516 MB. Not yet covered: Reduced Motion, perf-mode OFF. | session-212850 phone.log |
| CW-103-res | Verify | P1→partial | CW-103 (two-peer reveal sync): 4 peers (3 EXE + phone) joined within 0.6 s of each other; each stayed covered until its own gate finished (phone 3.5 s vs host 2.3 s); `PlayerJoined state=Starting` handled; no desync (Sync pairing: 111 heartbeats/pair, 0 mismatches ≥2 beats). **Late-join into Active not yet tested.** | |
| CW-101-res | Verify | P1→partial | CW-101: Pixel 10 Pro (not Pixel 9) holds 30 fps with 4 live chickens at the fixed camera, Perf Mode ON (`perfMode=1`): no GPU headroom problem visible (FrameTimingManager reports gpu==frame time because of the 30 fps cap — **not a usable GPU number**; use `dumpsys gfxinfo`/Perfetto for real GPU ms). Perf Mode OFF and centre-pile cases still open. | |
| CW-112-res | Verify | P2→pass | **Master departure mid-match — PASS.** Host EXE killed 15 s into a round (4 seats, 21:34:36Z): Photon reported the leave after ≈9 s (`OnPlayerLeft` 21:34:45Z), the phone was promoted (`Local peer promoted to Master Client. Re-arming match generators.`), the survivors saw the "Host left mid-round" notice, the match ended normally by timer and the **phone then hosted 3 more full rounds** (auto-loop). No errors/exceptions. Note the 9 s freeze before the notice. | session-213356 |
| CW-103-res2 | Verify | P1→pass | **Late join into an Active match — PASS.** A 3rd peer joined 18 s into a round: stayed behind the cover (`cover=1`), `Arena ready in 2.27 s`, `GET READY held 1.73 s`, first-use telegraph hitch (417 ms) happened under the cover, no desync afterwards. | session-214247 |
| CW-218 | Bug | P2 | **No reconnect after a short app interruption.** Backgrounding the app for 12 s (Home, then relaunch) on the Pixel: frame stalled 12.8 s → `OnDisconnectedFromServer reason=Timeout` → `OnShutdown DisconnectedByPluginLogic` → "Network shutdown… Returning to Bootstrap in 5 s" → main menu. A call/notification/app switch > ~10 s ejects a mobile player permanently from the match with no rejoin (and no host-side bot takeover). Decide policy: Fusion rejoin token / keep-alive on `OnApplicationPause`, or at least a "you were disconnected" message with a Rejoin button. | session-213904 phone.log 21:39:55Z |
| CW-219 | Bug | P2 | **Portrait main menu is clipped** (Pixel, phone lying flat → auto-rotated portrait): the `CLUCK WARS` title is cut at both screen edges, the class row shows `RRIOR` / `ASSAS` (Warrior/Assassin labels cut off). Same root as CW-206/212 (portrait unsupported or not laid out). Either lock to landscape in the manifest (the docs say it is locked) or lay out for portrait. | shots/phone-…after-resume.png |
| CW-220 | Verify | P3 | Perf Mode OFF, Reduced Motion, Pixel 9, gamepad/haptics (CW-107), notch/safe-area (CW-108), shader sweep (CW-104: no pink/cyan seen on Pixel 10 Pro Vulkan in ≈15 min of play incl. fence-fade, ghost/haze, mound occlusion), shadow bias (CW-105), twin-fence memory (CW-106) — **not covered** by this unattended session (no human at the device; needs eyes). Visual probe reported `offenders=0` in every scan on phone and PC. | |
| CW-221 | Bug | **P1** | **Second match starts where the first one ended** (reported by Maestro on a manual test, reproduced in the logs). At PLAY AGAIN the **master/host's own chicken is not moved back to its corner**: `ResetWorldForNewRound` calls `RPC_TeleportTo` on every chicken and the host logs `Teleported to (21.8, 0.05, -21.8)`, yet the host chicken is at its old position at the first Active tick of the next round (e.g. P1 parked at (0.1,19.3) at the end of round 1 and at (0.1,19.3) again at round 2 `rem=45.0`), while remote chickens P2–P4 are at their corners. Present in every round ≥ 2 of the three 4-seat sessions. Suspect: the local (same-peer) RPC invocation inside `GameManager.FixedUpdateNetwork` is overwritten by the chicken's own simulation/NetworkTransform state; also verify the Single (solo) path (`InputAuthority.IsRealPlayer` filter at GameManager L919). Fix in progress on the instrumentation branch (`fix(round-reset)`). | session-203530 / 210430 / 212850 Sync lines |
