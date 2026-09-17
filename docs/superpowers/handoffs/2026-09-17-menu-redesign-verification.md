# Handoff — Menu Redesign: Real Verification Needed (2026-09-17)

Written for whoever (or whichever session) picks this up next — Maestro, a fresh Claude
session, or a subagent. **This handoff exists because the code below has never actually been
compiled or tested against Unity.** Everything reported as "verified" by the agents that wrote
it was checked against the wrong project (see §1) — do not take their pass/fail claims on
faith, even though their commit messages and PR description sound confident.

---

## 1. Why this needs re-verification: the worktree's Unity MCP was pointed at `develop`, not the worktree

The implementation work happened in a git worktree
(`.claude/worktrees/cluck-wars-menu-ui-a62862`, branch `claude/cluck-wars-menu-ui-a62862`).
The `ai-game-developer` MCP bridge, however, talks to a single running Unity Editor process
that has **the main checkout** open (`C:\Users\MARCO\Documents\GitHub\CluckWars`), not the
worktree folder. Confirmed directly: the compiled `Assembly-CSharp.dll` the Editor was running
resolved to `MenuUiController` methods (`ShowMainMenu`/`ShowCharacterSelect`/`ShowLobby`) that
only exist in **the main checkout's** unmodified `develop` copy of the file — the worktree's
copy (with the new `ShowClassSelect`/`ShowLoadout`/`_armedSlot` state machine) was never
loaded, ever.

So when the implementing agents reported "clean recompile, 0 errors, 563 EditMode tests pass,"
they were compiling and testing the **old, unmodified `develop` code**, not their own changes.
That result is meaningless for this PR. Nothing about the actual new code has been build-checked
by Unity yet. This is the first real verification pass.

## 2. What's in the PR

**PR:** https://github.com/mtejedorg/CluckWars/pull/2 (`claude/cluck-wars-menu-ui-a62862` → `develop`)

Files changed: `Assets/UI/CharacterSelect.uxml` (reworked), `Assets/UI/CharacterSelectClass.uxml`
(new), `Assets/UI/Styles/CluckWarsTheme.uss`, `Assets/_Game/Scripts/UI/MenuUiController.cs`
(~900 lines changed — most of the controller was rewritten).

**Why:** Maestro's complaints about the shipped character-select screen — class selection
confusing, ability cards overlapping (Assassin's 6-card class pool wraps and paints over other
elements), ability-to-button mapping feeling like "a lottery," main menu cluttered. Design was
worked out interactively with Maestro via an HTML mockup before any Unity code was touched:
https://claude.ai/artifact/XQ9Fuon3aLdtDLFwvyi6zh (reference only, not authoritative over the
real per-class data — the mockup used placeholder ability/spec names in places).

**What changed:**
- Character select split into two screens: **Choose Your Chicken** (class + specialization
  picked together, one tap on a per-card pill selects both) and **Build Your Loadout**
  (abilities).
- The actual bug fix: `TogglePick`'s auto-fill-next-open-slot + compact-on-drop logic is
  deleted. Replaced with an explicit, always-valid "armed slot" — tapping an ability fills
  whichever slot is armed and auto-advances; tapping a slot just arms it; **nothing ever
  compacts or shifts** once assigned. This is the actual mechanism behind the "lottery"
  complaint, not just a visual reskin.
- Ability pools (`CommonCards`/`ClassCards`) now scroll horizontally instead of wrapping — the
  documented overlap bug (see the pre-existing comments in `CluckWarsTheme.uss` about
  `.cw-pick-scroll`) can't recur regardless of pool size.
- Numeric Cargo/Rate/Speed stat pips replaced with qualitative strong/weak one-liners (no
  attack/HP stat exists since the v0.4 combat rewrite — don't reintroduce one).
- **Bug found and fixed along the way, not originally in scope:** `PassiveAbilitySO
  .SignatureAbility` was never actually force-equipped into a slot — only its UI label existed.
  Some classes' real loadout silently differed from what the picker showed. Generalized
  `SeedMandatoryPeck` → `SeedForcedAbilities` to actually place it, mirroring
  `MatchBootstrapper.ResolveLegalLoadout`'s resolution order.

## 3. Design deviations worth a judgment call (not bugs, but diverge from the approved mockup)

1. **Strong/weak callouts ended up as one shared preview panel, not duplicated per class card.**
   The mockup had each card show its own strength/weakness inline. The implementing agent found
   `RoleCalloutStrong`/`RoleCalloutWeak` would need to be 4 duplicate-named elements to be
   per-card (UQuery-by-name across a whole tree only hits the first match), and built a single
   panel that updates for whichever class is currently selected instead — mirroring the old
   "Column B" preview pattern that already existed pre-redesign. Reasonable call, but confirm it
   still reads well with 4 cards to compare — a shared panel means you can't see two classes'
   callouts side by side.
2. **`NextBtn` (Step 1 → Step 2) is never disabled.** A default class is always seeded on entry,
   so there's no "nothing selected" state to gate against — intentional, not an oversight, per
   the implementing agent's note.
3. **`PreFilledTag` ("comes with Peck") covers Warrior, Speedy, and Fatty** — pulled from Peck's
   real `AllowedClasses` bitmask, not just Speedy/Fatty as originally guessed in the task brief.
   Trust the code-derived value over the earlier guess.
4. Step 2's method is named `ShowLoadout()`, not `ShowCharacterSelect()` — clearer name, but the
   serialized UXML field stayed `_characterSelectUxml` to avoid re-pointing the scene reference.

None of these block anything; they're just worth a look before merging.

## 4. Flagged, deliberately not touched

Warrior/Speedy/Assassin's **Mighty/Bracer/Opportunist passives are permanently inert** — their
damage-modifying hooks were deleted 2026-08-13 along with the whole HP/damage system (see the
doc comment in `PassiveAbilitySO.cs`). This redesign makes specialization more prominent in the
UI, which makes a currently-broken feature more visible, not less. Separate follow-up, not part
of this PR.

## 5. What to actually do

1. **Before switching branches in the main checkout**, run `git status` there — there was
   uncommitted state as of 2026-09-17 (`Assets/Plugins/NuGet/*` + `Packages/manifest.json`/
   `packages-lock.json`, likely the Unity MCP plugin's own self-update; and an untracked file
   at `docs/superpowers/handoffs/2026-09-16-progression-followups.md`, which belongs to
   unrelated in-progress work — don't lose it). Neither should conflict with this PR's files,
   but confirm before checking out.
2. Check out `claude/cluck-wars-menu-ui-a62862` in the main checkout, let Unity recompile, and
   actually read the console output this time — confirm 0 compile errors for real.
3. Run the unfiltered EditMode suite.
4. Play-test both screens for all 4 classes:
   - Choose Your Chicken: tap each specialization pill directly, confirm it selects class+spec
     in one tap and shows real `PassiveAbilitySO.Description` text (not placeholder copy).
   - Build Your Loadout: confirm slot assignment is stable — assign an ability to slot 2, clear
     slot 1, confirm slot 2's ability does **not** shift to slot 1. This is the actual bug fix;
     if it regresses, the redesign hasn't fixed anything.
   - Confirm Assassin's 6-card CLASS pool scrolls horizontally without overlapping anything.
5. Check mobile/tablet/4K breakpoints for safe zones and touch target sizes (this project's
   Android-landscape mobile sizing contract, documented at the top of `CluckWarsTheme.uss`).
6. Decide on §3's deviations, request changes or approve, then merge PR #2 into `develop`.

## Suggested order of operations

1. Resolve the uncommitted-state check (§5.1) before touching branches.
2. Checkout + recompile + EditMode suite (§5.2–3) — if this fails, stop and report back; the PR
   needs a real fix, not another unverified merge.
3. Play-test pass (§5.4–5).
4. Judgment calls on §3, then merge or request changes.
5. Mighty/Bracer/Opportunist (§4) is a separate, independent follow-up — file it, don't fold it
   into this review.
