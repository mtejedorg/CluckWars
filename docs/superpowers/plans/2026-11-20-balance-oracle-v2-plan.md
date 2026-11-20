# Balance Oracle v2 (part 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the solo, wall-less, pass/fail Balance Oracle with one that measures four kits at once on the real map, reports instead of blocking, learns from an accepted reference, and proposes values; and land the data rule changes solved with it.

**Architecture:** A baked `OracleMapSO` holds places, walls and wall-aware leg tables. A pure fixed-tick `FarmSim` runs one to four `OracleKit`s over it. `OracleRunner` turns runs into a report (economy per kit, Spoiler and Thief tables); `OracleReferenceSO` stores what Maestro accepted and yields per-kit offsets; `OracleSolver` searches free values and emits a balance proposal. Everything pure lives in `Assets/_Game/Scripts/Balance/` (Gameplay assembly, no Fusion); bake, menus and tests live in the Editor assemblies.

**Tech Stack:** Unity 6000.3, C#, NUnit EditMode tests, `BalanceBook` (schema-1 deltas), Python `tools/balance/`.

**Spec:** `docs/superpowers/specs/2026-11-20-balance-oracle-v2-design.md` (sections 5, 6, 8b, 8c bind; 8c overrides earlier sections where they differ).

## Global Constraints

- Balance code is pure C#, Fusion-free, in `Assets/_Game/Scripts/Balance/` (folder already has `CluckWars.Gameplay.asmref`). No upward assembly reference. Editor-only code in `Assets/_Game/Scripts/Editor/`; tests in `Assets/_Game/Scripts/Editor/Tests/`, namespace `CluckWars.Tests`.
- `ILogService` only, never `Debug.Log`. ScriptableObject types end in `SO`, assets under `Assets/_Game/Data/`.
- Values are read from shipped assets (through `BalanceBook.Preview` when a verdict is given). No test restates an asset value or map coordinate as a literal: load the asset or the bake.
- The oracle never blocks: no test and no editor path may fail a change because a kit misses a design target. The only oracle test that can go red is the drift guard of Task 6.
- Food starting values (spec 8c): personal 10, contested 15, centre 40. Standard farmer: the Warrior class stats with no passive and no abilities. Match stays 40 food / 45 s (`MatchConfig.asset`).
- Tests are judged by a batch run (`unity test`, EditMode) or `TestRunnerApi` through `script-execute`; check `scriptCompilationFailed` first. The known flake `MarkScreenEffectTests.RealRender_DimColdRed...("PC")` is re-run, not fixed here.
- The tree is shared with other sessions. Stage by explicit path only; never `git add -A`, stash, reset or broad checkout. Do not touch files this plan does not name. On 2026-11-20 these are someone else's uncommitted work: six `Data/Abilities/Passives/*.asset`, `FixedKitTests.cs`, `SimplifiedAbilitiesTests.cs`, `SlowAbilityTests.cs`, `tools/balance/tests/test_apply_delta.py`, `test_kitlab.py`, `BUILD_NUMBER`.
- Commit trailer (ruling D15b): `Co-Authored-By: Claude <noreply@anthropic.com>`. Commit per task; never push.
- Each implementer's report ends with a "Docs upkeep" line. `docs/FILE_STRUCTURE.md` and the rules docs are edited only by `librarian` (Task 9).

## Review Focus

1. A pile that empties while a chicken walks to it: the chicken must redirect, not stand at an empty pile until the clock ends (Task 2 test `EmptiedPile_Redirects`).
2. Seat bias: two identical kits reaching a contested pile on the same tick must not always favour seat 0 (Task 2 test `IdenticalKits_SymmetricSeats_BankTheSame`).
3. A non-forager (both Sneaky kits) passed to the farm run must yield "not applicable", not zero food reported as a result (Task 4 test `NonForager_HasNoEconomyNumber`).
4. A map bake that is stale against `Game.unity` (walls or piles moved) must be reported by a test naming the menu that fixes it (Task 1 test `Bake_MatchesTheScene`).
5. A balance delta that changes pile amounts or a class stat must change the oracle's numbers in the same run (Task 4 test `Verdict_ChangesTheMeasurement`).

---

### Task 0: Fusion clock spike (`-cw_timescale`)

**Files:**
- Modify: `Assets/_Game/Scripts/Diagnostics/AutoTestConfig.cs` (new key `cw_timescale`, float, default 1, clamped 1..20)
- Modify: `Assets/_Game/Scripts/App/FusionNetworkService.cs:68-113` (`StartAsync`)
- Test: `Assets/_Game/Scripts/Editor/Tests/TimeScaleFlagTests.cs`

**Interfaces:**
- Produces: `AutoTestConfig.TimeScale` (float). In `StartAsync`, when `mode == GameMode.Single`, dev tools are available and `TimeScale > 1`: the runner's simulation uses scaled time (`SimulationConfig.SimulationTimeMode.DeltaTime`, see `Assets/Photon/Fusion/Assemblies/Fusion.Runtime.xml` members `SimulationUpdateTimeMode` and `StartGameArgs`) and `Time.timeScale` is set to the value once the runner is ready; it is restored to 1 on shutdown. Any other mode ignores the flag and logs one Info line saying so.

- [ ] **Step 1:** Write `TimeScaleFlagTests`: `Parse_ClampsToOneToTwenty` (0.5 -> 1, 10 -> 10, 99 -> 20) and a source guard `Flag_IsReadOnlyInsideTheSingleModeBranch` in the style of the existing single-file guards.
- [ ] **Step 2:** Run the class; expect FAIL (no `TimeScale`).
- [ ] **Step 3:** Implement. Do not change `NetworkProjectConfig.fusion` on disk: apply the time mode to the config instance the solo runner starts with. If the SDK offers no per-start config, fall back to a custom `INetworkRunnerUpdater` passed as `StartGameArgs.Updater` that calls `UpdateInternal(Time.unscaledDeltaTime * scale)`.
- [ ] **Step 4:** Run the class; expect PASS. Run the full EditMode suite in batch.
- [ ] **Step 5:** Live check on a dev-tools Windows build (Editor closed or batch build allowed): `tools/test-session/scenario-pc.ps1 -Mode solo` three rounds at x1, then with `-ExtraArgs '-cw_timescale 5'`, `10`, `20`. Record per run: real seconds per round, ticks per round (expect 45 s x 30 = 1350 +-1 %), banked food per seat, errors and rejected RPCs in the log, the scenario verdict.
- [ ] **Step 6:** Write the result into the spec section 10 (works up to xN / fails because ...), then commit `feat(dev): -cw_timescale accelerates solo matches (Fusion scaled time, Single mode only)`.

---

### Task 1: `OracleMapSO` and its bake

**Files:**
- Create: `Assets/_Game/Scripts/Balance/OracleMapSO.cs`, `Assets/_Game/Scripts/Balance/OraclePaths.cs`
- Create: `Assets/_Game/Scripts/Editor/OracleMapBaker.cs` (menu `Cluck Wars/Balance/Oracle/Bake map`)
- Create: `Assets/_Game/Data/Balance/OracleMap.asset`
- Test: `Assets/_Game/Scripts/Editor/Tests/OracleMapTests.cs`

**Interfaces:**
- Produces:
  - `enum OraclePlaceKind : byte { Centre, Personal, Contested, Base }`
  - `[Serializable] struct OraclePlace { OraclePlaceKind Kind; int Seat; Vector2 Pos; Vector2 Footprint; float Yaw; }` (`Seat` = owning corner for Personal and Base, edge index for Contested, -1 for Centre)
  - `sealed class OracleMapSO : ScriptableObject` with `OraclePlace[] Places` in fixed order (index 0 centre, 1-4 personal, 5-8 contested, 9-12 bases), consts `CentreIndex = 0`, `PersonalIndex(seat)`, `ContestedIndex(edge)`, `BaseIndex(seat)`, `PileCount = 9`; `Vector2[] WallA, WallB`; `float WallHalfWidth` (half wall thickness + `PinwheelLayout.ChickenRadius`); `float BaseRadius, CollectReach`; `float[] WalkLegs, JumpLegs` (13 x 13, row-major, reach-aware metres); `string SceneFingerprint`; `float Leg(int from, int to, bool jumps)`.
  - `static class OraclePaths { static float ShortestPath(Vector2 a, Vector2 b, Vector2[] wallA, Vector2[] wallB, float halfWidth, float arenaHalf); }` (visibility graph round the inflated wall ends; straight distance when the segment is clear).
  - `OracleMapBaker.Bake()` returns the asset; `OracleMapBaker.Fingerprint()` hashes the inputs (plane size, arm count, wall thickness, corner distance, insets, footprints, deposit radius, collect reach).
- A leg is `max(0, pathLength(centreA, centreB) - radius(A toward B) - radius(B toward A))`, radius as `BalanceOracle.Radius` does today (base deposit radius, or pile oval toward the other end plus collect reach). `JumpLegs` uses the straight distance (a jumping kit crosses arms; approximation, stated in the report).

- [ ] **Step 1:** Tests, all against the baked asset and `ShippedMap`, no literals: `Bake_MatchesTheScene` (`map.SceneFingerprint == OracleMapBaker.Fingerprint()`, message "run Cluck Wars/Balance/Oracle/Bake map"); `Places_FollowTheFixedOrder`; `WalkLeg_NeverShorterThanJumpLeg` for all pairs; `BaseToOwnPersonalAndCentre_HaveNoDetour` (walk == jump within 0.01 for each seat); `EachBase_HasOneFreeAndOneWalledContestedPile` (of the two adjacent contested piles exactly one has walk/jump > 1.2); `Legs_AreFourFoldSymmetric` (seat s leg equals seat 0 leg after rotation, 0.01); `ShortestPath_GoesRoundAWallEnd` (one synthetic wall, expected length computed in the test from the geometry).
- [ ] **Step 2:** Run; expect FAIL.
- [ ] **Step 3:** Implement the types, `OraclePaths`, the baker (reads `Game.unity` through the same accessors `ShippedMap` uses, walls from `PinwheelLayout.Build` with the scene's parameters and the generator's keep-clear discs), and bake the asset.
- [ ] **Step 4:** Run; expect PASS.
- [ ] **Step 5:** Commit `feat(balance): OracleMapSO, wall-aware leg tables and the bake`.

---

### Task 2: `FarmSim`

**Files:**
- Create: `Assets/_Game/Scripts/Balance/OracleKit.cs`, `Assets/_Game/Scripts/Balance/FarmSim.cs`
- Test: `Assets/_Game/Scripts/Editor/Tests/FarmSimTests.cs`

**Interfaces:**
- Consumes: `OracleMapSO` (Task 1).
- Produces:
  - `struct OracleKit { string Label; bool Forages; float MoveSpeed; int CargoCapacity; float PeckAmount; float PeckCooldown; float PeckCastSeconds; float DepositRate; bool Jumps; bool IgnoresPileSlow; float BurstMultiplier; float BurstSeconds; float BurstCooldown; }` (burst = the Speed ability, used while travelling whenever ready)
  - `struct OracleFood { float Personal; float Contested; float Centre; }`
  - `enum FarmRoute : byte { Nearest, SidesFirst, CentreFirst, EasySideThenCentre }`
  - `enum FarmFoodEvent : byte { None, GoldenPile, Restock }`
  - `struct FarmRules { float WinTarget; float ClockSeconds; bool StopAtTarget; float TickSeconds; FarmFoodEvent Event; float EventSecondsLeft; float GoldenPileFood; float RestockFood; float PileSlowFactor; }`
  - `struct FarmSeat { bool Present; OracleKit Kit; FarmRoute Route; }`
  - `struct FarmSeatResult { float Banked; float SecondsToTarget; int Trips; }` (`SecondsToTarget` is -1 when not reached)
  - `static FarmSeatResult[] FarmSim.Run(OracleMapSO map, OracleFood food, FarmSeat[] seats, FarmRules rules)` (`seats.Length == 4`, index = seat).
- Rules of the run: fixed tick; seat processing order rotates every tick; a chicken returns when full, when carrying what it still needs, or when no food is left; a peck takes food `PeckCastSeconds` after it starts and the next may start `PeckCooldown` after the previous started (cast <= cooldown is not required: the cycle is `max(cooldown, cast)`); the cooldown also runs while walking; a target pile that empties redirects the walker using `min(done + leg(origin, new), remaining + leg(old, new))`; the pile slow applies to the walk inside a non-empty pile's reach unless `IgnoresPileSlow`; with `StopAtTarget` false a chicken keeps farming past `WinTarget`; routes: `Nearest` = least leg from where it stands; `SidesFirst` = the two adjacent contested piles, then own personal, then nearest; `CentreFirst` = centre while it has food, then nearest; `EasySideThenCentre` = own personal, the unwalled adjacent contested pile, centre, then nearest.

- [ ] **Step 1:** Tests on small synthetic maps built in the test (two or three places, hand-set leg tables), plus the baked map where noted: `SingleLoad_OneTrip` (time = legs/speed + pecks x cooldown + food/deposit rate, computed in the test); `Capacity_ForcesTwoTrips`; `PartialPeck_StillCostsAFullCycle`; `PeckCast_DelaysTheFood` (cast 0.5, cooldown 0.4: three pecks take 1.5 s, not 1.2 s); `WinCrossesMidDeposit_CountsOnlyNeeded`; `StopAtTargetFalse_KeepsBanking`; `EmptiedPile_Redirects`; `IdenticalKits_SymmetricSeats_BankTheSame` (baked map, four identical kits, banked equal within one peck amount); `Jumper_UsesJumpLegs`; `Burst_ShortensTravelOnly`; `Restock_AddsFoodAtTheEventTime`; `NotForaging_BanksNothingAndSaysSo` (`Forages` false -> Banked 0, Trips 0, SecondsToTarget -1); `Deterministic_SameInputsSameOutputs`.
- [ ] **Step 2:** Run; expect FAIL.
- [ ] **Step 3:** Implement `FarmSim.Run`.
- [ ] **Step 4:** Run; expect PASS.
- [ ] **Step 5:** Commit `feat(balance): FarmSim, four kits at once on the baked map`.

---

### Task 3: Kits from the shipped assets

**Files:**
- Create: `Assets/_Game/Scripts/Balance/OracleKits.cs`
- Test: `Assets/_Game/Scripts/Editor/Tests/OracleKitsTests.cs`

**Interfaces:**
- Consumes: `OracleKit`, `OracleFood`, `FarmRules` (Task 2); `BalanceBook.Preview<T>(T asset, BalanceVerdict verdict)`.
- Produces:
  - `sealed class OracleInputs { OracleMapSO Map; BalanceBook Book; BalanceVerdict Verdict; AbilityRegistrySO Abilities; ChickenClassRegistrySO Classes; MatchConfigSO Match; OracleFood? FoodOverride; }`
  - `static OracleKit[] OracleKits.All(OracleInputs inputs)` (one per shipped passive, label `"<Class>_<Passive>"` matching `ChickenSubclass` names)
  - `static OracleKit OracleKits.StandardFarmer(OracleInputs inputs)` (Warrior stats, no passive, no burst, no jump, label `"Standard"`)
  - `static OracleFood OracleKits.Food(OracleInputs inputs)` and `static FarmRules OracleKits.Rules(OracleInputs inputs, bool stopAtTarget, FarmFoodEvent foodEvent)`
- Kit fields come from: class stats; the passive's `ModifyCargoCapacity`, `ModifyCooldown` (for Peck), `ModifyDepositRate`, `IgnoresPileSlow` with a null self; `Forages` = `ChickenClasses.MayForage`; `Jumps` = the kit holds an ability whose `Job` is Jump; burst values from the kit's Speed ability asset (zero when the kit has none), its cooldown through the passive's `ModifyCooldown`. Until Task 7 the food comes from the baked map's scene amounts; after it, from `MatchConfigSO`.

- [ ] **Step 1:** Tests: `EightKits_OnePerShippedPassive`; `Hoarder_CarriesWhatItsPassiveGives` (compares with the passive hook, no literal); `Sneaky_DoesNotForage`; `SpeedKits_HaveABurst_OthersDoNot` (derived from the kit assets); `BullyJumps_SpeedyNever`; `StandardFarmer_IsTheNakedWarrior`; `Verdict_ChangesTheKit` (a verdict raising Fatty `CargoCapacity` changes `Fatty_*` kits and no other).
- [ ] **Step 2:** Run; expect FAIL. **Step 3:** Implement. **Step 4:** Run; expect PASS.
- [ ] **Step 5:** Commit `feat(balance): oracle kits read from the shipped assets`.

---

### Task 4: Measures and report

**Files:**
- Create: `Assets/_Game/Scripts/Balance/OracleReport.cs`, `Assets/_Game/Scripts/Balance/OracleRunner.cs`, `Assets/_Game/Scripts/Balance/SneakyTables.cs`
- Create: `Assets/_Game/Scripts/Editor/OracleMenu.cs` (menu `Cluck Wars/Balance/Oracle/Report`, batch entry `CluckWars.EditorTools.OracleMenu.ReportBatch`)
- Test: `Assets/_Game/Scripts/Editor/Tests/OracleRunnerTests.cs`

**Interfaces:**
- Consumes: Tasks 1-3.
- Produces:
  - `sealed class KitLine { string Label; bool Applicable; float[] BankedBySeatRoute; float[] SecondsBySeatRoute; FarmRoute BestRoute; float BestBanked; float MeanBanked; float Spread; float BestSeconds; float IncomePerSecond; float OffsetSeconds; float Result; }` (`BankedBySeatRoute` is 4 x route count, row-major; `Result = BestBanked + OffsetSeconds * IncomePerSecond`; `IncomePerSecond = BestBanked / clock`)
  - `sealed class SpoilerRow { int Kills; float CargoPerKill; float Final; float FinalWithPassive; float LeaderFinal; bool Wins; bool WinsWithPassive; }`
  - `sealed class ThiefRow { int Steals; float VictimCargo; float Banked; float SecondsNeeded; bool FitsTheClock; }`
  - `sealed class OracleReport { string MapFingerprint; string BalanceHash; OracleFood Food; List<KitLine> Economy; List<KitLine> EconomyGoldenPile; List<KitLine> EconomyRestock; List<SpoilerRow> Spoiler; List<ThiefRow> Thief; List<string> NotModelled; string ToJson(); string ToMarkdown(); }`
  - `static OracleReport OracleRunner.Run(OracleInputs inputs, IReadOnlyDictionary<string, float> offsets)`
- Economy scenario per kit, seat and route: the kit in that seat with that route, the other three seats the standard farmer on `Nearest`; rules `StopAtTarget = false`, clock from `MatchConfigSO.MatchDurationSeconds`. A second pass with `StopAtTarget = true` fills `SecondsBySeatRoute`. Non-foragers: `Applicable = false`, no numbers.
- Spoiler table: kills 0..4 x cargo per kill in {0, the standard farmers' mean cargo over the run, the standard farmer's capacity}. `Final = kills x ExecuteBounty + kills x cargo`; `FinalWithPassive` adds the Spoiler passive's `MatchEndBonusFood`; `LeaderFinal` = the best standard farmer's banked food in a four-standard-farmer run, less, per kill landed on it (kills spread evenly over the three farmers, the leader first), the cargo lost plus `IncomePerSecond x MarkKill RemovalSeconds`. Max kills per round from `MarkKill.asset` cooldowns and window, never a literal.
- Thief table: steals 1..N, each `min(Steal amount x Thief ModifyStealAmount, victim cargo)` for victim cargo in the same three levels; `SecondsNeeded` = steals x Steal cooldown (through the passive) + trips home x the mean base-to-contested walk leg / move speed + banked / deposit rate, trips from the Thief's cargo capacity.
- `NotModelled` always lists: fighting, pursuit, scatter cover, bots' decisions, the Peck cast until it ships in the game, straight-line jump legs.
- The menu writes `Captures/oracle/report-<UTC>.json` and `.md` and logs the path through `ILogService`.

- [ ] **Step 1:** Tests on the shipped assets and bake: `EveryKit_HasALine`; `NonForager_HasNoEconomyNumber`; `Spread_IsBestMinusWorstRoute`; `Offset_MovesTheResultByIncomeTimesSeconds`; `Verdict_ChangesTheMeasurement` (a verdict changing `MatchConfig` deposit rate changes every applicable line); `SpoilerTable_HasFiveKillRowsPerCargoLevel_AndMoreKillsNeverPayLess`; `SpoilerTable_PassiveAddsExactlyTheBonus`; `ThiefTable_MoreVictimCargoNeverNeedsMoreSteals`; `Report_ListsWhatItDoesNotModel`; `Json_RoundTripsTheKitLabels`.
- [ ] **Step 2:** Run; expect FAIL. **Step 3:** Implement. **Step 4:** Run; expect PASS, then run the menu once and read the two files.
- [ ] **Step 5:** Commit `feat(balance): oracle report: economy per kit, Spoiler and Thief tables`.

---

### Task 5: Player-build measurement, non-blocking check

**Files:**
- Modify: `Assets/_Game/Scripts/Balance/BalanceSctCheck.cs` (becomes a thin wrapper over `OracleRunner`), its callers in `Assets/_Game/Scripts/Balance/BalanceBook.Local.cs` / the local balance editor panel (find with `grep -rn "BalanceSctCheck" Assets/_Game/Scripts`)
- Modify: the installer that builds `BalanceBook` (find with `grep -rn "new BalanceBook(" Assets/_Game/Scripts`) to pass the `OracleMapSO`
- Delete: `Assets/_Game/Scripts/Editor/Tests/OracleMapHook.cs`
- Test: `Assets/_Game/Scripts/Editor/Tests/BalanceCheckTests.cs` (rename of the existing check tests if any)

**Interfaces:**
- Produces: `sealed class OracleCheck { bool Relevant; string Note; OracleReport Before; OracleReport After; }` and `static OracleCheck BalanceSctCheck.Run(...)` with no `Passed` member. The local editor shows, per kit, banked before -> after and the change; it never disables Apply. `MapProvider` is removed: the map comes from the serialized asset, so the check runs in a player build.

- [ ] **Step 1:** Tests: `Check_NeverBlocks` (no `Passed`, source guard that the panel does not gate Apply on the check); `Check_RunsWithoutTheEditorHook`; `IrrelevantVerdict_SaysSoAndRunsNothing`.
- [ ] **Step 2:** Run; expect FAIL. **Step 3:** Implement. **Step 4:** Run; expect PASS; full suite in batch.
- [ ] **Step 5:** Commit `feat(balance): the local editor reports the oracle's numbers and no longer blocks (D3a)`.

---

### Task 6: Accepted reference, offsets, drift guard; retire v1

**Files:**
- Create: `Assets/_Game/Scripts/Balance/OracleReferenceSO.cs`, `Assets/_Game/Data/Balance/OracleReference.asset`
- Modify: `Assets/_Game/Scripts/Editor/OracleMenu.cs` (menu `Cluck Wars/Balance/Oracle/Accept as reference`)
- Delete: `Assets/_Game/Scripts/Balance/BalanceOracle.cs`, `SctTargets.cs`, `SpecSctTargets.cs`, the v1 types in `BalanceTypes.cs` that nothing else uses, `Assets/_Game/Scripts/Editor/Tests/BalanceOracleTests.cs`; fix every remaining reference (`grep -rn "SctTargets\|BalanceOracle\b\|SpecSctTargets\|OracleChicken" Assets/_Game/Scripts`)
- Modify: tooltips on `ChickenStatsSO` that say "do NOT hand-tune"
- Test: `Assets/_Game/Scripts/Editor/Tests/OracleReferenceTests.cs`

**Interfaces:**
- Produces: `sealed class OracleReferenceSO : ScriptableObject { string AcceptedOn; string Note; string MapFingerprint; List<Entry> Entries; }` with `[Serializable] struct Entry { string Label; float BestBanked; float OffsetSeconds; }`; `IReadOnlyDictionary<string, float> Offsets()`; `static void OracleReferenceSO.Accept(OracleReferenceSO target, OracleReport report, string note, string date)`.
- Offsets on accept: the common result is the mean `BestBanked` of the applicable kits; a kit's `OffsetSeconds = (mean - BestBanked) / IncomePerSecond`, so every applicable kit's `Result` equals the mean right after an accept. The first reference shipped with this task keeps today's meaning instead: Warrior kits +5 s, the rest 0, note "v1 targets carried over".
- The one drift test: `Measured_EqualsTheAcceptedReference` runs `OracleRunner` on the shipped assets and compares each `BestBanked` with the reference within 0.05; its message: "numbers moved since <AcceptedOn>: accept the new state (Cluck Wars/Balance/Oracle/Accept as reference) or find what moved".

- [ ] **Step 1:** Tests: `Accept_MakesEveryApplicableResultEqual`; `Accept_KeepsNonForagersWithoutOffset`; `Measured_EqualsTheAcceptedReference`; `NoV1Type_IsLeft` (source guard on the deleted names).
- [ ] **Step 2:** Run; expect FAIL. **Step 3:** Implement, delete v1, accept the first reference. **Step 4:** Run; expect PASS; full suite in batch.
- [ ] **Step 5:** Commit `refactor(balance): the oracle is a guide: accepted reference and drift guard replace the SCT gate`.

---

### Task 7: Food as balance data, new amounts

**Files:**
- Modify: `Assets/_Game/Scripts/Gameplay/MatchConfigSO.cs` (new `[Min(0)] public float PersonalPileFood = 10f; ContestedPileFood = 15f; CentrePileFood = 40f;`), `Assets/_Game/Data/MatchConfig.asset`
- Modify: `Assets/_Game/Scripts/Gameplay/MapGenerator.cs:100-121` (amounts read from the injected `MatchConfigSO`; the three serialized amount fields go), `Assets/_Game/Scenes/Game.unity` only for the removed fields
- Modify: `Assets/_Game/Scripts/Balance/OracleKits.cs` (`Food` reads `MatchConfigSO`), `Assets/_Game/Scripts/Editor/Tests/ShippedMap.cs` and the tests that read `_centerPileAmount` etc. (`grep -rn "PileAmount" Assets/_Game/Scripts`); `ShippedMap_TotalFood_IsTwiceTheWinTarget` is replaced
- Modify: `docs/site/index.html` numbers (after the asset change; republish to the same URL, see `memory/mechanics-compendium-site.md`)
- Test: `Assets/_Game/Scripts/Editor/Tests/FoodRulesTests.cs`, `tools/balance/tests/` (a case that a delta can carry `MatchConfig.PersonalPileFood`)

**Interfaces:**
- Produces: `static class FoodRules { static bool OwnPlusOneIsNotEnough(OracleFood f, float target); static bool OwnPlusTwoIsEnough(OracleFood f, float target); }` (personal + contested < target; personal + 2 x contested >= target), used by the test here and by the solver in Task 8 as hard limits.

- [ ] **Step 1:** Tests: `ShippedFood_ObeysMaestrosRules` (both `FoodRules` on the shipped config and its win target); `Generator_SpawnsTheConfiguredAmounts`; the Python delta test.
- [ ] **Step 2:** Run; expect FAIL. **Step 3:** Implement. **Step 4:** Run; expect PASS; the drift guard now fails by design: run the report, then `Accept as reference` with note "food 10 / 15 / 40 (D1b)"; full suite in batch.
- [ ] **Step 5:** Commit `feat(balance): pile food is balance data; personal 10, contested 15, centre 40 (D1b)`.

---

### Task 8: Spoiler payout, mark toward the leader, Leader Bounty event removed

**Files:**
- Modify: `Assets/_Game/Scripts/Gameplay/AssassinExecute.cs:100-105` (no multiplier), `MatchConfigSO.cs` (remove `LeaderExecuteBountyMultiplier`), `MatchConfig.asset`
- Modify: `Assets/_Game/Scripts/Gameplay/GameManager.cs:1029-1050, 1129+` (three events; `ApplyLeaderBounty` goes), `ComebackRules.cs`, `ChickenController.cs` (`LeaderBountyActive` and its replication go), `MatchEventKind.cs` (keep the numeric values of the remaining members; do not renumber), `UiKeys`/wording and HUD paths for the removed event (`grep -rn "LeaderBounty" Assets/_Game`)
- Modify: `Assets/_Game/Scripts/Gameplay/MarkPickRules.cs` and its caller in `AssassinExecute` (leader weight), `Assets/_Game/Scripts/Abilities/MarkKillAbilitySO.cs` + `MarkKill.asset` (new `[Range(1f, 4f)] public float LeaderPickWeight = 2f;`)
- Test: extend `MarkPickRulesTests`, the comeback event tests, `ExecuteRules`/bounty tests

**Interfaces:**
- Produces: `MarkPickRules.Score(float roll, float homeFraction, float assassinFraction, float leaderWeight)` and `PickWeighted(..., IReadOnlyList<float> leaderWeights, ...)`; the caller passes `LeaderPickWeight` for the rival with the most banked food (ties: all tied rivals) and 1 for the rest. The value 2 is a placeholder the oracle and part 2 will tune; say so in the tooltip.

- [ ] **Step 1:** Tests: `Leader_IsPickedMoreOften` (same rolls, weight 2 vs 1); `LeaderWeightOne_IsTodaysPick`; `Kill_PaysTheSameWhoeverTheVictimIs`; `ComebackEvent_IsNeverLeaderBounty` (all rolls map to the three remaining kinds); a source guard that `LeaderBountyActive` is gone.
- [ ] **Step 2:** Run; expect FAIL. **Step 3:** Implement. **Step 4:** Run; expect PASS; full suite in batch; re-run the oracle report and accept the reference if the Spoiler table changed it.
- [ ] **Step 5:** Commit `feat(gameplay): flat execute bounty, mark weighted toward the leader, Leader Bounty event removed (D7b)`.

---

### Task 9: Solver

**Files:**
- Create: `Assets/_Game/Scripts/Balance/OracleSolver.cs`
- Modify: `Assets/_Game/Scripts/Editor/OracleMenu.cs` (menu `Cluck Wars/Balance/Oracle/Solve`, reads `Captures/oracle/problem.json`, writes `Captures/oracle/proposal-<UTC>.json`)
- Test: `Assets/_Game/Scripts/Editor/Tests/OracleSolverTests.cs`

**Interfaces:**
- Consumes: `OracleRunner`, `FoodRules`, `BalanceBook` delta application to previews.
- Produces:
  - `sealed class FreeValue { string Asset; string Field; float Min; float Max; float Step; }` (`Asset` and `Field` as a schema-1 delta names them; the oracle-only `PeckCastSeconds` per class is addressed as asset `"Oracle"`, field `"<Class>.PeckCastSeconds"` and is reported as a proposal, not written into a delta, until the cast ships in the game)
  - `sealed class OracleProblem { List<FreeValue> Free; float BandLow = 28f; float BandHigh = 36f; int MaxPasses = 6; }`
  - `sealed class OracleProposal { string DeltaJson; Dictionary<string, float> OracleOnly; OracleReport Before; OracleReport After; float ScoreBefore; float ScoreAfter; }`
  - `static OracleProposal OracleSolver.Solve(OracleInputs inputs, OracleProblem problem, IReadOnlyDictionary<string, float> offsets)`
- Score to minimise: the variance of `Result` over the applicable kits, plus the squared distance of the standard-farmer four-way run's banked food outside `[BandLow, BandHigh]`. `FoodRules` and each value's `[Range]`/`[Min]` are hard limits: a candidate breaking them is never scored. Search: coordinate descent over the free values at their step, best single move per pass, stop when no move improves or after `MaxPasses`. Fixed values are simply the ones not listed.

- [ ] **Step 1:** Tests: `NoFreeValues_ReturnsTheInput`; `OneFreeValue_NeverMakesTheScoreWorse`; `HardLimits_AreNeverBroken` (free contested food with a range that would break `FoodRules`); `Proposal_IsAValidSchema1Delta` (round-trips through `BalanceBook`); `OracleOnlyValues_StayOutOfTheDelta`.
- [ ] **Step 2:** Run; expect FAIL. **Step 3:** Implement. **Step 4:** Run; expect PASS. Then run one real solve with free values Fatty `CargoCapacity`, Fatty and Hoarder-relevant `PeckCooldown`, `Fatty.PeckCastSeconds`, `Warrior.PeckCastSeconds`, `Speedy.PeckCastSeconds`, and keep the proposal file for Maestro: it is not applied.
- [ ] **Step 5:** Commit `feat(balance): oracle solver proposes free values as a balance delta`.

---

### Task 10: Docs and close

**Files:**
- Modify: `docs/STATE.md` (entry for this work), `docs/BACKLOG.md` (close or retarget BL-046, BL-128, BL-184; add items: Peck cast in the game per spec 6.2/6.4; abstract match simulator, part 2; kill-in-base reserve), `docs/GDD.md` sections 2.1 and 5.2 and the comeback-events section, `docs/TESTING.md` ("Publishing a balance delta": the oracle reports, it does not gate), the project `CLAUDE.md` line "Class stats and cooldowns are solved against the SCT axiom", `.claude/agents/mechanics-designer.md` item 8, `docs/site/index.html`
- `librarian`: `docs/FILE_STRUCTURE.md` (new and deleted scripts, `python -I tools/docs/file_structure.py check`), `docs/CODE_RULES.md` CODE-DESIGN-01/02 and any rule whose enforcement named `BalanceOracleTests`
- Memory: mark `sct-axiom-is-the-balance-authority.md` as superseded by `oracle-is-a-guide-not-law.md`

- [ ] **Step 1:** Make the edits; numbers in the compendium and GDD are read from the assets and the oracle report, never typed from this plan.
- [ ] **Step 2:** `python -I tools/docs/file_structure.py check` passes; full EditMode suite in batch passes.
- [ ] **Step 3:** Commit `docs: Balance Oracle v2, food and Spoiler rule changes`, then run the `housekeeping` skill.
