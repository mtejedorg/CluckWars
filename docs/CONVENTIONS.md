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

## Footguns (real bugs we hit)

### `LogLevel` collides with `Fusion.LogLevel`

Any file that uses both `CluckWars.Logging` and `Fusion` namespaces and references `LogLevel` unqualified gets `CS0104: ambiguous reference`. Fix with an alias at the top of the file:

```csharp
using LogLevel = CluckWars.Logging.LogLevel;
```

Hit in `8fca391`. `FusionNetworkService` was already disambiguated with `Logging.LogLevel.Verbose` inline.

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
