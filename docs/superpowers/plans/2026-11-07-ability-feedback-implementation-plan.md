# Ability feedback, state cues and icons — implementation plan

**Date:** 2026-11-07 · **Branch:** `simplify-abilities` · **Spec:** `docs/superpowers/specs/2026-11-06-ability-feedback-and-icons-design.md` (§12 holds Maestro's recorded decisions, which bind every item below) · **Evidence:** `Captures/feedback-2026-11-06/` (lab captures, contact sheets, review page) · **Capture script:** `tools/ui_capture/capture_feedback_lab.py` (re-audits reuse it unchanged).

## 0. Re-audit of the findings under the decisions (self-review, 2026-11-07)

What the eight answers change, checked against the code before planning:

| Finding (page) | Under the decisions | Verified |
|---|---|---|
| F01 hidden bird keeps its rings, bar and plate | Unchanged, top of batch A. D4b keeps the buff ring, so the fix is a gate, not a removal: one predicate (`ControlStateVFX.IsFadedOpacity`) applied by every world-space tell. | `ControlStateVFX.cs:286-310` has no opacity read; `ChickenWorldBars`, `ChickenNameplate`, `ChickenStatusBadges` read none either (lens claim, confirmed by the capture `hide_3_hidden.png`). |
| F02 egg has no body; walk reads as slowed | Unchanged. D2a fixes the words (Egg Shell / EGG); the egg visuals are batch D. | capture `shield_3_walk.png` (egg phase) |
| F03 one ring for three self-states | D4b: the ring stays as a secondary, opacity-gated cue; each self-state gets its own body cue (streaks, egg, ghost). The "most-recent slot only" rule stays (secondary cue, acceptable). | `UpdateBuffRing` uses `MostRecentActiveSlot` |
| F04 accent collisions | D3a: the §6 retune lands as data in a new `FeedbackPaletteSO`; the ability assets reference palette keys instead of carrying literals. | accents read in 10 call sites; canonical colours read in 7 files (`FeedbackTuning` consts) |
| F05 icons | D6a + D1: six redraws land (Steal, Stun, Shove, Slow, Jump, Egg Shell) plus the paw for Hunt (D5b); Speed keeps the bolt because the stun badge becomes stars; Peck tweak optional. | sheets in `docs/design/feedback-icons-2026-11-06/` |
| F06 names and labels | D1 later: nothing renamed except Egg Shell (D2a) and Hunt / HUNT / POUNCE (D5b). Push and Zoom are void. | — |
| F07 exits are not events | Unchanged (star drop-off, poofs, hatch). | — |
| F08 aura motion | Unchanged. | capture `slow_3_impact.png` (edge already cyan) |
| F09 thief shake | Unchanged (a `FeedbackTuning` value, no decision). | `CasterStealShakeMagnitude` 0.18 |
| F10-F13 steal arc, speed streaks, jump beats, caster stun digit | Unchanged; pops per D7b: `+n`, `ROB -n`, `FAST!`, `HOP!`. | — |
| F14 slow on a walker previews as a hit | Unchanged, batch A. | STATE 2026-11-05 item 4 |
| F15 audio | Open; batch E brief now includes shell close / hatch and a hunt sting. | `AudioRegistrySO` has no such fields |
| F17 `ChickenVFX._stunPS` | Still unverified; check in D10 before deleting. | — |
| New: Hunt needs an icon and a badge | D5b: paw print (`alts/Hunt_paw.png`, `states/Glyph_Hunting_paw.png`, both reviewed at 24 px); the hunter's marker uses the paw; the victim keeps MARKED! and the skull marker for now (tune with the definitive names). | sheet `hunt_icon_options.png` |
| New: pop vocabulary doubles | D7b: ten new catalog rows; self-pops gated on `HasInputAuthority` so POOF! never reveals a ghost. | `UiText.csv` rows 209-214 today |

Interpretation of the two custom answers: **D3a** means no colour literal is allowed to decide a state or ability hue any more; the palette asset is the single source and tests pin the "ability wears the state it inflicts" rule. **D5b** is UI-only: `MarkKillAbilitySO`, `AssassinExecute`, `MarkFeedback` and the docs' internal name stay "Death Mark".

## 1. Rules for every item

- Branch `simplify-abilities`; one commit per verified item with explicit paths; `docs/STATE.md` batch table updated after each item.
- Conventions: VFX local and observed from replicated state, never RPC'd; `ILogService` only; `HasStateAuthority` gates; `LogLevel` alias where Fusion is imported; URP only; no per-instance materials; constants in `FeedbackTuning`, colours in the palette asset.
- No gameplay value changes. Nothing here touches cooldowns, reach, durations or the kits.
- Verification per item: EditMode suite green (batch run; the mound render test is live-Editor flaky), then a lab capture with `capture_feedback_lab.py` for anything visible, compared with the 2026-11-06 frames.
- Orchestrator: `code-architect`; implementers `senior-dev` (C#), `ui-designer` (UXML/USS, badges), `vfx-artist` / `shader-artist` (batch D), `artist-2d` (icon files), `audio-designer` (batch E). Never `fable`.

## 2. Batches

### Batch A — bugs and no-decision fixes (code only, Editor open)

| # | Item | Files | Fix | Verify |
|---|---|---|---|---|
| A1 | Hidden bird leaks every world tell | `Visuals/ControlStateVFX.cs` (buff ring), `ChickenWorldBars.cs` (identity ring, cargo bar), `ChickenNameplate.cs`, `ChickenStatusBadges.cs`, `ChickenVFX.cs`, `HitFeedback.cs` (sparks on a faded victim), `FloorRivalArrows.cs` if it draws for faded rivals | Every renderer multiplies its alpha by the bird's `VisualOpacity` or hides when `IsFadedOpacity`; the two Hide poofs (D9) are the only exception. Local bird: opacity floor 0.45 in `ChickenVisuals` (D9) | New `HiddenBirdTests`: with `VisualOpacity` 0.2 no ring, badge, plate or particle is enabled on a non-authority peer; lab capture `hide_3_hidden` shows a bare ghost |
| A2 | Slow aimed at a slow-immune walker previews as a hit | `Visuals/AbilityTelegraph.cs` / `TargetHighlight.cs`, `Gameplay/AbilityActivationRules.cs` (`WouldAffect`) | A slow-immune target is `NoEffect` (grey ⃠) for any slow source | `AbilityPreviewRulesTests` case; lab: cast Slow at a walking dummy |
| A3 | Thief shake louder than the victim | `Visuals/FeedbackTuning.cs` | `CasterStealShakeMagnitude` 0.18 → 0.08, duration 0.22 → 0.15 | existing tuning tests; capture `steal_2_cast` |
| A4 | Caster-side stun countdown | `Input/TouchControlsController.cs`, `UI/HudFeedbackStyle.cs` | While a stun cast by the local bird is live on any victim, the Stun hex centre shows the largest remaining stun as a digit (reads victims' `StunRemaining`) | HUD test; capture `stun_3_impact` |

### Batch B — palette, words, catalog (data + small code)

| # | Item | Files | Fix | Verify |
|---|---|---|---|---|
| B1 | `FeedbackPaletteSO` (D3a) | new `Visuals/FeedbackPaletteSO.cs` + `Data/FeedbackPalette.asset`; `Installers/ProjectInstaller.cs` (serialized reference, pushes `FeedbackPalette.Current` like `AbilityTargeting.Mode`); `Visuals/FeedbackTuning.cs` (`CanonicalStunColor`, `CanonicalSlowColor`, `CanonicalKnockColor`, `NeutralNoEffectColor`, `InkColor`, cream, gold become properties reading the palette, with today's values as the built-in default when no palette is bound); `Abilities/AbilityBaseSO.cs` (`AccentKey` enum field: Custom, Steal, Stun, Shove, Slow, Speed, Jump, EggShell, Hide, Peck, Hunt; `AccentColor` becomes a property: key → palette, Custom → the serialized literal, kept for retired assets) | Palette values: Stun = stun yellow (1, 0.85, 0.2), Slow = slow cyan (0.3, 0.75, 1), Shove (1, 0.55, 0.15), Speed (1, 0.9, 0.55), Hide (0.6, 0.6, 0.75), Egg Shell (0.98, 0.94, 0.86), Steal (1, 0.3, 0.1), Jump (0.4, 0.2, 0.9), Peck (0.96, 0.8, 0.3), Hunt (0.9, 0.1, 0.2); set the ten active assets' `AccentKey` | New `FeedbackPaletteTests`: every active ability has a non-Custom key; Stun's accent equals the stun state colour and Slow's the slow colour; all palette colours pairwise ΔE00 ≥ 12 and CVD-separated from the four player colours (reuse `AbilityPaletteTests` helpers); the installer binds the asset. Lab capture of all four targeted casts |
| B2 | Egg Shell words (D2a) | `Data/Abilities/TurtleMode.asset` (DisplayName "Egg Shell", ShortLabel EGG), `Data/Abilities/EggShell.asset` (DisplayName "Egg Shell (old)"), `Resources/Text/UiText.csv` description key, `docs/GDD.md` 7.2, `docs/site/mechanics.html` | Words only; no numbers | `AbilityDescriptionTests`, `UiTextTests`; loadout screenshot |
| B3 | Hunt words (D5b) | `Data/Abilities/MarkKill.asset` (DisplayName "Hunt", ShortLabel HUNT), `UiText.csv` `mark.hex.kill` → POUNCE, new `hud.pop.hunt` HUNT!, `UiKeys.cs`; GDD 6.4 note "UI name Hunt, internal Death Mark"; site | UI words only; code names untouched; MARKED! stays | tests as above; mark HUD capture with the Assassin loadout (`capture_feedback_lab.py` needs a class swap step: add `RespawnPlayerAs(Assassin)`) |
| B4 | Pop catalog (D7b) | `UiText.csv`, `UiKeys.cs`, `Visuals/FloatingCombatText.cs` (self-pop variant gated on `HasInputAuthority`) | Rows: `hud.pop.waddle` WADDLE, `hud.pop.egg` EGG!, `hud.pop.hatch` HATCH!, `hud.pop.poof` POOF!, `hud.pop.fast` FAST!, `hud.pop.hop` HOP!, `hud.pop.hunt` HUNT!, `hud.pop.robbed` ROB -{n}, `hud.pop.grabbed` +{n}, `hud.pop.push` PUSH (victim). IMMUNE / SAFE / STUCK! untouched (D8b) | `UiTextTests` (keys ↔ CSV); a test that self-pops are not spawned on a non-authority peer |

### Batch C — icons and badge sprites (files + UI code)

| # | Item | Files | Fix | Verify |
|---|---|---|---|---|
| C1 | Ability icons (D6a, D5b) | copy `docs/design/feedback-icons-2026-11-06/abilities/Icon_{Snatch,WingSlam,CluckShock,FeatherAura,Shadowstep,TurtleMode}AbilitySO.png` over `Assets/_Game/Art/UI/Icons/Abilities/` (same names, `.meta` untouched); `alts/Hunt_paw.png` → `Icon_MarkKillAbilitySO.png`; optional `Icon_PeckAbilitySO.png` | File swap only; `AbilityIconStyle` and the USS rules unchanged | `AbilityIconArtTests` green; HUD + loadout captures at phone size |
| C2 | State glyph sprites | new `Assets/_Game/Art/UI/Icons/States/Glyph_{Stunned,Slowed,KnockedBack,ShieldWalk,ShieldEgg,Hidden,Speeding,Hunting(paw),Safe,PeckChain}.png` from `docs/design/.../states/` (Marked keeps `Glyph_Skull`); import Sprite 2D/UI, no mipmaps, max 256 | New `StateGlyphArtTests` (one file per `HudStatusKind` / state, import profile) | tests |
| C3 | Badges use sprites | `UI/HudFeedbackStyle.cs` (`StunGlyph` etc. → sprite classes), `Assets/UI/Styles/TouchControls.uss` (`.cw-status-glyph` as a VisualElement with background-image), `Input/TouchControlsController.cs` (strip rows), `Visuals/ChickenStatusBadges.cs` + `ChickenNameplate.cs` (TextMesh glyph → SpriteRenderer), `Assets/UI/Styles/MarkHud.uss` (`.cw-mark-shell-glyph` → `Glyph_ShieldEgg`, hunter marker → paw) | Shape coding stays (stars orbit in world, static in the badge) | `HudFeedbackStyleTests`, captures of a stunned + slowed dummy at phone size (24 px check) |

### Batch D — VFX (Editor open; order = smallest risk first)

| # | Item | Files | Fix (spec §4-§5 details) | Verify |
|---|---|---|---|---|
| D1 | Textured particles | new `Art/Materials/Fx_Particles.mat` (URP Particles/Unlit, alpha blend, vertex colour), `Art/UI/Fx/Fx_Atlas.spriteatlas`; `Visuals/ChickenVFX.cs` uses the material + texture-sheet sprites | Unlocks every sprite effect; caps per spec §3.7 (≤ 64 live particles, ≤ 7 systems per chicken) | profiler stats in the lab; existing VFX tests |
| D2 | Egg Shell body | new `Visuals/ChickenShellVisuals.cs` (sibling of `ControlStateVFX`), `Art/Meshes/Fx/EggShell.fbx` halves, placeholder URP Lit; phases derived from the Shield slot + `IsControlImmune`; walk = cup rising to 35 % + waddle (±6°, 2.2 Hz via `ChickenAnimator` bank) + heel dust; close 0.30 s; idle wobble/breathing/jitter; open 0.40 s with `Fx_EggShard_1..3`, feathers, gold puffs; scale `min(ClassScale, 1.24)`; cargo bar and nameplate stay above | pops WADDLE / EGG! / HATCH! (D7b) | `ShellVisualsTests` (phase edges from timers, scale cap under the 1.70 nameplate, no stun overlay during the egg); lab captures `shield_2..7` |
| D3 | Egg shader | new `Art/Shaders/CW_EggShell.shadergraph` (URP Lit, Cull Off; `_StripeColor` player bands at 30 % / 70 %; `_CrackTex` = `Fx_EggCrackMask`, `_Crack` 0..1; `_SpeckleTex` = `Fx_EggSpeckle`; fresnel ink rim), `Art/Materials/Egg_Shell.mat`; MPB per chicken | cracks 40 → 90 % of the egg's life | shader compiles on PC and Mobile tiers (no pink); capture both tiers |
| D4 | Speed body cue | `ChickenVFX.cs` (hip-height stretched `Fx_SpeedLine` streaks ≤ 24, stride dust, feet sparkles), `ChickenAnimator.cs` lean | opacity- and Reduced-Motion-gated; pop FAST! | capture `speed_3_impact` |
| D5 | Jump beats | `ChickenVFX.cs` / `ControlStateVFX.cs` (launch dust + feathers, 5 arc sparkles along the chord, landing dust after the settle) | pop HOP!; never the hit flash | capture `jump_2/3` |
| D6 | Shove beats | `ChickenVFX.cs` (victim skid dust 0.3 s, caster sparkles), `ChickenAnimator.cs` squash (check the recoil first, single-write) | victim pop PUSH | capture `shove_2/3` |
| D7 | Steal beats | `HitFeedback.cs` (grab lines on the impact LineRenderers), new `Visuals/FoodChunkFlight.cs` (pooled `Fx_Corn` movers victim → thief, rerouted up if the thief is faded) | pops +n / ROB -n | capture `steal_2/3` |
| D8 | Slow aura | `AbilityTelegraph.cs` / `AbilityZoneVisuals.cs` (scrolling dash on the aura polyline, `Fx_AuraEdge` band alpha 0.20), `ChickenVFX.cs` feather-fall ≤ 24 world-space + caster feather puffs | no filled disc | capture `slow_3/4` |
| D9 | Hide beats | `ChickenVFX.cs` (enter / break poofs on all peers), `ChickenVisuals.cs` (local floor 0.45), `ControlStateVFX.cs` (local dashed ring), badge HIDDEN row (C3) | pop POOF! (self only) | capture `hide_2/3` on a second client (`tools/run-clients.ps1`) to prove the remote view |
| D10 | Stun beats | `ChickenStateOverlays.cs` (star pop-in / drop-off), `ChickenVFX.cs` (caster `Fx_Whiff` arc gated to `WingSlamAbilitySO`, victim head puff); verify `_stunPS` and delete if dead | — | capture `stun_2..5` |
| D11 | Peck chip | `FoodPileVisuals.cs` (0.12 s squash), `ChickenVFX.cs` kernels + straw puffs | only when the pile amount dropped | capture with the bird parked at the pile (add a `Park` step to the lab script) |
| D12 | Reduced Motion + mobile profile | all of the above | static / halved variants; Mobile tier profile against the caps | `profiler-get-rendering-stats` on the Mobile tier in the lab; Pixel session item |

### Batch E — audio (brief for `audio-designer`)

Six clip fields on `AudioRegistrySO`: hit-confirm, whiff, denied click, shell close ("clack"), hatch ("crack-pop"), hunt sting; wire from `HitFeedback`, `ChickenShellVisuals`, `MarkFeedback`.

## 3. Order and gates

1. **A** (A1 first: it is the one hard-rule bug), then **B** (B1 before C and D: every new effect reads the palette), then **C**, then **D1 → D2 → D3 → D4-D11 → D12**, then **E**.
2. Re-audit after **C** (icons and badges in place: HUD and loadout captures at phone size) and after **D12** (full lab capture with `capture_feedback_lab.py` at timeScale 1, plus a two-client Hide check), same lenses as 2026-11-06 with the re-audit brief, page republished to the same URL with grade history.
3. Compendium: `docs/site/mechanics.html` republished after B (names, labels, colours) and after D12 (feedback section).
4. Pixel session items: badge legibility at 24 px on the device, the egg at the Mobile tier, particle caps, haptics for the egg close.

## 4. Open items carried (not blocking)

- Definitive ability names (D1 later): when chosen, labels, pops and the compendium follow in one pass; the icons may be fine-tuned then (D6).
- Victim-side Hunt words (MARKED! and the skull) if Maestro wants the fox-and-hen frame on the victim too.
- Rule questions from the lenses, unanswered and unchanged: stun breaking Hide; the walk refusing pile and bump slows; "another cast ends the egg".
