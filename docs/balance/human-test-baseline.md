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

**Update 2026-11-05, later (branch `simplify-abilities`, code through `e358a1c0`, not merged):** Maestro ruled four more changes. (1) The Warrior is the
all-rounder: Bully is the cooldown spammer (every move except Peck x0.75; kit Peck, Steal, Stun, Jump), Relentless has tougher and stronger crowd control
(incoming stun / getting stuck / timed slow x0.75, outgoing stun and slow x1.3; kit Peck, Steal, Slow, Speed). (2) Shield is a 1 s slow-immune walk then an
automatic 2 s egg. (3) Quick Drop is retired: Featherfoot banks 10% faster permanently (kit Peck, Speed, Slow, Steal), 10 active abilities. (4) Death Mark's
kill range is 6.2 m. Balance Oracle (all four seats): Bully 34.98 s / 3 trips, Relentless 34.98 s / 3, Slippery 30.02 s / 4, Featherfoot 29.62 s / 4 (x1.1 banking, on
the Speedy target 30 s; first shipped at x1.5 = 28.54 s, tuned down by Maestro), Bulwark 30.05 s / 2, Hoarder 25.82 s / 1 (declared). Rows below that still describe
the old Shield (walk 3 s plus a lock) or Quick Drop are rewritten; nothing else moved. Build/scenario: dev-tools build 2026-11-05 17:00, PC solo scenario (Thief vs Featherfoot, Relentless, Bulwark) PASS.

**Update 2026-11-05, latest (branch `simplify-abilities`, not merged):** Maestro ruled the cooldowns too long for a 45 s match and set three bands
(low 1-3 s, medium 4-6 s, high 7-8 s): Jump 2, Steal 4, Speed 4 (and 1.3x, was 1.5x), Hide 4.5 (and 2 s, was 4 s), Shove 5, Slow 6, Stun 7,
Shield 8, Death Mark 4 after a miss and 8 after a kill (was 5 / 10). Kits: Featherfoot Shove for Steal (Speedys do not steal; its kit now equals
Slippery's), Thief Slow for Shove, Spoiler Hide for Slow. Slippery no longer shortens stuns or roots: ability slows on it are 40% weaker and
end 40% sooner (a zone or the Slow aura slows it only during the first 60% of the zone's life). Sneaky (Assassin) speed 12.15 -> 11.1
(SCT-exempt). The tables below are rewritten to these numbers.

This is a starting point to iterate, not a final roster. Every value below is data: kits on the passive assets (or the Balance
Editor's kits panel), rule numbers on `MatchConfig.asset`, `MarkKill.asset`, `WingSlam.asset` (Stun), `Snatch.asset` (Steal), `CluckShock.asset` (Shove), `FeatherAura.asset` (Slow), `TurtleMode.asset` (Shield) and `Shadowstep.asset` (Jump).
`MatchConfigSO.LoadoutMode = Free` brings back free picking; `MatchConfigSO.TargetingMode = Shaped` brings back the cones (Steal 140 deg arc, Stun 120 deg cone). Numbers here were read from the assets on 2026-11-05.

## The eight kits

The class shows as **Sneaky**; in code it stays `ChickenClass.Assassin`.

| Class | Specialization | Slot 1 | Slot 2 | Slot 3 | Slot 4 |
|---|---|---|---|---|---|
| Warrior | Bully | Peck | Steal | Stun | Jump |
| Warrior | Relentless | Peck | Steal | Slow | Speed |
| Speedy | Featherfoot | Peck | Speed | Slow | Shove |
| Speedy | Slippery | Peck | Speed | Slow | Shove |
| Fatty | Bulwark | Peck | Stun | Shove | Shield |
| Fatty | Hoarder | Peck | Shield | Slow | Steal |
| Sneaky | Assassin (Spoiler) | Death Mark | Stun | Jump | Hide |
| Sneaky | Thief | Steal | Hide | Jump | Slow |

## The 10 abilities

All reach figures are a radius around the caster. Cooldowns are the asset values; Bully multiplies every move except Peck by 0.75
(Steal 3 s, Stun 5.25 s, Jump 1.5 s). Relentless leaves cooldowns alone: its stuns and slows last 30% longer (Stun 1.3 -> 1.69 s, the Slow aura
3 -> 3.9 s, cooldown 6 s), and stuns, getting stuck and timed slows on it end 25% sooner (a Relentless stun on Slippery: the full 1.69 s, Slippery
no longer shortens stuns; roots and placed zones are not lengthened). Slippery: ability slows on it are 40% weaker (the 55% aura holds it at 73%)
and end 40% sooner (timed slows x0.6; a zone or aura slows it only during the first 60% of the zone's life: 1.8 s of a 3 s aura, 2.34 s of a
Relentless 3.9 s aura); stuns, getting stuck, pile and bump slows land in full.

| Ability (short) | Asset | Cooldown | Numbers |
|---|---|---|---|
| Steal (ROB) | `Snatch.asset` | 4 s | robs 5 food from every rival carrier within 5.16 m, no knockback (Thief x1.6) |
| Stun (STUN) | `WingSlam.asset` | 7 s | every rival within 5.4 m, 1.3 s (Slippery too; 0.845 s on Bulwark, 0.975 s on Relentless) |
| Shove (PUSH) | `CluckShock.asset` | 5 s | every rival within 5.4 m, knockback force 16.2 |
| Slow (SLOW) | `FeatherAura.asset` | 6 s | aura 6.48 m, 55% speed while inside, 3 s (Slippery: 73%, first 1.8 s only) |
| Speed (FAST) | `SpeedBurst.asset` | 4 s | 1.3x for 1.5 s |
| Jump (JUMP) | `Shadowstep.asset` | 2 s | 3x for 0.18 s (5.99 m for the Sneaky at 11.1, 5.47 m for the Warrior), over walls; not Speedy |
| Shield (SHLD) | `TurtleMode.asset` | 8 s | walk 1 s at 0.5x (immune to ability slows only: timed slow, zone, Slow aura; stun, shove, theft and execution still land), then an automatic egg for 2 s (immobile; immune to stun, slow, root and shove; steal-proof). Press again ends it; casting another move ends the egg only |
| Hide (HIDE) | `Invisibility.asset` | 4.5 s | ghostly outline 2 s, opacity 0.2; breaks on the next move |
| Peck | `Peck.asset` | 0.8 s | per-class amount and cadence (SCT axiom) |
| Death Mark (EXEC) | `MarkKill.asset` | 4 s after a miss, 8 s after a kill | see the rule numbers below; kill range 6.2 m (was 8 m); its stun comes from Stun (1.3 s) |

Free-mode pools: Warrior, Fatty and Sneaky pick from the 7 everyone-abilities plus Jump; Speedy from the 7 only (Quick Drop is retired). Peck and Death Mark are pre-equip-only.

## Rule numbers

| Rule | Value | Where |
|---|---|---|
| Targeting | `RangeOnly` (default): Steal, Stun, Shove, Slow hit every rival within the radius by planar distance, no cone or forward offset; reach +20% over the old base. `Shaped` restores the old shapes | `MatchConfig.asset` `TargetingMode` 0, `AbilityTargeting.Mode` |
| Death Mark | random rival anywhere, **weighted toward birds far from their own base and, softly, far from the Assassin** (score `roll * w * w * a`; w = distance to own base over the base span 61.7 m, clamped 0..1; a = 0.25 + 0.75 * clamp01(distance to the Assassin / 20.6 m), so a rival next to him keeps a quarter weight; uniform if every score is 0); never the last mark unless the only rival; no isolation | `MarkPickRules`, `AssassinExecute` |
| Kill range | 6.2 m (was 8 m; Stun 5.4 m + 6.2 m = 11.6 m, the same total as Ambush 3.6 m + 8 m), target stunned now and not shelled, no wall between | `MarkKill.asset` |
| Mark window | **4 s** (was 7, before that 12); **arm 3.0 s** (was 0.6), so a Kill is only possible in the last **1.0 s**; the stun must be active when the Kill lands (one begun before the arm and still running counts) | `MarkKill.asset` |
| Arm check | the victim's authority times the arm itself (verdict `NotArmed`, allowance 0.5 s) and checks its own stun (stunned on arrival or within one capped network latency, verdict `NotStunned`) | `AssassinExecute` |
| Stealing from an Assassin | the bounty bag counts as carried food for every steal (beak first, then bag) | `StealRules.Stealable`, `SplitDrain` |
| Death Mark at GO | **4 s cooldown** each round, PLAY AGAIN included | `MarkKill.asset` `RoundStartCooldownSeconds` |
| Cooldown after a failed mark | **4 s** (was 5) | `MarkKill.asset` |
| Cooldown after a kill | **8 s** (was 10, before that 15): at most 4 kills per 45 s round (press 4, kill 7.3, press 15.3, kill 18.6, press 26.6, kill 29.9, press 37.9, kill 41.2); any miss costs 8 s | `MarkKill.asset` |
| Stun | 1.3 s (Slippery too; 0.845 s on Bulwark), radius 5.4 m, **cooldown 7 s** (longer than the 4 s window, so one stun per hunt, ready for the next; no reset on mark). Replaces Ambush (1.0 s, 3.6 m) as the Spoiler's stun | `WingSlam.asset` |
| Executed bird | **out 3 s, then respawns at its own base with no cargo**; cooldowns keep running | `MarkKill.asset` `RemovalSeconds` |
| Shell | a steal-proof bird (Shield egg) **cannot be executed** or stunned; the Shield walk can be robbed, stunned and executed | `ExecuteRules.CanBeExecuted` |
| Shield | walk 1 s at 0.5x (**immune to ability slows only**, zone slows and the Slow aura included; stun, root, shove, theft and execution still land), then egg 2 s automatically (immobile, **immune to stun, slow, root and shove**, steal-proof); press again ends the whole Shield; another cast ends only the egg; auto-Peck does not fire during a live Shield; cooldown 8 s from the cast | `TurtleMode.asset` |
| Jump (Shadowstep) | 3x speed for **0.18 s** (dash 5.99 m at the Sneaky's 11.1), cooldown **2 s**; not for Speedy | `Shadowstep.asset` |
| Base wall | 2.5 m round each rival's claimed base; bodies and abilities | `MatchConfig.asset` |
| Match | 40 food or 45 s | `MatchConfig.asset` |

Map reference for the 4 s mark with a 3 s arm (computed from the asset and class numbers, straight lines, not measured in the scene):
Sneaky speed 11.1 m/s (`Assassin.asset`, 12.15 until 2026-11-05). The Kill is only possible in the last 1.0 s, so the Assassin must be in position at the
arm: walking 11.1 x 3.0 = 33.3 m gets him there (44.4 m in the whole 4 s, of which 11.1 m is inside the kill span). Each Shadowstep
(3x for 0.18 s, cooldown 2 s so two per mark, at 0 s and 2 s) adds 2 x 11.1 x 0.18 = 4.0 m over walking: 41.3 m by the arm, 52.4 m in 4 s. Stun's
5.4 m stun reach plus the 6.2 m kill range is 11.6 m, the same total as Ambush's 3.6 m plus the old 8 m (the walk-home and reach-share percentages below were computed with Ambush's 1.0 s / 3.6 m and are not recomputed). Bases stand 43.6 m apart on adjacent corners and 61.7 m on opposite ones:
walking alone reaches an adjacent base in 3.9 s (43.6 / 11.1), so against a bird at home the Assassin is in position only at the
very end, and an opposite base (5.6 s walking) is out of reach. The walk-home and reach-share percentages that follow were computed at 12.15 m/s and are not recomputed. The pick favours birds far from home, so the hunt starts far away. The
victim has the same 3 s: a bird within about 21-29 m of home (class and stun length dependent) that walks home is safe, about 29-36% of
marks. Marks that start inside the Ambush-era 10.2 m reach are 27% with the Assassin camping the centre and 10% roaming (were 40% and 15%
before the distance-to-Assassin factor).

**Jump (Shadowstep) with Underdog Surge.** Resolved by the 0.18 s: with Underdog Surge (x1.4) the dash is 8.39 m at 11.1 (9.19 m at the old 12.15), under the 9.85 m gap
between two pinwheel arms, so it cannot cross two arms even with Underdog. It can still cross an arm plus a food pile (7.66 m).

## What to watch for

New for 2026-11-05 (simplified abilities):

- **Range-only feel:** with no cone, does the 5.4 m circle around you read as "everyone near me"? Do players still try to aim? Does the telegraph (a circle with every rival inside marked) match exactly who gets hit? Does anyone feel hit "from behind" or from the side and find it unfair, or is the circle easier for kids?
- **Shield phases:** the 1 s walk refuses slows only (a stun still lands, theft and execution do not); the 2 s egg that follows on its own refuses everything and cannot move. Is one press and 3 s total understood? Does casting something else to cancel the egg feel right (a manual Peck counts as a cast)? Do rivals understand why nothing lands (the grey no-effect mark and SAFE)? Does a Slow aimed at a walking Shield look like a hit though it does nothing?
- **Spoiler stun -> kill:** Death Mark now stuns with the generic Stun (1.3 s, 5.4 m, 7 s cooldown). Is it timed into the last 1.0 s of the 4 s mark, does a Stun landed early still count at the kill, and does the Spoiler land kills more or less often than with Ambush? Stun reach 5.4 m plus the 6.2 m kill range is 11.6 m, the same as the Ambush era; watch whether the shorter kill range (was 8 m) makes the final approach feel tighter.
- **One ability per job:** can a child say what each of the 11 does from its name and short label (ROB, STUN, PUSH, SLOW, FAST, JUMP, SHLD, HIDE)? Is Steal on five of eight kits too samey?
- **Steal without knockback:** Steal robs 5 from every carrier in 5.16 m and does not push. Do Thieves and Hoarders feel safe afterwards, and does a Bully (Steal every 3.75 s) still outpace everyone?
- **Slow aura:** 6.48 m, 55%, 3 s. Is the aura (a zone around you) understood, or do players expect a one-shot slow?
- **Shorter cooldowns (2026-11-05):** with Jump every 2 s, Steal and Speed every 4 s and Stun every 7 s, does the 45 s match feel busier in a good way, or does a Bully (Jump 1.5 s, Steal 3 s) spam too much? Is Hide (2 s, every 4.5 s) still worth a slot?
- **Slippery vs Featherfoot:** both Speedys now carry Peck, Speed, Slow, Shove. Can kids tell them apart (Slippery shrugs off ability slows, Featherfoot ignores pile and bump slows and banks a little faster)? Does Slippery feel stunnable enough now that stuns land in full?
- **Sneaky speed 11.1:** does the Assassin still reach a mark in time, and can a Speedy now outrun him?
- **Featherfoot banking speed:** deposits are 10% faster for good (Oracle 29.62 s, on the Speedy target 30 s). Is the bonus noticeable at all, or does Featherfoot read only as "piles and bumps never slow me"? If it feels like nothing, say so: a bigger bonus needs a declared exception or a cost elsewhere.
- **Relentless longer crowd control:** do your stuns (1.69 s) and Slow aura (3.9 s) feel stronger, and do you shrug off rivals' stuns and timed slows (x0.75)? Is it too strong against Slippery (its stuns now land the full 1.69 s)?
- **Bully cooldowns:** with every move except Peck recharging 25% faster, does the Warrior feel like the cooldown spammer, and does it stay an all-rounder rather than a thief?
- **Kits at a glance:** Bully (Stun, Jump) and Relentless (Slow, Speed) differ by crowd control and mobility; Bulwark/Hoarder by Stun+Shove vs Slow+Steal. Do these pairs play differently?

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
- **Shield, walk then egg:** the egg starts by itself. Do players understand they stopped, and is pressing again (or casting) to leave early ever used?
- **Is home too safe?** Nothing reaches a bird at home. Does the base become a place to wait out marks, and at what cost in food?
- **Does the wall read?** Do players understand why a target is grey with a lock, and why they stopped?
- **Hold to aim:** walking while aiming: does a held move still feel deliberate, or are casts sloppier?
- **Thief income:** with no Peck, Steal + Hide + Jump + Slow, does it get food at all against careful players?
- **Hoarder and Bulwark:** both are slow and hold ground. Do they play differently?
- **Bots:** every class now appears. Do Warrior and Spoiler bots look alive, and does a bot ever stand idle? Does a Hoarder bot ever lock?
- **GEAR UP:** does a read-only kit page explain the specialization, or should it merge into PICK YOUR BIRD?
- **Audio:** the four Death Mark stings are placeholders (existing UI sounds). Note which ones are confusing, not whether they sound good.
