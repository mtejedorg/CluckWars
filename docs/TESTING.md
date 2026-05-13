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

---

## Running

### Solo (PC or Android)

1. Pick a class (1-4) and press **S** for Solo + SPACE.
2. `GameMode.Single` runner spawns. No lobby — `GameManager` auto-starts the match.
3. Movement works immediately after the 3-second intro countdown.

### Multiplayer (LAN via Photon Cloud relay)

1. **Host** (any device): pick class, press **H** + SPACE. Lands in the **lobby** (`MATCH LOBBY` overlay).
2. **Joiner(s)**: pick class, press **J** + SPACE. Connects to the host's room. Sees "Waiting for host to start the match…" overlay.
3. Host clicks **START MATCH** when ready. Player count is shown live as `Players: X / Y` (Y from `MatchConfigSO.MaxPlayers`, default 4). **No minimum — host can start with just themselves.**
4. Intro countdown plays on every peer simultaneously, then the round begins.

Session name is hardcoded to `cluck-lan`. Both peers must use that exact name + same AppId.

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

## Deferred to the test session (won't have data until then)

See `docs/STATE.md` § "Deferred work". Items there are all blocked on either:

- A real Android device for profiling / FPS / mobile layout
- 2+ devices on the same network for LAN networking validation
- Playtest sessions for balance data
- Hand-authored art / animation / audio assets

When the test session is scheduled, run through these in order:

1. **Build sanity** — `Ctrl+Shift+B` produces clean Windows + Android binaries. No compile errors. Reveal folder, confirm files present.
2. **Solo Windows** — class select works, match starts, all 8 abilities equippable and functional, food loop completes, win condition fires, restart loop works.
3. **Solo Android** — touch HUD responds, joystick + attack + abilities all register, camera follow is acceptable, FPS roughly steady.
4. **Two-device LAN** — Windows host + Android client. Lobby shows player count, host starts match, both chickens spawn at distinct corners, damage / drain / deposit / pickup RPCs cross the wire correctly, match end + restart cycle on both peers.
5. **Three- or four-device LAN** — repeat (4) with more joiners. Catch the "third player failed to connect" if it returns.
6. **Balance pass** — playtest 5-10 min matches, tune `MatchConfigSO` + per-ability `Duration` / `Cooldown` + `ChickenStatsSO` per-class numbers.
7. **Audio pass** — drop AudioClip refs into `AudioRegistry.asset`. Mix levels.
8. **Animator pass** — author Hit/Attack/Stunned/Idle state machine + transitions in the AnimatorController.
9. **VFX pass** — particle systems for the events listed in `ART.md` §7.
