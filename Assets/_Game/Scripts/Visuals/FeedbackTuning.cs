using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Single source of truth for every timing / magnitude / colour constant used by
    /// the Telegraph → Impact → Aftermath feedback system (<c>docs/FEEDBACK.md</c>).
    /// Four agents implement Stages 2–5 in parallel (hold-to-aim input, telegraph
    /// visuals, impact feedback, aftermath HUD) — this file exists so the finished
    /// system feels like one hand tuned it, not five. Every value below is picked
    /// against the mobile target (2021 mid-range Android, 30 fps, FEEDBACK.md §10)
    /// and against what already ships in <c>MatchCamera.ApplyShake</c>,
    /// <c>ChickenVFX</c>, <c>ControlStateVFX</c> and <c>ChickenStateOverlays</c> —
    /// nothing here should feel like a different game from those.
    ///
    /// This file makes decisions, it does not implement them. Consumers read these
    /// constants; nothing in this file touches gameplay state.
    /// </summary>
    public static class FeedbackTuning
    {
        // ================================================================
        // Telegraph / hold — FEEDBACK.md §2
        // ================================================================

        /// <summary>
        /// The cost/feedback ramp boundary, in seconds: how long an ability button must
        /// stay down before the cast stops being free and starts being <i>aimed</i>.
        /// Consumed by <c>AbilityHoldStateMachine.Decide</c> via
        /// <c>AbilityController.FixedUpdateNetwork</c>.
        ///
        /// <b>This is not a tap window, and nothing waits it out.</b> Maestro's casting
        /// model (v0.6.1): "no window, fire on release — if the user holds, there is
        /// feedback; if not, there is speed." A release always fires immediately, from
        /// either side of this line. What crossing it buys is the aim package —
        /// ground telegraph, target marks, opponent-visible wind-up glow — and what it
        /// costs is the aim-rotate movement lock. Under the threshold you get none of
        /// the four: no draw, no tell, and full movement speed.
        ///
        /// 120 ms sits at the low end of standard press-vs-hold thresholds (100–200 ms
        /// across mobile game UIs). Below ~100 ms an ordinary confident press would trip
        /// the aim package by accident and the movement stutter would feel like a bug;
        /// above ~150 ms a player deliberately reaching for the aim has to consciously
        /// wait for it, which reads as unresponsive telegraphing rather than a fluid
        /// gesture. Neither failure mode is about the cast's latency any more — that is
        /// now constant.
        ///
        /// <b>Measured on the simulation tick, and that is correct here.</b> The previous
        /// revision of this comment warned at length that the threshold had to be clocked
        /// off local raw input and never off the replicated <c>PlayerNetworkInput</c> hold
        /// bit, because Shared Mode's ~50 ms input latency would inflate the felt
        /// threshold to ~170 ms. That reasoning was sound <i>for the model it was written
        /// against</i>, where a tap had to wait out the window before the game would
        /// commit to firing — there, input latency stacked on top of the wait and the
        /// player ate both. It does not survive the new model. Release fires on the tick
        /// the release arrives, with exactly the same latency it always had; the threshold
        /// only measures a <i>duration</i>, and a duration is preserved under a constant
        /// input delay (both the press and the release are shifted by the same amount).
        /// Accumulating <c>Runner.DeltaTime</c> on the state authority is therefore the
        /// right clock, and has the added property that every peer agrees on when the
        /// wind-up tell should have appeared. Jitter still perturbs the measurement by up
        /// to a tick either way, which at 32 Hz is ±31 ms on a 120 ms boundary — and a tick
        /// of slop on "did the glow appear" is invisible, where a tick of slop on "did my
        /// ability fire" would not have been.
        ///
        /// Quantisation note: the accumulator advances in whole 32 Hz ticks, so the real
        /// boundary is the first multiple of 0.03125 that clears this value — 0.125 s
        /// (4 ticks). See the tick arithmetic comment on
        /// <c>AbilityController._pendingHoldSlot</c>.
        /// </summary>
        public const float TapHoldThresholdSeconds = 0.12f;

        /// <summary>
        /// Poll rate for the target-scan during hold (marking valid/immune targets
        /// in the preview shape). Confirmed at the spec's §10 cap of 10 Hz — the
        /// scan is at most 3 rivals via `OverlapSphere`-class checks, which is cheap,
        /// but running it every `Update` on a 2021 mid-range Android competes with
        /// everything else for CPU during the exact moment (ability about to fire)
        /// where a frame drop is most punishing. 10 Hz (100 ms) is fast enough that
        /// a target crossing the boundary reads as instant to a human, since the
        /// preview shape itself (the decal) still redraws every frame — only the
        /// *marking* re-evaluates at 10 Hz.
        /// </summary>
        public const float TargetScanPollHz = 10f;

        /// <summary>
        /// Time for the caster's foot-glow wind-up tell to reach full scale, once
        /// the hold has passed <see cref="TapHoldThresholdSeconds"/> and a preview
        /// has opened. 0.5 s: long enough that bystanders get a real read ("they're
        /// charging something, disengage" — §1.6) before the ability can fire, short
        /// enough that a hold longer than half a second doesn't leave the tell
        /// visibly still growing, which would read as "broken" rather than "fully
        /// charged and waiting." There is no charge mechanic (§2, "holding does not
        /// charge power") so this curve is purely a tell, not a gameplay timer.
        /// </summary>
        public const float WindupGlowRampSeconds = 0.5f;

        /// <summary>Wind-up glow scale at the start of a hold — barely visible, so a
        /// hold that never clears the tap threshold never flashes anything.</summary>
        public const float WindupGlowMinScale = 0.15f;

        /// <summary>Wind-up glow scale once fully charged (visual only, see
        /// <see cref="WindupGlowRampSeconds"/>). 1.0 = full authored scale.</summary>
        public const float WindupGlowMaxScale = 1.0f;

        /// <summary>
        /// Time for the ground-preview and target brackets to fully desaturate to
        /// <see cref="IllegalCastTintColor"/> once a held cast goes illegal mid-hold
        /// (§2.5 — caster gets stunned, last valid target leaves). 0.15 s: matches
        /// the tempo family of the other instant "something changed" cues in this
        /// system (<see cref="VictimHitFlashDurationSeconds"/> at 0.12 s,
        /// <see cref="DeniedPressBumpDurationSeconds"/> at 0.12 s) so a hold going
        /// illegal reads with the same snap as a denied press, not a slow fade that
        /// could be mistaken for the preview just dimming under a cloud shadow.
        /// </summary>
        public const float PreviewIllegalDesaturateSeconds = 0.15f;

        /// <summary>
        /// Radius, in world units, of the "self-ring" drawn at the caster's feet for
        /// an ability with no area at all (<c>AbilityAimShape.None</c> — Speed Burst,
        /// Turtle Mode, Invisibility, …). §2.2 says self-buffs mark the caster's own
        /// ring instead of marking anyone, and §3.1's cast flash needs the same ring
        /// so a self-buff still confirms it fired. Set to exactly
        /// <c>ControlStateVFX.RingRadius</c> (0.62) rather than picked independently:
        /// the status ring already establishes "this circle is the chicken's own
        /// footprint" in the player's vocabulary, so a self-buff ring at a different
        /// radius would read as a second, unrelated thing. Also used as the fallback
        /// radius for any ability that declares a shape but resolves to a
        /// non-positive <c>AimRadius</c>, so the preview degrades to a self-ring
        /// instead of a zero-size line.
        /// ADDED for Stage 3 — the number existed only as a private const in
        /// <c>ControlStateVFX</c>, and three consumers now need it.
        /// </summary>
        public const float SelfRingRadius = 0.62f;

        /// <summary>
        /// Resting alpha for the telegraph's ground-preview outline (§2.1) before the
        /// illegal-cast wash is applied. Sits deliberately between the two alphas
        /// already shipping in <c>AbilityRangeIndicator.UpdateRangeRing</c> (0.60 for
        /// "target in range", 0.16 for "dim / get closer") and
        /// <see cref="ValidTargetBracketAlpha"/> (0.9): during a hold the preview
        /// replaces the persistent range ring, so it must clearly out-read that ring,
        /// while still sitting *below* the per-target brackets so the brackets stay
        /// the foreground layer the eye lands on — the area is context, the marked
        /// targets are the decision.
        /// ADDED for Stage 3 — not derivable from any existing constant.
        /// </summary>
        public const float TelegraphPreviewAlpha = 0.75f;

        // The 130 px radial drag-cancel is gone (Phase 6, A4): kids drift that far by accident. Touch cancel is the
        // visible edge band (HoldCancelRules: arm at 20 dp, disarm at 28 dp from the physical screen edge).

        // ================================================================
        // Impact — FEEDBACK.md §3
        // ================================================================

        /// <summary>
        /// Caster's own micro-shake magnitude on a landed non-Steal cast. Matches
        /// what already ships in <c>ChickenVFX.LateUpdate</c> (do not re-tune this
        /// value independently of that call site) — reproduced here so every new
        /// consumer of the impact feedback stack reads the same number instead of
        /// re-deriving it. See <see cref="VictimHitShakeMagnitude"/> for why the
        /// caster's own shake stays this small.
        /// </summary>
        public const float CasterMicroShakeMagnitude = 0.06f;

        /// <summary>Duration pair for <see cref="CasterMicroShakeMagnitude"/>.</summary>
        public const float CasterMicroShakeDurationSeconds = 0.12f;

        /// <summary>
        /// Caster's own shake on a landed Steal cast — a touch above a plain hit
        /// (<see cref="CasterMicroShakeMagnitude"/>, 0.06) because cargo changes
        /// hands, but kept BELOW the victim's shake (<see cref="VictimHitShakeMagnitude"/>,
        /// 0.12, which the victim gets on cargo loss). Cut from 0.18 / 0.22 s to
        /// 0.08 / 0.15 s by spec 2026-11-06 §4.1: at 0.18 the thief shook harder than
        /// the bird being robbed, so the camera told the wrong player something
        /// happened to them. Read by both <c>ChickenVFX</c> and <c>HitFeedback</c>.
        /// </summary>
        public const float CasterStealShakeMagnitude = 0.08f;

        /// <summary>
        /// Duration pair for <see cref="CasterStealShakeMagnitude"/>. 0.15 s (was 0.22 s),
        /// shorter than the victim's <see cref="VictimHitShakeDurationSeconds"/> (0.25 s)
        /// for the same §4.1 reason.
        /// </summary>
        public const float CasterStealShakeDurationSeconds = 0.15f;

        /// <summary>
        /// NEW tier: the victim's own shake on being hit by any landing ability that
        /// does not remove them outright (control effect, steal, knockback). Today
        /// nothing shakes the victim's camera at all — only the caster gets a punch
        /// (via <see cref="CasterMicroShakeMagnitude"/>) and only full removal gets
        /// a shake (via the 0.35/0.45 death shake already wired in
        /// <c>ChickenCombat.Render</c>). That is the actual "did something just
        /// happen to me?" gap §3.2 is about. Sits deliberately between the two
        /// existing tiers — 2x the caster's plain-hit magnitude, ~2.9x smaller than
        /// death — so all three read as a clear escalation (tap → punch → knockout)
        /// rather than three shakes that blur together. Must be gated on
        /// <c>HasInputAuthority</c> on the victim's own camera, exactly like the
        /// existing caster-side gate, so peers never see or feel each other's
        /// shakes.
        /// </summary>
        public const float VictimHitShakeMagnitude = 0.12f;

        /// <summary>Duration pair for <see cref="VictimHitShakeMagnitude"/>.</summary>
        public const float VictimHitShakeDurationSeconds = 0.25f;

        /// <summary>
        /// Death/removal shake. Already live in <c>ChickenCombat.Render</c>
        /// (0.35 mag / 0.45 s) — reproduced here only so the escalation ratio above
        /// is documented in one place: 0.06 → 0.12 → 0.35 is roughly 2x then 3x,
        /// which is the ratio that keeps caster-punch, victim-hit, and death shakes
        /// distinguishable from each other without any of them individually feeling
        /// disruptive at 30 fps.
        /// </summary>
        public const float DeathShakeMagnitude = 0.35f;

        /// <summary>Duration pair for <see cref="DeathShakeMagnitude"/>.</summary>
        public const float DeathShakeDurationSeconds = 0.45f;

        /// <summary>
        /// Victim hit-flash duration — the white body flash on the instant a hit
        /// lands (§3.2). Confirmed at the spec's 0.12 s: at 30 fps that is ~3.6
        /// frames, enough to register as a distinct flash rather than a single
        /// frame that a dropped frame could eat entirely, short enough to never
        /// blend into the next event in a fast trade.
        /// </summary>
        public const float VictimHitFlashDurationSeconds = 0.12f;

        /// <summary>
        /// Peak white blend factor for the hit flash (0 = no tint, 1 = fully white).
        /// Deliberately not 1.0: a full 3–4 frame stretch at pure white on a chicken
        /// the player is tracking among four player colours (§1.3) is a moment
        /// where their own class/player-identity read vanishes completely. 0.85
        /// still reads unmistakably as "hit" while leaving a sliver of body colour
        /// so a bystander mid-flash doesn't lose track of *who* just got hit.
        /// </summary>
        public const float VictimHitFlashPeakIntensity = 0.85f;

        /// <summary>Motion-line count for the impact vector (§3.2) — fixed at 3 per
        /// spec. Three is enough to read as "a direction," more starts reading as
        /// generic sparkle rather than a vector.</summary>
        public const int ImpactMotionLineCount = 3;

        /// <summary>
        /// Motion-line length in world units. Set relative to the existing status
        /// ring radius (<c>ControlStateVFX.RingRadius</c> = 0.62) rather than in
        /// isolation — 0.9 reads as clearly longer than the chicken's own footprint
        /// ring, so the lines visually "leave" the body instead of sitting inside
        /// it, without extending so far they cross into a crowded 4-player brawl
        /// and get mistaken for someone else's effect.
        /// </summary>
        public const float ImpactMotionLineLength = 0.9f;

        /// <summary>
        /// Half-angle, in degrees, of the fan the impact motion lines are spread across
        /// either side of the incoming bearing (§3.2). ADDED for Stage 4 — the spec fixes
        /// the line *count* and *length* but says nothing about their spread, and three
        /// perfectly parallel lines read as a barcode rather than as a vector. 22 degrees
        /// gives a ~44 degree total fan: wide enough that the three strokes are clearly one
        /// splayed impact rather than three separate effects, narrow enough that the
        /// bearing they encode is still precise enough to point at a specific rival in a
        /// 4-player scrum (the screen-edge arc next to it is 40 degrees wide, so the two
        /// direction cues agree on roughly how precise "that way" means here).
        /// </summary>
        public const float ImpactMotionLineFanDegrees = 22f;

        /// <summary>
        /// Motion-line lifetime. Slightly longer than
        /// <see cref="VictimHitFlashDurationSeconds"/> (0.12 s) so the direction
        /// cue is still on screen for a beat after the flash itself fades — the
        /// flash says "hit," the trailing lines say "from there," and giving the
        /// second beat its own extra 60 ms lets the eye actually use it instead of
        /// both cues disappearing on the same frame.
        /// </summary>
        public const float ImpactMotionLineLifetimeSeconds = 0.18f;

        /// <summary>
        /// How long a named attacker stays claimable by a victim impact, and how long an
        /// impact that played without a direction stays claimable by a late attribution
        /// (<see cref="HitAttribution"/>). Both halves of one hit are observed from
        /// replicated state, but from <i>different</i> objects with different authorities —
        /// the victim's own knockback/control/cargo change versus the attacker's cast event
        /// — so either can be seen first with a few frames of jitter between them.
        /// <para>
        /// 0.25 s is deliberately just over one <see cref="ScreenEdgeArcLifetimeSeconds"/>
        /// fifth and comfortably longer than
        /// <see cref="ImpactMotionLineLifetimeSeconds"/> (0.18 s), so a late attribution can
        /// still light the direction cue while the flash it belongs to is a live memory,
        /// while staying far shorter than any cooldown — a second, unrelated hit cannot
        /// inherit the previous attacker's bearing.
        /// </para>
        /// </summary>
        public const float HitAttributionWindowSeconds = 0.25f;

        /// <summary>
        /// Minimum planar distance between attacker and victim for a bearing to mean
        /// anything (<see cref="HitAttribution.TryBearing"/>). Below this the arrow would be
        /// pointing at a chicken standing inside the victim's own footprint — the direction
        /// is noise, and drawing it would be a guess dressed as information. Set just under
        /// <c>ControlStateVFX.RingRadius</c> (0.62), i.e. "closer than the victim's own
        /// status ring", so contact-range shoves decline rather than spin the arc.
        /// </summary>
        public const float HitAttributionMinSeparation = 0.35f;

        /// <summary>
        /// Total floating-combat-text lifetime. Confirmed at the spec's 0.9 s — long
        /// enough to read a two-character number in a 4-player brawl without
        /// squinting, short enough that in a fast exchange the text from the last
        /// hit is gone before the *next* hit's text needs the same screen space.
        /// </summary>
        public const float FloatingTextLifetimeSeconds = 0.9f;

        /// <summary>
        /// Fraction of <see cref="FloatingTextLifetimeSeconds"/> spent fully opaque
        /// before the fade begins. 35% (≈0.32 s) gives the eye a guaranteed clean
        /// read of the number — per §1.4, numbers are the one form of "read a
        /// sentence" this system allows mid-fight, so they get a genuine hold beat
        /// rather than starting to fade the instant they spawn.
        /// </summary>
        public const float FloatingTextHoldFraction = 0.35f;

        /// <summary>
        /// Remaining fraction of <see cref="FloatingTextLifetimeSeconds"/> spent
        /// fading out while still rising. Complement of
        /// <see cref="FloatingTextHoldFraction"/> — kept as its own named constant
        /// rather than computed inline so a consumer never has to remember it's
        /// <c>1 - Hold</c>.
        /// </summary>
        public const float FloatingTextFadeFraction = 1f - FloatingTextHoldFraction;

        /// <summary>
        /// Total upward travel distance in world units over the text's lifetime.
        /// 1.1 — enough to visibly separate from the chicken's nameplate/status
        /// badge stack above it (§5.1) so the two never overlap mid-animation, not
        /// so much that a rapid multi-hit sequence pushes older numbers off the top
        /// of a tightly-framed mobile viewport.
        /// </summary>
        public const float FloatingTextRiseDistance = 1.1f;

        /// <summary>
        /// Per-peer live cap on floating combat text instances. Confirmed at the
        /// spec's §10 cap of 12 — a single pooled dynamic UI mesh, sized for the
        /// worst case of a 4-player scrum landing several hits within the same
        /// ~0.9 s window without ever needing to grow the pool at runtime.
        /// </summary>
        public const int FloatingTextLivePeerCap = 12;

        /// <summary>
        /// Height above a chicken's pivot at which floating combat text spawns.
        /// ADDED for Stage 4. Deliberately above <c>ChickenNameplate</c>'s 1.70 offset and
        /// well above <c>ChickenWorldBars</c>'s 1.16 cargo bar, so a number never spawns on
        /// top of the name or the cargo readout it is describing. Combined with
        /// <see cref="FloatingTextRiseDistance"/> (1.1) the text travels 2.50 → 3.60, which
        /// starts above the status-badge stack (see <see cref="StatusBadgeStackPillGap"/>)
        /// that sits over the nameplate pill. This is only the <b>floor</b>: a bird with badge rows showing spawns its
        /// pops at <c>max(this, stack top + <see cref="FloatingTextStackClearance"/>)</c>
        /// (<c>FloatingCombatText.PopSpawnHeight</c>), so the 0.30 m rows of batch D0c never sit under a pop.
        /// Back at 2.50 in D0c (it was raised to 2.75 in D0b to clear the rows; now the rows push it up themselves).
        /// </summary>
        public const float FloatingTextSpawnHeight = 2.50f;

        /// <summary>
        /// Clearance, pivot-local vertical metres, between the screen-level top of the shown badge rows
        /// (<c>ChickenStatusBadges.PopClearHeight</c>) and a pop's spawn point. 0.20 (batch H1, was 0.10): a word pop's glyphs
        /// reach ~0.15 m below its MiddleCenter anchor along the billboard, and a world-vertical offset draws only
        /// <c>up.y</c> = cos(camera pitch) = 0.866 per metre on screen, so 0.20 * 0.866 = 0.17 clears the glyph with ~2 cm to spare.
        /// The old 0.10 was paired with a world-Y stack top that ignored that projection and let a two-row entry pop overprint
        /// the top row (guarded by <c>StatusBadgeLayoutTests</c>).
        /// </summary>
        public const float FloatingTextStackClearance = 0.20f;

        /// <summary>
        /// Smallest cargo change, in food units, that earns a floating <c>±N 🌽</c> popup.
        /// ADDED for Stage 4. Cargo is a <c>float</c> that ticks up continuously while
        /// collecting and down continuously while depositing, so a naive "any change"
        /// trigger would fire a popup on literally every frame a chicken stands on a pile.
        /// The fastest continuous rate in the game is the deposit drain
        /// (<c>MatchConfigSO.DepositRatePerSecond</c>, 6/s) which is only ~0.2 per frame at
        /// 30 fps and ~1.2 even at a badly degraded 5 fps; the discrete events this popup
        /// exists for — a Sneaky Steal (~4–5), a Spine Coat steal-back (4), an execute
        /// transfer or a death drop (a whole load) — all clear 1.5 comfortably. That gap is
        /// what makes a single threshold separate "an event happened to my cargo" from
        /// "cargo is doing its normal continuous thing" without needing any new state.
        /// </summary>
        public const float FloatingTextCargoDeltaThreshold = 1.5f;

        /// <summary>
        /// Colour for a cargo *loss* popup (<c>-5 🌽</c> at the victim). ADDED for Stage 4 —
        /// §3.2 specifies "warm red" but no value. Picked as a warm, orange-leaning red
        /// rather than a pure red so it does not collide with the <c>FULL!</c> cargo warning
        /// (1.00, 0.18, 0.08) that already lives on the same chicken — a number reading
        /// "you lost cargo" must not be mistakable for it. (It was also kept clear of P1's
        /// old identity orange; P1 is aqua since Phase 8 C S4, <c>PlayerPalette.P1</c>.)
        /// </summary>
        public static readonly Color FloatingTextCargoLossColor = new Color(0.95f, 0.35f, 0.25f, 1f);

        /// <summary>
        /// Colour for a cargo *gain* popup (<c>+5 🌽</c> at the thief). ADDED for Stage 4.
        /// A bright grass green, matched in hue family to
        /// <see cref="CanonicalRootColor"/>'s deliberate "grass, not teal" choice so it
        /// stays clear of P4 Forest Teal (<c>PlayerPalette.P4</c>) for the same colour-blind-safety reason
        /// documented on that constant. Green-versus-red is carried by the leading
        /// <c>+</c>/<c>-</c> sign as the second channel, per §1.3 — never colour alone.
        /// </summary>
        public static readonly Color FloatingTextCargoGainColor = new Color(0.40f, 0.88f, 0.35f, 1f);

        /// <summary>
        /// Screen-edge directional damage-indicator arc lifetime. Confirmed at the
        /// spec's 1.2 s — deliberately longer than the floating text (0.9 s)
        /// because this is the *only* reliable off-screen "who hit me" cue (§3.2);
        /// giving it the longest hold time in the impact beat means a player who
        /// glances at the HUD a half-second late still catches it.
        /// </summary>
        public const float ScreenEdgeArcLifetimeSeconds = 1.2f;

        /// <summary>
        /// Angular width of the screen-edge direction arc, in degrees. 40° — wide
        /// enough to catch in peripheral vision on a phone screen without hunting
        /// for it, narrow enough that in a 4-player arena the bearing is still
        /// useful information (a 90°+ arc would tell you "roughly that side of the
        /// map" and lose the "which rival" signal the arc exists to give).
        /// </summary>
        public const float ScreenEdgeArcWidthDegrees = 40f;

        /// <summary>
        /// Fade-curve exponent for the screen-edge arc (alpha ∝ (timeLeft/lifetime)^n).
        /// 2.0 = ease-out: the arc stays near full intensity for the first chunk of
        /// its 1.2 s life (so it's unmissable the instant it appears) and then
        /// decays with increasing speed, rather than a linear fade that spends its
        /// last third barely visible but still occupying the exact same screen
        /// space, which reads as "stuck" rather than "fading."
        /// </summary>
        public const float ScreenEdgeArcFadeExponent = 2.0f;

        /// <summary>
        /// How much the whiff cast-ring's saturation is multiplied by, relative to
        /// the ability's accent colour, to produce the "grey ring" §3.3 requires.
        /// 0.15 — nearly fully desaturated but not pure grayscale, so a whiffed
        /// Steal (cyan-ish accent) and a whiffed Root Egg (purple accent) still
        /// carry a faint hue difference for a colour-attentive player, while both
        /// unambiguously read as "not accent colour, therefore not a hit."
        /// </summary>
        public const float WhiffRingSaturationMultiplier = 0.15f;

        /// <summary>
        /// Alpha multiplier applied to the whiff ring versus a hit-confirm ring at
        /// the same radius. 0.55 — dimmer is part of the "quieter" treatment §3.3
        /// asks for (paired with grey), but not so dim it fails on a sun-glared
        /// phone screen; a whiff still needs to be *seen*, just unmistakably not a
        /// hit.
        /// </summary>
        public const float WhiffRingAlphaMultiplier = 0.55f;

        /// <summary>
        /// Suggested relative volume multiplier for the whiff "swish" SFX versus
        /// the normal cast SFX, so "quieter" (§3.3) has a concrete number to hand
        /// to <c>audio-designer</c> rather than a vague adjective. Not enforced
        /// here — final gain lives in the AudioMixer / audio-designer's domain —
        /// but recorded so the visual (dim/grey) and audio (quieter) treatments of
        /// a whiff are calibrated to the same relative "turned down" amount.
        /// </summary>
        public const float WhiffSfxVolumeMultiplier = 0.6f;

        /// <summary>
        /// Kill/execute hit-stop duration. Confirmed at the spec's 0.08 s — see
        /// <see cref="ExecuteHitStopTimeScale"/> for why the *depth* of the dip
        /// matters more than the duration on this project.
        /// </summary>
        public const float ExecuteHitStopDurationSeconds = 0.08f;

        /// <summary>
        /// Time-scale value during the execute hit-stop (1.0 = no effect, 0.0 =
        /// fully frozen). Deliberately NOT a full stop: 0.08 s at a full freeze
        /// (timescale 0) on a 30 fps Android target is only ~2.4 frames, which is
        /// short enough to be misread as a hitch or a dropped frame rather than
        /// intentional juice — the device is already close to frame budget, so a
        /// hard freeze and a real stutter look identical to the player. 0.15
        /// (a heavy slow, not a stop) reads unambiguously as "the game did this on
        /// purpose" while still being a big departure from normal speed for one
        /// frame's worth of clarity. See the risk note in the report: if this is
        /// implemented as a global <c>Time.timeScale</c> write, it must be scoped
        /// carefully in a Shared-Mode session — it is process-global and will also
        /// touch anything else reading <c>Time.deltaTime</c> on this peer (audio
        /// pitch, other local animators), not just the killed chicken.
        /// </summary>
        public const float ExecuteHitStopTimeScale = 0.15f;

        // ================================================================
        // Aftermath — FEEDBACK.md §5
        // ================================================================

        /// <summary>
        /// Update rate for the depleting-arc geometry (the radial wipe added to the
        /// existing status ring, §5.1). The ring's colour pulse
        /// (<c>ControlStateVFX.UpdateStatusRing</c>) stays per-frame since it's a
        /// single Sin() and a colour write, but regenerating the wipe's arc segment
        /// positions is real per-vertex work; 12 Hz is imperceptible as a stepped
        /// drain against typical control durations (1.5–5 s per GDD §6.4 — a 1.5 s
        /// stun still gets ~18 update steps) while cutting that geometry work by
        /// roughly 5x versus doing it every frame at 60 Hz.
        /// </summary>
        public const float StatusArcDrainUpdateHz = 12f;

        /// <summary>
        /// Update rate for the status-badge countdown text (§5.1's badge stack and
        /// §5.2's HUD status strip). Explicitly NOT per-frame — UI Toolkit text
        /// writes trigger layout work, and a countdown showing one decimal
        /// (`1.4s`) doesn't need sub-200ms precision to read as accurate. 5 Hz
        /// keeps the number visibly "counting down" without paying a text-relayout
        /// cost every single frame on Android.
        /// </summary>
        public const float BadgeCountdownTextUpdateHz = 5f;

        /// <summary>
        /// Alpha multiplier applied to a depleting ring's *track* (the full circle that
        /// stays put) relative to its bright draining arc. ADDED for Stage 5a — the §5.1
        /// arc is only readable if "how much is left" out-contrasts "how much there was",
        /// and a shared multiplier keeps the status arc, the case-30 self-buff ring and
        /// the case-23 zone ring reading as one mechanism rather than three. 0.30 is dim
        /// enough that the arc's leading edge is unambiguous at arena distance, bright
        /// enough that the track still communicates the *full* extent (without it, a
        /// nearly-drained ring would read as a tiny unrelated sliver instead of "almost
        /// out").
        /// </summary>
        public const float DrainRingTrackAlphaMultiplier = 0.30f;

        /// <summary>
        /// Radius of the caster's own "my buff is expiring" ring (§5, case 30). ADDED for
        /// Stage 5a. Deliberately NOT <see cref="SelfRingRadius"/> (0.62): a chicken can
        /// be buffed and slowed at the same time, and the §5.1 status ring already owns
        /// 0.62. Sitting the buff ring 0.18 further out makes the two read as two
        /// concentric rings ("my Speed Burst is running out AND I'm slowed") instead of
        /// one ring flickering between two colours.
        /// </summary>
        public const float SelfBuffRingRadius = 0.80f;

        /// <summary>
        /// Fallback world-space height of the *bottom* row of the §5.1 status badge stack, above the chicken's
        /// pivot, used only when the bird has no nameplate. With one, the stack is anchored to the pill
        /// (<c>ChickenNameplate.TryGetPillTopAnchor</c> + <see cref="StatusBadgeStackPillGap"/>), so the local "You"
        /// pill, which is taller than a rival's, is cleared as well. The on-character ladder, bottom to top: cargo bar
        /// 1.16 (<c>ChickenWorldBars</c>) → nameplate 1.70 (<c>ChickenNameplate</c>, a pill centred there) → badge
        /// rows (bottom row centre = pill top + <see cref="StatusBadgeStackPillGap"/>, three rows
        /// <see cref="StatusBadgeRowSpacing"/> apart) → floating combat text, at least <see cref="FloatingTextSpawnHeight"/>
        /// and always above the rows currently shown (<c>ChickenStatusBadges.PopClearHeight</c>; guarded by
        /// <c>StatusBadgeLayoutTests</c>).
        /// </summary>
        public const float StatusBadgeStackBaseHeight = 2.12f;

        /// <summary>Gap between the nameplate pill's top and the centre of the bottom badge row (batch D0b 0.08; D0c 0.16 = half the
        /// 0.36 glyph plus a hair, so the bottom row's glyph sits on or above the pill).</summary>
        public const float StatusBadgeStackPillGap = 0.19f;

        /// <summary>Vertical spacing between status badge rows (batch D0c: 0.15 → 0.26; H7: 0.26 → 0.38, so a row pitch is at
        /// least the 0.36 glyph height and the rows no longer overprint). See <see cref="StatusBadgeStackBaseHeight"/> for the budget this has to fit.</summary>
        public const float StatusBadgeRowSpacing = 0.38f;

        /// <summary>
        /// Maximum simultaneous status badges. Three, because stun / root / slow are
        /// independent bits on <c>ChickenController.ControlFlags</c> and can all be set at
        /// once — the ladder in <c>CurrentControlState</c> picks a single *dominant*
        /// state, but §5.1 asks for "one small icon per active status," not per dominant
        /// state. See <see cref="StatusBadgeStackBaseHeight"/> for why three is also the
        /// hard ceiling the layout can afford.
        /// </summary>
        public const int StatusBadgeMaxRows = 3;

        /// <summary>Height (world metres) of the state glyph sprite on a status badge row (feedback batch C3).
        /// Batch D0b: 0.13 → 0.17 (measured only 10-13 px at 2400x1080); batch D0c: 0.17 → 0.30 (24-28 px); H7: 0.30 → 0.36.
        /// The row text scales with it (<c>ChickenStatusBadges.CharacterSize</c>), row pitch is <see cref="StatusBadgeRowSpacing"/>.</summary>
        public const float StatusBadgeGlyphSize = 0.36f;

        /// <summary>Gap between a badge row's glyph sprite and its text.</summary>
        public const float StatusBadgeGlyphGap = 0.02f;

        /// <summary>
        /// Peak alpha for the screen-edge vignette pulse on entering a control
        /// state (§5.3). 0.35 — strong enough to be felt as a distinct "something
        /// just happened to me" beat, capped well short of anything that would
        /// obscure the arena at the exact moment (just got stunned/rooted/slowed)
        /// the player most needs to see incoming danger.
        /// </summary>
        public const float VignettePulsePeakAlpha = 0.35f;

        /// <summary>
        /// Decay time for the vignette pulse back to zero. 0.4 s — short on
        /// purpose: this is an *entering the state* punctuation mark, not a
        /// sustained indicator for the state's full duration. The depleting arc
        /// (§5.1) and status badge (§5.2) already own "how long is this state,"
        /// so the vignette must get out of the way quickly or it starts competing
        /// with those for the same "how long" question and undercuts them.
        /// </summary>
        public const float VignettePulseDecaySeconds = 0.4f;

        /// <summary>
        /// Denied-press bump shake amplitude, in USS px, for a refused ability
        /// button (§6, "denied-press bump"). The spec's stated 4 px is a desktop-
        /// scale number: per <c>docs/STATE.md</c>'s measured 1 dp ≈ 2.35 USS px on
        /// this canvas, 4 px is only ≈1.7 dp — well under half the smallest
        /// commonly-readable UI motion on a phone screen and very likely invisible
        /// next to a thumb still resting on the button. Raised to 10 px (≈4.25 dp),
        /// which is still a small, tight shake (nowhere near the 116 px / 48 dp
        /// touch-target floor) but should actually register against a moving
        /// thumb. Flagged as a live-session number to eyeball, not a confident
        /// final value — see report.
        /// </summary>
        public const float DeniedPressBumpAmplitudePx = 10f;

        /// <summary>
        /// Denied-press bump duration. 0.12 s — matches the tempo family used for
        /// every other "instant, no ambiguity" cue in this file
        /// (<see cref="VictimHitFlashDurationSeconds"/>,
        /// <see cref="PreviewIllegalDesaturateSeconds"/>): a denied press is
        /// exactly that kind of event, and sharing the duration keeps the whole
        /// feedback system's micro-beats feeling like one vocabulary instead of
        /// five separately-tuned ones.
        /// </summary>
        public const float DeniedPressBumpDurationSeconds = 0.12f;

        /// <summary>
        /// Number of full horizontal oscillations the denied-press bump completes inside
        /// <see cref="DeniedPressBumpDurationSeconds"/>. ADDED for Stage 5 — the spec
        /// fixes the bump's amplitude and duration but not its frequency, and "shake"
        /// without a stated cycle count is undefined (one cycle is a nudge, ten is a
        /// buzz). 3 cycles over 0.12 s is 25 Hz, which at the 30 fps Android floor is
        /// ~1.2 rendered frames per cycle — deliberately at the edge: any faster and the
        /// motion aliases into a smear that reads as a rendering glitch rather than a
        /// deliberate refusal, any slower and 0.12 s only buys one visible left-right
        /// swing, which reads as the button *sliding* rather than being rejected. Paired
        /// with the linear (1 - t) amplitude decay in the HUD so the last cycle is
        /// already almost at rest and the button never appears to snap back.
        /// </summary>
        public const float DeniedPressBumpOscillations = 3f;

        // ---- Ability-hex refusal treatment (FEEDBACK.md §6, ART.md §6.6) ---------
        //
        // The four alphas below are ART §6.6 layers 2–4 (border hex + main fill) for the
        // five AbilityRefusal values. AbilityRefusalRules.Evaluate makes those values
        // mutually exclusive, so exactly one of these applies to a hex at any instant and
        // the whole table can live as a flat set of constants. Cooldown / NoTarget / Ready
        // reproduce values that already shipped inline in TouchControlsController (they
        // are recorded here, not re-tuned, so the Stage-5 table is readable in one place);
        // (A "another ability is running" dim tier existed until Phase 6 chunk 3 retired it.)

        /// <summary>Layer 2–4 alpha for a hex that can fire right now
        /// (<c>AbilityRefusal.None</c>). Fully opaque accent — the baseline every other
        /// tier is read against.</summary>
        public const float HexAlphaReady = 1.00f;

        /// <summary>
        /// Layer 2–4 alpha while the slot is cooling down. 0.45 is what ART §6.6 layer 9
        /// already specifies ("the base to 45% underneath it") and what shipped — kept
        /// because the bottom-up black clip and the centred seconds number are already
        /// carrying this state on two other channels, so the fill only has to stop
        /// competing with them, not announce anything itself.
        /// </summary>
        public const float HexAlphaCooldown = 0.45f;

        /// <summary>
        /// No longer used by the ability hexes (Phase 6, A1: "no target in range" is not a refusal;
        /// the hex rests at <see cref="HexAlphaReady"/> and the in-range pip carries the
        /// "someone is here" cue by its presence). Still the alpha of the greyed, disabled
        /// move stick (rooted / stunned) in <c>TouchControlsController.ApplyJoystickState</c>.
        /// </summary>
        public const float HexAlphaNoTarget = 0.70f;

        /// <summary>
        /// Colour of the layer-9 ✕ drawn across every hex while the caster is stunned
        /// (§6, case 26). ADDED for Stage 5. Deliberately NOT
        /// <see cref="IllegalCastTintColor"/>: that colour is a *wash* — desaturated and
        /// at α 0.80 precisely so it can sit under content — and it is already painting
        /// layers 2–4 of the same hex, so a cross drawn in it would be invisible against
        /// its own background. This is the foreground mark that has to out-read that wash,
        /// so it is a lighter, fully-opaque red. Kept lighter than
        /// <see cref="FloatingTextCargoLossColor"/> (0.95, 0.35, 0.25) as well, since a
        /// player who just lost cargo and a player who just got stunned are two different
        /// stories that can legitimately be on screen in the same second.
        /// </summary>
        public static readonly Color RefusalStunnedCrossColor = new Color(1.00f, 0.46f, 0.40f, 1f);

        // ================================================================
        // Rival indicators — provisional (playtest note 2 of 5, 2026-08-14)
        // ================================================================
        //
        // Maestro: "rivals are hard to keep track of during a match" — an on-ground
        // player-coloured ring under every visible rival, and a chevron at the view edge
        // pointing at every rival that has left the frame. Drawn entirely by
        // RivalIndicator from state that is already replicated; no networking of any kind.
        //
        // THIS FEATURE IS EXPLICITLY PROVISIONAL. Maestro: "will remove later if it
        // doesn't work." RivalIndicatorsEnabled below is the single kill switch — see its
        // own remarks for exactly what flipping it does.

        /// <summary>
        /// <b>The kill switch for the whole rival-indicator feature.</b> Set to false and
        /// every part of it stops existing at runtime: <c>RivalIndicator.Awake</c> disables
        /// itself before building any geometry, so there are no LineRenderers, no
        /// per-frame work, and nothing left on screen — not a hidden object, not an empty
        /// child. Nothing else in the game reads any of the <c>Rival*</c> constants below,
        /// so turning this off is the complete removal short of deleting the files.
        ///
        /// <b>Deliberately <c>static readonly</c>, not <c>const</c>.</b> A <c>const bool</c>
        /// is folded at compile time, which turns every guard that reads it into an
        /// unreachable-code warning (CS0162) in whichever position it is currently set to.
        /// The <c>static readonly Color</c> fields elsewhere in this file establish the
        /// same pattern.
        ///
        /// If the answer after the playtest is "remove it": delete
        /// <c>RivalIndicator.cs</c>, this section, and the component off Chicken.prefab.
        /// <c>ScreenEdgeProjection</c> stays — FEEDBACK.md §3.2 case 13's screen-edge
        /// damage arc is its other consumer.
        /// </summary>
        public static readonly bool RivalIndicatorsEnabled = true;

        /// <summary>
        /// Radius of the player-coloured ground ring under an on-screen rival. Third and
        /// outermost of three concentric rings a single chicken can wear, and sized
        /// against the other two rather than picked in isolation:
        /// <see cref="SelfRingRadius"/> (0.62, the §5.1 control-state ring) inside
        /// <see cref="SelfBuffRingRadius"/> (0.80, the case-30 buff ring) inside this one.
        /// A rival can legitimately be stunned and buffed while you are tracking them, so
        /// all three can be lit at once and must read as three rings rather than as one
        /// ring changing its mind. Chicken-relative, not arena-relative: the arena rescaled
        /// x1.35 on 2026-08-14 and the chickens did not, so this number was correct before
        /// that change and stays correct after it.
        /// </summary>
        public const float RivalRingRadius = 0.98f;

        /// <summary>Ring stroke width (root-local metres; 0.055 until Phase 8 chunk C R3, raised so it reads on a phone).
        /// Still thinner than the identity ring's stroke (<c>GroundRingStyle.IdentityStroke</c>) and at half its alpha — a
        /// persistent "this is a rival" marker under every opponent for the whole match must not out-shout the transient
        /// control states drawn inside it. <see cref="RivalRingRadius"/> is the floor; the live radius follows the body
        /// (<c>GroundRingStyle.RivalRadius</c>).</summary>
        public const float RivalRingWidth = 0.085f;

        /// <summary>
        /// Ring alpha before the rival's own <c>VisualOpacity</c> is multiplied in. 0.5
        /// keeps it clearly a ground decal rather than a solid painted circle; the player
        /// hue (PlayerPalette) is what carries the identity, and hue survives a low alpha far better than
        /// a shape does.
        /// </summary>
        public const float RivalRingAlpha = 0.5f;

        /// <summary>
        /// How far inside the frame the off-screen chevron is drawn, in viewport units.
        /// Note this does <i>not</i> decide who counts as off screen — see
        /// <see cref="ScreenEdgeProjection"/> for why that is judged against the clear
        /// viewport (the frame minus the HUD info zones). It only stops the chevron's own geometry from being half
        /// clipped by the frame it is pinned to, so it is a hair over half the chevron's
        /// size (<see cref="RivalChevronSizeFraction"/>).
        /// </summary>
        public const float RivalChevronViewportMargin = 0.045f;

        /// <summary>
        /// Extra inset on the TOP edge only, on top of
        /// <see cref="RivalChevronViewportMargin"/>. The match HUD owns that strip — the
        /// scoreboard top-left, the timer top-right — and a chevron pinned to y ≈ 0.955
        /// lands underneath them.
        /// </summary>
        /// <remarks>
        /// Not a guess. Measured in a live solo match on 2026-08-15: with the local player
        /// in a corner (the ordinary case at the start of a round) all three rival chevrons
        /// clamped to y = 0.955, and two of them sat under HUD panels — the feature was
        /// invisible in precisely the situation it was built for.
        ///
        /// 0.17 clears the scoreboard's measured height. Applied to the top edge alone via
        /// <see cref="RivalChevronSafeArea"/>: raising the uniform margin instead would push
        /// the side and bottom chevrons far enough inboard to read as floating in the play
        /// field rather than pinned to the frame.
        ///
        /// Since WP7 the top HUD band is an information zone and
        /// <c>ScreenEdgeProjection.ClearBand</c> cuts the band at its lower edge, which is
        /// lower than this inset in both orientations; this remains the cap if it ever is not.
        /// </remarks>
        public const float RivalChevronTopHudInset = 0.17f;

        /// <summary>
        /// The viewport band an off-screen rival chevron may be drawn in — uniform on three
        /// edges, inset further at the top to clear the HUD. Consumed by
        /// <c>ScreenEdgeProjection.TryProject</c>'s <c>Rect</c> overload.
        /// </summary>
        public static readonly Rect RivalChevronSafeArea = Rect.MinMaxRect(
            RivalChevronViewportMargin,
            RivalChevronViewportMargin,
            1f - RivalChevronViewportMargin,
            1f - RivalChevronTopHudInset);

        // ---- Death Mark feedback (MarkPresentationModel, MarkFeedback, MarkHudController, the top bar, the hexes) ---------
        // Maestro's 2026-11 redesign. Every timing below is the model's; the UI controllers only draw its outputs.

        /// <summary>Kill-ready: the hunt marker and the Death Mark hex turn this colour (#FFEB1A).</summary>
        public static readonly Color MarkKillColor = new Color(1f, 0.92f, 0.1f, 1f);
        /// <summary>The marker's disc and the skull's ink (#140A1A).</summary>
        public static readonly Color MarkInk = new Color(0.08f, 0.04f, 0.1f, 1f);
        /// <summary>The victim's toast and clock plates (#B80D1A).</summary>
        public static readonly Color MarkVictimPlateColor = new Color(0.72f, 0.05f, 0.1f, 0.92f);
        /// <summary>The marker while a base wall stands between Assassin and target (#8D8D8D).</summary>
        public static readonly Color MarkWalledColor = new Color(0.553f, 0.553f, 0.553f, 1f);
        /// <summary>HUD cream (#FEF5E0) and HUD ink (#2A1A0C), the lit / KILL hex rim and icon.</summary>
        public static Color MarkCream => FeedbackPalette.Current.CreamColor;
        public static readonly Color MarkHexIconInk = new Color(0.086f, 0.063f, 0.11f, 1f);

        /// <summary>The draw: a highlight steps down the standings for this long (never longer than the mark's arm), one row
        /// per step; under reduced motion the target's row fades in over the shorter time instead.</summary>
        public const float MarkDrawSeconds = 0.6f;
        public const float MarkDrawStepSeconds = 0.1f;
        public const float MarkDrawReducedFadeSeconds = 0.3f;
        /// <summary>At the lock the highlight pops (1 to this and back) over the pop time, then fades out.</summary>
        public const float MarkDrawLockPopSeconds = 0.15f;
        public const float MarkDrawLockPopScale = 1.06f;
        public const float MarkDrawFadeSeconds = 0.25f;

        /// <summary>The hunt marker appears at the lock with a pop from this scale over this long.</summary>
        public const float MarkMarkerPopFrom = 0.85f;
        public const float MarkMarkerPopSeconds = 0.15f;
        /// <summary>KILL: the marker pulses 1 to 1 + this, at this rate (static under reduced motion).</summary>
        public const float MarkMarkerKillPulse = 0.08f;
        public const float MarkMarkerKillPulseHz = 6f;

        /// <summary>An executed chicken's body while it is out (0 = gone; it reappears at its base). Its nameplate keeps the skull.</summary>
        public const float ExecutedOutAlpha = 0f;

        /// <summary>
        /// Floor on the LOCAL player's own faded bird (Hide / Invisibility, Smoke Roost): it renders at
        /// <c>max(VisualOpacity, this)</c> so you never lose your own chicken, and its world tells (rings,
        /// bars, nameplate, badges, particles) draw at the same alpha. Remote peers see the real
        /// <c>VisualOpacity</c> body and no tells at all (spec 2026-11-06 section 3.2 / 4.8, D9). Must stay
        /// above the Hide opacity on the shipped assets (0.2) and below 1, or "hidden" stops reading on your
        /// own screen; <c>HiddenBirdTests</c> pins both.
        /// </summary>
        public const float LocalHiddenOpacityFloor = 0.45f;

        /// <summary>Any ending: every output fades to 0 over this long, then the presentation is clean.</summary>
        public const float MarkEndFadeSeconds = 0.3f;
        /// <summary>The standings skull fades in over this long.</summary>
        public const float MarkSkullFadeInSeconds = 0.15f;

        /// <summary>The victim's MARKED! toast: in (scale from + opacity), hold, out.</summary>
        public const float MarkToastInSeconds = 0.15f;
        public const float MarkToastHoldSeconds = 1.2f;
        public const float MarkToastOutSeconds = 0.15f;
        public const float MarkToastScaleFrom = 0.85f;

        /// <summary>The victim's darkness (dim) fades in over this long and stays the mark's whole life.</summary>
        public const float MarkDimFadeInSeconds = 0.4f;
        /// <summary>Cold (and red) enter at the inner circle's radius R; they leave only after the hunter has been beyond
        /// this many R for the delay, continuously, so the edge never flickers. Fades in / out.</summary>
        public const float MarkColdExitFactor = 1.3f;
        public const float MarkColdExitDelaySeconds = 1.0f;
        public const float MarkColdFadeInSeconds = 0.4f;
        public const float MarkColdFadeOutSeconds = 1.0f;

        /// <summary>Death Mark stings (IAudioService.PlaySFX volume / pitch). DrawTick rises a step per row, capped.</summary>
        public const float MarkDrawTickVolume = 0.8f;
        public const float MarkDrawTickPitch = 1.0f;
        public const float MarkDrawTickPitchStep = 0.05f;
        public const float MarkDrawTickPitchMax = 1.35f;
        public const float MarkLockVolume = 0.9f;
        public const float MarkLockPitch = 1.0f;
        public const float MarkVictimVolume = 0.9f;
        public const float MarkVictimPitch = 0.75f;
        public const float MarkKillReadyVolume = 0.8f;
        public const float MarkKillReadyPitch = 1.30f;
        /// <summary>No KillReady sting within this long of the last one.</summary>
        public const float MarkKillReadyMinInterval = 0.4f;

        /// <summary>
        /// The kill-live beat (the 3 s arm ending, Maestro 2026-11-04: "feedback is critical here"). Stings reuse existing clips:
        /// the victim hears its MARKED! sting again, higher and louder (the danger it already knows, now live); the Assassin and
        /// observers hear the draw's lock sting, higher (the observers' quieter: not their fight).
        /// </summary>
        public const float MarkKillLiveVictimVolume = 1.0f;
        public const float MarkKillLiveVictimPitch = 1.15f;
        public const float MarkKillLiveAssassinVolume = 0.9f;
        public const float MarkKillLiveAssassinPitch = 1.35f;
        public const float MarkKillLiveObserverVolume = 0.45f;
        public const float MarkKillLiveObserverPitch = 1.35f;
        /// <summary>At the arm the victim's clock and the Assassin's marker pop from this scale back to 1 over
        /// <see cref="MarkLivePopSeconds"/>.</summary>
        public const float MarkLivePopFrom = 1.3f;
        public const float MarkLivePopSeconds = 0.2f;
        /// <summary>While armed the victim's clock and the standings skull pulse up to 1 + this, at this rate.</summary>
        public const float MarkLivePulse = 0.12f;
        public const float MarkLivePulseHz = 4f;
        /// <summary>The standings skull's tint once the kill is live (the screen mood's red), and its ink tint before.</summary>
        public static readonly Color MarkSkullLiveColor = new Color(0.9f, 0.04f, 0.04f, 1f);
        public static readonly Color MarkSkullInkColor = new Color(22f / 255f, 16f / 255f, 28f / 255f, 1f);

        /// <summary>Hunt marker geometry, panel px (MarkHud.uss holds the element boxes): ring radius, ink track and colour
        /// arc widths, track alpha, pointer base and how far its tip reaches beyond the ring.</summary>
        public const float MarkRingRadiusPx = 38f;
        public const float MarkRingTrackWidthPx = 12f;
        public const float MarkRingArcWidthPx = 8f;
        public const float MarkRingTrackAlpha = 0.55f;
        public const float MarkPointerBasePx = 30f;
        public const float MarkPointerTipBeyondRingPx = 18f;
        public const float MarkPointerInkPx = 3f;
        /// <summary>Marker box (84 x 102 incl. pointer) and placement: half-box, the clearance from the clear-viewport edge,
        /// the gap above the anchor, the most the marker rises to clear the local "You" plate (inflated by the margin).</summary>
        public const float MarkMarkerHalfPx = 42f;
        public const float MarkMarkerBoxWidthPx = 84f;
        public const float MarkMarkerBoxHeightPx = 102f;
        public const float MarkMarkerEdgeClearancePx = 64f;
        /// <summary>Extra vertical clearance at the clear-viewport edge: a full four-row landscape board hangs ~34 px below the
        /// top information zone (accepted, HudControlZones), and the marker's pointer (56 px from its centre) must not tuck
        /// under it when it points up at a hidden target.</summary>
        public const float MarkMarkerTopBandOverhangPx = 34f;
        public const float MarkMarkerAnchorGapPx = 10f;
        public const float MarkMarkerYouPlateMarginPx = 8f;
        public const float MarkMarkerYouPlateMaxRisePx = 96f;
        /// <summary>Floor, in world metres above the target's pivot, the on-screen marker must clear, plus the class model's head
        /// lift. A target with badge rows showing raises it to the stack top (<c>MarkerPlacement</c> reads
        /// <c>ChickenStatusBadges.CurrentStackTopLocalY</c>).</summary>
        public const float MarkMarkerClearHeight = 2.75f;
        /// <summary>The victim's hunter-skull badge on the edge chevron fallback: this many chevron sizes inward from its centre.</summary>
        public const float MarkHunterBadgeChevronInset = 0.9f;

        /// <summary>The Death Mark hex while KILL shows: size (multiplied into the controller's per-frame scale; 1.15 is the
        /// cap the hex gaps allow), enter pop from / over, idle pulse amplitude and rate (off under reduced motion).</summary>
        public const float MarkHexKillScale = 1.15f;
        public const float MarkHexKillPopFrom = 1.22f;
        public const float MarkHexKillPopSeconds = 0.15f;
        public const float MarkHexKillPulse = 0.03f;
        public const float MarkHexKillPulseHz = 3f;

        // ---- Death Mark screen mood (MarkScreenEffect / MarkScreenFeature / CWMarkMood.shader) ------------------------
        // The victim's whole-screen mood, drawn by one full-screen pass on the base game camera before post-processing,
        // so the HUD (UI overlay camera, Screen Space Overlay canvases, UI Toolkit panels) stays on top and undimmed.
        // Every alpha below is the value at weight 1; the caller's faded weights (dim01 / cold01 / red01) scale them.
        // Colours are colour-buffer values: the project is in Gamma space, so they are plain sRGB.

        /// <summary>DIM: darkness alpha at the screen centre (kept low: nothing opaque covers the centre).</summary>
        public const float MarkMoodDimCentre = 0.18f;
        /// <summary>DIM: darkness alpha at the screen corners.</summary>
        public const float MarkMoodDimEdge = 0.66f;
        /// <summary>Vignette ramp, as a fraction of the centre-to-corner distance: flat centre alpha inside Inner,
        /// smoothstep to the edge alpha at Outer.</summary>
        public const float MarkMoodVignetteInner = 0.22f;
        public const float MarkMoodVignetteOuter = 1.0f;
        /// <summary>DIM: the darkness colour with the Assassin far away (near-black, a hint of violet).</summary>
        public static readonly Color MarkMoodDimColour = new Color(0.03f, 0.015f, 0.05f, 1f);
        /// <summary>COLD: the darkness turns this deep violet as cold01 rises. Kept dark (luma ~0.1) so the edge stays
        /// darkness, not a violet fog: a lighter violet (0.22, 0.09, 0.46) read brighter than plain DIM in the 960x540 proof.</summary>
        public static readonly Color MarkMoodColdColour = new Color(0.13f, 0.05f, 0.28f, 1f);
        /// <summary>COLD: extra darkness alpha added at the edge at cold01 = 1 (the walls close in a little).</summary>
        public const float MarkMoodColdEdgeBoost = 0.08f;
        /// <summary>COLD: share of the scene's colour removed at cold01 = 1 (0 = untouched, 1 = greyscale).</summary>
        public const float MarkMoodColdDesaturation = 0.6f;
        /// <summary>RED: the tint colour, and its alpha at the edge at red01 = 1 (~7%: felt more than seen).</summary>
        public static readonly Color MarkMoodRedColour = new Color(0.9f, 0.04f, 0.04f, 1f);
        public const float MarkMoodRedAmount = 0.07f;
        /// <summary>RED: centre alpha as a share of the edge alpha (it lives inside the darkness, fainter in the middle).</summary>
        public const float MarkMoodRedCentreShare = 0.4f;
        /// <summary>Below this a weight counts as zero: all three under it and no pass is enqueued (zero GPU cost);
        /// cold under it and the cheap blend-only pass is used instead of the colour-reading one.</summary>
        public const float MarkMoodOffEpsilon = 1f / 255f;

        /// <summary>
        /// Chevron size as a fraction of the viewport's height. A fraction rather than a
        /// world-space length on purpose: it makes the chevron a constant share of the
        /// screen on every device and at every <c>MatchCamera._orthoSize</c>, and immune to
        /// arena rescales. 0.07 of the vertical is roughly the size of a chicken at the
        /// current framing — big enough to catch peripherally on a phone, small enough that
        /// three of them at once (the 4-player ceiling) do not crowd the frame.
        /// </summary>
        public const float RivalChevronSizeFraction = 0.07f;

        /// <summary>Chevron stroke width, as a fraction of its own size, so the arrow keeps
        /// its proportions at any framing instead of turning into a hairline when the camera
        /// pulls back.</summary>
        public const float RivalChevronWidthRatio = 0.22f;

        /// <summary>Chevron alpha before the rival's <c>VisualOpacity</c> is multiplied in.
        /// Higher than <see cref="RivalRingAlpha"/> because the chevron has to win against
        /// whatever arena content happens to be behind that edge of the screen, and unlike
        /// the ring it has no world context to sit in.</summary>
        public const float RivalChevronAlpha = 0.85f;

        /// <summary>Width of the chevron's dark ink outline (<c>RivalIndicator.ChevronOutlineColor</c>), as a fraction of the
        /// chevron's size so it scales with the arrow: 0.026 x 96 texels = 2.5 texels, ~2 px at 1080 rows (0.026 x
        /// <see cref="RivalChevronSizeFraction"/> x 1080). Phase 8 decision E1: without it the player-colour fill is
        /// 1.2-2.0:1 against the sunlit grass; the ink edge is what carries the arrow's silhouette (>= 3:1).</summary>
        public const float RivalChevronOutlineRatio = 0.026f;

        /// <summary>Ink outline alpha before the rival's <c>VisualOpacity</c> is multiplied in. Near-opaque, like the
        /// rival nameplate pill it borrows its ink from.</summary>
        public const float RivalChevronOutlineAlpha = 0.95f;

        /// <summary>
        /// How far past the camera's near clip plane the chevron geometry is placed, in
        /// world units. Its only job is to clear the near plane and sit in front of all
        /// arena geometry so an edge indicator is never occluded by the wall it is pointing
        /// past. Not a framing quantity — the chevron's apparent size comes entirely from
        /// <see cref="RivalChevronSizeFraction"/>, which under the orthographic match camera
        /// is independent of depth.
        /// </summary>
        public const float RivalChevronDepthOffset = 0.5f;

        /// <summary>
        /// Phase 8 chunk D (Maestro-approved edge-arrow polish): a rival counts as ON SCREEN while any part of its body
        /// is inside the viewport, and keeps counting as on screen until it is fully this far outside it (viewport
        /// units, 0.02 = 2 % of the frame). The margin only applies to the way OUT, so a rival creeping along the border
        /// does not flicker between "ring" and "arrow" frame to frame. See <c>ScreenEdgeProjection.IsPartlyOnScreen</c>.
        /// </summary>
        public const float RivalOnScreenHysteresis = 0.02f;

        /// <summary>
        /// Radius of the circle that contains the rotated chevron (outline included), as a fraction of the chevron's own
        /// size: the farthest sprite-space point of the arrowhead is a base corner at sqrt(0.42^2 + 0.45^2) = 0.62 from the
        /// pivot (<c>RivalIndicator.ChevronFill</c>), plus the ink outline (~0.03). Used to keep a whole arrow, not just its
        /// centre, out of the HUD control zones (<see cref="HudControlZones"/>).
        /// </summary>
        public const float RivalChevronClearanceRadius = 0.65f;

        /// <summary>
        /// CW-022: minimum centre-to-centre distance between two off-screen rival arrows, in chevron sizes
        /// (<see cref="RivalChevronSizeFraction"/>). 1.2 leaves a visible gap between adjacent arrows. See <c>EdgeArrowSpread</c>.
        /// </summary>
        public const float RivalChevronSpacing = 1.2f;

        // ---- Floor rival arrows: DEV PROTOTYPE (-cw_floorarrows), Maestro 2026-10-22 ---------------------------------
        // "For the arrows, the best and more immersive way is to put them in the outer circle at the floor." A ring on the
        // ground round the LOCAL chicken with one arrow per hidden rival, compared side by side with the edge chevrons, which
        // stay the default. Drawn by FloorRivalArrows; the rules are FloorArrowRules. Nothing reads these in production.

        /// <summary>Preferred ring radius, metres (inside the 5.4 m longest reach, outside every chicken-worn ring).</summary>
        public const float FloorArrowPreferredRadius = 4.5f;

        /// <summary>The ring never exceeds this share of the smallest clear ground extent from the chicken (lateral half-width,
        /// depth up to the top HUD band, depth down, distance to the nearest HUD control), so it stays in the clear viewport and
        /// off the controls: portrait 4.48 m (lateral 5.6 binds), landscape 16:9 4.15 m and 20:9 3.58 m (the 290 px top band binds),
        /// 4:3 touch 3.70 m (the hexes bind). See FloorArrowRules.RingRadius.</summary>
        public const float FloorArrowLateralFraction = 0.8f;

        /// <summary>Floor on the radius (nonsense framings only): well outside the rival ring (~1 m) and the identity ring.</summary>
        public const float FloorArrowMinRadius = 2f;

        /// <summary>Painted arrow length along the bearing, metres of floor (pass 3, Maestro: "painted on the floor"), for a rival
        /// just beyond the ring and far away: nearer is slightly larger. The tail sits on the ring line, the tip points outward.
        /// Natural perspective: no foreshortening compensation (an arrow pointing up the screen looks shorter, like the ring).</summary>
        public const float FloorArrowNearLength = 1.7f;
        public const float FloorArrowFarLength = 1.3f;

        /// <summary>Arrow width / length: 1.45 m wide near, 1.1 m far (pass 4: the 960x540 shot read as thin darts next to the ring, +17 % length / +25 % width).</summary>
        public const float FloorArrowWidthRatio = 0.85f;

        /// <summary>How far the chevron's tail notch reaches toward the tip, as a share of the length.</summary>
        public const float FloorArrowNotch = 0.25f;

        /// <summary>Paint alpha of the arrow fill (the grass shows through), times the rival's VisualOpacity.</summary>
        public const float FloorArrowPaintAlpha = 0.85f;

        /// <summary>Soft inner fade: the fill's core is this share of <see cref="FloorArrowPaintAlpha"/>, the fill's edge full,
        /// over <see cref="FloorArrowFadeInset"/> (share of the length) - a worn-paint look, no glow.</summary>
        public const float FloorArrowCoreAlphaShare = 0.7f;
        public const float FloorArrowFadeInset = 0.06f;

        /// <summary>The thin dark rim: width as a share of the length (~8 cm at 1.45 m), the tint's own hue at this share of its
        /// value, at this alpha.</summary>
        public const float FloorArrowOutlineRatio = 0.055f;
        public const float FloorArrowOutlineValue = 0.3f;
        public const float FloorArrowOutlineAlpha = 0.9f;

        /// <summary>Planar distance (metres) at which an arrow reaches its far length; ~half the 51.3 m arena.</summary>
        public const float FloorArrowFarDistance = 24f;

        /// <summary>Gap between two arrows' tails along the ring ARC, in near-arrow widths: arrows are spread so their tails
        /// (the widest part, lying on the ring) never touch.</summary>
        public const float FloorArrowSpacing = 1.15f;

        /// <summary>The painted ring: stroke (metres, plus a 2 cm soft edge either side), alpha and a soft cream, the arrows'
        /// own paint family. Drawn only while at least one arrow shows (pass 3 call: a permanent ring under the player is one
        /// more always-on shape next to the reach outlines; shown, it means "rivals out there").</summary>
        public const float FloorArrowRingWidth = 0.1f;
        public const float FloorArrowRingSoftEdge = 0.02f;
        public const float FloorArrowRingAlpha = 0.62f;
        public static readonly Color FloorArrowRingColor = new Color(1.00f, 0.96f, 0.86f, 1f);

        // ================================================================
        // Always-on ability reach overlay — AbilitySlotOverlay
        // ================================================================
        //
        // Maestro: a new player cannot tell what any ability reaches until they are already
        // aiming it. AbilitySlotOverlay draws the ground outline of EVERY equipped slot at
        // once, all match, as a persistent low-fidelity reference. It is additive to the
        // press-to-aim telegraph (FEEDBACK.md §2), which is unchanged.
        //
        // The governing constraint for every number below: this is on screen 100% of the
        // match, on up to four shapes at once, for the whole match. Anything that competes
        // with a transient cue wins by DURATION rather than by design intent, so the ambient
        // tier is deliberately the quietest thing in the feedback system. Only alpha encodes
        // state; the stroke width never changes. The grammar the rest of this file already
        // uses is thickness = "how sharp/immediate", alpha = "how ready".

        /// <summary>
        /// <b>The kill switch for the whole always-on reach overlay.</b> Set to false and
        /// <c>AbilitySlotOverlay.Awake</c> disables itself before allocating anything — no
        /// LineRenderers, no per-frame work, nothing left on screen. Same tier and same
        /// contract as <see cref="RivalIndicatorsEnabled"/>.
        ///
        /// <b>Deliberately <c>static readonly</c>, not <c>const</c>.</b> A <c>const bool</c>
        /// folds at compile time and turns the guard that reads it into a CS0162
        /// unreachable-code warning — see <see cref="RivalIndicatorsEnabled"/>'s own remarks.
        ///
        /// This is the DEVELOPER switch. The player's own runtime choice is a separate gate,
        /// <c>CluckWars.Settings.PlayerPreferences.AbilityRangeGuidesEnabled</c>; both are
        /// honoured independently and neither implies the other.
        /// </summary>
        public static readonly bool AbilitySlotOverlayEnabled = true;

        /// <summary>
        /// Stroke width of an ambient slot outline. Phase 6 (A2) raised it 0.035 -> 0.06: at
        /// 0.035 the guide was a hairline that vanished on grass at phone size. It is still
        /// thinner than the telegraph / cast-flash outlines
        /// (<see cref="TelegraphShapeLineWidth"/>, 0.11), so an always-on reference can never
        /// out-weigh a live aim. Flat across every alpha tier: width is not a state channel
        /// here, only alpha is.
        /// </summary>
        public const float AmbientSlotOutlineWidth = 0.06f;

        /// <summary>
        /// Stroke width of the live aim preview and the impact flash outline. Lives here (it was a
        /// private const in <c>AbilityTelegraph</c>) so the ambient guide's "thinner than the
        /// preview" relationship is checkable against the real number.
        /// </summary>
        public const float TelegraphShapeLineWidth = 0.11f;

        /// <summary>The ink under-stroke beneath every ambient guide is this many times wider than the
        /// coloured line, so a thin tinted line keeps a dark edge on light grass (Phase 6, A2).</summary>
        public const float AmbientSlotOutlineInkWidthMultiplier = 2f;

        /// <summary>Alpha of the ink under-stroke. Constant across the readiness tiers: it is an edge, not a
        /// state, and scaling it with the tier would make a cooling guide's edge vanish with its colour.</summary>
        public const float AmbientSlotOutlineInkAlpha = 0.35f;

        /// <summary>The ink colour shared by the ground guides' under-stroke and the HUD's hex rim
        /// (#2a1a0c, the menu's "Ink").</summary>
        public static Color InkColor => FeedbackPalette.Current.InkColor;

        /// <summary>
        /// Alpha for a ready slot with nothing in its shape — the resting tier. Phase 6 (A2)
        /// raised it 0.22 -> 0.45 together with the width: the guides are how a new player learns
        /// what a move covers, and at 0.22 they sat below the noise floor of the arena floor.
        /// Still well under <see cref="TelegraphPreviewAlpha"/>.
        /// </summary>
        public const float AmbientSlotOutlineAlphaReady = 0.45f;

        /// <summary>
        /// Alpha for a slot on cooldown (0.18). ~40% of <see cref="AmbientSlotOutlineAlphaReady"/>,
        /// mirroring the <i>proportional</i> relationship <see cref="HexAlphaCooldown"/> (0.45)
        /// bears to <see cref="HexAlphaReady"/> (1.00) on the HUD hex, so the world and the
        /// HUD tell the same story about the same slot.
        ///
        /// It recedes; it does not vanish. Mid-fight is the worst possible moment to lose the
        /// spatial reference for an ability you are waiting on.
        /// </summary>
        public const float AmbientSlotOutlineAlphaCooldown = 0.18f;

        /// <summary>
        /// Alpha for a ready slot that currently has a rival standing inside its shape — the
        /// brightened tier, which folds in the job the retired range ring used to do. 0.70, and
        /// still just under <see cref="TelegraphPreviewAlpha"/> (0.75) so an ambient outline can
        /// never be mistaken for a live aim preview (the width gap, 0.06 vs 0.11, does the rest).
        /// At most one or two slots are hot at once, so the brightest shape on screen is almost
        /// always the most decision-relevant one.
        /// </summary>
        public const float AmbientSlotOutlineAlphaHot = 0.70f;

        /// <summary>
        /// Alpha for the three NON-charging slots while one slot is being aimed. Holding them
        /// steady would read as "everything went on cooldown at once", which is the wrong
        /// story. Kept below <see cref="AmbientSlotOutlineAlphaCooldown"/> so "suppressed by an
        /// active aim" never reads louder than "on cooldown" (ordering is test-pinned:
        /// suppressed &lt; cooldown &lt; ready &lt; hot).
        /// </summary>
        public const float AmbientSlotOutlineAlphaSuppressed = 0.12f;

        // ================================================================
        // Colours — canonical control-state set
        // ================================================================
        //
        // Since B1 (spec D3a) the stun / slow / knock / no-effect / ink / cream values live in
        // Assets/_Game/Data/FeedbackPalette.asset (FeedbackPaletteSO); the properties here read
        // FeedbackPalette.Current so every existing reader keeps its name.
        //
        // ControlStateVFX.cs and ChickenStateOverlays.cs currently define their own,
        // slightly different RGB values for the same two states (see the report for
        // the exact numbers). Both are correct on *hue* (root = green, slow = cyan,
        // per FEEDBACK.md §1.3's worked example) but disagree on the exact shade,
        // which means the status ring and the ground blob for the same chicken in
        // the same state don't quite match. These three are now canonical — every
        // consumer of stun/root/slow colour should read from here.
        //
        // Both existing greens/blues were also checked against the four player-
        // identity colours (docs/ART.md "Player Identity Colors", Okabe-Ito derived):
        // P2 Ocean Blue and P4 Forest Teal (UI/PlayerPalette). A raw Okabe-Ito palette
        // pull for "root = bluish-green" would have landed on #009E73 — almost
        // exactly P4's Forest Teal, which is the one collision this system cannot
        // afford (a rooted P4 chicken would read as "extra teal," not "rooted").
        // The values below deliberately keep Root's blue channel near zero (a
        // "grass" green, not a "teal" green) and Slow noticeably lighter/more cyan
        // than P2's Ocean Blue, so both states stay legible even with all four
        // player colours on screen at once. Per §1.3, colour is still only one of
        // at least two channels for every state — these pair with shape (orbiting
        // stars / static shackle / trailing streaks), never used alone.

        /// <summary>Canonical stun colour — matches <c>ControlStateVFX.StunColor</c>
        /// exactly (no change needed there); reproduced so every future consumer
        /// has one named source instead of a local literal.</summary>
        public static Color CanonicalStunColor => FeedbackPalette.Current.StunColor;

        /// <summary>Canonical root colour. Matches <c>ControlStateVFX.RootColor</c>;
        /// diverges from <c>ChickenStateOverlays.RootColor</c> (0.32, 0.72, 0.28) —
        /// see the delete/replace instruction in the report.</summary>
        public static readonly Color CanonicalRootColor = new Color(0.35f, 0.90f, 0.25f, 1f);

        /// <summary>Canonical slow colour. Matches <c>ControlStateVFX.SlowColor</c>;
        /// diverges from <c>ChickenStateOverlays.SlowColor</c> (0.35, 0.62, 1.0) —
        /// see the delete/replace instruction in the report.</summary>
        public static Color CanonicalSlowColor => FeedbackPalette.Current.SlowColor;

        /// <summary>Darker slow blue for text (the SLOW row word and the SLOW pop, E6a); rings and tints keep
        /// <see cref="CanonicalSlowColor"/>.</summary>
        public static Color CanonicalSlowTextColor => FeedbackPalette.Current.SlowTextColor;

        /// <summary>Lighter Hide purple for text (the HIDDEN row word and the POOF! pop, H7); the Hide accent keeps
        /// rings and tints.</summary>
        public static Color CanonicalHideTextColor => FeedbackPalette.Current.HideTextColor;

        /// <summary>Canonical knockback shockwave colour — already consistent
        /// (white) wherever it appears; reproduced for completeness.</summary>
        public static Color CanonicalKnockColor => FeedbackPalette.Current.KnockColor;

        // ---- Threat-overlay colours (§2.2) --------------------------------
        //
        // "Valid target" intentionally has no colour constant here: per §2.2 it
        // uses the *casting ability's own* AccentColor (already authored per
        // AbilityBaseSO, docs/ART.md's Ability Accent Colors table) so the preview
        // bracket always matches the ground decal it belongs to. Inventing a
        // second "valid target" colour here would just be a second thing to keep
        // in sync with the ability data.

        /// <summary>
        /// Neutral grey for a "nothing will happen to THIS target" mark: immune/no-effect rivals
        /// inside the telegraph (§2.2 case 4). (It also used to colour the HUD hex's "no target in
        /// range" slash; Phase 6 removed that mark: a ready hex with nobody around is not a
        /// refusal and renders like any ready hex.) Kept light enough (not near-black) to read clearly against
        /// the dark arena background (<c>MatchCamera</c>'s solid clear colour is
        /// ≈(0.10, 0.12, 0.16)).
        /// </summary>
        public static Color NeutralNoEffectColor => FeedbackPalette.Current.NeutralNoEffectColor;

        /// <summary>
        /// Desaturated red-grey wash for a hold that is REALLY refused: the caster is stunned,
        /// the slot is on cooldown / unavailable, or another ability is running
        /// (<c>AbilityPreviewRules.IsRealRefusal</c>; §2.5 — "desaturates to red-grey").
        /// <b>"No target in the shape" is not one of those</b> (Phase 6, A2: pressing with nobody
        /// around is a legal, useful aim — you are lining the move up). That state has its own
        /// cream dashed preview (<see cref="NoTargetPreviewLineWidth"/>), so the contract is:
        /// <i>never-valid-yet</i> (nothing in the shape) = dashed cream; <i>became-invalid</i>
        /// (refused mid-hold) = this wash. The two must not look alike, and this one keeps its
        /// red bias against <see cref="NeutralNoEffectColor"/> (which now marks only an
        /// individual rival the move would bounce off).
        /// </summary>
        public static readonly Color IllegalCastTintColor = new Color(0.55f, 0.30f, 0.28f, 0.80f);

        /// <summary>
        /// Width of the "no target yet" preview ribbon (Phase 6, A2): the shape's outline drawn as the
        /// dashed ring texture (<c>Art/UI/Fx/Fx_DashedRing.png</c>, cream dashes with a baked ink edge)
        /// tiled along a LineRenderer. The dashes fill ~53% of the strip's height, so a 0.24 strip
        /// gives ~0.13 world-unit dashes, about the weight of the solid preview. Wider than
        /// <see cref="TelegraphShapeLineWidth"/> only because the texture's own margins are transparent.
        /// </summary>
        public const float NoTargetPreviewLineWidth = 0.24f;

        /// <summary>Texture repeats per world unit of outline length for the dashed ring (4 dashes per
        /// repeat, so 0.5 = a dash every ~0.5 world units). Only the dash pitch changes with it.</summary>
        public const float NoTargetPreviewRepeatsPerUnit = 0.5f;

        /// <summary>Vertex tint of the dashed ribbon. White keeps the texture's own cream + ink (a
        /// tint would darken the ink edge that carries the 3:1 against grass); only alpha is applied.</summary>
        public static readonly Color NoTargetPreviewTint = new Color(1f, 1f, 1f, 0.95f);

        /// <summary>Caster-to-centre stalk colour while the preview is in its no-target dashed state:
        /// the same cream the dashes carry, so the connector does not read as a solid accent line
        /// leading to a dashed shape.</summary>
        public static Color NoTargetPreviewStalkColor => new Color(MarkCream.r, MarkCream.g, MarkCream.b, 0.9f);

        // ---- Held-move readability on grass (Phase 7 B1) -----------------------------

        /// <summary>Cream of the filled shape drawn under a held move's outline (the same cream as the dashed ring).</summary>
        public static Color HeldFillColor => FeedbackPalette.Current.CreamColor;

        /// <summary>Fill opacity of the held shape (~25%); eased to <see cref="HeldFillAlphaCancelArmed"/> while the
        /// cancel is armed, so "this will NOT fire" reads as a fainter shape as well as a grey dashed one.</summary>
        public const float HeldFillAlpha = 0.25f;
        public const float HeldFillAlphaCancelArmed = 0.10f;

        /// <summary>Ink under-stroke beneath the held shape's outline (solid, dashed no-target and grey cancel alike):
        /// near opaque, so the edge keeps &gt;= 3:1 against the lightest grass (see <c>Phase7ChunkBTests</c>).</summary>
        public static Color PreviewInkUnderStrokeColor => new Color(InkColor.r, InkColor.g, InkColor.b, 0.9f);

        /// <summary>Total extra width of the ink under-stroke over the stroke it backs: 0.06 world units, about 3 px a
        /// side at the arena camera (96 px per world unit at 1080p).</summary>
        public const float PreviewInkUnderStrokeMargin = 0.06f;

        /// <summary>Share of the dashed ribbon's height the cream dashes fill (see <see cref="NoTargetPreviewLineWidth"/>).</summary>
        public const float NoTargetDashHeightFraction = 0.53f;

        /// <summary>While a move is held, the always-on guides' ink under-strokes fall to this share of their usual alpha,
        /// so the receding guides lose their dark edge together with their colour.</summary>
        public const float AmbientInkAlphaWhileAimingMultiplier = 0.3f;

        // ---- Fizzle + cancel (Phase 6 chunk 2) -------------------------------------

        /// <summary>How long the slash flash and the whiff puff show over the hex after a fizzle.</summary>
        public const float FizzleFlashSeconds = 0.3f;

        /// <summary>Whiff puff scale at the start / end of its 0.3 s life (static, alpha-only, under Reduced Motion).</summary>
        public const float FizzlePuffStartScale = 0.6f;
        public const float FizzlePuffEndScale = 1.35f;

        /// <summary>Opacity of the whiff puff when it appears (Phase 7 B10: a ~90 px cloud off the hex's top-right corner,
        /// translucent so the icon stays readable), fading to 0 over <see cref="FizzleFlashSeconds"/>.</summary>
        public const float FizzlePuffStartAlpha = 0.7f;

        /// <summary>Play volumes of the fizzle whiff and the cancel tick. The clips are mastered to the same loudness as
        /// the menu cues, so these keep two "nothing happened" sounds well under the real ability cue.</summary>
        public const float FizzleSfxVolume = 0.55f;
        public const float CancelSfxVolume = 0.5f;

        /// <summary>Colour of the preview while the touch cancel is armed: neutral grey (dashed as well, so it is
        /// never colour-only). Matches the "no effect" grey family rather than the red illegal wash.</summary>
        public static readonly Color CancelArmedPreviewColor = new Color(0.62f, 0.62f, 0.60f, 0.95f);

        // ---- Touch-hex states (Phase 6, A1) -------------------------------------

        /// <summary>Scale of the hex being held/aimed. Applied on the press frame, locally.</summary>
        public const float HexHeldScale = 1.12f;

        /// <summary>Ease-out time of the held-hex scale and of the other hexes' dim.</summary>
        public const float HexHeldTransitionSeconds = 0.06f;

        /// <summary>Opacity of every hex that is NOT the one being held.</summary>
        public const float HexOthersWhileHeldAlpha = 0.6f;

        /// <summary>
        /// A4 caster-side stun countdown: how much later than the cast's own stun length a rival's stun may still be
        /// counted as this caster's. Covers the hop from the caster's cast to the victim's state authority applying the
        /// stun (the RPC), so a victim stunned a few ticks late still reads. Presentation only.
        /// </summary>
        public const float CasterStunCountdownSlackSeconds = 0.35f;

        /// <summary>Length of the single scale pop the in-range pip plays when it lights.</summary>
        public const float HexPipPopSeconds = 0.2f;

        /// <summary>Peak scale of the pip's pop. Skipped (static pip) under Reduced Motion.</summary>
        public const float HexPipPopScale = 1.4f;

        /// <summary>Alpha for a solid "valid target" bracket at rest (before the
        /// pulse in <see cref="ValidTargetPulseHz"/> is applied). 0.9 — near-opaque
        /// so "this one will be hit" reads with confidence, distinct from the
        /// softer 0.55–0.85 alphas used on the neutral/illegal states above.</summary>
        public const float ValidTargetBracketAlpha = 0.9f;

        /// <summary>
        /// Pulse rate for the valid-target bracket while a hold is active. 2.5 Hz —
        /// fast enough to read as "live/armed," slower than
        /// <see cref="ControlStateRingPulseHz"/> (the 9 rad/s ≈ 1.4 Hz status-ring
        /// pulse, see below) would suggest is "urgent," since a
        /// valid-target mark is informative, not alarming — it shouldn't compete
        /// visually with an actual active stun/root/slow ring pulsing nearby on a
        /// different chicken.
        /// </summary>
        public const float ValidTargetPulseHz = 2.5f;

        /// <summary>
        /// Pulse rate of <c>ControlStateVFX</c>'s status ring — the "this effect is
        /// still running" breathing on an active stun/root/slow ring. 9 rad/s ≈ 1.43 Hz,
        /// expressed in Hz here (like every other rate in this file) and converted back
        /// to rad/s at the one call site.
        /// </summary>
        /// <remarks>
        /// <b>This used to be a "reference only" constant that documented a literal it did
        /// not drive.</b> <c>ControlStateVFX.UpdateStatusRing</c> hardcoded
        /// <c>Sin(Time.time * 9f)</c>, so re-tuning this number changed the rationale on
        /// <see cref="ValidTargetPulseHz"/> and nothing else — the exact failure §6.11's
        /// "nothing reads a literal" rule exists to prevent. It is now the single source of
        /// the ring's rate, so the comparison <see cref="ValidTargetPulseHz"/> makes against
        /// it stays true by construction.
        /// </remarks>
        public const float ControlStateRingPulseHz = 9f / (2f * Mathf.PI);

        /// <summary>
        /// Resting alpha the status ring pulses around. 0.55 keeps the ring clearly present
        /// at the bottom of its swing — a control effect you are still suffering must never
        /// look like it is fading out — while sitting below the near-opaque
        /// <see cref="ValidTargetBracketAlpha"/> so an active-effect ring reads softer than
        /// a "this one is about to be hit" bracket.
        /// </summary>
        public const float ControlStateRingPulseAlphaBase = 0.55f;

        /// <summary>
        /// Alpha swing either side of <see cref="ControlStateRingPulseAlphaBase"/>, giving a
        /// 0.30–0.80 range. Deliberately never reaches 1.0: the ring is Aftermath-beat
        /// information (§3) sharing the ground plane with telegraphs and range guides, so it
        /// breathes rather than flashes.
        /// </summary>
        public const float ControlStateRingPulseAlphaAmplitude = 0.25f;

        // ================================================================
        // Jump travel + landing — FEEDBACK.md §7.2
        // ================================================================

        /// <summary>
        /// How long the mesh takes to catch up to the pivot after a length-based teleport
        /// jump (<c>ChickenAnimator</c>), and — deliberately the same number — how long
        /// <c>ControlStateVFX</c> holds the landing shockwave back before firing it.
        /// </summary>
        /// <remarks>
        /// <b>The two readings are tied on purpose and must stay one constant.</b> A jump
        /// moves the collision pivot instantly; the mesh lags behind and eases in, so the
        /// body visually arrives one settle later than the pivot does. Firing the ground
        /// ring on the jump edge would expand it under a chicken that has not landed yet,
        /// which reads as the ring belonging to something else. Splitting these into two
        /// numbers is how they drift apart.
        ///
        /// 0.18 s is short enough that the lag never reads as input latency — the pivot,
        /// which is what collision and every ability resolve against, moved on the cast
        /// tick regardless — and long enough that a 3.25 m Short jump is a visible travel
        /// rather than a one-frame pop.
        /// </remarks>
        public const float JumpTravelSettleSeconds = 0.18f;

        /// <summary>
        /// Peak upward lift of the mesh at the midpoint of a full-length jump travel, in
        /// the model root's local units. Non-negative by construction (a half-sine over the
        /// travel), which is what keeps <c>ChickenAnimator</c>'s feet-on-ground invariant
        /// intact in the only direction that matters — nothing here can sink a chicken.
        /// </summary>
        public const float JumpTravelArcHeight = 0.55f;

        /// <summary>
        /// Travel distance that earns a full-strength arc. Read from
        /// <see cref="Gameplay.JumpResolver.ShortDistance"/> rather than copied, so the
        /// shortest jump in the game is the one that scores 1.0 and the arc cannot silently
        /// decouple from the traversal ladder the way the tiers themselves once did.
        /// </summary>
        /// <remarks>
        /// The scale is squared at the call site, giving a dead zone for free. That dead
        /// zone is load-bearing: on a remote peer <c>NetworkTransform</c> interpolation has
        /// already slid the pivot, so the measured residual is a fraction of a metre and the
        /// arc must come out to nothing. Squaring turns a 0.15 linear scale into 0.02 —
        /// about a centimetre of lift, i.e. none.
        /// </remarks>
        public const float JumpTravelFullArcDistance = Gameplay.JumpResolver.ShortDistance;

        /// <summary>
        /// Hard ceiling on the measured travel residual, in metres. Sits just above the Big
        /// tier's 11.7 m so no legitimate jump is ever clipped.
        /// </summary>
        /// <remarks>
        /// A safety rail, not a tuning knob. The residual is measured as "where the mesh was
        /// last frame minus where it is now", so anything that moves the pivot on the same
        /// frame as a jump edge — a resimulation snap, a spawn — lands in it. Without the
        /// clamp such a frame would fling the mesh an arbitrary distance from its own
        /// collider and ease back over the next 0.18 s, which reads as the model detaching.
        /// </remarks>
        public const float JumpTravelMaxResidual = 14f;

        // ---- Jump beats (batch D5, vfx.md 2.6) ----------------------------------------------------------

        /// <summary>Launch dust ring at the pre-jump position.</summary>
        public const int JumpLaunchDustCount = 6;
        public const float JumpLaunchDustRadius = 0.3f;
        public const float JumpLaunchDustSeconds = 0.3f;
        /// <summary>Two feathers thrown up from the launch.</summary>
        public const int JumpLaunchFeatherCount = 2;
        public const float JumpLaunchFeatherSpeed = 1.5f;
        public const float JumpLaunchFeatherSeconds = 0.4f;
        /// <summary>Accent sparkles along the chord at the animator's half-sine height (t = 0.1..0.9).</summary>
        public const int JumpArcDotCount = 5;
        public const float JumpArcDotSeconds = 0.25f;
        /// <summary>F4: 0.12 to 0.20 (the dots were sub-readable at the play camera).</summary>
        public const float JumpArcDotSize = 0.20f;
        /// <summary>Body-centre height the arc lift is added to (the dots ride the bird, not the ground).</summary>
        public const float JumpArcDotBaseHeight = 0.4f;
        /// <summary>A jump that moved the model less than this draws no arc dots (a proxy whose pivot already slid).</summary>
        public const float JumpArcMinChord = 0.5f;
        /// <summary>Landing dust at the pivot after <see cref="JumpTravelSettleSeconds"/>.</summary>
        public const int JumpLandingDustCount = 8;
        public const float JumpLandingDustSeconds = 0.3f;
        /// <summary>Reduced Motion: launch and landing dust are this many puffs, fading in place (no travel, no arc, no feathers).</summary>
        public const int JumpReducedDustCount = 3;
        /// <summary>A teleport-jump event within this long of a Jump slot edge is the same jump (not a second beat set or HOP!).</summary>
        public const float JumpPathDedupeSeconds = 0.6f;

        // ---- Egg Shell body (batch D2, vfx.md 1.2-1.5) -----------------------------------------------

        /// <summary>World height of the egg at class scale 1 (the authored mesh is 1 m, so it is scaled by this x the egg scale).</summary>
        public const float ShellEggBaseHeight = 1.22f;
        /// <summary>The egg scale is <c>min(ClassScale, this)</c>: the Fatty egg tops out at 1.22 x 1.27 = 1.55 m (just under ShellEggTopMaxHeight).</summary>
        public const float ShellEggScaleCap = 1.27f;
        /// <summary>No class's egg may reach this height (m): the nameplate pill starts at about 1.48-1.55.</summary>
        public const float ShellEggTopMaxHeight = 1.55f;
        /// <summary>Walk: how much of the seated cup is risen around the legs, and how long it takes (ease-out-back).</summary>
        public const float ShellWalkCupRise = 0.35f;
        public const float ShellWalkRiseSeconds = 0.25f;
        public const float ShellWalkRiseOvershoot = 1.06f;
        public const float ShellWalkRiseReducedSeconds = 0.15f;
        /// <summary>Walk waddle: roll amplitude (deg) and frequency, composed into the animator's bank. Reduced Motion: the smaller one.</summary>
        public const float ShellWaddleDegrees = 6f;
        public const float ShellWaddleReducedDegrees = 2f;
        public const float ShellWaddleHz = 2.2f;
        public const float ShellWaddleBlendSeconds = 0.10f;
        /// <summary>Heel dust while walking: puffs per metre walked (Reduced Motion halves it), puff colour alpha, and where it spawns behind the feet.</summary>
        public const float ShellWalkDustPerMeter = 2f;
        public const float ShellWalkDustAlpha = 0.6f;
        public const float ShellWalkDustBehind = 0.30f;
        /// <summary>Close: the cap drops from this multiple of its seated height over the drop time, then the egg squashes.</summary>
        public const float ShellCloseSeconds = 0.30f;
        public const float ShellCloseDropSeconds = 0.18f;
        public const float ShellCloseReducedSeconds = 0.15f;
        public const float ShellCapDropFromScale = 1.6f;
        public const float ShellSquashXZ = 1.08f;
        public const float ShellSquashY = 0.90f;
        public const float ShellSquashSeconds = 0.12f;
        public const int ShellClosePuffs = 6;
        public const float ShellClosePuffRadius = 0.30f;
        public const float ShellReducedGrowFrom = 0.85f;
        /// <summary>The bird shrinks uniformly to this inside the egg (eased over the close, back over the open) so tail and wings fit.</summary>
        public const float ShellBodyScale = 0.82f;
        /// <summary>E1a (2026-11-08): the Warrior tucks deeper than the default so wings and tail stay inside the egg (comb tip may peek).</summary>
        public const float ShellBodyTuckWarrior = 0.70f;
        /// <summary>E1a: the Warrior also sinks this far (world m) into the egg, eased with the tuck. Every other class sinks 0.</summary>
        public const float ShellBodySinkWarrior = 0.06f;

        /// <summary>E1a follow-up: the Warrior is squeezed along its own front-back axis (model local z) inside the egg so the tail and
        /// folded wing tips at the seam band stay in; the sides already fit, so x stays 1. Every other class 1.</summary>
        public const float ShellBodySqueezeZWarrior = 0.76f;

        /// <summary>The egg-phase front-back squeeze per class (model local z, eased with the tuck): Warrior <see cref="ShellBodySqueezeZWarrior"/>, others 1.</summary>
        public static float ShellBodySqueezeZFor(CluckWars.Gameplay.ChickenClass cls) => cls switch
        {
            CluckWars.Gameplay.ChickenClass.Warrior => ShellBodySqueezeZWarrior,
            _ => 1f,
        };

        /// <summary>
        /// The egg-phase body tuck per class (E1a): Warrior <see cref="ShellBodyTuckWarrior"/>, every other class the default
        /// <see cref="ShellBodyScale"/>. <c>ChickenShellVisuals.BodyScaleFor</c> still caps it for a capped egg (the Fatty).
        /// </summary>
        public static float ShellBodyTuckFor(CluckWars.Gameplay.ChickenClass cls) => cls switch
        {
            CluckWars.Gameplay.ChickenClass.Warrior => ShellBodyTuckWarrior,
            _ => ShellBodyScale,
        };

        /// <summary>The egg-phase body sink per class in world metres (E1a): Warrior <see cref="ShellBodySinkWarrior"/>, others 0.</summary>
        public static float ShellBodySinkFor(CluckWars.Gameplay.ChickenClass cls) => cls switch
        {
            CluckWars.Gameplay.ChickenClass.Warrior => ShellBodySinkWarrior,
            _ => 0f,
        };
        /// <summary>Authored height of every class model (m at root scale 1: the 1.6 m capsule) and how far the comb may peek above the egg.</summary>
        public const float ShellModelHeight = 1.6f;
        public const float ShellBodyTopAllowance = 0.10f;
        /// <summary>Identity ring at alpha 1.0 for this long on the close edge.</summary>
        public const float ShellRingFlashSeconds = 0.20f;
        /// <summary>Idle: wobble about the base, breathing, last-moments jitter.</summary>
        public const float ShellWobbleDegrees = 3f;
        public const float ShellWobbleHz = 1.8f;
        public const float ShellBreathAmount = 0.015f;
        public const float ShellBreathHz = 1f;
        public const float ShellJitterDegrees = 1.5f;
        public const float ShellJitterHz = 8f;
        public const float ShellJitterSeconds = 0.30f;
        /// <summary>Cracks: <c>_Crack = smoothstep(start, end, elapsed01)</c>, pushed at <see cref="StatusArcDrainUpdateHz"/>; an early end snaps it to the third value first.</summary>
        public const float ShellCrackStart = 0.40f;
        public const float ShellCrackEnd = 0.90f;
        public const float ShellCrackOnEarlyEnd = 0.60f;
        /// <summary>Open: the cap pops and tumbles, the cup sinks, then the burst.</summary>
        public const float ShellOpenSeconds = 0.40f;
        public const float ShellOpenReducedSeconds = 0.20f;
        public const float ShellCapPopHeight = 0.6f;
        public const float ShellCapPopSeconds = 0.35f;
        public const float ShellCapTumbleDegrees = 20f;
        public const float ShellCapDrift = 0.25f;
        public const float ShellCapScaleOutSeconds = 0.10f;
        public const float ShellCupSinkSeconds = 0.25f;
        public const float ShellFoldSeconds = 0.15f;
        public const int ShellShardCount = 3;
        /// <summary>H12 (E8a): shard pieces thrown by the Shield cast burst (the Shard family max is 3, see ChickenVFX.CastShardCount).</summary>
        public const int ShellCastShardCount = 2;
        public const int ShellFeatherCount = 10;
        public const int ShellYolkPuffs = 6;
        public const float ShellYolkAlpha = 0.7f;
        public const float ShellFeatherAlpha = 0.9f;
        /// <summary>F2: the hatch debris (shards, feathers, yolk puffs) leaves from a seam-height disc of this radius (metres at
        /// egg scale 1, multiplied by <c>ChickenShellVisuals.EggScale</c> like the egg), so it starts at the shell outline, not inside it.</summary>
        public const float ShellBurstRadius = 0.45f;
        /// <summary>F2: the constant upward speed (m/s) every hatch particle gets on top of its outward speed, so it rises off the seam.</summary>
        public const float ShellBurstLift = 0.8f;
        /// <summary>F2: the hatch debris falls at this fraction of g (a slow, floaty drop), so it clears the shell before it lands.</summary>
        public const float ShellBurstGravity = 0.3f;
        /// <summary>H2: of the <see cref="ShellFeatherCount"/> hatch feathers this many leave from the cap top (seam + <see cref="ShellCapFeatherOffset"/>), the rest from the seam disc.</summary>
        public const int ShellCapFeatherCount = ShellFeatherCount / 2;
        /// <summary>H2: the cap-top feathers start this far above the seam (m), about the top of the open cap.</summary>
        public const float ShellCapFeatherOffset = 0.4f;
        /// <summary>
        /// H2: how far above the head (m) the cap-top feathers peak. The launch lift is solved per class by
        /// <c>ChickenShellVisuals.CapFeatherLift</c> (head = <see cref="ShellModelHeight"/> x class scale, the hatched bird at full height).
        /// Measured in the lab (Warrior, scale 1, head 1.6 m, seam 0.73 m): the solved lift 11.9 peaked 0.54 m above the head
        /// (cap-half max 2.14 m above the feet at about 0.5 s; level with the head at 0.07 s, +0.4 m at 0.25 s). The Feather family's
        /// drag clamp (<c>ChickenVFX.FeatherSpeedLimit</c>) leaves a rise of about <c>0.6 * life + 0.07 * (vy - 0.6)</c>.
        /// </summary>
        public const float ShellCapFeatherClearance = 0.5f;
        /// <summary>H2: the cap-top feathers' gravity (fraction of g), baked into the launch like every Feather-family burst.</summary>
        public const float ShellCapBurstGravity = 0.6f;

        // ---- Speed body cue (D4, vfx.md 2.5) --------------------------------------------------------

        /// <summary>The cue runs while the Speed slot is active and the planar speed is above this fraction of the class MoveSpeed.</summary>
        public const float SpeedCueRunFraction = 0.5f;
        /// <summary>Wind streaks per metre travelled (none under Reduced Motion).</summary>
        public const float SpeedStreakPerMetre = 6f;
        public const float SpeedStreakLifeSeconds = 0.25f;
        public const float SpeedStreakSize = 0.30f;
        /// <summary>F4: 0.7 to 0.9 so the cream streak (with the Fx_SpeedLine ink edge) reads on the green floor.</summary>
        public const float SpeedStreakAlpha = 0.9f;
        /// <summary>Streaks spawn between these heights (hip height) in a 0.3 (across) x 0.1 (along) box at the tail.</summary>
        public const float SpeedStreakMinHeight = 0.55f;
        public const float SpeedStreakMaxHeight = 0.80f;
        public const float SpeedStreakBoxAcross = 0.30f;
        public const float SpeedStreakBoxAlong = 0.10f;
        /// <summary>How far behind the bird root the streak box sits.</summary>
        public const float SpeedStreakTailOffset = 0.45f;
        /// <summary>Stretched Billboard: speed scale 0.12 (spec) and the length scale (spec 1.0; raised, see ChickenVFX).</summary>
        public const float SpeedStreakSpeedScale = 0.12f;
        public const float SpeedStreakLengthScale = 3f;
        /// <summary>Tiny backward velocity that gives the Stretched Billboard a direction (the streak is left behind, not thrown).</summary>
        public const float SpeedStreakBackSpeed = 2f;
        public const float SpeedDustPerMetre = 2f;
        public const float SpeedDustPerMetreReduced = 1f;
        public const float SpeedDustSize = 0.22f;
        public const float SpeedDustLifeSeconds = 0.30f;
        public const float SpeedDustAlpha = 0.5f;
        /// <summary>Active edge: accent sparkles at the feet ("go!").</summary>
        public const int SpeedEdgeSparkleCount = 6;
        /// <summary>Reduced Motion: half the active-edge sparkles, fading in place.</summary>
        public const int SpeedEdgeSparkleCountReduced = 3;
        public const float SpeedEdgeSparkleLifeSeconds = 0.20f;
        /// <summary>Extra forward pitch while the cue runs (the animator lean saturates at the base MoveSpeed, so a 1.3x burst adds none).</summary>
        public const float SpeedLeanExtraDegrees = 6f;
        public const float SpeedLeanBlendPerSecond = 10f;
        /// <summary>Per-frame distance clamp for the emitters (a respawn teleport must not dump a thousand particles).</summary>
        public const float SpeedCueMaxStepMetres = 1.5f;

        // ---- Shove beats (batch D6, vfx.md 2.3) ------------------------------------------------------

        /// <summary>Victim skid dust runs this long after the KnockbackEventId edge (and only while planar speed is above the gate).</summary>
        public const float ShoveSkidWindowSeconds = 0.3f;
        /// <summary>The skid needs more than this planar speed (m/s): the knockback decays under it in about 0.17 s, and a bird merely walking does not skid.</summary>
        public const float ShoveSkidMinSpeed = 4f;
        public const float ShoveSkidPerMetre = 4f;
        /// <summary>F4: x1.3 of the D6 0.18-0.24 so the fainter puff (alpha 0.45) keeps its footprint.</summary>
        public const float ShoveSkidSizeMin = 0.234f;
        public const float ShoveSkidSizeMax = 0.312f;
        public const float ShoveSkidLifeSeconds = 0.25f;
        /// <summary>Scatter radius of a puff around the feet path.</summary>
        public const float ShoveSkidScatter = 0.12f;
        /// <summary>Most skid puffs one knockback may emit: with a 0.25 s life and a 0.3 s window this is also the most live at once.</summary>
        public const int ShoveSkidMaxLive = 10;
        /// <summary>Brown-cream ground dust (vfx.md 2.3); F4 alpha 0.6 to 0.45 (the brown read as mud on the green floor).</summary>
        public static readonly Color ShoveSkidColor = new Color(0.85f, 0.75f, 0.55f, 0.45f);

        /// <summary>Caster shock ring: accent sparkles on a circle around the caster, flying outward.</summary>
        public const int ShoveRingCount = 8;
        /// <summary>Reduced Motion: fewer sparkles, fading in place (no travel).</summary>
        public const int ShoveRingCountReduced = 4;
        public const float ShoveRingRadius = 0.6f;
        public const float ShoveRingSpeed = 3f;
        public const float ShoveRingLifeSeconds = 0.2f;
        public const float ShoveRingSizeMin = 0.24f;
        public const float ShoveRingSizeMax = 0.32f;
        /// <summary>Height of the ring above the caster's feet (the cast flash is on the ground, the ring is at body level).</summary>
        public const float ShoveRingHeight = 0.35f;

        /// <summary>Victim squash on the knockback edge, composed through ChickenAnimator.ExtraScale: this scale returns to 1 over the duration.</summary>
        public static readonly Vector3 ShoveSquashScale = new Vector3(1.12f, 0.88f, 1.12f);
        public const float ShoveSquashSeconds = 0.12f;

        // ---- Steal beats (batch D7, vfx.md 2.1) -----------------------------------------------------

        /// <summary>Caster beak: the grab lines start this far in front of the caster.</summary>
        public const float StealGrabBeakDistance = 0.9f;
        /// <summary>Height of the grab lines above the caster's feet (the impact motion lines' height).</summary>
        public const float StealGrabLineHeight = 0.85f;
        /// <summary>The lines reach this share of the way to the victim before they snap back.</summary>
        public const float StealGrabReach = 0.8f;
        public const float StealGrabExtendSeconds = 0.08f;
        public const float StealGrabRetractSeconds = 0.10f;
        /// <summary>Each further victim starts its line this much later (up to the 3 impact LineRenderers).</summary>
        public const float StealGrabStaggerSeconds = 0.06f;
        public const float StealGrabAlpha = 0.9f;
        public const float StealGrabWidth = 0.06f;
        /// <summary>Reduced Motion: the extended lines flash once and fade over this long (no travel).</summary>
        public const float StealGrabFlashSeconds = 0.15f;

        /// <summary>Whiff (zero-hit Steal): two Fx_Whiff arcs at the beak, grey (nothing orange), fading over this long.</summary>
        public const int StealWhiffCount = 2;
        public const float StealWhiffSeconds = 0.25f;
        public const float StealWhiffScaleStart = 0.5f;
        public const float StealWhiffScaleEnd = 1.1f;
        /// <summary>World size (m) of a whiff arc at scale 1.</summary>
        public const float StealWhiffSize = 0.55f;
        public const float StealWhiffSpreadDegrees = 25f;
        public const float StealWhiffAlpha = 0.7f;
        public static readonly Color StealWhiffColor = new Color(0.78f, 0.78f, 0.76f, 1f);

        /// <summary>Food chunks per robbed victim (kernel sprites), flying victim -> thief.</summary>
        public const int StealChunksPerVictim = 3;
        /// <summary>Pooled chunk movers per arena (3 victims x 3 kernels = 9 live at most, plus whiff arcs).</summary>
        public const int StealChunkPoolSize = 12;
        public const float StealChunkHeight = 1.0f;
        public const float StealChunkArcHeight = 0.6f;
        public const float StealChunkFlightSeconds = 0.35f;
        public const float StealChunkSpinDegreesPerSecond = 540f;
        /// <summary>Kernel world size. 0.24 m (spec 0.16): at 0.16 the kernel was about 10 px in the iso view and lost behind the bird.</summary>
        public const float StealChunkSize = 0.24f;
        public const int StealChunkSortingOrder = 5;
        /// <summary>Each kernel of one robbery leaves this much later than the previous one (a stream, not a clump).</summary>
        public const float StealChunkStaggerSeconds = 0.04f;
        /// <summary>Sideways spread (m) of the three kernels' arcs, so they do not overlap.</summary>
        public const float StealChunkSpread = 0.18f;
        /// <summary>Thief hidden or faded: the kernels climb to the victim's own position plus this and vanish there.</summary>
        public const float StealChunkHiddenRiseMetres = 1.2f;
        /// <summary>Reduced Motion: the kernels fade in at the thief over this long (no travel).</summary>
        public const float StealChunkReducedFadeSeconds = 0.2f;
        /// <summary>A kernel fades out over the last share of its flight when it vanishes (hidden thief).</summary>
        public const float StealChunkVanishFraction = 0.4f;

        /// <summary>I5: the thief own +n waits for the last kernel; with no kernel flight in the air (hidden-thief reroute,
        /// culled mover) it pops this long after the cargo write instead (one kernel flight).</summary>
        public const float StealSelfPopFallbackSeconds = 0.35f;
        /// <summary>I5: a held +n pops after this long even if a flight is still reported in the air, so it is never lost
        /// (fallback + the last kernel of a robbery, 2 x stagger + flight, + a round trip).</summary>
        public const float StealSelfPopMaxHoldSeconds = 1.2f;

        /// <summary>Arrival sparkles at the thief: 4 gold (2 under Reduced Motion, fading in place), 0.25 s.</summary>
        public const int StealArrivalSparkleCount = 4;
        public const int StealArrivalSparkleCountReduced = 2;
        public const float StealArrivalSparkleLifeSeconds = 0.25f;
        public const float StealArrivalSparkleSizeMin = 0.2f;
        public const float StealArrivalSparkleSizeMax = 0.3f;
        public const float StealArrivalSparkleSpeed = 1.2f;
        public const float StealArrivalSparkleHeight = 1.0f;

        // ---- Stun beats (D10, vfx.md 2.2) ------------------------------------------------------------

        /// <summary>Star pop-in on the Stunned rising edge: scale 0 to <see cref="StunStarPopOvershoot"/> (reached at
        /// <see cref="StunStarPopPeakFraction"/> of the time) and back to 1 over <see cref="StunStarPopSeconds"/>. Reduced Motion:
        /// full size, alpha fades in over <see cref="StunStarFadeInSecondsReduced"/>.</summary>
        public const float StunStarPopSeconds = 0.15f;
        public const float StunStarPopOvershoot = 1.25f;
        public const float StunStarPopPeakFraction = 0.6f;
        public const float StunStarFadeInSecondsReduced = 0.15f;

        /// <summary>Star drop-off on the falling edge: the stars stop orbiting, fall <see cref="StunStarDropMetres"/> (gravity, so
        /// quadratic in time) and fade over <see cref="StunStarDropSeconds"/>. Reduced Motion: fade in place over the shorter time.</summary>
        public const float StunStarDropSeconds = 0.30f;
        public const float StunStarDropMetres = 0.4f;
        public const float StunStarDropSecondsReduced = 0.20f;

        /// <summary>Caster wing-slam arcs (<c>Fx_Whiff</c> FoodChunkFlight movers): one each side at this distance and height, scale
        /// start to end over the life (static under Reduced Motion), Stun accent at this alpha, tilted outward by the roll.</summary>
        public const int StunArcCount = 2;
        public const float StunArcSideMetres = 0.9f;
        public const float StunArcHeight = 0.85f;
        public const float StunArcSeconds = 0.18f;
        public const float StunArcScaleStart = 0.5f;
        public const float StunArcScaleEnd = 1.3f;
        public const float StunArcSize = 0.7f;
        public const float StunArcAlpha = 0.8f;
        public const float StunArcRollDegrees = 55f;
        public const int StunArcSortingOrder = 5;

        /// <summary>Victim bonk puff: cream dust at the head (4, or 2 fading in place under Reduced Motion).</summary>
        public const int StunBonkCount = 4;
        public const int StunBonkCountReduced = 2;
        public const float StunBonkHeight = 1.5f;
        public const float StunBonkSeconds = 0.25f;
        public const float StunBonkSpeed = 0.8f;
        public const float StunBonkSizeMin = 0.28f;
        public const float StunBonkSizeMax = 0.40f;
        public const float StunBonkScatter = 0.18f;

        /// <summary>Stun stars (F3): orbit at beak height, <see cref="StunStarBeakHeight"/> world metres above the pivot (plus the
        /// class head lift), radius <see cref="StunStarOrbitRadius"/>, so they frame the head BELOW the nameplate pill (which
        /// starts about 1.5 m up) instead of crossing it. Sorting order 8 is the head-overlay layer (vfx.md 0.1), under the
        /// 10-12 label band.</summary>
        public const float StunStarBeakHeight = 1.20f;

        /// <summary>H5: the stars' resting height is <see cref="StunStarBeakHeight"/> times the model's class scale (a taller class has
        /// its head higher), capped so the star TOP (half a star + the tilt's vertical extent) stays under the cargo bar's bottom edge.
        /// The cap is the bar centre minus this clearance: 0.27 m = star half-size 0.13 + tilt extent 0.088 + the bar half-height 0.04
        /// + a hair. (The first H5 brief said 0.20; that brushes the bar by 0.07 m once the star radius and the tilt are counted.)</summary>
        public const float StunStarBarClearance = 0.27f;

        /// <summary>H5: the star orbit plane leans toward the camera by this many degrees (about the camera's right axis), so the
        /// ring reads as a ring from the high game camera and the near stars do not hide the head.</summary>
        public const float StunStarTiltDegrees = 15f;
        public const float StunStarOrbitRadius = 0.34f;
        public const int StunStarSortingOrder = 8;

        /// <summary>Label band (vfx.md 0.1, orders 10-12), bottom to top: rival pill, rival text (and dot ring), rival dot and slow
        /// glyph (and the badges: glyph + row text). The local "You" pill and text sit above every rival part (13 to 15), and
        /// everything stays under the cargo bars (100) and the floating popups (210). Stars (8) sit below the whole band.</summary>
        public const int RivalPillSortingOrder = 10;
        public const int RivalTextSortingOrder = 11;
        public const int RivalDotSortingOrder = 12;
        public const int StatusBadgeSortingOrder = 12;
        public const int LocalPillSortingOrder = 13;
        public const int LocalTextSortingOrder = 14;

        // ---- Slow aura (D8, vfx.md 2.4) --------------------------------------------------------------

        /// <summary>Ground heights of the aura outline (the order-0 band, same floor as every ring) and the edge band (order 1).</summary>
        public const float SlowAuraOutlineY = 0.05f;
        public const float SlowAuraBandY = 0.06f;
        public const int SlowAuraOutlineSortingOrder = 0;
        public const int SlowAuraBandSortingOrder = 1;

        /// <summary>The outline is the dashed ribbon (<see cref="NoTargetPreviewLineWidth"/> wide) in the Slow accent at this alpha,
        /// turning at <see cref="SlowAuraOutlineLapsPerSecond"/> (0.1 lap/s = one lap per 10 s; the dashes travel along the ring).</summary>
        public const float SlowAuraOutlineAlpha = 0.55f;
        public const float SlowAuraOutlineLapsPerSecond = 0.1f;
        public const int SlowAuraOutlineSegments = 72;

        /// <summary>Edge band (<c>Fx_AuraEdge</c>, outer radius = aura radius, fading inward): alpha, and its Reduced Motion alpha.
        /// Never a fill: a filled 13 m disc would wash the arena and hide the identity rings.</summary>
        public const float SlowAuraBandAlpha = 0.20f;
        public const float SlowAuraBandAlphaReduced = 0.14f;

        /// <summary>The feather-fall PS (<c>VFX_AuraFeather</c>): spawn disc radius is the aura radius minus this margin, rate per
        /// second (and Reduced Motion), spawn height, lifetime, gravity modifier, size range, alpha, Noise sway.</summary>
        public const float SlowAuraFeatherInsetMetres = 0.28f;
        public const float SlowAuraFeatherRate = 10f;
        public const float SlowAuraFeatherRateReduced = 6f;
        public const float SlowAuraFeatherSpawnHeight = 1.6f;
        public const float SlowAuraFeatherLifeSeconds = 1.6f;
        public const float SlowAuraFeatherGravity = 0.03f;
        /// <summary>F4: feather-fall 0.18-0.26 to 0.30-0.40 and alpha 0.8 to 0.95 (rate and max unchanged). The caster foot puffs keep the
        /// old size through <see cref="SlowAuraPuffSizeMin"/>.</summary>
        public const float SlowAuraFeatherSizeMin = 0.30f;
        public const float SlowAuraFeatherSizeMax = 0.40f;
        public const float SlowAuraFeatherAlpha = 0.95f;
        public const float SlowAuraFeatherNoiseStrength = 0.3f;
        public const float SlowAuraFeatherNoiseFrequency = 0.4f;
        /// <summary>The aura system's own maxParticles (counted outside the five ChickenVFX families; see ChickenParticleBudgetTests).</summary>
        public const int SlowAuraFeatherMaxParticles = 24;

        /// <summary>Lavender-tinted cream of the falling feathers: the palette cream pulled toward the Slow accent.</summary>
        public static Color SlowAuraFeatherColor
        {
            get
            {
                var p = FeedbackPalette.Current;
                Color c = Color.Lerp(p.CreamColor, p.SlowAccent, 0.35f);
                c.a = SlowAuraFeatherAlpha;
                return c;
            }
        }

        /// <summary>The caster's foot puffs: this many feathers (halved under Reduced Motion) every this many seconds while the aura runs.</summary>
        public const int SlowAuraPuffCount = 4;
        public const float SlowAuraPuffIntervalSeconds = 0.4f;
        public const float SlowAuraPuffLifeSeconds = 0.5f;
        public const float SlowAuraPuffHeight = 0.12f;
        /// <summary>Caster foot-puff feather size (the pre-F4 feather-fall size; F4 enlarged only the falling feathers).</summary>
        public const float SlowAuraPuffSizeMin = 0.18f;
        public const float SlowAuraPuffSizeMax = 0.26f;

        // ---- Hide beats (D9, vfx.md 2.8) ------------------------------------------------------------

        /// <summary>The two Hide poofs (enter = cream, break = Hide accent): 8 dust puffs at 0.6 m flying radially at 1.2 m/s for
        /// 0.35 s, sized 0.3 to 0.6 (the family has no size curve, so each puff takes one size from the range), plus 3 feathers
        /// drifting up on the enter edge. Reduced Motion: half the puffs, no travel (they fade in place).</summary>
        public const int HidePoofDustCount = 8;
        public const int HidePoofDustCountReduced = 4;
        public const int HidePoofFeatherCount = 3;
        public const int HidePoofFeatherCountReduced = 1;
        public const float HidePoofHeight = 0.6f;
        public const float HidePoofSpeed = 1.2f;
        public const float HidePoofLifeSeconds = 0.35f;
        public const float HidePoofSizeMin = 0.3f;
        public const float HidePoofSizeMax = 0.6f;
        public const float HidePoofScatterRadius = 0.2f;
        public const float HidePoofFeatherSpeed = 0.7f;
        public const float HidePoofFeatherLifeSeconds = 0.8f;
        public const float HidePoofFeatherSizeMin = 0.28f;
        public const float HidePoofFeatherSizeMax = 0.38f;

        /// <summary>The local bird's dashed cream ring under it for the whole Hide: radius is <see cref="SelfRingRadius"/> (0.62),
        /// order 0, turning 0.25 lap/s (static under Reduced Motion), at this ground height and alpha.</summary>
        public const float HideRingLapsPerSecond = 0.25f;
        public const float HideRingAlpha = 0.8f;
        public const float HideRingY = 0.05f;
        public const float HideRingWidth = 0.12f;
        public const int HideRingSortingOrder = 0;
        public const int HideRingSegments = 48;

        /// <summary>The ring blinks twice in the last <see cref="HideBlinkLeadSeconds"/> of the Hide (each blink one
        /// <see cref="HideBlinkPeriodSeconds"/>: dim for the first half, lit for the second). Reduced Motion replaces the blink with
        /// a steady dip to <see cref="HideBlinkSteadyAlpha"/> over the same last 0.25 s (no flicker).</summary>
        public const float HideBlinkLeadSeconds = 0.25f;
        public const float HideBlinkPeriodSeconds = 0.125f;
        public const float HideBlinkDimAlpha = 0.15f;
        public const float HideBlinkSteadyAlpha = 0.5f;

        // ---- Peck chip (D11, vfx.md 2.7) -------------------------------------------------------------

        /// <summary>Pile chip: gold <c>Fx_Corn</c> kernels (5; 3, fading at the beak with no travel, under Reduced Motion) from the
        /// mound top toward the pecker's beak. Pile-owned system <c>VFX_PileKernel</c>, at most
        /// <see cref="PeckKernelMaxPerPile"/> live per pile (4 piles = 32 of the arena budget).</summary>
        public const int PeckKernelCount = 5;
        public const int PeckKernelCountReduced = 3;
        public const float PeckKernelSeconds = 0.25f;
        public const float PeckKernelSpeed = 3f;
        public const float PeckKernelGravity = 0.5f;
        public const float PeckKernelSize = 0.12f;
        public const float PeckKernelScatter = 0.12f;
        public const int PeckKernelSortingOrder = 5;
        public const int PeckKernelMaxPerPile = 8;

        /// <summary>The beak: this far ahead of the pecker, and this high above the grass (metres).</summary>
        public const float PeckBeakDistance = 0.9f;
        public const float PeckBeakHeight = 0.45f;

        /// <summary>Contact puff: straw-cream dust at the contact point (3, or 2 fading in place), ground band (order 2).</summary>
        public const int PeckPuffCount = 3;
        public const int PeckPuffCountReduced = 2;
        public const float PeckPuffSeconds = 0.25f;
        public const float PeckPuffSpeed = 0.5f;
        public const float PeckPuffSizeMin = 0.25f;
        public const float PeckPuffSizeMax = 0.35f;
        public const float PeckPuffScatter = 0.12f;
        public const float PeckPuffHeight = 0.08f;
        public static readonly Color PeckPuffColor = new Color(0.90f, 0.85f, 0.65f, 1f);

        /// <summary>Mound squash: pile Y scale x this, back to 1 over <see cref="PeckSquashSeconds"/> (none under Reduced Motion).</summary>
        public const float PeckSquashScaleY = 0.94f;
        public const float PeckSquashSeconds = 0.12f;

        // ---- Particle budget (batch D, vfx.md 0.2) --------------------------------------------------

        /// <summary>Hard cap on ParticleSystems per chicken (one draw call each on the shared Fx material).</summary>
        public const int ParticleSystemsPerChickenCap = 7;

        /// <summary>Hard cap on live particles per chicken at the worst coincidence.</summary>
        public const int ParticlesPerChickenCap = 64;

        /// <summary>
        /// Headroom the per-chicken emission guard (<c>ChickenVFX.EmitRoom</c>) keeps under <see cref="ParticlesPerChickenCap"/> for the one
        /// emitter it cannot throttle, the rate-driven Slow aura feather system (10/s, so at most a particle or two lands between guard
        /// checks). D12: the cast-storm census peaked at 75 live on one bird (Speed + Slow aura + Shove + Jump, every family saturated)
        /// because the five families already sum to 64 before the aura adds its steady 16; the guard clips late emissions instead.
        /// </summary>
        public const int ParticleBudgetMargin = 2;

        /// <summary>Hard cap on live particles across the arena (4 birds, piles, the Slow aura, egg opens).</summary>
        public const int ParticlesPerArenaCap = 400;
    }
}
