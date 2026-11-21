# BL-206 — Peck becomes an interruptible cast (implementation spec)

Date 2026-11-21. Author: code-architect (opus). Status: ready for implementation, not started.
Branch `develop` (= `balance`). Implementer: `senior-dev` driving `developer-sonnet`, one batch at a time, commit per green batch.

Binding inputs (read them, do not re-decide them):
- `docs/superpowers/specs/2026-11-20-balance-oracle-v2-design.md` section 6.2 and 8c **D2a**: the food is taken when the
  cast ends; a cut cast takes nothing; a **stun or a shove** cuts it, and so do an **execution** and the chicken's **own
  next move**; a **slow** or **being robbed** does not.
- Maestro: "la animación debe adaptarse siempre al tiempo de casteo definido en el balancer": one full play of the Peck clip
  equals the effective cast time on every peer and every class model, no hard-coded durations or speed constants; a cut
  stops the animation at once and visibly; animation and VFX stay local (ChangeDetector / Render polling), never RPC.
- Session notes (side-session folder `local_d38a8c4a-...`): `bl206-peck-cast-map.md` (code map), `bl206-decisions.md`
  (decisions 1-9), `bl206-ux-spec.md` (feedback, MVS).

Line numbers below were verified on 2026-11-21 at `32767efd`; **re-grep every site before editing** (CODE-PROC-08).

---

## 0. Ground rules for every batch

- A parallel session owns the Oracle files (`Assets/_Game/Scripts/Balance/Oracle*.cs`, `FarmSim.cs`, `BalanceSctCheck.cs`,
  `DevBalanceEditor.cs`, their tests, `UiText.csv`, `ProjectSettings/*`, `BUILD_NUMBER`). Batches 1-4 do not touch them.
  Batch 5 starts only after that session's commit lands. Do not start any batch while that session is compiling or running
  tests in Unity (ask the caller; CODE-BUILD-03). `git status` first; stage by explicit path; no `git add -A`, stash, reset.
- Constraints from CLAUDE.md: `ILogService` only (ARCH-ERR-01); `HasStateAuthority` gate (ARCH-NET-01); no presentation
  call from `FixedUpdateNetwork` (ARCH-EVT-05); code outside `Scripts/Abilities/` never names `PeckAbilitySO` (ask
  `AbilityJob.Peck` / `FindPeckSlot()`); no new MonoBehaviour, so no new `SelfInject.Ensure` line.
- No shipped number changes except the three new cast values. Networked layout changes: exactly one property added
  (`AbilityController.PeckLandedEventId`); nothing removed or reordered (ARCH-NET-14).
- Test runs: EditMode through `TestRunnerApi` from `script-execute` with a class filter, polled result file; check
  `scriptCompilationFailed` first (CODE-BUILD-02, CODE-TEST-15). Full suite once at the end of batch 4.
- Tests: namespace `CluckWars.Tests`; no shipped asset value restated as a literal (compare against the asset, or assert a
  relationship) — CODE-TEST-18.

---

## Batch 1 — Data plumbing (no behaviour change yet)

Owned files:
- `Assets/_Game/Scripts/Gameplay/ChickenStatsSO.cs`
- `Assets/_Game/Data/Classes/Warrior.asset`, `Speedy.asset`, `Fatty.asset` (not `Assassin.asset`)
- `Assets/_Game/Scripts/UI/DebugHud.cs`, `Assets/_Game/Editor/BalanceEditorWindow.cs`
- `tools/balance/export_kitlab_data.py`, `tools/balance/kitlab_map.py`, `tools/balance/kitlab/kitlab.template.html`,
  `tools/balance/tests/test_kitlab.py`
- Tests: `DataIntegrityTests.cs`, `EconomyAndPilesTests.cs`

Changes:
1. `ChickenStatsSO`, under `[Header("Peck (foraging)")]`, after `PeckCooldown`:
   ```csharp
   [Tooltip("Seconds a Peck takes from press to food landing; the chicken is rooted for the whole cast and a stun, a shove, " +
            "an execution or its own next move cuts it (no food). PLACEHOLDER values for Maestro and the Balance Oracle to tune; " +
            "keep it readable, under about 0.9 s. The Peck clip is scaled to this value on every peer.")]
   [Range(0.2f, 1.2f)] public float PeckCastSeconds = 0.4f;
   ```
   The `[Range(...)]` **must sit on the same line as `public float`**: `tools/balance/schema.py` `FIELD_DECL` reads only the
   single-line attributes in front of the declaration (the multi-line tooltip is not seen). Update the class summary line
   14 ("foraging is PeckAmount / PeckCooldown") to say the cycle is `max(PeckCooldown, PeckCastSeconds)` (decision 1).
2. Class assets, text edit, one line after `PeckCooldown:`: Warrior `PeckCastSeconds: 0.5`, Speedy `0.4`, Fatty `0.7`
   (decision 7). Assassin has no Peck lines; it keeps the C# default and never forages.
3. `DebugHud.BuildBalanceReport` (~:417-433): add a `Cast` column after `Rate` (header string and
   `.Append(s.PeckCastSeconds.ToString("0.00").PadLeft(6))`).
4. `BalanceEditorWindow` (~:146): `s.PeckCastSeconds = FloatField(s.PeckCastSeconds, wF, minVal: 0.2f);` after
   `PeckCooldown`, plus its column header next to the Peck cooldown header (find it above line 135). Clamp the top too if
   `FloatField` takes a max; otherwise the `[Range]` clamps on serialization.
5. Kit Lab lists (keep the three in sync, `kitlab_map.py` says so):
   - `export_kitlab_data.py:22` `CLASS_STATS` add `"PeckCastSeconds"` after `"PeckCooldown"`.
   - `kitlab_map.py:7` `BASEKEY` add `"peckCast": "PeckCastSeconds"`; mention it in the `kitlab_to_proposal.py:12` docstring.
   - `kitlab.template.html`: `STD` (~:339) add
     `{k:'peckCast',label:'Peck cast',unit:' s',std:0.5,better:'down',dec:2,why:'Placeholder per class; Fatty longest'}`;
     `BASEKEY` (:353) add `peckCast:'PeckCastSeconds'`; the value map (:370) add
     `peckCast:hasPeck?(num(b.PeckCastSeconds)?b.PeckCastSeconds:0.4):null` (0.4 mirrors the C# default, like `peckCd:…:1`).
   - The Kit Lab artifact is refreshed later with `/balance kitlab` (capabilities `db` + `user` unchanged); not in this batch.
6. `schema.py`: no change (generic). `DENIED`: no change — the cast is tunable by delta.
7. Ability text: **no**. `ability.peck.desc` (UiText.csv:69) carries no number and the cast is per class, not per ability;
   adding one would need a per-class placeholder contract. CODE-TEXT-06/10 stay satisfied.

Tests (batch 1):
- `test_kitlab.py`: a test that `kitlab_to_proposal.convert({"classstats":{"Fatty":{"values":{"peckCast":0.6}}}})` gives
  `{"PeckCastSeconds": 0.6}` for `Fatty`, and a reverse round trip through `delta_to_kitlab`; a test that
  `schema.class_fields("ChickenStatsSO")["PeckCastSeconds"]` is `float` with `lo == 0.2`, `hi == 1.2` (pins the same-line rule).
- `DataIntegrityTests.ClassStats_EveryClass_HasAuthoredPeckValues` (:338-373): add, per forager, `PeckCastSeconds` inside the
  field's own `RangeAttribute` (read by reflection, not literals), and **`Peck_ShippedCast_FattyIsTheLongest`**: Fatty's
  `PeckCastSeconds` is strictly greater than every other forager's (relationship, no literal).
- `EconomyAndPilesTests.PureCollectSeconds` (:476): per press cost becomes `Mathf.Max(s.PeckCooldown, s.PeckCastSeconds)`
  (decision 1). Shipped numbers do not move (cast < cooldown for every class).

Rules: ARCH-DATA-11, ARCH-DATA-14, CODE-STYLE-01, CODE-TEXT-06/10, CODE-DESIGN-01 (the cast is an oracle input).
Gate: `python -I -m unittest discover -s tools/balance/tests -t tools/balance/tests`; EditMode classes
`DataIntegrityTests`, `EconomyAndPilesTests`, `BalanceBookTests`, `Phase8ChunkD6Tests`.

---

## Batch 2 — The cast on the state authority

Owned files: `Abilities/AbilityBaseSO.cs`, `Abilities/AbilityContext.cs`, `Abilities/PeckAbilitySO.cs`,
`Gameplay/AbilityRunRules.cs`, `Gameplay/AbilityController.cs`, new `Gameplay/PeckCastRules.cs` (+ `.meta` generated by
Unity), tests listed below. (`Scripts/` prefix omitted.)

### 2.1 Cast length = `Stats.PeckCastSeconds`, read through BalanceBook
- `AbilityBaseSO.ResolveActiveDuration` (:382) becomes `public virtual float ResolveActiveDuration(...)`, body unchanged.
  Every reader already goes through it: the run timer (`AbilityController.TryActivate` :1227), the ring drain
  `ActiveRemaining01For` (:154), `MostRecentActiveSlot` (:177), HUD and badge readers. Nothing else changes in the pipeline.
- New pure class `Gameplay/PeckCastRules.cs` (Fusion-free, `public static`):
  - `float CastSeconds(ChickenStatsSO stats, float fallbackSeconds)` → `stats != null && stats.PeckCastSeconds > 0f ?
    stats.PeckCastSeconds : fallbackSeconds` (mirrors `ResolveCooldown`).
  - `bool IsCut(bool stunned, bool knockedSinceLastTick)` → `stunned || knockedSinceLastTick`.
  - `float LandingTake(float amount, float freeSpace, float available)` → `Mathf.Max(0f, Mathf.Min(amount, Mathf.Min(freeSpace, available)))`.
- `PeckAbilitySO`: `public override float ResolveActiveDuration(ChickenController caster) =>
  PeckCastRules.CastSeconds(caster != null ? caster.Stats : null, Duration);`.
  `Duration` (asset 0.4) is now only the fallback for a caster with no stats (a wiring fault already reported elsewhere);
  it no longer adds a post-peck lock. Leave `Peck.asset` `Duration: 0.4` untouched (the Oracle still reads it until batch 5).
- BalanceBook writes deltas into the shipped `ChickenStatsSO` in place (`ApplyAsset`, `JsonUtility.FromJsonOverwrite`), so
  `caster.Stats.PeckCastSeconds` is the delta value with no extra code. **Every peer** reads its own copy: peers with the
  same `bal=` hash read the same number; peers with different hashes already get the D3c warning and play different numbers
  for every class stat (ARCH-DATA-14, ARCH-NET-05). The authority's `TickTimer` is the truth for gameplay; the cast length is
  **not** replicated as a new value (see Risks R3).

### 2.2 Natural end vs cut, smallest change
- `AbilityContext`: add `public bool EndedNaturally;` with a summary: "true only inside `OnDeactivate` when the slot's own
  timer ran out; false for every teardown (cut, cast, death, reset, swap, authority gain)".
- `AbilityRunRules.ReleaseSlot(..., Action<int> endTraversal, bool natural = false)`: set `ctx.EndedNaturally = natural`
  next to `ctx.Slot = slot` before `OnDeactivate`, reset to `false` in the `finally` next to `ctx.Slot = -1`. Existing test
  callers compile unchanged.
- `AbilityController.Deactivate` (:1336): pass `natural` through to `ReleaseSlot`. `Deactivate(i, natural: true)` stays the
  single natural call site (the expiry loop :349-354).

### 2.3 Food at the natural end (ARCH-NET-07)
`PeckAbilitySO`:
- `OnActivate`: keep `SetMovementLock(ctx.Slot)` (the lock is now the whole cast); **delete** the pile lookup, `take` and
  `cargo.RequestPeck` (:111-127). Nothing else.
- `OnDeactivate`:
  ```csharp
  var chicken = ctx.Controller;
  if (chicken == null) return;
  chicken.Effects.RemoveMovementLock(ctx.Slot);
  if (!ctx.EndedNaturally) return;                       // cut or torn down: no food, by design (D2a)
  var gm = GameManager.Instance;
  if (gm == null || !gm.IsMatchRunning) return;          // legal: the round ended on this tick (ARCH-ERR-03, no log)
  var cargo = chicken.Cargo;
  var pile = FindPile(chicken);                          // reach and food re-checked now (decision 5)
  if (cargo == null || pile == null) return;             // legal: pile emptied or chicken moved off it; lands empty
  float take = PeckCastRules.LandingTake(AmountFor(chicken), cargo.FreeSpace, pile.Available);
  if (take <= 0f) return;                                // legal: beak filled meanwhile
  cargo.RequestPeck(pile, take);                         // ask; the pile authority serves and reports (unchanged path)
  chicken.Abilities?.NotePeckLanded();
  ```
  No reservation during the cast; first to land wins (decision 5). The pile authority still re-checks reach and amount
  (`FoodPile.RPC_Drain` :480-513) — unchanged.
- Rewrite the class remarks (:25-38) and the `EndsOnNextMove` summary (:67): food is asked at the natural end of the cast;
  the movement lock lasts the cast; a cut (stun, shove, execution, own next move) asks nothing. Delete every "0.4 s" mention.

### 2.4 The cut check (authority only)
`AbilityController`:
- New field `private byte _peckCutKnockbackSeen;` seeded in `Spawned` next to `_lastKnockbackId` (:316) and in
  `OnAuthorityGained` (:1398) from `_controller.KnockbackEventId`.
- New method, called in `FixedUpdateNetwork` **right after the natural-expiry loop** (:349-354) and before the
  `_rearmRemaining` loop, so it runs whether or not there is input and before the match / removal returns:
  ```csharp
  private void CutPeckIfInterrupted()
  {
      if (_controller == null) return;
      byte knock = _controller.KnockbackEventId;
      bool knocked = knock != _peckCutKnockbackSeen;
      _peckCutKnockbackSeen = knock;                    // updated every tick: a shove before the press never cuts later
      int peck = FindPeckSlot();
      if (peck == InvalidSlot || !IsSlotActive(peck)) return;
      bool stunned = _controller.IsStunned;
      if (!PeckCastRules.IsCut(stunned, knocked)) return;
      Deactivate(peck);                                 // not natural: no food, no expire cue
      _log?.Debug(Source, $"Peck cut by {(stunned ? "a stun" : "a shove")}.");
  }
  ```
  - Reads **only** `IsStunned` and `KnockbackEventId`. It must not read `SelfImpulseEventId` (own dodge), `SlowMultiplier`,
    `CurrentControlState` (it folds in Slowed and Rooted), or any cargo / steal id (D2a). Rooted does not cut (not in D2a).
  - Stun immunity and knockback immunity already stop the networked state from changing (:1296, :1625, :1277-1280).
  - Tie (decision 6): expiry runs first, so a stun landing on the completing tick lets the food land. No reorder.
  - Own next move: already `EndStealthAndPeckForCast` → `Deactivate(i)` (not natural). Execution: `ExecuteRemoval` →
    `EndAllForRemoval` → `DeactivateAll` (not natural). Death / reset / match stop / swap / authority gain: not natural.
    No code change for those.
- Auto-Peck and bots: no change (decisions 3, 4). The chain already stops on Stunned/Knocked/Moved; `peckReady`
  (:579) waits for the cast to end.

### 2.5 `PeckLandedEventId` (ARCH-EVT-02)
- `AbilityController`, next to `LastCastEventId` (:98):
  ```csharp
  /// <summary>Bumped by the state authority when a Peck cast completes and food is actually asked of a pile (not on a cut,
  /// not on an empty landing). One-shot, wraps at 255; peers observe it through ChickenLocalEvents.</summary>
  [Networked] public byte PeckLandedEventId { get; private set; }
  internal void NotePeckLanded() { if (HasStateAuthority) PeckLandedEventId++; }
  ```
  Zero-initialised by Fusion; nothing to set in `onBeforeSpawned` (a byte event id is valid at 0; the observer seeds its
  baseline on first sight, so a late joiner never replays). `internal` is legal: `Scripts/Abilities` compiles into
  `CluckWars.Gameplay` (ARCH-LAY-07). **Protocol change**: name it in the commit body and STATE.md, live check owed (ARCH-NET-14).

### 2.6 Tests (batch 2)
New `Editor/Tests/PeckCastTests.cs` (pure unless noted):
- `IsCut`: stun → true; knocked → true; neither → false. Slow and steal are not inputs, so they cannot cut; pinned by the
  source guard below.
- `LandingTake`: equals the minimum; 0 when the pile or the room ran out; never negative.
- `CastSeconds`: for each forager stats asset equals `asset.PeckCastSeconds`; null stats → the Peck asset's `Duration`.
- **Delta**: build `new BalanceBook(log, registry, classes, match)` as `BalanceBookTests` (:78) does; verdict
  `{"schema":1,"assets":{"<Fatty asset name>":{"PeckCastSeconds": v}}}` with `v = asset value + one step inside the range`
  (computed, not a literal); `book.Preview(fattyStats, verdict)` → `PeckCastRules.CastSeconds(copy, ...) == v`; destroy the copy.
- `ReleaseSlot_PassesNatural_OnlyWhenAsked`: a test-only `AbilityBaseSO` subclass (CreateInstance, destroyed in TearDown,
  CODE-TEST-14) records `ctx.EndedNaturally` in `OnDeactivate`; `ReleaseSlot(..., natural: true)` → true,
  default → false; after the call `ctx.EndedNaturally == false`.
- Source guards (whole class bodies, CODE-TEST-12):
  - `PeckAbilitySO.OnActivate` contains no `RequestPeck(`; `OnDeactivate` contains `if (!ctx.EndedNaturally) return;`
    before `cargo.RequestPeck(pile, take)` and `NotePeckLanded()` after it; `SetMovementLock` still in `OnActivate`.
  - `AbilityController`: exactly one `Deactivate(i, natural: true)`; `CutPeckIfInterrupted();` appears after it and before
    `GetInput<`; the method body mentions `IsStunned` and `KnockbackEventId` and none of `SelfImpulseEventId`,
    `SlowMultiplier`, `CurrentControlState`, `StealEventId`.
  - Old post-peck lock: `PeckAbilitySO.cs` contains no `0.4`; its only read of `Duration` is inside
    `ResolveActiveDuration`.
Existing tests to change:
- `FoodTransferRulesTests.Peck_AsksThePile_AndIsCreditedOnlyByItsReport` (:456-476): read `RequestPeck` in the
  `OnDeactivate` body instead of the file; replace the "lock before RequestPeck" ordering assert with: `SetMovementLock` in
  `OnActivate`, `RequestPeck` only in `OnDeactivate` after the `EndedNaturally` return. Keep every cargo/pile assert.
- `Phase6Chunk3Tests` (:235-282): unchanged expectations (Peck still `EndsOnNextMove`); run it.
- `PeckChainAndJoystickTests`: no change expected; run it.
"Each cutter leaves pile and beak unchanged" is proven structurally here (no request on a non-natural release, every
cutter path is non-natural) and **live** in batch 4 — EditMode cannot drive a Fusion runner (CODE-TEST-19).

Rules: ARCH-NET-01, ARCH-NET-07, ARCH-NET-14, ARCH-EVT-02, ARCH-EVT-05, ARCH-ERR-03, ARCH-LAY-07, ARCH-DATA-11, CODE-TEST-12/18.
Gate: `PeckCastTests`, `FoodTransferRulesTests`, `Phase6Chunk3Tests`, `PeckChainAndJoystickTests`,
`WarriorSpecializationTests` (pins `float activeSeconds = ability.ResolveActiveDuration(_controller);`), `FeatherAuraZoneTests`,
`GameplayPresentationBoundaryTests`, `BalanceBookTests`, `EconomyAndPilesTests`.

---

## Batch 3 — Presentation: scaled clip, hard cut, landed beat

Owned files: `Gameplay/ChickenLocalEvents.cs`, `Gameplay/ChickenController.cs` (override block only, :823-866),
`Visuals/ChickenAnimator.cs`, `Visuals/FoodPileVisuals.cs`, `Visuals/PeckBeatRules.cs`, `Visuals/FeedbackTuning.cs`,
`Editor/ChickenAnimationSetup.cs`, `Assets/_Game/Art/Animations/Chicken.controller` (edited by the Editor method, below), tests.

### 3.1 Bind the per-class Peck clip (pre-existing bug, required here)
`ChickenController.GetOrBuildOverride` overrides five placeholders and the eight `Cast_*` states, **not `Peck`**: the Peck
state plays the empty 1 s placeholder `Placeholders/Peck.anim` (bind pose), so the generated per-class clips in the registry
(`Clips.Peck`) are never used. Add `public const string ClipKeyPeck = "Peck";` beside the other keys and
`[ClipKeyPeck] = entry.Clips.Peck,` in the initializer. Verify live with a capture (CODE-UI-02 style evidence).

### 3.2 Animator: `PeckSpeed` float + `Pecking` bool, hard cut
Edit `Chicken.controller` **through the AnimatorController API, not by hand**: add an idempotent
`public static void EnsurePeckCastWiring(AnimatorController c)` to `ChickenAnimationSetup` (called from its existing
wire-up pipeline and from a menu item `Cluck Wars/Animation/Wire Peck Cast`), run once via MCP `script-execute`, then save.
Reason: the file is fileID-linked YAML; the API keeps it valid and the method re-applies after any regeneration. It:
1. adds parameter `PeckSpeed` (Float, default 1) and `Pecking` (Bool, default false) if missing;
2. on state `Peck`: `speedParameterActive = true`, `speedParameter = "PeckSpeed"`, `speed = 1`;
3. the `Peck → Idle` transition (today fileID `-8185713725255539571`: exit time 1, 0.08 s blend, no condition) becomes:
   condition `Pecking` IfNot, `hasExitTime = false`, `duration = 0`, `hasFixedDuration = true`, `offset = 0`;
4. leaves the Any State `Peck` trigger transition (:848-872) as is (it can transition to self, so a back-to-back peck restarts).
Clips are **not** edited (`*_Peck.anim` stay 0.4 s at 24 fps; length is read at runtime).

`ChickenAnimator`:
- Hashes `PeckSpeedHash = StringToHash("PeckSpeed")`, `PeckingHash = StringToHash("Pecking")`.
- Cache `_peckClipSeconds` keyed on the `RuntimeAnimatorController` reference: when it changes, read
  `(_animator.runtimeAnimatorController as AnimatorOverrideController)?[ChickenController.ClipKeyPeck]?.length ?? 0f`.
- In `PushAnimatorParameters` (:585-594, already per frame because `Rebind` resets parameters):
  ```csharp
  var abilities = _controller.Abilities;
  int peck = abilities != null ? abilities.FindPeckSlot() : AbilityController.InvalidSlot;
  bool pecking = peck != AbilityController.InvalidSlot && abilities.IsSlotActive(peck);
  float cast = peck != AbilityController.InvalidSlot ? abilities.GetSlot(peck).ResolveActiveDuration(_controller) : 0f;
  _animator.SetFloat(PeckSpeedHash, PeckBeatRules.ClipSpeed(_peckClipSeconds, cast));
  _animator.SetBool(PeckingHash, pecking);
  ```
  Entry stays the cast event trigger (`OnCast` → `TriggerPeck`, every peer, ARCH-EVT-01). Exit: the slot bit clears (natural
  end at the last clip frame, or a cut) → `Pecking` false → instant Idle. A stun additionally enters Stunned via its own bool.
- Replace the `TriggerPeck` remark (:764-769, "clip is 0.4s, matching Duration") with: speed = clip length / effective cast,
  so one play spans the cast on every peer; the exit is the replicated slot bit.
- `PeckBeatRules.ClipSpeed(float clipSeconds, float castSeconds)` → `clipSeconds > 0f && castSeconds > 0f ?
  clipSeconds / castSeconds : 1f`. No speed constant anywhere.
- Reduced Motion: speed is timing, not amplitude; no change (the cut is blend 0 for everyone).

### 3.3 Landed beat (re-key `FoodPileVisuals`)
- `ChickenLocalEvents` (ARCH-EVT-03, the one observer): add `public byte PeckLandedEventId;` to `ChickenEventSnapshot`,
  fill it from `_abilities.PeckLandedEventId` in `Observe()`, add `EventEdge _peckLanded` (reset in `Reset()`), an
  `event Action PeckLanded`, and `public float LastPeckLandedTime { get; private set; } = float.NegativeInfinity;` stamped
  with `Time.unscaledTime` just before raising (reset to −∞ in `Reset()`).
- `FeedbackTuning`: `public const float PeckLandedWindowSeconds = 0.25f;` (ux spec 2, ARCH-LAY-15).
  `PeckBeatRules.LandedRecently(float now, float landedAt) => now >= landedAt && now - landedAt <= FeedbackTuning.PeckLandedWindowSeconds;`
- `FoodPileVisuals.PeckWindowOpen(AbilityController abilities)` (:353) becomes
  `PeckWindowOpen(ChickenController c)`: `PeckBeatRules.LandedRecently(Time.unscaledTime, c.LocalEvents.LastPeckLandedTime)`
  **or** the Peck slot still reads active on this peer (keeps the pile-authority peer correct when its RPC-driven `Amount`
  drop arrives before the pecker's snapshot). `FindPecker` passes `c`. `ObservePeck`, `PickPecker`, `PlayPeckBeat` unchanged.
- Ring: no code. `ControlStateVFX.UpdateBuffRing` (:413-429) already drains for the most recent running slot on all peers
  through `ActiveRemaining01For`, which now reads the cast; it disappears the tick the slot ends (the "ring snap" on a cut).

### 3.4 Not built in BL-206 (file as backlog lines, `ux-designer` review)
- HUD hex cast-progress sweep in a distinct tint: not cheap (new hex layer, USS class, rules and screenshot pass on the
  touch and PC HUD). The hex already shows the cooldown from the press.
- Cut puff and own-hex flash: needs a second networked byte (`PeckCutEventId`) or a bit-clear heuristic on proxies. The MVS
  (instant animation cut + ring vanishing) is visible without it.

### 3.5 Tests (batch 3)
New `Editor/Tests/PeckCastAnimationTests.cs`:
- `PerClass_ScaledPeckClip_SpansTheEffectiveCast`: for each forager in `ChickenClassRegistry`, `clip = entry.Clips.Peck`,
  `cast = PeckCastRules.CastSeconds(entry.Stats, peck.Duration)`; `clip.length / PeckBeatRules.ClipSpeed(clip.length, cast)`
  equals `cast` within `1f / 60f`. Same again with a `BalanceBook.Preview` copy carrying a changed cast (value computed).
- `ClipSpeed` guards: zero clip or zero cast → 1.
- `Controller_PeckState_IsDrivenByPeckSpeed_AndCutsHard`: load `Chicken.controller` via `AnimatorController`; state `Peck`
  has `speedParameterActive` and `speedParameter == "PeckSpeed"`; its transition to `Idle` has no exit time, `duration == 0`,
  one condition `Pecking` IfNot.
- `Override_BindsTheClassPeckClip`: source guard that `GetOrBuildOverride` contains `[ClipKeyPeck] = entry.Clips.Peck`.
- `LandedRecently`: inside / at / beyond the window, and before the stamp.
- Source guard: `ChickenAnimator.cs` contains no `0.4`; `PushAnimatorParameters` sets `PeckSpeedHash` from `ClipSpeed(`.
Existing tests to change:
- `DataIntegrityTests.ChickenController_Parameters_MatchTheNamesAndTypesHashedInCode` (~:1035): add `PeckSpeed` Float and
  `Pecking` Bool. The override-key test (~:1000) and placeholder test stay valid (`Peck` placeholder stays empty).
- `PeckBeatTests.TheBeat_FitsInsideTheShippedPeckWindow` (:51-52): drop the two `peck.Duration` asserts (the beat now plays
  after the landing, not inside a lock); assert `FeedbackTuning.PeckLandedWindowSeconds > 0`; keep the particle budget asserts.
- `PeckBeatTests.TheWindowIsTheShippedPeckSlot` (:113): expect the new `PeckWindowOpen(ChickenController c)` body
  (`LandedRecently(` and the slot fallback) instead of the old string.
- `CastPresentationTests.ARivalsPeck_PlaysThePeckBeat_NotACast` (:90): unchanged expectation; run it.
- `ReducedMotionCoverageTests`: no new motion; run it.

Rules: ARCH-EVT-01/02/03/05/06, ARCH-LAY-15, CODE-ASSET-08 (no clip edits, neck sign untouched), CODE-UI-08, CODE-TEST-18.
Gate: `PeckCastAnimationTests`, `PeckBeatTests`, `CastPresentationTests`, `DataIntegrityTests`, `ReducedMotionCoverageTests`,
`GameplayPresentationBoundaryTests`, `CastArchetypeWiringTests`; then the full EditMode suite (CODE-TEST-01).

---

## Batch 4 — Live check and docs (no gameplay code)

- Live (CODE-TEST-06/19, ARCH-NET-14): four PC clients, `-cw_perk`, Debug log level. Verify by state samples, not logs:
  food arrives at cast end; a stun, a shove (Wing Slam / Roll Push), the pecker's own other cast, and an execution mid-cast
  leave pile `Amount` and pecker cargo unchanged; a slow and a Snatch on the pecker do not cut; a self dodge does not cut;
  the clip length matches the cast on Warrior / Speedy / Fatty on a rival's screen; the mound beat plays once per landing on
  every peer; a late joiner replays no beat. Capture frames for the cut (CODE-BUILD-06).
- `docs/STATE.md` (protocol change named), `docs/FEEDBACK.md` §1.5/§1.6/§7 inventory, the GDD Peck lines,
  `docs/site/index.html` cast values from the assets and republish (CODE-PROC-13), backlog lines for 3.4.
- `librarian`: `FILE_STRUCTURE.md` (new `PeckCastRules.cs`, test files), ARCH-NET-07 / ARCH-EVT-02 enforcement pointers
  (the moved Peck guard, the new event id).

---

## Batch 5 — Oracle reads the real stat (GATED: after the oracle session's commit)

Owned: `Balance/OracleKit.cs`, `OracleKits.cs`, `OracleSolver.cs`, `OracleSweep.cs`, `FarmSim.cs`, `OracleRunner.cs` if it
passes `PeckCastSeconds`, `Data/.../OracleReference.asset`, tests `OracleKitsTests`, `FarmSimTests`, `OracleSolverTests`,
`OracleReferenceTests`. Re-grep all sites first: the parallel session is editing these files now.
1. `OracleKits` (:121, :160-162): `PeckCastSeconds = scope.Of(stats).PeckCastSeconds` — the same `BalanceBook.Preview` scope
   the other class stats use; delete `PeckCastOf` and `PeckLockSeconds = scope.Of(peck).Duration`.
2. `OracleKit`: delete `PeckLockSeconds` (:25-28); its cast doc says "the chicken is rooted for the cast; food lands at its end".
3. `FarmSim` (:20-21, :61, :228-230, :428-459): drop every `PeckLockSeconds` term; the chicken may walk when the food lands
   (`MoveFreeAt = PeckDoneAt`); cycle stays `max(PeckCooldown, PeckCastSeconds)` (:457, FarmSim:435 per decision 1).
4. `OracleSolver`: remove `OracleAsset = "Oracle"`, `PeckCastField`, `FreeValue.IsOracleOnly` path (:362-372),
   `OracleProposal.OracleOnly` and the copy at :420-427; a free value `{Asset:"Fatty", Field:"PeckCastSeconds"}` is a normal
   delta field probed through `inputs.Book.TryProbe` and limited by the `[Range(0.2,1.2)]`. Remove
   `OracleInputs.PeckCastSeconds` (OracleKits.cs:28-30) and its pass-through in `OracleSweep` (:102).
5. Re-record `OracleReference.asset` with **Record** semantics (`Record baseline (keep prices)`), never Accept.
6. Tests: `OracleKitsTests` (:144-145) expects the cast from the stats asset; `FarmSimTests` (:69, :154-156, :444) drop the
   lock; `OracleSolverTests` (4) address `"Fatty.PeckCastSeconds"` as an asset field and assert the delta JSON carries it;
   new source guard: no file under `Scripts/` contains `PeckLockSeconds` or `OracleSolver.OracleAsset`.
Rules: CODE-DESIGN-01, ARCH-DATA-14, ARCH-LAY-04 (balance code stays Fusion-free).
Gate: `OracleKitsTests`, `FarmSimTests`, `OracleSolverTests`, `OracleReferenceTests`, `OracleRunnerTests`, `BalanceCheckTests`,
`BalanceBookTests`; full suite.

---

## Risks

- **R1 Pre-existing animation bug (3.1).** The Peck clip was never bound; today every Peck plays a 1 s bind-pose
  placeholder. Fixing it changes what players see even before the cast scaling. Verify with captures on all three foragers.
- **R2 Mound beat attribution.** The pile `Amount` drop and the pecker's landed id replicate on different objects; the 0.25 s
  window plus the slot-active fallback covers both orders, but only the live 4-client run proves it.
- **R3 Cast length per peer.** Remote peers scale the clip from their own BalanceBook copy. Same `bal=` → identical; a
  mismatched `bal=` (already a D3c warning) gives a wrong clip speed while gameplay stays authority-correct. Replicating the
  value would be a second protocol addition; not done unless Maestro wants it.
- **R4 Bots** stay blocked from other casts for the whole cast (`BotMayPress`), 0.5 / 0.4 / 0.7 s instead of 0.4 s.
- **R5 Snatch knockback.** Snatch's shipped `KnockbackStrength` is 0, so a robbery does not cut. A delta raising it would make
  a robbery cut (it is a shove then). Spine Coat's steal-back knockback cuts the thief's own Peck: a shove, by D2a.
- **R6 Oracle between batches 2 and 5** counts the cast (its own input) plus a 0.4 s lock that the game no longer has; its
  numbers are pessimistic for foragers until batch 5.

Needs Maestro: nothing blocking. No shipped number changes besides the three placeholder casts he already ruled; the only
networked change is one added property. Optional question: R3 (replicate the cast value?).
