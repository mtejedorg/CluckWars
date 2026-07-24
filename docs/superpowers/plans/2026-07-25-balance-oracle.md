# Balance Oracle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a pure-C# simulation that, given a map (pile positions + food, base position) and a class's stats, returns its Solo Clear Time (SCT) in seconds and trip count — so every stat can be *solved* against the SCT axiom instead of guessed.

**Architecture:** A stateless static simulator (`BalanceOracle.Simulate`) running a greedy "farm nearest pile, return when full" policy over a mutable copy of pile food, accumulating travel + collect + deposit time on straight-line distances. No Fusion, no MonoBehaviour, no PlayMode — it compiles into `Assembly-CSharp` and is exercised by EditMode NUnit tests in `Assembly-CSharp-Editor`. A second, later layer (a separate plan) validates the model against real navmesh in PlayMode; this plan is the model layer only.

**Tech Stack:** C# (Unity 6000.3, `Assembly-CSharp`), `UnityEngine.Vector2` for coordinates, NUnit EditMode tests, Unity Test Runner / MCP `tests-run`.

## Global Constraints

- **No Fusion / no MonoBehaviour in the simulator** — pure static C#, deterministic, millisecond runtime (spec §7.1).
- **Straight-line distances only** in the model layer — wall/navmesh cost is the PlayMode validation layer's job, a separate plan (spec §7.2).
- **Production code** lives in `Assets/_Game/Scripts/Balance/`, namespace `CluckWars.Balance` (Assembly-CSharp, **no .asmdef** — matches project convention; see `docs/CONVENTIONS.md`).
- **Tests** live in `Assets/_Game/Scripts/Editor/Tests/`, namespace `CluckWars.Tests` (compiles to `Assembly-CSharp-Editor`; the Editor folder is what keeps them out of Fusion's IL weaver — see the header comment in `CoreLogicTests.cs`). NUnit `[Test]`, `Assert.AreEqual(expected, actual, tolerance)`.
- **SCT targets (spec §1):** Speedy 30 s / 4 trips · Fatty 30 s / 2 trips · Warrior 35 s / 3 trips · Assassin 40 s / 4 trips. Tolerance **±1.5 s**, trips **exact**. `W = 40` (win target), `F = 80` (total map food).
- **Running tests:** Unity Test Runner ▸ EditMode, filter to `BalanceOracleTests`; or MCP `tests-run` with `testMode: EditMode`. A green run is the gate for each commit.
- **Commit cadence:** one commit per task, on branch `develop` (already current). Prefix `feat(balance):`.

---

## File Structure

- `Assets/_Game/Scripts/Balance/BalanceTypes.cs` — the four input/output structs (`OraclePile`, `OracleMap`, `OracleChicken`, `OracleResult`). One responsibility: data shapes.
- `Assets/_Game/Scripts/Balance/BalanceOracle.cs` — the `Simulate` algorithm. One responsibility: the greedy policy + time accounting.
- `Assets/_Game/Scripts/Balance/SctTargets.cs` — the SCT target table (spec §1) + a `Within` tolerance helper. One responsibility: the balance goalposts as data.
- `Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs` — all EditMode tests for the above.

`Vector2` is `UnityEngine.Vector2`; it is safe in EditMode tests and carries no Fusion dependency.

---

### Task 1: Oracle data types + single-trip simulation

**Files:**
- Create: `Assets/_Game/Scripts/Balance/BalanceTypes.cs`
- Create: `Assets/_Game/Scripts/Balance/BalanceOracle.cs`
- Test: `Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs`

**Interfaces:**
- Produces: `CluckWars.Balance.OraclePile { Vector2 Pos; float Food; }`, `OracleMap { Vector2 BasePos; OraclePile[] Piles; }`, `OracleChicken { float MoveSpeed; int CargoCapacity; float CollectionRate; float DepositRate; }`, `OracleResult { float SctSeconds; int Trips; bool ReachedTarget; }`, and `static OracleResult BalanceOracle.Simulate(OracleMap map, OracleChicken chicken, float winTarget)`.

- [ ] **Step 1: Write the failing test**

Create `Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using CluckWars.Balance;

namespace CluckWars.Tests
{
    /// <summary>
    /// EditMode tests for the Balance Oracle (spec: docs/superpowers/specs/
    /// 2026-07-24-cluck-wars-core-redesign-design.md §7). Pure-C# simulation, so every
    /// case here is hand-computable: travel = distance / speed, collect = units / rate,
    /// deposit = units / depositRate.
    /// </summary>
    public sealed class BalanceOracleTests
    {
        // One pile that is exactly one full load, one round trip.
        // base(0,0) -> pile(10,0) food 10; speed 10, cap 10, rate 10, deposit 10, W 10.
        // out 1s + collect 1s + back 1s + deposit 1s = 4.0s, 1 trip.
        [Test]
        public void Simulate_SingleFullLoad_OneTrip()
        {
            var map = new OracleMap
            {
                BasePos = new Vector2(0f, 0f),
                Piles = new[] { new OraclePile { Pos = new Vector2(10f, 0f), Food = 10f } }
            };
            var chicken = new OracleChicken
            {
                MoveSpeed = 10f, CargoCapacity = 10, CollectionRate = 10f, DepositRate = 10f
            };

            var result = BalanceOracle.Simulate(map, chicken, winTarget: 10f);

            Assert.IsTrue(result.ReachedTarget, "Should bank the target.");
            Assert.AreEqual(4.0f, result.SctSeconds, 0.01f);
            Assert.AreEqual(1, result.Trips);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run EditMode tests filtered to `BalanceOracleTests` (Test Runner ▸ EditMode, or MCP `tests-run` `testMode: EditMode`).
Expected: FAIL — `CluckWars.Balance` / `BalanceOracle` does not exist (compile error).

- [ ] **Step 3: Write the data types**

Create `Assets/_Game/Scripts/Balance/BalanceTypes.cs`:

```csharp
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>A pile the model can farm: world position and remaining food.</summary>
    public struct OraclePile
    {
        public Vector2 Pos;
        public float Food;
    }

    /// <summary>The full map the Oracle simulates: one base + the piles to farm.</summary>
    public struct OracleMap
    {
        public Vector2 BasePos;
        public OraclePile[] Piles;
    }

    /// <summary>The naked stats the SCT axiom is solved against (no abilities, no passives).</summary>
    public struct OracleChicken
    {
        public float MoveSpeed;      // world units / second
        public int   CargoCapacity;  // units carried before a forced return
        public float CollectionRate; // units / second while on a pile
        public float DepositRate;    // units / second while at base
    }

    /// <summary>Simulation output: Solo Clear Time, trip count, and whether the target was reachable.</summary>
    public struct OracleResult
    {
        public float SctSeconds;
        public int   Trips;
        public bool  ReachedTarget;
    }
}
```

- [ ] **Step 4: Write the simulator (complete algorithm)**

Create `Assets/_Game/Scripts/Balance/BalanceOracle.cs`. This implements the whole greedy policy up front — travel, greedy nearest-pile collect, capacity-forced return, exact win-crossing during deposit, and the unreachable fallback — because it is one cohesive method. Later tasks pin each of those behaviours with their own tests.

```csharp
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>
    /// Solo Clear Time simulator. Runs the greedy "farm nearest pile, return when full"
    /// policy the SCT axiom describes and returns seconds + trip count. Pure, deterministic,
    /// no Fusion — spec §7.1. Travel is straight-line (walls are the PlayMode layer's job, §7.2).
    /// </summary>
    public static class BalanceOracle
    {
        // Guards against a policy bug spinning forever; far above any real trip count.
        private const int MaxIterations = 10_000;
        private const float Epsilon = 1e-4f;

        public static OracleResult Simulate(OracleMap map, OracleChicken chicken, float winTarget)
        {
            // Mutable copy of pile food so the input map is not modified.
            float[] remaining = new float[map.Piles.Length];
            for (int i = 0; i < map.Piles.Length; i++) remaining[i] = map.Piles[i].Food;

            Vector2 pos = map.BasePos;
            float carrying = 0f;
            float banked = 0f;
            float time = 0f;
            int trips = 0;
            int capacity = chicken.CargoCapacity;

            for (int iter = 0; iter < MaxIterations; iter++)
            {
                bool anyFood = false;
                for (int i = 0; i < remaining.Length; i++)
                    if (remaining[i] > Epsilon) { anyFood = true; break; }

                bool full = carrying >= capacity - Epsilon;

                // Decide: deposit, or collect.
                if (full || (!anyFood && carrying > Epsilon))
                {
                    // ---- return to base and deposit ----
                    time += Vector2.Distance(pos, map.BasePos) / chicken.MoveSpeed;
                    pos = map.BasePos;

                    float needed = winTarget - banked;
                    if (carrying >= needed - Epsilon)
                    {
                        // Win crosses mid-deposit — only count the time to bank `needed`.
                        time += needed / chicken.DepositRate;
                        banked = winTarget;
                        trips++;
                        return new OracleResult { SctSeconds = time, Trips = trips, ReachedTarget = true };
                    }

                    time += carrying / chicken.DepositRate;
                    banked += carrying;
                    carrying = 0f;
                    trips++;
                    continue;
                }

                if (anyFood)
                {
                    // ---- walk to the nearest pile that still has food, collect ----
                    int nearest = -1;
                    float bestDist = float.MaxValue;
                    for (int i = 0; i < remaining.Length; i++)
                    {
                        if (remaining[i] <= Epsilon) continue;
                        float d = Vector2.Distance(pos, map.Piles[i].Pos);
                        if (d < bestDist) { bestDist = d; nearest = i; }
                    }

                    time += bestDist / chicken.MoveSpeed;
                    pos = map.Piles[nearest].Pos;

                    float space = capacity - carrying;
                    float take = Mathf.Min(space, remaining[nearest]);
                    time += take / chicken.CollectionRate;
                    carrying += take;
                    remaining[nearest] -= take;
                    continue;
                }

                // No food left and nothing to deposit — target is unreachable.
                return new OracleResult { SctSeconds = time, Trips = trips, ReachedTarget = false };
            }

            // Iteration guard tripped — treat as unreachable rather than hang.
            return new OracleResult { SctSeconds = time, Trips = trips, ReachedTarget = false };
        }
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run EditMode tests filtered to `BalanceOracleTests`.
Expected: PASS (`Simulate_SingleFullLoad_OneTrip`).

- [ ] **Step 6: Commit**

```bash
git add Assets/_Game/Scripts/Balance/ Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs
git commit -m "feat(balance): SCT oracle — single-trip simulation"
```

---

### Task 2: Capacity forces multiple trips

**Files:**
- Modify: `Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs` (add one test)

**Interfaces:**
- Consumes: `BalanceOracle.Simulate` from Task 1.

- [ ] **Step 1: Write the failing test**

Add to `BalanceOracleTests`:

```csharp
// One 20-food pile, capacity 10 -> two identical round trips.
// base(0,0) -> pile(10,0) food 20; speed 10, cap 10, rate 10, deposit 10, W 20.
// trip1 4.0s (bank 10) + trip2 4.0s (bank 20) = 8.0s, 2 trips.
[Test]
public void Simulate_CapacityForcesTwoTrips()
{
    var map = new OracleMap
    {
        BasePos = new Vector2(0f, 0f),
        Piles = new[] { new OraclePile { Pos = new Vector2(10f, 0f), Food = 20f } }
    };
    var chicken = new OracleChicken
    {
        MoveSpeed = 10f, CargoCapacity = 10, CollectionRate = 10f, DepositRate = 10f
    };

    var result = BalanceOracle.Simulate(map, chicken, winTarget: 20f);

    Assert.IsTrue(result.ReachedTarget);
    Assert.AreEqual(8.0f, result.SctSeconds, 0.01f);
    Assert.AreEqual(2, result.Trips);
}
```

- [ ] **Step 2: Run test to verify it passes**

Run EditMode tests filtered to `BalanceOracleTests`.
Expected: PASS — Task 1's algorithm already handles capacity-forced returns; this pins that behaviour.

(If it FAILS, the capacity/return branch in `Simulate` is wrong — re-check the `full` computation and the deposit-continue path in Task 1 Step 4.)

- [ ] **Step 3: Commit**

```bash
git add Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs
git commit -m "test(balance): pin capacity-forced multi-trip"
```

---

### Task 3: Greedy nearest-pile selection

**Files:**
- Modify: `Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs` (add one test)

**Interfaces:**
- Consumes: `BalanceOracle.Simulate` from Task 1.

- [ ] **Step 1: Write the failing test**

Add to `BalanceOracleTests`:

```csharp
// Two piles; the near one must be farmed first. Proves greedy-nearest ordering.
// base(0,0); A(5,0) food 5; B(20,0) food 5; speed 5, cap 10, rate 5, deposit 5, W 10.
// ->A 1s, collect 1s (carry 5, not full); ->B dist15 =3s, collect 1s (carry 10 full);
// back from (20,0) 4s; deposit 10 -> 2s. total 12.0s, 1 trip.
[Test]
public void Simulate_GreedyPicksNearestPileFirst()
{
    var map = new OracleMap
    {
        BasePos = new Vector2(0f, 0f),
        Piles = new[]
        {
            new OraclePile { Pos = new Vector2(5f, 0f),  Food = 5f },
            new OraclePile { Pos = new Vector2(20f, 0f), Food = 5f }
        }
    };
    var chicken = new OracleChicken
    {
        MoveSpeed = 5f, CargoCapacity = 10, CollectionRate = 5f, DepositRate = 5f
    };

    var result = BalanceOracle.Simulate(map, chicken, winTarget: 10f);

    Assert.IsTrue(result.ReachedTarget);
    Assert.AreEqual(12.0f, result.SctSeconds, 0.01f);
    Assert.AreEqual(1, result.Trips);
}
```

- [ ] **Step 2: Run test to verify it passes**

Run EditMode tests filtered to `BalanceOracleTests`.
Expected: PASS — pins the nearest-pile loop in Task 1. (If FAIL: the `bestDist` selection scans from the wrong position — it must measure from the chicken's *current* `pos`, not the base.)

- [ ] **Step 3: Commit**

```bash
git add Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs
git commit -m "test(balance): pin greedy nearest-pile ordering"
```

---

### Task 4: Exact win-crossing mid-deposit

**Files:**
- Modify: `Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs` (add one test)

**Interfaces:**
- Consumes: `BalanceOracle.Simulate` from Task 1.

- [ ] **Step 1: Write the failing test**

Add to `BalanceOracleTests`:

```csharp
// Carrying 15 but only 10 needed to win — SCT must count deposit time for 10, not 15.
// base(0,0) -> pile(10,0) food 15; speed 10, cap 15, rate 10, deposit 10, W 10.
// out 1s + collect 15 (1.5s) + back 1s + deposit 10-of-15 (1.0s) = 4.5s, 1 trip.
[Test]
public void Simulate_WinCrossesMidDeposit_CountsOnlyNeeded()
{
    var map = new OracleMap
    {
        BasePos = new Vector2(0f, 0f),
        Piles = new[] { new OraclePile { Pos = new Vector2(10f, 0f), Food = 15f } }
    };
    var chicken = new OracleChicken
    {
        MoveSpeed = 10f, CargoCapacity = 15, CollectionRate = 10f, DepositRate = 10f
    };

    var result = BalanceOracle.Simulate(map, chicken, winTarget: 10f);

    Assert.IsTrue(result.ReachedTarget);
    Assert.AreEqual(4.5f, result.SctSeconds, 0.01f);
    Assert.AreEqual(1, result.Trips);
}
```

- [ ] **Step 2: Run test to verify it passes**

Run EditMode tests filtered to `BalanceOracleTests`.
Expected: PASS — pins the `carrying >= needed` crossing branch. (If FAIL: `Simulate` is counting `carrying / DepositRate` instead of `needed / DepositRate` on the winning deposit — fix that branch in Task 1 Step 4.)

- [ ] **Step 3: Commit**

```bash
git add Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs
git commit -m "test(balance): pin exact win-crossing during deposit"
```

---

### Task 5: Unreachable target reports ReachedTarget=false

**Files:**
- Modify: `Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs` (add one test)

**Interfaces:**
- Consumes: `BalanceOracle.Simulate` from Task 1.

- [ ] **Step 1: Write the failing test**

Add to `BalanceOracleTests`:

```csharp
// Map holds less food than the win target — the chicken can never bank W.
// base(0,0) -> pile(10,0) food 5; W 10. Collects 5, deposits 5, then no food & empty.
[Test]
public void Simulate_NotEnoughFood_ReportsUnreachable()
{
    var map = new OracleMap
    {
        BasePos = new Vector2(0f, 0f),
        Piles = new[] { new OraclePile { Pos = new Vector2(10f, 0f), Food = 5f } }
    };
    var chicken = new OracleChicken
    {
        MoveSpeed = 10f, CargoCapacity = 10, CollectionRate = 10f, DepositRate = 10f
    };

    var result = BalanceOracle.Simulate(map, chicken, winTarget: 10f);

    Assert.IsFalse(result.ReachedTarget, "Target exceeds total map food; must be unreachable.");
}
```

- [ ] **Step 2: Run test to verify it passes**

Run EditMode tests filtered to `BalanceOracleTests`.
Expected: PASS — pins the "no food, nothing carried" terminal branch. (If FAIL or hangs: the terminal `return ... ReachedTarget = false` is unreachable and the loop relies on `MaxIterations` — ensure the `!anyFood && carrying <= Epsilon` case returns, which it does after the final deposit empties `carrying`.)

- [ ] **Step 3: Commit**

```bash
git add Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs
git commit -m "test(balance): pin unreachable-target detection"
```

---

### Task 6: SCT targets table + class-hits-target harness

This is the payoff: the table of goalposts from spec §1, a tolerance helper, and a demonstration test that solves a **self-consistent canonical fixture** to prove the whole machinery end-to-end. The fixture's coordinates and the candidate stats are **provisional** — the real pile coordinates are owned by the map plan and the real stats by the combat plan; when those land, this fixture is replaced with the shipped map + solved stats and the same assertion re-run. Until then it proves the Oracle can *express and check* an SCT target.

**Files:**
- Create: `Assets/_Game/Scripts/Balance/SctTargets.cs`
- Modify: `Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs` (add fixture + tests)

**Interfaces:**
- Produces: `CluckWars.Balance.SctTarget { string ClassName; float Seconds; int Trips; }`, `static readonly SctTarget[] SctTargets.All`, and `static bool SctTargets.Within(OracleResult result, SctTarget target, float toleranceSeconds = 1.5f)`.
- Consumes: `BalanceOracle.Simulate`, `OracleMap`, `OracleChicken` from Task 1.

- [ ] **Step 1: Write the failing test**

Add to `BalanceOracleTests` (helpers + tests):

```csharp
// --- Task 6: SCT target goalposts (spec §1) ------------------------------

// A self-consistent PROVISIONAL fixture: one base at origin, nine piles laid on a
// line so distances are hand-checkable. Total food = 40 (= W). Replace with the
// shipped map coordinates once the map plan lands.
private static OracleMap CanonicalFixture()
{
    return new OracleMap
    {
        BasePos = new Vector2(0f, 0f),
        Piles = new[]
        {
            new OraclePile { Pos = new Vector2(4f, 0f),  Food = 5f },  // T1 doorstep
            new OraclePile { Pos = new Vector2(8f, 0f),  Food = 10f }, // T2
            new OraclePile { Pos = new Vector2(12f, 0f), Food = 10f }, // T2
            new OraclePile { Pos = new Vector2(16f, 0f), Food = 15f }, // T3-ish
        }
    };
}

[Test]
public void SctTargets_TableMatchesSpec()
{
    Assert.AreEqual(4, SctTargets.All.Length);
    var speedy = System.Array.Find(SctTargets.All, t => t.ClassName == "Speedy");
    Assert.AreEqual(30f, speedy.Seconds, 0.001f);
    Assert.AreEqual(4, speedy.Trips);
    var fatty = System.Array.Find(SctTargets.All, t => t.ClassName == "Fatty");
    Assert.AreEqual(30f, fatty.Seconds, 0.001f);
    Assert.AreEqual(2, fatty.Trips);
}

[Test]
public void Within_AcceptsInsideTolerance_RejectsOutside()
{
    var target = new SctTarget { ClassName = "X", Seconds = 30f, Trips = 4 };
    Assert.IsTrue(SctTargets.Within(
        new OracleResult { SctSeconds = 31.2f, Trips = 4, ReachedTarget = true }, target));
    Assert.IsFalse(SctTargets.Within(
        new OracleResult { SctSeconds = 31.2f, Trips = 3, ReachedTarget = true }, target),
        "Wrong trip count must fail even if seconds are inside tolerance.");
    Assert.IsFalse(SctTargets.Within(
        new OracleResult { SctSeconds = 33f, Trips = 4, ReachedTarget = true }, target),
        "Outside the ±1.5s window must fail.");
    Assert.IsFalse(SctTargets.Within(
        new OracleResult { SctSeconds = 30f, Trips = 4, ReachedTarget = false }, target),
        "Not reaching the target must fail regardless of seconds.");
}

// Demonstrates solving: candidate stats tuned to hit a 30s / 2-trip target on the
// provisional fixture (W=40, cap 20 -> ceil(40/20)=2 trips). Proves the harness end to end.
[Test]
public void CanonicalFixture_CandidateStatsHitTarget()
{
    var map = CanonicalFixture();
    var chicken = new OracleChicken
    {
        MoveSpeed = 4.2f, CargoCapacity = 20, CollectionRate = 3.0f, DepositRate = 6.0f
    };

    var result = BalanceOracle.Simulate(map, chicken, winTarget: 40f);

    Assert.IsTrue(result.ReachedTarget);
    Assert.AreEqual(2, result.Trips, "cap 20 over W 40 must be exactly 2 trips.");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run EditMode tests filtered to `BalanceOracleTests`.
Expected: FAIL — `SctTarget` / `SctTargets` do not exist (compile error).

- [ ] **Step 3: Write the targets table**

Create `Assets/_Game/Scripts/Balance/SctTargets.cs`:

```csharp
namespace CluckWars.Balance
{
    /// <summary>One class's Solo Clear Time goalpost (spec §1).</summary>
    public struct SctTarget
    {
        public string ClassName;
        public float  Seconds;
        public int    Trips;
    }

    /// <summary>
    /// The SCT axiom's goalposts and the tolerance check. Change these four numbers to
    /// re-solve the whole game (spec §1 / §7.1). The Oracle asserts each class's naked
    /// stats land Within() its target.
    /// </summary>
    public static class SctTargets
    {
        public const float DefaultToleranceSeconds = 1.5f;

        public static readonly SctTarget[] All =
        {
            new SctTarget { ClassName = "Speedy",   Seconds = 30f, Trips = 4 },
            new SctTarget { ClassName = "Fatty",    Seconds = 30f, Trips = 2 },
            new SctTarget { ClassName = "Warrior",  Seconds = 35f, Trips = 3 },
            new SctTarget { ClassName = "Assassin", Seconds = 40f, Trips = 4 },
        };

        /// <summary>True iff the result reached the target, matched the trip count exactly,
        /// and landed within <paramref name="toleranceSeconds"/> of the target time.</summary>
        public static bool Within(OracleResult result, SctTarget target,
            float toleranceSeconds = DefaultToleranceSeconds)
        {
            if (!result.ReachedTarget) return false;
            if (result.Trips != target.Trips) return false;
            return UnityEngine.Mathf.Abs(result.SctSeconds - target.Seconds) <= toleranceSeconds;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run EditMode tests filtered to `BalanceOracleTests`.
Expected: PASS — all three Task 6 tests plus the five earlier ones (8 total green).

- [ ] **Step 5: Commit**

```bash
git add Assets/_Game/Scripts/Balance/SctTargets.cs Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs
git commit -m "feat(balance): SCT target table + tolerance harness"
```

---

## Self-Review

**Spec coverage (§7.1 model layer):**
- Pure C#, no Fusion, ms runtime → Tasks 1–6, all EditMode. ✓
- Inputs = real pile coords + values, base position, per-class stats, W → `OracleMap` / `OracleChicken` (Task 1). ✓
- Greedy "farm nearest, return when full" on real coordinates → `Simulate` (Task 1), pinned by Tasks 2–4. ✓
- Outputs SCT + trip count → `OracleResult` (Task 1). ✓
- Assert within ±1.5 s, exact trips, configurable targets → `SctTargets` + `Within` (Task 6). ✓
- **Not in this plan (correctly):** §7.2 PlayMode validation layer (needs a live navmesh/bot — separate plan); pile-slow effect on collection (a model refinement — noted below); wall travel cost (spec assigns this to §7.2). 

**Placeholder scan:** No TBD/TODO in code steps; every step shows complete code and an exact expected result. The canonical fixture in Task 6 is explicitly labelled provisional with the reason and the follow-up owner — that is a documented handoff, not a placeholder in the code.

**Type consistency:** `Simulate(OracleMap, OracleChicken, float)` → `OracleResult` is used identically in every task. `SctTarget` fields (`ClassName`/`Seconds`/`Trips`) and `SctTargets.Within(OracleResult, SctTarget, float)` match between Task 6's tests and implementation. `OraclePile.Food`, `OracleMap.BasePos/Piles`, `OracleChicken.MoveSpeed/CargoCapacity/CollectionRate/DepositRate` are consistent across all tasks.

**Known model refinement (deferred, not a gap):** the model ignores pile-slow on exit and collision slow (spec §6.2/§6.1) — both are second-order for a *solo* clear and belong to a later tuning pass once the PlayMode validation layer shows whether they matter. Recorded here so the next plan's author sees it.

---

## Follow-on plans (this is 1 of 4)

1. **Balance Oracle** — this plan (model layer).
2. **Combat rewrite** — delete HP/damage; control ladder (slow/root/stun); natural steal cap; the Assassin Mark/Kill execute; passive pools. Consumes the Oracle to re-solve stats.
3. **Map & walls** — tiered pile budget (T1×4@5 / T2×4@10 / T3@20), pinwheel walls on `ObstacleClass`; feeds real pile coordinates back into the Oracle fixture (replaces Task 6's provisional fixture) and the PlayMode validation layer (spec §7.2).
4. **Ability-pool rewrite** — the four-category roster, two new abilities (Mark/Kill, Ambush/Wing Slam, Shadowstep), `AllowedClasses`/`SlotKind` reassignment, GDD §5/§7 update.
