# UI Frames, Cards, Pedestals, Badges — MANIFEST

Menu overhaul Phase 2 (spec: `docs/superpowers/specs/2026-10-06-menu-ui-overhaul.md`).
Authored procedurally with PIL at 4x supersampling and downsampled with LANCZOS so borders are exact.
Wood fill comes from a Flux-generated plank texture (assetforge, seed 4343), graded to the spec planks
`#8a5a2e` / `#7a4a22` and made seamless. Ink `#2a1a0c`, gold `#f5c842` / edge `#d9a521`,
cream `#fff8e8` / well `#f3e6c8`, CTA green `#7be35c` -> `#36a82c`.

9-slice borders are given as L / T / R / B in source pixels (for UI Toolkit use
`-unity-slice-left/top/right/bottom`; for a Sprite, set the same values in the Sprite Editor border).

| File | Size | 9-slice L / T / R / B | Notes |
|---|---|---|---|
| `Frame_WoodPanel.png` | 256x256 | 40 / 40 / 40 / 40 | Plank panel: 4 px ink, 7 px gold-leaf edge, 3 px inner ink, plank fill, corner nails inside the border. |
| `Frame_CtaPlank.png` | 384x128 | 48 / 36 / 48 / 40 | Green-painted plank button with gold edge, gloss band and darker 3D bottom lip; nails sit in the L/R borders. Tint white for pressed/disabled states, or swap. |
| `Frame_Ribbon.png` | 768x160 | 168 / 0 / 168 / 0 | Wood title banner with swallowtail tails. Horizontal 3-slice only: scale height uniformly, stretch the centre horizontally. Text area is the plank, about y 15-121. |
| `Card_Cream.png` | 192x192 | 40 / 40 / 40 / 44 | Cream card: 10 px transparent margin with soft drop shadow, 5 px ink outline, top highlight, bottom bevel. |
| `Card_CreamSelected.png` | 192x192 | 40 / 40 / 40 / 44 | Same geometry as `Card_Cream` (drop-in swap, no layout shift); the margin holds a gold glow and there is a 4 px gold inner edge. |
| `Pedestal_HayBale.png` | 512x256 | not sliced | Hay-bale pedestal for the hero chicken; the top face spans about y 40-96. |
| `Pedestal_Ring.png` | 512x128 | not sliced | Neutral white/grey disc with an ink outline. Tint it with the class or player colour (`-unity-background-image-tint-color`). |
| `Badge_Rosette.png` | 128x128 | not sliced | Neutral white rosette (Starter / Ready). Tint it at runtime. |
| `Tex_WoodTile.png` | 512x512 | not sliced, tiles | Seamless plank texture, horizontal boards. Use Wrap Mode Repeat. |

Particles live in `../Fx/`: `Fx_Sparkle.png` (64x64), `Fx_Feather_1..3.png` (128x128),
`Fx_Dust.png` (128x128). They are white or neutral, with alpha, and are meant to be tinted.

## Unity import settings

| Asset group | Texture Type | Sprite Mode | PPU | Filter | Other |
|---|---|---|---|---|---|
| Frames, cards, badge, pedestals | Sprite (2D and UI) | Single (set the border above) | 100 | Bilinear | Alpha Is Transparency on, Mip Maps off, Wrap Clamp, Compression None or High Quality |
| `Tex_WoodTile` | Default (or Sprite, with Mesh Type Full Rect) | Single | 100 | Bilinear | Wrap **Repeat**, Mip Maps off |
| `../Fx/*` | Sprite (2D and UI) | Single | 100 | Bilinear | Alpha Is Transparency on, Mip Maps off |
| `../Icons/Abilities/Icon_*` | Sprite (2D and UI) | Single | 100 | Bilinear | Alpha Is Transparency on, Max Size 256, Mip Maps off |
| `../Backgrounds/Bg_*` (2880x1440) | Sprite (2D and UI) | Single | 100 | Bilinear | Max Size 4096, Mip Maps off, Compression Normal (ASTC 6x6 on Android) |
