# Class essence, specializations and signature abilities

**Status: BUILT, except the three Peck variants.** Agreed 2026-08-21, implemented 2026-08-23.
Suite at 394/394.

| Part | State |
|---|---|
| §1 one class per Character ability | **done** — `fe5d756`, zero shared |
| §4 nine new abilities | **done** — `a03ea01`, `529e95d` |
| §3 signature mechanism | **done** — `7406193` |
| §3 signature assignments | **5 of 8** — three need ordinary abilities assigned (Peck-variant plan retired 2026-09-04, see §3) |
| §5 Snatch / Speed Burst out of Common | **done** — `Common` is now Egg Shell + Peck only |
| Visual identity (per-spec models/colours) | **not done** — concepts need regenerating |

This document exists so the intent stops living only in chat — every drift bug this month
(Shadowstep shared with Speedy, `TerrainTraversal: 0` on three abilities, stale pile spans in
`JumpResolverTests`) came from a design decision that was never written down next to the thing
it governed.

---

## 1. Why we are doing this

Abilities were shared across classes to widen the loadout pools (`21ddaa8`). It worked
mechanically and cost us the thing that makes a 4-player FFA readable: **you could not tell
what a chicken was by what it did.** Maestro, 2026-08-21:

> "We made a mistake by making abilities available for multiple classes. We need to revert
> that and just add more abilities. By doing so we can keep the identity of each class. Only
> common abilities will be shared."

The rule:

- A **Character** ability belongs to **exactly one class**.
- Sharing is expressed only by **Common** slot.
- `AllowedClasses` stays a **bitmask**, because one class is a *design* limit, not a code
  limit. A deliberate two-class ability must remain possible; it just must not happen by
  accident. `BalanceEditorWindow`'s roster-integrity panel reports violations live.

---

## 2. The four essences

Stated by Maestro, translated here into the mechanical contract each one implies. **The
contract is the testable half** — if an ability does not serve it, it is in the wrong class.

### Assassin — *"the sneaky one"*
> Stealing from others, isolating them, catching them, and killing from the shadows.

**Contract:** every tool needs a *victim*. Setup → payoff, binary outcomes, high skill
ceiling. The only class that cannot forage (no Peck) — it eats what others carried.

### Speedy
> The fastest one. Pick all the grain you can in the shortest time. Tools for escaping and
> slowing others, but **never jumping obstacles, because it would be unfair.**

**Contract:** tempo and denial-of-pursuit. **`TerrainTraversal.None`, always** — enforced by
`DataIntegrityTests.Speedy_HasNoTerrainTraversalAbilityAvailableToIt`. Speedy already answers
*escape* (highest MoveSpeed) and *denial* (slow tools); traversal would be a third currency
with nothing traded for it.

### Warrior
> The bruiser. Good for all, expert in nothing. Spams abilities.

**Contract:** short cooldowns, broad coverage, and **every effect deliberately weaker than the
specialist's version.** "Expert in nothing" is a number, not flavour text: Warrior's steal is
worse than the Assassin's, its sprint worse than Speedy's, its shove worse than Fatty's.

⚠️ **Currently violated:** Wing Slam has **cd 12**, the longest cooldown in the game, on the
class whose identity is spamming. Needs re-tuning.

### Fatty
> The slow but unstoppable force that carries no matter what. Jumps must be justified in the
> narrative, knowing the big size and the low agility. Toolbox is **opportunity and control**.

**Contract:** area denial, punish, cargo resilience. Any leap must read as **launched mass,
not agility** — slow wind-up, big telegraph, heavy landing, punishable if read.

---

## 3. Specializations grant a signature ability

> **Superseded in part, 2026-09-04.** The slot model is now stated uniformly in GDD §7.1:
> the **class** determines the forager slot (none for the Assassin), the **specialization**
> determines the signature slot, and the player picks the remaining **2** (foragers) or
> **3** (Assassin). The consequence for this document is that the three ⏳ **Peck-variant
> signatures proposed below — empty-beak rush, burst-fed, heavy beakful — are retired.** A
> specialization cannot grant a forager, because the forager answers to the class. The
> per-class foraging variation those variants wanted already exists as each class's
> authored `PeckAmount` / `PeckCooldown`. The Agile, the Anxious and the Hauler need
> **ordinary** signature abilities instead; the fantasies described for them below are
> still the right brief, only the mechanism changes.

Today `ResolveLegalLoadout` force-equips **Peck** for every forager and never for the
Assassin. That mechanism generalizes.

**Proposal:** each class offers two specializations; the chosen specialization grants a
**forced signature ability** occupying one of the four slots. The player then picks **3**
from the class pool plus Commons.

Maestro, 2026-08-21:

> "Peck already has different cooldowns depending on the class, so we should make one ability
> the 'forced class ability'."

This solves two problems at once: it makes specializations mechanically load-bearing rather
than a passive stat tweak, and it removes one slot from the selection pool, which is why each
class no longer needs a huge roster to offer a real choice.

### The eight specializations

Resolved with Maestro 2026-08-21. Each class offers **two**, each grants a **passive** and a
**forced signature ability**, and the player then picks **3** more.

The passives already shipped map onto these splits almost exactly — in three of four cases the
specialization was already there and only needed naming and a signature.

---

#### Speedy — *agile* vs *anxious*

Speedy had **three** passives against a UI that offers two, so one was unreachable. Maestro:
*"merge slippery into one speedy specialization and featherfoot into the other... think in one
as the agile chicken, and in other as the anxious chicken."*

| | **The Agile** | **The Anxious** |
|---|---|---|
| Passive | **Slippery** — control effects wear off 40% faster | **Featherfoot** — immune to pile-slow |
| Signature | ⏳ **Peck: empty-beak rush** — cooldown scales with how empty the cargo is | ⏳ **Peck: burst-fed** — pecking amplified while Speed Burst is active |
| Fantasy | Poised, unpinnable. Slips the grab, never panics. | Jittery, never stops moving. Raids at full speed and bolts. |
| Plays like | Duel-survivor. Walks into contested space and out again. | Smash-and-grab. Highest ceiling, no composure. |

The split is coherent both ways: Slippery is *"hard to pin down, not hard to hit"* — poise, so
agile. Featherfoot lets you stand **on** a contested pile at full speed — the nervy raid, so
anxious. The empty-beak Peck suits a bird that darts in light; the burst-fed Peck suits one
that does everything at once and then flees.

> **Drop-and-Go: retired as a passive** (Maestro, 2026-08-21). It banked cargo almost
> instantly and its own source warned it was worth **~27% of Speedy's SCT** — the most
> distorting passive in the set. Pairing it with Featherfoot would have given one
> specialization both "never slow at the pile" and "never slow at base", i.e. most of the SCT
> budget for free. **Re-author it as a selectable Speedy ability** so the tempo gain costs a
> slot. Needs a `BalanceOracle` solve, not a hand-tune.

---

#### Fatty — *hauler* vs *boulder*

Maestro: *"one was the cargo boost to make him able to win with just the last back to base."*
That is **Hoarder**, already shipped. The second is defined here.

| | **The Hauler** | **The Boulder** |
|---|---|---|
| Passive | **Hoarder** — carries at least a full win's worth | **Bulwark** — shorter control, 75% less knockback |
| Signature | ⏳ **Peck: heavy beakful** — long cooldown, much larger amount | ✅ **Ground Quake** — stomp roots everyone in radius |
| Fantasy | One perfect trip: fill once, walk home once, win. | The immovable object. Nothing moves him, he moves you. |
| Axis | Economy | Control |

The two signatures deliberately sit on **different axes** — the Hauler's is an economy
ability (a Peck variant), the Boulder's is a control ability. Fatty's essence is *"opportunity
and control"*, and these are the two halves: the Hauler takes the opportunity, the Boulder
makes it.

> `Hoarder.MinimumCapacity` must stay at or above `MatchConfig.FoodTargetToWin` or the
> one-trip fantasy silently fails and the chicken walks home a bite short. Already pinned by a
> DataIntegrity test — do not let a signature Peck change break that relationship.

---

#### Warrior — *thief* vs *fighter*

Maestro: *"one warrior-thief and one warrior-fighter (i.e. more control)."* Both passives
already exist and already split on exactly that line — Bully is the **magnitude** axis,
Relentless the **frequency** axis.

| | **The Brute** (thief) | **The Relentless** (fighter) |
|---|---|---|
| Passive | **Bully** — steals bigger, plus the cargo room to hold it | **Relentless** — abilities come back faster |
| Signature | ✅ **Scrap** — steals a small amount on contact | ✅ **Headbutt** — short shove + brief stagger, low cooldown |
| Fantasy | Takes what it wants, by weight. | Never stops swinging. Attrition by volume. |
| Axis | Magnitude | Frequency |

Each signature is **amplified by its own passive**, which is what makes the choice read: Bully
makes Scrap's theft worth taking, Relentless makes Headbutt genuinely spammable. Neither
signature is impressive under the *other* passive — that asymmetry is the point.

This also satisfies the essence honestly: the Warrior's steal is weaker than the Assassin's and
its control weaker than Fatty's. *Expert in nothing* is a number, not flavour.

> `Relentless` **deliberately excludes Peck** from its cooldown reduction, or the Warrior would
> gain ~25% farming throughput for free. Harmless today because Warrior's signatures are not
> Peck variants — but if a future Warrior specialization takes a Peck signature, that exclusion
> and the signature will contradict each other.

---

#### Assassin — *reaper* vs *burglar*

Named by Maestro 2026-08-23: **Reaper** and **Burglar**.

| | **The Reaper** | **The Burglar** |
|---|---|---|
| Passive | **Spoiler** — banks a bonus if the timer expires with no winner | **Thief** — every steal takes 1.6x more |
| Signature | ✅ **Mark/Kill** — isolate, arm, execute | ✅ **Sneaky Steal** — unavailable to the Reaper |
| Fantasy | Patient. Wins the game nobody else finished. | Pure predation. Never farms, only takes. |

---

### Visual identity

Maestro asks for *"at least slightly different colors and models"* per specialization, and
concept art specifically for **the agile chicken vs the anxious chicken**.

These are the strongest read in the set — poise versus panic is a silhouette and posture
difference, not just a palette swap. Agile: upright, still, weight centred, clean plumage.
Anxious: crouched, weight forward, feathers ruffled, wide eye, mid-flinch. That contrast should
carry at the game's ortho-iso distance, where a colour tint alone will not.

`artist-2d` for concepts, then `artist-3d` + `shader-artist` for the in-game distinction.

## 4. New abilities (agreed)

| Class | Ability | Effect | Notes |
|---|---|---|---|
| **Warrior** | Headbutt | Short shove + brief stagger | A weaker Cluck Shock. Low cd. |
| | Scrap | Steals a small amount on contact | A weaker Snatch. |
| | Ruffle | Modest self speed/turn boost | A weaker Speed Burst. |
| **Speedy** | Dust Kick | Rear-facing cone, slows pursuers behind | Escape tool that never crosses geometry. |
| | Feint | Instant lateral sidestep — **ground-only, blocked by walls** | Dodge without traversal. |
| **Fatty** | Belly Flop | Slow telegraphed launch, AoE stun on landing | **The justified jump**: mass, not agility. |
| | Ground Quake | Stomp roots everyone in radius | Control. |
| | Immovable | Brief knockback/root immunity | "Unstoppable force." |
| **Assassin** | Smoke Roost | Zone that conceals and breaks sightlines | Kill from the shadows. |

### Rejected, with reasons
- **Second Wind** (Warrior cooldown reset) — *"makes everything more complex, and passive
  already covers that."* `Relentless` already returns combat abilities 25% faster.
- **Grain Rush** (Speedy, faster pecks for a few seconds) — killed by the actual numbers:
  `PeckAmount` is **3**, a personal pile holds **5** (≈2 pecks) and Speedy's cargo caps at
  **10** (≈4 pecks). There is no window long enough for it to matter. Replaced by the
  per-specialization Peck variants above, which is the better version of the same idea.

---

## 5. Two "Common" abilities are class essences in disguise

Maestro: *"Don't even trust common ones."* Auditing them against §2:

| Ability | Verdict |
|---|---|
| **Snatch** (`All`, cd 3) | "Robs cargo from every rival in a forward arc." That is **theft** — the Assassin's core. A universal AoE steal is *why* the Assassin fantasy reads thin. → **Assassin** |
| **Speed Burst** (`All`, cd 6) | 2.5x sprint. Speedy is *defined* as the fastest; a universal sprint erases that. → **Speedy** |
| **Egg Shell** (`All`, cd 8) | Immune but immobile. No class flavour, mechanically trivial. → **genuinely Common** |
| **Peck** (`Warrior, Speedy, Fatty`) | Common slot but deliberately **not** `All` — the Assassin cannot forage. A real, intended exception: "Common" means *shared pool*, not *universal*. |

---

## 6. Constraints on implementation

**The SCT axiom governs every Peck variant.** `PeckAmount` and `PeckCooldown` feed Solo Clear
Time directly. The per-specialization Peck signatures **must be solved against
`BalanceOracle`**, never hand-tuned — this is exactly the surface where a +20% drift once shipped
green because a test pinned literals instead of loading the real assets.

**Current per-class Peck differentiation is weaker than it looks.** Cooldowns differ slightly
(Speedy 0.77 / Fatty 0.78 / Warrior 0.91) but **`PeckAmount` is 3 for all three**. Fatty
already pecks nearly as fast as Speedy and carries the same per beakful. The differentiation
this design assumes mostly does not exist yet.

~~**Speedy has three passives but the UI offers two.**~~ **Resolved** (`552b015`): Drop-and-Go
was retired as a passive and re-authored as the `QuickDrop` ability. Every class now has
exactly two specializations.

~~**Stale UI copy.**~~ **Resolved** (`552b015`): the damage-era passive names were removed. The
lobby card shows the class `Role` instead, which is stable across both specializations — a
per-class passive name is right for at most one of a class's two builds.

**Forced-slot asymmetry, for Maestro's eye.** A forager whose signature is NOT a Peck variant
now spends two of four slots on forced picks (Peck + signature), leaving two free choices. The
Assassin, which cannot forage, gets three. That is a real difference in expressiveness between
classes and it lands once the three Peck-variant signatures exist — at which point Speedy and
Fatty/Hauler get three free slots and only Warrior stays at two.

**Pool sizes as shipped** (actives only; passives excluded):

| Class | Own | + Common | Total | Specializations |
|---|---|---|---|---|
| Warrior | 6 | 2 | 8 | Bully / Relentless |
| Speedy | 6 | 2 | 8 | Slippery / Featherfoot |
| Fatty | 7 | 2 | 9 | Hoarder / Bulwark |
| Assassin | 8 | 1 | 9 | Spoiler / Thief |

**§5 is done.** The Common pool is now **Egg Shell** (All) and **Peck** (foragers only) —
nothing else. Snatch went to the Assassin because an AoE cargo steal *is* the class's core, and
Speed Burst to Speedy because a universal 2.5x sprint erases the one thing that class owns
outright. Every class stayed above the ≥7 floor.
