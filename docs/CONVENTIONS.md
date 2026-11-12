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
- **Animation is local.** `ChickenAnimator` plays on every peer from replicated state: the cast, peck and jump beats from the chicken's `ChickenLocalEvents`, the removed pose from `ChickenCombat.OnDeath` / `OnRespawn`. Nothing animation-related is RPC'd, and gameplay never calls the animator.
- **VFX, audio cues and HUD reactions are local.** Same pattern: observed from replicated state, never RPC'd. See "Replicated moments" below for the one rule.
- **One local chicken.** "The chicken this peer drives" is `ChickenController.Local` (`LocalDriven` for device input: never a bot). Do not scan for `HasInputAuthority`: a Doppelganger decoy shares its caster's.

---

## Zenject patterns

### Two contexts only

- `ProjectInstaller` (project-scope) — app-wide services, registries, log. Survives scene loads.
- `GameInstaller` (scene-scope, Game.unity) — match-scoped state (`MatchConfigSO`, `INetworkService`).

### Self-injection: one helper (A11 / A06, 2026-11-12)

Zenject injects what it builds: scene objects present at load, and anything an installer binds. Fusion-spawned network objects and components added at runtime with `AddComponent` are built outside any container, so they inject themselves, with **one line and nothing else**:

```csharp
[Inject]
public void Construct(ILogService log) => _log = log;

public override void Spawned()      // or Awake() for a plain MonoBehaviour
{
    SelfInject.Ensure(this, _log != null);
    // ... rest of init
}
```

`SelfInject` (`Scripts/Injection/SelfInject.cs`) does nothing when the second argument is true (Zenject already injected the object). Otherwise it injects from the scene container of the object's own scene, else from any live scene container (a network object can live outside the scene that holds the `SceneContext`), else from the project container. A scene container inherits every project binding, so a `Construct()` may take project-scoped and scene-scoped types (`INetworkService`, `IArenaReadiness`) alike and there is no call-site choice to get wrong: the old "project only" copy threw on the first scene-scoped parameter and crashed every chicken spawn (`14fbf8f`).

- **Never write `<context>.Container.Inject(this)`, `FindFirstObjectByType<SceneContext>()` or `ProjectContext.Instance.Container.TryResolve<T>()` in runtime code.** A dependency is a `Construct()` parameter. A guard test fails on any of these outside `SelfInject`, the installers and the two renderer features (assets with no injection point, which borrow the project logger for one error line).
- **Pass the null check of a dependency every `Construct()` sets**, normally `_log != null`.
- **Call it first, at the point the component first needs a dependency** (`Spawned`, `Awake`; a presentation component that only logs on a wiring error may call it on that error path).
- **It never throws.** A missing context or an unresolvable parameter is reported once per component type (`ILogService.Error`, source `SelfInject`, with Zenject's exception) and `Ensure` returns false. Read that line first when a spawned object misbehaves.
- **A new scene installer registers its container as the first line of `InstallBindings`** (`SelfInject.RegisterSceneContainer(this, Container)`, as `GameInstaller` does). Zenject's own `SceneContextRegistry` is filled at the scene kernel's `Start`, after the `Awake` calls that self-inject.
- **No guard on objects the container builds.** `FromNewComponentOnNewGameObject` keeps the new object inactive until it is injected, so `Construct` runs before `Awake` (the diagnostics probes, `ArenaReadinessDriver`, `FusionNetworkService`).
- `ProjectContext.HasInstance` is false until something reads `.Instance` (real bug `3bc2e13`). `SelfInject` reads `.Instance`; nothing else should need either.

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

Consumers handle empty fields gracefully — a null `AudioClip` plays nothing in `UnityAudioService`, which reports it once per call site (Warn, source `Audio`); a typed `SfxId` with no clip is reported once per id.

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

### Replicated moments: one rule (D2a, 2026-11-12)

How presentation (visuals, animation, audio, HUD) learns about a replicated gameplay moment. One pattern, no exceptions:

1. **A one-shot moment is a `[Networked] byte XEventId` bumped by the state authority**, with whatever ids describe it written to networked fields BEFORE the bump (`AbilityController.LastCastSlot` / `LastCastHitCount` then `LastCastEventId++`). The value wraps at 255 by design: only the change matters. Never reset one (writing 0 is itself an edge).
2. **Exactly one per-chicken local observer watches those counters**: `ChickenLocalEvents`, reached through `ChickenController.LocalEvents`. It seeds each baseline on its first observation (late-join safe), re-seeds on spawn / despawn, and raises plain C# events: `Cast`, `Knockback`, `SelfImpulse`, `Jump`, `Stolen`, `Deposited`. **Presentation subscribes; no component keeps its own `_last*EventId` cursor** (a guard test fails if a file under `Visuals/`, `UI/`, `Input/`, `AbilityLab/` or `Audio/` reads a counter). The cursor rules are in `EventEdge`: a change fires once, the wrap is a change, several bumps between two observations fire once.
3. **Payloads are small structs of ids, enums and numbers** (`CastEvent`: slot, ability asset, hit count, `CastArchetype`; `StealEvent`: the thief's id). **Never a localized string, never an `AudioClip`.** The listener resolves text through `UiText` / `UiKeys` and sound through a typed `SfxId` (`IAudioService.PlaySFX(SfxId)`), so a language pack swaps text and voice without touching gameplay.
4. **Gameplay never calls presentation.** Code in `FixedUpdateNetwork` changes replicated state and nothing else: no animator, VFX, camera, audio one-shot or floating text. A moment that gameplay notices in `Render` from continuous state is raised as a local C# event by the behaviour that owns the state (`ChickenLocalEvents.RaiseCargoChanged` / `RaiseAttackerNamed` / `RaiseZoneTriggered`, `ChickenCombat.OnDeath` / `OnRespawn`). `ChickenLocalEvents` references no Visuals, UI or Audio type; Visuals depends on Gameplay, not the reverse.
5. **Continuous replicated state stays as it is**: a `ChangeDetector` in `Render` on the NetworkBehaviour that declares the property (`ChickenCombat.IsRemoved`, `ChickenCargo.Cargo`), or polled by presentation (slot active mask, control flags, opacity).

Subscribing (pair every `+=` with a `-=`: `OnEnable` / `OnDisable` on the chicken's own components, bind / unbind when a HUD changes its chicken):

```csharp
private void OnEnable()
{
    if (_controller == null || _events != null) return;
    _events = _controller.LocalEvents;
    _events.Cast += OnCast;
}

private void OnDisable()
{
    if (_events != null) { _events.Cast -= OnCast; _events = null; }
    _castPending = false;
}
```

**Same-frame ordering.** `ChickenController.Render` observes once a frame. A listener that must act at its own point in the frame (after other work in its `LateUpdate`, in a HUD `Update`) does not act in the handler: it latches the event, calls `_events.Observe()` at that point, then consumes the latch. Whichever call sees the counter move first raises the event, so the listener acts in the same frame and at the same point as if it polled the byte itself. Observing is a few byte compares and allocates nothing.

```csharp
private void OnCast(CastEvent e) { _cast = e; _castPending = true; }

private void LateUpdate()
{
    _events?.Observe();          // this frame's edges, before the latch is read
    if (!_castPending) return;
    _castPending = false;
    // ... react to _cast (ids only: resolve text and clips here)
}
```

**Who hears a sound is a rule, written down.** A chicken's own cast and deposit sounds follow `OwnSoundRules`: only the human who drives that chicken hears them (never a bot's, a rival's or a decoy's). A sound played from gameplay plays on whichever peer holds state authority, which for bots is the master: that is the bug this rule replaced.

### ChangeDetector for continuous state

`ChickenCombat.Render` uses a `ChangeDetector` + cached `PropertyReader<T>` to turn a change of its own `[Networked]` state into a local C# event on every peer. Pattern reference:

```csharp
_detector = GetChangeDetector(ChangeDetector.Source.SimulationState);
_removedReader = GetPropertyReader<bool>(nameof(IsRemoved));

public override void Render() {
    foreach (var prop in _detector.DetectChanges(this, out var prev, out var cur)) {
        if (prop == nameof(IsRemoved)) {
            var (_, removed) = _removedReader.Read(prev, cur);
            if (removed) OnDeath?.Invoke(); else OnRespawn?.Invoke();   // presentation subscribes
        }
    }
}
```

The detector only resolves for the behaviour that declares the property. It is for state, not for one-shot counters: those go through `ChickenLocalEvents`.

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
`AbilityController.TryActivate` before the slot's `ActiveMask` bit, its active timer or `SetCooldown` are
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
