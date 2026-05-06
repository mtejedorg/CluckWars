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

## Phase 3: Combat System (Week 3-4)

### Goals
- Attack button mashing mechanic
- Damage, HP, hit reaction
- Death stun (5 seconds)

### Tasks
- [ ] `ChickenCombat` component with proximity detection
- [ ] Attack input handling + button mashing feel
- [ ] Damage calculation (simple: HP -= attacker.Attack)
- [ ] Hit animation trigger
- [ ] Stun on death (5 sec, chicken can't move/act)
- [ ] Dropped cargo on death (cargo items spawn as pickups)

### Deliverable
Two chickens can fight, one dies, stunned for 5 sec.

---

## Phase 4: Food System (Week 4-5)

### Goals
- Food piles with cargo collection
- Cargo capacity per class
- Base depositing

### Tasks
- [ ] `FoodPile` NetworkObject with `[Networked]` food amount
- [ ] `FoodPileVisuals` (scale + color based on remaining %)
- [ ] `ChickenCargo` component (collection rate, carrying, deposit)
- [ ] Base zones (no interaction, just visual)
- [ ] HUD showing current cargo / max cargo
- [ ] Food pickup from ground (dropped cargo after death)

### Deliverable
Players can collect from piles, see visual feedback, deposit at base.

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
- [ ] `NetworkBehaviour` on `FoodPile`, `GameManager`
- [ ] State sync via `[Networked]` for HP, cargo, stun, food piles, deposit totals
- [ ] LAN host/client UI (session name entry, replaces IMGUI character-select)
- [ ] Late-join handling: state authority transfer in Shared Mode
- [ ] Tested with PC host + Android client on same Wi-Fi

### Deliverable
2 devices (Windows PC host + Android client) can play together on LAN.

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
- [ ] `MobileInputProvider` (virtual joystick + buttons)
- [ ] Platform detection (Windows vs Android) at startup
- [ ] Zenject input provider binding per platform
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
