# Cluck Wars — Ability Pool Rewrite v0.4

**Date:** 2026-07-25
**Status:** Design in progress — follow-up to the Core Redesign
**Depends on:** `2026-07-24-cluck-wars-core-redesign-design.md` (combat model, targeting north
star, Assassin execute)
**Supersedes:** GDD §7 ability pool; the Damage category; the current class↔ability
assignments (authored under the old HP/damage model)

---

## 1. Why a rewrite, not a rename

Damage is deleted, so the three Damage abilities (Peck, Cluck Shock, Flying Peck) have no
home, and Warrior — now "the ability class" — had a Character pool that was two-thirds
damage. The pool must be rebuilt around the new brief:

- **Abilities buy time; steal converts tempo into food.** (At F=2W, farming is viable, so
  every non-steal ability's job is to cost a rival *time*.)
- **Self-centered or directional. One targeted exception: the Assassin Mark/Kill.**
- **Steal is capped naturally:** `stolen = min(abilityStealValue, attacker free space,
  defender cargo)`. A loaded thief can't steal; theft is a rhythm, not a drain.

## 2. Category model — Steal / Control / Defense / Utility

Steal is first-class. **Steal** (take cargo) and **Control** (slow/root/stun/knockback, no
cargo) are the two offensive pillars; **Defense** protects self/cargo; **Utility** is
movement/vision/misc. Drives bot roles (Steal→Hunt, Defense→Flee) and the character-select
headers.

## 3. The roster

Loadout: **1 mandatory class-passive + 1 Common + 2 Character** (Assassin: +1 Character via
Combo, one of which is the locked Mark/Kill). Traversal (ADR 0003) noted where it applies.

### Common (pick 1) — one self-directed pillar each; Control is class flavor

| Ability | Was | Now | Category | Targeting |
|---|---|---|---|---|
| **Peck** | Damage | short-range **snatch** + minor knockback | Steal | AoE-around-self |
| **Egg Shell** | Defense | invuln egg, immobile while active | Defense | Self |
| **Speed Burst** | Utility | self speed boost | Utility | Self |

### Speedy (pick 2) — kite/escape, **no steal** (he farms, doesn't rob)

| Ability | Now | Category | Targeting |
|---|---|---|---|
| **Feather Aura** | AoE slow around self | Control | AoE-around-self |
| **Feather Trap** | slow zone at feet | Control | Placed zone (at feet) |
| **Invisibility** | self-invisible (shared w/ Assassin) | Utility | Self |

### Fatty (pick 2) — survive the long sweep, **no steal**

| Ability | Now | Category | Targeting |
|---|---|---|---|
| **Roll & Push** | barge forward, knockback (his Barge mobility) | Control | Directional (Barge) |
| **Root Egg** | root zone at feet | Control | Placed zone (at feet) |
| **Cluck Shock** | high-AoE **knockback push** — shove the swarm off the loaded bulk | Control | AoE-around-self |
| **Turtle Mode** | near-zero speed, high resistance | Defense | Self |

### Warrior (pick 2) — generalist: one of each offensive pillar + defense

| Ability | Was | Now | Category | Targeting |
|---|---|---|---|---|
| **Flying Peck** | Damage dash | vault-dash, **snatch on contact** (gap-close + rob) | Steal | Directional (Vault) |
| **Wing Slam** *(new, name provisional)* | — | AoE **stun** burst (longer than Ambush's 1.0s) — the non-Assassin stun; can deny OR enable an Assassin execute | Control | AoE-around-self |
| **Spine Coat** | Damage aura | knockback + **steal-back** from anyone who touches you | Defense | Self-aura |

### Assassin — locked Mark/Kill + pick 2 from the pool

| Ability | Was | Now | Category | Targeting |
|---|---|---|---|---|
| **Mark/Kill** 🔒 | *new* | signature execute (Core §4) — **the only single-target ability** | — | Single-target (locked slot) |
| **Ambush** | *new* | **1.0s stun** — the execute qualifier | Control | AoE-around-self |
| **Sneaky Steal** | Utility | baseline rob | Steal | AoE-around-self |
| **Shadowstep** | *new* | short **Blink** gap-close dash — class-gated so Speedy can't take it | Utility | Directional (Blink) |
| **Invisibility** | Utility | approach (shared w/ Speedy) | Utility | Self |
| **Doppelganger** | Utility | decoy/escape | Utility | Self (Blink) |

**Assassin builds** (locked Mark/Kill + 2):
- **Executioner** — + Ambush + Shadowstep (close the gap, self-stun, kill).
- **Thief** — + Sneaky Steal + Invisibility (rob-focused; kills off *others'* stuns).
- **Trickster** — + Ambush + Doppelganger.

## 4. How this satisfies the constraints

- **Steal is concentrated where identity lives:** Warrior (Flying Peck, for Bully) and
  Assassin (Sneaky Steal + Mark/Kill, for Thief), plus baseline Peck. Speedy and Fatty
  have **no** steal — they farm and carry. Roles stay legible.
- **Stun lives in exactly two classes:** Assassin (**Ambush**, 1.0s — sets up his own
  execute) and Warrior (**Wing Slam**, longer). This is what makes the Core §4 counterplay
  real: a Warrior can stun the *Assassin* to deny a kill, or stun the Assassin's *marked
  target* to hand him the cash-in. Stun stays the rare top of the control ladder (2 of 5
  classes), and Warrior "polices" the Assassin — a deliberate rock-paper layer.
- **Every class has a mobility answer (ADR 0003 triangle):** Speedy = RUN (fastest);
  Warrior = Flying Peck (Vault); Fatty = Roll & Push (Barge); Assassin = Shadowstep /
  Doppelganger (Blink). The Assassin's Blink is the "routing beats raw speed" tool that
  lets a slower hunter catch a Speedy — and it's class-gated, so Speedy can't copy it.
- **Assassin gap-closer resolved as a class-gated Character ability (Shadowstep),** not an
  innate trait: the Common/Character split already stops Speedy from taking it, so no
  universal-pool rule is bent, and it lives in his flexible slot rather than costing the
  locked Mark/Kill slot.

## 5. Open items (this spec)

1. **Warrior stun power budget.** Wing Slam gives the generalist the game's second stun —
   strong, and potentially oppressive with **Relentless** (stun-spam). The Oracle/playtest
   sets its duration and cooldown; watch the Relentless + Wing Slam combo specifically.
2. **Wing Slam name** is provisional (narrative-designer pass later). Mechanically it is
   the same AoE-self stun subclass as Ambush, tuned to a longer duration and Warrior-gated.
3. **Peck-as-AoE-steal power budget.** Peck is Common (any class can snatch cargo from a
   small radius). Needs a low steal value so it's a poke, not a farming replacement —
   the Oracle/playtest sets it.
4. **Assassin pool size (5) and Fatty pool size (4)** vs the others' 3. Justified
   (Assassin by Combo; Fatty by absorbing Cluck Shock), but could be trimmed for symmetry.
   Tunable.
5. **Per-ability values** — steal amounts, cooldown tiers, stun/slow/root durations, AoE
   radii, dash distances. All deferred to authoring + the Balance Oracle + playtest.

## 6. Migration notes (implementation)

- `AbilityCategory` enum: rename/replace `Damage` → the four-category model (Steal is the
  new first-class value); update `BotRole` auto-derivation.
- Reassign `SlotKind` / `AllowedClasses` per §3; author the two new abilities (**Mark/Kill**
  as a special locked-slot signature, **Shadowstep**, **Ambush**) as new `AbilityBaseSO`
  subclasses; register in `AbilityRegistrySO`.
- Convert every ex-Damage effect (`ChickenCombat` HP path) to steal/knockback/stun on the
  control+steal pipeline. This is the large gameplay change — see Core §3.
- Update GDD §5 and §7 to match (they still describe the damage model and the flat pool).
- Bot `_botLoadouts` presets must be re-validated against the new `AllowedClasses` masks
  (STATE.md already flags stale presets, e.g. Flying Peck class-mask mismatches).
