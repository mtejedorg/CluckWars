---
status: live
owner: Maestro
type: file-structure
---
# File structure

What lives where in the Cluck Wars repository, which assembly owns it, and where a new thing goes.

Generated from commit `b7c2616e` (branch `simplify-abilities`, 3798 tracked files). Hand-written sections describe that
commit; the blocks between `BEGIN GENERATED` / `END GENERATED` markers are rewritten by
`tools/docs/file_structure.py` from the working tree (section 9). This file is the map; the rules live in
`docs/ARCHITECTURE_RULES.md` (layering, networking, injection) and `docs/CODE_RULES.md` (code-level rules), and the
older footgun list in `docs/CONVENTIONS.md`.

---

## 1. Orientation

Cluck Wars is a 4-player free-for-all arena game (collect food, bank it at your base, steal from rivals) on Unity 6000.3,
URP, Photon Fusion 2 Shared Mode, Zenject and UI Toolkit. Everything that is ours sits in `Assets/_Game` (code, data,
scenes, prefabs, art, audio), `Assets/UI` (UXML/USS) and a few URP/UI settings folders under `Assets`.

Runtime code is five assemblies, one `.asmdef` each plus `.asmref` files that attach other folders to it:

```
CluckWars.App            Installers (composition root), FusionNetworkService, UGSService, RemoteConfigBalanceSource
   ^
CluckWars.Diagnostics    probes, auto-test driver, Ability Lab
   ^
CluckWars.Presentation   Visuals, UI, Input
   ^
CluckWars.Gameplay       simulation: chickens, abilities, match, rules, audio + network interfaces
   ^
CluckWars.Core           logging, text keys, SelfInject, service interfaces, scene loader, Planar
```

An upper assembly may reference lower ones, never the reverse. Editor tools (`Assets/_Game/Scripts/Editor`,
`Assets/_Game/Editor`) and every test (`Assets/_Game/Scripts/Editor/Tests`) compile into Unity's built-in
`Assembly-CSharp-Editor`, which sees all five because they are `autoReferenced`. Nothing of ours compiles into
`Assembly-CSharp`.

---

## 2. Assemblies

<!-- BEGIN GENERATED: assemblies -->
| Assembly | Defined in (`Scripts/`) | Folders attached by `.asmref` | References | Woven by Fusion | `.cs` files |
|---|---|---|---|---|---|
| `CluckWars.Core` | `Logging` | `Bootstrap`, `Diagnostics/Shared`, `Geometry`, `Injection`, `Localization`, `Services` | Zenject; prebuilt: Zenject-usage.dll | no | 28 |
| `CluckWars.Gameplay` | `Gameplay` | `Abilities`, `Audio`, `Balance`, `Networking`, `Settings` | CluckWars.Core, Fusion.Unity, Zenject, Unity.AI.Navigation | yes | 208 |
| `CluckWars.Presentation` | `Visuals` | `Input`, `UI` | CluckWars.Core, CluckWars.Gameplay, Fusion.Unity, Zenject, Unity.InputSystem, Unity.RenderPipelines.Core.Runtime, Unity.RenderPipelines.Universal.Runtime, UnityEngine.UI | yes | 150 |
| `CluckWars.Diagnostics` | `Diagnostics` | `AbilityLab` | CluckWars.Core, CluckWars.Gameplay, CluckWars.Presentation, Fusion.Unity, Zenject, Unity.InputSystem | yes | 32 |
| `CluckWars.App` | `App` | `Installers` | CluckWars.Core, CluckWars.Gameplay, CluckWars.Presentation, CluckWars.Diagnostics, Fusion.Unity, Zenject, Unity.Services.Core, Unity.Services.Authentication, Unity.Services.Multiplayer, Unity.Services.RemoteConfig | yes | 5 |
| `Assembly-CSharp-Editor` | built-in (any `Editor` folder) | - | all `autoReferenced` assemblies | no | 327 |
| `Assembly-CSharp` | built-in (everything else) | - | all `autoReferenced` assemblies | yes | 0 |

`.cs` counts cover `Assets/_Game` only. `AssembliesToWeave` in `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`: `Fusion.Unity`, `Assembly-CSharp`, `Assembly-CSharp-firstpass`, `Fusion.Addons.Physics`, `Fusion.Addons.FSM`, `CluckWars.Gameplay`, `CluckWars.Presentation`, `CluckWars.Diagnostics`, `CluckWars.App`.
<!-- END GENERATED: assemblies -->

Facts a reader trips over:

- **Core is not woven and uses `overrideReferences`.** `CluckWars.Core` references Zenject only, so it cannot hold a
  NetworkBehaviour or `[Networked]` state, and Fusion's weaver has nothing to do there (it is absent from
  `AssembliesToWeave`). With `overrideReferences: true` an assembly stops seeing every prebuilt DLL automatically;
  Core lists one, `Zenject-usage.dll`, the DLL that declares Zenject's `[Inject]` (used by `SceneLoader`). The comment
  in `Assets/_Game/Scripts/Editor/Tests/AssemblyLayoutTests.cs`: "Core sees no prebuilt DLL except the one that
  declares Zenject's [Inject]: in particular no Fusion." The other four keep `overrideReferences: false`.
- **`Scripts/Diagnostics/Shared` is Core.** It sits inside the Diagnostics folder and keeps the namespace
  `CluckWars.Diagnostics`, but its own `Assets/_Game/Scripts/Diagnostics/Shared/CluckWars.Core.asmref` puts it in
  Core, so every layer can read the dev-tools gate (`DevTools`), `BuildInfo` and `DiagnosticsState`.
  `AssemblyLayoutTests.FolderAssembly` resolves the longest matching folder, so `Diagnostics/Shared` beats
  `Diagnostics`.
- **`Scripts/Geometry` is a Core folder** (`Assets/_Game/Scripts/Geometry/CluckWars.Core.asmref`), holding
  `Planar`, the ground-plane (XZ) maths. The Core row of the table in `docs/CONVENTIONS.md` "Assemblies" does not
  list it; `AssemblyLayoutTests.FolderAssembly` does.
- **The weave list** is `AssembliesToWeave` in `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion` (not in
  `ProjectSettings/`). Its contents are printed under the table above: the four Fusion-referencing CluckWars
  assemblies, Fusion's own `Fusion.Unity`, the built-in `Assembly-CSharp` / `Assembly-CSharp-firstpass`, and two
  Fusion add-on names (`Fusion.Addons.Physics`, `Fusion.Addons.FSM`) that have no `.asmdef` in this repository.
  `Assets/_Game/Scripts/Editor/Tests/WeavingGuardTests.cs` and `AssemblyLayoutTests.cs` pin it.
- **Package references** (`Unity.InputSystem`, `Unity.AI.Navigation`, the URP runtimes, `UnityEngine.UI`, UGS) are
  written as `GUID:` entries in the `.asmdef` files; the table prints their names.
- **Third-party assemblies** in the repository: `Fusion.Unity`, `Fusion.Unity.Editor`, `Fusion.CodeGen` and
  `PhotonWebSocket` under `Assets/Photon`; `Zenject` and `Zenject-Editor` under `Assets/Plugins/Zenject`.

Per-folder `.cs` counts (every folder under `Assets/_Game/Scripts` that holds a `.cs` file directly):

<!-- BEGIN GENERATED: script-folders -->
| Folder (`Assets/_Game/Scripts/`) | Assembly | `.cs` files directly in it |
|---|---|---|
| `Abilities` | `CluckWars.Gameplay` | 45 |
| `Abilities/Passives` | `CluckWars.Gameplay` | 9 |
| `AbilityLab` | `CluckWars.Diagnostics` | 5 |
| `App` | `CluckWars.App` | 3 |
| `Audio` | `CluckWars.Gameplay` | 9 |
| `Balance` | `CluckWars.Gameplay` | 39 |
| `Bootstrap` | `CluckWars.Core` | 1 |
| `Diagnostics` | `CluckWars.Diagnostics` | 27 |
| `Diagnostics/Shared` | `CluckWars.Core` | 9 |
| `Editor` | `Assembly-CSharp-Editor` | 18 |
| `Editor/Tests` | `Assembly-CSharp-Editor` | 301 |
| `Gameplay` | `CluckWars.Gameplay` | 97 |
| `Gameplay/Session` | `CluckWars.Gameplay` | 2 |
| `Geometry` | `CluckWars.Core` | 1 |
| `Injection` | `CluckWars.Core` | 1 |
| `Input` | `CluckWars.Presentation` | 15 |
| `Installers` | `CluckWars.App` | 2 |
| `Localization` | `CluckWars.Core` | 2 |
| `Logging` | `CluckWars.Core` | 4 |
| `Networking` | `CluckWars.Gameplay` | 5 |
| `Services` | `CluckWars.Core` | 10 |
| `Settings` | `CluckWars.Gameplay` | 2 |
| `UI` | `CluckWars.Presentation` | 53 |
| `Visuals` | `CluckWars.Presentation` | 82 |
<!-- END GENERATED: script-folders -->

---

## 3. `Assets/_Game` folder map

Non-script file counts by folder (depth 3):

<!-- BEGIN GENERATED: game-assets -->
| Folder (`Assets/_Game/`, depth 3; deeper files counted in their depth-3 folder) | Files by extension |
|---|---|
| `Art` | 2 `.png` |
| `Art/Animations` | 1 `.controller` |
| `Art/Animations/Generated` | 36 `.anim` |
| `Art/Animations/Placeholders` | 14 `.anim` |
| `Art/AppIcon` | 5 `.png` |
| `Art/Billboards` | 8 `.png` |
| `Art/Materials` | 46 `.mat` |
| `Art/Materials/Hazed` | 37 `.mat`, 2 `.png` |
| `Art/Meshes` | 12 `.asset` |
| `Art/Meshes/Fx` | 1 `.fbx` |
| `Art/Shaders` | 7 `.shader`, 3 `.hlsl` |
| `Art/Textures` | 2 `.png` |
| `Art/UI/Atoms` | 16 `.png` |
| `Art/UI/Backgrounds` | 7 `.png` |
| `Art/UI/Chickens` | 8 `.png` |
| `Art/UI/Frames` | 9 `.png`, 1 `.md` |
| `Art/UI/Fx` | 15 `.png`, 1 `.spriteatlas` |
| `Art/UI/Icons` | 59 `.png` |
| `Audio/Abilities` | 2 `.wav`, 1 `.md` |
| `Audio/UI` | 17 `.wav`, 1 `.md` |
| `Data` | 10 `.asset` |
| `Data/Abilities` | 30 `.asset` |
| `Data/Abilities/Passives` | 8 `.asset` |
| `Data/Balance` | 2 `.asset` |
| `Data/Classes` | 4 `.asset` |
| `Data/Simulator` | 1 `.asset` |
| `Editor` | 8 `.cs` |
| `Materials` | 1 `.mat` |
| `Prefabs` | 6 `.prefab` |
| `Prefabs/Abilities` | 1 `.prefab` |
| `Prefabs/Props` | 18 `.prefab` |
| `Resources` | 1 `.prefab` |
| `Resources/Dev` | 3 `.uxml`, 2 `.uss` |
| `Resources/Fonts` | 4 `.ttf` |
| `Resources/Text` | 1 `.csv` |
| `Scenes` | 4 `.unity` |
<!-- END GENERATED: game-assets -->

### 3.1 `Assets/_Game/Scripts` (purpose from the files' own headers)

| Folder | Assembly | What lives there |
|---|---|---|
| `Scripts/Logging` | Core (asmdef) | `ILogService`, `UnityLogService`, `LogLevel`, `LogLineFormat` (the one line format and its parser) |
| `Scripts/Localization` | Core | `UiKeys` (compile-time handles for every key in `Assets/_Game/Resources/Text/UiText.csv`) and `UiText` (loads the CSV) |
| `Scripts/Injection` | Core | `SelfInject`, the one way a component Zenject did not build gets its dependencies |
| `Scripts/Services` | Core | `IUGSService` / `NullUGSService` / `LobbyInfo`, `IBalanceSource` / `NullBalanceSource` (live-ops balance fetch), haptics (`IHapticsService`, `HapticsService`, `AndroidHapticDevice`, `HapticRules`), `FrameRatePolicy` |
| `Scripts/Bootstrap` | Core | `SceneLoader` (in `Bootstrap.unity`; loads the next scene) |
| `Scripts/Geometry` | Core | `Planar` |
| `Scripts/Diagnostics/Shared` | Core | `BuildInfo`, `DevTools`, `DevSeed` (the `cw_seed` seed for solo runs), `IDevBalanceEditor` (the interface the menu sees), `DiagnosticsState`, `DebugHudMode`, `DepositDiagnostics`, `CornerTapDetector`, `PerKeyRateLimiter` |
| `Scripts/Gameplay` (+ `Session/`) | Gameplay (asmdef) | The simulation; sub-areas below |
| `Scripts/Abilities` (+ `Passives/`) | Gameplay | Ability ScriptableObjects and their shared vocabulary (below) |
| `Scripts/Networking` | Gameplay | `INetworkService` (all Fusion runner lifecycle), `PlayerNetworkInput` (the per-tick input struct; its aim byte is always 0 since 2026-11-20), `AimRules` (aim decode, the soft-lock pick; no device aims any more), `DisconnectRules`, `NetworkProtocol` (the one `Version` integer that is part of the Photon app version; bumped by hand with every networked layout or RPC signature change, ARCH-NET-21) |
| `Scripts/Audio` | Gameplay | `IAudioService` with `UnityAudioService` / `NullAudioService`, `AudioTicker`, `SfxId`, `AudioRegistrySO`, `UiAudioSO`, `ProceduralAudioBank`, `MissingSfxReporter` |
| `Scripts/Balance` | Gameplay | The Balance Oracle, which reports and proposes and never blocks (decision 2026-11-20): `OracleMapSO` (the shipped arena baked as data, built by `Editor/OracleMapBaker`), `OraclePaths` (wall-aware shortest paths for the bake), `OracleKit` and `OracleKits` (assets resolved to kits, through `BalanceBook.Preview` copies for a proposal), `FarmSim` (fixed-tick farming run on the baked map), `OracleRunner` (economy per kit, Spoiler and Thief tables), `SneakyTables`, `OracleReport`, `OracleReferenceSO` (the accepted reference the drift guard compares to), `OracleSweep` (food sweep per pile setup), `OracleSolver` (proposes free values as a balance delta), `FoodRules` (the starting pile food rules); the match simulator (CODE-DESIGN-01): `MatchSim` (seeded four-chicken match on the baked map), `MatchSight` (the bot sight rule the simulator shares), `MatchCombat` (casts, contact, shoves and the timer tie-break), `MatchStats` (per-seat counters), `MatchPolicy` (the `IMatchPolicy` a seat decides with), `FarmOnlyPolicy` (the farming policy), `ScriptedPolicy` (P0, the policy that calls the game's own bot functions), `UtilityPolicy` (P1, a scored policy), `MatchRunner` (runs designs and populations over seeds), `MatchReport` (the match report and its sensitivity tables), `MatchWhatIf` (the what-if report: named schema-1 deltas run against the baseline with the same seeds, read-only; a delta may carry `"roster"`, a swap of a passive for an unregistered one), `OracleRoster` (that roster swap: lets `Champion` take a seat; BL-222), `MatchSweep` (the sensitivity sweep and robustness tags), `MatchSolverTerm` (the guarded match term of the solver), `OracleNav` (wall-aware steering on the baked map), `MatchRng` (seeded streams), `MatchKit` and `MatchKits` (kits with their declared ability facts, read through `BalanceBook.Preview` copies); the live-ops balance layer: `BalanceBook` (holds and applies a server-published delta), `BalanceJson` (strict reader), `BalanceRefresher` (boot cache + background fetch), `BalanceCache`, `BalanceSourceChoice`; the dev-tools-only local path (ARCH-DATA-14): `BalanceBook.Local.cs` (partial, local apply entry points), `BalanceLocalOverride` (apply window, stored/applied state), `BalanceLocalStore` (dev-only local file), `BalanceSctCheck` (the oracle's before and after numbers for a local apply, with the `OracleMapSO` the book was handed; reports, never blocks) |
| `Scripts/Settings` | Gameplay | `PlayerPreferences` (persisted display options, the `KeyboardScheme` choice and the dev-tools gate), `LastSetup` (last committed menu choices) |
| `Scripts/Visuals` | Presentation (asmdef) | Local-only presentation: chicken visuals/VFX/animator/nameplates/badges, telegraphs and range overlays, `StatusEffects` table, `FeedbackTuning`, `FeedbackPaletteSO`, `MatchCamera`, fence fade, surround haze (`CWHazeFeature`, `SurroundHazeMapSO`), Death Mark screen pass (`MarkScreenFeature`), rival indicators and the floor arrows (`RivalIndicator`, `FloorRivalArrows`, `FloorArrowRules`, `MarkedTargetArrowRules`), the Death Mark / Hunt hunter feedback (`MarkFeedback`, `HuntBrackets`, `HuntRules`), the Thief loot mark feedback (`LootFeedback`, `LootPresentationRules`, `LootHexRules`), `HudControlZones`, per-beat `*BeatRules`, `OwnSoundRules`, `ChickenModelPresenter` |
| `Scripts/UI` | Presentation | UI Toolkit controllers (`MenuUiController` and its partials, `MatchOverlaysController` and its partials, `MatchHudController`, `MarkHudController` and its partial `MarkHudController.Loot`), `CoinBagGlyph` (the loot bag drawn by code), `UiElements`, ability icon/palette mapping, pure layout/flow rules (`MatchStandings`, `WaitingRoomRules`, `SafeAreaPadding`, `OrientationPolicy`), `DebugHud`, and `MatchHud` (a UGUI canvas built in code for the hit flash and the comeback banner) |
| `Scripts/Input` | Presentation | `IInputProvider` and its providers (`KeyboardInputProvider`, `GamepadInputProvider`, `TouchInputProvider`, `CompositeInputProvider`); `TouchControlsController` and its `.Hud` partial; pure input and hex rules (`JoystickRules`, `HoldCancelRules`, `PeckLongPressRules` (tap pecks, long press flips Auto-Peck), `HudLayoutRules` (slot roles, per-device strip arrangement, hex tilt), `CasterStunCountdownRules`, `EggPhaseHexRules`, `HuntHexRules`, `PeckCastHexRules` (the Peck hex fill over the cast)) |
| `Scripts/Diagnostics` | Diagnostics (asmdef) | `AutoTestDriver` (+ `.Aura`, `.Mark` partials), `AutoTestArgs`, `AutoTestConfig`, `AutoPlayBrain`, `AutoPlayInputProvider`, `FighterTactics` (pure: the fighter auto-play personality), `SimClock` (also holds `TickCadence`; the simulation-time clock of the auto-play and the probes), `InvariantProbe` / `RoundInvariants`, `PerfProbe` / `PerfRules`, `VisualProbe` / `VisualScanRules`, `UiLayoutProbe` / `UiLayoutRules` / `UiLayoutAllowList`, `SyncHeartbeat` / `SyncHeartbeatFormat`, `SessionStamp`, `DevBalanceEditor` (dev-only UI Toolkit local balance editor, implements `IDevBalanceEditor`; bound in `InstallDiagnostics`), `DevBuildStamp` (dev-only UI Toolkit overlay, version + build number bottom-right; bound in `ProjectInstaller.InstallDiagnostics`), `MenuShotsDriver`, `DebugHudSpawner`, `RevealBreadcrumbs`, `DiagnosticsObjects` (keeps a probe alive across scene loads) |
| `Scripts/AbilityLab` | Diagnostics | The Ability Lab dev scene: `AbilityLabBootstrapper`, `AbilityLabPanel`, `AbilityLabHud`, `AbilityLabDummy`, `AbilityLabLoadout` |
| `Scripts/App` | App (asmdef) | `FusionNetworkService` (Fusion implementation of `INetworkService`), `UGSService` (live UGS auth + lobby, lobby `bal` hash check), `RemoteConfigBalanceSource` (the only Remote Config user) |
| `Scripts/Installers` | App | `ProjectInstaller` (app-wide bindings, on `Assets/_Game/Resources/ProjectContext.prefab`), `GameInstaller` (scene bindings, on the SceneContext of `Game.unity` and `AbilityLab.unity`) |
| `Scripts/Editor` | Assembly-CSharp-Editor | Build menu (`CluckWarsBuildMenu`, `BuildGuards`, `BuildInfoWriter`), import stampers (`UiAudioImportSettings`, `UiSpriteImportSettings`, `TmpEssentialsAutoImport`, `AndroidVibratePermission`), animation authoring (`ChickenAnimationSetup`, whose menu `Cluck Wars/Animation/Wire Peck Cast` runs `EnsurePeckCastWiring` on `Chicken.controller`; `CastArchetypeClipGenerator`, `CastArchetypePreview`), capture probes (`ParticleProbe`, `PeckEdgeProbe`), `AbilityLabMenu`, `AutoTestEditorHooks`, `BalanceEditorHooks` (Remote Config In Editor toggle, restores the assets on reload / Play Mode exit), `AbilityIconStyleChecker` (no header; on load it logs any registered ability without an `AbilityIconStyle` mapping), Oracle tooling (`OracleMapBaker` bakes `Game.unity` into `OracleMap.asset`, `OracleMenu` writes the oracle report to `Captures/oracle/`; its `MatchTimingBatch` and `BotArrivalRadius` serve the match simulator, and its `Match report` and `Match sensitivity` entries (batch methods `MatchReportBatch` and `MatchSensitivityBatch`) write the match report and the sensitivity run, its `Match what-if` entry (menu `Cluck Wars/Balance/Oracle/Match what-if`, batch method `MatchWhatIfBatch`) writes the what-if report, and its `Accept match as balanced` entry (batch method `AcceptMatchBatch`) records the match report in `OracleReference.asset`) |
| `Scripts/Editor/Tests` | Assembly-CSharp-Editor | The EditMode suite (section 6) |

**`Scripts/Gameplay` sub-areas** (all 97 files):

| Sub-area | Files |
|---|---|
| Match lifecycle and flow | `GameManager` (match state machine, timer, win check; spawned from `Assets/_Game/Prefabs/GameManager.prefab` by `MatchBootstrapper`), `MatchState`, `MatchBootstrapper` (match entry point in `Game.unity`), `MatchSignals`, `MatchFlowRules`, `MatchBackRules`, `MatchPresentationRules`, `MatchSummaryLines`, `MatchEventKind`, `ComebackRules`, `RoundTeleportRules`, `RoundStartCooldownRules`, `LocalJoinClock`, `ArenaReadiness`, `ArenaWarmup`, `ISessionSelectionService` and `SessionSelectionService` (both in `Gameplay/Session/`) |
| Match telemetry | `MatchTelemetry` (pure formatting of the `steal`, `peckcut`, `kill` and `contact` lines under the `Telemetry` source tag; BL-208) |
| Spawn corners and player identity | `CornerAssignment`, `CornerSlotRules`, `SpawnPlacement`, `PlayerColors` |
| The chicken | `ChickenController` (motion, control windows, RPC entry points, `Local` / `LocalDriven`), `ChickenCargo`, `ChickenCombat` (execute removal/respawn; hosts `SenderLatency` / `PeerLatency`), `ChickenMovement`, `ChickenTraversal`, `ChickenCapsule`, `ChickenMatchStats`, `ChickenLocalEvents`, `EventEdge`, `ListenerList`, `ControlState`, `EffectWindow`, `ContactHysteresis`, `RecentPositions`, `LocalChickenRules`, `IChickenModelPresenter`, `Doppelganger`, `SilentCargoWrite` |
| Chicken classes | `ChickenClass` (the enum), `ChickenClassInfo` (holds `ChickenClasses`), `ChickenStatsSO`, `ChickenClassRegistrySO` |
| Ability runtime | `AbilityController` (slots, cooldowns, `LoadoutCodes`), `AbilityRegistrySO`, `AbilityActivationRules`, `AbilityAimForward`, `AbilityRunRules`, `AbilityEffectStack`, `AbilityCue`, `AbilityZone`, `AreaEffectRules`, `ZoneDiscMesh`, `FixedKit`, `PreEquippedLoadout`, `LoadoutClaimRules`, `PeckChainRules`, `PeckCastRules` (the Peck cast: when it ends naturally, when a stun or shove cuts it; pure), `TargetCooldowns`, `JumpResolver`, `LootMark` (the Thief's loot mark rules, shared with the match simulator) |
| Warrior identity rules (BL-222, shipped off) | `HitStealRules` (the Bully's steal on a Peck hit), `PeckBreakRules` (the Champion's break of a cast), `TrophyRules` (trophies, overflow); pure, shared by the game's passives and the simulator |
| Death Mark (Assassin execute) | `AssassinExecute`, `ExecuteRules`, `ExecuteRespawnRules`, `MarkPickRules` |
| Thief loot mark | `ThiefLoot` (the `NetworkBehaviour` on the chicken: networked mark, state, age timer and the `LootTakenEventId` one-shot; runs only for a Thief), `MarkedStealBook` (the Thief's ledger of the one open marked steal; pure) |
| Food, bases and transfers | `FoodPile`, `PlayerBase`, `FoodTransferRules` (holds `FoodRequests`), `PendingStealLedger`, `StealMath`, `StealRules`, `BaseDepositRules`, `BaseKeepOutRules`, `BaseWallRules`, `PileFootprintShape` |
| RPC claims and control | `RpcClaim` (holds `RpcRejectionLimiter`), `ControlClaimRules`, `ControlReapplyRules` |
| Map and terrain | `MapGenerator`, `PinwheelLayout`, `SectorScatter`, `TerrainObstacle`, `TerrainTraversal`, `ArenaDensityMetric` |
| Bots | `BotController` (solo-mode bot AI on the state authority), `BotTactics`, `BotLootRules` (pure rules for the Thief bot's loot mark: pursue, hold Steal, bank now; the game's bot and the simulator's P0 call the same functions), `BotHitStealRules` and `BotBreakRules` (pure rules for the Bully's hit-steal and the Champion's break; called by the simulator's policies, not yet by `BotController`; BL-222), `BotSightRules` (pure rule for what a bot knows of a rival: live while visible, last seen position while hidden; ARCH-DATA-15) |
| Shared config assets | `MatchConfigSO`, `PrefabRegistrySO`, `ColorSchemeSO` |

**`Scripts/Abilities`** (45 files + 9 in `Passives/`): one `<Name>AbilitySO` per ability (30 concrete classes), the
abstract bases `AbilityBaseSO`, `PassiveAbilitySO` and `StunBurstAbilitySO` (shared base of `AmbushAbilitySO` and
`WingSlamAbilitySO`, which is why it has no asset), and the shared vocabulary: `AbilityContext`, `AbilityAim`,
`AbilityTargeting`, `AbilityJob`, `AccentKey`, `CastArchetype`, `ChickenSubclass`, `AreaEffectKind`,
`AreaEffectChannel`, `AreaEffectSpec`, `AreaEffectSpawner`, `AbilityOracleFacts` (the numbers the oracle reads off an ability, declared by the ability). `Passives/` holds the 9 `<Name>PassiveSO` classes: the 8 shipped ones and `ChampionPassiveSO` (unregistered, simulator only, BL-222).

### 3.2 `Assets/_Game/Data`

ScriptableObject assets only; the full class-to-asset table is section 5. Subfolders: `Data` (10 project-wide
singletons: registries, `MatchConfig`, palettes, haze/fence manifests), `Data/Abilities` (30 ability assets),
`Data/Abilities/Passives` (8 specializations, which also carry the fixed kits), `Data/Classes` (4 `ChickenStatsSO`, one
per class), `Data/Simulator` (assets only the match simulator uses: `Champion.asset`, the Warrior's second simulator kit; kept outside `Abilities/` because the roster tests treat every asset in that folder as shipped; never registered, never in a scene; BL-222).

- **Retired abilities:** 19 of the 30 ability assets have `Retired: 1` (the `Retired` field on `AbilityBaseSO`: "kept in
  the repo, out of the registry, every kit and the free pool"). Exactly those 19 are absent from
  `Assets/_Game/Data/AbilityRegistry.asset`; no other asset, prefab or scene in `Assets/_Game` references them. The 11
  active abilities and the 8 passives are the 19 entries of the registry.
- **Name differs from class:** `Assets/_Game/Data/Abilities/DiveBomb.asset` holds `RollTrampleAbilitySO` (retired).
- **Classes with no asset:** only the abstract bases `AbilityBaseSO`, `PassiveAbilitySO` and `StunBurstAbilitySO`.
  Every concrete ScriptableObject class under `Assets/_Game/Scripts` has at least one asset.

### 3.3 `Assets/_Game/Scenes`

Build order from `ProjectSettings/EditorBuildSettings.asset` (all enabled): 0 `Bootstrap.unity`, 1 `Game.unity`,
2 `Map.unity`, 3 `AbilityLab.unity`.

| Scene | Holds (project scripts placed in it) | Loaded by |
|---|---|---|
| `Bootstrap.unity` | `SceneLoader`, `MenuUiController` (main menu, Pick Bird, Gear Up, the Coop) | entry scene (build index 0); `MatchOverlaysController` and `AbilityLabPanel` return to it |
| `Game.unity` | SceneContext with `GameInstaller`, `MatchBootstrapper`, `MapGenerator`, `MatchCamera`, `MatchHud`, `MatchHudController`, `MarkHudController`, `MatchOverlaysController`, `TouchControlsController` | `SceneLoader` |
| `Map.unity` | Baked arena geometry and dressing, no project scripts (baked by `Assets/_Game/Editor/MapSceneBaker.cs`) | `MapGenerator`, additively (`_bakedMapSceneName = "Map"`) |
| `AbilityLab.unity` | `AbilityLabBootstrapper`, `AbilityLabPanel`, `AbilityLabHud`, `GameInstaller`, `MatchCamera`, `TouchControlsController`, `DebugHud` | `MenuUiController.Sheets.cs` (Developer Mode), or the Editor menu `AbilityLabMenu` |

`Assets/Scenes/SampleScene.unity` is a Unity template leftover that is not in the build list.

### 3.4 `Prefabs`, `Art`, `Audio`, `Materials`

- `Assets/_Game/Prefabs`: the spawned network objects `Chicken.prefab`, `Doppelganger.prefab` (decoy variant),
  `FoodPile.prefab`, `GameManager.prefab`, `PlayerBase.prefab`, `SpawnPoint.prefab`; `Prefabs/Abilities/AbilityZone.prefab`
  (placed ability areas); `Prefabs/Props/` 18 `Prop_*.prefab` arena props. `Assets/_Game/Data/PrefabRegistry.asset`
  (`PrefabRegistrySO`) references six of them: `Chicken`, `Doppelganger`, `FoodPile`, `GameManager`, `PlayerBase` and
  `AbilityZone` (not `SpawnPoint`, not the props).
- `Assets/_Game/Art`: `Animations` (`Chicken.controller`; per-class clips in `Generated/` written by
  `CastArchetypeClipGenerator` and `Placeholders/`), `Materials` (URP materials; `Hazed/` = material copies baked by
  `SurroundHazeBaker`), `Meshes` (generated mesh assets; `Fx/`), `Shaders` (`.shader` + `.hlsl`), `Textures`,
  `Billboards`, `AppIcon`, `UI` (`Icons/` incl. `Icons/Abilities/Icon_<Class>.png`, `Atoms/`, `Backgrounds/`,
  `Chickens/`, `Frames/` with a `MANIFEST.md`, `Fx/` with the sprite atlas). Two grass textures sit at the `Art` root.
- `Assets/_Game/Audio`: `UI/` (17 menu clips) and `Abilities/` (2 clips), each with a `MANIFEST.md`; both manifests
  state the clips are procedurally synthesised. The menu generators live in `tools/audio/menu`; `Assets/_Game/Audio/UI/Source~`
  is git-ignored (`.gitignore:42`, pattern `*~`) and Unity does not import `~` folders.
- `Assets/_Game/Materials`: one material, `GrassFloor.mat`.

### 3.5 `Resources` (what is loaded by name, and by whom)

| File | Loaded by |
|---|---|
| `Assets/_Game/Resources/ProjectContext.prefab` | Zenject, automatically on the first container resolve (per the `ProjectInstaller.cs` header); carries `ProjectInstaller` |
| `Assets/_Game/Resources/Text/UiText.csv` | `UiText` (`Resources.Load<TextAsset>("Text/UiText")`) |
| `Assets/_Game/Resources/Fonts/*.ttf` (4) | `UiGfx` (`Resources.Load<Font>`, e.g. `Fonts/LilitaOne-Regular`) |
| `Assets/_Game/Resources/BuildInfo.json` (generated, git-ignored at `.gitignore:181`) | `BuildInfo`; written by `BuildInfoWriter` before a player build |
| `Assets/_Game/Resources/Dev/DevBuildStamp.uxml` + `.uss` (dev tools only) | `DevBuildStamp` (`Resources.Load<VisualTreeAsset>("Dev/DevBuildStamp")`; the `.uss` is attached from the UXML) |
| `Assets/_Game/Resources/Dev/DevBalanceEditor.uxml` + `.uss`, `DevBalanceRow.uxml` (dev tools only) | `DevBalanceEditor` (loaded by name from `Dev/`; the row template is cloned per field) |
| `Assets/Resources/PanelSettings.asset` | Every scene `UIDocument` references it by GUID (`m_PanelSettings`): 1 in `Bootstrap.unity`, 4 in `Game.unity`, 2 in `AbilityLab.unity`, none in `Map.unity`. It is also loaded by name at runtime by `DevBuildStamp` (`Resources.Load<PanelSettings>("PanelSettings")`, for its code-created document) and by a test (`Phase8ChunkDRivalArrowTests.PanelSize_MatchesThePanelSettingsAsset`). It is ScaleWithScreenSize, reference 1920x1080, match 0.5 (`HudControlZones` mirrors those numbers). |
| `Assets/Resources/UnityPlayerAccountSettings.asset` | Unity services settings asset; no project code references it (purpose beyond that not established) |

### 3.6 UI Toolkit assets (`Assets/UI`, outside `_Game`)

9 `.uxml` at `Assets/UI` and 11 `.uss` in `Assets/UI/Styles`. Each UXML is assigned to a `UIDocument` in a scene (GUID
reference); each USS is attached from a UXML `<Style src=...>` line. All UIDocuments share
`Assets/Resources/PanelSettings.asset`. The one exception to "UXML at `Assets/UI`" is the dev-tools build stamp:
`Assets/_Game/Resources/Dev/DevBuildStamp.uxml` + `.uss` (section 3.5), loaded by name and built into a `UIDocument`
by code in `DevBuildStamp`, not assigned in a scene.

| UXML | Scene | Driven by | Styles |
|---|---|---|---|
| `MainMenu.uxml` | Bootstrap (2 references) | `MenuUiController` | `CluckWarsTheme.uss`, `CategoryMark.uss` |
| `CharacterSelect.uxml`, `CharacterSelectClass.uxml` | Bootstrap | `MenuUiController` | `CluckWarsTheme.uss` |
| `Lobby.uxml` | Bootstrap | `MenuUiController` | `CluckWarsTheme.uss`, `GetReadyCard.uss`, `YouMark.uss` |
| `MatchTopBar.uxml` | Game | `MatchHudController` | `CluckWarsTokens.uss`, `MatchTopBar.uss`, `YouMark.uss` |
| `MatchOverlays.uxml` | Game | `MatchOverlaysController` | `CluckWarsTokens.uss`, `MatchOverlays.uss`, `GetReadyCard.uss`, `ArenaCover.uss`, `YouMark.uss`, `CluckWarsTheme.uss`, `CategoryMark.uss` |
| `MarkHud.uxml` | Game | `MarkHudController` | `CluckWarsTokens.uss`, `MarkHud.uss` |
| `TouchControls.uxml` | Game, AbilityLab | `TouchControlsController` | `CluckWarsTokens.uss`, `TouchControls.uss`, `CategoryMark.uss` |
| `AbilityLab.uxml` | AbilityLab | `AbilityLabPanel` | `CluckWarsTokens.uss`, `AbilityLab.uss` |

`Assets/UI/Styles/CluckWarsTokens.uss` is generated from the `Design/cluckwars-tokens*.jsx` files by
`tools/generate-uss-tokens.ps1`. `Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss` is Unity's default
runtime theme.

### 3.7 `Assets/_Game/Editor` and the other `Assets` folders

- `Assets/_Game/Editor` (8 `.cs`, `Assembly-CSharp-Editor`): map tooling for `Map.unity` (`MapSceneBaker`,
  `MapPropsRescaler`, `ZoneMarkerBaker`, `GroundDressingBaker`, `CoverBoxDresser`, `FenceInnerMeshBuilder`,
  `SurroundHazeBaker`) and the `BalanceEditorWindow` (Cluck Wars / Balance / Balance Editor).
- `Assets/Settings`: URP assets: `PC_RPAsset` / `PC_Renderer` (renderer feature `CWHazeFeature`), `Mobile_RPAsset` /
  `Mobile_Renderer` (renderer feature `MarkScreenFeature`), `CW_MatchProfile`, `DefaultVolumeProfile`,
  `UniversalRenderPipelineGlobalSettings`. `Assets/DefaultVolumeProfile.asset` and
  `Assets/UniversalRenderPipelineGlobalSettings.asset` also sit at the `Assets` root.
- `Assets/Generated`: art-pipeline output (prop and chicken `.fbx` at its root, `Env/`, `PropTextures/`, `Textures/`).
- `Assets/AIConcepts`: 35 concept PNGs.
- Third-party, by name only: `Assets/Photon` (Fusion, PhotonLibs), `Assets/Plugins` (Zenject, NuGet).
  `Assets/TextMesh Pro` and `Assets/_Recovery` exist on disk but are git-ignored.

---

## 4. Key files by role

All under `Assets/_Game/Scripts/` unless the path says otherwise. One line each, from the file's own header.

**Composition roots and bootstrap**
- `Installers/ProjectInstaller.cs`: app-wide Zenject bindings (logging, UGS, audio, registries, `MatchConfigSO`, the baked `OracleMapSO` handed to `BalanceBook`, the chicken presenter); lives on `Assets/_Game/Resources/ProjectContext.prefab`.
- `Installers/GameInstaller.cs`: scene bindings (`INetworkService` and the rest) on the SceneContext of `Game.unity`; inherits the project bindings.
- `Bootstrap/SceneLoader.cs`: in `Bootstrap.unity`; loads the next scene (`Game.unity` by default) unless the menu takes over.
- `Gameplay/MatchBootstrapper.cs`: entry point of a match in `Game.unity`; reads the session selection and spawns the match objects.

**Match flow**
- `Gameplay/GameManager.cs`: master-client-owned NetworkObject; match state machine (`MatchState`), timer, win check, round reset.
- `Gameplay/MatchState.cs`, `Gameplay/MatchSignals.cs` (local per-peer signals such as RoundReset), `Gameplay/MatchFlowRules.cs` (post-match and menu-entry decisions).

**The chicken**
- `Gameplay/ChickenController.cs`: the chicken NetworkBehaviour: motion from the Fusion input buffer, control windows, knockback, the stun/root/slow RPCs, `ChickenController.Local` / `LocalDriven`.
- `Gameplay/ChickenCargo.cs`: carried food, pile collection, deposit; food crosses authorities by request and settlement.
- `Gameplay/ChickenCombat.cs`: execute removal and respawn state machine; hosts `SenderLatency` and `PeerLatency`, the RTT reads.
- `Gameplay/ChickenMovement.cs`: pure-C# movement helper (input vector to `CharacterController.Move`).
- `Gameplay/AbilityController.cs`: the four ability slots, activation from the input buffer, per-slot cooldowns, replicated `LoadoutCodes`.

**Abilities base, registry and traits**
- `Abilities/AbilityBaseSO.cs`: base of every ability; traits `Job`, `GrantsStealProof`, `FollowUpPhaseSeconds`, `IconId`, the `Retired` flag.
- `Abilities/PassiveAbilitySO.cs`: base of the class specializations (passives) and their fixed `Kit`.
- `Gameplay/AbilityRegistrySO.cs`: the global ability pool, ability-to-byte code mapping (`CodeOf` / `FromCode`); asset `Assets/_Game/Data/AbilityRegistry.asset`.
- `Abilities/AbilityJob.cs` (the job an ability declares), `Abilities/AccentKey.cs` (its palette accent), `Abilities/AreaEffectSpawner.cs` (the one spawn of an `AbilityZone`), `Gameplay/FixedKit.cs` (class + specialization to four abilities).

**Networking services and rules**
- `Networking/INetworkService.cs` / `App/FusionNetworkService.cs`: Fusion runner lifecycle behind an interface.
- `Services/IUGSService.cs` / `Services/NullUGSService.cs` / `App/UGSService.cs`: lobby and auth.
- `Services/IBalanceSource.cs` / `Services/NullBalanceSource.cs` / `App/RemoteConfigBalanceSource.cs` and `Balance/BalanceBook.cs`: live-ops balance (UGS Remote Config key `balance`), applied before the first match, hash `bal=` in the Session line.
- `Gameplay/RpcClaim.cs`: the "who may send this" rule for `RpcSources.All` RPCs that name a network object; also `RpcRejectionLimiter`.
- `Gameplay/FoodTransferRules.cs`: conservation rule for peck, steal and deposit between authorities; its stateful half is `FoodRequests` (same file); `Gameplay/PendingStealLedger.cs` holds the open requests.
- `Gameplay/ControlClaimRules.cs` (which control effects a peer may send), `Gameplay/LoadoutClaimRules.cs` (receiver check of a replicated loadout), `Gameplay/StealRules.cs`, `Gameplay/BaseDepositRules.cs`.
- `Gameplay/ChickenCombat.cs`: `SenderLatency` (per-RPC sender latency) and `PeerLatency` live here.

**Events**
- `Gameplay/ChickenLocalEvents.cs`: replicated one-shots seen on every peer (`CastEvent` and siblings); ids and numbers only.
- `Gameplay/EventEdge.cs`: the cursor over one replicated one-shot counter.
- `Gameplay/ListenerList.cs`: subscribers of one local event, each called separately.

**Injection and lookup**
- `Injection/SelfInject.cs`: `SelfInject.Ensure`, the one self-injection path.
- `Gameplay/LocalChickenRules.cs`: which chicken this peer drives (applied by `ChickenController.Local`).
- Registries: `ChickenController.ActiveControllers` and its siblings; tests reach them through `Editor/Tests/RegistryTestAccess.cs`.
- `Gameplay/IChickenModelPresenter.cs` (declared in Gameplay) / `Visuals/ChickenModelPresenter.cs` + `Visuals/ChickenPresentationAccess.cs` (implemented in Presentation, bound in `ProjectInstaller`).

**Tables**
- `Gameplay/ChickenClassInfo.cs`: `ChickenClassInfo` and the static `ChickenClasses` (per-class flag, forage rule, wording keys, bot profile). The enum is `Gameplay/ChickenClass.cs`.
- `Visuals/StatusEffects.cs`: one table relating the same status across badge, kind, control state, zone and VFX enums.
- `Abilities/AbilityJob.cs`: the job table abilities declare.
- `Gameplay/EffectWindow.cs`: the one implementation of a timed effect window over a `[Networked] TickTimer`.
- `Gameplay/PlayerColors.cs`: the four player colours keyed by spawn corner.
- `Geometry/Planar.cs`: ground-plane maths.

**UI plumbing**
- `UI/UiElements.cs`: show/hide, ensure an EventSystem, query and bind named elements.
- `UI/MenuUiController.cs` with partials `UI/MenuUiController.Chrome.cs` (music, backdrop), `UI/MenuUiController.Coop.cs` (host/join, seats, join code), `UI/MenuUiController.GearUp.cs` (loadout step), `UI/MenuUiController.PickBird.cs` (class tiles), `UI/MenuUiController.Sheets.cs` (disconnect and settings sheets; Ability Lab launch), `UI/MenuUiController.Stage.cs` (live 3D chicken stage).
- `Input/TouchControlsController.cs` (input contract read by `TouchInputProvider`) with `Input/TouchControlsController.Hud.cs` (drawing of the hex cluster, stick and status strip).
- `UI/MatchOverlaysController.cs` with `UI/MatchOverlaysController.Intro.cs`, `UI/MatchOverlaysController.Lobby.cs`, `UI/MatchOverlaysController.MatchEnd.cs`.
- `UI/MatchHudController.cs` (top bar), `UI/MarkHudController.cs` (Death Mark HUD: the hunter's marker, anchored on the target's floor arrow outside the circle and over its head inside; `MarkHud.uxml` has a `MarkBody` wrapper, `MarkPaw`, `MarkStar`), `Visuals/HudControlZones.cs` (where HUD controls sit).
- `Visuals/HuntRules.cs` (pure hunter-state rules: blocker priority, count text, end cause, marker and bracket colour, badge, anchor, marker scale, metres chip text and colour), `Visuals/MarkedTargetArrowRules.cs` (pure: when the marked target keeps its floor arrow), `Visuals/HuntBrackets.cs` (hunter-only ground brackets, owned by `MarkFeedback`), `Input/HuntHexRules.cs` (pure: the Hunt hex during a hunt; its `HuntDot` / `HuntGlyph` / `HuntFar` elements are in `TouchControls.uxml`, one set per slot). Tests: `Editor/Tests/HuntRulesTests.cs`, `HuntHexRulesTests.cs`, `MarkMarkerGeometryTests.cs`, `MarkOnArrowTests.cs`, `HuntBracketsTests.cs`, `HapticRulesTests.cs`.
- Thief loot mark: `Gameplay/ThiefLoot.cs` (networked; the mark is decided on the Thief's own authority), `Gameplay/MarkedStealBook.cs` (the open marked steal, used by `ChickenCargo.RequestMarkedSteal` and `RPC_SettleMarkedSteal`), `Visuals/LootFeedback.cs` (the loot mark's local presentation, a listener on `ChickenLocalEvents.LootTaken`), `Visuals/LootPresentationRules.cs` (pure: who sees what, three roles: the Thief, the marked rival, a bystander), `Visuals/LootHexRules.cs` (pure: the Steal hex dot; emits `cw-hex--loot`), `UI/MarkHudController.Loot.cs` (the loot bag badge and arrow bag in `MarkHud.uxml`), `UI/CoinBagGlyph.cs`. Tests: `Editor/Tests/ThiefLootGameTests.cs`, `LootFeedbackTests.cs`.
- `Networking/NetworkProtocol.cs`: `NetworkProtocol.Version` and `AppVersion` (`"p" + Version`), handed to Photon by `App/FusionNetworkService.cs`; guarded by `Editor/Tests/NetworkProtocolTests.cs`.

**Audio**
- `Audio/SfxId.cs`: typed sound ids (`enum SfxId : byte`).
- `Audio/AudioRegistrySO.cs`: clip registry; `Resolve(SfxId)`; asset `Assets/_Game/Data/AudioRegistry.asset`.
- `Audio/IAudioService.cs` / `Audio/UnityAudioService.cs` / `Audio/NullAudioService.cs`.
- `Audio/MissingSfxReporter.cs`: a sound that cannot play is reported once per call site.
- `Visuals/OwnSoundRules.cs`: who hears a chicken's own cast and deposit sounds.

**Tuning**
- `Visuals/FeedbackTuning.cs`: every timing/magnitude/colour constant of the feedback system.
- `Gameplay/MatchConfigSO.cs`: match-wide tunables, including the three starting pile amounts (`PersonalPileFood`, `ContestedPileFood`, `CentrePileFood`; `MapGenerator` reads them by injection, the oracle measures them); asset `Assets/_Game/Data/MatchConfig.asset`.
- `Visuals/FeedbackPaletteSO.cs`: the feedback colours; asset `Assets/_Game/Data/FeedbackPalette.asset`.

**Diagnostics**
- `Diagnostics/InvariantProbe.cs`: logs match state transitions and checks round invariants on every peer.
- `Diagnostics/AutoPlayBrain.cs`: the auto-play bot's pile choice and steering (pure).
- `Diagnostics/SimClock.cs`: `SimClock` (simulation time) and `TickCadence` (decisions on a fixed simulated step); the clock of the auto-play and the probes.
- `Diagnostics/FighterTactics.cs`: the fighter auto-play personality's decisions (pure).
- `Gameplay/MatchTelemetry.cs`: the telemetry line formats and the `Telemetry` source tag.
- `Diagnostics/AutoTestConfig.cs`: the `cw_*` auto-test keys from intent extras or the command line.
- `Diagnostics/VisualProbe.cs`: scans live renderers for pink / missing shaders.
- `Diagnostics/Shared/DevTools.cs`: the one gate for every developer-only tool.

**Logging**
- `Logging/ILogService.cs`, `Logging/UnityLogService.cs`, `Logging/LogLevel.cs`, `Logging/LogLineFormat.cs`.

---

## 5. Data map

Every `.asset` under `Assets/_Game/Data`, with the class its `m_Script` GUID resolves to. Ability status: `registered`
= listed in `AbilityRegistry.asset`; `retired` = `Retired: 1` in the asset.

<!-- BEGIN GENERATED: data -->
| Folder (`Assets/_Game/`) | Class | Class file (`Scripts/`) | Assets (ability status) |
|---|---|---|---|
| `Data` | `AbilityRegistrySO` | `Gameplay/AbilityRegistrySO.cs` | `AbilityRegistry.asset` |
| `Data` | `AudioRegistrySO` | `Audio/AudioRegistrySO.cs` | `AudioRegistry.asset` |
| `Data` | `ChickenClassRegistrySO` | `Gameplay/ChickenClassRegistrySO.cs` | `ChickenClassRegistry.asset` |
| `Data` | `ColorSchemeSO` | `Gameplay/ColorSchemeSO.cs` | `ColorScheme.asset` |
| `Data` | `FeedbackPaletteSO` | `Visuals/FeedbackPaletteSO.cs` | `FeedbackPalette.asset` |
| `Data` | `FencePerformanceMeshesSO` | `Visuals/FencePerformanceMeshesSO.cs` | `FencePerformanceMeshes.asset` |
| `Data` | `MatchConfigSO` | `Gameplay/MatchConfigSO.cs` | `MatchConfig.asset` |
| `Data` | `PrefabRegistrySO` | `Gameplay/PrefabRegistrySO.cs` | `PrefabRegistry.asset` |
| `Data` | `SurroundHazeMapSO` | `Visuals/SurroundHazeMapSO.cs` | `SurroundHazeMap.asset` |
| `Data` | `UiAudioSO` | `Audio/UiAudioSO.cs` | `UiAudio.asset` |
| `Data/Abilities` | `AmbushAbilitySO` | `Abilities/AmbushAbilitySO.cs` | `Ambush.asset` (retired) |
| `Data/Abilities` | `BellyFlopAbilitySO` | `Abilities/BellyFlopAbilitySO.cs` | `BellyFlop.asset` (retired) |
| `Data/Abilities` | `CluckShockAbilitySO` | `Abilities/CluckShockAbilitySO.cs` | `CluckShock.asset` (registered) |
| `Data/Abilities` | `DoppelgangerAbilitySO` | `Abilities/DoppelgangerAbilitySO.cs` | `Doppelganger.asset` (retired) |
| `Data/Abilities` | `DustKickAbilitySO` | `Abilities/DustKickAbilitySO.cs` | `DustKick.asset` (retired) |
| `Data/Abilities` | `EggShellAbilitySO` | `Abilities/EggShellAbilitySO.cs` | `EggShell.asset` (retired) |
| `Data/Abilities` | `FeatherAuraAbilitySO` | `Abilities/FeatherAuraAbilitySO.cs` | `FeatherAura.asset` (registered) |
| `Data/Abilities` | `FeatherTrapAbilitySO` | `Abilities/FeatherTrapAbilitySO.cs` | `FeatherTrap.asset` (retired) |
| `Data/Abilities` | `FeintAbilitySO` | `Abilities/FeintAbilitySO.cs` | `Feint.asset` (retired) |
| `Data/Abilities` | `GroundQuakeAbilitySO` | `Abilities/GroundQuakeAbilitySO.cs` | `GroundQuake.asset` (retired) |
| `Data/Abilities` | `HeadbuttAbilitySO` | `Abilities/HeadbuttAbilitySO.cs` | `Headbutt.asset` (retired) |
| `Data/Abilities` | `ImmovableAbilitySO` | `Abilities/ImmovableAbilitySO.cs` | `Immovable.asset` (retired) |
| `Data/Abilities` | `InvisibilityAbilitySO` | `Abilities/InvisibilityAbilitySO.cs` | `Invisibility.asset` (registered) |
| `Data/Abilities` | `MarkKillAbilitySO` | `Abilities/MarkKillAbilitySO.cs` | `MarkKill.asset` (registered) |
| `Data/Abilities` | `PeckAbilitySO` | `Abilities/PeckAbilitySO.cs` | `Peck.asset` (registered) |
| `Data/Abilities` | `QuickDropAbilitySO` | `Abilities/QuickDropAbilitySO.cs` | `QuickDrop.asset` (retired) |
| `Data/Abilities` | `RollPushAbilitySO` | `Abilities/RollPushAbilitySO.cs` | `RollPush.asset` (retired) |
| `Data/Abilities` | `RollTrampleAbilitySO` | `Abilities/RollTrampleAbilitySO.cs` | `DiveBomb.asset` (retired) |
| `Data/Abilities` | `RootEggAbilitySO` | `Abilities/RootEggAbilitySO.cs` | `RootEgg.asset` (retired) |
| `Data/Abilities` | `RuffleAbilitySO` | `Abilities/RuffleAbilitySO.cs` | `Ruffle.asset` (retired) |
| `Data/Abilities` | `ScrapAbilitySO` | `Abilities/ScrapAbilitySO.cs` | `Scrap.asset` (retired) |
| `Data/Abilities` | `ShadowstepAbilitySO` | `Abilities/ShadowstepAbilitySO.cs` | `Shadowstep.asset` (registered) |
| `Data/Abilities` | `SlowAbilitySO` | `Abilities/SlowAbilitySO.cs` | `Slow.asset` (registered) |
| `Data/Abilities` | `SmokeRoostAbilitySO` | `Abilities/SmokeRoostAbilitySO.cs` | `SmokeRoost.asset` (retired) |
| `Data/Abilities` | `SnatchAbilitySO` | `Abilities/SnatchAbilitySO.cs` | `Snatch.asset` (registered) |
| `Data/Abilities` | `SneakyStealAbilitySO` | `Abilities/SneakyStealAbilitySO.cs` | `SneakySteal.asset` (retired) |
| `Data/Abilities` | `SpeedBurstAbilitySO` | `Abilities/SpeedBurstAbilitySO.cs` | `SpeedBurst.asset` (registered) |
| `Data/Abilities` | `SpineCoatAbilitySO` | `Abilities/SpineCoatAbilitySO.cs` | `SpineCoat.asset` (retired) |
| `Data/Abilities` | `TurtleModeAbilitySO` | `Abilities/TurtleModeAbilitySO.cs` | `TurtleMode.asset` (registered) |
| `Data/Abilities` | `WingSlamAbilitySO` | `Abilities/WingSlamAbilitySO.cs` | `WingSlam.asset` (registered) |
| `Data/Abilities/Passives` | `BullyPassiveSO` | `Abilities/Passives/BullyPassiveSO.cs` | `Bully.asset` (registered) |
| `Data/Abilities/Passives` | `BulwarkPassiveSO` | `Abilities/Passives/BulwarkPassiveSO.cs` | `Bulwark.asset` (registered) |
| `Data/Abilities/Passives` | `FeatherfootPassiveSO` | `Abilities/Passives/FeatherfootPassiveSO.cs` | `Featherfoot.asset` (registered) |
| `Data/Abilities/Passives` | `HoarderPassiveSO` | `Abilities/Passives/HoarderPassiveSO.cs` | `Hoarder.asset` (registered) |
| `Data/Abilities/Passives` | `RelentlessPassiveSO` | `Abilities/Passives/RelentlessPassiveSO.cs` | `Relentless.asset` (registered) |
| `Data/Abilities/Passives` | `SlipperyPassiveSO` | `Abilities/Passives/SlipperyPassiveSO.cs` | `Slippery.asset` (registered) |
| `Data/Abilities/Passives` | `SpoilerPassiveSO` | `Abilities/Passives/SpoilerPassiveSO.cs` | `Spoiler.asset` (registered) |
| `Data/Abilities/Passives` | `ThiefPassiveSO` | `Abilities/Passives/ThiefPassiveSO.cs` | `Thief.asset` (registered) |
| `Data/Balance` | `OracleMapSO` | `Balance/OracleMapSO.cs` | `OracleMap.asset` |
| `Data/Balance` | `OracleReferenceSO` | `Balance/OracleReferenceSO.cs` | `OracleReference.asset` |
| `Data/Classes` | `ChickenStatsSO` | `Gameplay/ChickenStatsSO.cs` | `Assassin.asset`, `Fatty.asset`, `Speedy.asset`, `Warrior.asset` |
| `Data/Simulator` | `ChampionPassiveSO` | `Abilities/Passives/ChampionPassiveSO.cs` | `Champion.asset` |

Ability assets: 19 registered, 19 retired.
<!-- END GENERATED: data -->

`OracleReference.asset` holds the accepted route entries and `KillTargets`, and since match simulator slice 7 also `MatchRecords` (`MatchRecord` with `AcceptedOn`, `Note`, `BalanceHash`, `Design`, `Population`, `RunSeed` and `Lines` of `MatchRecordLine`): the accepted match reports the drift line compares with. It also holds the Thief's loot target rows. `Thief.asset` holds loot fields that only the match simulator reads today: the mechanic ships off (`LootBounty` 0). `Spoiler.asset` and `Thief.asset` also hold a `MinimumCapacity` floor, read through `ModifyCargoCapacity` like the Hoarder's (Spoiler 25, Thief 0 today).

ScriptableObject assets outside `Assets/_Game/Data`: the URP assets in `Assets/Settings` (section 3.7) and
`Assets/Resources/PanelSettings.asset` (section 3.5).

---

## 6. Tests layout

- **Where:** `Assets/_Game/Scripts/Editor/Tests`, flat (no subfolders), compiled into `Assembly-CSharp-Editor`. EditMode
  only: no `[UnityTest]` and no PlayMode test assembly at HEAD.
- **Naming:** `<Subject>Tests.cs`, one fixture class per file. 242 `.cs` files: 237 `*Tests.cs` and 5 helpers.
- **Counts by kind (file names):** 68 `Phase*` files (tests written per roadmap phase/chunk, e.g.
  `Phase8ChunkDRivalArrowTests.cs`), 14 `*RulesTests.cs` (pure rules), 3 `*GuardTests.cs` (source/weave guards:
  `RuntimeSourceGuardTests`, `WeavingGuardTests`, `MenuJuiceGuardTests`), the rest named after their subject.
  Attributes: 2832 `[Test]`, 589 `[TestCase]`, 8 `[TestCaseSource]`.
- **Structural guards** a newcomer meets first: `AssemblyLayoutTests` (folder-to-assembly map, exact reference lists,
  weave list), `WeavingGuardTests`, `RuntimeSourceGuardTests`, `SceneObjectAccessTests`, `AbilityTraitTests`,
  `ChickenClassTableTests`, `StatusEffectTableTests`.

Shared helpers (all in `Assets/_Game/Scripts/Editor/Tests/`):

| Helper | What it gives a test author | Used by (files) |
|---|---|---|
| `TestSources.cs` | Reads a runtime source file as ONE class (the file plus its partial siblings) for source-scan guards | 28 |
| `TestAssets.cs` | Loads the real shipped assets (registries, configs, scenes) through one place | 83 |
| `RegistryTestAccess.cs` | Writes into the read-only scene-object registries (`ChickenController.ActiveControllers` and siblings) | 3 |
| `ShippedMap.cs` | The arena as `Game.unity` configures it, rebuilt in pure C# from values read through `TestAssets`; `OracleMapBaker` and `OracleMapTests` read the same accessors | - |
| `MatchTestKit.cs` | Shared inputs and map for the match simulator tests (releases them in `TearDown`) | 2 |
| `IconStrokeMetrics.cs` | Pixel metrics on shipped icon PNGs (used by `AbilityIconArtTests`, `StateGlyphArtTests`) | - |

Test classes for the bot sight rule and the match simulator: `BotSightTests`, `BotLootRulesTests`, `BotWarriorRulesTests`, `HitStealRulesTests`, `PeckBreakRulesTests`, `TrophyRulesTests`, `WarriorPassiveDataTests`, `OracleRosterTests`, `WhatIfSpecTests`, `MatchWarriorTests`, `MatchNavTests`, `MatchRngTests`, `MatchSimFarmTests`, `MatchFactsTests`, `MatchKitsTests`, `MatchCombatTests`, `MatchSightTests`, `MatchSpoilerTests`, `MatchStatsTests`, `MatchReviewFixTests`, `ScriptedPolicyTests`, `MatchRunnerTests`, `MatchReportTests`, `MatchReferenceTests`, `MatchSolverTermTests`, `UtilityPolicyTests`, `MatchSweepTests`, `MatchWhatIfTests`, `MatchLootTests`; `TestHookPassive` is a passive whose knockback and control hooks a test sets (used only by the match kit tests).

Recording fakes are not shared: each test file declares its own private nested class (`RecordingLog` in 9 files, e.g.
`SelfInjectTests.cs` and `MissingSfxTests.cs`; `FakeAudio`, `FakeInput`, `FakeDevice`, `FakeClock`, `FakePresenter`,
`FakeProbe`, `FakeProvider`, `RecordingAbility` elsewhere). Log fixtures for the PowerShell session tools live in
`tools/test-session/fixtures` (25 folders, 47 files).

---

## 7. Tools, docs and everything outside `Assets`

### 7.1 `tools/`

| Path | Purpose (from its header) |
|---|---|
| `tools/run-clients.ps1` | Launch N windowed Windows clients for local multiplayer testing |
| `tools/generate-uss-tokens.ps1` | Design token JSX to `Assets/UI/Styles/CluckWarsTokens.uss` |
| `tools/export-design-assets.ps1` | Render design atoms from `Design/export.html` (headless Chrome) to PNG sprites |
| `tools/test-session/` | Multi-device test sessions (12 `.ps1` + `bots-compare.py` + `README.md` + `fixtures/`): `start.ps1` / `stop.ps1` (logcat, rolling screenrecord via `rec-loop.ps1`, EXE clients), `merge.ps1` (one UTC timeline + findings), `verify.ps1` (PASS/FAIL verdict), `scenario-pc.ps1` (PC-only unattended scenario), `scenario-4seat.ps1` (EXE host + Pixel + 2 EXE clients), `scenario-bots.ps1` (four EXE clients on auto-play, accelerated, with telemetry; BL-208), `bots-compare.py` (compares the banked food per round and seat of runs of one seed at different `cw_timescale`), `get-code.ps1` (join code from logs), `shot.ps1` (timestamped screenshots), `selftest.ps1` (merge.ps1 against a synthetic fixture), `lib.ps1` (shared helpers) |
| `tools/ui_capture/` | 71 files driving the Editor through the MCP bridge (`bridge.py`, JSON-RPC over HTTP): 47 `capture_*.py` renders, metric scripts (`*_metrics.py` and others), `run_editmode_tests.py` (filtered EditMode run), `run_suite_api.py` (full suite via TestRunnerApi), `open_scene.py`, `render_heroes.py`, and `*.cs.txt` C# snippets run through the bridge; see its `README.md` |
| `tools/audio/` | `match_music.py` (in-match music loop) and `menu/` (`synth.py`, `music.py`, `analyse.py`: procedural menu audio, numpy/scipy) |
| `tools/art/` | PIL/numpy texture edits for Phase 8: `d4_surround/`, `d5_decals/`, `e3_grass_macro/`, `e4_fatty_recolour/` (with `README.txt`) |
| `tools/docs/file_structure.py` | Regenerates and checks the generated blocks of this file (section 9) |

### 7.2 `docs/`

101 tracked files, 46 of them `.md`. Status column: **living** = maintained as current reference; **snapshot** =
dated record of one session, decision or plan, not updated afterwards; **stale** only where a source says so.

| File | Purpose (from its heading) | Status |
|---|---|---|
| `docs/STATE.md` | Project state snapshot: what works, recent work, open bugs | living |
| `docs/ROADMAP.md` | Development roadmap, per-phase status | living |
| `docs/BACKLOG.md` | Open work, one line per item | living |
| `docs/CONVENTIONS.md` | Coding conventions and known footguns (incl. "Assemblies") | living |
| `docs/ARCHITECTURE_RULES.md`, `docs/CODE_RULES.md` | The rules docs written alongside this file (not at `b7c2616e`) | living |
| `docs/FILE_STRUCTURE.md` | This map | living |
| `docs/GDD.md` | Game Design Document v0.4 | living; `CLAUDE.md` states it "carries known-stale figures" (numbers come from the `.asset` files) |
| `docs/ART.md` | Art Direction v0.3 | living |
| `docs/FEEDBACK.md` | Ability and combat feedback design | living; partly stale per its own note (`docs/FEEDBACK.md:347`, the section 4 table for Peck) and `docs/ART.md:895` (its section 5.2) |
| `docs/TEST_STRATEGY.md` | Test strategy after 2026-10-20 | living |
| `docs/UNITY_MCP_GUIDE.md` | Unity + UI Toolkit + MCP architecture guide | living |
| `docs/ANTIGRAVITY_CLI_GUIDE.md` | Antigravity CLI guide | living |
| `docs/ARCHITECTURE.md` | Architecture: scene graph, DI, networking model | drifted from the code (verified at the commit in the header; no committed note marks it stale): it names `CharacterSelectController` (line 15) and `TouchControlsHud` (line 16), which no longer exist. Prefer `docs/CONVENTIONS.md`, `docs/ARCHITECTURE_RULES.md` and this file |
| `docs/TESTING.md` | Testing and diagnostics playbook | drifted from the code (verified at the commit in the header; no committed note marks it stale): it names `CharacterSelectController` (line 159), `TouchHud` (line 535), `CargoHud` (line 538), `TouchControlsHud` (line 551), none of which exist, and it still says never to add a runtime asmdef. Prefer `docs/CODE_RULES.md` for process and testing rules |
| `docs/TDD.md` | Technical Design Document v0.1 | design-time document |
| `docs/UI_HANDOFF.md` | UI engineering handoff | snapshot |
| `docs/HANDOFF-NETWORKING-2026-07.md`, `docs/HANDOFF-COOP-REPORT-QUICKWINS-2026-08.md`, `docs/HANDOFF-PHASE8D-2026-10.md` | Session handoffs | snapshot |
| `docs/TEST_SESSION_PLAN_2026-10.md` | Multi-device test session plan | snapshot |
| `docs/adr/` | `0001-engine-unity-vs-godot.md`, `0002-server-mode-migration-plan.md`, `0003-movement-terrain-and-ability-slots.md` | decision records |
| `docs/proposals/` | `BALANCE_AND_PASSIVES_2026-10.md`, `DECISIONS_2026-10-21.md`, `EFFECT_TYPES_AURA_VS_STATIC_AOE.md`, `FABLE_REVIEW_AND_CODE_PLAN_2026-10-22.md`, `PORTRAIT_AND_LANDSCAPE_SPEC.md` | snapshot (the portrait spec is still cited by code, e.g. `UiLayoutRules`) |
| `docs/superpowers/` | `specs/` (6), `plans/` (5), `handoffs/` (2), each dated | snapshot |
| `docs/balance/` | `assassin-kit-4s-proposal.md`, `human-test-baseline.md` | snapshot |
| `docs/audio/` | `death-mark-cues.md`, `sfx-brief-2026-11-07.md` | snapshot |
| `docs/design/` | `class-essence-and-signatures.md`; image sets `feedback-icons-2026-11-06/`, `speedy-spec-eval/` (with two Python scripts) | snapshot |
| `docs/diagnostics/2026-07-24-comfyui-sdpa-patch.md` | ComfyUI diagnostic | snapshot |
| `docs/reviews/2026-08-02-map-geometry.md` | Map geometry panel review | snapshot |
| `docs/site/` | The interactive mechanics site (`index.html`, `mechanics.html`, `tech.html`, `style.css`, `phone.js`, `portfolio.json`, `assets/`); deployed by `.github/workflows/pages.yml` | living (`CLAUDE.md` step 6) |
| `docs/OBSIDIAN.md` | Obsidian start-here guide | living |
| `docs/DASHBOARD.md` | Link dashboard, no copied numbers | living |
| `docs/docs.base` | Obsidian Base: docs by status | living |
| `docs/architecture.canvas` | Obsidian canvas: how the main docs fit together | living |
| `docs/GDD-v0.3.md`, `docs/TDD-v0.1.md`, `docs/ART-v0.1.md`, `docs/CLASSES-2026-08-24.md` | The four pages migrated from Notion, copied verbatim, `status: archived`; stale, never a source of numbers (live: GDD, TDD, ART) | snapshot |
| `docs/attachments/` | Images pasted through Obsidian; design images live beside their docs | living |

### 7.3 Other top-level content

| Path | Tracked | Content |
|---|---|---|
| `Design/` | yes (19 files) | Claude-design export: HTML sheets (`Design/export.html` and three design sheets), JSX screens and token sources (`cluckwars-tokens*.jsx`, read by `tools/generate-uss-tokens.ps1`), `Design/docs/ART.md` |
| `art-src/` | yes | Source art kept out of Unity's import path; `art-src/Generated/` (52 raw ComfyUI/TRELLIS outputs); its `README.md` says nothing in the project references them |
| `ProjectSettings/` | yes | Unity settings; `ProjectSettings/EditorBuildSettings.asset` is the build scene list |
| `Packages/manifest.json` | yes | Key packages: `com.unity.render-pipelines.universal` 17.3.0, `com.unity.inputsystem` 1.20.1, `com.unity.ugui` 2.0.0, `com.unity.ai.navigation` 2.0.16, `com.unity.services.core` 1.18.0, `com.unity.services.authentication` 3.8.1, `com.unity.services.multiplayer` 2.4.1, `com.unity.test-framework` 1.6.0, `com.unity.pipeline` 0.9.1-exp.1, `com.ivanmurzak.unity.mcp` 1.5.0 (+ animation, inputsystem, navigation add-ons 2.0.2), `com.unity.ai.assistant` 2.9.0-pre.1. Photon Fusion and Zenject are not packages; they live in `Assets/Photon` and `Assets/Plugins` |
| `.github/workflows/pages.yml` | yes | Deploys `docs/site` to GitHub Pages |
| Root files | yes | `CLAUDE.md` (entry point for agents), `README.md`, `LICENSE`, `.editorconfig`, `.gitattributes`, `.gitignore` |
| `.claude/` | ignored (`.gitignore:130`) | Local agent config: `.claude/CLAUDE.md`, `.claude/agents/` (15 agents: artist-2d, artist-3d, audio-designer, code-architect, level-designer, mechanics-designer, narrative-designer, qa-reviewer, render-pipeline-artist, senior-dev, shader-artist, tester, ui-designer, ux-designer, vfx-artist), `.claude/skills/`, `.claude/settings.local.json`, `.claude/worktrees/` |
| `Builds/` | ignored (`.gitignore:13`) | Player builds from the build menu |
| `Captures/` | ignored (`.gitignore:178`) | Local evidence: test sessions, capture renders |
| `Library/` | ignored (`.gitignore:9`) | Unity's import cache |
| `Logs/` | ignored (`.gitignore:14`) | Unity logs |
| `UserSettings/` | ignored (`.gitignore:15`) | Per-user Editor settings |
| `graphify-out/` | ignored (`.gitignore:157`) | Knowledge-graph output |

The ignore lines come from `git check-ignore -v <folder>` run on 2026-11-13.

---

## 8. Where to put things

Each row cites an existing example. Folder paths are under `Assets/_Game/Scripts` unless they start with `Assets/`.

| Adding | Folder | Assembly | Companion files to touch too | Example |
|---|---|---|---|---|
| Gameplay system (networked) | `Gameplay` | Gameplay | the prefab that carries it; `Assets/_Game/Data/PrefabRegistry.asset` if spawned at runtime | `ChickenCargo`, `FoodPile`, `PlayerBase` |
| Pure game rule (no Fusion types) | `Gameplay` as `<Topic>Rules.cs` | Gameplay | a `<Topic>RulesTests.cs` | `StealRules`, `BaseDepositRules` |
| Ability | `Abilities/<Name>AbilitySO.cs` | Gameplay | asset in `Assets/_Game/Data/Abilities/`; entry in `Assets/_Game/Data/AbilityRegistry.asset`; kit entries in the passive assets that use it; its `Job` (`AbilityJob`) and `AccentKey`; icon `Assets/_Game/Art/UI/Icons/Abilities/Icon_<Class>.png` and its USS class (`AbilityIconStyle`, `CluckWarsTheme.uss`, `TouchControls.uss`); name/description rows in `UiText.csv` + `UiKeys` | `SlowAbilitySO` + `Slow.asset` (commit `8eae659a` touched each of these) |
| Passive (specialization) | `Abilities/Passives/<Name>PassiveSO.cs` | Gameplay | asset in `Assets/_Game/Data/Abilities/Passives/` (holds the fixed kit); registry entry; a `ChickenSubclass` value | `BullyPassiveSO` + `Bully.asset` |
| Chicken class | `Gameplay/ChickenClass.cs` (append, never renumber) | Gameplay | `ChickenClasses` row in `Gameplay/ChickenClassInfo.cs`; stats asset `Assets/_Game/Data/Classes/<Name>.asset`; `Assets/_Game/Data/ChickenClassRegistry.asset`; `ChickenClassTableTests` | `Assassin.asset` |
| Status effect | state in `Gameplay` (`ControlState`, `EffectWindow` on `ChickenController`) | Gameplay + Presentation | row in `Visuals/StatusEffects.cs`; VFX in `Visuals` (`ControlStateVFX`); `StatusEffectTableTests` | `ControlState`, `StatusEffects` |
| Change to a `[Networked]` property or an `[Rpc]` signature | where the member lives | the member's assembly | bump `Networking/NetworkProtocol.cs` `Version` in the same commit, then set `ExpectedVersion` and `ExpectedFingerprint` in `NetworkProtocolTests` (the failing test prints both); a live two-build check is owed (ARCH-NET-21) | `ThiefLoot` (version 2) |
| Networked behaviour | `Gameplay` (or another woven folder) | a woven assembly | a new assembly that references Fusion goes into `AssembliesToWeave` of `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`; `WeavingGuardTests` | `ChickenMatchStats` |
| VFX / visual component (local only) | `Visuals` | Presentation | the prefab it sits on; constants in `FeedbackTuning` | `ChickenVFX`, `FoodPileVisuals` |
| UI screen | `Assets/UI/<Name>.uxml`, `Assets/UI/Styles/<Name>.uss`, controller in `UI` | Presentation | a `UIDocument` in the scene using `Assets/Resources/PanelSettings.asset` | `MatchTopBar.uxml` + `MatchHudController` |
| Menu page | partial file `UI/MenuUiController.<Page>.cs` | Presentation | its elements in the menu UXML | `MenuUiController.PickBird.cs` |
| HUD element | `UI` (top bar, overlays) or `Input` (touch cluster) | Presentation | its UXML/USS; `Visuals/HudControlZones.cs` when it occupies screen space controls avoid | `MatchHudController`, `TouchControlsController.Hud.cs` |
| Service interface | next to its domain: `Services` (Core), `Audio` / `Networking` (Gameplay), `Input` (Presentation), `Logging` (Core) | the lowest layer its users sit in | binding in `Installers/ProjectInstaller.cs` or `GameInstaller.cs` | `IUGSService`, `IAudioService`, `INetworkService` |
| Service implementation | Fusion/UGS-dependent: `App`; otherwise beside the interface | App, or the interface's assembly | the installer binding | `FusionNetworkService`, `UGSService`; `NullUGSService`, `UnityAudioService` |
| Diagnostic probe | `Diagnostics` (state lower layers read: `Diagnostics/Shared`) | Diagnostics (Shared: Core) | gate on `DevTools`; its pure rules in a `*Rules.cs` | `InvariantProbe`, `PerfProbe` + `PerfRules` |
| Editor tool | `Scripts/Editor` (map/scene bakers: `Assets/_Game/Editor`) | Assembly-CSharp-Editor | none | `CluckWarsBuildMenu`, `MapSceneBaker` |
| Simulator ability fact (a number or behaviour the match simulator reads off an ability) | `Abilities` (declared on the SO through `AbilityOracleFacts`), read by `Balance/MatchKits` | Gameplay | a `MatchFactsTests` row proving the fact equals the authored field; `MatchKitsTests` if it changes a kit | `AbilityOracleFacts`, `MatchKits` |
| Oracle input (a number the balance oracle reads) | `Balance` (reader), `Abilities` (a fact an ability owns, via `AbilityOracleFacts`) | Gameplay | `OracleKits` if a new kit input; `OracleReference.asset` re-accepted with `Cluck Wars / Balance / Oracle / Accept as balanced` when the drift guard goes red; `OracleMap.asset` re-baked when the arena moves | `OracleKits`, `AbilityOracleFacts` |
| Test | `Scripts/Editor/Tests/<Subject>Tests.cs` | Assembly-CSharp-Editor | source scans read through `TestSources`, shipped assets through `TestAssets` | `FoodTransferRulesTests.cs` (the Peck food guard also lives in `PeckCastTests.cs`; the Peck clip and controller wiring in `PeckCastAnimationTests.cs`; the Peck hex fill and the cut cue in `PeckCastCuesTests.cs`) |
| ScriptableObject | class `<Name>SO` beside its domain (`Gameplay`, `Audio`, `Visuals`); asset in `Assets/_Game/Data/` | the class's folder's assembly | binding in `ProjectInstaller` if injected | `MatchConfigSO`, `AudioRegistrySO`, `FeedbackPaletteSO` |
| Prefab | `Assets/_Game/Prefabs` (`Props/`, `Abilities/`) | - | `PrefabRegistry.asset` if spawned at runtime | `Chicken.prefab`, `AbilityZone.prefab` |
| Audio clip | `Assets/_Game/Audio/UI` or `Assets/_Game/Audio/Abilities` + its `MANIFEST.md` | - | an `SfxId` value, a slot in `AudioRegistrySO` (`Resolve`) and `Assets/_Game/Data/AudioRegistry.asset`; menu sounds go through `UiAudioSO` / `UiAudio.asset` | existing `.wav` + `MANIFEST.md` |
| Localization string | row in `Assets/_Game/Resources/Text/UiText.csv` | Core (`UiKeys`) | a constant in `Localization/UiKeys.cs` | commit `2341c869` (ability descriptions) |
| New `Scripts/` folder | `Assets/_Game/Scripts/<Folder>` | the layer it belongs to | an `.asmref` (`{ "reference": "CluckWars.<Layer>" }`) and the folder in `AssemblyLayoutTests.FolderAssembly`; a subfolder of an existing folder needs nothing | `Geometry/CluckWars.Core.asmref` |

---

## 9. How this file stays true

The four generated blocks (`assemblies`, `script-folders`, `game-assets`, `data`) come from
`tools/docs/file_structure.py`, which reads the working tree (`.asmdef` / `.asmref` / `.meta` files, the weave
config, `Assets/_Game`), standard library only:

```
python -I tools/docs/file_structure.py generate          # print the blocks
python -I tools/docs/file_structure.py check             # exit 0 = doc matches, 1 = diff printed, 2 = repo unreadable
python -I tools/docs/file_structure.py check --write     # rewrite the blocks in place
python -I tools/docs/file_structure.py check --doc <path>
```

Run `check` after adding or moving a script folder, an `.asmdef` / `.asmref`, a weave entry, an asset under
`Assets/_Game/Data`, or files under `Assets/_Game`. A package assembly the tool does not know prints as `GUID:<hex>`;
add it to `KNOWN_PACKAGE_ASMDEFS` in the script. The hand-written sections (paths, key files, purposes) are not
checked by the script: when a file they name moves, update the line, and update the commit in the header when you
re-verify the whole file.
