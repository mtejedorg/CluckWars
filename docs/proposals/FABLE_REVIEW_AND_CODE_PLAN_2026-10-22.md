# Independent review of the 2026-10-21 decisions + code plan

**Reviewer:** Claude (Fable 5.1), read-only pass at Maestro's request, 2026-10-22, branch `work/backlog-2026-10-21`
(HEAD `dae5ede`). **Inputs:** `DECISIONS_2026-10-21.md`, the three proposals, CLAUDE.md x2, CONVENTIONS, TEST_STRATEGY,
BACKLOG §9-10 + addenda, and the code named below (every claim about the code was checked against the file). Nothing
was edited, run or committed. Maestro makes every final call; §3 is advice with trade-offs, not overrides.

## Executive summary

1. **A2 (Bully +2 cargo) does not do what it was chosen to do.** Cap 14→16 is the *worst* row of the oracle table: 38.77 s / 3 trips, 3.8 s **slower** than the 35 s target. "+6" made Bully the fastest Warrior; "+2" makes him the slowest. Push back: +0 cargo (Oracle-clean) or an explicitly declared target.
2. **B10 "events" must be raised on every peer, not inside the master's reset.** In Shared Mode a zone's state authority is its caster's peer, and `Runner.Despawn` only works there. The reset already reaches every chicken's authority through `RPC_ResetControlStates`; the event hangs off that. This is also a bug today: a Root Egg (8 s) placed late survives PLAY AGAIN.
3. **B1's "local child of the caster" is unnecessary.** The ring already lives on the zone object (`AbilityZoneVisuals`); an aura zone whose transform follows the owner in `Render` needs no reparenting and no renderer-scan exclusion. Keep the zone unparented, visuals on the zone.
4. **B3's three channels are sound, but only OnEnter and WhileInside have shipped behaviour to re-express** (Root Egg = OnEnter+consume, slows = WhileInside). Periodic has no user until Spine Coat becomes an aura (B8 "later"); build its spec now, its RPC then.
5. **The new caster→victim RPC would be the only validated control RPC.** `RPC_ApplyStun/Root/AbilitySlow` accept any sender today. Harden all of them in one package (CW-230 pattern) or the new rule is theatre.
6. **A4 (8 s / 1.3 s) is viable; with Relentless (6 s) it reaches 21.7 % stun uptime per target**, close to the 25.6 % the proposal flagged as "needs a playtest". Keep WS-B (9 s) as the one-field fallback. CW-230 evidence is unaffected (5 s recent-stun window).
7. **C1/C2 are coherent**: with landscape fixed at 5.6, phone landscape (20:9, 557 m²) and phone portrait (9:20, 560 m²) see about the same ground; only PC 16:9 sees less, which Maestro accepts. The lateral 5.6 m floor stands on its own reason (every in-range target visible, same px/m as landscape). Near clip must become per-orientation or trees slice at s=12.5.
8. **C6 changes landscape too** (rivals under the 330 px top band get a chevron) and reverses `ScreenEdgeProjection`'s documented "full rect decides on/off-screen" contract. Fine, but it is a cross-cutting rule change, not a portrait patch.
9. **B7 (zones ignore decoys) is already true in effect** (`ChickenController.FixedUpdateNetwork` returns at `IsDecoy` before any zone check). It conflicts with the "perfect copy" goal only later; pin it with a test so flipping it is deliberate.
10. **Plan:** two independent lanes (gameplay/network, presentation), 11 packages, about 12 agent-days + one device session; the mode×round matrix gates every package in lane 1.

---

## 1. Code plan

Rule for every package: not done until (a) a new rule fails on the old build and passes on the new one, (b) EditMode unfiltered green, (c) `selftest.ps1` green, (d) lane-1 packages: `scenario-pc.ps1` solo/shared2/shared4/hostleave × 3 rounds = PASS. **qa-reviewer is mandatory for WP0, WP1, WP3, WP4, WP5** (networking). Nothing here lands on `landing/shared-mode-fixes-2026-10-21`; it is a feature branch off the landed state.

| WP | Scope | Agent | Est. | Needs |
|---|---|---|---|---|
| 0 | Round-reset event + zones/auras=0 invariant (B10) | senior-dev + qa-reviewer | 0.5 d | matrix |
| 1 | Control re-application (A1), collision hysteresis (A11), Featherfoot hook (A6), control-RPC sender validation, delete `ToughDamageBonus` (A12) | senior-dev + qa-reviewer | 1 d | matrix |
| 2 | `SpecSctTargets` test (A8), Bully (A2*), Wing Slam (A4), bot radius (A10), bot log identity, GDD §7.3 (A9), perk text, compendium | mechanics-designer (numbers) + senior-dev (test) | 1 d | EditMode, solo/shared4 |
| 3 | Effect-type model, anchored zone, channels OnEnter/WhileInside, World-hash split (B1-B3, B5-B7, B9) | senior-dev + qa-reviewer | 1.5 d | matrix |
| 4 | Feather Aura onto the anchored zone, delete the `ChickenController` triplet (B4) | senior-dev + qa-reviewer | 1 d | matrix + 2-client aura check |
| 5 | Periodic channel + `RPC_ApplyAreaEffect` (B2/B3) — **deferred to the first Periodic aura (Spine Coat, B8)**; spec only now | senior-dev + qa-reviewer | 1 d later | matrix |
| 6 | `LayoutClasses`, orientation policy (C4/C5), `OrientationFraming` + `MatchCamera` (C1-C3) | senior-dev, render-pipeline-artist reviews near/shadow | 1.5 d | Editor Game View captures |
| 7 | Match HUD portrait + info zones + band rule (C5/C6) | senior-dev, ui-designer review | 1.5 d | Editor captures |
| 8 | `UiLayoutProbe` + `capture_orientation.py` + `verify.ps1` gate | tester + senior-dev | 1 d | Editor |
| 9 | Menus portrait (C7/C8), Ability Lab landscape force | ui-designer (USS) + senior-dev (C#) | 2 d | Editor |
| 10 | Device pass (Pixel 10 Pro / Pixel 9), human 4-player session for A2/A4/A6/A3 | tester / coop-test | 0.5 d + session | device, humans |
| 11 | Docs: STATE, ROADMAP, ARCHITECTURE (effect types), ART §6, theme header, site republish | code-architect | 0.25 d | — |

**Lanes.** Lane 1 = WP0→1→2→3→4 (Gameplay/Abilities/Diagnostics files). Lane 2 = WP6→7→8→9 (Visuals/UI/tools). They share no source file except `ChickenNameplate.cs` (WP4 removes the `AuraSlowActive` read at :365; WP7 adds the band rule) and the docs — run WP4 before WP7 or accept one small merge. WP2 can start as soon as WP1 lands; A2 waits for Q1 below. The Editor is needed to run tests, the matrix and captures, not to author: every edit is YAML/C#/USS (`.asset` values, `AreaKind` lines, `ProjectSettings.asset`, `MatchTopBar.uxml`); no scene or prefab edit is required (new `[Networked]` fields on `AbilityZone` need no prefab re-bake). A device is needed only for WP10.

### WP0 — Round reset by event (B10)

*Why first:* `ResetWorldForNewRound` (`GameManager.cs:839-941`) resets combats, cargos, controls, stats and teleports; it never touches `AbilityZone.ActiveZones`. A Root Egg lives 8 s; PLAY AGAIN can be pressed within that. A caster-anchored aura would be carried to the corner. This is a today bug with no rule.

*Design.* New `Assets/_Game/Scripts/Gameplay/MatchSignals.cs`: `public static event Action RoundReset;` + `Raise()`. Raised in two places so every peer sees it exactly once per reset: (1) on the authority inside `ResetWorldForNewRound` before the RPC fan-out; (2) on every other peer from a `ChangeDetector` on `GameManager.StartRound`/`State` in `GameManager.Render` (`:298`), on the replicated `Ended→WaitingForPlayers` edge (`RoundOrdinal`-style pure rule, tested). `AbilityZone` subscribes in `Spawned`, unsubscribes in `Despawned` (pooled: re-arm per spawn), and on the signal sets a local `_resetRequested`; its authority-gated `FixedUpdateNetwork` despawns next tick (never despawn from inside the event: the handler may run inside another object's tick). `Doppelganger` gets the same subscription (today a decoy is excluded from the teleport by `RoundTeleportRules.ShouldTeleport` and simply lingers). Why not raise it only from the master: in Shared Mode only the state authority can despawn, and a joiner's zone has the joiner as authority. Why not poll `GameManager.State` in the zone's FUN: Maestro asked for events, and the edge detector is the same "observed replicated state" pattern as `ChickenCombat.Render`.

*Rules.* `RoundInvariants.CheckRoundStart` (`RoundInvariants.cs:53`) gains `zones` and `auras` counts → failures `zones-zero` / `auras-zero`; `InvariantProbe.CheckStart` supplies them and prints `zn=`/`au=` on the `Invariant ok|FAIL` line; `merge.ps1` adds the round-start rule and `selftest.ps1` a fixture with `zn=1`. Fails on the old build with an `AutoTest` step that places a Root Egg at t=44 s and presses PLAY AGAIN. Interaction with CW-221: the zone despawn and the chicken's pending teleport both apply at the top of their own next FUN; one tick of an aura evaluated at the old spot is harmless because slow sources are rebuilt every tick. Hostleave: a zone whose authority left is either destroyed by Fusion or orphaned, depending on the prefab's `NetworkObject` flags (`Flags: 262145`, not decoded by anyone yet) — qa-reviewer must verify this against Fusion 2's Shared Mode leave semantics before signoff; an orphan with no authority can never be despawned by anyone and would fail `zones-zero` on every peer, which is the point of the rule.

### WP1 — Control correctness (A1, A6, A11, A12) + control RPC hardening

Files: `ChickenController.cs` (`RPC_ApplyAbilitySlow :1227`, `RPC_ApplyStun :1240`, `RPC_ApplyRoot :1254`, `CheckCollisionSlow :1377`, pile-slow branch `:841-844`, `ToughDamageBonus :136`), `PassiveAbilitySO.cs` (new hook), `FeatherfootPassiveSO.cs`, new `Gameplay/ControlReapplyRules.cs` (pure).

- **A1.** `ControlReapplyRules.MergeTimer(remaining, incoming) = max` for stun and root (after `ApplyPassiveControlDuration`; `StunRemaining`/`RootRemaining` exist). Timed slow: `MergeSlow((factor, until), incoming)` → min factor, max until. Note the simplification this encodes: a strong short slow followed by a weak long one keeps the strong factor until the later expiry — slightly stronger than GDD 7.4. Only Dust Kick applies a timed slow today, so a two-slot list is over-engineering; document it in the rule's remarks. Tests: `ControlReapplyRulesTests` (1.5 then 0.35 → 1.5; old code → 0.35).
- **Sender validation.** All three RPCs are `RpcSources.All` with no `RpcInfo`. Add `RpcInfo info = default` and a receiver check modelled on `ExecuteRules`: the sender must be the state authority of a chicken (or zone) whose ability can produce this effect, within range + `RemoteLatency` slack, and the duration/factor within the registry's authored ceilings (`StealRules.MaxSingleSteal` is the precedent for reading bounds off the registry). This is a protocol change (flag in STATE.md). Rule: shared4 + `grep rejected RPC_Apply` = 0 with honest clients (TEST_STRATEGY checklist).
- **A11.** Authority-local `_inContact` with enter 1.2 m / exit 1.3 m (`ContactHysteresis.Update`, pure, tested); the Spine Coat steal-back inside the same loop keeps the 1.2 m test, and the hook below must skip only the `ApplySlow(SlowSource.Collision, …)` line, not the steal-back.
- **A6.** `PassiveAbilitySO.IgnoresCollisionSlow(self)` with its one call site in `CheckCollisionSlow`; `FeatherfootPassiveSO` overrides both; `PerkTextTests` follows the new template.
- **A12.** Delete `ToughDamageBonus` (grep shows no reader).

### WP2 — Balance values and the spec-aware oracle (A2, A4, A8, A9, A10)

- **A8.** New `Balance/SpecSctTargets.cs`: declared `(class, passive, seconds, trips, reason)` entries. New test in `BalanceOracleTests.cs` beside `RealMap_ShippedClassAssets_SatisfyTheSctAxiom (:43)`: for every shipped `PassiveAbilitySO`, build the `OracleChicken` through `ModifyCargoCapacity`, `ModifyDepositRate`, `ModifyCooldown(Peck)` with `self = null` (add a test that every shipped passive tolerates a null `self`, since the hooks are declared pure) and assert within ±0.5 s of the class target or of a declared entry. Expected declarations: Hoarder 25.82 s / 1; Bully per Q1. Break-it-on-purpose: set `BonusCapacity` back to 6 → red.
- **A2.** `Bully.asset` `BonusCapacity` per Q1; `StealMultiplier` stays 1.4. `StealRulesTests` unchanged.
- **A4.** `WingSlam.asset` `Cooldown 12→8`, `StunDuration 1.5→1.3`, `Duration 1.5→1.3` (the asset carries both at :24/:31; `WingSlamAbilitySO` defaults at :21-23 should follow so a fresh asset agrees). `AbilityDescriptionTests` re-pins. Add the executes-per-match and stuns-landed counters to `MATCH SUMMARY` so the WP10 session produces numbers.
- **A10.** `BotTactics.ProfileFor` Warrior `dangerRadius 4.0→5.5`; bot transition lines prefixed with class+corner. Matrix only.
- **A9 + docs.** GDD §7.3 tiers, Slippery/Bulwark perk text (coverage overstated, proposal §1.1), `docs/site/index.html` from the assets (CLAUDE.md rule 6).

### WP3 — Effect-type model and the anchored zone (phase 1)

New: `Abilities/AreaEffectKind.cs` (`byte`: None 0, StaticAoE 1, Aura 2), `Abilities/AreaEffectChannel.cs` (`[Flags] byte`: OnEnter 1, WhileInside 2, Periodic 4), `Abilities/AreaEffectSpec.cs` (readonly struct: Kind, Channels, Effect, Radius, Lifetime, SlowFactor, RootDuration, ForwardOffset, ConsumeOnTrigger, EndsWithAbility, PeriodicInterval ≥ 0.25, PeriodicAmountPerSecond), `Abilities/AreaEffectSpawner.cs` (the one `Runner.Spawn` + stamp block that `FeatherTrapAbilitySO.cs:81-105`, `RootEggAbilitySO`, `SmokeRoostAbilitySO` copy today; stamp order preserved). `AbilityBaseSO`: serialized `AreaKind`, `virtual TryGetAreaEffect`, `PlacesZone => AreaKind != None` (non-virtual; drop the three overrides). Assets: one `AreaKind:` line on FeatherTrap, RootEgg, SmokeRoost (StaticAoE) and FeatherAura (Aura).

`AbilityZone.cs`: `[Networked] ZoneAnchor Anchor` (World 0 / Caster 1), `[Networked] byte SourceSlot`, `[Networked] AreaEffectChannel Channels`, `TryGetCenter(out Vector3)` (World → stamp; Caster → owner transform via `Runner.TryFindBehaviour(OwnerChicken)`, false while unresolved). Every simulation and feedback read of `transform.position` (`:221, :353, :374`) goes through it; `Render` moves a Caster zone's transform to the centre for `AbilityZoneVisuals` (visuals on the zone, nothing parented under the chicken — see §2a). Despawn rules at the top of the authority FUN: lifetime, consumed, `_resetRequested` (WP0), owner unresolved/removed (B5), `EndsWithAbility && !owner.Abilities.IsSlotActive(SourceSlot)` (`AbilityController.cs:125`). Channels: `TryTriggerRoot` becomes the OnEnter scan (per-victim `_entered` set on the authority, cleared per spawn; chickens inside on the first authority tick fire, which is Maestro's "already inside at cast" rule); WhileInside stays victim-evaluated in `ChickenController.CheckAbilityZoneSlow (:1454)` through `TryGetCenter` with the 3D metric kept in phase 1 (B7's planar unification is one line, separately committed, with its own before/after oracle note). Hit bearing for a Caster zone points at the centre = the caster (B6); update the long remark at `AbilityZone.cs:322-349`, which argues the opposite for placed zones and stays true for them.

`InvariantProbe.World (:198-200)`: World zones stay in `zones`/`static`; Caster zones go to `auras=<hash>/<n>` hashed over (OwnerChicken raw id, radius bucket, effect, channels); `merge.ps1` adds the same 2-heartbeat parity rule for `auras` (regex at `:307` is `static`-only today); selftest fixtures: an `auras` mismatch, and a moving aura that must not trip `static`.

Tests (names from the proposal, all still valid): ordinal pins, `AreaKind ⇔ AimShape`, `TryGetAreaEffect` equals authored fields, `SmokeRoost_IsAStaticAoE`, `ZonesIgnoreDecoys` (new, pins B7), `AbilityZonePrefab_HasNoNetworkTransform`, `WorldHash_CasterZones_AreExcludedFromStatic`, `NoSimulationRead_OfZoneTransform` (source scan like `AbilityAimTests`), extend `AbilityWiringGateTests` to every `AreaKind != None`, and `SpawnPlacementTests.EverySpawnedNetworkObjectPrefab_… (:53)` keeps passing through the stamp.

### WP4 — Feather Aura migration (phase 2)

`FeatherAuraAbilitySO.OnActivate` spawns a Caster zone (`EndsWithAbility`, `SourceSlot = ctx.Slot`, lifetime = `Duration` backstop) via the spawner; `OnDeactivate` empty; `CanActivate => ctx.CanSpawnZone`. Delete `AbilityEffectStack.SetAura/RemoveAura/TryGetAura`, `ChickenController.AuraSlowActive/Radius/Factor`, `CheckAuraSlow (:1434)`, `AuraSlowSearchRadius (:137)`, the `SyncAbilityEffects` aura block (`:1276-1285`), and the three visual reads (`ChickenNameplate.cs:365`, `ChickenStateOverlays.cs:350, :376`) — the self-snail-badge bug goes with them. `[Networked]` removal on `ChickenController`: flag as protocol change; SHA parity covers it. Cast feedback: an Aura no longer reports cast-time hits (`ReportsCastHits` false for `AreaKind != None`), the first-entry confirmation in `AbilityZone.Render` takes over. Rules: `NothingReads_AuraSlowActive`; shared2 `AutoTest` step where a remote peer casts Feather Aura and walks into a rival, with the `Invariant` probe sampling the victim's replicated `ControlVfx.Snared` on every peer (state, not logs); bot-cast aura in hostleave must despawn with its bot.

### WP5 — Periodic channel (deferred)

Spec now, code with the first Periodic aura: `ChickenController.RPC_ApplyAreaEffect(NetworkBehaviourId zone, AreaEffectChannel channel, float amount, RpcInfo info)` `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]`; receiver resolves the zone, requires `zone.Object.StateAuthority == info.Source` (or `info.IsInvokeLocal`), victim inside `TryGetCenter` radius + `RemoteLatency` slack, `amount ≤ spec.PeriodicAmountPerSecond × interval` read off the registry, and at most one accepted per `(zone, channel)` per `interval − slack` (a `TickTimer` dictionary keyed by zone id, like `_stealBackCooldowns`). Sender side: per-victim `TickTimer` on the zone authority, independent of the 32 Hz tick because the interval is in seconds. Bots: a bot's chicken and its zone share the master as authority, so bot casts are local invokes.

### WP6 — Orientation classes, policy and framing (C1-C5)

New `UI/LayoutClasses.cs` (pure `Apply(root, rect)` + `Bind`), replacing `MenuUiController.UpdateLayout (:421)` and the ad-hoc block in `MatchOverlaysController (:458-464)`; `MatchHudController` and `TouchControlsController` bind it (note `TouchControlsController.UpdateLayoutMode (:462)` is the device-mode switch, not orientation — keep separate). New `Gameplay/OrientationPolicy.cs`: on `MatchState.Starting` set `Screen.autorotateTo*` to the current orientation only, restore all on leaving `Ended`/results (observed from `GameManager.State` on every peer); `ProjectSettings.asset :63` `allowedAutorotateToPortraitUpsideDown 1→0`. New `Visuals/OrientationFraming.cs` (pure): `s = a ≥ 1 ? 5.6 : min(5.6/a, 12.5)`; focus offset `(fy − 0.5)·4s` along the camera's ground-forward; `nearClip = min(−6, distance − 1.732·s − 6.2)` **per orientation** so landscape keeps −6; `MatchCamera.ApplyCamera (:380)` reads it every frame from `_camera.aspect`; `_orthoSize` stays the landscape value. `MatchCamera.VisibleGround` log line for the C1 capture check. Tests: `OrientationFramingTests` pinning the proposal §2.3 table rows for 16:9, 20:9, 9:16, 9:20, 9:21 (with s = 5.6 for every landscape row now), `LayoutClassesTests`; re-capture `FenceFadeRules.TargetHoleOnScreen` and `ArenaWarmupFraming` fixtures. render-pipeline-artist reviews near clip and the Mobile 30 m shadow distance at s = 12.5 (far clear-band edge ≈ 28 m).

### WP7 — Match HUD portrait + info zones (C5, C6)

`MatchTopBar.uxml` (goal pill into the timer column, both orientations), `MatchTopBar.uss`, `TouchControls.uss`, `MatchOverlays.uss`, `MatchHud.cs` banner position. `HudControlZones.cs`: add `BuildInfoZones` (top band rect; status strip rect only while it has rows — the `Current(cam)` cache key at `:129` must gain that boolean); rewrite the remark at `:23-26` that says the strip is deliberately not a zone. `ScreenEdgeProjection.TryProject`: "off-screen" = outside the clear viewport (screen minus info zones); update its remarks at `:28-34`, which document the opposite. `RivalIndicator.LateUpdate (:311-343)` + `ChickenNameplate`: a rival ≥ 50 % inside an info zone hides its nameplate and gets a chevron at the zone's lower edge. Tests: `Phase8ChunkDRivalArrowTests` extended to the portrait overrides and the info zones; `ScreenEdgeProjection` pure tests for the clear-rect clamp; the USS-pinning tests listed in the proposal §1.8.

### WP8-WP9 — Probe, captures, menus

As the proposal's S3/S4: `Diagnostics/UiLayoutProbe.cs` (dev-only, logs `ui offenders=N`, gated in `verify.ps1`), `tools/ui_capture/capture_orientation.py`, `scenario-pc.ps1` `-Width/-Height` passthrough (today hardcoded at `:37`), the `-cw_safearea` seam shared by `SafeAreaPadding` and `HudControlZones.SafeInsets`; then the menu USS overrides and the Ability Lab landscape force. ui-designer owns USS; senior-dev the C#.

---

## 2. Audit

**(a) B1/B2/B3 together.** Viable on Fusion 2 Shared Mode: the victim-evaluated channel is the shipped Feather Aura/collision model; the caster-authority channel is the shipped Root Egg model (`TryTriggerRoot` runs on the zone authority and sends `RPC_ApplyRoot`). Three findings. (1) "Local child of the caster" buys nothing: `AbilityZoneVisuals` draws the ring from the zone's own transform, so a Render-time follow is enough, and reparenting under the chicken is what triggers the `ChickenVisuals` renderer scan (`:109/:137`) and the decoy/teleport child problems the proposal lists. (2) Shared Mode validation "without a host" works because the victim's own authority is the judge: it can check that `info.Source` is the state authority of the zone named in the RPC, that it is itself inside the zone it can compute locally, and that the amount is inside the registry's ceiling. What it cannot check is whether the zone's authority is honest about its own zone; that is the same trust model CW-230 accepted and documented (`ExecuteRules.cs:99-104`). (3) The OnEnter "already inside at cast" rule is naturally satisfied by a first-tick scan on the zone authority; the one subtlety is that the authority sees remote victims a replication delay late, so a victim "inside at cast" on the caster's screen can be inside a few ticks later on the authority — the slack favours the victim, same direction as every caster-centred effect today.

**(b) B7 vs the perfect-copy decoy.** Already consistent in effect: the decoy's `FixedUpdateNetwork` returns at `IsDecoy (:825)` before `CheckAbilityZoneSlow`, and `IsCaught`/`TryTriggerRoot` skip decoys. The real inconsistency is elsewhere: `CheckCollisionSlow` deliberately lets a decoy slow whoever bumps it, and aimed casts may target a decoy. A perfect copy would need the decoy to *be* slowed/rooted/stunned visibly, which means not early-returning on control states in its FUN and letting zones (and their cast confirmations) treat it as a chicken. That is a later ticket, not a B7 reversal now — but B7 encodes "decoy ≠ chicken" in exactly the place the future goal needs "decoy == chicken", so pin it with `ZonesIgnoreDecoys` and name the ticket in BACKLOG.

**(c) Balance.** A2: see summary 1; the greedy router's cliff at cap 16-18 (38.77 s) also deserves one look in `BalanceOracle.Simulate` — a non-monotonic SCT in cargo is a property of the router, and the [C] figures come from a Python port that must be confirmed by the real `SpecSctTargets` test before any number is final. A4 × Relentless × execute: stun uptime 1.3/6 = 21.7 % per target; Slippery victim 0.78 s; CW-230's rule records "stun seen" on the victim's authority with a 5 s window (`ExecuteRules.cs:64`, `AssassinExecute.WindowDuration`), so a shorter stun does not shrink the execute's legal window, only the moment the victim stands still. A3: 6 of 41 harness wins from 0 deposited; Maestro accepts it until a human session — the only cheap thing to add is the counter so the session yields a number. A6: `IsDecoy` chickens never reach `CheckCollisionSlow`, so no decoy interaction; chase-feel needs humans. A11: pure, cosmetic; the "contact edge" flicker in CW-226 is exactly this.

**(d) C1/C2.** Coherent (summary 7). The implications: the nameplate/HUD rules of the proposal (W1-W3) hold unchanged because the floor keeps 96 px/m on the Pixel; C6's info zones carry more of the fairness argument now that equal-area is dropped (the top band hides up to 15 m of ground). The risks that remain are rendering: near clip (computed, per orientation), shadows (far clear band ≈ 28 m < Mobile 30 m; do not raise), +25 % ground on a 30 fps device (re-measure, CW-229). `ArenaRevealGateTests` already covers 9:16 warm-up framing.

**(e) C6 reverses CW-009/Phase 8 D.** Deliberate and evidenced (an arrow on the SLOW strip). Two consequences the decision does not mention: it changes landscape behaviour (330 px top band) and reverses the on/off-screen contract documented in `ScreenEdgeProjection`; both are comment-rot traps for qa-reviewer's checklist.

**(f) B10.** See WP0. The existing event-shaped mechanisms in the architecture are `ChangeDetector` on replicated state (`ChickenCombat.Render`, `AbilityZone.ObserveRootConsumed`) and per-object reset RPCs; a C# static event raised from both is the smallest addition that reaches every authority. The invariant probe already samples 0.5 s after Active and prints per-chicken rules; zone counts slot in beside `cooldowns-zero`.

**(g) `[Networked]` layout and compat.** WP1 (RPC signatures), WP3 (+3 bytes on `AbilityZone`), WP4 (−3 fields on `ChickenController`), WP5 (new RPC): each is a protocol change; all peers must run one SHA, which TEST_STRATEGY already requires and `merge.ps1` flags. The practical risk is the landing branch: the shared-mode fixes must land first, and this work must not be cherry-picked onto a branch some device still runs an older build of.

---

## 3. Recommendations (push-backs on [M] marked)

1. **[M] A2 — do not ship +2.** Reason: 38.77 s / 3 trips, outside the guard on the slow side; the "symbolic" bonus is the largest single balance move in the set. Options: (a) +0, keep ×1.4 (Oracle-clean, free-space rule means a loaded Bully steals less — the designed rhythm); (b) +6 declared "31 s / 2" (the Bully farms like a Speedy, visible and pinned); (c) +2 declared "38.8 s / 3" (a nerf nobody asked for). Recommend (a).
2. **[M] B1 — drop "child transform".** Visuals stay on the zone object, which follows the owner in `Render`. Same look, no renderer-scan exclusion, no decoy/teleport child cases. Reparent only particle VFX that must ride the bird, through the existing local `ChickenVFX` path.
3. **[M] B3 — ship OnEnter + WhileInside now, Periodic with Spine Coat.** A channel with no user cannot satisfy "a rule fails on the old behaviour"; its receiver validation is the riskiest code in the set and deserves its own package and review.
4. **[M] B10 — events, but raised per peer.** As WP0; the master-only variant would leave joiners' zones alive.
5. **[M] A4 — accept, with the fallback pre-agreed:** if the human session reports "stunned too often", revert to WS-B (9 s) by one field.
6. **[R] Harden the three existing control RPCs in WP1** rather than validating only the new one.
7. **[R] B7 — keep, pinned by test, with a "Doppelganger fidelity" ticket** that lists what a perfect copy changes (control states on the decoy, zones catching it, confirmations).
8. **[R] C1 — compute near clip per orientation**, never change the landscape −6.
9. **[R] A1 — document the timed-slow simplification** (strongest factor to the later expiry) rather than building a multi-slot slow for one ability.
10. **[R] Sequence:** WP0 and WP1 before any balance number; WP3/WP4 after the human session only if the session is scheduled within the week — otherwise run them in parallel, they do not touch balance.

---

## 4. Open questions for Maestro

| # | Question | Recommended default |
|---|---|---|
| Q1 | Bully cargo: +0 / +6 declared / +2 declared (38.77 s)? | +0, steal ×1.4 |
| Q2 | Aura visuals on the zone object (follows in Render) instead of a child of the caster? | Yes |
| Q3 | Periodic channel + `RPC_ApplyAreaEffect` now, or with the Spine Coat aura? | With Spine Coat |
| Q4 | Add sender validation to `RPC_ApplyStun/Root/AbilitySlow` in WP1 (protocol change)? | Yes |
| Q5 | Round-reset signal raised on every peer (edge detector) — accept that design over polling? | Yes |
| Q6 | C6 applies to landscape too (rival under the 330 px top band → chevron)? | Yes, both orientations |
| Q7 | Near clip computed per orientation; landscape untouched? | Yes |
| Q8 | Add "winner deposited = 0" and executes/stuns counters to `MATCH SUMMARY` before the human session? | Yes (tooling only) |
| Q9 | Phase 2 (Feather Aura migration) before or after the human session? | Before: it moves no number |
| Q10 | Open a "Doppelganger fidelity" ticket now so B7 is a known, pinned stopgap? | Yes |
