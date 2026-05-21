# Project State — Snapshot

**Live source of truth for "where are we right now."** Updated each session.
Designed to be the first doc an agent reads after `CLAUDE.md` to understand
what's shipped, what's in flight, and what's blocked on testing.

---

## Latest tag

`v0.3.1-alpha` — Debug logging, deposit fix, cargo feedback, base tinting. Pinned at commit `7fe855e`.

Earlier tags:
- `v0.3.0-alpha` (`312f511`) — Pre-test polish + build tooling.
- `v0.2.0-alpha` (`7ee1742`) — Phase 9 polish (audio service, registries, reconnection).
- `v0.1.0-alpha` (`10e527c`) — Core gameplay loop closed.

All tags pushed to origin.

---

## What works (Phases 1–9 complete + Phase 10 complete + DCBA polish + v0.3.1 fixes + Phase R Part A + Phase R Part B)

### Core loop
- 4 classes (Warrior / Speedy / Fatty / Assassin) — selectable in Bootstrap menu.
  Each class has a named passive: Warrior=Tough, Speedy=Slippery, Fatty=Immovable, Assassin=Combo.
- Movement: keyboard (WASD) + touch (joystick). `CompositeInputProvider` ORs both.
  Keys: Q = Ability1, E = Ability2, R = Ability3 (Assassin/Combo only).
- **No basic attack** (v0.3). All combat is ability-driven.
  HP, hit reaction, 5-sec death stun still fully networked.
- Food collect from piles → carry → deposit at base.
  Pile slow: standing on a pile applies a 0.80× speed penalty (GDD §6.2).
  Collision slow: brushing another chicken applies a 0.75× speed penalty (GDD §6.1).
- Food drop on death; pickup-on-overlap.
- **13 abilities in the global pool** (all classes can equip any):
  Speed Burst, Egg Shell, Flying Peck (fka Roll & Trample), Invisibility,
  Spine Coat (now also knockbacks attacker), Turtle Mode, Sneaky Steal, Doppelganger.
  **New in Part B:** Cluck Shock, Peck, Roll & Push, Feather Trap, Feather Aura, Root Egg.
- 2 ability slots for all classes; 3 slots for Assassin (Combo passive gates slot 2).
- Global ability pool via `AbilityRegistrySO` — no per-class restrictions.
- All 4 control states now exercised by real abilities:
  - Knockback: Peck, Roll & Push, Spine Coat reflect
  - Root: Root Egg zone
  - Slow: Feather Trap zone, Feather Aura broadcast
  - Speed boost: Speed Burst, Roll & Push (caster-side)
- `AbilityCategory` and `BotRole` authored on every AbilityBaseSO subclass.

### DCBA polish (code complete — pending Maestro prefab wiring for B + A)
- **D — Dead visual states**: `ChickenVisuals` greys out + goes semi-transparent (0.40 alpha) when `IsStunned`. `ChickenNameplate` shows red "☠" while dead; restores the normal label on respawn.
- **C — Screen shake**: `MatchCamera.Instance.ApplyShake()` called on HP decrease (0.12 mag / 0.25s) and on death (0.35 mag / 0.45s). Linear decay, y-axis damped 0.3×. Only fires on `HasInputAuthority` (local chicken).
- **B — Match stats**: New `ChickenMatchStats` NetworkBehaviour (`[Networked] Kills` + `FoodDeposited`). `ChickenCombat.CreditKillToAttacker()` credits kills via RPC. `ChickenCargo.TryDepositAtNearbyBase()` records deposits. `GameManager.RestartMatch()` resets stats. Match-end overlay shows `food | kills` per player. **Maestro: add `ChickenMatchStats` component to Chicken prefab.**
- **A — AI bots (solo mode)**: `ChickenController.IsBot` [Networked] + `BotTick()`. `BotController` NetworkBehaviour (FSM: **Idle / CollectFood / ReturnToBase / Flee / Hunt**; throttled Think @ 0.3s). Phase R-Bot: 5-tier decision priority, `FindNearestRival` perception, `ReactWithAbility()` role-preference dispatch (Defense→Escape→Control on Flee; Steal→Offense→Control on Hunt), per-class personality (`ApplyClassPersonality()`). `MatchBootstrapper._soloBotsToSpawn = 3` + `_botOffenseAbility` / `_botEscapeAbility` / `_botStealAbility` (Maestro assigns). **Maestro: also add `BotController` component to Chicken prefab.**

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

### UI (Phase 10 visual redesign — Clash Royale/Supercell warm palette)
- **Design tokens** (ART.md §6, `cluckwars-tokens-v2`): panel `#3a2210` warm dark wood, gold `#f5c842`, green CTA `#33a332`, text `#fef5e0`. All inline in code; independent of serialised SO.
- **Player palette** updated to colorblind-safe Okabe-Ito set: Orange `#E8751A`, Blue `#1A7FC4`, Pink `#C4286F`, Teal `#0D9E7A`.
- **Bootstrap menu**: full-screen `#0e0804` background, warm wood panel, "CLUCK WARS" title in gold, section labels in gold accent. Keyboard shortcuts (1-4, S/H/J, SPACE). UGS lobby browser with scrollable list.
  - **Class stat cards** (Phase 11): flat class buttons replaced with tall cards showing a class-tint top strip, bold name, SPD/HP/CGO stat bars (normalized across all 4 classes from `ChickenClassRegistrySO`; falls back to design-time percentages when registry not bound), and ability-pool hint. Entire card is a `Button`.
  - **Ability slot picker** (B3): "ABILITIES" section replaced by a slot-selector row (Slot 1 / Slot 2 / Slot 3★) + scrollable ability grid grouped by `AbilityCategory` (Damage/Control/Defense/Utility), with cooldown tier badge (Short/Medium/Long). Click a card to equip it in the selected slot. Dedup: equipping an ability already in another slot swaps them. Slot 3 visible only for Assassin.
- **MatchHud** (UGUI): ranked leaderboard top-left (4 rows sorted live by food score, with player-color dot + progress bar + score). Timer badge top-right in gold. HP + cargo bars bottom-left above joystick. Centered overlays: lobby (with green Start button), match-end (ranked results), session-end, intro countdown (gold "3, 2, 1, GO!").
- **TouchControlsHud** (B4): joystick + 3 ability buttons. Attack button removed (v0.3); former attack position is now Ability3 (R key / Assassin slot). Ability tint = equipped `AbilityBaseSO.AccentColor`. Cooldown radial fill for all 3 slots. **Buttons grey out (alpha 0.45) while on cooldown** — v0.3 §10 hard UI requirement.
- **DebugHud** (F1 toggle): FPS, network state, GameManager state, local chicken stats, base ownership, pickup count.

### v0.3.1 — Debug logging, deposit fix, cargo feedback, base tinting

- **Debug logging**: `BotController`, `ChickenCombat`, `AbilityController`, `ChickenCargo`, `GameManager` fully instrumented. Bot FSM logs every state transition (Idle/CollectFood/ReturnToBase) with cargo fraction.
- **Deposit fix**: `GameManager.AssignBasesToPlayers` now uses nearest-position matching — fixes the 75% base-ownership failure caused by `MatchBootstrapper`'s Fisher-Yates corner shuffle vs. the old `PlayerId % 4` corner selection. `ChickenCargo.FindNearestBaseInRange` removed `Physics.OverlapSphere` dependency — uses `FindObjectsByType<PlayerBase>` with a 1.5 s cache; `PlayerBase` prefab no longer needs a trigger collider.
- **Cargo feedback**: `MatchHud` cargo bar shifts gold→orange→red, shows "FULL → RETURN TO BASE!" label at capacity. `ChickenNameplate` adds a world-space cargo line (`3/10`, orange ≥70%, red "■ FULL!"). `ChickenVFX` adds a looping gold orbit ring (`VFX_CargoFull`) while cargo is at capacity.
- **Base tinting**: `PlayerBase.LateUpdate` polls `Owner` each frame and applies corner-indexed colors (Orange/Blue/Pink/Teal) via `MaterialPropertyBlock` + direct material fallback. Replaced a broken `ChangeDetector + Render()` approach that silently skipped locally-written `[Networked]` props in `GameMode.Single`.

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
- `Cluck Wars / Balance / Balance Editor` — Editor window: editable table of all 4 `ChickenStatsSO` assets (HP / Spd / Turn / Cap / Rate — Attack fields removed in v0.3) + all 8 `AbilityBaseSO` assets (Duration / Cooldown / AccentColor). "Save All ★" flushes dirty assets. Auto-discovers assets — no list to maintain.
- **F2 in-game toggle** in `DebugHud` — runtime Balance panel (right side of screen): class stats table from `ChickenClassRegistrySO` + local chicken's equipped ability timings (Duration / Cooldown, active slot indicator).

---

## Outstanding before next test session

### Pending Maestro (Editor work — Phase R Part A + Part B)

These steps must be done in the Unity Editor after the code compiles cleanly:

1. **Four class `.asset`s** (`Assets/_Game/Data/Classes/`): set the new `Passive` field for each
   (Warrior=Tough, Speedy=Slippery, Fatty=Immovable, Assassin=Combo).
   The old `Attack`/`AttackRange`/`AttackCooldown`/`AvailableAbilities` fields will be gone
   after recompile — Unity will strip them automatically.

2. **AbilityRegistry**: create an `AbilityRegistrySO` asset via `Cluck Wars / Ability Registry`.
   Drag all 7 ability assets (`Assets/_Game/Data/Abilities/`) into its `All` array.
   Assign it in `ProjectInstaller._abilityRegistry` on the `ProjectContext` prefab.

3. **Chicken prefab — `AbilityController`**: the `_slot2` field is now exposed.
   For Assassin builds, assign a default ability to `_slot2` (e.g., Sneaky Steal).

4. **Chicken prefab — AnimatorController**: delete the `Attack` (trigger) parameter,
   add `AbilityCast` (trigger). `ChickenAnimator` now fires `TriggerAbilityCast()`.

5. **Flying Peck `.asset`** (was Roll & Trample): the asset file itself is unchanged (same GUID).
   Open it and update `DisplayName` = "Flying Peck", `ShortLabel` = "FP".

6. **ProjectInstaller**: remove any reference to the old `_attackButtonSize`/`_attackAnchoredPosition`
   inspector values on `TouchControlsHud` — those fields were removed; the GameObject will
   reset to the new `_ability3AnchoredPosition` default.

**New Phase R Part B Maestro tasks:**

7. **AbilityZone prefab**: create a NetworkObject prefab with the `AbilityZone` script + a trigger sphere collider at `_triggerRadius` = 1.5. Assign to `PrefabRegistrySO.AbilityZone`. (Without this, FeatherTrap and RootEgg log a warning and do nothing.)

8. **Six new ability `.asset`s** — create in `Assets/_Game/Data/Abilities/`:
   - Cluck Shock (via `Cluck Wars/Ability/Damage/Cluck Shock`): Category=Damage, Cooldown≈10s
   - Peck (`Cluck Wars/Ability/Damage/Peck`): Category=Damage, Cooldown≈4s
   - Roll & Push (`Cluck Wars/Ability/Control/Roll and Push`): Category=Control, Cooldown≈4s
   - Feather Trap (`Cluck Wars/Ability/Control/Feather Trap`): Category=Control, Cooldown≈10s
   - Feather Aura (`Cluck Wars/Ability/Control/Feather Aura`): Category=Control, Cooldown≈10s
   - Root Egg (`Cluck Wars/Ability/Control/Root Egg`): Category=Control, Cooldown≈10s
   Set `AccentColor` and `DisplayName` on each.

9. **AbilityRegistry**: add all 6 new `.asset`s to `AbilityRegistrySO.All`.

10. **Sneaky Steal `.asset`**: set `BotRole = Steal` in Inspector.

11. **MatchBootstrapper** (Game scene): assign `_botOffenseAbility` (Flying Peck `.asset`), `_botEscapeAbility` (Speed Burst `.asset`), `_botStealAbility` (Sneaky Steal `.asset`).

Pre-existing Maestro tasks still pending:
- **UGS Dashboard link** (Phase 10): Unity → Project Settings → Services → link org. Enable Auth + Lobby.
- **ColorScheme.asset**: hit "Reset" in Inspector for Phase 10 warm palette on `TouchControlsHud`.
- Optional: fill `AudioRegistry` clips.

### Pending agents (code)
- Phase R Part A: **complete**. See ROADMAP.md § Phase R for what's done.
- Phase R Part B: **complete** (code). Remaining: Maestro steps below + B6 balance pass (test session) + B7 VFX (test session).
- `ISessionSelectionService` carries `Ability0`/`Ability1`/`Ability2`; `MatchBootstrapper`
  calls `AbilityController.SetSlots(slot0, slot1, slot2)` in `onBeforeSpawned`.

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
- AnimatorController state authoring — **state machine needs update** (replace `Attack` trigger with `AbilityCast` trigger per Phase R A10). States: Idle/Walk/AbilityCast/Hit/Stunned. Needs `.anim` clip assets once artwork is recorded/imported.
- VFX particle systems — **code complete** (`ChickenVFX.cs`: hit sparks, death burst, stun orbit, deposit gold shower, **ability accent burst**). `VFX_Ability` PS: 16-particle sphere, tinted dynamically from `AbilityBaseSO.AccentColor` at activation, floats upward. Maestro: add `ChickenVFX` component to Chicken prefab.
- Audio clip recording / mixing.
- Balance pass (food rates, ability cooldowns, attack damage, HP).
- Mobile layout fine-tune for actual phone aspects.
- `MatchCamera._orthoSize` / `_followSmoothTime` final tuning on device.

---

## Recent commits (most recent first)

```
0772810 Part B — B3/B4/B5: bot AI, ability grid UI, cooldown grey-out
c3f70c9 Part B — B2: new ability SOs + SpineCoat knockback
b120170 Part B — B1: interaction primitives (knockback, root, placed zones)
578a2ce Phase R Part A: v0.3 parity refactor
7fe855e Debug logging, deposit fix, cargo feedback, base tinting  ← v0.3.1-alpha
1aa3b93 Fix: multiple chickens spawning at same base
540797a Visuals: per-class scale applied at spawn
90a071f Balance: faster movement + closer camera (test feedback)
ca39dfb Spawn: random starting edge per session
312f511 Build menu                                           ← v0.3.0-alpha
7ee1742 Phase 9 wiring                                       ← v0.2.0-alpha
```

`git log --oneline -20` for more.

---

## Branch / push status

- All work on `develop`.
- `main` has not been merged since project start (per Maestro's decision — wait for stable test pass before promoting).
- `develop` and `v0.3.1-alpha` pushed to origin. Branch is clean.
