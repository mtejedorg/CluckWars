# 🐔 Cluck Wars

Fast-paced 4-player multiplayer arena game. Collect food, fight rivals, survive.

**Status:** Pre-production (GDD, TDD, Art Direction v0.1 complete)

---

## Quick Links

- **[Game Design Document (GDD v0.2)](./docs/GDD.md)** — Core mechanics, classes, abilities, win conditions
- **[Technical Design Document (TDD v0.1)](./docs/TDD.md)** — Fusion 2, Zenject, UGS, architecture
- **[Art Direction (v0.1)](./docs/ART.md)** — Visual style, characters, UI, colors
- **[Development Roadmap](./docs/ROADMAP.md)** — Milestones from bootstrap to demo

---

## Game Overview

### The Pitch
Four chickens enter a farm. Only the fattest leaves. Cluck Wars is a fast-paced 4-player free-for-all where players collect food, defend their stash, and sabotage rivals using character classes and short-burst abilities. Easy to learn, deep to master.

### Core Loop
1. Select character class & abilities
2. Spawn at base edge
3. Move to food pile
4. Stand on pile to collect cargo (passive, no button)
5. Return to base to deposit food
6. Fight/steal/disrupt rivals along the way
7. First to 150 food units OR most food at 3 minutes wins

### Platforms
- **Windows** (primary dev target)
- **Android** (mobile primary, also valid LAN host)
- **iOS** (post-demo)

### Rendering
- **2.5D** — Real 3D meshes, orthographic isometric camera
- **Pipeline** — Universal Render Pipeline (URP)
- **Characters** — Chunky, exaggerated proportions (Hei Hei from Moana reference)
- **Map** — Dark, desaturated tones; characters pop with bright, warm colors

---

## Character Classes

| Class | Cargo | Speed | Attack | Role |
|---|---|---|---|---|
| **Fatty Chicken** | ⭐⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐ | Bulk carrier, dominates uncontested piles |
| **Speedy Chicken** | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | Hit-and-run, thrives on chaos |
| **Warrior Chicken** | ⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐ | All-rounder, contests piles well |
| **Assassin Chicken** | ⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | Disruptor, equips 2 abilities |

---

## Abilities Pool

- **Speed Burst** — Greatly increases movement speed (1-2 sec)
- **Egg Shell** — Become invulnerable inside an egg, immobile (1-2 sec)
- **Roll & Trample** — Roll forward, stun chickens in path (1-2 sec)
- **Doppelganger** — Create brief decoy copy (1-2 sec)
- **Invisibility** — Become invisible to other players (1-2 sec)
- **Spine Coat** — Contact deals damage to attacker (1-2 sec)
- **Turtle Mode** — Near-zero speed, greatly increased resistance (1-2 sec)
- **Sneaky Steal** — Steal cargo without fighting, nearby rival (instant)

All abilities balanced by design — no pay-to-win mechanics.

---

## Tech Stack

- **Engine:** Unity 6000.3 LTS (Unity 6)
- **Render Pipeline:** Universal Render Pipeline (URP)
- **Networking:** Photon Fusion 2 (Shared Mode for demo, Server Mode post-funding)
- **DI Framework:** Zenject (Extenject)
- **Backend:** Unity Gaming Services (behind interface, can be muted for demo)
- **Audio:** Unity Audio (FMOD-ready interface)
- **Architecture:** MonoBehaviour (no ECS/DOTS for demo)

---

## Demo Milestone

The demo is complete when:

- ✅ Any device (PC or Android) can host LAN session
- ✅ 4 players connect and play full match (5-10 min)
- ✅ All 4 classes playable with at least 2 abilities each
- ✅ Full loop: spawn → collect → fight → win condition → end screen
- ✅ Windows & Android builds stable at 30 fps (Android target)
- ✅ Session join by ID (no UGS dependency in demo)

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

## Next Steps

1. **Bootstrap** — Zenject installer, networking interface, game bootstrap
2. **Chicken prototype** — Walking, basic animation, cargo collection
3. **Combat** — Attack system, stun on death
4. **Abilities** — Ability system, first 2 abilities
5. **Multiplayer test** — 4 players on LAN

---

## References

- GDD v0.2 (Notion) — Full game design with mechanics, balancing values, ability pool
- TDD v0.1 (Notion) — Architecture, networking model, systems, project structure
- Art Direction v0.1 (Notion) — Visual style, character proportions, UI layout, color palette

---

## Team

**Maestro** — Senior Engineer, telco background, Unity/C#/Android expertise, Madrid

---

## License

(TBD)
