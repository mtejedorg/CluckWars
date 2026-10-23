# Balance and passives proposal (CW-053 / CW-057 / CW-058), October 2026

**Status: PROPOSAL. Nothing in this document has been applied.** No `.asset`, `.cs`, scene or
existing doc was changed. Every number below that differs from the shipped value is a
**proposal** and is marked so. Author: mechanics-designer, 2026-10-21, branch
`work/backlog-2026-10-21` (HEAD `bf5067a`).

## How to read the numbers

Each number has a source tag:

| Tag | Meaning |
|---|---|
| **[R]** | Read from a shipped `.asset`, `Game.unity` or a code constant |
| **[C]** | Computed with a Python port of `BalanceOracle.Simulate`, outside the repo (scratchpad). The port rebuilds `ShippedMap.OracleMap(seat)` from `Game.unity` values (`_planeSize` 51.3, `_baseCornerDistance` 25.65, pile amounts 20/5/10, footprints, insets, jitter 0), `MapGenerator.BaseInsetFraction` 0.15, `PileFootprintShape.RadiusToward`, deposit radius 2.5 and collect reach 1.0. **It reproduces the shipped results exactly:** Speedy 30.02 s / 4 trips, Fatty 30.05 s / 2, Warrior 34.98 s / 3, the same on all 4 seats. These match the figures recorded in the 2026-10-16 re-solve. Unity was not launched. `BalanceOracleTests` stays the authority. |
| **[E]** | Estimated by reasoning, not simulated |
| **[H]** | Taken from the 2026-10-21 test-harness logs (`Captures/session-20261021-*/merged.log`, `MATCH SUMMARY` blocks, about 41 four-seat matches). These matches are bots plus autoplay, **not humans**. Treat them as directional only, never as balance evidence. |

The Oracle measures a **naked** chicken (no abilities, no passive) farming alone. Anything a
passive or an ability does to farming speed lies outside it, unless we add it on purpose
(see §4.4).

---

## 0. Summary

1. **The premise of CW-053 is stale.** Mighty, Bracer and Opportunist do not exist in the
   shipped game. Commit `62219fc` (2026-08-13) deleted them and their damage hooks and replaced
   them with eight specializations. All eight run on live hooks, and each hook has a production
   call site (checked, §1.1). The backlog row and the 2026-09-17 menu handoff §4 restated an old
   doc comment in `PassiveAbilitySO.cs`, which describes what *was* inert. §1 still answers
   Maestro's request: one primary option and one alternative for each of the three slots. It
   aims at the real gaps those slots have today.
2. **Two real problems sit in today's passives instead.** **Featherfoot** (Speedy) does almost
   nothing, about 0.25 s per match [E]. **Bully** (Warrior) is a hidden farming buff: its +6
   cargo cuts Warrior SCT from 34.98 s / 3 trips to **30.91 s / 2 trips** [C]. That is the exact
   kind of off-Oracle buff Relentless was built to avoid.
3. **Spoiler wins from zero in the harness.** In 6 of about 41 four-seat matches, an Assassin
   with **0 food deposited** won on the +15 bonus [H], because the leader had banked 14 or less
   when the timer ran out. The current test only checks that the bonus stays below 40, the win
   target, so it misses this case.
4. **Wing Slam (CW-058):** the recommended change is **cooldown 12 → 9 s (proposal)** with the
   stun (1.5 s), range (4.5 m) and cone (120°) unchanged. It has zero Oracle impact, and its
   control efficiency stays below Ambush's. A precondition comes first: a later stun currently
   **overwrites** an earlier one, so the Warrior's own Headbutt (0.35 s) cuts his Wing Slam
   stun from 1.5 s down to 0.35 s.

---

## 1. CW-053: class passives without damage

### 1.1 What actually ships (all [R])

| Class | Specialization | Signature? | Effect | Hook (single chokepoint) | Asset values |
|---|---|---|---|---|---|
| Warrior | Relentless | yes | Every cooldown ×0.75 **except Peck** | `ModifyCooldown` → `AbilityController.ResolveCooldownFor` (L769) | `CooldownMultiplier 0.75` |
| Warrior | Bully | | Steal ×1.4, +6 cargo | `ModifyStealAmount` → `AbilityBaseSO.ResolveStealAmount` (L251); `ModifyCargoCapacity` → `ChickenCargo.Capacity` (L84) | `1.4`, `6` |
| Speedy | Slippery | yes | Slow / stun / root durations ×0.6 | `ModifyControlDuration` → `ChickenController.ApplyPassiveControlDuration` (L1066) | `0.6` |
| Speedy | Featherfoot | | Ignores the pile slow | `IgnoresPileSlow` → `ChickenController` FUN (L842) | — |
| Fatty | Hoarder | yes | Cargo is at least 40 | `ModifyCargoCapacity` | `MinimumCapacity 40` |
| Fatty | Bulwark | | Control ×0.65, knockback ×0.25 | `ModifyControlDuration`, `ModifyKnockback` (L1035) | `0.65`, `0.25` |
| Assassin | Spoiler | yes | +15 food if the timer expires with no winner | `MatchEndBonusFood` → `GameManager` (L691) | `BonusFood 15` = `MatchConfig.SpoilerBounty 15` |
| Assassin | Thief | | Steal ×1.6 | `ModifyStealAmount` | `1.6` |

`ModifyDepositRate` and `OnTraversalTick` are wired, but no shipped passive uses them.

Historical mapping, for the record: Mighty was the Warrior signature and Bracer the Warrior
alternative. Second Wind (not Bracer) was the Speedy alternative, and Opportunist was the
Assassin alternative. The request says "Speedy Bracer". I cover both readings: the Warrior
alternative (Bully) in §1.5 and the Speedy alternative (Featherfoot) in §1.3.

What a passive may legally do (from `PassiveAbilitySO`): it may only change values at the nine
hooks. It must be a **pure function** of its arguments. It may read state on `self`
(`ChickenController`) but must never store anything on the shared SO. Each new hook needs
**exactly one** production call site.

What the control hooks do **not** cover (matters for Slippery and Bulwark):
`ModifyControlDuration` only shortens **timed** effects: stuns (Wing Slam, Ambush, Belly Flop,
Headbutt), roots (Ground Quake, Sticky Egg) and Dust Kick's timed slow. **Presence** slows have
no duration and get no reduction: Feather Aura, Feather Trap and Smoke Roost zones, the
collision slow and the pile slow (`CheckAuraSlow`, `CheckAbilityZoneSlow`,
`CheckCollisionSlow`) [R]. So "Slows end 40% sooner" in the perk text is true for one slow in
the game out of five.

### 1.2 Slot "Warrior signature" (was Mighty, now Relentless)

**Verdict: live and on-identity ("the ability class"). It does not need a rescue.**

| | Primary: keep Relentless | Alternative: "Warpath" |
|---|---|---|
| Effect | Cooldown ×0.75 on everything but Peck (unchanged) | **Control**-category cooldowns ×0.65 (Wing Slam, Cluck Shock, Headbutt); all others ×1.0 (**proposal**) |
| Player text | "Every move except Peck recharges 25% faster" | "Your shoves and stuns recharge 35% faster" |
| Hook | `ModifyCooldown` (exists) | `ModifyCooldown`, branching on `ability.Category == Control` (exists, pure) |
| Oracle / economy | 0 s [C]: Peck is excluded by design | 0 s [C]: Peck is Category Utility, untouched |
| Stacking risk | With Wing Slam at 9 s (CW-058 rec.), Relentless gives 6.75 s; at 12 s it gives 9 s. Headbutt 4 → 3 s, so the stagger chains | Wing Slam 9 → 5.85 s: stun uptime 1.5 / 5.85 = **25.6%** per target [C]. Above every other stun in the game; needs a playtest |
| Recommendation | **Pick this.** Re-check it alongside CW-058, because the Relentless × Wing Slam pairing is the strongest control in the Warrior kit | Only if playtests show Relentless reads as "a bit of everything" |

### 1.3 Slot "Speedy alternative" (was Second Wind, now Featherfoot)

**Verdict: nearly inert. This is the real CW-053.** The pile slow (×0.80 [R]) only applies
within collect reach, 1.0 m of a non-empty pile [R]. While pecking, Peck locks movement
anyway (0.4 s of every 0.71 s [R]). In the Oracle its effect is **exactly 0 by construction**,
because every leg ends at the reach edge, which is also the edge of the slow band. In a real
match it costs at most ~0.04 s per pile visit, × 6 visits = **≈0.25 s per 30 s run** [E].

| | Primary: "Featherfoot: slips through crowds" | Alternative: "Featherfoot: light-footed banker" |
|---|---|---|
| Effect | Ignores the pile slow **and the collision slow** (0.75 within 1.2 m [R]) | Ignores the pile slow, and deposits ×1.5 (**proposal**) |
| Player text | "Bumping into birds or piles never slows you" | "Banks 50% faster" |
| Hook | `IgnoresPileSlow` (exists) plus **new** `IgnoresCollisionSlow(self)`, with its single call site in `CheckCollisionSlow`'s `ApplySlow(SlowSource.Collision, …)` branch | `IgnoresPileSlow`, `ModifyDepositRate` (exists, unused today) |
| Oracle / economy | 0 s naked [C] (no other chickens). In contact today both birds drop to 0.75×, so a Speedy pulling away from a Warrior gains 1.52 m/s. With the primary it gains **4.56 m/s** [C: 12.15 − 0.75 × 10.125] | Speedy 30.02 → **28.54 s** (−1.48 s, −4.9%) [C]; ×1.25 would give 29.13 s [C]. This is a farming buff outside the Oracle, so it needs a declared spec target (§4.4) |
| Stacking risk | Speedy already owns three escape tools (Speed Burst, Dust Kick, Feint). This adds a fourth, passive one. It does **not** dodge Spine Coat (proximity steal), Snatch or Sneaky Steal (ranged), or zones | Multiplies with Quick Drop (`ResolveDepositRate` stacks both): 9 × 1.5 × 4 = 54/s, so a 10-food load banks in 0.19 s. Harmless, but Quick Drop becomes pointless on this spec |
| Fit with the spec fantasy ("The Anxious") | Strong: wriggles out of a scrum | Weak: banking is not anxiety |
| Recommendation | **Pick this** | Fallback if a new hook is unwanted |

### 1.4 Slot "Assassin alternative" (was Opportunist, now Thief)

**Verdict: live and readable.** Sneaky Steal 6 → 9.6 and Snatch 5 → 8 per rival [C]. Both are
capped by the Assassin's 10 free cargo [R].

| | Primary: keep Thief | Alternative: "Opportunist v2" (the original idea, without damage) |
|---|---|---|
| Effect | Steal ×1.6 (unchanged) | Steal ×1.3, and **×1.8 against a slowed, rooted or stunned target** (**proposal**) |
| Player text | "Every steal takes 1.6× more" | "Steals take more, and much more from birds that are slowed, stuck or stunned" |
| Hook | `ModifyStealAmount` (exists) | **Signature change:** `ModifyStealAmount(float amount, ChickenController self, ChickenController victim)`. Keep the single call site, `ResolveStealAmount`, which must pass the victim. `MaxStealMultiplier` = 1.8, so `StealRules.MaxSingleSteal` rises from ×1.6 to ×1.8 |
| Oracle / economy | 0 s (exempt class; the Predation axiom is unsolved) | 0 s. In practice a control-assisted Sneaky Steal takes 10.8, so the free-space cap of 10 sets the result |
| Stacking risk | Low | Overlaps the Reaper ("stun, then take everything"), so Burglar becomes a mini-Reaper without the removal. ×1.8, not ×2, keeps a gap. Ambush (own stun) followed by Sneaky Steal is a two-button full rob |
| Recommendation | **Pick this** (no change) | Only if the Burglar playtests as flat |

### 1.5 Bonus slot "Warrior alternative" (was Bracer, now Bully): hidden SCT buff

Bully's +6 cargo turns Warrior 14 → 20. The result is **30.91 s / 2 trips**, against the
governed 34.98 s / 3 [C]: 4.07 s faster, as fast as Speedy and Fatty. That erases the Warrior
"35 s" identity for anyone who picks Bully. No test covers it.

The Oracle is **not monotonic** in cargo, because greedy nearest-pile routing changes with cap
[C]:

| Warrior cap | 14 (shipped) | 15 | 16 | 17 | 18 | 19 | 20 (Bully) |
|---|---|---|---|---|---|---|---|
| SCT [C] | 34.98 / 3 | 31.65 / 3 | 38.77 / 3 | 38.77 / 3 | 38.77 / 3 | 41.14 / 3 | 30.91 / 2 |

So a "smaller bonus" does not fix it predictably.

| | Primary: "Bully without the hidden buff" | Alternative: keep +6, but govern it |
|---|---|---|
| Change | `BonusCapacity` 6 → **0**, `StealMultiplier` 1.4 → **1.5** (proposals). Scrap 3 → 4.5, Dive Bomb 8 → 12 (capped by free space) | Keep both values. Add a declared spec target "Warrior/Bully 31 s / 2 trips" to the spec-aware Oracle test (§4.4) |
| Oracle | Back to 34.98 s / 3 [C] | 30.91 s / 2, now visible and pinned |
| Cost | A loaded Bully can no longer steal (the free-space rule), which is the designed "steal → bank → return" rhythm | Bully remains the better farming Warrior; Relentless is weaker for pure farming |

Hoarder is the same class of effect: Fatty cap 40 gives **25.82 s / 1 trip** (−4.23 s) [C]. It is
designed (the one-trip win) and has a test for the 40 floor. It still sits outside the axiom,
though, and should be pinned the same way (§4.4).

---

## 2. CW-057: broad balance pass

### 2.1 What the evidence does and does not support

| Evidence | What it supports | What it does not support |
|---|---|---|
| Oracle [C], time split per class: Speedy travel 14.22 / peck 11.36 / deposit 4.44 s; Fatty 12.82 / 12.78 / 4.44; Warrior 17.10 / 13.43 / 4.44 | Peck time is 38–42% of SCT and deposit time is 15%. A stun of *s* seconds costs a farming victim about *s* seconds of SCT. Victim slack in a 45 s match is **14.98 s** (Speedy), **14.95 s** (Fatty), **10.02 s** (Warrior) | Anything about multi-player contact, walls (detours up to 1.38× on seat 0 are not modelled), or abilities |
| Map budget [R]: 80 food = 2 × 40, four players | The **average** bank is about 20 food per player, so a 40-food win needs half the map. Timer expiry is the normal ending | Nothing about human routing |
| Harness [H] (about 41 four-seat matches on 2026-10-21, bots plus autoplay) | About 89% of matches end on the timer (79 timer vs 10 food-target end lines). Winner by class: Fatty 27, Assassin 9 (**6 of them with 0 deposited**, i.e. the Spoiler bonus), Speedy 4, Warrior 1. Mean deposited: Fatty 24.7, Speedy 19.6, Warrior 9.3, Assassin 3.3. **0 executes in every match** | Human balance. Seats and classes are confounded (the same class per seat), autoplay foraging defects existed earlier (CW-214), and bot logs carry no bot id |
| GDD §3.9 (observed by humans) | Encounters are rare on the 51.3 m arena | How often encounters happen *after* a density fix, which changes every control and steal number |
| No human 4-player data after the v0.4 redesign | — | **Any change whose case rests on "feels too strong/weak"** |

The consequence: the safe changes are correctness fixes, zero-Oracle-impact fixes and
tooling. Magnitudes of slows, stuns, steals and radii stay where they are until a human session,
and that session should come **after** or **together with** the arena-density decision (§3.9),
because density sets how often any of these numbers fire.

### 2.2 Cooldown tiers

Shipped cooldowns [R]:

| Range | Abilities |
|---|---|
| 3–5 s | Roll & Push 3, Snatch 3, Headbutt 4, Ruffle 5, Scrap 5, Feint 5, Sneaky Steal 5, Death Mark 5 |
| 6–9 s | Dive Bomb 6, Dust Kick 6, Speed Burst 6, Shadowstep 6, Cluck Shock 7, Feather Aura 7, Feather Trap 7, Sticky Egg 7, Egg Shell 8, Invisibility 8, Spine Coat 8, Ground Quake 9 |
| 10–12 s | Ambush 10, Belly Flop 10, Turtle Mode 10, Doppelganger 11, Smoke Roost 11, **Wing Slam 12**, Quick Drop 12, Immovable 12 |

GDD §7.3 lists Short 3–6 / Medium 8–12, so **7 s falls in no tier**, and five abilities sit on 7.
Class means (hand-pickable plus pre-equips) [C]: Warrior 6.5 s, Assassin 7.4, Speedy 7.2,
Fatty 8.4. Warrior is already the fastest class on average; Wing Slam is its one outlier.

- **Safe (doc-only, proposal):** redefine the tiers as **Quick 3–5 / Standard 6–9 / Heavy 10–12**,
  with the rule that "Heavy is reserved for immunity, a hard AoE stun of at least 1.4 s, or an
  economy burst". Under that rule Wing Slam (1.5 s stun, smallest stun area in the game) belongs
  in Standard. See §3.
- **Needs playtest:** any other tier move (Quick Drop 12, Immovable 12, Doppelganger 11, Smoke Roost 11).

### 2.3 Pile slow and collision slow

| Constant [R] | Value | Finding |
|---|---|---|
| `PileSlowFactor` | 0.80 inside collect reach (1.0 m) | Close to inert (§1.3): 0 in the Oracle by construction, about 0.25 s per Speedy run [E] |
| `CollisionSlowFactor` / `CollisionSlowRadius` | 0.75 / 1.2 m, **applied to both** chickens | A chaser in contact keeps the same relative speed as without the slow (both are scaled), so it does not help catch anyone. Its only real effect is congestion at contested piles. CW-226 showed it toggling at the radius edge (cosmetic) |

- **Safe:** none of the magnitudes.
- **Low risk, but proposal plus scenario check:** collision-slow hysteresis, enter at 1.2 m and
  exit at 1.3 m. This removes the flag flicker seen in CW-226. It is a code constant, so it
  needs the scenario matrix and no playtest.
- **Needs playtest:** making the pile slow meaningful, e.g. a 2.0 m band. That would no longer be
  0 in the Oracle: `BalanceOracle` would have to model the band, and all three classes would need
  a re-solve. Do not change this until the arena-density decision is made.

### 2.4 Control durations

Shipped [R]:

| Kind | Abilities |
|---|---|
| Stuns | Ambush 1.0, Belly Flop 1.4, Wing Slam 1.5, Headbutt 0.35 |
| Roots | Ground Quake 1.8, Sticky Egg 2.0 |
| Timed slow | Dust Kick 0.55× for 2.5 |
| Presence slows | Feather Aura 0.55×, Feather Trap 0.45× (5 s zone), Smoke Roost 0.55× (5.5 s zone) |

Effect on passive holders [C]: a Slippery victim gets stuns of 0.6 / 0.84 / 0.9 / 0.21 s; a
Bulwark victim gets 0.65 / 0.91 / 0.975 / 0.23 s.

**Correctness finding (safe, proposal, code):** `RPC_ApplyStun`, `RPC_ApplyRoot` and
`RPC_ApplyAbilitySlow` **replace** the running timer (and, for slows, the factor) instead of
keeping the stronger or later one [R, `ChickenController` L1227–1258]. Consequences:

- A Warrior who Wing Slams and then Headbutts cuts his own victim's stun from 1.5 s to 0.35 s.
  This is likely common: Headbutt is the Relentless forced signature, and Wing Slam is a free pick.
- A third party's Headbutt can also rescue a Death-Marked victim early.
- A weaker slow (0.55× Dust Kick) arriving during a stronger one replaces it.

Proposal: a stun or root keeps `max(remaining, new)`; a timed slow keeps the strongest factor and
the later expiry. That matches GDD §7.4 ("same kind never stacks: strongest wins"). Oracle
impact is 0. Stun-chain ceiling after the fix: about 3.9 s (Wing Slam 1.5 + Belly Flop 1.4 +
Ambush 1.0), which is already reachable today.

- **Needs playtest:** every stun, root and slow magnitude, and whether Slippery and Bulwark should
  also soften presence slows (e.g. factor `1 − (1 − f) × mult`). That would need a new hook,
  `ModifyPresenceSlow(factor, self)`, with one call site in `ApplySlow` for `SlowSource.Ability`
  and `Collision`.

### 2.5 Passive magnitudes

| Passive [R] | Oracle impact [C] | Proposal |
|---|---|---|
| Relentless 0.75 | 0 | Keep (§1.2) |
| Bully 1.4 / +6 | **−4.07 s, 3 → 2 trips** | §1.5 primary: +0 / 1.5 (proposal) |
| Slippery 0.6, Bulwark 0.65 / 0.25 | 0 | Keep. Fix the perk text, which overstates coverage of slows (§1.1) |
| Featherfoot | ≈0 | §1.3 |
| Hoarder 40 | **−4.23 s, 2 → 1 trip** | Keep (designed). Pin it with a spec target (§4.4) |
| Thief 1.6 | 0 | Keep |
| **Spoiler +15** | n/a | **Wins from zero in the harness, 6 of about 41 [H].** Primary (proposal): `bonus = min(BonusFood, own banked food)`, "doubles your bank, up to +15", which cannot win from 0 by definition. Same hook (`MatchEndBonusFood`), but `GameManager` must pass the chicken's banked total: signature `MatchEndBonusFood(self, float banked)`. Alternative: GDD §11's parked "Spoiler v2", +3 per landed steal or execute, capped at 15, reading counts from `ChickenMatchStats`. Both need a playtest; the harness proves only that the gap exists |

### 2.6 Bot radii and Hunt ↔ Flee hysteresis

Shipped profiles [R, `BotTactics.ProfileFor`]:

| Class | Danger | Hunt | Engage | Guard | Protect | Return | Think |
|---|---|---|---|---|---|---|---|
| Warrior | 4.0 | 11 | 9 | 6 | 0.60 | 0.85 | 0.28 |
| Speedy | 7.5 | — | — | — | 0.10 | 0.45 | 0.22 |
| Fatty | 6.0 | — | — | 7 | 0.35 | 0.90 | 0.30 |
| Assassin | 6.5 | 16 | 7 | — | 0.05 | 0.30 | 0.25 |

One shared `_stateExitRadiusFactor` of 1.35 is used for both Flee and Hunt/Stalk.

- **Coherence gap (safe for bots, proposal):** the Warrior's `dangerRadius` of 4.0 is smaller than
  the longest steal reaches: Sneaky Steal 5.4, Dive Bomb 3.25 + 2.05 = 5.3. A loaded Warrior bot
  lets a thief stand inside steal range without reacting. Proposal: **4.0 → 5.5** (just past
  5.4). Validate with the scenario matrix only (bots, not humans).
- **Evidence gap:** Flee/Hunt flip rates cannot be measured, because `[Bot]` log lines carry no
  bot identity. A solo session logs 27–40 Flee and 26 Hunt transitions [H], and they cannot be
  split per bot. **Safe tooling proposal:** prefix Bot transition lines with class and corner,
  then measure "Flee ↔ Hunt/Stalk flips within 1.0 s per bot-minute", with a target below 1.
  Leave the 1.35 factor and the hunt radii alone until that number exists.
- **Predation gap:** 0 executes in all harness matches [H], so the Assassin bot never completes
  Death Mark. No Assassin balance claim is supported until a bot or a human lands executes.

---

## 3. CW-058: Wing Slam

Shipped [R]: cooldown **12 s** (the longest in the game, tied with Quick Drop and Immovable),
stun **1.5 s**, radius **4.5 m**, **120° cone** ahead (`WingSlamAbilitySO.AimShape = Cone`,
`AimConeAngle 120`), `Duration` 1.5, Category Control, BotRole Auto (→ Control). Relentless
gives it 9 s.

**Oracle consequence of every candidate: 0 s on all three SCTs** [C]. Wing Slam is not in the
naked loadout, and Relentless excludes Peck. The consequences that matter are on victims and
on the Assassin's execute (any stun qualifies a marked target, GDD §6.4). The table measures
those [C]:

- Efficiency = stun × area / cooldown.
- Casts per 45 s assume the ability is ready at GO.
- "Max stun-s" = casts × stun on one target (a theoretical ceiling; real land rates will be far
  lower). Compare it to the victim slack: Speedy 14.98 s, Warrior 10.02 s.
- Food denied is measured against a farming Speedy (1.33 food/s).

| Candidate | Cooldown | Stun | Radius | Cone | Area m² | Uptime | Efficiency | Casts / 45 s | Max stun-s | With Relentless: cooldown / casts / efficiency | Stun on Slippery | Food denied per land |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Shipped | 12 | 1.5 | 4.5 | 120° | 21.2 | 12.5% | 2.65 | 4 | 6.0 | 9.0 / 5 / 3.53 | 0.90 | 2.00 |
| **WS-B (rec.)** | **9** | 1.5 | 4.5 | 120° | 21.2 | 16.7% | 3.53 | 5 | 7.5 | 6.75 / 7 / 4.71 | 0.90 | 2.00 |
| WS-A (alt.) | **8** | **1.3** | 4.5 | 120° | 21.2 | 16.2% | 3.45 | 6 | 7.8 | 6.0 / 8 / 4.59 | 0.78 | 1.73 |
| WS-D (conservative) | **10** | 1.5 | 4.5 | 120° | 21.2 | 15.0% | 3.18 | 5 | 7.5 | 7.5 / 6 / 4.24 | 0.90 | 2.00 |
| WS-C (spammy) | **7** | **1.0** | **5.0** | 120° | 26.2 | 14.3% | 3.74 | 7 | 7.0 | 5.25 / 9 / 4.99 | 0.60 | 1.33 |
| WS-E (wide) | **10** | **1.2** | 4.5 | **180°** | 31.8 | 12.0% | 3.82 | 5 | 6.0 | 7.5 / 6 / 5.09 | 0.72 | 1.60 |

Reference stuns (same metric, [C]):

| Ability | Efficiency | Shape and timing |
|---|---|---|
| Ambush | 4.07 | 1.0 s, 3.6 m circle, 10 s |
| Belly Flop | 7.04 | 1.4 s, 4 m circle, 10 s; pays for it with a jump telegraph |
| Ground Quake | 12.7 | root, not stun: 1.8 s, 4.5 m, 9 s |
| Headbutt | 0.69 | 0.35 s stagger, 70° cone |

**Recommendation: WS-B, cooldown 12 → 9 s (proposal), nothing else changed.**

- One field to change, and the description template stays valid.
- It moves Wing Slam into the Standard tier, which fits the "spam class" essence (GDD §11 item 2a).
- Efficiency 3.53 stays **below Ambush (4.07)**, so the Warrior does not become the best stunner.
- The stun keeps 0.9 s on a Slippery target, which is still a usable execute window for a
  third-party Assassin (Ambush on Slippery is only 0.6 s).
- With Relentless, 6.75 s and 7 casts per match is the ceiling to watch in the playtest.

**Alternative: WS-A, 8 s and 1.3 s (proposals).** More casts, shorter lock, nearly the same
efficiency. Pick it if playtesters report "stunned too long". WS-C is not recommended: its 1.0 s
stun gives 0.6 s on Slippery, and a 7 s cooldown makes it the Warrior's second spam button next
to Headbutt. WS-E changes the readable shape ("ahead") for a gain the cooldown alone provides.

**Precondition:** the stun-overwrite fix in §2.4. A shorter Wing Slam cooldown increases how
often Headbutt truncates it.

**FFA note:** each extra Wing Slam per match is also one more stun window for any Assassin who
has a mark armed nearby. Going from 4 to 5 casts (from 5 to 7 with Relentless) is a small
Predation buff that the Oracle cannot see. Watch the executes-per-match number in the playtest.

---

## 4. Validation protocol (for anything that gets approved)

1. **SCT guard (must stay green, never widened):**
   `BalanceOracleTests.RealMap_ShippedClassAssets_SatisfyTheSctAxiom`, ±0.5 s
   (`SctTargets.ShippedAssetToleranceSeconds`), all 4 seats, trip count exact. Nothing proposed
   here changes `ChickenStatsSO`, the map or `MatchConfig`, so this test should not move. If it
   does, the change touched something it should not have.
2. **Break it on purpose once** (memory rule: a test that restates its inputs cannot see drift).
   Before trusting a new test, mutate the value it guards (e.g. Bully `BonusCapacity` back to 6)
   and confirm it goes red.
3. **New, proposed: a spec-aware Oracle test.** For each passive, build the `OracleChicken`
   through the real hooks: `ModifyCargoCapacity` → cap, `ModifyDepositRate` → deposit rate,
   `ModifyCooldown(Peck)` → peck cooldown. Assert either within ±0.5 s of the class target, or
   within ±0.5 s of an explicitly declared `SpecSctTargets` entry with a reason. Today it would
   go red on Bully (−4.07 s) and on Hoarder (−4.23 s, which should be declared). This turns
   "outside the Oracle" from silent into declared.
4. **Scenario matrix (regression, not balance):** `tools/test-session/scenario-pc.ps1` in solo,
   shared2, shared4 and hostleave, 3 rounds each. `verify.ps1` must PASS (0 ERROR / EXCEPTION /
   INVARIANT; SYNC per the current rules) and `selftest.ps1` must be green. Read the
   `MATCH SUMMARY` blocks for degenerate outcomes only, for example "Assassin wins with 0
   deposited", which should drop to 0 under the Spoiler primary.
5. **EditMode suite** unfiltered (2031 at last count). `PerkTextTests` must follow any perk-text
   template change. `StealRulesTests` must follow `MaxStealMultiplier`.
6. **Human playtest** for every "needs playtest" item: 4 humans, at least 8 matches, mixed
   classes, seats rotated. Record encounters per minute, stuns landed (and on whom), steals,
   executes, end reason, banked food at expiry, and one feel question per changed ability
   ("Wing Slam: too rare / right / too often"). Run it after, or alongside, the arena-density
   decision (GDD §3.9).
7. **Docs and compendium.** After anything lands: GDD §5.3, §7.2, §7.3 and §11, and
   `docs/site/index.html` (numbers read from the assets), republished to the same Artifact URL
   (CLAUDE.md rule 6). Also BACKLOG CW-053/057/058 and STATE.md.

### Change classification

| Class | Items |
|---|---|
| **Safe now** (0 Oracle impact; correctness, tooling or doc) | Stun/root/slow re-application fix (§2.4) · spec-aware Oracle test (§4.3) · bot log identity (§2.6) · GDD §7.3 tier rewrite (§2.2) · perk text for Slippery and Bulwark (§1.1) |
| **Low risk, needs scenario matrix only** | Wing Slam 12 → 9 (§3) · Warrior bot `dangerRadius` 4.0 → 5.5 (§2.6) · collision-slow exit hysteresis 1.3 m (§2.3) · Bully +0 / ×1.5 (§1.5; restores the Oracle, changes feel) |
| **Needs a human playtest** | Featherfoot redesign (§1.3) · Spoiler cap or rework (§2.5) · all stun, root and slow magnitudes · pile-slow band · Hunt/Flee factor and hunt radii · other Heavy-tier cooldowns · Warpath and Opportunist v2 alternatives |

---

## 5. Decisions for Maestro

- [ ] **CW-053 premise:** accept that Mighty, Bracer and Opportunist were already replaced
      (`62219fc`), and rescope CW-053 to "Featherfoot is near-inert; Bully is a hidden SCT buff".
- [ ] **Warrior signature:** keep **Relentless 0.75** (rec.) · or switch to **Warpath** (Control cooldowns ×0.65, proposal).
- [ ] **Speedy Featherfoot:** **ignore pile and collision slow** (rec.; new hook `IgnoresCollisionSlow`) · or **deposit ×1.5** (−1.48 s SCT, declared spec target) · or leave as is.
- [ ] **Assassin Thief:** keep **×1.6** (rec.) · or **Opportunist v2** (×1.3 / ×1.8 vs controlled; hook signature change).
- [ ] **Bully:** **+0 cargo, steal ×1.5** (rec.; Oracle back to 34.98 s / 3) · or keep +6 and declare "Warrior/Bully 31 s / 2".
- [ ] **Spoiler:** **bonus = min(15, own banked)** (rec.; hook gets `banked`) · or Spoiler v2 earned bounty · or keep +15 until a human playtest.
- [ ] **Wing Slam:** **WS-B, cooldown 9 s** (rec.) · WS-A 8 s / 1.3 s · WS-D 10 s · keep 12 s.
- [ ] **Stun/root/slow re-application fix** (`max` of remaining / strongest factor): approve as a precondition to Wing Slam.
- [ ] **Spec-aware Oracle test** (`SpecSctTargets`, declared deltas): approve.
- [ ] **Cooldown tiers:** rewrite GDD §7.3 as Quick 3–5 / Standard 6–9 / Heavy 10–12 with the Heavy rule.
- [ ] **Bots:** Warrior `dangerRadius` 4.0 → 5.5 · bot log identity first, hysteresis numbers later.
- [ ] **Collision slow:** add an exit hysteresis of 1.3 m (cosmetic flicker fix) · or leave it.
- [ ] **Sequencing:** hold every "needs playtest" magnitude until the arena-density decision (GDD §3.9) plus one human 4-player session.
- [ ] **Chore (spotted, out of scope):** the dead constant `ChickenController.ToughDamageBonus`, a damage-era leftover, can be deleted.
