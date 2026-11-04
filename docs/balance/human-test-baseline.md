# Human test baseline: fixed kits

Baseline v3 (2026-11-03): code at `2399f89b` on branch `balance` (full gate run on it); the commit adding this line is docs
only and is the one to tag. v2 was `82d107bc`, v1 `ae35c5f1`.

**Update 2026-11-04 (code through `920311d8`):** the Death Mark rows below are rewritten to the shipped assets (3 s arm, kill
cooldown 10 s, Ambush cooldown 8 s, Shadowstep 0.18 s, pick weighted by distance to the Assassin). The kits and the rest are as
of v3. A PC scenario run on a fresh EXE is still pending (the `Builds/` EXE predates these commits).

This is a starting point to iterate, not a final roster. Every value below is data: kits on the passive assets (or the Balance
Editor's kits panel), rule numbers on `MatchConfig.asset`, `MarkKill.asset`, `Ambush.asset`, `TurtleMode.asset` and `Shadowstep.asset`.
`MatchConfigSO.LoadoutMode = Free` brings back free picking. Numbers here were read from the assets on 2026-11-03 (Death Mark, Ambush and Shadowstep rows on 2026-11-04).

## The eight kits

The class shows as **Sneaky**; in code it stays `ChickenClass.Assassin`.

| Class | Specialization | Slot 1 | Slot 2 | Slot 3 | Slot 4 |
|---|---|---|---|---|---|
| Warrior | Relentless | Peck | Headbutt | Wing Slam | Cluck Shock |
| Warrior | Bully | Peck | Scrap | Headbutt | Ruffle |
| Speedy | Slippery | Peck | Speed Burst | Dust Kick | Feint |
| Speedy | Featherfoot | Peck | Speed Burst | Feather Aura | Quick Drop |
| Fatty | Hoarder | Peck | Turtle Mode (walk, then lock) | Spine Coat | Root Egg |
| Fatty | Bulwark | Peck | Ground Quake | Belly Flop | Immovable |
| Sneaky | Assassin (Spoiler) | Death Mark | Ambush | Shadowstep | Smoke Roost |
| Sneaky | Thief | Sneaky Steal | Snatch | Invisibility | Doppelganger |

Benched, still in the pool: Dive Bomb, Roll & Push, Feather Trap. **Retired:** Egg Shell (merged into Turtle Mode's lock;
code and asset kept, out of the registry). Free-mode pools: Warrior 6, Speedy 6, Fatty 7, Sneaky 7, no shared ability.

## Rule numbers

| Rule | Value | Where |
|---|---|---|
| Ruffle | 1.3x, 2 s, cooldown 6 s | `Ruffle.asset` |
| Death Mark | random rival anywhere, **weighted toward birds far from their own base and, softly, far from the Assassin** (score `roll * w * w * a`; w = distance to own base over the base span 61.7 m, clamped 0..1; a = 0.25 + 0.75 * clamp01(distance to the Assassin / 20.6 m), so a rival next to him keeps a quarter weight; uniform if every score is 0); never the last mark unless the only rival; no isolation | `MarkPickRules`, `AssassinExecute` |
| Kill range | 8 m, target stunned now and not shelled, no wall between | `MarkKill.asset` |
| Mark window | **4 s** (was 7, before that 12); **arm 3.0 s** (was 0.6), so a Kill is only possible in the last **1.0 s**; the stun must be active when the Kill lands (one begun before the arm and still running counts) | `MarkKill.asset` |
| Arm check | the victim's authority times the arm itself (verdict `NotArmed`, allowance 0.5 s) and checks its own stun (stunned on arrival or within one capped network latency, verdict `NotStunned`) | `AssassinExecute` |
| Stealing from an Assassin | the bounty bag counts as carried food for every steal (beak first, then bag) | `StealRules.Stealable`, `SplitDrain` |
| Death Mark at GO | **4 s cooldown** each round, PLAY AGAIN included | `MarkKill.asset` `RoundStartCooldownSeconds` |
| Cooldown after a failed mark | 5 s | `MarkKill.asset` |
| Cooldown after a kill | **10 s** (was 15): at most 3 kills per 45 s round (press 4, kill 7.3, press 17.3, kill 20.6, press 30.6, kill 33.9); any miss costs 9 s | `MarkKill.asset` |
| Ambush | stun 1.0 s (0.6 s on Slippery, 0.65 s on Bulwark), radius 3.6 m, **cooldown 8 s** (was 10; longer than the 4 s window, so one stun per hunt, ready for the next; no reset on mark) | `Ambush.asset` |
| Executed bird | **out 3 s, then respawns at its own base with no cargo**; cooldowns keep running | `MarkKill.asset` `RemovalSeconds` |
| Shell | a steal-proof bird (Turtle walk or lock) **cannot be executed** | `ExecuteRules.CanBeExecuted` |
| Turtle Mode | walk 3 s at 0.5x (steal-proof, still stunnable); press again: lock up to 2 s (immobile, control-immune); cooldown 10 s from the first press | `TurtleMode.asset` |
| Shadowstep | 3x speed for **0.18 s** (dash 6.56 m), cooldown **4 s** (was 0.25 s / 4 s; before 2026-11-03 0.4 s / 6 s) | `Shadowstep.asset` |
| Sneaky Steal, same bird | 10 s | `SneakySteal.asset` |
| Base wall | 2.5 m round each rival's claimed base; bodies and abilities | `MatchConfig.asset` |
| Match | 40 food or 45 s | `MatchConfig.asset` |

Map reference for the 4 s mark with a 3 s arm (computed from the asset and class numbers, straight lines, not measured in the scene):
Sneaky speed 12.15 m/s (`Assassin.asset`). The Kill is only possible in the last 1.0 s, so the Assassin must be in position at the
arm: walking 12.15 x 3.0 = 36.5 m gets him there (48.6 m in the whole 4 s, of which 12.15 m is inside the kill span). One Shadowstep
(3x for 0.18 s, cooldown 4 s so one per mark) adds 2 x 12.15 x 0.18 = 4.37 m over walking: 40.9 m by the arm, 53.0 m in 4 s. Ambush's
3.6 m reach on top of the 6.56 m dash gives a 10.2 m lunge. Bases stand 43.6 m apart on adjacent corners and 61.7 m on opposite ones:
walking alone reaches an adjacent base in 3.6 s (43.6 / 12.15), so against a bird at home the Assassin is in position only at the
very end, and an opposite base (5.1 s walking) is out of reach. The pick favours birds far from home, so the hunt starts far away. The
victim has the same 3 s: a bird within about 21-29 m of home (class and stun length dependent) that walks home is safe, about 29-36% of
marks. Marks that start inside the 10.2 m lunge are 27% with the Assassin camping the centre and 10% roaming (were 40% and 15%
before the distance-to-Assassin factor).

**Shadowstep with Underdog Surge.** Resolved by the 0.18 s: with Underdog Surge (x1.4) the dash is 9.19 m, under the 9.85 m gap
between two pinwheel arms, so it cannot cross two arms even with Underdog. It can still cross an arm plus a food pile (7.66 m).

## What to watch for

- **Is the execute real now?** Too strong, or too hard to land in a stun of about 1 s, in the last 1.0 s of a 4 s mark?
- **The arm countdown:** is the 3 / 2 / 1 on the hunt marker (Assassin) and "LIVE IN 3 / 2 / 1" on the clock pill (victim) readable? Does the red "LIVE 1s" read as "now it can kill you"? Does the skull on the standings turn red and pulse so an observer notices?
- **1 s to press KILL:** is the last second enough to press KILL (and to time Ambush into it), or do players miss it? Does KILL staying dark until the arm confuse the Assassin?
- **Walk home:** with a 3 s arm, a victim who walks home from about 21-29 m is safe. Does that feel like a real escape or like a dead hunt?
- **Rush and doubt:** with 4 s the Assassin must rush and the victim must choose (cooldowns, dodging, running). Does the Assassin ever finish a hunt, and does the victim feel a real choice or only panic? Is 4 s too short to ever land?
- **The weighting:** does the mark feel aimed at whoever strayed far from home, and slightly away from the Assassin (a rival next to him is rarely picked)? Does anyone exploit it (hovering at home to be ignored)? Is a bird at home ever marked (it should only happen when everyone is home)?
- **Stealing from a Thief:** a Thief's beak is as stealable as anyone's, even while Invisibility fades him. Do Thieves end up robbing each other and Bullies in loops, and does Invisibility feel like it should protect the loot?
- **Stealing from the Assassin:** after an execute the bounty bag can be stolen. Does a loaded Assassin become prey, and does that discourage executes or just make the Assassin bank sooner?
- **The draw:** does the ~0.6 s sweep down the standings read as "the game picked someone", or is it noise?
- **The hunt marker:** can the Assassin find the target from the one glyph (target colour, distance, ring)? Does it ever sit on a nameplate or the You plate?
- **KILL:** does the big yellow KILL hex get pressed in time, or do players still miss the window? Does the Ambush hex lighting up help?
- **The victim:** does the darkness read as "you are marked" without hiding the game? Does the cold violet tell you the Assassin is close, and does the faint red when he is invisible register at all (it is meant to be nearly subliminal)?
- **The skull on the arrow:** does the marked bird know where the danger comes from?
- **Third parties:** does anyone use the skull on the standings to help or deny a kill?
- **Does the mark feel fair?** It reaches you anywhere. Does the marked bird have a real choice (avoid the stun, run, go home, shell up), or does everyone just run home?
- **Respawn at base:** does the kill now read as a kill? Is 3 s out too long or too short? Does respawning at home help the victim too much (instant safety)?
- **Turtle Mode, walk then lock:** do Fatty players find the second press? Is a lock that a stun can deny a fair counter, or does it feel random?
- **Is home too safe?** Nothing reaches a bird at home. Does the base become a place to wait out marks, and at what cost in food?
- **Does the wall read?** Do players understand why a target is grey with a lock, and why they stopped?
- **Shadowstep 0.18 s / 4 s:** still a real escape and chase tool at 6.6 m, or now too short to matter?
- **Ambush 8 s:** is the stun ready for each next hunt, and does the bot (casts only when the stun will still run 0.25 s past the arm) look sensible?
- **Sneaky Steal:** does 10 s per bird push the Thief to new victims, or just idle him in a duel?
- **Hold to aim:** walking while aiming: does a held move still feel deliberate, or are casts sloppier?
- **Featherfoot against Slippery:** with Quick Drop in the kit, is one Speedy simply better?
- **Bully with Ruffle:** is 1.3x for 2 s enough to catch anyone, or is the slot dead?
- **Thief income:** with no Peck and no stun, does it get food at all against careful players?
- **Hoarder and Bulwark:** both are slow and hold ground. Do they play differently?
- **Bots:** every class now appears. Do Warrior and Spoiler bots look alive, and does a bot ever stand idle? Does a Hauler bot ever lock?
- **GEAR UP:** does a read-only kit page explain the specialization, or should it merge into PICK YOUR BIRD?
- **Audio:** the four Death Mark stings are placeholders (existing UI sounds). Note which ones are confusing, not whether they sound good.
