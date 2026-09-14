# Coding Conventions + Known Footguns

Rules of the road for Cluck Wars contributors (human and agent). Everything
here is enforced by either a `[Networked]` boundary, a Zenject binding, or
a hard-won bug. Don't rediscover them.

---

## Core rules

- **All services go behind interfaces.** Never call Fusion, UGS, or Unity Input directly from gameplay code. Use `INetworkService`, `IUGSService`, `IAudioService`, `IInputProvider`, `ILogService`.
- **ScriptableObjects use the `SO` suffix** — `ChickenStatsSO`, `AbilityBaseSO`, `MatchConfigSO`. Assets live in `/Assets/_Game/Data/`.
- **Game logic reads input only from the Fusion buffer** — `GetInput<PlayerNetworkInput>(out var input)` inside `FixedUpdateNetwork`. Never `Keyboard.current` from gameplay scripts.
- **All log output goes through `ILogService`.** No ad-hoc `Debug.Log`. Pass a `Source` tag per call. `MinLevel` defaults to `Verbose` during dev; raise via `ProjectInstaller._logMinLevel` for release builds.
- **State writes gated on `HasStateAuthority`.** Always early-return at the top of `FixedUpdateNetwork` if not authoritative. Cross-authority writes go through RPCs.
- **URP materials only.** No Built-in RP shaders. Lit / Unlit / custom URP Shader Graph.
- **Animation is local.** `ChickenAnimator` reads `[Networked]` state via `ChangeDetector` and triggers animations on every peer locally. Nothing animation-related is networked.
- **VFX are local.** Same pattern as animation — observed-state-driven, never RPC'd.

---

## Zenject patterns

### Two contexts only

- `ProjectInstaller` (project-scope) — app-wide services, registries, log. Survives scene loads.
- `GameInstaller` (scene-scope, Game.unity) — match-scoped state (`MatchConfigSO`, `INetworkService`).

### Self-injection (NetworkBehaviours + Bootstrap scene)

```csharp
[Inject]
public void Construct(ILogService log) => _log = log;

public override void Spawned()      // or Awake() in Bootstrap
{
    if (_log == null)               // Construct hasn't fired? self-inject.
    {
        ProjectContext.Instance.Container.Inject(this);
    }
    // ... rest of init
}
```

**Don't gate on `ProjectContext.HasInstance`** — it returns false until something accesses `.Instance`. Reading `.Instance` directly triggers the lazy load. This was a real bug (`3bc2e13`).

**If `Construct()` takes a scene-scoped type** (anything bound in `GameInstaller`, e.g. `MatchConfigSO`, `INetworkService`), plain `ProjectContext.Instance.Container.Inject(this)` will throw an unresolved-dependency exception — `ProjectContext` only knows about `ProjectInstaller` bindings. Self-inject from the `SceneContext` first, falling back to `ProjectContext` only if none exists:

```csharp
if (_log == null)
{
    var sceneCtx = FindFirstObjectByType<SceneContext>();
    if (sceneCtx != null) sceneCtx.Container.Inject(this);
    else ProjectContext.Instance.Container.Inject(this);
}
```

This matters most for runtime-spawned `NetworkBehaviour`s (chickens, piles, etc.) — the self-inject path fires on every spawn, so a mismatch here crashes the game on the very first spawn, not just in some edge case. Before adding a new `[Inject]` parameter to any `Construct()`, check which installer binds that type and update the self-inject call site to match. Real bug: `ChickenCargo` gained a `MatchConfigSO` dependency (timed deposit rate) but kept the `ProjectContext`-only self-inject, so every chicken spawn threw (`14fbf8f`).

### Bind by instance

Static-data SOs use `FromInstance`:

```csharp
Container.Bind<ChickenClassRegistrySO>().FromInstance(_chickenClassRegistry).AsSingle();
```

If the inspector slot is null, bind a runtime-empty instance so consumers don't crash:

```csharp
var fallback = _audioRegistry != null
    ? _audioRegistry
    : ScriptableObject.CreateInstance<AudioRegistrySO>();
Container.Bind<AudioRegistrySO>().FromInstance(fallback).AsSingle();
```

Consumers handle empty fields gracefully — a null `AudioClip` is a silent no-op in `UnityAudioService`, etc.

---

## Networking patterns

### State authority gate

Every `FixedUpdateNetwork` opens with `if (!HasStateAuthority) return;` unless it explicitly needs proxy-side simulation (almost never).

### Cross-authority writes via RPC

Damage, drain, deposit, steal — caller is any client, executed on the target's authority:

```csharp
[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
public void RPC_ApplyDamage(float amount, PlayerRef attacker) { ... }
```

### ChangeDetector for local reactions

`ChickenCombat.Render` uses a `ChangeDetector` + cached `PropertyReader<T>` to fire local visuals (Hit/Attack/Stunned anims) on every peer in response to `[Networked]` state changes. Pattern reference:

```csharp
_detector = GetChangeDetector(ChangeDetector.Source.SimulationState);
_hpReader = GetPropertyReader<float>(nameof(HP));

public override void Render() {
    foreach (var prop in _detector.DetectChanges(this, out var prev, out var cur)) {
        if (prop == nameof(HP)) {
            var (prevHp, curHp) = _hpReader.Read(prev, cur);
            if (curHp < prevHp) _animator?.TriggerHit();
        }
    }
}
```

### `[Networked]` defaults

- `int` / `float` → 0
- `bool` → false
- `PlayerRef` → `PlayerRef.None`
- `TickTimer` → default (not running)
- enums → 0 (the zero member)

Initialize in `Spawned` on the authority side if you need a non-zero default:

```csharp
if (HasStateAuthority && VisualOpacity <= 0f) VisualOpacity = 1f;
```

### `onBeforeSpawned`

The way to set `[Networked]` properties so the value is replicated from tick zero (before `Spawned()` fires on any peer):

```csharp
runner.Spawn(prefab, pos, rot, player,
    onBeforeSpawned: (_, networkObject) => {
        var c = networkObject.GetComponent<ChickenController>();
        if (c != null) c.Class = chosenClass;
    });
```

---

## Error surfacing — the silent-failure sorting rule

Settled 2026-08-26, after four byte-identical silent guards were found across the
placed-zone abilities. The point of this section is not "log more": a sweep that
surfaces every early return is worse than the bug, because it buries real errors under
per-cast noise until nobody reads the log at all.

### The rule

**Ask whether the null could ever be produced by a legal game state.**

- **Only reachable via an unassigned serialized field or a missing prefab component**
  → **wiring bug.** It never self-heals, it is invisible on device, and no amount of
  playing will fix it. It must reach `ILogService.Error`, and the message must name
  *which* reference is null and *what the player just lost* — someone will read that
  line in an `adb logcat` with no debugger attached.
- **Reachable in a correctly-built game** — nobody in range, cargo full, nothing to
  steal, `victim == null`, `spaceLeft <= 0f` → **legitimate no-op.** It must stay
  silent. This is the game working.

### The better option, when the caller offers one

**Move the guard ahead of the side effect entirely.** Where the call site provides a
pre-commitment gate, refusing the press outranks surfacing the failure afterwards: nothing
is half-applied, no cooldown is charged, and there is no misleading evidence to explain.
`AbilityBaseSO.CanActivate(AbilityContext)` is that gate — it runs inside
`AbilityController.TryActivate` before `ActiveSlot`, `ActivationTimer` or `SetCooldown` are
touched, and the three zone abilities use it. Reach for this first; the corollaries below
rank what to do when you cannot.

Note the ordering constraint it carries: the `_ctx.Runner` / `_ctx.PrefabRegistry` refresh
must happen *before* `CanActivate`, not merely before `OnActivate`, because the zone
abilities' overrides read exactly those two fields.

### Two corollaries that make it a priority order, not a binary

1. **A guard that trips after a side effect has already been applied ranks higher.**
   Smoke Roost *used to* set `caster.VisualOpacity` before its zone guard, so on a wiring
   bug the player visibly faded and nothing else happened — the ability didn't just fail,
   it produced convincing evidence that it had worked. That is the case this rule was
   written from. It no longer exists in the code: the check moved ahead of the fade into
   `CanActivate`. The rule stands for the next method that does the same thing.
2. **A guard that trips after a `Spawn` has already committed ranks higher still,
   because that one leaks.** A `return` inside `onBeforeSpawned` abandons an already-
   spawned `NetworkObject` whose `LifetimeTimer` was never set: an inert networked
   object that persists for the entire match. That is a resource bug with a networked
   footprint, not a missing tell.

### Two things not to do about it

- **Don't roll back the side effect to make the failure tidy.** The worked example is
  historical (see above), but the reasoning is the live part: Smoke Roost's fade was a
  genuinely successful half of a two-half ability (he fades; everyone in the cloud slows),
  and `OnDeactivate` restored it on schedule. Reverting it would have deleted working
  behaviour and left the ability doing nothing instead of half. Where you cannot gate
  ahead of the side effect, the `Error` line is the fix for the misleading feedback; a
  rollback is not.
- **Don't despawn from inside `onBeforeSpawned`.** That callback runs mid-`Spawn` and
  despawning there is not safe. Surfacing really is the whole fix available at that
  point.

### How a ScriptableObject reaches the logger

SOs have no injected `ILogService` and **must not** acquire one through a service
locator. The logger arrives on `AbilityContext.Log`, set by `AbilityController` from its
own injected `_log` — the same channel `Runner` and `PrefabRegistry` already use. Ability
code calls `ctx.Log?.Error(...)`.

---

## Gameplay → progression boundary

Settled 2026-09-11 (progression slice 1). Gameplay *announces* what happened in a round to
`IMatchEventSink`; it never asks progression anything back. There is no `.asmdef` under
`Assets/_Game`, so the compiler cannot enforce the boundary — the tests below do.

- **Contract surface.** Outside `Scripts/Progression/`, code may name only `IMatchEventSink`,
  `RoundRuleset`, `RoundStandings`, `RoundStandingEntry` and `UnlockKeyTable` from
  `CluckWars.Progression`. The exceptions are `Installers/` (the composition root binds the tracker,
  journal and service), `UI/` and `Editor/`. Every other folder, including a new one, is scanned, and the
  sweep is default-deny: every new progression type is automatically off-limits to gameplay.
- **`UI/` has its own, slightly wider list:** the contract surface plus `IProgressionService`,
  `ProgressionProfile`, `RoundAward`, `ProgressionFault` and `ProgressionFaultKind` — the read side of the
  service. UI never names `MatchTracker`, `ProgressionService` (the concrete type) or any other
  progression type; the same default-deny sweep enforces it.
- **Progression speaks generically.** Code in `Progression/` says actor/resource/round/ability key,
  never food/chicken/cluck, and imports no gameplay namespace. `UnlockKeyTable` is the one
  translation point allowed to name a gameplay type.
- **Actor ids** come only from `MatchActorId.Of(...)` (the `NetworkId` raw value;
  `MatchActorId.None` = 0 = no actor). Emit sites never filter by actor — in solo the player's peer
  simulates the bots, so bot events reach the sink too.
- **`ChickenCargo.ReceiveStolen(amount, victim)` is the only steal credit.** Never write
  `thiefCargo.Cargo += x` on a steal path; that credits silently and progression never hears of the
  steal. Foraging (`PeckAbilitySO`) is the one allowlisted direct write. `ChickenCargo.cs` itself is
  exempt from the `.Cargo +=` scan: it owns `Cargo` and declares `ReceiveStolen`, and its own writes
  (deposit drains, round resets, the credit inside `ReceiveStolen`) are not steal credits by another
  path.
- **The bounty bag is scanned too.** `BountyBag +=` is forbidden outside `ChickenCargo.cs` (its
  owner, whose `RPC_TransferAllToBountyBag` is the execute's cargo transfer). `AssassinExecute.cs` is
  the one allowlisted file: it pays the flat `ExecuteBounty`, which is income, not a steal, and is
  never announced (Maestro's ruling, 2026-09-12). The execute's steal is announced by
  `ChickenCargo.AnnounceExecuteSteal(victimCargo, victim)` — announce-only, no cargo write — called
  from `AssassinExecute.Press` on the **assassin's** authority, with the victim's `Cargo` read
  **before** the transfer RPC (in solo that RPC invokes locally and synchronously and zeroes it). It
  cannot be announced inside the RPC: that body runs on the victim's authority, which in Shared PvP is
  another peer, whose sink would credit nobody on the assassin's device.
- **Emits are local calls, never RPCs.** The tick-driven ones (`ResourceBanked`, `ResourceStolen`,
  `AbilityResolved`) are guarded by `Runner.IsForward`; the round edges are polled in
  `GameManager.LateUpdate`, **never** via a `ChangeDetector` (see `PlayerBase.LateUpdate` for why
  that is unreliable in `GameMode.Single`).
- **A sink must not throw** — every call is inline, mid-effect. Callers may rely on the bound sink
  never throwing: `ProjectInstaller` always binds `IMatchEventSink` as
  `GuardedMatchEventSink(MatchTracker)`, the one project-scoped `MatchTracker`, which
  `ProgressionService` also subscribes to. The guard catches everything, logs one `Error` per event
  kind and then only counts, so a broken sink can never half-apply a steal or bury the log. The
  tracker additionally contains its own subscribers' exceptions. Only `Installers/` may name the
  guard, the tracker or the service's concrete type. `NullMatchEventSink` stays in the repo for tests.
- **Wiring faults are loud once, never per emit.** Each emitting component logs one `Error` in
  `Spawned` if the sink is still null after injection; `GameManager` then leaves its round announcer
  null rather than throwing, so the match still starts.

Enforced by `ProgressionBoundaryTests` (contract surface, no direct steal credit, no direct bounty-bag
write outside the allowlist, both allowlists rot-checked, progression vocabulary, scanner self-tests
incl. block comments and expression-bodied members) and `MatchEventSinkTests` (one pin per emit site,
the five `ReceiveStolen` steal sites' amount, victim and credit/drain order, the execute as the sixth
steal site, no `GetChangeDetector(` in `GameManager`, the guard, the snapshot selector, and the sink
binding resolves to the guard wrapping the `MatchTracker` the `ProgressionService` listens to).

### Progression persistence

Settled 2026-09-13 (progression slice 2). The rules for anything that writes or reads the
progression journal.

- **JSON Lines via `JsonUtility`.** One `RoundOutcome` per line, UTF-8 without a BOM, at
  `persistentDataPath/progression/journal.jsonl`. Append-only. No new package: there is no Cloud Save
  and no Newtonsoft in the project. `RoundOutcome` stays `JsonUtility`-shaped (`[Serializable]`,
  public fields, arrays not dictionaries): anything `JsonUtility` cannot see vanishes silently.
- **One journal per process.** The `-progressionDir <path>` command-line switch
  (`ProjectInstaller.ResolveProgressionDirectory`, resolved at install, no I/O) moves the journal;
  `tools/run-clients.ps1` gives each local client `Builds/Windows/progression/clientN`, so test rounds
  never reach the real journal and two processes never append to one file.
- **Memory equals disk.** `JournalStore.Append` serializes the outcome once, writes exactly that string,
  and returns the record parsed back from it; the service folds that record, never the in-memory
  object. An outcome that would not read back is refused, not written.
- **Every record starts on its own line.** Before writing, `Append` adds a newline if the file does not
  end with one, so a partial line from an earlier failed append stays one malformed line instead of
  swallowing the next round.
- **Schema acceptance is `1..RoundOutcome.CurrentSchemaVersion`** (`RoundOutcome.IsReadableSchema`), in
  both the loader and `ProgressionRules.Evaluate`. Below 1 is not a record; above is a newer build's.
- **The rested day is stamped, not re-derived.** `MatchTracker` stamps `RoundOutcome.LocalDay`
  (`yyyy-MM-dd`, `ProgressionCalendar.FormatDay`) in the device's zone when the round ends, and the fold
  groups rounds by that stamp, so moving the device to another zone cannot change past balances. Lines
  without a usable stamp (written before it existed) fall back to their instant's day in the current
  zone. `TimeZoneInfo.Local` is read lazily (tracker: first round end; service: `Initialize()`), never in a
  constructor, and falls back to UTC with one warning.
- **Invariant culture for every hand-formatted number or timestamp.** Timestamps are ISO-8601
  round-trip (`"o"`, UTC) via `ProgressionCalendar.FormatUtc` / `TryParseUtc`. This machine is
  es-ES, where `44.97f.ToString()` is `44,97`. `JsonUtility` itself is culture-independent; the
  journal round-trip test runs under es-ES.
- **Facts only; the wallet is derived.** The journal stores what happened (placement, banked total
  from the standings, stolen total, tallies), never a currency amount. Grain, the balance and every
  stat are refolded from the journal against the current `ProgressionConfig.asset`
  (`ProgressionLedger.Fold`, deterministic and order-independent). A config change therefore changes
  past balances: that is intended while Grain is only earned, and the first slice that lets the player
  spend Grain must journal spending as facts of its own.
- **Write before show.** A round's line is appended and flushed to disk *before* `OnProfileChanged`,
  `OnRoundAwarded` or `LatestAward` can describe it. A failed write is an `Error` + `OnFault`, no award
  and no balance change. Nothing may show a number the journal does not back.
- **A round that cannot be evaluated is still journaled** (it is facts; a config fix credits it on a
  later load) but earns nothing and counts toward no total; it is reported as a `Warn` + `OnFault`.
- **Recover, never lose.** A torn final line that is a complete record gets its newline written; any
  other torn tail is moved to `journal.jsonl.torn` and cut; malformed middle lines are skipped and
  reported, never rewritten. `JournalStore` never throws, it reports in its results.
- **The award is the balance delta.** `RoundAward.Grain` is what the balance moved. If the device
  clock went backwards it can differ from the round's own Grain (even be ≤ 0), so UI shows the
  "+N Grain" line only when it is positive.
- **One day definition.** `ProgressionCalendar` is the only "local calendar day" in progression; the
  rested bonus uses it and later slices must reuse it.
- **No I/O at construction or install.** `JournalStore` and `ProgressionService` take a directory and
  touch no file until `Initialize()`; tests always use a temporary directory, never the real journal.

---

## Footguns (real bugs we hit)

### `LogLevel` collides with `Fusion.LogLevel`

Any file that uses both `CluckWars.Logging` and `Fusion` namespaces and references `LogLevel` unqualified gets `CS0104: ambiguous reference`. Fix with an alias at the top of the file:

```csharp
using LogLevel = CluckWars.Logging.LogLevel;
```

Hit in `8fca391`. `FusionNetworkService` was already disambiguated with `Logging.LogLevel.Verbose` inline.

### `Fusion.Assert` collides with `NUnit.Framework.Assert`

Same shape, different types. Any EditMode test file that imports `Fusion` (to assert
on `NetworkObject` / `NetworkTransform` wiring, say) gets `CS0104: 'Assert' is an
ambiguous reference` on **every** assertion. Alias it:

```csharp
using Assert = NUnit.Framework.Assert;
```

Hit while building `ProjectConfigTests.cs` (2026-07-22).

### `ChickenClass` is `byte`-backed — can't cast `-1`

`enum ChickenClass : byte`. `(ChickenClass)(-1)` doesn't fit in a byte and throws `CS0221`. Use a separate `bool _valid` flag instead of a sentinel value.

Hit in `85a96b3`.

### `const` with platform enums

`const AndroidSdkVersions x = AndroidSdkVersions.AndroidApiLevel24` is *legal* but trips some C# compilers. Use `var` or `static readonly` for platform / Unity enums.

### Two `ProjectInstaller`s could exist

Our `CluckWars.Installers.ProjectInstaller` and a stale Zenject `OptionalExtras/IntegrationTests/ProjectInstaller`. The OptionalExtras folder has been removed. If you re-import Zenject and it comes back, delete the OptionalExtras folder again.

### Line endings

Repo is LF-only, enforced by `.gitattributes`. Unity editor on Windows + IDEs sometimes save CRLF. If you see massive "this file changed entirely" diffs, run:

```
git add --renormalize .
```

Then commit the normalization pass.

### `EditorBuildSettings` scene order

`Bootstrap.unity` must be index 0. `CluckWarsBuildMenu.ValidateScenes` warns if it's not. If Maestro accidentally re-orders, the player starts in Game scene with no class selection.

### `ChickenStatsSO` resolution can fail

`ChickenController.Spawned` resolves `_activeStats` from `ChickenClassRegistrySO`. If the registry isn't bound or has no entry for the chicken's `Class`, it falls back to the prefab's `_fallbackStats` SerializeField. If THAT's also null, `_movement` is never instantiated and the chicken can't move. There's a verbose log at FUN entry to surface this.

### Camera follow can hide motion

The closer `MatchCamera._orthoSize` is, the less screen-relative motion you'll see when moving. If a tester reports "chicken doesn't move", check whether the camera is following first.

### Spawn collision drift

Two `CharacterController`s spawned at the same XZ shove each other in collision response — the loser drifts indefinitely. Mitigations in place: spawn jitter per `PlayerId` (`MatchBootstrapper.HandlePlayerJoined`) and a degenerate-`_baseCornerDistance` guard (`MapGenerator.ComputeSpawnPoints`). Don't undo either without replacing.

### `Plane`/`MeshCollider` tunneling on Android, `primitiveDefault` renders magenta

Two related footguns hit in `MapGenerator.BuildPlane` (fixed 2026-07-23, commit `5032ae9`):

- **Ground tunneling**: a `PrimitiveType.Plane` is a flat, paper-thin `MeshCollider`. On Android's lower/more variable framerate, a `CharacterController`'s per-step movement can tunnel straight through it — chickens fell forever. Fix: use `PrimitiveType.Cube` scaled thin (e.g. `(_planeSize, 1, _planeSize)`) for a real `BoxCollider`, same pattern `CreateWall` already uses for the arena walls. Any new runtime-generated ground/floor geometry should default to Cube, not Plane.
- **Magenta materials**: `GameObject.CreatePrimitive`'s default material uses the Built-in RP Standard shader, which URP can't render (shows magenta). Any material fallback chain must bottom out in a real project material (e.g. `_groundMaterial`) — never let it fall through to the primitive's own default.

### URP pipeline assets can silently corrupt without a compile or import error

`Assets/Settings/Mobile_RPAsset.asset` (the Render Pipeline Asset for the "Mobile" quality
tier) was found zeroed out — same byte count, every byte `0x00` — in commit `22642fe`
("Add iOS build…", 2026-08-17), and stayed that way, committed, for a month (fixed
2026-09-15, see `docs/STATE.md`). Nobody noticed because the Editor defaults to the "PC"
quality tier; the corruption only surfaces the moment something switches to the Mobile
tier (an Android build, or the Quality dropdown), at which point `QualitySettings
.renderPipeline` resolves to null for that tier, Unity falls back to the Built-in RP, and
**every** URP material on screen renders `Hidden/InternalErrorShader` (solid pink) — even
though every material and shader reference is completely intact.

This is the second time a `.asset`/`ProjectSettings` file in the URP chain has drifted or
broken outside of an intentional edit — see commit `9a0b965` ("incidental URP/
ProjectSettings churn") for the first, milder case (Editor auto-touching
`DefaultVolumeProfile.asset` / `UniversalRenderPipelineGlobalSettings.asset` on ordinary
use). Treat any diff touching `Assets/Settings/*RPAsset*.asset`,
`*UniversalRenderPipelineGlobalSettings.asset`, or `ProjectSettings/GraphicsSettings.asset`
/ `QualitySettings.asset` as worth a second look, especially if it shows as a same-size
binary diff (`git show --stat`) rather than a readable YAML change.

**Guardrail**: `ProjectConfigTests.EveryQualityLevel_ResolvesToANonNullRenderPipelineAsset`
(`Assets/_Game/Scripts/Editor/Tests/ProjectConfigTests.cs`) fails if any declared Quality
Level's `QualitySettings.renderPipeline` is null or not a real `RenderPipelineAsset`. Run
the EditMode suite after any change that touches the files above, not just after
gameplay-code changes.

---

## Asset / prefab GUID stability

- Never delete and recreate a `.cs` file via the file system without also handling the `.cs.meta`. The GUID in the meta is referenced by scene/prefab YAML.
- New scripts I (or other agents) author come with a hand-rolled `.cs.meta` containing a unique GUID. If a build fails with "script reference broken", check that the meta is committed alongside the script.
- Prefab variant inheritance: `Doppelganger.prefab` is a variant of `Chicken.prefab` with `ChickenCargo` stripped. Don't restructure the base prefab without verifying the variant still works.

---

## Folder conventions

- `Assets/_Game/Scripts/Editor/` — editor-only. Unity auto-compiles into the editor assembly. Don't reference editor APIs from runtime scripts.
- `Assets/_Game/Data/` — SO assets only (`.asset` files). Source SO `.cs` definitions live in `Scripts/Gameplay/`.
- `Assets/_Game/Resources/` — Unity loads anything here on demand. Used for `ProjectContext.prefab` which Zenject auto-discovers. **Don't dump general assets here** — bloats the build.

---

## UI rules (settled 2026-06-11 — do not re-litigate)

- **All menu/screen UI is UI Toolkit**: layout in `Assets/UI/*.uxml`, styling in
  `Assets/UI/Styles/CluckWarsTheme.uss`, logic in `MenuUiController` (binds data +
  navigation only — no layout code in C#). Design source of truth: `Design/*.jsx`
  wireframes + `docs/ART.md` tokens.
- **Never build menu UI procedurally in C#** (RectTransforms/LayoutGroups from code)
  and **never generate UGUI prefabs via editor scripts** — both approaches were tried
  and failed; see `docs/UI_HANDOFF.md` for the postmortem.
- The **in-game HUD** (`MatchHud`, `TouchControlsHud`, `DebugHud`, drawn with `UiGfx`)
  is the one legacy procedural-UGUI island. It works; leave it unless a full HUD
  migration to UXML is explicitly scheduled.
- **Verify UI changes visually**: with the Editor open, use Unity MCP
  `screenshot-game-view` in play mode after any UXML/USS change. UI work without a
  screenshot check is not done.
- `Assets/Resources/PanelSettings.asset` must stay `ScaleWithScreenSize` (`m_ScaleMode: 2`)
  — `ConstantPhysicalSize` re-broke mobile scaling once already (2026-06-01).

---

## Commit hygiene

- One concern per commit. Avoid "fix bug + add feature + refactor" mega-commits.
- Commit message body explains the *why*. Subject is the *what*.
- Co-authored attribution for agent contributions: `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>`.
- Build output is gitignored (`/Builds/`). `.gitattributes` enforces LF. `.editorconfig` keeps IDEs from re-introducing CRLF.

---

## When in doubt

- Architecture question: `docs/ARCHITECTURE.md`.
- "What does this code do today?": `git log --oneline -20` + `docs/STATE.md`.
- Build / device debugging: `docs/TESTING.md`.
- Design intent: `docs/GDD.md`, `docs/ART.md`, `docs/TDD.md`.
- Roadmap status: `docs/ROADMAP.md`.
