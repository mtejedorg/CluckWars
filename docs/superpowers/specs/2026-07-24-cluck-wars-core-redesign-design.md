# Cluck Wars — Core Redesign v0.4 (Axiom-Driven Balance)

**Date:** 2026-07-24
**Status:** Design approved — pending spec review, then implementation plan
**Supersedes:** GDD v0.3 combat model (HP/damage), win target, match length, class roles

---

## 0. Why this exists

The game "isn't funny" because it was never *defined*. Cute chickens + a battlefield +
chaotic abilities was hoped to be enough; it wasn't. This document replaces hope with
**axioms**: a small set of load-bearing rules from which every stat, food value, and
ability is *derived* rather than guessed. The central diagnosis:

- **HP/damage is a second currency that doesn't feed the first.** The chain
  button → reduce HP → HP zero → stun → drop cargo → walk over cargo is six steps of
  indirection. A fight never feels like it's about food. And because nothing the victim
  does mid-chain changes the outcome, the only variable is who has a cooldown up — the
  "fastest cooldown wins" problem.
- **A 5s death stun is 2.8% of a 180s match but ~11% of a 45s match.** The death model
  *had* to change before the timer could.
- **`TerrainTraversal` (Vault/Barge/Blink vs Low/Standard/Tall) is already built and
  tested** but the arena has too few obstacles for it to matter. "Jumping obstacles is
  critical" is a level-design fix, not an engineering one.

---

## 1. The Axiom — Solo Clear Time (SCT)

> A **naked** chicken (no abilities, no passives), **alone** on the **full** map,
> farming greedily with return-when-full, banks the win target **W** in exactly **N**
> seconds via exactly **T** base trips.

Every stat and food value is a **dependent variable** solved against SCT. You do not
tune `MoveSpeed` because 9 feels nice — you tune it because Speedy must land on 30s.
This is the difference between balancing and guessing.

Identity: `trips = ceil(W / CargoCapacity)`.

| Class | SCT | Trips | Cap | Economic strategy | Where the gap comes from |
|---|---|---|---|---|---|
| **Speedy** | 30s | 4 | 10 | **Throughput** — many small loads, roams wide | baseline |
| **Fatty** | 30s | 2 | 35 | **Payload** — one huge sweep, barely returns | Cap ↑↑, Speed ↓ |
| **Warrior** | 35s | 3 | 14 | **The ability class** (mid stats, leans on buttons) | mid everything |
| **Assassin** | 40s | 4 | 10 | **Denial** — never actually farms | **CollectionRate ↓↓** |

**Constants:** `W = 40` · `F = 80` (total map food = 2W).

### 1.1 Derived findings (contradict the current SOs — must be re-solved)

- Current caps are off by 3–5×. At W=40: Fatty 35, Speedy 10, Warrior 14, Assassin 10.
- **CollectionRate roughly doubles across the board.** Speedy at 4 trips in 30s has
  ~2.8s per pile visit to fill 10 units → rate ≈ 3.5–4.3/s (currently 2).
- **Speedy and Assassin share cap (10) and trips (4) but clear in 30s vs 40s.** The 33%
  gap *cannot* come from movement (they must both stay fast) — **it comes entirely from
  CollectionRate.** Assassin is a fast chicken who is genuinely bad at farming. That is
  the mechanical reason he denies instead of collects.
- **`DepositRatePerSecond` is a hidden tax that scales with trip count.** At 6/s it
  costs Speedy ~8s of a 30s clear. It is a clean lever for tuning Speedy-vs-Fatty
  without touching speed or capacity.

Exact stat values are **outputs of the Balance Oracle (§7)**, not hand-authored here.

---

## 2. Map Budget

At F=2W, pure farming is viable (two players *could* max out by farming alone), so the
race is honest and combat's job is to **buy time, not food**. Every ability must cost the
opponent tempo; **steal** is the one mechanic that converts tempo directly into food.

| Tier | × | Each | Σ | Role |
|---|---|---|---|---|
| T1 doorstep | 4 | 5 | 20 | Fatty's top-up; safe-ish early; invadeable |
| T2 contested | 4 | 10 | 40 | Fight for it or rush it |
| T3 center | 1 | 20 | 20 | Fight for it |
| | | **F** | **80** = 2W | |

**Fatty's route pins the budget.** Cap 35, W=40:
- Trip 1: a 5-food **doorstep top-up** (→ T1 = 5).
- Trip 2: a **35-food sweep** of the rest of the map (T3 20 + two T2s 20 = 40, capped to
  35), banked as the winning deposit.

The sweep is forced *through* the contested ring and center — **Fatty cannot win without
walking past everyone.** That single long walk is the whole match.

**Walls:** pinwheel layout per the reference sketch (radial black wedges + a ring),
built on the existing `ObstacleClass` (Low / Standard / Tall) so `Vault` / `Barge` /
`Blink` finally have geometry to route around. Walls block movement, not sight. Chases
become *routing* plays, not pure speed races.

---

## 3. Combat — Control + Steal

**No HP. No damage. No death** except the Assassin execute (§4). All interaction is
control effects and theft.

### 3.0 Casting & authority model

- **Targeted casting, never skill-shot. Abilities cannot be dodged — only counterplayed.**
  Counterplay happens *before* the button (positioning, saving a defensive ability), not
  as a twitch reaction to a projectile. This is a deliberate networking choice: there is
  no projectile to lag-compensate, so no "I dodged on my screen but died on theirs."
- **The caster endpoint does NOT have the last word.** A cast is a *request*; the
  authority (host / `StateAuthority`) **validates it with tolerance** — range, isolation,
  cooldown, target legality — before the effect applies. This prevents trivial client-side
  hacking (spoofed range/cooldown). Fits the existing convention
  (`[Rpc(RpcSources.All, RpcTargets.StateAuthority)]` + the `HasStateAuthority` gate) and
  the planned Server-Mode-post-funding authority model. "Undodgeable" means the *target*
  can't react out of it, **not** that the caster's word is final.
- **North star: every effect is self-centered or directional — nothing selects an
  external target.** Both resolve trivially on the authority (no target to arbitrate) and
  need at most one thumb, which is why this is simultaneously the cheapest networking
  model and the lowest input complexity.
- **Targeting taxonomy** (resolved):
  | Type | Target | Input | Notes |
  |---|---|---|---|
  | Self (buff) | none | press | e.g. Speed Burst, Turtle, Egg Shell, Invisibility |
  | AoE-around-self | everyone in radius | press | Cluck Shock, Feather Aura, **Peck, Sneaky Steal, Ambush** |
  | Placed zone | **drops at caster's feet** (a sub-case of AoE-around-self) | press | Feather Trap, Root Egg — "lay it as you pass/flee," not forward-thrown |
  | Directional / line | first valid target in facing (auto-snap, can't miss) | press | Flying Peck, Roll & Push |
  | Single-target | nearest **isolated** rival in facing arc + soft-lock reticle | press (aim = facing) | **the Assassin Mark/Kill ONLY** |

  **The Assassin Mark/Kill is the single lone single-target ability in the game** — every
  other formerly-single-target ability (Peck, Sneaky Steal, Ambush stun) is now
  AoE-around-self. This finishes the collapse toward "self-centered or directional":
  the one targeted exception is deliberate, because the whole Assassin fantasy is *picking
  a specific victim*. Its soft-lock reticle previews who Mark will hit (turn to face to
  switch); the pick is a hint, the authority still validates (§3.0).

### 3.1 Control ladder

| State | Move | Use abilities | Collect | Notes |
|---|---|---|---|---|
| **Slowed** | reduced | ✅ | ✅ | collision / pile / ability slow |
| **Rooted** | ❌ | ✅ | ❌ | the lesser hard-control; counterplay stays open |
| **Stunned** | ❌ | **❌** | ❌ | the only state that locks abilities |

Stun locking abilities is *why* it is the correct execute qualifier — the victim cannot
cleanse or ability-escape out of it. **Stun duration is authored per-ability** (like
cooldown tiers), in the ~1s neighborhood. The Assassin's own stun (the one that qualifies
his execute) is **1.0s**; other classes' stun abilities may run longer.

### 3.2 Steal — the natural cap

```
stolen = min(abilityStealValue, attacker.Capacity − attacker.Cargo, defender.Cargo)
```

No immunity windows, no artificial timers. The formula self-regulates:

- **A loaded thief cannot steal** (no free capacity). Theft becomes a rhythm: steal →
  bank → return. Not a drain.
- **Fatty bleeds, never pops.** His 35 can only leave in ≤10–14 bites; a Speedy stealing
  10 is then *full and must go home*. Three chickens can't strip him at once because each
  is capped by their own free space — fair to the attackers, survivable for Fatty (whose
  defensive abilities are what he's actually playing).
- **Cargo capacity = theft capacity.** This is what makes Warrior's Bully passive
  (+cargo) coherent: more capacity literally means bigger heists.

**No cargo ever hits the floor.** Steals transfer attacker←→victim directly. There is no
"spill" mechanic anywhere in the game. (The sole cargo transfer that isn't a steal is the
Assassin execute — see §4.)

### 3.3 Knockback

Retained as involuntary displacement + collection interrupt. No damage, no cargo drop.

---

## 4. The Assassin Execute (the only hard removal)

**One locked slot, one button, two presses.** Mark/Kill occupies a **fixed signature
slot** on the Assassin (cannot be swapped) — this is *why* the Combo passive grants a 3rd
slot: one slot is spent on the signature, the other two are the player's to build with.

| Stage | Input / condition | Victim / third-party counterplay |
|---|---|---|
| **Mark** | Press → soft-locks the nearest **isolated** rival (no chicken within **R = 8m**) in the facing arc. Visible to the target (panic indicator). | Stay near others — in an FFA, safety = approaching an enemy |
| **Arm** | **X = 2.0s** elapse with the mark alive | Break isolation (someone enters R) or escape range → mark fizzles harmlessly |
| **Qualify** | The marked+armed target enters a **Stun** — from the Assassin's own **Ambush**, or *any* stun in the match. Button lights up as **KILL**. | Don't get stunned; hold a defensive ability for exactly this |
| **Kill** | **Press again** while the target is stunned **and the Assassin is NOT himself stunned** | **A third party can stun the Assassin during the window to deny the kill** — stun the hunter to save the prey |

On success: victim's **entire cargo → the Assassin as a capacity-exempt "bounty bag"**
(he becomes a courier carrying a stolen win he couldn't otherwise hold — the one time
capacity stops being the governor); victim **removed ~2s**, respawns at base empty.

On **fail** (window passes, mark fizzles, or the Assassin is stunned through it):
Mark/Kill goes on a **reduced cooldown** (lower than a successful kill), so he re-marks
and keeps pressure fast.

**Three counterplay gates** — two for the victim (stay in a crowd; don't get stunned),
one for third parties (stun the hunter mid-window). The kill is *hard*, so when it lands
it is spectacular and earned.

---

## 5. Passives

Each class has a **pool** of passives; the player picks **one** at character select.
(Generalized from "1 of 2" so adding a 3rd/4th to any class later is free.)

Passives sit **outside the naked SCT** — the axiom is measured with no passive equipped,
so cargo-changing passives (Hoarder, Bully) are legal even though they'd change a farming
route. This is already established for Fatty ("with the cargo passive he backs just the
time he wins — that's apart from the balancing scene").

| Class | Passive | Effect | Cost |
|---|---|---|---|
| **Speedy** | Slippery | Control **duration** reduced (also shortens the Assassin's Arm exposure) | — |
| | Featherfoot | Immune to **pile slow** — dip a contested pile at full speed, gone before the scrum | — |
| | Drop & Go | **Instant deposit** (removes the trip-count tax) | ⚠ ~27% of SCT — **requires a Speedy stat re-solve** if equipped is to be balanced; Oracle catches it |
| **Fatty** | Hoarder | Cap ≥ W → wins in **one** trip | — |
| | Bulwark | Control effects reduced | — |
| **Warrior** | Relentless | Abilities more **often** (lower cooldowns) | — |
| | Bully | Abilities **stronger** → bigger steals, **+cargo** to hold them | — |
| **Assassin** | Spoiler | If timer expires with **no winner**, bank **+15** food, then normal "most banked wins" resolution runs | — |
| | Thief | Steal-focused (values TBD) | — |

**Warrior is the ability class**, not a "tempo" generalist. Baseline Warrior has *no*
cooldown reduction — Relentless grants it. His two passives are the two axes an ability
can improve on: frequency (Relentless) or magnitude (Bully). Both pure-amplify, no
drawback, no slot change — balanced purely by numbers (the Oracle's job).

**Spoiler guard:** it is **+15**, not an auto-win. 15 vaults a mid-pack Assassin over a
typical stalemate leader (~25–35 held) but cannot win from zero, so "idle in a corner and
stall" is never a winning line. A second Assassin in a lobby is well-defined (both get the
+15; normal resolution decides).

---

## 6. Match

- **Length: 45s.** (Target was 45–60; 45 chosen. 15s "special rule" endgame deferred.)
- **Win:** first to bank W=40, else most banked at 0:00 (existing tie-break: food → kills
  → lower corner index).
- **Endgame rule: DEFERRED.** Not implemented until the base loop is balanced — tuning an
  endgame rule against an unbalanced base is tuning against noise.

---

## 7. The Balance Oracle (the automated balance test)

The tool the axioms make possible. Two layers:

### 7.1 Model layer — pure C# EditMode simulation

- **No Fusion, no PlayMode**, runs in milliseconds.
- **Inputs:** real pile coordinates + values, base positions, per-class stats, W.
- **Policy:** the greedy "farm nearest, return-when-full" line the SCT axiom describes,
  on **real pile coordinates** (not distance averages — the corner-to-center diagonal is
  nothing like a doorstep hop, so averaging is wrong).
- **Outputs:** SCT (seconds) and trip count per class.
- **Assertions:** each class within **±1.5s** of its target SCT and exact trip count.
  Targets are **configurable constants** — change four numbers, re-solve the whole game.

### 7.2 Validation layer — PlayMode bot

- A slower PlayMode test drives a real bot on the real navmesh + physics and checks it
  lands within **~15%** of the model's SCT.
- **Divergence is itself a signal:** if the bot is much slower than the model, the walls
  are costing more travel time than the geometry implies — exactly the level-design
  feedback you want.

---

## 8. Deferred / future (recorded, not in scope now)

- **Endgame rules** — three candidates, all validated as class-neutral and food-value-safe,
  to be added later as random-spawn events and A/B tested:
  1. **Last Call** — at T-15s all remaining pile food collapses into one center jackpot.
  2. **Open Bases** — at T-15s all bases unseal; banked food becomes raidable.
  3. **Golden Egg** — a single ~15-food objective spawns center; carrier is revealed and
     slowed; must be physically banked. (Injects food beyond 2W — a true comeback valve.)
- **King of the Hill — a future CLASS,** not a Warrior passive: a zone-tax squatter who
  "owns" a pile and taxes rivals collecting from it. Parked deliberately to keep Warrior
  as the ability class.
- **Spoiler v2 — earned bounty:** +N per steal/execute landed during the match (capped),
  replacing the flat +15. Philosophically correct (makes stalling strictly worse than
  hunting) but adds an accumulator to track; adopt once there's data.
- **Speedy's Drop & Go** — if it ships as a real pick, requires re-solving Speedy's
  speed/cap so his equipped-SCT stays honest.

---

## 9. Open items rolled into implementation

- Exact stat values per class (Oracle outputs).
- Per-ability stun durations, steal values, cooldowns (authored, then Oracle/playtest).
- **Single-target selection rule** and **placed-zone placement method** — the two OPEN
  targeting decisions from §3.0.
- Wall layout parameters (count, tier mix, keep-clear radii) — seeded, validated by the
  Oracle's validation layer.

### 9.1 Ability-pool model (ADR 0003 Decision 3 — already in code)

The GDD §7.1 line "all abilities available to all classes — no restrictions" is **stale**;
the code superseded it with a **Common vs Character** split (`AbilityBaseSO.SlotKind` +
`AllowedClasses` bitmask). GDD §7.1/§5 must be updated to match. Current loadout:
**1 mandatory class-passive + 1 Common + 2 Character** (Assassin +1 Character via Combo).

Current assignment (authored under the *old damage model* — now partly incoherent):

| Pool | Abilities | Note |
|---|---|---|
| Common | Peck · Egg Shell · Speed Burst | Peck is a *damage* ability → must become steal |
| Warrior | Cluck Shock · Flying Peck · Spine Coat | **two-thirds damage** → whole kit needs rework |
| Speedy | Feather Aura · Feather Trap · Invisibility* | control-heavy, mostly survives |
| Fatty | Roll & Push · Root Egg · Turtle Mode | control + defense, mostly survives |
| Assassin | Doppelganger · Sneaky Steal · Invisibility* | *Invisibility shared Speedy+Assassin (mask 10) |

**The ability-pool rewrite is therefore a full follow-up spec, not a rename pass** — every
Damage-category ability (Peck, Cluck Shock, Flying Peck) becomes a Steal/Control ability,
which guts Warrior's Character pool and the shared pool and must be redesigned around the
"abilities buy time / steal converts tempo to food" brief. The Assassin gap-closer
(innate trait vs a class-gated Character ability) is decided there too — no universal-pool
rule is broken either way, since Character abilities are already class-gated.
