# ADR 0003 — Movement, Terrain, and the Ability-Slot Model

**Status:** Accepted (design) · **Date:** 2026-07-21 · **Author:** Maestro + Claude
**Supersedes:** the flat global ability pool (all 14 abilities equippable by all classes)

---

## Context

Three juice passes (procedural audio, control-state visuals, cast shake, aggressive bots)
shipped between 2026-06-13 and 2026-07-14. The game still is not fun. That is informative:
the deficit is **not** feedback or polish — those were genuinely fixed. It is the
moment-to-moment movement decision.

Two concrete symptoms:

1. **Chases are pre-decided.** `MoveSpeed` is Speedy 10 / Assassin 9 / Warrior 7 / Fatty 4 —
   a **2.5× spread**. Whoever is faster wins, and the map is open enough that a straight
   line always works.
2. **No class identity.** Every class can equip any of the 14 abilities, so classes differ
   only by stat line.

The spread cannot simply be compressed: with speed as the *only* mobility currency, a small
spread makes all four classes feel identical. Terrain is what makes compression possible.

---

## Decision 1 — Three mobility currencies (the triangle)

| Verb | Cost | Beats | Loses to |
|---|---|---|---|
| **RUN** — raw `MoveSpeed` | free, continuous | Skip (it is on cooldown) | Deny |
| **SKIP** — terrain traversal | cooldown, burst | Deny (vault the trap/wall) | Run |
| **DENY** — zones, traps, walls | cooldown, positional | Run (stops the sprinter) | Skip |

New field on `AbilityBaseSO`:

```csharp
public enum TerrainTraversal : byte { None, Vault, Barge, Blink }
```

- `None` — default; all 14 existing abilities start here.
- `Vault` — arcs **over** low obstacles (interior walls, pile blockers).
- `Barge` — plows **through** low obstacles.
- `Blink` — instant reposition to a point. Rare, long cooldown.

**Boundary walls are never traversable.** Arena containment is non-negotiable.

### The balance rule

> **Speed is continuous and cheap. Traversal is discrete and expensive.**
> **The fastest class gets no traversal. The slowest gets the best.**

| Class | `MoveSpeed` now | Proposed | Terrain answer |
|---|---|---|---|
| Speedy | 10 | **9** | none — runs around; speed *is* the answer |
| Assassin | 9 | **8.5** | **Blink** (Doppelganger) |
| Warrior | 7 | **7.5** | **Vault** (Flying Peck) |
| Fatty | 4 | **6.5** | **Barge** — shortest routes on the map |

Spread drops **2.5× → 1.4×**, yet classes feel *more* distinct because they differ in
**kind**, not degree.

---

## Decision 2 — Piles are consumable terrain (THE PILLAR)

**This is the core mechanic of the game, not a tuning detail.**

A food pile is three things at once: a **resource**, an **obstacle**, and a **lane that can
be opened**. Its physical footprint is a **stepped** function of the food remaining in it
(discrete buckets, not continuous — see the NavMesh warning in Implementation notes):

```
blockerRadius  = f(Amount / MaxAmount)
visualScale    = f(Amount / MaxAmount)
```

As a pile drains it **shrinks** — from a hard obstacle you must route around, to a soft
speed-slower you can cross, to nothing at all. **Emptying a pile is a permanent edit to the
map**, performed by a player, as a side effect of scoring.

### The match arc this produces

| Phase | Map | Dominant | Why |
|---|---|---|---|
| **Early** | dense, cluttered | **Fatty** | short routes through obstacles; big cargo; straight lines don't exist |
| **Mid** | opening up | Warrior / Assassin | vault and blink still beat partial cover |
| **Late** | open field | **Speedy** | straight lines finally work; raw speed is king |

This gives the match a **natural tempo arc with no scripting** — the endgame becomes frantic
because the terrain that slowed everyone down is gone. It also converts the speed spread from
a *flat* balance problem into a *time-dependent* one: Fatty is not "slow forever," it is
**strong early and must bank a lead** before the map opens. Speedy is not "fast forever," it
must **survive to the late game** it scales into.

**Strategic consequence:** which piles you empty is a terrain decision, not just a scoring
one. A Fatty may deliberately *not* empty a pile to keep its cover. A Speedy may empty piles
along its preferred lanes to open them.

### Design pillar (binding)

> **The map's *density* drops as the match progresses. Every class's power curve is a
> function of how open the map is.**

⚠ **Correction (2026-07-21):** an earlier draft framed this as "early game is a maze, late
game is a track." That overstated it. **Interior walls and boundary walls are permanent** —
`MapGenerator.BuildInteriorWalls` places up to 6 low wall segments that never disappear. Only
*piles* are consumable. So the real arc is:

```
early:  walls + centre + many outer piles     (dense)
late:   walls + centre                        (moderate — lanes, not an empty field)
```

That is a **gentler, better arc** than the original framing. The map never becomes a bare
plane, so routing always matters and no ability is ever fully obsoleted by the clock.

Anything that breaks the monotonic density drop (refilling outer piles mid-match, spawning
new obstacles) must be justified against this pillar. **The centre pile is the one sanctioned
exception — see below.**

### Decision 2b — The center pile is permanent (the anchor)

The center pile **can never be removed**. It is the one piece of terrain that survives to the
final second.

```
Amount ∈ [floor, max]      // floor ≈ 60% of max — never drainable below
regen  → slowly refills toward max
```

Conceptually: *a mountain with food on it. You harvest the surface; the mountain stays.*

**Why.** ⚠ An earlier draft justified this as "otherwise traversal abilities die when the map
opens." **That was wrong** — permanent interior walls already guarantee Vault/Barge/Blink
always have targets. The centre pile does not rescue the triangle; walls already do. Its
actual value is narrower but still real:

1. **Forced convergence.** As outer piles deplete, the centre becomes the only rich resource
   — the endgame reliably collapses into one contested arena instead of four players farming
   separate corners. This is the frantic-endgame mechanic.
2. **Fatty's anchor.** A permanent, large obstacle is home turf for the Barge/Deny class —
   complements the terrain-maker mitigation below rather than replacing it.
3. **A resource floor.** The late game always has something worth fighting over, so the match
   cannot decay into an empty-map stalemate.

The late-game arena becomes a **donut**: lanes between the permanent walls, one contested
core, fights orbiting it.

**"Empty" and "not collectable" are different states.** A permanent pile at its floor is
simultaneously *not empty* (it is still terrain, still blocking) and *not harvestable* (that
food cannot be taken). Every consumer must distinguish them, or the floor becomes an infinite
food source. Concretely: `Available = Amount − DrainFloor` is what cargo credits against, and
`HasCollectableFood` (not `IsEmpty`) is what collection and bot pile-targeting gate on. This
is the highest-risk detail in Decision 2b — it silently breaks the economy and parks bots on
the centre forever if missed.
A strictly better endgame shape than an empty plane.

**Do NOT make it literally infinite.** Unbounded food removes scarcity; the win condition
degenerates into an uncontested hauling-throughput race with no way to deny anyone. The floor
+ regen model gives something better: **the center pile's size is a live readout of contest
intensity.** One farmer → regen keeps pace, it stays fat. Three fighting over it → they
out-drain regen, it shrinks toward the floor, and cover vanishes exactly as the fight gets
crowded. Self-balancing and readable from across the map.

**Shape thresholds** (discrete, not continuous — readable for players *and* it avoids NavMesh
re-carve thrash):

| State | Amount | Form | Play effect |
|---|---|---|---|
Percentages are of the pile's **usable range `[floor, max]`**, not of `MaxAmount`. (An earlier
draft expressed them against `MaxAmount`, which was incoherent: with a 60% floor, "Mesa at
40%" is unreachable and the centre would never leave the top of the table. Normalising over
the usable range is also what makes the centre's size a visible readout of contest intensity —
across `[0,1]` the full-to-floor span is only 1.0 → 0.82, which nobody can see.)

| **Mountain** | top ~25% of range | full radius, solid | route around entirely; max cover |
| **Plateau** | middle | same radius, 3–4 channels cut through | chokepoints — fight lanes open |
| **Mesa** | at/near floor | smallest radius + wide slow-apron | permanent; max contest, min cover |

⚠ **Scope:** the Plateau's channels need multiple colliders, not one capsule — materially more
work than radius stepping. **Slice 1 ships radius-only stepping** (Mountain → smaller → Mesa);
channels are a later enhancement. That tests the tempo idea at a fraction of the cost.

**Camping is self-limiting** — scoring requires depositing at your *base*, so the centre is
inherently a round-trip and cargo capacity caps any stay. Camping to *deny* is legitimate
area denial and self-corrects in a 4-player FFA.

The final-minute comeback events / golden pile (IP2) should now be tuned to land at or near
the centre, reinforcing convergence.

---

## Decision 3 — Slot model: mandatory class passive + ability slots

```
ALL CLASSES        [Passive ×1 — mandatory, class pool]  +  [Common ×1]  +  [Character ×2]
ASSASSIN w/ COMBO  [Passive: Combo]                      +  [Common ×1]  +  [Character ×3]
```

The passive slot is **separate and mandatory** — it is the class specialization, chosen from
that class's passive pool. It never competes with an active for a slot, so it cannot erode
the "3–4 active abilities" fun target.

### Assassin's fork

Assassin's passive pool creates a real build decision:

- **COMBO** — a 4th ability slot. Breadth.
- **OPPORTUNIST** — bonus damage and steal vs. *slowed / rooted / stunned* targets. Depth,
  and plugs Assassin into the **Deny** corner of the triangle as the class that punishes what
  Feather Trap and Root Egg set up.

### Ability pools — all 14 placed, nothing wasted

**Common (pick 1)** — the primary colours, simplest possible: **Peck** (attack) ·
**Speed Burst** (mobility) · **Egg Shell** (defense)

| Class | Character pool (pick 2; Assassin+Combo picks 3) |
|---|---|
| **Warrior** | Cluck Shock · **Flying Peck** *(Vault)* · Spine Coat |
| **Speedy** | Invisibility\* · Feather Trap · Feather Aura |
| **Fatty** | Turtle Mode · Root Egg · **Roll & Push** *(Barge)* |
| **Assassin** | Sneaky Steal · **Doppelganger** *(Blink)* · Invisibility\* |

\* Invisibility is shared by Speedy and Assassin — abilities may be bound to **one or more**
classes.

### Passive pools

| Passive | Class | Effect |
|---|---|---|
| MIGHTY | Warrior | +25% outgoing ability damage (existing) |
| Bracer | Warrior | Vault cooldown −1.5s |
| SLIPPERY | Speedy | reduced control-effect duration (existing) |
| Second Wind | Speedy | regain speed faster after a slow expires |
| IMMOVABLE | Fatty | greatly reduced knockback (existing) |
| Juggernaut | Fatty | Barge shoves chickens as well as terrain |
| COMBO | Assassin | 4th ability slot (existing) |
| OPPORTUNIST | Assassin | bonus damage/steal vs. controlled targets |

### Guardrails

1. **No passive may grant traversal outright** — only *improve* traversal the class already
   has. Otherwise every build takes the traversal passive and the four-way differentiation
   collapses on day one.
2. **Power budget:** an active at 6s cooldown / 1.5s duration has ~25% uptime. A passive
   should be **40–60% of that active's peak effect at 100% uptime**. Above → passives
   dominate; below → dead content.

---

## Decision 4 — The collect → carry → deposit loop is load-bearing. Keep it.

Considered and **rejected**: removing carrying entirely (chickens "eat" at piles and score
directly). Also considered and **kept**: depositing only at your *own* base (already enforced
at `ChickenCargo.FindNearestBaseInRange` — `if (b.CornerIndex != homeCorner) continue;`).

### Why carrying stays

**The round trip *is* the game.** The loop has three phases with different risk:

| Phase | State | Risk |
|---|---|---|
| Collect | stationary at a pile | exposed, predictable |
| **Carry** | travelling, loaded | **maximum — this is when you are a target** |
| Deposit | at your base | safe |

The carry phase is the only reason players fight each other. Delete it and:

- **The entire ADR is pointless.** No travel → no routes → routing, chasing, interception and
  therefore Run/Skip/Deny all stop mattering. Decisions 1–3 exist to make *travel* interesting.
- **Death stops costing anything** but time — the cargo drop is the core risk/reward.
- **Sneaky Steal dies.** It exists solely because rivals carry cargo.
- **Fatty loses its identity** — "bulk carrier" (cargo 20) is meaningless with nothing to carry.
- Cargo bars, the FULL→RETURN TO BASE prompt, and the cargo-full VFX all become dead code.

If the round trip feels tedious, the fix is to make the *trip* interesting — which is exactly
what Decisions 1–2 do — not to delete the trip.

### Why own-base-only stays

A fixed, known destination is what makes **interception** possible. If a rival is at the
centre pile and their base is the NE corner, their route is knowable — so cutting them off
with a Vault shortcut is a *play*. Deposit-anywhere would let players always pick the nearest
base, shortening trips, randomising destinations, and destroying the geometry that chases
depend on.

**Predictability is a feature here, not a limitation.** It is the basis of all interception
play, and it is what makes corner identity and home-turf defence meaningful.

### Optional future: "graze" as a pressure valve

Not adopted now; recorded so it isn't re-derived. Allow eating at a pile for **immediate but
reduced** score (~30% of carried value). Creates a real decision under pressure: being chased
with full cargo and no safe route home? Graze to bank *something* rather than lose everything
on death.

⚠ **Tuning risk:** if the graze ratio is too generous everyone grazes, travel collapses, and
this becomes the rejected "eat-only" design by the back door. Only revisit after Slices 1–2,
and treat the ratio as hostile.

---

## Fatty's late game must not be a countdown to defeat

The arc above risks a feels-bad class: "Fatty loses once the map opens." Mitigation, and it
is load-bearing:

> **Fatty stops being a terrain *user* and becomes a terrain *maker*.**

Root Egg and Turtle Mode are portable terrain. On an open map, placed zones are the *only*
cover that exists — so Fatty's Deny kit naturally appreciates exactly as the map's natural
cover disappears. Fatty's curve should be a **U with a high start**, not a slide. Verify in
playtest: if Fatty's last minute feels hopeless, buff placed-zone duration/radius as a
function of match time rather than nerfing Speedy.

---

## Implementation notes

Most of Decision 2 is already plumbed:

- `FoodPile` already has `[Networked] Amount`, `MaxAmount`, `VisualScale`.
- `_blockerRadius` (0.65) / `_blockerHeight` (1.2) are already serialized fields.
- `CreateBlocker()` already builds the `CapsuleCollider` + `NavMeshObstacle` carve, and
  already deactivates when the pile empties.
- The line-24 tooltip already says the pile "will be visually scaled against" its amount.

The change is to make radius and scale **stepped** in `Amount/MaxAmount` rather than binary.
`Amount` is networked, so all peers agree with no new state.

⚠ **The collection-radius coupling.** `_blockerRadius` is deliberately held *under*
`CollectRadius` minus the chicken capsule + skin width, so edge collection still works.
Note the blocker is a **child** of the pile root and `CollectRadius` is a raw, unscaled
world-space float — so *shrinking is inherently safe* and the bound only ever binds **at
maximum size**. The robust fix is therefore not to re-derive a number but to **cap the top
bucket at exactly the currently-authored size**, which makes the whole class of bug
unreachable. Violating this reproduces the 2026-06-01 game-breaking bug (every match ended
0-0-0-0).

Measured margins at max (chicken radius 0.5 + skin 0.08; `CollectRadius` 1.6):
outer pile **0.37** clearance; centre pile at 1.5× root scale **0.045** — correct but thin.
That thinness is pre-existing, not introduced by this ADR, but the centre now spends far more
of the match at full size, so it is exercised harder. Watch centre-pile collection in
playtest; if it ever fails, drop `_centerPileVisualScale` 1.5 → 1.4 (margin 0.089) rather
than touching `_blockerRadius`.

⚠ **NavMesh churn.** A continuously-resizing `NavMeshObstacle` re-carves the NavMesh. Update
in steps (quantise to ~4–5 size buckets), not per-tick, or bot pathfinding will thrash.

For Decision 3, `PassiveAbilitySO : AbilityBaseSO` (Duration ∞, Cooldown 0, `OnActivate` once
on `Spawned`) reuses the entire existing pipeline — slot serialization, `AbilityRegistrySO`,
the character-select picker, bot loadout presets, networked `ActiveSlot`. Only two
special-cases: `AbilityController` must not expire a permanent, and the HUD renders a passive
slot without a cooldown ring.

---

## Open questions

1. **Pile exhaustion curve.** The map holds ~220 food; the win target is 110. Outer piles
   must be *partly* exhausted at the 3-minute mark — not fully, or the late game has nothing
   to fight over outside the centre. Target: outer pile mass ~30–40% remaining at timer
   expiry. Needs measurement. Note the centre's regen adds to effective map supply — retune
   `FoodTargetToWin` against total *throughput*, not starting mass.
2. ~~Center pile as last stand.~~ **RESOLVED — see Decision 2b.** Permanent, floored, with
   regen and discrete shape states.
3. **Center regen rate** is now the single most sensitive number in the design: too high and
   the endgame is a free farm with no scarcity; too low and the centre erodes to Mesa in the
   first minute and stops being an obstacle. Start conservative (regen ≈ one player's steady
   drain) and measure.
4. **Positive feedback loop.** Speedy's scoring action (emptying piles) also builds Speedy's
   late-game advantage. The opened lane benefits everyone, which dampens it, but watch for
   runaway leaders.
5. **Readability.** Players must be able to *see* a pile shrinking and predict when a lane
   opens. Without that the map "randomly" changes. Needs a visual pass. The centre's three
   named states (Mountain/Plateau/Mesa) should be visually unmistakable at a glance.
6. **`GameManager.RestockPiles()` contradicts the pillar — decision needed.** The `Restock`
   comeback event refills **every** pile by +10 (`GameManager.cs:765`). That re-densifies the
   map in the final minute, precisely when the design wants it open, and re-scatters the
   resource exactly when fights should be converging on the centre. It is a second,
   unacknowledged exception to the density pillar.
   **Recommendation:** drop `Restock` from the event pool, or repoint it at the centre pile
   only. The `GoldenPile` event already supplies comeback drama and — per item 7 — should
   spawn near the centre, which reinforces convergence instead of fighting it.
   *(Found during Slice 1 implementation; left unchanged as out of scope.)*
7. **Final-minute events.** The existing comeback events / golden pile (IP2) should be tuned
   to land in the now-open late game. Late-scaling passives (e.g. Second Wind) may want to
   key off match time.

---

## Build order (strict)

Do **not** build this in one pass. Ship the cheapest slice that can falsify the whole idea:

1. **Slice 1 — pile footprint + permanent centre.** `blockerRadius`/`visualScale` stepped
   from `Amount/MaxAmount` on outer piles; centre pile gets floor + regen + radius-only shape
   stepping (no channels yet). Cheap — the plumbing exists — and immediately testable: does
   the map opening up *feel* like a tempo change, and does the centre hold as a late-game
   arena?
2. **Slice 2 — Vault on Flying Peck only.** Add `TerrainTraversal`, implement `Vault`, bump
   wall density. Playtest **Warrior vs Speedy** — that single matchup tests the whole
   triangle.
3. **Slice 3 — slot restructure + passives.** Large UI/data work. Do not commit until 1 and 2
   feel good.
4. **Slice 4 — speed retune.** Last. The numbers are meaningless until traversal exists.

Re-measure bot pacing after each slice. All pacing data before 2026-07-18 is void (measured
with double-speed bots).
