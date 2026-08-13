# Peck foraging, four-slot loadouts, and the balance correction

**Date:** 2026-08-13
**Status:** Design approved, not implemented
**Supersedes:** GDD §5.2 move-speed row (drift correction), GDD §7.1–7.2 slot counts,
ADR 0003 Decision 3's "1 Common + 2 Character" loadout shape

---

## 1. Why this exists

Maestro's report from playtest: *"piles are too big and chickens move too fast. It is
funnier to move fast, but makes fighting almost impossible."* Plus: too few ability
options, so loadouts don't express anything.

Investigation turned the speed complaint into something more specific than a tuning
disagreement — see §3. The rest of the work is a deliberate redesign: food collection
becomes a pressed ability rather than an automatic drain, and every class gets four
ability buttons instead of two (three for Assassin).

---

## 2. Decisions locked

| # | Decision |
|---|---|
| D1 | Move speed reverts to the Oracle-solved spec values: Fatty 7.5, Warrior 7.5, Speedy 9.0, Assassin 9.0 |
| D2 | Pile footprints shrink ~35% linear |
| D3 | Food collection becomes **Peck**, a pressed ability: **3 food per press**, per-class cooldown |
| D4 | Every class gets **4 ability buttons**. Peck is force-equipped on all classes except Assassin |
| D5 | Assassin cannot Peck; it gets a 4th freely-chosen ability instead |
| D6 | Peck's button position is **player-assignable** in the loadout screen |
| D7 | The existing Common ability "Peck" is renamed **Snatch**; "Flying Peck" becomes **Dive Bomb** |
| D8 | The **Combo** passive is retired; GDD §5.3's *Spoiler* and *Thief* are implemented instead |
| D9 | Kill-bounty `FoodPickup` **floor drops** are removed, replaced by an **execute bounty paid straight into the Assassin's bounty bag** on top of the stolen cargo. Nothing ever lands on the ground, so the pickup subsystem still becomes dead code and is deleted |
| D10 | Assassin is **exempt from the SCT axiom**; a parallel **Predation axiom** governs it |
| D11 | Ability pool expansion sequences **widen-AllowedClasses first**, then new Commons, then bespoke class abilities |

---

## 3. Part A — the move-speed correction

### 3.1 The finding

The shipped `ChickenStatsSO` assets have drifted from the Oracle-solved values recorded
in GDD §5.2 and in `BalanceOracleTests`:

| Class | GDD §5.2 + Oracle test | Shipped `.asset` | Drift |
|---|---|---|---|
| Fatty | 7.5 | 9.0 | +20% |
| Warrior | 7.5 | 9.0 | +20% |
| Speedy | 9.0 | 10.5 | +17% |
| Assassin | 9.0 | 10.5 | +17% |

Running the real Oracle policy on the **shipped** values against `SctTargets`
(tolerance ±1.5 s):

| Class | Shipped SCT | Target | Verdict |
|---|---|---|---|
| Speedy | 29.1 s / 4 trips | 30 s / 4 | pass (−0.9) |
| Fatty | 28.2 s / 2 trips | 30 s / 2 | **FAIL (−1.8)** |
| Warrior | 33.1 s / 3 trips | 35 s / 3 | **FAIL (−1.9)** |
| Assassin | 39.3 s / 4 trips | 40 s / 4 | pass (−0.7) |

**Two of four classes currently violate the SCT axiom.**

### 3.2 Why the suite didn't catch it

`Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs:41-44` **hardcodes** the design
numbers into `OracleChicken` literals instead of loading `ChickenStatsSO` from disk. The
test therefore asserts "the spec satisfies the spec" and is structurally incapable of
observing the shipped assets. This is the same failure shape as the art-metrics incident:
a green check that validates a model rather than the artifact.

### 3.3 The fix

1. Set `MoveSpeed` in the four class assets to **7.5 / 7.5 / 9.0 / 9.0**.
2. **Rewrite `BalanceOracleTests` to load the real `ChickenStatsSO` assets** via
   `TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir)` — the pattern
   `DataIntegrityTests` and `EconomyAndPilesTests` already use — so this class of drift
   cannot recur. This is the load-bearing half of the fix; changing the numbers without
   it just resets the clock.

### 3.4 Why we are not cutting further

The 45 s match duration (`MatchConfig.asset`) caps how far speed can drop. Holding
collection rates constant, cutting *below* spec:

| Cut below spec | Speedy | Fatty | Warrior | Assassin |
|---|---|---|---|---|
| 0% (spec) | 30.9 s | 30.1 s | 35.6 s | 41.1 s |
| −20% | 34.2 s | 33.0 s | 39.4 s | 44.4 s |
| −30% | 36.6 s | 35.1 s | 42.0 s | **46.8 s — cannot clear** |
| −40% | 39.7 s | 37.8 s | **45.6 s** | **49.9 s** |

Beyond ~20% below spec the Assassin cannot bank the win target inside the match at all.
**Revert to spec first, then playtest.** If it still reads as too fast, the next lever is
raising `MatchDurationSeconds` and re-solving `SctTargets`, not shaving speed alone.

### 3.5 Documentation drift found alongside

`CLAUDE.md` states *"First to 150 food units (or most at 3 minutes)"*. The shipped
`MatchConfig.asset` is **40 food / 45 seconds / deposit 9 per second**. GDD §2 agrees with
the asset. `CLAUDE.md` must be corrected.

---

## 4. Part B — pile shrink

Current pile footprints total **361 m² in a 1444 m² arena — 25% of the floor** — and piles
are solid colliders that carve the NavMesh.

| Pile | Count | Current | New (−35% linear) |
|---|---|---|---|
| Centre (permanent) | 1 | 12 × 10 | **7.8 × 6.5** |
| Contested | 4 | 6.5 × 5.4 | **4.2 × 3.5** |
| Personal | 4 | 5.5 × 4.6 | **3.6 × 3.0** |

New total ≈ **153 m² = 10.6%** of the floor. Edited in `MapGenerator`'s
`_centerPileFootprint` / `_contestedPileFootprint` / `_personalPileFootprint`.

Food amounts, the permanent floor fraction (0.6) and regen (0.5/s) are **unchanged** —
this is a geometry change only.

**No SCT impact.** `BalanceOracle` travels centre-to-centre and models no footprint at
all, so pile size is invisible to it. That is a real limitation of the model worth
recording: a chicken actually walks to the pile *surface*, so the Oracle slightly
underestimates travel. It underestimates it consistently across classes, so relative
balance holds, but do not treat Oracle SCT as an absolute wall-clock prediction.

Collection reach is unaffected: `FoodPile.IsWithinCollectRange` measures to the **surface**
(`_collectReach = 1.0`), so shrinking piles does not change how close you must stand.

---

## 5. Part C — Peck

### 5.1 Shape

Peck is a real `AbilityBaseSO` (`PeckAbilitySO`, the freed name) occupying a real ability
slot. It is **not** a parallel interaction system.

Rationale: a slot-resident ability inherits the per-slot cooldown timer, the HUD hex with
its radial fill and cooldown number, the `AbilityRefusalRules` precedence table, the
denied-press bump, the telegraph, and bot integration via `TryGetReadySlotForRole`. A
separate system would duplicate every one of those, and the duplicates would drift.

| Property | Value |
|---|---|
| Food per press | **3** (uniform for now, see §5.3) |
| Cooldown | Speedy **0.75 s**, Fatty **0.80 s**, Warrior **0.90 s** |
| Duration | ~0.2 s (short, so it barely blocks other casts under the one-ability-at-a-time rule) |
| `SlotKind` | Common |
| `AllowedClasses` | Warrior \| Speedy \| Fatty (**not** Assassin) |
| Range | Reuses `FoodPile.IsWithinCollectRange` — surface-relative, `_collectReach = 1.0` |
| Refusal when no pile in range | `AbilityRefusal.NoTarget` |

### 5.2 SCT verification

Simulated with the quantised collection model (a partial peck still costs a full
cooldown), at spec speeds, win target 40:

| Class | Move | Cap | Cooldown | SCT | Target | Verdict |
|---|---|---|---|---|---|---|
| Speedy | 9.0 | 10 | 0.75 s | 29.6 s / 4 trips | 30 s / 4 | pass (−0.4) |
| Fatty | 7.5 | 35 | 0.80 s | 30.4 s / 2 trips | 30 s / 2 | pass (+0.4) |
| Warrior | 7.5 | 14 | 0.90 s | 34.7 s / 3 trips | 35 s / 3 | pass (−0.3) |

Pile interaction: personal (5 food) = 2 pecks, contested (10) = 4, centre (8 collectable
above its floor) = 3.

**Time pinned at a pile filling from empty — the window that creates fights:**
Speedy 3.0 s, Warrior 4.5 s, **Fatty 9.6 s** (21% of the whole match). This is the
mechanism that answers the original complaint: the fights happen because farming now
holds you still and visible.

### 5.3 Per-class amount must be built in from day one

At a uniform amount, **Speedy and Fatty are mechanically indistinguishable** — their
solved cooldowns land within 0.05 s of each other at every amount tested, because their
`CollectionRate`s (3.0 and 3.2) barely differ. Only Warrior separates.

So `PeckAmount` must be authored as a **per-class value that currently happens to be 3
everywhere**, not as a shared constant. Concretely: a `PeckAmount` field on
`ChickenStatsSO` alongside a `PeckCooldown` field, both read by `PeckAbilitySO` from
`ctx.Controller.Stats`. Do not put the amount on the ability asset as a single number —
that is the shape that would have to be torn out later.

**Avoid amount 5.** Piles hold 5/10/20 food, so an amount of 5 divides exactly and all
three classes collapse onto the same ~1.55 s cooldown, erasing class differentiation
entirely.

### 5.4 What replaces automatic collection

`ChickenCargo.TryCollectFromNearbyPile` is deleted. `ChickenCargo` keeps `Cargo`,
`BountyBag`, deposit, and the pile-slow flag `IsPileSlow` (which is read by
`ChickenController` for the GDD §6.2 movement penalty and is independent of collection).

`ChickenStatsSO.CollectionRate` becomes dead and is removed — replaced by
`PeckAmount` / `PeckCooldown`.

---

## 6. Part D — four-slot architecture

### 6.1 Slots

Every class has **4 slots**. Non-Assassin classes have Peck force-equipped into one of
them, leaving 3 free. Assassin has 4 free and no Peck.

`AbilityController.EquippedSlotCount` returns 4 unconditionally. The Combo-passive branch
is deleted (see §6.4).

### 6.2 Peck's button position is player-assignable

The loadout screen lets the player place Peck on any of the four buttons. Consequences:

- The HUD cannot assume a fixed index for Peck. Anything that wants "the Peck button"
  must query the loadout, not hardcode slot 0.
- `MatchBootstrapper.ResolveLegalLoadout` must **guarantee Peck is present exactly once**
  for non-Assassin classes and **absent** for Assassin, whatever the picker sends. It is
  already the single sanitising chokepoint both bots and players pass through, so this
  rule belongs there and nowhere else.
- Bots need a default placement. Use slot 0 for bots; the position only matters to a
  human's thumb.

### 6.3 Code changes

| File | Change |
|---|---|
| `Networking/PlayerNetworkInput.cs` | Append `Ability4 = 7`, `AbilityHold4 = 8`. **Append only** — the existing comment forbids renumbering, and `AbilityCancel = 6` must keep its bit. |
| `Gameplay/AbilityController.cs` | Add `_slot3`, `Cooldown3`; grow `_holdBits` / `_pressBits` / `_canBeginCharge` from `[3]` to `[4]`; extend `GetSlot` / `GetCooldown` / `SetCooldown` / `TriggerCooldown`; `SetSlots` gains a 4th parameter; `EquippedSlotCount` returns 4. |
| `Gameplay/AbilityActivationRules.cs` | `AbilityHoldStateMachine.Decide` iterates 4 slots; `ChargingSlot`'s 1-based encoding now spans 1..4. |
| `Input/IInputProvider.cs` | Add `GetAbility4Pressed()`; `GetAbilityHeld(int)` accepts 0..3. |
| `Input/KeyboardInputProvider.cs` | Bind **F** as the 4th key alongside Q / E / R (keeps the cluster under the left hand with WASD; avoids T/R ambiguity). |
| `Input/TouchInputProvider.cs`, `CompositeInputProvider.cs` | Plumb the 4th press. |
| `Input/TouchControlsController.cs` | 4th hex: pointer capture, hold tracking, drag-off cancel, cooldown/refusal styling. |
| `Assets/UI/TouchControls.uxml` | 4th hex with the full 8-element sub-tree (`Cooldown4`, `ActiveDrain4`, `Icon4`, `Label4`, `CooldownNum4`, `StateGlyph4` + ring + 2 bars, `Badge4`). |
| `Assets/UI/Styles/TouchControls.uss` | `cw-hex--a4` arc position; re-balance the MOBA arc for 4 buttons. |
| `Gameplay/MatchBootstrapper.cs` | `ResolveLegalLoadout` handles 4 slots + the Peck presence/absence rule. |
| `UI/MenuUiController.cs` + `CharacterSelect.uxml` | 4-slot picker with Peck placement. |

### 6.4 Combo passive retired

`AbilityController.EquippedSlotCount`'s only job was granting Assassin a 3rd slot via
`ComboPassiveSO`. With 4 slots for everyone that job is gone.

GDD §5.3 already names Assassin's intended passives as **Spoiler** (*"if the timer expires
with no winner, bank +15 food, then normal resolution"* — `MatchConfig.SpoilerBounty` is
already authored at 15) and **Thief**. Combo is pre-0.4 legacy. Retire `ComboPassiveSO`
and implement those two.

⚠️ `ChickenPassive.Combo` is referenced in `ChickenController.IsPassiveActive`,
`ChickenStatsSO.Passive` (Assassin's asset holds `Passive: 4`), and the `EquippedSlotCount`
branch. `ChickenClass` is `: byte` for Fusion serialisation — check whether
`ChickenPassive` shares that constraint before renumbering the enum.

### 6.5 Verify before implementing

**`NetworkButtons` bit capacity.** We currently use bits 0–6 and this design needs 0–8.
`NetworkButtons` lives inside the compiled Fusion DLL and could not be inspected from
source during design. Confirm it holds ≥ 9 bits before writing the input changes — an
overflow here would drop button presses silently.

---

## 7. Part E — Oracle rework and the Predation axiom

### 7.1 The Oracle's collection model breaks

`BalanceOracle.cs:78` is `time += take / chicken.CollectionRate` — continuous collection.
Peck makes collection **discrete and quantised**:

```
time += ceil(take / PeckAmount) * PeckCooldown
```

The difference is not cosmetic: a chicken topping up 1 food from a nearly-empty pile still
pays a full cooldown. `OracleChicken.CollectionRate` must be replaced by `PeckAmount` +
`PeckCooldown`.

Required changes:
- `Balance/BalanceTypes.cs` — `OracleChicken` swaps `CollectionRate` for `PeckAmount` and `PeckCooldown`.
- `Balance/BalanceOracle.cs` — quantised collection step.
- `Editor/Tests/BalanceOracleTests.cs` — rewritten, **loading real assets** per §3.3.

**If this is skipped, the balance authority silently stops describing the game** — exactly
the failure that produced the speed drift.

### 7.2 The Predation axiom

The SCT axiom is *"a naked chicken, alone, farming greedily, banks the win target."* A
naked Assassin cannot farm at all, so its SCT is undefined and its 40 s / 4-trip target is
unreachable by construction. Assassin is formally **exempt** from `SctTargets`.

In its place, a two-parameter **Predation axiom**:

> Given rivals carrying **C** food encountered every **T** seconds, a naked Assassin banks
> the win target in **X** seconds.

**D9 gives the axiom a floor** (Maestro's call, 2026-08-13). A successful execute pays the
Assassin **twice**:

1. the victim's entire cargo, transferred to the bounty bag — existing behaviour; and
2. a flat **execute bounty**, also straight into the bounty bag — new.

Both are direct transfers. Nothing touches the ground, so an execute can no longer be
vultured by a bystander who happens to walk past the corpse, and — the point — **an execute
on an empty-handed rival still pays**. That converts the Assassin's income from purely
opportunistic to *guaranteed-per-kill plus opportunistic*, which is what makes the class
playable in the opening seconds when nobody is carrying yet.

The axiom becomes: given rivals carrying **C** food encountered every **T** seconds, a naked
Assassin banks the win target in **X** seconds, where the per-execute yield is
`C + ExecuteBounty` rather than `C`.

**Sizing.** `MatchConfigSO` gains `ExecuteBounty`, proposed at **5** — the same magnitude as
the 5 × 1-food floor drops it replaces, so the change is a *routing* change rather than a
buff. Against a 40-food win target that is 12.5% of a win per kill, and `SuccessCooldown` is
15 s, which caps a 45 s match at ~3 executes (~15 food) before stolen cargo. Treat 5 as a
starting value to be solved once the Predation axiom has real C and T from a playtest.

⚠️ **Open:** the existing `LeaderBountyActive` path spawned 8 extra pickups for executing
the match leader — a comeback valve. It should survive as a **multiplier on the execute
bounty** rather than as floor drops, but the multiplier is unchosen. Flagged, not decided.

`ChickenStatsSO.CollectionRate` for Assassin (1.7) becomes meaningless and is removed with
the rest. GDD §5.2 calls that 1.7 *"the load-bearing detail"* separating Assassin from
Speedy; **that sentence must be rewritten**, because the mechanism it describes no longer
exists.

---

## 8. Part F — dead code removal

Execute transfers all cargo to the bounty bag (`AssassinExecute.cs:112`) *before*
`ExecuteRemoval` fires (`:116`), so `ChickenCargo.HandleDeath`'s `dropped = Cargo` branch
is **always zero**. The 5-pickup kill bounty and 8-pickup leader bounty are therefore the
only remaining spawners of `FoodPickup`. D9 converts both into direct bounty-bag payments
(§7.2), which makes the whole subsystem unreachable:

- `Gameplay/FoodPickup.cs` and `FoodPickup.prefab`
- `PrefabRegistrySO.FoodPickup`
- `ChickenCargo`: `_foodPickupPrefab`, `ResolveFoodPickupPrefab`, `TryCollectFromNearbyPickup`, `FlushPickupDrain`, `FindNearestPickupInRange`, `SpawnSinglePickup`, `HandleDeath`, and the `_activePickupTarget` / `_pendingPickupDrain` / `_pickupDrainTicks` accumulators
- `BotController.FindNearestPickup` and any bot state that targets pickups
- `GameManager.RegisterPickupSpawned` / `RegisterPickupCollected` and their counters
- `DebugHud`'s pickup readout

`ChickenCargo.HandleDeath` also unsubscribes from `_combat.OnDeathAuthority`; check whether
that event retains any other consumer (`AbilityController.HandleOwnerDeath` does subscribe,
and must stay).

**Do this as a separate commit from the gameplay change**, so a revert of the Assassin
economy doesn't have to resurrect deleted files. Sequence it *after* the execute bounty
lands and has been played, not before — deleting the pickup code is the step that makes the
old behaviour expensive to restore.

---

## 9. Part G — renames

| Was | Becomes | Cost |
|---|---|---|
| "Flying Peck" | **Dive Bomb** (`ShortLabel: DVB`) | Asset data only — backed by `RollTrampleAbilitySO`, whose name already doesn't say "Peck" |
| "Peck" (Common steal) | **Snatch** (`ShortLabel: SNC`) | `DisplayName` + class rename `PeckAbilitySO` → `SnatchAbilitySO` |
| — | **Peck** | New `PeckAbilitySO` (the foraging ability) |

⚠️ The `PeckAbilitySO` → `SnatchAbilitySO` rename touches:
`AbilityAimTests.cs:405`, `AbilitySystemTests.cs:267`, `ContractsAndEnumsTests.cs:120,136`,
and — the dangerous one — a **string literal** `"PeckAbilitySO"` in
`UI/AbilityIconStyle.cs:28`. That is a stringly-typed icon lookup: rename the class without
updating the string and the ability silently falls back to a default icon with nothing
logged. Add a test asserting every registry ability resolves a non-default icon key.

Do the class rename **through the Unity Editor** so the `.cs.meta` GUID is preserved and
the `.asset`'s `m_Script` reference survives.

---

## 10. Part H — ability pool expansion

Post-rename pools, with Peck occupying a slot on non-Assassin classes:

| Class | Pool | Free slots | Combinations |
|---|---|---|---|
| Warrior | 6 (Snatch, Egg Shell, Speed Burst, Dive Bomb, Spine Coat, Wing Slam) | 3 | 20 |
| Speedy | 6 | 3 | 20 |
| Fatty | 7 | 3 | 35 |
| Assassin | 9 | 4 | 126 |

Three of every class's six are the same Commons, so Warrior and Speedy effectively have a
fixed build. **Four slots makes the variety problem worse, not better, until the pool
grows.**

Sequenced per D11:

1. **Widen `AllowedClasses` on existing abilities** — pure asset edits, no code, no art.
   Target 8–9 options per class so 4-slot loadouts can be playtested immediately.
   Preserve deliberate gates (Invisibility is Speedy + Assassin by design).
2. **Add new Commons** — best count-per-unit-of-work, but applied second because Commons
   already dominate the thin pools.
3. **Author bespoke class abilities** — target ~10–11 per class, informed by what step 1's
   playtest shows is actually missing rather than designed blind.

Steps 2 and 3 are `mechanics-designer` work and are **out of scope for this spec's
implementation plan**; only step 1 is in scope.

---

## 11. Suggested implementation sequence

This spec is broad. It decomposes into five independently-landable stages, ordered so that
each one is separately revertible and the earliest stage delivers value on its own:

| Stage | Content | Depends on |
|---|---|---|
| **1. Balance correction** | §3 speed revert + Oracle test rewritten against real assets, §4 pile shrink, §3.5 `CLAUDE.md` fix | nothing — **ship this first**, it fixes a live regression and is valuable even if the rest slips |
| **2. Renames** | §9 Snatch + Dive Bomb, freeing the Peck name | nothing |
| **3. Oracle rework** | §7.1 quantised collection model | stage 1 |
| **4. Peck + four slots** | §5 Peck ability, §6 slot architecture, §6.4 Combo retirement | stages 2 and 3 |
| **5. Cleanup + pool** | §8 dead-code removal, §10 step 1 (widen `AllowedClasses`) | stage 4 |

Stage 1 is deliberately first and standalone: it is a bug fix, not a redesign, and
verifying it in isolation tells us how much of the "too fast" complaint the drift alone
was responsible for — which is information we want *before* committing to the rest.

---

## 12. Verification plan

1. **EditMode suite.** Baseline is **274/274**. Expect additions in `BalanceOracleTests`
   (asset-loading + quantised model), `AbilityActivationRulesTests` (4-slot state machine),
   `DataIntegrityTests` (Peck presence/absence per class, `PeckAmount`/`PeckCooldown`
   authored, icon-key resolution). Do not quote a new total until observed.
2. **Oracle green on real assets** — the point of §3.3.
3. **Play Mode solo, each class**: Peck fires only near a pile; refuses with `NoTarget`
   otherwise; cooldown ring reads correctly; 4th hex responds to tap and hold-to-aim;
   Assassin has no Peck and four usable abilities.
4. **Measured SCT sanity check** in a live solo match — wall-clock a greedy farm run per
   class and compare against §5.2. Expect the live number to exceed the Oracle's, per the
   footprint limitation noted in §4.
5. **Multi-client** via `tools/run-clients.ps1`: 4th button replicates, Peck drains the
   pile once per press with no double-drain across peers.
6. **Pixel 9**: 4-hex overlay reachable one-handed; hexes not overlapping the joystick.

---

## 13. Open items

- **`NetworkButtons` capacity** — blocking, verify first (§6.5).
- **Predation axiom's C and T** — needs playtest data (§7.2).
- ~~**Assassin viability with no income floor**~~ — **resolved 2026-08-13** by the execute
  bounty (§7.2). What remains is sizing it: `ExecuteBounty` is proposed at 5 on the
  reasoning that it should match the magnitude of the floor drops it replaces, but that is a
  starting value, not a solved one.
- **Leader-bounty multiplier** — the comeback valve for executing the match leader needs a
  value once it moves off floor drops (§7.2).
- **`ChickenPassive` enum renumbering** vs Fusion byte-serialisation (§6.4).
- **Assassin execute may not work cross-authority** — spotted during this design,
  tracked separately; `ExecuteRemoval` is a plain method guarded by `HasStateAuthority`
  while its sibling cargo transfer is a proper RPC. Not part of this spec.
- `CLAUDE.md`'s "150 food / 3 minutes" needs correcting to 40 / 45 s (§3.5).
- GDD §5.2's "load-bearing detail" sentence about Assassin's 1.7 CollectionRate needs
  rewriting (§7.2).
