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
be opened**. Its physical footprint is a continuous function of the food remaining in it:

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

> **The map opens as the match progresses. Early game is a maze; late game is a track.
> Every class's power curve is a function of how open the map is.**

Anything that breaks the monotonic open-up (refilling piles mid-match, spawning new
obstacles) must be justified against this pillar. **The center pile is the one sanctioned
exception — see below.**

### Decision 2b — The center pile is permanent (the anchor)

The center pile **can never be removed**. It is the one piece of terrain that survives to the
final second.

```
Amount ∈ [floor, max]      // floor ≈ 60% of max — never drainable below
regen  → slowly refills toward max
```

Conceptually: *a mountain with food on it. You harvest the surface; the mountain stays.*

**Why this is required, not optional.** Without it, every traversal ability becomes dead
weight exactly when the map opens — Vault, Barge and Blink are all useless on an empty field,
and the Run/Skip/Deny triangle collapses to pure Run in the endgame. That is the precise
failure the triangle exists to prevent. A permanent central obstacle keeps **Skip** live for
the whole match, and gives Fatty a permanent home turf (a cleaner fix for Fatty's late game
than the terrain-maker mitigation below — the two are complementary).

The late-game arena becomes a **donut**: open field, one contested core, fights orbiting it.
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
| **Mountain** | 100–75% | full radius, solid | route around entirely; max cover |
| **Plateau** | 75–40% | same radius, 3–4 channels cut through | chokepoints — fight lanes open |
| **Mesa** (floor) | 40%–floor | smaller solid core + wide slow-apron | permanent; max contest, min cover |

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

The change is to make radius and scale **continuous** in `Amount/MaxAmount` rather than
binary. `Amount` is networked, so all peers agree with no new state.

⚠ **The collection-radius coupling.** `_blockerRadius` is deliberately held *under*
`CollectRadius` minus the chicken capsule so edge collection still works. A continuous
radius must respect that bound **at its maximum**, or collection silently breaks — this is
exactly the 2026-06-01 game-breaking bug (every match ended 0-0-0-0). Re-derive the bound;
do not just scale the number up.

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
6. **Final-minute events.** The existing comeback events / golden pile (IP2) should be tuned
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
