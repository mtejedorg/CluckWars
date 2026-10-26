# Handoff — Progression & Follow-ups (updated 2026-10-26)

Supersedes the 2026-09-16 version. Everything here was checked against the repo and the PR on
2026-10-26. I did **not** re-run tests (Unity bridge was down); test counts are the ones recorded
on the branch.

---

## 1. Status

Progression slices 0–4 are implemented and shipped in **PR #1** (`feature/progression` → `develop`),
open, mergeable, clean, 12 commits ahead of `develop` and 0 behind (rebased onto `aadcd048`).

| Commit | What |
|---|---|
| `cc7e103c` … `abf28e6b` | Slices 0–4: contract, announcement sites, journal + Grain, identity, ramp/goals/daily task |
| `86fb80b6` | Port of the progression UI and event sites onto the redesigned menus and results panel |
| `d8c51a84`, `2aeea3ae` | Dev-tools harness bypasses the ramp; per-client `-progressionDir` |
| `8f1ea682`, `339aa8c2` | Review fixes B1 (ramp locks on GEAR UP), B2/S3/S4 (identity strip is the PROFILE button; menu fits 20:9 and 9:16) |
| `098ac3d4` | Review status doc after the rebase |

Recorded gate on the rebased tip: EditMode **2726/2726**, production + dev-tools builds OK, multiplayer
matrix 9/9 PASS, 0 rejected RPCs. The full branch review is `docs/proposals/PROGRESSION_REVIEW_2026-10.md`
on the PR branch (not on `develop`).

Verified in review: no `develop` logic lost or duplicated in the rebase; emits happen only in
authority-gated code; CW-230 / `ExecuteRules` / `StealRules` untouched; the ramp bypass cannot reach a
release build.

## 2. Open before or just after merge (from the review)

Priority order. None blocks merging on its own, but **S2 makes goals/records count something that did
not happen.**

1. **S2 — execute counted as a steal before it is validated.** `AssassinExecute.Press`
   (`AssassinExecute.cs:170`) calls `AnnounceExecuteSteal` with a snapshot of the victim's cargo. A refused
   execute still counts as "stolen" / "rivals robbed". Fix: emit `ResourceStolen(...)` right after
   `BountyBag += allowed` in `ChickenCargo.RPC_CreditExecuteLoot` (develop's validated path), delete
   `AnnounceExecuteSteal`, update the tests that pin the Press-time announce, remove the stale
   "Known gap" remark at `ChickenCargo.cs:689`.
2. **S6 — hard-coded English and a missing glyph.** `" GRAIN"` (`MenuUiController.cs:1123`) and `"+N Grain"`
   (`MatchOverlaysController.cs:802`) should be `UiText` keys, with consistent casing. The daily task's
   `✓` (`MenuUiController.cs:~2795`) is in none of the fonts; use the USS-drawn tick the class tile has.
   `Profile.uxml` and `ProfileController.cs` labels are all literals too.
3. **S1 — mastery ring covers the selected-check badge**, and in portrait covers the chickens' heads; also a
   "0" ring on a fresh profile reads like a notification counter (consider hiding level 0). This is the
   "ring over the class name" issue first seen in slice 3.
4. **S7 — a refused locked class tile explains itself on another page.** `RefuseLocked` writes to the GEAR UP
   detail strip; on PICK YOUR BIRD the player only sees a 220 ms flash. Needs a message surface on the class page.
5. **N1–N8** — minor: duplicate self-inject in `ChickenMatchStats.Spawned`; stale comments (`RoundEdgeDetector`,
   `GameManager`, `MenuUiController`, `Profile.uss`); a peer can miss `RoundEnded` if the host restarts within
   one snapshot; ordinary steals are announced optimistically and a refused drain's refund never retracts them;
   `-progressionDir` works in release builds; `docs/STATE.md` needs a port summary under develop's top entry and
   the "slice 2 findings still open" note marked superseded.

**Not verified by anyone:** the daily-task card when visible and a completed task; the Profile page at any size;
9:21 phones and real devices; a live tap on the identity strip; multi-peer behaviour of the S2 announce.

## 3. Deferred documentation pass (per Maestro, 2026-09-14)

One combined pass: rewrite GDD §8/§11 for the shipped progression system and sync `docs/site/index.html`
(compendium). Read numbers from the shipped `.asset` files and code, never from GDD prose. Not started.

## 4. Earlier follow-ups — now resolved or obsolete

- **Assassin execute (Findings 1 & 2): fixed on `develop`** by CW-050 (`a22dee88`: kill credited and looted cargo
  paid via a bounded, receiver-validated report) and CW-230 (sender validation, mark + stun evidence).
  `fix/assassin-execute-credit` (`692736f2`, `86fe81c3`) is **not** in `develop` and is superseded — do not merge it.
- **Egg Shell: fixed on `develop`.** Description now reads "…nobody can push you, stop you or steal your food."
  `fix/egg-shell-defence` has zero commits and is obsolete. (Turtle Mode: not re-checked.)
- **Pink shader / `Mobile_RPAsset` corruption: fixed** (guardrail test added).

## 5. Housekeeping

- Local `feature/progression` still points at the **pre-rebase** tip `02bba218`. Reset it to
  `origin/feature/progression` (local only) before using it.
- Stale worktrees safe to remove: `CluckWars-turtle` (`fix/ability-description-damage`, merged),
  `CluckWars-eggshell` (`fix/egg-shell-defence`, empty), `CluckWars-execute` (`fix/assassin-execute-credit`,
  superseded). Check each is clean first:
  ```bash
  git worktree remove ../CluckWars-turtle ../CluckWars-eggshell ../CluckWars-execute   # one at a time
  git branch -D fix/egg-shell-defence fix/assassin-execute-credit                        # -d for the merged one
  ```

## 6. Suggested order

1. Fix S2, S6, S7 (and S1 if cheap) on `feature/progression` with a sonnet implementer; re-run the gate.
2. Merge PR #1.
3. STATE.md tidy (N8), then the GDD §8/§11 + compendium pass.
4. Clean up worktrees and branches.
