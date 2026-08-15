# Map props rescale policy — arena 38 m → 51.3 m (×1.35)

> ## ⚠️ SUPERSEDED — this document's numbers are WRONG. Read `docs/STATE.md` first.
>
> **Implemented 2026-08-14 with corrections.** The reasoning below is sound; the
> arithmetic is not. It states correctly that the boundary wall's **inner face is at
> `half`**, then uses the wall *centres* (19.25, and 25.99 = 19.25 × 1.35) in every
> derived figure. The true hard limits are **19.0** and **25.65**.
>
> | | below | actual |
> |---|---|---|
> | Authored standoff | 0.405 | **0.155** |
> | Pure-×1.35 standoff | 0.635 | **0.295** |
> | Fence line `F` | 25.34 | **25.25** |
> | Spacing (13 posts) | 3.890833 | **3.8758** |
>
> The **"walkable pocket" argument does not hold** — a chicken is 0.96 m wide, so
> neither figure could hold one. The real regression is body overhang past the visible
> fence (0.155 → 0.295 m), which still justified the fix.
>
> **The census discrepancy was not one:** 121 = root children, 136 = after recursing
> into the `FenceDressing` container. Both correct.
>
> **FenceDressing and the Silos kept the plain ×1.35 transform** — the mixed-transform
> recommendation rested on the inflated numbers above.
>
> Live rule: `MapPropsRescaler.SolveFenceLayout()`, pinned by `MapPropsRescalerTests`
> against the authored 38 m round-trip.

Context: the 2026-08-14 arena/move-speed rescale. `MapGenerator._planeSize` 38 → 51.3,
`_baseCornerDistance` 19 → 25.65. `Map.unity`'s hand-authored `MapProps` (121 children)
and `MapSurroundings` roots are positioned for the 38 m arena and nothing regenerates
them.

---

## The governing rule

**Arena-registered quantities scale ×1.35. Prop-local clearances do not.**

This is the same call already made for `_pilePositionJitter` and the pile footprints: a
clearance measured against a prop of *fixed* size is not a fraction of the arena, and
scaling it inflates a gap that was authored to be tight.

---

## Two measurements that reframe everything

1. **The generated boundary walls are invisible.** `MapGenerator.cs:57` has
   `_wallsVisible = false`, and `CreateWall` drops the renderer while keeping the
   `BoxCollider`. **The hand-authored `MapProps` fence is the only visual boundary** —
   so its registration to the invisible collider is load-bearing, not cosmetic.
2. **The collider's inner face is at `half`, not its centre.** `MapGenerator.cs:506-513`
   places each wall at `half + t*0.5` with `t = _wallThickness = 0.5`. Hard limit is
   therefore `half` itself: **19.25 old → 25.99 new**.

---

## Fence line: `F = 25.34` (rigid +6.74 shift, NOT ×1.35)

| | authored | pure ×1.35 | **decided** |
|---|---|---|---|
| Fence centre plane | 18.60 | 25.11 | **25.34** |
| Outer face (+0.245) | 18.845 | 25.355 | 25.585 |
| Hard limit | 19.25 | 25.99 | 25.99 |
| **Standoff** | **0.405** | 0.635 (+57%) | **0.405** |

Pure ×1.35 opens a 0.635 m pocket behind the visual boundary — wide enough for a chicken
to stand in and read as "outside the fence." `F = 25.99 − 0.65 = 25.34` on all four sides
(N: z=+F, S: z=−F, E: x=+F, W: x=−F).

## Fence posts: 13 per side, spacing 3.890833

Spacing falls out of corner closure — set covered extent equal to the side length so end
panels reach corner to corner:

```
spacing = (2F − W) / (N − 1)   where F = 25.34, W = 3.99 (panel width), N = 13
        = (50.68 − 3.99) / 12 = 46.69 / 12 = 3.890833

Wall_N (z = +25.34):  x_k = −23.345 + k × 3.890833,  k = 0..12
```

Other three sides by C4 rotation. **52 props total** (12 new; renumber `Wall_N_0..12`).

- Joint overlap **0.0992 m**; spacing vs authored 3.72 is **+4.6%**, imperceptible.
- **Why not 14 posts:** `Prop_wall_segment.prefab:45` sets `m_LocalScale.x = 0.35` — the
  source mesh is squashed 3× along its run. 14 posts gives 0.3985 overlap, which doubles
  **1.14 m** of original geometry per joint (~10% of the panel) as coplanar plank faces —
  a z-fighting stripe. 13 doubles only 0.28 m (2.5%), *better than what ships today*
  (authored 0.27 overlap doubles 0.77 m, 6.8%). Also 4 fewer draw calls per side.
- **Corners seal automatically.** The last panel's edge lands at exactly ±F — the centre
  plane of the perpendicular fence — buried 0.245 m into its 0.49 m depth with margin
  either side. No corner pieces, no outward tails, no special-cased end spacing.

## Prop scale stays (2,2,2) — do NOT scale props

The panel is already 2.97 m tall; ×1.35 makes it **4.01 m**. Plus the mesh is already
non-uniformly squashed (see above), so scaling compounds an existing distortion and
stretches compressed UVs.

---

## Per-family rules

| Family | Rule |
|---|---|
| **Base ×12** (Coop/Nest/Barrel) | Straight ×1.35. At 45°, ¦x¦=¦z¦ 16.19–17.21 → 21.86–23.23, inside 25.99. **Check after:** the Coop's 2.89 m collider keeps absolute size while the deposit trigger radius scales — verify it hasn't drifted out of / into the deposit ring. |
| **InnerProp ×32 / InnerWall ×32** | Straight ×1.35. Arm *lengths* scaled (`_interiorWallLengthRange` 3,6 → 4.05,8.1), so fractional-along-arm placements stay registered. **Caveat:** arm *thickness* (`_wallThickness = 0.5`) and `_interiorWallHeight = 1.1` did **not** scale, so a prop flush against an arm's **side** floats ~0.26 m off it. Props flush to an arm **end** are fine. Spot-check and pull side-leaners back along the arm normal. |
| **FenceDressing ×16** | **Mixed transform** — the ~1.6 m standoff from the fence's inner face is absolute; ×1.35 makes it 2.5 m and the scarecrows float in open field. Along-fence coord: **×1.35**. Perpendicular coord: **old + 6.74** (rigid). Now 4 per side over 45 m instead of 33.5 m — sparser; optional follow-up is +1–2 per side. ⚠️ `FenceDressing` is a **container at the origin whose 16 children carry the positions** — a direct-children-only pass misses them. |
| **Silo_A/B** (∓8.5, ±17.4) | Fence-registered (1.2 m inside the old line) → same mixed rule → **Silo_A (−11.475, +24.14)**, **Silo_B (+11.475, −24.14)**. Preserves C2 pairing. |
| **Trough_A/B** (∓6.5, 0) | Centre-island-registered, not fence-registered → straight ×1.35 → ∓8.775. Island 7×4 → 9.45×5.4, so edge clearance 3.0 → 4.05, consistent. |

**⚠️ Census discrepancy — reconcile before running anything.** The families sum to
40+12+32+32+16+2+2 = **136**, but `MapProps` has **121** children. 15 unaccounted (nesting?
miscount?). FenceDressing and the Silos take a *different* transform from everything else,
so misclassifying a prop puts it 2.5 m off the fence.

## MapSurroundings — ×1.35 on everything except the grass apron

All seven boundary-adjacent props resolve clear with no hand-fixing (Bush_1 is tightest at
1.4 m outside the fence outer face — confirm its canopy doesn't overhang F). **SurroundGrass
stays at origin / 260×260** — farthest scaled prop lands ~35 m out, well inside ±130.

Two things to eyeball after:
- **Treeline recedes** from ~6.7 m outside the old wall to ~9.1 m outside the new one, at
  unchanged size — framing gets airier. If thin, hand-add 4–6 oaks at r ≈ 30–32 rather
  than re-solving the transform.
- **Barn + Silo + Hay cluster** — if framed as a hero silhouette behind one wall, ×1.35
  shrinks its apparent size in that frame; give that cluster the rigid **+6.74** shift on
  that wall's axis instead.

---

## Fallback (pure ×1.35, if F = 25.34 is rejected)

```
F = 25.11,  spacing = (50.22 − 3.99) / 12 = 3.8525,  x_k = −23.115 + k × 3.8525
overlap 0.1375;  FenceDressing perpendicular shift becomes old + 6.51
```
Still 13 posts, corners still seal. Accepts the 0.635 m pocket behind the fence.
