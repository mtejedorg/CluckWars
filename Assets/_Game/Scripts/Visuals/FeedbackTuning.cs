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

        /// <summary>
        /// Distance, in USS px, the touch point must move away from the ability
        /// hex's centre before a hold counts as "dragged off" and cancels (§2.6).
        /// Derived from <c>docs/STATE.md</c>'s measured conversion on this canvas:
        /// 1 dp ≈ 2.35 USS px, and <c>TouchControls.uss</c>'s hex is 150 px (≈64 dp)
        /// wide. 130 px (≈55 dp) is roughly the hex's own half-width (≈75 px) plus a
        /// margin comparable to a thumb's resting wobble radius during aim — big
        /// enough that ordinary aiming jitter never triggers an accidental cancel,
        /// small enough that a deliberate "slide off the button" gesture reaches it
        /// well within the reach of the same thumb that pressed it.
        /// </summary>
        public const float DragCancelDistancePx = 130f;

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
        /// Caster's own micro-shake on a landed Steal cast — bigger than a plain
        /// hit because cargo visibly changes hands. Matches the existing
        /// <c>ChickenVFX</c> call site (0.18 mag / 0.22 s); reproduced for the same
        /// reason as <see cref="CasterMicroShakeMagnitude"/>.
        /// </summary>
        public const float CasterStealShakeMagnitude = 0.18f;

        /// <summary>Duration pair for <see cref="CasterStealShakeMagnitude"/>.</summary>
        public const float CasterStealShakeDurationSeconds = 0.22f;

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
        /// <see cref="FloatingTextRiseDistance"/> (1.1) the text travels 2.05 → 3.15, which
        /// clears the whole on-character stack and the §5.1 status-badge row planned above
        /// the nameplate.
        /// </summary>
        public const float FloatingTextSpawnHeight = 2.05f;

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
        /// rather than a pure red so it does not collide with P1's identity orange
        /// (#E8751A, <c>ChickenWorldBars</c>) or with the <c>FULL!</c> cargo warning
        /// (1.00, 0.18, 0.08) that already lives on the same chicken — a number reading
        /// "you lost cargo" must not be mistakable for either.
        /// </summary>
        public static readonly Color FloatingTextCargoLossColor = new Color(0.95f, 0.35f, 0.25f, 1f);

        /// <summary>
        /// Colour for a cargo *gain* popup (<c>+5 🌽</c> at the thief). ADDED for Stage 4.
        /// A bright grass green, matched in hue family to
        /// <see cref="CanonicalRootColor"/>'s deliberate "grass, not teal" choice so it
        /// stays clear of P4 Forest Teal (#0D9E7A) for the same colour-blind-safety reason
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
        /// World-space height of the *bottom* row of the §5.1 status badge stack, above
        /// the chicken's pivot. ADDED for Stage 5a. The on-character stack is, bottom to
        /// top: cargo bar 1.16 (<c>ChickenWorldBars</c>) → nameplate 1.70
        /// (<c>ChickenNameplate</c>) → badges → floating combat text 2.05
        /// (<see cref="FloatingTextSpawnHeight"/>, rising to 3.15). That leaves a 0.35
        /// gap for the badges; this constant plus <see cref="StatusBadgeRowSpacing"/> and
        /// <see cref="StatusBadgeMaxRows"/> is the invariant that keeps the stack inside
        /// it (1.78 + 0.11 × 2 = 2.00, clearing the floating-text floor). The badges are
        /// authored deliberately smaller than the nameplate so three rows fit the gap.
        /// </summary>
        public const float StatusBadgeStackBaseHeight = 1.78f;

        /// <summary>Vertical spacing between status badge rows. See
        /// <see cref="StatusBadgeStackBaseHeight"/> for the budget this has to fit.</summary>
        public const float StatusBadgeRowSpacing = 0.11f;

        /// <summary>
        /// Maximum simultaneous status badges. Three, because stun / root / slow are
        /// independent bits on <c>ChickenController.ControlFlags</c> and can all be set at
        /// once — the ladder in <c>CurrentControlState</c> picks a single *dominant*
        /// state, but §5.1 asks for "one small icon per active status," not per dominant
        /// state. See <see cref="StatusBadgeStackBaseHeight"/> for why three is also the
        /// hard ceiling the layout can afford.
        /// </summary>
        public const int StatusBadgeMaxRows = 3;

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
        // OtherActive is the genuinely new tier.

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
        /// Layer 2–4 alpha for "no valid target in range". Higher than
        /// <see cref="HexAlphaCooldown"/> on purpose: this state is *ready to fire the
        /// moment someone walks into range*, so it must stay visibly brighter than a slot
        /// the player simply cannot use yet. Its distinguishing channel is hue, not alpha
        /// — the fill switches to <see cref="NeutralNoEffectColor"/> (§1.3: never colour
        /// alone, so it also gets the layer-9 ⃠ mark).
        /// </summary>
        public const float HexAlphaNoTarget = 0.70f;

        /// <summary>
        /// Layer 2–4 alpha while a *different* ability is mid-duration. ADDED for Stage 5.
        /// The dimmest tier in the table, and deliberately dimmer than
        /// <see cref="HexAlphaCooldown"/>: cooldown is a per-slot condition the player is
        /// used to waiting out, whereas "another ability is running" suppresses the whole
        /// cluster at once, and if the two sat at the same alpha a suppressed cluster
        /// would read as "everything went on cooldown simultaneously" — the wrong story.
        /// Pushing it to 0.35 makes the cluster recede as a group, which is what leaves
        /// the one slot carrying the top-down accent drain (the ability that is actually
        /// running) as the only bright thing on screen.
        /// </summary>
        public const float HexAlphaOtherActive = 0.35f;

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
        // Colours — canonical control-state set
        // ================================================================
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
        // P2 Ocean Blue #1A7FC4 and P4 Forest Teal #0D9E7A. A raw Okabe-Ito palette
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
        public static readonly Color CanonicalStunColor = new Color(1.00f, 0.85f, 0.20f, 1f);

        /// <summary>Canonical root colour. Matches <c>ControlStateVFX.RootColor</c>;
        /// diverges from <c>ChickenStateOverlays.RootColor</c> (0.32, 0.72, 0.28) —
        /// see the delete/replace instruction in the report.</summary>
        public static readonly Color CanonicalRootColor = new Color(0.35f, 0.90f, 0.25f, 1f);

        /// <summary>Canonical slow colour. Matches <c>ControlStateVFX.SlowColor</c>;
        /// diverges from <c>ChickenStateOverlays.SlowColor</c> (0.35, 0.62, 1.0) —
        /// see the delete/replace instruction in the report.</summary>
        public static readonly Color CanonicalSlowColor = new Color(0.30f, 0.75f, 1.00f, 1f);

        /// <summary>Canonical knockback shockwave colour — already consistent
        /// (white) wherever it appears; reproduced for completeness.</summary>
        public static readonly Color CanonicalKnockColor = new Color(1f, 1f, 1f, 1f);

        // ---- Threat-overlay colours (§2.2) --------------------------------
        //
        // "Valid target" intentionally has no colour constant here: per §2.2 it
        // uses the *casting ability's own* AccentColor (already authored per
        // AbilityBaseSO, docs/ART.md's Ability Accent Colors table) so the preview
        // bracket always matches the ground decal it belongs to. Inventing a
        // second "valid target" colour here would just be a second thing to keep
        // in sync with the ability data.

        /// <summary>
        /// Shared neutral-grey used for every "nothing will happen here" case:
        /// immune/no-effect targets inside the telegraph (§2.2 case 4) AND the "no
        /// valid target in range" refusal state (§6 case 25). The spec already
        /// describes both with the same visual language (grey + dashed/⃠ glyph),
        /// so one colour serves both rather than two greys a player would have to
        /// learn apart. Kept light enough (not near-black) to read clearly against
        /// the dark arena background (<c>MatchCamera</c>'s solid clear colour is
        /// ≈(0.10, 0.12, 0.16)).
        /// </summary>
        public static readonly Color NeutralNoEffectColor = new Color(0.58f, 0.58f, 0.62f, 0.85f);

        /// <summary>
        /// Desaturated red-grey wash for a hold that has gone illegal mid-cast
        /// (§2.5 — "desaturates to red-grey"). Kept distinct from
        /// <see cref="NeutralNoEffectColor"/> (pure neutral) with a deliberate red
        /// bias, so "this specific target stopped being valid" (the illegal-wash
        /// colour) doesn't visually collide with "there was never a valid target
        /// here" (the neutral-grey colour) — two different refusal stories that
        /// should not look identical.
        /// </summary>
        public static readonly Color IllegalCastTintColor = new Color(0.55f, 0.30f, 0.28f, 0.80f);

        /// <summary>Alpha for a solid "valid target" bracket at rest (before the
        /// pulse in <see cref="ValidTargetPulseHz"/> is applied). 0.9 — near-opaque
        /// so "this one will be hit" reads with confidence, distinct from the
        /// softer 0.55–0.85 alphas used on the neutral/illegal states above.</summary>
        public const float ValidTargetBracketAlpha = 0.9f;

        /// <summary>
        /// Pulse rate for the valid-target bracket while a hold is active. 2.5 Hz —
        /// fast enough to read as "live/armed," slower than
        /// <see cref="ControlStateVfxPulseHzReference"/> (the existing 9 rad/s ≈
        /// 1.4 Hz status-ring pulse, see below) would suggest is "urgent," since a
        /// valid-target mark is informative, not alarming — it shouldn't compete
        /// visually with an actual active stun/root/slow ring pulsing nearby on a
        /// different chicken.
        /// </summary>
        public const float ValidTargetPulseHz = 2.5f;

        /// <summary>
        /// Reference only, not a new tunable: <c>ControlStateVFX.UpdateStatusRing</c>
        /// pulses via <c>Sin(Time.time * 9f)</c>, i.e. 9 rad/s ≈ 1.43 Hz. Recorded
        /// here so <see cref="ValidTargetPulseHz"/>'s "distinct from the status
        /// ring pulse" rationale has the actual number next to it instead of an
        /// unverifiable claim.
        /// </summary>
        public const float ControlStateVfxPulseHzReference = 9f / (2f * Mathf.PI);
    }
}
