# Human test baseline: fixed kits

Baseline: code at `82d107bc` on branch `balance` (full gate run on it); the commit adding this line is docs only and is the one to tag. v1 was `ae35c5f1`.

This is a starting point to iterate, not a final roster. Every value below is data: kits on the passive assets (or the Balance
Editor's kits panel), rule numbers on `MatchConfig.asset` and `MarkKill.asset`. `MatchConfigSO.LoadoutMode = Free` brings back
free picking. Numbers here were read from the assets on 2026-10-31.

## The eight kits

The class shows as **Sneaky**; in code it stays `ChickenClass.Assassin`.

| Class | Specialization | Slot 1 | Slot 2 | Slot 3 | Slot 4 |
|---|---|---|---|---|---|
| Warrior | Relentless | Peck | Headbutt | Wing Slam | Cluck Shock |
| Warrior | Bully | Peck | Scrap | Headbutt | Ruffle |
| Speedy | Slippery | Peck | Speed Burst | Dust Kick | Feint |
| Speedy | Featherfoot | Peck | Speed Burst | Feather Aura | Quick Drop |
| Fatty | Hoarder | Peck | Turtle Mode | Spine Coat | Root Egg |
| Fatty | Bulwark | Peck | Ground Quake | Belly Flop | Immovable |
| Sneaky | Assassin (Spoiler) | Death Mark | Ambush | Shadowstep | Smoke Roost |
| Sneaky | Thief | Sneaky Steal | Snatch | Invisibility | Doppelganger |

Benched, still in the pool: Dive Bomb, Roll & Push, Feather Trap, Egg Shell.

## Rule numbers

| Rule | Value | Where |
|---|---|---|
| Ruffle | 1.3x, 2 s, cooldown 6 s | `Ruffle.asset` |
| Death Mark | random rival anywhere, never the last mark unless the only rival | `AssassinExecute` |
| Kill range | 8 m, target stunned now, no wall between | `MarkKill.asset` |
| Mark window | 12 s; 0.6 s before a Kill can qualify (replication floor) | `MarkKill.asset` |
| Cooldown after a failed mark | 5 s | `MarkKill.asset` |
| Cooldown after a kill | 15 s | `MarkKill.asset` |
| Sneaky Steal, same bird | 10 s | `SneakySteal.asset` |
| Base wall | 2.5 m round each rival's claimed base; bodies and abilities | `MatchConfig.asset` |
| Match | 40 food or 45 s | `MatchConfig.asset` |

## Open decision

**Featherfoot with Quick Drop farms 28.35 s / 4 trips (all four seats), against the Speedy target of 30 s +-1.5 s.** That
is 0.15 s under the band, and two Quick Drops is the most the 12 s cooldown fits in a run, so it is the kit's best case. Nothing
was tuned. Maestro's call, 2026-10-31: keep it for this human test and decide from the results. The options after the test: accept and declare it, shorten the burst, or swap the slot.

## What to watch for

- **Is the execute real now?** Until this baseline the Kill press was refused by the mark's own cooldown, so nobody has ever played a working execute. Too strong, or too hard to land in a stun of about 1 s?
- **Can the Assassin find the target?** Arrow, chevrons, banner: is any of it noise? Is 12 s enough to cross the map?
- **Does the mark feel fair?** It reaches you anywhere, and you see it. Does the marked bird have a real choice, or does everyone just run home?
- **Is home too safe?** Nothing reaches a bird at home. Does the base become a place to wait out marks, and at what cost in food?
- **Does the wall read?** Do players understand why a target is grey at the rim, and why they stopped?
- **Sneaky Steal:** does 10 s per bird push the Thief to new victims, or just idle him in a duel?
- **Hold to aim:** walking while aiming: does a held move still feel deliberate, or are casts sloppier?
- **Featherfoot against Slippery:** with Quick Drop in the kit, is one Speedy simply better?
- **Bully with Ruffle:** is 1.3x for 2 s enough to catch anyone, or is the slot dead?
- **Thief income:** with no Peck and no stun, does it get food at all against careful players?
- **Hoarder and Bulwark:** both are slow and hold ground. Do they play differently?
- **Bots:** every class now appears. Do Warrior and Spoiler bots look alive, and does a bot ever stand idle?
- **GEAR UP:** does a read-only kit page explain the specialization, or should it merge into PICK YOUR BIRD?
