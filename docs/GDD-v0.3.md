---
status: archived
type: archive
superseded-by: GDD.md
source: Notion page, last edited 2026-08-24
---

> [!warning] Historical record, archived from Notion. Superseded by [the live GDD](GDD.md); numbers here (for example 150 food, 3 minutes) are stale and never shipped. Do not use this file as a source of numbers.

> [!warning] ⚠️
> **SUPERSEDED for classes, specializations and abilities.** This v0.3 draft describes an earlier design — HP/damage, a basic attack, no per-class ability gating, 150 food/3 min match config. None of that shipped. For the current, accurate state of classes and abilities, see [Classes, Specializations & Abilities (Current)](CLASSES-2026-08-24.md).
>
> Kept intact below as a historical record of the pre-redesign concept and the May 2026 demo milestone.

**Version:** 0.3 — Core Concept & Mechanics

**Date:** May 2026

**Status:** Draft

**Changelog v0.3:** Removed attack mechanic — fully ability-driven interaction system. Added control states, class passives, redesigned ability pool. 2 abilities per chicken (Assassin 3). Pile slow added.

---

# 1. Overview

**Game Title:** Cluck Wars *(working title)*

**Genre:** Competitive multiplayer arena — fast-paced resource collection

**Platform:** Multiplatform — Mobile (primary), PC, Tablet

**Players:** 4 players, free-for-all (no teams)

**Perspective:** Isometric top-down — 2.5D with real 3D meshes, orthographic camera

**Session Length:** ~5–10 minutes per match

**Target Audience:** Casual to mid-core competitive players, mobile-first

**Elevator Pitch:**

Four chickens enter a farm. Only the fattest leaves. Cluck Wars is a fast-paced 4-player free-for-all where players collect food, defend their stash, and sabotage rivals using character classes and short-burst abilities. Easy to learn, deep to master.

---

# 2. Core Game Loop

```javascript
SELECT character & abilities → SPAWN at base edge → MOVE to food pile
→ STAND on pile to collect cargo (cargo rate / sec, slowed while collecting)
→ CARRY food back to base → DEPOSIT food at base
→ REPEAT until time runs out or target is reached
                    ↕
    USE ABILITIES to disrupt, protect, control or steal from rivals
```

**Core strategic loop:** Every aggressive interaction costs a cooldown. Players must decide: burn an ability to disrupt a rival, or save it to protect their own run. Cooldown management is the primary skill.

**Win Condition:**

- First player to reach **150 food units** stored at base wins immediately, OR
- Player with the **most food stored at base** when the **3-minute timer** expires wins.

*(150 units and 3 minutes are placeholder values — subject to balancing)*

**On Death (Full Stun):**

- Chicken is **stunned in place for 5 seconds** — cannot move, use abilities, collect, or interact.
- All **carried cargo is dropped** around the fallen chicken and becomes freely collectable.
- After stun expires, chicken respawns at their base edge with zero cargo.

> ⚠️ **Stun ≠ other control states.** Full stun (death equivalent) only triggers when HP reaches zero from damage abilities. Other control effects (slow, knockback, root) are temporary and do not drop cargo. See Section 6 for full control state definitions.

---

# 3. Map Design & Visuals

**Layout:**

- Fixed square/hex arena with isometric top-down view.
- Map shape and food island positions are **randomized each match** within defined constraints.
- The following elements are always fixed:

| Element | Position | Notes |
| --- | --- | --- |
| Central Food Pile | Center of map | Large, high risk, high reward |
| Player Bases | 4 edges of map | Cannot be attacked or stolen from |
| Personal Island | Near each base | Small, relatively safe early game |
| Contested Islands | Between each pair of adjacent players | Designed to provoke early fights |

**Food piles do not respawn.** Once depleted, they are gone for the match. This creates increasing scarcity over time, forcing players to transition from farming to fighting as the match progresses. The pacing of this transition is a **critical balancing target**.

**Food Distribution Philosophy:**

Players must constantly evaluate three strategic options:

1. **Secure** — farm their personal island safely but with low yield.
2. **Contest** — fight over the high-value central pile.
3. **Invade** — target rivals who are carrying cargo or whose personal island is undefended.

Placeholder food values per pile *(subject to heavy testing)*:

| Pile | Food Units | Notes |
| --- | --- | --- |
| Central pile | 60 units | Shared, most contested |
| Personal island | 15 units per player | One per player |
| Contested island | 25 units | One between each pair of players |

---

# 4. Visual Direction

## Rendering

- **2.5D** — real 3D meshes on an isometric orthographic camera. No perspective distortion.
- Characters use chunky, exaggerated proportions — large head, round body, tiny limbs. Reference: Hei Hei (Moana).
- Map uses darker, desaturated tones to maximize character readability and reduce visual noise.
- Characters use warm, saturated cartoon colors to pop against the map.

## Character Facing & Animation

Characters have two base facing directions (left, right). Vertical movement is handled by mirroring:

| Movement direction | Facing | Vertical mirror |
| --- | --- | --- |
| Right / Down-right / Up-right | Face right | Down = default, Up = vertical flip |
| Left / Down-left / Up-left | Face left | Down = default, Up = vertical flip |

**Direction snaps instantly** — no lerp or transition. On stop, last direction is held. After 2 seconds stationary, idle animation triggers.

## Animation States

| State | Trigger | Notes |
| --- | --- | --- |
| Walk | Moving | Base locomotion |
| Idle | Stationary > 2 seconds | Subtle loop |
| Collecting | Standing on food pile with cargo filling | Distinct from idle — signals intent |
| Attacking | Attack button held near enemy | Rapid, energetic |
| Hit | Receiving damage | Brief hit reaction |
| Stunned | Death stun active (5 seconds) | Held until stun expires |
| Ability | Defined per AbilitySO | 2–3 shared animations reused across abilities |

Ability animations are defined in the `AbilitySO` asset — each ability references one of the shared animation clips. Adding a new ability does not require a new animation unless explicitly needed.

---

# 5. Character Classes

Each player selects one class before the match. Classes define the stat spread and determine which abilities are available to equip. All stats use a relative 1–5 scale for design purposes.

## Core Stats

| Stat | Description |
| --- | --- |
| Cargo Capacity | Maximum food units the chicken can carry at once |
| Cargo Rate | Food units collected per second while standing on a pile |
| HP | Total hit points — reaching zero triggers full stun |
| Resistance | Damage reduction from ability hits — higher resistance means harder to stun |
| Speed | Base movement speed across the map |

> **Attack stat removed.** No basic attack exists. Aggressive play is entirely ability-driven. Class damage output is determined by ability selection and the Warrior passive.

## 5.1 Fatty Chicken 🐔

*"Slow and steady wins the race — if you can't push me off it."*

| Stat | Value |
| --- | --- |
| Cargo Capacity | ⭐⭐⭐⭐⭐ |
| Cargo Rate | ⭐⭐⭐⭐⭐ |
| HP | ⭐⭐⭐⭐ |
| Resistance | ⭐⭐⭐⭐ |
| Speed | ⭐⭐ |

**Passive — Immovable:** Knockback distance from push abilities is greatly reduced. Rivals need damage abilities to threaten Fatty — pushing it away barely works.

**Ability slots:** 2

**Role:** Bulk carrier. Dominates uncontested piles. Hard to displace once collecting. Vulnerable on slow journeys back to base — a well-timed control ability can intercept it.

## 5.2 Speedy Chicken 🐤

*"You can't root what you can't catch."*

| Stat | Value |
| --- | --- |
| Cargo Capacity | ⭐⭐⭐ |
| Cargo Rate | ⭐⭐⭐ |
| HP | ⭐⭐ |
| Resistance | ⭐ |
| Speed | ⭐⭐⭐⭐⭐ |

**Passive — Slippery:** Control abilities (slow, root, knockback) have reduced duration when applied to Speedy. Damage abilities stun it normally — low HP means it goes down fast if hit.

**Ability slots:** 2

**Role:** Evasion and opportunism. Survives through speed and control resistance, not HP. Excels at snatching dropped cargo and escaping ganks. Must pick abilities that compensate for low survivability.

## 5.3 Warrior Chicken ⚔️

*"Hit hard, hit once."*

| Stat | Value |
| --- | --- |
| Cargo Capacity | ⭐⭐⭐ |
| Cargo Rate | ⭐⭐⭐ |
| HP | ⭐⭐⭐ |
| Resistance | ⭐⭐⭐ |
| Speed | ⭐⭐⭐ |

**Passive — Tough:** Warrior's damage abilities deal increased damage to all targets. The only class that can reliably stun tanks through damage alone.

**Ability slots:** 2

**Role:** The designated damage dealer. Balanced stats mean it can collect and survive, but its passive makes it uniquely threatening. Best used to stun high-value targets (loaded Fatty heading to base) and contest central piles aggressively.

## 5.4 Assassin Chicken 🗡️

*"Three abilities. Zero mercy."*

| Stat | Value |
| --- | --- |
| Cargo Capacity | ⭐⭐ |
| Cargo Rate | ⭐⭐ |
| HP | ⭐⭐ |
| Resistance | ⭐⭐ |
| Speed | ⭐⭐⭐⭐ |

**Passive — Combo:** Equips 3 abilities instead of 2. No stat advantage — wins entirely through ability chaining and cooldown management.

**Ability slots:** 3

**Role:** Pure ability specialist. Low cargo means it rarely wins by farming — it wins by denying everyone else. Chains control, utility and damage abilities in sequence. Fragile if abilities are on cooldown; unstoppable when they aren't.

---

# 6. Interaction & Control System

There is no basic attack. All chicken interactions are ability-driven. Chickens do not interact on collision beyond a passive slow.

## 6.1 Collision Behavior

- Chickens **slow each other on contact** (passive friction, no damage, no knockback).
- This is tagged as a **separate slow source** (collision slow) from ability slows and pile slow, so it can be modified by passives independently in the future.
- No cargo is dropped, no HP lost from collision alone.

## 6.2 Pile Slow

- All chickens are **slowed while standing on a food pile** (collecting or not).
- Applies to all classes equally (no passive immunity currently — flagged for future balancing).
- Tagged as a **separate slow source** (pile slow) from collision and ability slows.
- Creates a risk/reward tension: collecting is passive but standing still makes you slow and vulnerable.

## 6.3 Control States

Stun is the only full incapacitation. All other control states are temporary and do not drop cargo:

| State | Source | Effect | Cargo dropped? |
| --- | --- | --- | --- |
| **Stunned** | HP reaches zero from damage abilities | Full incapacitation, 5 seconds, respawn at base | ✅ Yes |
| **Slowed** | Collision / pile / slow abilities | Reduced movement speed | ❌ No |
| **Knocked Back** | Push abilities | Involuntary displacement, interrupts collection | ❌ No |
| **Rooted** | Trap abilities | Cannot move, can still use abilities | ❌ No |

## 6.4 Interaction Design Intent

Every aggressive interaction costs a cooldown. Players must ask:

- Is stunning this chicken worth spending my damage ability cooldown?
- Can I use a control ability to block their path without burning my best tool?
- Should I defend myself or run?

This creates meaningful decisions. A Speedy Chicken with Speed Burst might escape a Warrior's Flying Peck entirely. A Fatty with Egg Shell can tank a Cluck Shock that would stun a Speedy. The interplay between class passives, ability choices, and cooldown timing is the core skill of the game.

## 6.5 Secondary Opportunities

When a chicken is stunned and drops cargo, nearby players can collect the scattered food. This creates emergent three-way decisions: do you engage the fight and risk your own cooldowns, or let two others fight and swoop in for the dropped cargo?

---

# 7. Abilities System

## 7.1 Rules

- All classes equip **2 abilities** during character selection (pre-match).
- **Assassin** equips **3 abilities** (passive — Combo).
- All abilities available to all classes — no class-based restrictions.
- Each ability has a **cooldown**. Most are short (3–6s TBD). High-impact abilities may be medium (8–12s TBD).
- Ability duration: **1–2 seconds** for most effects.
- **All abilities must be balanced** — no pay-to-win. Paid abilities unlock for convenience, never advantage.

## 7.2 Cooldown Philosophy

- **Short cooldown (3–6s TBD):** Spammable, lower individual impact. Good for utility and harassment.
- **Medium cooldown (8–12s TBD):** Deliberate use, higher impact. Stuns, strong control, powerful defense.
- Most abilities fall in the short range. The most impactful ones trend medium.
- Assassin's Combo passive does not reduce cooldowns — the advantage is the third slot, not faster cycling.

## 7.3 Ability Pool

### Damage — deal HP, can stun (HP-dependent)

| Ability | Description | Range | Cooldown tier | Notes |
| --- | --- | --- | --- | --- |
| **Flying Peck** | Dash forward, deal HP damage on contact | Melee dash | Short | Stuns Speedy/Assassin; not enough alone vs tanks |
| **Cluck Shock** | AoE HP burst around self | Short radius | Medium | Stuns weak chickens; Warrior passive makes it stun-worthy vs tanks |
| **Peck** | Instant short-range HP hit + minor knockback | Melee | Short | Low damage, spammable, good harassment tool |

### Control — no HP damage, disrupt movement or actions

| Ability | Description | Range | Cooldown tier | Notes |
| --- | --- | --- | --- | --- |
| **Roll & Push** | Roll forward, push target away | Melee roll | Short | No damage — redirect rival from their base or off a pile |
| **Feather Trap** | Throw a feather cloud to a target location — slows anyone walking through | Ranged (thrown) | Medium | Persists a few seconds on ground, zone control |
| **Feather Aura** | Emit feather cloud around self — slows nearby chickens | Self radius | Medium | Defensive or offensive variant of Feather Trap |
| **Root Egg** | Lay an egg that roots the first chicken that steps on it | Ranged (placed) | Medium | Trap mechanic — punishes inattention near piles |

### Defense — protect self or cargo

| Ability | Description | Range | Cooldown tier | Notes |
| --- | --- | --- | --- | --- |
| **Egg Shell** | Become invulnerable — immobile while active | Self | Short | Blocks all damage and control. Can bait ability waste |
| **Turtle Mode** | Near-zero speed, greatly increased resistance | Self | Short | Tanky crossing; hard to stun while active |
| **Spine Coat** | Damages + knockback any chicken that contacts you | Self radius | Medium | Punishes melee approach; combo well with Egg Shell bait |

### Utility — non-combat advantage

| Ability | Description | Range | Cooldown tier | Notes |
| --- | --- | --- | --- | --- |
| **Speed Burst** | Short movement speed boost | Self | Short | Escape, pile race, cargo run |
| **Invisibility** | Temporarily invisible to other players | Self | Medium | Reposition, sneak to pile, avoid incoming ability |
| **Doppelganger** | Spawn a decoy copy of yourself | Self | Medium | Breaks targeting, wastes rival ability, confusion |
| **Sneaky Steal** | Instantly steal small cargo from nearby rival | Short range | Short | Only non-HP interaction. Bypasses defense entirely |

---

# 8. Progression & Monetization

## 7.1 Player Accounts

- Accounts are **required** and **cross-platform** — progress, currency, and unlocks sync across mobile, PC, and tablet.
- Account tracks: match history, in-game currency balance, unlocked characters, unlocked abilities, owned cosmetics.

## 7.2 In-Game Economy

- Every match awards **in-game currency** based on performance *(earn formula TBD — likely based on food collected, rivals stunned, and final placement)*.
- Currency unlocks characters and abilities **slowly over time**.
- Real money purchases allow **faster access** to the same content — not exclusive content.

## 7.3 Monetization Pillars

| Pillar | Model | Pay-to-Win? |
| --- | --- | --- |
| Characters | Free base roster + earnable/purchasable | No — all classes balanced |
| Abilities | Free base pool + earnable/purchasable | No — all abilities balanced by design |
| Chicken Skins | Paid / seasonal / earnable | No — purely cosmetic |
| Map Skins | Paid / seasonal | No — purely cosmetic |
| Seasonal Battle Pass | Paid, thematic content per season | No — cosmetics only |
| Free Rotation | Rotating free characters & abilities | — |

## 7.4 Free Rotation

A subset of locked characters and abilities will be available **for free on a rotating basis**, lowering the barrier for new players and letting them try content before purchasing.

---

# 9. Cosmetics & Skins

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

# 10. Controls & UX

- **Designed mobile-first** — all inputs must work on touchscreen without compromise.
- **Movement:** Virtual joystick (left thumb).
- **Ability 1 / Ability 2:** Two dedicated buttons (right thumb area). Assassin has a third.
- **Collecting food:** Passive and automatic — stand on a pile and cargo fills at Cargo Rate per second. No button needed.
- **No attack button.** All interactions are ability-driven.
- UI must be **minimal and readable** at small screen sizes.
- Character silhouettes must be **instantly readable** in isometric view — class identity must be clear at a glance.
- Ability buttons must show **cooldown state clearly** — radial fill, greyed out when on cooldown. Cooldown timing is a core skill; UI must support it.

---

# 11. Open Questions & TBD Items

| # | Item | Priority |
| --- | --- | --- |
| 1 | Food target value (placeholder: 150 units) | High |
| 2 | Match timer (placeholder: 3 min) | High |
| 3 | All ability cooldown values | High |
| 4 | Cargo Rate values per class | High |
| 5 | In-game currency earn formula | High |
| 6 | Ability cooldown values (short: 3–6s, medium: 8–12s — exact TBD per ability) | High |
| 7 | Pile slow magnitude and collision slow magnitude | High |
| 8 | Map randomization rules and constraints | Medium |
| 9 | Exact food distribution per pile | Medium |
| 10 | Seasonal content scope and cadence | Low |

---

# 12. Flavor System — Franchise Architecture

The **Flavor System** is an architectural decision, not an in-game feature. The game engine and core mechanics are designed to be **reskinned and redeployed as separate games**, each targeting a different theme and potentially a different audience or ruleset variant.

Each flavor is a **standalone game** built on the same codebase, sharing mechanics, systems, and infrastructure with minimal rework.

| Flavor | Game Title (TBD) | Setting | Mechanic Variants |
| --- | --- | --- | --- |
| The Farm *(launch)* | Cluck Wars | Countryside grange, chickens | Base version |
| Plunder Coop *(planned)* | TBD | Pirate ships & sea | 4v4 variant, similar mechanics |
| Cluck Station *(planned)* | TBD | Outer space | TBD |

**Design principle:** Core loop, stats, ability system, and progression must be engineered to be **theme-agnostic** from day one. Any flavor-specific mechanic variation must be a configurable layer, not a code fork.

---

# IMPLEMENTATION STATUS (v1.0 — SHIPPED)

**Demo Complete:** May 21, 2026 | **Build Status:** Windows ✅ | Android ✅

## Map Design — Implementation Note

**GDD Design:** Three strategic island types

- Personal islands (near each base, safe early)
- Contested islands (between adjacent players, provoke fights)
- Central pile (high reward, high risk)

**Actual Implementation:** Ring of piles

- 1 large center pile at map center
- N small piles on a jittered ring around it
- Personal/contested distinction removed; all piles equally accessible

**Why:** Simpler generation, maintains strategic tension through geography and scarcity over time.

---

## Camera — Implementation Note

**GDD Design:** Static overhead isometric view, full arena always visible

**Actual Implementation:** Smooth camera follows local player, closer ortho size (8)

**Why:** Mobile feel better with player-centric view. Keeps action centered, encourages positional play around the camera target. Players learn map positions naturally.

---

## Abilities — Implementation Note

**GDD Design:** Availability per class is TBD (flagged for balance pass)

**Actual Implementation:** All 4 classes have all 8 abilities available in character selection

**Impact:** No mechanical gate; full flexibility in ability selection. Balance tuned via AbilitySO assets (cooldown, duration, damage). Can be restricted per class later without code changes.

---

## Upgrades / Character Selection — Implementation Note

**GDD Core Loop:** "SELECT character **& upgrades**" → implies separate upgrade step

**Actual:** Character selection screen includes:

1. Class pick (stat preview shown)
2. Ability slot selection (1 or 2 depending on class)
3. (No separate upgrade flow)

**Why:** Ability pick serves as the "upgrade" moment. Simpler UX flow, same strategic depth.

---

## Features Built Beyond GDD

These emerged from development and playtesting:

- **AI Bots** (solo mode, FSM-based)
- **Match stats screen** (kills + cargo deposited beyond final food count)
- **Screen shake** on hit/death feedback
- **Cargo visuals** (gold orbit ring, HUD bar, nameplate label)
- **Player identity colors** (colorblind-safe Okabe-Ito palette)
- **Reconnection handling** (graceful session end)
- **UGS Auth + 6-char join codes** (lobby system)
- **Debug tooling** (F1 HUD, F2 balance editor)
- **WASD keyboard input** (alongside touch)
- **Per-class spawn scale variation**

---

## Deferred Post-Demo

Intentionally out of scope for this demo:

- **Progression & economy** (currency earn, unlock formula)
- **Persistent accounts** (UGS identity layer, no profile/progression yet)
- **Cosmetics & skins** (architecture ready, no assets)
- **Battle Pass / seasonal content**
- **Flavor System variants** (Plunder Coop, Cluck Station)
- **Animation .anim clips** (state machine wired, no recordings)
- **Audio clips** (service hooked, no SFX/music)
- **Balance tuning pass** (all values at placeholders)
