# Art Direction v0.3

**Version:** 0.3  
**Date:** May 2026  
**Status:** UI specification finalized — single visual direction committed; implemented in code (cluckwars-*-v3)  
**Changelog v0.3.1:** Reconciled spec with the v0.3 gameplay build and the v3 wireframes (cluckwars-hud-v3 / -charselect-v3 / -ability-ref). **Removed the basic ATTACK button everywhere** — combat is ability-only; the former attack slot is now Ability 3 (Assassin's Combo slot). Added per-ability **icon glyphs** (§3), the **hex slot-index badge + cooldown number** (§6.6), the **HUD leaderboard + win-target badge** (§6.3), and a new **Control-State Overlays** section (§6.10). Updated touch layout (§6.7) and console hints (§6.8) to the ability cluster.  
**Changelog v0.3:** Art direction consolidated to a single rich/glossy style (Clash Royale / Disney-ish). MOBA-style touch controls. Removed two-direction comparison — one direction only. Updated all §6 subsections.  
**Changelog v0.2:** Full UI/UX specification added (§6); resolved player identity colors, typography, HUD layout, character select, overlays, touch controls, console adaptation.

---

## 1. Vision Statement

Cluck Wars should feel like a **warm, funny, chaotic cartoon farm** the moment it loads. The visual language must communicate instantly on a small screen — who is who, what is happening, and where the food is. Clarity always wins over decoration.

Three words to test every visual decision against: **readable, chunky, warm**.

The UI must feel **exciting, tactile, and attractive** — not minimalist. Think Clash Royale glossy panels, beveled buttons, dimensional depth. This is a fast-paced game suitable for children; every surface should feel like something you want to tap.

---

## 2. Rendering & Camera

- **2.5D** — real 3D meshes rendered with an orthographic isometric camera.
- **Render Pipeline** — Universal Render Pipeline (URP). All materials must use URP-compatible shaders (Lit, Unlit, or custom URP shader graph). No Built-in RP materials.
- No perspective distortion. Consistent scale across the entire map.
- Fixed camera — full map always visible. No zoom, no pan.
- Isometric angle: standard 45° rotation, ~30° elevation (exact value TBD in engine).
- URP 2D lights may be used for ambient warmth and food pile glow — evaluate against mobile performance budget.

---

## 3. Color Palette

### Overall Direction

| Area | Tone | Rationale |
|---|---|---|
| Map / terrain | Dark, desaturated warm browns and muted greens | Background — must not compete with characters |
| Food piles | Mid warm tones — golden yellows, amber | Readable against dark map, feel appetizing |
| Characters | Bright, saturated, warm cartoon colors | Must pop immediately against the map |
| UI | Rich amber/wood gradients with glossy highlights | Dimensional, warm, inviting — never flat |
| Ability FX | Saturated accent colors per ability type | Instant readability of what is happening |

### Map Palette (reference tones)

- Base ground: deep warm brown (#3B2A1A range)
- Grass patches: muted olive/dark green (#4A5C2A range)
- Path/dirt: desaturated tan (#6B5540 range)
- Shadows: very dark, low saturation — no pure black

### Food Pile Palette

- Full pile: warm golden yellow (#F5C842)
- 50% depleted: amber orange (#D48C20)
- Nearly empty: dull brown-grey (#7A6040)

Transition is continuous (driven by food % in `FoodPileVisuals`) — the three values above are waypoints.

### Character Palette

Each class has a **primary color** used on the body as the main read, plus a **dark** and **light** variant for gradients and highlights. Player identity (player 1–4) is communicated via a **color ring at the base** of the character.

| Class | Primary | Dark | Light | Feel |
|---|---|---|---|---|
| Fatty Chicken | #F5D75A | #B89E20 | #FFF3B0 | Soft, round, inviting |
| Speedy Chicken | #E85A2A | #B84418 | #FFB088 | Energetic, alert |
| Warrior Chicken | #C04030 | #8A2A20 | #F09888 | Strong, grounded |
| Assassin Chicken | #7B68EE | #5A48C8 | #C4B8FF | Mysterious, sleek |

### Player Identity Colors (Color-Blind Safe)

Derived from the Okabe-Ito palette. Validated across protanopia, deuteranopia, and tritanopia.

| Player | Color | Hex | Notes |
|---|---|---|---|
| P1 | Sunset Orange | #E8751A | Warm — reads yellow-ish under deuteranopia |
| P2 | Ocean Blue | #1A7FC4 | Cool — universally distinguishable |
| P3 | Berry Pink | #C4286F | Bold — reads purple-ish under protanopia |
| P4 | Forest Teal | #0D9E7A | Green-blue — distinct from all above |

**Never rely on color alone** — always supplement with labels (P1/P2/P3/P4).

### Ability Accent Colors & Icons

Each ability has a unique accent color (used on hex buttons, cooldown overlays, and VFX) and an **icon glyph**. The accent is authored per `AbilityBaseSO.AccentColor`; the glyph per `AbilityBaseSO.Icon` (blank falls back to the subclass `DefaultIcon`, surfaced via `ResolveIcon()`). 14 abilities across 4 categories (GDD §7.2). Cooldown tier: **S** = short 3–6 s, **M** = medium 8–12 s.

| Category | Ability | Icon | Accent | Hex | CD |
|---|---|---|---|---|---|
| Damage  | Flying Peck   | 🪽 | Orange      | #FF7043 | S |
| Damage  | Cluck Shock   | ⚡ | Deep Orange | #FF5722 | M |
| Damage  | Peck          | 🐦 | Burnt Orange| #E64A19 | S |
| Control | Roll & Push   | 🌀 | Light Purple| #AB47BC | S |
| Control | Feather Trap  | 🪤 | Purple      | #9C27B0 | M |
| Control | Feather Aura  | 💨 | Deep Purple | #8E24AA | M |
| Control | Root Egg      | 🌱 | Dark Purple | #7B1FA2 | M |
| Defense | Egg Shell     | 🥚 | Light Green | #66BB6A | S |
| Defense | Turtle Mode   | 🐢 | Green       | #4CAF50 | S |
| Defense | Spine Coat    | 🦔 | Dark Green  | #43A047 | M |
| Utility | Speed Burst   | 💨 | Cyan        | #26C6DA | S |
| Utility | Invisibility  | 👻 | Light Blue  | #00ACC1 | M |
| Utility | Doppelganger  | 👥 | Teal        | #0097A7 | M |
| Utility | Sneaky Steal  | 🤏 | Cyan        | #00BCD4 | S |

> **Note on glyph rendering:** the icons are stored on each ability asset and wired into the hex buttons and character-select cards in code. Color-emoji rendering needs a TMP sprite/emoji font (see §9 TBD #11); under the current legacy `UnityEngine.UI.Text` + built-in font, glyphs not in the base font fall back to the short label.

---

## 4. Character Design

### Proportions

- **Reference:** Hei Hei from Moana — oversized round body, tiny wings, large expressive head, short stubby legs.
- Exaggerated and chunky — not realistic, not cute-tiny. Big and dumb-looking is correct.
- Silhouette must be **instantly readable** at ~100px height on a mobile screen.
- Each class must have a **distinct silhouette** — different body shape, not just color swap:

| Class | Silhouette direction |
|---|---|
| Fatty Chicken | Widest, lowest center of gravity, wobbles |
| Speedy Chicken | Leaner, slightly taller, more angular posture |
| Warrior Chicken | Broad-shouldered (for a chicken), upright, strong stance |
| Assassin Chicken | Sleek, slightly hunched, smaller profile |

### Visual Treatment

Characters use **radial gradients** (light highlight at upper-left → primary center → dark at lower-right) for a dimensional, Disney-ish look. Each chicken has:

- Body ellipse with gradient fill + subtle highlight
- Distinct head with gradient, eye with specular highlight
- Comb (red, wavy path with darker stroke)
- Beak (orange-gold, pointed right)
- Small wing (darker overlay on body)
- Stubby legs with feet details
- Ground shadow ellipse below feet
- Player identity ring at the base (colored oval matching player color)

### Facing & Mirroring

- Two base mesh orientations: facing right, facing left (horizontal mirror).
- Vertical movement handled via vertical sprite/mesh flip.
- No in-between angles — snap only.

### Animation Style

- Snappy and exaggerated — no motion blur, no easing. Cartoon timing.
- Hit reactions should be big and readable (squash, wobble).
- Idle animation: subtle loop — breathing, occasional eye blink or head twitch.
- Stunned: slumped over, stars or birds circling (particle FX TBD).
- Collecting: pecking motion — rhythmic, clear, immediately readable as "I am eating".

---

## 5. Map Design

### Layout Visual Guidelines

- Map is dark enough that characters pop without effort.
- Central food pile should be the **brightest element on the map** — draws the eye immediately on load.
- Player bases (edges) should be subtly distinct per player via color tinting — matches their player identity color.
- Contested food islands: slightly raised terrain or rim to visually separate them from the path between.

### Terrain & Props

- Farm aesthetic: hay, wooden fences, dirt paths, patches of grass.
- Props are low-profile — must not obscure characters or food piles.
- No tall props near the center or high-traffic areas.
- Suggested prop set (preliminary): hay bales, wooden fence segments, flower patches, mud puddles.

### Food Pile Visuals

- Full pile: large mound, vibrant golden color with radial gradient (center highlight), subtle glow shadow.
- As it depletes: physically shrinks + color desaturates (continuous, driven by food %).
- At <25%: consider a visual "last scraps" state — small pile, dull color, maybe a flies/dust particle (TBD).
- Empty pile: a faint ground mark or shadow — should still be readable as "there was food here".

---

## 6. UI Design

### 6.1 Visual Style — Rich Glossy

A single art direction: **rich, dimensional, glossy**. Inspired by Clash Royale / Supercell games with a Disney-ish warmth. Everything feels tactile and layered.

**Surface treatment rules:**

| Element | Treatment |
|---|---|
| Panels | Double-layer: outer frame gradient (#5a3a1a → #3a2210) + inner body gradient (#4a3018 → #2a1a0c). 3px border (#8a6a3a). Inner glow highlight at top, dark shadow at bottom. Corner radius 14px. |
| Cards | Gradient fill (#3a2816 → #2a1c0e). 2px border (#6a4a28). Embossed feel: inset highlight at top, drop shadow. Selected state: accent-color border + outer glow shadow. Scale 1.04× on select. |
| Buttons | Three-stop vertical gradient (light → mid → dark). 2px border (darker shade). Inset highlight at top (white 0.6 alpha), inset shadow at bottom (black 0.25). Drop shadow. Radius 10px. |
| Text | Lilita One for headings. Always has text-shadow (0 2px 4px rgba(0,0,0,0.6)). Accent text gets color glow shadow. |
| Ribbons | Gold gradient banners with triangular tail cutouts. Used for section titles ("CHOOSE YOUR CHICKEN", "MATCH LOBBY"). Embossed with inner highlights. |

**Theme tokens (all in one table for implementation):**

| Token | Value |
|---|---|
| Panel outer gradient | linear-gradient(180deg, #5a3a1a, #3a2210) |
| Panel inner gradient | linear-gradient(180deg, #4a3018, #2a1a0c) |
| Panel border | #8a6a3a |
| Panel shadow | inset 0 1px 0 rgba(255,220,140,0.3), inset 0 -1px 0 rgba(0,0,0,0.4), 0 6px 20px rgba(0,0,0,0.6) |
| Card background | linear-gradient(180deg, #3a2816, #2a1c0e) |
| Card border | #6a4a28 |
| Selected card glow | 0 0 16px rgba(245,200,66,0.5) |
| Primary button | linear-gradient(180deg, #f5c842, #d4a020, #b88a14) |
| Start button | linear-gradient(180deg, #5ac54f, #33a332, #228b22) |
| Danger button | linear-gradient(180deg, #e85a4a, #c83030, #a82020) |
| Primary text | #fef5e0 |
| Secondary text | #c4a060 |
| Accent text | #f5c842 |
| Screen background | radial-gradient(ellipse at 50% 20%, #2a1a0c, #0e0804 80%) |
| Map background | radial-gradient(ellipse at 50% 40%, #1a2a10, #0a1208 60%, #050804) |
| HUD top gradient | linear-gradient(180deg, rgba(10,6,2,0.7), transparent) |
| HP bar fill | linear-gradient(180deg, #e84040, #c03030) |
| Cargo bar fill | linear-gradient(180deg, #f5c842, #d4a020) |
| Bar trough | #1a1008 with inset shadow |

### 6.2 Typography

| Role | Font | Weight | Notes |
|---|---|---|---|
| Headings, titles, labels, timer, scores | Lilita One | 400 (single weight) | Bold, punchy, cartoon-friendly. Highly legible at small sizes. |
| Body text, descriptions, lore quotes | Nunito | 400 / 600 / 700 / 800 | Round, warm, pairs well with Lilita One. |

Both available via Google Fonts. For Unity, import as TMP font assets with SDF rendering.

All text uses `text-shadow: 0 2px 4px rgba(0,0,0,0.6)` as a baseline. Accent-colored text adds a color glow: `0 0 8px {color}88`.

Minimum font sizes (at 1920×1080 reference resolution):
- Mobile: 20px labels, 28px buttons, 44px titles.
- Console: 24px labels, 32px buttons, 56px titles.

### 6.3 Match HUD Layout (Minimal — On-Character Bars)

The match HUD is minimal — the game area is sacred. There is **no attack button** (combat is ability-only). HP and cargo bars live on/near the local chicken.

```
┌─────────────────────────────────────────────────────────────────┐
│ ┌───────────────┐                                   ┌────────┐  │  TOP
│ │1st P1   ▓▓░ 42│                                   │ 02:34  │  │
│ │2nd P4   ▓▓░ 31│                                   └────────┘  │
│ │3rd P3   ▓░░ 28│                                               │
│ │4th P2   ▓░░ 17│                  GAME AREA                    │
│ └───────────────┘                                               │
│  ★ FIRST TO 150                  [chicken sprite]               │
│                                   ████░░░░ HP                   │
│                                   ███░░░░░ Cargo · 🍎 9/20      │
│                                                      ⬡ Ability2 │
│  ┌──────────┐                              ⬡ Ability1          │
│  │ Joystick │                          ( ⬡ Ability3 ★ —        │
│  │   ○      │                            Assassin only )       │
│  └──────────┘                                                   │
└─────────────────────────────────────────────────────────────────┘
```

#### Top-left — ranked leaderboard panel
- One row per player, **sorted live by stored food** (descending).
- Each row: ordinal rank (`1st`/`2nd`/`3rd`/`4th`), glossy player-color dot, `P#` in player color, a thin progress bar (food ÷ win-target), and the food score in gold.
- The local player's row gets a player-color left border + tint; the leader's row a faint gold wash.
- A **win-target badge** (`★ FIRST TO 150`, value from `MatchConfigSO.FoodTargetToWin`) sits directly under the panel.

#### Top-right — timer
- Embossed badge: dark gradient background, gold border, inner highlight, letter-spacing 3px, gold text.

#### On-character indicators
- HP bar: 52–64px wide, 7px tall, dark trough (#0a0604) with 1.5px border (#4a2818), inset shadow. Fill uses HP gradient with inset highlight at top.
- Cargo bar: same width, 5px tall, 2px below HP. Fill uses cargo gradient.
- Cargo text: food icon + "9/20" in Nunito 700, 10px.
- Player identity ring: colored oval at feet, matching player color with slight stroke.
- All bars have beveled appearance: inset shadow on trough, highlight on fill.

### 6.4 Character Select Screen

Full-screen menu in the Bootstrap scene.

**Layout (landscape):**

```
┌───────────────────────────────────────────────────────────────┐
│              ═══ CHOOSE YOUR CHICKEN ═══  (ribbon)            │
├────────┬─────────────────────────┬───────────────────────────┤
│ ┌────┐ │                         │  ╔═══════════════════╗    │
│ │Faty│ │    [glowing platform]   │  ║ STATS             ║    │
│ └────┘ │    [chicken preview]    │  ║ Cargo  ■■■■■      ║    │
│ ┌────┐ │                         │  ║ Rate   ■■■■■      ║    │
│ │Sped│ │    Character Name       │  ║ HP     ■■■■░      ║    │
│ └────┘ │    "Lore quote..."      │  ║ Resist ■■■■░      ║    │
│ ┌────┐ │                         │  ║ Speed  ■■░░░      ║    │
│ │Warr│ │    [Default] [🔒] [🔒]  │  ║ Attack ■■░░░      ║    │
│ └────┘ │                         │  ╠═══════════════════╣    │
│ ┌────┐ │                         │  ║ ABILITY            ║    │
│ │Assn│ │                         │  ║ ⬡ Speed Burst      ║    │
│ └────┘ │                         │  ╚═══════════════════╝    │
├────────┴─────────────────────────┴───────────────────────────┤
│  [Solo] [Host] [Join]                  [START MATCH ▶▶]      │
└───────────────────────────────────────────────────────────────┘
```

**Components:**

| Element | Description |
|---|---|
| Title ribbon | Gold gradient ribbon banner with triangular tail cutouts. "CHOOSE YOUR CHICKEN" in Lilita One with text shadow. |
| Class cards (left column) | Vertical stack of 4 cards. Each shows chicken silhouette + class short name + role. Active card: accent-color border, outer glow, scale 1.04×. Gradient card backgrounds. |
| Character preview (center) | Chicken on a glowing circular platform. Radial gradient background tinted to class color. Class-colored glow aura around the platform. Name in class color with glow shadow. Lore quote in italic Nunito. 3 skin slots below (Default + 2 locked). |
| Detail panel (right) | Glossy panel frame. Stats section: 6 rows of beveled square dots (filled = gradient with highlight, empty = dark with inset shadow). Ability section: hex button + name + type. Assassin shows ×2. Gold gradient divider lines between sections. |
| Session bar (bottom) | Gradient fade background. Mode buttons (Solo/Host/Join) as beveled toggles. Start button: green gradient with embossed highlight. |
| Console hints | Bottom-left: gamepad button hints (←→ Select, A Confirm, LB/RB Tab) at 60% opacity. |

**Console adaptation:** All elements scale ~1.15×. Larger cards (135px vs 118px), bigger preview (180px vs 150px), wider detail panel (300px vs 260px).

### 6.5 Overlays

#### Match End

| Element | Specification |
|---|---|
| Background | Game area dimmed with overlay (rgba(8,4,2,0.88)). Decorative particles scattered. |
| Panel | Glossy double-layer panel, ~440px wide. |
| Winner crown | 👑 emoji, 36px, with gold drop-shadow glow. |
| Winner banner | Ribbon component in winner's player color. "PLAYER X WINS!" in Lilita One. |
| Winner chicken | Chicken silhouette with player identity ring below the banner. |
| Leaderboard | 4 rows: medal emoji (🥇🥈🥉) + glossy player dot + "PX" in player color + beveled score bar (gradient fill proportional to max) + food icon + score number. Winner row has subtle accent background. |
| Restart countdown | "Next match in 5s…" in secondary text, Nunito 600. |

#### Match Lobby

| Element | Specification |
|---|---|
| Panel | Glossy panel, ~480px wide. |
| Header | "MATCH LOBBY" as ribbon banner. Join code in bright green (#4ae66a) badge with green gradient background, 2px border, glow shadow. Monospace-ish feel (Nunito 800, letter-spacing 3px). |
| Player list | One row per player: glossy player dot + chicken silhouette + name + class/role + ready status (green "✓ Ready" with glow, or dim "Picking…"). Rows have card background + player-color border tint. Empty slots: dashed border, "Waiting for Player N…" |
| Start button | Full-width green gradient button. Host-only. |

#### Intro Countdown

| Element | Specification |
|---|---|
| Background | Game area dimmed (rgba(0,0,0,0.45)). Radiating circular glow behind number. |
| Number | 180px Lilita One, gold (#f5c842), multi-layer text-shadow + drop-shadow filter for intense glow effect. |
| Subtext | "GET READY" in a subdued ribbon banner. |
| Top bar | Player badges + timer visible at 40% opacity behind the overlay. |
| "GO!" flash | Replaces number after countdown, lingers 0.6s. |

### 6.6 Ability Buttons (Hexagonal — Beveled Glossy)

Hex buttons use a **pointy-top hexagon** with dimensional shading. The hexagon suggests honeycomb / farm motif while being visually distinct from the circular attack button.

**Construction (layered, bottom to top):**

| Layer | Description |
|---|---|
| 1. Outer shadow | Slightly larger hex in dark (#1a0e04) at 60% opacity — provides drop shadow depth. |
| 2. Border hex | Dark shade of ability color, 2px stroke — defines the hard edge. |
| 3. Main fill | Three-stop vertical gradient: lighter shade → ability accent → darker shade. |
| 4. Gloss overlay | Linear gradient: white 45% alpha at top → white 5% at middle → black 20% at bottom. Creates the glossy bevel. |
| 5. Inner highlight | Slightly smaller hex outline in white 15% alpha — subtle inner rim. |
| 6. Cooldown fill | Dark semi-transparent overlay (rgba(0,0,0,0.6)), clipped from bottom upward. Driven by `remainingCooldown / totalCooldown`. |
| 7. Content | Ability **icon glyph** (`AbilityBaseSO.ResolveIcon()`) + short label (`ShortLabel`) in Lilita One, white, drop-shadow. |
| 8. Slot badge | Small circle, top-left: `1` / `2` / **`★`** for the third (Assassin Combo) slot. Slot 3's badge is gold-filled with dark text to call out the class privilege. |
| 9. Cooldown number | Seconds-remaining integer, centered, shown only while `cd > 0`; the icon dims to ~35% alpha and the base to 45% underneath it. |

| Property | Value |
|---|---|
| Size | 150px (mobile reference), scales with `CanvasScaler` |
| Fill | Ability's `AccentColor` from `AbilityBaseSO` as center stop |
| Neutral state | `ColorSchemeSO.AbilityNormal` (blue) when no ability equipped |
| Slot 3 visibility | Hidden unless an ability is equipped in slot 2 (Assassin only) |
| Cluster layout | 2 abilities → vertical-ish stack; 3 abilities → triangle (primary at thumb base) |

### 6.7 Touch Controls — MOBA Layout (Mobile)

Controls follow a **MOBA arc pattern** (Wild Rift / Mobile Legends style). There is **no attack button** — combat is ability-only. **Ability 1** is the primary anchor at the bottom-right thumb base; the other abilities arc up-and-left within natural right-thumb sweep distance. Most classes show **2** ability hexes (vertical-ish stack); the **Assassin** shows **3** (triangle — primary at the thumb base, the ★ Combo slot furthest out).

```
                                   ⬡ Ability 2
                              ⬡ Ability 3 ★
  ┌──────────┐                         ⬡ Ability 1
  │ Joystick │                          (primary)
  │    ○     │
  └──────────┘
  LEFT THUMB                          RIGHT THUMB
```

**Right-hand cluster (hex buttons, anchored bottom-right; reference positions, design v3):**

| Control | Shape | Size | Anchored pos (from bottom-right) | Notes |
|---|---|---|---|---|
| Ability 1 | Hexagon (glossy) | 150 | (−170, 170) | Primary thumb anchor. Slot badge `1`. Tinted with equipped `AccentColor`. |
| Ability 2 | Hexagon (glossy) | 150 | (−210, 360) | Arc above-left. Slot badge `2`. Radial cooldown + seconds number. |
| Ability 3 ★ | Hexagon (glossy) | 150 | (−410, 270) | **Assassin only** — hidden when slot 2 is empty. Gold `★` slot badge (Combo). |

**Left-hand joystick:**

| Control | Shape | Size | Position | Notes |
|---|---|---|---|---|
| Joystick base | Circle | 135×135 | bottom:20, left:28 | Radial gradient (center slightly brighter), 2.5px white border at 12% alpha. Inner shadow. |
| Joystick knob | Circle | 55×55 | Centered in base, draggable | Glossy radial gradient with highlight at upper-left. White border at 20% alpha. |

All touch elements on a `CanvasScaler` at 1920×1080 reference, `matchWidthOrHeight = 0.5`.

### 6.8 Console Adaptation

On console (16:9, gamepad input), all touch controls are removed. **Gamepad button hints** appear in the bottom-right. There is no attack — every face button maps to an ability slot:

| Button | Action | Accent Color | Style |
|---|---|---|---|
| X | Ability 1 | Equipped slot-0 accent | Glossy gradient square, 30×30, radius 8, with bevel highlights |
| Y | Ability 2 | Equipped slot-1 accent | Same glossy style |
| B | Ability 3 ★ | Equipped slot-2 accent | **Assassin only** — hidden otherwise |

Button hints: 30×30 rounded squares with 3-stop gradient fill matching ability color, 2px darker border, inset highlight + drop shadow. Text label to the right in Nunito 600, 11px, at 70% opacity. Group with 14px gap.

Navigation: Left stick moves. D-pad navigates menus. A confirms, B backs (in menus). LB/RB switches between panel sections in character select.

### 6.9 ColorSchemeSO Updates

`ColorSchemeSO` should be updated with the following values. Gradient buttons require either a custom `Image` shader or a two-color vertical system (top/bottom colors).

| Field | Value | Notes |
|---|---|---|
| `PanelBackground` | rgba(0.29, 0.19, 0.09, 0.92) | Warm dark wood |
| `ButtonNormal` | Top: #f5c842, Bottom: #b88a14 | Gold gradient |
| `ButtonHover` | Top: #ffe066, Bottom: #c89818 | Brighter gold |
| `ButtonActive` | #f5c842 | Flat fallback |
| `StartButton` | Top: #5ac54f, Bottom: #228b22 | Green gradient |
| `TextOnButton` | #fef5e0 | Warm white |
| `TextOnActive` | #1a0e04 | Near-black |
| `JoystickBase` | rgba(1,1,1,0.06) center → rgba(1,1,1,0.02) edge | Radial gradient |
| `JoystickKnob` | rgba(1,1,1,0.25) highlight → rgba(1,1,1,0.08) | Glossy radial |
| `AttackNormal` | Top: adjust(#d43030, +40), Bottom: adjust(#d43030, -30) | **Legacy** — field retained for the flavor framework but unused (no attack button in v0.3) |
| `AbilityNormal` | Driven per-ability by `AccentColor` gradient | Three-stop gradient; neutral blue when slot empty |
| `CooldownDim` | rgba(0,0,0,0.6) | Dark overlay |

### 6.10 Control-State Overlays (on-character)

Control states attach to the affected chicken **in world space** — never to a HUD panel — so "who is what" reads instantly mid-effect. The player identity ring stays visible underneath every overlay. (Wireframe: `cluckwars-hud-v3` `CWStateChicken` / `CWControlStatesSheet`.)

| State | Trigger | Duration | Visual |
|---|---|---|---|
| **Stunned** | HP reaches 0 (damage ability) | 5 s | Cartoon stars circle overhead; body tilts + desaturates; nameplate turns red with a 💀. Drops all cargo at feet (gameplay consequence — show crumbled food sprites). |
| **Slowed** | Collision / pile / slow ability | 1–4 s (ability-dependent) | Subtle blue tint blob + fading speed-trail behind the chicken; 🐌 on the nameplate. Speedy resists via Slippery. |
| **Knocked back** | Push abilities (Roll & Push, Spine Coat, Peck) | Instant | Motion lines + ghost trail in the travel direction; no persistent overlay; 💨 flash on the nameplate. Fatty resists via Immovable. |
| **Rooted** | Root Egg trap | 2–3 s | Vines/roots wrap the legs; 🌱 on the nameplate. Can still cast abilities while rooted. |

Nameplate badge: rounded pill in the player's color (dark red while stunned), holding the state icon + `P#`. All overlays are local, driven by observed `[Networked]` state — never networked themselves.

### 6.11 Ability Feedback — Telegraph & Impact (v0.6)

The visual language of the Telegraph → Impact → Aftermath system. Design intent lives in
`docs/FEEDBACK.md`; this section is the **look**.

**Every number below is referenced by name, never restated.** All timings, alphas,
magnitudes and semantic colours are `public const` / `static readonly` in
`Assets/_Game/Scripts/Visuals/FeedbackTuning.cs`, which is the single source of truth and
carries the rationale for each value. Restating them here would create a second copy to
drift. If a value looks wrong on device, change it in `FeedbackTuning` — nothing reads a
literal.

#### Telegraph shapes (the ground preview)

One geometry builder, `TelegraphShapes`, serves **both** the hold-to-aim preview
(`AbilityTelegraph`) and the impact cast flash (`AbilityRangeIndicator`), so the shape you
aimed and the shape that fired are the same polyline. Drawn as a closed `LineRenderer`
outline on the ground plane, in the ability's `AccentColor` at
`FeedbackTuning.TelegraphPreviewAlpha`.

| `AbilityAimShape` | Drawn as | Abilities |
|---|---|---|
| `None` | Self-ring at the caster's feet, radius `FeedbackTuning.SelfRingRadius` (0.62 — deliberately the status ring's radius, so "the chicken's own footprint" stays one idea). No target marking; the caster's own ring is the mark. | Speed Burst, Turtle Mode, Invisibility, Spine Coat, Egg Shell, Doppelganger |
| `SelfCircle` | Circle centred on the caster at `AimRadius`. | Cluck Shock, Stun Burst, Peck, Sneaky Steal |
| `ForwardCircle` | Circle offset `AimForwardOffset` metres along flattened facing, plus a thin **stalk** connecting caster to circle so the offset reads as deliberate rather than as a detached decal. | Feather Trap, Roll Push, Roll Trample, Root Egg |
| `Cone` | Forward arc of `AimConeAngle` degrees, closed back through the caster — a wedge, not an arc segment. | Wing Slam |
| `Aura` | Circle that follows the caster while the ability is active (not just while aiming). | Feather Aura |
| `Jump` | Landing ring at the destination plus the same stalk as `ForwardCircle`, reading as an arc to a place. | Shadowstep, Ambush |
| `SingleTarget` | Range circle; the marking on the chosen target carries the information. | Mark Kill |

Any ability declaring a shape but resolving to a non-positive `AimRadius` degrades to a
self-ring rather than a zero-size line — an unfinished ability looks unfinished, never
invisible.

#### `TargetHighlight` — the colour triple

Three states, each coded by **at least two** channels per §1.3 of FEEDBACK.md, so all
three survive greyscale and colour-blind reads:

| Mark | Colour | Bracket | Motion | Extra | Means |
|---|---|---|---|---|---|
| **Valid** | the *casting ability's own* `AccentColor` — never a colour of its own, so the bracket always matches the ground decal it belongs to | **solid**, alpha `FeedbackTuning.ValidTargetBracketAlpha` | **pulsing** at `FeedbackTuning.ValidTargetPulseHz` | — | Will be hit. `WouldAffect` says yes. |
| **Immune / no-effect** | `FeedbackTuning.NeutralNoEffectColor` (neutral grey) | **dashed** | **static** | small ⃠ glyph | Inside the shape, but the ability does nothing — Spine Coat reflect, Turtle Mode, already-stunned for a stun, a `requireCargo` ability against an empty-handed rival. |
| **Not in shape** | — | hidden | — | — | Outside the aim shape entirely. Drawn as nothing, not as a third colour. |

`NeutralNoEffectColor` is deliberately shared with the §6 "no valid target" refusal state
on the hex button: the same grey means "nothing will happen here" in both the world and
the HUD, so it is one word in the player's vocabulary, not two.

The illegal-cast wash (`FeedbackTuning.IllegalCastTintColor`, a red-biased grey) is a
**fourth, distinct** colour and must not be confused with the neutral grey. Neutral grey
says "there was never a valid target here"; the red-grey wash says "this *stopped* being
legal" — two different refusal stories that must not look identical. The preview lerps
into it over `PreviewIllegalDesaturateSeconds`.

`TargetHighlight` is **strictly additive** — its own sprites, never the body material —
specifically so it cannot fight the hit-flash, which does own body colour. Same discipline
as `ChickenStateOverlays`.

#### Hit and whiff

| | Treatment |
|---|---|
| **Hit** | Victim white body flash for `VictimHitFlashDurationSeconds` peaking at `VictimHitFlashPeakIntensity` (deliberately not 1.0 — a fully white chicken loses its player-identity colour at the exact moment a bystander needs to track *who* got hit). White hit-spark at each victim. `ImpactMotionLineCount` lines of `ImpactMotionLineLength`, fanned `ImpactMotionLineFanDegrees` either side of the incoming bearing, living `ImpactMotionLineLifetimeSeconds` — deliberately *outliving* the flash, so "hit" and "from there" land as two beats. |
| **Whiff** | Cast ring desaturated by `WhiffRingSaturationMultiplier` and dimmed by `WhiffRingAlphaMultiplier`. **Not silent and not invisible** — a whiff that looks like a hit is the single worst failure in this system. Not fully greyscale, so a whiffed Steal and a whiffed Root Egg still carry a faint hue difference. `AimShape.None` self-buffs are exempt and always report as hits — a self-buff has no one to miss. |
| **Shake tiers** | Three, and the ratios are the point: `CasterMicroShakeMagnitude` (0.06) → `VictimHitShakeMagnitude` (0.12) → `DeathShakeMagnitude` (0.35). Roughly 2× then 3×, which keeps caster-punch, took-a-hit and knockout distinguishable without any one of them being disruptive at 30 fps. Every shake is gated on `HasInputAuthority` — peers never feel each other's. |
| **Execute** | Feather burst, death shake, and a hit-stop at `ExecuteHitStopTimeScale` for `ExecuteHitStopDurationSeconds`. Deliberately a heavy slow, **not** a full freeze: 0.08 s at timescale 0 on a 30 fps Android target is ~2.4 frames and reads as a dropped frame rather than as intent. |

Floating combat text (`FloatingCombatText`) spawns at `FloatingTextSpawnHeight`, rises
`FloatingTextRiseDistance`, holds fully opaque for `FloatingTextHoldFraction` of
`FloatingTextLifetimeSeconds` then fades. Pooled at `FloatingTextLivePeerCap`, never
grown; when full it recycles the **oldest**, because a fresh number is always worth more
than one already most of the way through its fade.

### 6.12 Status badges and drain rings (v0.6)

**Three drain rings, one mechanism.** The status arc (§5.1), the self-buff ring (case 30)
and the placed-zone ring (case 23) all use the same `DrainRing`: a dim full-circle
*track* at `FeedbackTuning.DrainRingTrackAlphaMultiplier` of the bright draining *arc*.
The contrast is what makes "how much is left" out-read "how much there was" — without the
track, a nearly-drained ring reads as an unrelated sliver instead of as "almost out". Arc
geometry regenerates at `StatusArcDrainUpdateHz`, not per frame.

Radii are deliberately distinct so they stack rather than fight:

| Ring | Radius | Why |
|---|---|---|
| Status ring (stun/root/slow) | `SelfRingRadius` 0.62 | Established first; owns "the chicken's own footprint". |
| Self-buff expiry ring | `SelfBuffRingRadius` 0.80 | 0.18 further out, so a buffed-**and**-slowed chicken shows two concentric rings instead of one ring flickering between two colours. |
| Placed-zone ring | `AbilityZone.TriggerRadius` | Not an artist's approximation — the *same accessor gameplay overlaps against*, so the drawn edge **is** the trigger. |

Zone rings take the canonical colour of the state they inflict, so a Feather Trap's edge
is the same cyan as the slow ring it will put on you. An unhandled future `ZoneEffect`
falls through to `CanonicalKnockColor` (white) — visible but unstyled, so a new zone type
ships looking unfinished rather than shipping invisible.

**Canonical control-state colours** — `FeedbackTuning.CanonicalStunColor` /
`CanonicalRootColor` / `CanonicalSlowColor` are now the single source for stun/root/slow
everywhere (rings, badges, zones, HUD strip, stick tint). Root is a *grass* green with its
blue channel near zero, not the Okabe-Ito "bluish-green" #009E73 — that value is almost
exactly **P4 Forest Teal**, and a rooted P4 chicken reading as "extra teal" is the one
collision this system cannot afford. Slow is likewise kept lighter and more cyan than P2
Ocean Blue. Per §1.3 colour is never the only channel: each pairs with shape (orbiting
stars / static shackle / trailing streaks).

**Badge stack** — this extends §6.10 rather than replacing it. §6.10's nameplate glyphs
(🌱 root, 🐌 slow, inline in `ChickenNameplate`'s label) stay exactly as they are: the
always-on, zero-cost "this chicken is not free" marker attached to the name. The v0.6
addition is the layer §6.10 never had — a **stack**, up to `StatusBadgeMaxRows` (3) rows,
one per *simultaneously active* status, each carrying its own quantity. A rooted-and-slowed
chicken shows one §6.10 nameplate glyph and two badge rows.

The stack occupies a tight vertical band, and the constants are an invariant, not
preferences: cargo bar 1.16 → nameplate 1.70 → **badges `StatusBadgeStackBaseHeight` 1.78
+ `StatusBadgeRowSpacing` 0.11 × 2 = 2.00** → floating text floor
`FloatingTextSpawnHeight` 2.05. Three rows exactly fill the gap. Badges are authored
smaller than the nameplate to make that fit. Countdown text updates at
`BadgeCountdownTextUpdateHz`, never per frame — UI text writes trigger layout.

Glyphs: ⚡ stun, ⛓ root, 🐌 slow. These are **shared verbatim** between the world-space
badges and the HUD strip — `ChickenStatusBadges` aliases `HudFeedbackStyle`'s consts at
compile time, so the two surfaces cannot drift into speaking different languages for the
same state. None of the three exist in `LilitaOne-Regular.ttf` (225 codepoints) and TMP's
global fallback list is empty, so any element showing one **must** carry the
`cw-glyph-font` class (NotoEmoji, monochrome and therefore tintable).

### 6.13 Hex refusal states — extending §6.6's nine layers

The four distinguishable refusal reasons add **no tenth layer**. Instead, §6.6's **layer 9**
generalises from "the cooldown seconds" to "**the state indicator**". This works because
`AbilityRefusalRules.Evaluate` makes the reasons mutually exclusive by precedence
(SlotUnavailable > Cooldown > Stunned > OtherAbilityActive > NoTarget), so the centre of
the hex never has to arbitrate between two marks.

| Refusal | Layers 2–4 (border / fill) | Layer 6 (clip) | Layer 7 (icon) | Layer 9 (centre) |
|---|---|---|---|---|
| *(ready)* | accent, alpha `HexAlphaReady` | — | full | hidden |
| **Cooldown** | accent, alpha `HexAlphaCooldown` | **black, bottom-up** | dimmed | **seconds integer** |
| **No target** | `NeutralNoEffectColor`, alpha `HexAlphaNoTarget` | — | dimmed | **⃠** in `NeutralNoEffectColor` |
| **Stunned** | `IllegalCastTintColor` wash, on **every** hex at once | — | dimmed | **✕** in `RefusalStunnedCrossColor` |
| **Other ability active** | accent, alpha `HexAlphaOtherActive` (the dimmest tier) | on the *running* slot only: **accent, top-down** | dimmed | hidden |
| **Slot unavailable** | `display: none` | — | — | — |

Three orthogonal channels do the separating, so no two states can collapse into each
other: **hue** (accent / neutral grey / red-grey wash), **shape** (nothing / number /
circle-slash / cross), and **clip direction** (none / black-from-bottom /
accent-from-top).

Two deliberate choices worth keeping:

- **The top-down accent drain is the mirror of the bottom-up black cooldown clip** in both
  direction and colour. This is necessary, not decorative: an ability burns its cooldown
  at activation, so the running slot is *also* cooling, and the two clips routinely
  coexist on the same hex. Opposite direction + opposite colour is what keeps "this is
  running" and "this is cooling" readable simultaneously. Driven from `ActiveRemaining01`.
- **`OtherAbilityActive` gets no centre mark.** Its signal is the drain on the *other*
  slot — the one actually running. Stamping a mark on every suppressed hex would say
  "four things are wrong" when one thing is happening.

`HexAlphaOtherActive` (0.35) is dimmer than `HexAlphaCooldown` (0.45) on purpose: if they
matched, a suppressed cluster would read as "everything went on cooldown at once", which
is the wrong story.

**Refusal marks are USS-drawn shapes, not characters.** ⃠ (U+20E0) and ✕ (U+2715) are
absent from *both* `LilitaOne-Regular.ttf` and `NotoEmoji-Regular.ttf`, and TMP's global
fallback list is empty, so both would have drawn tofu on every platform — not just
Android. ⃠ is drawn as a bordered circle plus a rotated bar; ✕ as two rotated bars. The
decision is reversible in one line via `HudFeedbackStyle.UseDrawnRefusalMarks`, kept
`static readonly` so both branches stay live code.

> ⚠️ **Known font gap, pre-existing:** the slot-3 Combo badge's **★ (U+2605)** is also
> absent from both fonts. It currently survives on OS-level font fallback, which is
> exactly the thing that varies on Android. Verify on the Pixel 9.

**Denied-press bump** (case 29): a refused press is never absorbed silently. The hex
shakes horizontally with a decaying oscillation — `DeniedPressBumpAmplitudePx` over
`DeniedPressBumpDurationSeconds` at `DeniedPressBumpOscillations` cycles, driven by
`unscaledDeltaTime` so the execute hit-stop can't stretch it past the press that caused
it. The amplitude was raised from the spec's 4 px (≈1.7 dp on this canvas — likely
invisible under a resting thumb) to 10 px and is flagged as a live-session number to
eyeball, not a confident final value.

**HUD status strip placement** — the strip sits **bottom-left, stacked directly above the
joystick** (`bottom: 380px`, clearing the joystick's 360px top edge by 20 px, growing
upward). This deviates from FEEDBACK.md §5.2's "below the cargo readout", which is stale:
the cargo readout moved to world space on the chicken (`ChickenWorldBars`, §6.3 — "the
game area is sacred") and no longer exists in the HUD. Siting it by the joystick puts the
constraint and the control it constrains in one glance. It lives in the existing
`TouchControls` UIDocument — no new UIDocument, no new PanelSettings.

---

## 7. Visual Effects (VFX)

| Event | VFX direction | Notes |
|---|---|---|
| Food collected | Small sparkle burst at chicken, subtle trail to base | Confirms collection, not distracting |
| Food deposited at base | Brief glow on base, food counter ticks up | Satisfying feedback |
| Attack hit | Impact burst on target, brief screen flash for receiver | Must be readable at distance |
| Stun (death) | Stars/birds circle stunned chicken | Classic cartoon read |
| Ability activation | Per-ability — distinct color accent per ability type | Must not obscure nearby characters |
| Food pile depleting | Dust/crumbs particle as pile shrinks | Subtle, continuous |
| Match end | Full-screen celebratory burst for winner | Go big here |

All VFX are local — never networked. Triggered by observed state changes.

---

## 8. Audio Direction (Preliminary)

- **Overall tone:** Cartoonish, warm, slightly chaotic. Think Looney Tunes energy — every action has a punchy sound.
- **Music:** Upbeat farm/bluegrass loop during match. Builds in intensity in the last 30 seconds.
- **SFX priorities:** Footsteps, collecting (pecking/crunching), attack (cluck/thwack), hit reaction, stun, ability activations, deposit at base, win/lose stings.
- All audio implementation via `IAudioService` — Unity Audio for demo, FMOD-ready via interface swap.
- No voice chat, no text chat.

---

## 9. Open Questions & TBD

| # | Item | Priority | Status |
|---|---|---|---|
| 1 | Final character primary colors validated in engine | High | Hex values specified in §3 — needs engine validation |
| 2 | Isometric camera angle (elevation) finalized | High | |
| 3 | ~~UI font selection~~ | ~~Medium~~ | **Resolved v0.2** — Lilita One + Nunito (§6.2) |
| 4 | Ability VFX color assignments per ability | Medium | Accent colors specified in §3 — VFX application TBD |
| 5 | Food pile mesh swap thresholds (<25%, <10%) | Medium | |
| 6 | ~~Player identity color set (color-blind safe)~~ | ~~Medium~~ | **Resolved v0.2** — Orange/Blue/Pink/Teal (§3) |
| 7 | Stunned FX — stars vs birds vs custom | Low | |
| 8 | Map prop set finalized | Low | |
| 9 | ~~Select art direction~~ | ~~High~~ | **Resolved v0.3** — Rich glossy (§6.1) |
| 10 | Gradient button implementation in Unity UGUI | Medium | Needs custom shader or two-color system (§6.9) |
| 11 | TMP font assets for Lilita One + Nunito | Medium | Import from Google Fonts, generate SDF |
