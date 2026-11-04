# Assassin (Spoiler) kit against the 4 s mark: proposal

Status: design proposal, 2026-11-04 (mechanics-designer). Nothing here is implemented. Branch `balance`.
Input: Maestro's playtest note of 2026-11-04 (the "desperate doubt" moment). Taken as given, being built in parallel:
mark window 4 s (`MarkKill.asset` `WindowSeconds`, landed in `e4515dc6`), arm floor 0.6 s
(`ArmSeconds`), a far-from-home weighted pick (`roll * w * w`, w = distance to own base / arena base-to-base distance, clamped
0..1, last mark excluded unless the only rival, uniform fallback when every score is 0), and an Assassin carrying cargo can be
stolen from.

## 1. The numbers this rests on

| Quantity | Value | Source |
|---|---|---|
| Sneaky (Assassin) speed | 12.15 m/s | `Classes/Assassin.asset` `MoveSpeed` |
| Speedy speed | 12.15 m/s | `Classes/Speedy.asset` `MoveSpeed` |
| Warrior, Fatty speed | 10.125 m/s | `Classes/Warrior.asset`, `Classes/Fatty.asset` `MoveSpeed` |
| Kill range | 8 m | `MarkKill.asset` `KillRange` |
| Arm floor / window | 0.6 s / 4 s (given) | `MarkKill.asset` `ArmSeconds`, `WindowSeconds` |
| Cooldowns: at GO / after a miss / after a kill | 4 / 5 / 15 s | `MarkKill.asset` `RoundStartCooldownSeconds`, `FailCooldownSeconds`, `SuccessCooldownSeconds` |
| Victim sees the mark | after the 0.6 s draw | `FeedbackTuning.MarkDrawSeconds` |
| Ambush | 3.6 m radius, 1 s stun, cooldown 10 s | `Ambush.asset` `StunRadius`, `StunDuration`, `Cooldown` |
| Shadowstep | 3x for 0.25 s = 9.11 m (6.08 m more than walking), cooldown 4 s | `Shadowstep.asset` `SpeedMultiplier`, `Duration`, `Cooldown` |
| Smoke Roost | 4 m cloud, 5.5 s, rivals inside at 0.55x; caster fades 3.5 s, a move breaks it; cooldown 11 s | `SmokeRoost.asset` `ZoneRadius`, `ZoneDuration`, `SlowFactor`, `Duration`, `Cooldown` |
| Base wall | 2.5 m | `MatchConfig.asset` `BaseKeepOutRadius` |
| Bases | 0.85 of the way out to the corners at +-25.65 m: (+-21.80, +-21.80) | `MapGenerator.BaseInsetFraction`, `Game.unity` `_baseCornerDistance` |
| Base to base | adjacent 43.6 m, opposite 61.7 m (straight) | derived from the row above |
| Base 0 to piles | own personal 7.9 m, its two edge contested 21.9 m, centre 30.8 m, the far contested 47.4 m | `MapGenerator` pile formulas + `Game.unity` insets (`_personalPileInset` 0.3673, `_contestedEdgeInset` 0.7895) |

**Derived Assassin reach.** Lunge = Shadowstep 9.11 m + Ambush 3.6 m = **12.7 m**: inside it the stun lands in 0.25 s. In the
3.4 s kill span (4.0 - 0.6) a walk-then-lunge covers 41.3 + 6.1 + 3.6 = **about 51 m of path** against a target that stands
still. Against a target running away on open ground: a Speedy is never closed on by walking, so only the lunge counts (gap at
most 9.7 m); a Warrior or Fatty loses 2.0 m/s, so a gap up to about 17 m.

### How far a weighted pick is

Monte Carlo over the shipped geometry (bases and piles from the generator formulas; paths are straight lines times 1.10, an
assumption: walls and arms are not modelled, see section 6). Rivals are placed by a farming model (half the time at a pile,
half in transit from home; piles by food: centre 20, edge contested 10 each, own personal 5, far contested 5 each). The
Assassin is either camping the centre or roaming like a farmer. Opposite base-to-base normalisation (61.7 m); the adjacent one
(43.6 m) moved no figure by more than about 0.02 (checked on the reach and run-home figures).

| | Uniform pick (today) | Weighted pick (given) |
|---|---|---|
| Target's distance from its own base, median | 23.6 m | **33.2 m** |
| Speedy time to its wall, median / share needing more than 4 s | 1.73 s / 7% | **2.53 s / 17%** |
| Warrior or Fatty time to its wall, median / more than 4 s | 2.08 s / 10% | **3.03 s / 24%** |
| Mark starts inside the 12.7 m lunge, Assassin at the centre | 27% | **42%** |
| Same, Assassin roaming | 12% | 17% |
| Target out of reach in 3.4 s (more than 51 m), Assassin at his own base | 17% | 9% |
| Assassin gets in Ambush range before a victim **running home** gets inside its wall (Assassin roaming): Speedy / Warrior-Fatty | 28% / 40% | **50% / 66%** |
| Same, Assassin camping the centre | 43% / 61% | **70% / 86%** |

The 4 s window barely changes the run-home race (it is decided in under 3 s either way; 7 s gave the same figures within 0.03).
What it changes is the kite: with 7 s a victim running straight away from the Assassin got cornered in 99% of runs; with 4 s
about 20-25% of victims escape by running alone (crude open-arena pursuit, a lower bound for a real player using cover).

**Reading.** Maestro's goal is already mostly met by the weighting: "go home" goes from a near-safe default to a coin flip, and
the 4 s window makes "run and dodge" a real alternative. Two new problems appear:

1. **Point-blank marks.** The weighting likes birds near the centre and the Assassin's best spot is the centre, so 42% of marks
   can start inside his lunge. The stun can land before the 0.6 s draw has even told the victim, and the Kill qualifies at 0.6 s.
   No doubt, no choice: just dead.
2. **A miss starves the next hunt.** Next mark at 4 + 5 = 9 s after the press; an Ambush spent at second *a* of a hunt is ready
   at *a* + 10. A whiff after about 2.5 s leaves the next hunt without its stun, so the Assassin waits, which is not rushing.

## 2. What each victim kit has inside the window

Ambush stun lengths are after the passive (`Slippery.asset` `DurationMultiplier` 0.6, `Bulwark.asset` 0.65). Any stun from
anyone qualifies a Kill (`AssassinExecute`: `ControlState.Stunned`).

| Kit | Answer | What it buys inside 4 s | Doubt it creates |
|---|---|---|---|
| Slippery | Dust Kick (`DustKick.asset`: 4.5 m rear 75 deg, 0.55x for 2.5 s, cd 6); Feint (`SidestepImpulse` 11, cd 5); stun only 0.6 s | Kick: Assassin at 6.7 m/s, victim gains 13.7 m, the hunt is over. Kick reach 4.5 m against a 12.7 m lunge, so it must be thrown early | Kick early and miss, or late and get stunned |
| Featherfoot | Speed Burst (`SpeedBurst.asset` 1.5x, 1.5 s, cd 6); Feather Aura (`FeatherAura.asset` 5.4 m, 0.55x, 3 s, cd 7) | Burst: +9.1 m. Aura: Assassin loses 16.4 m over 3 s | Burst home or burst away; aura now or at contact |
| Relentless | Wing Slam (`WingSlam.asset` 4.5 m, 1.3 s stun, cd 8 x0.75 = 6); Cluck Shock 4.5 m shove | Wing Slam outranges Ambush (4.5 vs 3.6): a stunned Assassin cannot cast and loses 1.3 s of 3.4 | Fight or flee, at 2 m/s slower |
| Bully | Headbutt (`Headbutt.asset` 3.6 m cone, 0.35 s stagger, shove 7.5, cd 4); Ruffle (`Ruffle.asset` 1.3x 2 s = 13.2 m/s, +6.1 m) | Thin: Headbutt ties Ambush's reach, Ruffle outruns him for 2 s only | **Weakest kit against a hunt** |
| Hoarder | Turtle Mode (`TurtleMode.asset`: walk 3 s at 0.5x, lock up to 2 s, cd 10) | Shelled = cannot be executed. Walk + lock = 5 s, longer than the window. Cost: about 15 m of travel lost | Press now or wait; the Assassin can deny the lock with a stun and kill as the walk ends |
| Bulwark | Immovable (`Immovable.asset` 3 s, cannot be stunned, cd 12); stun 0.65 s | Pressed at 1.0 s or later it covers the rest of the window. Cost: rooted 3 s, about 30 m | Press too early and the last 0.4 s is open |
| Thief | Snatch (`Snatch.asset` 4.3 m, shove 8.1, cd 3); equal speed | Snatch outranges Ambush and shoves him off. Invisibility and Doppelganger do nothing: the hunt marker stays visible through Invisibility, decoys are never marked (GDD 6.4) | Shove or run |

Every kit has an answer, which is what the 4 s window needs. Bully's is the weakest; Hoarder and Bulwark's are the strongest
(near-immunity for one button, paid in movement).

## 3. Options

### Option A: ship as given, no kit change

All four Assassin assets and the three cooldowns unchanged.

- **Feel:** the rush is real (median reach to a weighted pick 1.1 s, nearly all picks reachable from a central or roaming
  position). Smoke Roost does almost nothing in a hunt: its 4 m cloud is barely wider than Ambush's 3.6 m.
- **Counterplay:** good when the mark starts far away; none when it starts inside the lunge (17% of marks roaming, 42% camping).
- **Risk:** camping the centre becomes the Assassin's best play and the victim's worst experience. A miss late in a hunt starves
  the next one.

### Option B (recommended): a warning beat, and a stun for every hunt

1. **Stuns that start before the arm do not count.** `MarkKill.asset` `ArmSeconds` 0.6 -> **1.0** and a new rule: the target's
   stun must *begin* at or after the arm, not just be running. 1.0 = draw 0.6 s (`FeedbackTuning.MarkDrawSeconds`) + 0.4 s to
   react, so it is derived, not picked: if the draw changes, the arm follows. The kill span is 3.0 s (1.0-4.0). A point-blank
   mark now gives the victim 0.4 s after MARKED! (1.0 s after the visible sweep starts) to spend a cooldown, which is exactly
   the choice Maestro asked for. The Assassin still has to arrive by about 3.5 s, so the rush stays. On screen: KILL stays dark
   until the arm, as now. Code, not data (see the senior-dev notes below).
2. **A new mark readies Ambush.** Pressing Death Mark (the Mark press, not the Kill) clears Ambush's cooldown. Ambush stays a
   10 s stun outside hunts, and every hunt has its stun, so the Assassin re-marks the moment the 5 s cooldown ends instead of
   waiting. Data-only fallback if the code is not wanted: `Ambush.asset` `Cooldown` 10 -> 8; a typical whiff (1.5 s into a hunt)
   is then ready before the next press, but a whiff at the very end of a window is still only ready 2 s into the next kill span.
3. **Shadowstep unchanged** (0.25 s / 4 s): one lunge per hunt, sometimes two. The Underdog Surge question stays open (section 6).
4. **Smoke Roost unchanged, new role:** it is the getaway after a kill. The bounty bag is now stealable, so a cloud that slows
   anyone chasing him (0.55x, 5.5 s) and fades him is the way home. Watch whether players use it that way; if not, it is the slot to
   replace in the next pass.

- **Feel:** the mark always has a beat; MARKED! means "act now", never "you are already dead".
- **Counterplay:** every kit's answer (section 2) fits inside the beat or the hunt. A centre-camping Assassin still gets close
  fast, but must out-guess the victim's cooldown instead of deleting it.
- **Fairness:** the 0.4 s beat is short on purpose; a victim who ignores MARKED! still dies.
- **Risk:** the Assassin loses 0.4 s of kill span (3.4 -> 3.0 s). Against far targets this does not matter (the kill is late
  anyway). Hoarder and Bulwark get relatively stronger: watch them.

### Option C: no point-blank picks (change the pick, not the kill)

Score 0 for any rival closer to the Assassin than lunge + reaction: 12.7 m + 12.15 m/s x 0.75 s = **21.8 m** (derived from
`Ambush.asset` `StunRadius`, `Shadowstep.asset` `SpeedMultiplier` x `Duration`, `Assassin.asset` `MoveSpeed`, the 1.0 s beat
minus the dash). Pure `MarkPickRules`, no network rule touched, `ArmSeconds` stays 0.6.

- **Feel:** every hunt is a real run of at least about 1 s.
- **Counterplay / risk:** **exploitable**: hugging the Assassin makes you unmarkable, and around the centre pile that happens
  naturally. It also breaks Maestro's "anyone, anywhere" rule. Not recommended; listed because it is the cheapest change.

**Recommendation: Option B.** The weighting plus the 4 s window already turns "go home" into a gamble (the run-home race is lost
50-66% of the time against a roaming Assassin, up from 28-40%) and makes running and cooldowns real escapes. What breaks the
"desperate doubt" moment is the point-blank mark (up to 42% of marks), which B fixes with a 0.4 s beat derived from the draw,
and the starved hunt after a miss, which B fixes by readying Ambush on every mark. Ability numbers stay as they are, so the kit
plays as it was tested, with two rules added.

## 4. Death Mark cooldowns: keep all three

Marks per 45 s round (`MatchConfig.asset` `MatchDurationSeconds`):

| Pattern | Window 7 s (today) | Window 4 s |
|---|---|---|
| Every mark misses (cycle = window + 5 s) | presses at 4, 16, 28, 40: **4 marks** | presses at 4, 13, 22, 31, 40: **5 marks** |
| Every mark kills about 2 s in (cycle about 17 s) | 3 kills | 3 kills |
| Per rival (3 rivals, never twice in a row) | about 1.3 marks | about 1.7 marks |

- **At GO, 4 s: keep.** By then rivals have had time to walk 40-49 m (the centre is 30.8 m from every base), so the first weighted pick has
  real targets and nobody is still at home. At 3 s most would only be reaching the centre pile.
- **After a miss, 5 s: keep.** The 4 s window already raises the rate from 4 to 5 marks a round; each rival lives through a
  mark about every 25 s. Shorter would make the darkness constant and wear out the sting. Longer would leave the Assassin with
  nothing to do: he has no Peck.
- **After a kill, 15 s: keep.** A kill is the largest swing in the game (all cargo, 3 s out, respawn at home); three a round
  at most is already plenty. Option B's Ambush refresh makes the 15 s feel no worse.

## 5. The Balance Oracle

- **No naked stat changes** (`MoveSpeed`, `CargoCapacity`, `PeckAmount`, `PeckCooldown`), so the SCT targets
  (`SctTargets.cs`) do not move and **no SCT re-solve is needed** for A, B or C.
- The Assassin is SCT-exempt (`SctTargets.Exempt`); its "Predation axiom" is still unsolved. Section 1's reach and run-home
  numbers are the first draft of it. Proposal: a `PredationOracle` over `ShippedMap` (`Editor/Tests/ShippedMap.cs`) that asserts
  "p90 weighted-pick reach from the centre <= WindowSeconds - ArmSeconds" and "lunge (Shadowstep dash + Ambush radius) < the
  Option C distance" from the assets. **Re-run it** if any of these change: `WindowSeconds`, `ArmSeconds`, any `MoveSpeed`,
  `Shadowstep` multiplier or duration, `Ambush` `StunRadius`, `_baseCornerDistance`, `BaseInsetFraction`, pile insets, or the
  pick's normalisation distance.
- A speed bonus while hunting (considered and rejected: a Speedy could no longer kite, so it would make the hunt one-sided)
  would need that Oracle solved before any number is chosen.

## 6. Open questions for Maestro

1. **Option B:** OK to turn the arm into a gameplay beat (stuns must start after 1.0 s), after it was explicitly "a replication
   floor, not a gameplay beat"?
2. **Ambush refresh on the mark, or the data-only `Cooldown` 8?**
3. **Normalisation distance** for the weighting: settled in code (`55f24947`) as the opposite base-to-base distance (61.7 m,
   `PlayerBase.BaseSpan()`, read at runtime), the same one this proposal's tables use. Change only if Maestro prefers adjacent.
4. **Hoarder / Bulwark near-immunity:** acceptable as the "pay with movement" counter, or should the shell stop covering the
   whole kill span (for example `TurtleMode.asset` `Duration` 3 -> 2)? Recommend: test first.
5. **Bully** has the thinnest answer to a hunt. Accept for this test?
6. **Shadowstep with Underdog Surge** (x1.4, about 12.8 m) still open from v3: under a 4 s window a surging Assassin's lunge is
   about 16.4 m, so more of the map is point-blank.
7. **Smoke Roost as the getaway tool:** keep for this test and watch, or replace now?

### Notes for senior-dev (Option B)

- Stun-began-after-arm: the Assassin's authority can watch the target's replicated `CurrentControlState` turn to `Stunned` after
  `ArmTimer` expires (a stun already running at the arm does not qualify). The victim-side receiver checks
  (`ExecuteRules`, stun evidence latency + 0.5 s) need the same rule from the victim's own stun start. No new `[Networked]`
  field expected, but every peer must run one SHA (the receivers judge differently).
- A re-stun while still stunned would not show as a transition: decide whether a fresh Ambush after the arm on a bird already
  stunned before it qualifies (suggest yes, via the stun timer being extended).
- `BotTactics.NextMarkStep`: a Spoiler bot must hold Ambush until the arm.
- Ambush refresh: on the Mark press only, never on the Kill press.

## 7. Numbers not read from an asset or the Oracle

- Path factor 1.10 over straight lines (walls, pinwheel arms and scatter not modelled; the Oracle notes detours up to 1.38x on
  one leg). All distances in section 1 are straight-line times 1.10.
- The rival occupancy model (pile shares, half at a pile, half in transit) and the Assassin positions (centre, roaming, home).
  These are assumptions, not measurements; the matrix logs could replace them.
- The 0.4 s reaction allowance in Option B's 1.0 s arm.
- The pursuit figures (20-25% escape by running, 99% at 7 s): crude open-arena pursuit, no cover, no player skill.
- The movement cost of shelling (15 m Hoarder, 30 m Bulwark) is speed x time, not measured.
- Feint's distance: `SidestepImpulse` 11 has no confirmed unit, so not converted to metres.
- Whether a stunned bird can press Turtle's lock is inferred from `ControlRules.CanCast`, not tested.
