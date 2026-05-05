# Game Design Document v0.2

**Version:** 0.2  
**Date:** May 2026  
**Status:** Draft  
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
SELECT character & upgrades → SPAWN at base edge → MOVE to food pile
→ STAND on pile to collect cargo (cargo rate / sec) → CARRY food back to base
→ DEPOSIT food at base → REPEAT until time runs out or target is reached
                    ↕
         FIGHT / STEAL / DISRUPT rivals along the way
```

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
| Attacking | Attack button held near enemy | Rapid, energetic |
| Hit | Receiving damage | Brief hit reaction |
| Stunned | Death stun active (5 seconds) | Held until stun expires |
| Ability | Defined per AbilitySO | 2–3 shared animations reused across abilities |

Ability animations are defined in the `AbilitySO` asset — each ability references one of the shared animation clips. Adding a new ability does not require a new animation unless explicitly needed.

---

## 5. Character Classes

Each player selects one class before the match. Classes define the stat spread and determine which abilities are available to equip.

### Core Stats

| Stat | Description |
|---|---|
| Cargo Capacity | Maximum food units the chicken can carry at once |
| Cargo Rate | Food units collected per second while standing on a pile |
| HP | Total hit points before being stunned |
| Resistance | Damage reduction — affects how long the chicken survives hits |
| Speed | Movement speed across the map |
| Attack | Base damage contribution in combat encounters |

### 5.1 Fatty Chicken 🐔

*"Slow and steady wins the race — if it survives long enough."*

| Stat | Value |
|---|---|
| Cargo Capacity | ⭐⭐⭐⭐⭐ |
| Cargo Rate | ⭐⭐⭐⭐⭐ |
| HP | ⭐⭐⭐⭐ |
| Resistance | ⭐⭐⭐⭐ |
| Speed | ⭐⭐ |
| Attack | ⭐⭐ |

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
| Attack | ⭐⭐⭐ |

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
| Attack | ⭐⭐⭐⭐ |

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
| Attack | ⭐⭐⭐⭐⭐ |

**Role:** Disruptor. Low cargo means the Assassin wins by denying others, not by farming. Chains abilities and attacks to stun rivals and steal dropped cargo. **Unique: equips 2 abilities instead of 1.**

---

## 6. Combat System

Combat is **skill-based and manual** — not auto-resolved. When two chickens are in proximity, players can attack by **repeatedly pressing the attack button**. The outcome is influenced by:

- **Attack stat** — provides a minor damage advantage, not a decisive one.
- **Player skill** — timing, button frequency, and knowing when to disengage.
- **Abilities** — short-burst effects that can swing a fight dramatically.
- **Positioning** — catching a loaded rival mid-route is often more valuable than winning a direct fight.

**Design intent:** Attack stat differences should make fights feel slightly unequal, not predetermined. A Speedy Chicken should be able to win against a Warrior through good play and ability usage. Combat must feel fun and kinetic, never frustrating or purely stat-gated.

Fights also create **secondary opportunities** — when two players are locked in combat, a third can swoop in to steal dropped cargo or contest an unguarded pile. This emergent dynamic should be encouraged by the map layout and pacing.

---

## 7. Abilities System

### 7.1 Rules
- Each class equips **1 ability** during character selection (pre-match).
- **Assassin** equips **2 abilities**.
- Abilities are selected from a **shared pool** — availability per class is TBD and will be defined during balancing.
- Each ability has a **cooldown** *(all values TBD)*.
- All abilities have an **active duration of 1–2 seconds**.
- **All abilities must be balanced against each other** — the monetization model must never create a pay-to-win dynamic. This is a non-negotiable design principle.

### 7.2 Ability Pool

| Ability | Description | Type | Notes |
|---|---|---|---|
| Speed Burst | Greatly increases movement speed | Mobility | Escape or chase |
| Egg Shell | Become invulnerable inside an egg — immobile while active | Defensive | Blocks damage, not repositioning |
| Roll & Trample | Roll forward, stunning chickens in path | Offensive/Mobility | Also useful as escape |
| Doppelganger | Spawn a brief decoy copy of yourself | Deceptive | Breaks targeting, creates confusion |
| Invisibility | Temporarily invisible to other players | Deceptive | Strong for Assassin combos |
| Spine Coat | Contact with you deals damage to the attacker | Reactive | Punishes aggressive play |
| Turtle Mode | Near-zero speed, greatly increased resistance | Defensive | Protects cargo during a dangerous crossing |
| Sneaky Steal | Instantly steal a small amount of cargo from a nearby rival without entering combat | Utility | Unique non-combat ability — bypasses fight entirely |

*All cooldown and duration values are TBD pending playtesting. Balance pass required before any ability is made available for purchase.*

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
- **Attack:** Tap button rapidly when near an enemy (right thumb area).
- **Ability:** Dedicated button(s) per equipped ability (right thumb area).
- **Collecting food:** Passive and automatic — stand on a pile and cargo fills at Cargo Rate per second. No button needed.
- UI must be **minimal and readable** at small screen sizes.
- Character silhouettes must be **instantly readable** in isometric view — class identity must be clear at a glance.
- Attack and ability buttons must feel **responsive and satisfying** — core to the mobile experience.

---

## 11. Open Questions & TBD Items

| # | Item | Priority |
|---|---|---|
| 1 | Food target value (placeholder: 150 units) | High |
| 2 | Match timer (placeholder: 3 min) | High |
| 3 | All ability cooldown values | High |
| 4 | Cargo Rate values per class | High |
| 5 | In-game currency earn formula | High |
| 6 | Class ability compatibility matrix | High |
| 7 | Attack button feel & tuning on mobile | High |
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
