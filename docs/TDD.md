# Technical Design Document v0.1

**Version:** 0.1  
**Date:** May 2026  
**Status:** Draft

---

## 1. Overview & Guiding Principles

This document defines the technical architecture for Cluck Wars. All decisions are evaluated against three priorities in order:

1. **Demo viability** — must work as LAN/local with zero infrastructure cost.
2. **Replaceability** — networking, backend, and audio layers must be behind interfaces so they can be swapped post-funding without touching game logic.
3. **Simplicity** — no over-engineering. MonoBehaviour over ECS/DOTS unless a concrete performance bottleneck justifies the complexity cost.

---

## 2. Engine & Tooling

| Tool | Choice | Rationale |
|---|---|---|
| Engine | Unity 6000.3 LTS (Unity 6) | Latest LTS — improved URP, better mobile performance |
| Render Pipeline | Universal Render Pipeline (URP) | Built-in URP support in Unity 6; required for mobile fidelity and batching |
| Language | C# | Standard Unity |
| Networking | Photon Fusion 2 | Fusion 1 enters maintenance-only in 2026; Fusion 2 is the active branch |
| Dependency Injection | Zenject (Extenject) | Industry-standard, free, open source. Good CV value |
| Backend | Unity Gaming Services (UGS) | Free tier covers demo; always behind interface so it can be muted |
| Target platforms | Windows (primary dev), Android, iOS | Windows for fast iteration, mobile first-class from day one |
| Architecture | MonoBehaviour | No ECS/DOTS — complexity cost not justified at this scale |

---

## 3. Dependency Injection — Zenject

Zenject (Extenject) is the DI framework for the entire project. All service dependencies are injected — no class instantiates its own dependencies directly.

### 3.1 Binding Pattern

Two contexts split bindings by lifetime:

- **`ProjectInstaller`** lives on `Assets/_Game/Resources/ProjectContext.prefab`. Binds singletons that survive scene loads — services, static data, cross-scene state.
- **`GameInstaller`** lives on the `SceneContext` GameObject in `Game.unity`. Binds match-scoped wiring — `MatchConfigSO`, the Fusion runner.

```csharp
// ProjectInstaller (Resources/ProjectContext.prefab)
Container.Bind<ILogService>().To<UnityLogService>().AsSingle()
    .WithArguments(_logMinLevel);
Container.Bind<IUGSService>().To<NullUGSService>().AsSingle();      // demo default
Container.Bind<IAudioService>().To<NullAudioService>().AsSingle();  // demo default
Container.Bind<IInputProvider>().To<KeyboardInputProvider>().AsSingle();
Container.Bind<ISessionSelectionService>().To<SessionSelectionService>().AsSingle();
Container.Bind<ChickenClassRegistrySO>().FromInstance(_chickenClassRegistry).AsSingle();

// GameInstaller (Game.unity SceneContext)
Container.Bind<MatchConfigSO>().FromInstance(_matchConfig).AsSingle();
Container.Bind<INetworkService>().To<FusionNetworkService>()
    .FromNewComponentOnNewGameObject().AsSingle().NonLazy();
```

Swapping an implementation post-funding = change one `To<>()` binding in `ProjectInstaller`. Zero other code changes.

### 3.2 Null Bindings (Demo Default)

The demo build binds null/no-op implementations directly in `ProjectInstaller` — there is no separate `DemoInstaller`. `NullUGSService` is the default; the real `UGSService` will land in Phase 10 alongside its dependencies.

### 3.3 Compile-Time Override

Scripting define `UGS_DISABLED` is reserved to force `NullUGSService` regardless of inspector config in clean demo/production builds. Bindings inside `ProjectInstaller` honor this define when the real `UGSService` lands.

### 3.4 Self-Injection from `ProjectContext`

Two cases skip Zenject's normal scene-component injection path:

1. `Bootstrap.unity` has no `SceneContext` (it's just a menu scene).
2. Fusion's `Runner.Spawn` instantiates `NetworkBehaviour`s outside Zenject's lifecycle.

Both self-inject from `ProjectContext`:

```csharp
if (_log == null) ProjectContext.Instance.Container.Inject(this);
```

**Do not** gate on `ProjectContext.HasInstance` — it returns `false` until something reads `.Instance` for the first time. Reading `.Instance` directly triggers the lazy load.

---

## 4. Networking Architecture

### 4.1 Model

Cluck Wars uses **Photon Fusion 2** throughout. The mode changes between demo and post-funding:

| Mode | Used when | Host |
|---|---|---|
| Shared Mode | Demo — LAN, any device hosts | One peer acts as StateAuthority |
| Server Mode | Post-funding — dedicated server | Dedicated server process, fully authoritative |

Shared Mode allows any device (PC or Android) to host for the demo. The transition to Server Mode is a configuration and transport swap, not a rewrite.

### 4.2 The Networking Interface

No game logic class ever calls Fusion directly. All networking goes through:

```csharp
public interface INetworkService
{
    bool IsRunning { get; }
    NetworkRunner Runner { get; }

    Task StartSoloAsync();                            // GameMode.Single — solo dev / Phase 1-4
    Task StartHostAsync(string sessionName);          // GameMode.Shared — Phase 5+
    Task JoinSessionAsync(string sessionName);        // GameMode.Shared — Phase 5+
    Task ShutdownAsync();

    event Action<NetworkRunner> OnRunnerReady;
    event Action<NetworkRunner, PlayerRef> OnPlayerJoined;
    event Action<NetworkRunner, PlayerRef> OnPlayerLeft;
    event Action<ShutdownReason> OnShutdown;
}
```

Concrete implementation:

```
INetworkService
└── FusionNetworkService       ← all modes; offline path is GameMode.Single
```

There is **no** separate `LocalNetworkService`. `NetworkBehaviour` requires a `NetworkRunner`, so even the offline path runs Fusion in `GameMode.Single` with one player. The cost is one local socket; the benefit is no fork in spawn / state-sync logic between solo and multiplayer.

### 4.3 Session Modes

Three modes supported from day one via enum passed at startup:

```csharp
public enum SessionMode
{
    ListenServer,       // Any device is host + player (demo default)
    DedicatedServer,    // Device runs headless server, no local player
    Client              // Device joins an existing session
}
```

`INetworkService` handles Fusion-specific setup internally per mode. Game logic never branches on `SessionMode`.

### 4.4 Tick & State Sync

- **Fixed tick rate:** 30 ticks/second — tuned for mid-range Android. Adjustable via `MatchConfigSO`.
- **Model:** Input authority + state sync. Each client sends inputs; StateAuthority runs simulation and replicates `[Networked]` state.
- **Client-side prediction:** Enabled for local player movement via Fusion 2 built-in prediction. Remote players interpolated.
- **Lag compensation:** Fusion 2 native lag compensation used for attack proximity checks.

### 4.5 Networked State

| State | Sync method | Notes |
|---|---|---|
| Position & rotation | `[Networked]` + interpolation | Smooth on all clients |
| HP | `[Networked]` | Integer, authority-only writes |
| Carried cargo | `[Networked]` | Updated on pickup/deposit |
| Stun state + timer | `[Networked]` | Bool + float |
| Ability cooldowns | `[Networked]` | Per-ability float |
| Base stored food | `[Networked]` | Per-player int, win condition checked here |
| Food pile amounts | `[Networked]` | Decrements only, no respawn |

### 4.6 Input Struct

```csharp
public struct PlayerNetworkInput : INetworkInput
{
    public Vector2 Movement;
    public NetworkButtons Buttons;   // see InputButton enum below
}

public enum InputButton
{
    Attack   = 0,
    Ability1 = 1,
    Ability2 = 2,   // Assassin only; ignored otherwise
}
```

`NetworkButtons` is Fusion's bitfield helper — cheaper to wire than three separate `NetworkBool`s and gives us free press/release edge detection later. Game logic reads exclusively from the Fusion input buffer — never from `Input.GetKey` / `Keyboard.current` directly.

---

## 5. Unity Gaming Services (UGS)

### 5.1 Interface

```csharp
public interface IUGSService
{
    Task InitializeAsync();
    Task<string> SignInAnonymouslyAsync();
    Task<string> CreateLobbyAsync(string sessionId);
    Task JoinLobbyAsync(string sessionId);
    Task<string> GetRelayJoinCodeAsync();
}
```

`NullUGSService` implements all methods as silent no-ops or returns sensible defaults. Injected via Zenject in demo builds.

### 5.2 Services Rollout

| Service | Demo | Post-funding | Free tier |
|---|---|---|---|
| Authentication | Anonymous (local) | Full cross-platform accounts | 1,000 MAU free |
| Relay | No (LAN direct) | Yes — no open ports needed | 50 CCU free |
| Lobby | No (join by session ID) | Yes — matchmaking, session browser | 50 CCU free |
| Cloud Save | No | Yes — cross-platform progression sync | 1 GB free |
| Analytics | Optional | Yes — balance data, funnels | Generous free tier |
| Crash Reporting | Optional | Yes | Free |

### 5.3 Demo Session Flow

```
Host (PC or Android): launch → select Host → enter session name → game starts
Client (any device):  launch → select Join → enter session name → connects via LAN
```

LAN discovery via Fusion 2 built-in — no UGS dependency.

### 5.4 Post-Funding Session Flow

```
Client → UGS Authentication → UGS Lobby → Fusion via UGS Relay → Dedicated server
```

Entire flow is encapsulated in `FusionNetworkService` + `UGSService`. Game logic untouched.

---

## 6. Game Architecture

### 6.1 Core Systems

```
GameManager
├── PlayerSpawner        ← spawns/despawns NetworkObjects on join/leave
├── FoodSystem           ← pile state, cargo collection tick, pile visual events
├── CombatSystem         ← attack input processing, damage application
├── AbilitySystem        ← cooldown tracking, ability effect execution
└── MatchStateSystem     ← timer, scores, win/lose condition, end screen
```

All systems run on StateAuthority only. Results replicate to clients via `[Networked]` state.

### 6.2 Chicken Architecture

```
ChickenController (NetworkBehaviour)
├── ChickenStatsSO           ← ScriptableObject: base values per class
├── ChickenMovement          ← reads input, moves with prediction
├── ChickenCombat            ← attack detection, damage
├── ChickenCargo             ← cargo tracking, collection, deposit
├── AbilityController        ← manages equipped AbilitySO assets, cooldowns
├── ChickenAnimator          ← local only, drives Animator from observed state
└── ChickenVisuals           ← local only, material/mesh, no network sync
```

### 6.3 Ability System

Abilities are data-driven via ScriptableObjects:

```csharp
public abstract class AbilityBaseSO : ScriptableObject
{
    public float Duration;
    public float Cooldown;
    public AnimationClip AbilityAnimationClip; // one of 2-3 shared clips
    public abstract void OnActivate(ChickenController owner);
    public abstract void OnDeactivate(ChickenController owner);
}
```

Each ability is a concrete `AbilitySO` asset. Adding a new ability = create asset, no code changes.

### 6.4 Animation Architecture

The `ChickenAnimator` component is purely local — it reads `[Networked]` state and drives the Unity `Animator` accordingly. Never synced over network.

Facing logic:
- Two base orientations: left, right.
- Horizontal mirror for left/right.
- Vertical mirror for up/down variants.
- Snap on direction change — no blend.
- Idle timer tracked locally; triggers idle state after 2 seconds stationary.

### 6.5 Food Pile Visual System

`FoodPileVisuals` is a local MonoBehaviour on each pile that observes the `[Networked]` food amount and drives visuals:

```csharp
public class FoodPileVisuals : MonoBehaviour
{
    // Driven by networked food percentage (0-1)
    void UpdateVisuals(float foodPercent)
    {
        transform.localScale = Vector3.Lerp(emptyScale, fullScale, foodPercent);
        pileMaterial.color = Color.Lerp(depletedColor, fullColor, foodPercent);
    }
}
```

No network traffic — purely reactive to already-synced state.

### 6.6 ScriptableObject Asset Conventions

All SO class names use the `SO` suffix. All SO assets live under `/Assets/_Game/Data/`:

```
/Assets/_Game/Data/
├── Classes/
│   ├── FattyChickenSO.asset
│   ├── SpeedyChickenSO.asset
│   ├── WarriorChickenSO.asset
│   └── AssassinChickenSO.asset
├── Abilities/
│   ├── SpeedBurstSO.asset
│   ├── EggShellSO.asset
│   ├── RollTrampleSO.asset
│   ├── DoppelgangerSO.asset
│   ├── InvisibilitySO.asset
│   ├── SpineCoatSO.asset
│   ├── TurtleModeSO.asset
│   └── SneakyStealSO.asset
└── MatchConfigSO.asset
```

`MatchConfigSO` holds all tunable match values: timer, food targets, pile sizes, tick rate, UGS enabled flag.

#### Planned: centralized asset registries (post-demo refactor)

Today, prefab references and per-class colors are scattered: `MatchBootstrapper._chickenPrefab`, `ChickenCargo._foodPickupPrefab`, ad-hoc `Color` fields on individual visual components, etc. This works but hunting for "where is the X prefab assigned" gets tedious as systems grow. After the demo we consolidate into two registry SOs:

- **`PrefabRegistrySO`** — single asset under `/Assets/_Game/Data/` mapping logical IDs (`Chicken`, `FoodPile`, `FoodPickup`, `PlayerBase`, …) to `NetworkObject` references. Spawning systems take the registry via DI and look up by ID instead of holding a SerializeField slot per prefab. One source of truth, easy to inspect, easy to reassign in bulk.
- **`ColorSchemeSO`** — single asset holding palettes for player identity (Red / Blue / Green / Yellow), food states (full → empty), team accents, ability VFX. Visuals components read from this rather than serializing local color fields. Lets us re-skin the entire game from one inspector.

Both registries are deferred — they're refactor work, not new features. Track in Phase 9 polish or post-demo.

---

## 7. Input Architecture

```csharp
public interface IInputProvider
{
    Vector2 GetMovement();
    bool GetAttackHeld();
    bool GetAbility1Pressed();
    bool GetAbility2Pressed();
}
```

Implementations:

```
IInputProvider
├── KeyboardInputProvider   ← WASD + keyboard shortcuts (PC dev) — shipped Phase 1
└── MobileInputProvider     ← virtual joystick + touch buttons — Phase 8
```

Platform detected at startup; correct provider injected via Zenject. `PlayerNetworkInput` struct fed into Fusion is always identical regardless of source.

---

## 8. Audio Architecture

```csharp
public interface IAudioService
{
    void PlaySFX(AudioClip clip, Vector3 position);
    void PlayMusic(AudioClip clip, bool loop = true);
    void StopMusic();
    void SetSFXVolume(float volume);
    void SetMusicVolume(float volume);
}
```

Implementations:

```
IAudioService
├── UnityAudioService    ← Unity Audio (demo + initial release)
└── NullAudioService     ← silent no-op for testing
```

**Audio is always local.** Sounds are triggered by each client based on observed state changes — never synced over the network. No voice chat, no chat system.

FMOD can be added post-launch by implementing `FMODAudioService` and rebinding in Zenject.

---

## 9. Platform Strategy

### 9.1 Build Targets

| Target | Priority | Notes |
|---|---|---|
| Windows | Primary | Dev, testing, LAN host |
| Android | High | Main mobile target, also valid LAN host for demo |
| iOS | Medium | Post-demo — requires Apple dev account |

Any device (Windows or Android) can act as LAN host in Shared Mode. Host selection is a UI choice at session start, not a platform restriction.

### 9.2 Performance Targets

| Platform | Target FPS | Notes |
|---|---|---|
| Windows | 60 fps | No concern |
| Mid-range Android (2021+) | 30 fps stable | Design target |
| Low-end Android | Best effort | Not a demo requirement |

Tick rate (30/s) matches Android target. Raise if device benchmarks allow.

### 9.3 Camera

- **Orthographic isometric** — no perspective distortion, consistent visual language, simpler UI anchoring.
- Fixed camera — no player-following, no zoom. Entire map visible at all times.
- Camera angle and ortho size defined in `MatchConfigSO`.

---

## 10. Project Structure

```
/Assets
├── _Game/
│   ├── Scripts/
│   │   ├── Networking/         ← INetworkService, FusionNetworkService, PlayerNetworkInput
│   │   ├── Gameplay/           ← ChickenController, ChickenMovement, ChickenClass(es), MatchBootstrapper
│   │   ├── Abilities/          ← AbilityBaseSO + concrete ability SOs (Phase 6)
│   │   ├── Input/              ← IInputProvider + KeyboardInputProvider (MobileInputProvider Phase 8)
│   │   ├── Audio/              ← IAudioService + Null/Unity implementations
│   │   ├── Logging/            ← LogLevel, ILogService, UnityLogService
│   │   ├── Services/           ← IUGSService, NullUGSService, ISessionSelectionService
│   │   ├── Visuals/            ← ChickenAnimator, ChickenVisuals (FoodPileVisuals Phase 4)
│   │   ├── Installers/         ← ProjectInstaller, GameInstaller
│   │   ├── Bootstrap/          ← SceneLoader (Bootstrap → Game transition)
│   │   └── UI/                 ← CharacterSelectController (HUD, menus later)
│   ├── Data/                   ← All ScriptableObject assets (Classes/, ChickenClassRegistry, MatchConfig)
│   ├── Resources/              ← ProjectContext.prefab (Zenject auto-loads from here)
│   ├── Prefabs/                ← Chicken.prefab, SpawnPoint.prefab
│   ├── Scenes/                 ← Bootstrap.unity (idx 0), Game.unity (idx 1)
│   └── Art/                    ← Animations/, URP renderer data
├── Photon/Fusion/              ← Fusion 2 SDK
└── Plugins/Zenject/            ← Extenject (OptionalExtras stripped)
```

---

## 11. Risk Register

| Risk | Severity | Mitigation |
|---|---|---|
| Fusion 2 performance on low-end Android | High | Benchmark early on real device, tune tick rate |
| Desync / rubber-banding on poor mobile connections | High | Validate in LAN phase before going online |
| Zenject learning curve slowing early development | Medium | Invest time upfront on installer setup; payoff is clean architecture |
| UGS Relay latency on mobile | Medium | Benchmark Relay vs direct LAN delta before committing post-funding |
| iOS build complexity | Medium | Defer to post-demo |
| Android hosting stability (battery, heat) | Medium | Test sustained hosting sessions on target devices |
| ScriptableObject data not loading correctly in builds | Low | Add build validation step early |

---

## 12. Demo Milestone Definition

The demo is complete when:

- ✅ Any device (PC or Android) can host a LAN session
- ✅ 4 players can connect and play a full match
- ✅ All 4 classes playable with at least 2 abilities each
- ✅ Full match loop: spawn → collect → fight → win condition → end screen
- ✅ Windows and Android builds stable at target FPS
- ✅ Session join by ID — no UGS dependency
- ✅ `NullUGSService` injected by default in demo build via `UGS_DISABLED` scripting define
