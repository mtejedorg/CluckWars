# Project State — Snapshot

**Live source of truth for "where are we right now."** Updated each session.
Designed to be the first doc an agent reads after `CLAUDE.md` to understand
what's shipped, what's in flight, and what's blocked on testing.

---

## Latest tag

`v0.3.0-alpha` — Pre-test polish + build tooling. Pinned at commit `312f511`.

Earlier tags:
- `v0.1.0-alpha` (`10e527c`) — Core gameplay loop closed.
- `v0.2.0-alpha` (`7ee1742`) — Phase 9 polish (audio service, registries, reconnection).

All tags are local until pushed.

---

## What works (Phases 1–9 complete + Phase 10 in progress)

### Core loop
- 4 classes (Warrior / Speedy / Fatty / Assassin) — selectable in Bootstrap menu.
- Movement: keyboard (WASD) + touch (joystick). `CompositeInputProvider` ORs both.
- Combat: button-mash proximity attack, HP, hit reaction, 5-sec death stun.
- Food collect from piles → carry → deposit at base.
- Food drop on death; pickup-on-overlap.
- All 8 abilities equippable: Speed Burst, Egg Shell, Roll & Trample, Invisibility, Spine Coat, Turtle Mode, Sneaky Steal, Doppelganger.
- Per-class ability allowlist exists on `ChickenStatsSO` (empty = no restriction).

### Match lifecycle
- `GameManager` state machine: `WaitingForPlayers` → `Active` → `Ended` → restart.
- Host-controlled match start (lobby with "START MATCH" button for master client).
- Intro countdown ("3, 2, 1, GO!") before play begins each round.
- Win condition: first to `MatchConfigSO.FoodTargetToWin` OR most food when timer expires.
- Match restart loop: bases zeroed, piles refilled, pickups despawned, chickens reset + teleported to their corners.

### Networking
- Photon Fusion 2, Shared Mode. Solo mode = single-player Fusion runner.
- Photon Cloud relay already enables internet play (not LAN-only).
- Bootstrap menu: Solo / Host / Join. Host creates a UGS Lobby → unique 6-char join code. Joiner enters code or picks from lobby browser. Code becomes the Fusion session name.
- Photon AppId configured: `PhotonAppSettings.AppIdFusion = 259bda28-…`.
- Reconnection: `OnShutdown` → "SESSION ENDED" overlay → auto-return to Bootstrap.
- Connect / Disconnect / ConnectRequest / ConnectFailed callbacks all log explicitly.

### UGS (Phase 10 — code complete, pending Editor setup)
- `UGSService` (Auth + Lobby) bound in `ProjectInstaller` via `#if UGS_DISABLED` guard.
- `NullUGSService` used when `UGS_DISABLED` is defined (demo/offline fallback → "cluck-lan").
- Host: `CreateLobbyAsync` → join code displayed in Bootstrap + MatchHud lobby panel.
- Join: `JoinLobbyByCodeAsync` (type code) or `QueryLobbiesAsync` (browse list).
- Join code = Fusion session name — one code serves both UGS and Photon matchmaking.
- Lobby heartbeat (15s) runs automatically while host is in-session; stops on `LeaveLobbyAsync`.
- **Requires Unity Dashboard project link** — see "Outstanding before next test session" below.

### UI
- **Bootstrap menu**: procedural UGUI canvas, class + mode picker, keyboard shortcuts (1-4, S/H/J, SPACE).
- **MatchHud** (UGUI): top bar timer + per-player totals (Players: X / Y), bottom-left HP + cargo bars, centered match-end leaderboard with restart countdown, session-end overlay, lobby panel with Start button, intro countdown overlay, hit-flash on local damage.
- **TouchControlsHud**: joystick + attack + 2 ability buttons. Ability button tint = equipped `AbilityBaseSO.AccentColor`. Cooldown radial fill.
- **DebugHud** (F1 toggle): FPS, network state, GameManager state, local chicken stats, base ownership, pickup count.

### Map / camera
- `MapGenerator`: procedural plane + 4 invisible boundary walls + 4 corner bases + 1 large center pile + N small piles on a jittered ring. All master-spawned.
- Spawn points coincide with base positions (`Vector3.Lerp(corner, origin, 0.15)`); chickens spawn at their base.
- `MatchCamera`: orthographic isometric (45° yaw + 30° pitch), smooth-follows the local chicken with configurable `_followSmoothTime` (default 0.15s) and `_orthoSize` (default 8 — closer than ART.md §2's full-map fixed view).

### Registries (consolidated assets)
- `PrefabRegistrySO`: Chicken, Doppelganger, FoodPile, FoodPickup, PlayerBase, GameManager.
- `AudioRegistrySO`: Combat / Cargo / Ability / Match / Music clip refs.
- `ColorSchemeSO`: HUD palette, food-pile states, button colors, cooldown dim.
- `ChickenClassRegistrySO`: per-class stats + tint.

All bound app-wide in `ProjectInstaller`. Empty asset slots bind a runtime-empty instance with defaults so consumers never crash.

### Editor tooling
- `Cluck Wars / Build / Windows` (`Ctrl+Shift+W`) — `StandaloneWindows64`, output `Builds/Windows/CluckWars.exe`.
- `Cluck Wars / Build / Android` (`Ctrl+Shift+A`) — IL2CPP + ARM64 + minSdk 24, `Builds/Android/CluckWars-<version>.apk`.
- `Cluck Wars / Build / Windows + Android` (`Ctrl+Shift+B`) — sequential.
- `Cluck Wars / Build / Reveal Builds Folder`.

---

## Outstanding before next test session

### Pending Maestro (Editor work)
- **UGS Dashboard link** (required for Phase 10): Unity Editor → Edit → Project Settings → Services → link to your Unity Cloud Organization. Then enable Authentication and Lobby services in the Dashboard. Without this, `UGSService.InitializeAsync()` will throw.
- Optional: fill `AudioRegistry` clips, tune class ability allowlists in `ChickenStatsSO.AvailableAbilities`.

### Pending agents (code)
- Nothing blocking after Phase 10 code landed. Test session will validate UGS live paths.

---

## Known bugs / open questions (need device testing)

1. **Third player can't connect** (reported 2026-05-08). Connect callbacks now log every transition — next test should reveal whether it's `OnConnectFailed`, region mismatch, or session-name collision.
2. **Solo-on-Android movement** (reported 2026-05-08, may already be fixed). Was: chicken doesn't move with joystick. Likely root cause: spawn collision drift, addressed by spawn jitter + base-aligned spawns. Verify after rebuild.
3. **Spawn-stacking** (reported 2026-05-08, mitigated). `MapGenerator.ComputeSpawnPoints` now warns if `_baseCornerDistance < 1`. Spawn jitter in `MatchBootstrapper.HandlePlayerJoined` breaks symmetry even if positions collide.

---

## Deferred work — bundled for the dedicated test session

All require real-device or playtest data; queued so they don't get done piecemeal.

- Network desync hunting (cross-device LAN).
- On-device FPS / draw-call / memory profiling.
- AnimatorController state authoring (hit / attack / stunned animations).
- VFX particle systems (sparkles, glows, impacts, stun stars, ability accents).
- Audio clip recording / mixing.
- Balance pass (food rates, ability cooldowns, attack damage, HP).
- Mobile layout fine-tune for actual phone aspects.
- `MatchCamera._orthoSize` / `_followSmoothTime` final tuning on device.

---

## Recent commits (most recent first)

```
aa6a154 Lobby UX + Fusion connect callbacks
8fca391 Fix CS0104: ambiguous LogLevel
7ccb850 Lobby + spawn-at-base
fc09ddc Spawn-position safety
c4824d9 ChickenController: verbose log when _movement is null
d572e97 MatchCamera: smooth-follow local chicken + closer default zoom
85a96b3 Fix CS0221: ChickenClass is byte-backed
312f511 Build menu                                           ← v0.3.0-alpha
383dd67 Pre-test polish: debug HUD, nameplate, allowlist, hit-flash
226fab4 All 8 ability assets with distinct AccentColors
46a41e4 Pre-Phase-8 wiring
fdae00d Pre-Phase-8 advancements (iso cam, UGUI HUD, intro, nameplates)
7ee1742 Phase 9 wiring                                       ← v0.2.0-alpha
```

`git log --oneline -20` for more.

---

## Branch / push status

- All work on `develop`.
- `main` has not been merged since project start (per Maestro's decision — wait for stable test pass before promoting).
- ~41 local commits ahead of origin/develop at time of last snapshot. Push tags + branch when convenient.
