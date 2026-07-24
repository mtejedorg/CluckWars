# Combat Rewrite (Control + Steal) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Replace the HP/damage combat model with control + steal: delete health, formalize the Slowed/Rooted/Stunned control ladder, propagate the natural steal cap, and add the Assassin Mark/Kill execute — the only hard removal left.

**Architecture:** The control primitives already exist on `ChickenController` (`SlowMultiplier`, `Rooted`, `ApplySlow`, `ApplyKnockback`, `RPC_ApplyAbilitySlow`, `RPC_ApplyRoot`, `ControlVfx`). This plan adds a general **Stun** state (distinct from the old death-stun), retires the HP pipeline in `ChickenCombat`, generalizes cargo theft (the `min(amount, victimCargo, freeSpace)` cap already lives in `SneakySteal` / `RPC_DrainStolen`), and repurposes the death machinery into the Assassin execute. Pure logic (control-state resolution, steal-cap math) is EditMode-TDD'd; networked wiring is play-mode/MCP-verified.

**Tech Stack:** C# / Fusion 2 Shared Mode, Zenject, NUnit EditMode, Unity MCP (`editor-application-set-state` play-mode, `screenshot-game-view`, `console-get-logs`, `tests-run`).

## Global Constraints

- **`HasStateAuthority` gate** at the top of every `FixedUpdateNetwork`; cross-authority writes via `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]` (CLAUDE.md).
- **The caster endpoint never has the last word** — a cast is a request; the target's StateAuthority validates range/cooldown/legality with tolerance before applying (spec §3.0). Use the existing RPC-to-StateAuthority pattern (`RPC_DrainStolen`, `RPC_ApplyAbilitySlow`).
- **`ILogService` only** (no `Debug.Log`); `using LogLevel = CluckWars.Logging.LogLevel;` where both namespaces are imported; `ChickenClass : byte` unchanged.
- **Animation/VFX are local** — `ChangeDetector` + `Render()`, never RPC'd.
- **Control ladder (spec §3.1):** Slowed = reduced move, can cast, can collect · Rooted = no move, can cast, no collect · **Stunned = no move, no cast, no collect** (the only state that locks abilities). Stun duration is **per-ability**; Assassin Ambush = 1.0 s.
- **Steal cap (spec §3.2):** `stolen = min(abilityStealValue, attackerFreeSpace, defenderCargo)`. No cargo ever hits the floor. The only non-steal cargo transfer is the execute bounty bag.
- **Match (spec §6):** `W = 40`, duration `45 s`, Spoiler bounty `+15`. Values live in `MatchConfigSO`.
- **Depends on:** the Balance Oracle plan (for the stat re-solve in Task 8). **Blocks:** the ability-pool rewrite (needs the new steal/stun/execute primitives).
- Branch `develop`; one commit per task; prefix `feat(combat):` / `refactor(combat):`.

---

## File Structure

- Create `Assets/_Game/Scripts/Gameplay/ControlState.cs` — a pure enum + resolver (`CanMove`/`CanCast`/`CanCollect` as a total function of the active control state). One responsibility: the ladder as testable logic.
- Create `Assets/_Game/Scripts/Gameplay/StealMath.cs` — the `Clamp` steal-cap helper, pure and TDD'd (extracted from the inline `SneakySteal` math so every steal ability shares one implementation).
- Create `Assets/_Game/Scripts/Gameplay/AssassinExecute.cs` — the Mark/Kill state machine as a `NetworkBehaviour` sibling on the Chicken prefab (mark target, arm timer, qualify-on-stun, kill gate).
- Modify `Assets/_Game/Scripts/Gameplay/ChickenController.cs` — add `[Networked] bool IsStunned` general stun + `RPC_ApplyStun(float seconds)`; wire the ladder into the movement/collect/cast gates; add `ControlVfx.Rooted/Stunned` visual bits.
- Modify `Assets/_Game/Scripts/Gameplay/ChickenCombat.cs` — strip the HP path (`HP`, `RPC_ApplyDamage`, resistance/reflect-damage branches); keep and repurpose the stun-timer + respawn + `OnDeathAuthority` (cargo-cancel) as the **execute** removal path, renamed for clarity.
- Modify `Assets/_Game/Scripts/Gameplay/ChickenCargo.cs` — generalize `RPC_DrainStolen` usage; add `RPC_TransferAllToBountyBag(NetworkBehaviourId assassin)` for the execute.
- Modify `Assets/_Game/Data/MatchConfig.asset` — `FoodTargetToWin: 40`, `MatchDurationSeconds: 45`; add `SpoilerBounty: 15`.
- Modify `Assets/_Game/Scripts/Gameplay/MatchConfigSO.cs` — add `SpoilerBounty` field.
- Test `Assets/_Game/Scripts/Editor/Tests/ControlStateTests.cs`, `StealMathTests.cs`.

---

### Task 1: Control-state ladder as pure logic (EditMode-TDD)

**Files:**
- Create: `Assets/_Game/Scripts/Gameplay/ControlState.cs`
- Test: `Assets/_Game/Scripts/Editor/Tests/ControlStateTests.cs`

**Interfaces:**
- Produces: `enum CluckWars.Gameplay.ControlState : byte { Free = 0, Slowed = 1, Rooted = 2, Stunned = 3 }` and `static class ControlRules` with `bool CanMove(ControlState)`, `bool CanCast(ControlState)`, `bool CanCollect(ControlState)`.

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    public sealed class ControlStateTests
    {
        [Test] public void Free_AllowsEverything()
        {
            Assert.IsTrue(ControlRules.CanMove(ControlState.Free));
            Assert.IsTrue(ControlRules.CanCast(ControlState.Free));
            Assert.IsTrue(ControlRules.CanCollect(ControlState.Free));
        }

        [Test] public void Slowed_AllowsCastAndCollect()
        {
            Assert.IsTrue(ControlRules.CanMove(ControlState.Slowed));   // move, just reduced
            Assert.IsTrue(ControlRules.CanCast(ControlState.Slowed));
            Assert.IsTrue(ControlRules.CanCollect(ControlState.Slowed));
        }

        [Test] public void Rooted_BlocksMoveAndCollect_AllowsCast()
        {
            Assert.IsFalse(ControlRules.CanMove(ControlState.Rooted));
            Assert.IsTrue (ControlRules.CanCast(ControlState.Rooted));
            Assert.IsFalse(ControlRules.CanCollect(ControlState.Rooted));
        }

        [Test] public void Stunned_BlocksEverything()
        {
            Assert.IsFalse(ControlRules.CanMove(ControlState.Stunned));
            Assert.IsFalse(ControlRules.CanCast(ControlState.Stunned));   // the ONLY state that locks casting
            Assert.IsFalse(ControlRules.CanCollect(ControlState.Stunned));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails** — EditMode, filter `ControlStateTests`. Expected FAIL (type missing).

- [ ] **Step 3: Implement**

```csharp
namespace CluckWars.Gameplay
{
    /// <summary>The control ladder (spec §3.1). Backing byte to match the project's
    /// other gameplay enums; order is severity-ascending, do not renumber.</summary>
    public enum ControlState : byte { Free = 0, Slowed = 1, Rooted = 2, Stunned = 3 }

    /// <summary>Total function of ControlState → what the chicken may do. Pure so the
    /// whole ladder is pinned by EditMode tests; the NetworkBehaviours call these.</summary>
    public static class ControlRules
    {
        public static bool CanMove(ControlState s)    => s != ControlState.Rooted && s != ControlState.Stunned;
        public static bool CanCast(ControlState s)    => s != ControlState.Stunned;
        public static bool CanCollect(ControlState s) => s == ControlState.Free || s == ControlState.Slowed;
    }
}
```

- [ ] **Step 4: Run test to verify it passes** — Expected PASS (4 tests).
- [ ] **Step 5: Commit** — `git commit -m "feat(combat): control-state ladder as pure logic"`

---

### Task 2: Shared steal-cap helper (EditMode-TDD)

**Files:**
- Create: `Assets/_Game/Scripts/Gameplay/StealMath.cs`
- Test: `Assets/_Game/Scripts/Editor/Tests/StealMathTests.cs`

**Interfaces:**
- Produces: `static float CluckWars.Gameplay.StealMath.Clamp(float abilityValue, float attackerFreeSpace, float defenderCargo)` returning the spec §3.2 minimum, never negative.

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    public sealed class StealMathTests
    {
        [Test] public void Clamp_TakesAbilityValue_WhenRoomAndCargoAmple()
            => Assert.AreEqual(6f, StealMath.Clamp(6f, 10f, 20f), 0.001f);

        [Test] public void Clamp_LimitedByAttackerFreeSpace()      // loaded thief can't steal much
            => Assert.AreEqual(3f, StealMath.Clamp(6f, 3f, 20f), 0.001f);

        [Test] public void Clamp_LimitedByDefenderCargo()          // can't take more than they hold
            => Assert.AreEqual(2f, StealMath.Clamp(6f, 10f, 2f), 0.001f);

        [Test] public void Clamp_ZeroWhenAttackerFull()            // loaded thief steals nothing
            => Assert.AreEqual(0f, StealMath.Clamp(6f, 0f, 20f), 0.001f);

        [Test] public void Clamp_NeverNegative()
            => Assert.AreEqual(0f, StealMath.Clamp(6f, -5f, 20f), 0.001f);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — Expected FAIL (type missing).

- [ ] **Step 3: Implement**

```csharp
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>The natural steal cap (spec §3.2): stolen = min(abilityValue, attacker
    /// free space, defender cargo), never negative. One implementation shared by every
    /// steal ability so the "loaded thief can't steal" rule can't drift per-ability.</summary>
    public static class StealMath
    {
        public static float Clamp(float abilityValue, float attackerFreeSpace, float defenderCargo)
            => Mathf.Max(0f, Mathf.Min(abilityValue, Mathf.Min(attackerFreeSpace, defenderCargo)));
    }
}
```

- [ ] **Step 4: Run to verify it passes** — Expected PASS (5 tests).
- [ ] **Step 5: Commit** — `git commit -m "feat(combat): shared steal-cap helper"`

---

### Task 3: General Stun state on ChickenController

**Files:**
- Modify: `Assets/_Game/Scripts/Gameplay/ChickenController.cs` (add stun field, RPC, and ladder resolution)

**Interfaces:**
- Produces: `[Networked] bool ChickenController.IsStunned { get; }`, `[Rpc(RpcSources.All, RpcTargets.StateAuthority)] void RPC_ApplyStun(float seconds)`, and `ControlState ChickenController.CurrentControlState` computed each tick from `IsStunned` → `Rooted` → `SlowMultiplier < 1`.

**Change-spec (networked — play-mode verified):**
- Add `[Networked] public bool IsStunned { get; private set; }` and `[Networked] TickTimer StunTimer { get; set; }`. This is the **general** stun (control), distinct from the retired death-stun.
- Add `RPC_ApplyStun(float seconds)`: on StateAuthority, set `IsStunned = true`, `StunTimer = TickTimer.CreateFromSeconds(Runner, seconds)`. Respect Speedy's Slippery duration reduction the same way `RPC_ApplyRoot` already does (search that method for the `SlipperyDurationReduction` pattern and mirror it).
- In `FixedUpdateNetwork` (after the existing slow accumulation, before movement): if `IsStunned && StunTimer.Expired(Runner)` → `IsStunned = false`.
- Add a computed `public ControlState CurrentControlState => IsStunned ? ControlState.Stunned : Rooted ? ControlState.Rooted : (SlowMultiplier < 0.99f ? ControlState.Slowed : ControlState.Free);`
- Gate movement on `ControlRules.CanMove(CurrentControlState)` (the existing `Rooted`/`MovementLocked` checks fold into this) and add `ControlVfx.Stunned` to the `vfx` flags when stunned (mirror the `ControlVfx.Slowed` line at ~318).

- [ ] **Step 1:** Apply the change-spec above via `Unity_ScriptApplyEdits` / Edit.
- [ ] **Step 2: Compile** — MCP `assets-refresh`; then `console-get-logs` filtered to errors. Expected: no compile errors.
- [ ] **Step 3: Play-mode smoke** — enter play mode (MCP `editor-application-set-state` play=true), solo match; via `script-execute` call `RPC_ApplyStun(1.5f)` on one chicken; `screenshot-game-view`. Expected: chicken shows stun VFX, cannot move ~1.5 s, then resumes. `editor-application-set-state` play=false.
- [ ] **Step 4: Commit** — `git commit -m "feat(combat): general Stun control state + RPC"`

---

### Task 4: Gate ability casting and collection on the ladder

**Files:**
- Modify: `Assets/_Game/Scripts/Gameplay/AbilityController.cs` (~line 157, the `TryActivate` input path)
- Modify: `Assets/_Game/Scripts/Gameplay/ChickenCargo.cs` (~line 214/238, collection gate)

**Change-spec:**
- In `AbilityController.FixedUpdateNetwork` before `TryActivate`, early-return if `!ControlRules.CanCast(_controller.CurrentControlState)` — stunned chickens cannot cast. (Rooted/Slowed still cast.)
- In `ChickenCargo`'s collection path (`TryCollectFromNearbyPile`), gate on `ControlRules.CanCollect(_controller.CurrentControlState)` — rooted/stunned chickens don't collect; slowed still do.

- [ ] **Step 1:** Apply edits.
- [ ] **Step 2: Compile** — `assets-refresh` + error check.
- [ ] **Step 3: Play-mode smoke** — stun a chicken standing on a pile; confirm cargo does NOT rise and ability buttons do nothing during stun; after stun, both resume. `screenshot-game-view` + `console-get-logs`.
- [ ] **Step 4: Commit** — `git commit -m "feat(combat): stun blocks casting and collection"`

---

### Task 5: Retire the HP/damage pipeline

**Files:**
- Modify: `Assets/_Game/Scripts/Gameplay/ChickenCombat.cs` (delete HP path; keep removal/respawn machinery)
- Modify: `Assets/_Game/Scripts/Gameplay/ChickenController.cs` (remove `DamageResistance`, `ReflectDamage`, `DamageImmune`, `ApplyIncomingDamage`, `ApplyOutgoingDamage`, `SpineCoatKnockbackStrength` — or repurpose per Task 7)

**Change-spec (large, networked — the risky one; cravify's autonomous loop suits it):**
- Remove `[Networked] float HP`, `_hpInitialized`, the HP lazy-init in `FixedUpdateNetwork`, the `HP` `ChangeDetector` case in `Render` (keep the hit-flash trigger but drive it from the new stun/steal events instead), and `RPC_ApplyDamage` in its entirety.
- Keep `IsStunned`/`StunTimer`/`Respawn`/`OnDeathAuthority` **but** these now belong to the execute (Task 6). Rename the death-stun `_stunDuration` usage to an execute-removal duration; `RPC_ResetForNewMatch` keeps resetting removal state (drop the `HP = MaxHP` line).
- Delete the now-dead callers: `RollTrampleAbilitySO`, `CluckShockAbilitySO`, `PeckAbilitySO`, `SpineCoatAbilitySO` all call `RPC_ApplyDamage` — these are rewritten in the **ability-pool plan**, so for THIS plan, leave them compiling by having them no-op or call the new steal path stubs. Simplest: in this task, make `RPC_ApplyDamage` deletion clean by temporarily converting those four `OnActivate` bodies to `// TODO(ability-plan): convert to steal/stun` no-ops that still compile. (The ability plan replaces them fully.)
- Remove HP references in `MatchHud` / `DebugHud` if any (grep `\.HP` project-wide first).

- [ ] **Step 1: Grep the blast radius** — MCP `Unity_Grep` for `RPC_ApplyDamage`, `\.HP\b`, `DamageResistance`, `ReflectDamage`, `ApplyOutgoingDamage`. Record every hit.
- [ ] **Step 2: Apply deletions/stubs** per the change-spec.
- [ ] **Step 3: Compile clean** — `assets-refresh`; zero errors. Re-run the full EditMode suite (`tests-run` EditMode) — the existing `DataIntegrityTests`/`AbilitySystemTests` must stay green (fix any asset assertions that referenced HP).
- [ ] **Step 4: Play-mode smoke** — solo match runs, chickens move/collect/deposit, no NREs in `console-get-logs`.
- [ ] **Step 5: Commit** — `git commit -m "refactor(combat): retire HP/damage pipeline"`

---

### Task 6: Assassin Mark/Kill execute

**Files:**
- Create: `Assets/_Game/Scripts/Gameplay/AssassinExecute.cs` (NetworkBehaviour sibling)
- Modify: `Assets/_Game/Scripts/Gameplay/ChickenCargo.cs` (add `RPC_TransferAllToBountyBag`)
- Modify: Chicken prefab via MCP (add `AssassinExecute` component)

**Interfaces:**
- Consumes: `ChickenController.RPC_ApplyStun`, `ChickenController.CurrentControlState`, `StealMath` (not needed — execute takes ALL), `ControlRules`.
- Produces: `AssassinExecute` with `[Networked] NetworkBehaviourId MarkedTarget`, `[Networked] TickTimer ArmTimer`, `[Networked] bool KillReady`; input-driven `Press()` that does Mark→Kill per spec §4.

**Change-spec (state machine — the fun one):**
- **R = 8m isolation radius, X = 2.0s arm, bounty bag capacity-exempt** (spec §4). Constants at top of the file.
- `Press()` (called on the Mark/Kill button, StateAuthority-validated):
  - If no live mark: find nearest **isolated** rival in facing arc (no other chicken within R of the candidate) via `Physics.OverlapSphere` on `SearchMask`; set `MarkedTarget`, `ArmTimer = CreateFromSeconds(Runner, 2.0f)`, `KillReady = false`. Fire a local panic-VFX event visible to the target.
  - If `KillReady` and the caster is **not** stunned (`ControlRules.CanCast(self.CurrentControlState)`): execute → target `RPC_TransferAllToBountyBag(self.Id)`, target removed ~2s (reuse the retired `ChickenCombat` removal/respawn machinery), clear mark, set the **successful-kill** cooldown.
- `FixedUpdateNetwork` (StateAuthority): if marked, (a) if target breaks isolation or leaves range → clear mark (fizzle); (b) if `ArmTimer.Expired` and target `CurrentControlState == Stunned` → `KillReady = true`; (c) if the arm/kill window fully lapses → clear mark, set the **reduced fail** cooldown.
- `RPC_TransferAllToBountyBag(NetworkBehaviourId assassin)` in `ChickenCargo`: move ALL `Cargo` to the assassin's bounty bag (a new `[Networked] float BountyBag` that is **excluded** from `Capacity`/`IsFull` checks and counts toward deposit at base), then `Cargo = 0`.

- [ ] **Step 1:** Create `AssassinExecute.cs` and the `RPC_TransferAllToBountyBag` + `BountyBag` per change-spec.
- [ ] **Step 2:** Add `AssassinExecute` to `Chicken.prefab` via MCP (`gameobject-component-add` on the prefab; confirm exactly one instance, mirroring the `BotController` precedent in STATE.md).
- [ ] **Step 3: Compile** — zero errors.
- [ ] **Step 4: Play-mode combo test** — solo match, Assassin + one bot. Isolate the bot; press Mark; confirm panic VFX + 2s arm; Ambush-stun the bot; confirm Kill lights up; press Kill; confirm full cargo → assassin bounty bag, bot removed ~2s then respawns empty. Then re-test the two counterplays: (a) keep a 2nd bot within 8m → Mark refuses/fizzles; (b) stun the Assassin during the window → Kill is denied. `screenshot-game-view` at each stage + `console-get-logs`.
- [ ] **Step 5: Commit** — `git commit -m "feat(combat): Assassin Mark/Kill execute + bounty bag"`

---

### Task 7: Repurpose Spine Coat reflect → steal-back (bridge)

**Files:**
- Modify: `Assets/_Game/Scripts/Gameplay/ChickenController.cs` (rename `ReflectDamage`/`SpineCoatKnockbackStrength` semantics to steal-back)

**Change-spec:** Spine Coat becomes "knockback + steal-back from anyone who touches you" (spec ability-plan §3). The full ability SO rewrite is in the ability plan; here just provide the controller-side hook: a `[Networked] bool StealBackActive` + `float StealBackAmount`, and in the collision handler, when an enemy contacts a `StealBackActive` chicken, apply `StealMath.Clamp` from the toucher to the defender + knockback. Leave `ReflectDamage` removed (Task 5).

- [ ] **Step 1:** Apply. **Step 2:** Compile clean. **Step 3:** Play-mode: two chickens collide, one with steal-back on; confirm cargo moves toucher→defender + knockback. **Step 4:** `git commit -m "feat(combat): Spine Coat steal-back hook"`

---

### Task 8: Match config re-solve (W=40, 45s, Spoiler +15) + Oracle check

**Files:**
- Modify: `Assets/_Game/Scripts/Gameplay/MatchConfigSO.cs` (add `SpoilerBounty`)
- Modify: `Assets/_Game/Data/MatchConfig.asset` (`FoodTargetToWin: 40`, `MatchDurationSeconds: 45`, `SpoilerBounty: 15`)
- Modify: `Assets/_Game/Data/Classes/*.asset` (stat re-solve — capacities Speedy 10 / Fatty 35 / Warrior 14 / Assassin 10; CollectionRate roughly doubled; Assassin's rate deliberately low)
- Modify: `Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs` (replace the provisional fixture with real class stats once Task-8 values are set)

**Change-spec:** This is where the Oracle earns its keep. Set candidate stats, feed the real class SOs + the (map-plan) coordinates into `BalanceOracle.Simulate`, and iterate the numbers until every class is `SctTargets.Within` its target. Until the map plan lands real coordinates, use the canonical fixture; note the dependency.

- [ ] **Step 1:** Add `SpoilerBounty` to `MatchConfigSO`; set the three `MatchConfig.asset` values via MCP `assets-modify`.
- [ ] **Step 2:** Set candidate class stats via `assets-modify` on each `Classes/*.asset`.
- [ ] **Step 3:** Write an EditMode test that loads the real class SOs, builds an `OracleChicken` from each, runs `Simulate` against the fixture map, and asserts `SctTargets.Within`. Iterate stat values until green. (This is the "prove the stat re-solve" gate.)
- [ ] **Step 4:** Full EditMode suite green; play-mode: match ends at 45s / first-to-40; DebugHud shows the new target.
- [ ] **Step 5:** `git commit -m "feat(combat): W=40, 45s match, Spoiler bounty, stat re-solve"`

---

## Self-Review

**Spec coverage:** No-HP/no-damage (Task 5) · control ladder Slowed/Rooted/Stunned (Tasks 1,3,4) · steal cap (Task 2, applied in ability plan) · stun locks casting (Task 4) · Assassin execute R=8/X=2/stun-gate/bounty-bag/fail-cooldown (Task 6) · W=40/45s/Spoiler+15 (Task 8) · Spine Coat steal-back hook (Task 7). ✓
**Placeholder scan:** Pure-logic tasks (1,2) carry full TDD code. Networked tasks carry precise change-specs against real files with named fields/methods and play-mode acceptance — the deliberate, documented adaptation for un-unit-testable NetworkBehaviours (see plan header). The one intentional temporary stub (Task 5's ex-damage `OnActivate` no-ops) is explicitly handed to the ability plan.
**Type consistency:** `ControlState`/`ControlRules` (Task 1) consumed by Tasks 3,4,6. `StealMath.Clamp` (Task 2) consumed by Tasks 6,7 and the ability plan. `RPC_ApplyStun`/`CurrentControlState` (Task 3) consumed by Tasks 4,6. `RPC_TransferAllToBountyBag`/`BountyBag` (Task 6) consumed by the win/deposit logic.
**Ordering risk:** Task 5 (delete HP) must land after Tasks 3–4 (so stun exists before damage-stun is removed) and its ex-damage stubs are closed by the ability plan — called out in Task 5's change-spec.
