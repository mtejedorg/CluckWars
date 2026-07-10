# Improvement Plan — 2026-07-09 (design review follow-up)

Source: full design review of GDD v0.3 + live asset values (session 2026-07-09).
Owner directives from Maestro:
1. **Depositing cargo at base must NOT be instant** — reaching base first is not
   enough to win; deposit is a timed drain that creates an interception window.
2. **3–4 final-minute comeback events**, one picked at random per match
   (extra stats for the last-place player, extra pile, etc.).

Workstreams IP0–IP8, ordered. Each has acceptance criteria. Conventions in
`docs/CONVENTIONS.md` and `.claude/CLAUDE.md` apply to every task (StateAuthority
gates, ILogService only, `LogLevel` alias, `onBeforeSpawned` for tick-zero
networked props, **no ChangeDetector for gameplay-critical logic** — poll in
Render/LateUpdate or use synchronous authority events like
`ChickenCombat.OnDeathAuthority`; the ChangeDetector solo-mode footgun is
documented in STATE.md).

---

## IP0 — Quick correctness + identity fixes

1. **Speedy DisplayName typo**: `Assets/_Game/Data/Classes/Speedy.asset`
   `DisplayName: Speed` → `Speedy`.
2. **Deterministic timer-expiry tie-breaker** (GDD annex 13.3): when the timer
   expires and two+ bases tie on food, rank by (a) food, (b) kills
   (`ChickenMatchStats.Kills` of the corner's chicken), (c) lower `CornerIndex`.
   Implement in `GameManager`'s timer-expiry winner evaluation.
3. **Rename the Warrior passive display** from "Tough" (reads defensive) to
   **"Mighty"** (offensive: +25% outgoing ability damage). Code enum member
   `ChickenPassive.Tough` may stay (serialized as byte; renaming the enum member
   is safe for serialization but touch only display strings + docs unless
   trivial). Update char-select passive copy.

**Acceptance:** class select shows "Speedy" and "Mighty"; a forced two-way tie
at timer expiry resolves by kills, then corner index — no Unity-iteration-order
dependence.

---

## IP1 — Timed deposit (Maestro directive #1)

Today `ChickenCargo.TryDepositAtNearbyBase()` transfers the whole cargo in one
tick (`Cargo = 0f; playerBase.RPC_AddFood(dropped)`). Replace with a rate drain.

1. `MatchConfigSO`: add `[Min(0.5f)] public float DepositRatePerSecond = 6f;`
   (+ matching field in `MatchConfig.asset`). Design intent: deposit ≈2× the
   fastest collection rate so it's a *window*, not a chore — Fatty's full 20
   haul ≈ 3.3 s standing at base; Speedy's 6 ≈ 1 s.
2. `TryDepositAtNearbyBase` (runs in FUN, already StateAuthority-gated at the
   caller): transfer `min(Cargo, DepositRatePerSecond * Runner.DeltaTime)` per
   tick while in range of the own base. `RPC_AddFood` per tick is fine (same
   pattern as pile collection). Accumulate `ChickenMatchStats.RPC_AddDeposit`
   with the actual per-tick amount (or buffer and flush on completion — either,
   but stats must equal food banked).
3. **SFX**: no per-tick spam. Play the deposit SFX once when the drain
   *completes* (cargo reaches 0 while in range); optional subtle start cue.
4. **Feedback**: MatchHud cargo bar already polls — add a "DEPOSITING…" label
   state (replaces "FULL → RETURN TO BASE!" while draining). `ChickenNameplate`
   cargo line updates automatically via existing polling.
5. **Bots**: verify `BotController` ReturnToBase keeps the bot at the base until
   `Cargo <= 0` (it must not exit the state on arrival). Fix the exit condition
   if it uses a threshold.
6. Death mid-deposit needs no new code: stun already drops remaining carried
   cargo — that is the point of the mechanic.

**Acceptance (solo play mode):** a loaded Fatty stands at base and its base
total climbs over ~3 s instead of jumping; killing a chicken mid-deposit drops
the *remaining* cargo as pickups; bots complete full deposits; deposited stat
== base gain; single completion SFX.

---

## IP2 — Final-minute comeback events (Maestro directive #2)

One event fires at **T-60 s remaining**, chosen at random per match by the
state authority. Pool of four:

| Kind | Effect (rest of match) |
|---|---|
| `GoldenPile` | Spawn a bonus 25-food pile near the map center (jittered, keep-clear of the center pile). |
| `UnderdogSurge` | The lowest-scoring live, non-decoy chicken (by its base's `FoodTotal`) gets ×1.4 move speed and ×1.5 collection rate. Chosen once at fire time. |
| `LeaderBounty` | The current leader's chicken is marked; killing it spawns **+8 bonus food** in pickups around the victim (on top of the normal cargo drop). Marker visible on the nameplate (e.g. gold "★"). |
| `Restock` | Every pile refills by +10 food (clamped to its initial amount); depleted piles re-solidify (blocker reactivates — reuse the RestartMatch refill path). |

Implementation:
1. New `MatchEventKind : byte` enum (Fusion serialization — byte backing, same
   rule as `ChickenClass`). `[Networked] public MatchEventKind ActiveEvent` on
   `GameManager`, default `None`.
2. In `GameManager.FixedUpdateNetwork` (StateAuthority): when the match is
   Active and remaining time first crosses 60 s, roll the event
   (`Random.Range` on the authority is fine — the chosen kind replicates via
   the networked prop), apply its one-shot setup, log via ILogService.
3. Per-chicken effects (`UnderdogSurge` speed/rate, `LeaderBounty` mark) are
   `[Networked]` flags on `ChickenController` set by the authority; speed
   multiplier folds into the existing speed calculation (same slot as ability
   speed multipliers), collection multiplier into `ChickenCargo`'s rate.
4. Bounty payout hooks `ChickenCombat.OnDeathAuthority` (synchronous authority
   event — the pattern WS2 established); skip decoys (existing `IsDecoy` guard).
5. **HUD**: all peers poll `ActiveEvent` (poll, not ChangeDetector): MatchHud
   shows a centered banner ~3 s ("FINAL MINUTE: GOLDEN PILE!") + a persistent
   small label near the timer. Use existing overlay/badge styling. Audio: a
   match cue from `ProceduralAudioBank`.
6. `RestartMatch` resets `ActiveEvent = None` and clears all per-chicken event
   flags.
7. Event names/copy: GOLDEN PILE / UNDERDOG SURGE / BOUNTY ON THE LEADER /
   RESTOCK (or better — short, all-caps, readable on mobile).

**Acceptance (solo play mode, force each kind via a debug hook or temporary
default):** each of the four events fires at T-60 s, banner + label render, the
effect is observable (pile spawns and is collectable+solid; surge chicken
visibly faster and collects faster; bounty kill drops +8; piles refill and
re-block), match restart clears everything. No errors in solo; effects are
authority-written, visuals local.

---

## IP3 — Make fighting pay: kill bounty

Every kill (not just during `LeaderBounty`) spawns a flat **+5 food** in
pickups placed between victim and killer (biased toward the killer), in
addition to the victim's dropped cargo. Hook `OnDeathAuthority` /
`CreditKillToAttacker` path; skip decoy victims; attacker resolved via
`ChickenCombat.Id` (the B1-fixed id space).

Rationale from the review: TTK on a Warrior is ~10 s of chasing ≈ 20 food of
farming opportunity cost; the flat bounty plus the drop makes ganking
opportunistic instead of strictly irrational.

**Acceptance:** forced kill on an empty-cargo bot spawns 5 food of pickups;
killing a decoy spawns nothing; stats/kill credit unchanged.

---

## IP4 — Economy retune

1. `MatchConfig.asset`: `FoodTargetToWin` 70 → **110** (code default in
   `MatchConfigSO` stays a generic fallback; update it to 110 too for parity).
2. Rationale: WS1 overshoot (win in ~45–60 s vs the ~126 s design goal), plus
   the new timed deposit and kill bounty change throughput. 220 total map food
   with a 110 target means at most 2 players can mathematically reach it —
   maximum contest pressure.
3. **Measure, don't guess**: run 3 solo bot-only matches (Editor play mode via
   Unity MCP). Success band: winner reaches 110 in **100–160 s**, or timer
   expiry with the leader ≥ 90. If outside the band, adjust
   `FoodTargetToWin` (±10) — do NOT touch class collection rates this pass.

**Acceptance:** measured numbers recorded in STATE.md; target within band or a
documented recommendation if the band needs a human feel-pass.

---

## IP5 — Assassin rescue

Speedy currently strictly dominates Assassin (HP 80>70, speed 10>8, cargo 6>5,
rate equal). The 3rd slot must be an *addition*, not compensation for strictly
worse stats.

1. `Assassin.asset`: `MoveSpeed` 8 → **9**; `CargoCapacity` 5 → **8**.
2. `SneakySteal.asset`: `StealAmount` 4 → **6**; `Cooldown` 6 → **5**.
   (Steal must fit free capacity — that's why capacity rises to 8; keep the
   clamp behavior.)
3. GDD §5.4 + annex note recording the change and why.

**Acceptance:** values live in assets; a Sneaky Steal against a loaded bot
transfers 6 (or clamps to free space); GDD updated.

---

## IP6 — Noise abilities become real plays

1. `Invisibility.asset`: `Duration` 2 → **4** s.
2. Doppelganger: the decoy must be readable as a play — verify where the decoy
   *lifetime* lives (SO `Duration` 1.5 may be cast time; the B5 note mentions a
   4 s decoy). Ensure the decoy exists ≥ **4 s**; bump the SO field that
   controls it. Cooldown 11 s stays.

**Acceptance:** invisibility lasts 4 s in play mode; decoy persists ≥ 4 s and
bots/nameplates treat it as before (kill-credit guard intact).

---

## IP7 — KPI instrumentation (match-end summary)

At match end, the state authority logs ONE Info block via ILogService:
match length (s), winner corner + food, event fired, per-chicken
kills / food deposited, pickups spawned vs collected (if cheaply countable).
Purpose: bot-match regression eyeballing after every tuning change — no
telemetry backend, just a grep-able line block tagged `MatchSummary`.

**Acceptance:** one clean block per match end in the Editor log; restart
produces a fresh block next round.

---

## IP8 — Documentation sync

1. **GDD**: add the timed-deposit rule to §2 (core loop) and §6 (a deposit in
   progress is interruptible — stun drops what's left); new section
   **§6.6 Final-Minute Events** with the 4-event table; update §5 star tables
   to match live asset values (or replace stars with the real numbers);
   **remove the Resistance stat row** (unimplemented) or mark it explicitly
   "not implemented — see annex"; update §7.3 cooldown tiers to actual ranges
   (Short 3–6, Medium 7–11); rename Tough→Mighty; record IP4/IP5/IP6 value
   changes; annex entries for anything that deviates.
2. **STATE.md**: session entry (what shipped, measured pacing numbers, open
   follow-ups).
3. **ROADMAP.md**: tick if a phase entry covers this work; otherwise add a line.

**Acceptance:** a fresh reader of the GDD gets numbers that match the assets.

---

## Verification gate (whole plan)

- Batchmode compile: zero errors.
- Solo play mode via Unity MCP (if the Editor is reachable): IP1 deposit drain,
  at least 2 of 4 events forced + observed, kill bounty, IP4 pacing runs.
  If the Editor/MCP is unreachable, compile-only + say so explicitly in the
  report and in STATE.md — do not claim play verification that didn't run.
- Commit per workstream on `develop` (small, revertable commits, message style
  matches recent history e.g. "IP1: timed deposit drain at base").
