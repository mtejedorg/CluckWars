# Portrait + Landscape on Phones: Layout, Framing and Test Spec

**Status:** proposal, design only. Nothing here is built yet.
**Date:** 2026-10-21. **Author:** ui-designer agent. **Asked by:** Maestro.
**Covers:** CW-206, CW-212, CW-219, and the portrait part of CW-229.
**Decision behind it:** BACKLOG.md line 39. Phones support both landscape and portrait. Portrait shows more arena, and cross-play fairness has to be checked.

---

## 0. What the evidence shows and why

Screenshots are in `Captures/session-20261020-*/shots/` (git-ignored). The Pixel 10 Pro screen is 1080x2410.

| Shot | Defect |
|---|---|
| `session-201152/.../phone-...201455469Z-r1-mid.png` | The timer box covers the leaderboard score column. The rival edge arrow sits on the SLOW strip. The chicken fills about 35 % of the screen width. |
| `session-203530/.../phone-...203646686Z-auto2.png` | The `★ FIRST TO 40` pill sits on the "You" nameplate. The nameplate is about 50 % of the screen width and covers the head. The P3 nameplate is half hidden behind the leaderboard. The `ASSASSIN` pill at top centre stacks with the HUD. |
| `session-213904/.../phone-...214003750Z-after-resume.png` | The main menu `CLUCK WARS` title is cut at both edges. The cast row shows `RRIOR` and `ASSAS`. |
| `session-203530/.../desktop-...203648295Z-auto2.png` (960x540) | Landscape has the same goal-pill problem: the pill touches the P4 nameplate. |

There are three root causes. All of them can be fixed.

1. **The UI has no portrait rules.** `MenuUiController.UpdateLayout` already sets `layout--portrait` and `layout--landscape` on the menu root. Only one USS rule uses them (`.cw-mode-row`, CluckWarsTheme.uss:1612). The match HUD roots (MatchTopBar, TouchControls, MatchOverlays) get no orientation class at all. The top bar is one flex row (`justify-content: space-between`) with fixed-width children and no `flex-shrink`. On a 964-px-wide panel the leaderboard (about 690 px) and the timer (about 260 px) cannot both fit, so they overlap.
2. **The camera ignores aspect.** `MatchCamera._orthoSize = 5.6` is half the view *height* in every orientation. In portrait the view is 2.5 m wide either side of the chicken, which is **25 % of the ground a 16:9 player sees**. Everything world-space (nameplates, rings, chevrons) is sized in metres, so in portrait it is drawn 2.2 times larger in pixels. That is why the nameplate takes half the width.
3. **The ground area does not count the world under the HUD.** `HudControlZones` lists only the controls. A rival under the leaderboard keeps its nameplate behind the panel and gets no edge chevron. The status strip is deliberately left out of the zones (HudControlZones.cs:24), so a chevron can land on it.

**Useful fact: element sizes do not change between orientations.** `Assets/Resources/PanelSettings.asset` uses ScaleWithScreenSize, 1920x1080, match 0.5. The scale is `sqrt(W*H / (1920*1080))`, which depends only on the screen *area*. On a given phone, portrait and landscape use the same scale. The dp table in the CluckWarsTheme.uss header (1 dp = 2.35 panel px, 48 dp target = 116 px, 30 px text floor) stays valid without being re-derived. Portrait is a narrower canvas, not a smaller one.

| Screen | Panel size (panel px) | Aspect |
|---|---|---|
| 1920x1080, 960x540 | 1920 x 1080 | 16:9 |
| 2400x1080 | 2147 x 966 | 20:9 |
| 1080x2400 (and 540x1200) | **966 x 2147** | 9:20 |
| 1080x2410 (Pixel 10 Pro) | 964 x 2151 | 9:20.1 |
| 540x960 (and 1080x1920) | 1080 x 1920 | 9:16 |

The rest of this spec targets the **966-px-wide portrait column**, and every rule also has to hold at 1080 x 1920.

---

## 1. Layout rules per orientation

### 1.1 How it works in UI Toolkit (there are no media queries)

UI Toolkit has no `@media`. The usual replacement is **class switching on the panel root when its geometry changes**, and the project already does this in two places. Make that the single mechanism.

- **New shared helper:** `Assets/_Game/Scripts/UI/LayoutClasses.cs`, a static `Apply(VisualElement root, Rect panelRect)` plus a `Bind(root)` that registers `GeometryChangedEvent`. It sets exactly these classes:
  - `layout--portrait` when w < h, and `layout--landscape` otherwise. Exactly one is always set.
  - `layout--tall` when h/w ≥ 1.95 (9:19.5 and taller phones in portrait).
  - `layout--short` when h < 1100 (existing threshold, `MenuUiController.ShortLayoutHeight`).
  - `layout--narrow` when 1 ≤ w/h < 1.55 (existing 4:3 tablet rule, `NarrowLayoutAspect`).
- **Callers.** `MenuUiController` calls the helper instead of its private `UpdateLayout`. `MatchHudController` (TopBarRoot), `TouchControlsController` (TouchRoot) and `MatchOverlaysController` (OverlayRoot, replacing the ad-hoc short/narrow at :447-449) also call it. The thresholds move into the helper, so the four roots cannot disagree.
- **Rotation event.** Unity has no reliable rotation callback. The panel's `GeometryChangedEvent` *is* the rotation event for UI. The camera reads `Camera.aspect` every `LateUpdate` (it already calls `ApplyCamera()` every frame). `SafeAreaPadding` already polls `Screen.safeArea` each frame. `HudControlZones.Current()` already rebuilds when aspect or insets change.
- **Rule style.** Base rules stay landscape, which is the shipped look and avoids churn. Portrait is written as overrides: `.layout--portrait .cw-x { ... }`. Use flex and percentages (`flex-grow`, `flex-shrink: 1`, `min-width: 0`, `max-width: 100%`, `flex-wrap: wrap`) rather than new fixed pixel widths. The overlap bugs come from fixed widths with no shrink.
- **Safe area.** Every page and HUD root keeps the existing `SafeAreaPadding` host. In portrait the punch-hole is at the top and the gesture bar at the bottom. In landscape they are at the sides. All four edges are already handled. The new requirement is that nothing positioned `absolute` against the full-screen root may skip the padded host. The top bar timer and the gear button are the two to check.
- **Gutter.** 32 px page gutter in portrait menus. 16 px HUD gutter. Both are inside the safe area.

### 1.2 Main menu (`MainMenu.uxml`, CluckWarsTheme.uss §menu)

| Element | Landscape (unchanged) | Portrait rule |
|---|---|---|
| Page | column, centred | column, `padding: 32px`, `justify-content: space-evenly` |
| Title `.cw-menu-title` | 128 px, spacing 8 | **96 px, spacing 4**. This measures about 860 px for "CLUCK WARS" against 902 px available at 9:20 and 1016 px at 9:16. `white-space: nowrap`. The probe's no-clip check (§3) guards it. |
| Subtitle plate | one line | `white-space: normal; max-width: 100%` |
| Cast band `.cw-menu-cast` | 1 row, 200 px art, 26 px side margins (1008 px total, which is why it clipped) | **2x2 grid**: `flex-wrap: wrap; width: 100%`. Item `width: 50%; margin: 0 0 24px 0`. Art **200 px with `layout--tall`, 170 px otherwise**. A 2x2 grid survives localisation (a long class name) and a single row does not. |
| Play again / Solo | fixed widths | `width: 100%; max-width: 820px`, min-height 132 (≥ 116) |
| Host / Join | row | column (rule already exists, :1612) |
| Gear | top-right | top-right inside the safe area, 116x116 |
| Settings sheet | centred sheet | `width: 100%` minus gutters, `max-height: 85%`, ScrollView body (already) |

Height budget at 9:16 (1856 px available): title block 200, cast 2x(170+70+24)=528, play-again 150, solo 132, host 132, join 132, hint 70, gaps about 200. Total about 1550. It fits even with the dev row shown.

### 1.3 Class pick (`CharacterSelectClass.uxml`) and Gear Up (`CharacterSelect.uxml`)

**Class pick.** Landscape keeps the tiles column, hero and detail layout from ART.md §6.4. Portrait stacks everything in one column:

```
┌ header (back · ribbon) ───────────── 140 ┐
│ HERO stage: chicken, name, quote    ~600 │
│ 4 class tiles in one row, each ~220w 280 │   (966-48-36)/4 = 220 px ≥ 116
│ strong/weak callouts                 140 │
│ PERK A | PERK B (row, 50 % each)     220 │
│ starter chips (row)                  140 │
│ [ GEAR UP ] full width, pinned bottom 150 │
└────────────────────────────────── ≈1670 ┘
```

**Gear Up.** The slot-box row stays one row (4 x about 214 px). `GearDoorway` already hides itself when it does not fit. The deck groups stack in a column with cards wrapping. The bottom bar becomes a column: ability detail first, then READY at full width. **The deck is the only region allowed to scroll in portrait** (decision D9). Everything else stays fixed, so READY never scrolls off.

### 1.4 Lobby: THE COOP (`Lobby.uxml`) and the in-match waiting room (`MatchOverlays.uss`)

- Seats `PlayerGrid`: one row of 4 in landscape, **2x2** in portrait (each about 451 x 420).
- Rule chips row: `flex-wrap: wrap` (2 rows of 2 in portrait).
- `.cw-coop-bottom` (invite card · status · START): row in landscape, **column** in portrait. Invite card full width, then the status pill, then START full width at the bottom. The `--centred` variant's `min-width: 480px` on the centre must be dropped under portrait.
- The waiting room uses the same rules. It already receives short/narrow classes from MatchOverlaysController and will receive portrait/landscape through the helper.

### 1.5 Match HUD (portrait column 966 x 2147, 9:16 1080 x 1920)

```
 ┌───────────────── safe-area top ─────────────────┐
 │ ┌ LEADERBOARD (flex 1, ≤640) ┐  ┌ 00:29 ┐ timer │  TOP BAND
 │ │1st ● P2      ▬▬  12       │  └───────┘       │  ≈ 268 px
 │ │2nd ● You     ▬▬   8       │  ★ FIRST TO 40   │  (+ safe top)
 │ │3rd ● P3      ▬▬   5       │   goal pill       │
 │ │4th ● P1      ▬▬   0       │                   │
 │ └───────────────────────────┘                   │
 ├─────────────────────────────────────────────────┤
 │                                                 │
 │            CLEAR ARENA VIEWPORT                 │  ≈ 60 % of height
 │        (chicken at fy ≈ 0.55, see §2)           │  edge chevrons live here
 │                                                 │
 ├─────────────────────────────────────────────────┤
 │ [SLOW ▸ 280w]                     (2)           │  BOTTOM BAND
 │   ╭────╮                     (3)                │  ≈ 531 px
 │   │ ◎  │ joystick 280           (4)   (1)       │  (+ gesture bar)
 │   ╰────╯                                        │
 └─────────────── safe-area bottom ────────────────┘
```

| Element | File(s) | Landscape | Portrait rule |
|---|---|---|---|
| Top band `.cw-topbar` | MatchTopBar.uss | 330 px gradient | `height: auto`. The gradient covers the band plus 40 px. |
| Layout row `.cw-topbar-layout` | MatchTopBar.uss | space-between | **The leaderboard container gets `flex-grow: 1; flex-shrink: 1; min-width: 0; max-width: 640px`. The timer container gets `flex-shrink: 0; margin-left: 16px`.** This alone removes the timer-over-score overlap (CW-206). |
| Leaderboard row | MatchTopBar.uss | 64 h, ord 62, name 150, bar 120, score 76 | 52 h. ord 44 / 28 px. dot 32. **name `flex-grow:1; flex-shrink:1; min-width:0`** (ellipsis already). Bar `flex-basis: 72px; flex-shrink: 1; min-width: 40px`. Score 64 / 30 px. The score column is never shrunk. |
| Timer | MatchTopBar.uss | 52 px | 44 px, padding 8/24 (about 200 x 74) |
| **Goal pill `WinTargetBadge`** | **MatchTopBar.uxml** + .uss | under the leaderboard | **Moves under the timer, right-aligned, in both orientations** (decision D6). The timer container becomes `flex-direction: column; align-items: flex-end`. The pill no longer hangs into the arena, which fixes CW-212 and the landscape P4 touch. This needs a UXML move because USS cannot reparent an element. |
| Status strip `.cw-status-strip` | TouchControls.uss | left 80, bottom 380, width 380 | **width 280 (= joystick width), so its right edge is at most the joystick's right edge.** `.cw-status-value` min-width 110 → 72. It grows upward as today. |
| Joystick | TouchControls.uss | 80/80/280 | unchanged (x 80..360). The gap to hex a4 (x ≥ 451) is 91 px = 39 dp. |
| Hex arc a1..a4 | TouchControls.uss | right-anchored arc | unchanged. It spans x 451..871 and its top is at bottom 531 (with chain ring). Every hex is 150 px = 64 dp. |
| Hold hint | TouchControls.uss | bottom 560, right | unchanged (max-width 640 fits the 966 column minus the 130 px right padding). |
| Event banner (UGUI `MatchHud.cs`, or `EventBanner` in MatchOverlays.uxml) | MatchHud.cs / MatchOverlays.uss | lower third between stick and hexes | In portrait there is no lane between stick and hexes, so the banner sits **above the bottom band**: bottom = 600 px, centred, max-width 90 %. |
| Edge-cancel strips | TouchControls.uss | 104 px | unchanged (they are dp-based and only shown during a hold) |
| Perf line / F1 (`DebugHud.cs`, IMGUI) | DebugHud.cs | top-left | Dev only, outside the pass criteria. With `-cw_hud`, move the one-line perf strip to just below the top band so it stops covering row 1. |

**World-space furniture.** These are not UXML, but they are part of the same layout problem.

| Element | File | Rule |
|---|---|---|
| "You" nameplate size | (camera) | §2 framing gives the same pixels per metre as landscape, so the plate shrinks about 2.2x. Pass check: plate width ≤ 25 % of the screen width, and the plate's bottom is above the head's top. |
| Rival nameplate under the top band | `ChickenNameplate.cs`, `RivalIndicator.cs` | If a rival's body plus nameplate screen rect is ≥ 50 % inside an **info zone**, hide its nameplate and show the chevron, pinned to the zone's lower edge. This fixes P3 and `ASSASSIN` (that label appears to be a rival's world nameplate showing a class name, not HUD). The ground ring stays. |
| Info zones | `HudControlZones.cs` | Add an **info-zone list** next to the control zones: the top band rect, and the status strip rect when it has rows. The chevron clamp avoids both lists. This reverses the "status strip is not a zone" remark at :24 (decision D7). The numbers keep mirroring USS, and the existing parse test (`Phase8ChunkDRivalArrowTests`) is extended to the portrait overrides. |
| Edge chevrons | `RivalIndicator.cs` / `ScreenEdgeProjection.cs` | "Off-screen" means outside the **clear viewport** (screen minus info zones), not just outside the screen. This is also a fairness rule (§2.4). |

### 1.6 Results (`MatchOverlays.uxml` / `MatchOverlays.uss`)

- `.cw-me-panel`: row in landscape, **column in portrait**. The hero (ribbon + podium) is on top (`flex-grow: 1`, about 45 %), and the standings board is below it.
- `.cw-me-right`: `min-width: 800px; max-width: 980px` becomes **`min-width: 0; max-width: none; width: 100%`** under portrait. The 800 px floor is what would crush the hero.
- Podium: 3 pods in a row still fit (3 x about 280). The `--draw` 4-plate variant uses 26 px names (its floor is 30 px for load-bearing text, so allow ellipsis on names).
- Buttons: one row of 2, each ≥ 116 high. Leave sheet `.cw-leave-actions`: row, unchanged (2 x 439 fits).
- `GetReadyCard.uss`, leave panel, settings sheet: `max-width: 100%` minus gutters.

### 1.7 Out of scope

**Ability Lab** is a dev tool. It forces landscape while open (`Screen.orientation = LandscapeLeft`) and restores auto on exit (decision D10). No portrait USS.

### 1.8 Files that change (implementation is not part of this proposal)

| Area | Files |
|---|---|
| Orientation classes | **new** `Assets/_Game/Scripts/UI/LayoutClasses.cs`. `MenuUiController.cs`, `MatchHudController.cs`, `MatchOverlaysController.cs`, `Input/TouchControlsController.cs` |
| Styles | `Assets/UI/Styles/CluckWarsTheme.uss`, `MatchTopBar.uss`, `TouchControls.uss`, `MatchOverlays.uss`, `GetReadyCard.uss`. `CluckWarsTokens.uss` gets the gutter tokens. |
| UXML | `Assets/UI/MatchTopBar.uxml` (goal pill into the timer column). Menu UXML should need **no** structural change: MainMenu, CharacterSelectClass, CharacterSelect and Lobby are all reachable with USS overrides. Confirm during build. |
| World-space / camera | `Visuals/MatchCamera.cs` + **new** `Visuals/OrientationFraming.cs` (pure maths, testable), `HudControlZones.cs`, `RivalIndicator.cs`, `ChickenNameplate.cs`. `MatchHud.cs` (banner position). `DebugHud.cs` (perf line). |
| Orientation policy | `ProjectSettings/ProjectSettings.asset` (only if D5 turns upside-down off). A small `OrientationPolicy` (round lock, D4) in `UI/` or `Gameplay/`. |
| Tests that pin USS text and must be updated | `Phase6Chunk6Tests`, `Phase6Chunk7dTests`, `Phase7ChunkBTests`, `Phase7ChunkC1Tests`, `Phase7ChunkC2Tests`, `Phase8ChunkDRivalArrowTests`, `AbilityPreviewRulesTests`, `PlayerPaletteTests`. New `OrientationFramingTests` and `LayoutClassesTests`. `ArenaRevealGateTests` already covers 9:16 for the warm-up framing. |
| Tooling | **new** `tools/ui_capture/capture_orientation.py`. `tools/test-session/scenario-pc.ps1` (`-Width/-Height` passthrough), `verify.ps1` (UI offenders = 0). |
| Stale docs to fix after build | CluckWarsTheme.uss header ("landscape-locked, autorotate portrait = 0" is false). `docs/STATE.md` ~:1574 and :2573. The coop-test skill's "landscape-locked". `docs/ART.md` §6 (add portrait layouts). Check `docs/site/index.html` if it quotes the camera size. |

---

## 2. Camera framing rule (cross-play fairness)

### 2.1 Geometry

The camera is orthographic, pitched 30°, with half-height `s` (orthoSize) and aspect `a = W/H`. A ground point d metres "ahead" projects to view height d·sin30° = d/2. So:

- ground **lateral** half-extent `L = s·a`
- ground **depth** half-extent `D = 2s`
- visible ground area `G = 4·L·D = 8·s²·a`
- pixels per view-metre `p = H_px / 2s`. On a fixed screen, equal ground area is the same as equal p, so the chicken and its nameplate are the same pixel size.

### 2.2 Where we are today (s = 5.6 everywhere)

| Screen | L (m) | D (m) | G (m²) | vs PC 16:9 | farthest visible ground (corner) |
|---|---|---|---|---|---|
| PC 16:9 | 9.96 | 11.2 | 446 | 100 % | 15.0 m |
| Phone landscape 20:9 | 12.44 | 11.2 | 557 | **125 %** | 16.7 m |
| PC 21:9 | 13.07 | 11.2 | 585 | 131 % | 17.2 m |
| **Phone portrait 9:20 (today)** | **2.51** | 11.2 | **112** | **25 %** | 11.5 m |

Portrait today is a severe disadvantage, not an advantage. Wide landscape phones already see 25 % more than PC.

### 2.3 Proposed rule

```
A*   = 446 m²   ground area of the PC 16:9 baseline (s = 5.6), the framing the game was tuned and audited on
Lmin = 5.6 m    lateral half-extent floor in portrait = longest ability reach (StealRange / AuraRadius 5.4 m) + 0.2
Smax = 12.5     portrait cap
fy   = 0.5 + (B − T)/2   chicken's viewport height, where T and B are the top and bottom HUD band fractions (info + control zones)

landscape (a ≥ 1):  s = 5.6 · sqrt((16/9) / a)              equal ground area          [D2: or keep 5.6]
portrait  (a < 1):  s = min(Lmin / a, Smax)                  lateral 5.6 m, capped
both:               camera focus offset so the chicken sits at fy instead of 0.5
nearClip = min(−6, distance − 1.732·s − 6.2)                  the bottom edge must not clip (risk, see 2.5)
```

Numbers this produces. `OrientationFramingTests` should pin these.

| Screen | s | L | D | G | vs A* | corner reach | p on the device |
|---|---|---|---|---|---|---|---|
| 16:9 (1920x1080, 960x540) | 5.60 | 9.96 | 11.20 | 446 | 100 % | 14.99 m | 96 px/m @1080 |
| 20:9 (2400x1080) | 5.01 | 11.13 | 10.02 | 446 | 100 % | 14.98 m | 108 px/m |
| 21:9 | 4.89 | 11.41 | 9.78 | 446 | 100 % | 15.03 m | — |
| 4:3 tablet | 6.47 | 8.62 | 12.93 | 446 | 100 % | 15.54 m | — |
| 9:16 (540x960) | 9.96 | 5.60 | 19.91 | 446 | 100 % | (clear band, see below) | — |
| 9:20 (1080x2400) | 12.44 | 5.60 | 24.89 | 557 | 125 % | (clear band) | 96 px/m @2400 |
| Pixel 10 Pro 1080x2410 | 12.50 | 5.60 | 25.0 | 560 | 126 % | (clear band) | 96 px/m (same as its landscape today) |
| 9:21 | 12.50 (cap) | 5.36 | 25.0 | 536 | 120 % | (clear band) | — |

Two properties make this rule simple to defend.

- **In landscape, equal area also means equal sight radius.** Every landscape aspect from 16:9 to 21:9 ends up with a farthest visible ground point of 15.0 ± 0.05 m (4:3 reaches 15.5).
- **On a 9:20 phone, the clear viewport in portrait is exactly the same 15 m.** Ground depth per screen height is 4s = 49.8 m. The HUD bands are T ≈ 0.15 (268 px + safe top) and B ≈ 0.25 (531 px). That puts the chicken at fy ≈ 0.55. The clear band then reaches (0.85 − 0.55)·49.8 = **14.9 m ahead** and (0.55 − 0.25)·49.8 = **14.9 m behind**. At 9:16 it is 11.6 m each way. The extra depth a portrait player gets falls under the HUD bands.

### 2.4 Is it fair across platforms?

1. **Global information is identical in every orientation.** Scores, timer, goal, and an edge chevron for every rival outside the *clear viewport*. A rival hidden under the leaderboard counts as off-screen and gets a chevron (§1.5). This is a hard invariant: nobody loses a rival's bearing because of where their HUD sits.
2. **Local detail is bounded by the same 15 m sight radius.** No orientation shows unoccluded ground farther than about 15 m from its own chicken. Landscape shows a wide, shallow window (±10–11 m lateral, ±10–11 m deep). Portrait shows a narrow, deep strip (±5.6 m lateral, ±15 m deep in the clear band). The total clear area in portrait (about 11.2 x 30 = 336 m²) is **less** than landscape's 446 m². Portrait trades flank vision for sightlines toward the centre and toward your own base. That is a style trade-off, not dominance, and it is the player's choice.
3. **Every ability target in range is on screen.** The lateral floor of 5.6 m is at least the longest reach of 5.4 m, so a portrait player never aims at an in-range rival they cannot see. Pure equal area would give 5.0 m at 9:20, which fails this test, and that is why the floor exists.
4. **What portrait still gets for free.** The bottom band is not opaque: the arena shows between the stick and the hexes. A portrait player can see up to about 27 m behind (toward their own base) through those gaps. The top band is mostly panel, so that leak is small. This is why the cap question is decision D3:
   - **(a) Recommended for the first build: no shader.** Rely on the band geometry above. The leak is small, it points toward your *own* base, and arrows already show raiders.
   - **(b) Strict: a 15 m sight veil.** A ground-projected haze ring around the local chicken, fading to 70 % past 15 m. Rival nameplates are hidden past 15 m and chevrons take over. It costs one transparent quad (overdraw on about 40 % of a portrait screen and about 0–3 % of landscape) plus a RivalIndicator rule. Owners: shader-artist and render-pipeline-artist. It also makes the rule exact on 9:21 and 4:3.
   - **(c) Hard zoom cap** (s ≤ 7.5, so D ≤ 15 m with no veil). **Not recommended.** It gives L = 3.4 m in portrait, less than the 5.4 m reach, and portrait shrinks to 45 % of the landscape area. That reverses the unfairness.
5. **Wide landscape phones (D2).** Today they see 25 % more than PC players. Equal area zooms a 20:9 phone in by 11 % (s 5.6 → 5.0) and removes that surplus. This is the only change landscape players will notice.

### 2.5 Render risks the framing creates (render-pipeline-artist)

- **Near clip.** At s = 12.5 and distance 15, the bottom-edge ground sits at view depth 15 − 1.732·12.5 = −6.65 m. That is past the current `_nearClipPlane = −6`, so ground and trees would slice. Use the near-clip formula in §2.3 (≈ −13 m), or scale `_distance`.
- **Shadows.** The far edge of the clear band (15 m ahead) is at view depth 15 + 0.866·15 = 28 m. That is under Mobile `m_ShadowDistance: 30` and PC 35. Ground under the top band (up to 34 m) loses shadows, and that ground is covered anyway. Do not raise shadowDistance.
- **Cost.** Portrait 9:20 renders 25 % more ground than A*. Mobile measured GPU at about 3 ms (CW-211), so there is headroom. Re-measure on the Pixel 9 (CW-229).
- `FenceFadeRules.TargetHoleOnScreen` and `ArenaWarmupFraming` already take aspect into account. They need re-capturing, not re-design.

---

## 3. PC test plan (no phone)

### 3.1 Why a PC test is valid

Panel layout depends only on aspect (§0), and so does camera framing (§2). So **540x1200 is pixel-for-pixel the same layout as 1080x2400**, only softer. This matters because 2400-tall windows do not fit on the 3840x2160 monitor.

### 3.2 Matrix

| # | Size | Panel | How to run | Purpose |
|---|---|---|---|---|
| M1 | 540x960 | 1080x1920 | standalone `start.ps1 -Width 540 -Height 960`, or Game View fixed size | 9:16 portrait |
| M2 | 960x540 | 1920x1080 | standalone (the scenario default) | landscape regression |
| M3 | 1080x2400 | 966x2147 | **Editor Game View fixed resolution 1080x2400** + MCP `screenshot-game-view` (it reads the full-size RT). Standalone stand-in: **540x1200**. | 9:20 portrait |
| M4 | 2400x1080 | 2147x966 | standalone (fits a 3840-wide screen) or Game View | 20:9 landscape |
| M5 | 1920x1080 | 1920x1080 | standalone / Game View | PC baseline |
| + | 1080x2410 (540x1205), 1440x1080, 2560x1080 | — | Game View | Pixel exact, 4:3, 21:9 spot checks |

### 3.3 Screens per size

Main menu (plus settings sheet open), class pick, Gear Up, COOP (2 and 4 seats), waiting room. In-match, three captures: GET READY (YOU emphasis at 1.45x, the largest nameplate state), mid-round with SLOW applied and a rival off-screen and a rival under the top band, and the hold hint showing. Then results (win and draw) and the leave sheet. The menu captures are about 9 shots per size, and the full set is about 14 shots x 5 sizes.

### 3.4 Tooling (existing plus small additions)

- **Existing:** `tools/ui_capture/bridge.py` (MCP JSON-RPC over HTTP, works when the tool list is missing). `capture_menu.py`, `capture_phase6_hud.py`, `capture_rival_arrows.py`, `capture_postmatch.py` already drive each page and HUD state. MCP `screenshot-game-view`. `tools/test-session/start.ps1` / `scenario-pc.ps1` (`-screen-width/-height`, `-cw_autoplay`, `-cw_hud`), `shot.ps1 -Desktop`, `verify.ps1`.
- **New `capture_orientation.py`.** For each size it sets the Game View size (reflection on `GameViewSizes`, through script-execute), enters Play, and reuses the page drivers above. After two frames (so the classes have settled) it calls `UiLayoutProbe.Audit()` and writes `Captures/orient-<date>/<size>/<screen>.png` plus `.json`.
- **New `UiLayoutProbe`** (Diagnostics, runtime, dev-only, the same family as `VisualProbe`). It walks every UIDocument root and checks the criteria below. With `-cw_hud` it also runs in standalone builds and logs `ui offenders=N`, which `verify.ps1` gates on.
- **Safe-area simulation.** Route `SafeAreaPadding` and `HudControlZones.SafeInsets` through one provider with a dev override `-cw_safearea l,b,r,t` (viewport fractions). Test portrait with `0,0.02,0,0.055` (Pixel punch-hole and gesture bar) and landscape with `0.055,0.02,0,0`. Unity's Device Simulator is a manual cross-check only, because MCP cannot screenshot it.
- **Rotation on PC.** Mid-session `Screen.SetResolution(540,960)` via script-execute, or a Game View size change during Play, from M2 to M1 and back. Expect the classes to flip on the next `GeometryChangedEvent`, `s` to change the same frame, and chevrons to rebuild (the zone cache is keyed on aspect).
- **Contact sheet.** Put all PNGs side by side for a human pass, game-audit style.

### 3.5 Pass criteria

| ID | Check | Threshold |
|---|---|---|
| U1 | No overlap between HUD clusters: leaderboard, timer column, status strip, joystick zone, each hex (with ring pad), hold hint, event banner. Menus: no two Buttons or Labels overlap unless one contains the other. | intersection area 0, gap ≥ 8 px |
| U2 | Touch targets: every Button, hex, joystick base, tile, card and seat | ≥ 116 x 116 panel px (48 dp). Adjacent targets ≥ 19 px apart (8 dp). |
| U3 | No clipped text: every visible Label fits its content box (measured text width ≤ content width), or is on the ellipsis allow-list (leaderboard and podium names). No Label partly outside the panel or safe rect. Title and cast names must be whole. | 0 offenders |
| U4 | Type floors from `resolvedStyle.fontSize` | HUD ≥ 28 px, menu load-bearing ≥ 30 px |
| U5 | Every interactive or informational element inside the simulated safe area | 0 offenders |
| U6 | Each UIDocument root has exactly one of `layout--portrait` / `layout--landscape`, and it matches the aspect | — |
| W1 | Local nameplate (normal and 1.45x GET READY) | width ≤ 25 % of the screen. Its rect does not intersect the head (top 25 % of the chicken's screen bounds). |
| W2 | No rival nameplate and no chevron intersects any control or info zone | 0 |
| W3 | Every rival outside the clear viewport has a chevron. No chevron for a rival inside it. | exact |
| C1 | Camera numbers logged per capture (`MatchCamera.VisibleGround` L, D, G, fy) match the §2.3 table | ±1 % |
| C2 | Near-plane check: the ground ray at viewport (0.5, 0.01) hits the ground (not clipped) | hit |
| C3 | Shadows visible in the clear band (human check on PNG) | yes |
| R1 | Runtime rotation: re-laid out within 1 frame. Joystick and hexes return to rest (no stuck held state). | — |

---

## 4. What still needs a device (Pixel 10 Pro / Pixel 9, CW-229)

- **The real safe area.** Punch-hole size and position, the gesture bar, rounded-corner clipping, and the rotated insets (the cutout moves to a side in landscape). The `-cw_safearea` override only approximates these.
- **Touch ergonomics in portrait.** Two-thumb reach to hex a2 at 531 px from the bottom, joystick comfort at arm's length, the edge-cancel strips against the grip, and accidental palm touches on the bottom band.
- **Real rotation.** The sensor flip when the phone lies flat (the CW-219 trigger) and the Android configuration change mid-session. Check that Fusion and UGS survive a rotation in the waiting room. Check the round lock (D4) holds through GET READY, the round and results.
- **Legibility at physical size.** 96 px/m chicken and nameplate, and 28–30 px type. The dp maths says this is fine, but eyes decide.
- **Performance.** +25 % ground in portrait at a 30 fps cap. Thermal. Mobile tier with Performance Mode on and off.
- **Haptics and pad on phone.** Already on CW-229 and unchanged here.

---

## 5. Staged build plan

Owners follow the routing table. Estimates are focused agent-days, plus one device session.

| Stage | Content | Owner | Est. |
|---|---|---|---|
| S0 | `LayoutClasses` helper wired into the 4 roots. Orientation policy (D4/D5). `-cw_safearea` override seam. No visual change. | senior-dev | 0.5 d |
| S1 | `OrientationFraming` (pure) + `MatchCamera` (s, fy offset, near clip) + `OrientationFramingTests` with the §2.3 table. This fixes the nameplate size and the tight framing. | senior-dev (+ render-pipeline-artist review of near/shadow) | 1 d |
| S2 | Match HUD portrait: MatchTopBar uxml/uss, TouchControls.uss, banner. Info zones in `HudControlZones`. RivalIndicator/Nameplate band rule. Update the pinned USS tests. | senior-dev from this spec, ui-designer review | 1.5 d |
| S3 | `UiLayoutProbe` + `capture_orientation.py` + `verify.ps1` gate. First full PC matrix, run before S4 so the menu work is measured. | tester + senior-dev | 1 d |
| S4 | Menus portrait: main menu, class pick, Gear Up, COOP and waiting room, results, sheets, GET READY card. Ability Lab landscape force. | senior-dev, ui-designer review | 2 d |
| S5 | Re-run the PC matrix to green. Device pass on Pixel 10 Pro + Pixel 9 (§4). Fix round. | tester / coop-test | 0.5 d + session |
| S6 (optional, D3b) | 15 m sight veil | shader-artist + render-pipeline-artist | 1–1.5 d |
| S7 | Docs: ART.md §6 portrait, theme header contract, STATE, coop-test skill. Site check. | code-architect | 0.25 d |

**Total: about 6.75 days without the veil, about 8 with it.** S1 alone fixes the worst device symptom (the giant nameplate and 25 % view). S1 and S2 can ship for a device check before the menus.

---

## 6. Decisions for Maestro

| # | Question | Options | Recommended |
|---|---|---|---|
| D1 | Visible-area rule | (a) equal area to PC 16:9 in landscape, portrait lateral fixed at 5.6 m (capped at s = 12.5). (b) pure equal area everywhere (portrait lateral 5.0 m < 5.4 m reach). (c) keep s = 5.6 (portrait sees 25 %). | **(a)** |
| D2 | Apply equal area to wide landscape phones (20:9 sees +25 % today) | yes (zooms in 11 %) / no (keep 5.6 for landscape) | **yes**: one rule for everyone |
| D3 | Cap the sight radius at 15 m | (a) band geometry only, no shader. (b) add the 15 m veil. (c) hard zoom cap. | **(a) now**, revisit (b) after a mixed playtest |
| D4 | Rotation during a round | lock the orientation from GET READY to results, auto in menus and waiting room / allow live rotation | **lock** (the flat-phone flip is the CW-219 trigger) |
| D5 | Portrait upside-down | off / on | **off** |
| D6 | Goal pill under the timer in **both** orientations | both / portrait only | **both** (also fixes the landscape P4 touch) |
| D7 | Status strip becomes a chevron avoid-zone (reverses the Phase 8 D remark) | yes / no | **yes** |
| D8 | Rival under the top HUD band: hide its nameplate, chevron at the band edge | yes / no | **yes** |
| D9 | The Gear Up deck may scroll in portrait (relaxes "no page scroll") | yes / no | **yes**, deck only |
| D10 | Ability Lab is landscape-only | yes / no | **yes** |
| D11 | Settings > Orientation (Auto / Landscape / Portrait) | now / later / never | **later** (after D4 ships) |
| D12 | Main-menu cast in portrait | 2x2 grid / one row of smaller birds | **2x2** |
