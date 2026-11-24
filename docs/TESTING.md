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
| `Cluck Wars / Build / Android Play Store bundle (.aab)` | — | `Builds/Android/CluckWars-<version>-<build number>.aab`, signed (see below) |
| `Cluck Wars / Build / Reveal Builds Folder` | — | opens `Builds/` |

The Android build forces:
- `IL2CPP` scripting backend
- `ARM64` architecture only (no ARMv7 — keeps APK small, matches 2021+ devices)
- `minSdk = 24` (Android 7.0)
- `buildAppBundle = false` (`.apk`, sideload-friendly — not `.aab`)

`Builds/` is gitignored. APK filename embeds `PlayerSettings.bundleVersion`.

### Build number and tag

The **build number** names a commit, not a build moment (Maestro, 2026-11-19): the two-digit UTC year,
then the minute of that year zero-padded to six digits, always 8 digits (2026-11-14 13:05 UTC is
`26457265`; 2026-01-01 00:00 is `26000000`). It is committed in the repo-root `BUILD_NUMBER` file, which the
`tools/hooks/pre-commit` hook (also installed as `pre-merge-commit`) rewrites on every commit and merge on
`develop` only; other branches never touch it, so merges do not conflict on it. Install per clone with
`sh tools/hooks/install.sh`. Building a commit again, any day, gives the same number.
`CluckWarsBuildMenu.HeadBuildNumber` reads the file once per menu run (fallback: HEAD's commit time, then
the clock with a warning), so in `Windows + Android` both builds share it. Caveats: cherry-picks and
rebases do not run the hook, so those commits keep their source's number; a dirty tree builds with its last
commit's number (the build info's `+dirty` tells them apart). The same number is:

- the Android **versionCode** of every Android build (sideload `.apk` and Play `.aab`), set for the
  build only; ProjectSettings keeps `AndroidBundleVersionCode: 1`. If ProjectSettings ever holds a code
  at or above the number, the build uses that code + 1 and warns;
- the `buildNumber` in the in-game build info: the `[Build] BuildInfo: build=...` log line and
  `build=` in the `Session` line (`unknown` for builds from before it existed, `editor` in the Editor);
- the git **tag**, `v<semver>+<build number>`, e.g. `v0.5.0-alpha+26457265`. The run's last log line
  is `[Build] Build number 26457265 - tag this commit as v0.5.0-alpha+26457265`; copy the tag from it.

### Play Store internal testing (builds arrive as Play updates)

`Cluck Wars / Build / Android Play Store bundle (.aab)` builds a signed bundle,
`Builds/Android/CluckWars-<version>-<build number>.aab`. Batch form:
`Unity.exe -batchmode -quit -projectPath . -executeMethod CluckWars.EditorTools.CluckWarsBuildMenu.BuildPlayStore -logFile build.log`
(add `-cwDevTools` for an instrumented build; a failure exits non-zero).

- **versionCode** = the build number (see "Build number and tag" above), so every bundle outranks the
  last one (including the old minutes-since-2026-01-01 codes) and Play offers it as an update. It is set
  for the build only; ProjectSettings keeps `AndroidBundleVersionCode: 1`.
- **Signing** comes from environment variables, never from the repo or ProjectSettings:
  `CW_KEYSTORE_PATH`, `CW_KEYSTORE_PASS`, optional `CW_KEY_ALIAS` (default `upload`) and `CW_KEY_PASS`
  (default = keystore password). Missing ones fail the build with the variable's name. `*.jks` / `*.keystore` are gitignored.

One-time setup (Maestro):
1. Make the upload key, outside the repo, and back it up (losing it means a key-reset request to Google):
   `"C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\keytool.exe" -genkeypair -v -keystore %USERPROFILE%\keys\cluckwars-upload.jks -alias upload -keyalg RSA -keysize 2048 -validity 10000`
2. Set the variables (`setx CW_KEYSTORE_PATH %USERPROFILE%\keys\cluckwars-upload.jks`, `setx CW_KEYSTORE_PASS …`), then restart Unity Hub/Editor so it sees them.
3. Play Console: create the app with package `com.cluckwars.game`, keep **Play App Signing** on (Google holds the app key, ours is only the upload key).
4. Test and release → Testing → **Internal testing**: add a tester list (your Google account), upload the first `.aab`, roll out, and open the opt-in link on the phone.

Each later build: upload the new `.aab` as a new internal-testing release; the phone gets it as a normal Play update
(minutes after rollout). A sideloaded APK of `com.cluckwars.game` is signed with a different key, so uninstall it
once before the first install from Play.

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
> clicking.** (In-game keys are unaffected and still work: WASD or the arrows to move, the slot keys of the chosen Settings > Keyboard scheme (A: Down / Up / Right / Left arrows; B: Q W R E; digits 1-4 in both), F1, F2. A gamepad has A / Y / B / X and RB / RT / LB / LT on slots 1-4, L3 cancels, Start opens the Leave sheet.)

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
- cargo collection, the deposit rate, the request / report round trip of a peck, steal or deposit,
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

Read-only. No debug commands. Placed in `AbilityLab.unity`; a **dev-tools build** (see "Test-session instrumentation" below) also adds it to every loaded `Game.unity` at runtime (`DebugHudSpawner`).

F1 and the five-tap gesture cycle **off → compact strip → full panel**. On a phone (no F1): in a dev-tools build with **Developer Mode** on, **tap the top-left corner five times within 2 s** (corner = max(120 px, 8 % of the short side); a mouse click counts too, for testing on PC). Auto-test `cw_hud 1` shows it at boot. A dev-tools build adds the probe lines: frame-time p95 (+ fps), hitch count, Visual offenders + last scan time (UTC), RTT / loss, and a **Scan visuals (F9)** button.

---

## Ability Lab (dev scene)

An isolated scene for judging how an ability **feels** — no match clock, no bots hunting
you, no food economy. Cast it, watch it land on a dummy, read the numbers.

**Launch, in the Editor:** `Cluck Wars / Test / Ability Lab` — opens
`Assets/_Game/Scenes/AbilityLab.unity` and enters play mode in one click. Opening the scene
and pressing Play works too.

**Launch, in a build (Windows or Android):** tap the **gear** at the top-right of the main
menu, turn on **Dev Mode** in the Settings sheet, then tap **ABILITY LAB** (it appears on the
main menu as soon as the toggle flips). It is stored in `PlayerPrefs`, so it survives
relaunches — you set it once per device, not once per session. With it off there is no route
to the lab at all.

### The controls (UI Toolkit) and the readout (IMGUI)

The lab is deliberately split across two UI stacks, and it is not a style choice:
**IMGUI receives no input in a player build.** This project runs `activeInputHandler: 1`
("Input System Package"), and the Input System "cannot generate input for IMGUI"
(`KnownLimitations.md`); with that setting "the `OnGUI` methods in your player code won't
receive any input events" (`UISupport.md`). Editor GUI is exempt, which is why this was
invisible while the lab was Editor-only. **Drawing still works** — only input is missing —
so the read-only telemetry stayed in IMGUI and everything you can touch moved to UI Toolkit.

**Toolbar (top right, always visible):**

| Button | Does |
|---|---|
| `LAB` | Opens / closes the control sheet |
| `RESET` | Resets every dummy right now |
| `TELE` | Shows / hides the telemetry readout |
| `MENU` | Shuts the runner down and returns to the main menu |

**Keyboard still works on Windows:** `F3` toggles the telemetry, `T` resets the dummies.
(`F1` / `F2` are still `DebugHud` / balance.) Neither exists on a phone, hence the toolbar.

**Control sheet (`LAB`):** class buttons (respawn), an ignore-legality toggle, a slot
selector (Passive / 0 / 1 / 2 / 3) and the list of everything equippable — tapping an entry
equips it immediately and clears that slot's cooldown. Below that: Frozen / Patrol toggles,
a reset-interval slider, a zero-cooldown toggle, and a time-scale slider down to 0.1x —
slow-mo is what makes windup and recovery readable.

**Telemetry (IMGUI, left, read-only):**

- **Ability Lab — cast**: press→cast latency in ms, the measured length of the last active
  window, the last cast's slot / name / hit count, and per-slot cooldown plus live refusal
  reason (`None`, `Cooldown`, `NoTarget`, `Stunned`, …).
- **Dummy state**: countdown to the next automatic reset, the dummy's current control state
  (stun / root / slow multiplier) and its cargo.

**Movement and casting go through the real input path.** The scene carries the same
`TouchControls` UIDocument as `Game.unity`, so the joystick and the four ability hexes drive
`AbilityController` exactly as they do in a match — judging feel through a different input
path than the real game would defeat the point of the tool.

### Things worth knowing

- **Loadout swaps are live; class swaps respawn.** `AbilityController`'s slots are plain
  serialized fields, so swapping abilities keeps your camera, the dummies' positions and any
  charge state. `ChickenController` resolves its stats once in `Spawned` and nothing watches
  `Class`, so changing class rebuilds the chicken instead of half-swapping it.
- **Ignore legality** offers every ability to every class. That is the point of the lab —
  the lobby refuses these combinations on purpose.
- **The lab does not force Peck or the signature.** A real match spawn does, so a chicken can
  always score; the lab honours exactly the four abilities you picked.
- **Press→cast latency is measured through `IInputProvider`,** so the number is real on both
  platforms. It used to sample `Keyboard.current` directly, which read as `—` on a phone —
  no keyboard, so no press was ever stamped. It watches the rising edge of
  `GetAbilityHeld(slot)` rather than calling `GetAbilityNPressed()`, because the press
  getters are one-shot: reading them here would consume the latch and swallow the cast that
  `FusionNetworkService` was about to send.
- **`hits=0` is not always a whiff.** Self-buffs (`AimShape.None`) and placed zones (Feather
  Trap, Root Egg, Smoke Roost) legitimately report no hits at cast time — the panel says
  `(n/a)` for those and `WHIFF` only where a zero really means one.
- **A GameManager is running,** despite there being no match. Ability input, movement and
  casting all hard-gate on `GameManager.IsMatchRunning`; the lab spawns one and holds its
  timer open. **No `PlayerBase` is spawned**, so banking has no destination and nothing can
  win or score. One food pile *is* spawned — Peck refuses without a pile in range — so the
  absent base, not the absent pile, is what keeps the match inert.
- **Solo only.** The lab pins `SessionMode.Solo`; the dummy patrol and the held match timer
  both rely on there being exactly one peer.

The scene is in Build Settings **last and ENABLED** (changed 2026-09-04), so it ships in
Windows and Android builds — a phone has no Editor menu to launch a scene from, so the lab
could not otherwise exist there. Its runtime scripts are no longer fenced behind
`#if UNITY_EDITOR`; only `AbilityLabMenu.cs` and `AbilityLabTests.cs` remain in `Editor/`.

Being last keeps Bootstrap = 0 / Game = 1 / Map = 2 unshifted, which
`ProjectConfigTests.BuildSettings_StartAtBootstrap_ThenGame_ThenBakedMap_ThenAbilityLab`
pins exactly — it asserts the full four-scene list in order, not "3 or more".

**What keeps players out is `PlayerPreferences.DeveloperModeEnabled`, not the build.** It
defaults to off, and `MenuUiController.OpenAbilityLab` refuses even if the row is somehow
shown. Because the lab now ships, it is held to shipped-code standards: `ILogService` only,
self-injection, and no `Debug.Log`.

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

## Test-session instrumentation (2026-10, docs/TEST_SESSION_PLAN_2026-10.md §2)

### Log line format (every line, every build)

```
[HH:mm:ss.fff][mm:ss.fff][Level][Source] message
```

First bracket: **UTC wall clock** (merges phone + EXE + Editor logs on one timeline). Second: time since process start (minutes may exceed 99). Parser regex (`LogLineFormat.Pattern`):

```
^\[(\d\d:\d\d:\d\d\.\d{3})\]\[(\d+:\d\d\.\d{3})\]\[(\w+)\]\[([^\]]+)\] (.*)$
```

At boot every build logs one `[Session]` line (`Session sha=<short>[+dirty] built=<UTC|editor> devtools=0|1 platform= device= gpu= api= quality= perfMode=0|1 res=WxH hz= os= unity= date= battery= bal=`), and `SessionChange ...` whenever the quality tier, Performance Mode or the balance hash changes. `bal=` is the 8-hex hash of the live-ops balance delta in force (`BalanceBook`, FNV-1a 32 of the canonical delta; the shipped assets alone read `bal=5465b825`, the hash of `{}`); two peers with different `bal=` play different numbers (warn-only; the joiner also logs `Balance mismatch with lobby ...` and the F1 panel shows it in yellow). A player reads its SHA from `Resources/BuildInfo.json`, generated by the build and deleted after it (`BuildInfoWriter`); the Editor reads `.git` directly (`built=editor`, no dirty flag).

### Dev-tools builds

`CLUCKWARS_DEV_TOOLS` turns the probes, the auto-test and the on-device overlay on (`DevTools.Available`; the Editor and development builds count too). Production builds have none of them. Build one with **Cluck Wars / Build / Test Session (Windows + Android, dev tools)**, or any build menu entry with `-cwDevTools` on the Editor command line or `CW_DEV_TOOLS=1` in its environment. Still `BuildOptions.None` (non-development: representative perf), with Frame Timing Stats turned on for the build.

| Source | Level | What |
|---|---|---|
| `Perf` | Info/Warn | `Hitch ms= frame= state= phase= abilities= gc= alloc=` for frames > 50 ms (Warn > 100 ms, capped at Info while `phase=loading\|covered`); every 5 s `Perf avg= p95= p99= max= hitches50= hitches100= fps= mem= gc= cpu= gpu=` |
| `Sync` | Debug | every 2 s of simulation, keyed by the tick boundary: `Sync t= st= rem= \| P<id>:x=,z=,hp=,cg=,cs=[,bot] ; ... \| bases=<corner>=<food>,... \| piles=...` (humans by PlayerId, bots `B<corner>`; `hp` = 1 in play / 0 removed) |
| `Visual` | Info/Warn | `Visual scan#N reason=reveal\|periodic\|manual renderers= ms= errShader= unsupported= nullMat= nan= outOfBounds= chickensNoRenderer= cams= mask=0x cover= offenders= frames=`, then one Warn per category with up to 8 paths. 3.5 s of uncovered play after the reveal, every 10 s in Game, F9 / three-finger tap / overlay button; spread over frames (150 renderers each), hitches on scan frames tagged `probe=1` |
| `Fusion` | Info | `Net rtt= loss=n/a players= master= tick= resim=` every 5 s (Fusion 2 exposes no loss figure) |
| `AutoTest` | Info | config, steps, `AutoTest code=XXXXXX`, rounds |

In the **Editor** the heavy probes (automatic Visual scans, Sync lines) are off unless an auto-test config is active or **Cluck Wars / Diagnostics / Heavy Probes In Editor** is ticked; F9 still scans, Perf / Net / Session stay on.

The **Editor** also ignores the live-ops balance delta by default: Play Mode runs the assets as they are on disk (`Balance: Remote Config is off in the Editor` at boot). Tick **Cluck Wars / Balance / Remote Config In Editor** to read the dev environment's `balance` key and this machine's cache (`<persistentDataPath>/balance/dev.json`); the assets are restored when Play Mode ends.

#### Publishing a balance delta (`tools/balance/`)

All scripts are Python 3, stdlib only, run with `python -I` from the repo root. Dry run is the default everywhere.

| Script | What |
|---|---|
| `make_delta.py proposal.json` | proposal → validated delta (only values that differ from the shipped assets) |
| `kitlab_to_proposal.py dump.json` / `delta_to_kitlab.py delta.json` | Kit Lab database dump → proposal, and the reverse (reset the page to what is published) |
| `publish_delta.py publish delta.json [--env dev\|production] [--apply] [--confirm-production]` | re-validates through `make_delta.py`, prints env, delta, expected `bal=` and the diff against what is published; `--apply` publishes to UGS Remote Config key `balance` (JSON type), reads it back, writes `tools/balance/history/<UTC>-<env>.json` + `latest-<env>.json`. Also `fetch --env dev\|production` and `rollback <history file>`. Production needs `--confirm-production` (a human flag) |
| `apply_delta.py delta.json [--apply]` | promotes a tested delta into the shipped `.asset` files (scalar lines and `Kit:` guids only; refuses enum-by-name fields and files with uncommitted changes) |

**The Balance Oracle reports; it does not gate.** Since 2026-11-20 the old `BalanceOracle` and its SCT pass/fail gate are gone, and no oracle result stops a delta or a publish. Measure a change before it goes out; a delta bypasses the tests that run on the assets, so the report is the check. The only oracle test that can fail is the drift guard `OracleReferenceTests.Measured_EqualsTheAcceptedReference` (accept the new numbers or undo the change). Output goes to `Captures/oracle/` (git-ignored). The six menus sit under `Cluck Wars/Balance/Oracle/`, each with a batch method for `-executeMethod`:

| Menu | Batch method (`CluckWars.EditorTools.`) | What |
|---|---|---|
| Bake map | `OracleMapBaker.BakeBatch` | bakes `OracleMapSO` from the scene |
| Report | `OracleMenu.ReportBatch` | `report-<UTC>.md/.json`: every kit against three plain Warriors |
| Record baseline (keep prices) | `OracleMenu.RecordBatch -oracleNote "why"` | stores what the assets measure in the reference; keeps the prices |
| Accept as balanced (recompute prices) | `OracleMenu.AcceptBatch -oracleNote "why"` | stores it and recomputes every kit's price |
| Sweep food | `OracleMenu.SweepFoodBatch` | `sweep-food-<UTC>.md/.json`: ten pile setups, read-only |
| Solve | `OracleMenu.SolveBatch` | reads `Captures/oracle/problem.json`, writes `proposal-<UTC>.md/.json` with a schema-1 delta; applies nothing. With a `"match"` block (weight above 0) the score gains the match simulator's term and the search is the guarded one of spec 7.1: see "Problem file, match block" below |
| Match timing | `OracleMenu.MatchTimingBatch` | plays 1 000 farm-only matches of four standard farmers, then 1 000 P0 mixed-design matches with combat, on one thread each, and logs the milliseconds one match costs (the match simulator's runtime budget); read-only |
| Match report | menu `Cluck Wars/Balance/Oracle/Match report`, `OracleMenu.MatchReportBatch [-oracleSeed n] [-oracleThreads n]` | plays Mixed (R 10), One per class (R 25) and Seat check (R 25) under P0, P1 and the mixed population, Forced kills (R 5) under P0, and the two head-to-head splits (P1 on seats 0 and 2, then 1 and 3; Mixed R 10); writes `match-report-<UTC>.md/.json` plus `match-sample-<UTC>.md` (six matches event by event). A kit has a call only when the three populations agree, else it is policy-sensitive with its range. Reaction delay baseline 0 s plus 0 to 0.1 s jitter (the first reports ran 0.3 s). Read-only |
| Match sensitivity | menu `Cluck Wars/Balance/Oracle/Match sensitivity`, `OracleMenu.MatchSensitivityBatch [-oracleSeed n] [-oracleThreads n]` | the match report plus the sweep: a 16-point Latin hypercube (delay 0 to 0.6 s, return threshold, flee radius, hunt detour, target rule, route) x 3 populations over the Mixed design at R 3, the one-factor delay table (0, 0.1, 0.3 s) and the Death Mark `LeaderPickWeight` values (1 to 4, through the balance book); writes `match-sensitivity-<UTC>.md/.json` with the sensitivity tables and the sweep's tags on the calls; read-only |
| Accept match as balanced | menu `Cluck Wars/Balance/Oracle/Accept match as balanced`, `OracleMenu.AcceptMatchBatch -oracleNote "why" [-oracleSeed n] [-oracleThreads n]` | plays the standard match report (three populations, no splits, about 100 s on 12 threads) and appends a match record (every kit's and class's win rate with its interval, per population) to `OracleReference.asset`'s `MatchRecords`. A record only: part 1's entries, prices and drift guard are untouched, targets stay at 25 %, nothing is learnt (D5 (b)). The match report's drift section compares with the last record |
| Match what-if | menu `Cluck Wars/Balance/Oracle/Match what-if`, `OracleMenu.MatchWhatIfBatch [-oracleWhatIf file] [-oracleSeed n] [-oracleThreads n]` | reads named balance deltas from `Captures/oracle/whatif.json` (or the file given), plays the standard match report designs for the shipped game and for each delta with the same seeds (common random numbers) and writes `match-whatif-<UTC>.md/.json`: per scenario against the baseline the win rate per kit and class under P0, P1 and mixed with margins and the difference, P1's played Spoiler kills-to-win table with the `KillTargets` labels, the forced kills, Spoiler kills per match, the Thief's steals and income, the share of matches ending on the clock and the mean length. A delta the `BalanceBook` refuses (or part of which it skips) is reported with the book's reason and the other scenarios still run. Read-only: nothing is applied or published. About 25 s a scenario at scale 0.4, 62 s at 1 |
| Bot arrival radius | `OracleMenu.BotArrivalRadius` | not a menu or `-executeMethod` batch: a public static that reads the arrival radius authored on the chicken prefab's `BotController`, which the simulator's sight rule needs |

**What-if file.** `{"replicateScale":0.4,"scenarios":[{"name":"ExecuteBounty 12","delta":{"schema":1,"assets":{"MatchConfig":{"ExecuteBounty":12}}}}, ...]}`. `replicateScale` (default 1, above 0) multiplies every design's replicates (`MatchReports.ScaledReplicates`; the margins grow by about the square root of its inverse); a scenario's `delta` is a schema-1 delta as an object (or as a string) and goes through `BalanceBook.Evaluate` over the shipped assets, exactly like a solver candidate. Names are unique and `baseline` is reserved (the unchanged game is always played first). In the report a difference marked `*` is larger than the two margins combined as if the runs were independent; the shared seeds make a difference steadier than that. Asset names in a delta are the `m_Name` of the asset (`MatchConfig`, `MarkKill`, `Spoiler`, `Thief`, `Snatch`, `Assassin`).

**Problem file, match block.** `problem.json` keeps its part 1 keys (`bandLow`, `bandHigh`, `maxPasses`, `maxFlatSteps`, `free`). A `"match"` object adds the match simulator's term and switches the search to the guarded one (spec 7.1); every key is optional and an unknown key is an error: `{"match":{"weight":1,"maxMovedValues":3,"screenReplicates":3,"confirmReplicates":10,"confirmBest":3,"threads":12,"seatCheckReplicates":25,"fullReports":true,"seed":"0x00000000012a4c82"}}`. `weight` 0 or absent leaves the solver exactly as part 1 built it and plays no match. The match term is the squared distance, in win-rate points, of each kit's and class's rate from 25 % beyond one interval half-width, averaged over P0, P1 and the mixed population (a kit tagged policy-sensitive adds nothing); it sits next to part 1's seconds squared. Per pass the search screens every one-step move of every free value at `screenReplicates` of the Mixed design, confirms the best `confirmBest` at `confirmReplicates` (each replicate is a block; a move counts only when its gain beats twice the standard error of the paired block differences), takes the best, stops moving new values after `maxMovedValues`, and finishes with a holdout on fresh seeds that reverts the moves that do not hold, last first. A flagged seat check (full mirrors, three populations) at the start aborts the solve and changes nothing. `fullReports` plays the standard match report for the start and the proposal (about 100 s each on 12 threads) so the proposal shows every kit's and class's win rate before and after, the kits still policy-sensitive and the Spoiler kills-to-win table. The proposal is a schema-1 delta; nothing is applied. `OracleProblem.DefaultMatchFree(inputs)` lists the D8 (b) values (the Death Mark `LeaderPickWeight`, the three pile amounts, `PeckCastSeconds` per class, every non-Peck kit ability's cooldown fields and aim radius) with steps 0.25 / 1 / 0.05 / 0.25 / 0.25; the problem file names its own `free` list. `MatchConfig.ExecuteBounty` is a delta field (an integer with `[Min(0)]`, not deny-listed) and may be listed with an explicit `max`.

Leave Play mode first (Solve refuses in Play mode). `-cw_timescale` (above, solo dev builds) is for runs of the real game at speed; the oracle itself does not need it.

`publish_delta.py` reads `UGS_SERVICE_KEY_ID`, `UGS_SERVICE_SECRET`, `UGS_PROJECT_ID` (and optionally `UGS_ENV_DEV_ID` / `UGS_ENV_PRODUCTION_ID`) from the environment only. After a publish, start a build and check `bal=<hash>` in the Session log line or the F1 HUD matches the hash the tool printed; publish to `dev` first, production only as its own deliberate step. Tests: `python -I -m unittest discover -s tools/balance/tests -t tools/balance/tests` (fake HTTP, temporary copies of the Data folder; no network, no writes to `Assets/`). The `/balance` user skill drives all of this.

Always on (breadcrumbs): `MatchOverlays` `Cover on/off`, `Cover off: covered= lateJoin= reducedMotion=`, `GET READY held Xs`; `ArenaReadiness` `Render tier: rp= quality= post= haze=`; `Reveal` `first-use hitspark|invisibility|fencehole|telegraph frame=N` (once per scene load); `Fusion` join / leave / master change / shutdown with `cover= revealed= state=`.

### Auto-test (dev-tools builds)

| Key | Meaning |
|---|---|
| `cw_autohost 1` | HOST → class → loadout → READY → wait for the code → log `AutoTest code=XXXXXX` + write `<persistentDataPath>/cw_autotest.json` → START |
| `cw_autojoin CODE` | JOIN → class → loadout → READY → JOIN MATCH; retry every 5 s for 2 min |
| `cw_class Warrior\|Speedy\|Fatty\|Assassin` | case-insensitive; invalid → Warn + Warrior |
| `cw_autostart N` | host: press the waiting room's START once N humans are in (1 = at once) |
| `cw_autostart_timeout S` | …or after S seconds with whoever is there (default 90) |
| `cw_autoloop N` | host: PLAY AGAIN for N extra rounds (round 2+ re-STARTs as soon as the previous round's humans are back, max 15 s: `AutoTest loop restart humans=k wait=Xs`), then stay on the results; joiners just follow. Every peer logs `AutoTest loop round=k/N`; the host logs an Error if its PLAY AGAIN did not restart the round within 10 s |
| `cw_autoplay 1` | fake local input: piles → base, weave, unstick, an ability every 3 s. Runs on **simulation time** (BL-208): one decision per two ticks (`AutoPlayInputProvider.DecisionStepSeconds`), holds and press latches in sim seconds, so a time scale changes the speed of the match, not what the bird decides per second of it |
| `cw_personality aggressive\|runner\|balanced\|fighter` | this seat's auto-play style (default: PlayerId mod 3, which never gives the fighter). **fighter** plays the kit's attacks at legal targets through the normal input path: Stun / Shove at the nearest rival in reach, a rival with a Peck cast running first; Steal at a loaded rival in reach; Spoiler: mark, chase the marked rival, stun it so the stun outlives the 3 s arm, press Kill. It forages and banks in between and casts nothing else (`FighterTactics`) |
| `cw_seed N` | dev tools, solo: fixes the arena's interior obstacles, the corner permutation, the bot kit lineup and `UnityEngine.Random` (`DevSeed`); the MapGen line reads `solo-random (seed N)`. Windows command line only. Same seed + same `cw_perk` / `cw_botperks` / `cw_personality` = the same match to start from |
| `cw_devmode 1` | Developer Mode on |
| `cw_profile NAME` | Unity Services authentication profile (A-Z a-z 0-9 - _, max 30; other characters dropped). Give each EXE on one PC its own (`-cw_profile client2`): they otherwise share one anonymous identity and the second join fails with "player is already a member of the lobby". Unset: default profile |
| `cw_timescale N` | simulation speed of a **solo** match (dev tools only; 1 to 20, clamped; shared matches ignore it). Auto-play, the invariant probes and the Telemetry first-contact check run on simulation time (`SimClock`), so the round-start probes hold at any speed; what still varies between speeds is in "Accelerated runs" below |
| `cw_hud 1` / `cw_hud 2` | overlay at boot: 1 = compact one-line strip (FPS \| p95 \| hitches \| Visual \| RTT, top-left, translucent; leaves the match HUD visible), 2 = full panel |

Android (`-e` string extras; `--ei` / `--ez` work too). **Always use `am start -S`** (force-stop first): the extras are read once per process and then removed from the intent, and an activity re-opened from Recents ignores them, so a run never re-hosts / re-joins by accident:

```
adb shell am start -S -n com.cluckwars.game/com.unity3d.player.UnityPlayerGameActivity     -e cw_autojoin ABC123 -e cw_class Speedy -e cw_autoplay 1 -e cw_hud 1
adb shell cat /storage/emulated/0/Android/data/com.cluckwars.game/files/cw_autotest.json
```

Windows / Editor command line (booleans: `1`/`true` or a bare flag):

```
CluckWars.exe -cw_autohost -cw_class Warrior -cw_autostart 3 -cw_autoloop 2 -cw_autoplay
```

Editor via the MCP `script-execute` (survives the domain reload into Play Mode; cleared automatically when Play Mode is exited):

```
CluckWars.Diagnostics.AutoTest.EditorConfigure("autohost=1 class=Warrior autostart=2 autoplay");
CluckWars.Diagnostics.AutoTest.EditorClear();
```

A production build given any `cw_*` key logs one Warn (`AutoTest ignored: dev tools not available in this build`) and does nothing else.

### Accelerated runs and the fighter (BL-208, slice 1, 2026-11-21)

`tools/test-session/scenario-bots.ps1` (Windows EXE only, no adb) drives both runs; `tools/test-session/bots-compare.py LABEL=client1.log ...` tables per-seat banked food from the MATCH SUMMARY blocks and counts Invariant FAIL / Error / Telemetry lines. Output goes to `Captures/bots-<date>/<Label>`.

- **Accelerated solo:** `scenario-bots.ps1 -Mode timescale -Scale 5 -Seed 20261121 -Perk Warrior_Relentless -BotPerks Warrior_Bully,Fatty_Hoarder,Speedy_Slippery -Personality runner`. Same seed and seat flags give the same world (`World ... static=` hash and the corner permutation match at every speed).
- **Fixed in slice 1:** auto-play and the invariant probes (round-start delay, base-intrusion and aura-orphan timers) read the runner's simulation time (`SimClock`), not `Time.unscaledTime` / `realtimeSinceStartup`; auto-play decides once per two ticks (`TickCadence`, `AutoPlayInputProvider.DecisionStepSeconds`). Before: the round-start probe waited a real 0.5 s, which is 5 s of match at x10 (chickens already walking, pile eaten, timer down), so x10 and x20 logged 74 and 83 Error lines; the peck cadence (0.8 s), cast cadence (3 s), stuck window (2 s) and press holds were real seconds, i.e. N times slower in game terms. After: 0 Invariant FAIL and 0 Error at x1, x5 and x10.
- **Still varies (measured):** per-seat banked food. Two runs at the SAME speed and seed differ as much as runs at different speeds (x1 against x1: up to 31 food on a seat in a round), so the spread is run-to-run chaos, not the time scale: the stick is set once per rendered frame and Fusion reads it per tick, so the tick at which an input starts depends on the frame phase, and the bots, collisions and Peck cuts amplify that. Seat means over 3 rounds x 4 seats stay within 22.7 to 29.6 food at x1, x1 repeat, x5, x5 repeat and x10, no trend with speed. Use means over many matches (the simulator's validation does), never one seat of one match. Full determinism needs the stick computed inside `OnInput` per tick; not done.
- **Four clients, fighters:** `scenario-bots.ps1 -Mode fighters -Rounds 6`: Spoiler/fighter (host), Warrior_Bully/fighter, Fatty_Hoarder/runner, Speedy_Slippery/runner. The fighter plays Stun / Shove / Steal at legal targets and the Spoiler marks, chases, stuns and kills (`FighterTactics`).
- **Telemetry** lines: see the `Telemetry` row under Source tags. Check one against `MatchConfig.asset` (`ExecuteBounty`): `kill ... bounty=` must equal it.

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
| `Session` | boot / quality / Performance Mode identity line (every build) |
| `Perf`, `Sync`, `Visual`, `AutoTest` | test-session probes (dev-tools builds) |
| `Telemetry` | match events for Balance Oracle v2 (BL-208), `Info`, every build: `steal tick= thief= victim= amount=` (victim's authority, what really drained), `peckcut tick= pecker= class= cause=stun\|shove\|execution\|empty-pile` (pecker's authority), `kill tick= killer= victim= payout= bounty= victim_cargo= gained= unfit= victim_pecking=` (killer's authority; payout = bounty + victim_cargo, bounty = `MatchConfig.ExecuteBounty`, victim_cargo = what the victim carried and the execute drained; the loot is ordinary cargo held to the killer's free room since 2026-11-24, so `gained` is what really went into his cargo and `unfit` = payout - gained is what did not fit and is gone, victim's beak first, then the bounty), `contact tick= seat= rival= round= t= dist=` (first time per round a chicken is within the smallest offensive reach of another; the invariant probe, authority chickens only). Seats are `B<corner>` for a bot, `P<playerId>` for a human. Each event is written by one peer, so a session log set holds each once; a solo log holds all |
| `Reveal`, `ArenaReadiness`, `MatchOverlays` | reveal gate + first-use breadcrumbs |

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

During the 3-2-1 countdown, **mash the four slot keys (Scheme A arrows, or Q / W / R / E on Scheme B) and the touch ability hexes**.

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
