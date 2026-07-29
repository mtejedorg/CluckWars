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
**12 m long × 0.5 m thick**, and each has exactly **one opening, at one end**:

| | Spans | Opening |
|---|---|---|
| **Outer-gap wall** (4) | r 10 → 17.6 | 3 m at the **rim** |
| **Inner-gap wall** (4) | r 12 → boundary | 3 m at the **hub** |

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
| **Fatty** 🐔 | 7.5 | 35 | 3.2 | 30 s / 2 trips | *"Slow, but it all fits in the beak."* |
| **Speedy** 🐤 | 9.0 | 10 | 3.0 | 30 s / 4 trips | *"If you can't catch me, you can't rob me."* |
| **Warrior** ⚔️ | 7.5 | 14 | 2.6 | 35 s / 3 trips | *"The farm is a battlefield."* |
| **Assassin** 🗡️ | 9.0 | 10 | 1.7 | 40 s / 4 trips | *"Blink and your food is gone."* |

**The load-bearing detail:** Speedy and Assassin share Move Speed (9.0) and cargo (10) but
clear in 30 s vs 40 s — the entire gap is **Collection Rate** (3.0 vs 1.7). The Assassin is
a fast chicken who is *genuinely bad at farming*; that is the mechanical reason he denies
instead of collects.

### 5.3 Passives (design — reimplementation pending)

Each class has a **pool**; the player picks one. *These pools are the v0.4 design intent;
the passive system in code still carries the pre-0.4 set and is scheduled for a follow-up
pass — treat this table as the target, not the shipped state.*

| Class | Passive | Effect |
|---|---|---|
| **Speedy** | Slippery | Control **duration** reduced (also shortens the Assassin's arm window on you) |
| | Featherfoot | Immune to pile-slow — raid a contested pile at full speed |
| | Drop & Go | Instant deposit (removes the trip-count tax) |
| **Fatty** | Hoarder | Capacity ≥ win target → banks a full win in one trip |
| | Bulwark | Control effects reduced |
| **Warrior** | Relentless | Lower cooldowns — abilities more often |
| | Bully | Stronger abilities → bigger steals, + cargo to hold them |
| **Assassin** | Spoiler | If the timer expires with **no** winner, bank **+15** food, then normal resolution |
| | Thief | Steal-focused |

Passives sit **outside the naked SCT** (the axiom is measured with none equipped), so
cargo-changing passives (Hoarder, Bully) are legal.

---

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

**One locked signature slot, one button, two presses.** This is why the Assassin's Combo
gives a 3rd slot — one is spent on Mark/Kill, two are his to build with.

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

### 7.1 Rules

- Loadout = **1 mandatory class passive + N freely-chosen active abilities**, where **N = 2**
  normally and **N = 3** for the **Assassin** under the **Combo** passive (one of the 3 is the
  **locked Mark/Kill**).
- **Common** abilities are open to every class; **Character** abilities are gated by a
  per-class mask (`AbilityBaseSO.SlotKind` + `AllowedClasses`).
- **The Common slot is optional, not mandatory** — a player may fill their N active slots
  with any mix of Common and class-legal Character abilities, including zero Common ones
  (bounded only by the pool actually having 3 Common abilities to pick from). This is a
  **deliberate, explicit design reversal of an earlier "1 mandatory Common" rule** (2026-07-27
  directive) — **the current stance, at least for now**, and may be revisited later.
- Every ability is balanced against the others — monetization must never be pay-to-win.

### 7.2 The pool

| Category | Ability | Class(es) | Targeting | Effect |
|---|---|---|---|---|
| **Common** | Peck 🐦 | all | AoE-self | Steal a little from nearby rivals + minor knockback |
| | Egg Shell 🥚 | all | self | Invulnerable egg, immobile while active |
| | Speed Burst 💨 | all | self | Short speed boost |
| **Speedy** | Feather Aura 💨 | Speedy | AoE-self | Slow nearby rivals |
| | Feather Trap 🪤 | Speedy | zone-at-feet | Slow zone left behind |
| | Invisibility 👻 | Speedy + Assassin | self | Temporarily invisible |
| **Fatty** | Roll & Push 🌀 | Fatty | directional (Barge) | Barge forward, knockback |
| | Root Egg 🌱 | Fatty | zone-at-feet | Roots the first rival to step on it |
| | Cluck Shock ⚡ | Fatty | AoE-self | Knockback shockwave — shove the swarm off |
| | Turtle Mode 🐢 | Fatty | self | Near-zero speed, heavy control resistance |
| **Warrior** | Flying Peck 🪽 | Warrior | directional (Vault) | Vault-dash, steal on contact |
| | Wing Slam 💥 | Warrior | AoE-self | 1.5 s stun |
| | Spine Coat 🦔 | Warrior | self-aura | Steal back + knockback anyone who touches you |
| **Assassin** | Mark/Kill 🎯 🔒 | Assassin | single-target | The execute (§6.4) — locked signature slot |
| | Ambush 🗡️ | Assassin | AoE-self | 1.0 s stun (sets up his own execute) |
| | Sneaky Steal 🤏 | Assassin | AoE-self | Baseline rob |
| | Shadowstep 👤 | Assassin | directional (Blink) | Short blink dash over walls |
| | Doppelganger 👥 | Assassin | self (Blink) | Decoy copy |

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
- **Abilities:** right-thumb buttons — 2 for most classes, 3 for Assassin. The Mark/Kill
  button is **two-press**: first press marks, and it re-labels to **KILL** when the target
  qualifies.
- **Collecting:** passive — stand on a pile, cargo fills at Collection Rate.
- **Cooldowns:** radial fill + grey-out. Ability buttons must feel responsive.
- Class silhouettes must read instantly in the isometric view.

---

## 11. Open items & deferred

| # | Item | Status |
|---|---|---|
| 1 | Per-ability values (steal/stun/slow/cooldown/radius) | Tuning against Oracle + playtest |
| 2 | **Passive pools reimplementation** (§5.3) | Designed, not yet in code |
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
