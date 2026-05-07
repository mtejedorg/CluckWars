# Development Roadmap

**Target:** Playable demo on LAN with 4 players, all classes, core abilities, full match loop.

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
- [ ] Dropped cargo on death — deferred to Phase 4 (cargo system doesn't exist yet). `ChickenCombat.OnDeath` event ready for `ChickenCargo` to subscribe.

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
- [ ] Food pickup prefab (dropped cargo as collectables) — deferred to Phase 4b once Phase 5 multi-client lands and we can validate it

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
- [ ] Tested with PC host + Android client on same Wi-Fi

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
- [ ] `AbilityBaseSO` abstract class
- [ ] Concrete ability SOs (2-4 per class)
- [ ] `AbilityController` on chicken (equip, activate, cooldown)
- [ ] Ability button UI with cooldown indicator
- [ ] Effects for each ability (visuals only for now, no gameplay beyond stun/invuln)

### Deliverable
Players can equip and activate 2 abilities per chicken, cooldowns work.

---

## Phase 7: Game Loop & Balancing (Week 7-8)

### Goals
- Full match: spawn → play → end condition
- Basic balancing (food targets, timer, pile sizes)

### Tasks
- [ ] `GameManager` with match lifecycle
- [ ] Win condition check (first to 150 food OR most at 3 min)
- [ ] Match timer + end screen
- [ ] Spawn system (players spawn at base edge on join)
- [ ] Placeholder balancing values tested (adjust food amounts, cargo rates, cooldowns)
- [ ] Score/leaderboard display during and after match

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
- [ ] Android build export
- [ ] Test on real Android device (mid-range 2021+)
- [ ] FPS monitoring & optimization (target 30 fps)

### Deliverable
Android phone can host or join LAN match with full touch controls.

---

## Phase 9: Polish & Testing (Week 9-10)

### Goals
- Stability, bug fixes, performance tuning

### Tasks
- [ ] Catch all networking bugs (drop-outs, desync)
- [ ] Optimize for Android (reduce draw calls, check memory)
- [ ] Fix animation blending bugs
- [ ] Balance tweaks based on playtesting
- [ ] UI responsiveness on mobile
- [ ] Error handling & reconnection logic

### Deliverable
Demo is stable and playable for 30+ min sessions without crashes.

---

## Demo Milestone Checklist

- [ ] Any device (PC or Android) can host LAN session
- [ ] 4 players connect and play full 5-10 min match
- [ ] All 4 classes playable with at least 2 abilities
- [ ] Full loop: spawn → collect → fight → win condition → end screen
- [ ] Windows & Android builds stable at target FPS
- [ ] Session join by ID (no UGS)
- [ ] No compiler warnings or errors

---

## Post-Demo Roadmap (TBD)

- **Phase 10:** UGS integration (Relay, Lobby, Authentication)
- **Phase 11:** Dedicated server mode for production
- **Phase 12:** Account progression, cosmetic skins
- **Phase 13:** Seasonal content
- **Phase 14:** Additional flavor (pirate, space)
