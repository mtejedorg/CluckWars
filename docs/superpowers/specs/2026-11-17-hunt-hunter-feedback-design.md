# Hunt: hunter-side feedback — design (2026-11-17)

Hunt is the UI name of Death Mark (`MarkKillAbilitySO`, `AssassinExecute`, `MarkKill.asset`; GDD §6.4). This document covers what the **hunter** sees. Decision page with the mock-ups: https://claude.ai/artifact/UKj6VChjS8sSpGa2NpaLDT (source: session scratchpad, not in the repo).

## 1. Intent

Maestro, 2026-11-17: "I'm still missing some feedback for death mark. It is clearer for the victim than the killer. Killer must know the state of the ability and who is the target at a glance."

The hunter has one second to pounce (`ArmSeconds` 3 inside `WindowSeconds` 4) on a rival that must be stunned, within `KillRange` 6.2 m, with no home wall between and not in an egg. In that second the hunter's eyes are on the two birds and the thumb is on the hex, so that is where the state has to be.

## 2. What exists today (read from code, 2026-11-17, branch `balance`)

| State | Hunter | Victim |
|---|---|---|
| Mark placed | standings row highlight (0.6 s draw), tick and lock sounds, the marker appears | `MARKED!` toast, sting, standings skull |
| Arming, 3 s | 68 px screen-space disc over the target in its seat colour: digit 3-2-1, draining ring; metres only when off screen; **the hex looks exactly as it does at rest** (full colour, HUNT, no count) | pill `LIVE IN {secs}s`, screen dim, cold tint near the hunter, skull badge on the hunter's arrow |
| Live, pounce not legal | the paw replaces the digit (paw since 55615fc2, not a skull), the disc pops, `HUNT!` over the hunter; grey + lock (home wall) or cream + egg (shelled); **no number; hex still at rest** | pill red `LIVE {secs}s`, pulsing |
| Live, pounce legal | disc yellow and pulsing; hex `POUNCE`, pulsing; sting | nothing extra |
| Ended without a pounce | silent 0.3 s fade, 4 s cooldown, no reason | silent 0.3 s fade |

Sources: `MarkPresentationModel` (roles, marker modes, 0.3 s end rule), `MarkHudController` (hunter marker, `Role == Assassin` only), `MatchHudController` (toast, clock, standings), `TouchControlsController.Hud` (`UpdateMarkHexes`, `HexAlphaNoTarget`), `MarkKillAbilitySO.IsUsable` (returns `CanKillNow` while a target is marked; the resulting `NoTarget` draws at full alpha, `TouchControlsController.Hud.cs:350-355`).

Gaps: the hex carries nothing about the hunt until POUNCE (it is not dimmed: an earlier draft of this document said 0.70 alpha, which is the joystick's value, corrected 2026-11-17); no time number once live; the target is identified by one small disc; "not stunned" and "too far" are not shown; every ending looks the same; no haptics. (H7a, paw instead of skull on the hunter's disc, turned out to be shipped already.)

## 3. Goals, non-goals, anti-goals

- **Goals.** At a glance, without reading: (1) which bird is the target, on screen or off; (2) what state the hunt is in and how long is left; (3) what is still missing for the pounce; (4) why a hunt ended.
- **Non-goals.** No change to anything the victim or bystanders see. No change to timings, ranges, cooldowns, targeting or the kill rules. No new names (D1, D5b: Hunt, HUNT, POUNCE stay). No sound authoring (BL-133).
- **Anti-goals.** A third place to look (a hunter pill at the top was offered and declined, H6a). New networked state or RPCs for presentation. Cues that other players can see and use to find the hunter or the target.

## 4. Approaches considered

1. **On the target and on the hex (chosen).** The marker and a ground sign carry who and what on the rival; the Hunt hex mirrors the same state under the thumb. Two places, both already where the hunter looks.
2. **A hunter clock pill mirroring the victim's.** States it in words and names the target, but sits under the round clock, away from the birds, and adds a third place. Declined (H6a).
3. **Hex only.** Cheapest, but leaves "who" on a 68 px disc. Rejected: the requirement names the target first.

## 5. The design (Maestro's answers: all eight recommended options, 2026-11-17)

Everything below is drawn only on the hunter's own screen, read from replicated state (`MarkedTarget`, `ArmTimer`, `WindowTimer`, `KillReady`, the target's stun / steal-proof / position), with no new `[Networked]` field and no RPC.

- **H1a. The target sign.** (a) Ground brackets around the marked rival in the rival's seat colour (`PlayerPalette.ForCorner`), four arcs, hunter-only, drawn above other ground rings; red in the live second while the pounce is not legal, yellow when it is. (b) The marker disc grows by half (68 → about 100 px; the 96 px box and the pointer scale with it). (c) The distance chip `{m} m` (`mark.distance`) shows whenever a mark is on, not only off screen: red while the target is beyond `KillRange`, the seat colour inside it.
- **H2a. The Hunt hex.** While a mark is on, the hex stays at full alpha (already true in code; pinned by a test) and shows: a dot in the target's seat colour; a ring draining over the mark window; the count, whole seconds while arming (3, 2, 1) and tenths in the live second (`0.8`); and `POUNCE` with the tenths when the pounce is legal. The marker shows the same count (the arm digit already exists; the live second gains the tenths).
- **H3a. What is missing.** One glyph on the marker and the same glyph on the hex for the condition that blocks the pounce once the mark is live: a star when the target is not stunned; the red metres chip when it is beyond `KillRange`; the existing lock (home wall) and egg (shelled). Priority when several apply: egg, wall, too far, not stunned. The 6.2 m reach is not drawn on the ground.
- **H4a. Why it ended.** One hunter-only word over the hunter's bird and the reason glyph on the marker for 0.6 s before the usual fade: `MISSED` when the window ran out or the target was lost; `SAFE` (the existing `hud.pop.safe` word) when the mark ended with the target behind its wall or in an egg. A landed pounce keeps today's effects and adds no word. `MISSED` is the only new string.
- **H5a. Off screen.** The pinned marker stays (larger, per H1) and a short arrow in the target's seat colour leaves the hunter's bird toward it, hunter-only, hidden when the target is on screen.
- **H6a.** No hunter clock pill.
- **H7a.** The hunter's marker shows the paw; the skull stays on the victim's toast and clock and on the standings. Already shipped (55615fc2): no code, only the stale compendium line is fixed.
- **H8a.** One short haptic buzz when the mark goes live and a stronger one when the pounce becomes legal, through the existing haptics path and setting; no new audio.

## 6. Constraints

- Presentation observes replicated state and stays local (ARCH-EVT-01, ARCH-EVT-05, ARCH-EVT-14); gameplay never calls presentation.
- Strings are `UiKeys` constants with `UiText.csv` rows (CODE-TEXT-01 to 05); colours and timings live in `FeedbackTuning` / the palette (ARCH-LAY-15, CODE-UI-08).
- `MarkKillAbilitySO.IsUsable` keeps its meaning for input and bots and is not edited; the hex's hunt look is a presentation override keyed on the mark state.
- Reduced Motion: pulses and the bracket motion stand down as the existing marker pulse does.
- The five-assembly layering holds (no upward references).
- Ending cause must be derivable locally from the last frames of replicated state before the mark ends; if a cause cannot be told apart reliably, fall back to `MISSED`.

## 7. Left to the builder

Exact sizes and stroke widths, the bracket mesh or sprite, where the hex ring and dot sit among the existing hex layers, the haptic durations, the test names. Each item needs a guard test of the pure rule it adds and a `cw_markshots` capture of the hunter's view (`tools/test-session/scenario-pc.ps1 -ExtraArgs '-cw_markshots -cw_perk Assassin_Spoiler'`).

## 8. Builder calls settled with the plan (2026-11-17, mine unless Maestro overrides)

The build plan is `docs/superpowers/plans/2026-11-17-hunt-hunter-feedback-plan.md` (items K1-K9). Its nine open points were settled with its proposed defaults:

1. H7a needs no code (see §5).
2. The hex alpha rule is a pin; K2 starts with a capture of the hex during a hunt.
3. The marker disc holds the count in its centre for the whole hunt; the paw moves to a small corner badge.
4. The metres chip shows one decimal under 10 m (so 6.3 m reads red and 6.1 m does not), whole metres beyond.
5. A mark that expires while the pounce was legal shows no word (it cannot be told from a pounce without the cast event).
6. A mark that ends early without a pounce (target taken by someone else, target left) shows MISSED with no glyph.
7. When only the hunter is stunned, no blocker glyph shows; the hex keeps its stunned cross.
8. The brackets follow the marker onto a hidden target (the marker already tracks it; no new information).
9. The two buzzes obey the existing "Buzz When Hit" setting.

## 9. Owner revision, 2026-11-17 (after the first real frames): the mark rides the arrow

Maestro: "It is confusing that positions are tracked with arrows and then when mark is on the marker changes. Mark should be integrated in the arrow at the floor, and only appear above head if inside the circle."

This replaces H5a and the off-screen half of H1a(b). The circle is the floor ring round the hunter's own bird on which the rival arrows are painted (`FloorRivalArrows`, radius `FloorRivalArrows.RingRadiusFor`).

- **Target outside the circle** (on screen or not): the target keeps its floor arrow, forced on even when the bird is in view, and the mark is part of that arrow. The arrow is painted larger and in the mark's state colour (seat colour while arming, red when live and blocked, yellow when the pounce is legal), and the marker rides on it as a compact badge at the arrow's tip: count, draining ring, paw, blocker badge, metres chip. Nothing is drawn over the target's head and nothing is pinned to the screen edge.
- **Target inside the circle**: the marker sits over the target's head as built in K3 (large disc, count, badges, chip). The arrow is gone, as for any rival inside the ring.
- Crossing the circle uses a small hysteresis so the mark does not flicker between the two places.
- Fallback only: if the floor ring cannot paint the arrow (pool full, the dev edge-arrow mode, no local bird), the mark rides the edge chevron or the edge pin as before, so a mark is never left with no sign.
- This is the same pattern the victim already has (the hunter's arrow carries the skull while the hunter is beyond the inner circle).
- Unchanged: the ground brackets under the target when it is in view (H1a(a)), the hex (H2a), the blocker glyphs (H3a), the end word (H4a), the buzzes (H8a).

## 10. Owner revision, 2026-11-17 (evening): the hunter gets a screen tint too

Maestro: "we are using screen tint colours for the victim, to easily differentiate states of the mark. We definitely should apply that to the hunter (equal or different)".

Builder's call (mine unless Maestro overrides): **different shape, same colour language**.

- The victim's mood is unchanged (dark vignette for the mark's life, violet and washed-out while the hunter is near, a faint red while the hunter is near and hidden). Its states are about where the hunter is, which means nothing to the hunter, and its darkness and desaturation would cost the hunter the view it needs to chase.
- The hunter gets an **edge-only** tint: the centre of the screen stays untouched, nothing is desaturated, and the colour is the one the ground brackets already use for that state (yellow is also the POUNCE button's and the disc's).

| Hunt state | Screen edge |
|---|---|
| The draw is still running (no target told yet) | nothing |
| Arming (from the lock to live, 3 s) | dark edge (the victim's DIM colour, weaker): a hunt is running |
| Live, the pounce blocked (not stunned, too far, wall, shell) | red edge (`MarkHuntBlockedColor`) |
| The pounce is legal | yellow edge (`MarkKillColor`) |
| The mark ended | fades out with the mark's end fade |

- The red and yellow follow `HuntRules.BracketColour` for the armed states, so the edge and the brackets can never disagree. (As built the arrow and the disc follow `HuntRules.MarkerColour`: the target's colour until the pounce is legal, then yellow; they do not turn red in a blocked live second, which section 9 said they would. Open question H15 on the decision page.)
- The live window is one second, so a colour change crosses in about 0.1 s; the dark edge fades in over the victim's DIM time.
- No pulse and no flash: nothing to switch off under Reduced Motion.
- Cost: the blend-only Overlay pass of the existing `MarkScreenFeature` (one full-screen triangle, no colour read) on PC and Mobile; no new renderer feature, no new shader, no post-processing needed.
- One role at a time: the model's role decides which mood is drawn; a peer is never shown both.

## 11. Evening close, 2026-11-17: what the frames changed, and what is open

- The red edge at 0.30 read as brown corners in the 1200x540 capture: raised to 0.55 (`MarkHuntMoodGlowBlocked`, `cf22b4d1`). Yellow stays 0.40.
- Open look questions, put to Maestro on the decision page (H10 to H15): the tint's shape (own / same as the victim's), its strength, the colour of MISSED, the badge size on the arrow, the brackets outside the circle, and whether the arrow and disc should turn red in a blocked live second.

## 12. Owner answers to H10-H15, 2026-11-18

Maestro's answers, verbatim: `h10a(note: Also, for a better feedback, hunter and hunter should be distinguishable from the rest of the chickens e.g. by being inmune or react differently to the tint) h11a h12a(note: However, colors should be defined and easily tuneable in a colors so I think we already have one) h13a(note: I think it should be over the arrow, not at the tip) h14a h15 later(note: I need to see this to decide)`

| | Answer | What it means for the build |
|---|---|---|
| H10 | a, with a note | The hunter keeps its own edge-only tint. New: the two birds of a hunt (the hunter and the hunted; "hunter and hunter" read as that) stand out from every other chicken while a mark tints the screen: they are **exempt from the tint**, on the hunter's screen and on the marked player's. Item K13. |
| H11 | a | Strength stays as built (dark 0.40, red 0.55, yellow 0.40). |
| H12 | a, with a note | MISSED turns cream. The colour, and the mark's other colours, live in the existing colour config asset (`FeedbackPaletteSO`, ruling D3a of 2026-11-07), not in code. Item K11. Correction of the question's text: SAFE is the palette's neutral grey (`NeutralNoEffectColor`), not cream; it is left as it is. |
| H13 | a, with a note | The badge keeps its two-thirds size but sits **on the arrow**, not beyond its tip. Item K12. |
| H14 | a | Brackets whenever the target is on screen, as built. |
| H15 | later | Maestro needs to see it: a side-by-side of the three options goes on the decision page. No build change until answered. |

### K13 rules (builder's call unless Maestro overrides)

- **Who is exempt.** On the hunter's screen: its own bird and the target. On the marked player's screen: its own bird and the hunter. Nobody else has a tint to be exempt from.
- **What exempt means.** The bird's body is drawn with its true colours and brightness over the tint: no edge colour, no darkness, no violet, no desaturation. Everything else in the world keeps the tint.
- **It never reveals.** A bird that is hidden (Hide) or fading is not exempt: only a fully visible bird is. The marked player's faint red for a near and hidden hunter is unchanged.
- **No new information.** Exemption changes how two already-visible birds look, nothing else: no outline through walls, no marker.
- **Cost.** The two birds are drawn a second time, depth-tested, after the mood pass; nothing when no mark is shown.
