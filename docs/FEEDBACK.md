# FEEDBACK.md — Ability & Combat Feedback Design

**Status:** design locked; Stages 1–5 implemented (v0.6), four confirmed defects fixed in
v0.6.1 (casting model, placed-zone whiff, single-target marking, decoy targetability) plus
a richer shape vocabulary. Per-case status is marked in §7 — 25 implemented, 5 partial,
1 deferred. The visual language is specified in
`docs/ART.md` §6.11–§6.13; the player-facing rules in `docs/GDD.md` §10.1–§10.2.
**Problem statement:** abilities currently fire on button *press* with almost no
readable consequence. A player cannot tell whether an ability fired, whether it
landed, on whom, what it did to them, what state they are now in, how long that
state lasts, or why a button refuses to work. In a 4-player free-for-all this is
fatal — the whole game is reading other chickens.

---

## 1. Principles

### 1.1 The three vantage points
Every ability event must be legible from **three** points of view. An effect that
only reads for one of them is incomplete.

| Vantage | Question it must answer |
|---|---|
| **Caster** | Did it fire? Whom did it hit? Did it whiff? What did I gain? |
| **Victim** | What hit me? Who cast it? What can't I do now? For how long? |
| **Bystander** | Who did what to whom, from across the arena? |

### 1.2 The three beats
Every ability resolves as **Telegraph → Impact → Aftermath**. Today only a weak
Impact exists. All three get built.

```
   HOLD                 RELEASE                   AFTER
   ──────────────▶      ──────────▶               ────────────────▶
   TELEGRAPH            IMPACT                    AFTERMATH
   area + targets       hit-confirm + numbers     status + countdown
   (mostly local)       (all peers)               (all peers)
```

### 1.3 Redundant coding
Never rely on colour alone — the arena has 4 player colours already competing for
attention, and Okabe-Ito is chosen for colour-blind safety. Every state is coded
by **at least two** of: colour, *shape*, *motion*, *sound*, *number*.
Concretely: Stun = yellow **+ orbiting stars + frozen pose**; Root = green
**+ static ground shackle + no orbit**; Slow = cyan **+ trailing streaks**.

### 1.4 Glanceable, not readable
World-space carries *spatial* information (where, who, how far). The HUD carries
*personal* information (my cooldowns, my statuses). Nothing mid-fight requires
reading a sentence. Numbers are allowed only as short floating text (`-5`, `+5`,
`2.4s`).

### 1.5 Local by default
Per project convention, VFX/animation are local and derived from replicated
state. Nothing in this design adds an RPC. The only new networked data is one
byte (`ChargingSlot`) plus three input bits — everything else is observed.

### 1.6 Information asymmetry is deliberate
The caster sees the **exact** area and the **exact** target set. Opponents see
only a **wind-up tell** on the caster's body (glow + pose), not the area. That
preserves counterplay ("he's charging something, disengage") without turning the
arena into a solved puzzle.

---

## 2. Beat 1 — Telegraph (hold-to-aim)

**Input model changes from press-to-fire to hold-to-aim, release-to-fire.**

While the ability button is held:

1. **Area preview** — a ground decal at the ability's *true* shape and size, in
   the ability's `AccentColor`. Not a generic circle: the shape must match what
   the ability actually scans (§4).
2. **Target marking** — every chicken currently inside the shape gets a
   **threat overlay**: a pulsing body tint plus a bracket reticle at its feet.
   Three overlay states:
   - **Valid target** — accent colour, solid bracket. Will be hit.
   - **Immune / no-effect** — grey, dashed bracket, small ⃠ glyph. In the area but
     the ability will do nothing (Spine Coat reflect, Turtle Mode, already-stunned
     for a stun, `requireCargo` abilities vs an empty-handed rival).
   - **Self** — never marked. Self-buffs mark the caster's own ring instead.
3. **Aim rotation** — **once the hold passes `TapHoldThresholdSeconds`**, the
   movement stick rotates the aim instead of translating the chicken, for
   directional abilities (cone / forward-offset / capsule / jump). Caster-centred
   shapes (self-circle, aura, single-target) are rotation-invariant and keep normal
   movement even on a long hold. A sub-threshold tap never pays this cost at all —
   that is the "speed" half of the v0.6.1 decision above. The predicate is
   `AbilityBaseSO.IsDirectionalAim`, written as an *exclusion* list so a newly added
   shape is directional by default (a harmless wrong answer) rather than silently
   losing aim-rotate (a broken one).
4. **Caster wind-up tell (all peers)** — a growing emissive glow at the caster's
   feet scaled by hold time, in the accent colour. Visible to everyone.
5. **Live legality** — if the cast becomes illegal mid-hold (caster gets stunned,
   last valid target leaves the area), the preview desaturates to red-grey and
   the button shows the block reason. Releasing then **cancels without burning
   cooldown**.
6. **Cancel gesture** — drag off the button (touch) / press Esc (desktop) cancels.

**Hold cap.** Holding does not charge power, and there is no minimum hold.

**No tap window (v0.6.1, Maestro's call — supersedes the original §2 wording).**
*"I want no window, I want a fire on release. If user holds, there is feedback, if
not there is speed."* Every cast fires on release; `TapHoldThresholdSeconds` is a
**cost/feedback ramp**, not a fire-path branch. Below it: no telegraph, no target
marks, no wind-up glow, **and no aim-rotate movement lock** — full speed. Past it:
all four engage, and the mobility cost is the price of the aim.

The original spec framed the threshold as governing "what gets drawn" only, which
made the movement lock engage on every press — see §7 case 5. It also warned the
threshold had to be clocked off local raw input rather than the replicated hold
bit; that warning assumed a tap had to *wait out* the window before committing,
which this decision removes. A duration is invariant under a constant input delay,
so the charge state is clocked in whole simulation ticks (4 ticks @ 32 Hz = 125 ms).

---

## 3. Beat 2 — Impact

Fired the instant the ability resolves. All peers.

### 3.1 Always
- **Cast flash** — expanding ring at the true radius/shape, accent-tinted
  (extends the existing `AbilityRangeIndicator` flash to non-circular shapes).
- **Cast SFX** + caster animation trigger (both exist).
- **Caster micro-shake** — 0.06 s, tiny. Reserved larger shake for being hit.

### 3.2 On hit (the biggest current gap)
- **Hit-confirm for the caster** — a distinct rising "connect" SFX (different
  from the cast SFX), and a brief white hit-spark at each victim. The caster must
  never have to guess whether it connected.
- **Victim hit flash** — white body flash, 0.12 s.
- **Impact vector** — 3 short motion lines pointing along the knockback
  direction, so the victim reads *where it came from*.
- **Floating combat text** at the victim, rising and fading over 0.9 s:
  - Steal: `-5 🌽` at victim (warm red), `+5 🌽` at thief (green).
  - Control: `STUN 1.5s`, `ROOT 2s`, `SLOW 45%`.
  - Blocked: `BLOCKED` / `IMMUNE` in grey.
- **Directional damage indicator** for the local player — an arc at the screen
  edge pointing at the attacker, fading over 1.2 s. This is the only reliable way
  to learn who hit you from off-screen.

### 3.3 On whiff
Deliberately **not** silent. A muted "swish" SFX plus a grey (not accent) cast
ring. A whiff that looks identical to a hit is the single worst feedback failure.

### 3.4 Kill / execute
Assassin execute and any removal get the loudest treatment: hit-stop (0.08 s
timescale dip), feather burst, kill-feed line, and a distinct stinger.

---

## 4. Ability shape taxonomy

The core architectural piece: **one declarative aim descriptor per ability**,
used by *all four* consumers — preview shape, target highlight, usability gate,
and impact flash. Today `IndicatorRange` half-does this and each ability
re-implements its own `OverlapSphere`, so the preview and the real hit can
disagree. They must be derived from the same source.

```csharp
public enum AbilityAimShape : byte
{
    None          = 0, // self-buff, no area          (Speed Burst, Turtle, Invisibility, Spine Coat, Egg Shell, Doppelganger)
    SelfCircle    = 1, // circle centred on caster    (Cluck Shock, Ambush)
    ForwardCircle = 2, // circle at forward offset    (Feather Trap, Root Egg)
    Aura          = 3, // persistent circle, follows  (Feather Aura)
    Cone          = 4, // forward arc                 (Wing Slam 120°, Peck 140°)
    Jump          = 5, // teleport arc + landing ring (Shadowstep)
    SingleTarget  = 6, // nearest valid in range      (Sneaky Steal, Mark Kill)
    Capsule       = 7, // swept lane along forward    (Roll Push, Flying Peck)
}
```

**`Capsule` (v0.6.1).** Every point within `AimRadius` of the segment running from
the caster to `AimForwardOffset` metres ahead. Both caps are round, so the near cap
reaches slightly *behind* the caster — that is the point of the shape, and why Roll
Push stops whiffing on someone standing on your toes where a `ForwardCircle` at the
same reach leaves a point-blank hole. A zero length degenerates exactly to
`SelfCircle`; a negative offset clamps to zero rather than mirroring backwards. Its
`ShapeCenter` (the decal centre and the distance-sort origin) is the **axis
midpoint**, not the far end.

It reuses `AimForwardOffset` as the axis length rather than adding a new virtual —
so that field now reads as *how far along forward the shape's defining point sits*:
the centre for `ForwardCircle`/`Jump`, the far end of the axis for `Capsule`. Under
both readings, total forward reach is `AimForwardOffset + AimRadius`.

**Ambush no longer fakes a circle with a 360° cone.** `StunBurstAbilitySO`'s base
shape is now an explicit `SelfCircle` and Wing Slam overrides to `Cone` at 120°.
`AbilityAim` keeps the "360° degenerates exactly to `SelfCircle`" net and its test as
defence-in-depth against hand-authored data, but no shipped ability relies on it.

Added to `AbilityBaseSO` as virtuals so no asset re-authoring is needed:

| Member | Meaning |
|---|---|
| `AimShape` | shape enum above |
| `AimRadius` | radius of circle/cone/landing ring |
| `AimForwardOffset` | metres in front of caster for `ForwardCircle` / `Jump` |
| `AimConeAngle` | full arc in degrees for `Cone` |
| `AffectsEnemies` / `AffectsSelf` | who gets marked |
| `WouldAffect(caster, candidate)` | **the** predicate — returns whether this candidate would actually be affected right now, including per-ability conditions (`requireCargo`, immunity, already-stunned) |

`WouldAffect` is **eligibility**; `GatherTargets` is **resolution**. The preview
highlight, `IsUsable`, `AbilityController`'s hit count and each ability's
`OnActivate` all route through the same `GatherTargets` call, so *what you see
marked is exactly what gets hit*. That claim was false until v0.6.1: a
`SingleTarget` ability is eligible against every carrier in range but only ever robs
one, so the telegraph lit three rivals for a cast that took from one. `GatherTargets`
now truncates `SingleTarget` to the nearest eligible candidate, and the telegraph
consumes that same buffer instead of re-deriving marks per-candidate. Any ability
whose `OnActivate` disagrees is a bug, and gets an EditMode test.

Per-ability mapping (**v0.6.1** — changed values marked ⚠ are live balance changes):

| Ability | Shape | Radius | Offset / Angle | Notes |
|---|---|---|---|---|
| Cluck Shock | SelfCircle | 2.5 | — | knockback |
| Ambush | SelfCircle | 2.0 | — | stun; ⚠ was `Cone` @ 360° faking a circle |
| Wing Slam | Cone | 2.5 | 120° | directional, aimable |
| Peck | Cone | ⚠ 2.4 | ⚠ 140° | ⚠ was SelfCircle r2.0; area 12.57 → 7.04 m² (−44%) |
| Sneaky Steal | SingleTarget | 3.0 | — | requires cargo; ⚠ now marks only the one it robs |
| Mark Kill | SingleTarget | 8.0 | — | ⚠ preview now applies isolation + rival rules |
| Roll Push | ⚠ Capsule | ⚠ 1.05 | ⚠ 4.0 | ⚠ was ForwardCircle r1.8 @1.2; area 10.18 → 11.86 m² (+17%) |
| Flying Peck (Roll Trample) | ⚠ Capsule | ⚠ 1.15 | ⚠ 5.0 | ⚠ was ForwardCircle r1.8 @1.6; area 10.18 → 15.66 m² (+54%), max reach 8.4 → 6.15 m |
| Feather Trap | ForwardCircle | 2.0 | 2.5 | placed zone, persists 5 s |
| Root Egg | ForwardCircle | 0.8 | 0 | placed zone, at the caster's feet |
| Feather Aura | Aura | 3.0 | — | follows caster while active |
| Shadowstep | Jump | landing | tier length | ⚠ `AffectsEnemies = false` — pure mobility, was reporting phantom enemy hits |
| Speed Burst, Turtle Mode, Invisibility, Spine Coat, Egg Shell, Doppelganger | None | — | — | self-ring pulse only |

**Decoys are valid targets (v0.6.1, Maestro's call).** *"Make them valid, if not
ability loses its meaning."* `WouldAffect`'s blanket `IsDecoy` exclusion is gone: a
Doppelganger decoy is markable and hittable, so the attacker commits, burns the
cooldown and gets nothing — which is the ability's documented purpose. Only the
*targeting-eligibility* guards were removed; every guard that stops a decoy scoring,
carrying, depositing or being counted as a player stays, so a decoy absorbing a cast
cannot corrupt match state (no kill credit, no score, no food drop, no player count
change). A decoy has no `ChickenCargo`, so a thief's telegraph marks it grey
"no effect" rather than valid — honest, but it does identify the decoy to a thief.

---

## 5. Beat 3 — Aftermath: control status legibility

The control ladder is `Free → Slowed → Rooted → Stunned` (`ControlState.cs`).
Today a ring colour shows *which* state, but nothing shows **how long** or
**what it blocks**. Both get fixed.

### 5.1 On the affected chicken (world space, all peers)
- **Depleting arc** — the existing status ring gains a radial wipe that drains
  over the remaining duration. Time-left becomes readable without a number.
- **Status badge stack** above the nameplate — one small icon per active status
  with a numeric countdown (`⚡1.4s`). Stacks vertically, most severe on top.
- **Shape coding per §1.3** — stun keeps orbiting stars, root gets a static
  ground shackle glyph, slow gets trailing streaks. Distinguishable in greyscale.

### 5.2 On the local player (HUD)
- **Status strip** below the cargo readout: icon + name + draining bar per active
  status.
- **Consequence marking on the controls** — this is the key insight. The status
  must be shown *where it bites*, not only as an abstract badge:

| State | Ability buttons | Move stick | Collection |
|---|---|---|---|
| **Stunned** | all red-crossed, `CanCast` false | greyed | blocked |
| **Rooted** | normal (casting still allowed) | greyed + shackle glyph | blocked |
| **Slowed** | normal | cyan tint + `×0.45` label | allowed |
| **Free** | normal | normal | allowed |

This directly encodes `ControlRules.CanMove / CanCast / CanCollect` in the UI, so
the rules become learnable by playing rather than by reading the GDD.

### 5.3 Screen-edge vignette
A short, subtle coloured vignette pulse when the local player *enters* a control
state — yellow for stun, green for root, cyan for slow. Entering a state must be
felt, not discovered.

---

## 6. Why is my button dead? — refusal feedback

`TryActivate` currently refuses silently in four distinct cases that all look
identical to the player. Each gets its own signal on the button:

| Refusal | Current | New |
|---|---|---|
| On cooldown | radial fill (exists) | + remaining seconds as text |
| No valid target in range | greyed | grey + ⃠ glyph + "no target" pip; the slot's own ambient reach outline sits at its dim ready tier (see note below) |
| Stunned | nothing | red cross over all buttons + shake on press |
| Another ability active | nothing | buttons dim, active ability's button shows a duration ring |
| Slot 2 without Combo | nothing | button not rendered at all |

Plus a **denied-press bump**: pressing a refused button plays a short muted click
and shakes the button 4 px. Never absorb an input silently.

> **The single "world range ring" this table used to reference no longer exists (2026-08-15).**
> `AbilityRangeIndicator` used to draw one coarse circle at the widest ready range-gated
> ability's `IndicatorRange`, brightening when a target entered it. It has been retired in
> favour of `AbilitySlotOverlay`, which draws **every** equipped slot's real aim shape
> permanently, and folds the old brightening in as a per-slot readiness tier
> (suppressed → cooldown → ready → hot, where "hot" means a rival is actually inside *that
> slot's* shape). So the refusal above is now expressed per-slot rather than by one shared
> ring, and two range-gated abilities can light up independently instead of a tie-break
> picking one winner. `AbilityRangeIndicator` still owns the §3.1 cast flash and its stalk.
>
> Note also that **§4's descriptor table is stale for Peck**: `PeckAbilitySO.AimShape` is
> `None`, because Peck targets a *food pile*, not a chicken — its reach lives in
> `FoodPile.IsWithinCollectRange` as a band around each pile's surface. It is therefore not
> caster-centred and the ambient overlay deliberately draws nothing for it.

---

## 7. Complete case inventory

Everything the audit turned up, where it's handled, and **where it actually stands**
as of v0.6 Stages 1–5. Status is marked against shipped code, not against intent.

**Legend** — ✅ implemented · 🟡 partial (visual half shipped, a named piece outstanding)
· ⬜ deferred (no implementation; reason recorded).

| # | Case | Beat | Section | Status | Where it lives / why it isn't done |
|---|---|---|---|---|---|
| 1 | Ability fired at all | Impact | §3.1 | ✅ | `AbilityRangeIndicator` cast flash, now shape-aware via `TelegraphShapes` so a Cone flashes as a cone; `AudioRegistrySO.AbilityActivate`; caster micro-shake in `ChickenVFX`. |
| 2 | Area the ability will cover, before firing | Telegraph | §2.1 | ✅ | `AbilityTelegraph` ground preview at `FeedbackTuning.TelegraphPreviewAlpha`, geometry from `TelegraphShapes` — the same builder the cast flash uses, so preview and impact cannot disagree. |
| 3 | Which chickens will be hit, before firing | Telegraph | §2.2 | ✅ | `TargetHighlight` `Valid` mark — ability accent, solid bracket, pulsing at `ValidTargetPulseHz`. **v0.6.1:** now routed through `GatherTargets` (resolution), not per-candidate `WouldAffect` (eligibility). Before that fix a `SingleTarget` ability marked every carrier in range and robbed one — three highlighted, one robbed. |
| 4 | Chicken in area but immune / no-op | Telegraph | §2.2 | ✅ | `TargetHighlight` `NoEffect` — `NeutralNoEffectColor`, dashed, static, ⃠ glyph. Exactly the gap between `IsInAimShape` and `WouldAffect`. |
| 5 | Aiming a directional ability | Telegraph | §2.3 | ✅ | `ChickenMovement.Tick(..., aimRotateOnly)` — the stick rotates instead of translating while a directional ability is charging; shape predicate is `AbilityBaseSO.IsDirectionalAim`. **v0.6.1 fix:** the lock used to engage on the *first tick the hold bit read true*, so every human tap stuttered the caster. `ChargingSlot` now only goes live past `TapHoldThresholdSeconds`, so a tap keeps full speed. |
| 6 | Opponent is charging something | Telegraph | §2.4 | ✅ | `ControlStateVFX` wind-up foot glow, ramped over `WindupGlowRampSeconds`, observed off `[Networked] AbilityController.ChargingSlot`. No RPC. **v0.6.1:** now a real tell — it previously fired on *every* press, so rivals learned to ignore it. |
| 7 | Cast became illegal mid-hold | Telegraph | §2.5 | ✅ | `AbilityTelegraph.PreviewColor` lerps to `IllegalCastTintColor` over `PreviewIllegalDesaturateSeconds`; releasing cancels without burning cooldown (`AbilityHoldStateMachine` → `ChargeAction.Cancel`). |
| 8 | Cancelling a held ability | Telegraph | §2.6 | ✅ | Touch: drag past `DragCancelDistancePx` → `TouchControlsController.ConsumeAbilityCancelled`. Desktop: `KeyboardInputProvider.GetAbilityCancelPressed` (Esc). Capture-loss counts as cancel, not as fire. |
| 9 | The ability connected | Impact | §3.2 | 🟡 | Visual half shipped — white hit-spark per victim + caster micro-shake in `HitFeedback`, driven off `LastCastHitCount`/`LastCastEventId`. **Outstanding: the distinct rising "connect" SFX.** `AudioRegistrySO` has no `HitConfirm` clip; needs `audio-designer` + a new clip field. |
| 10 | The ability whiffed | Impact | §3.3 | 🟡 | Visual half shipped — grey, dimmed cast ring via `WhiffRingSaturationMultiplier` / `WhiffRingAlphaMultiplier`. **v0.6.1:** the exemption is now `AbilityBaseSO.ReportsCastHits`, covering self-buffs **and placed zones**. Root Egg / Feather Trap resolve their hit *later*, in `AbilityZone`, so they reported 0 targets at cast time and were whiff-styled on **every** cast — Root Egg on open ground, always. **Outstanding: the muted "swish" SFX** (`WhiffSfxVolumeMultiplier` records the intended relative gain; no clip exists). |
| 11 | Who exactly got hit | Impact | §3.2 | ✅ | Per-victim hit flash + spark in `HitFeedback`; the marked target set from case 3 is the same set that resolves. |
| 12 | How much cargo was stolen / lost | Impact | §3.2 | ✅ | `ChickenCargo` → `FloatingCombatText.SpawnCargoDelta`, gated by `FloatingTextCargoDeltaThreshold` so continuous collect/deposit drip never spams popups. `FloatingTextCargoLossColor` / `…GainColor`, sign carried as a second channel. |
| 13 | Which direction I was hit from | Impact | §3.2 | ✅ | `HitFeedback` screen-edge arc, `ScreenEdgeArcWidthDegrees` wide, fading over `ScreenEdgeArcLifetimeSeconds` on `ScreenEdgeArcFadeExponent`. Gated on `HasInputAuthority`. |
| 14 | I got knocked back | Impact | §3.2 | ✅ | Pre-existing knockback extended with the §3.2 impact vector — `ImpactMotionLineCount` lines over `ImpactMotionLineFanDegrees`, plus the new victim shake tier `VictimHitShakeMagnitude`. |
| 15 | A hit was blocked / reflected | Impact | §3.2 | ✅ | `HitFeedback` spawns `IMMUNE` floating text in `NeutralNoEffectColor` — the same grey vocabulary case 4 uses in the telegraph, so "nothing will happen" and "nothing happened" match. |
| 16 | Someone was executed / removed | Impact | §3.4 | 🟡 | Feather burst, death shake (`DeathShakeMagnitude`) and hit-stop (`HitStopDriver`, victim's peer only, `_enableHitStop`) all shipped. **Outstanding: the kill-feed line and the distinct stinger** — the kill feed is a HUD surface nobody has designed yet, the stinger needs a clip. |
| 17 | Which control state I'm in | Aftermath | §5.1, §5.2 | ✅ | World: `ControlStateVFX` ring + `ChickenStatusBadges` stack. HUD: the §5.2 status strip in `TouchControls.uxml`. Both read the same glyphs — `ChickenStatusBadges` aliases `HudFeedbackStyle`'s consts so they cannot drift. |
| 18 | How long the state lasts | Aftermath | §5.1 | 🟡 | **Accepted deferral, by design — see §5.4 below.** Stun and root show real countdowns and draining arcs off `StunTimer` / `RootTimer`. **Slow shows magnitude (`×0.45`), never a countdown**, because `SlowMultiplier` has no deadline to count down to. |
| 19 | What the state blocks | Aftermath | §5.2 | ✅ | Control-consequence marking: ability hexes carry the `Stunned` refusal treatment, the move stick greys for stun/root (`⛓` on root) and cyan-tints with a `×0.45` label on slow. Encodes `ControlRules.CanMove/CanCast/CanCollect` directly. |
| 20 | Entering a control state | Aftermath | §5.3 | ⬜ | **Deferred.** The screen-edge vignette pulse is unimplemented; `VignettePulsePeakAlpha` and `VignettePulseDecaySeconds` exist but nothing reads them. Reason: it needs a full-screen post/overlay pass, and the four cues that *do* ship for entering a state (badge appears, ring starts draining, strip row appears, stick repaints) already cover it — the vignette is punctuation, not information. Cheapest remaining win in the whole system; take it first next pass. |
| 21 | A rival's control state, from range | Aftermath | §5.1 | ✅ | `ChickenStatusBadges` for the exact number up close, `ControlStateVFX`'s ring + per-state shape coding for the approximate read from across the arena. |
| 22 | A placed zone exists and where its edge is | Aftermath | zone edge ring, §4 | ✅ | `AbilityZoneVisuals` draws the ring at exactly `AbilityZone.TriggerRadius`. **v0.6.1:** the drawn edge is now genuinely the trigger — the zone was the last direct-hit path still resolving against *collider bounds* via `Physics.OverlapSphere`, and now iterates `ChickenController.ActiveControllers` with a planar-XZ test like everything else. Zone colour = the colour of the state it inflicts. |
| 22b | A placed zone actually caught someone | Aftermath | §3.2, v0.6.1 | ✅ | **New.** A zone that worked perfectly was also the one ability that never told you so. `AbilityZone.Render()` observes the trigger locally — the replicated `Consumed` false→true edge for root, a derived occupancy edge for slow — and calls `HitFeedback.NotifyZoneTriggered()` on the caster. Plain local call off replicated state, **no RPC**. A `Despawned` fallback covers the case where the zone consumes and despawns inside one rendered frame at the 30 fps Android floor. |
| 23 | A zone is about to expire | Aftermath | zone ring drains | ✅ | Same component; the ring drains against the zone's replicated `LifetimeTimer`, sharing `DrainRingTrackAlphaMultiplier` with the status arc so all three drain rings read as one mechanism. |
| 24 | My ability is on cooldown, and for how long | Refusal | §6 | ✅ | ART §6.6 layer 6 black bottom-up clip (existing) + layer 9 seconds integer. Alpha `HexAlphaCooldown`. |
| 25 | No valid target in range | Refusal | §6 | ✅ | `NeutralNoEffectColor` at `HexAlphaNoTarget` + a USS-drawn ⃠ in layer 9. |
| 26 | Can't cast — I'm stunned | Refusal | §6 | ✅ | `IllegalCastTintColor` wash + a USS-drawn ✕ in `RefusalStunnedCrossColor`, on **every** hex at once, plus the greyed stick. |
| 27 | Another ability already active | Refusal | §6 | ✅ | Cluster dims to `HexAlphaOtherActive`; the running slot alone keeps a full-alpha accent **top-down** drain off `ActiveRemaining01` — deliberately the mirror of the bottom-up black cooldown clip, because the two routinely coexist on the same hex. |
| 28 | Slot unavailable for my class | Refusal | §6 | ✅ | Hex is `display: none` — ART §6.6's slot-3 visibility rule already removes it from the layout, so there is nothing left to mark. |
| 29 | My press was ignored | Refusal | §6 | 🟡 | Visual half shipped — `AbilityController.TryConsumeDeniedPress` drives a decaying bump, `DeniedPressBumpAmplitudePx` over `DeniedPressBumpDurationSeconds` at `DeniedPressBumpOscillations` cycles. **Outstanding: the short muted click SFX.** |
| 30 | My buff is active and expiring | Aftermath | caster self-ring drains | ✅ | `ControlStateVFX` buff ring at `SelfBuffRingRadius` (0.80), deliberately outside the 0.62 status ring so "my buff is running out" and "I am slowed" read as two concentric rings rather than one flickering one. |

**Rollup:** 25 implemented, 5 partial, 1 deferred.

### 7.1 Still unanswered — not changed, awaiting Maestro

`PeckAbilitySO` and `RollTrampleAbilitySO` (Flying Peck) each carry an
`ExtraTargetFilter` requiring the target to be **carrying cargo**, so a cargo-less rival
takes zero knockback from a Peck and is not a valid Flying Peck target at all. The source
documents this as intentional (Stage 1 flagged it as going a step beyond the spec's four
called-out changes), but Maestro has never confirmed it. Left exactly as-is. It is
load-bearing for the telegraph's grey "no effect" mark, so changing it would change what
the preview shows as well as what the ability does.

Four of the five partials (cases 9, 10, 16, 29) are the **same** outstanding item — the
audio pass. `AudioRegistrySO` currently has no hit-confirm, whiff, execute-stinger or
denied-click clip, and adding them is `audio-designer`'s call plus four new clip fields.
Every one of those cases ships its visual half today. Case 16 additionally wants a
kill-feed line, which is an undesigned HUD surface. Case 18 is a design decision, not
debt (§5.4). Case 20 is the only genuinely unstarted item.

### 5.4 Why slow has no countdown (accepted deferral, case 18)

Stun and root each carry a real `[Networked] TickTimer` — `StunTimer` and `RootTimer`
(the latter promoted from a StateAuthority-only `double` in v0.6 precisely so remote
peers could read a countdown) — so "1.4s left" is a fact, and both get a numeric
countdown and a draining arc.

`SlowMultiplier` is different in kind. It is **re-derived from scratch every tick** by
`ChickenController` out of whatever is currently touching the chicken: slow zones,
Feather Aura, food piles, collisions. It is a `Mathf.Min` over live sources with no end
time anywhere in it. Two overlapping slow sources have no single deadline, and a source
you are still standing in has no deadline at all.

So a draining bar on a slow row would be **inventing a fact**. Instead the slow row
shows its **magnitude** — `×0.45` — which is the honest number for that state, and the
one that actually changes the player's decision ("how much slower am I" beats "for how
long", when the answer to the latter is "until you move"). The same choice is made
identically in three places: `ChickenStatusBadges` (world badge), the HUD status strip,
and the move stick's tint label. `HudFeedbackStyle.HasDrainBar` is the single predicate.

This is a deliberate divergence from §5.1's "one small icon per active status **with a
numeric countdown**" and is recorded here rather than silently dropped.

---

## 8. Architecture

Two new components, three extended, one new service. No RPCs added.

**New**
- `AbilityAimDescriptor` (in `AbilityBaseSO`) — §4. Pure data + `WouldAffect`.
- `AbilityTelegraph` (MonoBehaviour on chicken, local player only) — draws the
  aim shape and drives target marking during hold.
- `TargetHighlight` (MonoBehaviour on every chicken) — the threat overlay
  requested by the brief; additive sprite + tint, never touches the body material
  so it cannot fight the hit-flash (same discipline as `ChickenStateOverlays`).
- `FloatingTextService` — pooled world-space text popups (`-5 🌽`, `STUN 1.5s`).
- `HitFeedback` — hit flash, impact lines, hit-confirm SFX, screen-edge
  direction arc.

**Extended**
- `AbilityController` — hold/release activation, `[Networked] byte ChargingSlot`,
  `LastCastHitCount` for whiff-vs-hit, refusal reason enum surfaced to the HUD.
- `ControlStateVFX` — depleting arc + badge stack + per-state shape coding.
- `MatchHudController` / `TouchControlsController` — release-to-fire, refusal
  states, status strip, control-consequence marking.
- `IInputProvider` — `GetAbilityHeld(slot)` alongside the existing pressed API.

**Networked additions (minimal)**
- `AbilityController.ChargingSlot` — 1 byte, drives the opponent-visible wind-up.
- `PlayerNetworkInput` — 3 extra button bits for hold state.
- Status *end times* are already derivable from existing timers; the countdown is
  computed locally, not replicated.

---

## 9. Non-goals for this pass
- No new art assets — everything is procedural (LineRenderer / SpriteRenderer /
  UI Toolkit), consistent with the existing `ControlStateVFX` approach.
- No re-balancing. Radii, durations and cooldowns are read as-is.
- No animation-clip authoring. Wind-up uses scale/emissive, not new clips.

## 10. Mobile budget
All of this must hold 30 fps on a 2021 mid-range Android. Constraints:
- Target marking scans at most 3 rivals, polled at 10 Hz during hold only.
- Floating text is pooled (max 12 live), single dynamic UI mesh.
- Ground decals reuse the existing `LineRenderer` + `Sprites/Default` path — no
  new render feature, no extra camera pass.
