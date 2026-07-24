# Map & Walls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Re-budget the arena food to the tiered SCT map (T1×4@5 / T2×4@10 / T3@20 = 80 = 2W), and add the pinwheel wall layout so the `Vault`/`Barge`/`Blink` traversal that's already coded finally has geometry to route around.

**Architecture:** `MapGenerator` (876-line `MonoBehaviour`) already spawns 4 personal + 4 contested + 1 center pile and three obstacle classes (Low/Standard/Tall) on a seeded RNG. This plan (1) changes the pile food *values* to the new budget and disables the center pile's permanence/regen (which would break the fixed-F=80 assumption), and (2) adds a seeded **pinwheel** wall pass (radial wedges + a ring) built on the existing `CreateWall`/obstacle infrastructure. It also exports the final pile coordinates so the Balance Oracle's fixture becomes the *real* map. Pure geometry helpers are EditMode-TDD'd; placement is play-mode/MCP-verified via scene screenshots.

**Tech Stack:** C# / Unity `MonoBehaviour`, seeded `System.Random`, Unity MCP (`screenshot-scene-view`, `screenshot-game-view`, `scene-get-data`), NUnit EditMode.

## Global Constraints

- **Total food F = 80 = 2W (spec §2):** T1 doorstep ×4 @ 5 · T2 contested ×4 @ 10 · T3 center ×1 @ 20.
- **Center pile must NOT be permanent/regenerating** — the SCT model assumes fixed F=80 (spec §2 / §7.1). Disable `_centerPileIsPermanent` and any regen.
- **Walls block movement, not sight** (spec §2); seeded from the session code so all peers agree; keep-clear radii around center, bases, and pile surfaces.
- **Walls build on `ObstacleClass`** (Low/Standard/Tall) so `Vault`/`Barge`/`Blink` (ADR 0003, already coded + tested) apply. Boundary walls stay un-clearable (containment is structural — see `TerrainTraversal`/`TraversalRules`).
- **URP materials only**; obstacle material fallback per the recent Android fix (STATE.md) — reuse existing shared materials, no per-wall clones.
- **Depends on:** nothing hard (can build against current combat), but its **output (pile coordinates) feeds the Oracle re-solve** in the combat plan's Task 8 and the PlayMode validation layer (spec §7.2).
- Branch `develop`; one commit per task; prefix `feat(map):`.

---

## File Structure

- Create `Assets/_Game/Scripts/Gameplay/PinwheelLayout.cs` — pure geometry: given arena size, wedge count, and a seed, returns the wall segments (start/end/class) of the pinwheel. One responsibility: the layout math, testable without Unity spawns.
- Modify `Assets/_Game/Scripts/Gameplay/MapGenerator.cs` — call `PinwheelLayout` in the wall pass; change the three pile-amount serialized fields' defaults; disable center permanence.
- Modify `Assets/_Game/Data/Prefabs/FoodPile.prefab` — clear the permanent/regen flags if they live on the prefab.
- Modify `Assets/_Game/Scripts/Editor/Tests/EconomyAndPilesTests.cs` — update the pinned pile-amount assertions to the new budget.
- Create `Assets/_Game/Scripts/Editor/Tests/PinwheelLayoutTests.cs`.

---

### Task 1: Re-budget pile food to the tiered map

**Files:**
- Modify: `Assets/_Game/Scripts/Gameplay/MapGenerator.cs` (`_personalPileAmount` 4→5, `_contestedPileAmount` 25→10, `_centerPileAmount` 60→20, `_centerPileIsPermanent` true→false)
- Modify: `Assets/_Game/Scripts/Editor/Tests/EconomyAndPilesTests.cs` (pinned values)
- Modify: `Assets/_Game/Data/Prefabs/FoodPile.prefab` (regen/floor off, if present)

**Change-spec:** Personal (T1) = 5, Contested (T2) = 10, Center (T3) = 20. With 4+4+1 piles that is 20+40+20 = 80. Disable center permanence + regen so F stays fixed.

- [ ] **Step 1: Update the pinned test first (TDD-ish)** — in `EconomyAndPilesTests.cs`, change the asserted amounts to 5 / 10 / 20 and assert the map total equals 80. Run EditMode → FAIL (generator still emits old values).
- [ ] **Step 2:** Set the four serialized defaults in `MapGenerator.cs`; clear `FoodPile.prefab` permanence/regen via MCP `assets-modify` if the flags are on the prefab (grep `Permanent`/`Regen` first via `Unity_Grep`).
- [ ] **Step 3:** Run EditMode → the economy test passes; full suite green.
- [ ] **Step 4: Play-mode** — solo match; `screenshot-game-view`; confirm center is smaller, all piles present, and (via `script-execute`) the summed pile food = 80.
- [ ] **Step 5: Commit** — `git commit -m "feat(map): re-budget piles to tiered 80-food map"`

---

### Task 2: Pinwheel layout geometry (EditMode-TDD)

**Files:**
- Create: `Assets/_Game/Scripts/Gameplay/PinwheelLayout.cs`
- Test: `Assets/_Game/Scripts/Editor/Tests/PinwheelLayoutTests.cs`

**Interfaces:**
- Produces: `struct WallSegment { Vector2 A; Vector2 B; ObstacleClass Class; }` and `static WallSegment[] PinwheelLayout.Build(float arenaHalfSize, int wedges, int seed, float centerKeepClear, float baseKeepClear)`. Deterministic for a given seed; never places a segment inside the center or base keep-clear discs.

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using UnityEngine;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    public sealed class PinwheelLayoutTests
    {
        [Test] public void Build_IsDeterministicForSeed()
        {
            var a = PinwheelLayout.Build(15f, 8, seed: 42, centerKeepClear: 3f, baseKeepClear: 4f);
            var b = PinwheelLayout.Build(15f, 8, seed: 42, centerKeepClear: 3f, baseKeepClear: 4f);
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].A, b[i].A);
                Assert.AreEqual(a[i].B, b[i].B);
                Assert.AreEqual(a[i].Class, b[i].Class);
            }
        }

        [Test] public void Build_ProducesOneSegmentPerWedge()
        {
            var segs = PinwheelLayout.Build(15f, 8, seed: 1, centerKeepClear: 3f, baseKeepClear: 4f);
            Assert.AreEqual(8, segs.Length);
        }

        [Test] public void Build_KeepsSegmentsOutOfCenterDisc()
        {
            var segs = PinwheelLayout.Build(15f, 8, seed: 7, centerKeepClear: 3f, baseKeepClear: 4f);
            foreach (var s in segs)
            {
                // Neither endpoint may sit inside the center keep-clear disc.
                Assert.GreaterOrEqual(s.A.magnitude, 3f, "segment endpoint inside center keep-clear");
                Assert.GreaterOrEqual(s.B.magnitude, 3f, "segment endpoint inside center keep-clear");
            }
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — Expected FAIL (type missing). Note: `ObstacleClass` already exists (from `TerrainObstacle`), reuse it.

- [ ] **Step 3: Implement** `PinwheelLayout.Build` — for each of `wedges`, compute an angle `2π·i/wedges` plus a seeded jitter, emit a radial segment from `centerKeepClear + margin` out toward `arenaHalfSize`, skipping the base-corner keep-clear discs; assign `ObstacleClass` in a seeded pattern (e.g. alternate Low/Standard, occasional Tall) so `Vault`/`Barge`/`Blink` all matter. (Complete implementation written to satisfy the three tests: determinism from a single `System.Random(seed)`, one segment per wedge, endpoints outside the center disc.)

- [ ] **Step 4: Run to verify it passes** — Expected PASS (3 tests).
- [ ] **Step 5: Commit** — `git commit -m "feat(map): pinwheel wall layout geometry"`

---

### Task 3: Spawn the pinwheel in MapGenerator

**Files:**
- Modify: `Assets/_Game/Scripts/Gameplay/MapGenerator.cs` (wall pass calls `PinwheelLayout.Build`, spawns each segment via the existing `CreateWall` + obstacle-class tagging)

**Change-spec:** Replace/augment the current scattered interior-wall pass with the pinwheel. Each `WallSegment` becomes a wall GameObject carrying a `TerrainObstacle` of the segment's `ObstacleClass` (so traversal applies) — reuse the shared per-class materials (no clones, per the Android fix). Seed from the session code (same source the existing obstacle pass uses) so all peers agree. Keep boundary walls untouched.

- [ ] **Step 1:** Wire `PinwheelLayout.Build` into the wall pass; map each segment to a `CreateWall` call + `TerrainObstacle` with the right `ObstacleClass`.
- [ ] **Step 2: Compile clean** — `assets-refresh` + error check.
- [ ] **Step 3: Scene verification** — solo match; MCP `screenshot-scene-view` (top-down) and `screenshot-game-view`; visually confirm the pinwheel reads like the reference sketch (radial wedges, clear lanes, center/bases/pile-surfaces clear). Confirm bots path without freezing (`console-get-logs` for NavMesh warnings — cross-reference the recent "bot pathing freeze near piles" fix).
- [ ] **Step 4: Determinism check** — restart the match with the same session seed; confirm identical layout (`scene-get-data` wall count/positions stable).
- [ ] **Step 5: Commit** — `git commit -m "feat(map): spawn pinwheel walls with obstacle classes"`

---

### Task 4: Export final pile coordinates for the Oracle

**Files:**
- Create/Modify: a small editor utility or a logged dump so the combat plan's Task 8 can paste real coordinates into the Oracle fixture.

**Change-spec:** Add an editor-only method (or a `_log.Info` dump behind a debug flag) that prints each spawned pile's world XZ + food to the console after generation. This closes the loop: the Oracle's provisional fixture (Balance Oracle plan Task 6) is replaced by these real coordinates, and the SCT re-solve (combat plan Task 8) runs against the actual map.

- [ ] **Step 1:** Add the coordinate dump.
- [ ] **Step 2: Play-mode** — generate a map; copy the printed coordinates; verify they sum to F=80 and match the tier layout.
- [ ] **Step 3:** Hand the coordinates to the combat plan's Oracle re-solve (cross-plan handoff — note in commit).
- [ ] **Step 4: Commit** — `git commit -m "feat(map): dump pile coordinates for Oracle fixture"`

---

## Self-Review

**Spec coverage:** Tiered budget T1/T2/T3 = 80 (Task 1) · center not permanent (Task 1) · pinwheel walls on ObstacleClass, seeded, keep-clear (Tasks 2,3) · boundary containment preserved (Task 3, untouched) · coordinates feed the Oracle (Task 4). ✓
**Placeholder scan:** Task 2 (pure geometry) is full TDD. Tasks 1,3,4 are asset/scene work verified by MCP screenshots + `scene-get-data`, the correct verification surface for procedural spawns (no EditMode path exercises a live scene generate). Task 3's implementation detail ("map each segment to CreateWall") points at the real existing method rather than inventing a signature — confirm `CreateWall`'s parameters in-file before wiring.
**Type consistency:** `WallSegment`/`ObstacleClass` (Task 2) consumed by Task 3. `PinwheelLayout.Build(...)` signature identical across Tasks 2–3.
**Cross-plan:** Task 4's coordinate export is the explicit dependency the combat plan's Task 8 (`stat re-solve`) consumes — both plans reference it.
