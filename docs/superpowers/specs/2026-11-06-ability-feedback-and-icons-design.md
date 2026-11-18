# Ability feedback, state cues and icons for the 10-move kit — design

**Date:** 2026-11-06 · **Branch:** `simplify-abilities` · **Status:** design for Maestro's sign-off; nothing implemented in code yet. Assets generated this session are listed in §8.

## 1. Intent

Maestro (2026-11-06): now that the abilities are down to ten generic jobs, bring every ability and every state (stunned, slowed, egg, hidden…) to a production level of attention: feedback that is *non-invasive but completely clear*, and one simple, unmistakable icon per ability and per state. His example: the Shield ("egg shell") must show one clear cue for its slow-walk phase and an egg closing around the chicken for the invulnerable phase. Names may change if a better one fits the game's style.

The audience is children. A kid makes one decision per cue, in the first 0.3 s of seeing it. So every state needs a loud **onset** (one beat, everyone), a **persistent** cue that survives a glance from across the arena, and an **exit** that is an event. Colour is never the only channel (FEEDBACK.md §1.3).

## 2. Method and evidence

- Five parallel read-only lenses (mechanics, game feel, VFX, naming, 2D/icons) against the code, the ten `.asset` files and `docs/FEEDBACK.md`; their reports are in the session scratch, synthesised here.
- Real captures: `Captures/feedback-2026-11-06/lab/` (46 Game View frames from `AbilityLab.unity`, every ability at hold / cast / impact / aftermath, Warrior caster, Fatty dummy at 5 m; `capture.log` holds the replicated state per frame). Contact sheets `sheetA.png`, `sheetB.png`, `closeups.png`. Caveat: Fusion's tick timers ignore `Time.timeScale`, so the slow-motion frames show real-time gameplay with slowed visuals; the states captured are genuine, the per-frame timing is approximate.
- Verified before claiming (what each lens got right or wrong):

| Claim | Verdict | Where |
|---|---|---|
| The 0.80 m buff ring is drawn on a hidden bird (no `VisualOpacity` gate in `ControlStateVFX.UpdateBuffRing`) | **Confirmed in code and in `hide_3_hidden.png`**: the dithered ghost keeps a full-alpha ring, identity ring and nameplate | `ControlStateVFX.cs:286-310` |
| Shield egg has no on-body visual | **Confirmed live** (`shield_3_walk.png` is the egg phase: brown ring only, bird unchanged) | capture |
| Speed shows nothing on the bird | **Confirmed live** (`speed_3_impact.png`) | capture |
| The Slow aura edge is drawn purple, so victims cannot connect it to their cyan state (mechanics lens) | **Wrong**: the aura zone ring is already cyan (`slow_3_impact.png`); the purple ring is the permanent range guide. What is missing is motion that says "this circle moves with him" | capture |
| A bird that leaves the aura stays slowed for the rest of the 3 s (mechanics lens Q8) | **Wrong**: the aura slows by presence each tick (`CheckAbilityZoneSlow`); leaving the circle frees you | `ChickenController.cs:888-894` |
| Shove's accent equals the stun state yellow; Stun's accent is orange; Speed's accent sits in the slow-cyan band | **Confirmed in the assets** (table in §6) | `.asset` + `FeedbackTuning` |
| `ChickenVFX._stunPS` is a dead duplicate of the stars | Reviewer judgement, not checked | — |

## 3. Principles (the rules every item below obeys)

1. **One body cue per self-state.** Speed, Egg and Hide each get their own shape on or around the silhouette. The shared accent buff ring stops being the peers' cue (it stays on the caster's own HUD hex as the countdown).
2. **Hidden means hidden.** Every world-space tell (rings, badges, nameplate, dust, streaks, sparks) is gated on one predicate, `ControlStateVFX.IsFadedOpacity`, on every peer. The two Hide poofs are the only effects allowed on a fading bird, by design.
3. **An ability wears the colour of the state it inflicts.** Stun = the stun yellow, Slow = the slow cyan. Instants and self-buffs leave those bands (§6).
4. **Button = cause, badge = effect.** The hex icon shows the tool or action (mallet, egg, claw with food); the state badge shows what happened (stars, snail, egg with a tick).
5. **Name = label = pop.** The word on the hex is the word in the loadout and the word that pops on the victim (STUN already does this). Every label has a vowel and ≤ 4 letters.
6. **Exits are events.** Stars fly off, the egg hatches, the ghost reappears with a poof. Leaving a state is never a silent fade.
7. **Local only, replicated state only, Reduced Motion honoured, mobile caps kept** (≤ 64 live particles and ≤ 7 particle systems per chicken, ≤ 400 per arena; no trails, sub-emitters, collision or lights).

## 4. Per-ability spec

Numbers are from the shipped assets. "Primary" = the one cue that must survive Reduced Motion and the low tier.

### 4.1 Steal (`Snatch.asset`) — keep name Steal, label ROB
- Robs 5 food from every rival carrier within 5.16 m, cooldown 4 s. Accent stays red-orange (1, 0.3, 0.1).
- **Icon:** chicken claw clutching a corn cob (redraw; the bare claw read "hand", not "take food").
- Caster: existing cast flash; **new grab lines** (the three impact LineRenderers extend 80 % toward each victim over 0.08 s and retract over 0.10 s); caster shake **cut** from 0.18/0.22 s to 0.08/0.15 s (today the thief shakes harder than the victim); gold `+n` summed per cast (primary).
- Victim: existing white flash, `-5` text, screen-edge arc; **new corn arc**: 3 `Fx_Corn` kernels fly victim → thief over 0.35 s on a 0.6 m arc (primary read of "who took it"). If the thief is faded, the kernels rise 1.2 m and vanish at the victim.
- Bystander: the cast flash plus the kernels say "thief, now loaded".

### 4.2 Stun (`WingSlam.asset`) — keep Stun / STUN
- Every rival within 5.4 m, 1.3 s, cooldown 7 s. **Accent → the stun yellow** (1, 0.85, 0.2) (§6).
- **Icon:** mallet with one impact star (redraw; the flying bird with arcs read "noise").
- Victim: existing yellow 0.62 drain ring, orbiting stars, red nameplate, STUN pop, red-X hexes (all kept). **Add** star pop-in (0→1 with overshoot, 0.15 s) and star drop-off (stars stop, fall 0.4 m and fade, 0.3 s) so entering and leaving are events; a 4-puff "bonk" at the head.
- Caster: a wing-swing arc (2 `Fx_Whiff` either side, 0.18 s), gated to `WingSlamAbilitySO` so a Death Mark stun keeps "red flash + stars"; **new caster-side countdown**: the Stun hex shows the victims' remaining stun as a digit while his stun runs (serves Steal-after-stun and the Death Mark kill timing; leaks nothing the badge does not already show).
- Remove `ChickenVFX._stunPS` if confirmed dead.

### 4.3 Shove (`CluckShock.asset`) — rename **Push** / PUSH
- Every rival within 5.4 m, knockback 16.2, cooldown 5 s. **Accent → push orange** (1, 0.55, 0.15) (off the stun yellow).
- **Icon:** open palm with three dashes (redraw); alternate: radial burst. The shouting chicken head read "shout" and sat next to Peck's head.
- Victim: existing shockwave, impact lines, flash, shake, haptic; **add** a 0.3 s skid dust trail while the knockback travels and a 0.12 s squash (verify `ChickenAnimator`'s recoil first — single-write rule). Pop word PUSH.
- Caster: existing cast flash plus 8 accent sparkles flying out at 3 m/s.

### 4.4 Slow (`FeatherAura.asset`) — keep Slow / SLOW
- Aura 6.48 m following the caster, 55 % speed, 3 s, cooldown 6 s. **Accent → the slow cyan** (0.3, 0.75, 1) (§6).
- **Icon:** snail (redraw; the chicken-in-a-swirl read "refresh"). Alternate: a single feather, which matches the VFX noun; the snail matches the kid word and the existing 🐌 vocabulary.
- Victims: existing cyan ring, blob + streaks, SLOW pop, `×0.55` badge, stick tint (kept, no countdown by design).
- The circle itself (primary for the victim: "step out of *his* circle"): the aura outline gains a scrolling dash (0.1 lap/s) so a live aura differs from a static range guide by motion; one `Fx_AuraEdge` band at alpha 0.20 (no fill); **feather-fall inside** (≤ 24 world-space feathers, lavender-cream, drifting down; new ones spawn under the moved disc, so the drift is the "it follows me" read); 4 feather puffs at the caster's feet every 0.4 s so an off-screen circle still attributes.

### 4.5 Speed (`SpeedBurst.asset`) — rename **Zoom** / ZOOM
- 1.3× for 1.5 s, cooldown 4 s. **Accent → warm gold-white** (1, 0.9, 0.55) (out of the slow band).
- **Icon:** keep the lightning bolt (conditional on the stun badge becoming stars, which removes the ⚡ collision).
- Caster (primary): hip-height stretched `Fx_SpeedLine` streaks left behind (≤ 24), stride dust, 6° extra lean, 6 sparkles at the feet on the press, pop ZOOM!. **No ground ring on peers.**
- Chaser: the streaks say "can't catch him for 1.5 s".
- All of it gated on opacity (Hide + Zoom is legal) and off under Reduced Motion (dust at 1/m stays).

### 4.6 Jump (`Shadowstep.asset`) — keep Jump / JUMP
- Dash 3× for 0.18 s over walls, cooldown 2 s. Accent violet unchanged.
- **Icon:** leaping chicken over a solid arc (redraw; the ink smoke cloud breaks the cream-only rule and smears at 24 px).
- Add launch dust ring + 2 feathers at take-off, 5 accent sparkle dots along the arc chord (reads "went over" when a wall sits under it, no wall detection), landing dust after the existing 0.18 s settle. Keep the white landing shockwave. Never the hit flash.
- Description for kids: "One quick jump forward — straight over walls." (drops the ×3 / 0.18 s numbers).

### 4.7 Shield (`TurtleMode.asset`) — rename **Egg Shell** / EGG (Maestro's own word; "Egg" if a one-word name is preferred — see D2)
- Walk 1 s at 0.5× immune to ability slows, then egg 2 s immune to stun/slow/root/shove, steal-proof and unkillable; press again ends it; another cast ends the egg; cooldown 8 s. **Accent → cream** (0.98, 0.94, 0.86).
- **Icon:** whole egg with one gloss tick (redraw; the turtle is the wrong noun; the retired cracked-egg icon says "breaking").
- Phases are derived from replicated state, no new field: Walk = slot active ∧ ¬`IsControlImmune`; Egg = slot active ∧ `IsControlImmune`; End = either edge. Egg time left = `ActiveRemaining01For × ResolveActiveDuration`.
- **Walk ("Waddle")** (primary: *armour forming*, never *resistance*): the bottom shell cup rises from the ground to 35 % height around the legs (0.25 s, overshoot 1.06), a ±6° waddle at 2.2 Hz composed into the animator's bank, half-speed dust at the heels. Motion converges *onto* the body; slow's streaks drag *behind* — the two never read alike. Cup and body stay fully visible: the walk must look unfinished (it is the punishable half; a rival who reads "egg incoming" and stuns it is the intended counterplay). Hex shows WADDLE; a Slow aimed at a walker marks it grey ⃠ (fixes STATE 2026-11-05 item 4). Pop WADDLE.
- **Close** (edge, 0.30 s): top cap drops from 1.6× height, cup rises to seat, 0.12 s squash with 6 dust puffs, "shell clack" sound hook. Identity ring flashes to 1.0 for 0.2 s. Pop EGG!.
- **Egg idle** (≤ 2 s): wobble ±3° at 1.8 Hz about the base, 1.5 % breathing, cracks appear in the shader from 40 % to 90 % of the egg's life, ±1.5° jitter at 8 Hz in the last 0.3 s. The egg is opaque cream with two **player-colour bands** (30 % and 70 % height) so "whose egg" and "is it loaded" survive (the cargo bar and nameplate stay above it: egg top capped at 1.55 m). A refused steal: SAFE pop + 3 cream clinks on the attacker's side; refused control: the existing IMMUNE pop (proposed NOPE!), nothing more.
- **Open** (edge, 0.40 s, same for timeout, second press or another cast): cap pops up 0.6 m with a tumble, cup sinks, 3 `Fx_EggShard` shards, 10 class-tint feathers, 6 gold yolk puffs. Pop HATCH!.
- Scale per class = `min(ClassScale, 1.24)`: Speedy egg 1.04 m, Fatty 1.51 m (under the 1.70 m nameplate).
- The status badge shows the phase (hollow egg = waddle, egg with a tick = egg) with the countdown; no separate immunity or steal-proof badges (the egg *is* the badge).

### 4.8 Hide (`Invisibility.asset`) — keep Hide / HIDE
- Ghost 2 s at opacity 0.2, any move breaks it, cooldown 4.5 s. **Accent → desaturated violet-grey** (0.6, 0.6, 0.75) (out of the cyan band).
- **Icon:** keep the ghost.
- **Bug fix first:** gate the buff ring, identity ring, cargo bar, nameplate and badges on `IsFadedOpacity` on every peer (today all stay at full alpha on the ghost).
- All peers: an enter poof (8 cream puffs + 3 feathers, marks where they vanished — fair, the cast was visible) and a break poof in accent with the self-ring cast flash ("revealed"). Pop POOF! on the caster only.
- Caster only (`HasInputAuthority`): opacity floor 0.45 so you still see yourself, a rotating cream dashed ring at 0.62, a HIDDEN badge/strip row with a drain bar, a 2× blink 0.25 s before expiry, and a "breaks Hide" mark on any hex while held.
- Nothing else ever emits on a faded remote bird (every particle owner checks the predicate before `Emit`).

### 4.9 Peck (`Peck.asset`) — keep Peck / PECK
- Add: 5 corn kernels from the mound to the beak, 3 straw puffs at the contact point, a 0.12 s mound squash in `FoodPileVisuals`. Only when the pile's `Amount` actually dropped. Nothing else: the cargo bar, FULL! and the chain ring already carry it.

### 4.10 Death Mark (`MarkKill.asset`) — keep the name (ruling); label EXEC → MARK, then KILL
- Not redesigned. Its stun is the generic Stunned state (same stars, pop-in/drop-off); its cast flash stays red; the hunter's shell badge must become the new egg-with-tick glyph; the Hide vanish poof is drawn once at the last visible spot so the direction-only hunter arrow never becomes a position reveal. Optional softer variant for D5: **Hunt / HUNT then POUNCE** (fox-and-hen frame, eye or paw icon).

## 5. States

| State | Badge word · pop | Body cue (primary) | Who sees it |
|---|---|---|---|
| Stunned | STUN · STUN | stars (pop-in / drop-off) + yellow drain ring; red-X hexes for the victim | everyone |
| Slowed | SLOW · SLOW | cyan ring + dragging streaks; `×0.55` *(as shipped: the SLOW word alone, no magnitude — §12 E2a, E6a)* | everyone |
| Knocked back | — · PUSH | shockwave + impact lines + skid; instant, no row | everyone |
| Waddle (shield walk) | WADDLE · WADDLE | cup around the legs + waddle + heel dust | everyone |
| Egg | EGG · EGG! / HATCH! | closed two-half egg with player bands, wobble, cracks | everyone |
| Hidden | HIDDEN · POOF! | dither ghost (remote); dashed cream ring + HIDDEN row (self only) | self only |
| Zoom *(as shipped: Speed · FAST · FAST! — §12 D1)* | ZOOM · ZOOM! | hip-height streaks + stride dust, lean | everyone (motion), self (row) |
| Jump landing | — · (HOP! optional) | launch dust, arc dots, landing shockwave + dust | everyone |
| Marked / Hunting | MARKED · MARKED! / HUNT · HUNT!, then MISSED or SAFE at the end | MarkFeedback; the hunter's side reworked 2026-11-17 (`2026-11-17-hunt-hunter-feedback-design.md`: marker with count and blocker badge riding the target's floor arrow outside the circle and by its head inside, ground brackets, the Hunt hex mirroring the state, end word (MISSED cream, SAFE grey), haptics, a screen edge tint by hunt state, hunter and hunted exempt from the tint on both screens) | victim / Assassin |
| Steal-proof | — · SAFE | the egg itself; clinks on a refused steal | the robber |
| Peck chain | — · +1 … FULL! | hex chain ring; no body cue | self |

Stacking priority (what owns the body when states overlap): Removed > Stunned (head + feet) > Egg (body; suppresses speed/slow/hide cues) > Waddle (body-adjacent) > Hidden (suppresses every ground ring) > Marked (screen) > Slowed (feet) > Zoom / Speed (motion only) > one-shots > Peck chain (HUD). Realistic worst case on one bird is stun + slow + zoom = 3 badge rows (the egg refuses both controls, the waddle refuses slows), so the badge stack stays at 3.

Not shown at all (noise): immunity or steal-proof as badges, Zoom or Hide rings on peers, Jump in flight, pile/bump drag on rivals, a second purple ring at a slowed victim's feet.

## 6. Accent colour retune (asset values only, no code)

| Ability | Today | Problem | Proposed |
|---|---|---|---|
| Stun | (0.9, 0.4, 0.1) orange | the state it inflicts is yellow | **(1, 0.85, 0.2)** = `CanonicalStunColor` |
| Shove/Push | (1, 0.9, 0.1) | *is* the stun yellow: "a Push fired" and "that bird is stunned" share one hue | **(1, 0.55, 0.15)** push orange |
| Slow | (0.6, 0.3, 0.9) purple | victims are cyan | **(0.3, 0.75, 1)** = `CanonicalSlowColor` |
| Speed/Zoom | (0.3, 0.85, 0.95) | in the slow-cyan band, opposite meaning | **(1, 0.9, 0.55)** gold-white |
| Hide | (0.55, 0.85, 0.85) | cyan family | **(0.6, 0.6, 0.75)** violet-grey |
| Shield/Egg Shell | (0.55, 0.42, 0.25) brown | reads as the ink under-stroke | **(0.98, 0.94, 0.86)** cream (the egg's colour) |
| Steal, Jump, Peck, Death Mark | — | fine | unchanged |

Changes hex fills, range guides and telegraphs for every player and the compendium site; CVD separation against the four player colours (Okabe-Ito) must be re-checked by `AbilityPaletteTests` before shipping. This is a visual-identity decision (D3).

## 7. Icons and glyphs

Locked style (menu overhaul 2026-10-07): 256×256, glyph only, cream #FEF5E0 fill, thick ink #1A0E04 outline, transparent background; hex tinted by category in code. At 40–56 px on a phone.

| Job | Verdict | Noun | File |
|---|---|---|---|
| Steal | redraw (tweak) | claw clutching a corn cob | `Icon_SnatchAbilitySO.png` |
| Stun | redraw | mallet + one star | `Icon_WingSlamAbilitySO.png` |
| Push | redraw | open palm + dashes (alt: radial burst) | `Icon_CluckShockAbilitySO.png` |
| Slow | redraw | snail (alt: feather) | `Icon_FeatherAuraAbilitySO.png` |
| Zoom | keep | lightning bolt | `Icon_SpeedBurstAbilitySO.png` |
| Jump | redraw | leaping chicken over an arc | `Icon_ShadowstepAbilitySO.png` |
| Egg Shell | redraw | whole egg + gloss tick | `Icon_TurtleModeAbilitySO.png` |
| Hide | keep | ghost | — |
| Peck | keep (optional tweak) | head, beak down | — |
| Death Mark | keep | skull in crosshair | — |

State glyphs (new folder `Assets/_Game/Art/UI/Icons/States/`, outside `Icons/Abilities` because `AbilityIconArtTests.NoOrphanSprite` fails on any extra file there), ~208 px glyph on 256, must survive 24 px: `Glyph_Stunned` (three stars in an arc), `Glyph_Slowed` (snail), `Glyph_KnockedBack` (puff + dashes), `Glyph_ShieldWalk` (hollow egg ring), `Glyph_ShieldEgg` (egg with a tick), `Glyph_Hidden` (closed eye, self only), `Glyph_Speeding` (whoosh + chevron), `Glyph_Hunting` (crosshair without skull), `Glyph_Safe` (no-sign over a cob), `Glyph_PeckChain` (open beak). Marked keeps `Glyph_Skull.png`. Stuck/rooted: no caster exists after the merge; not generated.

Generation: Flux txt2img at 1024² through the local ComfyUI (prompt template in the icons lens), then a two-tone post-process (corner flood-fill background removal, ink/cream threshold, uniform ink coat, fit to 216 px on 256, size/coverage asserts, 112/56/40/24 px readability sheet). Candidates are picked by eye on the review page; the chosen files are copied into `Assets/` with the shipped import profile (Sprite 2D/UI, single, no mipmaps, max 256).

## 8. Asset inventory

Generated this session (in the repo unless noted):

| Asset | Where | Status |
|---|---|---|
| Egg shell meshes `EggShell_Whole`, `EggShell_Top`, `EggShell_Bottom` (720 / 624 / 912 tris, Y-up, 1 m tall, zigzag crack at 55 %, 2.5 cm solidified rim, cylindrical UVs) | `Assets/_Game/Art/Meshes/Fx/EggShell.fbx` (builder: scratch `make_eggshell.py`, headless Blender 5.1) | done; preview `Captures/feedback-2026-11-06/eggshell_preview.png` |
| `Fx_EggShard_1..3.png` 64², `Fx_Corn.png` 48², `Fx_SpeedLine.png` 128×32, `Fx_AuraEdge.png` 256², `Fx_EggSpeckle.png` 128² (tiling), `Fx_EggCrackMask.png` 256² (R = order of appearance, seam at V 0.55) | `Assets/_Game/Art/UI/Fx/` (builder: scratch `make_fx_sprites.py`) | done |
| Ability icon redraws (7) and state glyphs (10), 2 candidates each | `Assets/_Game/Art/UI/Icons/Abilities/`, `…/Icons/States/` after the pick | candidates generated; picks on the review page |
| Lab captures (46) + contact sheets | `Captures/feedback-2026-11-06/lab/` (gitignored) | done |

Still to build (implementation, not this pass): `CW_EggShell.shadergraph` (URP Lit, Cull Off, `_StripeColor`, `_CrackTex`/`_Crack`, fresnel ink rim) + `Egg_Shell.mat`; `Fx_Atlas.spriteatlas` + `Fx_Particles.mat` (move `ChickenVFX` off its untextured in-code material); `ChickenShellVisuals` (new sibling of `ControlStateVFX`); `FoodChunkFlight` pool; animator waddle/squash accumulators; badge sprites replacing the text glyphs in `HudFeedbackStyle` / `ChickenStatusBadges` / `TouchControls.uss`; UiText rows for the new pops; the four missing audio clips (hit-confirm, whiff, denied click, shell clack/hatch) via `audio-designer`.

## 9. Decisions for Maestro

- **D1 Names and labels.** Push / PUSH, Zoom / ZOOM, Egg Shell / EGG, Death Mark label EXEC → MARK; Steal stays Steal / ROB. (Alternative: keep every current name and only fix the labels SHLD → EGG, FAST → ZOOM, EXEC → MARK.)
- **D2 "Egg Shell" vs "Egg".** The retired `EggShell.asset` already carries the DisplayName "Egg Shell"; either rename the retired asset ("Egg Shell (old)") or use the one-word "Egg" on the live move.
- **D3 Accent retune** (§6): abilities wear the colour of the state they inflict. Yes / no.
- **D4 The peers' buff ring.** Drop it from rivals' screens entirely once each self-state has a body cue (keep the hex countdown), or keep it as a thin caster-only ring.
- **D5 Death Mark softening.** Keep Death Mark + skull (ruling), or Hunt / HUNT then POUNCE with an eye or paw.
- **D6 Icon alternates.** Push: palm vs radial burst. Slow: snail vs feather. Stun: mallet (vs keep the current bird).
- **D7 Self-pop budget.** The egg alone says WADDLE, EGG!, HATCH!; Zoom and Hide add ZOOM! / POOF!. Phase changes only, or all of them? (Self-pops are never drawn for rivals.)
- **D8 Refusal words.** IMMUNE → NOPE!? Keep SAFE separate (it tells the robber *why*)? Drop the orphaned STUCK! rows?

Rule questions raised by the lenses, for later (no redesign proposed): should a stun break Hide (avoids a ghost-with-stars picture); should the waddle refuse pile and bump slows too (one sentence: "nothing slows an egg"); should "another cast ends the egg" stay (three exits, one HATCH! beat).

## 12. Decisions recorded 2026-11-07 (binding; they override §4-§9 where they differ)

Maestro's answers on the review page: `d1 later d2a d3a d4b d5b d6a d7b d8b`.

| Decision | Answer | Effect on this spec |
|---|---|---|
| D1 names/labels | **Later.** He did not like Push / Zoom / MARK. | DisplayNames and labels stay generic as shipped (Steal/ROB, Stun/STUN, Shove/PUSH, Slow/SLOW, Speed/FAST, Jump/JUMP, Hide/HIDE, Peck/PECK) until the definitive names are chosen. §4.3 "Push" and §4.5 "Zoom" are void; the pops use the shipped labels (PUSH, FAST!). |
| D2 Egg Shell | **a.** "Egg Shell" / EGG on `TurtleMode.asset`; rename the retired `EggShell.asset` ("Egg Shell (old)"). | §4.7 stands. |
| D3 accents | **a, with a condition:** retune as §6, but the colours live in a **config ScriptableObject** (stunColor, slowColor, …). | New `FeedbackPaletteSO` (state colours + per-job accents) pushed to a static by `ProjectInstaller`; `FeedbackTuning`'s canonical colours and `AbilityBaseSO.AccentColor` read from it (ability assets carry an `AccentKey`, retired assets keep their literal). See the plan, item B1. |
| D4 buff ring | **b.** Keep the ring on everyone, opacity-gated, as a secondary cue. | §3.1 and §4.8: the ring is gated, not removed; each self-state still gets its own body cue. |
| D5 Death Mark | **b.** Hunt / HUNT then POUNCE with an eye or paw icon; internally still "Death Mark". | UI only: DisplayName "Hunt", label HUNT, second press POUNCE, paw icon (`alts/Hunt_paw.png`) and paw hunting badge; code names unchanged; MARKED! stays for the victim for now. |
| D6 icons | **a.** Palm, snail, mallet; fine-tune later with the definitive abilities. | §7 set as proposed; Steal claw + cob, Jump chicken + dashes, whole egg. |
| D7 self-pops | **b.** All of them. | WADDLE, EGG!, HATCH!, POOF!, FAST!, HOP!, HUNT!, ROB -n, +n; never drawn for rivals. |
| D8 refusals | **b.** Keep IMMUNE and SAFE; keep the STUCK! rows. | §5 unchanged; no NOPE!. |
| E1 egg fit (2026-11-08) | **a.** Per-class body tuck in the egg phase: Warrior 0.82 -> 0.70 plus a 0.06 m sink; the comb tip may peek, wings and tail stay inside. | Egg scale cap 1.27 and the 1.55 m top unchanged. |
| E2 who owns the number (2026-11-08) | **a.** The badge row owns the countdown (STUN 1.5s, STUCK 1.2s, refreshed per tenth); pops are words only (STUN, SLOW, STUCK, no seconds, no %). The caster hex digit keeps its ceil-tenths. | Supersedes the SLOW 45% magnitude pop; the snail + cyan carry the slow state. |
| E3 one glyph per state (2026-11-08) | **a.** The nameplate snail shows only when no Slow badge row is visible for that bird, and never for pile or bump drag. | ChickenNameplate.StateIcon reads the badge stack. |
| E4 rivals and a robbery (2026-11-08) | **a.** Rivals keep the bare -n at the robbed victim (it is the victim cargo change); ROB -n stays on the victim own screen. D7b is about self-pops only. | No change. |
| E5 Slow row for pile drag (2026-11-08) | **a.** The Slow badge row, like the pill snail, shows only for ability slows (the replicated Snared bit); pile and bump drag draw no row on any peer. | ChickenStatusBadges.ShowSlowRow. |
| E6 Slow row word (2026-11-09) | **a.** The Slow badge row reads the word SLOW (no number) in a darker cyan text tint, the same row shape as HIDDEN / FAST. | Supersedes the Phase 7 A12 glyph-only row; the no-number rule stays. |
| E7 HUD strip Slow source (2026-11-09) | **a.** The local HUD strip Slow row follows the Snared bit like the world row and the pill snail; pile and bump drag keep the joystick tint alone. | TouchControlsController / HudFeedbackStyle. |
| E8 Egg Shell cast burst (2026-11-09) | **a.** The Shield cast burst is shell cream plus two ink-rimmed shard pieces; the range ring and the hex keep the brown accent (D3a). | Burst colour only; no accent change. |
| E9 two stacks at contact range (2026-11-10) | **c, with a note: "always draw the player above the others".** No nudge or fade until a two-client capture shows how often stacks collide; the local bird's label band (pill, text, badge rows, own pops) draws above every rival's where they overlap. | Sorting orders only (`FeedbackTuning` local band 13-15, local pops 211); nothing moves. Batch J1. |
| E10 one shape for FAST (2026-11-10) | **a.** `Glyph_Speeding` is redrawn as the hex bolt with two trailing dashes. | Icons/States only; the hex icon unchanged. Batch J2. |
| E11 rival rows at the top edge (2026-11-10) | **c.** Leave it; the chevron and the pill are enough near the edge. | No change. |
| E12 telling the egg it can hatch (2026-11-10) | **a, with a note: "also with an exact timer".** While EggSeconds runs the Egg Shell hex swaps to a cracked-egg icon and its label prints the egg's remaining seconds in tenths in the shell accent (the caster-digit path). | Walk phase unchanged; no gameplay change. Batch J3. |
| E13 Egg Shell picker text (2026-11-10) | **b.** `Waddle for 1s (slows bounce off), then be an egg for 2s: nothing can touch you. Press again to hatch.` (101 chars; closes BL-137). | `ability.turtlemode.desc` and its fallbacks. Batch J4. |

## 10. Implementation order (after sign-off)

1. **Bug:** opacity gate on every world-space tell (buff ring, identity ring, cargo bar, nameplate, badges, all particle owners). Test: a faded bird renders no ring/badge/particles on a remote peer.
2. Accent retune (4–6 asset values) + `AbilityPaletteTests` CVD check + compendium site.
3. Names/labels/descriptions + UiText rows + Shield hex phase label and icon swap.
4. `Fx_Atlas` + `Fx_Particles.mat`; `ChickenVFX` on the textured material; icon and glyph files in place with the test chain extended (`AbilityIconArtTests` sibling for `Icons/States`).
5. Egg Shell: `ChickenShellVisuals` with the FBX halves and a placeholder URP Lit, phases, close/idle/open, waddle; then `CW_EggShell.shadergraph` (bands, cracks).
6. Zoom streaks, Jump launch/arc/landing, Push skid, Steal grab lines + corn arc + shake cut, Peck chip.
7. Slow aura dash + band + feather-fall; Hide poofs + caster cue; Stun pop/drop + caster arc + caster countdown.
8. Reduced Motion pass; Pixel profile against the §3.7 caps; FEEDBACK.md §13 and ART.md §6.10 updated; compendium republished.
