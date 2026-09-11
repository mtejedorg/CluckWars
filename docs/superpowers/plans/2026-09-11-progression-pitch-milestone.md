# Cluck Wars — Progression: Pitch Milestone (Slices 0–4)

## Context

Design approved (interactive spec: `docs/superpowers/specs/2026-09-11-progression-design.html`). This plan takes the pitch
milestone — **slices 0–4, fully local, no backend** — from sketch to grounded implementation plan.
Every file:line below was re-verified against the working tree on 2026-09-10.

### Rulings
| Topic | Ruling |
|---|---|
| Milestone | **Slices 0–4.** Contract, tracker, journal, Grain, profile, records, career, goals, ramp. Flock Rank ladder, collections, cloud save, social come after the pitch. |
| Ramp | **4 steps**, ceiling 12 rounds. |
| Goals | **Weekly three** from templates × thresholds, cumulative across the week — **plus one daily first-game task**: a single-round template shown before the day's first round, open all day (a bad first round never burns it), paying the daily bonus. Hidden while the ramp runs. Rested ×1.5 stays. |
| Usernames | **Generated** (`SwiftBeak#2213`, re-rollable). Chosen names are a later slice. |
| Steal announcements | **One `ChickenCargo.ReceiveStolen` chokepoint.** |
| In-flight work | **Commit first, then branch** (pre-flight). |

---

## Pre-flight — land the in-flight work

`git status`: **50 files uncommitted, +2,863 / −452** — work STATE.md records as shipped and green
(562/562): ability-feedback audit, RPC hardening (`NominalStealAmount`, `RPC_DrainStolen` gaining
`thiefId`), `CastArchetype` replacing `AbilityAnimationClip`, `HitFeedback`, the Ability Lab `DevRow`.
Last commit is `ab62529`. It overlaps nearly every file slices 0–4 edit.

1. Unfiltered EditMode run on the current tree — confirm 562/562.
2. Commit via `/housekeeping`.
3. Branch `feature/progression` from that commit.

---

## The contract, grounded

```csharp
// Assets/_Game/Scripts/Progression/IMatchEventSink.cs
public interface IMatchEventSink
{
    void RoundStarted     (RoundRuleset ruleset);
    void ResourceBanked   (int actorId, float amount);
    void ResourceStolen   (int actorId, int victimActorId, float amount);
    void OpponentDisabled (int actorId);
    void AbilityResolved  (int actorId, string abilityKey, bool connected);
    void RoundEnded       (RoundStandings standings, int localActorId, float durationSeconds);
}
```

**Two changes from the design page, both forced by the code:** `OpponentDisabled` loses `targetId`
(the kill is credited inside `RPC_CreditKill`, whose signature carries no target, and changing a
networked RPC is out of bounds); `RoundEnded` gains `localActorId` (see below).

| Event | Site | Runs on | Notes |
|---|---|---|---|
| `ResourceBanked` | `ChickenCargo.FlushBaseDeposit`, beside `RPC_AddDeposit` — `ChickenCargo.cs:342` | depositing chicken's state authority | One call per batched flush |
| `ResourceStolen` | **New `ChickenCargo.ReceiveStolen(amount, victim)`** replacing `thiefCargo.Cargo += x` at: `SnatchAbilitySO.cs:75`, `SneakyStealAbilitySO.cs:63`, `ScrapAbilitySO.cs:77`, `RollTrampleAbilitySO.cs:106`, `ChickenController.cs:1205` (Spine Coat steal-back — credits the *defender*) | thief's authority | Behaviour-identical; test forbids direct cargo writes on steal paths |
| `OpponentDisabled` | `ChickenMatchStats.RPC_CreditKill` — `ChickenMatchStats.cs:45` | attacker's state authority | Kills == executes in v0.4 |
| `AbilityResolved` | `AbilityController.TryActivate`, beside `LastCastEventId++` — `AbilityController.cs:674` | caster's authority | `connected = hitCount > 0`, already computed at `:640`. Needs slice 0's key |
| `RoundStarted` / `RoundEnded` | **`GameManager.LateUpdate` polling `State`** against a cached previous value | every peer | Covers `State = Active` at `GameManager.cs:359` and `:664`, and `EndMatch` at `:526` |

### Why a poll, not a ChangeDetector
Solo runs `GameMode.Single` (`FusionNetworkService.cs:60`); `PlayerBase.cs:123-126` documents that
`ChangeDetector` + `Render` can silently skip locally-written networked properties in that mode, which
is why `PlayerBase` polls in `LateUpdate`. A missed `Ended` edge is a silently lost reward — and it
would happen **only in solo, i.e. the pitch build.** `GameManager` has no `Render()` today. Capture
timing is safe: `RestartMatch` (`:574`) zeroes `PlayerBase.FoodTotal` only after the 6 s delay.

### Local actor resolution
- `actorId` = the chicken's `NetworkId` raw value, opaque to progression.
- In solo the player's peer is state authority for the three bots (spawned `inputAuthority:
  PlayerRef.None`, `IsBot = true` — `MatchBootstrapper.cs:227`, `:234`), so bot events **do** reach the sink.
- The tracker **buckets every actor per round**; `RoundEnded` names the local actor, resolved with
  `HasInputAuthority && !IsDecoy` (the `MatchCamera.cs:250` pattern — decoys share their caster's input
  authority). Emit sites never filter.

### Standings
`PlayerBase.FoodTotal` descending; **ties share the better placement.** Do not copy the existing
display sorts (`MatchOverlaysController.cs:632`, `MatchHudController.cs:210`) — unstable `List.Sort`.

## Boundary enforcement
No `.asmdef` under `Assets/_Game` — one `Assembly-CSharp`, so the compiler can't enforce the boundary.
Source-scanning EditMode tests do, following `StealRulesTests.cs:412`:
- no file under `Gameplay/` or `Abilities/` references `CluckWars.Progression` except `IMatchEventSink`;
- no steal path writes `.Cargo +=` outside `ReceiveStolen` (allowlist `PeckAbilitySO.cs:121`, foraging).

## Persistence
Installed UGS packages: `authentication`, `core`, `multiplayer` — **no Cloud Save, no Newtonsoft.**
Journal = **JSON Lines via `JsonUtility`** at `persistentDataPath/progression/journal.jsonl`; one round
per line, appended and flushed before anything is shown. A crash can only tear the final line; the
loader drops it and reports via `IProgressionService.OnFault`. No new package for the milestone.

---

## Slices

### Slice 0 — Contract & stable keys
- **New:** `Progression/IMatchEventSink.cs`, `NullMatchEventSink.cs`, `RoundTypes.cs`
  (`RoundRuleset`, `RoundStandings`), `UnlockKeyTable.cs` (explicit `ChickenClass → key`, never
  `ToString()`), `Editor/Tests/UnlockKeyTests.cs`, committed key manifest.
- **Modified:** `Abilities/AbilityBaseSO.cs` (`_unlockKey`, `OnValidate` fill-once), `Installers/ProjectInstaller.cs`
  (bind Null sink), `Editor/Tests/TestAssets.cs`.
- **Tests:** every ability has a key · keys unique · keys match manifest.
- **Done when:** suite green (562 + new), zero behaviour change.

### Slice 1 — Announcement sites
- **Modified:** `Gameplay/ChickenCargo.cs` (`ReceiveStolen`; emit in `FlushBaseDeposit`), four steal
  abilities + `Gameplay/ChickenController.cs`, `Gameplay/ChickenMatchStats.cs`, `Gameplay/AbilityController.cs`,
  `Gameplay/GameManager.cs` (`LateUpdate` poll + standings builder).
- **New:** `Editor/Tests/MatchEventSinkTests.cs`, `Editor/Tests/ProgressionBoundaryTests.cs`, recording test sink.
- **Tests:** one assert per site · boundary scans · poll fires once per transition incl. restart · tie rule ·
  `ReceiveStolen` credits exactly what the raw write did.
- **Done when:** a solo play-mode round, captured by a recording sink, holds all four actors' streams and
  `RoundEnded` names the human.

### Slice 2 — Tracker, journal, Grain
- **New:** `Progression/MatchTracker.cs`, `RoundOutcome.cs` (facts only), `ProgressionRules.cs` (pure
  `Evaluate`), `ProgressionConfigSO.cs` + `Data/Progression/ProgressionConfig.asset` (five earn constants + the daily-task bonus), `JournalStore.cs`, `IProgressionService.cs` + `ProgressionService.cs`
  (no Null impl — `IsReady=false` hides UI).
- **Modified:** `Installers/ProjectInstaller.cs` (real sink, service, config slot — null-fallback + loud
  warning like `_matchConfig`), `UI/MatchOverlaysController.cs` (one "+N Grain" line).
- **Tests:** fold deterministic + order-independent · `Evaluate` total · torn-line recovery · journal before
  display · config-derived expectations · slot resolves (copy `LobbyMatchSettingsTests.cs:126`).
- **Done when:** 20 solo rounds → quit → relaunch → correct balance; kill mid-results → award survives.

### Slice 3 — Identity
- **New:** `UI/Profile.uxml` + `ProfileController.cs`, `Progression/NameGenerator.cs`, nameplate composer,
  `RecordDefinitionSO.cs` + `RecordEngine.cs`, career view.
- **Modified:** `UI/MenuUiController.cs` (Profile page; `SetPage` `:213-217` if-chain → page list; mastery
  rings at the chip loop `:306`), `UI/MainMenu.uxml` (strip + Profile entry), `UI/CharacterSelect.uxml`.
- **Tests:** predicates pure and re-evaluable · a record added later credits past play · ≥1 non-purchasable
  nameplate part · new glyphs are USS shapes/sprites (LilitaOne ~225 glyphs).
- **Done when:** profile, nameplate, records and career render from local play; a new record credits a
  player who already earned it.

### Slice 4 — Weekly goals, the daily task & the ramp
- **New:** `Progression/GoalTemplateSO.cs` (template × threshold tiers, cumulative over the week),
  `GoalRotation.cs` (seeded by ISO week; shows the refresh day, never a ticking countdown),
  `DailyTask.cs` (one single-round template per day, shown before the first round, open all day, hidden during the ramp),
  `RampStepSO.cs` ×4 assets (asset references, **never scene-baked**), `RampController.cs`.
- **Modified:** `UI/MenuUiController.cs` (locked state in `MakeAbilityCard`, not the `RebuildPickRows`
  filter; class chips gated; refusal on the press), `UI/CharacterSelect.uxml`, `UI/Lobby.uxml`.
- **Tests:** loadouts resolve against `AbilityRegistrySO.All` · every `AbilityCategory` held after step 3 ·
  every goal and daily task completable without placing first · the daily task survives a failed first round · ramp state never networked · no step past 3 attempts.
- **Done when:** a fresh profile walks the ramp in ≤12 rounds; a returning one gets three weekly goals and a
  daily task it can complete while losing.

| Step | Rounds | Grants | Objective |
|---|---|---|---|
| 1 · First Cluck | 1 | Warrior + Peck, 3 slots hidden | Bank 10 |
| 2 · Getting Robbed | 2–3 | + Egg Shell, Headbutt | Headbutt connects twice, or bank 20 |
| 3 · Your Loadout | 4–5 | + Scrap, full Warrior pool, specialization | Steal 5 |
| 4 · Other Chickens | 6–8 | Fatty → Speedy → Assassin | One round as each |

**Weekly goal templates** (initial set): Bank {100|150|200} · Rob {10|15} rivals · Finish a round with
more stolen than banked · Land {15|25} control abilities · Play {20|30} rounds.

---

## Execution
Pre-flight via `/housekeeping`. Then per project routing: `code-architect` → `senior-dev`, one slice per
pass, `qa-reviewer` on slices 1 and 2 (networked emit sites, persistence). STATE.md updated each slice.

## Verification
- **EditMode, unfiltered.** Baseline 562/562.
- **Play mode, solo:** journal flushed before results draw; network killed mid-results keeps the award;
  10 skipped results screens → balance equals the sum of awards.
- **UI:** `screenshot-game-view` on every UXML/USS change, Pixel 9 resolution, reduced motion on/off.
- **Docs:** GDD §8 rewritten when slice 2 closes §11 item #4; CONVENTIONS.md gains the boundary rule.
