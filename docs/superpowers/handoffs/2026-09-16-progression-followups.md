# Handoff — Progression Pitch Milestone & Follow-ups (2026-09-16)

Written for whoever (or whichever session) picks this up next — Maestro, a fresh Claude
session, or a subagent. Everything below was verified against the actual repo state on
2026-09-16, not just reported by an agent.

---

## 1. Status: the progression pitch milestone is done

Slices 0–4 are all committed on `feature/progression`, **not yet merged to `develop`**:

| Slice | Commit | What |
|---|---|---|
| 0 | `2da2c9f` | Event contract, stable unlock keys |
| 1 | `9c58f46` | Announcement sites |
| 2 | `a4d7bbd` | Tracker, journal, Grain economy |
| 3 | `fbef3ca` | Identity — profile, generated names, nameplate, records, career |
| — | `a96d6eb` | (chore, unrelated) Unity 6000.3.14f1 QualitySettings schema bump |
| 4 | `02bba21` | Ramp, weekly goals, daily task |

Unfiltered EditMode suite: **809/809 green** as of slice 4. Every slice was reviewed by
`qa-reviewer` before its commit. Full design/rationale: `docs/superpowers/specs/2026-09-11-progression-design.html`
and `docs/superpowers/plans/2026-09-11-progression-pitch-milestone.md`. Slice-by-slice detail
lives in `docs/STATE.md` (top four entries).

**Not yet decided:** when/how `feature/progression` merges into `develop`. That's a call for
Maestro, not made here.

## 2. Immediate next step (not started)

Per Maestro's 2026-09-14 ruling, the next piece of work is **one combined pass**:
- Rewrite GDD §8/§11 to describe the shipped progression system.
- Sync `docs/site/index.html` (the mechanics compendium) to match.

Both must read their numbers from the shipped `.asset` files and code, **never** from GDD
prose (the GDD carries known-stale figures — see `CLAUDE.md`'s standing instruction on the
compendium).

## 3. Cosmetic — mastery ring overlaps the class name

Found during slice 3's review, still present as of slice 4 (not fixed, out of scope for the
milestone). At both aspect ratios tested, the mastery ring drawn on a class-select chip
overlaps the class name's last two characters: `WARRIOR`→ring covers `OR`, `SPEEDY`→`DY`,
`ASSASSIN`→`SI`. Needs a `ui-designer` pass on `Assets/UI/CharacterSelect.uxml` /
`Assets/UI/Styles/CluckWarsTheme.uss` (wherever the ring is positioned relative to the name
label). Low priority, not blocking anything.

## 4. Cleanup — a stale worktree

`fix/ability-description-damage` (worktree `C:/Users/MARCO/Documents/GitHub/CluckWars-turtle`)
is fully merged into `develop` (its one commit, `1fba816`, is an ancestor of `develop`'s tip)
and the worktree is clean. Safe to remove:

```bash
git worktree remove ../CluckWars-turtle
git branch -d fix/ability-description-damage
```

---

## 5. Assassin execute — Findings 1 & 2

**Status check (2026-09-16): this appears to already be done, not a fresh task.**
`fix/assassin-execute-credit` (worktree `C:/Users/MARCO/Documents/GitHub/CluckWars-execute`)
already has two commits ahead of `develop`, clean tree:

- `692736f` — "execute now pays its bounty and credits the kill" (touches `AssassinExecute.cs`,
  `ChickenCargo.cs`, and adds `AssassinExecuteCreditTests.cs`)
- `86fe81c` — "pay the execute only after the victim validates" (touches `AssassinExecute.cs`,
  `ChickenCombat.cs`)

**Suggested next action is review + merge, not re-implementation:** send this branch to
`qa-reviewer` against the brief below, confirm the two findings are actually resolved (ideally
with a live solo check and, if possible, a real multi-client Shared session), run the unfiltered
EditMode suite, then merge to `develop`. **It will conflict with `feature/progression`** — slice 2
added one line to `AssassinExecute.Press` (`_cargo.AnnounceExecuteSteal(...)` before the transfer
RPC) that this branch doesn't have; keep both when resolving.

The original brief, for reference (what it was scoped to fix):

> In the Cluck Wars Unity project (`C:\Users\MARCO\Documents\GitHub\CluckWars`; Unity 6, Photon
> Fusion 2 Shared Mode, Zenject), the Assassin's Mark/Kill execute has two gameplay bugs. They
> were found and documented during the progression work — see the "Finding 1" and "Finding 2"
> bullets in the top entry of `docs/STATE.md` on branch `feature/progression`.
>
> **Finding 1 — the transfer and the kill credit never resolve, even in solo.**
> `AssassinExecute.Press` (`Assets/_Game/Scripts/Gameplay/AssassinExecute.cs`) passes its own
> `Id` — the `AssassinExecute` behaviour's `NetworkBehaviourId` — to
> `ChickenCargo.RPC_TransferAllToBountyBag` and to `ChickenCombat.RPC_ExecuteRemoval`.
>
> - `RPC_TransferAllToBountyBag` then calls `Runner.TryFindBehaviour(assassinId, out ChickenCargo)`.
> - `RPC_ExecuteRemoval` → `CreditKillToAttacker` calls `TryFindBehaviour(attackerId, out ChickenCombat)`.
>
> Both lookups return false. This was checked live in `GameMode.Single` on all four chickens;
> only `TryFindBehaviour<NetworkBehaviour>` succeeds. The result: the victim's cargo is zeroed
> but never reaches the assassin's bounty bag, and an execute never increments Kills. Likely
> fix: pass `_cargo.Id` and `_combat.Id`, or resolve the `NetworkBehaviour` and use
> `GetComponent`.
>
> **Finding 2 — the bounty is credited on a proxy in Shared PvP.**
> `RPC_TransferAllToBountyBag` runs on the victim's state authority and writes
> `assassinCargo.BountyBag += …` to a proxy of the assassin's chicken. That write is never
> replicated, and the assassin's next update overwrites it. Fix: credit the bounty bag on the
> assassin's own state authority — as `ChickenCargo.ReceiveStolen` does for the other steals —
> and leave only the drain to the victim's RPC.
>
> **Rules:**
>
> - Use your own git worktree off `develop`, e.g.
>   `git worktree add ../CluckWars-execute -b fix/assassin-execute-credit develop`. The main
>   checkout has uncommitted progression work on `feature/progression`: never switch branches,
>   stash, reset or commit there. The Unity Editor holds the main checkout's project lock, so
>   run tests headless against your worktree path.
> - Keep the change minimal and behaviour-focused. Don't change RPC signatures unless there's no
>   alternative, and keep all `HasStateAuthority` gates. Note that `feature/progression` adds
>   one line to `AssassinExecute.Press` — an `_cargo.AnnounceExecuteSteal(...)` call before the
>   transfer RPC — so expect a small conflict when the branches meet.
> - Route per `CLAUDE.md`: `senior-dev` implements, `qa-reviewer` reviews. Add EditMode tests
>   pinning that the ids passed resolve to `ChickenCargo` and `ChickenCombat`.
> - Verify Finding 1 live in a solo round: the cargo reaches the bounty bag and `Kills`
>   increments.
> - Verify Finding 2 in a real multi-client Shared session if you can. `tools/run-clients.ps1`,
>   or the `coop-test` skill. If you can't, say so plainly.
> - Run the unfiltered EditMode suite, commit on your branch, and report.

---

## 6. Egg Shell — give it a real defensive effect

**Status check (2026-09-16): not started.** `fix/egg-shell-defence` (worktree
`C:/Users/MARCO/Documents/GitHub/CluckWars-eggshell`) has zero commits ahead of `develop` and a
clean tree — this one is genuinely still pending, the brief below applies as written.

> In the Cluck Wars Unity project (`C:\Users\MARCO\Documents\GitHub\CluckWars`), the Egg Shell
> ability (`Assets/_Game/Scripts/Abilities/EggShellAbilitySO.cs`, asset
> `Assets/_Game/Data/Abilities/EggShell.asset`) currently only sets `MovementLocked = true` for
> its 2 s duration. Since GDD v0.4 removed HP and damage, it grants no protection at all —
> nothing in the codebase gives it steal immunity or control immunity
> (`ChickenController.IsControlImmune` is granted by other abilities). It is the only ability
> every class can take (`AllowedClasses: 15`), is Warrior's only Defense-category ability, and
> the progression onboarding ramp (shipped this milestone, ramp step 2) uses it to teach
> defence. Maestro has decided to fix it in the game.
>
> **Goal:** give Egg Shell a genuine defensive effect that fits the v0.4 control + steal combat
> model (for example, immunity to steals and/or control effects while active), keeping the
> "seal yourself in, can't move" identity.
>
> **Rules:**
>
> - Route per the project's `CLAUDE.md`: `mechanics-designer` proposes the effect; `senior-dev`
>   implements; `qa-reviewer` reviews. Read `docs/GDD.md` §6 (control + steal) and §7
>   (abilities), `docs/CONVENTIONS.md`, and `docs/STATE.md` first.
> - Balance must go through the BalanceOracle / SCT rules — never hand-tune stats.
> - Work in your own git worktree off `develop` (e.g.
>   `git worktree add ../CluckWars-eggshell -b fix/egg-shell-defence develop`). The main
>   checkout has uncommitted progression work on `feature/progression` — never switch branches,
>   stash, reset or commit there. The Unity Editor holds the main checkout's project lock, so
>   run tests headless against your worktree path.
> - Update the Egg Shell description to match the new effect. Per `CLAUDE.md`, update
>   `docs/site/index.html` (the mechanics compendium) if it quotes Egg Shell, reading values
>   from the `.asset`.
> - Turtle Mode (`TurtleModeAbilitySO.cs`) has the same problem (it's a pure self-slow now).
>   Flag it in your report, but don't change it — that decision is Maestro's.
> - Run the unfiltered EditMode suite, commit on your branch, and report what effect was chosen
>   and why.

**Note:** the ramp (slice 4, already shipped) grants Egg Shell at step 2 regardless of whether
this lands — that was Maestro's explicit ruling on 2026-09-12, restated in `docs/STATE.md`'s
slice 4 entry. This fix improves what the ramp is already teaching; it doesn't block it.

---

## Suggested order of operations

1. Review and merge `fix/assassin-execute-credit` — it's done, just needs eyes and a merge
   through the `feature/progression` conflict.
2. Remove the stale `CluckWars-turtle` worktree (§4) — a one-line cleanup, no dependency on
   anything else.
3. Egg Shell (§6) and the GDD/compendium sync (§2) can run in parallel — the compendium sync
   should read Egg Shell's final effect once §6 lands, so sequence the compendium pass after (or
   note it as a known-stale figure if run before).
4. The mastery-ring cosmetic fix (§3) is low priority and independent of everything else.
