# Game Design Document v0.4

**Version:** 0.4
**Date:** July 2026
**Status:** Redesign implemented (control + steal, axiom-driven balance)

**Changelog v0.4 — the core redesign.** Deleted HP and damage entirely; combat is now
**control + steal**. Introduced the **Solo Clear Time (SCT) axiom** — every stat is
*derived* from a per-class clear-time target, not hand-tuned. Win target 150→**40**, match
3-min→**45 s**. Tiered map food budget (**80 = 2× win**) with pinwheel walls. New Assassin
**Mark/Kill execute** (the only hard removal). Ability pool split into **Steal / Control /
Defense / Utility** with a **Common + Character** class-gated model. Targeting collapsed to
**self-centred or directional** (one exception: Mark/Kill). Full rationale in
`docs/superpowers/specs/2026-07-24-cluck-wars-core-redesign-design.md` and
`…/2026-07-25-ability-pool-rewrite-design.md`.

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
   STEAL from loaded rivals · CONTROL them (slow/root/stun) · DEFEND your haul
```

**Win condition**
- First to bank **40 food** at base wins immediately, OR
- Most food banked when the **45 s** timer expires (tie-break: food → kills → lower corner
  index).

**No death.** There is no HP and no damage. A chicken is never "killed" except by the
Assassin's execute (§6.4), which removes it for ~2 s and respawns it empty. Every other
interaction is a **steal** or a **control effect** — you take food or buy time, you never
grind someone down.

### 2.1 The Solo Clear Time (SCT) axiom — the balance backbone

> A **naked** chicken (no abilities, no passive), **alone** on the full map, farming
> greedily, banks the win target in a fixed **time** via a fixed number of **base trips**.

Every stat is a *dependent variable* solved against SCT — we don't pick `MoveSpeed` because
9 feels nice, we pick it because Speedy must clear in 30 s. This is verified automatically
by the **Balance Oracle** (a pure-C# simulator, `Assets/_Game/Scripts/Balance/`) which
runs the greedy policy on the real pile coordinates and asserts each class lands within
±1.5 s of its target.

| Class | SCT | Trips | Strategy |
|---|---|---|---|
| **Speedy** | 30 s | 4 | Throughput — many small loads, roams wide |
| **Fatty** | 30 s | 2 | Payload — one huge sweep, barely returns |
| **Warrior** | 35 s | 3 | The ability class — mid stats, leans on his kit |
| **Assassin** | 40 s | 4 | Denial — never actually farms; robs and executes |

`WinTarget = 40 · TotalMapFood = 80 (= 2× win) · trips = ceil(WinTarget / CargoCapacity)`.

At 2× supply, two players *could* max out by pure farming — so combat's job is to **buy
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

Pile positions jitter each match (seeded from the session code so all peers agree); the
centre never moves.

**The arena is a baked scene, not runtime geometry.** Because the layout is now fixed
(§3.4), the ground, boundary and walls live in `Assets/_Game/Scenes/Map.unity`, loaded
additively at startup — so the level can be opened, inspected and hand-tuned like any
other asset instead of existing only while the game runs. `Cluck Wars/Map/Bake Map Scene`
regenerates it from `MapGenerator`'s authored tuning; `Cluck Wars/Map/Open Map Scene`
just opens it. Untick **Use Baked Geometry** on `MapGenerator` to fall back to generating
procedurally at runtime, which is what a future randomised map would use.

Only *static* geometry is baked. Player bases and food piles stay runtime `Runner.Spawn`s —
they are `NetworkObject`s and Fusion must own their spawning.

### 3.2 Tiered food budget (total = 80 = 2× win)

| Tier | Count | Food each | Σ | Role |
|---|---|---|---|---|
| **T1 — doorstep** | 4 (one per base) | 5 | 20 | Safe income, ~8 m off your mouth |
| **T2 — contested** | 4 (edge midpoints) | 10 | 40 | Equidistant from two bases — contested arrival is the default |
| **T3 — centre** | 1 | 20 | 20 | The prize, and the wall protecting it (§3.6) |

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
two-lap topology. Match-to-match variety therefore comes from **pile jitter only**
(±0.4 m), and — now the primary reason — a fixed wall layout makes the map **learnable**,
which the two-lap rule above depends on.

Two consequences worth keeping in mind if this is ever revisited:

- Should jitter ever return, it must repeat with **4-fold period** — a 90° rotation maps
  each wall onto another, so those must share a value or the four players' sectors differ,
  which is a fairness bug in an FFA, not a cosmetic one.
- Pile keep-clear discs scale with pile footprint + jitter, so **growing a pile eats the
  walls**. `PinwheelLayoutTests.Build_WallsSurvivePileKeepClearDiscs_AtRealV05Sizes` guards
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
| **Collection Rate** | Food/sec while standing on a pile |
| **Move Speed** | Across-map speed |

There is **no HP and no Resistance** — durability is expressed through *control resistance*
(via passives) and *defensive abilities*, not a health bar.

### 5.2 The roster (stats are Oracle-solved to hit §2.1 targets)

| Class | Move | Cargo | Collect | SCT | Fantasy |
|---|---|---|---|---|---|
| **Fatty** 🐔 | 10.125 | 35 | 3.2 | 30 s / 2 trips | *"Slow, but it all fits in the beak."* |
| **Speedy** 🐤 | 12.15 | 10 | 3.0 | 30 s / 4 trips | *"If you can't catch me, you can't rob me."* |
| **Warrior** ⚔️ | 10.125 | 14 | 2.6 | 35 s / 3 trips | *"The farm is a battlefield."* |
| **Assassin** 🗡️ | 12.15 | 10 | 1.7 | 40 s / 4 trips | *"Blink and your food is gone."* |

> **Move speeds are the x1.35 values** from the 2026-08-14 arena rescale (38 m -> 51.3 m).
> Arena and speed scaled together, so SCT is unchanged — travel time is `distance / speed`
> and both moved by the same factor. The table above previously still listed the pre-rescale
> 7.5 / 9.0; the authority is `Assets/_Game/Data/Classes/*.asset`, solved against
> `BalanceOracle`, never this table.

**The load-bearing detail:** Speedy and Assassin share Move Speed (12.15) and cargo (10) but
clear in 30 s vs 40 s — the entire gap is **Collection Rate** (3.0 vs 1.7). The Assassin is
a fast chicken who is *genuinely bad at farming*; that is the mechanical reason he denies
instead of collects.

**Speedy never gets terrain traversal.** Not a jump, not a Blink, not a Vault — Maestro,
2026-08-21: *"Speedy can use control on other chickens and escape just by having more speed.
Speedy with a jump feels unfair."* Speedy already answers two currencies by itself: **escape**
(highest Move Speed in the roster, and Speed Burst multiplies it further) and **denial** (three
Control abilities — Root Egg, Feather Trap, Feather Aura). Traversal would be a third with
nothing traded for it, and the map would stop constraining the fastest chicken in the game.

The line is *"no ignoring terrain"*, not *"no mobility"* — Speed Burst is deliberately legal,
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

| Class | Specialization | Passive | Signature (forced) |
|---|---|---|---|
| **Warrior** | The Brute (Bully) | Steals bigger, + cargo room to hold it | **Scrap** (steal on contact) |
| | The Relentless | Abilities return faster | **Headbutt** (shove + stagger, low cd) |
| **Speedy** | The Agile (Slippery) | Control effects wear off 40% faster | ⏳ *pending — a Peck variant* |
| | The Anxious (Featherfoot) | Immune to the pile-slow | ⏳ *pending — a Peck variant* |
| **Fatty** | The Hauler (Hoarder) | Carries at least a full win's worth | ⏳ *pending — a Peck variant* |
| | The Boulder (Bulwark) | Shorter control, 75% less knockback | **Ground Quake** (stomp roots the area) |
| **Assassin** | The Reaper (Spoiler) | Banks a bonus if the clock expires with no winner | **Mark/Kill** (the execute, §6.4) |
| | The Burglar (Thief) | Every steal takes 1.6x more | **Sneaky Steal** |

**Three signatures are still unassigned — and the plan for them changed on 2026-09-04.**
Slippery / Featherfoot / Hoarder previously each wanted a Peck *variant* (empty-beak rush,
burst-fed, heavy-beakful) as their signature. §7.1's simplified slot model retires that
approach: the forager slot is now determined by **class**, so a specialization cannot
grant a Peck. The per-class foraging variation those variants were reaching for already
exists as each class's authored `PeckAmount` / `PeckCooldown`.

Those three specializations therefore need **ordinary signature abilities** — control,
economy or escape tools that read as that specialization's fantasy. Until they land, the
three grant their passive only, which is the one remaining place where §7.1's "every
specialization determines a signature" is not yet literally true. Tracked as §11 item 2.

**Why Bully/Relentless and Reaper/Burglar are each amplified only by their own passive:**
Bully makes Scrap's theft worth taking (bigger steal + room to carry it); Relentless
makes Headbutt genuinely spammable. Neither signature is impressive under the *other*
passive on purpose — that asymmetry is what makes the specialization choice read as a
build, not a stat tweak.

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
- **Targeting is self-centred or directional.** Self buffs, AoE-around-self, and
  drop-at-feet zones need no aim; dashes auto-snap to the first target in the lane. The
  **only** single-target ability in the game is the Assassin's Mark/Kill.

### 6.2 Steal — the natural cap

```
stolen = min(abilityStealValue, attackerFreeSpace, defenderCargo)   // never negative
```

Cargo transfers **directly** attacker←→victim — nothing ever drops on the floor. The
formula self-regulates: a **loaded thief cannot steal** (no free space), so theft is a
rhythm (steal → bank → return), not a drain. Fatty's 35 cargo can only leave in ≤10–14
bites, so he *bleeds* under pressure instead of popping, and his defensive abilities are
what he's actually playing.

### 6.3 Control ladder

| State | Move | Cast abilities | Collect | Source |
|---|---|---|---|---|
| **Slowed** | reduced | ✅ | ✅ | collision / pile / slow abilities |
| **Rooted** | ❌ | ✅ | ❌ | trap abilities |
| **Stunned** | ❌ | **❌** | ❌ | stun abilities (Ambush, Wing Slam) |

Stun is the only state that locks *casting* — which is exactly why it's the qualifier for
the Assassin execute. Stun duration is authored per-ability (Assassin Ambush 1.0 s, Warrior
Wing Slam 1.5 s). **Slow sources** (collision, pile, ability) are tagged separately so
passives like Slippery can target one without the others.

### 6.4 The Assassin execute (the only hard removal)

**One button, two presses.** Mark/Kill is the **Reaper** specialization's forced
signature (§5.3/§7.1) — choosing Reaper equips it automatically in one of the four
active slots, leaving three to build with. The Burglar specialization does not carry
it at all, and cannot execute.

| Stage | Condition | Counterplay |
|---|---|---|
| **Mark** | Press → soft-locks the nearest **isolated** rival (no chicken within **8 m**), in facing. Visible to the target. | Stay near others — in an FFA, safety = approaching an enemy |
| **Arm** | **2.0 s** pass with the mark alive | Break isolation or leave range → mark fizzles |
| **Qualify** | The marked target enters a **Stun** (his own Ambush, or *any* stun — e.g. a Warrior's Wing Slam) → button lights up **KILL** | Don't get stunned; hold a defence |
| **Kill** | Press again while target is stunned **and the Assassin is not himself stunned** | **A third party can stun the Assassin to deny the kill** — stun the hunter to save the prey |

On success the victim's **entire cargo transfers to the Assassin as a capacity-exempt
"bounty bag"** (he becomes a courier of a stolen win) and the victim is removed ~2 s, then
respawns empty. On failure Mark/Kill takes a **reduced cooldown**, so he re-marks fast.
Stun lives in exactly two classes (Assassin + Warrior), which is what makes the deny/enable
counterplay reachable.

---

## 7. Abilities System

### 7.1 Rules — two slots are determined for you, the rest you pick

> **Simplified 2026-09-04 (Maestro).** The old wording made slot pressure vary by
> specialization, which nobody could hold in their head. The model below is uniform: one
> slot answers to your **class**, one to your **specialization**, and the remainder is
> yours. Nothing about the four-slot total changed — only the rule that fills it.

**Loadout = 1 mandatory specialization (its passive) + 4 active ability slots, always 4,
for every class.** There is no separate "passive slot" outside the specialization choice —
no class has 5 buttons.

Exactly two things are determined rather than chosen:

| Slot | Determined by | Rule |
|---|---|---|
| **Forager** | Your **class** | Warrior, Speedy and Fatty each get their class's Peck. `PeckAmount` / `PeckCooldown` are authored per class, so the forager is already a per-class variant, not one shared button. **The Assassin has no forager at all** — `AllowedClasses` on `Peck.asset` excludes it outright, and that is the mechanical reason it cannot farm. |
| **Signature** | Your **specialization** | Every specialization grants exactly one signature ability, force-equipped (§5.3). |

Everything left over is **freely chosen** from the abilities that class may legally equip
(its Character pool plus the Common pool):

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
- **Common** abilities are open to every class that is allowed to equip them; **Character**
  abilities are gated by a per-class mask (`AbilityBaseSO.SlotKind` + `AllowedClasses`).
  **The Common pool is now exactly two abilities: Egg Shell (every class) and Peck
  (Warrior/Speedy/Fatty).** It used to also hold Speed Burst and Snatch — both turned out
  to be class essences hiding in the shared pool (Speed Burst is Speedy's whole "fastest
  in the game" claim; Snatch is an AoE steal, which is the Assassin's entire income) and
  were moved to their own class. **A Character ability belongs to exactly one class, full
  stop** — sharing is expressed only by the Common slot now, never by a wider
  `AllowedClasses` mask on a Character ability (`docs/design/class-essence-and-signatures.md`
  §1). `BalanceEditorWindow`'s roster-integrity panel reports any violation live.
- Every ability is balanced against the others — monetization must never be pay-to-win.

### 7.2 The pool — 29 abilities, one class each (except Common)

Rebuilt 2026-08-23/24: abilities used to be shared across classes to widen loadout
pools, which cost the thing that makes a 4-player FFA readable — you could not tell
what a chicken was by what it did (`docs/design/class-essence-and-signatures.md` §1).
**A Character ability now belongs to exactly one class.** Cooldowns below are the
shipped values (`Assets/_Game/Data/Abilities/*.asset`), not illustrative.

| | Ability | Cooldown | Effect |
|---|---|---|---|
| **Common** | Peck 🐦 | 0.8 s | Take a beakful from the pile you're standing on. Forced for every forager (§7.1). |
| | Egg Shell 🥚 | 8 s | Seals you in an egg — invulnerable, immobile. |
| **Warrior** | Cluck Shock ⚡ | 7 s | Knockback shockwave, shoves the swarm off. |
| | Dive Bomb 🪽 | 6 s | Diving lunge, robs cargo on contact. |
| | Headbutt 🐏 *(Relentless signature)* | 4 s | Short shove + brief stagger — deliberately weaker than Cluck Shock; the shortest cooldown pays for it. |
| | Ruffle 💨 | 5 s | Short pace burst — deliberately weaker than Speed Burst; no traversal, ground only. |
| | Scrap 🪝 *(Bully signature)* | 5 s | Grabs cargo off the nearest carrier — deliberately weaker than Sneaky Steal. |
| | Wing Slam 💥 | 12 s | Ground slam, 1.5 s stun. ⚠️ Longest cooldown in the game, on the class whose essence is spamming — flagged for re-tuning, see §11. |
| **Speedy** | Dust Kick 🌫️ | 6 s | Kicks dust **backwards** — the one cone in the game that fires away from facing, so fleeing is the play. |
| | Feather Aura 💨 | 7 s | Slows every rival within 3 m for the cloud's lifetime. |
| | Feather Trap 🪤 | 7 s | Feather cloud thrown ahead; crossing it slows for 5 s. |
| | Feint ↔️ | 5 s | Hard sidestep, delivered as a knockback impulse — walls still stop it, unlike a blink. |
| | Quick Drop 💰 | 12 s | Dumps the whole beakful at the base in one motion. Retired as a passive (was worth ~27% of Speedy's SCT for free); now an ability, so the tempo costs a slot. |
| | Speed Burst 💨 *(moved from Common)* | 6 s | 2.5x movement speed. Speedy-only now — a universal sprint erased the "fastest in the game" claim. |
| **Fatty** | Belly Flop 🫃 | 10 s | Launches his mass forward, lands with an AoE stun — the justified jump: long telegraph, shortest jump tier, payoff on landing not travel. |
| | Ground Quake 🌋 *(Bulwark signature)* | 9 s | Stomp roots everyone nearby — reachable pre-emptively while guarding, not just as a Retreat reflex. |
| | Immovable 🧱 | 12 s | Immunity (not resistance) to stun/root/slow/knockback for the duration. Does not clear control already on him — spent in anticipation, not as an escape. |
| | Roll & Push 🌀 | 3 s | Rolls forward at double speed, shoves chickens from the path. |
| | Root Egg 🌱 | 7 s | Drops an egg; the first chicken to step on it roots for 2 s. |
| | Spine Coat 🦔 | 8 s | Steals cargo back from and knocks away anyone who hits you. |
| | Turtle Mode 🐢 | 8 s | Heavy control resistance at quarter speed. |
| **Assassin** | Ambush 🗡️ | 10 s | AoE stun — sets up his own execute. |
| | Doppelganger 👥 | 11 s | Decoy copy that soaks attacks. |
| | Invisibility 👻 | 8 s | Fades to a ghostly outline for 4 s (Assassin-only now — was also Speedy). |
| | Mark/Kill 🎯 *(Reaper signature)* | 5 s | The execute — see §6.4. |
| | Shadowstep 👤 | 6 s | Short blink dash, phases over walls (Assassin-only — was briefly also Speedy by asset drift, corrected; Speedy is permanently denied all terrain traversal, §5.2). |
| | Smoke Roost 🌁 | 11 s | Cloud at his own feet — he fades, everyone else in it slows. Pairs with Mark/Kill's isolate-and-execute. |
| | Snatch 🤏 *(moved from Common)* | 3 s | Robs cargo from every rival in a forward arc. Assassin-only now — a universal AoE steal is exactly the Assassin's core, and diluted the class. |
| | Sneaky Steal 🤏 *(Thief signature)* | 5 s | Yanks cargo from the nearest carrier within 3 m. |

**Known stale flavor text, not yet corrected:** Turtle Mode's and Roll & Push's
descriptions still say "damage" (`Assets/_Game/Data/Abilities/*.asset`), left over from
the pre-0.4 combat model this GDD's §5.1/§6 explicitly retired ("no HP, no Resistance,
no damage"). The mechanics are correct (control resistance / no-op shove); only the
copy needs a pass — see `docs/design/class-essence-and-signatures.md` for narrative
follow-ups.

Zones (Feather Trap, Root Egg) drop **at the caster's feet** and never affect their own
caster — "lay it as you flee."

### 7.3 Cooldown tiers

| Tier | Range |
|---|---|
| Short | 3–6 s |
| Medium | 8–12 s |

Exact per-ability values (steal amounts, stun/slow durations, radii, cooldowns) are tuned
against the Oracle and playtests.

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

The **Common/Character** split does not create pay-to-win: Character abilities are class
identity, not power tiers, and every ability is balanced against every other.

---

## 9. Cosmetics & Skins

Purely cosmetic, never affecting stats. Chicken skins (reskin any class) and map skins
(terrain/props/lighting mood). Acquired via purchase, battle pass, or slow currency.
Seasonal content is post-demo, contingent on success.

---

## 10. Controls & UX

- **Mobile-first**, all inputs work on touchscreen.
- **Movement:** left-thumb virtual joystick (also sets facing, which aims directional
  abilities and the Mark soft-lock).
- **Abilities:** right-thumb buttons — **4 for every class** (§7.1). 1-2 are forced by
  foraging/your specialization's signature; the rest are freely chosen. The Mark/Kill
  button (Reaper only) is **two-press**: first press marks, and it re-labels to **KILL**
  when the target qualifies.
- **Collecting:** passive — stand on a pile, cargo fills at Collection Rate.
- **Cooldowns:** bottom-up clip + seconds remaining. Ability buttons must feel responsive.
- Class silhouettes must read instantly in the isometric view.

### 10.1 Casting: hold to aim, release to fire (v0.6)

Abilities changed from **press-to-fire** to **hold-to-aim, release-to-fire**. The rule that
matters is short: **an ability always fires on release, never on press.**

**There is no tap window and no branch.** Every cast fires on release, full stop. Nothing
waits out a threshold before committing, and no press is ever swallowed. Holding does
**not** charge power; there is no minimum hold and no damage ramp.

**What the hold buys you, and what it costs.** `FeedbackTuning.TapHoldThresholdSeconds`
(~120 ms) is not a fire-path decision — it is a **cost/feedback ramp**:

| | Below the threshold (a quick tap) | Past it (a deliberate hold) |
|---|---|---|
| **Ground telegraph** | none | your true area, drawn |
| **Target marks** | none | every chicken in the area classified |
| **Wind-up glow rivals can see** | none | ramps up at your feet |
| **Movement** | **full speed** | the stick rotates your aim instead of moving you |

So the trade is explicit: *tap for speed, hold for information.* A player who wants to
keep moving taps and gives up the preview; a player who wants to aim holds and pays for it
in mobility. Both fire on release, with identical latency.

This also makes the wind-up glow mean something. It used to fire on every press, so rivals
learned to ignore it; now it only appears when someone is genuinely committing to an aim,
which is what makes "he's charging something, disengage" a real read.

> **Superseded (v0.6.1).** This section previously said the threshold "only ever changes
> what gets drawn, never when the cast happens", and carried a warning that the 120 ms had
> to be clocked off the local raw input timestamp rather than the replicated hold bit,
> because Shared Mode's ~50 ms of input latency would otherwise inflate the effective
> threshold to ~170 ms and feel laggy. Both are retired. The first is now too narrow — the
> threshold governs the *movement cost* as well as the drawing. The second assumed a tap
> had to **wait out** the window before committing to fire, which is exactly the design
> Maestro removed: a cast fires on release with unchanged latency, and the threshold
> measures a *duration*, which is preserved under a constant input delay. The charge state
> is therefore clocked in whole simulation ticks (4 ticks at 32 Hz = 125 ms).

**While holding:**

| | |
|---|---|
| **You see** | The ability's true area on the ground, and every chicken inside it marked — accent + solid + pulsing for "will be hit", grey + dashed + ⃠ for "in the area but immune / no-op". What you see marked is exactly what gets hit; the preview and the real scan run through the same `WouldAffect` predicate. |
| **Rivals see** | Only a growing glow at your feet — a wind-up tell, not your area. This asymmetry is deliberate: it preserves counterplay ("he's charging something, disengage") without turning the arena into a solved puzzle. |
| **Aiming** | Once past the threshold, directional abilities (cone / forward-offset / swept capsule / jump) let the movement stick **rotate your aim** instead of moving you. Caster-centred shapes (self-circle, aura, single-target) are rotation-invariant, so they keep normal movement even on a long hold — there is nothing to aim. |
| **If it goes illegal** | If you get stunned mid-hold, or the last valid target walks out, the preview washes to red-grey and the button shows why. Releasing then **cancels without burning the cooldown**. |

**Cancelling a held ability** — drag your thumb off the button (past
`FeedbackTuning.DragCancelDistancePx`, ≈55 dp) on touch, or press **Esc** on desktop.
Losing pointer capture to an OS gesture also counts as a cancel, never as a fire — an
involuntary loss of tracking is not a deliberate "let go to cast".

### 10.2 What each control state prevents, and how you're told

The control ladder is §6.3's; this is how the game *communicates* it. The rule is that a
state is shown **where it bites** — on the control it disables — not only as an abstract
badge somewhere.

| State | Move | Cast | Collect | How the HUD says so |
|---|---|---|---|---|
| **Free** | ✅ | ✅ | ✅ | Nothing marked. |
| **Slowed** | reduced | ✅ | ✅ | Stick tints cyan with a **`×0.45`** magnitude label. Ability buttons untouched — casting is unaffected, and greying them would say the opposite of the truth. |
| **Rooted** | ❌ | ✅ | ❌ | Stick greys out and shows a **⛓** shackle. Ability buttons stay normal — **you can still cast while rooted**, and that is a real tactical option the UI must not hide. |
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

A dead button always says why, and the four reasons are told apart at a glance:

| Reason | What you see |
|---|---|
| **On cooldown** | Button darkens from the bottom up, with the **seconds remaining** in the centre. |
| **No valid target in range** | Colour drains to grey and a **⃠** appears. |
| **Stunned — can't cast** | Red wash and a **✕**, on all buttons at once. |
| **Another ability is running** | The whole cluster dims; the **running** ability's button keeps full colour and drains from the top down. |
| **Slot not available for your class** | The button isn't rendered at all. |

Pressing a refused button **shakes it and clicks** — a press is never silently swallowed.

---

## 11. Open items & deferred

| # | Item | Status |
|---|---|---|
| 1 | Per-ability values (steal/stun/slow/cooldown/radius) | Tuning against Oracle + playtest |
| 2 | ~~Passive pools reimplementation~~ **Specializations** (§5.3) | **Shipped.** 5 of 8 signatures assigned. The other 3 (Slippery / Featherfoot / Hoarder) need **ordinary** signature abilities — the Peck-variant plan was retired by §7.1's simplified slot model on 2026-09-04 |
| 2c | **The arena is too big for 4 players in 45 s — you barely see combat** | ⚠️ **Known, accepted for now, must be solved.** Observed in play: four chickens on a 51.3 m square rarely meet, so the control-and-steal systems that the whole v0.4 redesign exists to serve go unexercised. The map reads as a place, but a place built for more players than it has. See §3.9 |
| 2a | Wing Slam's cd 12 contradicts Warrior's "spams abilities" essence | Flagged, needs re-tuning |
| 2b | Specialization visual identity (per-spec colour/model) | Designed (§5.3), concepts need regenerating in the shipped house style |
| 3 | **Last-15 s endgame rule** | Deferred until base loop is fun; 3 candidates parked (Open Bases / center-collapse / Golden Egg) |
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

## 13. Implementation status (v0.4)

Reflects the four committed v0.4 plans (`docs/superpowers/plans/`).

- **Balance Oracle** — shipped. Pure-C# SCT simulator + target harness (`Assets/_Game/
  Scripts/Balance/`), EditMode-tested.
- **Map & walls** — shipped. Tiered 80-food budget, pinwheel walls on `ObstacleClass`,
  centre pile no longer permanent/regenerating (fixed supply).
- **Combat** — shipped. HP/damage pipeline deleted; `ControlState`/`ControlRules` ladder;
  `StealMath` cap; `RPC_ApplyStun`; `AssassinExecute` (Mark/Kill, bounty bag);
  `MatchConfig` W=40 / 45 s / Spoiler +15 / DepositRate 9; class stats Oracle-solved.
- **Ability pool** — shipped. Steal/Control/Defense/Utility categories; ex-damage abilities
  converted to steal; new Ambush / Wing Slam / Shadowstep / Mark/Kill; roster masks;
  Spine Coat steal-back rate-gated.

**Known gaps:** passive pools (§5.3) still carry the pre-0.4 set; endgame rule deferred;
the Oracle re-solve rests on the simulator's model refinement (bank a winning load without
overfilling) and has not been re-verified in a live human playtest — **that playtest is the
next step.**
