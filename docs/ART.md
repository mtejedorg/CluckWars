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
