# Testing & Diagnostics Playbook

How to build, run, and diagnose Cluck Wars on Windows + Android. Optimized for
the dedicated test session that's been bundled (see `docs/STATE.md` for what's
queued for that session).

---

## Builds

Single-keypress builds from the Unity Editor:

| Menu | Shortcut | Output |
|---|---|---|
| `Cluck Wars / Build / Windows` | `Ctrl+Shift+W` | `Builds/Windows/CluckWars.exe` |
| `Cluck Wars / Build / Android` | `Ctrl+Shift+A` | `Builds/Android/CluckWars-<version>.apk` |
| `Cluck Wars / Build / Windows + Android` | `Ctrl+Shift+B` | both, sequential |
| `Cluck Wars / Build / Reveal Builds Folder` | — | opens `Builds/` |

The Android build forces:
- `IL2CPP` scripting backend
- `ARM64` architecture only (no ARMv7 — keeps APK small, matches 2021+ devices)
- `minSdk = 24` (Android 7.0)
- `buildAppBundle = false` (`.apk`, sideload-friendly — not `.aab`)

`Builds/` is gitignored. APK filename embeds `PlayerSettings.bundleVersion`.

### Prerequisites

- **Android Build Support** module installed via Unity Hub (Add Modules to the Editor install).
- **Android SDK + NDK + JDK** — either bundle with the Editor install (default), or point Unity at your own in `Edit / Preferences / External Tools`.
- **Photon AppId** already configured at `Assets/Photon/Fusion/Resources/PhotonAppSettings.asset` → `AppIdFusion = 259bda28-…`. Both peers need the same AppId to be matchmade.

### Known gotchas

**Android x86_64 emulator cannot run Unity 6 URP — use Pixel 9 or Windows builds**

Tested exhaustively (2026-06-04) with `cluck_emu2` (AVD: `google_apis_playstore;android-34;x86_64`,
Pixel 5 profile, 4 GB RAM). The blocker is a Hyper-V + Unity URP GPU requirement conflict:

| GPU mode | ES 3.1+? | Emulator stable? | Result |
|---|---|---|---|
| `swiftshader_indirect` | ❌ ES 2.0 only | ✅ | Unity URP crashes — `EGL_BAD_CONFIG: no ES 3.1 support` |
| `host` | ✅ | ❌ | Emulator process killed by OS (Hyper-V + GPU passthrough) |
| `angle_indirect` | ✅ | ❌ | Emulator crashes (ANGLE D3D11 init fails under WHPX) |
| `mesa_indirect` | ✅ (ES 3.1) | ❌ | Emulator crashes during Unity render init |

**Root cause:** Hyper-V/WHPX is enabled on this machine. Every GPU mode that exposes ES 3.1+
crashes the emulator process. `swiftshader_indirect` survives but only provides ES 2.0,
which Unity URP rejects — it then sends `SIGILL` to itself (`SI_TKILL`) and dies.

**Decision:** All Android testing on the real Pixel 9 (`adb -s 57080DLAQ0030B`).
Emulators not viable for this stack.

> `cluck_emu2` remains configured with `angle_indirect` in case you ever want to retry after
> a Hyper-V change. To revert to `swiftshader_indirect` (at least keeps emulator alive):
> edit `~/.android/avd/cluck_emu2.avd/config.ini` → `hw.gpu.mode = swiftshader_indirect`

**ADB version alignment (one-time fix, already done)**
`$SDKROOT\platform-tools\adb.exe` was v40; `C:\Users\MARCO\Documents\platform-tools\adb.exe`
was v41. The two servers fought every command, causing `device offline` drops mid-install.
Fix: v41 binary was copied over the SDK v40 — both paths are now v41. Never switch between them.

**`AndroidArchitecture.X86_64` = Magic Leap in Unity 6**
`AndroidArchitecture.X86_64` (value 8) maps to "x86-64 (Magic Leap)" in Unity 6000.3+
and is rejected by `BuildPipeline.BuildPlayer` with a `UnityException`. The
`Cluck Wars / Build / Android (Emulator)` menu item has been updated to ARM64-only.

**MCP saturation after Android build**
`Cluck Wars/Build/Android` (Ctrl+Shift+A) switches the active build target to Android,
triggering a full domain reload. If MCP calls follow immediately, they queue and expire —
the IvanMurzak command cache floods with `[Command Cache] Cleaned 60 expired entries`
in `%LOCALAPPDATA%\Unity\Editor\Editor.log` and all tool calls time out for ~2 minutes.

**Workaround:** use `Cluck Wars/Build/Android + Restore Windows Target` — it builds then
switches back to Windows, keeping MCP responsive. If already stuck, wait for
`[Command Cache] Cleaned` to stop repeating, then reconnect.

---

## Unity MCP (agent ↔ Editor bridge)

The project has the **Unity MCP** package installed (`com.unity.ai.assistant`).
It lets Claude agents call Unity Editor APIs directly — inspect scenes, run
`AssetDatabase` queries, trigger builds, etc. — without manual back-and-forth.

### One-time setup (already done on Maestro's machine)

Config is in `.claude/mcp.json` (project-local, gitignored).  
To restore it on a new machine, add this entry to your Claude Code MCP config:

```json
{
  "mcpServers": {
    "unity-mcp": {
      "command": "C:\\Users\\MARCO\\.unity\\relay\\relay_win.exe",
      "args": ["--mcp"]
    }
  }
}
```

Or via CLI:
```bash
claude mcp add unity-mcp "%USERPROFILE%\.unity\relay\relay_win.exe" --args --mcp
```

If you ever have **multiple Unity projects open** simultaneously, add the project
path disambiguator to `args`:
```json
"args": ["--mcp", "--project-path", "C:\\Users\\MARCO\\Documents\\GitHub\\CluckWars"]
```

### Activating the connection

After adding the config and **restarting Claude Code**:

1. In Unity: **Edit → Project Settings → AI → Unity MCP**
2. Click **Accept** on the pending connection request.
3. `unity-mcp` should appear under **Connected Clients**.
4. Tools such as `Unity_ManageScene`, `Unity_GetAssets`, etc. will be available
   to agents in this session.

> **Note:** The relay process (`relay_win.exe`) must be running — it starts
> automatically when Unity opens the project. If tools appear unavailable after
> a fresh Editor launch, open `Edit → Project Settings → AI → Unity MCP` once
> to wake the relay.

---

## Running

> ⚠️ **There are no menu keyboard shortcuts.** The old `1-4 / S / H / J / SPACE` bindings
> belonged to the procedural-UGUI `CharacterSelectController`, which was **deleted** in the
> UI Toolkit migration. `MenuUiController` never reimplemented them. **Navigate the menus by
> clicking.** (In-game keys — WASD, Q/E/R, F1, F2 — are unaffected and still work.)

### Solo (PC or Android)

1. Click a class card, then click **SOLO**, then click the confirm/start button.
2. `GameMode.Single` runner spawns. No lobby — `GameManager` auto-starts the match.
3. Movement works immediately after the 3-second intro countdown.

### Multiplayer (via Photon Cloud relay + UGS Lobby)

1. **Host** (any device): click a class, click **HOST**, confirm. UGS creates a lobby with a unique
   6-char join code (shown in the **MATCH LOBBY** overlay). The code is also the Fusion session name.
2. **Joiner(s)**: click a class, click **JOIN**, confirm, then type the host's code (or pick from the lobby browser).
3. Host clicks **START MATCH** when ready. Player count is shown live as `Players: X / Y` (Y from `MatchConfigSO.MaxPlayers`, default 4). **No minimum — host can start with just themselves.**
4. Intro countdown plays on every peer simultaneously, then the round begins.

> Since 2026-06-11 **all platforms use the real `UGSService`** — `UGS_DISABLED` was removed from
> the Standalone scripting defines (it previously made Editor/Windows use `NullUGSService` with the
> fixed `cluck-lan` session while Android used real UGS, so cross-platform both-HOST never met —
> BUG-4). If you need the offline fallback, re-add `UGS_DISABLED` to the platform's defines.

### Multi-client Windows testing (the standard multiplayer loop)

```powershell
.\tools\run-clients.ps1 -Count 3     # launch 3 windowed clients, per-client logs
.\tools\run-clients.ps1 -Tail 1     # live-tail client 1's log
```

Each instance writes to `Builds/Windows/logs/clientN.log`. Build the EXE first (`Ctrl+Shift+W`).
This replaces emulator-based testing entirely; for real-device checks use the Pixel 9
(or `/coop-test` for an orchestrated Editor + phone session).

---

## Automated tests

### EditMode unit suite — the pre-playtest health gate

**101 EditMode tests** in `Assets/_Game/Scripts/Editor/Tests/`, split one file per
subsystem so a red test names the area immediately. Run this **before** every play
session: it takes ~1.5 s and catches the class of problem that otherwise burns the
first twenty minutes of a playtest (an unassigned inspector slot, a pile tuned past
its collect radius, an ability nobody can equip).

| File | Tests | What it guards |
|---|---|---|
| `CoreLogicTests.cs` | 10 | Small pure helpers: `UiGfx.Hex32` parsing + palette distinctness, `MenuUiController.GetPassiveInfo`, `ChickenClassRegistrySO` lookup and its documented Warrior fallback. |
| `DataIntegrityTests.cs` | 14 | The **real SO assets** in `Assets/_Game/Data/` — every class has stats + the GDD passive + a distinct visible tint, `MatchConfig` values are in range, every `PrefabRegistry` slot is assigned, no `ColorScheme` colour is invisible, no audio clip is wired to two cues. Highest-value category: these are the failures that only surface as a crash or a silent no-op mid-match. |
| `AbilitySystemTests.cs` | 14 | Registry completeness (no orphan ability type, no unregistered asset, no duplicate/null entry), per-asset authoring (labels, accent alpha, cooldown ≥ duration), `ResolveBotRole()` never leaves `Auto`, range-gated abilities expose a non-zero `IndicatorRange`, physics scanners include layer 8, and `AbilityIconStyle` covers every concrete subclass. |
| `EconomyAndPilesTests.cs` | 22 | ADR 0003 pile maths via `FoodPileMath` (top bucket is exactly the authored size, stepping is quantised and monotonic, a permanent pile never reaches step 0 and never drains below its floor) plus the **blocker-vs-`CollectRadius` margin** read off the real prefab, and win-target reachability against the real `MatchConfig` + class stats. |
| `ContractsAndEnumsTests.cs` | 12 | Serialisation-sensitive contracts: byte backing types, `ChickenClass` numbering, the zero member of every `[Networked]` enum, exhaustive `ResolveBotRole` / `GetPassiveInfo` coverage, and **`SessionNameSeed` parity** between `MapGenerator` and `MatchBootstrapper` (the two duplicated copies that keep peers' wall layouts identical). |
| `ServicesAndInputTests.cs` | 16 | `SessionSelectionService` defaults + change-event semantics, `NullUGSService` / `NullAudioService` (what an offline demo build actually runs on), `LobbyInfo`, and `CompositeInputProvider` — including that it reads **every** provider each tick rather than short-circuiting, which is what keeps edge-triggered ability presses from leaking into the next tick. |
| `ProjectConfigTests.cs` | 13 | Build-settings scene order, physics layer names, `Chicken.prefab` component composition (incl. the `NetworkTransform` from finding C1 and the no-duplicates rule from C2), colliders on layer 9 in every interactable prefab, `Doppelganger` is a cargo-stripped variant, URP-only materials, SO folder + `SO`-suffix conventions. |

`TestAssets.cs` is a shared helper, not a fixture — it centralises asset paths so a
moved asset produces one clear failure instead of a scatter of NREs.

**Run it three ways:**

| How | Command |
|---|---|
| Editor UI | `Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All` |
| Agent (Unity MCP) | `tests-run` with `{"testMode":"EditMode","testNamespace":"CluckWars.Tests"}` |
| Headless CI | `Unity.exe -runTests -batchmode -projectPath . -testPlatform EditMode -testResults results.xml` |

Filter to one category with `testClass`, e.g.
`tests-run {"testMode":"EditMode","testClass":"DataIntegrityTests"}`.

**Baseline as of 2026-07-22: 99 passed / 2 failed.** The two reds are *real* and
deliberately left failing — see `docs/STATE.md` for the write-up. Do not weaken them;
they go green when the class-registry tints are authored.

**Writing new tests — two gotchas.**

- Fusion ships its own `Assert`, so any test file that also imports `Fusion` needs
  `using Assert = NUnit.Framework.Assert;` at the top (same family as the documented
  `Fusion.LogLevel` collision — see `CONVENTIONS.md ▸ Footguns`).
- Populate private serialized fields with `ScriptableObject.CreateInstance` +
  reflection and always `Object.DestroyImmediate` in a `finally` or `[TearDown]`.

### What the automated suite does NOT cover

**All Fusion-networked runtime behaviour.** Everything that needs a live
`NetworkRunner` is out of scope by construction, including:

- movement, knockback, root/slow application, and the whole control-state stack;
- damage RPCs, kill credit, Spine Coat reflect, death drop;
- cargo collection, the deposit rate, the optimistic-credit duplication window,
  and the two-chickens-deposit-at-once race;
- match flow ticks — state machine, timer, win check, restart, comeback events;
- the permanent pile's regen tick and the NavMesh re-carve when a footprint steps;
- bot FSM behaviour, pathfinding, and the Flee-with-no-escape-ability stall;
- everything cross-peer: replication, ability cooldowns on remote peers, master
  promotion, reconnection, and the `OnShutdown` → Bootstrap transition.

The unit suite passing means **"the build is healthy enough to start a playtest"**,
never "the game works". Those behaviours are covered by the play-mode plan below and
by `tools/run-clients.ps1` for anything needing 2+ clients.

Do **not** try to close that gap with an `.asmdef`: game code is in the predefined
`Assembly-CSharp`, and adding a runtime asmdef can silently break **Fusion's IL weaver**
(NetworkBehaviours stop being woven — compiles fine, fails at runtime). The tests live
in an `Editor/` folder precisely to get game-code access without that risk.

### Log levels — turning up the signal

`ILogService` filters by `level >= MinLevel` (`CluckWars.Logging.LogLevel`):

| Level | Value | Use |
|---|---|---|
| `Verbose` | 0 | every-frame trace, input, transform deltas |
| `Debug` | 1 | state transitions, network events, spawn flow |
| `Info` | 2 | milestones (match start, scene load) |
| `Warn` | 3 | recoverable anomalies |
| `Error` | 4 | unrecoverable, usually with an exception |
| `Off` | 5 | silence |

Set it on the **`ProjectContext` prefab → `ProjectInstaller` → `Log Min Level`**
(`Assets/_Game/Resources/ProjectContext.prefab`; field `_logMinLevel`, defaults to
`Verbose`).

- **`Debug`** is the right level for a test session — you get bot FSM transitions,
  spawn flow, and network events without the per-tick `Verbose` firehose.
- Drop to **`Verbose`** only when chasing a specific bug (per-tick cargo/collect
  tracing). Note the hot Verbose calls are `IsEnabled`-gated, so raising `MinLevel`
  genuinely removes the string-building cost.
- Never test at `Warn` — you lose the state-transition breadcrumbs that make a
  failure diagnosable.

---

## Debug HUD (F1)

While playing, press **F1** to toggle the developer overlay (`DebugHud` MonoBehaviour). Shows:

- **FPS** (sampled at `_fpsSampleInterval`, default 0.25s)
- **Network**: `GameMode`, `LocalPlayer`, `IsMaster`, active player count.
- **Match**: `State`, intro / time-remaining / restart countdowns, winner + final food when ended.
- **Local chicken**: `Class`, HP + stun, cargo + capacity, equipped abilities + cooldowns, active slot.
- **World**: per-base ownership + corner index + food total, loose `FoodPickup` count.

Read-only. No debug commands. Drop the component on any GameObject in `Game.unity` to enable.

> Note: F1 requires a hardware keyboard. On a phone you can either connect a Bluetooth keyboard, or temporarily edit `_visible = true` on `DebugHud` to start the overlay on.

---

## Reading device logs

### Android

```
adb logcat -s Unity:* CluckWars:*
```

(`CluckWars` is the `Source` tag prefix on most of our log lines via `UnityLogService`.)

To focus on a specific subsystem, filter by source tag:

```
adb logcat -s Unity:* | grep -E '\[Chicken\]|\[Combat\]|\[Cargo\]|\[Fusion\]'
```

Filter to errors/warnings only:

```
adb logcat -s Unity:E Unity:W
```

To pipe to a file:

```
adb logcat -s Unity:* CluckWars:* -d > device.log
```

### Windows builds

Player log file location:
```
%USERPROFILE%\AppData\LocalLow\DefaultCompany\CluckWars\Player.log
```

Or with `Application.consoleLogPath` from inside the build.

---

## Source tags reference

`ILogService.Verbose/Debug/Info/Warn/Error` calls take a `source` string as the first arg. Each subsystem uses a consistent tag — `grep` for these in logcat to follow specific systems:

| Tag | Subsystem |
|---|---|
| `Fusion` | `FusionNetworkService` — start/shutdown + connect callbacks |
| `MatchBootstrap` | scene entry, runner kickoff, chicken spawn |
| `MapGen` | procedural map, base/pile spawn, boundary walls |
| `GameManager` | state machine, win check, restart, intro |
| `Chicken` | `ChickenController` Spawned + state init |
| `Combat` | `ChickenCombat` Swing, damage RPC, hit, stun |
| `Cargo` | `ChickenCargo` collect, deposit, drop, steal |
| `Ability` | `AbilityController` activate, deactivate, cooldown |
| `FoodPile` | drain RPC |
| `PlayerBase` | add-food RPC, owner assignment |
| `FoodPickup` | drop + RPC drain |
| `Doppelganger` | decoy spawn, death-despawn |
| `TouchHud` | UI build, button events |
| `CharacterSelect` | menu input + selection |
| `MatchHud` | UGUI HUD, shutdown handling |
| `CargoHud` | (legacy IMGUI, disabled by MatchHud on Awake) |

---

## Diagnostic flows for common bugs

### "Chicken doesn't move"

1. **Is it visual or actual?** With `MatchCamera._followLocalChicken = true` and a small `_orthoSize`, a moving chicken looks stationary against a featureless map. Watch a food pile or base — if it slides relative to your chicken, you ARE moving.
2. **Does the joystick knob follow your finger?** If not, touch input isn't reaching the UI.
   - Possible: stray `EventSystem` with the legacy `StandaloneInputModule` in scene. Project uses the new Input System; legacy module is a no-op. Delete the EventSystem; `TouchControlsHud` auto-creates one with `InputSystemUIInputModule`.
   - Possible: `TouchControlsHud` GameObject missing from `Game.unity`.
3. **Does the attack button respond?** Same input path as joystick — if attack flashes red but joystick is dead, only joystick is broken.
4. **`[Chicken] FixedUpdateNetwork: _movement is null (stats unresolved?)`** in logcat: `ChickenClassRegistry` missing or has no entry for the class, AND `_fallbackStats` SerializeField on the Chicken prefab is empty. Fix by assigning a fallback `ChickenStatsSO` on the prefab.
5. **`State != Active` or `IsIntroActive == true`** → input is gated. Lobby + intro lockouts are intentional.

### "Multiple chickens spawn at the same place"

`MatchBootstrapper`'s spawn log includes `pos`, `mapGen` presence, `spawnPoints` count, `PlayerId`. Check:

- `mapGen=missing` → `MapGenerator` not in scene or destroyed.
- `spawnPoints=0` → `ComputeSpawnPoints` failed.
- `pos=(0.0, 0.0, 0.0)` → fell through to `Vector3.zero` fallback.
- `pos` same for both peers → both peers picking same modulo index (PlayerIds collide?).

Fixes already in place:
- `MapGenerator._baseCornerDistance < 1` auto-snaps to 12 + warns.
- Spawn jitter per-PlayerId in `MatchBootstrapper.HandlePlayerJoined` breaks symmetry even if positions collide horizontally.

### "Third player failed to connect"

Look in logcat on the failing device for:

- `[Fusion] OnConnectFailed: remote=…, reason=…` — explicit failure reason.
- `[Fusion] OnDisconnectedFromServer. reason=…` — silent disconnect, kicked.
- `[Fusion] StartGame failed: result.ShutdownReason=…, errorMessage='…'` — start-time failure (wrong AppId, region, room full).

Common `NetConnectFailedReason` values to recognize:
- `Timeout` — network unreachable.
- `ServerFull` — room hit `MatchConfigSO.MaxPlayers`.
- `ServerRefused` — region or AppId mismatch.

### "Match doesn't start when host clicks button"

In host's logcat, look for:

```
[CargoHud] / [MatchHud] Lobby Start button → GameManager.StartMatchNow().
[GameManager] StartMatchNow accepted — host pressed Start.
[GameManager] Match started: <N>s playable + <intro>s intro, target <X> food.
```

If the first log fires but the second doesn't, `HasStateAuthority` is false on this peer — i.e., this isn't actually the master client. Check `[Fusion] StartGame OK. … IsSharedModeMasterClient=true` from earlier in the log.

### "I'm immortal / nothing damages me"

`Combat: Damage absorbed by immunity` at verbose → Egg Shell or similar ability is up. Check `AbilityController.ActiveSlot` in the Debug HUD.

### Disconnect → "SESSION ENDED" overlay → returns to Bootstrap

This is intentional. `_disconnectReturnDelay` on `MatchHud` controls the wait time before auto-reload (default 5s). If it never happens, `INetworkService.OnShutdown` isn't firing — check that the runner actually shut down.

---

## What to capture before reporting a bug

For agents handling a Maestro bug report, ask for:

1. **Mode**: Solo / Host / Join, and on which device (Windows vs Android).
2. **Player count** at the time of the issue.
3. **Joystick knob behavior** if movement-related.
4. **Debug HUD screenshot** if possible (F1, in-game).
5. **Last ~50 lines of logcat / Player.log** filtered to `Unity` + `CluckWars` tags.
6. **Commit SHA** the build was made from (`git rev-parse HEAD` at build time; matches the APK's bundle version if iterations follow the build menu's filename pattern).

That set is usually enough to localize the bug to a subsystem without another test cycle.

---

## Test session plan — networking verification (authoritative, 2026-07-20)

The ordered plan for the current session. It closes the editor-verification checklist
in `docs/HANDOFF-NETWORKING-2026-07.md` (items 2–6; item 1 is already done). Run in
order — each stage gates the next. **Set `Log Min Level = Debug` first** (see
"Log levels" above).

Legend: **Watch** = log tags/levels to follow. **Pass** = the criterion. Stop and
capture logs on any fail rather than pushing to the next stage.

### T0 — Static gate (2 min, no play mode)

| Step | Watch | Pass |
|---|---|---|
| Run the EditMode suite (`Test Runner ▸ EditMode ▸ Run All`) | — | 8/8 green |
| Clear the Console, then `Ctrl+R` (reimport/recompile) | Console `Error` | zero errors |

### T1 — Solo smoke, in Editor (golden path)

Play from `Bootstrap.unity`. **Click** a class card → **SOLO** → confirm. (No menu
keyboard shortcuts exist — see the warning under "Running".)

| Step | Watch | Pass |
|---|---|---|
| Runner starts, map builds | `[Fusion]` Info, `[MapGen]` Debug | `StartGame OK`; piles/bases/walls spawn, no NRE |
| Intro countdown 3-2-1 | `[GameManager]` Info | countdown runs then `Match started` |
| **Bot speed (Stage B regression)** | `[BotController]` Debug FSM transitions | bots move at a *believable* pace — **not** the old double-speed. This is the C2 fix landing |
| Food loop | `[Cargo]` Debug, `[FoodPile]`, `[PlayerBase]` | bots drain piles, cargo fills, deposits land; leaderboard climbs |
| VFX | visual | **single** burst per cast — a double burst means a duplicate `ChickenVFX` came back |
| Win + restart | `[GameManager]` Info | win fires at target, match-end overlay, auto-restart resets bases/piles/positions |
| Whole run | Console `Error` | **zero** errors/exceptions |

F1 toggles the Debug HUD for live state; F2 shows the balance panel.

### T2 — Intro input latch (Stage I.3 edge case)

During the 3-2-1 countdown, **mash Q / E / R and the touch ability hexes**.

**Pass:** nothing fires on match frame one — no `[Ability]` activation logs until
after `GO!`. A cast landing at t=0 means the latch-clear regressed.

### T3 — 2-client, the C1 headline (**the single most important test**)

Build first: `Cluck Wars ▸ Build ▸ Windows` (`Ctrl+Shift+W`), then:

```powershell
.\tools\run-clients.ps1 -Count 2
```

Client 1 = **Host** (note the 6-char code), client 2 = **Join** + code — all by clicking.
Host clicks START MATCH.

| Check | Watch | Pass |
|---|---|---|
| Both peers in one room | `[Fusion]` Info both logs | same session name; `Players: 2 / 4` |
| **Remote chicken visibly moves** | watch the *other* player's chicken | it **moves**. Frozen-at-spawn = `NetworkTransform` not replicating → C1 regressed. Everything else is secondary to this |
| Food totals agree | `[Cargo]`/`[PlayerBase]` on both | scores match across screens (±one 4 Hz batch — Stage D batches to ~0.25 s) |
| Center pile | visual, both peers | 1.5× on **both**, and both collide with it at the same radius (Stage H) |
| Abilities cross-peer | `[Ability]`, `[Combat]` | damage/stun/knockback replicate; VFX fire on both |

Per-client logs: `Builds/Windows/logs/clientN.log`; live tail with
`.\tools\run-clients.ps1 -Tail 1`.

### T4 — 3-client host-quit (Stage E, the risky one)

```powershell
.\tools\run-clients.ps1 -Count 3
```

All three join, start the match, then **close client 1 (the master) mid-match**.

**Pass:** piles / bases / `GameManager` **survive**; a remaining peer is promoted
master; the match keeps running and is still winnable.

**Fail mode + the known fix:** if world objects vanish and gameplay freezes, the
`DestroyWhenStateAuthorityLeaves` bit (0x40000) is still set — STATE.md records the
remedy: change the world-object prefabs' `NetworkObject` `Flags: 393217` → **`131073`**.
That is a deliberate, documented follow-up, not an improvisation.

### T5 — Abuse pass (only after T1–T4 are green)

- Spam class-select / Back / Start during lobby and intro.
- Deposit at another player's base; stand between two piles; die with a full cargo.
- Join with a wrong/lowercase code (case fix shipped v0.3.2).
- Let a match end on the **timer** with a 0–0–0–0 tie (this path historically hid the
  broken-collection bug).
- 4th client → full lobby; then a 5th → expect a clean `ServerFull` refusal.

### T6 — Bot pacing re-measure (Stage B caveat)

All pacing data before 2026-07-18 was measured with **double-speed bots** and is void.
Run 2–3 solo matches and record time-to-win-target. Design goal: uncontested
time-to-target ≈ **70 % of the 180 s timer**. Feed the numbers to the balance pass —
do **not** retune off the old WS1 overshoot numbers.

### T7 — Pixel 9 (optional, needs the device)

`Ctrl+Shift+A` (use the **Restore Windows Target** variant to keep MCP alive), then
`adb install -r Builds/Android/CluckWars-*.apk`. Solo smoke + a cross-platform join
against a Windows host. Watch `adb logcat -s Unity:* CluckWars:*`. Check FPS ≈ 30 and
that the menu fits the screen (the `ScaleWithScreenSize` fix — PanelSettings is baked
into the player, so this only proves out in a real build).

---

## Deferred to the test session (won't have data until then)

See `docs/STATE.md` § "Deferred work". Items there are all blocked on either:

- A real Android device for profiling / FPS / mobile layout
- 2+ devices on the same network for LAN networking validation
- Playtest sessions for balance data
- Hand-authored art / animation / audio assets

When the test session is scheduled, run through these in order:

**Phase 1 — Windows Editor + builds (no device needed)**

1. **Build sanity** — `Cluck Wars/Build/Android + Restore Windows Target` (`Ctrl+Shift+A` restores target) + `Ctrl+Shift+W` for Windows. Confirm both produce without errors. Use the "Restore Windows Target" variant to keep MCP responsive.
2. **Solo in Editor** — Enter play mode. Class select, Solo. Verify bots spawn, food loop runs, win condition fires, restart loop works. Check Debug HUD (F1) for state.
3. **Solo Windows EXE** — run `Builds/Windows/CluckWars.exe` standalone. Same checks.
4. **2-player Host + Join** — open two Windows EXEs. P1 = Host, P2 = Join with code. Verify both land in the same Photon room (case fix ships in v0.3.2), lobby shows 2 players, host can start, both chickens at distinct corners.
5. **3-player stress** — add a third Windows EXE joiner. Verify BUG-2 (third player can't connect) is closed after the join-case fix.
6. **4-player full lobby** — fourth EXE. Full arena test: all food mechanics, combat, abilities, win/restart.

**Phase 2 — Android smoke test (Pixel 9 via USB)**

7. **USB connect** — `adb devices` should show `57080DLAQ0030B`. `adb install -r Builds/Android/CluckWars-*.apk`.
8. **Solo Android** — launch, class select, Solo. Joystick moves chicken, abilities trigger. Camera follows. FPS stable (target 30).
9. **Cross-platform join** — Windows host + Android joiner. Confirm cross-platform session works end to end.

**Phase 3 — Polish passes (need device data)**

10. **Balance pass** — playtest 5-10 min matches, tune `MatchConfigSO` + per-ability `Duration` / `Cooldown` + `ChickenStatsSO` per-class numbers.
11. **Audio pass** — drop AudioClip refs into `AudioRegistry.asset`. Mix levels.
12. **Animator pass** — author Hit/AbilityCast/Stunned/Idle state machine + transitions in the AnimatorController.
13. **VFX pass** — particle systems for the events listed in `ART.md` §7.
