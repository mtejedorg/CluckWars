# Architecture

System map for Cluck Wars. Complements `TDD.md` (higher-level design rationale)
with the concrete current shape of the code.

---

## Scenes

```
Bootstrap.unity  ──► Game.unity
└─ menu / mode select       └─ map + chickens + match
```

- **Bootstrap.unity**: `CharacterSelectController` builds the menu UGUI procedurally on Awake. No `SceneContext`; relies on self-injection from `ProjectContext`. User picks class (1-4) + mode (S=Solo / H=Host / J=Join) + SPACE.
- **Game.unity**: hosts `MatchBootstrapper`, `MapGenerator`, `Hud` parent (with `MatchHud` + `TouchControlsHud` + `DebugHud` children), `SceneContext` (binds `MatchConfigSO`, `INetworkService`, `FusionNetworkService` on a fresh GameObject).

---

## Dependency Injection (Zenject)

Two container scopes:

### `ProjectInstaller` (app-wide)
Lives on `Assets/_Game/Resources/ProjectContext.prefab`. Survives scene loads.

```
ILogService              UnityLogService(_logMinLevel)
IUGSService              NullUGSService
IAudioService            UnityAudioService(transform)        ← lives under ProjectContext, survives scene loads
IInputProvider           CompositeInputProvider(KB + Touch)
ISessionSelectionService SessionSelectionService             ← carries class/mode/sessionName across scenes
ChickenClassRegistrySO   FromInstance(serialized)            ← per-class stats + tint
AudioRegistrySO          FromInstance OR empty runtime SO    ← Combat/Cargo/Ability/Match/Music clips
PrefabRegistrySO         FromInstance OR empty runtime SO    ← Chicken/Doppelganger/FoodPile/FoodPickup/PlayerBase/GameManager prefabs
ColorSchemeSO            FromInstance OR empty runtime SO    ← HUD palette + food states + cooldown dim
```

### `GameInstaller` (scene-scope, Game.unity)
Lives on the `SceneContext` GameObject.

```
MatchConfigSO   FromInstance(serialized)                     ← match duration, food target, tick rate, MaxPlayers, intro seconds
INetworkService FusionNetworkService.FromNewComponentOnNewGameObject().NonLazy()
```

### Self-injection

`Bootstrap.unity` has no `SceneContext`, and Fusion spawns `NetworkBehaviour`s outside Zenject's normal injection path. Both paths use this pattern in `Awake` / `Spawned`:

```csharp
if (_log == null)           // _log is set by [Inject] Construct; null = not yet injected
{
    ProjectContext.Instance.Container.Inject(this);
}
```

**Don't** gate on `ProjectContext.HasInstance` — that returns false until something accesses `.Instance` for the first time. Reading `.Instance` directly triggers the lazy load.

---

## Networking model (Fusion 2 Shared Mode)

```
                  Photon Cloud (matchmaking + relay)
                  ▲                                ▲
                  │                                │
            ┌─────┴─────┐                    ┌─────┴─────┐
            │  Peer A   │ ◄── snapshots ───► │  Peer B   │
            │ (master)  │                    │  (joiner) │
            │ StateAuth │                    │ StateAuth │
            │ over A's  │                    │ over B's  │
            │ chicken + │                    │ chicken   │
            │ all scene │                    │           │
            │ NetObjs   │                    │           │
            └───────────┘                    └───────────┘
```

- **Each chicken**'s StateAuthority = the player who owns it. They run movement/combat/cargo/abilities.
- **Scene NetworkObjects** (FoodPile, PlayerBase, GameManager) — master client owns. Spawned by master via `Runner.Spawn` (`MapGenerator`, `MatchBootstrapper`).
- **Cross-authority writes** go via RPC. Pattern: `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]`. Examples: `ChickenCombat.RPC_ApplyDamage(amount, attackerRef)`, `FoodPile.RPC_Drain(amount)`, `PlayerBase.RPC_AddFood(amount)`, `ChickenCargo.RPC_DrainStolen(amount)`.
- **Prediction** for the local player is automatic in Fusion 2: input feeds the simulation each tick on the owner, server snapshots reconcile silently.
- **Demo is "trust the client"** by design — Shared Mode means each peer is authority over their own state. Real anti-cheat needs Server Mode (Phase 11+). Code is forward-compatible: every state write is gated on `HasStateAuthority`, every cross-write is an RPC, so the shape is identical in Server Mode.

`PhotonAppSettings.AppIdFusion = 259bda28-…` (already wired).

---

## Chicken architecture

The chicken `NetworkObject` carries every chicken-side system as siblings.
Lookups go through `ChickenController` (cached in `Spawned`).

```
ChickenController (NetworkBehaviour)
├── [Networked] Class                  Replicated; spawner sets via onBeforeSpawned
├── [Networked] VisualOpacity          0..1, replicated, drives Invisibility ability fade
├── (local) MoveSpeedMultiplier        Ability scalar — read by ChickenMovement
├── (local) MovementLocked             Egg Shell / Turtle — zeros planar input
├── (local) DamageImmune               Egg Shell — short-circuits RPC_ApplyDamage
├── (local) DamageResistance           Turtle — % damage absorbed
├── (local) ReflectDamage              Spine Coat — bounces damage to attacker
├── (local) IsDecoy                    Doppelganger flag — gates input on all 3 FUNs
│
├── ChickenStatsSO                     Resolved at Spawned from ChickenClassRegistrySO
├── ChickenMovement                    Pure C# helper; CharacterController.Move
├── ChickenCombat                      [Networked] HP / IsStunned / StunTimer / AttackTimer / AttackEpoch
├── ChickenCargo                       [Networked] Cargo; collect/deposit/death-drop/steal
├── AbilityController                  [Networked] ActiveSlot / ActivationTimer / Cooldown0 / Cooldown1
├── ChickenAnimator                    Local; Speed/Attack/Hit/Stunned animator params
├── ChickenVisuals                     Local; MaterialPropertyBlock tint + alpha
└── ChickenNameplate                   Local; TextMesh "P1 Warrior" billboarded
```

### Input gating (all three FUNs)

Movement, combat, and ability input all early-return when:

1. `!HasStateAuthority` (proxies don't simulate)
2. `IsDecoy` (Doppelganger decoys don't read the caster's input)
3. `GameManager.Instance == null || !GameManager.IsMatchRunning` (lobby / intro / end / restart)
4. (Combat / Movement only) `_combat.IsStunned`

---

## Match lifecycle

```
WaitingForPlayers ──host clicks Start──► Active (Intro 3s) ──countdown─► Active (playable)
                                                                                │
                                                                                ▼
                                                                              Ended (6s restart countdown)
                                                                                │
                                                              ┌────────reset────┘
                                                              ▼
                                                  Active (Intro 3s) (next round)
```

Drivers:

- `GameManager.Spawned`: master sets `WaitingForPlayers` (Solo auto-starts).
- `GameManager.StartMatchNow()`: master-only entry, arms `IntroTimer` + `MatchTimer = duration + intro`.
- `IsMatchRunning` = `State == Active && !IsIntroActive`. The canonical "gameplay allowed" flag.
- Win check (`EvaluateWinCondition`): runs at 4 Hz on master, skips during intro, scans `PlayerBase`s for `FoodTotal >= FoodTargetToWin`.
- `EndOnTimerExpiry`: picks highest-total owned base when `MatchTimer` runs out.
- `RestartMatch`: master zeros bases, refills piles to `MaxAmount`, despawns pickups, RPCs each chicken's `RPC_ResetForNewMatch` (combat + cargo) + `RPC_TeleportTo(corner)`, transitions back to `Active` with a fresh intro.

---

## Base ↔ corner ↔ player pairing

Stable, deterministic, agreed by every peer:

```
MapGenerator._corners[i]                              raw (±d, 0, ±d)
MapGenerator._spawnPoints[i] = Lerp(_corners[i], 0, 0.15)   ← where chickens spawn
MapGenerator.SpawnBases:                                    ← spawns base at _spawnPoints[i]
    base.CornerIndex = i  via onBeforeSpawned

MatchBootstrapper.PickSpawnCorner(player):
    preferred = sorted-roster index of player                ← plain join order in a fresh session
    corner    = first ShuffledCorner(preferred + offset)     ← scan forward past corners already
                not stamped on any live chicken                stamped on a HomeCornerIndex
    stamp chicken.HomeCornerIndex = corner  via onBeforeSpawned

GameManager.AssignBasesToPlayers:
    pick base whose CornerIndex == chicken.HomeCornerIndex   ← exact identity, no fallbacks
```

The occupied-corner scan makes rejoins safe: roster slots shift when someone
leaves, but stamped `HomeCornerIndex` values don't — a rejoiner takes the free
corner instead of colliding with a live player. Restart teleports, deposits,
leaderboard rows, and nameplates all key off the same stamped corner.

---

## Procedural map (`MapGenerator`)

- **Local on every peer (`Awake`)**: ground plane primitive + 4 invisible boundary walls (height 5m, thickness 0.5m) + cached spawn points.
- **Master client only (after `INetworkService.OnRunnerReady`)**: spawns 4 `PlayerBase`s at the spawn points (each tagged with `CornerIndex`), 1 center `FoodPile` (larger `Amount`, 1.5× visual scale), N small piles on a jittered ring around the center.
- `_baseCornerDistance < 1` auto-snaps to 12 with a warning — guards against the "all corners collapse to origin" footgun.

---

## Camera

`MatchCamera` (on Main Camera):

- Orthographic, 45° yaw + 30° pitch (canonical iso).
- `_followLocalChicken = true` (default): finds `ChickenController` with `HasInputAuthority`, smooth-damps `_focusPoint` toward chicken position with `_followSmoothTime` (default 0.15s).
- Falls back to `_focusPoint` (map center) when no local chicken — intro, post-despawn, between matches.
- `_orthoSize = 8` for close framing. Bump to ~18 for the original ART.md §2 "full map" view.

---

## Ability system

```csharp
public abstract class AbilityBaseSO : ScriptableObject {
    public string DisplayName;
    public string ShortLabel;
    public Color AccentColor;             // tints the TouchControlsHud button
    public float Duration;
    public float Cooldown;
    public AnimationClip AbilityAnimationClip;   // (Phase 9 polish hook, unused today)
    public abstract void OnActivate(AbilityContext ctx);
    public abstract void OnDeactivate(AbilityContext ctx);
}
```

Adding a new ability = subclass + drop a `.asset` under `/Assets/_Game/Data/Abilities/`. No code changes to `AbilityController`.

### Concrete abilities (all 8 shipped)

| Ability | Hook used | Accent |
|---|---|---|
| Speed Burst | `MoveSpeedMultiplier` | cyan |
| Egg Shell | `MovementLocked` + `DamageImmune` | pale white |
| Roll & Trample | one-shot `OverlapSphere` + `RPC_ApplyDamage` | orange |
| Doppelganger | `Runner.Spawn` decoy NetworkObject | purple |
| Invisibility | `VisualOpacity` (Networked) | teal |
| Spine Coat | `ReflectDamage` flag | olive |
| Turtle Mode | `MoveSpeedMultiplier` + `DamageResistance` | brown |
| Sneaky Steal | one-shot `RPC_DrainStolen` on victim cargo | magenta |

---

## Input

```
KeyboardInputProvider ─┐
                        ├─► CompositeInputProvider ─► OnInput ─► PlayerNetworkInput { Movement, Buttons } ─► chicken FUNs
TouchInputProvider ────┘
   ▲
   └─ reads TouchControlsHud.Instance (joystick + 3 buttons)
```

- Composite ORs both sources: clicking buttons with the mouse in the editor exercises the touch flow without a build.
- Buttons: `Attack` (bit 0), `Ability1` (bit 1), `Ability2` (bit 2). See `InputButton` enum.

---

## Assets — locations

```
/Assets/_Game/
├── Data/
│   ├── Classes/               ChickenStatsSO assets (Warrior / Speedy / Fatty / Assassin)
│   ├── Abilities/             AbilityBaseSO concrete .asset files (all 8 authored)
│   ├── AudioRegistry.asset
│   ├── PrefabRegistry.asset
│   ├── ColorScheme.asset
│   └── MatchConfig.asset
├── Prefabs/
│   ├── Chicken.prefab         (+ Doppelganger.prefab variant)
│   ├── FoodPile.prefab
│   ├── FoodPickup.prefab
│   ├── PlayerBase.prefab
│   ├── GameManager.prefab
│   └── SpawnPoint.prefab      (legacy, unused since MapGenerator)
├── Resources/
│   └── ProjectContext.prefab  ← Zenject auto-loads via Resources lookup
├── Scenes/
│   ├── Bootstrap.unity
│   └── Game.unity
└── Scripts/
    ├── Abilities/             AbilityBaseSO + 8 concrete SOs + AbilityContext
    ├── Audio/                 IAudioService + UnityAudioService + NullAudioService + AudioRegistrySO
    ├── Bootstrap/             SceneLoader
    ├── Editor/                CluckWarsBuildMenu (editor-only)
    ├── Gameplay/              Chicken*, FoodPile, FoodPickup, PlayerBase, GameManager, MapGenerator, MatchBootstrapper, registries
    ├── Input/                 IInputProvider + impls + VirtualJoystick + HoldButton + TouchControlsHud
    ├── Installers/            ProjectInstaller + GameInstaller
    ├── Logging/               LogLevel + ILogService + UnityLogService
    ├── Networking/            INetworkService + FusionNetworkService + PlayerNetworkInput
    ├── Services/              IUGSService + NullUGSService + ISessionSelectionService + SessionSelectionService
    ├── UI/                    CharacterSelectController + MatchHud + CargoHud (legacy) + DebugHud
    └── Visuals/               ChickenAnimator + ChickenVisuals + FoodPileVisuals + MatchCamera + ChickenNameplate
```
