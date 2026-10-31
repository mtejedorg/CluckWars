# Human test baseline: fixed kits

Baseline commit: see `git log --oneline balance -- docs/balance/human-test-baseline.md` (the commit that added this line closes
the baseline work on branch `balance`).

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
| Death Mark reach | 8 m | `MarkKill.asset` |
| Arm | 2 s | `MarkKill.asset` |
| Window | 5 s | `MarkKill.asset` |
| Cooldown after a failed mark | 5 s | `MarkKill.asset` |
| Cooldown after a kill | 15 s | `MarkKill.asset` |
| Isolation | 8 m | `MarkKill.asset` |
| Cold trail | 15 s, off with one real rival | `MarkKill.asset` |
| Home breaks the mark | inside own deposit radius (2.5 m) | `PlayerBase` |
| Base wall | 2.5 m round each rival's claimed base | `MatchConfig.asset` |
| Match | 40 food or 45 s | `MatchConfig.asset` |

## Open decision

**Featherfoot with Quick Drop farms 28.35 s / 4 trips (all four seats), against the Speedy target of 30 s +-1.5 s.** That
is 0.15 s under the band, and two Quick Drops is the most the 12 s cooldown fits in a run, so it is the kit's best case. Nothing
was tuned. Options are Maestro's: accept and declare it, shorten the burst, or swap the slot.

## What to watch for

- **Is the execute real now?** Until this baseline the Kill press was refused by the mark's own cooldown, so nobody has ever
  played the Assassin specialization with a working execute. Watch whether it is too strong, not only whether it works.
- **Does cold trail end the camping?** A Sneaky should have to change target. With three rivals, does he run out of targets?
- **Is home too safe?** Does a marked bird just walk home? Does the mark ever land near a corner?
- **Does the base wall read?** Do players understand why they stopped, or does it feel like a bug? Does anyone feel stuck on it?
- **Doorstep play.** Abilities still reach across the wall: is standing at the rim a strong steal spot, or a camping spot?
- **Featherfoot against Slippery:** with Quick Drop in the kit, is one Speedy simply better?
- **Bully with Ruffle:** is 1.3x for 2 s enough to catch anyone, or is the slot dead?
- **Thief income:** with no Peck and no stun, does it get food at all against careful players?
- **Hoarder and Bulwark:** both are slow and hold ground. Do they play differently?
- **Bots:** every class now appears. Do Warrior and Spoiler bots look alive, and does a bot ever stand idle?
- **GEAR UP:** does a read-only kit page explain the specialization, or should it merge into PICK YOUR BIRD?
