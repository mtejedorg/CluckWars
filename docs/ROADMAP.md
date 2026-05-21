# Development Roadmap

**Target:** Playable demo on LAN with 4 players, all classes, core abilities, full match loop.

**Status snapshot:** Phases 1–10 shipped + closed (demo is feature-complete and playable). Device-dependent work is bundled into the **Dedicated test session** below.

> **ACTIVE WORK — Phase R: v0.3 Mechanics Refactor.** The GDD moved v0.2 → v0.3 (basic attack removed, ability system redesigned, interaction/control system rewritten). The shipped code still implements v0.2. **Phase R** (near the bottom of this file, before the Post-Demo section) is split into **Part A — Parity** (rebuild today's playable state under v0.3 rules) and **Part B — Continuation** (the new content v0.3 unlocks). Start there.

For current-state details: `docs/STATE.md`. For architecture: `docs/ARCHITECTURE.md`. For build + diagnostic flows: `docs/TESTING.md`. For the design itself: `docs/GDD.md` (now v0.3).

---

## Phase 1: Bootstrap & Foundation ✅

### Goals
- Project structure & packages installed
- Zenject setup with DI binding
- Networking interface + Fusion solo session
- First walking chicken (no combat, no abilities)

### Tasks
- [x] Unity 6000.3 LTS (Unity 6) project created, URP configured, folder structure initialized
- [x] Photon Fusion 2 imported & verified
- [x] Zenject imported & configured (`ProjectContext` + `SceneContext`)
- [x] `INetworkService` + `FusionNetworkService` implemented (Single mode for solo dev)
- [x] `ChickenController` (NetworkBehaviour) with basic movement
- [x] Bootstrap → Game scene transition wired
- [x] WASD controls working on Windows via Input System

### Deliverable
Single chicken walking on empty map using keyboard. ✅

---

## Phase 2: Character Systems ✅

### Goals
- 4 character classes with distinct stats
- Animation system (locomotion blend; combat triggers reserved for Phase 3)
- ScriptableObject-based class definitions

### Tasks
- [x] `ChickenStatsSO` for each class (Warrior, Speedy, Fatty, Assassin) authored in `Data/Classes/`
- [x] `MatchConfigSO` with tunable values (timer, food target, tick rate)
- [x] `ChickenClass` enum + `ChickenClassRegistrySO` mapping class → stats + tint
- [x] `ChickenAnimator` driving `Speed` blend from local position-delta velocity
- [x] `ChickenVisuals` applying per-class tint via `MaterialPropertyBlock`
- [x] `ISessionSelectionService` carrying menu choice across the scene transition
- [x] `CharacterSelectController` (placeholder IMGUI; UGUI menu lands in Phase 7)
- [x] Class-aware spawn: `MatchBootstrapper` sets `ChickenController.Class` via `OnBeforeSpawned`

### Deliverable
4 different chickens with working locomotion animation, selectable at start. ✅

### Deferred to Phase 7
- Proper UGUI character-select screen (replacing the IMGUI placeholder)
- Per-class meshes (placeholder capsule + tint for now)

---

## Cross-Cutting Infrastructure ✅ (off-cycle, between Phase 2 & 3)

Not part of the original phase plan; added after Phase 2 close-out to debug a
silent injection failure in the Bootstrap scene. Stable on `develop`.

- [x] `LogLevel` enum (Verbose / Debug / Info / Warn / Error / Off)
- [x] `ILogService` + `UnityLogService` with `MinLevel` filter, `Source` tag, `[mm:ss.fff][Level][Source] message` format
- [x] Bound app-wide in `ProjectInstaller`; `_logMinLevel` defaults to Verbose
- [x] Diagnostic logging across `CharacterSelectController`, `SceneLoader`, `MatchBootstrapper`, `FusionNetworkService` (incl. per-tick OnInput at Verbose), `ChickenController`
- [x] **Lazy-load fix** at three self-inject sites: drop `ProjectContext.HasInstance` guard, call `ProjectContext.Instance.Container.Inject(this)` directly. `HasInstance` returns false until something reads `.Instance`, which silently disabled all self-injection on cold-start scenes.

### Deliverable
Single chokepoint for log output with level filter; structured Console narrative for the full Bootstrap → Game spawn flow makes future bugs trivially diagnosable.

---

## Phase 3: Combat System (Week 3-4)

### Goals
- Attack button mashing mechanic
- Damage, HP, hit reaction
- Death stun (5 seconds)

### Tasks
- [x] `ChickenCombat` component with proximity detection (`Physics.OverlapSphere` on Swing; nearest valid target selected)
- [x] Attack input handling + button mashing feel (`AttackTimer` throttles held-button to one swing per `AttackCooldown`)
- [x] Damage calculation (`HP -= stats.Attack` via `RPC_ApplyDamage` to target's StateAuthority — Shared Mode authority crossing)
- [x] Hit animation trigger (`ChangeDetector` on `HP` decrease → `ChickenAnimator.TriggerHit()` on every peer)
- [x] Stun on death (5 sec via `TickTimer`; `ChickenController` skips movement while `IsStunned`)
- [x] Dropped cargo on death (Phase 4b). `FoodPickup` NetworkBehaviour spawned by `ChickenCargo.HandleDeath` carrying cargo amount; auto-despawns when drained or after 30s. Picked up by walking over it.

### Prefab work (Maestro, in Editor)
- Add `ChickenCombat` to the Chicken prefab.
- AnimatorController needs `Attack` (trigger), `Hit` (trigger), `Stunned` (bool) parameters — hashes already declared in `ChickenAnimator`.

### Deliverable
Two chickens can fight, one dies, stunned for 5 sec. (Solo dev session can only verify "no target in range" verbose logs; full validation in Phase 5.)

---

## Phase 4: Food System (Week 4-5)

### Goals
- Food piles with cargo collection
- Cargo capacity per class
- Base depositing

### Tasks
- [x] `FoodPile` NetworkBehaviour with `[Networked] Amount` + `RPC_Drain` (master client owns scene-placed piles)
- [x] `FoodPileVisuals` — scales mesh + tints color from full→empty per `Amount / MaxAmount`
- [x] `ChickenCargo` NetworkBehaviour: per-tick collect from nearest in-range pile, deposit at nearest in-range base
- [x] `PlayerBase` NetworkBehaviour with `[Networked] FoodTotal` + `RPC_AddFood` (per-player ownership wiring deferred to Phase 7 win condition)
- [x] `CargoHud` IMGUI overlay — local cargo / capacity, base total, stun indicator
- [x] Cargo zeroed on death via `ChickenCombat.OnDeath` subscription
- [x] Food pickup prefab (dropped cargo as collectables) — Phase 4b: `FoodPickup` NetworkBehaviour spawned by `ChickenCargo.HandleDeath` carrying the chicken's cargo amount; any chicken in pickup range drains it via `RPC_Drain` and credits its own cargo. Auto-despawns when empty or after `_despawnDelay` (30s default).

### Prefab/scene work (Maestro, in Editor)
- Add `ChickenCargo` to the Chicken prefab.
- Author a `FoodPile` prefab (or scene-placed NetworkObject): NetworkObject + FoodPile + FoodPileVisuals + a child mesh, baked into Game.unity.
- Author a `PlayerBase` placeholder: NetworkObject + PlayerBase + a tinted child mesh, placed in Game.unity.
- Add a single empty GameObject to Game.unity with the `CargoHud` component.

### Deliverable
Players can collect from piles, see visual feedback, deposit at base. (Solo-testable end-to-end except food-drop pickups.)

---

## Phase 5: Multiplayer Networking (Week 5-6)

### Goals
- Extend `FusionNetworkService` from Single → Shared mode
- Multiple players on LAN
- Sync all networked state added by Phases 3-4

### Tasks
- [x] `FusionNetworkService` skeleton with `StartHostAsync` / `JoinSessionAsync` (Phase 1)
- [x] `NetworkBehaviour` on `ChickenController` (Phase 1)
- [x] Input sync via Fusion `PlayerNetworkInput` struct (Phase 1)
- [x] `NetworkBehaviour` on `FoodPile`, `PlayerBase` (Phase 4 — `GameManager` deferred to Phase 7)
- [x] State sync via `[Networked]` for HP, IsStunned, Cargo, FoodPile.Amount, PlayerBase.FoodTotal (Phase 3-4)
- [x] Bootstrap-scene mode picker: Solo / Host / Join via S/H/J keys; session name hardcoded to `cluck-lan` for the demo (UGUI session entry lands in Phase 7)
- [x] `MatchBootstrapper` branches on selected `SessionMode`; late-join already works because `HandlePlayerJoined` filters `player == runner.LocalPlayer` so each peer only spawns its own chicken and Fusion replicates the rest automatically
- [ ] Tested with PC host + Android client on same Wi-Fi — **bundled into dedicated test session**.

### Photon prerequisites
- `Assets/Photon/Fusion/Resources/PhotonAppSettings.asset` has `AppIdFusion = 259bda28-...` ✓. "LAN" still routes through Photon Cloud relay; both peers use the same AppId + same session name to be matchmade.

### Deliverable
2 devices (Windows PC host + Android client) can play together via Photon Cloud-routed Shared session.

---

## Phase 6: Abilities (Week 6-7)

### Goals
- Ability system architecture
- First 4 abilities (Speed Burst, Egg Shell, Roll Trample, Invisibility)
- Cooldown tracking

### Tasks
- [x] `AbilityBaseSO` abstract class — `OnActivate(ctx)` / `OnDeactivate(ctx)` + Duration / Cooldown / DisplayName / ShortLabel / AccentColor / optional AbilityAnimationClip.
- [x] `AbilityContext` (passed to ability hooks; holds the `ChickenController` ref so SOs are free of `GetComponent` calls).
- [x] `SpeedBurstAbilitySO` — multiplies `ChickenController.MoveSpeedMultiplier` while active.
- [x] `EggShellAbilitySO` — toggles `MovementLocked` + `DamageImmune` while active.
- [x] `RollTrampleAbilitySO` — one-shot `Physics.OverlapSphere` sweep ahead of the caster, slams hit chickens with damage via `ChickenCombat.RPC_ApplyDamage`.
- [x] `InvisibilityAbilitySO` — drives `ChickenController.VisualOpacity`; `ChickenVisuals` polls in `LateUpdate` and re-pushes the tint with the new alpha. Local-only fade; networked invisibility deferred to Phase 9.
- [x] `TurtleModeAbilitySO` — sets `MoveSpeedMultiplier` (slow) + `DamageResistance` (absorbs %).
- [x] `SpineCoatAbilitySO` — toggles `ReflectDamage`; `RPC_ApplyDamage` bounces incoming damage to the attacker via a self-RPC, recipient eats nothing.
- [x] `SneakyStealAbilitySO` — Phase 6c. One-shot OverlapSphere find-nearest-enemy-with-cargo, optimistic self-credit + `ChickenCargo.RPC_DrainStolen` to take from the victim's authority. Capped to thief free space and victim cargo.
- [x] `DoppelgangerAbilitySO` — Phase 6d. `Runner.Spawn` a `Doppelganger` decoy at a side-offset from the caster; spawner sets `MimickedClass` + `LifetimeTimer` in `onBeforeSpawned` so every peer sees the right tint + despawn time from tick zero. `Doppelganger` script self-injects `ChickenClassRegistrySO`, applies tint locally on Spawned, despawns when the timer expires. **Prefab still needs authoring in the Editor** — see "Editor work" below.
- [x] `AbilityController` on Chicken (NetworkBehaviour): 2 slots (slot 0 universal, slot 1 Assassin-only), `[Networked] ActiveSlot` + `ActivationTimer` + per-slot cooldown timers, single-active-at-a-time policy.
- [x] Ability button UI with radial cooldown indicator — `TouchControlsHud` now adds a `Image.FillMethod.Radial360` overlay per ability button, fillAmount = remaining/total cooldown.
- [x] Movement / damage hooks on `ChickenController` (`MoveSpeedMultiplier`, `MovementLocked`, `DamageImmune`) read by `ChickenMovement` and `ChickenCombat.RPC_ApplyDamage`.

### Prefab/asset work (Maestro, in Editor)
- Create `SpeedBurst.asset` and `EggShell.asset` under `/Assets/_Game/Data/Abilities/` via the new menu items (`Cluck Wars/Ability/Speed Burst` and `…/Egg Shell`). Tune Duration / Cooldown there; defaults are placeholders.
- Add `AbilityController` to the Chicken prefab.
- Drag the new ability assets into `AbilityController._slot0` (and `_slot1` for Assassin builds) on the Chicken prefab.
- **Phase 6d**: author a `Doppelganger.prefab` — NetworkObject + `Doppelganger` script + `ChickenVisuals` + chicken-shaped child mesh + small trigger collider. No controller / combat / cargo / animator. Then create a `Doppelganger.asset` via `Cluck Wars/Ability/Doppelganger` and drag the prefab into the asset's `_decoyPrefab` slot.

### Deliverable
Players can equip and activate 1 ability per chicken (2 for Assassin once `_slot1` is populated). Cooldowns drawn as radial fill on the touch HUD. Roll & Trample + Invisibility ship in Phase 6b once the system is validated solo.

---

## Phase 7: Game Loop & Balancing (Week 7-8)

### Goals
- Full match: spawn → play → end condition
- Basic balancing (food targets, timer, pile sizes)

### Tasks
- [x] `GameManager` (NetworkBehaviour) with `MatchState` enum (`WaitingForPlayers` → `Active` → `Ended`), `[Networked]` `MatchTimer` (`TickTimer`), `WinnerPlayer` (`PlayerRef`), `WinnerFoodTotal` (float). Master-client owned.
- [x] Win condition check (first to `MatchConfigSO.FoodTargetToWin` OR highest total when timer expires). Throttled to 4× / sec via `Runner.SimulationTime`.
- [x] Match timer (top-center MM:SS overlay) + end screen (centered banner showing winner + final food). Both rendered by `CargoHud` IMGUI.
- [x] `MatchBootstrapper` spawns the `GameManager` prefab on master-client side after `StartGame` completes.
- [x] Spawn system polish — `MapGenerator.SpawnPoints` now coincide with base positions (`Vector3.Lerp(corner, origin, 0.15)`). `MatchBootstrapper.PickSpawnPosition(player)` uses `Mathf.Abs(player.PlayerId) % count` so spawn corner and base CornerIndex agree by construction. `RestartMatch` teleports each chicken back to its corner.
- [x] Per-player base ownership — Phase 7b. `GameManager` polls every tick, finds players without an assigned base, stamps them onto the first unowned `PlayerBase`. `ChickenCargo` deposits only at bases where `Owner == Object.InputAuthority`. Win check skips unowned bases. HUD shows per-player labels (`P1: 42  P2: 17`).
- [x] Procedural map (programmer art) — `MapGenerator` MonoBehaviour. Local on every peer: builds a Plane primitive + 4 invisible boundary walls + caches 4 corner spawn positions. Master client only (after `OnRunnerReady`): spawns 4 corner `PlayerBase`s + 1 center `FoodPile` (larger amount, scaled mesh) + N small piles on a jittered ring. `FoodPile.Spawned` adjusted to honor pre-set `Amount` / `MaxAmount` from `onBeforeSpawned`. `MatchBootstrapper` reads `MapGenerator.SpawnPoints` indexed by `PlayerId % count`.
- [x] Match restart loop — `GameManager.RestartCountdown` arms on `EndMatch`; expiry triggers `RestartMatch` which zeros base totals, refills piles to `MaxAmount`, despawns loose `FoodPickup`s, RPCs `ChickenCombat.RPC_ResetForNewMatch` + `ChickenCargo.RPC_ResetForNewMatch` on every chicken, then flips State back to Active with a fresh `MatchTimer`. End-screen overlay shows the live "Next match in Ns…" countdown.
- [ ] Placeholder balancing pass — tune `MatchConfigSO`, `FoodPile._initialAmount`, `ChickenStatsSO.CollectionRate`, ability cooldowns once 4-player playtest data is available. **Bundled into dedicated test session.**
- [x] Score display during match — `CargoHud` shows each base's running food total in the right panel; full per-player leaderboard arrives with Phase 7b.

### Editor work after this commit
- Author a `GameManager` prefab: NetworkObject + `GameManager` script. Drag into `MatchBootstrapper._gameManagerPrefab` on the Game scene's MatchBootstrapper GameObject.

### Deliverable
4 players can play a complete 5-10 min match and see winner.

---

## Phase 8: Mobile Input & Android Build (Week 8-9)

### Goals
- Touch controls (virtual joystick + ability buttons)
- Android build working

### Tasks
- [x] `IInputProvider` interface + `KeyboardInputProvider` (Phase 1)
- [x] `VirtualJoystick`, `HoldButton`, `TouchControlsHud`, `TouchInputProvider` (landed early in Phase 5)
- [x] `CompositeInputProvider` ORs keyboard + touch — same input path on PC and mobile, clicking buttons with the mouse exercises the touch flow without a build
- [x] No platform branching needed at install time: composite is bound app-wide. Touch HUD activates whenever it's present in scene (Game.unity), keyboard is always live
- [x] One-button Android build export — `Cluck Wars / Build / Android` (`Ctrl+Shift+A`) forces IL2CPP + ARM64 + minSdk 24, output `Builds/Android/CluckWars-<version>.apk`. See `docs/TESTING.md`.
- [ ] Test on real Android device (mid-range 2021+) — **bundled into deferred test session**.
- [ ] FPS monitoring & optimization (target 30 fps) — **bundled into deferred test session**.

### Deliverable
Android phone can host or join LAN match with full touch controls.

---

## Phase 9: Polish & Testing (Week 9-10)

### Goals
- Stability, bug fixes, performance tuning

### Tasks
- [ ] Catch all networking bugs (drop-outs, desync) — needs real-device multiplayer testing. **Bundled into dedicated test session.**
- [ ] Optimize for Android (reduce draw calls, check memory) — needs on-device profiling. **Bundled.**
- [ ] Fix animation blending bugs — needs hand-authored AnimatorController states. **Bundled.**
- [ ] Balance tweaks based on playtesting — needs playtest data. **Bundled.**
- [x] UI responsiveness on mobile — UGUI `MatchHud` replaced the legacy IMGUI `CargoHud`. Layout follows ART.md §6.1. Auto-disables legacy CargoHud on Awake. Per-aspect fine-tune deferred to the dedicated test session.
- [x] **Error handling & reconnection** — `CargoHud` subscribes to `INetworkService.OnShutdown`, replaces the match HUD with a "SESSION ENDED" overlay (reason + countdown), then `SceneManager.LoadScene("Bootstrap")` after `_disconnectReturnDelay`.
- [x] **Audio service** — `UnityAudioService` (was `NullAudioService`) + `AudioRegistrySO`. SFX cues wired in `ChickenCombat` (Swing / Hit / Stun), `ChickenCargo` (Deposit / Pickup), `AbilityController` (Activate / Expire), `GameManager` (MatchStart + Music / MatchEnd / Victory). Maestro drops clips into the registry asset.
- [x] **`PrefabRegistrySO`** — consolidates Chicken / Doppelganger / FoodPile / FoodPickup / PlayerBase / GameManager prefabs into one SO. Consumers prefer registry value, fall back to legacy SerializeField slots — gradual migration. See TDD §6.6.
- [x] **`ColorSchemeSO`** — HUD palette + food-pile states + button states + cooldown overlay in one SO. `TouchControlsHud`, `CharacterSelectController`, `FoodPileVisuals` all refactored to read from it. Per-class chicken tints stay on `ChickenClassRegistrySO`; per-ability accents stay on `AbilityBaseSO`. See TDD §6.6.
- [x] **Isometric `MatchCamera`** per ART.md §2 — orthographic, 45° yaw + 30° pitch, framed to map.
- [x] **UGUI `MatchHud`** — replaces IMGUI `CargoHud`. Top bar (timer + per-player totals), bottom-left HP/cargo bars, centered match-end leaderboard, session-end overlay. Auto-disables legacy CargoHud.
- [x] **Player nameplates (`ChickenNameplate`)** — "P1/P2/.." `TextMesh` billboards above each chicken.
- [x] **Intro countdown** — `GameManager.IntroTimer` + "3, 2, 1, GO!" overlay; match timer offset so playable duration is unchanged.
- [x] **Ability button accent** — TouchControlsHud re-tints each ability button with the equipped `AbilityBaseSO.AccentColor`.
- [x] **Match restart loop** — `GameManager.RestartCountdown` (6s after Ended) resets bases / piles / pickups / chicken state via RPCs, teleports each chicken to its corner, then transitions back to Active with a fresh intro.
- [x] **Host-controlled lobby** — `GameManager` stays in `WaitingForPlayers` (Shared Mode); host clicks "START MATCH" in `MatchHud`. No minimum-player gate. Solo auto-starts.
- [x] **Match camera follow** — `MatchCamera` smooth-damps focus toward the local chicken; default `_orthoSize = 8` for close framing (overrides ART.md §2 "full map fixed" for playability).
- [x] **Per-class ability allowlist** — `ChickenStatsSO.AvailableAbilities` (GDD §7.1). Empty = no restriction (current default). `AbilityController.Spawned` warns but doesn't block.
- [x] **Hit-flash overlay** — `MatchHud` flashes a red full-screen tint when local chicken HP drops; fades over 0.35s.
- [x] **Debug HUD (F1)** — `DebugHud` MonoBehaviour with FPS, network state, GameManager state, chicken stats, base ownership, pickup count. See `docs/TESTING.md`.
- [x] **Fusion connect-callback logging** — `OnConnectedToServer` / `OnDisconnectedFromServer` / `OnConnectFailed` / `OnConnectRequest` all log explicitly (were silent stubs). Hardens the "third player can't connect" diagnosis path.

### Deliverable
Demo is stable and playable for 30+ min sessions without crashes.

---

## Demo Milestone Checklist

Code-side status / pending dedicated test session validation:

- [~] **Any device (PC or Android) can host LAN session** — Solo / Host / Join modes shipped, Photon AppId wired. Pending: Windows ↔ Android LAN smoke test.
- [~] **4 players connect and play full 5-10 min match** — `MaxPlayers=4` enforced in `StartGameArgs`. Pending: 4-device LAN validation (last test hit "third player can't connect"; connect-callback logging in place to diagnose).
- [x] **All 4 classes playable with at least 2 abilities** — all 8 abilities authored as `.asset` instances with distinct accent colors. Per-class allowlist (`ChickenStatsSO.AvailableAbilities`) empty → any class can equip any ability for now; balance pass to lock down later.
- [x] **Full loop: spawn → collect → fight → win condition → end screen → restart**
- [ ] Windows & Android builds stable at target FPS — pending on-device profiling.
- [x] **Session join by ID (no UGS)** — session name `cluck-lan` hardcoded; UGS bypassed via `NullUGSService`. UGUI lobby entry comes with Phase 10.
- [x] **No compiler warnings or errors** as of `v0.3.0-alpha` + subsequent fixes.

Legend: `[x]` shipped & verified, `[~]` shipped but pending device validation, `[ ]` not shipped.

---

## Dedicated test session — bundled work

Everything that needs real-device, multi-device, or playtest data is queued for a single focused session. Doing this in one batch (rather than piecemeal) avoids partial rebuilds. See `docs/TESTING.md` for the run order.

- Android build sanity + on-device FPS profile.
- Cross-device LAN smoke test (2 / 3 / 4 peers).
- "Third player can't connect" repro — connect-callback logs should pinpoint cause.
- Draw-call / memory profile on Android.
- AnimatorController state authoring (Hit / Attack / Stunned / Idle) — needs visual feedback to tune.
- VFX particle systems per ART.md §7.
- Audio clip recording / mixing into `AudioRegistry.asset`.
- Balance pass: `MatchConfigSO` (duration / food target / max players), `FoodPile._initialAmount`, `ChickenStatsSO` per-class numbers, all 8 abilities' `Duration` / `Cooldown` / per-ability tunables.
- Mobile layout fine-tune for real phone aspect ratios.
- `MatchCamera._orthoSize` + `_followSmoothTime` final tuning.

---

## Phase 10: UGS Integration + UI Visual Redesign ✅ (pending Maestro editor steps)

### Goals
- Replace hardcoded `cluck-lan` session name with unique per-match join codes.
- Players anywhere can create/find games without sharing IPs or being on the same LAN.
- Anonymous player identity for future account/progression work.
- Full UI visual redesign: warm Clash Royale / Supercell style (cluckwars-tokens-v2 design system).

### Tasks
- [x] `LobbyInfo` data class (`Services/LobbyInfo.cs`)
- [x] `IUGSService` expanded: Auth + `CreateLobbyAsync` / `JoinLobbyByCodeAsync` / `JoinLobbyAsync` / `QueryLobbiesAsync` / `LeaveLobbyAsync`
- [x] `NullUGSService` updated — offline fallback returns `"cluck-lan"` so solo dev flow unchanged
- [x] `UGSService` — real implementation: anonymous Auth, Lobby create/join/query, host heartbeat (15s)
- [x] `ProjectInstaller` — binds `UGSService` unless `UGS_DISABLED` define is set
- [x] `CharacterSelectController` — full redesign: `#0e0804` screen bg, warm brown panel, gold "CLUCK WARS" title, gold section labels; Host: lobby name + code display; Join: code input + lobby browser
- [x] `MatchHud` — Phase 10 HUD redesign: ranked leaderboard top-left (sorted by score, color dots, progress bars), timer badge top-right in gold, warm brown overlays, Okabe-Ito player colors
- [x] `ColorSchemeSO` — updated defaults: warm brown palette, gold accent, green CTA, warm white text
- [x] UGS packages added to `Packages/manifest.json` (`core 1.16.0`, `authentication 3.6.1`, `multiplayer 2.2.2`)
- [x] Bug fixes: host lobby loop, second-player-becomes-host, `ZenjectException` on `GameManager.Spawned()`
- [ ] **Maestro: link Unity project to Cloud Dashboard** (Edit → Project Settings → Services). Enable Authentication + Lobby. Required before UGS paths activate.
- [ ] **Maestro: reset `ColorScheme.asset`** in Inspector ("Reset" button) to pick up Phase 10 palette for `TouchControlsHud` button visuals.
- [ ] Live smoke test: Host on PC, Join from Android using the displayed code.

### Architecture note
UGS Relay is **not** part of Phase 10. Photon Cloud relay already handles internet routing — adding UGS Relay would require a custom Fusion transport adapter with no net benefit. Deferred to Phase 11 (dedicated server work).

### Deliverable
Players on different networks find each other via lobby browser or 6-char code. Match HUD shows ranked race leaderboard. Warm Supercell visual theme throughout.

---

## Phase R: v0.3 Mechanics Refactor (ACTIVE)

**Why this exists:** the GDD was revised v0.2 → v0.3 (see `docs/GDD.md` changelog). The shipped game implements v0.2: a button-mash *basic attack*, an `Attack` stat, one ability slot per class (two for Assassin), and a flat "combat" model. v0.3 deletes the basic attack entirely, makes **all** interaction ability-driven, gives every class **2 ability slots (3 for Assassin)**, replaces the `Attack` stat with a **class passive**, and introduces a formal **Interaction & Control System** (collision slow, pile slow, and four control states: Stunned / Slowed / Knocked Back / Rooted).

This phase is split in two:

- **Part A — Parity.** Get back to *exactly today's playable demo* (collect → deposit → win → restart, abilities equip & fire, death-stun drops cargo) but re-implemented under v0.3 rules. No new gameplay content beyond what already works — just the new rule-set and the foundational systems v0.3 redefines. **Done = the demo is as functional as it is on `develop` today, with zero references to "attack" left in gameplay code.**
- **Part B — Continuation.** The net-new content v0.3 unlocks: the brand-new abilities (Cluck Shock, Peck, Roll & Push, Feather Trap, Feather Aura, Root Egg), the primitives they need (knockback, root, placed zones), the expanded selection UI, and the balance pass.

> **Read before starting:** `docs/CONVENTIONS.md`. The hard rules that matter most here: (1) gameplay reads input only from the Fusion buffer inside `FixedUpdateNetwork`; (2) every `FixedUpdateNetwork` opens with `if (!HasStateAuthority) return;`; (3) cross-authority writes go through `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]`; (4) **never rename a MonoBehaviour/SO `.cs` class that is referenced by a prefab or `.asset`** — the serialized reference is by type+GUID and renaming silently breaks the prefab. Repurpose in place instead.

---

### Part A — Parity under v0.3 rules

#### A1. Input: delete Attack, add Ability3
- [x] `Networking/PlayerNetworkInput.cs` — in `enum InputButton`, remove `Attack`. Renumber to `Ability1 = 0, Ability2 = 1, Ability3 = 2`. (Wire indices only need to be consistent across peers, and every peer runs the same build, so renumbering is safe.)
- [x] `Input/IInputProvider.cs` — remove `GetAttackHeld()`, add `GetAbility3Pressed()`.
- [x] `Input/KeyboardInputProvider.cs` — drop the LMB attack read; map `GetAbility3Pressed()` to a third key (e.g. `rKey.wasPressedThisFrame`). Keep Q/E for Ability1/2.
- [x] `Input/TouchInputProvider.cs` and `Input/CompositeInputProvider.cs` — same interface change (composite ORs all providers; mirror the existing `GetAbility2Pressed` plumbing for slot 3).
- [x] `Networking/FusionNetworkService.cs` (~line 113) — remove the `InputButton.Attack` set, add the `InputButton.Ability3` set, and fix the verbose `OnInput` log string that prints `attack=…`.

#### A2. Strip the basic attack from `ChickenCombat`
- [x] `Gameplay/ChickenCombat.cs` — **keep the class name `ChickenCombat`** (the Chicken prefab references it by type; renaming breaks the prefab — see CONVENTIONS GUID note). It becomes a pure *health / damage-receiver / death-stun* component. Remove: `Swing()`, `BotTrySwing()`, `AttackTimer`, `AttackEpoch`, the `InputButton.Attack` read in `FixedUpdateNetwork`, and the `AttackEpoch` branch in `Render()` (which fired the attack anim + swing SFX).
- [x] Keep everything damage abilities still need: `[Networked] HP`, `[Networked] IsStunned`, `StunTimer`, `RPC_ApplyDamage`, `RPC_ResetForNewMatch`, the `OnDeath` event, `CreditKillToAttacker`, `ReflectDamageTo`, and the `HP` / `IsStunned` `ChangeDetector` branches in `Render()`.
- [x] `RPC_ApplyDamage` currently reads `stats.Attack` only at the call site (in `Swing`), so the RPC itself is fine — it takes `amount` as a parameter. Damage abilities already pass their own `amount` (see `RollTrampleAbilitySO.OnActivate`). No RPC signature change needed.

#### A3. `ChickenStatsSO`: drop Attack, drop the allowlist, add Passive
- [x] `Gameplay/ChickenStatsSO.cs` — remove `Attack`, `AttackRange`, `AttackCooldown` (the whole `[Header("Combat")]` block except `MaxHP`, which stays under a renamed `[Header("Health")]`).
- [x] Remove `AvailableAbilities` and the `Allows()` method. v0.3 §7.1: *all abilities available to all classes — no class-based restrictions.* The "ability compatibility matrix" TBD was deleted from the GDD.
- [x] Add a passive descriptor. Create `Gameplay/ChickenPassive.cs` → `public enum ChickenPassive : byte { None = 0, Immovable, Slippery, Tough, Combo }` (byte-backed to match the `ChickenClass : byte` convention — see CONVENTIONS footgun on byte enums). Add `public ChickenPassive Passive;` to `ChickenStatsSO`. Author the four class `.asset`s with their passive (Maestro editor step below): Fatty=Immovable, Speedy=Slippery, Warrior=Tough, Assassin=Combo.

#### A4. `AbilityController`: 3 slots, Assassin-gated 3rd
- [x] `Gameplay/AbilityController.cs` — add `_slot2` (SerializeField), `Cooldown2` (`[Networked] TickTimer`), and extend `GetSlot` / `GetCooldown` / `SetCooldown` / `Slot2` accessor to cover index 2.
- [x] In `FixedUpdateNetwork`, add `else if (input.Buttons.IsSet((int)InputButton.Ability3)) TryActivate(2);`.
- [x] Gate slot 2 to Assassin: in `TryActivate`, if `slot == 2` and `_controller.Stats.Passive != ChickenPassive.Combo`, log + return. (This is how the **Combo** passive is implemented — it *is* "you get the 3rd slot".)
- [x] Extend `SetSlots(slot0, slot1)` → `SetSlots(slot0, slot1, slot2)` (null = keep prefab default, same as today).
- [x] Remove the `stats.Allows(...)` warning block in `Spawned()` (the allowlist is gone in A3).

#### A5. Ability selection: 2 pickers (3 for Assassin), global pool
- [x] `Services/SessionSelectionService.cs` + `Services/ISessionSelectionService.cs` — add `AbilityBaseSO Ability2 { get; set; }` alongside the existing `Ability0` / `Ability1`.
- [x] All abilities must be selectable by every class. The selection UI currently sources the per-class pool from `entry.Stats?.AvailableAbilities` (deleted in A3). Replace that source with a **single global pool**. Recommended: author an `AbilityRegistrySO` (mirror the existing `ChickenClassRegistrySO` / `PrefabRegistrySO` pattern in `Gameplay/`), bind it `FromInstance` in `ProjectInstaller`, and have `CharacterSelectController.GetAvailableAbilities` return `_abilityRegistry.All` regardless of class.
- [x] `UI/CharacterSelectController.cs` — show **2** ability pickers for every class, **3** when `SelectedClass == Assassin` (check via the class's `Passive == Combo`, or just `== ChickenClass.Assassin`). Wire the 3rd picker to `_selection.Ability2`. (Selection UI today already supports 2 — see the `GetAvailableAbilities` calls around lines 1023 / 1070; extend, don't rewrite.)
- [x] `Gameplay/MatchBootstrapper.cs` (~line 273) — pass the 3rd slot: `abilityCtrl?.SetSlots(_selection.Ability0, _selection.Ability1, _selection.Ability2);`.

#### A6. Control-state model on the chicken
v0.3 §6.4 defines four states. Stun already exists and already drops cargo (`ChickenCombat` death → `OnDeath` → `ChickenCargo` drop). Add scaffolding for the other three on `ChickenController` (these are StateAuthority-side fields read by `ChickenMovement`, exactly like the existing `MoveSpeedMultiplier` / `MovementLocked` ability hooks):
- [x] **Slowed** — add a `SlowMultiplier` accumulator (1 = no slow; abilities/sources multiply it down). `ChickenMovement.Tick` already multiplies by `MoveSpeedMultiplier`; multiply by `SlowMultiplier` too. Reset to 1 each tick and re-apply active slow sources (so overlapping sources don't leak). Tag sources distinctly per §6.3 (collision / pile / ability) so a future passive can exempt one source — a small `enum SlowSource` + a per-source flag is enough for now.
- [x] **Rooted** — add `bool Rooted`. In `ChickenMovement`, treat like `MovementLocked` for *planar* movement, **but** Rooted must still allow ability casts (movement-lock from Egg Shell already blocks input; Rooted should block movement only). Gravity still runs.
- [x] **Knocked Back** — add a `Vector3 ExternalDisplacement` (or a short `TickTimer`-driven impulse). `ChickenMovement` applies it on top of planar movement and decays it. Full knockback abilities are Part B; Part A just lands the field + the movement integration so the state exists.

#### A7. Slow sources (collision + pile)
- [x] **Pile slow** (§6.2) — `Gameplay/ChickenCargo.cs` already detects "standing on a pile and collecting". While that condition holds, set the pile slow source on the owner each tick. Applies to all classes equally.
- [x] **Collision slow** (§6.1) — passive friction when two chickens touch. Simplest networked-safe approach: each tick on the StateAuthority, `Physics.OverlapSphere` at the chicken's position on the chicken layer (the same pattern `ChickenCombat.Swing` used to use); if another live chicken is within contact range, set the collision slow source. No damage, no knockback. (Avoid relying on `OnTriggerStay` for networked state — keep the check inside `FixedUpdateNetwork` on the authority.)
- [x] Magnitudes are placeholders for now; final tuning is a balance-pass item (Part B / test session). GDD TBD #6 (pile slow magnitude) and #7 (collision slow magnitude).

#### A8. Class passives (the other three)
**Combo** is done in A4. Implement the rest at their natural hook points:
- [x] **Immovable** (Fatty) — when knockback is applied (A6 `ExternalDisplacement`), scale it down hard if `Passive == Immovable`. (No knockback abilities ship until Part B, so this is just the scaling hook for now.)
- [x] **Slippery** (Speedy) — control-state *durations* (slow / root / knockback) are reduced for this chicken. Apply a duration multiplier wherever a control state's timer is set on a Slippery target. Damage-stun is unaffected (stun is always 5s).
- [x] **Tough** (Warrior) — outgoing damage abilities deal more. Apply in the damage path: simplest is to scale `amount` up at the *caster* side before calling `RPC_ApplyDamage` when the caster's `Passive == Tough`. (Scaling at the caster keeps the target's RPC authority-clean.)

#### A9. Ability pool re-mapping (parity subset only)
v0.3 reorganizes abilities into Damage / Control / Defense / Utility. Seven of today's eight map directly; only **Roll & Trample** is removed (it splits into **Flying Peck** + **Roll & Push**). For parity, keep the seven that already work and convert Roll & Trample into Flying Peck:
- [x] Keep as-is (update `DisplayName` / `ShortLabel` / `[CreateAssetMenu]` category only if you want the menu to read by category): `SpeedBurstAbilitySO`, `EggShellAbilitySO`, `TurtleModeAbilitySO`, `SpineCoatAbilitySO`, `InvisibilityAbilitySO`, `SneakyStealAbilitySO`, `DoppelgangerAbilitySO`.
- [x] `Abilities/RollTrampleAbilitySO.cs` → repurpose **in place** into **Flying Peck** (damage dash, HP on contact). Keep the class name to preserve the existing `.asset` reference, OR if you want a clean class name, create `FlyingPeckAbilitySO` + new `.asset` and delete the old pair (script + `.cs.meta` + `.asset` + `.asset.meta`) together — see CONVENTIONS GUID note. Update `DisplayName = "Flying Peck"`, menu name, and (Part B) add the forward dash on the caster.
- [ ] **Roll & Push** (control, push no damage) is **deferred to Part B** — it needs the knockback primitive.
- [x] Net result: 7 working abilities, full slot/selection/cooldown pipeline intact = functional parity with today.

#### A10. Animation
- [x] `Visuals/ChickenAnimator.cs` — remove the `Attack` trigger and its hash. The v0.3 animation table renames "Attacking" → "Ability Cast"; add an `AbilityCast` trigger fired by `AbilityController.TryActivate` (it can reuse the shared ability clip per `AbilityBaseSO.AbilityAnimationClip`). Keep `Hit` / `Stunned`.

#### A11. Bots
- [x] `Gameplay/BotController.cs` (line ~102) — remove the `_combat?.BotTrySwing();` call (method deleted in A2). Bots keep their collect → deposit FSM. Bots *using abilities* is optional polish in Part B.

#### A12. Docs + cleanup
- [x] Grep the whole gameplay codebase for `Attack`, `Swing`, `AttackEpoch`, `AttackCooldown`, `Allows`, `AvailableAbilities` — confirm zero gameplay references remain (Photon's internal `InputButton.Right` is unrelated).
- [x] Update `docs/STATE.md` (remove the "GDD v0.3 pending" warning once landed; list the new ability set and passives) and `docs/TDD.md` (combat → interaction system, slot count). Tick the relevant boxes here.

#### Maestro (Unity Editor) steps for Part A
- Author/refresh the four class `.asset`s in `Assets/_Game/Data/Classes/`: set the new `Passive` field, confirm the `Attack`/`AttackRange`/`AttackCooldown` fields are gone after recompile.
- On the **Chicken prefab**: confirm `ChickenCombat` is still attached (type unchanged). Add the `_slot2` ability reference for Assassin builds on `AbilityController`. The AnimatorController: delete the `Attack` trigger param, add `AbilityCast`.
- Rename/re-author the Flying Peck `.asset` (and delete the old Roll & Trample `.asset` if you took the new-class route).
- If you add `AbilityRegistrySO`: create the `.asset`, populate it with all ability assets, and assign it in `ProjectInstaller`'s inspector slot.

**Part A deliverable:** a build that plays identically to today — 4 chickens, collect/deposit/win/restart, equip & fire abilities, die → 5s stun → drop cargo → respawn — with the basic attack gone, 2/3 ability slots, passives wired, and the slow + control-state scaffolding live. Validate solo + (in the test session) multi-device.

---

### Part B — Continuation (new v0.3 content)

Everything below is net-new gameplay the v0.3 design unlocks. It builds on the Part A scaffolding (control states, slow sources, passive hooks, 3-slot controller).

#### B1. Interaction primitives (build these first — the new abilities depend on them)
- [x] **Knockback** — `RPC_ApplyKnockback` on `ChickenController`; respects Immovable passive. *(B1 commit)*
- [x] **Root** — `RPC_ApplyRoot` + `_rootUntil` timer; respects Slippery duration reduction. *(B1 commit)*
- [x] **Placed zone** — `AbilityZone` NetworkBehaviour with `Effect` (Slow/Root), `LifetimeTimer`, `NetworkedRadius`; static `ActiveZones` list for zero-RPC slow detection. *(B1 commit)*

#### B2. New abilities (each = new `AbilityBaseSO` subclass + `.asset` under `Data/Abilities/`, per the GDD v0.3 §7.2 pool)
- [x] **Cluck Shock** (Damage, Medium) — AoE HP burst around self. *(B2 commit)*
- [x] **Peck** (Damage, Short) — nearest-enemy HP hit + minor knockback. *(B2 commit)*
- [x] **Roll & Push** (Control, Short) — roll forward + sphere push, no damage. *(B2 commit)*
- [x] **Feather Trap** (Control, Medium) — spawns slow zone `ForwardOffset` metres ahead. *(B2 commit)*
- [x] **Feather Aura** (Control, Medium) — broadcasts `AuraSlowActive/Radius/Factor` on caster; nearby chickens self-apply slow. *(B2 commit)*
- [x] **Root Egg** (Control, Medium) — spawns root zone consumed on first trigger. *(B2 commit)*
- [x] **SpineCoat knockback** — `ReflectKnockback` field; `ReflectDamageTo` pushes attacker away. *(B2 commit)*
- **Maestro:** Create `.asset` files for all 6 new SOs in `Data/Abilities/`; set `Category` and `AccentColor` on each; set `BotRole = Steal` on Sneaky Steal `.asset`.

#### B3. Expanded selection UI
- [x] `UI/CharacterSelectController.cs` — slot-selector row + scrollable ability grid grouped by `AbilityCategory`, tier badge (Short/Medium/Long), click-to-equip. *(B3 commit)*

#### B4. Cooldown UI for slot 3 + tiers
- [x] `Input/TouchControlsHud.cs` — base button `Image` alpha dims to 0.45 while on cooldown (fill > 0), restores to 1.0 when ready. Hard UI requirement met. *(B3 commit)*

#### B5. Bot AI rework (ability-driven) ✅
Full Phase R-Bot implementation shipped. See Phase R-Bot section below for detail. *(B3 commit)*

#### B6. Balance pass (bundles with the Dedicated test session)
- [ ] Per-ability cooldown values within the v0.3 tiers (Short 3–6s, Medium 8–12s) — GDD TBD #3.
- [ ] Pile slow + collision slow magnitudes — GDD TBD #6 / #7.
- [ ] Control-state durations (slow / root / knockback) and the Slippery reduction factor.
- [ ] Passive magnitudes: Immovable knockback reduction, Tough damage bonus.
- [ ] Use the existing `Assets/_Game/Editor/BalanceEditorWindow.cs` where it helps.

#### B7. Animation / VFX per ability (test session)
- [ ] Author the 2–3 shared ability clips (§4) and a feather-cloud / egg VFX. Wire via `AbilityBaseSO.AbilityAnimationClip` and the local VFX pattern (`ChickenVFX`, observed-state-driven — see CONVENTIONS "VFX are local").

**Part B deliverable:** the full v0.3 ability roster (≈13 abilities across 4 categories) selectable and balanced, all four control states exercised by real abilities, passives meaningfully felt, on top of the Part A foundation.

---

## Phase R-Bot: Ability-Driven Bot AI

**Depends on Part A** (3-slot `AbilityController`, basic attack removed). Can land before Part B's new abilities — it works with whatever abilities are equipped. The more of Part B's abilities exist, the richer it gets, but it must not *require* them.

### The problem
v0.2 bots "fought" by walking into a rival and letting `ChickenCombat.BotTrySwing` mash an auto-attack. That method is deleted in Part A (A2/A11). Today's `BotController` (`Gameplay/BotController.cs`) is therefore a pure farmer: it collects from the nearest pile and deposits past a cargo threshold (states `Idle` / `CollectFood` / `ReturnToBase`). In v0.3 **all** interaction is ability-driven, so a bot that never casts an ability never interacts — it's a target dummy. The AI has to learn to *use its 2–3 equipped abilities* offensively and defensively.

### Design goals
- **Simple**: one FSM, one throttled think tick (keep the existing 0.3s cadence), no pathfinding/navmesh, no per-ability hardcoding.
- **Effective**: protects its own cargo, punishes loaded rivals (stun → they drop cargo → scoop it), and contests piles.
- **Generic**: works with *any* equipped ability via category/role tags — never `if (ability is SneakyStealAbilitySO)`.
- **Characterful**: light per-class personality from tuning numbers, not separate code paths.
- **Network-clean**: everything runs on the StateAuthority (master client) only, exactly like today. Ability activation goes through a bot hook that reuses the existing cooldown/active/stun gates — no Fusion input buffer involvement.

### How it behaves (decision priority, evaluated top-down each Think)
The proven farm loop stays as the backbone; offense/defense layer on top as two new states plus an ability-reaction step.

1. **FLEE / PROTECT** — *carrying a meaningful haul (`Fraction ≥ _protectCargoThreshold`) AND a rival within `_dangerRadius`.* Move toward home base to bank the haul; fire a ready ability by role preference **Defense → Escape → Control** (turtle up / dash away / shove the chaser off). This is the highest priority because dropped cargo is the biggest swing in the game.
2. **DEPOSIT** — *`Fraction ≥ _returnThreshold`* → `ReturnToBase` (existing behavior).
3. **HUNT** — *a rival within `_huntRadius` is loaded (`rivalFraction ≥ _huntCargoThreshold`), the bot has cargo space, and the class is in an aggressive mood.* Move toward the rival; once within `_abilityRange` fire by role preference **Steal → Offense → Control** (snatch cargo / stun them so they drop it / pin them). Then fall through to COLLECT next think to scoop the dropped `FoodPickup`s.
4. **COLLECT** — nearest non-empty pile (existing). Opportunistic: if a rival is standing on the *same* pile, fire a ready **Control** ability to displace them.
5. **IDLE** — nothing to do (existing).

### How it picks an ability (the generic part)
A bot can't reason about an ability it can't classify. Two small authoring tags on `AbilityBaseSO` solve it (the category tag is shared with Part B's selection-UI grouping, so it's not bot-only work):

- `AbilityCategory { Damage, Control, Defense, Utility }` — the GDD §7.2 category.
- `BotRole { Auto, Offense, Defense, Control, Escape, Steal }` — how the *bot* should use it. `Auto` derives from category: `Damage→Offense`, `Control→Control`, `Defense→Defense`, `Utility→Escape`. Override only where Auto is wrong — e.g. **Sneaky Steal** = `Steal`. (Doppelganger/Invisibility/Speed Burst all want `Escape`, which is the Utility default — no override needed.)

The bot asks its controller "give me a ready slot whose role is X" and activates it. It tries roles in the priority order for the current state and fires the first match.

### Per-class personality (tuning only — one switch on `_controller.Class` at Spawned)
| Class (passive) | Mood | Key tuning | Prefers |
|---|---|---|---|
| Warrior (Tough) | Aggressive | large `_huntRadius`, low `_huntCargoThreshold`, hunts even while lightly loaded | Offense |
| Assassin (Combo) | Opportunist disruptor | hunts vulnerable carriers, flees early (squishy), uses 3rd slot | Steal → Offense |
| Fatty (Immovable) | Cautious turtle | never hunts, deposits early (low `_returnThreshold`) | Defense |
| Speedy (Slippery) | Hit-and-run | hunts only to snatch drops, flees very early | Escape |

These are just different default values for the same serialized fields — no class-specific branches in the logic.

### Implementation roadmap (for a zero-context Sonnet)

> All new perception (`FindObjectsByType` / `OverlapSphere`) stays **inside the throttled `Think()`** (every `_thinkInterval`, default 0.3s), never per-tick. All state writes are StateAuthority-only (the FSM already early-returns on `!HasStateAuthority`). Follow CONVENTIONS: self-injection, `ILogService` with a `Source` tag, no `Debug.Log`.

**BOT-1 — Tag abilities.**
- [x] `Abilities/AbilityBaseSO.cs` — `BotRole` enum + `public BotRole BotRole` field + `ResolveBotRole()` helper. *(B3 commit)*
- [ ] **Maestro:** set `Category` on every ability `.asset`; set `BotRole = Steal` on Sneaky Steal `.asset`. Everything else stays `Auto`.

**BOT-2 — Bot activation API on `AbilityController`.**
- [x] `Gameplay/AbilityController.cs` — `BotTryActivate(int slot)`, `EquippedSlotCount`, `TryGetReadySlotForRole(BotRole, out int)`. *(B3 commit)*

**BOT-3 — Give bots a randomized loadout from a small preset pool.**
Bots currently get no abilities (the bot `onBeforeSpawned` in `TrySpawnBots()` never calls `SetSlots`), so they fall back to whatever prefab defaults exist. The AI needs *something* equipped to function. Rather than one fixed kit (boring — all bots play the same) or full open random selection (more than we want to build now), define a **small pool of curated 2-slot loadout presets and pick one at random per bot**, with **some presets restricted to certain classes** for flavor. All abilities used are Part A's already-working ones.

Loadout presets (each = slot0 / slot1 / optional Assassin slot2 + the classes allowed to roll it):

| Preset | Slot 0 | Slot 1 | Slot 2 (Assassin) | Allowed classes |
|---|---|---|---|---|
| Bruiser | Flying Peck (Offense) | Egg Shell (Defense) | — | All |
| Skirmisher | Flying Peck (Offense) | Speed Burst (Escape) | — | All |
| Tank | Spine Coat (Defense) | Turtle Mode (Defense) | — | Fatty, Warrior |
| Trickster | Flying Peck (Offense) | Invisibility (Escape) | — | Speedy, Assassin |
| Thief | Sneaky Steal (Steal) | Speed Burst (Escape) | Flying Peck (Offense) | Assassin only |

This gives variety (no two bots guaranteed identical), keeps a sane shape (every preset has ≥1 way to threaten + ≥1 way to survive), and gives class identity via restrictions: Fatty/Warrior can roll the slow, sticky **Tank** kit; Speedy/Assassin can roll the evasive **Trickster** kit; only the Assassin can roll **Thief** (and only the Assassin's 3rd slot is ever used). The AI's role-preference lists fall through gracefully whatever rolls — e.g. a Tank bot fleeing prefers Defense and finds it; a Skirmisher bot fleeing falls through to Escape (Speed Burst).

> **Not a GDD contradiction.** GDD §7.1 says *players* face no class-based ability restrictions. These restrictions are **bot-AI flavor only** — they shape how bots feel, not what a human may equip. Keep that boundary: never reuse this table to gate the player selection UI.

Wiring (simplest path, no `AbilityRegistrySO` dependency):
- [ ] `Gameplay/MatchBootstrapper.cs` — add a small serializable `[System.Serializable] struct BotLoadoutPreset { AbilityBaseSO Slot0, Slot1, Slot2; ChickenClass[] AllowedClasses; }` and a `[SerializeField] BotLoadoutPreset[] _botLoadouts;`. Maestro authors the rows above in the inspector (drop in the Flying Peck / Egg Shell / Speed Burst / Spine Coat / Turtle Mode / Invisibility / Sneaky Steal `.asset`s). An empty/omitted `AllowedClasses` means "all classes".
- [ ] Add a helper `BotLoadoutPreset PickBotLoadout(ChickenClass cls)`: filter `_botLoadouts` to presets whose `AllowedClasses` is empty or contains `cls`, then pick one with `UnityEngine.Random`. **Solo-only**, single authoritative client, so plain `Random` is fine — no cross-peer seeding needed (contrast with the corner-permutation seeding, which exists only because online peers must agree). If the filtered set is empty, fall back to the first preset or skip (log a Warn).
- [ ] In `TrySpawnBots()` (~line 199, bot `onBeforeSpawned`), after `ctrl.IsBot = true`, resolve `var lo = PickBotLoadout(botClass);` and call `networkObject.GetComponent<AbilityController>()?.SetSlots(lo.Slot0, lo.Slot1, botClass == ChickenClass.Assassin ? lo.Slot2 : null);`. (`SetSlots` ignores null args; the Assassin 3rd-slot gate from BOT-2/A4 keeps a non-Assassin's slot 2 inert anyway — the null is just tidy.) Compute the loadout *outside* the lambda and capture it, mirroring the existing `int captured = i;` pattern, so the random roll is stable for that spawn.
- [ ] Guard for missing assets/empty pool: log a Warn via `ILogService` and let the bot run ability-less rather than NRE.
- [ ] Log the rolled preset per bot at Info (e.g. `"Bot 2 (Assassin) → Thief loadout"`) so solo playtests are debuggable.

> **Deferred (not this pass):** weighted / fully-open random loadouts drawn from the populated `AbilityRegistrySO` once Part B's full roster exists (e.g. probability tables per class). The preset pool above is the deliberate, easily-extended placeholder — add a row to grow it.

**BOT-4 — Perception.**
- [x] `Gameplay/BotController.cs` — `FindNearestRival(out dist, out cargoFraction, requireCargo)` inside throttled `Think()`. *(B3 commit)*

**BOT-5 — Decision priority + new states.**
- [x] `BotState` extended with `Flee` + `Hunt`. `Think()` rewritten to 5-tier priority. *(B3 commit)*

**BOT-6 — Ability reaction.**
- [x] `ReactWithAbility(BotRole[] rolePreferences)` dispatches role-preference list per state. *(B3 commit)*

**BOT-7 — Personality params.**
- [x] `_dangerRadius`, `_huntRadius`, `_abilityRange`, `_protectCargoThreshold`, `_huntCargoThreshold` added. `ApplyClassPersonality()` sets per-class defaults in `Spawned()`. *(B3 commit)*

**BOT-8 — Tuning, balance, validation.**
- [ ] Add to the **Dedicated test session** balance list: bot radii/thresholds per class, oscillation check.
- [ ] Verify solo mode: 3 bots farm, protect hauls, hunt loaded rivals.
- [ ] **Maestro:** assign `_botOffenseAbility` (Flying Peck), `_botEscapeAbility` (Speed Burst), `_botStealAbility` (Sneaky Steal) on `MatchBootstrapper` in the Game scene.

### Phase R-Bot deliverable
Solo-mode bots that play the v0.3 game: farm efficiently, protect a full haul (turtle/dash/shove when chased), hunt loaded rivals to stun-and-rob them, and contest piles — each class with a recognisably different temperament, all driven by their equipped abilities through one small FSM.

---

## Post-Demo Roadmap (TBD)

- **Phase 11:** Dedicated server mode for production (+ UGS Relay if Photon is replaced)
- **Phase 12:** Account progression, cosmetic skins
- **Phase 13:** Seasonal content
- **Phase 14:** Additional flavor (pirate, space)
