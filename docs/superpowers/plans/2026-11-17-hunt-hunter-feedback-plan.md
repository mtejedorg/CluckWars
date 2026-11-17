# Hunt: hunter-side feedback — build plan (2026-11-17)

Design (binding): `docs/superpowers/specs/2026-11-17-hunt-hunter-feedback-design.md` (1889b76e), options H1a-H8a.
Scope: the hunter's own screen only. No victim/bystander change, no timing/range/cooldown/rule change, no `[Networked]`
field, no RPC; presentation polls replicated state (ARCH-EVT-01, ARCH-EVT-05, ARCH-EVT-14). All new code is in
`CluckWars.Presentation` (`Visuals/`, `UI/`, `Input/`) plus two enum values in Core `Services/` (ARCH-LAY-01: no upward reference).

## 0. What the code says today (read 2026-11-17, branch `balance`)

- The mark ends only on expiry, a landed Kill, the target lost/removed, the hunter removed or a round reset
  (`AssassinExecute.cs:273-309`, `:472-476`, `:478-483`); walls and eggs never end it. A Kill on a shell is refunded and
  the same mark is restored with the same timers (`:375-385`), so it comes back with the same `MarkKey`.
- `CanKillNow` = armed and target stunned and not shelled, hunter can cast, planar distance <= `KillRange`, no wall
  (`AssassinExecute.cs:317-329`). `MarkKillAbilitySO.IsUsable` returns it while marked (`MarkKillAbilitySO.cs:70-75`);
  `AbilityController.cs:1095-1099` turns that into `NoTarget`.
- **Hex alpha: not confirmed.** `NoTarget` falls in the default branch, tint alpha `HexAlphaReady` 1.00
  (`TouchControlsController.Hud.cs:350-355`, `FeedbackTuning.cs:596`), and `HudFeedbackStyle.HexClass(NoTarget)` is null
  (`HudFeedbackStyle.cs:145-155`). `HexAlphaNoTarget` 0.70 (`FeedbackTuning.cs:613`) is used only on the joystick
  (`TouchControlsController.Hud.cs:819`). The design's "hex at 0.70" is not in the code; K2 starts by capturing it (Q2).
- **H7a is already shipped.** The hunter marker's `MarkSkull` element draws `Glyph_Hunting.png` (the paw) since 55615fc2
  (`MarkHud.uss:32-39`); only the victim badge keeps `Glyph_Skull.png` (`MarkHud.uss:109-114`). Stale: the design §2 table,
  `docs/site/mechanics.html:193` ("a skull"). No code item for H7a; K9 fixes the docs (Q1).
- Model: `MarkInputs` (`MarkPresentationModel.cs:51-93`) lacks the target's stun and `KillRange`; `MarkOutputs`
  (`:96-161`); `MarkEvents` is a byte with 1..16 used (`:33-47`); the end rule freezes the last live frame and fades
  everything over `MarkEndFadeSeconds` 0.3 (`:370-390`, `:671-698`, `FeedbackTuning.cs:782`); `MarkerModeFor` already
  orders Kill > Shelled > Walled (`:482-483`). `MarkFeedback.Gather` returns before any Assassin fact once the mark is
  gone (`MarkFeedback.cs:171-177`), so the end cause must come from the frozen last live frame.
- Marker colour is chosen inline in `MarkHudController.cs:174-177`; arm digit vs paw `:185-197`; lock/egg `:198-199`;
  metres only off screen `:206-216`; Painter2D ring/pointer `:221-267` read `FeedbackTuning.cs:839-845`; placement reads
  `MarkMarkerHalfPx/BoxWidthPx/BoxHeightPx/EdgeClearancePx/TopBandOverhangPx` (`FeedbackTuning.cs:848-858`,
  `MarkerPlacement.cs:49,59-60,118,137,274-277`).
- Hex: KILL/lit classes `TouchControlsController.cs:212-213`, `UpdateMarkHexes` `.Hud.cs:157-168`, KILL scale `:72-73`,
  tint override `:357-360`, label owners `RefreshCasterStunDigit`/`RefreshEggDigit` `:412-450,478-543`, duration ring
  `HexDurationRing` built in code (`TouchControlsController.cs:458,464-465`; fixed cream fill `HexDurationRing.cs:25`),
  `.cw-hex--kill` hides icon/label/cdnum/pip (`TouchControls.uss:697-705`). Tenths helpers:
  `CasterStunCountdownRules.Tenths/Format` (`CasterStunCountdownRules.cs:45,48`).
- Ground: rival ring sortingOrder 1, floor-arrow ring 2, arrows 3, heights `GroundY+0.005/0.012`
  (`FloorRivalArrows.cs:45-50`); telegraphs -3/-2, range overlay -1. The marked target's floor arrow and chevron stand
  down while the marker shows (`RivalIndicator.cs:371-373,414-417`).
- Haptics: `IHapticsService.Buzz(HapticKind)` (`IHapticsService.cs:4-22`), policy + 200 ms limiter + "Buzz When Hit"
  preference (`HapticsService.cs:32-39`, `ProjectInstaller.cs:130-136`), `HapticRules.PatternFor`/`Allowed` (stronger =
  larger enum value). Only caller today: `TouchControlsController.cs:754`. PC binds `NullHapticsService`.
- Strings: `mark.distance` `{m} m` (`UiKeys.cs:139`, `UiText.csv:105`), `hud.pop.safe` SAFE (`UiKeys.cs:257`,
  `UiText.csv:211`). Self-pops: `FloatingCombatText.SpawnSelfPop` (`FloatingCombatText.cs:202-219`).
- Captures: `AutoTestDriver.Mark.cs` (shots `:138-189`, `mark-end` at 0.6 s after any end `:85-104`).

## 1. Recommendations on the five called-out points

**(a) Hex brightness without touching `IsUsable`.** Leave `MarkKillAbilitySO.IsUsable`, `EvaluateRefusal` and the bots
alone (design §6). The hex's look during a hunt is a presentation override keyed on `MarkFeedback.Current`
(`Role == Assassin && AssassinMarkActive`) and the slot's job (`Is(AbilityJob.DeathMark)`), exactly like KILL today
(`.Hud.cs:163,357-358`): a pure `HuntHexRules.TintAlpha(refusal, huntActive)` returns `HexAlphaReady` for
`NoTarget|None` while hunting and the existing table otherwise (Cooldown/Stunned keep theirs). Today that is already
1.00, so the rule is a pin, not a change, unless the K2 step-0 capture shows a dim hex.

**(b) End cause, locally.** Pure `HuntRules.EndCauseFor(in HuntLastFrame last, bool matchRunningNow, bool hunterRemovedNow)`
over the frozen last live frame (role, armed, `CanKillNow`, shelled, walled, blocker, `Remaining`):
1. not Assassin, round stopped, or the hunter removed -> `None` (no word);
2. `CanKillNow` on the last live frame -> `Pounced` (no word; covers the landed Kill and the shell refund);
3. `Remaining > MarkEndExpiryToleranceSeconds` (0.15 s, two frames + a tick at 30 fps) -> `Missed` (ended early:
   target lost, executed by someone else, left);
4. shelled or walled -> `Safe`; 5. else `Missed`.
Fallback is `Missed` for anything not provably SAFE (design §6). Known ambiguity: expiry while POUNCE was legal reads
as `Pounced` (Q5).

**(c) Ground brackets: a scene-level `HuntBrackets` component, not on the chicken.** Created and owned by `MarkFeedback`
(child of its DontDestroyOnLoad object), never parented under a chicken so `ChickenVisuals`' renderer scan and ghost
swap never find it (same reason as `FloorRivalArrows.cs:29-34`), no `Chicken.prefab` edit (CODE-ASSET-06/11). One
`MeshRenderer`, one mesh of 4 arcs (~200 verts, built once), shared `TelegraphShapes.SharedUnlitSpriteMaterial` with
vertex colour (CODE-ASSET-12), one draw call, no particles; per frame only the transform moves, vertex colours rewrite
on a state change. sortingOrder 4 and height `GroundY + 0.019`: above the rival ring (1), the floor-arrow ring/arrows
(2/3) and every telegraph/overlay (<0), below body FX (5, `ChickenVFX.cs:1338`). Radius = the target's rival ring radius
(`RivalIndicator`'s `_ringRadius`, expose a read-only `RingRadius`) x `HuntBracketRadiusFactor`.

**(d) Art.** Reuse: paw `States/Glyph_Hunting.png` (already the marker glyph), star = `States/Glyph_Stunned.png` (three
stars, ink-outlined cream, tintable like the paw), lock `Icons/Glyph_Lock.png`, egg `States/Glyph_ShieldEgg.png`. The
"too far" glyph is the red metres chip (text). Brackets are a procedural mesh. **No new sprite**, so no `artist-2d` item.

**(e) Reduced Motion and haptics.** Read `PlayerPreferences.ReducedMotionEnabled` live (as `MarkHudController.cs:169`):
under RM the bracket lock-snap and POUNCE breathe, the marker KILL pulse (existing) and the hex KILL pulse (existing
`MarkHexRules`) are static; colours, counts and glyphs are unchanged. Haptics ignore RM and go through
`IHapticsService` only, so the "Buzz When Hit" preference, the device check and the 200 ms limiter apply unchanged.

## 2. Items (order = dependency order; each item one commit)

Every item: EditMode suite green through `TestRunnerApi` from `script-execute`, checking `scriptCompilationFailed` first
(CODE-TEST-01, CODE-TEST-15, CODE-BUILD-02); then, from K2 on, a dev-tools PC run
`tools/test-session/scenario-pc.ps1 -ExtraArgs '-cw_markshots -cw_perk Assassin_Spoiler'` with the Editor out of Play
mode (CODE-BUILD-06) and the named shots read by the reviewer (CODE-UI-02, CODE-TEST-18, CODE-TEST-19). Tests derive
numbers from `Assets/_Game/Data/Abilities/MarkKill.asset` via `AssetDatabase.LoadAssetAtPath` (pattern
`StunBeatTests.cs:177`) or from `FeedbackTuning`, never hand-copied. New `.cs` files are committed with their `.meta`
(CODE-ASSET-05). Each item ends with the specialist's "Docs upkeep" line (CODE-PROC-08).

### K1 Hunter state rule and end cause (senior-dev, M)
- New `Assets/_Game/Scripts/Visuals/HuntRules.cs` (Presentation; `MarkPresentationModel.cs` is already 723 lines):
  - `enum HuntBlocker : byte { None, Shelled, Walled, TooFar, NotStunned }` (priority order = declaration order).
  - `enum MarkEndCause : byte { None, Pounced, Missed, Safe }`.
  - `HuntBlocker BlockerFor(bool armed, bool canKill, bool shelled, bool walled, float distance, float killRange, bool targetStunned)`:
    `None` while arming or when `canKill`; else first of shelled, walled, `distance > killRange`, `!targetStunned`;
    `None` if none applies (hunter itself stunned, Q7).
  - `bool TooFar(float distance, float killRange)` => `distance > killRange` (same comparison as `AssassinExecute.cs:327`).
  - `int LiveTenths(float remaining)` => `CasterStunCountdownRules.Tenths(remaining)` (never 0 while alive).
  - `string CountText(bool armed, int armSeconds, float remaining)`: arming -> `armSeconds` ("3","2","1"); live with
    `remaining >= 1` -> whole seconds (ceil, covers a Remote Config `LiveSeconds > 1`); live < 1 -> `Format(tenths)` ("0.8").
  - `MarkEndCause EndCauseFor(...)` as in §1(b). `HuntLastFrame` struct holds the frozen facts.
  - `Color MarkerColour(MarkMarkerMode mode, int targetCorner)`: moved verbatim from `MarkHudController.cs:174-177`
    (one home, CODE-UI-08); `Color BracketColour(bool armed, bool canKill, int corner)`: arming -> seat colour, live and
    legal -> `MarkKillColor`, live not legal -> `MarkHuntBlockedColor`.
- `MarkInputs` += `TargetStunned`, `KillRange`, `LocalRemoved`; gathered in `MarkFeedback.GatherAssassin`
  (`MarkFeedback.cs:203-209`: `target.CurrentControlState == ControlState.Stunned`, `exec.KillRange`) and
  `LocalRemoved` in `Gather` before the early return (`:171-177`, from the snapshot's local entry).
- `MarkOutputs` += `HuntBlocker Blocker`, `float Remaining`, `MarkEndCause EndCause`, `bool EndHolding`;
  `MarkEvents.Ended = 32` (fires once, the first end frame, Assassin role, cause Missed/Safe only).
- End hold in `MarkPresentationModel.Step` (`:370-390`) and `FillFrozen` (`:671-698`): for role Assassin with cause
  Missed/Safe only, `MarkerAlpha` stays at the frozen value for `MarkEndHoldSeconds` 0.6, then fades over the existing
  0.3 s; every other output keeps today's 0.3 s fade from end frame 0; `AssassinMarkActive` still false from frame 0;
  the model is clean after 0.9 s. Victim and ThirdParty paths untouched. Same key back during the hold resumes (existing `:364`).
- Constants (`FeedbackTuning.cs` mark block ~782): `MarkEndHoldSeconds = 0.6f`, `MarkEndExpiryToleranceSeconds = 0.15f`,
  `MarkHuntBlockedColor => MarkSkullLiveColor` (same red, one home, `:834`).
- `MarkHudController.cs:174-177` calls `HuntRules.MarkerColour` (no visual change in K1).
- Tests (new `Editor/Tests/HuntRulesTests.cs`): `Blocker_FollowsEggWallFarStunPriority` (all 16 flag combos, live, not
  legal: the first true in declaration order); `Blocker_NoneWhileArmingOrLegal`; `Blocker_OrderAgreesWithMarkerModeFor`
  (shelled/walled map to the same precedence as `MarkerModeFor`); `TooFar_UsesShippedKillRange` (asset `KillRange` +/-
  0.01 m); `Count_ArmingShowsShippedArmSecondsDownToOne`; `Count_LiveSecondShowsTenthsNeverZero` (from asset
  `WindowSeconds - ArmSeconds`); `EndCause_Table` (expired+walled/shelled -> Safe; expired+star/far/none -> Missed;
  early end not legal -> Missed; last legal -> Pounced; round stopped / hunter removed / not Assassin -> None).
  `MarkPresentationModelTests` +: `AssassinMissed_HoldsMarkerThenFades`, `AssassinPounced_FadesWithoutHold`,
  `VictimEnd_StillCleanAfterEndFade` (unchanged timing), `Ended_FiresOnceAndNotOnSameKeyReturn`,
  `MarkerColour_MatchesPreviousInlineTable`.
- Capture: none required (no visual change); `mark-end` log line still prints.
- Rules: ARCH-EVT-01/05/14, ARCH-LAY-15, CODE-UI-08, ARCH-DATA-11, CODE-TEST-18. Risk: the hold must not delay
  `AssassinMarkActive` (range guides read it) or the victim; covered by tests.

### K2 The Hunt hex during a hunt (senior-dev, M; USS/UXML by the same agent, reviewed by ui-designer)
- Step 0: run the markshots session on K1's build and read the hex in `mark-arming`/`mark-live`. Record its alpha in
  the item report. If it is already full (expected from the code), the alpha rule below is a pin only (Q2).
- New pure `Assets/_Game/Scripts/Input/HuntHexRules.cs`: `bool Applies(in MarkOutputs o, bool slotIsDeathMark)` (Assassin,
  `AssassinMarkActive`, Death Mark slot); `float TintAlpha(AbilityRefusal r, bool hunting)`;
  `string GlyphClass(HuntBlocker b)` -> `cw-hex-huntglyph--star|--lock|--egg|--far` or null.
- `TouchControlsController.Hud.cs`: `UpdateMarkHexes` (`:157-168`) also records `_hunt[slot]`; `RefreshSlot` (`:337-360`)
  uses `TintAlpha`; new `RefreshHuntDigit` in `RefreshCenterMark` (`:412-423`) with the same ownership/hand-back contract
  as `RefreshEggDigit` (`:478-507`), precedence StunnedCross > hunt > caster stun > egg > cooldown, text
  `HuntRules.CountText`, written only when it changes; ring: during a hunt the slot's `HexDurationRing` shows
  `MarkerFill01` in `HuntRules.MarkerColour` (add `HexDurationRing.SetColour(Color)`, default cream, `HexDurationRing.cs:25`);
  hunt takes the ring over the 0.1 s cast duration (`:296-306`). The far chip on the hex uses `UiKeys.MarkDistance`.
- `TouchControls.uxml` per slot 1-4 (beside `KillText{n}`, `:108`): `HuntDot{n}` (`cw-hex-huntdot`), `HuntGlyph{n}`
  (`cw-hex-huntglyph`), `HuntFar{n}` Label (`cw-hex-huntfar`, text empty, CODE-TEXT-04). Lookups through `UiElements`
  with a once-only Error when missing (ARCH-ERR-08, pattern `TouchControlsController.cs:471-472`).
- `TouchControls.uss`: `.cw-hex--hunt` (on the hex while hunting) shows dot (18 px, ink 2 px border, top-centre, colour
  inline) and glyph (28 px badge at the rim, sprites as §1(d)); `.cw-hex--kill.cw-hex--hunt .cw-hex-cdnum
  { visibility: visible; }` placed under `KillText` so POUNCE carries the tenths; every emitted class gets a rule (CODE-UI-06).
- Tests (`HuntHexRulesTests`): `OnlyTheDeathMarkSlotTakesTheHunt`; `TintAlpha_FullWhileHunting_ExistingTableOtherwise`
  (== `HexAlphaReady`, > `HexAlphaNoTarget`; Cooldown/Stunned unchanged); `GlyphClass_TotalOverHuntBlocker`
  (ARCH-DATA-10). `MarkKillAbilitySO.cs` must not appear in the K2 diff (review check, design §6).
- Capture: `mark-arming` (dot, ring, "2"/"1", full alpha), `mark-live` (tenths + glyph), `mark-kill` (POUNCE + "0.x"),
  `mark-walled` (lock on hex), `mark-hunt-offscreen` (far chip on hex if beyond range). Also check the desktop strip
  (`-cw_hud desktop` if available; else the reviewer states it was not checked).
- Rules: CODE-UI-01/04/06/08, CODE-TEXT-02/03/04, ARCH-ERR-08, ARCH-DATA-10, CODE-STYLE-09. Risk: three label owners
  now; a wrong hand-back leaves a stale digit (test the hand-back like the egg digit does).

### K3 The marker: 1.5x, count, blocker glyph, always-on chip (ui-designer, M)
- `MarkHud.uss`: every box x1.5 (box 96->144, disc 68->102 at 21, paw 44->66 at 39, arm label 40->60 px font, lock/shell
  badge 34->51 at +39). The chip stays 34 px text (a label, not scaled). `FeedbackTuning.cs:839-858` px constants x1.5
  in the same commit (ring radius, widths, pointer, half/box/clearance/overhang); USS comment `MarkHud.uss:12` updated.
- Layout during the hunt (Q3 default): the disc centre always holds the count (`MarkArm`, `HuntRules.CountText`), the paw
  moves to a new top-left badge `MarkPaw` (mirror of the lock badge) for the whole hunt; `MarkSkull` is renamed only if
  the reviewer wants it (CODE-STYLE-10 keeps code names; leave it).
- `MarkHud.uxml`: add `MarkStar` (`cw-mark-lock cw-mark-star`, glyph `Glyph_Stunned.png` tinted cream) and `MarkPaw`.
  `MarkHudController.UpdateMarker` (`:185-216`): badge = `o.Blocker` (Shelled->shell, Walled->lock, NotStunned->star,
  TooFar->chip red, None->none) once armed; during the K1 hold the frozen blocker is the reason glyph. Chip always shown
  while a mark is shown: above the disc on screen, current side rule off screen (`MarkerPlacement.DistanceGoesAbove`),
  colour `MarkHuntBlockedColor` when `HuntRules.TooFar`, else seat colour; text via `UiText.Format(UiKeys.MarkDistance)`,
  number per Q4 default (one decimal under 10 m, whole beyond), rewritten only on change.
- Tests: `MarkMarkerGeometryTests` (relationships: ring radius + arc/2 <= half box; disc radius < ring radius - track/2;
  `MarkMarkerHalfPx * 2 == MarkMarkerBoxWidthPx`; pointer tip inside `MarkMarkerBoxHeightPx`; the 0.85 pop keeps the chip
  >= the 28 px HUD floor); `ChipText_OneDecimalUnderTen`; `ChipRed_IffBeyondShippedKillRange`. Existing `MarkerPlacement`
  tests must stay green with the scaled constants.
- Capture: `mark-onscreen` (larger disc, paw badge, chip above), `mark-hunt-offscreen` (pinned, red chip), `mark-live`
  (tenths + star), `mark-walled` (lock), `mark-shelled` if it fires, `mark-end-word` (K8).
- Rules: CODE-UI-01/02/06/08/11, CODE-TEXT-03/05, ARCH-LAY-15. Risk: 144 px box at the clear-viewport edge on a phone;
  check portrait capture; the marker must not cover the "You" plate (`MarkerPlacement` already slides it).

### K4 Hunter-only ground brackets (senior-dev, M; vfx-artist reviews look)
- New `Assets/_Game/Scripts/Visuals/HuntBrackets.cs` per §1(c), created in `MarkFeedback.Awake`, driven from
  `MarkFeedback.Update` after the model step (owns no state of its own beyond mesh/colour cache); shown iff
  `o.Role == Assassin && o.MarkerAlpha > 0 && MarkFeedback.Target != null` (end fade/hold included, alpha follows
  `MarkerAlpha`). Colour `HuntRules.BracketColour`; lock snap from `MarkMarkerPopFrom` over `MarkMarkerPopSeconds`;
  POUNCE breathe with `MarkMarkerKillPulse`/`Hz`; both off under RM. Static `ShownNow` for the harness.
- `RivalIndicator`: public `RingRadius` getter (read-only). `FeedbackTuning`: `HuntBracketRadiusFactor 1.35`,
  `HuntBracketArcDegrees 50`, `HuntBracketWidth 0.12`, `HuntBracketHeightOffset 0.019`, `HuntBracketSortingOrder 4`.
  Make `FloorRivalArrows.ArrowSortingOrder` public const so the test can relate to it.
- Tests (`HuntBracketsTests`): `SortsAboveFloorArrowsAndRivalRing`; `ColourByState` (arming seat, live blocked red,
  legal yellow); `HiddenForVictimAndThirdParty`; `StaticUnderReducedMotion`; `OneRendererOneSharedMaterial`
  (instantiate in EditMode, CODE-TEST-14 clean-up).
- Capture: `mark-onscreen`, `mark-live`, `mark-kill` (brackets visible, seat/red/yellow, above the arrow ring).
- Rules: ARCH-EVT-01, CODE-ASSET-01/04/12, CODE-UI-08, ARCH-LAY-15, CODE-DESIGN-16. Risk: draws on an invisible
  (Hide) target exactly like the marker already does; adds no information the marker does not (Q8).

### K5 Off-screen arrow from the hunter's bird (developer-sonnet, S)
- `RivalIndicator.cs:414-417`: for the local mark's target that would get a chevron, offer it to
  `FloorRivalArrows.TryRepresent` (seat colour, existing length rule) and never show its chevron; on screen nothing
  changes (ring + brackets). Extract the branch to a pure `MarkedTargetArrowRules.Decide(marked, chevron, floorAccepted)`.
- Tests: `MarkedTarget_OffScreen_FloorArrowNotChevron`, `MarkedTarget_OnScreen_NoArrow`, `Unmarked_Unchanged`.
- Capture: `mark-hunt-offscreen` (pinned marker + seat-colour floor arrow from the hunter's ring).
- Rules: ARCH-EVT-01, CODE-UI-08. Risk: arrow pool full (`FloorRivalArrows.cs:341`) leaves only the pinned marker; acceptable.

### K6 End word (senior-dev, S)
- `UiKeys.HudPopMissed = "hud.pop.missed"` + `UiText.csv` row `hud.pop.missed,MISSED` (the only new string).
- `MarkFeedback.Update` (`:134-143`): on `MarkEvents.Ended`, `FloatingCombatText.SpawnSelfPop(_localHunter, key, colour)`
  with pure `MarkFeedback.EndWordKey(MarkEndCause)` (Missed -> `HudPopMissed`, Safe -> `HudPopSafe`, else null); colour:
  MISSED = Hunt accent (as `:142`), SAFE = the colour `HitFeedback.cs:719` gives SAFE. Marker reason glyph is K3's frozen blocker.
- Tests: `EndWordKey_Table`; `HudPopMissed_HasCatalogueRow` (existing key-coverage test if present, else add).
- Capture: `mark-end-word` (K8) shows MISSED or SAFE + glyph. Rules: CODE-TEXT-01/02/03/09, ARCH-EVT-01.

### K7 Haptics (developer-sonnet, S)
- `IHapticsService.cs`: append `HuntLive = 3, HuntPounce = 4` (append only). `HapticRules`: `HuntLive` [0,30] amp 120,
  `HuntPounce` [0,50,40,50] amp 220; `PatternFor` total over the enum.
- `MarkFeedback`: `[InjectOptional] IHapticsService`; pure `HapticFor(in MarkOutputs)`: Assassin + `KillLive` -> HuntLive,
  Assassin + `KillReady` -> HuntPounce (already rate-limited by `MarkKillReadyMinInterval`), else none.
- Tests: `HapticRulesTests` + `PounceStrongerThanLive` (TotalMs and peak amplitude), `PatternFor_TotalOverKinds`;
  `HapticFor_AssassinOnly`. PC binds `NullHapticsService`: device check deferred to the next Pixel 10 Pro session (say so).
- Rules: CODE-UI-11, ARCH-EVT-01, ARCH-DATA-10. Risk: HuntPounce > Stun in the limiter order, so a pounce buzz replaces a
  stun buzz inside 200 ms; intended (the hunter is the one acting).

### K8 `cw_markshots` additions (developer-sonnet, S)
- `AutoTestDriver.Mark.cs:85-104`: `mark-end-word` 0.3 s after an Assassin mark ends (hold), `mark-end` moved to
  `MarkEndHoldSeconds + MarkEndFadeSeconds + 0.1` (clean). Log line `AutoTest markshots end cause=<cause> blocker=<b>`.
  State line `:129-131` += `blocker`, `brackets=HuntBrackets.ShownNow`. Keep existing log shapes (CODE-TEST-16).
- Capture: the full set once; the walled pass gives a SAFE end, a timed-out stun pass gives MISSED.

### K9 Docs (code-architect inline + librarian, S)
- `docs/FEEDBACK.md` shipped words line (`:164`) += MISSED and the hunter cues; feedback spec row
  (`2026-11-06-ability-feedback-and-icons-design.md:117`) hunter column; `docs/site/mechanics.html:193` (paw, brackets,
  chip, tenths, words, buzz) and republish the compendium to its URL (CODE-PROC-13); `docs/STATE.md` (CODE-PROC-12).
- `librarian`: `docs/FILE_STRUCTURE.md` for `HuntRules.cs`, `HuntBrackets.cs`, `HuntHexRules.cs` (ARCH-LAY-02 not
  triggered: no new folder). Flag `/graphify . --update` (new `.cs` files).

## 3. Git (every item)

Stage by explicit path only, e.g. `git add Assets/_Game/Scripts/Visuals/HuntRules.cs Assets/_Game/Scripts/Visuals/HuntRules.cs.meta ...`.
Never `git add -A`/`.`, stash, reset, or a broad checkout: other sessions share the tree. Never stage
`ProjectSettings/*.asset`, `Assets/UniversalRenderPipelineGlobalSettings.asset`, `CLAUDE.md` or `.obsidian/`. Run
`git status` before each commit. Message `feat(feedback): K<n> <what>`, body with the why (CODE-PROC-11), trailer
`Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Review: code-architect per item; qa-reviewer over K1-K4
pinned to the commit range (CODE-PROC-09).

## 4. Questions for the owner (proposed default in brackets)

1. H7a: the marker already shows the paw (since 55615fc2). [No code; fix the stale docs in K9.]
2. The hex is not at 0.70 in the code during a hunt. [Pin full alpha by test; K2 step 0 checks it on screen.]
3. The disc holds either the paw or the count. [Count in the centre for the whole hunt, paw as a top-left badge.]
4. Whole metres cannot show the 6.2 m line truthfully ("6 m" in red at 6.3). [One decimal under 10 m, whole beyond.]
5. Expiry while POUNCE was legal reads as a pounce. [No word; precise MISSED would need the local cast event as evidence.]
6. A mark that ends early without a pounce (target executed by someone else, left) shows MISSED with no glyph. [Yes.]
7. Only the hunter is stunned: none of the four blockers. [No marker glyph; the hex keeps its stunned cross.]
8. Brackets on a hidden (Hide) target follow the marker, which already tracks it. [Keep; no new information.]
9. The hunt buzzes obey the "Buzz When Hit" toggle. [Yes, label unchanged.]

Nothing blocks K1. K2-K8 need the Editor closed or the bridge up for the suite, and one PC dev-tools session per item.
