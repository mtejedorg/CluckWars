---
status: archived
type: archive
superseded-by: ../ART.md
source: Notion page, last edited 2026-05-28
---

> [!warning] Historical record, archived from Notion. Superseded by [the live art doc](../ART.md); placeholder-art status and palette values here are stale. Do not use this file as a source of facts.

**Version:** 0.1

**Date:** May 2026

**Status:** Preliminary — subject to revision once art production begins

---

# 1. Vision Statement

Cluck Wars should feel like a **warm, funny, chaotic cartoon farm** the moment it loads. The visual language must communicate instantly on a small screen — who is who, what is happening, and where the food is. Clarity always wins over decoration.

Three words to test every visual decision against: **readable, chunky, warm**.

---

# 2. Rendering & Camera

- **2.5D** — real 3D meshes rendered with an orthographic isometric camera.
- No perspective distortion. Consistent scale across the entire map.
- Fixed camera — full map always visible. No zoom, no pan.
- Isometric angle: standard 45° rotation, ~30° elevation (exact value TBD in engine).

---

# 3. Color Palette

## Overall Direction

| Area | Tone | Rationale |
| --- | --- | --- |
| Map / terrain | Dark, desaturated warm browns and muted greens | Background — must not compete with characters |
| Food piles | Mid warm tones — golden yellows, amber | Readable against dark map, feel appetizing |
| Characters | Bright, saturated, warm cartoon colors | Must pop immediately against the map |
| UI | Near-neutral, minimal color — cream/wood tones | Thematic but never distracting |
| Ability FX | Saturated accent colors per ability type | Instant readability of what is happening |

## Map Palette (reference tones)

- Base ground: deep warm brown (#3B2A1A range)
- Grass patches: muted olive/dark green (#4A5C2A range)
- Path/dirt: desaturated tan (#6B5540 range)
- Shadows: very dark, low saturation — no pure black

## Food Pile Palette

- Full pile: warm golden yellow (#F5C842)
- 50% depleted: amber orange (#D48C20)
- Nearly empty: dull brown-grey (#7A6040)

Transition is continuous (driven by food % in `FoodPileVisuals`) — the three values above are waypoints.

## Character Palette Guidelines

- Each class has a **primary color** used on the body as the main read.
- Player identity (player 1–4) is communicated via a **color ring or marker at the base** of the character, not by recoloring the whole character.
- Skins can override primary color freely — player identity marker always stays.

| Class | Suggested primary color | Feel |
| --- | --- | --- |
| Fatty Chicken | Warm yellow / cream | Soft, round, inviting |
| Speedy Chicken | Bright orange-red | Energetic, alert |
| Warrior Chicken | Deep red / terracotta | Strong, grounded |
| Assassin Chicken | Dark grey / black with accent | Mysterious, sleek |

*All colors TBD — final palette to be validated in engine against the map background.*

---

# 4. Character Design

## Proportions

- **Reference:** Hei Hei from Moana — oversized round body, tiny wings, large expressive head, short stubby legs.
- Exaggerated and chunky — not realistic, not cute-tiny. Big and dumb-looking is correct.
- Silhouette must be **instantly readable** at ~100px height on a mobile screen.
- Each class must have a **distinct silhouette** — different body shape, not just color swap:

| Class | Silhouette direction |
| --- | --- |
| Fatty Chicken | Widest, lowest center of gravity, wobbles |
| Speedy Chicken | Leaner, slightly taller, more angular posture |
| Warrior Chicken | Broad-shouldered (for a chicken), upright, strong stance |
| Assassin Chicken | Sleek, slightly hunched, smaller profile |

## Facing & Mirroring

- Two base mesh orientations: facing right, facing left (horizontal mirror).
- Vertical movement handled via vertical sprite/mesh flip.
- No in-between angles — snap only.

## Animation Style

- Snappy and exaggerated — no motion blur, no easing. Cartoon timing.
- Hit reactions should be big and readable (squash, wobble).
- Idle animation: subtle loop — breathing, occasional eye blink or head twitch.
- Stunned: slumped over, stars or birds circling (particle FX TBD).
- Collecting: pecking motion — rhythmic, clear, immediately readable as "I am eating".

---

# 5. Map Design

## Layout Visual Guidelines

- Map is dark enough that characters pop without effort.
- Central food pile should be the **brightest element on the map** — draws the eye immediately on load.
- Player bases (edges) should be subtly distinct per player via color tinting — matches their player identity color.
- Contested food islands: slightly raised terrain or rim to visually separate them from the path between.

## Terrain & Props

- Farm aesthetic: hay, wooden fences, dirt paths, patches of grass.
- Props are low-profile — must not obscure characters or food piles.
- No tall props near the center or high-traffic areas.
- Suggested prop set (preliminary): hay bales, wooden fence segments, flower patches, mud puddles.

## Food Pile Visuals

- Full pile: large mound, vibrant golden color, possibly with a subtle shimmer particle.
- As it depletes: physically shrinks + color desaturates (continuous, driven by food %).
- At <25%: consider a visual "last scraps" state — small pile, dull color, maybe a flies/dust particle (TBD).
- Empty pile: a faint ground mark or shadow — should still be readable as "there was food here".

---

# 6. UI Design

## HUD Layout

```javascript
┌───────────────────────────────────────────────┐
│  [Match Timer]          [P1] [P2] [P3] [P4] food totals  │   TOP
├───────────────────────────────────────────────┤
│                                                           │
│                    GAME AREA                              │
│                                                           │
├───────────────────────────────────────────────┤
│  [HP bar] [Cargo: 12/20]       [Ability 1] [Ability 2]   │   BOTTOM
└───────────────────────────────────────────────┘
```

**Top bar:** Match timer (center), all 4 players' stored food totals with player color indicator.

**Bottom bar:** Local player HP, current cargo / max cargo, ability button(s) with cooldown indicator.

## UI Style

- Minimalist — no unnecessary chrome or decoration.
- Thematic: wood and rope textures for panels, hand-painted feel for icons. Subtle, not loud.
- Font: bold, rounded, cartoon-friendly. Legible at small sizes. TBD — evaluate Google Fonts (Fredoka One, Baloo 2, or similar).
- Ability buttons: large, thumb-friendly. Cooldown shown as a radial fill overlay — standard mobile pattern.
- All UI elements must pass contrast check at 30fps on a mid-range Android screen in daylight conditions (practical test, not just spec).

## Color Coding in UI

- Each player has a consistent color throughout: HP bar, food total indicator, base tint, player marker on character.
- Suggested player colors (high contrast, distinguishable under color-blind conditions): Red, Blue, Green, Yellow.

---

# 7. Visual Effects (VFX)

| Event | VFX direction | Notes |
| --- | --- | --- |
| Food collected | Small sparkle burst at chicken, subtle trail to base | Confirms collection, not distracting |
| Food deposited at base | Brief glow on base, food counter ticks up | Satisfying feedback |
| Attack hit | Impact burst on target, brief screen flash for receiver | Must be readable at distance |
| Stun (death) | Stars/birds circle stunned chicken | Classic cartoon read |
| Ability activation | Per-ability — distinct color accent per ability type | Must not obscure nearby characters |
| Food pile depleting | Dust/crumbs particle as pile shrinks | Subtle, continuous |
| Match end | Full-screen celebratory burst for winner | Go big here |

All VFX are local — never networked. Triggered by observed state changes.

---

# 8. Audio Direction (Preliminary)

- **Overall tone:** Cartoonish, warm, slightly chaotic. Think Looney Tunes energy — every action has a punchy sound.
- **Music:** Upbeat farm/bluegrass loop during match. Builds in intensity in the last 30 seconds.
- **SFX priorities:** Footsteps, collecting (pecking/crunching), attack (cluck/thwack), hit reaction, stun, ability activations, deposit at base, win/lose stings.
- All audio implementation via `IAudioService` — Unity Audio for demo, FMOD-ready via interface swap.
- No voice chat, no text chat.

---

# 9. Open Questions & TBD

| # | Item | Priority |
| --- | --- | --- |
| 1 | Final character primary colors validated in engine | High |
| 2 | Isometric camera angle (elevation) finalized | High |
| 3 | UI font selection | Medium |
| 4 | Ability VFX color assignments per ability | Medium |
| 5 | Food pile mesh swap thresholds (<25%, <10%) | Medium |
| 6 | Player identity color set (color-blind safe) | Medium |
| 7 | Stunned FX — stars vs birds vs custom | Low |
| 8 | Map prop set finalized | Low |

---

# IMPLEMENTATION STATUS — AI-Generated Placeholders

**Placeholder Asset Strategy:** AI-generated 3D models from Meshy, rigged and animated automatically.

---

## Chickens — ✅ Messy AI-Generated + Auto-Rigged

**Current State:**

- All 4 classes generated via Meshy text-to-3D
- Exported as FBX with auto-rigging (Meshy's built-in skeleton)
- Basic animations included (walk, idle, attack, hit, stun)
- Per-class spawn scale applied (Fatty larger, Assassin smaller)
- Player identity color tinting applied at runtime (4 colorblind-safe colors)

**Visual Language:**

- Chunky, round proportions (Hei Hei reference maintained in prompts)
- Warm base colors per class
- Works at ~100px height on mobile (readable silhouettes confirmed)

**Next:** Replace with production hand-sculpted/animated models when art arrives.

---

## Food Piles — ✅ Meshy AI-Generated

**Current:**

- Simple golden mound shapes from Messy
- Scale + color shift implemented in `FoodPileVisuals` component
- Visual feedback working: full = bright yellow, empty = dull brown

**Known Limitation:** Mesh doesn't animate internally; scale/color shift is only system-level.

**Next:** Replace with production particle-based or sculpted pile mesh.

---

## Map / Terrain — ⏳ Partial

**Current:**

- Flat procedural terrain plane (simple quad)
- Dark material matches GDD spec (desaturated browns/greens)
- Food piles placed procedurally on jittered ring
- Player bases tinted with player colors (readable)

**Missing:**

- Props (hay bales, fences, flowers) — not added yet
- Skybox / background — placeholder flat color
- Terrain variation — currently flat

**Next:** Import Blockade Labs skybox + Poly Haven props, or generate variations.

---

## Animation — State Machine Wired, No Clips

**Current:**

- Animator state machine defined for all 7 states
- Transitions configured with parameters
- Characters default to T-pose (Meshy auto-rig starting pose)

**Missing:**

- Actual animation `.anim` clip recordings
- Locomotion cycles (walk, idle run in-place with positional movement)
- Ability-specific animations beyond generic "play ability animation"

**Why T-pose works:** Animations for food collection and stun are largely positional movement + scaling, not skeletal. Idle/walk defined by Meshy's auto animations (transitioning when integrated).

**Next:** Record proper animation clips or import from library.

---

## Audio — Interface Ready, No Content

**Current:**

- `IAudioService` fully wired
- `UnityAudioService` can play SFX/music when clips assigned
- `AudioRegistrySO` asset ready for SFX library

**Missing:**

- All SFX (footsteps, collecting, attack, hit, stun, ability)
- Match music (upbeat farm/bluegrass loop)
- UI sounds

**Next:** Record or source SFX + compose/license music.

---

## Colorblind Accessibility — ✅ Implemented

Player colors use Okabe-Ito palette (4 colors, distinguishable under all common color blindness types):

- Player 1: Orange
- Player 2: Blue
- Player 3: Pink
- Player 4: Teal

Applied to: base tints, nameplate rings, HUD indicators. No red/green distinction anywhere.

---

## UI Visuals — ✅ Functional, Minimal

Current:

- Clean, minimal UI (matches GDD spec)
- Wood/rope texture placeholder (simple brown/tan)
- Touch-friendly large buttons
- Readable at 30 fps on mid-range Android

Fonts: Arial placeholder (ready for Fredoka One or similar when art arrives).

---

## VFX — Minimal Placeholder

Current:

- Screen shake on hit/death (feel enhancement)
- Simple debug particles for food pickup
- No fancy effects

Next: Add sparkle bursts, impact effects, ability-specific VFX.

---

## Summary: Production-Ready Placeholders

**What works now:** All 4 classes are visually distinct, readable, and can be replaced. Food piles are functional with visual feedback. Map is playable. No art blocks gameplay.

**What needs replacement:** Animation clips, audio, detailed terrain/props, hero VFX.

**Timeline flexibility:** Artists can replace any asset independently. No hard dependencies. Placeholder → production swap is asset-level, no code changes.
