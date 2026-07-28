# 🐔 Cluck Wars

Fast-paced 4-player multiplayer arena game. Collect food, fight rivals, survive.

**Status:** Pre-production (v0.4 control+steal redesign in progress; GDD, TDD, Art Direction live)

---

## Quick Links

- **[Game Design Document (GDD v0.4)](./docs/GDD.md)** — Control+steal model, class strategies via SCT axioms, tiered map, abilities
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
7. First to 40 food units OR most food at 45 seconds wins

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

| Class | Cargo | Speed | Role |
|---|---|---|---|
| **Fatty Chicken** | ⭐⭐⭐⭐⭐ | ⭐⭐ | Bulk carrier, clears contested piles in 2 trips |
| **Speedy Chicken** | ⭐⭐ | ⭐⭐⭐⭐⭐ | Hit-and-run, clears piles in 4 quick trips |
| **Warrior Chicken** | ⭐⭐⭐ | ⭐⭐⭐ | All-rounder, contests piles with control abilities |
| **Assassin Chicken** | ⭐⭐ | ⭐⭐⭐⭐ | Disruptor with Mark/Kill execute; equips 3 abilities (Combo passive) |

---

## Abilities Pool

**Steal** (direct food transfer; clamped by free space & defender cargo):
- **Peck** — AoE steal around self
- **Flying Peck** — Vault-dash steal on first contact
- **Sneaky Steal** — Steal from nearby rival (instant, common)

**Control** (disruption via slowed/rooted/stunned ladder):
- **Cluck Shock** — AoE knockback around self
- **Ambush** — AoE stun around self (Assassin)
- **Wing Slam** — AoE stun with larger radius (Warrior)
- **Shadowstep** — Blink dash (Assassin)

**Defense** (damage reduction / steal-back):
- **Spine Coat** — Steal-back + knockback on contact
- **Turtle Mode** — Reduced move, increased knockback resistance
- **Egg Shell** — Temporary immobility, no contact damage

**Utility** (mobility, perception):
- **Speed Burst** — Greatly increases movement speed
- **Doppelganger** — Create brief decoy copy
- **Invisibility** — Become invisible to other players
- **Feather Trap** — Drop a control zone (Common)

**Signature** (class-exclusive, one-per-chicken):
- **Mark/Kill** — Assassin execute: mark isolated target, two-press sequence for full cargo steal + removal

All abilities balanced by Solo Clear Time axiom — no pay-to-win.

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

## Current Phase (v0.4 Control+Steal)

**Shipped:**
- ✅ Control ladder (Slowed/Rooted/Stunned) replaces HP/damage
- ✅ Steal economy with natural clamping (free space + defender cargo)
- ✅ Tiered 80-food map with pinwheel walls
- ✅ Assassin Mark/Kill execute (full cargo steal + removal)
- ✅ SCT Oracle simulator for axiom-driven balance
- ✅ Class stats solved to match target trip counts
- ✅ Ability categories (Steal/Control/Defense/Utility)

**In Progress:**
- 🔧 Map wall rendering (pinwheel interior geometry)
- 🔧 Bot AI improvement (movement oscillation)
- 🔧 Pile feedback accuracy (visual sync with actual food)

**Next:**
- Passive ability pool redesign (Mighty, Slippery, Immovable, Combo)
- Mobile controls & HUD layout
- LAN stress test (4 players, 45-second match stability)

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
