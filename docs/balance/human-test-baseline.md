# Human test baseline: fixed kits

Baseline v3 (2026-11-03): code at `2399f89b` on branch `balance` (full gate run on it); the commit adding this line is docs
only and is the one to tag. v2 was `82d107bc`, v1 `ae35c5f1`.

**Update 2026-11-04 (code through `920311d8`):** the Death Mark rows below are rewritten to the shipped assets (3 s arm, kill
cooldown 10 s, Ambush cooldown 8 s, Shadowstep 0.18 s, pick weighted by distance to the Assassin). The kits and the rest are as
of v3. A PC scenario run on a fresh EXE is still pending (the `Builds/` EXE predates these commits).

**Update 2026-11-05 (branch `simplify-abilities`, code through `ab7ee3a1`, not merged):** Maestro ruled the ability set too complex for a
children's game. The registry is now **11 abilities, one per job**, the eight kits were rewritten, and **every targeted ability hits every
rival within its radius by distance only** (range-only targeting). Kits, ability rows and the checklist below are rewritten to the shipped
assets; the Death Mark rows keep their 2026-11-04 numbers except where the new Stun changes them. A PC scenario run on a fresh EXE is still
pending. Retired (out of the registry, assets kept): Scrap, Dive Bomb, Sneaky Steal, Ambush, Belly Flop, Ground Quake, Headbutt,
Roll & Push, Dust Kick, Feather Trap, Sticky Egg, Smoke Roost, Ruffle, Feint, Immovable, Spine Coat, Doppelganger (Egg Shell earlier).

This is a starting point to iterate, not a final roster. Every value below is data: kits on the passive assets (or the Balance
Editor's kits panel), rule numbers on `MatchConfig.asset`, `MarkKill.asset`, `WingSlam.asset` (Stun), `Snatch.asset` (Steal), `CluckShock.asset` (Shove), `FeatherAura.asset` (Slow), `TurtleMode.asset` (Shield) and `Shadowstep.asset` (Jump).
`MatchConfigSO.LoadoutMode = Free` brings back free picking; `MatchConfigSO.TargetingMode = Shaped` brings back the cones (Steal 140 deg arc, Stun 120 deg cone). Numbers here were read from the assets on 2026-11-05.

## The eight kits

The class shows as **Sneaky**; in code it stays `ChickenClass.Assassin`.

| Class | Specialization | Slot 1 | Slot 2 | Slot 3 | Slot 4 |
|---|---|---|---|---|---|
| Warrior | Bully | Peck | Steal | Shove | Speed |
| Warrior | Relentless | Peck | Stun | Shove | Speed |
| Speedy | Featherfoot | Peck | Speed | Slow | Quick Drop |
| Speedy | Slippery | Peck | Speed | Slow | Shove |
| Fatty | Bulwark | Peck | Stun | Shove | Shield |
| Fatty | Hoarder | Peck | Shield | Slow | Steal |
| Sneaky | Assassin (Spoiler) | Death Mark | Stun | Jump | Slow |
| Sneaky | Thief | Steal | Hide | Jump | Shove |

## The 11 abilities

All reach figures are a radius around the caster. Cooldowns are the asset values; Relentless multiplies every move except Peck by 0.75
(Stun 6 s, Shove 5.25 s, Speed 4.5 s).

| Ability (short) | Asset | Cooldown | Numbers |
|---|---|---|---|
| Steal (ROB) | `Snatch.asset` | 5 s | robs 5 food from every rival carrier within 5.16 m, no knockback (Bully x1.4, Thief x1.6) |
| Stun (STUN) | `WingSlam.asset` | 8 s | every rival within 5.4 m, 1.3 s (0.78 s on Slippery, 0.845 s on Bulwark) |
| Shove (PUSH) | `CluckShock.asset` | 7 s | every rival within 5.4 m, knockback force 16.2 |
| Slow (SLOW) | `FeatherAura.asset` | 7 s | aura 6.48 m, 55% speed while inside, 3 s |
| Speed (FAST) | `SpeedBurst.asset` | 6 s | 1.5x for 1.5 s |
| Jump (JUMP) | `Shadowstep.asset` | 4 s | 3x for 0.18 s (6.56 m), over walls; not Speedy |
| Shield (SHLD) | `TurtleMode.asset` | 10 s | walk 3 s at 0.5x; steal-proof, unkillable and immune to stun, slow, root and shove (the Slow aura included); press again: lock up to 2 s |
| Hide (HIDE) | `Invisibility.asset` | 8 s | ghostly outline 4 s, opacity 0.2; breaks on the next move |
| Peck | `Peck.asset` | 0.8 s | per-class amount and cadence (SCT axiom) |
| Quick Drop (DROP) | `QuickDrop.asset` | 12 s | banks 4x faster for 2 s; Speedy only |
| Death Mark (EXEC) | `MarkKill.asset` | 5 s | see the rule numbers below; its stun now comes from Stun (1.3 s) |

Free-mode pools: Warrior, Fatty and Sneaky pick from the 7 everyone-abilities plus Jump; Speedy from the 7 plus Quick Drop. Peck and Death Mark are pre-equip-only.

## Rule numbers

| Rule | Value | Where |
|---|---|---|
| Targeting | `RangeOnly` (default): Steal, Stun, Shove, Slow hit every rival within the radius by planar distance, no cone or forward offset; reach +20% over the old base. `Shaped` restores the old shapes | `MatchConfig.asset` `TargetingMode` 0, `AbilityTargeting.Mode` |
| Death Mark | random rival anywhere, **weighted toward birds far from their own base and, softly, far from the Assassin** (score `roll * w * w * a`; w = distance to own base over the base span 61.7 m, clamped 0..1; a = 0.25 + 0.75 * clamp01(distance to the Assassin / 20.6 m), so a rival next to him keeps a quarter weight; uniform if every score is 0); never the last mark unless the only rival; no isolation | `MarkPickRules`, `AssassinExecute` |
| Kill range | 8 m, target stunned now and not shelled, no wall between | `MarkKill.asset` |
| Mark window | **4 s** (was 7, before that 12); **arm 3.0 s** (was 0.6), so a Kill is only possible in the last **1.0 s**; the stun must be active when the Kill lands (one begun before the arm and still running counts) | `MarkKill.asset` |
| Arm check | the victim's authority times the arm itself (verdict `NotArmed`, allowance 0.5 s) and checks its own stun (stunned on arrival or within one capped network latency, verdict `NotStunned`) | `AssassinExecute` |
| Stealing from an Assassin | the bounty bag counts as carried food for every steal (beak first, then bag) | `StealRules.Stealable`, `SplitDrain` |
| Death Mark at GO | **4 s cooldown** each round, PLAY AGAIN included | `MarkKill.asset` `RoundStartCooldownSeconds` |
| Cooldown after a failed mark | 5 s | `MarkKill.asset` |
| Cooldown after a kill | **10 s** (was 15): at most 3 kills per 45 s round (press 4, kill 7.3, press 17.3, kill 20.6, press 30.6, kill 33.9); any miss costs 9 s | `MarkKill.asset` |
| Stun | 1.3 s (0.78 s on Slippery, 0.845 s on Bulwark), radius 5.4 m, **cooldown 8 s** (longer than the 4 s window, so one stun per hunt, ready for the next; no reset on mark). Replaces Ambush (1.0 s, 3.6 m) as the Spoiler's stun | `WingSlam.asset` |
| Executed bird | **out 3 s, then respawns at its own base with no cargo**; cooldowns keep running | `MarkKill.asset` `RemovalSeconds` |
| Shell | a steal-proof bird (Shield walk or lock) **cannot be executed**, and since 2026-11-05 cannot be stunned either | `ExecuteRules.CanBeExecuted` |
| Shield | walk 3 s at 0.5x (steal-proof, unkillable, **immune to stun, slow, root and shove, zone slows and the Slow aura included**); press again: lock up to 2 s (immobile, same immunity); cooldown 10 s from the first press | `TurtleMode.asset` |
| Jump (Shadowstep) | 3x speed for **0.18 s** (dash 6.56 m), cooldown **4 s**; not for Speedy | `Shadowstep.asset` |
| Base wall | 2.5 m round each rival's claimed base; bodies and abilities | `MatchConfig.asset` |
| Match | 40 food or 45 s | `MatchConfig.asset` |

Map reference for the 4 s mark with a 3 s arm (computed from the asset and class numbers, straight lines, not measured in the scene):
Sneaky speed 12.15 m/s (`Assassin.asset`). The Kill is only possible in the last 1.0 s, so the Assassin must be in position at the
arm: walking 12.15 x 3.0 = 36.5 m gets him there (48.6 m in the whole 4 s, of which 12.15 m is inside the kill span). One Shadowstep
(3x for 0.18 s, cooldown 4 s so one per mark) adds 2 x 12.15 x 0.18 = 4.37 m over walking: 40.9 m by the arm, 53.0 m in 4 s. Stun's
5.4 m reach on top of the 6.56 m dash gives an 11.96 m lunge (10.2 m with Ambush; the walk-home and lunge-share percentages below were computed with Ambush's 1.0 s / 3.6 m and are not recomputed). Bases stand 43.6 m apart on adjacent corners and 61.7 m on opposite ones:
walking alone reaches an adjacent base in 3.6 s (43.6 / 12.15), so against a bird at home the Assassin is in position only at the
very end, and an opposite base (5.1 s walking) is out of reach. The pick favours birds far from home, so the hunt starts far away. The
victim has the same 3 s: a bird within about 21-29 m of home (class and stun length dependent) that walks home is safe, about 29-36% of
marks. Marks that start inside the 10.2 m lunge are 27% with the Assassin camping the centre and 10% roaming (were 40% and 15%
before the distance-to-Assassin factor).

**Jump (Shadowstep) with Underdog Surge.** Resolved by the 0.18 s: with Underdog Surge (x1.4) the dash is 9.19 m, under the 9.85 m gap
between two pinwheel arms, so it cannot cross two arms even with Underdog. It can still cross an arm plus a food pile (7.66 m).

## What to watch for

New for 2026-11-05 (simplified abilities):

- **Range-only feel:** with no cone, does the 5.4 m circle around you read as "everyone near me"? Do players still try to aim? Does the telegraph (a circle with every rival inside marked) match exactly who gets hit? Does anyone feel hit "from behind" or from the side and find it unfair, or is the circle easier for kids?
- **Shield immunity:** a shelled bird cannot be robbed, executed, stunned, slowed (the Slow aura too) or shoved. Does that make Hoarder and Bulwark untouchable for 3 s plus 2 s of lock, or is the 0.5x walk and the 10 s cooldown enough of a price? Do rivals understand why nothing lands (the grey no-effect mark and SAFE)?
- **Spoiler stun -> kill:** Death Mark now stuns with the generic Stun (1.3 s, 5.4 m, 8 s cooldown). Is it timed into the last 1.0 s of the 4 s mark, does a Stun landed early still count at the kill, and does the Spoiler land kills more or less often than with Ambush? With Jump (6.56 m) plus Stun (5.4 m) the lunge is about 12 m.
- **One ability per job:** can a child say what each of the 11 does from its name and short label (ROB, STUN, PUSH, SLOW, FAST, JUMP, SHLD, HIDE)? Are Steal and Stun on three of four kits each too samey?
- **Steal without knockback:** Steal robs 5 from every carrier in 5.16 m and does not push. Do Thieves and Hoarders feel safe afterwards, and do Bully Steals (x1.4) outpace everyone?
- **Slow aura:** 6.48 m, 55%, 3 s. Is the aura (a zone around you) understood, or do players expect a one-shot slow?
- **Quick Drop only on Featherfoot:** is Featherfoot simply better than Slippery (Shove)?
- **Kits at a glance:** Bully/Relentless differ by Steal vs Stun; Bulwark/Hoarder by Stun+Shove vs Slow+Steal. Do these pairs play differently?

Carried from earlier sessions (rewritten where an ability was retired):

- **Is the execute real now?** Too strong, or too hard to land in a stun of 1.3 s, in the last 1.0 s of a 4 s mark?
- **The arm countdown:** is the 3 / 2 / 1 on the hunt marker (Assassin) and "LIVE IN 3 / 2 / 1" on the clock pill (victim) readable? Does the red "LIVE 1s" read as "now it can kill you"? Does the skull on the standings turn red and pulse so an observer notices?
- **1 s to press KILL:** is the last second enough to press KILL (and to time Stun into it), or do players miss it? Does KILL staying dark until the arm confuse the Assassin?
- **Walk home:** with a 3 s arm, a victim who walks home from about 21-29 m is safe. Does that feel like a real escape or like a dead hunt?
- **Rush and doubt:** with 4 s the Assassin must rush and the victim must choose (cooldowns, dodging, running). Does the Assassin ever finish a hunt, and does the victim feel a real choice or only panic? Is 4 s too short to ever land?
- **The weighting:** does the mark feel aimed at whoever strayed far from home, and slightly away from the Assassin (a rival next to him is rarely picked)? Does anyone exploit it (hovering at home to be ignored)? Is a bird at home ever marked (it should only happen when everyone is home)?
- **Stealing from a Thief:** a Thief's beak is as stealable as anyone's, even while Hide fades him. Do Thieves end up robbing each other and Bullies in loops, and does Hide feel like it should protect the loot?
- **Stealing from the Assassin:** after an execute the bounty bag can be stolen. Does a loaded Assassin become prey, and does that discourage executes or just make the Assassin bank sooner?
- **The draw:** does the ~0.6 s sweep down the standings read as "the game picked someone", or is it noise?
- **The hunt marker:** can the Assassin find the target from the one glyph (target colour, distance, ring)? Does it ever sit on a nameplate or the You plate?
- **KILL:** does the big yellow KILL hex get pressed in time, or do players still miss the window? Does the Stun hex lighting up help?
- **The victim:** does the darkness read as "you are marked" without hiding the game? Does the cold violet tell you the Assassin is close, and does the faint red when he is invisible register at all (it is meant to be nearly subliminal)?
- **The skull on the arrow:** does the marked bird know where the danger comes from?
- **Third parties:** does anyone use the skull on the standings to help or deny a kill?
- **Does the mark feel fair?** It reaches you anywhere. Does the marked bird have a real choice (avoid the stun, run, go home, shell up), or does everyone just run home?
- **Respawn at base:** does the kill now read as a kill? Is 3 s out too long or too short? Does respawning at home help the victim too much (instant safety)?
- **Shield, walk then lock:** do players find the second press? Since the walk is stun-immune, is the lock still worth pressing?
- **Is home too safe?** Nothing reaches a bird at home. Does the base become a place to wait out marks, and at what cost in food?
- **Does the wall read?** Do players understand why a target is grey with a lock, and why they stopped?
- **Hold to aim:** walking while aiming: does a held move still feel deliberate, or are casts sloppier?
- **Featherfoot against Slippery:** with Quick Drop in the kit, is one Speedy simply better?
- **Thief income:** with no Peck, Steal + Hide + Jump + Shove, does it get food at all against careful players?
- **Hoarder and Bulwark:** both are slow and hold ground. Do they play differently?
- **Bots:** every class now appears. Do Warrior and Spoiler bots look alive, and does a bot ever stand idle? Does a Hoarder bot ever lock?
- **GEAR UP:** does a read-only kit page explain the specialization, or should it merge into PICK YOUR BIRD?
- **Audio:** the four Death Mark stings are placeholders (existing UI sounds). Note which ones are confusing, not whether they sound good.
