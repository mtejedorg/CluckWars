# HANDOFF — Coop Report Quick Wins (2026-08-25)

**Audience:** whoever opens the next thread on this — likely starts by invoking
`code-architect` per this project's routing rules (see `.claude/CLAUDE.md`). This doc is
self-contained; code-architect can read it cold and triage straight to specialists
(`senior-dev`, `ui-designer`, `mechanics-designer`, `vfx-artist`, etc.) without needing the
conversation that produced it.

**Source:** "The Coop Report" — a persona-review + 50-simulated-player-panel pass over the
game's design docs (no code touched). Findings referenced below by the same codes used
there (A, B, C1/C2/C3, D1/D2/D3). Nothing in this handoff has been built yet.

**Baseline:** branch `develop`, at whatever commit is HEAD when this thread starts — note
the actual commit in the Progress Log below once work begins.

**Explicitly out of scope for this thread:** Theme D — Longevity — as a whole. Progression
systems, ranked/matchmaking, and map-variety strategy all need their own design + roadmap
pass in a separate thread; don't let scope creep into that here. The three D items below
are the exception: they were pulled out specifically because they're cheap, local-only,
and don't require the bigger Longevity decisions to be made first.

---

## Why these 13, in this shape

Every item below cleared two bars: the persona/panel review flagged it, and it's cheap
enough to ship without waiting on the human playtest that's still the real gate on
whether any of this matters (see `docs/GDD.md:674` — "is it actually fun?" is still open).
Treat this as a **first-pass, low-risk layer** on top of the existing game, not a
response to validated player behavior.

Three items were **skipped even though they're in the same families**, worth knowing why
so nobody re-adds them by accident:
- **A4** (class-first, abilities mid-match) and **A6** (named presets) — alternate
  approaches to the same onboarding problem as A1/A2/A3/A5; picking all of them would be
  solving onboarding five different ways at once.
- **B1** (HUD callout) and **B4/B5** (late-match bonus / post-match summary) — B2 and B3
  cover the "make the comeback visible" job more cheaply; B4 in particular overlaps the
  deferred endgame rule (`docs/GDD.md:670`) and is likely premature.

---

## Build-order notes (read before triaging to specialists)

Three real dependencies/synergies sit inside this list — worth deciding sequencing around
these before parallelizing everything else:

1. **A2 defines what A1 applies.** A1 (Quick Play) isn't a separate feature from A2
   (pre-filled recommended loadout) — A1 is a shortcut that instantly applies whatever A2
   decides the recommended default is. Build A2's "what counts as a recommended loadout
   per class" logic first; A1 is then a thin button on top of it, not a parallel track.

2. **C1c, C2b, and C3c are the same underlying feature wearing three names.** All three
   fixes for "that felt cheap" moments resolve to the same thing: give the *victim* better
   information before or during the hit — a wall-permeable-AoE telegraph, a loud tell for
   Immovable's immunity window, and a "you are marked" warning for the Assassin's execute.
   Worth scoping this as one shared **threat-telegraph capability** (a generic
   "warn the target" hook other abilities can reuse later) rather than three bespoke VFX
   passes. Whoever picks this up should decide that scoping question before assigning it
   out to three separate specialists.

3. **A3 and D2c both want a "matches played per class" counter.** A3 (progressive ability
   pool) unlocks abilities as a class is played more; D2c (per-class mastery stats) reads
   off the same counter to display it. Build the counter once, use it for both.

Everything else in the list (A5, A7, B2, B3, D1a, D3a) is independent and can be
parallelized freely once the three items above are scoped.

---

## A — Getting into the match fast
*Panel: 26/50 agreed this is real friction. Verified — no tutorial exists anywhere in
`docs/`.*

### A1 · Quick Play button
Auto-assigns a class + loadout and drops the player straight into a match; the full
customization screen stays behind an "Advanced" toggle for anyone who wants it.
**Depends on A2** (see build-order note above) — don't start this before A2's default
logic exists. Likely touches: menu flow / `MenuUiController`, UXML.

### A2 · Pre-filled recommended loadout
The loadout screen opens already valid — a sane class/passive/2-ability combo — instead of
blank. Play immediately, or tweak from a working starting point. Also teaches by example
what a reasonable loadout looks like, which nothing in the game currently does. Likely
touches: loadout selection logic, `mechanics-designer` input on what "recommended" means
per class.

### A3 · Progressive ability pool
Each class starts with 2 abilities available; the pool widens as that class is played
more. **Shares its matches-played counter with D2c** — build that once. Likely touches:
new persistence (matches-played per class), loadout screen filtering, `mechanics-designer`
input on unlock order.

### A5 · Filter loadout by category
Surface the ability roster's existing Steal / Control / Defense / Utility tags as a filter
on the loadout screen. Cheapest item on the whole board — the categories already exist as
data, this is a UI-only pass. Likely touches: UXML/USS on the loadout screen.

### A7 · First match is the tutorial
Run the player's first match against bots with contextual on-screen prompts instead of a
separate tutorial scene. Reuses the bot AI rebuild from 2026-08-23 (`docs/STATE.md`) as
the teaching substrate — the bots already play every class competently, so they're a
free demonstration of what each ability does. Likely touches: match-start flow, HUD
prompts, `narrative-designer` input on prompt copy, `ux-designer` input on prompt timing.

---

## B — Making the comeback visible
*Panel: 30/50 agreed. Verified the mechanic itself exists — `docs/GDD.md:39` states
stealing food back **is** the designed comeback path; the gap is purely that it isn't
visible to a losing player mid-match.*

### B2 · Leader wears a target
A visible marker on whoever currently holds the lead. Self-balancing by design — everyone
naturally hunts the leader. **Near-free**: the rival-indicator system (class-tint,
screen-edge chevron) already exists per project memory; this is a "who's the leader"
lookup feeding a system that's already built, not a new system. Likely touches: HUD /
rival-indicator logic.

### B3 · Cargo readable on the body
Loaded chickens visibly bulge or trail food as they carry more, so "who's worth robbing"
is glanceable without opening a HUD. More involved than B2 — needs an actual visual
state, not just a data lookup. Likely touches: shader/model work (`shader-artist` or
`artist-3d`), cargo-state wiring to drive it.

---

## C — Removing "that felt cheap" moments
*Panel rated all three of these surprisingly high (31–38 of 50 agree) despite only C1
being a confirmed defect — C2 and C3 are still speculative until watched in a real match.
**Re-read the build-order note above before starting any of these — they're one feature,
not three.***

### C1c · Declare walls ability-permeable by design (chosen path for C1)
`docs/STATE.md:529` confirms AoE abilities currently hit through walls with no
line-of-sight check. Rather than adding occlusion (which would force re-solving reach
values against the Balance Oracle), this path leans into it on purpose: make the "walls
don't fully block this" rule legible through a telegraph, instead of fixing the geometry.
Cheapest of the four C1 options specifically *because* it avoids the Oracle re-solve.
Needs a supporting VFX/telegraph pass to actually read as "on purpose" rather than "still
broken" — see the shared threat-telegraph note above.

### C2b · Keep Immovable's immunity, add a loud telegraph (chosen path for C2)
Fatty's control-immunity window stays exactly as strong as it is; opponents just get a
clear, loud signal that it's active so they can hold their control ability instead of
wasting it. Counterplay through information rather than a nerf.

### C3c · Victim-side "you are marked" warning (chosen path for C3)
An explicit on-screen callout when the Assassin's Mark lands, before the kill window
resolves. Same lever as C2b — give the person on the receiving end something to react to.

---

## D — Longevity quick wins only
*The rest of Theme D (map variants, real progression, ranked/matchmaking) is out of scope
for this thread — see the top of this doc. These three are here because they're cheap,
local-only, and don't require the bigger Longevity roadmap decisions first.*

### D1a · Jitter props/piles per match (chosen path for D1)
Keep the wall topology fixed (that's a deliberate learnability call, per
`docs/STATE.md` — don't touch it) but vary prop/pile placement within it, so repeat
matches don't feel identical. Likely touches: match-start setup, `level-designer` input on
jitter bounds so placements stay fair.

### D2c · Per-class mastery stats (chosen path for D2)
Local, no-backend stat tracking — "Warrior: 12 matches, 340 food banked" — a sense of
progress with no unlock economy to design. **Shares its matches-played counter with A3**
(see build-order note above). Likely touches: local persistence, a display surface
(end-of-match screen or profile panel).

### D3a · Local leaderboard / personal bests (chosen path for D3)
Device-local, no backend. Likely shares storage plumbing with D2c — worth building both
on the same local player-stats store rather than two separate save files. Likely touches:
local persistence, a display surface.

---

## Progress Log

*(New thread: fill this in as work lands. One entry per item, in the order actually
built — doesn't need to match the grouping above.)*

- Baseline commit: _fill in_
