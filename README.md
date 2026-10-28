# 🐔 Cluck Wars

A 4-player free-for-all arena game. Collect food, bank it at your base, steal from your rivals. Matches last 45 seconds and nobody has hit points.

**Status:** In development. The first human playtest is the next step.

**Site:** <https://mtejedorg.github.io/CluckWars/> (mechanics and tech pages, built from the docs below)

---

## Quick Links

- **[Game Design Document (GDD v0.4)](./docs/GDD.md)** — control and steal model, Solo Clear Time axiom, tiered map, class specializations, abilities. This is the authority when documents disagree.
- **[Technical Design Document (TDD v0.1)](./docs/TDD.md)** — Fusion 2, Zenject, UGS, architecture. Written in May 2026; where it disagrees with the GDD (hit points, tick rate), the GDD wins.
- **[Art Direction](./docs/ART.md)** — visual style, characters, UI, colors
- **[Development Roadmap](./docs/ROADMAP.md)** — milestones from bootstrap to demo

---

## Game Overview

### The Pitch
Four chickens, one farm, forty-five seconds. Collect food, bank it at your base, and rob everyone else blind before the clock runs out. No health bars, no grinding a rival down: every fight is about taking their food and buying time.

### Core Loop
1. Pick a class, a specialization and abilities
2. Spawn at your base corner
3. Farm the tiered food piles (stand on a pile and Peck)
4. Carry the food home and deposit it at your base
5. Steal from loaded rivals, control them, defend your haul
6. First to bank **40 food** wins. If nobody does, the most food banked at **45 seconds** wins. A tie at the top that KOs do not settle crowns every tied bird.

### The map
80 food in total (twice the win target): four 5-food piles at the doorsteps, four 10-food piles at the contested edges, one 20-food pile in the centre. Piles do not respawn. Eight radial walls with one opening each make a safe lap and a risky lap.

### The last 10 seconds
Once per round, with 10 seconds left, one of four events fires: Golden Pile, Underdog Surge, Bounty on the Leader or Restock.

### Platforms
- **Windows** (primary dev target)
- **Android** (mobile target, also a valid host)
- **iOS** (after the demo)

### Rendering
- **2.5D** — real 3D meshes, orthographic isometric camera
- **Pipeline** — Universal Render Pipeline (URP)
- **Characters** — chunky, exaggerated proportions
- **Map** — dark, desaturated tones; characters pop with bright, warm colors

---

## Character Classes

Every stat is solved against a Solo Clear Time target: a naked chicken, alone on the map, banks the win target in a fixed time over a fixed number of base trips. A pure-C# simulator, the Balance Oracle, checks it.

| Class | Cargo | Solo Clear Time | Trips | Role |
|---|---|---|---|---|
| **Fatty** | 35 | 30 s | 2 | Payload: one huge sweep, barely returns |
| **Speedy** | 10 | 30 s | 4 | Throughput: many small loads, roams wide |
| **Warrior** | 14 | 35 s | 3 | The ability class: mid stats, leans on the kit |
| **Assassin** | 10 | 40 s | 4 | Denial: cannot forage, robs and executes |

### Specializations
Each class offers two, chosen once at character select. A specialization grants a passive and, for five of the eight, a signature ability.

| Class | Specializations |
|---|---|
| **Warrior** | The Brute (steals 40% more; signature Scrap), The Relentless (moves recharge 25% faster; signature Headbutt) |
| **Speedy** | The Agile (stuns and timed slows end 40% sooner), The Anxious (immune to pile and collision slows) |
| **Fatty** | The Hauler (carries at least a full win's worth), The Boulder (knockback cut by 75%; signature Ground Quake) |
| **Assassin** | The Reaper (banks a bonus if the clock expires with no winner; signature Death Mark), The Burglar (steals 1.6× more; signature Sneaky Steal) |

Three signatures (Agile, Anxious, Hauler) are not assigned yet.

---

## Abilities

29 abilities. Two are shared by every class and the rest belong to one class each. Every player has four active slots. Moves fire on release, with an instant preview while the button is held.

- **Shared (2):** Peck, Egg Shell
- **Warrior (6):** Cluck Shock, Dive Bomb, Headbutt, Ruffle, Scrap, Wing Slam
- **Speedy (6):** Dust Kick, Feather Aura, Feather Trap, Feint, Quick Drop, Speed Burst
- **Fatty (7):** Belly Flop, Ground Quake, Immovable, Roll & Push, Sticky Egg, Spine Coat, Turtle Mode
- **Assassin (8):** Ambush, Doppelganger, Invisibility, Death Mark, Shadowstep, Smoke Roost, Snatch, Sneaky Steal

**Steal** is capped three ways: `min(steal value, free space, defender cargo)`. **Control** is a ladder: Slowed, Stuck, Stunned. **Death Mark** is the Assassin's execute, one button pressed twice: mark an isolated rival, wait for a stun, press again to take everything they carry.

Full tables, cooldown tiers and the exact values are on the [mechanics page](https://mtejedorg.github.io/CluckWars/mechanics.html) and in [the GDD](./docs/GDD.md).

---

## Tech Stack

- **Engine:** Unity 6000.3 LTS (Unity 6)
- **Render Pipeline:** Universal Render Pipeline (URP)
- **Networking:** Photon Fusion 2, Shared Mode now, Server Mode after funding
- **DI Framework:** Zenject (Extenject)
- **Backend:** Unity Gaming Services behind `IUGSService`, with a null implementation in the demo
- **Audio:** Unity Audio behind `IAudioService`
- **Architecture:** MonoBehaviour (no ECS/DOTS)

Networking, UGS, audio, input and logging all sit behind interfaces. See the [tech page](https://mtejedorg.github.io/CluckWars/tech.html).

---

## Project Structure

```
/Assets
├── _Game/
│   ├── Scripts/
│   │   ├── Networking/
│   │   ├── Gameplay/
│   │   ├── Abilities/
│   │   ├── Input/
│   │   ├── Audio/
│   │   ├── Services/
│   │   ├── Visuals/
│   │   ├── Installers/
│   │   └── UI/
│   ├── Data/
│   ├── Prefabs/
│   ├── Scenes/
│   └── Art/
├── Plugins/
└── Tests/
```

---

## Where it stands

**Shipped (GDD §13):** Balance Oracle, tiered 80-food map with walls, control and steal combat, the 29-ability pool, ability input rework (instant preview, fizzle, cancel, gamepad), steal-proof state, ties at the top.

**Known gaps:** three specialization signatures are unassigned, the endgame rule beyond the last-10-seconds event is deferred, and the arena is probably too big for four players in 45 seconds. A human playtest is the next step and has to answer the last one.

---

## Team

**Maestro** — Senior Engineer, telco background, Unity/C#/Android expertise, Madrid

---

## License

(TBD)
