# Proposal: ability effect types — Aura vs Static AoE

**Status:** design only, awaiting Maestro. No code, assets or other docs changed.
**Date:** 2026-10-21 · **Branch studied:** `work/backlog-2026-10-21` (HEAD `f291d18`)
**Trigger:** BACKLOG "Smoke Roost" decision (2026-10-21). Maestro rejected a per-ability `FollowsCaster` flag.
Ability effects get **types** instead: an **Aura** (follows its caster) and a **Static AoE** (placed once, stays fixed).
Smoke Roost stays a Static AoE. Future abilities will be Auras.

---

## 0. TL;DR

- **The type is data on the ability asset.** Add `AreaEffectKind : byte { None = 0, StaticAoE = 1, Aura = 2 }` as a
  serialized field on `AbilityBaseSO`. Each `.asset` declares its kind. The tuned numbers stay in the fields they
  already use, so no balance value moves and the Balance Editor needs no change.
- **One runtime object for both kinds.** `AbilityZone` stays the only area-effect NetworkObject. It gets one new
  `[Networked]` byte that says whether it is anchored to the world (Static) or to its caster (Aura). It reuses the
  `OwnerChicken` id it already stamps as the anchor.
- **Recommended Aura sync: no moving networked object.** An Aura zone does **not** get a `NetworkTransform` and is
  **not** parented. Every peer works out its centre from the caster's already-replicated position (option C2 in §2).
  After the spawn it costs no bandwidth, late joiners get it from the normal snapshot, and `ChickenController` keeps
  its `[Networked]` layout. The only layout change is one byte on `AbilityZone`.
- **Overlap code reads one `Center` property** instead of `transform.position`, so the order of `FixedUpdateNetwork`
  calls cannot matter.
- **Existing abilities:** Feather Trap, Root Egg and Smoke Roost become `StaticAoE`, with no behaviour change. Feather
  Aura is already an aura but uses a separate mechanism (three `[Networked]` fields on `ChickenController`). Phase 2
  moves it onto the anchored zone and deletes that mechanism. The move also fixes a latent bug: the caster wears the
  slowed badge on themselves (§4.3).
- **World hash:** anchored zones must leave the position-based `static` hash, because they move with a chicken that
  peers see about 3 m apart. They get their own hash keyed by anchor id instead.
- **Estimate:** about 2–3 dev-days in total, plus one full mode × round matrix run.

---

## 1. How area effects work today (traced)

### 1.1 `AbilityZone` (Static AoE in all but name)

`Assets/_Game/Scripts/Gameplay/AbilityZone.cs`, one prefab `Assets/_Game/Prefabs/Abilities/AbilityZone.prefab`
(NetworkObject + `AbilityZone` + `AbilityZoneVisuals`, **no NetworkTransform**).

| Piece | What it does |
|---|---|
| Spawn | Each ability SO calls `ctx.Runner.Spawn(PrefabRegistry.AbilityZone, pos, …, onBeforeSpawned: …)`. The same block is copied three times (Feather Trap, Root Egg, Smoke Roost). |
| `[Networked]` layout | `LifetimeTimer`, `Consumed`, `Effect` (`ZoneEffect : byte` Slow/Root), `SlowFactor`, `RootDuration`, `NetworkedRadius`, `OwnerChicken` (`NetworkBehaviourId`), `SpawnPosition`, `HasSpawnPosition`. |
| Position (CW-217, `f135268`) | The SO stamps `SpawnPosition`/`HasSpawnPosition` in `onBeforeSpawned`, and the authority self-stamps as a fallback. Non-authority copies move to the stamp in `Spawned` and retry from `Render` (`SpawnPlacement.ProxyMustMove`). Before the fix, every non-owner peer left the zone at the prefab root, so remote players were never slowed or rooted. |
| Registry | A static list, `AbilityZone.ActiveZones`, maintained in `Spawned`/`Despawned`. |
| Slow application | **On each chicken's own state-authority peer**: `ChickenController.FixedUpdateNetwork` → `CheckAbilityZoneSlow()` iterates `ActiveZones`, skips `OwnerChicken == Id`, tests `(zone.transform.position - transform.position).sqrMagnitude <= r²` (**3D**), and calls `ApplySlow(SlowSource.Ability, zone.SlowFactor)`. No RPC is involved. |
| Root application | **On the zone's state-authority peer**: `AbilityZone.FixedUpdateNetwork` → `TryTriggerRoot()`, a **planar** XZ test over `ChickenController.ActiveControllers` that skips the owner, decoys and removed chickens. It sends `chicken.RPC_ApplyRoot(RootDuration)` and sets `Consumed = true`. |
| Feedback | Every peer, in `Render`: the `Consumed` false→true edge (root) or the rival-occupancy rising edge (slow) leads to `HitFeedback.NotifyAttacker(zone centre)` for victims and `NotifyZoneTriggered()` for the caster. No RPC is involved. |
| Despawn | Authority: `LifetimeTimer.Expired || Consumed` → `Runner.Despawn`. |

### 1.2 Feather Aura (a real aura, built a different way)

`FeatherAuraAbilitySO` → `ctx.Controller.Effects.SetAura(slot, factor, radius)` (the `AbilityEffectStack` aura channel,
"any, strongest slow wins"). `ChickenController.SyncAbilityEffects()` mirrors the result into three `[Networked]`
properties on the **caster's** controller: `AuraSlowActive`, `AuraSlowRadius`, `AuraSlowFactor`. Every chicken's
authority runs `CheckAuraSlow()` in its own tick. That method iterates `ActiveControllers`, finds casters with
`AuraSlowActive`, tests the 3D distance to the caster's `transform.position`, and calls
`ApplySlow(SlowSource.Ability, factor)`.

So an aura that follows its caster **already ships**, with no moving networked object. The victim's peer evaluates it
from the caster's replicated transform. This is the precedent for the recommendation below. Its limits:

- Each new aura effect (root, steal, knockback…) would need new `[Networked]` fields on `ChickenController`, the most
  central behaviour in the game.
- Only one aura per caster can be resolved (the strongest-slow entry, using *its* radius).
- The aura's lifetime is fixed to the ability's `Duration`.
- It has no world visual of its own. Only the victims' snail badge and snare pulse show it.
- Its emitter flag is misread by two visual consumers (§4.3).

### 1.3 Doppelganger (relevant only as a contrast)

`DoppelgangerAbilitySO` spawns a Chicken-prefab variant **with a NetworkTransform**, whose input authority is the
caster. It is a moving *entity* that mirrors input, not an area effect, so it gets `AreaEffectKind.None`. It shows the
cost of the "NetworkTransform" option (A below): a second independently interpolated transform per object.

### 1.4 Other pieces the design must fit

- **`AbilityBaseSO`** already declares behaviour as data or virtual descriptors: `AimShape` (`AbilityAimShape.Aura = 3`
  exists, "persistent circle, follows"), `PlacesZone`, `ReportsCastHits`, `IsStealth`, `CanActivate` (the wiring gate,
  through `ctx.CanSpawnZone`). Enums that reach `.asset` files are `byte` and append-only (see `CastArchetype` remarks).
- **`AbilityRegistrySO.All`** is the shipped pool. Feather Aura, Feather Trap, Root Egg and Smoke Roost are all in it.
- **Balance Editor** (`Assets/_Game/Editor/BalanceEditorWindow.cs`) finds every visible serialized field by reflection
  (`DrawAbilityEffectValues`). A new serialized enum appears there automatically. Fields left where they are need no
  editor change.
- **`docs/TEST_STRATEGY.md`:** rule 6 (cross-peer parity via the `World` hash) and the checklist "new or changed
  networked object → NetworkTransform or stamped position, add to the World hash". `InvariantProbe.World` hashes
  `AbilityZone.ActiveZones` positions at 0.5 m resolution into `static`. `merge.ps1` flags a `static` mismatch that
  lasts 2 or more heartbeats.
- **Prefab guard:** `SpawnPlacementTests.EverySpawnedNetworkObjectPrefab_HasANetworkTransformOrAStampedSpawnPosition`.
- **CONVENTIONS:** `HasStateAuthority` gate at the top of every FUN, cross-authority writes via RPC, VFX are local and
  driven from replicated state, `onBeforeSpawned` for tick-zero `[Networked]` values, and silent failure vs legitimate
  no-op (an unresolvable anchor is a legitimate state, see §3).

---

## 2. Design

### 2.1 (a) The effect-type model — where it lives, how it is authored

```csharp
// Assets/_Game/Scripts/Abilities/AreaEffectKind.cs  (new)
/// What kind of lasting area an ability leaves behind. Byte + explicit, append-only values: persisted by ordinal in .asset files.
public enum AreaEffectKind : byte
{
    None      = 0, // no lasting area: self-buffs, instant bursts resolved at cast (Cluck Shock, Dust Kick…), Doppelganger
    StaticAoE = 1, // placed once, stays where it was put (Feather Trap, Root Egg, Smoke Roost's cloud)
    Aura      = 2, // centred on its caster for its whole life (Feather Aura; future auras)
}
```

**On `AbilityBaseSO`** (data, authored per asset):

```csharp
[Header("Area effect")]
[Tooltip("None = no lasting area. Static AoE = placed once, fixed. Aura = follows the caster.")]
public AreaEffectKind AreaKind = AreaEffectKind.None;
```

**Why a field and not a virtual property.** Maestro asked for types *as data*, so the type has to be authored, not
inferred. A virtual property would make it code. `None` as the zero default means every ability that leaves no area
needs no asset edit. Only four `.asset` files change, and each gains one line.

**Numbers do not move (phase 1).** Every zone ability exposes a read-only, code-built `AreaEffectSpec` from the fields
it already serializes:

```csharp
// Assets/_Game/Scripts/Abilities/AreaEffectSpec.cs  (new; plain struct, not serialized)
public readonly struct AreaEffectSpec
{
    public readonly AreaEffectKind Kind;      // copied from AbilityBaseSO.AreaKind
    public readonly ZoneEffect     Effect;    // Slow | Root (existing byte enum, append-only)
    public readonly float Radius, Lifetime, SlowFactor, RootDuration, ForwardOffset;
    public readonly bool  ConsumeOnTrigger;   // Root Egg true
    public readonly bool  EndsWithAbility;    // Aura only: despawn when the casting slot deactivates (Feather Aura)
}
// AbilityBaseSO:  public virtual bool TryGetAreaEffect(out AreaEffectSpec spec) { spec = default; return false; }
```

Feather Trap, Root Egg, Smoke Roost and Feather Aura override `TryGetAreaEffect` from their existing fields
(`ZoneRadius`, `ZoneDuration`, `SlowFactor`, `EggLifetime`, `AuraRadius`…). Nothing is renamed, so the `.asset` YAML,
the Balance Editor, `AbilityDescriptionTests` and the oracle all stay as they are. If two or more auras exist later,
the fields can be folded into one serialized `AreaEffectSpec` with an editor migration (phase 3, optional).

**One spawner instead of three copies.** A new `AreaEffectSpawner.Spawn(AbilityContext ctx, in AreaEffectSpec spec, Vector3 pos)`
does the `Runner.Spawn` and the `onBeforeSpawned` stamping (the Error log for a prefab missing its `AbilityZone`
included). Each SO's `OnActivate` shrinks to "compute the position, call the spawner" plus its own extras (Smoke
Roost's fade). `CanActivate` keeps calling `ctx.CanSpawnZone`.

**Derived descriptors (one source of truth):**

| Descriptor | Today | Proposed |
|---|---|---|
| `PlacesZone` | virtual, `true` in 3 SOs | `=> AreaKind == StaticAoE` (non-virtual). Phase 2 decides about Aura, see Q3. |
| `AimShape` | virtual per SO | unchanged, but test-locked: `AreaKind == Aura ⇔ AimShape == Aura`; `StaticAoE ⇒ ForwardCircle or SelfCircle` |
| `ZoneEffect` | `Slow`/`Root` | unchanged (what the area *does* is orthogonal to whether it *moves*) |

The two axes are kept apart on purpose. **Kind** answers whether the area moves. **`ZoneEffect`** answers what it
does. A future "root aura" or "steal aura" is a new `ZoneEffect` member (appended), never a new kind.

### 2.2 (b) How each type syncs

**Static AoE: unchanged from CW-217.** It is stamped once in `onBeforeSpawned` (`SpawnPosition` + `HasSpawnPosition`),
moved into place on proxies by `SpawnPlacement`, and hashed by position. No per-tick traffic.

**Aura: options evaluated** (Fusion 2, Shared Mode, 32 Hz tick, 4 seats; the zone's state authority is the peer that
spawned it, which is the caster's peer):

| | A. `NetworkTransform` on the zone | B. Parent to caster's NetworkObject | **C2. Evaluate from caster's replicated position (recommended)** |
|---|---|---|---|
| How | The zone authority copies the caster position into the zone transform each tick, and NT replicates it. | Reparent the zone under the caster's chicken; Fusion parent sync carries it. | The zone carries `Anchor = Caster` + `OwnerChicken`. Every peer computes `Center = owner.transform.position` and never networks a position. |
| Bandwidth | Position (+ rotation) delta every tick the caster moves, for the zone's whole life. It duplicates data the caster's NT already sends. | Same NT cost (parent sync still needs NT on the child), plus parent changes. | **0 per tick.** One spawn snapshot (≈ 40 B of existing fields + 1 byte). |
| Agreement with caster | Two independently interpolated transforms. Because of FUN order, the zone can lag its caster by a tick on the authority (≈ 0.4 m at 12 m/s), and proxies show the drift. | Exact visually, but see risks. | **Exact by construction:** each peer uses the same caster transform it already uses for collision slow and Feather Aura. |
| Late join | OK (NT snapshot). | Fragile: parent must resolve before child. | OK: `OwnerChicken` is in the snapshot. If the owner is not resolved yet, the zone is **inert and hidden** until it is (retry in `Render`/FUN, like `ApplySpawnPosition`). It never sits at the prefab root. |
| Caster leaves / despawns | Zone keeps moving to stale data until its own lifetime ends. | **Dangerous**: Unity destroys child GameObjects with the parent, so a NetworkObject dies outside `Runner.Despawn`. That breaks pooling and logs Fusion errors. | Owner unresolved, dead or removed → authority despawns the zone next tick. Proxies treat it as inert until then. |
| Side effects | All zones pay NT unless a second prefab is added; the prefab guard passes. | `ChickenVisuals` scans `GetComponentsInChildren<Renderer>` (lines 109/137), so a parented zone's ring would be swapped to the ghost material and tinted with the chicken. Round-reset teleport and decoy logic also see an extra child. | `AbilityZone` gains one `[Networked] byte`. No change to `ChickenController` or the prefab hierarchy. |
| `[Networked]` layout change | Prefab component, no field | Prefab component, no field | 1 byte on `AbilityZone` |

**Recommendation: C2, a caster-anchored `AbilityZone`.** It keeps one object type for both kinds, so the registry,
lifetime, consumption, visuals, hit feedback and World hash bookkeeping are shared. It still uses the sync model that
Feather Aura and collision slow have already proven in shipped play.

**Trade-off C2 accepts:** victims are tested against the caster's position as their own peer sees it. That lags the
caster's own view by the interpolation delay (`merge.ps1` assumes ~0.25 s, which is the 3 m `PosTolerance`). This is the
same latency every caster-centred effect already has: Feather Aura, collision slow, Spine Coat steal-back. The lag
favours the victim (the aura reaches them slightly late), which is the conventional and acceptable direction. A, with
its extra interpolated transform, would not remove the lag. It would add drift on top of it.

**Why not C1** (generalize Feather Aura's `[Networked]` triplet on `ChickenController`): each new aura effect grows
the chicken's networked layout, auras could not outlive the cast, it allows one aura per caster, and auras would get
no visuals path. C2 gives all of that through the zone that already exists.

**`AbilityZone` changes (C2):**

```csharp
public enum ZoneAnchor : byte { World = 0, Caster = 1 }   // append-only
[Networked] public ZoneAnchor Anchor { get; set; }          // stamped in onBeforeSpawned from spec.Kind
[Networked] public byte SourceSlot { get; set; }            // Aura + EndsWithAbility only (Feather Aura)

/// The one centre every overlap and feedback test uses. World: the stamped/placed transform.
/// Caster: the owner's live transform; false while the owner is unresolved (legitimate: late join / owner gone).
public bool TryGetCenter(out Vector3 center);
```

`SpawnPosition`/`HasSpawnPosition` stay, so the prefab guard is unaffected and a Caster zone remembers where it was
cast (useful for the "zone hit you from here" bearing, see Q5). In `Render`, a Caster zone sets
`transform.position = center` for visuals only. Simulation never reads the transform of a Caster zone.

### 2.3 (c) Overlap and application on the owner peer

The rule is unchanged: **a chicken's own state authority decides whether it is slowed**, and the zone's authority
decides one-shot triggers. Only the centre lookup changes.

| Effect | Who evaluates | Static AoE | Aura (C2) |
|---|---|---|---|
| Slow | each chicken's authority, `CheckAbilityZoneSlow` | `zone.TryGetCenter` → stamped position | `zone.TryGetCenter` → owner's transform **on this peer**. Unresolved → skip. |
| Root (consume) | zone authority, `TryTriggerRoot` | as today | same code. The centre is the owner's transform on the zone authority, which is the caster's own peer, so it is the true position. |
| Feedback (`Render`, every peer) | `IsCaught` / `NotifyCaughtVictims` | as today | same, via `TryGetCenter`. |
| Self-exclusion | `OwnerChicken == Id` | as today | as today (an aura never hits its own caster) |
| Decoys / removed | skipped in root and feedback, **not** in slow | unchanged | unchanged (consistency fix → Q6) |

**Despawn rules (zone authority, top of FUN after the `HasStateAuthority` gate):** `LifetimeTimer.Expired || Consumed`
(as today). A Caster zone also despawns when the owner is unresolved, dead or removed, or when
`EndsWithAbility && !owner.Abilities.IsSlotActive(SourceSlot)` (the accessor exists, `AbilityController.cs:125`). The
caster's slot and the zone have the same state authority, so no RPC is needed. Shared Mode bots belong to the master
client, so for a bot caster both objects are on the master too.

**Stacking.** `ApplySlow` already takes the minimum across all sources each tick, so two auras resolve to "strongest
wins", which matches the `AbilityEffectStack` aura rule. One subtle difference: today a caster with two auras uses
only the strongest entry's *radius*. With zones, each aura keeps its own radius. No ability can produce two auras on
one caster today. This is noted, not flagged.

**Distance metric.** `CheckAbilityZoneSlow` and `CheckAuraSlow` use 3D `sqrMagnitude`. `TryTriggerRoot`, `IsCaught`,
`AbilityAim` and the drawn ring are planar. The new `TryGetCenter` path should **keep each call site's current metric**
in phase 1, so there is no balance drift. Unifying on planar is a small reach change on the mound and ramps, and stays
a separate decision (Q6).

---

## 3. (d) Mapping of existing abilities

| Ability | Class | Today | `AreaKind` | Behaviour change |
|---|---|---|---|---|
| Feather Trap | Speedy | `AbilityZone`, Slow 0.45, r 3.6 m, 4.5 m ahead, 5 s | **StaticAoE** | none |
| Root Egg | Fatty | `AbilityZone`, Root 2 s, r 1.45 m, at feet, 8 s, consumed on trigger | **StaticAoE** | none |
| Smoke Roost | Assassin | `AbilityZone`, Slow 0.55, r 4 m, at feet, 5.5 s + self fade 0.2 for 3.5 s | **StaticAoE** (Maestro, 2026-10-21) | none. The fade stays an `Effects.SetOpacity` self-buff, which is not part of the area. |
| Feather Aura | Speedy | `ChickenController.AuraSlow*` triplet, Slow 0.55, r 5.4 m, follows caster for `Duration` 3 s | **Aura** | phase 1: tag only, still runs on the triplet. Phase 2: becomes a Caster-anchored zone with `EndsWithAbility`, and the triplet is deleted. |
| Spine Coat | — | caster-centred steal-back on *contact* (`CollisionSlowRadius` 1.2 m) | None | none. It is a contact self-buff, not an area, and stays out of scope (Q7). |
| Cluck Shock, Stun Burst, Ground Quake, Belly Flop, Dust Kick, Wing Slam, Headbutt, Snatch… | — | resolved once at cast via `GatherTargets` (Dust Kick applies a timed slow by RPC) | None | none. These are instant areas, not lasting ones. |
| Doppelganger | Assassin | spawned decoy entity with a NetworkTransform | None | none |

### 3.1 Latent bug found while tracing (not part of this design, but phase 2 removes it)

`ChickenNameplate.StateIcon()` (`ChickenNameplate.cs:364-366`) and `ChickenStateOverlays.IsSlowed/IsSnared`
(`ChickenStateOverlays.cs:350, 376`) treat `_controller.AuraSlowActive` as "this chicken is slowed". That flag is the
**emitter** flag: it means "this chicken is *casting* Feather Aura". So by code reading, a Feather Aura caster shows the
snail badge and the snared pulse on **themselves**, and victims get no early local cue from it. This is found by
reading the code only; I have not seen it in a build. Phase 2 deletes the flag, and these three reads go with it (the
replicated `ControlVfx` bits already drive the victim side). If phase 2 is not approved, this needs its own small
backlog fix.

---

## 4. (e) Migration steps and tests

### Phase 1: the type model, static zones on it, anchored zone ready (no gameplay change)

1. Add `AreaEffectKind`, `AreaEffectSpec`, and `AbilityBaseSO.AreaKind` + `TryGetAreaEffect`.
2. Author `AreaKind` on the four assets (one YAML line each). Make `PlacesZone` derived.
3. Add `AreaEffectSpawner` and route Feather Trap, Root Egg and Smoke Roost through it, with byte-identical stamping.
4. On `AbilityZone`: add `Anchor` + `SourceSlot` + `TryGetCenter`. Every simulation and feedback read of
   `transform.position` goes through it. Add the Caster-anchor despawn rules and the Render-only transform follow.
5. In `ChickenController.CheckAbilityZoneSlow`: use `TryGetCenter`, keeping the 3D metric.
6. `InvariantProbe.World`: World zones stay in `zones`/`static`. Caster zones are excluded from it and logged as
   `auras=<hash>/<n>`, hashed over `(type, OwnerChicken raw id, radius bucket, effect)`. That hash is
   position-independent but still catches "a peer is missing an aura" or "a peer has the wrong radius". `merge.ps1`
   gains the same 2-heartbeat parity rule for `auras`.
7. No shipped ability uses `Anchor = Caster` yet. The path is exercised by tests and by the AbilityLab (step 9).

### Phase 2: Feather Aura onto the anchored zone

8. Feather Aura's `OnActivate` spawns a Caster zone (`EndsWithAbility`, `SourceSlot = ctx.Slot`, lifetime =
   `Duration` as a backstop). `OnDeactivate` does nothing, because the zone despawns itself. Delete the `SetAura`
   channel, `AuraSlowActive/Radius/Factor`, `CheckAuraSlow`, and the three visual reads (§3.1). This is a
   `[Networked]` layout change on `ChickenController` (removal). It is fine because every peer runs one build (the SHA
   parity rule).
9. AbilityLab / Single mode: verify the aura ring follows the caster, and that a bot victim is slowed and pulses.

### EditMode tests (new or extended)

| Test | Locks |
|---|---|
| `AreaEffectKind_Ordinals_ArePinned` | None=0, StaticAoE=1, Aura=2 (append-only, like `CastArchetype`) |
| `EveryRegistryAbility_AreaKind_AgreesWithAimShape` | Aura ⇔ `AimShape.Aura`; StaticAoE ⇒ ForwardCircle/SelfCircle; `PlacesZone ⇔ StaticAoE` |
| `AreaAbilities_TryGetAreaEffect_MatchesTheirFields` | spec radius/lifetime/factor equal the authored fields, so phase 1 cannot drift a number |
| `SmokeRoost_IsAStaticAoE` | pins Maestro's decision so a later "make it follow" is a visible, deliberate change |
| `AreaAbilities_RefuseWithoutAbilityZonePrefab` | extend `AbilityWiringGateTests.ZoneAbility_RefusesWhenAbilityZonePrefabIsUnassigned` to every `AreaKind != None` |
| `ZoneCenter_*` (pure helper, in `SpawnPlacementTests`) | World → stamp. Caster resolved → owner position. Caster unresolved → *no centre*, never the prefab root. |
| `AbilityZonePrefab_HasNoNetworkTransform` | locks the C2 decision; the existing prefab guard keeps passing through the stamp |
| `WorldHash_CasterZones_AreExcludedFromStatic` | two synthetic peers with the caster 3 m apart → identical `static` and `auras` hashes; a missing aura → different `auras` |
| `NoSimulationRead_OfZoneTransform` | source scan in the style of `AbilityAimTests.NoAbilityScript_CallsPhysicsOverlapDirectly`: overlap code uses `TryGetCenter` |
| (phase 2) `NothingReads_AuraSlowActive` | the emitter flag and its visual misuse are gone |

`merge.ps1` self-test (`tools/test-session/selftest.ps1`): add synthetic logs for an `auras` mismatch, and for a moving
aura that must **not** trip `static`.

### Matrix (TEST_STRATEGY rule 4: spawning/Fusion change → all four modes)

`scenario-pc.ps1 -Mode solo|shared2|shared4|hostleave -Rounds 3`, requiring `VERDICT: PASS` and 0 `SYNC`/`INVARIANT`.
Proposed additions:

- **Remote-victim state check.** This is the CW-217 lesson: check positions on a *client*. In shared2/shared4, a dev
  `AutoTest` step has a remote peer's chicken cast Feather Aura (phase 2) and walk into a rival. The `Invariant` probe
  samples the victim's replicated `ControlVfx.Snared` on every peer while the distance is inside the radius. This is
  evidence from state, not logs (rule 1).
- **Round-start invariant:** `zones=0` and `auras=0` at round start. Today nothing despawns zones at round reset
  (`RPC_ResetControlStates` does not touch `ActiveZones`), and correctness relies on lifetimes (≤ 8 s) being shorter
  than the intermission. A Caster zone whose owner is teleported at reset would also be carried to the spawn corner.
  Phase 1 should despawn all zones at round reset, or at least assert this.
- **hostleave:** a bot's aura must despawn or migrate with its bot, and must not be orphaned. See risk R3.

---

## 5. (f) Estimate and risks

| Phase | Work | Estimate |
|---|---|---|
| 1 | type model, spawner refactor, anchored zone, World-hash split + merge rule, tests | ~1–1.5 days (`senior-dev`) |
| 2 | Feather Aura migration, `ChickenController` triplet removal, visual-read cleanup, tests | ~0.5–1 day |
| Verify | full matrix (4 modes × 3 rounds, unattended) + triage; AbilityLab visual check | ~0.5 day |
| **Total** | | **~2–3 days** |

| # | Risk | Mitigation |
|---|---|---|
| R1 | A refactor drifts a tuned number, or the stamp order of the three SOs | `TryGetAreaEffect` field-equality test; descriptions test already ties text to fields; spawner stamps the same fields in the same order |
| R2 | Overlap code still reads `zone.transform.position` somewhere. On the authority, a Caster zone's transform is only Render-updated, so a stale read could be a frame behind. | source-scan test; `TryGetCenter` is the only accessor that simulation uses |
| R3 | Shared Mode authority: the zone's state authority (spawner) and the anchor's authority can differ after host-leave (bot chicken migrates to the new master, zone flags may destroy or orphan it). I have not verified the `AbilityZone` NetworkObject flags (`Flags: 262145` in the prefab) against Fusion 2's state-authority-leave options. | Check the flags before phase 1. hostleave matrix run. The anchor-unresolved/dead despawn rule caps the damage. |
| R4 | World-hash false positives if Caster zones stay in `static` | split in step 6 + selftest fixture |
| R5 | Small behaviour change for a hypothetical double aura (per-zone radius) | none shipped; documented in §2.3 |
| R6 | `ChickenController` `[Networked]` removal in phase 2 breaks cross-build play | already a rule: one commit for every platform (SHA parity) |
| R7 | Bots: `BotTactics` treats `AimShape.Aura` as a radial self-cast with reach = radius. That is still correct, but a bot does not know an aura *persists* and may recast it pointlessly. | out of scope; Q8 |

---

## 6. (g) Open questions for Maestro

1. **Is C2 (no moving networked object, evaluated from the caster's replicated position) acceptable** as the aura sync
   model? It is the same model Feather Aura uses today. The alternative is a NetworkTransform on the zone, which costs
   bandwidth and adds drift. *Recommendation: C2.*
2. **Approve phase 2** (move Feather Aura onto the anchored zone and delete the `ChickenController` aura fields)? It
   gives one aura mechanism instead of two and fixes the self-snail badge (§3.1). *Recommendation: yes.*
3. **Cast feedback for auras:** today Feather Aura reports cast-time hits (sparks on rivals inside at cast). Should an
   Aura instead behave like a placed zone (no cast-time verdict; confirm on the first rival entry, as Feather Trap
   does)? *Recommendation: like a placed zone. It is a persistent effect, and the first-entry confirmation already
   exists in `AbilityZone.Render`.*
4. **Caster death or removal while an aura is live:** despawn the aura (proposed), or let it detach and finish as a
   static zone where the caster fell?
5. **Where an aura's hit bearing points:** the zone centre is now the caster's live position, so FEEDBACK.md §1.6
   (do not leak a rival's live position) behaves differently. An aura is *on* the caster, so pointing at them is
   honest and leaks nothing the victim cannot already see. Confirm.
6. **Consistency fixes (separate, small balance shifts):** unify slow-zone and aura distance on planar XZ like
   root/aim/ring (slightly larger reach on mound and ramps), and decide whether a decoy should be slowed by zones
   (today slow catches decoys and root does not).
7. **Spine Coat:** keep it as a contact self-buff (proposed), or eventually re-express it as a 1.2 m steal Aura?
8. **First real aura ability:** is there a named design waiting? It decides whether `ZoneEffect` needs a new member
   (e.g. a root or steal aura) in phase 1, and whether bots need "aura already up, don't recast".
9. **Round reset:** OK to despawn every live zone at round reset? It is safer than relying on lifetime vs intermission,
   and it makes round N start equal round 1 start, which `TEST_STRATEGY` rule 5 requires.
