# Progression branch review — 2026-10-24

**Branch under review:** `feature/progression` = `origin/feature/progression` @ `cfd153ee` (PR #1), reviewed on the
local branch `review/progression-2026-10-24` (no commits besides this file; nothing pushed).
**Compared against:** `develop` @ `aadcd048`.

## Gates run in this review

| Gate | Result | Evidence |
|---|---|---|
| Dev-tools Windows build | `[Build] StandaloneWindows64 OK. Size: 225,7 MB, Time: 39,9s` (devtools=on, define `CLUCKWARS_DEV_TOOLS`) | scratchpad `build-win-dev.log` line 12976 |
| EditMode, full suite, batchmode **without** `-nographics` | `total="2719" passed="2719" failed="0" inconclusive="0" skipped="0"` | scratchpad `editmode-results.xml` |
| Orientation capture (harness, `-cw_menushots`) | 1920x1080, 2400x1080, 1080x2400 (ran 540x1200), 540x960: HUD scans 0 bad at every size. **Menu probe offenders at 2400x1080 (main 2) and 540x960 (main 1)**: menu offenders are reported, not gated, which is why the earlier "5/5 PASS" missed them. Portrait runs show "scenario exit=1" only because this review asked for 1 round (`--rounds 1`); the scenario wants 2. | see the paths per finding |
| Ramp captures without the harness (fresh `-progressionDir`, `-cw_menushots main,class,gear`, no auto-test menu flow, so the ramp is **not** bypassed) | 4 sizes | `Captures/review-progression-2026-10-24/ramp/<size>/shots/` |
| Results panel (one solo round, window grab once `Match ended`) | 4 sizes; Grain recorded every time (`+39 / +42 / +42 / +40 Grain (rested)`) | `Captures/review-progression-2026-10-24/results/<size>/results-<size>-b.png` |

`Captures/` is git-ignored: the screenshot paths below are on Maestro's PC only.

Side effects: the build re-serialized `Assets/Settings/PC_RPAsset.asset` and `ProjectSettings/UnityConnectSettings.asset`.
Both were restored with `git checkout --`, and the working tree is clean.

---

## BLOCKER

### B1. The ramp's ability locks never show on GEAR UP, and their dimming is overwritten by the starter style — **FIXED on the review branch (see "B1 fix" below)**
- `MenuUiController.AddDeckCard` added `cw-ability-card--locked` and the LOCKED badge **only when the card was built**.
  The badge was absolute at the card's top-left and was added *before* the category band, so the band drew over it.
  The probe confirms it: `U1 .cw-ability-card__lock ... other=.cw-ability-card__band overlap=3808px2`. `EnsureDeck`
  builds the deck once per class, and nothing re-marked the cards on `OnUnlocksChanged`, so a completed ramp step (or a
  deck built before the journal loads) would leave stale locks.
- **The class name collides with develop.** `RefreshDeck` (`:2186`, develop code) already uses
  `cw-ability-card--locked` to mean "pre-equipped starter" and rewrites it on every card on every refresh. So even a
  correctly built ramp lock loses its dim on the next refresh. The new USS rules (`Assets/UI/Styles/CluckWarsTheme.uss:1860`)
  now also apply to starters.
- Effect: on a fresh profile (ramp step 1 grants only Peck, `Data/Progression/Ramp/FirstCluck.asset`), Egg Shell,
  Cluck Shock, Dive Bomb, Wing Slam, Scrap and Ruffle look pickable. Tapping one is refused (`OnAbilityCardTapped`
  `:1951` checks at tap time), and the only feedback is a word in the detail strip. This is the onboarding surface of slice 4.
- Screens: `Captures/review-progression-2026-10-24/ramp/1920x1080/shots/menu-gear-ramp1920x1080.png`,
  `.../ramp/540x1200/shots/menu-gear-ramp540x1200.png`. Compare `.../ramp/1920x1080/shots/menu-class-ramp1920x1080.png`
  in the same run, where the class tiles **are** locked.
- The harness masks this because `AutoTestDrivesTheMenu` bypasses the ramp.

#### B1 fix (review branch, uncommitted)
- `MenuUiController.cs`:
  - Every deck card gets a hidden `RampLock` badge, in flow under the name where STARTER sits.
  - `RefreshDeck` toggles the new class `cw-ability-card--ramp-locked` and the badge from `IsRampLockedCard`
    (= `IsAbilityLocked && !IsPreEquipped`). A class starter such as Headbutt before ramp step 2 is equipped regardless,
    so it never reads as locked, and tapping it is no longer refused.
  - `OnUnlocksChanged` is subscribed next to `OnIdentityChanged` (and unsubscribed in `OnDisable`). It re-marks the
    class tiles when PICK YOUR BIRD is showing, and the deck whenever it exists.
- `CluckWarsTheme.uss`:
  - The ramp dim moves to `--ramp-locked` (icon, sprite and name at 0.4), leaving `--locked` to develop's starter meaning.
  - The card badge is in flow and the chip badge stays absolute.
  - Both badges go to 30 px. The menu probe's floor is `UiLayoutRules.MenuFontFloorPx = 30`; 28 px tripped U4 on every
    badge, before and after the fix.
- New `Editor/Tests/RampDeckLockTests.cs` (4 source-scan tests): no lock decided at build time, `RefreshDeck` toggles
  `--ramp-locked`, unlock changes refresh the deck, and the ramp dim never styles `--locked`.
- Verified:
  - EditMode 2723/2723 (2719 + 4).
  - `[Build] StandaloneWindows64 OK` (dev tools).
  - Fresh-profile captures with the harness off. GEAR UP probe is 0 offenders at 1920x1080, 2400x1080, 540x1200
    (1080x2400) and 540x960, against 6–10 before the fix.
  - Screens: `Captures/review-progression-2026-10-24/ramp-b1-30px/<size>/shots/menu-gear-b1<size>.png`.
  - PICK YOUR BIRD is 0 offenders except 3× U1 at 1080x2400 (LOCKED badge vs mastery ring), which is S1's ring placement.

### B2. Main menu overflows the panel at 20:9 landscape (title and hint cut off). This is a regression from develop.
- The extra `ProfileRow` (`Assets/UI/MainMenu.uxml:98`) adds a fourth action row to a column that develop sized to fill
  the 20:9 panel exactly.
- Probe at 2400x1080: `ui offender U3 ... el=.cw-title<MainMenuPage [739,-44,654x128] partly off the panel 0,0,2133x960`
  and `el=MainMenuHint [836,955,460x52]`. The title also overlaps the Settings sheet title (U1, `overlap=4430px2`).
- Screens: progression `Captures/session-20261024-094834/shots/menu-main-s2400x1080.png` ("CLUCK WARS" cut at the top,
  hint plate cut at the bottom). Develop `654cd39` at the same size fits:
  `Captures/orient-20261023-193138/2400x1080/menu-main-s2400x1080.png`.
- 2400x1080 is the Pixel 9/10 landscape aspect. The same row clips the hint at 540x960 (finding S3).
- Not fixed: this needs a placement decision. Options: (a) make the identity strip itself the PROFILE button and drop
  the row; (b) put PROFILE in the HOST/JOIN row as a third button; (c) a compact `.layout--short` rule for the row.
  (a) costs no vertical space and is where players already look for "my profile".

---

## SHOULD-FIX

### S1. The class-tile mastery ring covers the selected check badge, and in portrait it covers the chickens' heads
- The ring is built in code at `top:6 right:6`, 72 px (`MenuUiController.cs:1432`, `Profile.uss:104`). develop's selected
  check `.cw-class-tile__check` sits at `top:10 right:10`, 60 px (`CluckWarsTheme.uss`). The ring is added later, so it
  draws over the tick. In portrait the tiles are 1/4 width, so the ring sits on the bird's face.
- Screens: `Captures/session-20261024-094347/shots/menu-class-s1920x1080.png` (Warrior tile),
  `Captures/session-20261024-140234/shots/menu-class-s1080x2400.png` (all four heads covered),
  `Captures/review-progression-2026-10-24/portrait/540x960/menu-class-s540x960.png`.
- STATE.md:1666 already listed an older form of this ("ring over the class name") as open. The port moved the ring but
  did not resolve it. At 1080x2400 the class LOCKED badge also overlaps the ring (probe U1 ×3,
  `.../ramp-b1-30px/540x1200/client.log`). Also: a "0" in a coloured ring reads like a notification counter on a fresh profile. Consider
  hiding level 0.

### S2. Execute is announced to progression as a steal before it is validated, though develop now has a validated, exact path
- `AssassinExecute.Press` (`Assets/_Game/Scripts/Gameplay/AssassinExecute.cs:170`) calls `_cargo.AnnounceExecuteSteal`
  with the assassin's **snapshot** of the victim's cargo, before `RPC_TransferAllToBountyBag`. On develop the transfer is
  validated on the victim's authority and paid back through `ChickenCargo.RPC_CreditExecuteLoot`
  (`ChickenCargo.cs:1051`). That path runs on the assassin's authority, gated by attribution and the
  `_pendingExecutes` ledger, and it pays only what was actually taken (`BountyBag += allowed`, `:1080`).
- Result: a refused execute (CW-230 mark check, stale mark, victim banked while stunned) still counts as "Stolen" /
  "rivals robbed" for goals and records. In Shared mode the amount can be wrong. It does **not** weaken the game-state
  validation (CW-230 / ExecuteRules are untouched, and the RPC bodies are unchanged).
- Fix: emit `ResourceStolen(MatchActorId.Of(Object), MatchActorId.Of(victim), allowed)` right after
  `BountyBag += allowed` in `RPC_CreditExecuteLoot`, and delete `AnnounceExecuteSteal`. No `IsForward` guard is needed in an
  RPC. The tests that pin the Press-time announce need updating.
- The `AnnounceExecuteSteal` remark "Known gap, not fixed here (2026-09-13)" (`ChickenCargo.cs:689`) describes a bug develop
  fixed (CW-050). It is stale and misleading.

### S3. The hint is clipped at 540x960 portrait (9:16), and the identity strip has no room to grow
- `ui offender U3 scope=menu page=main ... el=MainMenuHint<MainMenuPage [227,1593,468x53] partly off the panel 0,0,923x1641`.
  The develop baseline at this size had 0 main-page offenders. Screen: `Captures/review-progression-2026-10-24/portrait/540x960/menu-main-s540x960.png`.
- The identity strip (`MainMenu.uxml:38`, absolute, `white-space: nowrap`) already **touches the title** at 1920x1080
  with a 16-char name (`Captures/session-20261024-094347/shots/menu-main-s1920x1080.png`). The longest generated name is
  `FeatheredScarecrow#0000` (23 chars, `NameGenerator.cs`). Add a title line and a 4–5 digit Grain balance, and the strip
  will run into "CLUCK WARS" in landscape and into the gear in portrait. This is computed, not captured: no long name
  appeared in any run. Needs a max-width with ellipsis, or the name and Grain stacked.
- Weak spot 4 (9:21, 746 px panel width) was **not captured**. Given the 9:16 result, expect the same clipping or worse there.

### S4. The landscape main-menu trims from the pre-rebase slice-3 fix were kept, but the reason for them is gone
- `CluckWarsTheme.uss:431-469`: cast art 200→150, cast margins 44/48→12/12, primary min-height 148→126, secondary-row gap
  20→10. All are justified by "the header-row overlap fix — see `.cw-menu-hero`'s comment". That rule no longer exists on
  develop, and the identity strip is now absolute, so it adds no height. These are develop's tuned values silently shrunk
  (the U2 gap note on develop asks for ≥ 19 px between PLAY SOLO and HOST/JOIN). Revert to develop's values once B2 is
  solved another way.

### S5. Branch is not on current develop
- Merge-base is `654cd39d`, not `aadcd048`. Develop's `7dd13b0f` (Root-claim recent-position hardening in
  `ControlClaimRules` / `AbilityZone` / `RecentPositions`) is not in PR #1, so the 2719 count was run without it. A trial
  merge (`git merge-tree`) is clean: only `docs/STATE.md` changed on both sides, and it auto-merges. A plain
  `git merge develop` (no history rewrite) brings it in. Expected count afterwards: 2721.
- The local branch `feature/progression` still points at the **pre-rebase** tip `02bba218`. Anyone who checks it out
  gets the old code. Reset it to `origin/feature/progression` (local only) or delete it.

### S6. Hard-coded English in UI, against the UiText-keys rule
- `MenuUiController.cs:1123` `" GRAIN"`; `MatchOverlaysController.cs:802` `"+N Grain"`. The casing also differs between
  them: GRAIN on the strip, Grain on the results panel (see results screens).
- `MenuUiController.cs:2795` prefixes `✓ `. U+2713 is in **neither** Nunito-Bold nor LilitaOne, nor in NotoEmoji
  (which only has U+2714), and none of them is wired as a fallback. A completed daily task will likely render a missing
  glyph. Not captured: the daily-task card is hidden while the ramp runs, and no fresh profile clears the ramp in one
  round. Use the USS-drawn tick the class tile already has.
- `Assets/UI/Profile.uxml` (`BACK`, `PROFILE`, `TITLE`, `EMBLEM`, `BANNER`, `RECORDS`, `NEW NAME`, the not-ready line, the
  reroll hint) and `ProfileController.cs` (`NO TITLE`, `WHILE LOSING`, `EARNED`, `ROUNDS`, `WINS`, `GRAIN`, `BANKED`,
  `STOLEN`, `RIVALS ROBBED`, `MOST …`) are all literals. They predate the port but the PR ships them.

### S7. A locked class tile's refusal message goes to an element on another page
- `RefuseLocked` writes the message to `_abilityDetailName/_abilityDetailText` (`MenuUiController.cs:1632`), which live
  on the GEAR UP page (`_loadout.Q("AbilityDetail")`, `:1793`). On PICK YOUR BIRD the player sees only a 220 ms border
  flash and never sees *why*. Needs a message surface on the class page (the hero panel's description, for example).

---

## NICE-TO-HAVE

- **N1. Rebase leftover: duplicate self-injection** in `ChickenMatchStats.Spawned` (`ChickenMatchStats.cs:68` and `:79`).
  The second block is now dead. Harmless.
- **N2. Stale comments from the pre-redesign flow.** `RoundEdgeDetector.cs:21` says the restart path is "Ended → Active".
  It is Ended → Starting → Active (solo PLAY AGAIN) or → WaitingForPlayers (MP). `GameManager.cs:270` says
  "before RestartMatch zeroes FoodTotal after the restart delay", but there is no restart timer. The bases are zeroed by
  the reset on PLAY AGAIN, so the Ended frame still sees the totals: **behaviour verified correct**, wording stale.
  `MenuUiController.cs:580` cites a `_pages` field that does not exist (it is `Pages()`). `Profile.uss:229` describes a
  ring position from the old chip layout, and `.cw-chip-mastery` is unused.
- **N3. Edge case: a peer can miss RoundEnded.** In Shared mode, a peer that gets Ended and the reset in one snapshot
  sees Active→Starting, which the detector treats as abandoned. The round is then not recorded. This only happens if the
  host restarts within one snapshot of Ended. PLAY AGAIN needs a press, so it is low risk.
- **N4. Ordinary steals are also announced optimistically** (`ChickenCargo.ReceiveStolen`), and a refused drain's refund
  (`RPC_RefundRefusedSteal`) never retracts them. This is documented as intended. If goals get competitive, retract in
  the refund.
- **N5. `-progressionDir` is honoured in production builds** (`ProjectInstaller.ResolveProgressionDirectory`, not gated on
  `DevTools.Available`). It only moves a local file, so the risk is low, but it is a dev switch shipping in release.
- **N6. `ProjectInstaller.cs:159,170` use `Debug.LogWarning`.** This matches develop's existing installer precedent
  (the composition root runs before `ILogService`), so it is consistent, not a regression.
- **N7. Locked class tiles read as available at a glance.** Only the art is dimmed (0.45). Name and role stay full ink,
  and the badge is the main cue. Screen: `.../ramp/1920x1080/shots/menu-class-ramp1920x1080.png`.
- **N8. `docs/STATE.md` tidy (weak spot 2).** There are no conflict markers. The 5 progression slice sections were
  inserted at line ~1598 inside the 2026-09 history, and slice 0 is glued to the previous section with no separator
  (`STATE.md:2232`). There is no top entry for the port, and "Two gameplay-side findings remain open from slice 2"
  (`:1674`) is outdated (execute credit fixed on develop by CW-050/CW-230). On merge, develop's 2026-10-24 top entry
  arrives automatically. Add the port summary under it and mark the slice-2 items superseded.

---

## Checked and found correct

- **Lost or duplicated logic (weak spot 1).** I read the `develop...feature/progression` diff of `GameManager`,
  `ChickenMatchStats`, `ChickenCargo`, `AssassinExecute`, `AbilityController`, `MatchOverlaysController`,
  `MatchOverlays.uss/uxml` and `MenuUiController` line by line. No `=======` ruler mis-split, no dropped develop lines.
  The diffs are additive except the `OnDisable` move in `MenuUiController` (behaviour preserved, now also unsubscribes)
  and the CSS trims in S4. The only duplication is N1. The 37 ability `.asset` diffs are `_unlockKey` plus Unity
  field reordering. No balance value changed.
- **Network safety (task 2).** `ReceiveStolen` credits first and unconditionally, so it behaves the same as the old
  `Cargo +=`. Emits happen only inside `HasStateAuthority`-gated ticks, behind `Runner.IsForward`.
  `RPC_CreditKill` emits `OpponentDisabled` only **after** `ExecuteRules.EvaluateKillCredit` accepts. CW-230,
  `ExecuteRules`, `StealRules` and `ControlClaimRules` are untouched. `RoundAnnouncer` polls replicated state from
  `LateUpdate`, outside the simulation, with no authority gate by design. `MatchTracker` buckets per actor and records
  only the local actor at round end. The only semantic gap is S2 (progression-side only).
- **Ramp bypass in release (weak spot 5).** `AutoTestDrivesTheMenu` (`MenuUiController.cs:1586`) is
  `DevToolsAvailable && AutoTestArgs...WantsMenuFlow`. `DevTools.Available` is the `CLUCKWARS_DEV_TOOLS` define or
  `Debug.isDebugBuild`, and `CluckWarsBuildMenu` builds with `BuildOptions.None`, so it is false in a production build.
  The `&&` short-circuits before the args are read. No other bypass exists in `Progression/`.
- **Results "+N Grain" line.** Placed above the actions, centred and readable at all four sizes, with no overlap. It
  appears a frame after the panel, as designed.
- **Gear / coop / coopjoin / settings / disconnect pages.** Unchanged from develop apart from B1. Lobby daily-task card:
  correctly hidden while the ramp runs. Its shown state could not be captured (see S6).

## Not verified

- The daily-task card when visible, and a completed task (`✓`). They need a profile past the ramp.
- The Profile page (it is not in `-cw_menushots`) at any size.
- 9:21 / 746 px phones. Real devices (notch, Pixel).
- Multi-peer behaviour of the S2 announce. The Assassin execute was not exercised live in this review's runs.
- Runs after merging develop's `7dd13b0f` (S5).
