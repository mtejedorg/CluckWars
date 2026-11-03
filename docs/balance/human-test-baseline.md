# Human test baseline: fixed kits

Baseline v3 (2026-11-03): code at `2399f89b` on branch `balance` (full gate run on it); the commit adding this line is docs
only and is the one to tag. v2 was `82d107bc`, v1 `ae35c5f1`.

This is a starting point to iterate, not a final roster. Every value below is data: kits on the passive assets (or the Balance
Editor's kits panel), rule numbers on `MatchConfig.asset`, `MarkKill.asset`, `TurtleMode.asset` and `Shadowstep.asset`.
`MatchConfigSO.LoadoutMode = Free` brings back free picking. Numbers here were read from the assets on 2026-11-03.

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
| Death Mark | random rival anywhere, never the last mark unless the only rival; no isolation | `AssassinExecute` |
| Kill range | 8 m, target stunned now and not shelled, no wall between | `MarkKill.asset` |
| Mark window | **7 s** (was 12); 0.6 s before a Kill can qualify (replication floor) | `MarkKill.asset` |
| Death Mark at GO | **4 s cooldown** each round, PLAY AGAIN included | `MarkKill.asset` `RoundStartCooldownSeconds` |
| Cooldown after a failed mark | 5 s | `MarkKill.asset` |
| Cooldown after a kill | 15 s | `MarkKill.asset` |
| Executed bird | **out 3 s, then respawns at its own base with no cargo**; cooldowns keep running | `MarkKill.asset` `RemovalSeconds` |
| Shell | a steal-proof bird (Turtle walk or lock) **cannot be executed** | `ExecuteRules.CanBeExecuted` |
| Turtle Mode | walk 3 s at 0.5x (steal-proof, still stunnable); press again: lock up to 2 s (immobile, control-immune); cooldown 10 s from the first press | `TurtleMode.asset` |
| Shadowstep | 3x speed for **0.25 s** (about 9.1 m), cooldown **4 s** (were 0.4 s / 6 s) | `Shadowstep.asset` |
| Sneaky Steal, same bird | 10 s | `SneakySteal.asset` |
| Base wall | 2.5 m round each rival's claimed base; bodies and abilities | `MatchConfig.asset` |
| Match | 40 food or 45 s | `MatchConfig.asset` |

Map reference for the 7 s mark (computed from the map generator, not measured in the scene): Sneaky speed 12.15 m/s. Opposite
base by path about 60 m: 4.9 to 5.0 s walking, 4.1 to 4.2 s with Shadowstep used on cooldown. A random target anywhere from a
corner: 3.0 s on average, 4.4 s at the 90th percentile, 5.6 s at worst (4.6 s with Shadowstep). That is against a target that
stands still: a fleeing bird at the same speed is never closed on by walking.

## Open decisions

**Featherfoot with Quick Drop farms 28.35 s / 4 trips (all four seats), against the Speedy target of 30 s +-1.5 s.** That
is 0.15 s under the band, and two Quick Drops is the most the 12 s cooldown fits in a run, so it is the kit's best case. Nothing
was tuned. Maestro's call, 2026-10-31: keep it for this human test and decide from the results. The options after the test: accept and declare it, shorten the burst, or swap the slot.

**Free-mode pool floor.** With Egg Shell gone, Warrior and Speedy have 6 legal abilities each; the design test
`EveryClass_HasEnoughLegalAbilities` was relaxed from free picks + 4 to + 3. Fixed kits are unaffected.

**Shadowstep with Underdog Surge.** 0.25 s cannot cross two pinwheel arms (closest pair 9.85 m apart, dash 9.1 m), but
Underdog Surge (x1.4) makes it about 12.8 m, which can. It can also still cross an arm plus a food pile (7.66 m). Options: 0.18 s
(two arms impossible even with Underdog), or Underdog does not multiply Shadowstep.

## What to watch for

- **Is the execute real now?** Too strong, or too hard to land in a stun of about 1 s, in 7 s?
- **Is 7 s enough?** The whole map is crossable in about 5 s, but only against a bird that stands still. Does the Assassin ever finish a hunt on a target that runs?
- **The draw:** does the 0.6 s sweep down the standings read as "the game picked someone", or is it noise?
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
- **Shadowstep 0.25 s / 4 s:** still a real escape and chase tool, or now too short to matter?
- **Sneaky Steal:** does 10 s per bird push the Thief to new victims, or just idle him in a duel?
- **Hold to aim:** walking while aiming: does a held move still feel deliberate, or are casts sloppier?
- **Featherfoot against Slippery:** with Quick Drop in the kit, is one Speedy simply better?
- **Bully with Ruffle:** is 1.3x for 2 s enough to catch anyone, or is the slot dead?
- **Thief income:** with no Peck and no stun, does it get food at all against careful players?
- **Hoarder and Bulwark:** both are slow and hold ground. Do they play differently?
- **Bots:** every class now appears. Do Warrior and Spoiler bots look alive, and does a bot ever stand idle? Does a Hauler bot ever lock?
- **GEAR UP:** does a read-only kit page explain the specialization, or should it merge into PICK YOUR BIRD?
- **Audio:** the four Death Mark stings are placeholders (existing UI sounds). Note which ones are confusing, not whether they sound good.
