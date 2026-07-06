# Handoff — Full-Project Revision Findings & Execution Roadmap (2026-07-05)

**Audience:** any agent (or human) executing follow-up work. This doc is
self-contained — you do not need the conversation that produced it.
**Source:** full code + design revision of the v0.3 vertical slice performed
2026-07-05 on `develop` (HEAD `88371c9`). All line numbers refer to that commit;
re-locate by symbol name if files have drifted.

**Overall verdict from the revision:** engineering quality is solid for a demo
(interfaces, authority gates, RPC discipline, optimistic-credit patterns, doc
discipline). The slice is held back by (a) a broken match economy — the numbers
make both fighting and the win target pointless — and (b) a small set of real
bugs, the worst of which cluster around one architectural flaw: **player
identity is fragmented across `PlayerRef`, `HomeCornerIndex`, and `BotClaimed`.**

---

## Ground rules for every task below

Read `docs/CONVENTIONS.md` first. Non-negotiables that apply to this work:

- `ILogService` only — never `Debug.Log` (exception: the two existing
  `Debug.LogWarning` calls in ability SOs are themselves a smell; see WS2-4).
- `HasStateAuthority` gate at the top of every `FixedUpdateNetwork`.
- Cross-authority writes only via `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]`.
- Files importing both `CluckWars.Logging` and `Fusion` need
  `using LogLevel = CluckWars.Logging.LogLevel;`.
- `ChickenClass : byte` — do not change the backing type.
- `[Networked]` props needed at tick zero are set in `onBeforeSpawned`.
- New scripts written while the Editor is busy can be silently excluded from
  Assembly-CSharp — prefer the Unity MCP `script-update-or-create` tool for new
  `.cs` files (see STATE.md "Tooling gotcha").
- After each workstream lands: update `docs/STATE.md`, one commit per task
  (suggested messages given below).

**Verification loop available to agents:** Unity MCP (`editor-application-set-state`
to enter play mode, `screenshot-game-view`, `console-get-logs`) for solo tests;
`tools/run-clients.ps1` for multi-client Windows tests. Emulators are dead on
this machine — never attempt them; the real device is a Pixel 9.

---

## WS0 — Repo hygiene (5 min, do first)

**Problem:** untracked Unity Performance Testing package residue sits in
`Assets/Resources/` — anything in Resources is baked into player builds.
`Packages/manifest.json` + `packages-lock.json` are also modified (package was
added), and `Assets/packages-merged-link/` appeared.

**Tasks:**
1. Confirm the performance-testing package is not intentionally used
   (`grep -r "Unity.PerformanceTesting" Assets/` — expect no hits outside the
   residue). If unused: delete `Assets/Resources/PerformanceTestRunInfo.json`,
   `PerformanceTestRunSettings.json` (+ `.meta`s), `Assets/packages-merged-link*`,
   and revert the `manifest.json` / `packages-lock.json` changes.
2. If it IS intentional, gitignore the generated files instead and document why
   in STATE.md.

**Acceptance:** `git status` clean of test residue; Windows build contains no
PerformanceTestRun* assets.
**Commit:** `Chore: remove performance-testing residue from Resources`

---

## WS1 — Balance economy pass (asset edits only, highest fun-per-hour)

### Why (keep this rationale in STATE.md when done)

Current numbers: 180 s match, target 150, map total 220 food, collection rates
1/s (Warrior/Speedy/Assassin) and 2/s (Fatty), caps 4/6/10/25.

- At 1/s, 150 food is 150 s of pure pile-standing **before** travel and
  deposits; real throughput is ~0.4–0.6 food/s → 250–375 s. **Three of four
  classes cannot reach the win target inside the timer at all.** Only Fatty
  (~1/s effective) approaches it. Every match ends on the timer; the
  "FIRST TO 150" HUD badge is fiction; Fatty is structurally dominant.
- Fighting doesn't pay: a Peck kill needs 5+ casts (15 dmg vs 70–120 HP, 3 s CD)
  ≈ 15–20 s of chasing to force one 5 s stun + drop. The same time farming
  yields as much, risk-free. This — not missing juice — is the residual
  "boring" from the juice-pass feedback.
- Assassin can't express its role: cargo cap 4 but Sneaky Steal amount 5 —
  every steal clamps to its own free space.

**Design goal: uncontested time-to-target ≈ 70 % of the timer**, so both win
conditions are live and a stun (5 s + cargo drop) is worth ~10–15 food of tempo
— making fights the fastest route to the lead (GDD §3 scarcity curve intent).

### Target values

Edit assets directly (same procedure as commit `1bcd204`) or use the Balance
Editor window (`Cluck Wars / Balance / Balance Editor`).

`Assets/_Game/Data/MatchConfig.asset`:

| Field | Old | New |
|---|---|---|
| FoodTargetToWin | 150 | **70** |
| MatchDurationSeconds | 180 | 180 (keep) |

`Assets/_Game/Data/Classes/*.asset`:

| Class | CollectionRate old → new | CargoCapacity old → new |
|---|---|---|
| Warrior | 1 → **2** | 10 (keep) |
| Speedy | 1 → **2** | 6 (keep) |
| Assassin | 1 → **2** | 4 → **5** |
| Fatty | 2 → **3** | 25 → **20** |

(Fatty cap trimmed so a single full haul isn't 28 % of the win target.)

`Assets/_Game/Data/Abilities/`:

| Asset | Field | Old | New | Rationale |
|---|---|---|---|---|
| FlyingPeck.asset | TrampleDamage | 200 | **50** | 2–3 hit kill, not 1 (see WS2-1 — this is also a bug fix) |
| Peck.asset | PeckDamage | 15 | **28** | 3–4 hit kill at 3 s CD |
| CluckShock.asset | ShockDamage | 20 | **35** | AoE, 7 s CD, should trade favourably vs Peck per-cast |
| SneakySteal.asset | StealAmount | 5 | **4** | ≤ smallest cap so the number shown is the number stolen |

Also update the code defaults in the SO classes to match
(`RollTrampleAbilitySO.TrampleDamage`, `PeckAbilitySO.PeckDamage`,
`CluckShockAbilitySO.ShockDamage`, `SneakyStealAbilitySO.StealAmount`) so a
fresh asset isn't born broken.

**Slippery double-dip (GDD annex 13.2):** the passive currently reduces both
slow *magnitude* (`SlipperySlowRetention = 0.50`, `ChickenController.ApplySlow`)
and *duration* (`SlipperyDurationReduction = 0.40`, `RPC_ApplyAbilitySlow` /
`RPC_ApplyRoot`). Per GDD §5.2 the passive is duration-only. **Remove the
magnitude reduction** (delete the `Slippery` branch inside `ApplySlow`), keep
duration. Update GDD annex 13.2 to say "resolved".

### Verification

Solo play mode via Unity MCP, watch the leaderboard: at least one competitor
should reach 70 before the timer in a mostly-uncontested run; kill TTK with
Peck ≈ 3–4 casts. Log a 2-line summary of observed pacing in STATE.md.

**Commits:**
`Balance: match economy pass — reachable win target, fights that pay`
`Balance: Slippery passive is duration-only per GDD §5.2`

---

## WS2 — Small code fixes (independent of each other; land before WS3)

### WS2-1 · Flying Peck instakill + missing range gate

`Assets/_Game/Scripts/Abilities/RollTrampleAbilitySO.cs`
- Damage handled in WS1. Additionally: the ability has **no**
  `IndicatorRange` / `RequiresEnemyInRange` overrides, unlike Peck/CluckShock —
  a whiff burns the 6 s cooldown with zero feedback. Add:
  `public override float IndicatorRange => ForwardOffset + SweepRadius;` and
  `public override bool RequiresEnemyInRange => true;`
  (this automatically wires the HUD grey-out and the `TryActivate` refusal —
  no other code changes needed; see `AbilityController.TryActivate` and
  `TouchControlsHud` three-state buttons).

**Acceptance:** in solo, Flying Peck button greys out with no enemy in ~3.4 u;
a landed hit takes a bot to ~50 % HP, not to 0.
**Commit:** `Fix: Flying Peck — sane damage + range gate (was a 200-dmg free-cast instakill)`

### WS2-2 · Root Egg roots its own caster

`RootEggAbilitySO.OnActivate` spawns the zone at the caster's feet
(`RootEggAbilitySO.cs:44`), and `AbilityZone.FixedUpdateNetwork` roots the
*first* chicken found with **no caster exclusion** (`AbilityZone.cs:119-137`).
The caster is inside the radius on the next tick → self-root button.

**Fix:**
1. Add to `AbilityZone`: `[Networked] public NetworkBehaviourId OwnerChicken { get; set; }`.
2. Stamp it in **both** zone-spawning SOs' `onBeforeSpawned`
   (`RootEggAbilitySO`, `FeatherTrapAbilitySO`):
   `zone.OwnerChicken = ctx.Controller.Id;` (capture outside the lambda like
   the other fields).
3. In the root scan, skip the owner:
   `if (chicken.Id == OwnerChicken) continue;`
4. Decide + document: does the owner's own Feather Trap slow the owner?
   GDD intent is a thrown trap for *others* — recommend excluding the owner in
   `ChickenController.CheckAbilityZoneSlow` too
   (`if (zone.OwnerChicken == this.Id) continue;`). Note it in GDD §7.2.

**Acceptance:** solo — cast Root Egg while standing still: caster does NOT get
the green root ring; a bot walking onto the egg does.
**Commit:** `Fix: ability zones no longer affect their caster (Root Egg self-root)`

### WS2-3 · Root zone scan ignores the authored radius

`AbilityZone.cs:122` scans with the serialized `_triggerRadius` (prefab default
1.5) instead of the `TriggerRadius` property that honours the networked
per-spawn override — Root Egg's authored 0.8 radius is silently ~doubled.
Slow zones already use the correct property.

**Fix:** replace `_triggerRadius` with `TriggerRadius` in the
`Physics.OverlapSphere` call (and in the `Spawned` debug log while there).

**Acceptance:** with WS2-2 in, a bot must come within ~0.8 u (not 1.5) to
trigger the egg.
**Commit:** `Fix: root zone uses networked radius override, not prefab default`

### WS2-4 · Death pipeline rides a mechanism documented as unreliable in solo

Cargo drop, ability cancel, and kill stats all hang off `ChickenCombat.OnDeath`,
which fires from the `ChangeDetector` in `ChickenCombat.Render()`
(`ChickenCombat.cs:116-154`). STATE.md (v0.3.1 base-tinting entry) records that
`ChangeDetector + Render()` **silently skipped locally-written `[Networked]`
props in `GameMode.Single`** — the exact write pattern `RPC_ApplyDamage` uses
when the authority damages itself locally. If a skip happens, the death drop
silently never spawns and nothing logs.

**Fix (keep cosmetics on Render, move consequences to the state change):**
1. In `RPC_ApplyDamage`, where `IsStunned = true` is set
   (`ChickenCombat.cs:190-195`), invoke a new authority-side event, e.g.
   `OnDeathAuthority?.Invoke()`, directly (this code already runs only on the
   StateAuthority).
2. Move the subscribers that mutate state to it:
   `ChickenCargo.HandleDeath` (drop spawn) and
   `AbilityController.HandleOwnerDeath` (deactivate). Their existing
   `HasStateAuthority` guards become redundant but keep them (cheap).
3. Keep `Render()`/`OnDeath` for every-peer cosmetics (animator, SFX, shake,
   nameplate ☠).
4. `Respawn()` path (stun expiry) needs no change — it already runs in FUN.

**Acceptance:** solo — kill a loaded bot; a `FoodPickup` spawns every time
(run 5 kills); `Death drop: spawned FoodPickup` appears in logs each time.
**Commit:** `Fix: death consequences fire from authority state change, not Render ChangeDetector`

### WS2-5 · Minor sweep (single commit, optional but cheap)

- Delete unused `KnockbackDecayRate` const in `ChickenController.cs:71`
  (the live one is in `ChickenMovement`).
- `MatchBootstrapper.Start` is `async void` — wrap the body in
  `try { … } catch (System.Exception e) { _log?.Error(Source, $"Session start failed: {e}"); }`.
- Replace the two `Debug.LogWarning` calls in `RootEggAbilitySO` /
  `FeatherTrapAbilitySO` with nothing (SOs have no injected logger; simplest
  compliant option: route through `ctx.Controller`'s logger if exposed, or
  leave a `// TODO(logging)` — do NOT add a service locator).
- Proxy-side HP shows 0 until the authority's first FUN initializes it
  (`_hpInitialized` lazy init) → transient `IsDead == true` on proxies at spawn.
  Cheap hardening: initialize `HP` in `onBeforeSpawned` from
  `MatchBootstrapper`/bot spawn (registry lookup by class), keep the lazy init
  as fallback.

**Commit:** `Cleanup: dead const, async void guard, proxy HP init`

---

## WS3 — Identity unification (the one real architectural fix)

**Do this BEFORE the next 3–4 player device session** — it sits directly under
the unresolved BUG-5 ("third player can't connect" era) surface.

### The flaw

Three identity systems disagree:

| System | Used by | Weakness |
|---|---|---|
| `PlayerBase.Owner` (PlayerRef) | GameManager assignment, win checks, tints | assignment has nearest/first-unowned **fallbacks** that can diverge from home corner |
| `ChickenController.HomeCornerIndex` | deposits (`ChickenCargo.FindNearestBaseInRange`), restart teleports, leaderboard, nameplates | stamped as `PlayerId % 4` — **Fusion does not reuse PlayerIds**, so one leave+rejoin gives PlayerId 4 → corner 0 → two players on one base |
| `PlayerRef attacker` in combat RPCs | kill credit, Spine Coat reflect | all bots are `PlayerRef.None` → **Spine Coat does nothing vs bots** (3/3 opponents in the solo build) and bot attackers are indistinguishable |

Failure modes already latent: a player whose base assignment fell back to a
non-home corner deposits into an **unclaimed base that never counts for the
win**; rejoin corner collisions; a whole Defense ability inert in solo.

### Target state

**Corner index is the single match identity. Combat attribution uses
`NetworkBehaviourId`, not `PlayerRef`.**

### Tasks (order matters)

**WS3-1 · Corner assignment that survives rejoins.**
Replace `PlayerId % 4` in `MatchBootstrapper.PickSpawnCorner`
(`MatchBootstrapper.cs:386-392`) with a deterministic **join-order slot**:
sort `Runner.ActivePlayers` by `PlayerId` and use the index of `player` in that
sorted list (all peers agree on `ActivePlayers` contents; sorting removes
ordering nondeterminism). Slot → `ShuffledCorner(slot)` as today. Handle >4 by
refusing spawn with a logged error (MaxPlayers is 4 anyway).
*Note:* a mid-match leaver shifting later joiners' slots is acceptable for the
demo because chickens are spawned once at join — the corner is stamped at spawn
and never recomputed. State this in a comment.

**WS3-2 · Base assignment derives from the corner — no fallbacks.**
In `GameManager.AssignBasesToPlayers` (`GameManager.cs:223-269`): keep the
retry-every-tick loop, but assign a player **only** the base whose
`CornerIndex == chicken.HomeCornerIndex`. Delete
`FindNearestUnownedBaseToPlayer` and `FindUnownedBase` fallbacks (they are the
divergence source). If the chicken hasn't spawned yet, skip and retry next tick
(already the pattern). Log a Warn if the home-corner base is somehow taken —
that's now a real invariant violation, not a case to paper over.

**WS3-3 · Deposit gate can stay as-is** (`ChickenCargo.FindNearestBaseInRange`
already gates on `HomeCornerIndex`); after WS3-2 Owner and corner can no longer
disagree. Add an assert-style Warn in `TryDepositAtNearbyBase` if the base
found is claimed by someone else (`b.IsClaimed && b.Owner != Object.InputAuthority
&& !c.IsBot`) — should never fire; if it does we want the log.

**WS3-4 · Combat attribution via NetworkBehaviourId.**
Change `ChickenCombat.RPC_ApplyDamage(float amount, PlayerRef attacker)` to
`RPC_ApplyDamage(float amount, NetworkBehaviourId attackerId)` where the id is
the attacker's `ChickenCombat.Id` (NetworkBehaviourId is RPC-serializable and
resolvable on any peer via `Runner.TryFindBehaviour`).
Update every caller (grep `RPC_ApplyDamage(`): `PeckAbilitySO`,
`CluckShockAbilitySO`, `RollTrampleAbilitySO`, `ChickenCombat.ReflectDamageTo`.
Then:
- **Reflect** (`ReflectDamageTo`): resolve the attacker behaviour directly via
  `Runner.TryFindBehaviour(attackerId, out ChickenCombat c)` — the
  `attacker.IsRealPlayer` gate and the ActiveCombats scan both disappear.
  **Spine Coat now works against bots.** Guard against reflect ping-pong: pass
  a `bool isReflected` flag (or overload) so a reflected hit is never
  re-reflected.
- **Kill credit** (`CreditKillToAttacker`): resolve the attacker the same way;
  keep the "bots don't earn kills" rule if desired by checking
  `attackerCombat.Object.InputAuthority.IsRealPlayer` — but now it's a policy
  choice, not a technical limitation. Recommend: credit bot kills too (stats
  overlay already shows CPU rows).
- Self-damage guard: compare `attackerId == this.Id` instead of PlayerRefs.

**WS3-5 · Docs.** Update `docs/ARCHITECTURE.md` §"Base ↔ corner ↔ player
pairing" (delete the modulo story, describe join-order slots + corner-only
assignment) and GDD annex 13.3. STATE.md entry.

### Verification

- Solo: Spine Coat vs a hunting bot — bot takes reflected damage + knockback
  (previously nothing).
- Windows multi-client (`tools/run-clients.ps1`, 3 clients): each client owns
  the base at its spawn corner; deposits credit the right base; kill one
  client, rejoin — the rejoined client gets a free corner, not a shared one.
- Match restart in the 3-client session: everyone teleports to their own corner.

**Commits (suggested granularity):**
`Net: corner identity from join-order slots (rejoin-safe)`
`Net: base assignment strictly by home corner — remove fallbacks`
`Net: combat attribution via NetworkBehaviourId — Spine Coat + kill credit work vs bots`

---

## WS4 — Editor wiring backlog (blocks several shipped systems)

Already itemised in STATE.md § "Outstanding before next test session" — kept
there as source of truth. Summary of what is DORMANT until wired:
`ChickenMatchStats` component (stats overlay), `BotController` component +
`_botLoadouts` rows (bots currently ability-less), **AbilityZone prefab +
`PrefabRegistrySO.AbilityZone`** (Feather Trap & Root Egg do nothing but warn),
`ChickenVFX` component.

Most of this is executable by an agent via Unity MCP
(`gameobject-component-add` on the prefab stage via `assets-prefab-open`/`save`,
`assets-create-folder`/`assets-modify` for the zone prefab + registry slot).
The `_botLoadouts` rows need the recommended set from ROADMAP Phase R-Bot BOT-3.
**Do WS4 before judging the slice again** — half the ability pool and the whole
stats overlay are invisible in current builds.

**Acceptance:** solo match shows bot ability casts in the log; Feather Trap /
Root Egg spawn visible zones; match-end overlay shows kills column.

---

## WS5 — Arena visual floor (perceived-quality, cheap)

The bare grey plane + placeholder meshes are doing real damage to how the slice
reads. One day of work: tiled ground material (URP, mobile-safe), darker
desaturated palette per GDD §4, ~a dozen static props via `assetforge`
(crates/fences/hay), light vignette. No gameplay code. Route through
`render-pipeline-artist` / `artist-3d` agents or assetforge skill if agent
routing is back in use; otherwise keep it to URP-Lit materials + static meshes.

---

## Execution order & dependency graph

```
WS0 ──────────────────────────────► (independent, do first, 5 min)
WS1 (assets) ─────────────────────► independent; do before any playtest verdicts
WS2-1..5 (small code) ────────────► independent of each other; WS2-1 pairs with WS1
WS3-1 → WS3-2 → WS3-3 → WS3-4 ───► strictly ordered; requires WS2 landed (merge conflicts)
WS4 (editor wiring) ──────────────► anytime; required before "is it fun now?" retest
WS5 (visuals) ────────────────────► anytime, parallel
FINAL: 3-client Windows test + Pixel 9 coop test (/coop-test) after WS3+WS4
```

**Definition of done for this handoff:** a solo match where (1) the win target
is reachable and gets reached, (2) fights visibly change the standings, (3) all
14 abilities do something observable, (4) no self-roots / instakills — followed
by a 3-client session with correct per-corner ownership and a clean rejoin.
