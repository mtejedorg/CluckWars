---
status: live
owner: Maestro
type: rules
---
# Cluck Wars — Architecture Rules

Generated from commit `b7c2616e` (branch `simplify-abilities`), 2026-11-13. Committed code only: every path, line number, test and type named below was read at that commit with `git show b7c2616e:<path>` / `git grep b7c2616e`.

**Purpose.** This is the consolidated set of architecture rules for Cluck Wars, with each rule tied to its sources and to whatever enforces it. Agents, Maestro and the repeatable architecture-audit skill all read it. A rule that nothing enforces tends to drift unnoticed, so every rule says plainly what enforces it. When the answer is "nothing" or "a reviewer", the entry gives a grep or review step an auditor can run today, plus the guard that should be written. The rules cover layering and assemblies, dependency injection, networking and authority, replicated events and presentation, data-driven abilities and classes, and error surfacing. After the rules come the lens criteria the 2026-11-12 audit was run with, the observation rule set it adopted, and a catalogue of the patterns it established.

**Sibling documents.** Coding, testing, process and design-constraint rules are in [`CODE_RULES.md`](CODE_RULES.md). Folder and file placement is in [`FILE_STRUCTURE.md`](FILE_STRUCTURE.md). The day-to-day prose rules are in [`CONVENTIONS.md`](CONVENTIONS.md). This document does not repeat the other two. **Where this document disagrees with a stale line elsewhere, this document wins until that line is fixed.** Appendix A lists every stale line found.

## How to read an entry

```
### ARCH-NET-07 — <imperative title>
- Rule:          the rule, keeping the source's strength words ("never", "must")
- Why:           the reason, or "not stated"
- Enforced by:   one or more mechanisms (vocabulary below); the first one listed is the primary one
- Audit check:   present only when the line above includes CONVENTION or NONE: the step that finds violations
- Proposed guard: present only when the line above includes NONE: the test that would enforce it
- Source:        where the rule is written (path:line), or the owner thread it came from
- Status:        CURRENT (the rule is in force) | PARTIAL (in force, but only part of it is enforced; the entry says which part)
```

**IDs are permanent.** `ARCH-<AREA>-nn`. A rule is never renumbered. A rule that dies moves to Appendix B with its ID, and its number is never reused. Areas: `LAY` layering, assemblies, weaving, constants placement, dependency direction · `DI` injection, services behind interfaces, scene-object lookup, registries and singletons · `NET` authority, RPCs, claims, latency, transfers, loadouts, networked layout · `EVT` replicated moments, observers, presenter inversion, presentation reading state, audio audience, the local-player accessor · `DATA` data-driven abilities, effects and classes, traits instead of types, one path per primitive, tables with totality tests · `ERR` logging and error surfacing.

## Enforcement vocabulary

| Term (index word) | Meaning |
|---|---|
| `TEST` (TEST) | An EditMode NUnit test fails when the rule is broken. **Caveat:** a TEST only bites when somebody runs the suite. There are no git hooks and no test CI; the only workflow deploys `docs/site`. See section 7. |
| `COMPILER` (COMPILER) | The broken code does not compile, because an asmdef reference is missing or an access modifier forbids it. |
| `ASSEMBLY CONFIG` (CONFIG) | A configuration file the toolchain obeys, such as asmdef settings or Fusion's `AssembliesToWeave`. |
| `CONVENTION` (REVIEW) | The rule is written down, but only a human or agent review catches a violation. The entry's Audit check is that review step. |
| `NONE` (NONE) | No mechanism and no habitual review. The entry gives an Audit check and a Proposed guard. |

The index uses the first (primary) mechanism of each entry. An entry whose Status is PARTIAL also carries a CONVENTION or NONE part, and that part appears in section 7.

**Path shorthand:** `Scripts/` = `Assets/_Game/Scripts/`; `Tests/` = `Assets/_Game/Scripts/Editor/Tests/`; `M/<file>` = the project's auto-memory folder `C:\Users\MARCO\.claude\projects\C--Users-MARCO-Documents-GitHub-CluckWars\memory\<file>`, which is outside the repo; `.claude/CLAUDE.md` is git-ignored and was read from disk. A bare `:N` after a test name is a line in that test's file.

**Not covered here.** Progression-system rules, such as polling round end and never using a ChangeDetector for it (M/progression-system-plan.md:15), live on the progression branch. No progression code exists at `b7c2616e`.

## 2. Index

Every rule in this document. "Enforced" is the primary mechanism (the first one listed in the entry); Status PARTIAL means part of the rule is enforced only by review or by nothing (section 7).

| ID | Rule | Enforced | Status |
|---|---|---|---|
| ARCH-LAY-01 | Keep five runtime assemblies and never add an upward reference | COMPILER | CURRENT |
| ARCH-LAY-02 | Give a new top-level Scripts folder an .asmref and a FolderAssembly row | TEST | CURRENT |
| ARCH-LAY-03 | Put no Editor folder inside a runtime folder | TEST | CURRENT |
| ARCH-LAY-04 | Keep Core at the bottom: Zenject only, no Fusion, no package | TEST | CURRENT |
| ARCH-LAY-05 | Add a package reference to the asmdef that needs it and to the pinned list | TEST | CURRENT |
| ARCH-LAY-06 | List every assembly with network code in AssembliesToWeave, in the same commit | TEST | PARTIAL |
| ARCH-LAY-07 | Treat internal as "this assembly" and expose no internals to tests | COMPILER | PARTIAL |
| ARCH-LAY-08 | Keep editor APIs out of runtime code; nothing compiles into Assembly-CSharp | TEST | PARTIAL |
| ARCH-LAY-09 | Stay with MonoBehaviour; no ECS/DOTS without a measured bottleneck | TEST | CURRENT |
| ARCH-LAY-10 | Put a constant both layers need in Gameplay and alias it upward | TEST | PARTIAL |
| ARCH-LAY-11 | Keep the ability accent key with the abilities and resolve its colour in Presentation | TEST | CURRENT |
| ARCH-LAY-12 | Keep the composition root in App | TEST | CURRENT |
| ARCH-LAY-13 | Split an oversized scene or prefab component only as a partial class | TEST | PARTIAL |
| ARCH-LAY-14 | Leave ChickenController whole; never loosen a guard to make a split pass | REVIEW | CURRENT |
| ARCH-LAY-15 | Name every colour and juice timing that carries gameplay meaning | TEST | PARTIAL |
| ARCH-DI-01 | Put every service behind its interface | COMPILER | PARTIAL |
| ARCH-DI-02 | Bind services in the two installers only | NONE | CURRENT |
| ARCH-DI-03 | Self-inject with the one SelfInject.Ensure line and no other form | TEST | PARTIAL |
| ARCH-DI-04 | Let only the two renderer features borrow the project logger | TEST | PARTIAL |
| ARCH-DI-05 | Never gate on ProjectContext.HasInstance | TEST | PARTIAL |
| ARCH-DI-06 | Register a scene installer's container on the first line | TEST | PARTIAL |
| ARCH-DI-07 | Do not guard objects the container builds | REVIEW | CURRENT |
| ARCH-DI-08 | Expect SelfInject to report, never throw | TEST | CURRENT |
| ARCH-DI-09 | Reach each kind of thing one way: Construct, registry, or static accessor | TEST | PARTIAL |
| ARCH-DI-10 | Make a registry an IReadOnlyList over a private list that only its owner mutates | TEST | PARTIAL |
| ARCH-DI-11 | Iterate a registry by index | TEST | CURRENT |
| ARCH-DI-12 | Have a singleton set and clear itself, and reset every caching static | TEST | PARTIAL |
| ARCH-DI-13 | Check a Unity-object singleton with != null, never ?. | TEST | CURRENT |
| ARCH-DI-14 | Use Find* only at one-time bootstrap and in dev tools, never per frame | TEST | CURRENT |
| ARCH-DI-15 | Hand a ScriptableObject its logger through AbilityContext.Log | TEST | CURRENT |
| ARCH-DI-16 | Bind static-data SOs by instance, with a runtime-empty fallback that warns | TEST | PARTIAL |
| ARCH-DI-17 | Keep exactly one serialized reference to MatchConfig.asset | TEST | PARTIAL |
| ARCH-NET-01 | Open every FixedUpdateNetwork with the HasStateAuthority gate | NONE | CURRENT |
| ARCH-NET-02 | Write foreign state only through an RPC to its state authority | TEST | CURRENT |
| ARCH-NET-03 | Give every RPC an RpcInfo and check the sender before any write | TEST | PARTIAL |
| ARCH-NET-04 | Choose the sender check by what the RPC names; never compare with input authority | TEST | CURRENT |
| ARCH-NET-05 | Bound an RPC's magnitude by what the registered abilities declare | TEST | PARTIAL |
| ARCH-NET-06 | Take a sender's latency from ChickenCombat.SenderLatency, never raw GetPlayerRtt | TEST | CURRENT |
| ARCH-NET-07 | Make every food transfer authority-confirmed: the owner debits, then reports | TEST | PARTIAL |
| ARCH-NET-08 | Keep the comeback flags private-set and granted by one RPC | TEST | CURRENT |
| ARCH-NET-09 | Accept round-reset RPCs from the match authority only | TEST | CURRENT |
| ARCH-NET-10 | Judge control and knockback claims before applying, and send them only through the primitives | TEST | CURRENT |
| ARCH-NET-11 | Replicate loadouts as registry codes and validate them on proxies | TEST | CURRENT |
| ARCH-NET-12 | Set networked properties in onBeforeSpawned; never despawn inside it | REVIEW | CURRENT |
| ARCH-NET-13 | Give networked and serialized enums a sane zero, byte backing and an ordinal pin | TEST | PARTIAL |
| ARCH-NET-14 | Never change networked layout to tidy code | TEST | PARTIAL |
| ARCH-NET-15 | Hold a timed window on a chicken in a networked TickTimer read through EffectWindow | TEST | PARTIAL |
| ARCH-NET-16 | Give every networked object a NetworkTransform or a stamped position, and check it on a client | TEST | PARTIAL |
| ARCH-NET-17 | Read game input only from the Fusion input buffer | COMPILER | PARTIAL |
| ARCH-NET-18 | Read every input provider every tick; never read a press getter for diagnostics | TEST | PARTIAL |
| ARCH-NET-19 | Keep the duplicated SessionNameSeed copies identical | TEST | CURRENT |
| ARCH-NET-20 | Keep the simulation identical in solo and online; branch on SessionMode only for seeds and session setup | NONE | CURRENT |
| ARCH-EVT-01 | Keep animation, VFX, audio cues and HUD reactions local and never RPC them | TEST | PARTIAL |
| ARCH-EVT-02 | Announce a one-shot moment by bumping a networked byte event id, and never reset it | TEST | PARTIAL |
| ARCH-EVT-03 | Watch the counters in exactly one per-chicken observer; presentation keeps no cursor | TEST | PARTIAL |
| ARCH-EVT-04 | Carry ids, enums and numbers in event payloads, never display strings or clips | TEST | PARTIAL |
| ARCH-EVT-05 | Keep gameplay from calling presentation; raise a local event instead | COMPILER | CURRENT |
| ARCH-EVT-06 | Read continuous replicated state with a ChangeDetector on its own behaviour, or poll it | REVIEW | CURRENT |
| ARCH-EVT-07 | Pair every += on a local event with a -= | TEST | PARTIAL |
| ARCH-EVT-08 | Latch an event and call Observe() when the listener must act at its own point in the frame | REVIEW | CURRENT |
| ARCH-EVT-09 | Isolate a throwing listener, and report a raise nobody hears | TEST | CURRENT |
| ARCH-EVT-10 | Let only the human who drives a chicken hear its own sounds | TEST | CURRENT |
| ARCH-EVT-11 | Latch an own-player cue in the tick, raise it in Render, play it by SfxId | TEST | CURRENT |
| ARCH-EVT-12 | Find the local chicken through ChickenController.Local, never by scanning for input authority | TEST | PARTIAL |
| ARCH-EVT-13 | Reach chicken presentation through the presenter interface Gameplay declares | COMPILER | CURRENT |
| ARCH-EVT-14 | Let UI poll state, and use a reset static mailbox for cross-HUD cues | REVIEW | CURRENT |
| ARCH-EVT-15 | Route UI actions through a public method that checks authority itself | REVIEW | CURRENT |
| ARCH-DATA-01 | Add an ability as an AbilityBaseSO subclass plus a registry entry, and nothing else | TEST | CURRENT |
| ARCH-DATA-02 | Ask an ability what it does, never which class it is | TEST | CURRENT |
| ARCH-DATA-03 | Answer a new question with a new trait and a test row | REVIEW | CURRENT |
| ARCH-DATA-04 | Give each job one ability; a new effect needs a new job | TEST | CURRENT |
| ARCH-DATA-05 | Compose the one path per gameplay primitive; never re-implement it | TEST | PARTIAL |
| ARCH-DATA-06 | Refuse a spawning or area ability's press in CanActivate, before any cost | TEST | PARTIAL |
| ARCH-DATA-07 | Use Planar for ground-plane maths, but keep exact-boundary range verdicts inline | TEST | PARTIAL |
| ARCH-DATA-08 | Define each chicken class once, in ChickenClasses; never switch on ChickenClass | TEST | PARTIAL |
| ARCH-DATA-09 | Translate status effects through the one StatusEffects table | TEST | PARTIAL |
| ARCH-DATA-10 | Make every enum-keyed table total and pin a replaced mapping | REVIEW | CURRENT |
| ARCH-DATA-11 | Keep gameplay numbers in data, and tune by changing the asset only | NONE | CURRENT |
| ARCH-DATA-12 | Fill every number in ability and passive text from the SO field it describes | TEST | PARTIAL |
| ARCH-DATA-13 | Keep the retired ability classes until alpha feedback | REVIEW | CURRENT |
| ARCH-DATA-14 | Change shipped balance only through the BalanceBook delta, which clients read and never write | TEST | CURRENT |
| ARCH-ERR-01 | Send all log output through ILogService | TEST | CURRENT |
| ARCH-ERR-02 | Keep the log line format, and lead an error with its Source line | TEST | PARTIAL |
| ARCH-ERR-03 | Sort every null guard by whether a legal game state can produce it | REVIEW | CURRENT |
| ARCH-ERR-04 | Refuse before the side effect rather than reporting after it | TEST | PARTIAL |
| ARCH-ERR-05 | Surface a failure after a side effect or a Spawn; never roll back, never despawn in onBeforeSpawned | REVIEW | CURRENT |
| ARCH-ERR-06 | Log an RPC rejection as one rate-limited Warn with the stable prefix | TEST | PARTIAL |
| ARCH-ERR-07 | Report a missing sound clip; keep silence only where it is designed and say so | TEST | PARTIAL |
| ARCH-ERR-08 | Look up UXML elements through UiElements so a missing element is reported | NONE | CURRENT |

## 3. Rules

## 3.1 Layering, assemblies and weaving (ARCH-LAY)

Runtime code is five assemblies stacked bottom to top, and the compiler enforces the direction. Most of this area is mechanically enforced by `AssemblyLayoutTests` and `WeavingGuardTests`. The weak spots are the "same commit" and "Library/Bee" process steps, editor-API use inside runtime code, and where shared constants and colours live.

### ARCH-LAY-01 — Keep five runtime assemblies and never add an upward reference
- Rule: Runtime code is `CluckWars.Core` < `Gameplay` < `Presentation` < `Diagnostics` < `App`, and each references only the ones to its left. "Never add an upward reference to make something compile." Move the type down, or put an interface in the lower layer and bind the implementation in an installer (ARCH-EVT-13).
- Why: a lower layer that names an upper type no longer compiles, which keeps the simulation free of presentation.
- Enforced by: COMPILER (asmdef references); TEST `AssemblyLayoutTests.EveryAsmdef_KeepsExactlyItsReferences` (Tests/AssemblyLayoutTests.cs:195), `AssemblyLayoutTests.TheCompiledAssemblies_ReferenceOnlyTheLayersBelow` (:239), `GameplayPresentationBoundaryTests.NoGameplayFile_ImportsVisualsOrUi` (Tests/GameplayPresentationBoundaryTests.cs:102)
- Source: docs/CONVENTIONS.md:478-480, docs/CONVENTIONS.md:494, .claude/CLAUDE.md:77, docs/STATE.md:132; owner decision D5b 2026-11-12 (M/arch-audit-decisions-2026-11-12.md:18)
- Status: CURRENT

### ARCH-LAY-02 — Give a new top-level Scripts folder an .asmref and a FolderAssembly row
- Rule: A new top-level folder under `Scripts/` compiles into `Assembly-CSharp` until it gets a `<Assembly>.asmref` (`{ "reference": "CluckWars.<Layer>" }`). Add the file, then add the folder to `AssemblyLayoutTests.FolderAssembly`. A subfolder of an existing folder needs nothing.
- Why: without the asmref, the folder falls outside the five assemblies and their reference rules.
- Enforced by: TEST `AssemblyLayoutTests.EveryScript_CompilesIntoTheAssemblyOfItsFolder` (Tests/AssemblyLayoutTests.cs:132), `AssemblyLayoutTests.EveryFolder_HasExactlyTheAssemblyFileItShould` (:167)
- Source: docs/CONVENTIONS.md:496, .claude/CLAUDE.md:77
- Status: CURRENT

### ARCH-LAY-03 — Put no Editor folder inside a runtime folder
- Rule: There must be no `Editor` folder inside a runtime folder. Editor code goes in `Scripts/Editor/`.
- Why: below an asmdef, an `Editor` folder is not editor-only. It compiles into the runtime assembly and breaks the player build.
- Enforced by: TEST `AssemblyLayoutTests.NoRuntimeAssemblyFolder_HoldsAnEditorFolder` (Tests/AssemblyLayoutTests.cs:155)
- Source: docs/CONVENTIONS.md:497, docs/CONVENTIONS.md:508
- Status: CURRENT

### ARCH-LAY-04 — Keep Core at the bottom: Zenject only, no Fusion, no package
- Rule: `CluckWars.Core` references Zenject only, through `overrideReferences` with `Zenject-usage.dll` as its only prebuilt DLL: "no Fusion, no package". `Diagnostics/Shared/` is Core even though it sits inside the Diagnostics folder and keeps the `CluckWars.Diagnostics` namespace; its own `.asmref` keeps it there. `Geometry/` (`Planar`) is also Core.
- Why: every layer reads Core, so Core must not drag the network stack or packages into the layers below them.
- Enforced by: TEST `AssemblyLayoutTests.EveryAsmdef_KeepsItsSettings` (Tests/AssemblyLayoutTests.cs:205), `AssemblyLayoutTests.TheCompiledAssemblies_ReferenceOnlyTheLayersBelow` (:239); ASSEMBLY CONFIG (Scripts/Logging/CluckWars.Core.asmdef)
- Source: docs/CONVENTIONS.md:484, docs/CONVENTIONS.md:498-499, Tests/AssemblyLayoutTests.cs:37-38 (folder map including `Geometry`)
- Status: CURRENT (docs/CONVENTIONS.md:484 omits `Geometry/` from Core's folder list; see Appendix A)

### ARCH-LAY-05 — Add a package reference to the asmdef that needs it and to the pinned list
- Rule: A new package reference goes into the asmdef that needs it and into `AssemblyLayoutTests.References`, whose lists are pinned exactly. This is also what keeps UGS (`Unity.Services.*`) in App only and device input (`Unity.InputSystem`) out of Core and Gameplay.
- Why: an unreviewed package reference is how a layer quietly gains a dependency it must not have.
- Enforced by: TEST `AssemblyLayoutTests.EveryAsmdef_KeepsExactlyItsReferences` (Tests/AssemblyLayoutTests.cs:195); COMPILER
- Source: docs/CONVENTIONS.md:499
- Status: CURRENT

### ARCH-LAY-06 — List every assembly with network code in AssembliesToWeave, in the same commit
- Rule: Fusion's IL weaver processes only the assemblies named in `AssembliesToWeave` of `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`. "Any new assembly that references Fusion goes into that list in the same commit as its `.asmdef`." In batch mode a config-only change does not re-weave an already-compiled assembly, so delete `Library/Bee` first.
- Why: a NetworkBehaviour in an unlisted assembly compiles, passes most tests, and throws "has not been weaved" at spawn.
- Enforced by: TEST `WeavingGuardTests.EveryAssemblyWithNetworkCode_IsStampedAndListed` (Tests/WeavingGuardTests.cs:117), `WeavingGuardTests.EveryNetworkBehaviour_IsWeaved` (:93), `AssemblyLayoutTests.TheWeaveList_NamesAllFiveAssemblies_AndTheNetworkTypesLiveWhereTheGuardLooks` (Tests/AssemblyLayoutTests.cs:255); ASSEMBLY CONFIG; CONVENTION for the same-commit and `Library/Bee` steps
- Audit check: for each commit that adds or renames an `.asmdef`, run `git show --stat <sha>` and confirm it also touches `NetworkProjectConfig.fusion`. After a config-only weave edit, confirm `Library/Bee` was deleted before a green `WeavingGuardTests` is trusted.
- Source: docs/CONVENTIONS.md:495, .claude/CLAUDE.md:77, docs/STATE.md:109
- Status: PARTIAL (weaving is checked by reflection only on assemblies named `Assembly-CSharp` or `CluckWars.*`, Tests/WeavingGuardTests.cs:36-40; the process steps are review only)

### ARCH-LAY-07 — Treat internal as "this assembly" and expose no internals to tests
- Rule: `internal` means "this assembly", not "the whole game". No `InternalsVisibleTo` exists. Tests reach the internals of the runtime assemblies only through public API.
- Why: not stated.
- Enforced by: COMPILER (`internal` across assemblies does not compile); NONE for the `InternalsVisibleTo` ban (0 hits at `b7c2616e`)
- Audit check: `git grep -n InternalsVisibleTo -- Assets/_Game` must print nothing.
- Proposed guard: a case in `AssemblyLayoutTests` asserting that no loaded `CluckWars.*` assembly carries `System.Runtime.CompilerServices.InternalsVisibleToAttribute`.
- Source: docs/CONVENTIONS.md:500
- Status: PARTIAL

### ARCH-LAY-08 — Keep editor APIs out of runtime code; nothing compiles into Assembly-CSharp
- Rule: Editor tools and tests stay in the built-in `Assembly-CSharp-Editor` (`Scripts/Editor/`, `Assets/_Game/Editor/`). Nothing compiles into `Assembly-CSharp` any more. Do not reference editor APIs from runtime scripts.
- Why: an editor API in runtime code compiles in the Editor and breaks only the player build.
- Enforced by: TEST `AssemblyLayoutTests.TheCompiledAssemblies_ReferenceOnlyTheLayersBelow` (Tests/AssemblyLayoutTests.cs:239; fails if `Assembly-CSharp` exists); NONE for `UnityEditor` use inside runtime files (5 `UnityEditor.` hits at `b7c2616e`, all meant to sit inside `#if UNITY_EDITOR`)
- Audit check: `git grep -n "UnityEditor" -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'`, then confirm every hit sits inside an `#if UNITY_EDITOR` block. Building a player is the real proof.
- Proposed guard: a source scan over `RuntimeSourceGuardTests.RuntimeFiles()` that fails on `UnityEditor` outside `#if UNITY_EDITOR ... #endif` regions.
- Source: docs/CONVENTIONS.md:490, docs/CONVENTIONS.md:508
- Status: PARTIAL

### ARCH-LAY-09 — Stay with MonoBehaviour; no ECS/DOTS without a measured bottleneck
- Rule: The architecture is MonoBehaviour. Use "MonoBehaviour over ECS/DOTS unless a concrete performance bottleneck justifies the complexity cost."
- Why: "Simplicity — no over-engineering" (guiding priority 3).
- Enforced by: TEST `AssemblyLayoutTests.EveryAsmdef_KeepsExactlyItsReferences` (Tests/AssemblyLayoutTests.cs:195; indirect: no runtime asmdef can reference an Entities package without editing the pinned list)
- Source: docs/TDD.md:15, docs/TDD.md:30, CLAUDE.md:16
- Status: CURRENT

### ARCH-LAY-10 — Put a constant both layers need in Gameplay and alias it upward
- Rule: Values the simulation owns live in Gameplay (`AbilityHoldStateMachine.TapHoldThresholdSeconds`, `ChickenCargo.CargoChangeEventThreshold`), and the `FeedbackTuning` names are compile-time aliases of them. The player colours P1..P4 are defined once in `Scripts/Gameplay/PlayerColors.cs`, and `PlayerPalette` forwards them. A Gameplay file cannot name `FeedbackTuning`, so its colour stays local, "with a comment saying why" (`PlayerBase.UnownedColor`).
- Why: Gameplay must not depend on Presentation constants, and a copied literal drifts.
- Enforced by: TEST `GameplayPresentationBoundaryTests.TheTwoSimulationConstants_LiveInGameplay_AndFeedbackTuningIsTheSameValue` (Tests/GameplayPresentationBoundaryTests.cs:115), `PlayerPaletteTests.NoOtherFile_DefinesItsOwnCopyOfThePlayerColours` (Tests/PlayerPaletteTests.cs:45); COMPILER; CONVENTION for any new shared constant and for the "comment saying why"
- Audit check: for each new `const` or `static readonly` in a Presentation file that mirrors a gameplay value, `git grep -n "<value>" -- Assets/_Game/Scripts` should find exactly one literal, in Gameplay.
- Source: docs/CONVENTIONS.md:263, docs/STATE.md:128-129
- Status: PARTIAL

### ARCH-LAY-11 — Keep the ability accent key with the abilities and resolve its colour in Presentation
- Rule: `AccentKey` is `CluckWars.Abilities.AccentKey`, and its numbers are pinned. Its colour is resolved by the extension `ability.AccentColor()` in `Scripts/Visuals/AbilityAccent.cs`, not by `AbilityBaseSO`.
- Why: an ability asset serializes the key, while the colour is presentation (Gameplay < Presentation).
- Enforced by: TEST `FeedbackPaletteTests.AccentKey_KeepsItsSerializedNumbers_AndLivesWithTheAbilities` (Tests/FeedbackPaletteTests.cs:35), `FeedbackPaletteTests.AccentColor_IsResolvedByPresentation_WithTheSameRuleTheAbilityUsed`
- Source: docs/STATE.md:130
- Status: CURRENT

### ARCH-LAY-12 — Keep the composition root in App
- Rule: `CluckWars.App` holds the Zenject installers and the two concrete services that see every layer (`FusionNetworkService`, `UGSService`). Only App references the UGS packages.
- Why: the composition root sees every layer; nothing below it may.
- Enforced by: TEST `AssemblyLayoutTests.EveryAsmdef_KeepsExactlyItsReferences` (Tests/AssemblyLayoutTests.cs:195); COMPILER
- Source: docs/CONVENTIONS.md:488
- Status: CURRENT

### ARCH-LAY-13 — Split an oversized scene or prefab component only as a partial class
- Rule: A component that a scene or prefab references keeps its type, name, namespace, attributes, serialized fields and public API. The only way to split one is a partial class (extra `Name.Concern.cs` files beside it), "never moving its serialized state and never adding a component to a prefab".
- Why: scene and prefab YAML reference the script by GUID and its fields by name.
- Enforced by: TEST `MissingScriptsTests.NoPrefabFile_ReferencesAScriptThatDoesNotResolve` (Tests/MissingScriptsTests.cs:47; catches a broken script reference only); CONVENTION for the unchanged fields and API
- Audit check: in a split commit, run `git diff <sha>~1 <sha> -- <Name>.cs` and confirm no `[SerializeField]` line, namespace, type name or public member was removed. The new files must be `Name.*.cs` declaring `partial class Name`.
- Source: docs/CONVENTIONS.md:273, docs/STATE.md:62-64
- Status: PARTIAL

### ARCH-LAY-14 — Leave ChickenController whole; never loosen a guard to make a split pass
- Rule: `ChickenController` was examined and deliberately left whole. Its control-claim RPCs are its own wire surface, and the two dozen single-file guards over them are worth more than the line count. "Splitting a file must never be what makes a guard pass."
- Why: an extraction of its 370-line control-claim surface compiled but failed 24 single-file guards.
- Enforced by: CONVENTION
- Audit check: `git log -p -- Assets/_Game/Scripts/Editor/Tests` over a refactor range: any removed `Assert` line, or any guard switched from `TestSources.ReadClass` back to a narrower read, needs a written reason.
- Source: docs/CONVENTIONS.md:275, docs/CONVENTIONS.md:277, docs/STATE.md:65, docs/STATE.md:69
- Status: CURRENT

### ARCH-LAY-15 — Name every colour and juice timing that carries gameplay meaning
- Rule: A colour that carries gameplay meaning (carry level, health, a status, a removed chicken) is a named constant in `FeedbackTuning` (or a `FeedbackPaletteSO` entry it exposes), and every consumer uses it. The same goes for a juice timing. A texture pixel, a serialized prefab default, or an alpha rebuild of a colour it already has stays where it is. Do not add a serialized field to a shipped palette asset without re-authoring it, because the new field reads as black.
- Why: a single source of truth prevents silent disagreement between, for example, a bar and its number.
- Enforced by: TEST `WorldBarRampTests.TheCargoNumber_TakesTheBarsOwnRamp_AndNeitherHoldsALiteral`, `PlayerPaletteTests.NoOtherFile_DefinesItsOwnCopyOfThePlayerColours` (specific colours only); NONE in general
- Audit check: `git grep -n "new Color(" -- Assets/_Game/Scripts/Visuals Assets/_Game/Scripts/UI ':!*FeedbackTuning.cs' ':!*FeedbackPaletteSO.cs'`, then classify each hit against the exemptions above.
- Proposed guard: a counted allow-list scan of `new Color(` literals outside `FeedbackTuning`, `FeedbackPaletteSO` and `PlayerColors`, in the style of `SceneObjectAccessTests.AllowedFinds`.
- Source: docs/CONVENTIONS.md:261-262; M/feedback-icons-decisions-2026-11-07.md:15 (D3: colours in a config ScriptableObject)
- Status: PARTIAL (which colours belong in the palette SO and which in code constants is open question Q1)

## 3.2 Dependency injection, services and scene objects (ARCH-DI)

A dependency arrives one way (a `Construct()` parameter) and a scene object is found one way (a registry or a static accessor). Source guards in `SelfInjectTests` and `SceneObjectAccessTests` hold most of this. The gaps: service boundaries in Presentation, the `HasInstance` ban outside one file, and static fields that are never reset.

### ARCH-DI-01 — Put every service behind its interface
- Rule: "Never call Fusion, UGS, or Unity Input directly from gameplay code." Use `INetworkService`, `IUGSService`, `IAudioService`, `IInputProvider`, `ILogService`. Swapping an implementation is one `To<>()` binding in `ProjectInstaller`.
- Why: replaceability (TDD guiding priority 2): the backend, the network stack and audio can be swapped without touching game logic.
- Enforced by: COMPILER (only App references `Unity.Services.*`; Core and Gameplay do not reference `Unity.InputSystem`; pinned by `AssemblyLayoutTests.EveryAsmdef_KeepsExactlyItsReferences`); NONE for device reads in Presentation outside `Scripts/Input`, legacy `UnityEngine.Input`, and audio played around `IAudioService`
- Audit check: `git grep -nE "Keyboard\.current|Mouse\.current|Touchscreen\.current|Gamepad\.current|UnityEngine\.Input\.|Input\.Get" -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Input' ':!Assets/_Game/Scripts/Editor'` and `git grep -nE "PlayOneShot|PlayClipAtPoint" -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Audio' ':!Assets/_Game/Scripts/Editor'` should both print nothing (or only listed dev tools).
- Proposed guard: a `RuntimeFiles()` scan with a counted allow-list that bans device reads outside `Scripts/Input` and `PlayOneShot`/`PlayClipAtPoint` outside `Scripts/Audio`.
- Source: docs/CONVENTIONS.md:11, .claude/CLAUDE.md:65, docs/TDD.md:14, docs/TDD.md:61
- Status: PARTIAL (what "never call Fusion directly from gameplay" means, given that every gameplay object is a Fusion NetworkBehaviour, is open question Q3)

### ARCH-DI-02 — Bind services in the two installers only
- Rule: There are two contexts. `ProjectInstaller` is project scope (app-wide services, registries, log) and survives scene loads. `GameInstaller` is scene scope (Game.unity: `INetworkService`, `IArenaReadiness`).
- Why: one place to read every binding and to swap an implementation.
- Enforced by: NONE (true at `b7c2616e`: the only `MonoInstaller`s are `ProjectInstaller` and `GameInstaller`)
- Audit check: `git grep -n "MonoInstaller\|ScriptableObjectInstaller" -- Assets/_Game/Scripts` must list exactly `Scripts/Installers/ProjectInstaller.cs` and `Scripts/Installers/GameInstaller.cs`.
- Proposed guard: a reflection test asserting that the only `Zenject.InstallerBase` subclasses in `CluckWars.*` assemblies are those two.
- Source: docs/CONVENTIONS.md:25-27 (:28's contents are stale; see Appendix A), docs/TDD.md:61
- Status: CURRENT

### ARCH-DI-03 — Self-inject with the one SelfInject.Ensure line and no other form
- Rule: Fusion-spawned objects and `AddComponent`-ed components take dependencies through `[Inject] Construct(...)` and inject themselves with the single line `SelfInject.Ensure(this, _log != null)`. Pass the null check of a dependency that `Construct()` sets, and call it first, at the point the component first needs a dependency (`Spawned`, `Awake`). "Never write `<context>.Container.Inject(this)`, `FindFirstObjectByType<SceneContext>()` or `ProjectContext.Instance.Container.TryResolve<T>()` in runtime code."
- Why: the old project-only copy threw on the first scene-scoped parameter and crashed every chicken spawn (`14fbf8f`).
- Enforced by: TEST `SelfInjectTests.NoRuntimeFile_InjectsItselfOrLocatesAService_OutsideTheHelper` (Tests/SelfInjectTests.cs:222; exempts `Scripts/Installers/`, `SelfInject.cs` and the two renderer features); CONVENTION for the argument and the call position
- Audit check: `git grep -n "SelfInject.Ensure(this" -- Assets/_Game/Scripts`. For each hit, the second argument must test a field that `Construct` assigns, and the call must precede the first use of a dependency in that method.
- Source: docs/CONVENTIONS.md:32-50, docs/STATE.md:152
- Status: PARTIAL (`.claude/CLAUDE.md:66` still prescribes the banned form; see Appendix A)

### ARCH-DI-04 — Let only the two renderer features borrow the project logger
- Rule: `CWHazeFeature` and `MarkScreenFeature` are renderer-feature assets with no injection point. They "borrow the project logger for one error line". Each has a private static `Log.Error` that returns while `ProjectContext.HasInstance` is false (so a renderer never creates the ProjectContext in Edit mode), resolves `ILogService` through `ProjectContext.Instance.Container.TryResolve<ILogService>()`, and reports once per session. No other runtime file may do this.
- Why: an asset cannot self-inject, and a per-frame render error would otherwise flood the log or go unreported.
- Enforced by: TEST `SelfInjectTests.NoRuntimeFile_InjectsItselfOrLocatesAService_OutsideTheHelper` (the two files are allow-listed whole, Tests/SelfInjectTests.cs:227); CONVENTION for what the exempt files may do
- Audit check: after any edit to `Scripts/Visuals/CWHazeFeature.cs` (logger at :117-135) or `Scripts/Visuals/MarkScreenFeature.cs` (logger at :149-164), read the diff. The whole file is exempt from the guard, so a second container call there would pass unnoticed.
- Source: docs/CONVENTIONS.md:47, docs/STATE.md:162, Scripts/Visuals/CWHazeFeature.cs:117-135
- Status: PARTIAL (the `HasInstance` gate is not mentioned in `docs/CONVENTIONS.md`; open question Q2)

### ARCH-DI-05 — Never gate on ProjectContext.HasInstance
- Rule: `ProjectContext.HasInstance` is false until something reads `.Instance` (real bug `3bc2e13`). `SelfInject` reads `.Instance`, and "nothing else should need either".
- Why: a `HasInstance` gate silently skips injection on the first object that asks.
- Enforced by: TEST `TelegraphMaterialAndInjectionTests.RivalIndicator_SelfInjects_WithoutGatingOnHasInstance` (one file only); NONE elsewhere
- Audit check: `git grep -n "ProjectContext.HasInstance" -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'` must list only `CWHazeFeature.cs` and `MarkScreenFeature.cs` (ARCH-DI-04).
- Proposed guard: a `RuntimeFiles()` scan for `ProjectContext.HasInstance` with the two renderer features allow-listed.
- Source: docs/CONVENTIONS.md:53, Scripts/Injection/SelfInject.cs:44
- Status: PARTIAL

### ARCH-DI-06 — Register a scene installer's container on the first line
- Rule: A new scene installer registers its container as the first line of `InstallBindings` (`SelfInject.RegisterSceneContainer(this, Container)`), as `GameInstaller` does.
- Why: Zenject fills its `SceneContextRegistry` at the scene kernel's `Start`, after the `Awake` calls that self-inject.
- Enforced by: TEST `SelfInjectTests.TheSceneInstaller_RegistersItsContainer_BeforeItBindsAnything` (Tests/SelfInjectTests.cs:241; `GameInstaller` only); CONVENTION for any new installer
- Audit check: for every `MonoInstaller` other than `ProjectInstaller`, the first statement of `InstallBindings` is `SelfInject.RegisterSceneContainer(this, Container);`.
- Source: docs/CONVENTIONS.md:51
- Status: PARTIAL

### ARCH-DI-07 — Do not guard objects the container builds
- Rule: Put no `SelfInject` guard on an object the container builds. `FromNewComponentOnNewGameObject` keeps the object inactive until it is injected, so `Construct` runs before `Awake` (the diagnostics probes, `ArenaReadinessDriver`, `FusionNetworkService`).
- Why: the guard is dead code there; nine were removed (docs/STATE.md:152).
- Enforced by: CONVENTION
- Audit check: for each `FromNewComponentOnNewGameObject` binding in `Scripts/Installers/`, `git grep -n "SelfInject.Ensure" -- <bound type's file>` must print nothing.
- Source: docs/CONVENTIONS.md:52, docs/STATE.md:152
- Status: CURRENT

### ARCH-DI-08 — Expect SelfInject to report, never throw
- Rule: `SelfInject.Ensure` never throws. A missing context or an unresolvable parameter is reported once per component type (`ILogService.Error`, source `SelfInject`), and `Ensure` returns false. "Read that line first when a spawned object misbehaves."
- Why: a throw inside `Spawned` would abort the rest of the object's set-up.
- Enforced by: TEST `SelfInjectTests.AnUnresolvableDependency_IsReportedOncePerType_AndDoesNotThrow` (Tests/SelfInjectTests.cs:171), `SelfInjectTests.AProjectContextThatCannotBeCreated_IsReported_NotThrown` (:204)
- Source: docs/CONVENTIONS.md:50
- Status: CURRENT

### ARCH-DI-09 — Reach each kind of thing one way: Construct, registry, or static accessor
- Rule: "Do not reach the same object two ways." A service (`ILogService`, `INetworkService`, `IAudioService`, a registry SO, `MatchConfigSO`) is a `Construct()` parameter, never `Find*`, a static, or `ProjectContext...Resolve`. Many live objects of one kind come from the type's read-only registry (`ChickenController.ActiveControllers`, `ChickenCargo.ActiveCargos`, `ChickenCombat.ActiveCombats`, `ChickenMatchStats.ActiveStats`, `FoodPile.ActivePiles`, `PlayerBase.ActiveBases`, `AbilityZone.ActiveZones`). The one object of a kind comes from its static accessor (`GameManager.Instance`, `MapGenerator.Instance`, `MatchCamera.Instance`, `MatchHudController.Instance`, `TouchControlsController.Instance`, `AbilityTelegraph.Active`).
- Why: audit A13. Runtime `Find*` calls went from 43 to 23, and two access paths to one object had drifted.
- Enforced by: TEST `SceneObjectAccessTests.FindCalls_ExistOnlyInTheListedBootstrapSites_AndTheDevTools` (Tests/SceneObjectAccessTests.cs:113), `SceneObjectAccessTests.RegisteredAndSingletonObjects_AreNeverSearchedFor` (:190); CONVENTION for "never a static" for services
- Audit check: `git grep -nE "static [A-Za-z]+SO [A-Za-z]+ *\{|static [A-Za-z]+SO Current" -- Assets/_Game/Scripts`. Any static that hands out a service or SO needs a stated reason. The known one is `FeedbackPalette.Current`, set by `ProjectInstaller` because `FeedbackTuning` colours are static reads (Scripts/Installers/ProjectInstaller.cs:225-226).
- Source: docs/CONVENTIONS.md:76-84, docs/CONVENTIONS.md:91, docs/STATE.md:157, docs/STATE.md:159
- Ruling D07 (2026-11-14): a static accessor is allowed **only** for data whose readers are themselves static and cannot take an injected parameter (`FeedbackPalette.Current` read by `FeedbackTuning`/`AbilityAccent`, `AbilityTargeting.Mode` read by ability SOs), and only when an installer sets it once at startup. Anything with an instance reader takes a `Construct()` parameter.
- Status: PARTIAL

### ARCH-DI-10 — Make a registry an IReadOnlyList over a private list that only its owner mutates
- Rule: A registry is an `IReadOnlyList<T>` over a private list. Only the owning type adds and removes (`Register` / `Unregister`, private, called from `Spawned` / `Despawned`), and the owner clears the list on subsystem registration. A registry holds spawned objects only. Tests use `RegistryTestAccess` to insert a never-spawned chicken.
- Why: nothing else can add, remove or clear entries, and every entry has a valid `Object`.
- Enforced by: TEST `SceneObjectAccessTests.EveryRegistry_IsAReadOnlyView_ThatCannotBeCastBackToAList` (Tests/SceneObjectAccessTests.cs:33), `SceneObjectAccessTests.OnlyTheOwningType_CanRegister` (:54); COMPILER; CONVENTION for adding a new registry to those tests
- Audit check: `git grep -nE "static IReadOnlyList<" -- Assets/_Game/Scripts` and confirm every `Active*` registry appears in the registry list of `Tests/SceneObjectAccessTests.cs`.
- Source: docs/CONVENTIONS.md:86, docs/CONVENTIONS.md:88
- Status: PARTIAL

### ARCH-DI-11 — Iterate a registry by index
- Rule: `for (int i = 0; i < list.Count; i++)`. Never use `foreach` or LINQ over the interface.
- Why: a `foreach` or LINQ over the interface boxes an enumerator on every call, which is garbage in a per-frame path (the mobile target is 30 fps).
- Enforced by: TEST `SceneObjectAccessTests.NoRuntimeFile_EnumeratesARegistryWithForeachOrLinq` (Tests/SceneObjectAccessTests.cs:79)
- Source: docs/CONVENTIONS.md:87
- Status: CURRENT

### ARCH-DI-12 — Have a singleton set and clear itself, and reset every caching static
- Rule: A singleton sets itself in `Awake` / `OnEnable` / `Spawned` and clears itself (`if (Instance == this) Instance = null;` in `OnDestroy` / `OnDisable` / `Despawned`), plus a subsystem-registration reset. Callers check for null. "A static that caches an object is cleared on `SubsystemRegistration`."
- Why: so a session never inherits a reference the previous session destroyed (Enter Play Mode options skip the domain reload).
- Enforced by: TEST `SceneObjectAccessTests.EverySceneSingleton_ClearsItself_WhenItGoesAway` (Tests/SceneObjectAccessTests.cs:205; six listed singletons), `ChickenParticleBudgetTests.TheSharedFxMaterial_IsClearedOnSubsystemRegistration` (Tests/ChickenParticleBudgetTests.cs:72); NONE for every other static field
- Audit check: list static mutable fields with `git grep -nE "private static [^(=;]+ [_a-zA-Z]+ *(=|;)" -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor' | grep -v "readonly\|const"`, and for each declaring type confirm a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` method resets it.
- Proposed guard: a reflection test over the `CluckWars.*` types: every static non-readonly field, or static collection, requires a `SubsystemRegistration` method in the same type (with an allow-list for immutable caches).
- Source: docs/CONVENTIONS.md:89, docs/CONVENTIONS.md:461
- Status: PARTIAL

### ARCH-DI-13 — Check a Unity-object singleton with != null, never ?.
- Rule: "Check a Unity-object singleton with `!= null`, never `?.`." `MatchCamera.Instance?.ApplyShake()` compares the C# reference and calls into a destroyed object.
- Why: `?.` bypasses Unity's overloaded null check.
- Enforced by: TEST `SceneObjectAccessTests.NoRuntimeFile_UsesNullConditional_OnAUnityObjectSingleton` (Tests/SceneObjectAccessTests.cs:223)
- Source: docs/CONVENTIONS.md:90
- Status: CURRENT

### ARCH-DI-14 — Use Find* only at one-time bootstrap and in dev tools, never per frame
- Rule: `Find*` is for one-time bootstrap and for dev tools (`Diagnostics/` except `Diagnostics/Shared/`, `AbilityLab/`, editor code). "Never in `Update` / `LateUpdate` / `Render` / `FixedUpdateNetwork` / `OnGUI` or anything they call every frame." Every remaining call in game code is listed with its reason in `SceneObjectAccessTests.AllowedFinds`.
- Why: per-frame cost, and two ways to reach one object.
- Enforced by: TEST `SceneObjectAccessTests.FindCalls_ExistOnlyInTheListedBootstrapSites_AndTheDevTools` (Tests/SceneObjectAccessTests.cs:113), `SceneObjectAccessTests.TheAllowList_IsNotStale` (:130), `SceneObjectAccessTests.NoPerFrameMethod_SearchesTheScene` (:159)
- Source: docs/CONVENTIONS.md:91, docs/STATE.md:157, docs/STATE.md:162
- Status: CURRENT (one allow-listed per-frame exception: `MenuChickenStage` searches every scene Light once per frame while a stage camera renders, Tests/SceneObjectAccessTests.cs:102; the per-frame scan only sees bodies of methods with those exact names)

### ARCH-DI-15 — Hand a ScriptableObject its logger through AbilityContext.Log
- Rule: SOs have no injected `ILogService` and "must not acquire one through a service locator". The logger arrives on `AbilityContext.Log`, set by `AbilityController` from its own injected `_log`. Ability code calls `ctx.Log?.Error(...)`.
- Why: an SO has no injection point; this is the same channel as `Runner` and `PrefabRegistry`.
- Enforced by: TEST `SelfInjectTests.NoRuntimeFile_InjectsItselfOrLocatesAService_OutsideTheHelper` (bans container resolves in every runtime file, SOs included)
- Source: docs/CONVENTIONS.md:344-347
- Status: CURRENT

### ARCH-DI-16 — Bind static-data SOs by instance, with a runtime-empty fallback that warns
- Rule: Static-data SOs use `FromInstance`. If the inspector slot is null, bind a runtime-empty instance (`ScriptableObject.CreateInstance<T>()`) so consumers do not crash. The installer warns, and consumers handle empty fields gracefully.
- Why: no crash on an unassigned slot, and the warning is the signal that wiring broke.
- Enforced by: TEST `FeedbackPaletteTests.ProjectContextPrefab_BindsTheShippedPalette`, `LobbyMatchSettingsTests.ProjectContextPrefab_MatchConfigSlot_ResolvesToTheRealAsset` (two slots); CONVENTION for the pattern on every other slot
- Audit check: in `Scripts/Installers/ProjectInstaller.cs`, every `[SerializeField] private <X>SO` slot has a `!= null ? slot : ScriptableObject.CreateInstance<X>()` fallback next to a `log.Warn` that names the slot.
- Source: docs/CONVENTIONS.md:57-72
- Status: PARTIAL

### ARCH-DI-17 — Keep exactly one serialized reference to MatchConfig.asset
- Rule: Exactly one serialized reference to `MatchConfig.asset` exists: the `_matchConfig` slot on `ProjectContext.prefab`. "Never add a second `[SerializeField]` reference." A controller that needs match rules injects `MatchConfigSO`.
- Why: two references once let the lobby advertise "3:00 / 150 food" against the asset's 45 s / 40 food.
- Enforced by: TEST `LobbyMatchSettingsTests.ProjectContextPrefab_MatchConfigSlot_ResolvesToTheRealAsset` (the one slot holds the real asset); NONE for "no second reference"
- Audit check: `git grep -n "SerializeField" -- Assets/_Game/Scripts | grep MatchConfigSO` must print one line (Scripts/Installers/ProjectInstaller.cs:52). Also grep `MatchConfig.asset.meta`'s GUID across `*.unity`, `*.prefab` and `*.asset`, which must hit `ProjectContext.prefab` only.
- Proposed guard: an asset scan for that GUID in every scene, prefab and asset YAML, expecting exactly `ProjectContext.prefab`.
- Source: M/matchconfig-centralization.md:13-22, Scripts/Installers/GameInstaller.cs:25-27
- Status: PARTIAL

## 3.3 Networking and authority (ARCH-NET)

Photon Fusion 2 in Shared Mode: each peer is state authority over its own chickens, and the master client owns the world objects and the bots. Every foreign write is a sender-checked RPC, and every food transfer is confirmed by the owner. The RPC surface is heavily guarded. The gaps: the `FixedUpdateNetwork` gate, networked layout, unpinned enums, and runtime food conservation.

### ARCH-NET-01 — Open every FixedUpdateNetwork with the HasStateAuthority gate
- Rule: "Every `FixedUpdateNetwork` opens with `if (!HasStateAuthority) return;` unless it explicitly needs proxy-side simulation (almost never)." State writes are gated on `HasStateAuthority`.
- Why: forward compatibility with Server Mode; a proxy that simulates writes state it does not own.
- Enforced by: NONE (true at `b7c2616e`: 10 overrides, 9 open with the gate, and `AbilityController` records an authority change and then returns when not authoritative, Scripts/Gameplay/AbilityController.cs:332-341)
- Audit check: `git grep -n -A4 "override void FixedUpdateNetwork" -- Assets/_Game/Scripts`. Every body's first statement is the gate, or the `AbilityController` form.
- Proposed guard: a source test over every `FixedUpdateNetwork` override found in `RuntimeFiles()`: the first statement is `if (!HasStateAuthority) return;`, or the file is on an allow-list with a reason (`AbilityController`).
- Source: docs/CONVENTIONS.md:15, docs/CONVENTIONS.md:97-99, .claude/CLAUDE.md:67
- Status: CURRENT

### ARCH-NET-02 — Write foreign state only through an RPC to its state authority
- Rule: Damage, drain, deposit, steal: the caller is any client, and the write executes on the target's authority via `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]`. Never RPC presentation (rule set B.1).
- Why: in Shared Mode only the state authority's write replicates; a foreign write is lost or races.
- Enforced by: TEST for specific state only: `FoodTransferRulesTests.CargoIsCredited_OnlyFromASettlement_OrAFlushThatCouldNotBeSent` (Tests/FoodTransferRulesTests.cs:529), `ComebackRulesTests.NoOtherFile_AssignsEitherFlag` (Tests/ComebackRulesTests.cs:52); NONE in general
- Audit check: list `[Networked]` properties whose setter is not private (`git grep -nE "\[Networked\].*\{ *get; *set; *\}" -- Assets/_Game/Scripts`), then grep each property name for assignments outside its declaring file.
- Proposed guard: a reflection test that every `[Networked]` property setter in `CluckWars.*` is private (counted allow-list), so a foreign write does not compile.
- Source: docs/CONVENTIONS.md:15, docs/CONVENTIONS.md:101-108, .claude/CLAUDE.md:67; audit rule set B.1 (section 5)
- Status: CURRENT (at `b7c2616e`, 69 one-line `[Networked]` properties have a public `set`, so the compiler cannot help)

### ARCH-NET-03 — Give every RPC an RpcInfo and check the sender before any write
- Rule: "An `RpcSources.All` RPC is a claim from an arbitrary peer, so every one takes `RpcInfo info = default` and checks its sender before it changes anything."
- Why: audit A19; an unchecked RPC lets any peer push, reset or credit anyone.
- Enforced by: TEST `RpcClaimTests.EveryRpc_TakesTheSender` (Tests/RpcClaimTests.cs:87); gate-before-write ordering pinned for named RPCs by `RpcClaimTests.SelfImpulse_IsHonouredOnlyFromTheChickensOwnAuthority` (:99), `RpcClaimTests.ResetStats_IsMatchAuthorityOnly` (:112), `ControlRpcValidationTests.ControlRpcs_NameTheCaster_TakeRpcInfo_AndJudgeBeforeApplying` (Tests/ControlRpcValidationTests.cs:610), `ComebackRulesTests.Grant_IsSenderChecked_AndGameManagerUsesIt`; CONVENTION for a new RPC's ordering
- Audit check: for every `[Rpc(` added in a diff, read the body: a sender check (`RpcClaim.SenderSpeaksFor`, `ChickenController.IsFromMatchAuthority`, `AcceptOwnAuthority`) precedes the first write, and a refusal returns.
- Source: docs/CONVENTIONS.md:110, docs/STATE.md:79, docs/STATE.md:81
- Status: PARTIAL

### ARCH-NET-04 — Choose the sender check by what the RPC names; never compare with input authority
- Rule: When the RPC names another network object the sender acts for, use `RpcClaim.SenderSpeaksFor(named, info)`: a local invoke must be simulated here, and a remote one must come from the object's state authority (a bot's is the master). When it names nothing and only the match authority may send it, use `ChickenController.IsFromMatchAuthority(info, out reason)`. When a chicken only ever sends it to itself, use `AcceptOwnAuthority`. "Never compare with input authority: a bot has none."
- Why: comparing with input authority refused every bot steal from a remote human (`1fbc75c4`, docs/STATE.md:80).
- Enforced by: TEST `RpcClaimTests.NoFile_ComparesTheSenderWithAnAuthority_OutsideTheHelper` (Tests/RpcClaimTests.cs:137), `RpcClaimTests.ThiefAndDepositor_AreAttributedByTheOneRule` (:63)
- Source: docs/CONVENTIONS.md:112-116, docs/STATE.md:80
- Status: CURRENT

### ARCH-NET-05 — Bound an RPC's magnitude by what the registered abilities declare
- Rule: "A magnitude is bounded by what the registered abilities declare, never by a new tunable": a duration by `EmittedControl` (`ControlClaimRules.CeilingFor`), a steal by `NominalStealAmount`, a knockback by `EmittedKnockback` (`ControlClaimRules.KnockbackCeiling`). "An ability that sends an effect it does not declare is refused at the victim."
- Why: the receiver judges a claim against the real pool, so a forged value has nowhere to hide and no number is duplicated.
- Enforced by: TEST `ControlRpcValidationTests.Forged_DurationAboveTheCeiling_IsRefused` (Tests/ControlRpcValidationTests.cs:160), `ControlRpcValidationTests.EveryAbilityThatCallsAControlRpc_DeclaresThatEffect` (:688), `KnockbackClaimTests.KnockbackCeiling_IsTheStrongestDeclaredShove_OfTheShippedPool`, `KnockbackClaimTests.EveryAbilityThatSendsAKnockback_DeclaresItsStrength_FromItsOwnField`; CONVENTION for "never by a new tunable"
- Audit check: in a diff touching `ControlClaimRules`, `RpcClaim` or an `RPC_*` body, any new `[SerializeField]`, `const` or literal used as a bound is a violation.
- Source: docs/CONVENTIONS.md:118
- Status: PARTIAL

### ARCH-NET-06 — Take a sender's latency from ChickenCombat.SenderLatency, never raw GetPlayerRtt
- Rule: "A sender's latency is `ChickenCombat.SenderLatency(Runner, info)`": 0 for a local invoke, otherwise floored at 0.15 s and capped at 0.5 s. `ChickenCombat.PeerLatency` is the only caller of `Runner.GetPlayerRtt` in gameplay code. Steal, deposit and peck range use it.
- Why: `Runner.GetPlayerRtt` reads 0 for a remote player in Shared Mode, which refused honest remote pecks at 1.0-1.1 m (docs/STATE.md:98).
- Enforced by: TEST `SenderLatencyTests.OnlyOnePlaceInGameplay_ReadsTheRawRtt` (Tests/SenderLatencyTests.cs:116; scans Gameplay, Abilities, Networking), `SenderLatencyTests.TheReceiver_TakesTheFlooredSenderLatency` (:108)
- Source: docs/CONVENTIONS.md:119, docs/STATE.md:83, docs/STATE.md:98
- Status: CURRENT (Scripts/App/FusionNetworkService.cs:260 reads the local peer's own RTT for diagnostics, outside the rule; docs/STATE.md:99 is stale, see Appendix A)

### ARCH-NET-07 — Make every food transfer authority-confirmed: the owner debits, then reports
- Rule: Peck, steal and deposit all follow "receiver asks and records, owner debits and reports the real amount, only the report credits" (`FoodTransferRules`, decision D3a). Every request carries a ushort id that the report echoes (`PendingStealLedger`, `FoodRequests<TKey>`). Open requests reserve room (`ChickenCargo.FreeSpace`). A refused steal reports 0. A late pile or base report with no open request is honoured, capped at one bite (`ChickenCargo.MaxPeck`) or one flush (`BaseDepositRules.MaxSingleDeposit`). A STEAL report later than `FoodTransferRules.SettleWindowSeconds` (2.5 s) is refused, and that food is lost (known, accepted). What does not fit goes to the bounty bag (`FoodTransferRules.Place`).
- Why: an optimistic credit duplicates or destroys food whenever the owner disagrees (audit A02).
- Enforced by: TEST `FoodTransferRulesTests.CargoIsCredited_OnlyFromASettlement_OrAFlushThatCouldNotBeSent` (Tests/FoodTransferRulesTests.cs:529), `FoodTransferRulesTests.Peck_AsksThePile_AndIsCreditedOnlyByItsReport` (:457), `FoodTransferRulesTests.Steal_AsksTheVictim_EveryExitThatKnowsTheThiefReports` (:482), `FoodTransferRulesTests.Deposit_IsAnsweredWithItsId_AndNeverDroppedOnTheFloor` (:503), `StealProofTests.EveryCallerOfTheStealRpcs_GoesThroughTheOneGate`; NONE for total-food conservation in a running match
- Audit check: `git grep -nE "Cargo \+=|Cargo -=" -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'` and review any new transfer path. `AssassinExecute`'s whole-load `RPC_TransferAllToBountyBag` is a separate, documented transfer (docs/STATE.md:90).
- Proposed guard: a pure simulation test that drives `FoodRequests<TKey>`, `PendingStealLedger` and `FoodTransferRules` through scripted request, report, timeout and bounce sequences and asserts pile + cargo + bank + bag is constant; or a conservation check in `InvariantProbe` once in-flight requests can be counted.
- Source: docs/STATE.md:194, docs/STATE.md:197-198, M/arch-audit-decisions-2026-11-12.md:16; rule set B.1
- Status: PARTIAL

### ARCH-NET-08 — Keep the comeback flags private-set and granted by one RPC
- Rule: `UnderdogSurgeActive` / `LeaderBountyActive` are private-set. The only grant is `ChickenController.RPC_GrantComeback(ComebackGrant)`, which runs on the state authority and accepts the match authority only (via `IsFromMatchAuthority`).
- Why: audit A01; the flags change scoring.
- Enforced by: TEST `ComebackRulesTests.Flag_SetterIsPrivate` (Tests/ComebackRulesTests.cs:43), `ComebackRulesTests.NoOtherFile_AssignsEitherFlag` (:52), `ComebackRulesTests.Grant_IsSenderChecked_AndGameManagerUsesIt` (:101)
- Source: docs/STATE.md:190
- Status: CURRENT

### ARCH-NET-09 — Accept round-reset RPCs from the match authority only
- Rule: `ChickenCargo.RPC_ResetForNewMatch`, `ChickenCombat.RPC_ResetForNewMatch`, `RPC_ResetControlStates` and `ChickenMatchStats.RPC_ResetStats` reject, before any state change, a call that is neither a local invoke nor from the match authority.
- Why: A19 / review R-7; a forged reset wipes another player's round.
- Enforced by: TEST `ComebackRulesTests.TheRoundResets_OfCargoAndCombat_AreSenderCheckedToo` (Tests/ComebackRulesTests.cs:80), `ComebackRulesTests.TheReset_ThatClearsTheFlags_IsSenderCheckedToo` (:66), `RpcClaimTests.ResetStats_IsMatchAuthorityOnly` (Tests/RpcClaimTests.cs:112)
- Source: docs/STATE.md:171, docs/STATE.md:197, docs/STATE.md:81
- Status: CURRENT

### ARCH-NET-10 — Judge control and knockback claims before applying, and send them only through the primitives
- Rule: `RPC_ApplyStun` / `RPC_ApplyRoot` / `RPC_ApplyAbilitySlow` / `RPC_ApplyKnockbackFrom` name the caster, take `RpcInfo`, and judge the claim (`ControlClaimRules.EvaluateSource` / `EvaluateKnockback`, the base-wall answer) before applying. "Nothing else calls" them except `ChickenController.SendControl` / `SendControlFrom` / `KnockAway` (ARCH-DATA-05). The unvalidated `RPC_ApplyKnockback(impulse)` was removed. `RPC_ApplySelfImpulse` is own-authority only.
- Why: an unvalidated RPC lets an arbitrary peer push or stun anyone.
- Enforced by: TEST `ControlRpcValidationTests.ControlRpcs_NameTheCaster_TakeRpcInfo_AndJudgeBeforeApplying` (Tests/ControlRpcValidationTests.cs:610), `ControlRpcValidationTests.EveryCallSite_PassesTheCastingChicken` (:646), `KnockbackClaimTests.SpineCoatContact_NamesItsWearer` (Tests/KnockbackClaimTests.cs:224), `KnockbackClaimTests.TheUnvalidatedKnockbackRpc_IsGone`
- Source: docs/CONVENTIONS.md:236, docs/STATE.md:81
- Status: CURRENT

### ARCH-NET-11 — Replicate loadouts as registry codes and validate them on proxies
- Rule: A chicken's four slots and passive replicate as `AbilityController.LoadoutCodes` (5 bytes; `AbilityRegistrySO.CodeOf` / `FromCode`, 0 = none). Spawners set slots only through `SetSlots`. A proxy applies a replicated passive only if it is legal for the chicken's class, and a slot only if it is the kit's (Fixed) or class-pool / pre-equip legal (Free). A refused value falls back to the class default with a rate-limited Warn (`LoadoutClaimRules`).
- Why: `GameManager.AwardMatchEndBonuses` reads every chicken's passive on the master, so a forged loadout changes the score.
- Enforced by: TEST `LoadoutReplicationTests.EveryRegisteredAbilityAndPassive_RoundTripsThroughItsCode` (Tests/LoadoutReplicationTests.cs:22), `LoadoutReplicationTests.TheRegistry_HasNoDuplicate_AndFitsAByte` (:42), `LoadoutReplicationTests.EverySpawner_GoesThroughSetSlots` (:128), `LoadoutClaimRulesTests.AbilityController_JudgesEveryResolvedEntry_BeforeItUsesIt` (Tests/LoadoutClaimRulesTests.cs:110)
- Source: docs/STATE.md:192-193
- Status: CURRENT

### ARCH-NET-12 — Set networked properties in onBeforeSpawned; never despawn inside it
- Rule: Set `[Networked]` properties via `onBeforeSpawned` so the value replicates from tick zero. Initialise a non-zero default on the authority in `Spawned` (`if (HasStateAuthority && VisualOpacity <= 0f) VisualOpacity = 1f;`). "Don't despawn from inside `onBeforeSpawned`."
- Why: the callback runs before `Spawned()` on any peer; despawning mid-`Spawn` is not safe.
- Enforced by: CONVENTION
- Audit check: `git grep -n "onBeforeSpawned" -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'`. Read each lambda: no `Despawn`, and no early `return` before the object's `LifetimeTimer` or its required fields are set.
- Source: docs/CONVENTIONS.md:195-211, docs/CONVENTIONS.md:338-340, .claude/CLAUDE.md:76
- Status: CURRENT

### ARCH-NET-13 — Give networked and serialized enums a sane zero, byte backing and an ordinal pin
- Rule: Every `[Networked]` enum's zero member is the correct uninitialised state. Networked enums are byte-backed (`ChickenClass : byte`: "Don't change"). Networked and serialized enums are append-only, and "numeric values never move". Pins exist for `ZoneEffect`, `ControlVfx`, `ControlKind`, `ControlState`, `AbilityCategory`, `ChickenClassFlags`, `ChickenClass`, `ChickenSubclass`, `AbilityJob`, `AccentKey`, the input bits and two `BotRole` values.
- Why: Fusion zero-initialises networked state, and every peer, asset and wire byte reads the ordinal.
- Enforced by: TEST `ContractsAndEnumsTests.NetworkedEnums_ZeroMember_IsTheCorrectUninitialisedState` (Tests/ContractsAndEnumsTests.cs:109), `ContractsAndEnumsTests.NetworkedEnums_AreByteBacked` (:33), `ContractsAndEnumsTests.ChickenClass_KeepsItsAuthoredNumbering` (:82), `EffectWindowTests.ZoneEffect_IsPinned` (Tests/EffectWindowTests.cs:96), `EffectWindowTests.ControlKind_AndControlState_ArePinned` (:103), `AbilityTraitTests.AbilityJob_ValuesArePinned` (Tests/AbilityTraitTests.cs:119), `ChickenClassTableTests.ChickenClassFlags_ValuesArePinned` (Tests/ChickenClassTableTests.cs:98); NONE for unpinned ones (`MatchEventKind` beyond `None == 0`, `ComebackGrant`, `CastArchetype`) and for any new enum
- Audit check: for each enum type used as a `[Networked]` property, an `[Rpc]` parameter or a serialized SO field, `git grep -n "<EnumName>" -- Assets/_Game/Scripts/Editor/Tests` should show an ordinal pin (`Pin(` or per-value asserts).
- Proposed guard: a reflection test that collects the enum types of every `[Networked]` property and `[Rpc]` parameter in `CluckWars.*` and requires each to appear in one central pinned-enum table, naming any newcomer.
- Source: docs/CONVENTIONS.md:189-193, docs/CONVENTIONS.md:245, docs/CONVENTIONS.md:257, docs/CONVENTIONS.md:375-377, .claude/CLAUDE.md:70, Scripts/Abilities/AbilityJob.cs:8-10, docs/STATE.md:87
- Status: PARTIAL

### ARCH-NET-14 — Never change networked layout to tidy code
- Rule: Do not delete, retype or reorder a `[Networked]` property to make code tidier. `Rooted` stays a `[Networked] bool` beside `RootTimer` (folding it into `EffectWindow` deletes a networked property), and the four ability slot timers are NOT a `NetworkArray`.
- Why: a layout change is a protocol change that EditMode cannot validate; it needs a live multi-client session, and every peer must run the same build.
- Enforced by: TEST `EffectWindowTests.TheNetworkedWindows_KeepTheirTypeAndOrder` (Tests/EffectWindowTests.cs; `ChickenController`'s windows only); NONE elsewhere
- Audit check: `git diff <base>..<head> -- '*.cs' | grep -n "\[Networked\]"`. Every added, removed or moved networked property in a refactor range must be called a protocol change in its commit and in docs/STATE.md, with a live check owed.
- Proposed guard: a snapshot test that reflects every NetworkBehaviour's `[Networked]` property names, types and declaration order into a pinned table, updated deliberately alongside a protocol-change note.
- Source: docs/STATE.md:56, docs/STATE.md:69
- Status: PARTIAL

### ARCH-NET-15 — Hold a timed window on a chicken in a networked TickTimer read through EffectWindow
- Rule: A timed window on a chicken (stun, root, the immunities, steal-proof) is a `[Networked] TickTimer` read and granted through `EffectWindow.Remaining` / `IsOpen` / `TryGrant`.
- Why: one path per primitive; a hand-written remaining or grant body drifts in its rounding and its later-end-wins rule.
- Enforced by: TEST `EffectWindowTests.NoWindowBody_IsWrittenOutInTheController` (Tests/EffectWindowTests.cs:64), `EffectWindowTests.EveryWindowRead_IsOneCall`, `EffectWindowTests.EveryGrant_IsOneCall` (all `ChickenController` only); CONVENTION elsewhere
- Audit check: `git grep -n "TickTimer" -- Assets/_Game/Scripts/Gameplay ':!Assets/_Game/Scripts/Gameplay/ChickenController.cs'`. A new per-chicken effect window outside `ChickenController` must also go through `EffectWindow`.
- Source: docs/CONVENTIONS.md:239, docs/STATE.md:87
- Status: PARTIAL

### ARCH-NET-16 — Give every networked object a NetworkTransform or a stamped position, and check it on a client
- Rule: A new or changed networked object needs a `NetworkTransform` (or a stamped position). It is added to the `World` hash, and its position is checked on a client, not only on the host.
- Why: `PlayerBase` / `FoodPile` without a NetworkTransform stood at the prefab root on clients, so remote players could never bank (CW-216).
- Enforced by: TEST `SpawnPlacementTests.EverySpawnedNetworkObjectPrefab_HasANetworkTransformOrAStampedSpawnPosition`, `ProjectConfigTests.ChickenPrefab_HasEveryComponentTheGameplayLoopResolves` (Tests/ProjectConfigTests.cs:177); CONVENTION for the client-side check
- Audit check: for a new networked prefab, a multi-client run (`tools/run-clients.ps1`) whose client log shows the object's position matching the host's (`World` hash equal).
- Source: docs/TEST_STRATEGY.md:56-57, M/test-session-2026-10-20-learnings.md:16
- Status: PARTIAL

### ARCH-NET-17 — Read game input only from the Fusion input buffer
- Rule: Game logic reads input only from the Fusion buffer (`GetInput<PlayerNetworkInput>(out var input)` inside `FixedUpdateNetwork`). "Never `Keyboard.current` from gameplay scripts."
- Why: one input path for keyboard, touch and bots, carried under input authority.
- Enforced by: COMPILER (Gameplay does not reference `Unity.InputSystem`); NONE for legacy `UnityEngine.Input`, which every assembly can see
- Audit check: `git grep -nE "Input\.Get|Input\.mouse|Input\.touch|UnityEngine\.Input" -- Assets/_Game/Scripts/Gameplay Assets/_Game/Scripts/Abilities Assets/_Game/Scripts/Networking` must print nothing.
- Proposed guard: a `RuntimeFiles()` scan of the Gameplay assembly's folders that bans `UnityEngine.Input` and `Input.Get*`.
- Source: docs/CONVENTIONS.md:13, docs/TDD.md:183
- Status: PARTIAL

### ARCH-NET-18 — Read every input provider every tick; never read a press getter for diagnostics
- Rule: `CompositeInputProvider` reads every provider every tick instead of short-circuiting. The ability press getters are one-shot: diagnostics must not read them, because that consumes the latch and swallows the cast `FusionNetworkService` was about to send. Watch `GetAbilityHeld(slot)` instead. Since 2026-11-20 `FusionNetworkService` gates the Peck slot's press through `PeckLongPressRules` (a tap sends on release, a long press flips Auto-Peck instead), but still reads every getter every tick, so the gate sits after the read, never in place of it.
- Why: edge-triggered presses must not leak into the next tick or be eaten by a probe.
- Enforced by: TEST `ServicesAndInputTests.Composite_ReadsEveryProvider_EvenAfterOneAlreadyReturnedTrue`, `PeckLongPressTests.TheNetworkInputPath_RoutesThePeckSlotThroughTheGate` (Tests/PeckLongPressTests.cs:221); CONVENTION for diagnostics
- Audit check: `git grep -nE "GetAbility[0-9]?Pressed|Ability[0-9]Pressed" -- Assets/_Game/Scripts/Diagnostics Assets/_Game/Scripts/AbilityLab` must print nothing.
- Source: docs/TESTING.md:212, docs/TESTING.md:374-379
- Status: PARTIAL

### ARCH-NET-19 — Keep the duplicated SessionNameSeed copies identical
- Rule: The session-name seed hash exists in two deliberate copies (`MapGenerator` and `CornerAssignment`) that must stay identical so every peer builds the same walls and corners.
- Why: peers that disagree on the seed generate different arenas.
- Enforced by: TEST `ContractsAndEnumsTests.SessionNameSeed_IsIdentical_InMapGeneratorAndCornerAssignment` (Tests/ContractsAndEnumsTests.cs:186)
- Source: docs/TESTING.md:211
- Status: CURRENT

### ARCH-NET-20 — Keep the simulation identical in solo and online; branch on SessionMode only for seeds and session setup
- Rule: The simulation must behave identically whether the match is solo or online. Game logic never branches on `SessionMode`. Two kinds of code may: **seed derivation** (every peer must reach the same number without talking) and **session setup** (which connection to open, and what a seat can know before join order exists). The four sites that do so today are the whole allow-list: `CornerAssignment.SeedFor` (solo rolls a menu seed, online hashes the session name), `CornerAssignment.LobbySeatCorner` (a guest returns -1 because its join order is not decided yet), `MapGenerator.BuildInteriorObstacles` (online seeds the obstacle layout from the session name so every peer builds the same arena, solo uses a random seed), and `MatchBootstrapper` (host, join or solo connection, and the corner permutation it seeds from them).
- Why: ruling D06 (2026-11-14). The rule as inherited (`docs/TDD.md:145`, "game logic never branches on `SessionMode`") was broken at four sites and so taught nothing, but its intent is intact: none of the four changes how the game plays, only how a shared number is derived or which connection is opened. Stating the intent plus the allow-list makes it enforceable; the absolute wording was not.
- Enforced by: NONE
- Audit check: `git grep -n "SessionMode" -- Assets/_Game/Scripts` outside `Scripts/Services` and `Scripts/App`. Every hit must be one of the four named sites, or be seed derivation or session setup with that reason written at the site. A branch that changes a gameplay value, a duration, a rate or a spawn count is a violation whatever it is called.
- Proposed guard: a source test over the runtime folders that lists the allowed `SessionMode` call sites by file and member, the way `SceneObjectAccessTests.AllowedFinds` lists the allowed `Find*` sites, so a fifth site has to be argued for in the test rather than appearing silently.
- Source: docs/TDD.md:145 (the inherited wording), Assets/_Game/Scripts/Gameplay/CornerAssignment.cs:39, Assets/_Game/Scripts/Gameplay/CornerAssignment.cs:49, Assets/_Game/Scripts/Gameplay/MapGenerator.cs:613, Assets/_Game/Scripts/Gameplay/MatchBootstrapper.cs:114-127
- Status: CURRENT

## 3.4 Replicated moments and presentation (ARCH-EVT)

Presentation (visuals, animation, audio, HUD) learns about gameplay by reading replicated state. It is never told by an RPC or by a call from the tick. One-shot moments travel as networked byte event ids, read by one shared per-chicken observer, and payloads carry ids only so language packs can swap text and voice (decision D2a). The observer side is well tested. The producer side and the UI-facing rules are mostly review only.

### ARCH-EVT-01 — Keep animation, VFX, audio cues and HUD reactions local and never RPC them
- Rule: `ChickenAnimator` plays on every peer from replicated state. "Nothing animation-related is RPC'd, and gameplay never calls the animator." VFX, audio cues and HUD reactions follow the same pattern: observed from replicated state, never RPC'd. Audio is always local, "never synced over the network".
- Why: an RPC'd cue arrives late, plays twice or plays for the wrong audience; replicated state already says what happened.
- Enforced by: TEST `GameplayPresentationBoundaryTests.Gameplay_CallsNoOneShotPresentation` (Tests/GameplayPresentationBoundaryTests.cs:87), `CastPresentationTests.Gameplay_NoLongerPlaysTheCastAnimationOrSound_SourceGuard`; NONE for presentation inside an RPC body in a Presentation-assembly behaviour
- Audit check: `git grep -n -A25 "\[Rpc(" -- Assets/_Game/Scripts | grep -nE "Animator|SetTrigger|PlaySFX|ApplyShake|ParticleSystem|FloatingText"` must print nothing.
- Proposed guard: run the pattern list of `Gameplay_CallsNoOneShotPresentation` over every `[Rpc]` method body found by reflection in every `CluckWars.*` assembly, extracted with `AssassinExecuteCreditTests.MethodBody`.
- Source: docs/CONVENTIONS.md:17-18, docs/TDD.md:281, docs/TDD.md:389, .claude/CLAUDE.md:72
- Status: PARTIAL

### ARCH-EVT-02 — Announce a one-shot moment by bumping a networked byte event id, and never reset it
- Rule: "A one-shot moment is a `[Networked] byte XEventId` bumped by the state authority, with whatever ids describe it written to networked fields BEFORE the bump" (`LastCastSlot` / `LastCastHitCount`, then `LastCastEventId++`). The value wraps at 255 by design. "Never reset one (writing 0 is itself an edge)."
- Why: one pattern for every presentation moment (D2a); a late joiner or a missed frame still sees exactly one edge.
- Enforced by: TEST, observer side only: `ChickenLocalEventsTests.TheByteWrap_255To0_FiresOnce`, `ChickenLocalEventsTests.SeveralBumps_BetweenTwoObservations_FireOnce`, `ChickenLocalEventsTests.ALateJoiner_InheritingNonZeroIds_RaisesNothing`; NONE for the producer side (write-before-bump, never reset)
- Audit check: `git grep -nE "EventId\s*=\s*\(?byte\)?\s*0\b|EventId\s*=\s*0\b" -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'` must print nothing (true at `b7c2616e`). For each `EventId++`, the describing fields are written on the lines above it.
- Proposed guard: a source scan that bans assigning a literal to any `*EventId` property, plus a `MethodBody` ordering test per bump site (describing fields before `++`).
- Source: docs/CONVENTIONS.md:126, M/arch-audit-decisions-2026-11-12.md:15; rule set B.3
- Status: PARTIAL

### ARCH-EVT-03 — Watch the counters in exactly one per-chicken observer; presentation keeps no cursor
- Rule: `ChickenLocalEvents`, reached through `ChickenController.LocalEvents`, is the only watcher of the event counters. It seeds each baseline on first observation (late-join safe), re-seeds on spawn and despawn, and raises plain C# events (`Cast`, `Knockback`, `SelfImpulse`, `Jump`, `Stolen`, `Deposited`). "Presentation subscribes; no component keeps its own `_last*EventId` cursor."
- Why: audit A12; a dozen private cursors disagreed on first observation and on wrap.
- Enforced by: TEST `ChickenLocalEventsTests.NoPresentationComponent_ReadsAOneShotCounterItself` (Tests/ChickenLocalEventsTests.cs:449; scans `Visuals/`, `UI/`, `Input/`, `AbilityLab/`, `Audio/`), `ChickenLocalEventsTests.TheController_ObservesInRender_AndReSeedsOnSpawnAndDespawn` (:492); CONVENTION for `Diagnostics/`, `App/`, `Installers/`
- Audit check: `git grep -nE "(LastCast|Knockback|SelfImpulse|Jump|Steal|Deposit)EventId" -- Assets/_Game/Scripts/Diagnostics Assets/_Game/Scripts/App Assets/_Game/Scripts/Installers` must print nothing.
- Source: docs/CONVENTIONS.md:127, docs/STATE.md:174, docs/STATE.md:116
- Status: PARTIAL

### ARCH-EVT-04 — Carry ids, enums and numbers in event payloads, never display strings or clips
- Rule: Payloads are small structs of ids, enums and numbers (`CastEvent`, `StealEvent`). "Never a localized string, never an `AudioClip`." The listener resolves text through `UiText` / `UiKeys` and sound through a typed `SfxId`. `CastEvent` carries registry codes (`AbilityCode` / `AimedAbilityCode`), not `AbilityBaseSO` references; listeners resolve them through `ChickenLocalEvents.AbilityOf`. Owner ruling 2026-11-12: "Events carry ids only."
- Why: so a language pack swaps text and voice without touching gameplay.
- Enforced by: TEST `ChickenLocalEventsTests.TheCastEvent_HoldsNoAssetReference_OnlyIdsEnumsAndNumbers` (Tests/ChickenLocalEventsTests.cs:250), `ChickenLocalEventsTests.TheObserver_IsGameplaySide_AndKnowsNoPresentationType` (:479; no `string` or `AudioClip` in `ChickenLocalEvents.cs` / `EventEdge.cs`); CONVENTION for local events declared in other files
- Audit check: `git grep -nE "event (System\.)?Action<[^>]*(string|AudioClip|Sprite)" -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'` must print nothing.
- Source: docs/CONVENTIONS.md:128, docs/STATE.md:156, M/arch-audit-decisions-2026-11-12.md:15
- Status: PARTIAL

### ARCH-EVT-05 — Keep gameplay from calling presentation; raise a local event instead
- Rule: "Code in `FixedUpdateNetwork` changes replicated state and nothing else: no animator, VFX, camera, audio one-shot or floating text." A moment gameplay notices in `Render` from continuous state is raised as a local C# event by the behaviour that owns that state (`ChickenLocalEvents.RaiseCargoChanged` / `RaiseAttackerNamed` / `RaiseZoneTriggered`, `ChickenCombat.OnDeath` / `OnRespawn`). No sound is played from the tick.
- Why: Visuals depends on Gameplay, not the reverse; a sound played from the tick plays on whichever peer holds state authority (for bots, the master).
- Enforced by: COMPILER (Gameplay cannot name a Presentation type); TEST `GameplayPresentationBoundaryTests.Gameplay_CallsNoOneShotPresentation` (Tests/GameplayPresentationBoundaryTests.cs:87), `GameplayPresentationBoundaryTests.TheRemoval_IsAnnouncedByEvents_AndTheZone_ByLocalRaises` (:131), `AbilityCueTests.TheAbilityController_PlaysNoSound` (Tests/AbilityCueTests.cs:67)
- Source: docs/CONVENTIONS.md:129, docs/CONVENTIONS.md:165, docs/STATE.md:176, docs/STATE.md:180; rule set B.3
- Status: CURRENT

### ARCH-EVT-06 — Read continuous replicated state with a ChangeDetector on its own behaviour, or poll it
- Rule: Continuous replicated state uses a `ChangeDetector` in `Render` on the NetworkBehaviour that declares the property (`ChickenCombat.IsRemoved`, `ChickenCargo.Cargo`), or is polled by presentation (slot active mask, control flags, opacity). "The detector only resolves for the behaviour that declares the property. It is for state, not for one-shot counters."
- Why: Fusion's detector resolves per declaring behaviour, and a counter read through it misses wraps and late joins.
- Enforced by: CONVENTION
- Audit check: `git grep -n "GetChangeDetector" -- Assets/_Game/Scripts`. For each hit, the properties it reads are declared in the same class, and none is an `*EventId`.
- Source: docs/CONVENTIONS.md:130, docs/CONVENTIONS.md:167-185; rule set B.2
- Status: CURRENT

### ARCH-EVT-07 — Pair every += on a local event with a -=
- Rule: Pair every `+=` on a local event with a `-=`: `OnEnable` / `OnDisable` on the chicken's own components, bind / unbind when a HUD changes its chicken, and unsubscribe in `Despawned` / `OnDestroy`.
- Why: a dangling handler keeps a destroyed listener alive and fires into it.
- Enforced by: TEST `ChickenLocalEventsTests.EveryWatcher_PairsItsSubscriptions` (Tests/ChickenLocalEventsTests.cs:464; seven listed watchers), `RoundResetSignalTests.ZoneAndDecoy_SubscribePerSpawn_AndDespawnOnlyInTheirOwnAuthorityTick` (Tests/RoundResetSignalTests.cs:157); CONVENTION for other subscribers
- Audit check: for every file in a diff that adds `+= On`, count `git grep -c " += " <file>` against `git grep -c " -= " <file>`, and read any mismatch.
- Source: docs/CONVENTIONS.md:132-147; rule set B.4
- Status: PARTIAL

### ARCH-EVT-08 — Latch an event and call Observe() when the listener must act at its own point in the frame
- Rule: `ChickenController.Render` observes once a frame. A listener that must act at its own point in the frame does not act in the handler: it latches the event, calls `_events.Observe()` at that point, then consumes the latch.
- Why: whichever call sees the counter move first raises the event, so the listener acts in the same frame, at the same point, as if it polled the byte itself.
- Enforced by: CONVENTION
- Audit check: a handler of a `ChickenLocalEvents` event that does more than set a latch must be one of the documented direct handlers (animator cast/peck trigger, the two own sounds, steal attribution: docs/STATE.md:178).
- Source: docs/CONVENTIONS.md:149-161, docs/STATE.md:178
- Status: CURRENT

### ARCH-EVT-09 — Isolate a throwing listener, and report a raise nobody hears
- Rule: `ChickenLocalEvents` keeps one `ListenerList` per event. `LocalRaise` calls each subscriber on its own, logs a throw (`ILogService.Error`, event plus listener type, one line per 5 s) and carries on. Every local raise says when nobody listens (one Warn per event and chicken kind).
- Why: one throwing listener must not silence the others, and a raise nobody hears is a wiring bug.
- Enforced by: TEST `ChickenLocalEventsTests.AListenerThatThrows_IsReported_AndEveryOtherListenerStillGetsTheBeat`, `ChickenListenerWiringTests.TheRaisers_ReportASilentRaise_SourceGuard` (Tests/ChickenListenerWiringTests.cs:131), `ChickenListenerWiringTests.TheChickenPrefab_CarriesEveryListenerTheGameplayEventsRelyOn` (:48)
- Source: docs/STATE.md:153, docs/STATE.md:155
- Status: CURRENT

### ARCH-EVT-10 — Let only the human who drives a chicken hear its own sounds
- Rule: A chicken's own sounds (cast, deposit, hit-confirm, whiff, cancel, fizzle, cargo full, expire) follow `OwnSoundRules`: only the human who drives that chicken hears them, never a bot's, a rival's or a decoy's.
- Why: a sound played from gameplay plays on the state authority, which for bots is the master; that is the bug this rule replaced.
- Enforced by: TEST `CastPresentationTests.OwnSounds_AreHeardOnlyByTheHumanWhoDrivesTheChicken` (Tests/CastPresentationTests.cs:158), `CastPresentationTests.TheOwnSounds_AreKeyedById_AndGatedByTheAudienceRule_SourceGuard` (:192), `AbilityCueTests.HitFeedback_PlaysEveryCue_ById_ForTheOwnSoundAudience` (Tests/AbilityCueTests.cs:85)
- Source: docs/CONVENTIONS.md:163, docs/STATE.md:179
- Status: CURRENT

### ARCH-EVT-11 — Latch an own-player cue in the tick, raise it in Render, play it by SfxId
- Rule: Cancel, fizzle, cargo-full and expire need no replication, but are still not played from the tick. The tick latches an `AbilityCue` (`AbilityCueLatch`, a local field), `AbilityController.Render` raises it the same frame through `ChickenLocalEvents.RaiseAbilityCue`, and `HitFeedback` plays it by `SfxId`.
- Why: playing from the tick broke ARCH-EVT-05, and an `SfxId` reports an empty slot once.
- Enforced by: TEST `AbilityCueTests.TheCue_ReachesItsListeners_AndSaysWhenNobodyListens` (Tests/AbilityCueTests.cs:52), `AbilityCueTests.TheAbilityController_PlaysNoSound` (:67)
- Source: docs/CONVENTIONS.md:165, docs/STATE.md:154
- Status: CURRENT

### ARCH-EVT-12 — Find the local chicken through ChickenController.Local, never by scanning for input authority
- Rule: "The chicken this peer drives" is `ChickenController.Local` (`LocalDriven` for device input, which is never a bot), resolved by the pure `LocalChickenRules.Pick`. "Do not scan for `HasInputAuthority`: a Doppelganger decoy shares its caster's."
- Why: a decoy was taken for the local chicken by range guides, the HUD and touch controls.
- Enforced by: TEST `LocalChickenRulesTests.NoCopyOfTheRule_ScansTheSceneForInputAuthority` (Tests/LocalChickenRulesTests.cs:51; eight listed files; `FusionNetworkService` left the list on 2026-11-20 because it no longer resolves the local chicken at all), `LocalChickenRulesTests.ADecoyThatSharesInputAuthority_IsSkipped_WhicheverComesFirst`; CONVENTION for files outside the list
- Audit check: `git grep -n "HasInputAuthority" -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'`. No hit may be a "which chicken is mine" loop over chickens. `ArenaReadinessDriver` is the one documented exception (docs/STATE.md:172).
- Source: docs/CONVENTIONS.md:19, docs/STATE.md:172; rule set B.6
- Status: PARTIAL

### ARCH-EVT-13 — Reach chicken presentation through the presenter interface Gameplay declares
- Rule: Gameplay declares `IChickenModelPresenter`. Presentation implements it (`ChickenModelPresenter`), bound in `ProjectInstaller` and taken `[InjectOptional]` by `ChickenController.Construct`. Presentation reaches per-chicken visuals (`Nameplate()`, `Indicator()`, `StatusBadges()`, `PopAnchor()`) through the extension methods in `Scripts/Visuals/ChickenPresentationAccess.cs`.
- Why: this is the worked example of "put an interface in the lower layer" (ARCH-LAY-01).
- Enforced by: COMPILER; TEST `GameplayPresentationBoundaryTests.NoGameplayFile_ImportsVisualsOrUi` (Tests/GameplayPresentationBoundaryTests.cs:102)
- Source: docs/CONVENTIONS.md:494, docs/STATE.md:132, Scripts/Gameplay/ChickenController.cs:601
- Status: CURRENT

### ARCH-EVT-14 — Let UI poll state, and use a reset static mailbox for cross-HUD cues
- Rule: Presentation to UI: the UI polls in `Update`. A cue that crosses HUD components goes through a static mailbox that is reset on `SubsystemRegistration`.
- Why: rule set B.5, adopted 2026-11-12; a pushed UI update couples gameplay timing to UI lifetime.
- Enforced by: CONVENTION
- Audit check: `git grep -nE "static event|static Action" -- Assets/_Game/Scripts/UI Assets/_Game/Scripts/Visuals Assets/_Game/Scripts/Input`. Each declaring type has a `SubsystemRegistration` reset, and no UI controller is called from a gameplay file (the compiler blocks Gameplay; check Diagnostics and App by hand).
- Source: audit rule set B.5 (owner thread 2026-11-12, section 5); not yet written in docs/CONVENTIONS.md
- Status: CURRENT

### ARCH-EVT-15 — Route UI actions through a public method that checks authority itself
- Rule: UI to gameplay: call a public method on the gameplay owner, which checks authority itself. UI does not send RPCs or write networked state directly.
- Why: rule set B.7, adopted 2026-11-12; the authority check belongs with the state it protects.
- Enforced by: CONVENTION
- Audit check: `git grep -nE "\.RPC_[A-Za-z]+\(" -- Assets/_Game/Scripts/UI Assets/_Game/Scripts/Input Assets/_Game/Scripts/Visuals` must print nothing.
- Source: audit rule set B.7 (owner thread 2026-11-12, section 5); not yet written in docs/CONVENTIONS.md
- Status: CURRENT

## 3.5 Abilities, effects and classes as data (ARCH-DATA)

Code outside the ability folder asks an ability what it does, never which class it is. Each gameplay primitive has one path. Each chicken class, and each status-effect vocabulary, has one table with a totality test. Adding an ability costs its class plus the registry asset; adding a class costs seven code files (docs/STATE.md:91). The gaps: switches on enums outside the tables, literal tunables in runtime code, and the text that quotes numbers.

### ARCH-DATA-01 — Add an ability as an AbilityBaseSO subclass plus a registry entry, and nothing else
- Rule: A new ability extends `AbilityBaseSO`, is registered in `AbilityRegistrySO`, and authors `AbilityCategory` + `BotRole`. Adding one costs its class plus the registry asset, "0 other code files".
- Why: up to 20 consumers used to be keyed on the concrete type (docs/STATE.md:91).
- Enforced by: TEST `AbilitySystemTests.Registry_ContainsEveryAbilityAsset_AndNothingElse` (Tests/AbilitySystemTests.cs:44), `AbilitySystemTests.Registry_HasNoNullOrDuplicateEntries` (:63), `AbilitySystemTests.EveryAbility_ResolvesToAConcreteBotRole` (:186), `AbilitySystemTests.AbilityIconStyle_CoversEveryConcreteAbilityType` (:316)
- Source: .claude/CLAUDE.md:75, docs/STATE.md:91
- Status: CURRENT

### ARCH-DATA-02 — Ask an ability what it does, never which class it is
- Rule: Outside `Scripts/Abilities`: "No `is` / `as` / `typeof` on a concrete `XxxAbilitySO` and no `GetType().Name`." Ask a trait instead: `Job` (`ability.Is(AbilityJob.Shield)`, false for null), `GrantsStealProof`, `IsStealth`, `EmittedControl`, `EmittedKnockback`, `NominalStealAmount`, `HasFollowUpPhase` / `FollowUpPhaseSeconds`, `TryGetAreaEffect`, `IconId`. Traits are virtual properties on `AbilityBaseSO`, overridden in code ("not a serialized field, so they cannot drift from the behaviour").
- Why: 63 concrete type checks in 22 files were the reason adding an ability touched many files (audit A15).
- Enforced by: TEST `AbilityTraitTests.NoFileOutsideTheAbilityFolder_AsksWhichClassAnAbilityIs` (Tests/AbilityTraitTests.cs:155; allow-list `AssassinExecute`, `AutoTestConfig`), `AbilityTraitTests.TheGuard_SeesATypeCheck_AndTheAllowListIsStillNeeded` (:181), `AbilityTraitTests.Job_AnswersExactlyAsTheTypeCheckItReplaced_ForEveryAbility` (:61)
- Source: docs/CONVENTIONS.md:219-226, docs/STATE.md:85
- Status: CURRENT (the job list at docs/CONVENTIONS.md:223 omits `SlowArea`; see Appendix A)

### ARCH-DATA-03 — Answer a new question with a new trait and a test row
- Rule: "A new consumer that needs a new question adds a trait to the base, with a row in `AbilityTraitTests`." `PassiveAbilitySO` is an abstract family with its own API and may be tested for.
- Why: this keeps the type-check ban workable without exceptions.
- Enforced by: CONVENTION (the ban in ARCH-DATA-02 forces the trait; the test row is review only)
- Audit check: a diff that adds a `public virtual` member to `Scripts/Abilities/AbilityBaseSO.cs` must also touch `Tests/AbilityTraitTests.cs`.
- Source: docs/CONVENTIONS.md:228
- Status: CURRENT

### ARCH-DATA-04 — Give each job one ability; a new effect needs a new job
- Rule: One ability per job, with the same name and icon for every chicken that has it. Classes differ through stats, cooldown reduction and the passive. "A new slow-like effect needs its own job." `AbilityJob` is append-only.
- Why: for a children's game, one icon must mean one effect (owner, 2026-11-05).
- Enforced by: TEST `AbilityTraitTests.TheShippedPool_HasOneAbilityPerJob` (Tests/AbilityTraitTests.cs:103), `AbilityTraitTests.AbilityJob_ValuesArePinned` (:119)
- Source: M/ability-merge-2026-11-05.md:12, M/slow-split-2026-11-13.md:18, Scripts/Abilities/AbilityJob.cs:8-10
- Status: CURRENT

### ARCH-DATA-05 — Compose the one path per gameplay primitive; never re-implement it
- Rule: An ability's `OnActivate` composes these primitives and does not re-implement them. Shove: `caster.KnockAway(victim, strength)`, declaring `EmittedKnockback`. Steal: `StealFrom(thief, victim, nominalAmount)` -> `ChickenCargo.StealFrom` (steal-proof gate, `StealMath.Clamp`, request; `RequestSteal` is private), declaring `NominalStealAmount`. Stun, root, slow: `SendControl(caster, victim)` -> `ChickenController.SendControl` (applies the caster's passive through `ControlClaimRules.OutgoingSeconds`: stuns and slows lengthened, roots never); a zone acting for its owner uses `SendControlFrom`. Lasting area: set `AreaKind`, override `TryGetAreaEffect`, call `SpawnArea(ctx)`. Timed window: `EffectWindow` (ARCH-NET-15).
- Why: each primitive applies the caster's passive and the receiver's ceiling through one predicate, so a copy drifts.
- Enforced by: TEST `KnockbackClaimTests.PairedKnockback_NamesItsCaster` (Tests/KnockbackClaimTests.cs:215), `StealProofTests.ThiefSite_ChecksStealProof_BeforeCreditingItself` (Tests/StealProofTests.cs:143; listed thief files), `StealProofTests.TheOneStealEntryPoint_Gates_Clamps_ThenAsks`, `ControlRpcValidationTests.EveryCallSite_PassesTheCastingChicken` (Tests/ControlRpcValidationTests.cs:646); CONVENTION for a thief or shover file not in those lists
- Audit check: `git grep -nE "StealMath\.Clamp|RequestSteal|RPC_Apply(Stun|Root|AbilitySlow|KnockbackFrom)" -- Assets/_Game/Scripts/Abilities` must print nothing. `AssassinExecute`'s whole-load bounty transfer is the documented exception (docs/STATE.md:90).
- Source: docs/CONVENTIONS.md:232-241, docs/STATE.md:84, docs/STATE.md:90
- Status: PARTIAL

### ARCH-DATA-06 — Refuse a spawning or area ability's press in CanActivate, before any cost
- Rule: "The base `CanActivate` already refuses the press when the zone prefab chain is missing; an override adds its checks after `base.CanActivate(ctx)`." Every ability that spawns overrides `CanActivate`, so a missing prefab refuses the press before the cooldown is spent. `OnDeactivate` is virtual and empty: override it only to undo what `OnActivate` switched on.
- Why: refusing before the side effect leaves nothing half-applied (ARCH-ERR-04).
- Enforced by: TEST `AbilityWiringGateTests.EverySpawningAbility_OverridesCanActivate` (Tests/AbilityWiringGateTests.cs:334), `AbilityWiringGateTests.SpawnerKey_CoversEveryAbilityThatSpawns` (:369), `AbilityWiringGateTests.EveryAreaAbility_RefusesWithoutTheAbilityZonePrefab`; CONVENTION for the "after base" ordering
- Audit check: `git grep -n -A3 "override bool CanActivate" -- Assets/_Game/Scripts/Abilities`. In each override, the first condition evaluated is `base.CanActivate(ctx)`.
- Source: docs/CONVENTIONS.md:237, docs/CONVENTIONS.md:241
- Status: PARTIAL

### ARCH-DATA-07 — Use Planar for ground-plane maths, but keep exact-boundary range verdicts inline
- Rule: Use `Planar.Distance` / `SqrDistance` / `Flat` / `Forward` / `DirectionOr` (Core, root namespace `CluckWars`). "Do not write another private planar helper, and do not write `v.y = 0f` instead of `Planar.Flat(v)`." "One measured exception: a range verdict keeps its own inline `dx * dx + dz * dz <= limit * limit`."
- Why: computing both sides of a comparison in one method is not bit-for-bit the same as comparing `Planar.SqrDistance`'s returned float; routing five pure rule files through it refused a thief standing at exactly the reach.
- Enforced by: TEST `PlanarTests.NoRuntimeFile_DefinesItsOwnPlanarHelper` (Tests/PlanarTests.cs:79), `PlanarTests.NoRuntimeFile_FlattensAVectorInline` (:91), `SenderLatencyTests.ALocalSteal_HasNoAllowance` (the exception, steal only); CONVENTION for the other exact-boundary rule files
- Audit check: `git grep -n "Planar.SqrDistance\|Planar.Distance" -- Assets/_Game/Scripts/Gameplay/StealRules.cs Assets/_Game/Scripts/Gameplay/BaseDepositRules.cs Assets/_Game/Scripts/Gameplay/ControlClaimRules.cs Assets/_Game/Scripts/Gameplay/AreaEffectRules.cs Assets/_Game/Scripts/Gameplay/BaseWallRules.cs` must not show a range verdict (`<= limit`) built on them.
- Source: docs/CONVENTIONS.md:238, docs/STATE.md:68
- Status: PARTIAL

### ARCH-DATA-08 — Define each chicken class once, in ChickenClasses; never switch on ChickenClass
- Rule: `ChickenClasses` (`Scripts/Gameplay/ChickenClassInfo.cs`) holds, per class, the ability-mask flag (`1 << class`), the forage rule, the wording keys, the bot profile and the solo roster. "Do not switch on `ChickenClass` for any of these." Tuned numbers stay on `ChickenStatsSO`. Per-class presentation data beside its consumer (`MenuUiController` tints and tiles, `PodiumLayout.StaticHeadAnchor`, `UiAudioSO.CluckFor`) must answer for every class. Adding a class = enum value (append only), flag bit, `ChickenClasses` entry, stats asset, and whatever the test then lists.
- Why: adding a class cost 14 code files before; now 7.
- Enforced by: TEST `ChickenClassTableTests.EveryClass_HasAnEntry_AndAll_IsTheEnum` (Tests/ChickenClassTableTests.cs:27), `ChickenClassTableTests.EveryRemainingPerClassTable_AnswersForEveryClass` (:183), `ChickenClassTableTests.TheSoloBotRoster_IsOneList` (:124); NONE for "never switch on ChickenClass" (0 `case ChickenClass.` in runtime code at `b7c2616e`)
- Audit check: `git grep -nE "case ChickenClass\.|ChickenClass\.[A-Za-z]+ *(\?|=>)" -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor' ':!Assets/_Game/Scripts/Gameplay/ChickenClassInfo.cs'` and judge each hit.
- Proposed guard: a source scan with a counted allow-list for `case ChickenClass.` and `switch` on a `ChickenClass` outside `ChickenClassInfo.cs`.
- Source: docs/CONVENTIONS.md:245, docs/STATE.md:86, docs/STATE.md:91
- Status: PARTIAL

### ARCH-DATA-09 — Translate status effects through the one StatusEffects table
- Rule: `StatusEffects` (`Scripts/Visuals/StatusEffects.cs`) is the one row-per-effect table relating `ControlKind`, `ControlState`, `ControlVfx`, `ChickenStatusBadges.Badge` and `ZoneEffect`, and the one place a badge's glyph, word, countdown rule and text colour are declared. "Translate through it; do not write a sixth switch." `Default` in a column means "no counterpart in that vocabulary". Gameplay keeps its own facts-to-enum computations, pinned against the table.
- Why: five enums name the same effects in different orders, and three of them are networked.
- Enforced by: TEST `StatusEffectTableTests.EveryBadge_HasExactlyOneRow` (Tests/StatusEffectTableTests.cs:33), `StatusEffectTableTests.EveryControlKind_MapsOrIsExplicitlyNone` (:45), `StatusEffectTableTests.TheThreeControlEffects_AgreeAcrossAllFiveVocabularies`, `StatusEffectTableTests.EveryAbilityTheGameShips_EmitsAKindTheTableKnows` (:160); NONE for "no sixth switch"
- Audit check: `git grep -nE "case (ControlKind|ControlState|ZoneEffect|ControlVfx|Badge)\." -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor' ':!Assets/_Game/Scripts/Visuals/StatusEffects.cs'`. Each hit is either a pinned Gameplay computation or a violation.
- Proposed guard: a counted allow-list scan for `case` labels of those five enums outside `StatusEffects.cs`.
- Source: docs/CONVENTIONS.md:253-257
- Status: PARTIAL

### ARCH-DATA-10 — Make every enum-keyed table total and pin a replaced mapping
- Rule: A table keyed by an enum answers for every value, or marks the value explicitly as having no counterpart, and a test iterates `Enum.GetValues` to prove it. When a switch or hand table is replaced by a table, the old mapping is written once in the test and compared for every value.
- Why: a new enum value cannot be forgotten, and a refactor cannot silently change an answer.
- Enforced by: CONVENTION (applied in `StatusEffectTableTests`, `ChickenClassTableTests`, `MissingSfxTests.EverySoundId_ResolvesToItsOwnRegistrySlot`)
- Audit check: for any new `switch` on an enum, or `Dictionary<SomeEnum, ...>`, in a diff: `git grep -n "Enum.GetValues(typeof(<Enum>))" -- Assets/_Game/Scripts/Editor/Tests` should find a totality test.
- Source: docs/CONVENTIONS.md:245, docs/CONVENTIONS.md:255; audit test patterns (section 6, "Abilities, effects and classes as data")
- Status: CURRENT

### ARCH-DATA-11 — Keep gameplay numbers in data, and tune by changing the asset only
- Rule: Tuned numbers live on the SO that owns them (`ChickenStatsSO`, the ability SOs, `MatchConfigSO`); "keep kits and rule numbers in data". "When tuning a value, change the asset only; the text follows." An RPC bound uses declared values, "never a new tunable" (ARCH-NET-05).
- Why: one source per number; the balance oracle and the player-facing text read the same asset.
- Enforced by: NONE (only specific values are pinned: ARCH-LAY-10)
- Audit check: `git grep -nE "[^A-Za-z0-9_.][0-9]+\.[0-9]+f" -- Assets/_Game/Scripts/Gameplay Assets/_Game/Scripts/Abilities Assets/_Game/Scripts/UI`, then review the hits that are not `const` / `static readonly` declarations or SO field initialisers. Known example: `bool shortCd = ab.Cooldown <= 6f;` at Scripts/UI/MenuUiController.GearUp.cs:508.
- Proposed guard: a counted allow-list scan of float literals in arithmetic and comparisons in Gameplay, Abilities and UI, outside constant declarations and SO field initialisers.
- Source: docs/CONVENTIONS.md:245, M/fixed-kits-baseline-2026-10-31.md:29, M/slow-split-2026-11-13.md:18
- Note (owner, 2026-11-17): the asset stays the source, with one sanctioned second source: the server-written BalanceBook delta over the assets (ARCH-DATA-14). A delta that proves out is promoted into the assets later (decision D5, deferred; BL-183, BL-184).
- Status: CURRENT

### ARCH-DATA-12 — Fill every number in ability and passive text from the SO field it describes
- Rule: "No number in player-facing ability or passive text is hard-coded: each comes from the SO field it describes, in the simplest words, and the text mentions a second press where one exists." Owner, 2026-11-13: "Text should use the values in SO variables to be always updated." The text mechanics (templates, `UiText`, `UiKeys`) are in CODE_RULES.md.
- Why: text that drifts from the assets misleads players (the Hunt text quoted the kill range as the stun range).
- Enforced by: TEST `AbilityDescriptionTests.EveryRegisteredAbilityAndPassive_TemplatesHoldNoLiteralDigits_AndFillEveryPlaceholder`, `AbilityDescriptionTests.Templates_ContainNoHandTypedNumbers`, `AbilityDescriptionTests.AssetDescriptionField_IsTheRenderedText`, `AbilityDescriptionTests.EveryArgument_IsARealFieldValue_ToDisplayPrecision`, `PerkTextTests.PerkLines_CarryTheNumbersTheAssetFieldsHold`; NONE for a new passive's raw `Description` prose, an argument read from the wrong field, and digits in other `UiText.csv` rows
- Audit check: for each passive asset under `Assets/_Game/Data/Abilities/Passives/`, any digit in its `Description:` line must match a field of the same asset. In `Assets/_Game/Resources/Text/UiText.csv`, list rows with a digit outside `{...}` and confirm each is an ordinal, not a game-config number.
- Proposed guard: give each `DescriptionArgs` entry the field name it reads and assert the argument equals THAT field; make `PerkTextTests.PassiveDescriptions_ThatQuoteNumbers_QuoteTheCurrentOnes` fail on an unknown passive type; add a csv-wide digit test with a short allow-list.
- Source: M/slow-split-2026-11-13.md:13, docs/STATE.md:16; owner thread 2026-11-13 (balance thread)
- Status: PARTIAL

### ARCH-DATA-13 — Keep the retired ability classes until alpha feedback
- Rule: Keep the 19 retired ability classes until alpha feedback settles the kits, then delete what is not returning (D4a; audit finding A18 waits).
- Why: owner decision 2026-11-12; a retired ability may come back.
- Enforced by: CONVENTION
- Audit check: `git log --diff-filter=D --name-only <base>..HEAD -- Assets/_Game/Scripts/Abilities` shows no `*AbilitySO.cs` deleted before the alpha feedback is in.
- Source: M/arch-audit-decisions-2026-11-12.md:17
- Status: CURRENT

### ARCH-DATA-14 — Change shipped balance only through the BalanceBook delta, which clients read and never write
- Rule: A balance change that ships without an app update is one JSON delta from UGS Remote Config (key `balance`; the `dev` environment only where `DevTools.Available`, `production` otherwise), applied by `BalanceBook` over the loaded assets. Only number, bool and enum fields and kit slots, minus a deny-list (loadout legality, classification, `MaxPlayers`, `TargetingMode`, `LoadoutMode`), are overridable. A bad value refuses the whole delta; a newer `schema` is ignored. The delta applies at boot or in the menu, never after the first lobby or match of a launch. Clients only read from the service: no client holds a write credential, writes to UGS or any network service, or publishes, in any build. One narrow exception (owner ruling 2026-11-18): in dev-tools builds only (`DevTools.Available`) a local balance editor may apply a delta on that device through `BalanceBook`, with the same `Evaluate` validation and a new origin for local, only while the apply window is open (menu, before the first lobby or match of the launch; otherwise the delta is stored and applies at next launch), and may persist it in a dev-only local file on that device with a clear/reset. The owner copies the delta JSON and its `bal=` hash and publishes through `/balance publish dev` on the PC as before. Production builds cannot reach the local path: it has its own `DevTools` gate inside `BalanceBook`, and its editor is bound only in `ProjectInstaller.InstallDiagnostics`. Class stats and cooldowns are checked against `BalanceOracle` (SCT, ±0.5 s; CODE-DESIGN-01, CODE-DESIGN-02) before a local apply, and a failing proposal is blocked. The source sits behind `IBalanceSource` (Core), and only `RemoteConfigBalanceSource` (App) references the package. Peers whose 8-hex `bal=` hashes differ get a warning only (owner D3c); each judges claims against its own copy (ARCH-NET-05).
- Why: owner rulings 2026-11-17 (D1c, D2c, D3c, D4b): balance updates without a build, written only by the owner and designed for future live-ops, not as a client cheat panel. Owner ruling 2026-11-18 ("Sí, excepción estrecha"; "que el cambio sea local, pero yo pueda consultar y copiar el valor cuando quiera"): the dev-tools-only local editor lets the owner try a change on a device and copy the value, while the service stays write-protected from the app.
- Enforced by: TEST `BalanceBookTests` (round trips, whole-delta refusal, schema gate, deny-list, hash, snapshot/restore, `NullBalanceSource`, environment choice); `AssemblyLayoutTests` (the package only in App's pinned list). The local-path exception: TEST `DevBalanceEditorTests` (`EveryLocalEntryPointOfTheBook_ChecksTheDevToolsGateFirst`, `TheLocalFileStore_GatesEveryOperationInsideItself`, `TheOverrideService_ChecksTheGateInApplyAtBootApplyAndReset`, `TheOverrideAndTheEditor_AreBoundOnlyInTheDevToolsInstallPaths`, `TheEditor_RefusesToLiveOrOpenWithoutDevTools`, `TheMenuSeesOnlyTheInterface_AndTheButtonHidesWithoutOne`, `NothingInTheLocalPath_TalksToANetworkServiceOrLogsThroughDebug`) and `BalanceLocalTests` (`LocalAllowed_IsExactlyTheDevToolsAnswer`, `TheStore_IsInertWithoutDevTools_AndWorksWithThem`), all in Assets/_Game/Scripts/Editor/Tests/. Note: the SCT check (`BalanceSctCheck`) runs only in the Editor until a baked oracle map exists; player builds block SCT-relevant edits.
- Audit check: `git grep -n "Unity.Services.RemoteConfig" -- Assets/_Game/Scripts` hits only `App/RemoteConfigBalanceSource.cs`; `git grep -n "FromJsonOverwrite" -- Assets/_Game/Scripts ':!*Editor*'` hits only `Balance/BalanceBook.cs`; `git grep -nE "UGS_SERVICE_|services\.api\.unity\.com" -- Assets` hits nothing; the local-apply entry point in `BalanceBook` checks `DevTools.Available` and the editor is bound only in `ProjectInstaller.InstallDiagnostics`.
- Source: docs/STATE.md (BalanceBook entry, 2026-11-17); decision page https://claude.ai/artifact/NpJhf84c1ckj7qJthRzC7Q; owner ruling 2026-11-18 (narrow local-editor exception; to be filed in MemPalace by the caller)
- Status: CURRENT

## 3.6 Logging and error surfacing (ARCH-ERR)

One log path, a fixed line format, and one rule for which failures are loud: a wiring bug must reach `ILogService.Error`, while a legal game state must stay silent. The log path is fully guarded. The sorting rule itself is judgement, so it is review only.

### ARCH-ERR-01 — Send all log output through ILogService
- Rule: "All log output goes through `ILogService`. No ad-hoc `Debug.Log`." This bans `Debug.Log*` and `Debug.Assert*` in runtime code outside `Scripts/Logging`. Pass a `Source` tag on every call. `ProjectInstaller` builds the log service first and reports its own missing slots through it.
- Why: one path that obeys `_logMinLevel`, carries the standard prefix, and can be merged across devices (DI-6).
- Enforced by: TEST `RuntimeSourceGuardTests.NoRuntimeFile_CallsDebugLog_OutsideTheLoggingService` (Tests/RuntimeSourceGuardTests.cs:56), `RuntimeSourceGuardTests.TheDebugLogGuard_MatchesWhatItIsMeantToCatch` (:65)
- Source: docs/CONVENTIONS.md:14, .claude/CLAUDE.md:68, docs/STATE.md:151
- Status: CURRENT

### ARCH-ERR-02 — Keep the log line format, and lead an error with its Source line
- Rule: Every line is `[HH:mm:ss.fff][mm:ss.fff][Level][Source] message`. `UnityLogService.Error` leads with its `[Source]` line, with the exception under it. "Each subsystem uses a consistent tag."
- Why: phone, EXE and Editor logs merge on one timeline, and `tools/test-session/merge.ps1` parses them.
- Enforced by: TEST `LogErrorFormatTests.AnErrorWithACause_LeadsWithTheSourceLine_AndStillCarriesTheCause` (Tests/LogErrorFormatTests.cs:20); CONVENTION for tag consistency
- Audit check: `git grep -nE "const string (Source|LogSource|Tag) *=" -- Assets/_Game/Scripts`, then compare the tags with the table at docs/TESTING.md:519-541. That table is partly stale (it still lists removed UGUI HUDs).
- Source: docs/TESTING.md:451, docs/TESTING.md:519-541, docs/STATE.md:61
- Status: PARTIAL

### ARCH-ERR-03 — Sort every null guard by whether a legal game state can produce it
- Rule: "Ask whether the null could ever be produced by a legal game state." If it is reachable only through an unassigned serialized field or a missing prefab component, it is a wiring bug. It "must reach `ILogService.Error`", and the message must name which reference is null and what the player just lost. If it is reachable in a correctly built game (nobody in range, cargo full, `victim == null`, `spaceLeft <= 0f`), it is a legitimate no-op and "must stay silent". An unassigned slot found at install time is reported through the log service and obeys `_logMinLevel`.
- Why: a sweep that surfaces every early return buries real errors under per-cast noise. Someone will read the line in `adb logcat` with no debugger attached.
- Enforced by: CONVENTION
- Audit check: `git grep -nE "== null\) (return|continue)" -- Assets/_Game/Scripts/Abilities Assets/_Game/Scripts/Gameplay Assets/_Game/Scripts/Visuals`. Classify each hit: a wiring-only null without an `Error` naming the reference and the loss is a finding; a legal-state null that logs is a finding too.
- Source: docs/CONVENTIONS.md:283-299, M/silent-failure-vs-legitimate-noop.md:8-23, docs/STATE.md:161
- Status: CURRENT

### ARCH-ERR-04 — Refuse before the side effect rather than reporting after it
- Rule: "Move the guard ahead of the side effect entirely." `AbilityBaseSO.CanActivate(AbilityContext)` runs inside `AbilityController.TryActivate` before the slot's `ActiveMask` bit, active timer or `SetCooldown`: "Reach for this first." The `_ctx.Runner` / `_ctx.PrefabRegistry` refresh must happen before `CanActivate`. Do not route the guard through `IsUsable`, which turns a silent failure into a permanently greyed-out button with no explanation.
- Why: nothing half-applied, no cooldown charged, no misleading evidence.
- Enforced by: TEST `AbilityWiringGateTests.EverySpawningAbility_OverridesCanActivate` (Tests/AbilityWiringGateTests.cs:334; spawning abilities, see ARCH-DATA-06); CONVENTION for every other side effect
- Audit check: in `Scripts/Gameplay/AbilityController.cs` `TryActivate`, the context refresh precedes `CanActivate`, which precedes the first write to `ActiveMask`, the active timer or `SetCooldown`. A new ability whose `OnActivate` returns early after a visible change is a finding.
- Source: docs/CONVENTIONS.md:303-313, M/silent-failure-vs-legitimate-noop.md:37-41
- Status: PARTIAL

### ARCH-ERR-05 — Surface a failure after a side effect or a Spawn; never roll back, never despawn in onBeforeSpawned
- Rule: A guard that trips after a side effect ranks higher, and one that trips after a `Spawn` has committed ranks higher still, because it leaks an inert `NetworkObject` whose `LifetimeTimer` was never set. "Don't roll back the side effect to make the failure tidy": where you cannot gate ahead, "the `Error` line is the fix". "Don't despawn from inside `onBeforeSpawned`."
- Why: a rollback deletes working behaviour; despawning mid-`Spawn` is not safe.
- Enforced by: CONVENTION
- Audit check: `git grep -n -A15 "onBeforeSpawned" -- Assets/_Game/Scripts/Abilities Assets/_Game/Scripts/Gameplay`. Any `return` inside the lambda before the `LifetimeTimer` is set must be preceded by an `ILogService.Error`, and no `Despawn` may appear there.
- Source: docs/CONVENTIONS.md:315-340
- Status: CURRENT

### ARCH-ERR-06 — Log an RPC rejection as one rate-limited Warn with the stable prefix
- Rule: "A rejection is one rate-limited Warn through an `RpcRejectionLimiter`" (one line per sender per 2 s, one instance per RPC family), with the stable prefix `rejected RPC_` that `tools/test-session/merge.ps1` counts (0 with honest clients).
- Why: a forged or buggy peer must not flood the log, and the session tooling counts these lines as invariant findings.
- Enforced by: TEST `RpcClaimTests.NoGameplayFile_BuildsItsOwnRejectionRateLimiter` (Tests/RpcClaimTests.cs:148), `ControlRpcValidationTests.ControlRpcs_NameTheCaster_TakeRpcInfo_AndJudgeBeforeApplying` (prefix pinned for the control RPCs only); CONVENTION for the prefix on other RPC families
- Audit check: `git grep -n "rejected RPC_" -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'`. Every `RPC_*` refusal path logs through an `RpcRejectionLimiter` with that exact prefix.
- Source: docs/CONVENTIONS.md:120, docs/TEST_STRATEGY.md:62-64
- Status: PARTIAL

### ARCH-ERR-07 — Report a missing sound clip; keep silence only where it is designed and say so
- Rule: `UnityAudioService.PlaySFX(null)` plays nothing and Warns once per call site; a typed `SfxId` with no clip Warns once, by name. "Every own sound goes through `IAudioService.PlaySFX(SfxId)`." "Do not pass clips through null guards or `AudioRegistrySO.TryPlay` unless silence is the designed behaviour of an optional slot (and say so there)."
- Why: audit A08; a missing clip is a wiring bug that is otherwise inaudible.
- Enforced by: TEST `MissingSfxTests.ANullClip_IsReportedOncePerCallSite_NotPerCall`, `MissingSfxTests.EverySoundId_WithNoClip_IsReportedOnce_ByName` (Tests/MissingSfxTests.cs:124), `MissingSfxTests.EverySoundId_ResolvesToItsOwnRegistrySlot` (:91); CONVENTION for "say so there"
- Audit check: `git grep -nE "TryPlaySFX|\.TryPlay\(" -- Assets/_Game/Scripts ':!Assets/_Game/Scripts/Editor'`. Each site needs a comment saying the silence is designed. Known sites: `MarkFeedback`, `TouchControlsController`, `ChickenShellVisuals` (docs/STATE.md:162).
- Source: docs/CONVENTIONS.md:72, docs/CONVENTIONS.md:165, docs/STATE.md:61, docs/STATE.md:173
- Status: PARTIAL

### ARCH-ERR-08 — Look up UXML elements through UiElements so a missing element is reported
- Rule: Use `UiElements.Bind` / `Query` instead of a bare `root.Q<T>(name)` "that skips a missing element in silence"; they report a renamed UXML element once per name through `ILogService`. Use `UiElements.SetShown(element, shown)` instead of an inline `style.display = cond ? Flex : None`. `UiElements.EnsureEventSystem()` is the one allowed `Find*` for the EventSystem.
- Why: a renamed element otherwise breaks a screen with no log line.
- Enforced by: NONE (`EnsureEventSystem` is pinned by `SceneObjectAccessTests.AllowedFinds`)
- Audit check: `git grep -nE "\.Q<|\.Q\(" -- Assets/_Game/Scripts/UI Assets/_Game/Scripts/Input ':!Assets/_Game/Scripts/UI/UiElements.cs'` and `git grep -nE "style\.display *= " -- Assets/_Game/Scripts`. Hits are findings, except `MenuUiController`'s five deliberate `style.display` comparisons (docs/STATE.md:69).
- Proposed guard: a `RuntimeFiles()` scan of `UI/` and `Input/` with a counted allow-list for `.Q<`, `.Q(` and `style.display =` outside `UiElements.cs`.
- Source: docs/CONVENTIONS.md:267, docs/STATE.md:60, docs/STATE.md:69
- Status: CURRENT, but **not followed**: at `b7c2616e` the two audit greps find 182 bare `.Q` lookups in 9 files under `UI/` and `Input/`, and 53 inline `style.display =` assignments outside `UiElements.cs` in 11 files (for example Scripts/Input/TouchControlsController.Hud.cs:135). `UiElements.Bind` / `Query` are used only in `MenuUiController.cs`. The A22 pass converted `MenuUiController` only (docs/STATE.md:60).

## 4. The audit lens criteria

This is how the 2026-11-12/13 architecture audit was run, reproduced here so it can be re-run. Each lens was one read-only agent with one concern.

Rules common to every lens agent: read-only; no sub-agents; run on opus, never on Fable (CODE-PROC-03: Fable is an escalation for the final verdict, not for a lens); scope = runtime C# under `Assets/_Game` (Editor and Tests only to see what is pinned); every claim needs a `path:line` the agent actually opened; every count comes from a grep whose pattern is stated; anything unverified is labelled `[unverified]`; read docs/ARCHITECTURE.md and docs/CONVENTIONS.md first. A pattern that follows a documented rule is not a finding. A documented rule the code breaks is a finding, and so is an undocumented de-facto rule. Stop after about 60 files opened and say what was not covered.

Each lens returns: a verdict and a grade A-F; evidence tables; findings (ID, title, severity critical/high/medium/low, PRE-ALPHA = likely to cause a bug or block the playtest vs POST-ALPHA = debt, two `path:line` pieces of evidence, why it matters, smallest fix, effort S/M/L); a keep-list; a proposed rule set; and what was not covered.

| Lens (prefix) | Question it answers (the lens's required conclusion) | What to inventory | How it is graded | Rule areas it checks |
|---|---|---|---|---|
| Events / communication (EV) | Is there one ruling logic for how state changes reach other code, and what is the smallest rule set that matches most of the code? | Every mechanism, with count and grep pattern: C# event/Action, static event, UnityEvent, SignalBus, Fusion RPC, `[Networked]` + ChangeDetector, byte event-id polling, polling in Update, static singletons and registries, `Find*ByType`, SendMessage, SO channels, UI Toolkit callbacks. A layer-pair matrix (network->gameplay, gameplay->visuals/audio/UI, UI->gameplay, services->anything, diagnostics). Lifecycle safety (subscribe/unsubscribe pairs, static events surviving domain reload, listeners existing before `Spawned`). | Verdict + grade A-F (common format); findings by severity and PRE/POST-ALPHA. Known rules it must check: "animation + VFX local, never RPC'd"; "round end polled, never ChangeDetector". 2026-11-12: C+ | EVT, NET-02, DI-12 |
| DI / service boundaries / layering (DI) | Does every dependency arrive one way, and does every layer depend only downward? | Binding map (service, bound in, lifetime, consumer count); self-injection variants and count; bypasses (static `Instance`, `Find*`, service location, `new` of services); interface leaks (direct Input/InputSystem, `Unity.Services.*`, `Debug.Log`, Fusion runner/session calls outside the service); folder dependency graph from `using` and qualified references (cycles, inversions); the 10 largest runtime files judged cohesive or not; tunables in SOs vs magic numbers vs duplicated consts; whether asmdefs are worth adding before alpha. | Verdict + grade A-F (common format); findings by severity and PRE/POST-ALPHA. 2026-11-12: C | LAY, DI, ERR-01, DATA-11 |
| Networking (NET) | Is every write made by the authority that owns the state, and is every claim from a peer checked? | Every NetworkBehaviour (networked properties, `FixedUpdateNetwork`, authority gate at the top, RPC count, ChangeDetector); every `[Rpc]` (signature, what it mutates, receiver-side validation, presentation smuggled in); cross-authority writes without an RPC (siblings of CW-216); state shape (TickTimer vs float vs `Time.time`, state duplicated between networked and local, state lost on late join or master migration); authority-check boilerplate; Server-Mode readiness (Shared-Mode-only assumptions, how concentrated). | Verdict + grade A-F (common format); findings by severity and PRE/POST-ALPHA. 2026-11-12: C+ | NET, EVT-01 |
| Duplication / data-driven / extensibility (DUP) | Can an ability, status effect, class or match rule be added by data, or does it need copied code? | Whether the ability pipeline is data-driven or copied; switches on concrete ability type or on ChickenClass/LoadoutMode outside the ability classes; status effects (one model or bespoke per effect); class-definition scatter; whether bots use the same ability path as players; top ten duplicated clusters with every location; "add one" walkthroughs (files touched today vs ideal) for an ability, status effect, class and match rule; stringly-typed ids and networked/serialized enums unsafe to reorder. | Verdict + grade A-F (common format); findings by severity and PRE/POST-ALPHA. 2026-11-12: C | DATA, NET-13, LAY-10 |
| Presentation (PRES) | Does presentation read state through one path, cheaply, with text and colour in one place? | UI controller pattern table (base class, lifecycle, root, query, show/hide), in-match HUD vs menus; state->UI binding per HUD element (poll, subscribe, ChangeDetector, push); visual feedback path (one path or one watcher per effect); per-frame allocation and `FindObjectOfType` in Update/Render (the mobile target is 30 fps); juice constants and colours outside FeedbackTuning/palette; audio call sites through `IAudioService` with typed ids; input through `IInputProvider` only; localization (literals vs `UiText`); whether diagnostics are separable from shipping code. | Verdict + grade A-F (common format); findings by severity and PRE/POST-ALPHA. 2026-11-12: C+ | EVT, ERR-07, ERR-08, LAY-15, DI-11 |

Grades given on 2026-11-12: events C+, DI C, networking C+, duplication C, presentation C+, overall C+. There were 23 findings (A01-A23): 9 pre-alpha, 14 post-alpha. The owner's decisions: D1a fix everything, pre and post alpha; D2a one observer rule, events carry ids only; D3a all food transfers authority-confirmed; D4a retired ability classes wait for alpha feedback; D5b assemblies before alpha (M/arch-audit-decisions-2026-11-12.md:14-18).

## 5. The adopted observation rule set

The events lens proposed this rule set, and it was adopted on 2026-11-12. The numbers are the audit's own and are cited elsewhere as "rule set B.n".

1. A cross-authority write goes through `[Rpc(All, StateAuthority)]` only; never RPC presentation. -> ARCH-NET-02, ARCH-NET-03, ARCH-NET-04, ARCH-EVT-01
2. Continuous replicated state: the owning NetworkBehaviour reads it with a ChangeDetector in Render, or presentation polls it. -> ARCH-EVT-06
3. A one-shot replicated moment is a `[Networked] byte XEventId` bumped by the authority, observed on every peer through ONE shared per-chicken observer with baseline seeding; never call presentation from FixedUpdateNetwork. -> ARCH-EVT-02, ARCH-EVT-03, ARCH-EVT-05
4. Local fan-out of an observed moment is a C# event raised from Render and unsubscribed in Despawned/OnDestroy. -> ARCH-EVT-05, ARCH-EVT-07, ARCH-EVT-09
5. Presentation to UI: the UI polls in Update; cross-HUD cues use a static mailbox with a SubsystemRegistration reset. -> ARCH-EVT-14
6. Scene lookups go through static registries plus one local-player accessor; `Find*` only for bootstrap and SceneContext self-injection. -> ARCH-DI-09, ARCH-DI-10, ARCH-DI-14, ARCH-EVT-12
7. UI to gameplay: call a public method on the gameplay owner, which checks authority itself. -> ARCH-EVT-15

Owner addition D2a: events carry ids and keys only, never display strings or AudioClips, so language packs can swap text and voice without touching gameplay. -> ARCH-EVT-04

## 6. Pattern catalogue

The patterns the audit introduced or confirmed. Every identifier was checked at `b7c2616e`. Each pattern lists what it solves, where it lives, how a new feature uses it, and what keeps it true.

### 6.1 Injection and lookup

**Self-injection helper.** *Solves:* Fusion-spawned and `AddComponent`-ed objects are built outside any Zenject container, and 53 hand-written injection copies in 47 files disagreed on which container to use. *Where:* `SelfInject.Ensure(this, alreadyInjected)` and `SelfInject.RegisterSceneContainer` in `Scripts/Injection/SelfInject.cs`. The lookup order is: the scene container of the object's own scene, else any live scene container, else the project container. It never throws and reports once per type. `GameInstaller.InstallBindings` registers its container first. *Use:* declare `[Inject] public void Construct(ILogService log, ...)`, then call `SelfInject.Ensure(this, _log != null);` as the first line of `Spawned` / `Awake`. *Kept true by:* ARCH-DI-03, ARCH-DI-06, ARCH-DI-08; `SelfInjectTests`.

**Read-only registries and self-clearing singletons.** *Solves:* 43 runtime `Find*` calls, and objects reached two ways. *Where:* `ChickenController.ActiveControllers`, `ChickenCargo.ActiveCargos`, `ChickenCombat.ActiveCombats`, `ChickenMatchStats.ActiveStats`, `FoodPile.ActivePiles`, `PlayerBase.ActiveBases`, `AbilityZone.ActiveZones` (private list, `IReadOnlyList<T>`, private `Register` / `Unregister`, `ResetRegistry` on SubsystemRegistration). The singletons are `GameManager.Instance`, `MapGenerator.Instance`, `MatchCamera.Instance` and the others, each cleared with `if (Instance == this) Instance = null;`. Remaining `Find*` calls are allow-listed in `SceneObjectAccessTests.AllowedFinds`. *Use:* a new "many of a kind" network object copies the registry block from `PlayerBase` and is added to `SceneObjectAccessTests`. A new "one of a kind" object gets an `Instance` that clears itself. Iterate by index. *Kept true by:* ARCH-DI-09 to ARCH-DI-14; `SceneObjectAccessTests`; `RegistryTestAccess` for tests.

**One local chicken.** *Solves:* twelve copies of "find the chicken with input authority", which picked a Doppelganger decoy. *Where:* `ChickenController.Local` / `ChickenController.LocalDriven` over the pure `LocalChickenRules.Pick`. *Use:* read `ChickenController.Local` (null when there is none) and never loop over chickens checking `HasInputAuthority`. *Kept true by:* ARCH-EVT-12; `LocalChickenRulesTests`.

### 6.2 Replicated moments and presentation

**Event-id byte, one observer, ids-only payloads.** *Solves:* presentation that missed, doubled or mis-attributed moments, and payloads that tied gameplay to display text and clips. *Where:* the producer bumps `[Networked] byte XEventId` (`AbilityController.LastCastEventId`, `ChickenCargo.DepositEventId`, ...). The observer is `ChickenLocalEvents` (owned by `ChickenController`, reached as `ChickenController.LocalEvents`, observed in `Render`), with the pure cursor `EventEdge` and the per-subscriber `ListenerList` / `LocalRaise` (per-listener try/catch, no allocation per raise, a Warn when nobody listens). Payloads are `CastEvent` (registry codes `AbilityCode` / `AimedAbilityCode`, resolved via `ChickenLocalEvents.AbilityOf`), `StealEvent` and `CargoChangeEvent`. *Use:* for a new one-shot moment, add a networked byte, write the describing fields, then `++` on the authority. Add an `EventEdge` and a C# event to `ChickenLocalEvents`. Listeners subscribe in `OnEnable` and unsubscribe in `OnDisable`; a listener that must act at its own point in the frame latches the event and calls `Observe()` there. *Kept true by:* ARCH-EVT-02 to ARCH-EVT-09; `ChickenLocalEventsTests`, `ChickenListenerWiringTests`, `GameplayPresentationBoundaryTests`.

**Own-player cue latch.** *Solves:* cancel, fizzle, cargo-full and expire sounds played from the tick. *Where:* `AbilityCueLatch` (tick) -> `AbilityController.Render` -> `ChickenLocalEvents.RaiseAbilityCue` -> `HitFeedback` by `SfxId`. *Use:* a new local-only cue is a new `AbilityCue` value latched in the tick, never a sound call. *Kept true by:* ARCH-EVT-11; `AbilityCueTests`.

**Presenter inversion.** *Solves:* Gameplay importing Visuals to attach models and read overlay anchors. *Where:* `IChickenModelPresenter` (Gameplay) implemented by `ChickenModelPresenter` (Visuals), bound in `ProjectInstaller`, `[InjectOptional]` on `ChickenController.Construct` (Scripts/Gameplay/ChickenController.cs:601). Extension methods in `Scripts/Visuals/ChickenPresentationAccess.cs` (`Nameplate()`, `Indicator()`, `StatusBadges()`, `PopAnchor()`) read a holder cached at spawn. *Use:* when Gameplay needs a presentation service, declare a small interface in Gameplay and bind the Presentation implementation in an installer. *Kept true by:* ARCH-LAY-01, ARCH-EVT-13; the compiler.

### 6.3 Authority and transfers

**RPC claims.** *Solves:* ten hand-written "sender speaks for X" checks, input-authority comparisons that refused bots, and unvalidated RPCs. *Where:* `RpcClaim.SenderSpeaksFor(NetworkObject, in RpcInfo)` and `RpcRejectionLimiter` in `Scripts/Gameplay/RpcClaim.cs`; `ChickenController.IsFromMatchAuthority`; `ControlClaimRules.EvaluateSource` / `EvaluateKnockback` / `KnockbackCeiling` / `CeilingFor` / `OutgoingSeconds`. *Use:* a new RPC takes `RpcInfo info = default`, picks one of the three sender checks (ARCH-NET-04), bounds any magnitude by a declared trait, and on refusal writes one `rejected RPC_<Name>` Warn through a family-level `RpcRejectionLimiter`, then returns. *Kept true by:* ARCH-NET-03 to ARCH-NET-05, ARCH-ERR-06; `RpcClaimTests`, `ControlRpcValidationTests`, `KnockbackClaimTests`.

**Sender latency.** *Solves:* honest remote pecks refused at the edge of reach. *Where:* `ChickenCombat.SenderLatency(Runner, info)` (0 local; floor 0.15 s, cap 0.5 s remote) over `ChickenCombat.PeerLatency`, the only `GetPlayerRtt` read in gameplay. *Use:* any reach check on an RPC adds drift for `SenderLatency`. *Kept true by:* ARCH-NET-06; `SenderLatencyTests`.

**Authority-confirmed transfers.** *Solves:* food created or destroyed when the receiver credited optimistically. *Where:* `FoodTransferRules` (`Place`, `SettleWindowSeconds`), `FoodRequests<TKey>`, `PendingStealLedger` (per-ledger ushort ids echoed by every report), caps `ChickenCargo.MaxPeck` and `BaseDepositRules.MaxSingleDeposit`. *Use:* a new transfer is request (id, reserved room) -> owner debits and reports the real amount -> the receiver credits only the report; overflow goes to the bag via `FoodTransferRules.Place`. *Kept true by:* ARCH-NET-07; `FoodTransferRulesTests`, `StealProofTests`.

**Loadouts as registry codes.** *Solves:* proxies that never learnt a remote chicken's real kit. *Where:* `AbilityController.LoadoutCodes` (5 bytes), `AbilityRegistrySO.CodeOf` / `FromCode`, `LoadoutClaimRules`. *Use:* set slots only through `SetSlots`; a new loadout field is a new code byte validated on proxies. *Kept true by:* ARCH-NET-11; `LoadoutReplicationTests`, `LoadoutClaimRulesTests`.

**Comeback flags.** *Where:* `UnderdogSurgeActive` / `LeaderBountyActive` (private set), `ChickenController.RPC_GrantComeback(ComebackGrant)`, pure `ComebackRules`. *Kept true by:* ARCH-NET-08; `ComebackRulesTests`.

### 6.4 Abilities, effects and classes as data

**Traits, not types.** *Where:* `AbilityBaseSO.Job` (`AbilityJob`, `ability.Is(...)`), `GrantsStealProof`, `IsStealth`, `EmittedControl`, `EmittedKnockback`, `NominalStealAmount`, `HasFollowUpPhase` / `FollowUpPhaseSeconds`, `TryGetAreaEffect`, `IconId`. *Use:* a consumer that needs to know something about an ability adds a virtual trait and a row in `AbilityTraitTests`. *Kept true by:* ARCH-DATA-02, ARCH-DATA-03; `AbilityTraitTests` (allow-list `AssassinExecute`, `AutoTestConfig`).

**One path per primitive.** *Where:* `ChickenController.KnockAway`; `ChickenCargo.StealFrom` (with `StealMath.Clamp`); `ChickenController.SendControl` / `SendControlFrom`; `AbilityBaseSO.CanActivate` default plus `SpawnArea` for zone abilities; `EffectWindow` (`Remaining` / `IsOpen` / `TryGrant`); `CluckWars.Planar` (`Flat`, `SqrDistance`, `Distance`, `Forward`, `DirectionOr`) in `Scripts/Geometry/Planar.cs`. *Use:* a new ability composes these in `OnActivate` and declares what it sends. *Kept true by:* ARCH-DATA-05 to ARCH-DATA-07, ARCH-NET-10, ARCH-NET-15.

**One definition per class.** *Where:* `ChickenClasses` in `Scripts/Gameplay/ChickenClassInfo.cs` (flag `1 << class`, forage rule, wording keys, bot profile, solo roster); tuned numbers on `ChickenStatsSO`; presentation-only per-class data stays beside its consumer. *Use:* adding a class is the enum value, the flag, a `ChickenClasses` entry, a stats asset, and whatever `ChickenClassTableTests` then names. *Kept true by:* ARCH-DATA-08.

**One table for status effects.** *Where:* `StatusEffects` in `Scripts/Visuals/StatusEffects.cs` (rows relating `ControlKind`, `ControlState`, `ControlVfx`, `ChickenStatusBadges.Badge`, `ZoneEffect`); ordinal pins on the networked and serialized enums. *Use:* a new effect is a new row plus append-only enum values. *Kept true by:* ARCH-DATA-09, ARCH-DATA-10, ARCH-NET-13; `StatusEffectTableTests`, `EffectWindowTests`.

### 6.5 Layer-safe constants and presenters

*Where:* simulation constants in Gameplay (`AbilityHoldStateMachine.TapHoldThresholdSeconds` in `Scripts/Gameplay/AbilityActivationRules.cs`, `ChickenCargo.CargoChangeEventThreshold`), aliased by `FeedbackTuning`. `PlayerColors` in Gameplay with `PlayerPalette` forwarding. `AccentKey` in Abilities with the `AccentColor()` extension in `Scripts/Visuals/AbilityAccent.cs`. `ChickenCapsule` in Gameplay with `ChickenGround` forwarding. *Use:* when a lower layer needs a number or a key the upper layer also uses, define it in the lower layer and alias it upward; never copy the literal. *Kept true by:* ARCH-LAY-10, ARCH-LAY-11; `GameplayPresentationBoundaryTests`, `PlayerPaletteTests`, `FeedbackPaletteTests`.

### 6.6 UI plumbing

*Where:* `UiElements` in `Scripts/UI/UiElements.cs` (`SetShown`, `IsShown`, `EnsureEventSystem`, `Query`, `Bind`; `Bind` logs once per missing element name). Oversized MonoBehaviours are split as partial classes (`TouchControlsController`, `MatchOverlaysController`, `MenuUiController`). `TestSources.ReadClass` (Tests/TestSources.cs:32) reads a file plus its partial siblings for source guards. *Use:* look elements up through `UiElements.Bind`, and split a large component into `Name.Concern.cs` partial files without moving its serialized state. *Kept true by:* ARCH-ERR-08 (not yet followed outside the menu), ARCH-LAY-13; element-name constants only where a test pins the name (docs/CONVENTIONS.md:269).

### 6.7 Audio

*Where:* typed `SfxId` with `AudioRegistrySO.Resolve`; `IAudioService.PlaySFX(SfxId, volume, pitch)` and `TryPlaySFX` (`Scripts/Audio/IAudioService.cs`); `MissingSfxReporter` (once per id or call site); `OwnSoundRules` (`Scripts/Visuals/OwnSoundRules.cs`: only the human driving a chicken hears its own sounds). *Use:* a new sound is an appended `SfxId` and a registry slot, played from a listener of a local event, never from `FixedUpdateNetwork`. *Kept true by:* ARCH-EVT-10, ARCH-EVT-11, ARCH-ERR-07; `MissingSfxTests`, `CastPresentationTests`.

### 6.8 Logging

*Where:* `ILogService` with `UnityLogService` (`Scripts/Logging/UnityLogService.cs`); `Error` leads with the `[Source]` line, cause below. `PerKeyRateLimiter` (Core, `Scripts/Diagnostics/Shared/`) under `RpcRejectionLimiter` and `ListenerList`. *Kept true by:* ARCH-ERR-01, ARCH-ERR-02; `RuntimeSourceGuardTests`, `LogErrorFormatTests`.

### 6.9 Assemblies and weaving

*Where:* five asmdefs (`Scripts/Logging/CluckWars.Core.asmdef`, `Scripts/Gameplay/CluckWars.Gameplay.asmdef`, `Scripts/Visuals/CluckWars.Presentation.asmdef`, `Scripts/Diagnostics/CluckWars.Diagnostics.asmdef`, `Scripts/App/CluckWars.App.asmdef`) plus `.asmref` files for every other runtime folder. `AssembliesToWeave` in `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`. Guards: `AssemblyLayoutTests` (folder map, pinned references and settings, compiled references, weave list) and `WeavingGuardTests` (weaver attributes matched by type name: `NetworkBehaviourWeavedAttribute`, `NetworkStructWeavedAttribute`, `NetworkInputWeavedAttribute`, `NetworkAssemblyWeavedAttribute`). *Use:* ARCH-LAY-02 for a new folder, ARCH-LAY-06 for a new assembly. Prove a new guard bites by removing the config line, deleting `Library/Bee`, watching it fail, then restoring.

### 6.10 Concealment and visibility composition (the Egg Shell)

*Solves:* a bird that clipped out of its egg, without weakening the Invisibility rules. *Where:* the hide composes AFTER the invisibility rule: `ChickenVisuals.BodyAlpha(raw, isLocal, shellConceal01)` first applies the local Invisibility floor (`HiddenVisibilityRules.AlphaFor`, `LocalHiddenOpacityFloor`) to a faded bird, then multiplies by the shell's conceal (`ChickenVisuals.SetShellConcealment`, driven by `ChickenShellVisuals`). The vanish is timed from the replicated timer rule `ShieldRules.EggStartsNow`, before any occluding geometry exists. The sealed state disables renderers outright, because a dither at alpha 0 is not a guarantee. *Use:* any new "hide the bird" effect composes on top of `HiddenVisibilityRules` and never edits it. Its invariants are asserted from the real curves, not copied numbers. *Kept true by:* `ShellBodyHiddenTests`.

### 6.11 Caveats that cost real bugs

- **Planar and exact-boundary maths.** Routing a range verdict through `Planar.SqrDistance` refused a thief standing at exactly the reach: computing both sides in one method is not bit-for-bit the same as comparing a returned float (docs/STATE.md:68; ARCH-DATA-07).
- **Raw `GetPlayerRtt` reads 0 for a remote peer in Shared Mode.** It gave `FoodPile.RPC_Drain` no drift margin, so honest remote pecks were refused (docs/STATE.md:98; ARCH-NET-06).
- **A failed compile writes no test-result XML,** and a runner can pass on stale assemblies. Confirm the XML exists and contains the new fixtures before trusting a green run (CODE_RULES.md has the test procedure).
- **Fusion in batch mode does not re-weave on a config-only change.** Edit `AssembliesToWeave` in the same step as the asmdef, and delete `Library/Bee` to force a recompile (docs/CONVENTIONS.md:495; ARCH-LAY-06).
- **`Object.Destroy` on an asset** is refused in the Editor (one log line) but real in a player. `ArenaWarmupProxies` destroyed the shared `Fx_Particles.mat`, so every chicken particle drew with a null material. Destroy only what you created (`ChickenVFX.BuildMaterial(out bool ownedByCaller)`), and verify cleanup in a player build (docs/CONVENTIONS.md:449-464).
- **A green session proves nothing about a path it never exercised.** The 2-client run cannot produce combat. Check the log counts of the specific events (rejected RPCs, exceptions, missing clips) before trusting a pass (see the testing rules in `docs/CODE_RULES.md`).

## 7. Unenforced and partly enforced rules

This table lists every rule whose Enforced-by line includes CONVENTION or NONE, plus three blind spots that sit outside a single rule. Rows are ranked by regression risk, highest first: how likely the gap is to let a player-visible or multiplayer bug through, weighted by how often the code is touched. Effort: S = one source-scan or reflection test of about 50 lines; M = a new harness or several sites; L = needs code changes before a guard can pass.

**Blind spot 0, above every row: nothing runs the suite automatically.** There are no git hooks (`.git/hooks` holds only `*.sample` files, `core.hooksPath` is unset) and no test CI. The only workflow, `.github/workflows/pages.yml`, deploys `docs/site/`. Every TEST in this document therefore bites only when someone runs EditMode by hand (the MCP bridge, `TestRunnerApi`, or `unity test`). Proposed guard: a pre-push hook, or a scheduled local job, that runs `unity test --mode EditMode` in batch and refuses on red or on a missing result XML. Effort: M (L if it must run in hosted CI with a Unity licence).

| Rank | ID | What is not enforced | Proposed guard | Effort |
|---|---|---|---|---|
| 1 | ARCH-NET-01 | `HasStateAuthority` gate at the top of every `FixedUpdateNetwork` (holds today, 10/10 sites) | Source test over every override, with `AbilityController` allow-listed | S |
| 2 | ARCH-NET-20 | Branching on `SessionMode` (4 sites today, all seed derivation or session setup) | Source test with the allowed sites listed by file and member, like `SceneObjectAccessTests.AllowedFinds` | S |
| 3 | ARCH-NET-14 | `[Networked]` layout outside `ChickenController`'s windows | Reflection snapshot of every NetworkBehaviour's networked properties (name, type, order) | M |
| 4 | ARCH-NET-02 | Foreign writes to networked state (69 one-line `[Networked]` properties have a public `set`) | Reflection test: networked setters private, with a counted allow-list | L |
| 5 | ARCH-NET-03 | Gate-before-write in a new RPC (pinned only per named RPC) | A generic test: each `[Rpc]` body's first statement calls one of the three sender checks | M |
| 6 | ARCH-NET-13 | Ordinal pins for `MatchEventKind`, `ComebackGrant`, `CastArchetype` and any new wire enum | Collect enums from `[Networked]` and `[Rpc]` signatures; require a central pin table | S |
| 7 | ARCH-NET-07 | Total-food conservation in a running match | Pure simulation of request/report/timeout/bounce sequences asserting conservation | M |
| 8 | ARCH-EVT-02 | Producer side of event ids: write-before-bump, never reset | Ban literal assignment to `*EventId`; per-site `MethodBody` ordering | S |
| 9 | ARCH-EVT-01 | Presentation calls inside RPC bodies | Run the boundary pattern list over every `[Rpc]` body by reflection | S |
| 10 | ARCH-DI-12 | SubsystemRegistration reset of every mutable static (only 6 singletons and 1 material pinned) | Reflection: static non-readonly fields need a reset method in their type | M |
| 11 | ARCH-ERR-08 | `UiElements.Bind` / `SetShown` (rule not followed: 182 bare `.Q`, 53 inline `style.display =`) | Counted allow-list scan of `UI/` and `Input/`, burned down per file | M |
| 11 | Blind spot 1 (see below) | `UiTextTests` literal-text guards skip the partial files of split controllers | Read through `TestSources.ReadClass` | S |
| 12 | ARCH-DATA-12 | Passive raw `Description` prose, the wrong-field argument, digits in other csv rows | Field-named arguments; a default arm that fails in `PerkTextTests`; csv digit test | M |
| 13 | ARCH-DATA-11 | Literal tunables in Gameplay, Abilities and UI (e.g. `ab.Cooldown <= 6f`) | Counted allow-list scan of float literals outside constant declarations | M |
| 14 | ARCH-NET-17 | Legacy `UnityEngine.Input` in the Gameplay assembly | `RuntimeFiles()` scan of Gameplay folders | S |
| 15 | ARCH-DI-01 | Device reads outside `Scripts/Input`; audio played around `IAudioService` | Counted allow-list scan (dev tools and DebugHud listed) | S |
| 16 | ARCH-LAY-06 | Same-commit weave edit; `Library/Bee` step; assemblies not named `CluckWars.*` | Widen `WeavingGuardTests.IsGameAssembly` to every non-package runtime asmdef | S |
| 17 | ARCH-DATA-08 | "Never switch on ChickenClass" (0 hits today) | Counted allow-list scan for `case ChickenClass.` | S |
| 18 | ARCH-DATA-09 | "No sixth switch" over the five status-effect enums | Counted allow-list scan for their `case` labels outside `StatusEffects.cs` | S |
| 19 | ARCH-DI-05 | `ProjectContext.HasInstance` ban outside `RivalIndicator` | `RuntimeFiles()` scan; allow-list the two renderer features | S |
| 20 | ARCH-DI-17 | A second serialized reference to `MatchConfig.asset` | GUID scan of every scene, prefab and asset | S |
| 21 | ARCH-EVT-03 | Counter reads in `Diagnostics/`, `App/`, `Installers/` | Extend the folder list of `NoPresentationComponent_ReadsAOneShotCounterItself` to every runtime folder except Gameplay | S |
| 22 | ARCH-EVT-12 | Input-authority scans in files outside the nine listed | Scan every runtime file for the scan pattern; allow-list `ArenaReadinessDriver` | S |
| 23 | ARCH-DATA-05 | A thief or shover file not in the per-file guard lists | Derive the file list from abilities whose traits declare a steal or knockback | S |
| 24 | ARCH-NET-15 | Effect windows outside `ChickenController` | Extend `NoWindowBody_IsWrittenOutInTheController` to every NetworkBehaviour | S |
| 25 | ARCH-NET-16 | The client-side position check for a new networked object | Live check only (`World` hash in `tools/run-clients.ps1`); prefab side already TEST | — |
| 26 | ARCH-NET-05 | "Never by a new tunable" for RPC bounds | Review only; optionally ban `SerializeField` in `ControlClaimRules` and `RpcClaim` | S |
| 27 | ARCH-EVT-04 | ids-only payloads on local events declared outside `ChickenLocalEvents.cs` | Reflection over every `event Action<...>` in `CluckWars.*`: no `string` or `AudioClip` arguments | S |
| 28 | ARCH-EVT-07 | `+=` / `-=` pairing outside the seven listed watchers | Per-file count of `+= Handler` vs `-= Handler` over `RuntimeFiles()` | M |
| 29 | ARCH-LAY-08 | `UnityEditor` in runtime files outside `#if UNITY_EDITOR` | Region-aware source scan | S |
| 30 | ARCH-LAY-15 | Gameplay-meaning colour literals outside `FeedbackTuning` / palette | Counted allow-list scan of `new Color(` (after open question Q1 is settled) | M |
| 31 | ARCH-ERR-07 | A comment at every designed-silence `TryPlay` site | Allow-list of `TryPlaySFX` / `TryPlay` call sites with reasons | S |
| 32 | ARCH-ERR-06 | The `rejected RPC_` prefix outside the control RPCs | Source scan: every `RpcRejectionLimiter` call's message starts with the prefix | S |
| 33 | ARCH-ERR-04 | Refuse-before-side-effect beyond spawning abilities | Review only (judgement); `TryActivate` ordering could be a `MethodBody` test | S |
| 34 | ARCH-DATA-06 | `base.CanActivate(ctx)` first in every override | Source test over every `override bool CanActivate` | S |
| 35 | ARCH-DATA-07 | Exact-boundary verdicts in the other four pure rule files | One "exactly at the limit is accepted" test per rule file | S |
| 36 | ARCH-DI-03 | The `Ensure` argument is a field `Construct` sets, and the call comes first | Source test pairing each `Ensure(this, X != null)` with an assignment to `X` in `Construct` | M || 37 | ARCH-DI-04 | What the two allow-listed renderer files may do | Narrow the allow-list from whole files to the one `Log` helper body | S |
| 38 | ARCH-DI-06 | Container registration in a future scene installer | Apply the existing test to every `MonoInstaller` other than `ProjectInstaller` | S |
| 39 | ARCH-DI-09 | "Never a static" for services (`FeedbackPalette.Current`, `AbilityTargeting.Mode` exist) | Settle open question Q5 first | — |
| 40 | ARCH-DI-10 | A new registry not added to `SceneObjectAccessTests` | Reflection: every static `IReadOnlyList<T>` named `Active*` is in the tested list | S |
| 41 | ARCH-DI-16 | The fallback-and-warn pattern on every installer slot | Reflection over `ProjectInstaller`'s serialized SO fields: each is assigned on `ProjectContext.prefab` | S |
| 42 | ARCH-NET-18 | One-shot press getters read by diagnostics | Source scan of Diagnostics and AbilityLab for `GetAbility[1-4]Pressed` | S |
| 43 | ARCH-NET-12 | `onBeforeSpawned` discipline | Review only | — |
| 44 | ARCH-LAY-10 | A new shared constant copied instead of aliased | Review only | — |
| 45 | ARCH-LAY-13 | Serialized fields and public API unchanged by a split | Snapshot of serialized field names per MonoBehaviour referenced by scenes and prefabs | M |
| 46 | ARCH-LAY-14 | Guards loosened during a refactor | Review only | — |
| 47 | ARCH-LAY-07 | `InternalsVisibleTo` ban | Reflection case in `AssemblyLayoutTests` | S |
| 48 | ARCH-DI-02 | Two installers only | Reflection over `InstallerBase` subclasses | S |
| 49 | ARCH-DI-07 | No guard on container-built objects | Source test over `FromNewComponentOnNewGameObject` targets | S |
| 50 | ARCH-EVT-06 | ChangeDetector only on the declaring behaviour, never on counters | Source scan: `GetPropertyReader` names a property of the same class, never `*EventId` | S |
| 51 | ARCH-EVT-08 | Latch-and-Observe discipline | Review only | — |
| 52 | ARCH-EVT-14 | UI polls; static mailbox reset | Covered by the ARCH-DI-12 guard once written | — |
| 53 | ARCH-EVT-15 | UI never sends an RPC | Source scan of `UI/`, `Input/`, `Visuals/` for `.RPC_` | S |
| 54 | ARCH-DATA-03 | A trait without an `AbilityTraitTests` row | Reflection: every virtual trait on `AbilityBaseSO` is named in `AbilityTraitTests` | S |
| 55 | ARCH-DATA-10 | Totality tests for new enum-keyed tables | Review only | — |
| 56 | ARCH-DATA-13 | Retired ability classes kept | Review only | — |
| 57 | ARCH-ERR-02 | Source-tag consistency | Review only (the tag table at docs/TESTING.md:519-541 is partly stale) | — |
| 58 | ARCH-ERR-03 | The silent-failure sorting rule | Review only (judgement) | — |
| 59 | ARCH-ERR-05 | No rollback; no despawn in `onBeforeSpawned` | Review only | — |
| 60 | Blind spot 2 (see below) | SO suffix for SO types with no asset in `Data/` | Reflection over every `ScriptableObject` subclass in `CluckWars.*` | S |

**Blind spot 1 (verified).** `UiTextTests.MenuController_HasNoPlayerFacingStringLiterals` and `UiTextTests.MatchHud_HasNoPlayerFacingStringLiterals` read single files with `File.ReadAllText` (`ReadCode`, Tests/UiTextTests.cs:209-210). `UiTextTests.CodeNeverPassesAKeyLiteral_ToUiText` reads a fixed list that names `MenuUiController.cs` and `MatchOverlaysController.cs` only (Tests/UiTextTests.cs:193-203). Since the partial-class splits, `MenuUiController.{Chrome,Coop,GearUp,PickBird,Sheets,Stage}.cs` and `MatchOverlaysController.{Intro,Lobby,MatchEnd}.cs` exist at `b7c2616e` and are not scanned. That breaks "a source-scan guard is about the CLASS" (docs/CONVENTIONS.md:275). Fix: read through `TestSources.ReadClass`. The rule itself belongs to CODE_RULES.md.

**Blind spot 2 (verified).** `ProjectConfigTests.EveryProjectScriptableObjectType_UsesTheSoSuffix` derives its types from assets found under `Assets/_Game/Data/`. A `ScriptableObject` type that has no asset there (made with `CreateInstance`, or abstract) is never checked. The naming rule itself is in CODE_RULES.md and FILE_STRUCTURE.md.

## 8. Appendix A — Stale sources

Every line here was checked against `b7c2616e`. Until a line is fixed, this document wins over it. "Who should fix": **agent** = any agent may edit the doc; **Maestro** = `.claude/CLAUDE.md` is git-ignored, and agents are told not to edit it; **memory** = the main session's memory upkeep.

| Source path:line | What it says | What is true at `b7c2616e` | Who should fix |
|---|---|---|---|
| .claude/CLAUDE.md:66 | Self-inject with `if (_log == null) ProjectContext.Instance.Container.Inject(this);` | The only allowed form is `SelfInject.Ensure(this, _log != null)`. The old form fails `SelfInjectTests.NoRuntimeFile_InjectsItselfOrLocatesAService_OutsideTheHelper` (ARCH-DI-03) | Maestro |
| .claude/CLAUDE.md:72 | "Animation + VFX are local — `ChangeDetector` + `Render()`" | One-shot moments use a networked event-id byte read by `ChickenLocalEvents`; ChangeDetector is only for continuous state on the declaring behaviour (ARCH-EVT-02, ARCH-EVT-06) | Maestro |
| .claude/CLAUDE.md:111 | cravify skill "for any task touching 3+ scripts/prefabs" | The owner's global rule: do not use cravify / Antigravity unless asked for by name | Maestro |
| docs/TESTING.md:260-263 | Do not add a runtime `.asmdef`, because it can break Fusion's IL weaver | Five asmdef-based assemblies are current; the weave-list rule replaced the ban (ARCH-LAY-01, ARCH-LAY-06) | agent |
| docs/TESTING.md:106-154 | Documents Unity's own MCP (`com.unity.ai.assistant`, relay) as the project's bridge | That server is banned (needs a Pro licence); the bridge is the open-source IvanMurzak Unity-MCP (`ai-game-developer`). Root `CLAUDE.md:57` names it correctly | agent |
| docs/CONVENTIONS.md:28 | `GameInstaller` binds `MatchConfigSO` | `MatchConfigSO` is bound in `ProjectInstaller` (Scripts/Installers/ProjectInstaller.cs:220), and `GameInstaller` says so (Scripts/Installers/GameInstaller.cs:25-27) | agent |
| docs/CONVENTIONS.md:106-107 | Example RPC `RPC_ApplyDamage(float amount, PlayerRef attacker)` | No such RPC exists, and the example has no `RpcInfo`, which `RpcClaimTests.EveryRpc_TakesTheSender` would fail (ARCH-NET-03) | agent |
| docs/CONVENTIONS.md:223 | The job list (Peck, Steal, Stun, Shove, Slow, Speed, Jump, Shield, Hide, Bank, Death Mark) | `AbilityJob.SlowArea` exists (Slow split, 2026-11-13) | agent |
| docs/CONVENTIONS.md:484 | Core's folders: `Localization/`, `Injection/`, `Services/`, `Bootstrap/`, `Diagnostics/Shared/` | `Geometry/` (`Planar`) is also Core (Scripts/Geometry/CluckWars.Core.asmref; Tests/AssemblyLayoutTests.cs:38) | agent |
| docs/CONVENTIONS.md:509 | "Source SO `.cs` definitions live in `Scripts/Gameplay/`" | SO sources also live in `Scripts/Abilities/`, `Scripts/Visuals/` (`FeedbackPaletteSO`), `Scripts/Audio/` and elsewhere (see FILE_STRUCTURE.md) | agent |
| docs/CONVENTIONS.md:523-525 | The in-game HUD (`MatchHud`, `TouchControlsHud`, `DebugHud`, `UiGfx`) is a procedural-UGUI island | `TouchControlsHud` no longer exists; `TouchControlsController` and `MatchHudController` are UXML-based. `MatchHud`, `DebugHud` and `UiGfx` still exist | agent |
| docs/STATE.md:99 | `ChickenCargo.TryResolveThief` and `PlayerBase.TryResolveDepositor` still read raw `GetPlayerRtt` | `ChickenCombat.PeerLatency` is the only `GetPlayerRtt` read in gameplay; the later entry docs/STATE.md:83 moved both (ARCH-NET-06) | agent |
| docs/ARCHITECTURE.md:15-16 | `CharacterSelectController` builds the menu UGUI; Game.unity holds `TouchControlsHud` | Neither class exists; menus are UI Toolkit | agent |
| docs/ARCHITECTURE.md:43 | `MatchConfigSO` bound by the scene context | Bound in `ProjectInstaller` | agent |
| docs/ARCHITECTURE.md:48-59 | Self-injection snippet `ProjectContext.Instance.Container.Inject(this)` | `SelfInject.Ensure` (ARCH-DI-03) | agent |
| docs/ARCHITECTURE.md:82 | Example RPCs `ChickenCombat.RPC_ApplyDamage(amount, attackerRef)`, `FoodPile.RPC_Drain(amount)`, `PlayerBase.RPC_AddFood(amount)` | `RPC_ApplyDamage` does not exist; the food RPCs now carry request ids and `RpcInfo` (ARCH-NET-07) | agent |
| docs/ARCHITECTURE.md:219 | The camera "finds `ChickenController` with `HasInputAuthority`" | `MatchCamera` uses `ChickenController.Local` (Scripts/Visuals/MatchCamera.cs:389) (ARCH-EVT-12) | agent |
| docs/ARCHITECTURE.md:241, 247 | "Adding a new ability = subclass + drop a `.asset`"; "Concrete abilities (all 8 shipped)" | A new ability also needs its registry entry (ARCH-DATA-01); the roster is one ability per job | agent |
| docs/TDD.md:65, :69, :477 | `NullUGSService` is the demo default via `UGS_DISABLED` | `UGS_DISABLED` is not in `ProjectSettings/ProjectSettings.asset`; every platform binds the real `UGSService` (Scripts/Installers/ProjectInstaller.cs:75-80) | agent |
| docs/TDD.md:81 | Self-injection snippet with `ProjectContext.Instance.Container.Inject(this)` | `SelfInject.Ensure` (ARCH-DI-03) | agent |
| docs/TDD.md:103 | "No game logic class ever calls Fusion directly" | Gameplay is Fusion NetworkBehaviours; Gameplay and Abilities call `Runner.Spawn` at 12 sites. See open question Q3 | agent, after Q3 |
| docs/TDD.md:145 | "Game logic never branches on `SessionMode`" | `CornerAssignment.SeedFor` / `LobbySeatCorner` (Scripts/Gameplay/CornerAssignment.cs:39-49), `MapGenerator` (Scripts/Gameplay/MapGenerator.cs:613) and `MatchBootstrapper` (Scripts/Gameplay/MatchBootstrapper.cs:114-127) branch on it. See open question Q4 | agent, after Q4 |
| docs/TDD.md:373 | `IAudioService.PlaySFX(AudioClip clip, Vector3 position)` | `PlaySFX(AudioClip clip, float volume, float pitch, ...)` and `PlaySFX(SfxId id, float volume, float pitch)` (Scripts/Audio/IAudioService.cs:18, :24) | agent |
| docs/TDD.md:420 | "Fixed camera — no player-following" | `MatchCamera` follows the local chicken by default (`_followLocalChicken = true`, Scripts/Visuals/MatchCamera.cs:59) | agent |
| CLAUDE.md:65 | Latest tag `v0.3.0-alpha` | The newest tag is `v0.4.0-alpha` | agent |
| M/branching-model-v0-4-release.md:26 | The branching model is "Recorded in `CLAUDE.md` (Branches)" | Root `CLAUDE.md` has no Branches section | memory |
| M/ui-rebuild-2026-07.md:40 | `code-architect` is pinned to Fable | It runs on opus; Fable is a per-call escalation, never pinned (CODE-PROC-03, ban lifted 2026-11-17) | memory |
| M/agent-model-routing.md:3, :10 | Offers cravify/Antigravity as a coding route | The global rule forbids it unless asked for by name | memory |
| docs/TESTING.md:77 | All Android testing on the Pixel 9 | **Not settled**: M/test-session-2026-10-20-learnings.md names a Pixel 10 Pro, and M/feedback-verify-state-not-logs.md says PC-only until a phone is reconnected. Code cannot settle it | Maestro |
| docs/CONVENTIONS.md:538 | Attribution `Claude Opus 4.7` | **Not settled**: the model name is set per session by the harness; whether the rule should name a model at all is a process question for CODE_RULES.md | Maestro |

Checked and **not** stale: root `CLAUDE.md:57` ("Unity MCP (Open-source `IvanMurzak/Unity-MCP`) is configured") names the allowed `ai-game-developer` bridge, not the banned `mcp__unity-mcp__` server. docs/TESTING.md:177-180 (real UGS on all platforms) is current.

## 9. Appendix B — Superseded rules

Kept so nobody re-adds them. No ARCH ID has been retired yet.

| Superseded rule | Replaced by |
|---|---|
| `if (_log == null) ProjectContext.Instance.Container.Inject(this);` in `Spawned` / `Awake` | `SelfInject.Ensure(this, _log != null)`, ARCH-DI-03 (2026-11-12, `9b2ae033`) |
| "Do not add a runtime asmdef; it breaks Fusion's weaver" | Five assemblies plus the weave-list rule, ARCH-LAY-01, ARCH-LAY-06 (2026-11-13) |
| Animation and VFX via "ChangeDetector + Render()" for every moment; per-component `_last*EventId` cursors | Event-id byte + one observer, ARCH-EVT-02, ARCH-EVT-03, ARCH-EVT-06 (D2a) |
| `MatchConfigSO` bound in `GameInstaller` (a second serialized slot) | Bound once in `ProjectInstaller`, ARCH-DI-17 |
| Optimistic food credit (`CreditStealAndDrain`, the steal refund RPC, `RPC_AddDeposit`) | Authority-confirmed transfers, ARCH-NET-07 (D3a) |
| Attributing a thief or depositor by input authority | State authority via `RpcClaim.SenderSpeaksFor`, ARCH-NET-04 |
| Unvalidated `RPC_ApplyKnockback(impulse)` | `RPC_ApplyKnockbackFrom` through `KnockAway`, ARCH-NET-10 |
| Raw `Runner.GetPlayerRtt` as a remote sender's drift margin | `ChickenCombat.SenderLatency`, ARCH-NET-06 |
| `is` / `as` / `typeof` on concrete `XxxAbilitySO` outside the ability folder | Traits, ARCH-DATA-02 |
| Eight private planar helpers and inline `v.y = 0f` | `CluckWars.Planar`, ARCH-DATA-07 |
| Finding the local chicken by scanning `HasInputAuthority` | `ChickenController.Local`, ARCH-EVT-12 |
| Ability sounds played from `FixedUpdateNetwork` (`AbilityController.PlayLocalSfx`) | `AbilityCueLatch` raised in Render, ARCH-EVT-11 |
| `IAudioService.PlaySFX(AudioClip, Vector3)` for own sounds | `PlaySFX(SfxId)`, ARCH-ERR-07 |
| `NullUGSService` as the demo default via `UGS_DISABLED` | The real `UGSService` on every platform (the define is an opt-in offline fallback) |
| Several abilities per job under different names per class | One ability per job, ARCH-DATA-04 (2026-11-05) |

## 10. Open questions for the owner

> **Answered 2026-11-14.** Maestro ruled on these via the decision page (https://claude.ai/artifact/PtyqzDvoGh6ZoF5N1zgGcJ); the rulings are folded into the rules above and recorded in the project memory `rule-decisions-2026-11-14`. The questions are kept here for the reasoning that produced them. All of them are now ruled on; the last, branching on `SessionMode`, became `ARCH-NET-20`.


These need Maestro's ruling; the code cannot settle them.

**Q1 — Where do colours live: palette ScriptableObject or code constants?** docs/CONVENTIONS.md:262 says `FeedbackTuning` holds colours "as code constants, not fields on `FeedbackPaletteSO` or `ColorSchemeSO`". Decision D3 (2026-11-07, M/feedback-icons-decisions-2026-11-07.md:15) says the per-state accent colours "must live in a config ScriptableObject". **Today the code does both.** `FeedbackPaletteSO` (Scripts/Visuals/FeedbackPaletteSO.cs, shipped values in `Assets/_Game/Data/FeedbackPalette.asset`) holds the state colours (`StunColor`, `SlowColor`, `SlowTextColor`, `KnockColor`, `NeutralNoEffectColor`, `InkColor`, `CreamColor`, `YolkColor`) and one accent per `AccentKey`. `ProjectInstaller` sets it into the static `FeedbackPalette.Current` (Scripts/Installers/ProjectInstaller.cs:225), and `FeedbackTuning` re-exposes those colours as properties (`CanonicalStunColor => FeedbackPalette.Current.StunColor`). Meanwhile `CanonicalRootColor`, the cargo and health ramps and the removed-chicken colour are `static readonly` code constants in `FeedbackTuning`, "because adding a field to the shipped palette asset would read as black". *Which should the rule be:* (a) state colours and ability accents in the palette SO, everything else as `FeedbackTuning` code constants (today's split, but the root colour is a state colour in code); or (b) every gameplay-meaning colour in the palette SO, with a re-author step whenever a field is added?

**Q2 — Ratify the renderer features' `HasInstance` gate?** docs/CONVENTIONS.md:53 says nothing but `SelfInject` should need `ProjectContext.HasInstance`. `CWHazeFeature.cs:130` and `MarkScreenFeature.cs:158` gate on it, deliberately, so a renderer never creates the ProjectContext in Edit mode. Should the exception in docs/CONVENTIONS.md:47 name the gate explicitly (ARCH-DI-04)?

**Q3 — What does "never call Fusion directly from gameplay code" mean?** (docs/CONVENTIONS.md:11, docs/TDD.md:103.) Every gameplay object is a Fusion NetworkBehaviour, and Gameplay calls `Runner.Spawn`, `GetInput` and RPCs by design. A proposed reading: session lifecycle (start, join, host, shutdown, runner creation) goes through `INetworkService`, and NetworkBehaviour APIs are allowed in Gameplay. Confirm, and the rule can be made enforceable (ARCH-DI-01).

**Q4 — answered (ruling D06, 2026-11-14).** The rule is kept, restated around its intent and given a named allow-list: see `ARCH-NET-20`.
