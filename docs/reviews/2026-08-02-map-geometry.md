# Panel Review — v0.5 Map Geometry — Action Plan

**Review date:** 2026-08-02 · **Scope:** v0.5 arena (38×38 m, baked to `Map.unity`)
**Panel:** 4 Claude reviewers (level design, economy/pacing, engineering, UX) + 2 agy
(independent engineer, creative director).

Every task below is tagged **CONFIRMED** (coordinator verified against code/scene) or
**ASSERTED** (plausible, unverified). Owner is **C** (Claude can do it), **M** (Maestro must
do it), or **D** (needs a decision first — see §Questions).

---

# P0 — Do first. These gate everything else.

### T1. Repoint the Balance Oracle test at the real map and real stats
**CONFIRMED · Owner: C · ~1h**

`BalanceOracleTests.Task8_RealMap_StatResolvePassesAllClasses` hardcodes the base at
`(11,11)` → r=15.56 (real: **22.84**) and speeds 9.0/7.5 (real: **10.5/9.0** — every class
off by exactly +1.5). It is green in CI while measuring a map that no longer exists.

Run against real values, all four classes overrun their SCT tolerance and **Assassin clears
in 44.12 s against a 45 s cap** — 0.88 s of slack, before wall routing, jump cooldowns or
control effects are charged.

*Why first:* it is the safety net for every balance question in this report. Until it is
honest, you cannot tell which pacing concerns are real. Fix the fixture, re-solve SCT
targets against what it reports, then re-triage T5/T6.

`BalanceOracleTests.cs:15-45` · `Assets/_Game/Data/Classes/*.asset:18`

### T2. Live Play Mode check: does T1 overlap the deposit zone?
**CONFIRMED (static) · Owner: M · ~2 min**

Spawn/deposit sits at r=22.84; the deposit disc spans r 20.34–25.34 (`_depositRadius 2.5`);
the T1 pile surface plus `_collectReach 1.0` reaches inward to ~21.2. On paper collection
and deposit ranges **overlap**, and both run independently every tick — so a player can farm
and bank their doorstep pile almost without leaving spawn. GDD §3.2 documents this as "~8 m
off your mouth."

*Do this:* stand at spawn, don't move, watch whether Cargo and Bank both climb. Confirm
before changing any number — this gates Q1.

`MapGenerator.cs:225` · `PlayerBase.cs:24` · `FoodPile.cs:115`

### T3. Correct the stale documentation
**CONFIRMED · Owner: C · ~30 min**

Six separate errors, all of which have already caused wrong decisions this session:

| Doc | Says | Reality |
|---|---|---|
| `CLAUDE.md:11`, `.claude/CLAUDE.md:5` | "150 food", "3 minutes" | `FoodTargetToWin: 40`, `MatchDurationSeconds: 45` |
| `GDD.md` §5.2 roster table | speeds 7.5 / 9.0 | **9.0 / 10.5** |
| `GDD.md` §3.1 warning | "input still raw world space — hard prerequisite" | **already shipped and correct** |
| `GDD.md` §3.2 | Fatty sweeps "centre + two contested" | greedy route **never touches centre** |
| `ART.md:27` | "Fixed camera — full map always visible" | follow-camera, ~45% visible |
| `ART.md:167` | "no tall props near the centre" | written under the false premise above; blocks the cheapest fix for T7 |

*Why P0:* `CLAUDE.md` is the first file every agent reads. Its wrong numbers propagated into
this session's own analysis.

---

# P1 — Before the live test session

### T4. Make the map bake text-serialized and non-destructive
**CONFIRMED · Owner: C · ~1h**

`MapSceneBaker.cs:84-91` documents cloning `Game.unity` *specifically* to inherit text
serialization — "a scene that cannot be diffed or merged defeats the point of baking it for
hand-editing." The clone does get text, then `SaveScene` (line 129) writes it back to
binary. `Game.unity`/`Bootstrap.unity` start `%YAML 1.1`; `Map.unity` does not.

Separately, `BakeMapScene()` unconditionally `AssetDatabase.DeleteAsset(Map.unity)` then
re-clones — **any hand-edit is destroyed with no warning.** Nothing hand-authored exists
*yet*, so this is latent — but it goes live the moment anyone places a prop, which is
exactly what T6 requires.

`MapSceneBaker.cs:84-129`

### T5. Mark baked geometry static
**CONFIRMED (found independently by both engineering reviewers) · Owner: C · ~15 min**

All 26 objects have `StaticFlags = 0`, so no static batching — ground, 8 sector walls, 4
boundary walls and 13 zone markers each cost their own draw call. Free win against the
30 fps Android target. Set `BatchingStatic | OccluderStatic | NavigationStatic` at creation.

### T6. Make walls readable
**CONFIRMED · Owner: C (geometry) + M (art call) · ~2h**

`_interiorWallHeight = 1.1` against a **1.6 m chicken** — walls are shorter than the players.
At 0.5 m thick and 30° pitch, with one flat tint (`_interiorWallColor`) shared by both wall
types, they read as ground texture rather than barriers.

Worse, GDD §3.4 hangs the whole information-gap design on a "left = safe lap, right = risky
lap" rule that requires telling an outer-gap wall from an inner-gap wall at a glance —
and they are **visually identical**. The rule is currently unlearnable from the screen.

Minimum: differentiate the two wall classes by colour or texture, and mark the openings.
`ART.md:167` must be corrected first (T3) or this gets reverted by the next artist.

`MapGenerator.cs:69,71`

### T7. Make the boundary exist
**CONFIRMED · Owner: D → C/M · ~2h**

GDD §3.1 specifies a hybrid straight/curved boundary "delimited by props rather than a
uniform wall, so the arena reads as a place rather than a box." Actual: four axis-aligned
boxes forming a hard square, `_wallsVisible = false`, zero props. An **invisible box** —
precisely what the spec says it is avoiding. A player pushed against it hits nothing visible
and reads it as broken movement.

Cheapest interim: flip `_wallsVisible` and assign a material. Proper fix needs the prop pass
(see O2). Blocked on Q3.

`MapGenerator.cs:57,472`

### T12. Open a real gap between the T1 pile and the deposit zone
**CONFIRMED · Owner: C (after T2 + T4) · ~1h · Decision made 2026-08-03**

Maestro's ruling: *"pile should be far enough of the base so that delivering that cargo
requires some movement."*

**⚠️ The fix originally proposed in this report was backwards.** The level designer suggested
raising `PersonalPileRadius` to 19–20. The base sits **farther** from centre (r=22.84) than
the pile (r=17), so raising the pile radius moves it *toward* the base and makes the overlap
worse. The pile must move **inward**, or the base **outward**.

**Measured today:**

| | Value |
|---|---|
| Pile centre | r = 17 |
| Footprint 5.5 × 4.6 → half-extent along the 45° diagonal | 3.25 m |
| `_collectReach` | 1.0 m |
| **Collectible out to** | **r = 21.25** |
| Deposit disc inner edge (22.84 − 2.5) | **r = 20.34** |
| **Result** | **0.91 m overlap — no walk at all** |

**GDD §3.2's "~8 m off your mouth" is unreachable.** Neither lever can deliver it:
- *Pile inward alone:* walk = `16.09 − PersonalPileRadius`. Even 1 m of walk needs r ≈ 15.1,
  landing T1 on top of the T2 ring.
- *Base outward alone:* hard-capped by the boundary. The deposit disc must stay inside the
  arena: `(r + 2.5)/√2 ≤ 19.25` → base r ≤ **24.72**, i.e. `BaseInsetFraction` ≥ 0.08.

The 8 m figure and the base inset were authored independently and never reconciled. Update
GDD §3.2 to whatever value ships rather than leaving an unreachable target in the spec.

**Recommended change — move both, modestly:**

| Constant | From | To |
|---|---|---|
| `BaseInsetFraction` (`MapGenerator.cs:416`) | 0.15 | **0.08** (base r 22.84 → 24.72, the boundary-safe maximum) |
| `PersonalPileRadius` (`MapGenerator.cs:225`) | 17 | **16** |
| `_personalPileFootprint` (`MapGenerator.cs:138`) | 5.5 × 4.6 | **4.5 × 3.8** *(optional; buys ~0.55 m more)* |

Yields collectible edge r ≈ 19.69 vs deposit edge r ≈ 22.22 → **~2.5 m of genuine walk**
(~0.3 s at 9 m/s). T1 stays clearly yours: ~7 m from your base vs ~16 m to the nearest T2.

**Open sub-decision:** 2.5 m is a design choice, not a derived one. Pile footprint can be
traded for a longer walk if Maestro wants more.

**Sequencing:** do **T2** (confirm the overlap live) and **T4** (text-serialize the bake)
first — this change requires a re-bake, and until T4 lands the re-bake writes binary and
destroys anything hand-placed. `DataIntegrityTests`' base-zone-shadow pin stays valid
(it tracks `PlayerBase._depositRadius`, which is unchanged).

---

# P2 — Cleanup and hardening

### T8. NavMesh agent radius → 0.48
**CONFIRMED · Owner: C · ~10 min**
Baked at `0.4`, but `PinwheelLayout.ChickenRadius = 0.48` (0.4 controller + 0.08 skin) is
the real push-back distance. Bots path 0.08 m tighter than the controller allows, against a
corridor GDD §3.4 already describes as having ~0.15 m of slack. Players unaffected.

### T9. Add an out-of-bounds / respawn volume
**ASSERTED · Owner: C · ~30 min**
The agy engineer flagged boundary tunneling as critical: 0.5 m walls vs 0.578 m single-tick
displacement at 18.5 m/s (10.5 speed + 8.0 knockback, 32 Hz). Premises verified —
`CharacterController.Move()` at `ChickenMovement.cs:96`, 32 Hz tick, 0.5 m walls — **but the
conclusion is overstated**: `CharacterController.Move()` performs a *swept* test, so it does
not tunnel merely because displacement exceeds thickness. Real risk is confined to severe
frame drops. Cheap insurance is still worth it: there is no floor past the 38 m plane.

### T10. Delete dead configuration
**CONFIRMED · Owner: C · ~20 min**
`_lowObstacleCount = 6` / `_tallObstacleCount = 3` are live inspector values, but
`PinwheelLayout.Build` only ever emits `Standard` — zero `Terrain_Low`/`Terrain_Tall` in the
bake. Self-documented as dead at `MapGenerator.cs:759`, yet the inspector still invites
tuning. Also strip orphaned pre-v0.4 fields `PeckDamage: 28` (`Peck.asset:31`) and
`TrampleDamage: 50` (`FlyingPeck.asset`). Also fix `MapGenerator.cs:14-31`'s class doc,
which still describes the component as pure runtime-procedural.

### T11. Bring the additive map load under Fusion's scene lifecycle
**PARTLY CONFIRMED · Owner: C · ~1h**
`MapGenerator.Awake()` calls `SceneManager.LoadScene(..., Additive)` outside
`NetworkSceneManagerDefault`. The Claude engineering reviewer verified the **join race is
safe** (synchronous load in `Awake()` precedes Fusion spawning) — that half is fine. The
agy reviewer's separate concern stands: on rematch or host migration the scene is unmanaged,
risking duplicate or stale geometry. Lower priority than it first appears.

---

# Optional — bigger bets, not blockers

### O1. Macro-orientation layer (minimap / off-screen indicators)
There is **no minimap, compass, off-screen pile marker or rival indicator anywhere in the
client** — `TouchControls.uxml` is joystick + status strip + 3 hex buttons, and the only
screen-edge cue is combat-only. GDD §3.3 defends "you never see the whole map" as deliberate
tension, but two reviewers independently argued that without cues it reads as blindness, not
tension — especially when a player can burn 4 s walking to a pile someone already emptied.

Cheapest version: a directional ping toward your own base, reusing the camera-relative
bearing maths already proven in `HitFeedback`. **Highest player-experience payoff on this
list**, but it is a feature, not a fix.

### O2. Farmyard art pass
The scene is a scaled cube, 8 flat-tinted primitive cubes and 13 cylinder discs. No fences,
coops, haystacks or troughs — against `ART.md`'s "warm, funny, chaotic cartoon farm." A
player loading it cannot tell it is a chicken game. Also the natural vehicle for T6 and T7
(landmark props over wall openings, prop-dressed boundary), so doing those *first* and this
*separately* means doing the work twice.

### O3. Porous / asymmetric walls
The creative director's single highest-leverage recommendation: replace the eight rigid
single-opening walls with porous or low-profile prop barriers (jumpable haybales, breakable
fences, mid-wall gaps), to kill 1-D corridor traps and give a rooted player somewhere to go.
Interacts with Q4.

### O4. Widen the centre ring
`MinCorridorWidth = 2.0` is derived from `ChickenBodyDiameter` (0.8 m) — but effective
collision diameter is `ChickenRadius × 2 = 0.96 m`. Two chickens abreast occupy **1.92 m**,
so the real passing margin is **0.08 m, not the nominal 0.4 m**. The tightest corridor on the
map is also, by construction, the highest-traffic contest point. *(This one no reviewer
caught; it came out of verification.)* Cheapest fix is a fixed hub-plaza floor of 11–12
independent of centre-pile size. Blocked on Q2.

`PinwheelLayout.cs:66,76,84`

---

# Questions to answer

**~~Q1. T1 doorstep placement.~~ RESOLVED 2026-08-03 — see T12.**
Maestro: *"pile should be far enough of the base so that delivering that cargo requires some
movement."* Decision is to open a real gap. Exact values in T12.

**Q2. Centre corridor width.** Given the real 0.08 m passing margin (O4) — widen the hub
plaza, or let the centre be a deliberate pinball scrum?

**Q3. The boundary.** Build the curved, prop-dressed boundary GDD §3.1 specifies, or rewrite
the spec to describe the invisible square that actually ships? Note §3.7's sightline maths
was derived against the boundary shape that does not exist.

**Q4. Symmetry.** The creative director argues 4-fold rotational symmetry is wrong for a
45-second chaos game — fair, but it erases landmarks ("every corner is just a corner") and
flattens the meta. The level designer treats it as sound. Unresolved taste call. Note it
costs far less if quadrants are visually distinct (O2), so O2 may dissolve the question.

**Q5. Does the centre pile matter?** The greedy-optimal Fatty route never touches it —
outer-ring hops (~12.4 m) beat any centre approach (17–22.8 m). If that holds in play, the
centre-pile and wall-collapse system in GDD §3.6, described as balance-critical, goes unused.
Re-tune distances so the centre competes, or accept it as a contested-only prize?

**Q6. Race or timeout?** Solo clear times run 32.7–44.1 s against a 45 s cap *before* walls,
jumps and control effects. In practice most matches will end on "most banked at timeout"
rather than a race to 40. Intended, or should the target drop?

**Q7. Still outstanding from the feedback review.** Peck / Flying Peck now require the target
to be carrying cargo — a cargo-less rival takes zero knockback. Documented as intentional in
source, never confirmed. It is now load-bearing: it drives the telegraph's grey "no effect"
mark, so changing it moves both the preview and the ability.
