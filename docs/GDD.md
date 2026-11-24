# Game Design Document v0.4

**Version:** 0.4
**Date:** July 2026
**Status:** Redesign implemented (control + steal, axiom-driven balance)

**Changelog v0.4 — the core redesign.** Deleted HP and damage entirely; combat is now
**control + steal**. Introduced the **Solo Clear Time (SCT) axiom** — every stat is
*derived* from a per-class clear-time target, not hand-tuned. Win target 150→**40**, match
3-min→**45 s**. Tiered map food budget (**140**, since 2026-11-20; 80 = 2× win before) with pinwheel walls. New Assassin
**Death Mark execute** (the only hard removal). Ability pool split into **Steal / Control /
Defense / Utility** with an `AllowedClasses`-gated model (shared / class-only / pre-equip-only). Targeting collapsed to
**self-centred or directional** (one exception: Death Mark). Full rationale in
`docs/superpowers/specs/2026-07-24-cluck-wars-core-redesign-design.md` and
`…/2026-07-25-ability-pool-rewrite-design.md`.

**Update 2026-11-05 (Maestro) - simplified abilities.** The ability set was too complex for a children's game: the registry now holds **10 active abilities, one per job** (Steal, Stun, Shove, Slow, Speed, Jump, Shield, Hide, Peck, Death Mark; Quick Drop was retired later the same day and became a Featherfoot passive, section 5.3), and **every targeted ability hits every rival within its radius of the caster, by distance only** (range-only targeting, §6.1). The eight fixed kits were rewritten (§5.3). Retired moves are marked retired rather than deleted from history. **Later the same day (Maestro):** the Warrior became the all-rounder (Bully and Relentless reworked, outgoing crowd-control multiplier, §5.3), Shield became a slow-immune walk followed by an automatic egg (§7.2), Quick Drop was retired in favour of a permanent Featherfoot banking bonus, and Death Mark's kill range went from 8 m to 6.2 m (§6.4). **Later still (Maestro, 2026-11-05):** shorter cooldowns in three bands (low 1-3 s, medium 4-6 s, high 7-8 s, §7.3), Speed 1.3x, Hide 2 s, three kit swaps (Featherfoot Shove for Steal, Thief Slow for Shove, Spoiler Hide for Slow, §5.3), Slippery resists ability slows only (§5.3, §6.3) and the Assassin moves at 11.1 (§5.1).

*Earlier: v0.3 removed the basic attack; v0.2 added the 2.5D visual direction. See git
history for the pre-0.4 document.*

---

## 1. Overview

**Game Title:** Cluck Wars
**Genre:** Competitive multiplayer arena — fast, chaotic resource collection
**Platform:** Multiplatform — Mobile (primary), PC, Tablet
**Players:** 4, free-for-all (no teams)
**Perspective:** Isometric top-down — 2.5D real 3D meshes, orthographic camera
**Session Length:** **~45 seconds per match** (target 45; hard cap on rage-quit value)
**Target Audience:** Casual to mid-core, mobile-first

**Elevator Pitch:**
Four chickens, one farm, forty-five seconds. Collect food, bank it at your base, and rob
everyone else blind before the clock runs out. No health bars, no grinding a rival down —
every fight is about *taking their food and buying time*. Easy to learn, chaotic to master,
over before anyone can rage-quit.

**Why 45 seconds.** A mobile FFA lives or dies on session length. At 45 s a loss costs
nothing, a comeback is always one steal away, and the game is inherently replayable. Every
system below is tuned against this number.

---

## 2. Core Game Loop

```
PICK class + passive + abilities → SPAWN at your base corner → farm the tiered piles
→ CARRY food home → DEPOSIT at base → REPEAT
                         ↕
   STEAL from loaded rivals · CONTROL them (slow/stuck/stun) · DEFEND your haul
```

**Win condition**
- First to bank **40 food** at base wins immediately, OR
- Most food banked when the **45 s** timer expires (tie-break: food → kills → lower corner
  index).
- **Ties at the top (Phase 7).** The corner-index rule only decides the stored winner (stats);
  the player is never told a coin-flip won. If food is level at the top and KOs do not settle it,
  the end screen reads **"TIED AT THE TOP!"** ("Share the crown!") and **every tied-1st bird is
  crowned**; there is no "{name} WINS!". If the tie is settled by KOs the banner stays
  "{name} WINS!" with "Tied 1st · won on KOs". A 4-way tie at the top shows three birds on the
  podium (the standings board lists all four).

**Comeback event — the last 10 s** (`MatchConfig.ComebackEventSecondsLeft` = 10). Once per
round, when 10 playable seconds are left (never during the 3-2-1-GO intro), the authority rolls
one of three events and the HUD shows **"FINAL 10!"** over its name: **Golden Pile** (a 25-food
pile near the centre), **Underdog Surge** (the lowest-banked chicken moves ×1.4) or
**Restock** (every pile +10, up to its max). All three end with the round: PLAY AGAIN despawns the
golden pile and clears the surge flag. A fourth event, Bounty on the Leader (execute pays ×2 on the
top-banked chicken), was removed on 2026-11-20 (Maestro, D7b): an event that only matters to the
Assassin makes no sense; the leader is now favoured by the mark pick instead (§6.4). The event used to be a hard-coded T-60 s, which in a
45 s match fired at GO; round-2 decision 2 (2026-10-07) moved it to the last 10 s. Note: Golden
Pile and Restock add food beyond the §3.2 budget of 140 — a deliberate late-match exception.

**No death.** There is no HP and no damage. A chicken is never "killed" except by the
Assassin's execute (§6.4), which removes it for 3 s and respawns it empty at its own base. Every other
interaction is a **steal** or a **control effect** — you take food or buy time, you never
grind someone down.

### 2.1 The Solo Clear Time (SCT) axiom — the balance backbone

> A **naked** chicken (no abilities, no passive), **alone** on the full map, farming
> greedily, banks the win target in a fixed **time** via a fixed number of **base trips**.

Every stat is a *dependent variable* measured against SCT — we don't pick `MoveSpeed` because
9 feels nice, we pick it because Speedy must clear in 30 s. Since 2026-11-20 this is measured by
**Balance Oracle v2** (`Assets/_Game/Scripts/Balance/`, menus under `Cluck Wars/Balance/Oracle/`),
which **reports and proposes; it never blocks a change**. The old pass/fail SCT gate (±1.5 s design
band, ±0.5 s drift guard) and `BalanceOracle` were deleted. The oracle simulates four-chicken
matches on the shipped map (baked from the scene into `OracleMapSO`), every kit against three plain
Warriors, with the wall-aware walk distance between places in `OracleMapSO`. It reports per kit the food banked at 45 s with
the goal switched off, the seconds to 40, and the range over the four seats; with a Golden Pile and
with a Restock too. Nobody fights in it: no steals, stuns or executes.

**The oracle is a guide, not the law (Maestro, 2026-11-20).** It reports and learns from his manual
tunings: *Record baseline* stores what the shipped assets measure and keeps the kits' prices
(offsets in seconds; the Warrior kits +5 s today, the rest 0); *Accept as balanced* recomputes the
prices so every kit's result is equal. The only oracle test that can fail is the drift guard
`OracleReferenceTests.Measured_EqualsTheAcceptedReference`: the numbers moved away from the accepted
reference, so either accept the new numbers or undo the change. **"Balanced"** means both (Maestro,
design spec 8c, D5a and D5b): each of the 8 kits wins 20 to 30 % of mixed four-player matches, AND
the same statistic holds per class. The match simulator that measures this is part 2 (BL-207); today
the oracle measures the economy only. The solver aims four plain Warriors' banked food at 28 to 36
(`bandLow`, `bandHigh` of its problem file) and the kits' results at each other. The *Solve* menu proposes free values as a schema-1 balance delta and never
applies it.

The targets below are the original SCT design intent, kept for the identity of each class; the
oracle report, not this table, says where the shipped assets are.

| Class | SCT | Trips | Strategy |
|---|---|---|---|
| **Speedy** | 30 s | 4 | Throughput — many small loads, roams wide |
| **Fatty** | 30 s | 2 | Payload — one huge sweep, barely returns |
| **Warrior** | 35 s | 3 | The ability class — mid stats, leans on his kit |
| **Assassin** | 40 s | 4 | Denial — never actually farms; robs and executes |

`WinTarget = 40 · TotalMapFood = 140 (§3.2; was 80 = 2× win) · trips = ceil(WinTarget / CargoCapacity)`.

At 3.5× supply, two players *could* max out by pure farming — so combat's job is to **buy
time**, and **steal** is the only mechanic that converts a won fight directly into food.

---

## 3. Map Design

> **v0.5 — locked 2026-07-29.** This section supersedes the earlier 30 m pinwheel arena
> and **replaces ADR 0003's Low/Standard/Tall mobility triangle** with length-based jump
> traversal (§3.5). ADR 0003 Decisions 1 and 5 should be marked superseded.

### 3.1 Arena geometry

**Square arena, 38 × 38 m** (53.7 m corner to corner), isometric top-down. Four bases,
one per **corner**. Four edges, each the route from one base to a neighbour.

Every client **rotates the arena so its own base sits at the bottom of the screen**. All
players therefore see an identical relative layout — no starting corner is easier to read
than another, and the arena renders as a diamond with your corner nearest the camera.

> **Movement input must be camera-relative.** Today `ChickenMovement.Tick` applies input in
> raw world space (`new Vector3(input.x, 0, input.y)`), which only works because every
> client shares one fixed 45° yaw. Per-player rotation makes that a bug: each player's
> "up" would map to a different screen direction. Input must be rotated by the local
> camera yaw before it reaches movement. **This is a hard prerequisite of per-player
> rotation, not an optional polish item.**

| Feature | Radius from centre | Notes |
|---|---|---|
| Centre pile | 0 | span **12 m**, never moves |
| Hub plaza edge | 10 | walkable ring around the centre pile |
| **T2 — contested** | 15 | at the four **edge midpoints** |
| **T1 — doorstep** | 17 | on your corner diagonal |
| Base mouth | ~25 | 3 m opening |
| Corner (base apex) | 26.9 | base wraps the corner |
| Edge midpoint (boundary) | 19 | |

The **boundary is a hybrid** — straight along each base frontage, curving through the
corners — and is delimited by **props** rather than a uniform wall, so the arena reads as
a place rather than a box. Prop placement is art-tunable without touching the generator.

**Piles do not jitter (Phase 8 D9, 2026-10-15).** Every pile sits on its nominal spot
(`MapGenerator._pilePositionJitter` = 0), concentric with its baked zone disc. The old
independent ±0.4 m offset per pile gave the four seats different round trips every match
(up to ~0.8 m to the doorstep pile and ~1.5 m summed over the two contested piles; Balance
Oracle SCT spread up to ~0.4 s between seats). If a nudge is ever turned back on it is ONE
random vector per match, rotated 90° per corner (`MapGenerator.FourFoldNudge`), so the seats
stay exact rotations of each other. The centre never moves.

**The arena is a baked scene, not runtime geometry.** Because the layout is now fixed
(§3.4), the ground, boundary and walls live in `Assets/_Game/Scenes/Map.unity`, loaded
additively at startup — so the level can be opened, inspected and hand-tuned like any
other asset instead of existing only while the game runs. `Cluck Wars/Map/Bake Map Scene`
regenerates it from `MapGenerator`'s authored tuning; `Cluck Wars/Map/Open Map Scene`
just opens it. Untick **Use Baked Geometry** on `MapGenerator` to fall back to generating
procedurally at runtime, which is what a future randomised map would use.

Only *static* geometry is baked. Player bases and food piles stay runtime `Runner.Spawn`s —
they are `NetworkObject`s and Fusion must own their spawning.

### 3.2 Tiered food budget (total = 140; pile food is balance data)

| Tier | Count | Food each | Σ | Role |
|---|---|---|---|---|
| **T1 — doorstep** | 4 (one per base) | 10 | 40 | Safe income, ~8 m off your mouth |
| **T2 — contested** | 4 (edge midpoints) | 15 | 60 | Equidistant from two bases — contested arrival is the default |
| **T3 — centre** | 1 | 40 | 40 | The prize, and the wall protecting it (§3.6) |

Since 2026-11-20 the three amounts are fields of `MatchConfig.asset` (`PersonalPileFood`,
`ContestedPileFood`, `CentrePileFood`), not constants of the map generator; a balance delta can move
them. The rules they must keep (`FoodRules`): your own pile plus one contested pile is **not** enough
to win, your own pile plus two contested piles is. (The old 5 / 10 / 20, total 80, broke the second.)

**Piles do not respawn or regenerate** — banked food leaves circulation, so the pot
visibly shrinks and the late game gets desperate on its own. Fatty's winning route (a
35-cargo sweep of centre + two contested piles) is forced *through* the contested ring — he
cannot win without walking past everyone.

### 3.3 Scale package

Every spatial constant derives from these. **Jump distances are defined as a share of
screen width**, so they rescale automatically if the camera changes and never need
re-tuning against the map.

| | Value | Derivation |
|---|---|---|
| Chicken | **⌀0.8 m × 1.6 m** | 4.0 % of screen width |
| Move speed | **9.0 / 10.5** | base / fast classes |
| Camera | orthographic, **orthoSize 5.6**, 45° yaw, 30° pitch | |
| Ground visible | **19.9 m across × 22.4 m deep** | `3.56·o` × `4·o` — deeper than wide, because 30° pitch stretches the vertical axis 2× |
| Min corridor | **2.0 m** | `2 × chicken ⌀ + 0.4` |
| Wall opening | **3.0 m** | |

The arena is ~1.9 screens edge-to-edge and ~2.7 corner-to-corner: **you never see the
whole map**. That information gap is deliberate — it is what makes routing and ambush
matter.

### 3.4 Sector & wall topology — the two laps

Eight radial walls sit on the boundaries of eight 45° sectors, alternating **base sector**
(centred on a corner) and **neutral sector** (centred on an edge midpoint). Walls are
**0.7 m thick** (0.5 until 2026-08-19), and each has exactly **one opening, at one end**:

> **The radii in the table below are the pre-rescale 38 m arena's.** The shipped arena is
> 51.3 m since 2026-08-14; the *derivation* is unchanged, so the live figures are the same
> formulas at `arenaHalfSize = 25.65`: hub plaza **10**, opening **3**, both wall types
> **14.76 m** long, **118.1 m** of interior wall in total. Do not read the absolute numbers
> here as current — read the relationships.

| | Spans | Length | Opening |
|---|---|---|---|
| **Outer-gap wall** (4) | r 10 → 17.6 | 7.6 m | 3 m at the **rim** |
| **Inner-gap wall** (4) | r 13 → 20.6 (boundary) | 7.6 m | 3 m at the **hub** |

Wall directions sit 22.5° off each diagonal, so on a square of half-extent 19 m the
boundary along a wall's bearing is at r = 19 / cos 22.5° = **20.6 m**.

Nothing here is hardcoded: the hub plaza is `max(10, centreKeepClear + MinCorridorWidth)`
so a growing centre pile can never seal the hub, and each wall's outer end is measured
against the boundary along *its own* bearing. Both wall types come out the same 7.6 m
length, which is a consequence of the derivation rather than a tuned value.

**Walls are deterministic — they do not jitter.** Piles sit at sector centres and walls on
sector boundaries, which is the widest separation this topology allows: at the contested
pile's radius (20.2507 m) the wall LINE passes 7.7496 m from the pile centre against a
4.0936 m keep-clear disc, leaving **+3.656 m of slack**.

> ⚠ **Corrected 2026-08-19.** This paragraph previously claimed *~0.15 m of slack*, and so
> did `PinwheelLayout.JitterFraction`'s comment. That figure was computed against the
> **pre-shrink** contested footprint (6.5 × 5.4, disc 5.585) at the **pre-rescale** radius
> (15 m, separation 5.740) — a map that stopped shipping on 2026-08-13. Against what
> actually ships the margin is **24× larger**. The historical measurement below (27 % of
> walls collapsing under jitter) was taken on that same superseded map and should be
> re-measured before it is relied on again.

Measured at the time: any wall jitter swung a wall into a pile's disc and got clipped away —
27 % of walls collapsed entirely and the rest shrank from 7.6 m to 1–3 m, destroying the
two-lap topology. Match-to-match variety used to come from **pile jitter only** (±0.4 m,
removed in Phase 8 D9 as a fairness bug, see §3.1), and — now the primary reason — a fixed
layout makes the map **learnable**, which the two-lap rule above depends on.

Two consequences worth keeping in mind if this is ever revisited:

- Should jitter ever return, it must repeat with **4-fold period** — a 90° rotation maps
  each wall onto another, so those must share a value or the four players' sectors differ,
  which is a fairness bug in an FFA, not a cosmetic one.
- Pile keep-clear discs scale with pile footprint + `max(jitter, PileKeepClearSlack 0.4)`
  (the 0.4 is pinned so the shipped arms re-bake identically with jitter 0), so **growing a
  pile eats the walls**. `PinwheelLayoutTests.Build_WallsSurvivePileKeepClearDiscs_AtRealV05Sizes` guards
  this; clearance tests alone cannot, because clipping shortens a wall without ever
  violating corridor clearance.

**Eight wedges is a proven global optimum, not a round number.** Wall bearings are
`(i + 0.5)·360/W`; every pile and base sits on a multiple of 45° (piles at sector centres,
bases on the corner diagonals). The offset between a wall and the nearest objective is
therefore a pure function of `W`. The binding constraint is the contested pile: its
keep-clear disc is 4.0936 m at r = 20.2507, so a wall must sit at least
`asin(4.0936 / 20.2507)` = **11.665°** off the objective bearing or it gets clipped away.

| W | Best offset from a 45° multiple | Verdict |
|---|---|---|
| **8** | **22.500°** — the maximum the topology allows | ✅ clears the requirement by 10.8° |
| 12 | 0.000° — four arms land **exactly** on the corner diagonals | ❌ |
| 16 | 11.250° | ❌ misses by 0.415° |
| 20 | 0.000° — four arms land **exactly** on the corner diagonals | ❌ |

So **wedge count is not a density lever**: raising `W` adds arms that the objectives then
delete. Interior wall LENGTH is tuned through the hub plaza and opening width; interior
DENSITY is added with scatter cover (`SectorScatter`). Pinned by
`PinwheelLayoutTests.WedgeCount_Eight_IsTheGlobalOptimumForObjectiveBearingOffset`, which
fails with the reason in the message rather than letting a plausible-looking map ship.

The pattern is 4-fold symmetric, so **every player's sector is identical**: an outer-gap
wall on one side, an inner-gap wall on the other. Combined with per-player rotation this
becomes a universal, learnable rule:

- **Left → the safe lap.** Out to the rim, along the edge, past a T2 pile, to a neighbour.
- **Right → the risky lap.** In past the hub plaza and the centre pile.

Inner walls terminate exactly where a base's frontage ends, so wall and base meet at a
seam rather than colliding.

Boundary props are never crossable — arena containment is structural.

### 3.5 Traversal by jump length

**All jumps are teleports.** There are no obstacle height classes. Whether a jump clears
something depends only on **length versus the obstacle's span along the jump direction**.

- **Resolution:** the landing point must be clear of colliders and inside the arena. For
  convex obstacles this is exactly equivalent to "did I span it?", so no swept-volume test
  is needed — cast to the landing point and validate it.
- **Span needed** = obstacle width + **0.8 m** (0.4 m body clearance each side).
- **Tolerance:** if the landing point is blocked but clearing needs only slightly more,
  extend the jump. Allowance = `max(12 %, 0.4 m)`, **capped at 0.8 m absolute** so long
  jumps don't quietly gain reach. Balance against *effective* length, not nominal.
- **Failure:** travel as far as possible and **stop flush against the near face** — you
  slam into it rather than failing in place. **Cooldown is still spent.**

| Obstacle | Span | Needs | short **5 m** | normal **10 m** | big **18 m** *(future)* |
|---|---|---|---|---|---|
| Wall, square on | 0.5 m | 1.3 m | ✅ | ✅ | ✅ |
| Wall, oblique 20° | 1.5 m | 2.3 m | ✅ | ✅ | ✅ |
| T1 pile | 5.5 m | 6.3 m | ❌ | ✅ | ✅ |
| T2 pile | 6.5 m | 7.3 m | ❌ | ✅ | ✅ |
| Centre pile, full | 12 m | 12.8 m | ❌ | ❌ | ✅ |

This yields a clean ladder — **5 m is the gap-closer** (every wall, no pile), **10 m is the
pile-vaulter**, and only a big jump crosses a full centre pile.

Two behaviours emerge for free and are intended: crossing a near-circular pile **near its
edge is a shorter chord** than through its middle, and a wall taken **obliquely costs more**
than square on. Both are invisible to the player, so a **landing-point indicator is
required** — a ghost marker that reads red when the jump will fall short.

### 3.6 The ring becomes a circle

The centre pile's footprint already shrinks with its remaining food. Since span is the only
thing gating traversal, **the map opens itself in stages, with no extra systems**:

| Centre pile | Crossable by | Feel |
|---|---|---|
| 12 m (full) | big jump only | a fortress |
| 9 m (75 %) | normal jump | the siege breaks |
| 6 m (50 %) | normal jump; still blocks 5 m | contested crossing |
| 4 m (25 %) | everything | wide open |

Late-game acceleration for free: the prize everyone is fighting over is also the wall
protecting it, so the arena dissolves exactly as the match converges. **How fast it opens
is a balance-critical tuning curve** on the pile's shrink rate — not cosmetic. A linear
footprint would end the fortress phase at ~75 % remaining, which is likely too early.

### 3.7 Sightline contract

These relationships are the reason the arena is 38 m and not larger. Any change to arena
size, pile radii, pile spans or `orthoSize` **must re-check all four**:

| | Holds because |
|---|---|
| Centre **hidden** from base | 19 m to its near edge vs 11.2 m view depth |
| Centre **clips into view** from T1 | 11.0 m vs 11.2 m — deliberately marginal |
| Centre **visible** from T2 | 9 m |
| T2 **not** visible from the base mouth | picked up a few steps out along the edge |

The second is sensitive to the centre pile's size, and the whole set is sensitive to
**aspect ratio** — Unity's `orthographicSize` is vertical, so a wider device widens the
view. If these must hold across devices, pin the camera to a **horizontal** extent instead.

### 3.8 Pile visuals

Piles are **near-circular ellipses** (~1.2 : 1). They shrink and desaturate as they
deplete, driven locally by the `[Networked]` food amount — instant strategic read, no extra
sync. Shrinking is also load-bearing for traversal (§3.6), not merely cosmetic.

**One oval for everything (Phase 8 D9).** The footprint ellipse (centre 7.8 × 6.5, contested
4.2 × 3.5, doorstep 3.6 × 3.0 m at full; stepped down with fill, min 0.45×) is at once:

- the **collider** — a convex `MeshCollider` on a 48-sided ellipse prism, within 4 mm of the
  oval (it was a `BoxCollider` of the bounding rectangle, 0.7–1.5 m proud at the diagonals);
- the **NavMesh carve** — 10 nested `NavMeshObstacle` boxes (a staircase; the API has no
  ellipse), within 0.15 m of the oval on the centre pile and ≤ 0.08 m on the others;
- the **collect surface** — `FoodPile.DistanceToSurface` / `SurfaceApproachPoint` measure to the
  ellipse, so a chicken pressed against any side (0.58 m off) is inside the 1.0 m reach;
- the **visible mound** — its widest ring is seated on the ground and its X,Z come only from
  the footprint step; fill reads through **height** (1.0 → 0.3) rather than a second X,Z
  shrink that used to leave the grain ~0.8× the collider when full and ~0.3× near empty;
- the **baked zone disc** in `Map.unity`.

Outer ovals (doorstep and contested) point their **long axis at the arena centre**, so the
layout is exactly 4-fold (it was 2-fold: two contested ovals lay radial, two tangential, and
the doorstep ovals were chiral). The centre oval stays E–W and is the one 2-fold element left
(see open question in `docs/STATE.md`). Any replacement mound (Phase 8 D2) must fit inside
this oval at every fill step.

### 3.9 ⚠️ Open problem: the arena is too big for four players in 45 seconds

**Status: known, deliberately not fixed yet, must be solved before the map is called done.**

Observed in play: four chickens on a 51.3 m square rarely run into each other. Routes are
long enough that a whole match can pass in parallel — everyone farms, everyone banks, and
the encounter that the entire v0.4 redesign exists to produce never happens. **The
control-and-steal systems go unexercised**, which means the part of the game we spent the
redesign on is the part players see least.

This is a density problem, not a layout problem. The topology (two laps, eight wedges,
tiered piles) is sound and measured; there is simply too much floor per chicken per second.

Levers, in rough order of cheapness — **none chosen yet, and this needs a playtest, not a
spreadsheet**:

| Lever | Note |
|---|---|
| Shrink the arena | Cheapest, but every spatial constant derives from `arenaHalfSize`, and the 2026-08-14 rescale held SCT constant by moving speeds with it. Shrinking alone *shortens* SCT unless speeds come down too |
| Pull the objectives inward | Reduce the contested/doorstep radii so routes overlap sooner, without touching arena size or the sightline contract (§3.7) |
| Raise player count | The arena may simply be sized for 6–8. Changes the FFA maths and the win target, so it is the most expensive option |
| Shorten routes with geometry | More interior structure funnelling traffic through shared corridors — `SectorScatter`'s barrier role already exists and ships at 0 |

**Why it is parked.** The honest answer is that we do not yet know whether the encounter
rate is wrong or the *readability* of encounters is wrong, and guessing costs a re-solve of
every derived constant. This is the first thing a human playtest (§11 item 7) has to
answer.

---

## 4. Visual Direction

- **2.5D** — real 3D meshes on an isometric orthographic camera, no perspective distortion.
- Chunky, exaggerated proportions (large head, round body, tiny limbs; ref: Hei Hei).
- Dark, desaturated map; warm, saturated chickens that pop.
- **Facing:** two base directions (left/right), vertical handled by mirroring; direction
  snaps instantly; idle after 2 s stationary.

**Animation states:** Walk · Idle · Collecting · Ability Cast (2–3 shared clips, per
`AbilityBaseSO`) · **Stunned/Removed** (control-stun or execute removal). *There is no Hit
state — damage no longer exists.* Control states each have an on-character overlay (💫 stun,
🐌 slow, 🌱 root, 💨 knockback); full spec in `ART.md §6.10`.

---

## 5. Character Classes

Each player picks one class, one **passive** (from that class's pool), and their abilities.
Classes differ by stat spread, passive pool, and **class-gated ability access** (§7).

### 5.1 Core stats

| Stat | Meaning |
|---|---|
| **Cargo Capacity** | Food carried before a forced return (also caps how much you can *steal* in one go) |
| **Peck** (amount / cooldown / cast) | Food per cast (3 for every class), the per-class seconds between presses, and the per-class cast length (`PeckCastSeconds`: Speedy 0.4 s, Warrior 0.5 s, Fatty 0.7 s, placeholders for Maestro and the oracle); the Assassin cannot forage |
| **Move Speed** | Across-map speed |

There is **no HP and no Resistance** — durability is expressed through *control resistance*
(via passives) and *defensive abilities*, not a health bar.

### 5.2 The roster (stats are Oracle-solved to hit §2.1 targets)

| Class | Move | Cargo | Peck cooldown / cast | SCT | Fantasy |
|---|---|---|---|---|---|
| **Fatty** 🐔 | 10.125 | 35 | 0.71 s / 0.7 s | 30 s / 2 trips | *"Slow, but it all fits in the beak."* |
| **Speedy** 🐤 | 12.15 | 10 | 0.71 s / 0.4 s | 30 s / 4 trips | *"If you can't catch me, you can't rob me."* |
| **Warrior** ⚔️ | 10.125 | 14 | 0.79 s / 0.5 s | 35 s / 3 trips | *"The farm is a battlefield."* |
| **Assassin** 🗡️ | 11.1 | 10 | n/a (cannot forage) | 40 s / 4 trips | *"Blink and your food is gone."* |

> **Move speeds are the x1.35 values** from the 2026-08-14 arena rescale (38 m -> 51.3 m).
> Arena and speed scaled together, so SCT was unchanged then — travel time is `distance / speed`
> (no longer exactly scale-invariant: the reach-aware legs subtract fixed 2.5 m / 1.0 m radii that
> do not scale with the arena)
> and both moved by the same factor. The table above previously still listed the pre-rescale
> 7.5 / 9.0; the authority is `Assets/_Game/Data/Classes/*.asset`, measured with the Balance Oracle
> report (§2.1), never this table. The SCT column is the original design target, not a gate.

**The load-bearing detail:** Speedy and Assassin share cargo (10), and until 2026-11-05 shared Move Speed too (12.15; the
Assassin is 11.1 since Maestro's 2026-11-05 ruling, legal because he does not forage), but they
target 30 s vs 40 s — the gap is that Speedy forages (Peck every 0.71 s) and the Assassin
cannot forage at all (the oracle report has no farming number for him: "does not forage"). The
Assassin is a fast chicken who is *genuinely bad at farming*; that is the mechanical reason he
denies instead of collects.

**Peck is a cast (BL-206, 2026-11-21).** Pressing Peck starts a cast of `PeckCastSeconds` (table above; `ChickenStatsSO`, Fatty 0.7 s, Warrior 0.5 s, Speedy 0.4 s). The chicken is rooted for the whole cast. The food is asked of the pile when the cast ends, not at the press: amount = min(`PeckAmount` 3, free cargo, pile food), re-checked at the end. A stun, a shove, an execution or the chicken's own next move cuts the cast and nothing is taken; a pile that runs out of food mid-cast (another chicken emptied it) cuts it at once for everyone still pecking it (Maestro, 2026-11-21), while a pile with some food left does not and the cast lands what remains; a slow or being robbed does not cut it (a Snatch cuts only if a balance delta ever gives it knockback, and a Bulwark-damped push too weak to count as a knockback does not cut). The cooldown counts from the press, so a cut cast spends it. A stun landing in the same tick the cast completes lets the food land. Auto-Peck and bots are unchanged: they press again after the cast ends. This replaces the old rule "food at the press, then a 0.4 s movement lock", which is gone. Cast lengths are placeholders until Maestro and the oracle set them.

**Speedy never gets terrain traversal.** Not a jump, not a Blink, not a Vault — Maestro,
2026-08-21: *"Speedy can use control on other chickens and escape just by having more speed.
Speedy with a jump feels unfair."* Speedy already answers two currencies by itself: **escape**
(highest Move Speed in the roster, and Speed multiplies it further) and **denial** (three
Control abilities — Sticky Egg, Feather Trap, Feather Aura; since 2026-11-05 only the Slow aura is left, the other two are retired). Traversal would be a third with
nothing traded for it, and the map would stop constraining the fastest chicken in the game.

The line is *"no ignoring terrain"*, not *"no mobility"* — Speed is deliberately legal,
because a pure ground-speed multiplier deepens Speedy's identity instead of bypassing the
geometry. Enforced by `DataIntegrityTests.Speedy_HasNoTerrainTraversalAbilityAvailableToIt`,
which sweeps the whole roster, because this rule had already been broken once: `Shadowstep.asset`
serialized `AllowedClasses: 10` (Speedy + Assassin) against a constructor, a class docstring and
§7.2 below that all said Assassin — giving Speedy a 14.58 m Blink that crossed every obstacle
class, further than the Big jump.

### 5.3 Specializations — SHIPPED, this is the current mechanic

**Status: built.** Every class offers exactly **two specializations**, chosen once at
character select. Each specialization grants a permanent passive AND, for five of the
eight, force-equips a **signature ability** into one of the four active slots — see
§7.1 for the slot math this drives. Full design rationale, essences and rejected
alternatives live in `docs/design/class-essence-and-signatures.md`; this table is the
current, shipped state.

> **Rewritten 2026-11-05.** Under the fixed-kits baseline (`LoadoutMode` Fixed) every specialization plays one authored four-ability kit
> (`PassiveAbilitySO.Kit`), so the old "signature + pending Peck variant" column is gone: all eight specializations have a full kit and the
> "three unassigned signatures" gap is closed. Per-class differences now come only from stats, `CooldownMultiplier` and the passives.
> Kits are in `Assets/_Game/Data/Abilities/Passives/*.asset`; cooldowns are the base values, then Bully's x0.75 where it applies (every move except Peck).

| Class | Specialization | Passive | Kit (slot order) |
|---|---|---|---|
| **Warrior** | The Brute (Bully) | Every move except Peck recharges 25% faster (`CooldownMultiplier` 0.75: Steal 3 s, Stun 5.25 s, Jump 1.5 s) | Peck, **Jump**, **Stun**, **Steal** |
| | The Relentless | Your stuns and slows last 30% longer (`OutgoingDurationMultiplier` 1.3: Stun 1.69 s, the Slow aura runs 3.9 s); stuns, getting stuck and timed slows on you end 25% sooner (`DurationMultiplier` 0.75, no knockback cut) | Peck, **Speed**, **Slow**, **Steal** |
| **Speedy** | The Anxious (Featherfoot) | You bank food 10% faster (`DepositRateMultiplier` 1.1, permanent); immune to the pile-slow and the collision slow | Peck, **Speed**, **Slow**, **Shove** |
| | The Agile (Slippery) | Ability slows on you are 40% weaker and end 40% sooner (`Resist` 0.6); stuns, getting stuck, pile and bump slows land in full | Peck, **Speed**, **Slow**, **Shove** |
| **Fatty** | The Boulder (Bulwark) | Knockback cut by 75%; stuns, getting stuck and timed slows end 35% sooner | Peck, **Stun**, **Egg Shell**, **Shove** |
| | The Hauler (Hoarder) | Carries at least 40 food (a full win's worth) | Peck, **Slow Area**, **Egg Shell**, **Steal** |
| **Sneaky** | The Assassin (Spoiler) | Banks 15 bonus food if the clock expires with no winner; carries at least 25 food (`MinimumCapacity`, a kill's loot is ordinary cargo held to it) | **Hunt**, **Jump**, **Hide**, **Stun** |
| | The Thief | Every steal takes 1.6x more | **Steal**, **Jump**, **Hide**, **Slow Area** |

**Slot roles (Maestro, 2026-11-20).** The kits above are listed in slot order, and the order is a rule: slot 1 is the **income** move (Peck; Hunt for the Spoiler, Steal for the Thief), slot 2 **movement** (the Fatty's second attack instead), slot 3 the **flex** move (defence or self utility, or a second attack; the least used), slot 4 the **main attack**, the combo partner of slot 1. The touch hexes are tilted to match (ART.md section 6.7). `FixedKitTests.ShippedKits_HaveTheExactSlotOrder` pins the eight orders and `EveryKit_FollowsTheSlotRoles` the roles (BL-156).

The old signatures (Scrap, Headbutt, Ground Quake, Sneaky Steal) are retired (2026-11-05). The class shows as Sneaky; in code it is still `ChickenClass.Assassin`.
The Spoiler passive asset's display name is "Assassin".

**Kit swaps (Maestro, 2026-11-05, later).** Featherfoot carries Shove instead of Steal ("Speedys should not steal by
design"), so its kit is now identical to Slippery's: the two Speedys differ by passive only (approved as is). The Thief
carries Slow instead of Shove, and the Spoiler Hide instead of Slow.

**Slippery reworked (Maestro, 2026-11-05, later).** It no longer shortens stuns (the stun is the Speedy's weakness) or
roots. It resists ability slows only, with one value (`Resist` 0.6) for strength and duration: the slow's reduction is
scaled by 0.6 (factor' = 1 - (1 - factor) x 0.6, so the 55% Slow aura holds a Slippery bird at 73%), a timed ability
slow lasts x0.6, and a slow zone or aura, which has no duration, slows it at the weaker strength for as long as it
stands inside (the first-60%-of-the-zone's-life window was removed the same day; the zone rule is open, BL-123). The pile slow and the bump slow are not resisted.

**The Warrior is the all-rounder (Maestro, 2026-11-05, later).** Both Warrior kits carry Peck, Steal, one
crowd control and one mobility move; the old "defend a pile" identity is gone. **Bully** is the cooldown spammer
(every move except Peck recharges 25% faster; the old steal x1.4 and +2 cargo were removed, and its declared
Solo Clear Time exception with them, so it farms like the naked Warrior: Oracle 34.98 s / 3 trips on every seat).
**Relentless** is tenacity plus stronger crowd control, and it stays the Warrior's signature. Two multipliers
on one passive: `DurationMultiplier` 0.75 shortens the stuns, getting-stuck and timed slows that land on it (the same
`ModifyControlDuration` mechanism as Bulwark, no knockback cut), and `OutgoingDurationMultiplier`
1.3 lengthens the stuns and slows it applies. The caster's factor is applied first, then the victim's: a Relentless
stun on a Bulwark bird lasts 1.3 x 1.3 x 0.65 = 1.10 s; on a Slippery bird the full 1.69 s (Slippery leaves stuns
whole since the later 2026-11-05 rework). The Slow aura's run time is its crowd control, so it
runs 3.9 s (cooldown 6 s, from the cast). Roots and placed zones are not lengthened. Because receivers
check claims against a ceiling, the stun and slow ceilings are multiplied by the strongest outgoing factor among
the shipped passives (stun ceiling 1.3 x 1.3 = 1.69 s). Relentless's Oracle result is 34.98 s / 3 trips.
**Featherfoot** (Speedy) lost Quick Drop and gained a permanent banking rate instead. It shipped at 1.5x (Oracle 28.54 s / 4 trips,
1.46 s under the Speedy target 30 s); Maestro had it tuned to **1.1x** the same day, which lands on the target
(29.62 s / 4 trips on all seats, inside +-0.5 s), so Featherfoot needed no exception (the SCT gate that asked for one was removed on 2026-11-20). The naked Speedy
run is 30.02 s, so any bonus above about 1.13x leaves the target. Assassin/Thief remain amplified only by their own passive.

**Visual identity (per-specialization colour/model) is designed, not shipped.**
`docs/design/class-essence-and-signatures.md` §"Visual identity" specifies Speedy's
Agile/Anxious split concretely (upright-and-still vs. crouched-and-ruffled); concept
art exists but needs regenerating in the shipped front-facing house style before
`artist-3d`/`shader-artist` can build it.

## 6. Interaction & Control System

All chicken-to-chicken interaction is **control + steal**. No basic attack, no HP, no
damage. Encounters are decided by ability choice, cooldown management, and positioning —
catching a *loaded* rival mid-route beats winning a fair fight.

### 6.1 Casting & authority

- **Targeted, never skill-shot. Abilities cannot be dodged — only counterplayed.**
  Counterplay happens *before* the button (positioning, saving a defensive ability), not as
  a twitch reaction. No projectile to lag-compensate.
- **The caster's word is not final** — a cast is a *request*; the target's authority
  validates range / cooldown / legality with tolerance before it applies (anti-cheat).
- **Targeting is self-centred.** Self buffs, AoE-around-self, and
  drop-at-feet zones need no aim. The **only** single-target ability in the game is the Assassin's Death Mark.
- **Range-only targeting (Maestro, 2026-11-05).** Every targeted ability (Steal, Stun, Shove, Slow) hits **every rival within its radius
  around the caster, by planar distance only**: no cone, arc, forward offset or single pick. Reach is +20% over the previous base
  (Steal 5.16 m, Stun 5.4 m, Shove 5.4 m, Slow 6.48 m). The old shapes are kept behind `MatchConfigSO.TargetingMode`
  (`RangeOnly` default, `MatchConfig.asset` 0; `Shaped` restores the cones: Steal 140 deg arc, Stun 120 deg cone), pushed once by
  `ProjectInstaller` into the static `AbilityTargeting.Mode`. Placed zones, auras, Jump and no-shape moves never change.
  Base walls still block every ability.

### 6.2 Steal — the natural cap

```
stolen = min(abilityStealValue, attackerFreeSpace, defenderCargo)   // never negative
```

Cargo transfers **directly** attacker←→victim — nothing ever drops on the floor. The
formula self-regulates: a **loaded thief cannot steal** (no free space), so theft is a
rhythm (steal → bank → return), not a drain. Fatty's 35 cargo can only leave in ≤10–14
bites, so he *bleeds* under pressure instead of popping, and his defensive abilities are
what he's actually playing. A **steal-proof** bird (Egg Shell, §7.2) refuses every
steal outright: the thief is not credited, so the formula never runs.

### 6.3 Control ladder

| State | Move | Cast abilities | Collect | Source |
|---|---|---|---|---|
| **Slowed** | reduced | ✅ | ✅ | collision / pile / slow abilities |
| **Stuck** (rooted) | ❌ | ✅ | ❌ | trap abilities |
| **Stunned** | ❌ | **❌** | ❌ | the Stun ability |

A Peck cast (§5.2) sits beside the ladder: the caster is rooted by the cast itself, and a **stun, a shove (knockback) or an execution cuts it** with nothing taken. Slowed, and being robbed, do not cut it.
Stun is the only state that locks *casting* — which is exactly why it's the qualifier for
the Assassin execute. There is one stun, 1.3 s (`WingSlam.asset`, now called Stun; 1.3 s against Slippery too since the later 2026-11-05 rework, 0.845 s against Bulwark, 0.975 s against Relentless, 1.69 s from a Relentless caster). **Protections:** control immunity (the Egg Shell egg stops the whole ladder; the Egg Shell walk stops ability slows only, §7.2); **steal-proof** (Egg Shell) is separate and only stops cargo being taken (§7.2). **Slow sources** (collision, pile, ability) are tagged separately so
passives like Slippery can target one without the others. Bulwark and Relentless shorten
only **timed** effects (stuns, getting stuck, timed slows); presence slows (zones,
the Slow aura, pile, collision) have no duration and apply to them in full. **Slippery** (2026-11-05, later) resists
ability slows only: weaker (reduction x0.6), timed ones x0.6 shorter; a zone or aura slows it at the weaker strength while it is inside
(no lifetime window, removed 2026-11-05, BL-123). Stuns, roots, pile
and bump slows land on it in full. Getting stuck has no source in the shipped registry (Sticky Egg and Feather Trap are retired). A control-immune bird (the egg) now also refuses zone slows; before 2026-11-05 zone slows ignored immunity. **Control ceilings and Relentless:** receivers cap incoming stun and slow claims at the shipped ceiling multiplied by the largest `OutgoingDurationMultiplier` among the shipped passives (1.3 today, so the stun ceiling is 1.69 s); the root ceiling does not move.

### 6.4 The Assassin execute (the only hard removal)

UI name Hunt (short label HUNT, second press POUNCE, HUNT! pop for the hunter when the kill goes live); internal name Death Mark (spec 2026-11-06 D5b).

**One button, two presses.** Death Mark (named Mark/Kill until 2026-10-07; the code type is still
`MarkKillAbilitySO`) is the **Assassin** specialization's forced (class shown as Sneaky, code `ChickenClass.Assassin`)
pre-equip (§7.1: its `AllowedClasses` is `None`, so it can only arrive this way) — choosing Assassin equips it automatically,
locked in slot 0 of the four active slots, leaving three to build with. The Thief specialization does not carry
it at all, cannot hand-pick it, and cannot execute. The Assassin's stun now comes from the generic Stun ability (1.3 s), not Ambush (retired 2026-11-05).

| Stage | Condition | Counterplay |
|---|---|---|
| **Mark** | Press → a living rival anywhere on the map, picked at random but **likelier the farther it is from its own base**. The target sees it is marked. | Reach your base in time, where nothing reaches you |
| **Hunt** | The Assassin has **5 s** to find it and close in (4 s until 2026-11-23); the Kill is only possible in the **last 2.0 s** (the 3 s arm is a gameplay beat; 1.0 s until 2026-11-23) | Escape: stay out of his reach until the timer runs out, or walk home |
| **Stun** | The target must be stunned **when the Kill lands** (his own Stun, or *any* stun; one that began before the arm and is still running counts) | Don't get stunned |
| **Kill** | Press again in the last 2.0 s while it is stunned, within **6.2 m**, with no base wall between, and the Assassin is not stunned (a stunned Assassin cannot cast anything) | — |

No isolation: the mark ignores crowds. The only defences are not being stunned, escaping, and reaching your base in time.
A steal-proof Egg Shell egg (§7.2; the walk is not steal-proof) also makes you unkillable while it lasts: a steal-proof chicken cannot be executed
(`ExecuteRules.CanBeExecuted`). The egg is stun-immune as well, so an egged bird cannot be stunned at all; the mark stays alive and its timer keeps running. A stun can still land on the one-second walk.

On success the victim's **entire cargo is drained and goes into the Assassin's ordinary cargo**, as far as it fits,
and then the flat bounty goes into the room left; whatever does not fit is not gained and is not left on the victim either
(Maestro, 2026-11-24: "como hacer trampas": a capacity-free store let a chicken win without going home and took the Fatty's identity,
a big beak). The Spoiler's capacity is **25** (`Spoiler.asset` `MinimumCapacity`, a floor read through `ModifyCargoCapacity` like the Hoarder's;
the Sneaky class stat stays 10, shared with the Thief), so a Fatty's whole beak does not fit and a kill is a courier's trip, not a deposit in the field.
Until 2026-11-24 the loot went into a capacity-exempt "bounty bag" (`ChickenCargo.BountyBag`), banked before the beak and stolen after it; it no longer exists. The victim is **out for 3 s** (`MarkKill.asset`
`RemovalSeconds`): invisible, no collisions, untargetable, cannot act or pick up, not counted by any base wall. Every
running ability, control state and effect window ends at the removal (stun, root, slow, invisibility, steal-proof, any
mark it holds); its **cooldowns keep running**. It then **respawns at its own base** (its corner's spawn point, through
the round reset's teleport, so it never smears across the map and never lands in a rival base) with **no cargo**, and
sees a short **BACK IN!** cue. On failure Death Mark takes a **reduced cooldown** (4 s; 5 s until 2026-11-05), so he re-marks fast; after a kill it is **8 s** (10 s until 2026-11-05, 15 s until 2026-11-04).
**Each round opens with Death Mark on a 4 s cooldown at GO** (`RoundStartCooldownSeconds`), PLAY AGAIN included, so
the first mark is not wasted on targets still at home.

**Spec v2 (Maestro, 2026-10-31).** The press marks a **living rival anywhere on the
map**, chosen at random and weighted toward birds far from home (see below): no mark range, no isolation. Decoys are never picked, and the next mark can never be the last one unless that bird is the
only rival (no time release; reset each round). The Assassin then finds it, stuns it and presses again. The Kill needs the target
**stunned now**, not shelled, the Assassin free to cast, within **KillRange 6.2 m** (8 m until 2026-11-05), and no base wall between them. The mark lasts **5 s** (4 s until 2026-11-23, 12 s until 2026-11-03, 7 s until 2026-11-04). A Kill cannot qualify before the **arm, 3.0 s** (`ArmSeconds`; 0.6 s until 2026-11-04, then only a replication floor), so the Kill happens in the **last 2.0 s** of the mark (1.0 s until 2026-11-23). Only expiry, a kill or the target leaving play ends a mark: it survives
range, crowds and the target going home, where no ability reaches it, so the timer can run out. Expiry costs the fail cooldown (4 s);
a kill 8 s (10 s until 2026-11-05, 15 s until 2026-11-04). All numbers are on `MarkKill.asset`. The bounty is paid on the victim's kill report, so a refused kill pays nothing.

**Leader-weighted pick (Maestro, 2026-11-20, D7b).** The flat bounty gives no reason to hunt the leader, so the pick does: the rival with the most banked food (every rival tied for it) has its score multiplied by `LeaderPickWeight` (`MarkKill.asset`, range 1 to 4, 2 is a placeholder, BL-210); everyone else counts 1. It sits next to the two weights below and does not replace them: a leader at home still scores 0 (`MarkPickRules.LeaderWeight`, `Score`).

**Weighted pick (Maestro, 2026-11-04).** A 7 s mark left the victim time to walk home, so the interaction died. The window is 5 s (4 s until 2026-11-23) and
the pick favours birds far from home and, softly, far from the Assassin: each candidate rolls `roll * w * w * a`. `w` is its planar
distance to its own base over the longest base-to-base distance (the diagonal, read from the live bases, 61.7 m), clamped to 0..1.
`a = 0.25 + 0.75 * clamp01(distance to the Assassin / (span / 3))`, where span / 3 is about 20.6 m. A rival next to the Assassin keeps a
quarter weight, so nobody is unmarkable; marks that start inside the old 10.2 m reach (Ambush era) drop from 40% to 27% with the Assassin camping the
centre, and from 15% to 10% roaming (Monte Carlo, mechanics-designer). The highest score wins. A bird at home
scores 0 and is never picked while another candidate is away from home; if every score is 0 the pick is uniform, so a mark always
lands when a rival exists (`MarkPickRules`, authority side, rolls from `UnityEngine.Random`). The last mark is still excluded
unless it is the only rival. **Stealing from an Assassin:** his kill loot is ordinary cargo, so every steal takes it like any food (`StealRules.Stealable`, `StealRules.DrainAmount`); the "beak first, then the bag" split went with the bag on 2026-11-24.

**The 3 s arm and the stun at the kill (Maestro, 2026-11-04).** `ArmSeconds` is 3.0, so the Kill is possible only in the last
2.0 s of the 5 s window (1.0 s of 4 s until Maestro's 2026-11-23 ruling: "5 seconds, 3 of mark and 2 effective"). The stun must be active when the Kill lands; a stun that began before the arm and is still running counts.
Stun lasts 1.3 s (Slippery too; 0.845 s on Bulwark, 0.975 s on Relentless), so the Assassin times Stun into roughly `[arm - stun, window)`.
The victim's authority times the arm itself (verdict `NotArmed`, allowance 0.5 s) and checks its own stun (stunned on arrival or
within one capped network latency, verdict `NotStunned`). **Cooldowns:** 4 s at GO, 4 s after a miss, 8 s after a kill (5 s / 10 s until 2026-11-05). That allows
at most 4 kills in a 45 s round (press 4, kill 7.3, press 15.3, kill 18.6, press 26.6, kill 29.9, press 37.9, kill 41.2; a 5th press
at 49.2 is past the end; it was 3 kills at 5 s / 10 s); any miss costs 9 s (5 s window plus the 4 s fail cooldown; 8 s until 2026-11-23). **Stun** cooldown is 7 s, longer than the 5 s window: never a second stun in one hunt,
and the stun is ready for the next. **Jump** (Shadowstep) lasts 0.18 s: dash 3 x 11.1 x 0.18 = 5.99 m (6.56 m at the old 12.15 speed; the reach figures below predate both changes). **Reach framing (2026-11-05, later):** Stun's reach plus Death Mark's `KillRange` stays 11.6 m. Ambush era: 3.6 + 8 = 11.6 m; now Stun 5.4 m + `KillRange` 6.2 m = 11.6 m (`MarkKill.asset`; Stun's reach grew 1.8 m, so the kill range gave 1.8 m back). `KillRange` is the planar distance at the Kill press; the code has no lunge term. The percentages below were computed for the Ambush era (10.2 m reach, 1.0 s stun) and are not recomputed. With Underdog Surge the dash is 8.39 m (9.19 m at 12.15), under the 9.85 m gap between two pinwheel arms, so it cannot cross two arms even with Underdog.
**Walk home.** With a 3 s arm a victim within about 21-29 m of home (class and stun length dependent) who walks home is safe: about
29-36% of marks. **D5 (changed twice on 2026-11-05):** Egg Shell is a 1 s slow-immune walk (stun, theft and execution still land) then a 2 s egg (steal-proof, unkillable and stun-immune), so one well-timed button covers part of the kill span for Hoarder and Bulwark (open for Maestro). **Bounty economy (D4):** the Assassin earns only from executes (victim cargo
plus the flat `ExecuteBounty` 15 (5 until 2026-11-23), the same whoever the victim is; the leader Bounty comeback event and `LeaderExecuteBountyMultiplier` were removed on 2026-11-20). `MatchConfig.SpoilerBounty` 15 is read by no
code; what is paid is `Spoiler.asset` BonusFood 15 at timer expiry with no winner. Mean victim cargo is about 5.7, so with the 15 bounty one kill is nominally worth about 20.7 food and two kills about 41.4, past the 40 target (arithmetic before the time to bank it and, since 2026-11-24, before the carry limit: the loot is ordinary cargo held to the Spoiler's 25, so two kills in one load never exceed 25 and the second trip home is part of the price; the match simulator's kills-to-win table is the measure). At the old 5, 3 banked kills were about 32 food, 8 short of 40. No rule added.

**Feedback (redesign 2026-11-03; all local presentation read from replicated state, every number in `FeedbackTuning`).**
- **Assassin.** (The arm reads for every role: a countdown digit on the hunt marker until the arm, KILL stays dark until then, and a sting plays at the arm for each role.) The press runs a ~0.6 s *draw*: a highlight sweeps the standings rows (a tick per row) and locks on the
  target (lock sting); reduced motion skips the sweep. Then **one hunt marker** (reworked 2026-11-17, Maestro: "Killer
  must know the state of the ability and who is the target at a glance"; design
  `docs/superpowers/specs/2026-11-17-hunt-hunter-feedback-design.md`): a disc in the **target's own player colour** that
  holds the **count** in its centre (3, 2, 1 while arming, then tenths in the live second), a ring draining over the
  mark's life, the paw as a corner badge, **one blocker badge** (star = not stunned, lock = behind its base wall, egg =
  shelled) and the **distance** on a pill, with one decimal under 10 m, a red pill while the target is beyond the kill
  range. **Where it sits follows the rival arrows** (Maestro, same day: "Mark should be integrated in the arrow at the
  floor, and only appear above head if inside the circle"): while the target is outside the floor circle round the
  Assassin's own bird, its floor arrow stays, larger and in the mark's colour, and carries the marker as a compact badge
  **on the arrow** (Maestro, 2026-11-18: "over the arrow, not at the tip"; the arrowhead stays visible beyond it), in
  view or not and through Hide; nothing is pinned to the screen edge. Inside the circle the marker sits
  by the target's head, never over the Assassin's own bird. **Ground brackets** in the target's colour frame the target
  whenever it is in view (red while live and blocked, yellow when the kill is possible). **Yellow** while a kill is
  possible, **grey with a lock** behind a base wall, **cream with a shell** while shelled. The target's standings row
  shows a skull. The Stun hex lights while the target is inside Stun's reach (read from the ability). The **Hunt hex**
  mirrors the marker for the whole hunt: a dot in the target's colour, the draining ring, the count and the blocker
  badge, then a big yellow **POUNCE** with the tenths exactly while a kill is possible, with a sting. When a hunt ends
  without a kill the Assassin reads **MISSED** (time ran out, target lost) or **SAFE** (wall or shell) over its own
  bird while the marker holds its last state for 0.6 s; a landed kill shows no word. The phone buzzes when the mark
  goes live and harder when the kill becomes possible (the "Buzz When Hit" setting). The **edge of the Assassin's
  screen** follows the hunt (2026-11-17, Maestro: the victim reads the mark's states from its screen tint, so the
  hunter must too): a dark edge while the mark arms, a **red** edge in the live second while the kill is blocked, a
  **yellow** edge while the kill is possible, the same red and yellow as the ground brackets. Unlike the
  victim's mood the centre of the screen stays untouched and nothing is washed out. **The two birds of a hunt are
  exempt from the tint** (Maestro, 2026-11-18: "hunter and hunted should be distinguishable from the rest of the
  chickens"): on the Assassin's screen its own bird and the target, on the marked player's screen its own bird and
  the Assassin keep their true colour and brightness while everything else is tinted; only a fully visible bird is
  exempt, so Hide is never given away. MISSED is cream (SAFE stays the neutral grey); the mark's colours are fields
  of `FeedbackPalette.asset`. During a mark only the Stun
  range guide stays on. No banner, no kill range ring, no second clock for the Assassin.
- **Victim.** A short **MARKED!** toast with a sting; the clock pill reads **LIVE IN 3 / 2 / 1** while the mark arms, then a red **LIVE 1s** that pops and pulses. The screen stays **darkened** (heavier at the edges, nothing
  over the centre) for the mark's life; the time left sits beside the match clock and its own row shows the skull. With
  the Assassin outside the inner circle (the floor-arrow ring radius) its rival arrow carries a skull, shown even while
  it is invisible. Inside, no arrow: the darkness turns **cold violet and desaturated**, the same whether it is visible
  or not, plus an almost imperceptible red (~7%) if it is invisible. Hysteresis: in at the ring radius, out only beyond
  1.3x it for 1 s; fades 0.4 s in, 1 s out. No heartbeat.
- **Everyone else** sees only the skull on the marked player's standings row (information, not a defence); it turns red and pulses at the arm, for observers too.
- **End of mark:** every ending (kill, expiry, removal, either player leaving, host migration, round reset, match end,
  scene unload) removes everything for all three viewpoints with a 0.3 s fade.
- Stings (draw tick, lock, marked, kill ready) go through `IAudioService`; **all four are placeholder clips** (brief:
  `docs/audio/death-mark-cues.md`).

**The base wall.** A chicken's body cannot stand inside another chicken's claimed base (radius `MatchConfigSO.BaseKeepOutRadius`, 2.5 m),
and **no ability crosses the wall in either direction**: targeted, cone, area, steal, zones, control, knockback, the execute. A walled target shows as grey "in the area, no effect". Zones test zone centre against victim; an aura's centre is
its caster. Your own base is free for your body, an unclaimed base has no wall, two birds on the same side are not blocked, and self-buffs are
unaffected. A race (the target stepped through a wall after the cast) is a silent no-op with a refund for a steal, never a rejected
RPC. Walking at a wall slides round it. A base whose owner left stays claimed, keeps its colour and keeps its wall.

---

## 7. Abilities System

### 7.1 Rules — two slots are determined for you, the rest you pick

> **Balance baseline, 2026-10-31: fixed kits.** `MatchConfigSO.LoadoutMode` ships **Fixed**: each specialization plays one
> authored four-ability kit (`PassiveAbilitySO.Kit`), players and bots alike, and GEAR UP is a read-only view of it. The kits
> and what to watch for are in `docs/balance/human-test-baseline.md`. Everything below is the **Free** mode, unchanged and one
> switch away; a malformed kit logs an Error and falls back to it. Egg Shell was **retired** 2026-11-03 (merged into Turtle Mode).
> This is a baseline to iterate, not a final roster.

> **Simplified 2026-11-05 (Maestro): 11 abilities, one per job.** The class-owned pool wording in §7.1 is **history** except where this
> note and the new §7.2 table say otherwise. Free mode now draws on the 10 actives: Warrior, Fatty and Sneaky can pick
> the 7 everyone-abilities plus Jump (8); Speedy the 7 only (Quick Drop was retired later on 2026-11-05); Peck and Death Mark stay pre-equip-only. Nineteen moves are
> now retired (listed under the §7.2 table), and the §7.3 / §7.4 tables are rewritten to the 10.

> **Simplified 2026-09-04 (Maestro).** The old wording made slot pressure vary by
> specialization, which nobody could hold in their head. The model below is uniform: one
> slot answers to your **class**, one to your **specialization**, and the remainder is
> yours. Nothing about the four-slot total changed — only the rule that fills it.

**Loadout = 1 mandatory specialization (its passive) + 4 active ability slots, always 4,
for every class.** There is no separate "passive slot" outside the specialization choice —
no class has 5 buttons.

> **Revised 2026-10-06 (Maestro) — the pre-equip model.** Every **subclass** (specialization,
> `ChickenSubclass`) has **two pre-equipped slots**: a **Peck slot** (a Peck variant for a forager,
> Death Mark for the Assassin) and a **Signature slot**. **The ability owns the assignment**: each ability
> carries two flag columns, `PeckSlotPreEquippedBy` and `SignaturePreEquippedBy` (each a
> `ChickenSubclassFlags` set of subclasses), edited in the Balance Editor's Abilities table ("Peck slot" /
> "Signature slot"). Two abilities claiming the same slot for one subclass is a flagged data error (runtime
> takes the first in registry order), as is one ability in both slots of a subclass. Either slot may hold **any ability except a Peck on the Assassin**
> (off-class abilities are allowed; they bypass class identity but nothing breaks at runtime).
> Pre-equips are **locked**: they sit in a fixed slot (Peck slot 0, signature 1, or 0 with no Peck slot)
> and the player cannot remove, swap or move them (the Signature slot is locked, and so is the Peck slot). An **empty** Peck slot on a forager falls back to the
> plain Peck so a forager cannot stop foraging by accident; a non-Peck deliberately placed there means
> that subclass starts with no Peck. A subclass with an unfilled slot is a pending design gap, reported by
> the Balance Editor's "Pre-equipped completeness" panel.
>
> **`AllowedClasses` (the "Classes" column) means only "hand-selectable in the loadout picker"** — the old
> Common/Character slot kind is retired. `All` = shared, a single class = that class's own, and
> **`None` = pre-equip-only**: never in the picker, bot presets or backfill, obtainable only through a slot
> column. **Peck is None-class** (never hand-picked; forced via the Peck slot column) and so is Death Mark
> (only the Assassin specialization gets it; the Thief cannot hand-pick it). A subclass **forages iff its resolved Peck slot
> is a Peck**; the one hard rule is that the **Assassin never forages** (Predation axiom), enforced in code
> rather than by any ability's class mask.

The two pre-equipped slots per subclass are therefore filled by the specialization, not chosen:

| Slot | Determined by | Rule |
|---|---|---|
| **Peck slot** | Your **specialization** | The forager's Peck (variant) — or Death Mark for the Assassin. Falls back to the class's plain Peck if left empty. **The Assassin cannot hold a Peck**: a hard rule (`PreEquippedLoadout.ClassMayForage`), not a class mask, and that is the mechanical reason it cannot farm. A Peck is also never a Signature. |
| **Signature** | Your **specialization** | One further ability, force-equipped. |

Everything left over is **freely chosen** from the abilities that class may legally equip
(everything its `AllowedClasses` includes, shared or class-only):

| Class | Determined | Free picks |
|---|---|---|
| Warrior / Speedy / Fatty | forager + signature | **2** |
| Assassin | signature only | **3** |

That is the whole rule. A forager always picks 2; the Assassin always picks 3. Slot
pressure no longer depends on which specialization you took.

**Consequence — the three pending Peck-variant signatures are superseded.** §5.3 parked
Slippery / Featherfoot / Hoarder on "signature pending, wants a Peck variant". Under this
model a Peck variant cannot be a signature: the forager slot answers to the *class*, so a
spec-granted Peck would be a second forager. Per-class foraging variation already lives
where it belongs — in each class's `PeckAmount` / `PeckCooldown`. Those three
specializations therefore need **ordinary signature abilities** assigned (see §11 item 2).
- **Eligibility is `AllowedClasses` flags only** (`AbilityBaseSO.AllowedClasses`; the Common/Character
  slot kind was retired 2026-10-06). **There was no shared ability from 2026-11-03 to 2026-11-05; since the 11-ability cut seven abilities are open to every class (§7.2)** (Egg Shell, the last one, merged into
  Turtle Mode; Free pools before 2026-11-05: Warrior 6, Speedy 6, Fatty 7, Assassin 7; now 8 each, see the note above). **Peck is None-class: never picked,
  pre-equipped through the Peck slot of every forager specialization.** It used to also hold Speed Burst and Snatch — both turned out
  to be class essences hiding in the shared pool (Speed Burst is Speedy's whole "fastest
  in the game" claim; Snatch is an AoE steal, which is the Assassin's entire income) and
  were moved to their own class. **A class ability belongs to exactly one class, full
  stop** — never a wider mask (`docs/design/class-essence-and-signatures.md`
  §1). `BalanceEditorWindow`'s roster-integrity panel reports any violation live, and flags
  pre-equip-only (`None`) abilities no subclass references.
- Every ability is balanced against the others — monetization must never be pay-to-win.

### 7.2 The pool - 10 active abilities, one per job (2026-11-05)

Ability text follows the `Description` field of each shipped `.asset`. Every number below is read from `Assets/_Game/Data/Abilities/*.asset` (before passives). The asset file names are the old ones (the survivor keeps its asset, GUID and icon): Steal is `Snatch.asset`, Stun `WingSlam.asset`, Shove `CluckShock.asset`, Slow `FeatherAura.asset`, Speed `SpeedBurst.asset`, Jump `Shadowstep.asset`, Egg Shell `TurtleMode.asset`, Hide `Invisibility.asset`, Death Mark `MarkKill.asset`. Generic names, short labels (ROB, STUN, PUSH, SLOW, FAST, JUMP, SHLD, HIDE; SHLD became EGG and Shield became Egg Shell on 2026-11-07) and description templates came in the same change. All reach figures are a radius around the caster (range-only targeting, §6.1).

| Ability | Short | Cooldown | Who can pick it | Effect |
|---|---|---|---|---|
| **Steal** | ROB | 4 s | everyone | Robs 5 food from every rival carrier within 5.16 m (Thief x1.6); no knockback. Was Snatch: 4.3 m ahead, cooldown 3 s, with knockback, Assassin-only. |
| **Stun** | STUN | 7 s | everyone | Stuns every rival within 5.4 m for 1.3 s. Was Wing Slam (4.5 m, 120 deg cone). Also the Spoiler's way to qualify a Hunt pounce. |
| **Shove** | PUSH | 5 s | everyone | Shoves every rival within 5.4 m away (knockback force 16.2). Was Cluck Shock (4.5 m). |
| **Slow** | SLOW | 6 s | everyone | Aura: slows every rival within 6.48 m to 55% speed for 3 s (a Slippery bird: to 73%, and only for the first 1.8 s). Was Feather Aura (5.4 m, "every chicken"). |
| **Speed** | FAST | 4 s | everyone | A 1.5 s sprint at 1.3x movement speed (1.5x until 2026-11-05). Was Speed Burst; no longer Speedy-only. |
| **Jump** | JUMP | 2 s | everyone but Speedy | Dashes forward at 3x speed for 0.18 s (about 6 m for the Sneaky at 11.1, 5.5 m for the Warrior), straight over walls. Unchanged (Shadowstep); Speedy never gets terrain traversal (§5.2). |
| **Egg Shell** | EGG | 8 s | everyone | **Walk, then egg, on one press.** Phase 1 (`WalkSeconds` 1): walk at 0.5x speed, immune to ability slows only (timed slow, zone, Slow aura); stun, root, shove, theft and execution still land. Phase 2 (`EggSeconds` 2, starts automatically): cannot move; immune to stun, slow, root and shove; steal-proof and unkillable. Duration 3 s, cooldown 8 s from the cast. Press again to end it. Was Shield (until 2026-11-07), before that Turtle Mode (Fatty-only). |
| **Hide** | HIDE | 4.5 s | everyone | Fades you to a ghostly outline for 2 s (opacity 0.2; 4 s until 2026-11-05). Using a move breaks it. **Bots lose track of you** (2026-11-22): they act on the spot where they last saw you. Was Invisibility (Sneaky-only). |
| **Peck** | PECK | 0.8 s | none (pre-equip) | Start a cast (0.4 to 0.7 s by class, §5.2) rooted at the pile you stand at; when it ends you take a beakful, unless a stun, shove, execution or your own move cut it. Forced for every forager. |
| **Hunt** (code Death Mark) | HUNT | 4 s after a miss, 8 s after a kill | none (pre-equip) | The hunt (§6.4): mark a rival far from home, stun them within 6.2 m, press again to pounce. `KillRange` 8 -> 6.2 m on 2026-11-05 (later); its stun comes from the generic Stun (1.3 s). |

**Retired 2026-11-05** (the `Retired` flag, out of the registry and every kit, asset and code kept, like Egg Shell): Scrap, Dive Bomb, Sneaky Steal, Ambush, Belly Flop, Ground Quake, Headbutt, Roll & Push, Dust Kick, Feather Trap, Sticky Egg (`RootEggAbilitySO`), Smoke Roost, Ruffle, Feint, Immovable, Spine Coat, Doppelganger. **Quick Drop** (`QuickDrop.asset`, `Retired` 1, out of the registry) was retired later the same day: its 4x banking burst became Featherfoot's permanent `DepositRateMultiplier` (1.5x, tuned to 1.1x the same day) (19 retired in all, 10 active). Egg Shell was retired earlier (2026-11-03). Their old numbers are in git history; the rest of this section describes only the 10.

Control ceilings: with no root and no RPC slow in the registry, a populated registry yields a known 0 s ceiling for those kinds, so every such claim is refused at Debug level (it used to log an Error per spawn and leave an Unknown ceiling that trusted the sender).

**Egg Shell has a real defensive half (Phase 7, Maestro decision 6; reworked into walk-then-egg 2026-11-05).** It grants the **steal-proof** state (`ChickenController.IsStealProof` / `GrantStealProof`, a networked timer; recasting keeps the later end). While steal-proof, nothing can take the bird's cargo: Steal and Death Mark's execute check it *before* crediting the thief, so no food is created or duplicated; the victim's side re-checks in `RPC_DrainStolen` / `RPC_TransferAllToBountyBag` (sub-RTT race, logged; a refused optimistic drain is refunded). Details:

- **Steal** takes nothing from a steal-proof bird.
- **Death Mark's execute** cannot touch a steal-proof bird (a shelled chicken cannot be executed: caster gate and the victim's authority; a sub-round-trip race refunds the Kill cooldown and restores the mark).
- **Egg Shell phases** (`TurtleMode.asset`, 2026-11-05 later):
  - **Walk** (`WalkSeconds` 1): 0.5x speed. A new networked slow-immunity window makes the timed ability slow and the zone / aura slow refuse the bird; stun, root, shove, theft and execution still land (only the egg is steal-proof). Pile and bump slows still apply (the immunity covers ability slows only).
  - **Egg** (`EggSeconds` 2): starts on its own when the walk ends. The bird cannot move, is immune to stun, slow, root and shove, and steal-proof (so unkillable). Control immunity also refuses zone slows (`CheckAbilityZoneSlow`).
  - **Ending it:** pressing Egg Shell again ends the whole Egg Shell in either phase. Casting any other ability ends only the egg: during the egg the Egg Shell ends; during the walk the pending egg is dropped and the walk runs out. Auto-Peck never fires during a live Egg Shell, while a manual Peck counts as a cast. The old second-press lock is removed; bots no longer press-lock and leave a running Egg Shell alone.
  - Duration 3 s (walk + egg), cooldown 8 s from the cast.
- **Telegraph:** a rival aiming a steal at a steal-proof bird sees the grey no-effect mark and a **SAFE** pop (`hud.pop.safe`).
- **Bots** raise a shell when a rival with a steal ability is within 5.4 m and the bot carries 5 or more food.

### 7.3 Cooldown tiers

Rewritten 2026-10-22 (decision A9), reduced to the 10 actives on 2026-11-05, then re-banded the same day (Maestro: the
cooldowns were too long for a 45 s match).

| Tier | Range | Shipped (cooldown read from the `.asset` files, before passives) |
|---|---|---|
| **Low** | 1-3 s | Jump 2 |
| **Medium** | 4-6 s | Steal 4, Speed 4, Hide 4.5, Shove 5, Slow 6; Hunt (code Death Mark) 4 after a miss (and 4 at GO) |
| **High** | 7-8 s | Stun 7, Egg Shell 8; Hunt 8 after a kill |

The previous bands were Quick 3-5 s, Standard 6-9 s and Heavy 10-12 s (Jump 4, Steal 5, Speed 6, Shove 7, Slow 7, Stun 8,
Hide 8, Shield 10, Death Mark 5 / 10). The High band holds the immunity move (Egg Shell) and the one hard stun (Stun, which
also qualifies a Death Mark kill). Stun stays 1.3 s. Peck (per-class, about 0.7-0.8 s) is foraging cadence
governed by the SCT axiom, not a tiered move.

Exact per-ability values (steal amounts, stun/slow durations, radii, cooldowns) are tuned
against the Oracle and playtests. Class passives scale cooldowns at runtime (Bully
x0.75, every move but Peck), so a Bully Warrior's Steal is 3 s, Stun 5.25 s and Jump 1.5 s; Relentless leaves cooldowns alone (its Slow aura just runs 3.9 s).

### 7.4 Several moves at once (Phase 6)

There is **no "one ability at a time" rule** for players. A used move runs on its own
timer; a second move cast while the first is still running starts alongside it, and each
keeps its own countdown. Re-casting a move that is still running stays refused (it counts
as cooling). Bots still cast one move at a time.

**Same kind of effect never stacks: the strongest active value wins.** Example: Speed
(x1.5) while Jump's dash (x3.0) runs is x3.0 for the dash, not x4.5. Each move keeps its own timer, so
when the stronger one ends the weaker one is still in force. **Different kinds both apply**
(Egg Shell 0.5 speed x Speed 1.5 = 0.75, because a speed boost and a self-slow are
different kinds).

| Kind | How several are resolved | Moves |
|---|---|---|
| Self speed boost | highest | Speed 1.5, Jump 3.0 |
| Self speed penalty | lowest | Egg Shell 0.5 (the walk) |
| Stealth opacity | lowest | Hide |
| Movement lock | any | Egg Shell (egg), Peck |
| Slow immunity | later expiry | Egg Shell (walk) |
| Control immunity | later expiry | Egg Shell (egg) |
| Steal-proof | later expiry | Egg Shell (egg only) |
| Deposit rate | multiplied | Featherfoot passive 1.1 (permanent) |
| Wall-phasing tier | highest holder (Barge < Vault < Blink) | Jump |
| Aura slow | lowest factor, its radius | Slow |

Outside the rule: Underdog Surge (x1.4) and the slow done *to* you by rivals.

**Stealth breaks on your next move.** Hide's fade ends the moment
you cast anything else. Peck counts as a move; a fizzle (§10.1) does not. **Peck's cast (the movement root, §5.2) is cut when you cast another move.** So Hide
is an opener for Steal / Stun, not a 2 s cast lock.

**Bots and a hidden bird (Maestro, 2026-11-22).** Hide only fades the bird, but a bot no longer sees through it: while a rival is faded (Hide, Smoke Roost's fade) the bot keeps the **last position it saw** and chases, flees from, picks and casts at that spot as if the rival still stood there (the Spoiler's Death Mark hunt too, though a hidden mark is not stunned). The moment the rival shows itself again (any move ends Hide) the bot sees it live. A bot that reaches the remembered spot (inside its arrival radius) or has searched for 4 s (`BotSightRules.GiveUpSeconds`) finds nothing and drops that rival until it reappears. Area abilities still hit whoever is really inside their radius, so a bot casting at the remembered spot can catch a hidden bird that is truly there. A rival that was already hidden before the bot first saw it is unknown to that bot. Rule: `BotSightRules` (Gameplay assembly), read by `BotController` only; the dev-tools auto-play has its own view and is unchanged.

**Combos that are now legal on one bird** (flagged for balance, no value changed): Speed + Slow is about 2.4x relative speed against
chasers inside 6.48 m for 1.5 s (1.3 / 0.55); Hide + Steal (the Thief opener); Jump + Stun (the Spoiler dash, 5.99 m, then a 5.4 m stun); Egg Shell + Speed walks at 0.65x while slow-immune. **Watch in playtest (no caps now):** Egg Shell into its egg back to back with the Slow aura nearby (the aura no longer works on a walking or egged bird).

---

## 8. Progression & Monetization

Accounts are required and cross-platform. Matches award currency (formula TBD — food
banked, rivals robbed, placement) that unlocks classes and abilities slowly; real money
buys **faster access to the same content**, never exclusives.

| Pillar | Model | Pay-to-Win? |
|---|---|---|
| Classes | Free base roster + earnable/purchasable | No — Oracle-balanced |
| Abilities | Free base pool + earnable/purchasable | No — balanced by design |
| Chicken / Map skins | Paid / seasonal / earnable | No — cosmetic |
| Seasonal Battle Pass | Paid, thematic | No — cosmetic |
| Free Rotation | Rotating free classes & abilities | — |

The class-gated ability model does not create pay-to-win: class abilities are class
identity, not power tiers, and every ability is balanced against every other.

---

## 9. Cosmetics & Skins

Purely cosmetic, never affecting stats. Chicken skins (reskin any class) and map skins
(terrain/props/lighting mood). Acquired via purchase, battle pass, or slow currency.
Seasonal content is post-demo, contingent on success.

---

## 10. Controls & UX

- **Mobile-first**, all inputs work on touchscreen. Desktop (keyboard + mouse) and gamepad
  are first-class too (below).
- **Movement:** left-thumb virtual joystick (dead zone 0.12 with a rescaled ramp, throw 60 dp;
  also sets facing). Keyboard WASD; gamepad left stick.
- **Abilities:** right-thumb buttons — **4 for every class** (§7.1). 1-2 are forced by
  foraging/your specialization's signature; the rest are freely chosen. The Death Mark
  button (Assassin specialization only) is **two-press**: first press marks, and it re-labels to **KILL**
  when the target qualifies.
- **Collecting:** stand on a pile and Peck (Peck chain, §10.1). Peck repeats by itself
  until the hold is full.
- **Cooldowns:** bottom-up clip + seconds remaining. Ability buttons must feel responsive.
- **Leaving a match:** Back (Esc, Android back, pad Start) during GET READY, the countdown or
  the round opens "Leave match?" (KEEP PLAYING is the default; LEAVE). Solo pauses the game
  under the sheet; multiplayer keeps going without you. On the podium Back is BACK TO LOBBY.
  With a move held, Back only cancels the move.
- **Near cover:** the fence and your base coop fade to a faint screen-door around your own
  bird, so the bird is never hidden behind them. At GET READY a pulsing YOU ring marks
  your bird.
- Class silhouettes must read instantly in the isometric view.

### 10.1 Casting: hold to aim, release to use (v0.7, Phase 6)

An ability always fires on **release**, never on press. There is no tap window and no
minimum hold; holding does not charge power.

**Press = instant preview.** The moment you press a move's button (or key) your own screen
shows its true area on the ground and marks every chicken inside it. It does not wait for a
threshold. What you see marked is exactly what gets hit (preview and real scan run through
the same `WouldAffect` predicate). Release = use it.

**Past about 125 ms (4 ticks at 32 Hz) the commitment becomes real:**

| | Quick tap (under ~125 ms) | Deliberate hold |
|---|---|---|
| **Your preview** | shown from the press frame | shown |
| **Wind-up glow rivals can see** | none | ramps up at your feet (hidden while you are invisible) |
| **Movement** | full speed | full speed; nothing aims, so moving is how you aim |

Rivals see only the glow at your feet — a wind-up tell, not your area. That asymmetry is
deliberate: counterplay ("he's charging something, disengage") without a solved puzzle.

**The preview is honest (Phase 7).** The ground shape, the dashed / solid state, the target marks and the gold in-range pip on the hex all use the direction the release will use: the soft-lock pick, else your facing (no device sends an explicit aim any more). While a move is held it is drawn as a bold cream-filled shape (about 25%) with an ink under-stroke, and the other range guides dim, so it reads on grass.

**No target is not illegal.** A move that needs a rival in range and has nobody in range
shows a **cream dashed ring** (a legal state: you may still release). The red-grey wash is
only for real refusals: stunned, cooling down, slot unavailable.

**Fizzle.** Release a target-gated move with nobody in range and nothing happens: no
effect, no cooldown, and the slot re-arms after 0.4 s (so you cannot spam it). This
applies to quick taps too. A fizzle does not turn the bird (it keeps its heading; only a real cast rotates it). Other refusals (cooldown, stunned) keep the plain denied bump.

**Cancelling.** A cancelled hold never fires and never burns the cooldown.

| Input | Cancel |
|---|---|
| Touch | Drag your thumb to the screen edge. A cancel zone is drawn along the edges as soon as the hold passes the tap threshold (dim), bright on the edge you approach. It arms within 36 dp of a physical edge and disarms past 44 dp. While armed the hex icon dims to 40% under an ink-outlined X, the preview goes grey and dashed, and the phone gives a 15 ms tick. Release while armed = cancel. |
| Touch, involuntary | A system edge gesture or a lost finger cancels and never fires. |
| Keyboard / mouse | **Esc** or **right mouse button**. |
| Gamepad | **L3** (click the left stick). **B is slot 3** in a match and cancels nothing. |

**Switching.** Pressing another slot's key while holding one switches the hold to the new
slot (the old one is dropped with no fire and no cooldown). When nothing is held yet, the
lowest held slot claims the gesture.

**Quick Moves (setting, off by default).** The move fires the moment you press, with no
preview and no hold.

**Aiming (none on any device, 2026-11-20).** Mouse aim and right-stick aim were removed: PC and pad play exactly like touch, the aim byte on the wire is always 0, and the direction is your facing (or the soft-lock pick). The right stick does nothing. What follows is how a move uses that direction. Directional shapes (jump; the cone / forward circle / capsule shapes only under `TargetingMode` Shaped) turn the
bird toward your aim when the move fires. Under the default RangeOnly every targeted move is a caster-centred circle and ignores
aim. Single-target moves keep their heading and pick
the rival closest to your aim (nearest as tie-break). Self, aura and no-shape moves ignore
aim. Dashes (Speed, Jump) follow your movement, not
your aim.

| Device | Direction |
|---|---|
| Keyboard / mouse | your facing; no magnetism |
| Gamepad | your facing; a soft-lock of +-12 deg |
| Touch | soft-lock snaps to the rival with the smallest angle to your facing, within +-30 deg; farther than that, the move fizzles |

**Slot roles on the controls (2026-11-20).** Slot 1 income, 2 movement, 3 flex, 4 main attack (kits table, section 5.3). Every device lays its four slots out to say so.

**Gamepad bindings:** left stick moves, the right stick does nothing · **A / Y / B / X = slots 1 / 2 / 3 / 4** (A bottom, Y top, B right, X left); RB / RT / LB / LT = slots 1 / 2 / 3 / 4 as before · **L3** (click the left stick) cancels a hold · **Start** opens the Leave sheet mid-match and leaves the waiting room · **B** backs out only outside a match (menu pages, an open Leave sheet, the podium): in a match it is slot 3 and never opens the sheet or leaves the waiting room.

**Keyboard bindings.** Two schemes, a two-button "Keyboard" row in Settings (hidden on phones), live with no restart, saved in `PlayerPreferences.KeyboardSchemeChoice`:

| | Move | Slots 1 / 2 / 3 / 4 | Strip |
|---|---|---|---|
| **Scheme A, "WASD + arrows"** (default) | W A S D | Down / Up / Right / Left arrow | inverted T: slot 2 on top; 4, 1, 3 along the bottom |
| **Scheme B, "Arrows + QWER"** | arrow keys | Q / W / R / E | one row Q W E R (slots 1, 2, 4, 3) |

Digits 1-4 also fire slots 1-4 in both schemes. Esc and the right mouse button cancel a hold in both.

**Peck is exempt from "press previews, release casts" (Maestro, 2026-11-20).** Peck has no range preview, so on whichever slot holds Peck (slot 1 in six of the eight kits; the Spoiler and the Thief carry none) a **tap pecks when it ends**, and a **hold of 0.5 s** (`PeckLongPressRules.PeckLongPressSeconds`) **flips Auto-Peck once**; the release after the flip pecks nothing. A cancel before the release (Esc, right mouse, L3, the touch edge band) kills the press. The same on every device. While Auto-Peck is on, an **AUTO** tag sits on the Peck hex (touch arc and strip). The Peck hex never hides, wherever the kit puts it.

**Desktop and pad HUD.** With a keyboard, mouse or gamepad the hexes become an
**information strip** at the bottom centre (scale 0.75, not clickable): each hex carries its
key badge (a drawn arrow for Scheme A, Q / W / E / R for Scheme B, A / Y / B / X on pad), the cooldown number (tenths under 1 s)
and the in-range pip. The strip reads like the keys: an inverted T for Scheme A and the pad, one row for Scheme B. A held key scales its hex. The last device you touched decides the
layout; any finger on a touchscreen brings the thumb layout back.

**Hold hint.** A one-line hint above the cluster teaches the gesture for your device
("Hold to see the reach · Let go to use it · Click the left stick to cancel" on pad). It shows for your first 5 holds, then
never again, and not with Quick Moves.

**Peck chain.** A Peck that succeeds with no move input keeps pecking by itself until the
hold is full. A dashed ring turns on the Peck hex while the chain runs. It stops on: match
end, death, stun, a knockback, another move cast, move input, hold full (a "full" sound
plays), or no pile with food in reach. It never restarts by itself. **Auto-Peck** (setting,
on by default on touch devices, off on desktop; a saved choice always wins) starts the first peck for you after you stand still (0.18 s) by a pile with
food, unless a stealth move is running (it must not break your Invisibility).

**Settings** (Settings sheet; labels and descriptions are the player copy in `UiText.csv`):

| Setting | Description shown | Default |
|---|---|---|
| Range Guides | Show how far your moves reach. | on |
| Quick Moves | Moves fire the moment you tap. | off |
| Auto-Peck | Peck by yourself when you stand by a pile. | on (touch) / off (desktop) |
| Buzz When Hit | Your phone buzzes when a move hits you. (Android only; hidden elsewhere) | on |
| Reduced Motion | Calms camera shake and screen movement. | off |
| Performance Mode | Static chickens in menus. Saves battery. | on for phones and tablets |
| Dev Mode | Unlock the Ability Lab. | off |
| Music, Sound Effects | volume sliders | full |

### 10.2 What each control state prevents, and how you're told

The control ladder is §6.3's; this is how the game *communicates* it. The rule is that a
state is shown **where it bites** — on the control it disables — not only as an abstract
badge somewhere.

| State | Move | Cast | Collect | How the HUD says so |
|---|---|---|---|---|
| **Free** | ✅ | ✅ | ✅ | Nothing marked. |
| **Slowed** | reduced | ✅ | ✅ | Stick tints cyan with a **`×0.45`** magnitude label. Ability buttons untouched — casting is unaffected, and greying them would say the opposite of the truth. |
| **Stuck** (rooted) | ❌ | ✅ | ❌ | Stick greys out and shows a **⛓** shackle. Ability buttons stay normal — **you can still cast while rooted**, and that is a real tactical option the UI must not hide. |
| **Stunned** | ❌ | **❌** | ❌ | **Every** ability hex takes a red wash and a **✕**; the stick greys. Stun is the only state that locks casting — which is exactly why it's the qualifier for the Assassin execute (§6.4). |

Alongside the controls, a **status strip** above the joystick lists every currently active
status — stun, root and slow are independent and can all be on at once, so the strip shows
the whole set, not just the most severe one. Each row carries its own quantity.

**Stun and root count down; slow shows a magnitude instead.** Stun and root have real
deadlines, so `⚡1.4s` and `⛓2.0s` are facts and both get draining bars. Slow does not
have a deadline at all — it is recomputed every tick from whatever is currently touching
you (zones, auras, piles, collisions), so a slow row shows **`×0.45`** rather than a
countdown. That is also the more useful number: when the honest answer to "how long?" is
"until you move", "how much slower am I?" is what changes your decision.

### 10.3 Why a button refuses

A dead button always says why. The reasons are told apart at a glance:

| Reason | What you see |
|---|---|
| **On cooldown** | Button darkens from the bottom up, with the **seconds remaining** in the centre. |
| **Stunned — can't cast** | Red wash and a **✕**, on all buttons at once. |
| **Slot not available for your class** | The button isn't rendered at all. |
| **Re-arming after a fizzle** | The slot is dead for 0.4 s; no cooldown shows. |

**Not a refusal any more:** "no valid target in range". The button stays at full colour with
no slash; the ground preview is the cream dashed ring, and a gold pip on the hex appears
when something is in range. **Also retired:** "another ability is running". Moves run side
by side (§7.4); a running move shows a draining ring on its own hex.

Pressing a refused button **shakes it and clicks** — a press is never silently swallowed.
A fizzle (release with nobody in range) plays a short slash flash, a bump, a whiff sound
and a dust puff on the hex, and costs nothing.

---

## 11. Open items & deferred

| # | Item | Status |
|---|---|---|
| 1 | Per-ability values (steal/stun/slow/cooldown/radius) | Tuning against Oracle + playtest |
| 2 | ~~Passive pools reimplementation~~ **Specializations** (§5.3) | **Shipped.** 5 of 8 signatures assigned. The other 3 (Slippery / Featherfoot / Hoarder) need **ordinary** signature abilities — the Peck-variant plan was retired by §7.1's simplified slot model on 2026-09-04 |
| 2c | **The arena is too big for 4 players in 45 s — you barely see combat** | ⚠️ **Known, accepted for now, must be solved.** Observed in play: four chickens on a 51.3 m square rarely meet, so the control-and-steal systems that the whole v0.4 redesign exists to serve go unexercised. The map reads as a place, but a place built for more players than it has. See §3.9 |
| 2a | Wing Slam's (now Stun) cd 12 contradicts Warrior's "spams abilities" essence | **Re-tuned 2026-10-22** to 8 s / 1.3 s stun (A4). Confirm in the human 4-player session; fallback 9 s / 1.5 s |
| 2b | Specialization visual identity (per-spec colour/model) | Designed (§5.3), concepts need regenerating in the shipped house style |
| 3 | **Last-15 s endgame rule** | Deferred until base loop is fun; 3 candidates parked (Open Bases / center-collapse / Golden Egg). The shipped stand-in is the last-10 s comeback event (§2) |
| 4 | Currency earn formula | TBD |
| 5 | Pile-slow / collision-slow magnitudes | Tuning |
| 6 | Task 8 Oracle test loads real SOs (not hardcoded stats) | Follow-up |
| 7 | Human playtest — "is it actually fun?" | **The open question the whole redesign exists to answer** |

**Parked future content:** **King of the Hill** — a future *class* (a zone-tax squatter who
"owns" a pile and taxes rivals farming it). **Spoiler v2** — an *earned* bounty (+N per
steal/execute landed) replacing the flat +15, so stalling is strictly worse than hunting.

---

## 12. Flavor System — Franchise Architecture

The engine is built to be **reskinned into separate games** sharing one codebase. Core
loop, stats, ability system, and progression are theme-agnostic; any flavor-specific
variation is a configurable layer, never a code fork.

| Flavor | Title | Setting |
|---|---|---|
| The Farm *(launch)* | Cluck Wars | Countryside grange, chickens |
| Plunder Coop *(planned)* | TBD | Pirate ships & sea |
| Cluck Station *(planned)* | TBD | Outer space |

---

## 13. Implementation status (v0.4, Phase 6 added)

Reflects the four committed v0.4 plans (`docs/superpowers/plans/`).

- **Balance Oracle** — v2 shipped 2026-11-20 (§2.1): farming simulation, report, sweep and solver
  (`Assets/_Game/Scripts/Balance/`); it reports, it does not gate. The v1 SCT gate was deleted.
- **Map & walls** — shipped. Tiered 80-food budget, pinwheel walls on `ObstacleClass`,
  centre pile no longer permanent/regenerating (fixed supply).
- **Combat** — shipped. HP/damage pipeline deleted; `ControlState`/`ControlRules` ladder;
  `StealMath` cap; `RPC_ApplyStun`; `AssassinExecute` (Death Mark, kill loot into ordinary cargo);
  `MatchConfig` W=40 / 45 s / Spoiler +15 / DepositRate 9; class stats Oracle-solved.
- **Ability pool** — shipped. Steal/Control/Defense/Utility categories; ex-damage abilities
  converted to steal; new Ambush / Wing Slam / Shadowstep / Mark/Kill (now Death Mark); roster masks;
  Spine Coat steal-back rate-gated.

- **Phase 6 (2026-10-13) — shipped.** Ability input rework: instant preview, fizzle, edge
  cancel, Esc / right mouse / B cancel, slot switching, Quick Moves, twin-stick aim and
  soft-lock, gamepad, desktop / pad info strip, Peck auto-chain + Auto-Peck, joystick dead
  zone, haptics (Buzz When Hit), Music / Sound Effects sliders. Moves run side by side with
  strongest-wins per effect kind (§7.4). Ability text rendered from the real numbers (§7.2).
  Leave-match sheet, colour-blind-safe player palette, see-through fence / coop, YOU ring
  (§10). Not verified on hardware: Pixel 9 haptics, a real gamepad, a real mouse cursor,
  two-peer aim replication.

- **Phase 7 (2026-10-13) — shipped.** Round-4 audit fixes: steal-proof state, Egg Shell
  "Full shell" and Turtle Mode "Shell walk" (§7.2), Root Egg renamed Sticky Egg and one kid word
  STUCK! (§6.3), Spine Coat text matches the code, ties at the top (§2), Auto-Peck on by default
  on touch (§10.1), honest preview + visible cancel zone, rival nameplates, calmer arena
  (darker surround + hedge bank, calmer grass). Not verified on hardware: Pixel 9 touch feel and
  GPU frame time, a real pad B, Android Back.

- **Simplified abilities (2026-11-05) - on `simplify-abilities`, not merged.** 11 abilities, 8 new kits, range-only targeting (`MatchConfigSO.TargetingMode`), Shield later reworked into a slow-immune walk then an egg. Later the same day: Warrior all-rounder rework, Quick Drop retired into a Featherfoot passive, Death Mark kill range 6.2 m. See `docs/STATE.md`.

**Known gaps:** passive pools (§5.3) still carry the pre-0.4 set; endgame rule deferred;
the Oracle re-solve rests on the simulator's model refinement (bank a winning load without
overfilling) and has not been re-verified in a live human playtest — **that playtest is the
next step.**
