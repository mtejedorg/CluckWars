# Decisions taken 2026-10-21/22 (Maestro) on the three proposals + earlier answers

Source proposals (read them; they hold the numbers and evidence):
- `docs/proposals/BALANCE_AND_PASSIVES_2026-10.md`
- `docs/proposals/EFFECT_TYPES_AURA_VS_STATIC_AOE.md`
- `docs/proposals/PORTRAIT_AND_LANDSCAPE_SPEC.md`

Legend: **[M]** = Maestro decided or changed it himself (an override or a free-text instruction).
**[R]** = I (Claude) recommended it and Maestro accepted the recommendation unchanged.
Nothing below has been implemented. All code work is still to be planned.

## A. Balance and passives (proposal 1)

| # | Decision | Who |
|---|---|---|
| A1 | Fix the stun/root/slow re-application bug: a stun or root keeps `max(remaining, new)`, a timed slow keeps the strongest factor and the later expiry (GDD 7.4 "strongest wins"). Precondition for the Wing Slam change. | [R] |
| A2 | **Bully (Warrior alternative): keep the steal boost, change the +6 cargo to a symbolic +2.** "Steal boost is very good on its own... we will need real testing for that kind of balance." (Open point I raised: the oracle table shows Warrior cap 16 = 38.77 s / 3 trips, outside the +-0.5 s guard, so Bully needs a declared spec target.) | [M] |
| A3 | **Spoiler: keep +15 until a human playtest.** | [M] (against [R] min(15, banked)) |
| A4 | **Wing Slam: cooldown 8 s, stun 1.3 s** (proposal's WS-A). With Relentless: 6 s. | [M] (against [R] 9 s) |
| A5 | Warrior signature: keep Relentless (x0.75 cooldowns except Peck). Maestro's reminder: every class has exactly two passives, one per subclass (Warrior = Relentless + Bully). Warpath is dropped. | [M] (clarification) |
| A6 | Speedy Featherfoot: ignore pile slow AND collision slow (new pure hook `IgnoresCollisionSlow`, one call site). Needs a human playtest for chase feel. | [R] |
| A7 | Assassin Thief: keep x1.6. Opportunist v2 dropped for now. | [R] |
| A8 | Spec-aware oracle test with declared `SpecSctTargets` (Bully, Hoarder must be declared) + class/corner identity on bot log lines. | [R] |
| A9 | Rewrite GDD 7.3 cooldown tiers as Quick 3-5 / Standard 6-9 / Heavy 10-12 with the Heavy rule. | [R] |
| A10 | Warrior bot `dangerRadius` 4.0 -> 5.5 (bots only, matrix-validated). | [R] |
| A11 | Collision slow: enter 1.2 m, exit 1.3 m hysteresis (cosmetic flicker fix). | [R] |
| A12 | Sequencing: implement the approved items behind the scenario matrix, then ONE human 4-player session before any further magnitudes; delete dead `ChickenController.ToughDamageBonus`. | [R] |

## B. Effect types: Aura vs Static AoE (proposal 2)

Maestro's framing (earlier): ability effects have TYPES, not a per-ability flag: **Aura** (follows its caster) and **Static AoE** (placed once). Smoke Roost stays Static AoE. Future abilities will be auras.

| # | Decision | Who |
|---|---|---|
| B1 | **Visuals as a LOCAL (non-networked) child of the caster on every peer; the effect applied by authority and values propagated.** Maestro's words: "put the visuals as child transform but only apply the effect in the host, and propagate damage values and stuff". Resolved as: the zone NetworkObject stays unparented; ring/VFX are local children excluded from the ChickenVisuals renderer scan; no NetworkTransform. | [M] |
| B2 | Effect authority is **HYBRID**: continuous slows stay victim-evaluated (each victim's own peer tests distance to the caster's replicated position, zero RPC); periodic and one-shot effects are sent by the caster's authority by RPC to each victim, validated on the receiver (sender must be the aura's authority, victim inside radius + latency slack, rate-limited per interval). Shared Mode has no host: a chicken's own peer is the only one allowed to change its state. | [R] |
| B3 | **Effect over time:** three channels per area effect in `AreaEffectSpec`: **OnEnter** (once per chicken entering, AND once for chickens already inside at cast: "it should have effect on the chickens that were in the zone when it is cast"), **WhileInside** (state re-evaluated per tick: slows), **Periodic(interval >= 0.25 s)** per-victim cooldown, amount scaled by the interval, independent of the 32 Hz tick. Maestro: "we need to define a proper strategy for dmg/effect over time". | [M] asked, [R] model |
| B4 | Phase 2: migrate Feather Aura onto the caster-anchored zone and delete the three `[Networked]` aura fields on `ChickenController` (also fixes the self-snail-badge bug). | [R] |
| B5 | Caster dies/removed while an aura is live: the aura despawns next tick. | [R] |
| B6 | Aura hit bearing points at the caster (honest, leaks nothing). | [R] |
| B7 | Consistency: slow-zones and aura use planar XZ distance like root/aim/ring; decoys are ignored by zones consistently. (Conflict to review: Maestro also wants the Doppelganger decoy to become "a perfect copy" in the future.) | [R] |
| B8 | Spine Coat: stays a contact self-buff now; re-express as a 1.2 m steal aura (first Periodic-channel aura) later. | [M] (chose the "later" option) |
| B9 | No named first aura yet: build the model with Slow only; add `ZoneEffect` members when a real aura is designed. | [R] |
| B10 | Round reset: **despawn every live zone, using events** ("You should use events for that"), i.e. an event on round reset that zones/auras subscribe to, not polling; plus a round-start invariant `zones=0 / auras=0`. | [M] (events) |

## C. Portrait + landscape (proposal 3)

Maestro (earlier): support BOTH orientations on phones; spec first.

| # | Decision | Who |
|---|---|---|
| C1 | **Visible-area rule:** "This is not an ultra competitive game. Ensure it looks nice on all devices; area of vision is not as important as the game being fun and playable." Claude's reading: no strict equal-area enforcement; portrait uses a fixed lateral half-width of 5.6 m (capped) so abilities in range stay visible and the HUD/nameplates look right; the proposal's fairness analysis is demoted to a guideline. | [M] (against [R] equal-area) |
| C2 | **Landscape keeps ortho size 5.6** (no zoom-in for wide 20:9 phones). | [M] (against [R]) |
| C3 | No sight-radius veil now (geometry only); revisit after a mixed playtest. | [R] |
| C4 | Lock orientation from GET READY through results; auto-rotate in menus and the waiting room. | [R] |
| C5 | Upside-down portrait off; goal pill moves under the timer in both orientations. | [R] |
| C6 | Status strip becomes a chevron avoid-zone (overrides the earlier "no status strip in safe zones for now", CW-009, because portrait evidence shows an arrow on the SLOW strip); a rival mostly under the top HUD band loses its nameplate and gets an edge arrow at the band's lower edge. | [R] |
| C7 | Gear Up deck may scroll in portrait (only region); Ability Lab forced landscape. | [R] |
| C8 | Settings > Orientation (Auto/Landscape/Portrait) LATER; portrait main-menu cast as a 2x2 grid. | [R] |

## D. Other decisions of the same session (context)

- CW-001: Doppelganger decoy colours stay grey for now; **future goal: the decoy is a perfect copy of its caster** [M].
- CW-006: the chicken must stand up earlier in the countdown (done, CW-021).
- CW-002: scarecrow arm-tip rule relaxed to 3 m (rule done; scene move still needs the Editor).
- CW-218: "You were disconnected" sheet with REJOIN (implemented, PC-tested).
- CW-230: execute RPC sender validation (done); landing branch `landing/shared-mode-fixes-2026-10-21` exists locally.
- CW-053 premise corrected (the three passives were replaced in August 2026; the real problems are Bully, Featherfoot, Spoiler).

## E. Constraints that the code plan must respect

- CLAUDE.md / .claude/CLAUDE.md rules: services behind interfaces, `ILogService` only, self-injection, `HasStateAuthority` gate at the top of every `FixedUpdateNetwork`, cross-authority writes via RPC, `ChickenClass : byte`, UXML/USS only for menus, animation/VFX local (ChangeDetector + Render), SOs use the `SO` suffix, no balance number changes without Maestro's approval (the numbers above ARE his approval for A2, A4; A3 means no change), network-protocol / `[Networked]` layout changes must be flagged.
- Test strategy: `docs/TEST_STRATEGY.md` (a fix needs a rule failing on the old behaviour; mode x round matrix solo/shared2/shared4/hostleave x 3 rounds; `selftest.ps1` green).
- Every build must come from one commit for all peers (SHA parity).

## F. Fable review outcome (docs/proposals/FABLE_REVIEW_AND_CODE_PLAN_2026-10-22.md), Maestro's answers 2026-10-22

| # | Decision | Who |
|---|---|---|
| F1 (Q1) | **Bully = steal x1.4 AND +2 cargo** (Maestro restated: not "+0"). The oracle shows cap 16 = 38.77 s / 3 trips; this is a greedy-router artifact, not a real slowdown of a human. Ship it with a DECLARED spec target in `SpecSctTargets` (reason: router non-monotonicity at caps 16-18) and confirm with a human session. | [M] (against fable's +0) |
| F2 (Q2) | Aura visuals live on the zone object and follow the owner in `Render` (no reparenting under the chicken; chicken-riding particles via the existing local `ChickenVFX` path). | [R] accepted (supersedes B1's "child transform") |
| F3 (Q3) | Periodic channel: spec now, RPC + receiver validation when the first Periodic aura (Spine Coat steal aura) is built (WP5 deferred). | [R] |
| F4 (Q4) | Harden `RPC_ApplyStun` / `RPC_ApplyRoot` / `RPC_ApplyAbilitySlow` with sender validation in WP1 (protocol change, flagged). | [R] |
| F5 (Q5) | Round-reset signal: static `MatchSignals.RoundReset` raised on EVERY peer once per reset (authority inside `ResetWorldForNewRound`; others via a ChangeDetector edge on the replicated GameManager state). | [R] |
| F6 (Q6) | The info-zone rule (rival under the top HUD band -> chevron, no nameplate) applies to landscape and portrait. | [R] |
| F7 (Q7, Q8) | Near clip computed per orientation (landscape stays -6); add `winner deposited=0`, executes landed and stuns landed counters to MATCH SUMMARY. | [R] |
| F8 (Q9, Q10) | Feather Aura migration (WP3/WP4) before the human playtest; open the "Doppelganger fidelity" ticket (CW-232). | [R] |

Effective balance set to implement (WP2): Bully steal x1.4 + cargo +2 (declared spec target ~38.8 s / 3 trips); Wing Slam cooldown 8 s, stun/duration 1.3 s (fallback WS-B 9 s / 1.5 s); Spoiler unchanged (+15); Featherfoot ignores pile and collision slow; Thief unchanged; Warrior bot dangerRadius 5.5; collision slow hysteresis 1.2/1.3 m; GDD 7.3 tiers; spec-aware oracle test; bot log identity.
