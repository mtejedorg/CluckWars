# HANDOFF — UI Final-Design Completion (all stages, single run)

**Audience:** a fresh Claude session (Sonnet) with the Unity MCP connected. This doc is
self-contained — execute it top to bottom without needing prior conversation context.
**Goal:** close every remaining gap between the shipped UI and the final design
(`docs/ART.md` §6 + `Design/CluckWars UI Design.html`) across all menus and the match HUD.

**Baseline:** branch `develop` at `68f694a`. The UI rebuild Stages 0–4 are DONE
(asset pipeline → overlays → top bar + touch controls → char-select fidelity →
on-character bars + control-state overlays). Legacy `Assets/UI/Sprites/` is deleted,
`UiGfx` art generators are deleted, MatchHud is only hit-flash + event banner.
What remains is listed below as Stages A–G. Do them in order, **one commit per stage**.

---

## 0. Binding rules (violating any of these fails review)

1. **UXML/USS only** for screen-space UI — no procedural UGUI, no runtime sprite baking.
   `UiGfx` holds only colour tokens/fonts/AddShadow — **never add sprite generation back.**
2. UI art comes from `Assets/_Game/Art/UI/` (design-exported). If an atom is missing,
   stop and flag it — do not generate art procedurally.
3. World-space on-character visuals are SpriteRenderer/TMP (`ChickenWorldBars`,
   `ChickenStateOverlays`, `ChickenNameplate`) — not UITK.
4. Visuals **observe** replicated state (`[Networked]` props) in `Update`/`LateUpdate`/`Render`
   and never write or RPC. Never touch the chicken's material or animator (fights hit-flash).
5. `ILogService` only — no `Debug.Log`. In files importing both `CluckWars.Logging` and
   `Fusion`: `using LogLevel = CluckWars.Logging.LogLevel;`
6. Zenject self-injection: `if (_log == null) ProjectContext.Instance.Container.Inject(this);`
   (never gate on `HasInstance`).
7. HUD elements must not eat game input: `picking-mode="Ignore"` on every non-interactive
   element (existing `MatchTopBar.uxml` shows the pattern).
8. URP materials only. `ChickenClass : byte` stays byte.

## 0.1 Tooling recipes (use these exactly)

- **Compile check** after every .cs/.uss/.uxml change:
  `assets-refresh` (options `ForceSynchronousImport`) → `console-get-logs` (filter `Error`).
  Zero errors before proceeding.
- **Diagnostics:** the console is too noisy to scrape. Use `script-execute` in full-code
  mode with a method that **returns a string**:
  ```csharp
  public class Diag { public static string Main() { /* inspect scene */ return report; } }
  ```
  The tool returns the string directly.
- **Drive a solo match** (chickens + HUD live, no input needed — bots play):
  1. Open `Assets/_Game/Scenes/Bootstrap.unity`, enter play mode.
  2. `script-execute` full-code:
  ```csharp
  using UnityEngine; using System.Reflection;
  public class StartSolo { public static string Main() {
      var mc = GameObject.FindAnyObjectByType<CluckWars.UI.MenuUiController>();
      if (mc == null) return "NO_MENU";
      var f = BindingFlags.NonPublic | BindingFlags.Instance;
      var sel = mc.GetType().GetField("_selection", f).GetValue(mc);
      var mp = sel.GetType().GetProperty("Mode");
      mp.SetValue(sel, System.Enum.Parse(mp.PropertyType, "Solo"));
      var loader = mc.GetType().GetField("_sceneLoader", f).GetValue(mc);
      loader.GetType().GetMethod("LoadNext").Invoke(loader, null);
      return "loading solo match"; } }
  ```
  Abilities are NOT required to spawn chickens (they only gate the UI's READY button).
- **Visual check:** `screenshot-game-view` at each verification point.
- **Menus without play mode:** menu screens can also be driven in play mode from Bootstrap
  by clicking through, but reflection-driving `MenuUiController` is faster (same pattern:
  find controller, call its private navigation methods).

## 0.2 Asset inventory (all in `Assets/_Game/Art/UI/`, 9-slice set by `UiSpriteImportSettings.cs`)

- `Atoms/`: `BarFill`, `BarTrough`, `ButtonGrayscale`, `CardBg`, `CardGlowFrame`,
  `CodeTile`, `GlossOverlay`, `HexGlossy`, `JoystickBase`, `JoystickKnob`, `PanelFrame`,
  `PlayerDotGlossy`, `RadialGlow`, `Ribbon`
- `Chickens/`: `Chicken_Warrior/Speedy/Fatty/Assassin`
- `Icons/`: 14 ability icons (see `CluckWars.UI.AbilityIconStyle` — the single
  ability→icon-class map; **reuse it, never duplicate the mapping**)
- `Backgrounds/`: screen backgrounds
- Grayscale atoms (`ButtonGrayscale`, `Ribbon`, bars) are tinted with
  `-unity-background-image-tint-color` (USS) or `SpriteRenderer.color` (world).
  **Tint multiplies** — pick tints brighter than the target colour.
- **Footgun:** when a UITK `Button` gets a sprite background, set
  `background-color: rgba(0,0,0,0)` or the default runtime grey shows through
  transparent sprite margins.

Player identity colours (Okabe-Ito, ART §6): P1 `#E8751A` orange, P2 `#1A7FC4` blue,
P3 `#C4286F` pink, P4 `#0D9E7A` teal. Gold `#f5c842`. Fonts: Lilita One (headings),
Nunito (body) — both in `Resources/Fonts`, already referenced by existing USS.

---

## Stage A — Match HUD: ranked leaderboard panel + embossed timer (§6.3) — **the big one**

**Gap:** the shipped top bar is a centered chip strip `[P1][P2][TIMER][P3][P4]`
(`Assets/UI/MatchTopBar.uxml` + `.uss`, driven by
`Assets/_Game/Scripts/UI/MatchHudController.cs`). The design wants:

- **Top-left ranked leaderboard panel** — one row per active player, **sorted live by
  stored food** (descending). Each row: ordinal (`1st`…`4th`), glossy player dot
  (`PlayerDotGlossy` tinted), `P#` in player colour, a **thin progress bar**
  (food ÷ win target; use `BarTrough`/`BarFill` atoms or plain USS bars), and the food
  score in gold. The **local player's row** gets a player-colour left border + tint;
  the **leader's row** a faint gold wash.
- **Win-target badge** directly under the panel: `★ FIRST TO 150` — value from
  `MatchConfigSO.FoodTargetToWin`, never hardcode.
- **Top-right embossed timer badge**: dark gradient background, gold border, inner
  highlight, letter-spacing 3px, gold text (keep existing timer format logic).

**How:** rewrite `MatchTopBar.uxml`/`MatchTopBar.uss` (rename classes to
`cw-lb-*` as needed) and rework `MatchHudController.cs`: keep its existing data
sources (it already reads per-player food + timer), add a sort each refresh, and
re-order rows by setting row content per rank slot (simplest: 4 fixed row elements,
controller writes rank/dot-tint/label/bar/score into them — avoids reparenting).
Rows for absent players hide (existing behaviour). Keep `picking-mode="Ignore"`
everywhere. During the intro countdown the top bar shows at 40% opacity (§6.5) —
check whether the countdown overlay already dims it; if not, add a
`cw-topbar--dimmed` class toggled by the existing countdown code path.

**Verify:** solo match; diagnostic string listing rows in order with scores; screenshot
early (all 0) and after ~60s (bots have different scores — confirm re-sorting and
leader wash). Confirm badge text matches `MatchConfigSO.FoodTargetToWin`.

**Commit:** `feat: UI Stage A — ranked leaderboard panel + embossed timer (§6.3)`

## Stage B — On-character bars on the exported atoms (§6.3)

**Gap:** `Assets/_Game/Scripts/Visuals/ChickenWorldBars.cs` draws HP/cargo bars with
procedural 1×1 quads. The design atoms `BarTrough.png`/`BarFill.png` exist (note:
STATE.md previously claimed BarTrough failed to export — that is stale; the file is
committed since Stage 0).

**How:** load the two sprites (e.g. via a `[SerializeField] Sprite` pair on the
component, assigned on `Chicken.prefab` — prefer serialized refs over `Resources`).
Trough uses `BarTrough`, fills use `BarFill` tinted by the existing colour ramps
(`SpriteRenderer.color` multiplies — ramps may need brightening). Keep: left-anchored
fill scaling, billboard, hide-on-stun, hide-cargo-at-zero, identity ring (ring stays
procedural — no ring atom exists). Watch sprite pivot/PPU: the current code relies on
1-world-unit sprites; either set `pixelsPerUnit` math accordingly or size via
`SpriteRenderer.drawMode = Sliced/Tiled` with explicit `size`.

Also: `ChickenNameplate.cs` still shows a cargo count as a second TextMesh line while
the cargo bar now exists → **remove the nameplate's cargo label**
(`BuildCargoLabel`/`UpdateCargoLabel`) and instead show the `9/20` count next to the
cargo bar in `ChickenWorldBars` (small TMP or TextMesh child, only while carrying).
Keep the `FULL!` red emphasis behaviour.

**Verify:** solo match screenshot — bars read as bevelled design bars, not flat quads;
cargo count appears beside the bar when a bot carries food; no duplicate count under
the nameplate. Fix the stale BarTrough note in `docs/STATE.md` in this commit.

**Commit:** `design: UI Stage B — on-character bars on exported BarTrough/BarFill atoms`

## Stage C — Character-select completion (§6.4)

**Present already:** ribbon, class cards, preview disc + radial glow, stat pips, ability
row, session buttons. **Missing (verified by grep — zero matches in
`Assets/UI/CharacterSelect.uxml`):**

1. **Lore quote** under the character name — italic Nunito, secondary colour. Wire per
   class from the existing class data SO if a flavour/description field exists; if not,
   add a `LoreQuote` string field to the class SO and use these placeholder lines
   (flag for a narrative pass in STATE.md):
   - Warrior: *"Mighty of wing, short of temper."*
   - Speedy: *"Blink and she's already back home."*
   - Fatty: *"An immovable object with a big appetite."*
   - Assassin: *"You'll hear the cluck after the strike."*
2. **Skin slots row** below the quote: 3 slots — `Default` (selected style) + 2 locked
   (🔒, dim, non-interactive). Pure static UXML/USS for now.
3. **Console hints** (bottom-left, 60% opacity: `←→ Select · A Confirm · LB/RB Tab`) —
   add the element but leave it `display: none` by default; §6.8 console adaptation is
   post-demo. One USS class flip enables it later.

Check while in there: stat pips should read bevelled per spec (filled = gradient +
highlight, empty = dark + inset). If they're flat colour swatches, restyle in USS only.

**Verify:** play-mode screenshot of char-select for each of the 4 classes (quote and
name change per class; skins row static; no layout overflow at 16:9 and at the Game
View's phone aspect).

**Commit:** `design: UI Stage C — char-select lore quote, skin slots, stat-pip bevel (§6.4)`

## Stage D — Match-end overlay polish (§6.5)

**Present already:** crown, winner banner + chicken (`.cw-chicken--*` classes) + ring,
controller-built leaderboard rows with medals (`cw-me-medal--1/2/3`) and dots, restart
countdown. **Close these:**

1. **Decorative particles** scattered on the dim background — a handful of small
   absolute-positioned dots/blurs in UXML/USS (static is fine; no animation needed).
2. **Per-row score bar**: §6.5 wants each row to carry a beveled score bar with
   gradient fill **proportional to the max score** + food icon + score number. Inspect
   `MatchOverlaysController.BuildRow` (around line 320) — if rows lack the bar, add it
   (USS classes + controller fill-width set from `score / maxScore`).
3. Winner row subtle accent background — verify, add if missing.

Explicit **non-goals** (match current ART.md — do not add): LEAVE/REMATCH buttons
(spec says restart countdown, which exists), per-row ability chips, S/D stat columns.

**Verify:** solo match run to completion (or reflect-call the overlay's show method with
fake data — `MatchOverlaysController` has the entry points; a diagnostic script can
invoke them). Screenshot of the end screen.

**Commit:** `design: UI Stage D — match-end overlay particles + proportional score bars (§6.5)`

## Stage E — Ability buttons audit (§6.6)

Stage 2b built the hex cluster on `HexGlossy` (`Assets/UI/TouchControls.uxml`/`.uss`,
`TouchControlsController.cs`). Audit against §6.6 and fix only what's missing:

- Cooldown: dark overlay clipped bottom-up by `remaining/total` **and** a centered
  seconds-remaining integer while `cd > 0`; icon dims to ~35%, base to ~45%.
- Slot badges `1`/`2`/gold `★` for slot 3; slot 3 hidden unless slot 2 equipped
  (Assassin rule).
- Neutral state (no ability equipped): `ColorSchemeSO.AbilityNormal` tint.
- Cluster positions per §6.7 table: A1 (−170,170), A2 (−210,360), A3 (−410,270)
  from bottom-right, 150px reference size.

**Verify:** solo match as a class with 2 abilities; trigger an ability via
`AbilityController` reflection (`BotTryActivate(0)` on the local chicken works) and
screenshot mid-cooldown — number + dim visible.

**Commit:** `fix: UI Stage E — ability hex cooldown number/dim + §6.6 audit fixes`

## Stage F — Hygiene sweep

1. **`AbilityIconStyle` must fail loudly.** Today a new ability SO with no icon mapping
   silently falls back to an emoji glyph. Add an EditMode test (see `tests-run` MCP
   tool; existing test asmdef if present, else create one under
   `Assets/_Game/Tests/Editor/`) that iterates `AbilityRegistrySO` entries and asserts
   every ability type has a mapping in `AbilityIconStyle`. If tests are impractical in
   this project, an `[InitializeOnLoadMethod]` editor check logging `ILogService`-level
   errors is acceptable.
2. **Delete `CargoHud`** (legacy IMGUI, auto-disabled by `MatchHud.DisableLegacyCargoHud`).
   Remove the component from `Game.unity` (use `gameobject-find` + component destroy or
   a `script-execute` editor script — never hand-edit scene YAML), delete
   `CargoHud.cs` + `.meta`, and remove `DisableLegacyCargoHud` from `MatchHud.cs`.
3. `docs/STATE.md`: tick off completed items, remove the stale BarTrough line (done in
   Stage B), keep the deferred §6.10 VFX-pass note.

**Verify:** full compile, solo match smoke run, zero errors/exceptions.

**Commit:** `chore: UI Stage F — AbilityIconStyle guard, delete CargoHud, docs sweep`

## Stage G — Full verification pass + docs closeout

1. Drive the entire flow in play mode: MainMenu → CharacterSelect (all 4 classes) →
   Lobby → countdown → match (60s+) → match end. Screenshot each screen.
2. Confirm zero Error/Exception console entries across the whole run
   (`console-get-logs`, filter `Error` then `Exception`).
3. Lobby spot-checks while there (§6.5): join-code badge green styling, empty slots
   dashed "Waiting for Player N…", ready-state glow. Fix in USS only if off.
4. Update `docs/STATE.md` (rebuild fully closed except listed deferrals) and tick
   `docs/ROADMAP.md` if it tracks the UI rebuild.
5. Final commit: `docs: UI final-design completion — STATE/ROADMAP closeout`

---

## Out of scope (do NOT attempt)

- **§6.10 body-mutating art pass** (stun desaturation/tilt, slow speed-trail, knockback
  motion lines) — needs a real VFX pass; procedural stand-ins would fight the hit-flash
  and animator.
- **§6.8 console adaptation** (post-demo) — Stage C only adds the hidden hints element.
- **Mobile/Pixel-9 device pass** — requires the physical device; leave as a STATE.md
  open item for Maestro (emulators do not work on this machine).
- Photon multi-client testing (`tools/run-clients.ps1` exists for that; not needed here).
- Any gameplay/balance/networking change. If a stage seems to require one
  (e.g. new `[Networked]` state), stop and flag it in STATE.md instead.

## Definition of done

Every stage committed (7 commits), full-flow screenshots match `docs/ART.md` §6.3–§6.7
for: main menu, char-select, lobby, countdown, match HUD (leaderboard panel + timer +
on-character bars + touch cluster), match end. Zero console errors. STATE.md current.
