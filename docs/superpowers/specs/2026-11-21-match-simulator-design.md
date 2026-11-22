---
status: approved
owner: Maestro
type: design
---
# Balance Oracle v2, part 2: the abstract match simulator

Status: approved 2026-11-22 with Maestro's answers below. Nothing here is implemented.
Plan: `docs/superpowers/plans/2026-11-22-match-simulator-plan.md`.
Branch: `develop` (= `balance`). Backlog: BL-207 (this), BL-208 (part 3), BL-209 (kill in base, reserve), BL-210 (LeaderPickWeight).
Parent design: `docs/superpowers/specs/2026-11-20-balance-oracle-v2-design.md` (called "the part 1 spec" below).
Consulted: `mechanics-designer` (opus) on the combat model, `librarian` (sonnet) for the shipped numbers.

## Maestro's answers, 2026-11-22

- **D1 (a).** Continuous 2D positions on a wall-aware path graph.
- **D2 deferred, with a game fix.** His words: "Debería hacer algo en los bots, eso es deuda técnica, arréglalo que se
  queden con la última posición y supongan que está ahí, por ejemplo." Hide having no effect on bots is debt in the
  game. The fix landed in commit 7a0fc7c5: `Assets/_Game/Scripts/Gameplay/BotSightRules.cs`, pure and Fusion-free.
  `BotSightRules.Observe(RivalSighting? previous, Vector3 actualPosition, float visualOpacity, Vector3 selfPosition,
  float now, float arrivalRadius) -> RivalSighting` (`Position`, `Hidden`, `GivenUp`, `HiddenSince`); a rival is hidden
  when `IsHidden(visualOpacity)`; a bot acts on the last seen spot, and drops the rival (`GivenUp`) once it reaches that
  spot or after `GiveUpSeconds` (4 s), until the rival shows itself. A rival hidden before the bot ever looked at it is
  dropped at once. Both simulator policies call the same rule, so Hide has the effect the game gives it. There is no
  zero-effect main number and no separate perception line.
- **D3 (a).** One-per-class design as the headline, pooled from Mixed as the second line.
- **D4 (a).** Report P0, P1 and mixed; a kit gets a call only when they agree, else "policy-sensitive".
- **D5 (b).** Accepting only records. Targets stay at 25 %. No bias learning. Part 1's prices in seconds are untouched.
- **D6: "90/80/20 para empezar".** Starting values he may change: 4 kills without the passive win at least 90 %;
  3 kills without the passive at least 80 %; 2 kills with the passive at most 20 %. They are a small data value on
  `OracleReferenceSO`, editable in the Inspector without a code change (section 7). The kills-to-win table labels each
  row against them; labels only, no test on rates (D7).
- **D7 (a).** No test on rates; determinism and equivalence tests only.
- **D8 (b).** The solver may move `LeaderPickWeight`, pile amounts, `PeckCastSeconds` per class, and every ability's
  cooldown and radius. The cost and the guard against a noisy wide search are in section 7.1.

Facts since the first draft: part 1's result is seconds to the goal (commit a804a9cb); Peck is a real cast with the
class stat `PeckCastSeconds`; BL-208 slice 1 added `SimClock`, a fighter auto-play and match telemetry lines for steals,
Peck cuts, kills and first contact (`docs/TESTING.md`), and found that real accelerated matches are not repeatable per
seed, so only means can be compared (section 8).

## 1. Intent

Part 1 measures farming: seconds to bank 40 with three standard farmers on the map and nobody fighting. It cannot say
who wins a real match, it has no number for the two Sneaky kits beyond arithmetic tables, and it cannot see that a long
Peck cast is easier to cut. Part 2 plays whole four-player matches in an abstract model, thousands of times, and answers:

1. Does each of the 8 fixed kits win 20 to 30 % of mixed four-player matches? And each class? (Maestro, D5a + b.)
2. How many kills does the Spoiler need to win, measured, not assumed? (Target in words: 4 win even without the passive
   given some cargo, 3 almost surely, 2 only with huge luck plus the passive. Starting numbers, D6: 90 / 80 / 20 %.)
3. What does the Thief earn per match, and from whom?
4. How often is each class's Peck cast cut, by which cutter?
5. Which values (LeaderPickWeight, pile amounts, cast lengths, ability cooldowns and radii) bring the kits into the band?

It stays a guide ("el oráculo no es la ley, sólo una guía"): it reports and proposes. No test fails because a kit misses
a target. It does not depend on Fusion, scenes or MonoBehaviours.

**Non-goals.** Better bots in the real game. Real-game runs (part 3). Any rule change: part 2 measures the game as it ships.

## 2. What a simulated match is

### 2.1 Clock and order

- Fixed tick, the same as part 1: `1 / MatchConfig.TickRate` (30 in `Assets/_Game/Data/MatchConfig.asset`, verified).
  Fusion measured 32 Hz in the part 1 clock spike (part 1 spec, section 10). The difference moves any event by under one
  tick (33 ms); the simulator follows the asset so part 1 and part 2 agree. Assumption: the error is negligible next to
  the reaction delay the policies carry (0 to 0.6 s).
- 45 s clock, win at 40 banked, both from `MatchConfigSO` (as `OracleKits.Rules` reads them, `Assets/_Game/Scripts/Balance/OracleKits.cs:77`).
- Seat order rotates every tick, as in `FarmSim` (`FarmSim.cs:130`), so simultaneous claims alternate.
- Every tick has three phases, in this order: (1) each chicken's policy reads a snapshot and returns an intent;
  (2) casts resolve against the snapshot, all at once (two stuns on the same tick both land); (3) movement, pecks,
  deposits and timers advance. Resolving against a snapshot removes the seat-order advantage from combat.

### 2.2 Space: continuous positions on the baked map

Part 1's `OracleMapSO` holds 13 places and two leg tables (walk, jump), with the wall segments it was baked from
(`OracleMapSO.cs:44-58`, verified). Ranges in this game are 5 to 6.5 m and the pinwheel arms bend paths by up to x1.52,
so a place graph is too coarse for fights (section 11, D1). The recommended model:

- Each chicken has a 2D position (metres, world XZ) and walks the shortest wall-aware path to its goal.
- The path uses a visibility graph built at run start from the walls already in `OracleMapSO` (`WallA`, `WallB`,
  `WallHalfWidth`), with the same geometry as `OraclePaths.ShortestPath` (`OraclePaths.cs`, verified: nodes ring each
  wall end, capsule clearance). All-pairs paths between graph nodes are computed once per run and cached by the map
  fingerprint. **No change to the asset or to the part 1 leg tables**, so the part 1 drift guard is untouched.
- Jump kits (`Jumps` true) use straight lines, as part 1's jump table does, with the Jump cooldown applied to each wall
  crossing (section 3). This is closer than part 1, which lets a jumper cross every arm for free.
- Pursuit re-paths to the target's seen position (section 4, perception) at each policy think (default every 0.25 s).
- Range checks are Euclidean, planar, as the game's range-only targeting (`AbilityTargeting.cs:9`, per librarian).
  **Checked in slice S3 (2026-11-22): abilities pass through the pinwheel arms.** A cast picks its victims in
  `AbilityBaseSO.GatherTargets`, which asks `WouldAffect` for each rival: `AbilityBaseSO.cs:852` refuses only when
  `PlayerBase.WallBetween` says a claimed base's wall stands between the two (`BaseWallRules.Blocks`, the keep-out pair
  test), after `IsInAimShape` (`AbilityAim.InShape`, pure planar distance, `AbilityAim.cs:53-56`). No ability code in
  `Abilities/` or `Gameplay/` raycasts for an arm. So `MatchCombat.InRange` is a distance test and nothing else; the arms
  stop walking only. Pinned by `MatchCombatTests.ArmWalls_GameCode_AsksOnlyTheBaseWall` (fails if `WouldAffect` starts
  asking the physics world) and `Abilities_PassThroughInteriorWallArms`.
- Piles are points with the map's `CollectReach`; bases are discs of `MatchConfig.BaseKeepOutRadius` (2.5).
  Expected error: a metre or less on contact distance, against several metres for a place graph (mechanics-designer).

### 2.3 State per chicken

| Field | Meaning |
|---|---|
| `Pos`, `Goal`, `PathNode` | position, current goal point, next node on the cached path |
| `Cargo`, `Bag`, `Banked` | food in the beak, food in the bounty bag (Spoiler kills, `AssassinExecute.cs:115-122`), food at the base |
| `Cooldown[slot]` | seconds until each of the four slots is ready; round-start cooldowns applied (`AbilityBaseSO.RoundStartCooldown`) |
| `Active[slot]` | seconds left of a running ability (Speed, Hide, Egg Shell walk and egg, Slow Area aura) |
| `StunUntil`, `SlowUntil`, `SlowFactor` | control states after the victim's passive (`ModifyControlDuration`, `ModifyIncomingSlowFactor`) |
| `Push` | knockback displacement still to apply (section 3, Shove) |
| `Cast` | Peck in progress: pile, end time; or none |
| `Mark` | for the Spoiler: target seat, mark time, arm end, window end; for a victim: marked by whom |
| `RemovedUntil` | executed: out of play until respawn at its base with cargo 0 (`ChickenCombat.cs:461-482`) |
| `Surge` | Underdog Surge active (x1.4 move speed, `ChickenMovement.cs:80-83`) |
| `Kills`, counters | kills, steals made and suffered, casts cut, contacts: the event log of section 6 |

### 2.4 Modelled and left out

| From the real game | Model | Left out because | Expected error |
|---|---|---|---|
| Movement, walls, pile slow x0.8, Featherfoot ignoring it | modelled (as part 1) | | under 1 tick per leg |
| Body collision between chickens, collision slow | **left out** | needs physics; rare outside piles | crowding at the centre costs nothing here, so contested piles are slightly too easy; Featherfoot's `IgnoresCollisionSlow` is worth 0 here (understated) |
| Pile footprints as solid ovals | point plus reach | the bake already folds the rim into the legs | under 1 m per approach |
| Peck cast, auto-Peck chain, cut rules | modelled with the game's own `PeckCastRules.IsCut` (`PeckCastRules.cs:17`, pure, verified) | | exact by rule |
| All 11 active jobs, 8 passives | modelled (section 3) | | see section 3 |
| Base keep-out and "no ability crosses a base wall" | modelled as a disc and a pair test | | exact by rule |
| Comeback events (Golden Pile, Underdog Surge, Restock) | modelled, drawn at random as the game does | | Golden Pile's random spot is drawn from the same 4.5 to 6.5 m ring (`GameManager.cs`, per librarian) |
| Golden Pile footprint and spot | the plain pile prefab's own footprint (3.6 x 3.0, baked into `OracleMapSO.GoldenFootprint`, not the centre island's), unrotated; up to `GoldenPileSpawnAttempts` (8) spots, the first a ball of `GoldenPileClearRadius` (0.7) fits at, else the last (`GameManager.SpawnGoldenPile`, the constants read directly) | the game's `Physics.CheckSphere` also sees scatter props, the piles' own colliders and the bases; only the walls are baked | a spot the game would redraw for a crate or a pile is kept here, so some Golden Piles stand nearer a prop than in the game; not quantified (the ring is 2 m wide and the props keep to the sectors) |
| Underdog Surge on equal banked food | the lowest seat index | the game takes the first of `ChickenController.ActiveControllers` (spawn order), which the simulator cannot know | a seat bias on ties, which are common when the event fires early (everyone at 0): seat 0 is the underdog here, an arbitrary chicken in the game. Print it among the report's caveats; read the Surge's worth per seat with the seat check |
| Slows and speed boosts together | slows: the strongest of pile, timed and aura applies (`ChickenController.ApplySlow`, mirrored, source-tested); boosts: the game's `AbilityEffectStack` itself | | exact by rule |
| Egg Shell follow-up | any other cast drops the pending egg or ends a running one (`ShieldRules.RunAfterOtherCast`, called); auto-Peck is refused while the egg is live (`AbilityController.AnyStealthRunning`) | the simulator's Peck is the auto-Peck chain; a Peck cast by hand would also drop the egg | none for a policy that stands on a pile; a policy that hand-pecks through its own Shield is not modelled |
| A shove that meets a wall | slides along it: the point of the wall's clearance edge nearest the push (`OracleNav.Slide`) | the game's `CharacterController` slides along the wall's box and its skin | well under 0.1 m a push (a wall's end is round here and square there) |
| A stunned chicken's push | waits for the stun to end (`ControlRules.CanMove`, called) | | exact by rule; note the game's bots have no stun gate in `BotController` (a stunned bot may still walk there, not verified at run time) |
| Timer tie-break: most banked, then most kills, then lowest corner | modelled exactly (`GameManager.cs:652-694`, verified) | | none; the report counts how often the corner decided, since that is a seat bias in the game itself |
| Aim, misclicks, hesitation | one reaction delay `d` per policy | a full human model is not knowable | overstates every active kit; measured by the sweep over `d` (section 4) |
| Hide's opacity | modelled through the game's bot sight rule (D2): an observer acts on a hidden rival's last seen position, and drops it after reaching that spot or `GiveUpSeconds` | | as close to the game as the bots are; a human may track a hidden chicken better or worse than the rule |
| Information | each policy sees what the game's bot sight rule lets a bot see (the same rule, called, not copied) | | overstates hunters only as much as the game's bots do |
| Network latency, prediction, animation, VFX, audio, UI | left out | not balance | latency 0.05 to 0.15 s is inside the swept `d` |
| Jump's arc and landing | a blink: straight line, crosses walls, cooldown per crossing | | under 1 m per jump |
| Retired abilities, free loadouts | left out | fixed kits only (`LoadoutMode: 0`, per librarian) | none |

Every report prints this table's "left out" column, as part 1's report does (part 1 spec, anti-goal "statistics presented as truth").

## 3. Abilities and passives

Every number below is read from the shipped assets through `BalanceBook.Preview` (the same `CopyScope` pattern as
`OracleKits.cs:172-197`), so a proposed delta is measured before it is applied. The simulator holds no number of its own.
Values quoted are today's, for orientation only; the librarian read them on 2026-11-21.

### 3.1 What the abilities already declare

`AbilityBaseSO` already declares most of what the simulator needs (verified, `AbilityBaseSO.cs`): `Cooldown` (146),
`Duration` (143), `RoundStartCooldown` (231), `AimRadius` / `IndicatorRange` (472, 224), `NominalStealAmount` (402),
`EmittedControl` (421, a `ControlEmission` of kind, seconds, slow factor: `ControlClaimRules.cs:21-33`), `EmittedKnockback`
(434), `TryGetAreaEffect` (602), `GrantsStealProof` (1053), `EndsOnNextMove` (1030), `FollowUpPhaseSeconds` (1057), `Job`.
Passives declare their hooks with a nullable owner (`PassiveAbilitySO.cs:118-209`, verified), so they can be called with
no chicken, as `OracleKits.Build` already does (`OracleKits.cs:136-142`).

**Rule:** the simulator reads these declared members first. `AbilityOracleFacts` grows only for numbers that are not
declared anywhere (3.3). Cooldowns always pass through the owner's passive `ModifyCooldown` (Bully x0.75).

### 3.2 Per ability

| Job (asset) | Abstract effect | Numbers read |
|---|---|---|
| Peck (Peck) | Cast of `PeckCastSeconds` at a pile in reach; food lands at the end, capped by free cargo and the pile; next cast after `max(PeckCooldown, cast)`. Cut by stun, shove, execution, empty pile or own move: nothing lands, cooldown spent. A stun on the landing tick lets the food land (per librarian, `AbilityController.cs:1343-1357`) | class `PeckAmount`, `PeckCooldown`, `PeckCastSeconds` (3 / 0.5 to 0.79 / 0.4 to 0.7), passive `ModifyCooldown` |
| Steal (Snatch) | Instant. Every rival within range, outside the keep-out pair rule, with cargo or bag, loses `min(amount x thief multiplier, its cargo then bag, thief's free room)`; an egg yields nothing | `AimRadius` (5.16), `NominalStealAmount` (5), `Cooldown` (4), `EmittedKnockback` (0), passive `ModifyStealAmount` (Thief x1.6) |
| Stun (WingSlam) | Every rival in radius: no move, no cast, for the seconds after both passives; cuts Peck; arms the Kill | `AimRadius` (5.4), `EmittedControl` (Stun 1.3 s), `Cooldown` (7), caster `OutgoingControlMultiplier`, victim `ModifyControlDuration` |
| Shove (CluckShock) | Radial push away from the caster. Real decay is exponential: `Lerp(v, 0, 8 dt)` per tick (`ChickenMovement.cs:94-99`, verified), so travel is about v0 / 8 = 2.0 m, 0.5 m on a Bulwark; an egg refuses it. Applied as a displacement on top of the victim's own steering, clamped by walls. Cuts Peck | `AimRadius` (5.4), `EmittedKnockback` (16.2), `Cooldown` (5), victim `ModifyKnockback` (Bulwark x0.25), decay rate (3.3) |
| Slow (Slow) | Every rival in radius moves at the factor for the seconds, after both passives (Slippery: 0.55 becomes 0.73). Does not cut Peck | `AimRadius` (6.48), `EmittedControl` (Slow 3 s x0.55), `Cooldown` (6) |
| Slow Area (FeatherAura) | Aura on the caster for its duration: any rival inside the radius is slowed, refreshed each tick. A placed zone (`AreaKind` 1) is modelled as a fixed disc if a kit ever ships it | `TryGetAreaEffect` (radius 6.48, factor 0.55, kind Aura), `Duration` (5), `Cooldown` (7) |
| Speed (SpeedBurst) | Self x multiplier for the seconds; the policy fires it when travelling far or fleeing | `OracleFacts.SpeedMultiplier`, `SpeedSeconds` (1.3, 1.5; already in part 1), `Cooldown` (4) |
| Jump (Shadowstep) | Blink: crossing a wall on the straight path costs one Jump; a ready Jump also gives an escape step of its blink distance | `Cooldown` (2), blink distance (3.3, new fact) |
| Shield (TurtleMode, Egg Shell) | Walk phase at the speed multiplier, then egg: no move, immune to control, shove, steal and execute | walk seconds, egg seconds, walk speed multiplier (3.3), `GrantsStealProof`, `Cooldown` (8) |
| Hide (Invisibility) | While active, the chicken's opacity is the ability's authored opacity, and rivals see it only as `BotSightRules.Observe` allows: they act on its last seen position, then drop it (D2). Ends on the chicken's next move; auto-Peck keeps it (per mechanics-designer, `AbilityController.cs:517-525`) | `Duration` (2), `Cooldown` (4.5), `EndsOnNextMove`, and whatever the sight rule reads from the game's data |
| Death Mark (MarkKill) | Press: the target is picked by the game's own `MarkPickRules` (pure, `MarkPickRules.cs`, verified) with the sim's RNG for the roll. Arm, then window; the Kill lands only on a stunned target within kill range inside the window. Kill: victim removed for the removal seconds, respawns at base with 0 cargo, its cargo plus `ExecuteBounty` go to the Spoiler's bag. Success and fail cooldowns | existing facts (`MarkRoundStartCooldown`, `MarkWindowSeconds`, `MarkSuccessCooldown`, `MarkRemovalSeconds`), plus arm seconds, kill range, fail cooldown, leader pick weight (3.3); `MatchConfig.ExecuteBounty` (5) |
| Bank | No live ability holds this job (no kit has one, per librarian). Not modelled; a builder check fails loudly if a kit ever carries a job the simulator does not know | |

Passives (hooks, per librarian, read through the hooks, never as copied values):

| Passive | Hook the simulator calls |
|---|---|
| Bully | `ModifyCooldown` (x0.75, not Peck) |
| Relentless | `ModifyControlDuration` (incoming x0.75), `OutgoingControlMultiplier` (x1.3) |
| Slippery | `ModifyControlDuration` (slow x0.6), `ModifyIncomingSlowFactor` |
| Featherfoot | `ModifyDepositRate` (x1.1), `IgnoresPileSlow`, `IgnoresCollisionSlow` (no effect here, see 2.4) |
| Bulwark | `ModifyControlDuration` (x0.65), `ModifyKnockback` (x0.25) |
| Hoarder | `ModifyCargoCapacity` (at least 40) |
| Spoiler | `MatchEndBonusFood` (15, paid before the winner is picked, `GameManager.cs:654-657`) |
| Thief | `ModifyStealAmount` (x1.6), `MaxStealMultiplier` |

### 3.3 New facts and constants (the only code changes outside `Scripts/Balance/`)

`AbilityOracleFacts` (`Assets/_Game/Scripts/Abilities/AbilityOracleFacts.cs`, today 7 fields, verified) gains:

| Field | Declared by | Why |
|---|---|---|
| `BlinkMetres` | Shadowstep | escape step and wall crossing |
| `ShieldWalkSeconds`, `ShieldWalkSpeedMultiplier`, `ShieldEggSeconds` | TurtleMode | the two phases |
| `MarkArmSeconds`, `MarkKillRange`, `MarkFailCooldown`, `MarkLeaderPickWeight` | MarkKill | the kill window and the pick |
| `HideOpacity` | Invisibility | the opacity `BotSightRules.IsHidden` reads while Hide runs |

`BotController` gains a read-only `ArrivalRadius` over its serialized `_arrivalRadius` (no behaviour change), so the
Editor caller can hand the bot prefab's value to `MatchSight`.

For the D8 (b) solver, `AbilityBaseSO` gains two declared members, with no behaviour change: `AimRadiusField` (the
name of the serialized field behind `AimRadius`, null where none may be tuned) and `CooldownFields` (`Cooldown` by
default; Death Mark names its success and fail cooldowns). The solver names a value by asset and field, and these let
it do so without naming a concrete ability type.

Two engine constants become public named constants, with no behaviour change: the knockback decay rate
(`ChickenMovement.KnockbackDecayRate`, private const 8 today, `ChickenMovement.cs:38`) and the Underdog Surge speed factor
(a literal `1.4f` today, `ChickenMovement.cs:82`). A literal copied into the simulator would be the drift the part 1 spec
forbids (anti-goal "a fixture or literal copied by hand").

## 4. Decision policies

A policy is a pure function: snapshot of the match, its own memory, an RNG stream, a parameter set; it returns one intent
(goal point, cast slot or none, start or keep a Peck). All policies share one set of legality rules (cooldown ready,
not stunned, not removed, keep-out pair rule), so a policy can only be dumb, never illegal.

**Perception (D2).** A policy never reads a rival's true position. The snapshot gives each observer a view built by
`MatchSight`, a thin adapter over `BotSightRules.Observe`: a visible rival at its true position, a hidden one at the
position the observer last saw it, and a given-up one not at all. The arrival radius is the bot prefab's own value.
P0 and P1 use the same view, so Hide counts in every main number.

### 4.1 P0, scripted: the game's bot, transcribed

P0 mirrors `BotController.Think` (`Assets/_Game/Scripts/Gameplay/BotController.cs:208-385`, verified) and the per-class
profiles (`ChickenClassInfo.cs:71-114`, values pinned in `ChickenClassTableTests.cs:141-144`, verified). Priority list:

1. **Bank or lose it**: cargo would not reach the base before the clock (`BotTactics.MustBankNow`, reused as is).
2. **Steal guard**: loaded, a stealer in range, Egg Shell ready: shell up.
3. **Mark follow-through**: hunt the marked target, keep the stun back until it still runs at arm end, stun, kill (`BotTactics.NextMarkStep`).
4. **Bag**: bounty bag not empty: go home.
5. **Flee**: cargo fraction at or above `ProtectCargoThreshold` and a rival within `DangerRadius`: home, cast the retreat plan (Escape, Defense, Control).
6. **Deposit**: cargo fraction at or above `BotTactics.ReturnThreshold` (moves with phase and lead).
7. **Hunt**: most valuable rival in `HuntRadius` (leader focus), cast the engage plan (Steal, Control, Offense).
8. **Guard** (Fatty): rival near own base: hold it.
9. **Search** (Assassin): walk to the nearest rival.
10. **Collect**: cheapest pile by round-trip cost (`RoundTripBias`); contest a rival on it.

Where `BotTactics` exposes a pure function, P0 calls it rather than re-implementing it (it is in the Gameplay assembly,
`BotTactics.cs:151`, "Pure, MonoBehaviour-free"). P0 is therefore the bridge to part 3: the same decisions on a cleaner
world, so a disagreement with part 3 points at the rules or geometry, not at the policy.

### 4.2 P1, stronger: a utility chooser

Re-evaluated every think (0.25 s). Each option is scored in expected food per second minus risk:
farm (food per cast over the trip), bank (cargo at risk times the nearest threat's reach), steal (expected take times the
chance to reach the victim before it banks), kill (cargo + bounty), deny (stop the rival nearest to 40).
On top it plays combos and counters: Stun then Steal or Kill; Egg Shell on an incoming stunner or stealer; Slow on a
chaser; Shove a rival off a contested pile mid-cast; a third party waits out a fight between two others. P1 has no
per-class profile: it plays the kit, which is what a skilled human does.

### 4.3 Parameters and the sensitivity sweep

Swept per seat with a Latin hypercube (16 points by default):

| Parameter | Range | Note |
|---|---|---|
| reaction delay `d` | 0 to 0.6 s | the single most important axis for a children's game; decides whether a 0.4 s cast can be cut |
| return threshold | 0.4 to 1.0 of cargo | |
| flee radius | 6 to 15 m | |
| hunt detour | 0 to 15 m | |
| target rule | nearest, richest, leader | |
| route set | part 1's four routes | |
| `LeaderPickWeight` | 1, 1.5, 2, 3, 4 (its authored range, `MarkKillAbilitySO.cs:65` per mechanics-designer) | also the BL-210 answer |

Populations: all P0, all P1, and mixed (policy drawn per seat), so a kit is never only measured against its mirror policy.

### 4.4 When is a verdict a verdict

A kit's call ("in band", "above", "below") is **policy-robust** when all three hold (thresholds are my proposal):

- the call is the same under P0, P1 and the mixed population;
- across the hypercube, the kits' rank order keeps Kendall tau at least 0.8 against the default point;
- across the `d` range, the kit's win rate never crosses a band edge by more than 3 points.

Otherwise the report says "policy-sensitive" for that kit, shows the range, and the solver does not move values to
please it. A balance verdict that flips with the policy is not a verdict.

## 5. Randomness and statistics

### 5.1 Seeds and determinism

- One generator owned by the simulator (SplitMix64 for seeding, PCG32 per stream), not `System.Random` and not
  `UnityEngine.Random`: its sequence is fixed by our code, so it is the same in Mono, IL2CPP and future .NET.
- Match seed = hash(run seed, design cell, replicate). Each chicken, the comeback draw and the mark pick get their own
  stream split from it, so adding a random draw to one policy does not shift the others.
- Same seed, same build, same machine: bit-identical result. Across runtimes floats may differ in the last bits; the
  reference stores numbers, never hashes of runs.
- Common random numbers: two candidate values in the solver run the same seeds, so a difference is the change, not the dice.
- What is random: comeback event kind and Golden Pile spot, the mark roll, per-seat policy parameters and route, and a
  small reaction jitter. Everything else is deterministic.

### 5.2 Designs (which matches are run)

| Design | Cells | Purpose |
|---|---|---|
| **Mixed** | all 70 sets of 4 distinct kits out of 8, times the 6 seatings that differ up to rotation (the map is four-fold symmetric), times R replicates | the headline per-kit win rate; each kit plays 35 x 6 x R matches |
| **One per class** | the 16 ways to pick one kit per class, times 6 seatings, times R | the per-class win rate as Maestro states it (D3) |
| **Class head-to-head** | read from Mixed: the 15 cells holding both kits of one class | which of the two kits of a class finishes ahead |
| **Seat check** | each kit four times (a full mirror), times R | every seat must win 25 % within the interval; a failure is a model or map bug, not balance |
| **Forced kills** | the Spoiler is granted exactly k = 0..4 kills on schedule, on empty and on loaded victims | the kills-to-win table without the survivor bias of observed kills (section 6) |
| **Economy check** | four standard farmers, no abilities | must reproduce part 1's `FarmSim` (section 9.3) |

### 5.3 How many matches

Win rate p near 0.25; 95 % Wilson interval half-width is about 1.96 x sqrt(p(1 - p) / n) for n appearances.

| Design, R | Matches | Appearances per kit or class | Half-width |
|---|---|---|---|
| Mixed, R = 10 | 4 200 | 2 100 | 1.9 points |
| One per class, R = 25 | 2 400 | 2 400 | 1.7 points |
| Sensitivity: Mixed, R = 3, per hypercube point | 1 260 | 630 | 3.4 points |

So the default report states each kit's rate within about plus or minus 2 points, enough to tell 24 % from 30 %.
Ties at the clock are rare and resolved by the game's own rule (2.4), so every match has exactly one winner and the 8
kits' win shares of the Mixed design add up to 4 x 25 %.

### 5.4 Runtime budget on this PC

Measured facts: the PC has 12 logical processors (`nproc`). Measured in S1 (2026-11-22, `OracleMenu.MatchTimingBatch`,
batch mode, one thread, 1 000 farm-only matches on the shipped inputs): 2.45 to 3.13 ms per match. Combat adds cost
(pairwise range checks, a policy think every 0.25 s) and is re-measured in Task 5; the 2 to 5 ms assumption still holds
as the budget below.

| Run | Matches | Budget |
|---|---|---|
| Default report (Mixed + One per class + Seat check + Forced kills) | about 9 000 | under 60 s |
| Sensitivity (16 points x 3 populations) | about 60 000 | under 5 min |
| Solver (D8 (b), common random numbers, up to 6 passes; formula in 7.1) | about 900 000 | 3 to 8 min at 2 to 5 ms, under 30 min with flat steps, batch mode |

Matches run in parallel (`Parallel.For` over the match index). Kits are resolved on the main thread first, because they
read ScriptableObjects; the simulation then touches only plain structs. Results are summed in match-index order, so the
totals do not depend on thread scheduling. Part 2 runs in the Editor and in batch mode only; the player build keeps
part 1's measurement (my call: the cost and threads do not suit a phone).

## 6. Outputs

All numbers come with their interval and the run's seed, design and policy population.

| Output | Definition |
|---|---|
| Win rate per kit | share of its appearances in Mixed that it won, Wilson 95 % interval, call against 20 to 30 %, robustness tag (4.4) |
| Win rate per class | from One per class (headline) and pooled from Mixed (second line) |
| Class head-to-head | per class, how often each of its two kits finished ahead of the other |
| Spoiler kills-to-win | rows k = 0..4 from Forced kills: P(win) without the passive, P(win) with it, each with its interval, its final total against the best rival's, P(nobody reached 40); plus the observed distribution of kills per match from Mixed. A row is labelled against the reference's kill targets when one covers it (D6 starting values: k = 4 without passive at least 90 %, k = 3 without passive at least 80 %, k = 2 with passive at most 20 %); labels only, never a test |
| Thief income | food stolen per match, banked per match, and the share taken from each victim kit |
| Peck cuts | per class x cutter (Stun, Shove, execution, empty pile, own move): the hazard P(cut, given a cutter with a ready cutting ability is in range when the cast starts), plotted against cast length and `d`; plus food lost to cuts per match per class. The raw count is shown but not used: it confounds exposure (the Fatty pecks more casts near more rivals) |
| Time to first contact | median and quartiles of the first time two chickens are within the smallest offensive range (5.16 m), per seating |
| Economy share | per kit, food banked from foraging, from stealing and from bounty, so a win is explained |
| Corner tie-breaks | how many matches the lowest-corner rule decided |
| Seat check | per seat, the win rate of the full mirror with its interval; outside 25 % it is flagged as a model or map bug (a report line, never a test: D7) |
| Drift | per kit, the change from the last accepted match record, in points, with both intervals (D7: a report line, not a test) |
| Left out | the "left out" column of 2.4 |

### 6.1 Joining part 1 without breaking its drift guard

- `OracleReport` gets an optional `Match` section; its existing fields, text and JSON keys are unchanged. Part 1's
  report from the same inputs is byte-identical when the match section is not requested.
- `OracleReferenceSO` gets two new serialized lists: accepted match records (section 7) and Spoiler kill targets (the
  D6 starting values). Its `Entries` and `Offsets()` are untouched; the existing asset loads with the new lists empty.
  Part 1's drift test reads only `Entries`.
- `OracleSolver.Score` gets a match term with weight `OracleProblem.MatchWeight`, default 0. With 0 the solver is the
  part 1 solver to the bit, so `OracleSolverTests` cannot move. With a weight, the score adds the squared distance of
  each kit's and each class's win rate from 25 % (D5 (b): fixed, no bias), counted only outside a dead zone of one
  interval half-width. Kits tagged "policy-sensitive" or "model not trusted" add nothing. The search guard is in 7.1.
- `FarmSim` is not edited. Part 2 does not reuse its loop; it reuses its inputs (`OracleKit`, `OracleFood`, `FarmRules`,
  `OracleKits`) and is checked against it (9.3).
- The problem JSON gains an optional `match` block (design, R, population, weight). Old problem files parse as before.

## 7. What it keeps from Maestro (D5 (b))

Part 1 stores prices in seconds and keeps them. Part 2 only records; it learns no bias.

- **Accept match as balanced.** One Editor action next to part 1's. It appends a match record with date and note:
  per-kit and per-class win rates, their intervals, the design, the run seed, the policy population and the balance
  hash. Targets stay at 25 % for every kit and class; nothing about the solver changes. Part 1's prices are not touched.
- **History, not overwrite.** Every accept is appended. The report's drift line compares with the last record, and lists
  earlier records, so his manual tunings stay visible as data.
- **Spoiler kill targets (D6).** A list on `OracleReferenceSO` of rows (kills k, with or without the passive, minimum
  and maximum win rate), shipped with the starting values 4 / no passive / at least 0.90, 3 / no passive / at least
  0.80, 2 / passive / at most 0.20. Maestro edits them in the Inspector; no code change. They label report rows only.
- The first draft's model bias and measured price are dropped: nothing would read them under D5 (b).

### 7.1 The solver search under D8 (b): cost and guard

**Search space.** About 24 free values: `LeaderPickWeight`; three pile amounts; `PeckCastSeconds` for 4 classes; the
cooldowns of the 10 non-Peck abilities in the kits (11 values: Death Mark has two); the radius of the 5 area abilities (Steal, Stun, Shove, Slow, Slow
Area). The solver prints the exact count. Each value moves in its authored `[Range]` / `[Min]` and the step the problem
file gives it.

**Cost.** One candidate is scored on the Mixed design under P0 and P1 together (D4 needs both). A pass has two stages:

- screen: every one-step move, both directions (2 x 24 = 48), at R = 3: 48 x 1 260 x 2 = about 121 000 matches;
- confirm: the best 3 screened moves at R = 10: 3 x 4 200 x 2 = about 25 000 matches.

Matches per solve = passes x (2V x 2 520 + 3 x 8 400), so about 146 000 per pass and about 880 000 for 6 passes, plus
one holdout run (8 400). At the measured 2.45 to 3.13 ms per farm-only match (S1; combat adds cost, re-measured in Task 5), 880 000 matches take
about 40 min on one thread and 3 to 6 min on 12 threads (about 10x); a pass
that needs the flat-step search (up to 4 steps) costs up to four times its screen, so the worst case stays under 30 min.
S1 measured the farm-only cost; the formula is re-evaluated with the combat cost in Task 5. Batch mode only.

**Guard.**

1. **Common random numbers.** Within a pass, every candidate and the current point run the same match seeds, so a
   difference is the change, not the dice.
2. **Gain threshold above the noise.** A move counts only when its score gain exceeds both the dead zone (rates within
   one interval half-width of 25 % score 0) and twice the standard error of the paired score difference, estimated from
   the per-cell paired results of the confirm run.
3. **Cap on values moved.** At most `OracleProblem.MaxMovedValues` distinct values (default 3) change in one solve. Once
   the cap is reached, only those values stay free.
4. **Holdout.** The final proposal is re-run on fresh seeds. Moves whose gain does not hold above the threshold are
   reverted, last first, and the proposal says which.
5. A Seat check outside 25 % aborts the solve: a seat bias is a model bug, not balance.

## 8. Validation against the real game

### 8.1 Sources

1. **Part 3 bot runs** (BL-208): solo matches with four game bots on the accelerated clock (`SimClock`, slice 1), x5
   being the honest ceiling today (part 1 spec, section 10). P0 is the same decision logic as the game's bots, and both
   use the same sight rule (D2), so P0 against part 3 compares the rules and geometry with the policy held nearly
   constant. The fighter auto-play (slice 1) gives human-seat runs that do fight.
   Assumption: about 350 matches per hour per instance; 600 matches (about 300 appearances per kit) take about 2 hours.
2. **Playtest logs**: the `MATCH SUMMARY` block (`MatchSummaryLines.cs`) and `ChickenMatchStats`. Few matches; used for
   human-versus-P1 sanity, never for a per-kit rate.

**Telemetry (BL-208 slice 1, landed).** `MatchTelemetry` (`Assets/_Game/Scripts/Gameplay/MatchTelemetry.cs`) logs
`steal`, `peckcut` (causes `stun`, `shove`, `execution`, `empty-pile`), `kill` and `contact` lines. The simulator's
`MatchEvent` uses the same names and causes, plus `own-move` for a cut the game does not log. Flee outcomes are still
not logged. **Real accelerated matches are not repeatable per seed** (slice 1 finding): comparisons are of means and
distributions over many matches, never of one seed's event log.

### 8.2 What must agree, and how closely

| Statistic | Tolerance (P0 against part 3) |
|---|---|
| Food banked per seat, no-fight economy run | 10 % |
| Per-kit win rate | the larger of 5 points and twice the combined standard error |
| Kit rank order | Kendall tau at least 0.7 |
| Spoiler kills per match, mean | 0.5 |
| Steals per match per kit | 30 % |
| Peck cut rate per class | 5 points |
| Time to first contact, median | 2 s |

### 8.3 When they disagree

1. Never tune the simulator's game numbers to fit: they come from the assets.
2. Compare the two sides statistic by statistic for the same seating (both emit the same event names): rates per
   minute of each event kind, by seat and class, and when they first occur. The first statistic outside its tolerance
   points at the cause. Per-seed diffs are not possible (the real game is not repeatable per seed).
3. The cause is one of: a rule bug in the simulator (fix it, with a behaviour test), a missing mechanic from 2.4 (add it
   or state the error), or the policy (only the reaction delay `d` may be fitted, and the fitted value is stored in the
   reference and printed; perception is the game's own sight rule and is never fitted).
4. Until it is resolved, the report marks that kit "model not trusted" and shows no call for it. The solver ignores it.

## 9. Architecture

### 9.1 Types and files

All in `Assets/_Game/Scripts/Balance/`, namespace `CluckWars.Balance`, Gameplay assembly (the folder's `CluckWars.Gameplay.asmref`,
verified), pure, no Fusion, no `UnityEngine` beyond `Vector2` and the ScriptableObject reads in the builder.
The Editor menu items go in `Assets/_Game/Scripts/Editor/OracleMenu.cs` (Editor assembly, exists, verified).

| Type | File | Role |
|---|---|---|
| `OracleNav` | `OracleNav.cs` | visibility graph from `OracleMapSO` walls, cached all-pairs, `NextPoint(from, to)`; geometry shared with `OraclePaths` (its node builder made internal, no behaviour change); the arena half-size is passed in, as the bake does (the asset does not store it) |
| `MatchSight` | `MatchSight.cs` | the only caller of `BotSightRules.Observe` (D2); builds each observer's view of the rivals |
| `MatchKit`, `MatchAbility`, `MatchPassive` | `MatchKit.cs` | a kit as numbers: wraps `OracleKit` for farming, adds the abilities and the passive hooks resolved for this kit |
| `MatchKits` | `MatchKits.cs` | builds the 8 kits from `OracleInputs` (same inputs as part 1, same Preview copy scope) |
| `MatchRules` | `MatchKit.cs` | `FarmRules` plus keep-out radius, execute bounty, comeback list and constants |
| `MatchSim` | `MatchSim.cs` | `Run(MatchSetup, ulong seed) -> MatchResult`; the tick loop of 2.1 |
| `MatchChicken` | inside `MatchSim` | the state of 2.3 |
| `IMatchPolicy`, `PolicyParams` | `MatchPolicy.cs` | the policy contract |
| `ScriptedPolicy` (P0), `UtilityPolicy` (P1) | `ScriptedPolicy.cs`, `UtilityPolicy.cs` | section 4; P0 calls `BotTactics` pure functions |
| `MatchRng` | `MatchRng.cs` | SplitMix64 and PCG32 streams |
| `MatchEvent` | `MatchSim.cs` | optional event list (contact, cast, steal, cut, kill, bank) for statistics, debugging and section 8 |
| `MatchDesign`, `MatchRunner` | `MatchRunner.cs` | the designs of 5.2, parallel execution, aggregation in index order |
| `MatchStats`, `WinInterval` | `MatchStats.cs` | Wilson intervals, Kendall tau, hazards |
| `MatchReportSection` | `MatchReport.cs`, held by an optional field of `OracleReport` | section 6 |
| `MatchRecord`, `SpoilerKillTarget` | `OracleReferenceSO.cs` (new lists) | section 7 |
| solver match term | `OracleSolver.cs` plus `MatchSolverTerm.cs` | section 7.1 |

Reused from part 1 unchanged: `OracleMapSO`, `OracleKit`, `OracleFood`, `FarmRules`, `OracleKits`, `OracleInputs`,
`FoodRules`, `SneakyTables` (its arithmetic tables stay as the zero-interaction baseline next to the simulated ones).
Reused from the game, pure and already in the Gameplay assembly: `PeckCastRules.IsCut` and `LandingTake`,
`MarkPickRules`, `BotTactics` (P0), `StealRules.Stealable` and `SplitDrain`, and `BotSightRules` (D2, through
`MatchSight` only).

### 9.2 Test strategy: behaviour, never design numbers

No test reads a shipped asset to assert a win rate, a band or a target. Tests build kits from synthetic numbers.

- **Rule tests** (one per mechanic, synthetic kits on a synthetic open map): a rival at radius minus epsilon is stunned,
  at radius plus epsilon is not; a stun mid-cast lands no food and spends the cooldown; a slow mid-cast does not cut it;
  a stun on the landing tick lets the food land; no ability crosses the keep-out pair; an egg refuses steal, shove,
  control and execute; a Kill needs stunned and in range inside the window; the executed chicken's cargo plus the
  bounty reach the bag; the Spoiler bonus is paid before the winner is picked; the tie-break order matches the game's.
- **Passive hook tests**: a synthetic passive returning a known multiplier changes the right quantity and only it.
- **Equivalence**: on the shipped map, four standard farmers with no abilities under a farm-only policy bank within
  1 food per seat of `FarmSim`. This compares two models, not a target, so it survives any balance change; it is the
  guard that part 2's movement and peck model did not drift from part 1.
- **Determinism**: same seed, same result; 1 thread and 12 threads give the same totals.
- **Sight**: a hidden rival appears to an observer at its last seen position, is dropped after the observer reaches that
  spot or after `GiveUpSeconds`, and appears at its true position when Hide ends.
- No test asserts a win rate, not even a seat mirror (D7). The Seat check is a report line (section 6).
- **Statistics helpers**: Wilson interval and Kendall tau against textbook values.
- **Facts**: each new `AbilityOracleFacts` field equals the authored field it reports (read from the SO type, not a literal).
- **Builder**: a kit holding a job the simulator does not model throws, naming the kit and the job.
- The suite runs through `TestRunnerApi` from `script-execute`, as the project's test command says.

### 9.3 Build order (each slice reviewable alone)

| Slice | Content | Done when |
|---|---|---|
| S1 | `OracleNav`, `MatchSim` with movement, piles, deposit, clock, farm-only policy; time per match measured | equivalence test passes; 5.4 corrected with the measured cost |
| S2 | new `AbilityOracleFacts` fields, the two public constants, `MatchKits` | facts and builder tests; no game behaviour change (suite green) |
| S3 | combat rules: control, Peck cut, Steal, Shove, Slow, Slow Area, Egg Shell, Jump, Hide through `MatchSight`, keep-out, comeback events, tie-break | rule and sight tests; arm-wall assumption of 2.2 checked |
| S4 | Spoiler and Thief: mark pick, arm, window, Kill, removal, bag, passive bonus; Forced kills design; kill targets list (empty) | rule tests; the kills-to-win table prints raw rates |
| S5 | P0, `MatchRunner`, Mixed, One per class, Seat check, report section | the first per-kit and per-class report |
| S6 | P1, mixed population, hypercube sweep, robustness tags | the sensitivity report |
| S7 | match records and Accept, drift line, solver match term with the 7.1 guard, D8 (b) free values, problem JSON block | part 1's reference and solver tests unchanged and green |
| S8 | comparison tool for part 3 and playtest logs (section 8) | not planned; waits for BL-208's runs |

After S5 the docs follow the project lifecycle: `librarian` brings `docs/FILE_STRUCTURE.md` and the rules docs in line,
`docs/STATE.md` and the backlog are updated, and the compendium only if a value changes.

## 10. Risks

1. **The policy is measured, not the kit.** One policy plays some kits better than others (the Spoiler's combo, the third
   party). Mitigation: two policies, a mixed population, the robustness tag, and P0 tied to part 3.
2. **A wide search on a noisy model (D8 (b)).** About 24 free values and 2-point precision cost about 880 000 matches
   per solve, and a wide search can tune the model's errors instead of the game. Mitigation (7.1): common random numbers,
   the dead zone, a gain threshold of twice the paired standard error, at most 3 values moved per solve, a holdout re-run,
   batch mode.
3. **The model misses what makes the Speedy strong in playtests** (pursuit, escape geometry, human hesitation). Part 1
   already could not show it. Mitigation: continuous space, the `d` sweep, flee-success statistics, part 3 calibration;
   until then the report says the Speedy call is uncalibrated.
4. **The sight rule can change with the bots** (D2). Mitigation: `MatchSight` is the only caller of `BotSightRules`, so
   a change touches one file, and P0 keeps measuring what the bots do.
5. **Validation by means only.** Real accelerated matches are not repeatable per seed, so a disagreement needs many runs
   to show and cannot be traced to one tick. Mitigation: statistic-by-statistic comparison (8.3).
6. Smaller: P0 drifts from `BotController` as the bots change (mitigation: call `BotTactics` instead of copying it);
   the arm-wall assumption (checked in S3); the cost per match is unmeasured (S1).

## 11. Decisions

Answered 2026-11-22 (see "Maestro's answers" at the top): D1 (a), D2 deferred with the game's sight fix, D3 (a),
D4 (a), D5 (b), D6 90 / 80 / 20 % as starting values, D7 (a), D8 (b). The table below is the questions as first put,
kept for the record.

| # | Question | Options | Recommended, and why |
|---|---|---|---|
| D1 | How fine is the map in a fight? | (a) continuous 2D positions on a wall-aware path graph built from the baked walls; (b) the 13 places plus a position along each leg; (c) a fine grid | **(a)**: ranges are 5 to 6.5 m and arms bend paths by x1.52; (b) gets in-range wrong by metres and has no pursuit, which is where the Speedy plays |
| D2 | Hide has no effect in code. In the simulator? | (a) zero, plus a labelled line where attackers skip a hidden target with p = 0, 0.5, 1; (b) a perception penalty in the main numbers; (c) zero only | **(a)**: the main number stays honest to the code, and the extra line shows what Hide would be worth if players do lose sight of it |
| D3 | The per-class statistic | (a) a One-per-class design as the headline, pooled from Mixed as the second line; (b) pooled from Mixed only; (c) One-per-class only | **(a)**: "each class wins 20 to 30 %" is cleanest when every match holds one of each class; the pooled line shows the same class in the mixed world |
| D4 | Which policy gives the verdict? | (a) report P0, P1 and mixed; call a kit only when they agree, else "policy-sensitive"; (b) P1 only; (c) P0 only | **(a)**: a verdict that flips with the policy is not a verdict, and P0 alone measures today's bots |
| D5 | What "accept as balanced" teaches | (a) store the model bias; targets become 25 % plus bias; the raw band is always shown; (b) only record, targets stay 25 %; (c) also rewrite part 1's offsets from part 2's measured price | **(a)**: it learns from his tuning as he asked, without hiding a raw number or silently changing part 1 |
| D6 | The Spoiler target in numbers | (a) 4 kills: win at least 80 % without the passive; 3: at least 70 %; 2: at most 15 % and only with the passive; (b) stricter: 90 / 80 / 10 % | **(a)**: closest to "almost surely" and "huge luck" while leaving room for the rival's own play; he may tighten it after the first table |
| D7 | A drift guard for part 2? | (a) no test on rates; determinism and equivalence tests only; drift is a report line against the last accept; (b) a test pinning exact rates for fixed seeds | **(a)**: any policy improvement would fail (b), which is a gate by another name |
| D8 | What the solver may move first | (a) `LeaderPickWeight`, pile amounts, `PeckCastSeconds` per class; (b) also every ability's cooldown and radius | **(a)**: these are the values part 2 was asked to propose (BL-207, BL-210, BL-206); a wide search on a new model would tune its errors |

## 12. Verified and assumed

**Verified in code or assets this session:** `FarmSim` tick, phases, seat rotation and peck model (`FarmSim.cs`);
`OracleKit` and `OracleKits` inputs and Preview scope; `OracleMapSO` places, walls and leg tables; `OraclePaths`
visibility graph; `SneakyTables`; `OracleReferenceSO` entries, Record and Accept; `OracleSolver` score and search;
`AbilityOracleFacts` fields; the declared members of `AbilityBaseSO` and the passive hooks; `PeckCastRules.IsCut`;
`MarkPickRules` purity; `InvisibilityAbilitySO` (visual only); knockback decay (`ChickenMovement.cs:38, 94-99`) and
surge literal (`:80-83`); timer tie-break (`GameManager.cs:652-694`); bot FSM (`BotController.Think`), profiles and
`AutoPlayPersonality` (aggressive, runner, balanced; `AutoPlayBrain.cs:80-82`); `ChickenMatchStats` fields;
`MatchConfig.TickRate` 30; 12 logical processors.

**From the librarian's cited read, not re-read by me:** the 8 kits and every ability and passive number in section 3,
Peck cut details in `AbilityController.cs`, the keep-out and base-wall rules, steal clamp, execute payout and respawn.

**Verified on 2026-11-22:** `MatchTelemetry` line names and cut causes; `StealRules.Stealable` and `SplitDrain` and
`PeckCastRules.LandingTake` are pure; `MarkKillAbilitySO` authors `ArmSeconds`, `KillRange`, `FailCooldownSeconds`,
`LeaderPickWeight`; `TurtleModeAbilitySO` authors `WalkSeconds`, `SpeedMultiplier`, `EggSeconds`; the arena half-size
is not in `OracleMapSO` (the bake computes it from the scene); `BotSightRules` (7a0fc7c5) as quoted in the answers;
`BotController` passes its serialized `_arrivalRadius` to it.

**Assumed:** abilities pass through the pinwheel arms; the cost per match (2 to 5 ms) and the 10x thread speed-up;
part 3 throughput; the robustness thresholds (tau 0.8, 3 points) and validation tolerances; about 24 free values
under D8 (b).
