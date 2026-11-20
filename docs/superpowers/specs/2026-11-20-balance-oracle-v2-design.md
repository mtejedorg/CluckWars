---
status: draft
owner: Maestro
type: design
---
# Balance Oracle v2: design (part 1 of 3)

Status: draft for Maestro's approval, 2026-11-20. Nothing here is implemented.
Branch: `develop` (= `balance`). Source conversation: 2026-11-20, Maestro's rulings quoted where they bind.

## 1. Intent

The Balance Oracle (`Assets/_Game/Scripts/Balance/BalanceOracle.cs`) was built on 2026-07-25 to solve class stats
against the Solo Clear Time axiom. It no longer helps, for twelve reasons (section 3). Maestro wants an oracle that:

1. can be used to balance the remaining values once he has fixed some values of each chicken by hand;
2. tells him roughly how balanced the game is;
3. has an answer for the chickens that do not forage (Spoiler, Thief), which today have no guard at all;
4. is **a guide, not the law**, and learns from a manual tuning he prefers
   ("el oráculo no es la ley, sólo una guía, que de hecho debería aprender de cuando manualmente se encuentre un ajuste mejor").

The work is three projects. This document designs the first and only outlines the other two:

| Part | What | Document |
|---|---|---|
| 1 | Oracle v2 (deterministic, instant) plus the game-rule changes it is solved together with | this one |
| 2 | Abstract match simulator: 4 scripted players, thousands of matches, statistics | own design, after part 1 |
| 3 | Real-game bot runs (accelerated Fusion clock if possible, parallel instances if not) to calibrate part 2 | own design, starts with a clock spike |

## 2. Goals, non-goals, anti-goals

**Goals**
- Measure farming as it happens in a match: four chickens at once, real walls, several sensible routes, the kit's own tools.
- Solve pile amounts and farming stats together, with Maestro choosing which values are fixed.
- Give every one of the eight kits a number, the non-foragers included.
- Report numbers and gaps; never block a change.
- Work in a player build (the local balance editor), not only in the Editor.

**Non-goals**
- Better bot AI in the real game. Win-rate statistics (part 2). Real-game runs (part 3).
- Changing class identity (Speedy never jumps, the Sneaky never forages, the Warrior is the all-rounder).
- Progression, UI of the menus, anything on `feature/progression`.

**Anti-goals** (a failure even if it works)
- An oracle that turns a design idea red and stops it. v1 did this: Featherfoot's banking bonus was capped near x1.13 by the gate (BL-128).
- A fixture or literal copied by hand from the game. Twice this hid drift (+20 % speed in August, 5.5 to 7.3 s of map drift in October).
- A number in a doc that the tool does not print. The GDD carries stale figures already.
- Statistics presented as truth. Every output states what it does not model.

## 3. Why v1 does not work today

**A. It measures a match that no longer exists**
1. One chicken alone with all 80 food. With four players the share is 20 and the goal is 40, so nobody wins by foraging; the targets 30 / 30 / 35 s describe something that never happens. Maestro: this is the most important one, and it is also a design problem (a Fatty carrying 40 can never fill up, which is frustrating).
2. A naked chicken. Since the fixed kits (2026-10-31) nobody plays without abilities.
3. Only three passive effects are seen (cargo, deposit rate, Peck cadence). Bully, Relentless and Slippery come out identical to the naked class.
4. No interaction: no steal, stun, execute, base wall, comeback. The Warrior's +5 s was Maestro's price in seconds for being able to fight; nothing shows what that price buys.
5. Two of eight kits are exempt. The "Predation axiom" (2026-08-13 spec, 7.2) was a sentence with two unknowns (cargo C met every T seconds) that was never computed or coded, and it predates the Thief.

**B. The model is too simple**
6. Straight lines. The eight pinwheel arms are ignored, and so is the fact that Jump crosses them while the two best foragers cannot jump on purpose.
7. One route. "Nearest pile, return when full" is a logical route and stays, but it is not the only one, and alone it produces artefacts: the Fatty's 30.05 s comes from walking pile to pile round the ring and returning from the opposite corner. It also creates plateaus (Warrior cargo 16, 17 and 18 give the same time).
8. Movement details: the pile slow (x0.8), bumps.

**C. It is a lock, not a tool**
9. One output: pass or fail at +-0.5 s. It freezes numbers.
10. It solves nothing. In practice only `PeckCooldown` was hand-adjusted until the test went green.
11. No measure of balance.
12. Never calibrated against real matches; the map is readable only in the Editor, so in a player build it blocks instead of checking.

## 4. Approaches considered

| Approach | Trade-off | Verdict |
|---|---|---|
| Patch v1: add walls, the kit, a predation formula | Cheap. Still one chicken alone, still no measure of balance | Rejected |
| Oracle v2 (deterministic) plus an abstract match simulator | Medium cost. The simulator's results depend on scripted policies, so it needs calibration | **Chosen by Maestro** |
| Only the real game with bots | Most faithful. 45 s of real time per match unless Fusion's clock can be advanced; measures the bots' AI as much as the rules | Kept as part 3, for calibration |

Maestro asked for both simulators ("quiero ver el simulador abstracto, así que haz los dos").

## 5. What part 1 builds

### 5.1 The map as data: `OracleMapSO`

A baked asset holding: the four bases, the nine piles (position, footprint, yaw, amount), the wall segments, and the
**walk distance between every pair of places** with walls (a second table for chickens whose kit has Jump).
An Editor menu bakes it from `Game.unity`, `MapGenerator` and `PinwheelLayout`; a test fails when the bake no longer
matches the scene, so it cannot go stale like the July fixture did. The asset ships in the build, which removes the
"no map data in this build" block of `BalanceSctCheck`.

Measured in the spike (section 8): from a base, the personal pile and the centre have no detour; of the two
neighbouring contested piles one is free (x1.04) and one is behind an arm (x1.52). The pinwheel gives every base an
easy and a hard contested pile, and a player's hard one is the neighbour's easy one.

### 5.2 The farming simulation

Deterministic, fixed tick (the game's 30 per second), pure C#, no Fusion, one to four chickens at once sharing the piles.
Each chicken is its **kit**: class stats, the passive through its real hooks, and the abilities that change a farming run
(Speed while travelling, Jump through the jump distance table, the Peck cast of section 6.2). Values are read from the
shipped assets through `BalanceBook`, so a proposed delta can be measured before it is applied.

**Routes.** A named set, each a simple rule: nearest pile first; side piles first, own pile last (Maestro's Speedy
rush); centre first; easy side then centre. The oracle reports, per kit and seat, the best route, the mean and the
spread. A large spread is itself a finding (in the spike the same Hoarder went from winning in 18 s to not reaching 40).

### 5.3 The measures

All in the same currency: seconds to bank the win target, or food banked when the clock ends.

| Measure | Scenario | Captures | Arrives with |
|---|---|---|---|
| Economy | The kit only forages, with three standard farmers on the map | speed, cargo, Peck, walls, routes | part 1 |
| Attack | The kit uses everything it has against three standard farmers | steal, stun, execute | part 2 (needs the interaction rules) |
| Resilience | The kit forages while a standard predator hunts it | Egg Shell, Slippery, escape speed | part 2 |

The **standard farmer** is a reference chicken that forages and banks and never fights or flees. For a non-forager the
Attack number is its Solo Clear Time: the cargo of its victims and how often it meets one come out of the farmers'
cycle instead of being playtest unknowns. A second figure, **denied food**, says how much the farmers failed to bank.
Maestro's reaction to the three measures: "rebuscado, pero probemos". If one adds nothing it is removed.

**The Spoiler table (part 1, arithmetic).** Maestro's rule: balance the Spoiler by how many kills without cargo, plus the
passive, win a match. Targets: four kills win even without the passive, counting some cargo; three almost always win;
two win only with a great deal of luck plus the passive. The oracle prints, for 0 to 4 kills and for empty and loaded
victims, what the Spoiler ends on against the farmers' banked food. With today's values (5 per kill, +15 at the clock,
at most 4 kills per round) that is 20, 30 and 25 plus stolen cargo against farmers ending near 30 to 35. The passive
pays only if nobody reaches 40, so the Spoiler must also stop whoever is about to.

### 5.4 Maestro's price in seconds, kept and made visible

Each kit has an **offset in seconds**: the part of its value that no measure captures. It is the number Maestro already
tuned in his head for the Warrior. The report shows, per kit: the measured numbers, the offset, and the result.

### 5.5 The solver

Maestro marks values as fixed or free. The solver searches the free ones inside their authored ranges (`[Range]`,
`[Min]`, the same limits `tools/balance/schema.py` validates) to bring every kit as close as possible to the targets,
across seats and routes, and writes the result as a balance proposal for the `/balance` flow. It proposes; it never
applies. Free values in the first use: pile amounts, the Peck cast and cadence per class, Fatty cargo.

### 5.6 A guide that learns

- No design gate. `SctTargets`, `SpecSctTargets` and the two +-0.5 s tests are retired. The local balance editor's
  SCT check reports and no longer blocks a proposal (a change to the 2026-11-18 behaviour).
- **Accept as reference.** When Maestro likes a state, tuned by hand or by the solver, one action stores the
  measured numbers of every kit with the date and a note. From then the offsets of 5.4 are recomputed from what he
  accepted: his tuning teaches the oracle what each kit is worth.
- One test remains: the measured numbers still equal the last accepted reference. It catches a change nobody meant
  (a map edit, a stat drift) and its message says "accept the new state or find what moved", not "re-solve".

### 5.7 The report

A text and JSON report from an Editor menu and from batch mode: per kit the measures by seat and route, the offset,
the Spoiler table, the spread, and a list of what the run did not model. A page for it is a later step.

## 6. Game-rule changes solved together with the oracle

Each is data-driven and is committed alone so it can be reverted.

### 6.1 More food, light scarcity

Maestro's rules: enough food that a forager feels it can win by foraging; own pile plus one contested pile must NOT be
enough; own pile plus two contested must be; fighting over the centre must matter.
Starting point for the solver: **personal 10, contested 15, centre 30** (total 130, was 80; own side 25; own plus two
contested exactly 40). In the spike four equal farmers end between 28 and 36 at 45 s with it, on every route.
Pile amounts are fields of `MapGenerator` in `Game.unity` today; they must become balance data.

### 6.2 Peck is a cast

Today a Peck takes its food at the press and then locks movement for 0.4 s. Maestro: "peck debería ser un casteo que si
no llega al final, se cancela", and the Fatty should take longer, which makes it vulnerable to a stun and gives the
Warrior a job. New rule: the food is taken when the cast ends; a cast that is cut takes nothing. What cuts it is decision D2
(proposal: a stun, a shove, an execution, and the chicken's own next move as today; a slow or being robbed does not). The cast length is a class stat; the oracle proposes the values. This is also the counterweight to the
Hoarder, who in the spike wins in about 18 s once food is plentiful (one trip, 14 of those seconds are pecking and
banking, so walls barely touch it).

### 6.3 The Spoiler's payout and mark

- Every kill pays the same. Correction found in review: `LeaderExecuteBountyMultiplier` (x2) is not a standing rule.
  It applies only while the `LeaderBounty` comeback event is on (one of four events, drawn at random for the last 10 s:
  Golden Pile +25 food, Underdog Surge, Leader Bounty, Restock +10 on every pile). Removing the multiplier leaves that
  event empty. **Open for Maestro (D7):** drop the event, or keep it with a new meaning (while it is on, the mark always
  falls on the leader).
- The mark pick gains a weight toward the leader, next to the two it has (far from its own base, far from the Assassin).
- Reserve, not built now: if the simulators show the Spoiler never wins, a kill becomes possible inside the base, so
  the answer to a mark is always to flee.
- Already true, no work: both Sneaky kits carry what they take in a bag, bank it at their base, and can be robbed of it.

### 6.4 What ships when (my call)

Part 1 **models** the Peck cast and the new pile amounts in the oracle and proposes their values. Putting the cast into
the networked game is its own item straight after (class stat, authority-side cancel, bots, HUD, `ux-designer` review):
it is the largest gameplay change here and should not hide inside a tooling batch. The pile amounts and the Spoiler
payout are data changes and go in with part 1.

## 7. Constraints

- Balance code lives in the Gameplay assembly, pure and Fusion-free; the Editor bake in the Editor assembly.
- Values come from assets through `BalanceBook`; the deny list is unchanged unless a rule above needs a new field.
- The compendium (`docs/site/index.html`), the GDD sections on the SCT axiom, `CODE-DESIGN-01/02` and the
  mechanics-designer's instructions describe v1 and must be brought in line when part 1 lands (`librarian` owns the rules docs).
- Peck as a cast changes feel and feedback: `ux-designer` and the feedback rules apply before it ships.

## 8. Evidence: the throwaway calculation of 2026-11-20

A Python model (not in the repo): four farmers at once, the eight pinwheel arms with shortest paths, reach-aware legs,
discrete pecks, three routes. Not modelled: abilities, the pile slow, scatter cover, any fighting.

| Finding | Numbers |
|---|---|
| Today (5 / 10 / 20) nobody gets near the goal | everyone ends on 14 to 26 at 45 s |
| 10 / 15 / 30 gives light scarcity among equals | 28 to 36 at 45 s, every route |
| A big cargo dominates once food is plentiful | Hoarder wins in 18 s on the nearest route; 26 to 28 s or not at all on others |
| Walls cost the Speedy, not the Hoarder | Speedy alone on today's map 26.8 s without walls, 29.4 s with; the Hoarder's single trip crosses no arm |
| 10 / 20 / 40 (total 160) is abundance | every class wins in 23 to 26 s |

Maestro reports the Speedy as the strongest kit in his playtests. This model does not show that, so the cause lies in
what it leaves out (the Speed ability, pursuit, nobody catching it): a calibration target for parts 2 and 3.

## 8b. Calls made after the builder check (mine, for Maestro to correct)

- **What the solver aims at.** One number per kit: food banked at 45 s with the goal switched off (so a fast kit is not
  cut at 40), shown also as seconds to 40 when it gets there. Target: every kit lands on the same number once its offset
  is applied; the offset in seconds is converted at the kit's own income per second. Before any reference is accepted
  the offsets are Warrior +5 s and 0 for the rest (today's targets). The pile rules of 6.1 are hard limits for the
  solver, the 28-to-36 band is a target.
- **Comeback events.** Every number is given with the events off, plus one variant per food event (Golden Pile,
  Restock), since they change the last 10 s.
- **The Thief in part 1.** It gets an arithmetic table like the Spoiler's (steals needed against the farmers' cargo
  cycle, with its x1.6 and its 10 cargo), so all eight kits have a number before part 2.
- **Player build.** Only the measurement runs there. The solver and "Accept as reference" are Editor-only, and the
  reference is a committed asset.

## 8c. Maestro's answers, 2026-11-20 (decision page https://claude.ai/artifact/RER4Q4kcStmogARww4ACQW)

- **D1b food:** personal 10, contested 15, **centre 40** (total 140). This replaces the 10 / 15 / 30 of section 6.1 as the solver's starting point.
- **D2a Peck cast:** cut by a stun or a shove (and by an execution or the chicken's own next move, as today); a slow or being robbed does not cut it.
- **D3a:** the local balance editor reports the oracle's numbers and lets the proposal through.
- **D4b standard farmer:** a Warrior with no abilities (replaces the "mean of three classes" proposal in section 9).
- **D5a + b balanced:** each of the 8 kits wins 20 to 30 % of mixed four-player matches, AND the same statistic per class; he wants both.
- **D6b order:** the Fusion clock spike first, then part 1, then the abstract simulator.
- **D7b:** the `LeaderBounty` comeback event is removed (three events remain). His reason: an event that only matters to the Assassin makes no sense.

## 9. Left to the builder

- The shape of the kit and route types, the solver's search method, the report format.
- Where the pile amounts live as balance data and how the delta schema carries them.
- How the cast interacts with the auto-Peck chain and the cadence stat.
- The standard farmer's exact stats (proposal, my call: the mean of the three foraging classes, stored in the reference).
- The value of the leader weight in the mark pick (the oracle and part 2 propose it).

## 10. Open for parts 2 and 3

- Fusion's clock in solo mode with bots (Task 0, measured 2026-11-20, Windows dev-tools build, 3 rounds per speed): **works up to x5**. `-cw_timescale N` (solo only, dev tools) gives the runner its own config copy with `SimulationTimeMode.DeltaTime` and sets `Time.timeScale`; the shipped config is untouched. Measured tick rate 32.0 / 160.4 / 320.1 / 640.5 ticks per real second at x1 / x5 / x10 / x20 (the tick rate is 32 Hz, not 30), a 45 s round plus 3 s intro takes 48.0 / 9.2-9.6 / 4.8 / 2.4 s real. x5 has no new log error. x10 and x20 keep the clock right but the round-start `Invariant` probe fails (74 and 83 Error lines, `timer`, `respawn`, `pile-full`, `cargo-zero`: it samples the world a real-time delay after the round began, so the sim has already run seconds), and the fake-player seat banks a different amount than at x1 (it steers once per rendered frame). So x5 is the honest ceiling for measurements that look at the log; a pure headless sim (`FarmSim`) needs no clock at all. One catch found on the way: the execute hit-stop ends by writing `timeScale = 1`, which silently dropped a run back to real time; the service now puts the flag's value back.
- What "balanced" means as a statistic. Proposal not yet answered by Maestro: each kit wins 20 to 30 % of mixed four-player matches.
