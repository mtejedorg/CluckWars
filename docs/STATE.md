# Project State — Snapshot

**Live source of truth for "where are we right now."** Updated each session.
Designed to be the first doc an agent reads after `CLAUDE.md` to understand
what's shipped, what's in flight, and what's blocked on testing.

---

## Latest tag

`v0.3.1-alpha` — Debug logging, deposit fix, cargo feedback, base tinting. Pinned at commit `7fe855e`.

Earlier tags:
- `v0.3.0-alpha` (`312f511`) — Pre-test polish + build tooling.
- `v0.2.0-alpha` (`7ee1742`) — Phase 9 polish (audio service, registries, reconnection).
- `v0.1.0-alpha` (`10e527c`) — Core gameplay loop closed.

All tags pushed to origin.

---

## 🧱 ADR 0003 Slice 1 — pile footprints + permanent centre (2026-07-22) — SHIPPED (untested)

Commit `856cd07` on `develop`. **Not pushed, not playtested.** Slices 2–4 (Vault /
`TerrainTraversal`, slot restructure, speed retune) are untouched.

**What changed**

- `FoodPile` root scale is now a **stepped** function of `Amount/MaxAmount`. The `Blocker`
  child holds the capsule + carving `NavMeshObstacle`, so root scale shrinks the mesh, the
  collider and the NavMesh carve together. Draining a pile permanently opens a lane.
- Stepping is quantised to **4 buckets**, applied in `Render()` only when the bucket changes
  — a carving obstacle re-carves the NavMesh on every resize, and per-frame re-carving
  thrashes bot `NavMesh.CalculatePath`.
- The **top bucket equals the previously authored size**, so the blocker never grows past
  what already shipped. The `CollectRadius` bound therefore holds by construction.
- **Centre pile is permanent** (`[Networked] bool IsPermanent`, stamped by `MapGenerator`
  via `onBeforeSpawned` — centre and outer piles share one prefab). Clamps drains at 60% of
  max, regenerates 0.5 food/s, never empties, never stops blocking. Its footprint steps over
  its usable `[floor, max]` range, so its size reads as contest intensity.
- `ChickenCargo` + `BotController` now gate on the new `FoodPile.Available` /
  `HasCollectableFood` instead of `Amount` / `IsEmpty`. Without this the centre would have
  been an **infinite food source** at its floor (cargo credits optimistically before the
  drain RPC lands), and bots would have parked on it collecting nothing forever.

**New tunables** (all serialized, no code edit needed): `_footprintSteps` 4 ·
`_minFootprintScale` 0.45 · `_permanentFloorFraction` 0.6 · `_permanentRegenPerSecond` 0.5 ·
`MapGenerator._centerPileIsPermanent` true.

**Blocker/collect margin at max** (chicken capsule 0.5 + skin 0.08, `CollectRadius` 1.6):
outer pile 0.65 → closest approach 1.23, **margin 0.37**. Centre pile 0.65 × 1.5 → 0.975 →
closest approach 1.555, **margin 0.045**. The centre's margin is thin but is *unchanged from
what already ships* — not a regression, but it wants widening (drop
`_centerPileVisualScale` to ~1.4, or raise `_collectRadius`) before the blocker is ever
allowed to grow.

**⚠ No Maestro prefab wiring required** — the new fields are code defaults on
`FoodPile.prefab` and `MapGenerator`, and `IsPermanent` is set at spawn, not authored.
Re-serialize the prefab only if you want to override a default in the inspector.

**Open / needs playtest**

1. **Regen rate 0.5/s is ADR open question 3 and the most sensitive number in the design.**
   Measure it first. Too high → endgame free farm; too low → centre erodes to floor in the
   first minute.
2. `FoodPileVisuals` already lerps the *mesh child* by fill ratio, so the mesh now shrinks
   twice (root step × child lerp). Probably desirable; confirm it reads well before tuning.
3. `GameManager.RestockPiles()` (comeback event) refills **all** piles by +10, which
   contradicts the ADR's monotonic-density pillar. Left alone — out of Slice 1 scope, but it
   needs a decision.
4. Bot pacing must be re-measured; all pre-2026-07-18 pacing data is void.

Verified: zero compile errors (Fusion IL weaver ran over the new networked property),
EditMode suite 8/8 green. **No play-mode or multi-client verification yet.**

---

## 🧭 ADR 0003 — movement, terrain & ability slots (2026-07-21) — DESIGN ACCEPTED

The feedback below converged into a full design, now recorded as
**`docs/adr/0003-movement-terrain-and-ability-slots.md`**. Read that before touching
movement, abilities, or piles. Headlines:

- **New design pillar — the map opens as the match progresses.** Food piles are consumable
  terrain: blocker radius + visual scale are continuous functions of `Amount/MaxAmount`, so
  draining a pile is a *permanent edit to the map*. Early game is a maze (Fatty strong), late
  game is a track (Speedy strong). Gives the match a tempo arc with no scripting, and turns
  the speed spread from a flat balance problem into a time-dependent one.
- **Three mobility currencies** (Run / Skip / Deny) via a new `TerrainTraversal` field
  (`None`/`Vault`/`Barge`/`Blink`). Fastest class gets no traversal; slowest gets the best.
  `MoveSpeed` spread compresses 2.5× → 1.4×.
- **Slot model:** 1 mandatory class-passive slot + 1 Common + 2 Character abilities.
  Assassin's COMBO passive grants a 3rd Character slot; OPPORTUNIST is the alternative fork.
  All 14 existing abilities are assigned to class pools — nothing wasted, nothing new needed.
- **Build order is strict** and starts with the pile slice (mostly plumbed already), NOT the
  slot restructure. Speed retune comes last.

Original raw feedback preserved below for provenance.

---

## 🎮 Maestro playtest feedback — movement & terrain (2026-07-21) — OPEN

First hands-on feedback of the test session. This is **design direction, not a bug list.**

**Core principle (new, load-bearing):**
> **A straight path should never work.** Chasing and escaping must be *routing* problems,
> not pure speed races. If the fastest chicken always wins by running in a straight line,
> movement abilities are decoration.

**Requested changes:**
1. **Bigger food piles — physical size, NOT food amount.** Keep `FoodAmount` as-is; grow the
   visual mesh + the `CreateBlocker` capsule so piles read as real obstacles you must route
   around. ⚠ Watch the coupling documented in `FoodPile.CreateBlocker`: the blocker radius
   (currently 0.65) is deliberately held *under* `CollectRadius` minus the chicken capsule so
   edge-collection still works. Growing the blocker without re-deriving that relationship
   will silently break collection again (the 2026-06-01 game-breaking bug).
2. **More interior walls.** `MapGenerator.BuildInteriorWalls` currently caps at 6 segments
   placed by rejection sampling. Raise the count/density so the arena has real lanes.
   Re-check the keep-clear zones (center pile, corner bases, island positions) and NavMesh
   bake — bots path via `NavMesh.CalculatePath`, so denser geometry must stay solvable.
3. **Define which abilities "skip" terrain.** ← *the real design task.* There is currently
   **no terrain-traversal concept in the ability system at all.** Needs an explicit spec:
   which abilities ignore walls/piles (jump-over? dash-through? teleport?), and how that is
   expressed on `AbilityBaseSO` (e.g. a `TerrainTraversal` enum: `None` / `OverLow` /
   `Through` / `Teleport`). Candidates to classify: Flying Peck, Roll & Push, Roll & Trample,
   Speed Burst, Doppelganger. Once abilities can bypass terrain, walls/piles stop being pure
   friction and become **counterplay** — which is the whole point of items 1 and 2.

4. **Class speed differential may be too high — but DO NOT tune it yet.** Maestro's read is
   that the fast/slow spread currently feels excessive. Explicitly **deferred until item 3
   lands**, because the two are coupled and tuning speed first would be thrown away:
   - a class that is *both* fastest *and* can skip terrain is doubly dominant;
   - a slow "bully" class is only viable if terrain gives it something speed can't buy —
     shortcuts it can take, chokepoints it can hold, or routes it can deny.

   Terrain traversal is the *other half* of the speed knob. Once abilities can bypass
   geometry, raw `MoveSpeed` stops being the only currency of a chase, and the right spread
   may fall out naturally. **Re-evaluate the spread only after the traversal spec exists** —
   then tune `ChickenStatsSO.MoveSpeed` per class against real routing, not straight lines.

**Sequencing note (strict):** item 3 is the design decision; 1, 2 and 4 are tuning that only
pays off once 3 exists. Doing 1+2 alone risks a slower, more annoying game rather than a more
tactical one; doing 4 alone gets re-done. Order: **3 → 1+2 → 4 → re-measure pacing.**
Route through `mechanics-designer` for the traversal spec, then `level-designer` for
wall/pile density, then a balance pass for speed.

**Fun assessment (Maestro, verbatim):** *"Game is still not funny but I think I see the path."*

Worth recording as a **diagnosis shift**, not just a mood. The 2026-06-13 read of "ultra
boring" was treated as a **juice** problem and answered with three juice passes (procedural
audio, control-state visuals, cast shake, aggressive bots). Those landed and the game still
isn't fun — so the remaining deficit is now understood to be a **movement/terrain design**
problem, not a feedback/polish one. That reframing is the most useful output of this session:
stop adding juice, fix what the moment-to-moment movement decision actually is. Items 1–4
above are the hypothesis; the next playtest tests it.

**Balance caveat:** bot pacing must be re-measured *after* these land — the Stage-B
double-speed-bot correction already invalidated all pre-2026-07-18 pacing data, and denser
terrain will slow routing further (the 2026-07-07 walls pass already cost ~20 s of pace).

---

## ⚠️ Menu keyboard shortcuts do not exist (documented 2026-07-21)

The `1-4 / S / H / J / SPACE` bindings described throughout older docs belonged to the
procedural-UGUI `CharacterSelectController`, **deleted** in the UI Toolkit migration.
`MenuUiController` never reimplemented them; only doc-comments reference the old class.
**Menus are click-only.** In-game keys (WASD, Q/E/R, F1, F2) are unaffected — they route
through `FusionNetworkService`'s input latch, not the menu.

Not a bug — a missing feature nobody noticed because the UI is clickable. Re-adding
shortcuts would meaningfully speed up multi-client test loops (4 clients × menu clicks per
run); logged as a candidate, not scheduled.

---

## ✅ Test-session setup pass (2026-07-20)

Pre-flight for Maestro's hands-on test session. Verified against the real Editor
(Unity 6000.3.14f1, MCP live) and the real asset files — not against doc prose.

**Verified healthy (no action needed):**
- **Compile clean.** `assets-refresh` (ForceSynchronousImport) → zero errors; every
  `CluckWars.*` type resolves. Covers checklist item 1 of the networking handoff.
- **`Chicken.prefab` is fully wired.** MCP component dump confirms `Fusion.NetworkObject`
  + **`Fusion.NetworkTransform`** (C1 fixed), and exactly **one** each of `BotController`
  and `ChickenVFX` (C2 duplicates gone). `ChickenMatchStats` + `BotController` both
  present — the two "pending Maestro prefab wiring" notes below were **stale**.
- **`_botLoadouts` intact** — all six presets with live ability GUIDs (it was wiped once
  by a bad scene save in B2, so it is now re-verified).
- **Logging is convention-clean.** No `Debug.Log` anywhere in runtime gameplay. The only
  hits are legitimate: editor-only tooling (`CluckWarsBuildMenu`, `TmpEssentialsAutoImport`),
  the sink itself (`UnityLogService`), pre-logger bootstrap (`ProjectInstaller`), and an
  `#if UNITY_EDITOR OnValidate` authoring check (`ChickenClassRegistrySO`).

**Added — the project's first automated tests.**
`Assets/_Game/Scripts/Editor/Tests/CoreLogicTests.cs` — 8 EditMode tests, **8/8 green**
via `tests-run`. Pins `UiGfx.Hex32` parsing, `MenuUiController.GetPassiveInfo` per-class
metadata (incl. the Assassin COMBO passive that gates the 3rd ability slot), and
`ChickenClassRegistrySO` lookup + its documented Warrior fallback.

> **Why an Editor folder and not an `.asmdef`:** game code lives in the predefined
> `Assembly-CSharp`, which a test `.asmdef` cannot reference. Wrapping the game in its own
> asmdef would be the "proper" fix but is **unsafe to do casually** — Fusion 2's IL weaver
> is configured against the current assembly set, and a new asmdef can silently stop
> `NetworkBehaviour` weaving (a clean compile will NOT catch it; it fails at runtime).
> Tests in an `Editor/` folder compile into `Assembly-CSharp-Editor`, which auto-references
> the game assembly — full access, zero weaver risk. Revisit the asmdef split deliberately,
> not before a test session.

**Doc fixes:** `.claude/CLAUDE.md` pointed at `Assets/Scenes/` for the main scenes; they
actually live in `Assets/_Game/Scenes/`. Corrected.

**Known cosmetic inconsistency (not fixed — design call):** the Warrior passive is named
`Tough` in code (`ChickenController.ToughDamageBonus = 1.25f`) and in these docs, but the
UI shows **MIGHTY**. The mechanic itself is real and correctly wired — Peck / CluckShock /
RollTrample all route through `ChickenController.ApplyOutgoingDamage`. Naming only.

---

## ⚙ Networking review fixes landed (2026-07-18) — EDITOR VERIFICATION PENDING

Full networking subsystem review (findings C1–L9) executed as
`docs/HANDOFF-NETWORKING-2026-07.md` via Antigravity (commits `fd3d237..8eae101`),
then QA'd by Claude (AOI revert `b4db53d` + fix-ups). What changed:

- **C1 fixed:** `Fusion.NetworkTransform` added to `Chicken.prefab` — remote chickens
  could never move before this (solo mode masked it). Script fileID verified by hash.
- **C2 fixed:** duplicate `BotController` + `ChickenVFX` removed from `Chicken.prefab`.
  **⚠ All bot pacing data before 2026-07-18 (incl. the WS1 overshoot numbers) was
  measured with double-speed bots — re-measure before any balance conclusions.**
- **Tick rate 64→32** (`NetworkProjectConfig.fusion`); dead `MatchConfigSO.TickRate`
  removed. Send rate 16 Hz via existing send indices.
- **Drain/deposit RPCs batched to ~4 Hz** (`ChickenCargo` accumulators). Known
  tradeoff: the optimistic-credit food-dup window (review M4) widens from ~1 tick to
  ~0.25 s under pile/pickup contention — documented, accepted for demo.
- **Master-leave resilience:** GameManager/FoodPile/PlayerBase flagged
  `MasterClientObject` (`Flags: 393217`); promotion re-arm poll in
  `MatchBootstrapper.Update`. Note: `DestroyWhenStateAuthorityLeaves` (0x40000) was
  deliberately left set — if the 3-client host-quit test still destroys world
  objects, clear it (→ `Flags: 131073`).
- **Photon `FixedRegion: eu`** pinned (M1 split-region join fix).
- **Physics layers** `Chickens` (8) / `Interactables` (9) authored; masks tightened
  from `~0`; buffers 16→32; aura slow now iterates `ActiveControllers` registry.
  Claude fix-up: agy set layers only on prefab roots — moved layer 9 onto the
  collider-bearing child GameObjects (FoodPile/FoodPickup/PlayerBase), without which
  all collection/deposit was broken.
- **Center pile scale** now `[Networked] VisualScale`, stamped `onBeforeSpawned`,
  applied before `CreateBlocker` (M2 cross-peer collision desync).
- **Hygiene:** verbose-log gates, runner restart cleanup (failed start no longer
  bricks retry), input latches cleared outside running matches, win-target fallback
  aligned to 110.
- **ADR 0002** (`docs/adr/`) records the Server Mode migration plan (H4) + RPC
  validation list (H5).
- **Reverted:** agy went off-script once and enabled AOI/interest management
  (explicitly out of scope) — reverted in `b4db53d`. Watch for this pattern.

**Editor verification checklist** (bottom of `docs/HANDOFF-NETWORKING-2026-07.md`) —
status as of the 2026-07-20 setup pass:
- [x] **1. Compile + prefab integrity** — zero errors; `Chicken.prefab` has
  `NetworkTransform` and no duplicate `BotController`/`ChickenVFX`. Done via MCP.
- [ ] 2. Solo run — bots at believable (halved) speed, single VFX bursts, no errors @32 Hz.
- [ ] 3. 2-client — **remote chicken visibly moves** (the whole point of C1), totals
      consistent across peers, center pile 1.5× on both.
- [ ] 4. 3-client host-quit — world objects survive, master promotes, match continues.
- [ ] 5. Intro button-mash — nothing fires at match start.
- [ ] 6. Bot pacing re-measure vs. the WS1 numbers (Stage B halved bot speed).

Items 2–6 need a human at the keyboard (play mode + multi-client) — they are the
test session. See the ordered plan in `docs/TESTING.md`.

---

## ⚙ In flight — full UI rebuild against the committed design (2026-07-12)

Rebuilding the entire UI to match `Design/CluckWars UI Design.html` + `docs/ART.md`
§6, because three prior attempts drifted by re-implementing the design's CSS gloss
by hand in C# (`UiGfx`). New approach: **share real assets with the design** and
use **UI Toolkit for all screen-space UI** (menus, HUD, overlays, touch controls —
chosen for mobile perf: retained-mode, no UGUI canvas-rebuild spikes). World-space
on-character bars stay sprite/TMP. This supersedes the old "HUD is UGUI" convention.

- **Stage 0 — asset pipeline (DONE, committed `4d790cd`).** `tools/export-design-assets.ps1`
  renders 39 design atoms via headless Chrome (`Design/export.html` + `export-atoms.jsx`)
  to `Assets/_Game/Art/UI/` (14 tintable atoms, 4 chickens, 19 icons, 2 backgrounds).
  Import settings applied by committed AssetPostprocessor
  `Assets/_Game/Scripts/Editor/UiSpriteImportSettings.cs` (Sprite type, per-atom
  9-slice borders, RGBA32-for-gradients) — **never hand-author sprite .meta YAML**
  (it corrupted 3 ways: truncated writes, invalid-GUID parse fails). USS token bridge
  `tools/generate-uss-tokens.ps1` → `Assets/UI/Styles/CluckWarsTokens.uss`. Verified
  in-editor: 39 sprites, correct borders, zero errors, self-healing on reimport.
- **Stage 1 — overlays (DONE, play-mode verified).** Replaced the procedural-UGUI
  match-end / lobby / intro-countdown / session-end panels (−453 lines from
  `MatchHud.cs`) with a UI Toolkit document `Assets/UI/MatchOverlays.uxml` +
  `Assets/UI/Styles/MatchOverlays.uss` (built on the Stage-0 sprites + tokens) and
  `Assets/_Game/Scripts/UI/MatchOverlaysController.cs` (self-injecting driver that
  mirrors MatchHud's old refresh logic). `MatchOverlaysUI` GameObject wired into
  `Game.unity` (UIDocument, shared PanelSettings, sortingOrder 100). Verified in
  play mode against the design captures (`CWMatchEndV3L` / `CWLobbyV3L` /
  `CWIntroCountdown`): all three render with full design fidelity — glossy panels,
  crown, winner ribbon + chicken platform, gold/silver/bronze medals, player-color
  bars, invite-code tiles, 2×2 player grid, gold countdown + glow + GET READY. Zero
  console errors; no double-rendered overlays. In-match HUD (leaderboard/timer/
  HP/cargo/hexes) still UGUI — that's Stage 2.
  - **Known gaps (data-availability, deferred):** per-row/winner equipped-ability
    hex chips (no per-corner loadout source), S/D (stuns/deposits) stat columns
    (only `Kills` networked → rows show `K{n}` only), and the design's LEAVE/REMATCH
    buttons (game auto-restarts; shows a "Starting next match…" line instead).
    Minor copy nuance: design personalizes the local win to "VICTORY!/YOU"; the
    controller always shows "P{n} WINS!". None block Stage 1.
- **Stage 2a — top-bar HUD (DONE, play-mode verified, committed `da1429d`).**
  Replaced MatchHud's UGUI leaderboard + timer with `Assets/UI/MatchTopBar.uxml`
  /`.uss` + `MatchHudController.cs` (4 per-player score pills + centered timer,
  ART §6.3). `MatchTopBarUI` wired in `Game.unity` (sortingOrder 90). −321 net from
  `MatchHud.cs`; kept HP/cargo (Stage 4), hit-flash, event banner.
- **Stage 2b — touch controls (DONE, play-mode verified).** Replaced UGUI
  `TouchControlsHud` with `Assets/UI/TouchControls.uxml`/`.uss` +
  `TouchControlsController.cs` (UITK joystick + 3 hex ability buttons, bottom-up
  cooldown clip, ability-accent tints via exported `Icon_*` sprites, slot-3 hidden
  unless Assassin). `TouchInputProvider` now reads `TouchControlsController.Instance`
  — same `Movement`/edge-triggered `AbilityNPressed` contract, gameplay input
  unchanged. Wired on `Hud/TouchControlsHud` GameObject (UIDocument sortingOrder 95,
  old component removed). Verified: controls render with correct accents/icons/badges,
  no double-render, zero errors, controller singleton bound.
  - **Cleanup (DONE, committed `4ec42f6`).** Deleted the now-dead UGUI trio
    `TouchControlsHud.cs` / `VirtualJoystick.cs` / `HoldButton.cs` (verified
    unreferenced by any live scene/prefab; only comment-only mentions remained).
    Same commit untracked `Assets/_Recovery/` (Unity crash-recovery scene swept
    in by a `git add -A` in `775ecae`) and added a `.gitignore` rule. The
    manifest/NuGet bumps in `775ecae` were a legit ivanmurzak MCP 0.82.4→0.83.1
    upgrade, kept.
- **Stage 3 — character-select fidelity (DONE, play-mode verified, committed
  `65496e4`).** Char-select was already UITK (menu migration) but leaned on
  procedural `UiGfx.Chicken()` blobs, emoji ability glyphs, and legacy
  `Assets/UI/Sprites/Hex*`/`RadialGlow`. Repointed to the Stage-0 exports:
  `Chickens/Chicken_*` (via new `.cw-chicken--<class>` USS modifiers on chips +
  preview + lobby cards), `Icons/Icon_*` (slot hexes, ability-grid cards, lobby
  mini-hexes), `Atoms/HexGlossy` (accent-tinted) + `Atoms/RadialGlow`. New
  `CluckWars.UI.AbilityIconStyle` single-sources the ability→`.cw-hex-icon--*`
  map (TouchControlsController dropped its private copy and uses it too).
  `CluckWarsTheme.uss` gained the 14 shared icon rules. UXML unchanged.
  - **Deferred:** shared `Gloss.png` sheen on menu cards/ribbons/buttons (spans
    MainMenu/Lobby — the later `CardBg`/`Ribbon`/`ButtonGrayscale` atom pass) and
    `UiGfx.Chicken()` in `MatchOverlaysController` (Stage-1 win screen) still live.
- **Deferred-from-Stage-3 cleanups (DONE).**
  - Match-end overlay chicken on exported sprites (`c5c8f86`) — retired the last
    `UiGfx.Chicken()` consumer; `MatchOverlays.uss` gained the `.cw-chicken--*` rules.
  - Menu surfaces on exported design atoms (`7b9b142`) — ribbons use the `Ribbon`
    atom (grayscale banner-with-tails, tinted gold); buttons use `ButtonGrayscale`
    (beveled pill, tinted per gold/green/neutral variant, border dropped since the
    bevel is baked, background cleared to transparent so the runtime-Button default
    grey stops showing through); the C#-colour-washed card surfaces moved to the
    `GlossOverlay` atom. **Deleted `Assets/UI/Sprites/`** — all four legacy sprites
    now unreferenced (verified by GUID). All screen UI paints from `Assets/_Game/Art/UI/`.
- **Stage 4a — on-character HP/cargo bars + identity ring (DONE, verified, `9201801`).**
  New `Assets/_Game/Scripts/Visuals/ChickenWorldBars.cs` on `Chicken.prefab`: HP bar
  (green→yellow→red off `ChickenCombat.HP / Stats.MaxHP`), cargo bar (hidden at zero
  cargo), and a per-corner Okabe-Ito identity ring at the feet. World-space
  SpriteRenderers (URP 2D sprite shader), billboarded; hidden while stunned.
- **Stage 4b — control-state overlays §6.10 (DONE, verified, `3a5abf2`).**
  New `ChickenStateOverlays.cs`: stun → orbiting stars, root → green foot blob,
  slow → pulsing blue foot blob (ability slow / Feather-Aura / pile drag). Nameplate
  gained 🌱 / 🐌 badges beside the existing ☠. Strictly additive sprites — they never
  touch the chicken's material or animator, so they can't fight the hit-flash.
  Verified live: dead P2 → stars + ☠ + bars hidden; slowed P4 → blue blob + 🐌;
  healthy chickens → no overlays.
  - **Deferred to an art pass** (these mutate the body and would fight the
    hit-flash/animator): stun desaturation + tilt, slow speed-trail, knockback motion
    lines / ghost trail (knockback also has no persistent replicated flag to observe).
- **MatchHud corner HP/cargo panel — REMOVED (Maestro confirmed).** Superseded by the
  Stage-4a on-character bars (§6.3: bars on the chicken, minimal HUD). Also swept the
  leftovers it orphaned, plus some stranded by the Stage-2a leaderboard removal
  (`_bases`/`HasStaleBase`, `PlayerColors`, `DtGoldMid`). `MatchHud` is now only the
  hit-flash + final-minute event banner — the two full-screen effects with no
  on-character or UI Toolkit home.
- **`UiGfx` procedural art generators — DELETED.** The rebuild's whole point: the
  runtime-baked rounded-rect / hex / gloss / circle / **chicken** sprites were the drift
  that made three prior attempts miss the design. Last callers went with Stage 4, so
  they're gone (−438 lines), along with the rasteriser and the UGUI/TMP helpers stranded
  by the UI Toolkit move. `UiGfx` is now just the §6.1 colour tokens + `Hex32` + the two
  brand fonts + `AddShadow`. **Do not add sprite generation back** — UI art comes from
  the design export, screen layout is UXML/USS, on-character indicators are world-space
  sprites.

### UI rebuild — CLOSED (2026-07-14, Stages A–G)

The rebuild itself is done (Stages 0–4 + cleanups). A 2026-07-14 re-survey against
ART.md §6 recalculated the remaining gaps and executed them as Stages A–G, one commit
each (source handoff doc removed after completion — verified via git log/play-mode,
see the QA pass note below). Highlights of the re-survey:

- **Biggest gap found: the match top bar is not §6.3.** What shipped (Stage 2a) is a
  centered chip strip `[P1][P2][TIMER][P3][P4]`; the design wants a **top-left ranked
  leaderboard panel** (rows sorted live by food, ordinal + glossy dot + P# + progress
  bar + gold score, local-row/leader highlights), a **★ FIRST TO 150 badge** (from
  `MatchConfigSO.FoodTargetToWin`), and a **top-right embossed timer badge**. → Stage A.
- **Correction:** `BarTrough.png` *was* exported and is committed since Stage 0
  (`4d790cd`). Stage B swapped `ChickenWorldBars`' procedural quads for
  `BarTrough`/`BarFill` and moved the cargo count from the nameplate to the bar stack.
- **Correction:** the match-end leaderboard already has medals + dots + winner ring
  (controller-built). Remaining §6.5 polish is background particles + per-row
  proportional score bars (Stage D). Per current ART.md, LEAVE/REMATCH buttons and
  S/D columns are **not** in the spec (restart countdown is) — dropped from scope.
- **Stage C (Menu Context):**
  - [x] Lore quote added under character name.
  - [x] Skin slots added (3 slots: Default + 2 locked).
  - [x] Console hints added (hidden by default).
- **Stage D (Match-End Polish):** [x] Background particles added; per-row proportional score bars implemented; winner row subtle accent background added.
- **Stage E (Ability Buttons):** [x] Audited hex button sizes/positions, cooldown numbers, and dimming (already matching the design spec).
- **Stage F (Hygiene Sweep):** [x] Added `AbilityIconStyle` loud-failure guard (`InitializeOnLoadMethod`); deleted `CargoHud` legacy IMGUI component and script.
- **Stage G (Full Verification + Docs):** [x] Full-flow verification complete (verified top-bar scoreboard, timer, touch controls hex layout, ability icons). Docs closeout complete.
- **2026-07-14 post-handoff QA pass** (independent review of the Stage A–G commits):
  confirmed Stages A–D and F–G as shipped correctly; Stage E's audit-only empty commit
  was verified accurate (TouchControls' §6.6 cooldown dim/number/badges were already
  correct from Stage 2b). Fixed one real gap the handoff itself specified but Stage A
  never implemented: the top bar (`MatchHudController`, a separate UIDocument from
  `MatchOverlaysController`) never dimmed to 40% opacity behind the intro countdown
  (ART.md §6.5). Added `MatchHudController.Instance` + `SetIntroDimmed(bool)` (mirrors
  `TouchControlsController`'s Instance pattern) and a `.cw-topbar--dimmed` USS class,
  toggled from `MatchOverlaysController.RefreshIntro`. Also removed a stale doc-comment
  on `MatchHud` claiming it "auto-disables any sibling CargoHud" (the method was deleted
  in Stage F but the comment wasn't), and deleted the now-empty `Hud/CargoHud`
  GameObject that Stage F's component removal left behind in `Game.unity`.

Still excluded from the handoff (unchanged): the **§6.10 body-mutating art pass**
(needs real VFX — would fight hit-flash/animator; knockback has no replicated flag),
**§6.8 console adaptation** (post-demo), and the **mobile/Pixel-9 device pass**
(physical device required — Maestro action).

Pipeline note: the Fable-pinned `code-architect` orchestrator hit its model limit
mid-session; orchestration continued on Opus (main session) delegating to
`ui-designer` / `senior-dev` (which inherit Opus).

---

## ✅ Full-project revision — CLOSED (2026-07-05 to 2026-07-07)

A complete code + design revision of the vertical slice was performed on
2026-07-05 (source handoff doc removed after completion — full history below and
in git log). Headlines: match economy is broken (win target unreachable for 3 of 4 classes,
fighting doesn't pay), Flying Peck is a 200-dmg instakill, Root Egg self-roots
its caster, the root-zone radius override is ignored, the death-drop pipeline
rides the ChangeDetector pattern already documented as unreliable in solo, and
player identity is fragmented (PlayerRef vs HomeCornerIndex vs BotClaimed —
source of the Spine-Coat-inert-vs-bots and rejoin corner-collision bugs).
The handoff defines workstreams WS0–WS5 with per-task acceptance criteria.

**Progress:** WS0 done (`5f23a7c`). WS1 + WS2 done and committed (session entry
below). WS3 + WS4 + WS5 were executed by Antigravity on 2026-07-06; a same-day
code review found two critical
defects, a rejoin flaw, and zero verification — **the fix-up items B1/B2/B3/B5/B6
were then applied on 2026-07-07** and the full §C SOLO verification pass ran
clean the same day (session entry below) — committed. The revision handoffs
are closed except two follow-ups: a WS1 balance overshoot (win target reached
in ~1 min; raise target or trim rates after a human feel-pass) and the §C
multi-client checks (3-client ownership + leave/rejoin), which need a human
session with a fresh Windows build.

---

## Session 2026-07-07 (b) — interior walls, solid piles, bot pathfinding

Maestro direction: chases were pure speed races — add walls and make piles
non-traversable so routing/juking matters. Implemented + verified live:

- **Interior walls** (`MapGenerator.BuildInteriorWalls`): up to 6 visible low
  (1.1 u) wall segments per match, placed by rejection sampling in an annulus
  with keep-clear zones around the center pile, corner bases, and nominal
  island positions. Walls are LOCAL geometry, so online layouts seed from the
  session name (same determinism trick as the corner permutation); solo rolls
  fresh each match. All tunables serialized.
- **Solid piles** (`FoodPile.CreateBlocker`): code-built child capsule
  (r=0.65 — under CollectRadius minus the chicken capsule, so edge collection
  still works) + `NavMeshObstacle` carve. Deactivates when the pile empties
  (stub walkable, mesh un-carves); reactivates on match-restart refill.
- **Bot pathfinding** (`BotController.ResolveSteerPoint`): runtime NavMesh
  baked in `MapGenerator.BuildNavMesh` (NavMeshSurface, physics colliders, so
  invisible boundary walls count); bots follow `NavMesh.CalculatePath` corners,
  recomputing when the target drifts >1 m; direct steering fallback. Solid-pile
  centers are off-mesh — `SamplePosition` snaps the path to the collectable rim.
- **Verified (solo play mode):** 4 walls placed, NavMesh 99 tris, cross-map
  path `PathComplete` with 4 corners; blockers solid=3 / walkable-empty=6
  mid-match; bots deposited 48/36/57 by ~1:07 (not stuck, contesting center);
  walls render as desaturated wood, see-over height; zero errors/exceptions.
  Side effect: routing slowed pacing slightly (nobody at 70 by 1:07 vs ~45 s
  before) — partially offsets the WS1 overshoot; re-measure before retuning.

---

## Session 2026-07-07 — fix-up of Antigravity's WS3–WS5 delivery

Review findings + rationale (source handoff doc removed after completion). Applied:

- **B1 (critical):** the three damage abilities (Peck / Cluck Shock / Flying
  Peck) passed `caster.Id` (ChickenController's behaviour id) into
  `RPC_ApplyDamage`, but the receiver resolves the attacker via
  `TryFindBehaviour<ChickenCombat>` — a different behaviour slot, so kill
  credit and Spine Coat reflect silently never resolved. All three now pass
  `casterCombat.Id`. Attacker id space is ChickenCombat everywhere.
- **B2 (critical):** Antigravity's scene save had wiped the five valid BOT-3
  loadout presets (authored in `3e44db0`) down to three empty rows → every bot
  spawned ability-less. Restored Bruiser/Skirmisher/Tank/Trickster/Thief
  verbatim (all ability GUIDs re-verified against `.meta`s) and added a sixth
  preset **Trapper** (Feather Trap + Peck, all classes) so the AbilityZone
  path is exercised by bots in solo.
- **B3:** `MatchBootstrapper.PickSpawnCorner` no longer trusts the sorted-
  roster index alone (slots shift after a leave; stamped `HomeCornerIndex`
  values don't → rejoiner collided with a live player's corner). It now scans
  forward from the preferred slot to the first corner not stamped on any live,
  non-decoy chicken (`IsCornerOccupied`). Fresh sessions behave identically.
- **B5:** decoy victims no longer credit kills (`CreditKillToAttacker` early-
  returns on `IsDecoy`) — a 4 s Doppelganger was a free kill every cooldown.
- **B6:** ARCHITECTURE.md pairing section rewritten (occupied-corner scan
  documented; stale "same modulo" sentence removed).

**Verification (solo play mode, Editor + MCP, 2026-07-07 ~16:50):** all §C
solo checks PASSED —
- Kill credit incl. bot attackers: organic match showed bot kills=2/1/0;
  forced Peck kill credited the human +1. (Impossible before B1.)
- Spine Coat reflect vs bots: forced test — bot HP 80→60 on reflected hit,
  reflector untouched.
- Death drop: 1 organic pickup + forced loaded-kill spawned pickup, victim
  stunned.
- Zone owner exclusion + radius: forced Root Egg at caster's feet, bot
  teleported onto it — sampler logged `humanEverRooted=False,
  botEverRooted=True, zonesLeft=0` (consumed); zone radius honoured the
  authored 0.8 override.
- Bot loadouts live (Bruiser/Skirmisher rolled); corner identity clean (human
  owns corner-0 base, bots claimed 1–3); restart teleported everyone home;
  grass floor renders (screenshot); ZERO errors/exceptions in the session
  window.
- Hardening added during verification: `ChickenCargo.FindNearestPickupInRange`
  now skips pickups whose NetworkObject isn't live (the subagent's session
  logged 54× `InvalidOperationException: FoodPickup.Amount … before Spawned`;
  not reproduced in normal play, but the physics-scan path had no
  `Object.IsValid` guard — `BotController` already had one).

**Open items:**
1. **Balance follow-up (WS1 overshoot):** the 70 target is reached in ~45–60 s
   (leader hit 75.5; next round leader was at 42 with 2:35 left). Design goal
   was ~70 % of the 180 s timer. Recommend raising `FoodTargetToWin` to
   ~110–120 (or trimming CollectionRate) after a human feel-pass.
2. **Multi-client (§C 6–8) still needs a human session:** 3-client corner
   ownership, leave/rejoin free corner (B3), cross-peer VFX. Requires a fresh
   Windows EXE build (`Ctrl+Shift+W`) + `tools/run-clients.ps1`; menu
   interaction per client makes it impractical to drive agent-only.

---

## Session 2026-07-06 — WS1 balance pass + WS2 fixes

**WS1 — Balance economy pass** (`5cc558b`, `c8697b3`). Rationale: at 1 food/s
and a 150 target, three of four classes could not reach the win target inside
the 180 s timer at all (real throughput ~0.4–0.6/s → 250–375 s); only Fatty
approached it, every match ended on the timer, and a Peck kill took 5+ casts —
farming was always safer than fighting. New numbers target uncontested
time-to-target ≈ 70 % of the timer so both win conditions are live and a stun
(5 s + cargo drop) is worth ~10–15 food of tempo:
- `FoodTargetToWin` 150 → **70** (MatchDuration unchanged at 180 s).
- CollectionRate: Warrior/Speedy/Assassin 1 → **2**, Fatty 2 → **3**.
- CargoCapacity: Assassin 4 → **5** (so Sneaky Steal isn't self-clamped),
  Fatty 25 → **20** (one haul ≠ 28 % of the target).
- Damage: Peck 15 → **28**, CluckShock 20 → **35**, FlyingPeck 200 → **50**,
  SneakySteal amount 5 → **4**. Code defaults in the four SOs match the assets.
- Slippery passive is now **duration-only** per GDD §5.2 — the undocumented
  50 % slow-magnitude reduction in `ChickenController.ApplySlow` was removed;
  GDD annex 13.2 marked resolved.

**WS2 — Small code fixes** (`03626ab`, `38b706a`, `61e15a7`, `2d836cd`, `f683331`):
- Flying Peck gained `IndicatorRange` / `RequiresEnemyInRange` overrides —
  HUD grey-out + TryActivate refusal, no more free-cast cooldown burn.
- `AbilityZone` has a `[Networked] OwnerChicken` (NetworkBehaviourId) stamped
  in `onBeforeSpawned` by Root Egg and Feather Trap; zones never affect their
  caster (root scan + `CheckAbilityZoneSlow` both skip the owner; GDD §7.2 note).
- Root zone scan uses the `TriggerRadius` property (networked per-spawn
  override) instead of the serialized prefab default.
- Death consequences (cargo drop, ability cancel) moved to a new
  `ChickenCombat.OnDeathAuthority` event invoked synchronously in
  `RPC_ApplyDamage` — no longer riding the Render ChangeDetector, which has
  silently skipped locally-written props in GameMode.Single. `OnDeath` kept
  for every-peer cosmetics.
- Cleanup: dead `KnockbackDecayRate` const removed from ChickenController;
  `MatchBootstrapper.Start` (async void) wrapped in try/catch logging via
  ILogService; the two `Debug.LogWarning` in RootEgg/FeatherTrap SOs removed
  (TODO(logging) left — no service locator); HP stamped in `onBeforeSpawned`
  from MatchBootstrapper (player + bot spawns, class-registry lookup) so
  proxies never see a transient HP=0/IsDead frame — lazy init kept as fallback.
  Note: `MatchBootstrapper.Construct` now also injects `ChickenClassRegistrySO`.

**Verification:** batchmode compile of the full project — zero errors (only
the two pre-existing CS0618 AndroidApiLevel24 warnings in CluckWarsBuildMenu).
Play-mode pacing/acceptance checks NOT run: the Editor was closed (Unity MCP
unreachable), and the WS4 wiring backlog (AbilityZone prefab +
`PrefabRegistrySO.AbilityZone`, `_botLoadouts`) means Root Egg / Feather Trap /
bot-ability behaviour isn't observable in solo yet anyway. Run the WS1/WS2
acceptance passes (pacing to 70, Peck TTK 3–4 casts, no self-root, death drop
×5) after WS4 wiring.

---

## What works (Phases 1–9 complete + Phase 10 complete + DCBA polish + v0.3.1 fixes + Phase R Part A + Phase R Part B)

### Core loop
- 4 classes (Warrior / Speedy / Fatty / Assassin) — selectable in Bootstrap menu.
  Each class has a named passive: Warrior=Tough, Speedy=Slippery, Fatty=Immovable, Assassin=Combo.
- Movement: keyboard (WASD) + touch (joystick). `CompositeInputProvider` ORs both.
  Keys: Q = Ability1, E = Ability2, R = Ability3 (Assassin/Combo only).
- **No basic attack** (v0.3). All combat is ability-driven.
  HP, hit reaction, 5-sec death stun still fully networked.
- Food collect from piles → carry → deposit at base.
  Pile slow: standing on a pile applies a 0.80× speed penalty (GDD §6.2).
  Collision slow: brushing another chicken applies a 0.75× speed penalty (GDD §6.1).
- Food drop on death; pickup-on-overlap.
- **13 abilities in the global pool** (all classes can equip any):
  Speed Burst, Egg Shell, Flying Peck (fka Roll & Trample), Invisibility,
  Spine Coat (now also knockbacks attacker), Turtle Mode, Sneaky Steal, Doppelganger.
  **New in Part B:** Cluck Shock, Peck, Roll & Push, Feather Trap, Feather Aura, Root Egg.
- 2 ability slots for all classes; 3 slots for Assassin (Combo passive gates slot 2).
- Global ability pool via `AbilityRegistrySO` — no per-class restrictions.
- All 4 control states now exercised by real abilities:
  - Knockback: Peck, Roll & Push, Spine Coat reflect
  - Root: Root Egg zone
  - Slow: Feather Trap zone, Feather Aura broadcast
  - Speed boost: Speed Burst, Roll & Push (caster-side)
- `AbilityCategory` and `BotRole` authored on every AbilityBaseSO subclass.

### DCBA polish (code complete — prefab wiring VERIFIED DONE 2026-07-20)
- **D — Dead visual states**: `ChickenVisuals` greys out + goes semi-transparent (0.40 alpha) when `IsStunned`. `ChickenNameplate` shows red "☠" while dead; restores the normal label on respawn.
- **C — Screen shake**: `MatchCamera.Instance.ApplyShake()` called on HP decrease (0.12 mag / 0.25s) and on death (0.35 mag / 0.45s). Linear decay, y-axis damped 0.3×. Only fires on `HasInputAuthority` (local chicken).
- **B — Match stats**: New `ChickenMatchStats` NetworkBehaviour (`[Networked] Kills` + `FoodDeposited`). `ChickenCombat.CreditKillToAttacker()` credits kills via RPC. `ChickenCargo.TryDepositAtNearbyBase()` records deposits. `GameManager.RestartMatch()` resets stats. Match-end overlay shows `food | kills` per player. ~~Maestro: add `ChickenMatchStats` component to Chicken prefab.~~ **DONE** — verified present on `Chicken.prefab` via MCP component dump, 2026-07-20.
- **A — AI bots (solo mode)**: `ChickenController.IsBot` [Networked] + `BotTick()`. `BotController` NetworkBehaviour (FSM: **Idle / CollectFood / ReturnToBase / Flee / Hunt**; throttled Think @ 0.3s). Phase R-Bot: 5-tier decision priority, `FindNearestRival` perception, `ReactWithAbility()` role-preference dispatch (Defense→Escape→Control on Flee; Steal→Offense→Control on Hunt), per-class personality (`ApplyClassPersonality()`). `MatchBootstrapper._soloBotsToSpawn = 3`; bots roll a random eligible loadout from the `_botLoadouts` preset pool (BOT-3, `TryPickBotLoadout` — class restrictions are bot-only flavor). ~~Maestro: add `BotController` component to Chicken prefab + author the `_botLoadouts` preset rows.~~ **DONE** — verified 2026-07-20: exactly one `BotController` on `Chicken.prefab` (no C2 duplicate), and all six `_botLoadouts` presets (Bruiser/Skirmisher/Tank/Trickster/Thief/Trapper) intact with live ability GUIDs in `Assets/_Game/Scenes/Game.unity`.

### Match lifecycle
- `GameManager` state machine: `WaitingForPlayers` → `Active` → `Ended` → restart.
- Host-controlled match start (lobby with "START MATCH" button for master client).
- Intro countdown ("3, 2, 1, GO!") before play begins each round.
- Win condition: first to `MatchConfigSO.FoodTargetToWin` OR most food when timer expires.
- Match restart loop: bases zeroed, piles refilled, pickups despawned, chickens reset + teleported to their corners.

### Networking
- Photon Fusion 2, Shared Mode. Solo mode = single-player Fusion runner.
- Photon Cloud relay already enables internet play (not LAN-only).
- Bootstrap menu: Solo / Host / Join. Host creates a UGS Lobby → unique 6-char join code. Joiner enters code or picks from lobby browser. Code becomes the Fusion session name.
- Photon AppId configured: `PhotonAppSettings.AppIdFusion = 259bda28-…`.
- Reconnection: `OnShutdown` → "SESSION ENDED" overlay → auto-return to Bootstrap.
- Connect / Disconnect / ConnectRequest / ConnectFailed callbacks all log explicitly.

### UGS (Phase 10 — code complete, pending Editor setup)
- `UGSService` (Auth + Lobby) bound in `ProjectInstaller` via `#if UGS_DISABLED` guard.
- `NullUGSService` used when `UGS_DISABLED` is defined (demo/offline fallback → "cluck-lan").
- Host: `CreateLobbyAsync` → join code displayed in Bootstrap + MatchHud lobby panel.
- Join: `JoinLobbyByCodeAsync` (type code) or `QueryLobbiesAsync` (browse list).
- Join code = Fusion session name — one code serves both UGS and Photon matchmaking.
- Lobby heartbeat (15s) runs automatically while host is in-session; stops on `LeaveLobbyAsync`.
- **Dashboard link done** (2026-06-11) — see "Project reorganization" session entry below.

### UI (Phase 10 visual redesign — Clash Royale/Supercell warm palette)
- **Design tokens** (ART.md §6, `cluckwars-tokens-v2`): panel `#3a2210` warm dark wood, gold `#f5c842`, green CTA `#33a332`, text `#fef5e0`. All inline in code; independent of serialised SO.
- **Player palette** updated to colorblind-safe Okabe-Ito set: Orange `#E8751A`, Blue `#1A7FC4`, Pink `#C4286F`, Teal `#0D9E7A`.
- **Bootstrap menu**: full-screen `#0e0804` background, warm wood panel, "CLUCK WARS" title in gold, section labels in gold accent. Keyboard shortcuts (1-4, S/H/J, SPACE). UGS lobby browser with scrollable list.
  - **Class stat cards** (Phase 11): flat class buttons replaced with tall cards showing a class-tint top strip, bold name, SPD/HP/CGO stat bars (normalized across all 4 classes from `ChickenClassRegistrySO`; falls back to design-time percentages when registry not bound), and ability-pool hint. Entire card is a `Button`.
  - **Ability slot picker** (B3): "ABILITIES" section replaced by a slot-selector row (Slot 1 / Slot 2 / Slot 3★) + scrollable ability grid grouped by `AbilityCategory` (Damage/Control/Defense/Utility), with cooldown tier badge (Short/Medium/Long). Click a card to equip it in the selected slot. Dedup: equipping an ability already in another slot swaps them. Slot 3 visible only for Assassin.
- **MatchHud** (UGUI): ranked leaderboard top-left (4 rows sorted live by food score, with player-color dot + progress bar + score). Timer badge top-right in gold. HP + cargo bars bottom-left above joystick. Centered overlays: lobby (with green Start button), match-end (ranked results), session-end, intro countdown (gold "3, 2, 1, GO!").
- **TouchControlsHud** (B4): joystick + 3 ability buttons. Attack button removed (v0.3); former attack position is now Ability3 (R key / Assassin slot). Ability tint = equipped `AbilityBaseSO.AccentColor`. Cooldown radial fill for all 3 slots. **Buttons grey out (alpha 0.45) while on cooldown** — v0.3 §10 hard UI requirement.
- **DebugHud** (F1 toggle): FPS, network state, GameManager state, local chicken stats, base ownership, pickup count.

### v0.3.1 — Debug logging, deposit fix, cargo feedback, base tinting

- **Debug logging**: `BotController`, `ChickenCombat`, `AbilityController`, `ChickenCargo`, `GameManager` fully instrumented. Bot FSM logs every state transition (Idle/CollectFood/ReturnToBase) with cargo fraction.
- **Deposit fix**: `GameManager.AssignBasesToPlayers` now uses nearest-position matching — fixes the 75% base-ownership failure caused by `MatchBootstrapper`'s Fisher-Yates corner shuffle vs. the old `PlayerId % 4` corner selection. `ChickenCargo.FindNearestBaseInRange` removed `Physics.OverlapSphere` dependency — uses `FindObjectsByType<PlayerBase>` with a 1.5 s cache; `PlayerBase` prefab no longer needs a trigger collider.
- **Cargo feedback**: `MatchHud` cargo bar shifts gold→orange→red, shows "FULL → RETURN TO BASE!" label at capacity. `ChickenNameplate` adds a world-space cargo line (`3/10`, orange ≥70%, red "■ FULL!"). `ChickenVFX` adds a looping gold orbit ring (`VFX_CargoFull`) while cargo is at capacity.
- **Base tinting**: `PlayerBase.LateUpdate` polls `Owner` each frame and applies corner-indexed colors (Orange/Blue/Pink/Teal) via `MaterialPropertyBlock` + direct material fallback. Replaced a broken `ChangeDetector + Render()` approach that silently skipped locally-written `[Networked]` props in `GameMode.Single`.

### Playtest fix (2026-06-01) — food collection range (game-breaking)
- **Food collection was completely broken** (found via MCP playtest): no chicken — bot or human — could ever drain a pile, so every match ran the full 3 min and ended 0–0–0–0 on the timer. The match-end + auto-restart path handled the all-zero tie without errors, which is why it had gone unnoticed.
- **Root cause:** `ChickenCargo.FindNearestPileInRange` / `FindNearestPickupInRange` / `FindNearestBaseInRange` compared the **full 3D distance** (`sqrMagnitude`) against the tuned radius, but a chicken's pivot floats ~1 unit above pile/base pivots (capsule centre). Bots parked at their arrival distance (~1.45 horizontal) from a 1.6-radius pile, yet the 3D distance was ~1.8 → always out of range. (Deposit's generous 2.5 radius happened to absorb the ~1 u Y offset, masking the same latent bug there — which is why the v0.3.1 "deposit fix" looked fine but collection never got the same treatment.)
- **Fix:** added `HorizontalSqr(a,b)` (XZ-only squared distance) and routed all three proximity gates through it — Y no longer gates interaction. Verified live in play mode: bots immediately began draining piles, filling cargo, and depositing; leaderboard climbed to 27 / 18 / 9 within ~20 s. Clean compile, no new errors.

### Menu UI pass (2026-06-01) — lobby redesign + char-select polish
- **Match Lobby rebuilt to the landscape design** (`Design/cluckwars-overlays-v3.jsx` `CWLobbyV3L`): 300px settings rail (MATCH LOBBY ribbon, host-only INVITE CODE card with gold letter tiles + SHARE/COPY, MATCH SETTINGS card, status pill, START + BACK) and a **2×2 player-card grid**. Cards are player-colored (Okabe-Ito P1 orange / P2 blue / P3 pink / P4 teal): accent bar, baked chicken sprite, name + Pn + HOST/CPU badge, `CLASS · PASSIVE`, ability mini-hexes (dashed for empty slots), and READY/PICKING badge. Solo fills 3 CPU bots (DashFox/BrunoB/PeckNoir); Host/Join show "WAITING FOR Pn" seats. Host pre-creates the UGS lobby and populates the code tiles; COPY/SHARE write the code to the clipboard. New markup `Assets/UI/Lobby.uxml`, styles in `CluckWarsTheme.uss` (`.cw-lobby-*`, `.cw-player-*`, `.cw-code-tile`, `.cw-status-*`), data wiring in `MenuUiController` (`BuildPlayerGrid`/`MakeLobbyCard`/`MakeMiniHex`/`SetCodeTiles`/`UpdateLobbyStatus`).
- **Character Select polish**: selected class chip now carries a faint class-color wash (not just a tinted border); equipped ability cards show a numbered slot badge (1/2/3) tinted to the ability accent — both per design `CWCharacterSelectV3L`. Softened the shared Gloss sheen on lobby rail cards so they read as dark surfaces instead of a bright grey band.
- Verified live (play mode, MCP screenshots): Solo lobby renders the 2×2 grid with correct per-class data; Assassin char-select shows ★S3 + numbered badges; no runtime exceptions.

### On-device fix (2026-06-01) — menu UI overflowed on the phone (PanelSettings scale mode)
- **Symptom:** on a Pixel 9 (real-device test), the menu UI rendered ~2.6× too large — Character Select's whole right column (ability grid + READY) was pushed off-screen, unusable. In the Editor at the same 2424×1080 resolution it looked perfect, which isolated it to **scaling, not layout**.
- **Root cause:** `Assets/Resources/PanelSettings.asset` had `m_ScaleMode: 1` = **ConstantPhysicalSize**, which scales the UI by **screen DPI** (Editor ≈96 dpi → ×1.0; Pixel 9 ≈420 dpi → ×~2.6). The `m_ReferenceResolution 1920×1080` + `m_Match 0.5` were set but **ignored** in that mode.
- **Fix:** `m_ScaleMode: 2` = **ScaleWithScreenSize** → UI now scales by resolution, so the phone renders like the Editor at the same resolution. Verified in-Editor at 2424×1080 (Pixel 9 landscape): Character Select and Lobby both fit fully. **Requires an APK rebuild to verify on device** (PanelSettings is baked into the player build).
- Tooling note: a co-op test skill was added at `.claude/skills/coop-test/SKILL.md` (`/coop-test`) for PC-Editor + Android-phone Photon testing. Driving the phone menu via `adb input tap` is unreliable (landscape app on a portrait `ROTATION_0` display → coordinate-space mismatch); have a human do phone-side taps, or drive only single gestures (joystick swipes).

### Map / camera
- `MapGenerator`: procedural plane + 4 invisible boundary walls + 4 corner bases + 1 large center pile + N small piles on a jittered ring. All master-spawned.
- Spawn points coincide with base positions (`Vector3.Lerp(corner, origin, 0.15)`); chickens spawn at their base.
- `MatchCamera`: orthographic isometric (45° yaw + 30° pitch), smooth-follows the local chicken with configurable `_followSmoothTime` (default 0.15s) and `_orthoSize` (default 8 — closer than ART.md §2's full-map fixed view).

### Registries (consolidated assets)
- `PrefabRegistrySO`: Chicken, Doppelganger, FoodPile, FoodPickup, PlayerBase, GameManager.
- `AudioRegistrySO`: Combat / Cargo / Ability / Match / Music clip refs.
- `ColorSchemeSO`: HUD palette, food-pile states, button colors, cooldown dim.
- `ChickenClassRegistrySO`: per-class stats + tint.

All bound app-wide in `ProjectInstaller`. Empty asset slots bind a runtime-empty instance with defaults so consumers never crash.

### Editor tooling
- `Cluck Wars / Build / Windows` (`Ctrl+Shift+W`) — `StandaloneWindows64`, output `Builds/Windows/CluckWars.exe`.
- `Cluck Wars / Build / Android` (`Ctrl+Shift+A`) — IL2CPP + ARM64 + minSdk 24, `Builds/Android/CluckWars-<version>.apk`.
- `Cluck Wars / Build / Windows + Android` (`Ctrl+Shift+B`) — sequential.
- `Cluck Wars / Build / Reveal Builds Folder`.
- `Cluck Wars / Balance / Balance Editor` — Editor window: editable table of all 4 `ChickenStatsSO` assets (HP / Spd / Turn / Cap / Rate — Attack fields removed in v0.3) + all 8 `AbilityBaseSO` assets (Duration / Cooldown / AccentColor). "Save All ★" flushes dirty assets. Auto-discovers assets — no list to maintain.
- **F2 in-game toggle** in `DebugHud` — runtime Balance panel (right side of screen): class stats table from `ChickenClassRegistrySO` + local chicken's equipped ability timings (Duration / Cooldown, active slot indicator).
- **MemPalace integration**: Local AI memory system installed in `.venv`. Configured with `mempalace.yaml`, `entities.json`, and `C:\Users\MARCO\.mempalace\identity.txt`. Mined 111 files including gitignored ones (like `.claude/skills/*/SKILL.md`) using `.venv\Scripts\mempalace mine --include-ignored .claude .`. Run `.venv\Scripts\mempalace wake-up` to load context.

### v0.3.2 — UI v3 design pass (Design.zip)

Applied the newest design wireframes (`cluckwars-hud-v3` / `-charselect-v3` / `-ability-ref` / `-tokens-v3`) to the code-driven UGUI. ART.md replaced with the design author's richer v0.3 spec and reconciled to the v3 wireframes (no attack button, control-state overlays, ability icons). GDD §6.4/§7.2 annotated with icons + overlay note.

- **Ability icons**: `AbilityBaseSO` gained an `Icon` (emoji glyph) field + `ResolveIcon()` + per-subclass `DefaultIcon`. All 14 ability subclasses carry their design-v3 glyph (🪽⚡🐦🌀🪤💨🌱🥚🐢🦔💨👻👥🤏). Existing `.asset`s need no re-authoring — blank `Icon` falls back to the subclass default.
- **TouchControlsHud**: hex ability buttons rebuilt to design v3 — centered icon glyph + short label, top-left slot-index badge (`1`/`2`/gold `★3`), centered seconds-remaining cooldown number, accent tint from `AbilityBaseSO.AccentColor`, icon dims on cooldown. Slot 3 hidden unless an ability is equipped in slot 2 (Assassin). MOBA arc positions: 2-ability stack / 3-ability triangle. Per-slot refs refactored into an `AbilityBtn[3]` struct array.
- **MatchHud**: leaderboard rows now show ordinal rank (`1st`–`4th`); added a `★ FIRST TO N` win-target badge under the panel (N from `MatchConfigSO.FoodTargetToWin`).
- **CharacterSelectController**: ability cards prefix the icon glyph; each class card shows its passive (name + one-line desc — Tough/Slippery/Immovable/Combo). Cards row height bumped to fit.

**Real fonts shipped**: `Assets/_Game/Resources/Fonts/` now contains the actual **Lilita One** (headings/labels), **Nunito** (body/descriptions), and **Noto Emoji monochrome** (ability icon glyphs) `.ttf`s, loaded at runtime via `Resources.Load<Font>`. No OS-font dependency. `UiGfx.ChunkyFont/BodyFont/EmojiFont` resolve them (OS/built-in fallback only if the Resources load fails).

**TMP for emoji icons**: legacy `UnityEngine.UI.Text` cannot render supplementary-plane emoji (🪽 = U+1FABD, 🪤, etc.), so the **ability icon glyphs use TextMeshPro** (`UiGfx.AddIcon` builds a runtime `TMP_FontAsset` from Noto Emoji and renders the glyph, accent-tinted). A `[InitializeOnLoad]` editor script (`Editor/TmpEssentialsAutoImport.cs`) auto-imports **TMP Essential Resources** on first compile so this works without the manual `Window ▸ TextMeshPro ▸ Import…` step. All other (BMP) text stays on legacy Text with the real Lilita One / Nunito.

**Procedural chicken art**: `UiGfx.Chicken(classKey)` bakes a per-class chicken figure (gradient body, head, comb, beak, eye, legs, shadow; class silhouette + colors from design v3) into a cached sprite — shown on every class card.

**Glossy visual rebuild (`UiGfx`)**: `Assets/_Game/Scripts/UI/UiGfx.cs` bakes rounded-rect / hexagon / circle / gloss-gradient / chicken sprites **procedurally at runtime** (no imported sprite assets needed) + the spec's text drop-shadow. Applied across all three code-driven UIs:
- **CharacterSelectController**: glossy rounded wood panel + gold ribbon title, glossy gradient class cards (class-color strip + gold selection glow), glossy accent-tinted ability cards, gold/green gradient buttons, rounded input fields, glossy lobby-browser panel. Class cards now use ignored-layout `GlossyBg` children + manual selection visuals (Button transition = None, clicks bubble from the bg frame).
- **TouchControlsHud**: ability buttons are now **pointy-top hexagons** (procedural hex sprite) with hex-masked radial cooldown; joystick base/knob and slot badges are real circles; chunky font + shadows.
- **MatchHud**: rounded glossy panels (leaderboard, timer, local stats, overlays), rounded target badge + buttons, chunky font + shadows.

**Still deferred from the full design** (next steps, not blockers): on-character control-state overlays (stars/vines/motion-lines/nameplate badges — ART §6.10), match-end/lobby/intro overlay art (crown, medals, ribbons, winner chicken), and moving HP/cargo bars fully onto the world chicken (§6.3 — `ChickenNameplate` already carries the world-space cargo line). A couple of the newest emoji (🪽 wing, 🪤 trap) may show a fallback box if the bundled Noto Emoji build lacks that codepoint; everything else renders.

**Pending Maestro (optional polish)**: align each ability `.asset`'s `AccentColor` to the ART.md §3 v3 hexes if desired; import Lilita One + Nunito + an emoji sprite asset as TMP fonts to render glyphs and the glossy type.

### Juice pass — stages 2 & 3: input fix, cooldowns, aggressive bots (2026-06-13)

- **Ability buttons "not working" — root-caused + fixed (commit `54d3a75`).** Input
  was a one-frame edge (`HoldButton.WasPressedThisFrame` / keyboard) but Fusion's
  `OnInput` runs at tick rate (~30 Hz) not per render frame (~60 Hz), so presses on
  non-tick frames were silently dropped (~half of taps did nothing). Movement was fine
  (continuous). `FusionNetworkService.Update` now latches each press edge and `OnInput`
  consumes it, so no press is lost. Touch + keyboard. **Pre-existing latent bug** — only
  surfaced when a human actually tapped (scripted activation bypassed this path). User
  confirmed fixed + feels better.
- **Cooldowns trimmed ~25–35% (commit `1bcd204`)** per feedback ("a bit too high").
  Short tier now 3s (Peck/RollPush), most Medium 6–8s; Doppelganger 15→11, CluckShock
  10→7. Edited the 14 ability `.asset`s directly.
- **Aggressive bots (commit `21195cf`)**: bots only hunted *loaded* rivals → passive vs
  an empty player. Warrior/Assassin/Speedy now engage any rival inside a tighter engage
  radius (8.5/6.5/6.0) even unloaded, when not hauling; Fatty stays the farming foil.
  Verified: empty player next to Assassin → it Hunts and closes in.

- **Networked control-state VFX triggers (commit `c6e80e6`)**: control-state rings +
  knockback shockwave were solo-only (read local fields). Now `[Networked] ControlVfx
  ControlFlags` (Slowed/Rooted, set on StateAuthority each tick) + `[Networked] byte
  KnockbackEventId` (one-shot, bumped in `ApplyKnockback`) drive the VFX on every peer.
  Architecture per Maestro: **network the minimal trigger, render particles locally** —
  the gameplay effect already replicated via the networked transform. Verified solo;
  cross-device replication is automatic via `[Networked]` (validate on multi-device test).

**Next (juice plan stage 3 — design/pacing):** still open — match length, map/food
density, win-target vs the 220 food on the map, fight incentives.

### Game-feel / juice pass — stage 1 of 3 (2026-06-13)

Maestro feedback: "0 feedback in abilities, game ultra boring." Agreed the ability
feedback was too narrow (only 3 of 13 abilities) and the game felt dead. Plan
chosen: **juice first, then bots, then design.** This is the juice stage.

- **Procedural audio (commit `…ProceduralAudioBank`)**: AudioRegistry was empty →
  total silence. `ProceduralAudioBank.FillMissing` synthesises every SFX at startup
  (sine/square/saw/noise + ADSR, click-free) — cast, hit, stun, deposit/victory arps,
  pickups, match cues. Wired in `ProjectInstaller`; only null fields are filled, so
  real `.wav`s override transparently. The game now has sound everywhere it had none.
- **Control-state visuals (commit `…ControlStateVFX`)**: the biggest gap — slow/root/
  knockback produced NO on-target visual. `ControlStateVFX` (Chicken prefab) draws a
  colour-coded ground ring on any affected chicken: yellow=stun, green=root, cyan=slow
  (+ white knockback shockwave). Reads StateAuthority control-state fields (solo-correct;
  **MP needs SlowMultiplier/Rooted/ExternalDisplacement networked** — noted in class doc).
  Verified: slowed→cyan, rooted→green, unaffected→none.
- **Cast/hit-connect shake (commit `f2080dd`)**: `ChickenVFX` shakes the local camera on
  cast — light for any ability, punchy for Damage. Range-gated Damage abilities can't
  fire without a target, so a Damage cast that lands here = a confirmed hit (no extra
  networking). Pairs with the audible cast SFX + accent burst.

**Next juice-pass stages (not yet done):** (2) aggressive bots that actually pressure
you so fights happen; (3) pacing/design tuning (cooldowns, map/food density, fight
incentives) — "boring" is partly a real design issue, acknowledged with Maestro.
Also still deferred: networking the control-state fields for MP feedback parity.

### Ability range/usability feedback (2026-06-13, commit `b86da7e`)

Maestro: targeted abilities were "super confusing" — no visible reach, no signal
for when a press would do anything. Added a layered feedback model:

- `AbilityBaseSO` gained `IndicatorRange` / `RequiresEnemyInRange` / `IsUsable(caster)`
  + a `HasEnemyInRange` helper. **Peck**, **Cluck Shock**, **Sneaky Steal** override
  them (Steal additionally requires the target to be carrying cargo).
- `AbilityController.TryActivate` now refuses to fire a range-gated ability with no
  valid target — the cooldown isn't burned on a guaranteed whiff.
- `TouchControlsHud` ability buttons have **three states**: accent+dimmed on cooldown,
  desaturated grey when ready-but-no-target, full accent when actually usable.
- New `AbilityRangeIndicator` (on the Chicken prefab): a local on-ground **range ring**
  that's faint when no target is in reach and **brightens + thickens** the instant an
  enemy enters, plus an expanding **cast flash** at the ability's true radius when it
  fires. Local VFX only (no RPCs), same `ActiveSlot`-poll pattern as `ChickenVFX`.
- Verified live in solo: ring + both buttons grey with no enemy near; ring turns gold
  and buttons colour (PCK red, CSK yellow) when a bot is inside range. `IsUsable`
  confirmed `False`@far / `True`@1.4u.

> **Tooling gotcha (next agent, read this):** a new `.cs` written to disk *while the
> Unity Editor was busy/MCP-disconnected* imported with a `MonoImporter` + `.meta` but
> was **silently excluded from Assembly-CSharp's source list** — `assets-refresh`,
> `ImportAsset(ForceUpdate)`, deleting the `.meta`, and `RequestScriptCompilation` all
> failed to add it (type stayed MISSING, `scriptCompilationFailed=False`). Diagnosis:
> `CompilationPipeline.GetAssemblies()` → `Assembly-CSharp.sourceFiles` didn't contain
> the path. Fix that worked: `AssetDatabase.DeleteAsset(path)` then **recreate the file
> through Unity's own `script-update-or-create` tool** (param keys: `filePath` +
> `content`) so Unity registers it cleanly. Prefer creating new scripts via that tool
> rather than the raw Write tool when the editor may be mid-reload.

### Solo-first polish pass (2026-06-12) — scoring fix, GDD map, bot AI, placeholder models

Direction set by Maestro: perfect solo mode before returning to multiplayer.

- **Solo scoring fixed (commit `e03b106`) — bots are real competitors now.**
  Root cause: bots share `[Player:None]` input authority, so they deposited at *any*
  unowned base (pooled, misattributed scores) and both win checks skipped
  non-real-player bases — bots could never win, so solo matches carried no balance
  signal. Fix: `[Networked] int HomeCornerIndex` on `ChickenController` (stamped at
  spawn for humans + bots), corner-gated deposits, `PlayerBase.BotClaimed` +
  `IsClaimed`, bot-inclusive win checks, `GameManager.WinnerCorner`, corner-true
  restart teleports (the old PlayerId-modulo mapping ignored the corner shuffle).
  Identity unified on corner numbers (P1–P4 + "(CPU)") across leaderboard,
  nameplates, base tints, and the winner banner. Verified live end-to-end.
- **Map now builds the GDD §3 layout (commit `4fc3036`)**: center pile (60) + 4
  personal islands (15, on each base→center line) + 4 contested islands (25,
  between adjacent corners), positions jittered per match. Replaces the old
  center-80 + ring-of-4×30. Total food 220.
- **Bot AI (same commit)**: BOT-8 hysteresis (state exit radii ×1.35), Hunt re-scans
  for a *loaded* rival when the nearest is empty, bots perceive ground pickups
  (scoop death-drops instead of walking off), contest-pile Control cast range-gated.
- **Placeholder models (commit `1f419b9`)**: `PlaceholderMeshFactory` +
  `PlaceholderModel` swap meshes at Awake — chunky chicken (GDD §4 silhouette),
  grain-mound piles, nest-with-eggs bases. Tints/scale feedback/colliders untouched.
  Remove the component per-prefab when real art lands.
- **Log floor raised**: `ProjectContext` `_logMinLevel` Verbose → Debug (per-tick
  cargo Verbose spam filled the entire console buffer and masked Info diagnostics).
- **Still open in the solo track**: collision/zone polish verification (BUG: none
  observed — needs an ability-heavy play session), Editor/runtime balance pass with
  the new map totals, ground/visual treatment for the arena (bare grey plane).

### Project reorganization (2026-06-11) — UI consolidation, BUG-4 fix, test infra

- **UI consolidated on UI Toolkit (Path B of `docs/UI_HANDOFF.md` — now marked RESOLVED).**
  The Bootstrap menu is solely `MenuUI` (`UIDocument` + `MenuUiController`) + `Assets/UI/*.uxml`
  + `CluckWarsTheme.uss`. Deleted the failed atomic-prefab experiment: inactive `MenuCanvas`
  removed from Bootstrap.unity, `CharacterSelectController` component removed from the
  `Bootstrap` GO, 6 dead UI scripts (`CharacterSelectController`, 5 `Ui*View`), 5 one-shot
  editor scripts (`UiPrefabBuilder`/`2`, `WireMenuScene`, `FixPrefabsAndCanvas`, `MenuUiWiring`),
  and 5 broken prefabs in `Assets/_Game/Prefabs/UI/`. `UiGfx`/`SDFImageEffect`/SDF shader kept —
  the in-game HUD still uses them. **Verified live**: clean compile (0 errors), MainMenu +
  Character Select render the v3 design correctly in play mode (MCP screenshots).
  New rule in `docs/CONVENTIONS.md` § UI rules: menu UI is UXML/USS only; never procedural UGUI.
- **BUG-4 FIXED + verified**: removed `UGS_DISABLED` from Standalone scripting defines —
  all platforms now bind the real `UGSService`. Verified in Editor play mode:
  `IUGSService` resolves to `UGSService`, `InitializeAsync` + `SignInAnonymouslyAsync` succeed
  (project IS Dashboard-linked). The fixed `cluck-lan` session is gone; hosts always get a
  real lobby code. Editor↔Android both-HOST mismatch eliminated.
  **Windows EXE must be rebuilt** (`Ctrl+Shift+W`) to pick this up.
- **Test infra**: `tools/run-clients.ps1` launches N windowed Windows clients with per-client
  logs (`Builds/Windows/logs/clientN.log`) — the standard multiplayer loop (emulators remain
  dead). TESTING.md updated (launcher + real-UGS multiplayer flow).
- **Repo hygiene**: root scratch files purged + gitignored (`args*.json`, `*_output.txt`,
  `scratch/`, `.agent/`, `*.cs.bak`), `Design/` wireframes now tracked,
  `UI_HANDOFF.md` moved to `docs/`.

---

## Outstanding before next test session

**Verified against live code/assets 2026-07-20; housekeeping pass same day closed
3 of the 4 items found in that verification** (Doppelganger fix committed `ece94b1`,
Assassin slot-3 assigned `183e421`, dead duplicate prefab removed `4ba742d`). One
real item remains:

1. **Run the networking editor-verification checklist** — see
   `docs/HANDOFF-NETWORKING-2026-07.md` bottom section. Stages A–K are code-complete
   and committed, but the checklist itself (2-client remote-movement, 3-client
   host-quit, intro button-mash, bot pacing re-measure) has never been executed.
   This is the actual gate before scheduling a multi-client test session.

Optional / low-priority:
- **ColorScheme.asset**: hit "Reset" in Inspector for Phase 10 warm palette on `TouchControlsHud`.
- Fill `AudioRegistry` clips (currently silent → procedural audio bank fallback covers it).
- Human feel-pass (from the improvement-plan closeout): deposit interception window,
  banner readability, observe the *Restock* comeback event at least once, sanity-check
  kill-bounty generosity.

### Pending agents (code)
- Phase R Part A + Part B: **complete** (code + assets, verified). See ROADMAP.md § Phase R.
- Phase R-Bot: **complete**, including BOT-3 randomized loadout — 6 presets authored
  in `Game.unity` (`_botLoadouts`: Bruiser/Skirmisher/Tank/Trickster/Thief/Trapper).
- `ISessionSelectionService` carries `Ability0`/`Ability1`/`Ability2`; `MatchBootstrapper`
  calls `AbilityController.SetSlots(slot0, slot1, slot2)` in `onBeforeSpawned`.

---

## Known bugs / open questions (need device testing)

1. ~~**Join case-sensitivity**~~ **FIXED (2026-06-03)** — `NullUGSService.JoinLobbyByCodeAsync` was uppercasing the join code to "CLUCK-LAN" while host used lowercase "cluck-lan". Photon room names are case-sensitive so the two clients never met. Fix: removed `.ToUpper()` from `JoinLobbyByCodeAsync` — both sides now use the code verbatim (lowercase).
2. ~~**RestartMatch Y=0 fall-through**~~ **FIXED (2026-06-04, commit d69fad3)** — `GameManager.RestartMatch()` called `RPC_TeleportTo(spawnPoints[i])` with raw Y=0 positions. `MatchBootstrapper` initial spawn adds `+0.05f Y` jitter; restart did not. On Android (IL2CPP real device) the chicken clipped into the floor mesh and fell infinitely on every round restart. Fix: added `Vector3.up * 0.05f` to both real-player and bot teleport targets in `RestartMatch()`.
3. ~~**ChickenCargo 3D distance**~~ **FIXED (2026-06-04, commit 61c46d8)** — See "Playtest fix" above. Previously `FindNearestPileInRange` used a 3D `sqrMagnitude` check, but the code was duplicated in the original commit without the XZ-only fix being applied to all three proximity methods. All three now use `HorizontalSqr()`.
4. ~~**UGS mismatch: Android real UGSService vs Editor NullUGSService**~~ **FIXED (2026-06-11)** — removed `UGS_DISABLED` from Standalone defines; all platforms use the real `UGSService`. Verified in Editor play mode (init + anonymous sign-in OK). Rebuild the Windows EXE to pick it up.
5. **Third player can't connect** (reported 2026-05-08). **Partially retested 2026-06-04** — 2-player session confirmed working. 3-player test not yet run this session.
6. **Solo-on-Android movement** (reported 2026-05-08). Not explicitly retested this session.

---

## Deferred work — bundled for the dedicated test session

All require real-device or playtest data; queued so they don't get done piecemeal.

- Network desync hunting (cross-device LAN).
- On-device FPS / draw-call / memory profiling.
- AnimatorController state authoring — **state machine needs update** (replace `Attack` trigger with `AbilityCast` trigger per Phase R A10). States: Idle/Walk/AbilityCast/Hit/Stunned. Needs `.anim` clip assets once artwork is recorded/imported.
- VFX particle systems — **code complete** (`ChickenVFX.cs`: hit sparks, death burst, stun orbit, deposit gold shower, **ability accent burst**). `VFX_Ability` PS: 16-particle sphere, tinted dynamically from `AbilityBaseSO.AccentColor` at activation, floats upward. Maestro: add `ChickenVFX` component to Chicken prefab.
- Audio clip recording / mixing.
- Balance pass (food rates, ability cooldowns, attack damage, HP).
- Mobile layout fine-tune for actual phone aspects.
- `MatchCamera._orthoSize` / `_followSmoothTime` final tuning on device.

---

## Testing strategy (updated 2026-06-04)

**Android x86_64 emulators are not viable** for Unity 6 URP on this machine (Hyper-V blocks
every GPU mode that supports ES 3.1+). See `docs/TESTING.md` § Known gotchas for the full
failure matrix.

**Co-op test completed 2026-06-04 — Editor + Pixel 9:**
- Solo mode (Editor): ✅ 4 chickens, bots, food loop, zero errors.
- 2-client multiplayer: ✅ Editor joined Pixel 9's real UGS session (`PNDMWK`) via JOIN-by-code flow. 2 chickens confirmed in same Photon room (`players=2`).
- Join-case fix (508f6e6): ✅ `JoinLobbyByCodeAsync("PNDMWK")` returned verbatim → correct session joined.
- Round restart: 🐛 Fixed (d69fad3) — Y=0 teleport caused Android fall-through. Now +0.05f Y.
- Food collection: ✅ Cargo draining confirmed in both solo and 2-player.
- **Key discovery:** Android APK uses real `UGSService` (Dashboard linked); Editor uses `NullUGSService`. Both-HOST fails. Connect via JOIN-by-code instead. See bug #4 above.

**Next test priorities:**
1. Rebuild Android APK (to pick up d69fad3 + 61c46d8 fixes), retest round restart on Pixel 9.
2. Test 3-player connect (BUG-5).
3. Add `UGS_DISABLED` to Android build defines OR fully enable real UGS on all platforms.

**Infrastructure:**
- ADB v40/v41 war resolved: SDK platform-tools ADB replaced with v41 copy.
- `cluck_emu2` AVD created but not useful (Hyper-V + ES 3.1+ conflict).

---

## Session 2026-07-10 — Improvement Plan (IP0 to IP8) Complete

Applied the full gameplay and system improvement plan (source doc removed after completion) across all nine workstreams:

- **IP0: Quick correctness + identity fixes:**
  - Corrected spelling of `Speedy` class display name.
  - Implemented deterministic tie-breaker in `GameManager.EndOnTimerExpiry` (highest food, then kills, then lowest base corner index).
  - Renamed Warrior passive display from "Tough" to "Mighty" (+25% outgoing ability damage).
- **IP1: Timed deposit:**
  - Converted instant deposits to rate-based deposits of 6 food/sec.
  - Added visual HUD `DEPOSITING...` indicator state.
  - Latched bot `ReturnToBase` behavior until cargo is empty.
- **IP2: Final-minute comeback events:**
  - Rolled a random comeback event at T-60s remaining:
    - **Golden Pile:** Spawns 25-food pile near center.
    - **Underdog Surge:** Buffs last-place player's movement speed and collection rate.
    - **Leader Bounty:** Marks current leader with a nameplate star ("★") and drops +8 food on death.
    - **Restock:** Refills all active piles by +10 food.
  - Polled event state in `MatchHud` to display centered alert banner and small persistent label, and play audio cue locally.
- **IP3: Kill bounty:**
  - Any non-decoy death spawns +5 food pickups between victim and killer (60% killer bias).
- **IP4: Economy retune:**
  - Raised default match win target from 70 to 110 food.
- **IP5: Assassin rescue:**
  - Tuned Assassin stats: Move Speed 8 -> 9, Cargo Capacity 5 -> 8.
  - Tuned Sneaky Steal: Cooldown 6 -> 5, Steal Amount 4 -> 6.
- **IP6: Noise abilities duration:**
  - Bumped Invisibility duration from 2 to 4 seconds.
- **IP7: KPI instrumentation:**
  - Added automatic tracking of pickups spawned and collected.
  - Logs a structured, grep-able `MatchSummary` block on state authority at match end containing length, winner, active event, player kills/deposits, and pickup counts.
- **IP8: Documentation sync:**
  - Synchronized GDD, STATE, and ROADMAP docs.

**Verification Status:**
- Fully compile-verified locally via `dotnet build` with zero errors.
- **Play-mode verified 2026-07-11** (solo bot matches driven via Unity MCP, Editor play mode; 3 full matches, zero errors):
  - **IP1 deposit drain:** "Deposited complete" logs fire from the per-tick path; base totals climb gradually; bots complete full deposits. No Zenject exception on chicken spawn (IP-fix1 confirmed live).
  - **Events — 3 of 4 observed** (random roll, one per match): *UnderdogSurge* applied to the true last-place chicken (corner 0, food 0); *LeaderBounty* marked the true leader (corner 2, food 103); *GoldenPile* spawned at (-5.7, 0, 1.7) with 25 food, first-candidate placement, no overlap warning. *Restock* not yet rolled — verify when it comes up naturally or force via a debug default.
  - **HUD:** persistent event label renders beside the timer; end-of-match overlay + auto-restart loop work; "FIRST TO 110" target displayed (IP4).
  - **IP4 pacing:** all 3 matches ended by reaching 110 before timer expiry. Matches 1–2 ended ~120–135 s (shortly after the T-60 event; exact lengths lost to the pre-IP-fix6 logging bug), match 3 measured 165.8 s. Winner totals 110.1 / 110.3 / 110.5. Within or marginally above the 100–160 s design band — no retune needed, but it sits at the slow edge; revisit after the human feel pass.
  - **IP3/IP5 signal:** hunter bots posted 9–14 kills per match and an Assassin bot won match 3 (110.5 food, 14 kills) — fighting now pays, possibly generously; watch kill-farming in the feel pass.
  - **Two new defects found by the summary instrumentation, fixed and re-verified:** IP-fix6 (Match Length always logged 180.0 — `TimeRemaining` reads 0 once `State` leaves Active; elapsed now captured at `EndMatch` entry) and IP-fix7 (Chicken.prefab carried a duplicate `ChickenMatchStats` component — pre-existing authoring slip — producing ghost zero rows in the summary and risking a wrong tie-breaker kill count; removed from the prefab, variant re-baked, plus a primary-component guard in code).
- **Pending Maestro:** human feel pass (deposit interception window, banner readability on device), the *Restock* event observation, kill-bounty generosity check, and the usual Android device smoke test.

### Code-Review Follow-up Fixes

Applied and committed four sequential fixes on `develop` based on code-review feedback:
1. **IP-fix1:** Updated self-injection in `ChickenCargo.Spawned()` to search `SceneContext` first before falling back to `ProjectContext.Instance`, preventing Zenject unresolved-dependency exceptions for runtime-spawned chickens (which need `MatchConfigSO` bound only in scene scope).
2. **IP-fix2:** Stored the spawned golden pile `NetworkObject` in a private field `_eventPile` in `GameManager` and despawned it upon `RestartMatch()` (if still valid) to prevent golden piles from persisting and multiplying across matches.
3. **IP-fix3:** Added rejection sampling (up to 8 candidate positions) in `SpawnGoldenPile()` using `Physics.CheckSphere` to prevent the golden pile from spawning inside interior walls, bases, or other piles.
4. **IP-fix4:** Cleaned up logging tags in `TriggerFinalMinuteEvent()` by removing the `[MatchSummary]` prefix to preserve the rule that `[MatchSummary]` is printed exactly once per match.
5. **IP-fix5:** Corrected the IP-fix3 overlap sphere — at `y=0.6, r=1.0` it dipped below the ground plane and rejected every candidate; raised to `y=0.8, r=0.7` so only walls/blockers/bases reject a spot.


---

## Recent commits (most recent first)

```
126ee4b Docs: add Antigravity CLI (agy) guide
e8c326c Chore: update packages and NuGet DLLs
b5ede4a UI v3: SDF shader system, atomic view components, UXML + Bootstrap wiring
61c46d8 Fix: ChickenCargo proximity checks use XZ distance instead of 3D
d69fad3 Fix: RestartMatch teleports chicken to Y=0 causing Android fall-through
5d67dd0 Docs: record emulator failure matrix + pivot to Windows-first testing
f53aaef Docs: update STATE.md recent commits after co-op test session fixes
508f6e6 Fix: NullUGSService join case bug + build menu restore-target + test docs
0772810 Part B — B3/B4/B5: bot AI, ability grid UI, cooldown grey-out
c3f70c9 Part B — B2: new ability SOs + SpineCoat knockback
b120170 Part B — B1: interaction primitives (knockback, root, placed zones)
578a2ce Phase R Part A: v0.3 parity refactor
7fe855e Debug logging, deposit fix, cargo feedback, base tinting  ← v0.3.1-alpha
1aa3b93 Fix: multiple chickens spawning at same base
540797a Visuals: per-class scale applied at spawn
90a071f Balance: faster movement + closer camera (test feedback)
ca39dfb Spawn: random starting edge per session
312f511 Build menu                                           ← v0.3.0-alpha
7ee1742 Phase 9 wiring                                       ← v0.2.0-alpha
```

`git log --oneline -20` for more.

---

## Branch / push status

- All work on `develop`.
- `main` has not been merged since project start (per Maestro's decision — wait for stable test pass before promoting).
- `develop` and `v0.3.1-alpha` pushed to origin. Branch is clean.
