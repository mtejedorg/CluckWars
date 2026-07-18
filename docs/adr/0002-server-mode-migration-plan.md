# ADR 0002 — Server Mode Migration Plan

- **Status:** Proposed
- **Date:** 2026-07-18
- **Deciders:** Maestro, AI Coding Assistant
- **Context tags:** networking, server-mode, architectural-refactor

## Context

Cluck Wars currently uses **Photon Fusion 2 in Shared Mode**, where clients have state authority over their own player objects (chickens) and the "Master Client" (the peer who hosted or first joined the room) handles spawning and managing world objects (bases, food piles). 

As a post-funding migration milestone, the project is scheduled to transition to **Server Mode** (dedicated server or host-authoritative server). In Server Mode, only the server has state authority over all network objects, client-side spawning is prohibited, and clients must request actions through RPCs or network inputs.

To prepare for this transition, this ADR logs the critical rework items, logic changes, and code anchors in the current codebase that will require refactoring.

## Decisions & Proposed Rework Items

### 1. Client-Side Spawn Sites (H4)
In Server Mode, clients cannot call `Runner.Spawn`. The following spawn sites must be refactored to be server-driven or initiated via a client-to-server request:

*   **Player Chicken Spawning:** 
    *   **File/Line Anchor:** [MatchBootstrapper.cs:346](file:///C:/Users/MARCO/Documents/GitHub/CluckWars/Assets/_Game/Scripts/Gameplay/MatchBootstrapper.cs#L346)
    *   **Issue:** Currently, `HandlePlayerJoined` runs on every client, and each client spawns its own chicken.
    *   **Rework:** Spawning must be centralized on the server. The server must handle the join callback, read the player's chosen loadout (using a loadout-RPC handshake because the server does not have access to the client-local `SessionSelectionService`), call `Runner.Spawn`, and pair the spawned chicken using `Runner.SetPlayerObject(player, chickenObject)`.
*   **Death Loot Spawning:**
    *   **File/Line Anchor:** [ChickenCargo.cs:325](file:///C:/Users/MARCO/Documents/GitHub/CluckWars/Assets/_Game/Scripts/Gameplay/ChickenCargo.cs#L325) (specifically within `HandleDeath`)
    *   **Issue:** The state authority of the dying chicken (which is currently the local client in Shared Mode) spawns the `FoodPickup` bounty loot.
    *   **Rework:** Centralized on the server when processing player death.
*   **Ability-Spawned Objects:**
    *   **File/Line Anchors:**
        *   Doppelganger Decoy: [DoppelgangerAbilitySO.cs:44](file:///C:/Users/MARCO/Documents/GitHub/CluckWars/Assets/_Game/Scripts/Abilities/DoppelgangerAbilitySO.cs#L44)
        *   Feather Trap Zone: [FeatherTrapAbilitySO.cs:61](file:///C:/Users/MARCO/Documents/GitHub/CluckWars/Assets/_Game/Scripts/Abilities/FeatherTrapAbilitySO.cs#L61)
        *   Root Egg Zone: [RootEggAbilitySO.cs:52](file:///C:/Users/MARCO/Documents/GitHub/CluckWars/Assets/_Game/Scripts/Abilities/RootEggAbilitySO.cs#L52)
    *   **Issue:** Caster code currently calls `Runner.Spawn` directly from the activating client.
    *   **Rework:** These must either be spawned by the server in response to a network input packet, or requested via client-to-server RPCs.

### 2. IsSharedModeMasterClient Refactoring (H5)
*   **File/Line Anchors:** 
    *   [FusionNetworkService.cs:97](file:///C:/Users/MARCO/Documents/GitHub/CluckWars/Assets/_Game/Scripts/Networking/FusionNetworkService.cs#L97) (and [line 196](file:///C:/Users/MARCO/Documents/GitHub/CluckWars/Assets/_Game/Scripts/Networking/FusionNetworkService.cs#L196))
    *   [MapGenerator.cs:375](file:///C:/Users/MARCO/Documents/GitHub/CluckWars/Assets/_Game/Scripts/Gameplay/MapGenerator.cs#L375)
    *   [MatchBootstrapper.cs:168](file:///C:/Users/MARCO/Documents/GitHub/CluckWars/Assets/_Game/Scripts/Gameplay/MatchBootstrapper.cs#L168) (and [line 546](file:///C:/Users/MARCO/Documents/GitHub/CluckWars/Assets/_Game/Scripts/Gameplay/MatchBootstrapper.cs#L546))
*   **Issue:** The codebase currently couples gameplay logic directly to Photon Fusion's shared-mode-specific property `IsSharedModeMasterClient` to check who should run master-side tasks.
*   **Rework:** Introduce an `IAuthorityPeer` or `IsServer` abstraction property on the `INetworkService` interface. This decouples the gameplay files from direct Fusion namespace coupling and allows a seamless switch between Shared/Server mode topologies.

### 3. Match Start UI Invocation
*   **File/Line Anchor:** [GameManager.cs:181](file:///C:/Users/MARCO/Documents/GitHub/CluckWars/Assets/_Game/Scripts/Gameplay/GameManager.cs#L181) (within `StartMatchNow`)
*   **Issue:** `StartMatchNow()` is a plain method called directly by the local host client's UI to transition the match state from `Lobby` to `Active`.
*   **Rework:** In Server Mode, the host is just another client. The local UI click must trigger a client-to-server RPC (e.g. `RPC_RequestStartMatch()`) requesting the server to call `StartMatchNow()`.

### 4. Prediction and Resimulation Hazards
*   **File/Line Anchor:** [ChickenMovement.cs:33](file:///C:/Users/MARCO/Documents/GitHub/CluckWars/Assets/_Game/Scripts/Gameplay/ChickenMovement.cs#L33) (`_verticalVelocity`)
*   **Issue:** `_verticalVelocity` is currently a private local float. During resimulation (rewinding and replaying ticks on client prediction), this value will not be rolled back correctly, leading to prediction jitter on vertical movement (jumping/falling).
*   **Rework:** `_verticalVelocity` must be decorated with `[Networked]` or wrapped inside a predicted struct so Fusion's rollback system can restore its correct tick state.
*   **Additional non-rollback fields:** `ChickenController._abilitySlowUntil`, `_rootUntil`, and `_activeSlowSources` are plain fields ticked in `FixedUpdateNetwork`. Fine under single-authority Shared Mode; under client prediction resimulation they drift (slow/root timers re-integrated per resim pass). Same decision applies: `[Networked]` them or run the local chicken without prediction.
*   **Input Latches:** `_pendingAbility` latches and inputs in [FusionNetworkService.cs](file:///C:/Users/MARCO/Documents/GitHub/CluckWars/Assets/_Game/Scripts/Networking/FusionNetworkService.cs) must not clear or mutate state unsafely during predictions without a proper `[Networked]` wrapper to prevent resimulation double-triggers.

### 5. RPC Receiver-Side Validation (H5 — trust surface)

The demo posture is trust-the-client; in Server Mode these RPCs become the cheat API unless the receiver validates. Two of them are total-compromise primitives today, so cheap validation is worth adding even before the port if the demo goes public:

| RPC | Validation to add |
|---|---|
| `PlayerBase.RPC_AddFood` | caller's chicken within `DepositRadius` + margin of this base; base owned by caller |
| `ChickenController.RPC_TeleportTo` | only accepted from match-flow authority (GameManager), never from arbitrary peers |
| `ChickenCombat.RPC_ApplyDamage` | amount ≤ max ability damage in the registry; attacker in range; rate-limited per attacker |
| `ChickenCargo.RPC_DrainStolen` | thief in steal range; amount ≤ steal cap |
| `ChickenCargo.RPC_ResetForNewMatch` | only from match-flow authority |
| `ChickenMatchStats.RPC_CreditKill` | victim actually died this tick; attacker was the recorded damage source |

## Consequences

- Direct C# references to `IsSharedModeMasterClient` will be banned in gameplay scripts.
- The `INetworkService` layer will be expanded to define authority checks.
- Zenject dependencies will be reviewed to ensure client-only services (like UI and `SessionSelectionService`) are not called in server-side spawn paths.
