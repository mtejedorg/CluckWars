# Match Simulator (Balance Oracle v2, part 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Play whole four-player Cluck Wars matches in a pure abstract model, thousands of times, and report per-kit and per-class win rates, the Spoiler's kills-to-win, Thief income, Peck cuts and first contact; then let the part 1 solver propose values against those rates.

**Architecture:** A fixed-tick `MatchSim` moves four chickens in continuous 2D on a wall-aware path graph (`OracleNav`) built from the baked `OracleMapSO`, resolves abilities against a per-tick snapshot, and asks one `IMatchPolicy` per seat for an intent. Kits are built once from the shipped assets through `BalanceBook.Preview` (`MatchKits`), then the simulation touches only plain values, so `MatchRunner` runs matches in parallel and sums in index order. P0 transcribes the game's bot through `BotTactics`; P1 is a utility chooser; both see rivals through the game's bot sight rule (`MatchSight`). The report section, the match records on `OracleReferenceSO` and a match term in `OracleSolver` join part 1 without changing its numbers.

**Tech Stack:** Unity 6000.3, C#, NUnit EditMode tests, `System.Threading.Tasks.Parallel`, `BalanceBook` schema-1 deltas.

**Spec:** `docs/superpowers/specs/2026-11-21-match-simulator-design.md` (approved 2026-11-22; the section "Maestro's answers" binds and overrides any older line). Part 1 for context: `docs/superpowers/specs/2026-11-20-balance-oracle-v2-design.md` and `docs/superpowers/plans/2026-11-20-balance-oracle-v2-plan.md`.

## Deviations from the plan after Task 1

Task 1 (3dfd9c21) deviated from the text below in five places; where a later task assumes the original, follow the code.

1. The equivalence test compares seats by rank (sorted banked) plus total within 1, with the win target lifted in both models.
2. A chicken spawns on the edge of its base zone facing the centre (`MatchSim.Spawn`), not at the base centre.
3. `MatchGeometry` (public static, in `MatchSim.cs`) holds `Rim` and `InReach`.
4. The nav cache key includes a hash of walls and place positions.
5. `MatchSim` thinks at once when a Peck is ready to start; `SeatView.CooldownLeft[0]` is the Peck; casts are refused and counted in `RejectedIntents` until Task 3. Tick phases are completions, think, act; `MatchRules.ThinkSeconds` defaults to 0.25.

Measured cost: 2.45 to 3.13 ms per farm-only match, one thread, batch (`OracleMenu.MatchTimingBatch`).

## Global Constraints

- Simulator code is pure, Fusion-free C# in `Assets/_Game/Scripts/Balance/`, namespace `CluckWars.Balance`, Gameplay assembly (the folder's `CluckWars.Gameplay.asmref`). No `using Fusion`, no MonoBehaviour, no `UnityEngine` beyond `Vector2`, `Mathf` and the ScriptableObject reads in the builders. No upward assembly reference.
- Editor code (menus, batch methods) in `Assets/_Game/Scripts/Editor/`. Tests in `Assets/_Game/Scripts/Editor/Tests/`, namespace `CluckWars.Tests`; asset paths through `TestAssets` / `OracleMapBaker.AssetPath` / `OracleMenu.ReferencePath`.
- `ILogService` only, never `Debug.Log`.
- Every game number is read from the shipped assets through `BalanceBook.Preview` (the `CopyScope` pattern of `OracleKits.cs`), on the main thread, before any parallel work. The simulator holds no game number of its own.
- No test restates an asset value or a map coordinate as a literal: load the asset or the bake and compare, or build synthetic kits and maps whose numbers the test itself chooses.
- Code outside `Assets/_Game/Scripts/Abilities/` never names a concrete `...AbilitySO` type. Dispatch on `AbilityBaseSO.Job` and read declared members and `OracleFacts`. Tests reach authored fields through `SerializedObject` by field name.
- The oracle never blocks (D7): no test asserts a win rate, a band, a target or a seat share. Tests assert rules, determinism and equivalence only.
- Part 1 stays green and unchanged: `OracleReferenceTests` (drift guard `Measured_EqualsTheAcceptedReference`), `OracleSolverTests`, `OracleRunnerTests`, `FarmSimTests`, `OracleMapTests`, `OracleKitsTests` are not edited. `FarmSim.cs` is not edited.
- Targets: kits and classes aim at 25 %, band 20 to 30 % (D5 (b), no bias). Spoiler kill targets (D6, starting values 4 kills no passive at least 0.90, 3 kills no passive at least 0.80, 2 kills with passive at most 0.20) are data on `OracleReference.asset`, edited in the Inspector; they label report rows only.
- The bot sight rule `BotSightRules` (`Assets/_Game/Scripts/Gameplay/BotSightRules.cs`, commit 7a0fc7c5) is called only from `MatchSight.cs`.
- Shared tree: stage by explicit path only. Never `git add -A`, stash, reset, checkout over files, or push. Touch only files the task names. `BUILD_NUMBER` is stamped by the commit hook; leave it alone.
- Commit per task with the trailer `Co-Authored-By: Claude <noreply@anthropic.com>`.
- Tests run in batch with graphics (no `-nographics`) while the Editor is closed: `"$UNITY" -runTests -batchmode -projectPath . -testPlatform EditMode -testFilter "<classes>" -testResults Captures/tests/<task>.xml -logFile Captures/tests/<task>.log`, where `$UNITY` is the Unity 6000.3 Editor executable. Passing means the XML's `<test-run` has `failed="0"` and the log has no `error CS`. If the Editor is open, use `TestRunnerApi` from `script-execute` with a polled result file and check `scriptCompilationFailed` first (`docs/TESTING.md`).
- Every task touches an existing file, so every task ends with the full EditMode suite in batch. The suite was not re-run in full after 7a0fc7c5's last edit (4075 of 4076 before it, the one failure fixed and re-run filtered), so Task 1 takes its own baseline first and every later task compares with it. The known flake `MarkScreenEffectTests.RealRender_DimColdRed...("PC")` is re-run, not fixed.
- Pending docs upkeep from 7a0fc7c5, folded into this plan's first `librarian` pass (after Task 1): `docs/FILE_STRUCTURE.md` gains `Gameplay/BotSightRules.cs` and `Editor/Tests/BotSightTests.cs`; the rules docs gain the `BotSightTests` source guard (one raw read of a rival position in `BotController`, the call that feeds `BotSightRules.Observe`).
- Each implementer's report ends with a "Docs upkeep" line naming added, moved or deleted files and menus. `docs/FILE_STRUCTURE.md` and the rules docs are edited only by `librarian`. The implementer updates the oracle menu table in `docs/TESTING.md` when it adds a menu.
- Plain short English in comments and report text, no em dashes.

## Review Focus

1. Look order at match start: every observer must look at every rival on tick 0, before any Hide can run, or a chicken that hides at once is dropped from every view for the whole Hide (the rule drops a rival hidden before the first look) (Task 3 test `FirstLook_HappensOnTickZero_SoAnEarlyHideIsRemembered`).
2. A pile emptied by a rival in the middle of a cast: the cut is logged with cause `empty-pile`, the cooldown is spent, and the pecker re-plans at once instead of standing still (Task 3 test `PileEmptiedMidCast_CutsWithEmptyPile_AndRethinksAtOnce`).
3. A chicken executed while pecking with intents still queued behind its reaction delay: nothing lands, queued intents are dropped, it respawns at its base with cargo 0 (Task 4 test `Executed_WhilePecking_LandsNothing_AndDropsQueuedIntents`).
4. An executed rival that respawns must be seen at its base, not remembered at the spot where it died (Task 4 test `Respawned_RivalIsSeenAtItsBase`).
5. A policy that throws inside one parallel match must fail the run with the cell index and match seed in the message, never leave a silent hole in the totals (Task 5 test `PolicyThrows_RunFailsNamingCellAndSeed`).

---

### Task 1 (S1): navigation, random streams, the farm-only match and its equivalence with `FarmSim`

**Files:**
- Modify: `Assets/_Game/Scripts/Balance/OraclePaths.cs` (extract the ring-node builder; no behaviour change)
- Modify: `Assets/_Game/Scripts/Editor/OracleMapBaker.cs` (add `ArenaHalf()`)
- Modify: `Assets/_Game/Scripts/Editor/OracleMenu.cs` (add `MatchTimingBatch`)
- Create: `Assets/_Game/Scripts/Balance/OracleNav.cs`, `MatchRng.cs`, `MatchKit.cs`, `MatchPolicy.cs`, `MatchSim.cs`, `FarmOnlyPolicy.cs` (all in `Assets/_Game/Scripts/Balance/`)
- Test: `Assets/_Game/Scripts/Editor/Tests/MatchTestKit.cs` (helpers), `MatchNavTests.cs`, `MatchRngTests.cs`, `MatchSimFarmTests.cs`
- Modify after measuring: the spec, section 5.4 and 7.1 (measured cost per match)

**Interfaces:**
- Consumes (part 1, unchanged): `OracleMapSO` (`Places`, `WallA`, `WallB`, `WallHalfWidth`, `BaseRadius`, `CollectReach`, `SceneFingerprint`, `PersonalIndex`, `ContestedIndex`, `BaseIndex`, `PileCount`, `SeatCount`); `OracleKit`; `OracleFood`; `FarmRules`; `FarmRoute`; `FarmSim.Run(OracleMapSO, OracleFood, FarmSeat[], FarmRules) -> FarmSeatResult[]`; `OracleKits.StandardFarmer(OracleInputs)`, `OracleKits.Food(OracleInputs)`, `OracleKits.Rules(OracleInputs, bool stopAtTarget, FarmFoodEvent)`; `OracleRunner.Routes`; `PeckCastRules.LandingTake(float amount, float freeSpace, float available)`; `PinwheelLayout.ArenaClampLimit(float)`.
- Produces:
  - `OraclePaths`: `internal static List<Vector2> RingNodes(Vector2[] wallA, Vector2[] wallB, float halfWidth, float arenaHalf)`; `internal static bool Clear(Vector2 p, Vector2 q, Vector2[] wallA, Vector2[] wallB, float halfWidth)`. `ShortestPath` keeps its signature and results.
  - `OracleMapBaker.ArenaHalf() -> float`: `PinwheelLayout.ArenaClampLimit(ShippedMap.PlaneSize * 0.5f)`, the value `Bake` uses.
  - `sealed class OracleNav`: `static OracleNav For(OracleMapSO map, float arenaHalf)` (cached by `(map.SceneFingerprint, arenaHalf)`, read-only and thread-safe after build); `Vector2 NextPoint(Vector2 from, Vector2 to)` (`to` itself when the segment is clear); `float PathLength(Vector2 from, Vector2 to)`; `bool IsClear(Vector2 a, Vector2 b)`.
  - `struct MatchRng`: `static MatchRng Pcg32(ulong initState, ulong initSeq)` (the reference `pcg32_srandom`); `static ulong SplitMix64(ref ulong state)`; `static MatchRng Create(ulong matchSeed, ulong stream)`; `static ulong MatchSeed(ulong runSeed, int cell, int replicate)`; `uint NextUInt()`; `float NextFloat()` in [0, 1); `int NextInt(int maxExclusive)`; consts `StreamComeback = 100`, `StreamMark = 101`, `StreamSetup = 102` (seat streams are 0 to 3).
  - `sealed class MatchKit { string Label; ChickenClass Class; OracleKit Farm; }` (Task 2 adds fields).
  - `struct MatchRules { FarmRules Farm; float ArenaHalf; float ThinkSeconds; }` (Task 2 adds fields; `ThinkSeconds` default 0.25).
  - `enum TargetRule : byte { Profile, Nearest, Richest, Leader }`.
  - `struct PolicyParams { float ReactionDelay; float ReturnThreshold; float FleeRadius; float HuntDetour; TargetRule Target; FarmRoute Route; static PolicyParams Default; }` (`Default`: delay 0.3 s, the three floats `NaN` meaning "use the bot profile", `TargetRule.Profile`, `FarmRoute.Nearest`).
  - `struct MatchIntent { Vector2 Goal; int PeckPile; int CastSlot; int CastTarget; static MatchIntent Idle(Vector2 at); }` (-1 means none).
  - `struct SeatView { bool Present; bool Seen; Vector2 Pos; float Cargo; float Bag; float Banked; bool Pecking; bool Stunned; bool Removed; bool Shelled; bool Hidden; float[] CooldownLeft; }`.
  - `sealed class MatchView { int Self; float Time; float TimeLeft; SeatView[] Seats; float[] PileFood; MatchKit[] Kits; OracleMapSO Map; OracleNav Nav; MatchRules Rules; }`.
  - `interface IMatchPolicy { MatchIntent Think(MatchView view, ref MatchRng rng); }`; `delegate IMatchPolicy PolicyFactory(int seat, PolicyParams p);`.
  - `sealed class MatchSetup { OracleMapSO Map; OracleNav Nav; OracleFood Food; MatchRules Rules; MatchKit[] Seats; PolicyFactory[] Policies; PolicyParams[] Params; bool RecordEvents; }` (arrays of 4; a null kit is an empty seat).
  - `enum MatchEventKind : byte { Contact, CastStart, PeckCut, Steal, Kill, Bank }`; `struct MatchEvent { float Time; MatchEventKind Kind; int Seat; int Other; float Amount; CutCause Cause; }`; `enum CutCause : byte { None, Stun, Shove, Execution, EmptyPile, OwnMove }`; `static string MatchEvent.CauseName(CutCause)` returning `MatchTelemetry.CauseStun`, `CauseShove`, `CauseExecution`, `CauseEmptyPile` and `"own-move"`.
  - `sealed class MatchResult { int Winner; float[] Banked; int[] Kills; float EndSeconds; bool ReachedTarget; bool CornerDecided; int RejectedIntents; List<MatchEvent> Events; }`.
  - `static class MatchSim { static MatchResult Run(MatchSetup setup, ulong matchSeed); }`.
  - `sealed class FarmOnlyPolicy : IMatchPolicy { FarmOnlyPolicy(int seat, PolicyParams p); static readonly PolicyFactory Factory; }`.

- [x] **Step 0: Take the baseline**

Run the full EditMode suite in batch before any edit. Record passed, failed and the names of any failures in the report; later tasks compare with this, not with an older count.

- [x] **Step 1: Write the failing tests**

`MatchTestKit` (helpers, not tests): `OracleMapSO OpenMap(float half)` (a synthetic map with 13 places on a square, no walls), `OracleMapSO OneWallMap(float half, Vector2 a, Vector2 b, float halfWidth)`, `MatchKit Farmer(string label, float speed, int capacity, float peckAmount, float peckCooldown, float castSeconds, float depositRate)`, `MatchSetup Setup(OracleMapSO map, MatchKit[] seats, PolicyFactory policy, float arenaHalf)`. Every number is the caller's.

```csharp
// MatchRngTests
[Test] public void Pcg32_MatchesTheReferenceVector()          // Pcg32(42, 54): 0xa15c02b7, 0x7b47f409, 0xba1d3330 (pcg32-demo)
[Test] public void SplitMix64_MatchesTheReferenceVector()      // state 0: first output 0xe220a8397b1dcdaf
[Test] public void Streams_AreIndependent()                    // extra draws on stream 0 leave stream 1's next 8 outputs unchanged
[Test] public void NextFloat_StaysInTheUnitInterval()          // 10 000 draws, all in [0, 1)
[Test] public void MatchSeed_ChangesWithEachInput()            // runSeed, cell, replicate each change the seed

// MatchNavTests
[Test] public void NextPoint_ClearSegment_ReturnsTheTarget()   // OpenMap: NextPoint(a, b) == b
[Test] public void NextPoint_RoundAWall_StepsToAVisibleRingNode() // OneWallMap: next != b, IsClear(a, next)
[Test] public void PathLength_EqualsShortestPath_OnShippedWalls() // every pair of shipped place centres: |nav - OraclePaths.ShortestPath| < 1e-3
[Test] public void For_SameFingerprintAndHalf_ReturnsTheSameInstance()

// MatchSimFarmTests
[TestCaseSource(nameof(Routes))] // OracleRunner.Routes
public void FourStandardFarmers_BankWithinOneFoodOfFarmSim(FarmRoute route)
    // shipped map, OracleKits.StandardFarmer/Food/Rules(inputs, false, FarmFoodEvent.None);
    // FarmSim.Run vs MatchSim.Run with FarmOnlyPolicy, ReactionDelay 0: |banked[s] - farm[s].Banked| <= 1 for each seat
[Test] public void SameSeed_SameResult()                       // two runs: Banked, Winner, EndSeconds and Events equal exactly
[Test] public void ReachingTheTarget_EndsTheMatch()            // synthetic farmer, low WinTarget: ReachedTarget, Winner = that seat, EndSeconds < clock
[Test] public void ReactionDelay_DelaysTheFirstMove()          // delay d: the seat's position is its spawn until d has passed
[Test] public void FarmOnly_NeverHasARejectedIntent()          // RejectedIntents == 0 on the shipped map, every route
```

- [x] **Step 2: Run them and confirm they fail**

Run: the batch command with `-testFilter "CluckWars.Tests.MatchRngTests;CluckWars.Tests.MatchNavTests;CluckWars.Tests.MatchSimFarmTests"`.
Expected: compile errors for the missing types.

- [x] **Step 3: Implement**

- `OraclePaths`: move the ring loop into `RingNodes`; `ShortestPath` calls it. `OracleMapTests` must stay green unchanged.
- `OracleNav`: nodes from `RingNodes`; all-pairs shortest distances and next hops once per build; a per-goal distance field cached for the 13 places, computed on demand for other goals. `NextPoint` picks the node visible from `from` that minimises `|from - node| + field(node)`.
- `MatchSim`: tick `Rules.Farm.TickSeconds`; seat order rotates every tick as in `FarmSim`; the three phases of spec 2.1 (think on the snapshot, resolve, advance). A seat thinks every `ThinkSeconds` (offset by seat so thinks spread) and at once on its own events: arrived, cast landed or cut, deposit done, target pile emptied. An intent takes effect `ReactionDelay` seconds after the think (rounded to ticks). Movement toward `Nav.NextPoint`, the pile slow `Rules.Farm.PileSlowFactor` inside `CollectReach` of a pile unless `IgnoresPileSlow`. Peck: a cast of `PeckCastSeconds` at a pile in reach, take `PeckCastRules.LandingTake`, next cast after `max(PeckCooldown, PeckCastSeconds)`. Deposit at `DepositRate` inside `BaseRadius` of the own base. The match ends at `WinTarget` banked or at the clock; at the clock the winner is most banked, then most kills, then the lowest corner (`CornerDecided` set when the corner decided). Illegal intents are refused and counted in `RejectedIntents`.
- `FarmOnlyPolicy`: the decisions of `FarmSim` (route choice, return when due, redirect when the target pile empties, the time-is-short rule) on continuous positions, with costs from `Nav.PathLength` minus reach as the bake defines legs. Do not edit `FarmSim.cs`; the equivalence test is the guard against drift.
- `OracleMenu.MatchTimingBatch()`: runs 1 000 farm-only matches on the shipped inputs on one thread and logs milliseconds per match through `ILogService`.

- [x] **Step 4: Run the tests and confirm they pass**

Same command. Expected: `failed="0"`. Then the full EditMode suite in batch: baseline plus the new tests, 0 failed.

- [x] **Step 5: Measure and correct the spec**

Run `"$UNITY" -batchmode -quit -projectPath . -executeMethod CluckWars.EditorTools.OracleMenu.MatchTimingBatch -logFile Captures/tests/timing.log`. Write the measured ms per match into the spec's 5.4 table and the runtime line of 7.1. If one match costs more than 5 ms, stop and report before Task 2; do not cut ring vertices silently (the equivalence test pins them).

- [x] **Step 6: Commit**

```bash
git add Assets/_Game/Scripts/Balance/OraclePaths.cs Assets/_Game/Scripts/Editor/OracleMapBaker.cs Assets/_Game/Scripts/Editor/OracleMenu.cs Assets/_Game/Scripts/Balance/OracleNav.cs Assets/_Game/Scripts/Balance/OracleNav.cs.meta Assets/_Game/Scripts/Balance/MatchRng.cs Assets/_Game/Scripts/Balance/MatchRng.cs.meta Assets/_Game/Scripts/Balance/MatchKit.cs Assets/_Game/Scripts/Balance/MatchKit.cs.meta Assets/_Game/Scripts/Balance/MatchPolicy.cs Assets/_Game/Scripts/Balance/MatchPolicy.cs.meta Assets/_Game/Scripts/Balance/MatchSim.cs Assets/_Game/Scripts/Balance/MatchSim.cs.meta Assets/_Game/Scripts/Balance/FarmOnlyPolicy.cs Assets/_Game/Scripts/Balance/FarmOnlyPolicy.cs.meta Assets/_Game/Scripts/Editor/Tests/MatchTestKit.cs Assets/_Game/Scripts/Editor/Tests/MatchTestKit.cs.meta Assets/_Game/Scripts/Editor/Tests/MatchNavTests.cs Assets/_Game/Scripts/Editor/Tests/MatchNavTests.cs.meta Assets/_Game/Scripts/Editor/Tests/MatchRngTests.cs Assets/_Game/Scripts/Editor/Tests/MatchRngTests.cs.meta Assets/_Game/Scripts/Editor/Tests/MatchSimFarmTests.cs Assets/_Game/Scripts/Editor/Tests/MatchSimFarmTests.cs.meta docs/superpowers/specs/2026-11-21-match-simulator-design.md
git commit -m "feat(balance): match simulator core, navigation and farm equivalence (BL-207 S1)" -m "Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 2 (S2): declared facts, named constants and the kit builder

**Files:**
- Modify: `Assets/_Game/Scripts/Abilities/AbilityOracleFacts.cs` (eight fields)
- Modify: `Assets/_Game/Scripts/Abilities/AbilityBaseSO.cs` (`AimRadiusField`, `CooldownFields`)
- Modify: `Assets/_Game/Scripts/Abilities/ShadowstepAbilitySO.cs`, `TurtleModeAbilitySO.cs`, `MarkKillAbilitySO.cs`, `InvisibilityAbilitySO.cs` (facts); `SnatchAbilitySO.cs`, `WingSlamAbilitySO.cs`, `CluckShockAbilitySO.cs`, `SlowAbilitySO.cs`, `FeatherAuraAbilitySO.cs` (`AimRadiusField`); `MarkKillAbilitySO.cs` (`CooldownFields`)
- Modify: `Assets/_Game/Scripts/Gameplay/ChickenMovement.cs:38,82` (two public named constants; no behaviour change)
- Modify: `Assets/_Game/Scripts/Gameplay/BotController.cs:73` (read-only `ArrivalRadius` over `_arrivalRadius`; no behaviour change)
- Modify: `Assets/_Game/Scripts/Editor/OracleMenu.cs` (`BotArrivalRadius()`)
- Modify: `Assets/_Game/Scripts/Balance/MatchKit.cs`; Create: `Assets/_Game/Scripts/Balance/MatchKits.cs`
- Test: `Assets/_Game/Scripts/Editor/Tests/MatchFactsTests.cs`, `MatchKitsTests.cs`

**Interfaces:**
- Consumes: Task 1's `MatchKit`, `MatchRules`; `OracleInputs`; `OracleKits.All(OracleInputs)` (labels and `OracleKit` per kit); `FixedKit.TryResolve`; `PassiveAbilitySO` hooks `ModifyCooldown`, `ModifyControlDuration(float, ControlKind, ChickenController)`, `ModifyIncomingSlowFactor(float, ChickenController)`, `OutgoingControlMultiplier`, `ModifyKnockback(float, ChickenController)`, `ModifyStealAmount(float, ChickenController)`, `MaxStealMultiplier`, `MatchEndBonusFood`; `AbilityBaseSO` members `Job`, `Cooldown`, `Duration`, `RoundStartCooldown`, `AimRadius`, `NominalStealAmount`, `EmittedControl`, `EmittedKnockback`, `TryGetAreaEffect`, `GrantsStealProof`, `EndsOnNextMove`, `FollowUpPhaseSeconds`, `OracleFacts`, `AuthoredAimForwardOffset`; `BotTactics.ProfileFor`, `IsStealer`, `IsStealShield`; `ControlEmission`.
- Produces:
  - `AbilityOracleFacts` new fields: `HideOpacity` (Hide: the authored `Opacity`), `BlinkMetres` (Jump: `AuthoredAimForwardOffset`), `ShieldWalkSeconds`, `ShieldWalkSpeedMultiplier`, `ShieldEggSeconds` (Shield: `WalkSeconds`, `SpeedMultiplier`, `EggSeconds`), `MarkArmSeconds`, `MarkKillRange`, `MarkFailCooldown`, `MarkLeaderPickWeight` (Death Mark: `ArmSeconds`, `KillRange`, `FailCooldownSeconds`, `LeaderPickWeight`). Declared by the SO that owns them.
  - `AbilityBaseSO`: `public virtual string AimRadiusField => null;` (the five area abilities return the field behind their `AimRadius`: `SnatchRange`, `IndicatorRange`, `ShockRadius`, `Radius`, `AuraRadius`); `public virtual string[] CooldownFields => new[] { nameof(Cooldown) };` (Death Mark: `SuccessCooldownSeconds`, `FailCooldownSeconds`).
  - `ChickenMovement.KnockbackDecayRate` becomes `public const float`; `public const float UnderdogSurgeSpeedFactor` replaces the literal at line 82.
  - `struct MatchAbility { AbilityJob Job; string Asset; float Cooldown; float RoundStartCooldown; float Duration; float Radius; ControlEmission Control; float Knockback; float StealAmount; bool AreaAura; float AreaRadius; float AreaSlowFactor; float SpeedMultiplier; float BlinkMetres; float ShieldWalkSeconds; float ShieldWalkSpeedMultiplier; float ShieldEggSeconds; bool StealProof; bool EndsOnNextMove; float MarkArmSeconds; float MarkWindowSeconds; float MarkKillRange; float MarkSuccessCooldown; float MarkFailCooldown; float MarkRemovalSeconds; float MarkRoundStartCooldown; float MarkLeaderPickWeight; BotRole Role; AbilityCategory Category; bool IsStealer; bool IsStealShield; }` (`Cooldown` already through the owner's `ModifyCooldown`).
  - `struct MatchPassive { string Asset; float StunInFactor; float SlowInDurationFactor; float OutgoingControlFactor; float KnockbackFactor; float StealFactor; float MaxStealMultiplier; float MatchEndBonusFood; float[] SlowFactorsEmitted; float[] SlowFactorsReceived; float ReceivedSlowFactor(float emitted); }` (`ReceivedSlowFactor` throws on a factor no kit emits).
  - `MatchKit` gains `MatchAbility[] Abilities` (four slots, fixed-kit order), `MatchPassive Passive`, `BotProfile Bot`.
  - `MatchAbility` also carries `float HideOpacity`.
  - `BotController.ArrivalRadius` (`public float`, read-only); `OracleMenu.BotArrivalRadius() -> float` reads it from the prefab that carries `BotController`.
  - `MatchRules` gains `float KeepOutRadius; float ExecuteBounty; float KnockbackDecayRate; float SurgeSpeedFactor; float SightArrivalRadius;`.
  - `static class MatchKits { static MatchKit[] All(OracleInputs inputs); static MatchRules Rules(OracleInputs inputs, float arenaHalf, float sightArrivalRadius); }` (same kits, order and labels as `OracleKits.All`).

- [ ] **Step 1: Write the failing tests**

```csharp
// MatchFactsTests: every registry ability, found by Job, read through SerializedObject by field name
[Test] public void JumpFacts_BlinkMetres_EqualsTheAuthoredForwardOffset()
[Test] public void ShieldFacts_EqualTheAuthoredWalkAndEggFields()       // WalkSeconds, SpeedMultiplier, EggSeconds
[Test] public void DeathMarkFacts_EqualTheAuthoredFields()              // ArmSeconds, KillRange, FailCooldownSeconds, LeaderPickWeight
[Test] public void AimRadiusField_NamesTheFieldBehindAimRadius()        // non-null field: property float == AimRadius
[Test] public void CooldownFields_AreSerializedFloats()                 // each name resolves to a float property
[Test] public void ChickenMovement_HasNoSurgeLiteral()                  // source text of ChickenMovement.cs holds no "1.4f"
[Test] public void HideFacts_HideOpacity_EqualsTheAuthoredOpacity_AndCountsAsHidden() // == "Opacity" field; BotSightRules.IsHidden(it) is true
[Test] public void BotArrivalRadius_EqualsThePrefabField()              // SerializedObject of the bot prefab's BotController, "_arrivalRadius"

// MatchKitsTests: shipped assets via TestAssets, no literals
[Test] public void All_MatchesOracleKits_LabelsAndFarmNumbers()         // same count, order, labels; Farm equals OracleKits kit field by field
[Test] public void AbilityCooldown_PassesThroughThePassive()            // == passive.ModifyCooldown(asset.Cooldown, asset, null)
[Test] public void Verdict_ChangesAbilityCooldownAndRadius()            // a schema-1 delta on one Stun ability's Cooldown and radius field changes MatchAbility.Cooldown and Radius
[Test] public void UnknownJob_Throws_NamingKitAndJob()                  // a verdict kit holding the Bank-job ability: InvalidOperationException, message has the kit label and "Bank"
[Test] public void SyntheticPassive_ChangesOnlyItsQuantity()            // a test-only PassiveAbilitySO subclass with ModifyKnockback x k: KnockbackFactor == k, every other factor == 1
[Test] public void NonLinearHook_Throws_NamingThePassive()              // test passive whose ModifyControlDuration(2s) != 2 x ModifyControlDuration(1s)
```

- [ ] **Step 2: Run them and confirm they fail**

`-testFilter "CluckWars.Tests.MatchFactsTests;CluckWars.Tests.MatchKitsTests"`. Expected: compile errors.

- [ ] **Step 3: Implement**

`MatchKits.All` reuses the `OracleKits` inputs and copy scope (one `CopyScope` per build, disposed at the end; no SO is read after it returns). Dispatch on `Job`; a job the simulator does not model (`Bank`, `None`) throws. Passive factors are read by calling each hook with a unit input and a null owner, and checked linear by a second call at 2; a non-linear hook throws naming the passive. `SlowFactorsEmitted` lists every slow factor the eight kits emit; `SlowFactorsReceived` holds the hook's answer for each.

- [ ] **Step 4: Run the tests, then the full suite**

Expected: the new classes pass; the full EditMode suite has 0 failed (the game's behaviour does not change).

- [ ] **Step 5: Commit**

```bash
git add <every file listed under Files, with the .meta of each new file>
git commit -m "feat(balance): declared oracle facts, named movement constants and the match kit builder (BL-207 S2)" -m "Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 3 (S3): combat rules, Hide through the game's sight rule, comeback events, tie-break

**Files:**
- Create: `Assets/_Game/Scripts/Balance/MatchSight.cs`, `Assets/_Game/Scripts/Balance/MatchCombat.cs`
- Modify: `Assets/_Game/Scripts/Balance/MatchSim.cs`, `MatchPolicy.cs`, `MatchKit.cs`
- Modify only if a comeback number is a literal today: `Assets/_Game/Scripts/Gameplay/GameManager.cs` (make it a public named constant, no behaviour change)
- Test: `Assets/_Game/Scripts/Editor/Tests/MatchCombatTests.cs`, `MatchSightTests.cs`, plus helpers in `MatchTestKit.cs` (`PolicyFactory CastAt(float time, int slot, int target)`, `MatchKit Fighter(...)` with synthetic abilities)
- Modify after checking: the spec, section 2.2 (arm-wall result)

**Interfaces:**
- Consumes: Tasks 1 and 2; `PeckCastRules.IsCut(bool stunned, bool knockedSinceLastTick, bool targetGone)`; `StealRules.Stealable(float cargo, float bountyBag)`, `StealRules.SplitDrain(float amount, float cargo, float bountyBag, out float fromCargo, out float fromBag)`; `BaseWallRules.Blocks(Vector3 a, Vector3 b, Vector2 center, float radius)`; `GameManager` comeback constants (`GoldenPileFood`, `RestockFood`, the Golden Pile ring and the event odds and timing); `BotSightRules` (commit 7a0fc7c5): `readonly struct RivalSighting { Vector3 Position; bool Hidden; bool GivenUp; float HiddenSince; }`, `static bool IsHidden(float visualOpacity)`, `static RivalSighting Observe(RivalSighting? previous, Vector3 actualPosition, float visualOpacity, Vector3 selfPosition, float now, float arrivalRadius)`, `const float GiveUpSeconds`. Read the file first; if its signature has changed since, adapt `MatchSight` only.
- Produces:
  - `static class MatchSight { static RivalSighting Observe(RivalSighting? previous, Vector2 truePos, float opacity, Vector2 self, float now, float arrivalRadius); }` (planar XZ in, `Vector3(x, 0, y)` to the rule; the only caller of `BotSightRules`). A chicken's opacity is its Hide ability's `HideOpacity` while Hide runs, else 1.
  - `SeatView` gains `bool Known` (false when the observer has given the rival up; `Pos` is then meaningless); `Seen` means visible now.
  - `static class MatchCombat`: `static bool InRange(Vector2 caster, Vector2 target, float radius)`; `static bool KeepOutBlocks(Vector2 caster, Vector2 target, Vector2[] baseCentres, float keepOutRadius)`; `static float ControlSeconds(ControlEmission e, MatchPassive caster, MatchPassive victim)`; `static float ShoveTravel(float knockback, float victimFactor, float decayRate)`.
  - `MatchRules` gains `ComebackRules Comeback` with `struct ComebackRules { float GoldenPileFood; float RestockFood; float GoldenRingMin; float GoldenRingMax; /* plus the kinds, odds and timing GameManager uses */ }`.
  - `struct SeatCounters { float Foraged; float Stolen; float LostToSteals; float Bounty; int Casts; int Steals; float FirstContact; float[] StolenFromSeat; int[] CutsByCause; int[] ExposedByCause; int[] CutWhileExposedByCause; }` (cause arrays indexed by `CutCause`; "exposed" = a rival with a ready cutting ability of that cause in range when the cast starts); `MatchResult` gains `SeatCounters[] Counters`.

- [ ] **Step 1: Check the arm-wall assumption**

Read how a cast picks its targets (`AbilityTargeting.cs`, the cast paths in `AbilityController.cs`): do the pinwheel arms block an ability? Write the answer with file and line into the spec's 2.2. If arms block, `MatchCombat.InRange` also requires `Nav.IsClear(caster, target)` and a test pins it.

- [ ] **Step 2: Write the failing tests**

Synthetic open map and synthetic kits; every number is the test's own.

```csharp
// MatchCombatTests
[Test] public void Stun_AtRadiusMinusEpsilon_Lands_AtRadiusPlusEpsilon_DoesNot()
[Test] public void Stun_MidCast_LandsNoFood_SpendsTheCooldown()          // cargo unchanged, Peck cooldown running, event PeckCut cause Stun
[Test] public void Slow_MidCast_DoesNotCut()
[Test] public void Stun_OnTheLandingTick_LetsTheFoodLand()
[Test] public void Shove_MidCast_Cuts_AndTravelsKnockbackOverDecay()      // travel within 5 % of ShoveTravel(...)
[Test] public void Shove_VictimFactor_ScalesTheTravel()
[Test] public void Steal_TakesFromCargoThenBag_CappedByFreeRoom()        // equals StealRules.SplitDrain with the thief's free room
[Test] public void Egg_RefusesStunShoveStealAndExecute()
[Test] public void NoAbility_CrossesTheKeepOutPair()
[Test] public void SlowArea_SlowsRivalsInsideEachTick_UntilItsDuration()
[Test] public void Speed_MultipliesMoveForItsSeconds()
[Test] public void Jump_CrossingAWall_CostsOneJump_AndWaitsForTheCooldown() // OneWallMap
[Test] public void Control_DurationPassesThroughCasterThenVictim()       // == ControlSeconds(e, caster, victim)
[Test] public void PileEmptiedMidCast_CutsWithEmptyPile_AndRethinksAtOnce() // Review Focus 2
[Test] public void Hide_EndsOnOwnMove_NotOnAutoPeck()
[Test] public void TimerTieBreak_MostBanked_ThenKills_ThenLowestCorner() // three set-ups, CornerDecided only in the third
[Test] public void ComebackDraw_UsesOnlyTheComebackStream()             // extra draws by a seat policy leave the event kind and spot unchanged
[Test] public void GoldenPile_SpotIsOnTheRing()                         // distance from centre in [GoldenRingMin, GoldenRingMax]
[Test] public void CutEvents_UseTheTelemetryCauseNames()                // CauseName(Stun) == MatchTelemetry.CauseStun, and the other three
[Test] public void ExposedCast_CountsOncePerCause()                     // a stunner in range at cast start: ExposedByCause[Stun] == 1

// MatchSightTests
[Test] public void HiddenRival_IsSeenAtItsLastSeenPosition()            // view.Seats[r].Pos == position at the moment it hid, while it walks away
[Test] public void HiddenRival_IsDropped_WhenTheObserverReachesTheSpot() // Known false once within SightArrivalRadius of the remembered spot
[Test] public void HiddenRival_IsDropped_AfterGiveUpSeconds()           // Known false at HiddenSince + BotSightRules.GiveUpSeconds
[Test] public void HideEnds_RivalIsSeenAtItsTruePosition()             // Seen and Known again
[Test] public void FirstLook_HappensOnTickZero_SoAnEarlyHideIsRemembered() // Review Focus 1: Hide cast on the first think, rivals keep its spawn
[Test] public void Policy_NeverReceivesAHiddenRivalsTruePosition()      // over a whole match, every view of a hidden rival equals the rule's Position
```

- [ ] **Step 3: Run them and confirm they fail**

`-testFilter "CluckWars.Tests.MatchCombatTests;CluckWars.Tests.MatchSightTests"`. Expected: compile errors or failures.

- [ ] **Step 4: Implement**

Casts resolve against the tick's snapshot, all at once (two stuns on one tick both land). Abilities follow the spec's 3.2 table. A cast is legal only when its slot is ready and the caster is not stunned, removed or shelled; the sim refuses anything else. A `RivalSighting?` is kept per (observer, target) pair and updated each tick by `MatchSight.Observe`, starting with a look on tick 0 before any cast resolves. The comeback event uses only `MatchRng.Create(seed, StreamComeback)`. Shove travel is applied as a displacement that decays by `KnockbackDecayRate` per second, on top of the victim's own steering, clamped by walls.

- [ ] **Step 5: Run the tests, then the full suite**

Expected: the new classes pass; Task 1 and 2 classes still pass; the full suite 0 failed.

- [ ] **Step 6: Commit**

```bash
git add <every file listed under Files that changed, with .meta for new files>
git commit -m "feat(balance): match combat rules, sight through the bot rule, comeback events and tie-break (BL-207 S3)" -m "Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 4 (S4): Spoiler and Thief, forced kills, kill targets as data

**Files:**
- Modify: `Assets/_Game/Scripts/Balance/MatchSim.cs`, `MatchPolicy.cs` (`SeatView` gains mark fields)
- Modify: `Assets/_Game/Scripts/Balance/OracleReferenceSO.cs` (add the kill target list; `Entries` and `Offsets()` untouched)
- Modify: `Assets/_Game/Data/Balance/OracleReference.asset` (the three D6 starting rows, set in the Inspector or by `SerializedObject`; nothing else in the asset changes)
- Create: `Assets/_Game/Scripts/Balance/MatchStats.cs`
- Test: `Assets/_Game/Scripts/Editor/Tests/MatchSpoilerTests.cs`, `MatchStatsTests.cs`

**Interfaces:**
- Consumes: Tasks 1 to 3; `MarkPickRules.HomeFraction`, `AssassinFraction`, `LeaderWeight(float banked, float bestBanked, float leaderWeight)`, `PickWeighted(...)`, `Filter<T>(...)`; `MatchRules.ExecuteBounty`; the Death Mark numbers of `MatchAbility`.
- Produces:
  - `enum ForcedVictims : byte { Empty, Loaded }`; `struct ForcedKills { int Spoiler; int Kills; ForcedVictims Victims; float[] Times; }` (`Kills == 0` and `Spoiler == -1` means off); `MatchSetup` gains `ForcedKills Forced`.
  - `SeatView` gains `int MarkedBy; int MarkTarget; float ArmLeft; float WindowLeft;`.
  - `MatchResult` gains `int WinnerWithoutSpoilerBonus; bool NobodyReachedTarget;`.
  - `[Serializable] struct SpoilerKillTarget { int Kills; bool WithPassive; float MinWinRate; float MaxWinRate; }`; `OracleReferenceSO.KillTargets` (`List<SpoilerKillTarget>`, empty by default in code; the asset ships the D6 rows: 4 / false / 0.90 / 1, 3 / false / 0.80 / 1, 2 / true / 0 / 0.20).
  - `struct WinInterval { int Wins; int Trials; float Rate; float Low; float High; static WinInterval Wilson(int wins, int trials, float z = 1.96f); }`.
  - `sealed class SpoilerKillRow { int Kills; ForcedVictims Victims; WinInterval Win; WinInterval WinWithPassive; float MeanFinal; float MeanBestRival; float NobodyReachedTarget; string Label; }` (`Label` null when the reference holds no target for that row).
  - `static class MatchStats { static float KendallTau(IReadOnlyList<float> a, IReadOnlyList<float> b); static SpoilerKillRow KillsToWin(IReadOnlyList<MatchResult> results, int spoilerSeat, int kills, ForcedVictims victims, IReadOnlyList<SpoilerKillTarget> targets); }`.

- [ ] **Step 1: Write the failing tests**

```csharp
// MatchSpoilerTests (synthetic kits; Death Mark numbers chosen by the test)
[Test] public void Mark_PicksThroughMarkPickRules_WithTheMarkStream()   // same pick as PickWeighted with the roll from Create(seed, StreamMark)
[Test] public void Kill_BeforeTheArmEnds_DoesNotLand()
[Test] public void Kill_NeedsStunnedAndInRange_InsideTheWindow()        // three negatives (not stunned, out of range, window over) and one positive
[Test] public void Kill_PaysCargoPlusBountyIntoTheBag_AndRemovesTheVictim()
[Test] public void Kill_SetsTheSuccessCooldown_AFailSetsTheFailCooldown()
[Test] public void Executed_WhilePecking_LandsNothing_AndDropsQueuedIntents() // Review Focus 3
[Test] public void Respawned_RivalIsSeenAtItsBase()                     // Review Focus 4
[Test] public void SpoilerBonus_IsPaidBeforeTheWinnerIsPicked()          // Winner differs from WinnerWithoutSpoilerBonus in the set-up built for it
[Test] public void Bag_IsBankedAtTheBase_AndIsStealable()
[Test] public void Thief_StealIsScaled_AndCappedByMaxStealMultiplier()
[TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4)]
public void ForcedKills_GrantExactlyK_OnSchedule(int k)                 // Kills[spoiler] == k, one Kill event per scheduled time
[Test] public void ForcedKills_LoadedVictims_PickTheRichestRival()

// MatchStatsTests
[Test] public void Wilson_MatchesTheTextbookInterval()                  // 10 of 100: Low 0.0552, High 0.1744 (+-1e-3)
[Test] public void Wilson_ZeroTrials_IsTheFullInterval()                // Rate NaN, Low 0, High 1
[Test] public void KendallTau_MatchesHandCountedPairs()                 // [1,2,3,4,5] vs [3,4,1,2,5]: 0.2; reversed: -1
[Test] public void KillsToWin_WithNoTarget_HasNoLabel()
[Test] public void KillsToWin_WithATarget_LabelsInOrOut()               // synthetic target list
[Test] public void ReferenceAsset_KillTargets_AreWellFormed()          // each row: 0 <= Min <= Max <= 1, Kills in 0..4, no two rows for the same (Kills, WithPassive); no value restated
[Test] public void ReferenceAsset_EntriesUnchanged_ByTheNewList()       // Entries and Offsets() equal before and after a load round trip
```

- [ ] **Step 2: Run them and confirm they fail**

`-testFilter "CluckWars.Tests.MatchSpoilerTests;CluckWars.Tests.MatchStatsTests"`. Expected: compile errors.

- [ ] **Step 3: Implement**

The mark flow of spec 3.2 (press, arm, window, Kill; success and fail cooldowns; removal for `MarkRemovalSeconds`, respawn at base with cargo 0; victim cargo plus `ExecuteBounty` into the Spoiler's bag). Forced kills bypass the mark: at each scheduled time the sim executes the chosen victim (`Empty`: least cargo, `Loaded`: most cargo) with the same payout. The Spoiler bonus (`MatchEndBonusFood`) is added before the winner is picked; `WinnerWithoutSpoilerBonus` is the same pick without it.

- [ ] **Step 4: Run the tests, then the full suite**

Expected: new classes pass; `OracleReferenceTests` green and unchanged; full suite 0 failed.

- [ ] **Step 5: Commit**

```bash
git add <every file listed under Files, with .meta for new files>
git commit -m "feat(balance): Spoiler mark and kill, Thief, forced kills and kill targets as data (BL-207 S4)" -m "Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 5 (S5): P0, the runner, the designs and the first report

**Files:**
- Create: `Assets/_Game/Scripts/Balance/ScriptedPolicy.cs`, `MatchRunner.cs`, `MatchReport.cs`
- Modify: `Assets/_Game/Scripts/Editor/OracleMenu.cs` (menu `Cluck Wars/Balance/Oracle/Match report`, `MatchReportBatch`)
- Modify: `docs/TESTING.md` (oracle menu table: one row)
- Test: `Assets/_Game/Scripts/Editor/Tests/ScriptedPolicyTests.cs`, `MatchRunnerTests.cs`, `MatchReportTests.cs`

**Interfaces:**
- Consumes: Tasks 1 to 4; `BotTactics.MustBankNow(float cargo, float distToBase, float moveSpeed, float timeRemaining, float depositSeconds)`, `ReturnThreshold(in BotProfile, MatchPhase, bool leading)`, `ResolvePhase(float timeRemaining, float matchDuration)`, `NextMarkStep(bool marked, bool canKill, float armRemaining, float windowRemaining, bool stunInReach, float stunSeconds)`, `CastPlan(BotStrategy, BotSituation, bool targetIsLoaded)`, `PileCost(float distToPile, float pileToBase, float available, float roundTripBias)`, `TargetPriority(...)`, `ShouldStealGuard(float ownCargo, float nearestStealerDist, bool alreadyProof)`, `ScoreCastCandidate(int rolePriorityIndex, float cooldownSeconds)`; `BotProfile` fields (`ReturnThreshold`, `ProtectCargoThreshold`, `DangerRadius`, `HuntRadius`, `HuntCargoThreshold`, `EngageRadius`).
- Produces:
  - `sealed class ScriptedPolicy : IMatchPolicy { ScriptedPolicy(int seat, PolicyParams p); static readonly PolicyFactory Factory; }` (a `PolicyParams` value that is not `NaN` overrides the bot profile's).
  - `enum MatchDesignKind : byte { Mixed, OnePerClass, SeatCheck, ForcedKills }`; `enum PolicyPopulation : byte { P0 }` (Task 6 adds `P1`, `Mixed`).
  - `sealed class MatchCell { int Index; int[] KitOfSeat; int Replicate; ForcedKills Forced; }`.
  - `static class MatchDesigns { static List<MatchCell> Cells(MatchDesignKind design, MatchKit[] kits, int replicates); }`.
  - `sealed class MatchContext { OracleMapSO Map; OracleNav Nav; OracleFood Food; MatchRules Rules; MatchKit[] Kits; string BalanceHash; static MatchContext From(OracleInputs inputs, float arenaHalf, float sightArrivalRadius); }`.
  - `sealed class MatchRunSpec { MatchDesignKind Design; int Replicates; PolicyPopulation Population; ulong RunSeed; int Threads; PolicyParams Params = PolicyParams.Default; int CellLimit; }` (`CellLimit` 0 = all).
  - `sealed class MatchRunOutput { MatchRunSpec Spec; MatchCell[] Cells; MatchResult[] Results; }`.
  - `static class MatchRunner { static MatchRunOutput Run(MatchContext context, MatchRunSpec spec); }`.
  - `enum KitCall : byte { InBand, Above, Below, PolicySensitive, NoCall }`.
  - `sealed class MatchReportSection { string BalanceHash; string MapFingerprint; ulong RunSeed; List<KitWinLine> Kits; List<ClassWinLine> Classes; List<HeadToHeadLine> HeadToHead; List<SpoilerKillRow> KillsToWin; List<ThiefLine> Thief; List<PeckCutLine> PeckCuts; ContactLine FirstContact; List<EconomyShareLine> Economy; int CornerDecided; List<SeatLine> SeatCheck; List<DriftLine> Drift; List<string> LeftOut; string ToMarkdown(); string ToJson(); }` with line types `KitWinLine { string Label; WinInterval Win; KitCall Call; string Reason; }`, `ClassWinLine { ChickenClass Class; WinInterval Headline; WinInterval Pooled; }`, `HeadToHeadLine { ChickenClass Class; string KitA; string KitB; float AAhead; }`, `ThiefLine { string Victim; float StolenPerMatch; float Share; }`, `PeckCutLine { ChickenClass Class; CutCause Cause; int Exposed; int Cut; float Hazard; float FoodLostPerMatch; }`, `ContactLine { float Median; float Q1; float Q3; }`, `EconomyShareLine { string Label; float Foraged; float Stolen; float Bounty; }`, `SeatLine { int Seat; WinInterval Win; bool Flagged; }`, `DriftLine { string Label; float Points; }` (Drift stays empty until Task 7).
  - `static class MatchReports { const float Target = 0.25f; const float BandLow = 0.20f; const float BandHigh = 0.30f; static readonly string[] LeftOut; static MatchReportSection Build(MatchContext context, IReadOnlyList<MatchRunOutput> runs, OracleReferenceSO reference); }` (`LeftOut` is the "left out" column of spec 2.4).

- [ ] **Step 1: Write the failing tests**

```csharp
// ScriptedPolicyTests (synthetic kits and map)
[Test] public void MustBankNow_SendsTheChickenHome()                     // cargo, clock and distance set so BotTactics.MustBankNow is true
[Test] public void StealGuard_ShellsUpWhenAStealerIsInRange()
[Test] public void MarkFollowThrough_StunsThenKills()                    // the steps of BotTactics.NextMarkStep, in order
[Test] public void FullBag_GoesHome()
[Test] public void Flee_AboveProtectThreshold_WithARivalInDanger()
[Test] public void Collect_PicksTheCheapestPileByPileCost()
[Test] public void ParamsNotNaN_OverrideTheProfile()                    // ReturnThreshold param changes when it goes home
[Test] public void P0_NeverHasARejectedIntent()                          // shipped kits, Mixed, CellLimit 20: every RejectedIntents == 0

// MatchRunnerTests (no rate is asserted)
[Test] public void Mixed_Has70x6xRCells_EachKitIn35x6xR()
[Test] public void OnePerClass_Has16x6xRCells_OneKitPerClass()
[Test] public void Seatings_AreTheSixDistinctUpToRotation()
[Test] public void SameSpec_SameResults()
[Test] public void OneThreadAndTwelveThreads_GiveTheSameResults()        // CellLimit 60
[Test] public void EveryMatch_HasExactlyOneWinner()                     // sum of wins over kits == matches
[Test] public void PolicyThrows_RunFailsNamingCellAndSeed()              // Review Focus 5: a test policy that throws in one cell

// MatchReportTests
[Test] public void Report_WithOnlyP0_GivesNoCall()                       // every KitWinLine.Call == NoCall, Reason names the missing populations (D4)
[Test] public void Report_ListsTheLeftOutColumn()                        // LeftOut equals MatchReports.LeftOut
[Test] public void KillsToWin_PrintsRawRatesWithoutLabels_WhenNoTargets()
[Test] public void KillsToWin_LabelsRowsFromTheReferenceAsset()         // each row's Label is non-null exactly when a KillTargets row of the loaded asset covers it
[Test] public void SeatCheck_OutsideTheInterval_IsFlaggedNotThrown()     // synthetic results built by hand
[Test] public void PeckCutHazard_IsCutOverExposed()
```

- [ ] **Step 2: Run them and confirm they fail**

`-testFilter "CluckWars.Tests.ScriptedPolicyTests;CluckWars.Tests.MatchRunnerTests;CluckWars.Tests.MatchReportTests"`. Expected: compile errors.

- [ ] **Step 3: Implement**

- `ScriptedPolicy`: the priority list of spec 4.1, each step calling the `BotTactics` function named there; it reads rivals only from `MatchView` (so Hide counts through `MatchSight`).
- `MatchDesigns`: cells in a fixed order (design, kit set lexicographic, seating, replicate). Forced kills: the Spoiler kit with each One-per-class set that holds it, 6 seatings, k = 0 to 4, both victim modes, R replicates.
- `MatchRunner`: kits come from the `MatchContext` built on the main thread; `Parallel.For` over cell indices with `MaxDegreeOfParallelism = spec.Threads`; each match seed is `MatchRng.MatchSeed(RunSeed, cell, replicate)`; results stored by index. An exception in a match is rethrown as `InvalidOperationException` naming the cell index and the seed.
- `MatchReports.Build`: section 6 of the spec. With one population no kit gets a call (D4).
- Menu: `MatchReportBatch` builds the context with `OracleMapBaker.ArenaHalf()` and `OracleMenu.BotArrivalRadius()`, runs Mixed R 10, One per class R 25, Seat check R 25 and Forced kills R 5 under P0, writes `Captures/oracle/match-report-<UTC>.md` and `.json`, and reads optional `-oracleSeed` and `-oracleThreads` (default 12). Add the row to the oracle menu table in `docs/TESTING.md`.

- [ ] **Step 4: Run the tests, the full suite and the menu**

New classes pass; full suite 0 failed. Then `"$UNITY" -batchmode -quit -projectPath . -executeMethod CluckWars.EditorTools.OracleMenu.MatchReportBatch -logFile Captures/tests/match-report.log`. Expected: both report files exist, the kit table has 8 rows, the class table 4, the kills-to-win table 10 rows with the D6 labels on the rows they cover, and the run time is logged. Paste the run time into the report.

- [ ] **Step 5: Commit**

```bash
git add <every file listed under Files, with .meta for new files>
git commit -m "feat(balance): scripted policy P0, match runner, designs and the match report (BL-207 S5)" -m "Co-Authored-By: Claude <noreply@anthropic.com>"
```

After review: the orchestrator updates `docs/STATE.md` and has `librarian` bring `docs/FILE_STRUCTURE.md` and the rules docs in line for Tasks 1 to 5.

---

### Task 6 (S6): P1, the mixed population, the hypercube sweep and robustness tags

**Files:**
- Create: `Assets/_Game/Scripts/Balance/UtilityPolicy.cs`, `MatchSweep.cs`
- Modify: `Assets/_Game/Scripts/Balance/MatchRunner.cs` (populations), `MatchReport.cs` (calls and tags)
- Modify: `Assets/_Game/Scripts/Editor/OracleMenu.cs` (menu `Cluck Wars/Balance/Oracle/Match sensitivity`, `MatchSensitivityBatch`; `MatchReportBatch` now runs P0, P1 and Mixed)
- Modify: `docs/TESTING.md` (one row)
- Test: `Assets/_Game/Scripts/Editor/Tests/UtilityPolicyTests.cs`, `MatchSweepTests.cs`

**Interfaces:**
- Consumes: Tasks 1 to 5; `OracleSolver`'s way of turning a value into a schema-1 delta and evaluating it with `BalanceBook.Evaluate` (for the `LeaderPickWeight` axis, so the swept value goes through `Preview` like any other).
- Produces:
  - `sealed class UtilityPolicy : IMatchPolicy { UtilityPolicy(int seat, PolicyParams p); static readonly PolicyFactory Factory; }`.
  - `PolicyPopulation` gains `P1`, `Mixed` (Mixed draws P0 or P1 per seat from `MatchRng.Create(seed, StreamSetup)`).
  - `sealed class MatchSweepSpec { int Points = 16; ulong RunSeed; int Replicates = 3; float[] LeaderPickWeights; }` (`LeaderPickWeights` default `{1, 1.5, 2, 3, 4}`).
  - `sealed class MatchSweepOutput { PolicyParams[] Points; MatchReportSection[] Reports; MatchReportSection Default; }`.
  - `static class MatchSweep { static PolicyParams[] LatinHypercube(int points, ulong seed); static MatchSweepOutput Run(OracleInputs inputs, float arenaHalf, float sightArrivalRadius, MatchSweepSpec spec); }` (axes and ranges of spec 4.3: delay 0 to 0.6 s, return threshold 0.4 to 1.0, flee radius 6 to 15 m, hunt detour 0 to 15 m, target rule, route).
  - `static class MatchRobustness { const float MinTau = 0.8f; const float MaxEdgeCrossPoints = 3f; static KitCall Call(WinInterval p0, WinInterval p1, WinInterval mixed); static KitCall Tag(KitCall call, float tauAgainstDefault, float worstEdgeCrossPoints); }`.

- [ ] **Step 1: Write the failing tests**

```csharp
// UtilityPolicyTests (synthetic)
[Test] public void StunThenSteal_OnALoadedRivalInRange()                // Stun cast, then Steal within the next two thinks
[Test] public void ShellsUp_OnAnIncomingStealer()
[Test] public void Shoves_ARivalMidCastOffAContestedPile()
[Test] public void P1_NeverHasARejectedIntent()                          // shipped kits, CellLimit 20
[Test] public void P1_HasNoPerClassProfile()                             // two kits with equal abilities and stats but different classes give the same intents on the same view

// MatchSweepTests
[Test] public void LatinHypercube_EachAxisHasOnePointPerStratum()
[Test] public void LatinHypercube_SameSeedSamePoints()
[Test] public void MixedPopulation_DrawsPerSeat_FromTheSetupStream()     // same seed same draw; draws differ across seeds
[Test] public void Call_AgreeingPopulations_KeepTheirCall()
[Test] public void Call_DisagreeingPopulations_ArePolicySensitive()
[Test] public void Tag_TauBelowMin_IsPolicySensitive()
[Test] public void Tag_EdgeCrossAboveMax_IsPolicySensitive()
[Test] public void LeaderPickWeightAxis_GoesThroughTheVerdict()          // the built Death Mark ability carries the swept weight
```

- [ ] **Step 2: Run them and confirm they fail**

`-testFilter "CluckWars.Tests.UtilityPolicyTests;CluckWars.Tests.MatchSweepTests"`. Expected: compile errors.

- [ ] **Step 3: Implement**

`UtilityPolicy` scores the options of spec 4.2 in expected food per second minus risk, every think, from `MatchView` only. The report now gives each kit `Call` from the three populations and `Tag` from the sweep (spec 4.4); a policy-sensitive kit shows its range instead of a call.

- [ ] **Step 4: Run the tests, the full suite and both menus**

New classes pass; full suite 0 failed. Run `MatchReportBatch` and `MatchSensitivityBatch` in batch; expected: report files written, each kit line has a call or "policy-sensitive", the sensitivity run time is logged.

- [ ] **Step 5: Commit**

```bash
git add <every file listed under Files, with .meta for new files>
git commit -m "feat(balance): utility policy P1, mixed population, sensitivity sweep and robustness tags (BL-207 S6)" -m "Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 7 (S7): match records, the drift line and the solver's match term

**Files:**
- Modify: `Assets/_Game/Scripts/Balance/OracleReferenceSO.cs` (match records, `AcceptMatch`)
- Modify: `Assets/_Game/Scripts/Balance/OracleReport.cs` (optional `Match` field; output unchanged when null)
- Modify: `Assets/_Game/Scripts/Balance/OracleSolver.cs` (problem fields, `match` JSON block, the guarded search when `MatchWeight > 0`)
- Create: `Assets/_Game/Scripts/Balance/MatchSolverTerm.cs`
- Modify: `Assets/_Game/Scripts/Balance/MatchReport.cs` (drift lines)
- Modify: `Assets/_Game/Scripts/Editor/OracleMenu.cs` (menu `Cluck Wars/Balance/Oracle/Accept match as balanced`, `AcceptMatchBatch -oracleNote "why"`; `Solve` passes the match block)
- Modify: `docs/TESTING.md` (one row; the Solve row mentions the match block)
- Test: `Assets/_Game/Scripts/Editor/Tests/MatchReferenceTests.cs`, `MatchSolverTermTests.cs`

**Interfaces:**
- Consumes: Tasks 1 to 6; `OracleReferenceSO` (`Entries`, `Offsets()`, `Record`, `Accept`, untouched); `OracleProblem`, `FreeValue`, `OracleProposal`, `OracleSolver.Solve`, `OracleSolver.Score(OracleReport, OracleProblem)` (untouched); `AbilityBaseSO.AimRadiusField`, `CooldownFields` (Task 2).
- Produces:
  - `[Serializable] struct MatchRecordLine { string Label; bool IsClass; float Rate; float Low; float High; int Trials; }`; `[Serializable] struct MatchRecord { string AcceptedOn; string Note; string BalanceHash; string Design; string Population; string RunSeed; List<MatchRecordLine> Lines; }` (seed as hex text).
  - `OracleReferenceSO.MatchRecords` (`List<MatchRecord>`); `static void OracleReferenceSO.AcceptMatch(OracleReferenceSO target, MatchReportSection report, string note, string date)` (appends; never edits `Entries`).
  - `OracleReport.Match` (`MatchReportSection`, null by default).
  - `OracleProblem` gains `float MatchWeight` (default 0), `int MaxMovedValues = 3`, `MatchProblemBlock Match`; `sealed class MatchProblemBlock { int ScreenReplicates = 3; int ConfirmReplicates = 10; int ConfirmBest = 3; ulong RunSeed; }`; JSON key `"match"` with `"weight"`, `"maxMovedValues"`, `"screenReplicates"`, `"confirmReplicates"`, `"confirmBest"`, `"seed"`, all optional.
  - `static List<FreeValue> OracleProblem.DefaultMatchFree(OracleInputs inputs)`: the D8 (b) set of spec 7.1 (the Death Mark `LeaderPickWeight`, the three pile amounts on `MatchConfig`, `PeckCastSeconds` per class stats asset, each kit ability's `CooldownFields`, and its `AimRadiusField` when not null). Proposed steps: weight 0.25, food 1, cast 0.05 s, cooldown 0.25 s, radius 0.25 m.
  - `static class MatchSolverTerm`: `static float Score(MatchReportSection match)` (squared distance from `MatchReports.Target` beyond one interval half-width, kits and classes, skipping `PolicySensitive` and `NoCall`); `static ulong PassSeed(ulong runSeed, int pass)`; `static bool Accepts(float gain, IReadOnlyList<float> blockDiffs, float minGain)`; `static int[] FreeIndices(IReadOnlyList<bool> moved, int maxMovedValues)`; `static int RevertCount(IReadOnlyList<float> holdoutGains, IReadOnlyList<float> thresholds)`.
  - `OracleProposal` gains `string Aborted` (null unless the seat check aborted the solve) and `List<string> Reverted`.

- [ ] **Step 1: Write the failing tests**

```csharp
// MatchReferenceTests
[Test] public void OldReferenceAsset_LoadsWithEmptyMatchLists_AndSameOffsets()
[Test] public void AcceptMatch_AppendsARecord_AndLeavesEntriesUnchanged()
[Test] public void AcceptMatch_Twice_KeepsBothInOrder()
[Test] public void Drift_ComparesWithTheLastRecord()                     // synthetic records and report
[Test] public void Report_WithoutMatch_HasNoMatchKeyOrHeading()          // OracleRunner.Run output: ToJson has no "match", ToMarkdown no match heading

// MatchSolverTermTests
[Test] public void OldProblemJson_ParsesWithMatchWeightZero()
[Test] public void MatchBlock_RoundTripsThroughJson()
[Test] public void MatchWeightZero_ProposalHasNoMatchSection()           // Solve on shipped inputs with a part 1 problem: After.Match == null
[Test] public void Score_RatesInsideTheDeadZone_ScoreZero()
[Test] public void Score_PolicySensitiveAndNoCallKits_AddNothing()
[Test] public void Accepts_GainBelowTwiceThePairedNoise_IsRejected()
[Test] public void Accepts_GainAboveThePairedNoise_IsAccepted()
[Test] public void PassSeed_IsTheSameForEveryCandidateOfAPass()          // common random numbers
[Test] public void FreeIndices_AfterTheCap_OnlyMovedValuesStayFree()
[Test] public void RevertCount_RevertsLastFirst_UntilTheGainHolds()
[Test] public void DefaultMatchFree_ListsEveryD8Value_AndEachResolves()  // one per CooldownFields name and non-null AimRadiusField of each kit ability, the weight, three piles, one cast per class; each passes the solver's field resolution
[Test] public void SeatCheckFlagged_AbortsTheSolve()                     // synthetic flagged report: Aborted not null, no value changed
```

- [ ] **Step 2: Run them and confirm they fail**

`-testFilter "CluckWars.Tests.MatchReferenceTests;CluckWars.Tests.MatchSolverTermTests"`. Expected: compile errors.

- [ ] **Step 3: Implement**

With `MatchWeight == 0` the solver takes its existing path untouched and runs no match. With a weight, the search follows spec 7.1, in this order per pass:

```text
seeds  = PassSeed(problem.Match.RunSeed, pass)
screen = for each free index i (FreeIndices), each direction: report at ScreenReplicates with seeds
best   = the ConfirmBest screened moves by score
for each m in best: current and m at ConfirmReplicates with seeds;
    split the cells into 10 blocks by index; blockDiffs[b] = score(current, b) - score(m, b)
    keep m if Accepts(score(current) - score(m), blockDiffs, MinGain)
take the kept move with the largest gain; mark its value moved; stop when none, MaxPasses, or no free value
holdout = final and start at ConfirmReplicates with PassSeed(RunSeed, MaxPasses + 1);
revert RevertCount(...) moves, last first; list them in Reverted
```

Score per candidate = part 1 `Score` + `MatchWeight x MatchSolverTerm.Score`. Mixed design, P0 and P1 together. A seat check flagged at the start sets `Aborted` and returns the start values.

- [ ] **Step 4: Run the tests, the full suite and part 1's guards**

New classes pass. `OracleReferenceTests`, `OracleSolverTests`, `OracleRunnerTests` pass unchanged. Full suite 0 failed. Then a batch solve with `Captures/oracle/problem.json` holding `DefaultMatchFree` and `"match": {"weight": 1}`; expected: a proposal file, its run time logged, at most 3 values changed. Put the measured time next to the 7.1 formula in the report (not in the spec; the orchestrator decides).

- [ ] **Step 5: Commit**

```bash
git add <every file listed under Files, with .meta for new files>
git commit -m "feat(balance): match records, drift line and the guarded match term in the solver (BL-207 S7)" -m "Co-Authored-By: Claude <noreply@anthropic.com>"
```

After review: `docs/STATE.md` and `librarian` upkeep as after Task 5. The compendium changes only if a value changes.

---

### Not planned: S8

The comparison tool for part 3 runs and playtest logs (spec section 8) waits for BL-208's runs. When it is planned, it compares means and distributions only (real matches are not repeatable per seed) and reads the `MatchTelemetry` line names that `MatchEvent.CauseName` already shares.

---

## Self-review (2026-11-22)

- **Spec coverage.** 2.1 to 2.3: Task 1, 3. 2.4 rows: movement and pile slow (1), Peck cut (3), all jobs and passives (2, 3, 4), keep-out (3), comeback (3), tie-break (1 builds, 3 tests), Hide and information through the sight rule (3), reaction delay (1), jump blink (3). 3.1 to 3.3: Task 2, including `AimRadiusField` and `CooldownFields`. 4.1: Task 5. 4.2 to 4.4: Task 6. 5.1: Task 1 (`MatchRng`), 5.2 designs: Task 4 (forced kills), 5 (the rest); the Economy check design is the Task 1 equivalence test. 5.4: Task 1 measures. 6 outputs: Task 5, with calls in 6 and drift in 7. 6.1: Task 7. 7 and 7.1: Task 7 (kill targets list: Task 4). 8: not planned (S8). 9.2 tests: rule (3, 4), passive hooks (2), equivalence and determinism (1, 5), sight (3), statistics (4), facts (2), builder (2). Gap closed: S4's "the table prints" now happens in Task 5's menu, because no policy exists before Task 5 to play the other seats.
- **Type consistency.** `PolicyFactory(int, PolicyParams)`, `MatchView`, `SeatView`, `MatchIntent`, `MatchSetup`, `MatchResult` are defined in Task 1 and only extended later (Tasks 3, 4 add fields, named in their Produces). `WinInterval` and `SpoilerKillRow` are defined in Task 4 and used in Tasks 5 to 7. `KitCall` is defined in Task 5 and filled in Task 6. `PolicyPopulation` starts with `P0` in Task 5 and gains `P1`, `Mixed` in Task 6. `MatchReports.Target` (Task 5) is the 25 % used by `MatchSolverTerm.Score` (Task 7).
- **Review Focus.** Five lines, each with its test in the owning task (3, 3, 4, 4, 5). Line 1 follows the real rule: a rival hidden before the first look is dropped, so the first look must come on tick 0.
- **Proportion.** The plan carries signatures, test names with assertions and three short algorithm notes (nav field, runner, solver search). It is about as long as the spec.
- **Unconfirmed.** Whether the arms block abilities (Task 3 step 1). The cost per match (Task 1 step 5). The comeback constants' current form in `GameManager` (Task 3). The bot prefab's path for `BotArrivalRadius` (Task 2). The full-suite baseline after 7a0fc7c5 (Task 1 step 0).
