# Game Design Document v0.3

**Version:** 0.3  
**Date:** May 2026  
**Status:** Draft  
**Changelog v0.3:** Removed basic attack; redesigned ability system (2 slots all classes, 3 for Assassin, no class restrictions); renamed Section 6 to Interaction & Control System; added Slow Sources and Control States; full ability pool redesign into 4 categories; added class passives; updated controls and TBD items.  
**Changelog v0.2:** Added visual direction (2.5D), food pile visuals, animation states

---

## 1. Overview

**Game Title:** Cluck Wars  
**Genre:** Competitive multiplayer arena — fast-paced resource collection  
**Platform:** Multiplatform — Mobile (primary), PC, Tablet  
**Players:** 4 players, free-for-all (no teams)  
**Perspective:** Isometric top-down — 2.5D with real 3D meshes, orthographic camera  
**Session Length:** ~5–10 minutes per match  
**Target Audience:** Casual to mid-core competitive players, mobile-first

**Elevator Pitch:**  
Four chickens enter a farm. Only the fattest leaves. Cluck Wars is a fast-paced 4-player free-for-all where players collect food, defend their stash, and sabotage rivals using character classes and short-burst abilities. Easy to learn, deep to master.

---

## 2. Core Game Loop

```
SELECT character & abilities → SPAWN at base edge → MOVE to food pile
→ STAND on pile to collect cargo (cargo rate / sec) [chickens are slowed while on pile]
→ CARRY food back to base → DEPOSIT food at base
→ REPEAT until time runs out or target is reached
                    ↕
     USE ABILITIES to disrupt, protect, control or steal
```

**Core Strategic Principle:** Every aggressive interaction costs a cooldown. Cooldown management is the primary skill.

**Win Condition:**
- First player to reach **150 food units** stored at base wins immediately, OR
- Player with the **most food stored at base** when the **3-minute timer** expires wins.

*(150 units and 3 minutes are placeholder values — subject to balancing)*

**On Death:**
- Chicken is **stunned in place for 5 seconds** — cannot move, use abilities, collect, or be attacked.
- All **carried cargo is dropped** around the fallen chicken's position and becomes freely collectable by any player walking over it.
- After stun expires, chicken respawns at their base edge with zero cargo.

---

## 3. Map Design & Visuals

**Layout:**
- Fixed square/hex arena with isometric top-down view.
- Map **shape and food island positions are randomized** each match within defined constraints.
- The following elements are always fixed:

| Element | Position | Notes |
|---|---|---|
| Central Food Pile | Center of map | Large, high risk, high reward |
| Player Bases | 4 edges of map | Cannot be attacked or stolen from |
| Personal Island | Near each base | Small, relatively safe early game |
| Contested Islands | Between each pair of adjacent players | Designed to provoke early fights |

**Food piles do not respawn.** Once depleted, they are gone for the match. This creates increasing scarcity over time, forcing players to transition from farming to fighting as the match progresses.

**Food Distribution Philosophy:**  
Players must constantly evaluate three strategic options:
1. **Secure** — farm their personal island safely but with low yield.
2. **Contest** — fight over the high-value central pile.
3. **Invade** — target rivals who are carrying cargo or whose personal island is undefended.

Placeholder food values per pile *(subject to heavy testing)*:

| Pile | Food Units | Notes |
|---|---|---|
| Central pile | 60 units | Shared, most contested |
| Personal island | 15 units per player | One per player |
| Contested island | 25 units | One between each pair of players |

### Food Pile Visuals

Food piles visually reflect their remaining food percentage in real time, giving players instant strategic reads without needing to check numbers:

- **Scale** — pile physically shrinks as food depletes. Full pile is at 100% scale; empty pile is a remnant stub.
- **Color shift** — pile transitions from vibrant warm tones (full) to desaturated/dull (depleted).
- **Future** — mesh swap at threshold values may be added post-demo.

Visual state is driven locally on each client by the `[Networked]` food amount — no additional sync needed.

---

## 4. Visual Direction

### Rendering
- **2.5D** — real 3D meshes on an isometric orthographic camera. No perspective distortion.
- Characters use chunky, exaggerated proportions — large head, round body, tiny limbs. Reference: Hei Hei (Moana).
- Map uses darker, desaturated tones to maximize character readability and reduce visual noise.
- Characters use warm, saturated cartoon colors to pop against the map.

### Character Facing & Animation

Characters have two base facing directions (left, right). Vertical movement is handled by mirroring:

| Movement direction | Facing | Vertical mirror |
|---|---|---|
| Right / Down-right / Up-right | Face right | Down = default, Up = vertical flip |
| Left / Down-left / Up-left | Face left | Down = default, Up = vertical flip |

**Direction snaps instantly** — no lerp or transition. On stop, last direction is held. After 2 seconds stationary, idle animation triggers.

### Animation States

| State | Trigger | Notes |
|---|---|---|
| Walk | Moving | Base locomotion |
| Idle | Stationary > 2 seconds | Subtle loop |
| Collecting | Standing on food pile with cargo filling | Distinct from idle — signals intent |
| Ability Cast | Ability button pressed | Defined per AbilitySO — 2–3 shared clips reused |
| Hit | Receiving damage | Brief hit reaction |
| Stunned | Death stun active (5 seconds) | Held until stun expires |

Ability animations are defined in the `AbilitySO` asset — each ability references one of the shared animation clips. Adding a new ability does not require a new animation unless explicitly needed.

---

## 5. Character Classes

Each player selects one class before the match. Classes define the stat spread and passive ability. All abilities are available to all classes — selection happens during character selection, with no class-based restrictions.

### Core Stats

| Stat | Description |
|---|---|
| Cargo Capacity | Maximum food units the chicken can carry at once |
| Cargo Rate | Food units collected per second while standing on a pile |
| HP | Total hit points before being stunned |
| Resistance | Damage reduction — affects how long the chicken survives hits |
| Speed | Movement speed across the map |

### 5.1 Fatty Chicken 🐔

*"Slow and steady wins the race — if it survives long enough."*

| Stat | Value |
|---|---|
| Cargo Capacity | ⭐⭐⭐⭐⭐ |
| Cargo Rate | ⭐⭐⭐⭐⭐ |
| HP | ⭐⭐⭐⭐ |
| Resistance | ⭐⭐⭐⭐ |
| Speed | ⭐⭐ |

**Passive — Immovable:** Greatly reduced knockback distance from push abilities.

**Role:** Bulk carrier. Best at maximizing food per trip. Dominant on uncontested piles, but slow enough that rivals can intercept it en route to base.

### 5.2 Speedy Chicken 🐤

*"If you can't catch me, you can't kill me."*

| Stat | Value |
|---|---|
| Cargo Capacity | ⭐⭐⭐ |
| Cargo Rate | ⭐⭐⭐ |
| HP | ⭐⭐ |
| Resistance | ⭐ |
| Speed | ⭐⭐⭐⭐⭐ |

**Passive — Slippery:** Control abilities (slow, root, knockback) have reduced duration. Damage stuns apply normally.

**Role:** Hit-and-run collector. Must avoid prolonged fights — dies fast if caught. Thrives on chaos, excels at snatching dropped cargo after fights.

### 5.3 Warrior Chicken ⚔️

*"The farm is a battlefield."*

| Stat | Value |
|---|---|
| Cargo Capacity | ⭐⭐⭐ |
| Cargo Rate | ⭐⭐⭐ |
| HP | ⭐⭐⭐ |
| Resistance | ⭐⭐⭐ |
| Speed | ⭐⭐⭐ |

**Passive — Tough:** All damage abilities deal increased damage to targets.

**Role:** All-rounder. Excels at contesting piles and eliminating threats. No glaring weakness but no dominant strength — wins through consistent play and good decision-making.

### 5.4 Assassin Chicken 🗡️

*"Blink and your food is gone."*

| Stat | Value |
|---|---|
| Cargo Capacity | ⭐⭐ |
| Cargo Rate | ⭐⭐ |
| HP | ⭐⭐ |
| Resistance | ⭐⭐ |
| Speed | ⭐⭐⭐⭐ |

**Passive — Combo:** Equips **3 abilities** instead of 2. No other stat advantage.

**Role:** Disruptor. Low cargo means the Assassin wins by denying others, not by farming. Chains abilities to stun rivals and steal dropped cargo. The extra ability slot amplifies cooldown management as the core skill expression.

---

## 6. Interaction & Control System

There is no basic attack. All chicken-to-chicken interaction is fully ability-driven. The outcome of every encounter is determined by:

- **Ability choice** — what you equipped, what your opponent equipped, and who has cooldowns available.
- **Cooldown management** — the primary skill expression. Every aggressive use costs a cooldown. A chicken with no cooldowns available is vulnerable.
- **Positioning** — catching a loaded rival mid-route is often more valuable than winning a direct confrontation.
- **Class passives** — modify how control states interact with each class (see Section 5).

### 6.1 Collision Behavior

When two chickens are in physical contact, a **collision slow** applies to both. This is a **passive friction effect** — no damage, no knockback, no ability required. It makes contested piles and chokepoints naturally slower and more dangerous for loaded carriers.

### 6.2 Pile Slow

All chickens are **slowed while standing on any food pile**. This is a separate slow source from collision slow, applied equally to all classes regardless of passives (Speedy's Slippery passive does not reduce pile slow — subject to balancing).

### 6.3 Slow Sources (3 distinct types)

| Source | Trigger | Notes |
|---|---|---|
| Collision slow | Passive — two chickens in contact | No damage, no knockback |
| Pile slow | Passive — standing on any food pile | Applies to all classes equally |
| Ability slow | Applied by specific abilities (Feather Trap, Feather Aura, etc.) | Affected by Speedy's Slippery passive |

These sources are tagged separately in code to allow passive abilities to cover specific sources without affecting others.

### 6.4 Control States

| State | Source | Effect | Drops Cargo? |
|---|---|---|---|
| Stunned | HP reaches zero from damage abilities | Full incapacitation, 5 seconds, drops all cargo, respawn at base | ✅ Yes |
| Slowed | Collision / pile / slow abilities | Reduced movement speed | ❌ No |
| Knocked Back | Push abilities | Involuntary displacement, interrupts collection | ❌ No |
| Rooted | Trap abilities | Cannot move, can still use abilities | ❌ No |

**Stun is the only state that drops cargo.** All other control states disrupt without rewarding the aggressor directly — the reward is the window of opportunity they create.

### 6.5 Design Intent

Every aggressive action has a cost (cooldown) and a window (the effect duration). Skilled play is about converting that window into a real advantage — pushing a carrier off route, rooting them while collecting their pile, or timing a damage ability to stun a fully-loaded rival.

**Secondary opportunities:** When an ability exchange stuns a chicken, its dropped cargo becomes freely collectable by any player. A third party watching two players clash and collecting the dropped cargo is an intended and encouraged dynamic.

---

## 7. Abilities System

### 7.1 Rules
- Each class equips **2 abilities** during character selection (pre-match).
- **Assassin** equips **3 abilities** (Combo passive — see Section 5.4).
- **All abilities are available to all classes** — no class-based restrictions.
- Each ability has a **cooldown** defined by tier (see 7.3).
- **All abilities must be balanced against each other** — the monetization model must never create a pay-to-win dynamic. This is a non-negotiable design principle.

### 7.2 Ability Pool

#### Damage — deal HP; can stun if target HP reaches zero

| Ability | Description | Cooldown |
|---|---|---|
| Flying Peck | Dash forward, HP damage on contact | Short |
| Cluck Shock | AoE HP burst around self | Medium |
| Peck | Instant short-range HP hit + minor knockback | Short |

#### Control — no HP damage; disrupt movement or actions

| Ability | Description | Cooldown |
|---|---|---|
| Roll & Push | Roll forward, push target away — no damage | Short |
| Feather Trap | Throw feather cloud to a location; slows anyone walking through | Medium |
| Feather Aura | Emit feather cloud around self, slows nearby chickens | Medium |
| Root Egg | Place egg that roots the first chicken that steps on it | Medium |

#### Defense — protect self or cargo

| Ability | Description | Cooldown |
|---|---|---|
| Egg Shell | Invulnerable egg form, immobile while active | Short |
| Turtle Mode | Near-zero speed, greatly increased resistance | Short |
| Spine Coat | Damages + knockbacks any chicken that contacts you | Medium |

#### Utility — non-combat advantage

| Ability | Description | Cooldown |
|---|---|---|
| Speed Burst | Short movement speed boost | Short |
| Invisibility | Temporarily invisible to other players | Medium |
| Doppelganger | Spawn decoy copy of yourself | Medium |
| Sneaky Steal | Instantly steal small cargo from nearby rival, no HP interaction | Short |

### 7.3 Cooldown Tiers

| Tier | Range |
|---|---|
| Short | 3–6 seconds *(exact values TBD per ability)* |
| Medium | 8–12 seconds *(exact values TBD per ability)* |

*Balance pass required before any ability is made available for purchase.*

---

## 8. Progression & Monetization

### 8.1 Player Accounts
- Accounts are **required** and **cross-platform** — progress, currency, and unlocks sync across mobile, PC, and tablet.
- Account tracks: match history, in-game currency balance, unlocked characters, unlocked abilities, owned cosmetics.

### 8.2 In-Game Economy
- Every match awards **in-game currency** based on performance *(earn formula TBD — likely based on food collected, rivals stunned, and final placement)*.
- Currency unlocks characters and abilities **slowly over time**.
- Real money purchases allow **faster access** to the same content — not exclusive content.

### 8.3 Monetization Pillars

| Pillar | Model | Pay-to-Win? |
|---|---|---|
| Characters | Free base roster + earnable/purchasable | No — all classes balanced |
| Abilities | Free base pool + earnable/purchasable | No — all abilities balanced by design |
| Chicken Skins | Paid / seasonal / earnable | No — purely cosmetic |
| Map Skins | Paid / seasonal | No — purely cosmetic |
| Seasonal Battle Pass | Paid, thematic content per season | No — cosmetics only |
| Free Rotation | Rotating free characters & abilities | — |

### 8.4 Free Rotation
A subset of locked characters and abilities will be available **for free on a rotating basis**, lowering the barrier for new players and letting them try content before purchasing.

---

## 9. Cosmetics & Skins

Skins are the primary long-term monetization vehicle and are **purely cosmetic** — they never affect gameplay or stats.

**Skin types:**
- **Chicken skins** — visual reskin of any class
- **Map skins** — thematic reskin of the arena (terrain, props, lighting mood)

**Acquisition methods:**
- Direct purchase with real money
- Earned via seasonal content / battle pass
- Potentially earnable slowly via in-game currency *(TBD)*

**Seasonal content** is scoped as a post-launch feature contingent on demo success. Each season would introduce a new skin set, a thematic battle pass, and potentially a new map skin.

---

## 10. Controls & UX

- **Designed mobile-first** — all inputs must work on touchscreen without compromise.
- **Movement:** Virtual joystick (left thumb).
- **Abilities:** Two dedicated ability buttons in the right thumb area. Assassin has three.
- **Collecting food:** Passive and automatic — stand on a pile and cargo fills at Cargo Rate per second. No button needed.
- **Cooldown readability:** Ability buttons use a radial fill indicator and are greyed out while on cooldown. Clear visual state is a UI requirement.
- UI must be **minimal and readable** at small screen sizes.
- Character silhouettes must be **instantly readable** in isometric view — class identity must be clear at a glance.
- Ability buttons must feel **responsive and satisfying** — core to the mobile experience.

---

## 11. Open Questions & TBD Items

| # | Item | Priority |
|---|---|---|
| 1 | Food target value (placeholder: 150 units) | High |
| 2 | Match timer (placeholder: 3 min) | High |
| 3 | Ability cooldown values per ability (short: 3–6s, medium: 8–12s) | High |
| 4 | Cargo Rate values per class | High |
| 5 | In-game currency earn formula | High |
| 6 | Pile slow magnitude | High |
| 7 | Collision slow magnitude | High |
| 8 | Map randomization rules and constraints | Medium |
| 9 | Exact food distribution per pile | Medium |
| 10 | Seasonal content scope and cadence | Low |

---

## 12. Flavor System — Franchise Architecture

The **Flavor System** is an architectural decision, not an in-game feature. The game engine and core mechanics are designed to be **reskinned and redeployed as separate games**, each targeting a different theme and potentially a different audience or ruleset variant.

Each flavor is a **standalone game** built on the same codebase, sharing mechanics, systems, and infrastructure with minimal rework.

| Flavor | Game Title (TBD) | Setting | Mechanic Variants |
|---|---|---|---|
| The Farm *(launch)* | Cluck Wars | Countryside grange, chickens | Base version |
| Plunder Coop *(planned)* | TBD | Pirate ships & sea | 4v4 variant, similar mechanics |
| Cluck Station *(planned)* | TBD | Outer space | TBD |

**Design principle:** Core loop, stats, ability system, and progression must be engineered to be **theme-agnostic** from day one. Any flavor-specific mechanic variation must be a configurable layer, not a code fork.
