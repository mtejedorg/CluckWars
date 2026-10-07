# Menu UI overhaul — spec (2026-10-06)

Source audit (6 specialist lenses, renders at 1920x1080 / 2424x1080 Pixel 9 / 2048x1536):
https://claude.ai/artifact/U139PZEDq42KTY6bEYkgCa. Verdict: the screens show the data model instead of
the chickens. This spec turns Maestro's answers into buildable work. Scope: the four menu screens
(Main Menu, Choose Your Chicken, Build Your Loadout, Lobby), plus the post-match buttons and settings.

## Decisions (Maestro, 2026-10-06)

1. **Look: Direction A "Barnyard Toybox" with B's painted-wood trim.** Bright golden-hour farm behind
   every screen; cream cards with dark ink text; Lilita One with a thick dark outline and drop shadow;
   class colours as full tile fills; the chickens are the largest thing on screen. Painted wood
   (planks, nails, gold-leaf edge) is used for frames, ribbons and the main call-to-action only.
2. **Phase 0 approved.**
3. **Perk wording: Claude's lines, with numbers.** Numbers must never drift from the data, so perk
   lines are **templates filled from the passive's own fields** (see Wording dictionary). Changing a
   value in a `.asset` updates the text automatically; a test guards it.
4. **Screen and button wording: the audit's.** PICK YOUR BIRD / GEAR UP / THE COOP, Perk / Starter /
   Moves vocabulary. READY stays READY (universal ready-check; the dictionary makes it a one-line change).
5. **Flow: explicit "Play Again" and "Back to Lobby" buttons. Never automatic.** Solo still goes
   through the lobby; nothing skips or auto-starts.
6. **Icons by image generation** (local ComfyUI via the `assetforge` skill). Licensed sets such as
   game-icons.net (CC BY 3.0) may be used only as *reference* for silhouettes, never shipped.
7. **Live 3D chicken in menus: yes, except phone/tablet.** New setting **Performance Mode**: ON shows
   static renders and skips the 3D stage. Default ON on phones and tablets
   (`Application.isMobilePlatform`), OFF on desktop. Toggleable in Settings.
8. **All UI text goes through a wording dictionary** keyed by id, ready for future copy changes and
   languages. No player-facing literal strings in UI code or UXML after Phase 0.

Unchanged by this work: game rules, balance values, ability names (`DisplayName`), networking. Copy
must stay mechanically accurate. Do not redesign gameplay (raise questions instead).

## Constraints (project rules, enforced)

- UI is **UXML/USS only** (UI Toolkit); no procedural UGUI. `ILogService` only; no `Debug.Log`.
- The Unity Editor is **one shared resource**: only one agent drives it (Play mode, scene edits,
  tests) at a time. Art/audio agents produce files only.
- Never `model: "fable"`. Never cravify/Antigravity (exhausted; Maestro must ask by name).
- After any `tests-run`, re-open `Assets/_Game/Scenes/Bootstrap.unity` and confirm with
  `scene-list-opened` before Play checks (tests can leave Untitled or AbilityLab open, late).
- Verify UI with real renders, not code reading: `tools/ui_capture/capture_menu.py` renders every
  menu screen off-screen at 1920x1080, 2424x1080 and 2048x1536 through the MCP bridge.
- Respect the existing reduced-motion preference for every new animation.
- If you change a gameplay `.asset` value or player-facing description, update
  `docs/site/index.html` and the GDD in the same pass (standing rule, CLAUDE.md).

## Visual system (Direction A + B wood)

| Token | Value | Use |
|---|---|---|
| Ink | `#2a1a0c` | Text on cream; all outlines (4–6 px at 1080p) |
| Cream card | `#fff8e8` (raised), `#f3e6c8` (inset well) | Cards, sheets, perk badges |
| Gold | `#f5c842` / edge `#d9a521` | Selection, "you", gold-leaf trim |
| CTA green | `#7be35c` → `#36a82c` | The ONE forward action per screen |
| Wood | `#8a5a2e` / `#7a4a22` planks, gold edge | Frames, ribbons, CTA plank, back button |
| Class fills | from `ChickenClassRegistrySO` tints, lightened gradient | Class tiles, hero platform tint |

Rules: one green CTA per screen; gold means selection/you only; wood means navigation/frames;
player colours (Okabe-Ito) only ever mark players, never abilities. Display type is Lilita One with
`-unity-text-outline` and `text-shadow` (remove the old ban in the USS header after verifying both
render on Unity 6000.3). Body text is Nunito **static Bold (700) / ExtraBold (800)** — the shipped
`Nunito-Bold.ttf` is the variable font defaulting to weight 200 and must be replaced. Minimum caption
30 px at 1080p reference; tappable ≥ 116 px (48 dp). Contrast ≥ 4.5:1 body, 3:1 large.

## Wording dictionary

One table, keyed by id, one column per language (`en` now). Recommended shape: a CSV
(`key,en`) in `Assets/_Game/Resources/Text/` loaded by a tiny `UiText` service
(`UiText.Get(key)`, `UiText.Format(key, args)` with **named** placeholders like `{pct}`), injected or
static. A test fails on any key referenced in code/UXML that the table lacks, and on any template
whose placeholders the caller doesn't supply. UXML labels take their text from keys (e.g. a
`text-key` binding resolved by the controller), not from literals.

**Templated perk lines** — each `PassiveAbilitySO` subclass exposes its numbers as named args
(computed from its fields, rounded to whole percent). A test asserts that every perk's formatted line
matches its current field values, so a balance change can never leave stale text. If a perk's
*mechanic* changes (not just its numbers), its template must be rewritten — note this in STATE.md.

| Key | en | Args from |
|---|---|---|
| `perk.relentless.line` | Moves recharge {pct}% faster. | (1 − CooldownMultiplier)·100 |
| `perk.relentless.detail` | Applies to every move except Peck. | |
| `perk.bully.line` | Steal {steal}% more, carry {cap} more. | (StealMultiplier − 1)·100, BonusCapacity |
| `perk.thief.line` | Every steal grabs {steal}% more. | (StealMultiplier − 1)·100 |
| `perk.slippery.line` | Slows and stuns end {pct}% sooner. | (1 − DurationMultiplier)·100 |
| `perk.slippery.detail` | Covers slows, roots and stuns. | |
| `perk.bulwark.line` | Barely budges when shoved. | |
| `perk.bulwark.detail` | Knockback cut by {kb}%. Slows, roots and stuns end {cc}% sooner. | (1 − KnockbackMultiplier)·100, (1 − DurationMultiplier)·100 |
| `perk.hoarder.line` | Carry {cap} food in one trip. | MinimumCapacity |
| `perk.hoarder.detail` | Carries at least {cap} food. | MinimumCapacity |
| `perk.featherfoot.line` | Raid piles at full speed. | |
| `perk.featherfoot.detail` | Food piles never slow you down. | |
| `perk.spoiler.line` | +{food} food if time runs out. | BonusFood |
| `perk.spoiler.detail` | If nobody reaches {goal} before time's up, you bank {food} bonus food first. | BonusFood, MatchConfig goal |

Note: the full `Description` fields on the passive assets contained inaccurate or vague claims
(Relentless "Combat abilities", Bulwark "shrugs off", Slippery "far"; Bully, Thief, Hoarder, Spoiler,
Featherfoot fixed in Phase 4). They now state the real numbers from the asset's own fields, so every
surface (HUD, Ability Lab, compendium site) agrees; `PerkTextTests` pins those numbers.

**Screens, buttons, labels**

| Key | en |
|---|---|
| `screen.class.title` | PICK YOUR BIRD |
| `screen.loadout.title` | GEAR UP |
| `screen.lobby.title` | THE COOP |
| `lobby.rules` | HOUSE RULES |
| `nav.home` / `nav.back` | HOME / BACK |
| `btn.next` | GEAR UP ▶ |
| `btn.ready` | READY ▶ |
| `btn.pickMore.one` / `btn.pickMore.many` | PICK 1 MORE / PICK {n} MORE |
| `btn.start` | START MATCH ▶ |
| `btn.playSolo` / `btn.host` / `btn.join` | PLAY SOLO / HOST GAME / JOIN GAME |
| `main.soloHint` | Solo: you vs 3 CPU chickens. |
| `lobby.hint.solo` | Hit START MATCH — 3 CPU rivals are waiting. |
| `btn.playAgain` | PLAY AGAIN |
| `btn.playAgain.sub` | {cls} · {perk} |
| `btn.backToLobby` | BACK TO LOBBY |
| `label.perk` / `label.starters` / `label.starter` | PERK / STARTERS / STARTER |
| `loadout.row.shared` / `loadout.row.class` | ANY BIRD / {cls} ONLY |
| `loadout.detail.empty` | Tap a move for the details. |
| `settings.title` | SETTINGS |
| `settings.rangeGuides` / `.desc` | Range Guides / Show how far your moves reach. |
| `settings.devMode` / `.desc` | Dev Mode / Unlock the Ability Lab. |
| `settings.performance` / `.desc` | Performance Mode / Static chickens in menus. Saves battery. |
| `role.warrior` … `role.assassin` | Brawler / Hit & Run / Hauler / Saboteur |
| `callout.warrior.strong` / `.weak` | Good at everything. / Great at nothing. |
| `callout.speedy.strong` / `.weak` | Fastest bird in the yard. / Tiny beak, tiny haul. |
| `callout.fatty.strong` / `.weak` | Biggest haul, quickest beak. / Slowest waddle in the coop. |
| `callout.assassin.strong` / `.weak` | Blink and it's gone. / Light load, slow beak. |

Existing strings not listed keep their current English text but still move into the table. Ability
display names stay as authored, except Mark/Kill → "Death Mark" (Phase 4 decision 7).

## Phases (commit each on `develop`)

### Phase 0 — Stop looking broken (no new art)
- Replace `Resources/Fonts/Nunito-Bold.ttf` with a **static** instance at weight 700 (instance the
  existing variable font with fontTools' `varLib.instancer`, or the OFL static file); keep the GUID.
  Add an 800 instance if ExtraBold is used. Test: font's OS/2 usWeightClass ≥ 700 and no `fvar`.
- Display type outlines + shadows; wire `CardBg`, `CardGlowFrame`, `PanelFrame`; three value steps.
- Barn backdrop behind every screen (lifted/blurred; class-tinted on chicken select).
- **Settings sheet** (gear on the main menu): Range Guides, Dev Mode, Performance Mode (new,
  `PlayerPreferences`, default ON when `Application.isMobilePlatform`), plus the existing reduced
  motion if it has UI. Remove both toggles from the loadout → fixes the phone pick list.
- Bugs: empty weak row (Warrior), lobby clipping (READY chip, P-tag collisions at 4:3), blank icons
  (close the fallback gap), 9 px PRE-EQUIPPED (lock icon + STARTER ≥ 26 px), safe-area padding.
- Contrast fixes per the audit table; one button-colour rule; themed scrollbars, no nested scroll.
- Wording dictionary + all strings moved; templated perk lines + tests; passive `Description` fixes.
- **Accept:** captures at all 3 sizes show no clipping/overflow, phone loadout shows ≥ 1 full row of
  cards, EditMode suite green, no player-facing literal left in MenuUiController/UXML.

### Phase 1 — Structure
- Build cards/slots **once**; refresh by toggling classes/labels (no destroy-and-rebuild per tap).
- Page transitions (out 120 ms ease-in, in 200 ms ease-out with a directional slide; reverse on back).
- Choose Your Chicken: four large portrait tiles (class fill, big render), one hero area with name,
  quote, strong/weak, two **perk badges** (name + templated line; detail on tap), starter icon chips.
- Build Your Loadout: four slot cards on top; pool as an art-forward card deck below with category
  frames; tap card = detail + equip into the armed slot; armed slot shows a NEXT caret (not colour-only);
  locked starters show a padlock.
- Lobby: player lineup (big renders on pedestals in player colours, nameplates, ready check) and a
  short "READY!" banner when all are ready; compact rule chips row.
- **Play Again** on the main menu (persist last class/perk/loadout in `PlayerPreferences`; one tap
  goes to the lobby with that loadout — never auto-starts) and **Back to Lobby / Play Again** on the
  post-match screen. Solo still passes through the lobby.
- **Accept:** journey Main → Pick → Gear Up → Coop → (match) → Back to Lobby works at all 3 sizes;
  Play Again restores the last loadout; tests green.

### Phase 2 — Art (generated; integrated after Phase 1)
- 38 ability icons in one locked style (bold silhouette, cream `#fef5e0` fill, thick ink outline, one
  white gloss tick), 256x256 transparent, coloured by the hex accent at runtime. Retire the emoji path.
- Four backdrops from one golden-hour farm (main barn regrade, show ring, barn pegboard, arena gate),
  2880x1440 with a centred 16:9 safe area.
- Wood 9-slice frames (panel trim, CTA plank, ribbon), cream card 9-slice, hay-bale pedestal + tint
  ring, rosette badge (starter/ready), sparkle/feather/dust particles.
- Hero renders of the 4 chickens at 1024 px (idle + cheer) from the real models (Unity render).
- **Accept:** no emoji glyph anywhere in menus/lobby; contact sheets reviewed for consistency.

### Phase 3 — Alive
- Live 3D chicken stage on a RenderTexture (idle loop, slow turntable, hop + cluck on select) on
  chicken select, loadout and lobby, **only when Performance Mode is OFF**; static hero renders otherwise.
- Juice: tile squash-pop, fly-to-slot on equip, READY stamp, START MATCH 3-2-1, staggered entries.
- Menu audio through `IAudioService` (the project has **no audio assets** yet): tap, back, per-class
  cluck, equip thunk, clear pop, READY stamp, match sting, menu loop. Procedurally synthesised or CC0.
- **Accept:** desktop shows the live chicken, phone/tablet default to static; frame time on desktop
  unaffected (> 60 fps); reduced motion disables non-essential animation.

## Re-audit
After Phase 3, re-run the same six lenses on fresh captures and publish the comparison.

## Phase 4 — re-audit fixes (decisions, Maestro 2026-10-07)

Source: re-audit page https://claude.ai/artifact/U139PZEDq42KTY6bEYkgCa ("New findings", 22 items).

1. **Fix all new findings**, including a rebuild of the post-match screen on the Coop's visual
   system (barn backdrop, podium of pedestals for the top 3, live 3D when Performance Mode is OFF,
   cream rows, wood ribbon banner, outlined button labels, names instead of P#, safe area).
2. **Solo after READY keeps the explicit START MATCH.** Decision 5 ("never automatic") stands.
3. **Ambiguous icons** were redrawn by artist-2d with Unity closed (13 icons, committed first).
   Icons are glyph-only; the hex colour is tinted in code from the ability's category.
4. **Hero on PICK YOUR BIRD sways gently** (like the Coop seats) instead of a full turntable.
5. **Reduced Motion keeps the 3D idle loop.** Re-audit finding 22 is closed as won't fix.
6. **Delete the unused old art**: the 14 top-level `Art/UI/Icons/Icon_*.png` and the 4 old
   `Chicken_*.png` portraits, after a GUID grep proves nothing references them.
7. **Mark/Kill is renamed "Death Mark"** everywhere player-facing (ability `DisplayName`, wording,
   GDD, compendium). This supersedes the "rename undecided" note above. The C# type and asset file
   keep their names (GUID stability).

Category colours replace the stock Material colours: abilities are coloured by category only, and no
category colour may sit near a player hue (orange `#e8751a`, blue `#1a7fc4`, pink `#c4286f`,
teal `#0d9e7a`). Esc / Android back goes through `IInputProvider` (Cancel), never `Keyboard.current`.

**Match start (finding 4).** START MATCH fades in a GET READY card and loads the Game scene with
`LoadSceneAsync`. Every round then passes through a new networked `MatchState.Starting` (appended,
value 3): the state authority arms the unchanged 3 s intro + match TickTimers only once every real
player has a chicken and frames have stayed smooth for 0.4 s (8 s cap), so "3" is never eaten by a
load hitch. Peers show GET READY during Starting. The MatchStart sting and match music play locally on
every peer at GO; the final-minute event banner is displayed 0.6 s after GO (its gameplay timing is
unchanged).

**Names (finding 16).** Overlays never identify a chicken by its corner number: the local player is
"You" / "YOU WIN!", solo bots use the Coop's bot names by class, remote humans are "P{corner+1}".
