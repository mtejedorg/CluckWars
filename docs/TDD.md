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

All major services are bound in a `GameInstaller` MonoInstaller on the main scene:

```csharp
public class GameInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        Container.Bind<INetworkService>().To<FusionNetworkService>().AsSingle();
        Container.Bind<IUGSService>().To<UGSService>().AsSingle();
        Container.Bind<IAudioService>().To<UnityAudioService>().AsSingle();
        Container.Bind<IInputProvider>().To<MobileInputProvider>().AsSingle();
    }
}
```

Swapping an implementation post-funding = change one `To<>()` binding. Zero other code changes.

### 3.2 Null / Demo Bindings

For demo builds or local dev, a `DemoInstaller` overrides specific bindings:

```csharp
public class DemoInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        Container.Bind<IUGSService>().To<NullUGSService>().AsSingle();
    }
}
```

This is the "mute UGS" mechanism — swap the binding, nothing else changes.

### 3.3 Runtime vs Compile-Time Switching

| Method | How | When to use |
|---|---|---|
| Runtime flag | `DemoInstaller` swaps bindings at startup via a config flag in `MatchConfigSO` | Dev and testing |
| Compile-time | Scripting define `UGS_DISABLED` forces `NullUGSService` regardless of config | Clean demo/production builds |

Both are supported simultaneously. Compile-time override takes priority.

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
    void StartHost(string sessionId);
    void StartClient(string sessionId);
    void StartDedicatedServer(string sessionId);
    void Shutdown();

    void SendInput(IGameInput input);
    event Action<PlayerRef, IGameInput> OnInputReceived;
    event Action<PlayerRef> OnPlayerJoined;
    event Action<PlayerRef> OnPlayerLeft;
}
```

Concrete implementations:

```
INetworkService
├── FusionNetworkService       ← demo + post-funding
└── LocalNetworkService        ← offline/unit testing, no Fusion dependency
```

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
public struct PlayerInput : INetworkInput
{
    public Vector2 Movement;
    public NetworkBool AttackHeld;
    public NetworkBool Ability1Pressed;
    public NetworkBool Ability2Pressed; // Assassin only, ignored otherwise
}
```

Game logic reads exclusively from the Fusion input buffer — never from `Input.GetKey` directly.

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
├── MobileInputProvider     ← virtual joystick + touch buttons
└── KeyboardInputProvider   ← WASD + keyboard shortcuts (PC dev)
```

Platform detected at startup; correct provider injected via Zenject. `PlayerInput` struct fed into Fusion is always identical regardless of source.

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
│   │   ├── Networking/         ← INetworkService, FusionNetworkService, LocalNetworkService
│   │   ├── Gameplay/           ← GameManager, all systems, ChickenController
│   │   ├── Abilities/          ← AbilityBaseSO + concrete ability SOs
│   │   ├── Input/              ← IInputProvider + implementations
│   │   ├── Audio/              ← IAudioService + implementations
│   │   ├── Services/           ← IUGSService, UGSService, NullUGSService
│   │   ├── Visuals/            ← ChickenAnimator, FoodPileVisuals, ChickenVisuals
│   │   ├── Installers/         ← Zenject GameInstaller, DemoInstaller
│   │   └── UI/                 ← HUD, menus, character select
│   ├── Data/                   ← All ScriptableObject assets
│   ├── Prefabs/
│   ├── Scenes/
│   └── Art/
│       └── URP/                ← URP Renderer Data, render features, shader graphs
├── Plugins/                    ← Photon Fusion 2 SDK, Zenject
└── Tests/
    ├── Unit/                   ← Game logic tests using LocalNetworkService
    └── Integration/            ← Scene-level tests
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
