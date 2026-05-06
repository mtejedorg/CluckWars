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
- **Zenject for all DI** — bindings live in `GameInstaller`; demo/null overrides in `DemoInstaller`.
- **URP materials only** — no Built-in RP shaders. Use Lit, Unlit, or custom URP Shader Graph.
- **Animation is local** — `ChickenAnimator` reads `[Networked]` state; nothing animation-related is synced over the network.
- **VFX are local** — triggered by observed networked state changes, never sent over network.

---

## Key Interfaces

```csharp
INetworkService   // All Fusion calls go through here
IInputProvider    // GetMovement(), GetAttackHeld(), GetAbility1/2Pressed()
IAudioService     // PlaySFX(), PlayMusic(), Stop/Volume
IUGSService       // Auth, Lobby, Relay — NullUGSService in demo
```

---

## Zenject Binding Pattern

Two contexts:

- **`ProjectInstaller`** — lives on `Assets/_Game/Resources/ProjectContext.prefab`. Binds app-wide singletons (`IInputProvider`, `IAudioService`, `IUGSService`). Survives scene loads.
- **`GameInstaller`** — lives on the `SceneContext` GameObject in `Game.unity`. Binds match-scoped state (`MatchConfigSO`, `INetworkService`).

```csharp
// ProjectInstaller (Resources/ProjectContext.prefab)
Container.Bind<IUGSService>().To<NullUGSService>().AsSingle();          // demo
Container.Bind<IAudioService>().To<NullAudioService>().AsSingle();      // demo
Container.Bind<IInputProvider>().To<KeyboardInputProvider>().AsSingle();

// GameInstaller (Game.unity SceneContext)
Container.Bind<MatchConfigSO>().FromInstance(_matchConfig).AsSingle();
Container.Bind<INetworkService>().To<FusionNetworkService>()
    .FromNewComponentOnNewGameObject().AsSingle().NonLazy();
```

Single-player dev sessions go through `FusionNetworkService.StartSoloAsync()` (`GameMode.Single`). There is no separate `LocalNetworkService` — `NetworkBehaviour` requires a `NetworkRunner`, so even the offline path is a Fusion runner with one player.

Scripting define `UGS_DISABLED` forces `NullUGSService` in any build regardless of config.

---

## Chicken Architecture

```
ChickenController (NetworkBehaviour)
├── [Networked] ChickenClass   Replicated; spawner sets via OnBeforeSpawned
├── ChickenStatsSO              Resolved at Spawned() from ChickenClassRegistrySO
├── ChickenMovement             Reads Fusion input, moves with client-side prediction
├── ChickenCombat               Attack detection + damage application       (Phase 3)
├── ChickenCargo                Collection, carrying, deposit               (Phase 4)
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
  public struct PlayerInput : INetworkInput
  {
      public Vector2 Movement;
      public NetworkBool AttackHeld;
      public NetworkBool Ability1Pressed;
      public NetworkBool Ability2Pressed; // Assassin only
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

**Phase 2 complete:** All four classes (Warrior / Speedy / Fatty / Assassin) selectable from the Bootstrap scene via 1-4 + Space. Spawned chicken adopts class-specific stats and tint via `ChickenClassRegistrySO`. `ChickenAnimator` drives the locomotion blend locally on every peer. `ISessionSelectionService` carries the choice from menu to match.
**Next:** Phase 3 — combat. `ChickenCombat`, attack input, damage / HP, hit reactions, death stun.

---

## Team

**Maestro** — Senior Engineer, telco background, Unity/C#/Android expertise, Madrid.
