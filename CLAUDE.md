# CLAUDE.md — Cluck Wars Development Context

This file provides Claude with the context needed to contribute effectively to Cluck Wars across sessions.

---

## Project at a Glance

**Cluck Wars** is a fast-paced 4-player free-for-all arena game. Players pick a chicken class, collect food from piles, deposit it at their base, and sabotage rivals. First to 150 food units (or most at 3 minutes) wins.

- **Engine:** Unity 6000.3 LTS (Unity 6)
- **Render Pipeline:** Universal Render Pipeline (URP)
- **Networking:** Photon Fusion 2 — Shared Mode for demo (LAN), Server Mode post-funding
- **DI:** Zenject (Extenject)
- **Backend:** Unity Gaming Services — always behind `IUGSService`; use `NullUGSService` for demo
- **Architecture:** MonoBehaviour — no ECS/DOTS
- **Platforms:** Windows (primary dev), Android (mobile target), iOS (post-demo)
- **Target FPS:** 60 on Windows, 30 stable on mid-range Android (2021+)

---

## Repository Layout

```
/Assets/_Game/
├── Scripts/
│   ├── Networking/     INetworkService, FusionNetworkService, PlayerNetworkInput
│   ├── Gameplay/       GameManager, systems (Food, Combat, Ability, MatchState)
│   ├── Abilities/      AbilityBaseSO + concrete ability SOs
│   ├── Input/          IInputProvider, MobileInputProvider, KeyboardInputProvider
│   ├── Audio/          IAudioService, UnityAudioService, NullAudioService
│   ├── Logging/        LogLevel, ILogService, UnityLogService
│   ├── Services/       IUGSService, NullUGSService, ISessionSelectionService
│   ├── Visuals/        ChickenAnimator, ChickenVisuals, FoodPileVisuals
│   ├── Installers/     ProjectInstaller, GameInstaller
│   ├── Bootstrap/      SceneLoader (Boot → Game transition)
│   └── UI/             CharacterSelectController, HUD, menus
├── Data/               ScriptableObject assets (Classes/, Abilities/, MatchConfigSO)
├── Prefabs/
├── Scenes/
└── Art/
    └── URP/            Renderer Data, render features, shader graphs

/docs/
├── GDD.md              Game Design Document v0.2
├── TDD.md              Technical Design Document v0.1
├── ART.md              Art Direction v0.1
└── ROADMAP.md          9-phase dev roadmap
```

---

## Core Coding Conventions

- **All services go behind interfaces** — never call Fusion, UGS, or Unity Input directly from game logic.
- **ScriptableObjects use `SO` suffix** — e.g., `ChickenStatsSO`, `AbilityBaseSO`, `MatchConfigSO`.
- **SO assets live in `/Assets/_Game/Data/`**.
- **Game logic reads input only from the Fusion buffer** — never `Input.GetKey` directly.
- **Zenject for all DI** — `ProjectInstaller` for app-wide singletons, `GameInstaller` for match-scoped. Null implementations are bound directly in `ProjectInstaller` for the demo build (no separate `DemoInstaller`).
- **All log output goes through `ILogService`** — no ad-hoc `Debug.Log`. Pass a `Source` tag string per call. Default `MinLevel` is `Verbose` during dev; raise it for release builds via the `_logMinLevel` field on `ProjectInstaller`.
- **URP materials only** — no Built-in RP shaders. Use Lit, Unlit, or custom URP Shader Graph.
- **Animation is local** — `ChickenAnimator` reads `[Networked]` state; nothing animation-related is synced over the network.
- **VFX are local** — triggered by observed networked state changes, never sent over network.

---

## Key Interfaces

```csharp
INetworkService              // All Fusion calls go through here
IInputProvider               // GetMovement(), GetAttackHeld(), GetAbility1/2Pressed()
IAudioService                // PlaySFX(), PlayMusic(), Stop/Volume
IUGSService                  // Auth, Lobby, Relay — NullUGSService in demo
ILogService                  // Verbose/Debug/Info/Warn/Error with Source tag + level filter
ISessionSelectionService     // Carries chosen ChickenClass from menu to match
```

---

## Zenject Binding Pattern

Two contexts:

- **`ProjectInstaller`** — lives on `Assets/_Game/Resources/ProjectContext.prefab`. Binds app-wide singletons (`IInputProvider`, `IAudioService`, `IUGSService`). Survives scene loads.
- **`GameInstaller`** — lives on the `SceneContext` GameObject in `Game.unity`. Binds match-scoped state (`MatchConfigSO`, `INetworkService`).

```csharp
// ProjectInstaller (Resources/ProjectContext.prefab)
Container.Bind<ILogService>().To<UnityLogService>().AsSingle()
    .WithArguments(_logMinLevel);                                       // Verbose by default
Container.Bind<IUGSService>().To<NullUGSService>().AsSingle();          // demo
Container.Bind<IAudioService>().To<NullAudioService>().AsSingle();      // demo
Container.Bind<IInputProvider>().To<KeyboardInputProvider>().AsSingle();
Container.Bind<ISessionSelectionService>().To<SessionSelectionService>().AsSingle();
Container.Bind<ChickenClassRegistrySO>().FromInstance(_chickenClassRegistry).AsSingle();

// GameInstaller (Game.unity SceneContext)
Container.Bind<MatchConfigSO>().FromInstance(_matchConfig).AsSingle();
Container.Bind<INetworkService>().To<FusionNetworkService>()
    .FromNewComponentOnNewGameObject().AsSingle().NonLazy();
```

Single-player dev sessions go through `FusionNetworkService.StartSoloAsync()` (`GameMode.Single`). There is no separate `LocalNetworkService` — `NetworkBehaviour` requires a `NetworkRunner`, so even the offline path is a Fusion runner with one player.

Scripting define `UGS_DISABLED` forces `NullUGSService` in any build regardless of config.

### Self-injection from `ProjectContext`

`Bootstrap.unity` has no `SceneContext`, and Fusion spawns `NetworkBehaviour`s outside Zenject's normal injection path. Both cases use the same self-inject pattern in `Awake` / `Spawned`:

```csharp
if (_log == null)               // _log is set by [Inject] Construct, so null = "not yet injected"
{
    ProjectContext.Instance.Container.Inject(this);
}
```

Two non-obvious gotchas the hard way:

1. **Don't gate on `ProjectContext.HasInstance`** — that returns `false` until something accesses `.Instance` for the first time. Reading `.Instance` directly triggers the lazy load.
2. **Two `ProjectInstaller`s live in the project** — ours under `CluckWars.Installers` and a stale one shipped inside Zenject's `OptionalExtras/IntegrationTests`. Always pick the `CluckWars.Installers` one when adding via the Inspector. The OptionalExtras folders were deleted to prevent this kind of name collision.

---

## Chicken Architecture

```
ChickenController (NetworkBehaviour)
├── [Networked] ChickenClass   Replicated; spawner sets via OnBeforeSpawned
├── ChickenStatsSO              Resolved at Spawned() from ChickenClassRegistrySO
├── ChickenMovement             Reads Fusion input, moves with client-side prediction
├── ChickenCombat               Attack detection + damage RPC + death stun
├── ChickenCargo                Collect from piles, deposit at base, drop on death
├── AbilityController           Equipped AbilitySOs, cooldown tracking      (Phase 6)
├── ChickenAnimator             Local — drives Animator from [Networked] state
└── ChickenVisuals              Local — applies tint via MaterialPropertyBlock
```

**Class-aware spawn flow:**

1. Bootstrap scene: `CharacterSelectController` writes the chosen `ChickenClass` to `ISessionSelectionService` (singleton bound in `ProjectContext`).
2. Game scene: `MatchBootstrapper` reads the selection and calls `Runner.Spawn(prefab, …, onBeforeSpawned: …)` to set `ChickenController.Class` before `Spawned()` fires anywhere.
3. Every peer's `Spawned()` self-injects from `ProjectContext.Instance.Container`, looks up the entry in `ChickenClassRegistrySO`, applies stats + tint locally.

---

## Ability System

```csharp
public abstract class AbilityBaseSO : ScriptableObject
{
    public float Duration;
    public float Cooldown;
    public AnimationClip AbilityAnimationClip;
    public abstract void OnActivate(ChickenController owner);
    public abstract void OnDeactivate(ChickenController owner);
}
```

Adding a new ability = create a concrete `AbilityBaseSO` asset + asset file in `Data/Abilities/`. No code changes to existing systems.

---

## Networking Rules

- **Tick rate:** 30/s — tuned for Android. Configurable via `MatchConfigSO`.
- **Networked state:** position, HP, carried cargo, stun, ability cooldowns, base food, pile amounts.
- **Input struct:**
  ```csharp
  public struct PlayerNetworkInput : INetworkInput
  {
      public Vector2 Movement;
      public NetworkButtons Buttons;   // Attack=0, Ability1=1, Ability2=2 (see InputButton enum)
  }
  ```
- **StateAuthority only** runs game systems. Results replicate via `[Networked]`.
- **Demo mode:** Fusion 2 Shared Mode — any device (PC or Android) can host.

---

## Character Classes (Quick Reference)

| Class | Cargo | Speed | Attack | Special |
|---|---|---|---|---|
| Fatty Chicken | ⭐⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐ | Highest capacity + rate |
| Speedy Chicken | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | Hit-and-run |
| Warrior Chicken | ⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐ | All-rounder |
| Assassin Chicken | ⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | Equips 2 abilities |

---

## URP Notes

- Project uses URP — all materials must use URP-compatible shaders.
- Renderer Data and render features live in `/Assets/_Game/Art/URP/`.
- URP 2D lights can be used for ambient warmth and food pile glow — validate against Android performance budget before committing.
- Use URP Shader Graph for any custom character or VFX shaders.

---

## Design Docs (for context, not code)

| Doc | Location | Contents |
|---|---|---|
| GDD v0.2 | `docs/GDD.md` | Mechanics, classes, abilities, monetization |
| TDD v0.1 | `docs/TDD.md` | Architecture, networking, systems |
| ART v0.1 | `docs/ART.md` | Visual style, palette, UI layout, VFX |
| ROADMAP | `docs/ROADMAP.md` | 9-phase plan to demo milestone |

Notion originals: linked in project instructions for deep reads.

---

## Demo Milestone (Definition of Done)

- [ ] Any device (PC or Android) can host LAN session
- [ ] 4 players connect and complete a full 5–10 min match
- [ ] All 4 classes playable with at least 2 abilities each
- [ ] Full loop: spawn → collect → fight → win condition → end screen
- [ ] Windows & Android builds stable at target FPS (60 / 30)
- [ ] Session join by ID — no UGS dependency
- [ ] `NullUGSService` injected via `UGS_DISABLED` scripting define in demo build

---

## Current Phase

**Phase 5 — multiplayer networking — code landed.** Bootstrap-scene UI now picks both class (1-4) and session mode (S=Solo / H=Host / J=Join) before SPACE confirms and loads `Game.unity`. `MatchBootstrapper` branches on the selected mode and calls `StartSoloAsync` / `StartHostAsync(sessionName)` / `JoinSessionAsync(sessionName)` accordingly. Session name is hardcoded to `cluck-lan` for the demo — Host creates it, Join joins by exact name; both peers must use the same Photon AppId (`PhotonAppSettings.asset` already has `AppIdFusion = 259bda28-…`).

The Bootstrap menu is a procedural UGUI canvas built at runtime by `CharacterSelectController.BuildCanvas()` — clickable buttons for class + mode + Start, with the same keyboard shortcuts (1-4 / numpad, S/H/J, SPACE/ENTER) still wired in `Update`. An `EventSystem` with `InputSystemUIInputModule` is auto-created if the scene doesn't already have one (Bootstrap.unity stays minimal). Project locked to landscape: `ProjectSettings.asset` disallows portrait autorotation, and the controller's `Awake` re-asserts that at runtime as a safety net.

**Mobile touch controls** for the Game scene shipped early (was Phase 8). `TouchControlsHud` (in `Assets/_Game/Scripts/Input/`) procedurally builds a joystick (left thumb), an attack button (right thumb, large), and two ability buttons (Q / E placeholders) on Awake. `VirtualJoystick` and `HoldButton` are the underlying UGUI controls; `HoldButton` exposes both `IsHeld` (for attack mash) and `WasPressedThisFrame` (for one-shot ability triggers, mirroring Input System edge timing). `TouchInputProvider` reads from the HUD singleton; `CompositeInputProvider` ORs keyboard + touch so PC and mobile input both drive the same chicken — and clicking the on-screen buttons with a mouse on Windows is a free way to test the touch flow without a build. `ProjectInstaller` binds the composite as `IInputProvider`.

Late-join handling is already correct: `HandlePlayerJoined` filters `player == runner.LocalPlayer` so each peer spawns only its own chicken; remote chickens, food piles, and bases replicate via Fusion's normal state sync. No code change needed for that.

`ISessionSelectionService` extended with `SessionMode Mode` + `string SessionName` (defaults: `Solo` + `"cluck-lan"`).

**Outstanding for Maestro before smoke-test:**
- One Windows build + one Android build, both pointing at the same Photon AppId.
- Pick H on the Windows host, J on the Android client. Both should land in the same scene with their own chicken; piles and bases sync.
- Verify cross-client paths from Phases 3-4: damage RPC, drain RPC, deposit RPC.

**Phase 4b — drop on death — done.** `FoodPickup` NetworkBehaviour spawned by `ChickenCargo.HandleDeath` carrying cargo amount; auto-despawns when drained or after 30s. Picked up by walking over it. Verified prefab + Chicken wiring is committed.

**Phase 7 — match loop — code landed.** `GameManager` (NetworkBehaviour, master-client owned) drives a 3-state machine (`WaitingForPlayers` → `Active` → `Ended`) with `[Networked] MatchTimer` (`TickTimer`), `WinnerPlayer` (`PlayerRef`), `WinnerFoodTotal` (float). On Spawned the master client transitions to `Active` and starts the match-duration countdown from `MatchConfigSO.MatchDurationSeconds`. Win check fires 4× / sec on the StateAuthority: scans every `PlayerBase` in scene; first to `MatchConfigSO.FoodTargetToWin` ends the match, else timer expiry picks the highest total. `MatchBootstrapper` spawns the GameManager prefab once Fusion is up, master-client-side only (`runner.IsSharedModeMasterClient`).

The HUD picked up timer + end overlay duties: `CargoHud` (despite the name, kept stable to avoid scene re-wiring) now renders a top-center MM:SS timer while `Active` and a centered "MATCH ENDED" banner with winner / final-food when `Ended`. Per-base totals show in the right panel — when Phase 7b lands per-player base ownership, the same code will display per-player labels naturally.

**Outstanding for Maestro before smoke-test:**
- Author a `GameManager` prefab: NetworkObject + `GameManager` script.
- Drag the prefab into `MatchBootstrapper._gameManagerPrefab` on the MatchBootstrapper GameObject in `Game.unity`.

**Phase 7b — per-player bases — code landed.** `GameManager.AssignBasesToPlayers` runs every `FixedUpdateNetwork` tick on the master client: iterates `Runner.ActivePlayers`, and for any player without a base, stamps `Owner` onto the first unowned `PlayerBase` in scene. Idempotent and cheap (4 × 4 worst case). `ChickenCargo.FindNearestBaseInRange` now filters by `b.Owner == Object.InputAuthority` — chickens only deposit at their own base. Win check (`EvaluateWinCondition` + `EndOnTimerExpiry`) now skips unowned bases so a stray scene-baked base can't trigger a `PlayerRef.None` winner. `CargoHud` shows per-player labels: `P1: 42  P2: 17  ?: 0` for any base GameManager hasn't assigned yet.

**Procedural map — code landed.** `MapGenerator` (MonoBehaviour, in `Scripts/Gameplay/`) replaces hand-placed scene clutter with one configurable component:

- **Local on every peer (Awake):** builds a Unity Plane primitive scaled to `_planeSize`, optionally tinted with `_groundMaterial`. Caches 4 corner spawn positions for `MatchBootstrapper`.
- **Master client only** (subscribes to `INetworkService.OnRunnerReady`): `Runner.Spawn`s 4 `PlayerBase` instances at the corners (slightly inset toward center), 1 large center `FoodPile` (configurable `_centerPileAmount` + visual scale), and N small piles around the center on a jittered ring (`_smallPileCount` / min / max radius). Per-instance `Amount` / `MaxAmount` set via `onBeforeSpawned` so the center pile can hold more than the satellites.

`FoodPile.Spawned` was tweaked to only seed the prefab default when `MaxAmount <= 0`, so callers using `onBeforeSpawned` can override per-instance.

**Outstanding for Maestro before smoke-test:**
- Add a `MapGenerator` GameObject to `Game.unity`. Drag `Chicken/PlayerBase.prefab` into `_basePrefab`, `FoodPile.prefab` into `_foodPilePrefab`. Optional: assign a URP/Lit material to `_groundMaterial` so the plane isn't magenta.
- Remove the hand-placed `Plane`, `PlayerBase` instance, and `FoodPile` instance from `Game.unity` — `MapGenerator` builds them at runtime now.

**Match restart loop — code landed.** `GameManager` now drives a full reset cycle. On `EndMatch` it arms `[Networked] RestartCountdown` (`TickTimer`, default 6s). `FixedUpdateNetwork` watches for the countdown to expire while `State == Ended` and then calls `RestartMatch`:

- Master mutates scene-NetworkObject state directly: every `PlayerBase.FoodTotal` to 0, every `FoodPile.Amount` back to its `MaxAmount`.
- Loose `FoodPickup`s on the ground are `Runner.Despawn`ed so the fresh round starts clean.
- Per-chicken state is cross-authority, so master iterates and fires `ChickenCombat.RPC_ResetForNewMatch` + `ChickenCargo.RPC_ResetForNewMatch` on every chicken — both are `RpcSources.All → RpcTargets.StateAuthority` so the call routes to the chicken's owning client, which restores HP / clears stun / zeros cargo.
- `State` flips back to `Active`, `MatchTimer` is reset to `MatchDurationSeconds`, `WinnerPlayer` cleared.

`CargoHud` end overlay now reads `_gameManager.RestartRemaining` and shows "Next match in Ns…" instead of the old "(reload the scene)" hint.

**Chicken position reset is deferred** — chickens stay wherever they were when the round ended. Per-corner teleport on restart can land in Phase 9 polish once we add a stable PlayerId → corner mapping in `GameManager`.

**Spawn-at-base-edge** is still deferred — `MatchBootstrapper` now uses `MapGenerator.SpawnPoints` keyed by `Mathf.Abs(player.PlayerId) % count` for deterministic per-peer agreement. Pairing spawn corner with the base eventually assigned to that player is a small Phase 9 polish item.

**Phase 6 — abilities — code landed.** Two new namespaces, one new component, two SO implementations:

- **`Assets/_Game/Scripts/Abilities/`** — `AbilityBaseSO` (abstract: Duration / Cooldown / accent color / animation clip + abstract `OnActivate(ctx)` / `OnDeactivate(ctx)`), `AbilityContext` (carries the `ChickenController` so SOs stay clean of `GetComponent`), and the first two concrete SOs:
  - **`SpeedBurstAbilitySO`** — multiplies `ChickenController.MoveSpeedMultiplier` for the duration.
  - **`EggShellAbilitySO`** — toggles `MovementLocked` + `DamageImmune` for the duration.
- **`Assets/_Game/Scripts/Gameplay/AbilityController.cs`** — NetworkBehaviour on the Chicken prefab. Two equipped slots (slot 0 universal, slot 1 Assassin-only). `[Networked]` ActiveSlot + ActivationTimer + per-slot Cooldown timers. Reads `Ability1` / `Ability2` from the Fusion input buffer, single-active-at-a-time, deactivates on duration expiry or owner death.
- **Hooks on `ChickenController`** — three new public properties (`MoveSpeedMultiplier`, `MovementLocked`, `DamageImmune`) live only on the StateAuthority; `ChickenMovement` reads them per tick (lock zeros planar input but keeps gravity), and `ChickenCombat.RPC_ApplyDamage` short-circuits when the target is immune. Other peers don't mirror these — they observe the resulting position / HP via existing `[Networked]` state. Cooldown progress IS networked (via `AbilityController` state), so the touch HUD's radial fill is correct on every peer.
- **`TouchControlsHud`** now creates a radial-fill cooldown overlay per ability button. Each frame it finds the local chicken via `HasInputAuthority` and reads `AbilityController.CooldownRemaining(slot)` — overlay fillAmount = remaining/total, so a freshly-cast ability is fully covered and shrinks counter-clockwise from the top.

**Outstanding for Maestro before smoke-test:**
- Add `AbilityController` to the Chicken prefab.
- Create `SpeedBurst.asset` and `EggShell.asset` under `/Assets/_Game/Data/Abilities/` via the new `Cluck Wars/Ability/…` menu items. Tune Duration / Cooldown / SpeedMultiplier there; current defaults are placeholders.
- Drag the assets into `AbilityController._slot0` on the Chicken prefab. Slot 1 only matters once Assassin gets a second ability — leave null for now.

**Phase 6b — code landed.** Four more concrete `AbilityBaseSO` subclasses + the supporting controller / combat / visuals hooks they need:

- `TurtleModeAbilitySO` — sets `MoveSpeedMultiplier` (slow) + `DamageResistance` (% damage absorbed). Resets both on deactivate.
- `SpineCoatAbilitySO` — toggles `ReflectDamage`. While on, `ChickenCombat.RPC_ApplyDamage` bounces the damage back to the attacker (looked up via `Object.InputAuthority`) instead of applying it locally.
- `InvisibilityAbilitySO` — sets `ChickenController.VisualOpacity`. `ChickenVisuals.LateUpdate` polls and re-pushes the tint with the new alpha. Local-only fade; remote peers don't see the invisibility (networked opacity is Phase 9 polish).
- `RollTrampleAbilitySO` — one-shot offensive sweep on activate. `Physics.OverlapSphere` centered ahead of the caster, slams every hit chicken with `TrampleDamage` (default 200, enough to instant-stun a full-HP target). No persistent state.

`ChickenController` gained three new state hooks: `DamageResistance`, `ReflectDamage`, `VisualOpacity`. `ChickenCombat.RPC_ApplyDamage` now takes a `PlayerRef attacker` argument so reflection knows where to bounce; `Swing()` passes `Object.InputAuthority` automatically.

**Phase 6c — code landed.** `SneakyStealAbilitySO` ships the cargo-theft pattern: thief calls `OverlapSphere`, finds the nearest enemy `ChickenCargo` with cargo on board, optimistically credits its own `Cargo` (capped to free space + victim's stock), then calls `target.RPC_DrainStolen(amount)` to drain on the victim's authority side. New `ChickenCargo.RPC_DrainStolen` is `RpcSources.All → RpcTargets.StateAuthority` and clamps to current cargo so over-requests are harmless — same shape as `FoodPile.RPC_Drain`.

**Build menu.** `Assets/_Game/Scripts/Editor/CluckWarsBuildMenu.cs` ships a one-button build pipeline:

- `Cluck Wars / Build / Windows` (`Ctrl+Shift+W`) — `StandaloneWindows64`, output `Builds/Windows/CluckWars.exe`.
- `Cluck Wars / Build / Android` (`Ctrl+Shift+A`) — forces IL2CPP + ARM64 + minSdk 24, output `Builds/Android/CluckWars-<version>.apk` (.apk, not .aab — sideload-friendly).
- `Cluck Wars / Build / Windows + Android` (`Ctrl+Shift+B`) — sequential.
- `Cluck Wars / Build / Reveal Builds Folder` — opens `Builds/` in Explorer.
- Reads scenes from `EditorBuildSettings`, warns if Bootstrap.unity isn't index 0. `Builds/` is gitignored.

**Pre-test polish — code-only items that either save diagnosis time during the deferred test session or capture design intent now:**

- **`DebugHud`** (toggle with **F1**) — IMGUI overlay showing FPS, network mode + active player count, GameManager state + timer + intro / restart countdowns, local chicken (class / HP / cargo / equipped abilities + cooldowns / active slot), per-base ownership + food total, loose pickup count. Read-only — no commands or networked side effects. Drop the component anywhere in `Game.unity`.
- **Class on nameplate** — `ChickenNameplate` now reads `ChickenController.Class` and renders "P1 Warrior" / "P2 Speedy" / etc. instead of just "P1". Re-renders when either authority or class changes (Doppelganger decoy re-stamps both).
- **Per-class ability allowlist** — `ChickenStatsSO.AvailableAbilities` (GDD §7.1). Empty array = no restriction (current behavior); fill in to gate which abilities a class can equip. `AbilityController.Spawned` warns (doesn't block) when an equipped slot isn't in the class's pool.
- **Hit-flash overlay** — full-screen red tint in `MatchHud` that pulses when the local chicken's HP drops. Fades back to transparent over ~0.35s. Tunable peak alpha / fade seconds on the MatchHud component.

**Pre-Phase-8 advancements (Phase 9b).** Five code-only items shipped on top of Phase 9, picked because they don't need device profiling or playtest data:

- **Isometric camera (`MatchCamera`)** — per ART.md §2. Orthographic, 45° yaw + 30° pitch, framed via `_orthoSize`. Drop on the Main Camera in `Game.unity`; tweak `_orthoSize` if the plane clips off-screen.
- **UGUI `MatchHud`** — replaces the IMGUI `CargoHud`. Top bar with centered MM:SS timer + 4 player food totals (color-coded). Bottom-left HP + cargo bars (`Image.FillMethod.Horizontal`). Centered match-end overlay with sorted leaderboard + live "Next match in Ns…" countdown. Centered session-end overlay on disconnect with auto-return to Bootstrap. Disables any existing `CargoHud` on Awake so the two HUDs don't overlap during migration.
- **Player nameplates (`ChickenNameplate`)** — floating "P1/P2/P3/P4" `TextMesh` above each chicken, tinted with the 4-color player palette (Red/Blue/Green/Yellow). Billboards toward the camera every frame. Drop on the Chicken prefab.
- **Intro countdown** — `GameManager.IntroTimer` (`[Networked]`) runs for `_introSeconds` (default 3) before the match timer ticks. `MatchHud` shows a big centered "3 / 2 / 1 / GO!" overlay. `MatchTimer` is offset by the intro window so playable duration stays equal to `MatchConfigSO.MatchDurationSeconds`; win-condition checks gate out during intro.
- **Ability button accent** — `TouchControlsHud` re-tints each ability button using the equipped `AbilityBaseSO.AccentColor` (Speed Burst, Egg Shell, etc. now visually distinct). Cached per-slot; only writes on equipped-SO change.

**Outstanding for Maestro before smoke-test:**
- Add `MatchCamera` to the Main Camera GameObject in `Game.unity`.
- Add `MatchHud` to a fresh GameObject under `Hud` (or anywhere in Game.unity) — it builds its own canvas + disables the legacy `CargoHud` on Awake.
- Add `ChickenNameplate` to the Chicken prefab.
- Tune `AbilityBaseSO.AccentColor` on each of the 8 ability assets if you want stronger per-ability palette differences.

**Phase 9 — polish pass — code landed.** Four shippable waves; the remaining roadmap items in Phase 9 (network desync hunting, on-device profiling, animator polish, balance) need real-device or playtest data, not code.

- **Audio (`b7eb0ae`)** — `UnityAudioService` replaces `NullAudioService`. Two `AudioSource`s on a child of `ProjectContext` (so audio survives scene loads); SFX via `PlayOneShot`, music looping. `AudioRegistrySO` centralizes the clip refs. SFX wired in: combat (Swing on attack epoch / Hit on HP-decrease / Stun on stun-begin), cargo (Deposit, Pickup), abilities (Activate, Expire), match (Start + music, End / Victory sting + stop music). Maestro creates the registry asset, drops clips into named fields, drags into `ProjectInstaller._audioRegistry`.
- **`PrefabRegistrySO` (`087b790`)** — one SO with named NetworkObject fields for Chicken / Doppelganger / FoodPile / FoodPickup / PlayerBase / GameManager. `MatchBootstrapper`, `MapGenerator`, `ChickenCargo` resolve via `Resolve…Prefab()` helpers that prefer the registry but fall back to existing SerializeField slots. Gradual migration — drop a registry asset to centralize, leave a slot empty to keep per-component override.
- **`ColorSchemeSO` (`5b39006`)** — HUD palette (panel / buttons / start / text) + touch HUD colors (joystick, attack, ability, cooldown dim) + food-pile state (full / empty). `TouchControlsHud`, `CharacterSelectController`, `FoodPileVisuals` all read from it. `CharacterSelectController` builders went from `static` to instance methods so they can reach the injected scheme. Per-class chicken tints stay on `ChickenClassRegistrySO`; per-ability accents stay on each `AbilityBaseSO`.
- **Reconnection (`2a94fc8`)** — `CargoHud` subscribes to `INetworkService.OnShutdown`. On disconnect, the match HUD is replaced by a "SESSION ENDED" overlay (reason + live countdown); after `_disconnectReturnDelay` (default 5s) `SceneManager.LoadScene("Bootstrap")` returns the user to the menu where they can pick a fresh session.

**Outstanding for Maestro before smoke-test:**
- Create `AudioRegistry.asset`, `PrefabRegistry.asset`, `ColorScheme.asset` under `/Assets/_Game/Data/` via the new `Cluck Wars/…` menu items. Drop them into the matching slots on `ProjectInstaller` (on the `ProjectContext.prefab`).
- Fill in `PrefabRegistry` immediately (it's the only one that has functional impact when empty — though current consumers fall back to their legacy slots). Audio and color are silent / default-themed until you fill them in.

**Phase 9 deferred:** Networking-bug audit, draw-call / memory optimization, animator polish, balance pass, UGUI HUD redesign (replacing the IMGUI `CargoHud` per ART.md §6.1). All need device profiling or playtest data — pick up after Phase 8 Android build lands.

**Phase 6d — code landed.** All 8 abilities now have concrete `AbilityBaseSO` subclasses. Doppelganger is the last and is implemented as a Chicken-prefab-variant decoy that takes hits + plays animations like a real chicken, but doesn't read input.

The decoy mechanic relies on a single new flag — `ChickenController.IsDecoy` — that gates input-processing branches in `ChickenController.FixedUpdateNetwork` (no input movement), `ChickenCombat.FixedUpdateNetwork` (no swing input read), and `AbilityController.FixedUpdateNetwork` (no ability input). All three early-return when the flag is set. Damage RPCs, hit-anim ChangeDetectors, tint, and the chicken's idle pose all still work — the decoy looks alive and reacts to hits.

- **`Doppelganger`** (NetworkBehaviour, `Scripts/Gameplay/`): `[Networked] LifetimeTimer` + per-frame despawn check on the StateAuthority. On `Spawned` it sets `IsDecoy = true` on the sibling `ChickenController` and subscribes to `ChickenCombat.OnDeath`; if killed, the decoy `Runner.Despawn`s immediately rather than entering the stun-and-respawn cycle. If lifetime expires first, same outcome.
- **`DoppelgangerAbilitySO`** (`Scripts/Abilities/`): `Runner.Spawn`s the decoy at a side-offset (default 1.2m) from the caster. `onBeforeSpawned` sets `ChickenController.Class = caster.Class` (so the existing tint code Just Works on every peer) and arms `Doppelganger.LifetimeTimer`. Caster gets InputAuthority over the decoy. `OnDeactivate` is a no-op.

**Outstanding for Maestro before smoke-test:**
- The `Doppelganger.prefab` exists already as a Chicken-prefab variant with `ChickenCargo` stripped — keep it as a variant so `Doppelganger` script + animator + visuals + combat carry over for free. Make sure the `Doppelganger` component is added to that variant.
- Create a `Doppelganger.asset` via `Cluck Wars/Ability/Doppelganger`, drag the prefab into the asset's `_decoyPrefab` slot.
- Equip on the Chicken prefab (Assassin slot 1 is the natural home).

---

## Team

**Maestro** — Senior Engineer, telco background, Unity/C#/Android expertise, Madrid.
